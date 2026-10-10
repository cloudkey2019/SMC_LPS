/**
 * APS V1 4号位 — RBAC 管理 API（17 写 + 4 读回 + 4 列表分页 + 4 批量删 + 1 清理 = 30 端点）
 *
 * @owner 3号位（认证 / 用户 / 角色 / 权限 / 业务范围）
 * @see lps/LPS.APS.Web/Controllers/RbacController.cs
 * @see frontNew/src/api/aps-v1/types/rbac.ts
 * @see 3号位致4号位_RBAC列表分页接口契约_v1.0_20260928.md
 * @see 3号位致4号位_RBAC管理4页面后端落地回执_v1.0_20260928.md
 *
 * 设计要点：
 *  - 4 个 listXxx 改为分页（v1.0 契约 §二：page/pageSize + PageResult<T>）
 *  - 4 读回端点（getUserRoles/getUserScopes/getRolePermissions/getRoleScopes）维持扁平 IReadOnlyList
 *  - mock 模式仅给读端点（listXxx + 4 读回）极小 fixture；写端点统一 403
 *  - R1 cleanupTestData：admin only，按命名规则清理 verify 残留
 *  - R4 batchDelete*：{ids}→{succeeded,failed}；权限=停用不解绑
 *  - 覆盖式 PUT（assignUserRoles/assignRolePermissions/assignRoleScopes）必传完整 id 数组
 */

import { apsHttp, APS_USE_MOCK, ApiError } from './http'
import type {
  UserSummaryDto,
  CreateUserRequest,
  UpdateUserRequest,
  RoleSummaryDto,
  CreateRoleRequest,
  UpdateRoleRequest,
  PermissionSummaryDto,
  CreatePermissionRequest,
  DataScopePolicyDto,
  CreateDataScopePolicyRequest,
  RbacPageResult,
  ListUsersParams,
  ListRolesParams,
  ListPermissionsParams,
  ListScopesParams,
  BatchDeleteRequest,
  BatchDeleteResult,
  CleanupResult
} from './types'

/* ==================== Mock Fixtures（仅离线 UI） ==================== */

const now = (): string => new Date().toISOString()
const fixedDate = (offset: number): string =>
  new Date(Date.now() - offset * 86400_000).toISOString()

const MOCK_USERS: UserSummaryDto[] = [
  {
    id: 1,
    userCode: 'admin',
    userName: '系统管理员',
    email: 'admin@example.com',
    status: 'Active',
    lastLoginTime: now(),
    createdAt: fixedDate(365)
  },
  {
    id: 2,
    userCode: 'pmc',
    userName: 'PMC 计划员',
    email: 'pmc@example.com',
    status: 'Active',
    lastLoginTime: fixedDate(1),
    createdAt: fixedDate(30)
  },
  {
    id: 3,
    userCode: 'viewer',
    userName: '查看员',
    status: 'Active',
    createdAt: fixedDate(60)
  }
]

const MOCK_ROLES: RoleSummaryDto[] = [
  {
    id: 1,
    roleCode: 'aps.admin.system',
    roleName: '系统管理员',
    description: '全局管理员，拥有全部权限',
    isSystemRole: true,
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 2,
    roleCode: 'aps.admin.aps',
    roleName: 'APS 管理员',
    description: '规则/参数/策略维护',
    isSystemRole: true,
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 3,
    roleCode: 'aps.planner',
    roleName: '计划员',
    description: 'PMC 排产 / CTP / Candidate 确认',
    isSystemRole: true,
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 4,
    roleCode: 'aps.viewer.management',
    roleName: '管理查看员',
    description: '只读账号',
    isSystemRole: true,
    isActive: true,
    createdAt: fixedDate(365)
  }
]

