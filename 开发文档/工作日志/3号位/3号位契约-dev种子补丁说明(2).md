# 3号位契约 — APS_Auth dev 种子增量补丁（v2，按已有 7 系统角色）

**接收人**：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
**来源**：4号位（前端页面与业务操作）
**生成日期**：2026-09-10
**前置文档**：[3号位契约-种子数据补齐.md](3号位契约-种子数据补齐.md)（v1，已废方向）+ [APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql](../../冻结文档/APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql)
**变更说明**：v1 文档建议"新建 5 个非系统角色 PMC/VIEWER/..."，**作废**。DDL v1.3 已冻结 7 个系统角色（`aps.admin.system` / `aps.admin.aps` / `aps.planner` / `aps.supervisor.workshop` / `aps.coordinator.material` / `aps.viewer.management` / `aps.service.api`），**不新建**，只用这 7 个建 5 测试用户。
**目的**：DDL v1.3 已冻结（13 表 + 7 系统角色 + 34 权限码 + admin 占位 + Global Scope），**主 DDL 不动**。本补丁单独一个 SQL 脚本，补 dev 联调必需的 5 测试用户 + 真实密码哈希 + RolePermission 关联 + 非 Global Scope 策略，让前端能跑通 `/api/auth/login` → `/api/auth/me` 的真实链路。

---

## 一、DDL 已种的内容（不需要重做）

| 项 | DDL 行 | 状态 |
|---|---|---|
| 13 张表 | 47-378 | ✅ |
| **7 个系统角色** | 383-394 | ✅（**就用这 7 个，不新建**） |
| 34 个 V1 权限码 | 402-510 | ✅ |
| `admin` 用户 + 挂 `aps.admin.system` | 522-535 | ✅ 但密码是 `TEMP_PASSWORD_HASH` 占位符 |
| Global Scope (`Global=*`) + 挂 `aps.admin.system` | 541-566 | ✅ |

## 二、前端 mock 角色 → DDL 已存在角色的映射（裁决）

| 前端 mock 角色名 | DDL RoleCode | 用户 LoginName | 用途 |
|---|---|---|---|
| `PMC` | `aps.planner` | `pmc` | 日常排程 / Candidate 确认 |
| `VIEWER` | `aps.viewer.management` | `viewer` | 只读 |
| `RULE_ADMIN` | `aps.admin.aps` | `rule_admin` | 规则/参数/策略编辑（**仅挂 edit，不挂 publish**）|
| `RULE_PUBLISHER` | `aps.admin.aps` | `rule_publisher` | 规则发布（**仅挂 publish，不挂 edit**）|
| `SYSTEM_ADMIN` | `aps.admin.system` | `sys_admin` | 全权限 + 全范围 |

> **裁决理由**：
> 1. DDL 已冻结 7 系统角色且都是 `aps.` 前缀，复用无需新建
> 2. 前端拿到的 `roles: string[]` 就是 DDL RoleCode（不需额外翻译层）
> 3. RULE_ADMIN / RULE_PUBLISHER 都用 `aps.admin.aps` 角色，靠 RolePermission 区分（少挂 vs 多挂）
> 4. 现有 `admin` 用户已挂 `aps.admin.system`，本补丁**不动**，密码单独替换

## 三、本补丁需补的 5 段（独立 SQL 脚本）

**建议路径**：`lps/LPS.APS.Web/Sql/APS_Auth_v1.3_dev_seed_data_20260910.sql`
**执行时机**：DDL 主脚本跑完后；前端 Phase D 调 `/api/auth/me` 之前
**幂等要求**：所有 INSERT 必须先 `IF NOT EXISTS` 判定；密码哈希在脚本里**预先生成好**写入（不是运行时算）

### 段 A — 替换 admin 密码占位

DDL:524 写死 `TEMP_PASSWORD_HASH`，PBKDF2 验证会失败，admin 登不进去。

```sql
IF EXISTS (SELECT 1 FROM [User] WHERE LoginName='admin' AND PasswordHash='TEMP_PASSWORD_HASH')
BEGIN
    UPDATE [User]
    SET PasswordHash = '<PBKDF2 哈希>"123456" 的字符串>',  -- 3号位本地生成后粘贴
        UpdatedAt = GETDATE()
    WHERE LoginName='admin';
END
```

**实现说明**：
- 哈希字符串请用本地 `dotnet run` 起 `PasswordHasher.Hash("123456")` 生成，复制粘贴进 SQL
- `PasswordHasher` 在 [LPS.APS.Core/Security/PasswordHasher.cs](../../lps/LPS.APS.Core/Security/PasswordHasher.cs)

### 段 B — 5 测试用户（4 个 + 1 个补 admin）

