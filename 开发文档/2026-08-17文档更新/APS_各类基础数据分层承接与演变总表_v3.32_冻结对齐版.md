# APS 各类基础数据分层承接与演变总表（冻结对齐版）

**版本**：v3.32  
**日期**：2026-08-12  
**文档性质**：架构总纲级参考文档 / APS V1冻结技术文档  
**维护责任人**：2号位（技术负责人）

> **冻结声明**：本文是《APS V1最终全部流程与业务基线》及两份专项冻结说明在“数据分层、对象承接、内存消费和职责边界”上的技术展开。本文不得独立改变已冻结业务。与历史版本说明、旧接口、旧DDL或旧代码冲突时，以冻结业务为准；只有0号位显式“重新打开冻结决策”后，业务口径才允许变化。

**v3.32更新内容**（2026-08-12，冻结业务对齐）：
- 🔒 需求排序改为“计算层→有序规则段→第一命中→段内排序”，`PriorityScore`退出V1主链权威，仅可历史/展示兼容。
- 🔒 2号位输出逻辑生产需求和完整求解上下文；最终 `FinalTask`、Resource、Start/End、最终拆合批由1号位生成，删除2号位“Task/ShippingTask实例化”作为正式主链。
- 🆕 正式加入计划良率数量语义：`NetRequiredQty / NetOutputQty` 与 `PlannedProcessQty` 分离；PI Supply不因良率膨胀。
- 🆕 正式加入PI位置（PI Position）承接链：MES/Stage/XC/跨厂在途等事实 → 5号位位置闭合 → 一张最小PI位置快照 → 2号位按PI承诺与位置消费 → 1号位只接收已解析起点/剩余工艺。
- 🔄 `StageProgressSnapshot`不再由1号位直接解释PI位置；其职责收敛为5号位位置计算的事实输入。
- 🔄 采购、VMI、已到厂未入库、厂间在途正式进入V1 Supply Context；废止“PipelineSupplies V1固定为空”的当前态口径。
- 🆕 无正式PO/VMI时允许运行时“规划性采购占位供给（Planning-only Purchase Placeholder）”，只在内存存在，标记 `ESTIMATED / NOT_COMMITTED`，不生成Task、不下ERP。
- 🔄 两类跨厂收敛：STAGE_HANDOFF=`目标M→PI→PI Position→缺口新增生产`；INTER_FACTORY_ORDER=`目标BS/KS→SH；SH内在途→同SH Received→未生产`。有限物流Task/ShippingTask退出V1有限产能主链，运输只形成LeadTime→AvailableTime。
- 🆕 Demand Protection正式进入分层承接：与Strict Binding、Execution Constraint三类分开，按数量份额锁定，不锁整PI/PO。
- 🔄 夜间FULL普通Allocation不保留跨版本稳定性偏好；只有严格绑定、Demand Protection、不可逆执行份额保留。
- 🔄 Candidate RemainingSupply改为“当前有效物理供给－已消耗－严格绑定－Demand Protection－不可逆－失效”；普通未锁Base Allocation可释放重竞争。
- 🔄 Candidate `ScopeJson`仅表达发起范围/业务输入，不得截断真实物理影响传播；`MaxImpactedOrders`和PlanHorizon不作为硬终止条件。
- 🆕 白天单Domain Candidate遇跨Domain共享设备时，其它Domain ACTIVE占用作为不可移动资源阻挡块；不建共享资源配额/借用/跨域传播平台。
- 🆕 夜间FULL即使按Domain独立PlanVersion，也必须在同一Run内遵守跨Domain共享资源真实互斥，禁止设备双占。
- 🆕 跨Domain供给按多段 `Quantity + AvailableTime` 传递；运输LT使用真实工厂/Stage口径，不以固定2天作为业务权威。
- 🆕 白天跨Domain插单采用多个单Domain WHATIF按拓扑串行（如C→B→A），前端汇总一个CTP/影响答案，不建MultiDomain Candidate。
- 🔄 WHATIF永不自动正式；正式采用保留最小人工确认和审计，不把完整OA审批作为V1主链硬依赖。
- 🔄 Firm/Frozen保留业务语义，但不建设 `FrozenZoneSnapshot` 平台；跨版本从上一ACTIVE读取仍有效不可移动锚点。
- 🔄 `VirtualInventoryBalance`不持久化；跨Domain只在运行时传递Quantity-Time切片。
- 🔒 Candidate、无PI占位Task、UNLOCATED保守Task不得下发MES。
- 🔄 失败恢复：上游Domain失败时其依赖下游本次不发布新ACTIVE；人工恢复创建新的ScheduleRun，原FAILED/PARTIAL_SUCCESS历史不可修改。
- 🔒 `ScheduleRun / PlanVersion / ExpectedDomainKeysJson / PARTIAL_SUCCESS / Realtime BOM / OrderBomRequestLink / Routing三件套 / MaterialStageDeptContext / MES五态 / 规则治理6表`继续作为保护区，不因本次收敛顺手重构。

