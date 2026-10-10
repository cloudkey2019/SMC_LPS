/**
 * APS V1 4号位 — Mock 假数据
 *
 * 用途：
 *  - 后端接口未就绪时，前端按 4号位文档定义 DTO 后用本 fixture 推进
 *  - 后端就绪后切换 VITE_USE_MOCK=false 即走真实 API
 *
 * 字段名严格使用 camelCase（前端 TS 风格），与 4号位文档第 19 节一致
 */

import type {
  GanttDataDto,
  GanttResourceDto,
  GanttTaskDto,
  OrderBasicInfo,
  OrderScheduleDetailDto,
  PlanOverviewDto,
  PlanVersionSummaryDto,
  ScheduleSummaryDto
} from '../types'
import type { DomainKey, OrderSummaryStatus, TaskStatus } from '../types'
import { ORDER_SUMMARY_STATUSES, TASK_STATUSES } from '../types'

/* ========== 时间工具 ========== */
const now = new Date()
const iso = (d: Date) => d.toISOString()
const dayOffset = (days: number, hour = 8) => {
  const d = new Date(now)
  d.setDate(d.getDate() + days)
  d.setHours(hour, 0, 0, 0)
  return iso(d)
}
const hourOffset = (hours: number) => {
  const d = new Date(now)
  d.setHours(d.getHours() + hours)
  return iso(d)
}

/* ========== Schedule（与后端对齐，可直接对接） ========== */

const RESOURCES: GanttResourceDto[] = [
  {
    resourceId: 1,
    resourceCode: 'MC01',
    resourceName: '注塑机-01',
    domainKey: 'FAMILY_INJECTION',
    factoryId: 1,
    factoryName: '苏州厂',
    productionDepartmentId: 1,
    productionDepartmentName: '注塑车间',
    stage: 'STG-INJ',
    // Section 十二 / U20：MC01 维护窗口 + 故障提示
    unavailableWindows: [
      { from: dayOffset(2, 8), to: dayOffset(2, 14), reason: '计划维护（季度保养）' },
      {
        from: dayOffset(5, 10),
        to: dayOffset(5, 18),
        reason: '设备故障 - 液压泵异常（Section 十二）'
      }
    ]
  },
  {
    resourceId: 2,
    resourceCode: 'MC02',
    resourceName: '注塑机-02',
    domainKey: 'FAMILY_INJECTION',
    factoryId: 1,
    factoryName: '苏州厂',
    productionDepartmentId: 1,
    productionDepartmentName: '注塑车间',
    stage: 'STG-INJ'
  },
  {
    resourceId: 3,
    resourceCode: 'ASM01',
    resourceName: '装配线-01',
    domainKey: 'FAMILY_ASSEMBLY',
    factoryId: 1,
    factoryName: '苏州厂',
    productionDepartmentId: 2,
    productionDepartmentName: '装配车间',
    stage: 'STG-ASM'
  },
  {
    resourceId: 4,
    resourceCode: 'ASM02',
    resourceName: '装配线-02',
    domainKey: 'FAMILY_ASSEMBLY',
    factoryId: 1,
    factoryName: '苏州厂',
    productionDepartmentId: 2,
    productionDepartmentName: '装配车间',
    stage: 'STG-ASM'
  },
  {
    resourceId: 5,
    resourceCode: 'TST01',
    resourceName: '测试台-01',
    domainKey: 'FAMILY_TEST',
    factoryId: 1,
    factoryName: '苏州厂',
    productionDepartmentId: 3,
    productionDepartmentName: '测试车间',
    stage: 'STG-TEST'
  },
  {
    resourceId: 6,
    resourceCode: 'MC03',
    resourceName: '注塑机-03',
    domainKey: 'FAMILY_INJECTION',
    factoryId: 2,
    factoryName: '成都厂',
    productionDepartmentId: 1,
    productionDepartmentName: '注塑车间',
    stage: 'STG-INJ'
  },
  {
    resourceId: 7,
    resourceCode: 'ASM03',
    resourceName: '装配线-03',
    domainKey: 'FAMILY_ASSEMBLY',
    factoryId: 2,
    factoryName: '成都厂',
    productionDepartmentId: 2,
    productionDepartmentName: '装配车间',
    stage: 'STG-ASM'
  },
  {
    resourceId: 8,
    resourceCode: 'TST02',
    resourceName: '测试台-02',
    domainKey: 'FAMILY_TEST',
    factoryId: 1,
    factoryName: '苏州厂',
    productionDepartmentId: 3,
    productionDepartmentName: '测试车间',
    stage: 'STG-TEST'
  }
]