const MOCK_PERMS: PermissionSummaryDto[] = [
  {
    id: 1,
    permissionCode: 'aps.plan.view',
    permissionName: '查看排产',
    module: 'plan',
    actionType: 'view',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 2,
    permissionCode: 'aps.plan.run',
    permissionName: '运行排产',
    module: 'plan',
    actionType: 'run',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 3,
    permissionCode: 'aps.rule.view',
    permissionName: '查看规则',
    module: 'rule',
    actionType: 'view',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 4,
    permissionCode: 'aps.rule.edit',
    permissionName: '编辑规则',
    module: 'rule',
    actionType: 'edit',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 5,
    permissionCode: 'aps.rule.publish',
    permissionName: '发布规则',
    module: 'rule',
    actionType: 'publish',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 6,
    permissionCode: 'aps.auth.user.view',
    permissionName: '查看用户',
    module: 'auth',
    actionType: 'view',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 7,
    permissionCode: 'aps.auth.user.edit',
    permissionName: '编辑用户',
    module: 'auth',
    actionType: 'edit',
    isActive: true,
    createdAt: fixedDate(365)
  },
  {
    id: 8,
    permissionCode: 'aps.audit.view',
    permissionName: '查看审计',
    module: 'audit',
    actionType: 'view',
    isActive: true,
    createdAt: fixedDate(365)
  }
]

const MOCK_SCOPES: DataScopePolicyDto[] = [
  {
    id: 1,
    scopeType: 'Factory',
    scopeValue: 'BJ',
    description: '北京工厂',
    createdAt: fixedDate(365)
  },
  {
    id: 2,
    scopeType: 'Factory',
    scopeValue: 'SUZ',
    description: '苏州工厂',
    createdAt: fixedDate(365)
  },
  {
    id: 3,
    scopeType: 'ProductFamily',
    scopeValue: 'INJECTION',
    description: '注塑族',
    createdAt: fixedDate(365)
  },
  {
    id: 4,
    scopeType: 'Domain',
    scopeValue: 'FAMILY_INJECTION',
    description: '注塑域',
    createdAt: fixedDate(365)
  }
]

/* ==================== Mock 读回 fixtures（仅离线 UI 预勾选用） ==================== */

/** 用户已分配角色（mock）：admin=全 4 角色，pmc=planner，viewer=管理查看员 */
const MOCK_USER_ROLES_BY_ID: Record<number, RoleSummaryDto[]> = {
  1: [MOCK_ROLES[0], MOCK_ROLES[1], MOCK_ROLES[2], MOCK_ROLES[3]],
  2: [MOCK_ROLES[2]],
  3: [MOCK_ROLES[3]]
}

/** 用户直授业务范围（mock）：admin=BJ 工厂 + 注塑域 */
const MOCK_USER_SCOPES_BY_ID: Record<number, DataScopePolicyDto[]> = {
  1: [MOCK_SCOPES[0], MOCK_SCOPES[3]],
  2: [MOCK_SCOPES[1]],
  3: []
}

/** 角色已分配权限（mock）：admin.system=全 8 权限，admin.aps=plan+rule，planner=plan+ctp，viewer=plan+rule */
const MOCK_ROLE_PERMS_BY_ID: Record<number, PermissionSummaryDto[]> = {
  1: MOCK_PERMS,
  2: [MOCK_PERMS[0], MOCK_PERMS[1], MOCK_PERMS[2], MOCK_PERMS[3], MOCK_PERMS[4]],
  3: [MOCK_PERMS[0], MOCK_PERMS[1]],
  4: [MOCK_PERMS[0], MOCK_PERMS[2]]
}

/** 角色已分配业务范围（mock）：admin.system=全部 4 范围，admin.aps=BJ+注塑族+注塑域 */
const MOCK_ROLE_SCOPES_BY_ID: Record<number, DataScopePolicyDto[]> = {
  1: MOCK_SCOPES,
  2: [MOCK_SCOPES[0], MOCK_SCOPES[2], MOCK_SCOPES[3]],
  3: [MOCK_SCOPES[0], MOCK_SCOPES[1]],
  4: []
}

/* ==================== 写端点 mock 拒绝 ==================== */

/** mock 模式下所有写端点统一抛 403（防伪成功误导） */
const rejectMockWrite = (op: string): Promise<never> =>
  Promise.reject(new ApiError(`mock 模式禁止写 RBAC（${op}）`, 403))

/* ==================== 分页 helpers ==================== */

/** mock 模式包装：把扁平数组包成 RbacPageResult（v1.0 契约 §二） */
function pageWrap<T>(items: T[]): RbacPageResult<T> {
  return { items, total: items.length, page: 1, pageSize: items.length || 20 }
}