> ⚠️ **以下 v3.31及更早版本说明仅用于历史追溯。当前开发、测试与验收以 v3.32 正文为准；历史说明中的“Pipeline V1空”“2号位Task/ShippingTask实例化”“Candidate不得重用ACTIVE普通Allocation”“固定2天跨Domain LT”“完整审批硬前置”等均不得作为现行实现依据。**


**v3.30更新内容**（2026-07-14 白天实时评估、Candidate版本与异常变化闭环，对齐 DDL v5.1.0 / 字段说明 v5.1.0 / 防腐层 v1.34 / 集成接口 v1.24）：
- 🆕 白天实时评估统一主链：Scenario（适用时）→ 3号位创建ScheduleRun（写入BasePlanVersionId/StrategyProfileVersionId/ScopeJson/ExpectedDomainKeysJson(["BasePlanVersion.DomainKey"],严格单Domain)/DataCutoffTime）→ 3号位创建Candidate PlanVersion → 2号位构造Candidate独立Order快照 → 实时BOM三表（复用判断+EnsureRealtimeBomReady）→ 三张RAW → RemainingSupplyContext → ScheduleContext → Task/ShippingTask → 有限产能排定 → Candidate结果持久化 → PlanVersion=CANDIDATE → ScheduleRun=COMPLETED（写 CompletedAt）
- 🆕 ScopeJson固定11字段（Purpose/OrderCanonicalIds/FactoryIds/ProductFamilyIds/ResourceGroupIds/PlanHorizonStart/PlanHorizonEnd/LockedTaskIds/AllowTouchFrozenZone/AllowDelaySalesOrder/MaxImpactedOrders）并作为运行范围唯一权威；ScheduleRun创建后ScopeJson不可变，禁止静默扩大Scope
- 🆕 Candidate订单快照：`PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)` 复制Base Order快照 + 仅叠加ScopeJson.OrderCanonicalIds指定订单的最新Order_Canonical变化 → 写入Candidate PlanVersion独立Order分区；禁止修改Base/ACTIVE版本；Order表按PlanVersionId隔离，OrderCanonicalId为稳定关联字段
- 🆕 实时BOM RequestDetail链：2号位先判断BOM切片是否可合法复用；不可复用时创建RequestDetail → `EnsureRealtimeBomReadyAsync(requestDetailId)` → `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` → Workset_Realtime / StageDetail_Realtime / CrossFactoryEdge_Realtime（ODS三张Realtime结果表，均允许0行）→ `PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)` 搬运到三张APS RAW；APS RAW的BatchNo固定为`RT:RD:{RequestDetailId}`；生成OrderBomRequestLink
- 🆕 Candidate剩余供给：`BuildRemainingSupplyContextAsync(candidatePlanVersionId)` 基于 `Base ACTIVE版本原始供给 - Base ACTIVE版本已确认分配/消耗 = Candidate剩余可用供给`；不得重新读取当前全部库存；Candidate不得重用ACTIVE已占用的供给
- 🆕 RunType+Purpose五个精确组合：`INSERT_ORDER_WHATIF+CTP`（永不激活）/ `INSERT_ORDER_WHATIF+INSERT_IMPACT_ANALYSIS`（永不激活）/ `LOCAL_RESCHEDULE+INSERT_RESCHEDULE`（审批后可激活）/ `LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT`（审批后可激活）/ `MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT`（审批后可激活）；CTP和INSERT_IMPACT_ANALYSIS是ScopeJson.Purpose，不是RunType
- 🆕 异常/变化统一闭环：事件或变化事实 → 5号位ImpactAssessment → 返回 Recommendation（及 ScheduleExplanationFact；⚠️ 此处原"Voucher"为历史已废止口径，V1 不落地 TaskPauseVoucher/TaskResumeVoucher）→ 4号位展示 → PMC人工确认 → 3号位创建ScheduleRun；禁止自动重排；ImpactAssessment/Recommendation是规则评估结果对象，不在本文件新增物理表
- 🔄 排程运行编排从"阶段二预留骨架"升级为白天实时正式链路；保留夜间全量既有时序，不修改ScheduleRun/PlanVersion夜间创建时机
- 🔄 1—5号位职责收敛：3号位负责ScopeJson归一化/ScheduleRun生命周期/Candidate PlanVersion创建/审批编排/激活；2号位负责Candidate数据构造/BOM复用判断/RAW搬运/RemainingSupplyContext/Pegging/Task实例化/结果持久化；5号位负责规则计算/BOM展开/ImpactAssessment，返回Recommendation（及 ScheduleExplanationFact；⚠️ 此处原"Voucher"为历史已废止口径，V1 不落地 TaskPauseVoucher/TaskResumeVoucher），不创建ScheduleRun不扣库存不写物理Pegging
- 🔄 Scenario定位更新：Scenario在白天实时评估中已可作为正式业务容器，INSERT_ORDER_WHATIF通常可创建Scenario；MANUAL_RESCHEDULE不要求先建Scenario；SimulationRun/ScenarioObjectiveScore仍为阶段二骨架
- 📌 V1 PipelineSupplies仍为空集合；未来Candidate管道供给必须读取BasePlanVersion对应SourceScheduleRun的准确非空BatchNo切片，禁止全局按IsActive=1读取
- 📌 ResourceEventDto当前仅从MES_Actual_Staging取得ResourceCode/EventType/EventTime；BreakdownReason/EstimatedRepairTimeMinutes/ActualRepairTimeMinutes均为null；不新增DDL字段；null字段展示"未提供"
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
- 【设计决策】1号位消费查询必须显式按StageScopeType区分

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

