/**
 * APS V1 4号位 — 排程数据 Pinia store
 *
 * 状态：
 *  - versions：计划版本列表
 *  - currentVersionId：当前选中的版本
 *  - ganttData：当前版本的甘特图数据
 *  - summary：当前版本的 KPI 概要
 *  - loading/error：UI 态（4号位文档第 20 节：UI 态 ≠ 业务态）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  scheduleApi,
  ApiError,
  type GanttDataDto,
  type PlanVersionSummaryDto,
  type ScheduleSummaryDto
} from '@/api/aps-v1'

export const useScheduleStore = defineStore('aps.schedule', () => {
  // ===== state =====
  const versions = ref<PlanVersionSummaryDto[]>([])
  const currentVersionId = ref<number | null>(null)
  const ganttData = ref<GanttDataDto | null>(null)
  const summary = ref<ScheduleSummaryDto | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ===== getters =====
  const currentVersion = computed(
    () => versions.value.find((v) => v.id === currentVersionId.value) ?? null
  )
  /** 4号位文档第 6 节：UI 态，业务层不应依赖此标志 */
  const isEmpty = computed(() => !ganttData.value || ganttData.value.tasks.length === 0)

  // ===== actions =====
  async function loadVersions(take = 30): Promise<void> {
    loading.value = true
    error.value = null
    try {
      versions.value = await scheduleApi.getVersions(take)
      // 默认选中第一个 ACTIVE 版本（4号位文档第 4 节）
      const active = versions.value.find((v) => v.status === 'ACTIVE')
      currentVersionId.value = active?.id ?? versions.value[0]?.id ?? null
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  async function selectVersion(planVersionId: number): Promise<void> {
    if (planVersionId === currentVersionId.value && ganttData.value) return
    currentVersionId.value = planVersionId
    await loadCurrent()
  }

  async function loadCurrent(): Promise<void> {
    const id = currentVersionId.value
    if (id === null) return
    loading.value = true
    error.value = null
    try {
      const [g, s] = await Promise.all([scheduleApi.getGantt(id), scheduleApi.getSummary(id)])
      ganttData.value = g
      summary.value = s
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
    versions.value = []
    currentVersionId.value = null
    ganttData.value = null
    summary.value = null
    error.value = null
  }

  return {
    versions,
    currentVersionId,
    ganttData,
    summary,
    loading,
    error,
    currentVersion,
    isEmpty,
    loadVersions,
    selectVersion,
    loadCurrent,
    reset
  }
})