/** 真实模式构造 query string：跳过 undefined / null / 空字符串 */
function buildQuery(params: Record<string, unknown>): string {
  const sp = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue
    sp.append(k, String(v))
  }
  const s = sp.toString()
  return s ? `?${s}` : ''
}

/* ==================== API ==================== */

export const rbacApi = {
  /* ===== User ===== */
  async listUsers(params: ListUsersParams = {}): Promise<RbacPageResult<UserSummaryDto>> {
    if (APS_USE_MOCK) return pageWrap(MOCK_USERS)
    return apsHttp.get<RbacPageResult<UserSummaryDto>>({
      url: `/api/rbac/users${buildQuery(params as Record<string, unknown>)}`
    })
  },

  async createUser(data: CreateUserRequest): Promise<UserSummaryDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createUser')
    return apsHttp.post<UserSummaryDto>({ url: '/api/rbac/users', data })
  },

  async updateUser(id: number, data: UpdateUserRequest): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('updateUser')
    return apsHttp.put<void>({ url: `/api/rbac/users/${id}`, data })
  },

  async deleteUser(id: number): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('deleteUser')
    return apsHttp.delete<void>({ url: `/api/rbac/users/${id}` })
  },

  async assignUserRoles(userId: number, ids: number[]): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('assignUserRoles')
    return apsHttp.put<void>({
      url: `/api/rbac/users/${userId}/roles`,
      data: { ids }
    })
  },

  async assignUserScopes(userId: number, ids: number[]): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('assignUserScopes')
    return apsHttp.put<void>({
      url: `/api/rbac/users/${userId}/scopes`,
      data: { ids }
    })
  },

  /** 读回用户已分配角色（3号位 GET /api/rbac/users/{id}/roles） */
  async getUserRoles(userId: number): Promise<RoleSummaryDto[]> {
    if (APS_USE_MOCK) return MOCK_USER_ROLES_BY_ID[userId] ?? []
    return apsHttp.get<RoleSummaryDto[]>({ url: `/api/rbac/users/${userId}/roles` })
  },

  /** 读回用户直授业务范围（3号位 GET /api/rbac/users/{id}/scopes） */
  async getUserScopes(userId: number): Promise<DataScopePolicyDto[]> {
    if (APS_USE_MOCK) return MOCK_USER_SCOPES_BY_ID[userId] ?? []
    return apsHttp.get<DataScopePolicyDto[]>({ url: `/api/rbac/users/${userId}/scopes` })
  },

  /* ===== Role ===== */
  async listRoles(params: ListRolesParams = {}): Promise<RbacPageResult<RoleSummaryDto>> {
    if (APS_USE_MOCK) return pageWrap(MOCK_ROLES)
    return apsHttp.get<RbacPageResult<RoleSummaryDto>>({
      url: `/api/rbac/roles${buildQuery(params as Record<string, unknown>)}`
    })
  },

  async createRole(data: CreateRoleRequest): Promise<RoleSummaryDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createRole')
    return apsHttp.post<RoleSummaryDto>({ url: '/api/rbac/roles', data })
  },

  async updateRole(id: number, data: UpdateRoleRequest): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('updateRole')
    return apsHttp.put<void>({ url: `/api/rbac/roles/${id}`, data })
  },

  async deleteRole(id: number): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('deleteRole')
    return apsHttp.delete<void>({ url: `/api/rbac/roles/${id}` })
  },

  async assignRolePermissions(roleId: number, ids: number[]): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('assignRolePermissions')
    return apsHttp.put<void>({
      url: `/api/rbac/roles/${roleId}/permissions`,
      data: { ids }
    })
  },

  async assignRoleScopes(roleId: number, ids: number[]): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('assignRoleScopes')
    return apsHttp.put<void>({
      url: `/api/rbac/roles/${roleId}/scopes`,
      data: { ids }
    })
  },

  /** 读回角色已分配权限（3号位 GET /api/rbac/roles/{id}/permissions） */
  async getRolePermissions(roleId: number): Promise<PermissionSummaryDto[]> {
    if (APS_USE_MOCK) return MOCK_ROLE_PERMS_BY_ID[roleId] ?? []
    return apsHttp.get<PermissionSummaryDto[]>({ url: `/api/rbac/roles/${roleId}/permissions` })
  },

  /** 读回角色已分配业务范围（3号位 GET /api/rbac/roles/{id}/scopes） */
  async getRoleScopes(roleId: number): Promise<DataScopePolicyDto[]> {
    if (APS_USE_MOCK) return MOCK_ROLE_SCOPES_BY_ID[roleId] ?? []
    return apsHttp.get<DataScopePolicyDto[]>({ url: `/api/rbac/roles/${roleId}/scopes` })
  },

  /* ===== Permission ===== */
  async listPermissions(
    params: ListPermissionsParams = {}
  ): Promise<RbacPageResult<PermissionSummaryDto>> {
    if (APS_USE_MOCK) return pageWrap(MOCK_PERMS)
    return apsHttp.get<RbacPageResult<PermissionSummaryDto>>({
      url: `/api/rbac/permissions${buildQuery(params as Record<string, unknown>)}`
    })
  },

  async createPermission(data: CreatePermissionRequest): Promise<PermissionSummaryDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createPermission')
    return apsHttp.post<PermissionSummaryDto>({ url: '/api/rbac/permissions', data })
  },

  /* ===== Scope ===== */
  async listScopes(params: ListScopesParams = {}): Promise<RbacPageResult<DataScopePolicyDto>> {
    if (APS_USE_MOCK) return pageWrap(MOCK_SCOPES)
    return apsHttp.get<RbacPageResult<DataScopePolicyDto>>({
      url: `/api/rbac/scopes${buildQuery(params as Record<string, unknown>)}`
    })
  },

  async createScope(data: CreateDataScopePolicyRequest): Promise<DataScopePolicyDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createScope')
    return apsHttp.post<DataScopePolicyDto>({ url: '/api/rbac/scopes', data })
  },

  /** 后端 UpdateDataScopePolicyRequest 只有 Description —— scopeType/scopeValue 不可改 */
  async updateScope(id: number, description: string | null): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('updateScope')
    return apsHttp.put<void>({ url: `/api/rbac/scopes/${id}`, data: { description } })
  },

  async deleteScope(id: number): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('deleteScope')
    return apsHttp.delete<void>({ url: `/api/rbac/scopes/${id}` })
  },

  /* ===== R4 批量删除（3号位 v1.0 §一.3） ===== */

  async batchDeleteUsers(req: BatchDeleteRequest): Promise<BatchDeleteResult> {
    if (APS_USE_MOCK) return rejectMockWrite('batchDeleteUsers')
    return apsHttp.post<BatchDeleteResult>({
      url: '/api/rbac/users/batch-delete',
      data: req
    })
  },

  async batchDeleteRoles(req: BatchDeleteRequest): Promise<BatchDeleteResult> {
    if (APS_USE_MOCK) return rejectMockWrite('batchDeleteRoles')
    return apsHttp.post<BatchDeleteResult>({
      url: '/api/rbac/roles/batch-delete',
      data: req
    })
  },

  /** 权限批量删除=停用（保留 RolePermission 关联不解绑，v1.0 §一.3） */
  async batchDeletePermissions(req: BatchDeleteRequest): Promise<BatchDeleteResult> {
    if (APS_USE_MOCK) return rejectMockWrite('batchDeletePermissions')
    return apsHttp.post<BatchDeleteResult>({
      url: '/api/rbac/permissions/batch-delete',
      data: req
    })
  },

  async batchDeleteScopes(req: BatchDeleteRequest): Promise<BatchDeleteResult> {
    if (APS_USE_MOCK) return rejectMockWrite('batchDeleteScopes')
    return apsHttp.post<BatchDeleteResult>({
      url: '/api/rbac/scopes/batch-delete',
      data: req
    })
  },

  /* ===== R1 测试数据清理（3号位 v1.0 §一.1） ===== */

  /** admin only；按命名规则软删/停用 verify 残留（自删保护） */
  async cleanupTestData(): Promise<CleanupResult> {
    if (APS_USE_MOCK) return rejectMockWrite('cleanupTestData')
    return apsHttp.post<CleanupResult>({ url: '/api/rbac/test-data/cleanup' })
  }
}
