# APS V1 3号位规则、参数、认证权限与运行生命周期开发实施包（20260920 Setup换型规则专项冻结对齐版）

**版本**：v1.6  
**日期**：2026-09-20（北京时间）  
**适用对象**：3号位及其开发AI  
**文档性质**：完整继承v1.5全部规则/参数/Auth/Domain/SH治理职责，本版只增加 Setup 换型规则（SetupTransitionRule）专项冻结对齐。  
**上位依据**：
- 《APS V1最终全部流程与业务基线 v1.6》
- 《APS Pegging供需承接与分层计算业务说明 v1.5》
- 《APS 有限产能排产与滚动90天计划业务说明 v1.5》
- 《APS 集成接口设计规范 v1.32》
- 《APS 核心排产全流程走查 V3.22》
- 《APS 数据架构与防腐层设计方案 v1.43》
- 六份技术冻结文档
- 《APS 集成接口设计规范 v1.28（20260901职责与前端接口冻结对齐版）》
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》
- 《APS_Auth数据库DDL v1.1（20260901冻结对齐版）》
- 《APS_Auth数据库字段说明文档 v1.1（20260901冻结对齐版）》
- 《APS V1 3号位规则参数认证权限与运行生命周期开发实施包 v1.5 20260915 白天业务场景 Candidate运行治理冻结对齐版》
- 《APS_V1_Setup换型v1.2 悬置项裁决请求 3号位致0号位 v1.0 20260917》
- 《APS_V1_Setup换型v1.2 悬置项 0号位正式回复 v1.0 20260917》
- 《APS_V1_P1-02 Solver 策略冻结》（0号位 2026-09-15 终裁）
- 《3号位书面回复_致4号位_Setup维护API职责与契约确认_v1.1_20260920》
- 《APS_V1_Setup换型_2号位回执_致1号位与3号位_v1.0_20260920》

## 当前版本变化说明（2026-09-20）

**修改依据**：Setup 换型专项（4号位 Setup 契约源补发 2026-09-20 + 2号位回执 v1.0 20260920 + 3号位 13 端点落地交付）  
**修订原则**：3号位仍只负责治理，不做 Pegging/Solver；本版正式接管 Setup 换型规则（SetupTransitionRule）治理对象：EXACT/DEFAULT 规则维护、版本态派生、发布前冲突预校验、Setup 三权限码、角色绑定矩阵、dev seed、主数据读仓储授权例外。  
**影响边界**：RuleSet/ParameterSet/StrategyProfile、Auth/RBAC、DomainDefinition、SH参数治理、白天业务场景/Candidate运行治理继续保持。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | Setup治理 | 新增章 | SetupTransitionRule（EXACT 七元组/DEFAULT 五元组）正式纳入 3号位 治理对象。 | 4号位 Setup 契约 §11.1 | 换型时间可版本化治理 | 13 端点 | RuleSet/RuleSetVersion 复用 + SetupTransitionRule 表 | 3号位 |
| 2 | 口径 | 新增章 | 混合口径终定：Id 字段（ProductionDepartmentId/ResourceId/FromMaterialId/ToMaterialId=INT Id）+ Code 字段（StageCode/OperationCode=NVARCHAR）；提交 Id、响应回带 Code。 | 用户 2026-09-18 裁决 | 前端 DTO 改 Id | DTO 双返回 | 列已对齐冻结 DDL | 3号位 |
| 3 | 版本态 | 新增章 | status 派生三态：DRAFT/SUBMITTED/APPROVED→DRAFT；PUBLISHED→ACTIVE；DISABLED/ARCHIVED→DEPRECATED。 | v1.1 契约确认 | 前端只消费三态 | DTO 派生字段 | 物理六态不变 | 3号位 |
| 4 | 发布 | 新增章 | #13 发布前 Setup 冲突全量预校验（422）→ 治理版本状态机推进；旧 GovernanceController 发布路径为旁路缺口（不校验）。 | 契约 §11.1 #13 | 防带冲突发布 | POST publish | 无 | 3号位 |
| 5 | 权限 | 新增章 | aps.setup.view/edit/publish 三权限码 + 角色绑定矩阵（viewer.management/planner→view；admin.aps/admin.system→全3码）。 | 契约 §十 | 4号位 RBAC 联调 | 授权 | RolePermission | 3号位 |
| 6 | seed | 新增章 | dev seed 脚本（段A 角色绑定/段B DRAFT RuleSetVersion/段C COMPLETED ScheduleRun），幂等可重跑。 | 契约 §五 | dev 联调前置 | 无 | APS_Auth+APS_Production 数据 | 3号位 |
| 7 | 例外 | 新增章 | 3号位自写 Engine 层主数据只读仓储（7 只读方法）= 用户授权例外，零写路径。 | 用户授权 | Code 回带 | 内部接口 | 只读 | 3号位 |


---

# 0A. v1.4当前版本变更说明

本v1.4完整继承v1.3以下全部职责：

- 规则集（RuleSet）、参数集（ParameterSet）、策略配置（StrategyProfile）治理；
- 排程运行（ScheduleRun）与候选计划（Candidate）治理；
- 身份认证（Authentication）；
- 角色与功能权限（RBAC）；
- 业务范围（Business Scope）；
- APS_Auth数据库及审计；
- 4号位治理类接口入口。

并正式合并Domain专项：

- 排程域定义（DomainDefinition）治理；
- 当前有效Domain集合；
- Run启动冻结 `ExpectedDomainKeysJson`；
- `Domain_Dependency`合法性治理；
- SH第二层整单优先级规则治理；
- SH厂间订单型标准跨厂运输/检验/转运提前期参数治理；
- Candidate严格单Domain；
- Domain业务范围权限。

> 本版不把2号位真实归域、Pegging、跨Domain数量—时间、共享资源占用、Solver调用或运行结果持久化迁入3号位。

---

# 0. 本版变更控制

本版只在原 v1.0 基础上做以下职责调整：

1. 原2号位现有认证（Auth）代码整体移交3号位；
2. 3号位新增完整身份认证（Authentication）与角色权限（Authorization/RBAC）职责；
3. V1正式增加业务范围权限（Business Scope）：
   - Factory
   - ProductFamily
   - Department / ProductionDepartment
   - Domain
4. APS_Auth 数据库及其DDL、Entity、Repository、Service、Controller归3号位；
5. 4号位治理/权限/运行治理类接口仍由3号位提供；
6. G4 / G7 / G8 / Schedule普通结果查询逐步移交5号位业务接口接入层；
7. 3号位不再扩展成普通计划结果查询代理；
8. 原规则、参数、策略、FrozenStrategySnapshot、ScheduleRun/Candidate治理业务全部保持不变。

除上述内容外，v1.0其余规则与参数业务不得漂移。

---

# 一、3号位在APS V1中的最新定位

3号位V1正式职责分成两大域：

## A. 规则、参数、策略与运行治理

继续负责：

- RuleSet / RuleSetVersion；
- ParameterSet / ParameterSetVersion；
- StrategyProfile / StrategyProfileVersion；
- FrozenStrategySnapshot；
- Demand Priority Segment；
- Demand Protection触发配置；
- Inventory / PI / Procurement规则与参数；
- Planning Yield参数；
- Solver Strategy；
- Candidate Guardrail；
- ScheduleRun生命周期元数据边界；
- ExpectedDomainKeysJson冻结；
- StrategyProfileVersion绑定；
- 白天Candidate Run创建；
- RunType / Purpose合法性；
- Candidate人工确认 / 激活；
- FAILED恢复治理。

