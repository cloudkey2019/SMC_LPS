/**
 * APS V1 4号位 — Candidate 对比与确认 Pinia store（页面 5）
 *
 * 状态：
 *  - list：本会话内可见的 Candidate 列表
 *  - currentComparison：当前选中的 Candidate 对比详情
 *  - lastConfirmResult：上一次确认结果（U13：Actor/Time 可追溯）
 *  - loading / error：UI 态
 *
 * 写接口：
 *  - confirm() → 走 Controller，后端产生新的 ACTIVE 版本，不直接 UPDATE PlanVersion
 *  - 4号位文档第 9 节：CTP/INSERT_IMPACT_ANALYSIS 永远无此接口（U11/U12）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  candidateApi,
  ApiError,
  type CandidateActivateInput,
  type CandidateActivateResult,
  type CandidateComparisonDto,
  type CandidateConfirmInput,
  type CandidateConfirmResult,
  type DomainKey
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'
import { useDomainStore } from './domain'
import { assertScope, ScopeViolationError, filterByScope } from './scope'

export interface CandidateListItem {
  candidatePlanVersionId: number
  candidateVersionCode: string
  domainKey: DomainKey
  status: string
  createdAt: string
  /** P0-05：是否允许激活为 ACTIVE（后端判定） */
  canActivate: boolean
  /** P0-05：触发本次候选的 RunType */
  runType?: 'FULL_SCHEDULE' | 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
  /** v1.4 §十二：调用 compare-with 端点的第二个路径参数
   *  - 当前 list 端点不返回（@see candidate.ts list() 注释）
   *  - 5号位 补齐 17 字段后会补到 CandidateBriefDto
   *  - 当前在 store.loadComparison 上做"参数缺失 → 报清晰错"的兜底
   */
  basePlanVersionId?: number
  impactSummary: { impactedOrderCount: number; newDelayCount: number }
}

