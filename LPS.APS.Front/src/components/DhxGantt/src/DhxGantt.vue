<script setup lang="ts">
import '@vendor/dhtmlx-gantt/codebase/dhtmlxgantt.css'
import gantt from '@vendor/dhtmlx-gantt/codebase/dhtmlxgantt.es.js'
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type {
  DhxGanttCompareTask,
  DhxGanttDragPayload,
  DhxGanttDragRejectedPayload,
  DhxGanttDragStartContext,
  DhxGanttDropValidationPayload,
  DhxGanttPayload,
  DhxGanttScaleUnit,
  DhxGanttTask
} from './types'

const ganttApi = gantt as any

const props = withDefaults(
  defineProps<{
    tasks: DhxGanttTask[]
    links?: DhxGanttPayload['links']
    compareTasks?: DhxGanttCompareTask[]
    showCompare?: boolean
    showActual?: boolean
    highlightTaskIds?: Array<string | number>
    highlightLaneIds?: Array<string | number>
    readonly?: boolean
    allowResize?: boolean
    allowProgress?: boolean
    rowHeight?: number
    scaleUnit?: DhxGanttScaleUnit
    dateFrom?: string
    dateTo?: string
    beforeTaskDrag?: (context: DhxGanttDragStartContext) => boolean | string
    validateTaskDrop?: (payload: DhxGanttDropValidationPayload) => boolean | string
  }>(),
  {
    links: () => [],
    compareTasks: () => [],
    showCompare: true,
    showActual: true,
    highlightTaskIds: () => [],
    highlightLaneIds: () => [],
    readonly: false,
    allowResize: true,
    allowProgress: true,
    rowHeight: 42,
    scaleUnit: 'day',
    dateFrom: undefined,
    dateTo: undefined,
    beforeTaskDrag: undefined,
    validateTaskDrop: undefined
  }
)
const emits = defineEmits<{
  taskClick: [taskId: string | number]
  taskDblClick: [taskId: string | number, e?: MouseEvent]
  taskDragEnd: [payload: DhxGanttDragPayload]
  taskDragRejected: [payload: DhxGanttDragRejectedPayload]
  taskContextMenu: [taskId: string | number, event: MouseEvent]
}>()

const rootRef = ref<HTMLDivElement | null>(null)
let initialized = false
let eventIds: string[] = []
let compareLayerId: string | number | null = null
let actualLayerId: string | number | null = null
let laneOverlayLayerId: string | number | null = null
let progressLayerId: string | number | null = null
const dragSnapshots = new Map<string | number, DhxGanttTask>()
const formatDate = gantt.date.date_to_str('%Y-%m-%d %H:%i')

const cloneTask = (task: any): DhxGanttTask => JSON.parse(JSON.stringify(task)) as DhxGanttTask
const compareTaskMap = () => new Map(props.compareTasks.map((item) => [String(item.taskId), item]))
const taskMap = () => new Map(props.tasks.map((item) => [String(item.id), item]))
const highlightTaskIdSet = () => new Set(props.highlightTaskIds.map((item) => String(item)))
const highlightLaneIdSet = () => new Set(props.highlightLaneIds.map((item) => String(item)))

const forceClearDragState = () => {
  const timeline = ganttApi.$ui?.getView?.('timeline') as any
  const tasksDnd = timeline?._tasks_dnd as any
  if (tasksDnd?.clear_drag_state) tasksDnd.clear_drag_state()
  if (ganttApi.unselectTask) ganttApi.unselectTask()
  ganttApi.render?.()
}
const parseShadowDate = (value?: string) => {
  if (!value) return null
  const parsed = new Date(value)
  if (!Number.isNaN(parsed.getTime())) return parsed
  try {
    return ganttApi.date.parseDate(value, ganttApi.config.xml_date)
  } catch {
    return null
  }
}

const buildDragPayload = (id: string | number, mode: string): DhxGanttDragPayload => {
  const task = cloneTask(gantt.getTask(id))
  const sourceTask = dragSnapshots.get(id) || task
  const taskStartDate = parseShadowDate(String(task.start_date)) || new Date(task.start_date)
  const taskEndDate = gantt.calculateEndDate(taskStartDate as any, task.duration)
  return {
    taskId: id,
    task,
    sourceTask,
    mode,
    sourceLaneId: sourceTask.parent,
    targetLaneId: task.parent,
    targetStart: formatDate(taskStartDate),
    targetEnd: formatDate(taskEndDate)
  }
}

