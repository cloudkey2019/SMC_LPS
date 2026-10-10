/**
 * APS V1 4号位 — PI Position / 供给追溯 Pinia store（页面 7）
 *
 * 状态：
 *  - list：PI 列表
 *  - currentDetail：当前选中 PI 的详情
 *  - filter：列表过滤（domainKey / positionType / materialCode）
 *  - loading / error：UI 态
 *
 * 设计要点：
 *  - U16：UNLOCATED 数量必须显示，并标注"不可下发 MES"
 *  - U17：无 PI 规划 Task 显示不可 MES
 *  - U18：依赖 PLANNING_PURCHASE_PLACEHOLDER 的 Task 不允许下 MES
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  piApi,
  ApiError,
  type PiListItem,
  type PiListFilter,
  type PiPositionViewDto,
  type PiPositionType,
  type SupplyType,
  type DomainKey
} from '@/api/aps-v1'

export const usePiStore = defineStore('aps.pi', () => {
  // ===== state =====
  const list = ref<PiListItem[]>([])
  const currentDetail = ref<PiPositionViewDto | null>(null)
  // 2026-09-17 5号位 回执后：详情端点用 productionInstructionNo（字符串）
  const currentId = ref<string | null>(null)
  const detailOpen = ref(false)
  const filter = ref<PiListFilter>({
    domainKey: 'ALL',
    positionType: 'ALL',
    materialCode: ''
  })
  const loading = ref(false)
  const listLoading = ref(false)
  const error = ref<string | null>(null)

  // ===== getters =====
  /** 按 Position Type 聚合（顶部 KPI 用） */
  const positionTypeCounts = computed(() => {
    const c: Record<PiPositionType, number> = {
      ERP_REMAINING: 0,
      PRODUCTION: 0,
      TRANSIT: 0,
      WAITING: 0,
      UNLOCATED: 0,
      CROSS_STAGE: 0
    }
    list.value.forEach((p) => {
      c[p.mainPositionType] = (c[p.mainPositionType] ?? 0) + 1
    })
    return c
  })

  /** MES 资格分布 */
  const mesStats = computed(() => {
    const c = { ELIGIBLE: 0, INELIGIBLE: 0, UNKNOWN: 0 }
    list.value.forEach((p) => {
      c[p.mesEligible] = (c[p.mesEligible] ?? 0) + 1
    })
    return c
  })

  /** 总数 + 异常数 */
  const totalCount = computed(() => list.value.length)
  const issueCount = computed(() => list.value.filter((p) => p.issueCount > 0).length)
  const unlocatedCount = computed(() => list.value.filter((p) => p.hasUnlocated).length)
  const placeholderCount = computed(
    () => list.value.filter((p) => p.hasPlaceholderDependency).length
  )

  /** 当前详情 Position 合计 vs ERP 差异 */
  const variance = computed(() => {
    if (!currentDetail.value) return 0
    return currentDetail.value.variance
  })

  /** 当前详情 Supply 类型分布 */
  const supplyTypeCounts = computed(() => {
    const c: Record<SupplyType, number> = {
      INVENTORY: 0,
      PI: 0,
      PO: 0,
      VMI: 0,
      ARRIVED_NOT_INBOUND: 0,
      INTERPLANT_TRANSIT: 0,
      RECEIVED: 0,
      PLANNED_PRODUCTION: 0,
      PLANNING_PURCHASE_PLACEHOLDER: 0
    }
    if (!currentDetail.value) return c
    currentDetail.value.supplies.forEach((s) => {
      c[s.supplyType] = (c[s.supplyType] ?? 0) + s.qty
    })
    return c
  })

  // ===== actions =====
  async function loadList(): Promise<void> {
    listLoading.value = true
    error.value = null
    try {
      list.value = await piApi.list(filter.value)
    } catch (err) {
      handleError(err)
    } finally {
      listLoading.value = false
    }
  }

  // 2026-09-17 5号位 回执后：详情端点用 productionInstructionNo（字符串），非 ID
  async function loadDetail(productionInstructionNo: string): Promise<void> {
    currentId.value = productionInstructionNo
    detailOpen.value = true
    loading.value = true
    error.value = null
    try {
      currentDetail.value = await piApi.getById(productionInstructionNo)
    } catch (err) {
      handleError(err)
      currentDetail.value = null
    } finally {
      loading.value = false
    }
  }

  function closeDetail(): void {
    detailOpen.value = false
  }

  function setDomainFilter(v: DomainKey | 'ALL'): void {
    filter.value = { ...filter.value, domainKey: v }
  }

  function setPositionFilter(v: PiPositionType | 'ALL'): void {
    filter.value = { ...filter.value, positionType: v }
  }

  function setMaterialFilter(v: string): void {
    filter.value = { ...filter.value, materialCode: v }
  }

  function resetFilter(): void {
    filter.value = { domainKey: 'ALL', positionType: 'ALL', materialCode: '' }
  }

  function handleError(err: unknown): void {
    if (err instanceof ApiError) {
      error.value =
        `[${err.code}] ${err.message}` + (err.traceId ? ` (traceId=${err.traceId})` : '')
    } else {
      error.value = (err as Error)?.message ?? '未知错误'
    }
  }

  return {
    // state
    list,
    currentDetail,
    currentId,
    detailOpen,
    filter,
    loading,
    listLoading,
    error,
    // getters
    positionTypeCounts,
    mesStats,
    totalCount,
    issueCount,
    unlocatedCount,
    placeholderCount,
    variance,
    supplyTypeCounts,
    // actions
    loadList,
    loadDetail,
    closeDetail,
    setDomainFilter,
    setPositionFilter,
    setMaterialFilter,
    resetFilter,
    handleError
  }
})
