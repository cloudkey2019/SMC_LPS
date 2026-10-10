/**
 * Candidate 对比与确认 DTO（占位）
 *
 * @see 4号位文档第 9 节（页面 5：Candidate 对比与确认）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U10（Candidate 影响既有订单，Base/Candidate 差异正确）
 *  - U11（CTP Purpose 无激活按钮）
 *  - U12（INSERT_IMPACT_ANALYSIS 无激活按钮）
 *  - U13（Candidate 采用，Actor/Time 可追溯）
 *  - U14（外 Domain 共享设备阻挡，页面显示阻挡原因）
 *
 * v1.2 Domain专项扩展（§10/§11/§13/§15）：
 *  - SharedResourceOccupancyDto  共享 Resource 占用明细
 *  - QuantityTimeSliceDto         跨域 Quantity-Time 切片（必须保留分段）
 *  - CandidateReasonCode          根因 reasonCode 字面量联合
 */

import type { DomainKey, FactType, IsoDateTime, PlanVersionStatus } from './common'
import type { ScheduleRunDto } from './run'

/**
 * 共享 Resource 占用（v1.2 §10/§13）
 *  - 其它 Domain 当前 ACTIVE 在共享 Resource 上的占用
 *  - 作为不可移动 `ExternalDomainResourceBlocks` 传给 1 号位
 *  - Candidate 不得跨域借资源；只能识别并提示
 */
export interface SharedResourceOccupancyDto {
  /** 共享资源 ID（如 MC01、测试台） */
  resourceRefId: number
  /** 资源代码（可选；UI 显示用） */
  resourceCode?: string
  /** 占用方 Domain */
  domainKey: DomainKey
  /** 占用时间窗起点（ISO8601） */
  occupiedFrom: IsoDateTime
  /** 占用时间窗终点（ISO8601） */
  occupiedTo: IsoDateTime
  /** 占用来源 PlanVersion（后续 Domain 不得修改） */
  sourcePlanVersionId: number
}

/**
 * Quantity-Time 切片（v1.2 §11）
 *  - 跨域依赖必须传递真实的 Material + Quantity + AvailableTime
 *  - **必须保留分段**（"40件@15日 / 60件@17日"），禁止压平为 100件
 *  - 通过内存上下文/DTO 传递；不建 VirtualInventoryBalance 持久化表
 */
export interface QuantityTimeSliceDto {
  materialCode: string
  /** 该切片数量（必须分多片保留，禁止合并） */
  quantity: number
  /** 该切片可用时间（ISO8601） */
  availableTime: IsoDateTime
  /** 来自哪个 Domain */
  sourceDomainKey: DomainKey
}

/**
 * Candidate 根因 reasonCode 字面量联合（v1.2 新增 SHARED_RESOURCE_/EXTERNAL_DOMAIN_/QUANTITY_TIME_SLICE）
 *  - 旧 reasonCode 是 string 类型；收紧为联合以编译期约束 mock 与 UI 渲染
 */
export type CandidateReasonCode =
  | 'CAPACITY_REALLOCATE'
  | 'CROSS_DOMAIN_BLOCK'
  | 'CTP_WHATIF'
  | 'INSERT_IMPACT_WHATIF'
  | 'SHARED_RESOURCE_OCCUPANCY'
  | 'EXTERNAL_DOMAIN_BLOCK'
  | 'QUANTITY_TIME_SLICE'

/** Candidate 头部（基础信息）
 * 审核报告 P0-05（WHATIF Candidate 语义）：
 *  - canActivate 区分"可激活"与"纯试算WHATIF"
 *  - runType + purpose 由 3 号位后端返回，前端只展示和禁用按钮
 *  - sourceRefType 保留兼容老接口（'CTP'/'MANUAL_RESCHEDULE'），新数据请用 canActivate
 */

/** UI 层用：候选来源类型中文映射（未知值原样展示） */
export const CANDIDATE_SOURCE_REF_TYPE_LABELS: Record<string, string> = {
  CTP: '可靠交期判断',
  MANUAL_RESCHEDULE: '手工重排',
  INSERT_ORDER_WHATIF: '插单试算'
}

/** UI 层用：候选业务用途中文映射（未知值原样展示） */
export const CANDIDATE_PURPOSE_LABELS: Record<string, string> = {
  CTP: '可靠交期判断',
  INSERT_IMPACT_ANALYSIS: '插单影响分析'
}
export interface CandidateHeader {
  candidatePlanVersionId: number
  candidateVersionCode: string
  basePlanVersionId: number
  baseVersionCode: string
  domainKey: DomainKey
  status: PlanVersionStatus
  createdAt: IsoDateTime
  /** 关联的 CTP / Manual 调度输入 ID */
  sourceRefId?: number
  sourceRefType?: 'CTP' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
  /** P0-05：是否允许激活为 ACTIVE（后端依据 RunType+Purpose+规则判定）
   *  - LOCAL_RESCHEDULE / MANUAL_RESCHEDULE → true
   *  - INSERT_ORDER_WHATIF（CTP/INSERT_IMPACT_ANALYSIS 产生） → false（仅试算）
   */
  canActivate: boolean
  /** P0-05：触发本次候选的 Run Type（与 ScheduleRun.runType 同源） */
  runType?: ScheduleRunDto['runType']
  /** P0-05：业务用途（仅 INSERT_ORDER_WHATIF 时填充） */
  purpose?: 'CTP' | 'INSERT_IMPACT_ANALYSIS'
  /** v1.2 §13：受外部 Domain 阻挡次数（≥1 说明跨域共享资源传递） */
  externalDomainBlocksCount?: number
  /** v1.2 §11：上游 Domain 传入的 Quantity-Time 切片（必须保留分段，禁止压平） */
  quantityTimeSlices?: QuantityTimeSliceDto[]
}

