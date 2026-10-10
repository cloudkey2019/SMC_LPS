/**
 * APS V1 4号位 — Candidate 对比与确认 API（占位）
 *
 * @owner 3号位（Candidate 运行治理 / 确认 / 激活 — 审核报告 §十四 P1-06）
 * @see 审核报告 §十六.4↔3
 *
 * @see 4号位文档第 9 节（页面 5）
 * 验收：U10 / U11 / U12 / U13 / U14
 *
 * 注意：
 *  - CTP 和 INSERT_IMPACT_ANALYSIS 永远不能激活为 PlanVersion（U11/U12）
 *  - 后端候选版本（status=CANDIDATE）只能由"手工发起重排"产生
 *  - 4号位页面是确认入口，confirms 走 Controller，不直接写库
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type {
  CandidateActivateInput,
  CandidateActivateResult,
  CandidateComparisonDto,
  CandidateConfirmInput,
  CandidateConfirmResult,
  DomainKey
} from './types'

const isoAgo = (ms: number) => new Date(Date.now() - ms).toISOString()
const isoFromNow = (ms: number) => new Date(Date.now() + ms).toISOString()

/* ===== 多个 Candidate 用于切换 ===== */

const candidate1: CandidateComparisonDto = {
  header: {
    candidatePlanVersionId: 2001,
    candidateVersionCode: 'C-2026-001',
    basePlanVersionId: 1001,
    baseVersionCode: 'V-2026-001',
    domainKey: 'FAMILY_INJECTION',
    status: 'CANDIDATE',
    createdAt: isoAgo(3600_000),
    sourceRefId: 5001,
    sourceRefType: 'MANUAL_RESCHEDULE',
    // P0-05：LOCAL_RESCHEDULE 触发可激活
    canActivate: true,
    runType: 'LOCAL_RESCHEDULE'
  },
  newOrderDiffs: [
    {
      orderId: 201,
      orderNo: 'ORD-2026-0201',
      requestedDueDate: isoFromNow(14 * 86400_000),
      baseCompletion: isoFromNow(15 * 86400_000),
      candidateCompletion: isoFromNow(13 * 86400_000),
      onTime: true,
      isEstimated: true
    }
  ],
  impactedOrders: [
    {
      orderId: 102,
      orderNo: 'ORD-2026-0102',
      baseCompletion: isoFromNow(8 * 86400_000),
      candidateCompletion: isoFromNow(11 * 86400_000),
      deltaHours: 72,
      becomesDelayed: true,
      hasProtectionConflict: true
    },
    {
      orderId: 103,
      orderNo: 'ORD-2026-0103',
      baseCompletion: isoFromNow(9 * 86400_000),
      candidateCompletion: isoFromNow(9.5 * 86400_000),
      deltaHours: 12,
      becomesDelayed: false,
      hasProtectionConflict: false
    }
  ],
  taskChangeSummary: {
    added: 2,
    removed: 1,
    timeShifted: 8,
    resourceChanged: 3,
    crossDomainBlocked: 0
  },
  impactSummary: {
    impactedOrderCount: 2,
    newDelayCount: 1,
    estimatedOnlyCount: 1,
    crossDomainImpacted: false
  },
  reasons: [
    {
      reasonCode: 'CAPACITY_REALLOCATE',
      description: 'MC01 资源重新分配至优先级更高的订单',
      factType: 'RECOMMENDATION'
    }
  ]
}

