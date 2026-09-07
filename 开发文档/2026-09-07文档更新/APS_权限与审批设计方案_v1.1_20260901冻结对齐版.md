# APS 权限与审批设计方案 v1.1（20260901冻结对齐版）

**文档定位**：APS V1 身份认证、功能权限、业务范围权限、审计留痕及审批扩展边界的统一设计方案  
**适用范围**：APS V1 单体解决方案（Web 前端 + 后端 API + 排程/批处理服务）  
**版本**：v1.1  
**日期**：2026-09-01（北京时间）  
**基于**：
- 《APS 权限与审批设计方案 v1.0》
- 《APS 权限与审批系统实施方案 v1.0》
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》
- 《APS 集成接口设计规范 v1.28（20260901职责与前端接口冻结对齐版）》
- 当前APS V1最终业务冻结基线、Pegging专项冻结、有限产能专项冻结、Candidate/Run相关冻结结论

---

# 0. 本版修订原则

本版只对权限、认证、业务范围、审计及其技术Owner进行冻结对齐，不允许通过权限/审批设计重新打开或改变APS V1已经冻结的业务流程。

本版特别修正v1.0中以下已不再适合作为V1正式业务基线的内容：

1. 认证/权限Owner由原2号位调整为3号位；
2. V1业务范围权限从“Factory + ProductFamily + ResourceOrgGroup”为主，调整为至少：
   - Factory
   - ProductFamily
   - Department / ProductionDepartment
   - Domain
3. V1明确需要功能权限 + 业务范围权限；
4. V1不建设任意记录级/字段级权限平台；
5. 旧文档中“冻结区任务变更审批、已开工任务变更审批、牺牲SO审批、版本发布审批、ODS强制重跑审批”等内容，不再自动视为V1必做业务；
6. Candidate人工确认属于既有Candidate业务治理，不等同于通用审批流；
7. V1不新增PAUSE/RESUME状态闭环；
8. 审批能力保留为可扩展设计，但是否启用、用于哪些场景，必须以当前业务冻结文档明确支持为前提，不得由本权限文档自行创造审批流程。

---

# 1. 设计目标

APS会影响计划版本、任务安排、供需关系、MES下发及人工业务操作，因此V1必须解决四类安全问题：

1. **身份问题**：谁在使用APS；
2. **功能问题**：该用户是否有权执行某类业务动作；
3. **业务范围问题**：该用户可以对哪些工厂、产品族、部门、计划域查看或操作；
4. **追溯问题**：关键动作是否能够追溯到人、时间、对象和结果。

V1正式目标：

> **Authentication + RBAC + Business Scope + Audit**

审批流（Approval）保留扩展能力，但不作为默认覆盖全部高风险动作的强制平台。

---

# 2. 总体模型

## 2.1 身份认证（Authentication）

回答：

> “当前请求是谁发起的？”

包括：

- Login
- Refresh
- Logout
- Current User
- Token/Session
- Disabled User控制

Owner：**3号位**

---

## 2.2 功能权限（Functional Permission / RBAC）

回答：

> “这个用户能不能做这件事？”

例如：

- 查看计划；
- 发起CTP；
- 发起局部重排；
- 发起人工重排；
- 确认Candidate；
- 发布规则；
- 修改参数；
- 维护Manual ETA；
- 释放Demand Protection；
- 进行MES受控操作；
- 管理用户/角色/权限。

Owner：**3号位**

---

## 2.3 业务范围权限（Business Scope Permission）

回答：

> “这个用户能对哪些业务范围做这件事？”

V1至少覆盖：

- Factory
- ProductFamily
- Department / ProductionDepartment
- Domain

允许组合。

例如：

- 用户A只能看天津工厂 + 产品族X；
- 用户B能看北京和天津，但只能看机加部门；
- 用户C只能维护指定产品族；
- 用户D只能确认指定Domain。

---

## 2.4 审计（Audit）

回答：

> “谁在什么时候，对什么对象，做了什么，结果如何？”

关键动作必须有审计留痕。

Owner：**3号位提供统一认证/权限审计能力；具体业务接口Owner负责提交业务动作审计信息。**

---

## 2.5 审批（Approval）的位置

审批不是V1所有写操作的默认前置。

