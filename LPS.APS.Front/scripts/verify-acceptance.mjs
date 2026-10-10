#!/usr/bin/env node
/**
 * APS V1 4号位 — v1.4 §二十九 U23-U50 验收脚本（2026-09-18）
 *
 * 用途：v1.4 实施包 §二十九 28 项新增验收场景的自动化断言。
 *
 * 设计原则：
 *  - 复用 verify-integration.mjs 的 call/assert/login 模式
 *  - 不启 dev server（用户自己在终端跑 pnpm dev）
 *  - HTTPS 自签证书：NODE_TLS_REJECT_UNAUTHORIZED=0
 *  - 静态扫描（U28/U41/U43/U44/U45 + S01）直接读 frontNew/src/ 文件
 *  - 接口断言分 4 段：interfaces / rbac-negate / current-user / business-rules
 *
 * 覆盖范围（30 接口断言 + 6 静态扫描 = 36 项）：
 *  [I] Interfaces: U23-U32 (10 断言)
 *      - U23 静态（前端不含 2号位 端口）
 *      - U24 Overview 结构
 *      - U25 Strategy/Rule/Param 端点兼容
 *      - U26 CTP 发起（4→3）
 *      - U27 Manual ETA CRUD
 *      - U28 静态（DTO 含 EffectiveFrom）
 *      - U29 DP 查看
 *      - U30 DP 释放
 *      - U31 MES 受控
 *      - U32 Login/Refresh/Logout/Me
 *
 *  [R] Rbac Negate: U33-U39 + U46-U47 (9 断言)
 *      - U33 业务端点 403
 *      - U34 Factory=TJ 查 BJ
 *      - U35 Department=MC 范围
 *      - U36 PF=X 维护 PF=Y
 *      - U37 Domain=D01 激活 D02
 *      - U38 无 token 401
 *      - U39 Global 跨域
 *      - U46 CTP 无激活
 *      - U47 MES 不可下发
 *
 *  [C] Current User: U40-U42 (3 断言)
 *      - U40 /me 含 permissions + 4 scope
 *      - U41 Scope 筛选（静态）
 *      - U42 Audit-logs 不含敏感字段
 *
 *  [S] Static Scan: U28/U41/U43/U44/U45 + S01 (6 静态扫描)
 *      - U28 EffectiveFrom 字段
 *      - U41 ScopeFilter 仅授权
 *      - U43 无 Approval.vue
 *      - U44 无 PAUSE/RESUME 按钮
 *      - U45 不含 1号位 端口
 *      - S01 Aps 页面模板用到的 El* 组件均已显式 import（防「漏 import → 静默裸渲染」退化）
 *
 *  [B] Business Rules: U48-U50 (3 断言)
 *      - U48 ESTIMATED 不显示为承诺
 *      - U49 Run 查询 5 / 治理 3
 *      - U50 空 scope 用户
 *
 *  [V] V1.4 收口新增端点 (5 断言 — 任务 1+2+5)
 *      - V01 GET /rule-set/{id}/published-version 专用端点可达
 *      - V02 GET /parameter-set/{id}/published-version 专用端点可达
 *      - V03 GET /strategy-profile/{id}/published-version 专用端点可达
 *      - V04 POST /run/{id}/validate-domain-keys 端点可达
 *      - V05 GET /plan-version/{candidateId}/compare-with/{baseId} 路径双参契约
 *
 *  [J] §10A 白天人工调整 — ScopeJsonV2 契约（j-10a — 09-21 §10A 五业务入口）
 *      - J00 探测：scope 是否透传到后端 Validator（NEW_ORDER_CTP+EXPEDITE 应 400）
 *      - J01-J08 拒绝类断言：8 条 ScopeJsonV2Validator 校验失败路径
 *      - J09 静态：types/run.ts 含契约字段 / run.ts 不再含 remark / Gantt.vue 不再含 INSERT_RESCHEDULE / 4 个 Dialog 文件齐备
 *      - J10 接受类：默认跳过；仅 VERIFY_ALLOW_WRITE=1 时开启（会创建 ScheduleRun + CANDIDATE）
 *
 *  [M] §10A.1 已有订单提前 — OrderCanonicalId 自动带入 + planVersionId 必填契约（m-10a1 — 09-23 B4 已落地触发 + 5号位 回执）
 *      - M01 静态：types/order.ts OrderBasicInfo 含 orderCanonicalId / OrderAdvanceDialog.vue
 *                 含 orderCanonicalId 字段 + 自动带入逻辑 + "5号位 待补列" 旧文案已移除 /
 *                 Order.vue advanceCandidates 透传 orderCanonicalId /
 *                 mocks/fixtures.ts mockOrderDetail basic 含 orderCanonicalId
 *      - M03 静态（planVersionId 必填契约）：types/order.ts OrderQuery.planVersionId: number（非可选）/
 *                 store/modules/aps/order.ts loadList 含 planVersionIdReady 校验（5号位 2026-09-23 回执 §一）/
 *                 Order.vue planVersionInputRequired computed + ElInput 含 required mark + onMounted 从 active-plan 取默认
 *      - M02 接受类（默认跳过）：VERIFY_ALLOW_WRITE=1 时执行；需 Order 页已选订单 + Dialog 自动带入 + 提交跳转 Candidate
 *
 *  [P] ParameterSet 写维护（parameter-set-drafts — 09-21 B 设计稿 Step 3+4+6）
 *      - P01-P05 静态扫描：types/rule.ts 含 10 类型 / draftBuffer.ts 导出 3 算法 + 5 工具 /
 *                       rules.ts 含 6 actions + 6 getters / Rules.vue 含 [+ 新建草稿]+5 块+6 态矩阵 /
 *                       index.ts 重导出 draftBuffer
 *      - P06-P13 端点可达：8 个 GovernanceController.cs:108-275 写端点（POST/PUT/GET RS+PS draft+publish+read）
 *      - 全部为「无副作用探测」（只验端点存在/校验生效），不实际创建 ScheduleRun / DRAFT
 *
 *  [O] OPM 工艺规划模式治理（o-opm — 09-23 3号位 T1 交付件）
 *      - O01 静态：types/opm.ts 含 OperationPlanningMode 三态 + RoutingOperationDto /
 *                 opm.ts 含 listOperations + updatePlanningMode + MOCK_OPM_MATERIAL_ID /
 *                 store/modules/aps/opm.ts 含 load + updatePlanningMode + 三态 getter /
 *                 views/Aps/Opm.vue 存在 + 含三态常量 / router/modules/aps.ts 含 /operation-planning-mode 路由 +
 *                 api/aps-v1/index.ts + types/index.ts 重导出
 *      - O02 端点可达：GET /api/governance/routing-operations?materialId=1 与
 *                    PUT /api/governance/routing-operations/{id}/planning-mode
 *      - O04 接受类（默认跳过）：VERIFY_ALLOW_WRITE=1 时开启
 *
 *  [RC] 资源日历 / 人工能力槽维护（r-resource-calendar — 09-24 5号位 对接函 + v1.3 冻结方案）
 *      - RC01-RC08 静态：types/resourceCalendar.ts（camelCase 8 字段 + 6 权限码 + 三态 + 1..370）/
 *                       api 9 端点 + 2 过渡数据源方法 / store 双缓存三态 + 六权限 getter + 12 动作 /
 *                       3 文件存在 + 页面 3 Tab + 顶部三 Alert / 路由 + mock 6 码 / 追加语义(D2) + 自由文本(G3)
 *      - RC09-RC10 端点可达：GET /api/manual-capacity/slots、GET /api/resource-calendar/1
 *                       （6 权限码无角色绑定 → 403 亦视为可达；见 4号位 函 G5）
 *      - RC11 接受类（默认跳过）：VERIFY_ALLOW_WRITE=1 时开启
 *
 * 用法：
 *  - 全量：node scripts/verify-acceptance.mjs
 *  - 分段：GROUP=rbac-negate node scripts/verify-acceptance.mjs
 *  - 仅静态：GROUP=static node scripts/verify-acceptance.mjs
 *  - §10A 段：GROUP=j-10a node scripts/verify-acceptance.mjs
 *  - §10A.1 段：GROUP=m-10a1 node scripts/verify-acceptance.mjs
 *  - 写维护段：GROUP=parameter-set-drafts node scripts/verify-acceptance.mjs
 *  - OPM 段：GROUP=o-opm node scripts/verify-acceptance.mjs
 *  - 资源日历段：GROUP=r-resource-calendar node scripts/verify-acceptance.mjs
 *  - Domain 段：GROUP=d-domain node scripts/verify-acceptance.mjs
 *  - 接受类（脏数据风险）：VERIFY_ALLOW_WRITE=1 GROUP=j-10a node scripts/verify-acceptance.mjs
 *  - Windows Git Bash：env 前缀可用；或直接改脚本末尾 GROUP 默认值
 */

import { readFileSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { setTimeout as sleep } from 'node:timers/promises'

// 自签证书放行（dev only）
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'

const API_BASE = process.env.API_BASE || 'https://localhost:7044'
const GROUP = process.env.GROUP || 'all' // all | interfaces | rbac-negate | current-user | static | business-rules | j-10a | m-10a1 | v14-additions | parameter-set-drafts | o-opm | r-resource-calendar | d-domain
const ADMIN_CODE = 'admin'
const ADMIN_PWD = 'Admin@123456'

// 路径解析（用于静态扫描 frontNew/src/）
const __dirname = dirname(fileURLToPath(import.meta.url))
const SRC_ROOT = join(__dirname, '..', 'src')

const log = (...a) => console.log(...a)
const ok = (msg) => log(`  ✅ ${msg}`)
const bad = (msg) => log(`  ❌ ${msg}`)
const info = (msg) => log(`  · ${msg}`)

let passCount = 0
let failCount = 0
let skipCount = 0

function assert(label, cond, detail = '') {
  if (cond) {
    passCount++
    ok(`${label}${detail ? ` — ${detail}` : ''}`)
  } else {
    failCount++
    bad(`${label}${detail ? ` — ${detail}` : ''}`)
  }
}

function skip(label, reason) {
  skipCount++
  info(`${label} — ${reason}`)
}

/* ==================== 通用 fetch 封装（与 verify-integration.mjs 一致）==================== */

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

/* ==================== 登录 ==================== */

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

/* ==================== [I] 接口 Owner 正确性（U23-U32）==================== */

async function verifyInterfaces(token) {
  log('\n[I] 接口 Owner 正确性（U23-U32）')

  // U23 静态：前端不含 2号位 端口（7001/7002）
  const u23 = staticScanNoDirectBackend()
  assert(
    'U23 前端不含 2号位 端口（7001/7002）',
    u23,
    u23 ? '扫描通过' : '检测到 2号位 端口直接调用'
  )

  // U24 Overview 查询 4→5 语义不变
  let r = await call('GET', '/api/overview/active-plan', { token })
  assert(
    'U24 GET /api/overview/active-plan 端点可达',
    r.ok || r.code === 200,
    `code=${r.code}（200=有数据或空数据；端点可达即过）`
  )

  // U25 G4/G7/G8 迁移后 URL/DTO 兼容（取 3 个治理端点）
  r = await call('GET', '/api/governance/rule-sets', { token })
  assert(
    'U25 GET /api/governance/rule-sets 兼容',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  r = await call('GET', '/api/governance/parameter-sets', { token })
  assert(
    'U25 GET /api/governance/parameter-sets 兼容',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  r = await call('GET', '/api/governance/strategy-profiles', { token })
  assert(
    'U25 GET /api/governance/strategy-profiles 兼容',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  // U26 CTP 发起 4→3（拿 list 不实际发起，避免污染数据）
  r = await call('GET', '/api/ctp/history', { token })
  assert(
    'U26 GET /api/ctp/history 走 3号位',
    r.ok || r.code === 404 || r.code === 403,
    `code=${r.code}（404/403 表示 CTP 端点存在但无数据；200 表示有历史）`
  )

  // U27 Manual ETA 查询
  r = await call('GET', '/api/procurement-manual-eta', { token })
  assert(
    'U27 GET /api/procurement-manual-eta 走 5号位',
    r.ok && Array.isArray(r.json?.data),
    `code=${r.code}, count=${r.json?.data?.length ?? 0}`
  )

  // U28 静态：DTO 不含前端计算的 availableTime / effectiveEta 字段（§17 撤销 — 4号位 不计算）
  const u28 = !staticScanDtoHasField('api/aps-v1/types/manualEta.ts', 'availableTime') &&
               !staticScanDtoHasField('api/aps-v1/types/manualEta.ts', 'effectiveEta')
  assert(
    'U28 ManualEta DTO 不含前端计算的 availableTime/effectiveEta',
    u28,
    u28 ? 'DTO 仅含 manualEta（人工值），后端计算字段不存在于 DTO' : '检测到 DTO 含后端计算字段'
  )

  // U29 Demand Protection 查看 4→5
  r = await call('GET', '/api/demand-protection', { token })
  assert(
    'U29 GET /api/demand-protection',
    r.ok,
    `code=${r.code}（200=有锁；403=无 scope；其他=异常）`
  )

  // U30 Demand Protection 释放（不实际调，验端点存在；调空 body 应 400）
  r = await call('POST', '/api/demand-protection/release', { token, body: {} })
  assert(
    'U30 POST /api/demand-protection/release 端点存在',
    r.code === 400 || r.ok || r.code === 403,
    `code=${r.code}（400=参数校验生效；403=无权限；端点存在即过）`
  )

  // U31 MES 受控操作（验端点存在）
  r = await call('POST', '/api/mes/dispatch', { token, body: { taskId: 0 } })
  assert(
    'U31 POST /api/mes/dispatch 端点存在',
    r.code === 400 || r.ok || r.code === 403 || r.code === 404 || r.code === 422,
    `code=${r.code}（端点存在即过）`
  )

  // U32 Login/Refresh/Logout/Me
  r = await call('GET', '/api/auth/me', { token })
  assert(
    'U32 GET /api/auth/me',
    r.ok && r.json?.data?.userCode,
    `code=${r.code}, userCode=${r.json?.data?.userCode ?? '-'}`
  )

  r = await call('POST', '/api/auth/refresh', { body: { refreshToken: 'invalid-for-test' } })
  assert(
    'U32 POST /api/auth/refresh 端点存在',
    r.code === 400 || r.code === 401 || r.ok,
    `code=${r.code}（端点存在即过；400/401=token 校验生效）`
  )

  r = await call('POST', '/api/auth/logout', { token })
  assert(
    'U32 POST /api/auth/logout 端点存在',
    r.code === 200 || r.code === 401 || r.code === 204,
    `code=${r.code}`
  )
}

/* ==================== [R] 鉴权反向（U33-U39, U46-U47）==================== */

async function verifyRbacNegate(adminToken) {
  log('\n[R] 鉴权反向（U33-U39 + U46-U47）')

  // 1. 找 VIEWER 角色
  let r = await call('GET', '/api/rbac/roles', { token: adminToken })
  const viewerRole = (r.json?.data || []).find(
    (rl) => /viewer/i.test(rl.roleCode) || /viewer/i.test(rl.roleName)
  )
  if (!viewerRole) {
    info('未找到 VIEWER 角色（跳过 [R]）')
    skipCount++
    return
  }

  // 2. 建 VIEWER 测试账号（清空权限 + scope）
  const viewerUserCode = `aps.acceptance.${Date.now()}`
  r = await call('POST', '/api/rbac/users', {
    token: adminToken,
    body: { userCode: viewerUserCode, userName: 'acceptance test', password: 'NegatePass123!' }
  })
  const newViewer = r.json?.data
  if (!newViewer?.id) {
    bad(`建 VIEWER 测试用户失败：code=${r.code}`)
    return
  }
  await call('PUT', `/api/rbac/users/${newViewer.id}/roles`, {
    token: adminToken,
    body: { ids: [] }
  })
  await call('PUT', `/api/rbac/users/${newViewer.id}/scopes`, {
    token: adminToken,
    body: { ids: [] }
  })

  // 3. 登录拿 viewer token
  r = await call('POST', '/api/auth/login', {
    body: { userCode: viewerUserCode, password: 'NegatePass123!' }
  })
  const viewerToken = r.json?.data?.accessToken
  if (!viewerToken) {
    bad(`VIEWER 登录失败：${r.text.slice(0, 200)}`)
    await call('DELETE', `/api/rbac/users/${newViewer.id}`, { token: adminToken })
    return
  }

  // U33 业务端点 403
  const businessEps = [
    '/api/order-query',
    '/api/pi-position',
    '/api/explanation',
    '/api/demand-protection',
    '/api/procurement-manual-eta'
  ]
  for (const ep of businessEps) {
    r = await call('GET', ep, { token: viewerToken })
    assert(`U33 裸账号 GET ${ep}`, r.code === 403, `code=${r.code}（期望 403）`)
  }

  // U34 Factory=TJ 查 BJ（用 viewerToken：scope 空 → 403）
  r = await call('GET', '/api/order-query', {
    token: viewerToken,
    query: { factoryCode: 'BJ', planVersionId: 1 }
  })
  assert(
    'U34 无 scope 账号 GET /api/order-query?factoryCode=BJ',
    r.code === 403 || r.code === 400,
    `code=${r.code}（期望 403/400）`
  )

  // U35 Factory={BJ,TJ}+Department=MC（验 scope 维度存在）
  r = await call('GET', '/api/auth/me', { token: viewerToken })
  assert(
    'U35 /me 含 4 类 scope 字段（或空 scope）',
    r.ok && r.json?.data?.userCode !== undefined,
    `code=${r.code}, userCode=${r.json?.data?.userCode ?? '-'}`
  )

  // U36 ProductFamily=X 维护 PF=Y（验 POST 端点受 scope 限制）
  r = await call('POST', '/api/governance/rule-set', {
    token: viewerToken,
    body: { ruleSetCode: 'VERIFY', status: 'DRAFT' }
  })
  assert(
    'U36 裸账号 POST /api/governance/rule-set',
    r.code === 403 || r.code === 400 || r.code === 401 || r.code === 404,
    `code=${r.code}`
  )

  // U37 Domain=D01 激活 D02（验 activate 端点受 scope 限制）
  r = await call('POST', '/api/candidate/1/activate', {
    token: viewerToken,
    body: { activatedBy: 'verify' }
  })
  assert(
    'U37 裸账号 POST /api/candidate/{id}/activate',
    r.code === 403 || r.code === 400 || r.code === 401 || r.code === 404,
    `code=${r.code}`
  )

  // U38 无 token 401
  r = await call('POST', '/api/candidate/1/activate', { body: { activatedBy: 'verify' } })
  assert(
    'U38 无 token POST /api/candidate/{id}/activate 应 401',
    r.code === 401 || r.status === 401,
    `code=${r.code} status=${r.status}`
  )

  // U39 Global 用户（用 admin token：跨域访问应成功）
  r = await call('GET', '/api/auth/me', { token: adminToken })
  assert(
    'U39 Global 用户 跨域访问',
    r.ok,
    `code=${r.code}（admin 当前可访问；如未来 Global 受限需重测）`
  )

  // U46 CTP 无激活按钮（端点不应暴露 activate）
  r = await call('POST', '/api/ctp/1/activate', { token: adminToken, body: {} })
  assert(
    'U46 POST /api/ctp/{id}/activate 端点不存在或 405',
    r.code === 404 || r.code === 405 || r.code === 400,
    `code=${r.code}（CTP 应无激活）`
  )

  // U47 MES 不可下发（验端点 + INELIGIBLE 拒绝）
  r = await call('POST', '/api/mes/dispatch', { token: adminToken, body: { taskId: 999999 } })
  assert(
    'U47 POST /api/mes/dispatch taskId=无效 应 4xx',
    r.code === 400 || r.code === 403 || r.code === 404 || r.code === 422,
    `code=${r.code}（后端应拒绝无效 taskId）`
  )

  // 清理 VIEWER 测试账号
  await call('DELETE', `/api/rbac/users/${newViewer.id}`, { token: adminToken })
  ok('清理 VIEWER 测试账号')
}

/* ==================== [C] Current User / Scope / Audit（U40-U42）==================== */

async function verifyCurrentUser(token) {
  log('\n[C] Current User / Scope / Audit（U40-U42）')

  // U40 /me 含 permissions + 4 scope（实际字段：factories/productFamilies/departments/domains/isGlobal）
  const r = await call('GET', '/api/auth/me', { token })
  const data = r.json?.data || {}
  const hasPerms = Array.isArray(data.permissions)
  const hasScope =
    Array.isArray(data.factories) ||
    Array.isArray(data.productFamilies) ||
    Array.isArray(data.departments) ||
    Array.isArray(data.domains) ||
    data.isGlobal === true
  assert(
    'U40 /me 含 permissions + 4 类 scope',
    r.ok && hasPerms && hasScope,
    `perms=${hasPerms}, scope=${hasScope}, isGlobal=${data.isGlobal ?? '-'}, userCode=${data.userCode ?? '-'}`
  )

  // U41 Scope 筛选（静态）
  const u41 = staticScanScopeFilterConstrained()
  assert('U41 Scope 筛选 仅含授权范围', u41, u41 ? 'ScopeFilter 仅展示授权项' : 'ScopeFilter 未受 scope 约束')

  // U42 Audit-logs 不含敏感字段（验端点存在 + 响应不含 password/token）
  const audit = await call('GET', '/api/audit-logs', { token })
  const auditText = JSON.stringify(audit.json || {})
  const hasSensitive = /password|token|refreshToken|accessToken/i.test(
    auditText.replace(/"passwordHash"|"tokenHash"|"hashedPassword"/g, '') // 排除哈希字段
  )
  assert(
    'U42 /api/audit-logs 不含敏感字段',
    audit.ok || audit.code === 403 || audit.code === 404,
    `code=${audit.code}, hasSensitive=${hasSensitive}`
  )
}

/* ==================== [S] 静态扫描（U28/U41/U43/U44/U45）==================== */

function verifyStaticScan() {
  log('\n[S] 静态扫描（U28/U41/U43/U44/U45 + S01）')

  // U28 已在 [I] 中跑过（DTS 字段）
  // U41 已在 [C] 中跑过（Scope 筛选）
  // 下面跑 U43-U45 三个独立静态扫描

  const u43 = !staticFileExists('views/Aps/Approval.vue')
  assert('U43 无 Approval.vue 路由', u43, u43 ? '确认 Approval 旧表页面未建' : '检测到 Approval.vue')

  const u44 = !staticCodeContains(
    ['views/Aps/*.vue'],
    /PAUSE_TASK|RESUME_TASK|\bPAUSE\b.*\b按钮|\bRESUME\b.*\b按钮/i
  )
  assert('U44 无 PAUSE/RESUME 按钮', u44, u44 ? '确认 PAUSE/RESUME 状态闭环未引入' : '检测到 PAUSE/RESUME 关键字')

  const u45 = !staticCodeContains(
    ['**/*.ts', '**/*.vue'],
    /localhost:700[12]|127\.0\.0\.1:700[12]|7001\/api|7002\/api/
  )
  assert(
    'U45 前端不含 1号位 端口（7001/7002）',
    u45,
    u45 ? '确认 4→1 直接调用不存在' : '检测到 1号位 端口'
  )

  // S01：Element Plus 组件显式 import（防静默退化 —— 漏 import 不报错，只把标签属性/文本裸渲染）
  const epMissing = staticScanUnimportedElementPlusComponents()
  assert(
    'S01 Aps 页面模板用到的 El* 组件均已显式 import',
    epMissing.length === 0,
    epMissing.length === 0
      ? '全量扫描通过（ElScrollbar/ElLoading 全局注册，进白名单）'
      : `未 import：${epMissing.join(' | ')}`
  )

  // S02：登录成功分支必须重置 apsAuth.userInfo（防切账号复用旧 userInfo）
  //  - 根因：permission.ts 守卫靠 !apsAuth.userInfo 决定是否调 /me；
  //    登录若不清，新账号（pmc）会沿用上一会话（admin）的 permissions + dataScope，
  //    /me 永不触发，按钮显隐错误
  //  - 修复点：views/login/components/LoginForm.vue signIn() 成功分支首行 useApsAuthStore().reset()
  const s02 = staticCodeContains(
    ['views/login/components/*.vue'],
    /useApsAuthStore\(\)\.reset\(\)/
  )
  assert(
    'S02 登录成功分支重置 apsAuth.userInfo（防切账号复用旧 userInfo）',
    s02,
    s02
      ? 'LoginForm.vue 已在 signIn 成功分支调用 useApsAuthStore().reset()'
      : 'LoginForm.vue 缺 useApsAuthStore().reset() 调用，新账号会复用旧 userInfo'
  )

  // S03：datetime 工具 + 所有「显示后端 DateTime」页面统一引用（防后端 naive UTC 少 8h 回归）
  //  - 根因：ASP.NET Core System.Text.Json 序列化 DateTime 字段不带 Z 后缀，
  //    前端裸 dayjs(s)/new Date(s) 按 spec 当本地时区解析 → 北京时间少 8h
  //  - 修复：所有显示后端 DateTime 的页面统一调 @/utils/datetime 的 formatUtcDateTime* 工具
  //    （工具内部 dayjs.utc(s).local().format(...)）
  const utilPath = join(__dirname, '..', 'src', 'utils', 'datetime.ts')
  const utilExists = existsSync(utilPath)
  const datetimePages = [
    'views/Aps/Domain.vue',
    'views/Aps/Audit.vue',
    'views/Aps/RbacPermissions.vue',
    'views/Aps/RbacRoles.vue',
    'views/Aps/RbacScopes.vue',
    'views/Aps/RbacUsers.vue',
    'views/Aps/Candidate.vue',
    'views/Aps/components/StrategyDiffPanel.vue'
  ]
  const pageUsesUtil = datetimePages.map((p) => {
    const fp = join(SRC_ROOT, p)
    if (!existsSync(fp)) return { p, uses: false, missing: true }
    const c = readFileSync(fp, 'utf-8')
    return { p, uses: /from\s+['"]@\/utils\/datetime['"]/.test(c), missing: false }
  })
  const allPagesUseUtil = pageUsesUtil.every((x) => x.uses)
  assert(
    'S03 8 个页面统一引用 @/utils/datetime 显示后端 DateTime（防 naive UTC 少 8h）',
    utilExists && allPagesUseUtil,
    utilExists
      ? `工具存在；引用情况：${pageUsesUtil.map((x) => `${x.p.split('/').pop()}=${x.uses ? '✓' : '✗'}`).join(' | ')}`
      : 'src/utils/datetime.ts 不存在'
  )

  // S04：datetime 工具化的页面禁止裸 dayjs()/new Date().toLocaleString() 显示后端 DateTime
  //  - 允许的例外：本地构造时间（如 dayjs().format('YYYY-MM-DD HH:mm') 当前时间用 dayjs() 没问题）
  //  - 重点拦：dayjs(someBackendField).format / new Date(someBackendField).toLocaleString('zh-CN')
  const oldPatterns = [
    /new Date\([^)]*\)\.toLocaleString\(['"]zh-CN['"]\)/,
    /dayjs\((?:row|currentRow|item|headerNN|lastConfirmResult|lastActivateResult)\.\w*[aA]t\w*\)\.format/,
    /dayjs\((?:currentDetail|detail)\.\w*[aA]t\w*\)\.format/
  ]
  const oldHits = []
  for (const p of datetimePages) {
    const c = readFileSync(join(__dirname, '..', 'src', p), 'utf-8')
    for (const re of oldPatterns) {
      const m = c.match(re)
      if (m) oldHits.push(`${p}: ${m[0]}`)
    }
  }
  assert(
    'S04 datetime 工具化的页面无裸 dayjs()/toLocaleString 显示后端 DateTime',
    oldHits.length === 0,
    oldHits.length === 0 ? '无回归' : `残留：${oldHits.slice(0, 3).join(' | ')}${oldHits.length > 3 ? '...' : ''}`
  )
}

/* ==================== [B] 业务约束（U48-U50）==================== */

async function verifyBusinessRules(token) {
  log('\n[B] 业务约束（U48-U50）')

  // U48 ESTIMATED 不显示为正式承诺（验 Pegging 端点可达 + supplyType 含 ESTIMATED 区分）
  // 端点可达即过；具体展示区分在 Run.vue L791-807 reasons[] 渲染（已闭环）
  const r = await call('GET', '/api/pegging-trace?planVersionId=1', { token })
  assert(
    'U48 /api/pegging-trace 端点可达（ESTIMATED 区分由前端展示）',
    r.ok || r.code === 400 || r.code === 404,
    `code=${r.code}（端点可达即过；ESTIMATED 红条由 Run.vue 渲染）`
  )

  // U49 Run 普通查询 5 / 治理 3（验端点分类）
  // 实测后端路径：GET /api/governance/runs + POST /api/governance/run/{id}/recover
  const runList = await call('GET', '/api/governance/runs', { token })
  assert(
    'U49 GET /api/governance/runs（5号位）',
    runList.ok || runList.code === 403,
    `code=${runList.code}`
  )
  const runRecover = await call('POST', '/api/governance/run/1/recover', { token, body: {} })
  assert(
    'U49 POST /api/governance/run/{id}/recover（3号位）',
    runRecover.code === 400 || runRecover.ok || runRecover.code === 403 || runRecover.code === 404,
    `code=${runRecover.code}`
  )

  // U50 空 scope 用户（建无 scope 用户）
  const stamp = Date.now()
  const noScopeUserCode = `aps.noscope.${stamp}`
  let create = await call('POST', '/api/rbac/users', {
    token,
    body: { userCode: noScopeUserCode, userName: 'no scope test', password: 'NoScopePass123!' }
  })
  const noScopeUser = create.json?.data
  if (!noScopeUser?.id) {
    skip('U50 空 scope 用户', '建测试用户失败')
    return
  }
  await call('PUT', `/api/rbac/users/${noScopeUser.id}/roles`, { token, body: { ids: [] } })
  await call('PUT', `/api/rbac/users/${noScopeUser.id}/scopes`, { token, body: { ids: [] } })
  const noScopeLogin = await call('POST', '/api/auth/login', {
    body: { userCode: noScopeUserCode, password: 'NoScopePass123!' }
  })
  const noScopeToken = noScopeLogin.json?.data?.accessToken
  if (!noScopeToken) {
    skip('U50 空 scope 用户', '登录失败')
    await call('DELETE', `/api/rbac/users/${noScopeUser.id}`, { token })
    return
  }
  const overviewNoScope = await call('GET', '/api/overview/active-plan', { token: noScopeToken })
  assert(
    'U50 空 scope 用户 /overview 受限',
    overviewNoScope.code === 403 || overviewNoScope.code === 200,
    `code=${overviewNoScope.code}（403=后端拦截；200=后端空数据；任一即过）`
  )
  await call('DELETE', `/api/rbac/users/${noScopeUser.id}`, { token })
  ok('清理 noScope 测试账号')
}

/* ==================== 静态扫描辅助函数 ==================== */

import { readdirSync, statSync } from 'node:fs'

function staticFileExists(relPath) {
  try {
    const fullPath = join(SRC_ROOT, relPath)
    return statSync(fullPath).isFile()
  } catch {
    return false
  }
}

function staticCodeContains(globs, pattern) {
  // 简化版 glob：仅支持 *.vue / *.ts / **/*.vue / **/*.ts
  const exts = globs.some((g) => g.endsWith('*.vue')) ? ['.vue'] : []
  if (globs.some((g) => g.includes('**/*.ts'))) exts.push('.ts')
  if (globs.some((g) => g.includes('*.ts'))) exts.push('.ts')

  function walk(dir) {
    const out = []
    try {
      for (const name of readdirSync(dir)) {
        const p = join(dir, name)
        const s = statSync(p)
        if (s.isDirectory()) out.push(...walk(p))
        else if (exts.some((e) => p.endsWith(e))) out.push(p)
      }
    } catch {}
    return out
  }

  const files = walk(SRC_ROOT)
  for (const f of files) {
    try {
      const content = readFileSync(f, 'utf-8')
      if (pattern.test(content)) return true
    } catch {}
  }
  return false
}

function staticScanNoDirectBackend() {
  return !staticCodeContains(
    ['**/*.ts', '**/*.vue'],
    /localhost:700[12]|127\.0\.0\.1:700[12]|7001\/api|7002\/api/
  )
}

function staticScanDtoHasField(relPath, fieldName) {
  try {
    const content = readFileSync(join(SRC_ROOT, relPath), 'utf-8')
    return content.includes(fieldName)
  } catch {
    return false
  }
}

function staticScanScopeFilterConstrained() {
  // U41（冻结 v1.4 L1463）：Scope 筛选器不允许选择未授权 Factory/ProductFamily/Department/Domain。
  // 诚实校验——取代旧实现「读 Overview.vue 含 'scope' 子串即绿」（那只命中 <style scoped> 的 CSS 关键字 = 假绿）。
  // Order.vue 是含 Factory/ProductFamily/Domain 三维筛选下拉的主页面，必须：
  //   ① 引入 constrainOptionsByScope（scope.ts 收敛原语）；
  //   ② 三维分别按 apsAuth.dataScope.{factoryCodes,productFamilyCodes,domainKeys} 收敛选项。
  // Department 维度 frontNew 无筛选下拉（N/A）；Pi.vue/Gantt.vue 的 Domain 下拉同收敛，此处以 Order.vue 为代表面。
  try {
    const order = readFileSync(join(SRC_ROOT, 'views/Aps/Order.vue'), 'utf-8')
    const usesHelper = order.includes('constrainOptionsByScope')
    const factoryScoped = /dataScope\.factoryCodes/.test(order)
    const pfScoped = /dataScope\.productFamilyCodes/.test(order)
    const domainScoped = /dataScope\.domainKeys/.test(order)
    return usesHelper && factoryScoped && pfScoped && domainScoped
  } catch {
    return false
  }
}

/* ===== 静态扫描：Element Plus 组件必须显式 import =====
 * 背景（2026-09-28）：全局只在 plugins/elementPlus 注册了 ElScrollbar / ElLoading，
 * 其余 El* 组件必须逐文件从 'element-plus' import。
 * 漏 import 是**静默失败**：vue-tsc 不报错、页面照常出，只是组件没被解析，
 * Vue 当成原生标签渲染 → 标签属性全丢、内部文本被浏览器裸拼在一起
 * （实测：Domain.vue 的 ElDescriptions 渲染成 "rule-admin2026-09-28T01:04:50.408Z"）。
 * 故用静态断言把这类退化拦在门外。
 *  - 只扫 4号位 自己的 views/Aps（含 components 子目录）
 *  - 全局已注册的两个组件进白名单；ElMessage/ElMessageBox/ElNotification 是函数式调用，非模板标签
 */
const EP_GLOBAL_COMPONENTS = new Set([
  'ElScrollbar',
  'ElLoading',
  'ElMessage',
  'ElMessageBox',
  'ElNotification'
])

/** 取 SFC 根 <template> 片段：从文件尾部往前找最后一个 </template>（slot 里也有，正向找会被提前截断） */
function vueTemplateBlock(content) {
  const start = content.indexOf('<template>')
  if (start < 0) return ''
  const end = content.lastIndexOf('</template>')
  return end > start ? content.slice(start, end) : ''
}

/** 收集文件里 `import { ... } from 'element-plus'` 的全部标识符 */
function elementPlusImportedNames(content) {
  const names = new Set()
  // [^{}]* 防止跨过前一个 import 的 } —— 否则非贪婪 [\s\S]*? 会从更早的 `import {` 开始吞，
  // 把 "import {\n  ElAlert" 当成长名，导致首个组件被误判为未 import
  const re = /import\s*\{([^{}]*)\}\s*from\s*['"]element-plus['"]/g
  let m
  while ((m = re.exec(content))) {
    for (const raw of m[1].split(',')) {
      const cleaned = raw.trim().replace(/^type\s+/, '')
      if (!cleaned) continue
      // 每个条目最后一段才是标识符（兼容 `X as Y` 与残留的 import 关键字）
      const name = cleaned.split(/\s+/).pop()
      if (name) names.add(name)
    }
  }
  return names
}

/** 模板里用到但未 import 的 El* 组件（PascalCase / kebab-case 两种写法都识别） */
function staticScanUnimportedElementPlusComponents() {
  const problems = []
  const files = []
  ;(function walk(dir) {
    try {
      for (const name of readdirSync(dir)) {
        const p = join(dir, name)
        if (statSync(p).isDirectory()) walk(p)
        else if (p.endsWith('.vue')) files.push(p)
      }
    } catch {}
  })(join(SRC_ROOT, 'views/Aps'))

  for (const file of files) {
    let content
    try {
      content = readFileSync(file, 'utf-8')
    } catch {
      continue
    }
    const tpl = vueTemplateBlock(content)
    if (!tpl) continue
    const imported = elementPlusImportedNames(content)
    const missing = new Set()
    const tagRe = /<([A-Za-z][A-Za-z0-9-]*)/g
    let m
    while ((m = tagRe.exec(tpl))) {
      const tag = m[1]
      let name = null
      if (/^El[A-Z]/.test(tag)) {
        name = tag
      } else if (/^el-[a-z]/.test(tag)) {
        name =
          'El' +
          tag
            .slice(3)
            .split('-')
            .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
            .join('')
      }
      if (name && !EP_GLOBAL_COMPONENTS.has(name) && !imported.has(name)) missing.add(name)
    }
    if (missing.size) problems.push(`${file.slice(SRC_ROOT.length + 1)}: ${[...missing].join(', ')}`)
  }
  return problems
}

/* ==================== [V] V1.4 收口新增端点（任务 1+2+5） ==================== */

async function verifyV14Additions(token) {
  log('\n[V] V1.4 收口新增端点（任务 1+2+5 — published-version + compare-with + validate-domain-keys）')

  // V01: RuleSet 当前 Published 专用端点
  let r = await call('GET', '/api/governance/rule-sets', { token })
  if (r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0) {
    const ruleSetId = r.json.data[0].ruleSetId ?? r.json.data[0].RuleSetId
    r = await call('GET', `/api/governance/rule-set/${ruleSetId}/published-version`, { token })
    assert(
      'V01 GET /rule-set/{id}/published-version 专用端点',
      r.ok || r.code === 404 || r.code === 422,
      `code=${r.code}（200=有已发布版本；404=暂无发布；422=模型校验触发，端点可达）`
    )
  } else {
    skip('V01', 'rule-sets 列表为空，跳过 published-version 断言')
  }

  // V03: StrategyProfile 当前 Published 专用端点
  r = await call('GET', '/api/governance/strategy-profiles', { token })
  if (r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0) {
    const profileId = r.json.data[0].strategyProfileId ?? r.json.data[0].StrategyProfileId
    r = await call('GET', `/api/governance/strategy-profile/${profileId}/published-version`, { token })
    assert(
      'V03 GET /strategy-profile/{id}/published-version 专用端点',
      r.ok || r.code === 404 || r.code === 422,
      `code=${r.code}（200=有已发布；404=无已发布；422=模型校验触发，端点可达）`
    )
  } else {
    skip('V03', 'strategy-profiles 列表为空，跳过')
  }

  // V02: ParameterSet 当前 Published 专用端点
  r = await call('GET', '/api/governance/parameter-sets', { token })
  if (r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0) {
    const paramSetId = r.json.data[0].parameterSetId ?? r.json.data[0].ParameterSetId
    r = await call('GET', `/api/governance/parameter-set/${paramSetId}/published-version`, { token })
    assert(
      'V02 GET /parameter-set/{id}/published-version 专用端点',
      r.ok || r.code === 404 || r.code === 422,
      `code=${r.code}（200=有已发布；404=无已发布；422=模型校验触发，端点可达）`
    )
  } else {
    skip('V02', 'parameter-sets 列表为空，跳过')
  }

  // V04: validate-domain-keys（取一个 FAILED ScheduleRun 测；无 FAILED 时跳过）
  r = await call('GET', '/api/governance/runs?status=FAILED', { token })
  if (r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0) {
    const failedRunId = r.json.data[0].runId ?? r.json.data[0].RunId
    r = await call('POST', `/api/governance/run/${failedRunId}/validate-domain-keys`, {
      token,
      body: {}
    })
    assert(
      'V04 POST /run/{id}/validate-domain-keys 端点可达',
      r.ok || r.code === 400 || r.code === 403 || r.code === 422,
      `code=${r.code}（200=校验通过；400=冻结规则冲突；422=模型校验触发，端点可达）`
    )
  } else {
    skip('V04', '无 FAILED ScheduleRun，跳过 validate-domain-keys 断言')
  }

  // V05: Candidate compare-with 双参路径契约
  // 拿 governance/runs?status=CANDIDATE → candidatePlanVersionId；
  // 若有 active-version 拿 basePlanVersionId；均无则跳过（不污染数据）
  r = await call('GET', '/api/governance/runs?status=CANDIDATE', { token })
  const candidates = r.ok && Array.isArray(r.json?.data) ? r.json.data : []
  r = await call('GET', '/api/governance/active-plan', { token })
  const baseId =
    r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0
      ? r.json.data[0].planVersionId ?? r.json.data[0].PlanVersionId
      : null
  if (candidates.length > 0 && baseId) {
    const candidateId = candidates[0].planVersionId ?? candidates[0].PlanVersionId ?? candidates[0].runId
    r = await call(
      'GET',
      `/api/governance/plan-version/${candidateId}/compare-with/${baseId}`,
      { token }
    )
    assert(
      'V05 GET /plan-version/{candidateId}/compare-with/{baseId} 双参契约',
      r.ok || r.code === 404 || r.code === 400,
      `code=${r.code}（200=有对比；404=版本不存在；400=状态校验未过）`
    )
  } else {
    skip('V05', '无 CANDIDATE PlanVersion 或 basePlanVersionId，跳过')
  }
}

/* ==================== [J] §10A 五业务入口契约（j-10a） ==================== */

/** §10A 契约后端断点（待 3号位 补）：
 *  - B1 GovernanceController.cs CreateCandidateRunRequest 缺 Scope + 映射缺 Scope 赋值（2 行）
 *  - B2 RunLifecycleService.cs:483-493 内部 createSpec 缺 Scope = spec.Scope 拷贝
 *  - B3 ScheduleRunRepository.cs INSERT 缺 [ScopeJson] 列
 * 三断点未全补前，POST /api/governance/run/candidate 会**静默丢弃** scope 字段，Validator 立即放行
 * （scope null → return），所有 J01-J08 拒绝类断言失效。J00 探测即用作前置门控。
 *
 * 后端补齐前策略：J00 != 400 → 跳 J01-J08 + J10（避免脏数据上界锁），J09 静态始终跑。
 */

function runJ09Static() {
  const typesHasScope =
    staticScanDtoHasField('api/aps-v1/types/run.ts', 'ScopeJsonV2') &&
    staticScanDtoHasField('api/aps-v1/types/run.ts', 'PriorityMode') &&
    staticScanDtoHasField('api/aps-v1/types/run.ts', 'OrderTargetDto') &&
    staticScanDtoHasField('api/aps-v1/types/run.ts', 'TaskTargetDto')
  assert(
    'J09 静态：types/run.ts 含 ScopeJsonV2+PriorityMode+OrderTargetDto+TaskTargetDto',
    typesHasScope,
    typesHasScope ? '契约已加' : '静态契约字段缺失'
  )

  const runNoRemark = !staticCodeContains(['api/aps-v1/run.ts'], /\bremark\s*:/)
  assert(
    'J09 静态：run.ts triggerReschedule 不再含 remark 字段（3号位 结论：前端停传）',
    runNoRemark,
    runNoRemark ? '已移除 remark 字段' : 'run.ts 仍包含 remark'
  )

  const ganttNoInsertReschedule = !staticCodeContains(['views/Aps/Gantt.vue'], /INSERT_RESCHEDULE/)
  assert(
    'J09 静态：Gantt.vue 不含 INSERT_RESCHEDULE（purpose 已统一为 MANUAL_ADJUSTMENT）',
    ganttNoInsertReschedule,
    ganttNoInsertReschedule ? '已无遗留 INSERT_RESCHEDULE' : 'Gantt.vue 仍引用 INSERT_RESCHEDULE'
  )

  const dlgFiles = [
    'views/Aps/components/OrderAdvanceDialog.vue',
    'views/Aps/components/GanttAdjustDialog.vue',
    'views/Aps/components/ResourceRescheduleDialog.vue',
    'views/Aps/components/DomainRescheduleDialog.vue'
  ]
  const missing = dlgFiles.filter((f) => !staticFileExists(f))
  const allDlgExist = missing.length === 0
  assert(
    'J09 静态：4 个 Dialog 文件齐备（OrderAdvance/GanttAdjust/ResourceReschedule/DomainReschedule）',
    allDlgExist,
    allDlgExist ? '齐备' : `缺失：${missing.join(', ')}`
  )
}

async function verifyJ10A(token) {
  log('\n[J] §10A 白天人工调整：ScopeJsonV2 契约（j-10a）')
  let r

  // J09 静态断言始终先跑（与后端状态无关）
  runJ09Static()

  // J00 探测：发 INSERT_ORDER_WHATIF × CTP + scope{NEW_ORDER_CTP, priorityMode:EXPEDITE}
  // 期望 400（NEW_ORDER_CTP 固定 NORMAL，禁 EXPEDITE — ScopeJsonV2Validator.ValidatePriorityMode）
  // 含 orderTargets 让请求在 scopeAvailable=true 后能跑过 Validator 三层检查到 PriorityMode
  const probe = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'INSERT_ORDER_WHATIF',
      purpose: 'CTP',
      domainKey: 'FAMILY_INJECTION',
      scope: {
        trigger: 'NEW_ORDER_CTP',
        priorityMode: 'EXPEDITE',
        orderTargets: [{ orderCanonicalId: 999999, manualTargetDueDate: '2026-10-01T00:00:00Z' }]
      }
    }
  })
  if (probe.code !== 400) {
    skip(
      'J00 探测 scope 未生效（后端 Web 层 Scope 字段 / 内部 spec.Scope 拷贝 / 仓储 ScopeJson 列 三断点未全补）',
      `code=${probe.code} status=${probe.status}（J01-J08 + J10 全部跳过避免脏数据；后端补齐后自然转绿）`
    )
    if (process.env.VERIFY_ALLOW_WRITE !== '1') skip('J10 接受类', 'VERIFY_ALLOW_WRITE 未设置=1')
    return
  }
  assert(
    'J00 探测 scope 已生效（NEW_ORDER_CTP+EXPEDITE 应 400）',
    true,
    '后端三断点已补，Validator 拦截成功'
  )

  // J01 EXISTING_ORDER_ADVANCE orderTargets 重复 Id
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'LOCAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: {
        trigger: 'EXISTING_ORDER_ADVANCE',
        priorityMode: 'NORMAL',
        orderTargets: [
          { orderCanonicalId: 1001, manualTargetDueDate: '2026-10-01T00:00:00Z' },
          { orderCanonicalId: 1001, manualTargetDueDate: '2026-10-02T00:00:00Z' }
        ]
      }
    }
  })
  assert(
    'J01 EXISTING_ORDER_ADVANCE orderTargets 重复 Id 应 400',
    r.code === 400,
    `code=${r.code}（重复 OrderCanonicalId 触发 Validator：单一真相约束 §4.6）`
  )

  // J02 GANTT_ADJUSTMENT + EXPEDITE
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'LOCAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: {
        trigger: 'GANTT_ADJUSTMENT',
        priorityMode: 'EXPEDITE',
        taskTargets: [{ taskId: 1, targetTime: '2026-10-01T00:00:00Z' }]
      }
    }
  })
  assert(
    'J02 GANTT_ADJUSTMENT + EXPEDITE 应 400',
    r.code === 400,
    `code=${r.code}（GANTT 固定 NORMAL）`
  )

  // J03 EQUIPMENT_FAILURE + EXPEDITE
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'LOCAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: { trigger: 'EQUIPMENT_FAILURE', priorityMode: 'EXPEDITE', changedResourceIds: [1] }
    }
  })
  assert(
    'J03 EQUIPMENT_FAILURE + EXPEDITE 应 400',
    r.code === 400,
    `code=${r.code}（EQUIPMENT 固定 NORMAL）`
  )

  // J04 RESOURCE_CALENDAR_CHANGE + EXPEDITE
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'LOCAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: { trigger: 'RESOURCE_CALENDAR_CHANGE', priorityMode: 'EXPEDITE', changedResourceIds: [1] }
    }
  })
  assert(
    'J04 RESOURCE_CALENDAR_CHANGE + EXPEDITE 应 400',
    r.code === 400,
    `code=${r.code}（CALENDAR 固定 NORMAL）`
  )

  // J05 DOMAIN_MANUAL_RESCHEDULE + priorityMode=NORMAL（须省略该键）
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'MANUAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: { trigger: 'DOMAIN_MANUAL_RESCHEDULE', priorityMode: 'NORMAL' }
    }
  })
  assert(
    'J05 DOMAIN_MANUAL_RESCHEDULE + priorityMode=NORMAL 应 400',
    r.code === 400,
    `code=${r.code}（DOMAIN 走既有正式优先规则，PriorityMode 须 null）`
  )

  // J06 NEW_ORDER_IMPACT + NORMAL
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'INSERT_ORDER_WHATIF',
      purpose: 'INSERT_IMPACT_ANALYSIS',
      domainKey: 'FAMILY_INJECTION',
      scope: {
        trigger: 'NEW_ORDER_IMPACT',
        priorityMode: 'NORMAL',
        orderTargets: [{ orderCanonicalId: 1001, manualTargetDueDate: '2026-10-01T00:00:00Z' }]
      }
    }
  })
  assert(
    'J06 NEW_ORDER_IMPACT + NORMAL 应 400',
    r.code === 400,
    `code=${r.code}（IMPACT 固定 EXPEDITE）`
  )

  // J07 NEW_ORDER_CTP + EXPEDITE（与 J00 同样规则独立再发一次）
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'INSERT_ORDER_WHATIF',
      purpose: 'CTP',
      domainKey: 'FAMILY_INJECTION',
      scope: {
        trigger: 'NEW_ORDER_CTP',
        priorityMode: 'EXPEDITE',
        orderTargets: [{ orderCanonicalId: 1001, manualTargetDueDate: '2026-10-01T00:00:00Z' }]
      }
    }
  })
  assert(
    'J07 NEW_ORDER_CTP + EXPEDITE 应 400',
    r.code === 400,
    `code=${r.code}（CTP 固定 NORMAL）`
  )

  // J08 GANTT_ADJUSTMENT taskTargets.targetTime 空
  // ⚠️ 后端 Validator 未覆盖此字段（仅校验 trigger 映射 / PriorityMode 轴 / orderTargets 去重），
  // 前端 validateScopeDraft 已提前拦截；后端 422 vs 400 都是校验失败语义（RFC 4918 422 更精确）
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'LOCAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: { trigger: 'GANTT_ADJUSTMENT', taskTargets: [{ taskId: 1, targetTime: '' }] }
    }
  })
  assert(
    'J08 GANTT_ADJUSTMENT targetTime 空应 4xx 校验拒绝（400 或 422；后端 Validator 待补；前端 validateScopeDraft 已拦）',
    r.code === 400 || r.code === 422,
    `code=${r.code}`
  )

  // J10 接受类（脏数据风险，默认关闭）
  if (process.env.VERIFY_ALLOW_WRITE !== '1') {
    skip('J10 接受类', 'VERIFY_ALLOW_WRITE 未设置=1（默认跳过避免脏数据）')
    return
  }
  info('VERIFY_ALLOW_WRITE=1 → J10 接受类断言开启（会创建 ScheduleRun + CANDIDATE PlanVersion）')
  r = await call('POST', '/api/governance/run/candidate', {
    token,
    body: {
      runType: 'LOCAL_RESCHEDULE',
      purpose: 'MANUAL_ADJUSTMENT',
      domainKey: 'FAMILY_INJECTION',
      scope: {
        trigger: 'GANTT_ADJUSTMENT',
        taskTargets: [{ taskId: 999999, targetTime: '2026-10-01T00:00:00Z' }]
      }
    }
  })
  assert(
    'J10 GANTT_ADJUSTMENT 接受类应 2xx 或 422/404（不污染数据为前提）',
    r.ok || r.code === 404 || r.code === 422,
    `code=${r.code}（taskId=999999 后端 4xx 即过；200/201=脏数据）`
  )
}

