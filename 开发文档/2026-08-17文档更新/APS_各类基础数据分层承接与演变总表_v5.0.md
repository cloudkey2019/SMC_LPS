# APS 各类基础数据分层承接与演变总表（定稿版）

**版本**：v3.38  
**日期**：2026-07-31  
**文档性质**：架构总纲级参考文档  
**维护责任人**：2号位（技术负责人）



**v3.38更新内容**（2026-07-31 1号位纯内存接口边界澄清；对齐 核心走查 V3.23 / 防腐层 v1.42 / 集成接口 v1.32 / 字段说明与DDL v5.2.5）：
- 🔒 `TaskDraft / FinalTaskDraft / ScheduledTaskDraft / AllocationShare`全部是内存对象，不新增物理表。
- 🔒 2号位负责数据库和快照读取、`ScheduleContext`/`DomainSolveRequest`组装及最终持久化；1号位只通过方法参数消费内存请求并返回内存结果，零数据库I/O。
- 🔒 正式`[Task]`只能由2号位根据1号位返回的`FinalTaskDraft`在统一Domain事务中实例化落库；“1号位生成Task”仅表示形成内存排程结果。
- 📝 原文“1号位查询Stage/进度/参数表”统一修正为“2号位查询并装载，1号位消费已传入的内存快照或TaskDraft”。

> **v3.38边界声明**：本轮不修改表结构、算法、接口字段或事务顺序，仅明确数据库访问和正式Task持久化归属。

**v3.37更新内容**（2026-07-31 Pegging编码契约与库存净量语义收口；对齐 核心走查 V3.22 / 防腐层 v1.41 / 集成接口 v1.31 / 字段说明与DDL v5.2.4）：
- 🔧 ERP库存ODS契约字段不扩展，`ERP_Inventory_View.Quantity`直接表示扣除`WasterQty`后的APS净可用量；无法按客户区分数量的专属仓库由`InventoryAvailabilityRule`整体排除。
- 🔧 SupplyBusinessKey固定由各Loader生成：PI/INV/DOC/PO/EXEC/VIRTUAL_PI六类最小格式；Pegging循环只消费最终键。
- 🔧 单Domain流程固定为“内存Task骨架→Pegging纯内存分配→1号位有限产能→2号位统一事务持久化”；取消占位Task提前落库和Pegging阶段DELETE/重建。
- 🔧 AllocationSequence采用每PlanVersion局部递增计数器，成功扣减后生成；不使用数据库Sequence、不放全局Context。
- 🔧 1号位接口固定返回最终Task草稿、AllocationSequence数量份额及求解摘要；合并拆分必须数量守恒。
- 🔧 Task、Ledger、非Task分配和物理Pegging在一个Domain显式事务中落盘，PlanVersion激活仍为独立事务。

> **v3.37边界声明**：不新增表和库存字段，不引入新协调平台或复杂Repository体系。

**v3.36更新内容**（2026-07-30 现有代码与目标对象映射澄清；对齐 核心走查 V3.21 / 防腐层 v1.40 / 集成接口 v1.30 / 字段说明与DDL v5.2.3）：
- 🔧 现有`PeggingRuleVoucher`继续作为5号位分配判断对象，在文档业务契约中对应`PeggingAllocationDecision`；不强制改名、不新增平行Decision类。
- 🔧 现有`PeggingLedgerEntry`继续作为内存分配记录，批量映射到数据库`PeggingAllocationLedger`；不新增同义的`PeggingAllocationLedgerDraft`代码类。
- 🔧 现有`PeggingSupplyAllocation`继续保留并收窄为非Task供给分配结果，通过LedgerId关联统一总账；物理`Pegging`仍只保存Task-to-Task血缘。
- 🔧 `ExecutionLock`是当前代码需要补齐的V1最小实体，用于跨版本延续现实MES工单；不新增Link表、完整事件溯源或独立锁平台。

> **v3.36边界声明**：本轮只补现有代码与冻结文档的映射说明，不修改DDL/字段说明，不调整Pegging算法、MES契约、软硬锁规则、Candidate或Task生成流程。

**v3.35更新内容**（2026-07-30 定点补丁；对齐 核心走查 V3.20 / 防腐层 v1.39 / 集成接口 v1.29 / 字段说明与DDL v5.2.3）：
- 🔧 `INightlyBatchOrchestrator`只创建`ScheduleRun`并冻结`ScheduleRunId + DataCutoffTime`；三类MES快照由00:40/00:45/00:50三个独立定时任务生成，`ISchedulingOrchestrator`在02:00只校验和消费快照。
- 🔧 撤销人工短量差额字段：MES工序状态4只表示小工序执行记录人工完结，不改变PI总剩余边界；小工序剩余加工量以`OperationProgressSnapshot`为权威。
- 🔧 `ExecutionLock.RemainingExecutionQty`改为2号位维护的现实工单未来Stage产出承诺上限；整张MES工单终结后的未完成、未正式取消差额返回PI未承诺剩余池。

> **v3.35覆盖声明**：仅修正MES快照调用关系与ExecutionLock数量语义，其余v3.34架构口径保持不变。


**v3.34更新内容**（2026-07-29 第三轮定点修订；对齐 核心走查 V3.19 / 防腐层 v1.38 / 集成接口 v1.28 / 字段说明与DDL v5.2.2）：
- 🔄 APS与MES统一为双向视图：`MESPlanRelease → APS_MES_PlanRelease_View → MES`，以及MES实时工单/Operation/Stage视图→APS运行快照；不建设MQ事件累计、主动REST下发和`MES_Actual_Staging`主链。
- 🆕 `ReleaseItemKey`为跨系统幂等锚点；Task写发布视图时仍PLANNED，MES工单视图确认建单后才创建ExecutionLock并转RELEASED；发布数量采用Stage级执行批次单一流转量，不累加串行小工序Task数量。
- ⚠️ ExecutionLock人工差额数量已由v3.35撤销；HardLock允许部分ReleasedQty且剩余量继续ACTIVE。
- 🔒 PI物理数量身份与Hard/Soft归属正交，ExecutionLock、PUBLISHED MESPlanRelease和普通PI位置切片互斥，不允许同一数量重复进入供给池。
- 🔄 Candidate保持Base库存/在途切片，同时按Candidate DataCutoffTime读取最新MES累计事实形成独立快照。
- 📦 APS_Auth由独立脚本`APS_Auth数据库DDL_v1.0.sql`部署；分区与结果保留策略纳入生产运维边界。

> **v3.34覆盖声明**：以下v3.33及更早版本中的主动MES下发、事件累计、旧Candidate公式和旧版本引用仅供追溯；现行架构、开发和测试一律以v3.34正文为准。

**v3.33更新内容**（2026-07-29 六份文档全局审计修正，对齐 核心走查 V3.18 / 防腐层 v1.37 / 集成接口 v1.27 / 字段说明与DDL v5.2.1）：
- 🔧 Task执行身份新增`ProductionInstructionNo + StageCode`；`MTS_InstructionNo`仅历史兼容，不能作为跨需求合并Task的执行PI权威。
- 🔧 Ledger新增`AllocationSequence`幂等序号与`TaskComponentQty`；`AllocatedQty`承载供需分配，`TaskComponentQty`承载Task—需求份额，视图只汇总后者。
- 🔧 ExecutionLock/HardLock生命周期、MES整项接受、非Task分配一对一持久化和Task.IsLocked概念边界补齐。
- 🔧 管道SupplyType统一为`INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / VMI_ONSITE / ARRIVED_NOT_RECEIVED`。

**v3.32更新内容**（2026-07-28 Pegging数量闭合、TaskDraft、软硬归属与跨版本执行闭环；对齐《APS Pegging与跨版本供给分配详细设计方案 v1.1（V1最小实现版）》及《APS 核心排产全流程走查 V3.17》）：
- 🆕 **生产指示供给快照链**：新增 `ProductionInstructionSupplySnapshot` + `ProductionInstructionPositionSlice`，按 `ScheduleRunId + DomainKey + ProductionInstructionNo` 保存生产指示总量、互斥Stage/XC/在途/未定位位置及数据截止时间；生产指示可生产量唯一口径为 `Order.Quantity - Order.ReceivedQty`，MES/XC/在途只定位，不二次改变总量。
- 🆕 **Stage互斥位置算法**：大工艺累计量以 `StageProgressSnapshot` 为权威；下游超过上游时保守下修下游；中间Stage缺失时采用下游已证明的最小完成量；`ERPProperty=XC` 且映射某Stage时，表示该Stage尚未完成；`UNLOCATED` 从生产指示承载路径最早Stage保守排程。
- 🆕 **统一分配账本**：新增 `PeggingAllocationLedger`，每一笔分配必须同时扣减需求余额与供给余额并写Ledger；Ledger承载需求—供给数量血缘、软/硬分配、位置切片、逻辑块和最终Task组成份额。非Task供给继续写 `PeggingSupplyAllocation`；物理 `Pegging` 仍只表达Task-to-Task血缘。
- 🔄 **Task生成时机重构**：Pegging阶段只形成 `LogicalBlock / TaskDraft`；1号位在有限产能条件下评估合并、拆分和时间排定，返回 `ScheduledTaskDraft + ComponentShares`；2号位随后批量持久化正式Task。V1允许不同需求合并为一个Task，只要产能、交期、资格和批量条件均满足。
- 🆕 **Task—需求份额查询链**：V1不重复新建 `TaskDemandAllocation`表；由Ledger保存`FinalTaskId + DemandOrderCanonicalId + TaskComponentQty`，提供`vw_TaskDemandAllocation`权威查询；`AllocatedQty`只表达供需分配数量。`Task.OrderId`仅作为代表订单/兼容字段，不能表达全部需求归属。
- 🆕 **跨版本执行与归属分离**：新增 `ExecutionLock`（现实MES执行过程）与 `DemandSupplyHardLock`（需求硬归属）。MES工单视图确认建单并进入`RELEASED`时只形成ExecutionLock，不自动把普通需求—供给关系永久固定；普通通用执行产出次日可按最新优先级重新Pegging，特殊出荷指示类型、客户专属、环保/质量资格等才形成HardLock。
- 🔄 **新TaskId恢复方式**：Task/Pegging仍随PlanVersion重新生成；新版本Task通过 `Task.ExecutionLockId`关联同一稳定MES执行事实，不复用旧TaskId、不重复下发MES。一个正式Task最多关联一个ExecutionLock，不同现实MES工单不得在新版本中伪装合并。
- 🔄 **Candidate供给恢复公式**：废止当前正文中的“Base原始供给－Base全部已分配”单一公式；Candidate按实际已消耗、HardLock、ExecutionLock剩余投入/产出、Scope外Soft、Scope内Soft和未分配供给分类重建。Scope内Soft可释放重算，Scope外Soft暂保留，HardLock与实际消耗不得释放。
- 🔄 **虚拟占位Task边界**：无正式生产指示时可生成可排程、不可下发MES的虚拟Task；不形成ExecutionLock、不跨版本保留；正式生产指示到达后次日全量重算自然生成真实Task。
- 🔄 **管道供给由空跑骨架升级为统一主链目标**：厂间在途、采购在途、VMI、已到厂未入库等各自保留ODS契约视图，经APS统一包装视图 `UNION ALL` 标准化，按稳定来源业务键去重后进入 `SupplyFact_Pipeline`；Candidate必须读取Base运行的准确切片，不得全局读取当前全部在途。
- 🔄 **2号位/5号位扩展边界瘦身**：2号位负责稳定机制、余额、Ledger、Task和锁的持久化；5号位负责少数策略接口和普通领域计算，返回只读Result或 `PeggingAllocationDecision`，不直接扣余额、不写最终Task或物理Pegging。普通计算结果不再全部Voucher化，只有正式审批或状态变化使用Voucher。
- 📌 **V1物理瘦身**：不建独立SoftAllocation表、不建DemandPromiseConstraint表、不建ExecutionLockTaskLink表；`Task.ExecutionLockId`直接形成跨版本映射；执行历史只记录创建、取消、完成、人工解除等关键事件，不做完整事件溯源。
- 📌 **保护区域不变**：一Run多Domain PlanVersion、Domain独立发布、`PARTIAL_SUCCESS`、`ExpectedDomainKeysJson`独立字段、ScopeJson固定11字段、白天Candidate严格单Domain、规则参数六表、BOM/Stage RAW、Routing主链、Task五态、设备故障不自动暂停等均不借本轮修改重构。

> **v3.32覆盖声明**：下方历史版本中“V1 PipelineSupplies固定为空”“Candidate=Base原始供给－Base全部已分配”“2号位先实例化正式Task”“RELEASED自动固定原需求—生产指示关系”等旧描述，仅用于版本追溯；当前开发、测试和后续六份文档同步一律以v3.34正文为准。

**v3.30更新内容**（2026-07-14 白天实时评估、Candidate版本与异常变化闭环，对齐 DDL v5.1.0 / 字段说明 v5.1.0 / 防腐层 v1.34 / 集成接口 v1.24）：
- 🆕 白天实时评估统一主链：Scenario（适用时）→ 3号位创建ScheduleRun（写入BasePlanVersionId/StrategyProfileVersionId/ScopeJson/ExpectedDomainKeysJson(["BasePlanVersion.DomainKey"],严格单Domain)/DataCutoffTime）→ 3号位创建Candidate PlanVersion → 2号位构造Candidate独立Order快照 → 实时BOM三表（复用判断+EnsureRealtimeBomReady）→ 三张RAW → RemainingSupplyContext → ScheduleContext → Task/ShippingTask → 有限产能排定 → Candidate结果持久化 → PlanVersion=CANDIDATE → ScheduleRun=COMPLETED（写 CompletedAt）
- 🆕 ScopeJson固定11字段（Purpose/OrderCanonicalIds/FactoryIds/ProductFamilyIds/ResourceGroupIds/PlanHorizonStart/PlanHorizonEnd/LockedTaskIds/AllowTouchFrozenZone/AllowDelaySalesOrder/MaxImpactedOrders）并作为运行范围唯一权威；ScheduleRun创建后ScopeJson不可变，禁止静默扩大Scope
- 🆕 Candidate订单快照：`PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)` 复制Base Order快照 + 仅叠加ScopeJson.OrderCanonicalIds指定订单的最新Order_Canonical变化 → 写入Candidate PlanVersion独立Order分区；禁止修改Base/ACTIVE版本；Order表按PlanVersionId隔离，OrderCanonicalId为稳定关联字段
- 🆕 实时BOM RequestDetail链：2号位先判断BOM切片是否可合法复用；不可复用时创建RequestDetail → `EnsureRealtimeBomReadyAsync(requestDetailId)` → `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` → Workset_Realtime / StageDetail_Realtime / CrossFactoryEdge_Realtime（ODS三张Realtime结果表，均允许0行）→ `PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)` 搬运到三张APS RAW；APS RAW的BatchNo固定为`RT:RD:{RequestDetailId}`；生成OrderBomRequestLink
- 🆕 Candidate剩余供给：`BuildRemainingSupplyContextAsync(candidatePlanVersionId)` 基于 `Base ACTIVE版本原始供给 - Base ACTIVE版本已确认分配/消耗 = Candidate剩余可用供给`；不得重新读取当前全部库存；Candidate不得重用ACTIVE已占用的供给
- 🆕 RunType+Purpose五个精确组合：`INSERT_ORDER_WHATIF+CTP`（永不激活）/ `INSERT_ORDER_WHATIF+INSERT_IMPACT_ANALYSIS`（永不激活）/ `LOCAL_RESCHEDULE+INSERT_RESCHEDULE`（审批后可激活）/ `LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT`（审批后可激活）/ `MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT`（审批后可激活）；CTP和INSERT_IMPACT_ANALYSIS是ScopeJson.Purpose，不是RunType
- 🆕 异常/变化统一闭环：实时视图事实或订单变化事实 → 5号位ImpactAssessment → 返回 Recommendation（及 ScheduleExplanationFact；⚠️ 此处原"Voucher"为历史已废止口径，V1 不落地 TaskPauseVoucher/TaskResumeVoucher）→ 4号位展示 → PMC人工确认 → 3号位创建ScheduleRun；禁止自动重排；ImpactAssessment/Recommendation是规则评估结果对象，不在本文件新增物理表
- 🔄 排程运行编排从"阶段二预留骨架"升级为白天实时正式链路；保留夜间全量既有时序，不修改ScheduleRun/PlanVersion夜间创建时机
- 🔄 1—5号位职责收敛：3号位负责ScopeJson归一化/ScheduleRun生命周期/Candidate PlanVersion创建/审批编排/激活；2号位负责Candidate数据构造/BOM复用判断/RAW搬运/RemainingSupplyContext/Pegging/Task实例化/结果持久化；5号位负责规则计算/BOM展开/ImpactAssessment，返回Recommendation（及 ScheduleExplanationFact；⚠️ 此处原"Voucher"为历史已废止口径，V1 不落地 TaskPauseVoucher/TaskResumeVoucher），不创建ScheduleRun不扣库存不写物理Pegging
- 🔄 Scenario定位更新：Scenario在白天实时评估中已可作为正式业务容器，INSERT_ORDER_WHATIF通常可创建Scenario；MANUAL_RESCHEDULE不要求先建Scenario；SimulationRun/ScenarioObjectiveScore仍为阶段二骨架
- 📌 V1 PipelineSupplies仍为空集合；未来Candidate管道供给必须读取BasePlanVersion对应SourceScheduleRun的准确非空BatchNo切片，禁止全局按IsActive=1读取
- ⚠️ **历史口径（已由v3.34废止）**：v3.30曾从MES_Actual_Staging构造ResourceEventDto；当前改为MES实时资源/工单/进度契约视图与运行快照，不建设MES_Actual_Staging。
- 📌 Order_Canonical.FactoryCode正式来源和排程准入规则已确认；ProcessCode反推路径关闭；当前DDL中FactoryCode TODO桩和ISNULL(f.Id,1)待后续配套修订

