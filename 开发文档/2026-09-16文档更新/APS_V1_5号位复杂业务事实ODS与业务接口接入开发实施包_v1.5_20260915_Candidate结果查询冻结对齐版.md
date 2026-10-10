# APS V1 5号位复杂业务事实、ODS与业务接口接入开发实施包（20260915 Candidate结果查询冻结对齐版）

**版本**：v1.5  
**日期**：2026-09-15（北京时间）  
**适用对象**：5号位及其开发AI  
**文档性质**：在《APS V1 5号位复杂业务事实与ODS开发实施包 v1.0（2026-08-14）》基础上的职责与接口增量修订版  

**当前有效上位依据（2026-09-13）**：

- 《APS V1最终全部流程与业务基线 v1.6（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS Pegging供需承接与分层计算业务说明 v1.5（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS 有限产能排产与滚动90天计划业务说明 v1.4（跨版本排程连续性 / 跨厂SH联合冻结对齐版）》；
- 《APS 集成接口设计规范 v1.32（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS 核心排产全流程走查 V3.22（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》。

> 本版完整继承v1.4及20260914 SH纠错全部BOM/ODS/PI Position/MES/采购/接口边界；本轮只增强Candidate PlanVersion结果查询和页面DTO，不解析Candidate Input，不计算Candidate。若历史章节与本版冲突，以v1.5当前口径为准。

**上位依据**：

- 当前APS V1最终业务冻结基线；
- 《APS Pegging供需承接与分层计算业务说明》当前冻结版；
- 《APS 有限产能排产与滚动90天计划业务说明》当前冻结版；
- 《APS 集成接口设计规范 v1.28（20260903职责与前端接口冻结对齐版）》；
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》；
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》；
- 《APS V1 4号位页面与业务操作开发实施包 v1.2（20260901 Domain专项冻结对齐版）》。

## 当前版本变化说明（2026-09-15）

**修改依据**：接口v1.32 + 核心走查V3.22 + 4号位实施包v1.4  
**修订原则**：5号位只提供Candidate结果查询接入/DTO/Scope过滤，不拥有Candidate输入Schema、不计算或激活Candidate。  
**影响边界**：BOM/ODS/PI Position/MES/SH/采购事实均不改变。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | 查询增强 | §16.3 | Gantt/Resource Plan支持显式Candidate PlanVersionId。 | Candidate比较 | UI可看真实Candidate甘特 | 4→5→2 Query变化 | 无 | 5号位 |
| 2 | DTO增强 | 模块G | 返回PlanVersion类型/Base/Domain/Explanation等Candidate展示字段。 | 页面需求 | 不改变运行真相 | DTO扩展 | 无 | 5号位 |
| 3 | 职责保护 | 模块G | 不解析ScopeJson/BusinessTriggerType，不计算Candidate，不做激活治理。 | Owner边界 | 防第二业务层 | 无 | 无 | 5号位 |


> **2026-09-14 P0文档纠错（业务版本保持v1.4不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

---

# 0. 本版变更控制

本版不是重新设计5号位，而是在原v1.0基础上只做已经冻结的增量修正。

原v1.0以下核心职责全部继续保留：

1. BOM入口和Workset复杂派生；
2. PI Position（生产指示位置）；
3. MES工序/大工艺进度事实标准化；
4. 跨厂模式相关复杂事实；
5. 采购、VMI、已到厂未入库、厂间在途等供应事实；
6. Received按单据号等强事实；
7. ProcessCode / ERPProperty等ODS防腐事实；
8. 数据问题/异常Issue识别与返回；
9. 5号位不做Pegging、不做Allocation、不做Solver、不做FinalTask、不做最终Explanation ReasonCode裁决。

本版新增/修正：

1. 5号位新增面向4号位的**非治理类APS业务接口接入/中转层**职责；
2. 4号位非治理业务统一通过5号位访问，不再直接调用2号位HTTP/API；
3. 5号位必须消费3号位提供的身份、功能权限和业务范围上下文；
4. 5号位后端必须校验Factory / ProductFamily / Department / Domain业务范围；
5. G4 / G7 / G8 / Schedule普通结果查询逐步由3号位迁移到5号位；
6. Manual ETA事实/API正式归5号位，4号位通过5号位维护；
7. **AvailableTime最终计算责任正式归2号位**，修正原v1.0中“5号位/ODS尽量算Effective ETA / AvailableTime”的旧表述；
8. `ProcurementManualEtaOverride`及5号位自属事实结构的DDL/Repository/Service/API归5号位Owner；
9. 旧“全部数据库DDL都由2号位执行”的泛化理解废止；
10. 5号位的“中转权”不得扩张为对2号位业务的重新实现。
11. 新增`NextOperationContext`复杂事实计算职责，用于回答PI剩余数量下一步应从哪个Stage/Operation继续；
12. 明确5号位负责“执行事实解释”，2号位负责Pegging/Allocation和LogicalProductionDemand组装，1号位负责有限产能时间资源求解；
13. 明确`NextOperationContext`不是Supply、不是Position身份、不是Allocation对象，也不形成Operation级Pegging；
14. 本版不要求为`NextOperationContext`新增独立持久化平台或DDL。
15. 新增工序级`LastReportResourceCode`标准化事实，明确禁止新增工单级同名字段；
16. 新增IN_PROGRESS既存执行上下文标准化输出，供2号位在当前Q形成后做Continuation分桶；
17. SH事实职责收敛：不再提供SH第二主档、SH权威总量、普通Demand↔SH固定关系或SourceReadyTime；
18. `ERP_InterplantInTransit_View`在SH主链中只提供SH号存在性及真实物流事实，查询成功0行属于合法成功。

**本版不得借前端接口调整重新设计Pegging、Demand Protection、CrossFactory、AvailableTime、Candidate、PlanVersion、MES下发或有限产能业务。**

---

# 一、5号位在APS V1中的最新定位

5号位仍然不是“规则插件中心”，也不是Pegging执行器。

原核心定位继续成立：

> **把ERP/MES/跨厂/采购等复杂、易变、需要业务知识解释的源事实，整理成2号位可以稳定消费的标准事实。**

2026-09-01后，5号位增加第二个技术角色：

> **作为4号位非治理类APS业务的统一接口接入/中转层。**

因此，5号位V1职责正式分成两条线。

## 1.1 复杂业务事实线

负责：

- BOM防腐与Workset；
- PI Position；
- NextOperationContext（下一执行工序上下文）；
- MES进度事实；
- 工序级LastReportResourceCode；
- IN_PROGRESS既存执行上下文标准化；
- 跨厂复杂事实；
- 采购在途/未结PO、VMI、采购已到厂未入库等正式Supply事实；
- 跨厂Transit/Received/ZP/BP等仅作为物流/位置追溯、PI Position或SH号存在性事实；
- ODS契约；
- ERPProperty；
- Manual ETA事实；
- BusinessFactIssue。

## 1.2 面向4号位业务接口接入线

负责：

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
- G4 Run列表；
- G7 Domain依赖；
- G8 Domain状态；
- Schedule普通结果查询；
- 其它非治理类APS业务查询/受控操作。

一句话：

> **5号位负责“事实算清楚 + 当前做到哪里/下一步从哪里继续 + 非治理业务接口接进来”；2号位负责“哪个Demand承接哪个PI、数量怎么闭合、如何形成LogicalProductionDemand”；1号位负责“从已确定执行起点开始，在哪台资源、什么时间排”。**

