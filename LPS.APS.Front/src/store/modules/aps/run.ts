/**
 * APS V1 4号位 — 运行/版本/MES Pinia store（页面 9）
 *
 * 状态：
 *  - data：RunStatusDto
 *  - loading / error：UI 态
 *
 * 注意：4号位文档第 2.1 节：阶段 A 阶段不直接触发任何写操作
 *       recoverFailedRun 由 PMC 在阶段 B 手工触发，4号位页面只读展示
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  runApi,
  ApiError,
  type RunStatusDto,
  type ScheduleRunDto,
  type RecoverFailedRunResult,
  type DomainKey,
  type MesDispatchInput,
  type MesDispatchResult
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'
import { assertScope, ScopeViolationError } from './scope'

export const RUN_STATUS_LABEL: Record<ScheduleRunDto['status'], string> = {
  PENDING: '排队',
  RUNNING: '运行中',
  PARTIAL_SUCCESS: '部分成功',
  SUCCESS: '成功',
  FAILED: '失败'
}

export const RUN_STATUS_TAG: Record<
  ScheduleRunDto['status'],
  'success' | 'warning' | 'danger' | 'primary' | 'info'
> = {
  PENDING: 'info',
  RUNNING: 'primary',
  PARTIAL_SUCCESS: 'warning',
  SUCCESS: 'success',
  FAILED: 'danger'
}

export const RUN_TYPE_LABEL: Record<ScheduleRunDto['runType'], string> = {
  FULL_SCHEDULE: '全量排产',
  LOCAL_RESCHEDULE: '局部重排',
  MANUAL_RESCHEDULE: '手工重排',
  INSERT_ORDER_WHATIF: '插单 WhatIf'
}

export const useRunStore = defineStore('aps.run', () => {
  // ===== state =====
  const data = ref<RunStatusDto | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  /** 最近一次 FAILED 恢复结果（U19 验收：原 Run 仍 FAILED + 新 Run 产生） */
  const lastRecover = ref<RecoverFailedRunResult | null>(null)
  const recovering = ref(false)
  /** 最近一次 MES 下发结果（§十九.MES展示/操作） */
  const lastDispatch = ref<MesDispatchResult | null>(null)
  const dispatching = ref(false)

  // ===== getters =====
  /** P1-15：业务范围联动（前端防御；3号位后端会二次校验） */
  const apsAuth = useApsAuthStore()
  const allowedDomainKeys = computed<DomainKey[] | null>(() => {
    const keys = apsAuth.dataScope.domainKeys
    return keys.length > 0 ? keys : null
  })

  /**
   * ScheduleRun 是否在当前用户业务范围内
   *  - 判定标准：expectedDomainKeys 与用户 domainKeys 至少有一个交集
   *  - 空 scope → 全部放行
   */
  function runInScope(run: ScheduleRunDto): boolean {
    const allow = allowedDomainKeys.value
    if (!allow) return true
    return run.expectedDomainKeys.some((k) => allow.includes(k))
  }

  /** recentRuns 受业务范围过滤（前端预过滤，不替换 data.value） */
  const runs = computed(() => (data.value?.recentRuns ?? []).filter(runInScope))
  /** activeVersions 受业务范围过滤 */
  const activeVersions = computed(() =>
    (data.value?.activeVersions ?? []).filter((v) => {
      const allow = allowedDomainKeys.value
      if (!allow) return true
      return allow.includes(v.domainKey)
    })
  )
  const mesSamples = computed(() => data.value?.mesEligibilitySamples ?? [])
  const partialBreakdown = computed(() => data.value?.currentPartialBreakdown ?? null)

  const runStatusCounts = computed(() => {
    const counts: Record<ScheduleRunDto['status'], number> = {
      PENDING: 0,
      RUNNING: 0,
      PARTIAL_SUCCESS: 0,
      SUCCESS: 0,
      FAILED: 0
    }
    runs.value.forEach((r) => {
      counts[r.status] = (counts[r.status] ?? 0) + 1
    })
    return counts
  })

  const mesStats = computed(() => {
    const stats = { ELIGIBLE: 0, INELIGIBLE: 0, UNKNOWN: 0 }
    mesSamples.value.forEach((s) => {
      stats[s.eligibility] = (stats[s.eligibility] ?? 0) + 1
    })
    return stats
  })

  // ===== actions =====
  async function load(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      data.value = await runApi.getStatus()
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  /**
   * FAILED Run 人工恢复（U19：新建 Run，原始 Run 仍 FAILED）
   *  - 4 号位文档第 15 节：FAILED 人工恢复按钮必须调用"新建 ScheduleRun"，不能把 FAILED 直接改回 RUNNING
   *  - 恢复完成后 refresh，新 Run 应出现在 runs 列表顶部，原 Run 仍为 FAILED
   *  - P1-15：payload.domainKeys 中每个 Domain 都必须在当前用户业务范围内
   */
  /**
   * 校验 ScheduleRun.ExpectedDomainKeysJson 冻结规则（v1.4 §十九.2）
   *  - 在 recoverFailedRun 之前调用，避免"空恢复"撞 400
   *  - 返回 { valid, message }；UI 层据此判断是否继续走恢复流程
   *  - v1.4 §十九.2：FULL ≥ 1 Domain / RESCHEDULE 恰 1 Domain
   */
  async function validateDomainKeys(
    scheduleRunId: number
  ): Promise<{ valid: boolean; message: string }> {
    try {
      const res = await runApi.validateDomainKeys(scheduleRunId)
      if (!res.valid) {
        error.value = res.message
      }
      return res
    } catch (err) {
      handleError(err)
      return { valid: false, message: (err as Error)?.message ?? '校验异常' }
    }
  }

  async function recoverFailedRun(payload: {
    originalRunId: number
    domainKeys: DomainKey[]
    reason: string
    actor: string
  }): Promise<RecoverFailedRunResult> {
    // P1-15：业务范围断言（防止"指定 Domain 确认人恢复其它 Domain"—— §二十五.7）
    for (const dk of payload.domainKeys) {
      try {
        assertScope(apsAuth.dataScope, { domainKey: dk })
      } catch (err) {
        if (err instanceof ScopeViolationError) {
          error.value = err.message
          throw err
        }
        throw err
      }
    }
    // v1.4 §十九.2：预校验（避免空恢复直接 400）
    const check = await runApi.validateDomainKeys(payload.originalRunId)
    if (!check.valid) {
      error.value = `DomainKey 冻结规则校验失败：${check.message}（请先修正再恢复）`
      throw new Error(error.value)
    }
    recovering.value = true
    error.value = null
    try {
      lastRecover.value = await runApi.recoverFailedRun(payload)
      // U19 验收：刷新后原 Run 仍 FAILED + 新 Run 出现在列表
      await load()
      return lastRecover.value
    } catch (err) {
      handleError(err)
      throw err
    } finally {
      recovering.value = false
    }
  }

  /**
   * MES 下发（§十九.MES展示/操作）
   *  - 仅 ELIGIBLE Task 可下发；前端按钮 :disabled 强制
   *  - P1-15：dataScope.domainKeys 断言（前端防御；3号位后端会二次校验）
   *  - 成功后将 MES 工单号回写到 lastDispatch；样本状态刷新为 DISPATCHED（mock 端不真改 status）
   */
  async function dispatchMes(input: MesDispatchInput): Promise<MesDispatchResult> {
    // P1-15：业务范围断言（task 的 domainKey 命中 scope；缺失则跳过，由 3号位兜底）
    const sample = data.value?.mesEligibilitySamples.find((s) => s.taskId === input.taskId)
    if (sample?.domainKey) {
      try {
        assertScope(apsAuth.dataScope, { domainKey: sample.domainKey })
      } catch (err) {
        if (err instanceof ScopeViolationError) {
          error.value = err.message
          throw err
        }
        throw err
      }
    }
    dispatching.value = true
    error.value = null
    try {
      lastDispatch.value = await runApi.dispatch(input)
      return lastDispatch.value
    } catch (err) {
      handleError(err)
      throw err
    } finally {
      dispatching.value = false
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
    lastRecover.value = null
    lastDispatch.value = null
  }

  return {
    data,
    loading,
    error,
    lastRecover,
    recovering,
    lastDispatch,
    dispatching,
    runs,
    activeVersions,
    mesSamples,
    partialBreakdown,
    runStatusCounts,
    mesStats,
    load,
    recoverFailedRun,
    validateDomainKeys,
    dispatchMes,
    reset,
    RUN_STATUS_LABEL,
    RUN_STATUS_TAG,
    RUN_TYPE_LABEL
  }
})
