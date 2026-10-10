# 3号位契约 — APS_Auth dev 种子（v3，3 段 SQL 一次性）

**接收人**：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
**来源**：4号位（前端页面与业务操作）
**生成日期**：2026-09-10
**前置文档**：
- [3号位契约-种子数据补齐.md](3号位契约-种子数据补齐.md)（v1，已废）
- [3号位契约-dev种子补丁说明.md](3号位契约-dev种子补丁说明.md)（v2，已废）
- [APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql](../../冻结文档/APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql)

**变更说明**：v2 文档建议"写 5 段大 SQL 补丁"已**作废**。3 号位说"lps 全了"是对的：
- ✅ 后端已有 `PermissionSeedService` 启动自动种 34 个 Permission 码
- ✅ 后端已有 `RbacController` + `RbacManagementService` 完整 CRUD（用户/角色/权限/Scope）
- ✅ 后端 `AuthService` 支持 PBKDF2 密码校验
- 缺的只有 **3 段 SQL**，解决"鸡生蛋"问题：admin 密码占位符 + admin 没挂权限

**目的**：3 段 SQL 让 admin 能登录 + 有权限调 RBAC API；其余 5 测试用户 / RolePermission / Scope 全部走 `POST /api/rbac/*` API，**4 号位自己调**，不需要 3 号位写 SQL。

---

## 一、后端已落地的（不需要重做）

| 项 | 文件 | 状态 |
|---|---|---|
| 13 张表 | DDL v1.3 47-378 | ✅ |
| 7 系统角色 | DDL v1.3 383-394 | ✅ |
| 34 个 Permission 码 | `PermissionSeedService` 启动自动种 | ✅ |
| admin 用户 + 挂 `aps.admin.system` | DDL v1.3 522-535 | ✅ |
| Global Scope + 挂 `aps.admin.system` | DDL v1.3 541-566 | ✅ |
| `RbacController` RBAC 增删改查 | `lps/LPS.APS.Web/Controllers/RbacController.cs` | ✅ |
| `RbacManagementService` 完整 CRUD | `lps/LPS.APS.Engine/Services/Auth/RbacManagementService.cs` | ✅ |
| PBKDF2 密码校验 | `lps/LPS.APS.Core/Security/PasswordHasher.cs` | ✅ |

## 二、鸡生蛋问题

1. admin 用户的 `PasswordHash` 是 `TEMP_PASSWORD_HASH` 占位符 → 登录校验必败
2. 即使密码对了，admin 也没挂任何 Permission → 调 `RbacController`（要求 `aps.auth.user.edit`）会被 403

所以必须用 SQL 绕过 API 来初始化 admin。

## 三、3 段 SQL（4 号位后做都用 API）

**建议路径**：`lps/LPS.APS.Web/Sql/APS_Auth_v1.3_admin_bootstrap_20260910.sql`
**执行时机**：DDL 主脚本跑完 + 启动后端服务**之前**；3 号位一次执行即可

### 段 A — 替换 admin 密码为 `Admin@123456`

> 注：原 v2 文档建议 `123456` 不行（`RbacManagementService.CreateUserAsync` 要求 8-128 位 + 不得与 LoginName 相同；admin 创建时不受限，但 Rbac API 创建新用户时受限）。这里 admin 用 `Admin@123456` 满足规则。

```sql
-- 3 号位本地生成 PBKDF2 哈希：
--   dotnet run --project lps/LPS.APS.Engine 启动临时控制台
--   调用 PasswordHasher.Hash("Admin@123456")
--   把输出字符串粘到这里
IF EXISTS (SELECT 1 FROM [User] WHERE LoginName='admin' AND PasswordHash='TEMP_PASSWORD_HASH')
BEGIN
    UPDATE [User]
    SET PasswordHash = '<PBKDF2$210000$<saltB64>$<hashB64> 字符串>',
        UpdatedAt = GETDATE()
    WHERE LoginName='admin';
END
```

**生成 PBKDF2 哈希的最快方式**（3 号位 1 行命令）：
```bash
cd lps && dotnet script -c 'Console.WriteLine(LPS.APS.Core.Security.PasswordHasher.Hash("Admin@123456"))'
```
或临时在 `Program.cs` 加一行 `Console.WriteLine(PasswordHasher.Hash("Admin@123456"))`，跑一次后回滚。

### 段 B — 给 `aps.admin.system` 角色挂全部 34 个 Permission

```sql
DECLARE @SysAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.admin.system');
INSERT INTO RolePermission (RoleId, PermissionId)
SELECT @SysAdminRoleId, p.Id
FROM [Permission] p
WHERE NOT EXISTS (
    SELECT 1 FROM RolePermission rp
    WHERE rp.RoleId=@SysAdminRoleId AND rp.PermissionId=p.Id
);
```

> 注：也可以只挂 `aps.auth.user.edit` 一个权限让 admin 能调 RBAC API；但**建议全挂**，dev 阶段 admin 是万能账号，后续按需删除。

### 段 C — 给其他 4 系统角色挂业务权限（可选，推荐）

如果不想让 admin 一直挂全部 34 权限（生产前要清理），可以现在就分摊：
- `aps.viewer.management` 挂 10 个只读
- `aps.planner` 挂 17 个 PMC 权限
- `aps.admin.aps` 挂 8 个 admin.aps 权限（rule/parameter/strategy）
- `aps.admin.system` 仅挂 `aps.auth.*` (5) + `aps.audit.view` + `aps.auth.user.edit`（让 admin 能创建用户）

