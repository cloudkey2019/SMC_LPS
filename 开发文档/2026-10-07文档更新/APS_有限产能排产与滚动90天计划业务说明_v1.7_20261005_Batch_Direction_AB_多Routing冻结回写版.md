# APS 有限产能排产与滚动90天计划业务说明 v1.7（20261005 Batch / Direction / A-B连续性 / 多Routing冻结回写版）

# 2026-10-05 本轮正式变更回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。

本版在唯一90天有限产能Solver框架内回写A/B既存执行批、C桶Execution Batch、Direction与V1多Routing，不新增第二Solver或Batch平台。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 当前有效求解边界

- A/B：既存Execution Batch，`NoSplitMerge=true`，固定真实RouteCode+PathId+StartOperation；不参加普通拆批/合批和Routing竞争。
- C：Free demand slice进入同一Solver；由1号位根据Batch Policy形成Execution Batch，并在2号位提供的`RoutingCandidates[]`中为每个Execution Batch选择一条完整真实Path。
- 一个Execution Batch只能绑定一条完整RouteCode+PathId，Operations/Dependencies/Eligibility必须在同一Path内闭合。
- Direction为AUTO/FORWARD/BACKWARD/MIXED，不由OrderType硬映射。
- `PreferredResourceCode`为可空V1正式输入；非空时1号位必须作为软偏好消费，不能升级Hard Lock。
- 1号位零DB I/O：所有Material、Routing、Batch Policy、Calendar、Eligibility、A/B/C上下文均由2号位通过DomainSolveRequest装载。


**当前版本**：v1.7\
**当前版本日期**：2026-10-05（北京时间）\
**适用版本**：APS V1\
**文档性质**：业务与算法口径专项说明\
**基于版本**：v1.6（2026-09-20 Setup换型规则 / 无设备小工序 / 人工有限产能 / 用户输出联合版）\
**当前上位基线**：《APS V1 最终全部流程与业务基线 v1.8》\
**Domain专项依据**：《APS V1 Domain定义、分域计算与运行边界业务裁决
v1.0》

> **当前版本声明**：本文件为v1.7当前正式变更正文，继承v1.6仍有效有限产能业务，并正式回写A/B既存执行批、C桶Batch Decision、Direction综合决策、多Routing候选/实际Path以及PreferredResource软偏好。仍使用唯一90天Solver，不新增第二Batch/Direction/Routing求解平台。历史章节与本版冲突时以v1.7为准。

## 历史版本变化说明（v1.5，2026-09-15）

**修改依据**：业务基线v1.6 + 白天业务场景/Candidate人工调整指导v1.1\
**修订原则**：继续使用唯一90天Solver，只扩展Candidate求解输入和局部传播Seed，不建设第二LocalSolver或Candidate专属资源事实。\
**影响边界**：Pegging数量真相、FinalTask、跨Domain普通Quantity-Time、跨版本Continuation、SH整单时间保持不变。

  -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
  编号    类型           章节            内容                                                                                    原因/依据      业务影响                    接口影响                 数据库影响   代码影响
  ------- -------------- --------------- --------------------------------------------------------------------------------------- -------------- --------------------------- ------------------------ ------------ ---------------
  1       求解输入       Candidate章节   支持多同类Seed、多Task软TargetTime、多订单人工目标、多故障/Calendar资源。               单场景多对象   一个Run可承载多个同类变化   影响2→1 SolveRequest     无           影响1/2号位

  2       上下文边界     Candidate章节   Seed只决定传播起点，当前完整Calendar/Eligibility/资源占用/物料时间仍全部进入Solver。    v1.1最高原则   防止Scope误做事实过滤       影响SolveRequest装配     无           影响1/2号位

  3       优先级边界     Candidate章节   甘特/故障/Calendar固定NORMAL；EXPEDITE只改变上游业务优先，不突破任何Solver硬约束。      场景矩阵       防止拖拽急单化              PriorityMode由上游传入   无           影响1/2号位

  4       LOCAL/MANUAL   局部兜底        LOCAL影响扩大为全Domain重排时RunType仍为LOCAL；MANUAL只表示用户从一开始选择整Domain。   v1.1边界       RunType不按影响大小变化     无新RunType              无           影响1/2/3号位
  -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

------------------------------------------------------------------------

## 历史继承说明（非当前版本元数据）

以下内容来源于上一冻结版本，仅用于追溯业务形成过程。

当前文档的有效版本、日期、上位基线以及业务裁决，以本文档顶部"当前版本声明"为准。

历史章节中的旧版本号、旧日期、旧上位基线，不代表当前开发、审核和验收依据。

------------------------------------------------------------------------

## 完整业务正文（继承历史冻结内容）

**适用版本**：APS V1\
**文档性质**：业务与算法口径专项说明\
**冻结对齐日期**：2026-08-12\
**主要读者**：0号位、1号位、2号位、3号位，兼供4号位和5号位理解上下游边界\
**上位基线**：《APS V1最终全部流程与业务基线_v1.0_20260812》

> **冻结声明**：本文是最终业务冻结基线的有限产能专项解释文档，不拥有独立修改冻结业务的权力。本文只允许补充说明、案例、边界和与最终基线的一致性修正。若与最终业务冻结基线冲突，以最终业务冻结基线为准。任何业务口径变更必须明确提出"重新打开已冻结决策"，经0号位重新裁决后形成新版本。禁止因为代码现状、旧文档、实现方便、未来扩展或"更优雅"而静默修改本文已冻结业务。

## v1.1相对v1.0的对齐范围

本次不是重新设计，只回写v1.0之后已经冻结的口径：

1.  白天单Domain Candidate遇到跨Domain共享资源时，其它Domain
    ACTIVE占用作为不可移动阻挡块；
2.  无正式PO/VMI时允许Planning-only Purchase
    Placeholder，但不构成确定性CTP承诺；
3.  跨Domain Quantity-Time必须保留分段；
4.  FAILED人工恢复新建ScheduleRun；
5.  Firm/Frozen跨版本由上一ACTIVE转换为本次不可移动锚点，不建设FrozenZoneSnapshot平台；
6.  Candidate正式采用只保留最小人工授权确认，不建设完整审批主链；
7.  强化"绝不建设有限物流Task体系"，删除任何"未来再把物流放进1号位Solver"的暗示；
8.  补充正式MES发布资格边界。

