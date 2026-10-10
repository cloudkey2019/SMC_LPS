<script setup lang="ts">
/**
 * APS V1 4号位 — 甘特图/资源计划（页面 5 / U01 / U06 / U14）
 *
 * 4号位文档第 6 节硬约束：
 *  - 仅展示 FinalTask，不展示 LogicalProductionDemand
 *  - Task 状态仅允许 5 种枚举值：PLANNED / RELEASED / IN_PROGRESS / COMPLETED / CANCELLED
 *  - lockMarker（EXECUTION / FIRM / FROZEN）仅 UI 标识，禁止拖拽直接改库
 *  - 本页面只读，所有"改动"通过阶段 B / C / D 的接口进行
 *
 * 本文件作为端到端冒烟测试页：
 *  - 验证 DhxGantt 包装组件 + schedule store + mock fixtures 链路
 *  - 验证 5 状态枚举 + lockMarker 在 dhtmlx-gantt 上正确染色
 *  - 验证只读模式不触发 drag 事件
 */

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { DhxGantt } from '@/components/DhxGantt'
import type {
  DhxGanttTask,
  DhxGanttDragPayload,
  DhxGanttDragRejectedPayload
} from '@/components/DhxGantt/src/types'
import { useScheduleStore } from '@/store/modules/aps/schedule'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { useDomainStore } from '@/store/modules/aps/domain'
import { triggerBusinessEntry } from '@/api/aps-v1'
import type { BusinessEntrySubmission } from '@/api/aps-v1'
import DomainUnavailableBadge from './components/DomainUnavailableBadge.vue'
import GanttAdjustDialog from './components/GanttAdjustDialog.vue'
import ResourceRescheduleDialog from './components/ResourceRescheduleDialog.vue'
import DomainRescheduleDialog from './components/DomainRescheduleDialog.vue'
import {
  PLAN_VERSION_STATUS_LABELS,
  TASK_STATUS_LABELS,
  roleLabelOf,
  type DomainKey,
  type GanttResourceDto,
  type GanttTaskDelayReason,
  type GanttTaskDto,
  type PlanVersionStatus,
  type PlanVersionSummaryDto,
  type RoleKey,
  type TaskLockMarker,
  type TaskStatus
} from '@/api/aps-v1/types'
import { SETUP_SOURCE_META } from '@/api/aps-v1/types/setup'

import {
  ElAlert,
  ElButton,
  ElDescriptions,
  ElDescriptionsItem,
  ElDivider,
  ElDropdown,
  ElDropdownMenu,
  ElDropdownItem,
  ElIcon,
  ElOption,
  ElRadioButton,
  ElRadioGroup,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'

const scheduleStore = useScheduleStore()
const { versions, currentVersionId, ganttData, loading, error } = storeToRefs(scheduleStore)

/**
 * ElSelect v-model 不接受 null，用一个本地 ref 桥接 store.currentVersionId
 */
const selectedVersionId = ref<number | undefined>(undefined)
watch(
  currentVersionId,
  (v) => {
    selectedVersionId.value = v ?? undefined
  },
  { immediate: true }
)
watch(selectedVersionId, (v) => {
  if (v !== undefined && v !== currentVersionId.value) {
    scheduleStore.selectVersion(v)
  }
})

/* ===== 时间范围（驱动 Gantt 时间轴） ===== */
type TimeRangeKey = 'D1' | 'D7' | 'D30' | 'D90'
const timeRange = ref<TimeRangeKey>('D7')

const dateRange = computed<{
  from: string
  to: string
  scaleUnit: 'hour' | 'day' | 'week' | 'month'
}>(() => {
  const start = dayjs().startOf('day')
  switch (timeRange.value) {
    case 'D1':
      return { from: start.toISOString(), to: start.add(1, 'day').toISOString(), scaleUnit: 'hour' }
    case 'D7':
      return { from: start.toISOString(), to: start.add(7, 'day').toISOString(), scaleUnit: 'day' }
    case 'D30':
      return {
        from: start.toISOString(),
        to: start.add(30, 'day').toISOString(),
        scaleUnit: 'week'
      }
    case 'D90':
      return {
        from: start.toISOString(),
        to: start.add(90, 'day').toISOString(),
        scaleUnit: 'month'
      }
    default:
      return { from: start.toISOString(), to: start.add(7, 'day').toISOString(), scaleUnit: 'day' }
  }
})

/* ===== 多维度切换（U01：Domain / 资源 / Stage / 工厂 / 部门） ===== */
type GroupByKey = 'resource' | 'domain' | 'stage' | 'factory' | 'department'
const groupBy = ref<GroupByKey>('resource')
const GROUP_BY_OPTIONS: Array<{ value: GroupByKey; label: string }> = [
  { value: 'resource', label: '按资源' },
  { value: 'domain', label: '按 Domain' },
  { value: 'stage', label: '按阶段' },
  { value: 'factory', label: '按工厂' },
  { value: 'department', label: '按生产部门' }
]

/** Domain 染色（U01：ACTIVE 多 Domain 各 Domain 正确展示）
 *  - v1.2 Domain专项：键改 FAMILY_ 前缀；未识别 Domain 回退灰色
 */
const DOMAIN_COLORS: Record<string, string> = {
  FAMILY_INJECTION: '#3b82f6',
  FAMILY_ASSEMBLY: '#8b5cf6',
  FAMILY_TEST: '#10b981',
  ALL: '#94a3b8'
}

/** Domain 字典（v1.2 Domain专项：从 useDomainStore 派生） */
const domainStore = useDomainStore()

/** Domain 过滤器（U01：多 Domain 多选；默认全选）
 *  - v1.2：选项以 useDomainStore.activeDomains 为全集（按 store sortOrder）
 *  - 仅展示当前数据中实际存在的 Domain（intersect）
 */
const domainFilter = ref<string[]>([])

/** 当前数据中存在 + store 启用的 Domain 列表（domainKey 排序） */
const availableDomains = computed<DomainKey[]>(() => {
  const storeSet = new Set(domainStore.activeDomains.map((d) => d.domainKey))
  const set = new Set<string>()
  ganttData.value?.tasks.forEach((t) => set.add(t.domainKey))
  return Array.from(set)
    .filter((k) => storeSet.has(k))
    .sort() as DomainKey[]
})

/** 默认全选 */
watch(
  availableDomains,
  (domains) => {
    if (domainFilter.value.length === 0 && domains.length > 0) {
      domainFilter.value = [...domains]
    }
  },
  { immediate: true }
)

/** 排程域显示名：中文名优先，缺失回退 domainKey（与排程域维护页一致；筛选值仍为 domainKey） */
function domainLabel(dk: string): string {
  return domainStore.label(dk)
}

/** Domain 任务数（按过滤后统计；U01 一目了然） */
const domainCounts = computed<Record<string, number>>(() => {
  const counts: Record<string, number> = {}
  for (const t of filteredTasks.value) {
    counts[t.domainKey] = (counts[t.domainKey] ?? 0) + 1
  }
  return counts
})

/** 过滤后的任务（U01：Domain 维度筛选 + P5：受影响筛选；AND 关系） */
const filteredTasks = computed(() => {
  let tasks = ganttData.value?.tasks ?? []
  if (domainFilter.value.length > 0) {
    tasks = tasks.filter((t) => domainFilter.value.includes(t.domainKey))
  }
  tasks = applyImpactFilter(tasks)
  return tasks
})

/* ===== 泳道（Y 轴；按 groupBy 维度动态生成） ===== */
interface Lane {
  id: string
  label: string
  color: string
  textColor: string
}

const lanes = computed<Lane[]>(() => {
  const data = ganttData.value
  if (!data) return []

  // 按资源：原始行为，每个资源一行
  if (groupBy.value === 'resource') {
    return data.resources.map((r) => ({
      id: `lane-${r.resourceId}`,
      label: `${r.resourceCode} | ${r.resourceName}`,
      color: '#e2e8f0',
      textColor: '#1e293b'
    }))
  }

  // 以下 4 类按 Task 维度动态聚合；用 filteredTasks 做数据源
  const tasks = filteredTasks.value
  const resourceMap = new Map(data.resources.map((r) => [r.resourceId, r]))

  if (groupBy.value === 'domain') {
    const seen = new Map<string, string>()
    for (const t of tasks) {
      if (!seen.has(t.domainKey)) {
        seen.set(t.domainKey, DOMAIN_COLORS[t.domainKey] ?? '#94a3b8')
      }
    }
    return Array.from(seen.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([dk, color]) => ({
        id: `lane-domain-${dk}`,
        label: `${domainLabel(dk)} (${tasks.filter((t) => t.domainKey === dk).length})`,
        color,
        textColor: '#ffffff'
      }))
  }

  if (groupBy.value === 'stage') {
    const seen = new Map<string, { color: string }>()
    for (const t of tasks) {
      const r = resourceMap.get(t.resourceId ?? -1)
      const stage = r?.stage ?? 'UNKNOWN'
      if (!seen.has(stage)) {
        seen.set(stage, { color: DOMAIN_COLORS[r?.domainKey ?? ''] ?? '#94a3b8' })
      }
    }
    return Array.from(seen.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([stage, info]) => ({
        id: `lane-stage-${stage}`,
        label: stage,
        color: info.color,
        textColor: '#ffffff'
      }))
  }

  if (groupBy.value === 'factory') {
    const seen = new Map<string, string>()
    for (const t of tasks) {
      const r = resourceMap.get(t.resourceId ?? -1)
      const name = r?.factoryName ?? 'UNKNOWN'
      if (!seen.has(name)) seen.set(name, '#94a3b8')
    }
    return Array.from(seen.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, color]) => ({
        id: `lane-factory-${name}`,
        label: name,
        color,
        textColor: '#ffffff'
      }))
  }

  if (groupBy.value === 'department') {
    const seen = new Map<string, string>()
    for (const t of tasks) {
      const r = resourceMap.get(t.resourceId ?? -1)
      const name = r?.productionDepartmentName ?? 'UNKNOWN'
      if (!seen.has(name)) seen.set(name, '#94a3b8')
    }
    return Array.from(seen.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, color]) => ({
        id: `lane-dept-${name}`,
        label: name,
        color,
        textColor: '#ffffff'
      }))
  }

  return []
})

/* ===== 状态 / 锁定 染色（仅展示，不写库） ===== */
const STATUS_COLORS: Record<TaskStatus, string> = {
  PLANNED: '#94a3b8',
  RELEASED: '#3b82f6',
  IN_PROGRESS: '#8b5cf6',
  COMPLETED: '#059669',
  CANCELLED: '#64748b'
}

const LOCK_LABEL: Record<TaskLockMarker, string> = {
  EXECUTION: '执行锁定',
  FIRM: '已确认',
  FROZEN: '冻结'
}

const LOCK_TAG_TYPE: Record<TaskLockMarker, 'danger' | 'warning' | 'info'> = {
  EXECUTION: 'danger',
  FIRM: 'warning',
  FROZEN: 'info'
}

/* ===== 文档第 2.2 节：四类事实标签 ===== */
const FACT_TYPE_LABELS: Record<'FACT' | 'RESULT' | 'ESTIMATED' | 'RECOMMENDATION', string> = {
  FACT: '真实事实',
  RESULT: '排程结果',
  ESTIMATED: '估算承诺',
  RECOMMENDATION: '建议重排'
}
const FACT_TYPE_TAG: Record<
  'FACT' | 'RESULT' | 'ESTIMATED' | 'RECOMMENDATION',
  'info' | 'success' | 'warning' | 'danger'
> = {
  FACT: 'info',
  RESULT: 'success',
  ESTIMATED: 'warning',
  RECOMMENDATION: 'danger'
}

/* ===== 计划版本下拉标签（状态枚举中文映射） ===== */
function versionOptionLabel(v: PlanVersionSummaryDto): string {
  const label = PLAN_VERSION_STATUS_LABELS[v.status as PlanVersionStatus] ?? v.status
  return `${v.versionCode}（${label}）`
}

/* ===== 文档第 16 节：MES 下发资格展示 ===== */
const MES_ELIGIBLE_LABEL = {
  ELIGIBLE: '可下发',
  INELIGIBLE: '不可下发',
  UNKNOWN: '未判定'
} as const
const MES_ELIGIBLE_TYPE = {
  ELIGIBLE: 'success',
  INELIGIBLE: 'error',
  UNKNOWN: 'info'
} as const