/** 在合法状态中循环，确保只有 5 种枚举值出现（4号位文档第 6 节） */
const pickStatus = (seed: number): TaskStatus => TASK_STATUSES[seed % TASK_STATUSES.length]

const TASKS: GanttTaskDto[] = (() => {
  const list: GanttTaskDto[] = []
  const orders = [
    { orderNo: 'ORD-2026-0001', material: '电机壳体-A', orderId: 101 },
    { orderNo: 'ORD-2026-0002', material: '机柜上盖-B', orderId: 102 },
    { orderNo: 'ORD-2026-0003', material: '控制箱-C', orderId: 103 },
    { orderNo: 'ORD-2026-0004', material: '标准泵-D', orderId: 104 },
    { orderNo: 'ORD-2026-0005', material: '包装套件-E', orderId: 105 },
    { orderNo: 'ORD-2026-0006', material: '机架焊接件-F', orderId: 106 }
  ]
  const operations = [
    { code: 'OP10', seq: 10, name: '切割' },
    { code: 'OP20', seq: 20, name: '焊接' },
    { code: 'OP30', seq: 30, name: '喷涂' },
    { code: 'OP40', seq: 40, name: '装配' },
    { code: 'OP50', seq: 50, name: '测试' }
  ]
  let taskId = 1000
  for (let i = 0; i < 60; i++) {
    const order = orders[i % orders.length]
    const op = operations[i % operations.length]
    const resource = RESOURCES[i % RESOURCES.length]
    const startDays = (i % 14) - 3 // -3 ~ 10
    const status = pickStatus(i)
    const plannedStart = dayOffset(startDays, 8 + (i % 8))
    const plannedEnd = dayOffset(startDays, 8 + (i % 8) + 4)
    const isDelayed = i % 7 === 0 // 与下方根因类型选择器 i % 5 解耦，确保 5 类根因都能展开

    // U05：净需求 + 计划加工（含良率补偿）
    const netQty = 50 + i * 3
    const plannedProcessQty = Math.ceil(netQty / 0.95)

    // U04：每 7 条任务中挑 1 条做 TaskShare（同一 Task 承接多 Order）
    let taskShare: GanttTaskDto['taskShare']
    if (i % 7 === 3) {
      const secondOrder = orders[(i + 2) % orders.length]
      const sharedQty = Math.floor(plannedProcessQty * 0.4)
      const mainQty = plannedProcessQty - sharedQty
      taskShare = [
        {
          orderId: order.orderId,
          orderNo: order.orderNo,
          shareQty: mainQty,
          shareRatio: mainQty / plannedProcessQty
        },
        {
          orderId: secondOrder.orderId,
          orderNo: secondOrder.orderNo,
          shareQty: sharedQty,
          shareRatio: sharedQty / plannedProcessQty
        }
      ]
    }

    // U14：每 13 条任务中挑 1 条做跨 Domain 阻挡（资源被外 Domain 占用）
    const crossDomainBlocked = i % 13 === 5
    const crossDomainBlockReason = crossDomainBlocked
      ? `外 Domain 共享设备 ${resource.resourceCode} 被 ${resource.domainKey === 'FAMILY_TEST' ? 'FAMILY_INJECTION' : 'FAMILY_TEST'} 域占用至 ${dayOffset(startDays + 2, 18).slice(0, 10)}`
      : undefined

    // U06：每 17 条任务中挑 1 条做数量-时间拆分（产能不足导致）
    let taskSegments: GanttTaskDto['taskSegments']
    if (i % 17 === 4) {
      const seg1Qty = Math.ceil(plannedProcessQty * 0.6)
      const seg2Qty = plannedProcessQty - seg1Qty
      const seg1Start = plannedStart
      const seg1End = dayOffset(startDays, 8 + (i % 8) + 2)
      const seg2Start = dayOffset(startDays + 2, 10)
      const seg2End = plannedEnd
      taskSegments = [
        {
          segmentSeq: 1,
          startTime: seg1Start,
          endTime: seg1End,
          qty: seg1Qty,
          qtyRatio: seg1Qty / plannedProcessQty,
          reason: 'MC01 主产能段'
        },
        {
          segmentSeq: 2,
          startTime: seg2Start,
          endTime: seg2End,
          qty: seg2Qty,
          qtyRatio: seg2Qty / plannedProcessQty,
          reason: '次日加班段补齐'
        }
      ]
    }

    // Section 二.2：FactType（默认 RESULT；部分任务带其它 4 类）
    let factType: GanttTaskDto['factType']
    if (crossDomainBlocked) {
      factType = 'FACT' // 资源被外 Domain 占用是当下事实
    } else if (i % 19 === 7) {
      factType = 'ESTIMATED' // 占位未承诺供应
    } else if (i % 23 === 11 && isDelayed) {
      factType = 'RECOMMENDATION' // 建议发起重排
    } else {
      factType = 'RESULT'
    }

    // Section 十一：根因（4号位文档第 11 节硬约束：必须优先展示根因，不能只丢 DUE_DATE_RISK）
    // 文档第 11 节原文示例：采购料9月3日才可用、MC01无产能、上游Stage完成晚、Firm任务占关键窗口
    const delayReasons: NonNullable<GanttTaskDto['delayReasons']> = []
    let upstreamStageBlocked: boolean | undefined
    let upstreamStageCode: string | undefined
    if (crossDomainBlocked) {
      delayReasons.push({
        reasonCode: 'CROSS_DOMAIN_BLOCK',
        issueCategory: 'SCHEDULING_ISSUE',
        severity: 'ERROR',
        description: '外 Domain 共享设备占用，本 Domain 任务无法按时开工',
        relatedObjectRef: resource.resourceCode,
        impactHours: 8
      })
    }
    if (isDelayed) {
      if (i % 5 === 0) {
        delayReasons.push({
          reasonCode: 'CAPACITY_SHORTAGE',
          issueCategory: 'SCHEDULING_ISSUE',
          severity: 'WARNING',
          description: `设备 ${resource.resourceCode} 产能不足，无法在本时段内完工`,
          relatedObjectRef: resource.resourceCode,
          impactHours: 4
        })
      } else if (i % 5 === 1) {
        delayReasons.push({
          reasonCode: 'MATERIAL_AVAILABILITY',
          issueCategory: 'DATA_ISSUE',
          severity: 'WARNING',
          description: `PO-${String(1000 + i).padStart(4, '0')} 物料预计 ${dayOffset(
            startDays + 3,
            14
          )
            .slice(0, 10)
            .replace(/-/g, '/')} 才到货`,
          relatedObjectRef: `PO-${String(1000 + i).padStart(4, '0')}`,
          impactHours: 24
        })
      } else if (i % 5 === 2) {
        upstreamStageBlocked = true
        upstreamStageCode = 'STG-AS'
        delayReasons.push({
          reasonCode: 'UPSTREAM_DELAY',
          issueCategory: 'SCHEDULING_ISSUE',
          severity: 'WARNING',
          description: `上游 ${upstreamStageCode} 完成晚，预计延误 6h 后才能开始本工序`,
          relatedObjectRef: upstreamStageCode,
          impactHours: 6
        })
      } else if (i % 5 === 3) {
        delayReasons.push({
          reasonCode: 'LOCK_BLOCKING',
          issueCategory: 'SCHEDULING_ISSUE',
          severity: 'INFO',
          description: '关键窗口已被 Firm/FROZEN 任务占用，本任务只能后置',
          relatedObjectRef: 'FIRM-WINDOW',
          impactHours: 3
        })
      } else {
        delayReasons.push({
          reasonCode: 'EQUIPMENT_FAILURE',
          issueCategory: 'DATA_ISSUE',
          severity: 'ERROR',
          description: `${resource.resourceCode} 在本时段发生设备故障，预计 ${dayOffset(
            startDays,
            18
          ).slice(11, 16)} 恢复`,
          relatedObjectRef: resource.resourceCode,
          impactHours: 12
        })
      }
    }

    // Section 十六：MES 下发资格
    // 典型不可下发：Candidate / UNLOCATED / 无 PI / 仍依赖 Placeholder / 窗口外 / 已取消
    let mesEligible: GanttTaskDto['mesEligible']
    const mesIneligibleReasons: string[] = []
    if (status === 'CANCELLED') {
      mesEligible = 'INELIGIBLE'
      mesIneligibleReasons.push('Task 已取消')
    } else if (status === 'COMPLETED') {
      mesEligible = 'INELIGIBLE'
      mesIneligibleReasons.push('Task 已完成')
    } else if (status === 'PLANNED') {
      mesEligible = 'UNKNOWN'
      mesIneligibleReasons.push('未达下发窗口，等待释放判定')
    } else if (crossDomainBlocked) {
      mesEligible = 'INELIGIBLE'
      mesIneligibleReasons.push('外 Domain 共享设备阻挡，未开始加工（U14）')
    } else if (factType === 'ESTIMATED') {
      mesEligible = 'INELIGIBLE'
      mesIneligibleReasons.push('依赖 PLANNING_PURCHASE_PLACEHOLDER，无正式承诺（U18）')
    } else if (i % 9 === 4) {
      mesEligible = 'INELIGIBLE'
      mesIneligibleReasons.push('PI 存在 UNLOCATED 数量，未下推具体位置（U16）')
    } else if (i % 11 === 7) {
      mesEligible = 'INELIGIBLE'
      mesIneligibleReasons.push('无对应 PI 规划 Task（U17）')
    } else {
      mesEligible = 'ELIGIBLE'
    }

    list.push({
      taskId: taskId++,
      taskNo: `T${taskId}`,
      orderId: order.orderId,
      orderNo: order.orderNo,
      domainKey: resource.domainKey,
      materialId: 200 + (i % orders.length),
      materialCode: `M-${(i % 9) + 1}`,
      materialName: order.material,
      resourceId: resource.resourceId,
      operationCode: op.code,
      operationSeq: op.seq,
      netQty,
      plannedProcessQty,
      quantity: plannedProcessQty,
      uom: 'PCS',
      plannedStartTime: plannedStart,
      plannedEndTime: plannedEnd,
      status,
      isDelayed,
      lockMarker: i % 7 === 0 ? 'EXECUTION' : i % 11 === 0 ? 'FIRM' : undefined,
      taskShare,
      crossDomainBlocked: crossDomainBlocked || undefined,
      crossDomainBlockReason,
      taskSegments,
      factType,
      mesEligible,
      mesIneligibleReasons: mesIneligibleReasons.length > 0 ? mesIneligibleReasons : undefined,
      delayReasons: delayReasons.length > 0 ? delayReasons : undefined,
      upstreamStageBlocked,
      upstreamStageCode
    })
  }
  return list
})()

