# 3号位 → 4号位：RBAC 管理 4 页面后端落地回执（v1.0）

> **发件**：3号位（Auth / RBAC / Scope Owner）
> **致**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-28 ｜ **版本**：v1.0
> **性质**：R1/R2/R4 后端实现**落地回执**——代码已合入、`dotnet build` 0 错误、已提交 SVN；请 4号位 按 §四 跑 V1~V6
> **依据**：`3号位致4号位_RBAC管理4页面数据清理与UI精简回执_v1.0_20260928.md`（R1-R5）+ `3号位致4号位_RBAC列表分页接口契约_v1.0_20260928.md`（R2 契约）+ `4号位-2026-09-28-RBAC数据清理与分页-回执-给3号位.md`（贵方回执）

---

## 〇、速答清单

```
R1 测试数据清理端点          : ✅ 已落码 POST /api/rbac/test-data/cleanup（含自删保护）
R2 4 端点列表分页            : ✅ 已落码 ApiResponse<PageResult<T>>（含 pageSize 200 上限 + XML 注释）
R4 4 端点批量删除            : ✅ 已落码 {ids:[]}→{succeeded,failed}，权限=停用，failed 对齐 {kind,id,reason}
贵方回执 §二.3 三条 R4 建议   : ✅ 全部照办（见 §二）
贵方回执 §三 pageSize 微调    : ✅ 已加扩展注释（见 §二）
增量硬化（系统角色/内置权限码）: ⚠️ 超出原始 R1-R5 的两条安全加固，请知悉（见 §三）
构建 / 提交                 : ✅ dotnet build 0 错误，已提交 SVN
```

---

## 一、落码范围

| 端点 | 方法 / 路由 | 语义 |
|---|---|---|
| R1 | `POST /api/rbac/test-data/cleanup` | 按命名规则软删/停用 verify 残留 |
| R2 | `GET /api/rbac/{users,roles,permissions,scopes}` | 分页（`page/pageSize/keyword/…`） |
| R4 | `POST /api/rbac/{users,roles,permissions,scopes}/batch-delete` | 批量删除（`{ids:[]}`） |

### 1.1 R1 cleanup 端点

- 鉴权：`auth.manage`（`aps.auth.user.edit`）。
- 命名规则（**仅按规则，不带任意 id 列表**）：
  - 用户 `TEST-USER-%` / `aps.auth.negate.%` → 软删（`IsDeleted=1, IsEnabled=0`）
  - 角色 `aps.verify.role.%` → 软删（`IsActive=0`）
  - 权限 `aps.verify.%` → 停用（`IsActive=0`）
- 响应 `{ users, roles, permissions, failed }`；`failed` 恒为 `[]`（幂等、命中 0 条也成功返回）。
- **自删保护**：用户软删 `WHERE … AND Id <> @OperatorId`，当前登录管理员永不被清（V2）。

### 1.2 R2 分页

- 响应统一 `ApiResponse<PageResult<T>>`，`data` = `{ items, total, page, pageSize }`。
- `page` 默认 1、`pageSize` 默认 20；超 200 截断；非法值回落默认（宽容）。
- 排序：`users/roles/scopes` = `Id ASC`；`permissions` = `Module ASC, Id ASC`。
- 读回端点（`users/{id}/roles` 等 4 个）**维持扁平 `IReadOnlyList`，不变**。

### 1.3 R4 批量删除

- 请求 `{ ids: [] }`，响应 `{ succeeded: [], failed: [] }`；`failed` 元素 = `{ kind, id, reason }`。
- 逐条 try/catch：单条失败不影响其余；`KeyNotFoundException`/`InvalidOperationException` 落入 `failed`，其余异常上抛 500（fail-closed）。
- 用户：自删保护 + 最后 `auth.manage` 保护；角色：系统角色保护；**权限：停用 `IsActive=false`、保留 RolePermission 关联不解绑**；scope：停用 `IsEnabled=0`。

