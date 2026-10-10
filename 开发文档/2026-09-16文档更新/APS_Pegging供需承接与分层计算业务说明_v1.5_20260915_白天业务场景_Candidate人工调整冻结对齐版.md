# APS Pegging供需承接与分层计算业务说明 v1.5（20260915 白天业务场景 / Candidate人工调整冻结对齐版）

**当前版本**：v1.5  
**当前版本日期**：2026-09-15（北京时间）  
**适用版本**：APS V1  
**文档性质**：业务与算法口径专项说明  
**基于版本**：v1.4（2026-09-14 SH旧模型残留清理版）  
**当前上位基线**：《APS V1 最终全部流程与业务基线 v1.6》  
**Domain专项依据**：《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》

> **当前版本声明**：本文件完整继承v1.4及2026-09-14 SH纠错后的Pegging冻结正文；本版只补充白天Candidate中的NORMAL/EXPEDITE、已有订单人工计划目标日期与当前Demand排序边界。PI单一Supply、SH单一Supply、Position、Demand Protection、跨Domain Quantity-Time和Candidate单Domain均不重新设计。历史章节若与本版冲突，以v1.5当前口径为准。

## 当前版本变化说明（2026-09-15）

**修改依据**：业务基线v1.6 + 白天业务场景/Candidate人工调整指导v1.1  
**修订原则**：只调整白天Candidate Demand排序与人工计划目标语义，不把Run治理、页面操作或Solver职责搬入Pegging。  
**影响边界**：PI/SH Supply、Demand Protection、Strict Binding、跨版本连续性、普通跨Domain Quantity-Time保持。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | 优先级语义 | Candidate重新Pegging | 新订单CTP=NORMAL；急插影响分析=EXPEDITE；EXPEDITE只作用于本次选中订单及其派生需求链。 | v1.1场景矩阵 | 白天Demand排序语义明确 | ScopeJson新增PriorityMode | 无永久字段 | 影响2/3号位 |
| 2 | 人工目标日期 | Candidate重新Pegging | ManualTargetDueDate只作为本次Candidate求解目标，NORMAL下不得改写正式DueDate或用来偷换Demand排序。 | 已有订单提前裁决 | 区分“目标时间”和“业务优先级” | OrderTargets结构化输入 | 无 | 影响2/3/4号位 |
| 3 | 规则保护 | 优先规则 | 不恢复全局PriorityScore/表达式评分器。 | 既有冻结保护 | 继续按规则段模型 | 无 | 无 | 影响2/3号位 |


> **2026-09-14 P0文档纠错（业务版本保持v1.4不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

---

## 历史继承说明（非当前版本元数据）

以下内容来源于上一冻结版本，仅用于追溯业务形成过程。

当前文档的有效版本、日期、上位基线以及业务裁决，以本文档顶部“当前版本声明”为准。

历史章节中的旧版本号、旧日期、旧上位基线，不代表当前开发、审核和验收依据。

---

## 完整业务正文（继承历史冻结内容）

**适用版本**：APS V1  
**文档性质**：业务与算法口径专项说明  
**冻结对齐日期**：2026-08-12  
**主要读者**：0号位、2号位、3号位、5号位，兼供1号位和4号位理解上下游边界  
**上位基线**：《APS V1最终全部流程与业务基线_v1.0_20260812》

> **冻结声明**：本文是最终业务冻结基线的Pegging专项解释文档，不拥有独立修改冻结业务的权力。本文只允许补充说明、案例、边界和与最终基线的一致性修正。若与最终业务冻结基线冲突，以最终业务冻结基线为准。任何业务口径变更必须明确提出“重新打开已冻结决策”，经0号位重新裁决后形成新版本。禁止因为代码现状、旧文档、实现方便、未来扩展或“更优雅”而静默修改本文已冻结业务。

## v1.1相对v1.0的对齐范围

本次不是重新设计，只把v1.0生成后已经冻结的补充口径回写：

1. 增加PI自消费红线；
2. 增加无正式PO/VMI时的规划性采购占位供给；
3. 强化Demand Protection / Strict Binding / Execution Constraint三类边界；
4. 明确跨Domain Quantity-Time必须保留分段；
5. 明确跨Domain失败链与人工重新计算；
6. 明确白天跨Domain CTP仍由多个单Domain Candidate串行完成；
7. 明确V1绝不建设有限物流Task体系；
8. 补充Candidate与正式发布/人工确认的边界。

除此以外，v1.0主体业务不重新打开。

---

## 1. 为什么需要单独说明Pegging

APS中的供需承接（Pegging）不是简单的“订单找库存”，而是把客户订单、生产指示、库存、在制、跨厂、采购/VMI等不同来源的供给，按照统一的业务层级和优先规则，建立**数量上可闭合、物理上不重复、跨版本可重新计算、最终可交给有限产能排程**的供需关系。

Pegging阶段只回答三类核心问题：

1. **谁需要什么、需要多少**；
2. **哪些真实供给可以承接这份需求、各承接多少**；
3. **承接后还剩多少缺口，需要形成新的生产需求**。

Pegging本身不决定最终设备、最终Operation Task、开工时间和完工时间。最终物理Task和时间由1号位有限产能排程决定。

---

## 2. V1最重要的业务前提

### 2.1 客户订单进入APS时已经是ERP成品库存处理后的“需生产量”