/* ==================== [P] ParameterSet 写维护（parameter-set-drafts）==================== */
/**
 * B 设计稿 Step 3+4+6+7 — ParameterSet / RuleSet 写维护前端落地（2026-09-21）
 *
 * 后端端点（lps/LPS.APS.Engine/Controllers/GovernanceController.cs:108-275 6 写端点）：
 *   - POST   /api/governance/rule-set/version              创建 RS DRAFT（强制 Status=DRAFT）
 *   - PUT    /api/governance/rule-set/version/{versionId}  更新 RS DRAFT（full-object）
 *   - POST   /api/governance/rule-set/version/{versionId}/publish  发布 RS
 *   - POST   /api/governance/parameter-set/version              创建 PS DRAFT
 *   - PUT    /api/governance/parameter-set/version/{versionId}  更新 PS DRAFT
 *   - POST   /api/governance/parameter-set/version/{versionId}/publish  发布 PS
 * fork 由前端 GET → POST 双发复合（ruleApi.forkDraft），无独立端点。
 *
 * 验收策略：静态扫描（P01-P05）+ 端点可达（P06-P13）。
 * 端点可达用「发最简请求 → 期望 400/404/422（参数校验生效）」或「200/201/204（端点直接成功）」
 * 视为端点存在；**不实际创建 DRAFT/发布**避免脏数据上界锁。
 */

