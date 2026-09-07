# APS 权限与审批系统实施方案 v1.1（20260901冻结对齐版）

**文档定位**：基于《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》形成的APS V1认证、角色权限、业务范围权限、审计及审批扩展实施方案  
**适用范围**：APS V1 单体解决方案  
**版本**：v1.1  
**日期**：2026-09-01（北京时间）  
**基于**：
- 《APS 权限与审批系统实施方案 v1.0》
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》
- 《APS_Auth数据库DDL_v1.0》
- 《APS_Auth数据库字段说明文档_v1.0》
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》
- 《APS 集成接口设计规范 v1.28（20260901职责与前端接口冻结对齐版）》
- 当前APS V1最终业务冻结基线

---

# 0. 本版实施原则

本实施方案只落实：

- 身份认证（Authentication）；
- 角色与功能权限（RBAC）；
- 业务范围权限（Business Scope Permission）；
- 审计（Audit）；
- 4号位、5号位、2号位对权限上下文的接入；
- 审批（Approval）的兼容保留边界。

不得因为权限/审批实现新增或改变以下冻结业务：

- Pegging；
- Supply选择；
- Allocation；
- AvailableTime；
- Demand Protection算法；
- RunType / Purpose；
- Candidate激活条件；
- PlanVersion；
- Solver；
- MES下发资格；
- PAUSE/RESUME；
- MultiDomain Candidate。

---

# 1. 原v1.0现有资产处理

现有APS_Auth v1.0已经建立13张表，其中包括：

- User
- Role
- Permission
- UserRole
- RolePermission
- DataScopePolicy
- UserDataScope
- RoleDataScope
- AuditLog
- ApprovalFlow
- ApprovalNode
- ApprovalRecord
- ApprovalRule

本次不推翻已有结构。

处理原则：

## 1.1 继续作为V1正式核心表

以下9张表继续作为V1正式使用：

1. User
2. Role
3. Permission
4. UserRole
5. RolePermission
6. DataScopePolicy
7. UserDataScope
8. RoleDataScope
9. AuditLog

## 1.2 审批表保留，但不作为V1默认强制业务主链

以下4张表保留：

- ApprovalFlow
- ApprovalNode
- ApprovalRecord
- ApprovalRule

但从v1.1开始定义为：

> **审批扩展兼容结构。**

除非当前冻结业务另有明确裁决，不要求4号位、3号位、5号位或2号位在V1主流程中强制进入多级Approval Flow。

因此：

- 不删除旧表；
- 不扩展旧审批业务；
- 不用审批表反向改变当前冻结业务；
- 不新增“冻结区/牺牲SO/已开工任务”等旧审批主链。

---

# 2. 代码Owner正式调整

## 2.1 3号位完整接管Auth

原2号位现有Auth相关代码全部移交3号位。

包括但不限于：

- `AuthDbContext`
- `Repositories/Auth/*`
- `AuthService`
- `AuthController`
- Auth DTO
- Auth实体
- `IAuthService`
- 登录结果模型
- APS_Auth DDL
- APS_Auth字段说明

从本版开始：

> **认证、权限、业务范围、权限审计均为3号位完整Owner。**

2号位不再维护Auth代码。

---

# 3. 数据库实施范围

## 3.1 APS_Auth归3号位

3号位负责：

- DDL；
- Entity；
- DbContext；
- Repository；
- Service；
- Controller；
- Seed数据；
- 升级脚本。

不再采用：

> 3号位设计，2号位代执行DDL。

---

## 3.2 DataScopePolicy 必须升级

v1.0当前约束：

```text
Factory
ProductFamily
ResourceOrgGroup
Global
```

v1.1正式至少支持：

```text
Factory
ProductFamily
Department
Domain
Global
```

`ResourceOrgGroup`：

- 可以兼容保留；
- 不能替代Department；
- 不能替代Domain；
- 是否继续实际使用由后续页面/业务范围配置决定。

建议v1.1约束：

```text
Factory
ProductFamily
Department
Domain
ResourceOrgGroup
Global
```

这样对现有v1.0兼容性最好。

---

