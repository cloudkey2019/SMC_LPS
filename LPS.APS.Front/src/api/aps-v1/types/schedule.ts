/**
 * 排程结果查询 DTO（已与后端对齐）
 *
 * 来源：lps/LPS.APS.Application/Services/Query/Dto/
 *  - GanttDataDto.cs
 *  - PlanVersionSummaryDto.cs
 *  - ScheduleSummaryDto.cs
 *
 * 接口路径（来自 ScheduleController）：
 *  - GET /api/schedule/versions?take=30
 *  - GET /api/schedule/gantt/{planVersionId}
 *  - GET /api/schedule/summary/{planVersionId}
 */

import type {
  DomainKey,
  FactType,
  IsoDateTime,
  MesEligibility,
  PlanVersionStatus,
  TaskLockMarker,
  TaskStatus
} from './common'
import type { SetupSource } from './setup'

/** 计划版本摘要（版本下拉用） */
export interface PlanVersionSummaryDto {
  id: number
  versionCode: string
  versionCategory: string
  planHorizonStart: IsoDateTime
  planHorizonEnd: IsoDateTime
  status: PlanVersionStatus
  computedAt?: IsoDateTime
  totalTasks?: number
  createdAt: IsoDateTime
}

/** 资源行（甘特图 Y 轴） */
export interface GanttResourceDto {
  resourceId: number
  resourceCode: string
  resourceName: string
  /** 适用 Domain（U01：多 Domain 时按 Domain 染色/分组） */
  domainKey: DomainKey
  /** 所属工厂 */
  factoryId?: number
  factoryName?: string
  /** 所属生产部门 */
  productionDepartmentId?: number
  productionDepartmentName?: string
  /** 所属 Stage（INJECTION/ASSEMBLY/TEST 等） */
  stage?: string
  /**
   * Section 十二 / U20：资源不可用窗口（设备故障、维护等）
   *  - V1 不建设 PAUSE/RESUME 状态闭环（审核报告 P1-13）；只展示给 PMC，由 PMC 决定是否发起 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE
   *  - 影响窗口与甘特条叠加渲染（背景阴影 / 标线）
   */
  unavailableWindows?: ResourceUnavailableWindow[]
}

/** 资源不可用窗口 */
export interface ResourceUnavailableWindow {
  from: IsoDateTime
  to: IsoDateTime
  /** 不可用原因 */
  reason: string
  /** 影响哪些 Task ID（后端推断；4号位不计算，仅展示） */
  impactedTaskIds?: number[]
}

/**
 * 任务条（甘特图 X 轴矩形）
 *
 * 4号位文档第 6 节约束：
 *  - 仅展示 FinalTask，不展示 LogicalProductionDemand
 *  - status 仅允许 5 种枚举值之一
 *  - 不允许拖拽直接改库
 */
