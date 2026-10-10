# APS_Auth 数据库字段说明文档 v1.1（20260901冻结对齐版）

**文档定位**：APS_Auth认证、角色权限、业务范围权限、审计及审批兼容结构的字段说明  
**适用范围**：APS V1  
**数据库名称**：`APS_Auth`  
**版本**：v1.1  
**日期**：2026-09-01（北京时间）  
**数据库Owner**：3号位  
**基于**：
- 《APS_Auth数据库字段说明文档 v1.0》
- 《APS_Auth数据库DDL v1.0》
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》
- 《APS 权限与审批系统实施方案 v1.1（20260901冻结对齐版）》
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》

---

# 0. v1.1变更摘要

v1.1不推翻v1.0现有13张表。

主要调整：

1. APS_Auth完整归3号位；
2. V1正式能力为：
   - 身份认证（Authentication）
   - 功能权限（RBAC）
   - 业务范围权限（Business Scope）
   - 审计（Audit）
3. `DataScopePolicy.ScopeType`正式增加：
   - `Department`
   - `Domain`
4. 原：
   - `Factory`
   - `ProductFamily`
   - `ResourceOrgGroup`
   - `Global`
   继续兼容；
5. `ResourceOrgGroup`不再替代Department/Domain；
6. Approval四表继续保留，但标记为审批扩展兼容结构，不进入当前V1默认主链；
7. Permission种子按当前V1业务动作补齐；
8. 原冻结区/已开工修改/旧CTP Commit/通用Approval权限码仅兼容保留，不作为当前V1新主链依赖；
9. 不在本权限数据库文档中要求修改PlanVersion、ScheduleRun、Task等APS核心运行表。

---

# 1. 表清单

| 序号 | 表名 | 中文名称 | v1.1定位 |
|---:|---|---|---|
| 1 | User | 用户表 | V1正式 |
| 2 | Role | 角色表 | V1正式 |
| 3 | Permission | 功能权限表 | V1正式 |
| 4 | UserRole | 用户角色关联 | V1正式 |
| 5 | RolePermission | 角色权限关联 | V1正式 |
| 6 | DataScopePolicy | 业务范围策略 | V1正式 |
| 7 | UserDataScope | 用户业务范围 | V1正式 |
| 8 | RoleDataScope | 角色业务范围 | V1正式 |
| 9 | AuditLog | 审计日志 | V1正式 |
| 10 | ApprovalFlow | 审批流 | 兼容/扩展 |
| 11 | ApprovalNode | 审批节点 | 兼容/扩展 |
| 12 | ApprovalRecord | 审批记录 | 兼容/扩展 |
| 13 | ApprovalRule | 审批规则 | 兼容/扩展 |

---

# 2. User（用户表）

业务用途：

> APS独立用户账号、登录身份、启停状态和审计主体。

字段保持v1.0不变：

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | INT | PK, IDENTITY | 用户ID |
| LoginName | NVARCHAR(50) | NOT NULL, UNIQUE | 登录名 |
| DisplayName | NVARCHAR(100) | NOT NULL | 显示名 |
| PasswordHash | NVARCHAR(500) | NOT NULL | 密码哈希 |
| Email | NVARCHAR(100) | NULL | 邮箱 |
| PhoneNumber | NVARCHAR(20) | NULL | 电话 |
| IsEnabled | BIT | NOT NULL | 是否启用 |
| LastLoginAt | DATETIME2 | NULL | 最后登录时间 |
| CreatedAt | DATETIME2 | NOT NULL | 创建时间 |
| UpdatedAt | DATETIME2 | NOT NULL | 更新时间 |

规则：

- 密码只能保存安全Hash；
- `TEMP_PASSWORD_HASH`仅允许开发初始化占位；
- Disabled用户不得继续取得有效访问权限；
- User删除时现有关联按DDL级联关系执行。

---

# 3. Role（角色表）

角色是功能权限和默认业务Scope的组织模板。

字段保持v1.0。

预置角色可以继续保留：

- `aps.admin.system`
- `aps.admin.aps`
- `aps.planner`
- `aps.supervisor.workshop`
- `aps.coordinator.material`
- `aps.viewer.management`
- `aps.service.api`

重要说明：

> 角色不等于业务范围。

例如两个`aps.planner`用户可以分别负责不同Factory/ProductFamily/Department/Domain。

---

# 4. Permission（功能权限表）

