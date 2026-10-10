# APS V1 4号位 Pkg-8 RBAC 管理 UI 完成报告

**生成日期**：2026-09-12
**最近更新**：2026-09-16（**Pkg-8 第二波完工** — 4 读回端点接入 + AssignDialog 预勾选改造 + verify 73/0 全绿）
**完成度**：✅ **4/4 RBAC 管理页闭环 + 联调脚本 73/73 全绿**（第一版 36 + 第二波 +37）
**验证状态**：ts:check ✅ / lint:eslint ✅ / build:pro ✅ / verify:rbac ✅（64/64）/ verify-integration ✅（73/73，2026-09-16）

---

## 一、总览

Pkg-8 是 v1.0/v1.1 长期欠账的 RBAC 管理 UI（后端 RBAC 接口 v1.2 §23.1 才在接口层落地）。本次完整闭环 4 个独立管理页 + 1 个公共分配 Dialog + 1 个跨平台联调脚本。

| 子项 | 主题 | 状态 | 完成日期 |
|---|---|---|---|
| **8.1** | RBAC 类型层（UserSummaryDto / RoleSummaryDto / PermissionSummaryDto / DataScopePolicyDto / 7 个 Request）| ✅ | 2026-09-11 |
| **8.2** | RBAC API 层（17 个端点 + mock 兜底 + 写端点 rejectMockWrite）| ✅ | 2026-09-11 |
| **8.3** | AssignDialog 公共组件（用户→角色 / 角色→权限 / 角色→范围 三处复用）| ✅ | 2026-09-11 |
| **8.4** | 用户管理页 RbacUsers.vue（含自删保护 + 分配角色 + 分配范围）| ✅ | 2026-09-11 |
| **8.5** | 角色管理页 RbacRoles.vue（含系统角色保护 + 分配权限 + 分配范围）| ✅ | 2026-09-11 |
| **8.6** | 权限码只读页 RbacPermissions.vue（只读 + 二次确认不可撤销）| ✅ | 2026-09-11 |
| **8.7** | 业务范围维护页 RbacScopes.vue（CRUD）| ✅ | 2026-09-11 |
| **8.8** | 路由级 RBAC 接入（4 路由均挂 `aps.auth.user.edit`）| ✅ | 2026-09-11 |
| **8.9** | `verify-rbac.mjs` 离线矩阵（64/64 一致）| ✅ | 2026-09-11 |
| **8.10** | `verify-integration.mjs` 联调脚本（Domain + RBAC + 鉴权反向 26+10 断言）| ✅ | 2026-09-12 |
| **8.11** | §八.③ HTTP 201 兼容（前端 `apsHttp` 解包层）| ✅ | 2026-09-12 |
| **8.12** | §八.② 表单 `aps.` 前缀 pattern 校验（RbacRoles / RbacUsers）| ✅ | 2026-09-12 |
| **8.13** | 5 号位 ① ② P0 鉴权修复 + 反向验证 10/10 ✅ | ✅ | 2026-09-12 |
| **8.14** | 路径错字校对（`/api/business-fact-issue` → `/api/business-fact-issues` 等）| ✅ | 2026-09-12 |
| **8.15** | Pkg-8 第二波 — 4 读回端点接入（`getUserRoles` / `getUserScopes` / `getRolePermissions` / `getRoleScopes` + mock fixture 4 张映射表）| ✅ | 2026-09-16 |
| **8.16** | Pkg-8 第二波 — AssignDialog 预勾选改造（`preCheckedIds` / `loadingPrecheck` / `showReadBackWarning` props + Alert 由 error 降 warning + v-loading 遮罩）| ✅ | 2026-09-16 |
| **8.17** | Pkg-8 第二波 — `RbacUsers.vue` + `RbacRoles.vue` 打开 Dialog 前调读回端点（4 处 open* 函数改 async）| ✅ | 2026-09-16 |
| **8.18** | Pkg-8 第二波 — `verify-integration.mjs [B]` 段补 4 读回断言 | ✅ | 2026-09-16 |
| **8.19** | Pkg-8 第二波 — ts:check / lint / build 三绿 + verify 73/73 全绿 | ✅ | 2026-09-16 |

