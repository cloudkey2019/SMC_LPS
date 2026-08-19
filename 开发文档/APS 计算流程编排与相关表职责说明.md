# APS 计算流程编排与相关表职责说明

> 本文档是 ScheduleRun / PlanVersion / Scenario / SimulationRun 四表职责及多运行模式编排的专题说明，用于解释运行对象、结果版本和规则参数绑定关系；不替代《APS 核心排产全流程走查》。具体端到端时序、跨系统数据流、异常处理、联调步骤，以《APS 核心排产全流程走查》为准。

## 按最终简化口径整理版

## 一、最终收敛后的核心原则

本次设计要解决的问题是：APS 后续不仅有凌晨全量排程，还会有人工重排、局部重排、仿真、多方案试算、插单影响分析等多种运行方式。因此必须把“一次运行”和“一套结果版本”分开。

最终口径如下：

```text
ScheduleRun    = 记录这次怎么跑
PlanVersion    = 记录跑完以后形成了哪一套结果版本
Scenario       = 记录为什么要这样试算、试算假设、目标、最终选中版本
SimulationRun  = 记录某个场景下具体用什么算法和参数执行了一次仿真
```

本次最终调整后，明确：

1. `ScheduleRun` 不再保留 `OutputPlanVersionId（产出计划版本ID）`。
  
2. 也不增加 `PrimaryOutputPlanVersionId（主输出计划版本ID）`。
  
3. `PlanVersion` 增加来源字段，用来说明自己是由哪次运行产出的。
  
4. 多方案仿真时，不增加 `CandidateRank（候选排序）`，也不增加 `IsRecommended（是否推荐）`。
  
5. 也不新增“运行产出版本关系表”。
  
6. 如果多个候选版本中选中一个，就只在 `Scenario.SelectedPlanVersionId（场景选中计划版本ID）` 记录被选中的版本。
  
7. 正式采用哪个计划版本，直接通过 `PlanVersion.Status = ACTIVE（正式采用）` 表示。
  

一句话理解：

```text
ScheduleRun 记录运行过程；PlanVersion 记录运行结果；Scenario 记录试算假设和选中版本；SimulationRun 记录算法执行过程；PlanVersion.Status = ACTIVE 表示正式采用版本。
```

### 补充说明：规则与参数引擎（v3.29 2026-06-23）

通过 RuleSetVersion/ParameterSetVersion/StrategyProfileVersion 形成可发布、可追溯的策略包。`ScheduleRun.StrategyProfileVersionId` 在创建时绑定策略包版本。规则参数是运行输入，不是运行结果。已发布版本不可原地修改。

---

# 二、关键表的职责分工

## 1. ScheduleRun（排程运行编排表 / Scheduling Run）

### 1.1 表的定位

`ScheduleRun` 记录的是“一次运行过程”。

它回答的问题是：

- 这次是谁触发的？
  
- 是凌晨全量，还是人工重排、局部重排、仿真、插单分析？
  
- 使用哪个数据截止时间？
  
- 是基于哪个已有版本重排或仿真的？
  
- 运行成功还是失败？
  
- 什么时候开始，什么时候结束？
  
- 如果失败，错误原因是什么？
  

它不负责记录“产出了哪个结果版本”。  
产出版本要从 `PlanVersion.SourceScheduleRunId（来源排程运行ID）` 反查。

### 1.2 建议字段

| 英文字段 | 中文名称 | 说明  |
| --- | --- | --- |
| `Id` | 排程运行ID | 一次运行的唯一编号 |
| `RunType` | 运行类型 | 例如：凌晨全量、人工重排、局部重排、仿真、插单分析 |
| `Status` | 运行状态 | 运行中、已完成、失败 |
| `ScenarioId` | 场景ID | 仿真和插单分析通常关联场景；人工重排、局部重排可为空 |
| `BasePlanVersionId` | 基准计划版本ID | 表示本次运行是基于哪个已有计划版本进行的 |
| `DataCutoffTime` | 数据截止时间 | 本次运行使用的数据切片边界 |
| `ScopeJson` | 重排范围JSON | 局部重排时记录本次只重排哪些工厂、资源组、订单等范围 |
| `StrategyProfileVersionId` | 策略包版本ID | 本次运行采用的策略包版本；FK→StrategyProfileVersion.Id；V1可为空，新运行应用层强制写入 |
| `TriggeredBy` | 触发来源 | Hangfire、用户、API、Agent 等 |
| `StartedAt` | 开始时间 | 本次运行开始时间 |
| `CompletedAt` | 完成时间 | 本次运行结束时间 |
| `ErrorMessage` | 错误信息 | 运行失败时记录原因 |

### 1.3 外键关系

| 字段  | 外键指向 | 是否必填 |
| --- | --- | --- |
| `ScenarioId` | `Scenario.Id` | 仿真/插单分析通常必填；人工重排、局部重排可为空 |
| `BasePlanVersionId` | `PlanVersion.Id` | 凌晨全量可为空；其他运行原则上应填写 |
| `StrategyProfileVersionId` | `StrategyProfileVersion.Id` | V1兼容历史可为空；新运行建议必填 |

### 1.4 重要说明