对客户订单（SALES_ORDER），ERP在订单进入APS前已经检查并使用成品库存。

例如：

- 客户原始需求120件；
- ERP成品库存已满足20件；
- APS收到Order.Quantity=100件。

APS从100件开始Pegging。

> **APS不得再次使用普通成品库存冲抵这100件。**

BOM展开后的下阶零件、半成品、采购件仍按各自库存与供给规则承接，这不属于“二次扣客户订单成品库存”。

### 2.2 生产指示PI剩余总量以ERP边界为准

V1使用：

`PI总剩余量 = max(Quantity - ReceivedQty, 0)`

ERP剩余量已经包含尚未最终进入目标M库前的全部剩余数量，包括：

- 未开工；
- MES在制；
- Stage间等待；
- PI级XC；
- PI级跨厂在途。

因此MES、XC、跨厂在途只回答“剩余数量现在在哪里”，**不得再次增加PI总量**。

---

## 3. 角色边界

### 3.1 2号位：Pegging执行者与数量真相所有者

2号位负责：

- 当前Domain需求池和供给池；
- 分层Demand排序执行；
- Demand→Supply实际Allocation；
- DemandBalance / SupplyBalance；
- AllocationSequence与数量闭合；
- Demand Protection / Lock执行；
- 跨层数量推进；
- 缺口转逻辑生产需求；
- 当前LogicalProductionDemand Q形成后，消费5号位标准化IN_PROGRESS事实形成逐MES工单连续份额与自由份额；
- SH第一层Supply分配、第二层源厂履约Demand形成及重新归域；
- Pegging结果统一持久化。

**2号位是APS运行时Demand/Supply数量关系的唯一修改者。**

### 3.2 3号位：规则与参数治理者

3号位配置、校验、发布并冻结：

- Demand排序规则；
- Lock Policy；
- 库存资格与Priority；
- PI选择规则；
- 采购ETA/LT参数；
- 计划良率；
- 其它排程策略参数。

3号位不逐笔参与Pegging，不修改运行余额。

### 3.3 5号位：复杂事实与复杂计算提供者

5号位负责：

- ODS、防腐层、BOM与Stage事实；
- 跨厂结构事实；
- MES工单/工序/Stage进度事实；
- 工序级最后报工设备（LastReportResourceCode）事实；
- PI当前位置计算；
- NextOperationContext计算；
- SH号在跨厂在途视图中的存在性等跨厂物流事实；
- 必要的数据质量Issue。

5号位不做最终Demand→Supply Allocation，不扣APS余额，不决定最终Lock，不生成最终排程ReasonCode。

### 3.4 1号位与4号位

- 1号位消费Pegging后的逻辑生产需求和数量—时间约束，决定最终Task/Resource/Start/End；
- 4号位负责CTP、插单、Candidate比较、影响展示和人工确认，不直接修改APS运行表。

---

## 4. Domain与“分层”的含义

V1中的分层是业务计算层级，不建设DemandCandidateGroup平台。

### 4.1 第一层：顶层独立需求

包括：

- 普通销售订单（SALES_ORDER），但排除客户分类（CustomerSegment）=`INTER_FACTORY`的厂间出荷指示（SH）订单；
- 顶层MTS/SS/SS_U等生产指示型需求；
- 其它独立补充需求。

SH订单本身不作为普通顶层Demand重复进入第一层，它在目标侧作为SH单一Supply。

### 4.2 第二层：SH源工厂履约需求

第一层普通Demand如果由SH承接，则当前层Demand→SH结束。

随后：

- `CLOSED / CANCELLED`：退出；
- `OPEN`且跨厂在途视图存在该SH号：已进入跨厂履行，不形成源厂Demand；
- `OPEN`且跨厂在途视图不存在该SH号：形成一条完整源厂履约Demand，`RequiredQty = Order.Quantity`。

该Demand使用`SourceFactoryId`重新归源厂Domain，再按PI→PI Position→缺口新增生产推进。不得按第一层Allocation份额或Transit/Received/ZP/BP状态拆分。

### 4.3 后续层：PI/BOM下阶需求逐层扫尾

逐层推进直到：

- 被库存/PI/采购/VMI等承接；
- 形成新的生产需求；
- 或确认缺口。

层之间只传必要的数量和业务身份，不建设独立“层平台”。

---

## 5. Demand排序：按层、按有序规则段，不做全局PriorityScore

同一层采用：

> **有序规则段（Ordered Segments） + 第一命中（First Match） + 段内排序（Intra-segment Sort）**。

例如：

1. 已受保护客户订单；
2. 临近交期客户订单；
3. 普通客户订单；
4. MTS补充需求。

一条Demand只进入第一条命中的规则段。

段内可按：

- DueDate；
- CustomerTier；
- OrderType；
- IssueDate；
- DelayStatus；
- 稳定来源键；

排序。

规则由3号位配置，2号位执行。V1不使用一个全局PriorityScore把所有Demand统排。

---

## 6. Supply身份：一份物理数量只能有一个当前身份

同一PlanVersion中同一物理数量只能以一种Supply身份出现。

### 6.1 普通库存

包括符合资格的普通现货库存。

### 6.2 生产指示PI

PI是一个独立Supply。PI内部Stage在制、XC、跨厂在途是PI Position，不是额外Supply。