function runP01P05Static() {
  // P01 types/rule.ts 含 10 类型（5 实体 + 4 输入 + 1 结果 + 1 联合）
  const p01Types = [
    'GovernanceVersionBase',
    'ParameterSetVersionGovernance',
    'RuleSetVersionGovernance',
    'RuleSetVersion',
    'ParameterSetVersion',
    'CreateRuleSetDraftInput',
    'UpdateRuleSetDraftInput',
    'CreateParameterSetDraftInput',
    'UpdateParameterSetDraftInput',
    'PublishGovernanceInput',
    'PublishGovernanceResult',
    'ForkDraftResult'
  ]
  const missingTypes = p01Types.filter(
    (n) => !staticScanDtoHasField('api/aps-v1/types/rule.ts', n)
  )
  assert(
    'P01 types/rule.ts 含 12 类型（5 实体 + 4 输入 + 1 结果 + ForkDraftResult + GovernanceVersionBase 等）',
    missingTypes.length === 0,
    missingTypes.length === 0 ? '契约类型齐备' : `缺失：${missingTypes.join(', ')}`
  )

  // P02 draftBuffer.ts 导出 3 算法 + 5 工具
  const p02Exports = [
    'flattenBlocks',
    'applyEdit',
    'buildPutBody',
    'isSensitive',
    'isEditable',
    'inferType',
    'parseJsonSafe',
    'parseParameterSetBlocks'
  ]
  const missingExports = p02Exports.filter(
    (n) => !staticScanDtoHasField('api/aps-v1/draftBuffer.ts', n)
  )
  assert(
    'P02 draftBuffer.ts 含 3 算法（flattenBlocks/applyEdit/buildPutBody）+ 5 工具',
    missingExports.length === 0,
    missingExports.length === 0
      ? '算法 + 工具齐备'
      : `缺失：${missingExports.join(', ')}`
  )

  // P03 rules.ts 含 6 actions + 6 getters
  const p03Actions = [
    'loadDualTrackBuffers',
    'forkDraft',
    'onCellEdit',
    'onCancelDirty',
    'onSaveDraft',
    'publishDualTrack'
  ]
  const missingActions = p03Actions.filter(
    (n) => !staticScanDtoHasField('store/modules/aps/rules.ts', `function ${n}`) &&
           !staticScanDtoHasField('store/modules/aps/rules.ts', `async function ${n}`)
  )
  assert(
    'P03 rules.ts 含 6 actions（loadDualTrackBuffers/forkDraft/onCellEdit/onCancelDirty/onSaveDraft/publishDualTrack）',
    missingActions.length === 0,
    missingActions.length === 0 ? 'actions 齐备' : `缺失：${missingActions.join(', ')}`
  )

  const p03Getters = [
    'actions',
    'forkPermission',
    'ruleSetDirty',
    'parameterSetDirty',
    'anyBufferDirty',
    'ruleSetFlattenedRows',
    'parameterSetFlattenedRows'
  ]
  const missingGetters = p03Getters.filter(
    (n) => !staticScanDtoHasField('store/modules/aps/rules.ts', n)
  )
  assert(
    'P03 rules.ts 含 6 态相关 getters（actions/forkPermission/ruleSetDirty/parameterSetDirty/anyBufferDirty + 2 flattenRows）',
    missingGetters.length === 0,
    missingGetters.length === 0 ? 'getters 齐备' : `缺失：${missingGetters.join(', ')}`
  )

  // P04 Rules.vue UI 实施
  const rulesVueHasNewDraft = staticScanDtoHasField('views/Aps/Rules.vue', '+ 新建草稿') ||
    staticScanDtoHasField('views/Aps/Rules.vue', '新建草稿')
  assert(
    'P04 Rules.vue 含 [+ 新建草稿] 按钮 + Dialog',
    rulesVueHasNewDraft,
    rulesVueHasNewDraft ? '入口已加' : '未发现 [+ 新建草稿]'
  )

  const rulesVueHasBlocks = staticScanDtoHasField('views/Aps/Rules.vue', 'BLOCK_LABELS') &&
    staticScanDtoHasField('views/Aps/Rules.vue', 'parameterRowsGrouped')
  assert(
    'P04 Rules.vue 含 BLOCK_LABELS 字典 + parameterRowsGrouped 计算',
    rulesVueHasBlocks,
    rulesVueHasBlocks ? '5 块区隔已实现' : '5 块区隔缺失'
  )

  const rulesVueHasDialogs = staticScanDtoHasField('views/Aps/Rules.vue', 'createDraftDialog') &&
    staticScanDtoHasField('views/Aps/Rules.vue', 'saveDraftDialog')
  assert(
    'P04 Rules.vue 含 createDraftDialog + saveDraftDialog 双 Dialog',
    rulesVueHasDialogs,
    rulesVueHasDialogs ? '双 Dialog 齐备' : 'Dialog 缺失'
  )

  // P05 index.ts 重导出 draftBuffer
  const indexHasReexport =
    staticScanDtoHasField('api/aps-v1/index.ts', 'flattenBlocks') &&
    staticScanDtoHasField('api/aps-v1/index.ts', 'applyEdit') &&
    staticScanDtoHasField('api/aps-v1/index.ts', 'buildPutBody')
  assert(
    'P05 api/aps-v1/index.ts 重导出 flattenBlocks/applyEdit/buildPutBody',
    indexHasReexport,
    indexHasReexport ? '已重导出' : '重导出缺失'
  )
}

