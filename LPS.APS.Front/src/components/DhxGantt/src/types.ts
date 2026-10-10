export type DhxGanttScaleUnit = 'day' | 'week' | 'month' | 'hour'

export interface DhxGanttTask {
  id: string | number
  text: string
  start_date: string
  duration: number
  progress?: number
  actualStart?: string
  actualEnd?: string
  parent?: string | number
  type?: 'task' | 'project'
  open?: boolean
  readonly?: boolean
  color?: string
  textColor?: string
  orderId?: string
  materialName?: string
  laneId?: string
  status?: string
  details?: string
  changeReason?: string
  warningCount?: number
  isCritical?: boolean
  zone?: 'frozen' | 'firm' | 'open'
  reasonCode?: string
  delayedHours?: number
  planFreezeFlag?: boolean
  executionLockFlag?: boolean
  isLocked?: boolean
  lockSource?: 'none' | 'plan_freeze' | 'execution' | 'manual' | 'mixed'
  exceptionSeverity?: 'none' | 'low' | 'medium' | 'high'
  laneStatus?: 'normal' | 'maintenance' | 'overloaded'
  freezeBoundary?: string
}

export interface DhxGanttCompareTask {
  taskId: string | number
  laneId?: string
  taskCode?: string
  orderId?: string
  materialName?: string
  start?: string
  end?: string
  baselineLaneId?: string
  baselineStart?: string
  baselineEnd?: string
  status?: string
  isCritical?: boolean
  driftHours?: number
  changedFields?: string[]
}
export interface DhxGanttLink {
  id: string | number
  source: string | number
  target: string | number
  type: string | number
}

export interface DhxGanttPayload {
  data: DhxGanttTask[]
  links?: DhxGanttLink[]
}

export interface DhxGanttDragStartContext {
  taskId: string | number
  task: DhxGanttTask
  mode: string
}

export interface DhxGanttDragPayload {
  taskId: string | number
  task: DhxGanttTask
  sourceTask: DhxGanttTask
  mode: string
  sourceLaneId?: string | number
  targetLaneId?: string | number
  targetStart: string
  targetEnd: string
}

export type DhxGanttDropValidationPayload = DhxGanttDragPayload

export interface DhxGanttDragRejectedPayload {
  taskId: string | number
  task: DhxGanttTask
  reason: string
}