const candidate2: CandidateComparisonDto = {
  header: {
    candidatePlanVersionId: 2002,
    candidateVersionCode: 'C-2026-002',
    basePlanVersionId: 1003,
    baseVersionCode: 'C-2026-003',
    domainKey: 'FAMILY_TEST',
    status: 'CANDIDATE',
    createdAt: isoAgo(7200_000),
    sourceRefId: 5001,
    sourceRefType: 'MANUAL_RESCHEDULE',
    // P0-05：LOCAL_RESCHEDULE 触发可激活
    canActivate: true,
    runType: 'LOCAL_RESCHEDULE',
    // v1.2 §13：1 个外部 Domain 阻挡（共享测试台）
    externalDomainBlocksCount: 1
  },
  newOrderDiffs: [
    {
      orderId: 401,
      orderNo: 'ORD-2026-0401',
      requestedDueDate: isoFromNow(20 * 86400_000),
      candidateCompletion: isoFromNow(22 * 86400_000),
      onTime: false,
      isEstimated: false
    }
  ],
  impactedOrders: [
    {
      orderId: 405,
      orderNo: 'ORD-2026-0405',
      baseCompletion: isoFromNow(18 * 86400_000),
      candidateCompletion: isoFromNow(20 * 86400_000),
      deltaHours: 48,
      becomesDelayed: true,
      hasProtectionConflict: false,
      // v1.2 §13：受共享资源阻挡
      blockedBySharedResourceRefId: 9001
    }
  ],
  taskChangeSummary: {
    added: 1,
    removed: 0,
    timeShifted: 4,
    resourceChanged: 1,
    crossDomainBlocked: 2, // U14 关键
    // v1.2 §10/§13：TEST 共享测试台被 FAMILY_INJECTION 占用
    sharedResourceOccupancies: [
      {
        resourceRefId: 9001,
        resourceCode: 'TEST-BENCH-01',
        domainKey: 'FAMILY_INJECTION',
        occupiedFrom: isoFromNow(5 * 86400_000),
        occupiedTo: isoFromNow(7 * 86400_000),
        sourcePlanVersionId: 1001
      }
    ]
  },
  impactSummary: {
    impactedOrderCount: 1,
    newDelayCount: 1,
    estimatedOnlyCount: 0,
    crossDomainImpacted: true, // U14 关键
    // v1.2 统计
    sharedResourceOccupancyCount: 1,
    quantityTimeSliceCount: 0
  },
  reasons: [
    {
      reasonCode: 'EXTERNAL_DOMAIN_BLOCK',
      description:
        'TEST 域共享测试台 TEST-BENCH-01 被 FAMILY_INJECTION 域 ACTIVE 占用至 9 月 8 日 18:00',
      factType: 'FACT'
    }
  ]
}

/** P0-05：WHATIF Candidate（由 CTP / INSERT_IMPACT_ANALYSIS 触发产生）
 *  - runType = INSERT_ORDER_WHATIF
 *  - canActivate = false（只读试算）
 *  - 可以形成 CANDIDATE PlanVersion，但前端不能激活
 */
const candidateWhatif: CandidateComparisonDto = {
  header: {
    candidatePlanVersionId: 2003,
    candidateVersionCode: 'W-2026-001-CTP',
    basePlanVersionId: 1001,
    baseVersionCode: 'V-2026-001',
    domainKey: 'FAMILY_INJECTION',
    status: 'CANDIDATE',
    createdAt: isoAgo(1800_000),
    sourceRefId: 5002,
    sourceRefType: 'INSERT_ORDER_WHATIF',
    // P0-05：INSERT_ORDER_WHATIF 触发 → 仅试算，绝不激活
    canActivate: false,
    runType: 'INSERT_ORDER_WHATIF',
    purpose: 'CTP'
  },
  newOrderDiffs: [
    {
      orderId: 999,
      orderNo: 'ORD-2026-NEW-001',
      requestedDueDate: isoFromNow(7 * 86400_000),
      baseCompletion: undefined,
      candidateCompletion: isoFromNow(9 * 86400_000),
      onTime: false,
      isEstimated: true
    }
  ],
  impactedOrders: [
    {
      orderId: 102,
      orderNo: 'ORD-2026-0102',
      baseCompletion: isoFromNow(8 * 86400_000),
      candidateCompletion: isoFromNow(11 * 86400_000),
      deltaHours: 72,
      becomesDelayed: true,
      hasProtectionConflict: true
    }
  ],
  taskChangeSummary: {
    added: 1,
    removed: 0,
    timeShifted: 3,
    resourceChanged: 0,
    crossDomainBlocked: 0
  },
  impactSummary: {
    impactedOrderCount: 1,
    newDelayCount: 1,
    estimatedOnlyCount: 1,
    crossDomainImpacted: false
  },
  reasons: [
    {
      reasonCode: 'CTP_WHATIF',
      description: '由 CTP 评估产生，仅试算 — 不能激活为 ACTIVE（审核报告 P0-05）',
      factType: 'ESTIMATED'
    }
  ]
}