# 4. V1角色与权限实施

## 4.1 角色

现有角色种子可以保留：

- `aps.admin.system`
- `aps.admin.aps`
- `aps.planner`
- `aps.supervisor.workshop`
- `aps.coordinator.material`
- `aps.viewer.management`
- `aps.service.api`

不要求删除。

但不能再通过角色名直接硬编码业务范围。

例如：

> `aps.planner` 不代表自动拥有所有工厂/所有产品族。

角色决定默认功能权限；业务范围由Scope单独配置。

---

## 4.2 功能权限

v1.0中的以下旧权限可以保留兼容：

- `aps.plan.view`
- `aps.plan.run`
- `aps.task.view`
- `aps.ctp.view`
- `aps.ctp.evaluate`
- `aps.audit.view`
- `aps.user.view`
- `aps.user.manage`
- `aps.role.view`
- `aps.role.manage`

但需要新增/调整为当前V1业务动作。

建议新增：

### 运行与计划

- `aps.plan.view`
- `aps.plan.run`
- `aps.plan.compare`
- `aps.plan.export`

### 白天试算/重排

- `aps.ctp.view`
- `aps.ctp.evaluate`
- `aps.insert.impact.evaluate`
- `aps.reschedule.local`
- `aps.reschedule.manual`

### Candidate

- `aps.candidate.view`
- `aps.candidate.confirm`
- `aps.candidate.activate`

### 规则/参数/策略

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

### Auth

- `aps.auth.user.view`
- `aps.auth.user.edit`
- `aps.auth.role.view`
- `aps.auth.role.edit`
- `aps.auth.permission.assign`
- `aps.auth.scope.assign`
- `aps.audit.view`

---

# 5. 旧权限码的兼容处理

以下v1.0权限码包含已过时或容易误导的业务语义：

- `aps.task.freeze.override`
- `aps.task.started.override`
- `aps.ctp.commit`
- `aps.approval.view`
- `aps.approval.approve`

处理原则：

## 5.1 不作为当前V1主链新增依赖

不得因为这些权限码已存在，就要求：

- 冻结区任务直接修改；
- 已开工任务修改；
- 插单方案“commit”成旧业务；
- 通用审批流强制介入。

## 5.2 兼容保留

如果已有数据库和代码依赖，可暂时保留Seed和表记录。

但应标记：

> `LEGACY_COMPAT / NOT_USED_IN_CURRENT_V1_MAIN_FLOW`

后续确认没有引用后再清理。

这样避免因本次权限调整大范围破坏已有代码。

---

# 6. 身份认证实施

## 6.1 正式能力

3号位实现：

- Login
- Refresh
- Logout
- Current User
- Disabled User拦截
- Password Hash
- Token校验

## 6.2 Refresh Token

原v1.0实施方案曾写：

> JWT 2小时，无Refresh Token。

但当前代码事实已经存在：

- login
- refresh
- logout
- me

因此v1.1以现有代码壳为准：

> **保留Refresh能力，不因为旧文档“无Refresh Token”而回退现有实现。**

具体Token存储方式由3号位在不改变前端契约的前提下实现。

---

# 7. Current User 返回契约

建议Current User至少返回：

```text
UserId
LoginName
DisplayName
Roles[]
Permissions[]
Scopes
```

Scopes至少包含：

```text
Factories[]
ProductFamilies[]
Departments[]
Domains[]
IsGlobal
```

如保留ResourceOrgGroup：

```text
ResourceOrgGroups[]
```

可以作为兼容扩展字段。

---

# 8. Business Scope 实施

## 8.1 现有结构复用

继续复用：

- DataScopePolicy
- UserDataScope
- RoleDataScope

不新增第二套：

- FactoryPermission
- ProductFamilyPermission
- DepartmentPermission
- DomainPermission

避免表数量膨胀。

---

## 8.2 ScopeValue

v1.1不强制所有范围值必须为INT ID。

因为当前业务中：

- Factory可能使用FactoryCode；
- Department可能使用DeptCode；
- Domain使用DomainKey；
- ProductFamily可能使用业务编码或ID。

因此：