/** 新订单完成情况对比 */
export interface CandidateNewOrderDiff {
  orderId: number
  orderNo: string
  requestedDueDate: IsoDateTime
  baseCompletion?: IsoDateTime
  candidateCompletion: IsoDateTime
  /** 是否按期（基于 candidateCompletion） */
  onTime: boolean
  /** 是否 Estimated */
  isEstimated: boolean
}

/** 既有订单影响 */
export interface CandidateImpactedOrder {
  orderId: number
  orderNo: string
  baseCompletion: IsoDateTime
  candidateCompletion: IsoDateTime
  /** 差值（小时） */
  deltaHours: number
  /** 是否从 On-time 变 Delayed */
  becomesDelayed: boolean
  /** Protection 冲突 */
  hasProtectionConflict: boolean
  /** v1.2 §13：受影响订单若被共享资源阻挡，引用 SharedResourceOccupancy.resourceRefId */
  blockedBySharedResourceRefId?: number
}

/** Task 变化摘要（不要求 V1 复杂 Diff Graph） */
export interface CandidateTaskChangeSummary {
  /** 新增 Task 数 */
  added: number
  /** 删除 Task 数 */
  removed: number
  /** 时间移动的 Task 数 */
  timeShifted: number
  /** Resource 变更的 Task 数 */
  resourceChanged: number
  /** U14：外 Domain 共享设备阻挡的 Task 数 */
  crossDomainBlocked: number
  /** v1.2 §10/§13：共享 Resource 占用列表（其它 Domain ACTIVE 在共享 Resource 上的占用）
   *  - 默认空数组（向后兼容 v1.0/v1.1）
   */
  sharedResourceOccupancies?: SharedResourceOccupancyDto[]
}

/** 影响摘要（用于最小人工确认 Modal） */
export interface CandidateImpactSummary {
  impactedOrderCount: number
  newDelayCount: number
  estimatedOnlyCount: number
  crossDomainImpacted: boolean
  /** v1.2 §10：共享资源占用总数（≥1 说明跨域共享资源传递）— 默认 0 */
  sharedResourceOccupancyCount?: number
  /** v1.2 §11：Quantity-Time 切片总数（≥1 说明跨域 Quantity-Time 传递）— 默认 0 */
  quantityTimeSliceCount?: number
}

/** Candidate 确认输入（前端只展示按钮 + 上送用户输入，不直接改 PlanVersion） */
export interface CandidateConfirmInput {
  candidatePlanVersionId: number
  basePlanVersionId: number
  /** 当前操作者（来自 JWT） */
  actor: string
  /** 用户备注 */
  remark?: string
}

/** Candidate 确认结果 */
export interface CandidateConfirmResult {
  candidatePlanVersionId: number
  basePlanVersionId: number
  actor: string
  confirmedAt: IsoDateTime
  remark?: string
}

/** Candidate 激活输入（v1.2 §十三 P0-08）
 *  - CANDIDATE → ACTIVE：每域单一正式采用版本
 *  - 后端 ActivateCandidateRequest 是空 body（仅 path 上的 planVersionId）
 *  - 前端 DTO 留空壳以匹配 future 扩展（如激活原因 / 关联 RunId）
 */
export interface CandidateActivateInput {
  candidatePlanVersionId: number
}

/** Candidate 激活结果（v1.2 §十三 P0-08）*/
export interface CandidateActivateResult {
  candidatePlanVersionId: number
  /** 激活人（来自 JWT）*/
  activatedBy: string
  /** 激活时间（ISO8601）*/
  activatedAt: IsoDateTime
  /** 该 Candidate 转 ACTIVE 后覆盖的旧 ACTIVE PlanVersionId（若有）*/
  replacedActivePlanVersionId?: number
}

/** CandidateComparisonDto — Candidate 对比（页面 5） */
export interface CandidateComparisonDto {
  header: CandidateHeader
  newOrderDiffs: CandidateNewOrderDiff[]
  impactedOrders: CandidateImpactedOrder[]
  taskChangeSummary: CandidateTaskChangeSummary
  impactSummary: CandidateImpactSummary
  /** 4号位文档第 11 节：根因提示 */
  reasons: Array<{
    reasonCode: CandidateReasonCode
    description: string
    factType: FactType
  }>
}
