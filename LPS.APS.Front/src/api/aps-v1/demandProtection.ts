/**
 * APS V1 4号位 — 手工释放需求保护（Demand Protection Release）API
 *
 * @owner 5号位（Demand Protection 查看 / 释放中转 — 4号位 → 5号位 → 2号位 链路）
 * @see 4号位文档第 13 节（页面 9 — Demand Protection）
 * @see 审核报告 P1-11 / 4号位 → 5号位 → 2号位 链路
 *
 * 接口路径（10-09 lps/ 落地后，对齐 5号位 DemandProtectionController）：
 *  - GET    /api/demand-protection                       列表（**per-lock 扁平**，一行 = 一个 DemandSupplyHardLock）
 *  - GET    /api/demand-protection/summary               汇总（KPI 4 卡权威数 + items 上限 50 ACTIVE）
 *  - POST   /api/demand-protection/release               释放（输入 lockIds + reason + actor）
 *
 * 后端契约（10-09 lps/ 落地后）：
 *  - 列表是 **per-lock** 扁平结构（一行 = 一个 DemandSupplyHardLock），5号位 不在 5号位 端聚合
 *  - 聚合（按 demandKey group by + PARTIALLY_RELEASED 派生）由**前端 store 完成**
 *  - KPI 4 卡由 /summary 端点给权威数（DB COUNT/SUM）；前端不本地推算
 *
 * 4号位职责边界（@see 4号位文档第 13 节 / 审核报告 P1-11）：
 *  - 4号位 只提交：Demand 标识 + 当前保护数量（回显）+ 释放范围 + Reason + 当前用户身份
 *  - 5号位 做 HTTP 入口 / 权限 Scope 检查 / 参数校验 / 转发给 2号位 Service / 返回结果
 *  - 4号位 不计算 保护数量 / 派生态 / 不写锁表
 *  - 释放走 per-lock partial success（5号位契约：逐 lock 处理，部分成功；Allocation 不动）
 *
 * ⚠️ 2026-09-13 治理红线复核说明（@see APS_V1_各号位AI冻结基线治理红线_v1.0_20260914.md §4.2）：
 *  - 历史曾加 releaseReason ≥5 字符硬约束，来自 9月13日 1号位 解读，非 0号位 正式裁决
 *  - 1号位 ≠ 0号位 → 该硬约束不能直接落地；当前仅保留「必填」+ 200 字符上限
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type {
  BackendDemandProtectionRow,
  DemandProtectionListFilter,
  DemandProtectionReleaseResult,
  DemandProtectionSummary,
  HardLockStatus,
  HardLockSupplyType,
  HardLockType,
  ReleaseDemandProtectionInput
} from './types/demandProtection'

const iso = (ms: number) => new Date(Date.now() + ms).toISOString()
const ACTOR_MOCK = 'mock-pmc'

/* ===== Mock 数据构造辅助（per-lock 扁平形态） ===== */

const SUPPLY_CYCLE: HardLockSupplyType[] = [
  'INVENTORY',
  'WIP',
  'PIPELINE',
  'PI',
  'PURCHASE_ORDER',
  'VMI'
]

/** 业务真实 supplyKey 模板（让锁明细表更可读） */
function genSupplyKey(supplyType: HardLockSupplyType, idx: number): string {
  const seq = (9000 + idx).toString()
  switch (supplyType) {
    case 'INVENTORY':
      return `WH01-BIN-A-${10 + (idx % 8)}-2026-10-0${1 + (idx % 9)}`
    case 'WIP':
      return `PI-2026-00${100 + idx}`
    case 'PIPELINE':
      return `PO-2026-00${80 + idx} / SUZ→CDG`
    case 'PI':
      return `PI-2026-00${123 + idx} / S${1 + (idx % 3)}-ASSY`
    case 'PURCHASE_ORDER':
      return `PO-2026-00${89 + idx} / MAT00${1 + (idx % 5)} / 苏州富士`
    case 'VMI':
      return `VMI / ${idx % 2 === 0 ? 'ACME-电机' : 'BOSCH-控制'} / 苏州前置仓`
    default:
      return `${supplyType}_${seq}`
  }
}

