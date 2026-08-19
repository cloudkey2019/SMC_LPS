# APS V1 最终全部流程与业务基线

**版本**：v1.0（最终业务基线）  
**冻结日期**：2026-08-12  
**适用范围**：APS V1 交付、0～5号位开发、后续六份权威技术文档统一修订、代码整改与验收  
**文档性质**：业务流程与算法边界的最高优先级基线之一  

---

## 0. 本文档的权威性与使用原则

### 0.1 本文档如何形成

本文档汇总并统一以下来源：

1. 0号位与AI在APS项目历次讨论中**明确确认**的业务裁决；
2. AI提出、0号位在后续讨论中**未明确反对**的修改建议——按0号位本次确认，均视为同意并纳入本基线；
3. 当前六份APS权威文档中仍然有效、且未被后续讨论覆盖的内容；
4. 2号位实际提交代码与数据库事实复核中，已经确认应保护、复用或退出V1主链的内容；
5. 最近完成的Pegging、有限产能、插单、跨厂、跨Domain、异常恢复等完整业务案例闭环。

### 0.2 冲突优先级

后续如果旧文档、旧代码注释、旧表结构与本文冲突，按以下优先级处理：

> **0号位最新明确裁决 > 本文档最终基线 > 后续按本文统一修订后的六份权威文档 > 旧版本文档/历史代码/历史注释。**

本文并不要求为了“文档一致”而推翻已经能工作的代码。所有技术整改必须遵循：

> **保护2号位现有代码外壳，优先停用冲突旧路径、补核心闭环，不为了架构漂亮而重写。**

### 0.3 V1的总原则

APS V1必须同时满足：

- **能按期交付**；
- **结果业务上可用**；
- **未来可扩展**；
- **当前不过度设计**；
- **不把所有任务压在2号位**；
- **也不能为了分工而硬拆插件、接口和表。**

V1的设计标准是：

> **稳定主流程 + 真正可变的规则/参数 + 少量必要复杂事实服务 + 单一有限产能Solver。**

---

# 第一部分  APS V1目标、范围与红线

## 1. V1核心业务目标

APS V1必须真正解决以下问题：

1. 90天订单完整BOM与完整Pegging；
2. 自制件、采购件、库存、PI、跨厂等供给的统一承接；
3. 真实有限产能排程；
4. 可靠交期判断（CTP）；
5. 白天插单及时评估；
6. 多工厂、大工艺接续和厂间订单型跨厂；
7. 采购/VMI等外部供给的可用时间约束；
8. MES执行事实反馈后次日重新计算；
9. 失败Domain可恢复、可人工重算；
10. 对延期、瓶颈、物料等待等原因可解释。

## 2. 90天计划的最终口径

### 2.1 90天全部订单都进入正式计算

V1滚动90天内的全部活跃订单：

> **完整BOM + 完整Pegging + 全量进入有限产能时间推演。**

90天不是“只有近期精排、远期只看日产量”的两套正式计划。

### 2.2 一套Solver，不预建第二套远期粗排算法

V1首先使用**同一套有限产能求解能力**覆盖90天。

近期与远期可以在工程实现中采用不同的搜索深度，例如远期减少候选设备、减少拆批尝试，但：

- 资源语义一致；
- Task仍占用真实未来产能；
- 不能把31～90天换成另一套完全不同的“日产量桶算法”后再拼接。

只有真实压测证明统一方案无法满足性能目标后，才允许重新评估远期降级；V1不提前建设第二套Solver。

### 2.3 周/月/季度只是同一90天计划的管理视图

- 周计划：按周聚合；
- 月计划：按月聚合；
- 季度：查看90天窗口与自然季度的交集；
- 年度详细排程不进入V1。

不单独再跑周、月、季度、年度四套排程。

## 3. 明确不进入V1的过度设计

V1明确不建设：

- MultiDomain Candidate；
- 跨Domain共享资源配额/借用平台；
- 有限物流Task体系；
- 车辆、班次、月台有限资源排程；
- 第二套远期粗排Solver；
- 任意脚本/DSL规则平台；
- Asprova式用户自定义Command链；
- 动态插件市场/运行时插件注册平台；
- 持久化Impact Graph；
- Solver完整搜索轨迹数据库；
- 通用因果图平台；
- 无限拆批组合；
- 数学全局最优多瓶颈平台；
- VirtualInventoryBalance持久化状态机；
- FrozenZoneSnapshot完整平台；
- 为普通插单强制建设Scenario/Simulation平台；
- 复杂多级审批作为排程主链前置；
- 为了“规范化”给所有表大量新增主外键关联。

---

# 第二部分  核心对象和数量语义

## 4. 客户订单（SALES_ORDER）进入APS时的数量含义

客户订单进入APS前，ERP已经完成成品库存检查/占用。

因此：

> **APS收到的SALES_ORDER.Quantity就是仍需要生产的数量。**

APS不得再次查普通成品库存冲抵客户订单，否则会重复扣减。

例如：

- ERP客户需求120；
- ERP已用成品库存满足20；
- APS Order.Quantity=100；

则APS从100开始找生产供给。

## 5. 订单类型

V1统一：

- SO/MTO → `SALES_ORDER`；
- MTS/SS/SS_U → `PRODUCTION_INSTRUCTION`。

不同订单类型在同一计算层内可以穿插排序，不允许简单写死成“客户订单全部排完后再MTS”。

## 6. 生产指示（PI）剩余总量

生产指示的总剩余量以ERP边界为准。

原则：

> **PI RemainingQty = ERP所定义的尚未最终入目标M库的全部剩余数量。**

其中可能包含：

- 未开工；
- MES在制；
- Stage间等待；
- PI级XC；
- PI级跨厂在途。

MES完成量、XC和在途不能再次加到PI剩余总量上；它们只用于判断PI剩余量“现在在哪里”。

## 7. 计划良率与数量语义

V1正式区分：

- **净需求量（Net Required Qty）**：最终需要的合格数量；
- **计划加工量（Planned Process Qty）**：考虑计划良率后预计要投入的加工数量。

例如：