export const mockVersions: PlanVersionSummaryDto[] = [
  {
    id: 1001,
    versionCode: 'V-2026-001',
    versionCategory: 'FULL',
    planHorizonStart: dayOffset(-7),
    planHorizonEnd: dayOffset(83),
    status: 'ACTIVE',
    computedAt: hourOffset(-2),
    totalTasks: TASKS.length,
    createdAt: hourOffset(-2)
  },
  {
    id: 1000,
    versionCode: 'V-2026-000',
    versionCategory: 'FULL',
    planHorizonStart: dayOffset(-7),
    planHorizonEnd: dayOffset(83),
    status: 'ARCHIVED',
    computedAt: hourOffset(-26),
    totalTasks: 58,
    createdAt: hourOffset(-26)
  },
  {
    id: 999,
    versionCode: 'C-2026-003',
    versionCategory: 'CANDIDATE',
    planHorizonStart: dayOffset(-7),
    planHorizonEnd: dayOffset(83),
    status: 'CANDIDATE',
    computedAt: hourOffset(-1),
    totalTasks: 62,
    createdAt: hourOffset(-1)
  }
]

export const mockSummary = (planVersionId: number): ScheduleSummaryDto => {
  const scheduled = TASKS.filter((t) => t.status !== 'CANCELLED').length
  const delayed = TASKS.filter((t) => t.isDelayed).length
  return {
    planVersionId,
    versionCode: mockVersions.find((v) => v.id === planVersionId)?.versionCode ?? 'UNKNOWN',
    status: 'ACTIVE',
    totalTasks: TASKS.length,
    scheduledTasks: scheduled,
    unscheduledTasks: 0,
    delayedTasks: delayed,
    totalOrders: 6,
    delayedOrders: Math.round(delayed / 5),
    firstTaskStart: TASKS[0]?.plannedStartTime,
    lastTaskEnd: TASKS[TASKS.length - 1]?.plannedEndTime,
    computeDurationSeconds: 18
  }
}

