/**
 * APS V1 4号位 — 手工释放需求保护（Demand Protection Release）Pinia store（页面 9）
 *
 * @see 4号位文档第 13 节 / 审核报告 P1-11
 * @see 4号位 → 5号位 → 2号位 链路
 *
 * 数据流（10-09 lps/ 落地后，对齐 5号位 Controller 契约）：
 *
 *   list() ──► rawRows（BackendDemandProtectionRow[]，per-lock 扁平）
 *                  │
 *                  └──► aggregatedList（computed，按 DemandKey 聚合 + 派生 status）
 *
 * 4号位职责边界：
 *  - 聚合（按 DemandKey group by + PARTIALLY_RELEASED 派生态）由前端 store 完成（5号位 R1 接受）
 *  - lockedQty 真聚合在 5号位（per-row 字段，5号位 不二次聚合）
 *  - 不写锁表（5号位 转发给 2号位 IDemandSupplyHardLockRepository.ReleaseLocksAsync）
 *  - 释放走 per-lock partial success（5号位 契约：逐 lock 处理，部分成功；Allocation 不动）
 *
 * 10-09 用户决定：
 *  - 移除 KPI 4 卡（列表已能表达信息密度）
 *  - GET /api/demand-protection/summary 端点保留在 api.demandProtectionApi（未来需要时启用）
 *  - store 内 kpiSummary ref + 5 KPI getter + refreshKpi 全部移除
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  ApiError,
  demandProtectionApi,
  type BackendDemandProtectionRow,
  type DemandProtectionDto,
  type DemandProtectionListFilter,
  type DemandProtectionLockDetailDto,
  type DemandProtectionReleaseResult,
  type DemandProtectionStatus,
  type HardLockStatus,
  type ReleaseDemandProtectionInput
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'
import { assertScope, ScopeViolationError } from './scope'

/* ===== 聚合辅助：按 demandKey group by 派生 DemandProtectionDto ===== */

/**
 * 把 per-lock raw rows 聚合成 DemandProtectionDto[]。
 *
 * 聚合规则：
 *  - 主键：demandKey
 *  - 聚合维度：同一 demandKey 下全部 lockDetail（ACTIVE/RELEASED/BROKEN 全集）
 *  - lockedQty = 该 demandKey 下 ACTIVE 明细的 lockedQty 求和
 *  - lockIds = ACTIVE 明细的 id 列表（可释放集合）
 *  - status 派生：
 *      ACTIVE              全部明细 ACTIVE
 *      RELEASED            全部明细 RELEASED（且至少 1 条）
 *      PARTIALLY_RELEASED  混合 ACTIVE+RELEASED
 *      BROKEN              全部 BROKEN（暂不消费，预留）
 *  - 工厂/物料/描述/demandLabel/materialCode/factory：后端 DTO 无，前端可由 demandKey 解析或留空
 *  - releasedAt/releasedBy/releaseReason：取**首条已释放**明细（聚合行展示用）
 *  - sourcePlanVersionId：取**首条**明细
 *
 * ⚠️ 5号位 R1（2026-10-09 函件 R1 回执）：
 *  - PARTIALLY_RELEASED 派生态由**前端 store** 完成；5号位 不在 5号位 端派生
 *  - 详见 frontNew/src/api/aps-v1/types/demandProtection.ts 头注释 § 一 § 二
 */
function aggregateByDemandKey(rawRows: BackendDemandProtectionRow[]): DemandProtectionDto[] {
  // group by demandKey（Map 保序；后端 ORDER BY Status, CreatedAt DESC）
  const groups = new Map<string, BackendDemandProtectionRow[]>()
  for (const row of rawRows) {
    if (!row.demandKey) continue
    const arr = groups.get(row.demandKey) ?? []
    arr.push(row)
    groups.set(row.demandKey, arr)
  }

  const aggregated: DemandProtectionDto[] = []
  for (const [demandKey, rows] of groups) {
    const lockDetails: DemandProtectionLockDetailDto[] = rows.map((r) => ({
      lockId: r.id,
      supplyType: r.supplyType,
      supplyKey: r.supplyKey,
      lockedQty: r.lockedQty,
      status: r.status,
      sourceAllocationSequence: r.sourceAllocationSequence
    }))

    const activeDetails = lockDetails.filter((d) => d.status === 'ACTIVE')
    const releasedDetails = lockDetails.filter((d) => d.status === 'RELEASED')
    const brokenDetails = lockDetails.filter((d) => d.status === 'BROKEN')

    const lockedQty = activeDetails.reduce((sum, d) => sum + d.lockedQty, 0)
    const lockIds = activeDetails.map((d) => d.lockId)

    let status: DemandProtectionStatus
    if (activeDetails.length === lockDetails.length) {
      status = 'ACTIVE'
    } else if (activeDetails.length === 0 && releasedDetails.length === lockDetails.length) {
      status = 'RELEASED'
    } else if (activeDetails.length === 0 && brokenDetails.length === lockDetails.length) {
      status = 'RELEASED' // 全部 BROKEN 视为 RELEASED（不可手工释放）
    } else {
      status = 'PARTIALLY_RELEASED'
    }

    // 释放时间/原因：取首条已释放（聚合行展示；不要求最值）
    const firstReleased = rows.find((r) => r.releasedAt)
    const created = rows[0]

    aggregated.push({
      demandType: created.demandType,
      demandKey,
      demandLabel: undefined,
      factory: undefined,
      materialCode: undefined,
      lockType: created.lockType,
      lockIds,
      lockedQty,
      lockCount: lockDetails.length,
      status,
      lockDetails,
      sourcePlanVersionId: created.sourcePlanVersionId,
      createdAt: created.createdAt,
      createdBy: created.createdBy ?? 'algorithm',
      releasedAt: firstReleased?.releasedAt,
      releasedBy: firstReleased?.releasedBy,
      releaseReason: firstReleased?.releaseReason
    })
  }

  return aggregated
}

