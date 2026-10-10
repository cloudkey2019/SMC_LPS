# APS V1 最终全部流程与业务基线 v1.8（20261005 四专题 / A-B跨版本连续性 / 多Routing / FINAL联合回写版）

**当前版本**：v1.8\
**当前版本日期**：2026-10-05（北京时间）\
**原业务冻结基线日期**：2026-08-12\
**基于版本**：v1.7（2026-09-20 Setup换型规则 / 用户输出能力联合冻结对齐版）\
**适用范围**：APS
V1交付、0～5号位开发、权威技术文档统一修订、代码整改与验收\
**文档性质**：业务流程与算法边界最高优先级基线

> **当前版本声明**：本文件为v1.8当前正式变更正文，完整继承v1.7及此前仍有效冻结业务，并按2026-09-30 Q1～Q4补充裁决、四专题+A/B连续性联合指导、多Routing与FINAL补充裁决完成原位回写。历史章节中的旧版本元数据、旧Routing单路径表达、旧Direction配置表达如与v1.8冲突，仅作历史形成记录，不再作为当前实现依据。

## v1.8 当前版本变化说明（2026-10-05）

**当前裁决依据**：本轮业务变更裁决v1.2 + 逐文档修改指导v1.1 + Q1～Q4补充裁决 + 《关于出口库物料与Routing冲突的补充裁决》。  
**本轮新增/覆盖**：Task/MES五身份、A/B/C跨版本连续性、Batch/Direction、Demand Goal传播、Stage/Routing、多Routing、FINAL Material标准化、PreferredResource软偏好。  
**状态**：正式变更态；待指导位Step 9复审通过后方可进入控制层收口。

## 历史版本变化说明（v1.7，2026-09-20）

**修改依据**：《Setup换型规则与有限产能优化——冻结文档修改指导 v1.3》+《用户输出能力冻结文档修改指导 v1.1 评审修订收口版》+《Setup与用户输出联合冻结修订影响矩阵及精确Delta v1.0》  
**修订原则**：从20260915最终终验完整母版自然演进；Setup只把已开发上位指导完整回写，不重新设计；用户输出V1/V1.1范围只认评审收口版。  
**影响边界**：不重新打开Pegging/PI/SH/Domain/Candidate/跨版本连续性；不建设第二Setup版本平台或第二报表平台。

| 编号 | 类型 | 章节 | 内容 | 业务影响 | 接口/DB影响 | 代码影响 |
|---|---|---|---|---|---|---|
| 1 | Setup模型替换 | 第十三部分§57 | 废止SetupAttribute、MES/RoutingOperation.SetupTime运行真相和From/ToOperation模型；改为当前Operation+Resource+From/ToMaterial。 | Setup时间成为真实资源占用 | RuleSetVersion从属Setup规则；SetupTime兼容废弃 | 1/2/3/4 |
| 2 | Setup求解增强 | 第十三/十四部分 | 初始资源/槽位即消费Setup；邻接重算；FULL连续可用生产窗口内有界序列优化；Candidate局部优先。 | 降低换型但不牺牲履约 | 2→1冻结Setup上下文 | 1/2/3 |
| 3 | 用户输出闭环 | 第十五/十九部分 | 当前PlanVersion预计完成 vs CustomerDueDate作为唯一计划延期判定；正式延期必须Explanation。 | 订单交付结果可解释 | 查询/Explanation链 | 1/2/4/5 |
| 4 | V1/V1.1范围 | 第十九部分 | V1保留核心交付/追溯；Resource Load/Setup KPI/Execution Risk/Full Version Diff等后移V1.1。 | 控制V1交付范围 | Summary降级可选缓存 | 2/3/4/5 |
| 5 | 底座保护 | Explanation/历史事实 | V1保存Setup、Explanation、PlanVersion、Pegging历史事实，避免V1.1返工Solver/Pegging。 | 后续增强自然演进 | 优先复用ExplainTrace/现有真相表 | 1/2/5 |

> **2026-09-14 P0文档纠错（业务版本保持v1.5不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

------------------------------------------------------------------------

## 0. 本文档的权威性与使用原则

### 0.1 本文档如何形成

本文档汇总并统一以下来源：

1.  0号位与AI在APS项目历次讨论中**明确确认**的业务裁决；
2.  AI提出、0号位在后续讨论中**未明确反对**的修改建议------按0号位本次确认，均视为同意并纳入本基线；
3.  当前六份APS权威文档中仍然有效、且未被后续讨论覆盖的内容；
4.  2号位实际提交代码与数据库事实复核中，已经确认应保护、复用或退出V1主链的内容；
5.  最近完成的Pegging、有限产能、插单、跨厂、跨Domain、异常恢复等完整业务案例闭环。

### 0.2 冲突优先级

后续如果旧文档、旧代码注释、旧表结构与本文冲突，按以下优先级处理：

> **0号位最新明确裁决 \> 本文档最终基线 \>
> 后续按本文统一修订后的六份权威文档 \> 旧版本文档/历史代码/历史注释。**

本文并不要求为了"文档一致"而推翻已经能工作的代码。所有技术整改必须遵循：

> **保护2号位现有代码外壳，优先停用冲突旧路径、补核心闭环，不为了架构漂亮而重写。**

### 0.3 V1的总原则

APS V1必须同时满足：

-   **能按期交付**；
-   **结果业务上可用**；
-   **未来可扩展**；
-   **当前不过度设计**；
-   **不把所有任务压在2号位**；
-   **也不能为了分工而硬拆插件、接口和表。**

V1的设计标准是：

> **稳定主流程 + 真正可变的规则/参数 + 少量必要复杂事实服务 +
> 单一有限产能Solver。**

------------------------------------------------------------------------

# 第一部分 APS V1目标、范围与红线

## 1. V1核心业务目标

APS V1必须真正解决以下问题：

1.  90天订单完整BOM与完整Pegging；
2.  自制件、采购件、库存、PI、跨厂等供给的统一承接；
3.  真实有限产能排程；
4.  可靠交期判断（CTP）；
5.  白天插单及时评估；
6.  多工厂、大工艺接续和厂间订单型跨厂；
7.  采购/VMI等外部供给的可用时间约束；
8.  MES执行事实反馈后次日重新计算；
9.  失败Domain可恢复、可人工重算；
10. 对延期、瓶颈、物料等待等原因可解释。

## 2. 90天计划的最终口径

### 2.1 90天全部订单都进入正式计算

V1滚动90天内的全部活跃订单：

> **完整BOM + 完整Pegging + 全量进入有限产能时间推演。**

90天不是"只有近期精排、远期只看日产量"的两套正式计划。

### 2.2 一套Solver，不预建第二套远期粗排算法

V1首先使用**同一套有限产能求解能力**覆盖90天。

近期与远期可以在工程实现中采用不同的搜索深度，例如远期减少候选设备、减少拆批尝试，但：

-   资源语义一致；
-   Task仍占用真实未来产能；
-   不能把31～90天换成另一套完全不同的"日产量桶算法"后再拼接。

只有真实压测证明统一方案无法满足性能目标后，才允许重新评估远期降级；V1不提前建设第二套Solver。

### 2.3 周/月/季度只是同一90天计划的管理视图

-   周计划：按周聚合；
-   月计划：按月聚合；
-   季度：查看90天窗口与自然季度的交集；
-   年度详细排程不进入V1。

不单独再跑周、月、季度、年度四套排程。

## 3. 明确不进入V1的过度设计

V1明确不建设：

-   MultiDomain Candidate；
-   跨Domain共享资源配额/借用平台；
-   有限物流Task体系；
-   车辆、班次、月台有限资源排程；
-   第二套远期粗排Solver；
-   任意脚本/DSL规则平台；
-   Asprova式用户自定义Command链；
-   动态插件市场/运行时插件注册平台；
-   持久化Impact Graph；
-   Solver完整搜索轨迹数据库；
-   通用因果图平台；
-   无限拆批组合；
-   数学全局最优多瓶颈平台；
-   VirtualInventoryBalance持久化状态机；
-   FrozenZoneSnapshot完整平台；
-   为普通插单强制建设Scenario/Simulation平台；
-   复杂多级审批作为排程主链前置；
-   为了"规范化"给所有表大量新增主外键关联。

------------------------------------------------------------------------

# 第二部分 核心对象和数量语义

## 4. 客户订单（SALES_ORDER）进入APS时的数量含义

客户订单进入APS前，ERP已经完成成品库存检查/占用。

因此：

> **APS收到的SALES_ORDER.Quantity就是仍需要生产的数量。**

APS不得再次查普通成品库存冲抵客户订单，否则会重复扣减。

例如：

-   ERP客户需求120；
-   ERP已用成品库存满足20；
-   APS Order.Quantity=100；

则APS从100开始找生产供给。

## 5. 订单类型

V1统一：

-   SO/MTO → `SALES_ORDER`；
-   MTS/SS/SS_U → `PRODUCTION_INSTRUCTION`。

不同订单类型在同一计算层内可以穿插排序，不允许简单写死成"客户订单全部排完后再MTS"。

## 6. 生产指示（PI）剩余总量

生产指示的总剩余量以ERP边界为准。

原则：

> **PI RemainingQty = ERP所定义的尚未最终入目标M库的全部剩余数量。**

其中可能包含：

-   未开工；
-   MES在制；
-   Stage间等待；
-   PI级XC；
-   PI级跨厂在途。

MES完成量、XC和在途不能再次加到PI剩余总量上；它们只用于判断PI剩余量"现在在哪里"。

## 7. 计划良率与数量语义

V1正式区分：

-   **净需求量（Net Required Qty）**：最终需要的合格数量；
-   **计划加工量（Planned Process
    Qty）**：考虑计划良率后预计要投入的加工数量。

例如：

-   需求合格100；
-   MACH计划良率98%；

则MACH可能计划加工约102～103，但：

> Allocation仍然只承接100。

额外预计损失不属于任何客户Demand。

### 7.1 已有PI不能因为计划良率被"放大"为Supply

PI剩余60，就是Supply 60。

不能因为预计后续有损失而把PI变成62件Supply。

如果确实需要额外生产来保证60件合格输出，额外数量形成新的生产需求。

### 7.2 当前不假设BOM存在ScrapRate参与V1反算

V1不依赖BOM ScrapRate做材料级反算。

未来如果BOM存在材料损耗，它与Stage计划良率是不同业务语义，不得因为字段名字类似而自动叠加。

------------------------------------------------------------------------

# 第三部分 0～5号位最终职责与"谁动数据库"红线

## 8. 0号位

负责：

-   业务红线；
-   核心优先规则；
-   V1/V2范围裁决；
-   跨团队冲突最终裁决；
-   重大规则参数的业务确认。

不负责：

-   Solver技术阈值；
-   内存队列、索引等工程细节。

## 9. 1号位：有限产能和时间真相

负责：

-   FinalTask；
-   实际Resource；
-   Start/End；
-   最终拆批/合批；
-   Task-to-Task真实物理依赖；
-   动态瓶颈；
-   正排/倒排；
-   Setup；
-   受影响范围传播；
-   Candidate局部修复；
-   Explanation Fact；
-   同时求解既存执行连续份额（Continuation Slice）与新增自由份额（Free Slice）；
-   原样回传连续性关联键（ContinuationKey）或现有等价稳定键；
-   消费工序级最后报工设备（LastReportResourceCode）作为资源连续性偏好，不单凭该字段形成硬锁；
-   对同一生产指示（PI）同一工序（Operation），保证既存执行连续份额先于新增自由份额，且APS自动求解不形成多设备时间重叠；
-   在SH场景中形成源工厂真实准备完成时间（SourceReadyTime）。

**数据库红线：1号位纯内存计算，不直接读写APS业务数据库。**

所需数据由2号位组装成ScheduleContext输入。

## 10. 2号位：主流程、Pegging和数量真相

2号位必须继续负责APS主流程实现。

核心职责：

-   SchedulingOrchestrator主入口；
-   ScheduleContext组装；
-   Demand/Supply池；
-   分层Pegging；
-   DemandBalance / SupplyBalance；
-   Allocation；
-   Lock执行；
-   跨Domain数量---时间传递；
-   计划良率反算；
-   形成逻辑生产需求；
-   在当前逻辑生产需求数量（Q）形成后，消费5号位标准化MES既存执行事实，逐MES工单形成连续份额与自由份额；
-   治理ContinuationKey与旧TaskNo / 原MES工单号的内部映射；
-   识别SH单一Supply、第一层Allocation和第二层源厂履约Demand；
-   对第二层SH使用SourceFactoryId重新归源厂Domain，不改写原Order目标侧FactoryId/DomainKey；
-   按当前Run冻结的排程域（Domain）集合，串行逐Domain调用同一套1号位有限产能求解器（Solver）；
-   统一结果持久化；
-   PlanVersion结果落盘；
-   Candidate变化种子识别；
-   MES下发资格所需运行结果准备。

**数据库红线：2号位是APS运行时业务结果表的唯一主要写入者。**