- 需求合格100；
- MACH计划良率98%；

则MACH可能计划加工约102～103，但：

> Allocation仍然只承接100。

额外预计损失不属于任何客户Demand。

### 7.1 已有PI不能因为计划良率被“放大”为Supply

PI剩余60，就是Supply 60。

不能因为预计后续有损失而把PI变成62件Supply。

如果确实需要额外生产来保证60件合格输出，额外数量形成新的生产需求。

### 7.2 当前不假设BOM存在ScrapRate参与V1反算

V1不依赖BOM ScrapRate做材料级反算。

未来如果BOM存在材料损耗，它与Stage计划良率是不同业务语义，不得因为字段名字类似而自动叠加。

---

# 第三部分  0～5号位最终职责与“谁动数据库”红线

## 8. 0号位

负责：

- 业务红线；
- 核心优先规则；
- V1/V2范围裁决；
- 跨团队冲突最终裁决；
- 重大规则参数的业务确认。

不负责：

- Solver技术阈值；
- 内存队列、索引等工程细节。

## 9. 1号位：有限产能和时间真相

负责：

- FinalTask；
- 实际Resource；
- Start/End；
- 最终拆批/合批；
- Task-to-Task真实物理依赖；
- 动态瓶颈；
- 正排/倒排；
- Setup；
- 受影响范围传播；
- Candidate局部修复；
- Explanation Fact。

**数据库红线：1号位纯内存计算，不直接读写APS业务数据库。**

所需数据由2号位组装成ScheduleContext输入。

## 10. 2号位：主流程、Pegging和数量真相

2号位必须继续负责APS主流程实现。

核心职责：

- SchedulingOrchestrator主入口；
- ScheduleContext组装；
- Demand/Supply池；
- 分层Pegging；
- DemandBalance / SupplyBalance；
- Allocation；
- Lock执行；
- 跨Domain数量—时间传递；
- 计划良率反算；
- 形成逻辑生产需求；
- 调用1号位一次；
- 统一结果持久化；
- PlanVersion结果落盘；
- Candidate变化种子识别；
- MES下发资格所需运行结果准备。

**数据库红线：2号位是APS运行时业务结果表的唯一主要写入者。**

包括Task、Allocation/Pegging结果、PlanVersion结果、Explanation落库等。

2号位不应：

- 提前生成最终Operation Task；
- 在运行过程中永久修改库存快照余额；
- 自己做第二套有限产能求解；
- 把可变业务逻辑全部硬编码在主流程。

## 11. 3号位：规则、参数与运行生命周期

负责：

- 规则/参数编辑；
- 校验；
- 发布；
- 不可变版本；
- StrategyProfile绑定；
- ScheduleRun生命周期；
- Candidate版本壳与激活边界。

3号位可以写：

- Rule/Parameter/Strategy治理表；
- ScheduleRun/PlanVersion生命周期元数据；
- 激活相关元数据。

3号位不能：

- 修改Task/Pegging运行结果；
- 修改库存运行余额；
- 成为逐Demand、逐Task的中央规则服务。

正确模式：

> 运行开始前加载冻结规则快照，1/2号位在内存执行。

## 12. 4号位：页面和业务操作入口

负责：

- 甘特图；
- CTP；
- 插单影响；
- Candidate比较；
- 异常展示；
- 人工确认；
- 参数维护页面。

**4号位不直接修改APS运行时业务表。**

所有正式状态变更必须通过3号位/2号位服务完成。

## 13. 5号位：ODS、防腐事实、BOM与复杂事实计算

负责：

- ODS；
- BOM展开；
- Stage路径；
- ProcessCode/ERPProperty真实业务属性；
- 跨厂事实；
- PI位置计算；
- 采购/VMI等外部供给事实准备；
- 数据质量Issue。

5号位可以写：

- ODS/MES_Integration中的自己负责的事实与防腐结果；
- 自己责任范围的数据质量结果。

5号位不能：

- 直接修改APS Task；
- 直接修改Pegging/Allocation；
- 修改运行时SupplyBalance；
- 生成最终有限产能Task；
- 决定最终延期ReasonCode；
- 每分配一条供给都回调一次决定业务动作。

---

# 第四部分  规则、参数、插件的最终边界

## 14. 规则引擎只治理真正“可变”的业务

规则/参数分四类：

1. **业务规则（Rule）**：什么条件下采用什么业务策略；
2. **业务参数（Parameter）**：LT、良率、批量、余裕等数值；
3. **事实（Fact）**：Routing、设备资格、日历、ETA等客观事实；
4. **算法硬约束（Invariant）**：数量不能负、设备不能冲突、Supply不能重复消费等。

只有前两类进入3号位治理。

## 15. V1规则引擎只保留两种基础执行模式

### 15.1 有序规则第一命中

用于：

- 需求排序；
- Demand Protection触发；
- 少量资格类规则。

### 15.2 全局默认 + 范围例外覆盖

用于：

- 采购LT；
- 计划良率；
- 正倒排策略；
- 瓶颈策略；
- 拆合批；
- Stage重叠；
- 其它参数。

V1不建设第三套通用脚本语言。

## 16. 需求排序不做全局PriorityScore

每个计算层内部按：

> **有序规则段（Ordered Priority Segments） → 第一命中 → 段内排序。**

例如一层内可以出现：

1. 满足某条件的客户订单前10；
2. 满足某条件的补库指示；
3. 其它紧急客户订单；
4. 普通MTS。

不同订单类型可以穿插。

排序条件可以使用：

- 客户；
- DueDate；
- IssueDate；
- 库存消耗量；
- DelayStatus；
- OrderType；
- 其它已存在稳定业务事实。

**需求排序由2号位主流程执行，规则由3号位配置。**

不为排序再拆一个5号位运行时插件。

## 17. 插件最终原则

插件/独立服务只用于：

> **业务复杂度高、变化大、且确实需要由另一个责任位独立维护的计算。**

当前最典型的必要复杂服务是：

> **生产指示位置计算（PI Position Calculator）由5号位负责。**

下列逻辑留在主流程或1号位，不为了分工硬拆插件：

