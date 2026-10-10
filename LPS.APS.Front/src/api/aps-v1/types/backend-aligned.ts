/**
 * 后端 Core DTO 类型对齐（只读参考，不修改后端）
 *
 * 这些 DTO 由 2 号位、5 号位在内部流转使用，前端一般不直接消费（除非需要展示
 * 内部细节，如 SolverStrategySnapshot、CandidateContext 等）。
 *
 * 大部分情况下，前端通过 4 号位专用 DTO（PlanOverview/OrderScheduleDetail/CtpResult
 * 等）消费数据；本文件仅保留"看名字就需要对齐"的子结构。
 *
 * 来源：lps/LPS.APS.Core/Dto/
 *  - OrderSpec.cs / TaskSpec.cs / RoutingOperationSpec.cs / OperationEligibilitySpec.cs
 *  - DemandBalance.cs / LogicalProductionDemand.cs / BatchSplitInput.cs
 *  - DomainSolveRequest.cs / DomainSolveResult.cs
 *  - PeggingExecutionRequest.cs / PeggingRuleVoucher.cs / PeggingResultVoucher.cs
 *  - PeggingLedgerEntry.cs / SchedulingRunResultDto.cs
 */

import type { IsoDateTime } from './common'

/* ===== OrderSpec ===== */
export interface OrderSpec {
  orderId: number
  orderNo: string
  materialId: number
  materialCode: string
  productFamilyId: number
  factoryId: number
  quantity: number
  uom: string
  customerDueDate: IsoDateTime
  priority: number
}

/* ===== TaskSpec ===== */
export interface TaskSpec {
  taskNo: string
  orderId: number
  materialId: number
  operationSeq: number
  operationCode: string
  operationName: string
  resourceId?: number
  routeCode: string
  pathId: number
  quantity: number
  uom: string
  durationMinutes: number
  taskType: string
  orderPriority: number
  customerDueDate: IsoDateTime
}

/* ===== RoutingOperationSpec ===== */
export interface RoutingOperationSpec {
  orderId: number
  materialId: number
  operationSeq: number
  operationCode: string
  /** 主资源（可能为空，表示待选） */
  primaryResourceId?: number
  /** 替代资源列表 */
  alternateResourceIds: number[]
  /** 工段代码 */
  stageCode: string
  /** 标准工时（分钟） */
  standardMinutes: number
  /** 良率（0~1） */
  yield: number
}

/* ===== OperationEligibilitySpec ===== */
export interface OperationEligibilitySpec {
  materialId: number
  operationCode: string
  resourceId: number
  priority: number
}

/* ===== DemandBalance ===== */
export type DemandType = 'ORDER' | 'BACKLOG' | 'FORECAST' | 'COMPONENT'

export interface DemandBalance {
  requiredQty: number
  remainingQty: number
  materialId: number
  materialCode: string
  factoryId: number
  factoryCode: string
  demandType: DemandType
  demandKey: string
  rootOrderId?: number
  currentOrderId?: number
  bomLevel: number
  dueTime: IsoDateTime
  priority: number
  productFamilyId: number
  isInFrozenZone: boolean
  worksetId?: number
}

/* ===== LogicalProductionDemand ===== */
export interface LogicalProductionDemand {
  logicalDemandKey: string
  planVersionId: number
  domainKey: string
  allocationSequence: number
  demandKey: string
  orderId?: number
  materialId: number
  factoryId: number
  startStageCode: string
  netOutputQty: number
  plannedProcessQty: number
  requiredAvailableTime: IsoDateTime
  /** 计算层→Priority Segment→段内排序的结果（不暴露 PriorityScore） */
  demandSequence: number
  productionInstructionNo?: string
}

/* ===== BatchSplitInput ===== */
export interface BatchSplitInput {
  planVersionId: number
  orders: OrderSpec[]
  operations: RoutingOperationSpec[]
  eligibilities: OperationEligibilitySpec[]
}

