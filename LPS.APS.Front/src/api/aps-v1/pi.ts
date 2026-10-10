/**
 * APS V1 4号位 — PI Position / 供给追溯 API
 *
 * @owner 5号位自属（PI Position 业务事实）
 * @see 审核报告 §十六.4↔5 + §十 P0-06 + §二十二
 *
 * @see 4号位文档第 13 节（页面 7）
 * 验收：U16 / U17 / U18
 *
 * 设计要点：
 *  - U16：UNLOCATED 数量必须显示，并标注"不可下发 MES"
 *  - U17：无 PI 规划的 Task 显示不可 MES
 *  - U18：依赖 PLANNING_PURCHASE_PLACEHOLDER 的 Task 不允许下 MES
 *  - Position 类型：ERP_REMAINING / PRODUCTION / TRANSIT / WAITING / UNLOCATED / CROSS_STAGE（6 类）
 *  - Supply 类型：INVENTORY / PI / PO / VMI / ARRIVED_NOT_INBOUND / INTERPLANT_TRANSIT /
 *                RECEIVED / PLANNED_PRODUCTION / PLANNING_PURCHASE_PLACEHOLDER（9 类）
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type { PiListItem, PiListFilter, PiPositionViewDto } from './types'

const iso = (ms: number) => new Date(Date.now() + ms).toISOString()

/* ===== 8 个 PI 用于列表 + 详情 ===== */