- Demand排序执行；
- Supply余额扣减；
- PI按创建时间选择；
- Inventory资格/优先消费；
- FinalTask拆批/合批；
- 正倒排；
- 动态瓶颈；
- Candidate影响传播。

V1不建设通用 `IPeggingRuleService` / 插件注册市场。

---

# 第五部分  夜间FULL完整流程

## 18. 凌晨数据准备

夜间FULL沿用现有数据准备主链，包括：

- 活跃根需求；
- Order_Canonical/Order快照；
- Material/Factory/ProductFamily；
- Resource；
- Routing；
- BOM展开；
- StageDetail/CrossFactoryEdge；
- Inventory；
- MES WorkOrder/Operation/Stage进度；
- 采购/VMI/已到厂未入库等外部供给事实；
- 规则参数冻结版本。

**ScheduleRun/PlanVersion具体创建时序不在本文强迫2号位改写。**

只要满足：

- 本次运行拥有明确ScheduleRun；
- 每个Domain拥有独立PlanVersion；
- 数据与版本可追溯；
- 最终运行状态闭合；

即可由2号位按现有实现把握具体时序。

## 19. 夜间Domain编排

一次FULL_SCHEDULE：

- 一个ScheduleRun；
- `ExpectedDomainKeysJson`冻结本次预期Domain集合；
- 每个Domain独立一个PlanVersion；
- 按Domain_Dependency拓扑顺序计算。

### 19.1 发布规则

- 无依赖的Domain独立发布；
- 一个无关Domain失败不得阻止其它Domain发布；
- ScheduleRun允许 `PARTIAL_SUCCESS`。

### 19.2 上游Domain失败

若：

`B → A`

本次B失败，则：

- B新PlanVersion=FAILED；
- A本次不得发布新ACTIVE；
- A继续保留上一ACTIVE；
- 与B无依赖的Domain继续正常发布。

这样保证真实依赖链内版本一致，但不恢复全域ALL_OR_NOTHING。

## 20. 失败后的人工重新计算

必须支持人工重新计算。

原则：

- 原FAILED ScheduleRun/PlanVersion永久保留；
- 不能把FAILED改回RUNNING；
- 人工重算创建新的ScheduleRun；
- 后台自动根据Domain_Dependency确定必要的失败上游和被阻断下游；
- 只重算必要依赖链。

例如：

`C → B → A`

凌晨：

- C成功；
- B失败；
- A未发布；

修复B后：

> 新Run只计算 `B → A`，C不重复计算。

如果B真正失败原因来自C仍未恢复，则系统自动把C纳入本次重算链。

---

# 第六部分  Pegging总流程

## 21. Pegging的职责

Pegging只回答：

1. 谁需要什么；
2. 哪些Supply承接多少；
3. 还缺多少；
4. 缺口是否形成新的生产需求。

Pegging不决定最终设备时间。

## 22. 计算层级

最终分层为：

### 第一层：顶层独立需求

包括：

- SALES_ORDER；
- 顶层MTS/SS/SS_U；
- 其它独立生产指示型需求。

### 第二层：厂间出荷指示未生产份额

顶层Demand如果由厂间出荷指示SH承接，则当前层到SH结束。

SH未生产份额进入下一层，成为源工厂生产需求。

### 第三层及后续：PI/BOM下阶需求逐层扫尾

逐层展开直到：

- 被库存；
- 被PI；
- 被采购/VMI；
- 或形成新的生产需求；
- 或确认缺口。

不额外建设DemandCandidateGroup平台。

## 23. Demand与Supply双边余额

每次成功Allocation必须原子完成：

- DemandBalance减少；
- SupplyBalance减少；
- 生成稳定AllocationSequence；
- 形成Allocation记录/逻辑账本。

满足：

`DemandQty = AllocatedQty + RemainingDemandQty`

`SupplyQty = AllocatedQty + RemainingSupplyQty`

V1需要**逻辑原子账本能力**，但业务基线不强迫新增独立`PeggingAllocationLedger`物理表。

如果现有Allocation/PSA结构可以承接持久化，就优先复用，避免为了DTO存在再建新表。

## 24. 供给身份红线

同一物理数量同一PlanVersion中只能有一个Supply身份。

不能把：

- PI总量；
- PI内部XC；
- PI内部在途；

重复当成三份Supply。

---

# 第七部分  PI选择与PI位置

## 25. PI选择顺序

同一Material有多个PI时：

> **默认按创建/发行时间先后，尽量用完一个PI再进入下一个PI。**

不同物料之间没有PI排序问题。

PI是Demand承诺的最小生产指示单位。

## 26. 先选PI，再消费PI内部位置

例如：

`Demand A ← PI-01 500`

先建立PI级数量承诺，再判断500件内部位于：

- Stage后待入；
- XC；
- 在途；
- 未完成Stage。

禁止把各PI的Position全部打散后再全局排序。

## 27. 生产指示位置可能事实包

2号位负责给5号位装载：

> **生产指示位置可能事实包。**

2号位只做相对简单、宽范围的事实获取，例如：

- 当前PI基本信息；
- 当前物料相关StageProgress；
- PI号相关库存余额；
- 相关XC库存；
- 相关跨厂在途；
- Routing/Stage路径；
- Received事实；
- 必要的工序/仓库属性。

2号位不需要先把复杂位置逻辑判断完再传给5号位。

否则5号位职责失去意义。

## 28. 5号位PI位置计算

5号位负责复杂判断：

- PI总量闭合；
- 累计Stage差分；
- XC/在途/Stage位置互斥；
- 强事实修正；
- UNLOCATED；
- 数据质量Issue。

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

PI位置是“尚未最终完成”的位置计算。

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

---

# 第八部分  库存、采购、VMI和外部供给

## 33. 库存资格与优先级

库存先判断：

1. 是否有资格使用；
2. 仓库Priority；
3. 稳定排序。

库存资格原则由`InventoryAvailabilityRule`统一治理。

## 34. 采购/VMI与库存资格保持一致

外部供给不再建设一张“万能Pipeline规则表”。