**v3.31更新内容**（2026-07-20 夜间 FULL_SCHEDULE 多 Domain 编排 + PARTIAL_SUCCESS + ExpectedDomainKeysJson 冻结，对齐 APS V1 最终决策）：
- 🔄 夜间全量口径升级：一次 FULL_SCHEDULE ScheduleRun 对应**多个 Domain PlanVersion**（每个 DomainKey 一个，经 `SourceScheduleRunId` 关联）；00:38 创建 ScheduleRun 并冻结 `ExpectedDomainKeysJson` → 02:00 按 Domain 创建多个 BUILDING PlanVersion → 各 Domain 独立计算/落盘/发布 → 全部成功 COMPLETED / 部分成功 PARTIAL_SUCCESS / 致命错误或零成功 FAILED；废止旧 ALL_OR_NOTHING 单版本全成功/全回退口径（一个无关 Domain 失败不得阻止其他成功 Domain 发布）
- 🆕 `ScheduleRun.Status` 值域 `RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED` 及定义写入 §3.7；所有终态（含 PARTIAL_SUCCESS）均写入 `CompletedAt`
- 🆕 运行启动时冻结 `ExpectedDomainKeysJson`：`ScheduleRun` 的**独立运行级字段，不属于 ScopeJson**（ScopeJson 仍是固定 11 字段，二者各自独立、互不嵌套）；创建时冻结本次预期参与计算的 DomainKey 集合，**不得按已建 PlanVersion 反推**
- 🔄 设备故障闭环口径重写：RESOURCE_BREAKDOWN / RESOURCE_REPAIRED **不再产出** `TaskPauseVoucher` / `TaskResumeVoucher`；改为"不可用/恢复事实 → ImpactAssessment → ScheduleExplanationFact → RescheduleRecommendation → 看板告警/通知 → PMC 决定是否发起单域重排"；设备故障**不得**自动暂停 Task、自动恢复 Task、自动创建 ScheduleRun、自动创建 Candidate；`TaskPauseVoucher` / `TaskResumeVoucher` 标记为**历史已废止口径，仅供追溯，V1 不落地**
- 🆕 BUILDING 失败终态：某 Domain 重试耗尽或确认不可恢复必须将该 Domain PlanVersion 转 `FAILED` 并写 `ErrorMessage`，不得无限期停留 BUILDING；运行级致命错误下已创建未完成的 Domain PlanVersion 统一标记 `FAILED`，不得让 `ScheduleRun` 永久 `RUNNING`；状态汇总（步骤5.3）仅在所有 `ExpectedDomainKeysJson` 对应 PlanVersion 进入 ACTIVE/FAILED 后执行
- 🆕 ReasonCode 权威字典冻结为 15 项（见 §3.8a）；`DUE_DATE_VIOLATION`→`DUE_DATE_RISK`；上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`；文中所有 ReasonCode 必须属于此列表
- 🆕 跨域依赖 V1 处理：仍按 Domain 独立发布；某 Domain 失败产生 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 原因事实 + `RescheduleRecommendation`，由 PMC/0号位人工选择相关 Domain 重算；V1 不自动回滚已成功上游、不建跨域多 Domain Candidate、不建原子激活组
- 🆕 白天 Candidate 严格单 Domain（一个 ScheduleRun → 一个 DomainKey → 一个 BasePlanVersionId → 一个 Candidate PlanVersion）；PMC 选多跨域 Domain 重算时由后台按 Domain_Dependency 拆成多个单域重排

**v3.29更新内容**（2026-07-06 规则与参数引擎 + 跨厂Pegging补强，对齐 DDL v5.0.46 / 防腐层 v1.33 / 字段说明 v5.0.46）：
- 🆕 规则与参数引擎6张表（RuleSet/RuleSetVersion/ParameterSet/ParameterSetVersion/StrategyProfile/StrategyProfileVersion）
- 🆕 ProcessCodeDict.ERPProperty（ERP真实属性，5号位维护）+ MES_ProcessCode_View 透出
- 🆕 ERP_Received_ByDocument_View / ext_ERP_Received_ByDocument_View（Received按单据汇总）
- 🆕 MES_APS_BOM_Workset_CrossFactoryEdge / APS_BOM_CROSS_FACTORY_EDGE_RAW（BOM跨厂边）
- 🆕 PeggingSupplyAllocation（非Task供给分配细账）；物理Pegging表=Task-to-Task
- 🔄 ScheduleRun.StrategyProfileVersionId
- 📌 跨厂模式：STAGE_HANDOFF vs INTER_FACTORY_ORDER；ZP/BP不通用；SupplyFact_Pipeline MOCK

**v3.28更新内容**（2026-06-15 管道供给链分层语义修正 + 字段契约锁定，对齐 DDL v5.0.42 / 防腐层 v1.31）：
- 🔄 **§2 [PipelineSupply]行**：分层语义统一——ODS视图=「ODS层/MES_Integration/来源ERP/5号位」；APS包装视图=「APS层/APS_Production/2号位」；明确 `FactoryCode`=目的工厂，`Quantity`=剩余在途数量，`MasterID` 为 ERP 物料映射主字段
- 🔄 **§3.6 管道供给一句话理解**：修正 `ext_ERP_InterplantInTransit_View` 定位（由「ODS包装」修正为「APS跨库包装视图」）；V1.1/V2 链路升级为14字段完整契约 + 四步映射
- 📌 **契约锁定规则**：ODS契约视图字段结构为强契约，V1.1/V2 允许调整ODS视图内部SELECT表达式、FROM、JOIN、WHERE及必要转换逻辑；对外14字段不变，禁止修改字段顺序/类型/名称
- 🔄 **SupplyFact_Pipeline**：v5.0.42 新增 `SourceMasterID`/`SourceFactoryCode`/`SourceDocumentLineNo`/`SourceUpdatedAt` 四个来源追溯字段

**v3.26更新内容**（2026-06-12 MES生产进度汇总链路 + 订单状态准入过滤 + Task/Pegging全量重算口径 + EAM V1预留，对齐 DDL v5.0.41）：
- 🆕 **§2 新增 [MES工单链路] 行**：`ODS.MES_APS_WorkOrder_View → APS.MESWorkOrderSnapshot → ScheduleContext.MESWorkOrderSnapshots`
- 🆕 **§2 新增 [MES工序进度链路] 行**：各大工艺子视图 UNION ALL → `ODS.MES_APS_OperationProgress_View → APS.OperationProgressSnapshot → ScheduleContext.OperationProgressSnapshots`
- 🆕 **§2 新增 [MES大工艺进度链路] 行**：各大工艺子视图 UNION ALL → `ODS.MES_APS_StageProgress_View → APS.StageProgressSnapshot → ScheduleContext.StageProgressSnapshots`
- 🆕 **§3.9/3.10/3.11 新增**：MES工单、工序进度、大工艺进度汇总链路一句话理解
- 📌 **订单状态准入（v3.12 收窄）**：`Order_Canonical.Status` 只有三种业务値：OPEN / CLOSED / CANCELLED；活跃根集合、BOM Request、Order 分区表只接收 `WHERE Status = 'OPEN'` 的订单/生产指示；CLOSED/CANCELLED 不得进入 BOM Request 或生成 Task/Pegging
- 📌 **Task/Pegging 全量重算**：随新 `PlanVersionId` 每日重新生成；MES 进度不匹配历史 TaskId；Pegging 不跨版本复用
- 📌 **EAM V1 预留**：`EAM_APS_Resource_View` 预留占位；V1 不读取 EAM 数据，不生成资源不可用窗口
- 🔧 **红线 #28/29/30 新增**：订单状态准入过滤；生产进度不混入 InventoryBalance；Task 透传生产指示号

**v3.25更新内容**（2026-06-08 DDL 建表顺序修复 + §2 横向表库存/管道供给行更新，对齐 DDL v5.0.40）：
- 🔧 **P0 说明**：DDL v5.0.40 已将 `ProductFamily`/`Factory` 提前至 §2.4a，修复 `ProductionDepartment`/`InventoryAvailabilityRule` 外键引用顺序问题
- 🔄 **§2 [Inventory]行**：工作集层改为**六层库存架构**；新增 `InventoryAvailableSupplyDetail`（第4层，规则命中后、汇总前明细层）；`InventoryBalance` 从该表汇总；APS落地层/输出列同步更新
- 🔄 **§2 [PipelineSupply]行**：工作集列改为 V1 空跑口径；`SupplyAvailabilityRule` V1 不调用；V1.1/V2 预留说明保留
- 🔄 **红线 #24**（v5.0.40已更新）：五层→六层；移除已删旧表引用；补 V1 SupplyAvailabilityRule 不调用声明

**v3.24更新内容**（2026-05-31 库存规则 V1口径收敛，对齐 DDL v5.0.39）：
- 删除 `ProductFamilyInventoryScope` + `InventorySourceRule` 旧引用（v5.0.39已从DDL删除）
- 库存工作集层改为 `sp_SyncInventorySnapshot` 六步 ETL；APS落地层改为 `InventoryAvailabilityRule`
- `InventoryBalance.ProductFamilyId`=库存使用上下文；`BatchNo`=快照标签
- FactoryId 红线修正：库存/供给事实+规则+余额表均可持有 FactoryId FK

**v3.23更新内容**（2026-05-30 产品族解析链，对齐 DDL v5.0.37）：
- 🔄 **§2 [Master/Material]行**：契约层增补 ODS 产品族解析三表（MaterialProductFamilyScopeRule/Rule/Resolved）；工作集层增补 `sp_ResolveMaterialProductFamily`（00:05）+ `sp_SyncMasterData` 步骤1c（ProductFamilyCode→ProductFamilyId 码表映射）
- 🔄 **§3.2 Master/Material**：更新说明，补充产品族解析链描述
- 📌 设计决策：产品族解析逻辑完全封装在 ODS 层；APS 层只做码表映射；Order.ProductFamilyId 从 Material 继承

> ⚠️ **v3.24 覆盖声明**：本条 v3.23 中关于产品族三表（`MaterialProductFamilyScopeRule` / `MaterialProductFamilyRule` / `MaterialProductFamilyResolved`）和 `sp_ResolveMaterialProductFamily` 的描述已被后续 **V1 简化方案**覆盖。**当前 V1 不建三表，不执行 `sp_ResolveMaterialProductFamily`**；三表和 SP 仅作 V2 预留，不进入 V1 正式链路。详见 §3.2 V1口径说明及防腐层 §2.4.4。

**v3.22更新内容**（2026-05-26 OrderBomRequestLink业务锏点升级，对齐 DDL v5.0.34）：
- 🔄 **[Order]表新增 `OrderCanonicalId BIGINT NULL`**：APS本地快照与 ODS 稳定关联；`sp_SyncOrdersToPartitionTable` 必须写入 `oc.Id AS OrderCanonicalId`；新增索引 `IX_Order_PlanVersion_OrderCanonical(PlanVersionId, OrderCanonicalId)`
- 🔄 **OrderBomRequestLink 唯一约束升级**：`UNIQUE(PlanVersionId, OrderId)` → `UNIQUE(PlanVersionId, OrderCanonicalId)`（业务锏点是 OrderCanonicalId，不是 OrderId）
- 🔄 **OrderBomRequestLink.OrderId 改为可空**：找不到时写 `OrderId=NULL, LinkStatus='SKIPPED'`，不阻断批次
- 🔄 **BOMResultPullService 签名升级**：`PullBOMResultFromODS(batchNo)` → `PullBOMResultFromODSAsync(batchNo, planVersionId)`；`planVersionId` 由 NightlyBatchOrchestrator 显式传入，禁止内部自查最新 PlanVersion
- 🔄 **红线 #29 升级**：补充 OrderBomRequestLink 完整生成口径、SKIPPED 场景说明、数据源禁止从 APS_BOM_RAW 反查
- 📌 **设计决策写死**：`OrderBomRequestLink` 业务锏点 = `PlanVersionId + OrderCanonicalId`；数据源必须为 ODS `MES_APS_BOM_Workset` 聊合；`APS_BOM_RAW` 禁止新增订单级字段

**v3.21更新内容**（2026-05-25 RequestDetail字段收敛，对齐 DDL v5.0.32）：
- 🗑️ **§3.1 BOM一句话理解**：删除 `ResolvedBOMNO由5号位展开后回填` 描述；改为《请求输入字段仅剩 `RequestedBOMNO`，`ResolvedBOMNO` 归 `OrderBomRequestLink`》
- 🔄 **§4 红线 #23**：删除旧口径 `sp_ExpandBOMBatch_vNext 步骤5a回填` 描述；改为《`ResolvedBOMNO` 归 `OrderBomRequestLink`（2号位写入）》
- 📌 **设计决策写死**：`MES_API_BOM_Request_Detail` 只保留请求输入；`Model`/`OrderStagingId`/`ResolvedBOMNO` 已删除；5号位只写 Workset/StageDetail/Issues；2号位生成 OrderBomRequestLink 承接解析结果

**v3.20更新内容**（2026-05-25 Order→BOM追溯链闭合，对齐 DDL v5.0.31）：
- 🔄 **§2 BOM行「工作集/计算层处理」列**：RequestDetail写入唯一约束改为 `(BatchNo, OrderCanonicalId)`；补说明展开完成后 2号位生成 `OrderBomRequestLink` 索引表
- 🔄 **§3.1 BOM一句话理解**：`OrderStagingId/BOMNO可空` 描述更新为 `OrderCanonicalId/RequestedBOMNO可空`；补 `OrderBomRequestLink` 查询桥接说明
- 🔄 **§4 红线 #3**：唯一约束更新为 `(BatchNo, OrderCanonicalId)`；废止旧口径 `OrderStagingId`
- 🔄 **§4 红线 #23**：2号位推送字段描述更新为 v5.0.31 新字段结构（OrderCanonicalId/RequestedBOMNO/ResolvedBOMNO等）
- 🆕 **§4 红线 #29（新增）**：OrderBomRequestLink 查询链路红线
- 📌 **设计决策写死**：`APS_BOM_RAW` 保持BOMNO级共享（不订单化）；`APS_BOM_STAGE_PATH_RAW` 无需改动；`OrderBomRequestLink` 承担 Order→BOMNO 桥接职责

**v3.19更新内容**（2026-05-17 订单提升链路清稿收口，对齐 DDL v5.0.27 补丁）：
- 🔄 **§3.4 Order**：`CustomerSegment` 口径修正：CustomerCode为空→NULL；CustomerCode有值但无IsActive=1匹配→`UNKNOWN`（v5.0.27废止旧"失效/无匹配→OVERSEAS"规则）
- 🔄 **ERP_Order_Staging.FactoryCode** NOT NULL→NULL（V1 TODO桩；V2补规则转换；不迫使同步层写占位值）
- 🔄 **CustomerCodeMap注释修正**：IsActive=0行JOIN过滤，不参与CustomerSegment派生
- 📌 **设计决策写死**：`CustomerSegment='UNKNOWN'` ≠ `'OVERSEAS'`，消费方须识别UNKNOWN走保守路径

**v3.18更新内容**（2026-05-14 BOM防腐层物化边表架构调整，对齐 DDL v5.0.26）：
- 🔄 **1 分层定义表 - 契约层**：定义扩展为"视图或物化边表"；`MES_BOM_View` 调整为兼容视图（`SELECT * FROM MES_BOM_Edge_Active`）；`MES_BOM_Edge_Active` 升为 BOM 合同执行双合一物化防腐边表
- 🔄 **2 BOM 行 - 契约层/防腐层处理**：来源改为 `sp_RefreshBOMEdgeActive  MES_BOM_Edge_Active`；展开链路改为 `sp_ExpandBOMBatch_vNext / sp_ExpandBOMRealtime_vNext`
- 🔄 **3.1 BOM 一句话理解**：更新防腐链路描述
- 🔄 **4 红线 #2**：补充 `MES_BOM_Edge_Active` 作为 BOM 契约层；补 BOM 展开不得直接递归复杂 View 红线
- 🔄 **5 负责人**：2号位增 `MES_BOM_Edge_Active` / `MES_BOM_Edge_RefreshLog` 表结构 + SP 骨架；5号位增 `sp_RefreshBOMEdgeActive` 业务映射逻辑 + `sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` 实现职责
- 📌 **V1/V2 边界写死**：V1 `MES_BOM_Edge_Active` = 物化防腐合同层 + 执行优化层合一；V2 视需要拆出 `MES_BOM_Edge_Contract`（多源裁决过程/历史/审计追溯）；追溯字段 `SourceSystem / SourceBOMId / SourceLineNo / IsActive / IsDefaultVersion / RefreshBatchNo / RefreshedAt` V1 必须保留
- 📌 **实时路径同步对齐**：`sp_ExpandBOMRealtime_vNext` 禁止继续直接递归复杂 `MES_BOM_View`，必须同步消费 `MES_BOM_Edge_Active`
- 📌 **StageDetail WorksetId 追溯**：`MES_APS_BOM_Workset_StageDetail` 及 Realtime/Archive 变体、`APS_BOM_STAGE_PATH_RAW` 均新增 `WorksetId` 字段；`RequestDetailId` 不进 StageDetail（可经 WorksetIdWorkset.RequestDetailId 反查）；`sp_CleanupBOMWorkset` 级联清理改为 `WHERE WorksetId IN (SELECT Id FROM Workset WHERE BatchNo=@BatchNo)`
**v3.17更新内容**（2026-05-13 阶段二三接缝：运行编排+Scenario骨架+原因事实+读模型）：
- 🆕 **§1 分层定义**：APS业务落地层典型对象补 `PlanVersion` / `ScheduleRun`（运行编排，阶段一即用）/ `Scenario` / `SimulationRun`（仿真，阶段二预留）/ `ScheduleExplanationFact`（原因事实，阶段一骨架）/ `OrderScheduleSummary` / `ResourceLoadSummary` / `PlanKpiSummary`（读模型，阶段一即用）
- 🆕 **§2 总表新增行**：「排程运行编排」（ScheduleRun / RunType / Scenario / SimulationRun）；「排程结果解释与读模型」（ScheduleExplanationFact + Summary 三张表）
- 🆕 **§3.7** 排程运行编排一句话理解；**§3.8** 排程结果读模型一句话理解
- 🆕 **§4 红线 #25~#28**：ScheduleRun统一编排 / 仿真不自动激活 / Scenario≠PlanVersion / 读模型不参与排程内核
- 🆕 **§5 负责人 v3.17**：3号位（运行触发 + ScheduleRun 初始记录创建 + 版本切换）/ 2号位（ScheduleRun 状态回填 + PlanVersion 持久化 + ExplanationFact 持久化 + Summary 生成）/ 1号位（内存 ExplanationFactDraft）
- 📌 **设计决策写死**：①`ScheduleRun` 是对现有"直接生成 PlanVersionId"流程的**最小包装**，阶段一不改排程内核；②`SIMULATION` / `INSERT_ORDER_WHATIF` / `MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` 产出的 `PlanVersion` 默认 CANDIDATE，正式激活**必须显式触发**；③`Scenario` 是 SIMULATION/WHATIF 类运行的业务容器，`MANUAL_RESCHEDULE` 不要求建 `Scenario`；④`ExplainTrace`（现有轻量追踪）≠`ScheduleExplanationFact`（结构化原因事实），共存不替代

**v3.16更新内容**（2026-05-13 OrderType重构+衍生字段澄清，对齐 DDL v5.0.24）：
- 🔄 **§2 Order行**： APS衍生字段列补 `DelayStatus`；透传字段列补 `DelayStatus`。OrderType重分类：SO/MTO→`SALES_ORDER`，MTS/SS/SS_U→`PRODUCTION_INSTRUCTION`
- 🔄 **§3.4 Order**：补 `DelayStatus` 字段说明；`DemandMaturityStatus` 收窄说明；OrderType重分类说明
- 📌 **设计决策写死**：`CustomerSegment` 由 `sp_ValidateAndPromoteOrders` 通过 `CustomerCodeMap` 本地映射表推导，非 ODS 共享字典

**v3.15更新内容**（2026-05-09 管道供给链，对齐 DDL v5.0.23）：
- 🆕 §2 新增「管道供给 / PipelineSupply」行：并行于现货库存五层主链；来源 `ERP_InterplantInTransit_View`；`InventoryBalance` 定义不变
- 🆕 §3.6 新增管道供给一句话理解：`ETA`=ODS原始事实；`AvailableTime`=本地派生（ETA+LeadTimeOffset）；`BatchNo` nullable 支持夜间快照
- 🆕 §4 红线 #24（新增）：管道供给链是现货五层主链的并行独立链，`InventoryBalance` 定义不变，结果为空不影响现有排程
- 📌 **相关权威文档**：`APS_数据库表结构设计 v5.0.23` / `防腐层 v1.18` / `集成接口 v1.15`

**相关权威文档（v3.15 基线，2026-05-09）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.23**
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.23**

---

**v3.14更新内容**（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21）：
- 🔄 §4 红线 #3 修正：`MES_API_BOM_Request_Detail` 唯一约束从 `(BatchNo, BOMNO)` 变更为 `(BatchNo, OrderStagingId)`；不再按BOMNO去重，改为按订单粒度写入
- 🔄 §3.3 BOM一句话理解：活跃根集合不再去重BOMNO，改为按订单粒度推送；BOMNO可空，BOM入口解析归5号位
- ✅ §4 红线 #23（新增）：`BOMNO` 可空红线——`ERP_Order_Staging.BOMNO` / `Order_Canonical.BOMNO` 均改可空；NULL=待5号位Workset阶段解析BOM入口；`FailureCode` / `NextActionCode` 为独立维度，禁止混用
- ✅ §5 负责人：2号位推送BOM请求改为订单粒度；5号位新增BOM入口解析分流职责
- 📌 **设计决策写死**：BOM入口解析（有BOMNO直接展开/无BOMNO从Model推导）由**5号位Workset处理阶段负责**；2号位只推送基础字段

**相关权威文档（v3.14 基线，2026-05-08）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.21**
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.21**

**v3.13更新内容**（2026-05-03 工厂归属关系总表 + 工厂字段承接红线 + DDL v5.0.17 Bug 修复）：
- 🆕 §6 新增：**工厂归属关系总表**——逐表列明 `FactoryCode` / `FactoryId` 的有无、来源、推导路径；覆盖 17 张表/视图
- 🆕 §4 红线 #19~#22 新增：**工厂字段承接 4 条红线**——`Factory.Code` 基准、ODS/字典层只留 FactoryCode、APS_Production 强关联才落 FactoryId、排程主链对象不重复存 FactoryId
- 🐛 DDL v5.0.17 Bug 修复：`sp_SyncResourceData` / `sp_SyncOrdersToPartitionTable` 两处 `LEFT JOIN Factory` 误用 `f.FactoryCode`，实际列名为 `f.Code`
- ✅ §6 备注：`Order_Canonical.FactoryCode` 补齐路径建议（若源端仅给 ProcessCode，2 号位可参考 `MES_ProcessCode_View.FactoryCode`——**待确认口径**）

**相关权威文档（v3.13 基线，2026-05-03）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.17**
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.16**

**v3.12更新内容**（2026-04-29 生产部门主链 + ProcessCodeDict 重定位 + WorkshopCode 全局清理，对齐 DDL v5.0.16 + 字段说明 v5.0.16）：
- 🔄 §4 红线 #12 修正：ProcessCodeDict 不再是「ERP 工序对照表 ODS 镜像」，改为「**APS 自维护 ODS 增强字典**」；`sp_SyncMasterData(@SourceType='ProcessCode')` 分支取消
- 🆕 §4 红线 #14 新增：**部门 = 物料 × 阶段联合属性**——不进 StageDict、不进 StageDetail；2 号位先组装 `MaterialStageDeptContext` 再交给 1 号位
- 🆕 §4 红线 #15 新增：**1 号位排程主链定调**`(MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套`；1 号位**禁止**直接读 MSC 或 ProcessCodeDict
- 🆕 §4 红线 #16 新增：**ProcessCode → StageCode 基础映射全链路共享**；5 号位 `sp_EnrichBOMWorkset` 与 2 号位 `sp_RebuildMaterialStageDeptContext` 必须查同一列 `MES_ProcessCode_View.StageCode`，禁止各写一套规则
- 🆕 §4 红线 #17 新增：**人工维护必须明确到 (Model/Material) × StageCode**；`MaterialStageDeptOverride` 导入时 Model 1:N 多个 MaterialCode 即拒收，避免误覆盖整串规格
- 🆕 §4 红线 #18 新增：**Routing 三件套 `ProductionDepartmentId NOT NULL`**（业务确认 MES 工艺数据全部带部门）；不引入 `_UNSPECIFIED` 哨兵
- ✅ §1 分层定义表：APS_Production 库新增 `ProductionDepartment` / `MaterialStageDeptOverride` / `MaterialStageDeptContext` / `MaterialStageDeptContext_Issues` 4 张表；ODS 资源契约视图字段 `WorkshopCode` 改为 `ProductionDeptCode`
- ✅ §1 分层定义表：APS_Production 库表 `Resource` / `RoutingOperation` / `RoutingDependency` / `OperationResourceEligibility` / `MaterialSupplyContext` 全部加 `ProductionDepartmentId` 维度
- ✅ §5 负责人：2 号位补 `sp_RebuildMaterialStageDeptContext` 占位 SP 责任（v1 骨架，三触发：FULL/INCR/PARTIAL）；2 号位**取消** v3.11.2 的 `sp_SyncMasterData(@SourceType='ProcessCode')` 同步分支责任
- 🚫 §5 负责人：APS 系统管理员（新增角色）—— `ProcessCodeDict` 人工维护责任人；0 号位审批
- 【设计决策】R20 跨组织视角**零特殊逻辑**——StageCode 已采目标工厂视角，按 `(MaterialId, StageCode)` 查 Context 天然得到目标工厂部门
- 【设计决策】Resource 删 `WorkshopCode`（业务确认 MES 也无此概念）+ `StageLeadTimeParam.WorkshopCode → ProductionDeptCode`（口径全局统一）
- 【设计决策】`ProductionDepartment` 与审批组织表解耦：本表是 APS 排程主数据字典，不接审批；与 `ResourceOrgGroup` 职责严格分开（前者排程主链维度，后者看板筛选切片）

**相关权威文档（v3.12 基线，2026-04-29）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.16**
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.16**

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

**v3.11.2更新内容**（2026-04-28 ProcessCode 防腐三件套补齐 + StageDict 字段净化，对齐 DDL v5.0.15 + 字段说明 v5.0.15）：
- ✅ §4 红线 #12 新增：**ProcessCode（6 位 ERP 工序码）严格只在 ODS 层活**；APS_Production 库永不出现 `ProcessCode / ActualFactoryCode / TrusteeProcCode` 三个字段；违反即打穿防腐墙
- ✅ §4 红线 #13 新增：**字典字段分层原则**——StageDict 只承载"阶段自身属性"（不依赖物料的语义）；"物料×阶段联合属性"（是否入库、入库角色、阶段 LeadTime）一律不放 StageDict，分层承接走 `RoutingStage` / `StageDetail` / `StageLeadTimeParam`
- ✅ §1 分层定义表：ODS 契约视图家族新增 `MES_ProcessCode_View`（Socket-Plug；主键 ProcessCode；对应 ODS 物理表 `ProcessCodeDict`）
- ✅ §1 红线#11 表述强化：R17 Produce→厂映射照片权威基线（v5.0.14 已纠正）+ v5.0.15 ProcessCode 相关防腐
- ✅ §5 负责人：2 号位补 `sp_SyncMasterData(@SourceType='ProcessCode')` 同步分支责任（v1 占位骨架）
- 【设计决策】两本字典职责严格分离：StageDict（APS 自主业务语义）vs ProcessCodeDict（ERP 工序对照表 ODS 镜像）；不可混淆
- 【设计决策】vw_MES_BOM_Stage_Enriched 重写为 BOM 边粒度（原 StageDetail 粒度 JOIN 设计失败，详见字段说明 §1.6）

**v3.11.1更新内容**（2026-04-28 照片权威纠正；详见字段说明 v5.0.14）：
- ✅ §4 红线 #11 R17 映射纠正：`1=继承父件 / 5,8=CN6课 / 9=SH / 6=BJ / 7=CN / 11=TJ`（原 7=TJ、11=SH 为 Bug）

**v3.11更新内容**（2026-04-25 资源 ODS 契约视图命名统一 + sp_SyncResourceData 占位 SP，对齐 DDL v5.0.13 + 防腐层 v1.14）：
- ✅ §1 分层定义表：契约层条目 `APS_Resource_View` 重命名为 `MES_APS_Resource_View`（与 `MES_APS_Routing_*_View` 对齐）
- ✅ §5 负责人：2 号位负责清单补“`sp_SyncResourceData(@SourceType)` 资源同步出口”（DDL v5.0.13 新增）
- 【设计决策】ODS 契约视图命名永久统一为“源系统_消费方_实体_View”三段式（单源可省略消费方）
- 【设计决策】EAM 扩展路径：未来并行新增 `EAM_APS_Resource_View` + `ext_EAM_APS_Resource_View`（同构契约零分叉）；`sp_SyncResourceData(@SourceType='EAM')` 分支 v1 预留 NOT_IMPLEMENTED

**相关权威文档（v3.11 基线，2026-04-25）**：
- `BOM_Workset_生成与错误处理技术方案_v1.0.md` → **v1.2**
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.13**
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.13**
- `APS_数据架构与防腐层设计方案_v5.0.md` → **v1.14**
- `APS_集成接口设计_v1.12.md` → **v1.12**
- `APS_资源与工艺数据模型重设计方案_v5.0.md` → **v5.1**

---

**v3.10更新内容**（2026-04-24 工艺数据三层模型收敛，对齐 DDL/字段文档 v5.0.12 + BOM_Workset v1.2 + 防腐层 v1.13）：
- ✅ §3.3 Routing 一句话理解：补三层模型（OperationName / ProcessType / StageCode）+ `(MaterialCode, StageCode)` 对接主键 + StageSeq 唯一权威源
- ✅ §5 负责人：3 号位补"MES 本地阶段 → StageDict 的映射责任"
- 【设计决策】三层分层模型：OperationName/OperationCode（具体工序）/ ProcessType（辅助分类，不参与排程对接）/ StageCode（BOM↔Routing 对接主键之二）——三者互不替换
- 【设计决策】BOM↔Routing 对接主键 = `(MaterialCode, StageCode)`；1 号位按此二元组从 RoutingOperation 取小工序生成 Task
- 【设计决策】StageSeq 唯一权威源 = `StageDetail.StageSeq`；`RoutingStage.StageSeq` 已从 DDL 中删除
- 【设计决策】R20 跨组织视角统一：StageDetail.StageCode 采用目标工厂视角（父件 TJ + R20 指派 BJ → StageCode 写 BJ_MACH），1 号位无需跨厂翻译；Task 自动落在目标工厂产能队列
- 【设计决策】ProcessType 配置化：新增 `ProcessTypeDict`（骨架 IsActive=0，预留扩展）
- 【设计决策】OperationCode 不引入全局字典（MES 侧不可控 + 新增频繁；跨厂对接靠 StageCode 足够）

**v3.9更新内容**（2026-04-24 对齐 BOM_Workset v1.1 + DDL/字段文档 v5.0.11）：
- ✅ §2 BOM 行：去除"批次放行校验"旧表述，改为"Issues 降级登记 + 月度巡检"
- ✅ §3.1 BOM 一句话理解：补规则资产化（`ProduceToFactoryMap`）+ 全局字典（`StageDict`）+ 批次永不阻塞
- ✅ §5 负责人：5 号位职责去除"放行校验"；0 号位/业务复核人员改为"月度巡检 + 反馈 ERP 维护方"
- 【设计决策·作废】v3.8 的"Severity IN ('ERROR','CRITICAL') 阻塞"策略作废
- 【设计决策·新口径】防腐层只做"吸震 + 登记"，不做"生产准入判断"；批次状态机永远走 READY，`FAILED` 仅保留给 SP 进程崩溃
- 【设计决策·新口径】R20 = `ShouldDrilldown=1 + CrossOrgHandoffFlag=1`：本厂仍下钻 BOM 拿下阶明细，打跨组织交接标签告知"该链归他用方排产、本厂不占产能"
- 【设计决策·新口径】Issues 表 +`DegradeAction` 列（`STAGE_NULL` / `FACTORY_FALLBACK` / `QTY_DEFAULT_1` / `CYCLE_SKIP` / `BOMNO_SKIP` / `PRODUCE_DEFAULT_1`）
- 【设计决策·新口径】规则资产化：`ProduceToFactoryMap`（R17/R20 规则宿主）+ `StageDict`（StageCode 全局字典，方案 B 工厂+阶段码）

**相关权威文档（v3.10 基线，2026-04-24，v3.11 已刷新，以上新基线为准）**：
- `BOM_Workset_生成与错误处理技术方案_v1.0.md` → v1.2
- `APS_数据库字段说明文档_v5.0.md` → v5.0.12
- `APS_数据库表结构设计_v5.0.sql` → v5.0.12
- `APS_数据架构与防腐层设计方案_v5.0.md` → v1.13

---

**v3.8更新内容**（2026-04-23 R17/R25/R26/R27 工厂映射 + BOM错误容错，基于《BOM_Workset_生成与错误处理技术方案_v1.0》）：
- ▸ **稳定合同原则**：Workset/StageDetail 核心表最小改动；ERP 特征字段不下沉到 L1/L2 合同层
- ✅ §1 分层定义表：工作集层 +`MES_APS_BOM_Workset_Issues`（诊断独立表）；契约层/防腐层**不新增** Socket-Plug 视图；新增一类"派生便利视图（vw_*）"旁路说明
- ✅ §2 BOM 行：装载层链路补 `ChildRequiredFactory` 回填（R17 推导）+ Issues 写入 + 批次放行校验
- ✅ §3.1 BOM 一句话理解：补 R17/R27 工厂映射与错误分治语义
- ✅ §4 红线：新增"诊断/错误信息不进核心表"、"APS 本地禁建 vw_APS_BOM_* 对称视图"两条红线
- ✅ §5 负责人：5 号位补"ChildRequiredFactory 回填 + Issues 写入 + 放行校验"职责；2 号位补"vw_MES_BOM_Stage_Enriched 视图维护（仅 ODS 内部，不作为 APS 排程输入）"
- 【设计决策】`ChildRequiredFactory` 值域=APS 自定义 5 厂枚举（CN/CN6课/BJ/TJ/SH），ERP 升级不影响核心表字段
- 【设计决策】APS 本地**不做** vw_APS_BOM_Stage_Enriched 对称视图；如需委外/受托信息由 2 号位预计算落独立配置表
- 【放行策略】批次 READY 前检查 Issues：Severity IN ('ERROR','CRITICAL') 阻塞；WARN 登记放行；INFO 静默登记

**v3.7更新内容**（2026-04-15 StageDetail升级为统一阶段路径结果表，支持ROOT根产品完工路径）：
- ✅ §1 分层定义表：工作集层StageDetail定位升级为"统一阶段路径结果表（EDGE+ROOT）"
- ✅ §2 BOM行：StageDetail升级为统一阶段路径（含StageScopeType EDGE/ROOT）
- ✅ §3.1 BOM一句话理解：补ROOT根产品完工路径说明
- ✅ §5 负责人：5号位新增ROOT路径推导职责
- 【设计决策】ROOT记录：ParentMaterialCode=NULL，IsSupplyThreshold恒为0
- 【设计决策】2号位装载Stage路径时必须显式按StageScopeType区分，再将结果随内存TaskDraft/DomainSolveRequest传给1号位

**v3.6更新内容**（2026-04-13 BOM双层结果+阶段提前期参数化，基于《BOM阶段顺序与Workset双层结果设计建议_v1.0》）：
- ▸ 本版替代v3.5，方案升级：单一StageHintCode → 3原始辅助字段 + StageDetail双层结果
- ✅ §1 分层定义表：契约层MES_BOM_View字段替换，工作集层补StageDetail，业务落地层补StageLeadTimeParam
- ✅ §2 BOM行：StageHintCode → 3辅助字段 + StageDetail双层结果链路
- ✅ §2 Routing行：RoutingStage定位调整为阶段字典 + StageLeadTimeParam
- ✅ §3.1 BOM一句话理解替换为双层结果说明
- ✅ §3.3 Routing一句话理解补充RoutingStage定位调整 + StageLeadTimeParam

**v3.4更新内容**（2026-04-09 客户分级字段）：
- ✅ §2 Order行补充CustomerTier（APS衍生字段）
- ✅ §3.4 Order一句话理解补充CustomerTier说明

**v3.3更新内容**（2026-04-09 订单ETL v1.2增补，基于《仅1.2增补内容v1.0》）：
- ✅ §2 Order行补充IssueDate/OriginalDueDate/ReceivedQty
- ✅ §3.4 Order一句话理解补充新3字段透传说明

**v3.2更新内容**（2026-04-09 订单业务字段补充，基于《订单ETL补充字段设计建议v1.1》）：
- ✅ §2 Order行补充源事实字段和APS衍生字段说明
- ✅ §3.4 Order一句话理解补充业务字段透传说明

**v3.1更新内容**（2026-04-03 订单链路审计）：
- ✅ §3.4 Order补完整链路描述（v_APS_SalesOrder→ERP_Order_Staging→sp_ValidateAndPromoteOrders→Order_Canonical→sp_SyncOrdersToPartitionTable→Order→ScheduleContext）

---

## 文档定位

本文档是 **APS数据架构的总纲级参考文档**，提供基础数据、供给事实、Pegging账本、执行锁、Task结果与运行编排的分层演进全景图。

**文档特点**：
- ✅ **横向对比**：将5类数据放在同一张表中，清晰展示每类数据的完整演进路径
- ✅ **全局视角**：从源头层→契约层→工作集层→业务落地层→内存消费层，一目了然
- ✅ **职责明确**：每个环节都标注了负责人/Owner，便于协作分工

**与其他文档的关系**：⚠️ **更新日期：2026-07-29**
- **本文档**：横向对比，全局视角，架构总纲（当前 v3.34）
- **《APS_数据架构与防腐层设计方案_v5.0》 v1.38**：纵向深入，实施细节，存储过程设计、防腐层契约
- **《APS_数据库字段说明文档_v5.0》 v5.2.2**：字典参考，表结构定义，字段说明
- **《APS_数据库表结构设计_v5.0.sql》 v5.2.2**：DDL 脚本，数据库表结构
- **《APS_集成接口设计_v1.12》 v1.28**：双向视图、MES发布承诺、白天实时评估、激活边界及各号位职责
- **《APS 核心排产全流程走查》 V3.23**：本轮Pegging、软硬锁、TaskDraft与跨版本恢复的端到端流程权威
- **《BOM_Workset_生成与错误处理技术方案》 v1.2**：BOM Workset/StageDetail 推导与异常降级矩阵 + BOM↔Routing 三层对接模型（R01~R27 经验库实现）
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：执行分工，Socket-Plug 职责划分

**使用建议**：
1. 新成员入职时，先读本文档理解数据演进全景
2. 开发实施时，参考防腐层设计方案和数据库字段说明文档
3. 协作分工时，以本文档§5 v3.34当前权威职责为准；旧职责记录只用于追溯
4. 涉及Pegging、执行锁、HardLock、TaskDraft或Candidate供给时，先读§3.13～§3.19

---

> **口径说明**：本表按当前最新管理口径整理，其中 **“ODS 递归展开到 `MES_APS_BOM_Workset` 由 5号位负责实现”** 已写入 **Owner** 列。若正式采纳该调整，建议同步修订《职责分工变更说明》、内部契约和相关实施清单，避免后续分工冲突。

---

## 1. 分层定义

| 层级 | 定义 | 这一层主要做什么 | 典型对象 |
|---|---|---|---|
| **源头层** | ERP / MES 现有生产库、业务中间表、主数据表 | 保存原始业务事实，不直接给 APS 使用 | ERP 订单中间表、ERP master、MES BOM 物理表、MES 工艺表、MES 库存表 |
| **契约层 / 防腐层** | ODS（`MES_Integration`）中的标准视图或物化边表契约（v3.18：BOM 防腐层已升级为物化边表；其余仍为视图） | 把源系统脏乱差物理表整理成稳定接口，隔离源库表结构变化 | `MES_BOM_Edge_Active`（v3.18新增，物化防腐边表；兼容视图 `MES_BOM_View` = `SELECT * FROM MES_BOM_Edge_Active`，不再作为递归展开对象）、`ERP_Master_View`、`MES_Material_View`、~~`MES_APS_Routing_View`~~(v5.0废弃)→`MES_APS_Routing_Operation_View`+`MES_APS_Routing_Dependency_View`+`APS_OperationResourceEligibility_View`+`MES_APS_Routing_Stage_View`(v3.5新增，v3.6定位调整为阶段字典)、`MES_APS_Resource_View`(v5.0新增；v3.11 命名统一，原名 `APS_Resource_View`）、预留 `EAM_APS_Resource_View`（未来 EAM 上线）、`ERP_Inventory_View`、`MES_Inventory_View` |
| **工作集 / 计算层** | ODS 工作集表 + APS 本地快照/算法账本 + 运行时计算对象 | 按当前数据截止时间筛选、递归展开、去重、映射、汇聚、位置互斥化、供需扣减和数量闭合 | `MES_API_BOM_Request*`、BOM Workset/StageDetail/CrossFactoryEdge、三张APS RAW、库存候选/明细/余额、MES三类进度快照、`ProductionInstructionSupplySnapshot`、`ProductionInstructionPositionSlice`、`PeggingAllocationLedger`。`LogicalBlock / TaskDraft / MergeCandidateGroup / ScheduledTaskDraft` 为内存对象，不提前持久化为正式Task |
| **APS 业务落地层** | APS 本地业务表与跨版本执行事实 | 形成排程、执行和业务查询可直接使用的标准实体；区分PlanVersion结果与跨版本事实 | `Material`、Routing五表、`Order_Canonical`、`Order`、`Resource`体系、`PlanVersion`、`ScheduleRun`、`Task`、`Pegging`、`PeggingSupplyAllocation`、`MESPlanRelease`、`ExecutionLock`、`DemandSupplyHardLock`、`ScheduleExplanationFact`、三张Summary读模型；`vw_TaskDemandAllocation`从Ledger提供Task—需求份额查询 |
| **内存消费层** | `ScheduleContext` / `DataSnapshot` / 领域策略模块 | 将准确切片装成纯内存沙盘，供Pegging和有限产能排程；所有余额变更只在Domain内存临界区执行，禁止循环逐行查库 | `ScheduleContext`、`RuntimeDemandBalance`、`RuntimeSupplyBalance`、PI PositionSlice余额、PUBLISHED ReleaseContext、ExecutionLockedOutput、HardLockContext、`LogicalBlock`、`TaskDraft`、`MergeCandidateGroup`、`ScheduledTaskDraft`、`RemainingSupplyContext`（按供给状态重建，不再简单扣掉Base全部分配） |

---

## 2. 各类基础数据分层承接与演变总表

| 数据类别 | 源头 | 契约层 / 防腐层处理 | 工作集 / 计算层处理 | APS 落地层 | 输出到哪 | 给谁用 | 负责人 / Owner |
|---|---|---|---|---|---|---|---|
| **BOM**（2026-04-23 v3.8更新） | ERP / MES 现有 BOM 物理表；第一层展开依赖订单/MTS 下发的 `BOMNO`，第二层及以后依赖物料型号的有效版本继续向下展开 | （v3.18）由 `sp_RefreshBOMEdgeActive` 从 ERP/MES 多源 BOM 物理表刷新到 `MES_BOM_Edge_Active`（物化防腐边表）；字段标准化：`BOMNO / ParentMaterialCode / ChildMaterialCode / Quantity / IsActive / IsDefaultVersion / ParentProcRefCode / ChildProcRefCode / ChildSourceHintCode`（值域0-11）+ 追溯字段 `SourceSystem / SourceBOMId / SourceLineNo / RefreshBatchNo / RefreshedAt`；执行 ProcessCode 左补零 6 位 / ChildSourceHintCode 值域标准化 / 双源唯一默认版本裁决（IsDefaultVersion=1 全局唯一）；**`MES_BOM_View` 降为兼容视图** = `SELECT * FROM MES_BOM_Edge_Active`，不再直接 UNION 源表；刷新失败时 `MES_BOM_Edge_RefreshLog` 记录并禁止 Workset 使用半刷新数据；这一层只做标准化+裁决，不做 90 天活跃窗口筛选 | ① 从 `Order_Canonical` 划 90 天活跃根；② 按订单粒度写入 `MES_API_BOM_Request_Detail`（v5.0.31：唯一约束 `(BatchNo, OrderCanonicalId)`；`RequestedBOMNO` 可空；展开完成后 2号位生成 `OrderBomRequestLink` 索引表）；③ 在 ODS 迭代展开到 `MES_APS_BOM_Workset`（`sp_ExpandBOMBatch_vNext` 读 `MES_BOM_Edge_Active`，WHILE 循环 #Frontier，#EntryResolved 入口预解析，透传3辅助字段）；**v3.30实时路径**：2号位先判断BOM切片可否复用；不可复用时创建RequestDetail → `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` → `MES_APS_BOM_Workset_Realtime`（通过RequestDetailId追溯）→ `sp_EnrichBOMWorksetRealtime` → `MES_APS_BOM_Workset_StageDetail_Realtime`（通过WorksetId关联RequestDetailId）→ `sp_GenerateBOMCrossFactoryEdgeRealtime` → `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`（直接按RequestDetailId隔离，均允许0行）→ `PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)` → 三张APS RAW（`APS_BOM_RAW` / `APS_BOM_STAGE_PATH_RAW` / `APS_BOM_CROSS_FACTORY_EDGE_RAW`，BatchNo=`RT:RD:{RequestDetailId}`，此BatchNo为APS RAW实时切片号）→ 生成OrderBomRequestLink；④ **5号位后置回填**：`ChildRequiredStageCode` + **`ChildRequiredFactory`**（v3.8 R17 Produce→厂映射） + 写入`StageDetail`（EDGE子件供给路径+ROOT根产品完工路径，v3.7统一阶段路径） + **异常登记到 `MES_APS_BOM_Workset_Issues`**（v3.8 R27：LEAF/FACTORY_MISMATCH/NO_STAGE/CYCLIC_BOM 等）；⑤ Issues 降级登记（v3.9 口径）：全部异常写入 `MES_APS_BOM_Workset_Issues`（含 `DegradeAction` 标签），**批次永远走 READY，永不阻塞**；⑥ APS 拉取到 `APS_BOM_RAW`（+`ChildRequiredFactory`）+ `APS_BOM_STAGE_PATH_RAW`（阶段顺序明细）；⑦ 本地计算 LLC、叶子、路径等运行时属性 | `APS_BOM_RAW` 作为夜间活跃窗口本地缓存；如保留 3.5 `BOM` 表，建议定位为**当前计划窗口标准 BOM 关系缓存**，而非全量 4000 万主数据总账 | `APS_BOM_RAW`、可选的窗口级 `BOM` 缓存、最终 `ScheduleContext` | 2号位 `IDataLoader`、1号位排程引擎、5号位 Pegging/缺料规则、0号位/业务复核人员（Issues）、当前窗口前端树展示 | **契约 Owner：0号位审批 / 5号位实现 `MES_BOM_View`**；**递归展开+R17回填+Issues写入：5号位**；**APS 拉取 / LLC / 本地缓存 / `vw_MES_BOM_Stage_Enriched` 维护：2号位**；Issues 复核：0号位/业务复核人员 |
| **Master / Material**（2026-04-01 v4.0更新） | ERP `master` 表（`MasterID` + 仓库）+ MES 物料表（`MES_ID`） | 通过 `ERP_Master_View`、`MES_Material_View`（v1.3同构化：MES_ID→MasterID别名、Location→Warehouse别名、移除MaterialType、新增供给属性字段）暴露**双源同构**主数据标准接口；APS 侧通过 `ext_ERP_Master_View`、`ext_MES_Material_View`（字段完全一致）访问 | 通过统一参数化存储过程 `sp_SyncMasterData(@SourceType)`（v4.0双源统一，原 `sp_SyncERPMasterData` + `sp_SyncMESMaterialData` 合并）将双源数据三表协同同步：Material（MaterialType由APS按MaterialCode前缀推导）+ MaterialMapping（统一SourceID+Warehouse，消除ERP/MES字段分叉）+ MaterialSupplyContext（仓库级供给上下文：SupplyMode、DefaultProductionDeptCode、LeadTimeDays、SafetyStock、InventoryManagementMode，SCD Type 2） | `MaterialMapping` 作为桥表（统一SourceID+Warehouse）；`Material` 作为 APS 统一业务物料表（含 Spec，废弃 IsPurchased/SafetyStock/LeadTimeDays，MaterialType由前缀推导）；`MaterialSupplyContext` 承载仓库级业务上下文（含InventoryManagementMode） | `MaterialMapping`、`MaterialSupplyContext`、`Material`、最终 `ScheduleContext.Materials` | 订单装载、BOM 映射、Routing 映射、库存汇聚、1号位引擎、5号位库存/优先级规则 | **契约 Owner：0号位审批 / 5号位实现 ERP/MES 主数据插头**；**映射与 APS 装载：2号位** |
| **Routing / 工艺**（2026-04-01 v5.0重构，v5.0.1变更 2026-04-02，v3.6更新 2026-04-13） | MES 工艺表；存在新旧两套结构：新结构带 `MES_ID`，老结构只有物料型号 | v5.0重构：原 `MES_APS_Routing_View` 废弃，拆分为三个独立视图：`MES_APS_Routing_Operation_View`（工序节点）+ `MES_APS_Routing_Dependency_View`（工序依赖边，支持并行/串行混合）+ `APS_OperationResourceEligibility_View`（工序资源能力关系，替代ResourceGroup）；**v5.0.1变更：ODS视图输出`MES_ID`+`Model`（非MaterialCode），老结构由3号位ETL处理为MES_ID**；V1默认路径约束（RouteCode='DEFAULT', PathId=1）；**v3.6：`MES_APS_Routing_Stage_View`定位调整为阶段字典/标准阶段语言，不作为排程权威阶段顺序源** | 基本不走递归计算；主要是清洗、标准化、按契约对齐、增量 Upsert；**v5.0.1：2号位装载时通过`MaterialMapping(Source='MES', SourceID=MES_ID)`映射得到`MaterialId`** | APS 侧通过 3 个 ext_ 包装视图分别装载到：`RoutingOperation`（工序节点）+ `RoutingDependency`（工序依赖）+ `OperationResourceEligibility`（工序资源能力）；`RoutingPlanningParam` 承接 APS 本地排程参数（MinBatchSize/MaxBatchSize） | `RoutingOperation`、`RoutingDependency`、`OperationResourceEligibility`、`RoutingPlanningParam`、最终 `ScheduleContext.RoutingGraph` | 1号位引擎（面对工艺图而非序列表）、Task 生成、资源分配、前端工艺查询 | **契约与清洗 Owner：3号位**；**APS 包装视图与装载（含MES_ID→MaterialId映射）：2号位** |
| **Order / 订单**（2026-04-09 v3.3补充） | ERP 订单中间表 / 生产指示中间表，按小时增量 Upsert，携带 `MaterialCode / BOMNO / Quantity / DueDate` + 源事实字段（`TransportMode / CustomerName / MTS_InstructionNo / IssueDate / OriginalDueDate / ReceivedQty(仅MTS)`） | 先进入 `ERP_Order_Staging` 做验证与清洗，含源事实字段透传 + APS衍生字段标准化（`CustomerSegment`「通过CustomerCodeMap本地映射推导，v5.0.24澄清」/ `SalesOrderCategory` / `DemandMaturityStatus`「收窄为PRE_CONFIRMED/FORECAST，v5.0.24」 / `CustomerTier` / **`DelayStatus`**「v5.0.24新增」，由`sp_ValidateAndPromoteOrders`负责）；再进入 `Order_Canonical` 作为 APS 防腐层核心订单表【业务澄清：OrderType v5.0.24重分类（SO/MTO→`SALES_ORDER`；MTS/SS/SS_U→`PRODUCTION_INSTRUCTION`）；FactoryCode为ERP订单契约正式输入字段（来源：`v_APS_SalesOrder.FactoryCode`，由`sp_ValidateAndPromoteOrders`负责标准工厂码映射校验，禁止通过ProcessCode/工序名/StageCode/BOM路径反推工厂）】 | 每天 00:00 从 `Order_Canonical` 划定 90 天活跃根；同时保留白天订单增量进入 `Order_Canonical`，供后续批次或实时插单使用 | 从 `Order_Canonical` 转换到 `Order` 业务表，补齐 `MaterialId / ProductFamilyId / FactoryId / DomainKey / PriorityScore` + 透传业务字段（`TransportMode / CustomerName / CustomerSegment / SalesOrderCategory / DemandMaturityStatus / CustomerTier / **DelayStatus** / MTS_InstructionNo / IssueDate / OriginalDueDate / ReceivedQty`） | `Order_Canonical`、`Order`、最终 `ScheduleContext.Orders` | 2号位快照融合、1号位引擎、5号位 Pegging、前端订单查询与版本追溯 | **订单同步与 APS 装载 Owner：2号位**；**活跃根口径、订单类型口径：0号位** |
| **生产指示供给快照 / PI Position**（v3.32新增） | ERP生产指示Order事实；MES Stage/Operation进度；XC库存；厂间在途；最终入M数量 | `Order.Quantity - Order.ReceivedQty`为唯一总量边界；MES/XC/在途只解释位置；StageProgressSnapshot为大工艺权威，OperationProgressSnapshot只做阶段内部裁剪和诊断 | 2号位按 `ScheduleRunId + DomainKey` 生成PI Header快照；5号位提供位置计算策略结果：累计量互斥化、下游超上游下修、中间Stage缺失取最小证明量、XC映射当前Stage、UNLOCATED回最早Stage | `ProductionInstructionSupplySnapshot` + `ProductionInstructionPositionSlice`（每次运行/Domain不可变快照） | `ScheduleContext.ProductionInstructionSupplyContexts`、位置切片余额、TaskDraft起始Stage/AvailableTime | 2号位Pegging框架、5号位位置计算模块、1号位TaskDraft排程、问题复盘 | **事实契约：2号位/5号位共同；算法Result：5号位；余额闭合与落库：2号位** |
| **Pegging分配账本 / 非Task分配**（v3.37最终实现契约） | 顶层与派生需求；库存、管道、Received、采购、PI Header及位置切片 | 5号位返回现有`PeggingRuleVoucher`/Decision；各Supply Loader预生成`SupplyBusinessKey`；2号位在单Domain内按优先级顺序原子扣减，成功后用PlanVersion局部计数器生成`AllocationSequence` | 现有`PeggingLedgerEntry`记录内存分配；Pegging阶段不访问数据库、不提前持久化Task；结束后形成数量化TaskDraft及来源Components | `PeggingAllocationLedger`（统一总账）+ `PeggingSupplyAllocation`（非Task结果，通过LedgerId关联）；物理`Pegging`仍只Task-to-Task | Ledger→TaskDraft→1号位最终Task/份额→统一事务；`vw_TaskDemandAllocation` | 2号位余额和事务、5号位策略、1号位有限产能、4号位解释 | **单PlanVersion内顺序分配；不新建第二套Ledger/Decision，不重写算法** |
| **MES计划发布 / 执行锁 / 硬归属**（v3.34升级） | ACTIVE版本正式Task；MES实时工单/进度事实；特殊出荷指示、客户/环保/质量专属条件 | 2号位按同PI+同Stage+同执行批次形成MESPlanRelease，生成不可变ReleaseItemKey；发布数量取Stage级执行批次的单一流转数量，不把关联小工序Task.Quantity相加，数量不一致且无显式换算闭合时拒绝组装；MES通过APS_MES_PlanRelease_View幂等建单，并在MES_APS_WorkOrder_View回传ReleaseItemKey。Task发布时仍PLANNED，确认建单后才RELEASED。ExecutionLock固定现实执行，HardLock只固定归属 | 物理数量身份先互斥恢复：已消耗、ExecutionLock、PUBLISHED发布承诺、普通PI位置；再在每项供给内部恢复HARD/SOFT/未分配。工序状态4只影响小工序执行记录；整张工单终结后的未完成、未正式取消差额返回PI未承诺剩余池；HardLock部分释放后剩余量继续ACTIVE | `MESPlanRelease` + `ExecutionLock` + `DemandSupplyHardLock`；`Task.MESPlanReleaseId/ExecutionLockId` | MES拉取建单、次日恢复、Candidate现实校验、取消/完成闭环 | 2号位发布/快照/锁维护，5号位ODS视图和HardLock判断，3号位运行/审批，MES负责ReleaseItemKey幂等保存与回传 | **APS发布视图/快照/状态：2号位；MES契约视图：5号位收口；HardLock策略：5号位；物理更新唯一执行者：2号位** |
| **TaskDraft / 正式Task / 需求份额**（v3.38纯内存边界） | Phase 1.6内存工艺骨架、Ledger、Stage剩余路径、Routing、资源与批量参数 | `TaskDraft / FinalTaskDraft / AllocationShare`均为内存对象，无物理表；2号位从数据库/快照装载事实并组装`DomainSolveRequest`，通过方法参数传入1号位 | 1号位零数据库I/O，只返回内存最终Task草稿、`AllocationSequence→FinalTaskDraftKey→ComponentQty`及求解摘要；合并拆分前后份额守恒 | 2号位在同一Domain事务中将FinalTaskDraft实例化为正式Task，再写DraftKey/TaskId映射→Ledger→PSA→物理Pegging；发布切换另事务 | MES发布、甘特图、订单交付追溯、Summary | 1号位纯内存有限产能，2号位数据装载/总编排/正式持久化 | **不存在TaskDraft表；1号位不得依赖任何数据库组件；正式Task唯一落库方为2号位** |
| **管道供给 / PipelineSupply**（v3.32统一主链） | 厂间在途、采购在途、VMI、已到厂未入库等来源事实；各来源保留独立单据和更新时间 | 各来源在ODS提供稳定契约视图；APS包装层以显式字段 `UNION ALL` 形成 `ext_PipelineSupply_Source_View`；`FactoryCode`为目的工厂，ETA为源事实，AvailableTime为APS派生；以SourceSystem+DocumentNo+LineNo+Material+DestinationFactory等稳定业务键去重 | `sp_SyncPipelineSupply(@BatchNo,@DataCutoffTime)`按准确切片映射Material/Factory、应用可用规则或既有资格规则，批量写入`SupplyFact_Pipeline`；厂间在途与采购/VMI/到厂未入库保持SupplyType可区分；Candidate读取Base运行准确切片，不全局读当前全部在途 | `SupplyFact_Pipeline`；`PeggingSupplyAllocation`记录被需求实际采用的非Task管道供给 | `ScheduleContext.PipelineSupplies`、Pegging、AvailableTime、物流风险解释 | 2号位同步/去重/装载，5号位来源契约与供给资格策略，1号位只消费形成后的TaskDraft/约束 | **ODS契约：5号位；APS统一视图/SP/切片：2号位；分配策略：5号位；余额与Ledger：2号位** |
| **跨厂Pegging / CrossFactoryPegging**（v3.29 2026-07-06 新增） | ERP（Received 表+ProcessCodeDict.ERPProperty）；5号位 ODS 层 | `MES_ProcessCode_View.ERPProperty`（来自ERP真实属性，5号位透出）；`ERP_Received_ByDocument_View`（ODS汇总视图，5号位，粒度=工厂+仓库+物料+单据类型+单据号）；`MES_APS_BOM_Workset_CrossFactoryEdge`（ODS跨厂边表，5号位基于StageDetail生成） | `ext_ERP_Received_ByDocument_View`（APS包装，2号位）；`APS_BOM_CROSS_FACTORY_EDGE_RAW`（APS缓存，2号位搬运）；M库判定索引（2号位内存：MaterialCode+FactoryCode→HasMStock）；`PeggingSupplyAllocation`（非Task供给分配细账，2号位）；物理Pegging表（Task-to-Task血缘，2号位写入） | `ERP_Received_ByDocument_View`（V1空骨架）；`CrossFactoryEdge`（ODS+APS）；`PeggingSupplyAllocation`（确认可用供给结果）；`SupplyFact_Pipeline`（MOCK数据联调） | `PeggingSupplyAllocation`→`ScheduleContext`；物理Pegging表→`ScheduleContext`；CrossFactoryEdge→`ScheduleContext`；M库索引（内存） | 5号位插件（跨厂裁决）；2号位主责；1号位消费 Task/ShippingTask | **ERPProperty:5号位**；**ODS视图:5号位**；**APS装载+分配+物理Pegging:2号位**；**1号位：不查中间表** |
| **Inventory / 库存**（v3.37净量口径） | ERP库存总量包含WasterQty；MES库存事实 | APS专用ODS `ERP_Inventory_View.Quantity=max(0,总量-WasterQty)`，不新增WasterQty/UnusableQty对外字段；MES契约不变；无法按客户区分数量的专属仓库由`InventoryAvailabilityRule`整体排除 | 六层库存架构保持：Fact→Candidate→Rule→AvailableDetail→Balance→Runtime；ERP链各层Quantity均继承ODS净可用量，禁止再次扣减WasterQty | `InventoryFact_ERP/MES`、`InventorySupplyCandidate`、`InventoryAvailabilityRule`、`InventoryAvailableSupplyDetail`、`InventoryBalance` | 库存SupplyBusinessKey=`INV|ERP|Factory|Warehouse|Material`；实际分配进入Ledger/PSA | 5号位ODS契约与规则，2号位装载和余额 | **不增加客户、质量等级或批次空壳字段；专属仓库保守排除** |
| **排程运行编排**（v3.30 2026-07-14 白天实时正式链路升级） | APS 内部触发（Hangfire 定时器 / API 主动触发 / 算法引擎） | N/A（APS 内部对象）；四表分工：`ScheduleRun`（运行过程/运行级状态机 `RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED`；含 `ScopeJson`（固定 11 字段，作为运行范围唯一权威，创建后不可变，后续服务只读不改）与 `ExpectedDomainKeysJson`（**`ScheduleRun` 的独立运行级字段，不属于 ScopeJson**；创建时冻结，记录本次预期 DomainKey 集合，**不得按已建 PlanVersion 反推**））→ `PlanVersion`（结果版本/**一次 FULL_SCHEDULE 运行按 DomainKey 一个 Domain 一个 PlanVersion**，经 `SourceScheduleRunId` 关联同一 ScheduleRun；BUILDING-CANDIDATE-ACTIVE-ARCHIVED-FAILED）；`Scenario`（试算场景+假设+目标+选中版本，白天实时评估适用时建；INSERT_ORDER_WHATIF通常可建）+ `SimulationRun`（算法执行记录，阶段二骨架）；`MANUAL_RESCHEDULE` 不要求先建 `Scenario` | **夜间全量（FULL_SCHEDULE 多 Domain）**：00:38 创建 ScheduleRun 并冻结 `ExpectedDomainKeysJson`（运行级不可变）→ 02:00 按 Domain 创建多个 BUILDING PlanVersion（每个 DomainKey 一个，经 `SourceScheduleRunId` 关联同一 ScheduleRun）→ 各 Domain 独立计算、落盘、发布 → 全部成功 ScheduleRun=COMPLETED（所有 ExpectedDomainKeysJson 成功）；部分成功 ScheduleRun=PARTIAL_SUCCESS（部分 Domain 成功/失败或缺失）；运行级致命错误或零成功 ScheduleRun=FAILED。一个无关 Domain 失败不得阻止其他已成功 Domain 发布。**白天实时评估（v3.30正式链路）**：Scenario（适用时）→ ①3号位归一化ScopeJson → ②3号位创建ScheduleRun（写入BasePlanVersionId/ScopeJson/ExpectedDomainKeysJson(["BasePlanVersion.DomainKey"],严格单Domain)/DataCutoffTime等）→ ③3号位创建Candidate类PlanVersion版本壳（初始Status=BUILDING） → ④2号位构造Candidate独立Order快照 → ⑤2号位BOM复用判断+EnsureRealtimeBomReady → ⑥2号位PullRealtimeBOMResult到三张RAW → ⑦2号位按供给状态BuildRemainingSupplyContext → ⑧互斥恢复PI位置切片/ExecutionLock/PUBLISHED MESPlanRelease，再在每项物理供给内部恢复HardLock与Scope外Soft并BuildScheduleContext → ⑨Ledger→LogicalBlock/TaskDraft → ⑩1号位有限产能合并/拆分与排定 → ⑪2号位批量持久化Task/ShippingTask/Ledger/Pegging结果 → PlanVersion=CANDIDATE → ScheduleRun=COMPLETED（写 CompletedAt；须显式审批激活，激活不回改 ScheduleRun）| `ScheduleRun`（含ScopeJson）+ `PlanVersion`（SourceScheduleRunId反查）+ `Scenario`（白天实时正式容器/阶段二扩展）+ `SimulationRun`（阶段二骨架）+ `ScenarioObjectiveScore`（阶段二骨架） | `FULL_SCHEDULE` → 各 Domain PlanVersion 独立发布，成功 Domain 自动 ACTIVE（无论运行 COMPLETED 还是 PARTIAL_SUCCESS，已成功 Domain 均可发布）；`LOCAL_RESCHEDULE+INSERT_RESCHEDULE` / `LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT` / `MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT` 经审批后允许激活；`INSERT_ORDER_WHATIF+CTP` / `INSERT_ORDER_WHATIF+INSERT_IMPACT_ANALYSIS` 永不激活。激活由3号位调用`ActivateCandidatePlanVersionAsync`，Serializable事务内执行 | 统一排程触发入口；白天实时评估；阶段二多方案比较；阶段三 Skill API | **ScopeJson归一化+ScheduleRun创建+Candidate类PlanVersion版本壳创建（初始Status=BUILDING）+审批编排+激活：3号位**；**Candidate数据构造+结果持久化+PlanVersion Status更新+单域发布事务+ScheduleRun最终状态汇总：2号位**；**Scenario管理：3号位**；**RunType/Purpose枚举+激活边界：0号位审批** |
| **MES工单与实时执行事实链路**（v3.34升级） | MES实时工单数据；APS发布视图中的ReleaseItemKey | `MES_APS_WorkOrder_View`必须对APS新发布工单回传ReleaseItemKey、MESWorkOrderNo、PI、物料、计划量、状态和SourceUpdatedAt；历史/外部工单ReleaseItemKey可空 | 2号位按ScheduleRunId+DataCutoffTime形成MESWorkOrderSnapshot；按ReleaseItemKey将MESPlanRelease从PUBLISHED转CONSUMED、创建ExecutionLock并更新相关Task。时间范围只缩小工作集，不做事件增量累计 | `MESWorkOrderSnapshot` + `MESPlanRelease` + `ExecutionLock` | 发布承接、生产指示→MES工单追溯、进度看板、Candidate现实事实 | 2号位快照/对账/锁维护，5号位ODS统一视图，MES保存/回传ReleaseItemKey | **不使用MES_Actual_Staging、MQ事件去重或REST逐项回执** |
| **MES工序进度汇总链路**（v3.38纯内存消费澄清） | MES各大工艺工序报工汇总 | 各大工艺标准化子视图→`UNION ALL`→`ODS.MES_APS_OperationProgress_View`；工序识别主字段为OperationName，V1不接逐条报工日志 | 2号位按ScheduleRun/DataCutoffTime同步并读取`OperationProgressSnapshot`，完成Stage内部小工序TaskDraft裁剪后，将结果作为内存排程请求传给1号位；1号位不直接查询快照表 | `OperationProgressSnapshot` | TaskDraft的小工序剩余量、进度看板、Stage/Operation差异Issue | 2号位快照装载/TaskDraft裁剪，1号位消费内存TaskDraft，5号位统一视图 | **子视图：加工类2号位/组装类5号位；ODS收口：5号位；快照：2号位** |
| **MES大工艺进度汇总链路**（v3.32位置权威） | MES各大工艺报工汇总 | 各Stage子视图→`UNION ALL`→`ODS.MES_APS_StageProgress_View`；颗粒度=生产指示号+物料+Stage | 2号位按ScheduleRun/DataCutoffTime同步`StageProgressSnapshot`；5号位位置计算模块返回累计互斥化Result，下游超上游下修、中间缺失取下游最小证明量；2号位闭合并落PI PositionSlice | `StageProgressSnapshot` → `ProductionInstructionSupplySnapshot/PositionSlice` | PI位置、Stage剩余逻辑块、Issue与复盘；1号位不直接据原始StageProgress扣PI总量 | 2号位位置闭合/快照、5号位位置算法、1号位消费TaskDraft | **ODS统一视图：5号位；APS快照和闭合：2号位；算法Result：5号位** |
| **排程结果解释与读模型**（2026-05-13 v3.17 阶段二三接缝新增） | 1号位排程引擎内存计算产物（ExplanationFact）+ Task / Pegging 落库后聚合计算（Summary 三张表）——APS 内部计算产生，无外部源 | N/A（APS 内部计算产物）；⚠️ **职责边界**：`ExplainTrace`（现有，轻量 Task 级追踪日志，文本战报输入）≠ `ScheduleExplanationFact`（v3.17 新增，结构化原因事实层，供 AI / 页面 / 比较复用）——共存不替代 | 1号位在排程推演内存中产出 `ExplanationFactDraft`（含 ObjectType / OrderId / TaskId / ResourceId / StageCode / ReasonCode / Severity / ImpactHours / EvidenceJson）；不落 DB，传递给 2号位；`EvidenceJson` 外壳稳定，各 `ReasonCode` 内部 schema 随阶段演进 | `ScheduleExplanationFact`（2号位与 Task/Pegging 同批次落库，阶段一最小骨架即用）；`PlanKpiSummary`（版本级总 KPI）+ `OrderScheduleSummary`（订单级摘要：计划完工/延期/风险/主因/VIP标记）+ `ResourceLoadSummary`（资源×日期：负荷小时/可用小时/负荷率/是否瓶颈）；**首先服务阶段一页面/战报/KPI**，随后扩展到 Scenario 比较 + AI 查询；⚠️ **不参与排程计算**，2号位在 Task 落库后异步后处理生成 | `ScheduleExplanationFact`；`PlanKpiSummary`；`OrderScheduleSummary`；`ResourceLoadSummary`；最终消费：页面 / 战报 / 仿真比较 / Skill API | 3号位/前端（阶段一）；Scenario 多方案比较（阶段二）；Agent（阶段三） | **1号位（内存产出 ExplanationFactDraft，不直接写 DB）**；**2号位（持久化 ExplanationFact + 生成 Summary 读模型）**；**3号位/应用层（消费战报/页面）**；**ReasonCode 枚举口径：0号位审批** |
| **Candidate订单快照**（v3.30 2026-07-14 新增） | Base ACTIVE PlanVersion 的 `Order` 表 + 白天增量进入 `Order_Canonical` 的最新目标变化 | N/A（APS 内部对象）；禁止修改 Base/ACTIVE 版本 Order 数据 | 2号位执行`PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)`：①复制Base PlanVersion的Order快照；②只对`scopeJson.OrderCanonicalIds`指定的目标订单，叠加最新Order_Canonical变化；③写入Candidate PlanVersion独立`Order`分区；不修改Base/ACTIVE版本；排程后续只消费Candidate Order，不直接消费Order_Canonical | Candidate PlanVersion 下的独立 `Order` 快照（物理同表，按 PlanVersionId 分区隔离；OrderCanonicalId 为稳定关联字段；`OrderBomRequestLink` 具有 `UNIQUE(PlanVersionId, OrderCanonicalId)` 唯一约束，Order 表本身按 PlanVersionId 隔离） | `ScheduleContext.Orders`（Candidate运行时内存） | 1号位引擎（Candidate排程推演）；2号位（快照构造）；3号位（触发） | **快照构造：2号位**；**目标变化口径：0号位审批** |
| **异常变化闭环**（v3.34升级） | MES实时资源/工单/进度契约视图；Order_Canonical变化事实 | 2号位按评估DataCutoffTime形成现实事实切片；5号位执行ImpactAssessment并返回Recommendation/ExplanationFact。设备故障不自动暂停/恢复Task、不自动创建运行或Candidate | PMC确认后由3号位创建单Domain ScheduleRun；Candidate重新读取MES现实累计事实，但库存/在途仍受Base准确切片保护 | 运行时Result/Recommendation；`ScheduleRun`由3号位创建 | 看板告警、PMC决策、白天Candidate | 5号位评估、2号位事实切片、3号位编排、4号位展示 | **无MQ事件累计和MES_Actual_Staging；2号位仍是状态唯一修改者** |

---


### 2.x 现有代码对象与冻结设计映射（v3.36）

| 现有代码/目标对象 | 业务理解 | 是否新增 |
|---|---|---|
| `PeggingRuleVoucher` | 5号位给出的“这笔需求可以使用这项供给多少”的判断 | 现有对象继续使用；不强制改名 |
| `PeggingLedgerEntry` | 计算过程中已经成功扣减的一笔内存记录 | 现有对象继续使用并补充落库映射 |
| `PeggingAllocationLedger` | 排程结束后可查询、可审计的统一分配总账 | 数据库目标表已存在；2号位补实体与批量落库 |
| `PeggingSupplyAllocation` | 非Task供给使用结果明细 | 现有实体继续使用，不替代统一总账 |
| `ExecutionLock` | MES现实工单的跨版本稳定身份 | 当前代码无则按DDL最小新增 |
| `ExecutionLockTaskLink`、完整事件溯源、独立锁平台 | V1不需要 | 明确不新增 |


### 2.y V1最终编码契约（v3.37）

**SupplyBusinessKey**：

| 类型 | 格式 |
|---|---|
| PI | `PI|ProductionInstructionNo` |
| INVENTORY | `INV|ERP|FactoryCode|WarehouseCode|MaterialCode` |
| DOCUMENT | `DOC|ERP|DocumentType|DocumentNo|MaterialCode|DestinationWarehouseCode` |
| PURCHASE_ORDER | `PO|ERP|PurchaseOrderNo|MaterialCode|ReceivingWarehouseCode` |
| EXECUTION_WITHOUT_PI | `EXEC|MES|MESWorkOrderNo` |
| VIRTUAL_PI | `VIRTUAL_PI|RootDemandOrderCanonicalId|MaterialCode|StageCode` |

**主链**：

```text
SchedulingOrchestrator
→ Phase 1.6内存TaskDraft
→ PeggingOrchestrator纯内存扣减 + PeggingLedgerEntry
→ 1号位返回FinalTaskDrafts + AllocationShares
→ 2号位单Domain显式事务持久化
→ 后续独立PlanVersion激活事务
```

**身份来源**：
- `ScheduleRunId = PlanVersion.SourceScheduleRunId`
- `DomainKey = PlanVersion.DomainKey`
- `Root/DemandOrderCanonicalId = Order.OrderCanonicalId`
- 上述任一缺失不得以0或“最新记录”兜底。


## 3. 每类数据的一句话理解

### 3.1 BOM（2026-04-23 v3.8更新为R17/R27工厂映射+错误容错；2026-05-08 v3.14更新为订单粒度+BOMNO可空；2026-05-25 v3.20更新为OrderCanonicalId锚点+OrderBomRequestLink；2026-05-25 v3.21更新为RequestDetail字段收敛）
BOM 不是"直接同步来"也不是"单纯表转表"，而是 **先通过 `MES_BOM_Edge_Active`（v3.18 物化防腐边表，承接 ERP/MES 多源 BOM 标准化 + 唯一默认版本裁决；`MES_BOM_View` 降为兼容视图）处理，再按 90 天活跃订单粒度写入 `MES_API_BOM_Request_Detail`（v5.0.32：定位为纯请求输入表；`OrderCanonicalId`为主锚点；仅保留 `MaterialCode`+`FactoryCode`+`OrderType`+`RequestedBOMNO`；`Model`/`OrderStagingId`/`ResolvedBOMNO` 已删除；展开完成后 2号位生成`OrderBomRequestLink`承接`ResolvedBOMNO`）进入 ODS 递归展开工作集（`sp_ExpandBOMBatch`透传3辅助字段），由 5 号位后置回填三件事物**（⚠️ BOM入口解析分流——有RequestedBOMNO直接展开/无RequestedBOMNO从MaterialCode推导——由5号位Workset阶段负责）：
1. `ChildRequiredStageCode`（子件供给阶段码）+ **`ChildRequiredFactory`**（v3.8 新增，R17 将 Produce 映射到 APS 自定义 5 厂枚举：CN/CN6课/BJ/TJ/SH）
2. `StageDetail`（EDGE 子件供给路径 + ROOT 根产品完工路径，v3.7 统一阶段路径）
3. **`MES_APS_BOM_Workset_Issues`**（v3.8 新增诊断登记：R27 的 LEAF/FACTORY_MISMATCH + NO_STAGE/CYCLIC_BOM/UNKNOWN_PROCCODE 等 9 类异常；**v3.9 口径更新**：Severity 分 INFO/WARN/ERROR/CRITICAL，+`DegradeAction` 列；**全部降级登记、永不阻塞批次**）

最后 2 号位拉到 APS 本地（`APS_BOM_RAW` +`ChildRequiredFactory` + `APS_BOM_STAGE_PATH_RAW`）做 LLC 和内存快照。

【v3.9 设计决策（替代 v3.8 放行策略）】
- **批次永不阻塞**：防腐层只做"吸震 + 登记"，不做"生产准入判断"；状态机永远走 READY；`FAILED` 仅保留给 SP 进程崩溃
- **降级 + 登记**：9 类 IssueType 全部配 `DegradeAction` 标签（`STAGE_NULL` / `FACTORY_FALLBACK` / `QTY_DEFAULT_1` / `CYCLE_SKIP` / `BOMNO_SKIP` / `PRODUCE_DEFAULT_1`）
- **规则资产化**：R17/R20 Produce→工厂映射落 **`ProduceToFactoryMap`**；StageCode 全局字典落 **`StageDict`**（方案 B 工厂+阶段码）
- **R20 跨组织交接**：`ShouldDrilldown=1`（本厂仍下钻拿下阶明细）+ `CrossOrgHandoffFlag=1`（标记他用方排产，本厂不占产能）
- **运营 SLA**：INFO 不关注 / WARN 月度巡检 / ERROR 次日晨会 / CRITICAL 追责 SP 本身
- **稳定合同原则**：Workset 仅新增 `ChildRequiredFactory` 1 列、StageDetail 核心表零改动；ERP 特征字段（ProcessCode/ActualFactory/TrusteeProcCode）**不下沉**到 L1/L2，统一走 ODS 内部的 `vw_MES_BOM_Stage_Enriched`（**非防腐层视图**，APS 本地不做对称）
- 【v3.7】ROOT 记录 `ParentMaterialCode=NULL`；1 号位消费查询必须按 `StageScopeType` 区分。

### 3.2 Master / Material（2026-04-01 v4.0更新；2026-05-30 v5.0.37 产品族解析链补充）
主数据不是"原样抄一份"，而是 **ERP + MES 双源同构契约（视图字段完全一致）后，通过统一 `sp_SyncMasterData(@SourceType)` 三表协同同步：`Material`（MaterialType由APS按前缀推导）+ `MaterialMapping`（统一SourceID+Warehouse桥接）+ `MaterialSupplyContext`（仓库级供给上下文含InventoryManagementMode）**。

**v5.0.38 V1口径修正——产品族解析链**：⚠️ v5.0.37 的三表+`sp_ResolveMaterialProductFamily` 方案**已被 v5.0.38 V1简化方案覆盖**。**V1 不建三张规则表，不执行 `sp_ResolveMaterialProductFamily`**。V1 口径：① `ERP_Master_View` / `MES_Material_View` 由 **5号位直接暴露** `IsProductFamilyRequired`、`ProductFamilyCode`、`FamilyResolveStatus` 三字段（无需中间规则表）；② `sp_SyncMasterData` **步骤1c** 只做码表映射：`ProductFamilyCode` → `ProductFamily.Code` → 写入 `Material.ProductFamilyId`（仅 `FamilyResolveStatus='RESOLVED'` 时更新；未解析物料 `ProductFamilyId` 保持 NULL）；③ `Order.ProductFamilyId` 从 `Material.ProductFamilyId` 继承，**禁止订单层另建解析链**。V1 调度序列中**无** `sp_ResolveMaterialProductFamily`；`MaterialProductFamilyScopeRule` / `MaterialProductFamilyRule` / `MaterialProductFamilyResolved` 和 `sp_ResolveMaterialProductFamily` **仅作 V2 预留**，不进入 V1 正式链路。

### 3.3 Routing（2026-04-01 v5.0重构，v5.0.1变更 2026-04-02，v3.6更新 2026-04-13，v3.10 三层模型收敛 2026-04-24）
v5.0 工艺从“线性顺序表”升级为“**工艺图模型**”：原 `MES_APS_Routing_View` 废弃，拆分为 `MES_APS_Routing_Operation_View`（工序节点）+ `MES_APS_Routing_Dependency_View`（工序依赖边，支持装配车间并行/串行混合）+ `APS_OperationResourceEligibility_View`（工序资源能力，替代静态 ResourceGroup）；原 `Routing` 表废弃，装载目标改为 `RoutingOperation` + `RoutingDependency` + `OperationResourceEligibility`；排程参数（MinBatchSize/MaxBatchSize）拆入 `RoutingPlanningParam`。**v5.0.1变更：ODS视图输出`MES_ID`+`Model`（非MaterialCode），2号位装载时通过MaterialMapping统一映射得到MaterialId。** **v3.6更新：`RoutingStage`定位调整为阶段字典/标准阶段语言（不作为排程权威阶段顺序源，MES工艺侧不含外协阶段）；排程权威阶段顺序来自`StageDetail`（5号位基于ERP BOM辅助字段推导）；新增`StageLeadTimeParam`参数化外协阶段提前期（1号位对无RoutingOperation的阶段查此表生成标准Task）。**

**v3.10 三层模型收敛（2026-04-24）**：
1. **三层分层模型**（不可混用）：
   - 第 1 层：`OperationCode` / `OperationName` = 具体工序（NC / MC / 切断 / 精修），执行粒度
   - 第 2 层：`ProcessType` = **辅助分类标签**（MACHINING / ASSEMBLY / INSPECTION 等，值域见新增 `ProcessTypeDict` 骨架）——**不参与排程对接**，仅统计/报表/粗分组
   - 第 3 层：`StageCode` = 业务大工艺阶段码（TJ_MACH / BJ_PAINT，值域权威在 `StageDict`）——**BOM↔Routing 对接主键之二**
2. **BOM↔Routing 对接主键** = `(MaterialCode, StageCode)` 二元组：1 号位按此从 `RoutingOperation` 取该阶段下的小工序，结合 `RoutingDependency` 生成 Task
   - **业务口径主键** = `(MaterialCode, StageCode)`（文档/沟通/经验库统一用）
   - **数据库运行态主键** = `(MaterialId, StageCode)`（物理表 JOIN/索引用；`MaterialId` 是 `MaterialCode` 经 `MaterialMapping` 得到的标准化代理键）
   - 二者等价，不是两套键；详见字段文档 §1.9c
3. **StageSeq 唯一权威源** = `StageDetail.StageSeq`；`RoutingStage.StageSeq` 已从 DDL 中**删除**（跨物料/跨根产品语境下 MES 给不出正确值）
4. **R20 跨组织视角统一**：`StageDetail.StageCode` 采用**目标工厂视角**（父件 TJ + R20 指派 BJ → 直接写 `BJ_MACH`），1 号位读 StageCode 直接去目标工厂的 RoutingOperation 找小工序，Task 自动落在目标工厂产能队列，无需跨厂翻译逻辑
5. **StageCode 强约束**：`RoutingOperation.StageCode` / `RoutingStage.StageCode` / `StageDetail.StageCode` **必须**取自 `StageDict`；MES 本地叫法由 `MES_APS_Routing_Stage_View` 负责映射标准化
6. **OperationCode 不全局字典化**：MES 侧不可控 + 新增频繁；跨厂对接靠 StageCode 已足够

### 3.4 Order（2026-05-13 v3.16 OrderType重构+DelayStatus新增）
订单是 **同步来的，不是算出来的**；完整链路为 `v_APS_SalesOrder`（ERP契约视图，UNION ALL SO+MTS，含TransportMode/CustomerName/MTS_InstructionNo/IssueDate/OriginalDueDate/ReceivedQty(仅MTS)源事实字段；OrderType在此层为ERP原始值）→ `ERP_Order_Staging`（验证清洗，状态机 PENDING→VALIDATED→PROCESSED/FAILED）→ `sp_ValidateAndPromoteOrders`（校验+Upsert+APS衍生字段标准化：①OrderType重分类v5.0.24（SO/MTO→`SALES_ORDER`；MTS/SS/SS_U→`PRODUCTION_INSTRUCTION`）；②通过`CustomerCodeMap`推导`CustomerSegment`（CustomerCode为空→NULL；CustomerCode有值但无IsActive=1匹配→`UNKNOWN`（v5.0.27口径，不再默认OVERSEAS））；③CustomerTier推导（VIP>KEY_ACCOUNT>STANDARD>GENERAL，默认GENERAL）；④推导`DelayStatus`（ON_TIME/FIRST_DELAY/REPEATED_DELAY，v5.0.24新增独立字段）；键=SourceSystem+SourceOrderId）→ `Order_Canonical`（防腐层核心表，含全部源事实+APS衍生结果字段包括DelayStatus）→ `sp_SyncOrdersToPartitionTable`（每天00:05，补齐MaterialId/FactoryId等 + 透传全部业务字段包括DelayStatus）→ `Order`（业务分区表）→ `ScheduleContext.Orders`。【业务澄清】`DemandMaturityStatus`已收窄为PRE_CONFIRMED/FORECAST；DELAYED已拆出为独立字段`DelayStatus`，禁止混用。ReceivedQty仅MTS有值，用于判断剩余待排数量（RemainingQty在排程引擎内存中计算）。

### 3.5 Inventory（v3.24更新；v5.0.40补充明细层）
库存是 **双源事实 + 契约防腐 + 候选供给池 + 规则裁决 + 可用供给明细 + 余额汇总 + 内存加载**，六层链路，不是单表硬扛；完整链路：`InventoryFact_ERP`/`InventoryFact_MES`（WarehouseCode 主链，物理事实保留）→ `MaterialMapping` 桥接 → `InventorySupplyCandidate`（候选供给池，IsEligible=0 初始）→ `InventoryAvailabilityRule`（统一可用规则，胜出规则裁决，替代旧 `ProductFamilyInventoryScope`+`InventorySourceRule`，v5.0.39 删除）→ **`InventoryAvailableSupplyDetail`（可用供给明细层，v5.0.40 新增；保留 SourceSystem/StorageCode/AvailabilityRuleId/RulePriority/InventorySupplyCandidateId；`InventoryBalance` 负责"总量够不够"，本表负责"从哪里来、按什么顺序扣"）** → `InventoryBalance`（按 MaterialCode+ProductFamilyId+FactoryId 汇总，`ProductFamilyId`=库存使用上下文；`BatchNo`=快照标签；V1 `AllocatedQty`固定0，不承载订单占用）→ 内存预加载；排程判断初始总量读 `InventoryBalance`，解释来源或分优先级扣减读 `InventoryAvailableSupplyDetail`，实际占用只写当前Domain内存余额、Ledger和非Task分配细账；统一管道供给通过独立`SupplyFact_Pipeline`正式参与Pegging且不影响InventoryBalance定义；排程前必须一次性预加载进内存，排程中严禁再按需查库。  
**`InventoryAvailableSupplyDetail` 定位补充**：它位于 `InventoryAvailabilityRule` 与 `InventoryBalance` 之间，不改变库存规则模型，而是保存规则裁决后的仓库级、来源级、优先级明细。`InventoryBalance` 从该表汇总生成。若未来只做总量判断，理论上可直接从规则生成 `InventoryBalance`；但当前 APS 需支持库存来源解释和扣减优先级，因此 V1 保留本表。

### 3.6 管道供给 / PipelineSupply（v3.32统一主链）
管道供给是**并行于现货六层库存主链的未来供给事实链**，不并入`InventoryBalance`，但正式参与Pegging。V1统一纳入厂间在途、采购在途、VMI、已到厂未入库等已确认来源；每类来源保留自己的`SupplyType`、单据键和AvailableTime语义。

完整链路：

```text
各来源ODS契约视图
→ APS包装层 ext_PipelineSupply_Source_View（显式字段UNION ALL）
→ sp_SyncPipelineSupply(@BatchNo,@DataCutoffTime)
→ Material/Factory映射 + SourceBusinessKey去重 + 可用资格裁决
→ SupplyFact_Pipeline准确批次切片
→ ScheduleContext.PipelineSupplies
→ PeggingAllocationDecision
→ PeggingSupplyAllocation + PeggingAllocationLedger
```

关键口径：
- `ETA`是来源系统事实；`AvailableTime`是APS本地派生，两者不得混用；
- 厂间在途唯一性按来源单据、行号、物料、来源/目的工厂控制；采购/VMI/已到厂未入库按各自单据键控制；
- 同一现实数量从“在途→Received→库存”转换时，通过稳定来源业务键和状态互斥避免重复供给；
- Pipeline被需求采用后写`PeggingSupplyAllocation`，不能再作为未分配供给重复使用；
- Candidate不得全局读取当前全部在途，必须基于Base ACTIVE对应运行的准确切片，结合HardLock、ExecutionLock和Scope内/外Soft状态重建。

`SupplyAvailabilityRule`是否直接承载全部管道类型的可用规则，后续在DDL/字段说明同步时按既有规则引擎边界收口；不得为每一种来源单独复制一套通用规则平台。

### 3.7 排程运行编排（v3.30 2026-07-14 白天实时正式链路升级）
排程运行编排不是"一张表记录一次跑批"，而是 **四表分工 + 双链路（夜间全量 / 白天实时）+ ScopeJson唯一权威 + 激活边界严格管控** 的完整编排体系。

**四表分工**：
- **`ScheduleRun`**（运行过程/运行级状态机；一次 FULL_SCHEDULE 运行对应**多个 Domain PlanVersion**，各 Domain 经 `SourceScheduleRunId` 关联回同一 ScheduleRun）：含 `ScopeJson` 固定 **11字段**作为运行范围唯一权威，创建后 ScopeJson 不可变，后续服务只读不改。11字段：`Purpose / OrderCanonicalIds / FactoryIds / ProductFamilyIds / ResourceGroupIds / PlanHorizonStart / PlanHorizonEnd / LockedTaskIds / AllowTouchFrozenZone / AllowDelaySalesOrder / MaxImpactedOrders`。页面便捷参数必须在创建ScheduleRun前归一化进ScopeJson；与已有ScopeJson冲突时拒绝，不静默覆盖。**`ExpectedDomainKeysJson` 是 `ScheduleRun` 的独立的运行级字段，不属于 ScopeJson（ScopeJson 内不得出现 ExpectedDomainKeys 之类嵌套）**；运行启动时冻结（记录本次预期参与计算的 DomainKey 集合；**不得按已建 PlanVersion 反推**）。**`ScheduleRun.Status` 值域与定义**：`RUNNING`=仍有预期 Domain 未进入终态；`COMPLETED`=所有 ExpectedDomainKeys 对应 Domain 均进入运行成功终态（夜间 FULL_SCHEDULE=对应 PlanVersion 已 ACTIVE；白天 Candidate=对应 PlanVersion 已 CANDIDATE；完整 RunType 状态矩阵见防腐层 §2.8.7）；`PARTIAL_SUCCESS`=部分 Domain 成功、部分失败或缺失（仅用于多预期 Domain 运行）；`FAILED`=运行级致命错误或零成功。**所有终态（含 PARTIAL_SUCCESS）均写入 `CompletedAt`**。
- **`PlanVersion`**（结果版本/**一次 FULL_SCHEDULE 运行按 DomainKey 一个 Domain 一个 PlanVersion**，BUILDING-CANDIDATE-ACTIVE-ARCHIVED-FAILED，`SourceScheduleRunId` 反向追溯同一 ScheduleRun）→ Task/Pegging/Summary
- **`Scenario`**（试算场景+假设+目标+选中版本，白天实时评估适用时建；INSERT_ORDER_WHATIF通常可创建Scenario）
- **`SimulationRun`**（算法执行记录，阶段二骨架）

**Scenario业务容器已进入白天实时链路；SimulationRun和ScenarioObjectiveScore仍为阶段二骨架。**

**夜间全量（FULL_SCHEDULE 多 Domain）**：00:38 创建 ScheduleRun 并冻结 `ExpectedDomainKeysJson`（运行级不可变，确定 DataCutoffTime）→ 02:00 按 Domain 创建多个 BUILDING PlanVersion（每个 DomainKey 一个，经 `SourceScheduleRunId` 关联同一 ScheduleRun）→ 各 Domain **独立计算、落盘、发布** → 全部成功为 `COMPLETED`（所有 ExpectedDomainKeysJson 成功）；部分成功为 `PARTIAL_SUCCESS`（部分 Domain 成功、部分失败或缺失）；运行级致命错误或零成功为 `FAILED`。**一个无关 Domain 失败不得阻止其他已成功 Domain 发布**（废止旧 ALL_OR_NOTHING 单版本全成功/全回退口径）。

**白天实时评估（v3.30 正式链路）**：
1. 3号位归一化ScopeJson
2. 3号位创建Scenario（适用时）
3. 3号位创建ScheduleRun，写入：`BasePlanVersionId / StrategyProfileVersionId / ScopeJson / ExpectedDomainKeysJson / DataCutoffTime`
4. 3号位创建独立的Candidate类PlanVersion版本壳（初始Status=BUILDING）
5. 2号位构造Candidate Order快照（`PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)`）
6. 2号位BOM复用判断+EnsureRealtimeBomReady+PullRealtimeBOMResult
7. 2号位BuildRemainingSupplyContext：先按已消耗、ExecutionLock、PUBLISHED MESPlanRelease、普通位置切片互斥重建物理身份，再在每项物理供给内部区分HardLock、Scope外Soft、Scope内Soft和未分配余额
8. 2号位BuildScheduleContext：恢复PI位置切片、现实执行、待MES承接发布承诺及其Hard/Soft归属，禁止把HardLock当作新增物理数量
9. Pegging Ledger → LogicalBlock/TaskDraft → 1号位有限产能合并/拆分及排定 → ScheduledTaskDraft
10. 2号位批量持久化Task/ShippingTask/Ledger/PeggingSupplyAllocation/物理Pegging → PlanVersion=CANDIDATE → ScheduleRun=COMPLETED（写 CompletedAt）
11. 经审批后，3号位调用`ActivateCandidatePlanVersionAsync`（Serializable事务）（激活发生在 ScheduleRun 已完成之后，激活不回改 ScheduleRun 状态）

**RunType+Purpose 白天精确组合**（CTP和INSERT_IMPACT_ANALYSIS是ScopeJson.Purpose，不是RunType）：
- `INSERT_ORDER_WHATIF + CTP`：承诺交期计算，永不激活
- `INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS`：插单影响分析，永不激活
- `LOCAL_RESCHEDULE + INSERT_RESCHEDULE`：插单局部重排，审批后允许激活
- `LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT`：人工局部调整，审批后允许激活
- `MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT`：单Domain内较大范围人工重排，审批后允许激活

`MANUAL_RESCHEDULE` 不要求先建 `Scenario`。`LOCAL_RESCHEDULE` 必须同时填写 `PlanHorizonStart` 和 `PlanHorizonEnd`，且至少有一个范围数组非空。所有范围和时间均空只允许 `MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT`。`MaxImpactedOrders` 有值时必须大于0。

### 3.7c 多 Domain 编排与跨域依赖（V1 口径，2026-07-20 v3.31 新增）

- **一次 FULL_SCHEDULE ScheduleRun 对应多个 Domain PlanVersion**：每个 `DomainKey` 一个 `PlanVersion`，经 `PlanVersion.SourceScheduleRunId` 关联回同一 `ScheduleRun`；`ScheduleRun` 不再持有单版本语义，其状态由 2号位按全部 `ExpectedDomainKeysJson` 的终态汇总得到（见 §3.7 状态机；汇总对应集成接口步骤5.3）。
- **Domain 独立计算 / 独立落盘 / 独立发布**：各 Domain 的排程内核、结果持久化、版本发布互不影响；一个无关 Domain 失败不得阻止其他已成功 Domain 的发布（废止旧 ALL_OR_NOTHING「一个 Domain 失败全部回退」）。
- **BUILDING 失败终态与运行级致命错误**：某 Domain 排程重试耗尽或确认不可恢复，必须将该 Domain 的 `PlanVersion` 转 `FAILED` 并写 `ErrorMessage`，不得无限期停留 `BUILDING`；运行级致命错误（如编排进程崩溃）下，已创建但未完成的各 Domain `PlanVersion` 统一标记 `FAILED`，不得让 `ScheduleRun` 永久 `RUNNING`；2号位状态汇总（步骤5.3）**仅在全部 `ExpectedDomainKeysJson` 对应 `PlanVersion` 进入 `ACTIVE` / `FAILED` 后才汇总 `ScheduleRun` 终态**。
- **跨域依赖 V1 处理**：V1 仍按 Domain 独立发布，不做原子跨域激活。某 Domain 因上游/依赖 Domain 失败而产生版本不一致时，生成 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 原因事实（对应 `ScheduleExplanationFact`）+ `RescheduleRecommendation`，交由 PMC / 0号位人工判断是否对相关 Domain 重算。**V1 不自动回滚已成功上游、不建跨域多 Domain Candidate、不建原子激活组。**
- **白天 Candidate 严格单 Domain**：一个白天 ScheduleRun → 一个 DomainKey → 一个 `BasePlanVersionId`（当前 ACTIVE）→ 一个 Candidate PlanVersion。若 PMC 选择多个跨域 Domain 重算，由后台按 `Domain_Dependency` 拆分成多个单域重排（每个拆出域各自走单 Domain Candidate 链路），不建跨域聚合 Candidate。

### 3.7a 规则与参数引擎及策略模块边界（v3.32收敛）
规则与参数引擎通过`RuleSetVersion / ParameterSetVersion / StrategyProfileVersion`形成可发布、可追溯的策略包；大多数业务变化优先参数化。V1“插件”只表示少数稳定.NET策略接口，可由依赖注入选择实现，不建设动态插件目录、运行时装卸程序集、第三方插件市场或万能脚本引擎。

V1保留的主要策略接口：需求优先级、供给资格/HardLock、供给排序、PI位置计算、部分份额绑定、缺口处理。数量公式、Stage累计差分、异常下修、SourceBusinessKey去重、Ledger闭合和批量持久化属于普通领域函数或框架机制，不单独插件化。5号位只执行策略并返回只读Result或`PeggingAllocationDecision`；2号位拥有余额、状态和持久化。普通计算结果不包装为Voucher，只有正式审批或状态变化使用Voucher。

### 3.7b 跨厂Pegging、Received与非Task供给（v3.32补强）
`ERPProperty`来自ERP真实属性，由5号位同步透出；2号位通过`MES_ProcessCode_View`消费形成M库判定及Stage位置上下文。跨厂模式仍分为：`STAGE_HANDOFF`（有M库的大工艺接续）与`INTER_FACTORY_ORDER`（无M库的厂间订单合同）。

`INTER_FACTORY_ORDER`按同一出荷指示范围依次检查厂间在途、同单据ZP/BP Received，再进入生产排产；ZP/BP不进入通用池。特殊出荷指示类型可由规则命中`DemandSupplyHardLock`，跨版本继续固定给原需求；普通通用供给只形成当前PlanVersion的Soft分配。

`PeggingSupplyAllocation`记录库存、管道、Received等非Task已确认分配，`PeggingAllocationLedger`记录完整需求—供给数量血缘；物理`Pegging`只记录Task-to-Task。1号位不直接读取Received、ERPProperty或PeggingSupplyAllocation进行供给判断，只消费LogicalBlock/TaskDraft、约束和最终排程输入。

### 3.8 排程结果读模型（2026-05-13 v3.17 新增）
排程结果读模型是 **非排程内核、纯读取用途的物化汇总层**，由 2号位在 Task / Pegging 落库完成后异步后处理生成：`OrderScheduleSummary`（订单级：计划完工时间 / 延期小时 / 风险等级 / 主因代码 / 是否影响 VIP）+ `ResourceLoadSummary`（资源×日期粒度：负荷小时 / 可用小时 / 负荷率 / 是否瓶颈）+ `PlanKpiSummary`（版本级总指标：准交率 / 延期订单数 / 最大延期小时 / VIP延期 / 平均负荷率 / 瓶颈数 / WIP估算）。**首要价值**：阶段一页面、战报、KPI 仪表盘即可受益，不必等仿真功能上线；**延伸价值**：阶段二 Scenario 比较时直接对比汇总结果（不需重扫 Task 明细），阶段三 Skill API 直接查这三张表实现秒级响应。`ScheduleExplanationFact` 是独立的结构化原因事实层——1号位在内存推演中以 `ExplanationFactDraft` 形式产出（含 ObjectType / ReasonCode / ImpactHours / EvidenceJson），由 2号位与 Task/Pegging 同批次落库；`EvidenceJson` 内部 schema 随阶段演进，阶段一不冻结。⚠️ **`ExplainTrace` ≠ `ExplanationFact`**：前者是轻量 Task 级追踪日志（供文本战报输入），后者是结构化原因事实（供 AI / 多版本比较 / 前端深钻）——共存，职责不同，不替代。

### 3.8a ReasonCode 权威字典（V1 冻结，2026-07-20 v3.31 新增）

`ScheduleExplanationFact.ReasonCode` 是结构化原因事实的**唯一主因代码维度**，由 1号位内核打标、2号位同批次落库。V1 权威枚举**仅以下 15 项**，文中所有出现的 ReasonCode 必须属于此列表；任何新增/调整须经 0号位审批后单独修订本表，不得临时自定义：

| # | ReasonCode | 含义（V1 口径） |
|---|---|---|
| 1 | `RESOURCE_CAPACITY_WAIT` | 资源产能排队等待 |
| 2 | `MATERIAL_SHORTAGE` | 物料短缺 |
| 3 | `PRECEDENCE_WAIT` | 前序工序/任务依赖等待 |
| 4 | `FROZEN_ZONE_LOCK` | 冻结区锁定不可动 |
| 5 | `ROUTING_FALLBACK` | 工艺路由降级/回退 |
| 6 | `STAGE_LEADTIME_FALLBACK` | 阶段提前期降级 |
| 7 | `BOM_DEGRADE` | BOM 展开降级（含 Issues 降级登记） |
| 8 | `CROSS_ORG_HANDOFF` | 跨组织交接（R20 本厂不占产能） |
| 9 | `PRIORITY_LOWER_THAN_OTHERS` | 优先级低于其他任务 |
| 10 | `DUE_DATE_RISK` | 交期风险（**原 `DUE_DATE_VIOLATION` 已统一更名**，V1 不再使用旧名） |
| 11 | `LOGISTICS_DELAY` | 物流/在途延迟 |
| 12 | `PRIORITY_INHERITANCE` | 优先级继承（父/关联任务传递） |
| 13 | `CROSS_DOMAIN_VERSION_MISMATCH_RISK` | 跨域版本不一致风险（**上游延期等跨域不一致并入此项**；由跨域失败产生，配 `RescheduleRecommendation`） |
| 14 | `MANUAL_COMPLETED_SHORT` | 人工标记完工短缺/短装 |
| 15 | `EQUIPMENT_BREAKDOWN_RISK` | 设备故障风险（对应 RESOURCE_BREAKDOWN 不可用事实，V1 不自动暂停/恢复 Task） |

⚠️ **已废止/未登记示例**：`DUE_DATE_VIOLATION`（→ `DUE_DATE_RISK`）、`DUE_DATE_TIGHT`、`UPSTREAM_DELAY`（上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`）不在 V1 字典内，禁止在生产代码/落库事实中使用。

