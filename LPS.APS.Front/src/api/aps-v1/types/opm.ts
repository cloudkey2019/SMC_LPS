/**
 * APS V1 4号位 — OperationPlanningMode（OPM）治理 API DTO
 *
 * 3号位 2026-09-23 交付件：
 *   frontNew/docs/APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md
 *
 * 后端：lps/LPS.APS.Web/Controllers/GovernanceController.cs（OPM 段）
 * 模型：lps/LPS.APS.Core/Domain/RoutingOperation.cs（OperationPlanningMode 列）
 *
 * 三态值域（0号位 2026-09-23 Q1 裁决）：
 *   FINITE_RESOURCE  需资源（人工有限产能）—— 正常资源找槽，无合格资源 → 业务 Unscheduled
 *   UNCONSTRAINED    无约束（无设备工序） —— 跳过资源，叠加 标准工时×数量 到工艺链最早位置
 *   WAIT_ONLY        仅等待/转运占位       —— 跳过资源，生成占位 Task（时间节点）
 *
 * 端点（2 个）：
 *   GET  /api/governance/routing-operations?materialId={物料Id}    权限 aps.rule.view
 *   PUT  /api/governance/routing-operations/{id}/planning-mode     权限 aps.rule.edit
 *
 * 维护边界（字段三来源原则 / 红线 #5 + #6）：
 *   - OPM 属 APS 治理属性（第三类），**不依赖 MES/ODS 供给**
 *   - sp_SyncRoutingData 不 MERGE OPM，治理值不被同步覆盖
 *   - 仅 DML（UPDATE 值），无 DDL
 */

/** OPM 三态值域（与 RoutingOperation.OperationPlanningMode 枚举对齐） */
export type OperationPlanningMode = 'FINITE_RESOURCE' | 'UNCONSTRAINED' | 'WAIT_ONLY'

/** 三态常量数组（用于遍历/校验） */
export const OPERATION_PLANNING_MODES = [
  'FINITE_RESOURCE',
  'UNCONSTRAINED',
  'WAIT_ONLY'
] as const satisfies readonly OperationPlanningMode[]

/** 三态中文标签 + Element Plus Tag 类型（与 Rules.vue / StrategyProfiles.vue 同套配色） */
export const OPM_META: Record<
  OperationPlanningMode,
  { label: string; tagType: 'success' | 'warning' | 'info'; description: string }
> = {
  FINITE_RESOURCE: {
    label: '需资源',
    tagType: 'success',
    description:
      '需资源（人工有限产能）；正常资源找槽；无合格资源 → 业务 Unscheduled（fail-closed）'
  },
  UNCONSTRAINED: {
    label: '无约束',
    tagType: 'info',
    description: '无约束（无设备工序）；跳过资源，叠加 标准工时×数量 到工艺链最早位置'
  },
  WAIT_ONLY: {
    label: '仅等待',
    tagType: 'warning',
    description: '仅等待/转运占位；跳过资源，生成占位 Task（时间节点）'
  }
}

/** 工序（RoutingOperation）DTO —— GET 列表 / PUT 单条回包 */
export interface RoutingOperationDto {
  id: number
  materialId: number
  productionDepartmentId: number
  routeCode: string
  pathId: number
  operationCode: string
  operationName: string
  processType: string
  stageCode: string
  /** OPM 当前值（三态之一；缺省默认 FINITE_RESOURCE） */
  operationPlanningMode: OperationPlanningMode
}

/** PUT 请求 body（路径 {id} 与 body.operationId 必须一致） */
export interface UpdateOperationPlanningModeInput {
  operationId: number
  operationPlanningMode: OperationPlanningMode
}