采购/VMI的仓库使用资格原则上继承库存仓库资格。

同仓库内部按AvailableTime排序。

VMI为独立仓库，不与普通PO同仓库混为一个Supply类型竞争。

## 35. 人工ETA、ERP ETA与默认LT

采购预计可用时间优先级：

> **人工维护ETA > ERP ETA > 默认采购LT推算。**

人工ETA允许覆盖ERP早期回复日期，不设置自动失效期限；人工删除/取消后才回落ERP ETA。

人工维护粒度：

> **采购单号 + 采购单项号 + 物料 + 收货仓库。**

默认采购LT：

- 不加ProductFamily维度；
- 收货仓库是重要维度；
- 因仓库编码全公司唯一，不需要重复再加Factory维度。

默认LT起算基准：

> 采购单正式下发日期。

如果按默认LT推算的日期已经早于当前日期，则按已冻结的延迟余裕参数向后修正，例如20%延迟余裕。

到货后可用Offset主要按收货仓库配置。

## 36. AvailableTime的职责

采购/VMI的EffectiveETA和AvailableTime应尽量在ODS/防腐事实层预先算好，由5号位维护真实业务事实，2号位直接装载消费。

1号位不重新计算采购ETA。

## 37. 采购供给内部排序

基本顺序：

1. 仓库资格；
2. 仓库Priority；
3. 同仓库按AvailableTime；
4. PO发行时间；
5. PO号+项号稳定排序。

已到厂未入库是更强的当前事实，在同一采购单内部优先于尚未到厂的未结余量。

同一来源内部坚持：

> **用完一条，再下一条。**

## 38. 无正式采购承诺时的规划性采购占位供给

当采购件：

- 库存无；
- 已到厂未入库无；
- PO无；
- VMI无；

V1允许在内存形成：

> **规划性采购占位供给（Planning-only Purchase Placeholder）。**

规则：

- 数量=当前缺口；
- AvailableTime按当前时间/计划基准时间 + DefaultLT等冻结参数估算；
- 必须标记`ESTIMATED / NOT_COMMITTED`；
- 不生成采购单；
- 不生成制造Task；
- 不落成正式已承诺Supply；
- 不下发ERP。

如果CTP依赖该占位，只能给：

> **估算日期，而不是确定性承诺。**

正式PO/VMI出现后，下一次排程自动用真实供给替换估算占位。

---

# 第九部分  两类跨厂业务

## 39. 大工艺接续型（Stage Handoff）

适用于同一PI沿大工艺跨厂继续生产。

正式供给顺序：

> **目标工厂可直接使用M → 选择PI → PI内部Position → 缺口新增生产。**

例如B厂需要C500：

- B厂M 100；
- PI-C01 300；
- PI内部已到B厂100、在途120、XC80；
- 缺口100新增生产。

红线：

- 不单独把XC再算一份Supply；
- 不单独把在途再算一份Supply；
- 不搜索上游工厂普通M作为“可直接跨厂借用库存”。

## 40. 厂间出荷指示型（Inter-factory Shipping Instruction）

目标工厂Demand顺序：

> **目标工厂BS/KS普通库存 → 已有厂间出荷指示SH。**

SH内部履行状态：

> **跨厂在途 → SH对应ZP/BP Received → 未生产份额。**

未生产份额进入下一层源工厂生产需求，再按：

> PI → PI Position → 新增生产。

当前层Demand只Pegging到SH本身，不在同一层递归追踪源工厂内部怎么排。

源工厂排完后的完成时间，再通过跨厂LeadTime向上回传SH的AvailableTime。

## 41. 跨厂物流V1最终红线

**绝不建设有限物流Task体系。**

V1只有：

`上游生产完成时间 + 跨厂运输/检验/转运LeadTime = 下游AvailableTime`

不建设：

- ShippingTask有限资源；
- 车辆；
- 班次；
- 月台；
- 物流有限产能Solver。

---

# 第十部分  Demand Protection、严格绑定和执行事实

## 42. 三类锁必须区分

### 42.1 严格绑定（Strict Binding）

例如：

- 客户专属；
- 出荷指示专属Received；
- 质量/环保/资格专属。

普通Demand不可抢占。

### 42.2 需求保护锁（Demand Protection）

用于保护某些必须优先确保的Demand。

触发条件全部应由规则引擎配置，例如：

- 剩余交期短于正常LT；
- 已经延期过；
- 特定客户等级；
- 其它业务保护条件。

一旦触发，保护的是：

> **该Demand真正必要的供给数量份额。**

不是把整个PI/整个PO一刀切全部锁死。

### 42.3 执行不可逆（Execution Constraint）

真实发生的：

- 已消耗；
- 已领料；
- 最终直接履约Task已开工；
- 其它不可逆执行事实；

不能在新版本中重新解释。

## 43. Demand Protection的数据模型

V1不再为Demand Protection单建一套大平台。

建议在现有需求—供给锁实体中增加/使用`LockType`区分：

- `STRICT_BINDING`；
- `DEMAND_PROTECTION`。

ExecutionLock仍单独表达MES现实执行事实。

## 44. 下阶通用PI已经开工，不等于必须锁给原Demand

例如通用零件PI-C01已经MACH开工。

如果它只是下阶公共零件：

> 开工只证明这批零件必须继续生产，并不证明它必须属于昨天分到它的订单B。

只要没有：

- 严格绑定；
- Demand Protection；
- 已实际消耗；
- 最终直接履约不可逆；

它仍可以在下一版被高优先订单重新Pegging。

相反，如果PI本身就是订单A最终成品装配，且已齐套/已开工，则对应关系可能已不可逆，不能随意换给订单B。

---

# 第十一部分  夜间重新Pegging与跨版本原则

## 45. 普通Allocation不保留跨版本关系稳定性偏好

夜间FULL每次按最新：

- Demand；
- Supply；
- Demand排序规则；
- Lock；
- 执行事实；

重新Pegging。

除严格绑定/保护/不可逆等关系外：

> **昨天订单A分到PI-01，不代表今天A天然优先继续保留PI-01。**