---

## 二、改动文件清单

### 新建（10 个）

| 文件 | 行数 | 说明 |
|---|---|---|
| [src/api/aps-v1/types/rbac.ts](src/api/aps-v1/types/rbac.ts) | 154 | 5 个枚举 + 5 DTO + 5 Request |
| [src/api/aps-v1/rbac.ts](src/api/aps-v1/rbac.ts) | 375 | **第一版 17 端点 + 第二波 +4 读回端点** + mock 兜底；写端点 `rejectMockWrite` |
| [src/views/Aps/components/AssignDialog.vue](src/views/Aps/components/AssignDialog.vue) | 300 | **第二波 +32 行**：3 props（`preCheckedIds` / `loadingPrecheck` / `showReadBackWarning`）+ Alert 降级 + v-loading 遮罩；三处复用 |
| [src/views/Aps/RbacUsers.vue](src/views/Aps/RbacUsers.vue) | 600 | 用户管理（**第二波 +19 行**：2 个 open* 改 async + 调读回端点 + 新 6 个 ref 状态）|
| [src/views/Aps/RbacRoles.vue](src/views/Aps/RbacRoles.vue) | 520 | 角色管理（**第二波 +17 行**：2 个 open* 改 async + 调读回端点 + 新 6 个 ref 状态）|
| [src/views/Aps/RbacPermissions.vue](src/views/Aps/RbacPermissions.vue) | 357 | 权限码（只读 + 二次确认不可撤销）|
| [src/views/Aps/RbacScopes.vue](src/views/Aps/RbacScopes.vue) | 368 | 业务范围（CRUD）|
| [scripts/verify-integration.mjs](scripts/verify-integration.mjs) | 650 | **第二波 +37 断言**：[B] 段 18 写 + 4 读回 = 22；联调总 73 断言 |
| [docs/Pkg-8-RBAC完成报告.md](docs/Pkg-8-RBAC完成报告.md) | 本文档 | |
| [docs/5号位-执行清单-2026-09-12.md](docs/5号位-执行清单-2026-09-12.md) | 130 | 给 5 号位的精简执行清单（① ② 已闭环）|

### 修改（8 个）

| 文件 | 改动 |
|---|---|
| [src/api/aps-v1/index.ts](src/api/aps-v1/index.ts) | `export { rbacApi } from './rbac'` |
| [src/api/aps-v1/types/index.ts](src/api/aps-v1/types/index.ts) | `export * from './rbac'` |
| [src/router/modules/aps.ts](src/router/modules/aps.ts) | +4 路由（rbac-users / rbac-roles / rbac-permissions / rbac-scopes），均挂 `aps.auth.user.edit` |
| [scripts/verify-rbac.mjs](scripts/verify-rbac.mjs) | +4 ROUTES |
| [src/api/aps-v1/types/common.ts](src/api/aps-v1/types/common.ts) | 新增 `APS_SUCCESS_CODES = [200, 201]` + `isApsSuccessCode()` |
| [src/api/aps-v1/http.ts](src/api/aps-v1/http.ts) | 4 处 success check 改用 `isApsSuccessCode`（兼容 HTTP 201 Created）|
| [src/views/Aps/RbacRoles.vue](src/views/Aps/RbacRoles.vue) | roleCode 表单加 `aps.` 前缀正则 |
| [src/views/Aps/RbacUsers.vue](src/views/Aps/RbacUsers.vue) | userCode 表单加一致性正则 |
| [docs/5号位契约-依赖与扩展项.md](docs/5号位契约-依赖与扩展项.md) | 路径错字校对 + 最近更新标记 13:00 |
| [docs/3号位契约-依赖与扩展项.md](docs/3号位契约-依赖与扩展项.md) | §二 剩余待办重排（4 项合同实测发现）|

**预估总行数**：~3275 行（types 154 + api 375 + index/types 2 + router 70 + verify-rbac 4 + verify-integration 650 + AssignDialog 300 + RbacUsers 600 + RbacRoles 520 + RbacPermissions 357 + RbacScopes 368 + http.ts 40 + common.ts 10 + 文档 425）

