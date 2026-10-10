import request from '@/axios'
import type { AuditLogListParams, CreateAuditLogReq } from './types'

export const getAuditLogListApi = (params: AuditLogListParams) => {
  return request.get({ url: '/mock/audit-log/list', params })
}

export const createAuditLogApi = (data: CreateAuditLogReq) => {
  return request.post({ url: '/mock/audit-log/create', data })
}