export const useCandidateStore = defineStore('aps.candidate', () => {
  // ===== state =====
  const list = ref<CandidateListItem[]>([])
  const currentComparison = ref<CandidateComparisonDto | null>(null)
  const lastConfirmResult = ref<CandidateConfirmResult | null>(null)
  const loading = ref(false)
  const listLoading = ref(false)
  const confirming = ref(false)
  const activating = ref(false)
  const lastActivateResult = ref<CandidateActivateResult | null>(null)
  const error = ref<string | null>(null)

  // ===== getters =====
  const currentCandidateId = computed(
    () => currentComparison.value?.header.candidatePlanVersionId ?? null
  )
  /** 当前选中的 Header（用于确认按钮处展示） */
  const currentHeader = computed(() => currentComparison.value?.header ?? null)

  /** 权限码门控：仅持有 aps.candidate.confirm 可"确认采用"（v1.2 §23.1）
   *  - dev seed：aps.planner（17 码）+ aps.admin.system（34 码）持有
   *  - aps.viewer.management 仅持 *.view → 按钮 v-if 隐藏
   *  - 不用角色判断的原因：v1.2 DDL 把 RULE_ADMIN/RULE_PUBLISHER 合并为 aps.admin.aps，
   *    该角色无 aps.candidate.confirm 权限码（候选确认属 PMC 领域操作）
   */
  const apsAuth = useApsAuthStore()
  const roleAllowedToConfirm = computed(() => apsAuth.has('aps.candidate.confirm'))

  /** 权限码门控：仅持有 aps.candidate.activate 可"正式采用"（v1.2 §23.1）
   *  - 与 confirm 同样的角色（PMC + SYSTEM_ADMIN 持双码），但前端分开判断
   *  - 防呆：未来若 0号位 拆分 confirm/activate 权限边界，前端无需联动改
   */
  const roleAllowedToActivate = computed(() => apsAuth.has('aps.candidate.activate'))

  /** 是否可"采用"（U11/U12 + 角色门控）
   * 审核报告 P0-05：直接依据后端返回的 canActivate 判定，不再推断 sourceRefType
   *  - canActivate=true  → LOCAL_RESCHEDULE / MANUAL_RESCHEDULE 触发
   *  - canActivate=false → INSERT_ORDER_WHATIF 触发（CTP/INSERT_IMPACT_ANALYSIS 仅试算）
   */
  const canConfirm = computed(() => {
    const h = currentComparison.value?.header
    if (!h) return false
    if (!roleAllowedToConfirm.value) return false
    return h.canActivate && h.status === 'CANDIDATE'
  })

  /** 是否可"激活"（CANDIDATE → ACTIVE，每域单一正式采用版本）
   *  - 与 confirm 同 canActivate 门控（U11/U12）
   *  - 但角色门控用 aps.candidate.activate（与 confirm 码分开，未来可拆）
   *  - 仅 CANDIDATE 状态可激活（ACTIVE / REJECTED / FAILED 都不允许）
   *  - 不强制要求先 confirm：confirm 是留痕，activate 是落地，两者独立
   */
  const canActivate = computed(() => {
    const h = currentComparison.value?.header
    if (!h) return false
    if (!roleAllowedToActivate.value) return false
    return h.canActivate && h.status === 'CANDIDATE'
  })

  /** 不可激活原因（用于 Tooltip 提示） */
  const cannotActivateReason = computed<string | null>(() => {
    const h = currentComparison.value?.header
    if (!h) return null
    if (!roleAllowedToActivate.value)
      return '当前角色无激活权限（需 aps.candidate.activate，PMC 及以上）'
    if (!h.canActivate) {
      return h.runType === 'INSERT_ORDER_WHATIF'
        ? 'WHATIF 候选（P0-05）：由 CTP / INSERT_IMPACT_ANALYSIS 触发，仅试算，不可激活为 ACTIVE'
        : '当前 Candidate 不允许激活（U11/U12）'
    }
    if (h.status !== 'CANDIDATE') return `状态 ${h.status} 不允许激活（仅 CANDIDATE 可激活）`
    return null
  })

  /** 当前是否 WHATIF（不可激活） */
  const isWhatif = computed(() => {
    const h = currentComparison.value?.header
    return !!h && !h.canActivate && h.runType === 'INSERT_ORDER_WHATIF'
  })

  /** Pkg-4 (v1.2 §10/§13)：domainKey → 人类可读 name（找不到回退 key 本身）
   *  - 通过 useDomainStore 解析；只读不写
   *  - Candidate scope 断言仍只校验 header.domainKey，不参与本函数
   */
  const domainStore = useDomainStore()
  const humanLabel = (domainKey: DomainKey | undefined | null): string => {
    if (!domainKey) return '—'
    return domainStore.label(domainKey)
  }

  /** Pkg-4：当前 Comparison 的共享资源占用明细（v1.2 §10/§13） */
  const currentSharedResourceOccupancies = computed(
    () => currentComparison.value?.taskChangeSummary.sharedResourceOccupancies ?? []
  )

  /** Pkg-4：当前 Comparison 的 Quantity-Time 切片（v1.2 §11，跨域依赖必须保留分段） */
  const currentQuantityTimeSlices = computed(
    () => currentComparison.value?.header.quantityTimeSlices ?? []
  )

  /** Pkg-4：根据 resourceRefId 反查当前 Comparison 中的 resourceCode（用于影响订单表格的"阻挡资源"列） */
  const resourceCodeByRefId = (resourceRefId: number | undefined): string => {
    if (resourceRefId === undefined) return '—'
    const occ = currentSharedResourceOccupancies.value.find(
      (o) => o.resourceRefId === resourceRefId
    )
    return occ?.resourceCode ?? `#${resourceRefId}`
  }

  /** 用于在 Header 里显示原因（"禁止激活"时附原因） */
  const cannotConfirmReason = computed<string | null>(() => {
    const h = currentComparison.value?.header
    if (!h) return null
    if (!roleAllowedToConfirm.value) return '当前角色无激活权限（仅 PMC 及以上）'
    if (!h.canActivate) {
      return h.runType === 'INSERT_ORDER_WHATIF'
        ? 'WHATIF 候选（P0-05）：由 CTP / INSERT_IMPACT_ANALYSIS 触发，仅试算，不可激活为 ACTIVE'
        : '当前 Candidate 不允许激活（U11/U12）'
    }
    if (h.status !== 'CANDIDATE') return `状态 ${h.status} 不允许激活`
    return null
  })

  // ===== actions =====
  /**
   * 加载 Candidate 列表
   *  - P1-15：按 dataScope.domainKeys 预过滤（前端防御；3号位后端会二次校验）
   *  - 空 scope → 不过滤
   */
  async function loadList(): Promise<void> {
    listLoading.value = true
    error.value = null
    try {
      const raw = await candidateApi.list()
      list.value = filterByScope(raw, apsAuth.dataScope, (c) => ({ domainKey: c.domainKey }))
    } catch (err) {
      handleError(err)
    } finally {
      listLoading.value = false
    }
  }

  /**
   * 加载 Candidate 对比详情（双参：candidateId + baseId）
   *  - v1.4 §十二：compare-with 端点必填双参（CompareCandidateWithBase L583）
   *  - 当前 in-flight：list 端点暂未返回 basePlanVersionId（5号位 17 字段收口中）
   *  - 上层应先查 list → 取 listItem.basePlanVersionId → 再调本方法
   *  - 真实模式缺 basePlanVersionId：抛错让 UI 走"等 5号位"提示，不静默失败
   */
  async function loadComparison(
    candidatePlanVersionId: number,
    basePlanVersionId?: number
  ): Promise<void> {
    loading.value = true
    error.value = null
    try {
      if (!basePlanVersionId) {
        throw new Error(
          `加载 Candidate #${candidatePlanVersionId} 详情需要 basePlanVersionId；当前 list 端点未返回（5号位 17 字段收口中，请参考 4号位-2026-09-18-Candidate17字段催办-给5号位.md）`
        )
      }
      currentComparison.value = await candidateApi.getComparison(
        candidatePlanVersionId,
        basePlanVersionId
      )
    } catch (err) {
      handleError(err)
      currentComparison.value = null
    } finally {
      loading.value = false
    }
  }

  /**
   * 确认 Candidate（U10/U13）
   *  - P0-05：canActivate 判定 + 角色门控
   *  - P1-15：dataScope.domainKey 断言（前端防御；3号位后端会二次校验）
   */
  async function confirm(input: CandidateConfirmInput): Promise<void> {
    if (!canConfirm.value) {
      error.value = cannotConfirmReason.value ?? '当前 Candidate 不允许确认（U11/U12 不可激活）'
      return
    }
    // P1-15：业务范围断言（防止"指定 Domain 确认人确认其它 Domain Candidate"—— §二十五.7）
    const header = currentComparison.value?.header
    if (header?.domainKey) {
      try {
        assertScope(apsAuth.dataScope, { domainKey: header.domainKey })
      } catch (err) {
        if (err instanceof ScopeViolationError) {
          error.value = err.message
          return
        }
        throw err
      }
    }
    confirming.value = true
    error.value = null
    try {
      lastConfirmResult.value = await candidateApi.confirm(input)
    } catch (err) {
      handleError(err)
    } finally {
      confirming.value = false
    }
  }

  /**
   * 激活 Candidate（CANDIDATE → ACTIVE，v1.2 §十三 P0-08）
   *  - 与 confirm 同 canActivate + scope 门控
   *  - 与 confirm 不同：不需要先 confirm（confirm 是留痕，activate 是落地，两者独立）
   *  - 成功后 lastActivateResult 暴露给 UI（U13：Actor/Time 可追溯）
   *  - 失败：scope 违例 / 非 CANDIDATE 状态（400）/ Domain 已存在 ACTIVE（409）/
   *          无权限（403）
   */
  async function activate(input: CandidateActivateInput): Promise<void> {
    if (!canActivate.value) {
      error.value = cannotActivateReason.value ?? '当前 Candidate 不允许激活'
      return
    }
    // P1-15：业务范围断言（同 confirm）
    const header = currentComparison.value?.header
    if (header?.domainKey) {
      try {
        assertScope(apsAuth.dataScope, { domainKey: header.domainKey })
      } catch (err) {
        if (err instanceof ScopeViolationError) {
          error.value = err.message
          return
        }
        throw err
      }
    }
    activating.value = true
    error.value = null
    try {
      lastActivateResult.value = await candidateApi.activate(input)
      // 激活成功后刷新 list + 重拉当前 comparison（status 应变 ACTIVE）
      // v1.4 §十二：compare-with 需 basePlanVersionId，沿用当前 comparison 的 baseId
      const baseId = currentComparison.value?.header.basePlanVersionId
      await Promise.all([
        loadList(),
        baseId ? loadComparison(input.candidatePlanVersionId, baseId) : Promise.resolve()
      ])
    } catch (err) {
      handleError(err)
    } finally {
      activating.value = false
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
    currentComparison.value = null
    lastConfirmResult.value = null
    error.value = null
  }

  return {
    list,
    currentComparison,
    lastConfirmResult,
    lastActivateResult,
    loading,
    listLoading,
    confirming,
    activating,
    error,
    currentCandidateId,
    currentHeader,
    canConfirm,
    canActivate,
    isWhatif,
    cannotConfirmReason,
    cannotActivateReason,
    roleAllowedToConfirm,
    roleAllowedToActivate,
    // Pkg-4：v1.2 §10/§11/§13 共享资源/切片渲染辅助
    humanLabel,
    currentSharedResourceOccupancies,
    currentQuantityTimeSlices,
    resourceCodeByRefId,
    loadList,
    loadComparison,
    confirm,
    activate,
    reset,
    handleError
  }
})