### 6.3 厂间出荷指示SH

SH识别：`OrderType=SALES_ORDER`、`CustomerSegment=INTER_FACTORY`、`OrderNo=SH号`。

SH始终只有一个Supply身份。Transit / Received / ZP / BP只是同一SH履行事实，不生成额外Supply/Allocation，不按状态数量形成不同AvailableTime，也不用在途Quantity倒算未生产量。

普通Demand↔SH允许多对多，由2号位本次Pegging形成Allocation；5号位不提供普通Demand↔SH固定Document关系。

### 6.4 采购/VMI/已到厂未入库

它们是独立外部供给事实，按真实单据和数量—时间切片进入Pegging。

正式入库存库后，原未入库供给身份必须退出，避免重复。

---

## 7. 普通库存使用

普通库存先判断资格，再按Priority和稳定键排序。

基本顺序：

1. Eligibility；
2. Warehouse Priority；
3. 稳定排序。

每次Allocation后必须同步扣减内存SupplyBalance。

---

## 8. PI：必须“先选PI，再消费PI内部位置”

### 8.1 同Material多个PI

按3号位冻结规则选择，V1默认：

- 创建/发行时间优先；
- 稳定来源键最终Tie-break；
- 尽量用完一个PI再进入下一个PI。

### 8.2 Demand↔PI正式承诺粒度

正式承诺粒度：

> **PI编号 + 承接数量。**

不是“PI某Stage+数量”。

### 8.3 Position不能拉平和其它PI全局混排

正确顺序：

1. 先选PI；
2. 固定PI承接数量；
3. 再消费PI内部互斥Position。

### 8.4 PI自消费红线

PI剩余量进入下层扫尾时：

> **同一个PI不能再作为自己的Supply消费自己。**

它只能触发自己尚需完成的下阶BOM/Stage需求。

这是一条数量正确性红线，防止逻辑自循环。

---

## 9. PI当前位置计算（Production Instruction Position Calculation）

### 9.1 输入事实

至少包括：

- PI Quantity / ReceivedQty / Material / PI号；
- Stage路径和StageSeq；
- MES Stage累计进度；
- PI级XC；
- PI相关跨厂在途；
- 工序/仓库属性；
- DataCutoffTime。

V1以Stage级事实为主要位置闭合依据，不要求Operation级重建整条PI位置。

### 9.2 Position必须互斥闭合

例如PI剩余400：

- MACH未完成100；
- XC 180；
- 跨厂在途100；
- SURF完成待最终入库20。

合计必须为400。

### 9.3 Stage累计值用差分，不直接相加

MES累计Stage值必须通过相邻Stage差分得到区间位置。

### 9.4 中间Stage缺失

使用下游已经证明的最小完成量保守推断。

### 9.5 下游累计高于上游

默认保守下修下游；如果XC/在途等强事实证明某数量必然已完成上游，可反向修正规划侧有效上游累计，同时登记Issue，不回写源MES。

### 9.6 Received边界

最终Stage有效完成不能低于ReceivedQty；已经最终Received的数量不再属于PI剩余Position。

### 9.7 UNLOCATED降级

如果无法定位差额，进入UNLOCATED。

如果强事实互相冲突且确认不是重复，导致Position明显超过PI总剩余，V1不猜哪条源事实错，而是可将该PI剩余整体降级为UNLOCATED并登记Issue。

单个PI位置异常不应拖垮整个Domain，只要PI总剩余量本身可信。

基于UNLOCATED形成的保守规划Task不得下发MES。

---

## 10. 采购、VMI和已到厂未入库

采购/VMI正式进入V1 Pegging与交期判断，不是未来预留。

### 10.1 ETA优先级

> **人工ETA > ERP ETA > 默认采购LT推算。**

人工ETA取消后回落ERP ETA；ERP ETA为空再回落DefaultLT。

### 10.2 AvailableTime

`AvailableTime = EffectiveETA + 到货后可用Offset`

到货后Offset可包含收货、检验等确定性处理时间。

### 10.3 ETA缺失但存在正式PO

如果存在PO但无人工/ERP ETA，可按冻结DefaultLT推算Estimated AvailableTime。

这种日期必须保留“估算”属性，不能伪装成真实供应商承诺。

### 10.4 无任何正式采购承诺：Planning-only Purchase Placeholder

如果：

- 库存无；
- 已到厂未入库无；
- PO无；
- VMI无；

则V1允许仅在内存形成：

> **规划性采购占位供给（Planning-only Purchase Placeholder）**。

规则：

- Quantity = 当前采购缺口；
- AvailableTime = 当前/计划基准时间 + 冻结DefaultLT等参数；
- 标记 `ESTIMATED / NOT_COMMITTED`；
- 不生成采购单；
- 不生成制造Task；
- 不下发ERP/MES；
- 不作为正式已承诺Supply持久化真相。

如果CTP依赖该占位：

> **只能给估算日期，不能给确定性客户承诺。**

正式PO/VMI出现后，下一次排程自动用真实Supply替换估算占位。

### 10.5 采购内部排序

基本顺序：

1. 仓库资格；
2. 仓库Priority；
3. 同仓库AvailableTime；
4. PO发行时间；
5. PO号+项号稳定键。

已到厂未入库是比尚未到厂PO更强的当前事实。

