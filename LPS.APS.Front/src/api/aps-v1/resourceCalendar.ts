/**
 * APS V1 4号位 — 设备资源日历 + 人工能力槽 API（9 端点）
 *
 * 依据：
 *  - 冻结文档 v1.3《Resource Calendar 资源日历能力补充冻结方案 v1.3》§三/§五/§九
 *  - 5号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》
 *
 * 后端（lps/** 只读参照）：
 *  - ResourceCalendarController.cs  Route("api/resource-calendar")
 *      POST   /slots                    aps.resource_calendar.edit
 *      GET    /{resourceId}?from&to     aps.resource_calendar.view
 *      DELETE /slots/{id}               aps.resource_calendar.delete
 *  - ManualCapacityController.cs    Route("api/manual-capacity")（类级 view 码兜底）
 *      POST   /slots                    aps.manual_capacity.edit
 *      GET    /slots?departmentId&operationName&includeInactive   aps.manual_capacity.view
 *      DELETE /slots/{manualSlotId}     aps.manual_capacity.delete（软删 isActive=0）
 *      POST   /calendar                 aps.manual_capacity.edit
 *      GET    /calendar/{manualSlotId}  aps.manual_capacity.view
 *      DELETE /calendar/{id}            aps.manual_capacity.delete（物理删）
 *
 * 已知缺口（已发函 5号位 2026-09-24）：
 *  - G1：无资源主表列表端点 → `listResourceCandidates()` 走**过渡数据源**（计划版本甘特资源池）
 *  - G2：无生产部门列表端点 → `listDepartmentCandidates()` 从过渡资源池去重
 *  - G4：DTO 无 hasCalendar → 状态由"窗口数组空"推导，且仅选中行才查询
 *  - D2：铺窗后端只 INSERT 不删旧窗口 → 前端提示"追加"，不承诺"覆盖"
 *  - D3：from/to 为"完整包含"语义 → V1 不传，前端本地筛选
 */

import { apsHttp, APS_USE_MOCK, ApiError } from './http'
import { scheduleApi } from './schedule'
import type {
  ManualCapacitySlotDto,
  ManualSlotCalendarBulkRequest,
  ManualSlotCalendarDto,
  ManualSlotQuery,
  ResourceCalendarBulkRequest,
  ResourceCalendarEntryDto,
  ResourceCandidate
} from './types/resourceCalendar'
import { BULK_DAYS_MAX, BULK_DAYS_MIN } from './types/resourceCalendar'

/* ==================== Mock Fixtures（仅离线 UI 演示） ==================== */

/** mock 设备窗口：resourceId=1 已配 2 窗（含 1 条禁用），resourceId=2 未配（演示"未配=不可用"） */
const MOCK_DEVICE_WINDOWS: Record<number, ResourceCalendarEntryDto[]> = {
  1: [
    {
      id: 900001,
      resourceId: 1,
      resourceCode: 'MC01',
      resourceName: '注塑机-01',
      startTime: '2026-09-25T08:00:00',
      endTime: '2026-09-25T17:00:00',
      availableFlag: true,
      remark: '白班'
    },
    {
      id: 900002,
      resourceId: 1,
      resourceCode: 'MC01',
      resourceName: '注塑机-01',
      startTime: '2026-09-25T17:00:00',
      // 与真实后端一致：窗口不得跨天（StartTime/EndTime 为 HH:mm:ss，跨天须拆两条）
      endTime: '2026-09-25T22:00:00',
      availableFlag: false,
      remark: '夜班停用'
    }
  ],
  // resourceId=2（MC02）故意无边 → 演示"Calendar 未配 = 不可用"
  2: []
}

let mockDeviceWindowSeq = 900100

/** mock 人工槽主档（覆盖 2 个生产部门 / 3 个工序名，含 1 条软删） */
const MOCK_SLOTS: ManualCapacitySlotDto[] = [
  {
    manualSlotId: 18376,
    productionDepartmentId: 1,
    departmentName: '注塑车间',
    operationName: '精修',
    slotCode: '精修01',
    isActive: true
  },
  {
    manualSlotId: 18377,
    productionDepartmentId: 1,
    departmentName: '注塑车间',
    operationName: '精修',
    slotCode: '精修02',
    isActive: true
  },
  {
    manualSlotId: 18378,
    productionDepartmentId: 3,
    departmentName: '装配车间',
    operationName: '装配',
    slotCode: '装配01',
    isActive: true
  },
  {
    manualSlotId: 18379,
    productionDepartmentId: 3,
    departmentName: '装配车间',
    operationName: '装配',
    slotCode: '装配02',
    isActive: false
  }
]