`ScheduleRun` 不再记录 `OutputPlanVersionId`。  
原因是：一次运行未来可能产生多个候选版本，如果在 `ScheduleRun` 上只放一个产出版本字段，会产生歧义。

例如，一次仿真产生方案 A、方案 B、方案 C：

```text
ScheduleRun 到底写哪个 OutputPlanVersionId？

```

所以最终改成：

```text
每一条 PlanVersion 自己记录 SourceScheduleRunId。

```

即：

```text
PlanVersion.SourceScheduleRunId = 这套结果来自哪次 ScheduleRun。

```

---

## 2. PlanVersion（计划版本表 / Plan Version）

### 2.1 表的定位

`PlanVersion` 记录的是“一套排程结果版本”。

它回答的问题是：

- 这套结果版本是什么？
  
- 是由哪次运行产生的？
  
- 如果是仿真结果，是由哪次仿真算法执行产生的？
  
- 这套版本是候选版本，还是正式采用版本？
  
- 这套版本对应哪些任务、供需绑定、KPI、订单摘要、资源负荷、解释事实？
  

一套结果版本对应一条 `PlanVersion`。  
如果一次仿真产生三套候选方案，就创建三条 `PlanVersion`。

### 2.2 建议字段

| 英文字段 | 中文名称 | 说明  |
| --- | --- | --- |
| `Id` | 计划版本ID | 一套计划结果的唯一编号 |
| `VersionCode` | 计划版本编号 | 业务可读的版本号 |
| `VersionCategory` | 版本类别 | 例如：日排程正式版本、人工重排候选、仿真候选、插单分析候选 |
| `Status` | 版本状态 | 候选、正式采用、归档、失败等 |
| `SourceScheduleRunId` | 来源排程运行ID | 说明这套版本由哪次 `ScheduleRun` 产出 |
| `SourceSimulationRunId` | 来源仿真运行ID | 如果是仿真结果，说明来自哪次 `SimulationRun` |
| `PlanHorizonStart` | 计划开始日期 | 本版本覆盖的计划起点 |
| `PlanHorizonEnd` | 计划结束日期 | 本版本覆盖的计划终点 |
| `DomainKey` | 分域标识 | 表示本版本覆盖的工厂、产品族、资源域等 |
| `BatchNo` | 数据批次号 | 关联本次输入数据准备批次 |
| `SnapshotFilePath` | 快照文件路径 | 用于回放、追溯、版本比较 |
| `SnapshotFileHash` | 快照文件哈希 | 用于校验快照完整性 |
| `ActivatedAt` | 正式采用时间 | 当版本被正式采用时记录 |
| `ActivatedBy` | 正式采用人/来源 | 用户、调度器或审批流 |

### 2.3 外键关系

| 字段  | 外键指向 | 是否必填 |
| --- | --- | --- |
| `SourceScheduleRunId` | `ScheduleRun.Id` | 建议必填 |
| `SourceSimulationRunId` | `SimulationRun.Id` | 只有仿真版本填写，其他版本为空 |

### 2.4 版本状态建议

| 状态  | 中文含义 | 说明  |
| --- | --- | --- |
| `BUILDING` | 构建中 | 版本正在生成中 |
| `CANDIDATE` | 候选版本 | 已生成，但还没有正式采用 |
| `ACTIVE` | 正式采用 | 当前被正式采用的计划版本 |
| `ARCHIVED` | 已归档 | 历史版本 |
| `FAILED` | 失败  | 版本生成失败或不可用 |

### 2.5 重要说明

`PlanVersion` 不再承担运行过程日志职责。

以下内容不应再作为 `PlanVersion` 的权威职责：

- 排程运行开始时间；
  
- 排程运行完成时间；
  
- 运行耗时；
  
- 运行错误信息。
  

这些应以 `ScheduleRun` 为权威。

正式采用哪个版本，直接看：

```text
PlanVersion.Status = ACTIVE

```

为了避免混乱，同一个计划范围、同一个分域下，应只允许一条 `PlanVersion.Status = ACTIVE`。

---

## 3. Scenario（场景表 / Scenario）

### 3.1 表的定位

`Scenario` 记录的是“一个业务试算场景”。

它回答的问题是：

- 为什么要试算？
  
- 假设条件是什么？
  
- 优化目标是什么？
  
- 这次场景下最终选中了哪个计划版本？
  

`Scenario` 不是运行记录，也不是结果版本。

适合建 `Scenario` 的场景包括：

- 仿真试算；
  
- 插单影响分析；
  
- 设备停机影响分析；
  
- 加班能力分析；
  
- 优先级变化分析；
  
- 多目标优化方案比较。
  

人工重排和局部重排不强制创建 `Scenario`。

### 3.2 建议字段

| 英文字段 | 中文名称 | 说明  |
| --- | --- | --- |
| `Id` | 场景ID | 一个业务试算场景的唯一编号 |
| `ScenarioName` | 场景名称 | 例如“BJ工厂加班4小时仿真” |
| `ScenarioType` | 场景类型 | 仿真、插单分析、设备停机、加班、优先级调整等 |
| `AssumptionJson` | 假设条件JSON | 记录本次试算的假设条件 |
| `ObjectiveJson` | 优化目标JSON | 记录本次试算关注的目标，例如交期、库存、切换、负荷等 |
| `SelectedPlanVersionId` | 选中计划版本ID | 如果多个候选版本中选中一个，就记录在这里 |
| `Status` | 场景状态 | 草稿、运行中、已完成、已选择、已提交等 |
| `CreatedBy` | 创建人 | 谁创建了场景 |
| `CreatedAt` | 创建时间 | 场景创建时间 |