本文档负责回答一个问题：**每一种APS关键数据，从源系统事实到防腐契约、APS落地、运行时内存以及最终消费者，究竟由谁承接、在哪一层改变语义。**

当前权威层级固定为：
1. 《APS V1最终全部流程与业务基线》；
2. 《APS Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版》与《APS有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版》；
3. 本文及其余五份权威技术文档；
4. 代码实现。

因此本文只做技术承接，不重新设计业务。

---

## 1. 分层定义

| 层级 | 定义 | 主要职责 | 典型对象 |
|---|---|---|---|
| **源头事实层** | ERP/MES/采购/VMI等业务系统中的原始事实 | 保存真实业务事实；APS不得反写或凭空推导不存在的源事实 | ERP订单、PI、库存、PO/VMI、Received、厂间在途、MES报工、MES资源 |
| **契约/防腐层（ODS）** | 稳定视图、物化边表、标准事实输出 | 屏蔽源库结构、编码和多源差异；5号位在复杂事实处负责解释与标准化 | `MES_BOM_Edge_Active`、`ERP_Master_View`、`MES_APS_*`、采购/VMI/Received/在途契约视图 |
| **工作集/事实计算层** | 当前窗口切片、BOM Workset、Stage/PI位置计算、资格裁决输入 | 只计算“可被APS消费的事实”，不得替2号位执行最终Demand→Supply分配 | BOM Workset/StageDetail、StageProgress、CrossFactoryEdge、PI Position计算输入/输出、库存资格明细 |
| **APS业务结果层** | 版本化业务实体、供需账本、运行记录、最终排程结果 | 2号位持久化数量真相和运行结果；1号位输出的FinalTask由2号位落库 | `Order`、`PlanVersion`、`ScheduleRun`、Allocation/PSA、PI Position快照、Task、Explanation、Summary |
| **运行时内存层** | 一次ScheduleRun/Domain的完整不可变输入与可变计算沙盘 | 2号位装载；1号位/5号位消费；运行时规划性占位可只存在这里 | `ScheduleContext`、Demand/Supply Pool、Quantity-Time slices、Planning-only Purchase Placeholder、RoutingGraph |