let nextLockId = 9001
function buildRow(
  supplyType: HardLockSupplyType,
  qty: number,
  status: HardLockStatus,
  sourceAllocationSequence: number,
  overrides: Partial<BackendDemandProtectionRow> = {}
): BackendDemandProtectionRow {
  const lockId = nextLockId++
  const supplyKey = genSupplyKey(supplyType, lockId - 9001)
  return {
    id: lockId,
    lockType: 'DEMAND_PROTECTION',
    demandType: 'ORDER',
    demandKey: '',
    supplyType,
    supplyKey,
    lockedQty: qty,
    status,
    sourceAllocationSequence,
    createdAt: iso(-15 * 86400_000),
    createdBy: 'algorithm',
    ...overrides
  }
}

/** 分散 createdBy：algorithm / rule-engine / pmc01-05 多样化 */
function pickActor(seed: number): string {
  const actors = ['algorithm', 'rule-engine', 'pmc01', 'pmc02', 'pmc03', 'pmc-wang']
  return actors[seed % actors.length]
}

/* ===== Mock 数据：14 条 demandKey × N lock 全展开为 per-lock 行 ===== */

const MOCK_ROWS: BackendDemandProtectionRow[] = [
  // d1 ORDER_12345 + DEMAND_PROTECTION + 3 ACTIVE
  buildRow(SUPPLY_CYCLE[0], 12, 'ACTIVE', 1001, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12345_MAT001_F01',
    createdAt: iso(-15 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 100
  }),
  buildRow(SUPPLY_CYCLE[3], 8, 'ACTIVE', 1002, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12345_MAT001_F01',
    createdAt: iso(-15 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 100
  }),
  buildRow(SUPPLY_CYCLE[4], 5, 'ACTIVE', 1003, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12345_MAT001_F01',
    createdAt: iso(-15 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 100
  }),
  // d2 ORDER_12346 + 1 ACTIVE
  buildRow(SUPPLY_CYCLE[0], 30, 'ACTIVE', 2001, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12346_MAT002_F01',
    createdAt: iso(-10 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 100
  }),
  // d3 WORKSET + 5 ACTIVE
  buildRow(SUPPLY_CYCLE[0], 10, 'ACTIVE', 3001, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F01',
    createdAt: iso(-7 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  buildRow(SUPPLY_CYCLE[1], 8, 'ACTIVE', 3002, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F01',
    createdAt: iso(-7 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  buildRow(SUPPLY_CYCLE[2], 12, 'ACTIVE', 3003, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F01',
    createdAt: iso(-7 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  buildRow(SUPPLY_CYCLE[3], 6, 'ACTIVE', 3004, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F01',
    createdAt: iso(-7 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  buildRow(SUPPLY_CYCLE[4], 14, 'ACTIVE', 3005, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F01',
    createdAt: iso(-7 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  // d4 PI_STAGE_DEMAND + 2 ACTIVE
  buildRow(SUPPLY_CYCLE[1], 20, 'ACTIVE', 4001, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_INJ_S2_MAT003',
    createdAt: iso(-5 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  buildRow(SUPPLY_CYCLE[2], 15, 'ACTIVE', 4002, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_INJ_S2_MAT003',
    createdAt: iso(-5 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 102
  }),
  // d5 ORDER + STRICT_BINDING + 1 ACTIVE
  buildRow(SUPPLY_CYCLE[3], 18, 'ACTIVE', 5001, {
    lockType: 'STRICT_BINDING',
    demandType: 'ORDER',
    demandKey: 'ORDER_12300_MAT001_F02',
    createdAt: iso(-20 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 98
  }),
  // d6 WORKSET + STRICT_BINDING + 2 ACTIVE
  buildRow(SUPPLY_CYCLE[2], 22, 'ACTIVE', 6001, {
    lockType: 'STRICT_BINDING',
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-07_F01',
    createdAt: iso(-30 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 96
  }),
  buildRow(SUPPLY_CYCLE[4], 13, 'ACTIVE', 6002, {
    lockType: 'STRICT_BINDING',
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-07_F01',
    createdAt: iso(-30 * 86400_000),
    createdBy: 'algorithm',
    sourcePlanVersionId: 96
  }),
  // d7 ORDER + 2 RELEASED（历史释放）
  buildRow(SUPPLY_CYCLE[0], 25, 'RELEASED', 7001, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12280_MAT005_F01',
    createdAt: iso(-25 * 86400_000),
    createdBy: pickActor(7),
    releasedAt: iso(-3 * 86400_000),
    releasedBy: 'pmc01',
    releaseReason: '紧急插单：订单 12350 缺料，需释放库存回可分配池',
    sourcePlanVersionId: 90
  }),
  buildRow(SUPPLY_CYCLE[1], 10, 'RELEASED', 7002, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12280_MAT005_F01',
    createdAt: iso(-25 * 86400_000),
    createdBy: pickActor(7),
    releasedAt: iso(-3 * 86400_000),
    releasedBy: 'pmc01',
    releaseReason: '紧急插单：订单 12350 缺料，需释放库存回可分配池',
    sourcePlanVersionId: 90
  }),
  // d8 ORDER + 跨工厂 + 4 ACTIVE
  buildRow(SUPPLY_CYCLE[0], 18, 'ACTIVE', 8001, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12350_MAT003_F01_V103',
    createdAt: iso(-2 * 86400_000),
    createdBy: pickActor(0),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[1], 12, 'ACTIVE', 8002, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12350_MAT003_F01_V103',
    createdAt: iso(-2 * 86400_000),
    createdBy: pickActor(0),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[3], 22, 'ACTIVE', 8003, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12350_MAT003_F01_V103',
    createdAt: iso(-2 * 86400_000),
    createdBy: pickActor(0),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[5], 8, 'ACTIVE', 8004, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12350_MAT003_F01_V103',
    createdAt: iso(-2 * 86400_000),
    createdBy: pickActor(0),
    sourcePlanVersionId: 103
  }),
  // d9 ORDER + 2 PI locks
  buildRow(SUPPLY_CYCLE[3], 45, 'ACTIVE', 9001, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12351_MAT001_F02_V103',
    createdAt: iso(-1 * 86400_000),
    createdBy: pickActor(1),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[4], 30, 'ACTIVE', 9002, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12351_MAT001_F02_V103',
    createdAt: iso(-1 * 86400_000),
    createdBy: pickActor(1),
    sourcePlanVersionId: 103
  }),
  // d10 ORDER + 部分释放（混合态：1 ACTIVE + 2 RELEASED）
  buildRow(SUPPLY_CYCLE[0], 15, 'ACTIVE', 10001, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12320_MAT004_F01_V100',
    createdAt: iso(-12 * 86400_000),
    createdBy: pickActor(2),
    sourcePlanVersionId: 100
  }),
  buildRow(SUPPLY_CYCLE[2], 20, 'RELEASED', 10002, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12320_MAT004_F01_V100',
    createdAt: iso(-12 * 86400_000),
    createdBy: pickActor(2),
    releasedAt: iso(-1 * 86400_000),
    releasedBy: 'pmc02',
    releaseReason: '客户减单 50%，释放多余在途 + 采购份额',
    sourcePlanVersionId: 100
  }),
  buildRow(SUPPLY_CYCLE[4], 12, 'RELEASED', 10003, {
    demandType: 'ORDER',
    demandKey: 'ORDER_12320_MAT004_F01_V100',
    createdAt: iso(-12 * 86400_000),
    createdBy: pickActor(2),
    releasedAt: iso(-1 * 86400_000),
    releasedBy: 'pmc02',
    releaseReason: '客户减单 50%，释放多余在途 + 采购份额',
    sourcePlanVersionId: 100
  }),
  // d11 WORKSET + 6 ACTIVE
  buildRow(SUPPLY_CYCLE[0], 8, 'ACTIVE', 11001, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-09_F01_V104',
    createdAt: iso(-6 * 3600_000),
    createdBy: pickActor(3),
    sourcePlanVersionId: 104
  }),
  buildRow(SUPPLY_CYCLE[1], 14, 'ACTIVE', 11002, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-09_F01_V104',
    createdAt: iso(-6 * 3600_000),
    createdBy: pickActor(3),
    sourcePlanVersionId: 104
  }),
  buildRow(SUPPLY_CYCLE[2], 9, 'ACTIVE', 11003, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-09_F01_V104',
    createdAt: iso(-6 * 3600_000),
    createdBy: pickActor(3),
    sourcePlanVersionId: 104
  }),
  buildRow(SUPPLY_CYCLE[3], 18, 'ACTIVE', 11004, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-09_F01_V104',
    createdAt: iso(-6 * 3600_000),
    createdBy: pickActor(3),
    sourcePlanVersionId: 104
  }),
  buildRow(SUPPLY_CYCLE[4], 11, 'ACTIVE', 11005, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-09_F01_V104',
    createdAt: iso(-6 * 3600_000),
    createdBy: pickActor(3),
    sourcePlanVersionId: 104
  }),
  buildRow(SUPPLY_CYCLE[5], 16, 'ACTIVE', 11006, {
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-09_F01_V104',
    createdAt: iso(-6 * 3600_000),
    createdBy: pickActor(3),
    sourcePlanVersionId: 104
  }),
  // d12 WORKSET + STRICT_BINDING + 3 ACTIVE
  buildRow(SUPPLY_CYCLE[0], 22, 'ACTIVE', 12001, {
    lockType: 'STRICT_BINDING',
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F02_V101',
    createdAt: iso(-18 * 86400_000),
    createdBy: pickActor(4),
    sourcePlanVersionId: 101
  }),
  buildRow(SUPPLY_CYCLE[3], 28, 'ACTIVE', 12002, {
    lockType: 'STRICT_BINDING',
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F02_V101',
    createdAt: iso(-18 * 86400_000),
    createdBy: pickActor(4),
    sourcePlanVersionId: 101
  }),
  buildRow(SUPPLY_CYCLE[4], 19, 'ACTIVE', 12003, {
    lockType: 'STRICT_BINDING',
    demandType: 'WORKSET',
    demandKey: 'WORKSET_WS-2026-08_F02_V101',
    createdAt: iso(-18 * 86400_000),
    createdBy: pickActor(4),
    sourcePlanVersionId: 101
  }),
  // d13 PI_STAGE_DEMAND + 5 ACTIVE
  buildRow(SUPPLY_CYCLE[1], 25, 'ACTIVE', 13001, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_ASSY_S3_MAT006_F01_V103',
    createdAt: iso(-3 * 86400_000),
    createdBy: pickActor(5),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[2], 18, 'ACTIVE', 13002, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_ASSY_S3_MAT006_F01_V103',
    createdAt: iso(-3 * 86400_000),
    createdBy: pickActor(5),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[3], 32, 'ACTIVE', 13003, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_ASSY_S3_MAT006_F01_V103',
    createdAt: iso(-3 * 86400_000),
    createdBy: pickActor(5),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[4], 14, 'ACTIVE', 13004, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_ASSY_S3_MAT006_F01_V103',
    createdAt: iso(-3 * 86400_000),
    createdBy: pickActor(5),
    sourcePlanVersionId: 103
  }),
  buildRow(SUPPLY_CYCLE[5], 10, 'ACTIVE', 13005, {
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_ASSY_S3_MAT006_F01_V103',
    createdAt: iso(-3 * 86400_000),
    createdBy: pickActor(5),
    sourcePlanVersionId: 103
  }),
  // d14 PI_STAGE_DEMAND + STRICT_BINDING + 1 ACTIVE
  buildRow(SUPPLY_CYCLE[3], 60, 'ACTIVE', 14001, {
    lockType: 'STRICT_BINDING',
    demandType: 'PI_STAGE_DEMAND',
    demandKey: 'PI_STAGE_TEST_S4_MAT007_F02_V102',
    createdAt: iso(-9 * 86400_000),
    createdBy: pickActor(0),
    sourcePlanVersionId: 102
  })
]

/* ===== Mock 辅助：per-lock 筛选（与 5号位 /summary 端点同语义） ===== */

function rowMatches(row: BackendDemandProtectionRow, filter?: DemandProtectionListFilter): boolean {
  if (!filter) return true
  if (filter.demandKey && !row.demandKey.toLowerCase().includes(filter.demandKey.toLowerCase()))
    return false
  if (filter.supplyKey && !row.supplyKey.toLowerCase().includes(filter.supplyKey.toLowerCase()))
    return false
  if (filter.lockType && row.lockType !== filter.lockType) return false
  if (filter.status && row.status !== filter.status) return false
  return true
}

function buildSummaryFromRows(
  rows: BackendDemandProtectionRow[],
  capItems = 50
): DemandProtectionSummary {
  const totalCount = rows.length
  const activeRows = rows.filter((r) => r.status === 'ACTIVE')
  const releasedRows = rows.filter((r) => r.status === 'RELEASED')
  const brokenRows = rows.filter((r) => r.status === 'BROKEN')
  const totalLockedQty = activeRows.reduce((sum, r) => sum + r.lockedQty, 0)
  return {
    totalCount,
    activeCount: activeRows.length,
    releasedCount: releasedRows.length,
    brokenCount: brokenRows.length,
    totalLockedQty,
    items: activeRows.slice(0, capItems)
  }
}

export const demandProtectionApi = {
  /**
   * 列表（GET /api/demand-protection）
   *  - **per-lock 扁平**：一行 = 一个 DemandSupplyHardLock
   *  - 5号位 不在 5号位 端聚合（按 demandKey group by + PARTIALLY_RELEASED 派生由前端 store 完成）
   *  - 后端 QueryAsync 签名：`demandKey? / supplyKey? / lockType? / status? / skip=0 / take=100`
   *  - 后端 `demandKey` / `supplyKey` 走 LIKE '%xx%' substring（@see lps/.../DemandProtectionRepository.cs:48-49）
   */
  async list(filter?: DemandProtectionListFilter): Promise<BackendDemandProtectionRow[]> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 250))
      const filtered = MOCK_ROWS.filter((r) => rowMatches(r, filter))
      const skip = filter?.skip ?? 0
      const take = filter?.take ?? 100
      return filtered.slice(skip, skip + take)
    }
    return apsHttp.get<BackendDemandProtectionRow[]>({
      url: '/api/demand-protection',
      params: filter as Record<string, unknown>
    })
  },

  /**
   * 汇总（GET /api/demand-protection/summary）
   *  - KPI 4 卡权威数（DB COUNT/SUM）；与 list 端点筛选条件完全脱钩（summary = 全集，items 上限 50 ACTIVE）
   *  - 用于 KPI 卡：totalCount / activeCount / releasedCount / brokenCount / totalLockedQty
   *  - 后端仅接受 `demandKey` 查询参数（@see DemandProtectionController.GetSummary）
   */
  async summary(
    filter?: Pick<DemandProtectionListFilter, 'demandKey'>
  ): Promise<DemandProtectionSummary> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 200))
      // 注意：mock 阶段 summary 不按 lockType/status 过滤，与 5号位 /summary 端点语义一致（全集 KPI）
      return buildSummaryFromRows(MOCK_ROWS)
    }
    return apsHttp.get<DemandProtectionSummary>({
      url: '/api/demand-protection/summary',
      params: filter as Record<string, unknown>
    })
  },

  /**
   * 释放（POST /api/demand-protection/release）
   *
   * 校验（mock 阶段在前端做，真实阶段由 5号位 兜底）：
   *  1. lockIds 必填且非空
   *  2. releaseReason 必填（trim 后非空字符串；2026-09-13 撤销 ≥5 字符限制）
   *  3. releasedBy 必填
   *
   * 成功（mock）：按 lockIds 逐条返回 DemandProtectionReleaseResult
   *  - RELEASED：lockId 命中 ACTIVE + DEMAND_PROTECTION 的明细
   *  - FAILED：lockId 不存在 / 命中 STRICT_BINDING / 命中 RELEASED
   *
   * 释放后 mock 原地把对应 row.status 改为 RELEASED（保持 mock 自洽；真实阶段后端已落库）
   *
   * @see lps/LPS.APS.Web/Controllers/DemandProtectionController.cs Release
   * @see lps/LPS.APS.Core/Dto/DemandProtectionDtos.cs DemandProtectionReleaseRequest
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

      // 逐 lock 处理（与 5号位 per-lock 部分成功语义一致）
      const results: DemandProtectionReleaseResult[] = input.lockIds.map((lockId) => {
        const row = MOCK_ROWS.find((r) => r.id === lockId)
        if (!row) {
          return { lockId, demandKey: '', status: 'FAILED', failureReason: 'Lock 不存在' }
        }
        if (row.lockType === 'STRICT_BINDING') {
          return {
            lockId,
            demandKey: row.demandKey,
            status: 'FAILED',
            failureReason: 'STRICT_BINDING 严格绑定锁不支持手工释放'
          }
        }
        if (row.status !== 'ACTIVE') {
          return {
            lockId,
            demandKey: row.demandKey,
            status: 'FAILED',
            failureReason: `当前明细状态为 ${row.status}，不可重复释放`
          }
        }
        // 成功：mock 原地更新 row.status 为 RELEASED
        row.status = 'RELEASED'
        row.releasedAt = new Date().toISOString()
        row.releasedBy = input.releasedBy
        row.releaseReason = input.releaseReason.trim()
        return { lockId, demandKey: row.demandKey, status: 'RELEASED' }
      })

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

/** 给 store 聚合层使用的 lockType 别名（兼容 HardLockType → LOCK_TYPES 名称） */
export type { HardLockType }