### 3.3 外键关系

| 字段  | 外键指向 | 是否必填 |
| --- | --- | --- |
| `SelectedPlanVersionId` | `PlanVersion.Id` | 可空；只有场景完成并选中某个方案后填写 |

### 3.4 重要说明

`SelectedPlanVersionId（选中计划版本ID）` 只表示：

```text
这个场景最终选中了哪个结果版本。

```

它不表示：

- 这个场景只产出一个版本；
  
- 这个版本已经正式采用；
  
- 这个版本已经发布到车间。
  

正式采用仍然要更新：

```text
PlanVersion.Status = ACTIVE

```

---

## 4. SimulationRun（仿真运行表 / Simulation Run）

### 4.1 表的定位

`SimulationRun` 记录的是“某个场景下的一次算法执行”。

它回答的问题是：

- 这次仿真属于哪个场景？
  
- 属于哪次排程运行？
  
- 使用了什么算法？
  
- 使用了什么参数？
  
- 算法执行成功还是失败？
  
- 什么时候开始和结束？
  

它不需要记录唯一 `PlanVersionId`。  
如果一次仿真算法产生多个候选方案，就通过 `PlanVersion.SourceSimulationRunId（来源仿真运行ID）` 反查这些候选版本。

### 4.2 建议字段

| 英文字段 | 中文名称 | 说明  |
| --- | --- | --- |
| `Id` | 仿真运行ID | 一次算法执行的唯一编号 |
| `ScenarioId` | 场景ID | 属于哪个业务场景 |
| `ScheduleRunId` | 排程运行ID | 属于哪次统一运行编排 |
| `AlgorithmType` | 算法类型 | 规则算法、遗传算法、OR-Tools、混合算法等 |
| `AlgorithmVersion` | 算法版本 | 记录算法版本 |
| `AlgorithmConfigJson` | 算法参数JSON | 记录算法配置、权重、迭代参数等 |
| `Status` | 仿真运行状态 | 运行中、已完成、失败 |
| `StartedAt` | 开始时间 | 算法开始时间 |
| `CompletedAt` | 完成时间 | 算法完成时间 |
| `ErrorMessage` | 错误信息 | 算法失败时记录原因 |

### 4.3 外键关系

| 字段  | 外键指向 | 是否必填 |
| --- | --- | --- |
| `ScenarioId` | `Scenario.Id` | 仿真场景下必填 |
| `ScheduleRunId` | `ScheduleRun.Id` | 建议必填 |

---

# 三、凌晨全量排程时间点与计算流程

本节按走查文档中的全量排程时间点整理。

## 0. 白天增量订单链路

### 时间

白天持续发生。

### 动作

```text
ERP 销售订单 / 生产指示    ↓ERP_Order_Staging（ERP订单暂存表）    ↓sp_ValidateAndPromoteOrders（订单校验与提升）    ↓Order_Canonical（订单标准表）

```

### 说明

白天订单持续进入 APS，但凌晨全量排程只从符合条件的 `Order_Canonical` 中划定活跃根集合。

---

## 1. 00:00 活跃根集合划定

### 负责人

2号位。

### 动作

从 `Order_Canonical（订单标准表）` 中筛选未来计划窗口内的活跃订单和生产指示，形成本次 BOM 展开的活跃根集合。

关键过滤：

```text
只允许 Status = OPEN 的订单进入本轮 BOM Request。CLOSED / CANCELLED 不进入 BOM Request，也不生成 Task / Pegging。

```

### 输出

写入：

```text
MES_API_BOM_RequestMES_API_BOM_Request_Detail

```

### 设计考虑

这一阶段按订单粒度推送 BOM 展开请求，而不是简单按 BOMNO 去重。  
原因是无 BOMNO 订单、生产指示、不同订单上下文，都需要保留订单级追溯。

---

## 2. 00:05 Order 分区表装载

### 负责人

2号位。

### 动作

执行：

```text
sp_SyncOrdersToPartitionTable

```

从：

```text
Order_Canonical（订单标准表）

```

同步到：

```text
Order（订单分区表）

```

### 处理内容

补齐或映射：

- `MaterialId（物料ID）`
  
- `ProductFamilyId（产品族ID）`
  
- `FactoryId（工厂ID）`
  
- `DomainKey（分域标识）`
  
- `PriorityScore（优先级分数）`
  

### 设计考虑

`Order_Canonical` 是标准化后的订单事实，`Order` 是排程业务表。  
排程内核消费的是 `Order`，不是直接消费 ERP 原始订单。

---

## 3. 00:10 主数据同步

### 负责人

2号位。

### 动作

执行主数据同步：

```text
sp_SyncMasterData

```

从 ERP / MES 的标准视图同步到 APS 本地表。

### 输出

```text
Material（物料表）MaterialMapping（物料映射表）MaterialSupplyContext（物料供给上下文表）

```

