export type ApsZoneType = 'frozen' | 'firm' | 'open'

export type ApsTaskStatus =
  | 'scheduled'
  | 'running'
  | 'completed'
  | 'delayed'
  | 'cancelled'
  | 'material_shortage'

export type ApsApprovalStatus = 'draft' | 'approved' | 'released' | 'rejected'

export type ApsComputeMode = 'FULL_DETAIL' | 'CRITICAL_PATH' | 'ROUGH_CUT' | 'DOMAIN_SPLIT'

export interface ApsVersionOption {
  id: string
  name: string
  generatedAt: string
  approvalStatus: ApsApprovalStatus
  computeMode: ApsComputeMode
}

export interface ApsSummaryKpi {
  versionId: string
  generatedAt: string
  approvalStatus: ApsApprovalStatus
  onTimeRate: number
  utilizationRate: number
  delayedOrderCount: number
  bottleneckResourceCount: number
  alertCount: number
}

export interface ApsKpiCard {
  key: string
  label: string
  value: string
  trend: string
  status: 'good' | 'warn' | 'risk'
}

export interface ApsAlertItem {
  id: string
  level: 'high' | 'medium' | 'low'
  type: string
  title: string
  message: string
  taskId?: string
  orderId?: string
  createdAt?: string
  handled?: boolean
  recommendAction?: string
  /** 静默期类型（v3.0 §5.8）：production 生产时段 / night 夜间静默 / weekend 周末静默 / maintenance 维护窗口 */
  silentPeriod?: 'production' | 'night' | 'weekend' | 'maintenance'
}

export interface ApsLane {
  id: string
  code: string
  name: string
  factory: string
  resourceGroup: string
  utilization: number
  reservationRatio: number
  exceptionCount: number
  status?: 'normal' | 'maintenance' | 'overloaded'
  bottleneck?: boolean
  frozenTaskCount?: number
  lockedTaskCount?: number
}

export interface ApsTask {
  id: string
  versionId?: string
  orderId: string
  lotId: string
  taskCode: string
  materialCode: string
  materialName: string
  productFamily: string
  factory?: string
  line?: string
  resourceGroup?: string
  laneCode?: string
  laneName?: string
  laneId: string
  setupFamily: string
  quantity: number
  unit: string
  start: string
  end: string
  actualStart?: string
  actualEnd?: string
  progress: number
  zone: ApsZoneType
  status: ApsTaskStatus
  priority: 'SO' | 'SS' | 'SS-U'
  priorityValue?: number
  delayedHours: number
  earliestKitTime?: string
  latestNeedTime?: string
  planFreezeFlag: boolean
  executionLockFlag: boolean
  isLocked?: boolean
  isPinned: boolean
  isCritical: boolean
  canDrag?: boolean
  canLock?: boolean
  canChangePriority?: boolean
  canFreeze?: boolean
  canApplyUnfreeze?: boolean
  impactedOrders: string[]
  warnings: string[]
  changeReason?: string
  commitNode?: string
  reasonCode?: string
  rescheduleState?: 'none' | 'pending' | 'running' | 'done'
  unfreezeRequestStatus?: 'none' | 'pending' | 'approved' | 'rejected'
  lockSource?: 'none' | 'plan_freeze' | 'execution' | 'manual' | 'mixed'
  exceptionSeverity?: 'none' | 'low' | 'medium' | 'high'
  // 血缘追溯相关字段（后端支持后可启用）
  peggingChain?: PeggingChainItem[]
  upstreamTasks?: TraceTaskInfo[]
  downstreamTasks?: TraceTaskInfo[]
  inventorySource?: InventorySourceInfo
}

export interface PeggingChainItem {
  peggingId: string
  demandOrderId: string
  demandMaterialCode: string
  supplyOrderId: string
  supplyMaterialCode: string
  quantity: number
  bindTime: string
}

export interface TraceTaskInfo {
  taskId: string
  taskCode: string
  orderId: string
  materialName: string
  start: string
  end: string
  status: ApsTaskStatus
}

export interface InventorySourceInfo {
  inventoryId: string
  materialCode: string
  materialName: string
  quantity: number
  availableTime: string
  sourceType: 'stock' | 'in_transit' | 'production'
}

export interface ApsExplainTrace {
  taskId: string
  decisionId: string
  rule: string
  parameterSnapshot: string
  bottleneck: string
  materialLock: string
  conflictResolution: string
  changeReason: string
  impact: string[]
}

export interface ApsCompareTask {
  taskId: string
  baselineLaneId: string
  baselineStart: string
  baselineEnd: string
  driftHours: number
  laneId?: string
  taskCode?: string
  orderId?: string
  materialName?: string
  start?: string
  end?: string
  status?: ApsTaskStatus
  isCritical?: boolean
  changedFields?: string[]
}

export interface ApsOperationLog {
  id: string
  time: string
  actor: string
  action: string
  detail: string
  taskId?: string
}

