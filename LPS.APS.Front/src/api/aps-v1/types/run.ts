/**
 * 运行 / 版本 / MES 下发状态 DTO（占位）
 *
 * @see 4号位文档第 15-16 节（页面 9：运行/版本/MES 下发状态）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U01（ACTIVE 多 Domain 各 Domain 版本正确展示）
 *  - U02（PARTIAL_SUCCESS：成功/失败/被上游失败阻断的 Domain 区分）
 *  - U19（FAILED Run 恢复：新 Run 产生，旧 Run 仍 FAILED）
 *  - U16-U18（MES 下发资格判定）
 */

import type {
  DomainKey,
  IsoDateTime,
  MesEligibility,
  PlanVersionStatus,
  TaskStatus
} from './common'

/** ScheduleRun 详情 */
export interface ScheduleRunDto {
  runId: number
  runType: 'FULL_SCHEDULE' | 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
  status: 'PENDING' | 'RUNNING' | 'PARTIAL_SUCCESS' | 'SUCCESS' | 'FAILED'
  dataCutoffTime: IsoDateTime
  strategyProfileVersion?: string
  /** 期望 Domain 列表 */
  expectedDomainKeys: DomainKey[]
  startedAt: IsoDateTime
  completedAt?: IsoDateTime
  errorMessage?: string
}

/** Domain 执行结果（在 ScheduleRun 内） */
export interface DomainRunResult {
  domainKey: DomainKey
  status: 'SUCCESS' | 'FAILED' | 'BLOCKED_BY_UPSTREAM'
  errorMessage?: string
  /** 该 Domain 产出的 PlanVersionId（成功时） */
  planVersionId?: number
  /** 阻断来源 Domain Key（BLOCKED_BY_UPSTREAM 时） */
  blockedByDomainKey?: DomainKey
}

/** PlanVersion 详情 */
export interface PlanVersionDto {
  domainKey: DomainKey
  planVersionId: number
  versionCode: string
  status: PlanVersionStatus
  basePlanVersionId?: number
  activatedAt?: IsoDateTime
  errorMessage?: string
  /** 当前排产范围 */
  planHorizonStart: IsoDateTime
  planHorizonEnd: IsoDateTime
}

/** PARTIAL_SUCCESS 拆解（U02） */
export interface PartialSuccessBreakdown {
  scheduleRunId: number
  successDomains: DomainRunResult[]
  failedDomains: DomainRunResult[]
  blockedDomains: DomainRunResult[]
}

/** MES 下发资格（4号位文档第 16 节） */
export interface MesEligibilityDto {
  taskId: number
  taskNo: string
  eligibility: MesEligibility
  /** 不可下发时的原因列表 */
  reasons?: Array<
    | 'CANDIDATE'
    | 'UNLOCATED'
    | 'NO_PI'
    | 'PLANNING_PLACEHOLDER_DEPENDENCY'
    | 'OUT_OF_DISPATCH_WINDOW'
    | 'CANCELLED'
  >
  /** 下发窗口（合法下发开始/结束时间） */
  dispatchWindow?: { from: IsoDateTime; to: IsoDateTime }
  /**
   * 任务所属 Domain（P1-15 业务范围断言用；缺失 = 跳过前端断言，由 3号位后端兜底）
   * @see 审核报告 §十七.17.2 + §二十五.7
   */
  domainKey?: DomainKey
}

/** MES 下发输入（5号位中转 → MES） */
export interface MesDispatchInput {
  taskId: number
  /** 当前操作人（Section 17 审计要求；mock 由 JWT 注入） */
  actor: string
  /** 可选备注 */
  remark?: string
}

/** MES 下发结果 */
export interface MesDispatchResult {
  taskId: number
  taskNo: string
  /** MES 返回的工单号（5号位 → MES → 返回） */
  mesDispatchId: string
  /** 下发时间 */
  dispatchedAt: IsoDateTime
  status: 'DISPATCHED'
  /** 当前操作人（回显） */
  actor: string
}

/** RunStatusDto — 运行/版本/MES 状态（页面 9） */
export interface RunStatusDto {
  recentRuns: ScheduleRunDto[]
  activeVersions: PlanVersionDto[]
  /** 当前 PARTIAL_SUCCESS 拆解（如有） */
  currentPartialBreakdown?: PartialSuccessBreakdown
  /** MES 下发资格检查的样本 Task */
  mesEligibilitySamples: MesEligibilityDto[]
}

/** FAILED 恢复输入（U19：新建 Run，不直接改 FAILED → RUNNING） */
export interface RecoverFailedRunInput {
  originalRunId: number
  domainKeys: DomainKey[]
  reason: string
  actor: string
}