/* ===== 文档第 11 节：异常与原因解释 — 根因展示标签 ===== */
const DELAY_REASON_LABEL: Record<GanttTaskDelayReason['reasonCode'], string> = {
  MATERIAL_AVAILABILITY: '物料未到货',
  CAPACITY_SHORTAGE: '设备产能不足',
  UPSTREAM_DELAY: '上游阶段延迟',
  LOCK_BLOCKING: 'Firm/FROZEN 锁占关键窗口',
  CROSS_DOMAIN_BLOCK: '外排程域共享设备阻挡',
  EQUIPMENT_FAILURE: '设备故障',
  DUE_DATE_RISK: '到期风险（兜底）',
  OTHER: '其它'
}
const DELAY_REASON_ICON: Record<GanttTaskDelayReason['reasonCode'], string> = {
  MATERIAL_AVAILABILITY: 'vi-mdi:package-variant',
  CAPACITY_SHORTAGE: 'vi-mdi:gauge-low',
  UPSTREAM_DELAY: 'vi-mdi:arrow-up-bold-circle-outline',
  LOCK_BLOCKING: 'vi-mdi:lock-alert',
  CROSS_DOMAIN_BLOCK: 'vi-mdi:alert-octagon',
  EQUIPMENT_FAILURE: 'vi-mdi:tools',
  DUE_DATE_RISK: 'vi-mdi:clock-alert',
  OTHER: 'vi-mdi:help-circle'
}
const SEVERITY_TAG_TYPE: Record<GanttTaskDelayReason['severity'], 'info' | 'warning' | 'danger'> = {
  INFO: 'info',
  WARNING: 'warning',
  ERROR: 'danger',
  CRITICAL: 'danger'
}
const SEVERITY_LABEL: Record<GanttTaskDelayReason['severity'], string> = {
  INFO: '提示',
  WARNING: '告警',
  ERROR: '错误',
  CRITICAL: '严重'
}
const ISSUE_CATEGORY_DESC: Record<GanttTaskDelayReason['issueCategory'], string> = {
  DATA_ISSUE: '上游主数据 / 业务事实层面问题（PO ETA / PI Position / 库存等）',
  SCHEDULING_ISSUE: '排程引擎层面问题（产能 / 锁 / 上游工序 / 设备冲突等）'
}

/* ===== DTO → DhxGantt 映射 ===== */
function formatDate(iso: string | undefined): string {
  if (!iso) return dayjs().format('YYYY-MM-DD HH:mm')
  return dayjs(iso).format('YYYY-MM-DD HH:mm')
}

function hoursBetween(startIso: string, endIso: string): number {
  const diff = dayjs(endIso).diff(dayjs(startIso), 'hour', true)
  return Math.max(1, Math.round(diff || 1))
}

/**
 * 4号位约束 #1：保证 status 只能是 5 种枚举之一
 * 后端理论上已是，但前端再做一次硬校验，避免 mock / 老数据渗入非法值
 */
function sanitizeStatus(input: string | undefined): TaskStatus {
  if (input && input in TASK_STATUS_LABELS) return input as TaskStatus
  return 'PLANNED'
}

/**
 * 任务条颜色
 *  - 按 Domain 分组时：Domain 色（让泳道视觉一致，U01）
 *  - 其他维度：Status 色（保持状态可读）
 */
function pickBarColor(task: GanttTaskDto, status: TaskStatus): string {
  if (groupBy.value === 'domain') {
    return DOMAIN_COLORS[task.domainKey] ?? '#94a3b8'
  }
  return STATUS_COLORS[status] ?? '#94a3b8'
}

/**
 * 决定任务所属泳道 ID（与 lanes 保持一致）
 *  - resource: lane-{resourceId}
 *  - domain:   lane-domain-{domainKey}
 *  - stage:    lane-stage-{stage}
 *  - factory:  lane-factory-{factoryName}
 *  - department: lane-dept-{productionDepartmentName}
 */
function getTaskGroupKey(task: GanttTaskDto, resourceMap: Map<number, GanttResourceDto>): string {
  switch (groupBy.value) {
    case 'domain':
      return `lane-domain-${task.domainKey}`
    case 'stage': {
      const r = resourceMap.get(task.resourceId ?? -1)
      return `lane-stage-${r?.stage ?? 'UNKNOWN'}`
    }
    case 'factory': {
      const r = resourceMap.get(task.resourceId ?? -1)
      return `lane-factory-${r?.factoryName ?? 'UNKNOWN'}`
    }
    case 'department': {
      const r = resourceMap.get(task.resourceId ?? -1)
      return `lane-dept-${r?.productionDepartmentName ?? 'UNKNOWN'}`
    }
    case 'resource':
    default:
      return `lane-${task.resourceId}`
  }
}

const dhxTasks = computed<DhxGanttTask[]>(() => {
  const data = ganttData.value
  if (!data) return []
  const resourceMap = new Map(data.resources.map((r) => [r.resourceId, r]))

  // 泳道行（Y 轴；按 groupBy 维度动态生成）
  const laneRows: DhxGanttTask[] = lanes.value.map((lane) => {
    const laneTasks = filteredTasks.value.filter((t) => getTaskGroupKey(t, resourceMap) === lane.id)
    const firstTask = laneTasks[0]
    return {
      id: lane.id,
      text: lane.label,
      start_date: formatDate(firstTask?.plannedStartTime),
      duration: firstTask
        ? hoursBetween(firstTask.plannedStartTime!, firstTask.plannedEndTime!)
        : 1,
      type: 'project',
      parent: 0,
      open: true,
      readonly: true,
      color: lane.color,
      textColor: lane.textColor,
      details: lane.label
    }
  })

  // 任务条（已应用 Domain 过滤器；U01/U04/U14/U06 全上）
  const taskRows: DhxGanttTask[] = filteredTasks.value.flatMap((t) => {
    const status = sanitizeStatus(t.status)
    const resource = resourceMap.get(t.resourceId ?? -1)
    const baseRow: DhxGanttTask & Record<string, unknown> = {
      id: t.taskId,
      text: `${t.taskNo}${t.orderNo ? ' | ' + t.orderNo : ''}`,
      start_date: formatDate(t.plannedStartTime),
      duration: hoursBetween(t.plannedStartTime!, t.plannedEndTime!),
      parent: getTaskGroupKey(t, resourceMap),
      type: 'task',
      open: true,
      // 任务条：可拖动（拖完会触发 handleTaskDragged → 弹确认 → 走 MANUAL_RESCHEDULE）
      // 全图 readonly 由 canManualReschedule 在 wrapper 层控制，VIEWER 时 dhtmlx-gantt 整图禁用 drag_move
      readonly: false,
      color: pickBarColor(t, status),
      textColor: '#ffffff',
      status,
      orderId: t.orderNo,
      materialName: t.materialName,
      laneId: t.resourceId ? String(t.resourceId) : '',
      lockMarker: t.lockMarker,
      planFreezeFlag: t.lockMarker === 'FROZEN',
      executionLockFlag: t.lockMarker === 'EXECUTION',
      isLocked: t.lockMarker === 'FIRM',
      warningCount: t.isDelayed ? 1 : 0,
      operationCode: t.operationCode,
      operationSeq: t.operationSeq,
      netQty: t.netQty,
      plannedProcessQty: t.plannedProcessQty,
      quantity: t.quantity,
      uom: t.uom,
      isDelayed: t.isDelayed,
      laneName: resource?.resourceName,
      domainKey: t.domainKey,
      shareCount: t.taskShare?.length ?? 1,
      crossDomainBlocked: !!t.crossDomainBlocked,
      hasSegments: !!t.taskSegments?.length,
      segmentCount: t.taskSegments?.length ?? 0
    }

    // U06：无拆分的任务直接返回单条
    if (!t.taskSegments || t.taskSegments.length === 0) {
      return [baseRow]
    }

    // U06：有拆分 → 在 baseRow 下方追加 N 条 segment 子条
    const segmentRows: DhxGanttTask[] = t.taskSegments.map(
      (seg) =>
        ({
          id: `${t.taskId}_seg_${seg.segmentSeq}`,
          text: `${t.taskNo} · 段${seg.segmentSeq} (${seg.qty} ${t.uom})`,
          start_date: formatDate(seg.startTime),
          duration: hoursBetween(seg.startTime, seg.endTime),
          parent: getTaskGroupKey(t, resourceMap),
          type: 'task',
          open: true,
          readonly: true,
          color: pickBarColor(t, status),
          textColor: '#ffffff',
          opacity: 0.85,
          // segment 字段供 drawer 使用
          isSegment: true,
          parentTaskId: t.taskId,
          segmentSeq: seg.segmentSeq,
          segmentQty: seg.qty,
          segmentReason: seg.reason ?? ''
        }) as DhxGanttTask & Record<string, unknown>
    )

    return [baseRow, ...segmentRows]
  })

  return [...laneRows, ...taskRows]
})

/* ===== 任务详情 Drawer ===== */
const drawerVisible = ref(false)
const selectedTaskId = ref<number | null>(null)
const selectedTask = computed<GanttTaskDto | null>(() => {
  if (selectedTaskId.value === null) return null
  return ganttData.value?.tasks.find((t) => t.taskId === selectedTaskId.value) ?? null
})

/** 当前 Task 关联的资源（用于展示 Resource Down / 不可用窗口） */
const selectedResource = computed(() => {
  const t = selectedTask.value
  if (!t?.resourceId) return null
  return ganttData.value?.resources.find((r) => r.resourceId === t.resourceId) ?? null
})

/** 当前 Task 的 plannedStart/End 与资源的 unavailableWindows 是否有交集（轻量客户端判定） */
const resourceImpactsSelected = computed(() => {
  const t = selectedTask.value
  const r = selectedResource.value
  if (!t?.plannedStartTime || !t?.plannedEndTime || !r?.unavailableWindows?.length) return []
  const tStart = new Date(t.plannedStartTime).getTime()
  const tEnd = new Date(t.plannedEndTime).getTime()
  return r.unavailableWindows.filter((w) => {
    const wStart = new Date(w.from).getTime()
    const wEnd = new Date(w.to).getTime()
    return wStart < tEnd && wEnd > tStart
  })
})

/**
 * Section 11：当前 Task 根因按 issueCategory 分组（数据问题 / 排程问题）
 *  - 文档第 11 节硬约束：数据问题与排程问题要分开显示
 *  - 仅展示后端下发的 ScheduleExplanationFact，前端不计算
 */
const explanationReasonsByCategory = computed<{
  DATA_ISSUE: NonNullable<GanttTaskDto['delayReasons']>
  SCHEDULING_ISSUE: NonNullable<GanttTaskDto['delayReasons']>
}>(() => {
  const reasons: GanttTaskDelayReason[] = selectedTask.value?.delayReasons ?? []
  const dataIssue = reasons.filter((r) => r.issueCategory === 'DATA_ISSUE')
  const schedulingIssue = reasons.filter((r) => r.issueCategory === 'SCHEDULING_ISSUE')
  // SCHEDULING_ISSUE 内部按 severity 倒序：CRITICAL > ERROR > WARNING > INFO
  const sevOrder = { CRITICAL: 4, ERROR: 3, WARNING: 2, INFO: 1 } as const
  const sortBySeverity = (a: GanttTaskDelayReason, b: GanttTaskDelayReason) =>
    sevOrder[b.severity] - sevOrder[a.severity]
  dataIssue.sort(sortBySeverity)
  schedulingIssue.sort(sortBySeverity)
  return { DATA_ISSUE: dataIssue, SCHEDULING_ISSUE: schedulingIssue }
})

/* ===== P5 §12 看板提示：扫所有任务，计算受设备故障影响的 ID 集合 =====
 * 文档第 12 节：故障事实 → ImpactAssessment → ScheduleExplanationFact → RescheduleRecommendation → 看板提示
 * 实现思路：
 *  - 对每个 Task，按 resourceId 找对应的 Resource
 *  - 如果 Task 时间窗与 Resource.unavailableWindows 任一窗口相交，则该 Task 算受影响
 *  - 结果传给 DhxGantt 的 :highlight-task-ids，wrapper 自动给任务条加 dhx-task-impacted class
 *    （CSS 黄色 box-shadow 圈，已写好在 wrapper 里）*/
const impactedTaskIds = computed<number[]>(() => {
  const data = ganttData.value
  if (!data) return []
  const resourceMap = new Map(data.resources.map((r) => [r.resourceId, r]))
  const ids: number[] = []
  for (const t of data.tasks) {
    if (!t.plannedStartTime || !t.plannedEndTime) continue
    const r = resourceMap.get(t.resourceId ?? -1)
    if (!r?.unavailableWindows?.length) continue
    const tStart = new Date(t.plannedStartTime).getTime()
    const tEnd = new Date(t.plannedEndTime).getTime()
    const hit = r.unavailableWindows.some((w) => {
      const wStart = new Date(w.from).getTime()
      const wEnd = new Date(w.to).getTime()
      return wStart < tEnd && wEnd > tStart
    })
    if (hit) ids.push(t.taskId)
  }
  return ids
})

/** 受影响任务的精简信息（taskId + taskNo + 资源 code），供 KPI Tooltip 展示 */
const impactedTaskList = computed(() => {
  const data = ganttData.value
  if (!data) return []
  const resourceMap = new Map(data.resources.map((r) => [r.resourceId, r]))
  const set = new Set(impactedTaskIds.value)
  return data.tasks
    .filter((t) => set.has(t.taskId))
    .map((t) => ({
      taskId: t.taskId,
      taskNo: t.taskNo,
      resourceCode: resourceMap.get(t.resourceId ?? -1)?.resourceCode ?? '-'
    }))
})