包括Task、Allocation/Pegging结果、PlanVersion结果、Explanation落库等。

2号位不应：

-   提前生成最终Operation Task；
-   在运行过程中永久修改库存快照余额；
-   自己做第二套有限产能求解；
-   把可变业务逻辑全部硬编码在主流程；
-   绕过5号位直接解释MES原始工单/工序/Stage事实；
-   用旧TaskNo、旧MES工单或既存执行量反向决定当前业务需求Q；
-   将SH拆成Transit / Received / ZP / BP多份Supply，或用在途数量倒算SH未生产量。

## 11. 3号位：规则、参数与运行生命周期

负责：

-   规则/参数编辑；
-   校验；
-   发布；
-   不可变版本；
-   StrategyProfile绑定；
-   ScheduleRun生命周期；
-   Candidate版本壳与激活边界；
-   SH标准跨厂运输/检验/转运提前期参数治理；
-   SH第二层整单优先级规则治理。

3号位可以写：

-   Rule/Parameter/Strategy治理表；
-   ScheduleRun/PlanVersion生命周期元数据；
-   激活相关元数据。

3号位不能：

-   修改Task/Pegging运行结果；
-   修改库存运行余额；
-   成为逐Demand、逐Task的中央规则服务。

正确模式：

> 运行开始前加载冻结规则快照，1/2号位在内存执行。

## 12. 4号位：页面和业务操作入口

负责：

-   甘特图；
-   CTP；
-   插单影响；
-   Candidate比较；
-   异常展示；
-   人工确认；
-   参数维护页面。

**4号位不直接修改APS运行时业务表。**

所有正式状态变更必须通过3号位/2号位服务完成。

## 13. 5号位：ODS、防腐事实、BOM与复杂事实计算

负责：

-   ODS；
-   BOM展开；
-   Stage路径；
-   ProcessCode/ERPProperty真实业务属性；
-   跨厂物流事实；
-   PI位置计算；
-   下一工序上下文（NextOperationContext）计算；
-   MES工单、工序、Stage执行事实标准化；
-   工序级最后报工设备（LastReportResourceCode）事实标准化；
-   采购/VMI等外部供给事实准备；
-   数据质量Issue。

5号位可以写：

-   ODS/MES_Integration中的自己负责的事实与防腐结果；
-   自己责任范围的数据质量结果。

5号位不能：

-   直接修改APS Task；
-   直接修改Pegging/Allocation；
-   修改运行时SupplyBalance；
-   生成最终有限产能Task；
-   决定最终延期ReasonCode；
-   每分配一条供给都回调一次决定业务动作；
-   替2号位计算Q/E分桶；
-   建设第二套SH主档、普通Demand↔SH固定关系或源厂有限产能完成ETA。

------------------------------------------------------------------------

# 第四部分 规则、参数、插件的最终边界

## 14. 规则引擎只治理真正"可变"的业务

规则/参数分四类：

1.  **业务规则（Rule）**：什么条件下采用什么业务策略；
2.  **业务参数（Parameter）**：LT、良率、批量、余裕等数值；
3.  **事实（Fact）**：Routing、设备资格、日历、ETA等客观事实；
4.  **算法硬约束（Invariant）**：数量不能负、设备不能冲突、Supply不能重复消费等。

只有前两类进入3号位治理。

## 15. V1规则引擎只保留两种基础执行模式

### 15.1 有序规则第一命中

用于：

-   需求排序；
-   Demand Protection触发；
-   少量资格类规则。

### 15.2 全局默认 + 范围例外覆盖

用于：

-   采购LT；
-   计划良率；
-   正倒排策略；
-   瓶颈策略；
-   拆合批；
-   Stage重叠；
-   其它参数。

V1不建设第三套通用脚本语言。

## 16. 需求排序不做全局PriorityScore

每个计算层内部按：

> **有序规则段（Ordered Priority Segments） → 第一命中 → 段内排序。**

例如一层内可以出现：

1.  满足某条件的客户订单前10；
2.  满足某条件的补库指示；
3.  其它紧急客户订单；
4.  普通MTS。

不同订单类型可以穿插。

排序条件可以使用：

-   客户；
-   DueDate；
-   IssueDate；
-   库存消耗量；
-   DelayStatus；
-   OrderType；
-   其它已存在稳定业务事实。

**需求排序由2号位主流程执行，规则由3号位配置。**

不为排序再拆一个5号位运行时插件。

## 17. 插件最终原则

插件/独立服务只用于：

> **业务复杂度高、变化大、且确实需要由另一个责任位独立维护的计算。**

当前最典型的必要复杂服务是：

> **生产指示位置计算（PI Position Calculator）由5号位负责。**

下列逻辑留在主流程或1号位，不为了分工硬拆插件：

-   Demand排序执行；
-   Supply余额扣减；
-   PI按创建时间选择；
-   Inventory资格/优先消费；
-   FinalTask拆批/合批；
-   正倒排；
-   动态瓶颈；
-   Candidate影响传播。

V1不建设通用 `IPeggingRuleService` / 插件注册市场。

------------------------------------------------------------------------

# 第五部分 夜间FULL完整流程

## 18. 凌晨数据准备

夜间FULL沿用现有数据准备主链，包括：

-   活跃根需求；
-   Order_Canonical/Order快照；
-   Material/Factory/ProductFamily；
-   Resource；
-   Routing；
-   BOM展开；
-   StageDetail/CrossFactoryEdge；
-   Inventory；
-   MES WorkOrder/Operation/Stage进度；
-   采购/VMI/已到厂未入库等外部供给事实；
-   规则参数冻结版本。

**ScheduleRun/PlanVersion具体创建时序不在本文强迫2号位改写。**

只要满足：

-   本次运行拥有明确ScheduleRun；
-   每个Domain拥有独立PlanVersion；
-   数据与版本可追溯；
-   最终运行状态闭合；

即可由2号位按现有实现把握具体时序。

## 19. 夜间Domain编排

一次FULL_SCHEDULE：

-   一个ScheduleRun；
-   `ExpectedDomainKeysJson`冻结本次预期Domain集合；
-   每个Domain独立一个PlanVersion；
-   按Domain_Dependency拓扑顺序计算。

### 19.1 发布规则

-   无依赖的Domain独立发布；
-   一个无关Domain失败不得阻止其它Domain发布；
-   ScheduleRun允许 `PARTIAL_SUCCESS`。

### 19.2 上游Domain失败

若：

`B → A`

本次B失败，则：

-   B新PlanVersion=FAILED；
-   A本次不得发布新ACTIVE；
-   A继续保留上一ACTIVE；
-   与B无依赖的Domain继续正常发布。

这样保证真实依赖链内版本一致，但不恢复全域ALL_OR_NOTHING。

## 20. 失败后的人工重新计算

必须支持人工重新计算。

原则：

-   原FAILED ScheduleRun/PlanVersion永久保留；
-   不能把FAILED改回RUNNING；
-   人工重算创建新的ScheduleRun；
-   后台自动根据Domain_Dependency确定必要的失败上游和被阻断下游；
-   只重算必要依赖链。

例如：

`C → B → A`

凌晨：

-   C成功；
-   B失败；
-   A未发布；

修复B后：

> 新Run只计算 `B → A`，C不重复计算。

如果B真正失败原因来自C仍未恢复，则系统自动把C纳入本次重算链。

------------------------------------------------------------------------

# 第六部分 Pegging总流程

## 21. Pegging的职责

Pegging只回答：

1.  谁需要什么；
2.  哪些Supply承接多少；
3.  还缺多少；
4.  缺口是否形成新的生产需求。

Pegging不决定最终设备时间。

## 22. 计算层级

最终分层为：

### 第一层：顶层独立需求

包括：

-   普通销售订单（SALES_ORDER），但排除客户分类（CustomerSegment）=`INTER_FACTORY`的厂间出荷指示（SH）订单；
-   顶层MTS/SS/SS_U；
-   其它独立生产指示型需求。

SH本身虽然也是SALES_ORDER，但不作为普通第一层Demand重复进入；它在目标侧作为SH单一Supply。

### 第二层：SH源工厂履约需求

第一层普通Demand如果由SH承接，则当前层Demand→SH结束。随后对SH整单判断：

-   `CLOSED / CANCELLED`：退出活跃计算；
-   `OPEN`且跨厂在途视图存在该SH号：整张SH已进入跨厂履行，不再生成源厂履约Demand；
-   `OPEN`且跨厂在途视图不存在该SH号：形成一条完整源厂履约Demand，`RequiredQty = Order.Quantity`。

第二层使用`SourceFactoryId`重新归入源厂Domain，并继续按：

> **PI → PI Position → 只有最终缺口才形成新增生产**

推进。第二层不得按第一层Allocation份额，也不得按Transit / Received / ZP / BP拆分。

### 第三层及后续：PI/BOM下阶需求逐层扫尾

逐层展开直到：

-   被库存；
-   被PI；
-   被采购/VMI；
-   或形成新的生产需求；
-   或确认缺口。

不额外建设DemandCandidateGroup平台。

## 23. Demand与Supply双边余额

每次成功Allocation必须原子完成：

-   DemandBalance减少；
-   SupplyBalance减少；
-   生成稳定AllocationSequence；
-   形成Allocation记录/逻辑账本。

满足：

`DemandQty = AllocatedQty + RemainingDemandQty`

`SupplyQty = AllocatedQty + RemainingSupplyQty`

V1需要**逻辑原子账本能力**，但业务基线不强迫新增独立`PeggingAllocationLedger`物理表。

如果现有Allocation/PSA结构可以承接持久化，就优先复用，避免为了DTO存在再建新表。

## 24. 供给身份红线

同一物理数量同一PlanVersion中只能有一个Supply身份。

不能把：

-   PI总量；
-   PI内部XC；
-   PI内部在途；

重复当成三份Supply。

------------------------------------------------------------------------

# 第七部分 PI选择与PI位置

## 25. PI选择顺序

同一Material有多个PI时：

> **默认按创建/发行时间先后，尽量用完一个PI再进入下一个PI。**

不同物料之间没有PI排序问题。

PI是Demand承诺的最小生产指示单位。

## 26. 先选PI，再消费PI内部位置

例如：

`Demand A ← PI-01 500`

先建立PI级数量承诺，再判断500件内部位于：

-   Stage后待入；
-   XC；
-   在途；
-   未完成Stage。

禁止把各PI的Position全部打散后再全局排序。

## 27. 生产指示位置可能事实包

2号位负责给5号位装载：

> **生产指示位置可能事实包。**

2号位只做相对简单、宽范围的事实获取，例如：

-   当前PI基本信息；
-   当前物料相关StageProgress；
-   PI号相关库存余额；
-   相关XC库存；
-   相关跨厂在途；
-   Routing/Stage路径；
-   Received事实；
-   必要的工序/仓库属性。

2号位不需要先把复杂位置逻辑判断完再传给5号位。

否则5号位职责失去意义。

## 28. 5号位PI位置计算

5号位负责复杂判断：

-   PI总量闭合；
-   累计Stage差分；
-   XC/在途/Stage位置互斥；
-   强事实修正；
-   UNLOCATED；
-   数据质量Issue。

输出：

> PI内部互斥Position份额。

2号位再按Allocation数量消费这些Position。

## 29. PI级库存余额

V1继续保留PI号别库存余额能力。

ODS层由5号位生成/透出，APS层由2号位承接。

有些库存如果确实无法识别PI号，仍可使用普通库存链；但：

> **无PI号通用XC库存V1暂不处理。**

V1假定参与PI位置计算的XC都可以归属到具体PI。

## 30. M库与PI位置

PI位置是"尚未最终完成"的位置计算。

因此目标M库中的最终完成库存不属于PI Position。

M库只在外层Supply搜索中使用。

## 31. PI位置异常

### 31.1 MES下游完成量超过上游

默认保守下修下游。

但若XC/在途等更强物理事实证明必须已经完成上游，可以反向修正规划侧有效上游累计，并登记Issue。

### 31.2 中间Stage缺失

使用下游已证明的最小完成量进行保守推断。

### 31.3 无法闭合

剩余差额进入`UNLOCATED`。

若强事实互相冲突且无法判断哪条错误，可将该PI整体RemainingQty降级为UNLOCATED。

一个PI异常不能拖垮整个Domain。

### 31.4 UNLOCATED不能下发MES

APS可以从最早可信Stage开始保守占未来产能，但这种Task是规划占位，不能下发MES。

## 32. PI自消费红线

PI进入下层扫尾时：

> **同一个PI不能再作为自己的Supply消费自己。**

它只能触发自己的下阶BOM需求。

------------------------------------------------------------------------

# 第八部分 库存、采购、VMI和外部供给

## 33. 库存资格与优先级

库存先判断：

1.  是否有资格使用；
2.  仓库Priority；
3.  稳定排序。

库存资格原则由`InventoryAvailabilityRule`统一治理。

## 34. 采购/VMI与库存资格保持一致

外部供给不再建设一张"万能Pipeline规则表"。

