/**
 * APS V1 4号位 — 订单 / 需求计划查询 API
 *
 * @owner 5号位（中转 2号位 Order / Supply/Pegging Trace 结果）
 * @see 审核报告 §十六.4↔5 + §十九 + §二十二
 *
 * @see 4号位文档第 5 节（页面 2）
 * 后端未实现，Phase A 阶段前端走 mock。
 */

import { apsHttp, APS_USE_MOCK } from './http'
import { mockOrderDetail, mockOrderList, mockOrderSummary } from './__mocks__/fixtures'
import type {
  OrderBasicInfo,
  OrderQuery,
  OrderScheduleDetailDto,
  OrderSummaryStatus,
  PageResult
} from './types'

export const orderApi = {
  /** 订单列表（服务端分页，含可选过滤）
   *  v1.4 §八：5号位 OrderQueryController
   *  - GET /api/order-query                  列表（无分页 Query；按 §八 11 维度筛选）
   *  - GET /api/order-query/{orderId:long}   详情
   *  旧版 /api/order/list 路径已废
   */
  async list(query: OrderQuery): Promise<PageResult<OrderBasicInfo>> {
    if (APS_USE_MOCK) {
      return mockOrderList(query.pageIndex, query.pageSize, {
        orderNo: query.orderNo,
        productionInstructionNo: query.productionInstructionNo,
        materialCode: query.materialCode,
        customerCode: query.customerCode,
        productFamilyCode: query.productFamilyCode,
        factoryCode: query.factoryCode,
        status: query.delayStatus,
        planVersionId: query.planVersionId,
        domainKey: query.domainKey,
        dueDateFrom: query.dueDateFrom,
        dueDateTo: query.dueDateTo
      })
    }
    return apsHttp.get<PageResult<OrderBasicInfo>>({
      url: '/api/order-query',
      params: { ...query }
    })
  },

  /** 订单详情（§八 4 块：basic / pegging / tasks / reasons） */
  async getDetail(orderId: number): Promise<OrderScheduleDetailDto> {
    if (APS_USE_MOCK) return mockOrderDetail(orderId)
    return apsHttp.get<OrderScheduleDetailDto>({
      url: `/api/order-query/${orderId}`
    })
  },

  /** 订单状态聚合（顶部 KPI 用）
   *  2026-09-17 5号位 回执后落地：
   *  - 端点 `GET /api/order-query/summary?planVersionId=`（@see OrderQueryController.cs）
   *  - 响应 DTO（PascalCase）：
   *      {
   *        planVersionId: number,
   *        totalCount: number,
   *        onTimeCount: number,
   *        delayedCount: number,
   *        riskCount: number,
   *        unscheduledCount: number
   *      }
   *  - 前端需将 5 字段映射回 OrderSummaryStatus[]（'ON_TIME' | 'DELAYED' | 'AT_RISK' | 'UNSCHEDULED'）
   *    'ESTIMATED_ONLY' 由前端按需另行计算（v1.4 §七）
   */
  async getSummary(
    planVersionId?: number
  ): Promise<{ status: OrderSummaryStatus; count: number }[]> {
    if (APS_USE_MOCK) return mockOrderSummary()
    const dto = await apsHttp.get<{
      planVersionId: number
      totalCount: number
      onTimeCount: number
      delayedCount: number
      riskCount: number
      unscheduledCount: number
    }>({
      url: '/api/order-query/summary',
      params: planVersionId !== undefined ? { planVersionId } : undefined
    })
    // 映射为现有 OrderSummaryStatus[] 形态
    return [
      { status: 'ON_TIME', count: dto.onTimeCount ?? 0 },
      { status: 'DELAYED', count: dto.delayedCount ?? 0 },
      { status: 'AT_RISK', count: dto.riskCount ?? 0 },
      { status: 'UNSCHEDULED', count: dto.unscheduledCount ?? 0 }
    ]
  }
}
