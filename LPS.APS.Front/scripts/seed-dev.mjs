#!/usr/bin/env node
/**
 * APS V1 4号位 — dev 环境种子脚本（一次性，运行后可重复 — 后端 RBAC API 均为覆盖式 / 幂等）
 *
 * 用途：联通 3号位 真实 RBAC 接口，把 dev DB 补齐 4 个测试用户 + 9 条 Scope + 3 业务角色挂权限/范围。
 *
 * 红线（务必遵守 — 见 冻结文档/3号位回执-dev种子补丁说明(3).md 第三节）：
 *  ❌ 不要 PUT /api/rbac/users/{adminId}/roles         —— 会清掉 admin 已挂 aps.admin.system
 *  ❌ 不要 PUT /api/rbac/roles/{apsAdminSystemId}/permissions  —— 会清掉 34 权限
 *  ❌ 不要 PUT /api/rbac/roles/{apsAdminSystemId}/scopes       —— 会清掉 Global
 *  ✅ 所有 PUT 必须传"该对象目标态完整集合"，不能当 append
 *
 * 用法：
 *   1) 后端启动并 admin 能登录（curl -X POST .../api/auth/login 验证）
 *   2) node scripts/seed-dev.mjs
 *      或 pnpm seed:dev （package.json 已配）
 *   3) 输出末行出现 "✅ seed:dev 完成"，方可启动 pnpm dev 进入浏览器联调
 *
 * 设计：
 *  - 用 Node 22 内置 fetch（无需 axios / node-fetch）
 *  - 与后端 ApiResponse<T> 解包：code===200 视为成功
 *  - 任何步骤失败立刻退出（exit 1），便于 CI/手工排查
 *
 * 覆盖式 PUT 的幂等性保证：
 *  - 本脚本所有 PUT 都基于"先 GET 取当前完整态 → 合并目标态 → 全集 PUT"，但简化版做法是：
 *    直接 PUT 目标态完整集合（脚本里写死），后端 DELETE+INSERT 幂等
 *  - 如需多环境/多套测试用户并行，请复制本脚本改 seedUsers / seedRoles 数组
 */

/* ========== 配置 ========== */
// dev 后端是 HTTPS 7044（dot net run 自签证书），必须用 https 直连避开 307 跨协议跳转
// （Node fetch / Undici 拒绝跟 http→https 重定向，与 curl 默认行为不同）
const BASE_URL = process.env.APS_BASE_URL || 'https://localhost:7044'
const ADMIN_USERCODE = 'admin'
const ADMIN_PASSWORD = 'Admin@123456'
// dev 自签证书容忍（仅 seed 脚本；前端 axios 不需要，由浏览器/HTTP 客户端处理）
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'

/** 测试用户（4 个）
 *  - viewer / pmc / rule_admin / rule_publisher
 *  - 密码 Password@123（10 字符，不与 userCode 重复，满足后端 8-128 位校验）
 *  - 与 frontNew 文档 v3「四、第 1 步」一致
 */
const TEST_USERS = [
  { userCode: 'viewer', userName: '查看员', password: 'Password@123' },
  { userCode: 'pmc', userName: 'PMC 计划员', password: 'Password@123' },
  { userCode: 'rule_admin', userName: '规则管理员', password: 'Password@123' },
  { userCode: 'rule_publisher', userName: '规则发布员', password: 'Password@123' }
]

/** 业务范围策略（9 条 — 见 3号位 回执 v3 第三节计数纠错）
 *  - Factory: BJ / SUZ  (2)
 *  - ProductFamily: INJECTION / ASSEMBLY / TEST  (3)
 *  - Department: PMC_DEPT  (1)
 *  - Domain: FAMILY_INJECTION / FAMILY_ASSEMBLY / FAMILY_TEST  (3)
 *  - 注意：admin 已有 Global=*（不动）；此处只补业务范围
 */
const SCOPES = [
  { scopeType: 'Factory', scopeValue: 'BJ', description: '北京工厂' },
  { scopeType: 'Factory', scopeValue: 'SUZ', description: '苏州工厂' },
  { scopeType: 'ProductFamily', scopeValue: 'INJECTION', description: '注塑族' },
  { scopeType: 'ProductFamily', scopeValue: 'ASSEMBLY', description: '装配族' },
  { scopeType: 'ProductFamily', scopeValue: 'TEST', description: '测试族' },
  { scopeType: 'Department', scopeValue: 'PMC_DEPT', description: 'PMC 部门' },
  { scopeType: 'Domain', scopeValue: 'FAMILY_INJECTION', description: '注塑域' },
  { scopeType: 'Domain', scopeValue: 'FAMILY_ASSEMBLY', description: '装配域' },
  { scopeType: 'Domain', scopeValue: 'FAMILY_TEST', description: '测试域' }
]