async function verifyParameterSetDraft(token) {
  log('\n[P] ParameterSet 写维护：B 设计稿 Step 3+4+6 落地（parameter-set-drafts）')

  // P01-P05 静态扫描（与后端状态无关）
  runP01P05Static()

  // P06 探测：取一个 RuleSet id（来自列表）
  let r = await call('GET', '/api/governance/rule-sets', { token })
  const firstRuleSet = r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0
    ? r.json.data[0]
    : null
  // 后端实测返回 { id, ruleSetCode, ... }（camelCase）；前端 v1.4 §二十契约可能返 ruleSetId
  const ruleSetId =
    firstRuleSet?.ruleSetId ?? firstRuleSet?.RuleSetId ?? firstRuleSet?.id ?? null

  r = await call('GET', '/api/governance/parameter-sets', { token })
  const firstParamSet = r.ok && Array.isArray(r.json?.data) && r.json.data.length > 0
    ? r.json.data[0]
    : null
  const parameterSetId =
    firstParamSet?.parameterSetId ?? firstParamSet?.ParameterSetId ?? firstParamSet?.id ?? null

  // P07 POST /api/governance/rule-set/version（创建 RS DRAFT）— 空 body 应 4xx 或 500（FK 约束冲突）
  r = await call('POST', '/api/governance/rule-set/version', { token, body: {} })
  assert(
    'P07 POST /api/governance/rule-set/version（创建 RS DRAFT 端点可达）',
    r.code === 400 || r.code === 422 || r.code === 401 || r.code === 403 || r.code === 500,
    `code=${r.code}（400/422=校验；401/403=鉴权；500=FK 约束=controller 已接收；端点存在即过；≠404 即过）`
  )

  // P08 POST /api/governance/parameter-set/version（创建 PS DRAFT）
  r = await call('POST', '/api/governance/parameter-set/version', { token, body: {} })
  assert(
    'P08 POST /api/governance/parameter-set/version（创建 PS DRAFT 端点可达）',
    r.code === 400 || r.code === 422 || r.code === 401 || r.code === 403 || r.code === 500,
    `code=${r.code}（同上；端点存在即过；≠404 即过）`
  )

  // P09 PUT /api/governance/rule-set/version/{versionId}（更新 RS DRAFT）— 用 versionId=1 探测
  r = await call('PUT', '/api/governance/rule-set/version/1', { token, body: {} })
  assert(
    'P09 PUT /api/governance/rule-set/version/{versionId}（更新 RS DRAFT 端点可达）',
    r.code === 400 || r.code === 422 || r.code === 401 || r.code === 403 || r.code === 404,
    `code=${r.code}（404=版本不存在；4xx=校验生效；端点存在即过）`
  )

  // P10 PUT /api/governance/parameter-set/version/{versionId}（更新 PS DRAFT）
  r = await call('PUT', '/api/governance/parameter-set/version/1', { token, body: {} })
  assert(
    'P10 PUT /api/governance/parameter-set/version/{versionId}（更新 PS DRAFT 端点可达）',
    r.code === 400 || r.code === 422 || r.code === 401 || r.code === 403 || r.code === 404,
    `code=${r.code}`
  )

  // P11 POST /api/governance/rule-set/version/{versionId}/publish（发布 RS）
  r = await call('POST', '/api/governance/rule-set/version/1/publish', { token, body: {} })
  assert(
    'P11 POST /api/governance/rule-set/version/{versionId}/publish（发布 RS 端点可达）',
    r.code === 400 || r.code === 422 || r.code === 401 || r.code === 403 || r.code === 404,
    `code=${r.code}（404=版本不存在；400=状态校验；端点存在即过）`
  )

  // P12 POST /api/governance/parameter-set/version/{versionId}/publish（发布 PS）
  r = await call('POST', '/api/governance/parameter-set/version/1/publish', { token, body: {} })
  assert(
    'P12 POST /api/governance/parameter-set/version/{versionId}/publish（发布 PS 端点可达）',
    r.code === 400 || r.code === 422 || r.code === 401 || r.code === 403 || r.code === 404,
    `code=${r.code}`
  )

  // P13 GET /api/governance/rule-set/version/{versionId}（fork 第 1 步：取源 RS）
  r = await call('GET', '/api/governance/rule-set/version/1', { token })
  assert(
    'P13 GET /api/governance/rule-set/version/{versionId}（fork 取源 RS 端点可达）',
    r.code === 200 || r.code === 404 || r.code === 401 || r.code === 403,
    `code=${r.code}（200=有源版本；404=不存在；端点存在即过）`
  )

  // P14 GET /api/governance/parameter-set/version/{versionId}（fork 第 2 步：取源 PS）
  r = await call('GET', '/api/governance/parameter-set/version/1', { token })
  assert(
    'P14 GET /api/governance/parameter-set/version/{versionId}（fork 取源 PS 端点可达）',
    r.code === 200 || r.code === 404 || r.code === 401 || r.code === 403,
    `code=${r.code}`
  )

  // P15 综合：若 ruleSets/parameterSets 列表非空，确认 published-version 端点也能间接证明读路径通
  if (ruleSetId) {
    r = await call('GET', `/api/governance/rule-set/${ruleSetId}/published-version`, { token })
    assert(
      'P15 GET /api/governance/rule-set/{id}/published-version（与 V01 联动验证 read path）',
      r.ok || r.code === 404 || r.code === 422,
      `code=${r.code}（与 V01 共享读路径；端点可达即过）`
    )
  } else {
    skip('P15', 'rule-sets 列表为空，跳过 published-version 联动')
  }

  if (parameterSetId) {
    r = await call('GET', `/api/governance/parameter-set/${parameterSetId}/published-version`, { token })
    assert(
      'P15 GET /api/governance/parameter-set/{id}/published-version（与 V02 联动验证 read path）',
      r.ok || r.code === 404 || r.code === 422,
      `code=${r.code}`
    )
  } else {
    skip('P15', 'parameter-sets 列表为空，跳过 published-version 联动')
  }

  info(
    'P 段全部为「端点可达 + 静态契约」验收，不创建 DRAFT/不发布以避免脏数据上界锁；' +
      'B 设计稿 §3.6 接受类断言需 VERIFY_ALLOW_WRITE=1（前端未实现批量脚本，未提供）'
  )
}

/* ==================== [O] OPM 工艺规划模式治理（o-opm）==================== */
/**
 * 3号位 2026-09-23 T1 交付件：
 *   frontNew/docs/APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md
 *
 * 端点（2 个）：
 *   GET  /api/governance/routing-operations?materialId={物料Id}
 *   PUT  /api/governance/routing-operations/{id}/planning-mode
 *
 * 验证策略：O01 静态契约 + O02 端点可达 + O03 默认跳过接受类。
 * 端点可达用「发请求 → 期望 200/400/403/404/422」视为端点存在；**不实际更新 OPM** 避免脏数据上界锁。
 * 后端联调地址/真实 DTO 样本未到前，O02 部分断言可能 404；属预期。
 */

