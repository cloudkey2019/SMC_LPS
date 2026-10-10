/**
 * APS V1 4号位前端 — 公共类型与枚举
 *
 * 依据：《APS_V1_4号位页面与业务操作开发实施包_v1.0_20260814.md》
 *  - 文档第 6 节：Task 正式状态仅 5 种，禁止 SCHEDULED/PAUSED/SUSPENDED/WAITING/RUNNING
 *  - 文档第 15 节：PlanVersion 状态机（ACTIVE/BUILDING/CANDIDATE/FAILED/ARCHIVED）
 *  - 文档第 19 节：DTO 设计原则（后端契约 → 前端 TS）
 *
 * 设计要点：
 *  - 后端 PascalCase 字段映射到前端 camelCase（API 层做映射）
 *  - 日期统一 ISO 字符串（前端不做时区运算，UI 层 dayjs 转换）
 *  - 枚举使用字符串联合类型，编译期校验，避免魔法字符串
 *  - 占位 DTO 字段标注 `@see 4号位文档第 X 节`，等 3 号位落地后对齐
 */

/**
 * 后端 ApiResponse 包装（与 LPS.APS.Shared/Models/ApiResponse.cs 一致）
 *
 * 成功示例：{ code: 200, message: 'success', data: {...}, traceId: '...', timestamp: '...' }
 * 失败示例：{ code: 400, message: '...', data: null,  traceId: '...', timestamp: '...' }
 *
 * 业务码：200/400/401/403/500；前端按 code === 200 判定成功
 */
export interface ApiResponse<T> {
  code: number
  message: string
  data: T | null
  traceId?: string
  timestamp: string
}

/** 与后端约定：HTTP 200 / 201 都表示业务成功
 *  - 200 OK（多数端点）
 *  - 201 Created（GovernanceController 的 POST CreatedAtAction 路径）
 *  - 3 号位是否统一包装见 [frontNew/docs/3号位契约-依赖与扩展项.md] §八.③
 */
export const APS_SUCCESS_CODE = 200
export const APS_SUCCESS_CODES = [200, 201] as const
export const isApsSuccessCode = (code: number): boolean =>
  (APS_SUCCESS_CODES as readonly number[]).includes(code)

/** 业务码常量 */
export const APS_ERROR_CODE = {
  BAD_REQUEST: 400,
  UNAUTHORIZED: 401,
  FORBIDDEN: 403,
  NOT_FOUND: 404,
  INTERNAL: 500
} as const

/** 分页请求 */
export interface PageQuery {
  pageIndex: number
  pageSize: number
  /** 模糊匹配关键字 */
  keyword?: string
  /** 排序字段（如 "createdAt desc"） */
  sort?: string
}

/** 分页响应 */
export interface PageResult<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

/**
 * 4号位文档第 6 节：Task 正式状态仅 5 种
 * 禁止 SCHEDULED / PAUSED / SUSPENDED / WAITING / RUNNING
 *
 *  - PLANNED      已排产，等待释放
 *  - RELEASED     已释放给下游（MES 资格中）
 *  - IN_PROGRESS  执行中（实际开工）
 *  - COMPLETED    已完成
 *  - CANCELLED    已取消
 */
export const TASK_STATUSES = [
  'PLANNED',
  'RELEASED',
  'IN_PROGRESS',
  'COMPLETED',
  'CANCELLED'
] as const

export type TaskStatus = (typeof TASK_STATUSES)[number]

/** UI 层用：状态中文标签 */
export const TASK_STATUS_LABELS: Record<TaskStatus, string> = {
  PLANNED: '已排产',
  RELEASED: '已释放',
  IN_PROGRESS: '执行中',
  COMPLETED: '已完成',
  CANCELLED: '已取消'
}

