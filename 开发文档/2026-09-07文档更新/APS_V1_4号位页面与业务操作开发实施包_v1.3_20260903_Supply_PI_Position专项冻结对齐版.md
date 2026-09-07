# APS V1 4号位页面与业务操作开发实施包（冻结对齐版）

**版本**：v1.3  
**日期**：2026-09-01（北京时间）  
**适用对象**：4号位及其开发AI  
**文档性质**：在 v1.0 冻结实施包基础上的职责、接口与权限调整版  
**上位依据**：

- 当前APS V1最终业务冻结基线；
- 《APS Pegging供需承接与分层计算业务说明》当前冻结版；
- 《APS 有限产能排产与滚动90天计划业务说明》当前冻结版；
- 《APS 集成接口设计规范 v1.28（20260903职责与前端接口冻结对齐版）》；
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》；
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》；
- 《APS V1 3号位规则、参数、认证权限与运行生命周期开发实施包 v1.2（Domain专项完整合并版）》。

---

# 0. 本版变更控制

本版以原《APS V1 4号位页面与业务操作开发实施包 v1.0》为母版，只做已经正式裁决的职责、接口、认证权限和页面补齐调整。

本版主要变化：

1. 4号位正式只直接调用3号位和5号位对外API；
2. 取消4号位直接调用2号位HTTP/API的正式关系；
3. 3号位负责：
   - 身份认证（Authentication）；
   - 角色与功能权限（RBAC）；
   - 业务范围权限（Business Scope）；
   - 规则、参数、策略治理；
   - Run/Candidate治理；
4. 5号位负责：
   - PI Position、Manual ETA等自身事实接口；
   - Overview、Order、Gantt、Explanation、Supply/Pegging Trace等非治理类业务接口接入；
   - Demand Protection释放、MES受控操作等2号位业务的接口中转；
5. G4 / G7 / G8 / Schedule普通查询逐步由3号位迁移至5号位；
6. 4号位增加完整权限前端能力：
   - Current User；
   - 功能权限；
   - Factory / ProductFamily / Department / Domain业务范围；
   - 路由、菜单、页面、按钮、筛选范围控制；
7. Manual ETA页面正式纳入4号位V1页面范围；
8. Demand Protection查看/释放正式纳入4号位业务操作范围；
9. 认证、权限管理页面正式纳入4号位交付；
10. 原Candidate、CTP、PlanVersion、MES、PI Position、Explanation等冻结业务语义全部保持不变。

**本版不得借接口调整重新设计Pegging、有限产能、Candidate、PlanVersion、Manual ETA、Demand Protection、MES下发或跨Domain业务。**

---

# 一、4号位在APS V1中的最新定位

4号位不是排程引擎，也不是数据库维护工具。

4号位在V1中的核心职责仍然是：

> **把已经由2号位、1号位、3号位、5号位形成的计划、影响、风险、规则、事实和受控业务动作，以PMC/业务人员能理解、能比较、能授权、能安全操作的方式呈现出来。**

本版以后，4号位职责分为四类：

## 1.1 计划与业务结果展示

包括：

- 排产总览；
- 订单/需求计划；
- 甘特图/资源计划；
- CTP/插单评估结果；
- Candidate差异；
- Explanation；
- PI Position；
- Supply/Pegging追溯；
- Run / Version / Domain状态；
- MES资格/状态。

## 1.2 受控业务操作入口

包括：

- 发起CTP；
- 发起INSERT_IMPACT_ANALYSIS；
- 发起LOCAL_RESCHEDULE；
- 发起MANUAL_RESCHEDULE；
- Candidate确认/激活；
- FAILED恢复；
- Manual ETA新增/修改/取消；
- Demand Protection释放；
- MES受控操作。

4号位只负责发起和交互，不自行执行核心业务。

## 1.3 治理与维护页面

包括：

- RuleSet；
- ParameterSet；
- StrategyProfile；
- Priority Segment；
- 发布/退役/Diff/Validation；
- 用户；
- 角色；
- Permission；
- Business Scope；
- Audit。

## 1.4 身份与权限体验

包括：

- 登录；
- Current User；
- 菜单/路由；
- 页面权限；
- 按钮权限；
- Factory / ProductFamily / Department / Domain业务范围限制；
- 403/无业务范围提示。

一句话：

> **4号位负责“看清楚、比较清楚、权限清楚、操作安全”，不负责“自己算”和“自己改结果”。**

---

# 二、4号位明确不负责

4号位不得负责：

- Demand排序算法；
- Pegging；
- Supply扣减；
- DemandBalance / SupplyBalance；
- Allocation；
- Demand Protection实际数量创建、消费、释放逻辑；
- PI Position计算；
- Effective ETA计算；
- AvailableTime计算；
- 有限产能求解；
- FinalTask生成；
- ScheduleRun / PlanVersion核心状态机；
- Candidate影响传播算法；
- 跨Domain WHATIF编排算法；
- 直接UPDATE APS运行结果表；
- 直接修改Task时间/Resource；
- 直接修改Allocation；
- 直接写APS_Auth数据库；
- 自行维护Role / Permission真值；
- 自行维护第二套Business Scope规则。

---

# 三、4号位正式接口边界

## 3.1 总原则

从v1.1开始：

> **4号位只直接对接3号位和5号位。**

正式禁止：

```text
4号位 → 1号位
4号位 → 2号位 HTTP/API
```

## 3.2 4 → 3：治理、安全、运行治理

4号位通过3号位接口调用：

### 身份认证
- Login
- Refresh
- Logout
- Current User

### 权限
- User
- Role
- Permission
- UserRole / RolePermission
- Business Scope
- Audit

### 规则/参数/策略
- RuleSet
- ParameterSet
- StrategyProfile
- Priority Segment
- Diff
- Publish
- Retire
- Validation
- Version History

