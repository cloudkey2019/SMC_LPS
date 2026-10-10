/**
 * APS V1 4号位 — RBAC 管理 DTO
 *
 * @owner 3号位（认证 / 用户 / 角色 / 权限 / 业务范围 / Domain 治理）
 * @see lps/LPS.APS.Core/Dto/RbacManagementDtos.cs
 * @see lps/LPS.APS.Web/Controllers/RbacController.cs（18 端点）
 *
 * 字段命名规范：
 *  - 后端 C# record PascalCase → JSON camelCase 绑定（如 UserName → userName）
 *  - 枚举值保留后端原值字符串（不双向转换），避免漂移
 *  - 所有可选字段用 `?:` + `| null`，匹配 Service 返回 null 的真实场景
 */

import type { IsoDateTime } from './common'

/* ==================== 枚举字面量 ==================== */

/** User.status（3号位 v1.0 §三.1 增量硬化：原 Active/Deleted 二态补 Disabled 为「已停用」） */
export type UserStatus = 'Active' | 'Disabled' | 'Deleted'
export const USER_STATUS_LABELS: Record<UserStatus, string> = {
  Active: '启用',
  Disabled: '已停用',
  Deleted: '已删除'
}

/** ScopeType 白名单（与 DTO CreateDataScopePolicyRequest 校验一致） */
export type ScopeType =
  | 'Factory'
  | 'ProductFamily'
  | 'Department'
  | 'Domain'
  | 'ResourceOrgGroup'
  | 'Global'

export const SCOPE_TYPES: readonly ScopeType[] = [
  'Factory',
  'ProductFamily',
  'Department',
  'Domain',
  'ResourceOrgGroup',
  'Global'
] as const

export const SCOPE_TYPE_LABELS: Record<ScopeType, string> = {
  Factory: '工厂',
  ProductFamily: '产品族',
  Department: '部门',
  Domain: '域',
  ResourceOrgGroup: '资源组织组',
  Global: '全局'
}

/* ==================== 响应 DTO ==================== */

/** GET /api/rbac/users 响应元素 */
export interface UserSummaryDto {
  id: number
  userCode: string
  userName: string
  email?: string | null
  phoneNumber?: string | null
  status: UserStatus
  lastLoginTime?: IsoDateTime | null
  createdAt: IsoDateTime
}

/** GET /api/rbac/roles 响应元素 */
export interface RoleSummaryDto {
  id: number
  roleCode: string
  roleName: string
  description?: string | null
  isSystemRole: boolean
  isActive: boolean
  createdAt: IsoDateTime
}

/** GET /api/rbac/permissions 响应元素 */
export interface PermissionSummaryDto {
  id: number
  permissionCode: string
  permissionName: string
  description?: string | null
  /** 权限模块（plan/ctp/candidate/rule/parameter/strategy/manual_eta/demand_protection/mes/auth/audit） */
  module: string
  actionType: string
  isActive: boolean
  createdAt: IsoDateTime
}

/** GET /api/rbac/scopes 响应元素 */
export interface DataScopePolicyDto {
  id: number
  scopeType: ScopeType
  scopeValue: string
  description?: string | null
  createdAt: IsoDateTime
}

/* ==================== 请求 DTO ==================== */

/** POST /api/rbac/users body */
export interface CreateUserRequest {
  userCode: string
  userName: string
  password: string
  email?: string | null
  phoneNumber?: string | null
}

/** PUT /api/rbac/users/{id} body — 全量覆盖，省略字段被置 null */
export interface UpdateUserRequest {
  userName: string
  status: UserStatus
  email?: string | null
  phoneNumber?: string | null
}

/** POST /api/rbac/roles body */
export interface CreateRoleRequest {
  roleCode: string
  roleName: string
  description?: string | null
}

/** PUT /api/rbac/roles/{id} body */
export interface UpdateRoleRequest {
  roleName: string
  isActive: boolean
  description?: string | null
}

/** POST /api/rbac/permissions body */
export interface CreatePermissionRequest {
  permissionCode: string
  permissionName: string
  module: string
  actionType: string
  description?: string | null
}

/** POST /api/rbac/scopes body */
export interface CreateDataScopePolicyRequest {
  scopeType: ScopeType
  scopeValue: string
  description?: string | null
}

/** PUT /api/rbac/{users|roles}/{id}/{roles|permissions|scopes} body
 *  - 覆盖式：DELETE 全表 → INSERT 当前数组
 *  - 数组空 = 清空全部；Ids 必须全部存在且启用（否则 400）
 */
export interface AssignIdsRequest {
  ids: number[]
}

/* ==================== 分页（3号位 v1.0 契约） ==================== */

/** RBAC 分页响应容器（v1.0 §二）
 *  - 注意：本类型刻意不复用 common.ts 的 PageResult<T>——3 号位 RBAC 后端契约字段名为 `page`
 *    （v1.0 §二 `PageResult<T>.Page`），与 common.ts 的 `pageIndex` 不一致；保留各自命名前端对齐各自后端
 *  - page 从 1 起；pageSize 默认 20，硬上限 200（超限截断）
 *  - 非法值（≤0 / 非整数）回落默认，宽容解析
 */
export interface RbacPageResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/** GET /api/rbac/users query（v1.0 §三.1） */
export interface ListUsersParams {
  page?: number
  pageSize?: number
  /** 模糊匹配 UserCode/UserName/Email */
  keyword?: string
  /** 精确匹配 Status（空=不过滤） */
  status?: UserStatus | ''
}

/** GET /api/rbac/roles query（v1.0 §三.2） */
export interface ListRolesParams {
  page?: number
  pageSize?: number
  /** 模糊匹配 RoleCode/RoleName */
  keyword?: string
  /** 精确过滤 IsSystemRole（空=不过滤） */
  isSystem?: boolean
}

/** GET /api/rbac/permissions query（v1.0 §三.3） */
export interface ListPermissionsParams {
  page?: number
  pageSize?: number
  /** 精确匹配 Module */
  module?: string
  /** 精确匹配 ActionType */
  actionType?: string
  /** 模糊匹配 PermissionCode/PermissionName */
  keyword?: string
}

/** GET /api/rbac/scopes query（v1.0 §三.4） */
export interface ListScopesParams {
  page?: number
  pageSize?: number
  /** 精确匹配 ScopeType */
  scopeType?: ScopeType
  /** 模糊匹配 ScopeValue/Description */
  keyword?: string
}

/* ==================== 批量删除（3号位 v1.0 §一.3 R4） ==================== */

/** POST /api/rbac/{users|roles|permissions|scopes}/batch-delete body */
export interface BatchDeleteRequest {
  ids: number[]
}

/** 批量删除响应：成功的 id + 失败明细 */
export interface BatchDeleteResult {
  succeeded: number[]
  failed: BatchDeleteFailure[]
}

/** 单条失败原因（v1.0 §一.3：kind+id+reason 三字段） */
export interface BatchDeleteFailure {
  kind: 'user' | 'role' | 'permission' | 'scope'
  id: number
  reason: string
}

/* ==================== 测试数据清理（3号位 v1.0 §一.1 R1） ==================== */

/** POST /api/rbac/test-data/cleanup 响应 */
export interface CleanupResult {
  users: number
  roles: number
  permissions: number
  /** 恒为 []（命中 0 条也成功返回） */
  failed: never[]
}
