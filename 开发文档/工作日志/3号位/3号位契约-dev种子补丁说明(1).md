# 3号位契约 — APS_Auth dev 种子增量补丁

**接收人**：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
**来源**：4号位（前端页面与业务操作）
**生成日期**：2026-09-10
**前置文档**：[3号位契约-种子数据补齐.md](3号位契约-种子数据补齐.md)（v1，已废方向）+ [APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql](../../冻结文档/APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql)
**目的**：DDL v1.3 已冻结（13 表 + 7 系统角色 + 34 权限码 + admin 占位 + Global Scope），**主 DDL 不动**。本补丁单独一个 SQL 脚本，补 dev 联调必需的 5 测试用户 + 真实密码哈希 + RolePermission 关联 + 非 Global Scope 策略，让前端能跑通 `/api/auth/login` → `/api/auth/me` 的真实链路。

---

## 一、DDL 已种的内容（不需要重做）

| 项 | DDL 行 | 状态 |
|---|---|---|
| 13 张表 | 47-378 | ✅ |
| 7 个系统角色 | 383-394 | ✅（含 `aps.admin.system` / `aps.planner` 等） |
| 34 个 V1 权限码 | 402-510 | ✅ |
| `admin` 用户 + 挂 `aps.admin.system` | 522-535 | ✅ 但密码是 `TEMP_PASSWORD_HASH` 占位符 |
| Global Scope (`Global=*`) + 挂 `aps.admin.system` | 541-566 | ✅ |

## 二、本补丁需补的 5 段（独立 SQL 脚本）

**建议路径**：`lps/LPS.APS.Web/Sql/APS_Auth_v1.3_dev_seed_data_20260910.sql`
**执行时机**：DDL 主脚本跑完后；前端 Phase D 调 `/api/auth/me` 之前
**幂等要求**：所有 INSERT 必须先 `IF NOT EXISTS` 判定；密码哈希在脚本里**预先生成好**写入（不是运行时算）

### 段 A — 替换 admin 密码占位

DDL:524 写死 `TEMP_PASSWORD_HASH`，PBKDF2 验证会失败，admin 登不进去。

```sql
IF EXISTS (SELECT 1 FROM [User] WHERE LoginName='admin' AND PasswordHash='TEMP_PASSWORD_HASH')
BEGIN
    UPDATE [User]
    SET PasswordHash = '<PBKDF2 哈希>"123456" 的字符串>',  -- 4号位不知道哈希算法，由 3号位生成
        UpdatedAt = GETDATE()
    WHERE LoginName='admin';
END
```

**实现说明**：
- 哈希字符串请用本地 `dotnet run` 起 `PasswordHasher.Hash("123456")` 生成，复制粘贴进 SQL
- `PasswordHasher` 在 [LPS.APS.Core/Security/PasswordHasher.cs](../../lps/LPS.APS.Core/Security/PasswordHasher.cs)，3号位参考即可

### 段 B — 4 个测试用户

```sql
IF NOT EXISTS (SELECT 1 FROM [User] WHERE LoginName='viewer')
BEGIN
    INSERT INTO [User] (LoginName, DisplayName, PasswordHash, Email, IsEnabled)
    VALUES ('viewer', '查看员', '<同段 A 哈希>', 'viewer@aps.local', 1);
END
-- 同结构重复 3 条：pmc / rule_admin / rule_publisher
```

| LoginName | DisplayName | Email | IsEnabled |
|---|---|---|---|
| `viewer` | 查看员 | viewer@aps.local | 1 |
| `pmc` | 计划员 | pmc@aps.local | 1 |
| `rule_admin` | 规则管理员 | rule_admin@aps.local | 1 |
| `rule_publisher` | 规则发布员 | rule_publisher@aps.local | 1 |

> 注：4号位前端 mock 角色名是 PMC / VIEWER / RULE_ADMIN / RULE_PUBLISHER / SYSTEM_ADMIN。
> 3 号位 DDL v1.3 的 RoleCode 是 `aps.planner` / `aps.viewer.management` / `aps.admin.aps` 等。
> 这是**命名不一致**，本补丁先按 DDL 现有 RoleCode 挂测试用户，
> **是否需要新建 5 个非系统角色**由 3 号位裁决（详见段 C 末「协调项」）。

### 段 C — RolePermission 关联

DDL 只种了 Permission，没把权限码挂到角色上，导致 admin 登录后 `permissions=[]`。

**两种走法二选一**：

#### 走法 1（推荐，最小改动）：把 34 权限码全挂到 `aps.admin.system`
```sql
INSERT INTO RolePermission (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM [Role] r CROSS JOIN Permission p
WHERE r.RoleCode='aps.admin.system'
  AND NOT EXISTS (
    SELECT 1 FROM RolePermission rp
    WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id
  );
```

#### 走法 2：按业务角色挂最小集
DDL 已有的 7 系统角色 + 34 权限码，挂法（4 号位建议）：

| RoleCode | 挂的权限码 |
|---|---|
| `aps.admin.system` | **全部 34 个** |
| `aps.admin.aps` | 规则/参数/策略全部（6）+ Plan/Candidate 查看（4）+ Auth 用户/角色/权限/Scope 分配（5）= 15 |
| `aps.planner` | Plan 全部（4）+ CTP 全部（2）+ Reschedule 全部（2）+ Candidate 全部（3）+ Manual ETA 全部（3）+ MES.view + DemandProtection.view = 16 |
| `aps.supervisor.workshop` | Plan.view + CTP.view + Candidate.view + ManualETA 全部（3）+ MES 全部（2）= 8 |
| `aps.coordinator.material` | Plan.view + ManualETA.view + DemandProtection.view = 3 |
| `aps.viewer.management` | Plan.view + CTP.view + Candidate.view + Rule.view + Parameter.view + Strategy.view + ManualETA.view + DemandProtection.view + MES.view + Audit.view = 10 |
| `aps.service.api` | **空**（按需动态挂） |

