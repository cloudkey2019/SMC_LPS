/**
 * APS V1 4号位 — 审计日志 DTO（U42 / §22.6 Audit / §二十四 人工操作审计）
 *
 * @owner 3号位（统一认证/权限审计能力；业务动作审计由各接口 Owner 提交）
 * @see lps/LPS.APS.Core/Entities/Auth/AuditLog.cs（实体 21 字段，权威源）
 * @see lps/LPS.APS.Web/Controllers/RbacController.cs L170-181（GET /api/rbac/audit-logs）
 * @see lps/LPS.APS.Engine/Repositories/Auth/AuditLogRepository.cs L106-145（QueryPagedAsync）
 *
 * 契约要点（2026-09-20 curl 实测 + 代码走读确认）：
 *  - 响应壳 ApiResponse<IReadOnlyList<AuditLog>>：data 是**纯数组，无 total** → 前端只能用 hasMore 分页
 *  - 过滤参数 userId / action / from / to 可空自由组合；action 是**精确等值匹配**（非模糊）
 *  - 排序固定 OccurredAt 倒序；size 后端 clamp 到 1..200
 *  - ⚠️ 端点**不支持 result 过滤参数**（页面只做 Result 列展示，不做筛选，避免客户端假过滤误导）
 *  - Login/Logout 审计已落地（09-20 lps 二次提交：AuthService.WriteAuthAuditAsync，
 *    Module="Auth" / EntityType="User" / EntityId=userCode / errorMessage 仅原因类别，U42 脱敏）
 *
 * 字段命名规范（与 types/rbac.ts 一致）：
 *  - 后端 C# PascalCase → JSON camelCase（如 ActionCode → actionCode）
 *  - 所有可空字段用 `?:` + `| null`
 */

import type { IsoDateTime } from './common'

/* ==================== 响应 DTO ==================== */

/** GET /api/rbac/audit-logs 响应元素（AuditLog 实体全 21 字段） */
export interface AuditLogDto {
  /** 主键（long） */
  id: number
  /** 操作人用户 Id（可空：系统动作/已删用户） */
  userId?: number | null
  /** 操作人 UserCode（Actor 展示首选；空时回退 `#userId`） */
  userCode?: string | null
  /** 动作码（自由字符串；实际枚举见 AUDIT_ACTION_CODES） */
  actionCode: string
  /** 所属模块（如 auth / governance / run） */
  module?: string | null
  /** 对象类型 ObjectType（§二十四 7 项之一） */
  entityType?: string | null
  /** 对象 Id ObjectId（字符串：可能是数值 id 也可能是业务码） */
  entityId?: string | null
  /** 版本码（治理发布类动作关联 RuleSetVersion 等） */
  versionCode?: string | null
  /** 变更前值（部分写入方存 JSON 文本；U42：后端写入点已确认不含密码/Token） */
  oldValue?: string | null
  /** 变更后值（同上） */
  newValue?: string | null
  /** 结果（默认 "Success"；失败写入方自定，如 "Failed"） */
  result: string
  /** 发生时间（UTC 写入，序列化为 ISO 字符串） */
  occurredAt: IsoDateTime
  /** 客户端 IP */
  clientIp?: string | null
  /** User-Agent */
  userAgent?: string | null
  /** 备注 / 原因 Remark（§二十四「Remark/Reason（必要时）」） */
  remark?: string | null
  /** 关联 PlanVersion Id */
  planVersionId?: number | null
  /** 批次号 */
  batchNo?: string | null
  /** 关联审批 Id（V1 审批未冻结，通常为空） */
  approvalId?: number | null
  /** 请求原文（写入方选择性记录） */
  requestData?: string | null
  /** 响应原文（写入方选择性记录） */
  responseData?: string | null
  /** 错误消息（result 非 Success 时） */
  errorMessage?: string | null
}

/* ==================== 查询入参 ==================== */

/** GET /api/rbac/audit-logs query（4 过滤参数可空自由组合 + 分页） */
export interface AuditLogQuery {
  /** 操作人用户 Id（精确匹配） */
  userId?: number | null
  /** 动作码（后端**精确等值**匹配，非模糊） */
  action?: string | null
  /** 起始时间（含，OccurredAt >= from） */
  from?: IsoDateTime | null
  /** 截止时间（含，OccurredAt <= to） */
  to?: IsoDateTime | null
  /** 页码，从 1 起（默认 1） */
  page?: number
  /** 每页条数（默认 20，后端 clamp 1..200） */
  size?: number
}

/* ==================== 枚举与中文映射 ==================== */

/**
 * 实际 actionCode 枚举（2026-09-20 后端代码走读去重，共 15 个）：
 *  - AuthService：Login / Logout（09-20 二次提交落地，Module="Auth"，U42 脱敏）
 *  - GovernanceVersionService：Publish / Disable
 *  - RunLifecycleService：ConfirmCandidate / CreateCandidateRun / ActivateCandidate / RecoverFailedRun
 *  - DomainDefinitionGovernanceService：Create / Update / Enable / Disable
 *  - SetupTransitionRuleService：Create / Update / Delete
 *  - RbacManagementService：Create / Update / Delete / AssignRoles / AssignPermissions / AssignScopes / Disable
 */
export const AUDIT_ACTION_CODES = [
  'Login',
  'Logout',
  'Create',
  'Update',
  'Delete',
  'Enable',
  'Disable',
  'Publish',
  'AssignRoles',
  'AssignPermissions',
  'AssignScopes',
  'ConfirmCandidate',
  'ActivateCandidate',
  'CreateCandidateRun',
  'RecoverFailedRun'
] as const

export type AuditActionCode = (typeof AUDIT_ACTION_CODES)[number]

export const AUDIT_ACTION_LABELS: Record<string, string> = {
  Login: '登录',
  Logout: '登出',
  Create: '创建',
  Update: '修改',
  Delete: '删除',
  Enable: '启用',
  Disable: '停用/退役',
  Publish: '发布',
  AssignRoles: '分配角色',
  AssignPermissions: '分配权限',
  AssignScopes: '分配业务范围',
  ConfirmCandidate: '确认 Candidate',
  ActivateCandidate: '激活 Candidate',
  CreateCandidateRun: '发起 Candidate 试算',
  RecoverFailedRun: '恢复 FAILED Run'
}

/** Result 中文映射（后端自由字符串，未知值原样展示） */
export const AUDIT_RESULT_LABELS: Record<string, string> = {
  Success: '成功',
  Failed: '失败'
}

/**
 * 审计对象类型中文映射（4号位文档第 17 节 AuditObjectType）
 * 后端 entityType 为自由字符串，未知值原样展示。
 */
export const AUDIT_ENTITY_TYPE_LABELS: Record<string, string> = {
  CANDIDATE: '候选版本',
  DEMAND_PROTECTION: '需求保护',
  RULE_VERSION: '规则版本',
  PARAMETER_VERSION: '参数版本',
  MANUAL_RESCHEDULE: '人工重排',
  MES_DISPATCH: 'MES 下发'
}
