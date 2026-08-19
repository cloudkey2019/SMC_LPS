# APS V1 5号位复杂业务事实与ODS开发实施包（冻结版）

**版本**：v1.0  
**日期**：2026-08-14  
**适用对象**：5号位及其开发AI  
**文档性质**：从零开发实施说明  
**上位依据**：APS V1三份业务冻结文档 + 六份技术冻结文档 +《APS V1关键接口冻结：1↔2、2↔5、2↔3》

---

# 一、5号位在APS V1中的定位

5号位不是“规则插件中心”，也不是Pegging执行器。

5号位在V1中的核心职责是：

> **把ERP/MES/跨厂/采购等复杂、易变、需要业务知识解释的源事实，整理成2号位可以稳定消费的标准事实。**

5号位重点负责：

1. BOM入口和Workset复杂派生；
2. PI Position（生产指示位置）；
3. MES工序/大工艺进度事实的标准化；
4. 跨厂模式识别所需复杂事实；
5. 采购、VMI、已到厂未入库、厂间在途等Timed Supply事实；
6. Received按单据号等强事实；
7. ProcessCode / ERPProperty等ODS防腐事实；
8. 数据问题/异常Issue的识别和返回。

5号位不负责：

- Demand最终排序；
- Supply最终挑选顺序；
- Allocation数量扣减；
- Demand Protection最终执行；
- Strict Binding最终执行；
- Task生成；
- FinalTask；
- 有限产能Solver；
- ScheduleRun/PlanVersion创建；
- Candidate激活；
- 最终Explanation ReasonCode裁决；
- 数据库中APS运行结果的最终持久化。

一句话：

> **5号位负责“事实算清楚”，2号位负责“怎么分”，1号位负责“怎么排”。**

---

# 二、总体开发原则

## 2.1 复杂事实前置，主流程保持稳定

只有真正满足以下条件的逻辑，才放到5号位：

- 源系统字段复杂；
- 需要ERP/MES业务知识解释；
- 未来可能随源系统变化；
- 如果放进2号位会让Pegging主流程持续被源系统细节污染。

简单、稳定、与源系统无关的主流程逻辑，不要拆给5号位。

例如：

- “先选PI再消费Position”属于2号位；
- “这个PI当前剩余量分别位于XC/Stage/Transit多少”属于5号位。

---

## 2.2 5号位返回事实，不返回最终业务决策

正确输出：

- Position；
- AvailableTime；
- SourceDocument；
- WarehouseRole；
- StrongEvidence；
- Issue；
- CrossFactoryMode Fact；
- Procurement Fact。

不正确输出：

- “这个PI应该给订单A”；
- “这个订单优先级=95”；
- “把供给锁给订单B”；
- “生成Task”；
- “这个订单必须延期3天”。

这些都不属于5号位。

---

## 2.3 尽量批量，不逐笔远程调用

夜间全量目标约10万Task，不能做：

> 2号位每分一笔Demand → RPC调用5号位一次。

应优先：

- 批量SP；
- 批量视图；
- 批量事实DTO；
- 一次装载到2号位内存。

白天Candidate可用小批量实时接口，但仍尽量按订单/PI批量。

---

# 三、5号位正式交付模块

建议按以下六个模块开发，不要求拆成六个服务进程，可在一个项目内按职责组织。

## 模块A：BOM防腐与Workset

## 模块B：PI Position Calculator

## 模块C：MES进度事实标准化

## 模块D：跨厂事实

## 模块E：采购/VMI/Timed Supply事实

## 模块F：ODS契约与Issue治理

---

# 四、模块A：BOM防腐与Workset

## 4.1 正式输入

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

---

## 4.2 需要继续遵守的防腐结构

正式BOM链仍为：

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
- 搬运APS RAW；
- OrderBomRequestLink；
- 在PlanVersion内消费。

---

## 4.3 BOMNO为空时

继续执行已冻结的BOM入口解析逻辑。

5号位可以使用：

- OrderType；
- MaterialCode；
- FactoryCode；
- ProcessCode相关事实；
- BOM边；
- ERP属性；

