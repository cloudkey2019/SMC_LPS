/**
 * 规则与参数维护 DTO（占位）
 *
 * @see 4号位文档第 14 节（页面 8：规则与参数维护）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U21（Priority Segment：页面不出现全局 PriorityScore）
 *  - U22（Rule 发布：新 Version 生成，历史不覆盖）
 *
 * 注意：前端不能直接改 PUBLISHED 字段（4号位文档第 14 节）
 */

import type { DomainKey, IsoDateTime, RoleKey, RuleStatus } from './common'

/* ===== RuleSet ===== */

/** RuleSet 列表项（精简字段） */
export interface RuleSetSummaryDto {
  ruleSetId: number
  ruleSetCode: string
  ruleSetName: string
  /** 适用 Domain（ALL 表示跨域） */
  domainKey: DomainKey
  /** 当前激活版本号 */
  currentVersion: number
  /** 待发布 DRAFT 版本号（仅当 status=DRAFT 时存在） */
  draftVersion?: number
  status: RuleStatus
  lastChangeReason?: string
  createdAt: IsoDateTime
  publishedAt?: IsoDateTime
  retiredAt?: IsoDateTime
  /** 4号位文档第 14 节：发布前调后端校验，前端只发请求不直接写库 */
  validationStatus: 'PENDING' | 'PASSED' | 'FAILED'
  validationMessage?: string
}

/** RuleSet 详情（包含 Parameters / Segments / Strategy / History） */
export interface RuleSetDetailDto {
  summary: RuleSetSummaryDto
  domainKey: DomainKey
  parameterSet: RuleSetParameterStubDto
  prioritySegments: PrioritySegmentDto[]
  strategyProfile: RuleSetStrategyStubDto
  history: RuleVersionHistoryDto[]
}

/** RuleSet 单版本历史 */
export interface RuleVersionHistoryDto {
  ruleSetId: number
  version: number
  /** v1.4 §二十：3 号位 后端 RuleSetVersion.VersionId（全局唯一；publish/disable/validate 端点键） */
  versionId?: number
  status: RuleStatus
  changeReason: string
  publishedAt?: IsoDateTime
  publishedBy: string
  /** 是否为当前生效版本 */
  isCurrent: boolean
}

/** Priority Segment（不暴露 PriorityScore） */
export interface PrioritySegmentDto {
  segmentId: number
  segmentCode: string
  segmentName: string
  /** 仅展示段内顺序（1 = 最高），禁止展示 PriorityScore（U21） */
  orderInSegment: number
  description?: string
}

/** Parameter 一项 */
export interface ParameterDto {
  parameterKey: string
  parameterName: string
  parameterType: 'NUMBER' | 'STRING' | 'BOOLEAN' | 'DURATION' | 'PERCENT'
  value: number | string | boolean
  defaultValue: number | string | boolean
  unit?: string
  description?: string
  /** 是否敏感（PMC 不可见，仅 SYSTEM_ADMIN / RULE_PUBLISHER） */
  sensitive: boolean
  /** 当前是否可编辑（DRAFT 才可编辑） */
  editable: boolean
}

/** ParameterSet 一份（占位 Stub，与 aps-v1/types/strategyProfile.ts 的真实 ParameterSetDto 不同名）
 *  真正的参数集 DTO 见 @/api/aps-v1/strategyProfile
 */
export interface RuleSetParameterStubDto {
  parameterSetId: number
  parameterSetCode: string
  parameterSetName: string
  version: number
  status: RuleStatus
  parameters: ParameterDto[]
  changeReason?: string
  createdAt: IsoDateTime
  publishedAt?: IsoDateTime
}

/** StrategyProfile 一份（RuleSetDetailDto 内嵌字段占位）
 *  注意：与 aps-v1/types/strategyProfile.ts 的真实 StrategyProfileDto 不同名（避免导出冲突）
 *  真正的策略配置 DTO 见 @/api/aps-v1/strategyProfile
 */