采购/VMI的仓库使用资格原则上继承库存仓库资格。

同仓库内部按AvailableTime排序。

VMI为独立仓库，不与普通PO同仓库混为一个Supply类型竞争。

## 35. 人工ETA、ERP ETA与默认LT

采购预计可用时间优先级：

> **人工维护ETA \> ERP ETA \> 默认采购LT推算。**

人工ETA允许覆盖ERP早期回复日期，不设置自动失效期限；人工删除/取消后才回落ERP
ETA。

人工维护粒度：

> **采购单号 + 采购单项号 + 物料 + 收货仓库。**

默认采购LT：

-   不加ProductFamily维度；
-   收货仓库是重要维度；
-   因仓库编码全公司唯一，不需要重复再加Factory维度。

默认LT起算基准：

> 采购单正式下发日期。

如果按默认LT推算的日期已经早于当前日期，则按已冻结的延迟余裕参数向后修正，例如20%延迟余裕。

到货后可用Offset主要按收货仓库配置。

## 36. AvailableTime的职责

采购/VMI的EffectiveETA和AvailableTime应尽量在ODS/防腐事实层预先算好，由5号位维护真实业务事实，2号位直接装载消费。

1号位不重新计算采购ETA。

## 37. 采购供给内部排序

基本顺序：

1.  仓库资格；
2.  仓库Priority；
3.  同仓库按AvailableTime；
4.  PO发行时间；
5.  PO号+项号稳定排序。

已到厂未入库是更强的当前事实，在同一采购单内部优先于尚未到厂的未结余量。

同一来源内部坚持：

> **用完一条，再下一条。**

## 38. 无正式采购承诺时的规划性采购占位供给

当采购件：

-   库存无；
-   已到厂未入库无；
-   PO无；
-   VMI无；

V1允许在内存形成：

> **规划性采购占位供给（Planning-only Purchase Placeholder）。**

规则：

-   数量=当前缺口；
-   AvailableTime按当前时间/计划基准时间 + DefaultLT等冻结参数估算；
-   必须标记`ESTIMATED / NOT_COMMITTED`；
-   不生成采购单；
-   不生成制造Task；
-   不落成正式已承诺Supply；
-   不下发ERP。

如果CTP依赖该占位，只能给：

> **估算日期，而不是确定性承诺。**

正式PO/VMI出现后，下一次排程自动用真实供给替换估算占位。

------------------------------------------------------------------------

# 第九部分 两类跨厂业务

## 39. 大工艺接续型（Stage Handoff）

适用于同一PI沿大工艺跨厂继续生产。

正式供给顺序：

> **目标工厂可直接使用M → 选择PI → PI内部Position → 缺口新增生产。**

例如B厂需要C500：

-   B厂M 100；
-   PI-C01 300；
-   PI内部已到B厂100、在途120、XC80；
-   缺口100新增生产。

红线：

-   不单独把XC再算一份Supply；
-   不单独把在途再算一份Supply；
-   不搜索上游工厂普通M作为"可直接跨厂借用库存"。

## 40. 厂间出荷指示型（Inter-factory Shipping Instruction）

### 40.1 SH身份与单一Supply

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

不建设第二套SH主档。第一层普通Demand与SH允许多对多，由2号位运行时Pegging形成Allocation。Transit / Received / ZP / BP仅为同一SH履行事实，不形成独立Supply。

### 40.2 SH第二层整单源厂Demand

```text
CLOSED / CANCELLED
→ 退出APS活跃计算

OPEN + ERP_InterplantInTransit_View存在该SH号
→ 已进入跨厂履行
→ 不生成源厂履约Demand

OPEN + ERP_InterplantInTransit_View不存在该SH号
→ 生成一条完整源厂履约Demand
→ RequiredQty = Order.Quantity
```

查询成功但0行属于合法成功。跨厂在途视图Quantity不得用于`Order.Quantity - TransitQty`，也不得形成70/30数量切片。

第二层完整SH Demand使用`SourceFactoryId`重新归域，不改写原Order的`FactoryId`和目标侧身份；继续按：

> **PI → PI Position → 只有未被已有PI承接的缺口才形成新增生产。**

### 40.3 SH整单时间

同一SH号只能整单一次入库；如需分批交货，ERP必须拆成多个SH号。因此一张SH在目标侧只有一个整单AvailableTime。

SH订单交货期表示目标工厂要求到货时间，不是实际ETA。源厂真实准备完成时间（SourceReadyTime）由1号位有限产能求解产生；SH标准跨厂提前期由3号位参数治理；ERP真实发运/到达事实由5号位提供且优先于默认参数。

同Domain时在一次Domain Solve内表达源厂生产→跨厂LT→目标侧依赖；跨Domain时由2号位传播上游真实完成结果。

## 41. 跨厂物流V1最终红线

**绝不建设有限物流Task体系。**

V1只有：

`上游生产完成时间 + 跨厂运输/检验/转运LeadTime = 下游AvailableTime`

不建设：

-   ShippingTask有限资源；
-   车辆；
-   班次；
-   月台；
-   物流有限产能Solver。

------------------------------------------------------------------------

# 第十部分 Demand Protection、严格绑定和执行事实

## 42. 三类锁必须区分

### 42.1 严格绑定（Strict Binding）

例如：

-   客户专属；
-   真实存在的业务单据强绑定证据；
-   质量/环保/资格专属。

普通Demand不可抢占。

### 42.2 需求保护锁（Demand Protection）

用于保护某些必须优先确保的Demand。

触发条件全部应由规则引擎配置，例如：

-   剩余交期短于正常LT；
-   已经延期过；
-   特定客户等级；
-   其它业务保护条件。

一旦触发，保护的是：

> **该Demand真正必要的供给数量份额。**

不是把整个PI/整个PO一刀切全部锁死。

### 42.3 执行不可逆（Execution Constraint）

真实发生的：

-   已消耗；
-   已领料；
-   最终直接履约Task已开工；
-   其它不可逆执行事实；

不能在新版本中重新解释。

## 43. Demand Protection的数据模型

V1不再为Demand Protection单建一套大平台。

建议在现有需求---供给锁实体中增加/使用`LockType`区分：

-   `STRICT_BINDING`；
-   `DEMAND_PROTECTION`。

ExecutionLock仍单独表达MES现实执行事实。

## 44. 下阶通用PI已经开工，不等于必须锁给原Demand

例如通用零件PI-C01已经MACH开工。

如果它只是下阶公共零件：

> 开工只证明这批零件必须继续生产，并不证明它必须属于昨天分到它的订单B。

只要没有：

-   严格绑定；
-   Demand Protection；
-   已实际消耗；
-   最终直接履约不可逆；

它仍可以在下一版被高优先订单重新Pegging。

相反，如果PI本身就是订单A最终成品装配，且已齐套/已开工，则对应关系可能已不可逆，不能随意换给订单B。

------------------------------------------------------------------------

# 第十一部分 夜间重新Pegging与跨版本原则

## 45. 普通Allocation不保留跨版本关系稳定性偏好

夜间FULL每次按最新：

-   Demand；
-   Supply；
-   Demand排序规则；
-   Lock；
-   执行事实；

重新Pegging。

除严格绑定/保护/不可逆等关系外：

> **昨天订单A分到PI-01，不代表今天A天然优先继续保留PI-01。**

新高优先需求可以重新竞争普通Supply。

## 46. 已下发/已开工的最终直接履约Task

如果已经形成最终直接履约、继续更换Demand会破坏真实生产执行，则必须保持。

锁定的是现实，不是历史Allocation关系本身。

------------------------------------------------------------------------

# 第十二部分 2号位交给1号位的正式求解上下文

## 47. 2号位不能只传Task列表

2号位必须给1号位完整问题，包括：

1.  Run/PlanVersion/Domain/Horizon；
2.  逻辑生产需求；
3.  Net Required Qty；
4.  Planned Process Qty；
5.  Demand业务优先关系；
6.  Allocation血缘；
7.  完整Routing候选图；
8.  Operation Dependency；
9.  Resource Eligibility；
10. Resource Calendar；
11. 已占用时间；
12. 物料Quantity + AvailableTime；
13. Firm/Frozen/Lock/执行固定事实；
14. 正倒排/瓶颈/拆合批/Setup/Stage重叠等冻结策略。

## 48. FinalTask由1号位产生

1号位决定：

-   最终Operation级Task；
-   实际设备；
-   最终批次；
-   Start/End；
-   真实Task依赖；
-   TaskShare。

2号位现有提前Task生成路径退出正式V1主链。

## 49. 一个Task可以承接多个Demand

允许：

-   一个Allocation拆成多个FinalTask；
-   一个FinalTask合并多个Allocation份额。

真实业务归属由`AllocationTaskShare`等数量映射表达。

因此Task不应再把单一`OrderId`视为最终唯一业务归属真相。

如果现有Task.OrderId字段难以立即删除，可保留兼容/代表性值，但不能作为唯一业务关系来源。

## 50. Task数量字段

建议：

-   `Task.Quantity`继续表示净合格产出语义；
-   增加/明确`PlannedProcessQty`表示实际计划加工量。

资源负荷使用PlannedProcessQty。

Allocation闭合使用净合格Quantity。

------------------------------------------------------------------------

# 第十三部分 1号位有限产能求解器最终业务边界

## 51. 一套Solver

V1只保留：

> `IFiniteCapacityScheduler.SolveAsync(...)`

正式单入口。

旧`FiniteCapacitySolver`第二条求解链退出。

PassThrough只允许测试/过渡，不能作为正式V1能力。

## 52. Routing与替代设备

2号位负责告诉1号位：

> 哪些Routing/Operation/Resource是合法候选。

1号位负责：

> 最终选哪条合法路径、哪台合法设备。

V1正式支持同一标准化Material存在多条合法Routing。A/B固定既有真实RouteCode+PathId；C桶进入Solver前保留全部合法RoutingCandidates，由1号位对每个Execution Batch选择一条完整Path。

## 53. 动态瓶颈

Stage不永久写死为瓶颈。

默认采用：

> **Load/Capacity动态识别 + 人工策略覆盖。**

3号位可配置：

-   AUTO；
-   PREFER_ANCHOR；
-   FORCE_ANCHOR；
-   NOT_ANCHOR。

V1只做简单可靠的瓶颈识别和锚定，不做复杂数学多瓶颈全局最优。

## 54. 正排与倒排

正排/倒排是同一个Solver内部的时间槽搜索策略，不是两套Solver。

V1支持：

-   Forward；
-   Backward；
-   Mixed/Auto。

Direction不得由`OrderType`直接决定或建立固定映射。正式决策上下文必须综合：

- Demand Goal；
- RequiredAvailableTime；
- Slack；
- Material可用时间；
- Resource/Calendar；
- 既存Execution连续性；
- Firm/Frozen/Lock。

3号位可以保留ProductFamily、Factory、Stage以及OrderType等策略辅助条件，但`OrderType`只能作为非决定性条件之一，不能单独映射为FORWARD/BACKWARD/MIXED；必要时可有Material级例外策略。

## 55. 求解器固定外层阶段

V1采用固定外层：

1.  建立硬约束；
2.  初始有限产能构造；
3.  可行性与延期诊断；
4.  有界局部修复；
5.  Gap Compaction与最终评价。

不能把整个流程做成用户可配置任意Command链。

## 56. 拆批与合批

区分：

-   强制物理拆批；
-   优化性拆批。

优化拆批只尝试少量候选，例如：

-   不拆；
-   2批；
-   3批。

不能无限组合。

允许不同Demand在有限产能和交期允许时合并为一个Task，但必须保留需求份额。

## 57. Setup/换型

Setup是**当前Task在当前Resource上的产品转换时间**，是真实资源时间占用，不是排序分数。

V1正式业务键：

```text
ProductionDepartment
+ Stage
+ 当前Task.OperationCode
+ 当前Resource
+ FromMaterial
+ ToMaterial
→ SetupMinutes
```

其中前一相邻Task只贡献`FromMaterial`；当前Task提供`OperationCode + ToMaterial`；上一Task的Operation不参与Setup命中；规则具有方向性。

**前产品不要求具备当前Task的Operation资格。** 只要前一Task已经真实排在该Resource上，就可以提供`FromMaterial`；当前Task自身仍必须满足当前Operation对该Resource的`OperationResourceEligibility`。

如果当前Resource在可追溯计划/执行上下文中**没有任何上一产品状态**，按初始设备状态处理：

```text
Setup = 0
ExplainTrace = INFO / INITIAL_SETUP_STATE
```

存在上一产品时，运行命中顺序只允许：

```text
EXACT
→ DEFAULT（Department+Stage+Operation+Resource）
→ 0分钟 + ExplainTrace WARNING / SETUP_RULE_MISSING_ZERO_FALLBACK
```

EXACT与DEFAULT均缺失的0分钟是规则数据质量兜底，必须与“初始设备状态”区分，并进入缺失规则查询。

同产品连续默认Setup=0，除非显式维护`A→A` EXACT规则。