解析真正入口。

但结果只返回：

- ResolvedBOMNO事实；
- Workset；
- Issue。

不决定订单优先级。

---

## 4.4 Workset Issue等级

建议最少分：

- ERROR：该对象无法形成可靠业务事实；
- WARN：可以保守降级继续；
- INFO：追溯性信息。

### ERROR不等于整个ScheduleRun失败

是否导致某Domain失败，由2号位根据数量闭合和业务影响决定。

---

# 五、模块B：PI Position Calculator

这是5号位V1最重要的新能力。

# 5.1 业务目标

给定一张生产指示PI：

> ERP告诉APS还有100件没有最终进入目标M库。

5号位要回答：

> 这100件现在分别在哪里？

例如：

- Stage A：20；
- Stage B：30；
- XC：10；
- 厂间在途：25；
- UNLOCATED：15。

必须满足：

> 20 + 30 + 10 + 25 + 15 = 100。

---

## 5.2 PI RemainingQty总量边界

最高红线：

> **PI RemainingQty由ERP定义，是该PI尚未最终进入目标M库的全部剩余数量。**

禁止：

```text
ERP RemainingQty
+ MES WIP
+ XC
+ Transit
```

这样会重复放大总量。

MES WIP/XC/Transit只是在解释：

> RemainingQty当前的位置。

---

## 5.3 PI Position输入

建议批量DTO：

```csharp
public sealed class ProductionInstructionPositionInput
{
    public string ProductionInstructionNo { get; init; } = default!;
    public int MaterialId { get; init; }
    public int FactoryId { get; init; }

    public decimal ErpRemainingQty { get; init; }

    public IReadOnlyList<StageProgressFact> StageProgress { get; init; } = [];
    public IReadOnlyList<OperationProgressFact> OperationProgress { get; init; } = [];

    public IReadOnlyList<PiInventoryFact> PiInventories { get; init; } = [];
    public IReadOnlyList<XcFact> XcFacts { get; init; } = [];
    public IReadOnlyList<InterplantTransitFact> TransitFacts { get; init; } = [];

    public IReadOnlyList<StagePathFact> StagePath { get; init; } = [];
    public IReadOnlyList<ReceivedFact> StrongFacts { get; init; } = [];
}
```

具体字段名可按现有数据库DTO风格调整，语义不能变。

---

## 5.4 Position输出

```csharp
public sealed class ProductionInstructionPositionResult
{
    public string ProductionInstructionNo { get; init; } = default!;
    public decimal TotalRemainingQty { get; init; }

    public IReadOnlyList<PositionSlice> Positions { get; init; } = [];
    public IReadOnlyList<PositionIssue> Issues { get; init; } = [];
}
```

`PositionSlice`建议至少：

- PositionType；
- StageCode/LocationKey；
- Quantity；
- AvailableTime（适用时）；
- StrongEvidence标记；
- SourceKey；
- IsUnlocated。

PositionType最少需要支持：

- `STAGE`
- `XC`
- `INTERPLANT_IN_TRANSIT`
- `WAITING`
- `UNLOCATED`

如果物理DDL里已有兼容枚举，开发可做映射，不要求为了命名重建表。

---

# 六、PI Position计算规则

## 6.1 Stage累计量异常

如果：

> 下游Stage完成量 > 上游Stage完成量

V1保守处理：

> 下修下游有效完成量，不制造凭空数量。

---

## 6.2 中间Stage缺失

如果：

Stage1有数据  
Stage2缺失  
Stage3有数据

采用：

> **下游已证明的最小完成量**作为保守依据。

不要因为Stage2缺数据就把Stage3已经证明的事实全部丢掉。

---

## 6.3 强事实校正

XC、跨厂在途、Received等强事实可能证明某些数量已经越过上游Stage。

允许：

> 用强事实修正“规划有效位置”。

但禁止：

> 回写/篡改MES源报工。

5号位只返回规划解释结果。

---

## 6.4 Position必须互斥

同一物理10件：

不能同时：