export const mockGantt = (planVersionId: number): GanttDataDto => ({
  planVersionId,
  versionCode: mockVersions.find((v) => v.id === planVersionId)?.versionCode ?? 'UNKNOWN',
  planHorizonStart: dayOffset(-7),
  planHorizonEnd: dayOffset(83),
  resources: RESOURCES,
  tasks: TASKS
})

/* ========== Overview（占位） ========== */

const DOMAIN_KEYS: DomainKey[] = ['FAMILY_INJECTION', 'FAMILY_ASSEMBLY', 'FAMILY_TEST']

export const mockOverview: PlanOverviewDto = {
  activeVersions: DOMAIN_KEYS.map((dk, i) => ({
    domainKey: dk,
    planVersionId: 1001 + i,
    versionCode: `V-2026-001-${dk}`,
    status: 'ACTIVE',
    activatedAt: hourOffset(-2),
    sourceScheduleRunId: 5001 + i,
    planHorizonStart: dayOffset(-7),
    planHorizonEnd: dayOffset(83)
  })),
  orderSummary: [
    { status: 'ON_TIME', count: 42 },
    { status: 'AT_RISK', count: 8 },
    { status: 'DELAYED', count: 6 },
    { status: 'ESTIMATED_ONLY', count: 3 },
    { status: 'UNSCHEDULED', count: 1 }
  ],
  resourceSummary: RESOURCES.map((r, i) => ({
    resourceId: r.resourceId,
    resourceCode: r.resourceCode,
    resourceName: r.resourceName,
    utilization: Math.round((60 + i * 4) * 100) / 100,
    isBottleneck: i % 3 === 0,
    unavailableWindows: [{ from: dayOffset(3, 8), to: dayOffset(3, 12) }]
  })),
  domainIssues: [
    {
      domainKey: 'FAMILY_TEST',
      issueType: 'PI_POSITION_ISSUE',
      message: 'PI-2026-3320 缺 XC 数量，Stage-3 不可定位',
      occurredAt: hourOffset(-4)
    }
  ],
  estimatedOnlyOrderCount: 3,
  pendingCandidateCount: 1
}

