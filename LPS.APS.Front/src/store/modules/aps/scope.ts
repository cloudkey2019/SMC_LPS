/**
 * APS V1 4号位 — 业务范围权限（Scope）助手
 *
 * @see 审核报告 v1.1 §十七.17.2（Factory / ProductFamily / Department / Domain）
 * @see 审核报告 v1.1 §二十五.7（业务范围权限证据清单）
 *
 * 设计要点：
 *  - **空数组 = 不限制（all-access）**；非空数组 = 受限白名单
 *  - 任一维度的"空"维度按"未限制"判定，便于渐进接入新维度
 *  - 前端 Scope 防御：mock 模式可演示 7 项权限场景；真接口模式由 3号位服务端二次校验
 *  - 不重写项目原 useUserStore；这里只关心 APS 业务范围的派生计算
 */

import type { RoleKey } from '@/api/aps-v1'
import type { DomainKey } from '@/api/aps-v1'

/**
 * 业务范围（4 维度；任一可选，缺失 = 未限制）
 *
 * 与 3号位 `UserInfo.dataScope` 字段对齐；
 * 字段为空数组 → 该维度未限制；非空 → 受限白名单。
 */
export interface BusinessScope {
  /** 工厂代码；空数组 = 未限制 */
  factoryCodes: string[]
  /** 产品族代码；空数组 = 未限制 */
  productFamilyCodes: string[]
  /** 部门 / 生产部门代码；空数组 = 未限制 */
  departmentCodes: string[]
  /** 计划域；空数组 = 未限制 */
  domainKeys: DomainKey[]
}

/**
 * Scope 越界异常（写操作前置断言；UI 弹错 + 后端 403 兜底）
 */
export class ScopeViolationError extends Error {
  readonly scope: BusinessScope
  readonly target: ScopeTarget
  readonly field: keyof BusinessScope
  readonly actual: string
  readonly allowed: string[]

  constructor(args: {
    scope: BusinessScope
    target: ScopeTarget
    field: keyof BusinessScope
    actual: string
    reason?: string
  }) {
    const { scope, target, field, actual, reason } = args
    const allowed = scope[field]
    super(
      `业务范围越界（${field}）` +
        `：当前用户 ${field}=${JSON.stringify(scope[field])}，` +
        `目标 ${field}=${actual}` +
        (target.domainKey ? `，domainKey=${target.domainKey}` : '') +
        (reason ? `，${reason}` : '')
    )
    this.name = 'ScopeViolationError'
    this.scope = scope
    this.target = target
    this.field = field
    this.actual = actual
    this.allowed = allowed
  }
}

/** 写操作目标的最小范围描述（可只包含参与判定的字段） */
export interface ScopeTarget {
  factoryCode?: string
  productFamilyCode?: string
  departmentCode?: string
  domainKey?: DomainKey
}

/**
 * 判定"target 是否在 scope 内"
 *
 *  - 任一维度有目标值 → 该维度必须在白名单内（或 scope 该维度未限制）
 *  - 全部维度通过 → 返回 `{ ok: true }`；否则返回 `{ ok: false, field, actual }`
 *
 * 示例：
 *  - scope = { factoryCodes: ['F-SUZ-01'], productFamilyCodes: [], ... }
 *  - target = { factoryCode: 'F-SUZ-01', domainKey: 'FAMILY_INJECTION' }
 *  - factoryCodes 命中白名单；domainKeys 空（未限制） → ok
 */
export function checkScope(
  scope: BusinessScope,
  target: ScopeTarget
): { ok: true } | { ok: false; field: keyof BusinessScope; actual: string } {
  if (target.factoryCode !== undefined) {
    const allowed = scope.factoryCodes
    if (allowed.length > 0 && !allowed.includes(target.factoryCode)) {
      return { ok: false, field: 'factoryCodes', actual: target.factoryCode }
    }
  }
  if (target.productFamilyCode !== undefined) {
    const allowed = scope.productFamilyCodes
    if (allowed.length > 0 && !allowed.includes(target.productFamilyCode)) {
      return { ok: false, field: 'productFamilyCodes', actual: target.productFamilyCode }
    }
  }
  if (target.departmentCode !== undefined) {
    const allowed = scope.departmentCodes
    if (allowed.length > 0 && !allowed.includes(target.departmentCode)) {
      return { ok: false, field: 'departmentCodes', actual: target.departmentCode }
    }
  }
  if (target.domainKey !== undefined) {
    const allowed = scope.domainKeys
    if (allowed.length > 0 && !allowed.includes(target.domainKey)) {
      return { ok: false, field: 'domainKeys', actual: target.domainKey }
    }
  }
  return { ok: true }
}

/**
 * 写操作前置断言：scope 越界 → 抛 ScopeViolationError
 *
 * 用于 store action 入口：
 * ```ts
 * async function confirm(input) {
 *   assertScope(scope, { domainKey: input.domainKey })
 *   ...
 * }
 * ```
 */
export function assertScope(scope: BusinessScope, target: ScopeTarget): void {
  const result = checkScope(scope, target)
  if (!result.ok) {
    throw new ScopeViolationError({
      scope,
      target,
      field: result.field,
      actual: result.actual,
      reason: '越权访问（前端防御，3号位后端会二次校验）'
    })
  }
}

/**
 * 列表预过滤：剔除越权条目（前端防御；后端是权威）
 *
 * @param items 候选条目
 * @param scope 当前用户业务范围
 * @param fieldMap 条目 → ScopeTarget 映射（如 `r => ({ domainKey: r.domainKey })`）
 */