正式禁止SetupAttribute/Mold/Tool/Color作为V1运行前提，禁止MES或`RoutingOperation.SetupTime`作为运行真相或fallback，禁止FromOperation+ToOperation Setup键、统一固定分钟和相似产品推测。

Setup必须真实进入Resource Timeline、Task Start/End、资源冲突和交期评价，不单独生成MES Setup工单或可下发Setup Task。

初始Resource/时间槽候选评价必须同时考虑：

```text
前置Setup + 当前Task Processing + 插入后后继Task新Setup
```

Task插入/删除/移动/交换后，受影响两侧邻接Setup全部重算。

### 57.1 夜间FULL Setup优化

FULL按：

```text
Resource
× 最大连续可用生产窗口
× 固定锚点之间可移动段
```

形成候选集，在搜索预算内做有界邻域优化。

连续可用生产窗口由Resource Calendar定义，不按自然日00:00切断；窗口边界不重置设备上一产品状态。

禁止全厂TSP、90天全排列或无界搜索。只有硬约束、业务优先、Demand Protection和客户履约不恶化时，才接受Setup更优序列。

### 57.2 白天Candidate

白天继续局部优先，只对变化Seed传播范围内受影响序列重算Setup，不因为Setup增强把每次Candidate变成全天洗牌。

### 57.3 规则冻结与解释

Setup业务规则属于`RuleSetVersion`，Setup搜索预算等算法参数属于`ParameterSetVersion`。两者必须在Run开始时一次性进入FrozenStrategySnapshot/等价冻结上下文，由2号位按Domain裁剪后一次性交给1号位；Solver运行中不得在线回查3号位。

正常Setup命中、DEFAULT、初始设备状态、规则缺失0分钟兜底优先写`ExplainTrace`/等价Task级轻量追踪，不新增Setup专属`ScheduleExplanationFact.ReasonCode`。

## 58. Stage重叠与转运

允许配置：

-   Transfer Batch；
-   Stage重叠；
-   Stage间固定转运LT。

下游可在达到规定数量门槛后提前开始，不要求整批全部完成。

------------------------------------------------------------------------

# 第十四部分 有限产能目标函数最终层级

## 59. 第一层：硬约束

绝不能违反：

-   资源冲突；
-   工艺前后依赖；
-   Material AvailableTime；
-   Resource Eligibility；
-   Firm/Frozen；
-   Lock；
-   已执行事实；
-   数量闭合。

## 60. 第二层：履约优先

Demand Protection和业务优先级属于硬层级。

高优先业务不应为了Setup、利用率等被低优先业务牺牲。

## 61. 第三层：客户承诺确保率目标

客户订单整单按期率可配置目标，例如95%或其它业务值。

如果存在满足该目标的可行方案，就不能为了次级优化选择明显更差的按期率方案。

如果物理上无法达到目标：

> Solver必须返回最佳可行计划，而不是报"计算失败"。

## 62. 第四层：次级优化

包括：

-   总延期时长；
-   Lead Time；
-   WIP；
-   Setup；
-   利用率；
-   计划稳定性；
-   避免过早生产。

V1优先使用可解释的目标顺序，不设计大量难以解释的小数权重。

------------------------------------------------------------------------

# 第十五部分 排程终点与交期回答

## 63. V1排程终点

客户订单V1的正式完成点：

> **最终生产/装配完成并进入目标ZP/BP成品仓。**

外部客户运输不进入V1有限产能。

未来如需固定出货LeadTime，可以在最终完成时间后追加确定性时间，但不属于1号位有限资源Task。

## 64. 客户最终交期、延期与解释由谁回答

1号位通过真实有限产能求解得到FinalTask时间；2号位通过`AllocationTaskShare`/Allocation把最终时间回溯到Order，形成：

> **当前PlanVersion下客户订单APS预计完成时间。**

订单是否计划延期的唯一业务判定：

```text
APS预计完成时间 > CustomerDueDate
```

即为延期；延期时长为二者差值。

不得用`Order.DelayStatus`、Risk字段、简单Lead Time、`RequiredAvailableTime`或任意中间Task时间替代当前PlanVersion的正式交付结果。

正常有限产能“能力不足”不是普通意义上的“排不下”；Solver应继续向后寻找真实可行时间。若最终时间超过CustomerDueDate，就是计划延期。主数据缺失、无合法Eligibility或数据不可计算才属于异常。

只要正式计划已经延期，V1必须存在可解释的真实主要原因，至少达到：

```text
原因类别 + 具体影响对象
```

原因可来自Resource、Material、Predecessor、Calendar、Equipment Unavailable、Firm/Frozen/Lock、Setup、Cross-factory Time等真实约束。

`RequiredAvailableTime`只用于内部约束链解释，不是客户订单延期判定标准。

采购件不需要制造Task，但必须真实参与最终交期判断。

------------------------------------------------------------------------

# 第十六部分 白天业务场景与Candidate

## 65. Candidate共同运行原则

所有白天Candidate继续满足：

- Base正式计划只读；
- 新建ScheduleRun；
- 新建Candidate PlanVersion；
- Candidate严格单Domain；
- 只使用现有合法RunType/Purpose组合；
- 不建设第二套Solver；
- 不建设Scenario平台。

每次Candidate创建时：

```text
BasePlanVersionId
=
本次目标Domain当时唯一ACTIVE PlanVersion.Id
```

禁止：

```text
BasePlanVersionId = CANDIDATE
```

即：

> **V1禁止Candidate叠Candidate。**

允许多个Candidate同时基于同一个ACTIVE A生成并存用于人工比较。但如果其中Candidate B先被确认并成为新的ACTIVE，则其它仍以旧ACTIVE A为Base的Candidate不得再直接激活，必须重新基于当前ACTIVE B和当前最新真实事实运行。

## 66. 白天业务场景矩阵

| 序号 | 业务场景 | 一次允许对象 | RunType + Purpose | PriorityMode | 是否可直接激活 |
|---|---|---|---|---|---|
| 1 | 新订单正常承诺评估 | 多个已进入标准订单事实的新订单 | INSERT_ORDER_WHATIF + CTP | NORMAL | 否 |
| 2 | 新订单急插影响分析 | 多个明确急单订单 | INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS | EXPEDITE | 否 |
| 3 | 按CTP结果正式插入 | 本次明确选择的新订单 | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | NORMAL | Candidate确认后可激活 |
| 4 | 按急插分析结果正式插入 | 本次明确选择的新订单 | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | EXPEDITE | Candidate确认后可激活 |
| 5 | 已有订单提前——正常模式 | 多个订单 + 每单独立目标日期 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL | Candidate确认后可激活 |
| 6 | 已有订单提前——急单模式 | 多个订单 + 每单独立目标日期 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | EXPEDITE | Candidate确认后可激活 |
| 7 | 甘特图人工拖拽调整 | 一个或多个Task + 各自目标时间 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | 固定NORMAL | Candidate确认后可激活 |
| 8 | 设备故障后的局部重排 | 一台或多台已正式不可用设备 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | 固定NORMAL | Candidate确认后可激活 |
| 9 | 资源日历正式调整后的局部重排 | 一台或多台已正式修改Calendar的设备 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | 固定NORMAL | Candidate确认后可激活 |
| 10 | 整个当前Domain人工重排 | 不选局部对象，直接当前Domain | MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT | 按既有冻结优先规则 | Candidate确认后可激活 |

正式删除：

> **“其它明确对象局部调整”万能入口。**

一次`LOCAL_RESCHEDULE`只能有一个业务发起场景类型，但该场景允许一次选择多个同类型对象。

同一Run中的多个对象必须属于同一个目标Domain；跨Domain对象必须拆成多个单Domain Run。跨Domain CTP继续按现有Domain依赖拓扑串行多个单Domain WHATIF后汇总客户答案。

## 67. Candidate运行输入与变化Seed

`ScheduleRun.ScopeJson`不再按“固定11字段绝对不变”理解。

V1正式升级为：

> **Candidate运行输入Schema v2。**

物理字段仍为现有`NVARCHAR(MAX)`，不新增CandidateInput表，不新增PriorityMode/BusinessTriggerType物理列。

新Run至少可表达：

- SchemaVersion；
- BusinessTriggerType；
- PriorityMode；
- 多订单目标集合；
- 多Task目标集合；
- 多故障/Calendar变化资源集合；
- 原ScopeJson中仍适用的范围字段。

其中：

```text
变化对象 = 本次影响传播Seed
```

而不是：

```text
Solver事实过滤器
```

因此当前正式生效的：

- Resource Calendar；
- 设备可用/不可用；
- Resource Eligibility；
- Routing；
- Setup；
- Material AvailableTime；
- 当前资源占用；
- Firm/Frozen；
- 已执行不可逆事实；
- Demand Protection；
- 其它Domain ACTIVE共享资源阻挡；

仍必须完整进入求解。

同时，本次没有被明确选择的其它新订单，不因为“当前真实事实完整”而自动进入本次业务场景对象集合。

## 68. 各业务场景的运行语义

### 68.1 CTP

CTP使用：

```text
INSERT_ORDER_WHATIF + CTP
PriorityMode = NORMAL
```

定义：

> 针对已经进入APS标准订单事实的新订单，在正常业务优先规则下，使用真实Pegging、Material AvailableTime、Eligibility、Calendar和有限产能计算最早可承诺完成时间，并说明对当前计划的真实影响。

CTP不是只查空档，也不是“完全不允许影响当前Domain可移动计划”。

CTP Candidate不得直接激活。

正式采用CTP结果时必须重新执行：

```text
当前最新ACTIVE
+ 当前最新真实事实
↓
LOCAL_RESCHEDULE + INSERT_RESCHEDULE
PriorityMode = NORMAL
↓
新的可激活Candidate
```

### 68.2 急插影响分析

使用：

```text
INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS
PriorityMode = EXPEDITE
```

EXPEDITE只作用于本次明确选择的新订单及其派生需求链，不得突破：

- 已执行不可逆事实；
- Firm/Frozen；
- Strict Binding；
- Demand Protection；
- Material AvailableTime；
- Routing；
- Eligibility；
- Business Scope；
- 其它Domain ACTIVE不可移动资源占用。

正式采用时同样重新基于当前ACTIVE创建：

```text
LOCAL_RESCHEDULE + INSERT_RESCHEDULE
PriorityMode = EXPEDITE
```

### 68.3 已有订单提前

允许一次选择多个订单，并为每个订单输入不同人工计划目标日期。

例如：

```text
订单A → 9月20日
订单B → 9月22日
订单C → 9月25日
```

人工计划目标日期只属于本次Candidate，不修改ERP/Order_Canonical正式DueDate。

NORMAL模式下，它只是本次有限产能求解目标，不得用来偷换正式Demand排序；需要提高业务优先层级时必须显式使用EXPEDITE。

### 68.4 甘特拖拽

甘特图允许一次提交多个Task及各自目标时间。

固定：

```text
LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
PriorityMode = NORMAL
```

拖到08:00表示软目标。08:00不可行时Solver可以返回08:30并通过Explanation说明原因。

禁止拖拽后直接UPDATE正式Task，也禁止通过拖拽把订单偷偷变成急单。

### 68.5 设备故障

一次可选择多台**已经正式不可用**的设备作为变化Seed。

故障必须先成为当前真实资源事实，再发起Candidate。

系统不直接指定“A换B”；1号位仍从全部合法Eligibility候选中结合Calendar、Capacity、Setup和现有占用选择资源。

### 68.6 Resource Calendar调整

一次可选择多台已经正式修改Calendar的资源作为变化Seed。

V1不支持“Calendar不改，先假设加班2小时”的Candidate专属假设Calendar。

### 68.7 Business Scope

局部Candidate不得越过当前用户Business Scope。

Scope外Task：

- 不得被本次Candidate移动；
- 仍作为真实资源/时间占用进入Solver。

如果真实可行方案必须移动无权Task，返回“当前Business Scope不足，需要更高范围用户重新发起”。

# 第十七部分 白天局部有限产能重排

## 69. 影响范围由1号位动态判断

2号位先把本次一个业务场景中的一个或多个同类型对象转换为变化Seed；1号位从这些Seed开始传播。

变化Seed可以是：

- 新增/变化的LogicalProductionDemand；
- 一个或多个Task软目标；
- 一台或多台故障Resource；
- 一台或多台Calendar变化Resource。

Seed只决定传播起点，不限制Solver读取当前完整真实上下文。

不按：

-   产品族；
-   固定7天/14天；
-   固定订单数；

硬截断。

主要传播关系：

1.  工艺前后依赖；
2.  物料Quantity-Time关系；
3.  共享有限资源时间轴；
4.  Setup邻接关系。

## 70. 待检查队列（Dirty/Affected Queue）

基本算法：

1.  变化种子入队；
2.  尝试最小扰动重新安排；
3.  如果Resource/Start/End/Quantity未变化，不继续传播；
4.  如果变化，把真正受影响的上下游、物料消费者、资源邻居加入队列；
5.  直到队列为空。