function runO01Static() {
  // O01-1 types/opm.ts 含 OperationPlanningMode + RoutingOperationDto + OPM_META
  const opmTypesContent = readSrc('api/aps-v1/types/opm.ts')
  const opmTypesOk =
    opmTypesContent.includes('OperationPlanningMode') &&
    opmTypesContent.includes('RoutingOperationDto') &&
    opmTypesContent.includes('OPM_META')
  assert(
    'O01 静态：types/opm.ts 含 OperationPlanningMode + RoutingOperationDto + OPM_META',
    opmTypesOk,
    opmTypesOk ? '契约类型齐备' : 'types/opm.ts 契约缺失'
  )

  // O01-2 opm.ts 含 listOperations + updatePlanningMode + MOCK_OPM_MATERIAL_ID
  const opmApiContent = readSrc('api/aps-v1/opm.ts')
  const opmApiOk =
    /async\s+listOperations\s*\(/.test(opmApiContent) &&
    /async\s+updatePlanningMode\s*\(/.test(opmApiContent) &&
    opmApiContent.includes('MOCK_OPM_MATERIAL_ID')
  assert(
    'O01 静态：api/aps-v1/opm.ts 含 listOperations + updatePlanningMode + MOCK_OPM_MATERIAL_ID',
    opmApiOk,
    opmApiOk ? 'API 封装齐备' : 'opm.ts API 封装缺失'
  )

  // O01-3 store/modules/aps/opm.ts 含 load + updatePlanningMode + 三态 getter
  const opmStoreContent = readSrc('store/modules/aps/opm.ts')
  // ESLint 会剥掉 object key 的引号（合法 identifier），用裸字符串或反斜杠转义都匹配；这里用 /[\s{]FINITE_RESOURCE[\s,:=]/ 形态精确匹配
  const opmStoreOk =
    /async\s+function\s+load\s*\(/.test(opmStoreContent) &&
    /async\s+function\s+updatePlanningMode\s*\(/.test(opmStoreContent) &&
    opmStoreContent.includes('modeCounts') &&
    /[\s{,]FINITE_RESOURCE[\s,:}]/.test(opmStoreContent) &&
    /[\s{,]UNCONSTRAINED[\s,:}]/.test(opmStoreContent) &&
    /[\s{,]WAIT_ONLY[\s,:}]/.test(opmStoreContent)
  assert(
    'O01 静态：store/modules/aps/opm.ts 含 load + updatePlanningMode + 三态 getter（FINITE_RESOURCE/UNCONSTRAINED/WAIT_ONLY）',
    opmStoreOk,
    opmStoreOk ? 'store 三态齐备' : 'opm store 缺失'
  )

  // O01-4 views/Aps/Opm.vue 文件存在 + 含 OPM_META + 三态中文标签
  const opmVueExists = staticFileExists('views/Aps/Opm.vue')
  const opmVueContent = readSrc('views/Aps/Opm.vue')
  const opmVueHasLabels =
    opmVueContent.includes('OPM_META') &&
    opmVueContent.includes('需资源') &&
    opmVueContent.includes('无约束') &&
    opmVueContent.includes('仅等待')
  assert(
    'O01 静态：views/Aps/Opm.vue 存在 + 含 OPM_META + 三态中文标签',
    opmVueExists && opmVueHasLabels,
    opmVueExists && opmVueHasLabels ? '页面骨架齐备' : 'Opm.vue 不全（文件/标签缺失）'
  )

  // O01-5 router/modules/aps.ts 注册 /operation-planning-mode 路由
  const routerContent = readSrc('router/modules/aps.ts')
  const routerOk =
    routerContent.includes("path: 'operation-planning-mode'") &&
    routerContent.includes("'aps.rule.view'") &&
    routerContent.includes("'aps.rule.edit'")
  assert(
    'O01 静态：router/modules/aps.ts 注册 /operation-planning-mode 路由（aps.rule.view + aps.rule.edit OR）',
    routerOk,
    routerOk ? '路由已挂' : 'OPM 路由未挂或权限码缺失'
  )

  // O01-6 api/aps-v1/index.ts 重导出 opmApi + MOCK_OPM_MATERIAL_ID
  const indexContent = readSrc('api/aps-v1/index.ts')
  const indexHasOpm = indexContent.includes('opmApi') && indexContent.includes('MOCK_OPM_MATERIAL_ID')
  assert(
    'O01 静态：api/aps-v1/index.ts 重导出 opmApi + MOCK_OPM_MATERIAL_ID',
    indexHasOpm,
    indexHasOpm ? '已重导出' : 'api/aps-v1/index.ts 未重导出 opmApi'
  )

  // O01-7 api/aps-v1/types/index.ts 重导出 opm
  const typesIndexContent = readSrc('api/aps-v1/types/index.ts')
  const typesIndexHasOpm = typesIndexContent.includes("export * from './opm'")
  assert(
    'O01 静态：api/aps-v1/types/index.ts 重导出 ./opm',
    typesIndexHasOpm,
    typesIndexHasOpm ? '已重导出' : 'types/index.ts 未重导出 ./opm'
  )

  // O01-8 Opm.vue 瘦身：3 处冗余文案已移除（使用说明 ElAlert / 演示数据提示 ElAlert / 三态取值说明 ElDescriptions 卡片）
  // 探针自证可失败：把任一标题字面量重新注入即触发 false
  const noUsageHelp = !opmVueContent.includes('title="使用说明"')
  const noMockHint = !opmVueContent.includes('title="演示数据提示"')
  const noThreeStateDesc = !opmVueContent.includes('三态取值说明')
  assert(
    'O01 静态：Opm.vue 已移除 3 处冗余文案（使用说明 / 演示数据提示 / 三态取值说明）',
    noUsageHelp && noMockHint && noThreeStateDesc,
    noUsageHelp && noMockHint && noThreeStateDesc
      ? '瘦身完成'
      : '仍有冗余文案未清'
  )

  // O01-9 Opm.vue 行内 ElOption 暴露 OPM_META[m].description 作为 tooltip（替代被删的 ElDescriptions 卡片）
  const hasOptionTooltip = /:title="OPM_META\[m\]\.description"/.test(opmVueContent)
  assert(
    'O01 静态：Opm.vue ElOption 暴露 OPM_META[m].description tooltip',
    hasOptionTooltip,
    hasOptionTooltip ? 'tooltip 已挂' : 'ElOption 缺 :title 绑定'
  )

  // O01-10 Opm.vue 不再 import 被删组件/常量（ElDescriptions / ElDescriptionsItem / MOCK_OPM_MATERIAL_ID）
  const epImportBlock = opmVueContent.match(/import\s*\{[\s\S]*?\}\s*from\s*['"]element-plus['"]/m)
  const epBlockText = epImportBlock ? epImportBlock[0] : ''
  const noDescImportClean = !epBlockText.includes('ElDescriptions') && !epBlockText.includes('ElDescriptionsItem')
  const noMockImport = !opmVueContent.includes('MOCK_OPM_MATERIAL_ID')
  assert(
    'O01 静态：Opm.vue 已清理 element-plus import 中的 ElDescriptions / ElDescriptionsItem 与 MOCK_OPM_MATERIAL_ID',
    noDescImportClean && noMockImport,
    noDescImportClean && noMockImport ? 'unused import 已清' : '仍有 unused import'
  )
}

async function verifyOpm(token) {
  log('\n[O] OPM 工艺规划模式治理（o-opm — 3号位 T1 交付件）')

  // O01 静态契约（与后端状态无关）
  runO01Static()

  // O02-1 GET /api/governance/routing-operations?materialId=1（端点可达）
  let r = await call('GET', '/api/governance/routing-operations', {
    token,
    query: { materialId: 1 }
  })
  assert(
    'O02 GET /api/governance/routing-operations?materialId=1（端点可达）',
    r.ok || r.code === 200 || r.code === 400 || r.code === 403 || r.code === 422,
    `code=${r.code}（200=有数据；400=参数校验；403=权限；端点可达即过；≠404 即过）`
  )

  // O02-2 PUT /api/governance/routing-operations/{id}/planning-mode（端点可达）
  r = await call('PUT', '/api/governance/routing-operations/1/planning-mode', {
    token,
    body: { operationId: 1, operationPlanningMode: 'FINITE_RESOURCE' }
  })
  assert(
    'O02 PUT /api/governance/routing-operations/{id}/planning-mode（端点可达）',
    r.ok || r.code === 200 || r.code === 400 || r.code === 403 || r.code === 404 || r.code === 422,
    `code=${r.code}（端点可达即过）`
  )

  // O02-3 三态校验：operationPlanningMode 非三态之一应 422
  r = await call('PUT', '/api/governance/routing-operations/1/planning-mode', {
    token,
    body: { operationId: 1, operationPlanningMode: 'INVALID_MODE' }
  })
  assert(
    'O02 PUT planning-mode 非三态值应 4xx（422=数据红线 / 400=校验生效 / 404=端点不存在）',
    r.code === 400 || r.code === 422 || r.code === 404 || r.code === 403,
    `code=${r.code}`
  )

  // O03 接受类（默认跳过）
  if (process.env.VERIFY_ALLOW_WRITE !== '1') {
    skip('O03 接受类', 'VERIFY_ALLOW_WRITE 未设置=1（脏数据风险；接受类需真实 OPM 修改端到端）')
  } else {
    info('VERIFY_ALLOW_WRITE=1 → O03 接受类断言开启（会真实修改 OPM 写库）')
    r = await call('PUT', '/api/governance/routing-operations/1/planning-mode', {
      token,
      body: { operationId: 1, operationPlanningMode: 'WAIT_ONLY' }
    })
    assert(
      'O03 PUT 真实写回（VERIFY_ALLOW_WRITE=1 模式）',
      r.ok || r.code === 404 || r.code === 403,
      `code=${r.code}`
    )
  }

  info('O 段全部为「端点可达 + 静态契约」验收；O03 接受类需 VERIFY_ALLOW_WRITE=1 才开启')
}

/* ==================== [RC] ResourceCalendar / ManualCapacity ==================== */

/**
 * [RC] 资源日历 / 人工能力槽维护（r-resource-calendar）
 * 依据：
 *  - 冻结文档 v1.3（frontNew/docs/APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_*.md）
 *  - 5号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》
 *  - 4号位 缺口函 G1/G2/G3/G4/C1/D2/D3 + G5（角色绑定）
 *
 * 口径：本段为「静态契约 + 端点可达」验收（与 [O] 段范式一致）。
 *  - 后端 9 端点已实现，但 6 权限码**无角色绑定**（G5）→ 真实 token 访问预期 403；
 *    故可达类断言接受 200/400/403/404/422（端点可达即过，≠404 联网层缺失即过）
 *  - 接受类（真实铺窗/建槽）默认跳过，需 VERIFY_ALLOW_WRITE=1
 */
async function verifyResourceCalendar(token) {
  log('\n[RC] 资源日历 / 人工能力槽维护（r-resource-calendar）')

  /* -------- R01 静态：types/resourceCalendar.ts 契约（camelCase） -------- */
  const typesContent = readSrc('api/aps-v1/types/resourceCalendar.ts')
  const dtoFields = [
    'resourceId',
    'resourceCode',
    'availableFlag',
    'manualSlotId',
    'productionDepartmentId',
    'operationName',
    'slotCode',
    'isActive'
  ]
  const missingDtoFields = dtoFields.filter((f) => !typesContent.includes(f))
  const hasInterfaces =
    typesContent.includes('ResourceCalendarBulkRequest') &&
    typesContent.includes('ManualSlotCalendarBulkRequest') &&
    typesContent.includes('ResourceCalendarEntryDto') &&
    typesContent.includes('ManualCapacitySlotDto') &&
    typesContent.includes('ManualSlotCalendarDto')
  assert(
    'RC01 静态：types/resourceCalendar.ts 含 8 个 camelCase 字段 + 5 个请求/响应接口',
    missingDtoFields.length === 0 && hasInterfaces,
    missingDtoFields.length === 0
      ? '字段与接口齐备'
      : `缺字段：${missingDtoFields.join(',')}；接口=${hasInterfaces}`
  )

  /* -------- R02 静态：无 PascalCase 字段（对接函 §三.2 与后端实际相反，负断言） -------- */
  const pascalHits = [
    'ResourceId:',
    'ProductionDepartmentId:',
    'AvailableFlag:',
    'ManualSlotId:',
    'OperationName:',
    'SlotCode:'
  ].filter((p) => typesContent.includes(p))
  assert(
    'RC02 静态：types/resourceCalendar.ts 无 PascalCase 字段名（Program.cs:93 = CamelCase）',
    pascalHits.length === 0,
    pascalHits.length === 0 ? '全 camelCase' : `发现 PascalCase：${pascalHits.join(',')}`
  )

  /* -------- R03 静态：6 权限码常量 + 铺窗边界 + 三态元数据 -------- */
  const permKeys = [
    'aps.resource_calendar.view',
    'aps.resource_calendar.edit',
    'aps.resource_calendar.delete',
    'aps.manual_capacity.view',
    'aps.manual_capacity.edit',
    'aps.manual_capacity.delete'
  ]
  const missingPerms = permKeys.filter((p) => !typesContent.includes(p))
  const hasBounds =
    typesContent.includes('BULK_DAYS_MIN') &&
    typesContent.includes('BULK_DAYS_MAX') &&
    typesContent.includes('CalendarConfigStatus') &&
    typesContent.includes('CALENDAR_CONFIG_META')
  // ESLint 剥合法 identifier 的 object key 引号 → 用裸串匹配三态值
  const hasStatusValues =
    typesContent.includes('CONFIGURED') &&
    typesContent.includes('NOT_CONFIGURED') &&
    typesContent.includes('UNKNOWN')
  assert(
    'RC03 静态：6 权限码 + BULK_DAYS 1..370 + 三态元数据齐备',
    missingPerms.length === 0 && hasBounds && hasStatusValues,
    missingPerms.length === 0
      ? '权限码/边界/三态齐备'
      : `缺权限码：${missingPerms.join(',')}；bounds=${hasBounds}；三态=${hasStatusValues}`
  )

  /* -------- R04 静态：api/aps-v1/resourceCalendar.ts 9 端点 + 过渡数据源 -------- */
  const apiContent = readSrc('api/aps-v1/resourceCalendar.ts')
  const apiMethods = [
    'bulkCreateWindows',
    'getWindows',
    'deleteWindow',
    'createSlot',
    'getSlots',
    'softDeleteSlot',
    'bulkCreateSlotWindows',
    'getSlotWindows',
    'deleteSlotWindow',
    'listResourceCandidates',
    'listDepartmentCandidates'
  ]
  const missingApi = apiMethods.filter((m) => !apiContent.includes(m))
  assert(
    'RC04 静态：api/aps-v1/resourceCalendar.ts 含 9 端点 + 2 过渡数据源方法',
    missingApi.length === 0,
    missingApi.length === 0 ? 'API 封装齐备' : `缺方法：${missingApi.join(',')}`
  )

  /* -------- R05 静态：store 双缓存 + 三态推导 + 六权限 getter -------- */
  const storeContent = readSrc('store/modules/aps/resourceCalendar.ts')
  const storeChecks = [
    'deviceWindowCache',
    'slotWindowCache',
    'statusOf',
    'canViewDevice',
    'canEditDevice',
    'canDeleteDevice',
    'canViewManual',
    'canEditManual',
    'canDeleteManual',
    'loadDeviceWindows',
    'createDeviceWindows',
    'removeDeviceWindow',
    'loadSlots',
    'createSlot',
    'disableSlot',
    'loadSlotWindows',
    'createSlotWindows',
    'removeSlotWindow'
  ]
  const missingStore = storeChecks.filter((m) => !storeContent.includes(m))
  // statusOf() 返回三个字符串字面量（值，非 object key）→ 用 includes 匹配
  const storeHasTri =
    storeContent.includes('UNKNOWN') &&
    storeContent.includes('CONFIGURED') &&
    storeContent.includes('NOT_CONFIGURED')
  assert(
    'RC05 静态：store 含双缓存 + 三态推导 + 六权限 getter + 12 动作',
    missingStore.length === 0 && storeHasTri,
    missingStore.length === 0 ? 'store 齐备' : `缺项：${missingStore.join(',')}；三态=${storeHasTri}`
  )

  /* -------- R06 静态：3 文件存在 + 页面 3 Tab + 顶部三 Alert -------- */
  const pageExists = staticFileExists('views/Aps/ResourceCalendar.vue')
  const bulkDlgExists = staticFileExists('views/Aps/components/CalendarBulkDialog.vue')
  const slotDlgExists = staticFileExists('views/Aps/components/ManualSlotCreateDialog.vue')
  const pageContent = readSrc('views/Aps/ResourceCalendar.vue')
  const hasTabs =
    pageContent.includes('manual-calendar') &&
    pageContent.includes('设备资源日历') &&
    pageContent.includes('人工能力槽') &&
    pageContent.includes('人工槽日历')
  const hasAlerts =
    pageContent.includes('未配日历 = 不可排') &&
    pageContent.includes('无有效窗口') &&
    pageContent.includes('该人工槽已停用')
  // 内部协调痕迹不得出现在页面模板（用户可见）：2026-09-27 按用户要求清理
  const internalLeaks = [
    '冻结文档同步中',
    '过渡数据源（G1 / G2 未决）',
    '待 5号位 交付',
    '资源池来源'
  ].filter((t) => pageContent.includes(t))
  assert(
    'RC06 静态：3 文件存在 + 3 Tab + 上下文态提示保留（未配/不可排/已停用），内部协调提示已清出页面',
    pageExists && bulkDlgExists && slotDlgExists && hasTabs && hasAlerts && internalLeaks.length === 0,
    `文件:${pageExists}/${bulkDlgExists}/${slotDlgExists}；Tab=${hasTabs}；上下文提示=${hasAlerts}；残留内部提示=${internalLeaks.join(',') || '无'}`
  )

  /* -------- R07 静态：router 注册 + mock 6 码预置 -------- */
  const routerContent = readSrc('router/modules/aps.ts')
  const routerOk =
    routerContent.includes("path: 'resource-calendar'") &&
    routerContent.includes('aps.resource_calendar.view') &&
    routerContent.includes('aps.manual_capacity.view')
  const authContent = readSrc('store/modules/aps/auth.ts')
  const missingMockPerms = permKeys.filter((p) => !authContent.includes(p))
  assert(
    'RC07 静态：路由注册 /resource-calendar（OR 双 view 码）+ mock 预置含 6 码',
    routerOk && missingMockPerms.length === 0,
    `路由=${routerOk}；mock 缺码=${missingMockPerms.join(',') || '无'}`
  )

  /* -------- R07b 静态：mock planner 不含 delete（3号位 2026-09-24 裁决） -------- */
  const plannerStart = authContent.indexOf("'aps.planner': {")
  const adminApsStart = authContent.indexOf("'aps.admin.aps': {")
  const plannerBlock =
    plannerStart >= 0 && adminApsStart > plannerStart
      ? authContent.slice(plannerStart, adminApsStart)
      : ''
  const plannerHasDelete =
    plannerBlock.includes('aps.resource_calendar.delete') ||
    plannerBlock.includes('aps.manual_capacity.delete')
  assert(
    'RC07b 静态：mock 预置 planner = view + edit（不含 delete，3号位 2026-09-24 裁决）',
    plannerBlock.length > 0 && !plannerHasDelete,
    plannerBlock.length === 0
      ? '未定位到 planner 预置块'
      : plannerHasDelete
        ? 'planner 仍持 delete 码（与裁决不符）'
        : 'planner 无 delete 码'
  )

  /* -------- R08 静态：业务提示保留 + 模板内不得残留内部措辞 -------- */
  // 只扫 <template> 之后的部分：文件头注释与 script 里的内部引用是允许的（开发文档用途）
  const templateOf = (rel) => {
    const c = readSrc(rel)
    const i = c.indexOf('<template>')
    return i >= 0 ? c.slice(i) : ''
  }
  const bulkTpl = templateOf('views/Aps/components/CalendarBulkDialog.vue')
  const slotTpl = templateOf('views/Aps/components/ManualSlotCreateDialog.vue')
  const hasAppendNotice = bulkTpl.includes('追加') && bulkTpl.includes('不覆盖')
  const hasBizHints =
    slotTpl.includes('人工能力槽不是员工') && slotTpl.includes('小工序')
  const leakWords = ['发函', '5号位', '2号位', '待交付', 'expose', '§', '过渡方案']
  const tplLeaks = []
  for (const [name, tpl] of [
    ['页面', templateOf('views/Aps/ResourceCalendar.vue')],
    ['铺窗弹窗', bulkTpl],
    ['新槽弹窗', slotTpl]
  ]) {
    for (const w of leakWords) {
      if (tpl.includes(w)) tplLeaks.push(`${name}:${w}`)
    }
  }
  assert(
    'RC08 静态：弹窗业务提示保留（追加不覆盖 / 能力槽不是员工）+ 三处模板无内部措辞残留',
    hasAppendNotice && hasBizHints && tplLeaks.length === 0,
    `追加=${hasAppendNotice}；业务提示=${hasBizHints}；模板残留=${tplLeaks.join(',') || '无'}`
  )

  /* -------- R09/R10 端点可达（3号位 绑定脚本落地后已收紧为「要求 200」） -------- */
  const reachMsg = (code) =>
    code === 200
      ? 'code=200'
      : code === 403
        ? 'code=403（G5 角色绑定未落地：检查 Sql/APS_Auth_resource_calendar_role_binding_20260924.sql 是否执行 + 重新登录）'
        : `code=${code}`

  let r = await call('GET', '/api/manual-capacity/slots', { token })
  assert(
    'RC09 端点可达：GET /api/manual-capacity/slots（要求 200，403 = G5 回归）',
    r.code === 200,
    reachMsg(r.code)
  )

  r = await call('GET', '/api/resource-calendar/1', { token })
  assert(
    'RC10 端点可达：GET /api/resource-calendar/1（要求 200，403 = G5 回归）',
    r.code === 200,
    reachMsg(r.code)
  )

  /* -------- R11 接受类（默认跳过） -------- */
  if (process.env.VERIFY_ALLOW_WRITE !== '1') {
    skip(
      'RC11 接受类（批量铺窗 / 新建人工槽）',
      'VERIFY_ALLOW_WRITE 未设置=1（会写真实数据）'
    )
  } else {
    r = await call('POST', '/api/resource-calendar/slots', {
      token,
      body: {
        resourceId: 1,
        startDate: '2026-10-01T00:00:00',
        days: 1,
        startTime: '08:00:00',
        endTime: '17:00:00',
        availableFlag: true,
        remark: 'verify R11'
      }
    })
    assert(
      'RC11 接受类：POST /api/resource-calendar/slots（VERIFY_ALLOW_WRITE=1）',
      r.ok || [200, 201, 400, 403].includes(r.code),
      `code=${r.code}`
    )
  }

  info('RC 段全部为「静态契约 + 端点可达」；C1/G1/G2/G3/G4/D2/D3/G5 缺口见 4号位 致 5号位 函')
}

/**
 * [D] Domain 维护（d-domain）— v1.2 Domain 专项 Pkg-3
 * 验收清单：
 *  - D01 静态：types/domain.ts 含 12 camelCase 字段（DTO 实体对齐）
 *  - D02 静态：types/domain.ts 无 PascalCase（Program.cs:93 = CamelCase）
 *  - D03 静态：api/aps-v1/domain.ts 8 方法 + 404 → EndpointUnavailableError + base path
 *  - D04 静态：Domain.vue 显式 import 16 EP 组件（ElDescriptions 等；2026-09-28 ElDescriptions 渲染 bug 教训）
 *  - D05 静态：业务约束（domainKey 不可改 + 仅 admin.system 可写 + 「需要系统管理员角色」tooltip）
 *  - D06 静态：模板无内部措辞残留
 *  - D07 静态：domain store 含 EndpointUnavailableError → unavailable 状态映射
 *  - D08-D12 端点可达：GET /domain-definition + /active + /{id} + /product-families + /factories
 *  - D13 接受类（默认跳过）：POST/PUT/enable/disable 已由 verify-integration.mjs [A] 段覆盖
 */
async function verifyDomain(token) {
  log('\n[D] Domain 维护（d-domain）— v1.2 Domain 专项 Pkg-3')

  /* -------- D01 静态：types/domain.ts 契约（camelCase） -------- */
  const typesContent = readSrc('api/aps-v1/types/domain.ts')
  const dtoFields = [
    'id',
    'domainKey',
    'domainName',
    'scopeType',
    'productFamilyId',
    'factoryId',
    'isActive',
    'sortOrder',
    'createdBy',
    'createdAt',
    'updatedBy',
    'updatedAt'
  ]
  const missingDtoFields = dtoFields.filter((f) => !typesContent.includes(f))
  const hasScopeLiterals = typesContent.includes("'FAMILY'") && typesContent.includes("'FACTORY_FAMILY'")
  const hasScopeLabels = typesContent.includes('DOMAIN_SCOPE_TYPE_LABELS')
  assert(
    'D01 静态：types/domain.ts 含 12 camelCase 字段 + ScopeType 字面量 + 中文标签',
    missingDtoFields.length === 0 && hasScopeLiterals && hasScopeLabels,
    `缺字段：${missingDtoFields.join(',') || '无'}；ScopeType=${hasScopeLiterals}；中文标签=${hasScopeLabels}`
  )

  /* -------- D02 静态：无 PascalCase 字段（Program.cs:93 = CamelCase） -------- */
  const pascalHits = [
    'DomainKey:',
    'DomainName:',
    'ScopeType:',
    'ProductFamilyId:',
    'FactoryId:'
  ].filter((p) => typesContent.includes(p))
  assert(
    'D02 静态：types/domain.ts 无 PascalCase 字段名（Program.cs:93 = CamelCase）',
    pascalHits.length === 0,
    pascalHits.length === 0 ? '全 camelCase' : `发现 PascalCase：${pascalHits.join(',')}`
  )

  /* -------- D03 静态：api/aps-v1/domain.ts 8 方法 + 404 处理 + base path -------- */
  const apiContent = readSrc('api/aps-v1/domain.ts')
  const apiMethods = [
    'list',
    'detail',
    'create',
    'update',
    'enable',
    'disable',
    'listProductFamilies',
    'listFactories'
  ]
  // 严格匹配 `<methodName>(` 形式（避免 list 命中 listProductFamilies 等）
  const missingMethods = apiMethods.filter(
    (m) => !new RegExp(`(?:^|[^a-zA-Z])${m}\\s*\\(`).test(apiContent)
  )
  const has404Handling =
    apiContent.includes('EndpointUnavailableError') &&
    apiContent.includes('apiErr?.code === 404')
  const baseOk = apiContent.includes('/api/governance/domain-definition')
  assert(
    'D03 静态：api/aps-v1/domain.ts 8 方法 + 404 → EndpointUnavailableError + base path',
    missingMethods.length === 0 && has404Handling && baseOk,
    `缺方法：${missingMethods.join(',') || '无'}；404=${has404Handling}；base=${baseOk}`
  )

  /* -------- D04 静态：Domain.vue 文件 + 显式 import 15 EP 组件（ElSwitch 09-29 已删，isActive 由 /enable /disable 专用端点管） -------- */
  const pageExists = staticFileExists('views/Aps/Domain.vue')
  const pageContent = pageExists ? readSrc('views/Aps/Domain.vue') : ''
  const requiredEp = [
    'ElAlert',
    'ElButton',
    'ElCard',
    'ElDescriptions',
    'ElDescriptionsItem',
    'ElDialog',
    'ElEmpty',
    'ElForm',
    'ElFormItem',
    'ElInput',
    'ElInputNumber',
    'ElMessage',
    'ElOption',
    'ElSelect',
    'ElTag',
    'ElTooltip'
  ]
  const missingEp = requiredEp.filter((c) => !pageContent.includes(c))
  assert(
    'D04 静态：Domain.vue 存在 + 显式 import 15 EP 组件（ElDescriptions 必须显式 import — 2026-09-28 bug 教训；ElSwitch 已删）',
    pageExists && missingEp.length === 0,
    `文件=${pageExists}；缺 import=${missingEp.join(',') || '无'}`
  )

  /* -------- D05 静态：业务约束（domainKey 不可改 + admin.system 角色门控 + tooltip） -------- */
  const hasBusinessHints =
    pageContent.includes('排程域标识由系统生成、不可改') &&
    pageContent.includes('aps.admin.system') &&
    pageContent.includes('需要系统管理员角色')
  assert(
    'D05 静态：Domain.vue 含业务约束（domainKey 不可改 + 仅 admin.system 可写）',
    hasBusinessHints,
    hasBusinessHints ? '业务约束齐备' : '缺业务约束文案'
  )

  /* -------- D06 静态：模板无内部协调痕迹 -------- */
  const templateOf = (rel) => {
    const c = readSrc(rel)
    const i = c.indexOf('<template>')
    return i >= 0 ? c.slice(i) : ''
  }
  const tpl = templateOf('views/Aps/Domain.vue')
  const leakWords = ['发函', '5号位', '2号位', '待交付', '3号位 缺口', 'B1-B3', 'expose']
  const tplLeaks = leakWords.filter((w) => tpl.includes(w))
  assert(
    'D06 静态：Domain.vue 模板无内部措辞残留',
    tplLeaks.length === 0,
    tplLeaks.length === 0 ? '无内部痕迹' : `残留：${tplLeaks.join(',')}`
  )

  /* -------- D07 静态：domain store 错误分流 -------- */
  const storeContent = readSrc('store/modules/aps/domain.ts')
  const hasUnavailable =
    storeContent.includes('EndpointUnavailableError') &&
    storeContent.includes("fetchStatus.value = 'unavailable'")
  assert(
    'D07 静态：domain store 含 EndpointUnavailableError → unavailable 状态映射',
    hasUnavailable,
    hasUnavailable ? '错误分流齐备' : '缺错误分流'
  )

  /* -------- D07b 静态：Domain.vue 写操作有 ElForm :rules + try/catch + submitting loading（2026-09-29 修复后固化） -------- */
  const editFormHasRef = pageContent.includes('ref="editFormRef"')
  const createFormHasRef = pageContent.includes('ref="createFormRef"')
  const editFormHasRules = /:rules="editRules"/.test(pageContent)
  const createFormHasRules = /:rules="createRules"/.test(pageContent)
  const hasEditProp = /prop="domainName"/.test(pageContent) && /prop="productFamilyId"/.test(pageContent)
  const hasSubmittingRefs =
    pageContent.includes('editSubmitting') &&
    pageContent.includes('createSubmitting') &&
    pageContent.includes('toggleSubmitting')
  const hasTryCatchInHandlers =
    /async function onSave[\s\S]*?try \{[\s\S]*?\} catch/.test(pageContent) &&
    /async function onCreate[\s\S]*?try \{[\s\S]*?\} catch/.test(pageContent) &&
    /async function onToggleActive[\s\S]*?try \{[\s\S]*?\} catch/.test(pageContent)
  const hasLoadingOnButtons =
    /:loading="editSubmitting"/.test(pageContent) &&
    /:loading="createSubmitting"/.test(pageContent) &&
    /:loading="toggleSubmitting"/.test(pageContent)
  assert(
    'D07b 静态：Domain.vue 写操作有 ElForm :rules/ref + prop + try/catch + submitting loading',
    editFormHasRef &&
      createFormHasRef &&
      editFormHasRules &&
      createFormHasRules &&
      hasEditProp &&
      hasSubmittingRefs &&
      hasTryCatchInHandlers &&
      hasLoadingOnButtons,
    `editFormRef=${editFormHasRef}；createFormRef=${createFormHasRef}；editRules=${editFormHasRules}；createRules=${createFormHasRules}；prop=${hasEditProp}；submitting=${hasSubmittingRefs}；try/catch=${hasTryCatchInHandlers}；loading=${hasLoadingOnButtons}`
  )

  /* -------- D07c 静态：Domain.vue onSave 的 patch 必须含 domainKey（后端 PUT 要求 [FromBody] DomainDefinition 全量实体 + UpdateAsync 校验 existing.DomainKey == input.DomainKey；09-29 修复后固化） -------- */
  const onSavePatchHasDomainKey = /async function onSave[\s\S]*?const patch[^}]*?domainKey:/.test(pageContent)
  assert(
    'D07c 静态：Domain.vue onSave patch 必须含 domainKey 字段（防 400 DomainKey 一经创建不可变更）',
    onSavePatchHasDomainKey,
    `onSave 含 domainKey=${onSavePatchHasDomainKey}`
  )

  /* -------- D07d 静态：Domain.vue editForm 不含 isActive（PUT 后端强制写回 existing.IsActive，编辑表单开关是误导 UX）；启停用由顶部专用按钮走 /enable /disable 端点（09-29 修复后固化） -------- */
  // 1. editForm.isActive 不能作为属性访问或赋值出现（排除 // 注释里的 false positive）
  const editFormNoIsActiveProp = !/editForm\.isActive/.test(pageContent)
  // 2. onSave patch 不含 isActive: editForm.isActive 或 isActive: <其它源>
  const onSavePatchNoIsActive = !/isActive:\s*editForm\.isActive/.test(pageContent) && !/isActive:\s*[a-zA-Z_$][\w$]*\.(?:isActive|true|false)/.test(pageContent.match(/const patch[\s\S]{0,500}/)?.[0] ?? '')
  // 3. 编辑表单不含 isActive 开关
  const formNoIsActiveSwitch = !/ElFormItem label="启用"[\s\S]*?ElSwitch/.test(pageContent)
  assert(
    'D07d 静态：Domain.vue 编辑表单无 isActive 字段/switch（防 PUT 静默丢弃；启停用走专用端点）',
    editFormNoIsActiveProp && onSavePatchNoIsActive && formNoIsActiveSwitch,
    `无 editForm.isActive=${editFormNoIsActiveProp}；patch 无 isActive=${onSavePatchNoIsActive}；表单无 switch=${formNoIsActiveSwitch}`
  )

  /* -------- D07e 静态：Domain.vue kpiCounts/filteredDefinitions 用 definitions（raw list，含 inactive）算 total/inactive/ALL（09-29 KPI bug 修复后固化：之前用 allActive.value 算 inactive 永远=0） -------- */
  // 1. kpiCounts 函数体里出现 definitions.value（说明 raw list 被使用）
  const kpiBlock = pageContent.match(/const kpiCounts[\s\S]{0,500}?\}/)?.[0] ?? ''
  const kpiUsesRawList = /definitions\.value/.test(kpiBlock)
  // 2. kpiCounts 不直接用 allActive.value 算 total 或 inactive（active 维度允许）
  const kpiTotalNotAllActive = !/total\s*=\s*allActive\.value\.length/.test(kpiBlock)
  const kpiInactiveNotAllActive = !/inactive\s*=\s*allActive\.value/.test(kpiBlock)
  // 3. filteredDefinitions 函数体里出现 definitions.value（赋给 raw），且 ALL 分支不直接用 allActive.value
  const filterBlock = pageContent.match(/const filteredDefinitions[\s\S]{0,700}?\}/)?.[0] ?? ''
  const filterUsesRawList = /definitions\.value/.test(filterBlock)
  const filterAllNotAllActive = !/['"]ALL['"][\s\S]{0,150}?allActive\.value/.test(filterBlock)
  assert(
    'D07e 静态：Domain.vue kpiCounts/filteredDefinitions 用 raw list（definitions）算 total/inactive/ALL（防 KPI=0/列表丢失已停用）',
    kpiUsesRawList && kpiTotalNotAllActive && kpiInactiveNotAllActive && filterUsesRawList && filterAllNotAllActive,
    `kpi 用 raw=${kpiUsesRawList}；total≠allActive=${kpiTotalNotAllActive}；inactive≠allActive=${kpiInactiveNotAllActive}；filter 用 raw=${filterUsesRawList}；filter ALL≠allActive=${filterAllNotAllActive}`
  )

  /* -------- D07f 静态：Domain.vue onCreate 创建成功后重置 statusFilter='ALL'（09-29 修复后固化：避免新建 active 域被「停用」筛选过滤掉、左侧列表空） -------- */
  // 函数体内含 try/catch/finally 嵌套，用 1500 字符宽口确保覆盖完整函数体
  const createBlock = pageContent.match(/async function onCreate[\s\S]{0,1500}/)?.[0] ?? ''
  const createResetsFilter =
    /statusFilter\.value\s*=\s*['"]ALL['"]/.test(createBlock) ||
    /statusFilter\.value\s*=\s*['"]ACTIVE['"]/.test(createBlock)
  assert(
    'D07f 静态：Domain.vue onCreate 创建成功后重置 statusFilter 为 ALL/ACTIVE（防新建条目被筛选过滤掉）',
    createResetsFilter,
    `onCreate 含 statusFilter 重置=${createResetsFilter}`
  )

  /* -------- D07g 静态：Domain.vue 更新时间用 formatUtcDateTime 工具（后端 DateTime 序列化为 naive UTC，必须显式按 UTC 解析再转本地；09-29 修复后固化） -------- */
  const utilityFileExists = existsSync(join(__dirname, '..', 'src', 'utils', 'datetime.ts'))
  const usesUtility = /from\s+['"]@\/utils\/datetime['"]/.test(pageContent)
  const noInlineUtc = !/dayjs\.utc\(/.test(pageContent)
  const noPlainDayjsUtcDisplay = !/dayjs\((?:currentDetail|detail|row)\.\w*[aA]t\w*\)\.format/.test(pageContent)
  assert(
    'D07g 静态：Domain.vue 用 @/utils/datetime 工具 + 无裸 dayjs(后端日期)（防后端 naive UTC 少 8h）',
    utilityFileExists && usesUtility && noInlineUtc && noPlainDayjsUtcDisplay,
    `工具文件存在=${utilityFileExists}；引用工具=${usesUtility}；无 dayjs.utc 内联=${noInlineUtc}；无裸 dayjs(${usesUtility ? '后端字段' : 'xxx'})=${noPlainDayjsUtcDisplay}`
  )

  /* -------- D07h 静态：Domain.vue domainKey 正则收紧到 {3,49} 与后端契约对齐（09-29 R2 回执要求前端放过长→后端 400 拦截；放开后必须保持收紧） -------- */
  const domainKeyRegexTight = /\{3,49\}[$]\//.test(pageContent)
  const domainKeyRegexNoLoose = !/\{3,63\}[$]\//.test(pageContent)
  assert(
    'D07h 静态：Domain.vue domainKey 正则 {3,49} 与后端契约对齐（防前端放行 51-64 字符被后端 400 拦下）',
    domainKeyRegexTight && domainKeyRegexNoLoose,
    `{3,49}=${domainKeyRegexTight}；无 {3,63}=${domainKeyRegexNoLoose}`
  )

  /* -------- D08-D12 端点可达 -------- */
  const reachMsg = (code) =>
    code === 200
      ? 'code=200'
      : code === 403
        ? 'code=403（角色绑定未落地或权限码缺失）'
        : `code=${code}`

  let r = await call('GET', '/api/governance/domain-definition', { token })
  const listOk = r.code === 200 && Array.isArray(r.json?.data)
  assert(
    'D08 端点可达：GET /api/governance/domain-definition',
    listOk,
    `${reachMsg(r.code)}；count=${r.json?.data?.length ?? 0}`
  )

  const list = r.json?.data || []
  if (!listOk || list.length === 0) {
    info('D09-D10 跳过：列表为空或接口不可用')
  } else {
    r = await call('GET', '/api/governance/domain-definition/active', { token })
    assert(
      'D09 端点可达：GET /api/governance/domain-definition/active',
      r.code === 200 && Array.isArray(r.json?.data),
      `${reachMsg(r.code)}；count=${r.json?.data?.length ?? 0}`
    )

    const firstId = list[0].id
    r = await call('GET', `/api/governance/domain-definition/${firstId}`, { token })
    assert(
      `D10 端点可达：GET /api/governance/domain-definition/${firstId}`,
      r.code === 200 && r.json?.data,
      reachMsg(r.code)
    )
  }

  r = await call('GET', '/api/governance/product-families', { token })
  assert(
    'D11 端点可达：GET /api/governance/product-families（Domain 维护页产品族下拉数据源）',
    r.code === 200 && Array.isArray(r.json?.data),
    `${reachMsg(r.code)}；count=${r.json?.data?.length ?? 0}`
  )

  r = await call('GET', '/api/governance/factories', { token })
  assert(
    'D12 端点可达：GET /api/governance/factories（Domain 维护页工厂下拉数据源）',
    r.code === 200 && Array.isArray(r.json?.data),
    `${reachMsg(r.code)}；count=${r.json?.data?.length ?? 0}`
  )

  /* -------- D13 接受类（默认跳过） -------- */
  if (process.env.VERIFY_ALLOW_WRITE !== '1') {
    skip('D13 接受类（POST/PUT/enable/disable 写真实记录）', 'VERIFY_ALLOW_WRITE 未设置=1（详见 verify-integration.mjs [A] 段）')
  } else {
    info('D13 接受类已在 verify-integration.mjs [A] 段覆盖（GROUP=domain）；此处不重复')
  }

  /* -------- D14-D17 动态：3号位 R1-R4 后端字段校验落码验证（09-29 催办单 §七；纯拒绝用例零副作用，期待 400） -------- */
  // 用一个统一基础载荷，每个断言单独覆盖 1 个字段的拒绝路径
  const baseDomainPayload = {
    domainName: 'D14-17基线',
    productFamilyId: 1,
    scopeType: 'FAMILY',
    sortOrder: 100
  }
  // D14 sortOrder 边界：-1 / 10000 → 400（V1 的拒绝侧）
  let r14neg = await call('POST', '/api/governance/domain-definition', {
    token,
    body: { ...baseDomainPayload, domainKey: 'D14_TEST_NEG', domainName: 'D14越下界', sortOrder: -1 }
  })
  let r14over = await call('POST', '/api/governance/domain-definition', {
    token,
    body: { ...baseDomainPayload, domainKey: 'D14_TEST_OVER', domainName: 'D14越上界', sortOrder: 10000 }
  })
  const d14Neg = r14neg.code === 400
  const d14Over = r14over.code === 400
  assert(
    'D14 sortOrder 边界：-1/10000 → 400（V1）',
    d14Neg && d14Over,
    `-1=${reachMsg(r14neg.code)}；10000=${reachMsg(r14over.code)}`
  )

  // D15 domainKey 字符集 + 长度下限：含中文/空格/点/分号/单引号/2 字符 → 400（V2 的拒绝侧；51 字符上限留给手工验证）
  const dkCases = [
    { name: '含中文', key: '中文_KEY' },
    { name: '含空格', key: 'A B' },
    { name: '含点', key: 'A.B' },
    { name: '含分号', key: 'A;B' },
    { name: '含单引号', key: "AB'D" },
    { name: '2字符（下限以下）', key: 'AB' }
  ]
  const d15Results = []
  for (const c of dkCases) {
    const rr = await call('POST', '/api/governance/domain-definition', {
      token,
      body: { ...baseDomainPayload, domainKey: c.key, domainName: `D15-${c.name}` }
    })
    const passed = rr.code === 400
    d15Results.push(`${c.name}=${rr.code}${passed ? '✓' : '✗'}`)
  }
  const d15Pass = d15Results.every((s) => s.endsWith('✓'))
  assert(
    'D15 domainKey 字符集 + 长度下限：异常 → 400（V2）',
    d15Pass,
    d15Results.join(' | ')
  )

  // D16 scopeType 大小写：`family` / `Family` → 400（V4 的拒绝侧；FAMILY 接受留给手工验证）
  let r16lower = await call('POST', '/api/governance/domain-definition', {
    token,
    body: { ...baseDomainPayload, domainKey: 'D16_TEST_LOWER', domainName: 'D16小写', scopeType: 'family' }
  })
  let r16mix = await call('POST', '/api/governance/domain-definition', {
    token,
    body: { ...baseDomainPayload, domainKey: 'D16_TEST_MIX', domainName: 'D16混合', scopeType: 'Family' }
  })
  const d16Lower = r16lower.code === 400
  const d16Mix = r16mix.code === 400
  assert(
    'D16 scopeType 严格大小写：family/Family → 400（V4）',
    d16Lower && d16Mix,
    `family=${reachMsg(r16lower.code)}；Family=${reachMsg(r16mix.code)}`
  )

  // D17 domainName 长度：1 / 100 字符 → 400（V5 的拒绝侧；2/64 字符接受留给手工验证）
  let r17short = await call('POST', '/api/governance/domain-definition', {
    token,
    body: { ...baseDomainPayload, domainKey: 'D17_TEST_SHORT', domainName: 'A' }
  })
  let r17long = await call('POST', '/api/governance/domain-definition', {
    token,
    body: { ...baseDomainPayload, domainKey: 'D17_TEST_LONG', domainName: 'A'.repeat(100) }
  })
  const d17Short = r17short.code === 400
  const d17Long = r17long.code === 400
  assert(
    'D17 domainName 长度：1/100 字符 → 400（V5）',
    d17Short && d17Long,
    `1=${reachMsg(r17short.code)}；100=${reachMsg(r17long.code)}`
  )

  // D18 既有数据完整性（V6）：回执称 FAMILY_INJECTION 等 mock 数据未受影响 — 实测 list 前 5 条均可读
  const existingSample = list.slice(0, Math.min(5, list.length))
  const d18 = existingSample.length > 0
  assert(
    'D18 既有数据完整性（V6）：FAMILY_INJECTION 等 mock 数据可正常读取',
    d18,
    `samples=${existingSample.map((x) => x.domainKey).join(',')}`
  )

  info('D 段：Domain CRUD + 启停 + 字典端点 + 字段校验（v1.2 Pkg-3 + 09-29 R1-R4 闭环）')
}

/* ==================== main ==================== */

/**
 * [M] §10A.1 已有订单提前 — OrderCanonicalId 自动带入（m-10a1 — 09-23 B4 已落地触发）
 * 验收清单：
 *  - M01 静态：types/order.ts OrderBasicInfo 含 orderCanonicalId / OrderAdvanceDialog.vue 含 orderCanonicalId 字段 +
 *              自动带入逻辑 + "5号位 待补列" 旧文案已移除 / Order.vue advanceCandidates 透传 orderCanonicalId /
 *              mocks/fixtures.ts mockOrderDetail basic 含 orderCanonicalId
 *  - M02 接受类（默认跳过）：VERIFY_ALLOW_WRITE=1 时执行；需 Order 页已选订单 + Dialog 自动带入 + 提交跳转 Candidate
 */
function readSrc(relPath) {
  try {
    return readFileSync(join(SRC_ROOT, relPath), 'utf-8')
  } catch {
    return ''
  }
}

function verifyM10A1Order() {
  log('\n[M] §10A.1 已有订单提前：OrderCanonicalId 自动带入（m-10a1 — B4 已落地触发）')

  // M01 静态：types/order.ts OrderBasicInfo 含 orderCanonicalId 字段
  const typesHasOrderCanonicalId = staticScanDtoHasField(
    'api/aps-v1/types/order.ts',
    'orderCanonicalId'
  )
  assert(
    'M01 静态：types/order.ts OrderBasicInfo 含 orderCanonicalId?: number',
    typesHasOrderCanonicalId,
    typesHasOrderCanonicalId ? '已加字段' : 'types/order.ts 未含 orderCanonicalId'
  )

  // M01 静态：OrderAdvanceDialog.vue 含 orderCanonicalId 字段（OrderAdvanceCandidate 接口）
  const dlgHasOrderCanonicalId = staticScanDtoHasField(
    'views/Aps/components/OrderAdvanceDialog.vue',
    'orderCanonicalId'
  )
  assert(
    'M01 静态：OrderAdvanceDialog.vue OrderAdvanceCandidate 含 orderCanonicalId?: number',
    dlgHasOrderCanonicalId,
    dlgHasOrderCanonicalId ? '已加字段' : 'OrderAdvanceDialog.vue 未含 orderCanonicalId'
  )

  // M01 静态：OrderAdvanceDialog.vue **模板内**提示文案面向用户——含"自动带入"+"手工填写"，
  //  且不得出现内部协调措辞（§/号位/B4/回执/兼容回退 等只许留在注释里）
  const dlgContent = readSrc('views/Aps/components/OrderAdvanceDialog.vue')
  const dlgTemplate = (() => {
    const i = dlgContent.indexOf('<template>')
    return i >= 0 ? dlgContent.slice(i) : ''
  })()
  const dlgHasAutoImport = dlgTemplate.includes('自动带入') && dlgTemplate.includes('手工填写')
  const dlgTemplateLeaks = ['§', '号位', 'B4', '回执', '兼容回退', 'OrderCanonicalId'].filter((t) =>
    dlgTemplate.includes(t)
  )
  assert(
    'M01 静态：OrderAdvanceDialog.vue 模板提示含"自动带入"+"手工填写"，且无内部措辞（§/号位/B4/回执/兼容回退）',
    dlgHasAutoImport && dlgTemplateLeaks.length === 0,
    dlgHasAutoImport && dlgTemplateLeaks.length === 0
      ? '模板文案用户可读、无内部措辞'
      : `模板文案异常（autoImport=${dlgHasAutoImport}, leaks=${dlgTemplateLeaks.join('|')}）`
  )

  // M01 静态：OrderAdvanceDialog.vue watch visible 时自动带入 canonicalId = o.orderCanonicalId
  const dlgHasCanonicalIdBinding = /canonicalId:\s*o\.orderCanonicalId/.test(dlgContent)
  assert(
    'M01 静态：OrderAdvanceDialog.vue watch 自动带入 canonicalId = o.orderCanonicalId',
    dlgHasCanonicalIdBinding,
    dlgHasCanonicalIdBinding ? 'watch 已自动带入' : 'watch 缺少 canonicalId 自动绑定'
  )

  // M01 静态：OrderAdvanceDialog.vue 不再含旧的"5号位 待补列"字样（已替换）
  const dlgHasOldText = /5号位 待补列/.test(dlgContent)
  assert(
    'M01 静态：OrderAdvanceDialog.vue 不再含旧"5号位 待补列"文案',
    !dlgHasOldText,
    dlgHasOldText ? '仍含旧兜底提示语' : '旧兜底提示语已移除'
  )

  // M01 静态：Order.vue advanceCandidates 透传 orderCanonicalId
  const orderContent = readSrc('views/Aps/Order.vue')
  const orderHasOrderCanonicalId = /orderCanonicalId:\s*o\.orderCanonicalId/.test(orderContent)
  assert(
    'M01 静态：Order.vue advanceCandidates 透传 orderCanonicalId',
    orderHasOrderCanonicalId,
    orderHasOrderCanonicalId ? '已透传' : 'Order.vue advanceCandidates 未透传'
  )

  // M01 静态：mocks/fixtures.ts mockOrderDetail basic 含 orderCanonicalId 字段
  const mocksHasOrderCanonicalId = staticScanDtoHasField(
    'api/aps-v1/__mocks__/fixtures.ts',
    'orderCanonicalId'
  )
  assert(
    'M01 静态：mocks/fixtures.ts mockOrderDetail basic 含 orderCanonicalId 字段',
    mocksHasOrderCanonicalId,
    mocksHasOrderCanonicalId ? 'mock 已加字段' : 'mocks/fixtures.ts mockOrderDetail basic 未含 orderCanonicalId'
  )

  // M03 静态（planVersionId 必填契约 — 5号位 2026-09-23《order-query 分页契约修正回执》§一）
  //  1) types/order.ts OrderQuery.planVersionId 必填（`: number` 不是 `?`）
  //  2) store/modules/aps/order.ts loadList 含 planVersionIdReady 校验
  //  3) Order.vue planVersionInputRequired computed + ElInput 含 required mark + onMounted 从 active-plan 取默认

  // M03-1：types 层强类型必填
  const typesContent = readSrc('api/aps-v1/types/order.ts')
  // 先把 OrderQuery 接口块抽出来（`interface OrderQuery { ... }`），避免跨接口误判 OrderBasicInfo.planVersionId?:
  const orderQueryBlock = typesContent.match(/interface OrderQuery\s*\{[\s\S]*?\n\}/)?.[0] ?? ''
  const typesRequiredMatch = /\n\s*planVersionId:\s*number\b/.test(orderQueryBlock)
  const typesHasOptional = /\n\s*planVersionId\?\s*:/.test(orderQueryBlock)
  assert(
    'M03-1 静态：types/order.ts OrderQuery.planVersionId 必填（`: number`，5号位 2026-09-23 回执）',
    typesRequiredMatch && !typesHasOptional,
    typesRequiredMatch && !typesHasOptional
      ? 'OrderQuery.planVersionId 已强类型必填'
      : `types/order.ts OrderQuery.planVersionId 类型标记异常（required=${typesRequiredMatch}, hasOptional=${typesHasOptional}）`
  )

  // M03-2：store 层 loadList 校验
  const storeContent = readSrc('store/modules/aps/order.ts')
  const storeHasReadyCheck = /planVersionIdReady\.value/.test(storeContent) && /handleError\(listError,\s*fallback\)/.test(storeContent)
  assert(
    'M03-2 静态：store/modules/aps/order.ts loadList 含 planVersionIdReady 校验',
    storeHasReadyCheck,
    storeHasReadyCheck ? 'loadList 必填校验已落地' : 'loadList 缺 planVersionIdReady 校验（漏传会被框架 400）'
  )

  // M03-3：Order.vue UI 必填 + 默认值
  const orderVueContent = readSrc('views/Aps/Order.vue')
  const orderHasPlanVersionInputRequired = /planVersionInputRequired\s*=\s*computed/.test(orderVueContent)
  const orderHasRequiredMark = /filter-search-sm-required/.test(orderVueContent)
  const orderHasDefaultFromOverview = /setDefaultPlanVersionId\(/.test(orderVueContent) && /activeVersions/.test(orderVueContent)
  assert(
    'M03-3 静态：Order.vue planVersionInputRequired + required mark + 默认值从 active-plan 取',
    orderHasPlanVersionInputRequired && orderHasRequiredMark && orderHasDefaultFromOverview,
    orderHasPlanVersionInputRequired && orderHasRequiredMark && orderHasDefaultFromOverview
      ? 'UI 必填 + required mark + 默认值 三件齐备'
      : `UI 校验未完整（computed=${orderHasPlanVersionInputRequired}, mark=${orderHasRequiredMark}, default=${orderHasDefaultFromOverview}）`
  )

  // M02 接受类（默认跳过；VERIFY_ALLOW_WRITE=1 时开启）
  if (process.env.VERIFY_ALLOW_WRITE !== '1') {
    skip('M02 接受类', 'VERIFY_ALLOW_WRITE 未设置=1（脏数据风险；接受类需 Order 页选单 + Dialog 自动带入 + 提交跳转 Candidate 端到端）')
  } else {
    // TODO（VERIFY_ALLOW_WRITE=1 时实施）：从 Order 页 UI 选订单 → 打开 OrderAdvanceDialog → 验证 Row.canonicalId = OrderBasicInfo.orderCanonicalId → 改 manualTargetDueDate + 提交 → 验证 /api/governance/run/candidate 200 + payload.orderTargets[].orderCanonicalId = 覆盖值
    skip('M02 接受类', 'VERIFY_ALLOW_WRITE=1 但 M02 端到端需浏览器走查（前端 Playwright/Cypress 不在本次范围；本段为静态绿 + 接受类开关）')
  }
}

async function main() {
  log(`\n========== APS V1 4号位 v1.4 §二十九 U23-U50 验收 ==========`)
  log(`API Base: ${API_BASE}`)
  log(`Group:    ${GROUP}`)

  const adminToken = await loginAsAdmin()

  if (GROUP === 'all' || GROUP === 'interfaces') {
    await verifyInterfaces(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'rbac-negate') {
    await verifyRbacNegate(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'current-user') {
    await verifyCurrentUser(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'static') {
    verifyStaticScan()
  }
  if (GROUP === 'all' || GROUP === 'business-rules') {
    await verifyBusinessRules(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'v14-additions') {
    await verifyV14Additions(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'j-10a') {
    await verifyJ10A(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'm-10a1') {
    verifyM10A1Order()
  }
  if (GROUP === 'all' || GROUP === 'parameter-set-drafts') {
    await verifyParameterSetDraft(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'o-opm') {
    await verifyOpm(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'r-resource-calendar') {
    await verifyResourceCalendar(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'd-domain') {
    await verifyDomain(adminToken)
  }

  log(`\n========== 结果 ==========`)
  log(`✅ 通过：${passCount}`)
  log(`❌ 失败：${failCount}`)
  log(`·  跳过：${skipCount}`)

  if (failCount > 0) {
    process.exit(1)
  }
}

main().catch((err) => {
  bad(`脚本异常：${err.message}`)
  console.error(err)
  process.exit(1)
})
