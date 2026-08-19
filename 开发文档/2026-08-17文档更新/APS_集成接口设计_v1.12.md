# APS 集成接口设计规范

**版本**：v1.32  
**日期**：2026-07-31  
**基于**：《APS数据架构与防腐层设计方案 v1.42》+《APS Pegging与跨版本供给分配详细设计方案 v1.1（V1最小实现版）》+《APS_各类基础数据分层承接与演变总表 v3.38》+《APS 核心排产全流程走查 V3.23》+《APS_数据库字段说明文档/DDL v5.2.5》  
**更新**：补齐生产指示位置快照、Pegging AllocationDecision、TaskDraft有限产能输入输出、MES双向视图发布与运行快照、ExecutionLock/HardLock恢复、Candidate按状态恢复等接口契约；收紧2号位/5号位插件与Voucher边界。普通计算返回Result，普通Pegging返回AllocationDecision，只有审批或正式人工状态变更使用Voucher。



**v1.32更新内容**（2026-07-31 1号位纯内存接口和正式Task落库归属澄清）：
- 🔒 `TaskDraftDto / FinalTaskDraftDto / AllocationTaskShareDto`是内部方法参数和返回值，不是数据库实体或表。
- 🔒 2号位从数据库/快照装载全部事实并构造`DomainSolveRequest`；1号位的`SolveAsync`只消费该内存请求并返回`DomainSolveResult`，不得执行任何数据库查询或写入。
- 🔒 1号位返回的`FinalTasks`仍是内存草稿；只有2号位可以在`PersistDomainResultAsync`事务中将其实例化为正式`[Task]`。
- 🔒 1号位实现项目不得依赖`DbContext`、Repository、Dapper、`SqlConnection`或数据库事务组件；解释事实同样只返回Draft，由2号位落库。
- 📝 原“1号位消费查询”措辞统一改为“2号位查询装载后通过接口传入1号位”。

> **v1.32边界声明**：本轮不修改DTO业务字段、算法或数据库结构，只消除“TaskDraft表”和“1号位实例化正式Task”的误解。

**v1.31更新内容**（2026-07-31 1号位接口、SupplyBusinessKey与Domain持久化事务冻结）：
- 🔧 `ERP_Inventory_View.Quantity`契约语义改为扣除`WasterQty`后的APS净可用量，字段集合不变；客户专属仓库V1按整仓排除。
- 🔧 SupplyBusinessKey六类最小格式及Loader生成责任冻结。
- 🔧 `SchedulingOrchestrator`总编排顺序冻结；PeggingOrchestrator改为纯内存，不再写Task、Ledger、PSA或物理Pegging。
- 🔧 AllocationSequence使用每PlanVersion局部计数器，成功双边扣减后生成。
- 🔧 1号位输入TaskDraft Components，输出FinalTasks、AllocationShares和求解摘要；合并拆分数量守恒。
- 🔧 Domain统一事务顺序冻结；PlanVersion激活保持独立事务。
- 🔧 明确`PlanVersionInfoDto.DomainKey`、`OrderLoadDto.OrderCanonicalId`及`SupplyLoadRow`最小字段来源。

> **v1.31边界声明**：不要求固定类名或独立Repository，不新增数据库对象、分布式事务和新外部接口。

**v1.30更新内容**（2026-07-30 现有代码对象映射澄清；不新增接口层级）：
- 🔧 现有`PeggingRuleVoucher`可继续作为5号位分配判断DTO；在本文接口契约中对应`PeggingAllocationDecision`，不强制改名、不新建第二套Decision。
- 🔧 现有`PeggingLedgerEntry`继续作为2号位成功应用分配后的内存记录，逻辑上对应待持久化Ledger；`PeggingLedgerBuffer`可直接保存该对象，不要求新建`PeggingAllocationLedgerDraft`类。
- 🔧 批量持久化时，全部成功Entry进入`PeggingAllocationLedger`；其中非Task供给再生成`PeggingSupplyAllocation`。现有`PeggingSupplyAllocation`保留，不承担全量分配总账职责。
- 🔧 `ExecutionLock`为需要补齐的V1最小领域实体；只实现DDL v5.2.3已定义的字段、关联和状态更新，不新增外部接口、Link表、事件溯源或锁平台。

> **v1.30边界声明**：本轮只调整内部接口说明和代码对象对应关系，不修改MES/ERP外部契约，不改变Pegging计算、TaskDraft接口、Candidate、HardLock或数据库结构。

**v1.29更新内容**（2026-07-30 定点补丁：MES快照独立定时服务与执行数量语义）：
- 🔧 `INightlyBatchOrchestrator`只创建`ScheduleRun`和冻结截止时间，不调用MES快照同步；三类快照由三个独立定时Job分别调用既有同步SP。
- 🔧 `ISchedulingOrchestrator`只校验快照完整性并装载`ScheduleContext`，不在PI位置计算、Pegging或有限产能排程期间读取MES实时视图。
- 🔧 撤销人工短量差额字段；小工序状态4不改变PI总剩余量。小工序剩余Task以`OperationProgressSnapshot`为权威，ExecutionLock剩余量只表示当前现实工单未来Stage产出承诺。

> **v1.29覆盖声明**：本轮只澄清定时服务边界和执行数量语义，不新增MES接口，不修改双向视图发布方式。


**v1.28更新内容**（2026-07-29 第三轮定点修订：MES双向视图集成与审计闭环）：
- 🔄 APS→MES改为拉取式发布：新增`MESPlanRelease`与`APS_MES_PlanRelease_View`，不再定义APS主动REST下发、逐项回执、UNKNOWN重试或取消接口。
- 🆕 `ReleaseItemKey`为跨系统唯一稳定幂等键；MES原样保存并在`MES_APS_WorkOrder_View`回传。`TaskNo`仅展示诊断，一条发布单元可关联同PI、同Stage下多个小工序Task；发布数量取Stage级执行批次单一流转量，不对关联Task数量求和。
- 🔄 MES→APS统一读取实时权威视图并按`ScheduleRun.DataCutoffTime`形成`MESWorkOrderSnapshot / OperationProgressSnapshot / StageProgressSnapshot`三类本地运行快照；不建设MQ事件累计、REST备用轮询或`MES_Actual_Staging`事件链。
- 🔄 `PLANNED→RELEASED`触发点改为：MES工单视图确认ReleaseItemKey已建单后，2号位将MESPlanRelease转CONSUMED、创建ExecutionLock并更新相关Task。
- ⚠️ ExecutionLock人工差额数量已由v1.29撤销；HardLock允许部分ReleasedQty，剩余量继续ACTIVE。
- 🔄 Candidate以Base供给切片保护库存/在途，但按Candidate DataCutoffTime读取最新MES累计事实并形成独立执行快照；激活前比较现实事实版本戳。
- 📦 APS_Auth由独立脚本`APS_Auth数据库DDL_v1.0.sql`部署，不属于APS_Production主DDL。

> **v1.28覆盖声明**：下方历史版本中的MQ/REST实绩、主动计划下发、DispatchRequestId/DispatchItemKey、逐项回执和主动取消接口均只作历史追溯；三类MES运行快照继续是现行主链。当前开发、测试和联调一律执行v1.28第三章的双向视图契约。

**v1.27更新内容**（2026-07-29 六份文档全局审计修正）：

> ⚠️ 本节记录的是v1.27主动下发模式的历史修订，已被v1.28双向视图模式整体覆盖，不得作为当前接口实现依据。
- 🔧 MES下发DTO与Task物理字段对齐：使用`Task.ProductionInstructionNo + Task.StageCode`，不读取不存在的`Task.MESWorkOrderNo`；跨版本去重以`ExecutionLockId + 正式出站台账`为准。
- 🔧 V1逐项接受要求`acceptedQuantity == requestedQuantity`；部分接受不创建整张Task的ExecutionLock，须由APS重新拆分。
- 🔧 Pipeline Loader当前正文改为正式读取准确BatchNo切片，不再保留“V1固定空集合”实现分支。
- 🔧 ComponentShares通过`AllocationSequence + TaskComponentQty`回填；`AllocatedQty`不再被误作Task需求份额。
- 🔧 HardLock创建增加同一SupplyBusinessKey累计剩余锁量校验；ExecutionLock/HardLock状态更新遵循v5.2.1生命周期约束。

**v1.26更新内容**（2026-07-29 Pegging软硬归属、跨版本执行事实及内部契约闭环）：

> ⚠️ 本节中涉及MQ/REST、逐项回执、主动取消和出站台账的内容均为历史口径；Pegging、TaskDraft、ExecutionLock/HardLock等非MES传输部分仍由v1.28正文继承。
- 🆕 **§3.2.1 MES实绩契约升级**：`MESWorkOrderNo`为现实执行稳定锚点；`apsTaskNo`仅为当次下发映射/诊断字段，不作为跨版本主键。事件优先携带`eventId + workOrderNo + reportStatus(0-4) + cumulativeCompletedQty`；MES不能透出PI/Stage时，APS必须通过正式出站台账反查，不得依赖旧TaskId。
- 🆕 **§3.2.3 MES拉取计划发布视图逐项回执**：历史上拟使用`DispatchRequestId / DispatchItemKey`幂等键；MES逐Task返回`accepted/rejected + mesWorkOrderNo`。只有接受成功并获得稳定工单锚点后，APS才在同一事务中创建/更新`ExecutionLock`、写`Task.ExecutionLockId`并迁移`Task.Status=RELEASED`。虚拟Task禁止进入请求。
- 🆕 **§3.2.3a MES取消事实视图反馈**：APS人工取消须审批后发起；只有MES明确回执`PARTIAL_CANCELLED/CANCELLED`后，才更新ExecutionLock取消量并释放未消耗剩余量。网络超时或回执不确定不得先释放。
- 🔄 **§3.3.3 下发服务重写**：下发前检查MES出站台账和ExecutionLock；新PlanVersion中已关联同一ExecutionLock的Task只建立版本表示，不重复下发。批次部分成功按Task逐项事务处理，不以整个批次一个成功标志覆盖。
- 🆕 **§十二 Pegging与跨版本执行内部契约**：定义PI快照构建、位置计算Result、HardLock判断Result、`PeggingAllocationDecision`、2号位原子扣减、`TaskDraft → ScheduledTaskDraft + ComponentShares`、批量持久化、ExecutionLock更新及Candidate供给状态恢复的接口边界。
- 🔄 **Candidate剩余供给接口重构**：废止“Base原始供给－Base全部已分配”；按实际消耗、HardLock、ExecutionLock剩余投入、ExecutionLock未来产出、Scope外Soft、Scope内Soft及未分配供给分类恢复；激活前重新校验DataCutoffTime后的执行事实变化。
- 🔄 **插件/Voucher边界瘦身**：V1不建设动态热插拔平台；规则变化优先参数化。5号位普通计算返回只读Result，供需匹配返回`PeggingAllocationDecision`；夜间/白天普通Pegging不再使用`PeggingVoucher`。Voucher仅保留人工冻结、容差关闭、HardLock创建/解除等需审批或正式人工状态变更的场景。
- 🔄 **Task生成时机统一**：2号位在Pegging阶段只生成`LogicalBlock / TaskDraft`；1号位返回`ScheduledTaskDraft + ComponentShares + ExplanationFactDraft`；2号位随后批量持久化正式Task、Ledger最终映射和物理Pegging。
- 🔒 **跨版本红线**：Task和物理Pegging随PlanVersion重建；ExecutionLock、HardLock、MES工单锚点及已消耗事实跨版本延续。一个正式Task最多关联一个ExecutionLock；不同现实MES工单不得伪装合并。
- 📌 **DDL落地边界**：本版定义`ExecutionLock / DemandSupplyHardLock / PeggingAllocationLedger / ProductionInstructionSupplySnapshot / ProductionInstructionPositionSlice`及`Task.ExecutionLockId / IsVirtual`的应用层契约；物理表、字段、索引和约束由后续DDL与字段说明同步，不在接口文档中虚构已落地状态。

> **v1.26覆盖声明**：正文中若仍有“2号位先实例化正式Task再由1号位排定”“PipelineSupplies固定为空”“普通Pegging必须生成PeggingVoucher”“workOrderNo等同TaskNo”等历史表述，均以v1.26新增/修订章节为准。历史版本说明仅供追溯。

