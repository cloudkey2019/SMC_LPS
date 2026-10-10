/**
 * APS V1 4号位 — 异常与原因解释 Pinia store（页面 6）
 *
 * 状态：
 *  - data：ExplanationViewDto
 *  - loading / error：UI 态
 *  - planVersionId：当前选中的版本（默认 1001）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  explanationApi,
  ApiError,
  type ExplanationViewDto,
  type ScheduleExplanationFact
} from '@/api/aps-v1'

export const useExplanationStore = defineStore('aps.explanation', () => {
  // ===== state =====
  const planVersionId = ref<number>(1001)
  const data = ref<ExplanationViewDto | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ===== getters =====
  /** 按严重度分组聚合（顶部 KPI 用） */
  const severityCounts = computed(() => {
    const facts = data.value?.scheduleExplanationFacts ?? []
    const counts = { INFO: 0, WARNING: 0, ERROR: 0, CRITICAL: 0 }
    facts.forEach((f) => {
      counts[f.severity] = (counts[f.severity] ?? 0) + 1
    })
    return counts
  })

  /** 按根因 objectType 分组的解释事实 */
  const factsByObjectType = computed(() => {
    const facts = data.value?.scheduleExplanationFacts ?? []
    const groups: Record<ScheduleExplanationFact['objectType'], ScheduleExplanationFact[]> = {
      TASK: [],
      ORDER: [],
      PI: [],
      PO: [],
      RESOURCE: [],
      STAGE: []
    }
    facts.forEach((f) => groups[f.objectType].push(f))
    // 按 severity 倒序排
    const sevOrder = { CRITICAL: 0, ERROR: 1, WARNING: 2, INFO: 3 }
    Object.keys(groups).forEach((k) => {
      groups[k as keyof typeof groups].sort((a, b) => sevOrder[a.severity] - sevOrder[b.severity])
    })
    return groups
  })

  // ===== actions =====
  async function load(id?: number): Promise<void> {
    if (id !== undefined) planVersionId.value = id
    loading.value = true
    error.value = null
    try {
      data.value = await explanationApi.getByPlanVersion(planVersionId.value)
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
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
  }

  return {
    planVersionId,
    data,
    loading,
    error,
    severityCounts,
    factsByObjectType,
    load,
    reset
  }
})