其它v1.0主体业务不重新打开。

------------------------------------------------------------------------

## 1. 为什么需要单独说明有限产能排产

有限产能排产（Finite Capacity
Scheduling）不是把预生成Task简单塞进日历，而是把Pegging已经确认的业务数量关系，结合完整工艺路线、资源资格、日历、物料AvailableTime、Firm/Frozen/Lock约束，在真实有限资源上形成可执行时间计划。

必须回答：

1.  哪些逻辑生产需求形成哪些最终Operation Task；
2.  每个Task在哪个合法Resource上、何时开始、何时结束；
3.  多Demand竞争资源时如何排序；
4.  物料何时齐套；
5.  是否能按客户交期完成；
6.  不能按期时真正根因是什么；
7.  白天插单影响了哪些既有计划。

> **1号位拥有时间和有限资源真相；2号位拥有Pegging业务数量真相。**

------------------------------------------------------------------------

## 2. V1总体原则

### 2.1 90天只有一套正式有限产能计划

APS V1采用滚动90天统一有限产能计划。

90天内活跃Demand原则上全部：

-   完整BOM；
-   完整Pegging；
-   计划良率；
-   进入同一套1号位有限产能求解器。

明确简化为Stage
LeadTime的工艺继续按LeadTime处理，不因为远期再造另一套日产粗排算法。

### 2.2 近期和远期不是两套算法

可以优化深度不同，但资源与时间语义必须一致。

不能：

-   近期设备级；
-   远期日产量级；
-   再把结果拼起来。

### 2.3 统一Solver优先

V1以约10万Task、夜间FULL约15分钟左右收敛作为工程目标。

只有真实压测和正常优化后仍不达标，才允许重新评估远期降级策略；**不能预建第二套Solver。**

------------------------------------------------------------------------

## 3. 角色边界

### 3.1 1号位

负责：

-   FinalTask；
-   最终Resource；
-   Start/End；
-   最终拆批/合批；
-   工序物理依赖；
-   物料数量---时间约束；
-   Firm/Frozen/Lock执行；
-   动态瓶颈；
-   正排/倒排/混合；
-   Candidate局部影响传播；
-   Explanation Facts；
-   Continuation Slice与Free Slice联合有限产能求解；
-   ContinuationKey原样回传；
-   工序级LastReportResourceCode作为资源连续性偏好；
-   SH整单SourceReadyTime计算。

1号位纯内存求解，不直接写APS数据库，不重新Pegging Supply。

### 3.2 2号位

进入1号位前完成：

-   Pegging数量闭合；
-   Supply选择；
-   Demand Protection/Lock；
-   计划良率反算；
-   逻辑生产需求；
-   Routing/资源资格/Calendar/AvailableTime组装；
-   Candidate变化种子；
-   当前Q形成后的Continuation/Free分桶；
-   ContinuationKey与TaskNo/MESWorkOrderNo身份映射；
-   SH源/目标工厂时间约束组装；
-   最终结果统一持久化。

2号位不预先锁死最终Operation Task、设备和时间槽。

### 3.3 3号位

治理并冻结：

-   正倒排策略；
-   瓶颈策略；
-   拆合批参数；
-   Stage重叠/转运；
-   Setup；
-   资源偏好；
-   计划良率；
-   履约确保率；
-   次级优化参数。

不建设任意Command脚本链。

### 3.4 4号位和5号位

-   4号位负责CTP、插单、甘特图、Candidate比较、影响展示与人工确认；
-   5号位负责复杂事实，不做有限产能求解和最终排程ReasonCode；工序级LastReportResourceCode属于5号位标准化MES执行事实，工单级不新增同名字段。

------------------------------------------------------------------------

## 4. 2号位交给1号位的是完整求解问题，不是FinalTask

正式输入包括九类：

### 4.1 求解边界

-   ScheduleRun / PlanVersion；
-   DomainKey；
-   FULL / Candidate；
-   DataCutoffTime；
-   90天Horizon；
-   Base ACTIVE；
-   可移动/不可移动边界。

### 4.2 生产需求与业务优先关系

至少包括：

-   Material；
-   Factory；
-   ProductFamily；
-   OrderType；
-   FromStage/ToStage；
-   NetRequiredQty；
-   DueDate；
-   业务优先层级；
-   Demand Protection / Lock；
-   Allocation/Order稳定血缘。

### 4.2A 跨版本执行连续份额

当前LogicalProductionDemand形成以后，2号位可以把其中一部分标记为既存执行连续份额。每个连续份额至少表达：ContinuationKey、PI身份、StartStage/StartOperation、NetOutputQty、PlannedProcessQty、NoSplitMerge或等价身份保护、PreferredResource（可空）、EligibleResources和Material
Quantity-Time。

连续份额仍属于当前Q，不得在Q之外额外叠加。多个MES工单必须形成多个独立连续份额。

工序级`LastReportResourceCode`存在且满足资格时可以作为PreferredResource，但不是Hard
Lock。1号位不需要知道MESWorkOrderNo、旧TaskNo或人工/历史来源，只按有限产能语义求解并原样回传ContinuationKey。

### 4.3 Net Required Qty与Planned Process Qty

必须分开：

-   **Net Required Qty**：交付给需求的合格净数量；
-   **Planned Process Qty**：考虑计划良率后实际预计投入加工的数量。

### 4.4 Allocation数量血缘

1号位需要知道FinalTask最终为哪些Allocation服务，以便返回多对多TaskShare。

### 4.5 Routing候选图

包括：

-   Stage；
-   Operation；
-   Dependency；
-   RouteCode / PathId；
-   Stage LeadTime；
-   重叠/转运关系。

### 4.6 资源资格与日历

包括Resource、OperationResourceEligibility、设备/部门日历、已有占用等。

### 4.7 物料Quantity-Time约束

所有已选Supply转成：

> **Quantity + AvailableTime。**

来源包括库存、PI、自制上游、采购、VMI、已到厂未入库、跨厂、跨Domain等。

1号位不能自行替换2号位已经Pegging确认的Supply。

### 4.8 排程策略包

正倒排、瓶颈、拆批、Stage重叠、Setup、资源偏好、局部修复、履约目标等。

### 4.9 执行与不可移动事实

包括：

