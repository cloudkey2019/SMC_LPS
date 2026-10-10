/**
 * APS V1 4号位 — Setup 0 分钟兜底统计 Pinia store（v1.5 Setup 专项 §5）
 *
 * 状态：
 *  - stats：0 分钟兜底统计列表
 *  - query：查询参数（runId + 可选筛选条件）
 *  - loading 系列：UI 态
 *
 * 权限：
 *  - aps.setup.view → 查看（只读页面，无写操作）
 *
 * 红线（§5.5）：
 *  - 不在前端聚合（性能问题，调用后端聚合端点）
 *  - 不展示 Run 外的统计（避免与 RuleSetVersion 解耦）
 *
 * 出口操作（§5.4）：
 *  - 点击"去补 EXACT" → 跳转页面 2.1，预填 4 字段
 *  - 点击"去补 DEFAULT" → 跳转页面 2.2，预填 3 字段
 *  - 导出 CSV
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  setupApi,
  ApiError,
  type SetupUncoveredStatDto,
  type SetupUncoveredQuery
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

export const useSetupUncoveredStore = defineStore('aps.setupUncovered', () => {
  // ===== state =====
  const stats = ref<SetupUncoveredStatDto[]>([])
  const query = ref<SetupUncoveredQuery | null>(null)

  const listLoading = ref(false)
  const error = ref<string | null>(null)

  // ===== auth =====
  const apsAuth = useApsAuthStore()

  // ===== getters =====

  /** 查看权限 */
  const canView = computed<boolean>(() => apsAuth.has('aps.setup.view'))

  /** 统计总数 */
  const statCount = computed<number>(() => stats.value.length)

  /** 按部门分组统计 */
  const statsByDepartment = computed<Record<string, SetupUncoveredStatDto[]>>(() => {
    const groups: Record<string, SetupUncoveredStatDto[]> = {}
    stats.value.forEach((s) => {
      if (!groups[s.departmentCode]) groups[s.departmentCode] = []
      groups[s.departmentCode].push(s)
    })
    return groups
  })

  /** 按大工艺分组统计 */
  const statsByStage = computed<Record<string, SetupUncoveredStatDto[]>>(() => {
    const groups: Record<string, SetupUncoveredStatDto[]> = {}
    stats.value.forEach((s) => {
      const key = `${s.departmentCode}/${s.stageCode}`
      if (!groups[key]) groups[key] = []
      groups[key].push(s)
    })
    return groups
  })

  // ===== actions =====

  function setQuery(newQuery: SetupUncoveredQuery): void {
    query.value = newQuery
  }

  async function loadStats(newQuery: SetupUncoveredQuery): Promise<void> {
    listLoading.value = true
    error.value = null
    query.value = newQuery
    try {
      stats.value = await setupApi.getUncoveredStats(newQuery)
    } catch (err) {
      handleError(err)
      stats.value = []
    } finally {
      listLoading.value = false
    }
  }

  /** 导出 CSV（前端生成，不调后端） */
  function exportCsv(): string {
    const headers = [
      'DepartmentCode',
      'StageCode',
      'OperationCode',
      'ResourceCode',
      'FromMaterialCode',
      'ToMaterialCode',
      'Count',
      'LastOccurrence'
    ]
    const rows = stats.value.map((s) => [
      s.departmentCode,
      s.stageCode,
      s.operationCode,
      s.resourceCode,
      s.fromMaterialCode,
      s.toMaterialCode,
      String(s.count),
      s.lastOccurrence ?? ''
    ])
    return [headers.join(','), ...rows.map((r) => r.join(','))].join('\n')
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
    stats.value = []
    query.value = null
    error.value = null
  }

  return {
    // state
    stats,
    query,
    listLoading,
    error,
    // getters
    canView,
    statCount,
    statsByDepartment,
    statsByStage,
    // actions
    setQuery,
    loadStats,
    exportCsv,
    handleError,
    reset
  }
})