- 算在Stage B；
- 又算XC；
- 又算Transit。

必须先识别更强/更后状态，再从上游Stage份额中扣除。

---

## 6.5 UNLOCATED

无法可靠定位但总量又必须闭合时：

> 剩余份额进入UNLOCATED。

UNLOCATED不是错误Supply，也不是丢失数量。

2号位后续会按冻结规则：

> 从该PI承载路径最早Stage开始保守形成计划需求。

---

## 6.6 PI Position失败边界

### 可降级

- 某一Stage字段缺失；
- 某个非关键来源无数据；
- 可通过UNLOCATED闭合。

### 必须报严重Issue

- Position总量无法等于ERP RemainingQty；
- 同一物理份额被重复识别；
- PI号/物料/工厂主键冲突；
- 强事实数量超过总RemainingQty且无法解释。

严重Issue由2号位决定是否使Domain失败。

---

# 七、PI Position持久化

冻结口径：

> 一张最小`ProductionInstructionPositionSnapshot`即可。

不建设：

- PI Position Header；
- Position Slice独立生命周期；
- Position事件溯源平台。

允许：

> 一个PI对应多行Position Snapshot。

5号位可负责计算，2号位负责最终APS侧快照持久化；如果实现上现有SP更适合由5号位直接写ODS侧事实，应保持“ODS事实与APS运行结果”边界清楚。

---

# 八、模块C：MES进度事实标准化

## 8.1 三类输入

继续保留：

- `MESWorkOrderSnapshot`
- `OperationProgressSnapshot`
- `StageProgressSnapshot`

5号位的重点不是再造第四种进度表，而是：

> 把这些原始/汇总事实转成PI Position可稳定消费的输入。

---

## 8.2 MES五态

继续使用：

- 0 待开工；
- 1 开工中；
- 2 完工报工；
- 3 未完工报工；
- 4 未完工报工已完结（手动完工）。

5号位不得新增：

- PAUSED；
- SUSPENDED；
- RESUME。

状态3不是完成，状态4要保留“人工完结且数量不足”的来源事实。

---

## 8.3 不匹配历史Task

MES进度：

> 不根据历史TaskId去找昨天的排程Task。

Task/Pegging跨PlanVersion重新计算。

5号位应以：

- PI；
- Material；
- OperationName；
- Stage；
- WorkOrder；

等业务锚点解释现实位置。

---

# 九、模块D：跨厂事实

跨厂必须区分两种业务模式。

# 9.1 STAGE_HANDOFF

业务含义：

> 同一制造链的大工艺跨厂继续加工。

5号位需要提供：

- Stage跨厂边；
- 源厂/目标厂；
- PI对应的跨厂在途；
- Transport/Inspection/Transfer LT；
- ERPProperty/仓库位置事实；
- 相关Issue。

不生成：

- ShippingTask；
- 物流有限产能；
- 车辆/月台Task。

---

## 9.2 INTER_FACTORY_ORDER

业务含义：

> 厂间订单/出荷指示供给。

5号位需要标准化：

- SH号；
- Material；
- SourceFactory；
- ReceivingFactory；
- SH总量/剩余量；
- 对应Transit；
- 同SH号Received；
- ZP/BP事实；
- AvailableTime；
- Document关系。

### 强红线

Received只能匹配：

> **同一个SH号。**

禁止“同物料就算可用”。

---

## 9.3 在途与Received去重

同一物理份额：

> 到货后不能继续同时留在Transit。

需要提供：

- PhysicalSourceKey；
- SourceDocument；
- SourceLine（如果源系统有）；
- 状态/更新时间；

帮助2号位避免重复消费。

---

## 9.4 Transport LT

不允许再返回：

> “跨Domain固定2天”。

应返回真实可配置事实，例如：

- SourceFactory；
- TargetFactory；
- Stage；
- TransportLT；
- InspectionLT；
- TransferLT。

最终：

> 上游完成 + LT = 下游AvailableTime。

---

# 十、模块E：采购 / VMI / Timed Supply

V1必须真实提供：