export interface ApsGanttPayload {
  versions: ApsVersionOption[]
  defaultVersionId: string
  summary: ApsSummaryKpi
  lanes: ApsLane[]
  tasks: ApsTask[]
  kpis: ApsKpiCard[]
  alerts: ApsAlertItem[]
  explainTraces: ApsExplainTrace[]
  compareTasks: ApsCompareTask[]
  operationLogs: ApsOperationLog[]
  horizonStart: string
  horizonEnd: string
  currentTime: string
}

export interface ApsAdjustPreview {
  allowed: boolean
  denyReason?: string
  impactedOrderCount: number
  impactedOrders: string[]
  impactedTaskCount: number
  maintenanceConflict?: boolean
  crossingFrozenBoundary?: boolean
  rescheduleRequired: boolean
  rescheduleScope?: 'task' | 'order' | 'resource' | 'domain'
  previewToken?: string
  messages: string[]
}

export interface ApsVersionRecord extends ApsVersionOption {
  isActive: boolean
  onTimeRate: string
  utilizationRate: string
}

export interface ApsVersionTaskChange {
  taskId: string
  taskCode: string
  orderId: string
  laneChanged: boolean
  startChanged: boolean
  endChanged: boolean
  driftHours: number
}

export interface ApsVersionOrderChange {
  orderId: string
  baselinePromiseDate: string
  currentPromiseDate: string
  delayHours: number
}

export interface ApsVersionKpiChange {
  key: string
  label: string
  baselineValue: string
  currentValue: string
}

export interface ApsVersionHistoryPayload {
  versions: ApsVersionRecord[]
  baselineVersionId: string
  currentVersionId: string
  taskChanges: ApsVersionTaskChange[]
  orderChanges: ApsVersionOrderChange[]
  kpiChanges: ApsVersionKpiChange[]
}

export interface ApsUnfreezeRequestItem {
  id: string
  taskIds: string[]
  orderIds: string[]
  applicant: string
  createdAt: string
  reason: string
  urgency: 'normal' | 'high' | 'urgent'
  status: 'pending' | 'approved' | 'rejected'
  approver?: string
  comment?: string
  unfreezeType?: 'time_shift' | 'resource_change' | 'both'
  targetStartTime?: string
  targetEquipmentId?: string
}

export interface ApsUnfreezePayload {
  myRequests: ApsUnfreezeRequestItem[]
  pendingApprovals: ApsUnfreezeRequestItem[]
}

export interface ApsFrozenTaskOption {
  taskId: string
  taskCode: string
  orderId: string
  materialName: string
  laneName: string
  start: string
  end: string
  priority: string
  zone: 'frozen'
}

export interface ApsUnfreezeImpactOrder {
  orderId: string
  customer: string
  delayDays: number
  originalDueDate: string
  newDueDate: string
}

export interface ApsUnfreezeImpactPreview {
  taskId: string
  warningMessage: string
  impactedOrders: ApsUnfreezeImpactOrder[]
  impactedOrderCount: number
  estimatedRecoveryHours: number
}

export interface ApsUnfreezeSubmitPayload {
  taskId: string
  reasonCategory: 'customer_urgent' | 'equipment_fault' | 'material_shortage' | 'other'
  reasonDetail: string
  unfreezeType?: 'time_shift' | 'resource_change' | 'both'
  targetStartTime?: string
  targetEquipmentId?: string
}

// CTP 插单评估相关类型
export interface CtpEvaluateParams {
  customerLevel: string
  materialCode: string
  materialName: string
  quantity: number
  expectedDate: string
}

export interface CtpImpactedOrder {
  orderId: string
  customer: string
  delayDays: number
}

export interface CtpEvaluationResult {
  feasible: boolean
  feasibilityLabel: string
  earliestDate: string
  expectedDate: string
  delayDays: number
  evaluationTime: string
  impactedOrders: CtpImpactedOrder[]
  suggestions: string[]
  previewToken?: string
}

export interface CtpEvaluationPayload {
  currentEvaluation: CtpEvaluationResult | null
  evaluationHistory: CtpEvaluationRecord[]
}

export interface CtpEvaluationRecord {
  id: string
  materialCode: string
  materialName: string
  quantity: number
  customerLevel: string
  expectedDate: string
  result: 'accepted' | 'rejected' | 'adjusted'
  evaluatedAt: string
  evaluator: string
}

export interface CtpMaterialOption {
  code: string
  name: string
  availableStock: number
  leadTime: number
}

// v3.0 §5.8 ScheduleRun 排产执行记录
export interface ApsScheduleRun {
  runId: string
  runType: 'SCHEDULE' | 'SIMULATION'
  status: 'RUNNING' | 'SUCCESS' | 'FAILED'
  startedAt: string
  finishedAt?: string
  strategySnapshotId: string
  strategySnapshotName: string
  outputVersionIds: string[]
  triggeredBy: string
  progress: number
  message?: string
}

export interface ApsScheduleRunPayload {
  list: ApsScheduleRun[]
  total: number
}