/* ===== DomainSolveRequest ===== */
export interface AllocationLineage {
  allocationSequence: number
  demandKey: string
  materialId: number
  supplyType: string
  supplyKey: string
  quantity: number
  availableTime?: IsoDateTime
}

/** 多段 Quantity-Time（4号位文档验收 U06：40+60 不压成 100 最晚） */
export interface MaterialAvailabilitySlice {
  allocationSequence: number
  materialId: number
  factoryId: number
  quantity: number
  availableTime: IsoDateTime
  sourceType?: string
  sourceKey?: string
  commitment?: string
  confidence?: string
}

export interface SolverStrategySnapshot {
  strategyProfileVersionId?: number
  parameterSetVersionId?: number
  parameters: Record<string, unknown>
}

export interface DomainSolveRequest {
  scheduleRunId?: number
  planVersionId: number
  domainKey: string
  dataCutoffTime?: IsoDateTime
  planningStart: IsoDateTime
  planningEnd: IsoDateTime
  logicalProductionDemands: LogicalProductionDemand[]
  allocationLineage: AllocationLineage[]
  routingOperations: unknown[]
  routingDependencies: unknown[]
  operationResourceEligibility: unknown[]
  materialConstraints: MaterialAvailabilitySlice[]
  resources: unknown[]
  calendarSlots: unknown[]
  resourceEligibility: unknown[]
  executionConstraints: unknown[]
  strategySnapshot: SolverStrategySnapshot
  candidateContext?: Record<string, unknown>
}

/* ===== DomainSolveResult ===== */
export interface FinalTaskDraft {
  finalDraftId: string
  sourceDraftId: string
  materialId: number
  factoryId: number
  stageCode: string
  operationCode: string
  taskType: string
  resourceId: number
  resourceCode: string
  routeCode?: string
  pathId?: number
  quantity: number
  plannedProcessQty: number
  uom: string
  plannedStartTime: IsoDateTime
  plannedEndTime: IsoDateTime
  setupTime: number
  priority: number
  isVirtual: boolean
  stageExecutionBatchDraftKey?: string
  stageExecutionBatchQty?: number
  existingMESPlanReleaseId?: number
  executionLockId?: number
}

export interface AllocationTaskShare {
  finalDraftId: string
  allocationSequence: number
  componentQty: number
}

export interface UnscheduledTaskResult {
  orderId: number
  materialId: number
  operationCode: string
  reason: string
}

export interface FinalTaskPeggingDraft {
  upstreamFinalDraftId: string
  downstreamFinalDraftId: string
  upstreamMaterialId: number
  downstreamMaterialId: number
  quantity: number
  uom: string
  inheritedPriority: number
  /** ES=结束-开始（默认）, SS, FF */
  dependencyType: 'ES' | 'SS' | 'FF'
  /** 延迟（分钟） */
  lagTime: number
}

export interface SolverExplanationFact {
  objectType: 'TASK' | 'ORDER' | 'PI' | 'PO' | 'RESOURCE' | 'STAGE'
  objectRef: string
  /** 4号位文档第 11 节：根因描述 */
  rootCause: string
  category: 'DATA_ISSUE' | 'SCHEDULING_ISSUE'
  severity: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL'
  occurredAt: IsoDateTime
}

export interface SolveSummary {
  totalDemands: number
  scheduledDemands: number
  unscheduledDemands: number
  totalTasks: number
}

export interface DomainSolveResult {
  success: boolean
  errorMessage?: string
  isRoughCut: boolean
  finalTasks: FinalTaskDraft[]
  allocationShares: AllocationTaskShare[]
  unscheduledTasks: UnscheduledTaskResult[]
  physicalPeggingDrafts: FinalTaskPeggingDraft[]
  /** 求解器内部事实（与视图层 ScheduleExplanationFact 同义不同名） */
  explanationFacts: SolverExplanationFact[]
  summary: SolveSummary
}

/* ===== PeggingExecutionRequest ===== */
export type CrossFactoryMode = 'STAGE_HANDOFF' | 'INTER_FACTORY_ORDER' | 'VIRTUAL_FACTORY'
export type PeggingStrategyType = 'FIFO' | 'FEFO' | 'LIFO' | 'MIN_QTY' | 'PRIORITY'