-   已完成；
-   已开工；
-   已下发且不可移动；
-   Firm；
-   Frozen；
-   Demand Protection / Lock带来的不可移动关系；
-   固定Resource/Start/End等。

------------------------------------------------------------------------

## 5. FinalTask由1号位形成

### 5.1 2号位不能提前生成最终Operation Task

否则1号位失去：

-   是否拆批；
-   拆几批；
-   合批；
-   替代资源；
-   等价路径；
-   时间槽联合优化。

正式口径：

> **2号位形成逻辑生产需求；1号位形成FinalTask。**

### 5.2 不同Demand可以合并为一个FinalTask

在工艺、资源、交期、Lock/Firm/Frozen都允许时可以合并。

必须保留Task→多个Allocation份额的数量映射。

### 5.3 Task与Demand不是一对一

必须支持：

-   一个Allocation→多个FinalTask；
-   一个FinalTask→多个Allocation份额。

------------------------------------------------------------------------

## 6. Routing、等价路径与资源选择

2号位负责"合法候选"，1号位负责"实际选择"。

V1正式支持多Routing：A/B固定真实RouteCode+PathId；C桶进入Solver前保留全部合法RoutingCandidates，并由1号位对每个Execution Batch选择一条完整路径。

只有等价路径不改变BOM、Material、Factory、资格和产出定义时，1号位才可在求解内直接选择；否则回到2号位业务层决定。

------------------------------------------------------------------------

## 7. 计划良率（Planning Yield）

### 7.1 计划良率必须进入V1

客户需要100件合格品，某Stage计划良率98%，计划加工量可能为102～103件。

设备能力按PlannedProcessQty占用，不能只按100件。

### 7.2 2号位反算，1号位占能力

计划良率改变上游投入和材料需求，因此由2号位反算；1号位只按PlannedProcessQty排产。

### 7.3 Allocation仍按净合格数量闭合

Allocation=100，PlannedProcessQty=103时，多出的3是计划损失，不属于客户Demand。

### 7.4 已完成Stage不重复应用良率

PI当前位置已经过MACH，则不能重新对MACH应用良率。

### 7.5 ERP PI不能因为计划良率膨胀成更大Supply

PI剩余60仍只有60
Supply。若预期损耗导致还需额外投入，形成新增生产需求，而不是把PI改成62。

------------------------------------------------------------------------

## 8. 物料Quantity-Time约束

### 8.1 Purchased与Self-made统一为AvailableTime约束

下游真正关心的是：

> 某数量什么时候可用。

### 8.2 采购件不生成制造Task

采购、VMI、库存只提供Supply和AvailableTime，不生成制造Task。

### 8.3 Quantity-Time可分段消费

如果200件库存当前可用、100件PO
16号可用，且下游允许拆批，1号位可以利用分段AvailableTime形成不同生产批次。

### 8.4 1号位不能自行更换Supply

更换PO/PI/库存等会改变Allocation，必须回2号位Pegging。

### 8.5 Planning-only Purchase Placeholder

若采购件没有库存、PO、VMI、已到厂未入库，2号位可以提供Planning-only
Purchase Placeholder：

-   Quantity=缺口；
-   AvailableTime按冻结DefaultLT估算；
-   `ESTIMATED / NOT_COMMITTED`。

1号位可以用它继续完成90天产能时间推演。

但是：

> **依赖该占位得到的日期只能是估算，不是确定性CTP承诺。**

该占位不生成Task、不生成PO、不下发ERP/MES。

------------------------------------------------------------------------

## 9. 跨厂与跨Domain时间处理

### 9.1 有限物流Task永久不进入当前冻结V1设计

跨厂运输/检验/转运统一通过确定性LeadTime形成下游AvailableTime。

> **当前冻结方向绝不建设ShippingTask、车辆、月台、班次有限资源或物流Solver。**

如果未来业务真的要求改变这一点，必须明确重新打开冻结决策，不能在技术文档中顺手预留成1号位未来能力。

### 9.1A 厂间出荷指示（SH）整单时间语义

SH订单交货期表示目标工厂要求到货时间，不是实际ETA。若目标要求时间为T、冻结SH跨厂LT为LT，则`源厂要求完成时间 = T - LT`。

源厂真实准备完成时间（SourceReadyTime）必须由1号位有限产能求解产生，2号位不得自行估算。

同一SH号只能整单一次入库，因此同一SH在目标侧只有一个整单AvailableTime。即使源厂内部40/60分两批完成，也不能形成两个目标侧Quantity-Time
Slice。该规则仅适用于SH，不改变普通跨Domain物料保留分段Quantity-Time的通用规则。

同Domain时，2号位在一个DomainSolveRequest中表达源厂生产→SourceReadyTime→跨厂LT→目标侧后续依赖，由1号位一次求解；跨Domain时，源Domain
Solver返回SourceReadyTime，2号位加LT形成目标Domain输入。3号位治理标准LT，5号位真实物流事实优先默认参数。

### 9.2 跨Domain仍传Quantity + AvailableTime

上游Domain最终产出Task完成，加必要转运LeadTime，形成下游供给时间。

不建设VirtualInventoryBalance状态平台。

### 9.3 Quantity-Time必须保留分段

若上游实际：

-   40件15号可用；
-   60件17号可用；

下游必须收到两个切片，不能汇总成100件17号。

### 9.4 跨DomainLeadTime不统一硬编码2天

使用真实工厂/Stage已有LeadTime口径。

------------------------------------------------------------------------

## 10. 动态瓶颈

默认使用Load/Capacity动态识别，可由3号位通过AUTO / PREFER_ANCHOR /
FORCE_ANCHOR / NOT_ANCHOR等有限策略覆盖。

V1不建设复杂全局数学多瓶颈最优器。

------------------------------------------------------------------------

## 11. 正排、倒排与混合

正排适合最早承诺、物料刚可用等场景；倒排适合靠近DueDate减少WIP。

V1在同一个Solver内混合使用，不是两套Solver，也不是全正排/全倒排二选一。

------------------------------------------------------------------------

## 12. Solver固定五阶段

### A. 建问题与硬约束

Routing、Resource Eligibility、Material
AvailableTime、Firm/Frozen/Lock、强制拆批、初步瓶颈。

### B. 初始有限产能计划

联合考虑优先关系、瓶颈锚点、正倒排、资源、批次、时间、Setup。

### C. 可行性与延期诊断

