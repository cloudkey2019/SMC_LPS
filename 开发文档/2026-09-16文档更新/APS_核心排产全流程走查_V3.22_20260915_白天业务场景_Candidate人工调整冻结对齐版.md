# APS 核心排产全流程走查（完整版 V3.22 白天业务场景 / Candidate人工调整冻结对齐版）

**当前版本**：V3.22  
**当前版本日期**：2026-09-15（北京时间）
**基于版本**：V3.21（2026-09-14 SH旧模型残留清理版）  
**适用范围**：APS V1  
**文档性质**：端到端运行流程、0～5号位接力边界、异常恢复与发布闭环的权威技术走查文档  
**维护责任**：2号位维护主流程；各号位按职责维护自己负责的接口与实现  

## 当前版本变化说明（2026-09-15）

**修改依据**：业务基线v1.6 + Pegging v1.5 + 有限产能v1.5 + 白天业务场景/Candidate人工调整指导v1.1  
**修订原则**：完整继承V3.21及20260914 SH纠错，只重写白天Candidate业务入口、Run输入、ACTIVE Base和人工调整端到端流程。  
**影响边界**：Domain定义、PI/SH、90天Solver、跨版本Continuation、夜间FULL、跨Domain普通Quantity-Time保持。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | 端到端重写 | 第八部分 | 白天入口收口为10个业务场景；一次LOCAL一个场景、同场景多对象。 | v1.1最终论证 | 白天操作可审计、可解释 | 4→3→2请求语义变化 | ScopeJson Schema v2 | 影响2/3/4号位 |
| 2 | Candidate治理 | 第八/十部分 | Base必须当前ACTIVE、禁止Candidate叠Candidate、旧Base激活保护。 | Candidate版本一致性 | 防止旧Candidate覆盖新正式计划 | Run创建/激活校验 | 复用BasePlanVersionId | 影响2/3号位 |
| 3 | 人工调整 | 第八部分 | 多订单目标、多Task软目标、多故障/Calendar资源；Seed不屏蔽其它真实事实。 | 单场景多对象 | 支持白天真实人工调整 | ScopeJson v2结构化输入 | 不增表/列 | 影响1/2/3/4号位 |
| 4 | RunType边界 | 第八/十部分 | 甘特改为LOCAL；LOCAL影响扩大仍为LOCAL；MANUAL只表示整Domain主动重排。 | v1.1边界 | 纠正旧甘特MANUAL口径 | RunType/Purpose映射不新增值 | 无 | 影响1/2/3/4号位 |


> **2026-09-14 P0文档纠错（业务版本保持V3.21不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

---

## 0. 冻结声明与权威关系

本文件是以下三份**已冻结业务文档**向技术流程层的落地说明：

1. 《APS V1最终全部流程与业务基线 v1.5（20260913 跨版本排程连续性 / 跨厂SH联合冻结对齐版）》；
2. 《APS Pegging供需承接与分层计算业务说明 v1.4（20260913 跨版本排程连续性 / 跨厂SH联合冻结对齐版）》；
3. 《APS有限产能排产与滚动90天计划业务说明 v1.4（20260913 跨版本排程连续性 / 跨厂SH联合冻结对齐版）》；
4. 《APS集成接口设计规范 v1.31（20260913 跨版本排程连续性 / 跨厂SH联合冻结对齐版）》。

**冻结规则**：

- 本文件不得独立修改已冻结业务；
- 若旧走查、旧接口、旧字段、旧DDL或旧代码与冻结业务冲突，优先修改旧技术实现；
- 不得因为“更标准、更优雅、更易扩展、未来V2可能需要”而把V1已删除的复杂度重新加回来；
- 只有明确提出“重新打开冻结决策”并经0号位重新裁决，才允许改变业务口径。

当前V3.22完整继承V3.21及2026-09-14 SH纠错后的全部冻结正文，本版只合入2026-09-15白天业务场景/Candidate人工调整裁决。历史章节中的V3.17～V3.21形成记录继续保留用于追溯；如与V3.22当前白天Candidate口径冲突，以V3.22当前口径为准。

---

## V3.22当前版本修订范围

V3.21当前有效增量包括：V3.18 Domain专项、V3.19 Supply / PI Position专项、V3.20 PI Position执行起点上下文专项，以及V3.21跨版本排程连续性/跨厂SH联合专项。以下既有条目继续继承，本版只修正与两项新裁决直接相关的流程与职责：

- 排程域（Domain）不再等同于产品族（ProductFamily）；
- 排程域定义（DomainDefinition）成为稳定治理对象；
- FULL按真实Domain依赖串行逐Domain执行；
- 每个Domain独立计划版本（PlanVersion）；
- 跨Domain数量—时间切片与共享资源阻挡继续沿用既有冻结业务；
- 候选计划（Candidate）继续严格单Domain；
- 不恢复旧多Hangfire Domain并发架构；
- 不新增MultiDomain Candidate、Domain资源配额、VirtualInventoryBalance等平台化设计。

除以上内容外，V3.17正文完整继承。

---

## V3.17母版主要修订内容（历史继承；不得作为V3.21现行SH主链）

本版相对V3.16的关键变化如下：

1. **Pegging重写**：取消全局PriorityScore作为V1主排序权威，改为“计算层 → 有序规则段 → 第一命中 → 段内排序”；补Demand/Supply双边余额、Allocation、Demand Protection、PI选择与PI Position。
2. **Task职责重写**：2号位不再提前实例化最终Operation Task；2号位形成逻辑生产需求与完整ScheduleContext，1号位生成FinalTask、实际Resource、Start/End、最终拆合批和真实Task依赖。
3. **采购/VMI正式进入V1**：不再把PipelineSupplies写成V1空集合；PO、VMI、已到厂未入库、厂间在途均作为真实Quantity-Time供给参与Pegging与交期判断。
4. **规划性采购占位**：当库存/PO/VMI均无正式承诺时，允许仅在内存中形成ESTIMATED/NOT_COMMITTED的Planning-only Purchase Placeholder，使90天计划不断链，但不得作为确定性客户承诺。
5. **两类跨厂重写（V3.17历史旧口径，已由V3.21废止）**：当时曾写为Stage Handoff“目标M → PI → PI内部Position → 新增生产”、Inter-factory Order“目标BS/KS → SH；SH内部在途 → 同SH Received → 未生产”。**该SH三段式仅保留用于版本追溯，不得实现；V3.21现行口径见§19.2与§68。**
6. **ShippingTask退出V1排程主链**：运输只形成LeadTime→AvailableTime，不建设车辆、班次、月台、发货Task等有限物流体系。
7. **90天统一有限产能**：V1只有一套正式Solver，远近计划资源语义一致；不再把8～90天按ROUGH_CUT/LeadTime粗排作为正式第二套排程。
8. **计划良率进入主链**：2号位反算PlannedProcessQty，1号位按PlannedProcessQty占能力，Allocation仍按Net Required Qty闭合。
9. **Candidate重写**：上一ACTIVE普通未锁Allocation可释放重新竞争；ScopeJson只代表初始业务请求范围，不得截断真实影响传播；MaxImpactedOrders只作提示阈值。
10. **白天共享资源冻结**：其它Domain当前ACTIVE在共享资源上的占用作为不可移动阻挡块；白天Candidate仍严格单Domain。
11. **跨Domain插单补齐**：按Domain_Dependency拓扑串行多个单Domain WHATIF，逐层传递Quantity-Time，最终合成一个CTP答案。
12. **夜间跨Domain失败补齐**：上游Domain失败，依赖它的下游本次不得发布新ACTIVE；无关Domain继续发布；人工修复后新建ScheduleRun重算失败链。
13. **Explanation职责重写**：根因由1号位求解过程中原生产出，2号位聚合持久化，5号位不再生成最终排程原因。
14. **审批边界收敛**：WHATIF不能自动正式；V1只要求最小人工确认与审计，不把完整OA审批体系强制为主链前置。
15. **防过度设计**：不建MultiDomain Candidate、VirtualInventoryBalance、FrozenZoneSnapshot平台、第二套远期Solver、有限物流Task、动态插件市场、复杂容量配额平台。

---

# 第一部分：整体架构与0～5号位职责

## 1. APS V1的运行本质

APS V1分成两个必须严格分开的核心计算阶段：

> **2号位先把“谁需要什么、由什么承接、还缺多少、需要生产多少”算清楚；1号位再把这些逻辑生产需求放到90天真实有限资源上，决定“在哪台设备、什么时候做、最终何时完成”。**

因此：

- Pegging是**数量真相**；
- 有限产能是**时间和资源真相**；
- 任何一个角色不得同时维护两套真相。

## 2. 0号位

负责业务红线、V1/V2范围、核心优先规则、重大冲突与最终裁决。

不负责Solver内部队列、索引、搜索次数等技术细节。

## 3. 1号位：有限产能和时间真相

负责：

- FinalTask；
- 实际Resource；
- Start/End；
- 最终拆批/合批；
- Task-to-Task真实物理依赖；
- 动态瓶颈；
- 混合正排/倒排；
- Setup；
- Stage重叠；
- Candidate受影响范围传播；
- 局部修复与单Domain全排兜底；
- ExplanationFactDraft；
- 同时求解既存执行连续份额（Continuation Slice）与新增自由份额（Free Slice）；
- 原样回传连续性关联键（ContinuationKey）或现有等价稳定键；
- 工序级最后报工设备（LastReportResourceCode）只作为资源连续性偏好消费；
- 对同一PI同一Operation，保证既存执行连续份额先于新增自由份额，且APS自动求解不形成多设备时间重叠；
- 在SH场景中形成源工厂真实准备完成时间（SourceReadyTime）。