## B. 身份认证与权限安全

新增完整Owner职责：

- APS_Auth；
- Login / Refresh / Logout / Current User；
- User；
- Role；
- Permission；
- UserRole；
- RolePermission；
- DataScopePolicy；
- UserDataScope；
- RoleDataScope；
- 功能权限；
- Factory / ProductFamily / Department / Domain业务范围权限；
- 权限审计。

---

# 二、3号位仍然不负责

即使本次增加认证/权限，以下业务仍不属于3号位：

- Demand逐笔排序执行；
- Supply逐笔选择；
- Pegging；
- Allocation；
- DemandBalance / SupplyBalance；
- PI Position；
- Procurement / VMI / CrossFactory复杂事实；
- FinalTask；
- Solver；
- Task持久化；
- MES最终业务执行；
- Demand Protection实际数量创建/消费/释放；
- AvailableTime最终计算；
- 普通Overview/Order/Gantt/Explanation/Supply Trace等计划结果查询的长期中转。

一句话：

> **3号位负责“规则与治理 + 身份与权限”，不负责“替2号位算业务结果”。**

---

# 三、V1规则引擎总体原则

## 3.1 采用“全局默认 + 少量例外覆盖”

V1不建设：

- 复杂DSL；
- 脚本平台；
- 任意表达式引擎；
- 通用事件驱动规则引擎；
- 插件市场；
- 任意命令链编排器。

规则配置保持：

> **默认值 + 明确维度覆盖 + 第一命中。**

## 3.2 配置对象分三类

### RuleSet
回答：“遇到某种业务情况，采用哪条业务规则？”

### ParameterSet
回答：“规则中的数值是多少？”

### StrategyProfile
回答：“这一类Domain/Factory/ProductFamily/OrderType使用哪组RuleSet + ParameterSet，并选择什么排程策略？”

---

# 四、现有六张治理表继续使用

继续围绕：

1. RuleSet
2. RuleSetVersion
3. ParameterSet
4. ParameterSetVersion
5. StrategyProfile
6. StrategyProfileVersion

不为每一种规则再建一套版本表。

---

# 五、版本生命周期

最小生命周期：

```text
DRAFT
→ PUBLISHED
→ RETIRED
```

规则：

- DRAFT可编辑；
- PUBLISHED不可直接修改；
- 修改必须产生新Version；
- RETIRED不能再被新ScheduleRun选中；
- 历史PlanVersion必须能追溯当时版本。

---

# 六、一次ScheduleRun中的冻结规则

Run开始时必须冻结：

- StrategyProfileVersionId；
- RuleSetVersion；
- ParameterSetVersion；
- ExpectedDomainKeysJson；
- DataCutoffTime。

一旦Run开始，本次Run不得因新版本发布而切换。

---

# 七、2↔3 FrozenStrategySnapshot 接口

3号位给2号位的是一次性Snapshot，不是逐笔在线决策。

建议结构继续保持：

```csharp
public sealed class FrozenStrategySnapshot
{
    public long StrategyProfileVersionId { get; init; }

    public DemandPriorityConfig DemandPriority { get; init; } = default!;
    public LockConfig Lock { get; init; } = default!;
    public SupplyRuleConfig Supply { get; init; } = default!;
    public ProcurementConfig Procurement { get; init; } = default!;
    public SolverStrategyConfig Solver { get; init; } = default!;
    public CandidateGuardrailConfig Candidate { get; init; } = default!;
}
```

语义保持不变。

---

# 八、Demand排序规则

正式模型继续是：

```text
CalculationLayer
→ Priority Segment（有序）
→ 第一命中
→ Segment内部Sort
→ Stable Tie-break
```

禁止：

- 全局PriorityScore；
- Weighted Score；
- 任意表达式DSL。

规则由3号位维护，2号位执行。

---

# 九、Demand Protection规则

3号位只负责触发与生命周期规则配置，例如：

- RemainingTime < NormalLT；
- DelayStatus = DELAYED；
- CustomerTier = VIP；
- 特定OrderType；
- PMC人工强保护。

默认Sticky到：

- Demand完成；
- Demand取消；
- Supply失效；
- PMC显式释放。

真正的保护数量：

> 由2号位创建、消费、释放。

3号位不得直接修改保护数量。

---

# 十、Supply相关规则

继续保持：

- Inventory Availability由3号位治理、2号位执行；
- PI简单排序由3号位治理、2号位执行；
- Procurement排序规则由3号位治理、2号位执行。

正式采购排序：

```text
Eligibility
→ Warehouse Priority
→ AvailableTime
→ PO Release Time
→ PO + Line Stable Sort
```

不得新增全局 Inventory > PI > Procurement 总优先级。

---

# 十一、采购参数

继续治理：

- Default Purchase LT；
- Margin；
- Arrival-to-Usable Offset；
- Warehouse Priority。

ETA业务优先级保持：

```text
Manual ETA > ERP ETA > ReleaseDate + DefaultLT
```

3号位只治理参数，不计算最终AvailableTime。

---

# 十二、计划良率参数

3号位维护Planning Yield参数。

2号位：

> NetOutputQty → PlannedProcessQty

1号位消费PlannedProcessQty。

已有PI Supply不得按Yield再次放大。

---

# 十三、Solver策略参数

继续支持：

- FORWARD；
- BACKWARD；
- MIXED；
- Dynamic Bottleneck；
- On-time Target；
- Split；
- Setup；
- Stage Overlap；
- Candidate Guardrail。

3号位只治理策略，不实现Solver。

---

# 十四、Candidate Guardrail

继续保留原V1技术Guardrail。

红线：

> Guardrail只能告警/限制搜索成本，不能截断业务正确性并返回伪可行结果。

---

# 十五、ScopeJson Candidate Input Schema v2 与 ExpectedDomainKeysJson

## 15.1 ScopeJson物理列保持

继续使用：

```text
ScheduleRun.ScopeJson NVARCHAR(MAX)
```

不新增CandidateInput表或场景专属字段表。

## 15.2 新建白天Candidate使用Schema v2

必须校验：

```text
SchemaVersion = 2
BusinessTriggerType
PriorityMode
```

并按场景校验：

```text
OrderTargets[]
TaskTargets[]
ChangedResourceIds[]
```

历史缺失SchemaVersion的记录按旧v1兼容读取，不要求迁移。

## 15.3 单一真相

- `BasePlanVersionId`不写入ScopeJson；
- `ExpectedDomainKeysJson`不写入ScopeJson；
- `ExpectedDomainKeysJson`继续是预期Domain唯一权威；
- `OrderCanonicalIds`若与`OrderTargets[]`同时存在，必须严格一致。

## 15.4 3号位只治理Schema

3号位校验/冻结/审计，不解释：

- Pegging；
- Demand数量；
- Solver传播；
- Task软目标可行时间。

这些运行解释分别属于2号位和1号位。

# 十六、ScheduleRun生命周期

3号位继续负责：

- Run元数据创建/治理；
- StrategyProfileVersion绑定；
- ExpectedDomainKeysJson；
- RunType / Purpose；
- 白天Candidate Run创建；
- Candidate确认/激活；
- FAILED恢复边界。

2号位继续负责：

- 实际排程主流程；
- 每Domain计算；
- 结果持久化；
- ScheduleRun实际结果状态闭合。

---