/* ========== Orders（页面 2 订单/需求计划） ========== */

/** 订单静态维度表（mock 用，10 个物料 × 5 个客户 × 2 个工厂） */
const ORDER_MATERIALS = [
  { materialId: 200, code: 'M-001', name: '电机壳体-A' },
  { materialId: 201, code: 'M-002', name: '机柜上盖-B' },
  { materialId: 202, code: 'M-003', name: '控制箱-C' },
  { materialId: 203, code: 'M-004', name: '标准泵-D' },
  { materialId: 204, code: 'M-005', name: '包装套件-E' },
  { materialId: 205, code: 'M-006', name: '机架焊接件-F' },
  { materialId: 206, code: 'M-007', name: '电路板组件-G' },
  { materialId: 207, code: 'M-008', name: '散热模组-H' },
  { materialId: 208, code: 'M-009', name: '金属面板-I' },
  { materialId: 209, code: 'M-010', name: '精密轴承-J' }
]

const ORDER_CUSTOMERS = [
  { code: 'CUST-001', name: '示例客户-华东' },
  { code: 'CUST-002', name: '示例客户-华南' },
  { code: 'CUST-003', name: '示例客户-华北' },
  { code: 'CUST-004', name: '示例客户-海外' },
  { code: 'CUST-005', name: '示例客户-西南' }
]

const ORDER_FACTORIES = [
  { id: 1, code: 'F-SUZ-01' },
  { id: 2, code: 'F-CDG-02' }
]

const ORDER_FAMILIES = ['INJECTION', 'ASSEMBLY', 'TEST']
const ORDER_PRIORITIES = ['P1', 'P2', 'P3']
const ORDER_STATUS_DISTRIBUTION: OrderSummaryStatus[] = [
  ...new Array(38).fill('ON_TIME'),
  ...new Array(10).fill('AT_RISK'),
  ...new Array(6).fill('DELAYED'),
  ...new Array(4).fill('ESTIMATED_ONLY'),
  ...new Array(2).fill('UNSCHEDULED')
]