**红线**：1号位纯内存计算，不直接读写APS业务数据库；不解析MESWorkOrderNo/TaskNo生命周期，不读取ERP跨厂在途视图。

## 4. 2号位：主流程、Pegging和数量真相

负责：

- SchedulingOrchestrator主入口；
- ScheduleContext组装；
- Demand/Supply池；
- 分层Pegging；
- DemandBalance / SupplyBalance；
- Allocation；
- Demand Protection / Strict Binding执行；
- 计划良率反算；
- 逻辑生产需求；
- 当前LogicalProductionDemand数量Q形成后的IN_PROGRESS执行覆盖识别与逐MES工单连续份额分桶；
- ContinuationKey与OldTaskNo/MESWorkOrderNo内部映射；
- SH第一层单一Supply Allocation、第二层源厂履约Demand及SourceFactoryId重新归域；
- 跨DomainQuantity-Time传递；
- 按本次Run冻结的真实排程域（Domain）集合，串行逐Domain调用同一套1号位有限产能求解器（Solver）；
- 统一持久化Task、Allocation、TaskShare、Explanation等结果；
- Candidate变化种子识别；
- MES下发资格准备。

**数据库红线**：2号位是APS运行时业务结果表的主要写入者。

2号位不应：

- 提前生成最终Operation Task；
- 自己再跑一套有限产能Solver；
- 永久修改共享InventoryBalance作为跨版本占用真相；
- 把5号位变成逐Demand、逐Supply的运行时插件服务；
- 绕过5号位直接解释MES原始工单、工序报工或Stage进度来计算连续份额；
- 用旧TaskNo/MESWorkOrderNo反向决定当前Q；
- 把SH重新拆成Transit / Received / ZP / BP多份Supply，或使用在途数量倒算SH未生产量。

## 5. 3号位：规则、参数与运行生命周期

负责：

- 规则/参数编辑、校验、发布；
- RuleSet / ParameterSet / StrategyProfile版本治理；
- ScheduleRun生命周期；
- Candidate版本壳与激活边界；
- 运行开始前冻结并装载规则参数快照；
- SH标准跨厂运输/检验/转运提前期参数；
- SH第二层整单优先级规则。

3号位不负责：

- 逐Demand排序；
- 逐Supply分配；
- Task/Pegging运行结果写入；
- 在排程过程中被逐笔同步回调。

## 6. 4号位：页面和业务动作入口

负责：

- 甘特图；
- CTP；
- 插单影响；
- Candidate对比；
- 异常展示；
- 人工确认；
- 参数维护。

4号位不得直接写APS运行时业务表。

## 7. 5号位：ODS、防腐事实、BOM和复杂事实计算

负责：

- ODS；
- BOM展开；
- Stage路径；
- ProcessCode/ERPProperty真实业务属性；
- 跨厂结构事实；
- PI Position复杂计算；
- NextOperationContext计算；
- MES工单/工序/Stage执行事实标准化；
- 工序级最后报工设备（LastReportResourceCode）事实标准化；
- 跨厂物流事实及SH号在跨厂在途视图中的存在性事实；
- 采购/VMI等外部供给事实准备；
- 数据质量Issue。

5号位不负责：

- 最终Demand→Supply Allocation；
- 运行时Supply扣减；
- Demand Protection最终创建/释放；
- FinalTask；
- 最终延期ReasonCode；
- 当前Q/E运行时分桶与TaskNo身份治理；
- 第二套SH主档、普通Demand↔SH固定关系或SourceReadyTime计算。

## 7.1 规则与参数引擎运行边界

V1运行时只允许两种基础配置模式：

1. **有序规则第一命中**：用于Demand排序、Lock Policy等“多条规则从上到下命中第一条”的主题；
2. **全局默认 + 范围例外覆盖**：用于LeadTime、计划良率、StageLeadTime、拆批参数、排程方向等标量或策略参数。

RuleSet / ParameterSet / StrategyProfile六张治理表属于保护区，本轮不因为“减法”而贸然删除。

运行开始前加载不可变版本快照，1/2号位在内存消费。禁止：

- 逐Demand调用3号位；
- 逐Task调用5号位；
- 动态SQL/脚本/DSL控制主链；
- 为每个业务主题再建一套插件注册平台。

---

# 第二部分：夜间FULL从数据准备到发布的完整主流程

## 8. 阶段0：数据准备与运行身份

### 8.1 数据准备范围

夜间FULL在02:00正式求解前，需要准备或刷新：

- 活跃Order_Canonical；
- 本次Order版本快照；
- Material / Factory / ProductFamily；
- Resource；
- RoutingOperation / RoutingDependency / OperationResourceEligibility；
- BOM Workset / StageDetail / CrossFactoryEdge；
- Inventory；
- MES WorkOrder / OperationProgress / StageProgress；
- PI相关XC、跨厂在途等事实；
- PO、VMI、已到厂未入库等外部供给；
- 规则与参数冻结版本；
- Domain_Dependency。

### 8.2 ScheduleRun / PlanVersion创建时序

V1业务只要求：

- 本次运行有明确ScheduleRun；
- `ScheduleRun.ExpectedDomainKeysJson`在运行启动时冻结本次预期Domain集合，运行过程中不得静默增删；
- 每个Domain有独立PlanVersion；
- 白天Candidate的ExpectedDomainKeysJson严格只有一个Domain；
- 数据与版本可追溯；
- 运行状态最终闭合。

`ExpectedDomainKeysJson`是ScheduleRun独立运行级字段，不属于白天ScopeJson。

**本走查不强制2号位把ScheduleRun/PlanVersion固定创建在某一个分钟点。**

如果2号位现有代码采用“先建壳再准备Order快照”，可以继续；如果采用其它可追溯时序，也不因为文档形式要求而重构。

### 8.3 Order准入

只有：

`Order_Canonical.Status = OPEN`

的订单/生产指示进入当前排程。

CLOSED/CANCELLED不得进入新的BOM Request、Pegging和Task生成。

### 8.4 客户SALES_ORDER顶层数量红线

客户订单进入APS时，Order.Quantity已经是ERP成品库存处理后的需生产数量。

例如：

- 客户原始120；
- ERP成品库存满足20；
- APS收到100。

APS顶层从100开始，**不得再次搜索普通成品库存冲抵这100**。

---

## 9. 阶段0.5：跨Domain依赖扫描与拓扑

### 9.1 Domain_Dependency用途

Domain_Dependency用于回答：

> 哪个Product Domain必须先算，哪个Domain才能消费它的生产结果。

例如：

`Domain C → Domain B → Domain A`

则夜间按C、B、A拓扑顺序执行。

### 9.2 Domain_Dependency不再承担统一2天物流权威

旧文档中的`DefaultLeadTimeDays=2`不得再作为正式业务口径。

如果物理字段暂时保留以兼容2号位现有代码，其值应理解为：

> 对真实工厂/Stage转运LeadTime的缓存或兼容字段。

不能继续硬编码“所有跨Domain统一2天”。

### 9.3 夜间共享资源不能因为Domain独立版本而双占

虽然每个Domain有独立PlanVersion，但如果多个Domain共享同一台真实设备：

> 夜间FULL必须保证同一ScheduleRun内共享资源时间轴真实互斥。

可采用按拓扑/求解顺序把已安排的共享资源占用传给后续Domain，或由1/2/3号位采用其它等价实现。

**禁止把这个要求扩展成资源配额、借用、Quota平台。**

---

## 10. 阶段1：加载当前Domain的完整ScheduleContext

2号位逐Domain组装纯内存求解上下文。

至少包括：

1. Run边界：ScheduleRun、PlanVersion、DomainKey、DataCutoffTime、Horizon；
2. 当前Domain的Demand；
3. 当前可用Supply；
4. Allocation/Lock运行时状态；
5. PI Position事实；
6. Routing候选图；
7. Resource Eligibility；
8. Resource Calendar；
9. 外部Quantity-Time供给；
10. Firm/Frozen/执行不可移动事实；
11. 冻结Rule/Parameter快照。

所有核心I/O必须尽量前置，Pegging与Solver核心循环不得频繁访问数据库。

---

# 第三部分：Pegging完整流程

## 11. 阶段2.0：建立Demand计算层

Demand不是全局一次排序，而是按业务层级推进。

### 第一层：顶层独立需求

包括：

- 普通销售订单（SALES_ORDER），但排除客户分类（CustomerSegment）=`INTER_FACTORY`的厂间出荷指示（SH）订单；
- 顶层MTS/SS/SS_U等Production Instruction类独立需求；
- 其它独立补充需求。

SH订单自身不作为普通顶层Demand重复进入第一层；它在目标侧作为SH单一Supply参与承接。

### 第二层：SH源工厂履约需求

当上一层普通Demand由SH承接后，Demand→SH在上一层结束。

随后按SH整单状态判断：

```text
CLOSED / CANCELLED
→ 退出APS活跃计算

OPEN + ERP_InterplantInTransit_View存在该SH号
→ 已进入跨厂履行
→ 不生成第二层源厂履约Demand

OPEN + ERP_InterplantInTransit_View不存在该SH号
→ 生成一条完整源厂履约Demand
→ RequiredQty = Order.Quantity
```

第二层使用`SourceFactoryId`重新归入源厂Domain，并继续按：

> **PI → PI Position → 只有最终未被PI承接的缺口才形成新增生产。**