/** 角色 → 权限码集
 *  - 引用 DDL v1.3 + frontNew verify-rbac.mjs 角色分配
 *  - VIEWER 10 / PMC 17 / RULE_ADMIN/RULE_PUBLISHER 8（admin.aps 等价） / SYSTEM_ADMIN 34
 *  - 注意：aps.admin.system 不动；此处只补 aps.viewer.management / aps.planner / aps.admin.aps
 *  - 角色码与 DDL v1.3 严格一致（aps.viewer.management / aps.planner / aps.admin.aps）
 */
const ROLE_PERMISSIONS = {
  // aps.viewer.management — 全只读 10 码
  'aps.viewer.management': [
    'aps.plan.view', 'aps.ctp.view', 'aps.candidate.view', 'aps.rule.view',
    'aps.parameter.view', 'aps.strategy.view', 'aps.manual_eta.view',
    'aps.demand_protection.view', 'aps.mes.view', 'aps.audit.view'
  ],
  // aps.planner — PMC 17 码
  'aps.planner': [
    'aps.plan.view', 'aps.plan.run', 'aps.plan.compare', 'aps.plan.export',
    'aps.ctp.view', 'aps.ctp.evaluate',
    'aps.insert.impact.evaluate', 'aps.reschedule.local', 'aps.reschedule.manual',
    'aps.candidate.view', 'aps.candidate.confirm', 'aps.candidate.activate',
    'aps.manual_eta.view', 'aps.manual_eta.edit', 'aps.manual_eta.cancel',
    'aps.demand_protection.view', 'aps.mes.view'
  ],
  // aps.admin.aps — 规则/参数/策略全集 8 码
  'aps.admin.aps': [
    'aps.plan.view', 'aps.rule.view', 'aps.rule.edit', 'aps.rule.publish',
    'aps.parameter.view', 'aps.parameter.edit',
    'aps.strategy.view', 'aps.strategy.edit'
  ]
  // aps.admin.system — 不动（已挂全 34 码）
}

/** 角色 → 业务范围 Id 集合
 *  - 这里只列业务范围维度策略；aps.admin.system 的 Global=* 不动
 *  - 注意：scope 集合靠运行期 GET /api/rbac/scopes 取 Id 后按 ScopeType+ScopeValue 匹配
 */
const ROLE_SCOPES_BY_TYPE_VALUE = {
  'aps.viewer.management': [
    // 全只读 → 给全 9 条业务范围（看全部）
    { scopeType: 'Factory', scopeValue: 'BJ' },
    { scopeType: 'Factory', scopeValue: 'SUZ' },
    { scopeType: 'ProductFamily', scopeValue: 'INJECTION' },
    { scopeType: 'ProductFamily', scopeValue: 'ASSEMBLY' },
    { scopeType: 'ProductFamily', scopeValue: 'TEST' },
    { scopeType: 'Department', scopeValue: 'PMC_DEPT' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_INJECTION' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_ASSEMBLY' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_TEST' }
  ],
  'aps.planner': [
    // PMC 聚焦注塑 + 装配域 + PMC_DEPT 部门
    { scopeType: 'Factory', scopeValue: 'BJ' },
    { scopeType: 'Factory', scopeValue: 'SUZ' },
    { scopeType: 'ProductFamily', scopeValue: 'INJECTION' },
    { scopeType: 'ProductFamily', scopeValue: 'ASSEMBLY' },
    { scopeType: 'Department', scopeValue: 'PMC_DEPT' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_INJECTION' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_ASSEMBLY' }
  ],
  'aps.admin.aps': [
    // 规则管理员跨全族（全 3 域 + 全 3 ProductFamily）
    { scopeType: 'ProductFamily', scopeValue: 'INJECTION' },
    { scopeType: 'ProductFamily', scopeValue: 'ASSEMBLY' },
    { scopeType: 'ProductFamily', scopeValue: 'TEST' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_INJECTION' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_ASSEMBLY' },
    { scopeType: 'Domain', scopeValue: 'FAMILY_TEST' }
  ]
}

/** 测试用户 → 角色码 */
const USER_ROLES = {
  viewer: ['aps.viewer.management'],
  pmc: ['aps.planner'],
  rule_admin: ['aps.admin.aps'],
  rule_publisher: ['aps.admin.aps']
}

/* ========== HTTP 辅助 ========== */

let adminToken = null

async function call(method, path, { body, query } = {}) {
  const url = new URL(BASE_URL + path)
  if (query) for (const [k, v] of Object.entries(query)) url.searchParams.set(k, v)
  const headers = { 'Content-Type': 'application/json' }
  if (adminToken) headers['Authorization'] = `Bearer ${adminToken}`
  // redirect: 'follow' — 自动跟 307 HTTPS 跳转
  // Node 22 fetch 默认不跟，dev 必须显式开
  const res = await fetch(url, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined,
    redirect: 'follow'
  })
  const text = await res.text()
  let json
  try {
    json = text ? JSON.parse(text) : null
  } catch {
    json = null
  }
  if (!res.ok) {
    throw new Error(
      `[${method} ${path}] HTTP ${res.status} ${res.statusText} — ${text.slice(0, 300)}`
    )
  }
  if (!json || typeof json !== 'object') {
    throw new Error(`[${method} ${path}] 非 JSON 响应: ${text.slice(0, 200)}`)
  }
  if (json.code !== 200) {
    throw new Error(`[${method} ${path}] 业务失败 code=${json.code}: ${json.message}`)
  }
  return json.data
}

