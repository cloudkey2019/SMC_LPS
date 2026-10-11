/**
 * APS V1 4号位 — 异常与原因解释 API（按 5号位 2026-10-09 函件 R1 + 10-10 冻结基线收口）
 *
 * @see 4号位文档第 11-12 节（页面 6：异常与原因解释）
 * @see 5号位 lps/LPS.APS.Web/Controllers/ExplanationController.cs（L41-94）
 * @see 5号位 lps/LPS.APS.Web/Controllers/BusinessFactIssueController.cs :60-95（聚合业务事实问题独立端点）
 * @see 5号位 lps/LPS.APS.BusinessRules/Services/ExplanationQueryService.cs
 * @see 5号位 lps/LPS.APS.Core/Dto/ExplanationDtos.cs :48-66
 *
 * 端点契约（5号位 落码）：
 *   GET /api/explanation?planVersionId=  - 列表查询（filter: objectType/reasonCode/severity/skip/take）
 *   GET /api/explanation/summary?planVersionId=  - 汇总（含 items[] + 按 ReasonCode 分组）
 *   GET /api/business-fact-issues?source=&materialCode=&factoryCode=...  - 业务事实问题（独立聚合）
 *
 * 10-09 改造：
 *  - R1 ReasonCode 字典对齐 v5.1.10 §八.1 15 码全集（@see types/explanation.ts REASON_CODES）
 *  - ObjectType 5 码 / Severity 3 码（@see types/explanation.ts）
 *  - 撤 ScheduleFailureInfo（5号位 关键纠正 3「后端无此实体」）
 *  - 修 400 bug：原 `apsHttp.get({url:'/api/explanation/summary'})` 漏 planVersionId query
 *    → 改 `params: { planVersionId }`（与 controller 签名 [FromQuery] int planVersionId 一致）
 *  - 端点对齐：调用 `/api/explanation/summary`（含 items[]）+ `/api/explanation`（list 备查）
 *
 * 10-10 收口：
 *  - 撤 RescheduleRecommendation / EquipmentFailure DTO（冻结基线无要求）
 *  - 撤 mockRecommendations / mockEquipmentFailures
 *  - 业务事实问题改为独立调用 `/api/business-fact-issues` 聚合（替代原占位 mockBusinessIssues）
 *  - 10-10 撤 .catch(() => []) 降级：5号位 回执确认根因=查错库（DatabaseId.APS→ODS），
 *    表在 ODS/MES_Integration 已存在，端点返 200；前端不再吞真错误（吞错会掩盖未来 DDL 漂移）
 *  - 10-10 business-fact-issues 分页 + total + PageResult 包装：
 *    - getByPlanVersion 调 pageIndex=1 pageSize=20 首查
 *    - 新增 listBusinessFactIssues(opts) 给 store 翻页用
 *    - 后端契约建议改 PageResult<T>（@see ./types/common.ts:62-67，TaskSplitRuleConfig 已用同模式）
 *    - 函件：frontNew/docs/4号位-2026-10-10-business-fact-issues分页severity排序total-给5号位.md
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type {
  BusinessFactIssue,
  ExplanationViewDto,
  ReasonCodeCount,
  ScheduleExplanationFact
} from './types'

/* ===== Mock 构造器（与 5号位 Summary 端点 DTO 1:1 对齐） ===== */

const ISO_NOW = () => new Date().toISOString()
const ISO_HOUR_AGO = () => new Date(Date.now() - 3600_000).toISOString()
const ISO_4H_AGO = () => new Date(Date.now() - 4 * 3600_000).toISOString()

