/**
 * Demand Protection 释放 DTO（页面 9 / P1-11）
 *
 * @see 4号位文档第 13 节（手工释放需求保护）
 * @see 审核报告 P1-11 / 4号位 → 5号位 → 2号位 链路
 *
 * 端点路径（**前瞻性约定**，5号位 Controller 当前不存在；待落地后复核）：
 *  - GET  /api/demand-protection                       列表（list = preview：每条带 lockIds + lockedQty）
 *  - POST /api/demand-protection/release               释放（输入 demandKey + lockIds + reason + actor）
 *
 * 4号位职责边界：
 *  - 不计算保护数量（lockedQty 由 5号位聚合，4号位只展示）
 *  - 不计算 status 派生态（ACTIVE / RELEASED / PARTIALLY_RELEASED 由 5号位聚合）
 *  - 不写锁表（5号位转发给 2号位 `IDemandSupplyHardLockRepository.ReleaseLocksAsync`）
 *  - actor 仅作回显/审计参考，真实身份以 5号位 JWT 校验为准
 *
 * 复合聚合维度：按 DemandKey 聚合（一条 DemandProtectionDto = 一个 DemandKey 下全部 Lock 集合 + 派生总量 + 派生状态）。
 * 这样"释放"等价于"该 DemandKey 下全部 ACTIVE lockIds 的 Full Release"，与 2号位 `ReleaseLocksAsync(IEnumerable<long> lockIds, ...)` 1:1。
 *
 * 命名冲突警告：
 *  - `types/common.ts` 已导出 `SUPPLY_TYPES` / `SupplyType`（9 值，来自文档第 13 节 Supply 追溯口径）
 *  - 2号位 Entity `DemandSupplyHardLock` 的 SupplyType 是 6 值集合（INVENTORY / WIP / PIPELINE / PI / PURCHASE_ORDER / VMI）
 *  - 两者**不同集合**；`types/index.ts` 用 `export *` 再导出时同名会触发 TS2308，故用 `HARD_LOCK_SUPPLY_TYPES` / `HardLockSupplyType` 前缀隔离
 *  - `DemandType` 同样用 `DemandProtectionDemandType` 前缀避免与 common 中的同名标识冲突
 */

import type { IsoDateTime } from './common'

/* ===== 枚举（as const + 派生 union，对齐 common.ts TASK_STATUSES 风格） ===== */

/** 4号位文档第 13 节：需求类型 */
export const DEMAND_TYPES = ['ORDER', 'WORKSET', 'PI_STAGE_DEMAND'] as const
export type DemandProtectionDemandType = (typeof DEMAND_TYPES)[number]

export const DEMAND_TYPE_LABELS: Record<DemandProtectionDemandType, string> = {
  ORDER: '订单',
  WORKSET: '工作集',
  PI_STAGE_DEMAND: 'PI 阶段需求'
}

/** 2号位 Entity LockType：DEMAND_PROTECTION 是本期可释放对象；STRICT_BINDING 仅展示 */
export const LOCK_TYPES = ['DEMAND_PROTECTION', 'STRICT_BINDING'] as const
export type HardLockType = (typeof LOCK_TYPES)[number]

export const LOCK_TYPE_LABELS: Record<HardLockType, string> = {
  DEMAND_PROTECTION: '需求保护',
  STRICT_BINDING: '严格绑定'
}

/** 2号位 Entity Status */
export const HARD_LOCK_STATUSES = ['ACTIVE', 'RELEASED', 'BROKEN'] as const
export type HardLockStatus = (typeof HARD_LOCK_STATUSES)[number]

export const HARD_LOCK_STATUS_LABELS: Record<HardLockStatus, string> = {
  ACTIVE: '启用',
  RELEASED: '已释放',
  BROKEN: '已失效'
}

/**
 * 2号位 Entity SupplyType（6 值，与 common.ts 的 SupplyType 9 值不同！）
 * 用 `HARD_LOCK_` 前缀隔离，避免与 common.ts 的同名导出冲突。
 */
export const HARD_LOCK_SUPPLY_TYPES = [
  'INVENTORY',
  'WIP',
  'PIPELINE',
  'PI',
  'PURCHASE_ORDER',
  'VMI'
] as const
export type HardLockSupplyType = (typeof HARD_LOCK_SUPPLY_TYPES)[number]

export const HARD_LOCK_SUPPLY_TYPE_LABELS: Record<HardLockSupplyType, string> = {
  INVENTORY: '库存',
  WIP: '在制',
  PIPELINE: '在途',
  PI: 'PI',
  PURCHASE_ORDER: '采购单',
  VMI: 'VMI'
}