检查资源、物料、工艺依赖、Firm/Frozen/Lock和交期。

### D. 有界局部修复

尝试替代资源、有限拆批、前后移动、多瓶颈协调、Setup邻域优化等。

### E. Gap Compaction与最终评价

形成FinalTask、TaskShare、Task-to-Task物理依赖、Explanation Facts和KPI。

V1不是任意可配置Command链。

------------------------------------------------------------------------

## 13. 不是"订单A整单排完再订单B"

1号位面对整个Domain的Demand、Routing、资源、物料和依赖。

2号位业务排序表达"冲突时谁优先保护"，并不意味着高优先订单所有Task全部完成后低优先订单才能开始。

如果高优先Task因物料未到暂时不可生产，低优先Task在不损害更高层履约的前提下可以先占空闲资源。

------------------------------------------------------------------------

## 14. Setup与换型

Setup是资源序列关系。

V1采用局部邻域启发式，不做全局旅行商式优化。

Setup/WIP/利用率属于次级优化，不能为了少换型而让高优先客户订单明显延期。

------------------------------------------------------------------------

## 15. Stage重叠、转运与批次

### 15.1 Stage重叠

业务允许时，下游可以在满足数量门槛后提前开始。

### 15.2 Stage间转运

非有限物流资源的Stage转运直接增加LeadTime。

### 15.3 强制拆批与优化拆批分开

-   物理强制拆批在阶段A执行；
-   优化拆批在阶段B/D只尝试少量候选。

不做无限组合。

------------------------------------------------------------------------

## 16. 目标层级

### 第一层：物理与业务硬约束

资源冲突、Dependency、AvailableTime、Eligibility、Firm/Frozen、Lock、执行事实、数量闭合绝不能违反。

### 第二层：履约优先关系

Demand Protection和高优先客户需求优先。

### 第三层：客户承诺确保率目标

如果存在可行方案达到冻结目标，不得为了Setup/利用率选择更差履约方案。

物理上达不到目标时返回最佳可行结果，不把"无法达标"当Solver失败。

### 第四层：次级优化

总延期、WIP、Setup、利用率、计划稳定性等。

------------------------------------------------------------------------

## 17. 客户按期率

核心KPI建议使用：

> **客户订单整单按时进入目标ZP/BP的比例。**

数量按期率可作为辅助，但不能混成一个指标。

------------------------------------------------------------------------

## 18. 排程终点

APS V1客户生产交期判断终点：

> **最终生产/装配完成并进入目标ZP/BP。**

客户外部运输不进入有限产能生产Solver。

如果业务只需在生产完成后追加固定物流时间，可作为生产之外的确定性LeadTime解释；**不能因此生成有限物流Task。**

------------------------------------------------------------------------

## 19. 白天Candidate为什么不无条件全Domain重排

正常Candidate采用：

> **Base ACTIVE + 一个业务场景的一组同类型变化Seed +
> 当前完整真实上下文 + 动态影响传播 + 最小扰动局部修复。**

一次Candidate可以包含多个同类型Seed，但仍严格一个Domain。

影响过大时允许使用同一Solver扩大到单Domain全部可移动Task重排。

### 19.1 Seed不是Solver事实过滤器

例如本次场景只选择故障设备MC01/MC02：

``` text
ChangedResourceIds = [MC01, MC02]
```

表示从MC01/MC02相关Task开始传播影响。

但Solver仍必须同时看到：

-   MC05此前已经正式修改的新Calendar；
-   当前全部Resource Eligibility；
-   当前资源占用；
-   当前Material AvailableTime；
-   Firm/Frozen；
-   已执行不可逆事实；
-   其它Domain ACTIVE共享资源阻挡。

### 19.2 多Task甘特软目标

一次可以传入多个Task目标：

``` text
TaskA → 08:00
TaskB → 10:30
```

这些是软目标。

如果08:00不可行，1号位允许给出08:30等真实可行结果，并返回Explanation。

甘特场景固定NORMAL，不得因为TargetTime更早自动提升业务优先层级。

### 19.3 多设备故障与Calendar变化

故障资源必须已经成为正式不可用事实；Calendar也必须先正式修改。

Candidate只把这些资源作为变化Seed：

``` text
ChangedResourceIds[]
```

1号位不得读取"假设故障/假设Calendar"作为第二套资源事实。

### 19.4 EXPEDITE仍受硬约束

即使上游`PriorityMode=EXPEDITE`，1号位仍不得突破：

-   Resource Eligibility；
-   Calendar；
-   Routing；
-   Material AvailableTime；
-   已执行事实；
-   Firm/Frozen；
-   外Domain ACTIVE Resource Block；
-   其它既有硬约束。

------------------------------------------------------------------------

## 20. 变化种子由2号位确定

2号位比较Candidate Pegging和Base ACTIVE，形成：

-   新增生产需求；
-   减少需求；
-   数量变化；
-   其它真实逻辑生产变化。

2号位不预先计算完整产能影响范围。

------------------------------------------------------------------------

## 21. 影响范围由1号位动态传播

沿：

1.  工艺Dependency；
2.  实际物料数量---时间关系；
3.  同一资源时间序列；
4.  Setup直接邻居；

传播。

无真实变化就停止。

不按固定产品族、固定天数、固定订单数裁切。

------------------------------------------------------------------------

## 22. 最小扰动优先

先尝试：

1.  原Resource；
2.  原附近时间；
3.  不移动其它Task的空档；
4.  少量合法替代Resource；
5.  最后才挤动普通低优先Task或做优化拆批。

计划稳定性不能阻止正确业务优先关系。

------------------------------------------------------------------------

## 23. 资源传播

变化Task真正改变Start/End/Resource后才继续传播到资源邻居。

Setup插入/移除至少检查原前驱、原后继、新前驱、新后继。

------------------------------------------------------------------------

## 24. 物料传播按真实份额

利用AllocationTaskShare和真实物料消费关系传播。

不能因为"MaterialCode相同"就把所有使用该Material的订单全部加入影响范围。

------------------------------------------------------------------------

## 25. 防振荡与局部修复预算

V1可在1号位内部使用：

-   Task revision stamp；
-   单Task修复次数；
-   全局修复轮次；
-   时间预算；
-   受影响Task比例。

这是Solver技术参数，不是PMC业务规则。

经验初值可沿用v1.0：