不得按第一层多个Allocation份额拆分SH源厂Demand，也不得按Transit / Received / ZP / BP拆分。

### 第三层及以后：PI/BOM下阶逐层扫尾

上层剩余PI、下阶生产需求继续沿BOM向下推进，直到被库存、PI、采购等供给承接，或形成新的生产需求。

**红线**：层级只是运行时业务推进，不建设独立LayerDemand平台。

---

## 12. 阶段2.1：Demand排序

V1不使用统一全局PriorityScore作为正式排序真相。

排序方式：

> **计算层 → 有序规则段 → 第一命中 → 段内排序。**

例如：

1. 已Demand Protection客户单；
2. 临近交期客户单；
3. 普通客户单；
4. MTS补充需求。

同一Demand只进入第一条命中的规则段。

规则由3号位治理，2号位在本次冻结快照中执行。

旧PriorityScore字段如果为了兼容保留，只能作为展示/历史兼容，不得继续驱动V1主链。

---

## 13. 阶段2.2：Demand/Supply双边余额与Allocation

每次分配必须同时满足：

`DemandQty = AllocatedQty + RemainingDemandQty`

`SupplyQty = AllocatedQty + RemainingSupplyQty`

2号位必须在内存中原子执行：

1. 校验Demand剩余；
2. 校验Supply剩余；
3. 生成Allocation；
4. 同时扣DemandBalance与SupplyBalance；
5. 生成AllocationSequence/稳定追溯键；
6. 必要时同步Lock状态。

出现以下情况必须使当前Domain失败：

- Demand超配；
- Supply超配；
- 负余额；
- 同一物理Supply重复使用；
- 跨PlanVersion/跨Domain错误引用；
- 持久化事务只成功一半。

不能仅Warning后继续正式发布。

---

## 14. 阶段2.3：Demand Protection、严格绑定与执行不可逆

V1必须区分三类约束。

### 14.1 Strict Binding

客户专属、质量/环保资格以及源系统真实存在的业务单据强绑定证据等，普通Demand不得竞争。SH的Received/ZP/BP内部状态不再作为独立Supply或Strict Binding对象。

### 14.2 Demand Protection

用于保护已经达到保护条件的Demand所需份额。

锁定的是：

> Demand与必要Supply之间的**数量份额**。

不是整个PI、整个PO。

### 14.3 Execution Constraint

已真实消耗、已领料、已形成不可逆直接履约等事实不能在下一版本重新解释。

但“下阶通用PI已开工”本身不自动等于它永久属于原Demand。

如果没有Strict Binding、Demand Protection或不可逆消耗，普通下阶通用PI仍可在下一FULL重新分配给更高优先Demand。

---

## 15. 阶段2.4：库存供给

普通库存先判断资格，再按Priority和稳定键消耗。

客户SALES_ORDER顶层不二次扣成品库存，但BOM下阶需求仍正常使用合法库存。

库存扣减只发生在本次内存SupplyBalance/Allocation中，不以永久UPDATE `InventoryBalance.AllocatedQty`作为跨版本真相。

---

## 16. 阶段2.5：生产指示PI

### 16.1 PI总剩余量

PI总剩余边界：

`max(Quantity - ReceivedQty, 0)`

MES进度、XC、在途只解释PI剩余数量**在哪里**，不得重新叠加改变PI总量。

### 16.2 先选PI，再消费Position

同一Material多个PI时：

1. 先按冻结PI选择规则选PI；
2. 确认该PI承接Demand多少；
3. 再在该PI内部消费互斥Position。

Demand↔PI正式承诺粒度是：

`PI号 + 数量`

不是“某Stage+数量”。

### 16.3 PI自消费红线

由某PI剩余份额派生出的下阶需求：

> 不允许再由同一个PI作为Supply承接自己。

否则会形成数量闭合但业务自循环。

---

## 17. 阶段2.6：5号位PI Position计算

5号位基于：

- PI总剩余；
- Stage路径；
- MES Stage累计进度；
- PI级XC；
- PI级跨厂在途；
- 必要仓库/工序属性；
- DataCutoffTime；

输出互斥Position。

Position合计必须等于PI总剩余。

### 17.1 正常示例

PI剩余400：

- MACH尚未完成100；
- MACH完成、SURF前XC180；
- MACH→SURF在途100；
- SURF完成待最终入M20。

合计400。

### 17.2 MES累计异常

下游累计超过上游时，默认保守下修下游。

但如果XC/在途等强当前位置事实已经证明上游必须完成，则可以提高必要的有效上游累计值，并记录Issue。

### 17.3 中间Stage缺失

采用下游已证明的最小完成量进行保守推断。

### 17.4 无法闭合

如果强事实互相冲突且无法消除重复：

> 该PI剩余整体降级UNLOCATED，记录Issue。

2号位仍可把PI作为总量Supply承接，1号位从最早可信Stage保守排程。

**UNLOCATED形成的保守Task不得下发MES。**

---


## 17A. 阶段2.6A：5号位标准化既存MES执行上下文

跨版本连续性使用的不是工单级“剩余净数量字段”，而是5号位对既有MES事实的组合解释。

### 工单级

继续消费：

```text
MES_APS_WorkOrder_View / MESWorkOrderSnapshot
→ ProductionInstructionNo
→ MESWorkOrderNo
→ MaterialCode
→ PlannedQty
→ WorkOrderStatus
→ SourceUpdatedAt / DataCutoffTime
```

工单级不新增：

```text
RemainingQty
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

### 工序级

继续使用：

```text
MES_APS_OperationProgress_View
→ OperationName
→ StageCode
→ PlannedQty
→ GoodQty
→ LastReportTime
→ LastReportResourceCode   // 本轮新增，工序级
```

APS本地：

```text
OperationProgressSnapshot.RemainingQty
= max(PlannedQty - GoodQty, 0)
```

并透传：

```text
OperationProgressSnapshot.LastReportResourceCode
```

### Stage级

继续：

```text
MES_APS_StageProgress_View
→ StageCode
→ PlannedQty
→ GoodCompletedQty
→ LastReportTime
```

APS本地：

```text
StageProgressSnapshot.RemainingQty
= max(PlannedQty - GoodCompletedQty, 0)
```

`GoodQty / GoodCompletedQty / WorkOrderStatus`既有英文技术名保持不变。

工序级`LastReportResourceCode`表示该MES工单该具体Operation在数据截止时间前最后一次有效报工设备，只作为后续有限产能资源连续性偏好，不自动形成Hard Lock。

## 18. 阶段2.7：采购、VMI、已到厂未入库与Pipeline

这些供给**正式属于V1**。

### 18.1 ETA优先级

1. 人工ETA；
2. ERP/采购系统真实ETA；
3. Default Purchase LT估算。

### 18.2 AvailableTime

`AvailableTime = EffectiveETA + 到货后可用偏移`

### 18.3 排序

一般按：

- 仓库资格；
- 仓库Priority；
- AvailableTime；
- PO发行时间；
- 单据稳定键。

### 18.4 Planning-only Purchase Placeholder

当：

- 库存0；
- 已到厂未入库0；
- PO无承诺；
- VMI无承诺；

仍存在采购件缺口时，允许在内存创建：

> `Planning-only Purchase Placeholder`

属性：

- Quantity=缺口；
- AvailableTime≈基准时间+DefaultLT；
- `ESTIMATED`；
- `NOT_COMMITTED`。

它：

- 不生成采购单；
- 不生成Task；
- 不下发ERP；
- 不落成真实承诺供给。

如果CTP依赖它，只能回答：

> “估算最早日期，非确定性承诺”。

正式PO/VMI出现后，下次排程自动用真实Supply替换。

---

## 19. 阶段2.8：跨厂模式判定与Pegging

### 19.1 Stage Handoff（大工艺接续型）

正式顺序：

> **目标工厂可直接使用M → 选择PI → PI内部Position → 新增生产。**

例如目标B需要C500：

- B M=100；
- PI-C01剩余300；
- PI Position中：B厂等待100、在途120、XC80；
- 缺口100形成新增生产。

XC、PI级跨厂在途不能再作为PI外部独立Supply重复计算。

不搜索“上游普通M库存”作为自动跨厂借用供给。

### 19.2 Inter-factory Shipping Instruction（厂间出荷指示型）

SH识别：

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

第一层正式顺序：

> **目标BS/KS普通库存 → SH单一Supply。**

普通Demand与SH之间允许多对多，由2号位本次Pegging形成Allocation。

进入某张SH后不再展开Transit / Received / ZP / BP多Supply，而做整单判断：

```text
CLOSED / CANCELLED
→ 退出

OPEN + ERP_InterplantInTransit_View存在该SH号
→ 已进入跨厂履行
→ 不生成源厂履约Demand