### 3.9 MES计划发布与工单现实链路（v3.34升级）
APS不直接暴露动态ACTIVE Task，也不主动调用MES建单接口。正式链路是：

```text
ACTIVE Task → MESPlanRelease(PUBLISHED)+ReleaseItemKey
→ APS_MES_PlanRelease_View → MES幂等建单
→ MES_APS_WorkOrder_View回传ReleaseItemKey+MESWorkOrderNo
→ MESWorkOrderSnapshot → MESPlanRelease(CONSUMED)+ExecutionLock+Task.RELEASED
```

一条MESPlanRelease对应一张未来MES工单，可关联同PI、同Stage下多个小工序Task。`MESPlanRelease.Quantity`是该Stage执行批次的单一流转数量，不是所关联小工序Task数量之和；同组数量不一致时必须先显式换算并闭合，否则不得发布。TaskNo只展示诊断。PUBLISHED但未建单的数量跨版本保持，不得重新进入普通竞争池。

### 3.10 MES工序进度汇总链路（2026-06-12 v3.26 新增）
MES 报工数据分散在不同大工艺中，因此 **ODS 层采用"各大工艺标准化子视图 + UNION ALL 统一契约视图"的分层结构**。链路：各大工艺报工表 → `MES_APS_OperationProgress_{大工艺}_View`（各大工艺标准化子视图）→ `UNION ALL` → `ODS.MES_APS_OperationProgress_View` → `APS.OperationProgressSnapshot` → `ScheduleContext.OperationProgressSnapshots`。**V1 设计决策三点**：① 工序识别主字段 = `OperationName`，不以 MES 工序编码为主（MES 编码不跨大工艺稳定）；② `RemainingQty` 为持久化计算列（`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END`），1号位全量重算前依此裁剪剩余 Task；③ 生产进度快照**绝对不进入 `InventoryBalance`**，二者在 `ScheduleContext` 中并存但严格独立。