const CANDIDATES: Record<number, CandidateComparisonDto> = {
  2001: candidate1,
  2002: candidate2,
  2003: candidateWhatif
}

/**
 * P0-04 收尾：INSERT_IMPACT_ANALYSIS 触发的 WHATIF 样本（ID=2004）
 *  - 与 candidateWhatif (ID=2003 / CTP) 并列展示，用于在 Candidate 列表验证 purpose 区分
 *  - canActivate=false：纯试算，不激活（P0-04 + P0-05）
 *  - 落入 ASSEMBLY 域（与 2003 INJECTION 区分），演示多 Domain 试算并存
 */
const candidateInsertImpact: CandidateComparisonDto = {
  header: {
    candidatePlanVersionId: 2004,
    candidateVersionCode: 'W-2026-002-IIA',
    basePlanVersionId: 1002,
    baseVersionCode: 'V-2026-002',
    domainKey: 'FAMILY_ASSEMBLY',
    status: 'CANDIDATE',
    createdAt: isoAgo(900_000),
    sourceRefId: 5003,
    sourceRefType: 'INSERT_ORDER_WHATIF',
    canActivate: false,
    runType: 'INSERT_ORDER_WHATIF',
    purpose: 'INSERT_IMPACT_ANALYSIS',
    // v1.2 §13：1 个外部 Domain 阻挡
    externalDomainBlocksCount: 1,
    // v1.2 §11：上游 Domain 传入 M-005 切片（必须保留分段：40件@15日 / 60件@17日，禁止压平为 100件）
    quantityTimeSlices: [
      {
        materialCode: 'M-005',
        quantity: 40,
        availableTime: isoFromNow(5 * 86400_000),
        sourceDomainKey: 'FAMILY_INJECTION'
      },
      {
        materialCode: 'M-005',
        quantity: 60,
        availableTime: isoFromNow(7 * 86400_000),
        sourceDomainKey: 'FAMILY_INJECTION'
      }
    ]
  },
  newOrderDiffs: [
    {
      orderId: 1000,
      orderNo: 'ORD-2026-NEW-002',
      requestedDueDate: isoFromNow(5 * 86400_000),
      baseCompletion: undefined,
      candidateCompletion: isoFromNow(9 * 86400_000),
      onTime: false,
      isEstimated: true
    }
  ],
  impactedOrders: [
    {
      orderId: 205,
      orderNo: 'ORD-2026-0205',
      baseCompletion: isoFromNow(6 * 86400_000),
      candidateCompletion: isoFromNow(10 * 86400_000),
      deltaHours: 96,
      becomesDelayed: true,
      hasProtectionConflict: false,
      // v1.2 §13：被 FAMILY_TEST 共享 ASM-LINE-03 阻挡
      blockedBySharedResourceRefId: 9002
    }
  ],
  taskChangeSummary: {
    added: 2,
    removed: 0,
    timeShifted: 5,
    resourceChanged: 1,
    crossDomainBlocked: 1,
    // v1.2 §10/§13：FAMILY_TEST ASM-LINE-03 共享资源占用明细（其它 Domain ACTIVE 占用的不可移动时间块）
    sharedResourceOccupancies: [
      {
        resourceRefId: 9002,
        resourceCode: 'ASM-LINE-03',
        domainKey: 'FAMILY_TEST',
        occupiedFrom: isoFromNow(6 * 86400_000),
        occupiedTo: isoFromNow(8 * 86400_000),
        sourcePlanVersionId: 1003
      }
    ]
  },
  impactSummary: {
    impactedOrderCount: 2,
    newDelayCount: 1,
    estimatedOnlyCount: 2,
    crossDomainImpacted: true,
    // v1.2 统计：1 个共享资源占用 + 2 个 Quantity-Time 切片
    sharedResourceOccupancyCount: 1,
    quantityTimeSliceCount: 2
  },
  reasons: [
    {
      reasonCode: 'INSERT_IMPACT_WHATIF',
      description: '由 INSERT_IMPACT_ANALYSIS 评估产生，仅试算 — 不能激活为 ACTIVE（P0-04）',
      factType: 'ESTIMATED'
    },
    {
      reasonCode: 'SHARED_RESOURCE_OCCUPANCY',
      description:
        'FAMILY_TEST 域共享线体 ASM-LINE-03 已被占用于 ACTIVE 排程，本 ASSEMBLY 候选无法抢占（v1.2 §10）',
      factType: 'FACT'
    },
    {
      reasonCode: 'QUANTITY_TIME_SLICE',
      description:
        'FAMILY_INJECTION 域传入 M-005 物料切片 40 件 @9/15 + 60 件 @9/17，分段保留未压平（v1.2 §11）',
      factType: 'ESTIMATED'
    }
  ]
}