## 16A. Candidate Base ACTIVE治理

创建白天Candidate时必须查询目标Domain当前唯一ACTIVE：

```text
BasePlanVersionId = ActivePlanVersion.Id
```

拒绝：

- Base不存在；
- Base不是ACTIVE；
- Base.DomainKey与ExpectedDomainKeysJson不一致；
- Base指向CANDIDATE。

允许多个Candidate同时基于同一个ACTIVE并存。

激活Candidate前再次校验当前ACTIVE仍等于其Base。

如果ACTIVE已变化：

> `CanActivate=false`，返回BASE_PLAN_STALE（或现有等价ReasonCode），要求重新运行。

不得通过把旧Candidate“rebase”到新ACTIVE来绕过重算。

# 十七、FULL失败链与人工恢复

原冻结保持：

- 上游Domain失败时，下游被阻断；
- 无关Domain继续；
- 原ACTIVE保持；
- 恢复必须新建ScheduleRun；
- 禁止FAILED→RUNNING。

---

# 十八、Candidate最小人工确认

继续保持：

- Candidate生成；
- 展示；
- Actor确认；
- ConfirmedAt；
- CandidatePlanVersionId；
- Activate；
- 审计。

Candidate人工确认不等同于通用ApprovalFlow。

---

# 十九、RunType / Purpose / BusinessTriggerType / PriorityMode

现有RunType/Purpose枚举不新增。

新增的是运行输入治理维度：

```text
BusinessTriggerType
PriorityMode
```

正式矩阵：

| BusinessTriggerType | RunType | Purpose | PriorityMode |
|---|---|---|---|
| NEW_ORDER_CTP | INSERT_ORDER_WHATIF | CTP | NORMAL |
| NEW_ORDER_IMPACT | INSERT_ORDER_WHATIF | INSERT_IMPACT_ANALYSIS | EXPEDITE |
| NEW_ORDER_INSERT | LOCAL_RESCHEDULE | INSERT_RESCHEDULE | NORMAL / EXPEDITE |
| EXISTING_ORDER_ADVANCE | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | NORMAL / EXPEDITE |
| GANTT_ADJUSTMENT | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | NORMAL |
| EQUIPMENT_FAILURE | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | NORMAL |
| RESOURCE_CALENDAR_CHANGE | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | NORMAL |
| DOMAIN_MANUAL_RESCHEDULE | MANUAL_RESCHEDULE | MANUAL_ADJUSTMENT | 既有正式优先规则 |

一次LOCAL只允许一个BusinessTriggerType。

`GANTT_ADJUSTMENT + EXPEDITE`、`EQUIPMENT_FAILURE + EXPEDITE`、`RESOURCE_CALENDAR_CHANGE + EXPEDITE`必须拒绝。

不得创建：

- EXISTING_ORDER_WHATIF；
- GANTT_DRAG_RUN；
- EQUIPMENT_FAILURE_RUN；
- CALENDAR_RESCHEDULE_RUN；
- EXPEDITE_RUN。

# 二十、3↔4正式接口范围

4号位只通过3号位调用以下治理/安全能力：

## 20.1 Authentication
- Login
- Refresh
- Logout
- Current User

## 20.2 Authorization
- User
- Role
- Permission
- UserRole
- RolePermission
- Business Scope
- Audit查询

## 20.3 Rule / Parameter / Strategy
- RuleSet
- ParameterSet
- StrategyProfile
- Version
- Diff
- Publish
- Retire
- Validation
- Trace

## 20.4 Run / Candidate治理
- 创建白天Candidate Run；
- Candidate Input v2校验；
- 一Run一BusinessTriggerType；
- 同场景多对象合法性；
- CTP / INSERT_IMPACT_ANALYSIS发起；
- LOCAL_RESCHEDULE发起；
- MANUAL_RESCHEDULE发起；
- 当前ACTIVE Base冻结；
- Candidate确认/激活；
- Base过期拒绝激活；
- FAILED恢复。

---

# 二十一、G4 / G7 / G8 / Schedule查询迁移

原3号位已实现/规划：

- G4 Run列表；
- G7 Domain Dependency；
- G8 Domain状态；
- Schedule versions / gantt / summary等查询。

从本版开始：

> **这些普通计划结果/运行结果查询逐步移交5号位业务接口接入层。**

3号位处理原则：

1. 不再新增同类查询能力；
2. 已有代码在迁移完成前允许兼容存在；
3. 尽量保持URL/DTO兼容；
4. 不为了“职责纯度”破坏当前可运行接口；
5. 迁移完成后，3号位不再作为4号位普通计划结果查询入口。

但以下仍归3号位：

- Run创建；
- RunType/Purpose治理；
- Strategy绑定；
- Candidate确认/激活；
- FAILED恢复；
- 规则参数权限治理。

---

# 二十二、完整接管认证（Authentication）

原2号位已有：

- AuthDbContext；
- Auth Repository；
- AuthService；
- AuthController；
- Login / Refresh / Logout / Me DTO；
- Auth实体；
- IAuthService。

全部移交3号位。

本次允许：

> **在不破坏现有登录契约的前提下直接接管和完善。**

不要求为了Owner变化重写可用登录逻辑。

---

# 二十三、APS_Auth数据库Owner

APS_Auth完整归3号位。

3号位负责：

- DDL；
- Migration；
- Entity；
- DbContext；
- Repository；
- Service；
- Controller；
- Seed。

不再采用：

> 3号位设计 → 2号位执行DDL。

除非权限功能要修改2号位核心运行表，否则无需2号位介入。

---

# 二十四、V1功能权限

V1必须至少覆盖：

- 计划查看；
- CTP；
- 插单影响分析；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate查看/确认/激活；
- Rule/Parameter/Strategy查看/编辑/发布；
- Manual ETA查看/编辑/取消；
- Demand Protection查看/释放；
- MES查看/受控操作；
- 用户/角色/权限/Scope管理；
- Audit查看。

具体PermissionCode以《APS_Auth数据库字段说明 v1.1》为准。

---

# 二十五、V1业务范围权限

正式ScopeType至少：

- Factory
- ProductFamily
- Department / ProductionDepartment
- Domain
- Global

ResourceOrgGroup可兼容保留，但不替代Department/Domain。

同一维度可以并集，不同维度共同约束。

例如：

```text
Factory in {BJ,TJ}
AND Department = MC
```

---

# 二十六、3号位权限服务输出

Current User至少向4号位/5号位提供：

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

ResourceOrgGroups[]可作为兼容扩展。

---

# 二十七、后端权限硬校验

不能只靠4号位隐藏按钮。

3号位自己的API：

> 3号位直接校验身份、功能权限和业务Scope。

5号位业务接口：

> 使用3号位提供/签发的身份与权限上下文进行校验。

3号位不得要求2号位自己维护用户权限规则。

---

# 二十八、业务范围与2号位接口边界

2号位只消费已经合法限定的：

- Factory
- ProductFamily
- Department
- Domain

Scope。

2号位不负责：

- 判断用户是谁；
- 维护角色；
- 维护Permission；
- 解释Role与Scope关系。

权限Owner仍是3号位。

---

# 二十九、Audit

3号位提供统一审计能力。

至少记录：

- Login / Logout；
- 用户/角色/权限/Scope变更；
- CTP；
- INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate确认/激活；
- FAILED恢复；
- Rule/Parameter/Strategy发布；
- Manual ETA维护/取消；
- Demand Protection释放；
- MES受控操作。