### 3.11 MES大工艺进度汇总链路（2026-06-12 v3.26 新增）
大工艺进度是工序进度的上一层汇总，**颗粒度 = 生产指示号 + 物料编码 + 大工艺阶段码**。链路：各大工艺报工表 → `MES_APS_StageProgress_{大工艺}_View` → `UNION ALL` → `ODS.MES_APS_StageProgress_View` → `APS.StageProgressSnapshot` → `ScheduleContext.StageProgressSnapshots`。**消费优先级**：1号位排程引擎优先按大工艺进度扣减已完成量（粒度粗、性能好），需要工序级精度时再查 `OperationProgressSnapshot`。**Task/Pegging与执行事实口径**（v3.32升级）：Task、物理Pegging和普通Soft分配随新的`PlanVersionId`重新生成，MES进度不匹配历史TaskId；但现实MES执行通过跨版本`ExecutionLock`延续，新版本Task用`Task.ExecutionLockId`关联同一执行事实。Stage级互斥位置最终以`StageProgressSnapshot`为准；OperationProgressSnapshot仅用于阶段内部小工序裁剪和诊断。**EAM V1**：`EAM_APS_Resource_View` 预留占位，V1 不读取 EAM 数据，不生成设备不可用窗口，不影响当前快照同步流程。