> 4 号位**不裁决**，3 号位按业务口径决定。

**挂示例**（以 `aps.planner` 为例）：
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

**协调项（4 号位建议）**：
前端 mock 角色名 `PMC` / `VIEWER` 等不匹配 DDL `aps.planner` / `aps.viewer.management`。
- **方案 ①**：3 号位新建 5 个 RoleCode 为 `PMC` / `VIEWER` / `RULE_ADMIN` / `RULE_PUBLISHER` / `SYSTEM_ADMIN` 的非系统角色（IsSystemRole=0），专门给前端 dev 用；保留原 7 系统角色给生产
- **方案 ②**：前端 mock 角色名改名为 `aps.planner` / `aps.viewer.management` 等
- 4 号位倾向方案 ①（前端无需改 mock 名字），最终由 3 号位裁决

### 段 D — 非 Global Scope 策略（6 条）

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

### 段 E — RoleDataScope 关联（按角色挂范围）

**走法 1（推荐，对应段 C 走法 1）**：`aps.admin.system` 已有 Global，挂其它范围不动它。

**走法 2（对应段 C 走法 2）**：每个业务角色挂对应 Scope：
```sql
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

-- aps.supervisor.workshop 挂 Factory=BJ
DECLARE @WorkshopRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode='aps.supervisor.workshop');
INSERT INTO RoleDataScope (RoleId, ScopePolicyId)
SELECT @WorkshopRoleId, dsp.Id
FROM DataScopePolicy dsp
WHERE dsp.ScopeType='Factory' AND dsp.ScopeValue='BJ'
  AND NOT EXISTS (
    SELECT 1 FROM RoleDataScope rds
    WHERE rds.RoleId=@WorkshopRoleId AND rds.ScopePolicyId=dsp.Id
  );

-- aps.coordinator.material 不挂 Scope（按需后续）
-- aps.viewer.management 不挂 Scope（管理层按 Global）

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
```

---

## 三、4 号位的测试用户 / Scope 矩阵

如果 3 号位选**方案 ①**（新建 5 个非系统角色），挂法对应：

| 用户 | 挂的角色 RoleCode | 期望 Roles | 期望 permissions 长度 | 期望 dataScope |
|---|---|---|---|---|
| `viewer` | `VIEWER` | `[VIEWER]` | 9 | IsGlobal=false, factories=[], productFamilies=[INJECTION] |
| `pmc` | `PMC` | `[PMC]` | 16 | IsGlobal=false, factories=[], productFamilies=[INJECTION,ASSEMBLY,TEST] |
| `rule_admin` | `RULE_ADMIN` | `[RULE_ADMIN]` | 7 | IsGlobal=false, factories=[BJ], productFamilies=[] |
| `rule_publisher` | `RULE_PUBLISHER` | `[RULE_PUBLISHER]` | 6 | IsGlobal=false, factories=[BJ], productFamilies=[] |
| `sys_admin` | `SYSTEM_ADMIN` | `[SYSTEM_ADMIN]` | 34 | IsGlobal=true, 其余空数组 |

> 段 B 的 4 用户（viewer/pmc/rule_admin/rule_publisher）**只插 User 不插 UserRole**，等角色在方案 ① / ② 落定后再插 UserRole。
> 段 A 的 `admin` 已经是 UserRole 关联 `aps.admin.system`，**不动**。

---

## 四、执行顺序（4 号位视角）

1. 3 号位写完 SQL 补丁并本地跑通
2. 3 号位**回执 4 号位**
3. 4 号位起后端服务（`dotnet run --project lps/LPS.APS.Web`，端口 5163）
4. 4 号位跑 `curl -X POST http://localhost:5163/api/auth/login -d '{"userCode":"admin","password":"123456"}' -H "Content-Type: application/json"` 验证登录通
5. 4 号位拿 accessToken 调 `/api/auth/me`，校验 11 字段符合本表预期
6. 4 号位启动前端 Phase A（DTO 类型对齐）

---

## 五、风险与注意

| 风险 | 缓解 |
|---|---|
| 密码统一 `123456` 仅 dev 用 | SQL 顶部加注释「PRODUCTION MUST REMOVE」 |
| 段 A 替换 admin 密码影响审计 | 文档明示 `admin` 是 DDL 自带，本就只用于 dev |
| 走法 1 把 34 码全挂 admin 看起来过度授权 | admin 本就是 dev 用，生产前会换成 `aps.admin.system` + 走法 2 细分 |
| 段 C 方案 ①/② 不确定 | **3 号位裁决**；4 号位倾向 ①，文档已列 |
| `ResourceOrgGroup` Scope 不挂 | V1 mock 不需要；前端 5 维度中此维度空数组 |
| ScopeType='Global' 已存在，唯一约束会拒重复 | 用 `IF NOT EXISTS` 判 `(ScopeType, ScopeValue)` |
| 执行顺序：DDL → 本补丁 → 启动后端 | 4 号位会等 3 号位回执后再起后端 |

---

## 六、签署

- 3 号位写完补丁并回执 → 4 号位启动 Phase A
- 4 号位收到回执后跑通 `/api/auth/me` → 启动 Phase D