const applyScaleConfig = (scaleUnit: DhxGanttScaleUnit) => {
  // 设置时间轴刻度
  if (scaleUnit === 'hour') {
    gantt.config.scales = [
      { unit: 'day', step: 1, format: '%m-%d' },
      { unit: 'hour', step: 1, format: '%H:%i' }
    ]
  } else if (scaleUnit === 'day') {
    gantt.config.scales = [{ unit: 'day', step: 1, format: '%m-%d' }]
  } else if (scaleUnit === 'week') {
    gantt.config.scales = [
      { unit: 'week', step: 1, format: '第%W周' },
      { unit: 'day', step: 1, format: '%m-%d' }
    ]
  } else if (scaleUnit === 'month') {
    gantt.config.scales = [
      { unit: 'month', step: 1, format: '%Y-%m' },
      { unit: 'day', step: 1, format: '%d' }
    ]
  }

  // 设置日期范围：优先使用外部传入的值，否则根据 scaleUnit 设置默认值
  if (props.dateFrom && props.dateTo) {
    gantt.config.start_date = new Date(props.dateFrom)
    gantt.config.end_date = new Date(props.dateTo)
  } else {
    // 默认时间范围：今天开始
    const now = new Date()
    now.setHours(0, 0, 0, 0)
    let rangeEnd = new Date(now)
    rangeEnd.setDate(rangeEnd.getDate() + 7) // 默认7天

    if (scaleUnit === 'hour') {
      rangeEnd = new Date(now)
      rangeEnd.setDate(rangeEnd.getDate() + 1) // 今日：1天
    } else if (scaleUnit === 'day') {
      rangeEnd = new Date(now)
      rangeEnd.setDate(rangeEnd.getDate() + 7) // 7天
    } else if (scaleUnit === 'week') {
      rangeEnd = new Date(now)
      rangeEnd.setDate(rangeEnd.getDate() + 30) // 30天
    } else if (scaleUnit === 'month') {
      rangeEnd = new Date(now)
      rangeEnd.setMonth(rangeEnd.getMonth() + 3) // 3个月
    }

    gantt.config.start_date = now
    gantt.config.end_date = rangeEnd
  }
}

const applyConfig = () => {
  gantt.config.readonly = props.readonly
  gantt.config.row_height = props.rowHeight
  gantt.config.xml_date = '%Y-%m-%d %H:%i'
  gantt.config.date_format = '%Y-%m-%d %H:%i'
  // 任务 duration 统一按「小时」传入（页面 hoursBetween 返回小时），
  // vendor 默认单位是 day，会把 4 小时画成 4 天（条宽放大 24 倍）
  gantt.config.duration_unit = 'hour'
  gantt.config.duration_step = 1
  gantt.config.drag_resize = !props.readonly && props.allowResize
  gantt.config.drag_move = !props.readonly
  gantt.config.drag_progress = !props.readonly && props.allowProgress
  gantt.config.open_tree_initially = true
  gantt.config.grid_width = 380
  gantt.config.bar_height = 28
  gantt.config.grid_resize = true
  gantt.config.show_progress = false // 禁用内置进度条，使用自定义进度显示
  // 禁用原生右键菜单，使用自定义菜单
  gantt.config.context_menu = false
  // 启用虚拟渲染，提升大量数据时的渲染性能
  gantt.config.virtual_render = true
  gantt.config.lazy_parsing = true
  // 防止自动滚动
  gantt.config.scroll_to_task = false
  gantt.config.auto_scheduling = false
  // 禁用 showEvent 防止滚动
  if ((gantt as any).showEvent) {
    ;(gantt as any).showEvent = function () {}
  }
  gantt.config.columns = [
    { name: 'text', label: '任务 / 订单', tree: true, width: 180 },
    { name: 'materialName', label: '物料', align: 'left', width: 110 },
    { name: 'start_date', label: '开始', align: 'center', width: 140 },
    { name: 'duration', label: '时长(h)', align: 'center', width: 80 }
  ]

  applyScaleConfig(props.scaleUnit)

  gantt.templates.task_class = (_, __, task) => {
    const classes: string[] = []
    if (task.type === 'project') classes.push('dhx-task-lane')
    if (task.readonly) classes.push('dhx-task-readonly')
    if (highlightTaskIdSet().has(String(task.id))) classes.push('dhx-task-impacted')
    if (task.type === 'project' && highlightLaneIdSet().has(String(task.id)))
      classes.push('dhx-lane-impacted')
    if (task.isCritical) classes.push('dhx-task-critical')
    if (task.warningCount) classes.push('dhx-task-warning')
    // APS 状态枚举为大写（COMPLETED），样式选择器为小写，需统一后再拼类名
    if (task.status) classes.push(`dhx-status-${String(task.status).toLowerCase()}`)
    if (task.zone) classes.push(`dhx-zone-${task.zone}`)
    if (task.reasonCode === 'LOGISTICS_DELAY') classes.push('dhx-reason-logistics-delay')
    if (task.planFreezeFlag) classes.push('dhx-task-frozen')
    if (task.executionLockFlag) classes.push('dhx-task-execution-locked')
    if (task.isLocked) classes.push('dhx-task-manual-locked')
    if (task.laneStatus === 'maintenance') classes.push('dhx-task-on-maintenance-lane')
    return classes.join(' ')
  }

  // 任务条本身不写字：条宽按真实工时绘制（4 小时 ≈ 1/6 个日列），
  // 放不下「任务号 | 订单号」，且 .gantt_task_content 是 overflow:visible，文字会溢出盖到相邻条上。
  // 这些信息左侧表格列同源展示，悬浮 tooltip / 双击浮窗另有详情。
  // 泳道（project）行跨度大，保留名称。
  gantt.templates.task_text = (_, __, task) => {
    if (task.type === 'project') return task.text || ''
    return ''
  }

  gantt.templates.tooltip_text = (_, __, task) => {
    const lines = [`任务：${task.text || '-'}`, `物料：${task.materialName || '-'}`]
    const note = task.changeReason || task.details
    if (note) lines.push(`说明：${note}`)
    return lines.join('<br/>')
  }
}