**分层红线**：源事实与APS业务裁决必须分开；5号位负责复杂事实，不修改APS运行余额；2号位负责Pegging数量真相和持久化；1号位负责有限产能与时间真相。

---

## 2. 各类基础数据分层承接与演变总表

| 数据类别 | 源头 / 契约事实 | 工作集 / 计算层 | APS业务落地 | 运行时输出 / 消费 | 负责人 / Owner |
|---|---|---|---|---|---|
| **BOM / Stage路径** | ERP/MES BOM → `MES_BOM_Edge_Active`；Realtime/Batch Workset契约保持 | 5号位展开Workset、StageDetail、CrossFactoryEdge和Issues；2号位搬运三张APS RAW、生成`OrderBomRequestLink` | `APS_BOM_RAW`、`APS_BOM_STAGE_PATH_RAW`、`APS_BOM_CROSS_FACTORY_EDGE_RAW`、`OrderBomRequestLink` | 2号位用于分层Pegging与求解上下文；1号位只消费已解析Routing/Stage起点 | 5号位复杂展开/事实；2号位装载与关联；0号位业务口径 |
| **Master / Material** | ERP/MES主数据契约 | `MaterialMapping`统一源ID；`MaterialSupplyContext`承接仓库级业务上下文 | `Material`、`MaterialMapping`、`MaterialSupplyContext` | 订单/BOM/Routing/库存/供给的统一物料身份 | 5号位契约；2号位映射与装载 |
| **Routing / Resource** | `MES_APS_Routing_Operation_View`、Dependency、Eligibility、Stage、Resource契约 | 5号位/数据Owner维护ODS事实契约；2号位按Material/ProductionDepartment装载；`MaterialStageDeptContext`作为部门解析入口 | `RoutingOperation`、`RoutingDependency`、`OperationResourceEligibility`、`Resource`、`MaterialStageDeptContext` | 2号位将完整合法候选图交1号位；1号位选择最终Resource/Path/时间 | 5号位/数据Owner维护源事实；3号位只治理可变规则/参数；2号位装载；1号位求解 |
| **Order / Demand** | ERP订单/PI → `ERP_Order_Staging` → `Order_Canonical`；进入APS的SALES_ORDER数量已经过ERP成品库存处理 | 00:00划90天OPEN活跃根；白天Candidate复制Base快照并叠加目标订单变化 | `Order`按PlanVersion隔离；`PriorityScore`如物理列仍存在，只作历史/展示兼容，**不作为V1排序权威** | 2号位按“计算层→有序规则段→第一命中→段内排序”形成Demand Pool | 2号位装载/排序执行；3号位冻结排序规则；0号位业务口径 |
| **Inventory / 现货库存** | ERP/MES库存契约 | 保留现有资格/优先级链：事实→Candidate→`InventoryAvailabilityRule`→可用明细→`InventoryBalance` | `InventoryBalance`是当前快照总量；Allocation不应通过修改共享`AllocatedQty`形成跨PlanVersion真相 | 2号位Supply Pool内原子扣减；需要来源解释时回到可用明细 | 5号位资格事实/复杂规则输入；3号位规则治理；2号位余额执行 |
| **生产指示 PI 总量** | ERP PI `Quantity / ReceivedQty`为总量边界；MES/XC/在途不重新计算总剩余 | 5号位接收PI相关可能事实包 | PI作为Supply进入2号位Supply Pool；承诺粒度=`PI号+数量` | 2号位先选PI，再消费其Position；**PI不得消费自己** | 5号位事实；2号位最终分配 |
| **PI Position** | MES Stage累计、PI级XC、PI级跨厂在途、PI总量、Stage路径 | 5号位按互斥位置闭合；异常强事实修正或UNLOCATED | **一张最小PI位置快照表，多行表示位置份额**；不建Header+Slice双生命周期 | 2号位按Position决定剩余Stage需求；1号位只收已解析起点/剩余工艺 | 5号位计算；2号位保存/消费 |
| **MES进度** | MES工单/Operation/Stage事实契约 | 三类快照继续保留；StageProgress是PI位置计算输入，不是1号位独立“扣Task”权威 | `MESWorkOrderSnapshot`、`OperationProgressSnapshot`、`StageProgressSnapshot` | 5号位→PI Position；2号位→生产需求；1号位→已解析执行锚点 | 5号位事实解释；2号位装载 |
| **采购 / VMI / 已到厂未入库 / 厂间在途** | 采购/VMI/ERP真实契约；ETA、PO号、行号、仓库、数量、质量状态、SourceDocument等 | 5号位提供真实源事实/AvailableTime基础；2号位装载并按资格/优先级形成Supply | `SupplyFact_Pipeline`及必要Received事实正式进入V1；不再定义为固定空链路 | 2号位将已选Supply转成`Quantity + AvailableTime`给1号位 | 5号位ODS/复杂事实；3号位默认参数；2号位Pegging/装载 |
| **规划性采购占位** | 无正式库存/已到厂未入库/PO/VMI承诺时产生的规划缺口 | 2号位在运行时用DefaultLT等冻结参数估算 | **不落成已承诺Supply，不建采购单，不生成Task** | `Planning-only Purchase Placeholder`：Qty=缺口，`ESTIMATED/NOT_COMMITTED`，仅用于90天连续规划和非确定CTP | 2号位运行时生成；3号位提供参数 |
| **Pegging Allocation / PSA** | Demand与Supply资格、优先规则、Lock事实 | 2号位维护DemandBalance/SupplyBalance并双边原子扣减 | Demand↔Supply Allocation；`PeggingSupplyAllocation`承接非Task供给分配细账；Task-to-Task物理血缘单独保存 | 1号位接收逻辑生产需求与Allocation份额，不重新选择Supply | 2号位唯一数量真相Owner |
| **Strict Binding / Demand Protection / Execution Constraint** | 客户专属/质量资格、保护条件、MES执行事实 | 3号位治理Lock Policy；2号位按份额执行/传播/释放 | 同一需求—供给锁实体可用`LockType`区分Strict/Protection；Execution事实独立 | Candidate/夜间FULL只释放普通未锁关系 | 3号位策略；2号位执行；5号位不返回最终HardLock |
| **计划良率** | 计划参数，不假设BOM存在ScrapRate | 2号位从净需求向上反算各Stage计划加工量 | `NetRequiredQty/NetOutputQty`与`PlannedProcessQty`分开持久化/传输；PI Supply不膨胀 | 1号位按`PlannedProcessQty`占有限产能；Allocation按净合格产出闭合 | 3号位参数；2号位反算；1号位排程 |
| **Task / FinalTask** | 无独立源事实；由逻辑生产需求、Routing、资源、物料时间约束求解形成 | 2号位只形成逻辑生产需求和ScheduleContext；**不预生成最终Operation Task** | 1号位返回`FinalTask`、Task↔Task物理依赖、AllocationTaskShare；2号位统一落库 | Task可承接多个Demand份额；`Task.Quantity`=净合格产出，`PlannedProcessQty`=产能加工量 | 1号位时间/资源真相；2号位持久化 |
| **跨厂 STAGE_HANDOFF** | 目标M、PI、PI级XC/在途、CrossFactoryEdge、LeadTime事实 | 顺序固定：目标M→选择PI→PI Position→缺口新增生产 | 不建立ShippingTask有限物流主链 | 运输/检验只形成下游AvailableTime | 2号位Pegging；5号位跨厂/位置事实；1号位消费剩余工艺 |
| **跨厂 INTER_FACTORY_ORDER** | 目标BS/KS、SH、同SH在途、同SH Received、SH未生产份额 | 顶层Demand→BS/KS→SH；SH内部在途→同SH Received→未生产；未生产进入下一层源厂生产Demand | SH是业务Supply；物流不生成有限产能Task | 上游完成+真实转运LT形成下游AvailableTime | 2号位分层Pegging；5号位Received/在途事实 |
| **跨Domain依赖与供给** | `Domain_Dependency`表达产品Domain依赖；真实转运LT来自工厂/Stage事实 | 夜间按拓扑运行；上游结果按**多段Quantity-Time**传下游，不汇总成单一虚拟库存 | 不建`VirtualInventoryBalance`；Domain各自PlanVersion | 40@15日、60@17日必须保持两段；共享资源必须保持真实互斥 | 2/3号位编排；1号位共享资源时间轴 |
| **ScheduleRun / PlanVersion** | 运行触发和冻结规则版本 | FULL一次Run可含多个Domain PlanVersion；Candidate严格单Domain | `ExpectedDomainKeysJson`独立不可变；`RUNNING/COMPLETED/PARTIAL_SUCCESS/FAILED`；PlanVersion `BUILDING/ACTIVE/CANDIDATE/FAILED/...` | 无关Domain失败不阻止成功Domain；依赖失败阻断下游本次发布 | 3号位生命周期/触发；2号位计算结果与持久化 |
| **夜间失败恢复** | 当前Run失败事实 | 上游失败后直接/间接依赖下游本次不发布新ACTIVE | 原FAILED/PARTIAL_SUCCESS历史不可修改；人工恢复创建**新ScheduleRun** | 自动推导失败根Domain及被阻断依赖链后按拓扑重算 | 3号位发起；2号位执行；4号位展示/操作 |
| **Candidate / RemainingSupply** | Base ACTIVE + 当前真实物理供给 + 新订单变化 | 固定已消耗、Strict、Protection、不可逆份额；普通未锁Allocation释放重竞争 | Candidate新PlanVersion，不修改Base | `ScopeJson`是发起范围，不是物理影响硬边界；影响由1号位动态传播 | 2号位Pegging/变化种子；1号位局部传播 |
| **白天共享资源** | 其它Domain ACTIVE在共享设备上的已排占用 | 加载为当前Candidate不可移动阻挡块 | 不建跨Domain资源配额/借用/影响传播状态平台 | 单Domain Candidate不得挤动其它Domain | 2号位装载；1号位遵守阻挡 |
| **跨Domain WHATIF / CTP** | 依赖拓扑与各Domain Base ACTIVE | 后台多个单Domain WHATIF串行，例如C→B→A | 每个Candidate仍只属于一个Domain；不建MultiDomain Candidate/Group | 逐层传Quantity-Time，最终前端合并一个CTP+影响答案 | 3号位编排；2号位数据/Pegging；1号位求解；4号位展示 |
| **Firm / Frozen** | 上一ACTIVE仍有效的不可移动排程事实 + MES执行事实 | 2号位在新Run装载为锚点；ExecutionLock独立反映执行不可逆 | **不建设FrozenZoneSnapshot平台** | 1号位视为不可移动/受限块 | 2号位装载；1号位执行约束 |
| **Explanation / Summary** | Solver真实约束、资源/物料/依赖/Lock事实 | 1号位在求解中原生产`ExplanationFactDraft`；2号位映射业务对象 | `ScheduleExplanationFact`、订单/资源/KPI Summary | `DUE_DATE_RISK`是结果，不替代真实根因；不保存完整SolverTrace | 1号位产出；2号位落库/聚合；4号位展示 |
| **MES下发与执行反馈** | ACTIVE正式计划 + MES五态/回报 | 下发前过滤正式发布资格；Candidate/规划性占位/无PI占位/UNLOCATED保守Task不得下发 | 出站台账幂等；执行反馈更新事实而非跨版本复用旧TaskId | 次日FULL按最新事实重新Pegging和排程 | 2号位出站/反馈编排；5号位事实；4号位展示 |