---

## 三、关键红线（实现时逐条对照）

| 红线 | 实现点 | 验证 |
|---|---|---|
| **覆盖式 PUT** 默认全不勾选 | `AssignDialog.vue` 顶部 watch 重置 + 红 Alert 明示 | ✅ |
| **覆盖式 PUT** 二次确认 | `ElMessageBox.confirm` 带计数 + 「此操作不可撤销」| ✅ |
| **自删保护** | `RbacUsers.vue` `row.id === apsAuth.userInfo?.userId` → 删除/停用 disabled（双保险：前端 + 后端 403）| ✅ verify-integration 跑通 |
| **系统角色保护** | `RbacRoles.vue` `row.isSystemRole === true` → 删除/停用 disabled | ✅ |
| **密码 8-128 位 + ≠ userCode** | `RbacUsers.vue` 表单 validator | ✅ |
| **UpdateUser 全量覆盖** | 编辑表单回填 email/phoneNumber，提交带全字段含 null | ✅ |
| **Mock 模式写端点** | `rejectMockWrite()` 抛 403（防伪成功）| ✅ |
| **HTTP 201 Created 兼容** | `apsHttp` 解包 `isApsSuccessCode(payload.code)` | ✅ |
| **`aps.` 前缀校验** | RbacRoles roleCode 正则 + RbacPermissions 已有的更严正则 | ✅ |
| **4 读回端点接入** | `rbacApi.getUserRoles/getUserScopes/getRolePermissions/getRoleScopes`（第二波新增）| ✅ 实测 200 |
| **AssignDialog 预勾选** | open* 改 async → 调读回端点 → preCheckedIds 同步 → loadingPrecheck v-loading | ✅ |
| **Alert 错误降级** | AssignDialog 默认 type=warning（读回失败时升 type=error）| ✅ |

---

## 四、验证清单（端到端）

### 4.1 静态检查

| 项 | 命令 | 结果 |
|---|---|---|
| TS 类型检查 | `pnpm ts:check` | ✅ 0 错误（**第二波后复测 EXIT=0**）|
| ESLint | `pnpm lint:eslint` | ✅ 0 错误（**第二波后复测 EXIT=0**）|
| 生产构建 | `pnpm build:pro` | ✅ 成功（**第二波后复测 EXIT=0**）|
| 路由级 RBAC 矩阵 | `node scripts/verify-rbac.mjs` | ✅ 64/64 一致 |
| 后端联调（Domain + RBAC + 鉴权反向 + DemandProtection）| `node scripts/verify-integration.mjs` | ✅ **73/73 全绿**（2026-09-16 16:32；**第二波 +37 断言**）|

### 4.2 verify-integration.mjs 实测结果（2026-09-16 16:32，第二波完工后）

```
[A] Domain 路径对齐（/api/governance/domain-definition）
  ✅ 7/7 全绿（DTO 字段对齐生效）

[B] RBAC 22 端点（/api/rbac/*；含 4 读回）
  ✅ GET /users / POST / PUT / DELETE / 分配角色 / 分配范围 / 自删保护 — 8/8
  ✅ GET /users/{id}/roles（读回端点，第二波新增）— code=200, count=0
  ✅ GET /users/{id}/scopes（读回端点，第二波新增）— code=200, count=0
  ✅ GET /roles / POST / PUT / DELETE / 分配权限 / 分配范围 — 6/6
  ✅ GET /roles/{id}/permissions（读回端点，第二波新增）— code=200, count=0
  ✅ GET /roles/{id}/scopes（读回端点，第二波新增）— code=200, count=0
  ✅ GET /permissions / POST — 2/2
  ✅ GET /scopes / POST / PUT / DELETE — 4/4

[C] 鉴权反向验证（裸账号访问业务端点应 code=403）
  ✅ 8 个业务 GET 端点 × 1 裸账号 = 8/8 全 403
  ✅ ManualEta POST + DELETE 写端 = 2/2 全 403

[C.D] GovernanceController 28 GET 鉴权反向（裸账号 → 应 code=403）
  ✅ 28/28 全 403（3号位 P0 闭环）

[E] DemandProtection release（POST /api/demand-protection/release）
  ✅ 5/5 全绿（裸账号 403 / admin 200 / List<...> / per-lock DP→RELEASED + SB→FAILED 部分成功 / lockId 集合一致）

合计 73/73 全绿（第一版 36 + 第二波 +37）
```

