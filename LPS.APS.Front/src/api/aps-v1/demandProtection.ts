/**
 * APS V1 4号位 — 手工释放需求保护（Demand Protection Release）API
 *
 * @owner 5号位（Demand Protection 查看 / 释放中转 — 4号位 → 5号位 → 2号位 链路）
 * @see 审核报告 §十六.4↔5 + §十三 P1-11
 *
 * @see 4号位文档第 13 节（页面 9 — Demand Protection）
 * @see 审核报告 P1-11 / 4号位 → 5号位 → 2号位 链路
 * @see 5号位只读参考 `lps/LPS.APS.Core/Entities/Aps/DemandSupplyHardLock.cs`
 * @see 5号位只读参考 `lps/LPS.APS.Core/Interfaces/IDemandSupplyHardLockRepository.cs`
 *
 * 接口路径（**前瞻性约定**，5号位 `DemandProtectionController` 当前不存在）：
 *  - GET  /api/demand-protection                       列表（list = preview，含 lockIds + lockedQty + lockDetails）
 *  - POST /api/demand-protection/release               释放（输入 demandKey + lockIds + reason + actor）
 *
 * 4号位职责边界（@see 审核报告第 13 节）：
 *  - 4号位只提交：Demand 标识 + 当前保护数量（回显）+ 释放范围 + Reason + 当前用户身份
 *  - 5号位做 HTTP 入口 / 权限 Scope 检查 / 参数校验 / 转发给 2号位 Service / 返回结果
 *  - 5号位不得 UPDATE 表 / 计算保护数量 / 复制规则
 *  - 4号位不写锁表；partial release 不在本期范围（2号位 Service 只有 Full release）
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type {
  DemandProtectionDto,
  DemandProtectionListFilter,
  DemandProtectionReleaseResult,
  HardLockStatus,
  HardLockSupplyType,
  ReleaseDemandProtectionInput
} from './types/demandProtection'

const iso = (ms: number) => new Date(Date.now() + ms).toISOString()
const ACTOR_MOCK = 'mock-pmc'

/* ===== Mock 数据构造辅助 ===== */

/** 6 个 SupplyType 循环轮转铺明细，模拟 5号位按来源展开 */
const SUPPLY_CYCLE: HardLockSupplyType[] = [
  'INVENTORY',
  'WIP',
  'PIPELINE',
  'PI',
  'PURCHASE_ORDER',
  'VMI'
]

let nextLockId = 9001
function buildLockDetail(
  supplyType: HardLockSupplyType,
  qty: number,
  status: HardLockStatus = 'ACTIVE',
  sourceAllocationSequence?: number
): {
  lockId: number
  supplyType: HardLockSupplyType
  supplyKey: string
  lockedQty: number
  status: HardLockStatus
  sourceAllocationSequence?: number
} {
  const lockId = nextLockId++
  return {
    lockId,
    supplyType,
    supplyKey: `${supplyType}_MAT${lockId}_WH01`,
    lockedQty: qty,
    status,
    sourceAllocationSequence
  }
}

/** 由明细推导聚合 lockIds / lockedQty（模拟 5号位聚合） */
function aggregate(activeDetails: ReturnType<typeof buildLockDetail>[]): {
  lockIds: number[]
  lockedQty: number
} {
  return {
    lockIds: activeDetails.map((d) => d.lockId),
    lockedQty: activeDetails.reduce((sum, d) => sum + d.lockedQty, 0)
  }
}

/* ===== Mock 数据：7 条 DemandKey 聚合 ===== */

const d1Details = [
  buildLockDetail(SUPPLY_CYCLE[0], 12, 'ACTIVE', 1001), // INVENTORY
  buildLockDetail(SUPPLY_CYCLE[3], 8, 'ACTIVE', 1002), // PI
  buildLockDetail(SUPPLY_CYCLE[4], 5, 'ACTIVE', 1003) // PURCHASE_ORDER
]
const d1Agg = aggregate(d1Details)

const d2Details = [buildLockDetail(SUPPLY_CYCLE[0], 30, 'ACTIVE', 2001)]
const d2Agg = aggregate(d2Details)

const d3Details = [
  buildLockDetail(SUPPLY_CYCLE[0], 10, 'ACTIVE', 3001),
  buildLockDetail(SUPPLY_CYCLE[1], 8, 'ACTIVE', 3002),
  buildLockDetail(SUPPLY_CYCLE[2], 12, 'ACTIVE', 3003),
  buildLockDetail(SUPPLY_CYCLE[3], 6, 'ACTIVE', 3004),
  buildLockDetail(SUPPLY_CYCLE[4], 14, 'ACTIVE', 3005)
]
const d3Agg = aggregate(d3Details)

const d4Details = [
  buildLockDetail(SUPPLY_CYCLE[1], 20, 'ACTIVE', 4001),
  buildLockDetail(SUPPLY_CYCLE[2], 15, 'ACTIVE', 4002)
]
const d4Agg = aggregate(d4Details)