/**
 * 4号位文档第 15 节：PlanVersion 状态机
 *
 *  - BUILDING    排程构建中
 *  - ACTIVE      当前生效版本（PMC 只读这个）
 *  - CANDIDATE   候选版本（PMC 可对比 + 最小人工确认）
 *  - FAILED      构建失败
 *  - ARCHIVED    已归档
 *
 * 注：CTP / INSERT_IMPACT_ANALYSIS 永远不能成为 PlanVersion（验收 U11/U12），
 *     因此枚举里也不包含这两种业务用途。
 */
export const PLAN_VERSION_STATUSES = [
  'BUILDING',
  'ACTIVE',
  'CANDIDATE',
  'FAILED',
  'ARCHIVED'
] as const

export type PlanVersionStatus = (typeof PLAN_VERSION_STATUSES)[number]

/** UI 层用：PlanVersion 状态中文标签（4号位文档第 15 节状态机） */
export const PLAN_VERSION_STATUS_LABELS: Record<PlanVersionStatus, string> = {
  BUILDING: '构建中',
  ACTIVE: '生效中',
  CANDIDATE: '候选',
  FAILED: '构建失败',
  ARCHIVED: '已归档'
}

/** 4号位文档第 6 节：Task 不可移动标识（UI 显示，不允许拖动改库） */
export type TaskLockMarker = 'EXECUTION' | 'FIRM' | 'FROZEN'

/** 4号位文档第 4 节：订单摘要状态（排产总览用） */
export const ORDER_SUMMARY_STATUSES = [
  'ON_TIME',
  'DELAYED',
  'AT_RISK',
  'UNSCHEDULED',
  'ESTIMATED_ONLY'
] as const

export type OrderSummaryStatus = (typeof ORDER_SUMMARY_STATUSES)[number]

export const ORDER_SUMMARY_STATUS_LABELS: Record<OrderSummaryStatus, string> = {
  ON_TIME: '按期',
  DELAYED: '延期',
  AT_RISK: '风险',
  UNSCHEDULED: '未排',
  ESTIMATED_ONLY: '仅估算'
}

/** 4号位文档第 13 节：Supply 来源类型 */
export const SUPPLY_TYPES = [
  'INVENTORY',
  'PI',
  'PO',
  'VMI',
  'ARRIVED_NOT_INBOUND',
  'INTERPLANT_TRANSIT',
  'RECEIVED',
  'PLANNED_PRODUCTION',
  'PLANNING_PURCHASE_PLACEHOLDER'
] as const

export type SupplyType = (typeof SUPPLY_TYPES)[number]

/** 4号位文档第 2.2 节：页面必须区分四类信息标签 */
export type FactType = 'FACT' | 'RESULT' | 'ESTIMATED' | 'RECOMMENDATION'

/** UI 层用：四类信息标签中文映射 */
export const FACT_TYPE_LABELS: Record<FactType, string> = {
  FACT: '事实',
  RESULT: '计算结果',
  ESTIMATED: '估算',
  RECOMMENDATION: '建议'
}

/** 4号位文档第 13 节：PI PositionType */
export const PI_POSITION_TYPES = [
  'ERP_REMAINING',
  'PRODUCTION',
  'TRANSIT',
  'WAITING',
  'UNLOCATED',
  'CROSS_STAGE'
] as const
export type PiPositionType = (typeof PI_POSITION_TYPES)[number]

/**
 * 4号位文档第 14 节 + v1.4 §二十：RuleSet / ParameterSet 6 态状态机
 *
 *  - DRAFT       草稿（可编辑）
 *  - SUBMITTED   已提交（待审批流转中间态，只读）
 *  - APPROVED    已审批（待发布流转中间态，只读）
 *  - PUBLISHED   已发布（生效中）
 *  - DISABLED    已停用（含原 RETIRED 归一；前端 DTO 兼容层处理）
 *  - ARCHIVED    已归档（历史快照，只读）
 *
 * 历史：v1.0/v1.1/v1.2/v1.3 仅 3 态（DRAFT/PUBLISHED/RETIRED）；
 *       v1.4 §二十扩 6 态（冻结契约，3 号位 后端 GovernanceVersionStatus 已对齐）。
 */