**v1.25更新内容**（2026-07-20 对齐《APS V1 最终决策》0号位确认）：
- 🔒 **§11.2 ScopeJson 保持 11 字段固定 Schema**：明确禁止新增 `ExpectedDomainKeys`（预期域集合不得塞入 ScopeJson）；§11.2 仅适用于白天实时评估与 Candidate 运行，FULL_SCHEDULE 不要求填写白天 Purpose（ScopeJson 可为 NULL）
- 🆕 **§11.7 ExpectedDomainKeysJson 写入规则（创建 ScheduleRun 服务）**：接口 DTO 新增独立属性 `IReadOnlyList<string> ExpectedDomainKeys`（不并入 ScopeJson DTO）；FULL_SCHEDULE 生成多元素 JSON 数组（≥1 不重复，ScopeJson 可 NULL）；白天 Candidate 由 `BasePlanVersion.DomainKey` 生成单元素数组并校验与 CandidatePlanVersion.DomainKey 一致；SIMULATION 阶段二骨架使用独立 ExpectedDomainKeysJson
- 🔄 **§3.4.1 / §3.2.1 暂停残留清理**：删除 `SuspendedAt` / `SuspendReason` / `ResumedAt` / `TaskPauseVoucher` / `TaskResumeVoucher` 旧口径（DDL 中 Task 表确无上述字段）；PAUSE/RESUME 仍标「V1 不启用 / 不改变 Task 状态」，仅登记日志
- 🔒 **§11.8 权威 ReasonCode 字典**：统一为 15 个取码；`DUE_DATE_VIOLATION`→`DUE_DATE_RISK`；删除未登记示例 `DUE_DATE_TIGHT` / `UPSTREAM_DELAY`（上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`，ObjectType=DOMAIN）；ScheduleExplanationFact 故障事实 `RESOURCE_BREAKDOWN` / `RESOURCE_REPAIRED` 统一为 `EQUIPMENT_BREAKDOWN_RISK`
- 🗑️ **删除旧口径**：`ALL_OR_NOTHING`、单全局 PlanVersion、Task 正式状态含 PAUSED/SUSPENDED/WAITING/PENDING/RUNNING 等（历史"已废止"说明保留并标注）

**v1.24更新内容**（2026-07-13 白天实时评估接口契约）：
- 🆕 **§十一 白天实时评估接口契约**：完整定义 7 个内部服务接口签名
  - `CreateRealtimeEvaluationRunAsync(RealtimeEvaluationRunRequest)`
  - `PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)` — **只三个参数**，目标订单来自 `ScopeJson.OrderCanonicalIds`
  - `EnsureRealtimeBomReadyAsync(requestDetailId)`
  - `PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)`
  - `BuildRemainingSupplyContextAsync(candidatePlanVersionId)`
  - `BuildScheduleContextAsync(candidatePlanVersionId)`
  - `ActivateCandidatePlanVersionAsync(candidatePlanVersionId)` — 内部硬校验；CTP 与 INSERT_IMPACT_ANALYSIS 永远不得激活
- 🆕 **§11.2 ScopeJson 唯一权威 Schema**：**11 字段**固定 Schema + 类型 + 冲突拒绝 + 不得静默扩大 Scope
- 🆕 **§11.3 READY 查询规范**：`SELECT TOP (1) ... WHERE RequestDetailId = @RequestDetailId ORDER BY Id DESC`；`Status='READY'` 为唯一权威；`ExpandedRowCount` 仅诊断；三张 Realtime 表允许 0 行；`CrossFactoryEdge_Realtime` 为 0 行合法
- 🆕 **§11.4 完整白天实时主链**：人工发起 → ScheduleRun → CANDIDATE PlanVersion → Candidate Order 快照 → RequestDetail 实时 BOM → 三 Realtime → READY → 三 RAW → OrderBomRequestLink → RemainingSupply → ScheduleContext → 2号位构造内存TaskDraft/ShippingTaskDraft → 通过接口调用1号位纯内存有限产能排定 → 2号位实例化并持久化正式Task/ShippingTask
- 🆕 **§11.5 异常/变化处理正式模式（修正）**：异常/变化评估主链（事件/变化事实 → ImpactAssessment → ScheduleExplanationFact（适用时）+ RescheduleRecommendation → 看板告警/推荐清单 → PMC/0号位决定，**不要求 Voucher**）；Voucher 仅保留用于需正式执行状态/分配变更的场景（ToleranceClosureVoucher / PeggingVoucher / ManualFreezeVoucher / FreezeZoneVoucher），**不删 Voucher 架构**、设备故障不生成暂停 Voucher
- 🆕 **§11.6 1–5 号位职责表** + 五种 RunType+Purpose 合法组合激活边界
- 🔄 **§5.2 ApprovalCallback**：按 `RequestType` 分流（Candidate 激活 / Voucher 审批 / 可选运行前审批扩展），不再无条件执行**旧自动重排调用**
- 🔄 **§3.4.3 HandleResourceBreakdownAsync**：保留资源不可用事实（`Resource.Status="DOWN"`）；**删除**自动暂停 Task、删除 `TaskPauseVoucher`、删除自动重排；改为 `ResourceEventDto → ImpactAssessment → ScheduleExplanationFact(资源故障风险事实) → RescheduleRecommendation → 看板告警 → PMC 推荐清单`（**绝不**把 Task 改为 PAUSED/SUSPENDED）
- 🔄 **§3.4.3 HandleResourceRepairedAsync**：保留资源可用事实（`Resource.Status="AVAILABLE"`）；**删除** `TaskResumeVoucher`、删除自动重排；改为 `ResourceEventDto → ImpactAssessment → ScheduleExplanationFact(资源恢复风险事实) → RescheduleRecommendation → 看板告警 → PMC 推荐清单`（**绝不**自动恢复 Task）
- 🗑️ **§3.4.3 删除 V1 TaskPauseVoucher / TaskResumeVoucher 正式流程**：清除 PAUSE→PAUSED、RESUME→RUNNING 旧口径；资源故障不再经 Voucher 改 Task 状态
- 🔄 **§3.2 MES 工序报工五态契约（新增）**：MES 生产进度权威输入改为「工序报工 0-4 状态」，新增统一映射表；`Task.Status` 正式值域**仅** `PLANNED / RELEASED / IN_PROGRESS / COMPLETED / CANCELLED`（无 PAUSED/SUSPENDED/WAITING/PENDING/RUNNING）
- 🔄 **§3.2.1 eventType 兼容口径**：保留通用 `eventType` 字段（`START`/`COMPLETE`/`SCRAP` 及 `RESOURCE_BREAKDOWN`/`RESOURCE_REPAIRED` 仍启用）；`PAUSE`/`RESUME` 标为 **V1 不启用 / 不改变 Task 状态**，收到仅登记日志/诊断，不得写 PAUSED/SUSPENDED
- 🔄 **§3.4.1 删除 PAUSE/RESUME 致 Task 状态变更示例**：移除 `RecordTaskPauseAsync`/`RecordTaskResumeAsync` 调用；`PAUSE`/`RESUME` 事件仅 `_logger` 登记，不进入任何 Voucher/状态流转
- 🔄 **§8.1 接口清单**：追加 DDL v5.1.0 新增对象与 §11 定义的 7 个内部服务
- 📌 **MESWorkOrderSnapshot/OperationProgressSnapshot/StageProgressSnapshot 保护**：保留表名和既有暂存主链（INSERT/MERGE SQL、`_dbContext.MESWorkOrderSnapshot/OperationProgressSnapshot/StageProgressSnapshot` 查询）；资源故障/修复处理方法消费 `ResourceEventDto`（从暂存行已有字段派生，不新增 DDL 列）

**v1.23更新内容**（2026-06-23 跨厂Pegging补强）：
- 🆕 **MES_ProcessCode_View.ERPProperty**：透出仓库/工序位置业务属性（M/XC/ZP/BP），来源于ERP真实属性
- 🆕 **ERP_Received_ByDocument_View / ext 包装视图**：ERP Received 按单据汇总接口（粒度=工厂+仓库+物料+单据类型+单据号）
- 🆕 **MES_APS_BOM_Workset_CrossFactoryEdge / APS_BOM_CROSS_FACTORY_EDGE_RAW**：BOM跨厂交接边表接口
- 📌 **PeggingSupplyAllocation**：APS 内部结果表，非外部接口，为内部数据消费边界；记录已确认可用的非Task供给分配细账
- 📌 **物理 Pegging 表**：仅记录 Task-to-Task 供需血缘，不承接库存/在途/Received 等非Task供给

**v1.21更新内容**（2026-06-23 四表职责收敛）：
- 🔄 **§1.3 ScheduleRun**：删除 OutputPlanVersionId，新增 BasePlanVersionId + ScopeJson；产出版本通过 PlanVersion.SourceScheduleRunId 反查
- 🔄 **PlanVersion**：VersionType→VersionCategory；新增 SourceScheduleRunId/SourceSimulationRunId/ActivatedAt/ActivatedBy
- 🔄 **Scenario**：Name→ScenarioName；RunType→ScenarioType；新增 ObjectiveJson/SelectedPlanVersionId/Status/UpdatedAt
- 🔄 **SimulationRun**：删除 PlanVersionId；新增 AlgorithmType/AlgorithmConfigJson/Status/ErrorMessage

**v1.19更新内容**（2026-06-15 管道供给链路完整骨架 + ODS契约视图14字段升级 + 分层语义修正，对齐 DDL v5.0.42 / 防腐层 v1.31 / 字段说明 v5.0.42 / 演变总表 v3.27）：

- 🔄 **§2.1.6 管道供给契约视图全面升级**：
  - ODS 层 `ERP_InterplantInTransit_View` 升级为14字段完整契约（新增 `MasterID`/`SourceFactoryCode`/`SourceDocumentLineNo`/`SourceUpdatedAt`）
  - 分层表述修正：ODS 视图=「ODS层/MES_Integration/来源ERP/5号位」；APS 包装视图=「APS层/APS_Production/2号位」
  - 新增字段语义红线：`FactoryCode`=目的工厂，`Quantity`=剩余在途数量，`MasterID`=物料映射主字段
  - 新增 APS 包装视图 `ext_ERP_InterplantInTransit_View`（显式列字段，禁止 SELECT *）
  - 新增契约锁定规则：V1.1/V2 允许调整视图内部实现逻辑，对外14字段投影不变
- 🔄 **§8.1 接口清单**：`ERP_InterplantInTransit_View` 类型修正为「ODS契约视图」，负责人修正为「5号位（ODS实现）」
  - `ext_ERP_InterplantInTransit_View` 类型修正为「APS跨库包装视图」，负责人修正为「2号位」
- 📌 **V1 空链路声明**：V1 返回 0 行，`ScheduleContext.PipelineSupplies` 为空集合

**v1.18更新内容**（2026-05-14 BOM防腐层物化边表架构调整，对齐 DDL v5.0.26 / 防腐层 v1.21）：
- ⚠️ **§1.3 Socket 更新**：`MES_BOM_Edge_Active`（物化边表）为 V1 正式 BOM 防腐合同层 Socket；`MES_BOM_View` v5.0.26 降为兼容视图
- ⭐ **§1.3 Plug 更新**：5号位新增 `sp_RefreshBOMEdgeActive`；StageDetail 全变体新增 `WorksetId`；`sp_CleanupBOMWorkset` 改按 WorksetId 清理
- ⭐ **§2.1.2 BOM视图设计说明升级**：补 `MES_BOM_Edge_Active` / WHILE迭代展开 / RefreshLog前置校验 / RequestDetailId投传记录
- 📌 **版本引用**：防腐层 v1.20 → v1.21；字段说明 v5.0.25 → v5.0.26；DDL v5.0.25 → v5.0.26

**v1.17更新内容**（2026-05-13 版本引用对齐 DDL v5.0.25 / 防腐层 v1.20）：
- 📌 **版本引用**：防腐层 v1.19 → v1.20；字段说明 v5.0.24 → v5.0.25；DDL v5.0.24 → v5.0.25
- 📌 **无结构性新增**：Batch 3 新增表（ScheduleRun / ScheduleExplanationFact / Summary三张表 / 阶段二骨架三张表）均为 APS 内部对象，不跨越 ODS 边界，不影响外部集成契约

**v1.16更新内容**（2026-05-13 OrderType重构+衍生字段澄清+Routing SourceSystem，对齐 DDL v5.0.24）：
- 🔄 **§2.1.1 v_APS_SalesOrder**：`OrderType` 注释更新（此层为ERP原始值，APS标准化在SP执行）；明确新枚举值映射 `SO/MTO → SALES_ORDER`，`MTS/SS/SS_U → PRODUCTION_INSTRUCTION`
- 🔄 **§2.1.3 视图1 RoutingOperation**：补 `SourceSystem`（追溯增强字段，非运行必需，`'MES'`；与 MES_BOM_View 模式对齐）
- 🔄 **§2.1.3 视图2 RoutingDependency**：补 `SourceSystem`（追溯增强字段）
- 📌 **设计决策写死**：`DemandMaturityStatus` 收窄为 `PRE_CONFIRMED/FORECAST`；`DELAYED` 已拆出为独立字段 `DelayStatus`，视图层无需感知（APS内部衍生）
- 📌 **设计决策写死**：`CustomerSegment` 由 `sp_ValidateAndPromoteOrders` 通过 `CustomerCodeMap` 推导，ODS视图不需要加列

**v1.15更新内容**（2026-05-09 管道供给链，对齐 DDL v5.0.23）：
- 🆕 §2.1.6 新增 `ERP_InterplantInTransit_View` 契约视图字段口径（当前管道供给链唯一 ODS 来源）
- 🆕 §8.1 接口清单新增 `ERP_InterplantInTransit_View` / `ext_ERP_InterplantInTransit_View` 行
- 🆕 §1.3 Socket-Plug Loader 清单新增 `LoadPipelineSupplies()` 方法签名
- 📌 **设计决策写死**：`InventoryBalance` 定义不变；管道供给链是独立并行链，结果为空不影响现有排程

---

**v1.14更新内容**（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21）：
- ✅ §2.1.1 v_APS_SalesOrder：`BOMNO` 字段说明改可空（v5.0.21废除"必填"要求，NULL=待5号位解析）
- ✅ §2.1.2 BOM双层结果：补 `RequestDetailId` 追溯锚点说明 + `MES_API_BOM_Request_Detail` 新结构说明
- ✅ §2.3.1 WriteToStagingAsync：BOMNO注释改可空；补 `sp_ValidateAndPromoteOrders` 写入 `FailureCode`/`NextActionCode` 双维度说明
- 📌 **设计决策写死**：`FailureCode`=原因维度，`NextActionCode`=动作维度，两个独立字段，禁止混用；BOM入口解析分流在5号位Workset阶段执行

**v1.13更新内容**（2026-05-04 BOM 回填 SP 完整实现，对齐 DDL v5.0.18 / 防腐层 v1.16）：
- ✅ §BOM 双层结果说明：“5号位后置回填”升级为明确 `sp_EnrichBOMWorkset` SP 名称，补充 `ChildRequiredFactory` + Issues 降级登记
- ✅ 修正“回填完成后批次才标记READY”→“永不阻塞批次”（对齐 v5.0.11 决策）
- ✅ 相关权威文档引用版本升级

---

**v1.12更新内容**（2026-04-29 生产部门主链 + ProcessCodeDict 重定位 + WorkshopCode 全局清理）：

### 契约视图字段升级（4 + 1 个视图）

| 契约视图 | 字段变更 | 受影响小节 |
|---|---|---|
| `MES_APS_Resource_View` | DROP `WorkshopCode` + ADD **`ProductionDeptCode`** | §2.1.4 视图5 + §8.1 接口清单 |
| `MES_APS_Routing_Operation_View` | ADD **`ProductionDeptCode`** | §2.1.3 视图1 |
| `MES_APS_Routing_Dependency_View` | ADD **`ProductionDeptCode`** | §2.1.3 视图2 |
| `APS_OperationResourceEligibility_View` | ADD **`ProductionDeptCode`** | §2.1.3 视图3 |
| `MES_ProcessCode_View` | ADD **`StageCode`**（APS 增强列）+ RENAME `SourceSystem` → **`CodeOrigin`** | §2.1.5 视图（ProcessCode 契约） |

**字段契约升级流程**：DBA 提交契约视图变更 PR → 0 号位审批 → 上线；任何下游消费方代码零改动（`ext_*` 跨库包装视图自动透传）。

### 新增同步 SP / 新增配置接口

| 接口 | 负责人 | 说明 |
|---|---|---|
| `sp_RebuildMaterialStageDeptContext(@TriggerMode, @BatchNo, ...)` 🆕 | 2 号位 | ⚠️ **占位骨架，当前未实现**（DDL Step1~6 全 TODO，v1 仅有签名与日志骨架）。设计意图：三触发 `FULL` / `INCR` / `PARTIAL`；产出 `MaterialStageDeptContext` + `MaterialStageDeptContext_Issues`。实装前下游消费方调用应做空表/降级处理 |
| `sp_SyncResourceData` 升级 | 2 号位 | MERGE 逻辑加 `ProductionDepartment` 双字典映射 JOIN（FactoryCode + ProductionDeptCode 任一未命中即跳过） |
| `MaterialStageDeptOverride` 维护 API 🆕 | 0 号位 / 业务 | UI/SQL 入口；导入时做 Model → MaterialCode 1:1 检查；1:N 拒收并返回明细 |
| `ProductionDepartment` 字典 CRUD 🆕 | 0 号位审批 + 业务 | 建/改部门记录走审批；1:1 归属 StageCode（不允许多阶段） |
| `ProcessCodeDict` 字典 CRUD 🔄 | APS 系统管理员 + 0 号位审批 | v5.0.16 翻转：取消 `sp_SyncMasterData(@SourceType='ProcessCode')` 自动同步分支；改为 UI/SQL 人工维护 |

### 1 号位排程消费契约定调

```text
排程主链（v5.0.16 红线 #15）：
  FROM StageDetail              -- 5 号位派生（已存在）
    取 (MaterialId, StageCode)
  →  MaterialStageDeptContext   -- 2 号位 sp_RebuildMaterialStageDeptContext 产出（v5.0.16 新增）
    按 (MaterialId, StageCode) 唯一索引取 DefaultProductionDepartmentId
  →  RoutingOperation / RoutingDependency / OperationResourceEligibility  -- 三件套
    按 (MaterialId, ProductionDepartmentId, StageCode) 三元组锁定（v5.0.16 唯一键升级）
    → 生成 Task（小工序 / 依赖图 / 资源能力）
```

**1 号位消费红线**：
- ❌ 1 号位**禁止直接读** `MaterialSupplyContext` / `ProcessCodeDict` / `MaterialStageDeptOverride`（这些是 2 号位组装 Context 的输入源，不是 1 号位入口）
- ❌ 1 号位**禁止跳过 Context** 直查 Routing 三件套（必须先经 `MaterialStageDeptContext` 锁部门）

### EAM 扩展路径预留（不变）

未来 EAM 上线时：
- 并行新增 `EAM_APS_Resource_View`（同构契约，含 `ProductionDeptCode`）
- `sp_SyncResourceData(@SourceType='EAM')` 实现 NOT_IMPLEMENTED 分支
- 双字典映射逻辑零分叉

**相关权威文档（v1.12 基线，2026-04-29）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.18**（含 sp_EnrichBOMWorkset / sp_EnrichBOMWorksetRealtime 完整实现）
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.18**
- `APS_数据架构与防腐层设计方案_v5.0.md` → **v1.16**
- `APS_资源与工艺数据模型重设计方案_v5.0.md` → **v5.2**
- `APS_各类基础数据分层承接与演变总表_v5.0.md` → **v3.12**

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

**v1.11更新内容**（2026-04-25 资源 ODS 契约视图命名统一 + sp_SyncResourceData 占位 SP）：
- ✅ §1.3 Socket-Plug + Loader 清单：`ext_APS_Resource_View` → `ext_MES_APS_Resource_View`；新增执行体 `sp_SyncResourceData(@SourceType)`（DDL v5.0.13）
- ✅ §8.1 接口清单：`APS_Resource_View` 条目重命名为 `MES_APS_Resource_View`（与 MES_APS_Routing_*_View 对齐）
- ✅ EAM 扩展路径明确：未来 EAM 上线时并行新增 `EAM_APS_Resource_View` + `ext_EAM_APS_Resource_View`（双源同构契约零分叉）
- 【设计决策】ODS 契约视图命名永久统一为“源系统_消费方_实体_View”三段式（单源可省略消费方）

**v1.10更新内容**（2026-04-25 工艺数据三层模型收敛）：
- ✅ §2.1.3 视图1 RoutingOperation：`ProcessType` 注释改为"辅助分类标签，不参与排程对接"；`StageCode` 注释强化"BOM↔Routing 对接主键之二，必须取自 `StageDict`"
- ✅ §2.1.3 视图4 RoutingStage：**删除 `StageSeq` 字段**；定位收敛为"该物料在哪些大工艺阶段存在配置"字典；顺序权威唯一信 `StageDetail.StageSeq`
- ✅ §2.1.3 新增"三层模型硬红线"小节：OperationCode/OperationName ≠ ProcessType ≠ StageCode
- ✅ §2.1.3 新增"BOM↔Routing 对接主键（业务 vs 物理）"小节：业务 `(MaterialCode, StageCode)` / 物理 `(MaterialId, StageCode)` 等价
- ✅ §2.1.3 `ProcessType` 值域配置化：新增 `ProcessTypeDict`（骨架 `IsActive=0`）说明；停用硬编码 `MACHINING/ASSEMBLY/INSPECTION/MANUAL` 的表述
- ✅ §2.1.2 BOM视图：StageDetail.StageCode **R20 目标工厂视角**约定（父件 TJ + R20 指派 BJ → 直接写 `BJ_MACH`）
- 【设计决策】OperationCode 不全局字典化（MES 不可控 + 新增频繁；跨厂对接靠 StageCode 足够）

**v1.9更新内容**（2026-04-15 StageDetail升级为统一阶段路径结果表，支持ROOT）：
- ✅ §1.3 Socket-Plug：5号位职责补StageDetail写入；2号位Loader补APS_BOM_STAGE_PATH_RAW搬运；RoutingStage标注阶段字典定位
- ✅ §2.1.2 BOM视图：StageDetail说明升级为统一阶段路径（EDGE+ROOT），补ROOT推导规则和1号位查询约定
- 【设计决策】ROOT记录：ParentMaterialCode=NULL，IsSupplyThreshold恒为0
- 【设计决策】1号位消费查询必须显式按StageScopeType区分

**v1.8更新内容**（2026-04-13 BOM双层结果+阶段提前期参数化，基于《BOM阶段顺序与Workset双层结果设计建议_v1.0》）：
- ▸ 本版替代v1.7，方案升级：单一StageHintCode → 3原始辅助字段 + StageDetail双层结果
- ✅ §1.3 Socket-Plug：`MES_BOM_View` 加列改为 `ParentProcRefCode` + `ChildProcRefCode` + `ChildSourceHintCode`
- ✅ §2.1.2 BOM视图：字段替换 + StageDetail双层结果说明
- ✅ §2.1.3 工艺路线视图：视图4「大工艺阶段」定位调整为阶段字典
- ℹ️ 【设计决策】ChildRequiredStageCode=NULL时按保守策略：子件必须全工艺完成后才可供给父件
- ℹ️ 【设计决策】RoutingStage=阶段字典，StageDetail=5号位派生结果，职责分离
- ℹ️ 【V1不建】BomProcessBinding / StageHintMapping 留V2

**v1.6更新内容**（2026-04-09 客户分级字段）：
- ℹ️ CustomerTier为APS衍生字段（VIP/KEY_ACCOUNT/STANDARD/GENERAL），由sp_ValidateAndPromoteOrders推导
- ℹ️ 视图无需加列（输入信号已在CustomerCode/RawData中），ERPOrderSyncService MERGE无需改动

**v1.5更新内容**（2026-04-09 订单ETL v1.2增补，基于《仅1.2增补内容v1.0》）：
- ✅ §2.1.2 v_APS_SalesOrder视图加列：+IssueDate/OriginalDueDate/ReceivedQty
- ✅ §2.1.2 字段说明补充：IssueDate/OriginalDueDate/ReceivedQty说明
- ✅ §2.3.1 ERPOrderSyncService WriteToStagingAsync MERGE语句补新3字段

**v1.4更新内容**（2026-04-09 订单业务字段补充，基于《订单ETL补充字段设计建议v1.1》）：
- ✅ §2.1.2 v_APS_SalesOrder视图加列：+TransportMode/CustomerName（源事实）、MTS补InstructionNo（≠OrderNo）
- ✅ §2.1.2 字段说明补充：业务澄清OrderType/FactoryCode为APS衍生字段，TransportMode/CustomerName/InstructionNo说明
- ✅ §2.3.1 ERPOrderSyncService WriteToStagingAsync MERGE语句补新字段

**v1.3更新内容**（2026-04-08 集成接口审计修正）：
- ✅ §1.1 ERP同步频率修正：5分钟/实时 → 每小时/每日凌晨，接口方式 CDC → 时间戳轮询
- ✅ §2.2 CDC整节废弃，改为时间戳增量轮询机制说明，补ODS侧ext_v_APS_SalesOrder包装视图
- ✅ §2.3.1 ERPOrderSyncService重写：CDC→时间戳轮询、直连ERP→ODS ext_视图、EF Core→Dapper
- ✅ §2.3.2 承诺交期回写SQL：PostgreSQL ON CONFLICT → SQL Server MERGE
- ✅ 章节编号修正：两个"四"→四/五依次递增至八
- ✅ §3.3 MES Adapter全部代码EF Core→Dapper（MQ消费者/REST轮询/计划下发/工艺路线同步）
- ✅ §4.2 采购Adapter代码EF Core→Dapper
- ✅ §5.2 OA Adapter代码EF Core→Dapper（含事务写法）
- ✅ §8.1 接口清单移除CDC配置行

**v1.2更新内容**（2026-04-03 订单链路审计）：
- ✅ §2.3.1 ERPOrderSyncService全面对齐：SyncStatus "NEW"→"PENDING"、补BOMNO/SourceSystem/SourceMasterID字段、增量频率改每小时、加sp_ValidateAndPromoteOrders调用、方法重命名WriteToStagingAsync
- ✅ v_APS_SalesOrder视图补BOMNO/SourceSystem/SourceMasterID/DueDate字段，补MTS UNION ALL来源说明

**v1.1更新内容**（2026-03-19）：
- 补充Socket-Plug集成模式、更新ERP/MES集成视图定义、明确职责分工

---

**相关文档**：
- **《APS_各类基础数据分层承接与演变总表_v5.0》**（当前 v3.34）：数据演进全景图
- **《APS_数据架构与防腐层设计方案_v5.0》**（当前 v1.38）：防腐层设计详解
- **《APS_数据库字段说明文档_v5.0》**（当前 v5.2.2）：字段清单与业务口径
- **《APS_数据库表结构设计_v5.0.sql》**（当前 v5.2.2）：DDL 脚本
- **《APS 核心排产全流程走查（完整版）》**（当前 V3.19）：端到端流程走查
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：Socket-Plug职责分工

---

## 一、集成架构总览

### 1.1 集成系统清单

| 系统 | 类型 | 接口方式 | 同步频率 | 数据方向 |
|------|------|---------|---------|---------|
| **ERP** | 自研 | 数据库视图（时间戳轮询） | 每小时/每日凌晨 | 双向 |
| **MES** | 自研 | 双向数据库契约视图 | 实时视图；APS按运行截止时间切片 | 双向 |
| **采购系统** | 自研 | 数据库视图 | 5分钟 | 单向（读取） |
| **OA审批** | 第三方 | REST API | 实时 | 双向 |
| **邮件网关** | 第三方 | REST API | 实时 | 单向（发送） |
| **短信网关** | 第三方 | REST API | 实时 | 单向（发送） |

### 1.2 集成模式

```
┌─────────────────────────────────────────────────────────────┐
│                        APS System                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │           Integration Layer (Adapters)                │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │   │
│  │  │   ERP    │  │   MES    │  │Procurement│           │   │
│  │  │ Adapter  │  │ Adapter  │  │  Adapter  │           │   │
│  │  └──────────┘  └──────────┘  └──────────┘           │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │   │
│  │  │    OA    │  │  Email   │  │   SMS    │           │   │
│  │  │ Adapter  │  │ Adapter  │  │ Adapter  │           │   │
│  │  └──────────┘  └──────────┘  └──────────┘           │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Staging Tables (ETL)                     │   │
│  │  ERP_Order_Staging, MESWorkOrderSnapshot/OperationProgressSnapshot/StageProgressSnapshot, SyncCheckpoint│  │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
    ↓ DB视图(轮询)  ⇄ 双向契约视图  ↓ DB视图    ↓ REST API