### Run / Candidate治理
- CTP Run发起
- INSERT_IMPACT_ANALYSIS发起
- LOCAL_RESCHEDULE发起
- MANUAL_RESCHEDULE发起
- Candidate确认/激活
- FAILED恢复

## 3.3 4 → 5：非治理APS业务与复杂事实

4号位通过5号位接口调用：

- Overview；
- Order/需求计划；
- Gantt；
- Explanation；
- Supply/Pegging Trace；
- PI Position；
- Manual ETA；
- Demand Protection查看；
- Demand Protection释放中转；
- MES资格/状态；
- MES受控操作中转；
- G4 / G7 / G8 / Schedule普通查询；
- 其它非治理APS业务查询。

## 3.4 中转不改变业务Owner

例如Demand Protection释放：

```text
4号位
→ 5号位API
→ 2号位Application Service
→ 2号位实际释放
```

4号位不得把“接口由5号位提供”理解为“业务由5号位负责”。

---

# 四、页面总原则

## 4.1 页面只调用后端正式API

禁止：

- 页面直连数据库；
- 页面直接UPDATE Task；
- 页面直接UPDATE PlanVersion；
- 页面直接UPDATE PeggingSupplyAllocation；
- 页面直接改Demand Protection；
- 页面直接改InventoryBalance；
- 页面直接执行排程SQL；
- 页面直接调用2号位Repository/内部表；
- 页面直接调用1号位Solver。

## 4.2 展示必须区分事实、结果、估算、建议

至少区分：

- **FACT**：设备故障、PI Position、ERP ETA、MES报工等真实事实；
- **RESULT**：FinalTask、Start/End、EarliestCompletion、Delay等排程结果；
- **ESTIMATED**：Planning-only Purchase Placeholder等估算；
- **RECOMMENDATION**：建议重排、建议确认Candidate等。

页面不得把ESTIMATED显示成确定承诺。

## 4.3 权限控制不能只做前端隐藏

4号位可以：

- 隐藏菜单；
- 禁用按钮；
- 限定筛选范围。

但后端仍必须由3号位或5号位再次校验。

---

# 五、V1页面范围

在原v1.0九类业务页面基础上，本版正式形成以下十二类页面/页面组：

1. 登录与当前用户
2. 排产总览
3. 订单/需求计划查询
4. 甘特图/资源计划
5. CTP/插单评估
6. Candidate对比与确认
7. 异常与原因解释
8. PI Position与供给追溯
9. Manual ETA维护
10. 规则、参数与策略维护
11. 运行/版本/Demand Protection/MES状态与受控操作
12. 用户、角色、权限、业务范围与审计

说明：

> 本版没有改变原核心业务页面，只把原来已经冻结但未独立列出的认证、权限、Manual ETA、Demand Protection等正式补入页面范围。

---

# 六、页面1：登录与当前用户

## 6.1 登录

调用3号位Authentication接口。

支持：

- Login；
- Refresh；
- Logout；
- Current User。

## 6.2 Current User至少消费

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

ResourceOrgGroups[]如后端保留，可作为兼容字段。

## 6.3 前端职责

4号位根据Current User：

- 控制菜单；
- 控制路由；
- 控制按钮；
- 限定Factory筛选；
- 限定ProductFamily筛选；
- 限定Department筛选；
- 限定Domain筛选；
- 显示无权限/无范围状态。

## 6.4 红线

不得：

- 自己推导权限；
- 自己写死“PMC拥有所有工厂”；
- 把Role名称当成全部权限；
- 在前端扩大后台返回的Scope；
- 把前端隐藏当成后端安全保障。

---

# 七、页面2：排产总览

目标：

> PMC快速回答当前计划是否正常、哪些订单延期、哪些资源成为瓶颈、哪些Domain失败、哪些结果依赖估算采购、是否有Candidate待确认。

主要区域：

- 当前ACTIVE版本；
- DomainKey；
- PlanVersionId；
- ActivatedAt；
- SourceScheduleRunId；
- PlanHorizon；
- On-time / Delayed / Risk / Unscheduled / Estimated-only摘要；
- 关键Resource负荷；
- Bottleneck；
- FAILED Domain；
- 被上游失败阻断Domain；
- PI Position严重Issue；
- ODS/事实问题；
- Candidate待处理摘要。

## 7.1 接口Owner

```text
4 → 5
```

5号位负责业务接口接入/聚合，真实计划结果Owner仍为2号位。

## 7.2 Business Scope

查询必须受：

- Factory
- ProductFamily
- Department
- Domain

范围限制。

不允许浏览器先拿全量再前端过滤。

---

# 八、页面3：订单/需求计划查询

支持按：

- OrderNo；
- ProductionInstructionNo；
- Material；
- Customer；
- Factory；
- ProductFamily；
- Department；
- DueDate；
- DelayStatus；
- PlanVersion；
- Domain；

查询。

订单详情建议四块：

1. 基本信息；
2. Pegging承接；
3. 生产计划；
4. Explanation。

Pegging部分可展示：

- SupplyType
- SupplyKey
- AllocatedQty
- AvailableTime
- Commitment / Confidence
- Lock

生产计划展示：

- FinalTask
- Stage / Operation
- Resource
- Start / End
- Quantity
- PlannedProcessQty
- Status

## 8.1 接口Owner

```text
4 → 5 → 2号位Query Service
```

5号位只做接入/DTO，不重算Pegging。

---

# 九、页面4：甘特图 / 资源计划

甘特图只展示FinalTask。

不得把LogicalProductionDemand当Task展示。

支持：

- Resource；
- Stage；
- ProductionDepartment；
- Factory；
- Domain；

维度。

Task正式状态只展示：

- PLANNED
- RELEASED
- IN_PROGRESS
- COMPLETED
- CANCELLED

不得增加：

- SCHEDULED
- PAUSED
- SUSPENDED
- WAITING
- RUNNING

不可移动标识可显示：

- EXECUTION
- FIRM
- FROZEN