CANDIDATES[2004] = candidateInsertImpact

/** mock 用：按 domainKey 归一到一个「CANDIDATES 里已存在」的可激活候选
 *  - 供 run.ts 的 mock triggerReschedule 复用：mock 触发后跳 Candidate 页需能加载详情
 *  - 原 mock 返 `2000 + domainKey.length` 是孤立 ID（如 FAMILY_INJECTION → 2016），
 *    跳转后 list 查不到 + CANDIDATES[2016] 不存在 → 报"未知 Candidate"
 *  - 真实模式不涉及（后端返真实 ID + list 端点真实返回）
 */
export const findMockActivatableCandidate = (
  domainKey: string
): CandidateComparisonDto | undefined => {
  const all = Object.values(CANDIDATES)
  return (
    all.find((c) => c.header.domainKey === domainKey && c.header.canActivate) ??
    all.find((c) => c.header.canActivate) ??
    all[0]
  )
}

/** 暴露给前端"候选版本列表"用 */
export const mockCandidateList = (): Array<{
  candidatePlanVersionId: number
  candidateVersionCode: string
  domainKey: DomainKey
  status: string
  createdAt: string
  canActivate: boolean
  runType?: 'FULL_SCHEDULE' | 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
  /** mock 临时补齐 basePlanVersionId（5号位 真实 list 端点 17 字段收口中；届时去掉 mock 兜底即可）
   *  - 从 CANDIDATES[id].header.basePlanVersionId 取值
   *  - 仅 mock 走查用，真实模式仍依赖 5号位 补 CandidateBriefDto
   */
  basePlanVersionId?: number
  impactSummary: { impactedOrderCount: number; newDelayCount: number }
}> =>
  Object.values(CANDIDATES).map((c) => ({
    candidatePlanVersionId: c.header.candidatePlanVersionId,
    candidateVersionCode: c.header.candidateVersionCode,
    domainKey: c.header.domainKey,
    status: c.header.status,
    createdAt: c.header.createdAt,
    canActivate: c.header.canActivate,
    runType: c.header.runType,
    basePlanVersionId: c.header.basePlanVersionId,
    impactSummary: c.impactSummary
  }))