> `ScopeValue NVARCHAR(100)` 继续保留是合理的。

由ScopeType决定其值解释。

---

## 8.3 Scope组合规则

同一维度：

> 并集。

例如：

```text
Factory = BJ,TJ
```

不同维度：

> 共同约束。

例如：

```text
Factory in (BJ,TJ)
AND Department = MC
```

这样支持：

> “北京和天津，但只允许机加部门。”

---

# 9. 业务范围在3/4/5/2号位之间的实施方式

## 9.1 3号位

负责：

- 认证；
- 权限码；
- Scope管理；
- Current User上下文。

## 9.2 4号位

负责：

- 路由守卫；
- 菜单权限；
- 按钮权限；
- Scope筛选项限制；
- 无权限提示。

不得只靠前端作为安全边界。

## 9.3 5号位

面向4号位的业务接口必须：

1. 读取用户身份；
2. 校验功能权限；
3. 限定Factory/ProductFamily/Department/Domain；
4. 调5号位自身事实Service或2号位Service。

## 9.4 2号位

接收：

- 合法Scope；
- 业务参数。

按Scope执行查询或业务动作。

2号位不维护用户角色规则。

---

# 10. 审计实施

## 10.1 AuditLog继续使用

现有AuditLog结构基本可保留。

V1必须审计：

- Login / Logout；
- 用户/角色/权限/Scope变更；
- CTP；
- 插单影响分析；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate确认/激活；
- FAILED恢复；
- Rule/Parameter/Strategy发布；
- Manual ETA编辑/取消；
- Demand Protection释放；
- MES受控操作。

## 10.2 不要求所有请求保存完整Request/Response

`RequestData` / `ResponseData` 字段可继续保留。

但V1原则：

- 关键业务动作按需要记录；
- 密码、Token等敏感值不得记录；
- 不要求普通查询全部保存大JSON。

---

# 11. Approval表实施边界

现有：

- ApprovalFlow
- ApprovalNode
- ApprovalRecord
- ApprovalRule

本版不删除。

但：

> 不把这些表加入当前V1强制主流程。

旧DDL中的：

- FreezeTaskAdjust
- SOSacrifice
- StartedTaskAdjust

不再作为当前V1默认正式审批类型。

旧Seed/示例可在升级时保留数据兼容，但新代码不得以其存在推导当前业务必须审批。

---

# 12. API实施范围

## 12.1 3号位直接面向4号位

### Auth

- login
- refresh
- logout
- me

### User

- 用户列表
- 用户详情
- 创建/修改/禁用

### Role

- 角色列表
- 角色详情
- 角色创建/修改

### Permission

- 权限目录
- 角色权限分配

### Scope

- 用户Scope查询/维护
- 角色Scope查询/维护

### Audit

- 审计查询

### Governance

继续提供规则、参数、策略、Candidate/Run治理接口。

---

# 13. 前端实施要求

4号位需要：

1. 登录页；
2. Current User状态；
3. 权限列表；
4. Scope列表；
5. 路由守卫；
6. 页面权限；
7. 按钮权限；
8. Factory/ProductFamily/Department/Domain筛选限制；
9. 403页面；
10. 权限管理页面；
11. 用户/角色/Scope维护页面。

说明：

> 权限管理属于3号位后端、4号位页面。

---

# 14. 数据库升级原则

当前已有APS_Auth v1.0数据库时，不建议整库删除重建。

应提供：

> **v1.0 → v1.1增量升级脚本。**

至少需要：

1. 扩充DataScopePolicy.ScopeType约束：
   - Department
   - Domain
   - 兼容ResourceOrgGroup
2. 新增当前V1权限码Seed；
3. 对旧权限码做兼容标记或注释策略；
4. 不强制删除Approval相关表；
5. 不重建User/Role等已有数据。

---

# 15. 与APS核心数据库的关系

v1.0旧实施方案曾建议：

- PlanVersion增加CreatedByUserId/Name；
- BatchLog增加TriggeredByUserId/Name；
- 并跨库FK到APS_Auth.User。

v1.1不自动执行这一修改。

原因：