---

## 11. 跨厂模式一：大工艺接续型（Stage Handoff）

适用于同一PI沿大工艺跨工厂继续生产。

正式Supply顺序：

> **目标工厂可直接使用M → 选择PI → PI内部Position → 缺口新增生产。**

例如B厂需要C500：

- B厂M100；
- PI-C01剩余300；
- PI内部：B厂等待100、在途120、XC80；
- 缺口100新增生产。

不搜索“上游工厂普通M库存”作为可直接跨厂借用的独立Supply。

PI级XC、PI级跨厂在途只属于PI Position。

### 11.1 跨厂运输

> **V1绝不建设有限物流Task体系。**

运输/检验/转运只通过确定性LeadTime形成下游AvailableTime。

不建车辆/月台/班次/ShippingTask有限产能Solver。

---

## 12. 跨厂模式二：厂间出荷指示型（Inter-factory Shipping Instruction）

### 12.1 第一层：SH作为目标侧单一Supply

第一层普通Demand可由一张或多张SH承接，普通Demand↔SH允许多对多；当前层Demand→SH后结束。SH订单本身不再作为普通SALES_ORDER Demand重复进入第一层。

### 12.2 第二层：SH整单源厂履约Demand

```text
CLOSED / CANCELLED
→ 退出活跃计算

OPEN + ERP_InterplantInTransit_View存在该SH号
→ 已进入跨厂履行
→ 不生成源厂履约Demand

OPEN + ERP_InterplantInTransit_View不存在该SH号
→ 生成一条完整源厂履约Demand
→ RequiredQty = Order.Quantity
```

查询成功0行合法。不得使用在途Quantity做`Order.Quantity - TransitQty`，也不得形成70/30等内部数量切片。

`FactoryId=目标工厂`，`SourceFactoryId=源工厂`。第二层按SourceFactoryId重新归源厂Domain，不改写原Order目标侧身份；继续按PI→PI Position→缺口新增生产。

### 12.3 SH整单优先级

一张SH只形成一条完整源厂Demand。其优先级取SH自身业务优先关系与当前全部上层Allocation中最强业务优先关系中的更强者。不得再按SH内部数量份额分别继承优先级。

### 12.4 SH整单时间

同一SH号只允许整单一次入库，因此目标侧只有一个整单AvailableTime。源厂真实准备完成时间由1号位有限产能产生；标准跨厂LT由3号位参数治理；跨Domain时由2号位传播真实完成结果。

## 13. 跨Domain供给与失败链

不同Product Domain存在物料依赖时，根据Domain_Dependency按拓扑顺序计算。

上游排完以后，下游得到的是：

> **Material + Quantity + AvailableTime。**

V1不建设VirtualInventoryBalance持久化平台。

### 13.1 Quantity-Time必须保留分段

如果实际是：

- 40件15号可用；
- 60件17号可用；

不能汇总成“100件17号可用”。必须保留两个数量—时间切片，让下游在允许拆批时能够提前使用40件。

跨Domain转运使用真实工厂/Stage LeadTime，不使用统一硬编码“2天”。

### 13.2 夜间上游Domain失败

若 `B → A`，本次B失败：

- B新PlanVersion=FAILED；
- A本次不得发布新ACTIVE；
- A保留上一ACTIVE；
- 与B失败链无依赖的其它Domain继续发布。

这不是全局ALL_OR_NOTHING，只在真实依赖链内保持一致。

### 13.3 人工重新计算

系统必须支持人工重新计算失败域。

规则：

- 原FAILED ScheduleRun/PlanVersion永久保留；
- 不得把FAILED改回RUNNING/BUILDING；
- 人工恢复新建ScheduleRun；
- 自动识别尚未解决的必要上游和因失败被阻断的下游；
- 只重新计算必要依赖链；
- 继续按拓扑顺序执行。

---

## 14. Strict Binding、Demand Protection与Execution Constraint

### 14.1 Strict Binding

客户专属、质量/环保资格以及源系统真实存在的业务单据强绑定证据等，普通Demand不能抢占。SH的Received/ZP/BP内部状态不再作为独立Supply或Strict Binding对象。

### 14.2 Demand Protection

用于保护必须优先确保的Demand。

触发条件由3号位Lock Policy配置，例如：

- 剩余交期短于正常LT；
- 已经延期；
- 特定客户等级；
- 其它已冻结业务保护条件。

保护的是：

> **该Demand真正必要的供给数量份额。**

不是整个PI/PO一刀切锁死。

### 14.3 Execution Constraint

已经真实发生的：

- 消耗；
- 领料；
- 最终直接履约Task开工；
- 其它不可逆执行事实；

不能在下一版本重新解释。

### 14.4 数据模型最小化原则

V1不为Demand Protection新建一套大平台。

同一需求—供给锁能力可通过LockType表达：

- `STRICT_BINDING`；
- `DEMAND_PROTECTION`。

ExecutionLock仍表达MES现实执行事实。

### 14.5 下阶通用PI开工不自动永久绑定原Demand

通用零件PI开工只说明“这批零件必须继续生产”，不自动说明“永久属于昨天的订单B”。

只要没有Strict Binding、Demand Protection、真实消耗或最终直接履约不可逆关系，仍可在下一版本重新分配给更高优先Demand。

---