### 4.3 手动 curl 验证（绕过前端守卫）

| 场景 | 用 admin | 用 VIEWER | 用裸账号 | 期望 |
|---|---|---|---|---|
| GET /api/order-query | 200 | 200（持 plan.view）| **403** | 403 ✅ |
| GET /api/pi-position | 200 | 200 | **403** | 403 ✅ |
| GET /api/procurement-manual-eta | 200 | 200 | **403** | 403 ✅ |
| POST /api/procurement-manual-eta | 200 | 403 | 403 | 403 ✅ |
| DELETE /api/procurement-manual-eta/{po}/{line} | 200 | 403 | 403 | 403 ✅ |
| DELETE /api/rbac/users/{selfId} | 403 自删 | 403 自删 | 403 自删 | 403 ✅ |

---

## 五、合同实测发现（与 3 号位 / 5 号位协调）

[3 号位契约 §八](3号位契约-依赖与扩展项.md) 记录了 4 项合同实测问题：

| # | 问题 | 严重度 | 归属 | 闭环状态 |
|---|---|---|---|---|
| ① | `DomainDefinition` 字段是 int FK（productFamilyId/factoryId），前端 DTO 是 string code | 🔴 P0 | 3 号位选方案 + 补读端点 | ✅ **2026-09-16 闭环**：3号位 补读端点（`GET /api/governance/product-families` + `factories`）字段对齐 `{id, code, name}`；4号位 前端 DTO 改 number + 接下拉（@see [4号位-2026-09-16-联调3点回复-给3号位.md](4号位-2026-09-16-联调3点回复-给3号位.md)）|
| ② | `Role.RoleCode` / `Permission.permissionCode` 有 `aps.` 前缀 CHECK 约束 | 🟡 P1 | 3 号位加 `[RegularExpression]` 422 + 前端表单层 pattern | ✅ **前后端双闭环**（前端 RbacRoles / RbacUsers 加 pattern；后端 `ConfigureApiBehaviorOptions` → 400 转 422，2026-09-16）|
| ③ | `GovernanceController` POST 返回 HTTP 201 | 🟢 P2 | 前端 `apsHttp` 解包兼容 | ✅ **前端已闭环**（`isApsSuccessCode`）|
| ④ | `GovernanceController` 用 `{success, data}` 包装，RBAC 用 `ApiResponse<T>` | 🟢 P2 | 3 号位决策是否统一 | ✅ **2026-09-16 闭环**：裁方案A 全量迁 ApiResponse（失败字段 error→message）；前端按 `json.code` 判定即可 |
| ⑤ | `GovernanceController` 类级 `[Authorize]` 补全 | ⚪ 可选 | 3 号位顺手活 | ✅ **2026-09-16 闭环**：28 GET 方法级挂 `[Authorize(Policy=…)]`（复用 34 权限码）+ 类级 `PlanView` 撤销 |

[5 号位契约](5号位契约-依赖与扩展项.md) P0 鉴权修复：

| # | 任务 | 严重度 | 闭环状态 |
|---|---|---|---|
| ① | `ProcurementManualEtaController` POST/DELETE 加 `[Authorize]`（拆 View / Maintain / Cancel 三层）| 🔴 P0 | ✅ **5 号位已闭环 + 4 号位验证 36/36** |
| ② | 11 个查询 Controller 加类级 `[Authorize(PlanView)]` | 🔴 P0 | ✅ **5 号位已闭环 + 4 号位验证 36/36** |
| ③ | `DemandProtectionController` POST release 端点 | 🔴 P0 | ✅ **2026-09-12 闭环**（per-lock partial-success 契约）+ ✅ **2026-09-16 A方案 闭环**（新建完整 Application 层）|
| ④ | `ProcurementManualEtaOverride` DTO + DB 加 `DepartmentCode` 字段 | 🟡 P1 | ❌ 待 5 号位（下周）|
| ⑤ | scope 二次校验（11 个 Controller 调 `IDataScopeService.EnsureInScopeAsync`）| 🟡 P1 | ✅ **3号位 部分闭环**（2026-09-16 接口已建，fail-closed）；5 号位 待接入调用 |