具体业务接口Owner负责提交业务上下文。

> **统一审计表 = AuditLog**（IAuditLogRepository）；GovernanceAuditLog 已 P0-04 否决并收敛删除，勿再引用旧表名。

---

# 三十、Approval兼容边界

APS_Auth中现有：

- ApprovalFlow；
- ApprovalNode；
- ApprovalRecord；
- ApprovalRule。

继续保留。

但不把旧v1.0中的：

- FreezeTaskAdjust；
- SOSacrifice；
- StartedTaskAdjust；

自动纳入当前V1主流程。

本次3号位不需要优先实现通用多级审批平台。

只有0号位未来明确重新打开审批业务时再扩展。

---

# 三十一、规则页面与权限页面

3号位后端需要为4号位提供：

### 规则治理
- RuleSet列表；
- RuleSetVersion；
- ParameterSet；
- ParameterSetVersion；
- StrategyProfile；
- Diff；
- Publish；
- Retire；
- Validation；
- Run引用追溯。

### Auth/RBAC
- 用户列表/详情/维护；
- 角色列表/详情/维护；
- Permission目录；
- 角色权限分配；
- 用户/角色Scope分配；
- 当前用户权限与Scope；
- Audit查询。

4号位负责页面。

---

# 三十二、发布前校验

原规则/参数发布前校验继续保持。

权限域额外至少检查：

- PermissionCode唯一；
- RoleCode唯一；
- ScopeType合法；
- Global ScopeValue必须为`*`；
- Disabled用户不能继续取得新有效会话；
- Scope分配引用有效用户/角色。

不建设复杂权限规则DSL。

---

# 三十三、不要做成万能平台

3号位V1明确不建设：

- 通用规则DSL；
- 通用脚本平台；
- 通用ABAC；
- 任意SQL权限；
- 字段级权限矩阵；
- 单Order/Task通用ACL；
- 通用工作流平台；
- 通用审批引擎强制接管APS业务；
- 普通计划结果万能查询网关。

---

# 三十四、建议开发顺序

## 阶段A：保留原规则治理主线
- 六表治理；
- 版本；
- 发布；
- FrozenStrategySnapshot。

## 阶段B：Auth接管
- 接管现有AuthDbContext/Repository/Service/Controller；
- Login/Refresh/Logout/Me；
- APS_Auth v1.1升级。

## 阶段C：RBAC
- User/Role/Permission；
- UserRole/RolePermission；
- 功能权限后端校验。

## 阶段D：Business Scope
- Factory；
- ProductFamily；
- Department；
- Domain；
- Current User Scope。

## 阶段E：4↔3联调
- Auth；
- Rule/Parameter/Strategy；
- Candidate/Run治理。

## 阶段F：迁出普通查询
- 与5号位交接G4/G7/G8/Schedule普通查询；
- 保持兼容路径优先。

## 阶段G：Setup 换型规则专项
- EXACT/DEFAULT 规则治理 API；
- 版本态派生 + 发布前冲突预校验；
- Setup 三权限码 + 角色绑定矩阵；
- dev seed。

---

# 三十五、最低验收场景

原R01～R22继续全部保留。

新增权限验收：

| 编号 | 场景 | 必须结果 |
|---|---|---|
| A01 | 正确账号登录 | 成功并返回身份 |
| A02 | Disabled用户登录/刷新 | 拒绝 |
| A03 | 无权限访问治理写接口 | Denied |
| A04 | RULE_ADMIN编辑规则 | 允许 |
| A05 | RULE_ADMIN无发布权 | 不得发布 |
| A06 | RULE_PUBLISHER发布 | 在权限和Scope合法时允许 |
| A07 | Factory=TJ用户查询BJ | 拒绝/过滤 |
| A08 | Factory={BJ,TJ}+Department=MC | 仅返回机加范围 |
| A09 | ProductFamily=X维护者修改Y | 拒绝 |
| A10 | Domain=D01用户确认D02 Candidate | 拒绝 |
| A11 | 手工构造HTTP绕过前端 | 后端仍拒绝 |
| A12 | 5号位消费权限上下文 | 能获得正确Scope |
| A13 | Token/密码写入Audit | 禁止 |
| A14 | 旧Approval表存在 | 不触发当前V1强制审批 |
| A15 | G4/G7/G8迁移 | 不改变原业务结果语义 |

---

# 三十六、性能要求

原规则性能要求不变：

- 一次Run只加载一次FrozenStrategySnapshot；
- 不在Pegging循环逐笔查3号位；
- 不在Solver每Task逐笔查治理表；
- Snapshot可缓存，Key包含VersionId。

权限新增要求：

- Current User/Permission/Scope可在合理生命周期缓存；
- 不允许每一行数据都向3号位发一次权限RPC；
- 5号位应一次取得用户Scope后传给2号位查询Service。

---

# 三十七、3号位交付物（更新）

原交付物继续保留，并新增：

1. 六张治理表后端；
2. Rule/Parameter/Strategy版本机制；
3. FrozenStrategySnapshot；
4. Demand Priority Segment；
5. Demand Protection配置；
6. Supply/Procurement参数；
7. Solver Strategy；
8. ScheduleRun/ExpectedDomainKeysJson治理；
9. Candidate确认/激活；
10. 原R01～R22测试；
11. 与2号位Snapshot联调；
12. 与1号位Solver Strategy联调；
13. **现有Auth代码正式接管记录**；
14. **APS_Auth v1.1升级脚本执行结果**；
15. **User/Role/Permission管理API**；
16. **Factory/ProductFamily/Department/Domain Scope管理API**；
17. **Current User权限与Scope接口**；
18. **A01～A15权限测试结果**；
19. **与4号位认证/权限联调记录**；
20. **与5号位权限上下文联调记录**；
21. **G4/G7/G8/Schedule普通查询迁移交接记录**。

---

# 三十八、与2号位联调红线

继续保持：

- 2号位只读PUBLISHED版本；
- Run开始冻结；
- 内存执行；
- 不逐笔调用3号位规则判断。

新增：

- 2号位不再拥有Auth代码；
- 2号位不直接向4号位提供HTTP；
- 2号位只消费合法业务Scope；
- 3号位不把权限规则侵入2号位Pegging/Allocation代码。

---

# 三十九、与5号位联调红线

5号位可消费：

- 当前用户身份；
- 功能权限；
- Business Scope。

5号位不得：

- 自己建立第二套Role/Permission/Scope；
- 修改3号位Auth数据；
- 绕过3号位身份认证；
- 把权限判断变成2号位职责。

3号位也不得：

- 把普通计划结果查询长期留作自己职责；
- 替5号位做非治理类前端中转。

---

# 四十、与4号位联调红线

4号位可以直接调用3号位：

- Auth；
- RBAC；
- Business Scope；
- Rule/Parameter/Strategy；
- Run/Candidate治理。

4号位不得：

- 自己判断后端最终权限；
- 自己维护角色真值；
- 直接修改APS_Auth；
- 因前端隐藏按钮而省掉后端授权。

---

# 四十一、完成定义（Definition of Done）

3号位完成必须同时满足：

### 原规则治理DoD
- 六表治理可用；
- 发布版本不可覆盖；
- Run冻结可追溯；
- Priority Segment正确；
- 无全局PriorityScore；
- Demand Protection配置可表达；
- Procurement参数完整；
- Planning Yield可取；
- Solver Strategy可消费；
- Candidate Guardrail完整；
- ExpectedDomainKeysJson正确；
- FULL/PARTIAL/FAILED可支持；
- Candidate确认可用；
- 无逐笔RPC；
- 无规则平台过度设计。

