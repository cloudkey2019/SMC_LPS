/**
 * Demand Protection 释放 DTO（页面 9 / P1-11）
 *
 * @see 4号位文档第 13 节（手工释放需求保护）
 * @see 审核报告 P1-11 / 4号位 → 5号位 → 2号位 链路
 *
 * 端点路径（10-09 lps/ 落地后，对齐 5号位 Controller 契约）：
 *  - GET    /api/demand-protection                       列表（per-lock 扁平，与后端 DemandProtectionDto 一一对应）
 *  - GET    /api/demand-protection/summary               汇总（KPI 4 卡：Total/Active/Released/Broken + TotalLockedQty）
 *  - POST   /api/demand-protection/release               释放（输入 lockIds + reason + actor）
 *
 * 后端契约（10-09 lps/ 落地后）：
 *  - 列表是**per-lock**扁平结构（一行 = 一个 DemandSupplyHardLock），5号位 不在 5号位 端聚合
 *  - 聚合（按 demandKey group by + PARTIALLY_RELEASED 派生）由**前端 store 完成**（见 5号位 R1 回执）
 *  - KPI 4 卡由 /summary 端点给权威数（DB COUNT/SUM）；前端不本地推算
 *
 * 4号位职责边界：
 *  - 不计算保护数量（lockedQty 由 5号位 真聚合在原始行；前端仅在聚合视图上求和）
 *  - 不计算 status 派生态（ACTIVE / RELEASED / PARTIALLY_RELEASED 由前端 store 聚合；详见 5号位 2026-10-09 函件 R1 回执）
 *  - 不写锁表（5号位转发给 2号位 `IDemandSupplyHardLockRepository.ReleaseLocksAsync`）
 *  - actor 仅作回显/审计参考，真实身份以 5号位 JWT 校验为准
 *
 * 复合聚合维度：按 DemandKey 聚合（一条 DemandProtectionDto = 一个 DemandKey 下全部 Lock 集合 + 派生总量 + 派生状态）。
 * 这样"释放"等价于"该 DemandKey 下全部 ACTIVE lockIds 的 Full Release"，与 2号位 `ReleaseLocksAsync(IEnumerable<long> lockIds, ...)` 1:1。
 *
 * 命名冲突警告：
 *  - `types/common.ts` 已导出 `SUPPLY_TYPES` / `SupplyType`（9 值，来自文档第 13 节 Supply 追溯口径）
 *  - 后端 Entity `DemandSupplyHardLock` 的 SupplyType 是 6 值集合（INVENTORY / WIP / PIPELINE / PI / PURCHASE_ORDER / VMI）
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
 * 聚合 DTO 自身的 status（由前端 store applyReleaseResultsToList 根据明细 lockDetail.status 派生）：
 *  - ACTIVE              全部 lockDetail.status = ACTIVE
 *  - RELEASED            全部 lockDetail.status = RELEASED
 *  - PARTIALLY_RELEASED  混合
 *
 * 注意：4号位 Full Release 后聚合 status 必为 RELEASED；PARTIALLY_RELEASED 仅用于展示历史数据。
 * 实际写入：[store/modules/aps/demandProtection.ts:172-174](frontNew/src/store/modules/aps/demandProtection.ts#L172-L174)。
 */
export type DemandProtectionStatus = 'ACTIVE' | 'RELEASED' | 'PARTIALLY_RELEASED'

export const DEMAND_PROTECTION_STATUS_LABELS: Record<DemandProtectionStatus, string> = {
  ACTIVE: '启用',
  RELEASED: '已释放',
  PARTIALLY_RELEASED: '部分释放'
}

/* ===== 接口 ===== */

