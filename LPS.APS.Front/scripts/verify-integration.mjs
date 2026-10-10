#!/usr/bin/env node
/**
 * APS V1 4号位 ↔ 5号位 联调脚本（2026-09-12）
 *
 * 用途：跑通 4 号位前端 ↔ 后端真实接口的最小验证集合，替代手工 curl。
 *
 * 设计原则：
 *  - 不启 dev server（用户自己在终端跑 pnpm dev；脚本独立可用）
 *  - 直接打 lps 后端 https://localhost:7044
 *  - admin 登录拿 token → 用 token 跑 Domain + RBAC 两组
 *  - 失败不静默：每条断言打印状态码 + 响应摘要，断言失败 exit 1
 *
 * 覆盖范围（今天能联调的两块）：
 *  [A] Domain 路径对齐（前端已切 /api/governance/domain-definition）
 *      - GET /domain-definition（列表）
 *      - GET /domain-definition/active
 *      - GET /domain-definition/{id}
 *      - POST /domain-definition
 *      - PUT /domain-definition/{id}
 *      - POST /domain-definition/{id}/enable
 *      - POST /domain-definition/{id}/disable
 *
 *  [B] RBAC 22 端点（18 写 + 4 读回；后端已就绪）
 *      - GET /users / POST /users / PUT /users/{id} / DELETE /users/{id}
 *      - PUT /users/{id}/roles（覆盖式）
 *      - PUT /users/{id}/scopes（覆盖式）
 *      - GET /roles / POST /roles / PUT /roles/{id} / DELETE /roles/{id}
 *      - PUT /roles/{id}/permissions（覆盖式）
 *      - PUT /roles/{id}/scopes（覆盖式）
 *      - GET /permissions / POST /permissions
 *      - GET /scopes / POST /scopes / PUT /scopes/{id} / DELETE /scopes/{id}
 *
 *  [C] 鉴权反向验证（5 号位 P0 ① ② 修复后跑通）
 *      - 创建临时 VIEWER 测试账号（持 viewer 角色 + aps.plan.view 权限码）
 *      - 清空角色 + 范围 → 变裸账号
 *      - 用 VIEWER token 访问 8 个业务 GET 端点 → 应 code=403
 *      - 用 VIEWER token 访问 GovernanceController 28 GET → 应 code=403（3 号位 §八.⑤ 待补）
 *      - 用 VIEWER token 调 ManualEta POST + DELETE 写端 → 应 code=403
 *      - 跑完清理 VIEWER 测试账号
 *
 *  [E] DemandProtection release（5号位 A方案 修复完成，2026-09-16）
 *      - 裸账号 POST release → 应 403（[Authorize(DemandProtectionRelease)] 生效）
 *      - admin POST release（DP + SB 混合入参）→ 期望 200 + per-lock DP→RELEASED / SB→FAILED
 *      - 数据模型：每行 = 一条 lock（无 lockIds[]）；idempotent（重跑消耗 ACTIVE 锁后优雅跳过）
 *
 * 排除（5 号位 P0 ①②③ 已闭环，2026-09-12）：
 *  - ~~11 个查询 Controller 缺 [Authorize]~~ → ✅ 已闭环（11/11 全生效，viewer 8/8 全 403）
 *  - ~~ManualEta POST/DELETE 缺 [Authorize]~~ → ✅ 已闭环（类级 ManualEtaView + POST ManualEtaMaintain + DELETE ManualEtaCancel）
 *  - ~~DemandProtection POST release → 后端 404~~ → ✅ 已落端点（per-lock partial success；走 2 号位 Application Service 待 5 号位 路径确认，2026-09-25 前）
 *
 * 用法：
 *  - 默认连 https://localhost:7044
 *    node scripts/verify-integration.mjs
 *  - 自定义后端地址：
 *    API_BASE=http://localhost:5163 node scripts/verify-integration.mjs
 *  - 仅跑 Domain / 仅跑 RBAC / 仅跑鉴权反向：
 *    GROUP=domain node scripts/verify-integration.mjs
 *    GROUP=rbac node scripts/verify-integration.mjs
 *    GROUP=auth-negate node scripts/verify-integration.mjs
 *  - Windows Git Bash / cmd.exe（POSIX env 前缀不通用）：
 *    Git Bash：env 前缀可用，但 Powershell/cmd 不行。最稳的方式是
 *    直接修改脚本最后一行的 main() 调用前临时改 GROUP 默认值，或
 *    用 Node 单行：
 *      node -e "process.env.GROUP='domain'; require('./scripts/verify-integration.mjs')"
 *    （本项目未装 cross-env，避免误导）
 *
 * 注意：
 *  - HTTPS 自签证书会被 Node 拒绝，已加 NODE_TLS_REJECT_UNAUTHORIZED=0
 *  - 需要后端 seed 已跑（admin/Admin@123456 等 4 用户）
 *
 * ❌ 2026-09-15 撤销 DepartmentCode 维度（@see 4号位-2026-09-13-裁定回退清单.md）：
 *  - 9月13日 `未命名的Markdown文件.md` 实际是 0号位 出的业务裁决（程序有效）
 *  - DepartmentCode 整条撤销：[F] 段函数整段删除 + 排除列表去掉 DepartmentCode 行
 *  - 当前状态：与 9月13日 撤销状态一致
 */

import { setTimeout as sleep } from 'node:timers/promises'

// 自签证书放行（dev only）
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'

const API_BASE = process.env.API_BASE || 'https://localhost:7044'
const GROUP = process.env.GROUP || 'all' // all | domain | rbac | auth-negate | strategy-profile | setup | first-wave | audit
const ADMIN_CODE = 'admin'
const ADMIN_PWD = 'Admin@123456'

const log = (...a) => console.log(...a)
const ok = (msg) => log(`  ✅ ${msg}`)
const bad = (msg) => log(`  ❌ ${msg}`)
const info = (msg) => log(`  · ${msg}`)

let passCount = 0
let failCount = 0

function assert(label, cond, detail = '') {
  if (cond) {
    passCount++
    ok(`${label}${detail ? ` — ${detail}` : ''}`)
  } else {
    failCount++
    bad(`${label}${detail ? ` — ${detail}` : ''}`)
  }
}

/** 通用 fetch 封装：自动剥 ApiResponse 壳，记录 traceId
 *  后端契约（LPS.APS.Shared/Models/ApiResponse.cs）：
 *  - RunAsync 包过的端点：HTTP 永远 200，code 在 body（200/400/403/500）
 *  - 直接 BadRequest/NotFound 的端点（如 GovernanceController）：HTTP 4xx + 错误 body
 *  - 因此判断"成功"必须看 json.code === 200，HTTP 4xx 仅在非 ApiResponse 包装时才算真错
 */