/** mock 排程解释事实（15 码全覆盖，每码至少 1 条以验证字典完整性） */
const mockFacts = (planVersionId: number): ScheduleExplanationFact[] => [
  // 5号位 实际产出的 5 码（@see PhaseThreeDiagnostics.cs L258/281/291/303/326/347/403/409）
  {
    factId: 1,
    planVersionId,
    objectType: 'TASK',
    objectRef: 'T-1001',
    reasonCode: 'MATERIAL_SHORTAGE',
    severity: 'WARN',
    impactHours: 4.5,
    evidenceJson: JSON.stringify({
      evidenceType: 'MATERIAL',
      summary: '物料 M-005 库存低于安全水位'
    }),
    occurredAt: ISO_NOW()
  },
  {
    factId: 2,
    planVersionId,
    objectType: 'RESOURCE',
    objectRef: 'MC01',
    reasonCode: 'FROZEN_ZONE_LOCK',
    severity: 'ERROR',
    impactHours: 12,
    evidenceJson: JSON.stringify({
      evidenceType: 'LOCK',
      summary: '冻结区 9-5~7 锁定 MC01 产能窗口'
    }),
    occurredAt: ISO_HOUR_AGO()
  },
  {
    factId: 3,
    planVersionId,
    objectType: 'ORDER',
    objectRef: 'ORD-2026-0023',
    reasonCode: 'PRECEDENCE_WAIT',
    severity: 'WARN',
    impactHours: 6,
    evidenceJson: JSON.stringify({
      evidenceType: 'PRECEDENCE',
      summary: '前序任务 T-1001 延期导致开工推迟'
    }),
    occurredAt: ISO_HOUR_AGO()
  },
  {
    factId: 4,
    planVersionId,
    objectType: 'STAGE',
    objectRef: 'STG-AS-02',
    reasonCode: 'RESOURCE_CAPACITY_WAIT',
    severity: 'INFO',
    impactHours: 0,
    evidenceJson: JSON.stringify({
      evidenceType: 'CROSS_DOMAIN_RESOURCE_BLOCK',
      summary: '跨 Domain TEST 占用 STG-AS-02 至 9-4'
    }),
    occurredAt: ISO_4H_AGO()
  },
  {
    factId: 5,
    planVersionId,
    objectType: 'TASK',
    objectRef: 'T-1002',
    reasonCode: 'ROUTING_FALLBACK',
    severity: 'WARN',
    impactHours: 3,
    evidenceJson: JSON.stringify({
      evidenceType: 'ROUTING',
      summary: 'Routing 资格降级到次优资源'
    }),
    occurredAt: ISO_HOUR_AGO()
  },
  // 字典完整性补全：5号位 PhaseThreeDiagnostics 暂不产出但冻结文档列出的 10 码
  {
    factId: 6,
    planVersionId,
    objectType: 'ORDER',
    objectRef: 'ORD-2026-0024',
    reasonCode: 'DUE_DATE_RISK',
    severity: 'WARN',
    impactHours: 8,
    evidenceJson: JSON.stringify({ evidenceType: 'DUE_DATE', summary: '订单交期风险' }),
    occurredAt: ISO_HOUR_AGO()
  },
  {
    factId: 7,
    planVersionId,
    objectType: 'ORDER',
    objectRef: 'ORD-2026-0025',
    reasonCode: 'PRIORITY_LOWER_THAN_OTHERS',
    severity: 'INFO',
    impactHours: 0,
    evidenceJson: JSON.stringify({ evidenceType: 'PRIORITY', summary: '优先级低于 VIP 订单' }),
    occurredAt: ISO_4H_AGO()
  }
]

const mockBusinessIssues = (): BusinessFactIssue[] => [
  {
    issueId: 1,
    source: 'BOM_WORKSET',
    issueType: 'BOM_MISSING',
    severity: 'WARN',
    detail: 'BOM-2026-X 缺 M-008 子件',
    materialCode: 'M-008',
    documentNo: 'BOM-2026-X',
    reviewStatus: 'PENDING',
    occurredAt: ISO_HOUR_AGO()
  },
  {
    issueId: 2,
    source: 'PI_POSITION',
    issueType: 'PI_POSITION_MISMATCH',
    severity: 'WARN',
    detail: 'PI-2026-3320 缺 XC 数量，Stage-3 不可定位',
    documentNo: 'PI-2026-3320',
    stageCode: 'STG-3',
    reviewStatus: 'PENDING',
    occurredAt: ISO_4H_AGO()
  }
]

/**
 * mock ExplanationViewDto（与 lps/LPS.APS.Core/Dto/ExplanationDtos.cs :48 ExplanationSummaryDto 字段对齐）
 *  - scheduleExplanationFacts = items
 *  - errorCount / warnCount / infoCount = Summary 端点权威数
 *  - reasonCodeCounts = Summary 端点按 15 码分布
 *  - businessFactIssues = mockBusinessIssues（mock 模式独立填充）
 */