export const RULE_STATUSES = [
  'DRAFT',
  'SUBMITTED',
  'APPROVED',
  'PUBLISHED',
  'DISABLED',
  'ARCHIVED'
] as const
export type RuleStatus = (typeof RULE_STATUSES)[number]

/** UI 层用：RuleSet / ParameterSet 状态中文标签（6 态） */
export const RULE_STATUS_LABELS: Record<RuleStatus, string> = {
  DRAFT: '草稿',
  SUBMITTED: '已提交',
  APPROVED: '已审批',
  PUBLISHED: '已发布',
  DISABLED: '已停用',
  ARCHIVED: '已归档'
}

/** UI 层用：RuleSet / ParameterSet 状态 Tag 颜色（Element Plus Tag type） */
export const RULE_STATUS_TAG: Record<
  RuleStatus,
  'success' | 'warning' | 'info' | 'primary' | 'danger'
> = {
  DRAFT: 'warning', // 黄 — 待编辑
  SUBMITTED: 'primary', // 蓝 — 中转
  APPROVED: 'info', // 灰蓝 — 中转
  PUBLISHED: 'success', // 绿 — 生效
  DISABLED: 'info', // 灰 — 失效
  ARCHIVED: 'info' // 灰 — 历史
}

/**
 * 兼容层：RETIRED → DISABLED 归一
 *
 * 后端 v1.4 已冻结 6 态不再返 RETIRED，但前端可能从：
 *  - mock fixture（v1.0-v1.3 数据）
 *  - 老 dev 环境（SQL 未迁移）
 *  - 后端字段返回大小写不一致
 * 拿到 RETIRED / retired / 未知态字符串。在 store receive 处 normalize 后再使用。
 */
export const normalizeRuleStatus = (raw: string | null | undefined): RuleStatus => {
  if (!raw) return 'DRAFT'
  const upper = String(raw).toUpperCase()
  if (upper === 'RETIRED') return 'DISABLED'
  if ((RULE_STATUSES as readonly string[]).includes(upper)) return upper as RuleStatus
  // 未知态 → DISABLED 兜底（不抛错，避免页面打不开）

  console.warn(`[RuleStatus] Unknown status from backend: ${raw}, falling back to DISABLED`)
  return 'DISABLED'
}

/**
 * 4号位 B 设计稿 §3.4 fork 状态矩阵
 * 决定源 RuleSet 处于某状态时，[+ 新建草稿] 按钮是否点亮
 *
 *  - PUBLISHED  ✅ 主用例 + v1.4 §10 文档要求
 *  - DISABLED   ✅ 重启治理中（已退役但未归档）
 *  - DRAFT      ❌ 已有"当前编辑"，再建会冲突（提示去编辑现有草稿）
 *  - SUBMITTED  ❌ 流程中，改需先撤回
 *  - APPROVED   ❌ 已审批就等发布，不应分叉
 *  - ARCHIVED   ❌ 归档文件不修改
 *  - 未知态      ❌ 兜底禁掉
 */
export const canCreateDraftForStatus = (
  sourceStatus: RuleStatus
): { allowed: boolean; reason?: string } => {
  if (sourceStatus === 'DRAFT') {
    return { allowed: false, reason: '当前 RuleSet 已有 DRAFT；请直接编辑现有草稿' }
  }
  if (sourceStatus === 'PUBLISHED') {
    return { allowed: true }
  }
  if (sourceStatus === 'DISABLED') {
    return {
      allowed: true,
      reason: '基于已停用 RuleSet fork；新 DRAFT 将重新进入 PUBLISHED 流程'
    }
  }
  return {
    allowed: false,
    reason: `当前状态 ${RULE_STATUS_LABELS[sourceStatus] ?? sourceStatus} 不允许 fork`
  }
}