const piListData: PiPositionViewDto[] = [
  {
    // PI-001 — PRODUCTION + UNLOCATED 40 + Placeholder 依赖（U16 + U18 双重）
    productionInstructionId: 1,
    productionInstructionNo: 'PI-2026-0001',
    domainKey: 'FAMILY_INJECTION',
    materialCode: 'M-001',
    erpRemainingQty: 200,
    positionTotalQty: 240,
    variance: 40,
    mainPositionType: 'PRODUCTION',
    positions: [
      {
        productionInstructionId: 1,
        productionInstructionNo: 'PI-2026-0001',
        materialId: 200,
        materialCode: 'M-001',
        positionType: 'PRODUCTION',
        stage: 'STG-INJ',
        crossFactoryQty: 0,
        transitQty: 40,
        waitingQty: 0,
        unlocatedQty: 40,
        positionQty: 200,
        issue: 'UNLOCATED 数量 40 — 不可下发 MES（U16）'
      },
      {
        productionInstructionId: 1,
        productionInstructionNo: 'PI-2026-0001',
        materialId: 200,
        materialCode: 'M-001',
        positionType: 'TRANSIT',
        stage: 'STG-TR',
        transitQty: 40,
        positionQty: 40
      }
    ],
    supplies: [
      {
        supplyType: 'INVENTORY',
        supplyKey: 'INV-2026-0512',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 80,
        availableTime: iso(-86400_000),
        relatedOrderId: 100,
        relatedOrderNo: 'ORD-2026-0100'
      },
      {
        supplyType: 'PLANNING_PURCHASE_PLACEHOLDER',
        supplyKey: 'PO-2026-9981',
        isPlanningOnlyPlaceholder: true,
        isNotCommitted: true,
        qty: 40,
        availableTime: iso(15 * 86400_000),
        description: '估算日期，采购尚无正式承诺'
      },
      {
        supplyType: 'PO',
        supplyKey: 'PO-2026-3210',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 80,
        availableTime: iso(5 * 86400_000),
        relatedOrderId: 100,
        relatedOrderNo: 'ORD-2026-0100'
      }
    ],
    issues: [
      {
        issueType: 'UNLOCATED_EXISTS',
        message: '存在 UNLOCATED 数量 40',
        relatedSupplyKey: 'PI-2026-0001'
      },
      {
        issueType: 'PLACEHOLDER_DEPENDENCY',
        message: '依赖 PLANNING_PURCHASE_PLACEHOLDER，不允许下 MES',
        relatedSupplyKey: 'PO-2026-9981'
      }
    ],
    updatedAt: iso(-3600_000)
  },

  {
    // PI-002 — TRANSIT 主，纯净，INVENTORY + INTERPLANT_TRANSIT
    productionInstructionId: 2,
    productionInstructionNo: 'PI-2026-0002',
    domainKey: 'FAMILY_INJECTION',
    materialCode: 'M-002',
    erpRemainingQty: 320,
    positionTotalQty: 320,
    variance: 0,
    mainPositionType: 'TRANSIT',
    positions: [
      {
        productionInstructionId: 2,
        productionInstructionNo: 'PI-2026-0002',
        materialId: 201,
        materialCode: 'M-002',
        positionType: 'TRANSIT',
        stage: 'STG-TR',
        transitQty: 200,
        positionQty: 200
      },
      {
        productionInstructionId: 2,
        productionInstructionNo: 'PI-2026-0002',
        materialId: 201,
        materialCode: 'M-002',
        positionType: 'PRODUCTION',
        stage: 'STG-INJ',
        positionQty: 120
      }
    ],
    supplies: [
      {
        supplyType: 'INVENTORY',
        supplyKey: 'INV-2026-0815',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 150,
        availableTime: iso(-2 * 86400_000),
        relatedOrderId: 102,
        relatedOrderNo: 'ORD-2026-0102'
      },
      {
        supplyType: 'INTERPLANT_TRANSIT',
        supplyKey: 'IT-2026-0044',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 100,
        availableTime: iso(3 * 86400_000),
        description: '苏州厂 → 成都厂在途'
      },
      {
        supplyType: 'PO',
        supplyKey: 'PO-2026-3300',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 70,
        availableTime: iso(7 * 86400_000),
        relatedOrderId: 102,
        relatedOrderNo: 'ORD-2026-0102'
      }
    ],
    issues: [],
    updatedAt: iso(-7200_000)
  },

  {
    // PI-003 — WAITING 主，跨工厂（XC）+ 部分 Placeholder
    productionInstructionId: 3,
    productionInstructionNo: 'PI-2026-0003',
    domainKey: 'FAMILY_TEST',
    materialCode: 'M-003',
    erpRemainingQty: 150,
    positionTotalQty: 180,
    variance: 30,
    mainPositionType: 'WAITING',
    positions: [
      {
        productionInstructionId: 3,
        productionInstructionNo: 'PI-2026-0003',
        materialId: 202,
        materialCode: 'M-003',
        positionType: 'WAITING',
        stage: 'STG-WAIT',
        waitingQty: 100,
        positionQty: 100
      },
      {
        productionInstructionId: 3,
        productionInstructionNo: 'PI-2026-0003',
        materialId: 202,
        materialCode: 'M-003',
        positionType: 'CROSS_STAGE',
        stage: 'STG-XS',
        crossFactoryQty: 80,
        positionQty: 80,
        issue: '跨阶段分配 +30，variance 非零'
      }
    ],
    supplies: [
      {
        supplyType: 'VMI',
        supplyKey: 'VMI-2026-0211',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 120,
        availableTime: iso(2 * 86400_000),
        description: 'VMI 供应商寄售'
      },
      {
        supplyType: 'PLANNING_PURCHASE_PLACEHOLDER',
        supplyKey: 'PO-2026-9982',
        isPlanningOnlyPlaceholder: true,
        isNotCommitted: true,
        qty: 30,
        availableTime: iso(20 * 86400_000),
        description: '估算日期，采购尚无正式承诺'
      },
      {
        supplyType: 'ARRIVED_NOT_INBOUND',
        supplyKey: 'ARN-2026-0078',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 30,
        availableTime: iso(-86400_000)
      }
    ],
    issues: [
      {
        issueType: 'CROSS_STAGE_RISK',
        message: '跨阶段分配数量 80，存在跨域交接风险',
        relatedSupplyKey: 'STG-XS'
      },
      {
        issueType: 'PLACEHOLDER_DEPENDENCY',
        message: '依赖 PLANNING_PURCHASE_PLACEHOLDER 30',
        relatedSupplyKey: 'PO-2026-9982'
      }
    ],
    updatedAt: iso(-10800_000)
  },

  {
    // PI-004 — ERP_REMAINING（早期阶段，未开始）
    productionInstructionId: 4,
    productionInstructionNo: 'PI-2026-0004',
    domainKey: 'FAMILY_INJECTION',
    materialCode: 'M-004',
    erpRemainingQty: 500,
    positionTotalQty: 0,
    variance: -500,
    mainPositionType: 'ERP_REMAINING',
    positions: [],
    supplies: [
      {
        supplyType: 'PLANNED_PRODUCTION',
        supplyKey: 'PP-2026-0099',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: true,
        qty: 500,
        availableTime: iso(45 * 86400_000),
        description: '尚未开工'
      }
    ],
    issues: [{ issueType: 'NO_PI', message: '无 PI 规划 Task — 不可下发 MES（U17）' }],
    updatedAt: iso(-14400_000)
  },

  {
    // PI-005 — PRODUCTION 主（单阶段，全生产中）
    productionInstructionId: 5,
    productionInstructionNo: 'PI-2026-0005',
    domainKey: 'FAMILY_ASSEMBLY',
    materialCode: 'M-005',
    erpRemainingQty: 180,
    positionTotalQty: 180,
    variance: 0,
    mainPositionType: 'PRODUCTION',
    positions: [
      {
        productionInstructionId: 5,
        productionInstructionNo: 'PI-2026-0005',
        materialId: 204,
        materialCode: 'M-005',
        positionType: 'PRODUCTION',
        stage: 'STG-ASM',
        positionQty: 180
      }
    ],
    supplies: [
      {
        supplyType: 'INVENTORY',
        supplyKey: 'INV-2026-1102',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 180,
        availableTime: iso(-3 * 86400_000),
        relatedOrderId: 105,
        relatedOrderNo: 'ORD-2026-0105'
      }
    ],
    issues: [],
    updatedAt: iso(-1800_000)
  },

  {
    // PI-006 — UNLOCATED 严重（极端案例）
    productionInstructionId: 6,
    productionInstructionNo: 'PI-2026-0006',
    domainKey: 'FAMILY_INJECTION',
    materialCode: 'M-006',
    erpRemainingQty: 100,
    positionTotalQty: 60,
    variance: -40,
    mainPositionType: 'UNLOCATED',
    positions: [
      {
        productionInstructionId: 6,
        productionInstructionNo: 'PI-2026-0006',
        materialId: 205,
        materialCode: 'M-006',
        positionType: 'UNLOCATED',
        unlocatedQty: 60,
        positionQty: 60,
        issue: '60% 数量未分配至任何位置'
      }
    ],
    supplies: [],
    issues: [
      {
        issueType: 'UNLOCATED_EXISTS',
        message: '存在 UNLOCATED 数量 60',
        relatedSupplyKey: 'PI-2026-0006'
      },
      { issueType: 'NO_PI', message: '无任何已确认 Supply 来源' }
    ],
    updatedAt: iso(-21600_000)
  },

  {
    // PI-007 — CROSS_STAGE 阶段间，混合 Supply（INVENTORY + RECEIVED + VMI）
    productionInstructionId: 7,
    productionInstructionNo: 'PI-2026-0007',
    domainKey: 'FAMILY_TEST',
    materialCode: 'M-007',
    erpRemainingQty: 250,
    positionTotalQty: 250,
    variance: 0,
    mainPositionType: 'CROSS_STAGE',
    positions: [
      {
        productionInstructionId: 7,
        productionInstructionNo: 'PI-2026-0007',
        materialId: 206,
        materialCode: 'M-007',
        positionType: 'CROSS_STAGE',
        stage: 'STG-XS',
        crossFactoryQty: 150,
        positionQty: 150
      },
      {
        productionInstructionId: 7,
        productionInstructionNo: 'PI-2026-0007',
        materialId: 206,
        materialCode: 'M-007',
        positionType: 'PRODUCTION',
        stage: 'STG-TEST',
        positionQty: 100
      }
    ],
    supplies: [
      {
        supplyType: 'RECEIVED',
        supplyKey: 'REC-2026-0455',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 100,
        availableTime: iso(-5 * 86400_000),
        relatedOrderId: 107,
        relatedOrderNo: 'ORD-2026-0107'
      },
      {
        supplyType: 'VMI',
        supplyKey: 'VMI-2026-0233',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 100,
        availableTime: iso(86400_000)
      },
      {
        supplyType: 'INVENTORY',
        supplyKey: 'INV-2026-1220',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: false,
        qty: 50,
        availableTime: iso(-86400_000)
      }
    ],
    issues: [],
    updatedAt: iso(-5400_000)
  },

  {
    // PI-008 — TRANSIT 主，含 PLANNED_PRODUCTION
    productionInstructionId: 8,
    productionInstructionNo: 'PI-2026-0008',
    domainKey: 'FAMILY_ASSEMBLY',
    materialCode: 'M-008',
    erpRemainingQty: 90,
    positionTotalQty: 90,
    variance: 0,
    mainPositionType: 'TRANSIT',
    positions: [
      {
        productionInstructionId: 8,
        productionInstructionNo: 'PI-2026-0008',
        materialId: 207,
        materialCode: 'M-008',
        positionType: 'TRANSIT',
        stage: 'STG-TR',
        transitQty: 90,
        positionQty: 90
      }
    ],
    supplies: [
      {
        supplyType: 'PLANNED_PRODUCTION',
        supplyKey: 'PP-2026-0142',
        isPlanningOnlyPlaceholder: false,
        isNotCommitted: true,
        qty: 90,
        availableTime: iso(10 * 86400_000),
        description: '计划生产（未开工）'
      }
    ],
    issues: [],
    updatedAt: iso(-2700_000)
  }
]