/** 单个订单的详情生成器（页面 2 的 OrderScheduleDetailDto） */
export const mockOrderDetail = (orderId: number): OrderScheduleDetailDto => {
  const mat = ORDER_MATERIALS[orderId % ORDER_MATERIALS.length]
  const cust = ORDER_CUSTOMERS[orderId % ORDER_CUSTOMERS.length]
  const factory = ORDER_FACTORIES[orderId % ORDER_FACTORIES.length]
  const family = ORDER_FAMILIES[orderId % ORDER_FAMILIES.length]
  const priority = ORDER_PRIORITIES[orderId % ORDER_PRIORITIES.length]
  const status = ORDER_STATUS_DISTRIBUTION[orderId % ORDER_STATUS_DISTRIBUTION.length]
  const dueOffset = (orderId % 30) - 5 // -5 ~ 24 天
  const orderQty = 80 + ((orderId * 17) % 320)
  const basic: OrderBasicInfo = {
    orderId,
    orderNo: `ORD-2026-${String(orderId).padStart(4, '0')}`,
    orderCanonicalId: orderId + 100000, // mock：B4 落地映射（orderId 1 → canonicalId 100001，ID 空间独立）
    productionInstructionNo: `PI-${String(orderId).padStart(5, '0')}`,
    mtS_InstructionNo: `MTS-${String(orderId).padStart(5, '0')}`, // mock：与 dev 实跑字段命名对齐
    materialId: mat.materialId,
    materialCode: mat.code,
    materialName: mat.name,
    customerCode: cust.code,
    customerName: cust.name,
    factoryId: factory.id,
    factoryCode: factory.code,
    productFamilyCode: family,
    orderQty,
    uom: 'PCS',
    customerDueDate: dayOffset(dueOffset, 17),
    prioritySegmentCode: priority,
    status,
    planVersionId: 1001,
    planVersionStatus: 'ACTIVE',
    domainKey: family
  }
  const pegging: OrderScheduleDetailDto['pegging'] = [
    {
      supplyType: 'INVENTORY',
      supplyKey: `INV-${String(orderId).padStart(4, '0')}`,
      allocatedQty: Math.round(orderQty * 0.4),
      availableTime: dayOffset(-1, 9),
      commitment: 'COMMITMENT',
      confidence: 'HIGH',
      isHardLocked: true,
      factType: 'FACT'
    },
    {
      supplyType: 'PLANNED_PRODUCTION',
      supplyKey: `T${1000 + (orderId % 50)}`,
      allocatedQty: Math.round(orderQty * 0.4),
      availableTime: dayOffset(Math.max(2, dueOffset - 2), 8),
      commitment: 'COMMITMENT',
      confidence: 'MEDIUM',
      isHardLocked: false,
      factType: 'RESULT'
    },
    {
      supplyType: 'PLANNING_PURCHASE_PLACEHOLDER',
      supplyKey: `PO-${String(orderId).padStart(4, '0')}`,
      allocatedQty: orderQty - Math.round(orderQty * 0.4) - Math.round(orderQty * 0.4),
      availableTime: dayOffset(dueOffset + 3, 12),
      commitment: 'NONE',
      confidence: 'LOW',
      isHardLocked: false,
      factType: 'ESTIMATED'
    }
  ]
  const tasks: OrderScheduleDetailDto['tasks'] = [
    {
      taskId: 9000 + orderId,
      taskNo: `T${9000 + orderId}`,
      operationCode: 'OP10',
      operationSeq: 10,
      stageCode: 'STG-INJ',
      resourceId: (orderId % 7) + 1,
      resourceCode: `MC0${(orderId % 3) + 1}`,
      startTime: dayOffset(0, 8),
      endTime: dayOffset(0, 17),
      netQty: orderQty,
      plannedProcessQty: Math.round(orderQty * 1.05),
      uom: 'PCS',
      status: status === 'DELAYED' ? 'IN_PROGRESS' : 'PLANNED',
      lockMarker: orderId % 7 === 0 ? 'EXECUTION' : undefined,
      taskShares: [{ orderId, orderNo: basic.orderNo, shareQty: orderQty }]
    }
  ]
  const reasons: OrderScheduleDetailDto['reasons'] = []
  if (status === 'DELAYED') {
    reasons.push({
      reasonCode: 'CAPACITY_INSUFFICIENT',
      description: '注塑机产能不足',
      rootCause: '下游 AS 占用了部分时段',
      relatedObjectRef: 'MC01'
    })
  } else if (status === 'AT_RISK') {
    reasons.push({
      reasonCode: 'PURCHASE_ESTIMATED_RISK',
      description: '部分物料依赖采购估算',
      rootCause: '采购尚未正式承诺',
      relatedObjectRef: `PO-${String(orderId).padStart(4, '0')}`
    })
  } else if (status === 'ESTIMATED_ONLY') {
    reasons.push({
      reasonCode: 'PURCHASE_ESTIMATED_RISK',
      description: '仅依赖 PLANNING_PURCHASE_PLACEHOLDER',
      rootCause: '本地无库存且未排产',
      relatedObjectRef: `PO-${String(orderId).padStart(4, '0')}`
    })
  }
  return { basic, pegging, tasks, reasons }
}

