/**
 * APS V1 4号位 — 人工到货时间（Manual ETA）API
 *
 * @owner 5号位自属（Manual ETA 业务事实，4号位 → 5号位 链路）
 * @see 审核报告 §十六.4↔5 + §十一 P1-7
 *
 * @see 4号位文档第 11 节（页面 8 — Manual ETA）
 * @see 5号位 `lps/LPS.APS.Web/Controllers/ProcurementManualEtaController.cs`
 * 验收：P1-7（审核报告第 11 节）
 *
 * 接口路径（与 5 号位 Controller 一致）：
 *  - GET    /api/procurement-manual-eta                          列表（Query 筛选）
 *  - GET    /api/procurement-manual-eta/{poNo}/{lineNo}          单条
 *  - POST   /api/procurement-manual-eta                          Upsert
 *  - DELETE /api/procurement-manual-eta/{poNo}/{lineNo}?actor=   取消（isActive=false）
 *
 * 4号位职责边界：
 *  - 不计算 Effective ETA / AvailableTime
 *  - 取消走"软删除"isActive=false，不硬删
 *
 * ❌ 2026-09-15 撤销 DepartmentCode 维度（@see 4号位-2026-09-13-裁定回退清单.md）：
 *  - 9月13日 `未命名的Markdown文件.md` 实际是 0号位 出的业务裁决（程序有效）
 *  - DepartmentCode 整条撤销：mock 数据 + filter 块 + 前端 4 处 UI + verify-integration [F] 段
 *  - 当前状态：与 9月13日 撤销状态一致
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type {
  ManualEtaDto,
  ManualEtaListFilter,
  ManualEtaUpsertInput,
  ManualEtaCancelInput
} from './types/manualEta'

const iso = (ms: number) => new Date(Date.now() + ms).toISOString()
const ACTOR_MOCK = 'mock-pmc'

/* ===== Mock 数据：8 条记录（覆盖 INJECTION/MACHINING/ASSEMBLY 域 + 1 条已取消） ===== */
const MOCK_ETAS: ManualEtaDto[] = [
  {
    poNo: 'PO-2026-1001',
    lineNo: 1,
    materialId: 200,
    materialCode: 'M-001',
    materialName: '电机壳体-A',
    receivingWarehouse: 'WH-SUZ-01',
    manualEta: iso(5 * 86400_000),
    isActive: true,
    updatedBy: 'pmc01',
    updatedAt: iso(-2 * 86400_000),
    createdBy: 'pmc01',
    createdAt: iso(-30 * 86400_000),
    remark: '供应商口头承诺 9 月初'
  },
  {
    poNo: 'PO-2026-1001',
    lineNo: 2,
    materialId: 201,
    materialCode: 'M-002',
    materialName: '机柜上盖-B',
    receivingWarehouse: 'WH-SUZ-01',
    manualEta: iso(7 * 86400_000),
    isActive: true,
    updatedBy: 'pmc01',
    updatedAt: iso(-1 * 86400_000),
    remark: '二次确认 ETA'
  },
  {
    poNo: 'PO-2026-1002',
    lineNo: 1,
    materialId: 202,
    materialCode: 'M-003',
    materialName: '控制箱-C',
    receivingWarehouse: 'WH-CDG-02',
    manualEta: iso(-1 * 86400_000), // 已过期（演示异常态）
    isActive: true,
    updatedBy: 'pmc02',
    updatedAt: iso(-3 * 86400_000),
    remark: '供应商延迟发货，正在催交'
  },
  {
    poNo: 'PO-2026-1003',
    lineNo: 1,
    materialId: 203,
    materialCode: 'M-004',
    materialName: '标准泵-D',
    receivingWarehouse: 'WH-SUZ-01',
    manualEta: iso(10 * 86400_000),
    isActive: true,
    updatedBy: 'pmc01',
    updatedAt: iso(-5 * 86400_000),
    remark: ''
  },
  {
    poNo: 'PO-2026-1004',
    lineNo: 1,
    materialId: 204,
    materialCode: 'M-005',
    materialName: '包装套件-E',
    receivingWarehouse: 'WH-CDG-02',
    manualEta: iso(15 * 86400_000),
    isActive: false, // 已取消
    updatedBy: 'pmc02',
    updatedAt: iso(-1 * 86400_000),
    remark: '改用替代供应商，保留记录便于审计'
  },
  {
    poNo: 'PO-2026-1005',
    lineNo: 1,
    materialId: 205,
    materialCode: 'M-006',
    materialName: '机架焊接件-F',
    receivingWarehouse: 'WH-SUZ-01',
    manualEta: iso(3 * 86400_000),
    isActive: true,
    updatedBy: 'pmc01',
    updatedAt: iso(-12 * 3600_000),
    remark: '走加急通道'
  },
  {
    poNo: 'PO-2026-1006',
    lineNo: 1,
    materialId: 206,
    materialCode: 'M-007',
    materialName: '电路板组件-G',
    receivingWarehouse: 'WH-SUZ-01',
    manualEta: iso(20 * 86400_000),
    isActive: true,
    updatedBy: 'pmc02',
    updatedAt: iso(-7 * 86400_000),
    remark: ''
  },
  {
    poNo: 'PO-2026-1007',
    lineNo: 1,
    materialId: 207,
    materialCode: 'M-001',
    materialName: '电机壳体-A',
    receivingWarehouse: 'WH-CDG-02',
    manualEta: iso(8 * 86400_000),
    isActive: true,
    updatedBy: 'pmc01',
    updatedAt: iso(-4 * 86400_000),
    remark: '跨工厂调拨'
  }
]

