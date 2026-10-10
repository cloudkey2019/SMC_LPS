/**
 * APS V1 4号位 — Setup 版本 Diff Pinia store（v1.5 Setup 专项 §6）
 *
 * 状态：
 *  - diff：版本 Diff 结果（含 setupRuleChanges）
 *  - selectedVersionId：当前版本
 *  - otherVersionId：对比版本
 *  - ruleSetVersions：RuleSetVersion 下拉源
 *  - loading 系列：UI 态
 *
 * 权限：
 *  - aps.setup.view    → 查看 Diff
 *  - aps.setup.publish → 发布（POST /rule-set-versions/{id}/publish）
 *
 * 红线（§6.4）：
 *  - 不展示非 Setup 维度的 Diff（ParameterSet/StrategyProfile 仍走原 v1.4 §十八 规则参数维护页）
 *  - 不允许跨 RuleSet 直接编辑（必须发布 DRAFT 后再编辑）
 *
 * 复用：
 *  - RuleSetVersion Diff 机制（v1.4 §十八）
 *  - 3号位 需补 setupRuleChanges 字段（§6.3）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  setupApi,
  ApiError,
  type SetupDiffDto,
  type SetupRuleSetVersionDto,
  type PublishSetupRuleSetVersionRequest
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

export const useSetupDiffStore = defineStore('aps.setupDiff', () => {
  // ===== state =====
  const diff = ref<SetupDiffDto | null>(null)
  const selectedVersionId = ref<number | null>(null)
  const otherVersionId = ref<number | null>(null)
  const ruleSetVersions = ref<SetupRuleSetVersionDto[]>([])

  const diffLoading = ref(false)
  const versionsLoading = ref(false)
  const actionRunning = ref(false)
  const error = ref<string | null>(null)

  // ===== auth =====
  const apsAuth = useApsAuthStore()

  // ===== getters =====

  /** 是否有 Diff 结果 */
  const hasDiff = computed<boolean>(() => diff.value !== null)

  /** 查看权限 */
  const canView = computed<boolean>(() => apsAuth.has('aps.setup.view'))

  /** 发布权限 */
  const canPublishPerm = computed<boolean>(() => apsAuth.has('aps.setup.publish'))

  /** 当前选中的 RuleSetVersion */
  const currentVersion = computed<SetupRuleSetVersionDto | null>(() => {
    if (selectedVersionId.value === null) return null
    return ruleSetVersions.value.find((v) => v.id === selectedVersionId.value) ?? null
  })

  /** 对比的 RuleSetVersion */
  const otherVersion = computed<SetupRuleSetVersionDto | null>(() => {
    if (otherVersionId.value === null) return null
    return ruleSetVersions.value.find((v) => v.id === otherVersionId.value) ?? null
  })

  /** 是否可发布（仅 DRAFT 状态） */
  const canPublishAction = computed<boolean>(() => currentVersion.value?.status === 'DRAFT')

  /** 变更总数 */
  const totalChanges = computed<number>(() => {
    if (!diff.value) return 0
    return diff.value.addedCount + diff.value.modifiedCount + diff.value.removedCount
  })

  // ===== actions =====

  function selectVersion(versionId: number | null): void {
    selectedVersionId.value = versionId
    diff.value = null
  }

  function selectOtherVersion(versionId: number | null): void {
    otherVersionId.value = versionId
    diff.value = null
  }

  async function loadRuleSetVersions(params?: { ruleSetId?: number }): Promise<void> {
    versionsLoading.value = true
    error.value = null
    try {
      ruleSetVersions.value = await setupApi.listRuleSetVersions(params)
    } catch (err) {
      handleError(err)
      ruleSetVersions.value = []
    } finally {
      versionsLoading.value = false
    }
  }

  async function loadDiff(versionId: number, otherId: number): Promise<void> {
    if (versionId === otherId) {
      diff.value = null
      error.value = '源版本和目标版本相同，无法对比'
      return
    }
    diffLoading.value = true
    error.value = null
    try {
      diff.value = await setupApi.diffRuleSetVersions(versionId, otherId)
    } catch (err) {
      handleError(err)
      diff.value = null
    } finally {
      diffLoading.value = false
    }
  }

  /** 发布 RuleSetVersion（需 publish 权限 + DRAFT 状态） */
  async function publish(
    versionId: number,
    request: PublishSetupRuleSetVersionRequest
  ): Promise<boolean> {
    if (!canPublishPerm.value) {
      error.value = '当前操作者无发布权限（需权限码 aps.setup.publish）'
      return false
    }
    if (!canPublishAction.value) {
      error.value = '只能发布 DRAFT 状态的 RuleSetVersion（§7.2 业务红线）'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      await setupApi.publishRuleSetVersion(versionId, request)
      await loadRuleSetVersions()
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      actionRunning.value = false
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
    diff.value = null
    selectedVersionId.value = null
    otherVersionId.value = null
    ruleSetVersions.value = []
    error.value = null
  }

  return {
    // state
    diff,
    selectedVersionId,
    otherVersionId,
    ruleSetVersions,
    diffLoading,
    versionsLoading,
    actionRunning,
    error,
    // getters
    hasDiff,
    canView,
    canPublishPerm,
    currentVersion,
    otherVersion,
    canPublishAction,
    totalChanges,
    // actions
    selectVersion,
    selectOtherVersion,
    loadRuleSetVersions,
    loadDiff,
    publish,
    handleError,
    reset
  }
})