┌──────────────┐ ┌──────────────┐ ┌──────────┐ ┌──────────────┐
│ ERP Database │ │  MES System  │ │Procurement│ │  OA/Email/   │
│ (SQL Server) │ │ (DB Views)   │ │  Database │ │  SMS Gateway │
└──────────────┘ └──────────────┘ └──────────┘ └──────────────┘
```

### 1.3 Socket-Plug集成模式

**架构原则**：采用Socket-Plug模式实现防腐层，确保APS系统与源系统解耦。

**契约插座（Socket）** - 源系统侧防腐：
- **ERP DBA负责**：创建`ERP_Master_View`（ERP主数据契约视图）
- **MES DBA / 5号位负责**：`MES_BOM_Edge_Active`（ODS库物化边表，**V1 BOM防腐合同层+执行优化层**，v5.0.26新增 2026-05-14）；`MES_BOM_View`（v5.0.26降为兼容视图 `SELECT * FROM MES_BOM_Edge_Active WHERE IsActive=1`）；`MES_Material_View`（MES物料契约视图）
- **职责红线**：契约视图的字段名和数据类型在v1.x版本中永不变更，只能追加字段

**数据插头（Plug）** - ODS侧防腐：
- **5号位负责**：
  - 创建`ext_ERP_Master_View`（ERP主数据跨库包装视图）
  - 创建`ext_MES_Material_View`（MES物料跨库包装视图）
  - `sp_RefreshBOMEdgeActive`：刷新 `MES_BOM_Edge_Active` 物化边表，写 RefreshLog RUNNING→COMPLETED/FAILED（v5.0.26新增 2026-05-14）
  - `sp_ExpandBOMBatch_vNext`：WHILE 迭代展开，直读 `MES_BOM_Edge_Active`，透传 RequestDetailId；旧 `sp_ExpandBOMBatch` 标记 deprecated
  - 后置回填`ChildRequiredStageCode` + 写入`MES_APS_BOM_Workset_StageDetail`（双层结果，v1.8新增）；**v5.0.26 +1列 `WorksetId`**（含Archive/Realtime变体）；`sp_CleanupBOMWorkset` 改按 WorksetId 清理
- **3号位负责**（v5.0更新）：
  - ~~创建`MES_APS_Routing_View`~~（v5.0废弃，已拆分为以下3个视图）
  - 创建`MES_APS_Routing_Operation_View`（工序节点视图）
  - 创建`MES_APS_Routing_Dependency_View`（工序依赖视图）
  - 创建`APS_OperationResourceEligibility_View`（工序资源能力视图）
  - 创建`MES_APS_Routing_Stage_View`（大工艺阶段视图，v1.7新增，v1.8定位调整为阶段字典 2026-04-15 更新）

**数据装载（Loader）** - APS侧（v5.0更新，v1.8更新 2026-04-15）：
- **2号位负责**：
  - 从ODS库拉取数据到APS库
  - 执行`sp_SyncMasterData(@SourceType)`（主数据同步）
  - 执行数据结构标准化（MaterialMapping → Material表）
  - 执行`APS_BOM_RAW → BOM表`转换
  - `MES_APS_BOM_Workset_StageDetail → APS_BOM_STAGE_PATH_RAW`：搬运阶段顺序明细（v1.8新增，与APS_BOM_RAW同批次拉取）
  - 从3个ext_包装视图分别装载到`RoutingOperation`/`RoutingDependency`/`OperationResourceEligibility`（v5.0新增）
  - 从ext_MES_APS_Routing_Stage_View装载到`RoutingStage`阶段字典（v1.7新增，v1.8定位调整为阶段字典）
  - 从`ext_MES_APS_Resource_View`全量刷新`Resource`表（v5.0新增；v1.11 命名统一，原名 `ext_APS_Resource_View`）
  - 执行 `sp_SyncResourceData(@SourceType)` 资源主数据同步（DDL v5.0.13 新增；v1 仅 'MES' 分支，'EAM' 分支预留）
  - 执行 `sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, @RowsAffected OUTPUT, @ErrorMessage OUTPUT)` 管道供给链同步（DDL v5.0.42；详见防腐层 §2.7.8）
  - `IDataLoader.LoadPipelineSupplies()` 方法签名（见下方）：从 `SupplyFact_Pipeline` 装载 `PipelineSupplyItem` 列表，注入 `ScheduleContext.PipelineSupplies`

**PipelineSupplyItem 契约定义**（V1正式契约；单一未接入来源可返回0行，但Loader必须读取准确批次切片）：

| 字段 | 类型 | 来源 |
|------|------|------|
| Id | BIGINT | SupplyFact_Pipeline.Id |
| MaterialCode | string | mat.MaterialCode（APS 标准编码） |
| MaterialId | int | mat.Id（NOT NULL） |
| FactoryCode | string | SupplyFact_Pipeline.FactoryCode |
| FactoryId | int | SupplyFact_Pipeline.FactoryId（NOT NULL） |
| ProductFamilyId | int? | mat.ProductFamilyId（物料未配置时可为空） |
| SupplyType | string | INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / ... |
| OwnershipType | string | OWNED / CONSIGNMENT / SUPPLIER |
| QualityStatus | string | AVAILABLE / PENDING_INSPECTION / HOLD |
| Quantity | decimal | 剩余在途数量 |
| ETA | DateTime? | ERP 原始预计到达时间 |
| AvailableTime | DateTime? | ETA + LeadTimeOffset（规则裁决后） |
| StorageCode | string | 目的仓库编码 |
| SupplierCode | string | 供应方编码 |
| SourceSystem | string | 来源系统（值域：ERP / PROCUREMENT / VMI；由 ext_PipelineSupply_Source_View 各分支派生） |
| SourceDocumentNo | string | ERP 来源单据号 |
| SourceDocumentLineNo | string | ERP 来源单据行号 |
| SourceMasterID | int | ODS.MasterID 直通 |
| SourceFactoryCode | string | 发出工厂编码 |
| SupplyAvailabilityRuleId | int? | 命中规则 ID（无命中为 NULL） |
| AppliedLeadTimeOffset | int? | 命中的 LeadTimeOffset（小时） |
| RulePriority | int? | 命中规则的 Priority 值 |
| RuleEvaluatedAt | DateTime? | 规则裁决时间（= @DataCutoffTime） |

**Loader 契约**：

```csharp
namespace APS.Core.Loaders
{
    /// <summary>
    /// 管道供给加载器（v1.27 当前口径）
    /// 夜间读取当前ScheduleRun的正式BatchNo；Candidate读取BasePlanVersion对应SourceScheduleRun的BatchNo。
    /// 单一来源未接入可以返回0行，但不得把整个PipelineSupplies实现为固定空集合。
    /// batchNo为null只保留deprecated兼容，正式运行必须拒绝。
    /// </summary>
    public interface IPipelineSupplyLoader
    {
        /// <summary>
        /// 加载管道供给到内存集合。
        /// </summary>
        /// <param name="batchNo">夜间快照：正式 BatchNo；Candidate 实时评估：BasePlanVersion 对应 SourceScheduleRun 的非空 BatchNo；null 仅 deprecated 兼容，不执行正式查询</param>
        /// <param name="dataCutoffTime">数据切片边界</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>准确批次的管道供给只读列表；0行必须是来源真实结果。</returns>
        Task<IReadOnlyList<PipelineSupplyItem>> LoadPipelineSuppliesAsync(
            string? batchNo,
            DateTime dataCutoffTime,
            CancellationToken cancellationToken);
    }
}
```

**消费查询**（V1当前口径）：
- 夜间快照：`WHERE BatchNo = @CurrentBatchNo AND IsActive = 1 AND AvailableTime IS NOT NULL`（ETA=NULL 的记录不进入正式供给扣减，另查询进入"待确认管道清单"）
- **Candidate实时消费**：`WHERE BatchNo = @BaseBatchNo AND IsActive = 1`（`@BaseBatchNo` = `BasePlanVersion` 对应 `SourceScheduleRun` 的非空 BatchNo）
- `BatchNo IS NULL` 仅为 deprecated 兼容路径，**不执行正式查询**
- **禁止**只按 `IsActive = 1` 查询（跨批次污染）

**职责红线**：
- ❌ 源系统DBA不得修改契约视图的字段名和数据类型（只能追加字段）
- ❌ 5号位不得修改源系统的物理表结构（只能创建跨库包装视图）
- ❌ 2号位不得直接访问源系统表（只能通过ODS库的ext视图拉取）

---

## 二、ERP 集成接口设计

### 2.1 ERP数据库视图定义（由ERP侧提供）

#### 2.1.1 ERP主数据契约视图（ERP_Master_View）

**负责人**：ERP DBA（源系统侧）

**所属库**：ERP生产库

**用途**：暴露ERP主数据标准字段，供ODS库跨库访问

```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-19
-- 负责人：ERP DBA
CREATE VIEW ERP_Master_View AS
SELECT 
    MaterialCode,          -- 物料编码（契约字段）
    MaterialName,          -- 物料名称（契约字段）
    MasterID,              -- ERP物理主键（契约字段）
    Warehouse,             -- 仓库（契约字段）
    UOM,                   -- 计量单位（契约字段）
    LeadTimeDays,          -- 提前期天数（契约字段）
    SafetyStock,           -- 安全库存（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM ERP.dbo.master
WHERE IsDeleted = 0;

-- ⚠️ 契约承诺：无论ERP内部表结构如何变更，此视图的列名和数据类型永不变更
```

#### 2.1.2 订单视图（v_APS_SalesOrder）（2026-04-03 订单链路审计修正）

**负责人**：ERP DBA（源系统侧）

**⚠️ 数据来源说明**（2026-04-03补充）：
- 此视图统一暴露**两类订单来源**：SO/MTO客户订单（来自订单中间表）+ MTS生产指示（来自生产指示中间表）
- 由 `OrderType` 字段区分（视图层为ERP原始值）：`SO`=销售订单, `MTO`=按单生产, `MTS`=库存生产指示, `SS`=安全库存, `SS_U`=紧急安全库存；`sp_ValidateAndPromoteOrders` 负责标准化（v5.0.24重分类：SO/MTO→`SALES_ORDER`；MTS/SS/SS_U→`PRODUCTION_INSTRUCTION`）
- ERP DBA负责在视图中 UNION ALL 两类中间表，对APS透明

```sql
-- ERP侧创建视图（只读）
-- ⚠️ 2026-04-03审计修正：补BOMNO/SourceSystem/SourceMasterID/DueDate，统一SO+MTS
-- ⚖️ 2026-04-09 v1.4：补TransportMode/CustomerName（源事实字段），MTS补InstructionNo（≠OrderNo）
-- ⚖️ 2026-04-09 v1.5：补IssueDate/OriginalDueDate/ReceivedQty（源事实字段，v1.2增补）
--   【业务澄清】OrderType/FactoryCode在此视图中为“原始值/简化映射”，真正的APS标准化由sp_ValidateAndPromoteOrders执行
--   本视图可包含“辅助字段”（用于衍生字段的输入信号），它们会进入RawData JSON但不作为Staging显式列
CREATE VIEW v_APS_SalesOrder AS
-- 客户订单（SO/MTO/SS/SS_U）
SELECT 
    CAST(OrderId AS NVARCHAR(100)) AS SourceOrderId,  -- ERP订单主键
    OrderNo,                            -- 订单号
    OrderType,                          -- SO/MTO/SS/SS_U（⚖️ 2026-04-09业务澄清：此处为ERP原始值，APS标准化由SP执行）
    MaterialCode,                       -- 物料编码
    BOMNO,                              -- BOM编号（⚠️ 2026-04-03审计补充）
    FactoryCode,                        -- 工厂编码（⚖️ 2026-04-09业务澄清：此处为ERP原始值，APS标准化由SP执行）
    Quantity,                           -- 数量
    UOM,                                -- 单位
    CustomerDueDate AS DueDate,         -- 交期（⚠️ 统一为DueDate）
    Priority,                           -- 优先级（1-100）
    Status,                             -- 订单状态
    'ERP' AS SourceSystem,              -- 来源系统（⚠️ 2026-04-03审计补充）
    MasterID AS SourceMasterID,         -- ERP物理主键（⚠️ 2026-04-03审计补充）
    -- ⚖️ 2026-04-09 v1.4 新增：源事实字段
    TransportMode,                      -- 运输方式（海运/空运/陆运）
    CustomerName,                       -- 客户名称
    NULL AS MTS_InstructionNo,          -- SO/MTO无生产指示号
    -- ⚖️ 2026-04-09 v1.5 新增：源事实字段（v1.2增补）
    OrderDate AS IssueDate,             -- 订单发行日期
    OriginalDueDate,                    -- 原始纳期（客户最初要求交期）
    NULL AS ReceivedQty,                -- SO订单无入库数量
    CreatedAt,                          -- 创建时间
    UpdatedAt                           -- 更新时间
FROM ERP.dbo.SalesOrder
WHERE Status NOT IN ('CANCELLED', 'CLOSED')
AND CreatedAt >= DATEADD(DAY, -90, GETDATE())

UNION ALL

-- MTS生产指示
SELECT 
    CAST(InstructionId AS NVARCHAR(100)) AS SourceOrderId,
    InstructionNo AS OrderNo,
    'MTS' AS OrderType,
    MaterialCode,
    BOMNO,
    FactoryCode,
    Quantity,
    UOM,
    PlannedDate AS DueDate,             -- MTS用PlannedDate作为DueDate
    Priority,
    Status,
    'ERP' AS SourceSystem,
    MasterID AS SourceMasterID,
    -- ⚖️ 2026-04-09 v1.4 新增：源事实字段
    TransportMode,                      -- 运输方式
    NULL AS CustomerName,               -- MTS生产指示无客户名称
    InstructionNo AS MTS_InstructionNo, -- ⚖️ 生产指示号（≠OrderNo，独立InstructionNo字段）
    -- ⚖️ 2026-04-09 v1.5 新增：源事实字段（v1.2增补）
    IssuedDate AS IssueDate,            -- 生产指示发行日期
    PlannedDate AS OriginalDueDate,     -- MTS原始纳期 = DueDate
    ReceivedQty,                        -- 已入库数量（MTS累计入库）
    CreatedAt,
    UpdatedAt
FROM ERP.dbo.ProductionInstruction      -- ⚠️ 生产指示中间表
WHERE Status NOT IN ('CANCELLED', 'CLOSED')
AND CreatedAt >= DATEADD(DAY, -90, GETDATE());
```

**字段说明**：
- `SourceOrderId`：ERP系统订单主键（NVARCHAR(100)，兼容INT和字符串主键）
- `OrderType`：订单类型（SO=销售订单, MTO=按单生产, MTS=库存生产指示, SS=安全库存, SS_U=紧急安全库存）（⚖️ 2026-04-09业务澄清：视图中为ERP原始值，APS标准化由`sp_ValidateAndPromoteOrders`执行）
- `FactoryCode`：工厂编码（⚖️ 2026-04-09业务澄清：视图中为ERP原始值，APS标准化由SP执行）
- `BOMNO`：BOM编号（⚠️ 2026-04-03审计补充；⚠️ v1.14/v5.0.21：改可空——有值=显式BOMNO，NULL=待5号位Workset解析BOM入口；ERP视图侧允许NULL透传）
- `SourceSystem`：来源系统标识（⚠️ 2026-04-03审计补充，固定为'ERP'）
- `SourceMasterID`：ERP物理主键（⚠️ 2026-04-03审计补充，用于物理追溯）
- `TransportMode`：运输方式（海运/空运/陆运），源事实字段（⚖️ 2026-04-09 v1.4新增）
- `CustomerName`：客户名称，ERP订单表直接提供，源事实字段（⚖️ 2026-04-09 v1.4新增）
- `MTS_InstructionNo`：生产指示号，仅MTS订单有值，来源于ProductionInstruction表的InstructionNo字段（≠OrderNo）（⚖️ 2026-04-09 v1.4新增）
- `IssueDate`：订单发行/下发日期，源事实字段；SO用OrderDate，MTS用IssuedDate（⚖️ 2026-04-09 v1.5新增）
- `OriginalDueDate`：原始纳期（客户最初要求交期），MTS时=PlannedDate（⚖️ 2026-04-09 v1.5新增）
- `ReceivedQty`：已入库数量，仅MTS有值（累计入库），SO订单为NULL（⚖️ 2026-04-09 v1.5新增）
- `DueDate`：统一交期（SO用CustomerDueDate，MTS用PlannedDate）
- `Priority`：优先级，1-100，数字越小优先级越高
- `Status`：订单状态（Open, Released, Scheduled, Completed, Cancelled）

**视图辅助字段边界说明**（2026-04-09 v1.4新增）：
- 本视图可包含“仅用于生成APS衍生字段的辅助字段”（如用于判断OrderType的源字段）
- 辅助字段会被`ERPOrderSyncService`读取，序列化到`RawData` JSON中保留
- 辅助字段**不作为Staging/Canonical的显式列**，仅衍生结果字段才落列

#### 2.1.2b BOM视图（v_APS_BOM）

```sql
CREATE VIEW v_APS_BOM AS
SELECT 
    BOMId AS SourceBOMId,
    ParentMaterialCode,
    ChildMaterialCode,
    Quantity,
    ScrapRate,
    LeadTimeOffset,
    BOMLevel,
    LowLevelCode,                   -- ⚠️ 新增：低阶码（LLC），用于拓扑排序
    EffectiveFrom,
    EffectiveTo,
    IsActive,
    ParentProcRefCode,         -- v1.8 父件工序参考码（ERP BOM原始辅助字段）
    ChildProcRefCode,          -- v1.8 子件工序参考码（同上）
    ChildSourceHintCode        -- v1.8 子件来源提示码（当前来源=ERP BOM的produce字段，0/1/2编码）
FROM ERP.dbo.BOM
WHERE IsActive = 1;
```

**⚠️ v1.8 BOM双层结果与阶段提前期参数化说明**（2026-04-13 更新，2026-04-15 v1.9补ROOT）：
- **3原始辅助字段**：`ParentProcRefCode` + `ChildProcRefCode` + `ChildSourceHintCode`，通过`MES_BOM_View`契约字段承接，APS不绑定ERP物理字段名
- **追溯增强字段**（v1.9）：`SourceSystem`（ERP/MES）+ `SourceBOMId`（源系统BOM物理主键）— 非运行必需，建议加入以支持版本裁决排查
- **⚠️ 唯一默认版本裁决原则（v1.9写死）**：`MES_BOM_View` 不是简单并表，ODS内部必须先裁决出唯一胜出版再暴露为`IsDefaultVersion=1`；VersionPriority不暴露为正式契约字段
- **双层结果Workset**：
  - 主层：`MES_APS_BOM_Workset` 携带 3辅助字段 + `ChildRequiredStageCode`（最终供给阈值，不承载根产品自身路径）
  - 明细层：`MES_APS_BOM_Workset_StageDetail` — **统一阶段路径结果表**（v1.9升级 2026-04-15），通过 `StageScopeType` 区分：
    - `EDGE`：子件供给路径（`ParentMaterialCode`=父件编码）
    - `ROOT`：根产品完工路径（`ParentMaterialCode=NULL`，`IsSupplyThreshold=0`）
- **流程**：`sp_RefreshBOMEdgeActive` 刷新边表（RefreshLog前置校验）→ `sp_ExpandBOMBatch_vNext` WHILE迭代展开+透传 RequestDetailId；`sp_EnrichBOMWorkset(@BatchNo)` 后置回填 `ChildRequiredStageCode` + `ChildRequiredFactory`（R17 工厂映射）+ 写入 StageDetail（EDGE+ROOT，WorksetId透传）+ 异常降级登记到 Issues（**永不阻塞批次**，v5.0.11 决策）
- **v1.14 RequestDetailId 追溯锚点**：`MES_APS_BOM_Workset.RequestDetailId` 和 `Issues.RequestDetailId` FK→`MES_API_BOM_Request_Detail.Id`；nullable；非业务键；**1号位主链不消费**；2/5号位追溯、回写、运营闭环用
- **v1.14 MES_API_BOM_Request_Detail 新结构**（v5.0.21）：新增 `OrderStagingId`/`Model`/`MaterialCode`/`FactoryCode`；`BOMNO` 改可空；唯一约束变更为 `(BatchNo, OrderStagingId)`——按订单粒度写入，BOM入口解析分流由**5号位Workset阶段负责**，2号位仅透传基础字段
- **ROOT推导规则**（v1.9）：5号位取Level=1的`ParentProcRefCode` → 映射标准化阶段路径 → 多条不一致时取最长路径+记WARNING（不静默并集）
- **2号位装载查询约定**（v1.32澄清）：2号位必须按`StageScopeType`区分查询并组装内存TaskDraft，不得混查EDGE+ROOT；1号位不直接查询该表
- **⚠️ NULL降级策略（写死）**：当 `ChildRequiredStageCode = NULL` 时，1号位排程引擎按**保守策略**处理——子件必须全工艺完成后才可供给父件
- **StageLeadTimeParam**：参数化外协阶段提前期，1号位对无RoutingOperation的阶段查此表生成标准Task
- **⚠️ v1.10 StageDetail.StageCode 取值约定（R20 目标工厂视角）**：
  - `StageDetail.StageCode` 必须取自 `StageDict`（字段文档 §1.9）
  - **R20 跨组织场景采用目标工厂视角**：父件在 TJ 工厂、子件 Produce=6（R20 指派到 BJ）→ `StageCode = BJ_MACH`（不是 TJ_MACH）
  - 1 号位读 StageCode 直接去目标工厂的 `RoutingOperation` 匹配 `(MaterialId, StageCode)` 找小工序生成 Task，Task 自动落在目标工厂产能队列，**无需跨厂翻译**
  - 落地责任：5 号位 `sp_EnrichBOMWorkset` 按 `ChildRequiredFactory`（查 `ProduceToFactoryMap` 得出）决定 StageCode 的工厂前缀
- **V1不建**：`BomProcessBinding` / `StageHintMapping` 留V2；结果字段已预埋在 Workset / Archive / APS_BOM_RAW 中

**⚠️ 重要说明 - LowLevelCode（低阶码）**：
- **定义**：物料在BOM树中的最大深度，用于支持APS步骤2.6的级联扫尾与拓扑排序
- **计算规则**：
  - 成品（无父件）：LLC = 0
  - 半成品/原料：LLC = MAX(父件LLC) + 1
  - 如果一个物料同时用于多个层级，取最大值
- **示例**：
  ```
  成品A (LLC=0)
    ├─ 半成品B (LLC=1)
    │   └─ 原料D (LLC=2)
    └─ 半成品C (LLC=1)
        └─ 原料D (LLC=2)  // D的LLC取最大值2
  ```
- **维护方式**：ERP侧需要在BOM变更时自动计算并更新LLC，或提供存储过程供APS调用计算
- **业务意义**：用于瀑布式扫尾时按LLC降序处理孤儿单据，避免多级工艺链下的负荷重复计算

#### 2.1.3 工艺路线视图（v5.0重构：拆分为3个视图）

> ⚠️ v5.0更新：原`v_APS_Routing`单视图已废弃，拆分为工序节点、工序依赖、工序资源能力三个独立视图。
> 详细字段定义见`APS_数据库字段说明文档_v5.0.md`。

**视图1：工序节点**（v5.0.1更新 2026-04-02：MaterialCode → MES_ID + Model）
```sql
CREATE VIEW MES_APS_Routing_Operation_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,            -- 默认'DEFAULT'
    PathId,               -- 默认1
    OperationCode,        -- 工序编码（唯一，如 NC / MC / 切断 / 精修）— 第1层：具体工序（执行粒度）
    OperationName,        -- 工序中文名
    ProcessType,          -- 第2层：辅助分类标签（值域见 ProcessTypeDict 骨架，骨架期 IsActive=0）；⚠️ v1.10：不参与 BOM↔Routing 对接，仅用于报表/粗分组/统计
    StageCode,            -- 第3层：大工艺阶段码（如 TJ_MACH / BJ_PAINT）；⚠️ v1.10：BOM↔Routing 对接主键之二；必须取自 StageDict（字段文档 §1.9）
    StandardTime,         -- 标准工时（分钟）
    SetupTime,
    IsActive,
    SourceSystem          -- v1.16新增（v5.0.24）：追溯增强字段，非运行必需；'MES'（当前唯一来源）；未来EAM上线时扩充；与 MES_BOM_View.SourceSystem 模式对齐