字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | INT | 权限ID |
| PermissionCode | NVARCHAR(100) | 权限编码 |
| PermissionName | NVARCHAR(100) | 权限名称 |
| Module | NVARCHAR(50) | 模块 |
| ActionType | NVARCHAR(50) | View/Edit/Execute |
| Description | NVARCHAR(500) | 描述 |
| CreatedAt | DATETIME2 | 创建时间 |

编码规则：

```text
aps.<module>.<action>
```

## 4.1 当前V1正式建议权限码

### Plan

- `aps.plan.view`
- `aps.plan.run`
- `aps.plan.compare`
- `aps.plan.export`

### CTP / Insert / Reschedule

- `aps.ctp.view`
- `aps.ctp.evaluate`
- `aps.insert.impact.evaluate`
- `aps.reschedule.local`
- `aps.reschedule.manual`

### Candidate

- `aps.candidate.view`
- `aps.candidate.confirm`
- `aps.candidate.activate`

### Rule / Parameter / Strategy

- `aps.rule.view`
- `aps.rule.edit`
- `aps.rule.publish`
- `aps.parameter.view`
- `aps.parameter.edit`
- `aps.strategy.view`
- `aps.strategy.edit`
- `aps.strategy.publish`

### Manual ETA

- `aps.manual_eta.view`
- `aps.manual_eta.edit`
- `aps.manual_eta.cancel`

### Demand Protection

- `aps.demand_protection.view`
- `aps.demand_protection.release`

### MES

- `aps.mes.view`
- `aps.mes.dispatch`

### Authentication / Authorization

- `aps.auth.user.view`
- `aps.auth.user.edit`
- `aps.auth.role.view`
- `aps.auth.role.edit`
- `aps.auth.permission.assign`
- `aps.auth.scope.assign`
- `aps.audit.view`

## 4.2 兼容权限码

下列v1.0权限如数据库中已存在，不要求立即删除：

- `aps.task.freeze.override`
- `aps.task.started.override`
- `aps.ctp.commit`
- `aps.approval.view`
- `aps.approval.approve`

但标记为：

> **历史兼容，不作为当前V1新业务主链依据。**

这些权限码的存在不能自动推出冻结区修改、已开工修改或通用多级审批已经是当前V1正式业务。

---

# 5. UserRole

用户与角色多对多关系。

字段保持v1.0：

- UserId
- RoleId
- AssignedAt
- AssignedBy

角色只负责组织权限，不替代业务Scope。

---

# 6. RolePermission

角色与Permission多对多。

字段保持v1.0：

- RoleId
- PermissionId
- AssignedAt

V1权限判断至少满足：

> 用户拥有某角色 → 角色拥有权限码 → 当前用户获得功能权限。

如后续支持用户直接功能权限，需要另行冻结；本版不新增直接UserPermission表。

---

# 7. DataScopePolicy（业务范围策略）

## 7.1 业务用途

定义“用户/角色可在哪些业务范围内查看或操作”。

不是任意记录ACL，也不是字段级权限。

字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | INT | Scope策略ID |
| ScopeType | NVARCHAR(50) | 范围类型 |
| ScopeValue | NVARCHAR(100) | 范围值 |
| Description | NVARCHAR(500) | 描述 |
| CreatedAt | DATETIME2 | 创建时间 |

## 7.2 v1.1 ScopeType

正式值域：

| ScopeType | 说明 | V1状态 |
|---|---|---|
| Global | 全局 | 正式 |
| Factory | 工厂 | 正式 |
| ProductFamily | 产品族 | 正式 |
| Department | 部门/生产部门 | 正式 |
| Domain | 计划域 | 正式 |
| ResourceOrgGroup | 资源组织维度 | 兼容/可扩展 |

`ResourceOrgGroup`不能替代`Department`或`Domain`。

## 7.3 ScopeValue

继续使用`NVARCHAR(100)`，不强制只保存INT。

允许按当前权威业务主键使用：

- FactoryCode / Id；
- ProductFamilyCode / Id；
- DeptCode / Id；
- DomainKey。

具体采用何种业务键，应与对应权威基础数据契约一致。

## 7.4 Scope组合

同一维度：并集。

例如：

```text
Factory = BJ + TJ
```

不同维度：共同约束。

例如：

```text
Factory in {BJ,TJ}
AND Department = MC
```

这用于支持：

> “能看北京和天津，但只能看机加部门。”

---

# 8. UserDataScope

用户直接业务范围。

字段保持：

- UserId
- ScopePolicyId
- AssignedAt
- AssignedBy

用户有效Scope由UserDataScope与角色Scope共同形成。

---

# 9. RoleDataScope

角色默认业务范围。

字段保持：

- RoleId
- ScopePolicyId
- AssignedAt

