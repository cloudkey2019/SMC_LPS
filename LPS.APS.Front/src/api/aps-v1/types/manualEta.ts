/**
 * Manual ETA DTO（与 5 号位 `lps/LPS.APS.Core/Dto/ProcurementManualEtaOverride.cs` 对齐）
 *
 * 来源：5号位 `ProcurementManualEtaController`（4号位 → 5号位）
 *  - GET    /api/procurement-manual-eta        Query，支持 materialIds / poNos / activeOnly
 *  - GET    /api/procurement-manual-eta/{poNo}/{lineNo}
 *  - POST   /api/procurement-manual-eta        Upsert
 *  - DELETE /api/procurement-manual-eta/{poNo}/{lineNo}?actor=
 *
 * 4号位职责（@see 4号位文档第 11 节 / 审核报告 P1-7）：
 *  - 查询 / 新增 / 更新 / 取消
 *  - Active 状态 / UpdatedBy / UpdatedAt / Remark
 *  - 不计算 有效到货时间（Effective ETA）/ 最终可用时间（AvailableTime）
 *
 * 复合主键：poNo + lineNo
 *
 * ❌ 2026-09-15 撤销 DepartmentCode 维度（@see 4号位-2026-09-13-裁定回退清单.md）：
 *  - 9月13日 `未命名的Markdown文件.md` 实际是 0号位 出的业务裁决（程序有效）
 *  - DepartmentCode 整条撤销：DTO + 5号位 Repository/Controller/Scope + 前端 4 处 UI
 *  - 当前状态：与 9月13日 撤销状态一致
 */

import type { IsoDateTime } from './common'

/** Manual ETA 单条（与 5号位 `ProcurementManualEtaOverride` 字段一一对应） */
export interface ManualEtaDto {
  /** 采购订单号（主键之一） */
  poNo: string
  /** PO 行号（主键之一） */
  lineNo: number
  /** 物料 ID（5号位推断；前端展示用） */
  materialId: number
  /** 物料编码 */
  materialCode: string
  /** 物料名称（前端增强字段；后端可选） */
  materialName?: string
  /** 收货仓库（前端增强字段；后端可选） */
  receivingWarehouse?: string
  /**
   * 人工设定的到货预期时间（5号位 ManualEta 注释："人工设定的到货预期时间"）
   *  - 仅人工值；有效到货时间由 5号位计算（4号位不计算）
   */
  manualEta: IsoDateTime
  /** 是否激活；取消 = false */
  isActive: boolean
  updatedBy: string
  updatedAt: IsoDateTime
  createdBy?: string
  createdAt?: IsoDateTime
  remark?: string
}

/** 列表筛选条件（与 5号位 Controller Query 参数对齐） */
export interface ManualEtaListFilter {
  poNos?: string[]
  materialIds?: number[]
  /**
   * 物料编码筛选（10-07 ME10 修复：UI 物料下拉选 code → 后端 ?materialCodes= 逗号分隔）
   *  - 后端 `ProcurementManualEtaController.cs:47` 接受 `?materialCodes=` 逗号分隔字符串
   *  - 与 materialIds 二选一：UI 选 code（语义友好），materialIds 保留供后续按需扩展
   */
  materialCodes?: string[]
  /** 仅返回 isActive=true */
  activeOnly?: boolean
  /** 分页：1 基页码（5号位 2026-10-08 分页落地） */
  pageIndex?: number
  /** 分页：每页条数；后端硬上限 200 */
  pageSize?: number
}

/**
 * Upsert 输入（POST /api/procurement-manual-eta）
 *  - 5号位通过 poNo + lineNo 复合主键判定 新增 / 更新
 *  - 后端实体 ProcurementManualEtaOverride 必填 MaterialId/ReceivingWarehouse（@see lps/LPS.APS.Core/Dto/ProcurementManualEtaOverride.cs）
 *  - isActive 默认 true（新增时），更新时若省略则保留原值
 *  - actor → 后端 UpdatedBy（新增时若空则用 actor 作为 CreatedBy）
 */
export interface ManualEtaUpsertInput {
  poNo: string
  lineNo: number
  /** 物料 ID（后端必填；前端从已存在记录取，或由 PO 主数据接口获取） */
  materialId: number
  /** 物料编码（前端展示用，后端必填但允许重复） */
  materialCode?: string
  /** 收货仓库（后端必填） */
  receivingWarehouse: string
  manualEta: IsoDateTime
  isActive?: boolean
  remark?: string
  /** 当前操作人（→ 后端 UpdatedBy） */
  actor: string
}

/** 取消 ETA 输入（DELETE /api/procurement-manual-eta/{poNo}/{lineNo}）
 *  - 后端 [FromBody] CancelManualEtaRequest（@see ProcurementManualEtaController.cs L175-237）必填 materialId/receivingWarehouse/updatedBy
 *  - 4号位不"硬删除"：通过 isActive=false 实现取消
 *  - materialId/receivingWarehouse 从 list 当前行数据拉取（无需额外 GET）
 */
export interface ManualEtaCancelInput {
  poNo: string
  lineNo: number
  materialId: number
  receivingWarehouse: string
  /** 当前操作人（→ 后端 updatedBy） */
  actor: string
}

/** 收货仓库下拉项（5号位 ManualEta 控制器 GET /api/procurement-manual-eta/warehouses）
 *  - 数据源 ext_MES_ProcessCode_View（processcode → code / processname → name）
 *  - 复用 ManualEtaView 权限；不带 ID
 */
export interface WarehouseOption {
  code: string
  name: string
}

/** ManualETA 列表分页响应（5号位 2026-10-08 落地；@see ProcurementManualEtaController.cs）
 *  - 注意：刻意不复用 common.ts 的 PageResult<T>——5号位 契约字段为 `page`（与 RbacPageResult 同模式）
 *  - page 从 1 起；pageSize 默认 20，硬上限 200（@see §一）
 */
export interface ManualEtaPageResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}