export interface GanttTaskDto {
  taskId: number
  taskNo: string
  /** 主 Order（任务主要承接的订单；U04：可能与 taskShare 中其他 Order 共担） */
  orderId: number
  orderNo?: string
  /** 适用 Domain（U01） */
  domainKey: DomainKey
  materialId: number
  materialCode?: string
  materialName?: string
  resourceId?: number
  operationCode: string
  operationSeq: number
  /** U05：净需求数量（去掉不良品的实际可用数量） */
  netQty: number
  /** U05：计划加工数量（含良率补偿，大于等于 netQty） */
  plannedProcessQty: number
  /** 兼容字段：旧字段，等于 plannedProcessQty（不让前端误以为是净需求） */
  quantity: number
  uom: string
  plannedStartTime?: IsoDateTime
  plannedEndTime?: IsoDateTime
  status: TaskStatus
  isDelayed: boolean
  /** 4号位文档第 6 节：不可移动标识（仅 UI 显示） */
  lockMarker?: TaskLockMarker
  /**
   * U04：TaskShare — 一个 Task 承接多 Order 时，每个 Order 承担的数量
   *  - 当 taskShare 仅一条且 orderId == task.orderId 时 = 单 Order
   *  - 当 taskShare 多条或包含非主 orderId 的项 = 共担
   */
  taskShare?: TaskShareLine[]
  /**
   * U14：是否被外 Domain 共享设备阻挡
   *  - true 时 UI 必须在任务条 + 详情 Drawer 显示阻挡原因
   */
  crossDomainBlocked?: boolean
  /** U14：阻挡原因（外 Domain 共享设备 / 资源冲突 等） */
  crossDomainBlockReason?: string
  /**
   * U06：数量-时间拆分（一个 Task 的数量被拆为多个时段加工）
   *  - 存在时 UI 渲染为多段连续条 + Drawer 显示拆分明细
   *  - 例：100 PCS 拆为 [60 PCS @ STG-1, 40 PCS @ STG-2]
   */
  taskSegments?: TaskSegmentLine[]
  /**
   * Section 二.2：四类信息标签（FACT / RESULT / ESTIMATED / RECOMMENDATION）
   *  - FACT          设备故障 / PI Position / PO ETA 等真实事实
   *  - RESULT        FinalTask / PlannedStart/End 等排程结果
   *  - ESTIMATED     Planning-only Purchase Placeholder 等估算
   *  - RECOMMENDATION 建议发起重排 / 建议确认 Candidate
   *  - 不显式给出时默认 RESULT
   */
  factType?: FactType
  /**
   * Section 十六：MES 下发资格（4号位只展示 + 调用服务，不直接写 MES）
   *  - ELIGIBLE      可下发
   *  - INELIGIBLE    不可下发（典型：Candidate / UNLOCATED / 无 PI / 仍依赖 Placeholder / 窗口外 / 已取消）
   *  - UNKNOWN       后端未判定
   */
  mesEligible?: MesEligibility
  /** 不可下发原因（Section 16） */
  mesIneligibleReasons?: string[]
  /**
   * Section 十一：异常与原因解释 — 根因（不只 DUE_DATE_RISK）
   *  - 后端 GanttDataDto 当前不带，由 3 号位落地后对齐；
   *  - 前端先以可选项存在，4 号位文档第 11 节硬约束：必须展示根因，不能只丢 DUE_DATE_RISK
   *  - issueCategory 区分数据问题 / 排程问题（Section 11 第三段）
   */
  delayReasons?: GanttTaskDelayReason[]
  /** Section 11：是否被上游 Stage 阻挡（前序延迟） */
  upstreamStageBlocked?: boolean
  /** Section 11：上游 Stage 名称 */
  upstreamStageCode?: string
  /**
   * v1.5 §9/§10 SetupSource：Setup 规则命中优先级（5 号位 R3 联调知会 2026-09-21 确认）
   *   - EXACT / DEFAULT / SAME_PRODUCT / NONE / INITIAL（大写字符串，与后端 [Task].[SetupSource] NVARCHAR(50) 严格对齐）
   *   - nullable：1 号位 数据未填充前为 undefined；UI 渲染按 SETUP_SOURCE_META 查表
   *   - 0 号位 Q4 裁决：INITIAL 与 NONE 严格分开，UI 不得合并
   */
  setupSource?: SetupSource
}

/** Section 11：根因条目（4号位文档第 11 节硬约束） */
export interface GanttTaskDelayReason {
  /** 根因类型编码 */
  reasonCode:
    | 'MATERIAL_AVAILABILITY'
    | 'CAPACITY_SHORTAGE'
    | 'UPSTREAM_DELAY'
    | 'LOCK_BLOCKING'
    | 'CROSS_DOMAIN_BLOCK'
    | 'EQUIPMENT_FAILURE'
    | 'DUE_DATE_RISK'
    | 'OTHER'
  /** 数据问题 vs 排程问题（Section 11 第三段） */
  issueCategory: 'DATA_ISSUE' | 'SCHEDULING_ISSUE'
  /** 严重度 */
  severity: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL'
  /** 文档第 11 节列出的根因示例：采购料9月3日才可用、MC01无产能、上游Stage完成晚、Firm任务占关键窗口 */
  description: string
  /** 关联对象引用（PO 编号 / 设备编码 / 上游 Task 等） */
  relatedObjectRef?: string
  /** 影响小时数 */
  impactHours?: number
}

/** U04：TaskShare 一行（一个 Task 承接的某个 Order 的份额） */
export interface TaskShareLine {
  orderId: number
  orderNo: string
  shareQty: number
  /** 该 Order 在 Task 上的占比（0-1）；按 shareQty / plannedProcessQty 计算 */
  shareRatio?: number
}

/** U06：数量-时间拆分（一个 Task 的数量被拆为多个时段加工） */
export interface TaskSegmentLine {
  /** 段序号（1-based，便于 UI 展示） */
  segmentSeq: number
  startTime: IsoDateTime
  endTime: IsoDateTime
  /** 该段加工的数量（与 plannedProcessQty 之和 = 总数） */
  qty: number
  /** 该段数量占总加工数量的占比（0-1） */
  qtyRatio?: number
  /** 拆分原因（产能不足 / 设备故障 / 物料分批 等） */
  reason?: string
}

/** 甘特图数据（一个版本全量） */
export interface GanttDataDto {
  planVersionId: number
  versionCode: string
  planHorizonStart: IsoDateTime
  planHorizonEnd: IsoDateTime
  resources: GanttResourceDto[]
  tasks: GanttTaskDto[]
}

/** 排程概要 KPI（顶部面板用） */
export interface ScheduleSummaryDto {
  planVersionId: number
  versionCode: string
  status: PlanVersionStatus
  totalTasks: number
  scheduledTasks: number
  unscheduledTasks: number
  delayedTasks: number
  totalOrders: number
  delayedOrders: number
  firstTaskStart?: IsoDateTime
  lastTaskEnd?: IsoDateTime
  computeDurationSeconds?: number
}
