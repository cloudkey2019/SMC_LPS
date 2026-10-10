/**
 * APS V1 4号位 — Setup 换型规则 DTO（v1.5 Setup 专项）
 *
 * @see 冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.5_20260916_Setup换型规则专项冻结对齐版.md
 *  - §3.2 EXACT 输入字段（7 元组）
 *  - §4.3 DEFAULT 输入字段（5 元组）
 *  - §5.2/§5.3 0 分钟兜底统计查询/输出
 *  - §6.2/§6.3 版本 Diff 列表字段 + setupRuleChanges
 *  - §7.1/§7.2 数据结构红线 + 业务红线
 *  - §8.2 SetupRule 实体必加字段
 *  - §11.1 13 端点契约
 *
 * 设计要点：
 *  - 字段名用 camelCase（与后端 PascalCase 通过 API 层映射对齐）
 *  - RuleType 用大写字符串字面量严格对齐后端 DDL CK 校验（'EXACT' | 'DEFAULT'）
 *  - Status 用大写字符串字面量（'DRAFT' | 'ACTIVE' | 'DEPRECATED'）
 *  - EXACT 7 元组：Department/Stage/Operation/Resource/FromMaterial/ToMaterial + RuleSetVersionId
 *  - DEFAULT 5 元组：Department/Stage/Operation/Resource + RuleSetVersionId
 *  - 红线：同产品连续（FromMaterial == ToMaterial）= 0 分钟，不存规则
 *  - 红线：SetupMinutes 必须 > 0（EXACT/DEFAULT 规则）
 *  - 红线：EXACT 必须填 FromMaterial/ToMaterial；DEFAULT 必须都空
 *
 * 09-20 3号位 交付回执偏差适配（APS_V1_Setup维护API_3号位致4号位_交付回执_v1.0_20260920.md）：
 *  - 偏差 #1：remark 字段撤除（用户口径终定不返回；DTO/Input 均不再含 remark）
 *  - 偏差 #5：eligibility 的 materialId = Material.Id（内部主键非 Code）；
 *    SetupRuleDto 按 v1.1 §3 双返回机制补 fromMaterialId/toMaterialId（字段名待回执确认）
 *  - 偏差 #6：status 派生三态 DRAFT/ACTIVE/DEPRECATED（与本文件既有三态一致，无改动）
 *
 * 09-20 lps 代码同步核实（SetupRuleDtos.cs 权威契约源）——Id-口径终定（v1.1）：
 *  - 写输入（ExactInput/DefaultInput）：部门/设备/物料一律传内部主键 Id
 *    （productionDepartmentId / resourceId / fromMaterialId / toMaterialId），大工艺/工序传 Code
 *  - 读模型（SetupRuleDto）双返回：Id + Code 同回（departmentCode/resourceCode/fromMaterialCode/
 *    toMaterialCode 可空回带），前端展示用 Code、编辑回填用 Id，零映射负担
 *  - 字段名核实：FromMaterialId/ToMaterialId（camelCase fromMaterialId/toMaterialId）与前端一致 ✅
 *  - #11 版本列表 Status = 治理六态原文（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED），
 *    区别于 SetupRuleDto.status 的派生三态
 *  - DEFAULT 入参带 [JsonExtensionData] 拦截：请求体出现含 "material" 的键 → 422
 *  - ⚠️ P0 缺口（已提请 3号位）：后端无主数据端点（部门/设备/物料 Code→Id），eligibility 响应
 *    仅回 ResourceCode 无 ResourceId → 真实模式 create 流程 Id 无来源（编辑流程靠双返回可用）
 */

import type { IsoDateTime } from './common'

/* ==================== 枚举与状态机 ==================== */

/** Setup 规则类型（与后端 DDL CK 字面量严格一致） */
export const SETUP_RULE_TYPES = ['EXACT', 'DEFAULT'] as const
export type SetupRuleType = (typeof SETUP_RULE_TYPES)[number]

/** Setup 规则状态（与后端 DDL CK 字面量严格一致） */
export const SETUP_RULE_STATUSES = ['DRAFT', 'ACTIVE', 'DEPRECATED'] as const
export type SetupRuleStatus = (typeof SETUP_RULE_STATUSES)[number]

/** UI 中文标签 + Tag 颜色映射 */
export interface SetupRuleStatusMeta {
  label: string
  tag: 'info' | 'primary' | 'success' | 'warning' | 'danger'
  effect?: 'light' | 'dark' | 'plain'
}

