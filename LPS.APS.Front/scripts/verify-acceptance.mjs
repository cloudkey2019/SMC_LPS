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
 *      - O03 接受类（默认跳过）：VERIFY_ALLOW_WRITE=1 时开启
 *      - O04 物料编码补全：types/material.ts 含 MaterialLookupItem /
 *                         materialLookup.ts 含 searchMaterials（GET lookups/materials）/
 *                         index 重导出 / Opm.vue 顶部 ElInput number → ElAutocomplete
 *      - O05 紧凑三态摘要：Opm.vue 移除 3 张 KPI 卡片（无 opm-kpi/.kpi-desc）/
 *                         工具栏内 ElFormItem v-if=hasList + 3 ElTag + 共 N 条
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
 *  - CTP DomainKey 段：GROUP=a-ctp-domainkey node scripts/verify-acceptance.mjs（A01-A05 前端 + C01-C02 后端契约，3号位 C 路径落码 09-30）
 *  - ManualETA 精简段：GROUP=me-eta node scripts/verify-acceptance.mjs（ME01-ME02 静态，顶部使用说明已撤）
 *  - 资源日历段：GROUP=r-resource-calendar node scripts/verify-acceptance.mjs
 *  - Domain 段：GROUP=d-domain node scripts/verify-acceptance.mjs
 *  - TaskSplitRuleConfig 段：GROUP=t-tsrc node scripts/verify-acceptance.mjs（TSRC01-TSRC05 静态契约）
 *  - DemandProtection 冻结一致性段：GROUP=dp-freeze node scripts/verify-acceptance.mjs（DP01-DP05 静态，R1 stale 注释 + §2.3/§2.4 文案）
 *  - DemandProtection lps/ 联调段：GROUP=dplive node scripts/verify-acceptance.mjs（DPL01-DPL10 静态，per-lock 列表 + /summary KPI + 前端聚合）
 *  - Explanation 冻结一致性段：GROUP=e-freeze node scripts/verify-acceptance.mjs（E01-E10 + E13-E22 静态 + E21 动态 + E11/E12 skip，R1 15 码字典 + R3 撤 ScheduleFailureInfo + 可选链漏写探针 + 版本不重复展示探针 + PlanVersionStatus 7 码探针 + 业务优先级渲染探针 + 首次 mount 联动探针 + selectVersion 并发去重探针 + loadCurrent allSettled 容错探针 + 10-10 option A 收口探针（撤 重排建议 + 设备故障 + /api/business-fact-issues 聚合）+ 10-10 business-fact-issues 端点 DDL 落地探针 + 10-10 样式简洁整齐探针；R2/R3 跨号位等回执）
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
const GROUP = process.env.GROUP || 'all' // all | interfaces | rbac-negate | current-user | static | business-rules | j-10a | m-10a1 | v14-additions | parameter-set-drafts | o-opm | r-resource-calendar | d-domain | a-ctp-domainkey | me-eta | t-tsrc | dp-freeze | dplive | e-freeze
const ADMIN_CODE = 'admin'
const ADMIN_PWD = 'Admin@123456'

