/**
 * APS V1 4号位 — 排产总览 API（占位）
 *
 * @owner 5号位（中转 2号位 PlanOverview 结果）
 * @see 审核报告 §十六.4↔5 + §十九
 *
 * 后端未实现，Phase A 阶段前端走 mock。
 * 验收场景：U01 / U02 / U07
 */

import { apsHttp, APS_USE_MOCK } from './http'
import { mockOverview } from './__mocks__/fixtures'
import type { PlanOverviewDto } from './types'

export const overviewApi = {
  /** 排产总览：ACTIVE 版本 / 订单摘要 / 资源摘要 / 异常
   *  v1.4 §七：Overview 数据源走 5号位 OverviewController（@see lps/LPS.APS.Web/Controllers/OverviewController.cs）
   *  - /api/overview/active-plan   当前 ACTIVE PlanVersion + 各 Domain 摘要
   *  - /api/overview/task-summary  任务（按状态聚合）
   *  - /api/overview/resource-bottleneck  资源瓶颈
   *  - /api/overview/candidate-summary    Candidate 待确认数
   *  单一端点 active-plan 已是 U01/U02/U07 的最小集；后续按需扩展为 4 卡
   */
  async getOverview(): Promise<PlanOverviewDto> {
    if (APS_USE_MOCK) return mockOverview
    return apsHttp.get<PlanOverviewDto>({
      url: '/api/overview/active-plan'
    })
  }
}