---

# 二、5号位明确不负责

即使新增前端业务接口接入职责，以下内容仍然不属于5号位：

- Demand最终排序；
- Supply最终挑选顺序；
- DemandBalance；
- SupplyBalance；
- Allocation数量扣减；
- Demand Protection实际数量创建、消费和释放逻辑；
- Strict Binding最终执行；
- CrossFactory主流程决策；
- Effective ETA最终裁决；
- AvailableTime最终计算；
- LogicalProductionDemand；
- Task生成；
- FinalTask；
- 有限产能Solver；
- ScheduleRun主流程编排；
- PlanVersion核心运行真值；
- Candidate求解；
- Candidate激活条件裁决；
- Explanation最终ReasonCode裁决；
- MES正式下发业务资格裁决；
- APS运行结果最终持久化；
- Authentication / RBAC / Business Scope规则维护；
- RuleSet / ParameterSet / StrategyProfile治理。

新增接口层后尤其禁止：

> **因为Controller在5号位，就把2号位业务复制到5号位。**

---

# 三、总体开发原则

## 3.1 复杂事实前置，主流程保持稳定

只有满足以下条件的逻辑放到5号位复杂事实层：

- 源系统字段复杂；
- 需要ERP/MES业务知识解释；
- 未来可能随源系统变化；
- 放进2号位会污染Pegging主流程。

例如：

- “先选PI再消费Position”属于2号位；
- “该PI当前剩余量分别位于XC/Stage/Transit多少”属于5号位。

---

## 3.2 事实输出与业务中转严格分层

### 事实层可以形成

- Position；
- SourceDocument；
- WarehouseRole；
- ERP ETA；
- ReleaseDate；
- Manual ETA；
- StrongEvidence；
- Issue；
- CrossFactoryMode Fact；
- Procurement Fact。

### 接入层可以做

- HTTP Controller；
- 身份/权限上下文接入；
- Scope校验；
- Query DTO；
- 页面聚合；
- 调用2号位Query Service；
- 调用2号位Application Service；
- 将结果转换为4号位稳定DTO。

### 接入层不得做

- 重算Pegging；
- 重算Allocation；
- 重算Demand Protection；
- 重算AvailableTime；
- 改Resource/Start/End；
- 重建FinalTask；
- 重做ScheduleRun状态机。

---

## 3.3 尽量批量，不逐笔远程调用

原v1.0原则继续有效。

夜间全量目标约10万Task，不允许：

```text
2号位每处理一笔Demand
→ RPC调用5号位一次
```

复杂事实优先：

- 批量SP；
- 批量视图；
- 批量事实DTO；
- 一次装载2号位内存。

前端查询也要避免：

```text
一个页面1000行Task
→ 5号位对2号位发1000次调用
```

应使用：

- 服务端分页；
- 批量Query；
- 一次聚合；
- 按页面/订单/版本/Domain范围请求。

---

# 四、5号位正式交付模块

在原六个模块基础上新增一个接口接入模块：

## 模块A：BOM防腐与Workset

## 模块B：PI Position与NextOperationContext Calculator

## 模块C：MES进度事实标准化

## 模块D：跨厂事实

## 模块E：采购 / VMI / Timed Supply事实

## 模块F：ODS契约、Manual ETA与Issue治理

## 模块G：面向4号位的非治理业务接口接入/中转层

不要求拆成七个服务进程，可按现有工程结构组织。

---

# 五、模块A：BOM防腐与Workset

原v1.0冻结内容保持。

## 5.1 正式输入

来自：

- ERP/MES BOM源；
- `MES_BOM_Edge_Active`；
- `MES_ProcessCode_View`；
- StageDict；
- RequestDetail；
- OrderType；
- MaterialCode；
- FactoryCode；
- RequestedBOMNO。

## 5.2 正式链路

```text
源BOM
→ MES_BOM_Edge_Active
→ sp_ExpandBOMBatch_vNext / sp_ExpandBOMRealtime_vNext
→ MES_APS_BOM_Workset
→ StageDetail
→ CrossFactoryEdge
→ APS RAW
```

5号位负责ODS侧：

- `sp_RefreshBOMEdgeActive`
- `sp_ExpandBOMBatch_vNext`
- `sp_ExpandBOMRealtime_vNext`
- `sp_EnrichBOMWorkset`
- `sp_EnrichBOMWorksetRealtime`
- CrossFactoryEdge相关派生
- Issues登记

2号位负责：

- 发起Request；
- 搬运/消费进入APS主流程；
- OrderBomRequestLink；
- PlanVersion运行内消费。

## 5.3 BOMNO为空

继续使用已冻结入口解析逻辑。

5号位可以使用：

- OrderType；
- MaterialCode；
- FactoryCode；
- ProcessCode；
- ERPProperty；
- BOM边；

解析入口。

只返回：

- ResolvedBOMNO；
- Workset；
- Issue。

不返回订单优先级或Pegging决策。

---

# 六、模块B：PI Position与NextOperationContext Calculator

PI Position仍是5号位V1核心能力之一；本版在同一复杂事实解释模块内补充NextOperationContext计算，不另建第二套MES解释器。

## 6.1 业务目标

给定PI的ERP RemainingQty：

> 5号位回答这批剩余量当前分别位于哪个Stage、XC、在途、等待或UNLOCATED。

必须满足：

```text
Σ PositionQty = ERP RemainingQty
```

## 6.2 最高数量红线

> **PI RemainingQty由ERP定义，是该PI尚未最终进入目标M库的全部剩余数量。**

禁止：

```text
ERP RemainingQty
+ MES WIP
+ XC
+ Transit
```

MES、XC、Transit只解释位置，不增加总量。

## 6.3 PositionType

至少支持：

- `STAGE`
- `XC`
- `INTERPLANT_IN_TRANSIT`
- `WAITING`
- `UNLOCATED`

现有DDL若有兼容枚举，可以映射，不要求重建。

## 6.4 Stage累计异常

下游完成量 > 上游完成量：

> 保守下修下游有效完成量，不制造数量。

## 6.5 中间Stage缺失

采用：

> 下游已证明的最小完成量。

## 6.6 强事实校正

XC、Transit、Received等强事实可以修正规划位置解释，但：

- 不回写MES；
- 不篡改ERP；
- 不改变PI RemainingQty。

## 6.7 Position互斥

同一物理份额不能同时出现在：

- Stage；
- XC；
- Transit。

## 6.8 UNLOCATED

无法可靠定位但总量可闭合：

> 剩余进入UNLOCATED。

后续由2号位按冻结规则保守形成计划需求。

## 6.9 严重Issue

至少：

- 总量无法闭合；
- 同一物理份额重复；
- PI/Material/Factory主键冲突；
- 强事实数量超过RemainingQty且不可解释。

是否使Domain失败由2号位决定。


## 6.10 NextOperationContext业务目标

在PI Position数量闭合之后，5号位继续基于：

- MES WorkOrder / OperationProgress / StageProgress；
- 当前PI Position；
- RoutingOperation / RoutingDependency；
- 工序与Stage映射；
- 已冻结的UNLOCATED保守规则；

回答：

> **该数量份额下一步应从哪个Stage、必要时哪个Operation继续进入有限产能排程。**