## 15. 夜间FULL重新Pegging

每次夜间FULL按本次最新事实和冻结规则重新建立普通供需关系。

不可重新竞争份额包括：

- 已真实消耗；
- Strict Binding；
- Demand Protection；
- 不可逆执行；
- 已失效Supply；
- 其它硬业务约束。

普通上一版本Allocation没有“关系稳定性优先权”。

但真实MES开工中（IN_PROGRESS）执行属于现实执行事实。FULL必须先重新Pegging形成当前LogicalProductionDemand Q，再消费5号位标准化执行事实形成逐MES工单Continuation Slice和Free Slice。连续份额不是Supply，不进入Supply排序，也不生成第二套Allocation。

新高优先订单进入后，不允许为了保持昨天Demand↔PI关系而把新订单排后。

---

## 16. 白天Candidate中的重新Pegging

白天Candidate不是重跑全部ODS，也不是只算新订单自己。

单Domain内：

1. 固定不可移动份额；
2. 释放Base ACTIVE中的普通未锁Allocation回可竞争池；
3. 当前未完成可移动Demand与新订单一起重新Pegging；
4. 形成新的Allocation和逻辑生产需求；
5. 把生产需求新增/减少/数量变化作为1号位有限产能变化种子。

概念上：

`Candidate可竞争供给 = 当前有效物理供给 - 已消耗 - Strict Binding - Demand Protection - 不可逆份额 - 已失效份额`

不能继续使用“Base Supply - Base全部Allocation”的旧语义。

### 16.1 跨Domain插单CTP

如果最终产品Domain A依赖B、C：

> 一个Candidate PlanVersion仍严格一个Domain。

后台按拓扑：

`C WHATIF → B WHATIF → A WHATIF`

逐层传递Quantity + AvailableTime，最终合成为一个客户CTP答案。

不建设MultiDomain Candidate。

### 16.2 Candidate与正式采用

WHATIF只用于分析，永远不能自动成为正式计划。

正式采用只要求最小人工授权确认；V1不因为旧DDL存在审批表就强制建设完整BPM审批主链。


### 16.3 NORMAL / EXPEDITE对白天Pegging的正式语义

`PriorityMode=NORMAL`：

- 继续使用现有正式业务优先规则段；
- 不因为用户输入人工计划目标日期就改变Demand正式业务优先顺序；
- 不把甘特拖拽、设备故障、Calendar变化转成急单。

`PriorityMode=EXPEDITE`：

- 只作用于本次明确选择的订单及其派生需求链；
- 其它普通Demand继续按现有冻结优先规则；
- 不得突破Demand Protection、Strict Binding、已执行不可逆事实、Firm/Frozen或Material AvailableTime。

EXPEDITE不得通过“把DueDate改早”实现。

### 16.4 已有订单人工计划目标日期

已有订单提前允许一次多个：

```text
OrderTargets = [
  OrderA + TargetDateA,
  OrderB + TargetDateB
]
```

人工计划目标日期：

- 只属于本次Candidate；
- 不修改ERP DueDate；
- 不修改Order_Canonical正式DueDate；
- 不建立长期Override；
- NORMAL下不作为新的Pegging排序权威；
- EXPEDITE是否提升优先级由`PriorityMode`明确表达。

如果`OrderCanonicalIds`与`OrderTargets[]`同时存在，两者订单集合必须一致，防止出现两个运行输入真相。

---

## 17. Allocation与数量闭合

### 17.1 Demand闭合

`DemandQty = AllocatedQty + RemainingDemandQty`

### 17.2 Supply闭合

`SupplyQty = AllocatedQty + RemainingSupplyQty`

### 17.3 PI闭合

`PI TotalRemainingQty = Σ PI PositionQty`

### 17.4 Allocation与最终Task闭合

Pegging只形成Allocation和逻辑生产需求。

1号位排完后：

`AllocationQty = Σ AllocationTaskShare中的净合格产出份额`

计划良率造成的额外加工量不属于订单Demand份额。

以下属于硬错误，当前Domain不能发布：

- Allocation超Demand；
- Allocation超Supply；
- 负余额；
- 同一物理Supply重复消费；
- PI自消费；
- TaskShare与Allocation不闭合；
- 跨Domain/跨PlanVersion非法引用；
- 核心事务失败。

---

## 18. 异常与降级

### 18.1 可降级业务数据异常

例如：

- 中间Stage缺失；
- PI位置无法完全定位；
- MES累计矛盾但可保守修正；
- 单PI事实冲突。

处理：

> 保守修正或UNLOCATED + Issue + Domain继续。

### 18.2 不可降级核心错误

例如：

- Supply重复消费；
- Demand/Supply数量越界；
- Lock被绕过；
- 非法引用；
- 事务只成功一半。

必须使Domain失败，不能Warning后发布。

---

## 19. 无PI号虚拟生产需求

阶段净需求存在但无正式PI时，可以形成规划用的无PI逻辑生产需求。

规则：

- 可以交1号位占未来产能；
- 可以用于交期风险计算；
- **不得下发MES**；
- 正式PI到达后，下一次FULL按最新事实重新Pegging；
- 不保留上一版本普通虚拟关系稳定偏好。

UNLOCATED PI形成的保守Task同样不得直接下发MES。

---

## 20. 典型案例一：普通客户订单

