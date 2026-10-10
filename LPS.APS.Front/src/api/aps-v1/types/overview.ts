/**
 * 排产总览 DTO（占位）
 *
 * @see 4号位文档第 4 节（页面 1：排产总览）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：U01（ACTIVE 多 Domain）/ U02（PARTIAL_SUCCESS 区分）
 */

import type { DomainKey, IsoDateTime, OrderSummaryStatus, PlanVersionStatus } from './common'

/** 当前 ACTIVE 版本（排产总览头部） */
export interface ActiveVersionPanel {
  domainKey: DomainKey
  planVersionId: number
  versionCode: string
  status: PlanVersionStatus
  activatedAt: IsoDateTime
  sourceScheduleRunId?: number
  planHorizonStart: IsoDateTime
  planHorizonEnd: IsoDateTime
}

/** 订单结果摘要（顶部卡片） */
export interface OrderSummaryItem {
  status: OrderSummaryStatus
  count: number
}

/** 资源摘要（关键 Resource 负荷） */
export interface ResourceSummaryItem {
  resourceId: number
  resourceCode: string
  resourceName: string
  /** 0~1 利用率 */
  utilization: number
  /** 是否瓶颈 */
  isBottleneck: boolean
  /** 无可用产能时段（ISO 时间区间数组） */
  unavailableWindows: Array<{ from: IsoDateTime; to: IsoDateTime }>
}

/** 数据 / 运行异常（Domain 失败 + 上游阻断） */
export interface DomainIssueItem {
  domainKey: DomainKey
  issueType: 'FAILED' | 'BLOCKED_BY_UPSTREAM' | 'PI_POSITION_ISSUE' | 'ODS_CONTRACT_FAILED'
  message: string
  occurredAt: IsoDateTime
}

/** PlanOverviewDto — 排产总览（页面 1） */
export interface PlanOverviewDto {
  activeVersions: ActiveVersionPanel[]
  orderSummary: OrderSummaryItem[]
  resourceSummary: ResourceSummaryItem[]
  domainIssues: DomainIssueItem[]
  /** 依赖估算采购的订单数（验收 U07） */
  estimatedOnlyOrderCount: number
  /** 待确认 Candidate PlanVersion 数 */
  pendingCandidateCount: number
}
