/**
 * APS V1 4号位 — 异常与原因解释 API（占位）
 *
 * @see 4号位文档第 11-12 节（页面 6）
 * 验收：U09 / U14 / U20
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type { ExplanationViewDto } from './types'

const ISO_NOW = new Date().toISOString()
const ISO_HOUR_AGO = new Date(Date.now() - 3600_000).toISOString()
const ISO_4H_AGO = new Date(Date.now() - 4 * 3600_000).toISOString()

const mockExplanation = (planVersionId: number): ExplanationViewDto => ({
  planVersionId,
  scheduleExplanationFacts: [
    {
      factId: 1,
      objectType: 'TASK',
      objectRef: 'T1001',
      rootCause:
        '采购料 PO-2026-9981 估算 9 月 15 日才到；非 DUE_DATE_RISK 表面原因（@see 4号位文档第 11 节：根因优先）',
      issueCategory: 'DATA_ISSUE',
      severity: 'WARNING',
      factType: 'ESTIMATED',
      occurredAt: ISO_NOW
    },
    {
      factId: 2,
      objectType: 'RESOURCE',
      objectRef: 'MC01',
      rootCause: 'MC01 在 9 月 5 日~7 日无可用产能窗口（已固化冻结占用）',
      issueCategory: 'SCHEDULING_ISSUE',
      severity: 'ERROR',
      factType: 'FACT',
      occurredAt: ISO_HOUR_AGO
    },
    {
      factId: 3,
      objectType: 'PI',
      objectRef: 'PI-2026-3320',
      rootCause: 'PI Position 缺 XC 数量，Stage-3 不可定位（@see 4号位文档第 11 节）',
      issueCategory: 'DATA_ISSUE',
      severity: 'CRITICAL',
      factType: 'FACT',
      occurredAt: ISO_4H_AGO
    },
    {
      factId: 4,
      objectType: 'ORDER',
      objectRef: 'ORD-2026-0023',
      rootCause: '订单优先级 P1 与下游 AS 产能冲突，建议手工触发 Candidate',
      issueCategory: 'SCHEDULING_ISSUE',
      severity: 'WARNING',
      factType: 'RESULT',
      occurredAt: ISO_HOUR_AGO
    },
    {
      factId: 5,
      objectType: 'STAGE',
      objectRef: 'STG-AS-02',
      rootCause: '跨 Domain 共享设备被 TEST Domain 占用至 9 月 4 日（@see U14 验收）',
      issueCategory: 'SCHEDULING_ISSUE',
      severity: 'INFO',
      factType: 'FACT',
      occurredAt: ISO_4H_AGO
    },
    {
      factId: 6,
      objectType: 'PO',
      objectRef: 'PO-2026-9981',
      rootCause: '采购尚未正式承诺，仅有 PLANNING_PURCHASE_PLACEHOLDER 估算',
      issueCategory: 'DATA_ISSUE',
      severity: 'WARNING',
      factType: 'ESTIMATED',
      occurredAt: ISO_NOW
    }
  ],
  businessFactIssues: [
    {
      issueId: 1,
      domainKey: 'FAMILY_INJECTION',
      issueType: 'EQUIPMENT_FAILURE',
      message: '注塑机 MC02 故障停机',
      relatedObjectRef: 'MC02',
      occurredAt: ISO_HOUR_AGO
    },
    {
      issueId: 2,
      domainKey: 'FAMILY_INJECTION',
      issueType: 'MATERIAL_AVAILABILITY',
      message: 'M-005 物料库存低于安全水位',
      relatedObjectRef: 'M-005',
      occurredAt: ISO_NOW
    },
    {
      issueId: 3,
      domainKey: 'FAMILY_TEST',
      issueType: 'PI_POSITION_MISMATCH',
      message: 'PI-2026-3320 缺 XC 数量，Stage-3 不可定位',
      relatedObjectRef: 'PI-2026-3320',
      occurredAt: ISO_4H_AGO
    },
    {
      issueId: 4,
      domainKey: 'FAMILY_ASSEMBLY',
      issueType: 'CAPACITY_SHORTAGE',
      message: '装配线 ASM01 临近瓶颈利用率 95%',
      relatedObjectRef: 'ASM01',
      occurredAt: ISO_HOUR_AGO
    }
  ],
  rescheduleRecommendations: [
    {
      recommendationId: 1,
      triggerRefId: 1,
      triggerRefType: 'EXPLANATION_FACT',
      recommendationType: 'CANDIDATE_GENERATION',
      description: '基于 PO 估算日期发起的 Candidate 生成建议（影响 3 单 / 8 task）',
      estimatedImpact: { orderCount: 3, taskCount: 8, mayIntroduceNewDelay: false },
      generatedAt: ISO_NOW
    },
    {
      recommendationId: 2,
      triggerRefId: 2,
      triggerRefType: 'EXPLANATION_FACT',
      recommendationType: 'MANUAL_RESCHEDULE',
      description: 'MC01 产能窗口期冲突；建议阶段 B 手工触发 MANUAL_RESCHEDULE',
      estimatedImpact: { orderCount: 5, taskCount: 12, mayIntroduceNewDelay: true },
      generatedAt: ISO_HOUR_AGO
    },
    {
      recommendationId: 3,
      triggerRefId: 3,
      triggerRefType: 'BUSINESS_FACT_ISSUE',
      recommendationType: 'NO_ACTION_REQUIRED',
      description: 'TEST Domain PI 异常由 ODS 侧修复，前端无需操作',
      estimatedImpact: { orderCount: 0, taskCount: 0, mayIntroduceNewDelay: false },
      generatedAt: ISO_4H_AGO
    }
  ],
  equipmentFailures: [
    {
      equipmentId: 1,
      equipmentCode: 'MC02',
      downFrom: ISO_4H_AGO,
      downTo: undefined,
      impactedOrderCount: 4,
      impactAssessment: '影响正在 INJECTION Domain 执行的 4 个订单；TSET/ASM 域无影响',
      recommendation:
        '由 PMC 评估是否发起 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE；不直接改 Task 状态（审核报告 P1-13：V1 不建设 PAUSE/RESUME 状态闭环）'
    }
  ],
  failures: [
    {
      domainKey: 'FAMILY_TEST',
      scheduleRunId: 5001,
      failureType: 'BLOCKED_BY_UPSTREAM',
      errorMessage: 'FAMILY_INJECTION Domain 排程未完成，阻断 FAMILY_TEST Domain',
      failedAt: ISO_4H_AGO,
      retryable: true
    }
  ]
})

export const explanationApi = {
  /** 异常与原因解释（页面 6，@owner 5号位 中转 2号位 Explanation 服务）
   *  v1.4 §十四：根因优先，不只显示 DUE_DATE_RISK
   *  后端 ExplanationController：GET /api/explanation/summary
   *  （无 planVersionId 维度；summary 聚合所有 Domain/Task 的根因）
   */
  async getByPlanVersion(planVersionId: number): Promise<ExplanationViewDto> {
    if (APS_USE_MOCK) return mockExplanation(planVersionId)
    return apsHttp.get<ExplanationViewDto>({
      url: '/api/explanation/summary'
    })
  }
}