### 新增Auth/RBAC DoD
- Auth代码Owner完成移交；
- APS_Auth归3号位；
- Login/Refresh/Logout/Me可用；
- User/Role/Permission可管理；
- 功能权限后端可强制；
- Factory/ProductFamily/Department/Domain Scope可配置；
- 4号位能拿到当前用户权限/Scope；
- 5号位能消费权限上下文；
- 越权HTTP后端可拒绝；
- Audit不记录密码/Token；
- Approval旧结构不误触当前V1主链；
- 普通计划结果查询迁移到5号位后无业务漂移。

---

# 四十二、一句话交付要求

> **3号位V1现在有两项核心任务：第一，把APS已经冻结的规则、参数和策略做成可版本化、可发布、可冻结、可追溯的治理体系；第二，完整接管APS身份认证、角色权限和Factory/ProductFamily/Department/Domain业务范围权限。3号位仍然不做Pegging、Allocation、Solver或普通计划结果中转。**


---

# Domain专项合并正文

# 0. 本版定位

本版只补充3号位在Domain专项中的治理职责。

继续保持v1.1已冻结职责：

- 规则集（RuleSet）治理；
- 参数集（ParameterSet）治理；
- 策略配置（StrategyProfile）治理；
- 排程运行（ScheduleRun）治理；
- 候选计划（Candidate）治理；
- 认证（Authentication）；
- 授权（Authorization / RBAC）；
- 业务范围（Business Scope）；
- APS_Auth数据库及审计；
- 4号位治理类接口入口。

本版不把2号位运行职责迁入3号位。

---

# 1. DomainDefinition正式定位

排程域定义（DomainDefinition）属于：

> APS内部稳定基础配置 / 运行治理配置。

它不是：

- 规则集（RuleSet）；
- 参数集（ParameterSet）；
- 策略配置（StrategyProfile）；
- ODS事实；
- Pegging运行结果；
- Solver配置表达式。

3号位负责DomainDefinition的治理。

---

# 2. V1允许的Domain范围类型

V1只允许两种：

## 2.1 产品族域（FAMILY）

一个产品族（ProductFamily）形成一个排程域。

## 2.2 工厂+产品族域（FACTORY_FAMILY）

一个工厂（Factory）+ 一个产品族（ProductFamily）形成一个排程域。

V1不支持：

- Customer；
- Stage；
- ProductionDepartment；
- Resource；
- OrderType；
- SQL条件；
- 脚本；
- DSL表达式；
- 任意组合条件。

---

# 3. 3号位DomainDefinition治理职责

3号位负责：

- 新建；
- 编辑；
- 启用；
- 停用；
- 唯一性检查；
- ScopeType合法性检查；
- Factory / ProductFamily引用合法性检查；
- 审计；
- 查询当前有效Domain集合。

最小维护字段：

- DomainKey；
- DomainName；
- ScopeType；
- ProductFamily；
- Factory（FACTORY_FAMILY时必填）；
- IsActive；
- SortOrder（如采用）；
- 审计字段。

---

# 4. DomainKey治理规则

DomainKey必须：

- 稳定；
- 唯一；
- 可追溯；
- 与业务定义一一对应。

DomainKey不是：

- ProductFamily.Code；
- PlanVersionId；
- ScheduleRunId；
- 临时运行编号。

推荐使用可读编码，但具体编码格式不作为硬业务规则。

DomainDefinition启用后，DomainKey不得因显示名称调整而自动变化。

---

# 5. DomainDefinition变更生效边界

DomainDefinition可以被管理员维护。

但：

> 已经启动的ScheduleRun不得受中途配置修改影响。

因此3号位必须保证：

1. Run启动时确定当前有效Domain集合；
2. 冻结到 `ExpectedDomainKeysJson`；
3. 当前Run生命周期内不重新改写该集合；
4. 后续DomainDefinition修改从下一次Run生效。

---

# 6. ScheduleRun启动治理

3号位继续负责排程运行（ScheduleRun）治理。

FULL启动时：

```text
读取有效DomainDefinition
↓
形成有效DomainKey集合
↓
写入ExpectedDomainKeysJson
↓
创建/启动ScheduleRun
↓
交给2号位执行
```

`ExpectedDomainKeysJson`是：

> 本次Run预期Domain集合的冻结快照。

不得：

- 后续根据PlanVersion反推；
- 运行中根据DomainDefinition变化重算；
- 继续放在ScopeJson中作为权威。

---

# 7. DomainDefinition与StrategyProfile必须分离

继续冻结：

## DomainDefinition

回答：

> 哪些业务范围构成一个排程域。

## 策略配置（StrategyProfile）

回答：

> 这个Run/Domain使用哪套规则、参数和Solver策略。

两者不得合并。

不允许：

- 在DomainDefinition中内嵌Rule JSON；
- 在StrategyProfile中重新定义Domain范围；
- 用RuleSet表达Domain归属。

---

# 8. Domain_Dependency治理

排程域依赖（Domain_Dependency）节点必须使用真实DomainKey。

3号位负责治理层面：

- 查询；
- 合法性校验；
- 节点存在性检查；
- 环依赖治理/启动前检查能力；
- 提供给运行链消费。

不得再默认：

```text
Domain = ProductFamily
```

也不得把ProductFamily.Code直接当作Domain_Dependency节点。

---

# 9. Domain_Dependency与2号位边界

3号位负责：

> 定义/治理依赖关系和运行前合法性。

2号位负责：

> 实际消费依赖关系、拓扑执行、失败传播、Quantity-Time运行传递。

3号位不得重新实现：

- Pegging；
- 跨Domain供给计算；
- Quantity-Time；
- 共享Resource占用；
- PlanVersion实际结果生成。

---

# 10. FULL当前串行，不建设Domain调度平台

当前V1运行方式：

> 按Domain依赖顺序串行逐Domain执行。

3号位不要建设：

- Domain并行调度平台；
- Ready Queue通用并发框架；
- Domain Worker池；
- 多Hangfire Domain Job管理平台。

未来若真实性能证明需要并行，再单独裁决。

---

# 11. Candidate治理保持单Domain

候选计划（Candidate）继续严格单Domain。

3号位负责保证Candidate创建时：

- 必须有且只有一个DomainKey；
- 必须有一个对应Base ACTIVE PlanVersion；
- 不允许MultiDomain Candidate；
- 不允许通过Candidate请求扩大成跨Domain重排。

---

# 12. Candidate与业务范围权限

Domain属于V1业务范围（Business Scope）的一个正式维度。

现有业务范围仍支持：

- Factory；
- ProductFamily；
- ProductionDepartment；
- Domain；
- Global。

3号位认证/授权层必须能够判断：

> 当前用户是否具有目标Domain的业务范围权限。

但不建设：

- 任意记录级ACL；
- 字段级权限；
- 通用ABAC DSL。

---

# 13. Domain维护页面权限

4号位Domain维护页面调用3号位接口。

3号位必须提供最小治理接口：

- 查询Domain列表；
- 查询Domain详情；
- 新增Domain；
- 修改Domain；
- 启用/停用；
- 查询Domain依赖（如页面需要）；
- 维护Domain依赖（若该入口最终由治理页面承担）。