/* ===== Mock 辅助：筛选 ===== */
function applyFilter(list: ManualEtaDto[], filter?: ManualEtaListFilter): ManualEtaDto[] {
  if (!filter) return [...list]
  let result = [...list]
  if (filter.activeOnly) result = result.filter((x) => x.isActive)
  if (filter.poNos?.length) result = result.filter((x) => filter.poNos!.includes(x.poNo))
  if (filter.materialIds?.length)
    result = result.filter((x) => filter.materialIds!.includes(x.materialId))
  return result
}

/** 真实模式序列化：把数组转成逗号分隔字符串（后端 Query 参数约定）
 *  @see lps/LPS.APS.Web/Controllers/ProcurementManualEtaController.cs L46-91
 *  后端用 [FromQuery] string? + Split(',') 拆，axios 默认会把数组序列化为 materialIds[]=1&materialIds[]=2 → 404
 */
function serializeFilter(filter?: ManualEtaListFilter): Record<string, unknown> {
  if (!filter) return {}
  const out: Record<string, unknown> = {}
  if (filter.poNos?.length) out.poNos = filter.poNos.join(',')
  if (filter.materialIds?.length) out.materialIds = filter.materialIds.join(',')
  if (filter.activeOnly !== undefined) out.activeOnly = filter.activeOnly
  return out
}