这是1号位固定算法，不进入规则引擎。

## 71. 最小扰动优先

1号位优先尝试：

1.  保留原资源；
2.  保留附近时间；
3.  找无须移动其它Task的空档；
4.  少量优选替代设备；
5.  必要时才挤动低优先Task、拆批、换序。

## 72. 局部重排工程默认值

真实压测前先采用经验默认：

-   正常Candidate目标：60秒；
-   软时间预算：90秒；
-   局部模式硬时间预算：180秒；
-   受影响Task比例阈值：30%；
-   单Task最大局部修复：5次；
-   全局局部修复轮数：10轮；
-   单Operation替代资源初筛：5个；
-   优化性拆批：最多3种；
-   不设置固定影响天数。

这些是Solver技术参数，不是业务规则。

## 73. LOCAL的单Domain完整重排兜底

当：

-   影响范围太大；
-   局部修复不稳定；
-   时间预算逼近；
-   主瓶颈大面积改变；

求解范围可以自动扩大为：

> **当前单Domain全部可移动Task完整有限产能重排。**

但本次业务Run仍然是：

```text
LOCAL_RESCHEDULE
```

不能因为影响扩大而自动改成`MANUAL_RESCHEDULE`。

`MANUAL_RESCHEDULE`只用于用户从一开始就明确选择“整个当前Domain全部可移动计划重新排”。

仍使用同一Solver。

仍然固定：

-   已执行；
-   Firm/Frozen；
-   Demand Protection；
-   不可逆关系。

------------------------------------------------------------------------

# 第十八部分 白天共享资源与跨Domain插单

## 74. 白天单Domain Candidate与跨Domain共享设备

如果当前Candidate Domain与其它Domain共享同一设备/资源：

> **V1不允许为了本Domain插单去挤动其它Domain当前ACTIVE计划。**

其它Domain ACTIVE在共享资源上的占用，作为不可移动资源阻挡块。

这样保证：

-   Candidate仍严格单Domain；
-   不引入跨Domain影响传播；
-   不需要资源配额/借用平台。

夜间FULL仍按全局Domain依赖与真实共享资源现实形成正式计划。

## 75. 跨Domain插单CTP

如果最终产品Domain A依赖B、C：

> Candidate仍一个Domain一个版本。

后台按Domain_Dependency拓扑依次：

`C WHATIF → B WHATIF → A WHATIF`

逐层传递：

> Material + Quantity + AvailableTime。

最终对销售/PMC只返回一个CTP答案。

V1不建设MultiDomain Candidate或多域原子激活组。

## 76. 跨Domain Quantity-Time必须保留分段

不能只汇总成：

> 100件，AvailableTime=17号。

如果实际是：

-   40件15号可用；
-   60件17号可用；

必须保留两个数量---时间切片，以便下游允许分批启动时使用。

跨Domain转运时间使用真实工厂/Stage LeadTime，不再硬编码统一"2天"。

------------------------------------------------------------------------

# 第十九部分 插单产品输出

## 77. CTP不能只回答"能/不能"

一次插单Candidate至少返回：

1.  是否满足目标交期；
2.  最早可承诺完成时间；
3.  是否依赖ESTIMATED采购占位；
4.  受影响订单数量；
5.  哪些订单仍按期；
6.  哪些订单延期及最大影响；
7.  是否触碰Demand Protection；
8.  主要瓶颈；
9.  主要原因。

例如：

> 新订单U可于8月14日10:00完成，满足交期；影响7张现有订单，其中5张仍按期，1张后移1天，1张后移3天；无受保护订单被破坏；主要瓶颈为MC03。

如果依赖规划性采购占位：

> 必须明确显示"估算日期，非确定性承诺"。

## 77A. V1统一用户输出能力

V1必须让业务用户直接看清：

1. 甘特图（ACTIVE / Candidate）；
2. 客户订单CustomerDueDate、APS预计完成时间、是否延期、延期时长、主要原因；
3. Task → Demand / Order及数量；
4. Order / Demand → Supply、PI、采购、跨厂、新增生产缺口和PI Position；
5. Candidate统一Change / Impact / Reason；
6. 当前PlanVersion、生成时间、DataCutoffTime、触发方式、ACTIVE/Candidate、Base及必要Run信息；
7. 历史PlanVersion下对应Pegging关系；
8. 今日/未来FinalTask与APS Task → MES下发/工单/执行状态；
9. 由上述正式事实组合形成的基础业务战报/工作台。

Candidate统一输出至少包含Change、Impact、Reason三组业务结果，不另建第二Candidate Diff真相。

### 77A.1 V1.1后移但V1保留底座

以下后移V1.1，不作为V1首发完成条件：

- Resource计划负荷率；
- Setup KPI；
- 执行偏差风险；
- 完整Version Diff；
- 自动“为什么昨天和今天不同”；
- Explanation完整性自动告警；
- 工作台深度统计与管理汇总。

但V1必须保存未来自然需要的Setup、Explanation、PlanVersion、Pegging历史事实。

> **后移统计，不后移事实底座。**

成熟Gantt/Schedule/Candidate Diff代码不因职责形式统一强制迁移；Candidate Diff只允许一套业务真相；2号位不得因用户输出新增第二套报表平台。

------------------------------------------------------------------------

# 第二十部分 Candidate采用与审批边界

## 78. WHATIF永远不能直接成为正式计划

CTP / INSERT_IMPACT_ANALYSIS Candidate只读分析，**不得直接激活**。

业务决定采用WHATIF结论时，不是把原WHATIF Candidate改成ACTIVE，而是：

```text
当前最新ACTIVE
+ 当前最新真实事实
↓
重新创建LOCAL_RESCHEDULE + INSERT_RESCHEDULE
↓
按NORMAL或EXPEDITE重新求解
↓
形成新的可激活Candidate
```

这样保证从评估到正式采用之间发生的真实故障、Calendar、订单、物料和执行事实不会被旧WHATIF快照覆盖。

## 79. LOCAL/MANUAL Candidate确认、Base校验与审计

LOCAL_RESCHEDULE / MANUAL_RESCHEDULE形成的可激活Candidate仍需要最小人工授权确认。

激活前必须再次校验：

```text
Candidate.SourceScheduleRun.BasePlanVersionId
=
当前目标Domain现行ACTIVE PlanVersion.Id
```

如果Base已经过期：

> **拒绝直接激活，要求重新基于当前ACTIVE生成Candidate。**

因此允许：

```text
ACTIVE A
→ Candidate B
→ Candidate C
```

B/C并存比较；

但如果B先成为ACTIVE，则C不能继续直接激活。

V1只保留：

- 谁确认；
- 何时确认；
- 采用哪个Candidate；
- BasePlanVersionId；
- 激活操作审计。

不建设完整OA/BPM审批平台。

------------------------------------------------------------------------

# 第二十一部分 Firm/Frozen与跨版本保持

## 80. 不建设FrozenZoneSnapshot平台

FrozenZoneSnapshot当前无外部消费者，V1停止继续建设。

但Firm/Frozen语义必须存在。

## 81. 跨版本Firm/Frozen与真实执行连续性承接

夜间新PlanVersion会生成新TaskId，因此不能依赖旧TaskId永久保持锁。

2号位从上一ACTIVE读取仍有效的Firm/Frozen执行事实/业务事实，转换成：

> **本次1号位的不可移动锚点约束。**

普通未锁Allocation仍可重新竞争；真实MES开工中（IN_PROGRESS）执行则属于现实生产事实。正式顺序：

```text
当前LogicalProductionDemand Q
↓
消费5号位标准化IN_PROGRESS执行事实
↓
逐MES工单形成Continuation执行上下文
（每个MESWorkOrderNo可形成1..N个Slices，同工单Slices共享一个ContinuationKey）
↓
Continuation Slices + Free Slices
↓
共同进入1号位Solver
```

不得先用旧TaskNo/MES工单剩余量决定Q。已有APS Task连续时可继承TaskNo/MESWorkOrderNo；无TaskNo外部MES工单在FinalTask后由2号位新建APS TaskNo并绑定原MESWorkOrderNo，不创建第二张MES工单。

------------------------------------------------------------------------

# 第二十二部分 MES下发与执行反馈

## 82. MES下发资格

只允许：

-   正式ACTIVE版本；
-   有正式生产指示/合法执行身份；
-   满足发布窗口和业务资格；
-   非Candidate；
-   非UNLOCATED规划占位；
-   非无PI虚拟占位；

的Task进入MES下发。

## 83. MES五态

MES仍采用既有五态：

-   0 待开工；
-   1 开工中；
-   2 完工报工；
-   3 未完工报工；
-   4 未完工报工已完结（手动完工）。

V1不新增PAUSE/RESUME闭环。

设备故障只形成资源不可用事实、影响评估、Explanation与人工重排建议，不自动暂停/恢复Task。

### 83.1 MES工单状态（WorkOrderStatus）

跨版本连续性识别使用既有工单状态字段`WorkOrderStatus`：

- 待开工（`RELEASED`）；
- 开工中/已开工未完成（`IN_PROGRESS`）；
- 已完工（`CLOSED`）；
- 已删除（`DELETED`，本轮新增状态值）。

`RELEASED`不形成执行连续；`IN_PROGRESS`在存在当前有效Q时可形成连续份额；`CLOSED`只进入完成事实/PI Position；`DELETED`结束旧执行身份。

不得改名为`NOT_STARTED / STARTED_INCOMPLETE / COMPLETED`等新技术值。

### 83.2 工单级与工序级字段边界

工单级不新增`RemainingQty / RemainingNetQty / LastReportResourceCode / ExecutionStatus`。工序级继续使用`GoodQty`，Stage级继续使用`GoodCompletedQty`。

本轮新增工序级最后报工设备：

```text
MES_APS_OperationProgress_View.LastReportResourceCode
OperationProgressSnapshot.LastReportResourceCode
```

它表示该MES工单的具体工序最后一次有效报工设备，只作为资源连续性偏好，不提升为整张工单唯一设备，也不单凭该字段形成Hard Lock。

## 84. 次日重新计算

MES执行事实在次日进入新的数据快照。夜间FULL统一顺序：

1. 5号位标准化MES工单、工序、Stage事实并计算PI Position / NextOperationContext；
2. 2号位按当前事实重新形成Demand/Supply与Pegging，得到当前LogicalProductionDemand Q；
3. 2号位再消费标准化IN_PROGRESS事实，逐MES工单形成连续份额；
4. 连续份额与自由份额一起进入1号位；
5. 1号位形成新的FinalTask并回传ContinuationKey；
6. 2号位治理TaskNo / MES工单身份并发布新版本。

禁止让旧TaskNo或MES工单数量反向决定本次Q。

普通Demand↔PI关系不跨版本强行保持。

------------------------------------------------------------------------

# 第二十三部分 Explanation与异常

## 85. Explanation必须由1号位原生产出

1号位知道真实原因：

-   MATERIAL_SHORTAGE；
-   RESOURCE_CAPACITY_WAIT；
-   PRECEDENCE_WAIT；
-   FROZEN/LOCK；
-   LOGISTICS_DELAY；
-   RESOURCE_BREAKDOWN等。

2号位只能把Task级事实通过AllocationTaskShare聚合到Order，不重新发明原因算法。

## 86. DUE_DATE_RISK不是根因

`DUE_DATE_RISK`只能说明结果有交期风险。

真正解释必须指出：

-   哪个物料；
-   哪台资源；
-   哪个前序；
-   哪个锁；
-   哪个上游Domain；

真正决定了最终时间。

## 87. 约束事实不能机械相加成"延期原因总小时"

如果SURF等待20h、PO晚到20h、ASSY等28h，但最终只晚16h：

不能说总原因=68h。

必须沿最终关键路径识别实际Binding Constraint。

## 88. 数据异常与算法错误分开

### 可降级继续

-   某PI位置不清；
-   Stage数据缺失；
-   某些MES事实矛盾；

处理：

> 保守修正/UNLOCATED + Issue + Domain继续。

### 必须使Domain失败

-   Demand/Supply负数；
-   同一Supply重复消费；
-   Allocation越界；
-   TaskShare不闭合；
-   非法Resource；
-   事务只成功一半；
-   跨PlanVersion错误引用。

这类不能Warning后继续发布。

无法按客户交期完成不是算法失败，而是正常业务结果。

------------------------------------------------------------------------

# 第二十四部分 数据库与表结构简化红线

## 89. 表与主外键原则

V1优先：

> **少表、少耦合、少跨表主外键，但保证业务主键和运行正确性。**

不为了理论完整性把大量辅助对象都物理化。

## 90. FrozenZoneSnapshot

-   表即使已存在也不继续建设正式生成链；
-   无外部消费者；
-   V1由Firm/Frozen约束输入 + ExecutionLock替代。

## 91. VirtualInventoryBalance

-   V1不持久化；
-   跨Domain只在内存传递Quantity + AvailableTime。

## 92. PeggingSupplyAllocation（PSA）