---

## 3. 每类数据的一句话理解

### 3.1 BOM与Routing
BOM回答“需要什么、从哪一Stage需要”，Routing回答“该Stage有哪些合法Operation/Resource候选”；2号位把二者组装成逻辑求解问题，1号位才决定最终Task、设备和时间。

### 3.2 Order与Demand
`Order`是版本化业务快照，不是最终排队序号容器；V1需求优先由冻结规则按层、规则段和第一命中执行，`PriorityScore`不再定义业务真相。

### 3.3 Inventory与外部Supply
库存、采购/VMI、Received、厂间在途都先保持真实物理身份，再由2号位在同一Demand/Supply余额框架中扣减；同一物理数量在同一PlanVersion只能有一个身份。

### 3.4 PI与Position
PI总剩余由ERP边界决定；MES、XC、在途只解释“剩余量现在在哪”。因此必须先选PI，再消费其互斥Position，绝不能把多个PI的位置拉平后全局混排，也不能让PI消费自己。

### 3.5 Pegging
Pegging只决定“谁用谁、用多少、还缺多少”，不决定最终设备和时间。2号位是数量真相Owner；1号位不能自行更换已选Supply。

### 3.6 计划良率
净需求量用于Demand/Allocation闭合，`PlannedProcessQty`用于占用设备产能；计划损失不是客户需求数量，已有PI也不能因此膨胀Supply。

