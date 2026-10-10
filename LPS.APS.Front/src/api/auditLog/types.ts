export interface AuditLogItem {
  id: number
  operateTime: string
  operator: string
  operationType: string
  objectType: string
  target: string
  result: 'Success' | 'Failed' | 'Denied'
  detail: string
  ip: string
}

export interface AuditLogQuery {
  operator: string
  operationType: string
  result: string
  dateRange: string[]
  page: number
  pageSize: number
}

export interface AuditLogListParams {
  operator?: string
  operationType?: string
  result?: string
  startTime?: string
  endTime?: string
  page: number
  pageSize: number
}

export interface CreateAuditLogReq {
  operationType: string
  objectType: string
  target: string
  detail: string
  result?: 'Success' | 'Failed' | 'Denied'
  operator?: string
}