### 设计考虑

APS 排程不能直接依赖 ERP / MES 的原始字段。  
主数据必须先进入 APS 本地结构，完成编码映射、供给上下文、仓库/工厂/产品族等语义统一。

---

## 4. 00:10 资源主数据同步

### 负责人

2号位。

### 动作

执行：

```text
sp_SyncResourceData

```

从 MES 资源视图同步到：

```text
Resource（资源表）

```

### 设计考虑

资源表是外部设备/产线/班组能力在 APS 中的镜像。  
排程时资源必须已经在本地，不允许排程内核再跨库查询 MES。

---

## 5. 00:15 Routing 工艺路线同步

### 负责人

2号位。

### 动作

从 ODS 工艺视图同步工艺路线相关数据。

### 输出

```text
RoutingOperation（工序节点表）RoutingDependency（工序依赖表）OperationResourceEligibility（工序-资源能力表）RoutingPlanningParam（工艺规划参数表）RoutingStage（工艺阶段表）

```

### 设计考虑

APS 不再使用单一线性 Routing 表，而是采用工艺图模型：

```text
工序节点 + 工序依赖 + 可用资源 + 规划参数

```

这样才能表达并行、前后依赖、替代资源、瓶颈资源等复杂排程逻辑。

---

## 6. 00:20 BOM 批次展开

### 负责人

2号位发起，5号位在 ODS 侧展开和回填。

### 动作

2号位触发 ODS 侧批次 BOM 展开。

5号位负责：

```text
sp_ExpandBOMBatch（BOM递归展开）sp_EnrichBOMWorkset（阶段、工厂、异常回填）

```

### 输出

ODS 侧形成：

```text
MES_APS_BOM_Workset（BOM展开工作集）MES_APS_BOM_Workset_StageDetail（阶段路径明细）MES_APS_BOM_Workset_Issues（BOM问题与降级记录）

```

### 设计考虑

无 BOMNO 订单、生产指示、BOM 入口分流等复杂逻辑，不放在 2号位排程内核中处理，而由 5号位在 Workset 阶段吸收。  
这样可以让 APS 排程主链保持稳定。

---

## 7. 00:30 APS_BOM_RAW 与 APS_BOM_STAGE_PATH_RAW 拉取

### 负责人

2号位。

### 动作

从 ODS 拉取 BOM 展开结果和阶段路径结果到 APS 本地。

### 输出

```text
APS_BOM_RAW（APS本地BOM展开原始表）APS_BOM_STAGE_PATH_RAW（APS本地阶段路径原始表）

```

### 设计考虑

排程内核不能跨库读取 ODS。  
所有 BOM 展开结果必须提前搬运到 APS 本地库。

---

## 8. 00:35 LLC 低阶码计算

### 负责人

2号位。

### 动作

执行：

```text
sp_CalculateLLC

```

计算：

```text
LLC（Low Level Code，低阶码）

```

### 设计考虑

LLC 用于确定物料在多层 BOM 中的层级顺序，避免供需展开、物料扣减、任务生成时出现顺序混乱。

---

## 9. 00:38 ScheduleRun 预创建

### 负责人

3号位 / NightlyBatchOrchestrator。

### 动作

创建：

```text
ScheduleRun（排程运行编排表）

```

写入：

```text
RunType = FULL_SCHEDULE（凌晨全量排程）Status = RUNNING（运行中）BasePlanVersionId = NULLScenarioId = NULLDataCutoffTime = 本次统一数据截止时间StrategyProfileVersionId = 默认 PUBLISHED 策略包版本
TriggeredBy = Hangfire 或 NightlyBatchOrchestrator

```

### 设计考虑

`ScheduleRun` 必须在 00:40 MES 快照同步前创建。  
因为 00:40、00:45、00:50 三个 MES 快照同步 SP 都必须使用同一个：

```text
ScheduleRunId + DataCutoffTime

```

`DataCutoffTime` 一经确定，后续快照同步不能各自取当前时间。  
否则会出现订单、MES进度、库存、管道供给数据不在同一个时间切片的问题。

---

## 10. 00:40 MES 工单快照同步

### 负责人

2号位。

### 动作

执行：

```text
sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime)

```

从：

```text
MES_APS_WorkOrder_View

```

同步到：

```text
MESWorkOrderSnapshot（MES工单快照表）

```

### 设计考虑

记录本次排程可见的 MES 工单关系。  
这些数据用于生产指示号与 MES 工单的追溯，以及后续 WIP / 生产进度判断。

---

## 11. 00:45 MES 工序进度快照同步

### 负责人

2号位。

### 动作

执行：

```text
sp_SyncOperationProgressSnapshot(@ScheduleRunId, @DataCutoffTime)

```

从：

```text
MES_APS_OperationProgress_View

```

同步到：

```text
OperationProgressSnapshot（工序进度快照表）

```

### 设计考虑

工序进度用于判断小工序层面的已报工、待加工、剩余加工状态。  
但它不是库存，不直接混入 `InventoryBalance（库存余额表）`。

---

## 12. 00:50 MES 大工艺进度快照同步

### 负责人

2号位。

### 动作

执行：