NextOperationContext只解释执行起点，不决定：

- Demand优先级；
- Supply选择；
- Allocation；
- Resource；
- StartTime / EndTime；
- 拆批/合批；
- 排程优化目标。

## 6.11 NextOperationContext最小语义

不强制新增特定类名，但输出必须能够表达：

- ProductionInstructionNo；
- MaterialId / MaterialCode；
- FactoryId / FactoryCode；
- PositionType；
- PositionQty / SliceQty；
- StartStageCode；
- StartOperationCode（需要精确到小工序时）；
- RoutingKey / RoutingVersion或等价稳定引用；
- IsUnlocated；
- DataCutoffTime；
- Issue / Evidence（存在异常时）。

其中：

- `StartStageCode`回答从哪个大工艺继续；
- `StartOperationCode`回答1号位从Routing中的哪个小工序开始展开；
- 不需要精确到Operation的场景可以只给StartStage；
- 不得为了填字段而猜测不存在的MES事实。

## 6.12 同一PI多执行起点切片

同一PI剩余量可以因真实执行进度不同形成多个执行起点切片。

例如：

```text
PI001 RemainingQty = 1000

200件：NextOperation = NC
800件：NextOperation = 挤丝
```

5号位可输出两个NextOperationContext数量切片。

必须满足：

```text
Σ NextOperationContext.SliceQty
=
需要继续生产的PI Position数量
```

同一物理份额只能属于一个执行起点切片。

这仍然是：

> **同一ProductionInstruction内部的执行上下文切片。**

不是：

- 两个Supply；
- 两条新的Operation级Allocation；
- NC Position / 挤丝 Position / 研磨 Position等新的Position体系。

## 6.13 已完成工序不得再次进入排程

若MES事实已经证明某数量份额完成NC、下一工序为挤丝：

> NextOperationContext必须指向挤丝，不能再把该份额从NC重新排一遍。

若仅能证明完成某Stage、无法可靠确定具体Operation：

- 按已冻结Stage级保守规则返回；
- 不由5号位臆测设备或具体资源；
- 不由2号位再次解析MES补猜。

## 6.14 UNLOCATED与NextOperationContext

PI Position为UNLOCATED时：

- 保留`IsUnlocated=true`；
- 按现有冻结业务允许的最早可信Stage/Operation形成保守执行起点；
- 无法确定时不得伪造精确StartOperation；
- Issue必须随结果返回。

后续MES不可下发限制继续由APS主链执行，本模块不得自行解除。

## 6.15 批量计算要求

夜间FULL必须优先按活跃PI批量计算：

```text
PI RemainingQty
+
MES执行事实
+
Routing
→
PI Position
+
NextOperationContext
```

禁止：

```text
2号位Pegging每处理一笔Demand
→
远程调用5号位重新解释一次MES
```

同一Run / DataCutoffTime应保持事实口径一致。

---

# 七、PI Position持久化边界

原“最小快照”思想保留。

不建设：

- PI Header + Slice双表生命周期平台；
- Position事件溯源平台；
- Position独立工作流。

数据库Owner原则按最新裁决执行：

- 5号位Own自身PI Position、MES解释、NextOperationContext相关事实计算实现与ODS自属结构；
- 5号位不直接写2号位PlanVersion运行结果表；
- 如`ProductionInstructionPositionSnapshot`或等价快照属于2号位ScheduleRun/PlanVersion运行事实，则由2号位负责运行时持久化；
- `NextOperationContext`本版优先作为批量DTO/运行上下文消费，不要求独立建表；
- 不再机械套用“所有DDL都归2号位”。

跨Owner修改必须评审。未经字段说明/DDL专项复核，不得仅为本次链路新增第二套Position或NextOperationContext持久化平台。

---

# 八、模块C：MES进度事实标准化

继续保留：

- `MESWorkOrderSnapshot`
- `OperationProgressSnapshot`
- `StageProgressSnapshot`

目标：

> 将MES原始/汇总事实转成PI Position、NextOperationContext、既存执行上下文及其它APS复杂事实的稳定输入。

## 8.1 工单状态与报工五态分开

工单级继续使用：

```text
WorkOrderStatus = RELEASED / IN_PROGRESS / CLOSED / DELETED
```

`DELETED`为本轮新增状态值。

工序报工五态继续既有0～4定义，不新增PAUSED/SUSPENDED/RESUME。

不得把工单状态字段改名为`ExecutionStatus`。

## 8.2 工单级字段红线

`MES_APS_WorkOrder_View / MESWorkOrderSnapshot`不新增：

```text
RemainingQty
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

## 8.3 工序级事实

`MES_APS_OperationProgress_View`继续：

```text
PlannedQty
GoodQty
LastReportTime
```

本轮新增：

```text
LastReportResourceCode
```

同步到：

```text
OperationProgressSnapshot.LastReportResourceCode
```

含义：同一MES工单的该具体Operation在DataCutoffTime前最后一次有效报工设备。

该字段只用于资源连续性事实/偏好，不自动形成Hard Lock。

工序级剩余：

```text
OperationProgressSnapshot.RemainingQty
= max(PlannedQty - GoodQty, 0)
```

## 8.4 Stage级事实

继续：

```text
GoodCompletedQty
StageProgressSnapshot.RemainingQty
= max(PlannedQty - GoodCompletedQty, 0)
```

`GoodQty`与`GoodCompletedQty`不得互换或重新命名。

## 8.5 标准化既存执行上下文

5号位结合WorkOrder、Operation、Stage、PI Position、NextOperationContext输出2号位可消费的ExistingExecutionContext。

最小语义：

```text
ProductionInstructionNo
MESWorkOrderNo
WorkOrderStatus
StartStageCode
StartOperationCode
DerivedRemainingQty
LastReportResourceCode（工序级，可空）
DataCutoffTime
Issue / Evidence
```

`DerivedRemainingQty`是运行时派生结果，不是MES工单级原生字段。

5号位不做：

- 当前Q；
- E<=Q分桶；
- ContinuationKey/TaskNo身份；
- FinalTask持久化。

## 8.6 历史Task

MES物理执行事实不依赖历史APS Task作为事实真值。

无TaskNo人工/历史MES工单只要有真实报工，仍必须进入事实解释链。

---

# 九、模块D：跨厂事实

两类跨厂业务继续存在，但职责必须分开。

## 9.1 大工艺接续型

PI口径保持不变：

- PI始终是一份ProductionInstruction Supply；
- 跨厂通过PI Position表达；
- 不新增Transit Supply。

5号位继续提供Stage跨厂边、SourceFactory、TargetFactory、PI Transit、ERPProperty、Warehouse/Process位置事实及既有Factory/Stage LT事实。

## 9.2 厂间出荷指示（SH）

SH业务主档来自2号位Order，不在5号位另建：

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

5号位只负责：

- `ERP_InterplantInTransit_View`跨厂物流事实；
- SH号在视图中的存在性；
- ERP真实发运/到达事实（有则提供）；
- SourceFactory/TargetFactory等视图真实字段（有则提供）；
- 真实Strict Binding Evidence（存在时）；
- 数据质量Issue。

5号位不再负责：

- SH权威总量/剩余；
- 普通Demand↔SH固定Document关系；
- SH SourceReadyTime；
- Transit/Received/ZP/BP多Supply；
- SH生产AvailableTime估算；
- 70/30物流数量切片。

## 9.3 在途视图语义

对OPEN SH：

```text
视图存在SH号 → 已进入跨厂履行
视图不存在SH号 → 尚未进入跨厂履行
```

查询成功0行属于合法成功。

在途Quantity不得解释成“70在途+30未生产”，也不得计算`Order.Quantity-TransitQty`。

## 9.4 SH标准LT

SH标准运输/检验/转运LT由3号位ParameterSet治理。

5号位只提供ERP真实物流事实；真实事实优先于默认LT。

本轮不得借SH修订改变PI大工艺接续型既有LT事实归属。

## 9.5 最终运行决策Owner

2号位负责：

- SH第一层Supply；
- Demand↔SH Allocation；
- 第二层源厂Demand；
- `RequiredQty = Order.Quantity`；
- SourceFactoryId重新归Domain；
- SH整单优先级运行执行；
- 跨Domain传播。

1号位负责真实SourceReadyTime。

5号位只提供事实。

---

# 十、模块E：采购 / VMI / Timed Supply事实

V1必须真实提供：

- 已到厂未入库；
- 采购在途；
- 正式PO剩余；
- VMI；
- 其它冻结的供应事实。

不能固定返回空集合。

## 10.1 标准事实字段

至少能够稳定提供：

- SupplyType
- PhysicalSourceKey
- MaterialId
- FactoryId
- WarehouseCode / StorageCode映射
- RemainingQty
- ERP ETA
- ReleaseDate
- SourceDocumentNo
- SourceDocumentLineNo（适用时）
- SourceUpdatedAt
- Commitment相关原始事实
- 来源系统标识

## 10.2 ReleaseDate

ReleaseDate为ERP原始发行/创建日期：

- ODS必须透传；
- 5号位不得自行猜测；
- 2号位用于ETA缺失时计算。

## 10.3 WarehouseCode

如果源字段是StorageCode：

> 按已冻结映射透出WarehouseCode语义。

不得重新创造新的仓库判定体系。

---

# 十一、Manual ETA与AvailableTime正式边界

这是对原v1.0最重要的后续冻结修正之一。

## 11.1 ERP ETA

ERP ETA：

> 原始供应事实。

5号位/ODS负责保持原值，允许为空。

不得被APS人工修改覆盖源值。

## 11.2 Manual ETA

Manual ETA：

> APS人工维护的采购ETA覆盖事实。

由5号位负责：

- `ProcurementManualEtaOverride`结构；
- Repository；
- Service；
- API；
- 查询；
- 新增；
- 修改；
- 取消/失效；
- 审计所需事实。

4号位通过5号位API维护。

## 11.3 有效ETA优先级

冻结顺序：

```text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

