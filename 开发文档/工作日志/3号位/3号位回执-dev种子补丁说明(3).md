# 3号位回执 — APS_Auth dev 种子（答复 v3）

**接收人**：4号位（前端页面与业务操作）
**来源**：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
**生成日期**：2026-09-10
**答复对象**：[3号位契约-dev种子补丁说明(3).md](3号位契约-dev种子补丁说明(3).md)（v3「3 段 SQL 一次性」）
**交付物**：`lps/LPS.APS.Web/Sql/APS_Auth_v1.3_admin_bootstrap_20260910.sql`（幂等引导脚本，已归档）

---

## 一、核对结论：dev DB 已就绪，现在无需再跑任何 SQL

3号位 用只读查询逐项核对了 dev DB（`10.116.2.75 / APS_Auth`）现状，**v3 的段 A + 段 B 目标态已经达成**：

| 核对项 | 现状 | v3 目标 |
|---|---|---|
| admin 密码 | 已是 `Admin@123456` 的 PBKDF2 哈希（本地 `PasswordHasher.Verify` 验证通过） | 段 A ✅ 已满足 |
| admin 挂角色 | 已挂 `aps.admin.system` | DDL 自带 ✅ |
| aps.admin.system 权限 | 已挂**全部 34 个**权限码（RolePermission 表中 34 行全部归属该角色） | 段 B ✅ 已满足 |
| aps.admin.system 范围 | 已挂唯一一条 `Global=*`（DataScopePolicy 现仅 1 条） | DDL 自带 ✅ |
| admin 帐号状态 | `IsEnabled=1`、`IsDeleted=0`；系统角色 `IsActive=1` | 可登录 ✅ |

**结论：**「鸡生蛋」已经打通。4号位 现在**直接启动后端**，用 `admin / Admin@123456` 登录、拿 token 调 RBAC API 即可，无需等 3号位 跑 SQL。

> 说明：v3 签署环节「3号位写完补丁并本地跑通」，本次「跑通」是通过**只读核对 + 本地密码哈希验证**达成的，**未对 DB 做任何写入**。因为现状已满足目标态，运行补丁没有意义（即便运行为幂等 0 行变更，也无必要）。

---

## 二、交付物：幂等引导脚本（归档，供 DDL 重建后使用）

| 文件 | 用途 |
|---|---|
| `APS_Auth_v1.3_admin_bootstrap_20260910.sql` | **段 A**（仅当 admin 密码仍是 `TEMP_PASSWORD_HASH` 占位符时才替换为 `Admin@123456`）+ **段 B**（差分集补齐 aps.admin.system 的 34 权限码） |

- 全程幂等：段 A 的 `WHERE PasswordHash='TEMP_PASSWORD_HASH'` 防覆盖，段 B 的 `WHERE NOT EXISTS` 防重复。
- 适用时机：将来 APS_Auth 按 v1.3 冻结 DDL 重建后，启动后端**之前**执行一次。
- 生产环境禁用的注释已在文件顶部标出。

---

## 三、回执三点

### ① 覆盖式 PUT 红线（重要，务必写入 `seed:dev` 脚本注意项）

`RbacController` 下列端点都是**「先 DELETE 全部、再 INSERT 新集合」的覆盖式语义**，不是增量 append：

| 端点 | 底层服务 | 覆盖式行为 |
|---|---|---|
| `PUT /api/rbac/users/{id}/roles` | `AssignUserRolesAsync` | 先 `DELETE FROM UserRole WHERE UserId=…`，再插入 |
| `PUT /api/rbac/users/{id}/scopes` | 用户范围分配 | 先 `DELETE FROM UserDataScope WHERE UserId=…`，再插入 |
| `PUT /api/rbac/roles/{id}/permissions` | 角色权限分配 | 先 `DELETE FROM RolePermission WHERE RoleId=…`，再插入 |
| `PUT /api/rbac/roles/{id}/scopes` | `AssignRoleScopesAsync` | 先 `DELETE FROM RoleDataScope WHERE RoleId=…`，再插入 |

**因此 `seed:dev` 脚本必须遵守：**