const renderCompareShadow = (task: DhxGanttTask) => {
  if (!props.showCompare || task.type === 'project') return null
  const compare = compareTaskMap().get(String(task.id))
  if (!compare) return null

  const anchorTask = compare.baselineLaneId
    ? taskMap().get(String(compare.baselineLaneId))
    : undefined
  const start = parseShadowDate(compare.start || compare.baselineStart)
  const end = parseShadowDate(compare.end || compare.baselineEnd)
  if (!start || !end) return null

  const position = ganttApi.getTaskPosition((anchorTask || task) as any, start, end)
  if (!position || position.width <= 0) return null

  const node = document.createElement('button')
  node.type = 'button'
  node.className = `dhx-compare-shadow${compare.isCritical ? ' is-critical' : ''}${compare.changedFields?.length ? ' is-changed' : ''}`
  node.style.left = `${position.left}px`
  node.style.top = `${position.top + Math.max(7, Math.round((position.rowHeight - 12) / 2))}px`
  node.style.width = `${Math.max(position.width, 10)}px`
  node.style.height = '12px'
  const currentStart = task.start_date || '-'
  const currentEnd = formatDate(gantt.calculateEndDate(task.start_date, task.duration))
  const baselineStart = compare.start || compare.baselineStart || '-'
  const baselineEnd = compare.end || compare.baselineEnd || '-'
  const laneChanged = Boolean(
    compare.baselineLaneId && task.laneId && compare.baselineLaneId !== task.laneId
  )
  node.title = [
    `任务：${compare.taskCode || compare.orderId || compare.taskId}`,
    `基线开始：${baselineStart}`,
    `基线结束：${baselineEnd}`,
    `当前开始：${currentStart}`,
    `当前结束：${currentEnd}`,
    `漂移小时：${compare.driftHours ?? 0}h`,
    `是否换设备：${laneChanged ? '是' : '否'}`,
    `变更字段：${compare.changedFields?.join(' / ') || '-'}`
  ].join('\n')
  const drift = compare.driftHours ?? 0
  const driftLabel = drift > 0 ? `+${drift}h` : drift < 0 ? `${drift}h` : '基线'
  if (Math.max(position.width, 10) > 72) {
    node.innerHTML = `<span>${driftLabel}</span>`
  }
  node.onclick = (event) => {
    event.preventDefault()
    event.stopPropagation()
    emits('taskClick', compare.taskId)
  }
  return node
}

const renderActualProgress = (task: DhxGanttTask) => {
  if (!props.showActual || task.type === 'project' || !task.actualStart) return null
  const actualStart = parseShadowDate(task.actualStart)
  // 对于running状态的任务，如果没有actualEnd，使用任务结束时间或当前时间（取较小值）
  let actualEnd = task.actualEnd ? parseShadowDate(task.actualEnd) : null
  if (!actualEnd) {
    // 使用任务结束时间和当前时间中的较小值，避免进度条超出任务范围
    const taskEnd = gantt.calculateEndDate(task.start_date as any, task.duration)
    const now = new Date()
    actualEnd = taskEnd && taskEnd < now ? taskEnd : now
  }
  if (!actualStart || !actualEnd || actualEnd <= actualStart) return null
  const position = ganttApi.getTaskPosition(task as any, actualStart, actualEnd)
  if (!position || position.width <= 0) return null

  const node = document.createElement('div')
  const isFinished = Boolean(task.actualEnd)
  node.className = `dhx-actual-progress${isFinished ? ' is-finished' : ' is-running'}`
  node.style.left = `${position.left}px`
  node.style.top = `${position.top + position.rowHeight - 9}px`
  node.style.width = `${Math.max(position.width, 6)}px`
  node.title = isFinished
    ? `实际完成：${task.actualStart} ~ ${task.actualEnd}`
    : `实际进行中：${task.actualStart} ~ 当前时刻`

  if (!isFinished) {
    const dot = document.createElement('span')
    dot.className = 'dhx-actual-progress-dot'
    node.appendChild(dot)

    if (position.width > 64) {
      const badge = document.createElement('span')
      badge.className = 'dhx-actual-progress-badge'
      badge.textContent = '进行中'
      node.appendChild(badge)
    }
  }

  return node
}