## 11.4 最终责任

**5号位不负责最终Effective ETA和AvailableTime计算。**

正式链路：

```text
ERP/ODS采购/VMI类Supply事实
        ↓
5号位Manual ETA事实
        ↓
2号位生成/装载SupplyFact_Pipeline
（禁止装载INTERPLANT_IN_TRANSIT）
        ↓
2号位计算Effective ETA
        ↓
2号位计算AvailableTime
        ↓
Pegging
```

因此原v1.0中类似：

> “5号位/ODS事实层尽量把Effective ETA算好”

的表述正式失效。

## 11.5 AvailableTime

AvailableTime属于APS最终可用时间事实，由2号位计算。

5号位可以返回：

- ERP ETA；
- ReleaseDate；
- Manual ETA；
- 计算所需源事实。

不能把自己推导的AvailableTime作为2号位必须接受的最终真值。

---

# 十二、Planning-only Purchase Placeholder

继续严格保持：

当真实采购/VMI/库存等无承诺供给时：

> 2号位可以在运行内存形成Planning-only Purchase Placeholder。

5号位不得：

- 生成Placeholder正式表；
- 把Placeholder落成正式Procurement SupplyFact；
- 创建PO；
- 创建采购Task；
- 向ERP写采购承诺。

Placeholder必须由2号位标记：

- ESTIMATED；
- NOT_COMMITTED。

---

# 十三、模块F：ODS契约与Issue治理

5号位继续Own复杂事实相关ODS契约。

包括但不限于：

- BOM相关；
- ProcessCode / ERPProperty；
- Stage；
- MES进度；
- Procurement；
- VMI；
- Received；
- Transit；
- PI Position源事实；
- Manual ETA自属APS事实。

## 13.1 ERPProperty

`ERPProperty`必须来自ERP真实业务属性，由5号位同步/透出。

禁止：

- 根据ProcessName猜；
- 根据WarehouseRole猜；
- 用临时硬编码替代真实ERP属性。

## 13.2 StageDict / StageCode

StageDict属于MES_Integration/ODS事实体系。

5号位应：

- 保持StageDict、MES_ProcessCode_View.StageCode等源事实契约稳定；
- 向消费方提供可解释的Stage事实；
- 不承担2号位`MaterialStageDeptContext`最终主流程Owner职责。

## 13.3 Issue

Issue至少有：

- ERROR
- WARN
- INFO

Issue不等于整个ScheduleRun失败。

5号位只报告事实问题和严重程度，2号位根据业务闭环判断是否Fail Domain。

---

# 十四、模块G：面向4号位的业务接口接入/中转层

这是v1.1新增的正式技术职责。

## 14.1 总原则

```text
4号位
   ↓
5号位HTTP/API
   ↓
┌───────────────┬────────────────┐
│ 5号位自属事实 │ 2号位内部服务   │
└───────────────┴────────────────┘
```

5号位对4号位提供稳定业务API，但：

> **接口Owner不等于底层业务Owner。**

Candidate查询同样遵守该原则：

> 5号位可以把Candidate PlanVersion结果包装给4号位，但Candidate运行输入与生命周期仍属于3号位治理、2号位运行结果Owner。

---

# 十五、5号位直接Own并可对4号位提供的接口

以下是5号位自属事实：

## 15.1 PI Position

```text
4 → 5
```

5号位直接查询自己的PI Position事实/服务。

## 15.2 Manual ETA

```text
4 → 5
```

5号位负责查询、维护、取消。

## 15.3 ODS/复杂事实Issue

```text
4 → 5
```

用于Explanation辅助、事实异常展示。

## 15.4 Procurement / VMI / Received / Transit原始追溯

若页面需要查看源事实：

```text
4 → 5
```

5号位可以直接提供。

---

# 十六、5号位中转2号位业务的接口

以下页面虽由5号位提供HTTP/API，但业务真值属于2号位：

## 16.1 Overview

```text
4
→ 5 Overview Controller
→ 2号位 Query Service
```

5号位可做页面DTO聚合，但不重新计算：

- Delayed；
- Unscheduled；
- Active Plan；
- Domain运行结果；
- Bottleneck业务真值。

## 16.2 Order / Demand Plan

```text
4
→ 5
→ 2号位 Order/Plan Query Service
```

5号位不重算Pegging。

## 16.3 Gantt / Resource Plan

```text
4
→ 5
→ 2号位 Plan Query Service
```

查询请求必须允许显式指定：

```text
PlanVersionId
DomainKey
```

因此既可以查询当前ACTIVE，也可以查询某个Candidate版本。

只展示该PlanVersion已持久化FinalTask。

Candidate DTO至少可返回：