export interface RuleSetStrategyStubDto {
  strategyProfileId: number
  strategyProfileCode: string
  strategyProfileName: string
  version: number
  status: RuleStatus
  /** Solver Strategy 配置（具体字段待定） */
  solverStrategy: Record<string, unknown>
  /** 是否包含 Split / Setup / Overlap 等 */
  features: Array<'SPLIT' | 'SETUP' | 'OVERLAP'>
  changeReason?: string
  createdAt: IsoDateTime
  publishedAt?: IsoDateTime
}

/** Rule Diff（用于发布前对比） */
export interface RuleDiffDto {
  baseVersion: number
  targetVersion: number
  changedParameters: Array<{
    parameterKey: string
    oldValue: unknown
    newValue: unknown
    /** 旧值显示（带单位/格式） */
    oldDisplay?: string
    /** 新值显示 */
    newDisplay?: string
  }>
  /** Diff 摘要 */
  summary: {
    addedCount: number
    modifiedCount: number
    removedCount: number
  }
}

/* ===== Inputs ===== */

/** 发布 Rule 输入 */
export interface PublishRuleInput {
  ruleSetId: number
  draftVersion: number
  /** v1.4 §二十：3 号位 后端 /rule-set/version/{versionId}/publish 端点使用 */
  versionId?: number
  changeReason: string
  actor: string
  actorRoles: RoleKey[]
}

/** 发布结果 */
export interface PublishRuleResult {
  newVersion: number
  ruleSetCode: string
  publishedAt: IsoDateTime
  /** 历史版本号列表（U22：新版本生成，历史不覆盖） */
  historicalVersions: number[]
  actor: string
}

/** 退役 Rule 输入 */
export interface RetireRuleInput {
  ruleSetId: number
  version: number
  /** v1.4 §二十：3 号位 后端 /rule-set/version/{versionId}/disable 端点使用 */
  versionId?: number
  reason: string
  actor: string
  actorRoles: RoleKey[]
}

/** 退役结果 */
export interface RetireRuleResult {
  ruleSetCode: string
  version: number
  retiredAt: IsoDateTime
  actor: string
}

/* ====================================================================== */
/* v1.4 §二十：ParameterSet / RuleSet 写维护 DTO（B 设计稿 Step 1）           */
/* ====================================================================== */

/**
 * 治理状态别名：与 RuleStatus 同义，专用于治理类版本（RuleSetVersion / ParameterSetVersion）
 * - 6 态：v1.4 已冻结（3 号位 后端 GovernanceVersionStatus 对齐）
 * - 命名独立避免与 SetupRuleStatus 混淆
 */
export type GovernanceStatus = RuleStatus

/**
 * 5 主题 JSON 块 Key（B 设计稿 §1.1）
 *  - lock                锁定策略（Task 锁定/冻结约束）
 *  - supply              供应参数（PI/PO/库存等供应策略）
 *  - procurement         采购参数（前置期/占位等）
 *  - solverStrategy      Solver 策略参数（Split/Overlap/Priority 等）
 *  - candidateGuardrail  Candidate 守门参数（约束/红线）
 */
export type BlockKey = 'lock' | 'supply' | 'procurement' | 'solverStrategy' | 'candidateGuardrail'

export const BLOCK_KEYS: readonly BlockKey[] = [
  'lock',
  'supply',
  'procurement',
  'solverStrategy',
  'candidateGuardrail'
] as const

/** 5 主题 JSON 块容器（反序列化后的对象形态；PUT 时序列化回字符串） */
export type ParameterSetBlocks = Record<BlockKey, Record<string, unknown>>

/**
 * 治理公共字段（PUT 时一律冻结取自原记录，service 强制置 Status=DRAFT 入参忽略）
 * - 与 lps ParameterSetVersion.cs / RuleSetVersion.cs 治理字段对齐
 * - RuleSet / ParameterSet 各自的业务 ID 在子接口补
 */
export interface GovernanceVersionBase {
  /** 后端主键；POST 时可省（后端生成），PUT 时必填 */
  id?: number
  versionCode: string
  status: GovernanceStatus
  effectiveFrom?: IsoDateTime
  effectiveTo?: IsoDateTime
  publishedAt?: IsoDateTime
  publishedBy?: string
  approvedAt?: IsoDateTime
  approvedBy?: string
  createdAt?: IsoDateTime
  createdBy?: string
  /** 变更原因 / 备注 */
  remarks?: string
}

