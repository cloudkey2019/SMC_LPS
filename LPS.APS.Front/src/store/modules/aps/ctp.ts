/**
 * APS V1 4号位 — CTP / 插单评估 Pinia store（页面 4）
 *
 * 状态：
 *  - input：评估输入（orderCanonicalId / materialCode / quantity / factoryCode / requestedDueDate / purpose）
 *  - result：评估结果
 *  - history：本会话内的历史评估（最多保留 5 条，方便切换对比）
 *  - loading / error：UI 态
 *
 * 注意：
 *  - 4号位文档第 7 节 / U11：CTP 不暴露"采用"按钮，只展示结果
 *  - 写入通过 Controller（ctpApi.evaluate）发起，不直接改库
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { ctpApi, ApiError, type CtpInput, type CtpResultDto } from '@/api/aps-v1'

const defaultInput = (): CtpInput => ({
  orderCanonicalId: '',
  materialCode: '',
  quantity: 100,
  factoryCode: 'F-SUZ-01',
  requestedDueDate: new Date(Date.now() + 10 * 86400_000).toISOString().slice(0, 10),
  purpose: 'CTP'
})

export interface CtpHistoryEntry {
  id: number
  input: CtpInput
  result: CtpResultDto
  evaluatedAt: string
}

let historySeq = 1

export const useCtpStore = defineStore('aps.ctp', () => {
  // ===== state =====
  const input = ref<CtpInput>(defaultInput())
  const result = ref<CtpResultDto | null>(null)
  const history = ref<CtpHistoryEntry[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ===== getters =====
  const hasResult = computed(() => result.value !== null)
  const isOverThreshold = computed(
    () =>
      result.value !== null &&
      result.value.impactedOrderCount > result.value.maxImpactedOrdersThreshold
  )

  // ===== actions =====
  async function evaluate(next?: Partial<CtpInput>): Promise<void> {
    if (next) input.value = { ...input.value, ...next }
    loading.value = true
    error.value = null
    try {
      const dto = await ctpApi.evaluate(input.value)
      result.value = dto
      history.value.unshift({
        id: historySeq++,
        input: { ...input.value },
        result: dto,
        evaluatedAt: new Date().toISOString()
      })
      if (history.value.length > 5) history.value = history.value.slice(0, 5)
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  function loadFromHistory(entry: CtpHistoryEntry): void {
    input.value = { ...entry.input }
    result.value = entry.result
  }

  function reset(): void {
    input.value = defaultInput()
    result.value = null
    error.value = null
  }

  function clearHistory(): void {
    history.value = []
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
    input,
    result,
    history,
    loading,
    error,
    hasResult,
    isOverThreshold,
    evaluate,
    loadFromHistory,
    reset,
    clearHistory,
    handleError
  }
})