- PlanVersionId；
- PlanVersion状态；
- DomainKey；
- BasePlanVersionId或对应ScheduleRun Base信息；
- FinalTask Resource/Start/End；
- ScheduleExplanationFacts；
- Candidate与Base比较所需的稳定Task键。

5号位不得：

- 修改Resource/Start/End；
- 解析ScopeJson业务场景；
- 重新运行Solver；
- 把Candidate结果合并到ACTIVE；
- 执行Candidate确认/激活。

## 16.4 Explanation

```text
4
→ 5
→ 2号位 Explanation Query
```

5号位可以补充自己的BusinessFactIssue，但不得覆盖2号位/1号位已形成的正式ReasonCode。

## 16.5 Supply/Pegging Trace

```text
4
→ 5
→ 2号位 Query Service
```

5号位可补充源事实详情，但：

- Allocation；
- Demand-Supply关系；
- 锁定结果；

以2号位结果为准。

---

# 十七、Demand Protection接口边界

## 17.1 查看

```text
4 → 5 → 2号位Query Service
```

5号位只负责查询中转和DTO。

## 17.2 释放

正式链路：

```text
4号位
→ 5号位API
→ 5号位权限/Scope校验
→ 2号位Demand Protection Application Service
→ 2号位执行实际释放
```

5号位不得：

- 自己算释放数量；
- 自己改Lock；
- 自己改Allocation；
- 直接UPDATE Demand Protection核心表；
- 绕过2号位业务条件。

---

# 十八、MES资格与受控操作接口边界

## 18.1 查询

```text
4 → 5 → 2号位Query Service
```

可展示：

- EligibleForMES；
- 不可下发原因；
- MES当前状态。

## 18.2 受控操作

```text
4
→ 5
→ 权限/Scope校验
→ 2号位MES Application Service
```

5号位不自行裁决MES业务资格。

即使用户拥有权限，如果Task业务条件不合法，2号位仍必须拒绝。

---

# 十九、G4 / G7 / G8 / Schedule普通查询迁移

此前3号位已有部分只读封装。

正式迁移至5号位：

- G4：Run列表；
- G7：Domain依赖；
- G8：Domain状态；
- Schedule普通计划结果查询。

## 19.1 迁移原则

- 优先保持现有URL兼容；
- 优先保持DTO兼容；
- 3号位停止新增同类查询；
- 5号位读取2号位事实或调用2号位Query Service；
- 不在5号位新建运行结果镜像表；
- 不允许3和5长期维持两套不同业务语义。

## 19.2 Run治理与普通查询分开

普通查询：

```text
4 → 5
```

治理动作：

```text
4 → 3
```

例如：

- Run列表 → 5；
- FAILED恢复 → 3；
- Candidate确认/激活 → 3。

---

# 二十、认证与权限执行边界

5号位不是Authentication/RBAC Owner。

3号位负责：

- Login；
- Refresh；
- Logout；
- Current User；
- User；
- Role；
- Permission；
- Business Scope；
- Auth Audit。

5号位负责：

> **消费3号位签发/提供的身份与权限上下文，并在自己的业务API执行后端授权。**

---

# 二十一、5号位后端必须实施的权限校验

至少校验两层：

## 21.1 功能权限

例如：

- `aps.plan.view`
- `aps.manual_eta.view`
- `aps.manual_eta.edit`
- `aps.manual_eta.cancel`
- `aps.demand_protection.view`
- `aps.demand_protection.release`
- `aps.mes.view`
- `aps.mes.dispatch`

具体Permission Code以APS_Auth当前冻结定义为准。

## 21.2 业务范围权限

至少：

- Factory
- ProductFamily
- Department / ProductionDepartment
- Domain

## 21.3 组合规则

同一维度允许并集。

不同维度共同限制：

```text
Factory in {BJ,TJ}
AND Department = MC
```

## 21.4 安全红线

禁止：

```text
数据库查询全量
→ 返回浏览器
→ 4号位前端自己过滤
```

必须在5号位或下游Query Service层将Scope带入查询。

---

# 二十二、5号位与2号位内部服务边界

2号位退出4号位HTTP层后，应向5号位提供稳定的：

- Query Service；
- Application Service；
- Domain Service（必要时）；
- 内部Contract/DTO。

5号位不得：

- 直接依赖2号位数据库表结构作为唯一接口；
- 通过跨库UPDATE代替Application Service；
- 复制2号位Domain逻辑；
- 为图方便绕过2号位校验。

允许：

> 对成熟稳定的只读事实，在接口冻结明确允许时采用受控查询方式。

但优先采用正式Query Service。

---

# 二十三、数据库Owner

旧的：

> “所有APS DDL都统一由2号位执行”

不再成立。

## 23.1 5号位Own

- ODS复杂事实结构；
- 5号位自属View/SP；
- PI Position / NextOperationContext计算所需的5号位自属事实结构；
- `ProcurementManualEtaOverride`；
- 5号位自属Issue事实结构。

## 23.2 5号位不Own

- DemandBalance；
- SupplyBalance；
- PeggingSupplyAllocation；
- PlanVersion；
- ScheduleRun运行结果；
- FinalTask；
- Demand Protection核心运行结构；
- APS_Auth。

## 23.3 中转接口不新建第二套运行表

尤其禁止为了前端：

- `UiPlanTaskCopy`
- `FrontendScheduleRun`
- `GatewayAllocation`
- `CachedDemandProtectionTruth`

之类第二真相表。

缓存如因性能确有需要，也不能成为业务真值，且本版不要求额外建设缓存平台。

---

# 二十四、错误处理

5号位需区分：

## 24.1 复杂事实Issue

- ODS缺字段；
- 源事实异常；
- PI Position不闭合；
- 单据关系异常；
- ERPProperty缺失。

## 24.2 权限错误

- 401：未认证；
- 403：无功能权限或越出业务Scope。

## 24.3 下游业务拒绝

例如：

- Demand Protection不可释放；
- MES Task不可下发；
- PlanVersion状态不允许操作。

此类业务拒绝应原样转译为稳定业务错误，不要变成“5号位内部异常”。

---

# 二十五、接口DTO原则

面向4号位应提供业务DTO，而不是数据库表裸字段。

建议包括：

- PlanOverviewDto
- OrderScheduleDetailDto
- GanttTaskDto
- ExplanationViewDto
- PiPositionViewDto
- NextOperationContextDto（内部批量事实DTO，具体命名可沿用现有代码）
- SupplyTraceDto
- ManualEtaDto
- DemandProtectionViewDto
- MesEligibilityDto
- RunStatusDto
- DomainDependencyDto
- DomainStatusDto

具体命名可以沿用现有代码，不要求为了文档改名。

原则：

> **语义稳定优先于命名统一。**

---

# 二十六、原F01～F22事实验收继续有效

原v1.0测试场景继续保留：