-   正常Candidate目标60秒；
-   软预算90秒；
-   局部硬预算180秒；
-   影响比例参考30%；
-   单Task修复5次；
-   全局轮次10；
-   替代资源初筛5；
-   优化拆批候选最多3。

这些值后续只按压测调优，不改变业务模型。

------------------------------------------------------------------------

## 26. LOCAL局部模式兜底：单Domain完整重排

局部影响过大、不收敛或超过预算时，可以把**求解范围**扩大为当前单Domain全部可移动Task重排。

但：

``` text
RunType仍为LOCAL_RESCHEDULE
```

不能因为影响扩大而改成`MANUAL_RESCHEDULE`。

只有用户从一开始明确选择"整个当前Domain全部可移动计划重排"时，才使用MANUAL。

仍固定：

-   已执行；
-   Firm/Frozen；
-   Demand Protection；
-   不可逆Lock；
-   Business Scope外不可移动Task；
-   其它Domain ACTIVE资源阻挡。

仍使用同一个Solver。

------------------------------------------------------------------------

## 27. Scope字段不能硬截断真实影响

旧接口里的MaxImpactedOrders、PlanHorizonStart/End等只能作为：

-   发起范围；
-   提示/警戒阈值；

不能作为"超过就不算"的物理截止线。

真实影响必须传播到闭合或触发单Domain完整重排。

------------------------------------------------------------------------

## 28. 白天共享资源：其它Domain ACTIVE是不可移动阻挡块

如果当前Candidate Domain与其它Domain共享同一设备/资源：

> **V1不允许为了本Domain急单去挤动其它Domain当前ACTIVE计划。**

其它Domain ACTIVE占用在当前Candidate Resource
Calendar中作为不可移动Block。

这样保证：

-   Candidate仍严格单Domain；
-   不产生跨Domain产能影响传播；
-   不需要共享资源配额、借用、虚拟子日历平台。

这会使白天CTP在少数场景偏保守，但属于V1明确选择。

夜间FULL再根据全局实际共享资源情况形成新的正式计划。

------------------------------------------------------------------------

## 29. 白天跨Domain插单CTP

一个Candidate仍严格一个Domain。

如果 `C → B → A`：

1.  C WHATIF Candidate；
2.  C的Quantity-Time传B；
3.  B WHATIF Candidate；
4.  B的Quantity-Time传A；
5.  A WHATIF Candidate；
6.  汇总成一次客户CTP答案。

不建设MultiDomain Candidate和多域原子激活组。

------------------------------------------------------------------------

## 30. 夜间跨Domain失败与人工重算

如果上游B失败：

-   B新PlanVersion=FAILED；
-   依赖B的下游A本次不发布新ACTIVE；
-   A保留旧ACTIVE；
-   无关Domain继续发布。

人工恢复：

-   原FAILED历史保留；
-   新建ScheduleRun；
-   自动识别必要失败上游和被阻断下游；
-   只重算必要依赖链；
-   按拓扑顺序执行。

不建设独立Retry Workflow平台。

------------------------------------------------------------------------

## 31. Candidate采用与审批边界

WHATIF Candidate永远不能自动成为正式计划。

正式采用只要求最小人工授权确认和审计：

-   谁确认；
-   何时确认；
-   采用哪个Candidate。

如果2号位当前代码没有审批逻辑，V1不新增完整OA/BPM审批平台。

如果已有审批体系过重且阻碍V1，可简化或旁路，不把审批变成排程主链依赖。

------------------------------------------------------------------------

## 32. Firm/Frozen与真实执行连续性跨版本保持

V1停止建设完整FrozenZoneSnapshot平台，但Firm/Frozen业务语义必须继续存在。

夜间新PlanVersion中，2号位从上一ACTIVE读取仍有效的Firm/Frozen事实并转换为本次1号位不可移动锚点。普通上一版本TaskId不会直接复用，普通未锁Allocation仍可重新竞争。

真实MES `IN_PROGRESS`执行则必须在当前Q形成后转成逐MES工单Continuation
Slice，与Free Slice一起进入1号位，不能简单从Q中扣掉以后只排Q-E。

### 32.1 同PI同Operation先后与并行红线

``` text
既存执行连续份额
→ 新增自由份额
```

1号位不得反转上述顺序；APS自动求解不得让两者在不同设备上时间重叠。现场已经存在的人工并行事实可接受，但不得主动新增自动并行。

### 32.2 工序级资源偏好

工序级`LastReportResourceCode`可作为PreferredResource：满足Eligibility时优先，不满足时允许顺序换其它合法资源。除非另有Execution/Frozen/Firm硬约束，该字段本身不是Hard
Lock，也不能提升为整张MES工单唯一设备。

不依赖FrozenZoneSnapshot重新建立第二状态平台。

### 32.3 设备故障与Firm/Frozen

故障设备上的已执行部分不可逆。

对尚未执行但位于Firm/Frozen范围内的Task：

-   不能因为设备故障就自动突破Fence；
-   无授权时返回需要人工授权重排；
-   有既有授权时才允许调整未来未执行部分；
-   不新增故障专属审批平台。

## 33. Explanation Facts由1号位原生产出

1号位在求解过程中已经知道真正约束，因此应产生结构化Explanation
Fact，包括：

-   TargetTime / ActualTime；
-   ConstraintType；
-   Material / Resource / Blocking Task；
-   ImpactHours；
-   Direct Cause；
-   Root Cause；
-   Evidence。

V1不保存完整Solver搜索轨迹。

### 33.1 根因不能机械累加所有等待

只沿最终有效关键路径识别真正影响最终完工的约束。

### 33.2 DUE_DATE_RISK只是结果

必须继续落到MATERIAL_SHORTAGE、RESOURCE_CAPACITY_WAIT、PRECEDENCE_WAIT、FROZEN_ZONE_LOCK、LOGISTICS_DELAY等真实根因。

LOGISTICS_DELAY可来自外部运输事实延误，但不意味着物流进入有限产能Task体系。

------------------------------------------------------------------------

## 34. 插单结果必须服务销售与PMC

一次Candidate至少回答：

-   是否按期；
-   最早日期；
-   是否依赖ESTIMATED采购占位；
-   影响订单数；
-   哪些仍按期；
-   哪些延期；
-   是否破坏Demand Protection；
-   主要瓶颈；
-   主要根因。

