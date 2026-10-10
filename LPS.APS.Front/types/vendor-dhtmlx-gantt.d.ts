declare module '@vendor/dhtmlx-gantt/codebase/dhtmlxgantt.es.js' {
  interface GanttTaskLike {
    id: string | number
    text?: string
    start_date: string | Date
    duration: number
    parent?: string | number
    readonly?: boolean
    isCritical?: boolean
    warningCount?: number
    status?: string
    materialName?: string
    changeReason?: string
    details?: string
    [key: string]: any
  }

  interface GanttScale {
    unit: string
    step: number
    format: string
  }

  interface GanttColumn {
    name: string
    label: string
    tree?: boolean
    align?: string
    width?: number | string
  }

  interface GanttStatic {
    date: {
      date_to_str: (format: string) => (date: string | Date) => string
    }
    config: {
      readonly: boolean
      row_height: number
      xml_date: string
      date_format: string
      drag_resize: boolean
      drag_move: boolean
      drag_progress: boolean
      open_tree_initially: boolean
      grid_width: number
      bar_height: number
      grid_resize: boolean
      show_progress: boolean
      scales: GanttScale[]
      columns: GanttColumn[]
      [key: string]: any
    }
    templates: {
      task_class: (start: Date | string, end: Date | string, task: GanttTaskLike) => string
      task_text: (start: Date | string, end: Date | string, task: GanttTaskLike) => string
      tooltip_text: (start: Date | string, end: Date | string, task: GanttTaskLike) => string
      [key: string]: any
    }
    init: (container: HTMLElement) => void
    clearAll: () => void
    parse: (data: { data: GanttTaskLike[]; links?: any[] }) => void
    getTask: (id: string | number) => GanttTaskLike
    calculateEndDate: (startDate: string | Date, duration: number) => string | Date
    attachEvent: (name: string, handler: (...args: any[]) => any) => string
    detachEvent: (id: string) => void
  }

  const gantt: GanttStatic
  export default gantt
}

declare module '@vendor/dhtmlx-gantt/codebase/dhtmlxgantt.css'