当前作为APS内部非Task供给分配结果存在，可保留极简镜像以保护现有代码。

原则：

-   不作为第二套业务真相源；
-   不建立独立生命周期；
-   不重复事务内/事务外写两次；
-   若最终全代码搜索确认无外部报表/MES消费者，可进一步与统一Allocation结果整合。

## 93. PI Position表

V1不建设Header + Slice复杂平台。

既然需要保存位置快照，采用：

> **一张最小PI位置快照表，多行表达Position。**

最小业务字段围绕：

-   Plan/Schedule上下文；
-   PI号；
-   PositionType；
-   Stage/NextStage；
-   Quantity；
-   AvailableTime；
-   Source/Issue；
-   快照时间。

不增加独立Header生命周期。

## 94. Pegging逻辑账本

Demand/Supply原子Allocation必须有逻辑Ledger能力。

但：

> **本文不强制新增独立PeggingAllocationLedger物理表。**

如果现有Allocation/PSA/TaskShare已经足以落地并追溯，优先复用。

## 95. 审批相关表

旧DDL即使已有多级审批表，也不代表V1必须实现完整审批。

没有2号位代码主链依赖时：

> 不把审批表反向变成V1必做功能。

------------------------------------------------------------------------

# 第二十五部分 与2号位现有代码的最终兼容方案

## 96. 必须保护的现有代码骨架

优先保留：

-   `SchedulingOrchestrator`总入口；
-   Hangfire调度；
-   PlanVersion/ScheduleRun现有框架；
-   ScheduleContext装载框架；
-   `SupplyPool.RemainingQty`余额原型；
-   StageProgressSnapshot装载；
-   `IFiniteCapacityScheduler.SolveAsync`接口；
-   DomainSolveRequest/Result方向；
-   AllocationTaskShare DTO方向；
-   统一事务持久化模板。

## 97. 必须退出正式主链的旧路径

-   2号位提前生成最终Task；
-   DefaultBatchSplitter作为5号位/2号位预拆批主链；
-   外层旧`FiniteCapacitySolver.Solve()`第二次求解；
-   PassThrough作为正式生产Solver；
-   FrozenZoneSnapshot生成链；
-   VirtualInventoryBalance正式持久化；
-   事务外PSA二次写入；
-   运行期永久UPDATE `InventoryBalance.AllocatedQty`；
-   Pegging失败Warning后继续正式排程。

无论某个旧类当前是"实际调用"还是"仅注入未调用"，最终V1正式链都只能保留上述新口径。

## 98. 2号位必须补齐的核心能力

### P0/P1

1.  DemandBalance；
2.  SupplyBalance原子执行；
3.  AllocationSequence在分配成功时生成；
4.  Lock进入Pegging循环；
5.  Allocation/逻辑Ledger实际生成；
6.  Candidate未锁Supply重新竞争；
7.  统一事务；
8.  TaskShare真正一对多/多对一；
9.  PI Position服务接入；
10. 单一真实1号位Solver。

## 99. 2号位不需要重写的内容

不需要：

-   推翻Orchestrator；
-   推翻版本框架；
-   推翻已有DTO；
-   为每个新业务再建一个独立服务层；
-   为PI位置在2号位重复实现一套复杂算法。

总体整改原则：

> **保留外壳，替换/补齐核心。**

------------------------------------------------------------------------

# 第二十六部分 全流程端到端闭环

## 100. 夜间FULL端到端

``` text
ERP/MES/采购/VMI事实准备
        ↓
订单/主数据/Routing/BOM/库存/进度/跨厂事实快照
        ↓
规则与参数版本冻结
        ↓
ScheduleRun + 多Domain PlanVersion
        ↓
按Domain依赖拓扑
        ↓
2号位装载Demand/Supply
        ↓
5号位PI Position复杂事实
        ↓
2号位分层Demand排序
        ↓
Demand/Supply原子Pegging
        ↓
计划良率反算
        ↓
逻辑生产需求 + Routing + Resource + Material AvailableTime
        ↓
1号位90天有限产能
        ↓
FinalTask + TaskShare + Physical Dependency + Explanation
        ↓
2号位统一持久化
        ↓
Domain ACTIVE/FAILED
        ↓
ScheduleRun COMPLETED/PARTIAL_SUCCESS/FAILED
        ↓
正式Task按资格下发MES
```

## 101. 白天插单端到端

``` text
新订单U
  ↓
创建单Domain WHATIF ScheduleRun/Candidate
  ↓
必要实时BOM
  ↓
固定已消耗/严格绑定/保护/不可逆
  ↓
释放上一ACTIVE普通未锁Allocation
  ↓
当前Domain可移动Demand + U重新Pegging
  ↓
2号位生成变化种子
  ↓
1号位基于Base ACTIVE局部传播和修复
  ↓
必要时单Domain完整重排兜底
  ↓
Candidate PlanVersion
  ↓
输出：能否接、最早日期、影响订单、瓶颈、原因
  ↓
业务确认真正采用时，再走正式LOCAL/MANUAL RESCHEDULE
```

## 102. 跨Domain插单端到端

``` text
最终订单A
  ↓
完整BOM发现依赖 C → B → A
  ↓
C单Domain WHATIF
  ↓ Quantity + AvailableTime
B单Domain WHATIF
  ↓ Quantity + AvailableTime
A单Domain WHATIF
  ↓
统一CTP结果
```

## 103. Domain失败恢复

``` text
夜间 C成功 → B失败 → A被阻断
        ↓
其它无关Domain继续ACTIVE
        ↓
Run=PARTIAL_SUCCESS
        ↓
人工修复B数据
        ↓
点击“重新计算”
        ↓
新ScheduleRun
        ↓
自动识别 B → A
        ↓
按拓扑重新计算
        ↓
成功后产生新ACTIVE
```

------------------------------------------------------------------------

# 第二十七部分 V1验收基线

## 104. 业务验收必须至少覆盖

### 正常订单

-   ERP成品库存已处理；
-   自制+采购混合；
-   PI+库存；
-   最终装配；
-   ZP/BP完成时间。

### 多订单竞争

-   不同订单类型同层穿插；
-   Demand Protection；
-   通用PI重新分配；
-   高优先插单影响低优先订单。

### PI位置

-   Stage正常；
-   XC；
-   跨厂在途；
-   Received；
-   中间Stage缺失；
-   UNLOCATED。

### 采购/VMI

-   人工ETA覆盖ERP ETA；
-   ETA缺失走默认LT；
-   已到厂未入库；
-   无PO时规划性采购占位；
-   CTP明确ESTIMATED。

### 跨厂

-   大工艺接续；
-   厂间出荷指示；
-   目标工厂库存优先；
-   OPEN且跨厂在途视图不存在该SH号时，整张SH形成一条源厂履约Demand；随后由PI→PI Position承接，只有最终缺口形成新增生产；
-   不生成ShippingTask。

### 有限产能

-   替代设备；
-   正倒排；
-   动态瓶颈；
-   Setup；
-   拆合批；
-   Stage重叠；
-   计划良率。

### 插单

-   局部影响传播；
-   Shared Resource其它Domain ACTIVE阻挡；
-   单Domain兜底全排；
-   跨Domain串行WHATIF。

### 失败恢复

-   单Domain失败；
-   下游不发布；
-   无关Domain继续；
-   人工新Run重算依赖链。

### MES

-   Candidate不下发；
-   虚拟PI Task不下发；
-   UNLOCATED不下发；
-   正式ACTIVE下发；
-   MES实绩进入次日重算。

## 105. 性能目标

当前工程目标：

-   约10万Task；
-   夜间90天FULL目标约15分钟收敛；
-   正常白天Candidate目标约1分钟；
-   大范围Candidate允许更长并自动升级单Domain完整重排。

真正数值以1号位压测结果最终优化，不因此提前增加第二套架构。

------------------------------------------------------------------------

# 第二十八部分 最终冻结清单

以下作为APS V1正式业务基线冻结：

1.  客户订单进入APS时已经是ERP成品库存处理后的需生产量；APS不再扣成品库存。\
2.  90天全部订单完整BOM、完整Pegging、进入统一有限产能时间推演。\
3.  90天不预建第二套远期粗排Solver。\
4.  2号位是主流程与Pegging数量真相所有者。\
5.  1号位是有限资源和时间真相所有者，并产生最终FinalTask。\
6.  3号位负责规则参数治理与运行生命周期，不做逐笔运行时裁决服务器。\
7.  5号位负责复杂事实，尤其PI
    Position，不直接做最终Pegging/Task/ReasonCode。\
8.  4号位不直接改运行时数据库。\
9.  Demand排序采用分层、有序规则段、第一命中、段内排序，不使用全局PriorityScore。\
10. 同一Material多个PI按冻结规则顺序选择，默认发行/创建时间优先；先选PI，再消费PI内部Position。\
11. PI RemainingQty以ERP边界为准，XC/在途/Stage只表达位置。\
12. 无PI号通用XC V1不处理。\
13. Demand Protection、Strict Binding、Execution Constraint三者分开。\
14. Lock触发尽量规则化配置；2号位结合事实真正执行。\
15. 普通Allocation不跨版本强制稳定；高优先Demand可重新竞争普通未锁Supply。\
16. 采购/VMI进入V1正式Supply；人工ETA优先ERP
    ETA，ETA缺失可按默认LT估算。\
17. 无正式采购承诺时允许规划性采购占位，但只能给估算、非确定承诺。\
18. 大工艺接续型跨厂：目标M → PI → PI Position → 新增生产。\
19. 厂间订单型：第一层普通Demand → SH单一Supply；`OPEN`且在途视图无该SH号时，第二层形成一条`RequiredQty=Order.Quantity`的源厂履约Demand，按`SourceFactoryId`重新归域，再执行PI → PI Position → 缺口新增生产。\
20. V1绝不建设有限物流Task体系。\
21. 计划良率区分Net Required Qty与Planned Process Qty。\
22. 2号位不提前生成最终Operation Task。\
23. FinalTask可以合并多个需求，必须保留TaskShare。\
24. 1号位使用同一Solver内的Forward/Backward/Mixed和动态瓶颈策略。\
25. 履约优先和客户承诺确保率高于Setup/WIP/利用率等次级优化。\
26. 白天Candidate严格单Domain。\
27. Candidate释放上一ACTIVE普通未锁Allocation重新竞争，不把ACTIVE全部Allocation扣死。\
28. Candidate有限产能采用变化种子→动态传播→最小扰动→单Domain完整重排兜底。\
29. 白天共享设备上其它Domain ACTIVE占用视为不可移动资源阻挡块。\
30. V1不建设跨Domain共享资源配额、借用、影响传播平台。\
31. 跨Domain CTP通过多个单Domain WHATIF按依赖拓扑串行计算。\
32. 夜间上游Domain失败时，依赖它的下游本次不发布新ACTIVE；无关Domain继续发布。\
33. FAILED后必须支持人工重新计算，新建ScheduleRun，保留失败历史。\
34. WHATIF Candidate永不自动激活。\
35. 如果2号位代码没有审批，V1不新增完整审批平台，只保留最小人工确认。\
36. FrozenZoneSnapshot、VirtualInventoryBalance不进入V1正式建设。\
37. PSA只保留极简内部镜像，不作为第二套真相源。\
38. PI Position落一个最小快照结构，不建设Header+Slice平台。\
39. 不为了逻辑Ledger强制新增物理Ledger表，优先复用现有结果结构。\
40. MES五态保持现状，不新增PAUSE/RESUME闭环。\
41. Candidate、虚拟无PI Task、UNLOCATED规划Task均不得下发MES。\
42. Explanation由1号位原生产出，2号位聚合持久化。\
43. 无法按期是正常业务结果，不是Solver失败。\
44. 数据异常可保守降级；数量闭合/事务/非法资源等核心错误必须使Domain失败。\
45. 所有V1技术实现都遵循：**能在主流程更自然完成的，不为了分工硬拆插件；真正复杂且变化大的事实/算法才独立。**

------------------------------------------------------------------------

# 第二十九部分 Manual ETA业务闭环补充（v1.1）

## 106. Manual ETA业务定位

由于ERP当前不存在人工ETA维护能力，APS V1增加人工ETA维护能力。

人工ETA对象：

`ProcurementManualEtaOverride`

其定位：

-   属于APS人工事实；
-   不属于ODS事实；
-   不修改ERP；
-   不形成新的Supply来源；
-   不改变原有SupplyFact业务模型。

## 107. Manual ETA计算链路

供应事实链路：

``` text
ERP ETA / ReleaseDate
        ↓
ODS供应事实
        ↓
SupplyFact_Pipeline

ProcurementManualEtaOverride
        ↓
2号位计算有效ETA
        ↓
AvailableTime
        ↓
Pegging / 有限产能排程
```

## 108. Manual ETA职责边界

5号位：

-   ProcurementManualEtaOverride表及接口支持；
-   ERP/ODS供应事实映射。

4号位：

-   Manual ETA维护页面。

2号位：