- ❌ **不要**对 `admin` 用户调 `PUT /api/rbac/users/{adminId}/roles`——会先删掉 admin 已有 `aps.admin.system` 关联，若传的集合不完整就丢关联。
- ❌ **不要**对 `aps.admin.system` 角色调 `scopes` / `permissions`——会先删掉它的 `Global=*` 范围或 34 权限码。
- ✅ 每个覆盖式 PUT 传入的必须是**该对象目标态的完整集合**，不能当作「再补几条」。

**一句话：`seed:dev` 只操作 4 个新建测试用户 + 3 个业务角色（`aps.planner` / `aps.viewer.management` / `aps.admin.aps`），完全不要碰 `admin` 用户和 `aps.admin.system` 角色。**

### ② `sys_admin` 复用 admin（确认）

确认采纳：**不新建 `sys_admin` 用户**，直接复用 `admin`（已挂 `aps.admin.system` + 全 34 权限 + `Global` 范围）。4号位 测「SYSTEM_ADMIN」视角就用 `admin` 登录。

→ v3 文档「四、第 4 步」的「5 个测试用户」应更正为「**4 个测试用户**」，`sys_admin → aps.admin.system` 这一行删除（admin 已天然具备）。

### ③ 两处计数纠错

1. **v3「四、第 2 步」`POST /api/rbac/scopes × 6` 实为 9 条**：
   - Factory：`BJ`、`SUZ`（2）
   - ProductFamily：`INJECTION`、`ASSEMBLY`、`TEST`（3）
   - Department：`PMC_DEPT`（1）
   - Domain：`FAMILY_INJECTION`、`FAMILY_ASSEMBLY`、`FAMILY_TEST`（3，注「看 3号位 是否接受」→ **3号位 接受**，`CreateDataScopePolicyAsync` 支持 `ScopeType=Domain`）
   - 合计 2+3+1+3 = **9 条**。

2. **v3「段 C」`aps.auth.* (5)` 实为 6 个**：
   - `aps.auth.user.view` / `aps.auth.user.edit` / `aps.auth.role.view` / `aps.auth.role.edit` / `aps.auth.permission.assign` / `aps.auth.scope.assign` = **6 个**
   - 其中 `aps.auth.user.edit` 已含在 `aps.auth.*` 内，无需单列；另加 `aps.audit.view` 1 个
   - 故段 C 目标应为 **6 auth + 1 audit = 7 个码**（而非文档暗示的 5+1+1=7 的口径）。

---

## 四、一个待确认点：3 个业务角色当前权限 / 范围为「空」

本次核对同时发现——这也正是 v3「**段 C 可选、推荐**」尚未执行的结果：

| 角色 | 权限码数 | 范围条数 |
|---|---|---|
| aps.planner | **0** | 0 |
| aps.viewer.management | **0** | 0 |
| aps.admin.aps | **0** | 0 |
| aps.admin.system | 34 | 1（Global） |

**后果：** admin 建好 viewer / pmc / rule_admin / rule_publisher 并挂角色后，这些用户**能登录、`roles` 正常**，但 `permissions: []`（其角色没有挂任何权限码）。

**处置结论（3号位 已明确）：** 3号位 **不补段 C**。业务角色（`aps.planner` / `aps.viewer.management` / `aps.admin.aps`）的权限码由 4号位 自己用 `admin` 登录后、调 `PUT /api/rbac/roles/{id}/permissions`（覆盖式，**传该角色目标态的完整码集**）授权即可。若相位 D 只需跑通登录链路、暂不需测试用户带非空 `permissions`，也可保持为空——二者都不阻塞联调。

---

## 五、4号位 后续动作（无变化，与 v3 一致）

1. 启动后端（`dotnet run --project lps/LPS.APS.Web`，端口 5163）
2. `POST /api/auth/login`，body `{"userCode":"admin","password":"Admin@123456"}` 验证登录
3. 拿 accessToken 调 `/api/auth/me`，核对 admin 的 `roles=[aps.admin.system]`、`permissions`（34 码）、`dataScope.IsGlobal=true`
4. 写 `seed:dev` 脚本建 4 测试用户 + 9 条 Scope + 挂角色（遵守第三节红线）
5. 前端 Phase D 启动（HTTP 客户端 + `/api/auth/me` 调通）

---

## 六、签署

- 3号位：已完成只读核对 + 引导脚本归档，确认 dev DB 就绪 ✅
- 4号位：业务角色权限自行经 API（用 admin）授权；收到本回执后即可启动后端联调。