/** P5 §12 受影响订单：点击 KPI 数字切换只看受影响任务（与 domainFilter 是 AND 关系） */
const filterImpactedOnly = ref(false)

/** 把"只看受影响"过滤器合进 filteredTasks */
function applyImpactFilter(tasks: typeof filteredTasks.value): typeof filteredTasks.value {
  if (!filterImpactedOnly.value) return tasks
  const set = new Set(impactedTaskIds.value)
  return tasks.filter((t) => set.has(t.taskId))
}

function openTaskDetail(taskId: string | number) {
  // 1) 泳道行被点击（dhtmlx-gantt 把泳道当成 project 类型渲染，ID = "lane-{resourceId}"）
  //    这种点击不应该打开任务详情，应该明确告诉用户"请点任务条而不是泳道行"
  const raw = String(taskId)
  if (raw.startsWith('lane-')) {
    ElMessage.info('请点击下方的任务条（彩色矩形），泳道行不承载任务详情')
    return
  }

  // 2) 其它异常 ID（如 dhtmlx-gantt 内部 ID）
  if (raw.startsWith('lane_')) {
    ElMessage.info(`请点击下方的任务条，泳道行 ID=${raw}`)
    return
  }

  // U06：segment 子条 ID 形如 "{parentTaskId}_seg_{seq}"，点击时跳到父 Task
  const segIdx = raw.lastIndexOf('_seg_')
  if (segIdx > 0) {
    selectedTaskId.value = Number(raw.slice(0, segIdx))
  } else {
    selectedTaskId.value = Number(raw)
  }

  // 3) 防御：selectedTaskId 拿不到对应任务（极少见；类型不匹配 / 数据未加载）
  const matched = ganttData.value?.tasks.find((t) => t.taskId === selectedTaskId.value) ?? null
  if (!matched) {
    console.warn(
      '[Gantt] selectedTaskId not matched. raw=',
      raw,
      'parsed=',
      selectedTaskId.value,
      'available taskIds=',
      ganttData.value?.tasks.slice(0, 5).map((t) => t.taskId)
    )
    ElMessage.warning(`未找到任务 #${selectedTaskId.value}，数据可能尚未加载完成`)
    drawerVisible.value = true
    return
  }

  drawerVisible.value = true
}

/* ===== 事件处理 ===== */
/**
 * 单击 → Drawer 打开
 *  - 浏览器在 dblclick 之前会派发 2 次 click 事件，若直接打开 Drawer 会"闪一下"
 *  - 用 setTimeout 延迟 ~250ms，等 dblclick 来抢
 *  - dblclick 抢到就把这个定时器清掉
 */
let pendingSingleClickTimer: number | null = null

function handleTaskClick(taskId: string | number) {
  if (pendingSingleClickTimer !== null) {
    window.clearTimeout(pendingSingleClickTimer)
  }
  pendingSingleClickTimer = window.setTimeout(() => {
    pendingSingleClickTimer = null
    openTaskDetail(taskId)
  }, 250)
}

function cancelPendingSingleClick(): void {
  if (pendingSingleClickTimer !== null) {
    window.clearTimeout(pendingSingleClickTimer)
    pendingSingleClickTimer = null
  }
}

/**
 * 4号位文档第 6 节最后一句：如需人工调整，应发起 MANUAL_RESCHEDULE 形成 Candidate，再比较、确认
 * 4号位文档第 24 节：禁止"任意拖拽直接改正式 Task"
 *
 * 现在允许拖动，但 DhxGantt 包装层永远 onBeforeTaskChanged=false → 自动回滚位置（不写库），
 * 这里只负责：拖完弹确认框 → 走 runApi.triggerReschedule（runType=LOCAL_RESCHEDULE）→ Candidate 页
 * 角色门控已通过 :readonly="!canManualReschedule" 在 dhtmlx-gantt 层拦住，VIEWER 完全拖不了
 */

/* ===== 统计 KPI ===== */
const stats = computed(() => {
  const tasks = ganttData.value?.tasks ?? []
  return {
    total: tasks.length,
    delayed: tasks.filter((t) => t.isDelayed).length,
    completed: tasks.filter((t) => sanitizeStatus(t.status) === 'COMPLETED').length,
    inProgress: tasks.filter((t) => sanitizeStatus(t.status) === 'IN_PROGRESS').length
  }
})

/* ===== 加载 ===== */
async function refresh() {
  // P0：先加载用户信息（决定按钮可见性 Section 18 角色门控）
  if (!apsAuth.userInfo) {
    await apsAuth.loadUserInfo()
  }
  await scheduleStore.loadVersions()
  if (scheduleStore.currentVersionId !== null) {
    await scheduleStore.loadCurrent()
  }
}

watch(currentVersionId, async (id) => {
  if (id !== null) await scheduleStore.loadCurrent()
})

onMounted(refresh)

/* ===== MANUAL_RESCHEDULE（文档第 6 节最后一句 / Section 12 / Section 17 审计） ===== */
const apsAuth = useApsAuthStore()
const router = useRouter()

/** PMC 及以上才可发起重排（v1.2 §23.1 权限码门控）
 *  - aps.reschedule.manual：dev seed 下 aps.planner + aps.admin.system 持有
 *  - 不用角色判断的原因：与 3号位后端二次校验用同一码；前端显隐 = 后端能写
 */
const canManualReschedule = computed(() => apsAuth.has('aps.reschedule.manual'))

/** 角色徽章颜色（P2 角色切换器 — v1.2 DDL 角色码版） */
const ROLE_TAG: Record<RoleKey, 'success' | 'warning' | 'info' | 'primary' | 'danger'> = {
  'aps.viewer.management': 'info',
  'aps.planner': 'success',
  'aps.admin.aps': 'warning',
  'aps.supervisor.workshop': 'primary',
  'aps.coordinator.material': 'primary',
  'aps.service.api': 'primary',
  'aps.admin.system': 'danger'
}

/* ===== §10A 白天人工调整：本页承载 4 个独立业务入口 =====
 * 冻结文档《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）：
 * V1 只提供 5 个独立中文业务入口，**不提供"其它明确对象局部调整"万能入口**。
 *  - 本页：甘特图调整(10A.2) / 设备故障后重排(10A.3) / 资源日历调整后重排(10A.4) / 整 Domain 人工重排(10A.5)
 *  - Order 页：已有订单提前(10A.1)
 * 统一出口 triggerBusinessEntry → POST /api/governance/run/candidate（Policy = aps.plan.run）
 *  - 权限：canPlanRun（端点要求 aps.plan.run）；拖拽只读门控仍用 canManualReschedule
 *  - 单 Domain（P0-02）：每个入口只能选一个 domainKey，跨 Domain 由多次单 Domain 调用链式汇总
 *  - U41：Domain 选项按 apsAuth.dataScope.domainKeys 收敛——非空(受限)仅授权 Domain，空(global)全量
 *  - 载荷构建/预校验集中在 runScope.ts（validateScopeDraft / buildScope），本页只做预填与跳转
 */
type BusinessEntryKey =
  | 'GANTT_ADJUSTMENT'
  | 'EQUIPMENT_FAILURE'
  | 'RESOURCE_CALENDAR_CHANGE'
  | 'DOMAIN_MANUAL_RESCHEDULE'

/** 端点权限码（aps.plan.run）——五入口统一门控 */
const canPlanRun = computed(() => apsAuth.has('aps.plan.run'))

const BUSINESS_DOMAIN_OPTIONS = computed<DomainKey[]>(() => {
  const all = domainStore.activeDomains.map((d) => d.domainKey)
  const allowed = apsAuth.dataScope.domainKeys as readonly string[]
  return allowed.length === 0 ? all : all.filter((k) => allowed.includes(k))
})

/** 入口 → Candidate 页 source 标签 */
const ENTRY_SOURCE: Record<BusinessEntryKey, string> = {
  GANTT_ADJUSTMENT: 'gantt-adjust',
  EQUIPMENT_FAILURE: 'equipment-failure',
  RESOURCE_CALENDAR_CHANGE: 'resource-calendar',
  DOMAIN_MANUAL_RESCHEDULE: 'domain-manual'
}

/** 当前打开的入口 Dialog（null = 全关） */
const entryDialog = ref<BusinessEntryKey | null>(null)
const entrySubmitting = ref(false)
const entryDomainKey = ref<DomainKey | undefined>(undefined)
const entryPreselectTaskIds = ref<number[]>([])
const entryPreselectTargetTime = ref<string | undefined>(undefined)
const entryPreselectResourceIds = ref<number[]>([])

/** 打开甘特图调整（拖拽 / Task Drawer 单条预填；工具栏入口无预填） */
function openGanttAdjust(taskIds: number[] = [], targetTime?: string, domainKey?: DomainKey): void {
  entryPreselectTaskIds.value = taskIds
  entryPreselectTargetTime.value = targetTime
  entryPreselectResourceIds.value = []
  entryDomainKey.value = domainKey
  entryDialog.value = 'GANTT_ADJUSTMENT'
}

/** 打开资源类入口（10A.3 / 10A.4；Resource Drawer 单条预填） */
function openResourceEntry(
  key: 'EQUIPMENT_FAILURE' | 'RESOURCE_CALENDAR_CHANGE',
  resourceIds: number[] = [],
  domainKey?: DomainKey
): void {
  entryPreselectTaskIds.value = []
  entryPreselectTargetTime.value = undefined
  entryPreselectResourceIds.value = resourceIds
  entryDomainKey.value = domainKey
  entryDialog.value = key
}

/** 打开整 Domain 人工重排（10A.5） */
function openDomainManual(): void {
  entryPreselectTaskIds.value = []
  entryPreselectTargetTime.value = undefined
  entryPreselectResourceIds.value = []
  entryDomainKey.value = undefined
  entryDialog.value = 'DOMAIN_MANUAL_RESCHEDULE'
}

/** 工具栏「白天人工调整」下拉分发 */
function onEntryCommand(key: BusinessEntryKey): void {
  if (key === 'GANTT_ADJUSTMENT') openGanttAdjust()
  else if (key === 'DOMAIN_MANUAL_RESCHEDULE') openDomainManual()
  else openResourceEntry(key)
}

/** Dialog 提交 → 统一出口 → 跳 Candidate 对比页（页面 5） */
async function submitBusinessEntry(payload: BusinessEntrySubmission): Promise<void> {
  const actor = apsAuth.userInfo?.userCode ?? 'unknown'
  entrySubmitting.value = true
  try {
    const result = await triggerBusinessEntry({
      trigger: payload.trigger,
      domainKey: payload.domainKey,
      draft: payload.draft,
      actor
    })
    ElMessage.success(
      `已发起：批次 #${result.newRunId}，候选版本 #${result.candidatePlanVersionId}。跳转对比页…`
    )
    entryDialog.value = null
    drawerVisible.value = false
    router.push({
      name: 'ApsCandidate',
      query: {
        candidatePlanVersionId: String(result.candidatePlanVersionId),
        basePlanVersionId: String(result.basePlanVersionId),
        source: ENTRY_SOURCE[payload.trigger as BusinessEntryKey] ?? 'manual'
      }
    })
  } catch (err) {
    ElMessage.error(`发起重排失败：${(err as Error)?.message ?? '未知错误'}`)
  } finally {
    entrySubmitting.value = false
  }
}

/** mock 模式角色切换：写一个 wrapper，便于 @command 回调 */
function switchMockRole(role: RoleKey): void {
  apsAuth.mockSwitchRole(role)
  ElMessage.success(`已切换角色：${role}（仅演示模式）`)
}

/** P1-08 插单影响分析：跳 /aps/ctp 并预选 INSERT_IMPACT_ANALYSIS（CTP 页面 onMounted 读取） */
function goInsertImpactAnalysis(): void {
  router.push({ path: '/aps/ctp', query: { purpose: 'INSERT_IMPACT_ANALYSIS' } })
}

/** Task 状态 → ElTag 类型映射（P3 浮窗用） */
function statusTagType(status: TaskStatus): 'success' | 'warning' | 'info' | 'primary' | 'danger' {
  switch (status) {
    case 'PLANNED':
      return 'info'
    case 'RELEASED':
      return 'primary'
    case 'IN_PROGRESS':
      return 'warning'
    case 'COMPLETED':
      return 'success'
    case 'CANCELLED':
      return 'danger'
    default:
      return 'info'
  }
}

/* ===== P3：双击任务条 → 轻量 Popover；右键任务条 → 完整菜单 ===== */
interface FloatingPopState {
  visible: boolean
  x: number
  y: number
  task: GanttTaskDto | null
}