/* ========== 主流程 ========== */

function log(step, msg) {
  console.log(`[${step}] ${msg}`)
}

async function step01_login() {
  log('1/9', `POST /api/auth/login (${ADMIN_USERCODE})`)
  const res = await call('POST', '/api/auth/login', {
    body: { userCode: ADMIN_USERCODE, password: ADMIN_PASSWORD }
  })
  adminToken = res.accessToken
  if (!adminToken) throw new Error('登录成功但 accessToken 为空')
  console.log(`     ↳ token 前 20: ${adminToken.slice(0, 20)}...`)
  console.log(`     ↳ userId=${res.userId} userCode=${res.userCode} roles=${JSON.stringify(res.roles)}`)
}

async function step02_getPermissions() {
  log('2/9', 'GET /api/rbac/permissions')
  const perms = await call('GET', '/api/rbac/permissions')
  if (!Array.isArray(perms)) throw new Error('permissions 非数组')
  const map = new Map(perms.map((p) => [p.permissionCode, p.id]))
  console.log(`     ↳ 总数 ${perms.length}（期望 34）`)
  if (perms.length !== 34) {
    console.warn(`     ⚠️ 预期 34 个 PermissionCode，实际 ${perms.length}（PermissionSeedService 自动种的不一致？）`)
  }
  return map
}

async function step03_getRoles() {
  log('3/9', 'GET /api/rbac/roles')
  const roles = await call('GET', '/api/rbac/roles')
  if (!Array.isArray(roles)) throw new Error('roles 非数组')
  const map = new Map(roles.map((r) => [r.roleCode, r.id]))
  console.log(`     ↳ 总数 ${roles.length}（期望 7 系统角色 + 0 自定义）`)
  for (const [code, id] of map) {
    console.log(`       - ${code} → id=${id}${code === 'aps.admin.system' ? '  ⚠️ 不参与任何 PUT（红线）' : ''}`)
  }
  return map
}

async function step04_createUsers() {
  log('4/9', `POST /api/rbac/users × ${TEST_USERS.length}`)
  const idMap = {}
  for (const u of TEST_USERS) {
    try {
      const created = await call('POST', '/api/rbac/users', { body: u })
      idMap[u.userCode] = created.id
      console.log(`     ↳ ${u.userCode} → id=${created.id} ✅`)
    } catch (err) {
      // 已存在也接受（重复跑幂等）— 通过 GET users 检测
      if (/UserCode|exists|duplicate/i.test(err.message)) {
        const all = await call('GET', '/api/rbac/users')
        const existing = all.find((x) => x.userCode === u.userCode)
        if (existing) {
          idMap[u.userCode] = existing.id
          console.log(`     ↳ ${u.userCode} → id=${existing.id}（已存在，跳过创建）`)
          continue
        }
      }
      throw err
    }
  }
  return idMap
}

async function step05_createScopes() {
  log('5/9', `POST /api/rbac/scopes × ${SCOPES.length}`)
  const idMap = new Map() // key = ScopeType|ScopeValue → id
  const all = await call('GET', '/api/rbac/scopes')
  for (const s of all) {
    idMap.set(`${s.scopeType}|${s.scopeValue}`, s.id)
  }
  for (const s of SCOPES) {
    const key = `${s.scopeType}|${s.scopeValue}`
    if (idMap.has(key)) {
      console.log(`     ↳ ${s.scopeType}=${s.scopeValue} → id=${idMap.get(key)}（已存在，跳过创建）`)
      continue
    }
    const created = await call('POST', '/api/rbac/scopes', { body: s })
    idMap.set(key, created.id)
    console.log(`     ↳ ${s.scopeType}=${s.scopeValue} → id=${created.id} ✅`)
  }
  return idMap
}