正式原则：

> **只有当前冻结业务明确要求“必须审批”的场景，才能进入Approval Flow。**

Candidate人工确认、规则发布权限校验、Manual ETA维护、Demand Protection释放等，不能因为旧权限文档曾提到审批，就自动增加多级审批。

当前V1先保证：

- 功能权限；
- 业务范围权限；
- 操作确认（若原业务冻结已有）；
- 审计。

通用多级审批引擎可保留实体/扩展点，但不允许反向扩大V1业务范围。

---

# 3. 3号位职责

2026-09-01职责调整后，3号位完整负责认证与权限域。

包括：

- APS_Auth数据库；
- User；
- Role；
- Permission；
- UserRole；
- RolePermission；
- Business Scope；
- Login / Refresh / Logout / Current User；
- 权限判定；
- 权限管理API；
- 权限审计；
- 认证授权相关Repository / Service / Controller / DDL。

原2号位Auth壳整体移交3号位。

2号位不再拥有认证/权限业务。

---

# 4. 4号位职责

4号位负责：

- 登录页面；
- 当前用户显示；
- 菜单权限；
- 页面权限；
- 按钮权限；
- Factory/ProductFamily/Department/Domain范围的前端筛选限制；
- 无权限/无范围时的友好提示。

但：

> **4号位前端控制不是安全边界。**

任何关键查询和写操作，3号位或5号位后端都必须再次校验。

4号位不自行维护第二套Role/Permission映射。

---

# 5. 5号位职责

5号位新增业务接口接入/中转职责后，必须消费3号位提供的身份和权限上下文。

对于：

- Overview
- Order
- Gantt
- Explanation
- Supply/Pegging Trace
- PI Position
- Manual ETA
- Demand Protection
- MES相关页面接口

5号位需：

1. 验证当前身份；
2. 验证功能权限；
3. 限定Factory/ProductFamily/Department/Domain Scope；
4. 调用自身事实服务或2号位Query/Application Service；
5. 返回4号位。

5号位不得自行定义另一套权限体系。

---

# 6. 2号位职责

2号位不维护用户、角色、权限。

2号位内部服务只接收：

- 已通过身份/权限校验的调用；
- 已限定的Factory/ProductFamily/Department/Domain Scope；
- 业务参数。

2号位负责按该Scope完成真实业务查询/执行，不得扩大Scope。

这不改变2号位的Pegging、Allocation、Demand Protection、排程主流程等Owner职责。

---

# 7. 角色设计

角色用于组织功能权限和业务范围，不应直接替代所有业务判断。

V1建议保留以下基础角色模板，实际项目可以配置，不要求硬编码：

1. 系统管理员（SYSTEM_ADMIN）
2. APS管理员 / 计划平台主管
3. 计划员（PMC / PLANNER）
4. 车间/制造主管
5. 采购/物料协同人员
6. 管理层/只读用户
7. 系统接口账号

其中4号位实施包现有最小角色：

- VIEWER
- PMC
- RULE_ADMIN
- RULE_PUBLISHER
- SYSTEM_ADMIN

可以继续作为权限模板的一部分。

### 重要原则

- 角色不是数据范围本身；
- 同一角色的两个用户可以拥有不同Factory/ProductFamily/Department/Domain Scope；
- 角色只提供默认权限集合，必要时可结合用户直接授权。

---

# 8. 功能权限设计

权限码以业务动作而不是页面名称为中心。

建议命名：

```text
aps.<module>.<action>
```

## 8.1 计划与运行

- `aps.plan.view`
- `aps.plan.run`
- `aps.plan.compare`
- `aps.plan.export`

## 8.2 白天评估与重排

- `aps.ctp.view`
- `aps.ctp.evaluate`
- `aps.insert.impact.evaluate`
- `aps.reschedule.local`
- `aps.reschedule.manual`

## 8.3 Candidate

- `aps.candidate.view`
- `aps.candidate.confirm`
- `aps.candidate.activate`

说明：

> CTP / INSERT_IMPACT_ANALYSIS产生的WHATIF候选即使用户拥有activate权限，也不得激活。`CanActivate`仍由3号位按RunType/Purpose硬校验。

## 8.4 规则与参数