DDL 已有 `admin`，本段只插其余 4 个。

```sql
IF NOT EXISTS (SELECT 1 FROM [User] WHERE LoginName='viewer')
BEGIN
    INSERT INTO [User] (LoginName, DisplayName, PasswordHash, Email, IsEnabled)
    VALUES ('viewer', '查看员', '<同段 A 哈希>', 'viewer@aps.local', 1);
END
-- pmc / rule_admin / rule_publisher / sys_admin 同结构重复
```

| LoginName | DisplayName | Email | IsEnabled |
|---|---|---|---|
| `viewer` | 查看员 | viewer@aps.local | 1 |
| `pmc` | 计划员 | pmc@aps.local | 1 |
| `rule_admin` | 规则管理员 | rule_admin@aps.local | 1 |
| `rule_publisher` | 规则发布员 | rule_publisher@aps.local | 1 |
| `sys_admin` | 系统管理员 | sys_admin@aps.local | 1 |

### 段 C — UserRole 关联（5 用户 × 1 角色）

```sql
-- viewer → aps.viewer.management
DECLARE @ViewerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.viewer.management');
DECLARE @ViewerUserId INT = (SELECT Id FROM [User] WHERE LoginName='viewer');
IF @ViewerRoleId IS NOT NULL AND @ViewerUserId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM UserRole WHERE UserId=@ViewerUserId AND RoleId=@ViewerRoleId)
   INSERT INTO UserRole (UserId, RoleId, AssignedBy) VALUES (@ViewerUserId, @ViewerRoleId, @ViewerUserId);

-- pmc → aps.planner
DECLARE @PlannerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.planner');
DECLARE @PmcUserId INT = (SELECT Id FROM [User] WHERE LoginName='pmc');
IF @PlannerRoleId IS NOT NULL AND @PmcUserId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM UserRole WHERE UserId=@PmcUserId AND RoleId=@PlannerRoleId)
   INSERT INTO UserRole (UserId, RoleId, AssignedBy) VALUES (@PmcUserId, @PlannerRoleId, @PmcUserId);

-- rule_admin → aps.admin.aps
DECLARE @ApsAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.admin.aps');
DECLARE @RuleAdminUserId INT = (SELECT Id FROM [User] WHERE LoginName='rule_admin');
IF @ApsAdminRoleId IS NOT NULL AND @RuleAdminUserId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM UserRole WHERE UserId=@RuleAdminUserId AND RoleId=@ApsAdminRoleId)
   INSERT INTO UserRole (UserId, RoleId, AssignedBy) VALUES (@RuleAdminUserId, @ApsAdminRoleId, @RuleAdminUserId);

-- rule_publisher → aps.admin.aps（与 rule_admin 共用角色，靠段 D 区分权限）
DECLARE @RulePublisherUserId INT = (SELECT Id FROM [User] WHERE LoginName='rule_publisher');
IF @ApsAdminRoleId IS NOT NULL AND @RulePublisherUserId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM UserRole WHERE UserId=@RulePublisherUserId AND RoleId=@ApsAdminRoleId)
   INSERT INTO UserRole (UserId, RoleId, AssignedBy) VALUES (@RulePublisherUserId, @ApsAdminRoleId, @RulePublisherUserId);

-- sys_admin → aps.admin.system
DECLARE @SysAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.admin.system');
DECLARE @SysAdminUserId INT = (SELECT Id FROM [User] WHERE LoginName='sys_admin');
IF @SysAdminRoleId IS NOT NULL AND @SysAdminUserId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM UserRole WHERE UserId=@SysAdminUserId AND RoleId=@SysAdminRoleId)
   INSERT INTO UserRole (UserId, RoleId, AssignedBy) VALUES (@SysAdminUserId, @SysAdminRoleId, @SysAdminUserId);
```

> 注：DDL 已有 `admin` → `aps.admin.system`，**不动**。

### 段 D — RolePermission 关联（按角色挂权限码）

DDL 只种了 Permission，没把权限码挂到角色上，导致任何用户登录后 `permissions=[]`。

**走法**：按业务语义最小集挂，不全挂到 `aps.admin.system`。