```text
sp_SyncStageProgressSnapshot(@ScheduleRunId, @DataCutoffTime)

```

从：

```text
MES_APS_StageProgress_View

```

同步到：

```text
StageProgressSnapshot（大工艺进度快照表）

```

### 设计考虑

大工艺进度用于辅助判断某个生产指示在大阶段上的进展。  
但大工艺是否真正成为可供给，仍要结合库存入库、在途、管道供给等供应事实判断。

---

## 13. 00:55 管道供给同步

### 负责人

2号位。

### 动作

执行：

```text
sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, @RowsAffected OUTPUT, @ErrorMessage OUTPUT)

```

同步跨厂在途、外协在途、采购在途等管道供给数据。

### 输出

```text
SupplyFact_Pipeline（管道供给事实表）

```

### V1 说明

V1 阶段可以是空跑：

```text
SupplyFact_Pipeline = 空ScheduleContext.PipelineSupplies = 空集合

```

空集合是正常结果，不是失败。

### 设计考虑

管道供给不等同于库存。  
它代表未来某个时间点可能到达的供给，不能直接混入当前库存余额。

---

## 14. 01:50 跨域依赖静态扫描

### 负责人

2号位。

### 动作

扫描跨产品族 BOM 依赖，生成：

```text
Domain_Dependency（跨域依赖表）

```

### 设计考虑

3号位在 02:00 启动分域排程时，需要提前知道哪些产品族之间有上下游依赖。  
如果跨域依赖等到排程内核运行时才动态发现，就无法提前做拓扑排序，容易出现调度死循环。

---

## 15. 02:00 全量排程启动

### 负责人

3号位触发，1号位执行排程内核，2号位负责落库。

### 动作一：3号位读取已创建的 ScheduleRun

02:00 不再新建 `ScheduleRun`。

读取 00:38 已创建的：

```text
ScheduleRunId
DataCutoffTime
StrategyProfileVersionId
```

根据 `StrategyProfileVersionId` 加载策略包：

```text
StrategyProfileVersion
  → RuleSetVersion → RuleSet
  → ParameterSetVersion → ParameterSet
```

初始化：

```text
ScheduleContext.RuleConfig
ScheduleContext.SchedulingParams
```

1号位/5号位只消费已装载的规则参数结果，不直接读维护表。

### 动作二：2号位创建 PlanVersion（排程启动时创建 BUILDING 版本壳）

创建：

```text
PlanVersion（计划版本表）

```

写入：

```text
SourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = NULLVersionCategory = DAILY_BASELINE（日排程基线版本）Status = BUILDING（构建中；非 FULL_SCHEDULE 类型默认 CANDIDATE）PlanHorizonStart = 计划开始日期PlanHorizonEnd = 计划结束日期DomainKey = 分域标识BatchNo = 本次批次号

```

注意：  
因为本次最终方案取消了 `ScheduleRun.OutputPlanVersionId`，所以不再从 `ScheduleRun` 回填产出版本。  
关联方向改为：

```text
PlanVersion.SourceScheduleRunId = ScheduleRun.Id

```

### 动作三：初始化 ScheduleContext

在服务器内存中初始化：

```text
ScheduleContext（排程上下文 / 排产沙盘）

```

装载本次排程所需全部数据：

```text
Order（订单）Material（物料）RoutingOperation（工序节点）RoutingDependency（工序依赖）OperationResourceEligibility（工序资源能力）InventoryBalance（库存余额）InventoryAvailableSupplyDetail（可用库存明细）MESWorkOrderSnapshot（MES工单快照）OperationProgressSnapshot（工序进度快照）StageProgressSnapshot（大工艺进度快照）SupplyFact_Pipeline（管道供给，V1可为空）Domain_Dependency（跨域依赖）

```

### 动作四：1号位内存计算

1号位只在内存中计算，不直接写数据库。

内存中产出：

```text
TaskDraft（任务草案）PeggingDraft（供需绑定草案）ExplanationFactDraft（解释事实草案）PlanKpiDraft（计划KPI草案）OrderSummaryDraft（订单摘要草案）ResourceLoadSummaryDraft（资源负荷摘要草案）

```

### 动作五：2号位批量落库

2号位统一批量写入：

```text
Task（任务表）Pegging（供需绑定表）ScheduleExplanationFact（排程解释事实表）PlanKpiSummary（计划KPI汇总表）OrderScheduleSummary（订单排程摘要表）ResourceLoadSummary（资源负荷汇总表）

```

所有结果都通过：

```text
PlanVersionId（计划版本ID）

```

关联到本次 `PlanVersion`。

### 动作六：更新运行状态和版本状态

排程成功：

```text
ScheduleRun.Status = COMPLETED（已完成）ScheduleRun.CompletedAt = 当前时间PlanVersion.Status = ACTIVE（正式采用）PlanVersion.ActivatedAt = 当前时间PlanVersion.ActivatedBy = Hangfire / 系统调度器

```

排程失败：

```text
ScheduleRun.Status = FAILED（失败）ScheduleRun.ErrorMessage = 错误信息PlanVersion.Status = FAILED（失败）

```

---

# 四、其他运行场景下的创建顺序与计算流程

