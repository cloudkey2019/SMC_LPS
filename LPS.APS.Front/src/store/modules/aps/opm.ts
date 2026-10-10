/**
 * APS V1 4号位 — OperationPlanningMode（OPM）Pinia store
 *
 * 3号位 2026-09-23 交付件（T1）：
 *   frontNew/docs/APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md
 *
 * 状态：
 *  - list：当前选中物料下的 RoutingOperation 列表（按 operationCode 升序）
 *  - currentMaterialId：当前加载的物料 Id（null = 未加载）
 *  - loading / saving / error：UI 态
 *
 * 权限（前端按钮显隐，对齐后端 PermissionCodes.cs aps.rule.* 三码）：
 *  - aps.rule.view → 查看（列表）
 *  - aps.rule.edit → 编辑（修改 OPM）
 *  - 注：路由级 apsRequiredPermissions = ['aps.rule.edit']（与 Rules.vue 路由级 OR 语义一致，
 *    后端类级 [Authorize(Policy = PermissionCodes.RuleMaintain = "aps.rule.edit")]）
 *
 * 红线：
 *  - OPM 属 APS 治理属性（第三类，0号位 2026-09-23 Q1 裁决）
 *  - sp_SyncRoutingData 不 MERGE OPM，治理值不被同步覆盖
 *  - 仅 DML（UPDATE 值），无 DDL
 *  - 写操作走 Controller（PUT /planning-mode），前端不直接 UPDATE 表
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  opmApi,
  ApiError,
  type RoutingOperationDto,
  type OperationPlanningMode,
  type UpdateOperationPlanningModeInput,
  OPERATION_PLANNING_MODES
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

export const useOpmStore = defineStore('aps.opm', () => {
  // ===== state =====
  const list = ref<RoutingOperationDto[]>([])
  const currentMaterialId = ref<number | null>(null)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref<string | null>(null)

  // ===== auth =====
  const apsAuth = useApsAuthStore()

  // ===== getters =====

  /** 查看权限（与路由级 view 码一致） */
  const canView = computed<boolean>(() => apsAuth.has('aps.rule.view'))

  /** 编辑权限（修改 OPM 的按钮显隐依据） */
  const canEdit = computed<boolean>(() => apsAuth.has('aps.rule.edit'))

  /** 当前列表三态分布（顶部 KPI 用） */
  const modeCounts = computed<Record<OperationPlanningMode, number>>(() => {
    const c: Record<OperationPlanningMode, number> = {
      FINITE_RESOURCE: 0,
      UNCONSTRAINED: 0,
      WAIT_ONLY: 0
    }
    list.value.forEach((op) => {
      const mode = op.operationPlanningMode
      if (OPERATION_PLANNING_MODES.includes(mode)) {
        c[mode] = (c[mode] ?? 0) + 1
      }
    })
    return c
  })

  // ===== actions =====

  /** 加载指定物料下的全部 RoutingOperation
   *  - 后端 materialId 必填（int > 0）；0 / 负数 → 直接抛错，不发请求
   */
  async function load(materialId: number): Promise<void> {
    if (!Number.isFinite(materialId) || materialId <= 0) {
      handleError(new ApiError('物料 Id 必须为正整数', 400))
      return
    }
    loading.value = true
    error.value = null
    try {
      const next = await opmApi.listOperations(materialId)
      // 服务端列表按工序号升序（UI 稳定）；前端再排一遍防后端未稳定排序
      list.value = [...next].sort((a, b) => a.operationCode.localeCompare(b.operationCode))
      currentMaterialId.value = materialId
    } catch (err) {
      handleError(err)
      list.value = []
      currentMaterialId.value = null
    } finally {
      loading.value = false
    }
  }

  /** 更新单条 OPM
   *  - 输入 { operationId, operationPlanningMode }
   *  - 成功后将后端回包回写到 list 中相应行（前端乐观更新被覆盖，以服务端为准）
   */
  async function updatePlanningMode(
    input: UpdateOperationPlanningModeInput
  ): Promise<RoutingOperationDto | null> {
    if (!canEdit.value) {
      handleError(new ApiError('当前操作者无 OPM 编辑权限（需 aps.rule.edit）', 403))
      return null
    }
    saving.value = true
    error.value = null
    try {
      const result = await opmApi.updatePlanningMode(input)
      const idx = list.value.findIndex((op) => op.id === result.id)
      if (idx >= 0) {
        list.value[idx] = result
      } else {
        // 兜底：若后端回包行不在当前 list（如并发加载），追加到尾部
        list.value = [...list.value, result]
      }
      return result
    } catch (err) {
      handleError(err)
      return null
    } finally {
      saving.value = false
    }
  }

  /** 清空状态（页面切换时调用，避免内存泄漏 / 跨物料残留） */
  function reset(): void {
    list.value = []
    currentMaterialId.value = null
    error.value = null
  }

  // ===== helpers =====

  function handleError(err: unknown): void {
    if (err instanceof ApiError) {
      error.value = `${err.message}${err.traceId ? `（traceId=${err.traceId}）` : ''}`
    } else if (err instanceof Error) {
      error.value = err.message
    } else {
      error.value = String(err)
    }
  }

  return {
    // state
    list,
    currentMaterialId,
    loading,
    saving,
    error,
    // getters
    canView,
    canEdit,
    modeCounts,
    // actions
    load,
    updatePlanningMode,
    reset,
    handleError
  }
})