export const useDemandProtectionStore = defineStore('aps.demandProtection', () => {
  // ===== state =====
  /** 后端 per-lock 原始行（来自 GET /api/demand-protection） */
  const rawRows = ref<BackendDemandProtectionRow[]>([])
  /** 当前 Drawer / Modal 编辑对象（新增时为 null；当前页面无新增入口） */
  const currentDetail = ref<DemandProtectionDto | null>(null)
  const loading = ref(false)
  const releasing = ref(false)
  const error = ref<string | null>(null)
  /** 最近一次查询的筛选条件（分页/筛选 UI 还原用） */
  const lastFilter = ref<DemandProtectionListFilter>({})

  // ===== getters（视图消费的唯一入口） =====
  /**
   * 聚合视图：按 DemandKey group by 派生 DemandProtectionDto[]。
   * 后端 rawRows 变 → 自动重算。
   */
  const aggregatedList = computed<DemandProtectionDto[]>(() => aggregateByDemandKey(rawRows.value))

  /** 兼容旧视图：aggregatedList 长度 = 当前页聚合行数 */
  const list = computed<DemandProtectionDto[]>(() => aggregatedList.value)

  // ===== actions =====
  /**
   * 加载列表（GET /api/demand-protection）
   *  - rawRows 触发 aggregatedList computed 重算 → UI 自动更新
   *  - list 端点支持分页（skip/take）
   *  - 注：GET /api/demand-protection/summary 端点 + KPI 4 卡在 10-09 用户决定中撤销
   *    （列表已表达信息密度，KPI 卡片属冗余 UI）；api.summary() 仍保留供未来需要时使用
   */
  async function load(filter?: DemandProtectionListFilter): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const next = filter ?? lastFilter.value
      rawRows.value = await demandProtectionApi.list(next)
      lastFilter.value = next
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  /**
   * 本地查找（aggregatedList = preview，不发请求）
   *  - 由调用方传入 demandKey，若未命中返回 null
   */
  function getDetail(demandKey: string): DemandProtectionDto | null {
    return aggregatedList.value.find((x) => x.demandKey === demandKey) ?? null
  }

  function openDetail(dto: DemandProtectionDto | null): void {
    currentDetail.value = dto
  }

  /**
   * 逐 lock 释放（per-lock partial success）
   *  - 5号位 契约：POST /api/demand-protection/release 接收 {lockIds, releasedBy, releaseReason}
   *    返回 List<{lockId, demandKey, status, failureReason?}>
   *  - 本地 rawRows 按返回结果回写：每个 RELEASED 的 row.status 改写 → aggregatedList computed 自动重算
   *  - P1-15：业务范围断言（按 lockIds 反查 owner DemandProtectionDto，取 factory）
   */
  async function release(
    input: ReleaseDemandProtectionInput
  ): Promise<DemandProtectionReleaseResult[]> {
    const apsAuth = useApsAuthStore()
    // P1-15：业务范围断言 — 按 lockIds 反查 owner DemandProtection
    const owners = new Map<string, DemandProtectionDto>()
    for (const id of input.lockIds) {
      const owner = aggregatedList.value.find((x) => x.lockDetails.some((d) => d.lockId === id))
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
      applyReleaseResultsToRawRows(perLock, input)
      return perLock
    } catch (err) {
      handleError(err)
      throw err
    } finally {
      releasing.value = false
    }
  }

  /**
   * 把 per-lock 释放结果回写到本地 rawRows（mock 阶段 + 真实模式本地缓存同步）
   *  - rawRows 一变 → aggregatedList computed 自动重算 → 表格 + Drawer 同步
   *  - 真实阶段后端已落库，本地回写主要为了让 UI 立即反映；
   *    下次 store.load() 会拉取最新
   */
  function applyReleaseResultsToRawRows(
    perLock: DemandProtectionReleaseResult[],
    input: ReleaseDemandProtectionInput
  ): void {
    const releasedSet = new Set(perLock.filter((r) => r.status === 'RELEASED').map((r) => r.lockId))
    if (releasedSet.size === 0) return

    rawRows.value = rawRows.value.map((row) => {
      if (releasedSet.has(row.id) && row.status === 'ACTIVE') {
        return {
          ...row,
          status: 'RELEASED' as HardLockStatus,
          releasedAt: row.releasedAt ?? new Date().toISOString(),
          releasedBy: row.releasedBy ?? input.releasedBy,
          releaseReason: row.releaseReason ?? input.releaseReason.trim()
        }
      }
      return row
    })

    // 同步 currentDetail（如果指向了被修改的 demandKey）
    if (currentDetail.value) {
      const touched = aggregatedList.value.find(
        (x) => x.demandKey === currentDetail.value!.demandKey
      )
      if (touched) currentDetail.value = touched
    }
  }

  /**
   * 批量释放（仅前端编排，不保证原子性）
   *  - 用 Promise.allSettled 如实报告"成功 N 条 / 失败 M 条"
   *  - 5号位未承诺跨 DemandKey 原子性；要原子需 5号位 提供批量端点
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
    rawRows.value = []
    currentDetail.value = null
    error.value = null
    lastFilter.value = {}
  }

  return {
    // state
    rawRows,
    currentDetail,
    loading,
    releasing,
    error,
    lastFilter,
    // getters
    aggregatedList,
    list,
    // actions
    load,
    getDetail,
    openDetail,
    release,
    releaseBatch,
    reset
  }
})