export const SETUP_RULE_STATUS_META: Record<SetupRuleStatus, SetupRuleStatusMeta> = {
  DRAFT: { label: '草稿', tag: 'warning' },
  ACTIVE: { label: '生效', tag: 'success' },
  DEPRECATED: { label: '已废弃', tag: 'info', effect: 'plain' }
}

/**
 * RuleSetVersion 治理六态原文（09-20 lps 核实：SetupRuleSetVersionDto.Status 为六态，
 * 区别于 SetupRuleDto.status 派生三态；#11 列表 / #12 diff.publishStatus 均回六态）
 */
export const SETUP_VERSION_STATUSES = [
  'DRAFT',
  'SUBMITTED',
  'APPROVED',
  'PUBLISHED',
  'DISABLED',
  'ARCHIVED'
] as const
export type SetupVersionStatus = (typeof SETUP_VERSION_STATUSES)[number]

/** 六态 UI 标签映射（版本选择器 / Diff publishStatus 展示用） */
export const SETUP_VERSION_STATUS_META: Record<
  SetupVersionStatus,
  { label: string; tag: 'info' | 'primary' | 'success' | 'warning' | 'danger' }
> = {
  DRAFT: { label: '草稿', tag: 'warning' },
  SUBMITTED: { label: '已提交', tag: 'primary' },
  APPROVED: { label: '已批准', tag: 'primary' },
  PUBLISHED: { label: '已发布', tag: 'success' },
  DISABLED: { label: '已停用', tag: 'info' },
  ARCHIVED: { label: '已归档', tag: 'info' }
}

/**
 * Setup 来源枚举（§9 命中优先级，前端只展示不计算）
 * 5 值对齐 1号位 实际产出（v1.5 SetupSource 输出链回执 2026-09-21）：
 *   - INITIAL: 无上一产品（班头首单/冷启动）→ 0 分钟，普通/灰 Tag，**非数据质量问题**
 *   - 0号位 Q4 裁决：INITIAL 与 NONE 严格分开，不得合并
 *   - 缺 INITIAL 会把正常班头首单误标红 Tag「无规则兜底」，误导车间
 */
export const SETUP_SOURCES = ['EXACT', 'DEFAULT', 'NONE', 'SAME_PRODUCT', 'INITIAL'] as const
export type SetupSource = (typeof SETUP_SOURCES)[number]

export const SETUP_SOURCE_META: Record<
  SetupSource,
  { label: string; tag: 'info' | 'success' | 'warning' | 'danger' }
> = {
  EXACT: { label: '明确转换规则（EXACT）', tag: 'success' },
  DEFAULT: { label: '默认换型规则（DEFAULT）', tag: 'info' },
  NONE: { label: '无规则兜底（0 分钟）', tag: 'danger' },
  SAME_PRODUCT: { label: '同产品连续（0 分钟）', tag: 'warning' },
  INITIAL: { label: '冷启动首单（0 分钟）', tag: 'info' }
}

/* ==================== 主实体 SetupRule（§8.2） ==================== */

/**
 * Setup 规则实体（对齐后端 SetupRule 表 §8.2）
 *
 * 唯一性约束（§8.3）：
 *  - EXACT: (RuleSetVersionId, DepartmentCode, StageCode, OperationCode, ResourceCode, FromMaterialCode, ToMaterialCode)
 *  - DEFAULT: (RuleSetVersionId, DepartmentCode, StageCode, OperationCode, ResourceCode)
 *
 * 业务约束（CHECK）：
 *  - RuleType = 'EXACT' → FromMaterialCode IS NOT NULL AND ToMaterialCode IS NOT NULL
 *  - RuleType = 'DEFAULT' → FromMaterialCode IS NULL AND ToMaterialCode IS NULL
 *  - FromMaterialCode != ToMaterialCode（同产品连续 = 0 分钟，不存规则）
 *  - SetupMinutes > 0
 */