const mockExplanation = (planVersionId: number): ExplanationViewDto => {
  const facts = mockFacts(planVersionId)
  const errorCount = facts.filter((f) => f.severity === 'ERROR').length
  const warnCount = facts.filter((f) => f.severity === 'WARN').length
  const infoCount = facts.filter((f) => f.severity === 'INFO').length

  // reasonCodeCounts = 按 reasonCode group
  const byCode = new Map<string, { count: number; totalImpactHours: number }>()
  facts.forEach((f) => {
    const cur = byCode.get(f.reasonCode) ?? { count: 0, totalImpactHours: 0 }
    cur.count += 1
    cur.totalImpactHours += f.impactHours ?? 0
    byCode.set(f.reasonCode, cur)
  })
  const reasonCodeCounts = Array.from(byCode.entries()).map(([reasonCode, v]) => ({
    reasonCode: reasonCode as ScheduleExplanationFact['reasonCode'],
    count: v.count,
    totalImpactHours: v.totalImpactHours
  }))

  return {
    planVersionId,
    scheduleExplanationFacts: facts,
    businessFactIssues: mockBusinessIssues(),
    businessFactIssuesTotal: mockBusinessIssues().length,
    businessFactIssuesPage: 1,
    businessFactIssuesPageSize: 20,
    errorCount,
    warnCount,
    infoCount,
    reasonCodeCounts
  }
}

/** 业务事实问题后端 DTO 原始字段（id 而非 issueId；createdAt 而非 occurredAt） */
interface BusinessFactIssueRawDto {
  id: number
  source: string
  issueType: string
  severity: string
  detail: string
  materialCode?: string
  factoryCode?: string
  documentNo?: string
  stageCode?: string
  affectedQuantity?: number
  degradeAction?: string
  reviewStatus: string
  reviewedBy?: string
  reviewedAt?: string
  createdAt: string
}

/**
 * business-fact-issues 端点响应包装（5号位 10-10 落地版）
 *  - 字段名 page（不是 pageIndex）—— 5号位 落地回执明确告知
 *    （@see frontNew/docs/5号位-2026-10-10-business-fact-issues分页severity排序total已落地-回执-给4号位.md）
 *  - 与 PageResult<T> 通用类型（@see types/common.ts:62-67，pageIndex 字段）不同
 *  - 与 LPS.APS.Core.Dto.PageResult<T>（RBAC/TaskSplitRuleConfig/ManualETA 同源）一致
 *  - 不用改造 PageResult<T> 全局（会触发 13 文件级联），仅在 explanation 模块适配
 */
interface BusinessFactIssueResponsePage {
  items: BusinessFactIssueRawDto[]
  total: number
  page: number
  pageSize: number
}

/** 原始 DTO → 前端 view DTO 转换（id → issueId / createdAt → occurredAt） */
function toBusinessFactIssue(r: BusinessFactIssueRawDto): BusinessFactIssue {
  return {
    issueId: r.id,
    source: r.source as BusinessFactIssue['source'],
    issueType: r.issueType,
    // 后端 4 码（INFO/WARN/ERROR/CRITICAL）→ 前端 view 用 EXPLANATION_SEVERITY_TAG 渲染，未命中走 default
    severity: r.severity as BusinessFactIssue['severity'],
    detail: r.detail,
    materialCode: r.materialCode ?? undefined,
    factoryCode: r.factoryCode ?? undefined,
    documentNo: r.documentNo ?? undefined,
    stageCode: r.stageCode ?? undefined,
    affectedQuantity: r.affectedQuantity ?? undefined,
    degradeAction: r.degradeAction ?? undefined,
    reviewStatus: r.reviewStatus as BusinessFactIssue['reviewStatus'],
    reviewedBy: r.reviewedBy ?? undefined,
    reviewedAt: r.reviewedAt ?? undefined,
    occurredAt: r.createdAt
  }
}