/** 订单分页 mock（按 status 过滤、按 orderNo 模糊匹配、按 PageQuery 切页） */
export const mockOrderList = (
  pageIndex: number,
  pageSize: number,
  filter?: {
    orderNo?: string
    productionInstructionNo?: string
    materialCode?: string
    customerCode?: string
    factoryCode?: string
    productFamilyCode?: string
    dueDateFrom?: string
    dueDateTo?: string
    status?: OrderSummaryStatus
    planVersionId?: number
    domainKey?: string
  }
): { items: OrderBasicInfo[]; total: number; pageIndex: number; pageSize: number } => {
  const all = Array.from({ length: 60 }, (_, i) => mockOrderDetail(i + 1).basic)
  let filtered = all
  if (filter?.orderNo) {
    const q = filter.orderNo.toLowerCase()
    filtered = filtered.filter((o) => o.orderNo.toLowerCase().includes(q))
  }
  if (filter?.productionInstructionNo) {
    const q = filter.productionInstructionNo.toLowerCase()
    filtered = filtered.filter((o) => o.productionInstructionNo?.toLowerCase().includes(q) ?? false)
  }
  if (filter?.materialCode) {
    const q = filter.materialCode.toLowerCase()
    filtered = filtered.filter((o) => o.materialCode.toLowerCase().includes(q))
  }
  if (filter?.customerCode) {
    filtered = filtered.filter((o) => o.customerCode === filter.customerCode)
  }
  if (filter?.productFamilyCode) {
    filtered = filtered.filter((o) => o.productFamilyCode === filter.productFamilyCode)
  }
  if (filter?.factoryCode) {
    filtered = filtered.filter((o) => o.factoryCode === filter.factoryCode)
  }
  if (filter?.status) {
    filtered = filtered.filter((o) => o.status === filter.status)
  }
  if (filter?.planVersionId !== undefined) {
    filtered = filtered.filter((o) => o.planVersionId === filter.planVersionId)
  }
  if (filter?.domainKey) {
    filtered = filtered.filter((o) => o.domainKey === filter.domainKey)
  }
  if (filter?.dueDateFrom) {
    const fromMs = new Date(filter.dueDateFrom).getTime()
    filtered = filtered.filter((o) => new Date(o.customerDueDate).getTime() >= fromMs)
  }
  if (filter?.dueDateTo) {
    const toMs = new Date(filter.dueDateTo).getTime() + 86400000
    filtered = filtered.filter((o) => new Date(o.customerDueDate).getTime() < toMs)
  }
  const start = (pageIndex - 1) * pageSize
  return {
    items: filtered.slice(start, start + pageSize),
    total: filtered.length,
    pageIndex,
    pageSize
  }
}

/** 订单聚合统计（顶部 KPI 用） */
export const mockOrderSummary = (): { status: OrderSummaryStatus; count: number }[] => {
  const all = Array.from({ length: 60 }, (_, i) => mockOrderDetail(i + 1).basic)
  const counts = new Map<OrderSummaryStatus, number>()
  ORDER_SUMMARY_STATUSES.forEach((s) => counts.set(s, 0))
  all.forEach((o) => counts.set(o.status, (counts.get(o.status) ?? 0) + 1))
  return ORDER_SUMMARY_STATUSES.map((s) => ({ status: s, count: counts.get(s) ?? 0 }))
}
