/**
 * 异常与原因解释 DTO（占位）
 *
 * @see 4号位文档第 11-12 节（页面 6：异常与原因解释 + 设备故障）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U09（CTP 无法按期：根因显示，不只 DUE_DATE_RISK）
 *  - U14（外 Domain 共享设备阻挡原因）
 *  - U20（设备故障：只提示影响/建议；V1 不建设 PAUSE/RESUME 状态闭环，决策由 PMC 发起 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE）
 */

import type { DomainKey, FactType, IsoDateTime } from './common'

/** 排程解释事实（ScheduleExplanationFact） */
export interface ScheduleExplanationFact {
  factId: number
  /** 关联对象类型 */
  objectType: 'TASK' | 'ORDER' | 'PI' | 'PO' | 'RESOURCE' | 'STAGE'
  /** 关联对象 ID / 编码 */
  objectRef: string
  /** 4号位文档第 11 节：根因描述（不只 DUE_DATE_RISK） */
  rootCause: string
  /** 数据问题 vs 排程问题分类 */
  issueCategory: 'DATA_ISSUE' | 'SCHEDULING_ISSUE'
  /** 严重度 */
  severity: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL'
  /** 关联 factType */
  factType: FactType
  occurredAt: IsoDateTime
}

/** 业务事实问题（BusinessFactIssue） */
export interface BusinessFactIssue {
  issueId: number
  domainKey: DomainKey
  issueType:
    | 'EQUIPMENT_FAILURE'
    | 'MATERIAL_AVAILABILITY'
    | 'PI_POSITION_MISMATCH'
    | 'CONTRACT_VIOLATION'
    | 'CAPACITY_SHORTAGE'
    | 'OTHER'
  message: string
  /** 关联 Resource / Material / Stage */
  relatedObjectRef?: string
  occurredAt: IsoDateTime
}

/** 重排建议（RescheduleRecommendation） */
export interface RescheduleRecommendation {
  recommendationId: number
  /** 触发原因 ID（关联到 BusinessFactIssue / ScheduleExplanationFact） */
  triggerRefId: number
  triggerRefType: 'BUSINESS_FACT_ISSUE' | 'EXPLANATION_FACT'
  /** 建议类型 */
  recommendationType: 'MANUAL_RESCHEDULE' | 'CANDIDATE_GENERATION' | 'NO_ACTION_REQUIRED'
  description: string
  /** 影响范围估计 */
  estimatedImpact: {
    orderCount: number
    taskCount: number
    /** 是否会引入新延期 */
    mayIntroduceNewDelay: boolean
  }
  generatedAt: IsoDateTime
}

/** Run / Domain 失败信息 */
export interface ScheduleFailureInfo {
  domainKey: DomainKey
  scheduleRunId?: number
  failureType: 'DOMAIN_FAILED' | 'BLOCKED_BY_UPSTREAM' | 'ENGINE_ERROR'
  errorMessage: string
  failedAt: IsoDateTime
  /** 是否可重试（仅展示，不直接重跑） */
  retryable: boolean
}

/** ExplanationViewDto — 异常与原因解释（页面 6） */
export interface ExplanationViewDto {
  planVersionId: number
  scheduleExplanationFacts: ScheduleExplanationFact[]
  businessFactIssues: BusinessFactIssue[]
  rescheduleRecommendations: RescheduleRecommendation[]
  /** 4号位文档第 12 节：设备故障明细（V1 不建设 PAUSE/RESUME 状态闭环，由 PMC 发起 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE） */
  equipmentFailures: Array<{
    equipmentId: number
    equipmentCode: string
    downFrom: IsoDateTime
    downTo?: IsoDateTime
    /** 影响订单数（仅展示，不自动操作） */
    impactedOrderCount: number
    impactAssessment: string
    recommendation: string
  }>
  failures: ScheduleFailureInfo[]
}