除 FULL_SCHEDULE 外，MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF 创建 ScheduleRun 时，也必须写入 StrategyProfileVersionId。其来源按《数据架构与防腐层设计方案 §2.8.6》执行：人工重排和局部重排默认继承 BasePlanVersion 对应运行的策略包；仿真和插单分析可由 Scenario 指定或继承基准版本策略包。

## 场景一：人工重排 MANUAL_RESCHEDULE

### 业务含义

计划员或管理人员基于当前正式采用计划，手动触发一次重新排程。

### 创建顺序

```text
1. 用户触发人工重排2. 创建 ScheduleRun3. 创建 PlanVersion4. 排程内核计算5. 结果落库6. 保持候选状态7. 人工确认后，将该 PlanVersion.Status 改为 ACTIVE

```

### 详细动作

#### 第一步：创建 ScheduleRun

```text
RunType = MANUAL_RESCHEDULE（人工重排）Status = RUNNING（运行中）BasePlanVersionId = 当前 ACTIVE 的 PlanVersion.IdScenarioId = NULLTriggeredBy = 当前用户

```

人工重排通常不需要 `Scenario`。  
但是必须记录 `BasePlanVersionId（基准计划版本ID）`，否则后续无法说明本次重排是基于哪个版本调整出来的。

#### 第二步：创建 PlanVersion

```text
SourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = NULLStatus = CANDIDATE（候选）VersionCategory = RESCHEDULE_CANDIDATE（重排候选版本）

```

#### 第三步：排程计算与结果落库

和凌晨全量一样：

```text
1号位内存计算2号位批量落库

```

#### 第四步：人工确认后正式采用

如果计划员确认采用该版本：

```text
PlanVersion.Status = ACTIVEPlanVersion.ActivatedAt = 当前时间PlanVersion.ActivatedBy = 当前用户

```

同一范围内原 ACTIVE 版本应改为归档或非当前状态，避免同时存在多个正式采用版本。

---

## 场景二：局部重排 LOCAL_RESCHEDULE

### 业务含义

只对某个范围进行重排，例如某个工厂、某个资源组、某批订单、某个产品族。

### 创建顺序

```text
1. 用户/API触发局部重排2. 创建 ScheduleRun3. 创建 PlanVersion4. 只对指定范围进行排程计算5. 结果落库6. 保持候选状态7. 人工确认后，将该 PlanVersion.Status 改为 ACTIVE

```

### 详细动作

#### 第一步：创建 ScheduleRun

```text
RunType = LOCAL_RESCHEDULE（局部重排）Status = RUNNING（运行中）BasePlanVersionId = 当前 ACTIVE 的 PlanVersion.IdScenarioId = NULLScopeJson = 本次重排范围TriggeredBy = 当前用户或 API

```

`ScopeJson（重排范围JSON）` 记录：

- 哪个工厂；
  
- 哪个资源组；
  
- 哪些订单；
  
- 哪些时间窗口；
  
- 哪些任务允许移动；
  
- 哪些任务必须冻结。
  

#### 第二步：创建 PlanVersion

```text
SourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = NULLStatus = CANDIDATEVersionCategory = LOCAL_RESCHEDULE_CANDIDATE（局部重排候选版本）

```

#### 第三步：计算与落库

排程内核只对范围内对象进行重排。  
结果仍然落到新 `PlanVersion` 下，不能直接覆盖正式采用版本。

#### 第四步：确认采用

如果确认采用：

```text
PlanVersion.Status = ACTIVE

```

---

## 场景三：单方案仿真 SIMULATION

### 业务含义

针对某个假设条件跑一次仿真，并生成一个候选计划版本。

例如：

```text
如果 BJ 工厂今天加班 4 小时，准交率能提升多少？

```

### 创建顺序

```text
1. 创建 Scenario2. 创建 ScheduleRun3. 创建 SimulationRun4. 创建 PlanVersion5. 算法/排程内核计算6. 结果落库7. 生成评分和摘要8. 保持候选状态9. 可选：在 Scenario 中记录选中版本10. 如果业务确认采用，再将 PlanVersion.Status 改为 ACTIVE

```

### 详细动作

#### 第一步：创建 Scenario

```text
ScenarioName = BJ工厂加班4小时仿真ScenarioType = SIMULATION（仿真）AssumptionJson = 加班4小时等假设ObjectiveJson = 准交率、VIP延期、资源负荷等目标Status = RUNNING 或 DRAFT

```

#### 第二步：创建 ScheduleRun

```text
RunType = SIMULATION（仿真）Status = RUNNINGScenarioId = 当前 Scenario.IdBasePlanVersionId = 当前 ACTIVE 的 PlanVersion.IdTriggeredBy = 当前用户或系统

```

#### 第三步：创建 SimulationRun

```text
ScenarioId = 当前 Scenario.IdScheduleRunId = 当前 ScheduleRun.IdAlgorithmType = 规则算法 / 遗传算法 / OR-Tools / 混合算法AlgorithmConfigJson = 算法参数Status = RUNNING

```

#### 第四步：创建 PlanVersion

```text
SourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = 当前 SimulationRun.IdStatus = CANDIDATEVersionCategory = SIMULATION_CANDIDATE（仿真候选版本）

```

#### 第五步：计算、落库、评分