### 3.12 Candidate 订单快照（v3.30 2026-07-14 新增）
Candidate 订单快照不是"复制一份 Order 表"，而是 **白天实时评估链路中 Candidate PlanVersion 的独立订单视图**。

2号位调用 `PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)`：
1. 复制 Base PlanVersion 的 Order 快照
2. 只对 `scopeJson.OrderCanonicalIds` 指定的目标订单，叠加最新 Order_Canonical 变化（数量/交期/优先级等）
3. 写入 Candidate PlanVersion 独立 `Order` 分区
4. 不修改 Base/ACTIVE 版本
5. 排程后续只消费 Candidate Order，不直接消费 Order_Canonical

**Order 表隔离说明**：Order 表按 PlanVersionId 隔离；OrderCanonicalId 作为稳定关联字段；Candidate 不修改 Base 版本。`UNIQUE(PlanVersionId, OrderCanonicalId)` 属于 `OrderBomRequestLink`，不属于 Order 表本身。

1号位排程引擎在Candidate运行时只消费本Candidate的`ScheduleContext.Orders`和2号位构造后的TaskDraft/约束，不直接感知Base订单或原始供给。Candidate供给状态重建见§3.17。


### 3.13 生产指示供给快照与Stage位置（v3.32新增）
生产指示供给快照回答两件事：**这张PI本次总共还有多少，以及这些数量现在分别在哪里**。总量唯一公式为`Order.Quantity - Order.ReceivedQty`；MES在制、Stage等待、XC和厂间在途只定位总量，不再次扣减。2号位按`ScheduleRunId + DomainKey + ProductionInstructionNo`保存`ProductionInstructionSupplySnapshot`，明细`ProductionInstructionPositionSlice`保存互斥位置、CompletedStage、NextRequiredStage、AvailableTime、来源键和IssueCode。