OPEN + ERP_InterplantInTransit_View不存在该SH号
→ 生成一条完整源厂履约Demand
→ RequiredQty = Order.Quantity
```

跨厂在途视图查询成功但0行属于合法成功。

第二层Demand使用`SourceFactoryId`重新归入源厂Domain，继续：

> **PI → PI Position → 缺口新增生产。**

同一SH号只允许整单一次入库，因此目标侧只有一个整单AvailableTime。

### 19.3 运输时间

两类跨厂都不生成有限物流Task。

运输、检验、转运仅形成：

`上游完成时间 + 真实LeadTime = 下游AvailableTime`

---

## 20. 阶段2.9：形成逻辑生产需求

Pegging完成后，2号位输出的不是最终Task，而是：

- 为哪个Allocation生产；
- Material；
- Factory；
- FromStage / ToStage；
- Net Required Qty；
- DueDate/业务优先关系；
- Lock/Firm/Frozen约束；
- 物料Quantity-Time依赖。

对于没有正式PI承接的净生产缺口，允许形成“无PI号规划占位生产需求”。

它可进入有限产能，但最终Task不得下MES，直到正式执行身份闭合。

---

# 第四部分：计划良率与2号位→1号位求解上下文

## 21. 计划良率反算

V1正式支持计划良率。

例如最终需要100件合格品，某Stage计划良率98%，则该Stage可能需要加工103件。

2号位负责从下游净需求向上反算：

- `NetRequiredQty`；
- `PlannedProcessQty`。

1号位按PlannedProcessQty占用设备能力。

Allocation仍按净合格产出闭合。

### 21.1 已有PI不因良率放大

PI剩余60仍然最多作为60件Supply。

如果预计后续损失会使最终合格量不足，额外缺口形成新生产需求，不能把PI变成62。

### 21.2 当前不假设BOM ScrapRate

BOM材料损耗与Stage计划良率是两种业务语义。

V1不因为未来可能有BOM ScrapRate就额外叠加放大量。

---

## 22. 2号位正式交给1号位的九类输入

1. Run边界；
2. 逻辑生产Demand及优先关系；
3. NetRequiredQty / PlannedProcessQty；
4. Allocation血缘；
5. Routing候选图；
6. Resource Eligibility与Calendar；
7. 完整物料Quantity-Time约束；
8. 冻结Scheduling Strategy Pack；
9. Firm/Frozen/Execution等不可移动事实；
10. 跨版本连续性上下文：ContinuationKey、ContinuationQty、NoSplitMerge或等价语义、工序级PreferredResource（可空）；
11. SH时间上下文（适用时）：SourceFactoryId、源厂/目标侧依赖、冻结跨厂LT及整单SourceReadyTime语义。

**禁止**只给1号位一个“已生成Task列表”。

---

# 第五部分：1号位90天有限产能求解

## 23. 一套90天正式Solver

V1滚动90天内所有活跃Demand进入同一套有限产能语义。

不再采用：

- 近7天详细排；
- 8～90天ROUGH_CUT/LeadTime粗排；
- 两套结果拼接。

远近只允许：

> 搜索深度、候选数量、局部优化轮数不同。

资源占用语义必须一致。

当前明确简化的Stage可以继续使用Stage LeadTime，但它是**Stage业务简化**，不是“远期整体变成粗排”。

---

## 24. FinalTask由1号位形成

1号位根据逻辑生产需求决定：

- 需要哪些Operation；
- 是否必须拆批；
- 是否优化拆批；
- 是否允许多Demand合并；
- 实际Resource；
- Start/End；
- Task-to-Task物理依赖。

### 24.1 多Demand可合并

如果工艺、设备资格、交期、Lock/Firm/Frozen允许：

> 两个或多个Demand份额可合并成一个FinalTask。

因此真实Demand血缘必须走：

> `Allocation ↔ FinalTask`数量映射。

Task.OrderId如果为了兼容保留，只能是非权威主归属，不能代表全部Demand关系。

---

## 25. Routing和Resource选择

2号位告诉1号位“哪些路线/设备合法”。

1号位根据：

- 负荷；
- 日历；
- Setup；
- 瓶颈；
- 交期；

决定实际Resource。

如果当前业务只有默认Route，V1不提前建设复杂多路径全局搜索。

---

## 26. 动态瓶颈

V1默认：

> Load/Capacity动态识别 + 少量人工策略覆盖。

可以识别一个主瓶颈，再必要时补第二关键瓶颈。

不建设数学全局多瓶颈最优平台。

---

## 27. 混合正排与倒排

正排适合求最早可行时间；倒排适合围绕交期减少过早生产。

V1采用同一Solver内的混合策略：

- 瓶颈可围绕交期倒排；
- 上游受物料约束正排；
- 局部冲突前后修复。

正排和倒排不是两套Solver。

---

## 28. 五阶段求解结构

### A：问题与硬约束构建

- Routing；
- Eligibility；
- Calendar；
- Material AvailableTime；
- Firm/Frozen/Lock；
- 强制物理拆批；
- 初始瓶颈。

### B：形成初始有限产能计划

联合考虑：

- 业务优先；
- 瓶颈锚定；
- 资源；
- 批次；
- 正/倒方向；
- Setup；
- 时间槽。

### C：可行性与延期诊断

检查：

- 资源冲突；
- 物料时间；
- 工艺前后依赖；
- Lock/Firm/Frozen；
- 客户交期。

### D：有界局部修复

可尝试：

- 替代Resource；
- 少量优化性拆批；
- 前移/后移；
- Setup换序；
- 邻域修复。

必须有轮次和时间预算。

### E：简单Gap Compaction与最终评价

输出：

- FinalTask；
- AllocationTaskShare；
- Task-to-Task Dependency；
- ExplanationFactDraft；
- KPI。

---

## 29. 排程目标层级

### 第一层：硬约束

不可违反：

- Resource互斥；
- Routing Dependency；
- Material AvailableTime；
- Eligibility；
- Firm/Frozen；
- Demand Protection/不可逆事实；
- 数量闭合。

### 第二层：履约优先关系

优先保护：

- Demand Protection；
- 高优先客户Demand；
- 其它冻结业务优先关系。

### 第三层：客户承诺确保率

如果存在达到目标履约率的可行方案，不能为了利用率/Setup选择更差履约方案。

如果物理上无法达到目标：

> 返回最佳可行计划和差距，不把它当Solver失败。

### 第四层：次级优化

包括：

- 总延期；
- Lead Time；
- WIP；
- Setup；
- 利用率；
- 计划稳定性。

---

# 第六部分：排程后处理、Explanation和发布

## 30. 客户订单完成时间

V1客户订单生产排程终点：

> 最终生产/装配完成并进入目标ZP/BP。

客户外部运输不进入有限产能主链。

订单由多个份额共同完成时：

> 完成时间取满足全部必要份额后的最晚完成时间。

---

## 31. 采购参考需用日期

采购参考需用日期应来自真正消费采购件的下游Task计划时间，而不是只按客户交期减固定LT。

例如最终装配计划9月20日开始：

> 采购件参考需用日期应围绕9月20日计算。

V1仍不自动生成正式采购单。

---

## 32. ExplanationFactDraft

Explanation由1号位在求解过程中原生产出。

至少表达：

- TargetTime；
- ActualTime；
- ConstraintType；
- Resource/Material/BlockingTask；
- ImpactHours；
- Evidence；
- 直接原因与必要的上游根因。

### 32.1 DUE_DATE_RISK不是根因

DUE_DATE_RISK只是结果标志。

真正原因应落到：

- MATERIAL_SHORTAGE；
- RESOURCE_CAPACITY_WAIT；
- PRECEDENCE_WAIT；
- FROZEN_ZONE_LOCK；
- LOGISTICS_DELAY；
- EQUIPMENT_BREAKDOWN_RISK；
- 等现有ReasonCode。

### 32.2 延迟原因不能机械相加

多个等待时间不能直接加总成最终延期小时。

必须沿最终关键路径识别真正决定Start/End的约束。

5号位数据Issue和1号位排程Explanation是两类不同对象。

### 32.3 ReasonCode权威字典保持15项

V1正式ReasonCode仅允许：

1. `RESOURCE_CAPACITY_WAIT`；
2. `MATERIAL_SHORTAGE`；
3. `PRECEDENCE_WAIT`；
4. `FROZEN_ZONE_LOCK`；
5. `ROUTING_FALLBACK`；
6. `STAGE_LEADTIME_FALLBACK`；
7. `BOM_DEGRADE`；
8. `CROSS_ORG_HANDOFF`；
9. `PRIORITY_LOWER_THAN_OTHERS`；
10. `DUE_DATE_RISK`；
11. `LOGISTICS_DELAY`；
12. `PRIORITY_INHERITANCE`；
13. `CROSS_DOMAIN_VERSION_MISMATCH_RISK`；
14. `MANUAL_COMPLETED_SHORT`；
15. `EQUIPMENT_BREAKDOWN_RISK`。

不得临时发明`DUE_DATE_TIGHT`、`UPSTREAM_DELAY`等新码。跨Domain运行失败/版本不一致使用`CROSS_DOMAIN_VERSION_MISMATCH_RISK`。

---

## 33. 统一持久化

2号位在同一Domain事务边界内统一持久化：

- FinalTask；
- Allocation/必要逻辑Ledger；
- PeggingSupplyAllocation；
- AllocationTaskShare；
- Task-to-Task关系；
- ScheduleExplanationFact；
- PlanVersion结果状态。

`PeggingSupplyAllocation`（PSA）只作为库存、采购在途/未结PO、VMI、采购已到厂未入库等**非Task供给Allocation的内部结果镜像/查询边界**，不是第二套供需真相源，不拥有独立生命周期。SH跨厂在途不写PSA，PI级`INTERPLANT_IN_TRANSIT`属于PI Position，也不写PSA。运行时数量真相仍来自本PlanVersion的Demand/Supply余额与Allocation。

**红线**：PSA不得事务内写一次、事务外再写第二次；不得先DELETE后在另一个事务INSERT造成中间空窗。

---

## 34. 夜间Domain发布

### 34.1 成功Domain

同Domain旧ACTIVE→ARCHIVED，本次版本→ACTIVE，在可控事务边界内完成。

### 34.2 上游失败门禁

例如：

`B → A`

若B本次FAILED：

- B保留旧ACTIVE；
- A本次不得发布新ACTIVE；
- A继续保留上一ACTIVE；
- 与B无依赖的其它Domain可正常发布。

### 34.3 ScheduleRun终态

- 全部预期Domain成功：COMPLETED；
- 部分成功、部分失败/被依赖阻断：PARTIAL_SUCCESS；
- 零成功或运行级致命错误：FAILED。

这不是ALL_OR_NOTHING，只保证真实依赖链一致性。

---

## 35. 人工失败恢复

FAILED的ScheduleRun/PlanVersion永久保留，不能改回RUNNING/BUILDING。

人工点击“重新计算”时：

1. 新建ScheduleRun；
2. 从失败Domain出发读取Domain_Dependency；
3. 自动补齐仍失败的必要上游；
4. 自动补齐此前因失败被阻断的下游；
5. 只重算必要依赖链；
6. 仍按拓扑执行。

示例：

`C → B → A`

凌晨：C成功、B失败、A未发布。

修复B后：

> 新Run只算B→A；C继续复用本次已成功ACTIVE。

V1不建设独立Retry Workflow平台。

---

# 第七部分：Firm/Frozen与MES下发

## 36. Firm/Frozen不是Pegging Lock

Firm/Frozen解决：

> 已安排Task能不能移动。

Demand Protection/Strict Binding解决：

> Supply份额能不能换Demand。

两者不能混成同一个锁。

---

## 37. 跨版本Firm/Frozen与真实执行连续性

V1不建设FrozenZoneSnapshot正式平台。

夜间新版本生成新TaskId时，2号位从上一ACTIVE读取仍有效的Firm/Frozen事实，转换为本次1号位的不可移动锚点约束。

同时必须把普通“上一版本关系稳定性”与真实MES执行连续性分开：

```text
当前Pegging先形成LogicalProductionDemand Q
↓
5号位标准化IN_PROGRESS执行事实
↓
2号位逐MES工单形成Continuation Slice E_i
↓
Continuation + Free
↓
1号位Solver
```

普通未锁Allocation仍可重新竞争；但真实IN_PROGRESS执行份额仍要继续占未来资源能力。

多MES工单只能在总量闭合时合计E，进入Solver必须逐MES工单保留独立ContinuationKey。

MES真实执行事实继续以执行事实/Execution Constraint承接。

---

## 38. MES下发资格

只有满足以下条件的Task才能下发：

- 来自正式ACTIVE计划；
- 有合法生产执行身份；
- 非Candidate；
- 非UNLOCATED保守占位；
- 非无PI虚拟占位；
- 满足Firm/Frozen/发布窗口等业务要求；
- 未被取消。

Candidate即使排得再好，也不能直接下MES。

---

## 39. MES五态

保持现有五态：

- 0 待开工；
- 1 开工中；
- 2 完工报工；
- 3 未完工报工；
- 4 未完工报工已完结（手动完工）。

APS Task状态维持：

- PLANNED；
- RELEASED；
- IN_PROGRESS；
- COMPLETED；
- CANCELLED。

V1不新增PAUSE/RESUME正式闭环。

---

## 40. 设备故障

设备故障只形成：

- Resource不可用事实；
- 影响评估；
- Explanation；
- RescheduleRecommendation；
- 看板告警。

PMC决定是否发起单Domain重排。

系统不得自动：

- 把Task改PAUSED；
- 自动恢复Task；
- 自动创建ScheduleRun；
- 自动激活Candidate。

---

# 第八部分：白天Candidate与插单完整流程

## 41. 白天业务场景与Candidate统一入口

### 41.1 共同运行链

所有白天业务从4号位发起，治理链统一：

```text
4号位
→ 采集一个业务场景 + 一个或多个同类型对象
→ 3号位校验RunType/Purpose/BusinessTriggerType/PriorityMode
→ 校验Business Scope与当前ACTIVE Base
→ 2号位解析Candidate运行输入、重新Pegging/组装变化Seed
→ 1号位唯一有限产能Solver
→ Candidate PlanVersion
→ 5号位提供普通Gantt/Schedule查询
→ 4号位展示Candidate与影响
→ 用户确认/放弃
→ 3号位做最终激活门禁
```

Candidate仍严格单Domain。

### 41.2 业务场景矩阵

| 场景 | RunType + Purpose | PriorityMode |
|---|---|---|
| 新订单正常承诺评估 | INSERT_ORDER_WHATIF + CTP | NORMAL |
| 新订单急插影响分析 | INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS | EXPEDITE |
| 按CTP结果正式插入 | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | NORMAL |
| 按急插结果正式插入 | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | EXPEDITE |
| 已有订单提前——正常 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 已有订单提前——急单 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | EXPEDITE |
| 甘特多Task调整 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 多设备故障后重排 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 多资源Calendar变化后重排 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 整Domain人工重排 | MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT | 既有正式优先规则 |

不再提供“其它明确对象局部调整”。

### 41.3 一个Run只允许一个业务发起场景

允许：

```text
已有订单提前
→ OrderA + TargetDateA
→ OrderB + TargetDateB
→ 一个LOCAL Run
```

不允许：

```text
订单提前 + 甘特拖拽 + 设备故障
→ 一个LOCAL Run
```

同一Run的多个对象必须归属于同一个目标Domain；否则拆成多个单Domain Run。

### 41.4 Candidate Base ACTIVE

创建Run时：

```text
BasePlanVersionId = 当前目标Domain唯一ACTIVE
```

禁止使用Candidate作为Base。

允许多个Candidate B/C/D同时基于ACTIVE A并存。

如果B先被确认并成为新ACTIVE：

```text
A → ARCHIVED
B → ACTIVE
```

则仍以A为Base的C/D不得再直接激活，必须重新基于B和最新真实事实生成。

### 41.5 Candidate运行输入Schema v2

继续复用：

```text
ScheduleRun.ScopeJson NVARCHAR(MAX)
```

不新增CandidateInput表。

新建白天Run使用版本化Schema v2，至少表达：

- BusinessTriggerType；
- PriorityMode；
- 多订单及各自ManualTargetDueDate；
- 多Task及各自TargetTime；
- 多ChangedResourceIds；
- 原11字段中仍适用的范围/授权字段。

`BasePlanVersionId`和`ExpectedDomainKeysJson`继续使用ScheduleRun独立字段，不重复写回ScopeJson。

### 41.6 Seed与当前真实事实

本次选择对象只决定影响传播起点。

例如本次从MC01/MC02故障发起，但MC05此前正式修改的Calendar仍必须进入当前Solver上下文。

同理：

- 当前设备可用性；
- Eligibility；
- Routing；
- Setup；
- Material AvailableTime；
- 资源占用；
- Firm/Frozen；
- 外Domain ACTIVE阻挡；

都使用当前真实值。

但未被本次用户选择的其它新订单，不自动成为本次业务场景对象。

### 41.7 场景特有边界

**CTP**：NORMAL；WHATIF不可激活。正式采用时重新基于当前ACTIVE创建LOCAL + INSERT_RESCHEDULE + NORMAL。

**急插影响分析**：EXPEDITE只作用选中订单及派生链；正式采用时重新创建LOCAL + INSERT_RESCHEDULE + EXPEDITE。

**已有订单提前**：支持多订单不同目标日期；人工计划目标日期不修改正式DueDate。

**甘特拖拽**：多Task、软目标、固定NORMAL；不得直接UPDATE正式Task。

**设备故障**：设备必须先成为正式不可用事实；可一次多设备。

**Calendar变化**：Calendar必须先正式修改；V1不做Candidate专属假设Calendar。

**LOCAL兜底**：影响扩大到当前Domain全部可移动Task时，RunType仍为LOCAL。

**MANUAL**：只有用户从一开始明确选择整个当前Domain重排时才使用。



## 42. 实时Order与BOM

白天只对新增/变化订单做必要实时处理：

1. Candidate独立Order快照；
2. 判断BOM切片能否合法复用；
3. 不可复用则创建Realtime RequestDetail；
4. ODS完成Workset/StageDetail/CrossFactoryEdge；
5. READY后2号位搬运到APS RAW；
6. 生成OrderBomRequestLink。

无需把整个夜间ODS链重新跑一遍。

---

## 43. Candidate RemainingSupply

旧语义：

`Base Supply - Base全部Allocation`

**废止。**

正式语义：

> `当前有效物理Supply - 已真实消耗 - Strict Binding - Demand Protection - 不可逆份额 - 已失效份额`

上一ACTIVE中的普通未锁Allocation可以释放回竞争池。

当前Domain可移动Demand与新订单一起重新Pegging。

---

## 44. Candidate变化种子

2号位比较Base与Candidate Pegging，只把真正改变的逻辑生产块交给1号位，例如：

- 新增需求；
- 减少需求；
- 数量变化；
- Supply AvailableTime变化导致的生产变化。

2号位不计算完整影响图。

---

## 45. 白天共享资源边界

如果当前Domain与其它Domain共享设备：

> 其它Domain当前ACTIVE占用作为不可移动Resource Blocker。

Candidate只能调整自己Domain。

即使其它Domain某Task优先级较低，也不允许白天跨域挤动。

这会略偏保守，但保证V1：

- Candidate严格单Domain；
- 不需要跨Domain资源借用；
- 不需要MultiDomain Impact传播。

---

## 46. 1号位动态影响传播

不按：

- 固定产品族；
- 固定7天/14天；
- 固定订单数；

硬截断。

主要沿：

1. 工艺前后关系；
2. 物料Quantity-Time关系；
3. Resource时间轴；
4. Setup邻居；

传播。

### 46.1 Dirty Queue

1. 变化种子入队；
2. 尝试重新安排；
3. 如果Resource/Start/End/Quantity无变化，则该支停止；
4. 有变化才把真实受影响的上下游/资源邻居/物料消费者入队；
5. 直到队列为空。

### 46.2 最小扰动

优先：

1. 保留原Resource；
2. 保留原附近时间；
3. 找不移动其它Task的空档；
4. 少量优选替代Resource；
5. 最后才挤动低优先Task、拆批、换序。

### 46.3 MaxImpactedOrders

只能作为：

- 警戒阈值；
- 页面提示；
- 人工确认阈值。

**不能到达数值就停止计算真实影响。**

### 46.4 PlanHorizonStart/End

代表初始请求/调整范围，不是物理影响硬边界。

真实影响可以传播到范围外；如果传播过大则进入单Domain完整重排。

---

## 47. 局部重排兜底

当：

- 影响范围持续扩大；
- 局部修复不稳定；
- 时间预算逼近；
- 主瓶颈大面积改变；

自动升级为：

> 当前单Domain全部可移动范围的完整有限产能重排。

仍使用同一Solver，不建设第二LocalSolver。

仍固定：

- 已执行；
- Firm/Frozen；
- Demand Protection；
- 不可逆事实；
- 其它Domain共享资源占用阻挡块。

---

## 48. Candidate工程默认值

真实压测前建议：

| 参数 | 初始建议 |
|---|---:|
| 正常Candidate目标 | 60秒内 |
| 软时间预算 | 90秒 |
| 局部模式硬预算 | 180秒 |
| 影响Task比例阈值 | 30% |
| 单Task局部修复上限 | 5次 |
| 全局局部修复轮数 | 10轮 |
| 单Operation替代资源初筛 | 5个 |
| 优化性拆批候选 | 最多3种 |
| 固定影响天数 | 不设 |

这些是1号位Solver技术参数，不是PMC业务规则。

---

# 第九部分：白天跨Domain CTP

## 49. 多个单Domain WHATIF串行

如果新订单属于A，但BOM存在：

`C → B → A`

后台依次：

1. C单Domain WHATIF；
2. 把C的Quantity-Time切片传给B；
3. B单Domain WHATIF；
4. 把B的Quantity-Time切片传给A；
5. A单Domain WHATIF；
6. 汇总一个客户CTP与影响结果。

**一个Candidate PlanVersion仍然只属于一个Domain。**

不建设：

- MultiDomainCandidate；
- CandidateGroup状态机；
- 多域原子版本；
- 全局仿真平台。

---

## 50. 跨Domain Quantity-Time必须分段

例如上游B产生：

- 40件：8月15日可用；
- 60件：8月17日可用。

下游A必须收到两个切片。

不能压成：

> 100件，8月17日可用。

否则会丢失下游提前生产40件的机会。

跨Domain转运同样使用真实工厂/Stage LeadTime，不使用统一2天权威值。

---

## 51. CTP输出

一次插单评估至少返回：

- 是否按目标交期完成；
- 最早可承诺完成时间；
- 是否依赖ESTIMATED采购占位；
- 影响多少已有订单；
- 哪些仍按期；
- 哪些延期、最大影响；
- 是否触碰Demand Protection；
- 主要瓶颈；
- 主要Explanation。

CTP与INSERT_IMPACT_ANALYSIS可以保留不同Purpose，但**同一次WHATIF应同时生成交期和影响结果，不重复跑两次Solver。**

---

# 第十部分：Candidate采用与人工干预

## 52. WHATIF结果采用与Candidate激活

CTP / INSERT_IMPACT_ANALYSIS只用于分析，原WHATIF Candidate不能直接激活。

如果用户决定正式采用：

```text
重新读取当前ACTIVE
+ 重新读取当前最新真实事实
↓
创建LOCAL_RESCHEDULE + INSERT_RESCHEDULE
↓
NORMAL或EXPEDITE按来源明确
↓
新的可激活Candidate
```

LOCAL/MANUAL Candidate激活前，3号位必须再次确认：

```text
BasePlanVersionId = 当前目标Domain现行ACTIVE
```

如果Base已过期：

> 拒绝激活并要求重跑。

V1只保留最小人工确认和审计，不建设完整OA/BPM审批平台。

---

## 53. 人工调整统一通过Candidate

甘特、已有订单提前、设备故障、Calendar变化都不能直接UPDATE正式Task或正式DueDate。

统一流程：

1. 4号位采集一个明确业务场景及一个或多个同类型对象；
2. 3号位校验业务场景、PriorityMode、Business Scope和Base ACTIVE；
3. 2号位形成变化Seed和允许移动集合；
4. 1号位验证全部硬约束并求解；
5. 页面展示真实Candidate甘特图、影响和Explanation；
6. 用户确认后才允许激活。

甘特TargetTime和订单ManualTargetDueDate都是本次Candidate输入，不是永久主数据。

人工调整不能绕过：

- Resource冲突；
- Material AvailableTime；
- Routing；
- Eligibility；
- Firm/Frozen；
- Demand Protection；
- 执行事实；
- Business Scope；
- 其它Domain ACTIVE共享资源阻挡。

---

## 54. 任务手动锁定与Demand Protection不是一回事

手工固定Task是排程不可移动约束。

Demand Protection是供给份额保护。

如果用户只是把Task固定在某时间，不应自动把对应PI/PO全部锁给该订单。

---

# 第十一部分：MES执行反馈与次日重算

## 55. MES事件接收与幂等

MES事件进入Staging/MQ消费后：

- 使用消息唯一键/业务键去重；
- MES不要求直接携带APS TaskId；
- 通过MES TaskNo/WorkOrderNo等映射到APS Task；
- EventType与Task.Status严格区分。

---

## 56. MES执行事实进入次日FULL

MES报工、PI Received、XC、跨厂在途等事实在下一次数据截止时进入新快照。

新FULL：

1. 重新计算PI Position；
2. 固定真实不可逆执行；
3. 普通Allocation按新优先规则重新竞争；
4. 重新形成逻辑生产需求；
5. 生成新的FinalTask；
6. 新PlanVersion发布。

普通Demand↔PI关系不因为“昨天这样分配”而获得稳定性偏好。

---

# 第十二部分：数据同步与变化处理

## 57. ERP订单增量

正式路径保持：

`ERP/ODS视图 → ERP_Order_Staging → sp_ValidateAndPromoteOrders → Order_Canonical`

夜间只取OPEN进入版本快照。

白天新订单如果需要实时评估，走Candidate独立Order快照与Realtime BOM链。

---

## 58. 主数据变化

主数据变化本身不自动触发排程。

系统可以：

- 记录变化事实；
- 产生ImpactAssessment/Recommendation；
- 提醒PMC。

由PMC决定是否发起LOCAL/MANUAL RESCHEDULE。

不恢复“变化即自动重排”。

---

# 第十三部分：监控、KPI和异常

## 59. 业务异常与系统错误分开

### 59.1 可降级业务数据异常

例如：

- 单个PI Position无法定位；
- 中间Stage缺失；
- MES累计值矛盾但可由强事实修正。

处理：

> 保守修正/UNLOCATED + Issue，Domain继续。

### 59.2 必须失败的系统闭合错误

例如：

- Supply重复消费；
- Allocation越界；
- FinalTask/TaskShare数量不闭合；
- 选择非法Resource；
- 事务半成功；
- 跨版本写错关系。

处理：

> 当前Domain失败，不发布。

### 59.3 “按期做不到”不是Solver失败

如果真实能力无法按客户交期：

> Solver仍应返回最佳可行日期和原因。

这属于业务结果。

---

## 60. KPI

V1核心履约KPI建议：

> 客户订单整单按时进入目标ZP/BP比例。

可同时展示数量按期率，但不能混为一个指标。

周、月、季度负荷和交付统计都来自同一90天计划聚合，不另外运行周/月/季度Solver。

年度详细排程不进入V1。

---

## 61. 性能目标

V1统一目标：

- 约10万FinalTask规模；
- 夜间FULL约15分钟左右收敛；
- 正常Candidate目标60秒内；
- 较大Candidate可放宽到2分钟左右；
- 超过约3分钟视为异常或触发单Domain全排兜底评估。

性能优化优先：

- 全内存核心计算；
- Resource Timeline索引；
- Demand/Supply高效索引；
- 减少无效候选；
- 批量持久化；
- 避免逐Task数据库I/O。

不以建设第二套远期粗排Solver解决性能问题。

---

# 第十四部分：2号位现有代码的保护与整改边界

## 62. 必须保护

- SchedulingOrchestrator；
- ScheduleRun / PlanVersion框架；
- ScheduleContext装载；
- SupplyPool.RemainingQty原型；
- Stage进度装载；
- `IFiniteCapacityScheduler.SolveAsync`接口方向；
- DomainSolve DTO；
- 事务模板；
- Realtime BOM链。

## 63. 必须退出正式主链

- 2号位提前FinalTask；
- DefaultBatchSplitter作为预拆Task主链；
- 外层第二次旧FiniteCapacitySolver；
- PassThrough作为正式生产Solver；
- ShippingTask有限物流主链；
- FrozenZoneSnapshot正式生成链；
- VirtualInventoryBalance持久状态；
- Base ACTIVE全部Allocation扣死；
- PSA事务外重复写；
- `InventoryBalance.AllocatedQty`跨版本永久污染；
- Pegging数量错误Warning后继续发布。

## 64. 必须补齐

1. DemandBalance；
2. SupplyBalance原子扣减；
3. Allocation/逻辑Ledger；
4. Strict Binding / Demand Protection；
5. PI Position接入；
6. Candidate普通Supply重新竞争；
7. PlannedProcessQty；
8. AllocationTaskShare多对多；
9. 单一真实1号位Solver；
10. Candidate动态影响传播；
11. 跨Domain Quantity-Time分段；
12. 失败Domain新ScheduleRun恢复。

总体原则：

> **保留外壳，替换/补齐核心。**

---

# 第十五部分：端到端典型场景走查

## 65. 正常客户订单

客户原始120，ERP成品库存已满足20，APS收到100。

BOM：

- 自制C100；
- 采购P100。

Pegging：

- 不再扣顶层成品库存；
- C已有PI80；
- C缺20形成生产需求；
- P库存60；
- PO40，AvailableTime=8月15日。

计划良率使MACH PlannedProcessQty可能>100。

1号位排完后：

- 采购P AvailableTime限制装配；
- 最终客户完成时间取全部必要份额完成后最晚值。

---

## 66. 多订单竞争与白天插单

原ACTIVE：

- A受Demand Protection，需要C60；
- B普通需求C100；
- PI-C01剩余140。

原分配：

- A60保护；
- B80；
- B缺20新生产。

插入急单U70。

Candidate重新Pegging：

- A60不动；
- B普通80释放；
- U获得70；
- B仅10；
- B新增缺口90。

2号位只把U新增/B数量变化等种子给1号位。

1号位局部传播，如果影响太大再单Domain全排。

---

## 67. Stage Handoff跨厂

B厂需要C500：

- B M=100；
- PI-C01=300；
- PI内部B等待100、在途120、XC80；
- 新生产100。

运输只形成AvailableTime，不生成ShippingTask。

---

## 68. Inter-factory Order跨厂

B厂普通Demand需要D300：

- BS/KS普通库存=50；
- SH-001数量=250，作为一份单一Supply承接剩余250。

此后不再把SH拆成“在途/Received/未生产”三个数量桶，而只做整单判断。

### 情况A：SH-001已进入跨厂履行

```text
OPEN + ERP_InterplantInTransit_View存在SH-001
```

处理：

```text
整张SH已进入跨厂履行
→ 不生成第二层源厂履约Demand
```

即使ERP事实同时显示：

```text
TransitQty = 80
ReceivedQty = 70
```

也**禁止**推导：

```text
250 - 80 - 70 = 100未生产
```

这些数量只可用于物流追溯/数据质量，不形成SH内部Supply切片。

### 情况B：SH-001尚未进入跨厂履行

```text
OPEN + ERP_InterplantInTransit_View不存在SH-001
```

处理：

```text
生成一条完整源厂履约Demand
RequiredQty = Order.Quantity = 250
SourceFactoryId = Order.SourceFactoryId
```

源厂继续：

```text
PI → PI Position → 最终缺口新增生产
```

例如已有PI/PI Position能够承接150，则最终新增生产缺口才是100。

> 这个100来自PI承接后的缺口，不来自Transit/Received数量倒算。

---

## 69. PI异常

PI剩余200，但XC150+在途120，确认不是重复。

事实总和270>200。

处理：

- 整体200降级UNLOCATED；
- 记录Issue；
- 2号位仍可把200作为PI Supply；
- 1号位从最早可信Stage保守排；
- 对应Task不得下MES。

---

## 70. 无正式采购承诺

装配需要采购件P100：

- 库存0；
- PO0；
- VMI0；
- DefaultLT=20天。

内存创建100件Planning-only Placeholder。

90天排程不断链，但CTP必须提示：

> “日期为估算，采购尚未承诺。”

---

## 71. 跨Domain CTP

订单属于A，BOM依赖C→B→A。

后台：

- C WHATIF；
- C Quantity-Time给B；
- B WHATIF；
- B Quantity-Time给A；
- A WHATIF；
- 输出一个最终CTP。

如果C瓶颈满负荷，即使A装配很空，最终交期仍由C约束。

---

## 72. Domain失败恢复

依赖：

`C → B → A`

夜间：

- C成功；
- B失败；
- A本次不发布；
- D/E无关Domain正常ACTIVE；
- ScheduleRun=PARTIAL_SUCCESS。

修复B后：

- 原FAILED历史不改；
- 新建ScheduleRun；
- 自动重算B→A；
- C不重复跑。

---

# 第十六部分：V1明确不做

为防止后续技术文档和代码再次膨胀，以下内容不进入V1正式主链：

1. 有限物流Task体系；
2. ShippingTask作为有限产能Task；
3. MultiDomain Candidate；
4. Candidate Group状态机；
5. 跨Domain资源配额/借用平台；
6. VirtualInventoryBalance持久化平台；
7. FrozenZoneSnapshot正式平台；
8. 第二套90天远期粗排Solver；
9. 周/月/季度独立Solver；
10. 动态脚本/DSL/插件市场；
11. 5号位逐Demand运行时插件裁决；
12. 无限多Route全局搜索；
13. 无限拆批组合；
14. 全局数学多瓶颈最优平台；
15. 完整SolverTrace数据库；
16. 为每种延期现象扩充独立ReasonCode；
17. 普通Allocation跨版本稳定性偏好；
18. 完整OA审批作为所有Candidate激活硬前置；
19. 自动设备故障重排；
20. PAUSE/RESUME Task正式状态闭环。

---

# 第十七部分：最终验收清单

## 73. 业务闭环

必须验证：

- SALES_ORDER不二次扣成品库存；
- PI总量不被MES/XC/在途重复放大；
- Demand排序第一命中；
- Demand/Supply双边闭合；
- PI先选号再消费Position；
- PI自消费被阻止；
- Demand Protection份额可正确传播和释放；
- 两类跨厂逻辑不混用；
- 采购/VMI/Placeholder正确参与；
- 计划良率数量正确。

## 74. 时间与资源闭环

必须验证：

- 90天同一资源语义；
- Resource不双占；
- 跨Domain共享资源夜间真实互斥；
- Material AvailableTime不被违反；
- Setup正确；
- 混合正/倒排可行；
- 单Domain Candidate不挤其它Domain ACTIVE。

## 75. 版本与失败闭环

必须验证：

- 一个FULL ScheduleRun对应多Domain PlanVersion；
- 无关Domain可独立发布；
- 上游FAILED会阻断依赖下游本次发布；
- PARTIAL_SUCCESS正确；
- FAILED人工恢复创建新ScheduleRun；
- 原失败历史不可改写。

## 76. Candidate闭环

必须验证：

- Base ACTIVE只读；
- 普通未锁Allocation可重新竞争；
- MaxImpactedOrders不截断真实影响；
- 局部传播能自然收敛；
- 影响过大能升级单Domain全排；
- CTP和Impact来自同一WHATIF结果；
- 跨Domain插单多个单Domain Candidate串行。

## 77. MES闭环

必须验证：

- 只有ACTIVE合法Task下发；
- Candidate不下发；
- 无PI占位不下发；
- UNLOCATED不下发；
- MES五态映射正确；
- 次日事实能形成新PI Position和新PlanVersion。

## 78. 过度设计回流检查

每次后续修订都要明确检查：

- ShippingTask是否又进入Solver；
- MultiDomain Candidate是否重新出现；
- Pipeline是否又被降成V1空；
- FrozenZoneSnapshot/VirtualInventory是否被重新建设；
- 远期ROUGH_CUT是否被重新定义为正式V1模式；
- PriorityScore是否重新成为排序真相；
- 5号位是否重新成为运行时中央插件服务；
- 2号位是否重新提前生成FinalTask。

---

# 附录A：当前保护区

以下技术骨架未被业务冻结推翻，后续不得因为“减法”而随意删除：

- ScheduleRun + PlanVersion；
- ExpectedDomainKeysJson；
- PARTIAL_SUCCESS；
- 白天Candidate严格单Domain；
- OrderCanonical→Order版本快照；
- BOM Workset / RequestDetail / Realtime / RAW；
- OrderBomRequestLink；
- RoutingOperation / RoutingDependency / OperationResourceEligibility；
- MaterialStageDeptContext；
- ProcessCodeDict.ERPProperty真实来源；
- MES五态0～4；
- RuleSet / ParameterSet / StrategyProfile六表；
- 2号位现有SchedulingOrchestrator、ScheduleContext、SupplyPool、IFiniteCapacityScheduler接口与事务框架。

---

# 附录B：本文件与六份权威文档的关系

本文件只负责回答：

> **APS V1到底按什么顺序跑、每一步谁负责、数量如何交接、时间如何交接、失败如何恢复、版本如何发布。**

后续文件分别承接：

- 《各类基础数据分层承接与演变总表》：每类数据从源到ODS到APS到运行时如何承接；
- 《数据架构与防腐层设计方案》：Socket/Plug/Loader、快照、上下文、物理数据流；
- 《集成接口设计》：0～5号位接口契约；
- 《数据库字段说明》：字段业务语义；
- 《数据库表结构设计DDL》：最终物理结构。

如果后五份文档与本走查、三份冻结业务出现冲突：

> **先修改后五份技术文档，不重新设计业务。**

---

# 结语

APS V1的正式主链可归纳为：

```text
凌晨数据与规则快照
    ↓