ERP原需求120，库存满足20，APS收到SALES_ORDER=100。

BOM：

- 自制C100；
- 采购P100。

Pegging：

1. 客户订单不再扣成品库存；
2. C已有PI-C01 80；
3. C缺20形成新增生产；
4. P库存60；
5. PO承接40并形成AvailableTime；
6. 采购不生成制造Task，只约束下游时间。

---

## 21. 典型案例二：多订单竞争与插单

原ACTIVE：

- A受Demand Protection，需要C60；
- B普通需求需要C100；
- 通用PI-C01剩余140。

原分配：A60、B80，B缺20。

插入高优先U需要C70：

- A60不动；
- B普通80释放；
- U获得70；
- B只剩10；
- B新生产缺口变90。

然后由1号位判断U交期及B和其它订单的真实产能影响。

---

## 22. 典型案例三：大工艺跨厂

B厂需要C500：

- B厂M100；
- PI-C01 300，其中B厂等待100、A→B在途120、XC80；
- 缺口100新增生产。

PI Position只决定这300件从哪里继续，不把XC/在途额外加成Supply。

---

## 23. 典型案例四：厂间SH

B厂普通Demand需要D300：

- BS/KS普通库存50；
- SH-001数量250，作为**一份单一Supply**承接剩余250。

随后只做整单状态判断，不按Transit/Received/ZP/BP拆数量。

### 情况A：SH-001已进入跨厂履行

```text
Order.Status = OPEN
且 ERP_InterplantInTransit_View 中存在 SH-001
```

则：

```text
整张SH已进入跨厂履行
→ 不生成第二层源厂履约Demand
→ 即使视图显示TransitQty=80、其它ERP事实显示ReceivedQty=70
   也不得计算“250-80-70=100未生产”
```

### 情况B：SH-001尚未进入跨厂履行

```text
Order.Status = OPEN
且 ERP_InterplantInTransit_View 中不存在 SH-001
```

则：

```text
生成一条完整源厂履约Demand
RequiredQty = Order.Quantity = 250
SourceFactoryId = Order.SourceFactoryId
```

第二层继续：

```text
PI → PI Position → 最终缺口新增生产
```

例如源厂已有PI/PI Position可以承接150，最终才形成100新增生产。

> 这里的100来自“250的源厂履约Demand被PI承接150后的最终缺口”，绝不是`250 - Transit80 - Received70`得到的所谓SH未生产份额。

上游真实SourceReadyTime再通过冻结跨厂LeadTime形成目标侧整单AvailableTime。

---

## 24. 典型案例五：PI异常但Domain继续

PI剩余200，但强事实XC150+在途120且确认非重复，无法判断哪条错误。

V1：

- PI整体200降级UNLOCATED；
- 记录Issue；
- 仍可作为200 Supply承接；
- 1号位从最早可信Stage保守占产能；
- 保守Task不得下发MES。

---

## 25. 典型案例六：Domain失败与人工重算

依赖：`C → B → A`。

凌晨：

- C成功；
- B失败；
- A因依赖B不发布；
- 无关D/E正常发布；
- ScheduleRun=PARTIAL_SUCCESS。

修复B后：

- 原FAILED历史保留；
- 新建ScheduleRun；
- 自动重算B→A；
- C不重复计算。

---

## 26. 典型案例七：无正式PO的90天规划

装配未来需要采购件P100：

- 库存0；
- PO 0；
- VMI 0；
- DefaultLT=20天。

APS形成内存Planning-only Purchase Placeholder：

- Qty=100；
- AvailableTime≈基准时间+20天；
- `ESTIMATED / NOT_COMMITTED`。

90天后续设备负荷继续计算，但销售CTP必须显示：

> “该日期依赖未承诺采购供给，仅为估算，不是确定性承诺。”

---

## 27. V1明确不做

V1不建设：

- DemandCandidateGroup / SupplyCandidateGroup平台；
- 全局PriorityScore；
- Pegging脚本/DSL；
- 动态插件市场；
- 泛化Position Header+Slice平台；
- 有限物流Task体系；
- MultiDomain Candidate；
- SupplyReallocation History平台；
- VirtualInventoryBalance持久化状态机；
- 通用SolverTrace/因果图；
- 普通Allocation跨版本稳定性偏好；
- 复杂审批工作流作为Pegging主链依赖；
- 为逻辑Allocation强制新建第二套权威账本真相源。

---

## 28. 开发检查清单

### 2号位

必须能回答：

- 当前层Demand有哪些？
- 排序规则段如何命中？
- Supply真实剩余是多少？
- 哪些份额不可重新竞争？
- 每次Allocation是否双边原子扣减？
- PI是否可能自消费？
- 缺口如何进入下一层或生产需求？
- Candidate是否错误扣死上一ACTIVE普通Allocation？

### 3号位

必须能回答：

- 当前ScheduleRun绑定哪个冻结策略版本？
- Demand排序有哪些规则段？
- Lock Policy触发/范围/传播/释放是什么？
- PI选择、采购ETA/LT等参数来自哪个冻结版本？
- 是否出现无必要脚本/动态插件？

### 5号位

必须能回答：

- PI总剩余是否严格以ERP边界为准？
- Position是否互斥闭合？
- XC/在途是否被重复当Supply？
- 何时强事实修正、何时UNLOCATED？
- 是否越权替2号位做最终Allocation/Lock？

