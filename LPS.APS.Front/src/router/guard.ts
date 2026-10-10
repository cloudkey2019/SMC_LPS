/**
 * APS V1 4号位 — 路由级 RBAC 守卫
 *
 * @see 审核报告 v1.1 §十七.17 / §二十五.1
 *
 * 触发条件：路由 meta 含 apsRequiredRoles / apsRequiredPermissions 任一字段
 *  - apsRequiredRoles: 当前用户必须包含至少一个角色
 *  - apsRequiredPermissions: 当前用户必须包含至少一个权限
 *  - APS V1 路由默认不挂（与已保存的硬约束一致 — RBAC 与 APS 路由）
 *  - 待 3 号位真实接口接入后，可逐页开启
 *
 * 行为：越权时跳转 /403（项目已有 NoPermission 页）
 */

import type { Router } from 'vue-router'
import type { RoleKey } from '@/api/aps-v1'
import { useApsAuthStore } from '@/store/modules/aps/auth'

/** 路由 meta 扩展（ApsV1 模块专用，不污染全局 AppRouteRecordRaw） */
export interface ApsRouteMeta {
  /** 角色白名单（任一命中即通过；OR 语义） */
  apsRequiredRoles?: RoleKey[]
  /** 权限白名单（任一命中即通过；OR 语义；v1.2 §23.1 层次码 aps.* 前缀）
   *  Phase B：放宽到 string[]，与 UserInfo.permissions 类型一致；后端 34 码及未来扩展由后端权威决定 */
  apsRequiredPermissions?: string[]
  /** 业务范围提示（仅展示用，不强制过滤；UI 由 store 防御） */
  apsScopeHint?: string
}

/**
 * 检查单个路由是否对当前用户可见
 *  - 任意 meta 字段缺失 → 通过
 *  - 两个字段都设 → 必须同时通过（AND 语义）
 */
export function isRouteAllowed(
  meta: ApsRouteMeta,
  ctx: { roles: RoleKey[]; permissions: string[] }
): { ok: true } | { ok: false; reason: string } {
  if (meta.apsRequiredRoles && meta.apsRequiredRoles.length > 0) {
    const hit = meta.apsRequiredRoles.some((r) => ctx.roles.includes(r))
    if (!hit) {
      return {
        ok: false,
        reason: `路由要求角色 ${meta.apsRequiredRoles.join('/')}，当前角色 ${ctx.roles.join('/') || '无'}`
      }
    }
  }
  if (meta.apsRequiredPermissions && meta.apsRequiredPermissions.length > 0) {
    const hit = meta.apsRequiredPermissions.some((p) => ctx.permissions.includes(p))
    if (!hit) {
      return {
        ok: false,
        reason: `路由要求权限 ${meta.apsRequiredPermissions.join('/')}，当前权限 ${ctx.permissions.join('/') || '无'}`
      }
    }
  }
  return { ok: true }
}

/**
 * 注册 RBAC 守卫到 vue-router
 *  - 仅 APS V1 命名空间下的路由（name 以 'Aps' 开头）触发
 *  - 其它路由不受影响（保持项目原行为）
 */
export function setupApsRouteGuard(router: Router): void {
  router.beforeEach((to, _from, next) => {
    // 仅 APS 路由触发（路由名以 Aps 开头 — 与 modules/aps.ts 一致）
    const isApsRoute = typeof to.name === 'string' && to.name.startsWith('Aps')
    if (!isApsRoute) return next()

    const meta = (to.meta ?? {}) as ApsRouteMeta
    if (!meta.apsRequiredRoles && !meta.apsRequiredPermissions) {
      return next() // 未挂 RBAC = 放行（保持现状）
    }

    const auth = useApsAuthStore()
    const result = isRouteAllowed(meta, {
      roles: auth.roles,
      permissions: auth.userInfo?.permissions ?? []
    })
    if (result.ok) return next()

    // 越权 → 跳 /403（项目已有 NoPermission 页）
    console.warn(`[APS RBAC] 路由 ${to.fullPath} 拒绝: ${result.reason}`)
    return next({ path: '/403', query: { from: to.fullPath } })
  })
}