FROM ...;  -- 3号位负责梳理28张离散工艺表（老结构由3号位ETL处理为MES_ID）
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping(Source='MES', SourceID=MES_ID) → MaterialId
```

**视图2：工序依赖边**（v5.0.1更新 2026-04-02：MaterialCode → MES_ID + Model）
```sql
CREATE VIEW MES_APS_Routing_Dependency_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,
    PathId,
    FromOperationCode,    -- 前置工序
    ToOperationCode,      -- 后置工序
    DependencyType,       -- ES(End-to-Start)/SS(Start-to-Start)
    LagTime,             -- 等待时间（分钟）
    SourceSystem          -- v1.16新增（v5.0.24）：追溯增强字段，非运行必需；'MES'；与 MES_APS_Routing_Operation_View 对齐
FROM ...;  -- 3号位负责（老结构由3号位ETL处理为MES_ID）
-- ⚠️ APS侧装载：同上，2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

**视图3：工序资源能力（替代原ResourceGroupCode）**（v5.0.1更新 2026-04-02：MaterialCode → MES_ID + Model）
```sql
CREATE VIEW APS_OperationResourceEligibility_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,
    PathId,
    OperationCode,
    ResourceCode,         -- 可执行该工序的资源编码
    Priority,             -- 优先级(1=首选)
    CapacityFactor        -- 该资源执行该工序的产能系数
FROM ...;  -- 3号位负责（老结构由3号位ETL处理为MES_ID）
-- ⚠️ APS侧装载：同上，2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

**视图4：大工艺阶段（阶段字典）**（v1.7新增，v1.8定位调整，v1.10 删除 StageSeq + 补 StageDict 映射责任）
```sql
CREATE VIEW MES_APS_Routing_Stage_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,            -- 默认'DEFAULT'
    PathId,               -- 默认1
    StageCode,            -- ⚠️ v1.10：大工艺阶段码，必须已映射标准化为 StageDict 值（如 TJ_MACH/BJ_PAINT）；MES 本地叫法 → StageDict 的映射责任在本视图层完成
    StageName,            -- 阶段中文名（如机加/外协/涂装）
    -- v1.10 删除：StageSeq（跨物料/跨根产品语境下 MES 给不出正确值；权威唯一信 StageDetail.StageSeq）
    IsOutsource,          -- 是否外协阶段
    IsStockPoint,         -- 是否半成品库存断点
    IsActive
FROM ...;  -- 3号位负责（MES原始大工艺阶段数据 + StageDict 标准化映射）
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping → MaterialId → RoutingStage
-- ⚠️ v1.10：契约视图层禁止直接输出 MES 原生字符串；必须已完成到 StageDict 的映射
```

**⚠️ v1.10 关键设计决策 — 工艺数据三层模型**（2026-04-25，见字段文档 §1.9b/§1.9c）：

| 层 | 字段 | 粒度 | 值域举例 | 是否参与 BOM↔Routing 对接 |
|---|---|---|---|---|
| 第 1 层：具体工序 | `OperationCode` / `OperationName` | 执行粒度 | NC / MC / 切断 / 精修 | ❌ 仅在 Routing 侧内部按 RoutingDependency 串联 |
| 第 2 层：辅助分类 | `ProcessType`（值域见 `ProcessTypeDict` 骨架）| 报表粒度 | MACHINING / ASSEMBLY | ❌ **完全不参与**；仅统计/粗分组 |
| 第 3 层：大工艺 | `StageCode`（值域权威在 `StageDict`）| 对接粒度 | TJ_MACH / BJ_PAINT | ✅ **BOM↔Routing 对接主键之二** |

**三层硬红线**（不可违反）：
- ❌ 禁止把 `OperationName` 值塞进 `ProcessType`（"NC" 不能当 ProcessType）
- ❌ 禁止把 `ProcessType` 当 `StageCode`（"MACHINING" 不能当 StageCode）
- ❌ 禁止把 `StageCode` 当 `OperationCode`（"TJ_MACH" 不是小工序）

**BOM↔Routing 对接主键（业务口径 vs 物理实现）**：

| 视角 | 主键 | 使用场景 |
|---|---|---|
| **业务口径主键** | `(MaterialCode, StageCode)` | 跨文档/跨号位沟通、经验库规则、日志/异常/Issues 登记（人可读）|
| **物理实现主键** | `(MaterialId, StageCode)` | 数据库表 JOIN/索引/约束；`MaterialId` 是 `MaterialCode` 经 `MaterialMapping` 得到的标准化代理键 |
| **等价性** | 二者等价，不是两套键 | 详见字段文档 §1.9c |

**RoutingStage / StageDetail / StageLeadTimeParam 职责分离**：
- `RoutingStage` = **阶段字典**（该物料在哪些大工艺阶段存在配置；v1.10 起**不承载任何顺序信息**）
  - 已知限制：MES工艺侧不包含外协阶段，数据可能不完整
- `MES_APS_BOM_Workset_StageDetail` = **排程权威阶段顺序源**（5号位基于ERP BOM辅助字段推导的完整阶段链；**`StageSeq` 权威唯一此处**）
- `StageLeadTimeParam` = **外协阶段参数化提前期**（1号位对无 RoutingOperation 的 StageCode 查此表生成标准 Task）
- `StageCode` 值域**三表统一**：`RoutingOperation.StageCode` / `RoutingStage.StageCode` / `StageDetail.StageCode` 必须均取自 `StageDict`

#### 2.1.4 库存视图（v_APS_Inventory）

```sql
CREATE VIEW v_APS_Inventory AS
SELECT 
    MaterialCode,
    FactoryCode,
    OnHandQty,
    AllocatedQty,
    (OnHandQty - AllocatedQty) AS AvailableQty,
    LastUpdatedAt
FROM ERP.dbo.Inventory
WHERE OnHandQty > 0 OR AllocatedQty > 0;
```

#### 2.1.5 承诺交期回写表（t_APS_PromisedDate）

```sql
-- ERP侧创建表（APS写入）
CREATE TABLE t_APS_PromisedDate (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    SourceOrderId NVARCHAR(100) NOT NULL,
    PromisedDate DATE NOT NULL,
    PlanVersionCode NVARCHAR(50) NOT NULL,
    UpdatedBy NVARCHAR(100) NOT NULL DEFAULT 'APS',
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    SyncStatus NVARCHAR(50) NOT NULL DEFAULT 'PENDING', -- PENDING, SYNCED, FAILED
    CONSTRAINT UQ_APS_PromisedDate UNIQUE (SourceOrderId, PlanVersionCode)
);

-- ERP侧定时任务读取此表，更新到SalesOrder表
```

#### 2.1.6 管道供给契约视图（ERP_InterplantInTransit_View）（v1.19 2026-06-15 升级为14字段完整契约）

**ODS层契约视图**（5号位负责）：

| 属性 | 内容 |
|------|------|
| 视图名 | `ERP_InterplantInTransit_View` |
| 所属层 | ODS 层 |
| 所属库 | MES_Integration |
| 来源系统 | ERP |
| 维护责任人 | 5号位（ODS 契约实现）；ERP DBA / ERP 业务负责人（来源字段确认） |
| 用途 | 暴露 ERP 厂间物流运输在途数据，当前为管道供给链的唯一 ODS 来源 |
| V1状态 | WHERE 1=0 返回 0 行的空契约骨架 |
| V1.1/V2计划 | 5号位替换内部 SELECT 和 ERP 源表 JOIN，不改变对外契约 |

**链路**：ERP 源系统厂间在途数据 → **ODS.`ERP_InterplantInTransit_View`**（ODS层/MES_Integration/5号位）→ **APS.`ext_ERP_InterplantInTransit_View`**（APS单来源包装/2号位）→ **APS.`ext_PipelineSupply_Source_View`**（多来源UNION ALL统一输入视图，V1已建立/2号位；15列）→ `sp_SyncPipelineSupply` → `SupplyFact_Pipeline` → `ScheduleContext.PipelineSupplies`

**⚠️ 契约锁定规则**：
ODS 契约视图字段结构为强契约。V1.1/V2 启用真实数据时，**仅允许**替换视图内部的 FROM 和 JOIN 逻辑，**禁止修改**字段顺序、字段类型、字段名称。

**字段契约（14字段，最终契约）**：

| 字段名 | 类型 | 是否必须 | 业务说明 |
|--------|------|---------|---------|
| MasterID | INT | 实际数据必须 | ERP物料主数据物理ID（→ MaterialMapping.SourceID → MaterialId） |
| MaterialCode | NVARCHAR(100) | 建议必须 | ERP物料编码（业务追溯；不替代 MasterID 权威映射） |
| SourceFactoryCode | NVARCHAR(50) | 可空 | 发出工厂编码（仅物流追溯） |
| FactoryCode | NVARCHAR(50) | 实际数据必须 | 目的工厂/收货工厂（→ SupplyFact_Pipeline.FactoryId） |
| SupplyType | NVARCHAR(50) | 必须 | 本视图固定为 INTERPLANT_IN_TRANSIT |
| OwnershipType | NVARCHAR(20) | 必须 | 厂间在途默认 OWNED |
| QualityStatus | NVARCHAR(20) | 必须 | 默认 AVAILABLE |
| Quantity | DECIMAL(18,4) | 必须 | **剩余在途数量**（非原始发货数量） |
| ETA | DATETIME2 | 可空 | ERP原始预计到达时间；**ODS不得加入APS提前期偏移** |
| StorageCode | NVARCHAR(50) | 可空 | 目的仓库编码 |
| SupplierCode | NVARCHAR(50) | 可空 | 厂间在途可为空；采购在途/VMI扩展预留 |
| SourceDocumentNo | NVARCHAR(100) | 建议必须 | ERP来源单据号 |
| SourceDocumentLineNo | NVARCHAR(50) | 可空 | ERP来源单据行号 |
| SourceUpdatedAt | DATETIME2 | 可空 | ERP来源更新时间（增量同步/新鲜度检查） |

**字段语义红线**：
```text
FactoryCode = 目的工厂 / 收货工厂 / 可使用该供给的工厂；
SourceFactoryCode = 发出工厂。

Quantity = 当前仍在途、尚未收货的剩余数量；
不得直接使用原始发货数量导致重复供给。

ETA = ERP原始事实；
AvailableTime 不允许在 ODS 中计算；
AvailableTime 由 APS 的 sp_SyncPipelineSupply 根据
ETA + SupplyAvailabilityRule.LeadTimeOffset 计算。
```

**视图行范围**（未来接入真实数据后）：
```sql
-- 仅输出：尚未全部收货 / 尚未关闭 / 尚未取消 / 剩余数量>0 / 能明确目的工厂 / 有效厂间调拨数据
WHERE 收货状态 NOT IN ('全部收货', '已关闭', '已取消') AND 剩余数量 > 0
```

**APS层包装视图**（2号位负责）：

| 属性 | 内容 |
|------|------|
| 视图名 | `ext_ERP_InterplantInTransit_View` |
| 所属层 | APS 层 |
| 所属库 | APS_Production |
| 来源 | MES_Integration.dbo.ERP_InterplantInTransit_View |
| 维护责任人 | 2号位 |

```sql
-- APS 跨库包装视图（⚠️ 禁止 SELECT *，必须显式列字段）
CREATE OR ALTER VIEW dbo.ext_ERP_InterplantInTransit_View
AS
SELECT
    MasterID,
    MaterialCode,
    SourceFactoryCode,
    FactoryCode,
    SupplyType,
    OwnershipType,
    QualityStatus,
    Quantity,
    ETA,
    StorageCode,
    SupplierCode,
    SourceDocumentNo,
    SourceDocumentLineNo,
    SourceUpdatedAt
