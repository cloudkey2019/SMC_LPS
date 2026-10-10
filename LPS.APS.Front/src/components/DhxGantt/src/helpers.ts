import dayjs from 'dayjs'
import type { ApsLane, ApsTask } from '@/api/aps/types'
import type { DhxGanttPayload, DhxGanttTask } from './types'

const DISPLAY_DATE = 'YYYY-MM-DD HH:mm'

const hoursBetween = (start: string, end: string) => {
  const diff = dayjs(end).diff(dayjs(start), 'hour', true)
  return Math.max(1, Math.round(diff || 1))
}

const pickTaskColor = (task: ApsTask) => {
  // 注意：running 状态不使用 color，让 CSS 的 .dhx-status-running 处理紫色背景
  // 否则 inline style 会覆盖 CSS class 的 !important 效果
  if (task.status === 'running') return undefined
  if (task.status === 'delayed') return '#be123c'
  if (task.status === 'completed') return '#475569'
  if (task.status === 'material_shortage') return '#d97706'
  if (task.status === 'cancelled') return '#64748b'
  return '#2563eb'
}

const pickTextColor = (task: ApsTask) =>
  task.status === 'material_shortage' ? '#1f2937' : '#ffffff'

const pickExceptionSeverity = (task: ApsTask): DhxGanttTask['exceptionSeverity'] => {
  if (task.status === 'delayed' || task.status === 'material_shortage' || task.delayedHours >= 4)
    return 'high'
  if (task.reasonCode === 'LOGISTICS_DELAY' || task.executionLockFlag || task.warnings.length >= 2)
    return 'medium'
  if (task.warnings.length || task.isLocked) return 'low'
  return 'none'
}

const pickLockSource = (task: ApsTask): DhxGanttTask['lockSource'] => {
  const flags = [task.planFreezeFlag, task.executionLockFlag, Boolean(task.isLocked)].filter(
    Boolean
  ).length
  if (flags > 1) return 'mixed'
  if (task.executionLockFlag) return 'execution'
  if (task.planFreezeFlag) return 'plan_freeze'
  if (task.isLocked) return 'manual'
  return 'none'
}

export const formatDhxDate = (value: string | Date) => dayjs(value).format(DISPLAY_DATE)

const buildFreezeBoundaryMap = (tasks: ApsTask[], lanes: ApsLane[]) => {
  return new Map(
    lanes.map((lane) => {
      const laneTasks = tasks
        .filter((task) => task.laneId === lane.id)
        .sort((a, b) => dayjs(a.start).valueOf() - dayjs(b.start).valueOf())
      const freezeTasks = laneTasks.filter((task) => task.zone !== 'open')
      const firstOpenTask = laneTasks.find((task) => task.zone === 'open')
      const boundary = freezeTasks.length
        ? freezeTasks.reduce(
            (latest, task) => (dayjs(task.end).isAfter(dayjs(latest)) ? task.end : latest),
            freezeTasks[0].end
          )
        : firstOpenTask?.start
      return [lane.id, boundary]
    })
  )
}

export const buildDhxGanttPayload = (tasks: ApsTask[], lanes: ApsLane[]): DhxGanttPayload => {
  const laneMap = new Map(lanes.map((lane) => [lane.id, lane]))
  const freezeBoundaryMap = buildFreezeBoundaryMap(tasks, lanes)

  const laneGroups: DhxGanttTask[] = lanes.map((lane) => {
    const laneTasks = tasks.filter((task) => task.laneId === lane.id)
    const firstTask = laneTasks[0]
    return {
      id: lane.id,
      text: `${lane.code || lane.id} | ${lane.name}`,
      start_date: firstTask ? formatDhxDate(firstTask.start) : formatDhxDate(new Date()),
      duration: firstTask ? hoursBetween(firstTask.start, firstTask.end) : 1,
      type: 'project',
      open: true,
      readonly: true,
      laneId: lane.id,
      details: `${lane.factory} / ${lane.resourceGroup}`,
      warningCount: lane.exceptionCount,
      status: lane.status,
      laneStatus: lane.status,
      zone: lane.frozenTaskCount ? 'frozen' : 'open',
      freezeBoundary: freezeBoundaryMap.get(lane.id),
      exceptionSeverity:
        lane.exceptionCount >= 3 ? 'high' : lane.exceptionCount >= 1 ? 'medium' : 'none'
    }
  })

  const taskRows: DhxGanttTask[] = tasks.map((task) => {
    const lane = laneMap.get(task.laneId)
    const readonly = false
    return {
      id: task.id,
      text: `${task.taskCode} | ${task.orderId}`,
      start_date: formatDhxDate(task.start),
      duration: hoursBetween(task.start, task.end),
      progress: task.progress,
      actualStart: task.actualStart,
      actualEnd: task.actualEnd,
      parent: task.laneId,
      open: true,
      readonly,
      color: pickTaskColor(task),
      textColor: pickTextColor(task),
      orderId: task.orderId,
      materialName: task.materialName,
      laneId: task.laneId,
      status: task.status,
      details: `${task.materialName} / ${lane?.code || task.laneId}`,
      changeReason: task.changeReason,
      warningCount: task.warnings.length,
      isCritical: task.isCritical,
      zone: task.zone,
      reasonCode: task.reasonCode,
      delayedHours: task.delayedHours,
      planFreezeFlag: task.planFreezeFlag,
      executionLockFlag: task.executionLockFlag,
      isLocked: Boolean(task.isLocked),
      laneStatus: lane?.status,
      freezeBoundary: freezeBoundaryMap.get(task.laneId),
      exceptionSeverity: task.exceptionSeverity || pickExceptionSeverity(task),
      rescheduleState: task.rescheduleState || 'none',
      unfreezeRequestStatus: task.unfreezeRequestStatus || 'none',
      lockSource: task.lockSource || pickLockSource(task)
    }
  })

  return {
    data: [...laneGroups, ...taskRows],
    links: []
  }
}