// 渲染计划进度条（替代内置进度条，更清晰显示）
const renderProgressBar = (task: DhxGanttTask) => {
  if (task.type === 'project' || !task.progress || task.progress <= 0) return null

  const taskRow = ganttApi.getTaskPosition(
    task as any,
    task.start_date,
    gantt.calculateEndDate(task.start_date, task.duration)
  )
  if (!taskRow) return null

  const progressWidth = Math.max(taskRow.width * task.progress, 8) // 最小宽度8px
  const isFinished = task.progress >= 1

  const node = document.createElement('div')
  node.className = `dhx-plan-progress${isFinished ? ' is-finished' : ''}`
  node.style.left = `${taskRow.left}px`
  node.style.top = `${taskRow.top + 6}px` // 进度条在任务条上方偏移
  node.style.width = `${progressWidth}px`
  node.style.height = '6px'
  node.title = `计划进度：${Math.round(task.progress * 100)}%`

  return node
}

const renderLaneOverlay = (task: DhxGanttTask) => {
  if (task.type !== 'project') return null
  const wrappers: HTMLElement[] = []
  const row = ganttApi.getTaskPosition(
    task as any,
    task.start_date,
    gantt.calculateEndDate(task.start_date, task.duration)
  )
  if (!row) return null

  if (highlightLaneIdSet().has(String(task.id))) {
    const impact = document.createElement('div')
    impact.className = 'dhx-lane-overlay is-impact'
    impact.style.left = '0px'
    impact.style.top = `${row.top + 2}px`
    impact.style.width = '100%'
    impact.style.height = `${Math.max(row.rowHeight - 4, 20)}px`
    impact.title = `${task.text} 为本次拖拽预演影响范围`
    wrappers.push(impact)
  }

  if (task.laneStatus === 'maintenance') {
    const maintenance = document.createElement('div')
    maintenance.className = 'dhx-lane-overlay is-maintenance'
    maintenance.style.left = '0px'
    maintenance.style.top = `${row.top + 2}px`
    maintenance.style.width = '100%'
    maintenance.style.height = `${Math.max(row.rowHeight - 4, 20)}px`
    maintenance.title = `${task.text} 处于维护窗口`
    wrappers.push(maintenance)
  }

  if (task.freezeBoundary) {
    const boundaryDate = parseShadowDate(task.freezeBoundary)
    if (boundaryDate) {
      const position = ganttApi.getTaskPosition(task as any, boundaryDate, boundaryDate)
      const boundary = document.createElement('div')
      boundary.className = 'dhx-freeze-boundary'
      boundary.style.left = `${Math.max(position.left, 0)}px`
      boundary.style.top = `${row.top}px`
      boundary.style.height = `${row.rowHeight}px`
      boundary.title = `冻结边界：${task.freezeBoundary}`
      wrappers.push(boundary)
    }
  }

  if (!wrappers.length) return null
  const container = document.createElement('div')
  wrappers.forEach((item) => container.appendChild(item))
  return container
}
const syncLayers = () => {
  if (!initialized) return
  if (compareLayerId !== null) ganttApi.removeTaskLayer(compareLayerId as any)
  if (actualLayerId !== null) ganttApi.removeTaskLayer(actualLayerId as any)
  if (laneOverlayLayerId !== null) ganttApi.removeTaskLayer(laneOverlayLayerId as any)
  if (progressLayerId !== null) ganttApi.removeTaskLayer(progressLayerId as any)
  compareLayerId = ganttApi.addTaskLayer((task: DhxGanttTask) => renderCompareShadow(task)) as
    | string
    | number
  actualLayerId = ganttApi.addTaskLayer((task: DhxGanttTask) => renderActualProgress(task)) as
    | string
    | number
  laneOverlayLayerId = ganttApi.addTaskLayer((task: DhxGanttTask) => renderLaneOverlay(task)) as
    | string
    | number
  progressLayerId = ganttApi.addTaskLayer((task: DhxGanttTask) => renderProgressBar(task)) as
    | string
    | number
}

const parseData = async () => {
  if (!initialized || !rootRef.value) return
  await nextTick()
  gantt.clearAll()
  dragSnapshots.clear()
  gantt.parse({ data: props.tasks, links: props.links || [] })
  // parse完成后，重新应用日期范围配置（parse可能会覆盖start_date/end_date）
  if (props.dateFrom && props.dateTo) {
    gantt.config.start_date = new Date(props.dateFrom)
    gantt.config.end_date = new Date(props.dateTo)
  }
  syncLayers()
  // 使用 ganttApi.render() 确保强制重绘
  ganttApi.render()
}

let nativeContextMenuHandler: ((e: MouseEvent) => void) | null = null
let nativeDblClickHandler: ((e: MouseEvent) => void) | null = null