但用户不能拖动后直接写正式计划。

如需调整：

> 发起MANUAL_RESCHEDULE形成Candidate，再比较、确认。

## 9.1 接口Owner

```text
4 → 5
```

不直接调用1号位或2号位HTTP。

---

# 十、页面5：CTP / 插单评估

## 10.1 发起

CTP和插单影响分析的Run发起统一走：

```text
4 → 3
```

3号位负责RunType/Purpose治理并调用2号位主流程。

## 10.2 输入至少包括

- OrderCanonicalId / 新订单信息；
- Material；
- Quantity；
- Factory；
- Requested DueDate；
- 必要客户信息；
- Purpose；
- 当前合法Domain。

## 10.3 输出必须回答

1. 是否满足Requested DueDate；
2. 最早完成日期；
3. 是否依赖ESTIMATED供给；
4. 受影响订单数；
5. 哪些订单新增延期；
6. 哪些Demand Protection冲突；
7. 主要瓶颈；
8. 主要根因；
9. Candidate是否单Domain；
10. 跨Domain场景是否完成链式WHATIF。

依赖Planning-only Purchase Placeholder时，必须明显显示：

> **估算日期，采购尚无正式承诺。**

不能只显示“可交”。

---

# 十一、跨Domain CTP展示

V1没有MultiDomain Candidate。

后台仍是：

```text
Domain C WHATIF
→ Quantity-Time
→ Domain B WHATIF
→ Quantity-Time
→ Domain A WHATIF
→ 汇总
```

页面可以展示一个综合客户答案，但详情应能看到：

- 每个Domain结果；
- Quantity-Time传递；
- 关键瓶颈；
- 失败Domain；
- Estimated标识。

页面不得把多个单Domain Candidate合成一个可编辑MultiDomain Candidate对象。

---

# 十二、页面6：Candidate对比与确认

至少展示：

- Requested DueDate；
- Candidate Completion；
- 是否按期；
- 是否Estimated；
- Base完成时间；
- Candidate完成时间；
- 时间差；
- 是否由On-time变Delayed；
- Demand Protection冲突；
- Task新增数；
- Task删除数；
- 时间移动数；
- Resource变化数。

不要求V1做复杂Diff Graph。

## 12.1 Candidate确认

确认信息：

- Candidate PlanVersionId；
- Base PlanVersionId；
- 影响摘要；
- Confirm按钮；
- Actor；
- ConfirmedAt；
- Remark（可选）。

## 12.2 接口Owner

```text
4 → 3
```

## 12.3 激活红线

CTP和INSERT_IMPACT_ANALYSIS永远不得激活。

即使用户拥有：

`aps.candidate.activate`

后端仍必须按RunType/Purpose拒绝不允许激活的Candidate。

---

# 十三、审批边界

Candidate人工确认：

> 属于Candidate现有生命周期，不等于通用ApprovalFlow。

V1不把完整OA/通用多级审批作为：

- CTP；
- Candidate；
- Manual ETA；
- Demand Protection；
- Rule Publish；

的默认必经主链。

APS_Auth内旧Approval四表保留兼容，但4号位不因此自动建设旧审批页面。

---

# 十四、页面7：异常与原因解释

主要消费：

- ScheduleExplanationFact；
- BusinessFactIssue；
- Run/Domain失败信息；
- RescheduleRecommendation。

页面必须优先显示根因，例如：

- 采购料某日才可用；
- MC01关键窗口无产能；
- 上游Stage晚；
- Firm/Frozen占用；
- 外Domain共享资源阻挡。

不得只显示：

`DUE_DATE_RISK`

数据问题与排程问题分开。

## 14.1 接口Owner

```text
4 → 5
```

5号位读取/聚合已形成的Explanation结果，不重新裁决延期原因。

---

# 十五、设备故障展示

V1流程保持：

```text
故障事实
→ ImpactAssessment
→ ScheduleExplanationFact
→ RescheduleRecommendation
→ 页面提示
→ PMC决定是否发起重排
```

页面可显示：

- Resource Down；
- 受影响订单；
- 建议重排。

不得：

- 自动PAUSE Task；
- 自动RESUME Task；
- 自动创建Run；
- 自动激活Candidate。

---

# 十六、页面8：PI Position与供给追溯

## 16.1 PI Position

按PI展示：

- ERP RemainingQty；
- PositionType；
- Stage；
- XC；
- Transit；
- Waiting；
- UNLOCATED；
- PositionQty；
- Issue；
- Position合计 / ERP RemainingQty。

接口：

```text
4 → 5
```

PI Position属于5号位自属复杂事实。

## 16.2 Supply/Pegging追溯

可展示：

- INVENTORY；
- PI；
- PO；
- VMI；
- ARRIVED_NOT_INBOUND；
- INTERPLANT_TRANSIT；
- RECEIVED；
- PLANNED_PRODUCTION；
- PLANNING_PURCHASE_PLACEHOLDER。

Placeholder必须明显标记：

- ESTIMATED
- NOT_COMMITTED

Supply/Pegging最终Allocation结果：

```text
4 → 5 → 2号位Query Service
```

5号位不得重算Allocation。

---

# 十七、页面9：Manual ETA维护

Manual ETA正式属于4号位V1页面范围。

## 17.1 页面职责

支持：

- 查询；
- 新增；
- 修改；
- 取消/失效；
- 查看ERP ETA；
- 查看ReleaseDate；
- 查看当前Manual ETA；
- 查看当前Effective ETA/AvailableTime结果（如接口提供）；
- 审计信息。

## 17.2 接口Owner

```text
4 → 5
```

Manual ETA事实/API属于5号位。

## 17.3 业务语义

正式优先级：