- 已到厂未入库；
- 正式PO剩余；
- VMI；
- 采购在途；
- 其它冻结文档列明的Timed Supply。

不能继续把Pipeline固定返回空集合。

---

## 10.1 标准输出字段

建议统一标准事实DTO：

```csharp
public sealed class TimedSupplyFact
{
    public string SupplyType { get; init; } = default!;
    public string PhysicalSourceKey { get; init; } = default!;

    public int MaterialId { get; init; }
    public int FactoryId { get; init; }
    public string WarehouseCode { get; init; } = default!;

    public decimal RemainingQty { get; init; }

    public DateTime? Eta { get; init; }
    public DateTime AvailableTime { get; init; }

    public string Commitment { get; init; } = default!;
    public string Confidence { get; init; } = default!;

    public string? SourceDocumentNo { get; init; }
    public string? SourceDocumentLineNo { get; init; }
    public DateTime? SourceUpdatedAt { get; init; }
}
```

---

# 十一、采购ETA规则

5号位/ODS事实层尽量把Effective ETA算好。

冻结优先级：

1. 人工ETA；
2. ERP ETA；
3. 默认采购LT。

人工ETA最小粒度：

> PO号 + 行号 + Material + Receiving Warehouse。

删除/取消人工ETA后：

> 自动回ERP ETA。

---

## 11.1 Default Purchase LT

不增加ProductFamily维度。

主要维度：

- Receiving Warehouse；
- Material/必要采购属性。

不重复保存Factory维度，如果WarehouseCode全局唯一可推出工厂。

Base Date：

> PO正式Release/Issue Date。

---

## 11.2 已逾期的默认ETA

如果：

> ReleaseDate + DefaultLT < Now

不能继续给一个过去时间。

按冻结参数：

> 应用逾期Margin/保守修正。

具体比例由3号位参数治理，5号位只执行本次冻结参数。

---

## 11.3 Arrival-to-usable Offset

已到厂不代表立即可用。

主要按：

- Receiving Warehouse；
- 必要Inspection/Inbound Offset；

形成AvailableTime。

---

# 十二、VMI

VMI必须作为独立SupplyType保留。

不要：

> 把VMI混成普通PO。

至少保留：

- VMI SourceKey；
- Warehouse；
- Qty；
- AvailableTime；
- Commitment；
- SourceUpdatedAt。

---

# 十三、Planning-only Purchase Placeholder边界

这个对象**不是5号位真实源事实**。

当真实：

- Inventory；
- Arrived；
- PO；
- VMI

全部不足时，2号位才根据缺口创建内存Placeholder。

因此5号位：

- 不建Placeholder表；
- 不生成Placeholder记录；
- 不伪造PO；
- 不回写ERP。

5号位只需要保证：

> 默认采购LT、Warehouse Offset等参数事实可供2号位使用。

---

# 十四、模块F：ODS契约与防腐层

## 14.1 ODS契约的目标

APS_Production不应该知道：

- ERP内部复杂表名；
- MES内部多套工艺表；
- 采购系统内部字段变化。

因此5号位要继续以：

> 稳定View / Workset / Standard Fact

隔离源系统变化。

---

## 14.2 关键契约

V1重点至少包括：

- ERP_Master_View / ext包装；
- MES_Material_View / ext包装；
- MES_ProcessCode_View；
- MES_APS_BOM相关；
- ERP_Received_ByDocument_View；
- ERP_InterplantInTransit_View；
- 采购/VMI相关真实契约；
- MES进度统一View；
- Routing/Resource源事实契约（数据Owner边界）。

---

## 14.3 ProcessCodeDict.ERPProperty

ERPProperty必须来自：

> ERP真实业务属性。

5号位负责同步/透出。

禁止：

- 根据ProcessName猜M/XC/ZP/BP；
- 根据WarehouseRole临时推断；
- 用人工粗规则替代ERP真实属性。

2号位消费`MES_ProcessCode_View`即可。

---

# 十五、数据Issue返回规范

建议统一结构：