export interface SetupRuleDto {
  id: number
  ruleSetVersionId: number
  ruleType: SetupRuleType
  /** 生产部门主键（Id-口径 v1.1；编辑回填/提交用） */
  productionDepartmentId: number
  /** 生产部门编码（双返回回带，展示用；后端 string? 可空） */
  departmentCode?: string | null
  stageCode: string
  /** 唯一工序维度（v1.2 收口版废止"前小工序 + 后小工序"） */
  operationCode: string
  /** 当前设备主键（Id-口径 v1.1；编辑回填/提交用） */
  resourceId: number
  /** 当前设备编码（双返回回带，展示用；后端 string? 可空） */
  resourceCode?: string | null
  /** EXACT 必填 / DEFAULT 必空（双返回回带，展示用） */
  fromMaterialCode?: string | null
  /** EXACT 必填 / DEFAULT 必空（双返回回带，展示用） */
  toMaterialCode?: string | null
  /** 前产品主键（Id-口径 v1.1 / 偏差 #5）：Material.Id，编辑回填 + 共同合法设备推荐回传用 */
  fromMaterialId?: number | null
  /** 后产品主键（Id-口径 v1.1 / 偏差 #5）：Material.Id */
  toMaterialId?: number | null
  /** 换型分钟数（> 0） */
  setupMinutes: number
  status: SetupRuleStatus
  createdAt: IsoDateTime
  createdBy?: string | null
  updatedAt?: IsoDateTime
  updatedBy?: string | null
}

/* ==================== EXACT 规则输入（§3.2） ==================== */

/**
 * EXACT 规则创建/更新输入（Id-口径 v1.1，对齐后端 SetupRuleExactInput）
 *
 * ⚠️ 写路径一律传内部主键 Id（部门/设备/物料），大工艺/工序传 Code：
 *  - 后端模型绑定按字段名精确匹配，传 *Code 会被静默丢弃 → Id 为 0 → 422 数据红线
 *  - Code→Id 映射来源：编辑态取 SetupRuleDto 双返回的 Id；新建态需主数据端点（P0 缺口，已提请 3号位）
 *
 * 红线（§7.1）：
 *  - FromMaterialId != ToMaterialId（同产品连续 = 0 分钟，不存规则）
 *  - SetupMinutes > 0
 *  - FromMaterialId/ToMaterialId 都必填（缺省/0 → 422）
 */
export interface SetupRuleExactInput {
  ruleSetVersionId: number
  /** 生产部门主键（Id） */
  productionDepartmentId: number
  stageCode: string
  operationCode: string
  /** 当前设备主键（Id） */
  resourceId: number
  /** 前产品主键（Material.Id） */
  fromMaterialId: number
  /** 后产品主键（Material.Id） */
  toMaterialId: number
  setupMinutes: number
}

/* ==================== DEFAULT 规则输入（§4.3） ==================== */

/**
 * DEFAULT 规则创建/更新输入（Id-口径 v1.1，对齐后端 SetupRuleDefaultInput）
 *
 * ⚠️ 写路径传 Id（部门/设备），大工艺/工序传 Code；
 * ⚠️ 后端 [JsonExtensionData] 拦截：请求体出现任何含 "material" 的键 → 422
 *   （DEFAULT 严禁携带产品字段，与 EXACT 互斥，§7.1 红线）——前端 payload 不得含 fromMaterial/toMaterial 任何键。
 *
 * 红线（§7.1）：SetupMinutes > 0
 */
export interface SetupRuleDefaultInput {
  ruleSetVersionId: number
  /** 生产部门主键（Id） */
  productionDepartmentId: number
  stageCode: string
  operationCode: string
  /** 当前设备主键（Id） */
  resourceId: number
  setupMinutes: number
}

/* ==================== 0 分钟兜底统计（§5） ==================== */

/** 0 分钟兜底统计查询参数（§5.2） */
export interface SetupUncoveredQuery {
  /** 选定的 Run（必填） */
  runId: number
  /** 按部门筛选（可选） */
  departmentCode?: string
  /** 按大工艺筛选（可选） */
  stageCode?: string
  /** 按设备筛选（可选） */
  resourceCode?: string
}

/** 0 分钟兜底统计输出行（§5.3） */
export interface SetupUncoveredStatDto {
  departmentCode: string
  stageCode: string
  operationCode: string
  resourceCode: string
  fromMaterialCode: string
  toMaterialCode: string
  /** 该转换对出现次数 */
  count: number
  /** 最近一次出现时间 */
  lastOccurrence?: IsoDateTime
}

/* ==================== 共同合法设备推荐（§3.3） ==================== */

/**
 * 共同合法设备推荐查询参数
 *
 * 算法定义（§7.2）：
 *  当前工序下前后产品的共同合法设备集合 =
 *    当前 Operation 合法资源 ∩ 同部门同 Stage 任一 Operation 合法资源
 */
export interface OperationResourceEligibilityQuery {
  operationCode: string
  /** 前产品 Material.Id（09-20 交付回执偏差 #5 终定：内部主键非 Code） */
  materialId: number
  /** 后产品 Material.Id（可选；不传 = 仅 from 物料合法设备） */
  toMaterialId?: number
}