FROM [MES_Integration].[dbo].[ERP_InterplantInTransit_View];
```

**⚠️ 重要说明**：
- `ETA` 是 ERP 侧原始事实字段，APS 不修改
- `AvailableTime` 在 `sp_SyncPipelineSupply` 装载时本地计算（`ETA + SupplyAvailabilityRule.LeadTimeOffset`），不在 ODS 视图提供
- 未来新增供给类型（VMI_ONSITE / PURCHASE_IN_TRANSIT 等），只需新增对应契约视图，不改此视图
- V1 空链路预留：ODS 视图返回 0 行，APS 包装视图同步返回 0 行
- 字段契约升级必须通过显式改列，禁止 `SELECT *` 静默传播字段变化

---

### 2.2 增量同步机制（时间戳轮询）（2026-04-08 审计修正）

> ⚠️ **2026-04-08修正**：原§2.2为CDC（变更数据捕获）配置，已废弃。ERP实际环境未启用CDC，改为基于`UpdatedAt`时间戳的增量轮询，与防腐层文档v1.3 §2.2.1对齐。

#### 2.2.1 增量检测原理

**时间戳轮询**：APS侧维护`SyncCheckpoint`表记录上次同步时间，每次查询 `ext_v_APS_SalesOrder WHERE UpdatedAt >= @lastSyncTime` 获取增量变更。

**优势**：
- 无需ERP侧启用CDC，对ERP零侵入
- 通过ODS的`ext_`包装视图间接访问，遵守Socket-Plug红线（2号位不直连源系统）
- 与防腐层文档v1.3 §2.2.1完全一致，无实现分歧

**局限**：
- 物理删除无法感知（ERP侧应使用逻辑删除/状态标记，由`sp_ValidateAndPromoteOrders`检测源端消失）
- 时间戳精度依赖ERP侧`UpdatedAt`字段的更新纪律

#### 2.2.2 ODS侧订单包装视图（ext_v_APS_SalesOrder）

```sql
-- ODS库（MES_Integration），5号位负责创建
-- 跨库包装视图，封装ERP→ODS的物理链路，遵循Socket-Plug模式
CREATE VIEW ext_v_APS_SalesOrder AS
SELECT *
FROM ERP_Production.dbo.v_APS_SalesOrder;
-- ⚠️ 以上为同实例跨库场景。若跨实例，改用 LinkedServer 或 Synonym
-- ⚠️ 契约承诺：列名和数据类型随 v_APS_SalesOrder 契约视图版本锁定
```

#### 2.2.3 同步检查点表（SyncCheckpoint）

```sql
-- APS本地库，2号位负责
CREATE TABLE SyncCheckpoint (
    Id INT PRIMARY KEY IDENTITY(1,1),
    SyncType NVARCHAR(50) NOT NULL,      -- 'ERP_Order', 'ERP_Inventory', ...
    CheckpointTime DATETIME2 NOT NULL,   -- 本次同步截止时间
    RowCount INT NOT NULL DEFAULT 0,     -- 本次拉取行数
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
CREATE INDEX IX_SyncCheckpoint_Type ON SyncCheckpoint(SyncType, CheckpointTime DESC);
```

### 2.3 ERP Adapter实现（APS侧）

#### 2.3.1 订单同步服务（2026-04-08 集成接口审计修正）

> ⚠️ **2026-04-08修正**（覆盖2026-04-03版本）：
> - CDC依赖废弃 → 改为`UpdatedAt`时间戳轮询（M1/M2）
> - 直连ERP → 改为通过ODS `ext_v_APS_SalesOrder`包装视图（M4 Socket-Plug红线）
> - EF Core `ApsDbContext` → 改为Dapper `SqlConnection`（M6 技术栈对齐）
> - 同步路径不变：`ERP_Order_Staging`（PENDING）→ `sp_ValidateAndPromoteOrders` → `Order_Canonical`

```csharp
public class ERPOrderSyncService
{
    private readonly ILogger<ERPOrderSyncService> _logger;
    private readonly string _odsConnectionString;           // ⚠️ ODS库，非ERP库（Socket-Plug红线）
    private readonly string _apsConnectionString;           // APS本地库
    private readonly IOrderPromotionService _promotionService;

    // 全量同步（每日凌晨执行）
    public async Task FullSyncAsync()
    {
        // ⚠️ 通过ODS的ext_包装视图间接访问ERP契约视图（不直连ERP）
        using var odsConn = new SqlConnection(_odsConnectionString);
        var orders = await odsConn.QueryAsync<ERPOrderDto>(@"
            SELECT * FROM ext_v_APS_SalesOrder
            WHERE UpdatedAt >= DATEADD(DAY, -90, GETDATE())
        ");

        using var apsConn = new SqlConnection(_apsConnectionString);
        foreach (var order in orders)
        {
            await WriteToStagingAsync(apsConn, order);
        }

        // 批量写入Staging后，统一执行验证与提升
        await _promotionService.ValidateAndPromoteAsync(); // 调用 sp_ValidateAndPromoteOrders

        _logger.LogInformation("Full sync completed: {Count} orders written to Staging", orders.Count());
    }

    // 增量同步（每小时执行，与防腐层文档v1.3 §2.2.1对齐）
    public async Task IncrementalSyncAsync()
    {
        // ⚠️ 时间戳轮询，从SyncCheckpoint取上次同步时间
        var lastSyncTime = await GetLastSyncCheckpointAsync();

        using var odsConn = new SqlConnection(_odsConnectionString);
        var orders = await odsConn.QueryAsync<ERPOrderDto>(@"
            SELECT * FROM ext_v_APS_SalesOrder
            WHERE UpdatedAt >= @LastSyncTime
        ", new { LastSyncTime = lastSyncTime });

        if (!orders.Any())
        {
            _logger.LogDebug("No incremental changes since {LastSync}", lastSyncTime);
            return;
        }

        using var apsConn = new SqlConnection(_apsConnectionString);
        foreach (var order in orders)
        {
            await WriteToStagingAsync(apsConn, order);
        }

        // 增量写入Staging后，统一执行验证与提升
        await _promotionService.ValidateAndPromoteAsync();

        // 更新同步检查点
        await UpdateSyncCheckpointAsync(DateTime.Now, orders.Count());

        _logger.LogInformation("Incremental sync completed: {Count} changes since {LastSync}",
            orders.Count(), lastSyncTime);
    }

    // Upsert到ERP_Order_Staging（Dapper + MERGE）
    // ⚠️ 2026-04-09 v1.4：补充TransportMode/CustomerName/MTS_InstructionNo源事实字段
    private async Task WriteToStagingAsync(SqlConnection apsConn, ERPOrderDto o)
    {
        await apsConn.ExecuteAsync(@"
            MERGE ERP_Order_Staging AS target
            USING (SELECT @SourceOrderId AS SourceOrderId, @SourceSystem AS SourceSystem) AS source
            ON target.SourceOrderId = source.SourceOrderId
               AND target.SourceSystem = source.SourceSystem
            WHEN MATCHED THEN UPDATE SET
                OrderNo = @OrderNo, OrderType = @OrderType, MaterialCode = @MaterialCode,
                Quantity = @Quantity, DueDate = @DueDate, Priority = @Priority,
                TransportMode = @TransportMode, CustomerName = @CustomerName,
                MTS_InstructionNo = @MTS_InstructionNo,
                IssueDate = @IssueDate, OriginalDueDate = @OriginalDueDate,
                ReceivedQty = @ReceivedQty,
                RawData = @RawData, SyncStatus = 'PENDING', SyncedAt = GETDATE()
            WHEN NOT MATCHED THEN INSERT
                (SourceOrderId, OrderNo, OrderType, MaterialCode, FactoryCode,
                 Quantity, UOM, DueDate, Priority, BOMNO, SourceSystem, SourceMasterID,
                 TransportMode, CustomerName, MTS_InstructionNo,
                 IssueDate, OriginalDueDate, ReceivedQty,
                 RawData, SyncStatus, SyncedAt)
            VALUES
                (@SourceOrderId, @OrderNo, @OrderType, @MaterialCode, @FactoryCode,
                 @Quantity, @UOM, @DueDate, @Priority, @BOMNO, @SourceSystem, @SourceMasterID,
                 @TransportMode, @CustomerName, @MTS_InstructionNo,
                 @IssueDate, @OriginalDueDate, @ReceivedQty,
                 @RawData, 'PENDING', GETDATE());
        ", new {
            o.SourceOrderId, o.OrderNo, o.OrderType, o.MaterialCode, o.FactoryCode,
            o.Quantity, o.UOM, DueDate = o.DueDate, o.Priority,
            o.BOMNO, o.SourceSystem, o.SourceMasterID,
            o.TransportMode, o.CustomerName, o.MTS_InstructionNo,
            o.IssueDate, o.OriginalDueDate, o.ReceivedQty,
            RawData = JsonSerializer.Serialize(o)
        });
        // 后续由 sp_ValidateAndPromoteOrders 统一校验并提升到 Order_Canonical
        // ⚠️ v1.14：校验失败时 SP 写入 FailureCode（原因维度）+ NextActionCode（动作维度），两个独立字段；
        //          SyncStatus=FAILED 仅表达技术流转；BOMNO 可为 NULL（NULL=待5号位Workset解析BOM入口）
    }

    private async Task<DateTime> GetLastSyncCheckpointAsync()
    {
        using var apsConn = new SqlConnection(_apsConnectionString);
        return await apsConn.QuerySingleOrDefaultAsync<DateTime?>(
            "SELECT MAX(CheckpointTime) FROM SyncCheckpoint WHERE SyncType = 'ERP_Order'"
        ) ?? DateTime.Today.AddHours(-1);
    }

    private async Task UpdateSyncCheckpointAsync(DateTime checkpointTime, int rowCount)
    {
        using var apsConn = new SqlConnection(_apsConnectionString);
        await apsConn.ExecuteAsync(@"
            INSERT INTO SyncCheckpoint (SyncType, CheckpointTime, RowCount, CreatedAt)
            VALUES ('ERP_Order', @CheckpointTime, @RowCount, GETDATE())
        ", new { CheckpointTime = checkpointTime, RowCount = rowCount });
    }
}
```

#### 2.3.2 承诺交期回写服务（2026-04-08 审计修正）

> ⚠️ **2026-04-08修正**：原SQL使用PostgreSQL的`ON CONFLICT`语法，SQL Server不支持，改为`MERGE`。

```csharp
public class ERPPromisedDateWritebackService
{
    private readonly string _erpConnectionString; // ⚠️ 回写场景：APS→ERP，需直连ERP写入表

    public async Task WritebackPromisedDateAsync(
        string sourceOrderId, 
        DateTime promisedDate, 
        string planVersionCode)
    {
        using var erpConn = new SqlConnection(_erpConnectionString);

        var sql = @"
            MERGE t_APS_PromisedDate AS target
            USING (SELECT @SourceOrderId AS SourceOrderId, 
                          @PlanVersionCode AS PlanVersionCode) AS source
            ON target.SourceOrderId = source.SourceOrderId
               AND target.PlanVersionCode = source.PlanVersionCode
            WHEN MATCHED THEN UPDATE SET
                PromisedDate = @PromisedDate,
                UpdatedAt = GETDATE(),
                SyncStatus = 'PENDING'
            WHEN NOT MATCHED THEN INSERT
                (SourceOrderId, PromisedDate, PlanVersionCode, UpdatedBy, UpdatedAt, SyncStatus)
            VALUES
                (@SourceOrderId, @PromisedDate, @PlanVersionCode, 'APS', GETDATE(), 'PENDING');
        ";

        await erpConn.ExecuteAsync(sql, new 
        { 
            SourceOrderId = sourceOrderId, 
            PromisedDate = promisedDate, 
            PlanVersionCode = planVersionCode 
        });
    }
}
```

---

## 三、MES 系统集成接口设计（v1.28 双向视图权威契约）

### 3.1 集成架构概览

APS与MES不采用APS主动调用MES建单/取消接口的模式，统一使用双向视图：

```text
APS → MES：
ACTIVE计划结果
→ MESPlanRelease稳定发布快照
→ APS_MES_PlanRelease_View
→ MES按ReleaseItemKey幂等创建现实工单

MES → APS：
MES实时工单/工序进度/Stage进度视图
→ APS按ScheduleRun.DataCutoffTime和Domain工作集读取
→ MESWorkOrderSnapshot / OperationProgressSnapshot / StageProgressSnapshot
→ MESPlanRelease对账 + ExecutionLock更新 + 排程输入
```

**红线**：
- 核心Pegging与排程循环不得直接反复查询MES实时视图；每次运行只读本运行快照。
- APS不建设MQ事件累计、REST备用轮询、`MES_Actual_Staging`事件链、`DispatchRequestId`/取消回执台账；三类运行快照与`ReleaseItemKey`必须保留。
- 三类快照同步任一技术失败必须向调用方抛出并阻断对应运行；契约视图合法0行与同步失败不得共用“空集合成功”语义。
- 时间范围只用于缩小工作集；MES视图输出必须是当前累计事实，不是需要APS二次累计的事件增量。

### 3.2 APS提供给MES的发布契约

#### 3.2.1 MESPlanRelease发布对象

一条`MESPlanRelease`对应一张未来MES现实工单，可关联同一生产指示、同一Stage、同一执行批次下的多个APS小工序Task。

| 字段 | 必填 | 语义 |
|---|---:|---|
| ReleaseItemKey | 是 | APS生成的不可变幂等键，格式建议`RLS:{GUID}`；MES必须唯一保存 |
| ProductionInstructionNo | 是 | 正式生产指示号 |
| StageCode | 是 | 一张现实MES工单所属唯一Stage |
| MaterialCode | 是 | 对MES公开的稳定物料编码；`MaterialId`仅在APS内部表保存 |
| FactoryCode | 是 | 对MES公开的稳定工厂编码；`FactoryId`仅在APS内部表保存 |
| Quantity | 是 | 该现实工单计划数量 |
| PlannedStartTime / PlannedEndTime | 否 | APS计划时间 |
| PublishStatus | 是 | PUBLISHED / CONSUMED / CANCELLED |
| PublishedAt / UpdatedAt | 是 | 增量读取与状态复核时间 |

`TaskNo`可以作为展示字段扩展到视图，但不得作为MES建单幂等键或跨版本锚点。

#### 3.2.2 APS_MES_PlanRelease_View

MES读取本视图时：
1. 只对`PublishStatus=PUBLISHED`且尚未按ReleaseItemKey建单的记录创建工单；
2. 同一ReleaseItemKey无论读取多少次，最多形成一张MES工单；
3. 真正落工单前再次读取当前状态，若已变为CANCELLED则不得建单；
4. 建单后在MES工单记录中原样保存ReleaseItemKey。

### 3.3 MES提供给APS的实时契约视图

#### 3.3.1 MES_APS_WorkOrder_View

最小字段契约：

| 字段 | 必填 | 说明 |
|---|---:|---|
| ReleaseItemKey | APS发布工单必填；历史/外部工单可空 | 回传APS发布幂等键 |
| MESWorkOrderNo | 是 | MES现实工单稳定编号 |
| ProductionInstructionNo | 是 | 生产指示号 |
| MaterialCode | 是 | 工单物料 |
| PlannedQty | 是 | 工单计划数量 |
| WorkOrderStatus | 是 | MES当前工单状态 |
| SourceUpdatedAt | 建议必填 | MES该工单最后更新时间；支持DataCutoffTime切片 |

对于APS发布的新工单，ReleaseItemKey必须与MESWorkOrderNo形成一一关系。MES若对同一ReleaseItemKey返回多个工单，APS登记`MES_RELEASE_DUPLICATE_WORKORDER`并阻断对应发布承接。

#### 3.3.2 MES_APS_OperationProgress_View

继续采用既有累计事实契约：生产指示号、MES工单号、物料、OperationName、StageCode、计划量、累计良品量、报废/返工量、最后报工时间、SourceUpdatedAt。OperationName是V1小工序识别主字段。

#### 3.3.3 MES_APS_StageProgress_View

继续采用既有累计事实契约：生产指示号、物料、StageCode、计划量、累计良品完成量、报废/返工量、最后报工时间、SourceUpdatedAt。PI大工艺位置以本视图快照为权威；OperationProgress只做Stage内部裁剪和诊断。

#### 3.3.4 资源状态事实

设备故障/恢复同样通过MES实时资源状态契约视图或既有设备事实视图提供，具体物理视图由5号位在ODS层收口。V1不要求逐条故障事件ID，不自动暂停/恢复Task，也不自动创建Candidate。

### 3.4 APS侧同步与对账服务

#### 3.4.1 MES三类快照独立定时任务边界

- 00:40、00:45、00:50三个独立Hangfire Job分别调用`sp_SyncMESWorkOrderSnapshot`、`sp_SyncOperationProgressSnapshot`、`sp_SyncStageProgressSnapshot`。
- 三个Job从已创建的`ScheduleRun`取得同一`ScheduleRunId + DataCutoffTime`，不得各自取当前时间，也不得由`INightlyBatchOrchestrator`直接调用。
- 同一ScheduleRun重试时，各Job只全量替换本运行对应快照切片，不累计事件。
- 02:00 `ISchedulingOrchestrator`只校验三张快照的运行标识、截止时间和同步成功状态，随后装载`ScheduleContext`；核心计算循环禁止访问MES实时视图。

#### 3.4.2 CreateMesPlanReleasesAsync

```csharp
Task<MesPlanReleaseResult> CreateMesPlanReleasesAsync(
    int activePlanVersionId,
    IReadOnlyCollection<ScheduledTaskReleaseCandidate> candidates,
    CancellationToken cancellationToken);
```

2号位按同PI、同Stage、同执行批次形成发布单元，生成ReleaseItemKey，写MESPlanRelease并回填Task.MESPlanReleaseId。`MESPlanRelease.Quantity`取发布草稿/LogicalBlock中的Stage级单一流转数量，不得对关联小工序Task.Quantity求和；同组数量不一致时必须先按已配置的换算/损耗规则显式换算并通过闭合校验，否则整组拒绝。虚拟Task、已有ExecutionLock或已有有效发布记录的Task必须拒绝。

#### 3.4.3 ReconcileMesPlanReleasesAsync

```csharp
Task<MesReleaseReconcileResult> ReconcileMesPlanReleasesAsync(
    int scheduleRunId,
    DateTime dataCutoffTime,
    CancellationToken cancellationToken);
```

按`MESWorkOrderSnapshot.ReleaseItemKey`匹配PUBLISHED发布记录，在单事务中：
1. 校验同一ReleaseItemKey只对应一个MES工单；
2. `MESPlanRelease→CONSUMED`并写MESWorkOrderNo；
3. 创建或幂等更新ExecutionLock；
4. 将关联Task写入ExecutionLockId并从PLANNED迁移到RELEASED。

#### 3.4.4 ApplyMesCumulativeExecutionFactsAsync

```csharp
Task<ExecutionFactApplyResult> ApplyMesCumulativeExecutionFactsAsync(
    int scheduleRunId,
    CancellationToken cancellationToken);
```

- 以MES累计事实覆盖/校准ExecutionLock当前状态，不按事件增量重复累加。
- MES工序状态4只表示该小工序执行记录人工完结，不直接改变PI总剩余量，也不单独关闭整张ExecutionLock。
- 各小工序剩余加工量由`OperationProgressSnapshot`计算；只有MES工单视图明确整张现实工单终结时，2号位才将`RemainingExecutionQty`置0，并把未完成、未正式取消的差额返回PI未承诺剩余池。
- MES明确取消事实才能增加CancelledQty；订单取消本身不能直接改ExecutionLock。
- 所有Task/ExecutionLock状态物理更新只由2号位执行；5号位只返回判断Result。

#### 3.4.5 ReconcileHardLockLifecycleAsync

```csharp
Task<HardLockLifecycleResult> ReconcileHardLockLifecycleAsync(
    int scheduleRunId,
    DateTime dataCutoffTime,
    IReadOnlyCollection<HardLockFulfillmentFact> fulfillmentFacts,
    CancellationToken cancellationToken);
```

- `fulfillmentFacts`必须是截至`DataCutoffTime`的累计、可追溯履约/不可逆消耗事实，不是重复累加的事件增量；2号位按HardLock聚合后覆盖式、单调更新`FulfilledQty`。
- 同一事实重复读取不得再次增加FulfilledQty；更新后按`RemainingLockedQty`自动维持ACTIVE或转COMPLETED。
- `ReleasedQty`只通过已批准的HardLock解除操作增加；不得用履约事实代替释放，也不得释放已履约数量。
- Pipeline与Received仍保留单据身份时沿用同一DOC键并确保旧表示退出；进入普通池化库存并失去单据身份后使用INV键。严格绑定数量不得提前丢失DOC身份；PI位置转ExecutionLockedOutput继续沿用PI键。
- 全部物理更新只由2号位在事务中执行；5号位只判断事实是否命中HardLock及其业务原因。

### 3.5 Candidate现实事实契约

Candidate创建时：
- Base ACTIVE提供订单、库存、在途、Received和Scope外SOFT保护边界；
- Candidate按自己的DataCutoffTime读取MES实时累计视图，形成独立MES执行快照；
- 先互斥重建物理身份（已消耗、ExecutionLock、PUBLISHED MESPlanRelease、普通位置切片），再在每项物理供给内部恢复HardLock与Scope外SOFT，并释放Scope内SOFT；
- 不得以“读取最新MES事实”为理由重新开放当前全部库存或在途；
- 激活前比较MESPlanRelease.RowVersion、ExecutionLock.RowVersion、HardLock.RowVersion及实际消耗事实，变化则拒绝激活。

### 3.6 容错与监控

| 检查 | 处理 |
|---|---|
| 同一ReleaseItemKey返回多个MESWorkOrderNo | 阻断该发布承接并告警，不自动选择 |
| MES工单无ReleaseItemKey | 历史/外部工单可按PI+工单号识别；APS新发布工单登记接口缺陷 |
| SourceUpdatedAt缺失 | 记录查询开始/结束时间、来源视图和条件；不伪造来源时间 |
| 三类快照数量矛盾 | StageProgress为大工艺位置权威，OperationProgress只裁剪并登记Issue |
| MES视图暂时不可用 | 当前运行对应Domain失败或Candidate拒绝生成；不得拿空集合继续排程 |
| PUBLISHED长期未被MES承接 | 看板告警；不得创建第二个ReleaseItemKey绕过 |

### 3.7 MES接口监控指标

- 发布待承接数量与最长等待时间；
- ReleaseItemKey重复工单数；
- MES视图读取耗时、行数和最大SourceUpdatedAt；
- ExecutionLock数量闭合异常数；
- 状态4短量完结数量；
- 工单/Operation/Stage快照差异数；
- Candidate因现实事实变化被拒绝激活次数。

## 四、采购系统集成接口设计（⚠️ v1.19 重对齐管道供给统一链路）

> ⚠️ **v1.19 架构修正**：V1 旧版将采购在途直接写入 `Inventory` 表（`UPDATE Inventory SET InTransitQty=...`），
> 与 V1 已确立的"**管道供给独立于 `InventoryBalance`**"架构冲突。
> 本节已统一改为通过 `SupplyFact_Pipeline` 承载采购在途管道供给。

### 4.1 统一管道供给接口（V1正式主链）

采购在途、厂间在途、VMI和已到厂未入库采用**同构管道供给链**：

```text
各来源业务系统
  → 各自ODS契约视图（5号位/来源系统维护）
  → APS单来源跨库包装视图（2号位）
  → ext_PipelineSupply_Source_View UNION ALL
  → sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime)
  → SupplyFact_Pipeline
  → ScheduleContext.PipelineSupplies
```

V1当前口径：
- `ext_PipelineSupply_Source_View`是统一输入合同；已接入来源返回真实数据，尚未接入的单一来源分支允许用`WHERE 1=0`返回0行；
- `sp_SyncPipelineSupply`必须读取统一输入视图，按`DataCutoffTime`截断、按稳定来源业务键去重、执行物料/工厂映射及规则裁决，并且只替换本次`BatchNo`切片；
- 禁止把整个管道主链实现成“TRUNCATE全表后固定空集合”；某一来源0行是合法结果，不代表全部`PipelineSupplies`固定为空；
- 管道供给不写入`InventoryBalance`，而是进入独立的`SupplyFact_Pipeline`，与现货库存六层链严格分离；
- `Received`继续走按单据严格消费的独立链路，不并入通用Pipeline池。

### 4.2 新增采购来源的契约扩展模式

采购来源尚未接入时可保留0行分支；接入时只替换该分支，不修改统一SP的主流程：

```sql
-- 将统一输入视图中的采购来源0行分支替换为真实APS包装视图
-- 单来源包装视图保持14个业务字段；统一输入视图追加SourceSystem形成15列
SELECT
    MasterID,
    MaterialCode,
    NULL                    AS SourceFactoryCode,
    TargetFactoryCode       AS FactoryCode,
    'PURCHASE_IN_TRANSIT'   AS SupplyType,
    'OWNED'                 AS OwnershipType,
    'AVAILABLE'             AS QualityStatus,
    InTransitQty            AS Quantity,
    PromisedDeliveryDate    AS ETA,
    StorageCode,
    SupplierCode,
    PONo                    AS SourceDocumentNo,
    POLineNo                AS SourceDocumentLineNo,
    UpdatedAt               AS SourceUpdatedAt,
    'PROCUREMENT'           AS SourceSystem
FROM ext_ERP_PurchaseInTransit_View;
```

**采购在途StorageCode要求**：采购在途同样参与`MasterID + StorageCode → MaterialMapping.Warehouse_Norm`多仓映射。若采购系统未直接提供目的仓库，ODS必须根据收货工厂和收货库位推导稳定`StorageCode`；多仓环境禁止只按MasterID降级匹配。

扩展红线：
- 新来源只增加ODS契约视图和统一包装视图分支，不在`sp_SyncPipelineSupply`中为各来源编写分叉流程；
- 所有分支列名、顺序和类型必须完全同构；
- `SourceDocumentNo + SourceDocumentLineNo + SourceMasterID + StorageCode + FactoryCode`等字段共同形成稳定来源身份；
- 不得恢复`UPDATE Inventory SET InTransitQty`旧模式。

---

## 五、OA审批系统集成接口设计

### 5.1 OA系统API规范（假设）

#### 5.1.1 创建审批流实例

**请求**：
```http
POST https://oa.example.com/api/v1/workflow/instances
Content-Type: application/json
Authorization: Bearer {access_token}

{
  "workflowCode": "APS_FROZEN_CHANGE",
  "title": "冻结区变更审批 - 订单SO202602260001",
  "applicant": {
    "userId": "planner001",
    "userName": "张三"
  },
  "approvers": [
    {
      "level": 1,
      "role": "WORKSHOP_SUPERVISOR",
      "scope": "Factory1"
    },
    {
      "level": 2,
      "role": "FACTORY_MANAGER",
      "scope": "Factory1"
    }
  ],
  "formData": {
    "orderNo": "SO202602260001",
    "changeReason": "客户紧急插单",
    "impactedOrders": ["MTS202602250010", "MTS202602250011"],
    "requestedBy": "planner001",
    "requestedAt": "2026-02-26T10:30:00Z"
  },
  "callbackUrl": "https://aps.example.com/api/v1/approval/callback",
  "timeoutHours": 24,
  "timeoutAction": "AUTO_APPROVE"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "workflowInstanceId": "WF202602260001",
    "status": "PENDING",
    "createdAt": "2026-02-26T10:30:05Z",
    "currentApprover": {
      "userId": "supervisor001",
      "userName": "李四",
      "role": "WORKSHOP_SUPERVISOR"
    }
  }
}
```

#### 5.1.2 查询审批流状态

**请求**：
```http
GET https://oa.example.com/api/v1/workflow/instances/{workflowInstanceId}
Authorization: Bearer {access_token}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "workflowInstanceId": "WF202602260001",
    "status": "APPROVED",
    "approvalRecords": [
      {
        "level": 1,
        "approverUserId": "supervisor001",
        "approverName": "李四",
        "action": "APPROVE",
        "comment": "同意变更",
        "actionAt": "2026-02-26T11:00:00Z"
      },
      {
        "level": 2,
        "approverUserId": "manager001",
        "approverName": "王五",
        "action": "APPROVE",
        "comment": "同意",
        "actionAt": "2026-02-26T11:30:00Z"
      }
    ],
    "completedAt": "2026-02-26T11:30:05Z"
  }
}
```

#### 5.1.3 审批回调接口（APS侧提供）

**OA系统回调APS**：
```http
POST https://aps.example.com/api/v1/approval/callback
Content-Type: application/json
Authorization: Bearer {callback_token}