-   读取人工ETA；
-   计算Effective ETA；
-   计算AvailableTime；
-   排程消费。

5号位不负责最终AvailableTime计算。

## 109. SupplyFact相关补充

SupplyFact_Pipeline继续表示**采购在途/未结PO、VMI、采购已到厂未入库等正式管道供应事实**。厂间SH跨厂在途与PI级`INTERPLANT_IN_TRANSIT`不进入该表：SH在途只用于整单履行状态判断，PI在途只属于PI Position。

不增加：

-   SupplyFact.ManualEta字段；
-   APS_SupplyManualEtaOverride；
-   Placeholder实体表。

Planning-only Purchase Placeholder仍保持：

-   仅内存存在；
-   不进入SupplyFact；
-   不形成正式供应承诺；
-   不下发ERP。

## 110. CommitmentStatus语义补充

CommitmentStatus字段保留。

V1语义：

> 表示正式供应事实承诺状态。

不用于：

-   Planning-only Purchase Placeholder；
-   虚拟采购；
-   虚拟生产指示。

# 结语

APS V1最终不是"把所有可能的APS能力都提前做出来"，而是：

> **把90天订单的供需关系算对，把真实有限资源时间算对，把跨厂和采购时间接上，把插单影响快速算清，把不能按期的原因说清，并保证MES执行与次日重算闭环。**

在这个目标下：

-   2号位继续掌握稳定主流程；
-   1号位真正完成有限产能Solver；
-   3号位把可变业务配置化；
-   5号位承担复杂事实，不越权修改运行状态；
-   4号位把结果变成PMC和销售真正能使用的决策界面；
-   0号位只对真正的业务边界做裁决，不被迫参与算法工程细节。

本文档作为后续六份权威技术文档统一修订的业务基线。任何后续字段、接口、表结构或代码修改，如果与本文发生冲突，必须先确认是否属于新的业务裁决，不能未经确认再次改变已冻结口径。

------------------------------------------------------------------------

# 2026-09-01 Domain专项历史形成记录（原v1.2；当前已吸收进v1.5正文）

# 历史版本标题：APS V1 最终全部流程与业务基线 v1.2（Domain定义、分域计算与运行边界修订版）

**版本**：v1.2（正式业务基线修订版）\
**日期**：2026-10-05（北京时间）\
**基于版本**：`APS_V1_最终全部流程与业务基线_v1.1_20260824.md`\
**修订依据**：`APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901.md`

------------------------------------------------------------------------

# 0. 文档定位

本文档由《APS V1 最终全部流程与业务基线 v1.1（2026-08-24）》升级而来。

本版采用**增量正式修订**方式：

-   完整继承v1.1已冻结全部业务；
-   完整继承v1.1 Manual ETA / SupplyFact链路；
-   只补齐Domain定义、Scope、归域、逐Domain执行、共享资源、失败传播和性能边界；
-   不重新设计Pegging、90天统一有限产能Solver、Candidate、Demand
    Protection、PI Position、Placeholder、MES五态；
-   不引入MultiDomain Candidate；
-   不建设VirtualInventoryBalance持久化平台。

v1.1转为历史追溯。

------------------------------------------------------------------------

# 1. Domain最终业务定义

APS V1中，**排程域（Domain）**定义为：

> **APS用于隔离排程计算、计划版本（PlanVersion）、白天候选计划（Candidate）、失败传播与发布边界的稳定业务范围。**

Domain是APS自己的独立业务对象，以稳定`DomainKey`标识。

Domain不等同于：

-   ProductFamily；
-   Factory；
-   ProductionDepartment；
-   Stage；
-   Resource Group。

------------------------------------------------------------------------

# 2. V1允许的Domain Scope

V1只允许两种：

## 2.1 FAMILY

一个ProductFamily整体形成一个Domain。

``` text
DomainKey = FAMILY_X
ScopeType = FAMILY
ProductFamily = X
Factory = NULL
```

## 2.2 FACTORY_FAMILY

一个Factory + 一个ProductFamily形成一个Domain。

``` text
DomainKey = BJ_FAMILY_Y
ScopeType = FACTORY_FAMILY
Factory = BJ
ProductFamily = Y
```

相同ProductFamily可因Factory不同形成不同Domain。

V1不得以Customer、Stage、Department、Resource、OrderType、SQL、脚本或DSL等任意条件拼接Domain。

------------------------------------------------------------------------

# 3. DomainDefinition定位

APS必须具备稳定、持久化、可维护、可查询的DomainDefinition能力，至少回答：

-   DomainKey；
-   DomainName；
-   ScopeType；
-   ProductFamily；
-   Factory（FACTORY_FAMILY时）；
-   IsActive；
-   稳定排序号（如需要）。

DomainDefinition属于：

> **APS内部基础配置 / 主数据级配置。**

明确不属于：

-   RuleSet；
-   ParameterSet；
-   StrategyProfile；
-   ODS；
-   ERP/MES源事实。

必须区分：

``` text
DomainDefinition = Domain是谁、范围是什么
StrategyProfile = Domain使用什么Rule/Parameter/Solver Strategy
ExpectedDomainKeysJson = 本次Run算哪些Domain
```

------------------------------------------------------------------------

# 4. Domain维护与DomainKey

Domain由**系统管理员**维护。

可维护：

-   DomainKey；
-   DomainName；
-   ScopeType；
-   ProductFamily；
-   Factory；
-   IsActive；
-   SortOrder。

不得在运行中的ScheduleRun内修改并即时影响本次Run。

DomainKey必须：

-   稳定；
-   唯一；
-   可追溯；
-   不依赖PlanVersionId；
-   不依赖ScheduleRunId；
-   一次Run内不变化。

禁止：

``` text
DomainKey = PlanVersionId.ToString()
DomainKey = ScheduleRunId.ToString()
```

------------------------------------------------------------------------

# 5. Order / Demand实际归域

正式链路：

``` text
当前有效DomainDefinition
↓
Order / Demand业务属性
↓
Domain匹配
↓
真实DomainKey
```

FAMILY按ProductFamily匹配。

FACTORY_FAMILY按Factory + ProductFamily匹配。

2号位不得：

-   默认Domain=ProductFamily；
-   在代码里另写Domain规则；
-   用技术Id冒充DomainKey。

------------------------------------------------------------------------

# 6. 0～4号位Domain专项职责

## 6.1 0号位

负责Domain业务边界、ScopeType和超范围需求最终裁决。

## 6.2 1号位

只消费：

-   DomainKey；
-   单Domain DomainSolveRequest；
-   上游Quantity-Time；
-   当前Domain Routing/Resource/Material/Strategy；
-   外部不可移动Resource Block。

1号位不解释FAMILY/FACTORY_FAMILY，不重新归域。

## 6.3 2号位

是Domain运行执行Owner，负责：

1.  读取有效DomainDefinition；
2.  Order/Demand/Supply/Material上下文归域；
3.  形成真实DomainKey；
4.  按Domain Pegging；
5.  按Domain形成LogicalProductionDemand；
6.  按Domain调用1号位Solver；
7.  按Domain持久化；
8.  跨Domain Quantity-Time；
9.  失败传播；
10. FULL共享资源互斥；
11. 按Domain性能统计。

## 6.4 3号位

负责：

-   DomainDefinition后端治理；
-   合法性校验；
-   启停；
-   审计；
-   当前有效Domain集合；
-   ScheduleRun开始时冻结ExpectedDomainKeysJson；
-   Candidate严格单Domain治理。

不负责逐Order归域、Domain内Pegging、Domain内Solver。

## 6.5 4号位

提供最小Domain维护页面，不建设Domain设计器、DSL或任意条件表达式编辑器。

------------------------------------------------------------------------

# 7. 夜间FULL运行模型

一次夜间FULL：

``` text
1 ScheduleRun
↓
多个Domain
↓
每个Domain独立PlanVersion
```

不得继续使用一个PlanVersion承载全部Domain。

ScheduleRun开始时：

1.  读取当前有效DomainDefinition；
2.  形成本次要计算的Domain集合；
3.  冻结到ExpectedDomainKeysJson；
4.  本次Run只按该集合执行。

运行中修改DomainDefinition只对后续Run生效。

------------------------------------------------------------------------

# 8. 当前V1串行逐Domain执行

正式冻结：

> **V1当前夜间FULL按Domain串行执行。**

``` text
ExpectedDomainKeysJson
↓
Domain_Dependency
↓
拓扑排序
↓
同拓扑层稳定排序
↓
逐Domain执行
```

每个Domain完整执行：

``` text
Context
↓
Pegging
↓
LogicalProductionDemand
↓
DomainSolveRequest
↓
Solve
↓
Validate
↓
Persist
↓
Publish / Fail
```

不恢复DomainSchedulingJob、DomainLayerCoordinatorJob、多Hangfire Domain
Job并发架构。

正确方向是在现有Nightly /
SchedulingOrchestrator内部形成稳定单Domain执行边界，例如：

`ExecuteDomainAsync(domainExecutionContext)`

当前仍串行调用。

------------------------------------------------------------------------

# 9. 未来并行边界

没有真实分域压测证明串行不满足目标前，不提前建设Domain并行。

未来只有同时满足：

``` text
无未完成上游依赖
AND
无共享Resource
```

的Ready Domain可受控并行。

即：

``` text
Dependency(D1,D2)=NONE
AND
Resources(D1) ∩ Resources(D2)=∅
```

当前实现必须做到：

> **串行运行，但单Domain执行边界天然可并行。**

------------------------------------------------------------------------

# 10. Domain_Dependency最终语义

`Domain_Dependency`的上下游节点必须是真实`DomainKey`。

不得继续默认：

``` text
DomainCode = ProductFamily.Code
```

例如：

``` text
BJ_FAMILY_Y
TJ_FAMILY_Y
```

是两个独立Domain节点。

------------------------------------------------------------------------

# 11. FULL共享Resource互斥

独立PlanVersion不代表共享设备可双占。

当前串行：

``` text
Domain A Solve
↓
得到共享Resource占用
↓
加入本次Run共享资源占用上下文
↓
作为后续Domain不可占用时间块
↓
Domain B Solve
```

V1不建设：

-   Domain Resource Quota；
-   跨Domain资源配额；
-   借用平台；
-   虚拟子日历平台。

------------------------------------------------------------------------

# 12. Candidate外Domain共享资源

Candidate继续严格单Domain。

其它Domain当前ACTIVE在共享Resource上的占用：

> 必须作为不可移动`ExternalDomainResourceBlocks`传给1号位。

Candidate不得：

-   推动其它Domain；
-   删除其它Domain Task；
-   跨Domain借资源；
-   形成MultiDomain Candidate。

------------------------------------------------------------------------

# 13. 跨Domain Quantity-Time

继续传：

``` text
Material + Quantity + AvailableTime
```

必须保留分段：

``` text
40 @ 15日
60 @ 17日
```

不得压成：

``` text
100 @ 17日
```

跨厂/Stage LeadTime继续按原冻结真实LeadTime处理。

Quantity-Time只作为当前ScheduleRun内存上下文/DTO传递。

不得建设VirtualInventoryBalance持久化表、Repository或跨版本状态机。

------------------------------------------------------------------------

# 14. FULL失败传播

例如：

``` text
C → B → A
```

若C成功、B失败：

-   B新PlanVersion=FAILED；
-   A本次不得发布新ACTIVE；
-   A保留上一ACTIVE；
-   A标识为被上游阻断；
-   无关Domain继续；
-   ScheduleRun汇总PARTIAL_SUCCESS或FAILED。

FAILED历史不能改回RUNNING。

人工恢复创建新的ScheduleRun，并按必要依赖链重算。

------------------------------------------------------------------------

# 15. 跨Domain CTP

仍采用链式单Domain WHATIF：

``` text
C WHATIF
→ Quantity-Time
→ B WHATIF
→ Quantity-Time
→ A WHATIF
→ 汇总客户答案
```

不建设MultiDomain Candidate。

------------------------------------------------------------------------

# 16. 性能目标与测量

V1性能目标继续保持：

> **约10万FinalTask / 90天 / 夜间FULL约15分钟收敛目标。**

分域后必须按Domain记录：

-   DomainKey；
-   DemandCount；
-   LogicalProductionDemandCount；
-   FinalTaskCount；
-   ContextBuildMs；
-   PeggingMs；
-   SolverMs；
-   PersistMs；
-   TotalMs。

同时记录：

-   ScheduleRunTotalMs。

依据真实测量判断：

1.  超大Domain是否为瓶颈；
2.  小Domain累计是否超时；
3.  是否存在重复DB I/O；
4.  并行是否有真实收益。

------------------------------------------------------------------------

# 17. 与既有业务的关系

## 17.1 Pegging

不改变Pegging算法和数量闭环。

只增加前置边界：

``` text
Order / Demand先归入真实Domain
↓
在该Domain内执行原冻结Pegging
```

## 17.2 90天有限产能

仍只有一套正式有限产能Solver。

"逐Domain Solve"是同一套Solver处理多个独立Domain问题，不是第二套算法。