/* ===== list 摘要 ===== */
const toListItem = (d: PiPositionViewDto): PiListItem => {
  const unlocatedQty = d.positions.reduce((s, p) => s + (p.unlocatedQty ?? 0), 0)
  const placeholderQty = d.supplies
    .filter((s) => s.isPlanningOnlyPlaceholder)
    .reduce((sum, s) => sum + s.qty, 0)
  const hasUnlocated = unlocatedQty > 0
  const hasPlaceholderDependency = placeholderQty > 0
  const mesEligible =
    hasUnlocated || hasPlaceholderDependency
      ? 'INELIGIBLE'
      : d.positions.length === 0
        ? 'UNKNOWN'
        : 'ELIGIBLE'
  return {
    productionInstructionId: d.productionInstructionId,
    productionInstructionNo: d.productionInstructionNo,
    domainKey: d.domainKey,
    materialCode: d.materialCode,
    erpRemainingQty: d.erpRemainingQty,
    positionTotalQty: d.positionTotalQty,
    variance: d.variance,
    mainPositionType: d.mainPositionType,
    hasUnlocated,
    unlocatedQty,
    hasPlaceholderDependency,
    placeholderQty,
    supplyCount: d.supplies.length,
    mesEligible,
    issueCount: d.issues.length,
    updatedAt: d.updatedAt
  }
}