{
  "workflowInstanceId": "WF202602260001",
  "status": "APPROVED",
  "finalApprover": {
    "userId": "manager001",
    "userName": "王五"
  },
  "completedAt": "2026-02-26T11:30:05Z"
}
```

### 5.2 OA Adapter实现（APS侧）

```csharp
public class OAApprovalAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    public async Task<string> CreateApprovalAsync(ApprovalInstance instance)
    {
        var request = new
        {
            workflowCode = GetWorkflowCode(instance.FlowId),
            title = $"{instance.FlowId} - {instance.InstanceNo}",
            applicant = new
            {
                userId = instance.RequestedBy,
                userName = await GetUserNameAsync(instance.RequestedBy)
            },
            approvers = await GetApproversAsync(instance.FlowId),
            formData = JsonSerializer.Deserialize<object>(instance.RequestData),
            callbackUrl = _config["OA:CallbackUrl"],
            timeoutHours = 24,
            timeoutAction = "AUTO_APPROVE"
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_config["OA:BaseUrl"]}/api/v1/workflow/instances", 
            request
        );

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OAResponse>();

        // 更新ApprovalInstance表（Dapper）
        using var conn = new SqlConnection(_apsConnectionString);
        await conn.ExecuteAsync(@"
            UPDATE ApprovalInstance SET OAWorkflowId = @OAWorkflowId, Status = 'PENDING'
            WHERE Id = @Id
        ", new { OAWorkflowId = result.Data.WorkflowInstanceId, instance.Id });

        return result.Data.WorkflowInstanceId;
    }

    // 审批回调处理（v1.24 修正）
    [HttpPost("api/v1/approval/callback")]
    public async Task<IActionResult> ApprovalCallback([FromBody] OACallbackDto callback)
    {
        using var conn = new SqlConnection(_apsConnectionString);
        await conn.OpenAsync();   // v1.24: 先 Open 再 BeginTransaction

        // v1.24 补 v3: OAWorkflowId 级应用锁 —— 防止两个并发回调同时进入
        //   使用 SQL Server sp_getapplock (Session 模式，超时 10 秒)
        //   连接关闭时自动释放；此处使用应用锁而非新增 DDL 字段
        var lockName = $"ApprovalCallback:{callback.WorkflowInstanceId}";
        var lockResult = await conn.ExecuteScalarAsync<int>(@"
            DECLARE @LockResult INT;
            EXEC @LockResult = sp_getapplock
                @Resource    = @LockName,
                @LockMode    = 'Exclusive',
                @LockOwner   = 'Session',
                @LockTimeout = 10000;
            SELECT @LockResult;
        ", new { LockName = lockName });
        if (lockResult < 0)
            throw new InvalidOperationException(
                $"ApprovalCallback: 获取 OAWorkflowId 应用锁失败 " +
                $"(WorkflowInstanceId={callback.WorkflowInstanceId}, sp_getapplock={lockResult})");

        var instance = await conn.QueryFirstOrDefaultAsync<ApprovalInstance>(
            "SELECT * FROM ApprovalInstance WHERE OAWorkflowId = @WorkflowInstanceId",
            new { callback.WorkflowInstanceId });

        if (instance == null)
            return NotFound();

        // 更新审批状态 + 记录审批结果（Dapper，事务）
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            UPDATE ApprovalInstance SET Status = @Status, CompletedAt = @CompletedAt
            WHERE Id = @Id
        ", new { callback.Status, callback.CompletedAt, instance.Id }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO ApprovalRecord (InstanceId, StepSeq, ApproverUserId, ApproverName, Action, ActionAt)
            VALUES (@InstanceId, 999, @UserId, @UserName, @Action, @ActionAt)
        ", new {
            InstanceId = instance.Id,
            callback.FinalApprover.UserId,
            callback.FinalApprover.UserName,
            Action = callback.Status,
            ActionAt = callback.CompletedAt
        }, tx);
        tx.Commit();

        // v1.24: 审批通过后按业务类型分流
        // 红线 1: 不假定 ApprovalInstance / ApprovalRecord 新增物理列（如 ExecutedAt）
        //         RequestType / CandidatePlanVersionId / VoucherId 从 instance.RequestData
        //         反序列化 ApprovalActionContext DTO 取得，DTO 不落库新表
        // 红线 2: 审批通过不再生成 Recommendation（Recommendation 是审批前产物）
        // 红线 3: callback.Status != "APPROVED" 时不做任何业务动作，不做"业务已执行"标记
        // 红线 4: 幂等由 sp_getapplock（并发保护）+ Activate/Voucher 执行服务自身持久化幂等实现
        //         例如：ActivateCandidatePlanVersionAsync 内检查 PlanVersion.Status == "ACTIVE" 直接返回；
        //         Voucher 执行模块检查 Voucher.Status == "EXECUTED" 直接返回
        if (callback.Status == "APPROVED")
        {
            var ctx = JsonSerializer.Deserialize<ApprovalActionContext>(instance.RequestData);

            // v1.24: 校验 ctx 非空并按 RequestType 校验对应字段
            if (ctx == null)
                throw new InvalidOperationException(
                    $"ApprovalCallback: 无法反序列化 ApprovalActionContext (InstanceId={instance.Id})");
            if (string.IsNullOrEmpty(ctx.RequestType))
                throw new InvalidOperationException(
                    $"ApprovalCallback: ctx.RequestType 不得为空 (InstanceId={instance.Id})");

            switch (ctx.RequestType)
            {
                case "CANDIDATE_ACTIVATION":
                    if (ctx.CandidatePlanVersionId == null)
                        throw new InvalidOperationException(
                            "ApprovalCallback: CANDIDATE_ACTIVATION 场景 CandidatePlanVersionId 不得为空");
                    // Candidate 激活申请通过 → 调用 ActivateCandidatePlanVersionAsync
                    // 该方法内部硬校验（CTP 与 INSERT_IMPACT_ANALYSIS 永远拒绝）+ 自身幂等
                    await _realtimeRunService.ActivateCandidatePlanVersionAsync(
                        ctx.CandidatePlanVersionId.Value);
                    break;

                case "VOUCHER_APPROVAL":
                    if (ctx.VoucherId == null)
                        throw new InvalidOperationException(
                            "ApprovalCallback: VOUCHER_APPROVAL 场景 VoucherId 不得为空");
                    // 其他 Voucher 审批（规则变更、Task 冻结解锁、资源事件 Voucher 等）
                    // 由 2号位 Voucher 执行模块消费；执行模块自身必须幂等
                    await _voucherExecutor.ExecuteAsync(ctx.VoucherId.Value);
                    break;

                // RESCHEDULE_REQUEST: V2/可选扩展，V1 正式主链不处理
                // V1 正式主链：由人工发起先创建 ScheduleRun + Candidate类PlanVersion版本壳（初始BUILDING）；
                // V2/可选："运行前审批"扩展——审批通过后，
                // 再由3号位创建Candidate类PlanVersion版本壳
                // （初始Status=BUILDING）
                // case "RESCHEDULE_REQUEST": ...（V2/可选扩展占位，不在 V1 switch 执行）

                default:
                    // 未知 RequestType 必须明确失败，不得只 Warning 后按成功处理
                    throw new InvalidOperationException(
                        $"ApprovalCallback: 未知 RequestType='{ctx.RequestType}' " +
                        $"(InstanceId={instance.Id}, WorkflowInstanceId={callback.WorkflowInstanceId})");
            }
        }

        // v1.24 补 v3: 不再回填 ApprovalRecord.ExecutedAt（DDL 无此字段）
        //   幂等由 sp_getapplock（并发保护）+ Activate/Voucher 服务自身持久化状态实现
        return Ok();
    }
}

// ============================================================================
// ApprovalActionContext (v1.24 新增)
// 应用传输对象；不落库新数据库表；仅供 OA 回调按业务类型分流使用
// ============================================================================
public class ApprovalActionContext
{
    public string RequestType { get; set; }
        // V1 值域: CANDIDATE_ACTIVATION / VOUCHER_APPROVAL
        // V2/可选扩展: RESCHEDULE_REQUEST（运行前审批，不在 V1 switch 执行）

    public int?  CandidatePlanVersionId { get; set; }
        // 对齐 DDL: PlanVersion.Id INT；仅 CANDIDATE_ACTIVATION 时必填

    public long? VoucherId { get; set; }
        // 仅 VOUCHER_APPROVAL 时必填

    public RealtimeEvaluationRunRequest? RunRequest { get; set; }
        // V2/可选扩展: RESCHEDULE_REQUEST 时填写
}
```

---

## 六、预警通知集成接口设计

### 6.1 邮件网关API

**请求**：
```http
POST https://mail-gateway.example.com/api/send
Content-Type: application/json
Authorization: Bearer {api_key}

{
  "to": ["planner001@example.com", "supervisor001@example.com"],
  "cc": [],
  "subject": "【APS预警】订单SO202602260001延期风险",
  "body": "<html><body><h3>预警详情</h3><p>订单SO202602260001存在延期风险...</p></body></html>",
  "isHtml": true,
  "priority": "HIGH"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Email sent successfully",
  "data": {
    "messageId": "MSG202602260001"
  }
}
```

### 6.2 短信网关API

**请求**：
```http
POST https://sms-gateway.example.com/api/send
Content-Type: application/json
Authorization: Bearer {api_key}

{
  "phoneNumbers": ["+86138****0001", "+86139****0002"],
  "content": "【APS预警】订单SO202602260001存在延期风险，请及时处理。",
  "templateCode": "APS_ALERT_001"
}
```

### 6.3 通知Adapter实现

```csharp
public class NotificationAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public async Task SendEmailAsync(AlertInstance alert, List<string> recipients)
    {
        var request = new
        {
            to = recipients,
            subject = $"【APS预警】{alert.AlertMessage}",
            body = GenerateEmailBody(alert),
            isHtml = true,
            priority = alert.Severity == "CRITICAL" ? "HIGH" : "NORMAL"
        };

        var response = await _httpClient.PostAsJsonAsync(
            _config["Email:GatewayUrl"], 
            request
        );

        // 记录发送日志
        await LogNotificationAsync(alert.Id, "EMAIL", recipients, response.IsSuccessStatusCode);
    }

    public async Task SendSMSAsync(AlertInstance alert, List<string> phoneNumbers)
    {
        var request = new
        {
            phoneNumbers = phoneNumbers,
            content = alert.AlertMessage,
            templateCode = "APS_ALERT_001"
        };

        var response = await _httpClient.PostAsJsonAsync(
            _config["SMS:GatewayUrl"], 
            request
        );

        await LogNotificationAsync(alert.Id, "SMS", phoneNumbers, response.IsSuccessStatusCode);
    }
}
```

---

## 七、集成容错与监控

### 7.1 重试策略

```csharp
public class IntegrationRetryPolicy
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    Log.Warning($"Retry {retryCount} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                }
            );
    }
}
```

### 7.2 集成监控指标

| 指标 | 说明 | 告警阈值 |
|------|------|---------|
| **ERP同步延迟** | SyncCheckpoint最后一次同步时间距现在的分钟数 | >65分钟（超过1个周期） |
| **Staging表积压** | ERP_Order_Staging表中SyncStatus='PENDING'的记录数 | >500条 |
| **Staging失败率** | ERP_Order_Staging表中SyncStatus='FAILED'的记录占比 | >5% |
| **OA审批超时率** | 超过24小时未审批的比例 | >20% |
| **邮件发送失败率** | 最近1小时邮件发送失败比例 | >5% |

### 7.3 集成健康检查

```csharp
public class IntegrationHealthCheck : IHealthCheck
{
    private readonly string _erpConnectionString;
    private readonly HttpClient _oaHttpClient;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, bool>();

        // 检查ERP连接
        try
        {
            using var conn = new SqlConnection(_erpConnectionString);
            await conn.OpenAsync(cancellationToken);
            checks["ERP_Connection"] = true;
        }
        catch
        {
            checks["ERP_Connection"] = false;
        }

        // 检查OA API
        try
        {
            var response = await _oaHttpClient.GetAsync("/api/health", cancellationToken);
            checks["OA_API"] = response.IsSuccessStatusCode;
        }
        catch
        {
            checks["OA_API"] = false;
        }

        var allHealthy = checks.Values.All(v => v);
        return allHealthy 
            ? HealthCheckResult.Healthy("All integrations healthy", checks)
            : HealthCheckResult.Degraded("Some integrations unhealthy", data: checks);
    }
}
```

---

## 八、集成接口总结

### 8.1 接口清单

| 接口 | 类型 | 实现方 | 状态 |
|------|------|--------|------|
| v_APS_SalesOrder | 数据库视图 | ERP侧 | 待实现 |
| v_APS_BOM | 数据库视图 | ERP侧 | 待实现 |
| ~~v_APS_Routing~~ | 数据库视图 | ERP侧 | v5.0废弃，拆分为下方3个视图 |
| MES_APS_Routing_Operation_View | ODS视图 | 3号位 | v5.0新增 |
| MES_APS_Routing_Dependency_View | ODS视图 | 3号位 | v5.0新增 |
| APS_OperationResourceEligibility_View | ODS视图 | 3号位 | v5.0新增 |
| MES_APS_Resource_View | ODS视图 | MES DBA | v5.0新增；v1.11 命名统一（原名 `APS_Resource_View`） |
| EAM_APS_Resource_View（预留）| ODS视图 | EAM DBA | 未来 EAM 上线时同构新增 |
| v_APS_Inventory | 数据库视图 | ERP侧 | 待实现 |
| t_APS_PromisedDate | 数据库表 | ERP侧 | 待实现 |
| **ERP_InterplantInTransit_View** | **ODS契约视图** | **5号位（ODS实现）** | **14字段合约；`MasterID`=物料映射主字段，`FactoryCode`=目的工厂，`Quantity`=剩余在途数量；来源未接入时可返回0行，但统一Pipeline主链不得整体硬编码为空** |
| ext_ERP_ERPOrderSync_CdcWrap | CDC包装视图 | 5号位 | ；（v5.0废弃） |
| ext_v_APS_SalesOrder | ODS包装视图 | 5号位 | v1.3新增（替代CDC） |
| **ext_ERP_InterplantInTransit_View** | **APS跨库包装视图** | **2号位** | **显式列字段，禁止 SELECT *；来源未接入时允许0行，接入后进入统一Pipeline主链** |
| v_APS_PurchaseOrder（⚠️ V1.1/V2 管道供给同构化） | ODS契约视图 | 5号位 + 采购DBA | V1.1/V2 新建；进入ext_PipelineSupply_Source_View UNION ALL → sp_SyncPipelineSupply → SupplyFact_Pipeline；不再UPDATE Inventory |
| OA审批API | REST API | OA系统侧 | 待对接 |
| 审批回调API | REST API | APS侧 | 待实现 |
| 邮件网关API | REST API | 第三方 | 待对接 |
| 短信网关API | REST API | 第三方 | 待对接 |
| `sp_ExpandBOMRealtime_vNext` | 存储过程 | 5号位 / DBA | v5.0.26 起正式；v1.24 明确 RequestDetail 入口 |
| `sp_GenerateBOMCrossFactoryEdgeRealtime` | 存储过程 | 5号位 / DBA | v5.1.0 新增 |
| `MES_API_BOM_Request_Realtime` | ODS 表 | 5号位 / DBA | v5.0.33 起 RequestDetail 级；v1.24 READY 权威 |
| `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime` | ODS 表 | 5号位 / DBA | v5.1.0 新增 |
| `CreateRealtimeEvaluationRunAsync` | 内部服务 | 3号位 | v1.24 新增；见 §11.1 |
| `PrepareRealtimeOrderSnapshotAsync` | 内部服务 | 2号位 | v1.24 新增；见 §11.1 |
| `EnsureRealtimeBomReadyAsync` | 内部服务 | 2号位 | v1.24 新增；见 §11.1 |
| `PullRealtimeBOMResultFromODSAsync` | 内部服务 | 2号位 | v1.24 新增；见 §11.1 |
| `BuildRemainingSupplyContextAsync` | 内部服务 | 2号位 | v1.26按状态恢复重构；见 §11.1.5 |
| `BuildScheduleContextAsync` | 内部服务 | 2号位 | v1.24 新增；见 §11.1 |
| `ActivateCandidatePlanVersionAsync` | 内部服务 | 3号位 | v1.24 新增；内部硬校验；见 §11.1 |

| `APS_MES_PlanRelease_View` | APS发布契约视图 | 2号位提供、MES读取 | v1.28；MES按ReleaseItemKey幂等建单，PUBLISHED/CANCELLED状态须在落工单前复核 |
| `MES_APS_WorkOrder_View` | MES实时工单契约视图 | MES提供、5号位收口 | v1.28；回传ReleaseItemKey与MESWorkOrderNo，驱动MESPlanRelease→CONSUMED及ExecutionLock创建 |
| `MES_APS_OperationProgress_View` / `MES_APS_StageProgress_View` | MES实时累计进度视图 | MES提供、5号位收口 | 2号位按DataCutoffTime生成运行快照，不按事件增量累计 |
| `BuildProductionInstructionSnapshotsAsync` | 内部服务 | 2号位 | v1.26；见 §12.2 |
| `ApplyPeggingDecisions` | Domain内存服务 | 2号位 | v1.26；原子扣减并写Ledger，见 §12.5 |
| `ScheduleTaskDraftsAsync` | 内部服务 | 1号位 | v1.26；返回ScheduledTaskDraft+ComponentShares，见 §12.6 |
| `PersistDomainScheduleResultAsync` | 内部服务 | 2号位 | v1.26；批量持久化，见 §12.7 |
| 三个MES快照独立定时Job | Hangfire Job + 同步SP | 2号位 | 分别生成MES工单、Operation、Stage运行快照；不由两个Orchestrator直接调用 |
| `CreateMesPlanReleasesAsync` | 内部服务 | 2号位 | 由已排定Task生成同PI+Stage发布单元并写MESPlanRelease |
| `ReconcileMesPlanReleasesAsync` | 内部服务 | 2号位 | 按ReleaseItemKey对账MES工单视图，创建ExecutionLock并更新Task |
| `ApplyMesCumulativeExecutionFactsAsync` | 内部服务 | 2号位 | 以MES累计事实覆盖更新ExecutionLock；小工序状态4差额返回PI剩余任务重建 |

### 8.2 开发工作量估算

| 模块 | 工作量（人天） | 负责人 |
|------|---------------|--------|
| ERP Adapter开发 | 8 | 全栈C#开发 |
| 采购Adapter开发 | 3 | 全栈C#开发 |
| OA Adapter开发 | 5 | 全栈C#开发 |
| 通知Adapter开发 | 2 | 全栈C#开发 |
| ERP侧视图/表创建 | 3 | ERP团队 |
| 采购侧视图创建 | 1 | 采购系统团队 |
| OA侧API对接 | 3 | OA系统团队 |
| 集成测试 | 5 | 全员 |
| **合计** | **30人天** | - |

---

## 九、跨厂Pegging接口方向（v1.22 2026-06-23 新增）

### ERP Received 按单据汇总接口

| 项目 | 内容 |
|------|------|
| ODS 视图 | `ERP_Received_ByDocument_View`（MES_Integration） |
| APS 包装视图 | `ext_ERP_Received_ByDocument_View`（APS_Production） |
| 粒度 | 工厂+仓库+物料+单据类型+单据号 |
| DocumentType | SHIPPING_INSTRUCTION / PRODUCTION_INSTRUCTION / UNKNOWN |
| 不保留 | 单据行号、SourceDocumentNo、SourceLineNo、当前剩余量 |
| 业务假设 | 出荷指示号未完成时 ReceivedQty 视为可用供给 |
| V1 状态 | 不建APS本地Received快照表 |

### MES_ProcessCode_View.ERPProperty 契约补充

| 项目 | 内容 |
|------|------|
| ODS 视图 | `MES_ProcessCode_View`（MES_Integration） |
| 新增字段 | `ERPProperty NVARCHAR(20)` — v5.0.46 |
| 值域 | M / XC / ZP / BP |
| 来源 | ERP 真实属性，5号位同步透出；不根据 WarehouseRole/ProcessName 推导 |
| 消费方 | 2号位，用于生成物料-工厂 M库判定索引（MaterialCode+FactoryCode→HasMStock） |
| 1号位 | 禁止直接查询 |

### MES_APS_BOM_Workset_CrossFactoryEdge 跨厂边接口

| 项目 | 内容 |
|------|------|
| ODS 表 | `MES_APS_BOM_Workset_CrossFactoryEdge`（MES_Integration） |
| 负责人 | 5号位 |
| 生成 | `sp_GenerateBOMCrossFactoryEdge(@BatchNo)`：StageDetail(EDGE) 按 StageSeq 排序 + LEAD 窗口函数；`FromFactoryCode/ToFactoryCode` 通过 `StageCode→StageDict.FactoryCode` 取得（禁止截 StageCode 前缀） |
| APS 缓存 | `APS_BOM_CROSS_FACTORY_EDGE_RAW`（APS_Production，2号位搬运） |
| 用途 | 供 Pegging 阶段读取跨厂边结构事实；不判断跨厂模式（STAGE_HANDOFF/INTER_FACTORY_ORDER 由2号位+5号位在 Pegging 时裁决） |
| V1 状态 | 只表示结构事实，不生成 Task/ShippingTask |

## 十、规则与参数引擎接口方向（v1.22 2026-06-23 新增）

**定位**：规则参数引擎 API 仅供 APS 内部消费，不作为外部集成接口。以下为 V1 接口方向。

| 接口方向 | 说明 |
|---------|------|
| 查询规则集/参数集/策略包列表 | 获取已发布的规则集、参数集、策略包及版本 |
| 获取当前默认策略包版本 | 按 RunType 查询 `IsDefault=1` 且 `Status=PUBLISHED` 的 StrategyProfileVersion |
| 发布策略包版本 | 3号位将 DRAFT/APPROVED 版本发布为 PUBLISHED |
| 查询指定 ScheduleRun 的策略包版本 | 通过 `ScheduleRun.StrategyProfileVersionId` 读取 → 返回完整 RuleSetVersion + ParameterSetVersion |
| 创建 ScheduleRun 时指定策略包 | 传入 `StrategyProfileVersionId`；如未指定，系统按运行类型自动选择默认 PUBLISHED 版本 |

V1 不展开完整 REST API 明细，不新增审批流闭环接口。

---

## 十一、白天实时评估接口契约（v1.24 新增 2026-07-13）

### 11.1 内部服务接口

#### **11.1.1 CreateRealtimeEvaluationRunAsync**

```csharp
Task<CreateRealtimeEvaluationRunResult> CreateRealtimeEvaluationRunAsync(
    RealtimeEvaluationRunRequest request);