const getTaskIdFromEvent = (e: MouseEvent): string | number | null => {
  const target = e.target as HTMLElement

  // 尝试找任务条元素 - DhtmlxGantt 的任务条有 task_id 属性
  const taskBar = target.closest('.gantt_task_line')
  if (taskBar) {
    const taskId = taskBar.getAttribute('task_id')
    if (taskId) return taskId
  }

  // 尝试找任务行
  const taskRow = target.closest('.gantt_row')
  if (taskRow) {
    const taskId = taskRow.getAttribute('task_id')
    if (taskId) return taskId
  }

  // 最后尝试通过 DhtmlxGantt API 获取
  try {
    const x = e.clientX
    const y = e.clientY
    // getTaskByPos is a DhtmlxGantt method to get task at position
    if (typeof ganttApi.getTaskByPos === 'function') {
      const result = ganttApi.getTaskByPos(x, y)
      if (result && result.id) return result.id
    }
  } catch (err) {
    // ignore
  }

  return null
}

onMounted(async () => {
  if (!rootRef.value) return
  applyConfig()
  gantt.init(rootRef.value)

  // 原生 contextmenu 事件监听，确保右键菜单能触发
  nativeContextMenuHandler = (e: MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()

    // 保存滚动位置
    let currentScrollTop = 0
    let currentScrollLeft = 0

    if (rootRef.value) {
      const taskArea = rootRef.value.querySelector('.gantt_task_area') as HTMLElement
      if (taskArea) {
        currentScrollTop = taskArea.scrollTop
        currentScrollLeft = taskArea.scrollLeft || 0
      }
    }

    const taskId = getTaskIdFromEvent(e)
    if (taskId !== null) {
      emits('taskContextMenu', taskId, e)

      // 恢复滚动位置
      if (rootRef.value) {
        const taskArea = rootRef.value.querySelector('.gantt_task_area') as HTMLElement
        if (taskArea) {
          taskArea.scrollTop = currentScrollTop
          taskArea.scrollLeft = currentScrollLeft
        }
      }
    }
  }
  // P3 修复：原生 contextmenu / dblclick 监听（dhtmlx-gantt 在 readonly 模式下不 dispatch onTaskDblClick）
  // 使用 capture 阶段确保在 gantt 内部处理前拦截事件
  if (rootRef.value) {
    rootRef.value.addEventListener('contextmenu', nativeContextMenuHandler, true)
  }
  // 双击：只对 .gantt_task_line 子元素响应（避免空白/Header 误触）
  nativeDblClickHandler = (e: MouseEvent) => {
    const target = e.target as HTMLElement
    const taskBar = target.closest('.gantt_task_line')
    if (!taskBar) return
    const taskId = taskBar.getAttribute('task_id')
    if (!taskId) return
    emits('taskDblClick', taskId, e)
  }
  if (rootRef.value) {
    rootRef.value.addEventListener('dblclick', nativeDblClickHandler, true)
  }

  eventIds = [
    gantt.attachEvent('onTaskClick', (id) => {
      // 保存滚动位置
      let currentScrollTop = 0
      let currentScrollLeft = 0
      let scrollEl: HTMLElement | null = null

      if (rootRef.value) {
        const selectors = ['.gantt_task_area', '.gantt_task_bg', '.gantt_bg', '.dhx_gantt']
        for (const sel of selectors) {
          const el = rootRef.value.querySelector(sel) as HTMLElement
          if (el && el.scrollTop !== undefined) {
            currentScrollTop = el.scrollTop
            currentScrollLeft = el.scrollLeft || 0
            scrollEl = el
            break
          }
        }
      }

      emits('taskClick', id)

      // 恢复滚动位置
      if (scrollEl) {
        scrollEl.scrollTop = currentScrollTop
        scrollEl.scrollLeft = currentScrollLeft
      }

      return true
    }),
    // P3 修复：dblclick 走 nativeDblClickHandler（dhtmlx-gantt readonly 模式下不 dispatch onTaskDblClick）
    // contextmenu 走 nativeContextMenuHandler（同样原因）
    gantt.attachEvent('onBeforeTaskDrag', (id, mode) => {
      const task = cloneTask(gantt.getTask(id))
      dragSnapshots.set(id, task)
      const result = props.beforeTaskDrag?.({ taskId: id, task, mode })
      if (typeof result === 'string') {
        emits('taskDragRejected', { taskId: id, task, reason: result })
        return false
      }
      if (result === false) {
        emits('taskDragRejected', { taskId: id, task, reason: '当前任务不允许直接拖拽。' })
        return false
      }
      return true
    }),
    gantt.attachEvent('onBeforeTaskChanged', (id, mode) => {
      const payload = buildDragPayload(id, mode)
      const result = props.validateTaskDrop?.(payload)
      const snapshot = dragSnapshots.get(id)

      if (snapshot) {
        ganttApi.silent(() => {
          const currentTask = gantt.getTask(id)
          currentTask.start_date = snapshot.start_date as any
          currentTask.duration = snapshot.duration
          currentTask.parent = snapshot.parent
          ganttApi.updateTask(id)
        })
      }

      if (typeof result === 'string') {
        queueMicrotask(() => {
          forceClearDragState()
          emits('taskDragRejected', { taskId: id, task: payload.task, reason: result })
        })
        return false
      }
      if (result === false) {
        queueMicrotask(() => {
          forceClearDragState()
          emits('taskDragRejected', { taskId: id, task: payload.task, reason: '目标落点不合法。' })
        })
        return false
      }

      queueMicrotask(() => {
        forceClearDragState()
        emits('taskDragEnd', payload)
      })
      return false
    }),
    gantt.attachEvent('onAfterTaskDrag', () => true)
  ]

  initialized = true
  await parseData()
})