let mockSlotSeq = 18380

/** mock 人工槽窗口：18376 已配；18377 未配（演示"未配=不可用"） */
const MOCK_SLOT_WINDOWS: Record<number, ManualSlotCalendarDto[]> = {
  18376: [
    {
      id: 910001,
      manualSlotId: 18376,
      startTime: '2026-09-25T08:00:00',
      endTime: '2026-09-25T12:00:00',
      availableFlag: true,
      remark: '上午班'
    },
    {
      id: 910002,
      manualSlotId: 18376,
      startTime: '2026-09-25T13:00:00',
      endTime: '2026-09-25T17:00:00',
      availableFlag: true,
      remark: '下午班'
    }
  ],
  18377: [],
  18378: [
    {
      id: 910003,
      manualSlotId: 18378,
      startTime: '2026-09-25T08:00:00',
      endTime: '2026-09-25T17:00:00',
      availableFlag: true,
      remark: '白班'
    }
  ],
  18379: []
}

let mockSlotWindowSeq = 910100

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** mock 侧参数校验（对齐后端 ResourceCalendarService.cs:38-43 / ManualCapacityService.cs:111-116） */
function assertBulkArgs(days: number, startTime: string, endTime: string): void {
  if (!Number.isFinite(days) || days < BULK_DAYS_MIN || days > BULK_DAYS_MAX) {
    throw new ApiError(`天数必须在 ${BULK_DAYS_MIN}~${BULK_DAYS_MAX} 之间（当前 ${days}）`, 400)
  }
  if (startTime >= endTime) {
    throw new ApiError(`结束时间必须晚于开始时间（${startTime} → ${endTime}）`, 400)
  }
}

/* ==================== 过渡数据源（G1 / G2 未决） ==================== */

export interface ResourceCandidateResult {
  list: ResourceCandidate[]
  /** 过渡数据源标记：true = 来自计划版本甘特资源池（可能遗漏未排资源） */
  isFallback: boolean
  /** 过渡数据源所用的计划版本 Id（null = 无可用版本） */
  planVersionId: number | null
  /** 过渡数据源说明（UI 直接展示） */
  note: string
}

/**
 * 资源（设备）候选列表
 *  ⚠️ 过渡实现：正式端点 `GET /api/resource` 缺失（发函 G1），改从最新计划版本的甘特资源池取
 *  - 优先 ACTIVE 版本，无则取列表第一条
 *  - 只含被排到的资源 → 标注 isFallback 供 UI 明示
 */
async function listResourceCandidates(): Promise<ResourceCandidateResult> {
  const versions = await scheduleApi.getVersions(30)
  const picked =
    versions.find((v) => v.status === 'ACTIVE') ?? (versions.length > 0 ? versions[0] : undefined)
  if (!picked) {
    return {
      list: [],
      isFallback: true,
      planVersionId: null,
      note: '无可用计划版本，无法带入资源池；请手填资源 Id'
    }
  }
  const gantt = await scheduleApi.getGantt(picked.id)
  const seen = new Set<number>()
  const list: ResourceCandidate[] = []
  gantt.resources.forEach((r) => {
    if (seen.has(r.resourceId)) return
    seen.add(r.resourceId)
    list.push({
      resourceId: r.resourceId,
      resourceCode: r.resourceCode,
      resourceName: r.resourceName,
      productionDepartmentId: r.productionDepartmentId,
      productionDepartmentName: r.productionDepartmentName,
      isFallbackSource: true
    })
  })
  return {
    list,
    isFallback: true,
    planVersionId: picked.id,
    note: `资源池来自计划版本 ${picked.id}（${picked.versionCode}）的甘特图，仅含已被排到的资源；未列出的资源请用「手填资源 Id」加载`
  }
}

/** 生产部门候选（G2 未决：正式端点缺失，从过渡资源池去重） */
export interface DepartmentCandidate {
  productionDepartmentId: number
  productionDepartmentName?: string
}

async function listDepartmentCandidates(): Promise<DepartmentCandidate[]> {
  const res = await listResourceCandidates()
  const map = new Map<number, DepartmentCandidate>()
  res.list.forEach((r) => {
    if (r.productionDepartmentId == null) return
    if (!map.has(r.productionDepartmentId)) {
      map.set(r.productionDepartmentId, {
        productionDepartmentId: r.productionDepartmentId,
        productionDepartmentName: r.productionDepartmentName
      })
    }
  })
  return [...map.values()].sort((a, b) => a.productionDepartmentId - b.productionDepartmentId)
}