public class RealtimeEvaluationRunRequest
{
    public string  RunType                  { get; set; }  // INSERT_ORDER_WHATIF / LOCAL_RESCHEDULE / MANUAL_RESCHEDULE
    // ⚠️ Purpose 权威来源为 ScopeJson.Purpose（CTP / INSERT_IMPACT_ANALYSIS / INSERT_RESCHEDULE / MANUAL_ADJUSTMENT），不再另设 ScenarioType 字段；DDL Scenario.ScenarioType 值域为 SIMULATION / INSERT_ORDER_WHATIF，与 ScopeJson.Purpose 两级枚举不得混写
    public int    BasePlanVersionId        { get; set; }  // 对齐 DDL：PlanVersion.Id INT
    public long   StrategyProfileVersionId { get; set; }  // 对齐 DDL：StrategyProfileVersion.Id BIGINT
    public string ScopeJson                { get; set; }  // 唯一权威，见 §11.2（固定 11 字段，绝不混入 ExpectedDomainKeys）
    public IReadOnlyList<string>? ExpectedDomainKeys { get; set; }  // 独立属性，绝不塞入 ScopeJson；仅作调用方一致性校验输入：必须恰好 1 项且等于 BasePlanVersion.DomainKey；服务端以 BasePlanVersion.DomainKey 为权威自动生成 ExpectedDomainKeysJson=["BasePlanVersion.DomainKey"]；本 DTO 仅用于白天实时评估（Candidate），不接受 FULL_SCHEDULE（夜间 FULL_SCHEDULE 使用独立的夜间运行创建入口 / 内部编排服务），写入规则见 §11.7
    public string TriggeredBy              { get; set; }
}

public class CreateRealtimeEvaluationRunResult
{
    public int  ScheduleRunId          { get; set; }  // 对齐 DDL：ScheduleRun.Id INT
    public int  CandidatePlanVersionId { get; set; }  // 对齐 DDL：PlanVersion.Id INT
    public int? ScenarioId             { get; set; }  // 对齐 DDL：Scenario.Id INT
}
```

**红线**：**不同时**传入 `orderCanonicalIds` 和 `ScopeJson`；OrderCanonicalIds 只从 `ScopeJson.OrderCanonicalIds` 取得。

**职责**：3号位归一化 ScopeJson 并落库；创建 Scenario（适用时）+ ScheduleRun（BasePlanVersionId + StrategyProfileVersionId + ScopeJson + ExpectedDomainKeysJson + DataCutoffTime）+ Candidate类PlanVersion版本壳（初始Status=BUILDING）。

---

#### **11.1.2 PrepareRealtimeOrderSnapshotAsync**

```csharp
Task PrepareRealtimeOrderSnapshotAsync(
    int    candidatePlanVersionId,   // 对齐 DDL：PlanVersion.Id INT
    int    basePlanVersionId,        // 对齐 DDL：PlanVersion.Id INT
    string scopeJson);
```

**只三个参数**。目标订单从 `scopeJson.OrderCanonicalIds` 取得（**不接受额外的 targetOrderCanonicalIds 参数**）。

**职责**（2号位）：读取 Base 版本 Order + 最新 `Order_Canonical`；生成 Candidate PlanVersion 独立 Order 快照；不修改 Base 版本 Order；不直接消费 `Order_Canonical`。

---

#### **11.1.3 EnsureRealtimeBomReadyAsync**

```csharp
Task<EnsureRealtimeBomReadyResult> EnsureRealtimeBomReadyAsync(
    long requestDetailId);   // 对齐 DDL：MES_API_BOM_Request_Detail.Id BIGINT

public class EnsureRealtimeBomReadyResult
{
    public long      RequestId        { get; set; }  // MES_API_BOM_Request_Realtime.Id BIGINT
    public string    Status           { get; set; }  // READY / FAILED
    public string    ErrorMessage     { get; set; }
    public DateTime? CompletedTime    { get; set; }
    public int?      ExpandedRowCount { get; set; }  // 仅诊断
}
```

**职责**（2号位）：

1. 使用 §11.3 SQL 查询**最新一条** `MES_API_BOM_Request_Realtime` 记录（TOP 1 + `ORDER BY Id DESC`）
2. 按最新记录状态分派：
   - `READY` → 直接返回（`Status=READY`，携带该记录 `Id / CompletedTime / ExpandedRowCount`）
   - `PROCESSING` → **只轮询，不重复调用 SP**（SP 已在执行）
   - `FAILED` → **V1 不自动重试**：直接返回 FAILED；仅在用户明确执行"重试"操作时，创建**新的** Request 记录（不复用旧行）
   - 不存在或需首次触发 → 调用 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)`（`@BOMNO` 可空），产生新的 Request 行
3. 轮询上限：应用层最大 5 分钟
4. **超时收口**（红线）：不使用 `RequestDetailId` 批量更新；只对**当前最新一条 Request 记录的 `Id`** 执行 `UPDATE MES_API_BOM_Request_Realtime SET Status='FAILED', ErrorMessage='应用层等待超时' WHERE Id = @LatestRequestId AND Status='PROCESSING'`

**红线**：
- SP 内部异常由 `BEGIN CATCH` 写 FAILED（DDL 层已实现）
- `PROCESSING` 状态**不重复调用 SP**，只轮询
- 超时按**最新 Request 记录的 Id** 更新 FAILED，**不按 RequestDetailId 批量更新**（保护并发场景下的其他 Request 行）
- `Status='READY'` 为唯一权威；`ExpandedRowCount` 仅诊断

---

#### **11.1.4 PullRealtimeBOMResultFromODSAsync**

```csharp
Task PullRealtimeBOMResultFromODSAsync(
    long requestDetailId,        // 对齐 DDL：MES_API_BOM_Request_Detail.Id BIGINT
    int  candidatePlanVersionId);// 对齐 DDL：PlanVersion.Id INT
```

**两个参数**。**禁止内部查询最新 PlanVersion**——`candidatePlanVersionId` 必须由调用方显式传入。

**职责**（2号位）：将 ODS 库三张 Realtime 表搬运到 APS 库三张 RAW 表，统一写入 `BatchNo = RT:RD:{RequestDetailId}`：
- `MES_APS_BOM_Workset_Realtime` → `APS_BOM_RAW`
- `MES_APS_BOM_Workset_StageDetail_Realtime` → `APS_BOM_STAGE_PATH_RAW`
- `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime` → `APS_BOM_CROSS_FACTORY_EDGE_RAW`

搬运完成后生成 `OrderBomRequestLink`。`BatchNo` 是**实时切片标识**，不是夜间批次号。

---

#### **11.1.5 BuildRemainingSupplyContextAsync（v1.26状态恢复版）**

```csharp
Task<RemainingSupplyContext> BuildRemainingSupplyContextAsync(
    int candidatePlanVersionId);   // PlanVersion.Id INT
```

**职责**（2号位）：读取Candidate对应的Base ACTIVE运行切片，不修改Base结果，按下列状态构建独立的Candidate供给上下文：

| Base状态 | Candidate处理 |
|---|---|
| 实际已消耗 | 永久扣除，不可恢复 |
| `DemandSupplyHardLock` | 只恢复给原需求，不进入普通竞争池 |
| `ExecutionLock`剩余投入需求 | 作为固定执行节点的优先投入需求；已领料/已实物预留来源不可释放 |
| `ExecutionLock`未来产出 | 以固定PI、Stage、MES工单、数量和AvailableTime恢复；无HardLock部分可在Scope内重新Pegging |
| Scope外SOFT分配 | 暂时保留，Candidate不得越界扰动 |
| Scope内SOFT分配 | 释放后按本次最新优先级重新Pegging |
| Base未分配供给 | 正常进入Candidate可用池 |

**管道供给**：按BasePlanVersion对应`SourceScheduleRun`的精确非空`BatchNo/DataCutoffTime`切片读取；某一来源返回0行合法，但不得将整个Pipeline硬编码为空。

**输出必须携带**：
- `BasePlanVersionId / CandidatePlanVersionId / BaseScheduleRunId / DataCutoffTime`；
- 每项供给的稳定业务键、来源类别、可用时间、总量、已消耗量、Hard量、Scope外Soft量、可释放量；
- ExecutionLock和HardLock的版本/更新时间戳，用于激活前并发校验。

**红线**：Candidate不回写`InventoryBalance`，不修改Base Ledger、PeggingSupplyAllocation、ExecutionLock或HardLock；不同Candidate相互隔离。

---

#### **11.1.6 BuildScheduleContextAsync**

```csharp
Task<ScheduleContext> BuildScheduleContextAsync(
    int candidatePlanVersionId);   // 对齐 DDL：PlanVersion.Id INT
```

**职责**（2号位）：装载 ScheduleContext 内存对象（Candidate Order 快照、剩余供给、BOM 切片、Routing、资源约束、跨厂边、规则参数结果）。ScheduleContext 是运行期内存对象，不落库。

---

#### **11.1.7 ActivateCandidatePlanVersionAsync（内部硬校验）**

```csharp
public async Task ActivateCandidatePlanVersionAsync(int candidatePlanVersionId)
{
    // 所有权威校验在 Serializable 事务内执行；事务外不做任何判断
    using var tx = await _dbContext.Database.BeginTransactionAsync(
        IsolationLevel.Serializable);
    try
    {
        // 校验 1: 加锁读取 Candidate
        var pvTx = await _planVersionRepo.GetByIdWithUpdateLockAsync(
            candidatePlanVersionId, tx: tx);
        if (pvTx == null)
            throw new InvalidOperationException(
                $"激活失败: PlanVersion 不存在 (Id={candidatePlanVersionId})");

        // 校验 2: 幂等 —— 已经 ACTIVE 直接返回
        if (pvTx.Status == "ACTIVE")
        {
            await tx.CommitAsync();
            return;
        }

        // 校验 3: 必须为 CANDIDATE
        if (pvTx.Status != "CANDIDATE")
            throw new InvalidOperationException(
                $"激活失败: PlanVersion.Status 必须为 CANDIDATE, 当前={pvTx.Status}");

        // 校验 4: SourceScheduleRunId 必须存在
        if (pvTx.SourceScheduleRunId == null)
            throw new InvalidOperationException(
                "激活失败: SourceScheduleRunId 必须存在");

        // 校验 5: 事务内加锁读取 ScheduleRun，从 runTx.ScopeJson 解析 Purpose
        var runTx   = await _scheduleRunRepo.GetByIdWithUpdateLockAsync(
            pvTx.SourceScheduleRunId.Value, tx: tx);
        if (runTx == null)
            throw new InvalidOperationException(
                $"激活失败: ScheduleRun 不存在 (Id={pvTx.SourceScheduleRunId.Value})");
        var scope   = JsonSerializer.Deserialize<ScopeJson>(runTx.ScopeJson);
        var purpose = scope?.Purpose;
        var runType = runTx.RunType;

        // 校验 6: CTP 永远拒绝
        if (purpose == "CTP")
            throw new InvalidOperationException(
                "激活拒绝: CTP 承诺交期评估永远不得激活");

        // 校验 7: INSERT_IMPACT_ANALYSIS 永远拒绝
        if (purpose == "INSERT_IMPACT_ANALYSIS")
            throw new InvalidOperationException(
                "激活拒绝: 插单影响分析永远不得激活");

        // 校验 8: Purpose 必填
        if (string.IsNullOrEmpty(purpose))
            throw new InvalidOperationException(
                "激活拒绝: Purpose 必填，不得为空");

        // 校验 9: 精确校验 RunType + Purpose 合法组合（三个精确组合）
        var allowed =
               (runType == "LOCAL_RESCHEDULE"  && purpose == "INSERT_RESCHEDULE")
            || (runType == "LOCAL_RESCHEDULE"  && purpose == "MANUAL_ADJUSTMENT")
            || (runType == "MANUAL_RESCHEDULE" && purpose == "MANUAL_ADJUSTMENT");
        if (!allowed)
            throw new InvalidOperationException(
                $"激活拒绝: 不支持的 RunType/Purpose 组合 " +
                $"(RunType={runType}, Purpose={purpose})");

        // 校验 10: 事务内确认审批通过
        var approval = await _approvalRepo.GetLatestWithUpdateLockAsync(
            candidatePlanVersionId, "CANDIDATE_ACTIVATION", tx: tx);
        if (approval == null || approval.Status != "APPROVED")
            throw new InvalidOperationException(
                "激活失败: 审批必须通过");

        // 校验 11: 读取 BasePlanVersion，Candidate.DomainKey 必须非空且等于 BasePlanVersion.DomainKey
        if (runTx.BasePlanVersionId == null)
            throw new InvalidOperationException("激活失败: 白天Candidate运行必须具有BasePlanVersionId");
        var basePv = await _planVersionRepo.GetByIdWithUpdateLockAsync(
            runTx.BasePlanVersionId.Value, tx: tx);
        if (basePv == null)
            throw new InvalidOperationException(
                $"激活失败: BasePlanVersion 不存在 (Id={runTx.BasePlanVersionId})");
        if (string.IsNullOrEmpty(pvTx.DomainKey))
            throw new InvalidOperationException(
                "激活拒绝: Candidate.DomainKey 不得为空");
        if (pvTx.DomainKey != basePv.DomainKey)
            throw new InvalidOperationException(
                $"激活拒绝: Candidate.DomainKey({pvTx.DomainKey}) " +
                $"与 BasePlanVersion.DomainKey({basePv.DomainKey}) 不一致");

        // 归档同 DomainKey 下所有旧 ACTIVE（带更新锁；可能多条，一律归档）
        var oldActives = await _planVersionRepo.GetActiveByDomainKeyWithUpdateLockAsync(
            pvTx.DomainKey, tx: tx);
        foreach (var old in oldActives)
        {
            if (old.Id == candidatePlanVersionId) continue;
            old.Status     = "ARCHIVED";
            old.ArchivedAt = DateTime.UtcNow;
            await _planVersionRepo.UpdateAsync(old, tx: tx);
        }

        // 激活 Candidate（与旧 ACTIVE 归档在同一事务提交）
        pvTx.Status      = "ACTIVE";
        pvTx.ActivatedAt = DateTime.UtcNow;
        pvTx.ActivatedBy = approval.FinalApproverUserId;
        await _planVersionRepo.UpdateAsync(pvTx, tx: tx);

        await tx.CommitAsync();
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
}
```

**激活边界**：

| Purpose | RunType | 允许激活 |
|---------|---------|---------|
| CTP | INSERT_ORDER_WHATIF | ❌ 永远拒绝 |
| INSERT_IMPACT_ANALYSIS | INSERT_ORDER_WHATIF | ❌ 永远拒绝 |
| INSERT_RESCHEDULE | LOCAL_RESCHEDULE | ✅ 审批后 |
| MANUAL_ADJUSTMENT | LOCAL_RESCHEDULE | ✅ 审批后 |
| MANUAL_ADJUSTMENT | MANUAL_RESCHEDULE | ✅ 审批后 |

---

### 11.2 ScopeJson 唯一权威 Schema

**Schema**（不得新增字段）：

> ⚠️ **适用范围**：本 §11.2 ScopeJson Schema **仅适用于白天实时评估（`CreateRealtimeEvaluationRunAsync`）与 Candidate 运行**。`FULL_SCHEDULE`（夜间全量排程）不要求填写白天 Purpose，其 `ScopeJson` **可为 NULL**，预期域集合由 §11.7 的 `ExpectedDomainKeysJson` 独立承载，不得塞入 ScopeJson。

```json
{
  "Purpose": "CTP|INSERT_IMPACT_ANALYSIS|INSERT_RESCHEDULE|MANUAL_ADJUSTMENT",
  "OrderCanonicalIds": [],
  "FactoryIds": [],
  "ProductFamilyIds": [],
  "ResourceGroupIds": [],
  "PlanHorizonStart": null,
  "PlanHorizonEnd": null,
  "LockedTaskIds": [],
  "AllowTouchFrozenZone": false,
  "AllowDelaySalesOrder": false,
  "MaxImpactedOrders": null
}
```

**类型**：

| 字段 | .NET 类型 |
|------|-----------|
| Purpose | `string`（枚举，四选一）|
| OrderCanonicalIds | `long[]` |
| FactoryIds | `int[]` |
| ProductFamilyIds | `int[]` |
| ResourceGroupIds | `int[]` |
| PlanHorizonStart | `DateTime?` |
| PlanHorizonEnd | `DateTime?` |
| LockedTaskIds | `long[]` |
| AllowTouchFrozenZone | `bool`（默认 false）|
| AllowDelaySalesOrder | `bool`（默认 false）|
| MaxImpactedOrders | `int?` |

共 **11 个字段**（Purpose + 4 个业务范围数组 + 1 个 LockedTaskIds 数组 + 2 个时间窗口 + 2 个开关 + 1 个上限）。

**校验规则**（3号位在创建白天实时评估 / Candidate 的 ScheduleRun 前执行；FULL_SCHEDULE 的 ScopeJson 为 NULL，本规则不适用）：

1. **Purpose 必填**，值域固定四选一
2. **冲突拒绝**：入口便捷参数（页面 OrderCanonicalId / FactoryId 等）必须先归一化到 ScopeJson；参数与已有 ScopeJson 冲突时**拒绝创建 ScheduleRun**，不静默覆盖
3. **不得静默扩大**：ScheduleRun 创建后 ScopeJson 视为不可变；运行过程中不得静默扩大 Scope
4. **ScheduleRun.ScopeJson 是唯一权威**：后续所有 SP 只读取，不修改
5. **Purpose 必填**：Purpose 始终必填，不允许为空
6. **RunType + Purpose 合法组合校验**（仅以下五种组合允许创建运行，其余组合一律拒绝）：
   - `INSERT_ORDER_WHATIF` + `CTP`（允许创建，永不激活）
   - `INSERT_ORDER_WHATIF` + `INSERT_IMPACT_ANALYSIS`（允许创建，永不激活）
   - `LOCAL_RESCHEDULE`    + `INSERT_RESCHEDULE`（允许创建，审批后可激活）
   - `LOCAL_RESCHEDULE`    + `MANUAL_ADJUSTMENT`（允许创建，审批后可激活）
   - `MANUAL_RESCHEDULE`   + `MANUAL_ADJUSTMENT`（允许创建，审批后可激活，语义为单Domain内较大范围人工重排）