/** 把 dhtmlx-gantt 传过来的 taskId 解析成 GanttTaskDto（兼容 lane-* / *_seg_* 前缀） */
function resolveTaskByRawId(raw: string | number): GanttTaskDto | null {
  const s = String(raw)
  if (s.startsWith('lane-') || s.startsWith('lane_')) return null
  const segIdx = s.lastIndexOf('_seg_')
  const taskId = segIdx > 0 ? Number(s.slice(0, segIdx)) : Number(s)
  if (!Number.isFinite(taskId)) return null
  return ganttData.value?.tasks.find((t) => t.taskId === taskId) ?? null
}

const dblClickPopover = ref<FloatingPopState>({ visible: false, x: 0, y: 0, task: null })
const contextMenu = ref<FloatingPopState>({ visible: false, x: 0, y: 0, task: null })

function handleTaskDblClick(taskId: string | number, e?: MouseEvent): void {
  const task = resolveTaskByRawId(taskId)
  if (!task) {
    ElMessage.info('双击无效：请双击任务条（彩色矩形），泳道 / Segment 行不承载双击交互')
    return
  }
  contextMenu.value.visible = false
  dblClickPopover.value = {
    visible: true,
    x: e?.clientX ?? Math.round(window.innerWidth / 2),
    y: e?.clientY ?? Math.round(window.innerHeight / 2),
    task
  }
}

/** P3 修复：直接在 Gantt.vue 的 gantt-area 上挂 capture 监听
 *  - 不依赖 DhxGantt 组件的 taskDblClick emit
 *  - 不依赖 dhtmlx-gantt 内部事件
 *  - 先于 gantt 内部处理（capture 阶段）
 *  - 兼容任务条的不同 class 名（gantt_task_line / gantt_task_line_full / data-task-id）
 */
function handleAreaDblClick(e: MouseEvent): void {
  const target = e.target as HTMLElement | null
  if (!target) return
  // 兼容 dhtmlx-gantt 7+ 的多种任务条渲染
  const taskBar = target.closest(
    '.gantt_task_line, .gantt_task_line_full, .gantt_row[data-task-id], .gantt_row[task_id]'
  ) as HTMLElement | null
  if (!taskBar) return
  const taskId = taskBar.getAttribute('task_id') ?? taskBar.getAttribute('data-task-id')
  if (!taskId) return
  // dblclick 抢先：取消待执行的单击（避免 Drawer 闪一下）
  cancelPendingSingleClick()
  // 关掉可能已经开着的 Drawer（双击语义：聚焦浮窗，关闭详情面板）
  drawerVisible.value = false
  handleTaskDblClick(taskId, e)
}

function handleTaskContextMenu(taskId: string | number, e: MouseEvent): void {
  const task = resolveTaskByRawId(taskId)
  if (!task) return
  dblClickPopover.value.visible = false
  // 防止 dhtmlx-gantt 默认行为抢焦点：把右键位置减去甘特图容器的 offset
  const root = document.querySelector('.aps-gantt-page') as HTMLElement | null
  const rect = root?.getBoundingClientRect()
  contextMenu.value = {
    visible: true,
    x: e.clientX - (rect?.left ?? 0),
    y: e.clientY - (rect?.top ?? 0),
    task
  }
}

/* ===== P4：软拖动 → 打开 §10A.2 甘特图调整 Dialog（预填单条 Task + 软目标时间） =====
 * 4号位文档第 6 节最后一句 + 第 24 节：禁止"任意拖拽直接改正式 Task"，必须经 Candidate 比较确认
 *  - DhxGantt wrapper 的 onBeforeTaskChanged 永远 return false → 位置自动回滚（不写库）
 *  - 拖拽只支持单 Task（payload.taskId 单值，组件不支持多 Task 拖拽）；多 Task 由 Dialog 表格多选承担
 *  - 角色门控：:readonly="!canManualReschedule"（VIEWER 在组件层就拖不动）
 */
function dhxTimeToIso(v: string): string {
  return new Date(v.replace(' ', 'T')).toISOString()
}

function handleTaskDragged(payload: DhxGanttDragPayload): void {
  const task = ganttData.value?.tasks.find((t) => t.taskId === Number(payload.taskId)) ?? null
  if (!task) {
    ElMessage.error(`未找到任务 #${payload.taskId}，请刷新页面后重试`)
    return
  }

  ElMessage.info(`任务位置已自动回滚到原计划；已在调整表单预填 ${task.taskNo} 的软目标时间`)
  openGanttAdjust([task.taskId], dhxTimeToIso(payload.targetStart), task.domainKey)
}

/** DhxGantt 包装层主动拒绝的拖动（一般不会触发，因为 wrapper 永远 onBeforeTaskChanged=false 让位置回滚） */
function handleTaskDragRejected(payload: DhxGanttDragRejectedPayload): void {
  ElMessage?.warning?.(payload.reason ?? '拖动被拒绝')
}

function closeDblClickPopover(): void {
  dblClickPopover.value.visible = false
}
function closeContextMenu(): void {
  contextMenu.value.visible = false
}
/** P3：Drawer 关闭 - 自实现 Drawer 后，click 100% 触发，直接关 */
function closeDrawer(): void {
  drawerVisible.value = false
}

/** 复制文本到剪贴板（modern API 失败时回退 execCommand） */
async function copyText(text: string, label = '已复制'): Promise<void> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
    } else {
      const ta = document.createElement('textarea')
      ta.value = text
      ta.style.position = 'fixed'
      ta.style.opacity = '0'
      document.body.appendChild(ta)
      ta.select()
      document.execCommand('copy')
      document.body.removeChild(ta)
    }
    ElMessage.success(`${label}：${text}`)
  } catch (err) {
    ElMessage.error(`复制失败：${(err as Error)?.message ?? '未知错误'}`)
  }
}

type CtxAction = 'detail' | 'copyTaskNo' | 'copyOrderNo' | 'goOrder' | 'goPi'

function onContextMenuAction(action: CtxAction): void {
  const t: GanttTaskDto | null = contextMenu.value.task
  if (!t) {
    closeContextMenu()
    return
  }
  switch (action) {
    case 'detail':
      selectedTaskId.value = t.taskId
      drawerVisible.value = true
      break
    case 'copyTaskNo':
      void copyText(t.taskNo, '已复制 TaskNo')
      break
    case 'copyOrderNo':
      if (!t.orderNo) {
        ElMessage.warning('该任务无 OrderNo，无法复制')
      } else {
        void copyText(t.orderNo, '已复制 OrderNo')
      }
      break
    case 'goOrder':
      if (!t.orderNo) {
        ElMessage.warning('该任务无 OrderNo，无法跳转订单页')
      } else {
        router.push({ name: 'ApsOrder', query: { orderNo: t.orderNo } })
      }
      break
    case 'goPi':
      router.push({ name: 'ApsPi' })
      break
  }
  closeContextMenu()
}

function onDblClickCopy(): void {
  const t = dblClickPopover.value.task
  if (!t) return
  void copyText(t.taskNo, '已复制 TaskNo')
}

/** 双击浮窗 → 打开完整详情 Drawer */
function openDetailFromDblClickPopover(): void {
  const t = dblClickPopover.value.task
  if (!t) return
  selectedTaskId.value = t.taskId
  drawerVisible.value = true
  closeDblClickPopover()
}

/** 全局 mousedown / Esc：点击空白关闭 floating；Esc 关闭 floating + Drawer */
function handleGlobalClose(e: MouseEvent): void {
  const target = e.target as HTMLElement | null
  if (!target) return
  if (target.closest('.aps-floating-popover')) return
  if (target.closest('.aps-ctx-menu')) return
  // 点击 Drawer panel 内部不关闭（Drawer 自己处理 click）
  if (target.closest('.aps-task-drawer')) return
  closeDblClickPopover()
  closeContextMenu()
}
function handleGlobalKey(e: KeyboardEvent): void {
  if (e.key === 'Escape') {
    closeDblClickPopover()
    closeContextMenu()
    if (drawerVisible.value) closeDrawer()
  }
}
onMounted(() => {
  document.addEventListener('mousedown', handleGlobalClose)
  document.addEventListener('keydown', handleGlobalKey)
})
onBeforeUnmount(() => {
  document.removeEventListener('mousedown', handleGlobalClose)
  document.removeEventListener('keydown', handleGlobalKey)
  cancelPendingSingleClick()
})

/**
 * Task Drawer「发起重排」→ 打开 §10A.2 甘特图调整 Dialog（预填单条 Task）
 * 4号位文档第 6 节最后一句：需人工调整时发起 Candidate 运行，再比较、确认
 * P0-02：单计划域（domainKey = task.domainKey）
 * P0-03：单 Task 属局部明确范围 → LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT（§10A.2）
 */
function triggerReschedule(): void {
  const task = selectedTask.value
  if (!task) return
  openGanttAdjust([task.taskId], task.plannedStartTime, task.domainKey)
}

/**
 * Resource Drawer「基于设备故障建议重排」→ 打开 §10A.3 设备故障后重排 Dialog（预填该资源）
 *
 * 4号位文档第 12 节硬约束 + 审核报告 P1-13：
 *  - 设备故障 → 仅展示影响窗口 + 提供建议重排入口，**V1 不建设 PAUSE/RESUME 状态闭环**，决策由 PMC 发起候选运行
 *  - 影响窗口 ≥1 个才允许触发（按钮 v-if 已卡住，这里再 defensive 兜底）
 * P0-02：单计划域（domainKey = resource.domainKey）
 * 载荷：changedResourceIds = [该 resourceId]（§10A.3）
 */
function triggerRescheduleForResource(): void {
  const task = selectedTask.value
  const resource = selectedResource.value
  if (!task || !resource) return

  if (resourceImpactsSelected.value.length === 0) {
    ElMessage.warning('当前资源不可用窗口不影响本任务，无需重排')
    return
  }

  openResourceEntry('EQUIPMENT_FAILURE', [resource.resourceId], resource.domainKey)
}
</script>