所有写操作：

- 必须认证；
- 必须授权；
- 必须审计。

---

# 14. Domain配置与运行权限必须分开

建议权限最小拆分：

- `DOMAIN_VIEW`
- `DOMAIN_MAINTAIN`
- `DOMAIN_ENABLE_DISABLE`
- `SCHEDULE_RUN_TRIGGER`
- `CANDIDATE_CREATE`

具体权限编码可按现有APS_Auth命名规范实现，不要求照抄名称。

关键要求：

> 能看Domain，不等于能修改Domain；能维护Domain，不等于能启动FULL。

---

# 15. Domain业务范围的判定逻辑

同维度：

> 多个允许值取并集。

不同维度：

> 取交集。

例如用户有：

- Factory=BJ；
- Domain=D01,D02。

则最终业务访问范围需同时满足对应Factory与Domain约束。

继续禁止仅靠4号位UI隐藏按钮实现安全。

---

# 16. DomainDefinition数据库Owner

DomainDefinition归属：

> 3号位。

原因：

它属于APS内部治理配置，不属于ODS事实，也不属于2号位运行结果。

3号位负责：

- 实体；
- Repository；
- Service；
- Controller；
- Migration / DDL Owner；
- 审计。

若最终数据库复用已有等价配置对象，则不重复建表。

---

# 17. ExpectedDomainKeysJson Owner边界

ScheduleRun属于运行治理与运行结果交界。

本专项明确：

- 3号位负责在Run启动时形成并冻结合法Domain集合；
- 2号位负责消费该集合执行；
- 当前Run中任何一方都不得随意改写集合。

---

# 18. 失败状态治理保持不变

ScheduleRun仍使用：

- RUNNING；
- COMPLETED；
- PARTIAL_SUCCESS；
- FAILED。

Domain失败传播由2号位执行。

3号位负责：

- Run生命周期合法性；
- 状态展示/治理；
- 禁止FAILED直接恢复RUNNING。

FAILED恢复：

> 创建新的ScheduleRun。

---

# 19. Domain状态不另建运行状态机

V1不新增：

- DOMAIN_RUNNING；
- DOMAIN_PAUSED；
- DOMAIN_RETRYING；
- DomainRun表；
- DomainExecutionState平台。

Domain执行结果继续通过：

- ScheduleRun；
- PlanVersion；
- Explanation/日志；

表达。

---

# 20. 性能指标职责

2号位负责产生Domain级运行指标。

3号位如需展示/治理：

只读取：

- DomainKey；
- DemandCount；
- LogicalProductionDemandCount；
- FinalTaskCount；
- ContextBuildMs；
- PeggingMs；
- SolverMs；
- PersistMs；
- TotalMs。

不重新计算业务指标。

---

# 21. 3号位不得承担的Domain职责

明确禁止迁入3号位：

- Order实际归域业务执行；
- Demand归域运行计算；
- Supply归域；
- Pegging；
- Allocation；
- Demand Protection；
- AvailableTime；
- Quantity-Time；
- 跨Domain供给承接；
- 共享Resource阻挡计算；
- Solver调用；
- FinalTask生成；
- PlanVersion业务结果持久化。

这些仍属2号位/1号位既有边界。

---

# 22. 与前端接口冻结关系

继续保持：

```text
4 → 3
```

只用于：

- Authentication / RBAC / Business Scope；
- Rule；
- Parameter；
- Strategy；
- ScheduleRun治理；
- Candidate治理；
- DomainDefinition治理。

普通计划结果、订单结果、甘特、供给追溯等仍走：

```text
4 → 5
```

不得借Domain专项重新把普通结果查询塞进3号位。

---


# 22A. 厂间SH规则与参数治理

## 22A.1 职责边界

3号位只治理，不逐SH运行计算。

不负责：

- SH识别；
- Demand↔SH Allocation；
- 第二层SH源厂Demand；
- SourceFactoryId归域；
- SourceReadyTime；
- Q/E Continuation分桶。

这些分别属于2号位/1号位。

## 22A.2 SH整单优先级规则

规则必须能表达：

> **整张SH源厂Demand的优先关系 = SH自身业务优先关系 与 当前全部上层Allocation中最强业务优先关系 两者中的更强者。**

3号位负责规则定义、版本、发布与Run快照冻结；2号位运行时逐SH执行。

## 22A.3 SH标准跨厂提前期

厂间SH标准运输/检验/转运LT归ParameterSet治理。

最小语义可复用现有参数模型表达：

```text
SourceFactory
TargetFactory
TransportLeadTime
InspectionLeadTime
TransferLeadTime
```

不因本轮机械新增SH专用参数表。

优先级：

```text
ERP真实物流事实（5号位）
>
本次Run冻结的SH标准LT（3号位）
```

## 22A.4 不误伤PI跨厂

本轮只调整SH厂间订单型LT。

PI大工艺接续型已有Stage/Factory提前期事实归属继续保持，不借本轮一起迁入3号位。

## 22A.5 跨版本连续性边界

Q、E、Continuation Slice、TaskNo/MESWorkOrderNo身份均不属于3号位。

3号位只继续治理既有Planning Yield、Fence/Firm、资源偏好等参数，不建设Continuation规则引擎。


# 23. 可直接复用的现有能力

如现有代码已经有：

- ScheduleRun管理；
- ExpectedDomainKeysJson；
- Business Scope中的Domain维度；
- Candidate单Domain字段；
- Rule/Parameter/Strategy治理；
- APS_Auth；
- 审计日志；

应直接复用。

本专项不要求重新设计等价对象。

---

# 24. 必须退出的历史假设

必须退出：

1. Domain等于ProductFamily；
2. Domain_Dependency节点等于ProductFamily.Code；
3. DomainDefinition属于RuleSet；
4. Run中途根据新配置改变Domain集合；
5. Candidate支持多个Domain；
6. 3号位负责Domain实际Pegging执行；
7. 为Domain额外建设通用并行调度平台。

---

# 25. Domain专项验收场景

| 编号 | 场景 | 预期 |
|---|---|---|
| G-D01 | 新建FAMILY Domain | 可保存并启用 |
| G-D02 | 新建FACTORY_FAMILY | Factory+ProductFamily合法 |
| G-D03 | 非法ScopeType | 拒绝 |
| G-D04 | 重复DomainKey | 拒绝 |
| G-D05 | FACTORY_FAMILY缺Factory | 拒绝 |
| G-D06 | Run启动 | 冻结ExpectedDomainKeysJson |
| G-D07 | Run中修改DomainDefinition | 当前Run不变化 |
| G-D08 | 下一次Run | 使用新配置 |
| G-D09 | Domain_Dependency | 节点必须是真实DomainKey |
| G-D10 | ProductFamily.Code直接当节点 | 不作为默认合法替代 |
| G-D11 | Candidate | 只允许一个Domain |
| G-D12 | MultiDomain Candidate | 拒绝 |
| G-D13 | Domain权限不足 | 后端拒绝 |
| G-D14 | 仅有查看权限修改Domain | 拒绝 |
| G-D15 | FAILED Run恢复 | 创建新Run |
| G-D16 | DomainDefinition | 不承载Rule/Parameter/Strategy |
| G-D17 | 普通结果查询 | 不迁入3号位 |
| G-D18 | Domain运行算法 | 不在3号位实现 |
| G-SH01 | 配置SH标准跨厂LT | 可发布并在Run开始冻结 |
| G-SH02 | ERP真实物流事实存在 | 默认LT不得覆盖真实事实 |
| G-SH03 | SH承接多个上层Demand | 能表达“最强上层优先 vs SH自身优先取更强者” |
| G-SH04 | 3号位逐SH计算Allocation | 验收失败 |
| G-SH05 | 3号位计算SourceReadyTime | 验收失败 |
| G-SH06 | 本轮把PI大工艺LT一起迁入3号位 | 验收失败 |
| G-SH07 | 为SH另建LT平台/专表 | 验收失败 |
| G-SH08 | 3号位实现Q/E/Continuation分桶 | 验收失败 |

