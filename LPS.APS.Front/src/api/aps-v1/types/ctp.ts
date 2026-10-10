/**
 * CTP / 插单评估 DTO（占位）
 *
 * @see 4号位文档第 7-8 节（页面 4：CTP / 插单评估 + 跨 Domain CTP）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U07（Estimated 采购明显标记 NOT_COMMITTED）
 *  - U08（CTP 可按期，最早日期和原因正确）
 *  - U09（CTP 无法按期，显示根因，不只显示 DUE_DATE_RISK）
 *  - U11（CTP Purpose 永远不能激活 — 此处只做展示，不暴露"采用"按钮）
 *  - U15（MaxImpactedOrders 超阈值：Warning，不表示截断）
 */

import type { DomainKey, IsoDate, IsoDateTime } from './common'

/** CTP 输入（插单评估请求） */
export interface CtpInput {
  /** 新订单 CanonicalId（ERP / 内部统一编号） */
  orderCanonicalId: string
  materialCode: string
  quantity: number
  factoryCode: string
  /** 客户请求交期 */
  requestedDueDate: IsoDate
  customerCode?: string
  /** 业务用途标签（CTP / INSERT_IMPACT_ANALYSIS — 后端不允许激活） */
  purpose: 'CTP' | 'INSERT_IMPACT_ANALYSIS'
}

/** CTP 受影响订单 */
export interface CtpImpactedOrder {
  orderId: number
  orderNo: string
  /** 原完成时间 */
  originalCompletion: IsoDateTime
  /** 新完成时间（若 CTP 插入导致延期） */
  newCompletion?: IsoDateTime
  /** 是否从 On-time 变为 Delayed */
  becomesDelayed: boolean
  /** 是否触发 Protection 冲突 */
  hasProtectionConflict: boolean
}

/** CTP 单 Domain 结果 */
export interface CtpDomainResult {
  domainKey: DomainKey
  /** 该 Domain 的最早完成时间 */
  earliestCompletion: IsoDateTime
  /** 主要瓶颈描述 */
  mainBottleneck: string
  /** 是否成功 */
  success: boolean
  /** 失败原因 */
  failureReason?: string
}

/** CTP 主要原因（根因优先，不只显示 DUE_DATE_RISK） */
export interface CtpReason {
  reasonCode:
    | 'MATERIAL_AVAILABLE'
    | 'CAPACITY_LIMIT'
    | 'CROSS_DOMAIN_HANDOFF'
    | 'DEMAND_PROTECTION_CONFLICT'
    | 'DUE_DATE_RISK'
  description: string
  /** 是否 Estimated（4号位文档第 7 节：必须显示徽章） */
  isEstimated: boolean
  /** 关联对象引用 */
  relatedRef?: string
}

/** CtpResultDto — CTP / 插单评估结果 */
export interface CtpResultDto {
  /** 是否能按 RequestedDueDate 完成 */
  meetsRequestedDueDate: boolean
  /** 最早完成日期 */
  earliestCompletion: IsoDateTime
  /** 是否依赖 ESTIMATED 供给（U07） */
  dependsOnEstimatedSupply: boolean
  /** 影响订单数（U15：超阈值时仅 Warning，不表示截断） */
  impactedOrderCount: number
  /** MaxImpactedOrders 阈值（前端比对用） */
  maxImpactedOrdersThreshold: number
  /** 受影响订单明细 */
  impactedOrders: CtpImpactedOrder[]
  /** Protection 冲突列表 */
  protectionConflicts: Array<{
    orderId: number
    orderNo: string
    conflictType: string
    description: string
  }>
  /** 主要瓶颈 */
  mainBottleneck: string
  /** 主要原因（根因优先） */
  reasons: CtpReason[]
  /** 是否单 Domain Candidate（4号位文档第 9 节） */
  isSingleDomain: boolean
  /** 跨 Domain 时是否完成链式 WHATIF（V1 仅汇总展示） */
  isCrossDomainChained: boolean
  /** 各 Domain 结果（跨 Domain 时填充） */
  domainResults?: CtpDomainResult[]
}