/** FAILED 恢复结果 */
export interface RecoverFailedRunResult {
  newRunId: number
  originalRunId: number
  /** 原始 Run 状态保持 FAILED（4号位文档第 15 节） */
  originalRunStatus: 'FAILED'
  newRunStatus: ScheduleRunDto['status']
  startedAt: IsoDateTime
}

/**
 * 候选重排触发输入（4号位文档第 6 节最后一句 + 审核报告 P0-02 / P0-03）
 *
 *  - runType 决定 LOCAL_RESCHEDULE（小范围）vs MANUAL_RESCHEDULE（较大范围人工主动）
 *  - 小范围 = 单 Task / 单 Resource / 指定订单 / 指定时间窗口
 *  - 较大范围 = 多 Task 跨 Domain 的人工主动重排（Gantt toolbar"批量人工重排"入口）
 *  - P0-02 严格单 Domain：跨 Domain CTP 由多个单 Domain WHATIF 链式计算后汇总
 *  - P0-03 范围 → runType 分流：
 *      局部明确范围 → LOCAL_RESCHEDULE（单值 objectRefId）
 *      较大范围人工主动 → MANUAL_RESCHEDULE（多 Task objectRefIds）
 *  - actor / reason 必填，落到 Section 17 审计
 *  - objectRef / objectRefIds 用于审计（Section 17）
 *  - Section 12 设备故障也可借此触发"建议重排"
 *
 * objectRefId 与 objectRefIds 互斥：
 *  - objectRefType='TASK' 且 runType='MANUAL_RESCHEDULE' → 必须传 objectRefIds（批量）
 *  - 其他 objectRefType（ORDER / DOMAIN / RESOURCE）以及 LOCAL_RESCHEDULE → 必须传 objectRefId（单值）
 *
 * 2026-09-17 3号位 回执后重构（@see 3号位回执_致4号位_治理端点对接确认_v1.0_20260917.md §四）：
 *  - 后端实际入参 `CreateCandidateRunRequest`（非 `TriggerRescheduleInput`）
 *  - 字段对齐：reason → Remark；objectRefIds 砍掉（前端传不到后端）；新增 Purpose（与 RunType 配对）
 *  - 权限码：`aps.plan.run`（后端 L626 已挂；aps.run.reschedule / aps.run.candidate 均不存在）
 *  - RunType×Purpose 冻结合法组合：
 *      MANUAL_RESCHEDULE → MANUAL_ADJUSTMENT
 *      LOCAL_RESCHEDULE → INSERT_RESCHEDULE | MANUAL_ADJUSTMENT
 *      INSERT_ORDER_WHATIF → CTP | INSERT_IMPACT_ANALYSIS（永不可激活）
 */
export interface TriggerRescheduleInput {
  /** runType 分流（P0-03）；INSERT_ORDER_WHATIF 仅供 WHATIF 码（永不可激活） */
  runType: 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
  /** Purpose 必填，与 runType 配对（@see RunLifecycleService.cs:65-71） */
  purpose: 'MANUAL_ADJUSTMENT' | 'INSERT_RESCHEDULE' | 'CTP' | 'INSERT_IMPACT_ANALYSIS'
  /** 单计划域（P0-02：白天候选运行严格单计划域；跨 Domain 由多个单 Domain 链式调用） */
  domainKey: DomainKey
  /** 触发人（必填，Section 17 审计要求） */
  actor: string
  /**
   * 备注：**前端停传**（2026-09-21 3号位 回执 §一.3——`CandidateRunCreateSpec` 无 Remark 属性，
   * 后端维持丢弃；DTO 上保留字段仅为契约位标注）。保留可选属性仅为兼容旧调用方。
   */
  remark?: string
  /** 基于的 ACTIVE 计划版本（缺省按 DomainKey 解析 ACTIVE） */
  basePlanVersionId?: number
  /** 数据切片边界（缺省 now） */
  dataCutoffTime?: IsoDateTime
  /**
   * §10A 白天人工调整范围载荷（ScopeJsonV2）。缺省不传 = 旧调用兼容（后端静默放行）。
   * @see 冻结文档《4号位页面与业务操作开发实施包 v1.4》§十A
   */
  scope?: ScopeJsonV2
}

/* ==================== §10A 白天人工调整：ScopeJsonV2 契约 ==================== */

/**
 * 八码业务触发类型（后端 `BusinessTriggerType`，`[EnumMember]` 契约字面量，字符串传输非数字）。
 * @see lps/LPS.APS.Core/Enum/BusinessTriggerType.cs:15-40
 */
export const BUSINESS_TRIGGER_TYPES = [
  'NEW_ORDER_CTP',
  'NEW_ORDER_IMPACT',
  'NEW_ORDER_INSERT',
  'EXISTING_ORDER_ADVANCE',
  'GANTT_ADJUSTMENT',
  'EQUIPMENT_FAILURE',
  'RESOURCE_CALENDAR_CHANGE',
  'DOMAIN_MANUAL_RESCHEDULE'
] as const