const applyFilter = (filter?: PiListFilter): PiListItem[] => {
  let arr = piListData.map(toListItem)
  if (!filter) return arr
  if (filter.domainKey && filter.domainKey !== 'ALL') {
    arr = arr.filter((p) => p.domainKey === filter.domainKey)
  }
  if (filter.positionType && filter.positionType !== 'ALL') {
    arr = arr.filter((p) => p.mainPositionType === filter.positionType)
  }
  if (filter.materialCode) {
    const q = filter.materialCode.toUpperCase()
    arr = arr.filter((p) => p.materialCode.includes(q))
  }
  return arr
}

export const piApi = {
  /** PI 列表（页面 7 入口）
   *  v1.4 §十六：5号位 PiPositionController
   *  - GET /api/pi-position          主数据列表（无 summary 路径上的 planVersionId 维度）
   *  - GET /api/pi-position/summary  按 domainKey/materialCode 聚合
   *  采用 list 端点；summary 后续按需扩展
   */
  async list(filter?: PiListFilter): Promise<PiListItem[]> {
    if (APS_USE_MOCK) return applyFilter(filter)
    return apsHttp.get<PiListItem[]>({
      url: '/api/pi-position/summary',
      params: filter as Record<string, unknown> | undefined
    })
  },

  /** PI Position 详情
   *  2026-09-17 5号位 回执后落地：
   *  - 端点 `GET /api/pi-position/{productionInstructionNo}?planVersionId=`
   *  - **路由参数是 productionInstructionNo（字符串，即生产指令号），不是数字 ID**
   *  - 响应 `List<PiPositionDto>`（该 PI 的所有 Position；多个 position 表示跨工序）
   *  - 前端取首个为详情（兼容多 Position 形态用 summary 合并展示）
   */
  async getById(productionInstructionNo: string): Promise<PiPositionViewDto> {
    if (APS_USE_MOCK) {
      const found = piListData.find((p) => p.productionInstructionNo === productionInstructionNo)
      if (!found) throw new Error(`[mock] 未知 PI ${productionInstructionNo}`)
      return found
    }
    const positions = await apsHttp.get<PiPositionViewDto[]>({
      url: `/api/pi-position/${encodeURIComponent(productionInstructionNo)}`
    })
    return positions[0]
  }
}
