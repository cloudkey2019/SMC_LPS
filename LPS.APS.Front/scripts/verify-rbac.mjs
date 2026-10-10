#!/usr/bin/env node
/**
 * APS V1 4号位 — 路由级 RBAC 离线验证脚本（v1.2 §23.1，Phase B 迁移至后端 34 码 + DDL 角色码）
 *
 * 用途：不启 dev server、不依赖浏览器，直接在 Node 里跑出
 *       "4 角色 × 16 路由" RBAC 矩阵，立刻看到红绿。
 *
 * 设计：复刻 src/router/guard.ts isRouteAllowed 逻辑 + MOCK_ROLE_PRESETS 权限码，
 *       与 router/modules/aps.ts 路由 meta 严格对齐。
 *
 * 用法：node scripts/verify-rbac.mjs
 *       （或 pnpm ts:check / lint 通过后任意时刻）
 *
 * 退出码：所有期望匹配则 0；不匹配则 1。
 */

/* ===== 22 路由 meta（与 router/modules/aps.ts 严格一致；v1.4 加 strategy-profile；v1.5 加 setup/* 4 条；09-20 加 audit）===== */
const ROUTES = [
  // 第一波（规则 / Domain）
  { path: '/aps/rules', meta: { apsRequiredPermissions: ['aps.rule.edit', 'aps.rule.publish'] } },
  { path: '/aps/domain', meta: { apsRequiredRoles: ['aps.admin.system'] } },
  // 第二波（PMC 写操作）
  { path: '/aps/ctp', meta: { apsRequiredPermissions: ['aps.ctp.evaluate'] } },
  { path: '/aps/candidate', meta: { apsRequiredPermissions: ['aps.candidate.confirm'] } },
  { path: '/aps/manual-eta', meta: { apsRequiredRoles: ['aps.planner', 'aps.admin.system'] } },
  {
    path: '/aps/demand-protection',
    meta: { apsRequiredRoles: ['aps.planner', 'aps.admin.system'] }
  },
  // 第三波（只读页 - 4 角色均持 aps.plan.view，全员放行）
  { path: '/aps/overview', meta: { apsRequiredPermissions: ['aps.plan.view'] } },
  { path: '/aps/order', meta: { apsRequiredPermissions: ['aps.plan.view'] } },
  { path: '/aps/gantt', meta: { apsRequiredPermissions: ['aps.plan.view'] } },
  { path: '/aps/explanation', meta: { apsRequiredPermissions: ['aps.plan.view'] } },
  { path: '/aps/run', meta: { apsRequiredPermissions: ['aps.plan.view'] } },
  { path: '/aps/pi', meta: { apsRequiredPermissions: ['aps.plan.view'] } },
  // v1.4 §十八.10：策略配置（长期欠账收口；独立路由不混入 Rules.vue）
  // OR 语义：aps.strategy.view/edit/publish 任一即可（VIEWER 持 view 可进，admin.aps 持 view+edit，admin.system 持全部）
  {
    path: '/aps/strategy-profile',
    meta: { apsRequiredPermissions: ['aps.strategy.view', 'aps.strategy.edit', 'aps.strategy.publish'] }
  },
  // v1.5 Setup 专项：4 条 setup 路由（页面 2.1-2.4；2.5 红线为组件无路由）
  {
    path: '/aps/setup/exact',
    meta: { apsRequiredPermissions: ['aps.setup.view', 'aps.setup.edit', 'aps.setup.publish'] }
  },
  {
    path: '/aps/setup/default',
    meta: { apsRequiredPermissions: ['aps.setup.view', 'aps.setup.edit', 'aps.setup.publish'] }
  },
  {
    path: '/aps/setup/uncovered',
    meta: { apsRequiredPermissions: ['aps.setup.view'] }
  },
  {
    path: '/aps/setup/diff',
    meta: { apsRequiredPermissions: ['aps.setup.view', 'aps.setup.publish'] }
  },
  // Pkg-8：RBAC 管理 UI（仅 aps.admin.system — 持 aps.auth.user.edit）
  { path: '/aps/rbac-users', meta: { apsRequiredPermissions: ['aps.auth.user.edit'] } },
  { path: '/aps/rbac-roles', meta: { apsRequiredPermissions: ['aps.auth.user.edit'] } },
  { path: '/aps/rbac-permissions', meta: { apsRequiredPermissions: ['aps.auth.user.edit'] } },
  { path: '/aps/rbac-scopes', meta: { apsRequiredPermissions: ['aps.auth.user.edit'] } },
  // 审计日志页（§22.6 / U42）：专属码 aps.audit.view，rbac.md §1.2 约定 4 角色均持
  { path: '/aps/audit', meta: { apsRequiredPermissions: ['aps.audit.view'] } }
]