---

# 26. 完成定义

3号位Domain专项完成需满足：

- 有稳定DomainDefinition治理能力；
- V1只允许FAMILY / FACTORY_FAMILY；
- DomainKey稳定唯一；
- Run启动冻结有效Domain集合；
- ExpectedDomainKeysJson当前Run不可漂移；
- Domain_Dependency使用真实DomainKey；
- Candidate严格单Domain；
- Domain正式纳入Business Scope；
- 写操作全部认证、授权、审计；
- DomainDefinition与StrategyProfile分离；
- 不接管2号位实际归域和排程；
- 不建设Domain并行调度平台；
- 不引入DSL/ABAC/通用规则表达式。

---

# 27. 一句话交付要求

> **3号位本轮只把Domain正式收口成“稳定基础配置 + Run启动冻结 + 依赖治理 + Candidate单Domain治理 + 权限审计”能力；不要把Domain变成RuleSet、不要接管2号位实际归域/Pegging/排程，也不要提前建设Domain并行调度平台。**


---

# v1.3 Supply / PI Position专项冻结对齐增量

## 一、3号位Domain治理职责补充

本版本补充冻结：

3号位负责DomainDefinition治理、Domain依赖关系合法性管理。

DomainKey表示APS稳定真实排程域标识。

DomainKey：
- 不等同ProductFamily；
- 不等同运行实例；
- 不作为资源配额模型。

## 二、Domain运行边界

V1仅支持冻结范围内Domain能力：

- DomainDefinition；
- DomainKey；
- Domain依赖关系。

不建设：

- DomainRuntime；
- DomainContext；
- VirtualInventoryBalance；
- Domain资源配额平台；
- Domain DSL。

## 三、Supply / Position规则影响

3号位规则治理需遵守：

ProductionInstructionPositionSnapshot为执行位置事实。

Position：
- 不作为Supply规则对象；
- 不参与Supply排序规则；
- 不产生独立Allocation。

ProductionInstruction仍作为Supply参与Pegging。

## 四、规则生命周期边界

保持V1增量实施：

- 复用现有规则参数框架；
- 不新增未冻结领域模型；
- 不扩大规则引擎运行边界。

---

# v1.6 Setup 换型规则专项冻结对齐增量

## 一、SetupTransitionRule 正式定位

换型规则（SetupTransitionRule）属于：

> APS 换型时间治理配置：同一资源（Resource）在工序产品转换（From 物料 → To 物料）时的换型时间（SetupMinutes）标准。

它是 3号位 正式治理对象（4号位 Setup 契约 §11.1），不是：

- ODS 事实；
- Pegging 运行结果；
- Solver 配置表达式。

## 二、两类规则形态

| 类型 | 匹配维度 | 语义 |
|---|---|---|
| EXACT（明确规则） | 七元组：VersionId + ProductionDepartmentId + StageCode + OperationCode + ResourceId + FromMaterialId + ToMaterialId | 精确换型时间 |
| DEFAULT（默认规则） | 五元组：VersionId + ProductionDepartmentId + StageCode + OperationCode + ResourceId | EXACT 无命中时的兜底换型时间 |

> **DEFAULT 是 EXACT 无命中时的兜底**，二者并存合理；跨类冲突不强制（见 §七）。

## 三、混合口径终定（提交 Id / 响应回带 Code）

用户 2026-09-18 裁决 + v1.1 契约确认，字段口径为**混合现实**：

- `ProductionDepartmentId` / `ResourceId` / `FromMaterialId` / `ToMaterialId` = **INT（主键 Id）**；
- `StageCode` / `OperationCode` = **NVARCHAR（Code）**。

响应 DTO **双返回**：

```text
提交/存储用 Id  +  展示/搜索用 Code（主数据回带）
```

Code 回带通过 3号位 自写主数据只读仓储完成（见 §八）。

## 四、status 派生版本态

RuleSetVersion 物理六态 → DTO 派生三态：

| 物理六态 | DTO 派生态 |
|---|---|
| DRAFT / SUBMITTED / APPROVED | DRAFT |
| PUBLISHED | ACTIVE |
| DISABLED / ARCHIVED | DEPRECATED |

前端只消费三态，后端保留六态状态机。

## 五、Setup 换型规则治理 API（13 端点）

3号位 已交付 `SetupRuleController`（路径前缀 `/api/governance`），共 13 端点：

| # | 方法 + 路径 | 权限 | 状态 |
|---|---|---|---|
| 1 | `GET  /api/governance/setup-rules/exact` | aps.setup.view | ✅ |
| 2 | `POST /api/governance/setup-rules/exact` | aps.setup.edit | ✅ |
| 3 | `PUT  /api/governance/setup-rules/exact/{id}` | aps.setup.edit | ✅ |
| 4 | `DELETE /api/governance/setup-rules/exact/{id}` | aps.setup.edit | ✅ |
| 5 | `GET  /api/governance/setup-rules/default` | aps.setup.view | ✅ |
| 6 | `POST /api/governance/setup-rules/default` | aps.setup.edit | ✅ |
| 7 | `PUT  /api/governance/setup-rules/default/{id}` | aps.setup.edit | ✅ |
| 8 | `DELETE /api/governance/setup-rules/default/{id}` | aps.setup.edit | ✅ |
| 9 | `GET  /api/governance/operation-resource-eligibility` | aps.setup.view | ✅ |
| 10 | `GET  /api/governance/setup-rules/uncovered-stats` | aps.setup.view | ⏳ 延期（依赖 2号位 C2 `SolveTraceNotes`） |
| 11 | `GET  /api/governance/rule-set-versions` | aps.setup.view | ✅ |
| 12 | `GET  /api/governance/rule-set-versions/{id}/diff` | aps.setup.view | ✅ |
| 13 | `POST /api/governance/rule-set-versions/{id}/publish` | aps.setup.publish | ✅ |

错误映射统一：

```text
400 状态红线/参数（InvalidOperationException）
422 数据红线（SetupRuleDataRedLineException，消息可读原文）
403 权限不足（[Authorize]）
404 不存在（ResourceNotFoundException）
```

信封统一 `ApiResponse<T>` + 真实 HTTP code。

## 六、权限码三件套 + 角色绑定矩阵

### 三权限码

```text
aps.setup.view    → 查看
aps.setup.edit    → 维护（增删改）
aps.setup.publish → 发布
```

落地位置：

- 常量：`LPS.APS.Core\Authorization\PermissionCodes.cs:76-80`；
- seed：`LPS.APS.Engine\Services\Auth\PermissionSeedService.cs:49-52`（启动幂等，37 码）；
- 角色绑定：`LPS.APS.Web\Sql\APS_Setup_dev_seed_20260920.sql` 段 A（幂等差集补齐）。

### 角色绑定矩阵