<template>
  <div class="aps-gantt-page">
    <!-- ===== 顶部工具栏 ===== -->
    <div class="toolbar">
      <div class="toolbar-left">
        <span class="label">版本</span>
        <ElSelect
          v-model="selectedVersionId"
          placeholder="选择计划版本"
          size="default"
          style="width: 180px"
          :loading="loading"
          :disabled="versions.length === 0"
        >
          <ElOption
            v-for="v in versions"
            :key="v.id"
            :label="versionOptionLabel(v)"
            :value="v.id"
          />
        </ElSelect>

        <span class="label">时间范围</span>
        <ElRadioGroup v-model="timeRange" size="default">
          <ElRadioButton value="D1">今日</ElRadioButton>
          <ElRadioButton value="D7">7 天</ElRadioButton>
          <ElRadioButton value="D30">30 天</ElRadioButton>
          <ElRadioButton value="D90">90 天</ElRadioButton>
        </ElRadioGroup>

        <span class="label">维度</span>
        <ElSelect v-model="groupBy" size="default" style="width: 130px">
          <ElOption
            v-for="opt in GROUP_BY_OPTIONS"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>

        <span class="label">排程域</span>
        <ElSelect
          v-model="domainFilter"
          multiple
          collapse-tags
          collapse-tags-tooltip
          size="default"
          style="width: 200px"
          placeholder="默认全选"
          :disabled="availableDomains.length === 0"
        >
          <ElOption v-for="dk in availableDomains" :key="dk" :label="domainLabel(dk)" :value="dk">
            <span>{{ domainLabel(dk) }}</span>
            <span style="margin-left: 8px; color: #94a3b8; font-size: 12px">{{ dk }}</span>
          </ElOption>
        </ElSelect>
        <DomainUnavailableBadge />

        <span class="stat">
          任务 <strong>{{ stats.total }}</strong> | 资源
          <strong>{{ ganttData?.resources.length ?? 0 }}</strong> | 维度
          <strong>{{ lanes.length }}</strong
          ><span
            v-for="dk in availableDomains"
            :key="dk"
            class="stat-domain"
            :style="{ color: DOMAIN_COLORS[dk] ?? '#94a3b8' }"
          >
            | {{ domainLabel(dk) }} <strong>{{ domainCounts[dk] ?? 0 }}</strong></span
          >
          <!-- P5 §12 受影响订单：现有 KPI 行内追加；0 新面板 -->
          <ElTooltip v-if="impactedTaskIds.length > 0" placement="bottom" :show-after="100">
            <template #content>
              <div class="impacted-tooltip">
                <div class="impacted-tooltip-title">
                  受设备故障影响的任务（{{ impactedTaskIds.length }} 个）：
                </div>
                <ul class="impacted-tooltip-list">
                  <li v-for="t in impactedTaskList.slice(0, 10)" :key="t.taskId">
                    <code>{{ t.taskNo }}</code>
                    <span class="impacted-tooltip-res">@ {{ t.resourceCode }}</span>
                  </li>
                  <li v-if="impactedTaskList.length > 10" class="impacted-tooltip-more">
                    …还有 {{ impactedTaskList.length - 10 }} 个
                  </li>
                </ul>
              </div>
            </template>
            <span
              class="stat-impacted"
              :class="{ 'is-active': filterImpactedOnly }"
              role="button"
              :title="filterImpactedOnly ? '点击取消只看受影响' : '点击只看受影响任务'"
              @click="filterImpactedOnly = !filterImpactedOnly"
            >
              <Icon icon="vi-mdi:alert-circle" />
              | 受影响 <strong>{{ impactedTaskIds.length }}</strong>
            </span>
          </ElTooltip>
        </span>
      </div>
      <div class="toolbar-right">
        <!-- 角色指示器 + 切换器（仅 mock 模式显示；生产模式自动隐藏） -->
        <ElDropdown
          v-if="apsAuth.mockAvailableRoles.length > 0"
          trigger="click"
          @command="switchMockRole"
        >
          <ElTag
            :type="ROLE_TAG[apsAuth.mockActiveRole]"
            effect="dark"
            class="role-badge"
            title="点击切换角色（仅演示模式可用）"
          >
            <Icon icon="vi-ep:user" />
            {{ apsAuth.mockActiveRole }}
            <Icon icon="vi-ep:arrow-down" class="role-caret" />
          </ElTag>
          <template #dropdown>
            <ElDropdownMenu>
              <ElDropdownItem
                v-for="r in apsAuth.mockAvailableRoles"
                :key="r"
                :command="r"
                :disabled="r === apsAuth.mockActiveRole"
              >
                <ElTag :type="ROLE_TAG[r]" size="small" effect="plain">{{ r }}</ElTag>
                <span class="role-hint">
                  {{
                    r === 'aps.viewer.management'
                      ? '只读'
                      : r === 'aps.admin.system'
                        ? '全部权限'
                        : ''
                  }}
                </span>
              </ElDropdownItem>
            </ElDropdownMenu>
          </template>
        </ElDropdown>
        <!-- §10A 白天人工调整：5 个独立中文业务入口（本页 4 项 + Order 页「已有订单提前」）
             V1 不提供"其它明确对象局部调整"万能入口（冻结文档 §十A） -->
        <ElDropdown trigger="click" :disabled="!canPlanRun" @command="onEntryCommand">
          <ElButton
            type="warning"
            plain
            :disabled="!canPlanRun"
            title="白天人工调整 — 4 个独立业务入口"
          >
            <Icon icon="vi-mdi:account-multiple-check" />
            白天人工调整
            <Icon icon="vi-ep:arrow-down" class="role-caret" />
          </ElButton>
          <template #dropdown>
            <ElDropdownMenu>
              <ElDropdownItem command="GANTT_ADJUSTMENT"> 甘特图调整（多个任务） </ElDropdownItem>
              <ElDropdownItem command="EQUIPMENT_FAILURE">
                设备故障后重排（多资源）
              </ElDropdownItem>
              <ElDropdownItem command="RESOURCE_CALENDAR_CHANGE">
                资源日历调整后重排（多资源）
              </ElDropdownItem>
              <ElDropdownItem command="DOMAIN_MANUAL_RESCHEDULE" divided>
                整排程域人工重排
              </ElDropdownItem>
            </ElDropdownMenu>
          </template>
        </ElDropdown>
        <ElButton
          type="info"
          plain
          :disabled="!canManualReschedule"
          title="插单影响分析 — 试算插入新订单对现有计划的影响"
          @click="goInsertImpactAnalysis"
        >
          <Icon icon="vi-mdi:flash-triangle-outline" />
          插单影响分析
        </ElButton>
        <ElButton type="primary" :loading="loading" title="刷新数据" @click="refresh">
          <Icon icon="vi-ep:refresh" />
        </ElButton>
      </div>
    </div>

    <!-- ===== 错误 / 空状态 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />
    <ElAlert
      v-else-if="!loading && stats.total === 0"
      type="info"
      :closable="false"
      show-icon
      title="当前版本暂无任务数据"
    />

    <!-- ===== 甘特图 =====
         软拖动（4号位文档第 6 节最后一句 + 第 24 节）：
         - VIEWER 角色 :readonly=true，整图不可拖
         - PMC+ 角色   :readonly=false，任务条可拖（lane 行仍然 task.readonly=true）
         - 拖完不直接写库：DhxGantt wrapper 的 onBeforeTaskChanged 永远 return false → 自动回滚位置
         - 然后 emit taskDragEnd → 这里弹确认 → 走 runApi.triggerReschedule（runType=LOCAL_RESCHEDULE）→ Candidate 页 -->
    <div class="gantt-area" @dblclick.capture="handleAreaDblClick">
      <DhxGantt
        :tasks="dhxTasks"
        :readonly="!canManualReschedule"
        :allow-resize="false"
        :allow-progress="false"
        :scale-unit="dateRange.scaleUnit"
        :date-from="dateRange.from"
        :date-to="dateRange.to"
        :show-compare="false"
        :show-actual="false"
        :highlight-task-ids="impactedTaskIds"
        @task-click="handleTaskClick"
        @task-dbl-click="handleTaskDblClick"
        @task-context-menu="handleTaskContextMenu"
        @task-drag-end="handleTaskDragged"
        @task-drag-rejected="handleTaskDragRejected"
      />
    </div>

    <!-- ===== P3：双击任务条 → 轻量浮窗（Task 摘要 + 复制 TaskNo） ===== -->
    <div
      v-if="dblClickPopover.visible && dblClickPopover.task"
      class="aps-floating-popover"
      :style="{ left: dblClickPopover.x + 'px', top: dblClickPopover.y + 'px' }"
    >
      <div class="popover-arrow"></div>
      <div class="popover-title">
        <Icon icon="vi-ep:pointer" class="popover-icon" />
        <span class="popover-taskno">{{ dblClickPopover.task.taskNo }}</span>
        <ElTag size="small" type="info" effect="plain">
          {{ dblClickPopover.task.domainKey }}
        </ElTag>
      </div>
      <ElDivider class="popover-divider" />
      <div class="popover-row">
        <span class="row-label">订单号：</span>
        <span class="row-val">
          {{ dblClickPopover.task.orderNo ?? '—' }}
        </span>
      </div>
      <div class="popover-row">
        <span class="row-label">计划开始：</span>
        <span class="row-val">
          {{ formatDate(dblClickPopover.task.plannedStartTime) }}
        </span>
      </div>
      <div class="popover-row">
        <span class="row-label">状态：</span>
        <ElTag size="small" :type="statusTagType(dblClickPopover.task.status)">
          {{ TASK_STATUS_LABELS[dblClickPopover.task.status] ?? dblClickPopover.task.status }}
        </ElTag>
      </div>
      <ElDivider class="popover-divider" />
      <div class="popover-actions">
        <ElButton size="small" type="primary" plain @click="onDblClickCopy">
          <Icon icon="vi-ep:copy-document" /> 复制 TaskNo
        </ElButton>
        <ElButton size="small" @click="openDetailFromDblClickPopover">
          <Icon icon="vi-ep:view" /> 查看完整详情
        </ElButton>
      </div>
    </div>

    <!-- ===== P3：右键任务条 → 完整菜单（5 个动作） ===== -->
    <div
      v-if="contextMenu.visible && contextMenu.task"
      class="aps-ctx-menu"
      :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
      @click.stop
    >
      <div class="ctx-menu-header">
        <Icon icon="vi-ep:pointer" />
        <span>{{ contextMenu.task.taskNo }}</span>
      </div>
      <ul class="ctx-menu-list">
        <li class="ctx-menu-item" @click="onContextMenuAction('detail')">
          <Icon icon="vi-ep:view" /> 查看任务详情
        </li>
        <li class="ctx-menu-item" @click="onContextMenuAction('copyTaskNo')">
          <Icon icon="vi-ep:copy-document" /> 复制 TaskNo
          <code class="ctx-menu-code">{{ contextMenu.task.taskNo }}</code>
        </li>
        <li
          class="ctx-menu-item"
          :class="{ 'is-disabled': !contextMenu.task.orderNo }"
          :title="contextMenu.task.orderNo ? '' : '该任务无 OrderNo'"
          @click="onContextMenuAction('copyOrderNo')"
        >
          <Icon icon="vi-ep:document" /> 复制 OrderNo
          <code v-if="contextMenu.task.orderNo" class="ctx-menu-code">
            {{ contextMenu.task.orderNo }}
          </code>
          <span v-else class="ctx-menu-empty">—</span>
        </li>
        <li
          class="ctx-menu-item"
          :class="{ 'is-disabled': !contextMenu.task.orderNo }"
          :title="contextMenu.task.orderNo ? '' : '该任务无 OrderNo'"
          @click="onContextMenuAction('goOrder')"
        >
          <Icon icon="vi-ep:position" /> 跳转到订单页
          <Icon icon="vi-ep:arrow-right" class="ctx-menu-arrow" />
        </li>
        <li class="ctx-menu-item" @click="onContextMenuAction('goPi')">
          <Icon icon="vi-ep:data-board" /> 跳转到 PI 页
          <Icon icon="vi-ep:arrow-right" class="ctx-menu-arrow" />
        </li>
      </ul>
    </div>

    <!-- ===== 页面只读提示 ===== -->
    <div class="hint-bar">
      <ElIcon class="hint-icon"><Icon icon="vi-mdi:information-outline" /></ElIcon>
      <span>
        本页只读：只展示排产结果，不直接修改计划。拖动任务条后位置会自动弹回原位，并转为「甘特图调整」表单，
        经确认后才可能生效。
      </span>
    </div>

    <!-- ===== 任务详情 Drawer（自实现 v-if + Transition + Teleport，避开 Element Plus append-to-body + slot 的 click 不触发 bug） =====
         设计要点：
         1. 不依赖 ElDrawer：自渲染 panel，自己捕获 click，绝对不会被全局 Header 用户头像遮挡
         2. mask 仍设 pointer-events: none，让双击任务条能穿透到 Gantt 任务条（drawerVisible 由 handleAreaDblClick 先置 false 再触发 popover）
         3. Esc 关闭由 handleGlobalKey 统一处理（已有）— 只需把 drawerVisible 也置 false
         4. 关闭按钮是原生 <button>，不依赖 Element Plus 的 click 事件链路 -->
    <Teleport to="body">
      <Transition name="aps-drawer-slide">
        <div
          v-if="drawerVisible"
          class="aps-task-drawer-mask"
          aria-hidden="false"
          @click.self="closeDrawer"
        >
          <div class="aps-task-drawer" role="dialog" aria-label="任务详情" @click.stop>
            <!-- 顶部 header：自渲染，绝对不会被全局 Header 遮挡 -->
            <div class="aps-drawer-header">
              <span class="aps-drawer-title">
                <Icon icon="vi-ep:tickets" class="aps-drawer-title-icon" />
                任务详情
                <ElTag
                  v-if="selectedTask"
                  size="small"
                  type="info"
                  effect="plain"
                  class="aps-drawer-taskno"
                >
                  {{ selectedTask.taskNo }}
                </ElTag>
              </span>
              <button
                type="button"
                class="aps-drawer-close-btn"
                aria-label="关闭 Drawer"
                @click="closeDrawer"
              >
                <Icon icon="vi-ep:close" />
                <span>关闭</span>
              </button>
            </div>

            <!-- Drawer 主体（可滚动） -->
            <div class="aps-drawer-body">
              <template v-if="selectedTask">
                <div class="detail-section">
                  <ElDescriptions :column="1" border size="small">
                    <ElDescriptionsItem label="任务编号">{{
                      selectedTask.taskNo
                    }}</ElDescriptionsItem>
                    <ElDescriptionsItem label="排程域">
                      <ElTag size="small" effect="plain">{{ selectedTask.domainKey }}</ElTag>
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="主订单">{{
                      selectedTask.orderNo ?? '-'
                    }}</ElDescriptionsItem>
                    <ElDescriptionsItem label="物料">{{
                      selectedTask.materialName ?? '-'
                    }}</ElDescriptionsItem>
                    <ElDescriptionsItem label="工序">
                      {{ selectedTask.operationCode }} (Seq {{ selectedTask.operationSeq }})
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="数量">
                      <span class="qty-cell">
                        <span class="qty-net">
                          净需求 <strong>{{ selectedTask.netQty }}</strong> {{ selectedTask.uom }}
                        </span>
                        <span class="qty-arrow">→</span>
                        <span class="qty-plan">
                          计划加工 <strong>{{ selectedTask.plannedProcessQty }}</strong>
                          {{ selectedTask.uom }}
                        </span>
                        <ElTooltip
                          :content="`计划加工 = 净需求 ÷ 良率（默认 95%）；两者不可混用`"
                          placement="top"
                        >
                          <ElTag size="small" type="info" effect="plain" style="margin-left: 6px">
                            说明
                          </ElTag>
                        </ElTooltip>
                      </span>
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="计划开始">
                      {{ selectedTask.plannedStartTime }}
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="计划结束">
                      {{ selectedTask.plannedEndTime }}
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="状态">
                      <ElTag
                        :color="STATUS_COLORS[sanitizeStatus(selectedTask.status)]"
                        size="small"
                        style="color: #fff"
                      >
                        {{ TASK_STATUS_LABELS[sanitizeStatus(selectedTask.status)] }}
                      </ElTag>
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="是否延期">
                      <ElTag :type="selectedTask.isDelayed ? 'danger' : 'success'" size="small">
                        {{ selectedTask.isDelayed ? '是' : '否' }}
                      </ElTag>
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="锁定标识">
                      <ElTag
                        v-if="selectedTask.lockMarker"
                        :type="LOCK_TAG_TYPE[selectedTask.lockMarker]"
                        size="small"
                      >
                        {{ LOCK_LABEL[selectedTask.lockMarker] }}
                      </ElTag>
                      <span v-else>-</span>
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="事实类型">
                      <ElTag
                        v-if="selectedTask.factType"
                        :type="FACT_TYPE_TAG[selectedTask.factType]"
                        size="small"
                        effect="dark"
                      >
                        {{ FACT_TYPE_LABELS[selectedTask.factType] }}
                      </ElTag>
                      <span v-else>-</span>
                      <ElTooltip
                        content="真实事实＝已经发生的事实；排程结果＝排产引擎算出的计划；估算承诺＝暂无确定依据的预计；建议重排＝系统给出的调整建议"
                        placement="top"
                      >
                        <ElTag size="small" type="info" effect="plain" style="margin-left: 6px">
                          说明
                        </ElTag>
                      </ElTooltip>
                    </ElDescriptionsItem>
                    <ElDescriptionsItem label="换型时间来源">
                      <ElTag
                        v-if="selectedTask.setupSource"
                        :type="SETUP_SOURCE_META[selectedTask.setupSource].tag"
                        size="small"
                        effect="dark"
                      >
                        {{ SETUP_SOURCE_META[selectedTask.setupSource].label }}
                      </ElTag>
                      <ElTooltip
                        v-if="selectedTask.setupSource === 'NONE'"
                        content="无换型规则兜底 → 按 0 分钟计算"
                        placement="top"
                      >
                        <ElTag size="small" type="info" effect="plain" style="margin-left: 6px">
                          兜底
                        </ElTag>
                      </ElTooltip>
                      <span v-else-if="!selectedTask.setupSource">-</span>
                      <ElTooltip
                        v-if="selectedTask.setupSource === 'INITIAL'"
                        content="班头首单 / 冷启动按 0 分钟计，属正常表现，不是数据质量问题"
                        placement="top"
                      >
                        <ElTag size="small" type="info" effect="plain" style="margin-left: 6px">
                          说明
                        </ElTag>
                      </ElTooltip>
                    </ElDescriptionsItem>
                  </ElDescriptions>

                  <!-- Section 十一：异常与原因解释 — 根因展示（4号位文档第 11 节硬约束）
                       文档原文："页面必须优先显示根因...不要只显示 DUE_DATE_RISK。
                       数据问题与排程问题要分开显示。"
                       根因类型（reasonCode）来自 ScheduleExplanationFact，4号位只展示不计算。 -->
                  <div class="explanation-section">
                    <div class="explanation-title">
                      <Icon icon="vi-mdi:comment-question-outline" />
                      原因解释
                      <ElTooltip
                        content="优先展示具体根因（如采购料到货晚 / 设备无产能 / 上游工序完成晚 / 固定任务占用关键窗口等），并按「数据问题」与「排程问题」分开列出。"
                        placement="top"
                      >
                        <ElIcon class="explanation-hint">
                          <Icon icon="vi-mdi:information-outline" />
                        </ElIcon>
                      </ElTooltip>
                    </div>

                    <!-- 无任何根因 -->
                    <div
                      v-if="
                        !selectedTask.delayReasons?.length &&
                        !selectedTask.isDelayed &&
                        !selectedTask.crossDomainBlocked
                      "
                      class="explanation-empty"
                    >
                      <Icon icon="vi-mdi:check-circle-outline" />
                      当前任务无延期根因
                    </div>

                    <!-- 分类渲染：先数据问题，再排程问题（文档第 11 节第三段硬约束） -->
                    <template v-else>
                      <!-- 数据问题 -->
                      <div
                        v-if="explanationReasonsByCategory.DATA_ISSUE.length > 0"
                        class="explanation-category is-data"
                      >
                        <div class="explanation-category-title">
                          <Icon icon="vi-mdi:database-alert" />
                          数据问题（{{ explanationReasonsByCategory.DATA_ISSUE.length }}）
                          <ElTooltip :content="ISSUE_CATEGORY_DESC.DATA_ISSUE" placement="top">
                            <ElIcon class="explanation-hint">
                              <Icon icon="vi-mdi:information-outline" />
                            </ElIcon>
                          </ElTooltip>
                        </div>
                        <ul class="explanation-reason-list">
                          <li
                            v-for="(r, idx) in explanationReasonsByCategory.DATA_ISSUE"
                            :key="`data-${idx}`"
                            class="explanation-reason-item"
                          >
                            <Icon
                              :icon="DELAY_REASON_ICON[r.reasonCode]"
                              class="reason-type-icon"
                            />
                            <span class="reason-type-label">
                              {{ DELAY_REASON_LABEL[r.reasonCode] }}
                            </span>
                            <ElTag
                              size="small"
                              :type="SEVERITY_TAG_TYPE[r.severity]"
                              effect="plain"
                            >
                              {{ SEVERITY_LABEL[r.severity] }}
                            </ElTag>
                            <span class="reason-desc">{{ r.description }}</span>
                            <span v-if="r.relatedObjectRef" class="reason-ref">
                              <code class="code-tag">{{ r.relatedObjectRef }}</code>
                            </span>
                            <span v-if="r.impactHours" class="reason-impact">
                              延 {{ r.impactHours }}h
                            </span>
                          </li>
                        </ul>
                      </div>

                      <!-- 排程问题 -->
                      <div
                        v-if="explanationReasonsByCategory.SCHEDULING_ISSUE.length > 0"
                        class="explanation-category is-scheduling"
                      >
                        <div class="explanation-category-title">
                          <Icon icon="vi-mdi:chart-timeline" />
                          排程问题（{{ explanationReasonsByCategory.SCHEDULING_ISSUE.length }}）
                          <ElTooltip
                            :content="ISSUE_CATEGORY_DESC.SCHEDULING_ISSUE"
                            placement="top"
                          >
                            <ElIcon class="explanation-hint">
                              <Icon icon="vi-mdi:information-outline" />
                            </ElIcon>
                          </ElTooltip>
                        </div>
                        <ul class="explanation-reason-list">
                          <li
                            v-for="(r, idx) in explanationReasonsByCategory.SCHEDULING_ISSUE"
                            :key="`sched-${idx}`"
                            class="explanation-reason-item"
                          >
                            <Icon
                              :icon="DELAY_REASON_ICON[r.reasonCode]"
                              class="reason-type-icon"
                            />
                            <span class="reason-type-label">
                              {{ DELAY_REASON_LABEL[r.reasonCode] }}
                            </span>
                            <ElTag
                              size="small"
                              :type="SEVERITY_TAG_TYPE[r.severity]"
                              effect="plain"
                            >
                              {{ SEVERITY_LABEL[r.severity] }}
                            </ElTag>
                            <span class="reason-desc">{{ r.description }}</span>
                            <span v-if="r.relatedObjectRef" class="reason-ref">
                              <code class="code-tag">{{ r.relatedObjectRef }}</code>
                            </span>
                            <span v-if="r.impactHours" class="reason-impact">
                              延 {{ r.impactHours }}h
                            </span>
                          </li>
                        </ul>
                      </div>

                      <!-- 仅有 isDelayed 但没具体根因：兜底（提醒不是只有 DUE_DATE_RISK） -->
                      <div
                        v-if="
                          selectedTask.delayReasons?.length === 0 &&
                          selectedTask.isDelayed &&
                          !selectedTask.crossDomainBlocked
                        "
                        class="explanation-no-detail"
                      >
                        <Icon icon="vi-mdi:clock-alert-outline" />
                        当前任务标记为延期，但暂无具体根因（仅 DUE_DATE_RISK 兜底）。
                        可联系系统管理员核查原因数据。
                      </div>
                    </template>

                    <!-- 上游 Stage 阻挡（Section 11 硬约束：前序延迟单列） -->
                    <div v-if="selectedTask.upstreamStageBlocked" class="upstream-stage-blocked">
                      <Icon icon="vi-mdi:arrow-up-bold-circle-outline" />
                      上游 <strong>{{ selectedTask.upstreamStageCode ?? 'STG' }}</strong>
                      完成晚，本任务被前序延迟阻挡。
                    </div>
                  </div>

                  <!-- Section 十六：MES 下发资格 -->
                  <ElAlert
                    :type="MES_ELIGIBLE_TYPE[selectedTask.mesEligible ?? 'UNKNOWN']"
                    :closable="false"
                    show-icon
                    class="mes-eligible-alert"
                  >
                    <template #title>
                      <span class="mes-eligible-title">
                        MES 下发资格：
                        <strong>{{
                          MES_ELIGIBLE_LABEL[selectedTask.mesEligible ?? 'UNKNOWN']
                        }}</strong>
                      </span>
                    </template>
                    <template v-if="selectedTask.mesEligible === 'INELIGIBLE'">
                      <ul class="mes-reason-list">
                        <li v-for="r in selectedTask.mesIneligibleReasons ?? []" :key="r">
                          {{ r }}
                        </li>
                      </ul>
                      <div class="mes-note">页面仅展示与调用下发服务，不直接写 MES 接口表。</div>
                    </template>
                    <template v-else-if="selectedTask.mesEligible === 'ELIGIBLE'">
                      已满足下发条件，可通过 MES Dispatch 服务下发。
                    </template>
                    <template v-else>
                      后端未判定（一般为等待释放窗口），将在释放瞬间更新。
                    </template>
                  </ElAlert>

                  <!-- U14：跨 Domain 共享设备阻挡 -->
                  <ElAlert
                    v-if="selectedTask.crossDomainBlocked"
                    type="error"
                    :closable="false"
                    show-icon
                    class="u14-alert"
                  >
                    <template #title>外排程域共享设备阻挡</template>
                    {{ selectedTask.crossDomainBlockReason ?? '被外排程域占用，未开始加工' }}
                  </ElAlert>

                  <!-- U04：TaskShare 一个 Task 承接多 Order -->
                  <div
                    v-if="selectedTask.taskShare && selectedTask.taskShare.length > 1"
                    class="taskshare-section"
                  >
                    <div class="taskshare-title">
                      <Icon icon="vi-mdi:share-variant" />
                      订单份额 — 一个任务承接 {{ selectedTask.taskShare.length }} 个订单
                    </div>
                    <ElTable :data="selectedTask.taskShare" size="small" border>
                      <ElTableColumn prop="orderNo" label="订单号" width="160">
                        <template #default="scope">
                          <code v-if="scope?.row" class="code-tag">{{ scope.row.orderNo }}</code>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn prop="shareQty" label="份额数量" width="120" align="right">
                        <template #default="scope">
                          <span v-if="scope?.row"
                            >{{ scope.row.shareQty }} {{ selectedTask.uom }}</span
                          >
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="占比" min-width="180">
                        <template #default="scope">
                          <div v-if="scope?.row" class="share-bar-wrap">
                            <div
                              class="share-bar"
                              :style="{
                                width: ((scope.row.shareRatio ?? 0) * 100).toFixed(1) + '%'
                              }"
                            ></div>
                            <span class="share-ratio"
                              >{{ ((scope.row.shareRatio ?? 0) * 100).toFixed(1) }}%</span
                            >
                          </div>
                        </template>
                      </ElTableColumn>
                    </ElTable>
                  </div>
                  <div v-else class="taskshare-section">
                    <span class="muted-text">
                      <Icon icon="vi-mdi:link-variant" /> 单 Order 任务（无 TaskShare）
                    </span>
                  </div>

                  <!-- U06：数量-时间拆分 -->
                  <div
                    v-if="selectedTask.taskSegments && selectedTask.taskSegments.length > 1"
                    class="segment-section"
                  >
                    <div class="segment-title">
                      <Icon icon="vi-mdi:call-split" />
                      数量-时间拆分 — 共 {{ selectedTask.taskSegments.length }} 段
                    </div>
                    <ElTable :data="selectedTask.taskSegments" size="small" border>
                      <ElTableColumn label="段" width="50" align="center">
                        <template #default="scope">
                          <ElTag v-if="scope?.row" size="small" effect="plain"
                            >#{{ scope.row.segmentSeq }}</ElTag
                          >
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="开始时间" width="170">
                        <template #default="scope">
                          <span v-if="scope?.row">{{ scope.row.startTime?.slice(0, 16) }}</span>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="结束时间" width="170">
                        <template #default="scope">
                          <span v-if="scope?.row">{{ scope.row.endTime?.slice(0, 16) }}</span>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="数量" width="110" align="right">
                        <template #default="scope">
                          <span v-if="scope?.row">{{ scope.row.qty }} {{ selectedTask.uom }}</span>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="占比" min-width="160">
                        <template #default="scope">
                          <div v-if="scope?.row" class="share-bar-wrap">
                            <div
                              class="share-bar"
                              :style="{ width: ((scope.row.qtyRatio ?? 0) * 100).toFixed(1) + '%' }"
                            ></div>
                            <span class="share-ratio"
                              >{{ ((scope.row.qtyRatio ?? 0) * 100).toFixed(1) }}%</span
                            >
                          </div>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="原因" min-width="160">
                        <template #default="scope">
                          <span v-if="scope?.row" class="muted-text">{{
                            scope.row.reason ?? '-'
                          }}</span>
                        </template>
                      </ElTableColumn>
                    </ElTable>
                  </div>
                  <div v-else class="segment-section">
                    <span class="muted-text">
                      <Icon icon="vi-mdi:vector-line" /> 单段加工（未拆分）
                    </span>
                  </div>

                  <!-- Section 十二 / U20：设备故障影响（仅展示 + 建议；V1 不建设 PAUSE/RESUME 状态闭环，由 PMC 发起 LOCAL_RESCHEDULE） -->
                  <div
                    v-if="selectedResource?.unavailableWindows?.length"
                    class="resource-down-section"
                  >
                    <div class="resource-down-title">
                      <Icon icon="vi-mdi:tools" />
                      资源不可用窗口（{{ selectedResource.resourceCode }}）
                    </div>
                    <ElTable :data="selectedResource.unavailableWindows" size="small" border>
                      <ElTableColumn label="开始" width="170">
                        <template #default="scope">
                          <span v-if="scope?.row">{{ scope.row.from?.slice(0, 16) }}</span>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="结束" width="170">
                        <template #default="scope">
                          <span v-if="scope?.row">{{ scope.row.to?.slice(0, 16) }}</span>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="原因" min-width="200">
                        <template #default="scope">
                          <span v-if="scope?.row" class="muted-text">{{ scope.row.reason }}</span>
                        </template>
                      </ElTableColumn>
                      <ElTableColumn label="是否影响本任务" width="140" align="center">
                        <template #default="scope">
                          <ElTag
                            v-if="
                              scope?.row &&
                              resourceImpactsSelected.some(
                                (w) => w.from === scope.row.from && w.to === scope.row.to
                              )
                            "
                            type="warning"
                            size="small"
                          >
                            影响
                          </ElTag>
                          <span v-else class="muted-text">-</span>
                        </template>
                      </ElTableColumn>
                    </ElTable>
                    <div class="resource-down-note">
                      <Icon icon="vi-mdi:information-outline" />
                      排产只展示不可用窗口并提供重排入口，不会直接改动任务状态。
                    </div>

                    <!-- P1：基于设备故障建议重排（Section 12 入口）
                         - 仅在有 ≥1 个影响窗口时显示
                         - 走与 P0 同一 runApi.triggerReschedule（runType=LOCAL_RESCHEDULE），但 objectRefType=RESOURCE -->
                    <div
                      v-if="canManualReschedule && resourceImpactsSelected.length > 0"
                      class="resource-down-action"
                    >
                      <ElDivider />
                      <div class="resource-down-action-row">
                        <ElButton type="danger" @click="triggerRescheduleForResource">
                          <Icon icon="vi-mdi:tools" />
                          基于设备故障建议重排（{{ resourceImpactsSelected.length }} 个影响窗口）
                        </ElButton>
                        <ElTooltip
                          content="设备故障只做展示与建议重排，不会直接改动任务状态。提交后由计划员在候选版本对比页确认采用。"
                          placement="top"
                        >
                          <ElIcon class="action-hint">
                            <Icon icon="vi-mdi:information-outline" />
                          </ElIcon>
                        </ElTooltip>
                      </div>
                      <div class="resource-down-action-note">
                        将对资源 <strong>{{ selectedResource.resourceCode }}</strong> 所属排程域
                        (<ElTag size="small" effect="plain">{{ selectedResource.domainKey }}</ElTag
                        >) 发起重排，将重排该资源涉及的任务
                      </div>
                    </div>
                  </div>

                  <!-- 文档第 6 节最后一句 + §10A.2：人工调整单 Task 走「甘特图调整」入口 → Candidate → 确认 -->
                  <div v-if="canPlanRun" class="action-section">
                    <ElDivider />
                    <div class="action-row">
                      <ElButton type="warning" @click="triggerReschedule">
                        <Icon icon="vi-mdi:restart-alert" />
                        发起甘特图调整
                      </ElButton>
                      <ElTooltip
                        content="人工调整不会直接改动当前生效的计划：系统先算出一版候选计划，由计划员对比确认后才采用。整排程域等较大范围入口在工具栏「白天人工调整」下拉。"
                        placement="top"
                      >
                        <ElIcon class="action-hint">
                          <Icon icon="vi-mdi:information-outline" />
                        </ElIcon>
                      </ElTooltip>
                    </div>
                    <div class="action-note">
                      当前账号：<strong>{{
                        apsAuth.userInfo?.userName ?? apsAuth.userInfo?.userCode ?? '-'
                      }}</strong>
                      （角色：<span class="role-tag">{{
                        apsAuth.roles.map(roleLabelOf).join(' / ') || '无'
                      }}</span
                      >） · 触发后会跳到候选版本对比页，由计划员确认后才生效
                    </div>
                  </div>
                  <div v-else class="action-section">
                    <ElDivider />
                    <div class="action-note readonly-note">
                      <Icon icon="vi-mdi:lock-outline" />
                      当前账号没有手工调整计划的权限，无法发起重排。如需调整，请联系计划员操作。
                    </div>
                  </div>
                </div>
              </template>
              <template v-else>
                <div class="detail-empty">请选择任务查看详情</div>
              </template>
            </div>
          </div>
        </div>
      </Transition>
    </Teleport>

    <!-- ===== §10A 白天人工调整：4 个独立业务入口 Dialog =====
         冻结文档《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）：
         LOCAL/MANUAL 候选运行 → 统一出口 triggerBusinessEntry → 跳 Candidate 对比页确认
         （已订单提前 §10A.1 在 Order 页；本页承载 §10A.2-10A.5） -->
    <GanttAdjustDialog
      :visible="entryDialog === 'GANTT_ADJUSTMENT'"
      :submitting="entrySubmitting"
      :domain-options="BUSINESS_DOMAIN_OPTIONS"
      :tasks="filteredTasks"
      :preselected-task-ids="entryPreselectTaskIds"
      :preselect-target-time="entryPreselectTargetTime"
      :default-domain-key="entryDomainKey"
      @update:visible="(v) => (entryDialog = v ? 'GANTT_ADJUSTMENT' : null)"
      @submit="submitBusinessEntry"
    />

    <ResourceRescheduleDialog
      :visible="entryDialog === 'EQUIPMENT_FAILURE'"
      :submitting="entrySubmitting"
      trigger="EQUIPMENT_FAILURE"
      :domain-options="BUSINESS_DOMAIN_OPTIONS"
      :resources="ganttData?.resources ?? []"
      :preselected-resource-ids="entryPreselectResourceIds"
      :default-domain-key="entryDomainKey"
      @update:visible="(v) => (entryDialog = v ? 'EQUIPMENT_FAILURE' : null)"
      @submit="submitBusinessEntry"
    />

    <ResourceRescheduleDialog
      :visible="entryDialog === 'RESOURCE_CALENDAR_CHANGE'"
      :submitting="entrySubmitting"
      trigger="RESOURCE_CALENDAR_CHANGE"
      :domain-options="BUSINESS_DOMAIN_OPTIONS"
      :resources="ganttData?.resources ?? []"
      :preselected-resource-ids="entryPreselectResourceIds"
      :default-domain-key="entryDomainKey"
      @update:visible="(v) => (entryDialog = v ? 'RESOURCE_CALENDAR_CHANGE' : null)"
      @submit="submitBusinessEntry"
    />

    <DomainRescheduleDialog
      :visible="entryDialog === 'DOMAIN_MANUAL_RESCHEDULE'"
      :submitting="entrySubmitting"
      :domain-options="BUSINESS_DOMAIN_OPTIONS"
      :default-domain-key="entryDomainKey"
      @update:visible="(v) => (entryDialog = v ? 'DOMAIN_MANUAL_RESCHEDULE' : null)"
      @submit="submitBusinessEntry"
    />
  </div>