生成：

```text
TaskPeggingScheduleExplanationFactPlanKpiSummaryOrderScheduleSummaryResourceLoadSummaryScenarioObjectiveScore

```

所有结果都挂到本次 `PlanVersionId`。

#### 第六步：选中版本

如果这个场景只有一个候选版本，且业务人员选择它作为方案，可以写：

```text
Scenario.SelectedPlanVersionId = 当前 PlanVersion.IdScenario.Status = SELECTED（已选择）

```

但这仍不代表正式采用。  
正式采用必须另行将该版本状态改为：

```text
PlanVersion.Status = ACTIVE

```

---

## 场景四：一次仿真产生多个候选方案

### 业务含义

一个场景下，算法一次执行产生多套候选方案。

例如：

```text
BJ工厂加班4小时，系统生成三套候选排程方案。

```

### 创建顺序

```text
1. 创建 Scenario2. 创建 ScheduleRun3. 创建 SimulationRun4. 算法在内存中产生多个候选方案5. 每个候选方案创建一条 PlanVersion6. 每个 PlanVersion 各自落 Task / Pegging / Summary / Explanation7. 每个 PlanVersion 各自生成评分8. 如果选中其中一个版本，只写 Scenario.SelectedPlanVersionId9. 其他未选版本不做特殊标记10. 如果业务确认采用，再将被选中的 PlanVersion.Status 改为 ACTIVE

```

### 详细动作

#### 第一步：创建 Scenario

```text
ScenarioName = BJ加班4小时多方案仿真ScenarioType = SIMULATIONAssumptionJson = BJ工厂加班4小时ObjectiveJson = 准交率最大、VIP延期最小、资源负荷均衡

```

#### 第二步：创建 ScheduleRun

```text
RunType = SIMULATIONScenarioId = 当前 Scenario.IdBasePlanVersionId = 当前 ACTIVE 的 PlanVersion.IdStatus = RUNNING

```

#### 第三步：创建 SimulationRun

```text
ScenarioId = 当前 Scenario.IdScheduleRunId = 当前 ScheduleRun.IdAlgorithmType = GA 或 HYBRIDAlgorithmConfigJson = 本次算法参数Status = RUNNING

```

#### 第四步：创建多个 PlanVersion

如果算法生成三个候选方案，则创建三条 `PlanVersion`：

```text
PlanVersion ASourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = 当前 SimulationRun.IdStatus = CANDIDATEPlanVersion BSourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = 当前 SimulationRun.IdStatus = CANDIDATEPlanVersion CSourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = 当前 SimulationRun.IdStatus = CANDIDATE

```

不记录候选排序。  
不记录是否推荐。  
不新增候选关系表。

#### 第五步：分别落库

每个版本独立落：

```text
TaskPeggingPlanKpiSummaryOrderScheduleSummaryResourceLoadSummaryScheduleExplanationFactScenarioObjectiveScore

```

每条数据都通过 `PlanVersionId` 区分属于哪个方案。

#### 第六步：记录选中版本

如果团队最终选中方案 B：

```text
Scenario.SelectedPlanVersionId = PlanVersion B.IdScenario.Status = SELECTED

```

方案 A 和方案 C 不需要特殊处理，继续保持：

```text
PlanVersion.Status = CANDIDATE

```

如果后续要正式采用方案 B，再单独更新：

```text
PlanVersion B.Status = ACTIVEPlanVersion B.ActivatedAt = 当前时间PlanVersion B.ActivatedBy = 当前用户

```

---

## 场景五：插单影响分析 INSERT_ORDER_WHATIF

### 业务含义

有一张急单或插单，需要分析如果插入当前计划，会影响哪些订单、资源和交期。

### 创建顺序

```text
1. 创建 Scenario2. 创建 ScheduleRun3. 可选创建 SimulationRun4. 创建一个或多个 PlanVersion5. 生成插单后的候选排程结果6. 生成影响摘要和风险解释7. 如果选中其中一个版本，记录到 Scenario.SelectedPlanVersionId8. 默认不正式采用9. 如果业务确认采用，再将被选中的 PlanVersion.Status 改为 ACTIVE

```

### 详细动作

#### 第一步：创建 Scenario

```text
ScenarioName = 插单 SO-NEW-001 影响分析ScenarioType = INSERT_ORDER_WHATIF（插单影响分析）AssumptionJson = 新增订单、交期、数量、保护VIP订单等约束ObjectiveJson = 插单交期、VIP订单不延期、资源负荷最小扰动等目标

```

#### 第二步：创建 ScheduleRun

```text
RunType = INSERT_ORDER_WHATIFScenarioId = 当前 Scenario.IdBasePlanVersionId = 当前 ACTIVE 的 PlanVersion.IdStatus = RUNNING

```

#### 第三步：是否创建 SimulationRun

如果只是规则试算，可以不创建 `SimulationRun`。  
如果调用元启发算法、遗传算法、OR-Tools 或多目标优化算法，建议创建 `SimulationRun`。

#### 第四步：创建 PlanVersion

单方案时创建一条。  
多方案时创建多条。

每条都写：