| 编号 | 场景 | 必须结果 |
|---|---|---|
| F01 | PI RemainingQty与MES/XC/Transit同时存在 | 不重复加总 |
| F02 | Stage下游完成量大于上游 | 保守下修 |
| F03 | 中间Stage缺失 | 使用下游已证明最小完成量 |
| F04 | 强事实证明后续位置 | 可修正规划位置，不改源事实 |
| F05 | Position无法完全定位 | UNLOCATED补齐 |
| F06 | Position重复 | 不允许重复消费 |
| F07 | 大工艺接续跨厂 | 返回正确CrossFactory事实 |
| F08 | 厂间SH | 只输出跨厂物流事实与SH号存在性，不建设SH第二主档/多Supply |
| F09 | OPEN SH视图有SH号 | 正确返回“已进入跨厂履行”事实 |
| F10 | OPEN SH视图无SH号 | 正确返回“未进入跨厂履行”事实；0行合法 |
| F11 | 在途Qty=70 / OrderQty=100 | 不派生30未生产，不形成70/30切片 |
| F12 | SH标准跨厂LT | 不由5号位维护默认参数；真实ERP事实优先 |
| F13 | PO有Manual ETA | Manual ETA事实可正确提供 |
| F14 | Manual ETA取消 | 事实层恢复无人工覆盖状态 |
| F15 | ERP ETA为空 | 提供ReleaseDate等计算基础，不由5号位最终算AvailableTime |
| F16 | ETA相关默认参数 | 参数由3号位治理，5号位不自行定义 |
| F17 | 已到厂未入库 | 正确返回来源事实/仓库事实 |
| F18 | VMI | 独立SupplyType事实 |
| F19 | ProcessCode ERPProperty | 来自ERP真实属性，不猜 |
| F20 | 真实采购Supply全空 | 返回真实空，不生成Placeholder |
| F21 | ODS单条坏数据 | Issue登记，不污染其它事实 |
| F22 | PI总量无法闭合 | 严重Issue，2号位可据此Fail Domain |

说明：

原v1.0中F15/F16/F17如把最终AvailableTime计算放到5号位，应按当前业务冻结改为：

> 5号位提供事实，最终AvailableTime由2号位计算。

---

# 二十七、新增前端接入与权限验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| F23 | 4号位Overview | 4→5，5调用2号位Query |
| F24 | Order查询 | 5不重算Pegging |
| F25 | Gantt查询 | 只返回正式FinalTask结果 |
| F26 | Explanation | 5可补事实Issue，但不覆盖正式ReasonCode |
| F27 | Supply Trace | Allocation以2号位结果为准 |
| F28 | PI Position页面 | 4→5直接读取5号位事实 |
| F29 | Manual ETA查看 | 4→5正常 |
| F30 | Manual ETA编辑 | 5号位事实更新，不改ERP ETA |
| F31 | Manual ETA取消 | Override失效，原ERP事实不变 |
| F32 | Effective ETA | 不在5号位作为最终真值计算 |
| F33 | AvailableTime | 不在5号位重新计算最终值 |
| F34 | Demand Protection查看 | 4→5→2 |
| F35 | Demand Protection释放 | 4→5→2，实际逻辑仍2号位 |
| F36 | 5号位直接UPDATE Protection | 禁止 |
| F37 | MES资格查看 | 4→5→2 |
| F38 | MES受控操作 | 权限通过后仍需2号位业务校验 |
| F39 | G4迁移 | Run普通查询由5号位提供 |
| F40 | G7迁移 | Domain依赖查询由5号位提供 |
| F41 | G8迁移 | Domain状态查询由5号位提供 |
| F42 | Schedule查询迁移 | 不产生第二套计划真相 |
| F43 | FAILED恢复 | 不由5号位治理，走3号位 |
| F44 | Candidate确认 | 不由5号位治理，走3号位 |
| F45 | 无功能权限访问Manual ETA | 403 |
| F46 | Factory=TJ用户请求BJ | 后端拒绝/过滤 |
| F47 | ProductFamily=X用户请求Y | 后端拒绝/过滤 |
| F48 | Department=MC用户请求ASSY | 后端拒绝/过滤 |
| F49 | Domain=D01请求D02数据 | 后端拒绝/过滤 |
| F50 | 浏览器篡改Scope | 5号位后端不得扩大权限 |
| F51 | 4号位直接调用2号位HTTP | 不作为正式集成路径 |
| F52 | 5号位重复实现Allocation | 验收失败 |
| F53 | 5号位创建第二套Plan结果表 | 验收失败 |
| F54 | Planning Placeholder | 5号位不落正式SupplyFact |
| F55 | Current User上下文 | 正确消费3号位身份/权限上下文 |
| F56 | 权限通过但MES业务资格失败 | 返回业务拒绝，不越权执行 |
| F57 | 权限通过但Protection不可释放 | 2号位拒绝，5号位正确转译 |
| F58 | G4/G7/G8迁移兼容 | URL/DTO优先保持兼容 |
| F59 | 前端1000 Task列表 | 不逐Task调用2号位 |
| F60 | Auth规则变更 | 5号位不维护第二套Permission规则 |

---

# 二十八、性能要求

原v1.0性能要求继续有效：

## 28.1 PI Position

夜间批量计算所有活跃PI。

避免：

- 每PI多次DB往返；
- 每Position单独远程调用。

## 28.2 Procurement / Timed Supply

批量读取：

- PO；
- VMI；
- Received；
- Transit；
- Arrival。

## 28.3 前端查询

新增要求：

- 服务端分页；
- Scope尽早下推；
- Overview使用聚合Query；
- Gantt按时间窗/资源范围加载；
- Explanation按需；
- Supply Trace按订单/需求查询；
- 不做逐Task N+1；
- 不把90天全部Task一次送前端。

---

# 二十九、5号位最终交付物（v1.1）

原交付物继续保留并扩展：

1. PI Position + NextOperationContext Calculator；
2. PI Position输入/输出DTO；
3. NextOperationContext批量输入/输出DTO；
4. Procurement / VMI / Received / Transit标准事实DTO；
5. 两类跨厂事实实现；
6. BOM/Workset必要SP与View；
7. ERPProperty真实同步链；
8. ODS契约；
9. Issue结构与日志；
10. `ProcurementManualEtaOverride`结构；
11. Manual ETA Repository / Service / API；
12. 面向4号位的非治理业务HTTP/API；
13. Overview接口；
14. Order/需求计划接口；
15. Gantt接口；
16. Explanation接口；
17. Supply/Pegging Trace接口；
18. PI Position接口；
19. Demand Protection查看/释放中转；
20. MES资格/受控操作中转；
21. G4 / G7 / G8 / Schedule普通查询迁移；
22. 3号位身份/权限上下文接入；
23. Factory/ProductFamily/Department/Domain后端Scope校验；
24. F01～F60测试结果；
25. 批量性能测试报告；
26. 与2号位Query/Application Service联调记录；
27. 与3号位认证/权限上下文联调记录；
28. 与4号位页面API联调记录；
29. 真实源数据尚未接通清单；
30. 旧3号位普通结果查询迁移兼容记录。
---

# 三十、严禁新增的过度设计

原v1.0禁止项继续有效：

- 通用规则Plugin平台；
- Voucher平台；
- PI Header+Slice双表生命周期平台；
- CrossFactory通用状态机；
- ShippingTask；
- Logistics Solver；
- Procurement Task；
- Planning-only Placeholder表；
- SupplyAvailabilityRule DSL；
- 通用事件溯源；
- 通用MDM；
- MultiDomain编排；
- Explanation原因推理平台。

本版新增禁止：

