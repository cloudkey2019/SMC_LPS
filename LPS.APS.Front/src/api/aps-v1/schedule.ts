/**
 * APS V1 4号位 — 排程查询 API
 *
 * 接口路径与 LPS.APS.Web/Controllers/ScheduleController.cs 一致：
 *  - GET /api/schedule/versions?take=30
 *  - GET /api/schedule/gantt/{planVersionId}
 *  - GET /api/schedule/summary/{planVersionId}
 *
 * mock 切换：VITE_USE_MOCK=true 时返回 fixture，否则走真实后端
 */

import { apsHttp, APS_USE_MOCK } from './http'
import { mockGantt, mockSummary, mockVersions } from './__mocks__/fixtures'
import type { GanttDataDto, PlanVersionSummaryDto, ScheduleSummaryDto } from './types'

export const scheduleApi = {
  /** 计划版本列表（默认 30 条） */
  async getVersions(take = 30): Promise<PlanVersionSummaryDto[]> {
    if (APS_USE_MOCK) return mockVersions.slice(0, take)
    return apsHttp.get<PlanVersionSummaryDto[]>({
      url: '/api/schedule/versions',
      params: { take }
    })
  },

  /** 指定版本的甘特图数据（@owner 5号位 中转 2号位 ScheduleResult） */
  async getGantt(planVersionId: number): Promise<GanttDataDto> {
    if (APS_USE_MOCK) return mockGantt(planVersionId)
    return apsHttp.get<GanttDataDto>({
      url: `/api/schedule/gantt/${planVersionId}`
    })
  },

  /** 指定版本的 KPI 概要（@owner 5号位 中转 2号位 ScheduleResult 聚合） */
  async getSummary(planVersionId: number): Promise<ScheduleSummaryDto> {
    if (APS_USE_MOCK) return mockSummary(planVersionId)
    return apsHttp.get<ScheduleSummaryDto>({
      url: `/api/schedule/summary/${planVersionId}`
    })
  }
}