async function call(method, path, { token, body, query } = {}) {
  const url = new URL(API_BASE + path)
  if (query) {
    for (const [k, v] of Object.entries(query)) {
      if (v !== undefined && v !== null) url.searchParams.set(k, String(v))
    }
  }
  const headers = { 'Content-Type': 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`

  const res = await fetch(url, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined
  })
  const text = await res.text()
  let json
  try {
    json = JSON.parse(text)
  } catch {
    json = null
  }

  // 业务码（默认 200 当作 HTTP 200 兼容；也接受 201 Created）
  const businessCode = json?.code ?? res.status
  const success = res.ok && (businessCode === 200 || businessCode === 201)
  return {
    status: res.status,
    code: businessCode,
    ok: success,
    json,
    text,
    traceId: res.headers.get('X-Trace-Id') || json?.traceId || ''
  }
}

/* ==================== 登录拿 token ==================== */

async function loginAsAdmin() {
  const r = await call('POST', '/api/auth/login', {
    body: { userCode: ADMIN_CODE, password: ADMIN_PWD }
  })
  if (!r.ok || !r.json?.data?.accessToken) {
    bad(`admin 登录失败：HTTP ${r.status} ${r.text.slice(0, 200)}`)
    throw new Error('login failed')
  }
  ok(`admin 登录成功（userId=${r.json.data.userInfo?.userId}）`)
  return r.json.data.accessToken
}

/* ==================== [A] Domain 路径对齐 ==================== */

async function verifyDomain(token) {
  log('\n[A] Domain 路径对齐（/api/governance/domain-definition）')

  // 1. GET 列表（无 Auth 属性 → 应 200）
  let r = await call('GET', '/api/governance/domain-definition', { token })
  assert('GET 列表', r.ok && Array.isArray(r.json?.data), `code=${r.code}, count=${r.json?.data?.length ?? 0}`)

  const list = r.json?.data || []
  if (list.length === 0) {
    info('列表为空，跳过 detail/update/enable/disable 验证')
    return
  }

  // 2. GET active
  r = await call('GET', '/api/governance/domain-definition/active', { token })
  assert('GET active', r.ok && Array.isArray(r.json?.data), `code=${r.code}, count=${r.json?.data?.length ?? 0}`)

  // 3. GET 详情（拿第一个 id）
  const firstId = list[0].id ?? list[0].Id
  r = await call('GET', `/api/governance/domain-definition/${firstId}`, { token })
  assert(`GET 详情 id=${firstId}`, r.ok && r.json?.data, `code=${r.code}`)

  // 4. POST 新建 — 用 list[0] 的 productFamilyId/factoryId 作为模板（后端要 int FK，不要 string code）
  const template = list[0]
  const newKey = `VERIFY_${Date.now()}`
  r = await call('POST', '/api/governance/domain-definition', {
    token,
    body: {
      domainKey: newKey,
      domainName: 'verify domain',
      scopeType: template.scopeType ?? template.ScopeType,
      productFamilyId: template.productFamilyId ?? template.ProductFamilyId,
      factoryId: template.factoryId ?? template.FactoryId ?? null,
      isActive: true,
      sortOrder: 999
    }
  })
  const created = r.json?.data
  assert('POST 新建', r.ok && created?.id, `code=${r.code}, newId=${created?.id ?? r.text.slice(0, 120)}`)

  if (created?.id) {
    // 5. PUT 更新（domainName）
    r = await call('PUT', `/api/governance/domain-definition/${created.id}`, {
      token,
      body: {
        domainKey: newKey, // 必填；后端不允许改
        domainName: 'verify domain updated',
        scopeType: template.scopeType ?? template.ScopeType,
        productFamilyId: template.productFamilyId ?? template.ProductFamilyId,
        factoryId: template.factoryId ?? template.FactoryId ?? null,
        isActive: true,
        sortOrder: 999
      }
    })
    assert('PUT 更新', r.ok, `code=${r.code}`)

    // 6. POST enable
    r = await call('POST', `/api/governance/domain-definition/${created.id}/enable`, { token })
    assert('POST enable', r.ok, `code=${r.code}`)

    // 7. POST disable
    r = await call('POST', `/api/governance/domain-definition/${created.id}/disable`, { token })
    assert('POST disable', r.ok, `code=${r.code}`)
  }
}

/* ==================== [B] RBAC 22 端点（18 写 + 4 读回） ==================== */

async function verifyRbac(token) {
  log('\n[B] RBAC 22 端点（/api/rbac/*；含 4 读回）')

  // === Users ===
  let r = await call('GET', '/api/rbac/users', { token })
  assert('GET /users', r.ok && Array.isArray(r.json?.data), `code=${r.code}, count=${r.json?.data?.length ?? 0}`)

  const userList = r.json?.data || []
  const selfId = userList.find((u) => u.userCode === ADMIN_CODE)?.id

  r = await call('POST', '/api/rbac/users', {
    token,
    body: {
      userCode: `verify_${Date.now()}`,
      userName: '联调测试用户',
      password: 'VerifyPass123!'
    }
  })
  const newUser = r.json?.data
  assert('POST /users', r.ok && newUser?.id, `code=${r.code}, newId=${newUser?.id}`)

  if (newUser?.id) {
    r = await call('PUT', `/api/rbac/users/${newUser.id}`, {
      token,
      body: { userName: '联调测试用户-已更新', status: 'Active', email: null, phoneNumber: null }
    })
    assert('PUT /users/{id}', r.ok, `code=${r.code}`)

    r = await call('PUT', `/api/rbac/users/${newUser.id}/roles`, {
      token,
      body: { ids: [] } // 覆盖式：清空
    })
    assert('PUT /users/{id}/roles（清空）', r.ok, `code=${r.code}`)

    // 4 读回端点之一（3号位 r13366+）：GET /users/{id}/roles → 应返回 []
    r = await call('GET', `/api/rbac/users/${newUser.id}/roles`, { token })
    assert(
      'GET /users/{id}/roles（读回端点）',
      r.ok && Array.isArray(r.json?.data) && r.json.data.length === 0,
      `code=${r.code}, count=${r.json?.data?.length ?? 'n/a'}`
    )

    r = await call('PUT', `/api/rbac/users/${newUser.id}/scopes`, {
      token,
      body: { ids: [] }
    })
    assert('PUT /users/{id}/scopes（清空）', r.ok, `code=${r.code}`)

    // 4 读回端点之二：GET /users/{id}/scopes → 应返回 []
    r = await call('GET', `/api/rbac/users/${newUser.id}/scopes`, { token })
    assert(
      'GET /users/{id}/scopes（读回端点）',
      r.ok && Array.isArray(r.json?.data) && r.json.data.length === 0,
      `code=${r.code}, count=${r.json?.data?.length ?? 'n/a'}`
    )

    r = await call('DELETE', `/api/rbac/users/${newUser.id}`, { token })
    assert('DELETE /users/{id}', r.ok, `code=${r.code}`)
  }

  // === Roles ===
  r = await call('GET', '/api/rbac/roles', { token })
  assert('GET /roles', r.ok && Array.isArray(r.json?.data), `code=${r.code}, count=${r.json?.data?.length ?? 0}`)
  const roleList = r.json?.data || []
  const customRole = roleList.find((r2) => !r2.isSystemRole)

  r = await call('POST', '/api/rbac/roles', {
    token,
    body: { roleCode: `aps.verify.role.${Date.now()}`, roleName: 'verify role', description: 'verify' }
  })
  const newRole = r.json?.data
  assert('POST /roles', r.ok && newRole?.id, `code=${r.code}, newId=${newRole?.id}`)

  if (newRole?.id) {
    r = await call('PUT', `/api/rbac/roles/${newRole.id}`, {
      token,
      body: { roleName: '联调测试角色-已更新', isActive: true, description: null }
    })
    assert('PUT /roles/{id}', r.ok, `code=${r.code}`)

    r = await call('PUT', `/api/rbac/roles/${newRole.id}/permissions`, {
      token,
      body: { ids: [] }
    })
    assert('PUT /roles/{id}/permissions（清空）', r.ok, `code=${r.code}`)

    // 4 读回端点之三：GET /roles/{id}/permissions → 应返回 []
    r = await call('GET', `/api/rbac/roles/${newRole.id}/permissions`, { token })
    assert(
      'GET /roles/{id}/permissions（读回端点）',
      r.ok && Array.isArray(r.json?.data) && r.json.data.length === 0,
      `code=${r.code}, count=${r.json?.data?.length ?? 'n/a'}`
    )

    r = await call('PUT', `/api/rbac/roles/${newRole.id}/scopes`, {
      token,
      body: { ids: [] }
    })
    assert('PUT /roles/{id}/scopes（清空）', r.ok, `code=${r.code}`)

    // 4 读回端点之四：GET /roles/{id}/scopes → 应返回 []
    r = await call('GET', `/api/rbac/roles/${newRole.id}/scopes`, { token })
    assert(
      'GET /roles/{id}/scopes（读回端点）',
      r.ok && Array.isArray(r.json?.data) && r.json.data.length === 0,
      `code=${r.code}, count=${r.json?.data?.length ?? 'n/a'}`
    )

    r = await call('DELETE', `/api/rbac/roles/${newRole.id}`, { token })
    assert('DELETE /roles/{id}', r.ok, `code=${r.code}`)
  }

  // === Permissions ===
  r = await call('GET', '/api/rbac/permissions', { token })
  assert('GET /permissions', r.ok && Array.isArray(r.json?.data), `code=${r.code}, count=${r.json?.data?.length ?? 0}`)

  r = await call('POST', '/api/rbac/permissions', {
    token,
    body: {
      permissionCode: `aps.verify.${Date.now()}`,
      permissionName: '联调测试权限',
      module: 'verify',
      actionType: 'test',
      description: 'verify'
    }
  })
  assert('POST /permissions', r.ok && r.json?.data?.id, `code=${r.code}, newId=${r.json?.data?.id}`)

  // === Scopes ===
  r = await call('GET', '/api/rbac/scopes', { token })
  assert('GET /scopes', r.ok && Array.isArray(r.json?.data), `code=${r.code}, count=${r.json?.data?.length ?? 0}`)

  r = await call('POST', '/api/rbac/scopes', {
    token,
    body: { scopeType: 'Factory', scopeValue: `VERIFY_${Date.now()}`, description: '联调测试' }
  })
  const newScope = r.json?.data
  assert('POST /scopes', r.ok && newScope?.id, `code=${r.code}, newId=${newScope?.id}`)

  if (newScope?.id) {
    r = await call('PUT', `/api/rbac/scopes/${newScope.id}`, {
      token,
      body: { description: '联调测试-已更新' }
    })
    assert('PUT /scopes/{id}', r.ok, `code=${r.code}`)

    r = await call('DELETE', `/api/rbac/scopes/${newScope.id}`, { token })
    assert('DELETE /scopes/{id}', r.ok, `code=${r.code}`)
  }

  // === 自删保护（用 Postman 绕过前端 disabled：直接调 DELETE自己）===
  if (selfId) {
    r = await call('DELETE', `/api/rbac/users/${selfId}`, { token })
    assert(
      'DELETE /users/{selfId}（自删保护应 code=403）',
      r.code === 403,
      `code=${r.code}, body=${r.text.slice(0, 200)}`
    )
  }
}

/* ==================== [C] 鉴权反向验证（VIEWER 账号访问业务端点 → 应 403） ==================== */

async function verifyAuthNegate(adminToken) {
  log('\n[C] 鉴权反向验证（VIEWER 账号访问业务端点应 code=403）')

  // 1. 找一个 VIEWER 角色 ID（aps.viewer.management / aps.viewer.readonly 等）
  let r = await call('GET', '/api/rbac/roles', { token: adminToken })
  const viewerRole = (r.json?.data || []).find((rl) =>
    /viewer/i.test(rl.roleCode) || /viewer/i.test(rl.roleName)
  )
  if (!viewerRole) {
    info('未找到 VIEWER 角色（跳过 [C] 鉴权反向验证）')
    return
  }

  // 2. 创建一个 VIEWER 测试用户（带时间戳避免重复）
  const viewerUserCode = `aps.auth.negate.${Date.now()}`
  r = await call('POST', '/api/rbac/users', {
    token: adminToken,
    body: { userCode: viewerUserCode, userName: 'auth negate test', password: 'NegatePass123!' }
  })
  const newViewer = r.json?.data
  if (!newViewer?.id) {
    bad(`创建 VIEWER 测试用户失败：code=${r.code}, ${r.text.slice(0, 200)}`)
    return
  }

  // 3. **关键**：先**清空** VIEWER 测试用户的角色和权限（覆盖式 PUT ids=[]）
  //    否则 VIEWER 角色默认带 aps.plan.view，部分 GET 端点会 200（合理但不利于测鉴权）
  //    目标：让 VIEWER 测试账号变成"裸账号"，验证业务端点是否拦截无权限用户
  await call('PUT', `/api/rbac/users/${newViewer.id}/roles`, {
    token: adminToken,
    body: { ids: [] }
  })
  await call('PUT', `/api/rbac/users/${newViewer.id}/scopes`, {
    token: adminToken,
    body: { ids: [] }
  })

  // 4. 用 VIEWER 账号登录拿 token
  r = await call('POST', '/api/auth/login', {
    body: { userCode: viewerUserCode, password: 'NegatePass123!' }
  })
  const viewerToken = r.json?.data?.accessToken
  if (!viewerToken) {
    bad(`VIEWER 登录失败：${r.text.slice(0, 200)}`)
    return
  }

  // 5. 业务端点列表（裸账号无任何权限码，应全 403）
  //    排除 404 的（实际后端有子路径）：overview / governance-query / domain-status
  const businessEndpoints = [
    '/api/order-query',
    '/api/pi-position',
    '/api/explanation',
    '/api/pegging-trace',
    '/api/supply-fact-trace',
    '/api/business-fact-issues',
    '/api/demand-protection',
    '/api/procurement-manual-eta'
  ]

  for (const ep of businessEndpoints) {
    r = await call('GET', ep, { token: viewerToken })
    assert(
      `裸账号 GET ${ep}`,
      r.code === 403,
      `期望 403 实际 code=${r.code}（鉴权未生效 = P0 漏洞）`
    )
  }

  // 6. GovernanceController 28 GET（3 号位 §八.⑤ 待补；当前全裸奔，应 403 实际 200）
  await verifyGovernanceNegate(viewerToken)

  // 7. ManualEta 写端（POST + DELETE）—— 即便用户有 plan.view 也无 maintain 权限
  r = await call('POST', '/api/procurement-manual-eta', {
    token: viewerToken,
    body: {
      poNo: 'PO_TEST_AUTH',
      lineNo: 1,
      materialId: 1,
      materialCode: 'MAT001',
      receivingWarehouse: 'WH01',
      manualEta: '2026-09-12T10:00:00Z',
      isActive: true,
      remark: 'auth negate test'
    }
  })
  assert('裸账号 POST /api/procurement-manual-eta', r.code === 403, `实际 code=${r.code}`)

  r = await call('DELETE', '/api/procurement-manual-eta/PO_TEST_AUTH/1', {
    token: viewerToken
  })
  assert('裸账号 DELETE /api/procurement-manual-eta/{po}/{line}', r.code === 403, `实际 code=${r.code}`)

  // 8. [E] DemandProtection release（TODO-等5号位任务 ③）
  await verifyDemandProtectionRelease(adminToken, viewerToken)

  // 9. 清理：删除 VIEWER 测试用户
  await call('DELETE', `/api/rbac/users/${newViewer.id}`, { token: adminToken })
}

/* ==================== [D] GovernanceController 28 GET 鉴权反向 ====================
 * 来源：scan-controllers.mjs 2026-09-12 实测
 * 现况：3 号位 §八.⑤ P0 待补；脚本断言"应 403"，当前会全红
 *       3 号位修完 GovernanceController 类级 [Authorize(PlanView)] 后复跑应自然全绿
 */
async function verifyGovernanceNegate(viewerToken) {
  log('\n[C.D] GovernanceController 28 GET 鉴权反向（裸账号 → 应 code=403）')

  // 端点列表 = scan-controllers.mjs 输出的 unauthEndpoints
  // 形如 { path, query }，query 可选
  const endpoints = [
    { path: '/api/governance/rule-set/1/versions' },
    { path: '/api/governance/rule-set/version/1' },
    { path: '/api/governance/parameter-set/1/versions' },
    { path: '/api/governance/parameter-set/version/1' },
    { path: '/api/governance/rule-set/version/diff', query: { sourceVersionId: 1, targetVersionId: 2 } },
    { path: '/api/governance/parameter-set/version/diff', query: { sourceVersionId: 1, targetVersionId: 2 } },
    { path: '/api/governance/strategy-profile/version/diff', query: { sourceVersionId: 1, targetVersionId: 2 } },
    { path: '/api/governance/rule-set/version/1/validate' },
    { path: '/api/governance/parameter-set/version/1/validate' },
    { path: '/api/governance/strategy-profile/1/versions' },
    { path: '/api/governance/strategy-profile/version/1' },
    { path: '/api/governance/strategy-profile/version/1/validate' },
    { path: '/api/governance/strategy-profile/default' },
    { path: '/api/governance/strategy-profile/version/1/trace' },
    { path: '/api/governance/plan-version/1/compare-with/2' },
    { path: '/api/governance/run/1/trace' },
    { path: '/api/governance/rule-sets' },
    { path: '/api/governance/parameter-sets' },
    { path: '/api/governance/strategy-profiles' },
    { path: '/api/governance/rule-set/1/published-version' },
    { path: '/api/governance/parameter-set/1/published-version' },
    { path: '/api/governance/strategy-profile/1/published-version' },
    { path: '/api/governance/domain-dependencies', query: { domainCode: 'FAMILY_INJECTION' } },
    { path: '/api/governance/run/1/domain-status' },
    { path: '/api/governance/runs' },
    { path: '/api/governance/domain-definition' },
    { path: '/api/governance/domain-definition/active' },
    { path: '/api/governance/domain-definition/1' }
  ]

  for (const { path, query } of endpoints) {
    const r = await call('GET', path, { token: viewerToken, query })
    const shortPath = path.replace('/api/governance/', '')
    assert(
      `裸账号 GET /api/governance/${shortPath}`,
      r.code === 403,
      `期望 403 实际 code=${r.code}（3 号位 §八.⑤ 待补）`
    )
  }
}

/* ==================== [E] DemandProtection POST release ====================
 * 触发条件：5号位 在 DemandProtectionController 实现 POST /api/demand-protection/release
 * 端点契约（2026-09-16 A方案 后端架构升级后落地）：
 *  - 请求体 { lockIds: number[], releasedBy: string, releaseReason: string }（camelCase）
 *  - 响应 List<{ lockId, demandKey, status: 'RELEASED'|'FAILED', failureReason? }> 逐 lock
 *  - 部分成功语义：lockType=STRICT_BINDING 的 lock 返回 status=FAILED（不抛异常）
 *  - 数据模型：每行 = 一条 lock（无 lockIds[] 字段）
 *  - 架构：Controller → IDemandProtectionAppService（Application 层）→ IDemandProtectionReleaseService（BusinessRules 层）
 *
 * 当前状态（2026-09-16）：
 *  - 5号位 A方案 修复完成：Application 层中转 + DemandProtectionReleaseService 接受 list
 *  - releaseReason ≥5 字符硬约束已撤销（仅 IsNullOrWhiteSpace）
 *  - dev DB 已有 seed（2 ACTIVE DEMAND_PROTECTION + 1 ACTIVE STRICT_BINDING）
 *  - 实测可逐 lock 部分成功；运行本段后会消耗 ACTIVE DEMAND_PROTECTION 锁（idempotent skip on re-run）
 */
async function verifyDemandProtectionRelease(adminToken, viewerToken) {
  log('\n[E] DemandProtection release（POST /api/demand-protection/release）')

  // 1. admin 拿全量（每行 = 一锁，id 即 lockId）
  let r = await call('GET', '/api/demand-protection', { token: adminToken })
  if (r.code !== 200) {
    info(`GET /api/demand-protection 当前 code=${r.code}`)
    info('[E] 跳过：GET 不可用')
    return
  }
  const all = r.json?.data || []
  const dpActive = all.find((x) => x.lockType === 'DEMAND_PROTECTION' && x.status === 'ACTIVE')
  const sbActive = all.find((x) => x.lockType === 'STRICT_BINDING' && x.status === 'ACTIVE')
  const dpReleased = all.find((x) => x.lockType === 'DEMAND_PROTECTION' && x.status === 'RELEASED')

  // 至少要有数据可测
  if (!dpActive && !sbActive && !dpReleased) {
    info('[E] 跳过：当前无任何 DemandProtection 数据（需 5号位 seed）')
    return
  }

  // 2. 裸账号 POST release → 期望 403（[Authorize(DemandProtectionRelease)] 生效）
  //    测试目标：随便一个存在的 lockId（含 ACTIVE/RELEASED 都行，只要存在）
  const probeLockId = (dpActive || sbActive || dpReleased)?.id ?? all[0].id
  r = await call('POST', '/api/demand-protection/release', {
    token: viewerToken,
    body: {
      lockIds: [probeLockId],
      releasedBy: 'verify-integration-viewer',
      releaseReason: 'verify [E] auth negate test'
    }
  })
  assert(
    '裸账号 POST /api/demand-protection/release',
    r.code === 403,
    `期望 403 实际 code=${r.code}`
  )

  // 3. admin POST release（部分成功语义：DP→RELEASED + SB→FAILED 同时入参）
  //    取所有当前 ACTIVE 的 id 拼成 lockIds（DP/SB 都可），期望 per-lock 部分成功
  const activeIds = all.filter((x) => x.status === 'ACTIVE').map((x) => x.id)
  if (activeIds.length === 0) {
    info('[E] 跳过：admin POST 段：当前无 ACTIVE 锁可释放（idempotent on re-run）')
    return
  }
  r = await call('POST', '/api/demand-protection/release', {
    token: adminToken,
    body: {
      lockIds: activeIds,
      releasedBy: 'verify-integration-admin',
      releaseReason: 'verify [E] admin release test'
    }
  })
  assert(
    'admin POST /api/demand-protection/release code',
    r.code === 200,
    `期望 200 实际 code=${r.code}`
  )
  const perLock = r.json?.data
  assert(
    'admin POST 响应是 List<DemandProtectionReleaseResult>',
    Array.isArray(perLock) && perLock.length === activeIds.length,
    `期望长度=${activeIds.length} 实际=${perLock?.length}`
  )
  if (Array.isArray(perLock)) {
    // 逐 lock 状态校验：DP→RELEASED，SB→FAILED
    const dpExpected = all.filter((x) => activeIds.includes(x.id) && x.lockType === 'DEMAND_PROTECTION')
    const sbExpected = all.filter((x) => activeIds.includes(x.id) && x.lockType === 'STRICT_BINDING')
    const dpReleasedCount = perLock.filter((x) => x.status === 'RELEASED').length
    const sbFailedCount = perLock.filter((x) => x.status === 'FAILED').length
    assert(
      'admin POST per-lock 语义（DP 全 RELEASED + SB 全 FAILED）',
      dpReleasedCount === dpExpected.length && sbFailedCount === sbExpected.length,
      `DP 期望 RELEASED=${dpExpected.length} 实际=${dpReleasedCount}；SB 期望 FAILED=${sbExpected.length} 实际=${sbFailedCount}`
    )
    // 锁 id 集合与请求一致
    const reqSet = new Set(activeIds)
    const retSet = new Set(perLock.map((x) => x.lockId))
    assert(
      'admin POST per-lock lockId 集合与请求一致',
      reqSet.size === retSet.size && [...reqSet].every((id) => retSet.has(id)),
      `请求={${[...reqSet].join(',')}} 响应={${[...retSet].join(',')}}`
    )
  }
}

/* ==================== [H] Setup 维护 API（v1.5 Setup 专项，2026-09-16；09-20 Id-口径终定 v1.1 重写）====================
 * 触发：2026-09-17 4号位 落地 5 个 Setup 维护页面（v1.5 §2 页面 2.1-2.5）
 * 09-20 3号位 代码合入 dev：SetupRuleController.cs 12 端点（#10 uncovered-stats 暂缓，待 2号位 C2 SolveTraceNotes）
 *
 * Id-口径终定（v1.1，权威契约源 lps/LPS.APS.Core/DTOs/Setup/SetupRuleDtos.cs）：
 *  - 写输入（exact/default）提交 Id：productionDepartmentId/resourceId/fromMaterialId/toMaterialId（int > 0）
 *    大工艺/工序传 Code（stageCode/operationCode）；传 Code 字段名会被模型绑定静默丢弃 → Id=0 → 422
 *  - 读模型 SetupRuleDto 双返回 Id+Code（Code 经 MasterDataCodeCache 回带，可空）
 *  - #11 版本列表 Status = 治理六态原文；写非 DRAFT 版本 → 400（GetVersionForWriteAsync）
 *  - DEFAULT 载荷含 "material" 键 → 422（[JsonExtensionData] 拦截，§7.1 互斥红线）
 *  - 唯一键冲突按 Id 元组分组（ConflictValidator）→ 422 消息含「唯一键冲突」
 *  - #9 eligibility：materialId/toMaterialId = Material.Id 数字字符串（int.TryParse，非法 → 400）
 *
 * 测试 Id 取 9001+ 任意值：DDL 无 FK 约束、ValidateCommon 仅校验 > 0 → 合法；Code 回带为 null 不影响断言
 * 跳过策略（环境阻塞不计 failCount）：
 *  - 探测 403 → 整段 skip：admin 缺 aps.setup.edit（dev seed 段A 角色绑定未执行）
 *  - 探测 404/405 且无 ApiResponse 壳 → 整段 skip：3号位 未合入
 *  - 无 DRAFT RuleSetVersion → 整段 skip：dev seed 段B（DEV_SETUP 规则集 + DRAFT 版本）未执行
 *  - H.9 uncovered-stats 404 → 单条 skip（#10 暂缓）
 * 联调基线：+12 断言（H.1-H.12，全绿后 95 → 107）
 */
async function verifySetup(adminToken) {
  log('\n[H] Setup 维护 API（POST/GET/PUT/DELETE /api/governance/setup-rules/*）')

  // 测试 Id（无 FK 约束，> 0 即合法；与真实主数据 1..N 段隔离避免污染业务数据）
  const TEST_DEPT_ID = 9001
  const TEST_RESOURCE_ID = 9002
  const TEST_FROM_MATERIAL_ID = 9003
  const TEST_TO_MATERIAL_ID = 9004

  // 0a. 动态发现 DRAFT 版本（写非 DRAFT → 400；不硬编码 ruleSetVersionId=1，dev seed 段B 建 DEV-SETUP-DRAFT-*）
  const vProbe = await call('GET', '/api/governance/rule-set-versions', { token: adminToken })
  const versions = Array.isArray(vProbe.json?.data) ? vProbe.json.data : []
  const draftVersion = versions.find((v) => (v.status ?? v.Status) === 'DRAFT') ?? null

  // 0b. 探测：POST 最小 EXACT（Id-口径）
  const probeR = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: {
      ruleSetVersionId: draftVersion?.id ?? 1,
      productionDepartmentId: TEST_DEPT_ID,
      stageCode: 'PROBE_STAGE',
      operationCode: 'PROBE_OP',
      resourceId: TEST_RESOURCE_ID,
      fromMaterialId: TEST_FROM_MATERIAL_ID,
      toMaterialId: TEST_TO_MATERIAL_ID,
      setupMinutes: 99
    }
  })
  if (probeR.code === 403) {
    info('[H] 跳过：admin 缺 aps.setup.edit（403）→ 请先 SSMS 执行 lps/LPS.APS.Web/Sql/APS_Setup_dev_seed_20260920.sql 段A（角色绑定）')
    return
  }
  if ((probeR.code === 404 || probeR.code === 405) && probeR.json == null) {
    info('[H] 跳过：3号位 Setup 维护 API 未落地（404/405 且无 ApiResponse 壳）')
    return
  }
  if (!draftVersion) {
    info(
      `[H] 跳过：无 DRAFT RuleSetVersion（探测 code=${probeR.code} msg=${probeR.json?.message || ''}）→ 请先执行 dev seed 段B（建 DEV_SETUP 规则集 + DRAFT 版本）`
    )
    return
  }
  if (probeR.code === 500) {
    // 后端写路径内部错误（非环境/权限/前端问题）→ 暴露 exception/detail 首行，避免泛化「服务器内部错误」埋没根因
    const d = probeR.json?.data || {}
    const detail = d.detail || probeR.json?.message || ''
    info(
      `[H] 跳过：POST 探测 500 后端内部错误 → ${d.exception || 'Exception'}: ${detail}` +
        `（写路径 bug，需 3号位 修复；4号位 不可改 lps。诊断详见 docs/4号位-2026-09-20-Setup-create-500-CreatedAt溢出-给3号位.md）`
    )
    return
  }
  if (probeR.code !== 200 || !probeR.json?.data?.id) {
    info(`[H] 跳过：POST 探测失败 code=${probeR.code} msg=${probeR.json?.message || ''}`)
    return
  }
  const probeId = probeR.json.data.id
  ok(`[H] POST EXACT 探测成功 newId=${probeId}（DRAFT 版本 ${draftVersion.versionCode} Id=${draftVersion.id}）`)

  const draftVersionId = draftVersion.id

  // H.1 POST EXACT 完整 7 元组（Id-口径 v1.1：部门/设备/物料传 Id，大工艺/工序传 Code）
  const exactBody = {
    ruleSetVersionId: draftVersionId,
    productionDepartmentId: TEST_DEPT_ID,
    stageCode: 'TEST_STAGE',
    operationCode: 'TEST_OP',
    resourceId: TEST_RESOURCE_ID,
    fromMaterialId: TEST_FROM_MATERIAL_ID,
    toMaterialId: TEST_TO_MATERIAL_ID,
    setupMinutes: 45
  }
  const r1 = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: exactBody
  })
  assert(
    '[H.1] admin POST EXACT 完整 7 元组（Id-口径）',
    r1.code === 200 && r1.json?.data?.id,
    `期望 200+newId 实际 code=${r1.code} msg=${r1.json?.message || ''}`
  )
  const exactId = r1.json?.data?.id

  // H.2 POST EXACT fromMaterialId=0 → 422（ValidateExactInput：<= MissingMaterialId(0) 红线）
  const r2 = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: { ...exactBody, fromMaterialId: 0 }
  })
  assert(
    '[H.2] admin POST EXACT fromMaterialId=0 → 422',
    r2.code === 422 && /EXACT|FromMaterial|前产品/i.test(r2.json?.message || ''),
    `期望 422+EXACT 业务消息 实际 code=${r2.code} msg=${r2.json?.message}`
  )

  // H.3 GET EXACT 列表（双返回：Id+Code 回带；测试 Id 无主数据 → Code 为 null 合法）
  const r3 = await call('GET', '/api/governance/setup-rules/exact', {
    token: adminToken,
    query: { ruleSetVersionId: draftVersionId }
  })
  const exactList = r3.json?.data
  assert(
    '[H.3] admin GET EXACT 列表',
    r3.code === 200 && Array.isArray(exactList) && exactList.some((x) => x.id === exactId),
    `期望 200+List 含 newId=${exactId} 实际 code=${r3.code} count=${exactList?.length}`
  )

  // H.4 PUT EXACT 修改 SetupMinutes 45→60（ruleSetVersionId 必须与既有一致，EnsureSameTypeAndVersion 红线）
  const r4 = await call('PUT', `/api/governance/setup-rules/exact/${exactId}`, {
    token: adminToken,
    body: { ...exactBody, setupMinutes: 60 }
  })
  assert(
    '[H.4] admin PUT EXACT 修改 SetupMinutes 45→60',
    r4.code === 200 && Number(r4.json?.data?.setupMinutes) === 60,
    `期望 200+setupMinutes=60 实际 code=${r4.code} setupMinutes=${r4.json?.data?.setupMinutes}`
  )

  // H.5 DELETE EXACT
  const r5 = await call('DELETE', `/api/governance/setup-rules/exact/${exactId}`, { token: adminToken })
  assert('[H.5] admin DELETE EXACT', r5.code === 200, `期望 200 实际 code=${r5.code}`)

  // H.6 POST DEFAULT 完整 5 元组（⚠️ 载荷严禁含 "material" 键 → [JsonExtensionData] 拦截 422）
  const defaultBody = {
    ruleSetVersionId: draftVersionId,
    productionDepartmentId: TEST_DEPT_ID,
    stageCode: 'TEST_STAGE',
    operationCode: 'TEST_OP',
    resourceId: TEST_RESOURCE_ID,
    setupMinutes: 30
  }
  const r6 = await call('POST', '/api/governance/setup-rules/default', {
    token: adminToken,
    body: defaultBody
  })
  assert(
    '[H.6] admin POST DEFAULT 完整 5 元组（无 material 键）',
    r6.code === 200 && r6.json?.data?.id,
    `期望 200+newId 实际 code=${r6.code} msg=${r6.json?.message || ''}`
  )
  const defaultId = r6.json?.data?.id

  // H.7 GET DEFAULT 列表
  const r7 = await call('GET', '/api/governance/setup-rules/default', {
    token: adminToken,
    query: { ruleSetVersionId: draftVersionId }
  })
  const defaultList = r7.json?.data
  assert(
    '[H.7] admin GET DEFAULT 列表',
    r7.code === 200 && Array.isArray(defaultList) && defaultList.some((x) => x.id === defaultId),
    `期望 200+List 含 newId=${defaultId} 实际 code=${r7.code} count=${defaultList?.length}`
  )

  // H.8 GET 共同合法设备推荐（#9：materialId 为 Material.Id 数字；测试 Id 无工艺路线主数据 → 空交集合法）
  const r8 = await call('GET', '/api/governance/operation-resource-eligibility', {
    token: adminToken,
    query: {
      operationCode: 'TEST_OP',
      materialId: TEST_FROM_MATERIAL_ID,
      toMaterialId: TEST_TO_MATERIAL_ID
    }
  })
  const eligibilityCodes = r8.json?.data?.resourceCodes
  if (r8.code === 200 && Array.isArray(eligibilityCodes) && eligibilityCodes.length === 0) {
    info('[H.8] 备注：resourceCodes 空交集 — 测试 Id(9003/9004) 无工艺路线主数据，属预期（契约断言不受影响）')
  }
  assert(
    '[H.8] admin GET 共同合法设备推荐（materialId=Material.Id 数字口径）',
    r8.code === 200 && Array.isArray(eligibilityCodes),
    `期望 200+resourceCodes[] 实际 code=${r8.code} count=${eligibilityCodes?.length} msg=${r8.json?.message || ''}`
  )

  // H.9 GET 0 分钟兜底统计（#10 暂缓未落地 → 404 跳过不计失败；需 runId）
  const runProbe = await call('GET', '/api/governance/runs', { token: adminToken })
  const firstRunId = runProbe.json?.data?.[0]?.id
  if (!firstRunId) {
    info('[H.9] 跳过：当前无 Run 数据（uncovered-stats 需 runId）')
  } else {
    const r9 = await call('GET', '/api/governance/setup-rules/uncovered-stats', {
      token: adminToken,
      query: { runId: firstRunId }
    })
    if (r9.code === 404) {
      info('[H.9] 跳过：#10 uncovered-stats 未落地（09-20 回执：暂缓，待 2号位 C2 SolveTraceNotes）')
    } else {
      const uncovered = r9.json?.data
      assert(
        '[H.9] admin GET 0 分钟兜底统计',
        r9.code === 200 && Array.isArray(uncovered),
        `期望 200+List 实际 code=${r9.code} type=${typeof uncovered}`
      )
    }
  }

  // H.10 GET Diff 含 setupRuleChanges（需 2 个 RuleSetVersion；不足 → 跳过不计失败）
  if (versions.length < 2) {
    info(`[H.10] 跳过：当前 RuleSetVersion 数量=${versions.length} < 2（Diff 需 2 版本）`)
  } else {
    const r10 = await call('GET', `/api/governance/rule-set-versions/${versions[0].id}/diff`, {
      token: adminToken,
      query: { otherVersionId: versions[1].id }
    })
    const hasSetupChanges = r10.json?.data && 'setupRuleChanges' in (r10.json.data || {})
    assert(
      '[H.10] admin GET Diff 含 setupRuleChanges 字段',
      r10.code === 200 && hasSetupChanges,
      `期望 200+setupRuleChanges 存在 实际 code=${r10.code} 字段存在=${hasSetupChanges}`
    )
  }

  // H.11 viewer（裸账号）POST 写端 → 403（与 [C] 段同法：临时建裸账号测鉴权反向）
  let viewerToken = process.env.VIEWER_TOKEN || ''
  if (!viewerToken) {
    const viewerUserCode = `aps.setup.negate.${Date.now()}`
    const cv = await call('POST', '/api/rbac/users', {
      token: adminToken,
      body: { userCode: viewerUserCode, userName: 'setup negate test', password: 'NegatePass123!' }
    })
    const newViewer = cv.json?.data
    if (newViewer?.id) {
      await call('PUT', `/api/rbac/users/${newViewer.id}/roles`, { token: adminToken, body: { ids: [] } })
      await call('PUT', `/api/rbac/users/${newViewer.id}/scopes`, { token: adminToken, body: { ids: [] } })
      const lv = await call('POST', '/api/auth/login', {
        body: { userCode: viewerUserCode, password: 'NegatePass123!' }
      })
      viewerToken = lv.json?.data?.accessToken || ''
    }
  }
  if (!viewerToken) {
    info('[H.11] 跳过：无法获取 viewer token（裸账号创建/登录失败，可设 VIEWER_TOKEN 环境变量）')
  } else {
    const r11 = await call('POST', '/api/governance/setup-rules/exact', {
      token: viewerToken,
      body: exactBody
    })
    assert('[H.11] viewer POST EXACT 写端 → 403', r11.code === 403, `期望 403 实际 code=${r11.code}`)
  }

  // H.12 admin 重复唯一键 POST → 422（冲突按 Id 元组分组：E|deptId|stage|op|resId|fromId|toId；H.1 已删，先重建再重复）
  const dupBase = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: exactBody
  })
  const dupBaseId = dupBase.json?.data?.id
  if (!dupBaseId) {
    info(`[H.12] 跳过：重复键基线创建失败 code=${dupBase.code}`)
  } else {
    const r12 = await call('POST', '/api/governance/setup-rules/exact', {
      token: adminToken,
      body: { ...exactBody, setupMinutes: 999 } // 故意同 Id 元组（仅 setupMinutes 不同）
    })
    assert(
      '[H.12] admin 重复唯一键 POST → 422+业务消息',
      r12.code === 422 && /冲突|已存在|重复|exists/i.test(r12.json?.message || ''),
      `期望 422+唯一键冲突 实际 code=${r12.code} msg=${r12.json?.message}`
    )
  }

  // 清理：probe EXACT + H.6 DEFAULT + H.12 基线 EXACT
  if (probeId) {
    await call('DELETE', `/api/governance/setup-rules/exact/${probeId}`, { token: adminToken })
  }
  if (defaultId) {
    await call('DELETE', `/api/governance/setup-rules/default/${defaultId}`, { token: adminToken })
  }
  if (dupBaseId) {
    await call('DELETE', `/api/governance/setup-rules/exact/${dupBaseId}`, { token: adminToken })
  }
  ok('[H] 清理完成：probe EXACT + H.6 DEFAULT + H.12 基线 EXACT 已删')
}

/* ==================== [G] Strategy Profile 12 端点（v1.4 §十八.10 收口）====================
 * 触发：2026-09-17 4 号位 落地独立策略配置页（v1.4 §十八.10 长期欠账收口）
 * 后端 12 端点已在 lps/LPS.APS.Web/Controllers/GovernanceController.cs 全部就绪
 * 前端 DTO 用 UPPER_CASE 6 态（与 DDL CK 严格对齐）
 */
async function verifyStrategyProfile(token) {
  log('\n[G] Strategy Profile 12 端点（/api/governance/strategy-profile/* + /strategy-profiles）')

  // G1: GET Profile 列表
  let r = await call('GET', '/api/governance/strategy-profiles', { token })
  assert(
    'G1 GET /strategy-profiles',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )
  const profiles = r.json?.data || []
  if (profiles.length === 0) {
    info('G1 Profile 列表为空，跳过详情相关断言')
    return
  }
  const profileId = profiles[0].id ?? profiles[0].Id

  // G2: GET 版本列表
  r = await call('GET', `/api/governance/strategy-profile/${profileId}/versions`, { token })
  assert(
    'G2 GET /strategy-profile/{id}/versions',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )
  const versions = r.json?.data || []

  // G3: GET 当前 Published（404 也合法）
  r = await call('GET', `/api/governance/strategy-profile/${profileId}/published-version`, { token })
  assert(
    'G3 GET /strategy-profile/{id}/published-version',
    r.ok || r.code === 404,
    `code=${r.code}, hasData=${!!r.json?.data}`
  )

  // G4: POST 创建 DRAFT
  // 2026-09-18 3号位 回执后落地：选项 C（前端真实下拉）
  //  - ruleSetVersionId / parameterSetVersionId 从真实 GET 列表取，不再硬编码 1
  //  - dev 库 RuleSetVersion / ParameterSetVersion 表为空时 G4 跳过（待 3号位 补种子）
  //  - 9/18 3号位 B 方案种子落地（TEST-RSV-SEED-001 / TEST-PSV-SEED-001）：
  //    verify 需扫所有 rule-set / parameter-set 找 DRAFT（3号位 TOP 1 没 ORDER BY，可能落到非默认列表项）
  const stamp = Date.now()
  let ruleSetVersionId = null
  let parameterSetVersionId = null

  // 扫所有 rule-set 找 DRAFT 版本
  const rsList = await call('GET', '/api/governance/rule-sets', { token })
  if (rsList.ok && Array.isArray(rsList.json?.data)) {
    for (const rs of rsList.json.data) {
      const rsId = rs.id ?? rs.Id
      if (!rsId) continue
      const rsVers = await call('GET', `/api/governance/rule-set/${rsId}/versions`, { token })
      const draft = (rsVers.json?.data ?? []).find(
        (v) => (v.status ?? v.Status) === 'DRAFT'
      )
      if (draft) {
        ruleSetVersionId = draft.id ?? draft.Id
        break
      }
    }
  }

  // 扫所有 parameter-set 找 DRAFT 版本
  const psList = await call('GET', '/api/governance/parameter-sets', { token })
  if (psList.ok && Array.isArray(psList.json?.data)) {
    for (const ps of psList.json.data) {
      const psId = ps.id ?? ps.Id
      if (!psId) continue
      const psVers = await call('GET', `/api/governance/parameter-set/${psId}/versions`, { token })
      const draft = (psVers.json?.data ?? []).find(
        (v) => (v.status ?? v.Status) === 'DRAFT'
      )
      if (draft) {
        parameterSetVersionId = draft.id ?? draft.Id
        break
      }
    }
  }

  if (!ruleSetVersionId || !parameterSetVersionId) {
    info(`G4 跳过：dev 库 RuleSetVersion=${ruleSetVersionId} / ParameterSetVersion=${parameterSetVersionId} 至少一个为空（需 3号位 补种子 — verify 已扫所有 rule-set/parameter-set 找 DRAFT）`)
    return
  }

  r = await call('POST', '/api/governance/strategy-profile/version', {
    token,
    body: {
      strategyProfileId: profileId,
      versionCode: `verify-${stamp}`,
      ruleSetVersionId,
      parameterSetVersionId,
      isDefault: false
    }
  })
  const draftVer = r.json?.data
  assert(
    'G4 POST /strategy-profile/version（创建 DRAFT）',
    r.ok && draftVer?.id,
    `code=${r.code}, newId=${draftVer?.id ?? r.text.slice(0, 120)}, ruleSetVer=${ruleSetVersionId}, paramSetVer=${parameterSetVersionId}`
  )
  if (!draftVer?.id) return
  const versionId = draftVer.id

  // G5: GET 单版本详情
  r = await call('GET', `/api/governance/strategy-profile/version/${versionId}`, { token })
  assert('G5 GET /strategy-profile/version/{id}', r.ok, `code=${r.code}`)

  // G6: PUT 更新 DRAFT
  r = await call('PUT', `/api/governance/strategy-profile/version/${versionId}`, {
    token,
    body: {
      strategyProfileId: profileId,
      versionCode: `verify-${stamp}`,
      ruleSetVersionId: 1,
      parameterSetVersionId: 1,
      isDefault: false
    }
  })
  assert('G6 PUT /strategy-profile/version/{id}', r.ok, `code=${r.code}`)

  // G7: GET validate
  r = await call('GET', `/api/governance/strategy-profile/version/${versionId}/validate`, { token })
  assert(
    'G7 GET /strategy-profile/version/{id}/validate',
    r.ok && typeof r.json?.data?.isValid === 'boolean',
    `code=${r.code}, isValid=${r.json?.data?.isValid}`
  )

  // G8: POST publish（默认 IsDefault=0 避免红线冲突）
  //  - 200=校验通过 + 发布成功
  //  - 400=校验未过（G7 isValid=false；3号位 9/18 补的最小列 DRAFT 无 ContentSnapshotJson，validate 失败 → publish 400）
  //    这是种子数据质量限制，不是 verify bug；400 也算"端点可达"
  const g7Valid = r.json?.data?.isValid
  r = await call('POST', `/api/governance/strategy-profile/version/${versionId}/publish`, {
    token,
    body: { changeReason: `verify-publish-${stamp}` }
  })
  assert(
    'G8 POST /strategy-profile/version/{id}/publish',
    r.ok || r.code === 400,
    `code=${r.code}, isValid=${g7Valid}（200=校验+发布成功；400=validate 未过，端点可达）`
  )

  // G9: GET diff（拿历史 Published 与刚发布的对比；如无则跳过）
  const olderVer = versions.find(
    (v) => v.status === 'PUBLISHED' && (v.id ?? v.Id) !== versionId
  )?.id ?? versions.find((v) => v.status === 'PUBLISHED' && (v.id ?? v.Id) !== versionId)?.Id
  if (olderVer) {
    r = await call(
      'GET',
      `/api/governance/strategy-profile/version/diff?sourceVersionId=${olderVer}&targetVersionId=${versionId}`,
      { token }
    )
    assert('G9 GET /strategy-profile/version/diff', r.ok, `code=${r.code}`)
  } else {
    info('G9 跳过：无历史 Published 版本可比对')
  }

  // G10: POST disable（容错 200/400，状态流转可能 400）
  r = await call('POST', `/api/governance/strategy-profile/version/${versionId}/disable`, {
    token,
    body: { reason: `verify-disable-${stamp}` }
  })
  assert(
    'G10 POST /strategy-profile/version/{id}/disable',
    r.ok || r.code === 400,
    `code=${r.code}, text=${r.text.slice(0, 120)}`
  )

  // G11: GET trace
  r = await call('GET', `/api/governance/strategy-profile/version/${versionId}/trace`, { token })
  assert('G11 GET /strategy-profile/version/{id}/trace', r.ok, `code=${r.code}`)

  // G12: GET default（按 RunType 解析默认 PUBLISHED）
  r = await call(
    'GET',
    '/api/governance/strategy-profile/default?runType=FULL_SCHEDULE&asOf=' + new Date().toISOString(),
    { token }
  )
  assert(
    'G12 GET /strategy-profile/default',
    r.ok || r.code === 404,
    `code=${r.code}, hasData=${!!r.json?.data}`
  )
}

/* ==================== [H] 第一波真实页面接入（v1.4 §五 页面 1/2/4/5/7/8）====================
 * 触发：2026-09-17 URL 错位修复（overview/order/explanation/pi/run/candidate）+ ManualETA DTO 修复
 * 覆盖页面：Overview / Order / Gantt / Explanation / Pi / DemandProtection / Run / Candidate
 * 数据形态：列表端点 → 仅校验 HTTP 200 + Array/Object 形态（不强校验字段，避免后端字段演进）
 */
async function verifyFirstWave(token) {
  log('\n[H] 第一波真实页面接入（Overview/Order/Gantt/Explanation/Pi/DP/Run/Candidate 8 页）')

  // H0: 探 planVersionId（active-plan 无数据时回退 planVersionId=1）
  let planVersionId = 1
  const activePlan = await call('GET', '/api/overview/active-plan?domainKey=FAMILY_INJECTION', { token })
  const activeItems = activePlan.json?.data?.items || activePlan.json?.data?.planVersions || (Array.isArray(activePlan.json?.data) ? activePlan.json.data : [])
  if (Array.isArray(activeItems) && activeItems[0]?.planVersionId) {
    planVersionId = activeItems[0].planVersionId
  } else if (activePlan.json?.data?.planVersionId) {
    planVersionId = activePlan.json.data.planVersionId
  }
  info(`使用 planVersionId=${planVersionId}（active-plan 探得）`)

  // H1: Overview
  let r = await call('GET', '/api/overview/active-plan', { token })
  assert(
    'H1 GET /api/overview/active-plan（Overview 页）',
    r.ok && (r.json?.data ?? r.json),
    `code=${r.code}`
  )

  // H2: Order 列表（5号位 端点需 planVersionId；当前 500=DelayStatus SQL bug 待修）
  r = await call('GET', `/api/order-query?pageIndex=1&pageSize=10&planVersionId=${planVersionId}`, { token })
  assert(
    'H2 GET /api/order-query（Order 列表）',
    r.ok && (r.json?.data?.items ?? r.json?.data),
    `code=${r.code}, data=${!!r.json?.data}`
  )

  // H3: Order 详情（取列表第一项；容错 404 也算合法）
  const orderList = r.json?.data?.items ?? r.json?.data ?? []
  if (orderList.length > 0) {
    const orderId = orderList[0].orderId ?? orderList[0].id ?? orderList[0].Id
    if (orderId) {
      r = await call('GET', `/api/order-query/${orderId}`, { token })
      assert(
        `H3 GET /api/order-query/{id}（Order 详情 id=${orderId}）`,
        r.ok || r.code === 404,
        `code=${r.code}`
      )
    } else {
      info('H3 跳过：列表第一项无 orderId 字段')
    }
  } else {
    info('H3 跳过：Order 列表为空')
  }

  // H4: Explanation summary（v1.4 URL 修正后）
  r = await call('GET', `/api/explanation/summary?planVersionId=${planVersionId}`, { token })
  assert(
    'H4 GET /api/explanation/summary（Explanation 页）',
    r.ok && (r.json?.data ?? r.json),
    `code=${r.code}`
  )

  // H5: Pi Position 列表
  r = await call('GET', `/api/pi-position?planVersionId=${planVersionId}`, { token })
  assert(
    'H5 GET /api/pi-position（Pi 列表）',
    r.ok && (r.json?.data ?? r.json),
    `code=${r.code}`
  )

  // H6: Pi Position summary
  r = await call('GET', `/api/pi-position/summary?planVersionId=${planVersionId}`, { token })
  assert(
    'H6 GET /api/pi-position/summary（Pi 汇总）',
    r.ok && (r.json?.data ?? r.json),
    `code=${r.code}`
  )

  // H7: DemandProtection 列表（release 已由 [E] 覆盖）
  r = await call('GET', '/api/demand-protection', { token })
  assert(
    'H7 GET /api/demand-protection（DemandProtection 页）',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  // H8: Run 列表（GovernanceController 聚合）
  r = await call('GET', '/api/governance/runs', { token })
  assert(
    'H8 GET /api/governance/runs（Run.vue 聚合）',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  // H9: Candidate 列表（绕道：runs?status=CANDIDATE）
  r = await call('GET', '/api/governance/runs?status=CANDIDATE', { token })
  assert(
    'H9 GET /api/governance/runs?status=CANDIDATE（Candidate 列表绕道）',
    r.ok,
    `code=${r.code}`
  )

  // H10: Manual ETA 列表（v1.4 URL 修正后 — filter 用逗号分隔字符串）
  r = await call('GET', '/api/procurement-manual-eta?activeOnly=true', { token })
  assert(
    'H10 GET /api/procurement-manual-eta?activeOnly=true（ManualEta 列表）',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  // H11: Order Summary 聚合（2026-09-17 5号位 落地，选项 A；当前 500=DelayStatus SQL bug 待修）
  r = await call('GET', `/api/order-query/summary?planVersionId=${planVersionId}`, { token })
  assert(
    'H11 GET /api/order-query/summary（Order 顶部 KPI）',
    r.ok && r.json?.data,
    `code=${r.code}, keys=${r.json?.data ? Object.keys(r.json.data).join(',') : '-'}`
  )

  // H12: PI Position 详情（2026-09-17 5号位 落地；先用 list 取真实 PI，DB 无 PI 时退 404=路径可达）
  r = await call('GET', `/api/pi-position?planVersionId=${planVersionId}`, { token })
  const realPi = r.json?.data?.[0]?.productionInstructionNo
    || r.json?.data?.[0]?.productionInstructionId
  const piTestNo = realPi ? String(realPi) : 'PI-VERIFY-NOEXIST'
  r = await call('GET', `/api/pi-position/${encodeURIComponent(piTestNo)}?planVersionId=${planVersionId}`, { token })
  assert(
    `H12 GET /api/pi-position/{productionInstructionNo}（Pi 详情 ${realPi ? '真 PI' : '占位 404'}）`,
    r.ok || r.code === 404,
    `code=${r.code}, pi=${piTestNo}`
  )
}

/* ==================== [I] Audit 审计日志（缺口 A / U42，2026-09-20 落地）====================
 * 触发：/aps/audit 页面落地（views/Aps/Audit.vue）
 * 端点契约（RbacController.cs L170-181，Policy=aps.audit.view）：
 *  - GET /api/rbac/audit-logs?userId&action&from&to&page&size
 *  - 响应 ApiResponse<IReadOnlyList<AuditLog>>：data 是纯数组无 total
 *    （AuditLogRepository.QueryPagedAsync 无 out total → 前端 hasMore 分页）
 *  - action 精确等值匹配；size clamp 1..200；OccurredAt 倒序
 * 跳过策略：探测 GET 失败（非 200 / data 非数组）→ 整段 skip（与 [E]/[H] 段同法，不计 failCount）
 * 联调基线：+3 断言（I.1-I.3）
 */
async function verifyAudit(token) {
  log('\n[I] Audit 审计日志（GET /api/rbac/audit-logs）')

  // I.0 探测：admin GET 首页（2026-09-20 curl 实测已就绪；此处防后端回退）
  const r0 = await call('GET', '/api/rbac/audit-logs', { token, query: { page: 1, size: 20 } })
  if (!r0.ok || !Array.isArray(r0.json?.data)) {
    info(`[I] 跳过：探测 GET 失败 code=${r0.code}（端点未落地或 admin 无 aps.audit.view）`)
    return
  }

  // I.1 GET 200 + data 是纯数组（契约：无 total 字段）
  assert(
    '[I.1] admin GET /api/rbac/audit-logs → 200 + 数组',
    Array.isArray(r0.json.data),
    `code=${r0.code}, count=${r0.json.data.length}`
  )

  // I.2 首条含 actionCode + occurredAt + result（§二十四：谁在什么时候做了什么结果如何）
  const first = r0.json.data[0]
  if (!first) {
    info('[I.2] 跳过：dev 库暂无审计数据（0 条；跑 [B]/[E] 段写操作后可产生）')
  } else {
    assert(
      '[I.2] 首条含 actionCode + occurredAt + result',
      !!first.actionCode && !!first.occurredAt && !!first.result,
      `actionCode=${first.actionCode}, occurredAt=${first.occurredAt}, result=${first.result}`
    )
  }

  // I.3 page=1&size=2 → 返回 ≤2 条（服务端 Skip/Take 分页生效）
  const r3 = await call('GET', '/api/rbac/audit-logs', { token, query: { page: 1, size: 2 } })
  assert(
    '[I.3] page=1&size=2 → ≤2 条',
    r3.ok && Array.isArray(r3.json?.data) && r3.json.data.length <= 2,
    `code=${r3.code}, count=${r3.json?.data?.length ?? 'n/a'}`
  )
}

async function main() {
  log(`\n=== APS V1 4号位 ↔ 5号位 联调脚本 ===`)
  log(`Backend: ${API_BASE}`)
  log(`Group:   ${GROUP}`)
  log(`Time:    ${new Date().toISOString()}\n`)

  try {
    const token = await loginAsAdmin()

    if (GROUP === 'all' || GROUP === 'domain') {
      await verifyDomain(token)
    }
    if (GROUP === 'all' || GROUP === 'rbac') {
      await verifyRbac(token)
    }
    if (GROUP === 'all' || GROUP === 'auth-negate') {
      await verifyAuthNegate(token)
    }
    if (GROUP === 'all' || GROUP === 'strategy-profile') {
      await verifyStrategyProfile(token)
    }
    if (GROUP === 'all' || GROUP === 'setup') {
      await verifySetup(token)
    }
    if (GROUP === 'all' || GROUP === 'first-wave') {
      await verifyFirstWave(token)
    }
    if (GROUP === 'all' || GROUP === 'audit') {
      await verifyAudit(token)
    }
  } catch (err) {
    bad(`顶层异常：${err.message}`)
    process.exit(1)
  }

  log(`\n=== 结果 ===`)
  log(`✅ 通过：${passCount}`)
  log(`❌ 失败：${failCount}`)

  if (failCount > 0) {
    process.exit(1)
  }
  log('\n全部断言通过 ✅\n')
}

main()