// 监听 props 变化，但只在 tasks 或 links 实际改变时重新解析
watch(
  () => [props.tasks, props.links],
  async () => {
    if (initialized) {
      applyConfig()
      await parseData()
    }
  }
)

// 监听 readonly / allowResize / allowProgress 变化，立即重应用 dhtmlx-gantt 的拖拽配置
// （Gantt 页面按角色切换 readonly：PMC+ 允许拖动；VIEWER 一律禁拖，
//  原 wrapper 只在初始化时设 drag_move，角色切换后必须重新应用）
watch(
  () => [props.readonly, props.allowResize, props.allowProgress],
  () => {
    if (!initialized) return
    applyConfig()
    // 重渲染，确保 cursor: grab → default 立即生效
    ganttApi.render?.()
  }
)

// 监听 scaleUnit 变化，重新应用时间轴配置
watch(
  () => props.scaleUnit,
  () => {
    if (initialized) {
      applyScaleConfig(props.scaleUnit)
      ganttApi.render?.()
    }
  }
)

// 监听 dateFrom/dateTo 变化，更新甘特图显示范围
watch(
  () => [props.dateFrom, props.dateTo],
  async () => {
    if (initialized) {
      applyScaleConfig(props.scaleUnit)
      // 重新解析数据以应用新的日期范围
      await parseData()
    }
  }
)

onBeforeUnmount(() => {
  for (const eventId of eventIds) {
    gantt.detachEvent(eventId)
  }
  eventIds = []
  if (nativeContextMenuHandler && rootRef.value) {
    rootRef.value.removeEventListener('contextmenu', nativeContextMenuHandler, { capture: true })
    nativeContextMenuHandler = null
  }
  if (nativeDblClickHandler && rootRef.value) {
    rootRef.value.removeEventListener('dblclick', nativeDblClickHandler, { capture: true })
    nativeDblClickHandler = null
  }
  if (compareLayerId !== null) ganttApi.removeTaskLayer(compareLayerId as any)
  if (actualLayerId !== null) ganttApi.removeTaskLayer(actualLayerId as any)
  if (laneOverlayLayerId !== null) ganttApi.removeTaskLayer(laneOverlayLayerId as any)
  compareLayerId = null
  actualLayerId = null
  laneOverlayLayerId = null
  if (initialized) {
    gantt.clearAll()
  }
})
</script>

<template>
  <div ref="rootRef" class="dhx-gantt-root"></div>
</template>

<style scoped lang="less">
.dhx-gantt-root {
  width: 100%;
  min-height: 680px;
  contain: strict;
  overflow-anchor: none; // 禁用滚动锚点
}