位置算法以Stage大工艺累计为权威：下游超过上游时下修下游；中间Stage缺失时采用下游已证明最小量；`ERPProperty=XC`且映射某Stage时，该Stage尚未完成。进入PI位置快照的XC必须能识别生产指示号，缺号XC不得按物料相同强行归入该PI；无法定位的PI数量进入`UNLOCATED`并从承载路径最早Stage保守排程。所有切片之和必须等于PI可生产量。

### 3.14 Pegging Allocation Ledger（v3.32新增）
`PeggingAllocationLedger`是Task生成前后统一的数量血缘权威。每笔`PeggingAllocationDecision`必须在2号位同一内存临界区完成“扣需求余额、扣供给余额、写Ledger”三个动作，任何一步失败均不得部分生效。 每条Ledger在PlanVersion内以`AllocationSequence`唯一，失败重试不得生成第二条相同序号。Ledger保存AllocationSequence、Demand、SupplyBusinessKey、PI Header、PositionSlice、AllocatedQty、AllocationMode、优先级继承、LogicalBlock，以及排程后回填的FinalTaskId/TaskComponentQty。

SoftAllocation不单独建表；由`Ledger.AllocationMode=SOFT/HARD`表达。库存、在途、Received等非Task供给被实际采用后同时写`PeggingSupplyAllocation`；物理Pegging仍只Task-to-Task。

### 3.15 MESPlanRelease、ExecutionLock与DemandSupplyHardLock（v3.34升级）
`MESPlanRelease`固定“已向MES公开但尚未/已经建单”的发布单元；其`Quantity`取Stage级执行批次单一流转量，禁止把同一批次下串行小工序Task数量累计相加。`ExecutionLock`固定现实MES执行；`DemandSupplyHardLock`固定指定数量归属。三者职责不同。

ExecutionLock数量约束：`CompletedQty + CancelledQty + RemainingExecutionQty <= OriginalExecutionQty`。`RemainingExecutionQty`由2号位按整张MES现实工单状态维护，只表示未来Stage产出承诺上限；小工序状态4不直接改变PI总量或关闭ExecutionLock。HardLock允许部分履约和部分释放，RemainingLockedQty>0时继续ACTIVE；FulfilledQty由累计履约/不可逆消耗事实覆盖式更新，ReleasedQty只来自规则或审批。Pipeline与Received仍保留单据身份时沿用同一DOC键并确保旧表示退出；真正进入普通池化库存并失去单据身份后使用INV键。严格绑定数量不得在转换前丢失DOC身份；PI位置转ExecutionLockedOutput继续沿用PI键。

PI位置、ExecutionLock、PUBLISHED MESPlanRelease是物理数量身份；Hard/Soft/未分配是归属状态。HardLock不能与物理身份直接相加，也不能创造供给。

### 3.16 TaskDraft、跨需求合并与需求份额查询（v3.32新增）
Pegging阶段不得提前创建正式Task。正确链路是：`Ledger → LogicalBlock → TaskDraft → 1号位有限产能合并/拆分与时间排定 → ScheduledTaskDraft + ComponentShares → 2号位正式持久化`。

V1允许不同需求合并成一个Task，只要ProductionInstruction、Stage、剩余路径、资格、AvailableTime、批量、产能和各需求交期均兼容。 正式生产Task必须落`ProductionInstructionNo + StageCode`，供MES下发和ExecutionLock创建直接消费；代表订单字段不得替代该执行身份。Ledger保存`FinalTaskId + DemandOrderCanonicalId + TaskComponentQty`，`vw_TaskDemandAllocation`提供查询；不重复建TaskDemandAllocation物理表。一个正式Task最多关联一个ExecutionLock，不同现实MES工单不得伪装合并。

### 3.17 Candidate供给与MES现实事实重建（v3.34升级）
Candidate的库存、在途、Received和Scope外Soft以Base ACTIVE准确切片为边界，不重新读取当前全部供给；但现实MES执行必须按Candidate自己的DataCutoffTime读取实时累计视图，形成独立MES快照并更新ExecutionLock/MESPlanRelease状态。

恢复顺序按两个维度处理：先形成互斥物理身份（已消耗、ExecutionLock、PUBLISHED发布承诺、普通位置切片），再恢复Hard/Soft/未分配归属。Scope内Soft释放，Scope外Soft保留。激活前现实事实变化则拒绝激活。

### 3.18 APS_Auth、分区与保留边界（v3.34新增）
APS_Auth使用独立脚本`APS_Auth数据库DDL_v1.0.sql`部署，不属于APS_Production主DDL。PlanVersion分区每100版本一个分区、预建至100000；ARCHIVED明细90天、FAILED半成品14天、Summary/KPI至少2年、终态锁对象1年，清理作业由DBA/Hangfire在测试验证后启用。

## 3.19 异常变化闭环（原3.13，v3.34顺延）
异常变化闭环不是"自动重排"，而是 **MES实时异常/资源状态事实 + 订单变化事实 → 5号位规则评估 → 返回建议/原因事实 → PMC 人工确认 → 3号位触发白天实时评估** 的人机协同闭环。设备故障**不**自动暂停/恢复 Task、不自动创建 ScheduleRun、不自动创建 Candidate。

**正式链路**：
```
事件或变化事实 → 5号位ImpactAssessment → 返回 Recommendation（含 ScheduleExplanationFact 原因事实）→ 4号位展示 → PMC人工确认 → 3号位创建ScheduleRun
```

**主要事件类型分支**（V1 设备故障不再产出 TaskPauseVoucher / TaskResumeVoucher，历史已废止口径，仅供追溯，V1 不落地）：
- `RESOURCE_BREAKDOWN`（设备故障）→ 5号位ImpactAssessment → 生成 **Resource 不可用事实** → `ScheduleExplanationFact`（原因事实）→ `RescheduleRecommendation` → 看板告警 → PMC 决定是否发起单域重排
- `RESOURCE_REPAIRED`（设备恢复）→ 5号位ImpactAssessment → 生成 **Resource 恢复事实** → 重新评估未关闭 Recommendation → 看板通知 → PMC 决定是否需要重排

**资源异常事实来源**：MES实时资源状态契约视图或既有设备事实视图；V1不建设MES_Actual_Staging或逐条事件累计。缺失字段展示“未提供”，不得推断。

禁止拼装或推断不存在的故障时间；V1直接消费契约视图与运行快照，不建设`MES_Actual_Staging`事件暂存模型。

订单变化写成"Order_Canonical变化事实"或"订单变化事件"。

**ImpactAssessment** 是规则评估结果对象；**Recommendation（及 `ScheduleExplanationFact` 原因事实）** 是5号位返回的运行对象/规则输出。Recommendation 由 PMC 人工确认，确认后由 3号位显式创建 ScheduleRun；Candidate 是否正式采用，另行通过 `CANDIDATE_ACTIVATION` 审批并调用 `ActivateCandidatePlanVersionAsync`。**两类动作相互独立，禁止将 Recommendation 确认或 Candidate 激活中的任一动作自动替代另一项。** V1 不启用 `RESCHEDULE_REQUEST` 审批回调；该类型仅作为 V2 可选扩展。不在本文件新增物理表。

**设备故障"四不"红线**（V1 不落地 TaskPauseVoucher / TaskResumeVoucher，历史已废止）：
1. 禁止系统自动暂停 Task
2. 禁止系统自动恢复 Task
3. 禁止系统因故障事件自动创建 ScheduleRun
4. 禁止系统因故障事件自动创建 Candidate

**六条"不自动"红线**：
1. 禁止系统因 Recommendation 自动创建 ScheduleRun；必须经 PMC 人工确认后由3号位显式创建（设备故障亦适用"四不"红线，不得自动创建）
2. 禁止5号位直接扣减库存
3. 禁止5号位直接生成最终 Task/ShippingTask
4. 禁止5号位写物理 Pegging
5. 禁止系统自动暂停/恢复 Task（见设备故障"四不"红线，V1 已废止 TaskPauseVoucher/TaskResumeVoucher 机制）
6. 禁止系统在 `CANDIDATE_ACTIVATION` 审批通过并调用 `ActivateCandidatePlanVersionAsync` 前自动激活任何 PlanVersion

`MANUAL_RESCHEDULE` 不要求先建 `Scenario`。

---

## 4. 当前版本的关键红线