### 3.7 跨厂
STAGE_HANDOFF是同一PI沿工艺继续；INTER_FACTORY_ORDER是目标库存/SH承诺链。两者都不建设有限物流Task，运输只改变AvailableTime。

### 3.8 跨Domain
Domain是计划与版本边界，不是资源隔离墙。白天Candidate仍单Domain，但其它Domain ACTIVE共享设备占用不可移动；夜间FULL必须保证所有Domain对同一物理设备的占用真实互斥。

### 3.9 Candidate
Candidate基于Base ACTIVE，但普通未锁Allocation可释放重竞争；Scope只定义发起请求，不得截断真实资源/工艺/物料影响。影响过大时使用同一Solver升级为当前单Domain完整可移动范围重排。

### 3.10 失败恢复
失败历史不可改写。依赖链中上游失败会阻止下游本次发布；人工修复后新建ScheduleRun，只重算必要上游/失败根/被阻断下游链。

### 3.11 MES
MES只接收已经正式发布、具备真实执行依据的Task；Candidate、无PI虚拟占位、UNLOCATED保守Task和规划性采购占位均不能以“正式生产任务”下发。

---

## 4. 当前版本关键红线

1. **冻结业务优先**：本文不得因旧代码、旧表或“更标准”而重新设计业务。
2. **SALES_ORDER不二次扣成品库存**：进入APS的数量已经是ERP处理后的需生产量。
3. **需求排序不使用全局PriorityScore作为权威**。
4. **Demand/Supply必须双边余额闭合，禁止只记Allocation不扣余额**。
5. **同一物理Supply在同一PlanVersion只能有一个身份，禁止库存/PO/在途/PI重复使用**。
6. **先选PI，再消费PI Position；不同PI的位置不得拉平全局排序**。
7. **PI总量由ERP边界决定；MES/XC/在途不重新叠加PI总量**。
8. **PI不能消费自己**。
9. **PI Position必须互斥闭合；无法可靠定位时UNLOCATED保守降级，不得猜测**。
10. **StageProgress由5号位用于PI Position，1号位不得形成第二套PI位置解释**。
11. **采购/VMI/Received/厂间在途是V1正式供给事实，不得再固定空跑**。
12. **规划性采购占位只在内存存在，必须标ESTIMATED/NOT_COMMITTED，不生成采购单/Task，不下ERP**。
13. **STAGE_HANDOFF不搜索上游普通M作为直接跨厂借用Supply**。
14. **INTER_FACTORY_ORDER中同SH在途与Received必须防重复，未生产份额才进入下一层生产Demand**。
15. **V1绝不建设有限物流Task/ShippingTask体系；运输只形成LeadTime→AvailableTime**。
16. **Demand Protection、Strict Binding、Execution Constraint必须分开；Protection锁份额不锁整PI/PO**。
17. **夜间FULL普通Allocation每次按最新规则重新竞争，不保留跨版本稳定性偏好**。
18. **2号位只生成逻辑生产需求；FinalTask、Resource、Start/End、最终拆合批由1号位决定**。
19. **Task可承接多个Demand份额；真实归属走AllocationTaskShare，不假定Task=一个Order**。
20. **`Task.Quantity`与`PlannedProcessQty`语义必须分开**。
21. **90天使用一套正式有限产能Solver；周/月/季度只是聚合视图**。
22. **跨Domain供给必须保持Quantity-Time分段，禁止汇总成单一AvailableTime**。
23. **跨Domain转运LT使用真实工厂/Stage口径，不以固定2天作为业务权威**。
24. **夜间Domain独立版本不等于资源独立；共享设备必须真实互斥，不能双占**。
25. **Candidate严格单Domain；其它Domain ACTIVE共享设备占用是不可移动阻挡块**。
26. **Candidate普通未锁Allocation可释放重竞争；不能继续使用“Base供给－Base全部Allocation”旧公式**。
27. **ScopeJson、MaxImpactedOrders、PlanHorizon不得截断真实物理影响传播**。
28. **跨Domain插单由多个单Domain WHATIF拓扑串行，不建MultiDomain Candidate**。
29. **CTP与插单影响应复用同一次WHATIF结果，不为两个Purpose重复跑Solver**。
30. **WHATIF永不自动正式；正式采用只需最小人工确认审计，不强制完整OA审批主链**。
31. **不建设FrozenZoneSnapshot和VirtualInventoryBalance平台**。
32. **FAILED/PARTIAL_SUCCESS历史不可改；人工恢复必须新建ScheduleRun**。
33. **上游Domain失败时，其依赖下游本次不得发布新ACTIVE；无关Domain可正常发布**。
34. **Candidate、无PI占位Task、UNLOCATED保守Task不得下发MES**。
35. **Explanation由1号位原生产出；2号位只做业务映射、持久化和汇总**。
36. **V1不因未来扩展新增通用插件、DSL、动态脚本、第二套Solver、资源配额平台或多域原子发布平台**。