:deep(.gantt_grid_scale),
:deep(.gantt_task_scale) {
  background: linear-gradient(180deg, #f6f9ff 0%, #edf4ff 100%);
  border-bottom: 1px solid #dce8f8;
}

:deep(.gantt_row),
:deep(.gantt_task_row) {
  border-bottom: 1px solid #eef3fb;
}

:deep(.dhx-compare-shadow) {
  position: absolute;
  z-index: 2;
  padding: 0 6px;
  overflow: hidden;
  font-size: 9px;
  line-height: 10px;
  color: #35598f;
  text-overflow: ellipsis;
  white-space: nowrap;
  pointer-events: auto;
  cursor: pointer;
  background: rgb(255 255 255 / 15%);
  border: 1px dashed rgb(37 99 235 / 75%);
  border-radius: 999px;
}

:deep(.dhx-compare-shadow.is-changed) {
  background: rgb(254 243 199 / 35%);
  border-color: rgb(245 158 11 / 85%);
}

:deep(.dhx-compare-shadow.is-critical) {
  box-shadow: inset 0 0 0 1px rgb(190 24 93 / 55%);
}

:deep(.dhx-actual-progress) {
  position: absolute;
  bottom: 4px !important;
  z-index: 3;
  height: 4px;
  pointer-events: none;
  border-radius: 999px;
  box-shadow: 0 0 0 1px rgb(255 255 255 / 65%);
}

:deep(.dhx-actual-progress.is-finished) {
  background: linear-gradient(90deg, #10b981 0%, #059669 100%);
}

:deep(.dhx-actual-progress.is-running) {
  background: repeating-linear-gradient(90deg, #10b981 0, #10b981 10px, #34d399 10px, #34d399 16px);
  opacity: 0.9;
}

:deep(.dhx-actual-progress-dot) {
  position: absolute;
  top: 50%;
  right: -3px;
  width: 8px;
  height: 8px;
  background: #059669;
  border: 2px solid #fff;
  border-radius: 999px;
  transform: translate(50%, -50%);
  box-shadow: 0 0 0 2px rgb(5 150 105 / 28%);
}

:deep(.dhx-actual-progress-badge) {
  position: absolute;
  top: -14px;
  left: 8px;
  padding: 0 4px;
  font-size: 8px;
  line-height: 12px;
  color: #fff;
  white-space: nowrap;
  background: rgb(5 150 105 / 92%);
  border-radius: 999px;
  box-shadow: 0 0 0 1px rgb(255 255 255 / 70%);
}

:deep(.dhx-lane-overlay.is-impact) {
  position: absolute;
  z-index: 1;
  pointer-events: none;
  background: linear-gradient(90deg, rgb(251 191 36 / 12%) 0%, rgb(251 191 36 / 4%) 100%);
  border: 1px dashed rgb(245 158 11 / 65%);
  border-radius: 8px;
}

:deep(.dhx-lane-overlay.is-maintenance) {
  position: absolute;
  z-index: 1;
  pointer-events: none;
  background: repeating-linear-gradient(
    135deg,
    rgb(239 68 68 / 8%) 0,
    rgb(239 68 68 / 8%) 10px,
    rgb(255 255 255 / 0%) 10px,
    rgb(255 255 255 / 0%) 20px
  );
  border: 1px dashed rgb(239 68 68 / 45%);
  border-radius: 8px;
}

:deep(.dhx-freeze-boundary) {
  position: absolute;
  z-index: 4;
  width: 0;
  pointer-events: none;
  border-left: 2px dashed rgb(15 23 42 / 70%);
}

:deep(.dhx-task-lane .gantt_task_content) {
  font-weight: 700;
  color: #24446b !important;
  background: linear-gradient(135deg, #d7e8ff 0%, #c6dbfb 100%) !important;
  border: 1px solid #aac8ef;
}

:deep(.dhx-task-readonly .gantt_task_content) {
  opacity: 0.78;
}

:deep(.dhx-task-impacted .gantt_task_content) {
  box-shadow:
    0 0 0 3px rgb(251 191 36 / 95%),
    0 0 0 6px rgb(251 191 36 / 18%);
}

:deep(.dhx-lane-impacted .gantt_task_content) {
  border-color: #f59e0b;
}

:deep(.dhx-task-critical .gantt_task_content) {
  box-shadow: inset 0 0 0 2px rgb(245 158 11 / 82%);
}

:deep(.dhx-task-warning .gantt_task_content) {
  border: 1px dashed rgb(255 255 255 / 88%);
}

// 状态背景色（status优先级低）
:deep(.dhx-status-frozen .gantt_task_content) {
  background-color: #2563eb !important;
}

:deep(.dhx-status-firm .gantt_task_content) {
  background-color: #10b981 !important;
}

:deep(.dhx-status-locked .gantt_task_content) {
  background-color: #f59e0b !important;
}

:deep(.dhx-status-delayed .gantt_task_content) {
  background-color: #ef4444 !important;
}

:deep(.dhx-status-open .gantt_task_content) {
  background-color: #94a3b8 !important;
  filter: saturate(1.05);
}

:deep(.dhx-status-running .gantt_task_content) {
  box-shadow: inset 0 4px 0 0 #8b5cf6 !important;
}

:deep(.dhx-status-completed .gantt_task_content) {
  box-shadow: inset 0 4px 0 0 #059669 !important;
}

:deep(.dhx-status-material_shortage .gantt_task_content) {
  box-shadow: inset 0 4px 0 0 #d97706 !important;
}

:deep(.dhx-status-cancelled .gantt_task_content) {
  box-shadow: inset 0 4px 0 0 #475569 !important;
}

// 区域背景色（zone优先级高，会覆盖status）
:deep(.dhx-zone-frozen .gantt_task_content) {
  background-color: #2563eb !important;
}

:deep(.dhx-zone-firm .gantt_task_content) {
  background-color: #10b981 !important;
}

:deep(.dhx-zone-locked .gantt_task_content) {
  background-color: #f59e0b !important;
}

:deep(.dhx-zone-delayed .gantt_task_content) {
  background-color: #ef4444 !important;
}

:deep(.dhx-zone-open .gantt_task_content) {
  background-color: #94a3b8 !important;
  filter: saturate(1.05);
}

:deep(.dhx-zone-running .gantt_task_content) {
  background-color: #8b5cf6 !important;
}

:deep(.dhx-zone-completed .gantt_task_content) {
  background-color: #059669 !important;
}

// Completed 状态：添加绿色对勾标识
:deep(.dhx-status-completed .gantt_task_content::after),
:deep(.dhx-zone-completed .gantt_task_content::after) {
  position: absolute;
  top: 50%;
  right: 4px;
  z-index: 10;
  font-size: 12px;
  font-weight: 700;
  color: #fff;
  pointer-events: none;
  content: '✓';
  transform: translateY(-50%);
}

// 确保文字在所有背景色上都清晰可见
:deep(.gantt_task_content) {
  z-index: 1;
  overflow: visible !important;
}

// 计划冻结-斜纹条纹（用于planFreezeFlag=true的任务）
:deep(.dhx-task-frozen .gantt_task_content) {
  background-image: repeating-linear-gradient(
    45deg,
    rgb(255 255 255 / 35%) 0,
    rgb(255 255 255 / 35%) 3px,
    transparent 3px,
    transparent 10px
  ) !important;
  background-position: 0 0 !important;
  background-size: 20px 20px !important;
}

// 文字颜色：保持 zone 背景色下的高对比度
:deep(.dhx-zone-frozen .gantt_task_content),
:deep(.dhx-zone-firm .gantt_task_content),
:deep(.dhx-zone-locked .gantt_task_content),
:deep(.dhx-zone-running .gantt_task_content),
:deep(.dhx-zone-completed .gantt_task_content) {
  padding-right: 24px !important;
  font-weight: 600;
  color: #fff !important;
}

:deep(.dhx-zone-delayed .gantt_task_content) {
  font-weight: 600;
  color: #fff !important;
}

:deep(.dhx-zone-open .gantt_task_content) {
  color: #1e293b !important;
}

// delayed 状态：顶部红色边框 + 红色加粗文字
:deep(.dhx-status-delayed .gantt_task_content) {
  font-weight: 700 !important;
  color: #ef4444 !important;
  box-shadow: inset 0 4px 0 0 #ef4444 !important;
}

:deep(.dhx-zone-delayed .gantt_task_content) {
  font-weight: 700 !important;
  color: #ef4444 !important;
}

// Running 状态：顶部紫色线条（即使背景是 zone 的颜色，也能看到紫色表示正在执行）
:deep(.dhx-status-running .gantt_task_content) {
  box-shadow: inset 0 4px 0 0 #8b5cf6 !important;
}

:deep(.dhx-task-execution-locked .gantt_task_content) {
  box-shadow:
    inset 0 0 0 2px rgb(255 255 255 / 80%),
    0 0 0 1px rgb(15 23 42 / 35%);
}

:deep(.dhx-task-critical .gantt_task_content) {
  box-shadow: inset 0 0 0 2px rgb(245 158 11 / 82%);
}

// 同时有 critical + execution-locked 时：合并两者的 box-shadow
:deep(.dhx-task-critical.dhx-task-execution-locked .gantt_task_content) {
  box-shadow:
    inset 0 0 0 2px rgb(245 158 11 / 82%),
    inset 0 0 0 4px rgb(255 255 255 / 80%),
    0 0 0 3px rgb(15 23 42 / 35%) !important;
}

:deep(.dhx-task-manual-locked .gantt_task_content::after) {
  position: absolute;
  top: 50%;
  right: 4px;
  z-index: 10;
  padding: 1px 4px;
  font-size: 8px;
  line-height: 1.2;
  color: #fff;
  pointer-events: none;
  background: #0f172a;
  border-radius: 999px;
  content: 'LOCK';
  transform: translateY(-50%);
}

:deep(.dhx-task-on-maintenance-lane .gantt_task_content) {
  box-shadow: 0 0 0 2px rgb(239 68 68 / 18%);
}

:deep(.dhx-reason-logistics-delay .gantt_task_content) {
  border: 2px solid rgb(127 29 29 / 95%);
}

:deep(.dhx-reason-logistics-delay .gantt_task_content::before) {
  position: absolute;
  top: 0;
  right: 0;
  width: 0;
  height: 0;
  border-color: transparent #991b1b transparent transparent;
  border-style: solid;
  border-width: 0 10px 10px 0;
  content: '';
}

// 计划进度条（自定义，更清晰显示）
:deep(.dhx-plan-progress) {
  position: absolute;
  z-index: 5;
  pointer-events: none;
  background: linear-gradient(90deg, rgb(255 255 255 / 50%) 0%, rgb(255 255 255 / 90%) 100%);
  border-radius: 3px;
  box-shadow: 0 1px 3px rgb(0 0 0 / 20%);
}

:deep(.dhx-plan-progress.is-finished) {
  background: linear-gradient(90deg, rgb(255 255 255 / 60%) 0%, rgb(255 255 255 / 100%) 100%);
}
</style>
