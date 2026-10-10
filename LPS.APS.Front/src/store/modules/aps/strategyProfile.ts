/**
 * APS V1 4号位 — 策略配置 Pinia store（独立模块，避免 rules store 膨胀）
 *
 * 状态：
 *  - profiles：Profile 列表
 *  - selectedProfileId：当前选中 Profile
 *  - versions：当前 Profile 的版本列表
 *  - selectedVersionId：当前选中版本
 *  - currentDiff：版本 Diff（base → target）
 *  - publishValidation：发布前校验结果
 *  - runTrace：Run 引用追溯
 *  - loading 系列：UI 态
 *
 * 权限（前端按钮显隐，对齐后端 PermissionCodes.cs 三码）：
 *  - aps.strategy.view     → 查看（列表 / 详情 / Diff / Trace / Default / Published）
 *  - aps.strategy.edit     → 编辑（创建 DRAFT / 更新 DRAFT / Disable）
 *  - aps.strategy.publish  → 发布（POST /publish）
 *  - 注：后端类级用 [Authorize(StrategyView/RuleMaintain/RulePublish)] 三码对应
 *    （RuleMaintain = edit 权限码，RulePublish = publish 权限码；命名上后端用 Rule.* 复用，
 *     前端通过策略包专属的 aps.strategy.* 三码 OR 组合表达，与路由 meta 保持一致）
 *
 * 红线：
 *  - 已发布/退役/归档版本不可编辑（前端按钮 disabled + 后端 400 双保险）
 *  - IsDefault 仅 DRAFT 可改（与 UQ_StrategyProfileVersion_DefaultPublished 联动）
 *  - 发布前必须先看 diff（UI 流程：选 DRAFT → 看 diff(base, draft) → validate → publish）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  strategyProfileApi,
  ApiError,
  type StrategyProfileDto,
  type StrategyProfileVersionDto,
  type StrategyVersionDraftInput,
  type PublishStrategyVersionRequest,
  type DisableStrategyVersionRequest,
  type PublishValidationResultDto,
  type StrategyVersionDiffDto,
  type StrategyRunTraceDto,
  type StrategyVersionStatus,
  STRATEGY_EDITABLE_STATUSES,
  STRATEGY_PUBLISHABLE_STATUSES,
  STRATEGY_DISABLEABLE_STATUSES
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

export const useStrategyProfileStore = defineStore('aps.strategyProfile', () => {
  // ===== state =====
  const profiles = ref<StrategyProfileDto[]>([])
  const selectedProfileId = ref<number | null>(null)

  const versions = ref<StrategyProfileVersionDto[]>([])
  const selectedVersionId = ref<number | null>(null)

  const currentDiff = ref<StrategyVersionDiffDto | null>(null)
  const publishValidation = ref<PublishValidationResultDto | null>(null)
  const runTrace = ref<StrategyRunTraceDto | null>(null)

  const loading = ref(false)
  const listLoading = ref(false)
  const versionsLoading = ref(false)
  const actionRunning = ref(false)
  const validating = ref(false)
  const error = ref<string | null>(null)

  // ===== auth =====
  const apsAuth = useApsAuthStore()

  // ===== getters =====

  /** 当前选中的 Profile（null = 列表页） */
  const currentProfile = computed<StrategyProfileDto | null>(() => {
    if (selectedProfileId.value === null) return null
    return profiles.value.find((p) => p.id === selectedProfileId.value) ?? null
  })

  /** 当前选中的版本 */
  const currentVersion = computed<StrategyProfileVersionDto | null>(() => {
    if (selectedVersionId.value === null) return null
    return versions.value.find((v) => v.id === selectedVersionId.value) ?? null
  })

  /** 当前版本的 status */
  const currentStatus = computed<StrategyVersionStatus | null>(
    () => currentVersion.value?.status ?? null
  )

  /** 是否可编辑（仅 DRAFT） */
  const canEdit = computed<boolean>(
    () => currentStatus.value !== null && STRATEGY_EDITABLE_STATUSES.includes(currentStatus.value)
  )

  /** 是否可发布（DRAFT/SUBMITTED/APPROVED） */
  const canPublishAction = computed<boolean>(
    () =>
      currentStatus.value !== null && STRATEGY_PUBLISHABLE_STATUSES.includes(currentStatus.value)
  )

  /** 是否可 Disable（仅 PUBLISHED） */
  const canRetire = computed<boolean>(
    () =>
      currentStatus.value !== null && STRATEGY_DISABLEABLE_STATUSES.includes(currentStatus.value)
  )

  /** 任意写权限 */
  const canWrite = computed<boolean>(() => apsAuth.has('aps.strategy.edit'))

  /** 发布权限 */
  const canPublishPerm = computed<boolean>(() => apsAuth.has('aps.strategy.publish'))

  /** 当前 Profile 是否已有任何 PUBLISHED 版本（IsDefault 勾选框显隐依据） */
  const hasPublished = computed<boolean>(() => versions.value.some((v) => v.status === 'PUBLISHED'))

  /** 当前选中 Profile 的 status 分布（顶部 KPI 用） */
  const statusCounts = computed<Record<StrategyVersionStatus, number>>(() => {
    const c: Record<StrategyVersionStatus, number> = {
      DRAFT: 0,
      SUBMITTED: 0,
      APPROVED: 0,
      PUBLISHED: 0,
      DISABLED: 0,
      ARCHIVED: 0
    }
    versions.value.forEach((v) => {
      c[v.status] = (c[v.status] ?? 0) + 1
    })
    return c
  })

  // ===== actions =====

  function selectProfile(profileId: number | null): void {
    selectedProfileId.value = profileId
    selectedVersionId.value = null
    versions.value = []
    currentDiff.value = null
    publishValidation.value = null
    runTrace.value = null
  }

  function selectVersion(versionId: number | null): void {
    selectedVersionId.value = versionId
    currentDiff.value = null
    publishValidation.value = null
    runTrace.value = null
  }

  async function loadProfiles(): Promise<void> {
    listLoading.value = true
    error.value = null
    try {
      profiles.value = await strategyProfileApi.listProfiles()
    } catch (err) {
      handleError(err)
    } finally {
      listLoading.value = false
    }
  }

  async function loadVersions(profileId: number): Promise<void> {
    versionsLoading.value = true
    error.value = null
    try {
      versions.value = await strategyProfileApi.listVersions(profileId)
    } catch (err) {
      handleError(err)
      versions.value = []
    } finally {
      versionsLoading.value = false
    }
  }

  async function loadDiff(sourceVersionId: number, targetVersionId: number): Promise<void> {
    if (sourceVersionId === targetVersionId) {
      currentDiff.value = null
      return
    }
    error.value = null
    try {
      currentDiff.value = await strategyProfileApi.diffVersions(sourceVersionId, targetVersionId)
    } catch (err) {
      handleError(err)
      currentDiff.value = null
    }
  }

  /** 发布前校验（结果回写到 publishValidation） */
  async function validateDraft(versionId: number): Promise<boolean> {
    validating.value = true
    error.value = null
    try {
      publishValidation.value = await strategyProfileApi.validateVersion(versionId)
      return publishValidation.value.isValid
    } catch (err) {
      handleError(err)
      publishValidation.value = null
      return false
    } finally {
      validating.value = false
    }
  }

  /** 创建 DRAFT（需 edit 权限） */
  async function createDraft(
    input: StrategyVersionDraftInput
  ): Promise<StrategyProfileVersionDto | null> {
    if (!canWrite.value) {
      error.value = '当前操作者无创建权限（需权限码 aps.strategy.edit）'
      return null
    }
    actionRunning.value = true
    error.value = null
    try {
      const v = await strategyProfileApi.createVersion(input)
      if (selectedProfileId.value === input.strategyProfileId) {
        await loadVersions(input.strategyProfileId)
      }
      return v
    } catch (err) {
      handleError(err)
      return null
    } finally {
      actionRunning.value = false
    }
  }

  /** 更新 DRAFT（需 edit 权限） */
  async function updateDraft(
    versionId: number,
    input: StrategyVersionDraftInput
  ): Promise<boolean> {
    if (!canWrite.value) {
      error.value = '当前操作者无更新权限（需权限码 aps.strategy.edit）'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      await strategyProfileApi.updateVersion(versionId, input)
      if (selectedProfileId.value !== null) {
        await loadVersions(selectedProfileId.value)
      }
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      actionRunning.value = false
    }
  }

  /** 发布（需 publish 权限） */
  async function publish(
    versionId: number,
    request: PublishStrategyVersionRequest
  ): Promise<boolean> {
    if (!canPublishPerm.value) {
      error.value = '当前操作者无发布权限（需权限码 aps.strategy.publish）'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      await strategyProfileApi.publishVersion(versionId, request)
      if (selectedProfileId.value !== null) {
        await loadVersions(selectedProfileId.value)
      }
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      actionRunning.value = false
    }
  }

  /** 退役（需 edit 权限） */
  async function disable(
    versionId: number,
    request: DisableStrategyVersionRequest
  ): Promise<boolean> {
    if (!canWrite.value) {
      error.value = '当前操作者无退役权限（需权限码 aps.strategy.edit）'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      await strategyProfileApi.disableVersion(versionId, request)
      if (selectedProfileId.value !== null) {
        await loadVersions(selectedProfileId.value)
      }
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      actionRunning.value = false
    }
  }

  /** Run 引用追溯 */
  async function loadTrace(versionId: number): Promise<void> {
    error.value = null
    try {
      runTrace.value = await strategyProfileApi.getRunTrace(versionId)
    } catch (err) {
      handleError(err)
      runTrace.value = null
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
    profiles.value = []
    selectedProfileId.value = null
    versions.value = []
    selectedVersionId.value = null
    currentDiff.value = null
    publishValidation.value = null
    runTrace.value = null
    error.value = null
  }

  return {
    // state
    profiles,
    selectedProfileId,
    versions,
    selectedVersionId,
    currentDiff,
    publishValidation,
    runTrace,
    loading,
    listLoading,
    versionsLoading,
    actionRunning,
    validating,
    error,
    // getters
    currentProfile,
    currentVersion,
    currentStatus,
    canEdit,
    canPublishAction,
    canRetire,
    canWrite,
    canPublishPerm,
    hasPublished,
    statusCounts,
    // actions
    selectProfile,
    selectVersion,
    loadProfiles,
    loadVersions,
    loadDiff,
    validateDraft,
    createDraft,
    updateDraft,
    publish,
    disable,
    loadTrace,
    reset,
    handleError
  }
})