/** 共同合法设备推荐响应 */
export interface OperationResourceEligibilityDto {
  operationCode: string
  /** 共同合法设备 Code 列表 */
  resourceCodes: string[]
  /** 可选：每个设备的详细信息（名称、部门、Stage 等） */
  resources?: Array<{
    resourceCode: string
    resourceName?: string
    departmentCode?: string
    stageCode?: string
  }>
}

/* ==================== 版本 Diff（§6） ==================== */

/** Setup 规则变更类型 */
export type SetupRuleChangeType = 'added' | 'modified' | 'removed'

/** Setup 规则变更行（§6.3 setupRuleChanges 数组元素） */
export interface SetupRuleChangeDto {
  ruleType: SetupRuleType
  /** added: 新规则；modified: 修改后；removed: 被删规则 */
  operationCode: string
  departmentCode?: string
  stageCode?: string
  resourceCode?: string
  fromMaterialCode?: string | null
  toMaterialCode?: string | null
  setupMinutes?: number
  /** modified 时含 before/after */
  id?: number
  before?: Partial<SetupRuleDto>
  after?: Partial<SetupRuleDto>
}

/** 版本 Diff 结果（§6.2 + §6.3） */
export interface SetupDiffDto {
  ruleSetVersionId: number
  otherVersionId: number
  /** 新增规则数（EXACT + DEFAULT） */
  addedCount: number
  /** 修改规则数 */
  modifiedCount: number
  /** 删除规则数 */
  removedCount: number
  /** 目标版本治理状态（09-20 lps 核实：六态原文，非派生三态） */
  publishStatus: SetupVersionStatus
  /** Setup 维度变更详情（3号位 需补此字段） */
  setupRuleChanges: {
    added: SetupRuleChangeDto[]
    modified: SetupRuleChangeDto[]
    removed: SetupRuleChangeDto[]
  }
  comparedAt?: IsoDateTime
}

/* ==================== RuleSetVersion 下拉源（§11.1 #11） ==================== */

/**
 * RuleSetVersion 简化 DTO（Setup 维护页 DRAFT 选择器用）
 *
 * 注：与 strategyProfile.ts 的 RuleSetVersionDto 隔离，避免跨模块耦合。
 * 端点：GET /api/governance/rule-set-versions（已有，复用）
 */
export interface SetupRuleSetVersionDto {
  id: number
  ruleSetId: number
  versionCode: string
  /** 版本治理六态原文（09-20 lps 核实；仅 DRAFT 可编辑 Setup 规则，§7.2 业务红线） */
  status: SetupVersionStatus
  effectiveFrom?: IsoDateTime | null
  effectiveTo?: IsoDateTime | null
  publishedAt?: IsoDateTime | null
  publishedBy?: string | null
  createdAt: IsoDateTime
}

/* ==================== Dialog 预填（Code 形态，页面 2.3 跳转用） ==================== */

/**
 * Setup Dialog 预填字段（§5.4 "去补 EXACT/DEFAULT" 路由 query 带入，Code 形态）
 *
 * 注：写输入已改 Id-口径（v1.1），但跳转源（0 分钟兜底统计行）只有 Code；
 * Dialog 内部做 Code→Id 解析（编辑态双返回 / mock 映射；真实模式待主数据端点，P0 缺口）。
 */
export interface SetupDialogPrefill {
  departmentCode?: string
  stageCode?: string
  operationCode?: string
  resourceCode?: string
  fromMaterialCode?: string
  toMaterialCode?: string
}

/* ==================== 发布请求体（§11.1 #13） ==================== */

/** POST /rule-set-versions/{id}/publish 请求体（复用 v1.4 范式） */
export interface PublishSetupRuleSetVersionRequest {
  changeReason: string
}

/* ==================== 权限码（v1.5 §11.1） ==================== */

/**
 * Setup 模块权限码（与后端 PermissionCodes.cs 对齐）
 *
 * 路由 meta 用 OR 语义：apsRequiredPermissions: ['aps.setup.view', 'aps.setup.edit', 'aps.setup.publish']
 *  - VIEWER（持 view）可读
 *  - APS_ADMIN（持全部）可写
 *  - RULE_PUBLISHER（持 publish）可发
 */
export const SETUP_PERMISSIONS = {
  VIEW: 'aps.setup.view',
  EDIT: 'aps.setup.edit',
  PUBLISH: 'aps.setup.publish'
} as const

export type SetupPermission = (typeof SETUP_PERMISSIONS)[keyof typeof SETUP_PERMISSIONS]