---

## 5. 0～5号位负责人最终承接关系

| 角色 | 当前V1职责 | 明确禁止越界 |
|---|---|---|
| **0号位** | 业务冻结、关键口径裁决、最小人工确认边界、最终验收 | 不参与Solver技术参数、逐条数据实现 |
| **1号位** | FinalTask、Resource、Start/End、拆合批、物理Task依赖、共享资源互斥、局部影响传播、Explanation Fact | 不读写DB；不重新选择Pegging Supply；不直接解释原始PI Stage事实 |
| **2号位** | ScheduleRun主流程协同、Demand/Supply池、排序执行、Pegging、Lock执行、PI选择、计划良率反算、逻辑生产需求、ScheduleContext、结果持久化、Candidate变化种子 | 不预生成最终Operation Task；不替5号位做复杂PI位置推导；不替1号位选最终时间槽 |
| **3号位** | 规则/参数治理、不可变版本快照、Run/Candidate生命周期和必要编排 | 不修改运行时余额；不逐Task回调；不建设通用脚本/插件平台 |
| **4号位** | CTP/插单/甘特图/异常/失败重算入口、Candidate比较和最小人工确认 | 不直接改业务运行表，不绕过正式服务写DB |
| **5号位** | ODS、防腐、BOM/Stage、跨厂事实、采购/VMI事实、PI Position等复杂事实计算与数据Issue | 不做最终Demand→Supply Allocation，不扣APS余额，不决定最终Lock，不生成FinalTask，不负责Solver ReasonCode |