| 角色 | view | edit | publish |
|---|---|---|---|
| aps.viewer.management | ✅ | — | — |
| aps.planner | ✅ | — | — |
| aps.admin.aps | ✅ | ✅ | ✅ |
| aps.admin.system | ✅ | ✅ | ✅ |

> ⚠️ admin 生效前置：`APS_Auth_v1.3_admin_bootstrap_20260910.sql` 段 B 执行于 Setup 码落库之前；admin.system 持有 Setup 码需重跑 bootstrap 段 B 或执行 dev seed 段 A。

## 七、冲突校验与发布红线

- 冻结校验器 `SetupTransitionRuleConflictValidator`：**仅同类判重**（EXACT 七元组去重 / DEFAULT 五元组去重）；
- **跨类（EXACT×DEFAULT）冲突不强制**——DEFAULT 为 EXACT 兜底，二者并存合理；契约 §8.3 跨类冲突条款**建议撤回**（如需强制须 0号位 裁决）；
- `SetupMinutes > 0`（严于 DDL CK ≥ 0，`≤0` → 422）；
- **#13 发布前 Setup 冲突全量预校验**（422 拦截）→ 治理版本状态机推进（`IGovernanceVersionService.PublishRuleSetVersionAsync` + changeReason 审计）；
- ⚠️ **旁路缺口**：旧发布路径 `GovernanceController rule-set/version/{versionId}/publish` **不校验 Setup 冲突**——前端 Setup 发布统一走 #13；旧路径处置待 0号位 知悉裁决。

## 八、主数据读仓储（用户授权例外）

- 背景：Code 回带需要 Id→Code 映射；Engine 层此前无独立主数据读仓储。
- **用户明确授权 3号位 自写只读实现**（例外，非越位；交接凭证已入致2号位回执）。
- 文件：`LPS.APS.Core\Interfaces\IMasterDataLookupRepository.cs`（契约）+ `LPS.APS.Engine\Repositories\Governance\MasterDataLookupRepository.cs`（Dapper + APS_Production）。
- **7 只读方法**：`GetDepartmentCodeAsync` / `GetResourceCodeAsync` / `GetMaterialCodeAsync`（Code 回带）＋ `GetEligibleResourceIdsAsync` / `GetResourceInfosAsync` / `GetDepartmentInfosAsync`（#9 资格交集 + 批查）＋ `GetAllRuleSetVersionsAsync`（#11）。
- 边界：**纯只读、零写路径**；未触碰 2号位 任何既有实现；DI 独立注册于 `GovernanceServiceExtensions.AddGovernanceRepositories()`；若 2号位 未来统一读口/投影层，此仓储可并入迁移。

## 九、dev seed（不涉 DDL）

`LPS.APS.Web\Sql\APS_Setup_dev_seed_20260920.sql`（dev 库手工执行，幂等可重跑）：

- **段 A**（APS_Auth）：Setup 三权限码 → 4 角色绑定矩阵（差集补齐）；
- **段 B**（APS_Production）：幂等 ≥1 `RuleSet`（`DEV_SETUP`）+ ≥1 **DRAFT** `RuleSetVersion`；
- **段 C**（APS_Production）：幂等 ≥1 **COMPLETED** `FULL_SCHEDULE` `ScheduleRun`。

> ⚠️ 术语偏差：契约 §五「ACTIVE Run」→ 落库为 **COMPLETED**。`ScheduleRun.Status` 冻结四态 = RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED，**无 ACTIVE 态**（DDL 中 "ACTIVE" 指 `BasePlanVersionId` 关联的当前 ACTIVE 计划版本）。COMPLETED 为 #10 uncovered-stats 的 runId 来源。

执行顺序：先启动应用一次（`PermissionSeedService` 落 Setup 3 码）→ 再执行脚本。

## 十、与 2号位 Setup 换型主链数据链

- **A1**（2号位 `SetupTransitionRuleRepository` CRUD + DI）✅ → 3号位 13 端点直接消费；
- **A2**（2号位 第⑦块 `SolverStrategySnapshot.SetupTransitionRules` 投影）✅ → 1号位 Solver 消费；
- **C2**（2号位 `SolveTraceNotes` 载体，explanationType 词表：`INITIAL_SETUP_STATE` / `DEFAULT_SETUP_FALLBACK` / `SETUP_RULE_MISSING_ZERO_FALLBACK`）——3号位 已采纳 `DEFAULT_SETUP_FALLBACK` 词表（#6 落地）；0号位 点头 + DTO 落库后 3号位 即补 **#10 uncovered-stats**；
- **B1**（视图丢 vs 源缺取证）/ **C1**（ScopeJsonV2 十码验收）→ 待 2号位 排期，与 Setup 主链无耦合。

## 十一、P1-02 Solver 策略冻结（Setup 相关）

0号位 2026-09-15 终裁（驳回四分法）：

- Solver 冻结能力**全保留并实际消费**（Setup / 按期 / 重叠 / 瓶颈 4 模式）；
- `MaxIterations` 归 1号位；
- 3号位 补**瓶颈阈值 + MinBatchQty**；
- `AllowMerge` / `MaxIterations` 字段已补，`AllowSplit` 口径认可（待 2号位 接投影）。

## 十二、Setup 专项验收场景

| 编号 | 场景 | 预期 |
|---|---|---|
| S-01 | EXACT 创建 | 200 + 双返回（Id + Code 回带） |
| S-02 | EXACT 重复七元组 | 422 |
| S-03 | SetupMinutes ≤ 0 | 422 |
| S-04 | DEFAULT 增删改 | 正常 + 审计 |
| S-05 | 非 DRAFT 版本写操作 | 400 |
| S-06 | 版本/规则不存在 | 404 |
| S-07 | 无 aps.setup.edit/publish | 403 |
| S-08 | #9 from∩to 合法设备交集 | 仅交集 + Code 回带 |
| S-09 | #11 版本列表 + ruleSetId/status 过滤 | 正常 |
| S-10 | #12 diff | added/modified/removed + before/after 快照 |
| S-11 | #13 发布前存在 Setup 冲突 | 422，不落版本态 |
| S-12 | #13 无冲突发布 | 发布成功 + 审计（changeReason） |
| S-13 | status 派生三态 | DRAFT/ACTIVE/DEPRECATED |
| S-14 | dev seed 重跑 | 幂等，无重复行 |
| S-15 | 主数据读仓储 | 纯只读，零写 |
| S-16 | 角色绑定矩阵 | viewer/planner=view；admin=全3码 |
| S-17 | 旧发布路径走 #13 | 冲突可被 422 拦截 |

## 十三、Setup 专项完成定义

- EXACT/DEFAULT 维护 API 可用（#1-9、#11-13）；
- #10 随 C2 `SolveTraceNotes` 落库即交付；
- 发布前冲突全量预校验（#13）；
- 三权限码 + 角色绑定矩阵就绪（verify-rbac 84/84）；
- dev seed 幂等可重跑（verify-integration GROUP=setup 12 断言）；
- 主数据读仓储只读、Code 回带正确；
- 前端 DTO 改 Id 回执确认；
- 统一审计表 AuditLog（不引用旧表名）。

## 十四、Setup 专项一句话交付要求

> **3号位本轮把换型时间（SetupTransitionRule）收口成“可版本化、可维护、可校验、可发布”的治理对象：提交 Id、回带 Code，发布前先拦冲突；仍只治理，不做换型时间的 Solver 计算（1号位）与运行解释（2号位）。**
