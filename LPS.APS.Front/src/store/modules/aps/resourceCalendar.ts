/**
 * APS V1 4号位 — 设备资源日历 + 人工能力槽 Pinia store
 *
 * 依据：
 *  - 冻结文档 v1.3《Resource Calendar 资源日历能力补充冻结方案 v1.3》§三/§五/§八/§九
 *  - 5号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》
 *
 * 结构（单页 3 Tab）：
 *  - Tab1 设备资源日历：resourceCandidates → 选中 resourceId → deviceWindows
 *  - Tab2 人工能力槽  ：slots（部门/工序过滤 + 含软删开关）
 *  - Tab3 人工槽日历  ：由 Tab2 选中行驱动 slotWindows
 *
 * 窗口按 id 缓存（`deviceWindowCache` / `slotWindowCache`）：
 *  - 命中缓存 = 已查证 → 可判"已配/未配"（v1.3 §九：无有效日历 = 不可用）
 *  - 未命中 = 未查证（G4：后端无 hasCalendar 字段，列表级无法批量判定）
 *
 * 权限（按钮显隐，对齐后端 PermissionCodes.cs:82-96 六码）：
 *  - 设备：aps.resource_calendar.{view,edit,delete}
 *  - 人工：aps.manual_capacity.{view,edit,delete}
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  ApiError,
  MANUAL_CAPACITY_PERMISSIONS,
  RESOURCE_CALENDAR_PERMISSIONS,
  resourceCalendarApi,
  type CalendarConfigStatus,
  type DepartmentCandidate,
  type ManualCapacitySlotDto,
  type ManualSlotCalendarBulkRequest,
  type ManualSlotCalendarDto,
  type ManualSlotQuery,
  type ResourceCalendarBulkRequest,
  type ResourceCalendarEntryDto,
  type ResourceCandidate
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

/** 窗口列表归属状态：未查证 / 已配 / 未配 */
function statusOf(cache: Record<number, unknown[]>, key: number): CalendarConfigStatus {
  const hit = cache[key]
  if (hit === undefined) return 'UNKNOWN'
  return hit.length > 0 ? 'CONFIGURED' : 'NOT_CONFIGURED'
}