> **关于历史Voucher/插件文字**：历史版本说明可保留追溯，但当前主链不得再用“5号位插件返回最终Pegging/Task裁决、2号位照单执行”描述总体架构。运行时业务余额和最终Allocation由2号位直接负责；5号位只提供复杂事实与必要计算结果。

---

## 6. 工厂归属关系总表（历史数据结构保护区，v3.32继续保留）

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
| **Order** | 否 | 🔗 `FactoryId NOT NULL FK` | `sp_SyncOrdersToPartitionTable`：`Order_Canonical.FactoryCode` → `Factory.Code` 映射得 `Factory.Id` | 映射失败订单不得写入，应标记失败；`ISNULL(f.Id, 1)` 静默默认工厂逻辑**待后续 DDL/SP 修订中删除**（当前 DDL v5.1.1 尚未完成该修订，不得写成"已废除"） |
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
6. **DDL待同步**：当前 DDL v5.1.1 中 `Order_Canonical.FactoryCode` 允许 NULL 进入 Canonical 的 Step 2e TODO 桩，以及 `sp_SyncOrdersToPartitionTable` 中的 `ISNULL(f.Id, 1)` 默认工厂代码，**尚未完成修订**；不得写成"已经废除"；待后续配套 DDL/SP 修订版本统一处理。
7. **不得发明失败码**：不得自行发明新的 FailureCode，具体失败码另行在 DDL/字段说明中统一。
