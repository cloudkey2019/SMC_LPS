/**
 * 订单 / 需求计划查询 DTO（占位）
 *
 * @see 4号位文档第 5 节（页面 2：订单/需求计划查询）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U03（订单 Pegging 详情，Supply 来源和数量可追溯）
 *  - U04（一个 Task 承接多 Order，TaskShare 正确展示）
 *  - U05（计划良率，NetQty 与 PlannedProcessQty 不混淆）
 */

import type {
  DomainKey,
  FactType,
  IsoDate,
  IsoDateTime,
  OrderSummaryStatus,
  PlanVersionStatus,
  SupplyType,
  TaskLockMarker,
  TaskStatus
} from './common'

/** 订单查询条件 */
export interface OrderQuery {
  orderNo?: string
  productionInstructionNo?: string
  materialCode?: string
  customerCode?: string
  factoryCode?: string
  productFamilyCode?: string
  /** 交期范围 */
  dueDateFrom?: IsoDate
  dueDateTo?: IsoDate
  delayStatus?: OrderSummaryStatus
  /**
   * PlanVersion 主键（必填，> 0）
   *  - 后端 OrderQueryController.QueryOrders 签名 `[FromQuery] int planVersionId` 非空、无默认值 = 必填
   *  - 5号位 2026-09-23《order-query 分页契约修正回执》§一明确：planVersionId 漏传 → 框架 400
   *  - 订单按 PlanVersion 分区（设计上必填；非 bug）
   *  - UI 层（Order.vue）必填输入框 + 默认从 active-plan 取首个 Domain 的 planVersionId 自动填入
   *  - store 层（order.ts）loadList 前必校验，缺则抛错并提示用户
   */
  planVersionId: number
  domainKey?: DomainKey
  pageIndex: number
  pageSize: number
}

/** 订单基本信息（详情第一块） */
export interface OrderBasicInfo {
  orderId: number
  orderNo: string
  /** 订单规范化 Id（v5.0.34 增列；与 orderId 非同一 ID 空间；EXPEDITE §10A.1 D00 需求订单标识）
   *  5号位 B4 已落地（OrderQueryRepository 列表+详情 2 处 SQL 补 o.OrderCanonicalId）
   *  v1.33 §5.3 一致性约束：与 OrderCanonicalIds 必须一致（前端现不传 OrderCanonicalIds）
   *  §10A.1 OrderAdvanceDialog 自动带入该字段（保留手填兜底作兼容回退）
   */
  orderCanonicalId?: number
  productionInstructionNo?: string
  /** MTS 订单特殊指令号（dev 实跑字段命名 = `mtS_InstructionNo`，System.Text.Json CamelCase 处理） */
  mtS_InstructionNo?: string
  materialId: number
  materialCode: string
  materialName: string
  customerCode: string
  customerName: string
  factoryId: number
  factoryCode: string
  productFamilyCode: string
  orderQty: number
  uom: string
  customerDueDate: IsoDateTime
  prioritySegmentCode?: string
  /** 4号位文档第 21 节：不暴露全局 PriorityScore，仅展示 Segment */
  // priorityScore 字段刻意不导出（前端禁出现）
  status: OrderSummaryStatus
  planVersionId?: number
  planVersionStatus?: PlanVersionStatus
  domainKey?: DomainKey
}

/** Pegging 承接（详情第二块） */
export interface PeggingSupplyLine {
  supplyType: SupplyType
  supplyKey: string
  allocatedQty: number
  availableTime?: IsoDateTime
  /** 履约承诺等级（COMMITMENT / CONFIDENCE 等） */
  commitment?: string
  confidence?: string
  /** 是否被硬锁 */
  isHardLocked: boolean
  /** 4号位文档第 2.2 节：FACT / RESULT / ESTIMATED / RECOMMENDATION */
  factType: FactType
}

/** 生产计划 Task（详情第三块） */
export interface OrderTaskLine {
  taskId: number
  taskNo: string
  operationCode: string
  operationSeq: number
  stageCode?: string
  resourceId?: number
  resourceCode?: string
  startTime?: IsoDateTime
  endTime?: IsoDateTime
  /** 净产出数量（U05：与 PlannedProcessQty 不混淆） */
  netQty: number
  /** 计划加工数量（含良率损耗） */
  plannedProcessQty: number
  uom: string
  status: TaskStatus
  lockMarker?: TaskLockMarker
  /** U04：一个 Task 承接多 Order 时展示 TaskShare 列表 */
  taskShares?: Array<{
    orderId: number
    orderNo: string
    shareQty: number
  }>
}

/** 原因解释（详情第四块） */
export interface OrderDelayReason {
  reasonCode:
    | 'MATERIAL_SHORTAGE'
    | 'CAPACITY_INSUFFICIENT'
    | 'PREDECESSOR_DELAY'
    | 'FIRM_FROZEN'
    | 'CROSS_DOMAIN_BLOCK'
    | 'PURCHASE_ESTIMATED_RISK'
    | 'OTHER'
  description: string
  /** 根因描述（4号位文档第 11 节：根因优先，不只显示 DUE_DATE_RISK） */
  rootCause?: string
  /** 关联上游 Task / Stage / Material */
  relatedObjectRef?: string
}

/** OrderScheduleDetailDto — 订单详情（页面 2） */
export interface OrderScheduleDetailDto {
  basic: OrderBasicInfo
  pegging: PeggingSupplyLine[]
  tasks: OrderTaskLine[]
  reasons: OrderDelayReason[]
}