- `aps.rule.view`
- `aps.rule.edit`
- `aps.rule.publish`
- `aps.parameter.view`
- `aps.parameter.edit`
- `aps.strategy.view`
- `aps.strategy.edit`
- `aps.strategy.publish`

## 8.5 Manual ETA

- `aps.manual_eta.view`
- `aps.manual_eta.edit`
- `aps.manual_eta.cancel`

## 8.6 Demand Protection

- `aps.demand_protection.view`
- `aps.demand_protection.release`

说明：

> 权限只决定用户是否可以发起释放。真正释放保护数量仍由2号位执行。

## 8.7 MES

- `aps.mes.view`
- `aps.mes.dispatch`

MES业务资格仍必须服从原冻结条件；拥有权限不代表Candidate、UNLOCATED或无正式物料承诺的Task可以下发。

## 8.8 用户权限管理

- `aps.auth.user.view`
- `aps.auth.user.edit`
- `aps.auth.role.view`
- `aps.auth.role.edit`
- `aps.auth.permission.assign`
- `aps.auth.scope.assign`
- `aps.audit.view`

---

# 9. 业务范围权限设计

## 9.1 ScopeType

V1正式支持至少：

```text
Factory
ProductFamily
Department
Domain
Global
```

如现有代码/实体中仍存在：

```text
ResourceOrgGroup
```

可作为兼容/扩展维度保留，但不替代Department和Domain。

---

## 9.2 生效模型

用户有效范围由：

- 用户直接范围；
- 角色范围；
- Global权限；

共同形成。

### 同一维度

同一维度可采用并集，例如：

```text
Factory = BJ + TJ
```

### 不同维度

不同维度共同约束，例如：

```text
Factory in {BJ,TJ}
AND
Department = MC
```

这才能表达：

> 能看北京和天津，但只能看机加部门。

---

## 9.3 查询权限

查询接口必须按照有效Scope返回数据。

不允许：

> 先返回全量数据，再只在前端隐藏。

---

## 9.4 写权限

写操作必须同时满足：

1. 功能权限；
2. 目标对象在业务Scope内；
3. 目标对象满足原业务冻结条件。

例如：

> 用户拥有Candidate确认权限，但Domain不在其授权范围内，仍必须拒绝。

---

# 10. V1明确不做的权限粒度

V1不建设通用：

- Row-Level Security平台；
- Field-Level Security平台；
- 单个OrderNo授权；
- 单个TaskId授权；
- 单个PONo授权；
- 每字段独立可见性；
- 通用ABAC表达式；
- 动态脚本式权限规则。

注意：

> Factory / ProductFamily / Department / Domain过滤虽然最终会影响“哪些记录可见”，但它属于业务范围权限，不属于本章所说的任意记录级授权。

---

# 11. APS_Auth 数据模型建议

## 11.1 核心表

- User
- Role
- Permission
- UserRole
- RolePermission
- DataScopePolicy
- UserDataScope
- RoleDataScope
- AuditLog

## 11.2 DataScopePolicy

建议：

```text
Id
ScopeType
ScopeValue
Description
IsEnabled
CreatedAt
UpdatedAt
```

ScopeType至少允许：

```text
Factory
ProductFamily
Department
Domain
Global
```

## 11.3 不建议V1增加的复杂结构

不要求：

- 通用RuleExpression；
- 任意SQL条件权限；
- JSON动态表达式授权；
- 字段权限矩阵；
- 每业务表独立ACL表。

---

# 12. 审计设计

## 12.1 必须审计的动作

至少包括：

- Login / Logout；
- 用户、角色、权限、Scope变更；
- 发起CTP；
- 发起插单影响分析；
- 发起LOCAL_RESCHEDULE；
- 发起MANUAL_RESCHEDULE；
- Candidate确认/激活；
- FAILED恢复；
- Rule/Parameter/Strategy发布；
- Manual ETA维护/取消；
- Demand Protection释放；
- MES受控操作。

## 12.2 最小字段

建议：

- AuditId
- UserId
- UserName
- ActionCode
- ObjectType
- ObjectId
- Result
- OccurredAt
- ClientIp
- ScheduleRunId（适用时）
- PlanVersionId（适用时）
- DomainKey（适用时）
- Reason / Remark（适用时）
- ErrorMessage