1. **ODS 只负责数据转换和缓存，不包含业务逻辑。**
2. **`MES_BOM_Edge_Active`（v3.18新增，物化防腐边表；兼容视图 `MES_BOM_View` = `SELECT * FROM MES_BOM_Edge_Active`，不再作为递归展开对象） / `ERP_Master_View` / `MES_Material_View` / `MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `APS_OperationResourceEligibility_View` / `MES_APS_Routing_Stage_View`(v3.5新增，v3.6定位调整为阶段字典) / `MES_APS_Resource_View`（v3.11命名统一，原名 `APS_Resource_View`）/ `ERP_Inventory_View` / `MES_Inventory_View` 属于契约层，字段变更必须走审批。**（v5.0更新：`MES_APS_Routing_View` 废弃，拆分为 3 个视图 + 1 个资源视图 + 1 个大工艺阶段视图）
3. **`MES_API_BOM_Request_Detail` 写入前必须以 `(BatchNo, OrderCanonicalId)` 为唯一约束**（v5.0.31 升级；废弃旧 v5.0.21 `(BatchNo, OrderStagingId)` 口径；`RequestedBOMNO` 可空；按订单粒度写入，同批次内每个 `Order_Canonical` 最多一行明细）。
4. **BOM 的 `Quantity` 在 ODS 和 APS 中都必须是单位用量，严禁累乘。**
4a. **【v3.18新增】`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` 禁止直接对复杂 BOM View 做递归 CTE**；正式 Workset 展开只读可索引的 `MES_BOM_Edge_Active`；`MES_BOM_View` 如保留只是兼容视图，禁止作为递归展开执行对象。
5. **APS 排程时必须基于本地库和内存快照，严禁联网回查源系统或 ODS。**
6. **当前版本不建议在 APS 再维护一份全量 4000 万 BOM 主数据总账；若保留 3.5 `BOM` 表，建议只定位为"当前计划窗口标准 BOM 关系缓存"。**
7. **库存必须经过规则筛选后才能进入 `InventoryBalance`；旧三表（`InventorySourcePriority` v2.8废弃、`ProductFamilyInventoryScope` v5.0.39删除、`InventorySourceRule` v5.0.39删除）均已废弃；V1 统一使用 `InventoryAvailabilityRule`（`IsAvailable`=1 允许进入可用库存池；`IsAvailable`=0 主动排除；无匹配规则时 `IsEligible` 保持 0 + `RejectReason='NoRuleMatch'`，不进 `InventoryBalance`——v5.0.40 白名单模式，已删除"无匹配兜底"旧口径）。**
7b. **【v5.0.40 新增】`InventoryAvailableSupplyDetail` 保留，不可删除；它是规则裁决后、余额汇总前的可用库存明细层，负责保存 `RulePriority`（扣减顺序）、`SourceSystem`/`StorageCode`（来源仓库）、`AvailabilityRuleId`（命中规则追溯）；`InventoryBalance` 从本表汇总生成；`InventoryAvailableSupplyDetail.InventorySupplyCandidateId` 不加外键（避免阻塞 `TRUNCATE TABLE InventorySupplyCandidate`）；本表不是订单消耗明细表。**
8. **`Material` 表的 `IsPurchased` / `SafetyStock` / `LeadTimeDays` 字段已废弃，仓库级业务上下文统一由 `MaterialSupplyContext` 承载。**
9. **【v3.8新增】诊断/错误信息不得写入 `MES_APS_BOM_Workset` / `StageDetail` 核心表**，必须单独进 `MES_APS_BOM_Workset_Issues`（诊断表结构可演进，核心表合同稳定）。
10. **【v3.8新增】APS_Production 本地库禁建 `vw_APS_BOM_*` 对称视图**：ERP 特征字段（ProcessCode/ActualFactory/TrusteeProcCode）只在 ODS 侧的 `vw_MES_BOM_Stage_Enriched` 表达；APS 本地如需委外/受托信息，由 2 号位预计算落独立配置表（如 `StageLeadTimeParam` 扩展），**不直接下沉 ERP 字段语义**。
11. **【v3.8新增，v3.11.1 照片权威纠正】Produce 字段值域 0-11**（非 0/1/2）；`ChildSourceHintCode` 对应注释以此为准；R17 映射：**1=继承父件 / 5,8=CN6课 / 9=SH / 6=BJ / 7=CN / 11=TJ / 其他={0,2,3,4,10}=外购不推导**（原 v3.8~v3.11 误为 7=TJ、11=SH，已以 v5.0.14 ProduceToFactoryMap 照片权威为准修复）。
12. **【v3.11.2 新增；v3.12 修正定位】ProcessCode 防腐墙红线**：6 位 `ProcessCode` 及其派生字段 `ActualFactoryCode / TrusteeProcCode / WarehouseRole / IsRetouch` **严格只在 ODS 层活**；`APS_Production` 库严禁出现这些字段（无论是表列、视图列、还是存储过程变量）。APS 排程只消费"语义字段"：通过 `StageCode → StageDict` 获取 `FactoryCode / StageCategory`；委外/受托等 ERP 特征由 2 号位预计算落 `StageLeadTimeParam` 等配置表吸收。违反即打穿防腐墙。ODS 侧的消费方也一律通过 `MES_ProcessCode_View`（Socket-Plug 契约视图）读取，不得绕过直查 `ProcessCodeDict` 物理表。**🔄 v3.12 修正**：ProcessCodeDict 不是「ERP 工序对照表 ODS 镜像」，而是 **「APS 自维护的 ODS 增强工序字典」**——APS 系统管理员人工维护 + 0 号位审批；`sp_SyncMasterData(@SourceType=.ProcessCode.)` 不恢复；v5.0.46起ERPProperty由5号位从ERP真实属性同步/透出，专用同步不覆盖APS增强字段；新增 `StageCode` 增强列承担 ProcessCode → StageCode 共享映射（5 号位 / 2 号位统一查 `MES_ProcessCode_View.StageCode`）。
13. **【v3.11.2 新增】字典字段分层原则**：`StageDict` **只承载阶段自身属性**（即不依赖具体物料就能确定的语义：StageCode / StageName / FactoryCode / StageCategory / SortHint / Description）；任何"**物料×阶段联合属性**"（同一阶段对不同物料取值不同，如是否入库、入库角色、阶段 LeadTime、入库阈值点）**一律不放 StageDict**，分层承接如下：
    - `IsStockPoint`（是否半成品库存断点）：字典级删除 → 物料稳态配置走 `RoutingStage.IsStockPoint`（3 号位维护）→ BOM 边级派生走 `StageDetail.IsSupplyThreshold`（5 号位回填）
    - 阶段 LeadTime：走 `StageLeadTimeParam`
    - `IsOutsource`：与 `StageCategory='OUTS'` 语义重复，已删除；改用 StageCategory 判断
    - 验收准则：任何字段要进 StageDict 前，先问"同一 StageCode 对所有物料取值都相同吗？" 若否 → 字段错位，不应进入。
14. **【v3.12 新增】部门 = 物料 × 阶段联合属性**：生产部门**不是** `ProcessCode` 的属性、**不是** `StageCode` 的属性，而是 `(MaterialId, StageCode)` 联合维度。**严禁**把部门字段塞进 `StageDict` / `StageDetail` / `RoutingStage`；2 号位通过 `sp_RebuildMaterialStageDeptContext` 组装 `MaterialStageDeptContext` 表（键 `(MaterialId, StageCode)` → `DefaultProductionDepartmentId`），1 号位排程**唯一**消费此表。
15. **【v3.12 新增】1 号位排程主链定调**：排程取数路径 `(MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套 (MaterialId, ProductionDepartmentId, StageCode)`。**1 号位禁止直接读** `MaterialSupplyContext` / `ProcessCodeDict` / `MaterialStageDeptOverride`（这些是 2 号位组装 Context 的输入源）。MSC 是原始仓库级上下文，**不直接作为 1 号位入口**——必须经 2 号位 Context 组装。
16. **【v3.12 新增】ProcessCode → StageCode 基础映射全链路共享**：5 号位 `sp_EnrichBOMWorkset` 与 2 号位 `sp_RebuildMaterialStageDeptContext` 在做"`ProcessCode` 归属哪个 `StageCode`"判定时**必须查同一列**——`MES_ProcessCode_View.StageCode`（v5.0.16 新增）。**禁止各写一套规则**——否则两边映射不一致会造成 1 号位按 `(Material, StageCode)` 锁部门时静默断裂。"如何选出 ProcessCode"的上层场景逻辑（5 号位走 BOM 边、2 号位走 MSC + Override）不共享，但底层映射必须共享。
17. **【v3.12 新增】人工维护必须明确到 (Model/Material) × StageCode**：`MaterialStageDeptOverride` 表导入时业务人员可用 `Model` 录入（更熟悉），但 2 号位**必须做 `Model → MaterialCode` 1:1 检查**——若 `Model` 1:N 多个 `MaterialCode`，**拒收**并返回明细，要求业务确认到 `MaterialCode`。**禁止只维护 `Model → Department`**（部门是物料 × 阶段联合属性，不是物料单属性）。
18. **【v3.12 新增】Routing 三件套 `ProductionDepartmentId NOT NULL`**：业务确认 MES 工艺数据全部带部门，因此 `RoutingOperation` / `RoutingDependency` / `OperationResourceEligibility` 三张表 `ProductionDepartmentId` 列**强制 NOT NULL**；ODS 契约视图 `MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `APS_OperationResourceEligibility_View` / `MES_APS_Resource_View` 同步加 `ProductionDeptCode` 字段契约（DBA 走审批升级）。**不引入** `_UNSPECIFIED` 哨兵；映射失败的行登记 `APS_ETL_Log` 并跳过（不阻塞批次）。
19. **【v3.13 新增】工厂代码值域基准**：`Factory.Code`（NVARCHAR(50), UNIQUE）是全系统工厂代码的**唯一权威值域**；所有表/视图/SP 中出现的 `FactoryCode` 字面值都必须在 `Factory` 表中能命中对应的 `Code` 记录。JOIN 时引用列名是 `Factory.Code`（**不是** `Factory.FactoryCode`，DDL v5.0.17 已修正两处 SP Bug）。
20. **【v3.13 新增】ODS / 契约 / 字典层只保留 `FactoryCode`（字符串），不做跨库 FK**：`StageDict.FactoryCode` / `ProcessCodeDict.FactoryCode` / `Order_Canonical.FactoryCode` / `StageLeadTimeParam.FactoryCode` 均为 **NVARCHAR 字符串**——因 ODS 与 APS_Production 不在同一数据库，不建跨库外键。字符串值域由 `Factory.Code` 间接保证（SP 映射时校验）。
21. **【v3.13 新增，v3.30 口径扩展】APS_Production 本地落 `FactoryId`（INT FK）的表范围**：排程主链对象（RoutingOperation/RoutingDependency/MaterialStageDeptContext等）不重复存 `FactoryId`，通过 `ProductionDepartmentId→PD.FactoryId` 或 `ResourceId→Resource.FactoryId` 或 `StageCode→StageDict.FactoryCode` 间接归厂；具有独立工厂业务维度的表（订单、资源、部门、库存/供给事实、规则、余额表等）可持有 `FactoryId` FK，完整清单见 §6.2（v5.0.39已补充库存/供给类表，"原强关联5张"说法已过时）。其余表通过间接路径归厂（详见 §6 工厂归属关系总表）。
22. **【v3.13 新增】排程主链对象不重复存 `FactoryId`**：`MaterialStageDeptContext` / `RoutingOperation` / `RoutingDependency` / `OperationResourceEligibility` / `ResourcePlanningContext` / `Material` / `MaterialMapping` 均**不直接存** `FactoryCode` 或 `FactoryId`——通过 `ProductionDepartmentId → ProductionDepartment.FactoryId` 或 `ResourceId → Resource.FactoryId` 或 `StageCode → StageDict.FactoryCode` 间接归厂，避免冗余字段和一致性维护负担。
23. **【v3.14 新增，v5.0.32 升级】RequestedBOMNO 可空红线与 ResolvedBOMNO 归属红线**：`RequestedBOMNO`（订单原始携带）可空；NULL=待5号位Workset阶段解析BOM入口；2号位推送 `MES_API_BOM_Request_Detail` 时只填 `RequestedBOMNO`（请求输入字段），不预先解析BOMNO；`ResolvedBOMNO`（展开实际使用的BOMNO）不进 RequestDetail，由 **2号位在 Workset 同步完成后写入 `OrderBomRequestLink.ResolvedBOMNO`**；**BOM入口解析分流由5号位Workset处理阶段负责**，5号位只写 Workset/StageDetail/Issues。`FailureCode`（原因维度）和 `NextActionCode`（动作维度）为两个**独立字段**，**禁止混用**；`SyncStatus` 只表达技术流转状态。
24. **【v3.32升级】管道供给链独立且正式参与Pegging**：`InventoryBalance`定义不变，现货库存保持六层结构；厂间在途、采购在途、VMI、已到厂未入库等通过独立ODS契约视图和APS统一包装视图进入`SupplyFact_Pipeline`准确切片。ETA是来源事实，AvailableTime是APS派生；同一现实数量在在途/Received/库存之间必须按稳定业务键互斥转换，禁止重复供给。Candidate只能读取Base对应运行准确切片，不得全局读取当前全部在途。
25. **【v3.17 新增】ScheduleRun 统一运行编排框架**：`FULL_SCHEDULE`（凌晨全量）/ `MANUAL_RESCHEDULE`（人工重排）/ `LOCAL_RESCHEDULE` / `SIMULATION` / `INSERT_ORDER_WHATIF` 均通过 `ScheduleRun.RunType` 表达，共用同一运行编排对象；`ScheduleRun` 是对现有"直接生成 `PlanVersionId`"流程的**最小包装**，阶段一不改排程内核；`Scenario` 是 SIMULATION/WHATIF 类型运行的上层业务容器，**不是所有非正式运行的总容器**——`MANUAL_RESCHEDULE` 不要求先建 `Scenario`；**禁止将 `ScheduleRun` 与 `PlanVersion` 混淆**（运行对象 ≠ 结果版本）。
26. **【v3.28 更新】仿真/重排产出的版本默认不激活**：非 `FULL_SCHEDULE` 类型产出的 `PlanVersion` 默认状态为 CANDIDATE，**禁止自动激活**；正式采用直接通过 `PlanVersion.Status = ACTIVE` 表示（v3.28 四表收敛，不再使用 `System_Active_Version`）。
27. **【v3.17 新增，v3.30 更新】`Scenario ≠ PlanVersion ≠ SimulationRun`**：`Scenario` 是 what-if 假设与目标容器（业务对象）；`SimulationRun` 是某次具体算法执行记录；`PlanVersion` 是结果版本快照——三者正交，不可混淆；**`Scenario` 业务容器已进入白天实时链路，适用时创建**；`SimulationRun` / `ScenarioObjectiveScore` 仍为阶段二骨架，阶段二实装；未来多方案比较依赖 `ScenarioObjectiveScore` 对 `PlanKpiSummary` 的复用，**不重新扫 Task 明细**。
28. **【v3.17 新增】排程结果读模型不参与排程内核**：`OrderScheduleSummary` / `ResourceLoadSummary` / `PlanKpiSummary` 属于读模型层，由 2号位在 Task/Pegging 落库后后处理生成，**禁止进入 1号位排程内核**；`ScheduleExplanationFact` 在 1号位内存中以 `ExplanationFactDraft` 形态产出，**1号位禁止直接写数据库**，统一由 2号位与 Task/Pegging 同批次批量落盘；`ExplainTrace`（现有轻量追踪日志）与 `ScheduleExplanationFact`（结构化原因事实）共存不替代，职责层次不同，**禁止合并或混用**。
29. **【v3.26 新增 / v3.27 收窄】`Order_Canonical.Status` 准入口径锁定**：`Order_Canonical.Status` 只有三种业务值：OPEN / CLOSED / CANCELLED；活跃根集合、BOM Request 推送、Order 分区表同步、Task/Pegging 生成均以 `WHERE Status = 'OPEN'` 为唯一准入条件（v3.12 窄口径）；CLOSED / CANCELLED 不得进入 BOM Request 或生成 Task / Pegging；`RELEASED` / `SCHEDULED` / `COMPLETED` 不是当前 APS V1 业务枚举，**禁止**出现在当前正文 SQL 或准入判断中。
30. **【v3.26 新增】生产进度快照绝对不混入 InventoryBalance**：`MESWorkOrderSnapshot` / `OperationProgressSnapshot` / `StageProgressSnapshot` 是排程前扣减已完成量的输入，**不是库存供给**；这三张快照表不得写入 `InventoryBalance`；现货库存链路与 MES 进度链路在 `ScheduleContext` 中并存但**严格独立**，混淆即为严重架构违规。
31. **【v3.26 新增】Task 必须透传生产指示号**：所有由某个 Order/生产指示驱动生成的 Task（含根产品、半成品子件及各大工艺阶段 Task）必须携带 `ProductionInstructionNo`（或 `MTS_InstructionNo`）；**禁止只在根产品 Task 上保留生产指示号而在拆解的子件 Task 上丢失**；1号位禁止生成不可追溯到 `ProductionInstructionNo` 的 Task。
32. **【v3.35澄清】ScheduleRun 与 MES 快照任务职责分离**：`ScheduleRun` 必须在夜间数据准备阶段（最晚 00:38 前）由 `INightlyBatchOrchestrator`创建并冻结`ScheduleRunId + DataCutoffTime`；00:40 / 00:45 / 00:50 三个MES快照由独立定时任务分别调用同步SP，必须使用同一组参数，且不由`INightlyBatchOrchestrator`直接调用；02:00 `ISchedulingOrchestrator`只校验和消费已完成快照，不在计算中再次同步。
33. **【v3.29 新增】ZP/BP 出口库不能通用**：ZP/BP 库存默认不是通用可用库存。仅当 `DocumentNo=当前出荷指示号 AND DocumentType=SHIPPING_INSTRUCTION AND 出荷指示号未完成` 时，对应 ReceivedQty 才进入供给分配。禁止将 ZP/BP 库存作为通用库存写入 `PeggingSupplyAllocation`。
34. **【v3.29 新增】PeggingSupplyAllocation 不是候选供给表**：`PeggingSupplyAllocation` 只记录已确认可用供给。不可用库存、未匹配出荷号的 ZP/BP、被过滤候选供给不得进入本表。禁止写入不可用原因诊断信息（原因应写入日志/Issues）。
35. **【v3.29 新增】物理 Pegging 表只记录 Task-to-Task**：物理 Pegging 表（`UpstreamTaskId/DownstreamTaskId`）是 Task-to-Task 血缘表。库存、ZP/BP、Received、管道在途等非 Task 供给写入 `PeggingSupplyAllocation`，不得强行塞进物理 Pegging 表。
36. **【v3.38澄清】1号位不消费数据库中间表**：1号位只消费2号位通过方法参数传入的内存TaskDraft/ShippingTaskDraft/约束。禁止1号位查询 `MES_ProcessCode_View.ERPProperty`、`ERP_Received_ByDocument_View`、`PeggingSupplyAllocation` 进行排程判断。

37. **【v3.30 新增/修正】ScheduleRun.ScopeJson 11字段唯一权威**：`ScopeJson` 固定 11 字段（`Purpose / OrderCanonicalIds / FactoryIds / ProductFamilyIds / ResourceGroupIds / PlanHorizonStart / PlanHorizonEnd / LockedTaskIds / AllowTouchFrozenZone / AllowDelaySalesOrder / MaxImpactedOrders`）是本次运行范围的唯一权威；创建后 ScopeJson 不可变，后续服务只读不改；页面便捷参数必须在创建ScheduleRun前归一化进ScopeJson；与已有ScopeJson冲突时拒绝，不静默覆盖；禁止新增 `FreezeZoneEnd / ChangedMaterialCodes / ImpactedResourceGroupIds / AssumptionSnapshot` 等扩展字段。**ScopeJson 必填校验**：`Purpose` 必填，只允许四个正式值（`CTP / INSERT_IMPACT_ANALYSIS / INSERT_RESCHEDULE / MANUAL_ADJUSTMENT`）；`PlanHorizonEnd` 不得早于 `PlanHorizonStart`（两者均填写时）；`LOCAL_RESCHEDULE` 必须同时填写 `PlanHorizonStart` 和 `PlanHorizonEnd`，且五个范围数组（`OrderCanonicalIds / FactoryIds / ProductFamilyIds / ResourceGroupIds / LockedTaskIds`）中至少一个非空；所有范围和时间均空只允许经明确审批的 `MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT`；`MaxImpactedOrders` 有值时必须大于0，达到上限后停止扩大影响范围。
38. **【v3.30 新增/修正】Candidate PlanVersion 必须使用独立 Order 快照**：白天实时评估链路中，2号位调用 `PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)` 复制Base Order快照，只对 `scopeJson.OrderCanonicalIds` 指定订单叠加最新Order_Canonical变化，写入独立分区；**禁止 Candidate 运行直接读取 Base/ACTIVE 版本的 Order 数据**；禁止修改 Base/ACTIVE 版本 Order 数据；`UNIQUE(PlanVersionId, OrderCanonicalId)` 属于 `OrderBomRequestLink`，不属于 Order 表本身；Order 表按 PlanVersionId 隔离，OrderCanonicalId 为稳定关联字段。
39. **【v3.30 新增/修正】CTP / INSERT_IMPACT_ANALYSIS 是 Purpose，永不激活**：`CTP` 和 `INSERT_IMPACT_ANALYSIS` 是 `ScopeJson.Purpose` 字段的值，不是 RunType；含此 Purpose 的运行产出的 `PlanVersion` **永不允许激活为 ACTIVE**，无论人工操作还是系统自动流程；此类运行仅用于评估影响，结果只读；`ActivateCandidatePlanVersionAsync` 必须读取 ScopeJson.Purpose，遇到 CTP 或 INSERT_IMPACT_ANALYSIS 立即拒绝。
40. **【v3.30 新增】禁止自动重排**：异常变化闭环中，5号位产出 Recommendation 后**禁止系统自动创建 ScheduleRun 触发重排**；必须经 PMC 人工确认后由 3号位显式创建 ScheduleRun；自动重排即为严重架构违规。
41. **【v3.30 新增/修正】Scenario定位**：`RunType = MANUAL_RESCHEDULE` 的 ScheduleRun 可直接由 3号位创建，无需先建 `Scenario`；`INSERT_ORDER_WHATIF` 运行通常可创建 `Scenario`；`Scenario` 业务容器已进入白天实时链路；`SimulationRun` 和 `ScenarioObjectiveScore` 仍为阶段二骨架。
42. **【v3.30 新增/修正】实时BOM链路与复用判断**：Candidate 运行链路中，2号位先检查 Scope 内 BOM 切片能否合法复用；可以复用时使用已验证的合法 BOM 切片，不重复创建 RequestDetail；不可复用时：创建 RequestDetail → `EnsureRealtimeBomReadyAsync(requestDetailId)` → `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` → `MES_APS_BOM_Workset_Realtime`（通过RequestDetailId追溯）→ `sp_EnrichBOMWorksetRealtime` → `MES_APS_BOM_Workset_StageDetail_Realtime`（通过WorksetId关联）→ `sp_GenerateBOMCrossFactoryEdgeRealtime` → `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`（直接按RequestDetailId隔离，为0行完全合法）→ Request状态READY → `PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)` → 三张APS RAW（BatchNo=`RT:RD:{RequestDetailId}`）→ 生成OrderBomRequestLink。ODS三张Realtime表的BatchNo不是共同字段；Workset_Realtime通过RequestDetailId追溯；StageDetail_Realtime通过WorksetId间接关联RequestDetailId；CrossFactoryEdge_Realtime直接按RequestDetailId隔离。
43. **【v3.32升级】RemainingSupplyContext按供给状态重建**：Candidate以Base ACTIVE准确供给切片为基线，分类处理实际已消耗、HardLock、ExecutionLock剩余投入、ExecutionLock未来产出、Scope外Soft、Scope内Soft和未分配供给；Scope内Soft可释放重算，Scope外Soft暂保留，HardLock和实际消耗不得释放。禁止重新读取当前全部库存/在途覆盖Base，禁止沿用“Base原始供给－Base全部已分配”的单一公式。
44. **【v3.30 新增/修正】READY状态机红线**：同一RequestDetailId有多条请求时，`ORDER BY Id DESC` 取最新一条；READY是唯一权威，直接返回；`ExpandedRowCount` 仅诊断，不参与READY判断；PROCESSING状态只轮询，禁止再次执行SP；FAILED状态不静默复用，V1不自动重试；显式重试必须创建新的Request行，不复用旧行；超时只按最新Request.Id更新FAILED，禁止按RequestDetailId批量更新。
45. **【v3.34升级】MES实时事实只经契约视图与运行快照**：V1不建设MES_Actual_Staging、MQ事件累计或REST备用通道；同一ScheduleRun三类快照使用同一DataCutoffTime并全量替换。
46. **【v3.30 新增】Candidate激活事务红线**：`ActivateCandidatePlanVersionAsync(candidatePlanVersionId)` 必须在 Serializable 事务内执行；加锁读取 Candidate；已经 ACTIVE 时幂等返回；状态必须为 CANDIDATE；SourceScheduleRunId 必须存在；读取并加锁 ScheduleRun；Purpose 必须存在；永远拒绝 CTP；永远拒绝 INSERT_IMPACT_ANALYSIS；只允许三个精确组合（`LOCAL_RESCHEDULE+INSERT_RESCHEDULE` / `LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT` / `MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT`）；审批必须通过；BasePlanVersionId 不得为空，使用前先做 null 校验；Candidate.DomainKey 不得为空且必须等于 Base PlanVersion.DomainKey；同一事务内归档相同 DomainKey 的旧 ACTIVE；同一事务激活 Candidate；失败时整体回滚。
47. **【v3.30 新增/修正】异常变化闭环不发明物理表**：ImpactAssessment 是规则评估结果对象；Recommendation（及 `ScheduleExplanationFact` 原因事实）是5号位返回的运行对象/规则输出；**V1 不落地 TaskPauseVoucher / TaskResumeVoucher**（历史已废止口径，仅供追溯）；**不在本文件新增物理表**；订单变化写成"Order_Canonical变化事实"或"订单变化事件"；资源异常Result的字段只能来自MES实时资源状态契约视图或既有设备事实视图；源端未提供的故障原因、预计修复时间不得推断。

48. **【v3.20 新增，v3.22 升级】OrderBomRequestLink 查询链路红线**：Order→BOM结构必须通过 `OrderBomRequestLink.ResolvedBOMNO`→`APS_BOM_RAW(BatchNo+BOMNO)` 查询（禁止直接用旧 `BOMNO` 字段站 JOIN）；Order→大工艺路径必须通过 `OrderBomRequestLink.RepWorksetId`→`APS_BOM_STAGE_PATH_RAW.WorksetId` 查询；`APS_BOM_RAW` **禁止新增订单级字段**，保持BOMNO级共享；`APS_BOM_STAGE_PATH_RAW` 无需改动；`OrderBomRequestLink` 由 2号位在 BOM Workset + StageDetail 同步完成后生成（`PullBOMResultFromODSAsync(batchNo, planVersionId)` Step 4）；**数据源**必须为 ODS `MES_APS_BOM_Workset` 聊合，**禁止从 `APS_BOM_RAW` 反查**；`ResolvedBOMNO`=Level=1 Workset.BOMNO；`RepWorksetId`=`MIN(Workset.Id) WHERE RequestDetailId+Level=1`；唯一约束 = `UNIQUE(PlanVersionId, OrderCanonicalId)`（不是 OrderId）；找不到 OrderId 时写 `OrderId=NULL, LinkStatus='SKIPPED'`，不阻断批次；**禁止 BOMResultPullService 内部自查最新 PlanVersion**，planVersionId 由 NightlyBatchOrchestrator 显式传入。


49. **【v3.32新增】生产指示总量唯一边界**：生产指示可生产量只按`max(0, Order.Quantity - Order.ReceivedQty)`计算；MES完成量、XC、在途、Received不得再次从总量扣减或扩张总量，只能定位该总量的位置。`ReceivedQty > Quantity`时按0处理并登记Issue。
50. **【v3.32新增】PI位置快照和互斥闭合**：每个ScheduleRun+Domain必须保存PI Header和PositionSlice；所有位置切片必须互斥且合计等于可生产量。下游Stage超过上游时下修下游；中间Stage缺失取下游最小证明量；UNLOCATED不得消失，须从最早Stage保守排程。
51. **【v3.32新增】XC位置与Task路径并存**：`ERPProperty=XC`且映射某Stage时，该数量处于该Stage投料前/阶段内，该Stage未完成。XC作为非Task现实供给被分配后，仍须按PositionSlice剩余路径生成当前Stage及后续Task，禁止因“已扣库存”漏掉生产Task。
52. **【v3.32新增】需求—PI Header分配与位置绑定分步**：先由Pegging决定某需求从某PI承接多少，再将该数量绑定到PositionSlice；不得在第6章前随意挑选某个位置，也不得只分配PI Header而不形成位置份额。
53. **【v3.32新增】一笔分配三动作原子闭合**：每个PeggingAllocationDecision必须同时扣需求余额、扣供给余额并写PeggingAllocationLedger；5号位不得直接修改余额；2号位是唯一执行者。Ledger不闭合时该Domain不得发布。
54. **【v3.32新增】Task正式生成时机**：Pegging阶段只生成LogicalBlock/TaskDraft；1号位完成有限产能合并/拆分和时间排定后，2号位才持久化正式Task。禁止先落正式Task再在排程结束后硬合并。
55. **【v3.32新增】跨需求合并与执行真实性**：V1允许不同需求合并为一个Task，但必须由Ledger保存各需求数量份额；一个正式Task最多关联一个ExecutionLock，不同现实MES工单不得被合并成一个新Task。
56. **【v3.34升级】发布、执行与归属三分离**：MESPlanRelease固定发布承诺；ExecutionLock固定现实执行；HardLock固定严格归属。RELEASED只在MES建单被APS确认后形成，不自动永久锁定普通需求归属。
57. **【v3.34升级】跨版本保留与重建边界**：跨版本保留PUBLISHED/CONSUMED MESPlanRelease、ExecutionLock、HardLock、MES工单锚点和实耗事实；重建TaskId、物理Pegging、普通Soft和未发布虚拟Task。新Task关联稳定发布/执行对象，不得重复发布。
58. **【v3.32新增】虚拟占位Task不可下发**：无正式PI的虚拟Task可以参与有限产能评估，但`IsVirtual=1`时绝对禁止MES下发、不形成ExecutionLock、不跨版本保留；正式PI到达后由下一次全量重算自然替换。
59. **【v3.33修正】Task—需求数量权威在Ledger**：`Task.OrderId`仅作代表订单或兼容字段；跨需求合并后的完整归属必须查询Ledger/`vw_TaskDemandAllocation`，且视图只汇总`TaskComponentQty`，不得汇总供需分配字段`AllocatedQty`。禁止通过Task单一OrderId推断全部需求归属。
60. **【v3.32新增】插件与Voucher最小边界**：V1不建设动态插件平台。规则变化优先进入RuleSet/ParameterSet；只有稳定算法差异使用少数.NET策略接口。普通领域计算返回Result，Pegging返回AllocationDecision；只有审批或正式状态变化使用Voucher。
61. **【v3.32新增】IssueCode与ReasonCode分层**：PI位置异常、Ledger不闭合、执行锁重复等技术/数据问题进入PeggingIssueCode及发布校验；ScheduleExplanationFact.ReasonCode仍保持既有15项业务原因字典，禁止把所有技术Issue塞入ReasonCode。
62. **【v3.32新增】Domain内存与批量持久化**：Pegging循环内禁止逐行查库、逐行UPDATE或逐行INSERT；输入一次装入Domain内存，结果通过TVP/SqlBulkCopy/批量SP落临时或Stage表，再在Domain事务内完成正式结果写入与发布。

---

## 5. 负责人调整记录


### v3.32 当前权威职责（Pegging与跨版本执行）

- **1号位**：只通过内部接口参数消费2号位提供的内存LogicalBlock/TaskDraft、Routing、资源和约束；不得直接查询或写入任何数据库；在有限产能条件下评估合并/拆分、排定开始结束时间，返回`ScheduledTaskDraft + ComponentShares + ExplanationFactDraft`；不直接写数据库、不直接读取Received/XC/管道原始事实、不直接修改Ledger。
- **2号位**：负责ScheduleContext装载、PI Header/Position快照、RuntimeDemand/Supply余额、应用`PeggingAllocationDecision`、原子扣减、Ledger、PeggingSupplyAllocation、ExecutionLock/HardLock持久化、Task正式批量落库、物理Pegging和发布事务；是余额和状态变更的唯一执行者。
- **3号位**：负责ScheduleRun/PlanVersion、白天单Domain Candidate、ScopeJson、审批编排和激活；不参与供给选择和余额扣减。Candidate激活前调用2号位校验MESPlanRelease/ExecutionLock/HardLock/实际消耗未变化。
- **4号位**：负责RuleSet/ParameterSet/StrategyProfile及特殊出荷指示HardLock类型等规则配置、版本发布和页面展示；不在运行时直接改排程结果。
- **5号位**：负责BOM/Stage结构事实、供给资格、优先级、PI位置、部分份额绑定、HardLock命中和不足处理等策略/领域计算；返回只读Result或`PeggingAllocationDecision`，不直接扣余额、不写最终Task、Ledger、ExecutionLock或物理Pegging。
- **0号位/PMC**：确认真正的业务政策和配置清单，尤其是特殊出荷指示HardLock类型、客户/环保/质量专属规则及人工解除审批边界；无需逐字段审查技术实现。

> **边界口诀**：5号位“算该怎么做”，2号位“验证并真正执行”，1号位“在有限产能下排得出来”，3号位“管运行和版本”，4号位“管规则配置和展示”。

- **当前版调整**：ODS 递归展开到 `MES_APS_BOM_Workset` 由 **5号位** 负责实现。
- **2号位保留职责**：ODS / APS 架构、DDL、APS 拉取、`APS_BOM_RAW`、LLC、本地 `IDataLoader`、验收；**v3.8 新增**：`vw_MES_BOM_Stage_Enriched` 视图维护（仅 ODS 内部）；**v3.18 新增**：`MES_BOM_Edge_Active` / `MES_BOM_Edge_RefreshLog` 表结构与索引、`sp_RefreshBOMEdgeActive` SP 骨架（事务框架 + 刷新日志写入；业务映射步骤 STEP 2+ 由 5号位实现）、`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` SP 框架结构（具体展开逻辑由 5号位实现）。
- **5号位保留职责**：`sp_RefreshBOMEdgeActive` 业务映射逻辑（ProcessCode 左补零 / ChildSourceHintCode 值域 / 双源版本裁决规则 / EffectiveFrom 裁剪）、ERP / MES 主数据插头、ODS 迭代展开（`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext`）、业务规则校验；v3.6新增：`ChildRequiredStageCode`回填 + `StageDetail`写入 + `StageLeadTimeParam`配置维护；v3.7新增：ROOT根产品完工路径推导（Level=1 ParentProcRefCode → 映射标准化 → 多条不一致取最长+记WARNING）；v3.8新增：`ChildRequiredFactory` R17 回填 + `MES_APS_BOM_Workset_Issues` 写入；**v3.9口径更新**：不做批次放行校验，改为"异常降级 + 登记 DegradeAction 标签"，批次永远走 READY；新增 `ProduceToFactoryMap` + `StageDict` 配置表维护；**v3.14新增（v3.22口径更新）**：BOM入口解析分流——从 `MES_API_BOM_Request_Detail` 读取 `RequestedBOMNO`（可空）/`MaterialCode`/`FactoryCode`/`OrderType`/`OrderCanonicalId`（⚠️ v5.0.32 已删除 `BOMNO`/`Model` 字段，禁止继续引用）；有 `RequestedBOMNO` 直接定位 `MES_BOM_Edge_Active` 展开，无 `RequestedBOMNO` 时按 `OrderType + MaterialCode + FactoryCode + BOM边/ProcessCode` 规则解析入口（不从 Model 推导）；`RequestDetailId` 回写到 `MES_APS_BOM_Workset` + `MES_APS_BOM_Workset_Issues`；**v3.18新增**：`sp_EnrichBOMWorkset_vNext` 中每条 Workset 行写 StageDetail 时附带 `WorksetId`（=Workset.Id），保证多路径收敛场景 StageDetail 一一追溯；StageDetail 不存 `RequestDetailId`（经 WorksetIdWorkset.IdRequestDetailId 反查）。
- **0号位/业务复核人员职责**（v3.9 口径更新）：**不做每日值班**，改为**月度巡检** `MES_APS_BOM_Workset_Issues`，统计高频 IssueType + 高频物料，反馈 ERP 维护方批量修正源头数据；ReviewStatus 标记 CONFIRMED/IGNORED/FIXED 闭环；ERROR 类（CYCLIC_BOM）次日晨会过一遍。
- **3号位保留职责**（v5.0更新，v3.6更新，v3.10 新增 StageDict 映射责任）：`MES_APS_Routing_Operation_View` + `MES_APS_Routing_Dependency_View` + `APS_OperationResourceEligibility_View` + `MES_APS_Routing_Stage_View`(v3.5新增，v3.6定位调整为阶段字典) 契约与清洗（原 `MES_APS_Routing_View` 废弃）；**v3.10 新增**：`MES_APS_Routing_Stage_View` 负责将 MES 本地阶段名标准化映射到 `StageDict`（契约视图层完成映射，MES 原生字符串不得直接下沉到 APS 本地表）；`ProcessTypeDict` 骨架期维护（业务启用前保持 IsActive=0）。
- **0号位保留职责**：业务口径审批、活跃根规则、策略参数、异常处理口径；**v3.12 新增**：审批 `ProductionDepartment` / `ProcessCodeDict` / `MaterialStageDeptOverride` 三张人工维护字典表的字段变更与启用/停用。
- **APS 系统管理员（v3.12 新增角色）**：负责 `ProcessCodeDict` 中 StageCode/CodeOrigin 等 APS 增强字段的人工维护，变更走 0 号位审批闭环。ERPProperty 由 5号位从 ERP 真实属性同步/透出维护，专用同步不覆盖 APS 增强字段。
- **2号位 v3.12 增量职责**：
  - `sp_RebuildMaterialStageDeptContext`（**v1 占位 SP，当前未实现**——DDL Step1~6 全 TODO）：设计三触发模式 `FULL` / `INCR` / `PARTIAL`；输入 MSC + Override + ProductionDepartment + MES_ProcessCode_View.StageCode；产出 `MaterialStageDeptContext` + `MaterialStageDeptContext_Issues`；实装前下游消费方应做空表/降级处理
  - `sp_SyncResourceData` 升级：`WorkshopCode` → `ProductionDeptCode`；MERGE 加 `ProductionDepartmentId` 双字典映射 JOIN（FactoryCode + ProductionDeptCode）
  - **取消** v3.11.2 的 `sp_SyncMasterData(@SourceType='ProcessCode')` 同步分支责任（ProcessCodeDict 改为 APS 自维护）
  - 4 个 ODS 契约视图字段升级跟进（`MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `APS_OperationResourceEligibility_View` / `MES_APS_Resource_View` 加 `ProductionDeptCode`；走防腐层审批流程，由 DBA 执行 ALTER VIEW）
  - `MaterialStageDeptOverride` 导入工具：实现 Model → MaterialCode 1:1 解析检查；1:N 拒收并返回明细
- **v3.28 更新职责 - 3号位**（⚠️ 历史口径，白天Candidate创建/Scenario定位/激活规则已由v3.30全面修正，以下仅供追溯）：`ScheduleRun` 触发（凌晨 Hangfire = FULL_SCHEDULE；API 接口 = MANUAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF 等）；版本激活旧口径：`FULL_SCHEDULE` 完成后 2号位同步更新 `PlanVersion.Status=ACTIVE`；其余类型须 3号位显式将 `PlanVersion.Status` 改为 ACTIVE。**⚠️ 此"其余类型直接改Status"口径已由 v3.30 废止**——现行激活只允许通过 `ActivateCandidatePlanVersionAsync`，且只允许三个精确 RunType+Purpose 组合（`LOCAL_RESCHEDULE+INSERT_RESCHEDULE` / `LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT` / `MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT`），CTP 和 INSERT_IMPACT_ANALYSIS 永远拒绝激活。**v3.30 澄清**：夜间 FULL_SCHEDULE 各成功 Domain 产出的 PlanVersion = 2号位直接更新为 ACTIVE（运行 COMPLETED 或 PARTIAL_SUCCESS 下均已成功 Domain 发布）；白天 Candidate PlanVersion = 3号位创建，2号位数据构造，审批通过后由3号位调用 `ActivateCandidatePlanVersionAsync`。`Scenario` 管理入口（v3.30已进入白天链路；`SimulationRun` / `ScenarioObjectiveScore` 仍为阶段二骨架）。
- **v3.28 更新职责 - 2号位**（⚠️ 现行口径限定：本条仅适用于夜间 `FULL_SCHEDULE`）：2号位在排程启动时按 Domain 创建多个 `PlanVersion`（每 DomainKey 一个，Status=BUILDING），各 Domain 独立计算、落盘、发布，成功 Domain 更新为 ACTIVE，并汇总回填 `ScheduleRun.Status`（全部成功=COMPLETED / 部分成功=PARTIAL_SUCCESS / 零成功或运行级致命错误=FAILED）；`ScheduleExplanationFact` 与 Task/Pegging 同批次批量落库；`OrderScheduleSummary` / `ResourceLoadSummary` / `PlanKpiSummary` 后处理生成。**⚠️ 白天 Candidate PlanVersion 的创建与激活职责已由 v3.30 覆盖**——由 3号位创建 Candidate PlanVersion，2号位负责数据构造、排程结果持久化并将 PlanVersion 更新为 CANDIDATE，**不得直接激活**。
- **v3.17 新增职责 - 1号位**：在排程内存推演过程中产出 `ExplanationFactDraft`（不直接写 DB，传递给 2号位批量落盘）；`ReasonCode` 打标逻辑由 1号位内核实现，权威枚举（V1 冻结，见 §3.8a，共 15 项）：`RESOURCE_CAPACITY_WAIT` / `MATERIAL_SHORTAGE` / `PRECEDENCE_WAIT` / `FROZEN_ZONE_LOCK` / `ROUTING_FALLBACK` / `STAGE_LEADTIME_FALLBACK` / `BOM_DEGRADE` / `CROSS_ORG_HANDOFF` / `PRIORITY_LOWER_THAN_OTHERS` / `DUE_DATE_RISK` / `LOGISTICS_DELAY` / `PRIORITY_INHERITANCE` / `CROSS_DOMAIN_VERSION_MISMATCH_RISK` / `MANUAL_COMPLETED_SHORT` / `EQUIPMENT_BREAKDOWN_RISK`（口径需 0号位审批后冻结；文中所有 ReasonCode 必须属于此列表）。
- **v3.30 更新职责 - 2号位**：Candidate独立Order快照构造（`PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)`：复制Base Order + 只叠加ScopeJson.OrderCanonicalIds指定订单的Order_Canonical变化 → 写入Candidate分区；OrderBomRequestLink具有`UNIQUE(PlanVersionId, OrderCanonicalId)`，Order表本身按PlanVersionId隔离）；BOM复用判断（可复用则使用合法切片，不重复创建RequestDetail）；EnsureRealtimeBomReadyAsync + PullRealtimeBOMResultFromODSAsync（三张RAW，BatchNo=`RT:RD:{RequestDetailId}`）；生成OrderBomRequestLink；BuildRemainingSupplyContextAsync（按实际消耗、HardLock、ExecutionLock、Scope内/外Soft分类重建；禁止重新读全量库存/在途覆盖Base）；BuildScheduleContext；PI快照与Ledger；LogicalBlock/TaskDraft构造；接收1号位ScheduledTaskDraft后正式持久化Task/ShippingTask；Candidate结果持久化后将PlanVersion.Status更新为CANDIDATE；MES实时工单/进度视图快照与发布对账。（注：V1 不落地 `TaskPauseVoucher` / `TaskResumeVoucher`，设备故障不再涉及 Voucher 事务执行，二者仅作历史废止口径、V1 不落地；普通计算Result和PeggingAllocationDecision不走Voucher；仅审批或正式状态变化保留少量Voucher，详见后续集成接口同步版本。）
- **v3.30 更新职责 - 3号位**：ScopeJson归一化（页面便捷参数归一化进ScopeJson，冲突时拒绝）；Scenario生命周期（INSERT_ORDER_WHATIF通常创建；MANUAL_RESCHEDULE不要求先建）；ScheduleRun生命周期（写入BasePlanVersionId/StrategyProfileVersionId/ScopeJson/DataCutoffTime；**创建 ScheduleRun 时冻结 `ExpectedDomainKeysJson`**（运行级不可变，记录本次预期 DomainKey 集合，不得按已建 PlanVersion 反推））；3号位创建白天Candidate类PlanVersion版本壳，初始Status=BUILDING；2号位负责数据构造与结果持久化，成功后将PlanVersion更新为CANDIDATE；随后由3号位负责审批编排和显式激活；调用`ActivateCandidatePlanVersionAsync`（Serializable事务，只允许三个精确RunType+Purpose组合，永远拒绝CTP和INSERT_IMPACT_ANALYSIS）；PMC确认后触发重排（禁止自动触发）。**禁止由2号位替代3号位创建白天Candidate版本壳。**
- **v3.30 新增职责 - 5号位**：ODS/BOM展开（`sp_ExpandBOMRealtime_vNext`）；StageDetail；CrossFactoryEdge结构事实；Issues；规则计算；ImpactAssessment（规则评估结果对象）；返回Recommendation（及 `ScheduleExplanationFact` 原因事实，运行对象/规则输出，**V1 不再返回 TaskPauseVoucher / TaskResumeVoucher**，历史已废止）；从MES实时资源状态契约视图形成资源事实Result（缺失字段为null，不推断）；5号位**不修改Order/Task**；**不扣库存**；**不生成最终Task/ShippingTask**；**不写物理Pegging**；**不创建ScheduleRun**；**不自动触发重排**；设备故障下**不自动暂停/恢复 Task**。
- **v3.30 新增口径 - 0号位**：自动重排禁止红线审批；CTP / INSERT_IMPACT_ANALYSIS 永不激活口径审批（明确其为ScopeJson.Purpose，不是RunType）；Candidate 激活边界（三个精确 RunType+Purpose 组合审批通过后允许激活，SIMULATION 不在白名单内）口径审批。

---

## 6. 工厂归属关系总表（2026-05-03 v3.13 新增）

> 本表逐行列出各基础数据对象的 `FactoryCode` / `FactoryId` 有无、来源及推导路径，配合 §4 红线 #19~#22 阅读。
>
> **阅读指引**：直接持有 `FactoryCode` 的表用 ✅ 标记；直接持有 `FactoryId FK` 的表用 🔗 标记；间接归厂的表用 ↗️ 标记。

### 6.1 FactoryCode 归属矩阵

| 基础数据对象 | FactoryCode | FactoryId | 来源 / 推导路径 | 备注 |
|---|---|---|---|---|
| **Factory** | ✅ `Code`（本表即基准） | `Id`（PK） | 自维护字典 | 全系统工厂代码值域权威；JOIN 列名为 `Factory.Code`（DDL v5.0.17 修正） |
| **StageDict** | ✅ `FactoryCode NOT NULL` | 否 | 本表自维护 | ODS 增强字典；按 `FactoryCode` 过滤可得某工厂的阶段列表 |
| **ProcessCodeDict** | ✅ `FactoryCode NOT NULL`<br>✅ `ActualFactoryCode NULL` | 否 | 本表自维护（APS 系统管理员） | ODS 增强字典；`FactoryCode` = 账面工厂（R26 过滤维度），`ActualFactoryCode` = 实际生产工厂 |
| **StageLeadTimeParam** | ✅ `FactoryCode NOT NULL` | 否 | 业务参数表 | 索引 `IX_StageLeadTime_Match` 含 `(StageCode, FactoryCode, IsActive)` |
| **Order_Canonical** | ✅ `FactoryCode NULL` | 否 | 由 `sp_ValidateAndPromoteOrders` 标准化写入；ERP 源端直接提供 FactoryCode，SP 完成标准化映射 | 防腐层核心订单表；FactoryCode 非空为参与排程的 OPEN 订单准入条件；缺失或无法映射者标记失败，不得进入排程快照；**禁止**通过 ProcessCode、StageCode 或 BOM 路径反推 FactoryCode；物理字段暂可保持 NULL 兼容历史数据；DDL TODO桩待后续配套修订 |
| **Order** | 否 | 🔗 `FactoryId NOT NULL FK` | `sp_SyncOrdersToPartitionTable`：`Order_Canonical.FactoryCode` → `Factory.Code` 映射得 `Factory.Id` | 映射失败订单不得写入，应标记失败；`ISNULL(f.Id, 1)` 静默默认工厂逻辑**待后续 DDL/SP 修订中删除**（当前DDL v5.2.2仍保留该待办，不得写成“已废除”） |
| **ProductionDepartment** | 否 | 🔗 `FactoryId NULL FK` | 录入/审批时直接选择 | 排程责任部门字典；当前 `FactoryId` 可空（远期建议 NOT NULL） |
| **Resource** | 否 | 🔗 `FactoryId NOT NULL FK` | `sp_SyncResourceData`：`ext_MES_APS_Resource_View.FactoryCode` → `Factory.Code` 映射得 `Factory.Id` | 审计源端部门码走 `SourceProductionDeptCode`（已有）；源端工厂码仅在 SP 临时表中中转，未持久化到 Resource 表 |
| **MaterialSupplyContext** | 否 | 🔗 `FactoryId NULL`（可选辅助） | `sp_SyncMasterData` 可选映射 | 仓库级供给上下文；`FactoryId` 作为辅助维度，非主路径 |
| **ResourceOrgGroup** | 否 | 🔗 `FactoryId NOT NULL FK` | 手工维护 | 看板筛选切片用；与 `ProductionDepartment` 职责分开 |
| **ResourceGroup**（v5.0 废弃） | 否 | 🔗 `FactoryId NOT NULL FK` | 遗留表 | **新代码禁止引用**；组织归属改由 `ResourceOrgGroup` |
| **MaterialStageDeptOverride** | 否 | 否 | ↗️ 通过 `StageCode → StageDict.FactoryCode` 间接归厂 | 人工维护表；键 `(MaterialCode, StageCode, ProductionDeptCode)` |
| **MaterialStageDeptContext** | 否 | 否 | ↗️ 通过 `DefaultProductionDepartmentId → ProductionDepartment.FactoryId` 间接归厂 | 2 号位组装正式消费表；1 号位排程主链入口 |
| **RoutingOperation** | 否 | 否 | ↗️ 通过 `ProductionDepartmentId → ProductionDepartment.FactoryId` 间接归厂 | 1 号位排程查询三元组 `(MaterialId, ProductionDepartmentId, StageCode)`；DB 唯一键含 `RouteCode + PathId + OperationCode` |
| **RoutingDependency** | 否 | 否 | ↗️ 同上 | DB 唯一键含 `RouteCode + PathId + FromOperationCode + ToOperationCode` |
| **OperationResourceEligibility** | 否 | 否 | ↗️ 同上 | DB 唯一键含 `RouteCode + PathId + OperationCode + ResourceId` |
| **ResourcePlanningContext** | 否 | 否 | ↗️ 通过 `ResourceId → Resource.FactoryId` 间接归厂 | APS 本地排程参数表 |
| **Material** | 否 | 否 | ↗️ 无直接归厂路径；按业务语义物料本身不绑定单一工厂 | 工厂维度在下游 `StageDetail` / `Order` / `RoutingOperation` 等场景中确定 |
| **MaterialMapping** | 否 | 否 | ↗️ 同上 | 双源桥接表；工厂信息由 `MaterialSupplyContext` 承载 |
| **RoutingStage** | 否 | 否 | ↗️ 通过 `StageCode → StageDict.FactoryCode` 间接归厂 | 阶段字典/标准阶段语言；本表无独立工厂直属 |

### 6.2 工厂字段承接红线（对应 §4 红线 #19~#22 摘要）

1. **`Factory.Code` 值域基准**：全系统工厂代码唯一权威。JOIN 列名 = `Factory.Code`。
2. **ODS/字典层只留 `FactoryCode` 字符串**：不建跨库 FK。
3. **APS_Production 落 `FactoryId` FK 的表**（v3.24扩展）：
   - 原强关联 5 表：`Order` / `Resource` / `ProductionDepartment` / `MaterialSupplyContext` / `ResourceOrgGroup`
   - v5.0.39 补充：`InventoryFact_ERP` / `InventoryFact_MES` / `InventorySupplyCandidate` / `InventoryAvailabilityRule` / `InventoryBalance` / `SupplyFact_Pipeline`（库存/供给事实+规则+余额表均可持有 `FactoryId FK`；原"强关联5表"说法过于狭窄，已修正）
4. **排程主链对象不重复存 `FactoryId`**：间接归厂路径 = `ProductionDepartmentId → PD.FactoryId` 或 `ResourceId → Resource.FactoryId` 或 `StageCode → StageDict.FactoryCode`。

### 6.3 Order_Canonical.FactoryCode 新确认正式口径（配套DDL/SP待同步）

1. **正式来源**：`v_APS_SalesOrder.FactoryCode` 是订单工厂的正式来源字段；ERP 契约视图应输出 APS 标准工厂码，若 ERP 原码与 APS 标准码不同，由明确、可审计的源工厂码映射规则完成标准化。
2. **排程准入要求**：参与排程的 OPEN 订单必须有合法 `FactoryCode`；`sp_ValidateAndPromoteOrders` 必须校验 `FactoryCode` 非空，且能唯一命中 `Factory.Code`；校验失败的 OPEN 订单标记失败，不得进入 BOM Request 和 Order 排程快照，应进入人工修正队列。
3. **缺失或无法映射处理**：缺失或无法映射时不得进入 BOM Request 和 Order 快照；禁止静默默认工厂。
4. **禁止反推**：禁止通过 ProcessCode、工序名称、StageCode 或 BOM 路径反推订单 `FactoryCode`。
5. **物理字段兼容性**：`Order_Canonical.FactoryCode` 物理字段暂可保持 NULL，以兼容历史数据；业务准入规则为参与排程的 OPEN 订单必须有合法 `FactoryCode`。
6. **DDL待同步**：当前DDL v5.2.2中 `Order_Canonical.FactoryCode` 允许 NULL 进入 Canonical 的 Step 2e TODO 桩，以及 `sp_SyncOrdersToPartitionTable` 中的 `ISNULL(f.Id, 1)` 默认工厂代码，**尚未完成修订**；不得写成"已经废除"；待后续配套 DDL/SP 修订版本统一处理。
7. **不得发明失败码**：不得自行发明新的 FailureCode，具体失败码另行在 DDL/字段说明中统一。