/* ===== 4 角色权限码（与 store/modules/aps/auth.ts MOCK_ROLE_PRESETS 一致）
 *      v1.2 DDL 角色码：RULE_ADMIN + RULE_PUBLISHER 已合并为 aps.admin.aps
 *      权限码分布（与 dev seed 段 D 一致）：
 *        aps.viewer.management  → 11 个 *.view（v1.5 加 aps.setup.view）
 *        aps.planner            → 19 个（plan/ctp/candidate/manual_eta/demand_protection.view + aps.setup.view + aps.audit.view）
 *        aps.admin.aps          → 13 个（rule/parameter/strategy/setup edit + publish + aps.audit.view；含 strategy.publish 与 auth.ts 既有漂移）
 *        aps.admin.system       → 37 个全部 */
const ROLE_PRESETS = {
  'aps.viewer.management': [
    'aps.plan.view',
    'aps.ctp.view',
    'aps.candidate.view',
    'aps.rule.view',
    'aps.parameter.view',
    'aps.strategy.view',
    'aps.setup.view',
    'aps.manual_eta.view',
    'aps.demand_protection.view',
    'aps.mes.view',
    'aps.audit.view'
  ],
  'aps.planner': [
    'aps.plan.view',
    'aps.plan.run',
    'aps.plan.compare',
    'aps.plan.export',
    'aps.ctp.view',
    'aps.ctp.evaluate',
    'aps.insert.impact.evaluate',
    'aps.reschedule.local',
    'aps.reschedule.manual',
    'aps.candidate.view',
    'aps.candidate.confirm',
    'aps.candidate.activate',
    'aps.setup.view',
    'aps.manual_eta.view',
    'aps.manual_eta.edit',
    'aps.manual_eta.cancel',
    'aps.demand_protection.view',
    'aps.mes.view',
    // 09-20 Audit 页落地：对齐 rbac.md §1.2「4 角色均持 aps.audit.view」
    'aps.audit.view'
  ],
  'aps.admin.aps': [
    'aps.plan.view',
    'aps.rule.view',
    'aps.rule.edit',
    'aps.rule.publish',
    'aps.parameter.view',
    'aps.parameter.edit',
    'aps.strategy.view',
    'aps.strategy.edit',
    'aps.strategy.publish',
    'aps.setup.view',
    'aps.setup.edit',
    'aps.setup.publish',
    // 09-20 Audit 页落地：对齐 rbac.md §1.2「4 角色均持 aps.audit.view」
    'aps.audit.view'
  ],
  'aps.admin.system': [
    'aps.plan.view',
    'aps.plan.run',
    'aps.plan.compare',
    'aps.plan.export',
    'aps.ctp.view',
    'aps.ctp.evaluate',
    'aps.insert.impact.evaluate',
    'aps.reschedule.local',
    'aps.reschedule.manual',
    'aps.candidate.view',
    'aps.candidate.confirm',
    'aps.candidate.activate',
    'aps.rule.view',
    'aps.rule.edit',
    'aps.rule.publish',
    'aps.parameter.view',
    'aps.parameter.edit',
    'aps.strategy.view',
    'aps.strategy.edit',
    'aps.strategy.publish',
    'aps.setup.view',
    'aps.setup.edit',
    'aps.setup.publish',
    'aps.manual_eta.view',
    'aps.manual_eta.edit',
    'aps.manual_eta.cancel',
    'aps.demand_protection.view',
    'aps.demand_protection.release',
    'aps.mes.view',
    'aps.mes.dispatch',
    'aps.auth.user.view',
    'aps.auth.user.edit',
    'aps.auth.role.view',
    'aps.auth.role.edit',
    'aps.auth.permission.assign',
    'aps.auth.scope.assign',
    'aps.audit.view'
  ]
}

const ROLES = Object.keys(ROLE_PRESETS)

/* ===== isRouteAllowed（复刻 src/router/guard.ts:41-64）===== */
function isRouteAllowed(meta, ctx) {
  if (meta.apsRequiredRoles && meta.apsRequiredRoles.length > 0) {
    const hit = meta.apsRequiredRoles.some((r) => ctx.roles.includes(r))
    if (!hit) return { ok: false, reason: `缺角色 ${meta.apsRequiredRoles.join('/')}` }
  }
  if (meta.apsRequiredPermissions && meta.apsRequiredPermissions.length > 0) {
    const hit = meta.apsRequiredPermissions.some((p) => ctx.permissions.includes(p))
    if (!hit) return { ok: false, reason: `缺权限码 ${meta.apsRequiredPermissions.join('/')}` }
  }
  return { ok: true }
}