| RoleCode | 挂的 PermissionCode（来自 PermissionCodes.All） |
|---|---|
| `aps.admin.system` | **全部 34 个** |
| `aps.admin.aps` | `aps.rule.view` / `aps.rule.edit` / `aps.rule.publish` / `aps.parameter.view` / `aps.parameter.edit` / `aps.strategy.view` / `aps.strategy.edit` / `aps.strategy.publish`（8 个） |
| `aps.planner` | `aps.plan.view` / `aps.plan.run` / `aps.plan.compare` / `aps.plan.export` / `aps.ctp.view` / `aps.ctp.evaluate` / `aps.insert.impact.evaluate` / `aps.reschedule.local` / `aps.reschedule.manual` / `aps.candidate.view` / `aps.candidate.confirm` / `aps.candidate.activate` / `aps.manual_eta.view` / `aps.manual_eta.edit` / `aps.manual_eta.cancel` / `aps.demand_protection.view` / `aps.mes.view`（17 个） |
| `aps.viewer.management` | `aps.plan.view` / `aps.ctp.view` / `aps.candidate.view` / `aps.rule.view` / `aps.parameter.view` / `aps.strategy.view` / `aps.manual_eta.view` / `aps.demand_protection.view` / `aps.mes.view` / `aps.audit.view`（10 个） |
| `aps.supervisor.workshop` | **不挂**（本批次不演示车间主管角色，段 B 也没建车间主管用户） |
| `aps.coordinator.material` | **不挂**（本批次不演示物料协同） |
| `aps.service.api` | **不挂**（按需后续） |

**关键点（rule_admin vs rule_publisher 区分）**：
两者都用 `aps.admin.aps` 角色，但实际业务由 RBAC Controller 按用户粒度管控（用户表 UserPermission）。**本批次最简方案**：rule_admin / rule_publisher 通过 UserRole 挂 `aps.admin.aps` 后，4 号位前端**不区分这俩**（都是 SYSTEM_ADMIN 看到的页面）；如果业务必须区分，再走 UserPermission 表。

> 4 号位**简化承诺**：当前 mock 演示只跑 4 用户（pmc / viewer / rule_admin / sys_admin），rule_publisher 占位但不演示。**段 B 仍建 rule_publisher 用户**，但本批次权限与 rule_admin 等价。

#### 段 D 示例 SQL（以 `aps.planner` 为例）

```sql
DECLARE @PlannerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.planner');
INSERT INTO RolePermission (RoleId, PermissionId)
SELECT @PlannerRoleId, p.Id
FROM Permission p
WHERE p.PermissionCode IN (
    'aps.plan.view','aps.plan.run','aps.plan.compare','aps.plan.export',
    'aps.ctp.view','aps.ctp.evaluate',
    'aps.insert.impact.evaluate',
    'aps.reschedule.local','aps.reschedule.manual',
    'aps.candidate.view','aps.candidate.confirm','aps.candidate.activate',
    'aps.manual_eta.view','aps.manual_eta.edit','aps.manual_eta.cancel',
    'aps.mes.view'
)
AND NOT EXISTS (
    SELECT 1 FROM RolePermission rp
    WHERE rp.RoleId=@PlannerRoleId AND rp.PermissionId=p.Id
);
```

`aps.viewer.management` / `aps.admin.aps` / `aps.admin.system` 同结构重复。

### 段 E — 非 Global Scope 策略（6 条）

DDL 只种了 `Global=*`，前端 mock 想看到 Factory / ProductFamily / Department / Domain 范围必须有策略。

```sql
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='Factory' AND ScopeValue='BJ')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('Factory','BJ','北京工厂');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='Factory' AND ScopeValue='SUZ')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('Factory','SUZ','苏州工厂');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='ProductFamily' AND ScopeValue='INJECTION')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('ProductFamily','INJECTION','注塑产品族');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='ProductFamily' AND ScopeValue='ASSEMBLY')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('ProductFamily','ASSEMBLY','装配产品族');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='ProductFamily' AND ScopeValue='TEST')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('ProductFamily','TEST','测试产品族');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='Department' AND ScopeValue='PMC_DEPT')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('Department','PMC_DEPT','计划部');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='Domain' AND ScopeValue='FAMILY_INJECTION')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('Domain','FAMILY_INJECTION','注塑域');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='Domain' AND ScopeValue='FAMILY_ASSEMBLY')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('Domain','FAMILY_ASSEMBLY','装配域');
IF NOT EXISTS (SELECT 1 FROM DataScopePolicy WHERE ScopeType='Domain' AND ScopeValue='FAMILY_TEST')
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description) VALUES ('Domain','FAMILY_TEST','测试域');
```

### 段 F — RoleDataScope 关联（按角色挂范围）