```text
SourceScheduleRunId = 当前 ScheduleRun.IdSourceSimulationRunId = 当前 SimulationRun.Id 或 NULLStatus = CANDIDATEVersionCategory = WHATIF_CANDIDATE（插单分析候选版本）

```

#### 第五步：生成影响分析结果

落库内容包括：

```text
Task：插单后的任务结果Pegging：插单后的供需绑定关系OrderScheduleSummary：哪些订单交期变化ResourceLoadSummary：哪些资源负荷变化PlanKpiSummary：整体KPI变化ScheduleExplanationFact：为什么产生影响ScenarioObjectiveScore：方案评分

```

#### 第六步：选中版本但不自动正式采用

如果选择某个插单方案：

```text
Scenario.SelectedPlanVersionId = 被选中的 PlanVersion.Id

```

但是否正式执行，仍要走人工确认或审批。  
不能因为插单分析完成就自动覆盖正式采用版本。

---

# 五、表之间的关系总览

## 1. 主关系

```text
Scenario（场景表）    ← ScheduleRun.ScenarioId（排程运行关联场景）    ← SimulationRun.ScenarioId（仿真运行关联场景）ScheduleRun（排程运行表）    ← PlanVersion.SourceScheduleRunId（计划版本来源于哪次运行）    ← SimulationRun.ScheduleRunId（仿真运行属于哪次排程运行）SimulationRun（仿真运行表）    ← PlanVersion.SourceSimulationRunId（计划版本来源于哪次仿真运行）PlanVersion（计划版本表）    ← Task.PlanVersionId（任务属于哪个计划版本）    ← Pegging.PlanVersionId（供需绑定属于哪个计划版本）    ← PlanKpiSummary.PlanVersionId（计划KPI属于哪个计划版本）    ← OrderScheduleSummary.PlanVersionId（订单摘要属于哪个计划版本）    ← ResourceLoadSummary.PlanVersionId（资源负荷属于哪个计划版本）    ← ScheduleExplanationFact.PlanVersionId（解释事实属于哪个计划版本）    ← ScenarioObjectiveScore.PlanVersionId（方案评分属于哪个计划版本）Scenario.SelectedPlanVersionId    → PlanVersion.Id（场景最终选中的计划版本）PlanVersion.Status = ACTIVE    → 表示该版本为正式采用版本

```

---

# 六、设计考虑点

## 1. 为什么不在 ScheduleRun 里记录输出版本？

因为一次运行未来可能产生多个候选版本。

如果 `ScheduleRun` 只有一个 `OutputPlanVersionId`，就会出现问题：

```text
一次仿真产生方案 A、方案 B、方案 C。ScheduleRun.OutputPlanVersionId 到底写哪一个？

```

所以改成：

```text
PlanVersion.SourceScheduleRunId 记录来源。

```

这样可以反查：

```text
这次 ScheduleRun 产出了哪些 PlanVersion？

```

## 2. 为什么不在 SimulationRun 里记录唯一 PlanVersionId？

因为一次算法执行也可能产生多个候选版本。

如果 `SimulationRun.PlanVersionId` 只能记录一个版本，多候选方案时仍然会歧义。

所以改成：

```text
PlanVersion.SourceSimulationRunId

```

每个候选版本自己说明：

```text
我是由哪次 SimulationRun 产生的。

```

## 3. 为什么不增加 CandidateRank / IsRecommended？

当前阶段不需要对候选方案排序，也不需要记录推荐标记。

业务上只需要知道：

```text
最后选中了哪个版本。

```

因此：

```text
Scenario.SelectedPlanVersionId

```

已经足够。

其他未选中的候选版本只作为 `CANDIDATE` 版本保留，供追溯和比较。

## 4. 为什么不增加候选版本关系表？

因为当前可以通过 `PlanVersion.SourceScheduleRunId` 和 `PlanVersion.SourceSimulationRunId` 反查候选版本列表。

例如：

```text
查某次仿真运行产生的所有版本：PlanVersion.SourceSimulationRunId = 当前 SimulationRun.Id查某次排程运行产生的所有版本：PlanVersion.SourceScheduleRunId = 当前 ScheduleRun.Id

```

在现阶段，不需要额外增加关系表。

## 5. 为什么 Scenario.SelectedPlanVersionId 不等于正式采用？

因为“选中”只是业务上认为这个方案较好。  
“正式采用”还需要人工确认或审批。

所以三层状态必须分清：

```text
候选版本：PlanVersion.Status = CANDIDATE场景选中版本：Scenario.SelectedPlanVersionId = 某个 PlanVersion.Id正式采用版本：PlanVersion.Status = ACTIVE

```

---

# 七、最终一句话总结

本次最终设计可以概括为：

```text
ScheduleRun 记录这次怎么跑；PlanVersion 记录跑出来的每一套结果；Scenario 记录试算假设和最终选中的版本；SimulationRun 记录算法怎么执行；PlanVersion.Status = ACTIVE 记录哪个版本正式采用。

```

对于多方案仿真：

```text
一次 Scenario    可以有一次或多次 ScheduleRun        可以有一次或多次 SimulationRun            可以产生多条 PlanVersion最终只需要在 Scenario.SelectedPlanVersionId 中记录选中的那条 PlanVersion。正式采用则把该 PlanVersion.Status 改为 ACTIVE。

```