不强制所有业务动作都保存完整RequestData/ResponseData，避免无必要数据膨胀。

---

# 13. 审批扩展边界

## 13.1 当前V1不自动纳入审批流的旧场景

以下旧v1.0设计曾被列为多级审批场景，但本版不再将其作为V1默认必做：

- 冻结区任务变更审批；
- 牺牲SO审批；
- 已开工任务变更审批；
- 正式版本发布审批；
- 历史版本回滚审批；
- ODS关键批次强制重跑审批；
- Mapping覆盖审批。

原因不是这些场景永远不能审批，而是：

> 当前最终V1业务冻结并未统一要求建设这套多级审批平台，权限文档不能自行把未来治理设想写成当前业务红线。

## 13.2 Candidate确认

Candidate人工确认属于：

> 现有Candidate业务生命周期的一部分。

不是通用ApprovalFlow。

仍按现有Candidate冻结业务执行。

## 13.3 未来扩展

如果后续0号位明确打开审批功能，可以复用：

- ApprovalFlow
- ApprovalNode
- ApprovalRecord
- ApprovalRule

但必须另行冻结：

- 哪个业务动作需要审批；
- 谁发起；
- 谁审批；
- 审批通过后调用哪个Owner Service；
- 审批失败/撤回/超时的业务含义。

在此之前，不得把Approval实体存在解释为V1所有高风险动作已经需要审批。

---

# 14. 后端鉴权原则

## 14.1 接口层

3号位和5号位对外接口必须：

- 验证身份；
- 验证功能权限。

## 14.2 业务范围

必须在业务服务调用前确认合法Scope。

## 14.3 原业务Owner硬校验

权限通过不代表业务条件通过。

例如：

- 用户有Candidate Activate权限；
- 但当前Purpose=CTP；
- 后端仍必须拒绝激活。

同理：

- 用户有MES Dispatch权限；
- 但Task为Candidate/UNLOCATED/Planning-only Placeholder依赖；
- 仍不得下发。

---

# 15. 前端权限原则

4号位应：

- 登录后加载Current User；
- 加载Permissions；
- 加载Business Scope；
- 路由守卫；
- 菜单/页面/按钮显示；
- Factory/ProductFamily/Department/Domain筛选范围收敛；
- 403/无范围提示。

禁止：

- 在前端伪造业务Scope；
- 只靠按钮隐藏代替后端鉴权；
- 自行硬编码业务Owner权限。

---

# 16. 数据库Owner

APS_Auth及其认证、权限、业务范围、权限审计结构归3号位完整Owner。

3号位负责：

- DDL；
- Entity；
- DbContext；
- Repository；
- Service；
- Controller。

不再采用：

> 3号位设计、2号位代执行APS_Auth DDL。

只有当权限功能需要修改2号位排程核心表时，才需跨Owner评审。

---

# 17. 与现有APS业务的边界

权限系统不得：

- 改变Pegging排序；
- 改变Supply选择；
- 改变Allocation；
- 改变Demand Protection算法；
- 改变AvailableTime；
- 改变Solver；
- 改变RunType/Purpose；
- 改变Candidate是否可激活的业务判断；
- 新增PAUSE/RESUME；
- 新增MultiDomain Candidate；
- 新增MES下发资格。

权限系统只回答：

> “这个人能不能在这个业务范围内发起/查看/执行已经存在的APS业务动作。”

---

# 18. v1.1最终冻结摘要

> **3号位完整Owner：Authentication + RBAC + Business Scope + Auth Audit。**

> **V1必须实现功能权限。**

> **V1必须实现Factory、ProductFamily、Department/ProductionDepartment、Domain业务范围权限。**

> **V1不建设任意记录级/字段级权限平台。**

> **4号位做前端权限体验，后端仍必须硬校验。**

> **5号位作为业务接口接入层消费3号位权限上下文，不建立第二套权限体系。**

> **2号位只消费合法Scope，不拥有用户权限规则。**

> **旧v1.0中多级Approval场景不再自动作为V1必做业务；审批扩展不得反向修改当前冻结业务。**

> **Candidate人工确认是Candidate治理，不等同于通用审批流。**