const d5Details = [buildLockDetail(SUPPLY_CYCLE[3], 18, 'ACTIVE', 5001)]
const d5Agg = aggregate(d5Details)

const d6Details = [
  buildLockDetail(SUPPLY_CYCLE[2], 22, 'ACTIVE', 6001),
  buildLockDetail(SUPPLY_CYCLE[4], 13, 'ACTIVE', 6002)
]
const d6Agg = aggregate(d6Details)

const d7Details = [
  buildLockDetail(SUPPLY_CYCLE[0], 25, 'RELEASED', 7001),
  buildLockDetail(SUPPLY_CYCLE[1], 10, 'RELEASED', 7002)
]
const d7Agg = aggregate(d7Details.filter((d) => d.status === 'ACTIVE'))

const MOCK_DEMAND_PROTECTIONS: DemandProtectionDto[] = [
  {
    demandType: 'ORDER',
    demandKey: 'ORDER_12345_MAT001_F01',
    demandLabel: '订单 12345 - 电机壳体 A',
    factory: 'F01',
    materialCode: 'MAT001',
    lockType: 'DEMAND_PROTECTION',
    lockIds: d1Agg.lockIds,
    lockedQty: d1Agg.lockedQty,
    lockCount: d1Details.length,
    status: 'ACTIVE',
    lockDetails: d1Details,
    sourcePlanVersionId: 100,
    createdAt: iso(-15 * 86400_000),
    createdBy: 'algorithm'
  },
  {
    demandType: 'ORDER',
    demandKey: 'ORDER_12346_MAT002_F01',
    demandLabel: '订单 12346 - 机柜上盖 B',
    factory: 'F01',
    materialCode: 'MAT002',
    lockType: 'DEMAND_PROTECTION',
    lockIds: d2Agg.lockIds,
    lockedQty: d2Agg.lockedQty,
    lockCount: d2Details.length,
    status: 'ACTIVE',
    lockDetails: d2Details,
    sourcePlanVersionId: 100,
    createdAt: iso(-10 * 86400_000),
    createdBy: 'algorithm'
  },
  {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F01',
    demandLabel: '工作集 WS-2026-08',
    factory: 'F01',
    materialCode: 'WS-MIX',
    lockType: 'DEMAND_PROTECTION',
    lockIds: d3Agg.lockIds,
    lockedQty: d3Agg.lockedQty,
    lockCount: d3Details.length,
    status: 'ACTIVE',
    lockDetails: d3Details,
    sourcePlanVersionId: 102,
    createdAt: iso(-7 * 86400_000),
    createdBy: 'algorithm'
  },
  {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_INJ_S2_MAT003',
    demandLabel: 'PI 注塑二阶段需求',
    factory: 'F02',
    materialCode: 'MAT003',
    lockType: 'DEMAND_PROTECTION',
    lockIds: d4Agg.lockIds,
    lockedQty: d4Agg.lockedQty,
    lockCount: d4Details.length,
    status: 'ACTIVE',
    lockDetails: d4Details,
    sourcePlanVersionId: 102,
    createdAt: iso(-5 * 86400_000),
    createdBy: 'algorithm'
  },
  {
    demandType: 'ORDER',
    demandKey: 'ORDER_12300_MAT001_F02',
    demandLabel: '订单 12300 - 严格绑定案例',
    factory: 'F02',
    materialCode: 'MAT001',
    lockType: 'STRICT_BINDING',
    lockIds: d5Agg.lockIds,
    lockedQty: d5Agg.lockedQty,
    lockCount: d5Details.length,
    status: 'ACTIVE',
    lockDetails: d5Details,
    sourcePlanVersionId: 98,
    createdAt: iso(-20 * 86400_000),
    createdBy: 'algorithm'
  },
  {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-07_F01',
    demandLabel: '工作集 WS-2026-07（严格绑定）',
    factory: 'F01',
    materialCode: 'WS-MIX',
    lockType: 'STRICT_BINDING',
    lockIds: d6Agg.lockIds,
    lockedQty: d6Agg.lockedQty,
    lockCount: d6Details.length,
    status: 'ACTIVE',
    lockDetails: d6Details,
    sourcePlanVersionId: 96,
    createdAt: iso(-30 * 86400_000),
    createdBy: 'algorithm'
  },
  {
    demandType: 'ORDER',
    demandKey: 'ORDER_12280_MAT005_F01',
    demandLabel: '订单 12280 - 已释放案例',
    factory: 'F01',
    materialCode: 'MAT005',
    lockType: 'DEMAND_PROTECTION',
    lockIds: d7Agg.lockIds,
    lockedQty: d7Agg.lockedQty,
    lockCount: d7Details.length,
    status: 'RELEASED',
    lockDetails: d7Details,
    sourcePlanVersionId: 90,
    createdAt: iso(-25 * 86400_000),
    createdBy: 'algorithm',
    releasedAt: iso(-3 * 86400_000),
    releasedBy: 'pmc01',
    releaseReason: '紧急插单：订单 12350 缺料，需释放库存回可分配池'
  }
]

/* ===== Mock 辅助：筛选 ===== */