export const candidateApi = {
  /** Candidate 列表（页面 5 入口）
   *  v1.4 §十二：3号位 回执（2026-09-17）确认：Candidate 列表归 5号位
   *  - 已存在：`GET /api/overview/candidate-summary`（OverviewController.cs L127，5号位 维护）
   *  - 当前返回 `CandidateBriefDto[]`，SQL `WHERE VersionCategory='CANDIDATE'`
   *  - 缺：§十二 17 字段（Base/Candidate 完成时间差、Task 增删移数等）→ 待 5号位 补齐
   *  - 临时方案：调 `/api/governance/runs?status=CANDIDATE` 兜底（绕道）
   *  TODO(待 5号位 补齐 17 字段后)：切换到 `/api/overview/candidate-summary`
   */
  async list(): Promise<
    Array<{
      candidatePlanVersionId: number
      candidateVersionCode: string
      domainKey: DomainKey
      status: string
      createdAt: string
      canActivate: boolean
      runType?: 'FULL_SCHEDULE' | 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
      /** v1.4 §十二：调用 compare-with 端点的第二个路径参数；当前 list 端点暂未返回（5号位 17 字段收口中）*/
      basePlanVersionId?: number
      impactSummary: { impactedOrderCount: number; newDelayCount: number }
    }>
  > {
    if (APS_USE_MOCK) return mockCandidateList()
    return apsHttp.get<
      Array<{
        candidatePlanVersionId: number
        candidateVersionCode: string
        domainKey: DomainKey
        status: string
        createdAt: string
        canActivate: boolean
        runType?: 'FULL_SCHEDULE' | 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
        /** v1.4 §十二：5号位 补 17 字段后会返；当前未返 → store 抛清晰错 */
        basePlanVersionId?: number
        impactSummary: { impactedOrderCount: number; newDelayCount: number }
      }>
    >({ url: '/api/governance/runs?status=CANDIDATE' })
  },

  /** 获取 Candidate 对比（§十二 17 字段）
   *  后端：GET /api/governance/plan-version/{candidatePlanVersionId}/compare-with/{basePlanVersionId}
   *  路径双参：candidatePlanVersionId + basePlanVersionId（v1.2 §十三 P0-08 + CompareCandidateWithBase）
   *  v1.4 §十二：5号位 待补 basePlanVersionId 到 CandidateBriefDto（@see 4号位-2026-09-18-Candidate17字段催办-给5号位.md）
   *  当前 list 返回的 brief 暂不含 basePlanVersionId；UI 流程：
   *   - list → 选中 → 若 basePlanVersionId 缺失 → 走 mock fallback（dev/演示）
   *   - 真实模式：依赖 5号位 把 basePlanVersionId 加入 CandidateBriefDto（17 字段之一）
   */
  async getComparison(
    candidatePlanVersionId: number,
    basePlanVersionId: number
  ): Promise<CandidateComparisonDto> {
    if (APS_USE_MOCK) {
      const c = CANDIDATES[candidatePlanVersionId]
      if (!c)
        throw new Error(
          `[mock] 未知 Candidate #${candidatePlanVersionId}，可选 ${Object.keys(CANDIDATES).join(', ')}`
        )
      return c
    }
    return apsHttp.get<CandidateComparisonDto>({
      url: `/api/governance/plan-version/${candidatePlanVersionId}/compare-with/${basePlanVersionId}`
    })
  },

  /** 最小人工确认（仅用于 Manual/Candidate，CTP/INSERT_IMPACT_ANALYSIS 永远无此接口）
   * URL 修正（2026-09-12 Pkg-9）：后端在 GovernanceController 里，
   * 真实路径 `/api/governance/plan-version/{id}/confirm-candidate`（v1.2 §十三 P0-08）。
   */
  async confirm(input: CandidateConfirmInput): Promise<CandidateConfirmResult> {
    if (APS_USE_MOCK) {
      return {
        candidatePlanVersionId: input.candidatePlanVersionId,
        basePlanVersionId: input.basePlanVersionId,
        actor: input.actor,
        confirmedAt: new Date().toISOString(),
        remark: input.remark
      }
    }
    return apsHttp.post<CandidateConfirmResult>({
      url: `/api/governance/plan-version/${input.candidatePlanVersionId}/confirm-candidate`,
      data: { remark: input.remark }
    })
  },

  /** Candidate 激活（CANDIDATE → ACTIVE，每域单一正式采用版本）
   *  - v1.2 §十三 P0-08；权限码 `aps.candidate.activate`
   *  - 仅 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE 触发（canActivate=true）可激活
   *  - 后端 GovernanceController.ActivateCandidate，空请求体
   *  - 错误：400（非 CANDIDATE 状态）/ 403（无权限或越权）/ 409（Domain 已存在其它 ACTIVE）
   */
  async activate(input: CandidateActivateInput): Promise<CandidateActivateResult> {
    if (APS_USE_MOCK) {
      return {
        candidatePlanVersionId: input.candidatePlanVersionId,
        activatedBy: 'mock-user',
        activatedAt: new Date().toISOString()
      }
    }
    // 后端返回 `{success, message}` 包装（GovernanceController 风格），用 apsHttp 解包
    return apsHttp.post<CandidateActivateResult>({
      url: `/api/governance/plan-version/${input.candidatePlanVersionId}/activate-candidate`,
      data: {}
    })
  }
}
