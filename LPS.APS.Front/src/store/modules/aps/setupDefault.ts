/**
 * APS V1 4号位 — Setup DEFAULT 规则 Pinia store（v1.5 Setup 专项 §4）
 *
 * 状态：
 *  - rules：DEFAULT 规则列表（当前 RuleSetVersion 下）
 *  - selectedRuleSetVersionId：当前选中 RuleSetVersion（DRAFT 状态可编辑）
 *  - ruleSetVersions：RuleSetVersion 下拉源
 *  - loading 系列：UI 态
 *
 * 权限（前端按钮显隐，对齐后端 PermissionCodes.cs 三码）：
 *  - aps.setup.view     → 查看（列表 / 详情）
 *  - aps.setup.edit     → 编辑（创建 / 更新 / 删除 DEFAULT 规则）
 *  - aps.setup.publish  → 发布（POST /rule-set-versions/{id}/publish）
 *
 * 红线（§7.1 + §7.2）：
 *  - 仅 DRAFT 状态的 RuleSetVersion 可编辑（PUBLISHED/ACTIVE 不可改）
 *  - SetupMinutes > 0
 *  - FromMaterialCode/ToMaterialCode 都必须为空（与 EXACT 互斥）
 *
 * 与 EXACT 的区别（§4.2）：
 *  - DEFAULT 5 元组：Department/Stage/Operation/Resource + RuleSetVersionId
 *  - EXACT 7 元组：+ FromMaterial/ToMaterial
 *  - DEFAULT 不需要共同合法设备推荐（无前后产品）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  setupApi,
  ApiError,
  type SetupRuleDto,
  type SetupRuleDefaultInput,
  type SetupRuleSetVersionDto
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

export const useSetupDefaultStore = defineStore('aps.setupDefault', () => {
  // ===== state =====
  const rules = ref<SetupRuleDto[]>([])
  const selectedRuleSetVersionId = ref<number | null>(null)
  const ruleSetVersions = ref<SetupRuleSetVersionDto[]>([])

  const listLoading = ref(false)
  const versionsLoading = ref(false)
  const actionRunning = ref(false)
  const error = ref<string | null>(null)

  // ===== auth =====
  const apsAuth = useApsAuthStore()

  // ===== getters =====

  /** 当前选中的 RuleSetVersion */
  const currentRuleSetVersion = computed<SetupRuleSetVersionDto | null>(() => {
    if (selectedRuleSetVersionId.value === null) return null
    return ruleSetVersions.value.find((v) => v.id === selectedRuleSetVersionId.value) ?? null
  })

  /** 是否可编辑（仅 DRAFT 状态的 RuleSetVersion） */
  const canEdit = computed<boolean>(() => currentRuleSetVersion.value?.status === 'DRAFT')

  /** 任意写权限 */
  const canWrite = computed<boolean>(() => apsAuth.has('aps.setup.edit'))

  /** 发布权限 */
  const canPublishPerm = computed<boolean>(() => apsAuth.has('aps.setup.publish'))

  /** 规则总数 */
  const ruleCount = computed<number>(() => rules.value.length)

  // ===== actions =====

  function selectRuleSetVersion(versionId: number | null): void {
    selectedRuleSetVersionId.value = versionId
    rules.value = []
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

  async function loadRules(ruleSetVersionId: number): Promise<void> {
    listLoading.value = true
    error.value = null
    try {
      rules.value = await setupApi.listDefaultRules(ruleSetVersionId)
    } catch (err) {
      handleError(err)
      rules.value = []
    } finally {
      listLoading.value = false
    }
  }

  /** 创建 DEFAULT 规则（需 edit 权限 + DRAFT 状态） */
  async function createRule(input: SetupRuleDefaultInput): Promise<SetupRuleDto | null> {
    if (!canWrite.value) {
      error.value = '当前操作者无创建权限（需权限码 aps.setup.edit）'
      return null
    }
    if (!canEdit.value) {
      error.value = '只能编辑 DRAFT 状态的 RuleSetVersion（§7.2 业务红线）'
      return null
    }
    actionRunning.value = true
    error.value = null
    try {
      const rule = await setupApi.createDefaultRule(input)
      if (selectedRuleSetVersionId.value === input.ruleSetVersionId) {
        await loadRules(input.ruleSetVersionId)
      }
      return rule
    } catch (err) {
      handleError(err)
      return null
    } finally {
      actionRunning.value = false
    }
  }

  /** 更新 DEFAULT 规则（需 edit 权限 + DRAFT 状态） */
  async function updateRule(id: number, input: SetupRuleDefaultInput): Promise<boolean> {
    if (!canWrite.value) {
      error.value = '当前操作者无更新权限（需权限码 aps.setup.edit）'
      return false
    }
    if (!canEdit.value) {
      error.value = '只能编辑 DRAFT 状态的 RuleSetVersion（§7.2 业务红线）'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      await setupApi.updateDefaultRule(id, input)
      if (selectedRuleSetVersionId.value !== null) {
        await loadRules(selectedRuleSetVersionId.value)
      }
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      actionRunning.value = false
    }
  }

  /** 删除 DEFAULT 规则（需 edit 权限 + DRAFT 状态） */
  async function deleteRule(id: number): Promise<boolean> {
    if (!canWrite.value) {
      error.value = '当前操作者无删除权限（需权限码 aps.setup.edit）'
      return false
    }
    if (!canEdit.value) {
      error.value = '只能编辑 DRAFT 状态的 RuleSetVersion（§7.2 业务红线）'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      await setupApi.deleteDefaultRule(id)
      if (selectedRuleSetVersionId.value !== null) {
        await loadRules(selectedRuleSetVersionId.value)
      }
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
    rules.value = []
    selectedRuleSetVersionId.value = null
    ruleSetVersions.value = []
    error.value = null
  }

  return {
    // state
    rules,
    selectedRuleSetVersionId,
    ruleSetVersions,
    listLoading,
    versionsLoading,
    actionRunning,
    error,
    // getters
    currentRuleSetVersion,
    canEdit,
    canWrite,
    canPublishPerm,
    ruleCount,
    // actions
    selectRuleSetVersion,
    loadRuleSetVersions,
    loadRules,
    createRule,
    updateRule,
    deleteRule,
    handleError,
    reset
  }
})