/** ParameterSet 治理字段（parameterSetId 是业务外键） */
export interface ParameterSetVersionGovernance extends GovernanceVersionBase {
  parameterSetId: number
}

/** RuleSet 治理字段（ruleSetId 是业务外键；parameterSetVersionId 是 RS→PS 1:1 引用） */
export interface RuleSetVersionGovernance extends GovernanceVersionBase {
  ruleSetId: number
  /** 引用 ParameterSetVersion（B 设计稿 §1.1.1 单向 1:1） */
  parameterSetVersionId: number
}

/** ParameterSet 单版本实体（B 设计稿 §1.1 ParameterSetVersion.cs 对齐） */
export interface ParameterSetVersion extends ParameterSetVersionGovernance {
  /** 5 主题 JSON 字符串（前端取出来反序列化成 Record）；PUT 时序列化回字符串 */
  lockJson?: string
  supplyJson?: string
  procurementJson?: string
  solverStrategyJson?: string
  candidateGuardrailJson?: string
  /** 发版落库的全量快照（前端可读不写） */
  contentSnapshotJson?: string
}

/** RuleSet 单版本实体（v1.4 §二十 + §1.1.1 RS↔PS 1:1 关联） */
export interface RuleSetVersion extends RuleSetVersionGovernance {
  /** RuleSetVersion 也有 5 JSON 字段（语义"规则策略"非"参数"）；当前 mock 未拆分 */
  lockJson?: string
  supplyJson?: string
  procurementJson?: string
  solverStrategyJson?: string
  candidateGuardrailJson?: string
  contentSnapshotJson?: string
}

/** 新建 RuleSet DRAFT 输入（POST /api/governance/rule-set/version） */
export interface CreateRuleSetDraftInput {
  ruleSetId: number
  versionCode: string
  /** fork 来源（PUBLISHED / DISABLED 时的源 RuleSetVersionId）；纯新建时为空 */
  sourceRuleSetVersionId?: number
  parameterSetVersionId: number
  remarks?: string
}

/** 更新 RuleSet DRAFT 输入（PUT /api/governance/rule-set/version/{id}） */
export interface UpdateRuleSetDraftInput {
  versionId: number
  body: RuleSetVersion
}

/** 新建 ParameterSet DRAFT 输入（POST /api/governance/parameter-set/version） */
export interface CreateParameterSetDraftInput {
  parameterSetId: number
  versionCode: string
  /** fork 来源（PUBLISHED / DISABLED 时的源 ParameterSetVersionId）；纯新建时为空 */
  sourceParameterSetVersionId?: number
  remarks?: string
}

/** 更新 ParameterSet DRAFT 输入（PUT /api/governance/parameter-set/version/{id}） */
export interface UpdateParameterSetDraftInput {
  versionId: number
  body: ParameterSetVersion
}

/** 通用 Governance Publish 输入 */
export interface PublishGovernanceInput {
  versionId: number
  changeReason: string
  actor: string
}

/** 通用 Governance Publish 结果 */
export interface PublishGovernanceResult {
  newVersion: number
  /** 治理对象 code（RuleSetCode / ParameterSetCode） */
  governanceCode: string
  publishedAt: IsoDateTime
  actor: string
}

/** 通用 Governance Disable 输入 */
export interface DisableGovernanceInput {
  versionId: number
  reason: string
  actor: string
}

/** 通用 Governance Disable 结果 */
export interface DisableGovernanceResult {
  governanceCode: string
  version: number
  disabledAt: IsoDateTime
  actor: string
}

/**
 * Fork Draft 复合结果（B 设计稿 §1.3 入口 B + §1.1.1 双轨）
 * - 前端手工 fork 走 GET → 复制 → POST 两步；POST 返回的新 DRAFT 两条都返回
 */
export interface ForkDraftResult {
  /** 新建 RuleSet DRAFT */
  ruleSetDraft: RuleSetVersion
  /** 新建 ParameterSet DRAFT（与 RuleSet 1:1 关联） */
  parameterSetDraft: ParameterSetVersion
}
