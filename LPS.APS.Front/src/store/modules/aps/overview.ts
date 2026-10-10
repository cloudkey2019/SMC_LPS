/**
 * APS V1 4号位 — 排产总览 Pinia store
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'
import { overviewApi, ApiError, type PlanOverviewDto } from '@/api/aps-v1'

export const useOverviewStore = defineStore('aps.overview', () => {
  const data = ref<PlanOverviewDto | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function load(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      data.value = await overviewApi.getOverview()
    } catch (err) {
      if (err instanceof ApiError) error.value = `[${err.code}] ${err.message}`
      else error.value = (err as Error)?.message ?? '加载总览失败'
    } finally {
      loading.value = false
    }
  }

  return { data, loading, error, load }
})