---

## 29. 与其它文档的权威关系

本文负责Pegging专项解释。

如果旧《核心排产全流程走查》、演变总表、接口、字段、DDL中出现以下旧口径，应由旧文档向本文及最终冻结基线收敛：

- 5号位通用Pegging插件/Voucher最终裁决；
- 2号位提前生成FinalTask；
- 全局PriorityScore；
- Candidate扣死Base ACTIVE全部Allocation；
- STAGE_HANDOFF把XC/上游M当独立供给；
- ShippingTask进入有限产能主链；
- PipelineSupplies在V1固定为空；
- 上游Domain失败后依赖下游继续发布；
- 5号位负责Freeze/ReasonCode；
- 1号位重新解释Stage累计来判断PI当前位置；
- 无正式PO时让90天计划直接断链。

本文不替代接口、字段、DDL；后者只负责把已冻结业务正确落地。

---

## 30. Pegging最终冻结红线

1. SALES_ORDER进入APS后不再扣普通成品库存。  
2. PI RemainingQty以ERP边界为准。  
3. Demand排序采用分层、有序规则段、第一命中、段内排序。  
4. 先选PI，再消费PI Position。  
5. PI不能消费自己。  
6. 同一物理数量只有一个Supply身份。  
7. PI Position互斥闭合，异常可UNLOCATED保守降级。  
8. 采购/VMI/已到厂未入库进入V1正式Supply。  
9. 无正式采购承诺时允许Planning-only Purchase Placeholder，但只能给估算日期。  
10. Demand Protection、Strict Binding、Execution Constraint分开。  
11. 普通Allocation不跨版本强制稳定。  
12. Candidate释放普通未锁Allocation重新竞争。  
13. 大工艺跨厂：目标M→PI→PI Position→新生产。  
14. 厂间订单：第一层普通Demand→SH单一Supply；OPEN且在途视图无SH号时，第二层形成一条RequiredQty=Order.Quantity的源厂Demand，按SourceFactoryId重新归域后执行PI→PI Position→缺口新增生产。  
15. 跨Domain供给必须保留Quantity-Time分段。  
16. 上游Domain失败时依赖下游本次不发布，FAILED恢复必须新建ScheduleRun。  
17. 跨Domain CTP用多个单Domain Candidate串行，不建MultiDomain Candidate。  
18. V1绝不建设有限物流Task体系。  
19. Candidate、无PI虚拟Task、UNLOCATED规划Task不得绕过正式MES发布资格。  
20. 数量闭合、重复消费、PI自循环、事务错误属于硬错误，Domain不得发布。

---

**状态**：v1.1 冻结对齐版。后续只接受符合性修正，不接受开放式业务再设计。

---

# 2026-09-01 Domain专项历史形成记录（当前v1.4已吸收）

# APS Pegging供需承接与分层计算业务说明 v1.2（Domain专项一致性修订版）

**适用版本**：APS V1  
**文档性质**：业务与算法口径专项说明  
**日期**：2026-09-01（北京时间）  
**基于版本**：《APS Pegging供需承接与分层计算业务说明 v1.1（冻结对齐版）》  
**上位基线**：《APS V1最终全部流程与业务基线 v1.2》  
**专项依据**：《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》

> **冻结声明**：本文完整继承v1.1全部Pegging业务，不重新设计Demand/Supply、Allocation、PI Position、Demand Protection、Strict Binding、Planning-only Purchase Placeholder、跨厂、Quantity-Time、失败恢复等既有冻结规则。本版只修订Domain相关口径，使其与业务基线v1.2一致。

---

## 1. 本版唯一修订范围

本版只增加或修正以下内容：

1. 当前Domain由有效DomainDefinition确定；
2. Domain不等于ProductFamily；
3. Order/Demand在进入Domain内Pegging前，由2号位完成真实归域；
4. `Domain_Dependency`节点使用真实DomainKey；
5. 跨Domain Quantity-Time继续保持既有分段规则；
6. 跨Factory不必然等于跨Domain，同ProductFamily也不必然等于同Domain。

除此以外，v1.1所有Pegging业务继续有效。

---

## 2. Domain与Pegging的边界

Domain是：

> APS独立排程、PlanVersion、Candidate、失败传播与发布边界。

Pegging仍然回答：

- 谁需要什么、需要多少；
- 哪些Supply承接；
- 各承接多少；
- 缺口形成什么逻辑生产需求。

因此：

> **Domain决定“在哪个计算边界内做Pegging”，不改变Pegging本身的业务算法。**

---

## 3. DomainDefinition不属于Pegging规则

DomainDefinition属于APS基础配置，不属于：

- Demand排序规则；
- Supply排序规则；
- RuleSet；
- ParameterSet；
- StrategyProfile。

Pegging不得通过Priority Segment或其它规则动态创造Domain。

---

## 4. Order / Demand归域

正式顺序：

```text
有效DomainDefinition
↓
Order / Demand业务属性
↓
2号位匹配真实DomainKey
↓
进入该Domain内Pegging
```

V1只允许：

- FAMILY：按ProductFamily归域；
- FACTORY_FAMILY：按Factory + ProductFamily归域。

禁止：

- 默认Domain=ProductFamily；
- 以PlanVersionId代替DomainKey；
- 代码内另写一套Domain归属规则。