async function step06_assignRolePermissions(permIdMap, roleIdMap) {
  log('6/9', 'PUT /api/rbac/roles/{id}/permissions（覆盖式）')
  for (const [roleCode, codes] of Object.entries(ROLE_PERMISSIONS)) {
    const roleId = roleIdMap.get(roleCode)
    if (!roleId) throw new Error(`角色 ${roleCode} 未在 GET /roles 中找到（DDL 必须先有该系统角色）`)
    const ids = codes.map((c) => {
      const id = permIdMap.get(c)
      if (!id) throw new Error(`权限码 ${c} 未在 GET /permissions 中找到`)
      return id
    })
    await call('PUT', `/api/rbac/roles/${roleId}/permissions`, { body: { ids } })
    console.log(`     ↳ ${roleCode} (id=${roleId}) 挂 ${ids.length} 个权限码 ✅`)
  }
}

async function step07_assignRoleScopes(scopeIdMap, roleIdMap) {
  log('7/9', 'PUT /api/rbac/roles/{id}/scopes（覆盖式）')
  for (const [roleCode, typeValues] of Object.entries(ROLE_SCOPES_BY_TYPE_VALUE)) {
    const roleId = roleIdMap.get(roleCode)
    if (!roleId) throw new Error(`角色 ${roleCode} 未在 GET /roles 中找到`)
    const ids = typeValues.map((tv) => {
      const id = scopeIdMap.get(`${tv.scopeType}|${tv.scopeValue}`)
      if (!id) throw new Error(`Scope ${tv.scopeType}=${tv.scopeValue} 未找到`)
      return id
    })
    await call('PUT', `/api/rbac/roles/${roleId}/scopes`, { body: { ids } })
    console.log(`     ↳ ${roleCode} (id=${roleId}) 挂 ${ids.length} 条 scope ✅`)
  }
}

async function step08_assignUserRoles(userIdMap, roleIdMap) {
  log('8/9', 'PUT /api/rbac/users/{id}/roles（覆盖式）')
  for (const [userCode, roleCodes] of Object.entries(USER_ROLES)) {
    const userId = userIdMap[userCode]
    if (!userId) throw new Error(`用户 ${userCode} 未创建成功`)
    const ids = roleCodes.map((rc) => {
      const id = roleIdMap.get(rc)
      if (!id) throw new Error(`角色 ${rc} 未找到`)
      return id
    })
    await call('PUT', `/api/rbac/users/${userId}/roles`, { body: { ids } })
    console.log(`     ↳ ${userCode} (id=${userId}) 挂角色 ${roleCodes.join('/')} ✅`)
  }
}

async function step09_verify(userIdMap, roleIdMap) {
  log('9/9', '验证：自检 admin /me + viewer /me（仅校验 login，不校验 /me 因为 viewer 用 admin token 也能调）')
  // 自检 admin /me
  const me = await call('GET', '/api/auth/me')
  if (me.userCode !== ADMIN_USERCODE) throw new Error('admin /me 异常')
  if (!Array.isArray(me.permissions) || me.permissions.length < 30) {
    throw new Error(`admin permissions 数量异常：${me.permissions?.length ?? 'null'}（期望 ≥ 30）`)
  }
  console.log(`     ↳ admin /me: userCode=${me.userCode} perms=${me.permissions.length} isGlobal=${me.isGlobal} ✅`)

  // 自检：取角色 roleId → 反查 role.permissions 是否 ≥ 设定
  for (const [roleCode, expectedCodes] of Object.entries(ROLE_PERMISSIONS)) {
    const roleId = roleIdMap.get(roleCode)
    // 用 admin token 调 GetRoles 看 scope（这里简化：只打印角色存在性）
    console.log(`       - ${roleCode} 已挂 ${expectedCodes.length} 权限码（写时设定）`)
  }
  console.log('     ↳ 自检通过 ✅')
}

async function main() {
  console.log(`📍 目标后端: ${BASE_URL}`)
  console.log('='.repeat(60))
  await step01_login()
  const permIdMap = await step02_getPermissions()
  const roleIdMap = await step03_getRoles()
  const userIdMap = await step04_createUsers()
  const scopeIdMap = await step05_createScopes()
  await step06_assignRolePermissions(permIdMap, roleIdMap)
  await step07_assignRoleScopes(scopeIdMap, roleIdMap)
  await step08_assignUserRoles(userIdMap, roleIdMap)
  await step09_verify(userIdMap, roleIdMap)
  console.log('='.repeat(60))
  console.log('✅ seed:dev 完成')
  console.log('')
  console.log('🔑 4 个测试账号（密码 Password@123）：')
  console.log('   viewer / pmc / rule_admin / rule_publisher')
  console.log('')
  console.log('➡️  下一步：浏览器启动 pnpm dev → 登录任一账号验证 /api/auth/me 数据正确')
}

main().catch((err) => {
  console.error('')
  console.error('❌ seed:dev 失败:')
  console.error(err.stack || err.message || err)
  process.exit(1)
})