新高优先需求可以重新竞争普通Supply。

## 46. 已下发/已开工的最终直接履约Task

如果已经形成最终直接履约、继续更换Demand会破坏真实生产执行，则必须保持。

锁定的是现实，不是历史Allocation关系本身。

---

# 第十二部分  2号位交给1号位的正式求解上下文

## 47. 2号位不能只传Task列表

2号位必须给1号位完整问题，包括：

1. Run/PlanVersion/Domain/Horizon；
2. 逻辑生产需求；
3. Net Required Qty；
4. Planned Process Qty；
5. Demand业务优先关系；
6. Allocation血缘；
7. 完整Routing候选图；
8. Operation Dependency；
9. Resource Eligibility；
10. Resource Calendar；
11. 已占用时间；
12. 物料Quantity + AvailableTime；
13. Firm/Frozen/Lock/执行固定事实；
14. 正倒排/瓶颈/拆合批/Setup/Stage重叠等冻结策略。

## 48. FinalTask由1号位产生

1号位决定：

- 最终Operation级Task；
- 实际设备；
- 最终批次；
- Start/End；
- 真实Task依赖；
- TaskShare。

2号位现有提前Task生成路径退出正式V1主链。

## 49. 一个Task可以承接多个Demand

允许：

- 一个Allocation拆成多个FinalTask；
- 一个FinalTask合并多个Allocation份额。

真实业务归属由`AllocationTaskShare`等数量映射表达。

因此Task不应再把单一`OrderId`视为最终唯一业务归属真相。

如果现有Task.OrderId字段难以立即删除，可保留兼容/代表性值，但不能作为唯一业务关系来源。

## 50. Task数量字段

建议：

- `Task.Quantity`继续表示净合格产出语义；
- 增加/明确`PlannedProcessQty`表示实际计划加工量。

资源负荷使用PlannedProcessQty。

Allocation闭合使用净合格Quantity。

---

# 第十三部分  1号位有限产能求解器最终业务边界

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

如果V1业务当前只有一条主Routing，不为了未来多路径提前做复杂全局路线搜索；但接口保留未来可扩展性。

## 53. 动态瓶颈

Stage不永久写死为瓶颈。

默认采用：

> **Load/Capacity动态识别 + 人工策略覆盖。**

3号位可配置：

- AUTO；
- PREFER_ANCHOR；
- FORCE_ANCHOR；
- NOT_ANCHOR。

V1只做简单可靠的瓶颈识别和锚定，不做复杂数学多瓶颈全局最优。

## 54. 正排与倒排

正排/倒排是同一个Solver内部的时间槽搜索策略，不是两套Solver。

V1支持：

- Forward；
- Backward；
- Mixed/Auto。

可按：

> ProductFamily × Factory × Stage × OrderType

配置策略，必要时Material例外覆盖。

## 55. 求解器固定外层阶段

V1采用固定外层：

1. 建立硬约束；
2. 初始有限产能构造；
3. 可行性与延期诊断；
4. 有界局部修复；
5. Gap Compaction与最终评价。

不能把整个流程做成用户可配置任意Command链。

## 56. 拆批与合批

区分：

- 强制物理拆批；
- 优化性拆批。

优化拆批只尝试少量候选，例如：

- 不拆；
- 2批；
- 3批。

不能无限组合。

允许不同Demand在有限产能和交期允许时合并为一个Task，但必须保留需求份额。

## 57. Setup/换型

Setup是资源序列关系。

Task移动时至少重新检查：

- 原前驱；
- 原后继；
- 新前驱；
- 新后继。

V1采用局部启发式减少换型，不做全局TSP式优化。

## 58. Stage重叠与转运

允许配置：

- Transfer Batch；
- Stage重叠；
- Stage间固定转运LT。

下游可在达到规定数量门槛后提前开始，不要求整批全部完成。

---

# 第十四部分  有限产能目标函数最终层级

## 59. 第一层：硬约束

绝不能违反：

- 资源冲突；
- 工艺前后依赖；
- Material AvailableTime；
- Resource Eligibility；
- Firm/Frozen；
- Lock；
- 已执行事实；
- 数量闭合。

## 60. 第二层：履约优先

Demand Protection和业务优先级属于硬层级。

高优先业务不应为了Setup、利用率等被低优先业务牺牲。

## 61. 第三层：客户承诺确保率目标

客户订单整单按期率可配置目标，例如95%或其它业务值。

如果存在满足该目标的可行方案，就不能为了次级优化选择明显更差的按期率方案。

如果物理上无法达到目标：

> Solver必须返回最佳可行计划，而不是报“计算失败”。

## 62. 第四层：次级优化

包括：

- 总延期时长；
- Lead Time；
- WIP；
- Setup；
- 利用率；
- 计划稳定性；
- 避免过早生产。

V1优先使用可解释的目标顺序，不设计大量难以解释的小数权重。

---

# 第十五部分  排程终点与交期回答

## 63. V1排程终点

客户订单V1的正式完成点：

> **最终生产/装配完成并进入目标ZP/BP成品仓。**

外部客户运输不进入V1有限产能。

未来如需固定出货LeadTime，可以在最终完成时间后追加确定性时间，但不属于1号位有限资源Task。

## 64. 客户最终交期由谁回答

1号位通过：

- 自制Task完成；
- 采购/VMI AvailableTime；
- 上游Material AvailableTime；
- 最终装配；

得到最终Task时间。

2号位通过TaskShare/Allocation把时间回溯到Order，形成：

> 客户订单最终计划完成时间。

采购件不需要制造Task，但必须真实参与最终交期判断。

---

# 第十六部分  白天插单与Candidate

## 65. Candidate必须产生新版本

白天WHATIF：

- Base ACTIVE不修改；
- 新建ScheduleRun；
- 新建Candidate PlanVersion；
- Candidate严格单Domain。

不能直接在ACTIVE上试算。

## 66. Candidate不是整个Domain从ODS开始重跑

白天插单：