- 通用API Gateway平台；
- 第二套RBAC；
- 第二套Business Scope；
- 第二套Pegging服务；
- 第二套Allocation服务；
- 第二套Demand Protection服务；
- 第二套ScheduleRun/PlanVersion结果库；
- 前端查询镜像运行库；
- 任意Row-Level ACL平台；
- 任意Field-Level ACL平台；
- ABAC/DSL权限引擎；
- 为中转而复制2号位核心代码。

---

# 三十一、5号位与4号位联调红线

4号位可通过5号位访问：

- Overview；
- Order；
- Gantt；
- Explanation；
- Supply Trace；
- PI Position；
- Manual ETA；
- Demand Protection；
- MES；
- G4/G7/G8/Schedule普通查询。

5号位必须保证：

- 权限上下文有效；
- Scope有效；
- DTO稳定；
- 业务Owner不漂移。

不得要求4号位：

- 直接连2号位；
- 自己做Scope过滤；
- 自己判断AvailableTime；
- 自己重算Pegging。

---

# 三十二、5号位与3号位联调红线

5号位从3号位消费：

- Identity；
- Permission；
- Factory Scope；
- ProductFamily Scope；
- Department Scope；
- Domain Scope。

5号位不得：

- 创建另一套User表；
- 创建另一套Role表；
- 创建另一套Permission表；
- 自己解释Role名称得到权限；
- 自己扩大Scope。

---

# 三十三、5号位与2号位联调红线

5号位继续向2号位提供复杂事实。

同时作为4号位中转层调用2号位内部服务。

必须保持：

> **两种关系不要混成“5号位接管2号位业务”。**

2号位仍Own：

- Pegging；
- Allocation；
- Demand/Supply Balance；
- Lock；
- Demand Protection；
- CrossFactory主流程；
- SupplyFact_Pipeline最终装载；
- Effective ETA；
- AvailableTime；
- ScheduleRun运行主流程；
- PlanVersion运行真值；
- FinalTask持久化。

---

# 三十四、5号位与1号位关系

5号位不直接调用1号位Solver。

任何页面需要有限产能结果：

```text
1号位求解
→ 2号位持久化
→ 5号位Query/API
→ 4号位展示
```

---

# 三十五、完成定义（Definition of Done）

5号位完成必须同时满足：

## 35.1 原复杂事实闭环

- BOM/Workset链稳定；
- PI Position总量闭合；
- Position互斥；
- UNLOCATED可追溯；
- MES五态不扩张；
- SH跨厂在途只输出真实物流事实/SH号存在性，不进入SupplyFact_Pipeline；
- Received/ZP/BP等如源系统存在，仅作追溯/数据质量，不形成SH Supply或数量切片；
- ERPProperty来自真实ERP属性；
- 采购在途/未结PO、VMI、采购已到厂未入库等正式Supply事实不再固定空；
- Planning Placeholder不由5号位落表；
- Issue可追溯；
- 批量性能满足夜间运行方式。

## 35.2 Manual ETA / SupplyFact闭环

- ERP ETA保持源事实；
- ReleaseDate正确透传；
- Manual ETA由5号位维护；
- Manual ETA不修改ERP；
- Effective ETA最终计算不在5号位；
- AvailableTime最终计算不在5号位；
- SupplyFact_Pipeline最终业务Owner仍为2号位，但其V1正式内容仅限采购/VMI类Timed Supply，禁止`INTERPLANT_IN_TRANSIT`。

## 35.3 前端业务接口闭环

- 4号位非治理业务统一可经5号位访问；
- Overview/Order/Gantt/Explanation/Supply Trace可用；
- PI Position/Manual ETA可用；
- Demand Protection中转正确；
- MES中转正确；
- G4/G7/G8/Schedule迁移正确；
- 不新增5号位运行结果第二真相。

## 35.4 权限闭环

- 正确消费3号位认证上下文；
- 正确校验功能权限；
- 正确校验Factory；
- 正确校验ProductFamily；
- 正确校验Department；
- 正确校验Domain；
- 浏览器篡改参数不能越权；
- 不建立第二套权限体系。

## 35.5 职责红线

- 不重做Pegging；
- 不重做Allocation；
- 不重做Demand Protection；
- 不重做CrossFactory主流程；
- 不重算最终AvailableTime；
- 不生成FinalTask；
- 不调用Solver；
- 不接管Run/Candidate治理；
- 不直接修改2号位核心运行结果；
- 不因前端中转新增过度设计。

---

# 三十六、一句话交付要求

> **5号位v1.4继续作为复杂事实Owner：MES侧把工单/工序/Stage事实、工序级LastReportResourceCode、PI Position、NextOperationContext和ExistingExecutionContext解释清楚；SH侧只提供在途/真实物流事实，不建设SH第二主档、不提供普通Demand↔SH固定关系、不计算SourceReadyTime。事实解释权和接口中转权都不能扩张成Pegging、Q/E分桶、TaskNo治理或Solver职责。**


---

---

# v1.3 PI Position执行起点上下文专项冻结对齐增量

## 一、本版专项冻结结论

本版将5号位在执行起点链路中的职责冻结为：

```text
ERP PI RemainingQty
+
MES执行事实
+
Routing
+
XC / Transit等强位置事实
        ↓
5号位
PI Position
+
NextOperationContext
        ↓
2号位
Pegging / Allocation
+
LogicalProductionDemand
        ↓
1号位
90天有限产能Solver
```

其中：

- 5号位负责事实标准化和业务事实解释；
- 2号位负责Demand↔ProductionInstruction Supply数量承接与运行上下文组装；
- 1号位负责资源与时间真相。

---

## 二、ProductionInstruction Position职责最终口径

本版明确覆盖v1.2末尾“5号位只给原始事实、2号位自行形成Position解释”的临时表述。

最终口径：

> **5号位负责PI Position业务计算和解释结果；2号位负责在ScheduleRun/PlanVersion运行链中消费、必要时持久化快照，并用于Pegging后的逻辑生产需求形成。**

数据库边界仍保持：

- 5号位不直接写2号位运行结果表；
- 2号位可以Own `ProductionInstructionPositionSnapshot`等运行快照；
- “谁计算业务事实”与“谁持久化运行快照”不是同一个职责问题。

---

## 三、NextOperationContext最终定义

NextOperationContext回答：

> **某个PI剩余数量份额已经完成到哪里，下一步应从哪个Stage/Operation继续进入排程。**

它不是：

- Supply；
- 新Position身份；
- Demand；
- Allocation；
- FinalTask；
- Resource选择结果。

不得把正式Pegging从：

```text
Demand ↔ ProductionInstruction Supply
```

扩展为：

```text
Demand ↔ PI ↔ Operation
```

---

## 四、计算输入与输出

### 4.1 输入

至少包括可获得的：

- ProductionInstruction；
- ERP RemainingQty；
- MES WorkOrder；
- MES OperationProgress；
- MES StageProgress；
- RoutingOperation；
- RoutingDependency；
- Process/Stage映射；
- XC / Interplant Transit等强位置事实；
- DataCutoffTime。

### 4.2 输出

批量输出：

- PI Position；
- NextOperationContext；
- Issue / Evidence。

不要求为了本版重新建设新的数据库实体。

---

## 五、数量闭合红线

必须同时满足：

```text
Σ PI PositionQty = ERP RemainingQty
```

以及对需要继续生产的数量：

```text
Σ NextOperationContext.SliceQty
=
需要继续排程的PI数量
```

禁止：

```text
ERP RemainingQty
+ MES WIP
+ XC
+ Transit
```