export function filterByScope<T>(
  items: T[],
  scope: BusinessScope,
  fieldMap: (item: T) => ScopeTarget
): T[] {
  return items.filter((it) => {
    const target = fieldMap(it)
    // target 字段全部 undefined → 不过滤（让 store 自身处理"全空"场景）
    const hasAny =
      target.factoryCode !== undefined ||
      target.productFamilyCode !== undefined ||
      target.departmentCode !== undefined ||
      target.domainKey !== undefined
    if (!hasAny) return true
    return checkScope(scope, target).ok
  })
}

/**
 * U41：scope 维度筛选下拉的「可选项」收敛
 *
 * 冻结 v1.4 L1463：Scope 筛选器**不允许选择未授权 Factory/ProductFamily/Department/Domain**。
 * 语义：
 *  - allowed 为空数组（未限制 / isGlobal）→ 返回 baseline 全量（不收敛）
 *  - allowed 非空 → 选项 = allowed 白名单本身（用 baseline 提供中文 label，未覆盖的码 fallback label=code）
 *    即「下拉选项 = token scope 内」：既不暴露未授权项，也不漏掉授权项。
 *
 * 与 filterByScope 的区别：filterByScope 过滤「已取数据行」；本函数收敛「可选筛选项」。
 * 二者均为前端纵深防御（§23.3 后端为最终安全边界）。
 */
export function constrainOptionsByScope(
  baseline: ReadonlyArray<{ value: string; label: string }>,
  allowed: readonly string[] | undefined
): Array<{ value: string; label: string }> {
  if (!allowed || allowed.length === 0) return baseline.map((o) => ({ ...o }))
  const labelOf = new Map(baseline.map((o) => [o.value, o.label]))
  return allowed.map((code) => ({ value: code, label: labelOf.get(code) ?? code }))
}

/* ==================== Mock 角色预设（用于 P1-14/15 7 项场景演示） ==================== */

/**
 * Mock 模式角色 → 业务范围预设（v1.2 DDL 角色码版）
 *
 * 字段含义：
 *  - 空数组 = "未限制"（all-access）
 *  - 非空 = 受限白名单
 *
 * 角色 → 典型场景：
 *  - aps.viewer.management  单域只读（演示 §二十五.1, .2）
 *  - aps.planner            工厂+产品族+部门+域均受限（演示 §二十五.4, .5, .6, .7）
 *  - aps.admin.aps          多工厂+多域管理（v1.2 合并 RULE_ADMIN + RULE_PUBLISHER；
 *                            §二十五.3 不再由角色码表达，改由 aps.rule.publish 权限码区分）
 *  - aps.admin.system       全开（空数组 = isGlobal）
 *  - aps.supervisor.workshop / aps.coordinator.material / aps.service.api
 *                          dev 未 seed 的 3 个 DDL 角色，mock 模式按对应场景分配
 */
export const MOCK_ROLE_SCOPE_PRESETS: Record<RoleKey, BusinessScope> = {
  'aps.viewer.management': {
    factoryCodes: ['F-SUZ-01'],
    productFamilyCodes: ['INJECTION'],
    departmentCodes: [],
    domainKeys: ['FAMILY_INJECTION']
  },
  'aps.planner': {
    factoryCodes: ['F-SUZ-01'],
    productFamilyCodes: ['INJECTION'],
    departmentCodes: ['INJECTION_DEPT'],
    domainKeys: ['FAMILY_INJECTION']
  },
  'aps.admin.aps': {
    factoryCodes: ['F-SUZ-01', 'F-BJ-01'],
    productFamilyCodes: ['INJECTION', 'ASSEMBLY'],
    departmentCodes: ['MACHINING_DEPT'],
    domainKeys: ['FAMILY_INJECTION', 'FAMILY_ASSEMBLY', 'BJ_FAMILY_INJECTION']
  },
  'aps.admin.system': {
    factoryCodes: [],
    productFamilyCodes: [],
    departmentCodes: [],
    domainKeys: []
  },
  'aps.supervisor.workshop': {
    factoryCodes: ['F-SUZ-01'],
    productFamilyCodes: ['INJECTION'],
    departmentCodes: ['INJECTION_DEPT'],
    domainKeys: ['FAMILY_INJECTION']
  },
  'aps.coordinator.material': {
    factoryCodes: ['F-SUZ-01', 'F-BJ-01'],
    productFamilyCodes: ['INJECTION', 'ASSEMBLY'],
    departmentCodes: [],
    domainKeys: ['FAMILY_INJECTION', 'FAMILY_ASSEMBLY']
  },
  'aps.service.api': {
    factoryCodes: [],
    productFamilyCodes: [],
    departmentCodes: [],
    domainKeys: []
  }
}

/**
 * 兼容旧 dataScope（仅 factory + productFamily）
 *
 * 历史 UserInfo.dataScope 没有 departmentCodes / domainKeys；
 * 这里补 undefined → 转空数组（视为"未限制"），保证 mock 切换不报错。
 */
export function normalizeScope(input: Partial<BusinessScope> | undefined | null): BusinessScope {
  return {
    factoryCodes: input?.factoryCodes ?? [],
    productFamilyCodes: input?.productFamilyCodes ?? [],
    departmentCodes: input?.departmentCodes ?? [],
    domainKeys: (input?.domainKeys ?? []) as DomainKey[]
  }
}
