/**
 * APS V1 4号位 — 订单 Pinia store（页面 2 订单/需求计划）
 *
 * 状态：
 *  - list / listTotal / listLoading / listError：分页列表
 *  - summary：5 种订单状态聚合（顶部 KPI）
 *  - currentDetail：当前打开详情的订单
 *  - query：当前过滤条件（planVersionId 必填，由 defaultPlanVersionId 兜底；详见 5号位 2026-09-23 回执）
 *
 * 必填 planVersionId（5号位 2026-09-23《order-query 分页契约修正回执》）：
 *  - 后端 OrderQueryController.QueryOrders 签名 `[FromQuery] int planVersionId` 非空、无默认值 = 必填
 *  - 前端 store 在 loadList 前校验；缺则不发起请求（抛 400 类错误到 listError）
 *  - defaultPlanVersionId 由 Order.vue onMounted 从 overviewApi.getOverview().activeVersions[0] 取首个填入
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  orderApi,
  ApiError,
  type OrderBasicInfo,
  type OrderQuery,
  type OrderScheduleDetailDto,
  type OrderSummaryStatus
} from '@/api/aps-v1'

export const useOrderStore = defineStore('aps.order', () => {
  // ===== state =====
  const list = ref<OrderBasicInfo[]>([])
  const listTotal = ref(0)
  const pageIndex = ref(1)
  const pageSize = ref(10)
  const listLoading = ref(false)
  const listError = ref<string | null>(null)

  const summary = ref<{ status: OrderSummaryStatus; count: number }[]>([])
  const summaryLoading = ref(false)

  const currentDetail = ref<OrderScheduleDetailDto | null>(null)
  const detailLoading = ref(false)
  const detailError = ref<string | null>(null)

  /**
   * 当前过滤条件；planVersionId 必填但运行时可空（默认兜底）
   *  - 类型 Partial 是因为用户在 UI 可清空输入框；store 用 effectivePlanVersionId 兜底
   *  - 提交时（loadList）planVersionIdReady 校验；缺则抛错不发请求
   */
  const query = ref<Partial<Omit<OrderQuery, 'pageIndex' | 'pageSize'>>>({})

  /**
   * 兜底 planVersionId（5号位 2026-09-23 回执：planVersionId 漏传 → 后端 400）
   *  - Order.vue onMounted 调 overviewApi.getOverview() 取首个 activeVersions[0].planVersionId 写入
   *  - query.planVersionId 缺值时，loadList 兜底用此值；仍缺 → 抛错（红字提示用户）
   *  - 不为 0/负数/NaN（与后端 `int planVersionId` 校验对齐）
   */
  const defaultPlanVersionId = ref<number | null>(null)

  /** 当前 effective planVersionId（用户输入值优先，否则取 default；用于 UI 显示 + 接口兜底） */
  const effectivePlanVersionId = computed<number | null>(() => {
    const userVal = query.value.planVersionId
    if (typeof userVal === 'number' && Number.isFinite(userVal) && userVal > 0) {
      return userVal
    }
    return defaultPlanVersionId.value
  })

  /** 当前 planVersionId 是否就绪（用户输入 或 default 已填） */
  const planVersionIdReady = computed<boolean>(
    () => effectivePlanVersionId.value !== null && effectivePlanVersionId.value > 0
  )

  // ===== getters =====
  const filteredTotalLabel = computed(() => `共 ${listTotal.value} 条`)

  // ===== actions =====
  async function loadList(resetPage = false): Promise<void> {
    if (resetPage) pageIndex.value = 1
    // 必填 planVersionId 校验（5号位 2026-09-23 回执 §一：缺则后端 400）
    if (!planVersionIdReady.value) {
      const fallback: ApiError = new ApiError(
        'PlanVersionId 必填（5号位 2026-09-23 回执：订单按 PlanVersion 分区）— 请等待总览页加载或在输入框手动填写',
        400
      )
      handleError(listError, fallback)
      ElMessage.warning('PlanVersionId 必填：请先加载排产总览或手动输入 PlanVersionId')
      return
    }
    listLoading.value = true
    listError.value = null
    try {
      const res = await orderApi.list({
        ...query.value,
        planVersionId: effectivePlanVersionId.value as number,
        pageIndex: pageIndex.value,
        pageSize: pageSize.value
      })
      list.value = res.items
      listTotal.value = res.total
    } catch (err) {
      handleError(listError, err)
    } finally {
      listLoading.value = false
    }
  }

  async function loadSummary(): Promise<void> {
    summaryLoading.value = true
    try {
      summary.value = await orderApi.getSummary()
    } catch (err) {
      // summary 顶部 KPI 不阻断列表，只静默
      console.warn('[order.store] loadSummary failed', err)
    } finally {
      summaryLoading.value = false
    }
  }

  async function loadDetail(orderId: number): Promise<void> {
    detailLoading.value = true
    detailError.value = null
    try {
      currentDetail.value = await orderApi.getDetail(orderId)
    } catch (err) {
      handleError(detailError, err)
    } finally {
      detailLoading.value = false
    }
  }

  function setFilter(next: Partial<typeof query.value>): void {
    query.value = { ...query.value, ...next }
  }

  function clearFilter(): void {
    query.value = {}
  }

  function setPage(p: { pageIndex?: number; pageSize?: number }): void {
    if (p.pageIndex !== undefined) pageIndex.value = p.pageIndex
    if (p.pageSize !== undefined) pageSize.value = p.pageSize
  }

  function reset(): void {
    list.value = []
    listTotal.value = 0
    currentDetail.value = null
    summary.value = []
    query.value = {}
    pageIndex.value = 1
    pageSize.value = 10
    listError.value = null
    detailError.value = null
  }

  /** 设置默认 PlanVersionId（Order.vue onMounted 从 overview 取首个填入）
   *  - 仅在 defaultPlanVersionId 缺值时设置；避免响应触发 watch 误覆盖用户输入
   */
  function setDefaultPlanVersionId(id: number | null): void {
    if (id !== null && Number.isFinite(id) && id > 0) {
      defaultPlanVersionId.value = id
    }
  }

  function handleError(target: typeof listError, err: unknown): void {
    if (err instanceof ApiError) {
      target.value =
        `[${err.code}] ${err.message}` + (err.traceId ? ` (traceId=${err.traceId})` : '')
    } else {
      target.value = (err as Error)?.message ?? '未知错误'
    }
  }

  return {
    list,
    listTotal,
    pageIndex,
    pageSize,
    listLoading,
    listError,
    summary,
    summaryLoading,
    currentDetail,
    detailLoading,
    detailError,
    query,
    defaultPlanVersionId,
    effectivePlanVersionId,
    planVersionIdReady,
    filteredTotalLabel,
    loadList,
    loadSummary,
    loadDetail,
    setFilter,
    clearFilter,
    setPage,
    setDefaultPlanVersionId,
    reset
  }
})