- 复用Base ACTIVE；
- 复用当前Routing/BOM/主数据快照；
- 只对新增订单做必要实时BOM；
- 重新计算当前Domain内可重新竞争的剩余Pegging；
- 不重新执行整套夜间ETL。

## 67. Candidate中的Supply重新竞争

Candidate可重新竞争Supply不是：

`Base Supply - Base全部Allocation`

而是：

> 当前有效物理Supply - 已真实消耗 - 严格绑定 - Demand Protection - 不可逆份额 - 已失效份额。

上一ACTIVE中的普通未锁Allocation可以释放回竞争池。

当前Domain尚未完成、仍可移动的Demand与新订单一起重新Pegging。

## 68. 2号位给1号位变化种子

2号位比较Base与Candidate Pegging，只告诉1号位：

- 新增生产需求；
- 减少生产需求；
- 数量变化；
- 其它真正改变的逻辑生产块。

2号位不提前算整个影响范围。

---

# 第十七部分  白天局部有限产能重排

## 69. 影响范围由1号位动态判断

不按：

- 产品族；
- 固定7天/14天；
- 固定订单数；

硬截断。

主要传播关系：

1. 工艺前后依赖；
2. 物料Quantity-Time关系；
3. 共享有限资源时间轴；
4. Setup邻接关系。

## 70. 待检查队列（Dirty/Affected Queue）

基本算法：

1. 变化种子入队；
2. 尝试最小扰动重新安排；
3. 如果Resource/Start/End/Quantity未变化，不继续传播；
4. 如果变化，把真正受影响的上下游、物料消费者、资源邻居加入队列；
5. 直到队列为空。

这是1号位固定算法，不进入规则引擎。

## 71. 最小扰动优先

1号位优先尝试：

1. 保留原资源；
2. 保留附近时间；
3. 找无须移动其它Task的空档；
4. 少量优选替代设备；
5. 必要时才挤动低优先Task、拆批、换序。

## 72. 局部重排工程默认值

真实压测前先采用经验默认：

- 正常Candidate目标：60秒；
- 软时间预算：90秒；
- 局部模式硬时间预算：180秒；
- 受影响Task比例阈值：30%；
- 单Task最大局部修复：5次；
- 全局局部修复轮数：10轮；
- 单Operation替代资源初筛：5个；
- 优化性拆批：最多3种；
- 不设置固定影响天数。

这些是Solver技术参数，不是业务规则。

## 73. 单Domain完整重排兜底

当：

- 影响范围太大；
- 局部修复不稳定；
- 时间预算逼近；
- 主瓶颈大面积改变；

自动升级为：

> **当前单Domain完整有限产能重排。**

仍使用同一Solver。

仍然固定：

- 已执行；
- Firm/Frozen；
- Demand Protection；
- 不可逆关系。

---

# 第十八部分  白天共享资源与跨Domain插单

## 74. 白天单Domain Candidate与跨Domain共享设备

如果当前Candidate Domain与其它Domain共享同一设备/资源：

> **V1不允许为了本Domain插单去挤动其它Domain当前ACTIVE计划。**

其它Domain ACTIVE在共享资源上的占用，作为不可移动资源阻挡块。

这样保证：

- Candidate仍严格单Domain；
- 不引入跨Domain影响传播；
- 不需要资源配额/借用平台。

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

- 40件15号可用；
- 60件17号可用；

必须保留两个数量—时间切片，以便下游允许分批启动时使用。

跨Domain转运时间使用真实工厂/Stage LeadTime，不再硬编码统一“2天”。

---

# 第十九部分  插单产品输出

## 77. CTP不能只回答“能/不能”

一次插单Candidate至少返回：

1. 是否满足目标交期；
2. 最早可承诺完成时间；
3. 是否依赖ESTIMATED采购占位；
4. 受影响订单数量；
5. 哪些订单仍按期；
6. 哪些订单延期及最大影响；
7. 是否触碰Demand Protection；
8. 主要瓶颈；
9. 主要原因。

例如：

> 新订单U可于8月14日10:00完成，满足交期；影响7张现有订单，其中5张仍按期，1张后移1天，1张后移3天；无受保护订单被破坏；主要瓶颈为MC03。

如果依赖规划性采购占位：

> 必须明确显示“估算日期，非确定性承诺”。

---

# 第二十部分  Candidate采用与审批边界

## 78. WHATIF永远不能自动成为正式计划

CTP/Impact Candidate只读分析。

正式采用需要：

> **最小人工授权确认。**

## 79. 审批不是V1主链能力

是否存在完整审批，以2号位实际代码为准。

如果2号位没有审批逻辑：

> V1不新增完整OA审批平台。

只保留：

- 谁确认；
- 何时确认；
- 采用哪个Candidate；
- 激活操作审计。

如果已有审批体系过重且阻碍V1，可以旁路/简化，不让审批拖垮主链交付。

---

# 第二十一部分  Firm/Frozen与跨版本保持

## 80. 不建设FrozenZoneSnapshot平台

FrozenZoneSnapshot当前无外部消费者，V1停止继续建设。

但Firm/Frozen语义必须存在。

## 81. 跨版本Firm/Frozen承接

夜间新PlanVersion会生成新TaskId，因此不能依赖旧TaskId永久保持锁。

2号位从上一ACTIVE读取仍有效的Firm/Frozen执行事实/业务事实，转换成：

> **本次1号位的不可移动锚点约束。**

新版本形成新的TaskId。

MES真实执行事实通过ExecutionLock等现实约束继续承接。

---

# 第二十二部分  MES下发与执行反馈

## 82. MES下发资格

只允许：

- 正式ACTIVE版本；
- 有正式生产指示/合法执行身份；
- 满足发布窗口和业务资格；
- 非Candidate；
- 非UNLOCATED规划占位；
- 非无PI虚拟占位；

的Task进入MES下发。

## 83. MES五态

MES仍采用既有五态：

- 0 待开工；
- 1 开工中；
- 2 完工报工；
- 3 未完工报工；
- 4 未完工报工已完结（手动完工）。

V1不新增PAUSE/RESUME闭环。

设备故障只形成资源不可用事实、影响评估、Explanation与人工重排建议，不自动暂停/恢复Task。

