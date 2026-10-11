/**
 * APS V1 4号位 — 异常与原因解释 Pinia store（页面 6）
 *
 * 2026-10-09 改造（按 5号位 函件 R1 回执）：
 *  - 字典对齐 v5.1.10 §八.1 15 码（ReasonCode）+ 3 码（Severity INFO/WARN/ERROR）+ 5 码（ObjectType）
 *  - 撤 scheduleFailureInfo 相关 getter（5号位 关键纠正 3「后端无此实体」）
 *  - errorCount / warnCount / infoCount 来自 Summary 端点权威数（取代旧 derived severityCounts）
 *
 * 2026-10-10 改造（业务事实问题分页 + 缓存 + severity 排序兜底）：
 *  - 加 4 ref：businessFactIssues / Total / Page / PageSize
 *  - 加 businessFactIssuesLoading / businessFactIssuesCached（缓存标记）
 *  - load(id?) 改造：版本切换不重发 biz-issues（仅首次 / 显式 refresh 才拉）
 *  - 新增 actions：fetchBizPage() / refreshBizIssues() / changeBizPageSize()
 *  - 缓存失效策略：仅「刷新」按钮；版本切换保留缓存
 *  - 排序兜底：sortedBusinessFactIssues getter（severity desc + occurredAt desc）
 *
 * 2026-10-10 续改：分页 UI 标准 ElPagination（翻页替换）
 *  - fetchBizPage 按 page 替换（不累积）
 *  - 视图用 ElPagination（@see views/Aps/Explanation.vue）
 *
 * 状态：
 *  - data：ExplanationViewDto（来自 Summary 端点；含 biz-issues 第一页）
 *  - loading / error：UI 态
 *  - planVersionId：当前选中的版本（默认 1001，dev seed）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  ApiError,
  explanationApi,
  type BusinessFactIssue,
  type ExplanationObjectType,
  type ExplanationViewDto,
  type ScheduleExplanationFact
} from '@/api/aps-v1'

export const useExplanationStore = defineStore('aps.explanation', () => {
  // ===== state =====
  const planVersionId = ref<number>(1001)
  const data = ref<ExplanationViewDto | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ===== 业务事实问题分页状态（10-10 增；10-10 后端落地后字段名 page） =====
  /** 当前页数据（每次分页跳转后整体替换；不再累积） */
  const businessFactIssues = ref<BusinessFactIssue[]>([])
  /** 后端 PageResult.total；首查后权威 */
  const businessFactIssuesTotal = ref<number>(0)
  /** 当前页码（首屏 1；翻页跳指定页）
   *  字段名 page：5号位 10-10 落地回执明确告知（@see 5号位-2026-10-10-business-fact-issues分页severity排序total已落地-回执-给4号位.md）
   *  与 LPS.APS.Core.Dto.PageResult<T>.page 一致（RBAC/TaskSplitRuleConfig/ManualETA 同源）
   */
  const businessFactIssuesPage = ref<number>(1)
  /** 每页条数（默认 20，对齐 Audit.vue；10/20/50 可选） */
  const businessFactIssuesPageSize = ref<number>(20)
  /** 业务事实问题加载态（与 loading 分离，独立 spinner） */
  const businessFactIssuesLoading = ref<boolean>(false)
  /** 缓存标记：false = 首次或已 invalidate → load() 触发首查；true = 命中缓存 → 不重查 */
  const businessFactIssuesCached = ref<boolean>(false)

  // ===== getters =====

  /**
   * 顶部 KPI（来自 Summary 端点权威数，@see ExplanationSummaryDto.errorCount / warnCount / infoCount）
   *  - 5号位 关键纠正 3 后：3 计数取代旧 derived 4 计数（CRITICAL 已撤）
   */
  const severityCounts = computed(() => {
    const d = data.value
    return {
      ERROR: d?.errorCount ?? 0,
      WARN: d?.warnCount ?? 0,
      INFO: d?.infoCount ?? 0
    }
  })

  /** 排程解释事实总数（来自 data.scheduleExplanationFacts.length） */
  const totalFacts = computed(() => data.value?.scheduleExplanationFacts?.length ?? 0)

  /**
   * 按根因 objectType 分组的解释事实（5 码：v5.1.10 :4858）
   *  - 取代表旧 6 码（TASK/ORDER/PI/PO/RESOURCE/STAGE）→ 新 5 码（ORDER/TASK/RESOURCE/STAGE/DOMAIN）
   */
  const factsByObjectType = computed(() => {
    const facts = data.value?.scheduleExplanationFacts ?? []
    const groups: Record<ExplanationObjectType, ScheduleExplanationFact[]> = {
      ORDER: [],
      TASK: [],
      RESOURCE: [],
      STAGE: [],
      DOMAIN: []
    }
    facts.forEach((f) => {
      if (groups[f.objectType]) groups[f.objectType].push(f)
    })
    // 按 severity 倒序排（ERROR > WARN > INFO）
    const sevOrder = { ERROR: 0, WARN: 1, INFO: 2 } as const
    Object.keys(groups).forEach((k) => {
      const key = k as ExplanationObjectType
      groups[key].sort((a, b) => sevOrder[a.severity] - sevOrder[b.severity])
    })
    return groups
  })

  /**
   * 视图用排序：severity desc（ERROR > WARN > INFO）+ occurredAt desc（10-10 增）
   *  - 双保险：即使 5号位 未落地 ORDER BY severity 排序，关键 ERROR 也在前
   *  - 后端 4 码 severity 含 CRITICAL，未命中字典时排到最后
   */
  const sortedBusinessFactIssues = computed(() => {
    const sevOrder: Record<string, number> = { ERROR: 0, WARN: 1, INFO: 2, CRITICAL: 0 }
    return [...businessFactIssues.value].sort((a, b) => {
      const sa = sevOrder[a.severity] ?? 99
      const sb = sevOrder[b.severity] ?? 99
      if (sa !== sb) return sa - sb
      return (b.occurredAt ?? '').localeCompare(a.occurredAt ?? '')
    })
  })

  // ===== actions =====

  /**
   * 主 load：summary 每次必重 fetch；business-fact-issues 仅当未缓存时执行首查
   *  - 版本切换 / 路由重 mount：summary 重拉；biz-issues 复用缓存
   *  - 显式 refreshBizIssues() 调用：invalidate 后重拉
   */
  async function load(id?: number): Promise<void> {
    if (id !== undefined) planVersionId.value = id
    loading.value = true
    error.value = null
    try {
      data.value = await explanationApi.getByPlanVersion(planVersionId.value)
      // 同步首查 biz-issues 到本地累积 ref（store 层持有，供 loadMore 累加）
      businessFactIssues.value = data.value.businessFactIssues ?? []
      businessFactIssuesTotal.value = data.value.businessFactIssuesTotal ?? 0
      businessFactIssuesPage.value = data.value.businessFactIssuesPage ?? 1
      businessFactIssuesPageSize.value = data.value.businessFactIssuesPageSize ?? 20
      businessFactIssuesCached.value = true
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  /**
   * 拉取指定页（替换式，不累积；10-10 续改：标准 ElPagination 用）
   *  - 替换 businessFactIssues 当页数据；Total / Page / PageSize 同步
   *  - 任意页调用都成功：cached = true
   *  - ElPagination 控件 v-model 双向绑定 page / pageSize 后 @current-change / @size-change 调到此处
   *  - 数据按设计跨版本（@see BusinessFactIssueController.cs 无 planVersionId 参数）
   */
  async function fetchBizPage(page: number, size: number): Promise<void> {
    if (businessFactIssuesLoading.value) return
    if (page < 1) page = 1
    businessFactIssuesLoading.value = true
    try {
      const res = await explanationApi.listBusinessFactIssues({ pageIndex: page, pageSize: size })
      businessFactIssues.value = res.items
      businessFactIssuesTotal.value = res.total
      businessFactIssuesPage.value = res.page
      businessFactIssuesPageSize.value = res.pageSize
      businessFactIssuesCached.value = true
    } catch (err) {
      handleError(err)
      // 不抛出：避免阻塞 Explanation 页（用户仍可看 summary / 排程解释事实）
    } finally {
      businessFactIssuesLoading.value = false
    }
  }

  /**
   * 显式刷新（用户点顶部"刷新"按钮）
   *  - 重置到第一页；保持当前 pageSize 不变
   *  - 失败时缓存标记回退到 false（下一次 load 会重拉）
   *  - 与 fetchBizPage 区别：失败时显式清缓存（用户主动语义）
   *    实现独立保留（fetchBizPage 不抛错；refresh 需要失败感知）
   */
  async function refreshBizIssues(): Promise<void> {
    businessFactIssuesLoading.value = true
    try {
      const res = await explanationApi.listBusinessFactIssues({
        pageIndex: 1,
        pageSize: businessFactIssuesPageSize.value
      })
      businessFactIssues.value = res.items
      businessFactIssuesTotal.value = res.total
      businessFactIssuesPage.value = res.page
      businessFactIssuesPageSize.value = res.pageSize
      businessFactIssuesCached.value = true
    } catch (err) {
      businessFactIssuesCached.value = false
      handleError(err)
    } finally {
      businessFactIssuesLoading.value = false
    }
  }

  /**
   * 修改 pageSize（ElPagination @size-change）
   *  - pageSize 变更后回退到第 1 页（ElPagination 内置语义）
   *  - 委派给 fetchBizPage(1, size)
   */
  async function changeBizPageSize(size: number): Promise<void> {
    if (size === businessFactIssuesPageSize.value) return
    businessFactIssuesPageSize.value = size
    await fetchBizPage(1, size)
  }

  function handleError(err: unknown): void {
    if (err instanceof ApiError) {
      error.value =
        `[${err.code}] ${err.message}` + (err.traceId ? ` (traceId=${err.traceId})` : '')
    } else {
      error.value = (err as Error)?.message ?? '未知错误'
    }
  }

  function reset(): void {
    data.value = null
    error.value = null
    businessFactIssues.value = []
    businessFactIssuesTotal.value = 0
    businessFactIssuesPage.value = 1
    businessFactIssuesCached.value = false
  }

  return {
    planVersionId,
    data,
    loading,
    error,
    severityCounts,
    totalFacts,
    factsByObjectType,
    businessFactIssues,
    businessFactIssuesTotal,
    businessFactIssuesPage,
    businessFactIssuesPageSize,
    businessFactIssuesLoading,
    businessFactIssuesCached,
    sortedBusinessFactIssues,
    load,
    fetchBizPage,
    refreshBizIssues,
    changeBizPageSize,
    reset
  }
})