</template>

<style lang="less" scoped>
.aps-gantt-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 140px);
  padding: 0;
  overflow: hidden;
  background: #f5f7fa;
  gap: 12px;
}

/* 工具栏 */
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);
  flex-wrap: nowrap;
  gap: 8px;

  .toolbar-left {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
    flex: 1;
    min-width: 0;
  }

  .toolbar-right {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .label {
    font-size: 12px;
    color: #64748b;
  }

  .stat {
    margin-left: 8px;
    font-size: 11px;
    color: #94a3b8;
    white-space: nowrap;

    strong {
      color: #1e293b;
    }

    .stat-domain {
      font-weight: 500;

      strong {
        font-family: 'Courier New', monospace;
        font-weight: 700;
      }
    }

    /* P5 §12：受影响 KPI 项（点切换只看受影响） */
    .stat-impacted {
      display: inline-flex;
      padding: 1px 8px;
      margin-left: 8px;
      font-size: 12px;
      color: #dc2626;
      cursor: pointer;
      background: #fef2f2;
      border: 1px solid #fecaca;
      border-radius: 10px;
      transition: all 0.12s ease;
      align-items: center;
      gap: 2px;

      strong {
        margin-left: 2px;
        font-family: 'Courier New', monospace;
        font-weight: 700;
      }

      &:hover {
        background: #fee2e2;
        border-color: #fca5a5;
      }

      &.is-active {
        color: #fff;
        background: #dc2626;
        border-color: #dc2626;

        strong {
          color: #fff;
        }
      }
    }
  }
}