## 84. 次日重新计算

MES执行事实在次日进入新的数据快照。

夜间重新：

- 计算PI当前位置；
- 重建普通Pegging；
- 固定真实不可逆执行；
- 形成新Task；
- 新版本重新排程。

普通Demand↔PI关系不跨版本强行保持。

---

# 第二十三部分  Explanation与异常

## 85. Explanation必须由1号位原生产出

1号位知道真实原因：

- MATERIAL_SHORTAGE；
- RESOURCE_CAPACITY_WAIT；
- PRECEDENCE_WAIT；
- FROZEN/LOCK；
- LOGISTICS_DELAY；
- RESOURCE_BREAKDOWN等。

2号位只能把Task级事实通过AllocationTaskShare聚合到Order，不重新发明原因算法。

## 86. DUE_DATE_RISK不是根因

`DUE_DATE_RISK`只能说明结果有交期风险。

真正解释必须指出：

- 哪个物料；
- 哪台资源；
- 哪个前序；
- 哪个锁；
- 哪个上游Domain；

真正决定了最终时间。

## 87. 约束事实不能机械相加成“延期原因总小时”

如果SURF等待20h、PO晚到20h、ASSY等28h，但最终只晚16h：

不能说总原因=68h。

必须沿最终关键路径识别实际Binding Constraint。

## 88. 数据异常与算法错误分开

### 可降级继续

- 某PI位置不清；
- Stage数据缺失；
- 某些MES事实矛盾；

处理：

> 保守修正/UNLOCATED + Issue + Domain继续。

### 必须使Domain失败

- Demand/Supply负数；
- 同一Supply重复消费；
- Allocation越界；
- TaskShare不闭合；
- 非法Resource；
- 事务只成功一半；
- 跨PlanVersion错误引用。

这类不能Warning后继续发布。

无法按客户交期完成不是算法失败，而是正常业务结果。

---

# 第二十四部分  数据库与表结构简化红线

## 89. 表与主外键原则

V1优先：

> **少表、少耦合、少跨表主外键，但保证业务主键和运行正确性。**

不为了理论完整性把大量辅助对象都物理化。

## 90. FrozenZoneSnapshot

- 表即使已存在也不继续建设正式生成链；
- 无外部消费者；
- V1由Firm/Frozen约束输入 + ExecutionLock替代。

## 91. VirtualInventoryBalance

- V1不持久化；
- 跨Domain只在内存传递Quantity + AvailableTime。

## 92. PeggingSupplyAllocation（PSA）

当前作为APS内部非Task供给分配结果存在，可保留极简镜像以保护现有代码。

原则：

- 不作为第二套业务真相源；
- 不建立独立生命周期；
- 不重复事务内/事务外写两次；
- 若最终全代码搜索确认无外部报表/MES消费者，可进一步与统一Allocation结果整合。

## 93. PI Position表

V1不建设Header + Slice复杂平台。

既然需要保存位置快照，采用：

> **一张最小PI位置快照表，多行表达Position。**

最小业务字段围绕：

- Plan/Schedule上下文；
- PI号；
- PositionType；
- Stage/NextStage；
- Quantity；
- AvailableTime；
- Source/Issue；
- 快照时间。

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

---

# 第二十五部分  与2号位现有代码的最终兼容方案

## 96. 必须保护的现有代码骨架

优先保留：

- `SchedulingOrchestrator`总入口；
- Hangfire调度；
- PlanVersion/ScheduleRun现有框架；
- ScheduleContext装载框架；
- `SupplyPool.RemainingQty`余额原型；
- StageProgressSnapshot装载；
- `IFiniteCapacityScheduler.SolveAsync`接口；
- DomainSolveRequest/Result方向；
- AllocationTaskShare DTO方向；
- 统一事务持久化模板。

## 97. 必须退出正式主链的旧路径

- 2号位提前生成最终Task；
- DefaultBatchSplitter作为5号位/2号位预拆批主链；
- 外层旧`FiniteCapacitySolver.Solve()`第二次求解；
- PassThrough作为正式生产Solver；
- FrozenZoneSnapshot生成链；
- VirtualInventoryBalance正式持久化；
- 事务外PSA二次写入；
- 运行期永久UPDATE `InventoryBalance.AllocatedQty`；
- Pegging失败Warning后继续正式排程。

无论某个旧类当前是“实际调用”还是“仅注入未调用”，最终V1正式链都只能保留上述新口径。

## 98. 2号位必须补齐的核心能力

### P0/P1

1. DemandBalance；
2. SupplyBalance原子执行；
3. AllocationSequence在分配成功时生成；
4. Lock进入Pegging循环；
5. Allocation/逻辑Ledger实际生成；
6. Candidate未锁Supply重新竞争；
7. 统一事务；
8. TaskShare真正一对多/多对一；
9. PI Position服务接入；
10. 单一真实1号位Solver。

## 99. 2号位不需要重写的内容

不需要：

- 推翻Orchestrator；
- 推翻版本框架；
- 推翻已有DTO；
- 为每个新业务再建一个独立服务层；
- 为PI位置在2号位重复实现一套复杂算法。

总体整改原则：

> **保留外壳，替换/补齐核心。**

---

# 第二十六部分  全流程端到端闭环

## 100. 夜间FULL端到端

```text
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

```text
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

```text
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

```text
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

---

# 第二十七部分  V1验收基线

## 104. 业务验收必须至少覆盖

### 正常订单

- ERP成品库存已处理；
- 自制+采购混合；
- PI+库存；
- 最终装配；
- ZP/BP完成时间。

### 多订单竞争

- 不同订单类型同层穿插；
- Demand Protection；
- 通用PI重新分配；
- 高优先插单影响低优先订单。

### PI位置

- Stage正常；
- XC；
- 跨厂在途；
- Received；
- 中间Stage缺失；
- UNLOCATED。

### 采购/VMI

- 人工ETA覆盖ERP ETA；
- ETA缺失走默认LT；
- 已到厂未入库；
- 无PO时规划性采购占位；
- CTP明确ESTIMATED。