---

## 二、对贵方回执的逐条落实

| 贵方建议 | 落实 |
|---|---|
| §二.1 「或补真删」分支删除，统一「停用」 | ✅ 权限批量删除＝停用（无真删） |
| §二.2 `failed` 对齐 `{kind,id,reason}` | ✅ `BatchDeleteFailure = { Kind, Id, Reason }`，与 cleanup 一致 |
| §二.3 权限停用不解绑（保留 role.permissions 关联） | ✅ 仅 `Permission.IsActive=0`，不碰 `RolePermission` |
| §三 pageSize 200 上限预留扩展注释 | ✅ 4 端点 XML 注释已加「如业务需 500/1000，仅调常量 + 前端选项两处」 |

---

## 三、两项增量硬化（超出原始 R1-R5，请知悉）

1. **用户列表 `Status` 升为三态**：原实现只区分 `Active`/`Deleted`，把 `IsEnabled=0` 的「停用」用户误标为 `Active`；现按契约 §3.1 补 `Disabled`。前端状态渲染请认三态（契约本就列了 Active/Disabled/Deleted 三态）。
2. **系统角色保护收敛**：原单删 `DELETE /roles/{id}` 无系统角色保护，现单删与批量共用 `EnsureRoleDeletableAsync`（`IsSystemRole=true` 不可停用，单删返 400、批量落入 `failed`）。
3. **内置权限码保护**：权限批量停用前校验，命中 `PermissionCodes.All`（43 个 V1 内置功能权限码，含 `aps.auth.user.edit`、`aps.audit.view`）→ 拒绝停用，防止功能授权失效/管理员自锁。自定义权限码、`aps.verify.%` 测试码不受影响。

> ⚠️ 上述 3 点为后端自愈/防护，不要求前端改动；仅第 1 点涉及前端「状态」列的取值为 `Disabled`（此前不会出现）。

---

## 四、4号位 验收就绪（V1~V6）

| # | 验证 | 通过标准 |
|---|---|---|
| V1 | R1 cleanup（admin token） | 一次清掉 30+20+41 残留；响应 `{users:30, roles:20, permissions:41, failed:[]}` |
| V2 | 自删保护 | cleanup 若 admin 账号匹配命名规则 → 被跳过 |
| V3 | R2 mock 联调 | `listXxx` 改分页参数 + `<ElPagination>` 翻页/页大小/筛选联动 |
| V4 | R2 dev 实跑 | `GET /api/rbac/users?page=2&pageSize=10` → `{items:[10条], total, page:2, pageSize:10}` |
| V5 | R4 批量删除用户/角色 | 选 5 测试用户一次清 5 行 |
| V6 | R4 权限批量删除 | 选 1 个 `verify.*` 权限 → `IsActive=false`；所属角色保留授权 |

→ V1~V6 全过 = RBAC 4 页面验收关闭（verify `[R]` 段 `[r-resource-calendar]` 模式追加 7 条断言）。

---

## 五、待 4号位 配合项

1. `src/api/aps-v1/rbac.ts` 4 个 `listXxx` 改分页参数 + 解包 `data.items`（10-01 起 mock）。
2. 4 页面 `<ElPagination>`：`total` 取 `data.total`、`page-size` 默认 20 可切 50/100。
3. R1 端点 UI 入口：贵方倾向方案 B（不暴露 UI，仅 verify 脚本/DBA 触发）——**保持不暴露**，待后续单独提请。
4. F3/F4/verify cleanup 脚本：按贵方回执暂缓，不被本件阻塞。

---

**发件**：3号位 ｜ **致**：4号位（抄送 0号位）｜ **日期**：2026-09-28 ｜ **版本**：v1.0
**本件性质**：R1/R2/R4 后端实现落地回执——代码合入、build 0 错误、已提交 SVN，请按 §四 跑 V1~V6。