```csharp
public sealed class BusinessFactIssue
{
    public string IssueType { get; init; } = default!;
    public string Severity { get; init; } = default!;

    public string ObjectType { get; init; } = default!;
    public string ObjectKey { get; init; } = default!;

    public string Message { get; init; } = default!;
    public string? SuggestedAction { get; init; }
}
```

5号位只说明：

> “事实哪里有问题”。

不替1号位生成排程ReasonCode，不替2号位判订单延期。

---

# 十六、5号位与2号位的正式接口

## 16.1 PI Position

冻结接口语义：

```csharp
Task<IReadOnlyList<ProductionInstructionPositionResult>>
CalculateProductionInstructionPositionsAsync(
    IReadOnlyList<ProductionInstructionPositionInput> inputs,
    FrozenFactParameters parameters,
    CancellationToken ct);
```

可以本地Service/Library形式实现，不强制远程微服务。

---

## 16.2 Timed Supply

优先：

```csharp
Task<IReadOnlyList<TimedSupplyFact>>
LoadTimedSupplyFactsAsync(
    SupplyFactScope scope,
    CancellationToken ct);
```

Nightly一次性批量加载。

---

## 16.3 CrossFactory Facts

可单独接口，也可并入Timed Supply/BOM Fact。

只要2号位能稳定得到：

- Mode；
- SH/PI；
- Transit；
- Received；
- LT；
- PhysicalSourceKey；

即可。

---

# 十七、不要做成5号位“万能插件”

明确禁止以下架构：

```text
2号位每遇到业务判断
→ 调5号位Plugin
→ 5号位返回Voucher
→ 2号位执行
```

V1不做这种泛化平台。

5号位只保留真正复杂、源系统相关的事实计算。

---

# 十八、与3号位的边界

3号位负责规则参数治理。

5号位可以消费：

- Default Purchase LT；
- Margin；
- Position容错阈值；
- Warehouse Offset；

等冻结参数。

但5号位不负责：

- RuleSet CRUD；
- StrategyProfile发布；
- Demand排序规则维护。

一次Run中使用的参数版本由3号位冻结，2号位装载后传给5号位，或5号位按同一Snapshot消费。

---

# 十九、与1号位的边界

1号位原则上不直接读取5号位ODS事实。

正确链：

```text
5号位复杂事实
→ 2号位解释/消费
→ DomainSolveRequest
→ 1号位
```

例如：

- StageProgress不是1号位原始输入；
- PI Position消费后的StartStage才是1号位输入；
- PO ETA不是1号位自己算；
- Material AvailableTime由2号位整理后传给1号位。

---

# 二十、开发顺序

建议按以下顺序，优先支撑2号位主链。

## 阶段A：PI Position最小闭环

1. PI输入DTO；
2. ERP RemainingQty；
3. StageProgress；
4. XC；
5. Transit；
6. UNLOCATED；
7. 总量闭合；
8. Issue。

先做测试Fixture，不等所有数据源接齐。

---

## 阶段B：跨厂事实

1. STAGE_HANDOFF；
2. INTER_FACTORY_ORDER；
3. SH关联；
4. Received同单号；
5. Transit去重；
6. Transport LT。

---

## 阶段C：Procurement/VMI

1. Arrived-not-inbound；
2. PO Remaining；
3. VMI；
4. ETA优先级；
5. AvailableTime；
6. DefaultLT；
7. Warehouse Offset。

---

## 阶段D：BOM/ODS完善

1. Realtime BOM；
2. Workset；
3. CrossFactoryEdge；
4. ProcessCode ERPProperty；
5. SourceContract性能与Issue。

---

## 阶段E：性能和数据质量

1. 夜间批量；
2. Candidate小批量；
3. 索引；
4. 数据去重；
5. Issue追溯；
6. 来源更新时间。

---

# 二十一、最低验收用例