### 跨厂

- 大工艺接续；
- 厂间出荷指示；
- 目标工厂库存优先；
- SH未生产进入下一层；
- 不生成ShippingTask。

### 有限产能

- 替代设备；
- 正倒排；
- 动态瓶颈；
- Setup；
- 拆合批；
- Stage重叠；
- 计划良率。

### 插单

- 局部影响传播；
- Shared Resource其它Domain ACTIVE阻挡；
- 单Domain兜底全排；
- 跨Domain串行WHATIF。

### 失败恢复

- 单Domain失败；
- 下游不发布；
- 无关Domain继续；
- 人工新Run重算依赖链。

### MES

- Candidate不下发；
- 虚拟PI Task不下发；
- UNLOCATED不下发；
- 正式ACTIVE下发；
- MES实绩进入次日重算。

## 105. 性能目标

当前工程目标：

- 约10万Task；
- 夜间90天FULL目标约15分钟收敛；
- 正常白天Candidate目标约1分钟；
- 大范围Candidate允许更长并自动升级单Domain完整重排。

真正数值以1号位压测结果最终优化，不因此提前增加第二套架构。

---

# 第二十八部分  最终冻结清单

以下作为APS V1正式业务基线冻结：

1. 客户订单进入APS时已经是ERP成品库存处理后的需生产量；APS不再扣成品库存。  
2. 90天全部订单完整BOM、完整Pegging、进入统一有限产能时间推演。  
3. 90天不预建第二套远期粗排Solver。  
4. 2号位是主流程与Pegging数量真相所有者。  
5. 1号位是有限资源和时间真相所有者，并产生最终FinalTask。  
6. 3号位负责规则参数治理与运行生命周期，不做逐笔运行时裁决服务器。  
7. 5号位负责复杂事实，尤其PI Position，不直接做最终Pegging/Task/ReasonCode。  
8. 4号位不直接改运行时数据库。  
9. Demand排序采用分层、有序规则段、第一命中、段内排序，不使用全局PriorityScore。  
10. 同一Material多个PI按冻结规则顺序选择，默认发行/创建时间优先；先选PI，再消费PI内部Position。  
11. PI RemainingQty以ERP边界为准，XC/在途/Stage只表达位置。  
12. 无PI号通用XC V1不处理。  
13. Demand Protection、Strict Binding、Execution Constraint三者分开。  
14. Lock触发尽量规则化配置；2号位结合事实真正执行。  
15. 普通Allocation不跨版本强制稳定；高优先Demand可重新竞争普通未锁Supply。  
16. 采购/VMI进入V1正式Supply；人工ETA优先ERP ETA，ETA缺失可按默认LT估算。  
17. 无正式采购承诺时允许规划性采购占位，但只能给估算、非确定承诺。  
18. 大工艺接续型跨厂：目标M → PI → PI Position → 新增生产。  
19. 厂间订单型：目标BS/KS → SH → 在途/Received/未生产 → 源厂下一层生产。  
20. V1绝不建设有限物流Task体系。  
21. 计划良率区分Net Required Qty与Planned Process Qty。  
22. 2号位不提前生成最终Operation Task。  
23. FinalTask可以合并多个需求，必须保留TaskShare。  
24. 1号位使用同一Solver内的Forward/Backward/Mixed和动态瓶颈策略。  
25. 履约优先和客户承诺确保率高于Setup/WIP/利用率等次级优化。  
26. 白天Candidate严格单Domain。  
27. Candidate释放上一ACTIVE普通未锁Allocation重新竞争，不把ACTIVE全部Allocation扣死。  
28. Candidate有限产能采用变化种子→动态传播→最小扰动→单Domain完整重排兜底。  
29. 白天共享设备上其它Domain ACTIVE占用视为不可移动资源阻挡块。  
30. V1不建设跨Domain共享资源配额、借用、影响传播平台。  
31. 跨Domain CTP通过多个单Domain WHATIF按依赖拓扑串行计算。  
32. 夜间上游Domain失败时，依赖它的下游本次不发布新ACTIVE；无关Domain继续发布。  
33. FAILED后必须支持人工重新计算，新建ScheduleRun，保留失败历史。  
34. WHATIF Candidate永不自动激活。  
35. 如果2号位代码没有审批，V1不新增完整审批平台，只保留最小人工确认。  
36. FrozenZoneSnapshot、VirtualInventoryBalance不进入V1正式建设。  
37. PSA只保留极简内部镜像，不作为第二套真相源。  
38. PI Position落一个最小快照结构，不建设Header+Slice平台。  
39. 不为了逻辑Ledger强制新增物理Ledger表，优先复用现有结果结构。  
40. MES五态保持现状，不新增PAUSE/RESUME闭环。  
41. Candidate、虚拟无PI Task、UNLOCATED规划Task均不得下发MES。  
42. Explanation由1号位原生产出，2号位聚合持久化。  
43. 无法按期是正常业务结果，不是Solver失败。  
44. 数据异常可保守降级；数量闭合/事务/非法资源等核心错误必须使Domain失败。  
45. 所有V1技术实现都遵循：**能在主流程更自然完成的，不为了分工硬拆插件；真正复杂且变化大的事实/算法才独立。**

---

# 结语

APS V1最终不是“把所有可能的APS能力都提前做出来”，而是：

> **把90天订单的供需关系算对，把真实有限资源时间算对，把跨厂和采购时间接上，把插单影响快速算清，把不能按期的原因说清，并保证MES执行与次日重算闭环。**

在这个目标下：

- 2号位继续掌握稳定主流程；
- 1号位真正完成有限产能Solver；
- 3号位把可变业务配置化；
- 5号位承担复杂事实，不越权修改运行状态；
- 4号位把结果变成PMC和销售真正能使用的决策界面；
- 0号位只对真正的业务边界做裁决，不被迫参与算法工程细节。

本文档作为后续六份权威技术文档统一修订的业务基线。任何后续字段、接口、表结构或代码修改，如果与本文发生冲突，必须先确认是否属于新的业务裁决，不能未经确认再次改变已冻结口径。