ScheduleRun + 各Domain PlanVersion
    ↓
Domain_Dependency拓扑
    ↓
2号位分层Pegging与数量闭合
    ↓
5号位PI Position等复杂事实
    ↓
2号位计划良率反算 + 逻辑生产需求 + Quantity-Time
    ↓
1号位90天统一有限产能Solver
    ↓
FinalTask + AllocationTaskShare + Task依赖 + Explanation
    ↓
2号位统一事务持久化
    ↓
依赖链一致的Domain发布
    ↓
ACTIVE合法Task下发MES
    ↓
MES执行事实进入次日重新计算
```

白天插单则是：

```text
新订单/变化
    ↓
单Domain Candidate
    ↓
释放普通未锁Allocation重新Pegging
    ↓
2号位变化种子
    ↓
1号位动态影响传播与局部修复
    ↓
必要时单Domain完整重排
    ↓
CTP + 影响 + 原因
    ↓
人工决定是否正式采用
```

跨Domain插单仍保持：

```text
C单Domain WHATIF → B单Domain WHATIF → A单Domain WHATIF
```

而不是MultiDomain Candidate。

**母版历史状态**：V3.17/V3.18/V3.19/V3.20均作为历史冻结演进保留；**当前正式版本为V3.21**。历史段落只用于追溯，不得覆盖V3.21当前流程。

---

# 2026-09-01 Domain专项冻结增量（本版新增，冲突时仅此专项口径覆盖历史Domain表述）

# APS 核心排产全流程走查（完整版）V3.18
## Domain专项冻结对齐版

版本：V3.18  
日期：2026-09-01（北京时间）

---

## 0. 修订原则

本版基于V3.17采用增量修订。

保持冻结：

- 需求统一模型；
- SupplyFact；
- Pegging；
- PI Position；
- Demand Protection；
- Manual ETA；
- AvailableTime；
- Planning-only Purchase Placeholder；
- 有限产能排程；
- Candidate；
- MES执行资格；
- ScheduleRun / PlanVersion。

仅补充Domain运行边界。

---

## 1. 夜间FULL总体流程

```
ScheduleRun创建
↓
加载当前有效DomainDefinition
↓
冻结本次Run有效Domain集合
↓
需求/供给真实归域
↓
建立Domain执行上下文
↓
读取Domain_Dependency
↓
拓扑排序
↓
逐Domain执行
↓
生成各Domain PlanVersion
↓
汇总ScheduleRun结果
↓
发布
```

---

## 2. Domain冻结

ScheduleRun开始时：

1. 读取当前有效DomainDefinition；
2. 形成当前Run计算Domain集合；
3. 保存ExpectedDomainKeysJson；
4. 后续执行只使用冻结集合。

运行期间修改DomainDefinition：

- 不影响当前Run；
- 下一次Run生效。

---

## 3. 需求与供给归域

流程：

```
Order / Demand / Supply事实
↓
DomainDefinition匹配
↓
生成DomainKey
↓
进入对应Domain上下文
```

V1支持：

- FAMILY；
- FACTORY_FAMILY。

Domain不是ProductFamily别名。

---

## 4. Domain内计算边界

每个Domain：

```
Domain Context
↓
需求准备
↓
供给准备
↓
Pegging
↓
LogicalProductionDemand
↓
有限产能求解
↓
验证
↓
PlanVersion保存
```

Domain定义计算边界，不改变业务算法。

---

## 5. Pegging与有限产能

Pegging保持：

- Demand/Supply匹配；
- Allocation；
- PI承接；
- Demand Protection；
- Quantity-Time。

有限产能保持：

- 单一90天Solver；
- FinalTask生成；
- 多目标优化；
- Resource约束。

1号位只消费单Domain求解上下文，不负责归域。

---

## 6. ScheduleRun与PlanVersion

保持：

```
一个ScheduleRun
↓
多个Domain
↓
多个Domain PlanVersion
```

禁止：

- 一个PlanVersion承载全部Domain；
- 一个Domain必须单独创建ScheduleRun。

---

## 7. Domain执行方式

V1当前：

```
Domain_Dependency
↓
拓扑排序
↓
稳定排序
↓
串行执行
```

不恢复：

- 多Hangfire Domain Job；
- Domain并行；
- 第二套Solver。

---

## 8. 共享资源

独立Domain计划不代表资源隔离。

执行：

```
Domain A完成
↓
形成共享Resource占用
↓
加入当前Run资源上下文
↓
Domain B消费
```

后续Domain不能重复占用共享资源。

不建设：

- Domain资源配额；
- 跨Domain资源借用平台；
- 虚拟资源平台。

---

## 9. Candidate

保持：

```
一个Candidate
=
一个Domain
+
一个Base ACTIVE PlanVersion
```

其它Domain ACTIVE共享资源：

作为ExternalDomainResourceBlocks传递。

禁止：

- MultiDomain Candidate；
- 修改其它Domain ACTIVE计划。

---

## 10. 跨Domain依赖

继续传递：

```
Material
+
Quantity
+
AvailableTime
```

保持分段。

例如：

```
40件 @ 15日
60件 @ 17日
```

不得压平。

---

## 11. 失败传播

例如：

```
C → B → A
```

若B失败：

- B新PlanVersion失败；
- A不发布本次新版本；
- A保持上一ACTIVE；
- 无依赖Domain继续。

FAILED不可恢复为RUNNING。

---

## 12. 性能验证

新增按Domain统计：

- DomainKey；
- Demand数量；
- LogicalProductionDemand数量；
- FinalTask数量；
- Pegging耗时；
- Solver耗时；
- 保存耗时；
- Domain总耗时。

同时记录ScheduleRun总耗时。

15分钟目标通过真实测量验证。

---

## 13. 本次不改变

不改变：

- Pegging规则；
- SupplyFact；
- PI Position；
- Demand Protection；
- Manual ETA；
- AvailableTime；
- Purchase Placeholder；
- 90天Solver；
- MES五态。

---

## 14. 最终流程口径

> APS V1夜间FULL以ScheduleRun作为入口，启动时冻结有效Domain集合，根据DomainDefinition完成真实归域，再按照Domain依赖顺序串行执行多个独立Domain计划。每个Domain使用同一套Pegging和有限产能Solver形成独立PlanVersion，同时通过Quantity-Time和共享Resource上下文保持跨Domain业务闭环。Domain只是计算和发布边界，不改变原有APS核心业务规则。


---

# V3.19 Supply / PI Position专项冻结对齐增量

## 一、ProductionInstruction Position参与走查规则

本版本补充冻结：

ProductionInstruction仍作为Supply参与Pegging。

ProductionInstructionPositionSnapshot用于表达一次排程运行中的PI位置事实。

Position不等于Supply：

- 不创建新的供给身份；
- 不参与Supply排序；
- 不独立生成Allocation；
- 不替代ProductionInstruction Supply。

## 二、跨厂接续走查规则

厂间订单型跨厂：

保持跨厂Supply承接。

大工艺接续型跨厂：

ProductionInstruction Supply
+
PositionType=INTERPLANT_TRANSIT

不新增Transit Supply。

## 三、Domain运行走查补充

DomainKey作为APS真实稳定排程域标识。

运行过程中统一关联：

- ScheduleRun；
- PlanVersion；
- Domain依赖关系。

V1不引入：

- DomainRuntime；
- DomainContext；
- VirtualInventoryBalance；
- Domain资源配额平台。

## 四、职责边界同步

3号位：
负责Domain定义和依赖治理。

2号位：
负责消费Domain信息并执行计算闭环。

1号位：
消费排程求解结果。

5号位：
提供事实数据和接口接入。


---

# V3.20 PI Position执行起点上下文专项冻结增量（2026-09-04）

## 1. PI Position与NextOperationContext关系

ProductionInstructionPositionSnapshot继续保持既有语义：
- 表示生产指示剩余数量当前所在位置；
- 不改变ProductionInstruction作为Supply的身份；
- 不创建新的Supply类型；
- 不参与独立Pegging。

新增NextOperationContext，用于表达当前PI剩余数量进入有限产能排程时的执行起点上下文。

来源：

MES执行事实 + Routing工序关系 + PI Position
→ NextOperationContext

## 2. 5号位职责补充

5号位负责基于ERP PI剩余量、MES执行事实及强位置事实计算PI Position，并结合Routing与PI Position计算NextOperationContext。

> 本节为V3.20当前有效口径，覆盖V3.19职责摘要中“5号位仅提供事实数据和接口接入”的不完整表述。

5号位不负责：
- Demand-Supply匹配；
- Allocation；
- 有限产能求解；
- FinalTask生成。

## 3. 2号位职责补充

2号位消费：
- Pegging结果；
- PI Position；
- NextOperationContext。

形成LogicalProductionDemand。

2号位不重新解析MES执行事实，不自行判断当前工序或下一执行工序。

## 4. 1号位职责补充

1号位有限产能Solver输入包含：
- LogicalProductionDemand；
- NextOperationContext；
- Routing；
- Resource Eligibility；
- Resource Calendar。

1号位从指定执行起点开始排程，不直接读取MES事实。

## 5. 冻结边界

本次不改变：
- PI Position不是Supply；
- 不建设Operation级Pegging；
- 不建设Operation级Position模型；
- 不改变Domain运行边界；
- 不改变90天统一有限产能Solver；
- 不改变FinalTask由1号位生成。

新增链路：

PI Position
↓
NextOperationContext
↓
LogicalProductionDemand
↓
有限产能Solver

---