依赖Planning-only Purchase Placeholder时必须显著标记：

> **估算日期，非确定性承诺。**

------------------------------------------------------------------------

## 35. 正常案例

客户订单100件，需要自制C100、采购P100。

Pegging：

-   C已有PI60，新增40；
-   P库存60，PO40在8月12日可用。

2号位反算计划良率、组装Quantity-Time约束；1号位按Routing、Eligibility、Calendar和PlannedProcessQty形成FinalTask并判断交期。

------------------------------------------------------------------------

## 36. 白天插单案例

A受保护60、B普通100、PI-C01剩余140。

急单U70进入：

-   A60不动；
-   U获得70；
-   B只剩10，新增90生产缺口。

1号位从Base ACTIVE做局部影响传播；如果共享设备上还有其它Domain
ACTIVE，则那些占用只作为不可移动阻挡块，不被本次Candidate挤走。

------------------------------------------------------------------------

## 37. 跨Domain CTP案例

A最终装配依赖B电机，B又依赖C轴承。

后台C→B→A三个单Domain Candidate依次计算。

如果C分40件15号、60件17号完成，B应收到两个Quantity-Time切片，而不是100件17号单点供给。

------------------------------------------------------------------------

## 38. 延期解释案例

SO-A要求8月18日18:00：

-   自制C 8月15日12:00齐；
-   采购P最后100件8月16日08:00可用；
-   ASSY唯一合法资源被受保护订单占用到8月17日12:00；
-   最终8月19日10:00完成。

最终解释应突出：

1.  采购P决定最早物料齐套；
2.  齐套后ASSY资源又被受保护任务占用；
3.  最终晚16小时。

无关的历史等待不能机械叠加为主根因。

------------------------------------------------------------------------

## 39. 90天计划的周/月/季度视图

周、月、自然季度只是同一90天FinalTask结果的聚合视图。

自然季度只看90天窗口与季度区间交集。

不另跑周排/月排/季度排；年度详细排程不进入V1。

------------------------------------------------------------------------

## 40. 90天计划与采购参考需用日期

采购件参考Required
Date应来自真实消费Task计划开始时间，而不是简单用客户DueDate减固定LeadTime。

V1只提供采购计划参考，不自动创建正式采购单。

------------------------------------------------------------------------

## 41. 异常与失败

业务数据异常在5号位/2号位按规则先保守降级后再进入1号位。

有限产能硬错误，例如：

-   非法Resource；
-   无法解释的资源冲突；
-   TaskShare数量不闭合；
-   跨版本非法引用；
-   工艺依赖被破坏；

必须使Domain失败。

> **无法按客户DueDate完成不是Solver失败，而是正常业务结果。**

------------------------------------------------------------------------

## 42. 正式结果输出与闭合

1号位至少输出：

1.  FinalTask；
2.  Allocation↔FinalTask数量份额；
3.  Task↔Task物理依赖；
4.  Explanation Facts；
5.  求解状态和关键KPI。

满足：

`AllocationQty = Σ AllocationTaskShare中的预计合格产出份额`

PlannedProcessQty不进入Allocation净需求闭合。

订单完成时间取满足该订单全部必要数量份额后的最晚完成时间。

------------------------------------------------------------------------

## 43. MES发布资格

只有正式、可执行、业务身份完整的Task才能下发MES。

以下不得绕过正式发布资格：

-   Candidate Task；
-   无PI号虚拟占位Task；
-   UNLOCATED保守规划Task；
-   其它仅用于估算的规划对象。

Planning-only Purchase Placeholder本身不是Task，更不得下发MES。

------------------------------------------------------------------------

## 44. V1明确不建设

V1有限产能主链不建设：

-   第二套远期粗排Solver；
-   任意Command链；
-   动态脚本/DSL算法平台；
-   MultiDomain Candidate；
-   独立LocalSolver；
-   持久化Impact Graph；
-   SolverTrace全搜索历史；
-   因果图平台；
-   有限物流Task体系；
-   车辆/月台/班次资源模型；
-   跨Domain共享资源配额/借用平台；
-   通用Capacity Reservation状态机；
-   无限Route搜索；
-   数学全局多瓶颈最优器；
-   无限拆批组合；
-   巨型加权总目标函数；
-   周/月/季度/年度独立排程引擎；
-   FrozenZoneSnapshot状态平台；
-   完整BPM审批作为排程主链依赖。

------------------------------------------------------------------------

## 45. V1实现深度与最终冻结红线

V1实现"完整能力边界 + 简单可靠启发式"：

-   动态瓶颈：Load/Capacity；
-   多瓶颈：有序锚点+局部协调；
-   资源选择：有限候选；
-   拆批：少量候选；
-   Setup：邻域启发式；
-   正倒排：组合时间槽搜索；
-   Gap Compaction：简单有效版本；
-   Candidate：变化传播+有界局部修复；
-   极端Candidate：单Domain全排兜底。

最终冻结：

1.  90天一套统一有限产能计划。\
2.  2号位不提前生成FinalTask。\
3.  FinalTask/Resource/Start/End/拆合批由1号位决定。\
4.  Net Required Qty与Planned Process Qty分开。\
5.  Supply统一以Quantity + AvailableTime约束1号位。\
6.  Planning-only Purchase Placeholder只用于估算，不构成确定性CTP承诺。\
7.  1号位不得自行替换2号位已选Supply。\
8.  动态瓶颈为默认，正倒排在同一Solver内混合。\
9.  履约和Demand Protection高于Setup/WIP/利用率。\
10. Candidate按变化种子动态传播，不按固定天数/订单数硬截断。\
11. 局部不稳定时用同一Solver升级单Domain完整重排。\
12. 白天其它Domain ACTIVE共享资源占用是不可移动Block。\
13. V1不建设跨Domain资源配额、借用、影响传播平台。\
14. 跨Domain CTP由多个单Domain Candidate串行完成。\
15. 跨Domain Quantity-Time保留分段。\
16. 上游Domain失败时依赖下游不发布；FAILED人工恢复新建ScheduleRun。\
17. WHATIF不自动激活，正式采用只需最小人工授权确认。\
18. Firm/Frozen跨版本转成新Run不可移动锚点，不建设FrozenZoneSnapshot平台。\
19. V1绝不建设有限物流Task体系。\
20. 无法按期是正常业务结果，必须返回最佳可行日期和根因。\
21. Explanation由1号位原生产出。\
22. Candidate、无PI虚拟Task、UNLOCATED规划Task不得下发MES。\
23. V1采用简单确定可压测的启发式，不为理论最优建设通用优化平台。

