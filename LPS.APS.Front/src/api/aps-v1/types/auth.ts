/**
 * 认证 DTO（v1.2 后端对齐版）
 *
 * 来源：lps/LPS.APS.Web/Dto/Auth/
 *  - LoginRequestDto.cs           (UserCode / Password)
 *  - LoginResponseDto.cs          (AccessToken / RefreshToken / ExpiresAt / UserId / UserCode / UserName / Roles)
 *  - RefreshTokenRequestDto.cs    (AccessToken / RefreshToken)
 *  - UserInfoDto.cs               (UserId / UserCode / UserName / Roles / Permissions / IsGlobal / Factories / ProductFamilies / Departments / Domains / ResourceOrgGroups)
 *
 * 字段命名按 0号位《Auth库DDL缺陷与真源裁决》R1=A′ 收口：
 *  - 后端 PascalCase → 前端 camelCase（API 层做映射）
 *  - 业务范围扁平 5 维度 + IsGlobal（Global 时为全放行）
 *  - PermissionCode 不在 TS 侧枚举（后端 34 码随 v1.2 冻结，前端只读不强制）
 *
 * Phase A 范围：仅 DTO 类型对齐。消费方（store/permission.ts/views/Login）暂未切换，
 *   旧字段（username / displayName / dataScope.* 嵌套 / expiresIn）保留 @deprecated 别名，
 *   后续 Phase B（权限码迁移）+ Phase C（scope 扁平化）再统一替换。
 */

import type { DomainKey, RoleKey } from './common'

// ==================== 登录 ====================

/** 登录请求 — 对齐后端 LoginRequestDto */
export interface LoginRequest {
  /** 用户工号（后端字段 UserCode；前端旧名 username 已废弃） */
  userCode: string
  password: string
}

/** 登录响应 — 对齐后端 LoginResponseDto
 *  注：后端将 userId/userCode/userName/roles 放在顶层，不嵌套 userInfo。 */
export interface LoginResponse {
  accessToken: string
  refreshToken: string
  /** AccessToken 过期时间（绝对时间 ISO8601，对齐后端 ExpiresAt） */
  expiresAt: string
  userId: number
  /** 用户工号 */
  userCode: string
  /** 用户姓名 */
  userName: string
  /** 角色列表（后端为 string[]，前端强类型为 RoleKey[]） */
  roles: RoleKey[]
}

// ==================== 刷新 Token ====================

/** 刷新 token 请求 — 对齐后端 RefreshTokenRequestDto
 *  后端要求同时携带过期的 accessToken + 有效的 refreshToken。 */
export interface RefreshTokenRequest {
  accessToken: string
  refreshToken: string
}

// ==================== 当前用户信息 ====================

/** 用户信息 — 对齐后端 UserInfoDto */
export interface UserInfo {
  userId: number
  /** 用户工号（对齐后端 UserCode；旧名 username 已废弃） */
  userCode: string
  /** 用户姓名（对齐后端 UserName；旧名 displayName 已废弃） */
  userName: string
  /** 角色列表（后端为 string[]，前端强类型为 RoleKey[]） */
  roles: RoleKey[]
  /**
   * 功能权限码列表（v1.2 §23.1：`aps.<module>.<action>` 层次码，统一 `aps.` 前缀）。
   * 后端签发 34 码（见 lps/LPS.APS.Core/Authorization/PermissionCodes.cs），前端不强制枚举以兼容后续扩展。
   * 前端比对字符串相等即可；`verifyPermission()` 提供归一化兜底（见 store/modules/aps/auth.ts）。
   */
  permissions: string[]
  /** 业务范围：是否全局放行（Global=true 时各维度集合为空） */
  isGlobal: boolean
  /** 业务范围：工厂范围值集合 */
  factories: string[]
  /** 业务范围：产品族范围值集合 */
  productFamilies: string[]
  /** 业务范围：部门范围值集合 */
  departments: string[]
  /** 业务范围：域范围值集合（DomainKey，例：FAMILY_INJECTION / BJ_FAMILY_INJECTION） */
  domains: DomainKey[]
  /** 业务范围：资源组织组范围值集合（兼容字段，V1 多为空） */
  resourceOrgGroups: string[]
}

// ==================== 兼容层（Phase B/C 期间临时保留） ====================

/**
 * @deprecated 自 v1.2 后端对齐后使用 userCode；旧 mock 数据可能仍含 username，Phase B 替换消费方后删除。
 */
export interface UserInfoLegacy {
  username: string
  displayName: string
  email?: string
  phone?: string
  avatar?: string
  dataScope: {
    factoryCodes: string[]
    productFamilyCodes: string[]
    departmentCodes?: string[]
    domainKeys?: DomainKey[]
  }
  factoryId?: number
  factoryCode?: string
  departmentId?: number
}