/**
 * 聚合 DTO 自身的 status（由 5号位根据明细 lockDetail.status 派生）：
 *  - ACTIVE              全部 lockDetail.status = ACTIVE
 *  - RELEASED            全部 lockDetail.status = RELEASED
 *  - PARTIALLY_RELEASED  混合
 *
 * 注意：4号位 Full Release 后聚合 status 必为 RELEASED；PARTIALLY_RELEASED 仅用于展示历史数据。
 */
export type DemandProtectionStatus = 'ACTIVE' | 'RELEASED' | 'PARTIALLY_RELEASED'

export const DEMAND_PROTECTION_STATUS_LABELS: Record<DemandProtectionStatus, string> = {
  ACTIVE: '启用',
  RELEASED: '已释放',
  PARTIALLY_RELEASED: '部分释放'
}

/* ===== 接口 ===== */

/** Lock 明细（只读） */
export interface DemandProtectionLockDetailDto {
  lockId: number
  supplyType: HardLockSupplyType
  supplyKey: string
  lockedQty: number
  status: HardLockStatus
  /** 来源分配序号（Entity SourceAllocationSequence） */
  sourceAllocationSequence?: number
}

/**
 * Demand Protection 聚合 DTO（按 DemandKey 聚合）
 *
 * 主键：`demandKey`
 */
export interface DemandProtectionDto {
  demandType: DemandProtectionDemandType
  /** 需求唯一键（聚合维度；Entity DemandKey 例：ORDER_12345_MAT001_F01） */
  demandKey: string
  /** 友好描述（前端增强字段；后端可选） */
  demandLabel?: string
  /** 工厂代码（前端增强字段；后端可选） */
  factory?: string
  /** 物料编码（前端增强字段；后端可选） */
  materialCode?: string
  lockType: HardLockType
  /** 当前 ACTIVE 的 lockIds 列表（可释放集合；释放时直接透传） */
  lockIds: number[]
  /** ACTIVE 明细 lockedQty 求和（由 5号位聚合，4号位不计算） */
  lockedQty: number
  /** 当前 lockDetails 长度 */
  lockCount: number
  /** 派生态（由 5号位聚合，4号位不计算） */
  status: DemandProtectionStatus
  /** Lock 明细全集（含 ACTIVE / RELEASED / BROKEN） */
  lockDetails: DemandProtectionLockDetailDto[]
  /** 来源计划版本 ID（Entity SourcePlanVersionId） */
  sourcePlanVersionId?: number
  createdAt: IsoDateTime
  createdBy: string
  /** 释放时间（仅 status=RELEASED 时有值） */
  releasedAt?: IsoDateTime
  /** 释放人 */
  releasedBy?: string
  /** 释放原因（仅 status=RELEASED 时有值） */
  releaseReason?: string
}

/** 列表筛选条件 */
export interface DemandProtectionListFilter {
  demandTypes?: DemandProtectionDemandType[]
  lockTypes?: HardLockType[]
  /** 仅返回 status='ACTIVE' */
  activeOnly?: boolean
}

/**
 * 释放输入（POST /api/demand-protection/release）
 *
 *  - lockIds 是 2号位 Service 的真实入参（5号位透传）
 *  - releaseReason 必填且非空（trim 后非空字符串；审计需要，无长度下限）
 *  - releasedBy 仅作回显参考；真实身份以 5号位 JWT 校验为准
 *  - **2026-09-13 冻结基线撤销 ≥ 5 字符限制**（无冻结业务规则依据）
 *
 * @see 5号位 `lps/LPS.APS.Core/Dto/DemandProtectionDtos.cs` DemandProtectionReleaseRequest
 */
export interface ReleaseDemandProtectionInput {
  lockIds: number[]
  releasedBy: string
  releaseReason: string
}

/** 释放结果（逐 Lock 一条，与请求 lockIds 一一对应；5号位 实现） */
export type ReleaseDemandProtectionStatus = 'RELEASED' | 'FAILED'

export interface DemandProtectionReleaseResult {
  /** Lock Id */
  lockId: number
  /** 需求标识（Lock 不存在时为空） */
  demandKey: string
  /** RELEASED=已释放 / FAILED=未释放（FailureReason 说明原因） */
  status: ReleaseDemandProtectionStatus
  /** 失败原因（仅 Status=FAILED 时非空） */
  failureReason?: string
}