| 编号 | 场景 | 必须结果 |
|---|---|---|
| F01 | PI Remaining=100，Stage分布正常 | Position合计100 |
| F02 | 下游Stage>上游Stage | 保守修正，不放大数量 |
| F03 | 中间Stage缺失 | 使用下游已证明最小完成量 |
| F04 | XC与Stage重叠 | 同一物理份额只保留一个Position |
| F05 | Transit与Stage重叠 | 去重 |
| F06 | Position无法定位15件 | UNLOCATED=15，总量仍闭合 |
| F07 | 强事实证明已越过Stage | 规划位置修正，不改MES源事实 |
| F08 | 多个PI同Material | 5号位分别返回，2号位决定先选哪个PI |
| F09 | STAGE_HANDOFF | 返回PI/Transit/LT事实，不生成ShippingTask |
| F10 | INTER_FACTORY_ORDER同SH Received | 正确绑定 |
| F11 | 同物料不同SH | 不可串用Received |
| F12 | Transit已Received | 不重复计算 |
| F13 | PO有人工ETA | 人工ETA优先 |
| F14 | 人工ETA删除 | 回退ERP ETA |
| F15 | ERP ETA为空 | DefaultLT |
| F16 | DefaultETA已过期 | 应用冻结Margin |
| F17 | 已到厂未入库 | AvailableTime含Warehouse Offset |
| F18 | VMI | 独立SupplyType |
| F19 | ProcessCode ERPProperty | 来自ERP真实属性，不猜 |
| F20 | 采购真实Supply全空 | 只返回真实空，不生成Placeholder |
| F21 | ODS单条坏数据 | Issue登记，不污染其它事实 |
| F22 | PI总量无法闭合 | 严重Issue，2号位可据此Fail Domain |

---

# 二十二、性能要求

PI Position应支持：

> 夜间批量计算全部活跃PI，而不是逐PI数据库往返。

建议：

- 一次批量加载StageProgress；
- 一次批量加载XC；
- 一次批量加载Transit；
- 内存按PI索引；
- 批量输出。

采购/Timed Supply同理。

避免：

- 每PI查5次DB；
- 每PO逐条调用源系统；
- Candidate重跑全夜间ETL。

---

# 二十三、5号位最终交付物

1. PI Position Calculator；
2. PI Position输入/输出DTO；
3. Timed Supply标准事实DTO；
4. 两类跨厂事实实现；
5. Procurement/VMI/Arrived标准化；
6. BOM/Workset必要SP与View；
7. ERPProperty真实同步链；
8. Issue结构与日志；
9. F01～F22测试结果；
10. 批量性能测试报告；
11. 真实源数据尚未接通的清单；
12. 与2号位联调记录。

---

# 二十四、严禁新增的过度设计

5号位V1不得建设：

- 通用规则Plugin平台；
- Voucher平台；
- PI Header+Slice双表生命周期；
- CrossFactory通用状态机平台；
- ShippingTask；
- Logistics Solver；
- Procurement Task；
- Planning-only Placeholder表；
- SupplyAvailabilityRule DSL；
- 通用事件溯源；
- 通用主数据MDM平台；
- MultiDomain编排；
- Explanation原因推理平台。

---

# 二十五、完成定义（Definition of Done）

5号位完成必须同时满足：

- PI RemainingQty总量语义正确；
- Position互斥且总量闭合；
- UNLOCATED可保守承接；
- MES进度不依赖历史Task；
- 两类跨厂事实区分正确；
- SH Received严格同单号；
- Transit/Received不重复；
- Transport LT真实可配置；
- PO/VMI/Arrived正式进入Timed Supply；
- ETA优先级正确；
- ERPProperty来自ERP真实事实；
- Planning-only Placeholder没有被5号位落表；
- 2号位无需知道源系统复杂表结构；
- 没有替2号位做Pegging；
- 没有替1号位生成Task；
- F01～F22通过；
- 无新增V1过度设计。

---

# 二十六、一句话交付要求

> **5号位的V1任务不是做一个“万能业务规则插件”，而是把PI位置、跨厂、采购/VMI、MES进度和BOM等最复杂、最容易受源系统变化影响的事实算清楚并标准化，让2号位能够稳定完成Pegging，让1号位只看到干净的有限产能输入。**