function applyFilter(
  list: DemandProtectionDto[],
  filter?: DemandProtectionListFilter
): DemandProtectionDto[] {
  if (!filter) return [...list]
  let result = [...list]
  if (filter.activeOnly) result = result.filter((x) => x.status === 'ACTIVE')
  if (filter.demandTypes?.length)
    result = result.filter((x) => filter.demandTypes!.includes(x.demandType))
  if (filter.lockTypes?.length)
    result = result.filter((x) => filter.lockTypes!.includes(x.lockType))
  return result
}

export const demandProtectionApi = {
  /**
   * 列表（list = preview）
   *  - 返回每条聚合的 lockIds[] + lockedQty + lockDetails，UI 无需二次请求
   *  - 支持 demandTypes / lockTypes / activeOnly 筛选
   */
  async list(filter?: DemandProtectionListFilter): Promise<DemandProtectionDto[]> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 250))
      return applyFilter(MOCK_DEMAND_PROTECTIONS, filter)
    }
    return apsHttp.get<DemandProtectionDto[]>({
      url: '/api/demand-protection',
      params: filter as Record<string, unknown>
    })
  },

  /**
   * 释放（POST /api/demand-protection/release）
   *
   * 校验（mock 阶段在前端做，真实阶段由 5号位兜底）：
   *  1. lockIds 必填且非空
   *  2. releaseReason 必填（trim 后非空字符串；2026-09-13 撤销 ≥ 5 字符限制）
   *  3. releasedBy 必填
   *
   * 成功（mock）：按 lockIds 逐条返回 DemandProtectionReleaseResult
   *  - RELEASED：lockId 命中 ACTIVE + DEMAND_PROTECTION 的明细
   *  - FAILED：lockId 不存在 / 命中 STRICT_BINDING / 命中 RELEASED
   *
   * @see 5号位 `lps/LPS.APS.Web/Controllers/DemandProtectionController.cs` Release
   * @see 5号位 `lps/LPS.APS.Core/Dto/DemandProtectionDtos.cs` DemandProtectionReleaseResult
   */
  async release(input: ReleaseDemandProtectionInput): Promise<DemandProtectionReleaseResult[]> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 300))
      if (!input.releaseReason || !input.releaseReason.trim()) {
        throw new Error(`释放原因必填`)
      }
      if (!input.releasedBy) {
        throw new Error(`操作人必填`)
      }
      if (!input.lockIds.length) {
        throw new Error(`lockIds 必填且非空`)
      }

      // 逐 lock 处理（与 5 号位 per-lock 部分成功语义一致）
      const results: DemandProtectionReleaseResult[] = input.lockIds.map((lockId) => {
        // 在所有 MOCK 聚合里找含此 lockId 的明细
        const owner = MOCK_DEMAND_PROTECTIONS.find((x) =>
          x.lockDetails.some((d) => d.lockId === lockId)
        )
        if (!owner) {
          return {
            lockId,
            demandKey: '',
            status: 'FAILED',
            failureReason: 'Lock 不存在'
          }
        }
        const detail = owner.lockDetails.find((d) => d.lockId === lockId)!
        if (owner.lockType === 'STRICT_BINDING') {
          return {
            lockId,
            demandKey: owner.demandKey,
            status: 'FAILED',
            failureReason: 'STRICT_BINDING 严格绑定锁不支持手工释放'
          }
        }
        if (detail.status !== 'ACTIVE') {
          return {
            lockId,
            demandKey: owner.demandKey,
            status: 'FAILED',
            failureReason: `当前明细状态为 ${detail.status}，不可重复释放`
          }
        }
        // 成功：mock 原地更新明细状态为 RELEASED
        detail.status = 'RELEASED'
        return {
          lockId,
          demandKey: owner.demandKey,
          status: 'RELEASED'
        }
      })

      // 同步聚合行的状态与 lockedQty / lockIds（mock 阶段保持列表自洽）
      for (const owner of MOCK_DEMAND_PROTECTIONS) {
        const activeDetails = owner.lockDetails.filter((d) => d.status === 'ACTIVE')
        const releasedCount = owner.lockDetails.filter((d) => d.status === 'RELEASED').length
        owner.lockIds = activeDetails.map((d) => d.lockId)
        owner.lockedQty = activeDetails.reduce((sum, d) => sum + d.lockedQty, 0)
        owner.lockCount = owner.lockDetails.length
        if (activeDetails.length === 0 && releasedCount > 0) {
          owner.status = 'RELEASED'
          owner.releasedAt = owner.releasedAt ?? new Date().toISOString()
          owner.releasedBy = owner.releasedBy ?? input.releasedBy
          owner.releaseReason = owner.releaseReason ?? input.releaseReason.trim()
        }
      }

      return results
    }
    return apsHttp.post<DemandProtectionReleaseResult[]>({
      url: '/api/demand-protection/release',
      data: input
    })
  }
}

/** 默认 actor（mock 阶段简化；真实接入时从 apsAuth.userInfo?.username 取） */
export const mockDpActor = ACTOR_MOCK