export interface VirtualInventoryItem {
  materialId: number
  factoryCode: string
  sourceProductFamilyId: number
  virtualAvailableQuantity: number
  availableAt: IsoDateTime
  upstreamTaskId?: number
  topologicalOrder: number
}

export interface PeggingExecutionRequest {
  planVersionId: number
  orderIds: number[]
  snapshotAt: IsoDateTime
  frozenWindowStart: IsoDateTime
  frozenWindowEnd: IsoDateTime
  allowCrossFactory: boolean
  crossFactoryMode?: CrossFactoryMode
  defaultStrategy: PeggingStrategyType
  productFamilyIds: number[]
  topologicalOrder: Record<number, number>
  virtualInventory: VirtualInventoryItem[]
  forceRePegging: boolean
  maxBomDepth: number
  timeoutSeconds: number
  executionMode: 'FULL_RUN' | 'DRY_RUN' | 'INCREMENTAL'
  schedulingContext?: Record<string, unknown>
}

/* ===== PeggingRuleVoucher ===== */
export type SupplySourceType =
  | 'INVENTORY'
  | 'PI'
  | 'PO'
  | 'VMI'
  | 'ARRIVED_NOT_INBOUND'
  | 'INTERPLANT_TRANSIT'
  | 'RECEIVED'
  | 'PLANNED_PRODUCTION'
  | 'PLANNING_PURCHASE_PLACEHOLDER'

export interface CrossFactoryModeDecision {
  sourceFactoryCode: string
  targetFactoryCode: string
  mode: CrossFactoryMode
  ruleBasis: string
}

export interface SupplyCandidate {
  rank: number
  sourceType: SupplySourceType
  sourceReference: string
  factoryCode: string
  availableQuantity: number
  availableAt: IsoDateTime
  shippingInstructionNo?: string
}

export type FrozenReasonType =
  | 'MES_DISPATCHED'
  | 'MANUAL_LOCK'
  | 'CONSTRAINT_FIXED'
  | 'IN_EXECUTION'
  | 'CUSTOMER_COMMITMENT'

export interface ZpBpValidationResult {
  shippingInstructionNo: string
  matched: boolean
  message?: string
}

export interface FreezeDecision {
  shouldFreeze: boolean
  reason?: FrozenReasonType
  reasonText?: string
}

export interface PeggingRuleVoucher {
  voucherId: string
  planVersionId: number
  orderId: number
  isSuccess: boolean
  errorMessage?: string
  warnings: string[]
  evaluatedAt: IsoDateTime
  crossFactoryDecision?: CrossFactoryModeDecision
  rankedSupplyCandidates: SupplyCandidate[]
  zpBpValidation?: ZpBpValidationResult
  freezeDecision?: FreezeDecision
  businessRuleErrors: string[]
}

/* ===== PeggingResultVoucher / PeggingLedgerEntry 概要 ===== */
export interface PeggingResultVoucher {
  voucherId: string
  planVersionId: number
  orderId: number
  isSuccess: boolean
  allocations: Array<{
    allocationSequence: number
    supplyType: SupplySourceType
    supplyKey: string
    qty: number
    availableTime: IsoDateTime
  }>
  evaluatedAt: IsoDateTime
}

export interface PeggingLedgerEntry {
  allocationSequence: number
  planVersionId: number
  orderId: number
  demandKey: string
  supplyType: SupplySourceType
  supplyKey: string
  qty: number
  committedAt: IsoDateTime
}

/* ===== SchedulingRunResultDto ===== */
export interface SchedulingRunResultDto {
  runId: number
  success: boolean
  isPartialSuccess: boolean
  successfulDomains: string[]
  failedDomains: Array<{
    domainKey: string
    errorMessage: string
  }>
  blockedDomains: Array<{
    domainKey: string
    blockedByDomainKey: string
  }>
  planVersionIds: number[]
  startedAt: IsoDateTime
  completedAt: IsoDateTime
  errorMessage?: string
}
