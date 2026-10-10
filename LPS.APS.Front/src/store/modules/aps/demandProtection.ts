/**
 * APS V1 4号位 — 手工释放需求保护（Demand Protection Release）Pinia store（页面 9）
 *
 * @see 4号位文档第 13 节 / 审核报告 P1-11
 * @see 4号位 → 5号位 → 2号位 链路
 *
 * 状态：
 *  - list：DemandProtectionDto[]（按 DemandKey 聚合）
 *  - currentDetail：当前 Drawer / Modal 编辑对象（新增时为 null；当前页面无新增入口）
 *  - loading / releasing / error：UI 态
 *  - lastFilter：最近一次查询的筛选条件
 *
 * 4号位职责边界：
 *  - 不计算 lockedQty / status 派生态
 *  - 不写锁表
 *  - 释放走 per-lock partial success（5号位契约：逐 lock 处理，部分成功；Allocation 不动）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  ApiError,
  demandProtectionApi,
  type DemandProtectionDto,
  type DemandProtectionListFilter,
  type DemandProtectionDemandType,
  type DemandProtectionReleaseResult,
  type HardLockType,
  type ReleaseDemandProtectionInput
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'
import { assertScope, ScopeViolationError } from './scope'

export const useDemandProtectionStore = defineStore('aps.demandProtection', () => {
  // ===== state =====
  const list = ref<DemandProtectionDto[]>([])
  const currentDetail = ref<DemandProtectionDto | null>(null)
  const loading = ref(false)
  const releasing = ref(false)
  const error = ref<string | null>(null)
  /** 最近一次查询的筛选条件（分页/筛选 UI 还原用） */
  const lastFilter = ref<DemandProtectionListFilter>({})

  // ===== getters =====
  /** ACTIVE 行数 */
  const activeCount = computed(() => list.value.filter((x) => x.status === 'ACTIVE').length)
  /** 已释放行数（含 PARTIALLY_RELEASED，UI 不区分历史混合态） */
  const releasedCount = computed(
    () =>
      list.value.filter((x) => x.status === 'RELEASED' || x.status === 'PARTIALLY_RELEASED').length
  )
  /** ACTIVE 锁定总量求和（仅 KPI 展示用，非业务计算） */
  const totalLockedQty = computed(() =>
    list.value.filter((x) => x.status === 'ACTIVE').reduce((sum, x) => sum + x.lockedQty, 0)
  )
  /** DemandType 去重集合（供筛选下拉用） */
  const distinctDemandTypes = computed<DemandProtectionDemandType[]>(() =>
    Array.from(new Set(list.value.map((x) => x.demandType))).sort()
  )
  /** LockType 去重集合（供筛选下拉用） */
  const distinctLockTypes = computed<HardLockType[]>(() =>
    Array.from(new Set(list.value.map((x) => x.lockType))).sort()
  )

  // ===== actions =====
  async function load(filter?: DemandProtectionListFilter): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const next = filter ?? lastFilter.value
      list.value = await demandProtectionApi.list(next)
      lastFilter.value = next
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  /**
   * 本地查找（list = preview，不发请求）
   *  - 由调用方传入 demandKey，若未命中返回 null
   */
  function getDetail(demandKey: string): DemandProtectionDto | null {
    return list.value.find((x) => x.demandKey === demandKey) ?? null
  }

  function openDetail(dto: DemandProtectionDto | null): void {
    currentDetail.value = dto
  }

  /**
   * 逐 lock 释放（per-lock partial success）
   *  - 5号位契约：POST /api/demand-protection/release 接收 {lockIds, releasedBy, releaseReason}
   *    返回 List<{lockId, demandKey, status, failureReason?}>
   *  - 本地 list 按返回结果回写：每个 RELEASED 的 lockDetail 状态变更 → 聚合行 status / lockIds / lockedQty 联动更新
   *  - P1-15：业务范围断言（按 lockIds 反查 owner DemandProtectionDto，取 factory）
   */
  async function release(
    input: ReleaseDemandProtectionInput
  ): Promise<DemandProtectionReleaseResult[]> {
    const apsAuth = useApsAuthStore()
    // P1-15：业务范围断言 — 按 lockIds 反查 owner DemandProtection
    const owners = new Map<string, DemandProtectionDto>()
    for (const id of input.lockIds) {
      const owner = list.value.find((x) => x.lockDetails.some((d) => d.lockId === id))
      if (owner?.factory) {
        try {
          assertScope(apsAuth.dataScope, { factoryCode: owner.factory })
        } catch (err) {
          if (err instanceof ScopeViolationError) {
            error.value = err.message
            throw err
          }
          throw err
        }
      }
      if (owner) owners.set(owner.demandKey, owner)
    }

    releasing.value = true
    error.value = null
    try {
      const perLock = await demandProtectionApi.release(input)
      applyReleaseResultsToList(perLock, input)
      return perLock
    } catch (err) {
      handleError(err)
      throw err
    } finally {
      releasing.value = false
    }
  }

  /**
   * 把 per-lock 释放结果回写到本地 list（聚合行的 lockDetails / lockIds / lockedQty / status 联动）
   *  - 仅在 mock 阶段真正变更本地数据；真实阶段后端已落库，下次 store.load() 会拉取最新
   *  - 同步两侧让 mock UI 与真实 UI 行为一致
   */
  function applyReleaseResultsToList(
    perLock: DemandProtectionReleaseResult[],
    input: ReleaseDemandProtectionInput
  ): void {
    const releasedSet = new Set(perLock.filter((r) => r.status === 'RELEASED').map((r) => r.lockId))
    if (releasedSet.size === 0) return

    for (const owner of list.value) {
      let touched = false
      const nextDetails = owner.lockDetails.map((d) => {
        if (releasedSet.has(d.lockId) && d.status === 'ACTIVE') {
          touched = true
          return { ...d, status: 'RELEASED' as const }
        }
        return d
      })
      if (!touched) continue

      const activeDetails = nextDetails.filter((d) => d.status === 'ACTIVE')
      const releasedDetails = nextDetails.filter((d) => d.status === 'RELEASED')
      const next: DemandProtectionDto = {
        ...owner,
        lockDetails: nextDetails,
        lockIds: activeDetails.map((d) => d.lockId),
        lockedQty: activeDetails.reduce((sum, d) => sum + d.lockedQty, 0),
        lockCount: nextDetails.length
      }
      if (activeDetails.length === 0) {
        next.status = 'RELEASED'
        next.releasedAt = owner.releasedAt ?? new Date().toISOString()
        next.releasedBy = owner.releasedBy ?? input.releasedBy
        next.releaseReason = owner.releaseReason ?? input.releaseReason.trim()
      } else if (releasedDetails.length > 0) {
        next.status = 'PARTIALLY_RELEASED'
      }
      const idx = list.value.findIndex((x) => x.demandKey === owner.demandKey)
      if (idx >= 0) list.value[idx] = next
    }

    if (currentDetail.value) {
      const touched = list.value.find((x) => x.demandKey === currentDetail.value!.demandKey)
      if (touched) currentDetail.value = touched
    }
  }

  /**
   * 批量释放（仅前端编排，不保证原子性）
   *  - 用 Promise.allSettled 如实报告"成功 N 条 / 失败 M 条"
   *  - 5号位未承诺跨 DemandKey 原子性；要原子需 5号位提供批量端点
   */
  async function releaseBatch(inputs: ReleaseDemandProtectionInput[]): Promise<{
    perLock: DemandProtectionReleaseResult[]
    succeeded: string[]
    failed: { demandKey: string; message: string }[]
  }> {
    releasing.value = true
    error.value = null
    const perLock: DemandProtectionReleaseResult[] = []
    const succeeded: string[] = []
    const failed: { demandKey: string; message: string }[] = []
    try {
      const results = await Promise.allSettled(inputs.map((i) => release(i)))
      results.forEach((r, idx) => {
        if (r.status === 'fulfilled') {
          perLock.push(...r.value)
          // 按 demandKey 分组：本组全部 RELEASED 算成功，否则算部分失败
          const groups = new Map<string, DemandProtectionReleaseResult[]>()
          for (const x of r.value) {
            const arr = groups.get(x.demandKey) ?? []
            arr.push(x)
            groups.set(x.demandKey, arr)
          }
          for (const [k, arr] of groups) {
            if (!k) continue
            if (arr.every((x) => x.status === 'RELEASED')) succeeded.push(k)
            else {
              const failReason = arr.find((x) => x.status === 'FAILED')?.failureReason ?? '部分失败'
              failed.push({ demandKey: k, message: failReason })
            }
          }
        } else {
          failed.push({
            demandKey: `(批量项 ${idx + 1})`,
            message: (r.reason as Error)?.message ?? '释放失败'
          })
        }
      })
      return { perLock, succeeded, failed }
    } finally {
      releasing.value = false
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
    list.value = []
    currentDetail.value = null
    error.value = null
    lastFilter.value = {}
  }

  return {
    list,
    currentDetail,
    loading,
    releasing,
    error,
    lastFilter,
    activeCount,
    releasedCount,
    totalLockedQty,
    distinctDemandTypes,
    distinctLockTypes,
    load,
    getDetail,
    openDetail,
    release,
    releaseBatch,
    reset
  }
})