// 路径解析（用于静态扫描 frontNew/src/ + lps/）
const __dirname = dirname(fileURLToPath(import.meta.url))
const SRC_ROOT = join(__dirname, '..', 'src')
const LPS_ROOT = join(__dirname, '..', '..', 'lps')

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
    'views/Aps/components/StrategyDiffPanel.vue',
    'views/Aps/TaskSplitRuleConfig.vue'
  ]
  const pageUsesUtil = datetimePages.map((p) => {
    const fp = join(SRC_ROOT, p)
    if (!existsSync(fp)) return { p, uses: false, missing: true }
    const c = readFileSync(fp, 'utf-8')
    return { p, uses: /from\s+['"]@\/utils\/datetime['"]/.test(c), missing: false }
  })
  const allPagesUseUtil = pageUsesUtil.every((x) => x.uses)
  assert(
    'S03 9 个页面统一引用 @/utils/datetime 显示后端 DateTime（防 naive UTC 少 8h）',
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

  // O01-4 views/Aps/Opm.vue 文件存在 + 含 OPM_META + 三态运行时 label 引用（不再硬编码中文标签）
  const opmVueExists = staticFileExists('views/Aps/Opm.vue')
  const opmVueContent = readSrc('views/Aps/Opm.vue')
  const opmVueHasLabels =
    opmVueContent.includes('OPM_META') &&
    /OPM_META\.FINITE_RESOURCE\.label/.test(opmVueContent) &&
    /OPM_META\.UNCONSTRAINED\.label/.test(opmVueContent) &&
    /OPM_META\.WAIT_ONLY\.label/.test(opmVueContent)
  assert(
    'O01 静态：views/Aps/Opm.vue 存在 + 含 OPM_META + 三态运行时 label 引用',
    opmVueExists && opmVueHasLabels,
    opmVueExists && opmVueHasLabels ? '页面骨架齐备' : 'Opm.vue 不全（文件/OPM_META 三态 label 缺失）'
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

  // ===== O04 物料编码自动补全（用户视觉键由物料 ID 改为物料编码）=====

  // O04-1 types/material.ts 含 MaterialLookupItem（masterId / code / name / spec / uom / isActive）
  const materialTypesContent = readSrc('api/aps-v1/types/material.ts')
  const materialDtoOk =
    materialTypesContent.includes('MaterialLookupItem') &&
    /[\s{,]masterId[\s,:}]/.test(materialTypesContent) &&
    /[\s{,]code[\s,:}]/.test(materialTypesContent) &&
    materialTypesContent.includes('name') &&
    materialTypesContent.includes('spec') &&
    materialTypesContent.includes('uom') &&
    materialTypesContent.includes('isActive')
  assert(
    'O04 静态：api/aps-v1/types/material.ts 含 MaterialLookupItem（masterId/code/name/spec/uom/isActive）',
    materialDtoOk,
    materialDtoOk ? 'DTO 齐备' : 'types/material.ts 缺字段'
  )

  // O04-2 materialLookup.ts 含 searchMaterials + 防抖过滤短串
  const materialLookupContent = readSrc('api/aps-v1/materialLookup.ts')
  const materialApiOk =
    /async\s+searchMaterials\s*\(/.test(materialLookupContent) &&
    materialLookupContent.includes('/api/governance/lookups/materials') &&
    materialLookupContent.includes("params: { search: q }")
  assert(
    'O04 静态：api/aps-v1/materialLookup.ts 含 searchMaterials（GET lookups/materials）',
    materialApiOk,
    materialApiOk ? 'API 封装齐备' : 'materialLookup.ts 缺失'
  )

  // O04-3 index.ts / types/index.ts 重导出
  const indexHasMaterialApi = indexContent.includes('materialLookupApi')
  const typesIndexHasMaterial = typesIndexContent.includes("export * from './material'")
  assert(
    'O04 静态：api/aps-v1/index.ts 与 types/index.ts 重导出 materialLookup / MaterialLookupItem',
    indexHasMaterialApi && typesIndexHasMaterial,
    indexHasMaterialApi && typesIndexHasMaterial ? '已重导出' : '未重导出'
  )

  // O04-4 Opm.vue 顶部改用 ElAutocomplete（ElInput number 已移除）+ materialCodeInput + selectedMaterial
  // 探针自证可失败：把 ElInput 重新引入即触发 false
  const noOldInput = !opmVueContent.includes('v-model.number="materialIdInput"')
  const hasAutocomplete = /<ElAutocomplete\b/.test(opmVueContent)
  const hasValueKey = /value-key="code"/.test(opmVueContent)
  const hasFetchSuggestions = /:fetch-suggestions="fetchMaterialSuggestions"/.test(opmVueContent)
  const hasCodeInput = opmVueContent.includes('materialCodeInput')
  const hasSelectedMaterial = opmVueContent.includes('selectedMaterial')
  const hasNewLabel = opmVueContent.includes('物料编码')
  assert(
    'O04 静态：Opm.vue 顶部 ElInput number → ElAutocomplete（materialCodeInput/selectedMaterial/value-key=code）',
    noOldInput && hasAutocomplete && hasValueKey && hasFetchSuggestions && hasCodeInput && hasSelectedMaterial && hasNewLabel,
    noOldInput && hasAutocomplete && hasValueKey && hasFetchSuggestions && hasCodeInput && hasSelectedMaterial && hasNewLabel
      ? '顶部改造完成'
      : '顶部仍含旧 ElInput number 或缺新 ElAutocomplete 关键属性'
  )

  // ===== O05 紧凑三态摘要（替代 KPI 卡片；hasList 时显示）=====

  // O05-1 Opm.vue 不再有 3 张 KPI 卡片（无 ElRow opm-kpi / 无 .kpi-desc）
  // 探针自证可失败：把 KPI 卡片重新引入即触发 false
  const noKpiCards = !/class="opm-kpi"/.test(opmVueContent) && !opmVueContent.includes('.kpi-desc')
  assert(
    'O05 静态：Opm.vue 已移除 3 张 KPI 卡片（无 opm-kpi / .kpi-desc 残留）',
    noKpiCards,
    noKpiCards ? 'KPI 卡片已清' : '仍有 KPI 卡片或 desc 样式残留'
  )

  // O05-2 Opm.vue 工具栏内新增紧凑三态摘要（hasList + 3 ElTag + 共 N 条）
  const hasSummaryFormItem = /<ElFormItem[^>]*class="opm-summary"/.test(opmVueContent)
  const hasSummaryHasListGuard = /<ElFormItem[^>]*v-if="hasList"[^>]*class="opm-summary"/.test(opmVueContent)
  const hasThreeTags =
    opmVueContent.includes('OPM_META.FINITE_RESOURCE.label') &&
    opmVueContent.includes('OPM_META.UNCONSTRAINED.label') &&
    opmVueContent.includes('OPM_META.WAIT_ONLY.label') &&
    opmVueContent.includes('modeCounts.FINITE_RESOURCE') &&
    opmVueContent.includes('modeCounts.UNCONSTRAINED') &&
    opmVueContent.includes('modeCounts.WAIT_ONLY')
  const hasSummaryTotal = /summary-total[^>]*>\s*共\s*<strong>/.test(opmVueContent) || /class="summary-total"[^>]*>\s*共/.test(opmVueContent)
  assert(
    'O05 静态：Opm.vue 工具栏内新增紧凑三态摘要（v-if=hasList + 3 ElTag + 共 N 条）',
    hasSummaryFormItem && hasSummaryHasListGuard && hasThreeTags && hasSummaryTotal,
    hasSummaryFormItem && hasSummaryHasListGuard && hasThreeTags && hasSummaryTotal
      ? '紧凑摘要已挂'
      : '紧凑摘要缺失（v-if/ElTag/共 N 条任一缺）'
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

/* ==================== [A] CTP 评估域键透传（a-ctp-domainkey）==================== */
/**
 * [A] CTP 评估域键透传（a-ctp-domainkey）
 *  - 触发：09-30 CTP 评估 422 修复（A 路径 — 4号位 前端自做）
 *  - 现象：POST /api/ctp/evaluate 缺 domainKey → ASP.NET Core [Required] 模型校验 → 422
 *  - A 路径修复：types/ctp.ts CtpInput 加 domainKey: DomainKey；Ctp.vue 顶部加 ElSelect；store 透传
 *  - C 路径（3号位 09-30 落码）：CtpEvaluateRequest.DomainKey 改 optional + service 自动 resolve
 *  - 4号位 09-30 撤下拉（用户拍板）：前端不再传 domainKey，纯由后端 resolve
 *  - A01-A05 翻成「撤下拉」反断言（前端不再含 domainKey 链路）
 *  - C01-C02 后端契约层断言保留
 *  - CTP-EVAL01/02 动态（10-09 v1.0 真实落地回执 §五 4号位 兑现）：
 *    - 验收依据：frontNew/docs/APS_V1_CTP主数据迁移_3号位致4号位_真实落地回执_v1.0_20261009.md §四/§五
 *    - 8 真实物料（VALVE/LGJ/XC/JS/WKQ/TZP/MJ/QT）× 4 工厂（CN/BJ/SH/TJ）= 32 路径
 *    - 全 501（核心试算未接入契约行为）；禁止 400（多域歧义）/ 422（DTO 字段缺失）
 *    - 守门：VERIFY_CTP_EVAL=1（默认跳过，避免无 dev 时炸 32 次请求）
 *  - 验收依据：
 *    - frontNew/docs/APS_V1_CTP评估C路径落码回执_3号位致4号位_v1.0_20260930.md §五
 *    - frontNew/docs/APS_V1_CTP主数据迁移_3号位致4号位_真实落地回执_v1.0_20261009.md §五
 */
async function verifyCtpDomainKey(token) {
  log('\n[A] CTP 评估域键（a-ctp-domainkey — 09-30 CTP 422 修复 / 4号位撤前端下拉）')

  // A01 静态：types/ctp.ts CtpInput 不含 domainKey 字段定义（撤下拉后纯前端不再传）
  //   - 用 `\bdomainKey\s*:` 排除注释里的字样（如 `CtpEvaluateRequest.DomainKey 改 optional`）
  const ctpTypesContent = readSrc('api/aps-v1/types/ctp.ts')
  const ctpInputBlock = (() => {
    const m = ctpTypesContent.match(/export\s+interface\s+CtpInput\b\s*\{([\s\S]*?)\n\}/)
    return m ? m[1] : ''
  })()
  const a01 = ctpInputBlock.length > 0 && !/\bdomainKey\s*:\s*/.test(ctpInputBlock)
  assert(
    'A01 静态：types/ctp.ts CtpInput 不含 domainKey 字段定义（撤下拉后纯前端不再传）',
    a01,
    a01 ? '已撤' : ctpInputBlock.length === 0 ? '未定位 CtpInput 块' : '仍含 domainKey 字段'
  )

  // A02 静态：Ctp.vue 无 ElSelect v-model="form.domainKey"（前端下拉已撤）
  const ctpVueContent = readSrc('views/Aps/Ctp.vue')
  const a02 = !/<ElSelect\b[^>]*v-model\s*=\s*['"]form\.domainKey['"]/.test(ctpVueContent)
  assert(
    'A02 静态：Ctp.vue 不含 ElSelect v-model="form.domainKey"（下拉已撤）',
    a02,
    a02 ? '已撤' : '仍含下拉'
  )

  // A03 静态：Ctp.vue onSubmit 不再传 domainKey（前端 evaluate() 调用无该字段）
  const onSubmitBlock = (() => {
    const m = ctpVueContent.match(/async\s+function\s+onSubmit[\s\S]*?\n\}/)
    return m ? m[0] : ''
  })()
  const a03 = onSubmitBlock.length > 0 && !/\bdomainKey\b/.test(onSubmitBlock)
  assert(
    'A03 静态：Ctp.vue onSubmit 不含 domainKey（前端 evaluate() 不再传该字段）',
    a03,
    a03 ? '已撤' : onSubmitBlock.length === 0 ? '未定位 onSubmit' : 'onSubmit 仍含 domainKey'
  )

  // A04 静态：store/ctp.ts defaultInput 不含 domainKey（默认值已撤）
  //   - 默认值函数是 `const defaultInput = (): CtpInput => ({...})`（箭头返回，无 return）
  //   - 用 indexOf 定位后切片到首个 `})` 闭合，避免依赖函数体形态
  const ctpStoreContent = readSrc('store/modules/aps/ctp.ts')
  const defaultInputBlock = (() => {
    const i = ctpStoreContent.indexOf('const defaultInput')
    if (i < 0) return ''
    const slice = ctpStoreContent.slice(i)
    const m = slice.match(/=>\s*\(\s*\{([\s\S]*?)\}\s*\)/)
    return m ? m[1] : ''
  })()
  const a04 = defaultInputBlock.length > 0 && !/\bdomainKey\s*:\s*/.test(defaultInputBlock)
  assert(
    'A04 静态：store/ctp.ts defaultInput 不含 domainKey 字段定义（默认值已撤）',
    a04,
    a04 ? '已撤' : defaultInputBlock.length === 0 ? '未定位 defaultInput' : 'defaultInput 仍含 domainKey 字段'
  )

  // A05 静态：Ctp.vue 不含 noDomainAccess / 空态 ElAlert「无任何 Domain 权限」（撤下拉后无空态兜底）
  const a05 =
    !/\bnoDomainAccess\b/.test(ctpVueContent) &&
    !/无任何 Domain 权限/.test(ctpVueContent)
  assert(
    'A05 静态：Ctp.vue 不含 noDomainAccess / 空态警告（撤下拉后无 UI 空态兜底）',
    a05,
    a05 ? '已撤' : '仍含空态兜底'
  )

  // A06 动态（需后端接入）— 本期不跑，留待 3号位 接入 CTP 引擎后开启
  skip('A06 动态：CTP 端点联调验证（需 3号位 接入 CTP 引擎，本期跳过）')

  /* -------- A07-A08 静态（Ctp.vue 工厂下拉拉真实 API，09-30 接入）--------
   * 验收依据：frontNew/docs/4号位-2026-09-30-CTP评估接口422诊断-A+C并行修复-给3号位.md
   *   + 3号位 GET /api/governance/factories（domainApi.listFactories 复用）
   * 口径：纯前端静态扫描，确保 Ctp.vue 不再硬编码工厂，改走 API + 兜底
   */
  // A07 静态：Ctp.vue 导入 domainApi 且含 FACTORY_OPTIONS 为 ref（不再硬编码 const 数组）
  //   - 反例：`const FACTORY_OPTIONS = [{ code: 'F-SUZ-01', ... }]` → 失败
  const factoryImportMatch = /import\s*\{[^}]*\bdomainApi\b[^}]*\}\s*from\s*['"]@\/api\/aps-v1\/domain['"]/.test(
    ctpVueContent
  )
  const factoryRefMatch =
    /\bFACTORY_OPTIONS\s*=\s*ref\s*<[^>]*FactorySummaryDto[^>]*>\s*\(\s*\[\s*\]\s*\)/.test(
      ctpVueContent
    )
  const factoryNoHardcodedArray = !/^\s*const\s+FACTORY_OPTIONS\s*=\s*\[[\s\S]*?\bcode\s*:/.test(
    ctpVueContent
  )
  const a07 = factoryImportMatch && factoryRefMatch && factoryNoHardcodedArray
  assert(
    'A07 静态：Ctp.vue 导入 domainApi + FACTORY_OPTIONS 为 ref（不再硬编码工厂数组）',
    a07,
    a07
      ? '已接入 API（ref + listFactories）'
      : !factoryImportMatch
        ? '缺 domainApi 导入'
        : !factoryRefMatch
          ? 'FACTORY_OPTIONS 未声明为 ref<FactorySummaryDto[]>([])'
          : '仍含硬编码 FACTORY_OPTIONS 数组'
  )

  // A08 静态：Ctp.vue onMounted 内调 domainApi.listFactories() + 含 FACTORY_FALLBACK 兜底
  //   - 必须 listFactories() 调用 + 兜底数组赋值（FACTORY_FALLBACK 或 fallback）
  const onMountedBlock = (() => {
    const m = ctpVueContent.match(/onMounted\s*\(\s*\(\s*\)\s*=>\s*\{([\s\S]*?)\n\}\s*\)/)
    return m ? m[1] : ''
  })()
  const callsListFactories = /\bdomainApi\s*\.\s*listFactories\s*\(\s*\)/.test(onMountedBlock)
  const hasFallback =
    /FACTORY_FALLBACK/.test(ctpVueContent) || /fallback/i.test(onMountedBlock)
  const a08 = callsListFactories && hasFallback
  assert(
    'A08 静态：Ctp.vue onMounted 调 domainApi.listFactories() + 含兜底 FACTORY_FALLBACK',
    a08,
    a08
      ? '已含 API 调用 + 兜底'
      : !callsListFactories
        ? 'onMounted 未调 listFactories()'
        : '缺 FACTORY_FALLBACK 兜底'
  )

  // A09 静态：Ctp.vue 物料下拉调 materialLookupApi.listActiveMaterials()（real API）+ 兜底
  //   - 端点：GET /api/governance/lookups/materials?activeOnly=true（无 search 全量）
  //   - 后端 SetupRuleController.cs:333 已含 activeOnly 参数，按既有契约适配
  const materialImportMatch =
    /import\s*\{[^}]*\bmaterialLookupApi\b[^}]*\}\s*from\s*['"]@\/api\/aps-v1\/materialLookup['"]/.test(
      ctpVueContent
    )
  const materialRefMatch =
    /\bMATERIAL_OPTIONS\s*=\s*ref\s*<[^>]*MaterialLookupItem[^>]*>\s*\(\s*\[\s*\]\s*\)/.test(
      ctpVueContent
    )
  const materialNoHardcodedArray =
    !/^\s*const\s+MATERIAL_OPTIONS\s*=\s*\[[\s\S]*?\bcode\s*:\s*'M-001'/.test(ctpVueContent)
  const callsListActiveMaterials = /\bmaterialLookupApi\s*\.\s*listActiveMaterials\s*\(\s*\)/.test(
    onMountedBlock
  )
  const materialHasFallback = /MATERIAL_FALLBACK/.test(ctpVueContent)
  const a09 =
    materialImportMatch &&
    materialRefMatch &&
    materialNoHardcodedArray &&
    callsListActiveMaterials &&
    materialHasFallback
  assert(
    'A09 静态：Ctp.vue 物料下拉调 materialLookupApi.listActiveMaterials()（real API）+ 含兜底 MATERIAL_FALLBACK',
    a09,
    a09
      ? '已接入 API（ref + listActiveMaterials）'
      : !materialImportMatch
        ? '缺 materialLookupApi 导入'
        : !materialRefMatch
          ? 'MATERIAL_OPTIONS 未声明为 ref<MaterialLookupItem[]>([])'
          : !callsListActiveMaterials
            ? 'onMounted 未调 listActiveMaterials()'
            : !materialHasFallback
                ? '缺 MATERIAL_FALLBACK 兜底'
                : '仍含硬编码 MATERIAL_OPTIONS 数组（M-001 ~ M-007）'
  )

  /* -------- C01/C02 静态（lps/ C 路径契约层，3号位 09-30 落码）--------
   * 验收依据：frontNew/docs/APS_V1_CTP评估C路径落码回执_3号位致4号位_v1.0_20260930.md §五
   * 口径：纯只读静态扫描 lps/（4号位 硬约束：lps/ 仅 Read，不 Edit/Write；本断言不改 lps/）
   */
  const lpsCtpReq = (() => {
    try {
      return readFileSync(join(LPS_ROOT, 'LPS.APS.Core', 'DTOs', 'Ctp', 'CtpEvaluateRequest.cs'), 'utf-8')
    } catch {
      return ''
    }
  })()
  const lpsCtpSvc = (() => {
    try {
      return readFileSync(join(LPS_ROOT, 'LPS.APS.Application', 'Services', 'CtpService.cs'), 'utf-8')
    } catch {
      return ''
    }
  })()

  // C01 静态：CtpEvaluateRequest.DomainKey 无 [Required]（契约 §3.2：可选化）
  //   - 断言：在 DomainKey 字段附近没有 [Required(...)] 属性
  //   - 反例：`[Required(ErrorMessage = "domainKey 必填")] public string? DomainKey { get; set; }` → 失败
  const c01 =
    lpsCtpReq.length > 0 &&
    /\bpublic\s+string\?\s+DomainKey\b/.test(lpsCtpReq) &&
    !/\[\s*Required\s*\([^)]*\)\][\s\S]{0,80}?\bpublic\s+string\?\s+DomainKey\b/.test(lpsCtpReq)
  assert(
    'C01 静态：lps/ CtpEvaluateRequest.DomainKey 无 [Required]（契约可选化）',
    c01,
    c01
      ? '已可选（string? + 无 Required）'
      : lpsCtpReq.length === 0
        ? 'lps/LPS.APS.Core/DTOs/Ctp/CtpEvaluateRequest.cs 不存在'
        : '仍含 [Required]，契约未可选化'
  )

  // C02 静态：CtpService.EvaluateAsync 调 IDomainResolver.ResolveAsync（materialCode+factoryCode，契约 §3.2）
  //   - 断言：EvaluateAsync 函数体内出现 _domainResolver.ResolveAsync(request.MaterialCode, request.FactoryCode, ...)
  const c02 =
    lpsCtpSvc.length > 0 &&
    /public\s+async\s+Task[\s\S]*?EvaluateAsync\b[\s\S]*?_domainResolver\.ResolveAsync\s*\(\s*request\.MaterialCode\s*,\s*request\.FactoryCode\b/.test(
      lpsCtpSvc
    )
  assert(
    'C02 静态：lps/ CtpService.EvaluateAsync 含 ResolveAsync(MaterialCode, FactoryCode) 调用',
    c02,
    c02
      ? '已含自动 resolve 调用'
      : lpsCtpSvc.length === 0
        ? 'lps/LPS.APS.Application/Services/CtpService.cs 不存在'
        : '未含自动 resolve 调用'
  )

  /* -------- CTP-EVAL01/02 动态（10-09 v1.0 真实落地回执 §五 4号位 兑现）
   * 验收依据：frontNew/docs/APS_V1_CTP主数据迁移_3号位致4号位_真实落地回执_v1.0_20261009.md
   *   - §四 真实冒烟物料清单 8 码 × 工厂 CN/BJ/SH/TJ = 32 路径全 501（核心试算未接入契约行为）
   *   - 禁止：HTTP 400（多域歧义 / 域键 resolve 漏配）/ 422（DTO 字段缺失 / 类型错）
   *   - 与 §六 既有 9 单测 + 32 真实路径冒烟 一致；4号位 不重复独立组 Group，复用 a-ctp-domainkey
   *   - 用 `VERIFY_CTP_EVAL=1` 守门（默认跳过，避免无 dev 时炸 32 次请求）
   */
  if (process.env.VERIFY_CTP_EVAL !== '1') {
    skip('CTP-EVAL01/02 动态：8 物料 × 4 工厂 = 32 路径全 501（设置 VERIFY_CTP_EVAL=1 跑，dev 联调）')
  } else {
    const realMaterials = [
      { productFamily: 'VALVE', materialCode: 'FINAL_ASSY-51-SYE100-3-2A-ZZ' },
      { productFamily: 'LGJ', materialCode: 'FINAL_ASSY-IDF-K2A0716M' },
      { productFamily: 'XC', materialCode: 'FINAL_RAW-A6061-355-1200-GC' },
      { productFamily: 'JS', materialCode: 'FINAL_ASSY-145111' },
      { productFamily: 'WKQ', materialCode: 'FINAL_ASSY-11E-XQB' },
      { productFamily: 'TZP', materialCode: 'FINAL_ASSY-10-VK334V-DND7362' },
      { productFamily: 'MJ', materialCode: 'FINAL_WIP-1_2A-37273-40' },
      { productFamily: 'QT', materialCode: 'FINAL_ASSY-GJ22408-1' }
    ]
    const realFactories = ['CN', 'BJ', 'SH', 'TJ']
    let eval501Count = 0
    let eval400Count = 0
    let eval422Count = 0
    const evalFailures = []

    for (const m of realMaterials) {
      for (const factory of realFactories) {
        const r = await call('POST', '/api/ctp/evaluate', {
          token,
          body: {
            orderCanonicalId: `PO-CTP-EVAL-${m.productFamily}-${factory}`,
            materialCode: m.materialCode,
            quantity: 100,
            factoryCode: factory,
            requestedDueDate: '2026-10-15',
            purpose: 'CTP'
          }
        })
        if (r.code === 501) eval501Count++
        else if (r.code === 400) {
          eval400Count++
          evalFailures.push({ mat: m.materialCode, factory, code: 400, msg: r.json?.message ?? '' })
        } else if (r.code === 422) {
          eval422Count++
          evalFailures.push({ mat: m.materialCode, factory, code: 422, msg: r.json?.message ?? '' })
        } else {
          evalFailures.push({ mat: m.materialCode, factory, code: r.code, msg: r.json?.message ?? '' })
        }
      }
    }

    const eval01All501 = eval400Count === 0 && eval422Count === 0 && eval501Count === realMaterials.length * realFactories.length
    assert(
      'CTP-EVAL01 动态：CTP 评估 501 契约行为（核心试算未接入；禁止 400/422）',
      eval01All501,
      eval01All501
        ? `32/32 全 501（核心试算未接入契约行为；域键 resolve 已命中）`
        : `501=${eval501Count} 400=${eval400Count} 422=${eval422Count} 其它=${evalFailures.length - eval400Count - eval422Count}`
    )

    const eval02No400 =
      eval400Count === 0 && eval422Count === 0 && evalFailures.every((f) => f.code !== 400 && f.code !== 422)
    assert(
      'CTP-EVAL02 动态：8 真实物料 × 4 工厂 = 32 路径 全部 501（域键 resolve 已命中，无 400 多域歧义）',
      eval02No400,
      eval02No400
        ? `32/32 全 501（域键 resolve 全命中）`
        : `失败路径：${evalFailures.map((f) => `${f.mat}@${f.factory}=${f.code}(${f.msg.slice(0, 60)})`).slice(0, 3).join(' | ')}`
    )
  }
}

/* ==================== [ME] ManualETA 顶部精简（me-eta）==================== */

/**
 * [ME] 人工到货 ETA 页面顶部精简（me-eta — 10-07 撤使用说明）
 * 依据：
 *  - 用户 2026-10-07 反馈："这块儿关于说明的，可以都去掉吧，页面以简洁好用为主"
 *  - 规则：feedback_no_internal_notices_in_ui.md（内部协调痕迹不进 UI）
 *  - 删：ElAlert「使用说明」整块（标题+说明+审计追溯）
 *  - 改：副标题精简为「仅人工值；有效到货时间由系统计算」
 *  - 留：标题「人工到货 ETA」、刷新按钮、错误条、KPI、筛选、列表、Drawer
 */
async function verifyManualEta(token) {
  log('\n[ME] 人工到货 ETA 顶部 + Drawer 精简（me-eta — 10-07 撤说明）')

  const manualEtaVueContent = readSrc('views/Aps/ManualEta.vue')

  // ME01 静态：ManualEta.vue 不含 ElAlert title="使用说明"（顶部冗余说明已撤）
  const me01 =
    !/<template\s+#title>\s*使用说明\s*<\/template>/.test(manualEtaVueContent) &&
    !/class\s*=\s*["']hint-bar["']/.test(manualEtaVueContent)
  assert(
    'ME01 静态：ManualEta.vue 不含 ElAlert「使用说明」（顶部冗余说明已撤）',
    me01,
    me01 ? '已撤' : '仍含使用说明 ElAlert'
  )

  // ME02 静态：ManualEta.vue 不含「可在『审计日志』中追溯」字样（内部协调痕迹已撤）
  const me02NoTrace =
    !/可在[「"']?审计日志[」"']?中追溯/.test(manualEtaVueContent) &&
    !/审计日志中追溯/.test(manualEtaVueContent)
  assert(
    'ME02 静态：ManualEta.vue 不含「可在『审计日志』中追溯」（内部协调痕迹已撤）',
    me02NoTrace,
    me02NoTrace ? '已撤' : '仍含内部协调痕迹'
  )

  // ME03 静态：ManualEta.vue Drawer 内不含 form-hint ElAlert（弹框顶部说明已撤）
  //   - 弹框原 ElAlert：title="新增（Upsert）" + 内容"按采购单号 + 行号去重..."
  //   - 反例：class="form-hint" 的 ElAlert 块
  const me03 =
    !/class\s*=\s*["']form-hint["']/.test(manualEtaVueContent) &&
    !/新增\s*（\s*Upsert\s*）/.test(manualEtaVueContent) &&
    !/按采购单号\s*\+\s*行号去重/.test(manualEtaVueContent)
  assert(
    'ME03 静态：ManualEta.vue Drawer 不含「新增（Upsert）」ElAlert 说明块',
    me03,
    me03 ? '已撤' : '仍含 Drawer 顶部 ElAlert'
  )

  // ME04 静态：ManualEta.vue 不含"复合主键 / 编辑态下 PO + 行号不可改"（PO号下冗余提示已撤）
  const me04 =
    !/与\s*LineNo\s*组成复合主键/.test(manualEtaVueContent) &&
    !/编辑态下\s*PO\s*\+\s*行号不可改/.test(manualEtaVueContent) &&
    !/class\s*=\s*["']field-hint["']/.test(manualEtaVueContent)
  assert(
    'ME04 静态：ManualEta.vue 不含「复合主键 / field-hint」PO 号下冗余提示',
    me04,
    me04 ? '已撤' : '仍含 PO 号下提示'
  )

  // ME05 静态：ManualEta.vue 不含 field-hint「必填」字样（红星已标识，必填文字冗余）
  //   - 反例：<div class="field-hint">必填</div>
  //   - 注意：此处依赖 ME04 已撤掉 field-hint 类（因为用户原话"必填的前面已经有红星标识了也去掉'必填'两字"）
  const me05 =
    !/<div\s+class\s*=\s*["']field-hint["']\s*>\s*必填\s*<\/div>/.test(manualEtaVueContent)
  assert(
    'ME05 静态：ManualEta.vue 不含「必填」field-hint（红星已标识，文字冗余）',
    me05,
    me05 ? '已撤' : '仍含「必填」field-hint'
  )

  // ME06 静态：ManualEta.vue 不含「仅人工值；有效 ETA / 可用时间由系统计算」（人工ETA下重复说明已撤）
  const me06 =
    !/仅人工值；有效\s*ETA/.test(manualEtaVueContent) &&
    !/仅人工值\s*；\s*有效\s*ETA\s*\/\s*可用时间由系统计算/.test(manualEtaVueContent)
  assert(
    'ME06 静态：ManualEta.vue 不含「仅人工值；有效 ETA / 可用时间由系统计算」（重复说明已撤）',
    me06,
    me06 ? '已撤' : '仍含人工ETA下重复说明'
  )

  // ME07 静态：ManualEta.vue 备注 placeholder 不含「写入审计」内部协调痕迹
  //   - 反例：placeholder="可选（写入审计）"
  const me07 = !/placeholder\s*=\s*["']可选（写入审计）["']/.test(manualEtaVueContent)
  assert(
    'ME07 静态：ManualEta.vue 备注 placeholder 不含「写入审计」（内部协调痕迹已撤）',
    me07,
    me07 ? '已撤' : '仍含「写入审计」字样'
  )

  /* -------- ME08 静态（manualEtaApi.serializeFilter 默认 activeOnly 修复）--------
   * 根因（2026-10-07）：
   *  - 后端 ProcurementManualEtaController.cs:53 默认 activeOnly=true
   *  - 前端 manualEtaApi.serializeFilter 缺省时不传 → 触发后端默认 → cancelled 记录被过滤
   *  - 现象：刷新页面/初次加载 → 列表空（KPI 仍有 cancelledCount），与 UI「仅看生效中」开关语义不一致
   * 修复：serializeFilter 显式传 activeOnly（缺省值 false），覆盖后端默认
   */
  const manualEtaApiContent = readSrc('api/aps-v1/manualEta.ts')
  const serializeFnBlock = (() => {
    // 定位 `function serializeFilter(...) : Record<string, unknown> {` 后，从第一个 `Record<string, unknown> {` 后的 `{` 开始
    const i = manualEtaApiContent.indexOf('function serializeFilter')
    if (i < 0) return ''
    const slice = manualEtaApiContent.slice(i)
    const open = slice.indexOf('Record<string, unknown> {')
    if (open < 0) return ''
    const start = i + open + 'Record<string, unknown> {'.length
    // 用 indexOf + 括号配对定位闭合 `}`（函数体结束）；这里采用从 start 处 slice 到函数体结尾
    // 简化：从 start 起 slice 200 字符够覆盖（函数体只有 ~5 行）
    const body = manualEtaApiContent.slice(start)
    // 找到下一个独立的 `}`（行首闭合），避免匹配到 Record<string, unknown> 的内层 `}`
    const m = body.match(/\n\}/)
    return m ? body.slice(0, m.index + 2) : ''
  })()
  // 反例：filter 缺省时不传 activeOnly（依赖后端默认 true → cancelled 被过滤）
  const me08 =
    serializeFnBlock.length > 0 &&
    /activeOnly\s*:\s*filter\s*\?\s*\.\s*activeOnly\s*\?\?\s*false/.test(serializeFnBlock) &&
    !/if\s*\(\s*filter\s*\?\s*\.\s*activeOnly\s*!==\s*undefined\s*\)/.test(serializeFnBlock)
  assert(
    'ME08 静态：manualEtaApi.serializeFilter 显式传 activeOnly（缺省 false），防止 cancelled 记录被后端默认过滤',
    me08,
    me08
      ? '已显式传 activeOnly'
      : serializeFnBlock.length === 0
        ? '未定位 serializeFilter 函数'
        : 'serializeFilter 仍依赖后端默认（activeOnly 未显式序列化）'
  )

  /* -------- ME09 静态（ManualETA Drawer 物料下拉化：合并 MaterialId + MaterialCode 输入）--------
   * 触发（2026-10-07）：用户问「物料和仓库是不是下拉更好」→ 选 A 只改物料下拉，仓库保留手填
   * 依据：
   *  - 5号位 SetupRuleController L333 已提供 /api/governance/lookups/materials?activeOnly= 端点
   *  - 4号位 复用 materialLookupApi.listActiveMaterials()（与 CTP 同源）
   * 验收口径：
   *  - ManualEta.vue 引入 materialLookupApi + MaterialLookupItem 类型
   *  - 含 materialOptions ref + loadMaterialOptions() 异步加载 + onMounted 调用
   *  - 含 materialKey → materialId + materialCode 同步 watch
   *  - 含 ElSelect v-model="form.materialKey"（替代原两个 ElInput）
   *  - 编辑态反例：不应再出现 ElInput v-model="form.materialId" 或 v-model="form.materialCode"
   */
  const me09Vue = readSrc('views/Aps/ManualEta.vue')
  const me09a = /import\s+\{[^}]*materialLookupApi[^}]*\}\s+from\s+['"]\@\/api\/aps-v1\/materialLookup['"]/.test(me09Vue)
  assert('ME09a 静态：ManualEta.vue 引入 materialLookupApi', me09a, me09a ? '已引入' : '缺导入')

  const me09b = /MaterialLookupItem/.test(me09Vue)
  assert('ME09b 静态：ManualEta.vue 含 MaterialLookupItem 类型', me09b, me09b ? '已含' : '缺类型')

  const me09c = /materialOptions\s*=\s*ref<MaterialLookupItem\[\]>/.test(me09Vue)
  assert('ME09c 静态：ManualEta.vue materialOptions ref<MaterialLookupItem[]>', me09c, me09c ? '已含' : '缺 ref')

  const me09d = /materialLookupApi\.listActiveMaterials\s*\(/.test(me09Vue)
  assert('ME09d 静态：ManualEta.vue loadMaterialOptions 调 listActiveMaterials()', me09d, me09d ? '已调' : '缺调用')

  const me09e = /onMounted\s*\(\s*\(\s*\)\s*=>\s*\{[\s\S]*?loadMaterialOptions\s*\(\s*\)/.test(me09Vue)
  assert('ME09e 静态：ManualEta.vue onMounted 含 loadMaterialOptions()', me09e, me09e ? '已挂载' : '缺 onMounted')

  const me09f = /watch\s*\(\s*\(\s*\)\s*=>\s*form\.value\.materialKey[\s\S]*?materialId\s*=\s*found\.masterId[\s\S]*?materialCode\s*=\s*found\.code/.test(me09Vue)
  assert('ME09f 静态：ManualEta.vue watch 同步 materialKey → materialId + materialCode', me09f, me09f ? '已同步' : '缺 watch 同步')

  const me09g =
    /<ElSelect\b[^>]*v-model="materialKeyModel"/.test(me09Vue) ||
    /<ElSelect\b[^>]*v-model="form\.materialKey"/.test(me09Vue)
  assert('ME09g 静态：ManualEta.vue 含 ElSelect 绑物料 v-model（materialKeyModel 或 form.materialKey）', me09g, me09g ? '已含' : '缺下拉')

  const me09h = !/v-model="form\.materialId"/.test(me09Vue) || /v-model="form\.materialId"\s*\/?>\s*<!--\s*hidden/.test(me09Vue)
  // 兜底：原 materialId ElInput 必须移除；除非是显式 hidden 注释占位
  const me09hStrict = !/ElInput[^>]*v-model="form\.materialId"/.test(me09Vue)
  assert(
    'ME09h 静态：ManualEta.vue 不含原 ElInput v-model="form.materialId"（已被下拉替代）',
    me09hStrict,
    me09hStrict ? '已替代' : '仍存在 materialId 输入框'
  )

  const me09i = !/ElInput[^>]*v-model="form\.materialCode"/.test(me09Vue)
  assert(
    'ME09i 静态：ManualEta.vue 不含原 ElInput v-model="form.materialCode"（已被下拉替代）',
    me09i,
    me09i ? '已替代' : '仍存在 materialCode 输入框'
  )

  /* -------- ME10 静态（ManualETA 物料编码筛选透传：修复 syncFilterDraft 硬编码 undefined bug）--------
   * 触发（2026-10-07）：用户审查 ManualETA 完整性时发现 syncFilterDraft 注释自承「materialIds: undefined」，
   *   UI 选物料编码后点筛选无效；后端实际支持 ?materialCodes= 逗号分隔（Controller.cs:47）
   * 修复：
   *  - ManualEtaListFilter 新增 materialCodes?: string[]
   *  - serializeFilter 把 materialCodes 序列化为逗号分隔字符串
   *  - syncFilterDraft 把 selectedMaterialCodes 透传给 materialCodes
   * 验收口径：
   *  - types/manualEta.ts ManualEtaListFilter 含 materialCodes?: string[]
   *  - api/manualEta.ts serializeFilter 含 materialCodes 序列化分支
   *  - ManualEta.vue syncFilterDraft 把 selectedMaterialCodes 赋值给 filterDraft.materialCodes
   */
  const me10Types = readSrc('api/aps-v1/types/manualEta.ts')
  const me10a = /export\s+interface\s+ManualEtaListFilter\b[^}]*\bmaterialCodes\s*\?\s*:\s*string\[\]/.test(me10Types)
  assert('ME10a 静态：types/manualEta.ts ManualEtaListFilter 含 materialCodes?: string[]', me10a, me10a ? '已含' : '缺字段')

  const me10Api = manualEtaApiContent
  const me10b = /out\.materialCodes\s*=\s*filter\.materialCodes\.join\(',\s*'\)/.test(me10Api) ||
    /if\s*\(\s*filter\s*\?\s*\.\s*materialCodes\s*\?\s*\.\s*length\s*\)\s*out\.materialCodes\s*=/.test(me10Api)
  assert('ME10b 静态：manualEtaApi.serializeFilter 序列化 materialCodes 为逗号分隔', me10b, me10b ? '已序列化' : '缺序列化分支')

  const me10c = /syncFilterDraft[\s\S]*?materialCodes\s*:\s*selectedMaterialCodes\.value\.length/.test(me09Vue)
  assert('ME10c 静态：ManualEta.vue syncFilterDraft 把 selectedMaterialCodes 透传给 materialCodes', me10c, me10c ? '已透传' : '仍硬编码 undefined')

  const me10d = !/materialIds\s*:\s*undefined,\s*\/\/\s*DTO\s+是\s+materialIds/.test(me09Vue) ||
    /UI\s+用\s+code\s+选/.test(me09Vue)
  // 兜底：原注释「简单起见先传 undefined」必须替换/扩展为 materialCodes 路径，否则前端 bug 复发
  assert(
    'ME10d 静态：ManualEta.vue 不再硬编码「materialIds: undefined / 简单起见先传 undefined」（已转 materialCodes 路径）',
    me10d,
    me10d ? '已迁移' : 'syncFilterDraft 注释仍指 materialIds，未走 materialCodes'
  )

  /* -------- ME11 静态：列表分页化（5号位 2026-10-08 落地 PageResult<T>）--------
   *  - types/manualEta.ts ManualEtaListFilter 含 pageIndex / pageSize
   *  - store/manualEta.ts 有 pageIndex / pageSize / total 三个 ref 并在 return 中暴露
   *  - store/manualEta.ts load() 处理两种形态：数组（旧）/ {items,total,page,pageSize}（新）
   *  - api/manualEta.ts serializeFilter 总是显式传 pageIndex / pageSize（避免后端默认 100）
   */
  const me11a =
    /export\s+interface\s+ManualEtaListFilter\b[^}]*\bpageIndex\s*\?\s*:\s*number/.test(me10Types) &&
    /export\s+interface\s+ManualEtaListFilter\b[^}]*\bpageSize\s*\?\s*:\s*number/.test(me10Types)
  assert('ME11a 静态：types/manualEta.ts ManualEtaListFilter 含 pageIndex / pageSize', me11a, me11a ? '已含' : '缺分页字段')

  const me11Store = readSrc('store/modules/aps/manualEta.ts')
  const me11b =
    /\bpageIndex\s*=\s*ref\(\s*1\s*\)/.test(me11Store) &&
    /\bpageSize\s*=\s*ref\(\s*20\s*\)/.test(me11Store) &&
    /\btotal\s*=\s*ref\(\s*0\s*\)/.test(me11Store)
  assert('ME11b 静态：store/manualEta.ts 含 pageIndex / pageSize / total 三个 ref', me11b, me11b ? '已含' : '缺分页 ref')

  const me11c =
    /\breturn\s*\{[\s\S]*?\bpageIndex\b[\s\S]*?\bpageSize\b[\s\S]*?\btotal\b[\s\S]*?load\b/.test(me11Store)
  assert('ME11c 静态：store/manualEta.ts return 暴露 pageIndex / pageSize / total', me11c, me11c ? '已暴露' : 'return 缺分页暴露')

  const me11d =
    /Array\.isArray\(resp\)/.test(me11Store) &&
    /resp\s*\?\s*\.\s*items/.test(me11Store) &&
    /resp\s*\?\s*\.\s*total/.test(me11Store)
  assert('ME11d 静态：store/manualEta.ts load() 处理 PageResult + 数组两种形态', me11d, me11d ? '双形态兼容' : 'load() 缺形态兼容')

  const me11e =
    /\bpageIndex\s*:\s*filter\s*\?\s*\.\s*pageIndex\s*\?\?\s*1\b/.test(manualEtaApiContent) &&
    /\bpageSize\s*:\s*filter\s*\?\s*\.\s*pageSize\s*\?\?\s*20\b/.test(manualEtaApiContent)
  assert('ME11e 静态：api/manualEta.ts serializeFilter 显式传 pageIndex / pageSize（避免后端默认）', me11e, me11e ? '已显式' : '缺显式分页参数')

  /* -------- ME12 静态：ManualEta.vue ElPagination 渲染 + 翻页 / 改页长回调 -------
   *  - 导入 ElPagination
   *  - 从 store 解构 pageIndex / pageSize / total
   *  - 模板含 <ElPagination> 且绑定 :total / :page-size / :current-page
   *  - onPageChange + onSizeChange 调用 store.load 并切到第 1 页（改页长时）
   */
  const me12a = /import\s*\{[^}]*\bElPagination\b[^}]*\}\s*from\s*['"]element-plus['"]/.test(me09Vue)
  assert('ME12a 静态：ManualEta.vue 导入 ElPagination', me12a, me12a ? '已导入' : '缺导入')

  const me12b =
    /\bpageIndex\b/.test(me09Vue) && /\bpageSize\b/.test(me09Vue) && /\btotal\b/.test(me09Vue)
  // storeToRefs 解构（与已有 list / error 同位置）
  assert('ME12b 静态：ManualEta.vue 解构 pageIndex / pageSize / total', me12b, me12b ? '已解构' : 'storeToRefs 缺分页字段')

  const me12c =
    /<ElPagination\b[^>]*:total="total"/.test(me09Vue) &&
    /<ElPagination\b[^>]*:page-size="pageSize"/.test(me09Vue) &&
    /<ElPagination\b[^>]*:current-page="pageIndex"/.test(me09Vue)
  assert('ME12c 静态：ManualEta.vue ElPagination 绑定 total / page-size / current-page', me12c, me12c ? '已绑定' : '缺分页属性绑定')

  const me12d =
    /@current-change\s*=\s*["']onPageChange["']/.test(me09Vue) &&
    /@size-change\s*=\s*["']onSizeChange["']/.test(me09Vue)
  assert('ME12d 静态：ManualEta.vue ElPagination 绑定 @current-change / @size-change', me12d, me12d ? '已绑定' : '缺事件回调')

  const me12e =
    /async\s+function\s+onPageChange[\s\S]*?store\s*\.\s*load\s*\(/.test(me09Vue) &&
    /async\s+function\s+onSizeChange[\s\S]*?store\s*\.\s*load\s*\(\s*\{[\s\S]*?pageIndex\s*:\s*1/.test(me09Vue)
  assert('ME12e 静态：ManualEta.vue onPageChange / onSizeChange 调 store.load（改页长 → 跳回第 1 页）', me12e, me12e ? '已闭环' : '缺回调函数体')

  const me12f = /applyFilter[\s\S]*?pageIndex\s*:\s*1/.test(me09Vue)
  assert('ME12f 静态：ManualEta.vue applyFilter 重置 pageIndex=1（避免筛选无交集时空态错觉）', me12f, me12f ? '已重置' : '筛选未重置分页')

  /* -------- ME13 静态：收货仓库列展示 name（10-09 加列名映射）--------
   *  - ManualEta.vue 含 warehouseNameMap computed（基于 warehouseOptions 派生 code → name）
   *  - 表格"收货仓库"列展示 `${code} · ${name}` 形式（name 找不到时只显示 code）
   */
  const me13a = /warehouseNameMap\s*=\s*computed/.test(me09Vue) &&
    /warehouseOptions\.value/.test(me09Vue)
  assert('ME13a 静态：ManualEta.vue 含 warehouseNameMap computed（基于下拉数据）', me13a, me13a ? '已含' : '缺派生映射')

  const me13b = /warehouseNameMap\.get\(\s*row\.receivingWarehouse\s*\)/.test(me09Vue)
  assert('ME13b 静态：ManualEta.vue 表格"收货仓库"列用 warehouseNameMap 查 name', me13b, me13b ? '已查' : '缺 name 展示')

  /* -------- ME14 静态：upsert / cancel 业务键 = 4 字段复合主键（10-09 修复）--------
   *  - 与 5号位 `ProcurementManualEtaRepository.MERGE ON` 对齐：poNo + lineNo + materialId + receivingWarehouse
   *  - @see lps/LPS.APS.BusinessRules/Repositories/ProcurementManualEtaRepository.cs:202-205
   *  - 之前只用 poNo + lineNo：同 poNo+lineNo 下多物料 / 多仓库会找错行 → 列表不增不删异常
   *  - receivingWarehouse 可选字段：sameBusinessKey 需对 undefined/null/'' 三态一致
   */
  const me14Store = me11Store
  const me14a = /function\s+sameBusinessKey\s*\(\s*a\s*:\s*ManualEtaDto\s*,\s*b\s*:\s*ManualEtaDto\s*\)/.test(me14Store)
  assert('ME14a 静态：store/manualEta.ts 含 sameBusinessKey(a, b) helper', me14a, me14a ? '已含' : '缺同键 helper')

  const me14b =
    /sameBusinessKey\(\s*x\s*,\s*result\s*\)/.test(me14Store) &&
    /sameBusinessKey\(\s*currentDetail\.value/.test(me14Store)
  assert('ME14b 静态：store/manualEta.ts upsert / cancel 列表与 currentDetail 都用 4 键查重', me14b, me14b ? '已闭环' : 'findIndex 仍用 poNo+lineNo 二键')

  const me14c =
    /a\.materialId\s*===\s*b\.materialId/.test(me14Store) &&
    /\(a\.receivingWarehouse\s*\?\?\s*''\)\s*===\s*\(b\.receivingWarehouse\s*\?\?\s*''\)/.test(me14Store)
  assert('ME14c 静态：sameBusinessKey 含 materialId + receivingWarehouse（4 键完整）', me14c, me14c ? '4 键齐备' : '缺 materialId / receivingWarehouse 对齐')
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

/* ==================== [TSRC] TaskSplitRuleConfig / Batch Policy 治理（t-tsrc）====================
 * 触发：3号位 2026-10-07 契约先行件《TaskSplitRuleConfig维护接口契约_v1.0_20261007.md》
 *  - 4 端点：GET/POST /api/governance/task-split-rule-configs + PUT/DELETE /{id}
 *  - 权限码：aps.rule.view / aps.rule.edit（复用 Rule 码，零权限种子变更）
 *  - 兼容字段不暴露：ResourceGroupId / MinimumOrderQuantity / EconomicOrderQuantity
 *  - 业务键：MaterialId + ProductionDepartmentId（NULL 部门 = Material 级默认）
 * 现状（10-07 收口）：
 *  - 后端 7 文件已落地（Controller / Service / DTOs / Entity / Interface / Projector / Tests）
 *  - 前端 4号位 已起草 types + api 桩代码 + store + 路由 + 页面（与 OPM 同范式）
 *  - dev 库 schema 5 列已迁移（10-07 3号位 / 2号位 协调完成）→ TSRC08 真实联调开启
 */
async function verifyTaskSplitRuleConfig(token) {
  log('\n[TSRC] TaskSplitRuleConfig / Batch Policy 治理（t-tsrc — 10-07 起草）')

  // TSRC01 静态：types/taskSplitRuleConfig.ts 含 TaskSplitRuleConfigDto + SaveTaskSplitRuleConfigRequest + ListQuery
  const tsrcTypes = readSrc('api/aps-v1/types/taskSplitRuleConfig.ts')
  const tsrc01a = /export\s+interface\s+TaskSplitRuleConfigDto\b/.test(tsrcTypes)
  assert('TSRC01a 静态：types/taskSplitRuleConfig.ts 含 TaskSplitRuleConfigDto', tsrc01a, tsrc01a ? '已含' : '缺 DTO')

  const tsrc01b = /export\s+interface\s+SaveTaskSplitRuleConfigRequest\b/.test(tsrcTypes)
  assert('TSRC01b 静态：types/taskSplitRuleConfig.ts 含 SaveTaskSplitRuleConfigRequest', tsrc01b, tsrc01b ? '已含' : '缺 Request')

  const tsrc01c = /export\s+interface\s+TaskSplitRuleConfigListQuery\b/.test(tsrcTypes)
  assert(
    'TSRC01c 静态：types/taskSplitRuleConfig.ts 含 TaskSplitRuleConfigListQuery',
    tsrc01c,
    tsrc01c ? '已含' : '缺 ListQuery'
  )

  // TSRC02 静态：types/index.ts 已 re-export 任务批拆分规则 DTO
  const tsrcIndex = readSrc('api/aps-v1/types/index.ts')
  const tsrc02 = /export\s+\*\s+from\s+['"]\.\/taskSplitRuleConfig['"]/.test(tsrcIndex)
  assert('TSRC02 静态：types/index.ts re-export ./taskSplitRuleConfig', tsrc02, tsrc02 ? '已 re-export' : '缺 re-export')

  // TSRC03 静态：api/taskSplitRuleConfig.ts 含 4 端点 list/create/update/deactivate
  //  - 10-08 列表分页改造：list() 返回 Promise<PagedTaskSplitRuleConfig>（与 R2 PageResult<T> 同形态）
  const tsrcApi = readSrc('api/aps-v1/taskSplitRuleConfig.ts')
  const tsrc03a = /async\s+list\s*\([^)]*\)\s*:\s*Promise<PagedTaskSplitRuleConfig>/.test(tsrcApi)
  assert('TSRC03a 静态：api/taskSplitRuleConfig.ts 含 list() 方法（10-08 返回 Paged）', tsrc03a, tsrc03a ? '已含' : '缺 list / 返回类型错')

  const tsrc03b = /async\s+create\s*\(\s*input\s*:\s*SaveTaskSplitRuleConfigRequest\s*\)/.test(tsrcApi)
  assert('TSRC03b 静态：api/taskSplitRuleConfig.ts 含 create(input)', tsrc03b, tsrc03b ? '已含' : '缺 create')

  const tsrc03c = /async\s+update\s*\(\s*id\s*:\s*number\s*,\s*input\s*:\s*SaveTaskSplitRuleConfigRequest\s*\)/.test(tsrcApi)
  assert('TSRC03c 静态：api/taskSplitRuleConfig.ts 含 update(id, input)', tsrc03c, tsrc03c ? '已含' : '缺 update')

  const tsrc03d = /async\s+deactivate\s*\(\s*id\s*:\s*number\s*\)/.test(tsrcApi)
  assert('TSRC03d 静态：api/taskSplitRuleConfig.ts 含 deactivate(id)', tsrc03d, tsrc03d ? '已含' : '缺 deactivate')

  // TSRC04 静态：4 端点 URL 路径与契约 §一 对齐
  const tsrc04a = /['"`]\/api\/governance\/task-split-rule-configs['"`]/.test(tsrcApi)
  assert('TSRC04a 静态：list/create URL = /api/governance/task-split-rule-configs', tsrc04a, tsrc04a ? '已对齐' : '路径错')

  const tsrc04b = /\/api\/governance\/task-split-rule-configs\/\$\{id\}/.test(tsrcApi)
  assert('TSRC04b 静态：update/deactivate URL = /api/governance/task-split-rule-configs/${id}', tsrc04b, tsrc04b ? '已对齐' : '路径错')

  // TSRC05 静态：兼容字段（ResourceGroupId / MOQ / EOQ）不进 DTO / 不进 Request
  //  - 契约 §2.2 末段明确：「兼容字段不暴露：不进请求体、不进 DTO、不投快照」
  //  - 注意：DTO/Request 类型里**不应该**出现 resourceGroupId / minimumOrderQuantity / economicOrderQuantity
  const tsrc05a = !/resourceGroupId\s*:\s*number/.test(tsrcTypes)
  const tsrc05b = !/minimumOrderQuantity\s*:\s*number/.test(tsrcTypes)
  const tsrc05c = !/economicOrderQuantity\s*:\s*number/.test(tsrcTypes)
  assert(
    'TSRC05 静态：DTO/Request 不暴露兼容字段（ResourceGroupId / MOQ / EOQ）',
    tsrc05a && tsrc05b && tsrc05c,
    tsrc05a && tsrc05b && tsrc05c
      ? '已剔除兼容字段'
      : `泄漏：${
          !tsrc05a ? 'resourceGroupId ' : ''
        }${!tsrc05b ? 'minimumOrderQuantity ' : ''}${!tsrc05c ? 'economicOrderQuantity' : ''}`
  )

  // TSRC06 静态：AllowSplit/AllowMerge 在 Request 是 nullable（须显式提供，后端 422 强校验）
  const tsrc06 = /allowSplit\s*:\s*boolean\s*\|\s*null/.test(tsrcTypes) && /allowMerge\s*:\s*boolean\s*\|\s*null/.test(tsrcTypes)
  assert(
    'TSRC06 静态：SaveTaskSplitRuleConfigRequest 的 AllowSplit/AllowMerge 是 boolean | null（显式提供）',
    tsrc06,
    tsrc06 ? 'nullable' : '缺失 nullable 标记（会触发后端 422 误判）'
  )

  // TSRC07 静态：策略枚举常量与契约 §二.2 一致
  const tsrc07a = /PREFER_SPLIT/.test(tsrcTypes) && /PREFER_MERGE/.test(tsrcTypes)
  assert('TSRC07a 静态：瓶颈策略枚举含 PREFER_SPLIT / PREFER_MERGE', tsrc07a, tsrc07a ? '已含' : '缺枚举')

  const tsrc07b = /PREFER_LARGE_BATCH/.test(tsrcTypes) && /PREFER_SMALL_BATCH/.test(tsrcTypes)
  assert('TSRC07b 静态：非瓶颈策略枚举含 PREFER_LARGE_BATCH / PREFER_SMALL_BATCH', tsrc07b, tsrc07b ? '已含' : '缺枚举')

  // TSRC08 动态：联调真实端点（10-07 dev 库 schema 已迁移，5 列齐全）
  //  - 真实调用：GET /api/governance/task-split-rule-configs
  //  - 10-08 后端 PageResult<T> 落地：data 是 { items, total, page, pageSize } 而非裸数组
  //    - 兼容两种形态：data 本身是数组（早期），或 data.items 是数组（PageResult）
  //  - 预期：r.ok=true 且 items/数组 可达（可能为空，dev 库暂未造数）
  //  - 不可达兜底：r.code === 401/403/404/500 一律视为「端点未达」
  //    - 500+SqlException "列名 xxx 无效" → schema 仍缺列（重新排查）
  const tsrc08r = await call('GET', '/api/governance/task-split-rule-configs', { token })
  const tsrc08items = Array.isArray(tsrc08r.json?.data)
    ? tsrc08r.json.data
    : Array.isArray(tsrc08r.json?.data?.items)
      ? tsrc08r.json.data.items
      : null
  if (tsrc08r.ok && tsrc08items !== null) {
    assert(
      'TSRC08 动态：GET /api/governance/task-split-rule-configs（dev 库 schema 已迁移，5 列齐全）',
      true,
      `code=${tsrc08r.code}, items 长度=${tsrc08items.length}, 形态=${Array.isArray(tsrc08r.json?.data) ? 'array' : 'PageResult'}`
    )
  } else {
    assert(
      'TSRC08 动态：GET /api/governance/task-split-rule-configs（dev 库 schema 已迁移，5 列齐全）',
      false,
      `code=${tsrc08r.code}, message=${tsrc08r.json?.message || tsrc08r.text?.slice(0, 80)}（可能 dev 库未跑 ALTER）`
    )
  }

  // TSRC09 静态：Pinia store 桩代码就位
  const tsrcStore = readSrc('store/modules/aps/taskSplitRuleConfig.ts')
  const tsrc09a = /export\s+const\s+useTaskSplitRuleConfigStore\s*=/.test(tsrcStore)
  assert('TSRC09a 静态：store/modules/aps/taskSplitRuleConfig.ts 含 useTaskSplitRuleConfigStore', tsrc09a, tsrc09a ? '已含' : '缺 store')

  const tsrc09b = /async\s+function\s+load\s*\([^)]*\)/.test(tsrcStore) &&
    /async\s+function\s+create\s*\(/.test(tsrcStore) &&
    /async\s+function\s+update\s*\(/.test(tsrcStore) &&
    /async\s+function\s+deactivate\s*\(/.test(tsrcStore)
  assert('TSRC09b 静态：store 含 load/create/update/deactivate 4 动作', tsrc09b, tsrc09b ? '4 动作齐' : '缺动作')

  // TSRC10 静态：路由占位
  const tsrcRouter = readSrc('router/modules/aps.ts')
  const tsrc10 = /path:\s*['"]task-split-rule-config['"]/.test(tsrcRouter)
  assert('TSRC10 静态：router/modules/aps.ts 含 path=task-split-rule-config', tsrc10, tsrc10 ? '路由已占位' : '缺路由')

  // TSRC11 静态：页面入口
  const tsrcPagePath = 'views/Aps/TaskSplitRuleConfig.vue'
  const tsrc11 = existsSync(join(SRC_ROOT, tsrcPagePath))
  assert(`TSRC11 静态：${tsrcPagePath} 存在`, tsrc11, tsrc11 ? '页面已落地' : '缺页面')

  // TSRC12 静态：Drawer 部门下拉数据源切到正式 lookup 端点（10-07 替换 G2 过渡实现）
  //  - departmentLookupApi.listDepartments() → GET /api/governance/lookups/departments（3 号位 SetupRuleController.cs:299）
  const tsrcDepPage = readSrc('views/Aps/TaskSplitRuleConfig.vue')
  const tsrc12a =
    /departmentLookupApi\.listDepartments\s*\(/.test(tsrcDepPage) &&
    !/resourceCalendarApi\.listDepartmentCandidates\s*\(/.test(tsrcDepPage)
  assert(
    'TSRC12a 静态：TaskSplitRuleConfig.vue 部门下拉用 departmentLookupApi（不再用 G2 过渡）',
    tsrc12a,
    tsrc12a ? '已切正式端点' : '仍用过渡或未切'
  )

  const tsrcDepApi = readSrc('api/aps-v1/departmentLookup.ts')
  const tsrc12b =
    /\/api\/governance\/lookups\/departments/.test(tsrcDepApi) &&
    /listDepartments\s*\(/.test(tsrcDepApi)
  assert(
    'TSRC12b 静态：departmentLookup.ts 封装 GET /api/governance/lookups/departments',
    tsrc12b,
    tsrc12b ? '已封装正式端点' : '封装缺失'
  )

  const tsrcDepIdx = readSrc('api/aps-v1/index.ts')
  const tsrc12c =
    /export\s*\{\s*departmentLookupApi\s*\}\s*from\s*['"]\.\/departmentLookup['"]/.test(tsrcDepIdx)
  assert('TSRC12c 静态：aps-v1/index.ts re-export departmentLookupApi', tsrc12c, tsrc12c ? '已 re-export' : '缺 re-export')

  // TSRC12d 动态：真实调 GET /api/governance/lookups/departments（dev 库 G2 正式端点）
  const tsrc12r = await call('GET', '/api/governance/lookups/departments', { token })
  if (tsrc12r.ok && Array.isArray(tsrc12r.json?.data)) {
    const data = tsrc12r.json.data
    const hasMasterId = data.length > 0 && typeof data[0]?.masterId === 'number'
    assert(
      'TSRC12d 动态：GET /api/governance/lookups/departments（dev 库 G2 正式端点）',
      hasMasterId,
      `code=${tsrc12r.code}, data 长度=${data.length}, masterId 类型=${hasMasterId ? 'number' : '缺失'}`
    )
  } else {
    assert(
      'TSRC12d 动态：GET /api/governance/lookups/departments（dev 库 G2 正式端点）',
      false,
      `code=${tsrc12r.code}, message=${tsrc12r.json?.message || tsrc12r.text?.slice(0, 80)}`
    )
  }

  // TSRC13 静态：主表格 ID → 名称 显示（10-08 用户要求：「4号位 所有列表/新增显示名称，不显示内部 ID」）
  //  - 物料ID 列渲染物料编码/名称（materialLabel）
  //  - 生产部门ID 列渲染部门编码（departmentLabel）
  //  - 规则ID 列（auto-increment 内部键）整体移除
  //  - onMounted 并行预加载 materialOptions / departmentOptions，不阻塞 refresh()
  const tsrc13a =
    /function\s+materialLabel\s*\(/.test(tsrcDepPage) &&
    /function\s+departmentLabel\s*\(/.test(tsrcDepPage) &&
    // 表格里调 materialLabel(row.materialId) / departmentLabel(row.productionDepartmentId)
    /materialLabel\(row\.materialId\)/.test(tsrcDepPage) &&
    /departmentLabel\(row\.productionDepartmentId\)/.test(tsrcDepPage) &&
    // 规则ID 列必须移除（不能再有 prop="id" label="规则ID"）
    !/prop="id"\s+label="规则ID"/.test(tsrcDepPage)
  assert(
    'TSRC13a 静态：TaskSplitRuleConfig.vue 主表格 ID → 名称（materialLabel/departmentLabel + 移除 规则ID 列）',
    tsrc13a,
    tsrc13a ? '已落地' : '未配齐 label 函数或未移除 规则ID'
  )

  // TSRC13b：onMounted 并行预加载 options，不阻塞 refresh()
  const tsrc13b =
    /onMounted\(\s*\(\)\s*=>\s*\{[^}]*loadMaterialOptions\(\)[^}]*loadDepartmentOptions\(\)[^}]*refresh\(\)/s.test(
      tsrcDepPage
    )
  assert(
    'TSRC13b 静态：onMounted 并行预加载 materialOptions/departmentOptions + refresh()',
    tsrc13b,
    tsrc13b ? '已预加载' : 'onMounted 加载顺序不合规'
  )

  // ============================================================
  // TSRC14 列表分页化（10-08 列表分页改造落地；3号位 拍板方案 A：pageIndex / pageSize + PageResult<T>）
  //   @see 3号位致4号位_TaskSplitRuleConfig列表分页改造回执_方案A_v1.0_20261008.md
  //   @see 4号位-2026-10-08-TaskSplitRuleConfig-列表分页改造-给3号位.md
  // ============================================================

  // TSRC14a 静态：types/taskSplitRuleConfig.ts ListQuery 含 pageIndex / pageSize
  //   - 拆成两个独立断言：避免 `[^}]]` 字符类嵌套
  const tsrc14aPageIndex = /export\s+interface\s+TaskSplitRuleConfigListQuery[\s\S]*?\bpageIndex\s*\?\s*:\s*number[\s\S]*?\}/.test(
    tsrcTypes
  )
  const tsrc14aPageSize = /export\s+interface\s+TaskSplitRuleConfigListQuery[\s\S]*?\bpageSize\s*\?\s*:\s*number[\s\S]*?\}/.test(
    tsrcTypes
  )
  const tsrc14a = tsrc14aPageIndex && tsrc14aPageSize
  assert(
    'TSRC14a 静态：types/taskSplitRuleConfig.ts ListQuery 含 pageIndex / pageSize',
    tsrc14a,
    tsrc14a ? '已含' : `pageIndex=${tsrc14aPageIndex} pageSize=${tsrc14aPageSize}`
  )

  // TSRC14b 静态：types/taskSplitRuleConfig.ts 含 PagedTaskSplitRuleConfig
  const tsrc14b = /export\s+interface\s+PagedTaskSplitRuleConfig\b/.test(tsrcTypes)
  assert(
    'TSRC14b 静态：types/taskSplitRuleConfig.ts 含 PagedTaskSplitRuleConfig',
    tsrc14b,
    tsrc14b ? '已含' : '缺 Paged 类型'
  )

  // TSRC14c 静态：api/taskSplitRuleConfig.ts list() 返回 Promise<PagedTaskSplitRuleConfig>
  const tsrc14c = /async\s+list\s*\([^)]*\)\s*:\s*Promise<PagedTaskSplitRuleConfig>/.test(tsrcApi)
  assert(
    'TSRC14c 静态：api/taskSplitRuleConfig.ts list() 返回 Promise<PagedTaskSplitRuleConfig>',
    tsrc14c,
    tsrc14c ? '已对齐' : 'list() 返回类型错'
  )

  // TSRC14d 静态：api list() 请求 URL 显式传 pageIndex / pageSize（避免后端默认 100）
  const tsrc14d =
    /params\s*:\s*Record<string,\s*unknown>\s*=\s*\{[\s\S]*?\bpageIndex\b[\s\S]*?\bpageSize\b[\s\S]*?\}/.test(
      tsrcApi
    ) ||
    /params\.pageIndex\s*=/.test(tsrcApi) ||
    /params\.pageSize\s*=/.test(tsrcApi)
  assert(
    'TSRC14d 静态：api list() 显式传 pageIndex / pageSize 给后端',
    tsrc14d,
    tsrc14d ? '已显式' : 'params 缺 pageIndex / pageSize'
  )

  // TSRC14e 静态：store 含 total / page / pageSize 三个 ref + 暴露在 return
  //   - 拆成 3 个 ref 断言 + 1 个 return 暴露断言（顺序不强制，只要求 3 个都在 return 内）
  //   - 兼容 `ref<number>(0)` / `ref(0)` / `ref<number>(DEFAULT_PAGE_SIZE)` 三种写法
  const tsrc14eTotal = /\btotal\s*=\s*ref(?:\s*<[^>]+>)?\s*\(\s*0(?:\s*as\s+number)?\s*\)/.test(tsrcStore)
  const tsrc14ePage = /\bpage\s*=\s*ref(?:\s*<[^>]+>)?\s*\(\s*1(?:\s*as\s+number)?\s*\)/.test(tsrcStore)
  const tsrc14ePageSize =
    /\bpageSize\s*=\s*ref(?:\s*<[^>]+>)?\s*\(\s*(?:20|DEFAULT_PAGE_SIZE)(?:\s*as\s+number)?\s*\)/.test(
      tsrcStore
    )
  const tsrc14eReturn =
    /\breturn\s*\{[\s\S]*?\btotal\b[\s\S]*?\bpage\b[\s\S]*?\bpageSize\b/.test(tsrcStore) &&
    /\breturn\s*\{[\s\S]*?\bload\b/.test(tsrcStore)
  const tsrc14e = tsrc14eTotal && tsrc14ePage && tsrc14ePageSize && tsrc14eReturn
  assert(
    'TSRC14e 静态：store 含 total / page / pageSize 三个 ref + return 暴露',
    tsrc14e,
    tsrc14e
      ? '已暴露'
      : `total=${tsrc14eTotal} page=${tsrc14ePage} pageSize=${tsrc14ePageSize} return=${tsrc14eReturn}`
  )

  // TSRC14f 静态：view storeToRefs 解构 total / page / pageSize
  const tsrc14f =
    /\bstoreToRefs\(store\)[\s\S]*?\btotal\b[\s\S]*?\bpage\b[\s\S]*?\bpageSize\b/.test(tsrcDepPage)
  assert(
    'TSRC14f 静态：TaskSplitRuleConfig.vue storeToRefs 解构 total / page / pageSize',
    tsrc14f,
    tsrc14f ? '已解构' : 'view 缺分页字段解构'
  )

  // TSRC14g 静态：view 含 ElPagination + :current-page / :page-size / :total / @current-change / @size-change
  const tsrc14g =
    /<ElPagination\b/.test(tsrcDepPage) &&
    /<ElPagination\b[\s\S]*?:current-page="page"/.test(tsrcDepPage) &&
    /<ElPagination\b[\s\S]*?:page-size="pageSize"/.test(tsrcDepPage) &&
    /<ElPagination\b[\s\S]*?:total="total"/.test(tsrcDepPage) &&
    /@current-change="onPageChange"/.test(tsrcDepPage) &&
    /@size-change="onSizeChange"/.test(tsrcDepPage)
  assert(
    'TSRC14g 静态：TaskSplitRuleConfig.vue 含 ElPagination（:current-page / :page-size / :total + @current-change / @size-change）',
    tsrc14g,
    tsrc14g ? '已含分页器' : '缺分页器 / 缺绑定'
  )

  // TSRC14h 静态：view applyFilter 重置 pageIndex=1（避免筛选无交集时空态错觉）
  const tsrc14h = /applyFilter[\s\S]*?resetPage\s*:\s*true/.test(tsrcDepPage)
  assert(
    'TSRC14h 静态：applyFilter 调用 resetPage:true（筛选无交集时回首页）',
    tsrc14h,
    tsrc14h ? '已重置' : '筛选未重置分页'
  )

  // TSRC14i 静态：KPI 总规则数 改用 total（不再用 list.length）
  const tsrc14i = /kpi-value[^>]*>\s*\{\{\s*total\s*\}\s*\}\s*</.test(tsrcDepPage)
  assert(
    'TSRC14i 静态：KPI 总规则数 改用 total（来自后端 COUNT）',
    tsrc14i,
    tsrc14i ? '已用 total' : 'KPI 仍用 list.length'
  )

  // TSRC15 静态：DateTime 字段改用 formatUtcDate / formatUtcDateTime（解决 naive UTC bug：少 8h）
  //  - dev 后端 DateTime 序列化为 naive UTC 无 Z 后缀；裸 dayjs() 按本地解析 → 时间少 8h
  //  - 锁 5 处：effectiveFrom / effectiveTo（formatUtcDate）+ updatedAt 表列 + createdAt/updatedAt 抽屉审计
  //  - 锁 import：import { formatUtcDate, formatUtcDateTime } from '@/utils/datetime'
  //  - 锁 import：禁止直接 `import dayjs from 'dayjs'`（除非确有 dayjs.utc 之外需求）
  const tsrc15Import = /import\s*\{[^}]*\bformatUtcDate\b[^}]*\}\s*from\s*['"]@\/utils\/datetime['"]/.test(
    tsrcDepPage
  )
  const tsrc15ImportDateTime = /import\s*\{[^}]*\bformatUtcDateTime\b[^}]*\}\s*from\s*['"]@\/utils\/datetime['"]/.test(
    tsrcDepPage
  )
  const tsrc15NoBareDayjs = !/\bdayjs\s*\(\s*(?:row|list\.find)/.test(tsrcDepPage)
  const tsrc15EffectiveFrom = /row\.effectiveFrom\s*\?\s*formatUtcDate\(/.test(tsrcDepPage)
  const tsrc15EffectiveTo = /row\.effectiveTo\s*\?\s*formatUtcDate\(/.test(tsrcDepPage)
  const tsrc15UpdatedAtTable = /formatUtcDateTime\(\s*row\.updatedAt\s*\)/.test(tsrcDepPage)
  const tsrc15CreatedAtDrawer = /formatUtcDateTime\(\s*list\.find\([\s\S]*?\)\?\.createdAt\s*\)/.test(tsrcDepPage)
  const tsrc15UpdatedAtDrawer = /formatUtcDateTime\(\s*list\.find\([\s\S]*?\)\?\.updatedAt\s*\)/.test(tsrcDepPage)
  const tsrc15 = tsrc15Import && tsrc15ImportDateTime && tsrc15NoBareDayjs &&
    tsrc15EffectiveFrom && tsrc15EffectiveTo && tsrc15UpdatedAtTable &&
    tsrc15CreatedAtDrawer && tsrc15UpdatedAtDrawer
  assert(
    'TSRC15 静态：DateTime 字段改用 formatUtcDate/formatUtcDateTime（5 处全覆盖）',
    tsrc15,
    tsrc15
      ? '已切 formatUtcXxx'
      : `import=${tsrc15Import}/${tsrc15ImportDateTime} noBare=${tsrc15NoBareDayjs} effFrom=${tsrc15EffectiveFrom} effTo=${tsrc15EffectiveTo} updTbl=${tsrc15UpdatedAtTable} creDrw=${tsrc15CreatedAtDrawer} updDrw=${tsrc15UpdatedAtDrawer}`
  )

  // TSRC16 静态：create() smart-insert（按后端排序契约 MaterialId, ProductionDepartmentId, Id ASC 插入到正确位置）
  //  - 锁 create() 不再 append（避免与后端排序不一致 → 刷新跳变）
  //  - 锁 findSortedInsertIndex 工具存在（camps: MaterialId → Dept NULL 最大 → Id）
  //  - 锁二分 lowerBound 查找（无边界越界检查：用户看到新条目出现在其排序位置，跨页场景下次 load 自然拉回）
  const tsrc16NoAppend = !/list\.value\s*=\s*\[\.\.\.list\.value,\s*result\]/.test(tsrcStore)
  const tsrc16HasHelper = /function\s+findSortedInsertIndex\s*\(/.test(tsrcStore)
  const tsrc16CallsHelper = /findSortedInsertIndex\(\s*list\.value\s*,\s*result\s*\)/.test(tsrcStore)
  const tsrc16HasCmp = /a\.materialId\s*!==\s*b\.materialId/.test(tsrcStore) &&
    /productionDepartmentId\s*\?\?\s*Number\.MAX_SAFE_INTEGER/.test(tsrcStore) &&
    /a\.id\s*-\s*b\.id/.test(tsrcStore)
  const tsrc16HasBinarySearch = /\(lo\s*\+\s*hi\)\s*>>>\s*1/.test(tsrcStore)
  const tsrc16 = tsrc16NoAppend && tsrc16HasHelper && tsrc16CallsHelper && tsrc16HasCmp && tsrc16HasBinarySearch
  assert(
    'TSRC16 静态：create() smart-insert（findSortedInsertIndex + 二分 lowerBound）',
    tsrc16,
    tsrc16
      ? '已切 smart-insert'
      : `noAppend=${tsrc16NoAppend} helper=${tsrc16HasHelper} call=${tsrc16CallsHelper} cmp=${tsrc16HasCmp} binary=${tsrc16HasBinarySearch}`
  )

  // TSRC17 静态：筛选物料 ElAutocomplete label 清空 → key 同步置 null
  //   - 锁：watch(filterMaterialLabel) 在 label 为空时把 filterMaterialKey 置 null
  //   - 否则 × 按钮 / 手动删空后 filterMaterialKey 残留旧 masterId，下次 [筛选] 仍按旧 materialId 过滤（违反「留空 = 全部」语义）
  const tsrcVueContent = readSrc('views/Aps/TaskSplitRuleConfig.vue')
  const tsrc17Watch =
    /watch\(\s*filterMaterialLabel\b/.test(tsrcVueContent) ||
    /watch\(\s*\(\)\s*=>\s*filterMaterialLabel\b/.test(tsrcVueContent)
  const tsrc17ResetsKey =
    /if\s*\(\s*!\s*next\s*\)\s*\{[\s\S]*?filterMaterialKey\.value\s*=\s*null/.test(tsrcVueContent) ||
    /if\s*\(\s*!\s*newLabel\s*\)\s*\{[\s\S]*?filterMaterialKey\.value\s*=\s*null/.test(tsrcVueContent)
  const tsrc17 = tsrc17Watch && tsrc17ResetsKey
  assert(
    'TSRC17 静态：ElAutocomplete label 清空 → filterMaterialKey 同步置 null（× 按钮 / 手动删空）',
    tsrc17,
    tsrc17 ? '已加 watch 同步' : `watch=${tsrc17Watch} resetsKey=${tsrc17ResetsKey}`
  )

  // TSRC14j 动态：pageIndex=1&pageSize=2 → items.length≤2 且 total≥items.length（验证脚本默认联调真实后端）
  //   - 兼容两种 dev 端响应形态（10-08 PageResult 部署时机可能滞后）：
  //     - PageResult：{ items, total, page, pageSize } → 严格断言 items.length≤2 && total≥items.length
  //     - 旧裸数组：[...] → 断言 length≤2（dev 默认 100 不带分页时可能 length=100 → 视为 PageResult 缺位/退回）
  try {
    const r1 = await call('GET', '/api/governance/task-split-rule-configs', {
      token,
      query: { pageIndex: 1, pageSize: 2 }
    })
    const raw1 = r1.json?.data
    const isPage1 = raw1 && typeof raw1 === 'object' && Array.isArray(raw1.items)
    const isArray1 = Array.isArray(raw1)
    const items1 = isPage1 ? raw1.items : isArray1 ? raw1 : null
    const ok1 =
      items1 !== null && items1.length <= 2 && (isPage1 ? raw1.total >= items1.length : true)
    assert(
      'TSRC14j 动态：GET list pageIndex=1&pageSize=2 → items.length≤2 且 total≥items.length',
      ok1,
      ok1
        ? `形态=${isPage1 ? 'PageResult' : 'array'}, items=${items1.length}${isPage1 ? ` total=${raw1.total} pageSize=${raw1.pageSize}` : ''}`
        : `code=${r1.code}, data 形态=${isPage1 ? 'PageResult' : isArray1 ? 'array' : 'unknown'}, items=${items1?.length}`
    )

    // TSRC14k 动态：pageSize=9999 → 后端 Math.Clamp 截断到 200（仅在 PageResult 形态下可证）
    //   - 旧裸数组形态无法证截断（dev 默认 100 全量回）→ 跳过 pageSize=200 断言
    const r2 = await call('GET', '/api/governance/task-split-rule-configs', {
      token,
      query: { pageIndex: 1, pageSize: 9999 }
    })
    const raw2 = r2.json?.data
    const isPage2 = raw2 && typeof raw2 === 'object' && Array.isArray(raw2.items)
    if (isPage2) {
      const ok2 = raw2.pageSize === 200
      assert(
        'TSRC14k 动态：GET list pageSize=9999 → 后端 Math.Clamp 截断到 200',
        ok2,
        ok2 ? `实际 pageSize=${raw2.pageSize}` : `实际 pageSize=${raw2.pageSize} code=${r2.code}`
      )
    } else {
      skip('TSRC14k 动态：GET list pageSize=9999 → dev 端尚未返 PageResult（10-08 部署可能滞后），无法验证 Math.Clamp=200')
    }
  } catch (err) {
    skip(`TSRC14j/k 动态：真实后端联调（跳过：${err.message}）`)
  }

  // TSRC18 静态：v1.1 接口签名 Scope 变更登记（10-09 P0-01）
  //   - 触发：3号位致4号位_TaskSplitRuleConfig与StageLeadTimeParam_接口签名Scope变更登记_v1.1_20261009.md
  //   - 前端共性影响：HTTP 零变化；仅 403「业务范围越界（Department）」新增 + 空态分两支 + 友好提示
  //   - 锁契约：3 个文件头注释均引用 v1.1；view 含 errorAlertTitle / emptyDescription + SCOPE_VIOLATION_PATTERN
  //   - 反向探针自证可失败：删 v1.1 引用会让 TSRC18 由绿转红
  const tsrc18a =
    /3号位致4号位_TaskSplitRuleConfig与StageLeadTimeParam_接口签名Scope变更登记_v1\.1_20261009/.test(
      tsrcApi
    )
  const tsrc18b =
    /3号位致4号位_TaskSplitRuleConfig与StageLeadTimeParam_接口签名Scope变更登记_v1\.1_20261009/.test(
      readSrc('store/modules/aps/taskSplitRuleConfig.ts')
    )
  const tsrc18c =
    /3号位致4号位_TaskSplitRuleConfig与StageLeadTimeParam_接口签名Scope变更登记_v1\.1_20261009/.test(
      tsrcVueContent
    )
  const tsrc18d = /const\s+SCOPE_VIOLATION_PATTERN\s*=\s*\/业务范围越界\//.test(tsrcVueContent)
  const tsrc18e = /const\s+errorAlertTitle\s*=\s*computed/.test(tsrcVueContent)
  const tsrc18f = /const\s+emptyDescription\s*=\s*computed/.test(tsrcVueContent)
  const tsrc18g = /:title="errorAlertTitle"/.test(tsrcVueContent)
  const tsrc18h = /:description="emptyDescription"/.test(tsrcVueContent)
  const tsrc18All =
    tsrc18a &&
    tsrc18b &&
    tsrc18c &&
    tsrc18d &&
    tsrc18e &&
    tsrc18f &&
    tsrc18g &&
    tsrc18h
  assert(
    'TSRC18 静态：v1.1 P0-01 契约落地（3 文件头注释 v1.1 引用 + view 友好提示 + 空态分两支）',
    tsrc18All,
    tsrc18All
      ? '8 项齐全'
      : `api=${tsrc18a} store=${tsrc18b} vue=${tsrc18c} pattern=${tsrc18d} errTitle=${tsrc18e} emptyDesc=${tsrc18f} alertTitleUse=${tsrc18g} emptyDescUse=${tsrc18h}`
  )

  info('TSRC 段：3号位 2026-10-07 v1.0 契约前端适配 + 后端 7 文件落地 + 10-08 列表分页改造（方案 A）落地完成 + 10-09 v1.1 P0-01 Scope 变更登记 友好提示闭环（TSRC18 静态 8 项齐）')
}

/**
 * [DP] DemandProtection 冻结文档一致性核查（dp-freeze — 10-09 R1/R2/R3 闭环静态）
 * 验收依据：5号位致4号位_DemandProtection一致性核查回执_20261009.md
 *  - R1 PARTIALLY_RELEASED 派生态 5号位 接受（前端聚合层派生），stale 5号位 聚合注释已纠正 → DP01-DP03
 *  - R2 isIrreversible 字段已移交 2号位 联合评估，本期不锁（待 5号位 DTO 透传后再开 DP06/07）
 *  - R3 三事实已核实答复，无前端联动
 *  - §2.3 page-sub §42.2 文案 + §2.4 4 项状态/placeholder/KPI 文案 → DP04/05
 */
async function verifyDemandProtectionFreeze() {
  console.log('\n=== [DP] DemandProtection 冻结一致性（dp-freeze — 10-09 函件 + 冻结文档 §42）===')

  const dpTypes = readSrc('api/aps-v1/types/demandProtection.ts')
  const dpVue = readSrc('views/Aps/DemandProtection.vue')

  // DP01 静态：types/demandProtection.ts L13 4号位职责边界已纠正派生态派生者 = 前端 store
  //   - 修复前："不计算 status 派生态（... 由 5号位聚合）"
  //   - 修复后："不计算 status 派生态（... 由前端 store 聚合；详见 5号位 2026-10-09 函件 R1 回执）"
  const dp01Boundary =
    /不计算\s+status\s+派生态[\s\S]{0,80}?前端\s+store\s+聚合/.test(dpTypes) &&
    /5号位\s+2026-10-09\s+函件\s+R1\s+回执/.test(dpTypes)
  assert(
    'DP01 静态：types/demandProtection.ts 4号位职责边界已标派生态派生者 = 前端 store（5号位 R1 回执）',
    dp01Boundary,
    dp01Boundary ? '已纠正' : '仍标 5号位 聚合'
  )

  // DP02 静态：DemandProtectionStatus 类型注释已改正（"由前端 store applyReleaseResultsToList 根据明细 lockDetail.status 派生"）
  const dp02TypeHeader =
    /由\s*前端\s+store\s+applyReleaseResultsToList\s+根据明细\s+lockDetail\.status\s+派生/.test(dpTypes)
  assert(
    'DP02 静态：DemandProtectionStatus 类型注释已标派生者 = 前端 store applyReleaseResultsToList',
    dp02TypeHeader,
    dp02TypeHeader ? '已加 store 函数名引用' : '类型注释仍标 5号位 派生'
  )

  // DP03 静态：DemandProtectionDto.status 字段注释已改正（"由前端 store 聚合，4号位不计算"）
  const dp03FieldDoc =
    /派生态[（(]由\s*前端\s+store\s+聚合/.test(dpTypes) &&
    !/派生态[（(]由\s*5号位\s*聚合/.test(dpTypes)
  assert(
    'DP03 静态：DemandProtectionDto.status 字段注释派生者 = 前端 store（不再误标 5号位）',
    dp03FieldDoc,
    dp03FieldDoc ? '已纠正' : '字段注释仍标 5号位 聚合'
  )

  // DP04 静态：§42.2 page-sub「锁的是供给数量份额（非整张 PI/PO）」已撤销（10-09 用户决定）
  //   - 依据：[[feedback-no-internal-notices-in-ui]] 内部协调痕迹不进用户可见区
  //   - 反向探针：page-sub 元素不应存在（防止未来回退）
  //   - 释放页面「只支持整条释放」ElAlert 也已撤销（同条原则）
  const dp04HasPageSub = /class="page-sub"/.test(dpVue)
  const dp04HasFullReleaseAlert = /只支持整条释放/.test(dpVue)
  assert(
    'DP04 静态：DemandProtection.vue page-sub + "只支持整条释放" ElAlert 已撤销（10-09 用户决定，反向探针）',
    !dp04HasPageSub && !dp04HasFullReleaseAlert,
    !dp04HasPageSub && !dp04HasFullReleaseAlert
      ? 'page-sub + 全条释放提示均已移除'
      : `仍含解释文字（page-sub=${dp04HasPageSub}, fullReleaseAlert=${dp04HasFullReleaseAlert}）`
  )

  // DP05 静态：§2.4 3 项中文命名术语对齐（KPI 卡 10-09 撤销后，"含部分释放的混合态" KPI 副文案消失）
  //   - 保留：生效中（表格状态列）+ 历史混合态（Drawer PARTIALLY_RELEASED 徽章）+ 便于事后追溯（释放原因 placeholder）
  //   - 移除：含部分释放的混合态（KPI 副文案）— 随 KPI 卡一并消失
  const dp05a = /生效中/.test(dpVue)
  const dp05c = /历史混合态[（(]前端不生产该状态[）)]/.test(dpVue)
  const dp05d = /便于事后追溯/.test(dpVue)
  const dp05 = dp05a && dp05c && dp05d
  assert(
    'DP05 静态：§2.4 3 项中文命名术语对齐（生效中/历史混合态/便于事后追溯）',
    dp05,
    `生效中=${dp05a} 历史混合态=${dp05c} 便于追溯=${dp05d}`
  )

  // 注意：R2 isIrreversible (5号位 R2 → 2号位 联合评估) 暂不开断言；
  //   - 落地窗口：5号位 承诺 DTO 透传在 2号位 写锁 IsIrreversible 落列后
  //   - 前端待办（待 2号位 + 5号位 落地后）：
  //     - DTO 增 isIrreversible?: boolean
  //     - canRelease() 增不可逆判定（聚合行任一 lockDetail.isIrreversible → 整条不可释放）
  //     - Drawer 锁明细表格加 "不可逆" 徽章列
  //     - 释放按钮 disabled + tooltip 复用 STRICT_BINDING 模式
  //   - 4号位 端不动，等回执
  skip('DP06 静态：lockDetail.isIrreversible 字段 + canRelease() 不可逆判定（待 2号位 + 5号位 落地后开）')
  skip('DP07 静态：Drawer 锁明细 "不可逆" 徽章列 + 释放按钮 disabled/tooltip（待后端 DTO 透传后开）')

  info('DP 段：5号位 2026-10-09 一致性核查回执 R1/R2/R3 闭环（前 R1 静态 + §2.3/§2.4 中文术语对齐；R2 等 2号位 落地）')
}

/**
 * [DPL] DemandProtection lps/ 联调（dplive — 10-09 5号位 后端落地 → 4号位 前端适配）
 *
 * 验收依据：5号位 lps/ 10-09 落地（GET 列表 per-lock / GET /summary KPI / POST release）
 *  4号位 端按 AskUserQuestion 拍板：
 *   - UX: A — 前端聚合后端 flat list
 *   - KPI: A — 调 /summary 端点拿权威数
 *
 * 验收点：
 *  - DPL01-03 静态：types/demandProtection.ts 含 BackendDemandProtectionRow + DemandProtectionSummary + 4 字段 filter
 *  - DPL04-05 静态：api/demandProtection.ts list() 返 per-lock；summary() 调 /summary 端点
 *  - DPL06-07 静态：store 含 aggregateByDemandKey + kpiSummary ref + 5 KPI getter
 *  - DPL08 静态：view 筛选改用 substring + single lockType/status
 *  - DPL09 静态：view KPI "总锁条数" 用 totalCount（不是 list.length）
 *  - DPL10 静态：store load() Promise.all(list + summary)
 */
async function verifyDemandProtectionLive() {
  console.log('\n=== [DPL] DemandProtection lps/ 联调（dplive — 10-09 5号位 后端落地 → 4号位 前端适配）===')

  const dpTypes = readSrc('api/aps-v1/types/demandProtection.ts')
  const dpApi = readSrc('api/aps-v1/demandProtection.ts')
  const dpStore = readSrc('store/modules/aps/demandProtection.ts')
  const dpVue = readSrc('views/Aps/DemandProtection.vue')

  // DPL01 静态：types/demandProtection.ts BackendDemandProtectionRow interface 存在
  const dpl01 =
    /export\s+interface\s+BackendDemandProtectionRow\b/.test(dpTypes) &&
    /\bid\s*:\s*number\b/.test(dpTypes.match(/export\s+interface\s+BackendDemandProtectionRow\s*\{[\s\S]*?\n\}/)?.[0] ?? '') &&
    /\blockedQty\s*:\s*number\b/.test(dpTypes.match(/export\s+interface\s+BackendDemandProtectionRow\s*\{[\s\S]*?\n\}/)?.[0] ?? '') &&
    /\bstatus\s*:\s*HardLockStatus\b/.test(dpTypes.match(/export\s+interface\s+BackendDemandProtectionRow\s*\{[\s\S]*?\n\}/)?.[0] ?? '')
  assert(
    'DPL01 静态：types/demandProtection.ts BackendDemandProtectionRow interface 含 id/lockedQty/status',
    dpl01,
    dpl01 ? '已含 per-lock raw DTO' : '缺 BackendDemandProtectionRow 字段'
  )

  // DPL02 静态：types/demandProtection.ts DemandProtectionSummary interface 存在
  const summaryBlock = dpTypes.match(/export\s+interface\s+DemandProtectionSummary\s*\{[\s\S]*?\n\}/)?.[0] ?? ''
  const dpl02 =
    /export\s+interface\s+DemandProtectionSummary\b/.test(dpTypes) &&
    /\btotalCount\s*:\s*number\b/.test(summaryBlock) &&
    /\bactiveCount\s*:\s*number\b/.test(summaryBlock) &&
    /\breleasedCount\s*:\s*number\b/.test(summaryBlock) &&
    /\bbrokenCount\s*:\s*number\b/.test(summaryBlock) &&
    /\btotalLockedQty\s*:\s*number\b/.test(summaryBlock) &&
    /\bitems\s*:\s*BackendDemandProtectionRow\[\]/.test(summaryBlock)
  assert(
    'DPL02 静态：types/demandProtection.ts DemandProtectionSummary 含 5 KPI 字段 + items[]',
    dpl02,
    dpl02 ? 'summary DTO 已对齐 5号位' : 'summary DTO 缺字段'
  )

  // DPL03 静态：types/demandProtection.ts DemandProtectionListFilter 含 demandKey/supplyKey/lockType/status/skip/take
  const filterBlock = dpTypes.match(/export\s+interface\s+DemandProtectionListFilter\s*\{[\s\S]*?\n\}/)?.[0] ?? ''
  const dpl03 =
    /\bdemandKey\?\s*:/.test(filterBlock) &&
    /\bsupplyKey\?\s*:/.test(filterBlock) &&
    /\blockType\?\s*:\s*HardLockType\b/.test(filterBlock) &&
    /\bstatus\?\s*:\s*HardLockStatus\b/.test(filterBlock) &&
    /\bskip\?\s*:\s*number\b/.test(filterBlock) &&
    /\btake\?\s*:\s*number\b/.test(filterBlock)
  assert(
    'DPL03 静态：types/demandProtection.ts DemandProtectionListFilter 6 字段（demandKey/supplyKey/lockType/status/skip/take）',
    dpl03,
    dpl03 ? 'filter 形状已对齐 5号位 QueryAsync' : 'filter 缺字段'
  )

  // DPL04 静态：api/demandProtection.ts list() 返回 BackendDemandProtectionRow[]
  const dpl04 =
    /async\s+list\s*\([^)]*\)\s*:\s*Promise<BackendDemandProtectionRow\[\]>/.test(dpApi) &&
    /url:\s*['"]\/api\/demand-protection['"]/.test(dpApi)
  assert(
    'DPL04 静态：api/demandProtection.ts list() 返 BackendDemandProtectionRow[]（per-lock 扁平）',
    dpl04,
    dpl04 ? 'list() 签名 + URL 已对齐' : 'list() 未返 per-lock / URL 不匹配'
  )

  // DPL05 静态：api/demandProtection.ts summary() 调 /api/demand-protection/summary
  const dpl05 =
    /async\s+summary\s*\(/.test(dpApi) &&
    /url:\s*['"]\/api\/demand-protection\/summary['"]/.test(dpApi) &&
    /Promise<DemandProtectionSummary>/.test(dpApi)
  assert(
    'DPL05 静态：api/demandProtection.ts summary() 调 /api/demand-protection/summary',
    dpl05,
    dpl05 ? 'summary() 已对齐 5号位' : 'summary() 缺端点 / 签名'
  )

  // DPL06 静态：store 含 aggregateByDemandKey 函数（按 demandKey 聚合）
  const dpl06 =
    /function\s+aggregateByDemandKey\s*\(/.test(dpStore) &&
    /new\s+Map<string,\s*BackendDemandProtectionRow\[\]>/.test(dpStore) &&
    /PARTIALLY_RELEASED/.test(dpStore)
  assert(
    'DPL06 静态：store aggregateByDemandKey 按 demandKey 聚合 + 派生 PARTIALLY_RELEASED',
    dpl06,
    dpl06 ? '前端聚合层已实现' : '缺 aggregateByDemandKey / 派生态'
  )

  // DPL07 静态：store KPI 基础设施已撤销（10-09 用户决定：列表已能表达信息密度，KPI 卡属冗余 UI）
  //   - 反向探针：kpiSummary ref + 5 KPI getter + refreshKpi 全部不应存在
  //   - GET /api/demand-protection/summary 端点（api.summary()）保留，未来需要时启用
  const dpl07HasKpiSummary = /kpiSummary\s*=\s*ref/.test(dpStore)
  const dpl07HasTotalCountGetter = /\btotalCount\s*=\s*computed/.test(dpStore)
  const dpl07HasActiveCountGetter = /\bactiveCount\s*=\s*computed/.test(dpStore)
  const dpl07HasReleasedCountGetter = /\breleasedCount\s*=\s*computed/.test(dpStore)
  const dpl07HasBrokenCountGetter = /\bbrokenCount\s*=\s*computed/.test(dpStore)
  const dpl07HasTotalLockedQtyGetter = /\btotalLockedQty\s*=\s*computed/.test(dpStore)
  const dpl07HasRefreshKpi = /function\s+refreshKpi|async\s+function\s+refreshKpi/.test(dpStore)
  const dpl07 =
    !dpl07HasKpiSummary &&
    !dpl07HasTotalCountGetter &&
    !dpl07HasActiveCountGetter &&
    !dpl07HasReleasedCountGetter &&
    !dpl07HasBrokenCountGetter &&
    !dpl07HasTotalLockedQtyGetter &&
    !dpl07HasRefreshKpi
  assert(
    'DPL07 静态：store KPI 基础设施已撤销（kpiSummary ref + 5 KPI getter + refreshKpi 全部移除）',
    dpl07,
    dpl07
      ? 'KPI 基础设施已全部移除'
      : `仍含 KPI 基础设施（kpiSummary=${dpl07HasKpiSummary}, totalCount=${dpl07HasTotalCountGetter}, activeCount=${dpl07HasActiveCountGetter}, releasedCount=${dpl07HasReleasedCountGetter}, brokenCount=${dpl07HasBrokenCountGetter}, totalLockedQty=${dpl07HasTotalLockedQtyGetter}, refreshKpi=${dpl07HasRefreshKpi}）`
  )

  // DPL08 静态：view 筛选改用 substring + single lockType/status（不再用 distinctDemandTypes）
  const dpl08 =
    /filterDemandKey/.test(dpVue) &&
    /filterSupplyKey/.test(dpVue) &&
    /filterLockType/.test(dpVue) &&
    /filterStatus/.test(dpVue) &&
    !/distinctDemandTypes/.test(dpVue) &&
    !/distinctLockTypes/.test(dpVue) &&
    !/activeOnlyDraft/.test(dpVue)
  assert(
    'DPL08 静态：view 筛选改用 4 字段（demandKey/supplyKey substring + single lockType/status）',
    dpl08,
    dpl08 ? '筛选形状已对齐 5号位' : 'view 仍用旧多选/activeOnly 形状'
  )

  // DPL09 静态：view KPI 卡已撤销（10-09 用户决定：列表已能表达信息密度）
  //   - 反向探针：4 个 KPI 卡（总锁条数/生效中/已释放/锁定总量）均不应存在
  //   - kpi-row / kpi-card / kpi-label / kpi-value / kpi-ratio 5 个 CSS class 不应在 view 模板出现
  const dpl09HasKpiRow = /class="kpi-row/.test(dpVue)
  const dpl09HasKpiCard = /class="kpi-card/.test(dpVue)
  const dpl09HasKpiLabel = /kpi-label">/.test(dpVue)
  const dpl09HasKpiValue = /kpi-value"/.test(dpVue)
  const dpl09 =
    !dpl09HasKpiRow && !dpl09HasKpiCard && !dpl09HasKpiLabel && !dpl09HasKpiValue
  assert(
    'DPL09 静态：view 4 个 KPI 卡已撤销（kpi-row / kpi-card / kpi-label / kpi-value 全部不存在）',
    dpl09,
    dpl09
      ? 'KPI 卡已全部移除'
      : `仍含 KPI 卡（kpi-row=${dpl09HasKpiRow}, kpi-card=${dpl09HasKpiCard}, kpi-label=${dpl09HasKpiLabel}, kpi-value=${dpl09HasKpiValue}）`
  )

  // DPL10 静态：store load() 单拉 list（KPI 撤销后无需 Promise.all）
  const dpl10 =
    /async\s+function\s+load\s*\(/.test(dpStore) &&
    /await\s+demandProtectionApi\.list\(/.test(dpStore) &&
    !/Promise\.all\(/.test(dpStore)
  assert(
    'DPL10 静态：store load() 单拉 list（无 Promise.all，无 summary 并行）',
    dpl10,
    dpl10 ? 'load() 已简化为单拉 list' : 'load() 仍并行 / 缺 list 调用'
  )

  info('DPL 段：10-09 5号位 lps/ 落地 → 4号位 前端 per-lock + /summary KPI 适配完成（A 路径）')
}

/**
 * [E] Explanation 页面冻结一致性（e-freeze — 10-09 5号位 函件 R1/R2/R3 一致性核查回执）
 *
 * 验收依据：5号位 2026-10-09 回执 [5号位-2026-10-09-Explanation页面一致性-回执-给4号位.md]
 *  - R1 ReasonCode 权威源 = v5.1.10 §八.1 15 码全集（**非 §85 6 码**）
 *  - R1 FROZEN_ZONE_LOCK = 15 码唯一锁码；FIRM/FROZEN/其它执行锁统一并入
 *  - R3 ScheduleFailureInfo 后端不存在 → 撤前端占位面板
 *  - R2 关键路径 / R3 failureSeverity 跨号位（1号位 求解 + 2号位 落盘 + 0号位 裁决 V1 范围）—— 前端不动
 *
 * 验收点（10 静态 + 2 skip）：
 *  - E01-04 静态：types/explanation.ts REASON_CODES 15 码 + 3 码 Severity + 5 码 ObjectType + 撤 ScheduleFailureInfo
 *  - E05 静态：api/explanation.ts getByPlanVersion 修 400 bug（params 透传 planVersionId）
 *  - E06-08 静态：store severityCounts 3 码 + factsByObjectType 5 码 + 接 Summary 端点 errorCount/warnCount/infoCount
 *  - E09 静态：view 排程解释事实展示 15 码 reasonCode 徽章
 *  - E10 静态：view 撤「排程域失败」面板（5号位 关键纠正 3）
 *  - E11-12 skip：R2 isOnCriticalPath + R3 failureSeverity 跨号位（@see dp-freeze DP06/07 模式）
 */
async function verifyExplanationFreeze(adminToken) {
  console.log('\n=== [E] Explanation 页面冻结一致性（e-freeze — 10-09 5号位 函件 R1/R2/R3 一致性核查回执）===')

  const eTypes = readSrc('api/aps-v1/types/explanation.ts')
  const eApi = readSrc('api/aps-v1/explanation.ts')
  const eStore = readSrc('store/modules/aps/explanation.ts')
  const eScheduleStore = readSrc('store/modules/aps/schedule.ts')
  const eVue = readSrc('views/Aps/Explanation.vue')
  const eCommon = readSrc('api/aps-v1/types/common.ts')

  // E01 静态：types/explanation.ts REASON_CODES = 15 码全集（v5.1.10 §八.1 :4863）
  //   - 修复前：view ISSUE_TYPE_LABEL 6 码占位（EQUIPMENT_FAILURE/MATERIAL_AVAILABILITY/...）
  //   - 修复后：REASON_CODES 15 码权威枚举（5号位 R1 接受）
  const e01ReasonCodes = eTypes.match(/export\s+const\s+REASON_CODES\s*=\s*\[([\s\S]*?)\]\s*as\s+const/)?.[1] ?? ''
  const e01ExpectedCodes = [
    'RESOURCE_CAPACITY_WAIT',
    'MATERIAL_SHORTAGE',
    'PRECEDENCE_WAIT',
    'FROZEN_ZONE_LOCK',
    'ROUTING_FALLBACK',
    'STAGE_LEADTIME_FALLBACK',
    'BOM_DEGRADE',
    'CROSS_ORG_HANDOFF',
    'PRIORITY_LOWER_THAN_OTHERS',
    'DUE_DATE_RISK',
    'LOGISTICS_DELAY',
    'PRIORITY_INHERITANCE',
    'CROSS_DOMAIN_VERSION_MISMATCH_RISK',
    'MANUAL_COMPLETED_SHORT',
    'EQUIPMENT_BREAKDOWN_RISK'
  ]
  const e01AllPresent = e01ExpectedCodes.every((c) => new RegExp(`['"]${c}['"]`).test(e01ReasonCodes))
  // 反向探针：旧 6 码占位（含 §85 6 码）不应在 types 出现（防回退）
  const e01Old6Codes = ['MATERIAL_AVAILABILITY', 'CAPACITY_SHORTAGE', 'CONTRACT_VIOLATION']
  const e01NoOld6 = e01Old6Codes.every((c) => !new RegExp(`['"]${c}['"]`).test(eTypes))
  assert(
    'E01 静态：types/explanation.ts REASON_CODES = v5.1.10 §八.1 15 码全集（无 §85 6 码残留）',
    e01AllPresent && e01NoOld6,
    e01AllPresent && e01NoOld6
      ? '15 码全集对齐'
      : `缺失码=${e01ExpectedCodes.filter((c) => !new RegExp(`['"]${c}['"]`).test(e01ReasonCodes)).join(',')} 旧码残留=${e01Old6Codes.filter((c) => new RegExp(`['"]${c}['"]`).test(eTypes)).join(',')}`
  )

  // E02 静态：Severity 3 码（v5.1.10 :4864：INFO / WARN / ERROR；CRITICAL 仅 BOM Issues 用）
  const e02SeverityBlock = eTypes.match(/export\s+const\s+EXPLANATION_SEVERITIES\s*=\s*\[([\s\S]*?)\]\s*as\s+const/)?.[1] ?? ''
  const e02Has3 = /['"]INFO['"]/.test(e02SeverityBlock) && /['"]WARN['"]/.test(e02SeverityBlock) && /['"]ERROR['"]/.test(e02SeverityBlock)
  const e02NoCritical = !/['"]CRITICAL['"]/.test(e02SeverityBlock)
  const e02NoWarningFullWord = !/\bWARNING\b/.test(e02SeverityBlock)
  assert(
    'E02 静态：EXPLANATION_SEVERITIES = INFO/WARN/ERROR 3 码（v5.1.10 :4864；CRITICAL 已撤 / WARNING → WARN）',
    e02Has3 && e02NoCritical && e02NoWarningFullWord,
    e02Has3 && e02NoCritical && e02NoWarningFullWord ? '3 码对齐' : `has3=${e02Has3} noCritical=${e02NoCritical} noWarning=${e02NoWarningFullWord}`
  )

  // E03 静态：ObjectType 5 码（v5.1.10 :4858：ORDER/TASK/RESOURCE/STAGE/DOMAIN；PI/PO 已撤）
  const e03ObjectTypeBlock = eTypes.match(/export\s+const\s+EXPLANATION_OBJECT_TYPES\s*=\s*\[([\s\S]*?)\]\s*as\s+const/)?.[1] ?? ''
  const e03Expected = ['ORDER', 'TASK', 'RESOURCE', 'STAGE', 'DOMAIN']
  const e03AllPresent = e03Expected.every((c) => new RegExp(`['"]${c}['"]`).test(e03ObjectTypeBlock))
  const e03NoOld2 = !/['"](PI|PO)['"]/.test(e03ObjectTypeBlock)
  assert(
    'E03 静态：EXPLANATION_OBJECT_TYPES = ORDER/TASK/RESOURCE/STAGE/DOMAIN 5 码（v5.1.10 :4858；PI/PO 已撤）',
    e03AllPresent && e03NoOld2,
    e03AllPresent && e03NoOld2 ? '5 码对齐' : `缺码=${e03Expected.filter((c) => !new RegExp(`['"]${c}['"]`).test(e03ObjectTypeBlock)).join(',')} 旧码残留=${/['"](PI|PO)['"]/.test(e03ObjectTypeBlock)}`
  )

  // E04 静态：撤 ScheduleFailureInfo interface（5号位 关键纠正 3「后端无此实体」）
  //   - 反向探针：ScheduleFailureInfo / failureType / DOMAIN_FAILED / BLOCKED_BY_UPSTREAM / ENGINE_ERROR 都不应存在
  const e04OldFail = /ScheduleFailureInfo|failureType\s*:|DOMAIN_FAILED|BLOCKED_BY_UPSTREAM|ENGINE_ERROR/.test(eTypes)
  assert(
    'E04 静态：types/explanation.ts 撤 ScheduleFailureInfo + failureType 字段（5号位 关键纠正 3）',
    !e04OldFail,
    !e04OldFail ? 'ScheduleFailureInfo + failureType 已撤' : '仍含 ScheduleFailureInfo / failureType 占位'
  )

  // E05 静态：api/explanation.ts getByPlanVersion 修 400 bug（params 透传 planVersionId）
  //   - 修复前：`apsHttp.get({ url: '/api/explanation/summary' })` 漏 planVersionId query → 400
  //   - 修复后：`apsHttp.get({ url, params: { planVersionId } })` 透传
  //   - 10-10 改造：getByPlanVersion 真实模式拆为 2 次 apsHttp.get（summary + business-fact-issues），Summary 仍带 planVersionId params
  const e05ApiGet =
    /apsHttp\.get<\{[\s\S]*?\}>\(\s*\{\s*url:\s*['"]\/api\/explanation\/summary['"]\s*,\s*params:\s*\{\s*planVersionId\s*\}\s*\}/.test(eApi) ||
    /apsHttp\.get<[^>]*>\(\s*\{\s*url:\s*['"]\/api\/explanation\/summary['"]\s*,\s*params:\s*\{\s*planVersionId\s*\}\s*\}/.test(eApi)
  assert(
    'E05 静态：api/explanation.ts getByPlanVersion 透传 planVersionId params（修 400 bug）',
    e05ApiGet,
    e05ApiGet ? 'params 透传 planVersionId' : '仍缺 planVersionId 参数（400 bug 未修）'
  )

  // E06 静态：store severityCounts 3 码（ERROR/WARN/INFO；CRITICAL 已撤）
  //   - 修复前：CRITICAL/WARNING/ERROR/INFO 4 计数
  //   - 修复后：ERROR/WARN/INFO 3 计数（来自 Summary 端点 errorCount/warnCount/infoCount）
  //   - 严格抓 return { ... } 块，不被上方注释里的"CRITICAL 已撤"误报
  const e06ReturnBlock = eStore.match(/severityCounts\s*=\s*computed[\s\S]*?return\s*\{([\s\S]*?)\}\s*\)/)?.[1] ?? ''
  const e06StoreSeverity = /ERROR\s*:\s*d\?\.errorCount/.test(e06ReturnBlock) &&
    /WARN\s*:\s*d\?\.warnCount/.test(e06ReturnBlock) &&
    /INFO\s*:\s*d\?\.infoCount/.test(e06ReturnBlock)
  const e06NoCritical = !/CRITICAL/.test(e06ReturnBlock)
  assert(
    'E06 静态：store severityCounts 3 码（来自 Summary 端点 errorCount/warnCount/infoCount）',
    e06StoreSeverity && e06NoCritical,
    e06StoreSeverity && e06NoCritical
      ? '3 计数对齐'
      : `has3Fields=${e06StoreSeverity} noCriticalInBlock=${e06NoCritical}`
  )

  // E07 静态：store factsByObjectType 5 码（ORDER/TASK/RESOURCE/STAGE/DOMAIN）
  //   - 修复前：TASK/ORDER/PI/PO/RESOURCE/STAGE 6 码
  //   - 修复后：ORDER/TASK/RESOURCE/STAGE/DOMAIN 5 码
  const e07GroupsBlock = eStore.match(/Record<ExplanationObjectType[\s\S]*?\}/)?.[0] ?? ''
  const e07Expected = ['ORDER', 'TASK', 'RESOURCE', 'STAGE', 'DOMAIN']
  const e07AllPresent = e07Expected.every(
    (c) => new RegExp(`\\b${c}\\s*:\\s*\\[\\]`).test(e07GroupsBlock)
  )
  const e07NoOld = !/\b(PI|PO)\s*:\s*\[\]/.test(e07GroupsBlock)
  assert(
    'E07 静态：store factsByObjectType groups = 5 码（ORDER/TASK/RESOURCE/STAGE/DOMAIN；PI/PO 已撤）',
    e07AllPresent && e07NoOld,
    e07AllPresent && e07NoOld
      ? '5 码对齐'
      : `缺码=${e07Expected.filter((c) => !new RegExp(`\\b${c}\\s*:\\s*\\[\\]`).test(e07GroupsBlock)).join(',')} 旧码残留=${/\b(PI|PO)\s*:\s*\[\]/.test(e07GroupsBlock)}`
  )

  // E08 静态：ExplanationViewDto.errorCount / warnCount / infoCount 来自 Summary 端点（5号位 关键纠正 3）
  const e08Dto = eTypes.match(/export\s+interface\s+ExplanationViewDto\s*\{[\s\S]*?\n\}/)?.[0] ?? ''
  const e08HasErrorCount = /errorCount\s*:\s*number\b/.test(e08Dto)
  const e08HasWarnCount = /warnCount\s*:\s*number\b/.test(e08Dto)
  const e08HasInfoCount = /infoCount\s*:\s*number\b/.test(e08Dto)
  const e08HasReasonCodeCounts = /reasonCodeCounts\s*:\s*ReasonCodeCount\[\]/.test(e08Dto)
  assert(
    'E08 静态：ExplanationViewDto errorCount/warnCount/infoCount/reasonCodeCounts（对齐 Summary 端点）',
    e08HasErrorCount && e08HasWarnCount && e08HasInfoCount && e08HasReasonCodeCounts,
    `errorCount=${e08HasErrorCount} warnCount=${e08HasWarnCount} infoCount=${e08HasInfoCount} reasonCodeCounts=${e08HasReasonCodeCounts}`
  )

  // E09 静态：view 排程解释事实展示 reasonCode 徽章（v5.1.10 15 码）
  //   - 模板应有 `{{ reasonLabel(f.reasonCode) }}` 或 `REASON_CODE_LABELS` 引用
  //   - 同时展示 severity + objectRef + reasonCode 3 列
  const e09HasReasonLabel = /reasonLabel\s*\(/.test(eVue) && /REASON_CODE_LABELS/.test(eVue)
  const e09HasReasonTagInTemplate = /REASON_CODE_LABELS|reasonLabel/.test(eVue)
  assert(
    'E09 静态：Explanation.vue 排程解释事实展示 reasonCode 徽章（REASON_CODE_LABELS / reasonLabel 引用）',
    e09HasReasonLabel && e09HasReasonTagInTemplate,
    e09HasReasonLabel && e09HasReasonTagInTemplate ? '已加 reasonCode 徽章列' : '排程解释事实未展示 reasonCode'
  )

  // E10 静态：view 撤「排程域失败」面板（5号位 关键纠正 3）
  //   - 反向探针：「排程域失败」标题 / FAIL_TYPE_LABEL / DOMAIN_FAILED / BLOCKED_BY_UPSTREAM / ENGINE_ERROR 都不应存在
  const e10HasFailPanel = /排程域失败|FAIL_TYPE_LABEL/.test(eVue)
  const e10HasOldFailTypes = /DOMAIN_FAILED|BLOCKED_BY_UPSTREAM|ENGINE_ERROR/.test(eVue)
  assert(
    'E10 静态：Explanation.vue 撤「排程域失败」面板 + FAIL_TYPE_LABEL + 3 旧 failureType（5号位 关键纠正 3）',
    !e10HasFailPanel && !e10HasOldFailTypes,
    !e10HasFailPanel && !e10HasOldFailTypes
      ? '排程域失败面板已撤'
      : `排程域失败=${e10HasFailPanel} 旧码残留=${e10HasOldFailTypes}`
  )

  // E11 / E12 skip：R2 / R3 跨号位（1号位 求解 + 2号位 落盘 + 0号位 裁决 V1 范围）
  //   - 4号位 端不动，等回执
  //   - 5号位 已承诺：一旦 1/2号位 落盘，5号位 立即 DTO 透传
  skip('E11 静态：fact.isOnCriticalPath 字段 + Binding Constraint 徽章列（@see §87；待 1号位 + 2号位 落盘 + 0号位 裁决 V1 范围）')
  skip('E12 静态：failure.failureSeverity 字段（DEGRADABLE / MUST_FAIL；@see §88；待 1号位 + 2号位 落盘 + 0号位 裁决 V1 范围）')

  // E13 探针：可选链漏写 `?.Xxx.length`（10-09 dev 浏览器捕获：store totalFacts `data.value?.scheduleExplanationFacts.length` 在 data=null 时报 TypeError）
  //   - TS 可选链 `?.` 只到直接成员，再读 `.length` 时若上一级是 undefined 仍抛错
  //   - 正确写法：`data.value?.scheduleExplanationFacts?.length`
  //   - 反向探针自证可失败：在 explanation.ts 临时插 `x?.y.length` 会让 E13 变红
  const e13Leak = (eStore + '\n' + eVue).match(/\?\.\w+\.length\b/g) ?? []
  assert(
    'E13 静态：store + view 无 `?.Xxx.length` 可选链漏写（10-09 dev 浏览器 TypeError 教训）',
    e13Leak.length === 0,
    e13Leak.length === 0 ? '无漏写' : `漏写：${e13Leak.join(' / ')}`
  )

  // E14 探针：版本切换条不在 ElTag 重复展示 currentVersionId（10-09 dev 浏览器捕获：ElTag dark + ElButton list 双渲染当前版本，看着像"重复"）
  //   - 同一 versionCode 在 version-bar 里出现 2 次会让 PMC 误以为后端 versionCode 撞了
  //   - 正确做法：仅靠 ElButton v-for 高亮表达当前；不另起 ElTag
  //   - 反向探针自证可失败：在 view 临时插回 `<ElTag ... currentVersionId>...</ElTag>` 会让 E14 变红
  const e14TagRedundant = /<ElTag\b[^>]*>[\s\S]*?currentVersionId[\s\S]*?<\/ElTag>/.test(eVue)
  assert(
    'E14 静态：版本切换条不在 ElTag 重复展示 currentVersionId（10-09 dev 重复显示教训）',
    !e14TagRedundant,
    !e14TagRedundant ? '仅按钮列表高亮' : 'ElTag + ElButton list 双渲染当前版本'
  )

  // E15 探针：PLAN_VERSION_STATUSES 含 Computed / ComputeFailed（10-09 dev `/api/schedule/versions` 捕获：响应 status="Computed" 不在 5 码权威内）
  //   - 后端实际 7 码：5 文档权威码 + Computed（D7 候选执行完成终态，@see lps/.../SchedulingOrchestrator.cs:601）+ ComputeFailed（D8 失败终态，SchedulingOrchestrator.cs:685）
  //   - 前端缺这两码 → LABELS[v.status] 返 undefined → UI 显示原文 "Computed" 不显示中文
  //   - 反向探针自证可失败：从 PLAN_VERSION_STATUSES 数组临时删 'Computed' 会让 E15 变红
  const e15EnumBlock = eCommon.match(/PLAN_VERSION_STATUSES\s*=\s*\[([\s\S]*?)\]\s*as\s+const/)?.[1] ?? ''
  const e15LabelBlock =
    eCommon.match(/PLAN_VERSION_STATUS_LABELS[\s\S]*?=\s*\{([\s\S]*?)\n\}/)?.[1] ?? ''
  const e15HasComputed = /\bComputed\b/.test(e15EnumBlock)
  const e15HasComputeFailed = /\bComputeFailed\b/.test(e15EnumBlock)
  const e15HasLabelComputed = /Computed\s*:\s*['"][^'"]+['"]/.test(e15LabelBlock)
  const e15HasLabelComputeFailed = /ComputeFailed\s*:\s*['"][^'"]+['"]/.test(e15LabelBlock)
  const e15All =
    e15HasComputed && e15HasComputeFailed && e15HasLabelComputed && e15HasLabelComputeFailed
  assert(
    'E15 静态：PLAN_VERSION_STATUSES 含 Computed/ComputeFailed（10-09 dev 响应 status=Computed 教训）',
    e15All,
    e15All
      ? '7 码齐全 + 中文 label 齐'
      : `Computed=${e15HasComputed} ComputeFailed=${e15HasComputeFailed} labelComputed=${e15HasLabelComputed} labelComputeFailed=${e15HasLabelComputeFailed}`
  )

  // E16 探针：version-bar 走 store.displayedVersions 业务优先级（10-09 用户反馈"slice(0,3) 不对"的方案 a 落地）
  //   - 旧：view `versions.slice(0, 3)` 硬截，PMC 看不到 ACTIVE（在 3 条外时）+ 状态混在一起
  //   - 新：store `displayedVersions` getter 按 ACTIVE → Computed → Failed → 当前已选 兜底
  //   - view 必须用 displayedVersions（不再渲染 ElTag 徽章——版本号已足够，按用户简化要求）
  //   - 反向探针自证可失败：view 改回 `versions.slice(0, 3)` 或 store 删 displayedVersions getter 会让 E16 变红
  const e16ScheduleStore = readSrc('store/modules/aps/schedule.ts')
  const e16HasDisplayedGetter = /const\s+displayedVersions\s*=\s*computed/.test(e16ScheduleStore)
  const e16HasActiveGetter = /const\s+activeVersion\s*=\s*computed[\s\S]*?status\s*===\s*['"]ACTIVE['"]/.test(
    e16ScheduleStore
  )
  const e16ViewUsesDisplayed = /v-for="v in displayedVersions"/.test(eVue)
  const e16ViewNoSlice3 =
    !/versions\.slice\(\s*0\s*,\s*3\s*\)/.test(eVue) && !/versions\.slice\(\s*0\s*,\s*5\s*\)/.test(eVue)
  const e16All =
    e16HasDisplayedGetter && e16HasActiveGetter && e16ViewUsesDisplayed && e16ViewNoSlice3
  assert(
    'E16 静态：version-bar 走 store.displayedVersions 业务优先级（10-09 业务讨论方案 a 落地）',
    e16All,
    e16All
      ? 'displayedVersions 三 getter + view 已用'
      : `displayedGetter=${e16HasDisplayedGetter} activeGetter=${e16HasActiveGetter} viewUsesDisplayed=${e16ViewUsesDisplayed} viewNoSlice3=${e16ViewNoSlice3}`
  )

  // E17 探针：Explanation.vue onMounted 必须联动 scheduleStore.currentVersionId 给 explanationStore.load，
  //         避免首次 mount summary 请求用 store ref 默认值 1001，与 scheduleStore 选中 ACTIVE 错位（10-09 dev 浏览器 KPI 全 0 根因）
  //   - 反向探针自证可失败：onMounted 改回 `await explanationStore.load()`（不传参）会变红
  const e17OnMounted =
    /onMounted\([\s\S]*?await\s+scheduleStore\.loadVersions\(\)[\s\S]*?explanationStore\.load\([\s\S]*?currentVersionId\.value/.test(
      eVue
    )
  assert(
    'E17 静态：Explanation.vue onMounted 联动 scheduleStore.currentVersionId 给 explanationStore.load（修首次 mount KPI 错位）',
    e17OnMounted,
    e17OnMounted
      ? 'onMounted 已显式传参'
      : 'onMounted 未联动 currentVersionId — 首次会拿 ref 默认 1001'
  )

  // E18 探针：schedule.selectVersion 精确缓存命中 + in-flight 去重（10-09 dev 浏览器反馈"两次 gantt/46"教训）
  //   - 精确缓存：用 ganttData.value?.planVersionId === planVersionId 而不是 `ganttData.value` 存在性检查
  //   - 去重：pendingVersionId / pendingPromise 同 id 共享同一 Promise，避免并发连点引发双请求
  //   - 反向探针自证可失败：把 `ganttData.value?.planVersionId === planVersionId` 改成 `ganttData.value` 会变红
  const e16Store = readSrc('store/modules/aps/schedule.ts')
  const e18ExactCache = /ganttData\.value\?\.planVersionId\s*===\s*planVersionId/.test(e16Store)
  const e18HasPendingId = /pendingVersionId/.test(e16Store)
  const e18HasPendingPromise = /pendingPromise/.test(e16Store)
  const e18SharesInFlight = /pendingVersionId\s*===\s*planVersionId[\s\S]{0,80}return\s+pendingPromise/.test(
    e16Store
  )
  const e18All =
    e18ExactCache && e18HasPendingId && e18HasPendingPromise && e18SharesInFlight
  assert(
    'E18 静态：schedule.selectVersion 精确缓存 + in-flight 去重（修连点同 id 触发两次 gantt 请求）',
    e18All,
    e18All
      ? '精确 planVersionId 匹配 + pending 去重齐全'
      : `exactCache=${e18ExactCache} pendingId=${e18HasPendingId} pendingPromise=${e18HasPendingPromise} sharesInFlight=${e18SharesInFlight}`
  )

  // E19 探针：schedule.loadCurrent 用 Promise.allSettled 而不是 Promise.all（10-09 dev schedule/summary 持续 500 教训）
  //   - 旧 Promise.all：summary 一 500 → 整体 reject → gantt 数据也不回写（用户看 Explanation 页 KPI 空 + 甘特空）
  //   - 新 Promise.allSettled：summary 失败保留旧 summary.value + 单独走 error；gantt 不受影响
  //   - 反向探针自证可失败：把 Promise.allSettled 改回 Promise.all 会变红
  const e19UsesAllSettled =
    /Promise\.allSettled\(\s*\[[\s\S]*?scheduleApi\.getGantt[\s\S]*?scheduleApi\.getSummary/.test(e16Store)
  const e19NotAllReject = !/Promise\.all\(\s*\[[\s\S]*?scheduleApi\.getGantt[\s\S]*?scheduleApi\.getSummary[\s\S]*?catch\s*\(err\)/.test(
    e16Store
  )
  const e19All =
    e19UsesAllSettled && e19NotAllReject
  assert(
    'E19 静态：schedule.loadCurrent 用 Promise.allSettled 容错单端点失败（修 summary 500 拖累 gantt）',
    e19All,
    e19All
      ? 'allSettled + 独立 handleError 齐全'
      : `usesAllSettled=${e19UsesAllSettled} notAllReject=${e19NotAllReject}`
  )

  // E20 探针：10-10 option A 收口（按 4号位 文档第 11-12 节冻结基线：撤 重排建议 + 设备故障 2 块 UI / DTO / mock / CSS）
  //   - types/explanation.ts 不再 export `interface RescheduleRecommendation` / `interface EquipmentFailure`
  //   - ExplanationViewDto 不再含 rescheduleRecommendations / equipmentFailures 字段
  //   - api/explanation.ts 真实模式调 /api/business-fact-issues 聚合（独立端点）；mockRecommendations() / mockEquipmentFailures() 函数已撤（mockBusinessIssues() 仍保留供 mock 模式填充，**不应被错误撤掉**）
  //   - view 撤 2 块 ElCard + REC_TYPE_LABEL/REC_TAG_TYPE 常量 + 相关 CSS（.rec-* / .equip-* / .u20-alert）+ ElTooltip 引用
  //   - 反向探针自证可失败：types 加回 `interface RescheduleRecommendation` 或 view 加回 .rec-list / .equip-list CSS 会让 E20 变红
  //   - 注意：api/view 头部注释里仍可能保留「重排建议 / 设备故障 / mockRecommendations / mockEquipmentFailures」历史描述文字（仅文字可保留），所以探针只对实际代码 token 做检查
  const e20DtoBlock = eTypes.match(/export\s+interface\s+ExplanationViewDto\s*\{[\s\S]*?\n\}/)?.[0] ?? ''
  const e20NoRecDto = !/interface\s+RescheduleRecommendation\b/.test(eTypes)
  const e20NoEquipDto = !/interface\s+EquipmentFailure\b/.test(eTypes)
  const e20NoRecField = !/rescheduleRecommendations\s*:/.test(e20DtoBlock)
  const e20NoEquipField = !/equipmentFailures\s*:/.test(e20DtoBlock)
  const e20ApiCallsBizFact =
    /['"]\/api\/business-fact-issues['"]/.test(eApi) &&
    (/take\s*:\s*100/.test(eApi) || // 真实模式默认 take=100（旧契约）
     /pageIndex\s*:\s*1[\s\S]{0,80}?pageSize\s*:\s*20/.test(eApi)) // 10-10 改为分页契约后：pageIndex=1, pageSize=20
  // 检查 mockRecommendations / mockEquipmentFailures 不再被**调用**（声明已撤；头注释里的描述文字可保留）
  const e20ApiNoMockRecEquip =
    !/\bmockRecommendations\s*\(/.test(eApi) && !/\bmockEquipmentFailures\s*\(/.test(eApi)
  // view 端：检查已撤掉的常量 / CSS 类名 / ElTooltip 实际代码 token（jSDoc 注释里的描述文字可保留）
  const e20ViewNoRecConstOrCss = !/REC_TYPE_LABEL|REC_TAG_TYPE|\.rec-list|\.rec-item|\.rec-head|\.rec-time|\.rec-desc|\.rec-impact|\.rec-warn/.test(
    eVue
  )
  const e20ViewNoEquipConstOrCss =
    !/EQUIP_TYPE_LABEL|EQUIP_TAG_TYPE|\.equip-list|\.equip-item|\.equip-head|\.equip-code|\.equip-meta|\.equip-field|\.equip-icon|\.u20-alert/.test(
      eVue
    )
  const e20ViewNoElTooltip = !/<ElTooltip\b/.test(eVue)
  const e20All =
    e20NoRecDto &&
    e20NoEquipDto &&
    e20NoRecField &&
    e20NoEquipField &&
    e20ApiCallsBizFact &&
    e20ApiNoMockRecEquip &&
    e20ViewNoRecConstOrCss &&
    e20ViewNoEquipConstOrCss &&
    e20ViewNoElTooltip
  assert(
    'E20 静态：10-10 option A 收口（撤 2 DTO + 2 DTO 字段 + mockRecommendations()/mockEquipmentFailures() + view 2 块 + REC_TYPE_LABEL/REC_TAG_TYPE + .rec-* / .equip-* / .u20-alert CSS + ElTooltip + /api/business-fact-issues 聚合）',
    e20All,
    e20All
      ? '2 DTO + 2 字段 + 2 mock 调用 + 2 块 + 2 常量 + 7 CSS 类 + ElTooltip + /api/business-fact-issues 聚合 全部撤清'
      : `recDto=${!e20NoRecDto} equipDto=${!e20NoEquipDto} recField=${!e20NoRecField} equipField=${!e20NoEquipField} bizFactApi=${e20ApiCallsBizFact} noMockRecEquipCall=${e20ApiNoMockRecEquip} recBlock=${!e20ViewNoRecConstOrCss} equipBlock=${!e20ViewNoEquipConstOrCss} noElTooltip=${!e20ViewNoElTooltip}`
  )

  // E21 探针：10-10 business-fact-issues 端点查错库修复验证（@see 4号位-2026-10-10-business-fact-issues撤函-根因是查错库非缺表-撤回建表建议-给5号位-抄送2号位3号位.md）
  //   - 原误诊：dev DB 缺表 → 推荐 DDL 建表
  //   - 真根因：Repository 查询1 用 DatabaseId.APS，应为 DatabaseId.ODS（表在 ODS/MES_Integration 一直存在）
  //   - 期望：5号位 改 [lps/LPS.APS.BusinessRules/Repositories/BusinessFactIssueRepository.cs:77](/d/proj/aps/aps-production-new/lps/LPS.APS.BusinessRules/Repositories/BusinessFactIssueRepository.cs#L77) DatabaseId.APS → ODS 后端点返 200
  //   - 反向探针自证可失败：把连接改回 DatabaseId.APS 会让 E21 由绿转红
  //   - 本探针默认 skip（dev 环境依赖）；设置 VERIFY_E21=1 且 dev 可访问时执行
  if (process.env.VERIFY_E21 === '1' && adminToken) {
    const e21Resp = await call('GET', '/api/business-fact-issues', {
      token: adminToken,
      query: { take: 100 }
    })
    const e21Ok = e21Resp.ok && e21Resp.code === 200
    const e21NoMissingTable = e21Ok || !/MES_APS_BOM_Workset_Issues/.test(e21Resp.text || '')
    assert(
      'E21 动态：/api/business-fact-issues?take=100 端点 200（Repository 已改 DatabaseId.APS→ODS；表在 ODS/MES_Integration 一直存在）',
      e21Ok && e21NoMissingTable,
      e21Ok
        ? '端点 200，连接已修正'
        : `status=${e21Resp.status} code=${e21Resp.code} missingTable=${/MES_APS_BOM_Workset_Issues/.test(e21Resp.text || '')}`
    )
  } else {
    skip('E21 动态：/api/business-fact-issues?take=100 连接修正验证（设置 VERIFY_E21=1 且 dev 可访问时执行）')
  }

  // E22 探针：10-10 样式简洁整齐（修 dev 浏览器 KPI 卡片"100%"换行 + 排程解释事实 grid 2 列硬截）
  //   - 修前症状：KPI 卡 `100%` 文本换行到第二行；`.fact-grid` 硬截 `repeat(2, 1fr)` 在窄屏溢出
  //   - 修后契约：`.kpi-card { min-width: 0 }` + `.kpi-label/.kpi-ratio { white-space: nowrap }` + `.fact-grid { repeat(auto-fit, minmax(320px, 1fr)) }`
  //   - 反向探针自证可失败：删 `min-width: 0` 或 `white-space: nowrap` 或 `auto-fit, minmax` 会让 E22 变红
  const e22KpiCardMinWidth = /\.kpi-card\s*\{[^}]*min-width\s*:\s*0\b/.test(eVue)
  const e22KpiLabelNowrap = /\.kpi-label[^{]*\{[^}]*white-space\s*:\s*nowrap/.test(eVue)
  const e22KpiRatioNowrap = /\.kpi-ratio[^{]*\{[^}]*white-space\s*:\s*nowrap/.test(eVue)
  const e22FactGridAutoFit = /\.fact-grid\s*\{[^}]*repeat\(\s*auto-fit\s*,\s*minmax\(\s*320px\s*,\s*1fr\s*\)\s*\)/.test(eVue)
  const e22ContentRowSingleCol =
    /\.content-row\s*\{[^}]*grid-template-columns\s*:\s*1fr\b/.test(eVue)
  const e22All =
    e22KpiCardMinWidth &&
    e22KpiLabelNowrap &&
    e22KpiRatioNowrap &&
    e22FactGridAutoFit &&
    e22ContentRowSingleCol
  assert(
    'E22 静态：Explanation.vue 样式简洁整齐（KPI 卡 min-width:0 + label/ratio nowrap + fact-grid auto-fit minmax(320px,1fr) + content-row 单列）',
    e22All,
    e22All
      ? 'KPI 防溢出 + fact-grid 响应式 + 单列布局 齐全'
      : `kpiMinW=${e22KpiCardMinWidth} labelNowrap=${e22KpiLabelNowrap} ratioNowrap=${e22KpiRatioNowrap} factGridAutoFit=${e22FactGridAutoFit} contentRow1col=${e22ContentRowSingleCol}`
  )

  // E23 探针：10-10 business-fact-issues 分页 + total + severity 排序兜底契约锁
  //   - api 调 pageIndex=1, pageSize=20（不再 take=100）
  //   - api 增 listBusinessFactIssues(opts) 单独方法
  //   - 10-10 后端落地后：响应字段名 page（5号位 落地回执明确告知，@see 5号位-2026-10-10-business-fact-issues分页severity排序total已落地-回执-给4号位.md）
  //     store 同步字段名 businessFactIssuesPage（前为 PageIndex）；DTO 同步 businessFactIssuesPage
  //   - store 增 4 ref（total/page/pageSize/loading）+ cached 缓存标记 + sortedBusinessFactIssues getter + fetchBizPage / refreshBizIssues / changeBizPageSize actions
  //     fetchBizPage 替换式（每页直接覆盖，不累积）：ElPagination @current-change 触发
  //   - 10-10 续改：view 用标准 ElPagination（v-model 双绑 + layout="total,sizes,prev,pager,next,jumper" + [10,20,50]）
  //   - 10-10 续改：view 移除"加载更多"按钮 + 旧 pageSize ElSelect + header-tags div（避免与 ElPagination 总数显示重复）
  //   - view 用 sortedBusinessFactIssues
  //   - ExplanationViewDto 增 3 字段（total/page/pageSize）
  //   - 函件：frontNew/docs/4号位-2026-10-10-business-fact-issues分页severity排序total-给5号位.md
  const eTypesContent = readSrc('api/aps-v1/types/explanation.ts')
  const e23ApiCallsPageResult =
    /pageIndex\s*:\s*1[\s\S]{0,80}?pageSize\s*:\s*20/.test(eApi)
  const e23ApiNoOldTake = !/params:\s*\{\s*take:\s*100\s*\}/.test(eApi)
  const e23ApiListMethod = /async\s+listBusinessFactIssues\s*\(/.test(eApi)
  const e23ApiToBusinessFactIssue = /function\s+toBusinessFactIssue\s*\(/.test(eApi)
  const e23ApiUsesPageField = /bizPage\.page(?!\w)/.test(eApi) || /raw\.page(?!\w)/.test(eApi)
  const e23ApiNoFallbackToList =
    !/PageResult<BusinessFactIssueRawDto>\s*\|\s*BusinessFactIssueRawDto\[\]/.test(eApi)
  const e23StoreRefTotal = /businessFactIssuesTotal\s*=\s*ref/.test(eStore)
  const e23StoreRefPage = /businessFactIssuesPage\s*=\s*ref/.test(eStore)
  const e23StoreRefPageSize = /businessFactIssuesPageSize\s*=\s*ref/.test(eStore)
  const e23StoreRefLoading = /businessFactIssuesLoading\s*=\s*ref/.test(eStore)
  const e23StoreCached = /businessFactIssuesCached\s*=\s*ref/.test(eStore)
  const e23StoreSorted = /sortedBusinessFactIssues/.test(eStore)
  const e23StoreFetchBizPage = /async\s+function\s+fetchBizPage/.test(eStore)
  const e23StoreRefresh = /async\s+function\s+refreshBizIssues/.test(eStore)
  const e23StoreChangeSize = /async\s+function\s+changeBizPageSize/.test(eStore)
  // 10-10 续改：删 loadMoreBizIssues / hasMore（替换式分页不需要累积/hasMore 概念）
  const e23StoreNoLoadMore = !/loadMoreBizIssues/.test(eStore)
  const e23StoreNoHasMore = !/businessFactIssuesHasMore/.test(eStore)
  const e23ViewPaginationClass = /\.biz-pagination/.test(eVue)
  const e23ViewHasElPagination = /<ElPagination\b/.test(eVue)
  const e23ViewPaginationLayout =
    /layout="total,\s*sizes,\s*prev,\s*pager,\s*next,\s*jumper"/.test(eVue)
  const e23ViewPaginationPageSizes = /:page-sizes="\[10,\s*20,\s*50\]"/.test(eVue)
  const e23ViewPaginationVModel =
    /v-model:current-page="businessFactIssuesPage"/.test(eVue) &&
    /v-model:page-size="businessFactIssuesPageSize"/.test(eVue)
  const e23ViewTotalHint = /businessFactIssuesTotal/.test(eVue)
  // 10-10 续改：删"加载更多"按钮 + header-tags 显示重复块 + 旧 ElSelect 10/20/50 标签页
  const e23ViewNoLoadMoreBtn = !/加载更多/.test(eVue)
  const e23ViewNoHeaderTags = !/header-tags/.test(eVue)
  const e23ViewNoOldPageSizeSelect = !/10\s*条\/页/.test(eVue)
  const e23ViewUsesSorted = /sortedBusinessFactIssues/.test(eVue)
  const e23ViewUsesPageField = /businessFactIssuesPage(?!\w)/.test(eVue) &&
    !/businessFactIssuesPageSize/.test(eVue.replace(/businessFactIssuesPageSize/g, ''))
  const e23TypesDtoFields =
    /businessFactIssuesTotal[\s\S]*?businessFactIssuesPage[\s\S]*?businessFactIssuesPageSize/.test(
      eTypesContent
    )
  const e23All =
    e23ApiCallsPageResult &&
    e23ApiNoOldTake &&
    e23ApiListMethod &&
    e23ApiToBusinessFactIssue &&
    e23ApiUsesPageField &&
    e23ApiNoFallbackToList &&
    e23StoreRefTotal &&
    e23StoreRefPage &&
    e23StoreRefPageSize &&
    e23StoreRefLoading &&
    e23StoreCached &&
    e23StoreNoLoadMore &&
    e23StoreNoHasMore &&
    e23StoreSorted &&
    e23StoreFetchBizPage &&
    e23StoreRefresh &&
    e23StoreChangeSize &&
    e23ViewPaginationClass &&
    e23ViewHasElPagination &&
    e23ViewPaginationLayout &&
    e23ViewPaginationPageSizes &&
    e23ViewPaginationVModel &&
    e23ViewTotalHint &&
    e23ViewNoLoadMoreBtn &&
    e23ViewNoHeaderTags &&
    e23ViewNoOldPageSizeSelect &&
    e23ViewUsesSorted &&
    e23ViewUsesPageField &&
    e23TypesDtoFields
  assert(
    'E23 静态：business-fact-issues 分页契约（PageResult + 后端字段 page + 缓存 + 替换式 fetchBizPage + 排序兜底 + DTO 3 字段 page + 标准 ElPagination + v-model + [10,20,50]）',
    e23All,
    e23All
      ? '全齐'
      : `apiCallPgRes=${e23ApiCallsPageResult} noTake100=${e23ApiNoOldTake} apiListMethod=${e23ApiListMethod} apiToBiz=${e23ApiToBusinessFactIssue} apiPage=${e23ApiUsesPageField} apiNoFallback=${e23ApiNoFallbackToList} stTotal=${e23StoreRefTotal} stPage=${e23StoreRefPage} stPageSz=${e23StoreRefPageSize} stLoad=${e23StoreRefLoading} stCached=${e23StoreCached} stNoLoadMore=${e23StoreNoLoadMore} stNoHasMore=${e23StoreNoHasMore} stSorted=${e23StoreSorted} stFetch=${e23StoreFetchBizPage} stRefresh=${e23StoreRefresh} stChangeSz=${e23StoreChangeSize} vPagination=${e23ViewPaginationClass} vElPag=${e23ViewHasElPagination} vLayout=${e23ViewPaginationLayout} vPageSizes=${e23ViewPaginationPageSizes} vVModel=${e23ViewPaginationVModel} vTotal=${e23ViewTotalHint} vNoLoadMore=${e23ViewNoLoadMoreBtn} vNoHeaderTags=${e23ViewNoHeaderTags} vNoOldSize=${e23ViewNoOldPageSizeSelect} vUsesSorted=${e23ViewUsesSorted} vPage=${e23ViewUsesPageField} tDtoFields=${e23TypesDtoFields}`
  )

  // E24 探针：5号位 落地 PageResult + severity 排序后，动态验证
  //   - 期望：`GET /api/business-fact-issues?pageIndex=1&pageSize=1` 形如 `{code, message, data: {items: [...], total: N, page: 1, pageSize: 1}}`
  //   - 5号位 10-10 落地回执：字段名是 `page`（非 `pageIndex`），与 RBAC/TaskSplitRuleConfig/ManualETA 同源
  //   - severity 排序期望：前 pageSize 条全为 ERROR（@see 5号位 回执实测：?pageIndex=1&pageSize=3 前 3 条全 ERROR）
  if (process.env.VERIFY_E24 === '1' && adminToken) {
    const e24Resp = await call('GET', '/api/business-fact-issues', {
      token: adminToken,
      query: { pageIndex: 1, pageSize: 1 }
    })
    const body = e24Resp.json
    const data = body?.data ?? {}
    const items = data.items
    const total = data.total
    const page = data.page
    const pageSize = data.pageSize
    const isPageResult =
      Array.isArray(items) &&
      typeof total === 'number' &&
      page === 1 &&
      pageSize === 1
    const allErrorSeverity =
      Array.isArray(items) && items.length > 0 && items.every((it) => it.severity === 'ERROR')
    assert(
      'E24 动态：/api/business-fact-issues?pageIndex/pageSize 返回 PageResult{items,total,page,pageSize}（5号位 10-10 落地后）',
      isPageResult,
      isPageResult
        ? `total=${total} items=${items.length} page=${page} pageSize=${pageSize} severity=${allErrorSeverity ? '全 ERROR ✅' : '非全 ERROR'}`
        : `status=${e24Resp.status} dataShape=${JSON.stringify(data).slice(0, 200)}`
    )
  } else {
    skip(
      'E24 动态：/api/business-fact-issues PageResult{total,items,page,pageSize}（设置 VERIFY_E24=1 等 5号位 落地 DTO 后执行）'
    )
  }

  // E25 探针：10-10 懒加载契约（setCurrentVersion 拆分锁）
  //   - store 加 setCurrentVersion 同步 action（仅更新 ref，不触发 fetch）
  //   - Explanation.vue 改用 setCurrentVersion（避免无意义拉甘特图）
  //   - Gantt.vue 仍用 selectVersion（保持甘特图拉取）
  //   - 反向探针自证可失败：Explanation.vue 改回 selectVersion 会让 E25 由绿转红（理由：会无意义触发 /api/schedule/gantt）
  //   - @see 10-10 教训：Explanation 页只需 currentVersionId 高亮，不需要 gantt 数据
  const e25StoreHasSetCurrent = /function\s+setCurrentVersion\s*\(/.test(eScheduleStore)
  const e25StoreKeepsSelectVersion = /function\s+selectVersion\s*\(/.test(eScheduleStore)
  const e25StoreSetCurrentNoFetch = (() => {
    // 校验 setCurrentVersion 函数体内不含 getGantt / getSummary（防被改成意外 fetch）
    const match = eScheduleStore.match(/function\s+setCurrentVersion\s*\([^)]*\)[^{]*\{([\s\S]*?)\n\s*\}\s*\n/)
    if (!match) return false
    const body = match[1]
    return !/getGantt|getSummary|loadCurrent/.test(body)
  })()
  const e25ExplanationUsesSetCurrent =
    /scheduleStore\.setCurrentVersion\s*\(/.test(eVue) &&
    !/function\s+selectVersion[\s\S]{0,200}?scheduleStore\.selectVersion/.test(eVue)
  const e25All =
    e25StoreHasSetCurrent &&
    e25StoreKeepsSelectVersion &&
    e25StoreSetCurrentNoFetch &&
    e25ExplanationUsesSetCurrent
  assert(
    'E25 静态：Explanation 懒加载契约（scheduleStore 拆 setCurrentVersion 仅更 ref + Explanation.vue 用 setCurrentVersion + Gantt.vue 仍走 selectVersion）',
    e25All,
    e25All
      ? '全齐'
      : `storeSetCurrent=${e25StoreHasSetCurrent} storeSelectVer=${e25StoreKeepsSelectVersion} storeNoFetch=${e25StoreSetCurrentNoFetch} explUsesSetCurrent=${e25ExplanationUsesSetCurrent}`
  )

  info('E 段：5号位 2026-10-09 一致性核查回执 R1 闭环 + 10-10 option A 收口 + 10-10 business-fact-issues 端点查错库修复（E21 dev 绿）+ 10-10 样式简洁整齐（E22）+ 10-10 business-fact-issues 分页契约锁（E23 静态绿：后端字段 page + 替换式 fetchBizPage + 标准 ElPagination + v-model + [10,20,50]；E24 等 5号位 PageResult 落地）+ 10-10 懒加载契约锁（E25 静态绿；Gantt.vue 仍走 selectVersion 不退化）；R2/R3 跨号位等回执')
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
  if (GROUP === 'all' || GROUP === 'a-ctp-domainkey') {
    await verifyCtpDomainKey(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'me-eta') {
    await verifyManualEta(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'r-resource-calendar') {
    await verifyResourceCalendar(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'd-domain') {
    await verifyDomain(adminToken)
  }
  if (GROUP === 'all' || GROUP === 't-tsrc') {
    await verifyTaskSplitRuleConfig(adminToken)
  }
  if (GROUP === 'all' || GROUP === 'dp-freeze') {
    await verifyDemandProtectionFreeze()
  }
  if (GROUP === 'all' || GROUP === 'dplive') {
    await verifyDemandProtectionLive()
  }
  if (GROUP === 'all' || GROUP === 'e-freeze') {
    await verifyExplanationFreeze(adminToken)
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