```text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

但：

- 4号位不计算Effective ETA；
- 4号位不计算AvailableTime；
- 5号位不计算最终AvailableTime；
- 2号位负责运行时Effective ETA / AvailableTime。

## 17.4 权限

至少受：

- `aps.manual_eta.view`
- `aps.manual_eta.edit`
- `aps.manual_eta.cancel`

以及相应Factory/ProductFamily/Department/Domain业务范围约束。

---

# 十八、页面10：规则、参数与策略维护

4号位负责页面，3号位负责后端治理。

支持：

- RuleSet；
- RuleSetVersion；
- ParameterSet；
- ParameterSetVersion；
- StrategyProfile；
- StrategyProfileVersion；
- DRAFT编辑；
- Diff；
- Publish；
- Retire；
- ChangeReason；
- Validation；
- Version History。

Demand Priority：

> 以Priority Segment展示，不展示全局PriorityScore。

Parameter可维护已冻结内容，例如：

- Default Purchase LT；
- Arrival Offset；
- Demand Protection触发参数；
- Planning Yield；
- Solver Strategy；
- On-time Target；
- Candidate Guardrail；
- Split；
- Setup；
- Overlap。

页面不能直接改PUBLISHED数据。

---

# 十九、页面11：运行 / 版本 / Demand Protection / MES

本页面组可以拆成多个子页，但职责统一。

## 19.1 ScheduleRun / PlanVersion普通查询

展示：

- RunId
- RunType
- Status
- DataCutoffTime
- StrategyProfileVersion
- ExpectedDomainKeys
- CompletedAt
- ErrorMessage

PlanVersion：

- DomainKey
- BUILDING
- ACTIVE
- FAILED
- CANDIDATE
- ARCHIVED
- BasePlanVersion
- ActivatedAt
- ErrorMessage

普通查询最终接口：

```text
4 → 5
```

G4/G7/G8/Schedule普通结果查询逐步迁5号位。

## 19.2 Run治理动作

以下仍走：

```text
4 → 3
```

包括：

- 新建Candidate Run；
- FAILED恢复；
- Candidate确认/激活。

FAILED恢复必须新建ScheduleRun。

不得把FAILED直接改回RUNNING。

## 19.3 PARTIAL_SUCCESS

页面必须区分：

- 成功Domain；
- 失败Domain；
- 因上游失败被阻断Domain。

---

# 二十、Demand Protection查看与释放

## 20.1 查看

页面可以展示：

- Demand；
- 当前Protection状态；
- 保护Supply份额；
- 原因/触发信息；
- 创建时间；
- 是否允许人工释放。

接口：

```text
4 → 5
```

## 20.2 释放

调用链：

```text
4
→ 5
→ 权限/Scope校验
→ 2号位Demand Protection Application Service
```

4号位不得：

- 自己计算释放数量；
- 直接改Lock表；
- 直接改Allocation；
- 绕过2号位业务校验。

## 20.3 权限

至少：

- `aps.demand_protection.view`
- `aps.demand_protection.release`

权限通过仍不代表业务一定可释放，最终业务条件由2号位硬校验。

---

# 二十一、MES下发资格与受控操作

Task详情可显示：

- EligibleForMES；
- 不可下发原因；
- 当前MES状态；
- 操作审计。

不得下发：

- Candidate；
- UNLOCATED；
- 无正式PI的规划占位Task；
- 仍依赖Planning-only Purchase Placeholder且没有真实物料承诺；
- 不在合法下发窗口；
- 已取消/失效。

接口：

```text
4 → 5 → 2号位MES Application Service
```

4号位不直接写MES接口表。

权限至少：

- `aps.mes.view`
- `aps.mes.dispatch`

权限不能绕过MES业务资格。

---

# 二十二、页面12：用户、角色、权限、业务范围与审计

后端Owner：

```text
4 → 3
```

## 22.1 用户

支持：

- 列表；
- 详情；
- 创建；
- 修改；
- 启用/禁用。

## 22.2 角色

支持：

- 角色列表；
- 角色详情；
- 权限分配；
- Scope分配。

## 22.3 Permission

展示权限目录。

权限码以业务动作为中心。

## 22.4 Business Scope

V1至少支持：

- Factory
- ProductFamily
- Department / ProductionDepartment
- Domain
- Global

ResourceOrgGroup如保留，只作为兼容维度。

## 22.5 Scope组合

同一维度并集：

```text
Factory = BJ + TJ
```

不同维度共同约束：

```text
Factory in {BJ,TJ}
AND Department = MC
```

## 22.6 Audit

至少支持查询：

- Login / Logout；
- 用户/角色/权限/Scope变更；
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

不得展示明文密码、Token、Refresh Token等敏感数据。

---

# 二十三、权限与业务范围前端要求

## 23.1 页面权限

4号位根据Permissions：

- 决定是否展示菜单；
- 决定是否进入页面；
- 决定按钮是否可用。

## 23.2 Scope筛选

Factory/ProductFamily/Department/Domain筛选项只能展示合法范围。

例如用户：

```text
Factory = TJ
ProductFamily = X
```

页面不得允许选择BJ或其它ProductFamily。

## 23.3 后端仍是最终安全边界

即使用户绕过页面直接构造HTTP：

> 3号位/5号位后端仍必须拒绝越权。

## 23.4 V1不做的权限粒度

4号位不建设：

- 任意Row-Level权限配置器；
- 任意Field-Level权限配置器；
- 单个Order授权页面；
- 单个Task授权页面；
- 字段脱敏配置平台；
- ABAC表达式编辑器。

---

# 二十四、人工操作审计

会改变业务状态的操作必须可追溯。

至少包括：

- Candidate确认；
- Candidate激活；
- FAILED恢复；
- Rule/Parameter/Strategy发布；
- Manual ETA维护/取消；
- Demand Protection释放；
- 发起Manual Reschedule；
- MES受控操作；
- 用户/角色/权限/Scope变更。

至少展示/记录：

- Actor
- Time
- ObjectType
- ObjectId
- Action
- Result
- Remark/Reason（必要时）

---

# 二十五、前端API原则

4号位不得以数据库表字段作为长期API契约。

后端应提供业务DTO，例如：

- CurrentUserDto
- PlanOverviewDto
- OrderScheduleDetailDto
- GanttTaskDto
- CandidateComparisonDto
- CtpResultDto
- PiPositionViewDto
- SupplyTraceDto
- ManualEtaDto
- ExplanationViewDto
- RunStatusDto
- RuleVersionDto
- PermissionDto
- BusinessScopeDto
- AuditLogDto

接口Owner迁移时优先保持现有URL/DTO兼容，特别是G4/G7/G8/Schedule查询迁移。

---

# 二十六、前端状态与后端业务状态不得混用

UI状态可以包括：

- loading
- disabled
- warning
- expanded
- refreshing
- no-permission

这些不是APS业务状态。

不得因此新增：

- PlanVersion状态；
- Task状态；
- Run状态；
- Candidate状态。

不要把“处理中”等前端词直接落库成业务状态。

---

# 二十七、建议开发顺序

## 阶段A：认证与基础权限接入

- Login
- Refresh
- Logout
- Current User
- 路由守卫
- 菜单权限
- 按钮权限
- Factory/ProductFamily/Department/Domain Scope

## 阶段B：只读计划结果

- Overview
- Order
- Gantt
- Explanation
- Run/Version普通查询

统一优先对接5号位。

## 阶段C：CTP / Candidate / Run治理

- CTP输入
- INSERT_IMPACT_ANALYSIS
- Candidate结果
- Base vs Candidate
- Estimated标识
- Candidate确认
- FAILED恢复

治理动作对接3号位。

## 阶段D：规则参数治理

- RuleSet
- ParameterSet
- StrategyProfile
- Priority Segment
- Diff
- Publish
- Retire
- Validation

## 阶段E：复杂事实与业务操作

- PI Position
- Supply/Pegging Trace
- Manual ETA
- Demand Protection
- BusinessFactIssue
- 设备故障影响
- MES资格/受控操作

对接5号位。

## 阶段F：权限管理

- User
- Role
- Permission
- Business Scope
- Audit

对接3号位。

---

# 二十八、最低验收场景

原v1.0 U01～U22全部继续保留，并按新接口Owner执行。

| 编号 | 场景 | 必须结果 |
|---|---|---|
| U01 | ACTIVE多Domain | 各Domain版本正确展示 |
| U02 | PARTIAL_SUCCESS | 成功/失败/阻断Domain区分 |
| U03 | 订单Pegging详情 | Supply来源和数量可追溯 |
| U04 | 一个Task承接多Order | TaskShare正确展示 |
| U05 | 计划良率 | NetQty与PlannedProcessQty不混淆 |
| U06 | 40+60 Quantity-Time | 页面不压平成100最晚 |
| U07 | Estimated采购 | 明显显示NOT_COMMITTED |
| U08 | CTP可按期 | 最早日期和原因正确 |
| U09 | CTP无法按期 | 显示根因，不只显示DUE_DATE_RISK |
| U10 | Candidate影响既有订单 | Base/Candidate差异正确 |
| U11 | CTP Purpose | 无激活按钮 |
| U12 | INSERT_IMPACT_ANALYSIS | 无激活按钮 |
| U13 | Candidate采用 | Actor/Time可追溯 |
| U14 | 外Domain共享设备阻挡 | 页面显示阻挡原因 |
| U15 | MaxImpactedOrders超阈值 | Warning，不表示截断 |
| U16 | PI UNLOCATED | 显示数量并提示不可MES |
| U17 | 无PI规划Task | 显示不可MES |
| U18 | Planning Placeholder依赖Task | 不允许下MES |
| U19 | FAILED Run恢复 | 新Run产生，旧Run仍FAILED |
| U20 | 设备故障 | 只提示影响/建议，不自动Pause |
| U21 | Priority Segment | 页面不出现全局PriorityScore |
| U22 | Rule发布 | 新Version生成，历史不覆盖 |

---

# 二十九、新增接口与权限验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| U23 | 4号位调用2号位HTTP | 正式代码中不得存在新的直接依赖 |
| U24 | Overview查询 | 4→5，业务结果语义不变 |
| U25 | G4/G7/G8迁移 | 迁5后URL/DTO优先兼容，结果不漂移 |
| U26 | CTP发起 | 4→3，3治理后调用2 |
| U27 | Manual ETA查询/编辑 | 4→5，5维护事实 |
| U28 | Manual ETA生效日期展示 | 不由前端计算AvailableTime |
| U29 | Demand Protection查看 | 4→5 |
| U30 | Demand Protection释放 | 4→5→2，4/5均不重算保护数量 |
| U31 | MES受控操作 | 4→5→2，权限与MES资格同时满足 |
| U32 | Login/Refresh/Logout/Me | 4→3正常 |
| U33 | 无功能权限进入页面 | 前端隐藏/403，后端仍拒绝 |
| U34 | Factory=TJ用户查询BJ | 不返回BJ数据 |
| U35 | Factory={BJ,TJ}+Department=MC | 仅显示机加范围 |
| U36 | ProductFamily=X用户维护Y | 拒绝 |
| U37 | Domain=D01用户确认D02 Candidate | 拒绝 |
| U38 | 手工构造HTTP绕过按钮 | 后端仍拒绝 |
| U39 | Global用户 | 按Global规则访问授权功能 |
| U40 | Current User | Permissions与四类Scope正确加载 |
| U41 | Scope筛选器 | 不允许选择未授权Factory/ProductFamily/Department/Domain |
| U42 | Audit查看 | 关键动作可追溯且不泄露密码/Token |
| U43 | Approval旧表存在 | 不出现当前V1未冻结的强制审批页面 |
| U44 | PAUSE/RESUME | 页面和业务操作中均不得新增 |
| U45 | 4→1调用 | 不得存在 |
| U46 | 权限通过但Purpose=CTP尝试Activate | 后端拒绝，页面不误导 |
| U47 | 权限通过但Task不可MES | 后端拒绝，页面显示真实原因 |
| U48 | ESTIMATED供给 | 不显示为正式承诺 |
| U49 | Run普通查询与Run治理 | 普通查询走5，治理动作走3 |
| U50 | 无业务Scope用户 | 页面明确提示无可访问业务范围，不加载全量数据 |

---

# 三十、性能与交互要求

继续保持：

- 甘特图按时间窗和资源分页/虚拟滚动；
- 大列表服务端分页；
- Explanation按需加载；
- Candidate Diff先摘要后详情；
- 不一次加载90天所有Task；
- 不在前端计算Pegging/负荷；
- 不在前端做跨Domain WHATIF。

新增：

- Current User / Permissions / Scope不应每个组件重复请求；
- 可在合理生命周期缓存当前用户权限上下文；
- Scope查询应在服务端完成过滤；
- 不允许为了中转接口产生逐Task/N+1远程调用；
- G4/G7/G8迁移不应引起前端重复拉取两套结果。

---

# 三十一、明确不做

V1禁止4号位建设：

- 可视化流程编排器；
- 任意拖拽直接改正式Task；
- MultiDomain Candidate编辑器；
- 物流有限排程界面；
- 通用审批平台；
- 通用BI平台；
- 通用规则DSL编辑器；
- Solver Trace逐步回放；
- Impact Graph图数据库可视化；
- Scenario多目标仿真平台；
- 自动设备PAUSE/RESUME控制台；
- 任意记录级权限配置器；
- 任意字段级权限配置器；
- ABAC表达式平台；
- 第二套Role/Permission体系；
- 第二套Business Scope体系；
- 直接面向2号位的HTTP接口层。

---

# 三十二、4号位交付物（v1.1）

1. 登录与Current User；
2. 前端权限守卫；
3. Factory/ProductFamily/Department/Domain Scope控件；
4. 排产总览；
5. 订单详情；
6. 资源甘特图；
7. CTP/插单评估；
8. Candidate对比与确认；
9. Explanation/异常页；
10. PI Position/供给追溯；
11. Manual ETA维护；
12. Demand Protection查看/释放入口；
13. Rule/Parameter/Strategy维护；
14. Run/Version状态；
15. MES资格/受控操作；
16. User/Role/Permission/Scope管理页面；
17. Audit页面；
18. U01～U50测试结果；
19. **与3号位认证/权限/治理API联调记录**；
20. **与5号位业务接口/API联调记录**；
21. G4/G7/G8/Schedule接口迁移兼容测试记录；
22. 原4→2 HTTP依赖清理记录（如现有代码存在）。

原v1.0中的：

> “与2号位、3号位API联调记录”

正式修改为：

> **与3号位、5号位API联调记录。**

---

# 三十三、与3号位联调红线

4号位可直接调用3号位：

- Authentication；
- RBAC；
- Business Scope；
- Rule / Parameter / Strategy；
- Run / Candidate治理。

4号位不得：

- 自己维护Auth真值；
- 自己生成Permission；
- 自己扩大Scope；
- 直接写APS_Auth；
- 把普通计划结果查询继续无限扩展到3号位。

---

# 三十四、与5号位联调红线

4号位可直接调用5号位：

- Overview；
- Order；
- Gantt；
- Explanation；
- Supply/Pegging Trace；
- PI Position；
- Manual ETA；
- Demand Protection；
- MES；
- G4/G7/G8/Schedule普通查询。

4号位不得要求5号位：

- 重做Pegging；
- 重做Allocation；
- 重新计算AvailableTime；
- 重做Demand Protection；
- 生成FinalTask；
- 直接改2号位结果表。

---

# 三十五、与2号位关系

4号位不再有正式直接HTTP/API关系。

如果页面需要2号位业务：

- 治理类入口经3号位；
- 非治理业务经5号位。

4号位不得因为“调试方便”重新直连2号位。

---

# 三十六、与1号位关系

4号位不直接调用1号位。

1号位计算结果由2号位持久化后，经5号位业务查询接口展示。

---

# 三十七、数据库边界

4号位原则上不Own APS运行业务数据库。

4号位：

- 不创建Task业务表；
- 不创建Allocation业务表；
- 不创建PlanVersion运行表；
- 不创建Auth第二套表；
- 不为页面中转新建缓存真相表。

页面需要的数据通过3号位或5号位正式API获取。

---

# 三十八、完成定义（Definition of Done）

4号位完成必须同时满足：

## 原业务DoD继续成立

- 不直接写APS运行库；
- 只展示FinalTask；
- Task状态值域正确；
- CTP区分确定与Estimated；
- Candidate严格单Domain；
- 跨Domain CTP只做链式结果汇总；
- Candidate Base对比清楚；
- CTP/Impact Analysis不能激活；
- Explanation展示根因；
- PI Position可追溯；
- UNLOCATED/Placeholder明确不可MES；
- FAILED恢复创建新Run；
- Priority Segment按真实规则展示；
- Rule发布不覆盖历史；
- 无直接拖动正式Task写库；
- 无新增V1过度设计。

## v1.1新增DoD

- 4号位只直接调用3号位和5号位；
- 不新增4→2 HTTP依赖；
- 不存在4→1调用；
- 登录/Refresh/Logout/Me可用；
- Current User可取得Roles/Permissions/Scope；
- Factory/ProductFamily/Department/Domain Scope前端可正确限制；
- 后端越权请求仍被拒绝；
- Manual ETA页面可用；
- Demand Protection查看/释放链路正确；
- MES操作链路正确；
- G4/G7/G8/Schedule普通查询迁移后不漂移；
- Run普通查询与Run治理入口不混淆；
- User/Role/Permission/Scope管理页面可用；
- Audit页面可追溯关键动作；
- 旧Approval结构不误触V1强制审批；
- 不新增PAUSE/RESUME；
- U01～U50全部通过或明确列出未通过项及原因。

---

# 三十九、一句话交付要求

> **4号位V1的任务不是做一个“看起来强大的APS大屏”，而是：在新的接口边界下，只通过3号位和5号位，把当前ACTIVE计划、CTP/插单影响、Candidate差异、真实延期原因、PI位置、供给追溯、Manual ETA、Demand Protection、规则参数、运行状态、MES资格以及用户权限准确呈现，并让业务人员只能在自己的功能权限和Factory/ProductFamily/Department/Domain业务范围内，通过受控后端服务改变计划状态，绝不绕过业务Owner直接修改运行结果。**

---

# 2026-09-01 Domain专项冻结增量（本版新增，冲突时仅此专项口径覆盖历史Domain表述）

# APS V1 4号位页面与业务操作开发实施包 v1.2
## Domain专项冻结对齐补充版

**版本**：v1.2  
**日期**：2026-09-01（北京时间）  
**基于版本**：《APS V1 4号位页面与业务操作开发实施包 v1.1（20260901职责与前端接口冻结对齐版）》  
**上位业务基线**：《APS V1 最终全部流程与业务基线 v1.2（Domain专项冻结修订版）》  
**专项依据**：《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》

---

# 0. 本版定位

本版只补充4号位的Domain页面与业务操作职责。

继续保持v1.1已经冻结的前端边界：

```text
4号位
├─ → 3号位：认证、授权、规则、参数、策略、ScheduleRun、Candidate、Domain治理
└─ → 5号位：计划结果、订单、甘特、供给追溯、PI Position、Manual ETA、Demand Protection展示及业务操作中转
```

4号位仍然：

- 不直接调用2号位；
- 不直接调用1号位；
- 不直接维护APS业务数据库；
- 不在前端重做业务判断。

---

# 1. Domain页面定位

4号位新增最小排程域（Domain）维护页面。

页面用途：

> 供有权限的管理员查看、维护和启停排程域定义（DomainDefinition）。

不是：

- 通用规则设计器；
- Domain表达式设计器；
- 调度编排平台；
- 运行结果主页面；
- 跨Domain可视化分析平台。

---

# 2. Domain维护页面最小功能

V1最小功能：

1. Domain列表；
2. Domain详情；
3. 新增Domain；
4. 修改Domain；
5. 启用 / 停用；
6. 查看ScopeType；
7. 查看产品族（ProductFamily）；
8. FACTORY_FAMILY时查看/选择工厂（Factory）；
9. 查看稳定排序（SortOrder，如启用）；
10. 查看审计信息。

不增加：

- SQL条件；
- DSL；
- 任意组合筛选表达式；
- Customer/Stage/Resource维度配置；
- 多层嵌套Domain。

---

# 3. Domain列表页面

建议最小列：

- DomainKey；
- DomainName；
- ScopeType；
- Factory；
- ProductFamily；
- IsActive；
- SortOrder；
- UpdatedBy；
- UpdatedAt。

展示语言：

- 产品族域（FAMILY）；
- 工厂+产品族域（FACTORY_FAMILY）。

避免页面只显示英文枚举。

---

# 4. Domain编辑规则

前端根据ScopeType做最小交互约束：

## FAMILY

- ProductFamily必填；
- Factory不填写。

## FACTORY_FAMILY

- ProductFamily必填；
- Factory必填。

前端可以做即时校验，但最终合法性必须由3号位后端再次校验。

---

# 5. DomainKey页面处理

DomainKey：

- 必须展示；
- 可在新增时录入或由3号位按既定规则生成；
- 一旦启用，不因DomainName修改自动变化。

4号位不得自行拼接：

```text
Factory + ProductFamily
```

作为权威DomainKey。

真实DomainKey以3号位返回为准。

---

# 6. Domain启停操作

启用/停用属于治理写操作。

流程：

```text
4号位
↓
3号位Domain治理接口
↓
认证
↓
权限校验
↓
业务合法性校验
↓
保存
↓
审计
```

4号位不得直接UPDATE数据库。

---

# 7. 当前Run不受页面修改影响

页面需要明确提示：

> Domain配置修改仅影响后续新启动的排程运行（ScheduleRun），不会改变当前已启动Run的冻结Domain集合。

这是关键业务提示。

不能让用户误以为：

“刚刚停用Domain，当前正在跑的FULL就会立即移除这个Domain。”

---

# 8. ScheduleRun页面补充Domain信息

现有ScheduleRun页面可增加最小展示：

- ExpectedDomainKeys；
- 实际产生PlanVersion的Domain；
- Domain成功/失败/阻断摘要；
- Run最终状态。

但不要新建“Domain运行平台”。

---

# 9. PlanVersion / 计划结果页面

现有计划结果页面：

应增加Domain筛选/标识。

展示：

- DomainKey；
- DomainName（如可查）；
- PlanVersion。

但普通计划结果数据仍：

```text
4 → 5
```

不是：

```text
4 → 3
```

3号位只负责Domain治理和Run/Candidate治理。

---

# 10. Candidate页面

Candidate继续严格单Domain。

创建Candidate时页面必须：

- 选择或显示唯一Domain；
- 显示Base ACTIVE PlanVersion；
- 禁止选择多个Domain；
- 不提供“跨Domain一起重排”按钮。

若用户目标涉及多个Domain：

V1不在一个Candidate中完成。

---

# 11. Candidate共享资源说明

当Candidate受其它Domain ACTIVE计划共享资源阻挡时：

4号位只展示后端返回的解释。

例如：

> 目标资源已被其它排程域当前有效计划占用，该占用在本次Candidate中不可移动。

4号位不得自行推断或重算资源冲突。

---

# 12. Domain业务范围权限

Domain是正式业务范围（Business Scope）维度。

页面展示、按钮、可选项需要根据3号位返回权限处理。

但：

> 前端隐藏仅用于体验，不能替代后端授权。

例如：

- 无DOMAIN_VIEW：不显示Domain维护入口；
- 有DOMAIN_VIEW无DOMAIN_MAINTAIN：只读；
- 有DOMAIN_MAINTAIN：允许编辑；
- 无运行触发权限：不能启动FULL。

具体权限码遵循3号位实际实现，不在4号位硬编码另一套权限体系。

---

# 13. Domain依赖页面

V1仅需要最小能力。

如纳入4号位治理界面，可提供：

- 上游Domain；
- 下游Domain；
- 是否有效；
- 基本依赖关系查看/维护。

节点必须展示真实DomainKey/DomainName。

不得用ProductFamily.Code代替Domain。

不建设：

- 图形化复杂Workflow Designer；
- 拖拽编排平台；
- 通用DAG编辑器。

---

# 14. Domain失败与阻断展示

当上游Domain失败导致下游阻断时：

页面至少能区分：

- 成功；
- 失败；
- 被上游失败阻断；
- 未参与当前Run。

避免全部显示成“失败”。

---

# 15. PARTIAL_SUCCESS展示

一次ScheduleRun可能：

- 部分Domain成功；
- 部分Domain失败/阻断；
- 无关Domain已成功发布。

因此PARTIAL_SUCCESS页面应展示：

> 哪些Domain成功、哪些失败、哪些被阻断。

不要简单显示一个红色“失败”而掩盖已成功Domain。

---

# 16. Domain性能信息

如果现有运行监控页有性能信息，可增加：

- DomainKey；
- DemandCount；
- LogicalProductionDemandCount；
- FinalTaskCount；
- ContextBuildMs；
- PeggingMs；
- SolverMs；
- PersistMs；
- TotalMs。

仅展示后端数据。

4号位不计算。

---

# 17. 4号位与3号位Domain接口

4号位直接调用3号位：

- DomainDefinition Query；
- DomainDefinition Create/Update；
- Enable/Disable；
- DomainDependency Query/Manage（如正式开放页面）；
- ScheduleRun Domain Snapshot；
- Candidate Domain治理。

具体HTTP路径不在业务实施包硬编码。

---

# 18. 4号位与5号位结果接口

仍通过5号位：

- PlanVersion结果；
- OrderScheduleSummary；
- Gantt；
- Supply Trace；
- Explanation；
- FinalTask；
- Demand Protection展示；
- MES资格与受控业务操作中转。

Domain专项不改变这条接口边界。

---

# 19. 不允许出现的前端调用

禁止：

```text
4 → 2 HTTP/API
4 → 1
4 → APS_Production直接SQL
```

也禁止因为Domain新增：

```text
4 → 2 DomainQueryController
```

重新出现。

---

# 20. 前端术语要求

页面中文主表达：

- 排程域（Domain）；
- 排程域定义（DomainDefinition）；
- 产品族域（FAMILY）；
- 工厂+产品族域（FACTORY_FAMILY）；
- 排程运行（ScheduleRun）；
- 计划版本（PlanVersion）；
- 候选计划（Candidate）；
- 业务范围（Business Scope）。

禁止大面积只展示：

Domain / Scope / Run / Candidate

导致业务人员难以理解。

---

# 21. 可以复用的原页面

优先在现有：

- 系统配置；
- 规则参数；
- Run管理；
- Candidate；
- 计划结果；

页面中增量加入Domain内容。

不要求为了Domain重做整个菜单和页面框架。

---

# 22. Domain专项验收场景

| 编号 | 场景 | 预期 |
|---|---|---|
| UI-D01 | Domain列表 | 正确显示中文业务含义 |
| UI-D02 | FAMILY新增 | ProductFamily必填、Factory不要求 |
| UI-D03 | FACTORY_FAMILY新增 | Factory+ProductFamily必填 |
| UI-D04 | 非法配置 | 后端拒绝并显示错误 |
| UI-D05 | 修改DomainName | DomainKey不自动变化 |
| UI-D06 | Run中停用Domain | 提示仅影响后续Run |
| UI-D07 | 无查看权限 | 不展示入口且后端拒绝 |
| UI-D08 | 只读权限 | 不允许编辑 |
| UI-D09 | Candidate创建 | 只能选择一个Domain |
| UI-D10 | 多Domain Candidate | 页面不提供 |
| UI-D11 | Domain依赖展示 | 使用真实DomainKey |
| UI-D12 | PARTIAL_SUCCESS | 能区分成功/失败/阻断Domain |
| UI-D13 | 计划结果 | 可显示Domain归属 |
| UI-D14 | 普通结果查询 | 仍走5号位 |
| UI-D15 | Domain治理 | 只走3号位 |
| UI-D16 | 4→2调用 | 不存在 |

---

# 23. 本版明确不做

不新增：

- Domain DSL配置器；
- SQL表达式编辑器；
- Domain流程设计器；
- MultiDomain Candidate页面；
- Domain资源配额页面；
- Domain资源借用页面；
- Domain并行调度控制台；
- Domain运行状态机；
- 新的一套普通结果查询入口。

---

# 24. 完成定义

4号位Domain专项完成需要：

- 有最小Domain维护页面；
- 中文业务术语清晰；
- FAMILY / FACTORY_FAMILY交互正确；
- DomainKey不由前端自造；
- 修改只影响后续Run的提示明确；
- Candidate严格单Domain；
- Domain权限由3号位后端控制；
- Run页面能展示Domain结果摘要；
- 普通计划结果仍走5号位；
- Domain治理只走3号位；
- 不出现4→2/4→1正式调用；
- 不建设Domain平台化UI。

---

# 25. 一句话交付要求

> **4号位本轮只需要在现有前端框架中增加“排程域（Domain）最小维护 + Run/Candidate的Domain展示与选择 + 权限控制”，Domain治理全部调用3号位，普通排程结果继续调用5号位，不直接调用2号位，也不要把Domain做成复杂的规则或调度设计平台。**