角色Scope与用户直接Scope的最终合成，由3号位权限服务实现。

---

# 10. AuditLog

## 10.1 定位

记录权限与APS关键业务动作的审计事实。

现有字段保持：

- Id
- UserId
- UserName
- ActionCode
- ObjectType
- ObjectId
- Result
- OccurredAt
- ClientIp
- PlanVersionId
- BatchNo
- ApprovalId
- RequestData
- ResponseData
- ErrorMessage

## 10.2 Result

- Success
- Failed
- Denied

## 10.3 当前V1至少记录

- Login / Logout；
- 用户/角色/权限/Scope维护；
- CTP；
- INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate确认/激活；
- FAILED恢复；
- Rule/Parameter/Strategy发布；
- Manual ETA编辑/取消；
- Demand Protection释放；
- MES受控操作。

## 10.4 安全要求

不得在RequestData/ResponseData记录：

- 明文密码；
- PasswordHash；
- Token；
- Refresh Token；
- 其它认证密钥。

---

# 11. ApprovalFlow / ApprovalNode / ApprovalRecord / ApprovalRule

## 11.1 v1.1统一定位

四张表继续保留，但：

> **属于审批扩展兼容结构，不是当前APS V1默认强制业务主链。**

旧v1.0中出现的：

- FreezeTaskAdjust
- SOSacrifice
- StartedTaskAdjust

仅作为历史设计值/兼容数据理解。

除非新的正式业务裁决重新启用，否则：

- 不要求创建这些ApprovalFlow；
- 不要求4号位建设对应审批页面；
- 不要求2号位在排程主流程等待通用审批；
- 不把Candidate人工确认映射成ApprovalFlow。

## 11.2 ApprovalRule

现有字段中的：

- FactoryId
- ProductFamilyId
- ResourceOrgGroupId

继续保留历史兼容。

本次不为Approval扩展增加DepartmentId/DomainKey，因为当前V1并未正式启用通用审批主链。

若未来正式启用Approval，再根据届时审批业务重新设计，不提前过度设计。

---

# 12. 数据关系

当前正式权限主链：

```text
User
 ├─ UserRole ─ Role ─ RolePermission ─ Permission
 └─ UserDataScope
                   \
                    DataScopePolicy
                   /
Role ─ RoleDataScope

User ─ AuditLog
```

审批兼容结构：

```text
ApprovalFlow
 └─ ApprovalNode
     └─ ApprovalRecord

ApprovalRule
```

审批结构与当前V1主排程流程没有默认强绑定关系。

---

# 13. 认证接口所需字段

`Current User`建议至少返回：

- UserId
- LoginName
- DisplayName
- Roles[]
- Permissions[]
- IsGlobal
- Factories[]
- ProductFamilies[]
- Departments[]
- Domains[]

如继续使用ResourceOrgGroup：

- ResourceOrgGroups[]

该返回模型是接口DTO，不要求全部反写User表字段。

---

# 14. 数据库Owner

`APS_Auth`整体由3号位Owner：

- DDL
- Entity
- DbContext
- Repository
- Service
- Controller
- Seed
- Migration/Upgrade Script

2号位不再承担APS_Auth结构执行职责。

---

# 15. 与APS核心运行库边界

本权限数据库文档不要求：

- PlanVersion新增CreatedByUserId跨库FK；
- BatchLog新增TriggeredByUserId跨库FK；
- Task新增权限字段；
- Allocation新增用户字段；
- ScheduleRun新增Auth外键。

若现有APS核心字段不足以满足业务审计，需要先回到对应权威字段/DDL文档评审。

不得由Auth文档单方面修改APS核心运行库。

---

# 16. v1.0 → v1.1升级动作

对已有APS_Auth v1.0：

1. 保留13张表；
2. 修改`CK_DataScope_Type`；
3. 增加`Department`；
4. 增加`Domain`；
5. 保留`ResourceOrgGroup`；
6. 增加当前V1权限码；
7. 不删除旧权限码；
8. 不删除Approval表；
9. 不重置User/Role/Scope数据；
10. 不重建数据库。

---

# 17. 最终冻结摘要

> **APS_Auth完整归3号位。**

> **正式V1主能力为Authentication + RBAC + Business Scope + Audit。**

> **业务范围至少包括Factory、ProductFamily、Department/ProductionDepartment、Domain。**

> **ResourceOrgGroup可兼容保留，但不能替代Department/Domain。**

> **Approval四表保留，但不作为当前V1默认强制主流程。**

> **现有13表结构总体复用，只做必要增量升级。**

> **本权限库不得反向修改APS核心排程业务。**