## 17.3 Manual ETA / SupplyFact

v1.1全部继续有效：

``` text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

5号位维护Manual ETA事实；2号位计算Effective ETA和AvailableTime。

## 17.4 Demand Protection

触发、保护数量、Lock、释放条件全部不变。

## 17.5 PI Position

5号位继续负责"PI剩余量在哪里"，2号位在正确Domain上下文中选PI并消费Position。

## 17.6 跨厂

大工艺接续型和厂间出荷指示型不变。

必须新增一个解释：

> "跨Factory"不必然等于"跨Domain"；"同ProductFamily"也不必然等于"同Domain"。是否跨Domain必须看真实DomainKey。

------------------------------------------------------------------------

# 18. 旧表述统一失效

从v1.2开始，以下不得再作为无条件规则：

``` text
Domain = ProductFamily
DomainCode = ProductFamily.Code
Product Domain = ProductFamily
跨产品族 = 跨Domain
同ProductFamily = 同Domain
```

统一改为：

> 根据当前有效DomainDefinition确定真实DomainKey。

------------------------------------------------------------------------

# 19. 本版明确不做

不引入：

-   任意Domain表达式；
-   Domain DSL；
-   Domain脚本；
-   MultiDomain Candidate；
-   VirtualInventoryBalance平台；
-   跨Domain共享资源配额；
-   自动跨Domain借用；
-   多Domain原子激活；
-   当前Domain并行；
-   第二套Solver；
-   第二套Pegging；
-   通用分域规则引擎。

------------------------------------------------------------------------

# 20. v1.2业务冻结摘要

1.  Domain是独立排程、PlanVersion、Candidate、失败传播和发布边界；
2.  Domain不等同于ProductFamily/Factory/Department/Stage；
3.  V1只支持FAMILY、FACTORY_FAMILY；
4.  Domain由系统管理员维护；
5.  DomainDefinition属于APS基础配置，不进入RuleSet/ParameterSet/ODS；
6.  3号位治理DomainDefinition并冻结Run有效Domain集合；
7.  2号位负责真实归域和逐Domain运行；
8.  1号位只消费DomainKey和单Domain上下文；
9.  4号位提供最小维护页面；
10. 一个FULL ScheduleRun包含多个Domain独立PlanVersion；
11. 当前串行逐Domain执行；
12. 不恢复旧多Hangfire Domain Job并发；
13. 单Domain执行边界为未来并行保留能力；
14. 未来仅无依赖且无共享Resource的Ready Domain允许并行；
15. Domain_Dependency必须使用真实DomainKey；
16. FULL共享Resource必须互斥；
17. Candidate严格单Domain并消费外Domain不可移动Block；
18. 跨Domain继续传递分段Quantity-Time；
19. 上游失败阻断依赖下游本次发布，无关Domain继续；
20. 约10万Task/90天/15分钟目标用真实分域性能埋点验证。

------------------------------------------------------------------------

# 21. 一句话业务基线

> **APS
> V1把排程域（Domain）正式定义为由系统管理员维护的稳定排程隔离单元，范围仅允许"产品族（FAMILY）"或"工厂+产品族（FACTORY_FAMILY）"；夜间一个FULL
> ScheduleRun按照真实DomainKey和Domain_Dependency拓扑，当前串行逐Domain完成Pegging、有限产能求解与独立PlanVersion发布，同时保持跨Domain物料Quantity-Time、共享资源互斥和失败传播闭合；未来只有在真实压测证明有必要时，才允许对无依赖且无共享资源的Ready
> Domain受控并行。**

------------------------------------------------------------------------

# v1.3 Supply / PI Position专项冻结对齐增量

## 一、ProductionInstruction Position边界

本版本补充冻结：

ProductionInstruction仍作为Supply参与Pegging。

ProductionInstructionPositionSnapshot用于表达生产指示内部执行位置事实。

Position不是独立Supply：

-   不参与Supply排序；
-   不单独产生Allocation；
-   不替代ProductionInstruction Supply。

## 二、跨厂接续规则

厂间订单型跨厂保持原有Supply承接方式。

大工艺接续型跨厂：

ProductionInstruction Supply + PositionType=INTERPLANT_TRANSIT

不新增Transit Supply。

## 三、Domain运行边界

DomainKey表示APS真实稳定排程域。

统一用于：

-   ScheduleRun；
-   PlanVersion；
-   Domain依赖关系。

V1不新增：

-   DomainRuntime；
-   DomainContext；
-   VirtualInventoryBalance；
-   Domain资源配额平台。

## 四、职责同步

3号位负责DomainDefinition治理。

2号位负责消费DomainKey并执行排程运行闭环。

1号位消费求解结果。

5号位负责事实数据和接口接入。


---


# 2026-10-05 本轮正式变更回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。

本版在v1.7正式源上原位修订，完整吸收“四专题 + A/B跨版本连续性 + V1多Routing + FINAL出口层级Material标准化”，不重新打开Domain、Supply/SH、PI Position、Resource Calendar、Auth/RBAC及既有Setup核心业务。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 本轮统一业务主链

```text
原始Material事实
→ APS Material标准化（普通 / FINAL）
→ Demand / Supply / Pegging
→ 当前LogicalProductionDemand Q
→ 5号位标准化MES执行事实
→ 2号位识别A/B/C
→ A/B固定真实RouteCode+PathId；C装载全部合法RoutingCandidates
→ 1号位联合执行Batch + Direction + Routing + Resource有限产能求解
→ FinalTask返回ExecutionBatchDraftKey/ContinuationKey?/真实RouteCode/PathId/Operation/Resource/时间/解释
→ 2号位持久化Operation级Task并形成TaskNo/MES身份闭环
→ A继续原MES工单；B新TaskNo绑定原MES工单；C新TaskNo并创建新MES工单
→ 下一Run通过历史APS下发映射或5号位事实回接真实Routing
```

## 五种身份与职责

- `MESWorkOrderNo`：MES真实执行身份；A/B继续原WO，C新建WO。
- `TaskNo`：Stage内MES执行批APS跨版本身份；A继承，B/C在FinalTask后由2号位生成。
- `Task.Id`：PlanVersion内单一Operation排程结果记录。
- `ContinuationKey`：2号位在Q形成并识别A/B后生成的ScheduleRun内关联键；同一WO多Slice共享一个Key。
- `ExecutionBatchDraftKey`：1号位求解阶段的执行批归组键，不等同TaskNo。

## Demand Goal、Batch、Direction、Routing

- Pegging数量真相先形成Q；A/B/C识别在Q之后。
- Pegging不传播Direction、Batch、Resource或Routing最终选择。
- A/B既存执行批NoSplitMerge并固定真实Route/Path；C桶由1号位动态形成Execution Batch并在合法RoutingCandidates中选择一条完整路径。
- Direction不得由OrderType硬映射，按冻结策略综合Demand Goal、RequiredAvailableTime、Slack、Material、资源、Execution与Lock决定。

## FINAL Material标准化

- 普通物料继续保持原标准Material身份，例如`FG_xxx / WIP_xxx / RAW_xxx`；出口库/最终可销售层级按既有裁决标准化为`FINAL_FG_xxx / FINAL_WIP_xxx / FINAL_RAW_xxx`。
- `FINAL_`是Material身份标准化前缀，不是MaterialType；MaterialType解析必须先Strip `FINAL_`，再按既有FG/WIP/RAW/ASSY分类。
- FINAL判定发生在Material标准化层：以源系统/ODS已经能够识别的“出口库/最终可销售层级”事实生成标准化MaterialCode；5号位不得通过Routing、Operation名称、包装工序或路径长度反向猜测FINAL。
- `MaterialMapping`保留SourceID + Warehouse/Location等源追溯，并把源事实稳定映射到普通或FINAL标准MaterialId；一般Warehouse差异不得全部提升成独立Material身份。
- Inventory、PI、Order、Demand、Supply及Routing在进入APS运行主链时统一消费标准化后的MaterialId；Routing直接绑定该MaterialId，不在Routing加载后再判断FINAL。
- FINAL不新增BOM层级，禁止自动生成`FINAL_xxx → xxx`的BOM关系；不新增FINAL SupplyType，不建立第二Material平台。

## 谁动数据库

- 1号位：零APS数据库读写，只消费2号位内存求解契约并返回结果。
- 2号位：运行主流程、Q/A-B-C、Solver输入装配、FinalTask校验与Task/TaskNo/MES关系等运行结果持久化Owner。
- 3号位：规则/参数/策略/Auth/RBAC及其治理表Owner，不写2号位运行结果真相。
- 5号位：ODS/MES复杂事实与Routing回接事实Owner，不重算Pegging/Batch/Solver，不写2号位运行结果真相。
- 4号位：页面与操作入口，不直接写APS核心运行数据库。

---
# 本次专项补充：PI Position与有限产能执行起点上下文冻结规则（2026-09-04）

## 1. 补充目的

本章节用于明确：

生产指示（Production Instruction，PI）已经存在MES执行事实时，如何将“当前生产位置”转换为有限产能排程可以消费的“执行起点上下文”。

本补充不改变：

- Demand与Supply的Pegging关系；
- ProductionInstruction作为Supply的业务定位；
- PI Position模型；
- 90天统一有限产能Solver；
- 1号位、2号位、5号位原冻结职责。

---

## 2. PI Position与NextOperationContext关系

### 2.1 PI Position语义

PI Position表示：

> 生产指示剩余数量当前已经执行到什么位置。

PI Position回答：

- 剩余数量在哪里；
- 当前处于哪个Stage；
- 是否存在XC、跨厂、Stage等待等状态。

PI Position不是：

- Supply；
- Demand；
- Operation级Pegging结果；
- 有限产能Task。

---

### 2.2 NextOperationContext语义

NextOperationContext表示：

> 对于PI剩余数量，有限产能排程下一步应该从哪里开始展开Routing。

其来源：

```
MES执行事实
+
Routing工序关系
+
PI Position
        ↓
NextOperationContext
```

NextOperationContext不是新的Position类型，不改变PI Position定义。

---

## 3. 号位职责补充

### 5号位

5号位负责：

- MES执行事实读取；
- PI Position计算；
- Routing关联；
- NextOperationContext计算。

5号位回答：

> 已经做到哪里，下一步进入哪里。

5号位不负责：

- Demand分配；
- Pegging；
- 有限产能排程；
- FinalTask生成。

---

### 2号位

2号位负责：

- 消费PI Position结果；
- 消费NextOperationContext；
- 形成LogicalProductionDemand；
- 调用1号位有限产能Solver。

2号位回答：

> 哪个Demand承接哪个PI Supply，以及这个Supply剩余数量从哪里开始排程。

2号位不重新解析MES执行事实。

---

### 1号位

1号位负责：

- 消费LogicalProductionDemand；
- 根据StartStage/必要时StartOperation展开Routing；
- 执行有限产能计算；
- 生成FinalTask。

1号位不负责：

- 读取MES；
- 判断已完成工序；
- 自行计算PI Position。

---

## 4. 排程链路冻结

最终链路：

```
MES执行事实
        ↓
5号位
PI Position
+
NextOperationContext
        ↓
2号位
Pegging
+
LogicalProductionDemand
        ↓
1号位
有限产能Solver
        ↓
FinalTask
```

---

## 5. 禁止重新打开事项

V1禁止：

- 将Pegging扩展为Demand→PI→Operation三级绑定；
- 建设Operation级Position模型；
- 让1号位直接访问MES；
- 让2号位自行推导下一工序；
- 将NextOperationContext定义为新的Supply身份。

---


# 2026-09-21 无设备小工序与人工有限产能模型联合冻结补充

> 本补充形成于v1.7阶段；当前v1.8继续继承其有效内容，仅吸收《无设备小工序与人工有限产能模型修改指导意见》。不改变已冻结的Pegging、PI Position、Domain、Candidate、90天唯一Solver等业务模型。

## Operation规划模式（Operation Planning Mode）

APS V1不再使用“有设备/无设备”作为是否进入有限能力排程的判断标准。MES Routing负责描述生产工艺事实，APS通过Operation规划模式决定该工序如何参与排程。

规划模式包括：

- FINITE_RESOURCE（有限资源约束模式）：进入有限产能Solver，占用Resource时间轴。
- UNCONSTRAINED（非约束加工模式）：保留工艺时间和依赖关系，不占用有限Resource。
- WAIT_ONLY（等待约束模式）：表达等待时间，不占用Resource。

## 人工资源模型

人工能力不建设独立排程体系，统一复用APS Resource模型：

Resource + ResourceCalendar + OperationResourceEligibility。

人工资源日历在APS中维护；MES数据可作为资源信息ETL来源，但不作为完整APS资源能力日历。

## 职责边界补充

- 1号位消费Operation Planning Mode和Resource约束，负责最终有限产能求解。
- 2号位负责加载规划属性和结果持久化，不生成FinalTask。
- 3号位负责规则与参数治理。
- 4号位负责配置页面。
- 5号位负责事实查询，不负责Solver。