```sql
-- aps.admin.system 已有 Global（DDL:556 挂过）→ 不动
-- aps.admin.aps 挂 Factory=BJ
DECLARE @ApsAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.admin.aps');
INSERT INTO RoleDataScope (RoleId, ScopePolicyId)
SELECT @ApsAdminRoleId, dsp.Id
FROM DataScopePolicy dsp
WHERE dsp.ScopeType='Factory' AND dsp.ScopeValue='BJ'
  AND NOT EXISTS (
    SELECT 1 FROM RoleDataScope rds
    WHERE rds.RoleId=@ApsAdminRoleId AND rds.ScopePolicyId=dsp.Id
  );

-- aps.planner 挂 3 个 ProductFamily
DECLARE @PlannerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.planner');
INSERT INTO RoleDataScope (RoleId, ScopePolicyId)
SELECT @PlannerRoleId, dsp.Id
FROM DataScopePolicy dsp
WHERE dsp.ScopeType='ProductFamily'
  AND dsp.ScopeValue IN ('INJECTION','ASSEMBLY','TEST')
  AND NOT EXISTS (
    SELECT 1 FROM RoleDataScope rds
    WHERE rds.RoleId=@PlannerRoleId AND rds.ScopePolicyId=dsp.Id
  );

-- aps.viewer.management 挂 Factory=BJ + ProductFamily=INJECTION（窄范围示例）
DECLARE @ViewerRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.viewer.management');
INSERT INTO RoleDataScope (RoleId, ScopePolicyId)
SELECT @ViewerRoleId, dsp.Id
FROM DataScopePolicy dsp
WHERE (dsp.ScopeType='Factory' AND dsp.ScopeValue='BJ')
   OR (dsp.ScopeType='ProductFamily' AND dsp.ScopeValue='INJECTION')
  AND NOT EXISTS (
    SELECT 1 FROM RoleDataScope rds
    WHERE rds.RoleId=@ViewerRoleId AND rds.ScopePolicyId=dsp.Id
  );
```

---

## 四、4 号位的测试期望矩阵

| 用户 | 期望 Roles | 期望 permissions 长度 | 期望 dataScope |
|---|---|---|---|
| `admin` | `[aps.admin.system]` | 34 | IsGlobal=true, 其余空数组 |
| `viewer` | `[aps.viewer.management]` | 10 | IsGlobal=false, factories=[BJ], productFamilies=[INJECTION] |
| `pmc` | `[aps.planner]` | 17 | IsGlobal=false, factories=[], productFamilies=[INJECTION,ASSEMBLY,TEST] |
| `rule_admin` | `[aps.admin.aps]` | 8 | IsGlobal=false, factories=[BJ], productFamilies=[] |
| `rule_publisher` | `[aps.admin.aps]` | 8 | IsGlobal=false, factories=[BJ], productFamilies=[] |
| `sys_admin` | `[aps.admin.system]` | 34 | IsGlobal=true, 其余空数组 |

> 前端 mock 角色名（`PMC` / `VIEWER` / `RULE_ADMIN` / `RULE_PUBLISHER` / `SYSTEM_ADMIN`）由前端 store 内部维护，作为 `userInfo.permissions` 的"展示别名"。
> 实际登录后端后，前端拿到的 `roles` 是 DDL RoleCode（`aps.planner` / `aps.viewer.management` 等），不是前端 mock 名。

## 五、执行顺序（4 号位视角）

1. 3 号位写完 SQL 补丁并本地跑通
2. 3 号位**回执 4 号位**
3. 4 号位起后端服务（`dotnet run --project lps/LPS.APS.Web`，端口 5163）
4. 4 号位跑 `curl -X POST http://localhost:5163/api/auth/login -d '{"userCode":"admin","password":"123456"}' -H "Content-Type: application/json"` 验证登录通
5. 4 号位拿 accessToken 调 `/api/auth/me`，校验 11 字段符合本表预期
6. 4 号位启动前端 Phase D（HTTP 客户端）

## 六、风险与注意

| 风险 | 缓解 |
|---|---|
| 5 用户密码统一 `123456` 仅 dev 用 | SQL 顶部加注释「PRODUCTION MUST REMOVE」 |
| 段 A 替换 admin 密码影响审计 | 文档明示 `admin` 是 DDL 自带，本就只用于 dev |
| rule_admin / rule_publisher 共用 `aps.admin.aps` 角色无法区分 | 本批次简化方案，后续走 UserPermission 表细分；前端用 mock 角色名做展示区分 |
| `ResourceOrgGroup` Scope 不挂 | V1 mock 不需要；前端 5 维度中此维度空数组 |
| ScopeType='Global' 已存在，唯一约束会拒重复 | 用 `IF NOT EXISTS` 判 `(ScopeType, ScopeValue)` |
| 执行顺序：DDL → 本补丁 → 启动后端 | 4 号位会等 3 号位回执后再起后端 |

## 七、签署

- 3 号位写完补丁并回执 → 4 号位启动 Phase D
- 4 号位收到回执后跑通 `/api/auth/me` → 启动 Phase E（端到端 15 步）