------------------------------------------------------------------------

## 46. 一句话理解

> **2号位先把"做什么、为谁做、多少、物料什么时候可用"算清楚；1号位再在90天真实有限资源上决定"在哪台设备、什么时候做、如何拆合批、最终什么时候完成"。白天插单从变化点动态传播，能局部就局部，影响过大就单Domain全排；跨Domain按多个单Domain
> Candidate串行评估；最终向销售和PMC给出可承诺日期、影响范围和真实根因。**

------------------------------------------------------------------------

**状态**：v1.1
冻结对齐版。后续只接受符合性修正，不接受开放式业务再设计。

------------------------------------------------------------------------

# 2026-09-01 Domain专项历史形成记录（当前v1.7继续继承）

# APS 有限产能排产与滚动90天计划业务说明 v1.2（Domain专项一致性修订版）

**适用版本**：APS V1\
**文档性质**：业务与算法口径专项说明\
**日期**：2026-10-05（北京时间）\
**基于版本**：《APS 有限产能排产与滚动90天计划业务说明
v1.1（冻结对齐版）》\
**上位基线**：《APS V1最终全部流程与业务基线 v1.2》\
**专项依据**：《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》

> **冻结声明**：本文完整继承v1.1全部有限产能业务，不重新设计90天统一Solver、FinalTask、Candidate、Quantity-Time、Firm/Frozen、物料约束、拆合批、Setup、多目标优化和MES资格。本版只补齐Domain定义与分域执行边界。

------------------------------------------------------------------------

## 1. 本版唯一修订范围

本版只补充：

1.  1号位一次只求解一个真实Domain；
2.  Domain由上游DomainDefinition决定；
3.  Domain可以是FAMILY或FACTORY_FAMILY；
4.  当前夜间FULL由2号位串行逐Domain调用同一套Solver；
5.  前序成功Domain的共享Resource占用作为后续Domain不可用时间块；
6.  Candidate继续单Domain并使用外Domain不可移动Resource Block；
7.  未来并行只在无依赖、无共享Resource时允许；
8.  按Domain记录Solver和全链路性能。

------------------------------------------------------------------------

## 2. 90天仍然只有一套正式有限产能Solver

原v1.1冻结不变：

> 90天滚动计划仍然使用一套正式有限产能Solver。

"逐Domain求解"表示：

``` text
同一套有限产能Solver
×
多个独立Domain问题实例
```

不是：

-   每个Domain一套算法；
-   近期一套、远期一套；
-   新增第二Solver。

------------------------------------------------------------------------

## 3. 1号位不定义Domain

1号位收到的DomainKey已经由上游确定。

1号位不判断：

-   Domain是不是ProductFamily；
-   Domain是不是Factory；
-   当前ScopeType是FAMILY还是FACTORY_FAMILY；
-   Order属于哪个Domain。

1号位只消费：

-   DomainKey；
-   单Domain完整DomainSolveRequest；
-   Routing；
-   Resource；
-   Calendar；
-   Material Quantity-Time；
-   Strategy；
-   Firm/Frozen；
-   外部Resource Block。

------------------------------------------------------------------------

## 4. Domain可以是两种Scope

V1只有：

-   FAMILY；
-   FACTORY_FAMILY。

这只影响上游如何形成一个Domain问题，不改变1号位内部求解算法。

因此：

> 1号位不得因ScopeType不同建设两套Solver路径。

------------------------------------------------------------------------

## 5. 夜间FULL的逐Domain求解

正式运行模型：

``` text
1 ScheduleRun
↓
多个真实DomainKey
↓
每Domain独立PlanVersion
↓
2号位逐Domain构建DomainSolveRequest
↓
1号位Solve
↓
2号位验证与持久化
```

当前V1：

> **串行逐Domain执行。**

------------------------------------------------------------------------

## 6. 不恢复旧多Job并行架构

本版不要求恢复：

-   DomainSchedulingJob；
-   DomainLayerCoordinatorJob；
-   多Hangfire Domain Job并发。

2号位应在现有调度主外壳内部形成稳定单Domain执行边界。

1号位只保持：

> 单Domain Solver契约稳定。

------------------------------------------------------------------------

## 7. FULL共享Resource互斥

即使不同Domain拥有独立PlanVersion，真实共享设备仍不能双占。

串行执行时：

``` text
Domain A完成求解
↓
形成共享Resource占用
↓
作为后续Domain不可占用Block
↓
Domain B求解
```

1号位必须把这些Block作为真实不可占用时间约束消费。

不建设：

-   Domain Resource Quota；
-   跨Domain资源借用；
-   虚拟子日历平台。

------------------------------------------------------------------------

## 8. Candidate外Domain Resource Block

Candidate继续严格单Domain。

其它Domain当前ACTIVE在共享Resource上的占用必须作为：

> **外部Domain资源阻挡块（ExternalDomainResourceBlocks）**

传给1号位。

1号位不得在Candidate中挤动其它Domain当前ACTIVE任务。

------------------------------------------------------------------------

## 9. 跨Domain Quantity-Time保持原冻结

上游Domain产出完成时间加真实LeadTime后，向下游传：

``` text
Material + Quantity + AvailableTime
```

必须保持切片。

例如：

``` text
40件 @ 15日
60件 @ 17日
```

1号位必须按真实数量---时间约束消费，不得压平。

------------------------------------------------------------------------

## 10. Domain_Dependency与求解顺序

Domain_Dependency使用真实DomainKey。

2号位负责：

-   读取依赖；
-   拓扑排序；
-   同层稳定排序；
-   按顺序发起单Domain求解。

1号位不负责跨Domain拓扑编排。

------------------------------------------------------------------------

## 11. 上游失败后的下游求解

如果上游Domain失败，依赖下游本次不得继续形成可发布的新ACTIVE。

是否阻断下游由2号位运行编排判断。

1号位不需要为失败Domain继续生成一个"假结果"。

------------------------------------------------------------------------

## 12. 当前不并行Domain

当前V1不要求Domain并行。

原因不是业务不允许未来并行，而是：