export const manualEtaApi = {
  /** 列表（支持 materialIds / poNos / activeOnly 筛选）
   *  v1.4 §十七：5号位 Query 端点；数组参数需逗号分隔字符串
   */
  async list(filter?: ManualEtaListFilter): Promise<ManualEtaDto[]> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 250))
      return applyFilter(MOCK_ETAS, filter)
    }
    return apsHttp.get<ManualEtaDto[]>({
      url: '/api/procurement-manual-eta',
      params: serializeFilter(filter)
    })
  },

  /** 单条：按 poNo + lineNo 复合主键 */
  async getByPoLine(poNo: string, lineNo: number): Promise<ManualEtaDto | null> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 150))
      return MOCK_ETAS.find((x) => x.poNo === poNo && x.lineNo === lineNo) ?? null
    }
    return apsHttp.get<ManualEtaDto>({
      url: `/api/procurement-manual-eta/${encodeURIComponent(poNo)}/${lineNo}`
    })
  },

  /**
   * Upsert：poNo + lineNo 复合主键去重
   *  - 后端 ProcurementManualEtaController.Upsert 接收 [FromBody] ProcurementManualEtaOverride
   *    必填 MaterialId/ReceivingWarehouse/ManualEta/IsActive/UpdatedBy/CreatedBy/Remark
   *    （@see lps/LPS.APS.Web/Controllers/ProcurementManualEtaController.cs L150-169）
   *  - 前端 upsert payload 字段映射：
   *      actor → updatedBy
   *      新增时用 actor 作 createdBy（首次创建）
   *  - MaterialId/ReceivingWarehouse 由前端 Dialog 收集（已知 PO 行时直接取，已列表存在行时取列表值）
   */
  async upsert(input: ManualEtaUpsertInput): Promise<ManualEtaDto> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 300))
      const idx = MOCK_ETAS.findIndex((x) => x.poNo === input.poNo && x.lineNo === input.lineNo)
      const now = new Date().toISOString()
      const next: ManualEtaDto = {
        poNo: input.poNo,
        lineNo: input.lineNo,
        materialId: input.materialId || 999,
        materialCode: input.materialCode ?? 'M-?',
        manualEta: input.manualEta,
        isActive: input.isActive ?? true,
        updatedBy: input.actor,
        updatedAt: now,
        remark: input.remark
      }
      if (idx >= 0) {
        // 保留 createdBy/createdAt/materialName
        next.materialCode = MOCK_ETAS[idx].materialCode
        next.materialName = MOCK_ETAS[idx].materialName
        next.receivingWarehouse =
          input.receivingWarehouse || MOCK_ETAS[idx].receivingWarehouse || ''
        next.createdBy = MOCK_ETAS[idx].createdBy
        next.createdAt = MOCK_ETAS[idx].createdAt
        MOCK_ETAS[idx] = next
      } else {
        next.receivingWarehouse = input.receivingWarehouse || ''
        next.createdBy = input.actor
        next.createdAt = now
        MOCK_ETAS.push(next)
      }
      return next
    }
    // 真实模式：直接传 ProcurementManualEtaOverride 形态（前后端 DTO 字段命名差异按 ASP.NET 默认约定）
    //  - 后端用 PascalCase 接收（ProcurementManualEtaOverride.PONo/LineNo/MaterialId/...）
    //  - axios 默认按字段名直接序列化（不改大小写），所以需要前端 payload 用 PascalCase key
    //    但 ManualEtaUpsertInput 是 camelCase；手动映射避免反序列化器属性丢失
    const payload = {
      PONo: input.poNo,
      LineNo: input.lineNo,
      MaterialId: input.materialId,
      MaterialCode: input.materialCode ?? '',
      ReceivingWarehouse: input.receivingWarehouse,
      ManualEta: input.manualEta,
      IsActive: input.isActive ?? true,
      UpdatedBy: input.actor,
      Remark: input.remark ?? ''
    }
    return apsHttp.post<ManualEtaDto>({
      url: '/api/procurement-manual-eta',
      data: payload
    })
  },

  /**
   * 取消：isActive=false（软删除）
   *  - 保留记录便于审计（4号位文档第 11 节硬约束）
   *  - 后端 Cancel 端点 [FromBody] CancelManualEtaRequest 必填 MaterialId/ReceivingWarehouse/UpdatedBy/Reason?
   *    （@see ProcurementManualEtaController.cs L175-237）
   *  - materialId/receivingWarehouse 由调用方从当前行数据传入（已在 list 中）
   */
  async cancel(input: ManualEtaCancelInput): Promise<ManualEtaDto> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 250))
      const idx = MOCK_ETAS.findIndex((x) => x.poNo === input.poNo && x.lineNo === input.lineNo)
      if (idx < 0) {
        throw new Error(`未找到 Manual ETA：${input.poNo} / Line ${input.lineNo}`)
      }
      MOCK_ETAS[idx] = {
        ...MOCK_ETAS[idx],
        isActive: false,
        updatedBy: input.actor,
        updatedAt: new Date().toISOString()
      }
      return MOCK_ETAS[idx]
    }
    return apsHttp.delete<ManualEtaDto>({
      url: `/api/procurement-manual-eta/${encodeURIComponent(input.poNo)}/${input.lineNo}`,
      data: {
        MaterialId: input.materialId,
        ReceivingWarehouse: input.receivingWarehouse,
        UpdatedBy: input.actor
      }
    })
  }
}

/** 默认 actor（mock 阶段简化；真实接入时从 apsAuth.userInfo 取） */
export const mockManualEtaActor = ACTOR_MOCK