7. **LOCAL_RESCHEDULE 时间窗口必填**：`PlanHorizonStart` 和 `PlanHorizonEnd` 均不得为 null
8. **PlanHorizonEnd 不得早于 PlanHorizonStart**
9. **LOCAL_RESCHEDULE 至少一个范围数组非空**：`OrderCanonicalIds` / `FactoryIds` / `ProductFamilyIds` / `ResourceGroupIds` / `LockedTaskIds` 中至少一个 `Count > 0`
10. **全部范围数组空 + 时间窗口空 → 仅允许经明确审批的 `MANUAL_RESCHEDULE`**（Purpose 仍必须为 `MANUAL_ADJUSTMENT`）
11. **MaxImpactedOrders 有值时必须 > 0**（`<= 0` 拒绝）
12. **达到 MaxImpactedOrders 上限时停止扩大 Scope 并返回提示**，运行过程中不追加受影响订单

**明确禁止的字段**（不得新增）：`FreezeZoneEnd`、`ChangedMaterialCodes`、`ImpactedResourceGroupIds`、`AssumptionSnapshot`、**`ExpectedDomainKeys`**（预期域集合严禁塞入 ScopeJson，改用 §11.7 的 `ExpectedDomainKeysJson`）

**明确禁止的 Purpose 值**：
- ~~`LOCAL_RESCHEDULE.INSERT`~~（错，应用 `INSERT_RESCHEDULE`）
- ~~`LOCAL_RESCHEDULE.MANUAL`~~（错，应用 `MANUAL_ADJUSTMENT`）

---

### 11.3 READY 查询规范

```sql
SELECT TOP (1) Id, Status, ErrorMessage, CompletedTime, ExpandedRowCount
FROM MES_API_BOM_Request_Realtime
WHERE RequestDetailId = @RequestDetailId
ORDER BY Id DESC;
```

**规则**：
- `Status='READY'` 是唯一权威
- `ExpandedRowCount` 仅诊断
- 三张 Realtime 结果表允许 0 行
- `CrossFactoryEdge_Realtime` 为 0 行合法
- 同一 RequestDetailId 存在多条请求时按 `Id DESC` 取最新一条

---

### 11.4 完整白天实时主链（v1.26 TaskDraft与状态恢复版）

```text
PMC / 销售 / 计划员在4号位页面人工发起
→ 3号位归一化ScopeJson并创建ScheduleRun + Candidate PlanVersion(BUILDING)
→ 2号位生成Candidate独立Order快照
→ 2号位完成实时BOM复用/展开、三张RAW和OrderBomRequestLink
→ 2号位调用BuildRemainingSupplyContextAsync
   按已消耗/Hard/ExecutionLock/Scope外Soft/Scope内Soft/未分配分类恢复
→ 2号位装载Domain ScheduleContext（一次装入内存，循环内不查库）
→ 5号位领域模块/策略返回：
   PositionCalculationResult / HardLockEvaluationResult / PeggingAllocationDecision
→ 2号位逐笔验证并原子执行：
   扣减需求余额 + 扣减供给余额 + 写内存PeggingAllocationLedger
→ 2号位形成LogicalBlock / TaskDraft（尚未持久化正式Task）
→ 1号位执行有限产能、交期、资格、批量、合并/拆分和时间排定
   返回ScheduledTaskDraft + ComponentShares + ExplanationFactDraft
→ 2号位批量持久化正式Task、Ledger.FinalTaskId、PeggingSupplyAllocation、物理Pegging和解释事实
→ PlanVersion转CANDIDATE；ScheduleRun进入终态
→ 审批通过且激活组合合法
→ 激活前重新校验DataCutoffTime后实际消耗、ExecutionLock和HardLock是否变化
→ 3号位在Serializable事务中激活Candidate
```

**激活前变化处理**：若发现现实执行或硬归属已变化，必须返回`EXECUTION_FACT_CHANGED_AFTER_CUTOFF`并拒绝激活；由PMC重新创建Candidate，不允许静默覆盖。

---

### 11.5 Result、AllocationDecision、Recommendation与Voucher分流（v1.26）

设备故障、订单变化、主数据变化**不得自动重排**。

#### A. 普通领域计算Result

```text
输入快照/运行时上下文
→ 5号位纯计算
→ PositionCalculationResult / EligibilityResult / PriorityResult /
   HardLockEvaluationResult / ShortageResult / ImpactAssessmentResult
```

Result只表达判断，不直接修改余额、Task、锁或数据库，不需要Voucher。

#### B. 普通Pegging AllocationDecision

```text
5号位返回PeggingAllocationDecision
(DemandKey, SupplyKey, Qty, AllocationMode, Reason, StableSourceKey)
→ 2号位校验Domain/资格/余额/幂等
→ 在同一内存原子步骤扣减需求和供给并写Ledger
```

夜间全量与白天Candidate的普通Pegging均走此路径，**不再使用PeggingVoucher**。

#### C. 异常变化Recommendation

```text
事件或变化事实
→ ImpactAssessmentResult
→ ScheduleExplanationFact（适用时）+ RescheduleRecommendation
→ 看板告警/推荐清单
→ PMC或0号位决定是否创建ScheduleRun
```

此路径不自动暂停Task、不自动重排、不需要Voucher。

#### D. 真正需要Voucher的场景

仅在需要人工审批或正式人工状态变更时使用，例如：
- `ManualFreezeVoucher`；
- `ToleranceClosureVoucher`；
- 人工创建/解除HardLock的`HardLockChangeVoucher`；
- 冻结区例外审批（现有`FreezeZoneVoucher`，适用时）。

设备故障、普通Pegging、PI位置计算、供给排序、缺口计算均不得为了统一形式而Voucher化。

- **CTP**和**INSERT_IMPACT_ANALYSIS**永远不得激活；
- 允许审批后激活的组合仍为：`LOCAL_RESCHEDULE+INSERT_RESCHEDULE`、`LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT`、`MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT`。

---

### 11.6 1–5号位接口职责（v1.26）

| 号位 | 当前权威职责 |
|---|---|
| **1号位** | 只通过`SolveAsync`方法参数消费2号位传入的内存`TaskDraft + Routing + Resource/Calendar + ExecutionLock约束`；执行有限产能、合并/拆分和时间排定；返回内存`FinalTaskDraft + AllocationShares + ExplanationFactDraft`。不读取任何数据库、不扣供给、不持久化正式Task。 |
| **2号位** | 构建PI快照和运行时余额；验证并应用分配判断（现有代码可继续接收`PeggingRuleVoucher`，业务契约等同`PeggingAllocationDecision`）；继续使用`PeggingLedgerEntry`维护内存分配记录；形成LogicalBlock/TaskDraft；批量持久化正式Task、统一Ledger、非Task分配和物理Pegging；维护ExecutionLock/HardLock当前状态；构建Candidate RemainingSupplyContext。2号位是唯一余额修改者。 |
| **3号位** | 管理Scenario/ScheduleRun/PlanVersion、ScopeJson、审批编排与Candidate激活；不参与供需扣减。 |
| **4号位** | 维护RuleSet/ParameterSet/StrategyProfile；展示原因、Recommendation、Candidate和审批入口；不执行排程内核。 |
| **5号位** | 负责BOM/Stage/CrossFactory结构事实及少数领域策略；普通计算返回Result，供需判断可继续返回现有`PeggingRuleVoucher`（其业务契约等同`PeggingAllocationDecision`），异常返回ImpactAssessment/Recommendation；不直接扣余额、不写Ledger、不生成最终Task、不创建ScheduleRun。 |

**V1插件边界**：
- 不扫描插件目录，不支持运行中装卸程序集；
- 规则变化优先通过RuleSet/ParameterSet/StrategyProfile；
- 只有稳定算法差异使用少数.NET接口和依赖注入；
- Task合并/拆分属于1号位有限产能职责，不作为5号位插件；
- 普通Result和AllocationDecision不是Voucher。

---

### 11.7 ExpectedDomainKeysJson 写入规则（创建 ScheduleRun 服务）

> ScopeJson（§11.2）保持 11 字段固定 Schema，**禁止**把预期域集合塞入 ScopeJson；预期域集合由独立的 **ExpectedDomainKeysJson** 承载。`RealtimeEvaluationRunRequest` 新增独立属性 `IReadOnlyList<string> ExpectedDomainKeys`（见 §11.1.1），**不得**并入 ScopeJson DTO；落库为 `ScheduleRun.ExpectedDomainKeysJson`（JSON 数组字符串）。

**写入规则（创建 ScheduleRun 服务按运行类型分别执行）**：

- **FULL_SCHEDULE（夜间全量排程）**：从夜间预期域集合生成**多元素 JSON 数组**（≥1 个、**不重复**的字符串域标识）；`ScopeJson` 可为 NULL（夜间全量运行不要求填写白天实时评估 Purpose）。
- **白天 Candidate（LOCAL_RESCHEDULE / MANUAL_RESCHEDULE / INSERT_ORDER_WHATIF）**：从 `BasePlanVersion.DomainKey` 生成**单元素数组** `["BasePlanVersion.DomainKey"]`；并校验其唯一元素 `== BasePlanVersion.DomainKey == CandidatePlanVersion.DomainKey`（与 §11.1.7 校验 11 的 DomainKey 一致性同源，三者必须相等）。
- **SIMULATION（阶段二骨架）**：使用独立的 `ExpectedDomainKeysJson`（1 个或多个域），**不进入 ScopeJson**；骨架仅登记，不参与 V1 正式排程。

**红线**：`ExpectedDomainKeys` 与 `ScopeJson` 互不包含；任何运行类型都不得把 `ExpectedDomainKeys` 写入 ScopeJson，也不得在 ScopeJson 中新增 `ExpectedDomainKeys` 字段（§11.2 明确禁止字段已含 `ExpectedDomainKeys`）。

---

### 11.8 权威 ReasonCode 字典（ScheduleExplanationFact / Voucher 统一）

> 下列为 V1 唯一权威 ReasonCode 取值；任何写入 `ScheduleExplanationFact.ReasonCode` 或 `Voucher.ReasonCode` 的代码**只能**取自此表，禁止自行发明或未登记取码。

| ReasonCode | 含义 |
|------------|------|
| `RESOURCE_CAPACITY_WAIT` | 资源产能等待 |
| `MATERIAL_SHORTAGE` | 物料短缺 |
| `PRECEDENCE_WAIT` | 工序 precedence 等待 |
| `FROZEN_ZONE_LOCK` | 冻结区锁定 |
| `ROUTING_FALLBACK` | 工艺路线降级 |
| `STAGE_LEADTIME_FALLBACK` | 阶段 leadtime 降级 |
| `BOM_DEGRADE` | BOM 降级 |
| `CROSS_ORG_HANDOFF` | 跨组织交接 |
| `PRIORITY_LOWER_THAN_OTHERS` | 优先级低于其他 |
| `DUE_DATE_RISK` | 交期风险（原 `DUE_DATE_VIOLATION` 已废弃统一为 `DUE_DATE_RISK`）|
| `LOGISTICS_DELAY` | 物流延迟 |
| `PRIORITY_INHERITANCE` | 优先级继承 |
| `CROSS_DOMAIN_VERSION_MISMATCH_RISK` | 跨域版本不一致风险（上游延期并入此码，`ObjectType=DOMAIN`）|
| `MANUAL_COMPLETED_SHORT` | 手动完工、数量不足 |
| `EQUIPMENT_BREAKDOWN_RISK` | 设备故障风险 |

**已废止 / 未登记示例（不得出现）**：`DUE_DATE_VIOLATION`、`DUE_DATE_TIGHT`、`UPSTREAM_DELAY`（上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`，`ObjectType=DOMAIN`）、`ALL_OR_NOTHING`。



---


## 十二、Pegging与跨版本执行内部契约（v1.31当前权威）

### 12.1 总体原则

1. `SchedulingOrchestrator`是一个Domain完整计算的总入口。
2. Phase 1.6只生成内存TaskDraft；正式Task在1号位返回最终结果前不得写库。
3. PeggingOrchestrator只做内存供需分配、余额扣减、Ledger Entry和TaskDraft裁剪。
4. 1号位只做有限产能、合并拆分和份额保持，不写数据库、不改变供给归属。
5. 2号位在单Domain显式事务中统一写Task、Ledger、PSA和物理Pegging；PlanVersion激活另事务。
6. Task/Pegging按PlanVersion重建；ExecutionLock、HardLock和现实MES锚点跨版本延续。

### 12.2 关键身份与DTO来源

```csharp
public sealed record PlanVersionInfoDto(
    int PlanVersionId,
    int SourceScheduleRunId,
    string DomainKey);

public sealed record OrderLoadDto(
    long OrderId,
    long OrderCanonicalId,
    string OrderNo);

public sealed record SupplyLoadRow(
    string SupplyType,
    string SupplyBusinessKey,
    string SourceSystem,
    string MaterialCode,
    int? FactoryId,
    decimal AvailableQty,
    DateTime? AvailableAt,
    string? SourceReference,
    long? SupplySourceId);
```

规则：
- `SourceScheduleRunId`只取`PlanVersion.SourceScheduleRunId`；
- `DomainKey`只取`PlanVersion.DomainKey`；
- `OrderCanonicalId`只取`Order.OrderCanonicalId`，不得用`Order.Id`替代；
- 任一必填身份缺失即终止当前Domain，不写0、不查询最新记录；
- 具体仓库、单据、PI等来源字段由各Loader读取并先生成最终SupplyBusinessKey，再投影为通用SupplyLoadRow。

### 12.3 SupplyBusinessKey契约

| SupplyType | 格式 |
|---|---|
| PI | `PI|ProductionInstructionNo` |
| INVENTORY | `INV|ERP|FactoryCode|WarehouseCode|MaterialCode` |
| DOCUMENT | `DOC|ERP|DocumentType|DocumentNo|MaterialCode|DestinationWarehouseCode` |
| PURCHASE_ORDER | `PO|ERP|PurchaseOrderNo|MaterialCode|ReceivingWarehouseCode` |
| EXECUTION_WITHOUT_PI | `EXEC|MES|MESWorkOrderNo` |
| VIRTUAL_PI | `VIRTUAL_PI|RootDemandOrderCanonicalId|MaterialCode|StageCode` |

统一规范：
- Trim、英文代码大写、`|`分隔；
- 不含数量、时间、TaskId、PlanVersionId或数据库自增Id；
- PI号全公司唯一，PI键不带工厂；
- 单据在ODS按单据+物料+目的仓库合并，V1不带行号；
- 虚拟PI不得进入HardLock、ExecutionLock、MES下发或现实PSA；
- Pipeline/Received保留单据身份时用DOC键；进入普通池化库存并失去单据身份后用INV键；严格绑定数量不得提前丢失DOC身份。

### 12.4 5号位Decision与2号位原子应用

现有`PeggingRuleVoucher`可继续使用，业务契约等同AllocationDecision。Decision自身不修改余额。

```csharp
public sealed record PeggingAllocationDecision(
    string DemandKey,
    string SupplyBusinessKey,
    decimal Quantity,
    string AllocationMode,
    string Reason,
    long? PositionSliceId,
    long? ExecutionLockId,
    long? HardLockId);
```

2号位在每个PlanVersion的一次Pegging调用内维护局部计数器：

```csharp
long nextAllocationSequence = 0;
var ledgerEntries = new List<PeggingLedgerEntry>();
```

一次成功应用原子完成：

```text
校验需求余额
+ 校验供给余额
+ 扣需求余额
+ 扣供给余额
+ AllocationSequence=++nextAllocationSequence
+ 追加PeggingLedgerEntry
```

规则：
- 单Domain内按优先级顺序执行；
- 不使用数据库Sequence、不在INSERT时生成、不放全局Context；
- 失败不得留下半笔扣减或正式序号；
- 核心循环不得访问数据库。

### 12.5 PeggingOrchestrator输出契约

```csharp
public sealed record PeggingResult(
    IReadOnlyList<PeggingLedgerEntry> LedgerEntries,
    IReadOnlyList<TaskDraftDto> TaskDrafts,
    IReadOnlyList<PhysicalPeggingDraftDto> PhysicalPeggingDrafts,
    IReadOnlyList<ScheduleIssueDto> Issues);
```

`TaskDraftDto`至少包含：
- TaskDraftKey
- ProductionInstructionNo
- StageCode
- Operation
- Quantity
- DueTime
- Routing / EligibleResources / Dependencies
- Components：`AllocationSequence + ComponentQty`

约束：

```text
SUM(Components.ComponentQty) = TaskDraft.Quantity
```

PeggingOrchestrator不得INSERT/DELETE/UPDATE Task，不直接写Ledger、PSA、物理Pegging或提交事务。

### 12.6 1号位有限产能接口

**内存对象与数据库隔离约束**：本节`TaskDraftDto / FinalTaskDraftDto / AllocationTaskShareDto / DomainSolveRequest / DomainSolveResult`均为进程内DTO，不对应数据库表。`DomainSolveRequest`由2号位从已装载的`ScheduleContext`组装并作为方法参数传入。1号位实现不得引用`DbContext`、Repository、Dapper、`SqlConnection`、数据库事务或任何外部表查询。

```csharp
Task<DomainSolveResult> SolveAsync(
    DomainSolveRequest request,
    CancellationToken ct);

public sealed record DomainSolveRequest(
    int ScheduleRunId,
    int PlanVersionId,
    string DomainKey,
    IReadOnlyList<TaskDraftDto> TaskDrafts,
    RoutingContext Routing,
    ResourceContext Resources,
    SchedulingParameterSnapshot Parameters,
    IReadOnlyList<ExecutionLockConstraintDto> ExecutionLocks);

public sealed record DomainSolveResult(
    bool Success,
    string? Summary,
    int ScheduledCount,
    int UnscheduledCount,
    IReadOnlyList<FinalTaskDraftDto> FinalTasks,
    IReadOnlyList<AllocationTaskShareDto> AllocationShares,
    IReadOnlyList<ExplanationFactDraftDto> ExplanationFacts,
    IReadOnlyList<ScheduleIssueDto> Issues);

public sealed record AllocationTaskShareDto(
    long AllocationSequence,
    string FinalTaskDraftKey,
    decimal ComponentQty);
```

类名允许按代码风格调整，但输出必须同时具备最终Task草稿和份额映射。`FinalTasks`仍是内存对象，不是正式`[Task]`；正式TaskId只在2号位后续统一持久化时生成。

数量约束：
- 每张FinalTask：`SUM(ComponentQty)=FinalTask.Quantity`；
- 每个AllocationSequence拆分/合并前后总ComponentQty不变；
- 1号位不得读取/写入数据库，不得重新做Pegging、改变需求优先级或供给归属。

### 12.7 单Domain统一持久化接口

```csharp
Task PersistDomainResultAsync(
    PlanVersionInfoDto planVersion,
    DomainSolveResult solveResult,
    IReadOnlyList<PeggingLedgerEntry> ledgerEntries,
    IReadOnlyList<PhysicalPeggingDraftDto> physicalPeggingDrafts,
    CancellationToken ct);
```

同一显式数据库事务：

```text
BEGIN
1. 批量INSERT最终Task/ShippingTask
2. 建立FinalTaskDraftKey→TaskId映射
3. 按AllocationShares回填Ledger.FinalTaskId+TaskComponentQty
4. 批量INSERT PeggingAllocationLedger
5. 仅对非Task供给批量INSERT PeggingSupplyAllocation
6. 批量INSERT Task-to-Task Pegging
7. 写同批PI快照/Explanation结果
8. 数量、引用、PlanVersion和Domain完整性校验
COMMIT
```

失败整域回滚，旧ACTIVE和其他Domain不受影响。禁止逐Task/逐Ledger提交；可使用现有Repository、Dapper、EF、TVP或SqlBulkCopy，不强制新增独立Repository层。

PlanVersion仍保持BUILDING，后续独立发布事务完成旧ACTIVE归档和新版本激活。

### 12.8 ExecutionLock最小实现边界

`ExecutionLock`按DDL v5.2.4实现实体、映射、MES工单确认后的创建/更新、次日恢复和`Task.ExecutionLockId`关联。

V1不新增：
- ExecutionLockTaskLink
- 完整事件溯源
- 独立锁服务
- 普通SOFT关系稳定化

### 12.9 库存Quantity接口语义

APS专用ODS `ERP_Inventory_View`字段集合不变：

```text
MaterialCode, MasterID, WarehouseCode, Quantity, FactoryCode, SnapshotTime, IsActive
```

其中：

```text
Quantity = max(0, ERP总量 - WasterQty)
```

`WasterQty`不向APS透传。InventoryFact_ERP及后续各层直接继承该净可用量，不得再次扣减。

ERP不能按客户提供专属库存数量时，无法区分客户的专属仓库由`InventoryAvailabilityRule`整体排除，不新增客户字段。


---

**文档结束**
