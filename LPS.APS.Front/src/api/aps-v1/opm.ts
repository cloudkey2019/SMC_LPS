/**
 * APS V1 4号位 — OperationPlanningMode（OPM）治理 API（2 端点）
 *
 * 3号位 2026-09-23 交付件（T1）：
 *   frontNew/docs/APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md
 *
 * 后端：lps/LPS.APS.Web/Controllers/GovernanceController.cs（OPM 段；3号位 已落库构建 0 错误）
 * 模型：lps/LPS.APS.Core/Domain/RoutingOperation.cs（OperationPlanningMode 列）
 *
 * 端点清单：
 *   1. GET  /api/governance/routing-operations?materialId={物料Id}
 *   2. PUT  /api/governance/routing-operations/{id}/planning-mode   body: { operationId, operationPlanningMode }
 *
 * 权限：
 *   - GET = aps.rule.view（复用 Rule 码，零权限种子变更）
 *   - PUT = aps.rule.edit（同上）
 *
 * 设计要点：
 *   - mock 模式：返回 1 个物料（materialId=mock 任意）下 ~6 条 RoutingOperation，覆盖三态
 *   - 写端点统一抛 503 防伪成功误导（dev APS_USE_MOCK=false 不走 mock）
 *   - 包装：`{ success: true, data: ... }`（3号位 交付件 §一/二）
 *   - 上层组件调 opmApi.* 即可，store + page 不必关心 unwrap / mock fallback
 */

import { apsHttp, APS_USE_MOCK, ApiError } from './http'
import type {
  RoutingOperationDto,
  UpdateOperationPlanningModeInput,
  OperationPlanningMode
} from './types/opm'

/* ==================== Mock Fixtures（仅离线 UI 演示） ==================== */

/** mock 模式固定物料 Id —— 在页面物料下拉里只能选这一个（mock 边界明示） */
const MOCK_MATERIAL_ID = 100

/** mock 模式下的固定 6 条 RoutingOperation（覆盖三态 + 不同工序号） */
const MOCK_OPERATIONS: RoutingOperationDto[] = [
  {
    id: 1,
    materialId: MOCK_MATERIAL_ID,
    productionDepartmentId: 3,
    routeCode: 'DEFAULT',
    pathId: 1,
    operationCode: 'OP10',
    operationName: '精修',
    processType: 'MACHINING',
    stageCode: 'SMT',
    operationPlanningMode: 'FINITE_RESOURCE'
  },
  {
    id: 2,
    materialId: MOCK_MATERIAL_ID,
    productionDepartmentId: 3,
    routeCode: 'DEFAULT',
    pathId: 1,
    operationCode: 'OP20',
    operationName: '清洗',
    processType: 'CLEANING',
    stageCode: 'SMT',
    operationPlanningMode: 'UNCONSTRAINED'
  },
  {
    id: 3,
    materialId: MOCK_MATERIAL_ID,
    productionDepartmentId: 3,
    routeCode: 'DEFAULT',
    pathId: 1,
    operationCode: 'OP30',
    operationName: '转运',
    processType: 'TRANSFER',
    stageCode: 'SMT',
    operationPlanningMode: 'WAIT_ONLY'
  },
  {
    id: 4,
    materialId: MOCK_MATERIAL_ID,
    productionDepartmentId: 5,
    routeCode: 'DEFAULT',
    pathId: 2,
    operationCode: 'OP40',
    operationName: '装配',
    processType: 'ASSEMBLY',
    stageCode: 'ASM',
    operationPlanningMode: 'FINITE_RESOURCE'
  },
  {
    id: 5,
    materialId: MOCK_MATERIAL_ID,
    productionDepartmentId: 5,
    routeCode: 'DEFAULT',
    pathId: 2,
    operationCode: 'OP50',
    operationName: '老化测试',
    processType: 'TESTING',
    stageCode: 'ASM',
    operationPlanningMode: 'WAIT_ONLY'
  },
  {
    id: 6,
    materialId: MOCK_MATERIAL_ID,
    productionDepartmentId: 7,
    routeCode: 'ALT',
    pathId: 3,
    operationCode: 'OP60',
    operationName: '包装',
    processType: 'PACKAGING',
    stageCode: 'PKG',
    operationPlanningMode: 'FINITE_RESOURCE'
  }
]

/* ==================== API ==================== */

export const opmApi = {
  /** 列出指定物料的全部 RoutingOperation（GET）
   *  - 必填 materialId（int > 0）；后端 400 校验
   *  - mock 模式：忽略 materialId 入参，直接返回 MOCK_OPERATIONS（mock 边界）
   */
  async listOperations(materialId: number): Promise<RoutingOperationDto[]> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 250))
      return MOCK_OPERATIONS
    }
    return apsHttp.get<RoutingOperationDto[]>({
      url: '/api/governance/routing-operations',
      params: { materialId }
    })
  },

  /** 更新单条 RoutingOperation 的 OPM（PUT）
   *  - body.operationId 与路径 {id} 必须一致（后端 400 校验）
   *  - operationPlanningMode 必须三态之一（后端 422 校验）
   *  - mock 模式：本地 mutate MOCK_OPERATIONS + 回包（**仅本地内存**，刷新后回到初值）
   */
  async updatePlanningMode(input: UpdateOperationPlanningModeInput): Promise<RoutingOperationDto> {
    if (APS_USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 200))
      const idx = MOCK_OPERATIONS.findIndex((op) => op.id === input.operationId)
      if (idx < 0) {
        throw new ApiError(`mock 无 RoutingOperation id=${input.operationId}`, 404)
      }
      const next: RoutingOperationDto = {
        ...MOCK_OPERATIONS[idx],
        operationPlanningMode: input.operationPlanningMode as OperationPlanningMode
      }
      MOCK_OPERATIONS[idx] = next
      return next
    }
    return apsHttp.put<RoutingOperationDto>({
      url: `/api/governance/routing-operations/${input.operationId}/planning-mode`,
      data: {
        operationId: input.operationId,
        operationPlanningMode: input.operationPlanningMode
      }
    })
  }
}

/** 暴露给页面：mock 模式默认物料 Id（页面"物料下拉"占位用，提示用户 mock 边界） */
export const MOCK_OPM_MATERIAL_ID = MOCK_MATERIAL_ID