-   共享Resource协调；
-   失败传播；
-   状态并发；
-   内存峰值；
-   事务一致性；

都会显著增加复杂度。

在没有真实分域压测之前，不提前增加该复杂度。

------------------------------------------------------------------------

## 13. 未来受控并行边界

未来只有同时满足：

``` text
无未完成上游依赖
AND
无共享Resource
```

的Ready Domain允许并行。

即：

``` text
Dependency(D1,D2)=NONE
AND
Resources(D1) ∩ Resources(D2)=∅
```

这不改变1号位单Domain Solver接口。

------------------------------------------------------------------------

## 14. 性能测量

原约10万FinalTask / 90天 / 夜间FULL约15分钟目标不变。

新增按Domain统计：

-   DomainKey；
-   DemandCount；
-   LogicalProductionDemandCount；
-   FinalTaskCount；
-   ContextBuildMs；
-   PeggingMs；
-   SolverMs；
-   PersistMs；
-   TotalMs。

其中1号位至少应能提供或支持：

-   SolverMs；
-   FinalTaskCount；
-   求解诊断。

最终ScheduleRun还记录总耗时。

------------------------------------------------------------------------

## 15. 与多目标优化的关系

原多目标优化层级不变。

分域不会把多目标优化改成：

-   每Domain不同目标体系；
-   Domain间资源竞价；
-   Domain配额优化。

当前通过：

-   拓扑顺序；
-   共享Resource Block；
-   单Domain统一Solver目标；

保持V1简单闭合。

------------------------------------------------------------------------

## 16. 本次明确不改变

以下全部沿用v1.1：

-   FinalTask由1号位形成；
-   2号位不能预生成最终Operation Task；
-   PlannedProcessQty占产能；
-   计划良率由2号位反算；
-   物料Quantity-Time；
-   Planning-only Purchase Placeholder；
-   动态瓶颈；
-   正排/倒排/混合；
-   Solver固定五阶段；
-   Setup；
-   Stage重叠；
-   强制/优化拆批；
-   履约目标；
-   Candidate动态影响传播；
-   最小扰动；
-   MES发布资格；
-   不建设有限物流Task。

------------------------------------------------------------------------

## 17. 本次明确不新增

-   MultiDomain Solver；
-   MultiDomain Candidate；
-   Domain资源配额；
-   跨Domain借用平台；
-   VirtualInventoryBalance；
-   当前Domain并行；
-   第二套远期Solver；
-   Domain DSL；
-   1号位自行归域。

------------------------------------------------------------------------

## 18. 最终一致性口径

> **APS
> V1仍然只有一套90天正式有限产能Solver。2号位先按照真实DomainDefinition和Domain_Dependency形成单Domain求解问题，当前夜间FULL按稳定拓扑顺序串行调用1号位；1号位一次只求解一个Domain，不解释FAMILY或FACTORY_FAMILY，也不重新归域。前序Domain已形成的共享Resource占用和Candidate场景下其它Domain
> ACTIVE占用，都必须作为不可移动资源阻挡块进入后续单Domain求解。**

------------------------------------------------------------------------

# 2026-09-04 PI Position执行起点上下文历史形成记录（当前v1.7继续继承）

## 1. 本次专项修改范围

本次仅补充：

有限产能排程如何消费已存在生产执行进度形成的执行起点。

不改变：

-   90天统一Solver；
-   FinalTask生成职责；
-   Candidate；
-   Quantity-Time；
-   Firm/Frozen；
-   MES发布资格。

------------------------------------------------------------------------

## 2. LogicalProductionDemand执行起点上下文

LogicalProductionDemand除原有：

-   数量；
-   交期；
-   Allocation血缘；
-   Material；
-   Factory；

外，可携带：

-   StartStage；
-   必要时StartOperation；
-   RoutingReference；
-   NextOperationContext。

------------------------------------------------------------------------

## 3. 1号位责任

1号位根据：

``` text
LogicalProductionDemand
+
StartStage/StartOperation
+
Routing
+
Resource
+
Calendar
```

展开后续工艺并进行有限产能计算。

1号位不负责：

-   读取MES执行事实；
-   判断PI已经完成哪些工序；
-   重新计算PI Position。

------------------------------------------------------------------------

## 4. 示例

Routing：

``` text
NC
 ↓
挤丝
 ↓
研磨
```

MES事实：

``` text
NC完成800
```

执行上下文：

``` text
800件 StartOperation=挤丝
```

1号位排程：

``` text
挤丝 → 研磨
```

剩余未完成数量仍按真实执行位置进入对应Routing路径。

------------------------------------------------------------------------

## 5. 冻结红线

执行起点上下文用于：

-   指导后续排程路径。

不用于：

-   改变Supply身份；
-   改变Pegging粒度；
-   建设Operation级库存模型。

------------------------------------------------------------------------

------------------------------------------------------------------------

# v1.6 本轮联合冻结修订内容（合并章节）

## Operation规划模式（Operation Planning Mode）

APS
V1不再以"有设备/无设备"作为有限产能判断依据，而采用Operation规划模式。

-   FINITE_RESOURCE：进入有限产能Solver，占用Resource Timeline。
-   UNCONSTRAINED：保留工艺时间，不占有限Resource。
-   WAIT_ONLY：表达等待时间，不占有限Resource。

MES Routing继续负责工艺事实，APS负责排程规划语义。

## 人工有限产能模型

人工统一复用Resource模型：

Resource + ResourceCalendar + OperationResourceEligibility。

不建设独立人工排程体系。

## Setup换型模型

Setup不再依赖旧SetupAttribute或RoutingOperation.SetupTime作为运行真相。

Setup依据：

ProductionDepartment + Stage + CurrentOperation + Resource +
FromMaterial + ToMaterial。

Setup属于Resource真实占用：

Task A → Setup → Task B。

## 用户输出边界

V1保留：

-   订单交付判断；
-   延期原因解释；
-   Task影响订单；
-   Supply/Pegging追溯；
-   Candidate影响分析；
-   基础版本追溯。

V1.1后移：

-   完整Resource Load；
-   Setup KPI；
-   执行风险；
-   完整Version Diff。

## 职责同步

1号位负责Solver、FinalTask、Resource时间真相。

2号位负责数据准备、结果持久化。

3号位负责规则与参数治理。

4号位负责页面与配置。

5号位负责事实查询组合。