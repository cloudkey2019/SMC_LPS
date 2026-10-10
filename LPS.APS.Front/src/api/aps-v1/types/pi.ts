/**
 * PI Position / 供给追溯 DTO（占位）
 *
 * @see 4号位文档第 13 节（页面 7：PI Position / 供给追溯）
 * 后端未实现，等 3 号位落地后对齐。
 *
 * 验收场景参考：
 *  - U16（PI UNLOCATED：显示数量并提示不可 MES）
 *  - U17（无 PI 规划 Task：显示不可 MES）
 *  - U18（Planning Placeholder 依赖 Task：不允许下 MES）
 */

import type { DomainKey, IsoDateTime, PiPositionType, SupplyType } from './common'

/** PI Position 一行（按 PI + Stage 维度） */
export interface PiPositionLine {
  /** 关联 PI */
  productionInstructionId: number
  productionInstructionNo: string
  materialId: number
  materialCode: string
  positionType: PiPositionType
  stage?: string
  /** 跨工厂（XC）数量 */
  crossFactoryQty?: number
  /** 在途数量 */
  transitQty?: number
  /** 等待数量 */
  waitingQty?: number
  /** UNLOCATED 数量（验收 U16；非 UNLOCATED 位置可省） */
  unlocatedQty?: number
  positionQty: number
  issue?: string
}

/** Supply 追溯（INVENTORY / PI / PO / VMI / ARRIVED_NOT_INBOUND / INTERPLANT_TRANSIT / RECEIVED / PLANNED_PRODUCTION / PLANNING_PURCHASE_PLACEHOLDER） */
export interface SupplyTraceLine {
  supplyType: SupplyType
  supplyKey: string
  /** 是否 Planning-only Placeholder（U18：明显标记 ESTIMATED/NOT_COMMITTED） */
  isPlanningOnlyPlaceholder: boolean
  /** 是否 NOT_COMMITTED */
  isNotCommitted: boolean
  qty: number
  availableTime?: IsoDateTime
  relatedOrderId?: number
  relatedOrderNo?: string
  description?: string
}

/** PiPositionViewDto — PI Position / 供给追溯（页面 7） */
export interface PiPositionViewDto {
  productionInstructionId: number
  productionInstructionNo: string
  /** 适用 Domain */
  domainKey: DomainKey
  /** 主物料 */
  materialCode: string
  /** ERP 剩余数量（用于对比 Position 合计） */
  erpRemainingQty: number
  /** Position 合计（=SUM(PositionQty)） */
  positionTotalQty: number
  /** Position vs ERP 差异（=PositionTotal - ErpRemaining） */
  variance: number
  /** 主位置类型（数量最多的 Position 类型） */
  mainPositionType: PiPositionType
  /** Position 明细 */
  positions: PiPositionLine[]
  /** Supply 来源 */
  supplies: SupplyTraceLine[]
  /** 数据问题列表 */
  issues: Array<{
    issueType: PiIssueType
    message: string
    relatedSupplyKey?: string
  }>
  updatedAt: IsoDateTime
}

/** PI 数据问题类型 */
export type PiIssueType =
  | 'UNLOCATED_EXISTS'
  | 'NO_PI'
  | 'PLACEHOLDER_DEPENDENCY'
  | 'CROSS_STAGE_RISK'
  | 'OTHER'

/** PI 列表项（摘要字段） */
export interface PiListItem {
  productionInstructionId: number
  productionInstructionNo: string
  domainKey: DomainKey
  materialCode: string
  erpRemainingQty: number
  positionTotalQty: number
  variance: number
  mainPositionType: PiPositionType
  /** 是否有 UNLOCATED（U16） */
  hasUnlocated: boolean
  unlocatedQty: number
  /** 是否依赖 PLANNING_PURCHASE_PLACEHOLDER（U18） */
  hasPlaceholderDependency: boolean
  placeholderQty: number
  /** Supply 数量 */
  supplyCount: number
  /** 是否可下发 MES（推断：无 UNLOCATED + 无 Placeholder 依赖 = ELIGIBLE） */
  mesEligible: 'ELIGIBLE' | 'INELIGIBLE' | 'UNKNOWN'
  /** Issues 数 */
  issueCount: number
  updatedAt: IsoDateTime
}

/** PI 列表过滤 */
export interface PiListFilter {
  domainKey?: DomainKey | 'ALL'
  positionType?: PiPositionType | 'ALL'
  materialCode?: string
}