/* ===== 期望矩阵（绿色 ✅=应通过；红色 ❌=应被 /403 拦截）===== */
const EXPECTED = {
  // 第一波（规则 / Domain）
  '/aps/rules': {
    'aps.viewer.management': false,
    'aps.planner': false,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/domain': {
    'aps.viewer.management': false,
    'aps.planner': false,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  // 第二波（PMC 写操作）
  '/aps/ctp': {
    'aps.viewer.management': false,
    'aps.planner': true,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  '/aps/candidate': {
    'aps.viewer.management': false,
    'aps.planner': true,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  '/aps/manual-eta': {
    'aps.viewer.management': false,
    'aps.planner': true,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  '/aps/demand-protection': {
    'aps.viewer.management': false,
    'aps.planner': true,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  // 第三波（只读页 - 4 角色均持 aps.plan.view，全员放行）
  '/aps/overview': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/order': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/gantt': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/explanation': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/run': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/pi': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  // v1.4 §十八.10：策略配置（OR 语义 — VIEWER 持 view / admin.aps 持 view+edit / admin.system 持全部）
  '/aps/strategy-profile': {
    'aps.viewer.management': true,
    'aps.planner': false,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  // v1.5 Setup 专项：4 角色均持 aps.setup.view（planner/viewer 只读；admin.aps/admin.system 可写可发）
  '/aps/setup/exact': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/setup/default': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/setup/uncovered': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  '/aps/setup/diff': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  },
  // Pkg-8：RBAC 管理 UI（仅 aps.admin.system — 持 aps.auth.user.edit）
  '/aps/rbac-users': {
    'aps.viewer.management': false,
    'aps.planner': false,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  '/aps/rbac-roles': {
    'aps.viewer.management': false,
    'aps.planner': false,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  '/aps/rbac-permissions': {
    'aps.viewer.management': false,
    'aps.planner': false,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  '/aps/rbac-scopes': {
    'aps.viewer.management': false,
    'aps.planner': false,
    'aps.admin.aps': false,
    'aps.admin.system': true
  },
  // 审计日志页（§22.6 / U42）：aps.audit.view 4 角色均持（rbac.md §1.2；09-20 mock 预设已对齐补码）
  // 注意与 rbac-* 四页（仅 admin.system）不同——审计是监督能力，管理层 viewer 也可查看
  '/aps/audit': {
    'aps.viewer.management': true,
    'aps.planner': true,
    'aps.admin.aps': true,
    'aps.admin.system': true
  }
}

/* ===== 跑矩阵 ===== */
let pass = 0
let fail = 0
const lines = []
lines.push('APS V1 RBAC 矩阵（v1.2 §23.1 + DDL 角色码；✅=放行 / ❌=403）')
lines.push('')

// 表头
const colW = 28
const cellW = 22
const header =
  '路由'.padEnd(colW) +
  ROLES.map((r) => r.padEnd(cellW)).join('')
console.log(header)
console.log('-'.repeat(header.length))

for (const route of ROUTES) {
  // 该路由 RBAC 期望配置
  const expectedSummary =
    (route.meta.apsRequiredRoles ? `角色 ${route.meta.apsRequiredRoles.join('|')}` : '') +
    (route.meta.apsRequiredPermissions
      ? `权限码 ${route.meta.apsRequiredPermissions.join('|')}`
      : '') +
    (!route.meta.apsRequiredRoles && !route.meta.apsRequiredPermissions
      ? '（无 RBAC）'
      : '')

  const cells = []
  for (const role of ROLES) {
    const ctx = { roles: [role], permissions: ROLE_PRESETS[role] }
    const actual = isRouteAllowed(route.meta, ctx).ok
    const expected = EXPECTED[route.path][role]
    const match = actual === expected
    if (match) pass++
    else fail++
    // 显示：actual 状态（✅/❌）+ 一致性标记（✓/✗）
    const actualMark = actual ? '✅ 放行' : '❌ 403'
    const matchMark = match ? '✓' : '✗'
    cells.push(`${actualMark} ${matchMark}`)
  }

  console.log(
    `${route.path.padEnd(colW - 8)}${expectedSummary.padEnd(8)}` +
      cells.map((c) => c.padEnd(cellW)).join('')
  )
}

console.log('')
console.log(`总组合数：${pass + fail}    ✅ 一致：${pass}    ❌ 不一致：${fail}`)
process.exit(fail > 0 ? 1 : 0)
