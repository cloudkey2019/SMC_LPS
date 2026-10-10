# 3号位契约 — APS_Auth dev 种子（v4，**实测后更新**）

**接收人**：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
**来源**：4号位（前端页面与业务操作）
**生成日期**：2026-09-10
**前置文档**：
- [3号位契约-种子数据补齐.md](3号位契约-种子数据补齐.md)（v1，已废）
- [3号位契约-dev种子补丁说明.md](3号位契约-dev种子补丁说明.md)（v3，已废）
- [APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql](../../冻结文档/APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql)

**变更说明（v3 → v4）**：4 号位实测数据库后发现 3 号位已经完成了 v3 的段 A（admin 密码替换）+ 段 B（admin 角色挂全部 34 权限）。**v3 文档中"3 段 SQL"作废**，admin 已经能登录 + 有权限调 RBAC API。**4 号位可以直接走 RBAC API** 完成剩余数据。

---

## 一、实测数据库（2026-09-10 13:42）

| 项 | DDL 默认 | 实测当前值 | 状态 |
|---|---|---|---|
| admin 密码 | `TEMP_PASSWORD_HASH` | **PBKDF2$210000$...（83 字符）** | ✅ 3 号位已改 |
| 7 系统角色 | 有 | 有 | ✅ |
| 34 Permission 码 | 有 | 有（`PermissionSeedService` 启动种）| ✅ |
| admin 挂 `aps.admin.system` | 是 | 是 | ✅ |
| **`aps.admin.system` 挂权限** | 0 | **34** | ✅ 3 号位已挂全部 |
| `aps.planner` 挂权限 | 0 | 0 | ⚠️ 需挂（RBAC API 或 SQL）|
| `aps.admin.aps` 挂权限 | 0 | 0 | ⚠️ 需挂 |
| `aps.viewer.management` 挂权限 | 0 | 0 | ⚠️ 需挂 |
| DataScopePolicy | 仅 Global | 仅 Global | ⚠️ 需补 6 条 |
| 测试用户（除 admin）| 0 | 0 | ⚠️ 需创建 4 个 |

## 二、后端已落地的（不需要重做）

| 项 | 文件 | 状态 |
|---|---|---|
| 13 张表 | DDL v1.3 47-378 | ✅ |
| 7 系统角色 | DDL v1.3 383-394 | ✅ |
| 34 个 Permission 码 | `PermissionSeedService` 启动自动种 | ✅ |
| admin 用户 + 密码 + 挂全部 34 权限 | 3 号位手动完成 | ✅ |
| `RbacController` RBAC 增删改查 | `lps/LPS.APS.Web/Controllers/RbacController.cs` | ✅ |
| `RbacManagementService` 完整 CRUD | `lps/LPS.APS.Engine/Services/Auth/RbacManagementService.cs` | ✅ |
| PBKDF2 密码校验 | `lps/LPS.APS.Core/Security/PasswordHasher.cs` | ✅ |

## 三、admin 密码是什么？

**4 号位不知道**。3 号位手动跑 `PasswordHasher.Hash("xxx")` 生成的。**3 号位回执时一并告知 admin 密码**。

## 四、剩余工作（**全部由 4 号位用 RBAC API 完成**）

| 步骤 | API | 数据 |
|---|---|---|
| 1 | `POST /api/rbac/users` × 4 | viewer / pmc / rule_admin / rule_publisher（密码 `Password@123`，8 位+含大小写+符号）|
| 2 | `POST /api/rbac/scopes` × 6 | Factory=BJ/SUZ, ProductFamily=INJECTION/ASSEMBLY/TEST, Department=PMC_DEPT, Domain=FAMILY_INJECTION/ASSEMBLY/TEST |
| 3 | `GET /api/rbac/permissions` | 取 34 个 PermissionCode → PermissionId 映射 |
| 4 | `PUT /api/rbac/roles/{id}/permissions` | 给 `aps.viewer.management` 挂 10 个只读 / `aps.planner` 挂 17 个 / `aps.admin.aps` 挂 8 个 |
| 5 | `PUT /api/rbac/roles/{id}/scopes` | 把相关 Scope 挂到 `aps.planner` / `aps.viewer.management` / `aps.admin.aps` 角色 |
| 6 | `PUT /api/rbac/users/{id}/roles` | 给 5 个测试用户分别挂对应角色 |

**4 号位会做**：写一个 `pnpm seed:dev` 脚本（一次性 ts-node），自动调上面 6 步。

## 五、3 号位需要做的

**几乎没有**。v3 文档里的 3 段 SQL 已经完成。

**唯一请 3 号位回执**：
1. **admin 现在的密码是什么？**（4 号位登录需要）
2. 确认 `aps.planner` / `aps.admin.aps` / `aps.viewer.management` 由 4 号位通过 RBAC API 挂权限 OK？（如需 3 号位手动挂 SQL，请告知）

## 六、风险与注意

| 风险 | 缓解 |
|---|---|
| admin 密码 3 号位忘了 | 3 号位跑过 `PasswordHasher.Hash` 必有记录；如果实在丢，重新生成 + SQL UPDATE 即可 |
| 业务角色挂权限不一致 | 4 号位用 RBAC API 严格按 4 号位文档执行（10/17/8 分布）|
| Scope 策略 6 条命名不一致 | 4 号位用前端 mock 的命名（`FAMILY_INJECTION` / `BJ` / `PMC_DEPT` 等）|
| 生产前清理 | dev 阶段 admin 全权限 OK；生产前 admin 改用真实管理账号 + 段 C 调整 |

## 七、签署

- 3 号位回执 admin 密码 + 确认业务角色挂权限由 4 号位用 RBAC API 做
- 4 号位收到回执后启动后端 → 跑 `POST /api/auth/login` 验证 admin 能登录 → 写 `seed:dev` 脚本跑 RBAC API
- 4 号位前端 Phase D 启动（HTTP 客户端 + `/api/auth/me` 调通）