/* ==================== API ==================== */

export const resourceCalendarApi = {
  /* ---------- 模块 A：设备资源日历 ---------- */

  /**
   * 批量铺窗（POST /api/resource-calendar/slots）→ 返回生成窗口数
   *  ⚠️ 后端只 INSERT 不删旧窗口（发函 D2）→ UI 须提示"追加"，不承诺覆盖
   */
  async bulkCreateWindows(req: ResourceCalendarBulkRequest): Promise<number> {
    if (APS_USE_MOCK) {
      await delay(300)
      if (req.resourceId <= 0) throw new ApiError('资源 Id 必须为正整数', 400)
      assertBulkArgs(req.days, req.startTime, req.endTime)
      const bucket = MOCK_DEVICE_WINDOWS[req.resourceId] ?? []
      for (let i = 0; i < req.days; i += 1) {
        const day = addDays(req.startDate, i)
        bucket.push({
          id: (mockDeviceWindowSeq += 1),
          resourceId: req.resourceId,
          startTime: `${day}T${req.startTime}`,
          endTime: `${day}T${req.endTime}`,
          availableFlag: req.availableFlag,
          remark: req.remark
        })
      }
      MOCK_DEVICE_WINDOWS[req.resourceId] = bucket
      return req.days
    }
    return apsHttp.post<number>({ url: '/api/resource-calendar/slots', data: req })
  },

  /**
   * 查询某设备的全部窗口（GET /api/resource-calendar/{resourceId}）
   *  ⚠️ from/to 为"窗口完整落在区间内"语义（发函 D3）→ V1 不传，前端本地筛选
   */
  async getWindows(resourceId: number): Promise<ResourceCalendarEntryDto[]> {
    if (APS_USE_MOCK) {
      await delay(200)
      return [...(MOCK_DEVICE_WINDOWS[resourceId] ?? [])]
    }
    return apsHttp.get<ResourceCalendarEntryDto[]>({
      url: `/api/resource-calendar/${resourceId}`
    })
  },

  /** 物理删除某设备窗口（DELETE /api/resource-calendar/slots/{id}） */
  async deleteWindow(id: number): Promise<boolean> {
    if (APS_USE_MOCK) {
      await delay(200)
      Object.keys(MOCK_DEVICE_WINDOWS).forEach((key) => {
        const rid = Number(key)
        MOCK_DEVICE_WINDOWS[rid] = (MOCK_DEVICE_WINDOWS[rid] ?? []).filter((w) => w.id !== id)
      })
      return true
    }
    return apsHttp.delete<boolean>({ url: `/api/resource-calendar/slots/${id}` })
  },

  /* ---------- 模块 B：人工能力槽 ---------- */

  /** 新增人工槽主档（POST /api/manual-capacity/slots）；唯一键重复 → 后端 400 */
  async createSlot(slot: ManualCapacitySlotDto): Promise<ManualCapacitySlotDto> {
    if (APS_USE_MOCK) {
      await delay(300)
      if (slot.productionDepartmentId <= 0) throw new ApiError('生产部门 Id 必须为正整数', 400)
      if (!slot.operationName?.trim()) throw new ApiError('小工序（工序名）必填', 400)
      if (!slot.slotCode?.trim()) throw new ApiError('能力槽编码必填', 400)
      const dup = MOCK_SLOTS.find(
        (s) =>
          s.isActive &&
          s.productionDepartmentId === slot.productionDepartmentId &&
          s.operationName === slot.operationName &&
          s.slotCode === slot.slotCode
      )
      if (dup) {
        throw new ApiError(
          `人工槽已存在：(部门 ${slot.productionDepartmentId}, 工序 ${slot.operationName}, 槽 ${slot.slotCode})`,
          400
        )
      }
      const created: ManualCapacitySlotDto = {
        manualSlotId: (mockSlotSeq += 1),
        productionDepartmentId: slot.productionDepartmentId,
        // 后端新增回包不含 departmentName（ManualCapacityService.cs:71-78），前端此处保持同语义
        operationName: slot.operationName,
        slotCode: slot.slotCode,
        isActive: true
      }
      MOCK_SLOTS.push(created)
      MOCK_SLOT_WINDOWS[created.manualSlotId] = []
      return created
    }
    return apsHttp.post<ManualCapacitySlotDto>({ url: '/api/manual-capacity/slots', data: slot })
  },

  /** 查询人工槽主档（GET /api/manual-capacity/slots）；默认不含软删 */
  async getSlots(query: ManualSlotQuery = {}): Promise<ManualCapacitySlotDto[]> {
    if (APS_USE_MOCK) {
      await delay(200)
      return MOCK_SLOTS.filter((s) => {
        if (!query.includeInactive && !s.isActive) return false
        if (query.departmentId != null && s.productionDepartmentId !== query.departmentId)
          return false
        if (query.operationName && s.operationName !== query.operationName) return false
        return true
      })
    }
    return apsHttp.get<ManualCapacitySlotDto[]>({
      url: '/api/manual-capacity/slots',
      params: {
        departmentId: query.departmentId,
        operationName: query.operationName,
        includeInactive: query.includeInactive ?? false
      }
    })
  },

  /** 软删人工槽主档（DELETE /api/manual-capacity/slots/{manualSlotId} → isActive=0，行保留） */
  async softDeleteSlot(manualSlotId: number): Promise<boolean> {
    if (APS_USE_MOCK) {
      await delay(200)
      const hit = MOCK_SLOTS.find((s) => s.manualSlotId === manualSlotId)
      if (!hit || !hit.isActive) return false
      hit.isActive = false
      return true
    }
    return apsHttp.delete<boolean>({ url: `/api/manual-capacity/slots/${manualSlotId}` })
  },

  /** 批量铺人工槽窗口（POST /api/manual-capacity/calendar）→ 返回生成窗口数 */
  async bulkCreateSlotWindows(req: ManualSlotCalendarBulkRequest): Promise<number> {
    if (APS_USE_MOCK) {
      await delay(300)
      if (req.manualSlotId <= 0) throw new ApiError('人工槽 Id 必须为正整数', 400)
      const slot = MOCK_SLOTS.find((s) => s.manualSlotId === req.manualSlotId)
      if (!slot || !slot.isActive) {
        throw new ApiError(`人工槽不存在或未启用：${req.manualSlotId}`, 400)
      }
      assertBulkArgs(req.days, req.startTime, req.endTime)
      const bucket = MOCK_SLOT_WINDOWS[req.manualSlotId] ?? []
      for (let i = 0; i < req.days; i += 1) {
        const day = addDays(req.startDate, i)
        bucket.push({
          id: (mockSlotWindowSeq += 1),
          manualSlotId: req.manualSlotId,
          startTime: `${day}T${req.startTime}`,
          endTime: `${day}T${req.endTime}`,
          availableFlag: req.availableFlag,
          remark: req.remark
        })
      }
      MOCK_SLOT_WINDOWS[req.manualSlotId] = bucket
      return req.days
    }
    return apsHttp.post<number>({ url: '/api/manual-capacity/calendar', data: req })
  },

  /** 查询某人工槽的全部窗口（GET /api/manual-capacity/calendar/{manualSlotId}） */
  async getSlotWindows(manualSlotId: number): Promise<ManualSlotCalendarDto[]> {
    if (APS_USE_MOCK) {
      await delay(200)
      return [...(MOCK_SLOT_WINDOWS[manualSlotId] ?? [])]
    }
    return apsHttp.get<ManualSlotCalendarDto[]>({
      url: `/api/manual-capacity/calendar/${manualSlotId}`
    })
  },

  /** 物理删除某人工槽窗口（DELETE /api/manual-capacity/calendar/{id}） */
  async deleteSlotWindow(id: number): Promise<boolean> {
    if (APS_USE_MOCK) {
      await delay(200)
      Object.keys(MOCK_SLOT_WINDOWS).forEach((key) => {
        const sid = Number(key)
        MOCK_SLOT_WINDOWS[sid] = (MOCK_SLOT_WINDOWS[sid] ?? []).filter((w) => w.id !== id)
      })
      return true
    }
    return apsHttp.delete<boolean>({ url: `/api/manual-capacity/calendar/${id}` })
  },

  /* ---------- 过渡数据源（G1 / G2 未决） ---------- */

  /** 资源（设备）候选 —— 过渡实现，见函数注释 */
  listResourceCandidates,

  /** 生产部门候选 —— 过渡实现（从资源池去重） */
  listDepartmentCandidates
}

/* ==================== helpers ==================== */

/** mock 专用：日期串 +i 天（保留 YYYY-MM-DD） */
function addDays(startDate: string, i: number): string {
  const d = new Date(`${startDate.slice(0, 10)}T00:00:00`)
  d.setDate(d.getDate() + i)
  return d.toISOString().slice(0, 10)
}