也禁止同一物理份额同时落入两个NextOperationContext。

---

## 六、示例：NC完成后从挤丝继续

Routing：

```text
NC → 挤丝 → 研磨
```

PI RemainingQty = 1000。

MES证明：

- 800件NC已完成、挤丝未完成；
- 200件NC尚未完成。

5号位输出：

```text
Slice A:
Qty = 800
StartOperation = 挤丝

Slice B:
Qty = 200
StartOperation = NC
```

2号位在完成Demand↔PI数量承接后，把对应数量切片组装成LogicalProductionDemand。

1号位：

- 800件从挤丝开始排；
- 200件从NC开始排；
- 不重复给800件安排NC。

---

## 七、与PI Position的边界

PI Position继续回答：

> **数量现在在哪里。**

NextOperationContext回答：

> **这个数量下一步从哪里继续。**

二者可以由同一计算模块一次完成，但不能混成一个新的Operation Position平台。

例如：

- XC；
- INTERPLANT_IN_TRANSIT；
- WAITING；
- STAGE；
- UNLOCATED；

仍然是Position语义。

`NC`、`挤丝`、`研磨`作为StartOperation时，是执行起点语义，不因此新增`NC_POSITION`、`EXTRUSION_POSITION`等业务类型。

---

## 八、与2号位接口冻结

5号位应提供适合批量消费的稳定结果。

2号位不得：

- 逐Demand回调5号位；
- 读取MES原始表重新推导StartOperation；
- 修改5号位已经解释出的执行起点；
- 将NextOperationContext重新注册为Supply。

5号位也不得：

- 根据订单优先级调整NextOperation；
- 根据设备负荷调整NextOperation；
- 根据交期跳过合法Routing；
- 提前选择Resource。

---

## 九、与1号位边界

5号位不直接把结果送入Solver并控制排程。

正式链路仍然经过2号位：

```text
5号位事实解释
↓
2号位LogicalProductionDemand / DomainSolveRequest
↓
1号位Solver
```

1号位不应再次读取MES或调用5号位判断工序完成状态。

---

## 十、FULL与白天边界

### 夜间FULL

- 按DataCutoffTime批量形成全部活跃PI的PI Position与NextOperationContext；
- 2号位批量装载；
- 同一Run中保持一致。

### 白天Candidate / CTP

本版不新增实时MES解释平台。

继续遵循上位冻结的数据刷新边界和新增PI保守规则，不因本专项扩张白天数据刷新范围。

---

## 十一、Issue与降级

以下至少需要Issue：

- PI数量无法闭合；
- MES上下游累计矛盾且无法保守修正；
- Routing无法定位下一Operation；
- 强事实与MES事实冲突；
- 同一份额出现多个下一工序；
- StartOperation不属于合法Routing。

对于可保守处理的：

- 返回UNLOCATED或最早可信起点；
- 保留Issue。

对于无法保证数量/工艺合法性的严重错误：

- 返回严重Issue；
- 是否使Domain失败仍由2号位按冻结主流程裁决。

---

## 十二、新增专项验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| F61 | PI全部未开工 | NextOperation指向Routing首个合法工序 |
| F62 | PI前序工序已完成 | NextOperation指向下一未完成工序 |
| F63 | 同PI两种执行进度 | 返回互斥数量切片，合计闭合 |
| F64 | 下游累计大于上游 | 按既有保守规则修正，不制造数量 |
| F65 | Routing与MES工序无法映射 | 返回Issue，不臆造StartOperation |
| F66 | 同一份额同时命中两个下一工序 | 严重Issue |
| F67 | 2号位传入Demand优先级 | 不影响5号位NextOperation计算 |
| F68 | 设备满载 | 5号位不得改NextOperation规避设备负荷 |
| F69 | PI Position=UNLOCATED | 按冻结保守规则返回并保持Issue |
| F70 | NextOperationContext被当作Supply | 联调验收失败 |
| F71 | MES工单级新增RemainingNetQty/RemainingQty | 验收失败 |
| F72 | MES工单级新增LastReportResourceCode | 验收失败 |
| F73 | 工序级LastReportResourceCode缺失 | 验收失败 |
| F74 | GoodQty / GoodCompletedQty改名或互换 | 验收失败 |
| F75 | 无TaskNo真实报工被忽略 | 验收失败 |
| F76 | 5号位替2号位做Q/E Continuation分桶 | 验收失败 |
| F77 | 5号位提供SH第二主档/权威总量 | 验收失败 |
| F78 | 5号位提供普通Demand↔SH固定关系 | 验收失败 |
| F79 | 5号位用TransitQty倒算未生产量 | 验收失败 |
| F80 | 查询成功0行被当跨厂接口失败 | 验收失败 |
| F81 | 5号位计算SH SourceReadyTime | 验收失败 |
| F82 | 5号位把Transit/Received/ZP/BP变成独立Supply | 验收失败 |
| F83 | 把ERP_InterplantInTransit_View接入SupplyFact_Pipeline | 验收失败 |
| F84 | 把ERP_Received_ByDocument_View作为SH Supply或部署必需Supply接口 | 验收失败 |

---

## 十三、代码整改检查点

不强制新增特定文件/类名，但必须检查：

1. 现有PI Position计算服务是否可复用MES OperationProgress与Routing信息；
2. 是否能够一次批量返回PI Position + NextOperationContext；
3. 是否保留DataCutoffTime，保证同一Run事实一致；
4. 是否存在逐Demand/逐Allocation远程调用路径，如有必须退出正式主链；
5. 是否存在5号位基于Demand Priority/Resource Load决定下一工序的逻辑，如有必须删除；
6. 是否存在NextOperation独立Supply或Operation级Allocation设计，如有必须删除；
7. 是否把2号位运行快照持久化职责错误搬到5号位，如有必须纠正；
8. 是否为NextOperationContext新增了未经冻结的DDL，如有先停止并等待数据库专项裁决。

---

## 十四、本版明确不新增

- Operation级Supply；
- Operation级Pegging；
- Operation Position平台；
- NextOperationContext独立生命周期平台；
- 第二套MES进度解释器；
- 资源选择逻辑；
- 排程优化逻辑；
- ShippingTask；
- MultiDomain Candidate；
- 新的Domain运行模型；
- 新的规则插件平台。

---

## 十五、v1.3完成定义

本专项完成必须同时满足：

- PI Position仍由5号位业务计算；
- NextOperationContext由5号位基于MES + Routing + PI Position形成；
- 5号位不参与Pegging/Allocation/Solver；
- 2号位不重新解释MES；
- ProductionInstruction仍是Supply；
- Position与NextOperationContext均不形成新Supply；
- 同PI可形成多个互斥执行起点数量切片；
- 已完成工序不重复进入有限产能排程；
- 2号位能够把执行起点传给1号位；
- 1号位从正确Operation继续Routing；
- 不新增Operation级Pegging；
- 不提前新增DDL。

---

## 十六、一句话交付要求

> **5号位在现有PI Position和MES事实标准化能力上补齐NextOperationContext：把“PI剩余数量在哪里”继续解释为“下一步从哪个Stage/Operation继续”，以批量事实DTO交给2号位；不碰Demand分配、Supply扣减、设备选择和排程时间，也不把NextOperationContext做成新的Supply、Position平台或Operation级Pegging。**



---