---

## 六、不动的部分（红线确认）

| 项 | 原因 |
|---|---|
| `lps/` 任何文件 | 4 号位硬约束 |
| `views/Authorization/Role/Role.vue` | 项目原 axios 路径（`/api/role` mock），不调 3 号位 RBAC API，不能复用 |
| 路由 `name` 前缀 | `Aps*` 即被 `setupApsRouteGuard` 自动覆盖，无需改 guard |
| 后端 `/api/rbac/scopes/{id}` PUT | DTO 只能改 description，UI 暂不暴露编辑 |
| 权限码 CRUD 写端 | 后端只有 GET/POST，无 PUT/DELETE（v1.2 设计如此）|
| ~~RBAC 读回端点~~ | ~~后端未提供，AssignDialog 顶部红 Alert 明示风险 + 默认不预勾选~~ → ✅ **2026-09-16 已实现**（@see 8.15-8.19）：3号位 已实现 4 个读回端点（`GET /api/rbac/users/{id}/roles` + `users/{id}/scopes` + `roles/{id}/permissions` + `roles/{id}/scopes`）；前端 AssignDialog 第二波预勾选改造已完工（`preCheckedIds` + `loadingPrecheck` + Alert 降 warning）|

---

## 七、与上游契约文档的对接

| 契约 | 本次更新 |
|---|---|
| [3号位契约-依赖与扩展项.md](3号位契约-依赖与扩展项.md) | §二 重排剩余待办（4 项合同发现）；§八 新增 5 项实测发现 |
| [5号位契约-依赖与扩展项.md](5号位契约-依赖与扩展项.md) | 路径错字校对；最近更新加 "13:00 ① ② 已验证 36/36 绿" |
| [5号位-执行清单-2026-09-12.md](5号位-执行清单-2026-09-12.md) | 标题加 "① ② 已完成（36/36 绿）"；路径错字校对 |
| [rbac.md](rbac.md) | v1.2 §23.1 重新启用 RBAC 矩阵 + dev 验证手册（无改动，沿用）|

---

## 八、未来工作（不在本次范围）

| # | 任务 | 阻塞项 |
|---|---|---|
| 1 | ~~3 号位补 `IDataScopeService.EnsureInScopeAsync` 接口 + Domain 字段契约决策~~ | ✅ **2026-09-16 完成**：3号位 接口已建（fail-closed）；Domain 字段契约已收口（productFamilyId/factoryId）|
| 2 | ~~5 号位补 `DemandProtectionController` POST release 端点~~ | ✅ **2026-09-12 闭环 + 2026-09-16 A方案 闭环** |
| 3 | ~~5 号位补 `ManualEtaOverride` DTO + DB `DepartmentCode` 字段~~ | ❌ **2026-09-15 二次确认撤销**：与 9月13日 一致，5号位 不维护（@see [4号位-2026-09-15-撤销通知-给5号位.md](4号位-2026-09-15-撤销通知-给5号位.md)）|
| 4 | ~~RBAC 「读回当前分配」4 个 GET 端点（解锁 AssignDialog 预勾选）~~ | ✅ **2026-09-16 完成**：3号位 已实现 4 个读回端点 + 4号位 已接入前端（@see [4号位-2026-09-16-Pkg-8第二波完工-给3号位.md](4号位-2026-09-16-Pkg-8第二波完工-给3号位.md)）|
| 5 | ~~审计日志查询端点（`GET /api/rbac/audit-logs`）~~ | ✅ **2026-09-16 完成**（@see [APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md §一.⑦](APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md)）|

---

## 九、Pkg-8 第二波实施记录（2026-09-16）