export const useResourceCalendarStore = defineStore('aps.resourceCalendar', () => {
  /* ===== auth ===== */
  const apsAuth = useApsAuthStore()

  /* ===== Tab1：设备资源日历 ===== */
  const resourceCandidates = ref<ResourceCandidate[]>([])
  const candidateNote = ref<string>('')
  const candidateIsFallback = ref<boolean>(true)
  const candidateLoading = ref(false)
  const currentResourceId = ref<number | null>(null)
  const deviceWindowCache = ref<Record<number, ResourceCalendarEntryDto[]>>({})

  /* ===== Tab2/3：人工能力槽 + 人工槽日历 ===== */
  const slots = ref<ManualCapacitySlotDto[]>([])
  const slotQuery = ref<ManualSlotQuery>({})
  const slotsLoading = ref(false)
  const departmentCandidates = ref<DepartmentCandidate[]>([])
  const currentSlotId = ref<number | null>(null)
  const slotWindowCache = ref<Record<number, ManualSlotCalendarDto[]>>({})

  /* ===== 公共 UI 态 ===== */
  const loading = ref(false)
  const saving = ref(false)
  const error = ref<string | null>(null)

  /* ===== getters：权限（按钮显隐） ===== */
  const canViewDevice = computed(() => apsAuth.has(RESOURCE_CALENDAR_PERMISSIONS.view))
  const canEditDevice = computed(() => apsAuth.has(RESOURCE_CALENDAR_PERMISSIONS.edit))
  const canDeleteDevice = computed(() => apsAuth.has(RESOURCE_CALENDAR_PERMISSIONS.delete))
  const canViewManual = computed(() => apsAuth.has(MANUAL_CAPACITY_PERMISSIONS.view))
  const canEditManual = computed(() => apsAuth.has(MANUAL_CAPACITY_PERMISSIONS.edit))
  const canDeleteManual = computed(() => apsAuth.has(MANUAL_CAPACITY_PERMISSIONS.delete))

  /* ===== getters：数据 ===== */

  /** 当前设备的窗口（未选/未查证 → 空数组） */
  const deviceWindows = computed<ResourceCalendarEntryDto[]>(() => {
    const id = currentResourceId.value
    if (id == null) return []
    return deviceWindowCache.value[id] ?? []
  })

  /** 当前设备窗口是否已查证 */
  const deviceWindowsLoaded = computed<boolean>(() => {
    const id = currentResourceId.value
    return id != null && deviceWindowCache.value[id] !== undefined
  })

  /** 当前设备的 Calendar 状态（v1.3 §九） */
  const deviceCalendarStatus = computed<CalendarConfigStatus>(() => {
    const id = currentResourceId.value
    if (id == null) return 'UNKNOWN'
    return statusOf(deviceWindowCache.value, id)
  })

  /** 设备进度总览（缓存命中的才计入） */
  const deviceStatusCount = computed(() => {
    const values = Object.values(deviceWindowCache.value)
    return {
      checked: values.length,
      configured: values.filter((v) => v.length > 0).length,
      notConfigured: values.filter((v) => v.length === 0).length
    }
  })

  /** 某人工槽的 Calendar 状态（列表行徽章用；UNKNOWN = 未查证） */
  function slotCalendarStatus(manualSlotId: number): CalendarConfigStatus {
    return statusOf(slotWindowCache.value, manualSlotId)
  }

  /** 当前人工槽的窗口 */
  const slotWindows = computed<ManualSlotCalendarDto[]>(() => {
    const id = currentSlotId.value
    if (id == null) return []
    return slotWindowCache.value[id] ?? []
  })

  /** 当前人工槽对象（Tab3 头部展示） */
  const currentSlot = computed<ManualCapacitySlotDto | null>(() => {
    const id = currentSlotId.value
    if (id == null) return null
    return slots.value.find((s) => s.manualSlotId === id) ?? null
  })

  /* ===== actions：Tab1 ===== */

  /** 加载资源候选（过渡数据源；G1 正式端点缺失） */
  async function loadResourceCandidates(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const res = await resourceCalendarApi.listResourceCandidates()
      resourceCandidates.value = res.list
      candidateNote.value = res.note
      candidateIsFallback.value = res.isFallback
    } catch (err) {
      handleError(err)
      resourceCandidates.value = []
    } finally {
      loading.value = false
    }
  }

  /** 加载生产部门候选（过渡数据源；G2 正式端点缺失） */
  async function loadDepartmentCandidates(): Promise<void> {
    try {
      departmentCandidates.value = await resourceCalendarApi.listDepartmentCandidates()
    } catch (err) {
      handleError(err)
      departmentCandidates.value = []
    }
  }

  /** 查询某设备的窗口（写入缓存；重复查询覆盖） */
  async function loadDeviceWindows(resourceId: number): Promise<void> {
    if (!Number.isFinite(resourceId) || resourceId <= 0) {
      handleError(new ApiError('资源 Id 必须为正整数', 400))
      return
    }
    loading.value = true
    error.value = null
    currentResourceId.value = resourceId
    try {
      const rows = await resourceCalendarApi.getWindows(resourceId)
      deviceWindowCache.value = { ...deviceWindowCache.value, [resourceId]: rows }
    } catch (err) {
      handleError(err)
      const next = { ...deviceWindowCache.value }
      delete next[resourceId]
      deviceWindowCache.value = next
    } finally {
      loading.value = false
    }
  }

  /** 批量铺设备窗口（追加语义；后端不删旧窗口 → 见发函 D2） */
  async function createDeviceWindows(req: ResourceCalendarBulkRequest): Promise<number | null> {
    if (!canEditDevice.value) {
      handleError(new ApiError('无设备日历编辑权限（需 aps.resource_calendar.edit）', 403))
      return null
    }
    saving.value = true
    error.value = null
    try {
      const inserted = await resourceCalendarApi.bulkCreateWindows(req)
      await loadDeviceWindows(req.resourceId)
      return inserted
    } catch (err) {
      handleError(err)
      return null
    } finally {
      saving.value = false
    }
  }

  /** 物理删除设备窗口 */
  async function removeDeviceWindow(id: number, resourceId: number): Promise<boolean> {
    if (!canDeleteDevice.value) {
      handleError(new ApiError('无设备日历删除权限（需 aps.resource_calendar.delete）', 403))
      return false
    }
    saving.value = true
    error.value = null
    try {
      await resourceCalendarApi.deleteWindow(id)
      await loadDeviceWindows(resourceId)
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      saving.value = false
    }
  }

  /* ===== actions：Tab2 / Tab3 ===== */

  /** 查询人工槽主档（默认不含软删） */
  async function loadSlots(query?: ManualSlotQuery): Promise<void> {
    if (query) slotQuery.value = query
    loading.value = true
    error.value = null
    try {
      slots.value = await resourceCalendarApi.getSlots(slotQuery.value)
      // 当前选中槽若已不在列表（被软删 / 被过滤）→ 清空 Tab3
      const id = currentSlotId.value
      if (id != null && !slots.value.some((s) => s.manualSlotId === id)) {
        const next = { ...slotWindowCache.value }
        delete next[id]
        slotWindowCache.value = next
        currentSlotId.value = null
      }
    } catch (err) {
      handleError(err)
      slots.value = []
    } finally {
      loading.value = false
    }
  }

  /** 新增人工槽主档 */
  async function createSlot(slot: ManualCapacitySlotDto): Promise<ManualCapacitySlotDto | null> {
    if (!canEditManual.value) {
      handleError(new ApiError('无人工能力槽编辑权限（需 aps.manual_capacity.edit）', 403))
      return null
    }
    saving.value = true
    error.value = null
    try {
      const created = await resourceCalendarApi.createSlot(slot)
      await loadSlots()
      return created
    } catch (err) {
      handleError(err)
      return null
    } finally {
      saving.value = false
    }
  }

  /** 软删人工槽主档（isActive=0；已有窗口不自动清 —— 对接函 §三.4） */
  async function disableSlot(manualSlotId: number): Promise<boolean> {
    if (!canDeleteManual.value) {
      handleError(new ApiError('无人工能力槽删除权限（需 aps.manual_capacity.delete）', 403))
      return false
    }
    saving.value = true
    error.value = null
    try {
      await resourceCalendarApi.softDeleteSlot(manualSlotId)
      await loadSlots()
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      saving.value = false
    }
  }

  /** 查询某人工槽的窗口（写缓存） */
  async function loadSlotWindows(manualSlotId: number): Promise<void> {
    if (!Number.isFinite(manualSlotId) || manualSlotId <= 0) {
      handleError(new ApiError('人工槽 Id 必须为正整数', 400))
      return
    }
    loading.value = true
    error.value = null
    currentSlotId.value = manualSlotId
    try {
      const rows = await resourceCalendarApi.getSlotWindows(manualSlotId)
      slotWindowCache.value = { ...slotWindowCache.value, [manualSlotId]: rows }
    } catch (err) {
      handleError(err)
      const next = { ...slotWindowCache.value }
      delete next[manualSlotId]
      slotWindowCache.value = next
    } finally {
      loading.value = false
    }
  }

  /** 批量铺人工槽窗口（追加语义） */
  async function createSlotWindows(req: ManualSlotCalendarBulkRequest): Promise<number | null> {
    if (!canEditManual.value) {
      handleError(new ApiError('无人工能力槽编辑权限（需 aps.manual_capacity.edit）', 403))
      return null
    }
    saving.value = true
    error.value = null
    try {
      const inserted = await resourceCalendarApi.bulkCreateSlotWindows(req)
      await loadSlotWindows(req.manualSlotId)
      return inserted
    } catch (err) {
      handleError(err)
      return null
    } finally {
      saving.value = false
    }
  }

  /** 物理删除人工槽窗口 */
  async function removeSlotWindow(id: number, manualSlotId: number): Promise<boolean> {
    if (!canDeleteManual.value) {
      handleError(new ApiError('无人工能力槽删除权限（需 aps.manual_capacity.delete）', 403))
      return false
    }
    saving.value = true
    error.value = null
    try {
      await resourceCalendarApi.deleteSlotWindow(id)
      await loadSlotWindows(manualSlotId)
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      saving.value = false
    }
  }

  /* ===== 生命周期 ===== */

  function reset(): void {
    currentResourceId.value = null
    deviceWindowCache.value = {}
    slots.value = []
    slotQuery.value = {}
    currentSlotId.value = null
    slotWindowCache.value = {}
    error.value = null
  }

  function clearError(): void {
    error.value = null
  }

  /* ===== helpers ===== */

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
    resourceCandidates,
    candidateNote,
    candidateIsFallback,
    candidateLoading,
    currentResourceId,
    deviceWindowCache,
    slots,
    slotQuery,
    slotsLoading,
    departmentCandidates,
    currentSlotId,
    slotWindowCache,
    loading,
    saving,
    error,
    // getters：权限
    canViewDevice,
    canEditDevice,
    canDeleteDevice,
    canViewManual,
    canEditManual,
    canDeleteManual,
    // getters：数据
    deviceWindows,
    deviceWindowsLoaded,
    deviceCalendarStatus,
    deviceStatusCount,
    slotCalendarStatus,
    slotWindows,
    currentSlot,
    // actions
    loadResourceCandidates,
    loadDepartmentCandidates,
    loadDeviceWindows,
    createDeviceWindows,
    removeDeviceWindow,
    loadSlots,
    createSlot,
    disableSlot,
    loadSlotWindows,
    createSlotWindows,
    removeSlotWindow,
    reset,
    clearError,
    handleError
  }
})