export const explanationApi = {
  /**
   * 异常与原因解释（页面 6）
   *  - 5号位 Summary 端点：`GET /api/explanation/summary?planVersionId=`
   *  - 后端返 ExplanationSummaryDto（@see lps/LPS.APS.Core/Dto/ExplanationDtos.cs :48-66）
   *  - 4号位 ExplanationViewDto = Summary + 聚合的 businessFactIssues（来自 `/api/business-fact-issues` 独立端点）
   *  - 10-10 收口：撤 rescheduleRecommendations / equipmentFailures（冻结基线无要求）
   *
   * 10-09 修 400 bug：原 `apsHttp.get({url: '/api/explanation/summary'})` 漏 planVersionId query → 改 params
   *
   * 10-10 业务事实问题改分页：pageIndex=1, pageSize=20（对齐 Audit.vue 默认）
   *  - 数据按设计跨版本（@see BusinessFactIssueController.cs:60-67 无 planVersionId 参数）
   *  - 后端建议改 PageResult<T> 包装（@see ./types/common.ts:62-67，TaskSplitRuleConfig 已用同模式）
   *  - 后端落地前 fallback：仍按 List<RawDto> 解析，total = items.length（保证前端 E23 绿）
   */
  async getByPlanVersion(planVersionId: number): Promise<ExplanationViewDto> {
    if (APS_USE_MOCK) return mockExplanation(planVersionId)
    const summary = await apsHttp.get<{
      planVersionId: number
      errorCount: number
      warnCount: number
      infoCount: number
      reasonCodeCounts: ReasonCodeCount[]
      items: ScheduleExplanationFact[]
    }>({
      url: '/api/explanation/summary',
      params: { planVersionId }
    })
    // 业务事实问题首查（pageIndex=1, pageSize=20；5号位 10-10 落地 PageResult{page} 包装）
    // 注：PageResult<T> 通用类型字段名是 pageIndex（@see types/common.ts:62-67），但
    //   后端 business-fact-issues 实际返字段是 page（5号位 落地回执明确告知，与 RBAC/TaskSplitRuleConfig/ManualETA 同源）
    //   此处用 BusinessFactIssueResponsePage（带 page 字段的子类型）覆盖
    const bizPage = await apsHttp.get<BusinessFactIssueResponsePage>({
      url: '/api/business-fact-issues',
      params: { pageIndex: 1, pageSize: 20 }
    })

    return {
      planVersionId: summary.planVersionId,
      scheduleExplanationFacts: summary.items ?? [],
      businessFactIssues: bizPage.items.map(toBusinessFactIssue),
      businessFactIssuesTotal: bizPage.total,
      businessFactIssuesPage: bizPage.page,
      businessFactIssuesPageSize: bizPage.pageSize,
      errorCount: summary.errorCount ?? 0,
      warnCount: summary.warnCount ?? 0,
      infoCount: summary.infoCount ?? 0,
      reasonCodeCounts: summary.reasonCodeCounts ?? []
    }
  },

  /**
   * 业务事实问题分页查询（10-10 新增；store loadMore 用）
   *  - 后端返 BusinessFactIssueResponsePage{page} 包装（5号位 10-10 已落地，字段 page/total/pageSize/items）
   *  - 数据按设计跨版本（不带 planVersionId 参数）
   */
  async listBusinessFactIssues(opts: {
    pageIndex: number
    pageSize: number
  }): Promise<{ items: BusinessFactIssue[]; total: number; page: number; pageSize: number }> {
    if (APS_USE_MOCK) {
      const all = mockBusinessIssues()
      const start = (opts.pageIndex - 1) * opts.pageSize
      const items = all.slice(start, start + opts.pageSize)
      return {
        items,
        total: all.length,
        page: opts.pageIndex,
        pageSize: opts.pageSize
      }
    }
    const raw = await apsHttp.get<BusinessFactIssueResponsePage>({
      url: '/api/business-fact-issues',
      params: { pageIndex: opts.pageIndex, pageSize: opts.pageSize }
    })
    return {
      items: raw.items.map(toBusinessFactIssue),
      total: raw.total,
      page: raw.page,
      pageSize: raw.pageSize
    }
  },

  /**
   * 排程解释事实列表（5号位 list 端点，含 filter）
   *  - 备查端点：本页未直接消费；保留供深钻或 store 后续扩展
   *  - 5号位 `GET /api/explanation?planVersionId=&objectType=&reasonCode=&severity=&skip=&take=`
   */
  async listFacts(opts: {
    planVersionId: number
    objectType?: string
    reasonCode?: string
    severity?: string
    skip?: number
    take?: number
  }): Promise<ScheduleExplanationFact[]> {
    if (APS_USE_MOCK) {
      const all = mockExplanation(opts.planVersionId).scheduleExplanationFacts
      return all
        .filter((f) => (opts.objectType ? f.objectType === opts.objectType : true))
        .filter((f) => (opts.reasonCode ? f.reasonCode === opts.reasonCode : true))
        .filter((f) => (opts.severity ? f.severity === opts.severity : true))
        .slice(opts.skip ?? 0, (opts.skip ?? 0) + (opts.take ?? 100))
    }
    return apsHttp.get<ScheduleExplanationFact[]>({
      url: '/api/explanation',
      params: { ...opts }
    })
  }
}