/** 4号位文档第 17 节：人工操作审计 ObjectType */
export type AuditObjectType =
  | 'CANDIDATE'
  | 'DEMAND_PROTECTION'
  | 'RULE_VERSION'
  | 'PARAMETER_VERSION'
  | 'MANUAL_RESCHEDULE'
  | 'MES_DISPATCH'

export type AuditAction = 'CONFIRM' | 'RELEASE' | 'PUBLISH' | 'RETIRE' | 'TRIGGER' | 'DISPATCH'

/**
 * 4号位 RBAC — 系统角色码（与后端 DDL v1.3 严格一致，lps/LPS.APS.Engine/Repositories/Auth/RoleRepository.cs）
 *
 * 历史：v1.0/v1.1 用 VIEWER/PMC/RULE_ADMIN/RULE_PUBLISHER/SYSTEM_ADMIN 5 个 mock 别名；
 *       v1.2 §23.1 后端落地 7 个 DDL 系统角色码（aps.* 前缀），前端对齐切换。
 *
 * 7 角色分布（前端可见的 7 个 DDL 角色码，dev seed 已写 4 个）：
 *  - aps.admin.system            系统管理员（持全部 34 权限码 + Global scope）
 *  - aps.admin.aps               规则/参数/策略管理员（v1.3 合并 RULE_ADMIN + RULE_PUBLISHER —
 *                                §二十五.3「RULE_ADMIN 不能自动拥有发布权」不再由角色码表达，
 *                                改由权限码 aps.rule.publish 区分，后端二次校验同一码）
 *  - aps.planner                 PMC 计划员（17 码：plan/ctp/candidate/manual_eta/demand_protection.view 等）
 *  - aps.viewer.management       查看员（10 码：仅 *.view）
 *  - aps.supervisor.workshop     车间主管（dev 未 seed，mock 模式按 §二十五 场景分配）
 *  - aps.coordinator.material    物料协调（dev 未 seed，mock 模式按 §二十五 场景分配）
 *  - aps.service.api             API 服务账号（dev 未 seed，无前端用户）
 */
export const ROLE_KEYS = [
  'aps.admin.system',
  'aps.admin.aps',
  'aps.planner',
  'aps.viewer.management',
  'aps.supervisor.workshop',
  'aps.coordinator.material',
  'aps.service.api'
] as const
export type RoleKey = (typeof ROLE_KEYS)[number]

/** 角色中文标签（前端 UI 用；非安全相关，纯展示） */
export const ROLE_LABELS: Record<RoleKey, string> = {
  'aps.admin.system': '系统管理员',
  'aps.admin.aps': '规则/参数管理员',
  'aps.planner': 'PMC 计划员',
  'aps.viewer.management': '查看员',
  'aps.supervisor.workshop': '车间主管',
  'aps.coordinator.material': '物料协调',
  'aps.service.api': 'API 服务账号'
}

/** 角色码 → 中文标签（未知角色码原样返回，供「当前操作者」等展示处统一使用） */
export const roleLabelOf = (code: string): string => ROLE_LABELS[code as RoleKey] ?? code

/** Domain 标识（每个产品族对应一个 Domain） */
export type DomainKey = string

/** ISO8601 字符串（前端不解析，统一 dayjs 处理） */
export type IsoDateTime = string
export type IsoDate = string

/**
 * 4号位文档第 7 节：CTP 必须明确区分 Estimated 与确定
 * 凡是依赖 PLANNING_PURCHASE_PLACEHOLDER 的供应，UI 必须显示该徽章
 */
export const ESTIMATED_BADGE_TEXT = '估算日期，采购尚无正式承诺'

/**
 * 4号位文档第 16 节：MES 下发资格判定结果
 *  UNKNOWN 后端未判定 / ELIGIBLE 可下发 / INELIGIBLE 不可下发
 */
export type MesEligibility = 'UNKNOWN' | 'ELIGIBLE' | 'INELIGIBLE'