然后 admin 通过 RBAC API 创建测试用户并分配这些角色。

**完整 SQL 示例（来自 v2 文档，3 号位按需参考）**：

```sql
-- aps.viewer.management
DECLARE @ViewerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.viewer.management');
INSERT INTO RolePermission (RoleId, PermissionId)
SELECT @ViewerRoleId, p.Id FROM [Permission] p
WHERE p.PermissionCode IN (
    'aps.plan.view','aps.ctp.view','aps.candidate.view','aps.rule.view',
    'aps.parameter.view','aps.strategy.view','aps.manual_eta.view',
    'aps.demand_protection.view','aps.mes.view','aps.audit.view'
) AND NOT EXISTS (SELECT 1 FROM RolePermission rp WHERE rp.RoleId=@ViewerRoleId AND rp.PermissionId=p.Id);

-- aps.planner
DECLARE @PlannerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.planner');
INSERT INTO RolePermission (RoleId, PermissionId)
SELECT @PlannerRoleId, p.Id FROM [Permission] p
WHERE p.PermissionCode IN (
    'aps.plan.view','aps.plan.run','aps.plan.compare','aps.plan.export',
    'aps.ctp.view','aps.ctp.evaluate','aps.insert.impact.evaluate',
    'aps.reschedule.local','aps.reschedule.manual',
    'aps.candidate.view','aps.candidate.confirm','aps.candidate.activate',
    'aps.manual_eta.view','aps.manual_eta.edit','aps.manual_eta.cancel',
    'aps.demand_protection.view','aps.mes.view'
) AND NOT EXISTS (SELECT 1 FROM RolePermission rp WHERE rp.RoleId=@PlannerRoleId AND rp.PermissionId=p.Id);

-- aps.admin.aps
DECLARE @ApsAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.admin.aps');
INSERT INTO RolePermission (RoleId, PermissionId)
SELECT @ApsAdminRoleId, p.Id FROM [Permission] p
WHERE p.PermissionCode IN (
    'aps.plan.view','aps.rule.view','aps.rule.edit','aps.rule.publish',
    'aps.parameter.view','aps.parameter.edit','aps.strategy.view','aps.strategy.edit','aps.strategy.publish'
) AND NOT EXISTS (SELECT 1 FROM RolePermission rp WHERE rp.RoleId=@ApsAdminRoleId AND rp.PermissionId=p.Id);
```

## 四、3 段 SQL 跑完后，4 号位做什么

4 号位用 admin 通过 RBAC API 创建剩余数据：

| 步骤 | API | 数据 |
|---|---|---|
| 1 | `POST /api/rbac/users` × 4 | viewer / pmc / rule_admin / rule_publisher（密码 `Password@123`，8 位+含大小写+符号）|
| 2 | `POST /api/rbac/scopes` × 6 | Factory=BJ/SUZ, ProductFamily=INJECTION/ASSEMBLY/TEST, Department=PMC_DEPT, Domain=FAMILY_INJECTION/ASSEMBLY/TEST（注：Domain/Family 3 个看 3 号位是否接受）|
| 3 | `PUT /api/rbac/roles/{id}/scopes` | 把相关 Scope 挂到 `aps.planner` / `aps.viewer.management` / `aps.admin.aps` 角色 |
| 4 | `PUT /api/rbac/users/{id}/roles` | 给 5 个测试用户分别挂对应角色（viewer → aps.viewer.management；pmc → aps.planner；rule_admin/rule_publisher → aps.admin.aps；sys_admin → aps.admin.system）|

**4 号位会做**：写一个 `pnpm seed:dev` 脚本（一次性 ts-node），自动调上面 4 步。

## 五、协调项

| 项 | 现状 | 处理 |
|---|---|---|
| admin 密码规则 | `RbacManagementService.CreateUserAsync` 要求 8-128 位 + 与 LoginName 不同 | admin 用 `Admin@123456`；测试用户用 `Password@123` |
| admin 应挂哪些权限 | dev 阶段：建议全挂 34；生产前清理 | 3 号位按段 C 决策 |
| 测试用户的密码哈希 | `RbacController.CreateUser` 调 `PasswordHasher.Hash()` 自动算 | API 自动处理，无需 3 号位生成 |
| Domain 范围值 | DDL v1.3 CK_DataScope_Type 已允许 `Domain` | OK，3 号位 RBAC API 接受 |
| `aps.coordinator.material` / `aps.supervisor.workshop` / `aps.service.api` | DDL 有，本批次不挂 | 不动 |

## 六、风险

| 风险 | 缓解 |
|---|---|
| 段 B 全挂 34 权限给 admin | dev 用；生产前走段 C 调整 + admin 改用真实管理用户 |
| 段 A 哈希字符串手贴易错 | 3 号位本地 `dotnet run` 生成后复制粘贴；SQL 含 `WHERE PasswordHash='TEMP_PASSWORD_HASH'` 防覆盖已正确哈希 |
| admin 登录后没看到数据 | RBAC API 调通后立即有数据；不需要等 3 号位 |
| 4 号位 RBAC API 调用脚本未写 | 4 号位 Phase D 时写 `seed:dev` 一次性脚本 |

## 七、签署

- 3 号位写完 3 段 SQL 并本地跑通 → 回执 4 号位
- 4 号位收到回执后启动后端 → 跑 `POST /api/auth/login` 验证 admin 能登录 → 跑 RBAC API 创建 5 用户
- 4 号位前端 Phase D 启动（HTTP 客户端 + `/api/auth/me` 调通）