export type BusinessTriggerType = (typeof BUSINESS_TRIGGER_TYPES)[number]

/** 八码中文 label（§10A 入口标题 / 下拉项直接用） */
export const BUSINESS_TRIGGER_LABELS: Record<BusinessTriggerType, string> = {
  NEW_ORDER_CTP: '新订单 CTP 评估',
  NEW_ORDER_IMPACT: '插单影响分析',
  NEW_ORDER_INSERT: '新订单插单',
  EXISTING_ORDER_ADVANCE: '已有订单提前',
  GANTT_ADJUSTMENT: '甘特图调整',
  EQUIPMENT_FAILURE: '设备故障后重排',
  RESOURCE_CALENDAR_CHANGE: '资源日历调整后重排',
  DOMAIN_MANUAL_RESCHEDULE: '整 Domain 人工重排'
}

/** 优先模式（后端 `PriorityMode`，字符串字面量） */
export type PriorityMode = 'NORMAL' | 'EXPEDITE'

export const PRIORITY_MODES = ['NORMAL', 'EXPEDITE'] as const

export const PRIORITY_MODE_LABELS: Record<PriorityMode, string> = {
  NORMAL: '普通（NORMAL）',
  EXPEDITE: '加急（EXPEDITE）'
}

/** 订单目标：本次 Candidate 的手工目标交期（不修改正式 DueDate） */
export interface OrderTargetDto {
  /**
   * 订单规范 Id（后端 `Order_Canonical.Id`，long）。
   * ⚠️ 与订单查询列表的 `orderId`（`[Order].Id`，本地分区自增主键）**不是同一 ID 空间**。
   */
  orderCanonicalId: number
  /** 本次 Run 的手工目标交期（ISO） */
  manualTargetDueDate: IsoDateTime
}

/** Task 目标：软目标时间（Solver 可返回其它可行时间，禁止直接 UPDATE 正式 Task） */
export interface TaskTargetDto {
  /** Gantt/Versions 任务主键 */
  taskId: number
  /** 软目标时间（后端为非空 DateTime，必填） */
  targetTime: IsoDateTime
}

/**
 * 局部重排范围载荷 v2（后端 `ScopeJsonV2`，camelCase）。
 * @see lps/LPS.APS.Core/DTOs/Governance/ScopeJsonV2.cs:12-28
 *
 * 后端校验（`ScopeJsonV2Validator`，违规 → HTTP 400）：
 *  - 八码 ↔ RunType × Purpose 固定映射（大小写敏感精确匹配）
 *  - PriorityMode 轴：GANTT/EQUIPMENT/RESOURCE_CALENDAR/NEW_ORDER_CTP 禁 EXPEDITE、
 *    NEW_ORDER_IMPACT 禁 NORMAL、DOMAIN_MANUAL_RESCHEDULE **须省略该键**
 *  - orderTargets 按 orderCanonicalId 去重唯一
 */
export interface ScopeJsonV2 {
  trigger: BusinessTriggerType
  /** 省略 = 不指定（后端对部分码要求必须省略该键） */
  priorityMode?: PriorityMode
  /** 订单目标（10A.1） */
  orderTargets?: OrderTargetDto[]
  /** Task 软目标（10A.2） */
  taskTargets?: TaskTargetDto[]
  /** 已正式不可用 / 已正式改 Calendar 的资源 Id（10A.3 / 10A.4） */
  changedResourceIds?: number[]
}

/** 候选重排触发结果：返回新的 ScheduleRunId + Candidate PlanVersionId */
export interface TriggerRescheduleResult {
  newRunId: number
  /** 产出的 CANDIDATE PlanVersionId（前端跳到 Candidate 对比页） */
  candidatePlanVersionId: number
  /** 基准 PlanVersionId（当前 ACTIVE 版本） */
  basePlanVersionId: number
  startedAt: IsoDateTime
  /** 预计运行耗时（秒） */
  estimatedDurationSeconds?: number
}

/** @deprecated 用 TriggerRescheduleInput；保留以兼容老调用方 */
export type TriggerManualRescheduleInput = TriggerRescheduleInput
/** @deprecated 用 TriggerRescheduleResult */
export type TriggerManualRescheduleResult = TriggerRescheduleResult

/** Task 状态机检查器（前端只读展示，禁止转换） */
export function assertValidTaskStatus(status: string): TaskStatus {
  const valid = ['PLANNED', 'RELEASED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED']
  if (!valid.includes(status)) {
    throw new Error(`[4号位文档第6节] Task 状态非法: ${status}，仅允许 ${valid.join('/')}`)
  }
  return status as TaskStatus
}