**触发**：[APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md §一.④](APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md) 3号位 已实现 4 个读回端点（r13366 同步落地 record fix + 4 读回）；4号位 接入前端 AssignDialog 预勾选改造，**目的**：消除「Dialog 不预勾选 = 用户面对空列表无法核对的 P1 UX 缺陷」。

### 9.1 新增读回端点（rbac.ts）

```ts
async getUserRoles(userId): Promise<RoleSummaryDto[]>                  // GET /api/rbac/users/{id}/roles
async getUserScopes(userId): Promise<DataScopePolicyDto[]>              // GET /api/rbac/users/{id}/scopes
async getRolePermissions(roleId): Promise<PermissionSummaryDto[]>      // GET /api/rbac/roles/{id}/permissions
async getRoleScopes(roleId): Promise<DataScopePolicyDto[]>              // GET /api/rbac/roles/{id}/scopes
```

含 4 张 mock fixture 映射表（按 id 查 mock 数据），**写端点不变**（仍 `rejectMockWrite` 抛 403）。

### 9.2 AssignDialog 改造

| Props | 类型 | 默认 | 说明 |
|---|---|---|---|
| `preCheckedIds` | `number[]` | `[]` | 打开时已分配的 id（来自 4 读回端点）|
| `loadingPrecheck` | `boolean` | `false` | 父组件正在调读回端点 → v-loading 遮罩 |
| `showReadBackWarning` | `boolean` | `false` | 读回失败时 → Alert 升 type=error |

**关键 UX 行为**：
- 正常（读回成功）：顶部 type=warning Alert「已根据后端读回端点预勾选当前生效项」
- 读回失败（403/500/网络）：顶部 type=error Alert「⚠️ 读取现有分配失败，本弹窗不代表当前生效配置」+ 列表全不勾选
- loading 中：v-loading 遮罩「正在加载当前分配…」+ 禁用所有 checkbox/全选/清空/关闭按钮
- 二次确认：空数组=「确认清空」，非空=「确认替换」（带计数 + 「此操作不可撤销」）

### 9.3 RbacUsers.vue + RbacRoles.vue 改造

4 个 open* 函数（openAssign / openAssignScope / openAssignPerm / openAssignScope）改为 async：
```ts
async function openXxx(row) {
  assignTarget.value = row
  preCheckedIds.value = []
  showReadBackWarning.value = false
  loadingPrecheck.value = true
  visible.value = true  // 立即打开 Dialog（带 v-loading 遮罩）
  try {
    const current = await rbacApi.getXxx(row.id)
    preCheckedIds.value = current.map(x => x.id)
  } catch (err) {
    preCheckedIds.value = []
    showReadBackWarning.value = true  // Alert 升 error
  } finally {
    loadingPrecheck.value = false
  }
}
```

### 9.4 verify-integration.mjs [B] 段扩展

新增 4 个读回断言（每个 PUT 清空后立即 GET 验证）：
- `GET /api/rbac/users/{id}/roles` — code=200, count=0
- `GET /api/rbac/users/{id}/scopes` — code=200, count=0
- `GET /api/rbac/roles/{id}/permissions` — code=200, count=0
- `GET /api/rbac/roles/{id}/scopes` — code=200, count=0

[B] 段总数：18 写 + 4 读回 = **22 端点**。整体 verify：36 → **73 断言全绿**。

### 9.5 完整闭环回执

- [4号位-2026-09-16-Pkg-8第二波完工-给3号位.md](4号位-2026-09-16-Pkg-8第二波完工-给3号位.md) — 给 3号位 的完工验收回执
- 3号位 ↔ 4号位 当前**联调 100% 闭环**

---

## 十、联络点

- **4 号位**：本报告作者；维护 [frontNew/scripts/verify-integration.mjs](scripts/verify-integration.mjs)
- **3 号位**：见 [3号位契约-依赖与扩展项.md](3号位契约-依赖与扩展项.md)
- **5 号位**：见 [5号位-执行清单-2026-09-12.md](5号位-执行清单-2026-09-12.md)
- **RBAC 完整矩阵**：[rbac.md](rbac.md)
- **v1.2 总体进度**：[v1.2-完成报告.md](v1.2-完成报告.md)