---

## 5. 当前Domain需求池与供给池

原v1.1中“当前Domain需求池和供给池”继续有效，但“当前Domain”必须解释为：

> 当前有效DomainDefinition匹配出的真实DomainKey。

不能再无条件理解为“当前ProductFamily”。

---

## 6. 跨Domain与跨ProductFamily不得混同

从本版开始，下列表述失效：

```text
跨ProductFamily = 跨Domain
同ProductFamily = 同Domain
Domain = ProductFamily
```

正确判断：

> 是否跨Domain，只看实际DomainKey是否不同。

例如同一个ProductFamily Y：

```text
BJ_FAMILY_Y
TJ_FAMILY_Y
```

仍属于两个Domain。

反之，一个FAMILY型Domain可以覆盖多个Factory中的同一ProductFamily范围。

---

## 7. 跨厂与跨Domain是两个不同维度

跨厂业务继续按原两类冻结规则执行：

- 大工艺接续型；
- 厂间出荷指示型。

但：

> **跨Factory不必然等于跨Domain。**

是否跨Domain，必须根据DomainDefinition判断。

Pegging不得因为看到Factory变化就自动创建跨Domain关系。

---

## 8. Domain_Dependency

原v1.1跨Domain依赖规则继续有效。

修正节点语义：

> `Domain_Dependency`中的上游/下游必须是真实DomainKey。

不得继续默认：

```text
UpstreamDomainCode = ProductFamily.Code
DownstreamDomainCode = ProductFamily.Code
```

---

## 9. 跨Domain Quantity-Time保持原冻结

上游Domain给下游Domain传递：

```text
Material + Quantity + AvailableTime
```

必须保留分段。

例如：

```text
40件 @ 15日
60件 @ 17日
```

不得压成：

```text
100件 @ 17日
```

这条规则没有因Domain定义修订而变化。

---

## 10. 跨Domain失败链保持原冻结

若：

```text
C → B → A
```

B本次失败：

- B新PlanVersion失败；
- A本次不得发布新ACTIVE；
- A保留上一ACTIVE；
- 无关Domain继续；
- ScheduleRun按现有规则汇总。

此处的C/B/A必须理解为真实DomainKey节点。

---

## 11. Candidate与CTP保持原冻结

Candidate继续严格单Domain。

跨Domain CTP继续采用：

```text
单Domain WHATIF
→ Quantity-Time
→ 下一个单Domain WHATIF
```

不建设MultiDomain Candidate。

---

## 12. Pegging不新增的能力

本次Domain修订不新增：

- Domain规则引擎；
- Domain DSL；
- 任意条件归域；
- 跨Domain通用供给池；
- VirtualInventoryBalance持久化平台；
- MultiDomain Allocation；
- 第二套Pegging算法。

---

## 13. 最终一致性口径

> **APS V1先根据当前有效DomainDefinition把Order/Demand归入真实DomainKey，再在该Domain内部执行原有Pegging冻结算法；跨Domain关系必须使用真实DomainKey，并继续按分段Quantity-Time传递供给时间。Domain不是ProductFamily的别名，也不会改变既有Demand/Supply、Allocation、PI Position、Demand Protection和跨厂业务规则。**


---

# 2026-09-04 PI Position执行起点上下文历史形成记录（当前v1.4已吸收）

## 1. 本次专项修改范围

本次仅补充：

- PI Position与有限产能排程执行起点的衔接；
- NextOperationContext（下一执行工序上下文）的责任边界。

不改变：

- ProductionInstruction作为Supply的定义；
- Demand ↔ PI Supply Pegging粒度；
- PI Position模型；
- Allocation规则；
- Demand Protection；
- Strict Binding；
- Quantity-Time闭环。

---

## 2. PI Position与NextOperationContext关系

PI Position回答：

> 当前生产指示剩余数量已经处于哪个执行位置。

NextOperationContext回答：

> 对于该剩余数量，下一步应从哪个工艺阶段/工序继续执行。

二者关系：

```text
MES执行事实
+
Routing/Stage关系
+
PI Position解析
        ↓
NextOperationContext
```

NextOperationContext不是：

- 新Supply；
- 新Position；
- Allocation对象；
- Operation级Pegging对象。

---

## 3. 职责边界

### 5号位

负责：

- 提供并标准化MES执行事实；
- 结合Routing/Stage关系计算PI Position；
- 基于MES执行事实 + Routing + PI Position计算NextOperationContext；
- 输出必要Issue / Evidence。

> 本节为v1.3当前有效职责口径，覆盖历史版本中“5号位只提供事实、2号位自行形成Position解释”的旧表述。

5号位不负责：

- Demand与PI Supply分配；
- Allocation；
- 最终排程。

### 2号位

负责：

- 消费PI Position；
- 消费NextOperationContext；
- 形成LogicalProductionDemand；
- 维持Demand↔PI Supply数量闭合。

### 1号位

负责：

- 消费LogicalProductionDemand；
- 根据执行起点展开Routing；
- 进行有限产能排程。

---

## 4. 禁止事项

禁止：

- 将NextOperationContext作为Supply身份；
- 将Pegging扩展为Demand↔Operation绑定；
- 将PI Position拆成工序级Supply；
- 由1号位重新读取MES判断当前位置。

---