/* P5 §12 Tooltip 内容 */
.impacted-tooltip {
  max-width: 280px;
  font-size: 12px;
  line-height: 1.6;
}

.impacted-tooltip-title {
  margin-bottom: 4px;
  font-weight: 600;
  color: #1e293b;
}

.impacted-tooltip-list {
  padding: 0;
  margin: 0;
  list-style: none;

  li {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 1px 0;

    code {
      padding: 0 4px;
      font-family: 'Courier New', monospace;
      font-size: 11px;
      color: #dc2626;
      background: #fef2f2;
      border-radius: 3px;
    }
  }
}

.impacted-tooltip-res {
  font-size: 11px;
  color: #64748b;
}

.impacted-tooltip-more {
  margin-top: 4px;
  font-style: italic;
  color: #94a3b8;
}

/* 甘特图区 */
.gantt-area {
  flex: 1;
  min-height: 0;
  overflow: hidden;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);

  :deep(.dhx-gantt-root) {
    height: 100%;
  }
}

/* 提示条 */
.hint-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  font-size: 12px;
  color: #64748b;
  background: #fff;
  border-radius: 6px;

  .hint-icon {
    color: #94a3b8;
  }
}

/* 详情 */
.detail-section {
  display: flex;
  padding: 0 4px;
  flex-direction: column;
  gap: 14px;
}

.detail-empty {
  padding: 40px 0;
  color: #94a3b8;
  text-align: center;
}

/* U05：净需求 vs 计划加工 */
.qty-cell {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}

.qty-net {
  color: #94a3b8;

  strong {
    font-weight: 600;
    color: #475569;
  }
}

.qty-arrow {
  font-weight: 600;
  color: #cbd5e1;
}

.qty-plan {
  color: #1e293b;

  strong {
    font-weight: 700;
    color: #1e293b;
  }
}

/* U14：跨 Domain 阻挡告警 */
.u14-alert {
  margin-top: 4px;
}