/**
 * 后端 per-lock 原始 DTO（直映 lps/LPS.APS.Core/Dto/DemandProtectionDtos.cs DemandProtectionDto）
 *  - 一行 = 一个 DemandSupplyHardLock；后端不聚合
 *  - 5号位 全仓不存 `DemandProtectionStatus 4 态` 派生；这是前端 store 的事
 *  - 字段集合：Id / LockType / DemandType / DemandKey / SupplyType / SupplyKey / LockedQty / Status / CreatedAt/CreatedBy / ReleasedAt/ReleasedBy/ReleaseReason / SourcePlanVersionId / SourceAllocationSequence
 *  - 序列化方向：lps/ Program.cs JsonNamingPolicy.CamelCase（@see lps/LPS.APS.Web/Program.cs:93）→ 全部字段小写驼峰
 *
 * 4号位 视角：
 *  - store 取 BackendDemandProtectionRow[] 后按 demandKey group by → 构造 DemandProtectionDto（聚合）
 *  - /summary 端点回包是 DemandProtectionSummary
 */
export interface BackendDemandProtectionRow {
  /** 单锁 Id（与 lps Entity DemandSupplyHardLock.Id 对应） */
  id: number
  /** 锁类型：STRICT_BINDING / DEMAND_PROTECTION */
  lockType: HardLockType
  /** 需求类型（后端 string；前端窄化到已知枚举，未知串保留原始值） */
  demandType: DemandProtectionDemandType
  /** 需求键（聚合维度） */
  demandKey: string
  /** 供应类型（后端 string；前端窄化到 HardLockSupplyType） */
  supplyType: HardLockSupplyType
  /** 供应键 */
  supplyKey: string
  /** 单锁锁定数量 */
  lockedQty: number
  /** 后端 Status 3 枚举（5号位 全仓不存 PARTIALLY_RELEASED 派生态） */
  status: HardLockStatus
  /** 来源计划版本 ID（Entity SourcePlanVersionId） */
  sourcePlanVersionId?: number
  /** 来源分配序列（Entity SourceAllocationSequence） */
  sourceAllocationSequence?: number
  createdAt: IsoDateTime
  createdBy?: string
  /** 释放时间（仅 status=RELEASED 时有值） */
  releasedAt?: IsoDateTime
  releasedBy?: string
  releaseReason?: string
}

/**
 * /summary 端点回包（直映 lps/LPS.APS.Core/Dto/DemandProtectionDtos.cs DemandProtectionSummaryDto）
 *  - KPI 4 卡权威数（DB COUNT/SUM）：与筛选条件完全脱钩（summary = 全集）
 *  - items 上限 50 条 ACTIVE（@see lps/.../DemandProtectionRepository.cs GetSummaryAsync）
 */
export interface DemandProtectionSummary {
  /** 全集记录数（含 ACTIVE/RELEASED/BROKEN） */
  totalCount: number
  /** ACTIVE 行数 */
  activeCount: number
  /** RELEASED 行数（含 PARTIALLY_RELEASED 的源行） */
  releasedCount: number
  /** BROKEN 行数 */
  brokenCount: number
  /** ACTIVE 锁定数量 SUM */
  totalLockedQty: number
  /** 前 50 条 ACTIVE 行（详情 KPI 副展示用） */
  items: BackendDemandProtectionRow[]
}

/** Lock 明细（只读；用于聚合视图 Drawer 渲染；来自后端行字段投影） */
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
  /** 派生态（由前端 store 聚合，4号位不计算；见 §上方 DemandProtectionStatus 类型注释） */
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

/**
 * 列表筛选条件（10-09 落地，对齐 5号位 Controller QueryAsync 签名）
 *  - 全部可选；与 lps/LPS.APS.Application/Services/DemandProtectionAppService.cs QueryAsync 一一对应
 *  - 字段均传空时不带查询参数（后端默认 take=100 / skip=0）
 *  - demandKey / supplyKey 是 substring 模糊匹配（@see lps/.../DemandProtectionRepository.cs:48-49 LIKE '%xx%'）
 */
export interface DemandProtectionListFilter {
  /** 需求键 substring（例：ORDER_12345） */
  demandKey?: string
  /** 供应键 substring */
  supplyKey?: string
  /** 锁类型（单值；与 backend LockType 字面量一致） */
  lockType?: HardLockType
  /** 状态（单值；3 枚举；与 backend Status 字面量一致） */
  status?: HardLockStatus
  /** 分页 skip（默认 0） */
  skip?: number
  /** 分页 take（默认 100 / 上限 500；后端硬截断） */
  take?: number
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
