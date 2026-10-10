/**
 * APS V1 4号位 — CTP / 插单评估 API（占位）
 *
 * @owner 3号位发起 Run（CTP / INSERT_IMPACT_ANALYSIS Purpose 注入） + 5号位查询结果
 * @see 审核报告 §十六.4↔3 + §十九 + §八.P0-04
 *
 * @see 4号位文档第 7-8 节（页面 4）
 * 验收：U07 / U08 / U09 / U11 / U15
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type { CtpInput, CtpResultDto } from './types'

/**
 * Mock 实现：
 *  - 根据 materialCode 与 requestedDueDate 简单分支，覆盖 4 种典型场景
 *  - 完全 DTO 层面 mock，不调任何外部求解器（4号位 V1 仅展示前端）
 */
const mockCtpResult = (input: CtpInput): CtpResultDto => {
  // 场景 1：按期 + 全部 FACT
  if (input.materialCode === 'M-001' && input.quantity <= 100) {
    return {
      meetsRequestedDueDate: true,
      earliestCompletion: input.requestedDueDate,
      dependsOnEstimatedSupply: false,
      impactedOrderCount: 0,
      maxImpactedOrdersThreshold: 5,
      impactedOrders: [],
      protectionConflicts: [],
      mainBottleneck: '当前产能充足，无瓶颈',
      reasons: [
        {
          reasonCode: 'MATERIAL_AVAILABLE',
          description: '物料 M-001 现存库存 + PI 在制可满足需求',
          isEstimated: false,
          relatedRef: 'INV-M-001'
        }
      ],
      isSingleDomain: true,
      isCrossDomainChained: false
    }
  }

  // 场景 2：单 Domain 延期（CAPACITY + ESTIMATED）
  if (input.materialCode === 'M-003') {
    return {
      meetsRequestedDueDate: false,
      earliestCompletion: new Date(Date.now() + 12 * 86400_000).toISOString(),
      dependsOnEstimatedSupply: true,
      impactedOrderCount: 3,
      maxImpactedOrdersThreshold: 5,
      impactedOrders: [
        {
          orderId: 101,
          orderNo: 'ORD-2026-0101',
          originalCompletion: new Date(Date.now() + 10 * 86400_000).toISOString(),
          newCompletion: new Date(Date.now() + 13 * 86400_000).toISOString(),
          becomesDelayed: true,
          hasProtectionConflict: false
        },
        {
          orderId: 102,
          orderNo: 'ORD-2026-0102',
          originalCompletion: new Date(Date.now() + 8 * 86400_000).toISOString(),
          newCompletion: new Date(Date.now() + 11 * 86400_000).toISOString(),
          becomesDelayed: true,
          hasProtectionConflict: true
        }
      ],
      protectionConflicts: [
        {
          orderId: 102,
          orderNo: 'ORD-2026-0102',
          conflictType: 'PROTECTION_OVERLAP',
          description: '订单 ORD-2026-0102 在 P1 段受 Demand Protection，本次 CTP 触发重叠'
        }
      ],
      mainBottleneck: 'MC01 注塑机产能不足（已 92% 利用）',
      reasons: [
        {
          reasonCode: 'CAPACITY_LIMIT',
          description: 'MC01 在 9 月 5 日~7 日无空闲窗口',
          isEstimated: false,
          relatedRef: 'MC01'
        },
        {
          reasonCode: 'MATERIAL_AVAILABLE',
          description: 'PO-2026-9981 采购估算 9 月 15 日才到',
          isEstimated: true,
          relatedRef: 'PO-2026-9981'
        }
      ],
      isSingleDomain: true,
      isCrossDomainChained: false
    }
  }

  // 场景 3：跨 Domain（INJECTION → ASSEMBLY → TEST 链式）
  if (input.materialCode === 'M-005') {
    return {
      meetsRequestedDueDate: false,
      earliestCompletion: new Date(Date.now() + 18 * 86400_000).toISOString(),
      dependsOnEstimatedSupply: true,
      impactedOrderCount: 7,
      maxImpactedOrdersThreshold: 5, // 超阈值 → Warning（U15）
      impactedOrders: [
        {
          orderId: 201,
          orderNo: 'ORD-2026-0201',
          originalCompletion: new Date(Date.now() + 14 * 86400_000).toISOString(),
          newCompletion: new Date(Date.now() + 19 * 86400_000).toISOString(),
          becomesDelayed: true,
          hasProtectionConflict: false
        }
      ],
      protectionConflicts: [],
      mainBottleneck: '跨 Domain 链式 WHATIF — INJECTION 产能不足导致下游 ASSEMBLY/TEST 顺延',
      reasons: [
        {
          reasonCode: 'CROSS_DOMAIN_HANDOFF',
          description: 'INJECTION 完成日 9 月 18 日 → ASSEMBLY 9 月 20 日 → TEST 9 月 22 日',
          isEstimated: false,
          relatedRef: 'INJECTION→ASSEMBLY→TEST'
        },
        {
          reasonCode: 'MATERIAL_AVAILABLE',
          description: 'M-005 部分依赖 PO 估算入库',
          isEstimated: true,
          relatedRef: 'PO-2026-7770'
        }
      ],
      isSingleDomain: false,
      isCrossDomainChained: true,
      domainResults: [
        {
          domainKey: 'FAMILY_INJECTION',
          earliestCompletion: new Date(Date.now() + 8 * 86400_000).toISOString(),
          mainBottleneck: 'MC01 产能不足',
          success: false,
          failureReason: '2026-09-05 ~ 09-07 窗口占满'
        },
        {
          domainKey: 'FAMILY_ASSEMBLY',
          earliestCompletion: new Date(Date.now() + 12 * 86400_000).toISOString(),
          mainBottleneck: '等待 FAMILY_INJECTION 完成',
          success: true
        },
        {
          domainKey: 'FAMILY_TEST',
          earliestCompletion: new Date(Date.now() + 18 * 86400_000).toISOString(),
          mainBottleneck: '等待 FAMILY_ASSEMBLY + 测试台日历',
          success: true
        }
      ]
    }
  }

  // 默认场景：dUE_DATE_RISK 表面原因
  return {
    meetsRequestedDueDate: false,
    earliestCompletion: new Date(Date.now() + 7 * 86400_000).toISOString(),
    dependsOnEstimatedSupply: false,
    impactedOrderCount: 1,
    maxImpactedOrdersThreshold: 5,
    impactedOrders: [
      {
        orderId: 301,
        orderNo: 'ORD-2026-0301',
        originalCompletion: new Date(Date.now() + 6 * 86400_000).toISOString(),
        becomesDelayed: false,
        hasProtectionConflict: false
      }
    ],
    protectionConflicts: [],
    mainBottleneck: '默认单 Domain 排产',
    reasons: [
      {
        reasonCode: 'DUE_DATE_RISK',
        description: '客户请求交期早于当前生产节拍',
        isEstimated: false
      }
    ],
    isSingleDomain: true,
    isCrossDomainChained: false
  }
}

export const ctpApi = {
  /** CTP 评估（页面 4，U11/Purpose=CTP 不可"采用"，仅展示结果） */
  async evaluate(input: CtpInput): Promise<CtpResultDto> {
    if (APS_USE_MOCK) return mockCtpResult(input)
    return apsHttp.post<CtpResultDto>({
      url: '/api/ctp/evaluate',
      data: input
    })
  }
}