- 当前PlanVersion/ScheduleRun已有独立冻结结构；
- 当前运行审计应优先通过现有CreatedBy/Actor/审计事实闭合；
- 不应因为权限方案给APS核心运行表新增字段或跨库FK。

因此：

> **除非当前权威字段/DDL文档明确仍缺不可替代字段，否则本权限实施方案不新增APS核心运行表字段。**

尤其不建议新增跨库FK把APS核心运行库强绑定到APS_Auth。

---

# 16. 安全实现要求

## 16.1 密码

必须哈希存储。

禁止：

- 明文；
- 可逆加密。

## 16.2 默认管理员

旧DDL中的：

```text
TEMP_PASSWORD_HASH
```

只能用于开发初始化占位，不能用于正式环境。

正式部署必须：

- 注入真实Hash；
- 或首次启动强制初始化；
- 或由管理员初始化脚本生成。

## 16.3 Token

- 不在日志中记录Token；
- 必须校验过期；
- 禁用用户应拒绝新请求/刷新；
- Refresh策略由3号位统一维护。

---

# 17. 验收场景

V1至少通过以下场景。

## 17.1 功能权限

- VIEWER不能发布规则；
- RULE_ADMIN能编辑规则但不能自动拥有发布权；
- RULE_PUBLISHER可以发布；
- 无Candidate确认权限不得确认；
- 无Demand Protection释放权限不得释放。

## 17.2 Factory Scope

用户A：

```text
Factory = TJ
```

不得查看BJ工厂业务数据。

## 17.3 ProductFamily Scope

用户A：

```text
Factory = TJ
ProductFamily = X
```

不得查看TJ下其它产品族。

## 17.4 Department Scope

用户B：

```text
Factory = BJ,TJ
Department = MC
```

只能访问北京/天津机加部门范围。

## 17.5 Domain Scope

用户D：

```text
Domain = D01
```

不得确认D02 Candidate。

## 17.6 前端绕过

即使人工构造HTTP请求：

> 后端仍必须拒绝越权。

## 17.7 2号位Scope

5号位传给2号位：

```text
Factory = TJ
Department = MC
```

2号位查询Service不得返回其它范围。

---

# 18. 实施优先级

## P0

1. Auth代码正式移交3号位；
2. APS_Auth Owner切换；
3. Login/Refresh/Logout/me可运行；
4. 功能权限后端校验；
5. Scope支持Factory/ProductFamily/Department/Domain；
6. 4号位Current User/Permission/Scope接入；
7. 5号位能消费权限上下文。

## P1

8. User/Role/Permission管理页面；
9. Scope管理页面；
10. Audit；
11. 所有关键写接口权限接入；
12. 业务Scope查询过滤。

## P2

13. 旧Approval兼容清理；
14. 旧权限码清理；
15. 审计归档策略。

---

# 19. 不得做的事情

本次实施禁止：

- 重建整个Auth数据库而丢失现有数据；
- 为四个Scope维度分别新增大量关联表；
- 把ResourceOrgGroup强行当Department；
- 把Domain权限省略；
- 只做前端隐藏不做后端校验；
- 在2号位重新实现用户权限；
- 在5号位建立第二套Role/Scope；
- 用旧Approval表强制打开当前V1未冻结的审批业务；
- 用权限项目修改PlanVersion/ScheduleRun业务；
- 新增PAUSE/RESUME；
- 新增跨库强FK侵入APS核心运行表。

---

# 20. v1.1最终实施口径

> **3号位完整接管APS_Auth和现有Auth代码。**

> **V1正式实现Authentication + RBAC + Business Scope + Audit。**

> **业务范围至少支持Factory、ProductFamily、Department/ProductionDepartment、Domain。**

> **现有DataScopePolicy/UserDataScope/RoleDataScope继续复用，不新建第二套范围体系。**

> **审批四表保留兼容，但旧多级审批场景不进入当前V1主链。**

> **4号位做页面权限体验；3号位和5号位后端做硬校验。**

> **5号位消费3号位权限上下文并调用2号位合法Scope内的业务Service。**

> **权限实施不得反向改变APS冻结业务。**