/* U04：TaskShare */
.taskshare-section {
  padding: 12px;
  margin-top: 4px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

.taskshare-title {
  display: flex;
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 600;
  color: #1e293b;
  align-items: center;
  gap: 6px;
}

/* U06：数量-时间拆分 */
.segment-section {
  padding: 12px;
  margin-top: 4px;
  background: #fef3c7;
  border: 1px solid #fde68a;
  border-radius: 6px;
}

.segment-title {
  display: flex;
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 600;
  color: #92400e;
  align-items: center;
  gap: 6px;
}

/* Section 16：MES 下发资格 */
.mes-eligible-alert {
  margin-top: 4px;
}

/* Section 11：异常与原因解释 — 根因展示 */
.explanation-section {
  padding: 12px;
  background: linear-gradient(180deg, #fefce8 0%, #fef9c3 100%);
  border: 1px solid #fde68a;
  border-radius: 6px;
}

.explanation-title {
  display: flex;
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 600;
  color: #92400e;
  align-items: center;
  gap: 6px;

  .explanation-hint {
    font-size: 14px;
    color: #94a3b8;
    cursor: help;
  }
}

.explanation-empty {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #059669;
}

.explanation-category {
  padding: 8px 10px;
  margin-top: 8px;
  background: #fff;
  border-left: 3px solid #94a3b8;
  border-radius: 4px;
}

.explanation-category.is-data {
  border-left-color: #d97706;
}

.explanation-category.is-scheduling {
  border-left-color: #6366f1;
}

.explanation-category-title {
  display: flex;
  margin-bottom: 6px;
  font-size: 12px;
  font-weight: 600;
  color: #1e293b;
  align-items: center;
  gap: 6px;

  .explanation-hint {
    font-size: 13px;
    color: #94a3b8;
    cursor: help;
  }
}

.explanation-reason-list {
  padding: 0;
  margin: 0;
  list-style: none;
}

.explanation-reason-item {
  display: flex;
  padding: 4px 0;
  font-size: 12px;
  line-height: 1.5;
  color: #1e293b;
  border-bottom: 1px dashed #fde68a;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;

  &:last-child {
    border-bottom: none;
  }

  .reason-type-icon {
    font-size: 14px;
    color: #6366f1;
  }

  .reason-type-label {
    font-weight: 600;
  }

  .reason-desc {
    padding-left: 22px;
    font-size: 11px;
    color: #475569;
    flex-basis: 100%;
  }

  .reason-ref {
    margin-left: auto;
    font-size: 11px;
    color: #64748b;
  }

  .reason-impact {
    padding: 0 6px;
    font-size: 11px;
    font-weight: 600;
    color: #92400e;
    background: #fef3c7;
    border-radius: 3px;
  }
}

.explanation-no-detail {
  display: flex;
  padding: 8px 10px;
  margin-top: 8px;
  font-size: 12px;
  line-height: 1.5;
  color: #92400e;
  background: #fff7ed;
  border: 1px dashed #fdba74;
  border-radius: 4px;
  align-items: flex-start;
  gap: 6px;
}

.upstream-stage-blocked {
  display: flex;
  padding: 6px 10px;
  margin-top: 8px;
  font-size: 12px;
  color: #991b1b;
  background: #fee2e2;
  border: 1px dashed #fca5a5;
  border-radius: 4px;
  align-items: center;
  gap: 6px;

  strong {
    font-family: 'Courier New', monospace;
    font-weight: 700;
  }
}

.mes-eligible-title {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;

  strong {
    font-weight: 700;
  }
}

.mes-reason-list {
  padding-left: 20px;
  margin: 4px 0 0;
  font-size: 12px;
  color: #7f1d1d;

  li {
    margin: 2px 0;
  }
}

.mes-note {
  margin-top: 4px;
  font-size: 11px;
  color: #94a3b8;
}

/* Section 12 / U20：资源故障影响 */
.resource-down-section {
  padding: 12px;
  margin-top: 4px;
  background: #fee2e2;
  border: 1px solid #fecaca;
  border-radius: 6px;
}

.resource-down-title {
  display: flex;
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 600;
  color: #991b1b;
  align-items: center;
  gap: 6px;
}

.resource-down-note {
  display: inline-flex;
  margin-top: 8px;
  font-size: 11px;
  color: #7f1d1d;
  align-items: center;
  gap: 4px;
}

/* P1：设备故障建议重排 - 操作区（红色调，与上方故障区视觉一致） */
.resource-down-action {
  padding-top: 4px;
  margin-top: 10px;
}

.resource-down-action-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.resource-down-action-note {
  margin-top: 6px;
  font-size: 11px;
  line-height: 1.6;
  color: #7f1d1d;

  strong {
    font-weight: 700;
    color: #991b1b;
  }
}

/* Section 6 + Section 12 + Section 17 + 审核报告 P0-02/P0-03：LOCAL_RESCHEDULE 操作区 */
.action-section {
  padding-top: 4px;
  margin-top: 8px;
}

.action-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.action-hint {
  font-size: 16px;
  color: #94a3b8;
  cursor: help;
}

.action-note {
  margin-top: 6px;
  font-size: 11px;
  line-height: 1.6;
  color: #64748b;

  strong {
    color: #1e293b;
  }
}

.role-tag {
  display: inline-block;
  padding: 1px 6px;
  margin: 0 2px;
  font-family: 'Courier New', monospace;
  font-size: 11px;
  font-weight: 600;
  color: #3730a3;
  background: #e0e7ff;
  border-radius: 8px;
}

/* P2 角色徽章（P2 mock 角色切换器） */
.role-badge {
  display: inline-flex;
  padding: 4px 10px;
  font-size: 12px;
  letter-spacing: 0.5px;
  cursor: pointer;
  transition: transform 0.15s ease;
  align-items: center;
  gap: 4px;

  &:hover {
    transform: translateY(-1px);
  }
}

.role-caret {
  font-size: 10px;
  opacity: 0.7;
}

.role-hint {
  margin-left: 8px;
  font-size: 11px;
  color: #94a3b8;
}

.readonly-note {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: #94a3b8;
}

.muted-text {
  display: inline-flex;
  font-size: 12px;
  color: #94a3b8;
  align-items: center;
  gap: 4px;
}

.share-bar-wrap {
  position: relative;
  display: flex;
  height: 18px;
  overflow: hidden;
  background: #e2e8f0;
  border-radius: 4px;
  align-items: center;
  gap: 8px;
}

.share-bar {
  height: 100%;
  background: linear-gradient(90deg, #3b82f6, #6366f1);
  border-radius: 4px;
}

.share-ratio {
  position: absolute;
  right: 8px;
  font-family: 'Courier New', monospace;
  font-size: 11px;
  font-weight: 600;
  color: #1e293b;
}

.code-tag {
  padding: 2px 6px;
  font-family: 'Courier New', monospace;
  font-size: 12px;
  color: #475569;
  background: #f1f5f9;
  border-radius: 3px;
}

/* 甘特任务条：U04 / U14 / U06 视觉标记 */
:deep(.gantt_task_line) {
  /* P4 软拖动：PMC+ 角色下任务条可拖，鼠标变 grab 提示 */
  cursor: grab;

  &:active {
    cursor: grabbing;
  }

  &.share-marked {
    border: 2px dashed #6366f1 !important;
    box-shadow: inset 0 0 0 2px rgb(99 102 241 / 15%);
  }

  &.domain-blocked {
    background-image: repeating-linear-gradient(
      45deg,
      transparent,
      transparent 4px,
      rgb(255 255 255 / 35%) 4px,
      rgb(255 255 255 / 35%) 8px
    ) !important;
    border: 2px solid #ef4444 !important;
    box-shadow: 0 0 0 1px #ef4444;
  }

  &.segment-bar {
    border: 1px dashed rgb(0 0 0 / 40%) !important;
    border-top-style: solid;
    border-bottom-style: solid;
    opacity: 0.85;
    box-shadow: inset 0 0 0 1px rgb(255 255 255 / 25%);
  }
}

:deep(.gantt_marker.cross_domain_block) {
  color: #fff !important;
  background: #ef4444 !important;
}

/* ===== P3 重写：自实现 Drawer（避开 Element Plus append-to-body + slot click bug） ===== */

/* mask：半透明视觉遮罩，但 pointer-events: none 让 dblclick 穿透到任务条
 * 用户在 mask 区域双击任务条会先关 drawer 再出 popover（handleAreaDblClick 已实现） */
.aps-task-drawer-mask {
  position: fixed;
  z-index: 2999;
  display: flex;
  pointer-events: none;
  background: rgb(15 23 42 / 18%);
  inset: 0;
  justify-content: flex-end;
}

/* panel：右侧抽屉本体，绝对不会被全局 Header 遮挡 */
.aps-task-drawer {
  position: relative;
  display: flex;
  width: 420px;
  height: 100%;
  max-width: 90vw;
  overflow: hidden;
  pointer-events: auto;
  background: #fff;
  box-shadow: -4px 0 24px rgb(15 23 42 / 18%);
  flex-direction: column;
}

/* 顶部 header：自渲染，绝对不会被全局 Header 遮挡 */
.aps-drawer-header {
  display: flex;
  width: 100%;
  padding: 14px 16px;
  background: #fff;
  border-bottom: 1px solid #e2e8f0;
  flex-shrink: 0;
  align-items: center;
  justify-content: space-between;
}

.aps-drawer-title {
  display: flex;
  min-width: 0;
  font-size: 15px;
  font-weight: 600;
  color: #1e293b;
  align-items: center;
  gap: 8px;
  flex: 1;
}

.aps-drawer-title-icon {
  color: #2563eb;
  flex-shrink: 0;
}

.aps-drawer-taskno {
  margin-left: 4px;
  font-family: 'Courier New', monospace;
}

/* 主体：可滚动 */
.aps-drawer-body {
  min-height: 0;
  padding: 16px;
  overflow-y: auto;
  flex: 1;
}

/* 自实现关闭按钮：原生 <button>，避免 Element Plus ElButton 的 click 事件丢失 */
.aps-drawer-close-btn {
  display: inline-flex;
  padding: 5px 12px;
  font-family: inherit;
  font-size: 13px;
  font-weight: 500;
  color: #1e293b;
  cursor: pointer;
  background: #fff;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  transition: all 0.15s ease;
  align-items: center;
  gap: 4px;

  &:hover {
    background: #f1f5f9;
    border-color: #94a3b8;
  }

  &:active {
    background: #e2e8f0;
  }
}

/* 抽屉滑入动画 */
.aps-drawer-slide-enter-active,
.aps-drawer-slide-leave-active {
  transition: opacity 0.22s ease;
}

.aps-drawer-slide-enter-active .aps-task-drawer,
.aps-drawer-slide-leave-active .aps-task-drawer {
  transition: transform 0.22s ease;
}

.aps-drawer-slide-enter-from,
.aps-drawer-slide-leave-to {
  opacity: 0;
}

.aps-drawer-slide-enter-from .aps-task-drawer,
.aps-drawer-slide-leave-to .aps-task-drawer {
  transform: translateX(100%);
}

/* ===== P3：双击任务条 → 轻量浮窗 ===== */
.aps-floating-popover {
  position: fixed;
  z-index: 3000;
  max-width: 360px;
  min-width: 280px;
  padding: 12px 14px;
  font-size: 13px;
  color: #1e293b;
  background: #fff;
  border-radius: 6px;
  box-shadow:
    0 4px 16px rgb(15 23 42 / 18%),
    0 0 0 1px rgb(15 23 42 / 6%);
  user-select: none;
}

.popover-arrow {
  position: absolute;
  top: -6px;
  left: 16px;
  width: 12px;
  height: 12px;
  background: #fff;
  transform: rotate(45deg);
  box-shadow: -1px -1px 1px rgb(15 23 42 / 4%);
}

.popover-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}

.popover-icon {
  color: #2563eb;
}

.popover-taskno {
  font-family: 'Courier New', monospace;
  color: #1e293b;
}

.popover-divider {
  margin: 8px 0 !important;
}

.popover-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 4px 0;
  line-height: 1.6;
}

.row-label {
  flex-shrink: 0;
  width: 80px;
  font-size: 12px;
  color: #64748b;
}

.row-val {
  font-size: 13px;
  color: #1e293b;
}

.popover-actions {
  display: flex;
  gap: 8px;
  margin-top: 4px;
}

/* ===== P3：右键任务条 → 完整菜单 ===== */
.aps-ctx-menu {
  position: fixed;
  z-index: 3000;
  min-width: 220px;
  overflow: hidden;
  font-size: 13px;
  background: #fff;
  border-radius: 6px;
  box-shadow:
    0 4px 16px rgb(15 23 42 / 18%),
    0 0 0 1px rgb(15 23 42 / 6%);
  user-select: none;
}

.ctx-menu-header {
  display: flex;
  padding: 8px 14px;
  font-family: 'Courier New', monospace;
  font-weight: 600;
  color: #1e293b;
  background: #f1f5f9;
  border-bottom: 1px solid #e2e8f0;
  align-items: center;
  gap: 6px;
}

.ctx-menu-list {
  padding: 4px 0;
  margin: 0;
  list-style: none;
}

.ctx-menu-item {
  display: flex;
  padding: 8px 14px;
  color: #1e293b;
  cursor: pointer;
  transition: background-color 0.12s ease;
  align-items: center;
  gap: 8px;

  &:hover {
    background: #f1f5f9;
  }

  &.is-disabled {
    color: #94a3b8;
    cursor: not-allowed;

    &:hover {
      background: transparent;
    }
  }
}

.ctx-menu-code {
  padding: 1px 6px;
  margin-left: auto;
  font-family: 'Courier New', monospace;
  font-size: 11px;
  color: #3730a3;
  background: #e0e7ff;
  border-radius: 4px;
}

.ctx-menu-empty {
  margin-left: auto;
  color: #cbd5e1;
}

.ctx-menu-arrow {
  margin-left: auto;
  color: #94a3b8;
}

/* ===== §10A 入口 Dialog 已抽为独立组件（GanttAdjustDialog / ResourceRescheduleDialog /
   DomainRescheduleDialog），此处不再保留 Drawer 样式 ===== */
</style>
