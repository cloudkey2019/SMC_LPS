-- =============================================
-- APS 数据库表结构设计 DDL v5.1.2（冻结对齐版）
-- 版本: v5.1.2
-- 冻结日期: 2026-08-12
--
-- 【冻结权威声明】
-- 本DDL是《APS V1最终全部流程与业务基线》+《Pegging业务说明v1.1冻结对齐版》+
-- 《有限产能排产业务说明v1.1冻结对齐版》在物理数据库层的最小实现。
-- 下层DDL不得反向修改已冻结业务。旧表/旧字段/旧代码若冲突，优先兼容退出旧路径。
-- 只有明确“重新打开冻结决策”并经0号位重新裁决，才允许改变业务语义。
--
-- v5.1.2更新（2026-08-12 冻结对齐，不重新设计业务）：
--   1) Task.OrderId降为可空兼容展示字段；新增 PlannedProcessQty；FinalTask真实Demand归属由 AllocationTaskShare 承接。
--   2) 新增 AllocationTaskShare、ProductionInstructionPositionSnapshot、DemandSupplyHardLock、PeggingSupplyAllocation 最小结构；
--      不新增 PeggingAllocationLedger / PI Header+Slice / DemandProtection第二套表。
--   3) SupplyFact_Pipeline 正式承接 V1 厂间在途/采购在途与未结PO/VMI/已到厂未入库；新增 CommitmentStatus；
--      删除“V1固定空链路/空跑成功”实现。真实ODS来源未绑定时必须失败，不得伪装0行成功。
--   4) Planning-only Purchase Placeholder 仅内存存在，明确禁止建表/生成Task/采购单。
--   5) InventoryBalance.AllocatedQty仅保留兼容快照字段；V1运行期Allocation在2号位内存完成，不永久UPDATE库存事实。
--   6) Domain_Dependency.DefaultLeadTimeDays取消固定2天默认权威，仅作兼容缓存；真实转运LT由工厂/Stage事实提供。
--   7) PriorityScore/ComputeMode旧值保留兼容，但PriorityScore不再是V1排序权威，ROUGH_CUT不再选择第二套90天Solver。
--   8) MES Progress快照改为5号位PI Position事实输入；1号位只消费已解析起点/剩余工艺/执行约束。
--   9) ShippingTask/TRANSFER/PROCUREMENT不进入V1新生成有限产能Task体系；跨厂运输只形成LeadTime→AvailableTime。
--  10) FenceConfig/Scenario/OA等已有结构可保留，但不得反向要求完整审批、Scenario强依赖或FrozenZoneSnapshot平台。
--
-- v5.1.0更新（2026-07-13 白天实时评估与局部重排链路）：
--   1) 新增 dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime 表 + 3 个索引
--   2) 新增 dbo.sp_GenerateBOMCrossFactoryEdgeRealtime SP，按 RequestDetailId 严格隔离
--   3) 修改 sp_EnrichBOMWorksetRealtime：Step 5 日志前调用 CrossFactoryEdge_Realtime，重新统计 IssueCount
--   4) 修复 sp_ExpandBOMRealtime_vNext：统一使用 @SyntheticBatchNo，正式路径 RT:RD:{RequestDetailId}，deprecated 兼容 RT:{ResolvedBOMNO}
--   5) READY 更新时回填 ExpandedRowCount，仅诊断，不参与 READY 判断
--   6) MES_API_BOM_Request_Realtime 现有字段不动；RequestDetailId/OrderCanonicalId 保持 NULL；不新增物理 FK
-- v5.1.1更新（2026-07-20 APS V1 最终决策，0号位确认）：
--   1) ScheduleRun 新增独立字段 ExpectedDomainKeysJson（运行启动冻结的预期DomainKey集合，终态判定唯一权威来源）+ JSON合法性CHECK约束；
--      ScopeJson 注释移除 ExpectedDomainKeys 相关内容（预期域集合不再承载于 ScopeJson）
--   2) ScheduleRun.Status 四态（RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED）口径已在表头注释固化；CompletedAt 含 PARTIAL_SUCCESS 写入
--   3) PlanVersion 唯一索引 UQ_PlanVersion_ScheduleRun_Domain 从建表位置（过早，列未建）下移：
--      在 ScheduleRun/SourceScheduleRunId/SourceSimulationRunId/FK 全部完成后创建，过滤条件加 AND SourceSimulationRunId IS NULL（阶段二Simulation候选不受阻止）
--   4) ScheduleExplanationFact 新增 ScheduleRunId(INT NULL) + FK_SEF_ScheduleRun + IX_SEF_ScheduleRun(ScheduleRunId, ReasonCode)；
--      ObjectType 值域补 STAGE / DOMAIN（DOMAIN 用于跨域版本不一致风险 CROSS_DOMAIN_VERSION_MISMATCH_RISK）
--   5) 禁止新增 PAUSED 状态 / 暂停相关新表 / 跨域发布组表；不保留任何将 ExpectedDomainKeys 写入 ScopeJson 的旧口径
-- v5.0.46更新（2026-06-23 跨厂Pegging补强：ERPProperty/Received视图/CrossFactoryEdge/PeggingSupplyAllocation/MOCK数据）
-- v5.0.45更新（2026-06-23 规则与参数引擎：RuleSet/RuleSetVersion/ParameterSet/ParameterSetVersion/StrategyProfile/StrategyProfileVersion + ScheduleRun ALTER ADD StrategyProfileVersionId）
-- 日期: 2026-08-12
-- 基于: 《APS数据架构与防腐层设计方案 v1.36 冻结对齐版》+《APS数据库字段说明文档 v5.1.2 冻结对齐版》
-- 数据库: SQL Server 2019+
-- 特性: 分区表、RCSI、ODS库、防腐层、批次状态机、权限系统
-- 更新: 新增ODS库表结构、MaterialMapping表（SCD Type 2）
--       修改PlanVersion表（新增快照字段）、APS_BOM_RAW表（新增LLC字段）
-- v2.6更新: 权限系统集成，PlanVersion表新增CreatedByUserId/CreatedByUserName字段
--          MES_API_BOM_Request表新增TriggeredByUserId/TriggeredByUserName字段
-- v2.5更新: 修复Order_Canonical表缺失问题、统一Material表字段命名(Code→MaterialCode)
-- v5.0.2更新(2026-04-03 订单链路审计):
--   - ERP_Order_Staging: 补SourceSystem/SourceMasterID字段，CustomerDueDate→DueDate，BOMNO改NOT NULL，DEFAULT 'PENDING'
--   - Order_Canonical: 补FactoryCode/UOM字段，新增Upsert键索引(SourceSystem+SourceOrderId)
--   - 新增 sp_ValidateAndPromoteOrders: ERP_Order_Staging校验并提升到Order_Canonical
--   - sp_SyncOrdersToPartitionTable: FactoryId从Factory表查（去硬编码），DomainKey改TODO注释
-- v5.0.3更新(2026-04-09 订单业务字段补充，基于《订单ETL补充字段设计建议v1.1》):
--   - ERP_Order_Staging: +6列（TransportMode/CustomerName/MTS_InstructionNo/CustomerSegment/SalesOrderCategory/DemandMaturityStatus）
--   - Order_Canonical: +6列（同上）
--   - Order: +5列（TransportMode/CustomerName/CustomerSegment/SalesOrderCategory/DemandMaturityStatus，MTS_InstructionNo已有）
--   - Task: +1列（MTS_InstructionNo，从Order冗余避免反查）
--   - sp_ValidateAndPromoteOrders: MERGE语句补新字段透传 + 修复FactoryCode未进INSERT的遗漏
--   - sp_SyncOrdersToPartitionTable: 补新字段 + MTS_InstructionNo改为从Canonical读真实值（原用OrderNo代替，与业务不符）
--   - 【业务澄清】OrderType/FactoryCode为APS衍生/标准化字段（非ERP直接源字段），本次仅修正注释说明
-- v5.0.4更新(2026-04-09 订单ETL字段补充v1.2增补，基于《仅1.2增补内容v1.0》)：
--   - ERP_Order_Staging: +3列（IssueDate/OriginalDueDate/ReceivedQty）
--   - Order_Canonical: +3列（同上）
--   - Order: +3列（同上）
--   - sp_ValidateAndPromoteOrders: MERGE语句补新3字段透传
--   - sp_SyncOrdersToPartitionTable: 补3字段透传
-- v5.0.5更新(2026-04-09 客户分级字段)：
--   - ERP_Order_Staging / Order_Canonical / Order: +1列 CustomerTier（APS衍生字段，VIP/KEY_ACCOUNT/STANDARD/GENERAL，默认GENERAL）
--   - sp_ValidateAndPromoteOrders: 补CustomerTier透传 + 衍生逻辑TODO桩
--   - sp_SyncOrdersToPartitionTable: 补CustomerTier透传
-- v5.0.7更新(2026-04-13 BOM双层结果+阶段提前期参数化，基于《BOM阶段顺序与Workset双层结果设计建议_v1.0》)：
--   ▸ 本版替代v5.0.6，方案升级：单一StageHintCode → 3原始辅助字段 + StageDetail双层结果
--   - MES_APS_BOM_Workset / Archive / Realtime: StageHintCode → ParentProcRefCode + ChildProcRefCode + ChildSourceHintCode（3个ERP BOM原始辅助字段，供5号位综合判断）
--   - APS_BOM_RAW: 去掉StageHintCode（APS侧只带最终结果），保留ChildRequiredStageCode
--   - 新增 MES_APS_BOM_Workset_StageDetail（5号位派生：BOM边的完整大工艺顺序明细）
--   - 新增 MES_APS_BOM_Workset_StageDetail_Archive（归档）
--   - 新增 MES_APS_BOM_Workset_StageDetail_Realtime（实时链路）
--   - 新增 APS_BOM_STAGE_PATH_RAW（APS本地缓存，2号位搬运）
--   - 新增 StageLeadTimeParam（阶段提前期参数表，参数化外协Task + 多级降级命中）
--   - RoutingStage: 结构不变，定位调整为"阶段字典/标准阶段语言"（不作为排程权威阶段顺序源）
--   - RoutingOperation.StageCode: 保留（1号位用于关联阶段下的小工序）
--   - sp_ExpandBOMBatch: 透传字段改为3辅助字段（原StageHintCode废弃）
--   - sp_ExpandBOMRealtime: 同上
--   - sp_CleanupBOMWorkset: 归档字段同步 + 新增StageDetail归档逻辑
--   - 【设计决策】ChildRequiredStageCode=NULL时按保守策略：子件必须全工艺完成后才可供给父件
--   - 【设计决策】RoutingStage=阶段字典，StageDetail=5号位派生结果，职责分离不混写
--   - 【设计决策】外协阶段不塞RoutingOperation虚拟工序，由StageLeadTimeParam参数化驱动Task生成
-- v5.0.8更新(2026-04-15 StageDetail升级为统一阶段路径结果表，支持ROOT根产品完工路径)：
--   - MES_APS_BOM_Workset_StageDetail / Archive / Realtime / APS_BOM_STAGE_PATH_RAW:
--     +1列 StageScopeType NVARCHAR(10) NOT NULL DEFAULT 'EDGE'（EDGE=子件供给路径 / ROOT=根产品完工路径）
--     ParentMaterialCode 改为可空（EDGE=父件编码；ROOT=NULL）
--   - sp_CleanupBOMWorkset: 归档字段同步加StageScopeType
--   - sp_ExpandBOMBatch / sp_ExpandBOMRealtime: 5号位回填注释补ROOT路径推导规则
--   - 【设计决策】ROOT记录：ParentMaterialCode=NULL，IsSupplyThreshold恒为0（V1不泛化此字段语义）
--   - 【设计决策】ROOT路径推导：Level=1的ParentProcRefCode → 映射标准化 → 多条不一致取最长+记WARNING（不静默并集）
--   - 【设计决策】1号位消费查询必须显式按StageScopeType区分（不得混查EDGE+ROOT）
-- v5.0.9更新(2026-04-18 跨域依赖静态扫描表，配合全流程走查文档修订)：
--   - 新增 Domain_Dependency（跨产品族域依赖表，01:50静态扫描产物）
--     字段：UpstreamDomainCode / DownstreamDomainCode / ChildMaterialCode / DefaultLeadTimeDays / ScannedAt
--     PK = (UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode)
--   - 消费方：3号位在02:00读取此表构建Kahn拓扑排序，决定域调度顺序
--   - 2号位每日01:50全量TRUNCATE+INSERT，数据源=APS_BOM_RAW + Material + ProductFamily
--   - 【历史口径，已废止】当时DefaultLeadTimeDays硬编码=2天；v5.1.2起禁止固定2天作为权威，必须使用真实工厂/Stage转运LT
-- v5.0.10更新(2026-04-23 R17/R25/R26/R27 工厂映射 + BOM错误容错，基于《BOM_Workset_生成与错误处理技术方案_v1.0》)：
--   ▸ 稳定合同原则：Workset/StageDetail 核心表最小改动；ERP 特征字段不下沉到 L1/L2 合同层
--   - MES_APS_BOM_Workset / Archive / Realtime: +1列 ChildRequiredFactory NVARCHAR(20) NULL
--     （R17 推导的子件账面工厂 CN/CN6课/BJ/TJ/SH；值域 APS 自定义枚举，ERP 升级不影响）
--   - APS_BOM_RAW: 同步 +1列 ChildRequiredFactory（从 Workset 透传）
--   - 订正 ChildSourceHintCode 注释："0/1/2编码" → "0-11编码（参见 §3.1 Produce 值域）"
--   - 新增 MES_APS_BOM_Workset_Issues（BOM 解析错误登记表）——独立诊断表，不影响 L1 合同
--     支持错误类型：LEAF / FACTORY_MISMATCH / FACTORY_MISMATCH_MULTI / NO_STAGE / UNKNOWN_PROCCODE / CYCLIC_BOM / QUANTITY_INVALID / MISSING_PRODUCE / EXPAND_FAILED
--     Severity: INFO/WARN/ERROR/CRITICAL；批次放行策略：CRITICAL/ERROR 阻塞，WARN 放行+登记，INFO 静默登记
--   - 新增 vw_MES_BOM_Stage_Enriched（ODS 派生查询便利视图，**非防腐层**）
--     用途：StageDetail + 工序对照字典 JOIN 派生 ProcessCode/StageFactory/ActualFactory/TrusteeProcCode 便利字段
--     边界：仅 ODS 内部跨厂/委外事实推导与运维诊断使用；V1不生成ShippingTask；**不是 APS 下游消费入口**
--     防腐定位：隔离"StageDetail 表结构不变"与"字典字段可能变"这对承诺；**APS 本地不做对称视图**，避免 ERP 特征字段下沉到 APS 排程内核
--   - sp_ExpandBOMBatch / sp_ExpandBOMRealtime: 5号位回填注释补 ChildRequiredFactory + Issues 写入
--   - 【设计决策】ChildRequiredFactory 的值域是 APS 自定义 5 厂实体枚举，由 R17 Produce→厂 映射规则产出；ERP 升级时只需 5 号位调整映射表，核心表字段不变
--   - 【设计决策】StageDetail 核心表**零改动**，扩展语义（ActualFactory/TrusteeProcCode 等 ERP 特征）全部走 vw_MES_BOM_Stage_Enriched
--   - 【设计决策】诊断/错误信息不进 Workset/StageDetail 核心表，单独进 MES_APS_BOM_Workset_Issues（结构可演进，不影响 L1 合同）
--   - 【设计决策】APS 本地**不新增派生视图**：ERP 特征字段若 APS 排程需要（如委外 LT），由 2 号位预计算落独立配置表（如 StageLeadTimeParam 扩展），不直接下沉 ERP 字段语义
-- v5.0.41更新(2026-06-12 MES生产进度快照三表 + 三个同步SP骨架):
--   新增 §2.9 MES生产进度快照（定位：APS本地快照，供1号位全量重算前扣减已完成量）
--   新增 MESWorkOrderSnapshot（2.9.1）：生产指示号→MES工单追溯关系快照
--   新增 OperationProgressSnapshot（2.9.2）：工序进度快照；RemainingQty CASE WHEN 计算列 PERSISTED
--     V1 工序识别主字段 = OperationName；UQ(ScheduleRunId+InstructionNo+WorkOrderNo+MaterialCode+OpName+StageCode)
--   新增 StageProgressSnapshot（2.9.3）：大工艺进度快照；RemainingQty CASE WHEN 计算列 PERSISTED
--     UQ(ScheduleRunId+InstructionNo+MaterialCode+StageCode)
--   新增 sp_SyncMESWorkOrderSnapshot：从 ODS.MES_APS_WorkOrder_View 同步；2号位实现；5号位收口视图
--   新增 sp_SyncOperationProgressSnapshot：从 ODS.MES_APS_OperationProgress_View 同步；UNION ALL收口
--   新增 sp_SyncStageProgressSnapshot：从 ODS.MES_APS_StageProgress_View 同步；UNION ALL收口
--   设计决策（写死）：Task/Pegging随新PlanVersionId每日全量重算；MES进度不匹配历史TaskId
--   设计决策（写死）：生产进度快照不混入InventoryBalance；EAM V1预留占位不取数
-- v5.0.40更新(2026-06-08 可用供给明细层新增；管道供给 V1 口径明确):
--   新增 InventoryAvailableSupplyDetail（§2.6.8.5）：规则命中后、余额汇总前的可用库存明细层
--     保留 SourceSystem / StorageCode / AvailabilityRuleId / RulePriority / InventorySupplyCandidateId
--     InventoryBalance 从本表汇总生成；排程引擎按 RulePriority 扣减时读本表
--     InventorySupplyCandidateId 不加 FK（避免阻塞 TRUNCATE TABLE InventorySupplyCandidate）
--   sp_SyncInventorySnapshot 升级为真正六步 ETL：
--     Step3 先 TRUNCATE InventoryAvailableSupplyDetail 再 TRUNCATE InventorySupplyCandidate（防御性顺序）
--     Step4c：胜出规则 IsAvailable=1 → 写 InventoryAvailableSupplyDetail（含 InventorySupplyCandidateId）
--     Step5：TRUNCATE + INSERT InventoryBalance，ProductFamilyId 来源明细层（非 Material.ProductFamilyId）
--     Step6 ETL 日志补 Detail 行数
--   InventorySupplyCandidate.IsEligible 白名单模式确认：DEFAULT 0；无匹配规则 → RejectReason='NoRuleMatch'
--   【历史口径，已废止】sp_SyncPipelineSupply 曾为V1空跑骨架；v5.1.2起V1必须接真实Timed Supply，未接通时失败，不得空跑成功。
--     不读 ERP 在途数据；不执行映射；不应用 SupplyAvailabilityRule；
--     SupplyAvailabilityRule 保留为 V1.1/V2 激活预留
--   库存六层架构总结（v5.0.40 定稿）：事实→候选→规则→明细→余额→内存
-- v5.0.39更新(2026-05-31 库存规则 V1口径收敛):
--   删除 ProductFamilyInventoryScope(旧§2.5.1) / InventorySourceRule(旧§2.5.2) / InventorySourcePriority(旧§2.6)
--   新增 InventoryAvailabilityRule: 统一「哪些库存可用 + 扣减优先级」
--   废弃 RuleAction(PREFER/EXCLUDE) 口径, 改用 IsAvailable + Priority
--   InventoryFact_ERP/MES 补 FactoryCode + WarehouseCode
--   InventoryBalance.ProductFamilyId = 库存使用上下文(非物料自身产品族)
--   InventoryBalance.BatchNo = 快照批次标签(非订单消耗记录)
--   新增 sp_SyncInventorySnapshot(@BatchNo) 六步 ETL 存储过程
--   【历史口径，已废止】ERP_InterplantInTransit_View 曾为空链路预留；v5.1.2起必须由5号位绑定真实ODS来源。
--   FactoryId 红线修正: 库存/供给事实+规则+余额表均可持有 FactoryId FK
-- v5.0.44更新(2026-06-18 四表职责收敛：ScheduleRun/PlanVersion/Scenario/SimulationRun)：
--   ▸ ScheduleRun：删除 OutputPlanVersionId（→反查）；新增 BasePlanVersionId+ScopeJson；索引调整
--   ▸ PlanVersion：VersionType→VersionCategory（sp_rename）；新增 SourceScheduleRunId/SourceSimulationRunId/ActivatedAt/ActivatedBy
--     Status 值域改为版本生命周期：BUILDING/CANDIDATE/ACTIVE/ARCHIVED/FAILED；StartedAt等标注为历史兼容字段
--   ▸ Scenario：Name→ScenarioName；RunType→ScenarioType；新增 ObjectiveJson+SelectedPlanVersionId+Status+UpdatedAt
--   ▸ SimulationRun：删除 PlanVersionId（→反查）；新增 AlgorithmType+AlgorithmConfigJson+Status+ErrorMessage
--   ▸ 关系：ScheduleRun(必有)→PlanVersion(一套结果一条)；Scenario(可选)→ScheduleRun→SimulationRun(可选)→PlanVersion

-- v5.0.43更新(2026-06-18 多仓映射+规则唯一胜出+SourceRowKey+DataCutoffTime+事务+FK)：
--   ▸ SupplyFact_Pipeline：SourceRowKey 持久化计算列（来源幂等）；FK 补全 ProductFamilyId→ProductFamily(Id) / SupplyAvailabilityRuleId→SupplyAvailabilityRule(Id)
--   ▸ 【历史口径，已废止】sp_SyncPipelineSupply：当时V1传值但空跑阶段不使用；v5.1.2起V1改为真实Timed Supply装载
--   ▸ V1.1/V2 TODO：MasterID+StorageCode→MaterialMapping.Warehouse_Norm 多仓映射；OUTER APPLY规则唯一胜出；
--     事务DELETE+INSERT批次原子替换；DataCutoffTime源数据截断与规则有效期判定；THROW异常传播
--   ▸ VMI 枚举统一：VMI_IN_TRANSIT → VMI_ONSITE；CHECK 约束同步更新
--   ▸ 注释清理：MaterialCode→APS标准编码；SourceMasterID→与StorageCode共同挂接
--   ▸ 索引：IX_MaterialMapping_SourceWarehouse（管道反查）；UX_SupplyFact_Pipeline_SourceRow_Batch（幂等）

-- v5.0.42更新(2026-06-15 管道供给链路完整骨架 + ODS空契约视图 + 字段契约锁定):
--   ▸ 【历史口径】ERP_InterplantInTransit_View 曾以14字段空契约占位；v5.1.2起字段契约保留，但不得以WHERE 1=0作为V1正式实现。
--   ▸ APS 层重建 ext_ERP_InterplantInTransit_View（显式列字段，禁止 SELECT *）
--   ▸ SupplyFact_Pipeline 新增4个来源追溯字段：SourceMasterID / SourceFactoryCode / SourceDocumentLineNo / SourceUpdatedAt
--   ▸ sp_SyncPipelineSupply V1.1/V2 TODO 升级为四步完整装载骨架（Step0校验→Step1读取→Step2映射→Step3规则→Step4写入）
--   ▸ ODS 契约锁定规则：V1.1/V2 仅允许替换视图 FROM 逻辑，禁止修改字段顺序/类型/名称
--   ▸ 分层语义统一：ODS视图=「ODS层/MES_Integration/来源ERP/5号位」；APS包装视图=「APS层/APS_Production/2号位」
-- v5.0.38更新(2026-05-30 产品族解析 V1口径修正):
--   V1口径：产品族判断由 5号位封装在 ERP_Master_View 内部逻辑；不新增 ODS 解析三张表；不新增独立解析SP
--   ▸ ERP_Master_View / MES_Material_View 契约升级 v1.5（V1最简口径）：
--      · 新增 IsProductFamilyRequired BIT NOT NULL（5号位按 ERP ProcessCode 判断是否需要产品族）
--      · 新增 ProductFamilyCode NVARCHAR(50) NULL（ODS 内部逻辑解析，不暴露 ERP 原始字段）
--      · 新增 FamilyResolveStatus NVARCHAR(30) NOT NULL
--        （值域：RESOLVED/NOT_REQUIRED/NO_RULE/AMBIGUOUS/FAMILY_CODE_NOT_FOUND/SOURCE_FIELD_MISSING）
--      · MES 侧 V1 固定返回 0 / NULL / 'NOT_REQUIRED'
--   ▸ ext_ERP_Master_View / ext_MES_Material_View：显式列列表，透传 IsProductFamilyRequired + ProductFamilyCode + FamilyResolveStatus
--   ▸ sp_SyncMasterData 步骤0快照新增 IsProductFamilyRequired；步骤1c 四规则消费逻辑
--   ▸ §1.11 MaterialProductFamilyScopeRule → V2 规则资产化预留，V1 不建
--   ▸ §1.12 MaterialProductFamilyRule → V2 预留（V1 判断逻辑封装在 ERP_Master_View 内部）
--   ▸ §1.13 MaterialProductFamilyResolved → V2 预留（V1 不建独立解析结果表）
--   ▸ sp_ResolveMaterialProductFamily → V2 预留（V1 不需要独立解析SP）
--   【设计决策】V1：IsProductFamilyRequired/ProductFamilyCode/FamilyResolveStatus 由 ERP_Master_View 直接输出；
--     5号位可在视图内读取 ERP.ITMASTER.ModelSort（不暴露给 APS 层）
--   【已知V1规则（仅在 ODS 视图内部）】ModelSort LIKE 'FRL%' → 'FRL'；ModelSort LIKE 'CYL%' → 'CYL'
--   【红线】APS 层禁止保存 ERP ProcessCode / ProdFinshProcCode / ModelSort
--   【红线】Order.ProductFamilyId 从 Material.ProductFamilyId 继承，禁止在订单层另建解析链
-- v5.0.37备注(2026-05-30 初版三表设计 - 已由 v5.0.38 修订为 V2 预留)
--
-- v5.0.36更新(2026-05-29 R37 R29工厂过滤降级兆底):
--   sp_ExpandBOMBatch_vNext + sp_ExpandBOMRealtime_vNext：
--   ▸ R37: SALES_ORDER+WIP%/RAW%+BOMNO=NULL 订单，R29 R17工厂过滤后无候选入口时，
--          降级去工厂过滤再查一次；命中则继续展开，并写FACTORY_MISMATCH_FALLBACK WARN
--     批量: EntryGroups后新增#R29FallbackIds临时表(Step1/2/3)——先模记无匹配DetailId，
--             再无工厂过滤查入口，再写WARN
--     实时: R29分支内加IF NOT EXISTS降级子块
--     根因场景: 订单FactoryCode=CN但BOM边ParentProcRefCode=010699(BJ工厂)时，R17过滤把唯一有效入口过滤掉→BOM_ENTRY_NOT_FOUND
--
-- v5.0.35更新(2026-05-27 R32/R33/R34/R35 BOM展开规则落地):
--   sp_ExpandBOMBatch_vNext + sp_ExpandBOMRealtime_vNext：
--   ▸ R33: RequestedBOMNO 空字符串/'0' 等价 NULL（NULLIF 规范化）
--   ▸ R34: 显式 BOMNO 首层不过滤 IsActive，含历史版本；Case A EXISTS 同步去 IsActive
--   ▸ R32: WHILE 迭代重构为三步（收集候选→MULTI_BOMNO_UNRESOLVED WARN→BOMNO精确展开）
--          ERP 非空 BOMNO 优先（ERPNNBOMCnt>0 取 MaxERPBOMNO），无 ERP 用任意最大非空，全 NULL 匹配 NULL 边
--   ▸ R35: WHILE 后新增 Produce 代入 UPDATE：NULL/'NULL' → ERP 边继承 → 前缀推断(RAW→2, 其余→1)
--   sp_EnrichBOMWorkset：
--   ▸ Step 0b: 增加 OR ChildSourceHintCode=N'NULL' 以处理 MES 导出的字符串 NULL
--   ▸ LEAF/NO_STAGE/FACTORY_MISMATCH Issue INSERT：JOIN 加 ChildRequiredFactory=nf.ChildRequiredFactory
--     防止同物料多 Produce 行时 Issue 记录错误 Produce 代码
--
-- v5.0.34更新(2026-05-26 OrderBomRequestLink业务锚点升级 + Order.OrderCanonicalId):
--   [Order] 表：
--   ▸ 新增 OrderCanonicalId BIGINT NULL — 来源 Order_Canonical.Id；允许 NULL 兼容历史数据
--   ▸ 新增过滤索引 IX_Order_PlanVersion_OrderCanonical(PlanVersionId, OrderCanonicalId) WHERE OrderCanonicalId IS NOT NULL
--   sp_SyncOrdersToPartitionTable：
--   ▸ INSERT 字段列表加 OrderCanonicalId；SELECT 加 oc.Id AS OrderCanonicalId
--   OrderBomRequestLink 全量重建（相比 v5.0.31 初版）：
--   ▸ OrderId BIGINT NULL（旧 NOT NULL）；删除 FOREIGN KEY (OrderId) REFERENCES [Order](Id)
--   ▸ 唯一约束由 UNIQUE(PlanVersionId, OrderId) 升级为 UNIQUE(PlanVersionId, OrderCanonicalId)
--     约束名：UQ_OrderBomRequestLink_Plan_Canonical（废止旧 UQ_OrderBomRequestLink_Version_Order）
--   ▸ 新增第4索引：IX_OrderBomRequestLink_Order(PlanVersionId, OrderId) WHERE OrderId IS NOT NULL
--   ▸ LinkStatus='SKIPPED'语义：订单未进入当前 PlanVersion 的 [Order] 快照（不是"前批次结果仍有效"）
--   BOMResultPullService 接口变更（防腐层 v1.26）：
--   ▸ PullBOMResultFromODSAsync 签名：增加显式 planVersionId 参数（由 NightlyBatchOrchestrator 传入）
--   ▸ 禁止 BOMResultPullService 内部自查最新 PlanVersion
--   ▸ Step 4 新增：GenerateOrderBomRequestLinkAsync(batchNo, planVersionId)
--   ▸ ResolvedBOMNO = Level=1 Workset.BOMNO；RepWorksetId = MIN(Workset.Id) WHERE RequestDetailId+Level=1
--   ▸ 数据源：ODS MES_APS_BOM_Workset 聚合（禁止从 APS_BOM_RAW 反查）
--
-- v5.0.32更新(2026-05-25 RequestDetail字段收敛):
--   MES_API_BOM_Request_Detail 定位收敛为纯 BOM 请求输入表：
--   ▸ 删除 Model（5号位不再依赖 ERP Model；只保留在 ERP_Order_Staging / Order_Canonical.SourceModel）
--   ▸ 删除 OrderStagingId（ERP_Order_Staging 仅作同步缓冲层；BOM主链锚点= OrderCanonicalId）
--   ▸ 删除 ResolvedBOMNO（Workset解析结果不回写 RequestDetail；改由 2号位写 OrderBomRequestLink.ResolvedBOMNO）
--   ▸ sp_ExpandBOMBatch_vNext 删除步骤5a（ResolvedBOMNO回写）
--   ▸ 最终字段：Id/BatchNo/OrderCanonicalId/OrderNo/SourceSystem/SourceOrderId/MaterialCode/FactoryCode/OrderType/RequestedBOMNO/CreatedAt
--   ▸ OrderBomRequestLink.ResolvedBOMNO = 2号位从 Level=1 Workset.BOMNO 取值生成
--
-- v5.0.31更新(2026-05-25 Order→BOM追溯链闭合):
--   MES_API_BOM_Request_Detail（ODS侧）：
--   ▸ 锚点升级：OrderStagingId(NOT NULL) → OrderCanonicalId(NOT NULL，跨库逻辑引用，无FK)
--   ▸ 原因：Order_Canonical 是 APS 防腐层核心表；ERP_Order_Staging 是同步过程层，不适合作为主链长期锚点
--   ▸ 字段改名：BOMNO → RequestedBOMNO（订单原始值）
--   ▸ 新增：OrderNo / SourceSystem / SourceOrderId（冗余/调试字段）
--   ▸ 唯一约束：UQ_BOMRequestDetail_BatchOrder → UQ_BOMRequestDetail_BatchCanonical(BatchNo, OrderCanonicalId)
--   ▸ SP d.BOMNO → d.RequestedBOMNO（sp_ExpandBOMBatch/sp_ExpandBOMRealtime_vNext共2处+旧SP1处）
--   新增 OrderBomRequestLink（APS本地，§2.2c）：
--   ▸ APS本地订单-BOM解析结果索引表；桥接 Order → BOM结构 / 大工艺路径
--   ▸ 关键字段：PlanVersionId/OrderId(NULL允许)/OrderCanonicalId/RequestDetailId/ResolvedBOMNO/RepWorksetId/LinkStatus
--   ▸ UNIQUE(PlanVersionId, OrderCanonicalId)（v5.0.34升级）；4个查询索引（Batch+Request / BOMNO / RepWorkset / PlanVersion+OrderId过滤索引）
--   ▸ RepWorksetId = MIN(Workset.Id) WHERE RequestDetailId+Level=1，与ROOT StageDetail规则一致
--   查询链路收敛：
--   ▸ Order→BOM结构：Order→OrderBomRequestLink.ResolvedBOMNO→APS_BOM_RAW(BatchNo+BOMNO)
--   ▸ Order→大工艺路径：Order→OrderBomRequestLink.RepWorksetId→APS_BOM_STAGE_PATH_RAW.WorksetId
--   ⚠️ APS_BOM_RAW 保持 BOMNO 级共享（不订单化）；APS_BOM_STAGE_PATH_RAW 无需改动
-- v5.0.30更新(2026-05-25 ROOT StageDetail追溯闭环):
--   sp_EnrichBOMWorkset Step 3：
--   ▸ ROOT生成粒度：BOMNO级 → BOMNO+RequestDetailId级（每个订单独立生成ROOT，多订单共享BOMNO时各有自己的ROOT）
--   ▸ ROOT.WorksetId：NULL → MIN(Workset.Id) WHERE BatchNo+BOMNO+RequestDetailId+Level=1（代表性锚点）
--   ▸ 语义：ROOT记录WorksetId不表示"由某条BOM边派生"，而是"归属于某RequestDetail下根产品展开结果"
--   ▸ 追溯链闭合：Order→RequestDetailId→WorksetId→StageDetail(ROOT+EDGE)
--   sp_EnrichBOMWorksetRealtime Step 3：同步补WorksetId（实时SP天然单RequestDetail，只补MIN(Id)）
--   ⚠️ 不改StageDetail表结构（RequestDetailId不进StageDetail，维持通过WorksetId反查的既有设计）
-- v5.0.29更新(2026-05-22 R29补R17工厂过滤+失败路径；R28补代理后仍空降级R29；对齐BOM_Workset_方案v1.8):
--   sp_ExpandBOMBatch_vNext Stage B/C：
--   ▸ R28：#R28ExportCodes由CTE改为临时表；代理后仍无出口库→降级走R29+R17
--         修正：降级判断从"有#R28ExportCodes"→"已写入#EntryCandidates"（有出口库码但BOM边不匹配时同样降级）
--   ▸ R29：加ProcessCodeDict.FactoryCode=@FactoryCode工厂过滤步骤；R17过滤后为空→BOM_ENTRY_NOT_FOUND
--   ▸ R30：静默条件精化为“RAW%+完全无BOM边”；有BOM边但无匹配工厂边→仍登记BOM_ENTRY_NOT_FOUND
--   sp_ExpandBOMRealtime_vNext：同步以上三点
--   sp_EnrichBOMWorkset + sp_EnrichBOMWorksetRealtime Step 3 ROOT路径：
--     修正：由仅取ParentProcRefCode → Unpivot(ChildProcRefCode+ParentProcRefCode)
--     原因：ASSY根产品ParentProcRefCode=出口库码（无StageCode），装配工序在ChildProcRefCode
--           仅取ParentProcRefCode导致全PURCHASE子件的ASSY产品ROOT StageDetail全空
-- v5.0.28更新(2026-05-21 BOM入口分流 R28/R29/R30/R31 + CN6课代理，对齐BOM_Workset_方案v1.7):
--   sp_ExpandBOMBatch_vNext Stage B/C 分流重构：
--   ▸ R28（SALES_ORDER+ASSY%+BOMNO IS NULL）：Step1查ProcessCodeDict出口库ProcessCode；
--     若工厂无出口库（当前仅CN6课），取母体工厂出口库（CN6课→CN，R10/§2.1隶属关系）
--     Step2 按ParentProcRefCode IN @ExportCodes过滤首层BOMNO候选（R17已隐式满足）
--   ▸ R29（SALES_ORDER+WIP%/RAW%+BOMNO IS NULL）：原Case B直查行为不变
--   ▸ R30（SALES_ORDER+RAW%+无BOM）：外购件兜底，静默跳过，不登记BOM_ENTRY_NOT_FOUND
--   ▸ R31（PRODUCTION_INSTRUCTION+BOMNO IS NULL）：直查同R29；必写Issues(BOMNO_MISSING_PRODUCTION,WARN找到/ERROR未找到)
--
-- v5.0.27更新(2026-05-16 订单提升链路重构，对齐《sp_ValidateAndPromoteOrders重构方案v3》)：
--   Schema 变更（不影响 BOM Workset v5.0.26d 任何内容）：
--   ▸ MaterialMapping: +1列 SourceModel NVARCHAR(100) NULL + IX_MaterialMapping_SourceModel 索引
--     用于 sp_ValidateAndPromoteOrders Step 0b Model→MaterialCode 解析链
--   ▸ ERP_Order_Staging: MaterialCode NOT NULL→NULL（SP Step 0 解析链写入前可为空）
--     +Model NVARCHAR(100) NULL（ERP原始型号透传）
--     +CustomerCode NVARCHAR(50) NULL（ERP原始客户代码，CustomerSegment派生用）
--     +RawNonStockShipmentType NVARCHAR(50) NULL（ERP原始非在库出荷区分）
--     +RawOrderSource NVARCHAR(50) NULL（ERP原始订单来源）
--   ▸ Order_Canonical: +SourceModel NVARCHAR(100) NULL（ERP原始型号追溯）
--     +NonStockShipmentType NVARCHAR(50) NULL（APS标准化：FULL_PURPLE_SLIP/DIFF_PURPLE_SLIP/UNKNOWN）
--     +OriginalOrderSource NVARCHAR(50) NULL（APS标准化：DAT/PO/UNKNOWN）
--   sp_ValidateAndPromoteOrders 全量重写（v5.0.27）：
--   ▸ 新增 #TargetStagingIds 临时表锁定本批次 ID 集合，防并发误操作
--   ▸ MaterialCode 三级解析链：SourceMasterID / SourceModel / EmergencyOverride
--   ▸ OrderType 未知值 → FAILED + ORDER_TYPE_UNKNOWN，禁止原始值写入 Canonical
--   ▸ BOMNO=NULL 非阻断：FailureCode=BOMNO_MISSING，订单仍提升；FailureCode记最高优先级，不互覆
--   ▸ CustomerSegment 口径：CustomerCode为空→NULL；CustomerCode有值但无匹配→UNKNOWN（不默认OVERSEAS）+ CUSTOMER_SEGMENT_UNKNOWN 追加 ErrorMessage
--   ▸ DemandMaturityStatus V1 严格 NULL：禁止从 OrderType/RAW字段/备注临时推断
--   ▸ FactoryCode V1 允许 NULL 进入 Canonical（TODO桩）
--   ▸ NonStockShipmentType/OriginalOrderSource inline CASE 标准化后写入 Canonical
--   ▸ @OnlyPending 参数删除（V1 只处理 PENDING）
--   ▸ MERGE UPDATE SET 补 CustomerCode；[Order]表+SourceModel/NonStockShipmentType/OriginalOrderSource
--   ▸ MaterialMapping JOIN 改为 mm.SourceID=stg.SourceMasterID（去掉CAST，两列均为INT）
--   ▸ CustomerCodeMap注释修正：废止"失效→OVERSEAS"旧口径，IsActive=0行JOIN过滤，无匹配→UNKNOWN
--   ▸ ERP_Order_Staging.FactoryCode NOT NULL→NULL（V1 TODO桩，允许空值透传；Canonical同为NULL允许）
--
-- v5.0.26d更新(2026-05-15 回填 SP + 实时展开 SP 正确性修订，Issue 1-5 + Realtime vNext 5项对齐)：
--   Realtime vNext 补丁（对齐批量链路 Case B）：
--     R1: #RT_EntryCandidates.CandidateBOMNO 改 NULL（允许纯物料路由 BOM 边 BOMNO=NULL）
--         #RT_EntryResolved 增 OriginalBOMNO(NULL 允许) + ResolvedBOMNO=ISNULL(OriginalBOMNO,'MAT:'+@MaterialCode)
--     R2: Step B3 拆两分支：OriginalBOMNO IS NOT NULL → INNER JOIN e.BOMNO；IS NULL → e.BOMNO IS NULL 精确过滤
--     R3: @RowsInserted 改 SELECT COUNT(*) FROM #RT_Expand WHERE Level=1（消除 DROP TABLE 后 @@ROWCOUNT=0 问题）
--     R4: MES_API_BOM_Request_Realtime INSERT BOMNO 改用 @ResolvedBOMNO（幂等保护键与写入键一致）
--     R5: @RowsInserted=0 时登记 BOM_ENTRY_NOT_FOUND Error Issue，避免 0 行展开却静默 READY
--   Issue 1: sp_EnrichBOMWorkset EDGE StageDetail INSERT 补 WorksetId=w.Id，去掉 SELECT DISTINCT
--   Issue 2: sp_EnrichBOMWorksetRealtime EDGE StageDetail_Realtime INSERT 同步修复（WorksetId + 去DISTINCT）
--   Issue 3: sp_ExpandBOMRealtime_vNext BOMNO=NULL 分支重构
--            旧：ROW_NUMBER PARTITION BY ParentMaterialCode Rank=1（只取1条L1边，丢失同入口其他子件）
--            新：#RT_EntryCandidates（DISTINCT BOMNO 按SourceSystem/IsDefaultVersion排名）
--                → #RT_EntryResolved（取CandidateRank=1入口）→ 抓全量L1边（INNER JOIN #RT_EntryResolved）
--                → 更新 @ResolvedBOMNO 为真实 CandidateBOMNO（Enrich SP 过滤键一致）
--            CATCH 块补 DROP TABLE IF EXISTS #RT_EntryCandidates / #RT_EntryResolved
--   Issue 4: sp_ExpandBOMRealtime_vNext RequestDetailId 反查 COALESCE(NULLIF(MaterialCode,''),NULLIF(Model,''))
--            用 @@ROWCOUNT 区分"行不存在"vs"找到但双空"；双空时登记 MISSING_MATERIALCODE Error 后 RETURN
--   Issue 5: vw_MES_BOM_Stage_Enriched FROM MES_BOM_View→MES_BOM_Edge_Active；
--            JOIN 字段 GoodsProcCode→ParentProcRefCode，MaterialProcCode→ChildProcRefCode；依赖注释更新
-- v5.0.26c更新(2026-05-15 sp_ExpandBOMBatch_vNext + 回填 SP 正确性修订，P1-P8八项)：
--   P1: sp_ExpandBOMBatch_vNext MES_APS_BOM_Workset_Issues 字段名修正（DetailId→RequestDetailId / Message→Detail）
--   P2: BOMNO NULL策略A：生成 ResolvedBOMNO='MAT:{MaterialCode}'，Workset.BOMNO NOT NULL 约束兼容
--   P3: #Request.MaterialCode 改 COALESCE(MaterialCode, Model)；双NULL行登记 MISSING_MATERIALCODE 不阻塞
--   P4: #EntryCandidates 改为按入口（DISTINCT CandidateBOMNO per DetailId）排名，非按边排名
--       Stage D L1 拆两分支：OriginalBOMNO IS NOT NULL / IS NULL（e.BOMNO IS NULL 精确过滤）
--   P5: sp_EnrichBOMWorkset + sp_EnrichBOMWorksetRealtime 全部 MES_BOM_View → MES_BOM_Edge_Active（8处）
--   P6: sp_ExpandBOMBatch_vNext 主体包 BEGIN TRY...END CATCH；CATCH 写 Status=FAILED + EXPAND_FAILED Issue + THROW
--   P7: sp_ExpandBOMBatch_vNext 幂等保护：同 BatchNo 重跑自动清理旧 Workset/StageDetail/Issues
--   P8: sp_ExpandBOMRealtime_vNext 引入 @ResolvedBOMNO（Strategy A）；Workset_Realtime BOMNO 写非空值
--       sp_EnrichBOMWorksetRealtime 增加 @RequestDetailId BIGINT=NULL 参数；传参路径对齐
-- v5.0.26更新(2026-05-14 BOM防腐层物化边表架构调整，对齐演变总表 v3.18)：
--   ▸ 新增 MES_BOM_Edge_Active（物化BOM防腐边表，V1兼任合同层+执行优化层）：
--     字段：BOMNO/ParentMaterialCode/ChildMaterialCode/Quantity/ParentProcRefCode/ChildProcRefCode
--           ChildSourceHintCode/SourceSystem/SourceBOMId/SourceLineNo/IsActive/IsDefaultVersion
--           EffectiveFrom/EffectiveTo/RefreshBatchNo/RefreshedAt/CreatedAt
--     索引：(ParentMaterialCode,IsActive) CLUSTERED / (BOMNO,ParentMaterialCode,IsActive) NONCLUSTERED
--           (ChildMaterialCode) NONCLUSTERED / (SourceSystem,SourceBOMId) NONCLUSTERED
--   ▸ 新增 MES_BOM_Edge_RefreshLog（刷新控制日志表）：
--     字段：RefreshBatchNo/RefreshType/Status/StartTime/EndTime/RowCount/ErrorMessage/CreatedAt
--     保护：刷新失败时 Status='FAILED'，Workset 展开前必须校验最新记录 Status='COMPLETED'
--   ▸ MES_APS_BOM_Workset_StageDetail / Archive / Realtime / APS_BOM_STAGE_PATH_RAW：
--     +1列 WorksetId BIGINT NULL（FK对应Workset表.Id；级联追溯锚点；NULL=兼容旧批次）
--     +1索引 IX_StageDetail_WorksetId(WorksetId)：用于sp_CleanupBOMWorkset级联清理
--   ▸ MES_APS_BOM_Workset_Realtime / Archive：
--     +1列 RequestDetailId BIGINT NULL（补齐与主批次 Workset 表的对称性）
--   ▸ sp_CleanupBOMWorkset：级联清理改为 DELETE StageDetail WHERE WorksetId IN (SELECT Id FROM Workset WHERE BatchNo=@BatchNo)
--   ▸ 【设计决策 V1/V2 边界】：V1 MES_BOM_Edge_Active 兼任合同层+执行优化层；
--     V2 视需要拆出 MES_BOM_Edge_Contract（多源历史+非活跃版本+裁决过程+审计追溯）
--   ▸ 【设计决策 StageDetail WorksetId】：5号位在 sp_EnrichBOMWorkset_vNext 中，
--     每条 Workset 行分别写 StageDetail（WorksetId=Workset.Id），同一 BOM 边出现 N 次写 N 组，
--     多路径收敛场景下每组 StageDetail 与具体 Workset 行一一对应；
--     RequestDetailId 不进 StageDetail，经 WorksetIdWorkset.RequestDetailId 反查。
---- v5.0.25更新(2026-05-13 排程运行编排+结果读模型+阶段二骨架，对齐总表 v3.17 / 防腐层 v1.20 / 字段说明 v5.0.25)：
--   ▸ 新增 ScheduleRun 表（运行编排主表）：RunType/Status/OutputPlanVersionId/ScenarioId/TriggeredBy
--   ▸ PlanVersion 追加 ScheduleRunId 列（ALTER TABLE）；Status 值域注释补 CANDIDATE
--   ▸ 新增 ScheduleExplanationFact 表（结构化原因事实，阶段一最小骨架，分区表）
--   ▸ 新增读模型三张表（阶段一即用）：
--     OrderScheduleSummary（订单级：DelayHours/RiskLevel/MainReasonCode/IsVipImpacted，分区）
--     ResourceLoadSummary（资源×日期：LoadHours/AvailableHours/LoadRate/IsBottleneck）
--     PlanKpiSummary（版本级：OnTimeRate/DelayedOrderCount/MaxDelayHours/VipDelayedCount/AvgLoadRate，分区）
--   ▸ 新增阶段二骨架三张表（预留，阶段一不实装）：
--     Scenario（what-if假设容器）/ SimulationRun（算法执行记录）/ ScenarioObjectiveScore（多目标评分）
--
-- v5.0.24更新(2026-05-13 OrderType重构+CustomerSegment来源澄清+DemandMaturityStatus收窄+DelayStatus新增+CustomerTier等级说明)：
--   ▸ OrderType 枚举值重分类：SO/MTO → SALES_ORDER；MTS/SS/SS_U → PRODUCTION_INSTRUCTION（三表注释+SP标准化步骤）
--   ▸ 新增 CustomerCodeMap 表（APS本地维护，用于 CustomerSegment 衍生；字段来源：CustomerCode.xlsx）
--   ▸ CustomerSegment 说明更新：通过 CustomerCode 查 CustomerCodeMap 本地映射表得到；非ODS共享字典
--     值域扩展为 JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER；失效客户统一给OVERSEAS+IsActive=0
--     ⚠️ v5.0.27修正：上述"失效→OVERSEAS"规则已废止，当前口径见 v5.0.27 changelog
--   ▸ DemandMaturityStatus 收窄为 PRE_CONFIRMED/FORECAST（去掉DELAYED；DELAYED拆出为独立字段）
--   ▸ 新增 DelayStatus（ERP_Order_Staging/Order_Canonical/Order三表）：ON_TIME/FIRST_DELAY/REPEATED_DELAY
--   ▸ CustomerTier 补充等级关系（VIP>KEY_ACCOUNT>STANDARD>GENERAL）及当前启用口径（VIP/GENERAL两档）
--   ▸ sp_ValidateAndPromoteOrders：补OrderType标准化步骤+CustomerSegment查CustomerCodeMap+DelayStatus推导
--
-- v5.0.23更新(2026-05-09 管道供给链 SupplyFact_Pipeline + SupplyAvailabilityRule)：
--   ▸ 新增 SupplyFact_Pipeline：APS 本地标准化供给事实层（允许少量本地派生字段）
--     并行于现货库存六层主链（InventoryBalance 定义不变）；当前来源：ERP_InterplantInTransit_View
--     SupplyType 将来按需扩展：INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / VMI_ONSITE 等
--     ETA = ODS 原始事实；AvailableTime = 本地派生（ETA + SupplyAvailabilityRule.LeadTimeOffset）
--     BatchNo nullable：夜间全量排程填 BatchNo 形成快照；白天实时 BatchNo=NULL 读最新 IsActive=1 记录
--   ▸ 新增 SupplyAvailabilityRule：供给主题独立规则表（不是统一规则引擎）
--     仅负责管道供给主题；现货链统一使用 InventoryAvailabilityRule（旧 ProductFamilyInventoryScope + InventorySourceRule 已于 v5.0.39 删除）
--     Filtered Unique Index（WHERE IsActive=1）防止完全重复规则，兼容软删除后重建
--   ▸ 【设计决策】InventoryBalance 定义不变；现货库存主链为六层结构（v5.0.40 新增第4层 InventoryAvailableSupplyDetail）
--   ▸ 【设计决策】SupplyFact_Pipeline 结果为空时不影响现有排程
--
-- v5.0.22更新(2026-05-09 MES_API_BOM_Request_Detail 补 OrderType 字段)：
--   ▸ MES_API_BOM_Request_Detail：新增 OrderType NVARCHAR(20) NULL
--     原因：BOMNO=NULL 时，BOM 入口解析规则因订单类型（生产指示 vs 客户订单）不同而不同；
--     5 号位需据此分支选取正确 BOM 条目；2 号位直接从 ERP_Order_Staging.OrderType 透传
--   ▸ 【设计决策】OrderType 不参与唯一约束，仅作为 5 号位入口解析的决策参数
--
-- v5.0.21更新(2026-05-08 订单BOM入口解析重构：request订单级粒度 + FailureCode/NextActionCode双维度 + Emergency临时桥接表)：
--   ▸ MES_API_BOM_Request_Detail：升级为订单级粒度；加 OrderStagingId/Model/MaterialCode/FactoryCode；BOMNO改可空；
--     废除 UQ_BOMRequestDetail_BatchBOMNO，改为 UQ_BOMRequestDetail_BatchOrder(BatchNo, OrderStagingId)
--   ▸ ERP_Order_Staging：BOMNO改可空（废除2026-04-03旧"必填"口径）；
--     新增 FailureCode（真实失败原因）+ NextActionCode（后续动作/分流）两个独立维度字段
--   ▸ MES_APS_BOM_Workset：新增 RequestDetailId（来源追溯锚点，nullable，非业务键，1号位主链不消费）
--   ▸ MES_APS_BOM_Workset_Issues：新增 RequestDetailId（追溯锚点）
--   ▸ 新增3张急单临时桥接表：
--     OrderEmergencyMaterialOverride（临时 Material 补齐）
--     OrderEmergencyBomWorkset（人工友好版临时 Workset 主层，最小字段）
--     OrderEmergencyBomStageDetail（人工友好版临时 StageDetail 路径层，最小字段）
--   ▸ 【设计决策】BOM入口解析业务分流归属5号位Workset处理阶段，2号位只做最基础字段透传
--   ▸ 【设计决策】FailureCode=失败原因维度；NextActionCode=后续动作维度；两者独立，禁止混用
--   ▸ 【设计决策】Emergency表为中介临时桥接数据，留痕可追溯，不替代夜间正式同步数据
--
-- v5.0.20更新(2026-05-06 StageDict 阶段细化拆分 + PAINT→SURF 重命名)：
--   ▸ StageDict：新增 5 种 StageCategory（MOLD/CAST/DRAW/FORGE/EXTRU）+ 7 条 StageCode（22→29 条）
--     PAINT→SURF 重命名：BJ_PAINT→BJ_SURF / CN_PAINT→CN_SURF / TJ_PAINT→TJ_SURF（表面处理 = 涂装+氧化+喷丸）
--     BJ_MOLD(注塑) / BJ_CAST(铸造) / BJ_DRAW(冷拔) / BJ_FORGE(锻造) / BJ_EXTRU(型材押出) / TJ_CAST(铸造) / CN_FORGE(锻造)
--   ▸ ProcessCodeDict：24 条 StageCode 改映射（按 Process 列关键词自动推断）
--     BJ_MACH→20: 注塑×7→BJ_MOLD / 铸造×10→BJ_CAST / 锻造×1→BJ_FORGE / 型材×2→BJ_EXTRU / 冷拔×0(BJ_DRAW 暂无)
--     TJ_MACH→13: 铸造×2→TJ_CAST
--     CN_MACH→11: 锻造×2→CN_FORGE
--   ▸ 未拆分暂留：TJ型材(340198/340199/341198)→TJ_MACH；CN拉拔(510899)→CN_MACH
--
-- v5.0.19更新(2026-05-06 StageDict/ProcessCodeDict 业务数据初始化)：
--   ▸ StageDict：基于工序对照表.xlsx 重建 seed data，5 厂 × 5 阶段类别 = 22 条
--     新增：BJ_OUTS/BJ_FINAL/CN6_OUTS/TJ_SURF/TJ_FINAL/SH_OUTS/SH_FINAL
--     移除：JP_*（无业务数据）/CN_CLEAN/CN_INSP（无对应 ProcessCode）
--   ▸ ProcessCodeDict：首次录入 152 条初始化数据（来源：工序对照表.xlsx，155 行去重 3 行重复码）
--     字段映射：ProcessCode(6位补0) / ProcessName=Description / FactoryCode=代码所属工厂
--     StageCode 映射规则：Process→StageCategory + FactoryCode→前缀（CN6课→CN6_）
--     重复码处理：010593/020593/070693 各有两条（原工序 vs 受托库），取第一条入表
--
-- v5.0.18更新(2026-05-04 sp_EnrichBOMWorkset 完整实现)：
--   ▸ 新增 sp_EnrichBOMWorkset（§3.4，~270行）：5号位核心 BOM 回填存储过程
--     实现内容：R17 工厂映射(FIXED/INHERIT/NONE) → R24 原生序阶段链 → R26 受托隔离 → R25 异厂回退
--     产出：ChildRequiredFactory / ChildRequiredStageCode 回填 + StageDetail EDGE+ROOT 写入 + Issues 降级登记
--   ▸ sp_ExpandBOMBatch：TODO 注释替换为 EXEC sp_EnrichBOMWorkset @BatchNo 调用
--   ▸ 新增 sp_EnrichBOMWorksetRealtime（§3.5）：实时链路回填，操作 _Realtime 表，Issues.BatchNo 正式路径=RT:RD:{RequestDetailId}（v5.1.0），RT:{ResolvedBOMNO}仅deprecated兼容
--   ▸ sp_ExpandBOMRealtime：TODO 替换为 EXEC sp_EnrichBOMWorksetRealtime @BOMNO 调用
--   ▸ 设计依据：《BOM_Workset_生成与错误处理技术方案_v1.0》§3 + _workset_excel.py 参考实现
--   ▸ SQL Server 兼容性：≥2016（DROP TABLE IF EXISTS / TRY_CAST / FOR XML PATH）
--
-- v5.0.17更新(2026-05-03 Factory JOIN 列名修正)：
--   ⚠️ Bug 修复：sp_SyncResourceData / sp_SyncOrdersToPartitionTable 两处 LEFT JOIN Factory 误用 f.FactoryCode，实际列名为 f.Code（Factory 表字段定义为 Code NVARCHAR(50)）
--   影响范围：两个 SP 在生产环境执行时会因列名不存在而报错
--
-- v5.0.16更新(2026-04-29 生产部门主链 + ProcessCodeDict 重定位 + WorkshopCode 全局清理)：
--   ▸ 业务事实定调：部门=「物料×阶段」联合属性（不进 StageDict、不进 StageDetail）；MSC 是原始仓库级上下文，不直接作为 1 号位入口
--   ▸ 1 号位排程主链升级：(MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套
--   ▸ ProcessCodeDict 定位**翻转**：v5.0.15 定的「ERP 镜像/缓存」错位，本版**改为「APS 自维护的 ODS 增强工序字典」**
--      • 删除 LastSyncedAt（无自动同步）；RENAME SourceSystem → CodeOrigin（值域 ERP/MES/MANUAL）；ADD StageCode 列（APS 增强，软引用 StageDict）+ UpdatedBy
--      • 维护方：APS 系统管理员 + 0 号位审批；不参与 sp_SyncMasterData 自动同步流程
--      • MES_ProcessCode_View 同步暴露 StageCode + CodeOrigin
--   ▸ ProcessCode→StageCode 基础映射全链路共享：5 号位 sp_EnrichBOMWorkset、2 号位 Context 组装统一查 MES_ProcessCode_View.StageCode（不各写映射）
--   - 新增 §2.4b ProductionDepartment（APS_Production 库；APS 排程责任部门字典；DeptCode/DeptName/FactoryId/StageCode 单值/IsActive；不做组织树/不接审批）
--     业务规则：一个 ProductionDepartment 只归属一个 StageCode（业务确认 1:1）；StageCode 必须取自 StageDict
--   - 新增 §2.4c MaterialStageDeptOverride（APS 库；人工维护表；Model/MaterialCode + StageCode + ProductionDeptCode）
--     导入时强制 Model→MaterialCode 1:1 检查；1:N 拒收返回明细，避免误覆盖整串规格
--   - 新增 §2.4d MaterialStageDeptContext（APS 库；2 号位组装的正式消费表；键 (MaterialId, StageCode) → DefaultProductionDepartmentId；SCD Type 2）
--     1 号位排程消费入口；同时点同 (MaterialId, StageCode) 只能有 1 条 IsCurrent=1
--   - 新增 §2.4e MaterialStageDeptContext_Issues（降级登记；旧值不动、新问题登记，待人工修正后局部重建）
--   - 新增 sp_RebuildMaterialStageDeptContext（三触发：每日定时全量、MSC 同步后增量、人工 Override 提交后局部）
--   - ALTER MaterialSupplyContext: ADD DefaultProductionDepartmentId（与 DefaultProductionDeptCode 双轨，FK 规范化）
--   - ALTER Resource: DROP WorkshopCode（业务确认 MES 也无此概念）+ ADD ProductionDepartmentId NOT NULL（FK） + ADD SourceProductionDeptCode（审计）
--   - ALTER RoutingOperation/RoutingDependency/OperationResourceEligibility: ADD ProductionDepartmentId NOT NULL FK；UQ 升级三元组
--      • UQ_RoutingOperation: (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode)
--      • UQ_RoutingDep:       (MaterialId, ProductionDepartmentId, RouteCode, PathId, FromOperationCode, ToOperationCode)
--      • UQ_OpResElig:        (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, ResourceId)
--   - sp_SyncResourceData: WorkshopCode → ProductionDeptCode（ext_MES_APS_Resource_View 字段契约升级，由 DBA 同步改契约视图）；MERGE 加 ProductionDepartmentId 映射 JOIN ProductionDepartment
--   - StageLeadTimeParam: WorkshopCode → ProductionDeptCode（口径统一；APS 自定义命中维度，纯字符串无 FK）
--   - sp_SyncMasterData: @SourceType 枚举不包含 ProcessCode（v5.0.46起ERPProperty由5号位同步，整表自动同步不恢复）
--   【设计决策】部门维度只在 APS_Production 主链注入；StageDetail/StageDict 永远不动
--   【设计决策】R20 跨组织视角零特殊逻辑——StageCode 已采目标工厂视角，按 (MaterialId, StageCode) 查 Context 天然得到目标工厂部门
--   【设计决策】NULL 处理放弃：业务确认 MES 工艺数据全部带部门，Routing 三件套 ProductionDepartmentId NOT NULL，不引入 _UNSPECIFIED 哨兵
--   【升级策略】现有环境：① 建 ProductionDepartment + 3 张新 Context 表 ② ALTER ProcessCodeDict（DROP LastSyncedAt + RENAME SourceSystem→CodeOrigin + ADD StageCode/UpdatedBy）③ ALTER MSC ADD ④ ALTER Resource (DROP WorkshopCode + ADD ProductionDepartmentId/SourceProductionDeptCode) ⑤ ALTER Routing 三件套（数据回填 ProductionDepartmentId 后改 UQ） ⑥ MES_ProcessCode_View 重建 ⑦ sp_SyncResourceData 升级 ⑧ 4 个 ODS 契约视图字段升级（走审批，DBA 执行）
--
-- v5.0.15更新(2026-04-28 ProcessCode 防腐三件套 + StageDict 字段净化)：
--   ▸ 设计原则：ProcessCode（6 位 ERP 工序码）是 ERP 易变字段，严格只在 ODS 层活；APS_Production 库永不出现此类字段
--   ▸ 原则：两本字典职责分离——StageDict=APS 大工艺阶段字典（APS 自主语义）；ProcessCodeDict=ERP 工序对照表的 ODS 物理镜像（ERP 特征字段）
--   ▸ 原则：字典只承载"阶段自身属性"；"物料×阶段"联合决定的属性（是否入库/入库角色/LeadTime）一律放到 RoutingStage / StageDetail / StageLeadTimeParam
--   - 新增 §1.9e ProcessCodeDict（ODS 物理表，ERP 工序对照表的 ODS 镜像；由 sp_SyncMasterData(@SourceType='ProcessCode') 同步；位于 MES_Integration）
--     字段：ProcessCode (PK, NVARCHAR(20), 6 位左补 0) / ProcessName / FactoryCode（代码所属工厂）/ ActualFactoryCode（实际生产工厂）
--          / TrusteeProcCode（受托对方工艺）/ IsOutsource / IsRetouch（是否追加工）/ WarehouseRole（仓库角色）
--          / SourceSystem / LastSyncedAt / IsActive
--     消费边界：仅 ODS 内部消费（5号位Stage/跨厂事实推导、运维诊断）；V1不生成ShippingTask；APS_Production永不直接查此表
--   - 新增 §1.9f MES_ProcessCode_View（ODS 契约视图，Socket-Plug，主键 ProcessCode）
--     吸震点：ERP 升级导致工序对照表字段改名时，改此视图 SELECT 别名即可；消费方零改动
--     同步物理表 SP 占位：sp_SyncMasterData @SourceType='ProcessCode' 分支（v1 骨架，正式实现由 CDC/全量同步专项立项）
--   - 重写 §1.10 vw_MES_BOM_Stage_Enriched：
--     ⚠️ 原 v5.0.10 设计的 JOIN 键 sd.StageCode = pc.StageCode 无法成立（StageDetail 是聚合大阶段，工序对照是 6 位 ProcessCode，值域不同 + 1:N 关系）
--     新设计：视图来源改为 BOM 边粒度——MES_BOM_View × MES_ProcessCode_View（按 GoodsProcCode JOIN ProcessCode 派生工厂/实际生产厂/受托工艺等）
--     可选附带 StageDetail 聚合 StageCode（通过 BatchNo+BOMNO+Parent+Child 四元组 LEFT JOIN）
--   - 修改 §1.9c StageDict：删除 IsStockPoint 列（错位——物料×阶段属性不该放字典）+ 删除 IsOutsource 列（与 StageCategory='OUTS' 语义重复）
--     初始化数据对应调整（19 行保持不变，仅字段瘦身）；索引 IX_StageDict_Factory/Category 保留
--   - 修改 §1.9d ProcessTypeDict 注释："StageDict.IsOutsource=1" → "StageDict.StageCategory='OUTS'"（同步字段移除）
--   【设计决策】APS 排程只消费"语义字段"：StageCode → StageDict JOIN 拿 FactoryCode / StageCategory；其他 ERP 特征字段走 ODS 内部视图，不下沉 APS
--   【设计决策】IsStockPoint 语义分层承接：字典级删除 → 物料×阶段级走 RoutingStage.IsStockPoint（如需，3 号位维护）→ BOM 边级走 StageDetail.IsSupplyThreshold（已存在，5 号位回填）
--   【升级策略】现有环境需：① 建 ProcessCodeDict 表 + 契约视图 ② ALTER TABLE StageDict DROP COLUMN IsStockPoint, IsOutsource ③ 重建 vw_MES_BOM_Stage_Enriched
--
-- v5.0.14更新(2026-04-28 ProduceToFactoryMap 崩溃修复：以用户提供的 Produce 权威照片为准纠正 5 处错误映射)：
--   ⚠️ 严重 Bug修复·1：§1.9b INSERT 数据中 Produce=7 的 TargetFactory 由 'TJ' 纠正为 'CN'（CN 中国公司内制他用）
--   ⚠️ 严重 Bug修复·2：§1.9b INSERT 数据中 Produce=11 的 TargetFactory 由 'SH' 纠正为 'TJ'（TJ 天津工厂内制他用）
--   ⚠️ 分类体系收敛：SourceCategory 取消自创的 INHOUSE_SPECIAL；Produce=5/8/9 从“内制·特注”纠正为“内制·自用”
--     值域由 4 类收敛为 3 类：PURCHASE / INHOUSE_SELF / INHOUSE_CROSS（正交于 R07/R20 业务约束）
--   ✅ ProduceName 按照片重写：0/3 保税；2/4/10 课税；1/5/8/9 内制自用；6/7/11 内制他用
--   ✅ 同步修改 §1.9b CREATE TABLE 的 SourceCategory 注释、INSERT 语句中的 R20 记录 Description、表头注释
--   【设计决策】SourceCategory 不处理照片的 “购入(保税/课税)” 细分（ERP 内部口径、对 APS 排程透明）；保税/课税语义保留在 Description 列
--   【升级策略】现有环境需 UPDATE ProduceToFactoryMap WHERE ProduceCode IN (5,7,8,9,11) 手工补丁；详见 README/迁移指南
--
-- v5.0.13更新(2026-04-25 资源 ODS 契约视图命名统一 + sp_SyncResourceData 占位 SP)：
--   ▸ 命名统一：历史遗留的 APS_Resource_View / ext_APS_Resource_View（按消费方命名）
--     与 MES_BOM_View / ERP_Master_View / MES_APS_Routing_*_View 系列（按来源命名）不一致
--     本版统一为 MES_APS_Resource_View / ext_MES_APS_Resource_View（与 Routing 系列完全对齐）
--     未来 EAM 上线时自然扩展为 EAM_APS_Resource_View / ext_EAM_APS_Resource_View 双源并存
--   ▸ 新增资源同步统一出口 sp_SyncResourceData(@SourceType)（占位 SP，v1 仅 MES 分支，EAM 分支预留）
--     职责定位：与 sp_SyncMasterData(@SourceType) 同构——双源视图契约一致时 SP 逻辑零分叉
--     数据流：ext_MES_APS_Resource_View ／ ext_EAM_APS_Resource_View → Resource 表（全量刷新）
--     调用时机：每天 00:10（v5.0.13.1 对齐走查 V3.4：与 sp_SyncMasterData 同窗口并行，二者同属外部主数据镜像且执行时间秒级）
--   - 重命名 APS_Resource_View → MES_APS_Resource_View（ODS 库，MES DBA 负责创建）
--   - 重命名 ext_APS_Resource_View → ext_MES_APS_Resource_View（APS 库跨库包装视图，2 号位负责创建）
--   - 新增 CREATE OR ALTER PROCEDURE sp_SyncResourceData @SourceType ∈ {'MES','EAM'}
--     v1 占位实现：仅 'MES' 分支走 MERGE ext_MES_APS_Resource_View → Resource；'EAM' 分支 RAISERROR 'NOT_IMPLEMENTED'
--     字段映射（v5.0.16 升级）：ResourceCode/ResourceName/ExternalResourceId/SourceSystem/FactoryId/ProductionDepartmentId/SourceProductionDeptCode/ResourceType/Status/CapacityFactor/IsActive
--     FactoryId / ProductionDepartmentId 双映射：均查字典表，找不到时登记 APS_ETL_Log 并跳过该行（不阻塞批次）
--     v5.0.16 弃用：WorkshopCode 字段（ext_MES_APS_Resource_View 契约对应改为 ProductionDeptCode）
--   - 【设计决策】命名口径永久统一：ODS 契约视图按"源系统_消费方_实体_View"三段式（单源可省略消费方）
--   - 【设计决策】sp_SyncResourceData 暂不三表协同（Resource 侧没有对称的 ResourceMapping/ResourceSupplyContext），保留将来扩展空间
--
-- v5.0.12更新(2026-04-24 工艺数据三层模型收敛 + Stage 顺序唯一权威)：
--   ▸ 三层分层模型确立：OperationName/OperationCode（具体工序）/ ProcessType（辅助分类标签，不参与排程对接）/ StageCode（BOM↔Routing 对接的业务大工艺码）
--   ▸ BOM↔Routing 对接主键 = (MaterialCode, StageCode)；1 号位按此二元组从 RoutingOperation 取小工序生成 Task
--   ▸ StageSeq 权威源唯一化：**StageDetail.StageSeq 是唯一权威**；RoutingStage.StageSeq 删除
--   ▸ R20 跨组织交接视角统一：BOM StageDetail.StageCode 采用**他用方视角**（如 R20 到 BJ 工厂时 StageCode 直接写 BJ_MACH），1 号位消费时无需再做跨厂翻译
--   - 删除 RoutingStage.StageSeq 字段 + 同步删除 IX_RoutingStage_Seq 索引
--     理由：跨物料/跨根产品场景下 MES 工艺侧给不出正确的跨大工艺顺序号；保留会造成误用
--     唯一影响面：任何下游代码若读取 RoutingStage.StageSeq 视为 bug（应改读 StageDetail.StageSeq）
--   - RoutingOperation.StageCode 约束加强：必须取值自 StageDict（业务规则，DDL 层建议加 CHECK 或 FK）
--     MES 本地阶段叫法若与 StageDict 不一致，由契约视图 MES_APS_Routing_Stage_View 负责映射标准化，MES 原生字符串不得直接下沉
--   - RoutingOperation.ProcessType 定位澄清：**辅助分类标签**，不参与 BOM↔Routing 对接，不作为 1 号位排程主键；
--     值域不再硬编码枚举，改为配置表驱动（新增 ProcessTypeDict，暂置 IsActive=0 的预留骨架，业务未使用时不加 CHECK 约束）
--   - 新增 ProcessTypeDict（工序级分类标签字典；暂为预留骨架，业务启用时扩充）
--     字段：ProcessType (PK, NVARCHAR(50)) / ProcessTypeName / Category / Description / IsActive / UpdatedAt
--     初始化数据：MACHINING / ASSEMBLY / INSPECTION / OUTSOURCE / PAINT / CLEANING（6 条占位，IsActive=0）
--     用途：报表、粗分组、统计；**不参与 BOM↔Routing 对接，不作为排程主键**
--   - StageDict 说明升级：支持 R20 跨组织交接场景——BOM 侧 StageDetail.StageCode 采用"目标工厂视角"，
--     例如父件在 TJ、R20 指派给 BJ，则 StageCode = BJ_MACH（不是 TJ_MACH），1 号位读到直接去 BJ 的 Routing 找小工序
--   - StageDetail.StageCode 注释更新：R20 视角语义 + 引用 StageDict
--   - OperationCode **不**引入全局字典（MES 侧不可控 + 新增频繁）；仅在同厂内部 Routing 消费，跨厂对接通过 StageCode 完成
--
-- v5.0.11更新(2026-04-24 规则资产化 + 批次永不阻塞降级策略)：
--   ▸ 设计哲学升级：防腐层只做"吸震 + 登记"，不做"生产准入判断"；批次永不因数据质量阻塞
--   ▸ 规则资产化：R17 Produce→厂映射从代码/文档硬编码 → 落独立配置表 ProduceToFactoryMap
--   ▸ StageCode 全局字典：RoutingStage 是物料级阶段配置，StageCode 本身没有全局字典；本版补齐 StageDict
--   - 新增 ProduceToFactoryMap（R17 规则资产化配置表）
--     字段：ProduceCode (PK, TINYINT, 0-11) / ProduceName / SourceCategory (PURCHASE|INHOUSE|INHOUSE_SPECIAL|INHOUSE_CROSS)
--          / FactoryStrategy (INHERIT|FIXED|NONE) / TargetFactory / Description / IsActive / UpdatedAt
--     初始化数据：12 行（Produce=0..11 全量）
--     用途：5号位 sp_EnrichBOMWorkset 查此表推导 ChildRequiredFactory，ERP 语义变化时改配置不改代码
--   - 新增 StageDict（StageCode 全局字典表，方案 B：工厂+阶段码）
--     字段：StageCode (PK, NVARCHAR(20)) / StageName / FactoryCode / StageCategory (MACH|OUTS|ASSY|PAINT|INSP|CLEAN|FINAL)
--          / IsOutsource / IsStockPoint / SortHint / IsActive / Description / UpdatedAt
--     初始化数据：~20 行草案（CN_MACH/CN_OUTS/CN6_MACH/BJ_MACH/TJ_MACH/SH_MACH 等，待业务审定扩充）
--     定位：StageCode 业务语言层的权威字典；RoutingStage(物料级)/StageDetail(BOM派生)/StageLeadTimeParam 的 StageCode 均引用此表
--   - MES_APS_BOM_Workset_Issues: Severity 字段注释订正——"仅为事后处置优先级，不决定批次 READY"
--   - sp_ExpandBOMBatch / sp_ExpandBOMRealtime 注释更新：
--     · 移除"Severity IN ('ERROR','CRITICAL') 阻塞批次"——改为"所有异常降级 + 登记，永不阻塞"
--     · CYCLIC_BOM 策略明确：首次访问保留 + 重复循环访问跳过（visited 集防环）
--     · 状态机简化：PENDING → EXPANDED → ENRICHED → READY（去掉数据质量 FAILED 分支；FAILED 仅保留给 SP 进程崩溃）
--   - 【设计决策】防腐层 3 条永久红线：(1) 规则可配置化的走配置表（ProduceToFactoryMap）；(2) 规则非配置化的走 SP 版本号字段 + 单元测试集；(3) vw_* 仅 ODS 内部，用数据库 GRANT 做技术阻断
--   - 【设计决策】CYCLIC_BOM 保留首次 + 跳过重复：既保证批次跑完，又保留已发现的父子边数据
--   - 【设计决策】运营 SLA：Severity 对应处置优先级——INFO 不关注；WARN 月度巡检（业务复核人员）；ERROR 次日晨会；CRITICAL 追责 SP 本身（数据层面永不阻塞）
--   - 【设计决策】Issues 统计驱动 ERP 源端数据质量改进：FACTORY_MISMATCH/NO_STAGE 积累阈值后反馈 ERP 维护方修正
-- =============================================
--
-- ⚠️⚠️⚠️ 【DBA 部署前必读】 ⚠️⚠️⚠️
-- 
-- 本脚本为架构定稿模板。实际执行前，DBA 需根据生产环境真实拓扑，完成以下配置：
--
-- 1. 【物理路径全局替换】
--    - 搜索并替换所有 "E:\SSD\" 为实际生产环境的SSD路径
--    - 搜索并替换所有 "D:\HDD\" 为实际生产环境的机械硬盘路径
--    - 确保路径存在且SQL Server服务账号具有读写权限
--
-- 2. 【跨库/跨实例视图配置】
--    - 本脚本使用 ext_ 前缀包装视图（如 ext_ERP_Master_View、ext_MES_Material_View）
--    - 这些视图指向 ODS 库（MES_Integration）的防腐契约视图
--    - 根据生产环境拓扑，选择以下配置方式之一：
--
--      a) 同实例跨库（推荐）：
--         CREATE VIEW ext_ERP_Master_View AS 
--         SELECT * FROM [MES_Integration].[dbo].[ERP_Master_View];
--
--      b) 跨实例 Linked Server：
--         CREATE VIEW ext_ERP_Master_View AS 
--         SELECT * FROM [LinkedServerName].[MES_Integration].[dbo].[ERP_Master_View];
--
--      c) 使用 Synonym（可选）：
--         CREATE SYNONYM ext_ERP_Master_View 
--         FOR [MES_Integration].[dbo].[ERP_Master_View];
--
-- 3. 【权限配置】
--    - 确保 APS_Production 库的执行账号对 MES_Integration 库有 SELECT 权限
--    - 如使用 Linked Server，需配置跨实例安全映射
--
-- 4. 【分区扩展】
--    - 分区函数 PF_PlanVersion 当前支持 100 个版本
--    - 生产环境需扩展到 400 个版本（见第 234 行注释）
--
-- 5. 【索引优化】
--    - 脚本中的索引为基础配置
--    - 上线后根据实际查询计划，按需添加覆盖索引或调整填充因子
--
-- ⚠️ 部署顺序：先执行 ODS 库（第一部分），再执行 APS 库（第二部分），最后执行存储过程（第三、四部分）
--
-- =============================================

-- =============================================
-- 第一部分: MES_Integration ODS库（防腐层）
-- =============================================

-- 创建ODS库
CREATE DATABASE MES_Integration
ON PRIMARY 
(
    NAME = MES_Integration_Data,
    FILENAME = 'E:\SSD\MES_Integration.mdf',  -- ⚠️ SSD路径，高频I/O
    SIZE = 5GB,
    MAXSIZE = UNLIMITED,
    FILEGROWTH = 1GB
)
LOG ON 
(
    NAME = MES_Integration_Log,
    FILENAME = 'E:\SSD\MES_Integration_log.ldf',  -- ⚠️ SSD路径
    SIZE = 2GB,
    MAXSIZE = 20GB,
    FILEGROWTH = 512MB
);
GO

USE MES_Integration;
GO

-- 启用 Snapshot Isolation
ALTER DATABASE MES_Integration SET READ_COMMITTED_SNAPSHOT ON;
ALTER DATABASE MES_Integration SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

-- =============================================
-- 1.1 BOM展开请求表（批次）
-- =============================================

CREATE TABLE MES_API_BOM_Request (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL UNIQUE,
    Status NVARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING/PROCESSING/READY/CONSUMED/FAILED
    RootCount INT NOT NULL,                          -- 活跃根数量（如80万）
    ExpandedRowCount INT NULL,                       -- 展开后行数（如350万）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    ProcessingStartTime DATETIME2 NULL,
    CompletedAt DATETIME2 NULL,
    ProcessingDuration INT NULL,                     -- 处理耗时（秒）
    RetryCount INT NOT NULL DEFAULT 0,
    ErrorMessage NVARCHAR(MAX) NULL,
    TriggeredByUserId INT NULL,                      -- ⚠️ v2.6新增：触发批次的用户ID（关联APS_Auth.User）
    TriggeredByUserName NVARCHAR(100) NULL           -- ⚠️ v2.6新增：触发批次的用户名（冗余）
);
GO

CREATE INDEX IX_BOMRequest_Status ON MES_API_BOM_Request(Status, CreatedAt);
CREATE INDEX IX_BOMRequest_Completed ON MES_API_BOM_Request(CompletedAt DESC);
GO

-- =============================================
-- 1.2 BOM展开请求明细表（v5.0.21 升级为订单级粒度；v5.0.22 补 OrderType；v5.0.31 锚点升级至 OrderCanonicalId）
-- =============================================
-- v5.0.21变更：从 BOMNO 粒度升级为订单/记录粒度
-- v5.0.31变更：锚点升级 OrderStagingId → OrderCanonicalId；BOMNO字段改名为 RequestedBOMNO
-- v5.0.32变更：字段进一步收敛，本表定位为 BOM 请求输入表
--   删除 Model（5号位不再依赖 ERP 原始 Model；保留在 ERP_Order_Staging.Model / Order_Canonical.SourceModel）
--   删除 OrderStagingId（ERP_Order_Staging 仅作同步缓冲层；BOM 主链锚点确定为 OrderCanonicalId）
--   删除 ResolvedBOMNO（Workset 解析结果归 OrderBomRequestLink；5号位只写 Workset/StageDetail/Issues）
-- 【设计决策】本表只保留请求输入：OrderCanonicalId + MaterialCode + FactoryCode + OrderType + RequestedBOMNO
-- 【设计决策】RequestDetailId（本表 Id）作为 MES_APS_BOM_Workset / Issues 的来源追溯锚点
-- 【设计决策】ResolvedBOMNO 由 2号位在 Workset 同步完成后写入 OrderBomRequestLink.ResolvedBOMNO

CREATE TABLE MES_API_BOM_Request_Detail (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    OrderCanonicalId BIGINT NOT NULL,            -- v5.0.31 核心锚点；逻辑引用 Order_Canonical.Id（跨库，不加FK约束）
    OrderNo NVARCHAR(100) NULL,                  -- v5.0.31 订单号（冗余，便于 ODS 侧排查/日志/人工核对）
    SourceSystem NVARCHAR(20) NULL,              -- v5.0.31 来源系统（'ERP'/'MES'）
    SourceOrderId NVARCHAR(100) NULL,            -- v5.0.31 来源系统订单ID（ERP订单编号或MES内部ID）
    MaterialCode NVARCHAR(100) NULL,             -- v5.0.21 物料编码（5号位 BOM 入口解析主键）
    FactoryCode NVARCHAR(50) NULL,               -- v5.0.21 工厂编码（5 号位按厂分流）
    OrderType NVARCHAR(20) NULL,                 -- v5.0.22 订单类型；值域：SALES_ORDER/PRODUCTION_INSTRUCTION（v5.0.24重分类）；RequestedBOMNO=NULL 时 5 号位据此选取 BOM 入口规则
    RequestedBOMNO NVARCHAR(50) NULL,            -- v5.0.31 订单原始携带的 BOMNO（请求输入字段，可空=待 5 号位解析）；原字段名 BOMNO
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),

    FOREIGN KEY (BatchNo) REFERENCES MES_API_BOM_Request(BatchNo),

    -- v5.0.31 新唯一约束：同批次内每个 Order_Canonical 最多一条明细（替换旧 BatchOrder 约束）
    CONSTRAINT UQ_BOMRequestDetail_BatchCanonical UNIQUE (BatchNo, OrderCanonicalId)
);
GO

CREATE CLUSTERED INDEX IX_BOMRequestDetail_Batch
ON MES_API_BOM_Request_Detail(BatchNo, OrderCanonicalId);   -- v5.0.31 改用 OrderCanonicalId
GO

-- =============================================
-- 1.2b BOM物化防腐边表（v5.0.26新增）
-- =============================================
-- 定位：V1兼任 BOM 防腐合同层 + 执行优化层
--   合同层职责：承接 ERP/MES 多源 BOM 原始数据；标准化字段名；执行 ProcessCode 左补零；
--     标准化 ChildSourceHintCode 值域（0-11）；生成 SourceSystem/SourceBOMId/SourceLineNo 追溯；
--     完成双源唯一默认版本裁决（IsDefaultVersion=1 全局唯一）；屏蔽源表结构变化。
--   执行优化层职责：只保留当前有效可展开 BOM 边；建立面向迭代展开的索引；
--     避免 sp_ExpandBOMBatch_vNext 每层展开都扫描 7-8 张源表 UNION 复杂 View。
-- 兼容视图：MES_BOM_View = SELECT * FROM MES_BOM_Edge_Active（兼容旧查询/文档引用）
-- V2预留：届时可拆出 MES_BOM_Edge_Contract（保留非活跃版本/历史/裁决过程/审计追溯）
-- 刷新控制：展开前校验 MES_BOM_Edge_RefreshLog 最新记录 Status='COMPLETED'；半刷新禁止 Workset 消费
-- 数据库：MES_Integration（ODS库）
-- =============================================

CREATE TABLE MES_BOM_Edge_Active (
    Id               BIGINT          PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BOMNO            NVARCHAR(50)    NULL,                   -- 订单显式 BOMNO；NULL=纯物料路由 BOM 边
    ParentMaterialCode NVARCHAR(50)  NOT NULL,               -- 父件物料编码（标准化后）
    ChildMaterialCode  NVARCHAR(50)  NOT NULL,               -- 子件物料编码（标准化后）
    Quantity         DECIMAL(18,6)   NOT NULL,               -- 单位用量（生产1父件需几子件），不累乘
    ParentProcRefCode  NVARCHAR(50)  NULL,                   -- 父件工序参考码（原 ERP BOM 辅助字段）
    ChildProcRefCode   NVARCHAR(50)  NULL,                   -- 子件工序参考码（同上）
    ChildSourceHintCode NVARCHAR(50) NULL,                   -- 子件来源提示码（0-11 编码，原 ERP produce 字段标准化后）
    SourceSystem     NVARCHAR(20)    NOT NULL,               -- 边的数据来源：'ERP'/'MES'
    SourceBOMId      NVARCHAR(100)   NULL,                   -- 源系统 BOM 物理主键（追溯用）
    SourceLineNo     NVARCHAR(50)    NULL,                   -- 源系统行号（追溯用）
    IsActive         BIT             NOT NULL DEFAULT 1,     -- 是否有效（刷新时仅保留 IsActive=1 行）
    IsDefaultVersion BIT             NOT NULL DEFAULT 0,     -- 是否为双源裁决后的唯一默认版本（全局唯一）
    EffectiveFrom    DATETIME2       NULL,                   -- 有效期开始（源系统下发）
    EffectiveTo      DATETIME2       NULL,                   -- 有效期结束（NULL=永久有效）
    RefreshBatchNo   NVARCHAR(50)    NOT NULL,               -- 写入本行的刷新批次号（FK->MES_BOM_Edge_RefreshLog.RefreshBatchNo）
    RefreshedAt      DATETIME2       NOT NULL DEFAULT GETDATE(), -- 本行最后刷新时间
    CreatedAt        DATETIME2       NOT NULL DEFAULT GETDATE()
);
GO

-- 面向迭代展开的核心索引
CREATE CLUSTERED INDEX IX_BOMEdgeActive_Parent
ON MES_BOM_Edge_Active(ParentMaterialCode, IsActive);         -- sp_ExpandBOMBatch_vNext 每层 JOIN 主键

CREATE NONCLUSTERED INDEX IX_BOMEdgeActive_BOMNO
ON MES_BOM_Edge_Active(BOMNO, ParentMaterialCode, IsActive)   -- BOMNO 寻址入口（第1层展开）
WHERE BOMNO IS NOT NULL;

CREATE NONCLUSTERED INDEX IX_BOMEdgeActive_Child
ON MES_BOM_Edge_Active(ChildMaterialCode)                     -- 反向追溯哪些父件用到此子件
INCLUDE (ParentMaterialCode, BOMNO);

CREATE NONCLUSTERED INDEX IX_BOMEdgeActive_Source
ON MES_BOM_Edge_Active(SourceSystem, SourceBOMId)             -- 追溯：定位到源系统原始行
WHERE SourceBOMId IS NOT NULL;
GO

-- =============================================
-- 1.2c BOM边刷新控制日志表（v5.0.26新增）
-- =============================================
-- 用途：记录每次 sp_RefreshBOMEdgeActive 的刷新状态，防止 Workset 消费半刷新数据
-- 保护规则：sp_ExpandBOMBatch_vNext 展开前必须校验最新记录 Status='COMPLETED'；
--           否则抛出异常并阻止展开（唯一允许阻止展开的非进程级保护）
-- 数据库：MES_Integration（ODS库）
-- =============================================

CREATE TABLE MES_BOM_Edge_RefreshLog (
    Id             BIGINT          PRIMARY KEY IDENTITY(1,1),
    RefreshBatchNo NVARCHAR(50)    NOT NULL,                  -- 刷新批次号（格式 REF-{yyyyMMdd}-{seq}）
    RefreshType    NVARCHAR(20)    NOT NULL,                  -- FULL / INCREMENTAL
    Status         NVARCHAR(20)    NOT NULL DEFAULT 'RUNNING',-- RUNNING / COMPLETED / FAILED
    StartTime      DATETIME2       NOT NULL DEFAULT GETDATE(),
    EndTime        DATETIME2       NULL,
    RowCount       INT             NULL,                      -- 本次刷新写入行数
    ErrorMessage   NVARCHAR(MAX)   NULL,
    CreatedAt      DATETIME2       NOT NULL DEFAULT GETDATE(),

    CONSTRAINT UQ_RefreshLog_BatchNo UNIQUE (RefreshBatchNo)
);
GO

CREATE INDEX IX_RefreshLog_Status ON MES_BOM_Edge_RefreshLog(Status, StartTime DESC);
GO

-- =============================================
-- 1.3 BOM展开结果工作集
-- =============================================

CREATE TABLE MES_APS_BOM_Workset (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,  -- ⚠️ 单位用量（生产1个父件需要几个子件），不累乘！
    Level INT NOT NULL,
    Path NVARCHAR(MAX) NULL,
    ParentProcRefCode NVARCHAR(50) NULL,               -- v5.0.7 父件工序参考码（ERP BOM原始辅助字段，经MES_BOM_View契约承接）
    ChildProcRefCode NVARCHAR(50) NULL,                -- v5.0.7 子件工序参考码（同上）
    ChildSourceHintCode NVARCHAR(50) NULL,             -- v5.0.7 子件来源提示码（当前来源=ERP BOM的produce字段，0-11编码；详见字段说明文档§3.1）
    ChildRequiredStageCode NVARCHAR(50) NULL,           -- v5.0.7 子件供给所需大工艺阶段码（5号位后置回填；NULL=保守策略：全工艺完成后才可供给）
    ChildRequiredFactory NVARCHAR(20) NULL,             -- v5.0.10 子件应归属账面工厂（R17 推导，值域=CN/CN6课/BJ/TJ/SH/NULL）；外购件=NULL；NULL=未回填
    RequestDetailId BIGINT NULL,                        -- v5.0.21 来源追溯锚点（逻辑关联 MES_API_BOM_Request_Detail.Id；数据库未建立物理外键）；nullable；非业务键；1号位主链不消费
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_BOMWorkset_Batch 
ON MES_APS_BOM_Workset(BatchNo, ParentMaterialCode);

CREATE NONCLUSTERED INDEX IX_BOMWorkset_BOMNO 
ON MES_APS_BOM_Workset(BOMNO, Level);
GO

-- =============================================
-- 1.4 BOM展开结果归档表
-- =============================================

CREATE TABLE MES_APS_BOM_Workset_Archive (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,
    Level INT NOT NULL,
    Path NVARCHAR(MAX) NULL,
    ParentProcRefCode NVARCHAR(50) NULL,               -- v5.0.7 同Workset
    ChildProcRefCode NVARCHAR(50) NULL,                -- v5.0.7 同Workset
    ChildSourceHintCode NVARCHAR(50) NULL,             -- v5.0.7 同Workset（0-11编码）
    ChildRequiredStageCode NVARCHAR(50) NULL,           -- v5.0.7 同Workset
    ChildRequiredFactory NVARCHAR(20) NULL,             -- v5.0.10 同Workset
    RequestDetailId BIGINT NULL,                            -- v5.0.26 同Workset RequestDetailId（归档时透传）
    CreatedAt DATETIME2 NOT NULL,
    ArchivedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_BOMArchive_Batch 
ON MES_APS_BOM_Workset_Archive(BatchNo, CreatedAt);
GO

-- =============================================
-- 1.5 实时BOM展开请求表
-- =============================================

-- v5.0.33: 补 RequestDetailId/OrderCanonicalId/ResolvedBOMNO，支持 RequestDetail 级幂等保护
CREATE TABLE MES_API_BOM_Request_Realtime (
    Id               BIGINT          PRIMARY KEY IDENTITY(1,1),
    BOMNO            NVARCHAR(50)    NOT NULL,
    RequestDetailId  BIGINT          NULL,           -- v5.0.33: 订单粒度锚点（@RequestDetailId 传入值）
    OrderCanonicalId BIGINT          NULL,           -- v5.0.33: 订单 ID 追溯（从 RequestDetail 反查）
    ResolvedBOMNO    NVARCHAR(50)    NULL,           -- v5.0.33: 解析后 BOMNO（MAT:前缀或真实BOMNO）
    RequestTime      DATETIME2       NOT NULL DEFAULT GETDATE(),
    Status           NVARCHAR(20)    NOT NULL DEFAULT 'PENDING',  -- PENDING/PROCESSING/READY/FAILED
    CompletedTime    DATETIME2       NULL,
    ExpandedRowCount INT             NULL,
    ErrorMessage     NVARCHAR(MAX)   NULL,
    RetryCount       INT             NOT NULL DEFAULT 0,
    Priority         INT             NOT NULL DEFAULT 0           -- 0=普通，1=紧急
);
GO

CREATE INDEX IX_Realtime_Status ON MES_API_BOM_Request_Realtime(Status, Priority DESC, RequestTime);
GO

-- =============================================
-- 1.6 实时BOM展开结果工作集
-- =============================================

CREATE TABLE MES_APS_BOM_Workset_Realtime (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,  -- ⚠️ 单位用量，不累乘！
    Level INT NOT NULL,
    ParentProcRefCode NVARCHAR(50) NULL,               -- v5.0.7 同Workset
    ChildProcRefCode NVARCHAR(50) NULL,                -- v5.0.7 同Workset
    ChildSourceHintCode NVARCHAR(50) NULL,             -- v5.0.7 同Workset（0-11编码）
    ChildRequiredStageCode NVARCHAR(50) NULL,           -- v5.0.7 同Workset
    ChildRequiredFactory NVARCHAR(20) NULL,             -- v5.0.10 同Workset
    RequestDetailId BIGINT NULL,                            -- v5.0.26 实时插单来源追溯锚点（逻辑关联 MES_API_BOM_Request_Detail.Id；数据库未建立物理外键，nullable）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_Realtime_BOMNO 
ON MES_APS_BOM_Workset_Realtime(BOMNO, Level);
GO

-- =============================================
-- 1.7 BOM阶段顺序明细工作集（v5.0.7新增，v5.0.8升级为统一阶段路径结果表）
-- 表达（v5.0.8）：统一阶段路径结果表，通过StageScopeType区分：
--   EDGE：某条BOM边对应的子件，在供给父件之前的完整大工艺顺序（ParentMaterialCode=父件编码）
--   ROOT：最上层产品自身完工所需的完整大工艺顺序（ParentMaterialCode=NULL）
-- 数据来源：5号位基于ERP BOM原始辅助字段组（ParentProcRefCode/ChildProcRefCode/ChildSourceHintCode + 对照表）推导产出
-- ROOT推导规则：5号位取Level=1的ParentProcRefCode → 映射标准化路径 → 不一致时取最长+记WARNING
-- 1号位消费：按StageScopeType区分查询；读取StageSeq+StageCode → 串接RoutingOperation中每个阶段的小工序排Task
-- 5号位消费：读取完整阶段链 → 结合StageLeadTimeParam做库存/供给判断
-- ⚠️ 职责分离：此表为BOM侧派生结果，RoutingStage为阶段字典/标准阶段语言，二者不混写
-- =============================================

CREATE TABLE MES_APS_BOM_Workset_StageDetail (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    WorksetId BIGINT NULL,                              -- v5.0.26 FK->对应 Workset 表 .Id；每条Workset行单独写 StageDetail；NULL=兼容旧批次
    BOMNO NVARCHAR(50) NOT NULL,
    StageScopeType NVARCHAR(10) NOT NULL DEFAULT 'EDGE', -- v5.0.8 EDGE=子件供给路径 / ROOT=根产品完工路径
    ParentMaterialCode NVARCHAR(50) NULL,                -- v5.0.8 EDGE=父件编码；ROOT=NULL
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    StageSeq INT NOT NULL,                              -- 阶段顺序号（10/20/30，间隔10）——v5.0.12：**排程唯一权威顺序源**
    StageCode NVARCHAR(50) NOT NULL,                    -- 大工艺阶段码（如TJ_MACH/TJ_OUTS/BJ_SURF）；v5.0.12：**必须取自 StageDict**；**R20 场景采用他用方视角**（父件在 TJ、R20 指派到 BJ 时，此处直接写 BJ_MACH，1 号位读到直接去 BJ 的 Routing 找小工序）
    IsSupplyThreshold BIT NOT NULL DEFAULT 0,            -- 1=此阶段为供给阈值点（仅EDGE有效；ROOT恒为0）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_StageDetail_Batch
ON MES_APS_BOM_Workset_StageDetail(BatchNo, BOMNO, ChildMaterialCode);

CREATE NONCLUSTERED INDEX IX_StageDetail_Child
ON MES_APS_BOM_Workset_StageDetail(ChildMaterialCode, StageSeq);
GO

CREATE NONCLUSTERED INDEX IX_StageDetail_WorksetId
ON MES_APS_BOM_Workset_StageDetail(WorksetId)           -- v5.0.26 sp_CleanupBOMWorkset 级联清理用
WHERE WorksetId IS NOT NULL;
GO

-- 1.7b BOM阶段顺序明细归档表（v5.0.7新增）
CREATE TABLE MES_APS_BOM_Workset_StageDetail_Archive (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    WorksetId BIGINT NULL,                              -- v5.0.26 SourceWorksetId语义：原始 MES_APS_BOM_Workset.Id（归档时透传，无FK约束，仅追溯用；与本表 Archive.Id 无关）；NULL=兼容旧批次
    BOMNO NVARCHAR(50) NOT NULL,
    StageScopeType NVARCHAR(10) NOT NULL DEFAULT 'EDGE', -- v5.0.8
    ParentMaterialCode NVARCHAR(50) NULL,                -- v5.0.8 ROOT=NULL
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    StageSeq INT NOT NULL,
    StageCode NVARCHAR(50) NOT NULL,
    IsSupplyThreshold BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL,
    ArchivedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_StageDetailArchive_Batch
ON MES_APS_BOM_Workset_StageDetail_Archive(BatchNo, CreatedAt);
GO

-- 1.7c BOM阶段顺序明细实时工作集（v5.0.7新增）
CREATE TABLE MES_APS_BOM_Workset_StageDetail_Realtime (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BOMNO NVARCHAR(50) NOT NULL,
    WorksetId BIGINT NULL,                              -- v5.0.26 FK->对应 Workset 表 .Id；每条Workset行单独写 StageDetail；NULL=兼容旧批次
    StageScopeType NVARCHAR(10) NOT NULL DEFAULT 'EDGE', -- v5.0.8
    ParentMaterialCode NVARCHAR(50) NULL,                -- v5.0.8 ROOT=NULL
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    StageSeq INT NOT NULL,
    StageCode NVARCHAR(50) NOT NULL,
    IsSupplyThreshold BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_StageDetailRT_BOMNO
ON MES_APS_BOM_Workset_StageDetail_Realtime(BOMNO, ChildMaterialCode);
GO


CREATE NONCLUSTERED INDEX IX_StageDetailRT_WorksetId
ON MES_APS_BOM_Workset_StageDetail_Realtime(WorksetId)  -- v5.0.26 实时链路级联清理用
WHERE WorksetId IS NOT NULL;
GO
-- =============================================
-- 1.8 日志表
-- =============================================

CREATE TABLE MES_API_BOM_Request_Log (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NULL,
    Message NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_Log_Batch ON MES_API_BOM_Request_Log(BatchNo, CreatedAt);
GO

-- =============================================
-- 1.9 BOM 解析错误登记表（v5.0.10 新增，v5.0.11 处置策略改为"降级不阻塞"）
-- 用途：登记 5 号位 BOM 推导/展开过程中发现的数据异常与容错诊断
-- 职责分离：诊断信息不进 Workset/StageDetail 核心表（L1 合同稳定），单独表演进不影响下游
-- 消费方：
--   - 5 号位：回填时写入（**永不阻塞批次**，异常一律降级 + 登记）
--   - 0 号位 / 业务复核人员：按 Severity + ReviewStatus='PENDING' 周度/月度巡检
--   - 运维：按 Severity 汇总告警；Issues 统计驱动 ERP 源端数据质量改进
-- 【v5.0.11 核心决策】：本表仅为"事后追溯与数据质量镜子"，**不决定批次是否 READY**
--   批次状态机：PENDING → EXPANDED → ENRICHED → READY（永不因数据问题进 FAILED）
--   FAILED 仅保留给 SP 自身崩溃（进程异常、tempdb 满、连接中断等极端情况）
-- =============================================

CREATE TABLE MES_APS_BOM_Workset_Issues (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NULL,           -- 涉及父件（EXPAND_FAILED 等可能为 NULL）
    ChildMaterialCode NVARCHAR(50) NULL,            -- 涉及子件
    Produce NVARCHAR(5) NULL,                       -- 子件 Produce 值（0-11）
    IssueType NVARCHAR(40) NOT NULL,                -- 见下方值域
    Severity NVARCHAR(10) NOT NULL,                 -- INFO/WARN/ERROR/CRITICAL；v5.0.11：仅为事后处置优先级，不决定批次 READY
    Detail NVARCHAR(1000) NOT NULL,                 -- 诊断详情（人类可读）
    DegradeAction NVARCHAR(100) NULL,               -- v5.0.11 新增：降级动作标签（如 STAGE_NULL / FACTORY_FALLBACK / QTY_DEFAULT_1 / CYCLE_SKIP / BOMNO_SKIP）
    ExpectedFactory NVARCHAR(20) NULL,              -- R17 预期工厂（FACTORY_MISMATCH 用）
    ActualFactory NVARCHAR(20) NULL,                -- BOM 实际工厂（FACTORY_MISMATCH 用）
    RawRefJson NVARCHAR(MAX) NULL,                  -- 原始 ERP 字段快照（JSON，供复核溯源，ERP 升级时字段名不影响本表结构）
    ReviewStatus NVARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING / CONFIRMED / IGNORED / FIXED
    ReviewedBy NVARCHAR(100) NULL,
    ReviewedAt DATETIME2 NULL,
    Resolution NVARCHAR(500) NULL,                  -- 复核结论（如"ERP Produce 录错，已修"）
    RequestDetailId BIGINT NULL,                    -- v5.0.21 来源追溯锚点（可通过 Workset.Id 间接回溯，此处冗余方便直查）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- IssueType 值域（v5.0.11 修订处置策略：全部降级 + 登记，永不阻塞批次）：
--   LEAF                     = Produce 声明内制但物料无下阶 BOM   Severity=INFO   降级=STAGE_NULL（ChildRequiredStageCode=NULL）
--   FACTORY_MISMATCH         = Produce 厂 ≠ BOM 实际厂（单厂）     Severity=WARN   降级=FACTORY_FALLBACK（保留 BOM 原生链）
--   FACTORY_MISMATCH_MULTI   = 跨多厂                              Severity=WARN   降级=FACTORY_FALLBACK
--   NO_STAGE                 = 有 BOM 但无大工艺段                 Severity=WARN   降级=STAGE_NULL（1号位保守策略兜底）
--   UNKNOWN_PROCCODE         = 工序码查不到对照表                  Severity=WARN   降级=STAGE_NULL 或 部分链（可识别部分推导）
--   QUANTITY_INVALID         = Quantity ≤ 0 或 NULL                Severity=WARN   降级=QTY_DEFAULT_1（按 1 兜底）
--   MISSING_PRODUCE          = Produce 字段空                      Severity=WARN   降级=PRODUCE_DEFAULT_1（按 1 兜底）
--   CYCLIC_BOM               = BOM 环路                            Severity=ERROR  降级=CYCLE_SKIP（首次访问保留，重复循环节点跳过）
--   EXPAND_FAILED            = 单个 BOMNo 展开异常                 Severity=CRITICAL 降级=BOMNO_SKIP（该 BOMNo 整棵树作废，继续其他）
-- ⚠️ 全部 IssueType 均"降级 + 登记"处置；批次状态机永远走到 READY（除非 SP 进程崩溃）

CREATE INDEX IX_BOMIssues_Batch ON MES_APS_BOM_Workset_Issues(BatchNo, IssueType, Severity);
CREATE INDEX IX_BOMIssues_Review ON MES_APS_BOM_Workset_Issues(ReviewStatus, Severity, CreatedAt);
CREATE INDEX IX_BOMIssues_Child ON MES_APS_BOM_Workset_Issues(ChildMaterialCode, BatchNo);
GO

-- =============================================
-- 1.9c OrderEmergencyMaterialOverride（v5.0.21 新增，急单临时 Material 补齐桥接表）
-- 定位：白天急单/插单验证时，正式 Master 尚未夜间同步的情况下，人工临时录入物料标识
-- 【设计约束】仅作中介临时桥接数据；留痕可追溯；不替代夜间正式 sp_SyncMasterData 写入的 Material 记录
-- 消费方：2 号位 BOM 入口准备步骤 + 5 号位 Workset 处理时 Model→MaterialCode 兜底查询
-- 夜间覆盖：正式同步后将 PromotedByOfficialSync 置 1，IsActive 置 0
-- =============================================

CREATE TABLE OrderEmergencyMaterialOverride (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    OrderStagingId BIGINT NOT NULL,              -- 逻辑追溯 ERP_Order_Staging.Id，不建 FK
    OrderNo NVARCHAR(50) NOT NULL,               -- 可读追溯
    Model NVARCHAR(100) NOT NULL,                -- 型号（人工录入）
    MaterialCode NVARCHAR(100) NOT NULL,         -- 临时映射的物料编码
    FactoryCode NVARCHAR(50) NULL,               -- 所属工厂（可空）
    MaterialName NVARCHAR(200) NULL,             -- 物料名称（可选辅助）
    UOM NVARCHAR(20) NOT NULL DEFAULT 'PCS',     -- 单位（默认件）
    Reason NVARCHAR(500) NOT NULL,               -- 补齐原因（必填，审计字段）
    CreatedBy NVARCHAR(100) NOT NULL,            -- 操作人（必填）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    ExpireAt DATETIME2 NULL,                     -- 失效时间（NULL=夜间同步后自动覆盖）
    IsActive BIT NOT NULL DEFAULT 1,
    PromotedByOfficialSync BIT NOT NULL DEFAULT 0,  -- 夜间正式同步覆盖后置1

    -- ⚠️ v5.0.40: OrderStagingId 为逻辑追溯字段；ERP_Order_Staging 位于 APS_Production，
    --   SQL Server 不支持跨库 FK，故不建约束；本表仅服务 Staging 阶段人工补录，不进入 BOM 主链
);
GO

CREATE INDEX IX_EmergencyMaterial_Order ON OrderEmergencyMaterialOverride(OrderStagingId, IsActive);
CREATE INDEX IX_EmergencyMaterial_Model ON OrderEmergencyMaterialOverride(Model, IsActive);
GO

-- =============================================
-- 1.9d OrderEmergencyBomWorkset（v5.0.21 新增，人工友好版临时 Workset 主层）
-- 定位：急单时正式 BOM 尚未就绪，人工临时录入 BOM 入口解析结果（BOMNO指针 或 处理标记）
-- 【设计约束】人工友好：只保留支撑 5 号位和 1 号位走通流程所需的最小字段；
--            不复制 MES_APS_BOM_Workset 全部技术字段
-- 【设计约束】临时桥接数据；留痕可追溯；不替代夜间正式 BOM 展开结果
-- 消费方：5 号位在 Workset 处理时，若正式 Workset 不存在，降级查此表
-- =============================================

CREATE TABLE OrderEmergencyBomWorkset (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    OrderStagingId BIGINT NOT NULL,              -- 逻辑追溯 ERP_Order_Staging.Id，不建 FK
    OrderNo NVARCHAR(50) NOT NULL,               -- 可读追溯
    MaterialCode NVARCHAR(100) NOT NULL,         -- 根产品物料编码
    BOMNO NVARCHAR(50) NULL,                     -- 指向的 BOMNO（NULL=纯阶段标记，无 BOM 展开）
    BomEntryResult NVARCHAR(50) NOT NULL,        -- 5号位解析结果标记（值域见下方注释）
    Reason NVARCHAR(500) NOT NULL,               -- 补齐原因（必填，审计字段）
    CreatedBy NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    ExpireAt DATETIME2 NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    PromotedByOfficialSync BIT NOT NULL DEFAULT 0,

    -- ⚠️ v5.0.40: OrderStagingId 为逻辑追溯字段；跨库 FK 不建约束（同上）
);
GO
-- BomEntryResult 值域（5号位在 Workset 处理阶段写入）：
--   EXPLICIT_BOMNO_USED         = 使用订单显式 BOMNO
--   DEFAULT_BOMNO_RESOLVED      = 按 Model 找到默认 BOMNO
--   SPECIAL_INTERNAL_PART       = 单品出口-内制零件（走阶段排程，无 BOM 展开）
--   SPECIAL_PURCHASED_PART      = 单品出口-外购件（打标，不排程）
--   BOM_ENTRY_UNRESOLVED        = 无法解析 BOM 入口（需进一步人工处理）

CREATE INDEX IX_EmergencyBomWorkset_Order ON OrderEmergencyBomWorkset(OrderStagingId, IsActive);
GO

-- =============================================
-- 1.9e OrderEmergencyBomStageDetail（v5.0.21 新增，人工友好版临时 StageDetail 路径层）
-- 定位：急单时正式 StageDetail 尚未就绪，人工临时录入阶段路径
-- 【设计约束】支撑 1 号位主链：StageDetail → MaterialStageDeptContext → ProductionDepartmentId → Routing三件套
--            只保留最小必要字段：MaterialCode / StageSeq / StageCode / IsSupplyThreshold
-- 【设计约束】StageCode 必须取自 StageDict（与正式 StageDetail 相同要求）
-- =============================================

CREATE TABLE OrderEmergencyBomStageDetail (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    EmergencyWorksetId BIGINT NOT NULL,          -- FK→OrderEmergencyBomWorkset.Id
    MaterialCode NVARCHAR(100) NOT NULL,         -- 物料编码（含子件，支持多层）
    StageSeq INT NOT NULL,                       -- 阶段顺序号（10/20/30，间隔10）
    StageCode NVARCHAR(50) NOT NULL,             -- 大工艺阶段码；必须取自 StageDict
    IsSupplyThreshold BIT NOT NULL DEFAULT 0,    -- 是否为供给阈值点（与正式 StageDetail 语义相同）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),

    FOREIGN KEY (EmergencyWorksetId) REFERENCES OrderEmergencyBomWorkset(Id)
);
GO

CREATE INDEX IX_EmergencyStageDetail_Workset ON OrderEmergencyBomStageDetail(EmergencyWorksetId);
CREATE INDEX IX_EmergencyStageDetail_Material ON OrderEmergencyBomStageDetail(MaterialCode, StageSeq);
GO

-- =============================================
-- 1.9b ProduceToFactoryMap（v5.0.11 新增，R17 规则资产化配置表）
-- 定位：R17 Produce→工厂映射规则的**物理资产宿主**；从硬编码/文档 → 配置表
-- 消费方：5 号位 sp_EnrichBOMWorkset 查此表推导 ChildRequiredFactory
-- 防腐价值：ERP 语义变化（如 Produce 扩值域、重新分类）只需 UPDATE 本表，**不改代码不改 DDL**
-- 维护责任：0 号位审批 + 5 号位维护；**本表变更必须走评审流程**（影响所有 BOM 展开结果）
-- =============================================

CREATE TABLE ProduceToFactoryMap (
    ProduceCode     TINYINT PRIMARY KEY,                    -- 0-11
    ProduceName     NVARCHAR(50) NOT NULL,                  -- 业务名称（如"内制"/"外购量产"）
    SourceCategory  NVARCHAR(30) NOT NULL,                  -- v5.0.14 收敛为 3 类：PURCHASE （购入、含保税/课税两子分类） / INHOUSE_SELF（内制·自用） / INHOUSE_CROSS（内制·他用、R20）
    FactoryStrategy NVARCHAR(20) NOT NULL,                  -- INHERIT（继承父件工厂）/ FIXED（固定工厂）/ NONE（外购不推导）
    TargetFactory   NVARCHAR(20) NULL,                      -- Strategy=FIXED 时生效：CN/CN6课/BJ/TJ/SH（v5.0.14 补入 CN，因 Produce=7 照片权威为 CN 中国公司内制他用）
    ShouldDrilldown BIT NOT NULL DEFAULT 1,                 -- 1=继续向下展开 BOM；0=BOM下钻终止（仅外购 R07）
    CrossOrgHandoffFlag BIT NOT NULL DEFAULT 0,             -- v5.0.11：1=打 CROSS_ORG_HANDOFF 标签（R20 内制他用：本厂仍下钻 BOM 拿到下阶物料明细，但该链最终归他用方工厂排产）；0=本厂自排
    Description     NVARCHAR(500) NULL,                     -- 含义说明 + 典型案例
    IsActive        BIT NOT NULL DEFAULT 1,
    RuleVersion     NVARCHAR(20) NOT NULL DEFAULT 'v1.0',   -- 规则版本（R17 规则升级时递增，便于追溯）
    UpdatedBy       NVARCHAR(100) NULL,
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 初始化数据（v5.0.14更新：以用户提供的 Produce 权威照片为准重写，2026-04-28）
-- R07：PURCHASE → ShouldDrilldown=0（外购件唯一的 BOM 下钻终止场景）
-- R20：INHOUSE_CROSS → ShouldDrilldown=1 + CrossOrgHandoffFlag=1【目标工厂 6→BJ、7→CN、11→TJ】
--        语义：本厂仍下钻 BOM（需要下阶物料明细、成本、时长估计），但标记“该链最终归他用方工厂排产”，不进入本厂排产队列
-- R17：INHOUSE_SELF → ShouldDrilldown=1 + CrossOrgHandoffFlag=0（正常下钻，本厂自排）
INSERT INTO ProduceToFactoryMap (ProduceCode, ProduceName, SourceCategory, FactoryStrategy, TargetFactory, ShouldDrilldown, CrossOrgHandoffFlag, Description) VALUES
( 0, N'日本保税/国内保税',                 N'PURCHASE',       N'NONE',    NULL,       0, 0, N'购入·保税；BOM 下钻终止（R07）；ChildRequiredFactory=NULL'),
( 1, N'中国/北京/天津内制自用',         N'INHOUSE_SELF',   N'INHERIT', NULL,       1, 0, N'内制·自用（通用内制）；继承父件账面工厂（R21：优先父件 GoodsProcCode，回退 MaterialProcCode）'),
( 2, N'国内课税',                              N'PURCHASE',       N'NONE',    NULL,       0, 0, N'购入·课税；同 Produce=0 按外购处置'),
( 3, N'海外保税（日本以外）',                N'PURCHASE',       N'NONE',    NULL,       0, 0, N'购入·保税（日本以外）；按外购处置'),
( 4, N'海外课税',                              N'PURCHASE',       N'NONE',    NULL,       0, 0, N'购入·课税；按外购处置（与 Produce=10 业务适用范围不同，具体区分由业务部门定义）'),
( 5, N'CN 制造 6 课内制自用',                  N'INHOUSE_SELF',   N'FIXED',   N'CN6课',   1, 0, N'内制·自用：CN 制造 6 课内制自用（不含 ASSY）；v5.0.14 从“特注”纠正为“自用”'),
( 6, N'BJ 北京工厂内制他用',                   N'INHOUSE_CROSS',  N'FIXED',   N'BJ',      1, 1, N'R20 跨厂跨域：本厂继续下钻 BOM 拿到下阶明细；打 CROSS_ORG_HANDOFF 标签；该链最终由 BJ 工厂排产，本厂不占自身产能'),
( 7, N'CN 中国公司内制他用',                   N'INHOUSE_CROSS',  N'FIXED',   N'CN',      1, 1, N'R20 跨厂跨域：本厂继续下钻 BOM 拿到下阶明细；打 CROSS_ORG_HANDOFF 标签；该链最终由 CN 中国公司排产，本厂不占自身产能【v5.0.14 照片权威纠正：TargetFactory 原误为 TJ】'),
( 8, N'CN 制造 6 课内制 ASSY 自用',              N'INHOUSE_SELF',   N'FIXED',   N'CN6课',   1, 0, N'内制·自用：CN 制造 6 课 ASSY 业务专用内制自用；v5.0.14 从“特注”纠正为“自用”'),
( 9, N'SH 上海公司内制自用',                   N'INHOUSE_SELF',   N'FIXED',   N'SH',      1, 0, N'内制·自用：SH 上海公司内制自用；v5.0.14 从“特注”纠正为“自用”'),
(10, N'海外课税（适用范围与 Produce=4 不同）', N'PURCHASE',     N'NONE',    NULL,       0, 0, N'购入·课税；按外购处置（与 Produce=4 适用范围不同，具体区分由业务部门定义）'),
(11, N'TJ 天津工厂内制他用',                   N'INHOUSE_CROSS',  N'FIXED',   N'TJ',      1, 1, N'R20 跨厂跨域：本厂继续下钻 BOM 拿到下阶明细；打 CROSS_ORG_HANDOFF 标签；该链最终由 TJ 天津工厂排产，本厂不占自身产能【v5.0.14 照片权威纠正：TargetFactory 原误为 SH】');
GO

-- =============================================
-- 1.9c StageDict（v5.0.11 新增；v5.0.12 加 R20 他用方视角；v5.0.15 字段净化：删 IsStockPoint + IsOutsource）
-- 方案 B：工厂+阶段码（如 CN_MACH / TJ_OUTS / JP_ASSY）
-- 定位：APS 大工艺阶段**业务语义字典**（APS 自主维护，非 ERP 字段）；BOM↔Routing 对接主键之二（主键之一为 MaterialCode）
-- 引用本表的：
--   - RoutingStage.StageCode（物料级阶段配置）
--   - RoutingOperation.StageCode（v5.0.12 显式引用；BOM↔Routing 对接主键）
--   - MES_APS_BOM_Workset_StageDetail.StageCode（BOM 派生结果）
--   - APS_BOM_STAGE_PATH_RAW.StageCode（APS 本地缓存）
--   - StageLeadTimeParam.StageCode（阶段提前期参数）
-- 维护责任：0 号位审批 + 3 号位/5 号位协同维护
-- 扩展策略：新增阶段由业务审定后 INSERT 一行即可，不需要改 DDL/代码
--
-- ⚠️ v5.0.15 字段净化原则（严格分层）：
--   StageDict 只承载"**阶段自身属性**"——即不依赖具体物料就能确定的语义（StageCode / StageName / FactoryCode / StageCategory / CategoryName / SortHint / Description）
--   "**物料×阶段联合属性**"（是否入库、入库角色、阶段 LeadTime、入库阈值点）一律不放本表，分层承接如下：
--     · IsStockPoint（是否半成品库存断点）：字典级删除 → 若需物料级稳态配置走 RoutingStage.IsStockPoint（3 号位维护）→ BOM 边级派生走 StageDetail.IsSupplyThreshold（已存在，5 号位回填）
--     · 阶段 LeadTime：走 StageLeadTimeParam（阶段提前期参数表）
--     · IsOutsource：与 StageCategory='OUTS' 语义重复，删除；下游改用 StageCategory 判断
-- ⚠️ v5.0.12 跨组织视角约定：BOM 侧 StageDetail.StageCode 采用**目标工厂视角**——
--    R20 Produce ∈ {6,7,11} 的跨组织交接场景，父件在 TJ 但 R20 指派到 BJ 时，
--    StageDetail.StageCode 直接写 BJ_MACH（不是 TJ_MACH），1 号位无需再做跨厂翻译
--    该视角由 5 号位在 sp_EnrichBOMWorkset 回填时统一处理（推导函数读 ChildRequiredFactory 决定视角）
-- ⚠️ v1.0 初始化数据为草案，需业务审定后迭代
-- =============================================

CREATE TABLE StageDict (
    StageCode       NVARCHAR(20) PRIMARY KEY,               -- 格式：{工厂}_{阶段类别}，如 CN_MACH
    StageName       NVARCHAR(50) NOT NULL,                  -- 中文名，如"CN机加"
    FactoryCode     NVARCHAR(20) NOT NULL,                  -- 工厂维度（独立字段便于按厂过滤）
    StageCategory   NVARCHAR(20) NOT NULL,                  -- MACH/MOLD/CAST/DRAW/FORGE/EXTRU/OUTS/ASSY/SURF/FINAL + 预留 INSP/CLEAN（**外协判定：StageCategory='OUTS'**）
    CategoryName    NVARCHAR(50) NOT NULL,                  -- 类别中文名（机加/注塑/铸造/冷拔/锻造/型材押出/外协/组立/表面处理/出口）
    SortHint        INT NOT NULL DEFAULT 100,               -- 同工厂内默认排序（仅参考，实际顺序以 StageDetail.StageSeq 为准）
    Description     NVARCHAR(200) NULL,
    IsActive        BIT NOT NULL DEFAULT 1,
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_StageDict_Factory ON StageDict(FactoryCode, SortHint) WHERE IsActive = 1;
CREATE INDEX IX_StageDict_Category ON StageDict(StageCategory) WHERE IsActive = 1;
GO

-- 初始化数据（v5.0.19 基于工序对照表.xlsx 业务数据生成，2026-05-06）
-- 命名规范：StageCode = {工厂短码}_{阶段类别}；CN6课 → CN6_（技术键不含中文）
INSERT INTO StageDict (StageCode, StageName, FactoryCode, StageCategory, CategoryName, SortHint, Description) VALUES
-- BJ 工厂（65 条工序码）
(N'BJ_MACH',    N'BJ机加',    N'BJ',     N'MACH',  N'机加', 10, N'BJ 工厂一般机加阶段（车削/铣削/钻孔等）'),
(N'BJ_MOLD',    N'BJ注塑',    N'BJ',     N'MOLD',  N'注塑', 11, N'BJ 工厂注塑/树脂成型阶段（8课）'),
(N'BJ_CAST',    N'BJ铸造',    N'BJ',     N'CAST',  N'铸造', 12, N'BJ 工厂铸造阶段（7课：铝铸造/锌铸造/熔铸）'),
(N'BJ_DRAW',    N'BJ冷拔',    N'BJ',     N'DRAW',  N'冷拔', 13, N'BJ 工厂冷拔/拉拔阶段'),
(N'BJ_FORGE',   N'BJ锻造',    N'BJ',     N'FORGE', N'锻造', 14, N'BJ 工厂锻造/热处理阶段（1课）'),
(N'BJ_EXTRU',   N'BJ型材押出', N'BJ',     N'EXTRU', N'型材押出', 15, N'BJ 工厂型材挤压/押出成型阶段（1课）'),
(N'BJ_OUTS',    N'BJ外协',    N'BJ',     N'OUTS',  N'外协', 20, N'BJ 工厂外协阶段'),
(N'BJ_SURF',    N'BJ表面处理', N'BJ',     N'SURF',  N'表面处理', 35, N'BJ 工厂表面处理阶段（涂装/氧化/喷丸）'),
(N'BJ_ASSY',    N'BJ组立',    N'BJ',     N'ASSY',  N'组立', 40, N'BJ 工厂组立/装配阶段'),
(N'BJ_FINAL',   N'BJ出口',    N'BJ',     N'FINAL', N'出口', 60, N'BJ 工厂出口完工'),
-- CN 工厂（32 条工序码）
(N'CN_MACH',    N'CN机加',    N'CN',     N'MACH',  N'机加', 10, N'CN 工厂一般机加阶段（车削/铣削等）'),
(N'CN_FORGE',   N'CN锻造',    N'CN',     N'FORGE', N'锻造', 14, N'CN 工厂锻造阶段（CG缸筒挤压等）'),
(N'CN_OUTS',    N'CN外协',    N'CN',     N'OUTS',  N'外协', 20, N'CN 工厂外协阶段'),
(N'CN_SURF',    N'CN表面处理', N'CN',     N'SURF',  N'表面处理', 35, N'CN 工厂表面处理阶段（涂装/氧化/喷丸）'),
(N'CN_ASSY',    N'CN组立',    N'CN',     N'ASSY',  N'组立', 40, N'CN 工厂组立/装配阶段'),
(N'CN_FINAL',   N'CN出口',    N'CN',     N'FINAL', N'出口', 60, N'CN 工厂出口完工'),
-- CN6课（特注产线，9 条工序码）
(N'CN6_MACH',   N'CN6课机加', N'CN6课',  N'MACH',  N'机加', 10, N'CN6课 机加阶段（特注产品）'),
(N'CN6_OUTS',   N'CN6课外协', N'CN6课',  N'OUTS',  N'外协', 20, N'CN6课 外协阶段（电镀等）'),
(N'CN6_ASSY',   N'CN6课组立', N'CN6课',  N'ASSY',  N'组立', 40, N'CN6课 组立阶段'),
-- TJ 工厂（39 条工序码）
(N'TJ_MACH',    N'TJ机加',    N'TJ',     N'MACH',  N'机加', 10, N'TJ 工厂一般机加阶段（车削/铣削等；型材/回炉暂归此阶段）'),
(N'TJ_CAST',    N'TJ铸造',    N'TJ',     N'CAST',  N'铸造', 12, N'TJ 工厂铸造阶段（7课）'),
(N'TJ_OUTS',    N'TJ外协',    N'TJ',     N'OUTS',  N'外协', 20, N'TJ 工厂外协阶段'),
(N'TJ_SURF',    N'TJ表面处理', N'TJ',     N'SURF',  N'表面处理', 35, N'TJ 工厂表面处理阶段（涂装/氧化/喷丸）'),
(N'TJ_ASSY',    N'TJ组立',    N'TJ',     N'ASSY',  N'组立', 40, N'TJ 工厂组立/装配阶段'),
(N'TJ_FINAL',   N'TJ出口',    N'TJ',     N'FINAL', N'出口', 60, N'TJ 工厂出口完工'),
-- SH 工厂（7 条工序码）
(N'SH_MACH',    N'SH机加',    N'SH',     N'MACH',  N'机加', 10, N'SH 工厂机加阶段'),
(N'SH_OUTS',    N'SH外协',    N'SH',     N'OUTS',  N'外协', 20, N'SH 工厂外协阶段（电镀等）'),
(N'SH_ASSY',    N'SH组立',    N'SH',     N'ASSY',  N'组立', 40, N'SH 工厂组立阶段'),
(N'SH_FINAL',   N'SH出口',    N'SH',     N'FINAL', N'出口', 60, N'SH 工厂出口完工');
GO

-- =============================================
-- 1.9d ProcessTypeDict（v5.0.12 新增，工序级分类标签字典；预留骨架）
-- 方案：配置表驱动的 ProcessType 值域（不硬编码在 DDL CHECK）
-- 定位：**辅助分类标签**；用于报表、粗分组、统计；**不参与 BOM↔Routing 对接，不作为 1 号位排程主键**
-- 引用本表的：
--   - RoutingOperation.ProcessType（仅作数据质量参考；暂不加 FK，等业务真正启用后再加）
-- 维护责任：0 号位审批 + 3 号位维护
-- ⚠️ v1.0 骨架初始化数据 IsActive=0，业务确定启用前不生效；启用时 0 号位批准后 UPDATE IsActive=1
-- =============================================

CREATE TABLE ProcessTypeDict (
    ProcessType     NVARCHAR(50) PRIMARY KEY,               -- 分类标签值，如 MACHINING / ASSEMBLY
    ProcessTypeName NVARCHAR(100) NOT NULL,                  -- 中文名，如"机加工类"
    Category        NVARCHAR(30) NULL,                       -- 一级归类（PRODUCTION/SUPPORT/QA/LOGISTICS），可扩展
    Description     NVARCHAR(500) NULL,                      -- 典型包含的 OperationName 示例
    IsActive        BIT NOT NULL DEFAULT 0,                  -- ⚠️ 骨架期默认 0；业务确认启用后改 1
    UpdatedBy       NVARCHAR(100) NULL,
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 初始化数据（v1.0 骨架，2026-04-24，IsActive=0 预留；典型 OperationName 仅作举例）
INSERT INTO ProcessTypeDict (ProcessType, ProcessTypeName, Category, IsActive, Description) VALUES
(N'MACHINING',  N'机加工类',   N'PRODUCTION', 0, N'典型 OperationName：NC / MC / 切断 / 精修 / 铣 / 钻'),
(N'ASSEMBLY',   N'装配类',     N'PRODUCTION', 0, N'典型 OperationName：组立 / 压装 / 拧紧 / 接合'),
(N'INSPECTION', N'检验类',     N'QA',         0, N'典型 OperationName：首件检查 / 终检 / 抽检 / 测量'),
(N'OUTSOURCE',  N'外协类',     N'PRODUCTION', 0, N'外协加工相关工序；对应 StageDict.StageCategory=''OUTS''（v5.0.15 起，IsOutsource 字段已删除）'),
(N'PAINT',      N'涂装类',     N'PRODUCTION', 0, N'典型 OperationName：底漆 / 面漆 / 烘烤'),
(N'CLEANING',   N'清洗/清扫类',N'SUPPORT',    0, N'典型 OperationName：超声清洗 / 去毛刺 / 擦净');
GO

-- =============================================
-- 1.9e ProcessCodeDict（v5.0.15 新增；v5.0.16 定位翻转 + APS 增强列）
-- 定位：APS 自维护的 ODS 增强工序字典（业务字段语义来源 ERP/MES，但本表无自动同步）
-- 所属库：MES_Integration（ODS 层）
-- 维护方式（v5.0.46）：字段分两类——
--   1. APS增强字段：StageCode/CodeOrigin/UpdatedBy/IsActive 由系统管理员维护，0号位审批，禁止自动同步覆盖；
--   2. ERP真实属性字段：ERPProperty 来源于ERP真实属性，由5号位同步/透出维护；专用同步只更新ERPProperty，不覆盖APS增强字段。
--   sp_SyncMasterData(@SourceType='ProcessCode') 不恢复。
-- 消费边界：2号位通过 MES_ProcessCode_View 消费 ERPProperty 生成 M库判定索引
-- ⚠️ APS_Production 库严禁查询本表 —— ProcessCode 是 ERP 易变维度，若下沉 APS 将打穿防腐墙
-- 维护方：**APS 系统管理员人工维护 + 0 号位审批**（不参与 sp_SyncMasterData 自动同步流程）
-- ⭐ APS 增强列：StageCode（v5.0.16 新增）—— 把 ProcessCode → StageCode 的基础映射沉淀到本字典；
--    5 号位 sp_EnrichBOMWorkset 与 2 号位 Context 组装统一查 MES_ProcessCode_View.StageCode，**确保两边映射一致**（防止静默断裂）
-- 下游消费方不直查本表，一律通过 §1.9f MES_ProcessCode_View 契约视图，实现吸震
-- =============================================

CREATE TABLE ProcessCodeDict (
    ProcessCode        NVARCHAR(20) PRIMARY KEY,              -- 6 位工序码（左补 0；APS 业务键）
    ProcessName        NVARCHAR(100) NULL,                    -- 工序名称
    FactoryCode        NVARCHAR(20) NOT NULL,                 -- 代码所属工厂（账面工厂，R26 过滤维度；APS 5 厂枚举 CN/CN6课/BJ/TJ/SH/JP）
    ActualFactoryCode  NVARCHAR(20) NULL,                     -- 实际生产工厂（含受托场景；与 FactoryCode 不同即为委外）
    TrusteeProcCode    NVARCHAR(20) NULL,                     -- 受托对方工艺码（仅受托场景非空）
    IsOutsource        BIT NOT NULL DEFAULT 0,                -- 是否外协工序（工序级外协判定，与 StageDict 级外协判定正交）
    IsRetouch          BIT NOT NULL DEFAULT 0,                -- 是否追加工（业务字段，源于 ERP 原生概念）
    WarehouseRole      NVARCHAR(30) NULL,                     -- 仓库角色（如 领料位 / ASSY品库 / 追加工现场库 等）
    StageCode          NVARCHAR(20) NULL,                     -- 🆕 v5.0.16：APS 增强列；ProcessCode → StageCode 基础映射；软引用 StageDict（不强 FK，避免阶段未注册时阻塞）
    CodeOrigin         NVARCHAR(20) NOT NULL DEFAULT 'MANUAL' -- 🔄 v5.0.16 RENAME from SourceSystem；语义=条目业务来源标记
        CONSTRAINT CK_ProcessCodeDict_Origin CHECK (CodeOrigin IN (N'ERP', N'MES', N'MANUAL')),
    ERPProperty        NVARCHAR(20) NULL,                     -- 🆕 v5.0.46：仓库/工序位置业务属性；值域 M/XC/ZP/BP；来源于ERP真实属性，由5号位同步/透出维护；2号位通过 MES_ProcessCode_View 消费；不根据 WarehouseRole/ProcessName 推导
    IsActive           BIT NOT NULL DEFAULT 1,
    UpdatedBy          NVARCHAR(100) NULL,                    -- 🆕 v5.0.16：维护人工号（替代 LastSyncedAt 同步时间戳的语义）
    UpdatedAt          DATETIME2 NOT NULL DEFAULT GETDATE()
    -- LastSyncedAt：v5.0.16 已删除（无自动同步）
);
GO

CREATE INDEX IX_ProcessCodeDict_Factory   ON ProcessCodeDict(FactoryCode) WHERE IsActive = 1;
CREATE INDEX IX_ProcessCodeDict_Actual    ON ProcessCodeDict(ActualFactoryCode) WHERE IsActive = 1 AND ActualFactoryCode IS NOT NULL;
CREATE INDEX IX_ProcessCodeDict_StageCode ON ProcessCodeDict(StageCode)   WHERE IsActive = 1 AND StageCode IS NOT NULL;  -- 🆕 v5.0.16：5 号位反查"哪些 ProcessCode 归属 X StageCode"
GO

-- ⚠️ 维护方式见上方 §3.12 注释（APS增强字段人工维护 + ERPProperty 5号位同步，两类分离）
-- 维护流程：管理员录入条目 → 0 号位审批（含 StageCode 归属确认）→ IsActive=1 启用
-- APS增强字段变更（含StageCode/CodeOrigin/UpdatedBy/IsActive等）必须经0号位审批，不得被自动同步覆盖。ERPProperty来源于ERP真实属性，由5号位专用同步/透出维护；ERPProperty专用同步只更新ERPProperty，不覆盖APS增强字段。sp_SyncMasterData(@SourceType='ProcessCode')不恢复

-- v5.0.19 初始化数据（基于工序对照表.xlsx，2026-05-06 生成；152 条唯一 ProcessCode）
-- ⚠️ 3 个重复 ProcessCode（010593/020593/070693）：原工序与受托库共用同一码，取第一条；受托库版本见末尾注释
INSERT INTO ProcessCodeDict (ProcessCode, ProcessName, FactoryCode, ActualFactoryCode, TrusteeProcCode, IsOutsource, IsRetouch, WarehouseRole, StageCode, CodeOrigin) VALUES
-- BJ 工厂（65 条）
(N'010398', N'BJ_8课_磁环M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MOLD', N'ERP'),
(N'010399', N'BJ_8课_磁环注塑现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MOLD', N'ERP'),
(N'010494', N'BJ_2课_CYL外协库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'010495', N'BJ_CYL_底板中间在库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'010496', N'BJ_2-6课_追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'010498', N'BJ_2课_气缸加工M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MACH', N'ERP'),
(N'010499', N'BJ_2-6课_加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'010593', N'BJ_3课_管接头组装现场库_KV', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_ASSY', N'ERP'),
(N'010598', N'BJ_8课_CYL注塑M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MOLD', N'ERP'),
(N'010599', N'BJ_6课_气缸涂装现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_SURF', N'ERP'),
(N'010699', N'BJ_6课_气缸氧化现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_SURF', N'ERP'),
(N'011298', N'BJ_7课_气缸铸造M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_CAST', N'ERP'),
(N'011299', N'BJ_7课_气缸铸造现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_CAST', N'ERP'),
(N'011494', N'BJ_2课_CYL第二外协库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'011799', N'BJ_7课气缸铸造喷丸现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_CAST', N'ERP'),
(N'012494', N'BJ_2课_CYL第三外协库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'012496', N'BJ_2-4课_追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'012499', N'BJ_2-4课_加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'013499', N'BJ_2-9课_加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'014499', N'BJ_2-8课_加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'015496', N'BJ_2-10课_追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'015499', N'BJ_2-10课_加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'020396', N'BJ_8课_外协现场库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'020398', N'BJ_8课_M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MOLD', N'ERP'),
(N'020399', N'BJ_8课_三联件注塑现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MOLD', N'ERP'),
(N'020494', N'BJ_5课外协加工现场库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'020498', N'BJ_5课_三联件加工M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MACH', N'ERP'),
(N'020499', N'BJ_5课_三联件加工现场', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'020596', N'BJ_6课_追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'020599', N'BJ_6课_三联件涂装现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_SURF', N'ERP'),
(N'020796', N'BJ_5课_FRL ASSY品', N'BJ', N'BJ', NULL, 0, 0, N'ASSY品库', N'BJ_ASSY', N'ERP'),
(N'020797', N'BJ_5课_FRL AC单体', N'BJ', N'BJ', NULL, 0, 0, N'ASSY品库', N'BJ_ASSY', N'ERP'),
(N'020799', N'BJ_5课_三联件组装现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_ASSY', N'ERP'),
(N'020899', N'BJ_8课ASSY现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_ASSY', N'ERP'),
(N'021298', N'BJ_7课_三联件铝铸造M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_CAST', N'ERP'),
(N'021299', N'BJ_7课_三联件铝铸造现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_CAST', N'ERP'),
(N'021398', N'BJ_7课_三联件锌铸造M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_CAST', N'ERP'),
(N'021399', N'BJ_7课_三联件锌铸造现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_CAST', N'ERP'),
(N'021498', N'BJ_7课_铝阀体铸造M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_CAST', N'ERP'),
(N'021598', N'BJ_7课_阀体锌铸造M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_CAST', N'ERP'),
(N'030398', N'BJ_3课_管接头注塑M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MOLD', N'ERP'),
(N'030399', N'BJ_8课_管接头注塑成型现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MOLD', N'ERP'),
(N'030494', N'BJ_3课外协现场库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'030496', N'BJ_3课_管接头外协黄铜棒M工号库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'030498', N'BJ_3课_管接头加工M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_MACH', N'ERP'),
(N'030499', N'BJ_3课_管接头加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'030699', N'BJ_6课_管接头氧化现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_SURF', N'ERP'),
(N'030799', N'BJ_3课_管接头组装现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_ASSY', N'ERP'),
(N'031494', N'BJ_3课_第二外协现场库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'032494', N'BJ_3课_第三外协现场库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'040196', N'BJ_1课_追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'040198', N'BJ_物流课_型材挤压M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_EXTRU', N'ERP'),
(N'040199', N'BJ_1课_型材挤压现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_EXTRU', N'ERP'),
(N'040494', N'BJ_1课铝棒外协库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'041198', N'BJ_1课_熔铝铸造M工号库', N'BJ', N'BJ', NULL, 0, 0, N'入库M工号', N'BJ_CAST', N'ERP'),
(N'041299', N'BJ_1课_热处理现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_FORGE', N'ERP'),
(N'050796', N'BJ_4课_冷干机内制品组装库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_ASSY', N'ERP'),
(N'050799', N'BJ_4课_冷干机组装现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_ASSY', N'ERP'),
(N'060594', N'BJ_6课_涂装外协库', N'BJ', N'BJ', NULL, 1, 0, N'外协发货库', N'BJ_OUTS', N'ERP'),
(N'070496', N'BJ_2-7课_追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'070499', N'BJ_2-7课_加工现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_MACH', N'ERP'),
(N'070699', N'BJ_6课_长尺氧化现场库', N'BJ', N'BJ', NULL, 0, 0, N'现场库', N'BJ_SURF', N'ERP'),
(N'071496', N'BJ_2-7课_第二追加工现场库', N'BJ', N'BJ', NULL, 0, 1, N'追加工现场库', N'BJ_MACH', N'ERP'),
(N'990201', N'BJ_出口制品', N'BJ', N'BJ', NULL, 0, 0, N'出口库', N'BJ_FINAL', N'ERP'),
(N'990202', N'BJ_出口部品', N'BJ', N'BJ', NULL, 0, 0, N'出口库', N'BJ_FINAL', N'ERP'),
-- CN 工厂（32 条）
(N'070693', N'CN_3课_外协现场库', N'CN', N'CN', NULL, 1, 0, N'外协发货库', N'CN_OUTS', N'ERP'),
(N'510195', N'CN_CYL_CQ追加工现场库', N'CN', N'CN', NULL, 0, 1, N'追加工现场库', N'CN_MACH', N'ERP'),
(N'510297', N'CN2-2课CQ切断库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'510298', N'CN_CG缸筒挤压M工号', N'CN', N'CN', NULL, 0, 0, N'入库M工号', N'CN_FORGE', N'ERP'),
(N'510299', N'CN_CG缸筒挤压现场', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_FORGE', N'ERP'),
(N'510489', N'CN_2-1课_加工现场库(课税)', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'510496', N'CN_3课_ASSY品库', N'CN', N'CN', NULL, 0, 0, N'ASSY品库', N'CN_ASSY', N'ERP'),
(N'510498', N'CN_3课_加工M工号库', N'CN', N'CN', NULL, 0, 0, N'入库M工号', N'CN_MACH', N'ERP'),
(N'510499', N'CN_2-1课_加工现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'510593', N'CN_2-3课_CYL涂装受托库', N'CN', N'CN', NULL, 0, 0, N'受托库', N'CN_SURF', N'ERP'),
(N'510599', N'CN_2-3课_CYL涂装现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_SURF', N'ERP'),
(N'510693', N'CN_2-3课_CYL氧化受托库', N'CN', N'CN', NULL, 0, 0, N'受托库', N'CN_SURF', N'ERP'),
(N'510699', N'CN_2-3课_CYL氧化现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_SURF', N'ERP'),
(N'510789', N'CN_3课_组装现场库(课税)', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_ASSY', N'ERP'),
(N'510799', N'CN_3课_组装现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_ASSY', N'ERP'),
(N'510899', N'CN_拉拔现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'511498', N'CN_外协现场库', N'CN', N'CN', NULL, 1, 0, N'外协发货库', N'CN_OUTS', N'ERP'),
(N'511499', N'CN_2-2课_加工现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'512498', N'CN_外协第二现场库', N'CN', N'CN', NULL, 1, 0, N'外协发货库', N'CN_OUTS', N'ERP'),
(N'513495', N'CN_2-5课_CYL追加工现场库', N'CN', N'CN', NULL, 0, 1, N'追加工现场库', N'CN_MACH', N'ERP'),
(N'513498', N'CN_外协第三现场库', N'CN', N'CN', NULL, 1, 0, N'外协发货库', N'CN_OUTS', N'ERP'),
(N'513499', N'CN_2-5课_CYL加工现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'520593', N'CN_2-3课_FRL涂装受托库', N'CN', N'CN', NULL, 0, 0, N'受托库', N'CN_SURF', N'ERP'),
(N'530496', N'CN_5课_ASSY品库', N'CN', N'CN', NULL, 0, 0, N'ASSY品库', N'CN_ASSY', N'ERP'),
(N'530498', N'CN_5课_加工M工号库', N'CN', N'CN', NULL, 0, 0, N'入库M工号', N'CN_MACH', N'ERP'),
(N'530499', N'CN_2-5课_DCF加工现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_MACH', N'ERP'),
(N'530789', N'CN_5课_组装现场库(课税)', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_ASSY', N'ERP'),
(N'530797', N'CN_5课_第二ASSY品库', N'CN', N'CN', NULL, 0, 0, N'ASSY品库', N'CN_ASSY', N'ERP'),
(N'530799', N'CN_5课_组装现场库', N'CN', N'CN', NULL, 0, 0, N'现场库', N'CN_ASSY', N'ERP'),
(N'533798', N'CN_5课_外协现场库', N'CN', N'CN', NULL, 1, 0, N'外协发货库', N'CN_OUTS', N'ERP'),
(N'980201', N'CN_出口制品', N'CN', N'CN', NULL, 0, 0, N'出口库', N'CN_FINAL', N'ERP'),
(N'980202', N'CN_出口部品', N'CN', N'CN', NULL, 0, 0, N'出口库', N'CN_FINAL', N'ERP'),
-- CN6课 工厂（9 条）
(N'510495', N'CN_3课_特注加工M工号库(课税)', N'CN6课', N'CN6课', NULL, 0, 0, N'入库M工号', N'CN6_MACH', N'ERP'),
(N'530495', N'CN_5课_特注加工M工号库(课税)', N'CN6课', N'CN6课', NULL, 0, 0, N'入库M工号', N'CN6_MACH', N'ERP'),
(N'540488', N'CN_6课_加工M工号库(课税)', N'CN6课', N'CN6课', NULL, 0, 0, N'入库M工号', N'CN6_MACH', N'ERP'),
(N'540489', N'CN_6课_加工现场库(课税)', N'CN6课', N'CN6课', NULL, 0, 0, N'现场库', N'CN6_MACH', N'ERP'),
(N'540495', N'CN_6课电镀现场库', N'CN6课', N'CN6课', NULL, 1, 0, N'现场库', N'CN6_OUTS', N'ERP'),
(N'540499', N'CN_6课_加工现场库', N'CN6课', N'CN6课', NULL, 0, 0, N'现场库', N'CN6_MACH', N'ERP'),
(N'540789', N'CN_6课_组装现场库(课税)', N'CN6课', N'CN6课', NULL, 0, 0, N'现场库', N'CN6_ASSY', N'ERP'),
(N'540796', N'CN_6课_ASSY品库', N'CN6课', N'CN6课', NULL, 0, 0, N'ASSY品库', N'CN6_ASSY', N'ERP'),
(N'540797', N'CN_6课_内制品库', N'CN6课', N'CN6课', NULL, 0, 0, N'现场库', N'CN6_MACH', N'ERP'),
-- TJ 工厂（39 条）
(N'020593', N'TJ_2课_CYL切断库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_MACH', N'ERP'),
(N'310494', N'TJ_2课_CYL加工外协库', N'TJ', N'TJ', NULL, 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'310495', N'TJ_2课_CYL追加工现场库', N'TJ', N'TJ', NULL, 0, 1, N'追加工现场库', N'TJ_MACH', N'ERP'),
(N'310496', N'TJ_3课CYL ASSY品', N'TJ', N'TJ', NULL, 0, 0, N'ASSY品库', N'TJ_ASSY', N'ERP'),
(N'310498', N'TJ_3课_CYL加工M工号库', N'TJ', N'TJ', NULL, 0, 0, N'入库M工号', N'TJ_MACH', N'ERP'),
(N'310499', N'TJ_2课_CYL加工现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_MACH', N'ERP'),
(N'310594', N'TJ_6课_CYL涂装外协库（BJ）', N'TJ', N'BJ', N'010593', 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'310599', N'TJ_6课_CYL涂装现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_SURF', N'ERP'),
(N'310694', N'TJ_6课_CYL氧化外协库（CN）', N'TJ', N'CN', N'510693', 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'310699', N'TJ_6课_CYL氧化现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_SURF', N'ERP'),
(N'310789', N'TJ_3课_CYL组装课税现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_ASSY', N'ERP'),
(N'310799', N'TJ_3课_CYL组装现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_ASSY', N'ERP'),
(N'311294', N'TJ_7课_外协库', N'TJ', N'TJ', NULL, 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'311298', N'TJ_7课_铸造M工号库', N'TJ', N'TJ', NULL, 0, 0, N'入库M工号', N'TJ_CAST', N'ERP'),
(N'311299', N'TJ_7课_铸造现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_CAST', N'ERP'),
(N'311494', N'TJ_2课_CYL加工第二外协库', N'TJ', N'TJ', NULL, 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'311594', N'TJ_6课_CYL涂装外协库（CN）', N'TJ', N'CN', N'510593', 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'320494', N'TJ_2课_FRL加工外协库', N'TJ', N'TJ', NULL, 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'320498', N'TJ_5课_FRL加工M工号库', N'TJ', N'TJ', NULL, 0, 0, N'入库M工号', N'TJ_MACH', N'ERP'),
(N'320499', N'TJ_2课_FRL加工现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_MACH', N'ERP'),
(N'320594', N'TJ_6课_FRL涂装外协库（BJ）', N'TJ', N'BJ', N'020593', 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'320597', N'TJ_6课_受托中间库', N'TJ', N'TJ', NULL, 0, 0, N'中间库', N'TJ_MACH', N'ERP'),
(N'320599', N'TJ_6课_FRL涂装现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_SURF', N'ERP'),
(N'320796', N'TJ_5课_FRL ASSY品', N'TJ', N'TJ', NULL, 0, 0, N'ASSY品库', N'TJ_ASSY', N'ERP'),
(N'320797', N'TJ_5课_FRL AC单体', N'TJ', N'TJ', NULL, 0, 0, N'ASSY品库', N'TJ_ASSY', N'ERP'),
(N'320799', N'TJ_5课_FRL组装现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_ASSY', N'ERP'),
(N'321594', N'TJ_6课_FRL涂装外协库（CN）', N'TJ', N'CN', N'520593', 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'340198', N'TJ_物流课_型材挤压M工号库', N'TJ', N'TJ', NULL, 0, 0, N'入库M工号', N'TJ_MACH', N'ERP'),
(N'340199', N'TJ_1课_型材挤压现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_MACH', N'ERP'),
(N'340494', N'TJ_1课_铝棒外协库', N'TJ', N'TJ', NULL, 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'341198', N'TJ_1课_回炉M工号库', N'TJ', N'TJ', NULL, 0, 0, N'入库M工号', N'TJ_MACH', N'ERP'),
(N'350494', N'TJ_4课_冷干机外协现场库', N'TJ', N'TJ', NULL, 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'350498', N'TJ_4课_冷干机M工号库', N'TJ', N'TJ', NULL, 0, 0, N'入库M工号', N'TJ_ASSY', N'ERP'),
(N'350799', N'TJ_4课_冷干机组装现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_ASSY', N'ERP'),
(N'351499', N'TJ_4课_冷干机加工现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_MACH', N'ERP'),
(N'370694', N'TJ_6课_长尺外协库', N'TJ', N'BJ', N'070693', 1, 0, N'外协发货库', N'TJ_OUTS', N'ERP'),
(N'370699', N'TJ_6课_长尺氧化现场库', N'TJ', N'TJ', NULL, 0, 0, N'现场库', N'TJ_SURF', N'ERP'),
(N'960201', N'TJ_出口制品', N'TJ', N'TJ', NULL, 0, 0, N'出口库', N'TJ_FINAL', N'ERP'),
(N'960202', N'TJ_出口部品', N'TJ', N'TJ', NULL, 0, 0, N'出口库', N'TJ_FINAL', N'ERP'),
-- SH 工厂（7 条）
(N'640488', N'SH_加工M工号库(课税)', N'SH', N'SH', NULL, 0, 0, N'入库M工号', N'SH_MACH', N'ERP'),
(N'640489', N'SH_加工现场库(课税)', N'SH', N'SH', NULL, 0, 0, N'现场库', N'SH_MACH', N'ERP'),
(N'640495', N'SH_电镀现场库', N'SH', N'SH', NULL, 1, 0, N'现场库', N'SH_OUTS', N'ERP'),
(N'640789', N'SH_组装现场库(课税)', N'SH', N'SH', NULL, 0, 0, N'现场库', N'SH_ASSY', N'ERP'),
(N'640796', N'SH_特注品_ASSY品库', N'SH', N'SH', NULL, 0, 0, N'ASSY品库', N'SH_ASSY', N'ERP'),
(N'970201', N'SH_出口制品', N'SH', N'SH', NULL, 0, 0, N'出口库', N'SH_FINAL', N'ERP'),
(N'970202', N'SH_出口部品', N'SH', N'SH', NULL, 0, 0, N'出口库', N'SH_FINAL', N'ERP');
GO

-- ⚠️ 以下 3 个 ProcessCode 在工序对照表中存在重复行（受托库版本），未入 INSERT：
--   010593: BJ_6课_CYL涂装受托库 / BJ表面 / 工厂=BJ / 实际=BJ / 受托库
--   020593: BJ_6课_FRL涂装受托库 / BJ表面 / 工厂=BJ / 实际=BJ / 受托库
--   070693: BJ_6课_长尺受托库 / BJ表面 / 工厂=BJ / 实际=BJ / 受托库

-- =============================================
-- 1.9f MES_ProcessCode_View（v5.0.15 新增，ODS 契约视图；Socket-Plug）
-- 定位：ProcessCodeDict 物理表的**防腐契约投影**；所有 ODS 内部消费方一律查本视图，**禁止直查物理表**
-- 吸震点：ERP 升级导致工序对照表列改名/增列时，由 DBA 改本视图 SELECT 别名吸收，消费方代码零改动
-- 同级物：MES_BOM_View / ERP_Master_View / MES_Material_View（均为 Socket-Plug 契约视图）
-- 字段契约（对 ODS 内部承诺稳定，v5.0.16 升级）：
--   ProcessCode / ProcessName / FactoryCode / ActualFactoryCode / TrusteeProcCode
--   IsOutsource / IsRetouch / WarehouseRole / StageCode（🆕 v5.0.16）/ CodeOrigin（🔄 v5.0.16 RENAME from SourceSystem）
-- 消费边界：仅 ODS 内部（5 号位 sp_EnrichBOMWorkset / 2 号位 sp_RebuildMaterialStageDeptContext / vw_MES_BOM_Stage_Enriched / 运维诊断）
-- 🔑 v5.0.16 关键约定：本视图的 StageCode 列是 5 号位与 2 号位**共享的基础映射来源**——两边的 ProcessCode→StageCode 必须查同一列，禁止各写一套规则
-- =============================================

CREATE OR ALTER VIEW dbo.MES_ProcessCode_View AS
SELECT
    ProcessCode,
    ProcessName,
    FactoryCode,
    ActualFactoryCode,
    TrusteeProcCode,
    IsOutsource,
    IsRetouch,
    WarehouseRole,
    StageCode,    -- 🆕 v5.0.16：APS 增强列；ProcessCode → StageCode 共享基础映射
    CodeOrigin,   -- 🔄 v5.0.16 RENAME：原 SourceSystem
    ERPProperty   -- 🆕 v5.0.46：仓库/工序位置业务属性，值域 M/XC/ZP/BP
FROM ProcessCodeDict
WHERE IsActive = 1;
GO

-- =============================================
-- 1.10 vw_MES_BOM_Stage_Enriched（v5.0.10 新增；v5.0.15 重写：BOM 边粒度 + 修 JOIN 键 bug）
-- ⚠️ 定位：**非防腐层视图**，是 ODS 内部的便利查询层（C 类派生视图）
--   - 不是 Socket-Plug 契约视图；不是 ext_ 跨库包装视图
--   - 仅 ODS 内部（跨厂/委外事实推导、运维诊断、BI 报表）使用；V1不生成ShippingTask
--   - **APS 本地不做对称视图**：避免 ERP 特征字段（ProcessCode/ActualFactoryCode/TrusteeProcCode）下沉到 APS 排程内核
--
-- ⚠️ v5.0.15 重写原因：
--   原 v5.0.10 设计 JOIN 键为 StageDetail.StageCode = ProcessCodeDict.StageCode，但两者值域完全不同：
--     · StageDetail.StageCode = 聚合大阶段码（如 TJ_MACH），值域来自 StageDict
--     · ProcessCodeDict 主键 = 6 位 ProcessCode（如 010496）
--   且一个大阶段对应 N 个 ProcessCode（1:N），根本无法 1:1 JOIN 派生
--   文档 §7.5 明确：工序级字段只在 BOM 行级上下文（GoodsProcCode/MaterialProcCode）可达
--
-- ✅ v5.0.15 新设计：视图主体改为 **BOM 边粒度**——直接基于 MES_BOM_View × MES_ProcessCode_View JOIN
--   · 父件工序派生：bv.GoodsProcCode → pc_g.ProcessCode 拿到父件完成位的厂/实际厂/受托码
--   · 子件工序派生：bv.MaterialProcCode → pc_m.ProcessCode 拿到子件领料位的厂/仓库角色
--   · 可选附带聚合 StageCode：LEFT JOIN StageDetail（按 BatchNo+BOMNO+Parent+Child 四元组）
--
-- 依赖：MES_BOM_Edge_Active（v5.0.26c 清稿，不走 MES_BOM_View）+ MES_ProcessCode_View（§1.9f）+ MES_APS_BOM_Workset_StageDetail（可选）
-- =============================================

CREATE VIEW vw_MES_BOM_Stage_Enriched AS
SELECT
    -- BOM 边稳定字段（来自 MES_BOM_View Socket-Plug 契约）
    bv.BOMNO,
    bv.ParentMaterialCode,
    bv.ChildMaterialCode,
    bv.Quantity,
    bv.ParentProcRefCode,            -- 父件工序辅助码（BOM 边原生）
    bv.ChildProcRefCode,             -- 子件工序辅助码（BOM 边原生）
    bv.ChildSourceHintCode,          -- Produce 原值（0-11）
    -- 父件工序派生（来自 ProcessCode 字典）
    pc_g.ProcessCode        AS ParentProcessCode,
    pc_g.FactoryCode        AS ParentStageFactory,       -- 父件账面工厂（R26 过滤维度）
    pc_g.ActualFactoryCode  AS ParentActualFactory,      -- 父件实际加工厂（含受托，委外识别用）
    pc_g.TrusteeProcCode    AS ParentTrusteeProcCode,    -- 父件受托对方工艺码
    pc_g.IsOutsource        AS ParentIsOutsource,
    -- 子件工序派生（来自 ProcessCode 字典）
    pc_m.ProcessCode        AS ChildProcessCode,
    pc_m.FactoryCode        AS ChildStageFactory,
    pc_m.ActualFactoryCode  AS ChildActualFactory,
    pc_m.TrusteeProcCode    AS ChildTrusteeProcCode,
    pc_m.IsRetouch          AS ChildIsRetouch,
    pc_m.WarehouseRole      AS ChildWarehouseRole,
    -- 可选：聚合大阶段码（5 号位派生结果，便于审计关联回 StageDetail）
    sd.BatchNo,
    sd.StageScopeType,
    sd.StageSeq,
    sd.StageCode            AS AggregatedStageCode,      -- StageDict 值域（APS 语义）
    sd.IsSupplyThreshold
FROM MES_BOM_Edge_Active bv          -- ⚡ 直读物化边表（v5.0.26c 清稿）
LEFT JOIN MES_ProcessCode_View pc_g
    ON pc_g.ProcessCode = bv.ParentProcRefCode           -- 父件完成位工序码（= MES_BOM_View.GoodsProcCode）
LEFT JOIN MES_ProcessCode_View pc_m
    ON pc_m.ProcessCode = bv.ChildProcRefCode            -- 子件领料位工序码（= MES_BOM_View.MaterialProcCode）
LEFT JOIN MES_APS_BOM_Workset_StageDetail sd
    ON  sd.BOMNO              = bv.BOMNO
    AND sd.ParentMaterialCode = bv.ParentMaterialCode
    AND sd.ChildMaterialCode  = bv.ChildMaterialCode
    AND sd.StageScopeType     = 'EDGE';                  -- 仅对接 EDGE 记录；ROOT 不在此视图体现
GO

-- ⚠️ 消费方提示：
--   · APS 排程内核禁止查询本视图（委外/受托语义由 2 号位预计算落 StageLeadTimeParam，不直接下沉 ERP 字段）
--   · 跨厂/委外事实诊断：按 ParentStageFactory ≠ ParentActualFactory（或子件侧同理）识别跨组织事实；V1不生成ShippingTask。
--   · BI 报表：按 ParentStageFactory / StageScopeType 等维度汇总

-- =============================================
-- §1.10b ERP_InterplantInTransit_View（ODS 契约视图 — v5.1.2 冻结对齐）
-- =============================================
-- 所属层/库：ODS / MES_Integration；维护责任人：5号位。
-- V1正式能力：必须输出真实厂间在途，不得继续用 WHERE 1=0 空契约作为正式实现。
-- 本通用DDL只冻结14字段契约，不猜ERP真实源表/JOIN。5号位/ERP DBA必须在部署前提供实际 CREATE OR ALTER VIEW。
-- 字段顺序/类型/语义保持：
-- MasterID INT, MaterialCode NVARCHAR(100), SourceFactoryCode NVARCHAR(50), FactoryCode NVARCHAR(50),
-- SupplyType NVARCHAR(50), OwnershipType NVARCHAR(20), QualityStatus NVARCHAR(20), Quantity DECIMAL(18,4),
-- ETA DATETIME2, StorageCode NVARCHAR(50), SupplierCode NVARCHAR(50), SourceDocumentNo NVARCHAR(100),
-- SourceDocumentLineNo NVARCHAR(50), SourceUpdatedAt DATETIME2。
-- 红线：Quantity=剩余在途量；FactoryCode=目的工厂；ETA=ERP原始事实；已收货/关闭/取消/剩余0不得输出。
--
-- ⚠️ 这里故意不创建空View。若真实View未部署，APS包装视图只打印部署阻断提示，sp_SyncPipelineSupply运行时会失败。
GO

-- =============================================
-- §1.11 MaterialProductFamilyScopeRule
-- ⚠️ V2 规则资产化预留，V1 不建
-- V1 产品族判断逻辑由 5号位封装在 ERP_Master_View 内部（ODS 内部逻辑，不暴露给 APS 层）
-- V2 若规则增多或需要业务可配置，再新建本表
-- =============================================

-- (V1 不执行 CREATE TABLE)

-- =============================================
-- §1.12 MaterialProductFamilyRule
-- ⚠️ V2 预留：V1 产品族判断逻辑封装在 ERP_Master_View / ODS 内部逻辑中
-- V2 若规则增多或需要业务可配置，再抽象为 MaterialProductFamilyRule
-- =============================================

-- (V1 不执行 CREATE TABLE)

-- =============================================
-- §1.13 MaterialProductFamilyResolved
-- ⚠️ V2 预留：V1 不建独立解析结果表
-- V1 直接通过 ERP_Master_View 输出 ProductFamilyCode / FamilyResolveStatus
-- =============================================

-- (V1 不执行 CREATE TABLE)

-- =============================================
-- 第二部分: APS_Production本地库（计算标准层）
-- =============================================

-- 创建APS本地库
CREATE DATABASE APS_Production
ON PRIMARY 
(
    NAME = APS_Data,
    FILENAME = 'E:\SSD\APS_Production.mdf',  -- ⚠️ SSD路径，高频I/O
    SIZE = 10GB,
    MAXSIZE = UNLIMITED,
    FILEGROWTH = 1GB
)
LOG ON 
(
    NAME = APS_Log,
    FILENAME = 'E:\SSD\APS_Production_log.ldf',  -- ⚠️ SSD路径
    SIZE = 5GB,
    MAXSIZE = 50GB,
    FILEGROWTH = 512MB
);
GO

USE APS_Production;
GO

-- 启用 Snapshot Isolation (RCSI)
ALTER DATABASE APS_Production SET READ_COMMITTED_SNAPSHOT ON;
ALTER DATABASE APS_Production SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

-- =============================================
-- 2.0 跨库寻址包装层（Wrapper Views）- P0-6修复（修复日期：2026-03-11）
-- =============================================
-- ⚠️ 作用：将ODS库的防腐契约视图透明地映射到当前计算库，隔离跨库强耦合
-- ⚠️ 优势：避免在存储过程里写死丑陋的三段式命名（[DB].[dbo].[Table]）
-- ⚠️ 优势：把跨库权限管控集中在了视图层，DBA部署时不会抓狂

-- 1. 指向ODS的ERP主数据视图
-- v5.0.38 升级：显式列列表，透传 IsProductFamilyRequired / ProductFamilyCode / FamilyResolveStatus
CREATE VIEW ext_ERP_Master_View AS
SELECT
    MaterialCode,
    MaterialName,
    Spec,
    MasterID,
    Warehouse,
    SupplyMode,
    ProductionDeptCode,
    UOM,
    LeadTimeDays,
    SafetyStock,
    InventoryManagementMode,
    IsActive,
    IsProductFamilyRequired,    -- v5.0.38：该物料是否需要产品族（5号位 ODS 内部判断，不暴露 ERP 原始字段）
    ProductFamilyCode,          -- v5.0.38：ODS 内部解析出的产品族编码（RESOLVED 时有值，其余 NULL）
    FamilyResolveStatus         -- v5.0.38：RESOLVED/NOT_REQUIRED/NO_RULE/AMBIGUOUS/FAMILY_CODE_NOT_FOUND/SOURCE_FIELD_MISSING
FROM [MES_Integration].[dbo].[ERP_Master_View];
GO

-- 2. 指向ODS的MES自建物料视图
-- v5.0.37 升级：SELECT * → 显式列列表（MES_ID→MasterID, Location→Warehouse 别名）
-- v5.0.38 升级：新增 IsProductFamilyRequired 同构透传（MES 侧 V1 固定返回 CAST(0 AS BIT)）
CREATE VIEW ext_MES_Material_View AS
SELECT
    MaterialCode,
    MaterialName,
    Spec,
    MES_ID          AS MasterID,           -- 同构别名（对齐 ext_ERP_Master_View.MasterID）
    Location        AS Warehouse,          -- 同构别名（对齐 ext_ERP_Master_View.Warehouse）
    SupplyMode,
    ProductionDeptCode,
    UOM,
    LeadTimeDays,
    SafetyStock,
    InventoryManagementMode,
    IsActive,
    IsProductFamilyRequired,               -- v5.0.38：MES 侧 V1 固定返回 0（同构透传）
    ProductFamilyCode,                     -- v5.0.38：MES 侧 V1 固定返回 NULL（同构透传）
    FamilyResolveStatus                    -- v5.0.38：MES 侧 V1 固定返回 'NOT_REQUIRED'（同构透传）
FROM [MES_Integration].[dbo].[MES_Material_View];
GO

-- 注释：在APS本地库的统一同步存储过程 sp_SyncMasterData 中，（2026-04-01 v4.0更新）
-- 按 @SourceType 参数分别查询 ext_ERP_Master_View 或 ext_MES_Material_View。
-- 双源同构契约：两个视图字段完全一致，SP逻辑零分叉。

-- 3. 指向ODS的资源主数据视图（v5.0新增；v5.0.13 重命名 ext_APS_Resource_View → ext_MES_APS_Resource_View，与 MES_APS_Routing_*_View 系列命名对齐）
CREATE VIEW ext_MES_APS_Resource_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Resource_View];
GO

-- 3b. （v5.0.13 预留）未来 EAM 上线时由 2 号位在 APS 库再建一张同构包装视图：
--     CREATE VIEW ext_EAM_APS_Resource_View AS SELECT * FROM [EAM_Integration].[dbo].[EAM_APS_Resource_View];
--     sp_SyncResourceData(@SourceType='EAM') 分支直接读此视图；字段契约必须与 MES 侧完全一致（零分叉原则）。

-- 4. 指向ODS的工序节点视图（v5.0新增，替代原MES_APS_Routing_View）
-- v5.0.1变更（2026-04-02）：ODS视图输出MES_ID+Model（非MaterialCode），2号位装载时通过MaterialMapping映射为MaterialId
CREATE VIEW ext_MES_APS_Routing_Operation_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Operation_View];
GO

-- 5. 指向ODS的工序依赖视图（v5.0新增）
-- v5.0.1变更（2026-04-02）：同上，MES_ID+Model
CREATE VIEW ext_MES_APS_Routing_Dependency_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Dependency_View];
GO

-- 6. 指向ODS的工序资源能力视图（v5.0新增）
-- v5.0.1变更（2026-04-02）：同上，MES_ID+Model
CREATE VIEW ext_APS_OperationResourceEligibility_View AS
SELECT * FROM [MES_Integration].[dbo].[APS_OperationResourceEligibility_View];
GO

-- 注释：v5.0资源与工艺重设计（2026-04-01，v5.0.1補正 2026-04-02，v5.0.13 命名统一 2026-04-25）
-- Resource：通过 ext_MES_APS_Resource_View 同步，每天全量刷新（sp_SyncResourceData @SourceType='MES'；v1 未实现 EAM 分支）
-- RoutingOperation：通过 ext_MES_APS_Routing_Operation_View 增量Upsert（视图输出MES_ID+Model，装载时映射MaterialId）
-- RoutingDependency：通过 ext_MES_APS_Routing_Dependency_View 增量Upsert（同上）
-- OperationResourceEligibility：通过 ext_APS_OperationResourceEligibility_View 增量Upsert（同上）

-- 7. 指向ODS的ERP库存视图（v5.0.39 新增）
-- 契约字段：MasterID, WarehouseCode, FactoryCode, Quantity（最小契约；ODS 5号位负责实现）
CREATE VIEW ext_ERP_Inventory_View AS
SELECT * FROM [MES_Integration].[dbo].[ERP_Inventory_View];
GO

-- 8. 指向ODS的MES库存视图（v5.0.39 新增）
-- 契约字段：MES_ID, WarehouseCode, LocationCode, FactoryCode, Quantity
-- V1 主链：WarehouseCode；LocationCode 仅追溯，不参与 InventoryFact_MES 主链计算
CREATE VIEW ext_MES_Inventory_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_Inventory_View];
GO

-- =============================================
-- 9. 指向ODS的ERP厂间在途包装视图（v5.0.42 重建：显式列字段 + 14字段契约）
-- =============================================
-- 所属层：APS 层
-- 所属库：APS_Production
-- 来源：MES_Integration.dbo.ERP_InterplantInTransit_View
-- 维护责任人：2号位
-- ⚠️ 契约锁定规则：V1.1/V2 仅允许替换视图 FROM 逻辑，
--    禁止修改字段顺序、字段类型、字段名称。
-- v5.1.2：仅当5号位真实ODS契约已存在时创建包装视图；不得自动造空数据源。
IF EXISTS (
    SELECT 1
    FROM [MES_Integration].sys.views v
    JOIN [MES_Integration].sys.schemas s ON s.schema_id = v.schema_id
    WHERE s.name = N'dbo' AND v.name = N'ERP_InterplantInTransit_View'
)
BEGIN
    EXEC(N'CREATE OR ALTER VIEW dbo.ext_ERP_InterplantInTransit_View AS
          SELECT MasterID, MaterialCode, SourceFactoryCode, FactoryCode,
                 SupplyType, OwnershipType, QualityStatus, Quantity, ETA,
                 StorageCode, SupplierCode, SourceDocumentNo,
                 SourceDocumentLineNo, SourceUpdatedAt
          FROM [MES_Integration].[dbo].[ERP_InterplantInTransit_View];');
END
ELSE
BEGIN
    PRINT N'V1_DEPLOY_BLOCKER: MES_Integration.dbo.ERP_InterplantInTransit_View尚未由5号位绑定真实ERP来源；未创建空包装视图。';
END;
GO

-- =============================================
-- 9a. ext_PipelineSupply_Source_View（V1正式Timed Supply统一输入契约）
-- =============================================
-- 所属层/库：APS / APS_Production；维护责任人：2号位，真实来源绑定由5号位/采购DBA共同提供。
-- v5.1.2冻结：厂间在途、采购在途/未结PO、VMI、已到厂未入库均为V1正式能力。
-- 本通用DDL不虚构采购/VMI/到厂未入库的源View名，因此不再生成任何 WHERE 1=0 placeholder 分支。
-- 部署前必须由2号位基于“已存在真实ODS契约”创建/ALTER dbo.ext_PipelineSupply_Source_View。
-- 输出必须固定15列（前14列同Timed Supply契约 + SourceSystem）：
-- MasterID, MaterialCode, SourceFactoryCode, FactoryCode, SupplyType, OwnershipType, QualityStatus,
-- Quantity, ETA, StorageCode, SupplierCode, SourceDocumentNo, SourceDocumentLineNo, SourceUpdatedAt, SourceSystem。
-- 允许某一批次真实0行；不允许“来源没接通”伪装成0行成功。
-- Planning-only Purchase Placeholder不进入本View、不落库，仅由2号位运行时内存生成。
GO

-- =============================================
-- 2.1 分区方案（按 PlanVersionId 分区）
-- =============================================

-- 创建分区函数（支持400个版本，每个版本一个分区）
CREATE PARTITION FUNCTION PF_PlanVersion (INT)
AS RANGE RIGHT FOR VALUES (
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
    11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
    21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
    31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
    41, 42, 43, 44, 45, 46, 47, 48, 49, 50,
    51, 52, 53, 54, 55, 56, 57, 58, 59, 60,
    61, 62, 63, 64, 65, 66, 67, 68, 69, 70,
    71, 72, 73, 74, 75, 76, 77, 78, 79, 80,
    81, 82, 83, 84, 85, 86, 87, 88, 89, 90,
    91, 92, 93, 94, 95, 96, 97, 98, 99, 100
    -- 实际部署时扩展到400
);
GO

-- 创建分区方案（所有分区都在PRIMARY文件组）
CREATE PARTITION SCHEME PS_PlanVersion
AS PARTITION PF_PlanVersion
ALL TO ([PRIMARY]);
GO

-- =============================================
-- 2.2 BOM原始数据表（从ODS拉取的本地缓存）
-- =============================================

CREATE TABLE APS_BOM_RAW (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,  -- ⚠️ 单位用量，不累乘！
    Level INT NULL,
    LLC INT NULL,                                    -- 低阶码（Low Level Code）
    IsLeaf BIT NOT NULL DEFAULT 0,                   -- 是否叶子节点
    Path NVARCHAR(MAX) NULL,
    ChildRequiredStageCode NVARCHAR(50) NULL,           -- v5.0.7 子件供给所需大工艺阶段码（从Workset最终结果透传；NULL=保守策略：全工艺完成后才可供给）
    ChildRequiredFactory NVARCHAR(20) NULL,             -- v5.0.10 子件应归属账面工厂（从Workset透传；CN/CN6课/BJ/TJ/SH/NULL）
    SyncedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_BOM_RAW_Batch 
ON APS_BOM_RAW(BatchNo, ParentMaterialCode);

CREATE NONCLUSTERED INDEX IX_BOM_RAW_LLC 
ON APS_BOM_RAW(BatchNo, LLC, IsLeaf);

CREATE NONCLUSTERED INDEX IX_BOM_RAW_Material 
ON APS_BOM_RAW(ChildMaterialCode, BatchNo);
GO

-- =============================================
-- 2.2b BOM阶段顺序明细本地缓存（v5.0.7新增，从ODS StageDetail拉取）
-- 2号位负责搬运，与APS_BOM_RAW同批次拉取
-- =============================================

CREATE TABLE APS_BOM_STAGE_PATH_RAW (
    Id BIGINT PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    WorksetId BIGINT NULL,                              -- v5.0.26 ODS 侧 Workset.Id（跨库引用，非FK）；2号位搬运时透传；NULL=兼容旧批次
    BOMNO NVARCHAR(50) NOT NULL,
    StageScopeType NVARCHAR(10) NOT NULL DEFAULT 'EDGE', -- v5.0.8
    ParentMaterialCode NVARCHAR(50) NULL,                -- v5.0.8 ROOT=NULL
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    StageSeq INT NOT NULL,
    StageCode NVARCHAR(50) NOT NULL,
    IsSupplyThreshold BIT NOT NULL DEFAULT 0,
    SyncedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE CLUSTERED INDEX IX_StagePath_Batch
ON APS_BOM_STAGE_PATH_RAW(BatchNo, ChildMaterialCode);

CREATE NONCLUSTERED INDEX IX_StagePath_Child
ON APS_BOM_STAGE_PATH_RAW(ChildMaterialCode, StageSeq);
GO

-- =============================================
-- 2.2c 订单-BOM解析结果索引表（v5.0.31新增）
-- =============================================
-- 定位：APS 本地读模型/索引表，非 BOM 明细表
-- 职责：记录某 PlanVersion/BatchNo 下，某 APS Order 最终使用的 BOM 解析结果
--       提供 Order → BOM结构(APS_BOM_RAW) / 大工艺路径(APS_BOM_STAGE_PATH_RAW) 的快速查询入口
-- 2号位在 BOM Workset + StageDetail 同步完成后生成此表记录
-- 查询链路：
--   Order → OrderBomRequestLink.ResolvedBOMNO → APS_BOM_RAW(BatchNo+BOMNO)  ← BOM结构
--   Order → OrderBomRequestLink.RepWorksetId  → APS_BOM_STAGE_PATH_RAW.WorksetId ← 大工艺路径
-- 【设计决策】APS_BOM_RAW 保持 BOMNO 级共享（不订单化），此表承担 Order→BOMNO 的桥接职责
-- 【设计决策】RepWorksetId = MIN(Workset.Id) WHERE RequestDetailId+Level=1，与ROOT StageDetail规则一致

-- v5.0.34: OrderBomRequestLink 全量重建
-- 业务锚点 = PlanVersionId + OrderCanonicalId（不是 PlanVersionId + OrderId）
-- OrderId 允许 NULL：若当前 PlanVersion 中找不到对应 OrderId，写 SKIPPED，不阻断批次
-- LinkStatus 值域：RESOLVED=正常可用；NO_BOM=外购件/无需展开；FAILED=解析失败；SKIPPED=订单未进入本 PlanVersion 快照
-- ResolvedBOMNO / RepWorksetId 来源：ODS MES_APS_BOM_Workset 聚合，不从 APS_BOM_RAW 反查
CREATE TABLE OrderBomRequestLink (
    Id               BIGINT          PRIMARY KEY IDENTITY(1,1),

    PlanVersionId    INT             NOT NULL,                -- 对应排程版本（FK→PlanVersion.Id，同库；INT 对齐 PlanVersion.Id 类型）
    BatchNo          NVARCHAR(50)    NOT NULL,                -- 对应 BOM 展开批次

    OrderId          BIGINT          NULL,                    -- v5.0.34: 允许 NULL（找不到时 LinkStatus='SKIPPED'）
    OrderCanonicalId BIGINT          NOT NULL,                -- ODS 侧 Order_Canonical.Id；业务唯一锚点
    OrderNo          NVARCHAR(100)   NULL,                    -- 订单号（冗余，便于人工核对）
    SourceSystem     NVARCHAR(50)    NULL,                    -- 来源系统（'ERP'/'MES'）
    SourceOrderId    NVARCHAR(100)   NULL,                    -- 来源系统订单ID

    RequestDetailId  BIGINT          NOT NULL,                -- ODS RequestDetail.Id（逻辑引用，跨库）

    RequestedBOMNO   NVARCHAR(50)    NULL,                    -- 订单原始携带的 BOMNO（可空）
    ResolvedBOMNO    NVARCHAR(50)    NULL,                    -- Level=1 Workset.BOMNO（由 ODS Workset 聚合生成）

    RepWorksetId     BIGINT          NULL,                    -- Level=1 MIN(Workset.Id)，与 ROOT StageDetail 规则一致；NULL=展开失败/SKIPPED

    LinkStatus       NVARCHAR(30)    NOT NULL DEFAULT 'RESOLVED',
    -- RESOLVED=找到 OrderId 且有 Workset 结果；NO_BOM=外购件/无需展开；
    -- FAILED=RequestDetail 有记录但 Workset 解析失败；SKIPPED=订单未进入当前 PlanVersion 的 Order 快照
    ErrorMessage     NVARCHAR(1000)  NULL,                    -- FAILED/SKIPPED 时记录原因

    SyncedAt         DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    -- PlanVersionId FK 延迟到 PlanVersion 表创建后追加（见下方 §2.7 后 ALTER TABLE 块）
    -- OrderId 可空，不建 FK 约束（NULL 时无 Order 记录）

    -- v5.0.34: 唯一锚点改为 PlanVersionId + OrderCanonicalId
    CONSTRAINT UQ_OrderBomRequestLink_Plan_Canonical UNIQUE (PlanVersionId, OrderCanonicalId)
);
GO

CREATE INDEX IX_OrderBomRequestLink_Batch_Request
ON OrderBomRequestLink (BatchNo, RequestDetailId);

CREATE INDEX IX_OrderBomRequestLink_BOMNO
ON OrderBomRequestLink (BatchNo, ResolvedBOMNO);

CREATE INDEX IX_OrderBomRequestLink_RepWorkset
ON OrderBomRequestLink (RepWorksetId);

-- v5.0.34: OrderId 查询加速（过滤 NULL，避免 NULL 膨胀索引）
CREATE INDEX IX_OrderBomRequestLink_Order
ON OrderBomRequestLink (PlanVersionId, OrderId)
WHERE OrderId IS NOT NULL;
GO

-- =============================================
-- 2.3 物料映射表（SCD Type 2拉链表）（2026-04-01 v4.0重构：双源同构）
-- =============================================
-- v4.0重构：消除ERP/MES字段分叉，统一为 SourceID + Warehouse
-- ERP的MasterID和MES的MES_ID统一存入SourceID
-- ERP的Warehouse和MES的Warehouse（原Location，实为仓库代码）统一存入Warehouse

CREATE TABLE MaterialMapping (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialCode NVARCHAR(50) NOT NULL,              -- 核心业务键
    SourceID INT NULL,                               -- 源系统物理主键（ERP的MasterID / MES的MES_ID）
    SourceModel NVARCHAR(100) NULL,                  -- ⚠️ v5.0.27新增：ERP原始型号（用于sp_ValidateAndPromoteOrders Step 0b Model→MaterialCode解析链）
    Warehouse NVARCHAR(50) NULL,                     -- 仓库编码（ERP和MES统一，原MES的Location实为仓库代码）
    Source NVARCHAR(20) NOT NULL,                    -- ERP / MES
    ValidFrom DATETIME2 NOT NULL,                    -- 生效时间
    ValidTo DATETIME2 NULL,                          -- 失效时间（NULL表示当前有效）
    IsCurrent BIT NOT NULL DEFAULT 1,                -- 是否当前有效版本
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- 持久化计算列，用于唯一索引（SQL Server不支持索引键中直接使用ISNULL）
    Warehouse_Norm AS ISNULL(Warehouse, 'N/A') PERSISTED
);
GO

-- SCD Type 2 当前有效版本唯一索引（v4.0简化：4列）
-- 业务键：MaterialCode + Source + Warehouse_Norm
-- 同一物料在不同源/不同仓库下各有一条当前记录
CREATE UNIQUE INDEX IX_MaterialMapping_Current 
ON MaterialMapping(MaterialCode, Source, Warehouse_Norm, IsCurrent) 
WHERE IsCurrent = 1;

-- 历史版本索引
CREATE INDEX IX_MaterialMapping_History 
ON MaterialMapping(MaterialCode, ValidFrom, ValidTo);

-- 时间点查询索引
CREATE INDEX IX_MaterialMapping_TimePoint 
ON MaterialMapping(MaterialCode, ValidFrom, ValidTo, IsCurrent);
-- ⚠️ v5.0.27新增：Step 0b Model→MaterialCode 解析链查找索引
CREATE INDEX IX_MaterialMapping_SourceModel
ON MaterialMapping(Source, SourceModel, IsCurrent)
WHERE SourceModel IS NOT NULL;

-- v5.0.42 新增：管道供给逆向映射索引（SourceID + Warehouse_Norm → MaterialCode）
-- 用于 sp_SyncPipelineSupply 按 ODS.MasterID + StorageCode 查找 APS物料身份
CREATE INDEX IX_MaterialMapping_SourceWarehouse
ON MaterialMapping(Source, SourceID, Warehouse_Norm, IsCurrent)
INCLUDE(MaterialCode)
WHERE SourceID IS NOT NULL;
GO

-- =============================================
-- 2.4 物料供给与责任上下文表（v2.7新增）
-- =============================================
-- 业务用途：记录物料在不同仓库/工厂下的供给方式、责任归属、计划参数
-- 核心理念：同一物料在不同仓库下，业务语义会变化（采购/自制、生产部门等）
-- 架构定位：承载"仓库级业务上下文"，而非"物料本体属性"

CREATE TABLE MaterialSupplyContext (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    
    -- 核心业务键
    MaterialCode NVARCHAR(50) NOT NULL,              -- 物料编码（统一业务键）
    WarehouseCode NVARCHAR(50) NOT NULL,             -- 仓库编码（关键维度）
    FactoryId INT NULL,                              -- 工厂ID（可选）
    
    -- 供给方式与责任归属
    SupplyMode NVARCHAR(20) NOT NULL,                -- PURCHASE/MAKE/OUTSOURCE/MIXED
    DefaultProductionDeptCode NVARCHAR(50) NULL,     -- 默认生产责任部门编码（源系统/业务码，便于追溯）
    DefaultProductionDepartmentId INT NULL,          -- 🆕 v5.0.16：APS 标准字典 FK（指向 ProductionDepartment.Id），与 DeptCode 双轨；2 号位 sp_RebuildMaterialStageDeptContext 优先用此 ID 组装 Context；FK 约束在 ProductionDepartment 表创建后追加
    ProcurementDeptCode NVARCHAR(50) NULL,           -- 采购责任部门编码（APS维护）
    OutsourceDeptCode NVARCHAR(50) NULL,             -- 委外责任部门编码
    
    -- 计划参数（仓库级）
    LeadTimeDays INT NULL,                           -- 该上下文提前期（天）
    SafetyStock DECIMAL(18,4) NULL,                  -- 该仓安全库存
    InventoryManagementMode NVARCHAR(20) NULL,       -- 库存管理方式：STOCKED（有备货）/ NON_STOCKED（无备货）（2026-04-01 v4.0新增）
    
    -- 数据来源与版本控制（SCD Type 2）
    SourceSystem NVARCHAR(20) NOT NULL,              -- ERP/MES（v4.0：双源同构，移除默认值）
    ValidFrom DATETIME2 NOT NULL,                    -- 生效时间
    ValidTo DATETIME2 NULL,                          -- 失效时间（NULL表示当前有效）
    IsCurrent BIT NOT NULL DEFAULT 1,                -- 当前是否有效
    
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 当前有效版本唯一索引
CREATE UNIQUE INDEX IX_MaterialSupplyContext_Current 
ON MaterialSupplyContext(MaterialCode, WarehouseCode, IsCurrent) 
WHERE IsCurrent = 1;

-- 历史版本索引
CREATE INDEX IX_MaterialSupplyContext_History 
ON MaterialSupplyContext(MaterialCode, WarehouseCode, ValidFrom, ValidTo);

-- 供给方式索引
CREATE INDEX IX_MaterialSupplyContext_SupplyMode 
ON MaterialSupplyContext(SupplyMode, IsCurrent) 
WHERE IsCurrent = 1;
GO

-- =============================================
-- 2.4a ProductFamily / Factory（v5.0.40 前置建表；供 ProductionDepartment / InventoryAvailabilityRule 等依赖表使用）
-- =============================================

-- 产品族配置表（§2.6.1，v5.0.40 提前至此，避免 FK 引用顺序失败）
CREATE TABLE ProductFamily (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Code NVARCHAR(50) NOT NULL UNIQUE,
    Name NVARCHAR(200) NOT NULL,
    Description NVARCHAR(500),
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_ProductFamily_Code ON ProductFamily(Code) WHERE IsActive = 1;
GO

-- 工厂表（§2.6.2，v5.0.40 提前至此，避免 FK 引用顺序失败）
CREATE TABLE Factory (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Code NVARCHAR(50) NOT NULL UNIQUE,
    Name NVARCHAR(200) NOT NULL,
    Location NVARCHAR(200),
    TimeZone NVARCHAR(50) NOT NULL DEFAULT 'China Standard Time',
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_Factory_Code ON Factory(Code) WHERE IsActive = 1;
GO

-- =============================================
-- 2.4b ProductionDepartment（v5.0.16 新增；APS 排程责任部门字典）
-- =============================================
-- 业务定位：APS 自维护的"排程责任部门"标准字典；不是审批组织树，不承担行政组织语义
-- 核心规则（业务确认）：
--   • 一个 ProductionDepartment **只归属一个 StageCode**（部门 vs 阶段 1:1）
--   • 一个 StageCode 可对应多个 ProductionDepartment（阶段 vs 部门 1:N）
--   • StageCode 必须取自 StageDict（不允许自由填写新阶段）
-- 维护方：0 号位审批 + 业务侧维护
-- 消费方：
--   • Resource.ProductionDepartmentId（资源归属）
--   • RoutingOperation/RoutingDependency/OperationResourceEligibility.ProductionDepartmentId（部门版本路由）
--   • MaterialSupplyContext.DefaultProductionDepartmentId（仓库级默认）
--   • MaterialStageDeptContext.DefaultProductionDepartmentId（1 号位排程主链入口）
-- 与审批系统解耦：未来审批可有 OrgUnit 表 → 与本表做映射；本表不接审批组织
-- 与 ResourceOrgGroup 区分：本表=排程主链维度；ResourceOrgGroup=看板筛选切片，职责不同，不可合并
-- =============================================

CREATE TABLE ProductionDepartment (
    Id              INT PRIMARY KEY IDENTITY(1,1),
    DeptCode        NVARCHAR(50) NOT NULL UNIQUE,                       -- APS 业务键（如 'CN_MACH_DEPT_01'）
    DeptName        NVARCHAR(200) NOT NULL,                             -- 部门中文名（如"加工一部"）
    FactoryId       INT NULL FOREIGN KEY REFERENCES Factory(Id),        -- 工厂归属（可空：兼容早期未明确归厂的部门）
    StageCode       NVARCHAR(20) NOT NULL,                              -- 单值归属阶段（业务约束 1:1）；软引用 StageDict.StageCode
    DeptType        NVARCHAR(50) NULL,                                  -- 可选业务标签（MACHINING/ASSEMBLY/SURFACE/OUTSOURCE/SPECIAL/OTHER）
    SourceSystem    NVARCHAR(20) NULL,                                  -- 来源标记（ERP/MES/APS 自建）
    SourceDeptCode  NVARCHAR(50) NULL,                                  -- 源系统部门码（审计用）
    IsSchedulingDept BIT NOT NULL DEFAULT 1,                            -- 是否参与 APS 排程（0=仅作汇总维度，不承担 Routing 路由职责）
    IsActive        BIT NOT NULL DEFAULT 1,
    UpdatedBy       NVARCHAR(100) NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_ProductionDepartment_Factory  ON ProductionDepartment(FactoryId)  WHERE IsActive = 1;
CREATE INDEX IX_ProductionDepartment_Stage    ON ProductionDepartment(StageCode)  WHERE IsActive = 1;
CREATE INDEX IX_ProductionDepartment_DeptType ON ProductionDepartment(DeptType)   WHERE IsActive = 1;
GO

-- 追加 MaterialSupplyContext.DefaultProductionDepartmentId 的 FK 约束（在 ProductionDepartment 创建后）
ALTER TABLE MaterialSupplyContext
    ADD CONSTRAINT FK_MSC_ProductionDepartment
    FOREIGN KEY (DefaultProductionDepartmentId) REFERENCES ProductionDepartment(Id);
GO

-- 索引：MSC 当前有效记录按部门反查
CREATE INDEX IX_MaterialSupplyContext_Dept
ON MaterialSupplyContext(DefaultProductionDepartmentId, IsCurrent)
WHERE IsCurrent = 1 AND DefaultProductionDepartmentId IS NOT NULL;
GO

-- =============================================
-- 2.4c MaterialStageDeptOverride（v5.0.16 新增；人工维护/覆盖表）
-- =============================================
-- 业务定位：弥补 MSC 数据缺失/冲突的人工维护入口
-- 适用场景：
--   1) MSC 中没有生产部门
--   2) MSC 自动归一化后出现歧义/冲突，无法自动拍板
--   3) ERP/MES 信息不全，需业务显式指定
-- 维护粒度：必须维护到 (Model 或 MaterialCode) × StageCode → ProductionDeptCode
--   ⚠️ 不能只维护 Model → Department（部门是物料×阶段联合属性）
-- 输入键策略：
--   • 业务人员可用 Model 录入（更熟悉）；2 号位导入时做 Model→MaterialCode 1:1 检查
--   • Model 1:N 多个 MaterialCode 时**拒收**，返回明细，要求业务确认到 MaterialCode
-- 优先级：人工维护 > 自动草稿（详见 sp_RebuildMaterialStageDeptContext）
-- 维护方：业务人员（含 0 号位审批补丁）
-- =============================================

CREATE TABLE MaterialStageDeptOverride (
    Id                  BIGINT PRIMARY KEY IDENTITY(1,1),
    Model               NVARCHAR(100) NULL,                             -- 业务录入键（与 MaterialCode 至少填一项）
    MaterialCode        NVARCHAR(50) NULL,                              -- 物料编码（业务能直接给出时优先填这里）
    StageCode           NVARCHAR(20) NOT NULL,                          -- 大工艺阶段码（必须取自 StageDict）
    ProductionDeptCode  NVARCHAR(50) NOT NULL,                          -- 指定的生产部门码（必须存在于 ProductionDepartment.DeptCode）
    Reason              NVARCHAR(500) NULL,                             -- 维护原因说明
    ValidFrom           DATETIME2 NOT NULL DEFAULT GETDATE(),
    ValidTo             DATETIME2 NULL,
    IsCurrent           BIT NOT NULL DEFAULT 1,
    CreatedBy           NVARCHAR(100) NULL,
    CreatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedBy           NVARCHAR(100) NULL,
    UpdatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- 至少填一项业务键
    CONSTRAINT CK_MaterialStageDeptOverride_HasKey CHECK (Model IS NOT NULL OR MaterialCode IS NOT NULL)
);
GO

-- 当前有效条目唯一索引（按 MaterialCode + StageCode 已解析后唯一）
CREATE UNIQUE INDEX IX_MaterialStageDeptOverride_CurrentByMaterial
ON MaterialStageDeptOverride(MaterialCode, StageCode)
WHERE IsCurrent = 1 AND MaterialCode IS NOT NULL;

-- 当前有效条目唯一索引（按 Model + StageCode；解析前为 Model 维度）
CREATE UNIQUE INDEX IX_MaterialStageDeptOverride_CurrentByModel
ON MaterialStageDeptOverride(Model, StageCode)
WHERE IsCurrent = 1 AND Model IS NOT NULL AND MaterialCode IS NULL;

-- 历史索引
CREATE INDEX IX_MaterialStageDeptOverride_History
ON MaterialStageDeptOverride(MaterialCode, StageCode, ValidFrom, ValidTo);
GO

-- =============================================
-- 2.4d MaterialStageDeptContext（v5.0.16 新增；2 号位组装的正式消费表 / 1 号位排程主链入口）
-- =============================================
-- 业务定位：2 号位 sp_RebuildMaterialStageDeptContext 的正式产出；1 号位排程**唯一**消费入口
-- 消费键：(MaterialId, StageCode) → DefaultProductionDepartmentId
--   含义：某物料在某大工艺阶段下，当前默认由哪个生产部门生产
-- 数据来源：
--   AUTO   = MSC 自动归一化（多数）
--   MANUAL = MaterialStageDeptOverride 人工覆盖
--   MIXED  = 自动草稿 + 人工补丁混合
-- 当前有效约束：同一时点同 (MaterialId, StageCode) 只能有 1 条 IsCurrent=1
-- 重建触发：
--   • 每日定时全量重建
--   • MSC 同步后增量重建（ETL 链路触发）
--   • 人工 Override 提交后局部重建
-- 1 号位接口契约（v5.0.16 红线）：
--   排程从 StageDetail 拿 (MaterialId, StageCode) → 查本表得 DefaultProductionDepartmentId
--   → 按 (MaterialId, ProductionDepartmentId, StageCode) 锁定 Routing 三件套
-- =============================================

CREATE TABLE MaterialStageDeptContext (
    Id                              BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId                      INT NOT NULL,   -- FK→Material(Id)（v5.0.40: 延迟添加，见 §2.6.5 后 ALTER TABLE 块）
    StageCode                       NVARCHAR(20) NOT NULL,              -- 必须存在于 StageDict
    DefaultProductionDepartmentId   INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),
    SourceType                      NVARCHAR(10) NOT NULL                -- AUTO / MANUAL / MIXED
        CONSTRAINT CK_MaterialStageDeptContext_SourceType CHECK (SourceType IN (N'AUTO', N'MANUAL', N'MIXED')),
    SourceDetail                    NVARCHAR(500) NULL,                 -- 组装来源说明（如"MSC 唯一推导" / "Override#123 覆盖" / "MSC+Override 混合"）
    ValidFrom                       DATETIME2 NOT NULL DEFAULT GETDATE(),
    ValidTo                         DATETIME2 NULL,
    IsCurrent                       BIT NOT NULL DEFAULT 1,
    LastRebuildBatchNo              NVARCHAR(50) NULL,                  -- 最近一次重建的批次号
    CreatedAt                       DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt                       DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 当前有效记录唯一索引（1 号位主消费索引）
CREATE UNIQUE INDEX IX_MaterialStageDeptContext_Current
ON MaterialStageDeptContext(MaterialId, StageCode)
WHERE IsCurrent = 1;

-- 历史索引
CREATE INDEX IX_MaterialStageDeptContext_History
ON MaterialStageDeptContext(MaterialId, StageCode, ValidFrom, ValidTo);

-- 部门反查索引（部门→所有归属此部门的物料×阶段）
CREATE INDEX IX_MaterialStageDeptContext_Dept
ON MaterialStageDeptContext(DefaultProductionDepartmentId, IsCurrent)
WHERE IsCurrent = 1;
GO

-- =============================================
-- 2.4e MaterialStageDeptContext_Issues（v5.0.16 新增；Context 重建降级登记）
-- =============================================
-- 业务定位：sp_RebuildMaterialStageDeptContext 重建时遇到无法自动拍板的情况，登记到此表
-- 降级哲学：旧值不动（IsCurrent=1 上一版本继续供 1 号位使用），新问题登记到本表
--   人工修正 Override 后触发局部重建 → 新版本上线
-- 典型 IssueType：
--   MULTI_DEPT_CONFLICT_FOR_STAGE  : MSC 同物料同阶段对应多部门，无法自动拍板
--   MISSING_DEPT_IN_MSC            : MSC 该物料该仓库无 DefaultProductionDept
--   DEPT_NOT_IN_DICT               : MSC 部门码在 ProductionDepartment 字典中找不到
--   STAGE_NOT_IN_DICT              : 推导出的 StageCode 在 StageDict 中找不到
--   MTS_INCONSISTENT               : MTS 中部门与 MSC/Override 结果不一致（一致性校验降级）
--   OVERRIDE_MODEL_AMBIGUOUS       : Override 维护时 Model 1:N 多个 MaterialCode（导入拒收）
-- Severity：INFO/WARN/ERROR（与 BOM_Workset_Issues 风格一致）
-- =============================================

CREATE TABLE MaterialStageDeptContext_Issues (
    Id              BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo         NVARCHAR(50) NOT NULL,                              -- 触发本次重建的批次
    MaterialCode    NVARCHAR(50) NULL,                                  -- 关联物料（部分场景如 OVERRIDE_MODEL_AMBIGUOUS 仅有 Model）
    Model           NVARCHAR(100) NULL,
    StageCode       NVARCHAR(20) NULL,
    IssueType       NVARCHAR(50) NOT NULL,                              -- 见上
    Severity        NVARCHAR(20) NOT NULL DEFAULT 'WARN'                -- INFO / WARN / ERROR
        CONSTRAINT CK_MSDeptContext_Issues_Severity CHECK (Severity IN (N'INFO', N'WARN', N'ERROR')),
    Detail          NVARCHAR(2000) NULL,                                -- 明细描述（含冲突部门列表/Model 多解列表等）
    DegradeAction   NVARCHAR(100) NULL,                                 -- 降级动作（如"沿用旧值" / "跳过此条" / "拒收 Override"）
    ReviewStatus    NVARCHAR(20) NOT NULL DEFAULT 'PENDING'             -- PENDING / CONFIRMED / IGNORED / FIXED
        CONSTRAINT CK_MSDeptContext_Issues_Review CHECK (ReviewStatus IN (N'PENDING', N'CONFIRMED', N'IGNORED', N'FIXED')),
    ReviewedBy      NVARCHAR(100) NULL,
    ReviewedAt      DATETIME2 NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_MSDeptContext_Issues_Batch    ON MaterialStageDeptContext_Issues(BatchNo);
CREATE INDEX IX_MSDeptContext_Issues_Material ON MaterialStageDeptContext_Issues(MaterialCode, StageCode);
CREATE INDEX IX_MSDeptContext_Issues_Pending  ON MaterialStageDeptContext_Issues(ReviewStatus, Severity) WHERE ReviewStatus = 'PENDING';
GO

-- =============================================
-- 2.5 统一库存可用规则表（v5.0.39 V1口径）
-- =============================================
-- V1 口径变更（以下旧表已从当前版本删除）：
--   ProductFamilyInventoryScope (旧§2.5.1)  -- 已删除
--   InventorySourceRule (旧§2.5.2)          -- 已删除
--   InventorySourcePriority (旧§2.6, v2.8已废弃) -- 已删除
-- V1 统一使用 InventoryAvailabilityRule，一张表回答两个问题：
--   1. 某工厂+某产品族+某物料模式，允许哪些 SourceSystem+StorageCode 进入可用库存池？
--   2. 允许时，扣减优先级是多少？
-- 废弃口径：RuleAction(PREFER/EXCLUDE) → 改用 IsAvailable + Priority
-- 历史追溯：参见 changelog v2.8（旧双表设计）
-- =============================================

CREATE TABLE InventoryAvailabilityRule (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- 规则上下文维度（必填）
    ProductFamilyId INT NOT NULL,                          -- 库存使用上下文产品族（非物料自身产品族）
    FactoryId       INT NOT NULL,                          -- 规则所属工厂

    -- 物料过滤（NULL = 该工厂+产品族下所有物料通用）
    MaterialCodePattern NVARCHAR(100) NULL,                -- 支持 LIKE 通配符，如 CYL-%；NULL=通配

    -- 库存来源（必填）
    SourceSystem NVARCHAR(20) NOT NULL,                    -- ERP / MES
    StorageCode  NVARCHAR(50) NOT NULL,                    -- V1 统一使用 WarehouseCode

    -- 规则行为
    IsAvailable BIT NOT NULL DEFAULT 1,                    -- 1=允许进入可用库存池；0=排除
    Priority    INT NOT NULL DEFAULT 100,                  -- 扣减优先级，数值越小越优先

    IsActive    BIT NOT NULL DEFAULT 1,
    Remark      NVARCHAR(500) NULL,
    CreatedAt   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_InventoryAvailabilityRule_ProductFamily
        FOREIGN KEY (ProductFamilyId) REFERENCES ProductFamily(Id),
    CONSTRAINT FK_InventoryAvailabilityRule_Factory
        FOREIGN KEY (FactoryId) REFERENCES Factory(Id)
);
GO

CREATE INDEX IX_InventoryAvailabilityRule_Context
ON InventoryAvailabilityRule(ProductFamilyId, FactoryId, SourceSystem, StorageCode, IsActive);

CREATE INDEX IX_InventoryAvailabilityRule_Priority
ON InventoryAvailabilityRule(ProductFamilyId, FactoryId, Priority);
GO

-- =============================================
-- 2.5 ETL日志表
-- =============================================

CREATE TABLE APS_ETL_Log (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    Step NVARCHAR(100) NOT NULL,                     -- CalculateLLC/SyncMapping/LoadInventory
    Message NVARCHAR(MAX),
    Status NVARCHAR(20) NOT NULL DEFAULT 'SUCCESS',  -- SUCCESS/FAILED
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_ETL_Log_Batch ON APS_ETL_Log(BatchNo, Step, CreatedAt);
GO

-- =============================================
-- 2.6 主数据表（介v1.0继承，部分修改）
-- ❗ ProductFamily §2.6.1 和 Factory §2.6.2 已提前建表至 §2.4a（v5.0.40）
-- =============================================

-- 2.6.3 资源组表
-- ⚠️ v5.0废弃：静态资源组无法表达真实设备可替代性（取决于物料+路径+工序动态组合）
-- 替代方案：组织/统计 → ResourceOrgGroup；排程能力 → OperationResourceEligibility
-- 保留此表定义仅为兼容，新代码禁止引用
CREATE TABLE ResourceGroup (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Code NVARCHAR(50) NOT NULL UNIQUE,
    Name NVARCHAR(200) NOT NULL,
    FactoryId INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    GroupType NVARCHAR(50) NOT NULL,
    TotalCapacity INT NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 2.6.3b 资源组织维度表（v5.0新增，替代原ResourceGroup的组织/统计功能）
-- 仅用于统计切片、前端筛选、组织归类，不再用于排程能力建模
CREATE TABLE ResourceOrgGroup (
    Id          INT PRIMARY KEY IDENTITY(1,1),
    Code        NVARCHAR(50) NOT NULL UNIQUE,
    Name        NVARCHAR(200) NOT NULL,
    FactoryId   INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    Description NVARCHAR(500) NULL,
    IsActive    BIT NOT NULL DEFAULT 1,
    CreatedAt   DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt   DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 2.6.4 资源表（v5.0重构：从"手工维护主数据"改为"外部主数据镜像"）
-- 数据来源：ext_MES_APS_Resource_View（ODS契约视图，v5.0.13 命名统一，原名 ext_APS_Resource_View）
-- 未来 EAM 上线：ext_EAM_APS_Resource_View 同构契约并存，由 sp_SyncResourceData(@SourceType) 按参数分流
-- 同步方式：每天定时全量刷新（设备主数据变化频率低）
-- ⚠️ v5.0废弃：ResourceGroupId 外键已移除，组织归属改由 ResourceOrgGroup 维护
CREATE TABLE Resource (
    Id                       INT PRIMARY KEY IDENTITY(1,1),
    ResourceCode             NVARCHAR(50) NOT NULL UNIQUE,           -- v5.0重命名（原Code），APS统一业务键
    ResourceName             NVARCHAR(200) NOT NULL,                 -- v5.0重命名（原Name）
    ExternalResourceId       NVARCHAR(50) NULL,                      -- v5.0新增：源系统物理主键
    SourceSystem             NVARCHAR(20) NOT NULL DEFAULT 'MES',    -- v5.0新增：MES / EAM
    FactoryId                INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),  -- 工厂归属：汇总/跨厂/日历/物流边界（不作为 Routing 选择主条件）
    -- 🔻 v5.0.16 删除 WorkshopCode（业务确认 MES 也无此概念）
    ProductionDepartmentId   INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：排程责任部门归属（汇总/资源能力归属维度）
    SourceProductionDeptCode NVARCHAR(50) NULL,                      -- 🆕 v5.0.16：源系统部门码（审计用，由 sp_SyncResourceData 从契约视图带入）
    ResourceType             NVARCHAR(50) NOT NULL,                  -- MACHINE / LINE / MANUAL_STATION
    Status                   NVARCHAR(20) NOT NULL DEFAULT 'AVAILABLE',
    CapacityFactor           DECIMAL(18,4) NOT NULL DEFAULT 1.0,     -- v5.0重命名（原Capacity）
    IsActive                 BIT NOT NULL DEFAULT 1,
    CreatedAt                DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt                DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_Resource_Factory ON Resource(FactoryId, ResourceType) WHERE IsActive = 1;
CREATE INDEX IX_Resource_Source  ON Resource(SourceSystem, ExternalResourceId) WHERE IsActive = 1;
CREATE INDEX IX_Resource_Dept    ON Resource(ProductionDepartmentId, ResourceType) WHERE IsActive = 1;  -- 🆕 v5.0.16：部门粒度资源筛选
GO

-- 2.6.4b 资源排程参数表（v5.0新增：APS本地排程控制，不污染外部资源事实层）
CREATE TABLE ResourcePlanningContext (
    Id                      INT PRIMARY KEY IDENTITY(1,1),
    ResourceId              INT NOT NULL FOREIGN KEY REFERENCES Resource(Id),
    CalendarPolicyId        INT NULL,                                   -- 排程日历策略ID
    DispatchPriority        INT NOT NULL DEFAULT 100,                   -- 派工优先级（越小越优先）
    LocalDisableFlag        BIT NOT NULL DEFAULT 0,                     -- APS本地禁用标记
    OverrideCapacityFactor  DECIMAL(18,4) NULL,                         -- APS侧覆盖产能系数
    EffectiveFrom           DATE NOT NULL DEFAULT '1900-01-01',
    EffectiveTo             DATE NULL,
    UpdatedBy               NVARCHAR(50) NULL,
    CreatedAt               DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt               DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- 2.6.5 物料主数据表（v2.7重构：物料本体属性与仓库级上下文分离）
-- ⚠️ 架构调整：IsPurchased、SafetyStock、LeadTimeDays已下沉到MaterialSupplyContext表
-- 这些字段会随仓库/工厂变化，不再作为物料本体的单值属性
CREATE TABLE Material (
    Id INT PRIMARY KEY IDENTITY(1,1),
    
    -- 物料本体属性（不随仓库变化）
    MaterialCode NVARCHAR(100) NOT NULL UNIQUE,      -- ⚠️ v2.5修复：统一使用MaterialCode
    MaterialName NVARCHAR(200) NOT NULL,             -- ⚠️ v2.5修复：统一使用MaterialName
    Spec NVARCHAR(100) NULL,                         -- ⚠️ v2.7新增：物料型号/规格（如：C25ILB-005）
    ProductFamilyId INT NULL FOREIGN KEY REFERENCES ProductFamily(Id),
    MaterialType NVARCHAR(50) NOT NULL,
    UOM NVARCHAR(20) NOT NULL,
    LowLevelCode INT NULL DEFAULT 0,                 -- ⚠️ 低阶码（LLC）
    IsSimpleItem BIT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    
    -- ⚠️ v2.7废弃字段（保留以兼容现有代码，但不再作为唯一真相）
    -- 这些字段已下沉到MaterialSupplyContext表，因为它们会随仓库/工厂变化
    LeadTimeDays INT NOT NULL DEFAULT 0,             -- ⚠️ 废弃：请使用MaterialSupplyContext.LeadTimeDays
    SafetyStock DECIMAL(18,4) NOT NULL DEFAULT 0,    -- ⚠️ 废弃：请使用MaterialSupplyContext.SafetyStock
    IsPurchased BIT NOT NULL DEFAULT 0,              -- ⚠️ 废弃：请使用MaterialSupplyContext.SupplyMode
    
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_Material_Type ON Material(MaterialType, ProductFamilyId) WHERE IsActive = 1;
CREATE INDEX IX_Material_LLC ON Material(LowLevelCode DESC) WHERE IsActive = 1;
CREATE INDEX IX_Material_Code ON Material(MaterialCode) WHERE IsActive = 1;
GO

-- v5.0.40: 延迟 FK（MaterialStageDeptContext.MaterialId → Material.Id）
ALTER TABLE MaterialStageDeptContext
    ADD CONSTRAINT FK_MSDC_Material FOREIGN KEY (MaterialId) REFERENCES Material(Id);
GO

-- 2.6.6 BOM表
CREATE TABLE BOM (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    ParentMaterialId INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ChildMaterialId INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    Quantity DECIMAL(18,6) NOT NULL,                 -- ⚠️ 单位用量，不累乘！
    ScrapRate DECIMAL(5,4) NOT NULL DEFAULT 0,
    LeadTimeOffset INT NOT NULL DEFAULT 0,
    BOMLevel INT NOT NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CHK_BOM_Level CHECK (BOMLevel BETWEEN 1 AND 10),
    CONSTRAINT CHK_BOM_NotSelf CHECK (ParentMaterialId <> ChildMaterialId)
);
GO

CREATE INDEX IX_BOM_Parent ON BOM(ParentMaterialId, IsActive, EffectiveFrom, EffectiveTo);
CREATE INDEX IX_BOM_Child ON BOM(ChildMaterialId, IsActive);
GO

-- 2.6.7 工艺路线表
-- ⚠️ v5.0废弃：线性OperationSeq无法支撑并行/串行混合工艺；MinBatchSize/MaxBatchSize无ODS来源
-- 替代方案：工序节点 → RoutingOperation；工序依赖 → RoutingDependency；
--          批量参数 → RoutingPlanningParam；工序资源能力 → OperationResourceEligibility
-- 保留此表定义仅为兼容，新代码禁止引用
CREATE TABLE Routing (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    OperationSeq INT NOT NULL,
    OperationCode NVARCHAR(50) NOT NULL,
    OperationName NVARCHAR(200) NOT NULL,
    ProcessType NVARCHAR(50) NOT NULL,
    ResourceGroupId INT NULL FOREIGN KEY REFERENCES ResourceGroup(Id),
    StandardDuration DECIMAL(18,4) NOT NULL,
    SetupTime DECIMAL(18,4) NOT NULL DEFAULT 0,
    MinBatchSize DECIMAL(18,4) NOT NULL DEFAULT 1,
    MaxBatchSize DECIMAL(18,4) NOT NULL DEFAULT 999999,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CHK_Routing_Seq CHECK (OperationSeq BETWEEN 1 AND 20),
    CONSTRAINT UQ_Routing UNIQUE (MaterialId, OperationSeq, EffectiveFrom)
);
GO

CREATE INDEX IX_Routing_Material ON Routing(MaterialId, IsActive, EffectiveFrom, EffectiveTo);
GO

-- =============================================
-- 2.6.7b ~ 2.6.7e 工艺图模型（v5.0新增，替代原线性Routing）
-- =============================================

-- 2.6.7b 工序节点表（v5.0新增）
-- 承接ODS工序节点，替代原Routing的工序定义部分
-- 数据来源：ext_MES_APS_Routing_Operation_View（输出MES_ID+Model，2号位装载时通过MaterialMapping映射为MaterialId）
CREATE TABLE RoutingOperation (
    Id                     BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId             INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductionDepartmentId INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：部门版本路由锁定维度；NOT NULL。来源：ODS 契约视图输出 ProductionDeptCode → 2 号位装载时 JOIN ProductionDepartment.Id 映射
    RouteCode              NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',    -- V1固定'DEFAULT'，V2多路径扩展
    PathId                 INT NOT NULL DEFAULT 1,                      -- V1固定1，V2多路径扩展
    OperationCode          NVARCHAR(50) NOT NULL,                       -- 工序编码（路径内唯一）
    OperationName          NVARCHAR(200) NOT NULL,
    ProcessType            NVARCHAR(50) NOT NULL,                       -- 工序级**辅助分类标签**（值域见 ProcessTypeDict）；v5.0.12 口径：仅用于报表/粗分组/统计，**不参与 BOM↔Routing 对接，不作为 1 号位排程主键**
    StageCode              NVARCHAR(50) NULL,                           -- v5.0.6 所属大工艺阶段码；v5.0.12：**BOM↔Routing 对接主键之一**；v5.0.16：联合 ProductionDepartmentId 构成三元组锁定键 (MaterialId, ProductionDepartmentId, StageCode)
    StandardDuration       DECIMAL(18,4) NOT NULL,                      -- 标准工时（分钟）
    SetupTime              DECIMAL(18,4) NOT NULL DEFAULT 0,            -- 准备时间（分钟）
    IsActive               BIT NOT NULL DEFAULT 1,
    CreatedAt              DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt              DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- v5.0.16 唯一键升级三元组：同物料同阶段下不同部门可有不同小工序集合
    CONSTRAINT UQ_RoutingOperation UNIQUE (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode)
);
GO

CREATE INDEX IX_RoutingOp_Material      ON RoutingOperation(MaterialId, RouteCode, PathId) WHERE IsActive = 1;
CREATE INDEX IX_RoutingOp_MaterialDept  ON RoutingOperation(MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId) WHERE IsActive = 1;  -- 🆕 v5.0.16：1 号位主消费索引
GO

-- 2.6.7c 工序依赖边表（v5.0新增）
-- 表达工序间的有向依赖关系，支持并行/串行混合工艺
-- 数据来源：ext_MES_APS_Routing_Dependency_View（输出MES_ID+Model，同上映射逻辑）
-- 并行：工序A→B, A→C 则B和C可并行；汇合：B→D, C→D 则D等B和C都完成
CREATE TABLE RoutingDependency (
    Id                     BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId             INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductionDepartmentId INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：与 RoutingOperation 同维度，避免不同部门依赖关系混垍
    RouteCode              NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',
    PathId                 INT NOT NULL DEFAULT 1,
    FromOperationCode      NVARCHAR(50) NOT NULL,                   -- 前驱工序
    ToOperationCode        NVARCHAR(50) NOT NULL,                   -- 后继工序
    DependencyType         NVARCHAR(10) NOT NULL DEFAULT 'ES',      -- ES/SS/FF（V1先只用ES）
    LagTime                DECIMAL(18,4) NOT NULL DEFAULT 0,        -- 延迟时间（分钟）
    IsActive               BIT NOT NULL DEFAULT 1,
    CreatedAt              DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt              DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- v5.0.16 唯一键升级四元组（含部门）
    CONSTRAINT UQ_RoutingDep UNIQUE (MaterialId, ProductionDepartmentId, RouteCode, PathId, FromOperationCode, ToOperationCode)
);
GO

CREATE INDEX IX_RoutingDep_Material     ON RoutingDependency(MaterialId, RouteCode, PathId) WHERE IsActive = 1;
CREATE INDEX IX_RoutingDep_MaterialDept ON RoutingDependency(MaterialId, ProductionDepartmentId, RouteCode, PathId) WHERE IsActive = 1;  -- 🆕 v5.0.16
GO

-- 2.6.7c2 大工艺阶段表（v5.0.6新增，v5.0.7定位为"阶段字典/标准阶段语言"，v5.0.12 删除 StageSeq）
-- ⚠️ v5.0.12 定位强化：此表仅承载"该物料在哪些大工艺阶段存在配置"的字典性质数据，**不承载任何顺序信息**
-- 排程权威阶段顺序**唯一**来自：MES_APS_BOM_Workset_StageDetail.StageSeq（BOM 派生结果）
-- 数据来源：ext_MES_APS_Routing_Stage_View（MES 原始大工艺阶段数据，2 号位装载时通过 MaterialMapping 映射为 MaterialId）
-- ⚠️ 已知限制：MES 工艺侧不包含外协阶段，数据不完整；完整阶段链由 StageDetail 承载
-- 【设计决策 v5.0.12】ProcessType=工序级分类标签（辅助），StageCode=业务大工艺阶段码（对接主键），OperationName=具体工序（NC/切断/精修）——三层模型互不替换
-- 【设计决策 v5.0.12】删除 StageSeq：跨物料/跨根产品场景下 MES 工艺侧给不出正确的跨大工艺顺序号；保留字段会造成误用
-- 【设计决策 v5.0.12】StageCode 必须取值自 StageDict；MES 本地叫法由 MES_APS_Routing_Stage_View 负责映射标准化
-- 【职责分离】RoutingStage=阶段字典（3 号位契约 → 2 号位装载），StageDetail=BOM 派生结果（5 号位产出 → 2 号位搬运），不混写
CREATE TABLE RoutingStage (
    Id              BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId      INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    RouteCode       NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',    -- 与RoutingOperation键体系对齐
    PathId          INT NOT NULL DEFAULT 1,                      -- 与RoutingOperation键体系对齐
    StageCode       NVARCHAR(50) NOT NULL,                       -- 大工艺阶段码；**必须来自 StageDict**（契约视图负责映射）
    StageName       NVARCHAR(200) NOT NULL,                      -- 阶段中文名（如机加/外协/涂装）
    -- StageSeq：v5.0.12 已删除。跨物料/跨根产品语境下此字段不可能给出正确值；唯一权威在 StageDetail.StageSeq
    IsOutsource     BIT NOT NULL DEFAULT 0,                      -- 是否外协阶段
    IsStockPoint    BIT NOT NULL DEFAULT 0,                      -- 是否半成品库存断点
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_RoutingStage UNIQUE (MaterialId, RouteCode, PathId, StageCode)
);
GO

CREATE INDEX IX_RoutingStage_Material ON RoutingStage(MaterialId, RouteCode, PathId) WHERE IsActive = 1;
-- IX_RoutingStage_Seq：v5.0.12 已删除（随 StageSeq 字段移除）
GO

-- 2.6.7d 工序资源能力关系表（v5.0新增）
-- 定义：某物料、某路径、某工序，允许使用哪些资源
-- 替代原ResourceGroup的排程能力分组功能
-- 数据来源：ext_APS_OperationResourceEligibility_View（输出MES_ID+Model，同上映射逻辑）
CREATE TABLE OperationResourceEligibility (
    Id                     BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId             INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductionDepartmentId INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：资源能力关系按部门场景划分（避免跨部门临时可用与资源归属错位）
    RouteCode              NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',
    PathId                 INT NOT NULL DEFAULT 1,
    OperationCode          NVARCHAR(50) NOT NULL,
    ResourceId             INT NOT NULL FOREIGN KEY REFERENCES Resource(Id),
    Priority               INT NOT NULL DEFAULT 1,                      -- 1=最优
    CapacityFactor         DECIMAL(18,4) NOT NULL DEFAULT 1.0,          -- 该资源执行该工序的产能系数
    IsPrimary              BIT NOT NULL DEFAULT 0,                      -- 是否首选资源
    IsActive               BIT NOT NULL DEFAULT 1,
    EffectiveFrom          DATE NOT NULL DEFAULT '1900-01-01',
    EffectiveTo            DATE NULL,
    CreatedAt              DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt              DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- v5.0.16 唯一键升级：资源归属部门（Resource.ProductionDepartmentId）与能力关系适用部门（本表）不一定等价
    CONSTRAINT UQ_OpResElig UNIQUE (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, ResourceId)
);
GO

CREATE INDEX IX_OpResElig_Operation     ON OperationResourceEligibility(MaterialId, RouteCode, PathId, OperationCode) WHERE IsActive = 1;
CREATE INDEX IX_OpResElig_MaterialDept  ON OperationResourceEligibility(MaterialId, ProductionDepartmentId, OperationCode) WHERE IsActive = 1;  -- 🆕 v5.0.16
CREATE INDEX IX_OpResElig_Resource      ON OperationResourceEligibility(ResourceId) WHERE IsActive = 1;
GO

-- 2.6.7e 排程规划参数表（v5.0新增）
-- 从原Routing表拆出MinBatchSize/MaxBatchSize（当前无ODS来源，不应污染工艺事实层）
-- 未来如MES提供，可通过ODS视图接回（SourceSystem='MES'）
CREATE TABLE RoutingPlanningParam (
    Id                  BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId          INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    RouteCode           NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',
    PathId              INT NOT NULL DEFAULT 1,
    OperationCode       NVARCHAR(50) NOT NULL,
    MinBatchSize        DECIMAL(18,4) NOT NULL DEFAULT 1,
    MaxBatchSize        DECIMAL(18,4) NOT NULL DEFAULT 999999,
    TransferBatchSize   DECIMAL(18,4) NULL,                      -- 转移批量（工序间流转单位）
    SourceSystem        NVARCHAR(20) NOT NULL DEFAULT 'APS_LOCAL', -- MES / APS_LOCAL
    MaintainedBy        NVARCHAR(50) NULL,
    EffectiveFrom       DATE NOT NULL DEFAULT '1900-01-01',
    EffectiveTo         DATE NULL,
    CreatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_RoutingParam UNIQUE (MaterialId, RouteCode, PathId, OperationCode)
);
GO

-- 2.6.8 库存表（修复日期：2026-03-11）
-- ⚠️ 架构重构：拆分为"双事实表 + 一张余额表"的三层架构
-- 原因：用一张Inventory表硬扛ERP和MES的双源数据是"玩具模型"，无法支撑库存对账和优先级剔除

-- 2.6.8.1 ERP库存事实表（按 MasterID + WarehouseCode）
-- ⚠️ 架构原则：保留源系统物理真相（MasterID + WarehouseCode），不直接存 MaterialCode
-- v5.0.39: 新增 FactoryCode（来自 ext_ERP_Inventory_View）; 字段名统一为 WarehouseCode
CREATE TABLE InventoryFact_ERP (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    MasterID     INT NOT NULL,                       -- ERP物理主键（保留用于物理追溯）
    WarehouseCode NVARCHAR(50) NOT NULL,              -- ERP仓库；V1 统一字段名
    FactoryCode  NVARCHAR(50) NULL,                   -- v5.0.39: 来自 ERP_Inventory_View；用于映射 FactoryId
    Quantity     DECIMAL(18,4) NOT NULL,              -- 库存数量
    SyncedAt     DATETIME2 NOT NULL DEFAULT GETDATE(), -- 同步时间
    CONSTRAINT UQ_Inventory_ERP UNIQUE (MasterID, WarehouseCode, FactoryCode)
);
GO

CREATE INDEX IX_InventoryFact_ERP_Query
    ON InventoryFact_ERP(MasterID, WarehouseCode)
    INCLUDE (Quantity, FactoryCode, SyncedAt);
GO

-- 2.6.8.2 MES库存事实表（按 MES_ID + WarehouseCode）
-- ⚠️ 架构原则：保留源系统物理真相（MES_ID + WarehouseCode），不直接存 MaterialCode
-- v5.0.39: 新增 WarehouseCode（MES V1 主链字段）+ FactoryCode；
--          Location 字段名历史保留；MES_Inventory_View.LocationCode 仅追溯，不参与 V1 主链
CREATE TABLE InventoryFact_MES (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    MES_ID       INT NOT NULL,                        -- MES物理主键（保留用于物理追溯）
    Location     NVARCHAR(50) NOT NULL,               -- 字段名历史保留；V1写入 MES_Inventory_View.WarehouseCode
    WarehouseCode NVARCHAR(50) NULL,                  -- v5.0.39: V1 主链字段 = MES_Inventory_View.WarehouseCode
    FactoryCode  NVARCHAR(50) NULL,                   -- v5.0.39: 来自 MES_Inventory_View；用于映射 FactoryId
    Quantity     DECIMAL(18,4) NOT NULL,              -- 库存数量
    SyncedAt     DATETIME2 NOT NULL DEFAULT GETDATE(), -- 同步时间
    CONSTRAINT UQ_Inventory_MES UNIQUE (MES_ID, WarehouseCode, FactoryCode)
);
GO

CREATE INDEX IX_InventoryFact_MES_Query
    ON InventoryFact_MES(MES_ID, WarehouseCode, FactoryCode)
    INCLUDE (Quantity, Location, SyncedAt);
GO

-- 2.6.8.3 库存候选供给池（v2.8新增）
-- 业务用途：通过MaterialMapping折算后的候选库存，保留来源和仓库维度用于规则筛选
-- ⚠️ 架构定位：库存链路中第一次正式形成统一MaterialCode的候选供给层
-- 说明：InventoryFact_ERP/MES保留物理主键，本表通过MaterialMapping进行物理身份挂接，
--       是库存链路中第一次真正进入APS统一业务语义的地方
CREATE TABLE InventorySupplyCandidate (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    
    -- 核心业务键
    MaterialCode NVARCHAR(50) NOT NULL,
    FactoryId INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    SourceSystem NVARCHAR(20) NOT NULL,              -- ERP/MES
    StorageCode NVARCHAR(50) NOT NULL,               -- 仓库/库位代码
    
    -- 库存数量
    Quantity DECIMAL(18,4) NOT NULL,                 -- 候选库存数量
    
    -- 来源追溯
    ERP_MasterID INT NULL,                           -- ERP来源时有值
    MES_ID INT NULL,                                 -- MES来源时有值
    
    -- 筛选状态（白名单模式：初始 0，命中 IsAvailable=1 规则后置 1）
    IsEligible BIT NOT NULL DEFAULT 0,               -- 是否可用（规则筛选后；默认不可用）
    RejectReason NVARCHAR(500),                      -- 被剔除原因（无匹配规则时写入）
    
    -- 时间戳
    SyncedAt DATETIME2 NOT NULL,                     -- 同步时间
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_InventorySupplyCandidate_Material 
ON InventorySupplyCandidate(MaterialCode, FactoryId, SourceSystem);

CREATE INDEX IX_InventorySupplyCandidate_Eligible 
ON InventorySupplyCandidate(MaterialCode, FactoryId, IsEligible) 
WHERE IsEligible = 1;
GO

-- 2.6.8.4 APS统一库存余额表（v2.8重构；v5.0.39口径修正）
-- 定位：规则筛选后按（MaterialCode + ProductFamilyId + FactoryId）汇总的可用库存快照
-- ⚠️ v5.0.39 口径修正：
--   ProductFamilyId = 库存使用上下文（不等同于库存物料自身的 Material.ProductFamilyId）
--   同一物料可在不同产品族上下文下形成不同的可用库存池（因 InventoryAvailabilityRule 范围不同）
--   BatchNo = 库存快照批次标签；标识"本行余额由哪次 sp_SyncInventorySnapshot 生成"
--   BatchNo != 订单消耗记录（订单消耗追溯由 InventoryAllocationResult 承接，V1.1/V2预留）
CREATE TABLE InventoryBalance (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),

    -- 核心业务键（ProductFamilyId = 库存使用上下文，非物料自身归属）
    MaterialCode    NVARCHAR(50) NOT NULL,
    ProductFamilyId INT NOT NULL FOREIGN KEY REFERENCES ProductFamily(Id),
    FactoryId       INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),

    -- 库存数量（应用 InventoryAvailabilityRule 后按上下文汇总）
    OnHandQty    DECIMAL(18,4) NOT NULL,              -- 现有量
    AllocatedQty DECIMAL(18,4) NOT NULL DEFAULT 0,   -- 兼容快照字段；sp_SyncInventorySnapshot初始化为0。V1运行期Allocation在2号位内存Balance中完成，禁止永久UPDATE本字段作为分配真相
    AvailableQty AS (OnHandQty - AllocatedQty) PERSISTED, -- 可用量（计算列）

    -- 来源与批次
    Source  NVARCHAR(20) NOT NULL,                   -- ERP/MES/BOTH
    BatchNo NVARCHAR(50) NULL,                       -- 库存快照批次标签（由 sp_SyncInventorySnapshot 写入）

    -- 时间戳
    LastUpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CreatedAt     DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- 唯一约束
    CONSTRAINT UQ_Inventory_Balance UNIQUE (MaterialCode, ProductFamilyId, FactoryId)
);
GO

CREATE INDEX IX_InventoryBalance_Query 
    ON InventoryBalance(MaterialCode, ProductFamilyId, FactoryId) 
    INCLUDE (OnHandQty, AllocatedQty, Source);

CREATE INDEX IX_InventoryBalance_Batch 
    ON InventoryBalance(BatchNo) 
    WHERE BatchNo IS NOT NULL;
GO

-- P1-1修复（修复日期：2026-03-11）：彻底删除旧Inventory表
-- 系统上线后，库存查询只能走 InventoryFact_ERP/MES 和 InventoryBalance（已强制）

-- =============================================
-- 2.6.8.5 库存可用供给明细表（v5.0.40 新增）
-- =============================================
-- 定位：InventoryAvailableSupplyDetail = 规则命中后、余额汇总前的可用库存明细层
-- 生成：InventorySupplyCandidate 经 InventoryAvailabilityRule 裁决后写入（sp_SyncInventorySnapshot Step 4c）
-- 用途：保留 SourceSystem / StorageCode / AvailabilityRuleId / RulePriority / InventorySupplyCandidateId 等明细
--       InventoryBalance 从本表按 (MaterialCode, ProductFamilyId, FactoryId) 汇总生成（Step 5）
--       排程判断总量时读 InventoryBalance；解释来源或执行分优先级扣减时读本表
-- 核心区别：
--   InventoryBalance      → 负责"总量够不够"（MaterialCode + ProductFamilyId + FactoryId 汇总）
--   本表                  → 负责"这些总量从哪里来、按什么顺序扣"（仓库级/来源级/规则级/优先级）
-- 注意：本表不是订单消耗明细表；订单消耗由 InventoryAllocationResult 承接（V1.1/V2 预留）
-- BatchNo 与 sp_SyncInventorySnapshot 同批；每次 Step 3 开头 TRUNCATE 全量替换
-- ❌ InventorySupplyCandidateId 不加 FK（加 FK 会阻塞 TRUNCATE TABLE InventorySupplyCandidate）
CREATE TABLE InventoryAvailableSupplyDetail (
    Id                 BIGINT        PRIMARY KEY IDENTITY(1,1),
    BatchNo            NVARCHAR(50)  NOT NULL,
    MaterialCode       NVARCHAR(50)  NOT NULL,
    ProductFamilyId    INT           NOT NULL,   -- 来源：InventoryAvailabilityRule.ProductFamilyId（库存使用上下文）
    FactoryId          INT           NOT NULL,
    SourceSystem       NVARCHAR(20)  NOT NULL,   -- ERP / MES
    StorageCode        NVARCHAR(50)  NOT NULL,   -- V1 统一使用 WarehouseCode
    Quantity           DECIMAL(18,4) NOT NULL,
    AvailabilityRuleId BIGINT        NOT NULL,   -- 命中的规则 Id
    RulePriority       INT           NOT NULL,   -- 扣减优先级，数值越小越优先
    ERP_MasterID              INT           NULL,
    MES_ID                    INT           NULL,
    InventorySupplyCandidateId BIGINT       NULL,   -- v5.0.40: 追溯原始候选供给池 Id
    CreatedAt                 DATETIME2     NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_IASD_ProductFamily FOREIGN KEY (ProductFamilyId)    REFERENCES ProductFamily(Id),
    CONSTRAINT FK_IASD_Factory       FOREIGN KEY (FactoryId)          REFERENCES Factory(Id),
    CONSTRAINT FK_IASD_Rule          FOREIGN KEY (AvailabilityRuleId) REFERENCES InventoryAvailabilityRule(Id)
    -- InventorySupplyCandidateId 为逻辑追溯字段，不加 FK，避免阻塞 TRUNCATE TABLE InventorySupplyCandidate
);
GO

CREATE INDEX IX_IASD_Deduction
ON InventoryAvailableSupplyDetail(MaterialCode, ProductFamilyId, FactoryId, RulePriority);

CREATE INDEX IX_IASD_Batch
ON InventoryAvailableSupplyDetail(BatchNo);

CREATE INDEX IX_IASD_Candidate
ON InventoryAvailableSupplyDetail(InventorySupplyCandidateId);
GO

-- =============================================
-- 2.6.8.5b 库存消耗明细表（V1.1/V2 预留）
-- =============================================
-- V1 不实现；由 sp_SyncInventorySnapshot 生成快照库存
-- V1.1/V2 正式实现订单级库存扣减明细追溯
-- CREATE TABLE InventoryAllocationResult ( ... );  -- V2 预留，不在 V1 建表

-- =============================================
-- 2.6.8.6 库存快照同步存储过程（v5.0.39 新增；v5.0.40 修正）
-- =============================================
-- 设计口径：V1 ETL 六步全量快照；每次以 @BatchNo 标记本轮快照
-- InventoryAllocationResult（订单消耗明细）V1 不执行
-- =============================================

CREATE OR ALTER PROCEDURE sp_SyncInventorySnapshot
    @BatchNo       NVARCHAR(50),          -- 快照批次标签，如 '20260531_000000'
    @RowsAffected  INT OUTPUT,
    @ErrorMessage  NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @RowsAffected = 0;
    SET @ErrorMessage = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- ============================================================
        -- 步骤 1：全量读取 ext_ERP_Inventory_View，写入 InventoryFact_ERP
        -- ============================================================
        TRUNCATE TABLE InventoryFact_ERP;

        INSERT INTO InventoryFact_ERP (MasterID, WarehouseCode, FactoryCode, Quantity, SyncedAt)
        SELECT v.MasterID, v.WarehouseCode, v.FactoryCode, v.Quantity, GETDATE()
        FROM ext_ERP_Inventory_View v
        WHERE v.Quantity > 0;

        -- ============================================================
        -- 步骤 2：全量读取 ext_MES_Inventory_View，写入 InventoryFact_MES
        --         V1 主链写 WarehouseCode；Location 字段名历史保留
        -- ============================================================
        TRUNCATE TABLE InventoryFact_MES;

        INSERT INTO InventoryFact_MES (MES_ID, Location, WarehouseCode, FactoryCode, Quantity, SyncedAt)
        SELECT v.MES_ID, v.WarehouseCode, v.WarehouseCode, v.FactoryCode, v.Quantity, GETDATE()
        FROM ext_MES_Inventory_View v
        WHERE v.Quantity > 0;

        -- ============================================================
        -- 步骤 3：通过 MaterialMapping 桥接，生成 InventorySupplyCandidate
        --         JOIN 补 Warehouse_Norm 精确区分同物料不同仓库场景
        --         IsEligible 默认 0（白名单模式）；步骤 4 裁决后置 1
        --   清理顺序：先清明细层，再清候选池（防御性顺序，即使无 FK 亦维持此顺序）
        -- ============================================================
        TRUNCATE TABLE InventoryAvailableSupplyDetail;
        TRUNCATE TABLE InventorySupplyCandidate;

        INSERT INTO InventorySupplyCandidate
            (MaterialCode, FactoryId, SourceSystem, StorageCode, Quantity, ERP_MasterID, MES_ID, IsEligible, SyncedAt, CreatedAt)
        SELECT
            m.MaterialCode, f.Id, 'ERP', e.WarehouseCode,
            e.Quantity, e.MasterID, NULL,
            0,
            GETDATE(), GETDATE()
        FROM InventoryFact_ERP e
        INNER JOIN MaterialMapping m
            ON  m.SourceID       = e.MasterID
            AND m.Source         = 'ERP'
            AND m.IsCurrent      = 1
            AND m.Warehouse_Norm = ISNULL(e.WarehouseCode, 'N/A')
        INNER JOIN Factory f ON f.Code = e.FactoryCode AND f.IsActive = 1;

        -- Step3 ERP：记录被 INNER JOIN 静默过滤的事实行（最多 50 条）
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        SELECT TOP 50
            @BatchNo,
            'sp_SyncInventorySnapshot.Step3.ERP.FilteredOut',
            CONCAT('FilteredOut: MasterID=', CAST(e.MasterID AS NVARCHAR(20)),
                   ' WarehouseCode=', ISNULL(e.WarehouseCode, 'NULL'),
                   ' FactoryCode=',  ISNULL(e.FactoryCode,   'NULL'),
                   ' Reason=', CASE
                       WHEN m.SourceID IS NULL THEN 'NoMaterialMapping'
                       ELSE 'NoFactory'
                   END),
            'WARN', GETDATE()
        FROM InventoryFact_ERP e
        LEFT JOIN MaterialMapping m
            ON  m.SourceID       = e.MasterID
            AND m.Source         = 'ERP'
            AND m.IsCurrent      = 1
            AND m.Warehouse_Norm = ISNULL(e.WarehouseCode, 'N/A')
        LEFT JOIN Factory f ON f.Code = e.FactoryCode AND f.IsActive = 1
        WHERE m.SourceID IS NULL OR f.Id IS NULL;

        INSERT INTO InventorySupplyCandidate
            (MaterialCode, FactoryId, SourceSystem, StorageCode, Quantity, ERP_MasterID, MES_ID, IsEligible, SyncedAt, CreatedAt)
        SELECT
            m.MaterialCode, f.Id, 'MES', ms.WarehouseCode,
            ms.Quantity, NULL, ms.MES_ID,
            0,
            GETDATE(), GETDATE()
        FROM InventoryFact_MES ms
        INNER JOIN MaterialMapping m
            ON  m.SourceID       = ms.MES_ID
            AND m.Source         = 'MES'
            AND m.IsCurrent      = 1
            AND m.Warehouse_Norm = ISNULL(ms.WarehouseCode, 'N/A')
        INNER JOIN Factory f ON f.Code = ms.FactoryCode AND f.IsActive = 1;

        -- Step3 MES：记录被 INNER JOIN 静默过滤的事实行（最多 50 条）
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        SELECT TOP 50
            @BatchNo,
            'sp_SyncInventorySnapshot.Step3.MES.FilteredOut',
            CONCAT('FilteredOut: MES_ID=', CAST(ms.MES_ID AS NVARCHAR(20)),
                   ' WarehouseCode=', ISNULL(ms.WarehouseCode, 'NULL'),
                   ' FactoryCode=',   ISNULL(ms.FactoryCode,   'NULL'),
                   ' Reason=', CASE
                       WHEN m.SourceID IS NULL THEN 'NoMaterialMapping'
                       ELSE 'NoFactory'
                   END),
            'WARN', GETDATE()
        FROM InventoryFact_MES ms
        LEFT JOIN MaterialMapping m
            ON  m.SourceID       = ms.MES_ID
            AND m.Source         = 'MES'
            AND m.IsCurrent      = 1
            AND m.Warehouse_Norm = ISNULL(ms.WarehouseCode, 'N/A')
        LEFT JOIN Factory f ON f.Code = ms.FactoryCode AND f.IsActive = 1
        WHERE m.SourceID IS NULL OR f.Id IS NULL;

        -- ============================================================
        -- 步骤 4：规则裁决（胜出规则模式）
        --   ① 匹配所有 IsActive=1 规则（含 IsAvailable=0 和 IsAvailable=1）
        --   ② 按 (Candidate.Id, r.ProductFamilyId) 分组，选唯一胜出规则：
        --      排序：a. MaterialCodePattern IS NOT NULL 优先（精确 > 通配）
        --             b. Priority ASC（数值越小越优先）
        --             c. r.Id ASC（确定性 tiebreak）
        --   ③ 若 a+b 完全并列（TieCount>1）→ 写 WARN；仍用 r.Id 最小规则胜出（不重复写入）
        --   ④ 胜出规则 IsAvailable=1 → 写 InventoryAvailableSupplyDetail + 回标 IsEligible=1
        --      胜出规则 IsAvailable=0 → 回标 RejectReason，IsEligible 保持 0
        --   ⑤ 无匹配规则 → NoRuleMatch WARN，不进 InventoryBalance
        --   ❌ 不再使用 Material.ProductFamilyId；不再有"无匹配默认可用"逻辑
        -- ============================================================
        -- 4a：裁决临时表：每 (CandidateId, ProductFamilyId) 选唯一胜出规则
        IF OBJECT_ID('tempdb..#WinnerRules') IS NOT NULL DROP TABLE #WinnerRules;

        SELECT
            c.Id              AS CandidateId,
            c.MaterialCode,
            c.FactoryId,
            c.SourceSystem,
            c.StorageCode,
            c.Quantity,
            c.ERP_MasterID,
            c.MES_ID,
            r.Id              AS RuleId,
            r.ProductFamilyId,
            r.IsAvailable,
            r.Priority,
            ROW_NUMBER() OVER (
                PARTITION BY c.Id, r.ProductFamilyId
                ORDER BY
                    CASE WHEN r.MaterialCodePattern IS NOT NULL THEN 0 ELSE 1 END ASC,
                    r.Priority ASC,
                    r.Id ASC          -- 确定性 tiebreak
            ) AS Rn,
            COUNT(*) OVER (
                PARTITION BY c.Id, r.ProductFamilyId,
                    CASE WHEN r.MaterialCodePattern IS NOT NULL THEN 0 ELSE 1 END,
                    r.Priority
            ) AS TieCount             -- 同级规则数；>1 表示存在真正并列
        INTO #WinnerRules
        FROM InventorySupplyCandidate c
        INNER JOIN InventoryAvailabilityRule r
            ON  r.FactoryId    = c.FactoryId
            AND r.SourceSystem = c.SourceSystem
            AND r.StorageCode  = c.StorageCode
            AND (r.MaterialCodePattern IS NULL OR c.MaterialCode LIKE r.MaterialCodePattern)
            AND r.IsActive     = 1;

        -- 4b：并列告警（最多 50 条）
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        SELECT TOP 50
            @BatchNo,
            'sp_SyncInventorySnapshot.Step4.TieWarn',
            CONCAT('RuleTie: MaterialCode=', w.MaterialCode,
                   ' ProductFamilyId=', CAST(w.ProductFamilyId AS NVARCHAR(10)),
                   ' TieCount=', CAST(w.TieCount AS NVARCHAR(5)),
                   ' WinnerRuleId=', CAST(w.RuleId AS NVARCHAR(20))),
            'WARN', GETDATE()
        FROM #WinnerRules w
        WHERE w.Rn = 1 AND w.TieCount > 1;

        -- 4c：胜出规则 IsAvailable=1 → 写明细层（每个 CandidateId+ProductFamilyId 恰好一行）
        INSERT INTO InventoryAvailableSupplyDetail
            (BatchNo, MaterialCode, ProductFamilyId, FactoryId, SourceSystem, StorageCode,
             Quantity, AvailabilityRuleId, RulePriority, ERP_MasterID, MES_ID,
             InventorySupplyCandidateId, CreatedAt)
        SELECT
            @BatchNo, w.MaterialCode, w.ProductFamilyId, w.FactoryId, w.SourceSystem, w.StorageCode,
            w.Quantity, w.RuleId, w.Priority, w.ERP_MasterID, w.MES_ID,
            w.CandidateId, GETDATE()
        FROM #WinnerRules w
        WHERE w.Rn = 1 AND w.IsAvailable = 1;

        -- 4d：回标至少命中一条 IsAvailable=1 胜出规则的候选
        UPDATE c
        SET c.IsEligible = 1
        FROM InventorySupplyCandidate c
        WHERE EXISTS (
            SELECT 1 FROM #WinnerRules w
            WHERE w.CandidateId = c.Id AND w.Rn = 1 AND w.IsAvailable = 1
        );

        -- 4e：所有胜出规则均为 IsAvailable=0 的候选 → 回标 RejectReason
        UPDATE c
        SET c.RejectReason = CONCAT('ExcludedByRule: RuleId=', w.RuleId,
                                    ' Priority=', CAST(w.Priority AS NVARCHAR(10)),
                                    ' ProductFamilyId=', CAST(w.ProductFamilyId AS NVARCHAR(10)))
        FROM InventorySupplyCandidate c
        INNER JOIN (
            SELECT CandidateId,
                   MIN(RuleId)        AS RuleId,
                   MIN(Priority)      AS Priority,
                   MIN(ProductFamilyId) AS ProductFamilyId
            FROM #WinnerRules
            WHERE Rn = 1 AND IsAvailable = 0
            GROUP BY CandidateId
        ) w ON w.CandidateId = c.Id
        WHERE c.IsEligible = 0;  -- 不覆盖已被其他 ProductFamilyId 规则接受的候选

        -- 4f：无任何匹配规则 → 回写 RejectReason + WARN 日志（最多 50 条）
        UPDATE c
        SET c.RejectReason = 'NoRuleMatch: no active InventoryAvailabilityRule matched'
        FROM InventorySupplyCandidate c
        WHERE c.IsEligible = 0 AND c.RejectReason IS NULL;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        SELECT TOP 50
            @BatchNo,
            'sp_SyncInventorySnapshot.Step4.NoRuleMatch',
            CONCAT('NoRuleMatch: MaterialCode=', c.MaterialCode,
                   ' SourceSystem=', c.SourceSystem,
                   ' StorageCode=', c.StorageCode,
                   ' FactoryId=', CAST(c.FactoryId AS NVARCHAR(20))),
            'WARN', GETDATE()
        FROM InventorySupplyCandidate c
        WHERE c.RejectReason = 'NoRuleMatch: no active InventoryAvailabilityRule matched';

        DROP TABLE #WinnerRules;

        -- ============================================================
        -- 步骤 5：InventoryAvailableSupplyDetail → InventoryBalance（汇总）
        --   ProductFamilyId 来源：明细层（= 规则输出，非 Material.ProductFamilyId）
        --   V1 全量替换：TRUNCATE 后重建，不保留历史批次；与 UNIQUE 约束无冲突
        -- ============================================================
        TRUNCATE TABLE InventoryBalance;

        INSERT INTO InventoryBalance
            (MaterialCode, ProductFamilyId, FactoryId, OnHandQty, AllocatedQty, Source, BatchNo, LastUpdatedAt, CreatedAt)
        SELECT
            d.MaterialCode,
            d.ProductFamilyId,      -- ⬅ 来源：规则，非 Material.ProductFamilyId
            d.FactoryId,
            SUM(d.Quantity)         AS OnHandQty,
            0                       AS AllocatedQty,
            CASE
                WHEN COUNT(DISTINCT d.SourceSystem) > 1 THEN 'BOTH'
                ELSE MAX(d.SourceSystem)
            END                     AS Source,
            @BatchNo,
            GETDATE(),
            GETDATE()
        FROM InventoryAvailableSupplyDetail d
        WHERE d.BatchNo = @BatchNo
        GROUP BY d.MaterialCode, d.ProductFamilyId, d.FactoryId;

        SET @RowsAffected = @@ROWCOUNT;

        -- ============================================================
        -- 步骤 6：写入 ETL 成功日志
        -- ============================================================
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (@BatchNo, 'sp_SyncInventorySnapshot',
                CONCAT('InventoryBalance rows=', @RowsAffected,
                       '; Detail rows=', (SELECT COUNT(*) FROM InventoryAvailableSupplyDetail WHERE BatchNo=@BatchNo)),
                'SUCCESS', GETDATE());

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (@BatchNo, 'sp_SyncInventorySnapshot', @ErrorMessage, 'FAILED', GETDATE());
    END CATCH
END;
GO

-- =============================================
-- 2.8 管道供给链（2026-05-09 v5.0.23 新增；并行于现货库存六层主链）
-- =============================================
-- 定位：APS 并行供给事实层，承接“在途/管道供给”；不替代 InventoryBalance
-- 当前来源：ERP 厂间物流运输在途（ERP_InterplantInTransit_View）
-- 未来按需扩展：ERP_VMI_View / ERP_PurchaseInTransit_View 等新增视图
-- 【设计决策】InventoryBalance定义不变；真实Timed Supply与现货库存并行。某来源真实0行合法，但“来源未接通”必须失败/告警，不得伪装空集合。
-- 【设计决策】SupplyFact_Pipeline 是标准化供给事实层（允许少量本地派生字段）
-- 【设计决策】SupplyAvailabilityRule 是管道供给主题规则表，不是统一万能规则引擎
--             现货链统一使用 InventoryAvailabilityRule（旧 ProductFamilyInventoryScope/InventorySourceRule 已删除）
-- 【v5.1.2冻结】V1必须接入真实厂间在途/采购在途或未结PO/VMI/已到厂未入库Timed Supply；
--                  Planning-only Purchase Placeholder仅内存，不写SupplyFact_Pipeline。
-- 【ProductFamilyId 口径】MaterialId 已成功映射但 Material.ProductFamilyId 本身为空时，
--                         允许 ProductFamilyId=NULL，并参与 ProductFamilyId=NULL 通配规则匹配。
-- 【映射失败红线】MasterID 无法映射 MaterialId（或 FactoryCode 无法映射 FactoryId）时，
--                 该行不得写入 SupplyFact_Pipeline，不得进入 SupplyAvailabilityRule，
--                 不得参与 ProductFamilyId=NULL 通配规则兜底，必须登记 APS_ETL_Log。
-- =============================================
-- 2.8.1 管道供给事实层
-- =============================================
CREATE TABLE SupplyFact_Pipeline (
    Id                BIGINT PRIMARY KEY IDENTITY(1,1),
    -- 物料维度
    MaterialCode      NVARCHAR(100) NOT NULL,         -- APS标准编码（mat.MaterialCode，经 MasterID+StorageCode→MaterialMapping→Material 桥接）
    MaterialId        INT NOT NULL,                     -- 必须有效映射（MasterID+StorageCode→MaterialMapping.Warehouse_Norm→Material.Id）；映射失败不写入
    CONSTRAINT FK_SupplyFact_Pipeline_Material FOREIGN KEY (MaterialId) REFERENCES Material(Id),
    FactoryCode       NVARCHAR(50)  NOT NULL,          -- ODS原始
    FactoryId         INT NOT NULL
        CONSTRAINT FK_SupplyFact_Pipeline_Factory FOREIGN KEY REFERENCES Factory(Id),  -- 映射失败不写入本表
    ProductFamilyId   INT NULL,                        -- 可为空：仅当物料自身未配置产品族时
    -- 供给特征
    SupplyType        NVARCHAR(50)  NOT NULL,
    -- 当前V1受控属性值：INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / OPEN_PO_REMAINING /
    -- ARRIVED_NOT_RECEIVED / VMI_ONSITE / SUPPLIER_RESERVED
    OwnershipType     NVARCHAR(20)  NOT NULL DEFAULT 'OWNED',    -- OWNED / CONSIGNMENT / SUPPLIER
    QualityStatus     NVARCHAR(20)  NOT NULL DEFAULT 'AVAILABLE', -- AVAILABLE / PENDING_INSPECTION / HOLD
    Quantity          DECIMAL(18,4) NOT NULL,
    -- 时间（❗字段语义严格区分）
    ETA               DATETIME2 NULL,                  -- ODS原始事实：源系统预计到达时间
    AvailableTime     DATETIME2 NULL,                  -- APS统一可用时间；可由ETA/人工ETA/DefaultLT/到厂可用偏移按冻结规则归一。1号位不得自行重算
    CommitmentStatus  NVARCHAR(30) NULL,                  -- COMMITTED/CONFIRMED/ESTIMATED/NOT_COMMITTED；按真实来源事实映射。Planning-only占位不落本表
    -- ERP 来源追溯（v5.0.42 新增4个来源追溯字段）
    SourceMasterID        INT NULL,                          -- ODS MasterID 直通，与 StorageCode 共同参与 MaterialMapping(SourceID+Warehouse_Norm) 挂接
    SourceFactoryCode     NVARCHAR(50) NULL,                 -- 发出工厂编码（ODS SourceFactoryCode 直通）
    SourceDocumentNo      NVARCHAR(100) NULL,
    SourceDocumentLineNo  NVARCHAR(50) NULL,                 -- ERP来源单据行号
    SourceUpdatedAt       DATETIME2 NULL,                    -- ERP来源记录更新时间
    -- 供给追溯
    StorageCode           NVARCHAR(50) NULL,                  -- 目的仓库编码 / 预计收货仓库
    SupplierCode          NVARCHAR(50) NULL,                  -- 供应方编码，厂间在途可为空
    SourceSystem          NVARCHAR(50) NOT NULL DEFAULT 'ERP',-- 来源系统，默认 ERP
    -- 来源幂等（v5.0.42 P0-11）：防止同一来源记录重复同步
    SourceRowKey          AS CONCAT(SourceSystem, '|', SupplyType, '|',
                              ISNULL(SourceDocumentNo,  ''), '|',
                              ISNULL(SourceDocumentLineNo,''), '|',
                              ISNULL(CAST(SourceMasterID AS NVARCHAR(20)),''), '|',
                              ISNULL(StorageCode,          ''), '|',
                              FactoryCode) PERSISTED,           -- 计算列，不可空；业务语义唯一
    -- 规则裁决追溯（v5.0.42 P1-3）：解释供给为何被纳入、AvailableTime为何是这个时间
    SupplyAvailabilityRuleId INT NULL,                         -- FK → SupplyAvailabilityRule(Id)；无命中时为 NULL
    AppliedLeadTimeOffset    INT NULL,                         -- 命中的 LeadTimeOffset（小时）
    RulePriority             INT NULL,                         -- 命中规则的 Priority
    RuleEvaluatedAt          DATETIME2 NULL,                   -- 规则裁决时间（与 @DataCutoffTime 同值）
    -- 快照与状态
    BatchNo           NVARCHAR(50)  NULL,               -- nullable；夜间全量排程=当日BatchNo；白天实时=NULL（读最新IsActive=1）
    IsActive          BIT           NOT NULL DEFAULT 1,
    SyncedAt          DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

-- 同批次内同来源记录幂等（P0-11：SourceRowKey 为持久化计算列，可直接作为唯一索引键）
CREATE UNIQUE INDEX UX_SupplyFact_Pipeline_SourceRow_Batch
ON SupplyFact_Pipeline(SourceRowKey, BatchNo)
WHERE BatchNo IS NOT NULL;

-- 补充外键约束（v5.0.43 P0-3：ProductFamilyId / SupplyAvailabilityRuleId）
ALTER TABLE SupplyFact_Pipeline
ADD CONSTRAINT FK_SupplyFact_Pipeline_ProductFamily
    FOREIGN KEY (ProductFamilyId) REFERENCES ProductFamily(Id);

GO

CREATE INDEX IX_SupplyFact_Pipeline_Query
ON SupplyFact_Pipeline(MaterialCode, FactoryId, ProductFamilyId, SupplyType)
WHERE IsActive = 1;

CREATE INDEX IX_SupplyFact_Pipeline_Batch
ON SupplyFact_Pipeline(BatchNo)
WHERE BatchNo IS NOT NULL;
GO

-- =============================================
-- 2.8.2 管道供给规则表（供给主题独立规则表）
-- =============================================
-- 定位：仅负责“在途/管道供给”主题；现货链统一使用 InventoryAvailabilityRule（旧 ProductFamilyInventoryScope + InventorySourceRule 已于 v5.0.39 删除）
-- NULL 维度表示“通配（适用所有）”；行为由 sp_SyncPipelineSupply 按 Priority 排序匹配
-- v5.1.2：本表保留为已有供给主题配置来源之一；不得演变成统一Pipeline规则平台。无匹配规则不等于Supply不存在；最终仓库资格/排序由冻结规则与2号位运行时执行。
CREATE TABLE SupplyAvailabilityRule (
    Id               INT       PRIMARY KEY IDENTITY(1,1),
    -- 规则匹配维度（NULL = 通配）
    ProductFamilyId  INT           NULL,              -- NULL = 适用所有产品族
    FactoryId        INT           NULL,              -- NULL = 适用所有工厂
    SupplyType       NVARCHAR(50)  NULL,              -- NULL = 适用所有类型
    OwnershipType    NVARCHAR(20)  NULL,
    QualityStatus    NVARCHAR(20)  NULL,
    -- 规则行为
    IncludeFlag      BIT           NOT NULL DEFAULT 1,  -- 1=纳入排程可用供给 / 0=排除
    Priority         INT           NOT NULL DEFAULT 50,
    LeadTimeOffset   INT           NOT NULL DEFAULT 0,  -- 单位：小时；AvailableTime = ETA + LeadTimeOffset
    -- 时效
    EffectiveFrom    DATETIME2 NULL,
    EffectiveTo      DATETIME2 NULL,
    IsActive         BIT           NOT NULL DEFAULT 1,
    Remark           NVARCHAR(500) NULL,
    CreatedAt        DATETIME2     NOT NULL DEFAULT GETDATE(),
    UpdatedAt        DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

-- ❗ SQL Server NULL 语义说明：本库中 NULL=NULL 对唯一索引成立，
-- 故本索引有效防止“完全相同的五维规则组合”重复写入（即使维度含 NULL 也受约束）。
ALTER TABLE SupplyFact_Pipeline
ADD CONSTRAINT FK_SupplyFact_Pipeline_AvailabilityRule
    FOREIGN KEY (SupplyAvailabilityRuleId) REFERENCES SupplyAvailabilityRule(Id);
-- NULL 维度代表通配，与具体属性値不同的规则行可合法共存。
-- WHERE IsActive=1：允许软删除后以相同维度组合重建规则，不被旧 IsActive=0 行阻塞。
CREATE UNIQUE NONCLUSTERED INDEX UX_SupplyAvailabilityRule_NoDupRule
ON SupplyAvailabilityRule (ProductFamilyId, FactoryId, SupplyType, OwnershipType, QualityStatus)
WHERE IsActive = 1;
GO

-- =============================================
-- 2.8.3 管道供给同步存储过程（v5.1.2 V1真实Timed Supply装载）
-- =============================================
-- 红线：
--   1) dbo.ext_PipelineSupply_Source_View不存在 = 技术/部署失败，THROW；不得TRUNCATE+SUCCESS伪装空集合。
--   2) 真实View存在但本批次0行 = 合法业务结果，可SUCCESS rows=0。
--   3) 本SP只做事实标准化/基础AvailableTime；Demand/Supply余额与Allocation仍在2号位内存，不UPDATE事实表分配量。
--   4) SupplyAvailabilityRule仅为兼容配置来源之一：明确IncludeFlag=0可排除；无命中不自动排除。
--   5) Planning-only Purchase Placeholder不进本SP、不落表。
CREATE OR ALTER PROCEDURE sp_SyncPipelineSupply
    @BatchNo          NVARCHAR(50),
    @DataCutoffTime   DATETIME2,
    @RowsAffected     INT OUTPUT,
    @ErrorMessage     NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @RowsAffected = 0;
    SET @ErrorMessage = NULL;

    BEGIN TRY
        IF @DataCutoffTime IS NULL
            THROW 51001, '@DataCutoffTime must not be NULL', 1;

        IF OBJECT_ID(N'dbo.ext_PipelineSupply_Source_View', N'V') IS NULL
            THROW 51002, 'V1 Timed Supply source not bound: dbo.ext_PipelineSupply_Source_View is missing', 1;

        CREATE TABLE #RawPipelineSupply (
            RawId                 BIGINT IDENTITY(1,1) PRIMARY KEY,
            MasterID              INT NULL,
            MaterialCode          NVARCHAR(100) NULL,
            SourceFactoryCode     NVARCHAR(50) NULL,
            FactoryCode           NVARCHAR(50) NULL,
            SupplyType            NVARCHAR(50) NOT NULL,
            OwnershipType         NVARCHAR(20) NULL,
            QualityStatus         NVARCHAR(20) NULL,
            Quantity              DECIMAL(18,4) NOT NULL,
            ETA                   DATETIME2 NULL,
            StorageCode           NVARCHAR(50) NULL,
            SupplierCode          NVARCHAR(50) NULL,
            SourceDocumentNo      NVARCHAR(100) NULL,
            SourceDocumentLineNo  NVARCHAR(50) NULL,
            SourceUpdatedAt       DATETIME2 NULL,
            SourceSystem          NVARCHAR(50) NOT NULL
        );

        DECLARE @LoadSql NVARCHAR(MAX) = N'
            INSERT INTO #RawPipelineSupply (
                MasterID, MaterialCode, SourceFactoryCode, FactoryCode,
                SupplyType, OwnershipType, QualityStatus, Quantity, ETA,
                StorageCode, SupplierCode, SourceDocumentNo,
                SourceDocumentLineNo, SourceUpdatedAt, SourceSystem
            )
            SELECT
                MasterID, MaterialCode, SourceFactoryCode, FactoryCode,
                SupplyType, OwnershipType, QualityStatus, Quantity, ETA,
                StorageCode, SupplierCode, SourceDocumentNo,
                SourceDocumentLineNo, SourceUpdatedAt, SourceSystem
            FROM dbo.ext_PipelineSupply_Source_View
            WHERE Quantity > 0
              AND FactoryCode IS NOT NULL
              AND (SourceUpdatedAt IS NULL OR SourceUpdatedAt <= @Cutoff);';

        EXEC sp_executesql @LoadSql, N'@Cutoff DATETIME2', @Cutoff=@DataCutoffTime;

        -- 物料映射必须唯一；仍沿用 MasterID + StorageCode + Source='ERP' 的仓库级身份链。
        SELECT
            r.RawId,
            COUNT(mm.Id) AS MappingCount,
            MIN(mm.Id)   AS MaterialMappingId
        INTO #MapCount
        FROM #RawPipelineSupply r
        LEFT JOIN MaterialMapping mm
          ON mm.SourceID = r.MasterID
         AND mm.Source = N'ERP'
         AND mm.IsCurrent = 1
         AND mm.Warehouse_Norm = ISNULL(r.StorageCode, N'N/A')
        GROUP BY r.RawId;

        DECLARE @MissingMap INT = (SELECT COUNT(*) FROM #MapCount WHERE MappingCount = 0);
        DECLARE @AmbiguousMap INT = (SELECT COUNT(*) FROM #MapCount WHERE MappingCount > 1);
        IF @MissingMap > 0 OR @AmbiguousMap > 0
        BEGIN
            INSERT INTO APS_ETL_Log(BatchNo, Step, Message, Status, CreatedAt)
            VALUES(@BatchNo, 'sp_SyncPipelineSupply',
                   CONCAT('WARN mapping skipped: missing=', @MissingMap, '; ambiguous=', @AmbiguousMap),
                   'SUCCESS', GETDATE());
        END;

        BEGIN TRANSACTION;

        DELETE FROM SupplyFact_Pipeline WHERE BatchNo = @BatchNo;

        INSERT INTO SupplyFact_Pipeline (
            MaterialCode, MaterialId, FactoryCode, FactoryId, ProductFamilyId,
            SupplyType, OwnershipType, QualityStatus, Quantity, ETA, AvailableTime, CommitmentStatus,
            SourceMasterID, SourceFactoryCode, SourceDocumentNo, SourceDocumentLineNo, SourceUpdatedAt,
            StorageCode, SupplierCode, SourceSystem,
            SupplyAvailabilityRuleId, AppliedLeadTimeOffset, RulePriority, RuleEvaluatedAt,
            BatchNo, IsActive, SyncedAt
        )
        SELECT
            mat.MaterialCode,
            mat.Id,
            f.Code,
            f.Id,
            mat.ProductFamilyId,
            r.SupplyType,
            ISNULL(r.OwnershipType, N'OWNED'),
            ISNULL(r.QualityStatus, N'AVAILABLE'),
            r.Quantity,
            r.ETA,
            CASE WHEN r.ETA IS NULL THEN NULL
                 ELSE DATEADD(HOUR, ISNULL(winner.LeadTimeOffset,0), r.ETA)
            END AS AvailableTime,
            NULL AS CommitmentStatus, -- 具体承诺强度由来源事实/2号位运行时映射；NULL不得被解释为确定承诺
            r.MasterID,
            r.SourceFactoryCode,
            r.SourceDocumentNo,
            r.SourceDocumentLineNo,
            r.SourceUpdatedAt,
            r.StorageCode,
            r.SupplierCode,
            r.SourceSystem,
            winner.RuleId,
            winner.LeadTimeOffset,
            winner.RulePriority,
            @DataCutoffTime,
            @BatchNo,
            1,
            GETDATE()
        FROM #RawPipelineSupply r
        INNER JOIN #MapCount mc
          ON mc.RawId = r.RawId AND mc.MappingCount = 1
        INNER JOIN MaterialMapping mm
          ON mm.Id = mc.MaterialMappingId
        INNER JOIN Material mat
          ON mat.MaterialCode = mm.MaterialCode AND mat.IsActive = 1
        INNER JOIN Factory f
          ON f.Code = r.FactoryCode AND f.IsActive = 1
        OUTER APPLY (
            SELECT TOP (1)
                sr.Id AS RuleId,
                sr.IncludeFlag,
                sr.LeadTimeOffset,
                sr.Priority AS RulePriority
            FROM SupplyAvailabilityRule sr
            WHERE sr.IsActive = 1
              AND (sr.EffectiveFrom IS NULL OR sr.EffectiveFrom <= @DataCutoffTime)
              AND (sr.EffectiveTo   IS NULL OR sr.EffectiveTo   >  @DataCutoffTime)
              AND (sr.ProductFamilyId IS NULL OR sr.ProductFamilyId = mat.ProductFamilyId)
              AND (sr.FactoryId       IS NULL OR sr.FactoryId = f.Id)
              AND (sr.SupplyType      IS NULL OR sr.SupplyType = r.SupplyType)
              AND (sr.OwnershipType   IS NULL OR sr.OwnershipType = r.OwnershipType)
              AND (sr.QualityStatus   IS NULL OR sr.QualityStatus = r.QualityStatus)
            ORDER BY
                sr.Priority ASC,
                (CASE WHEN sr.ProductFamilyId IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN sr.FactoryId       IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN sr.SupplyType      IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN sr.OwnershipType   IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN sr.QualityStatus   IS NOT NULL THEN 1 ELSE 0 END) DESC,
                sr.Id ASC
        ) winner
        WHERE winner.IncludeFlag IS NULL OR winner.IncludeFlag = 1;

        SET @RowsAffected = @@ROWCOUNT;

        INSERT INTO APS_ETL_Log(BatchNo, Step, Message, Status, CreatedAt)
        VALUES(@BatchNo, 'sp_SyncPipelineSupply',
               CONCAT('V1 Timed Supply synchronized rows=', @RowsAffected),
               'SUCCESS', GETDATE());

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        INSERT INTO APS_ETL_Log(BatchNo, Step, Message, Status, CreatedAt)
        VALUES(ISNULL(@BatchNo,N'UNKNOWN'), 'sp_SyncPipelineSupply', @ErrorMessage, 'FAILED', GETDATE());
        THROW;
    END CATCH
END;
GO

-- =============================================
-- 2.9 MES 生产进度快照表（v5.0.41 新增）
-- =============================================
-- 定位：APS 本地存储从 ODS MES 进度汇总视图同步的工单/工序/大工艺进度快照
-- 消费方：5号位用于PI Position闭合，2号位据Position形成剩余生产需求/执行约束；1号位只消费已解析起点、剩余工艺和不可移动执行事实
-- 分工：2号位负责 APS 本地快照同步（sp_Sync*）；5号位负责 ODS 统一契约视图收口（各大工艺子视图 UNION ALL）
-- V1 口径：不接 MES 每条报工明细，只接 ODS 汇总后的工序级/大工艺级进度
-- V1 工序识别主字段：OperationName（不以 MES 工序编码为主匹配字段）
-- Task/Pegging 全量重算口径：随新 PlanVersionId 每日重新生成；MES 进度不匹配历史 TaskId
-- EAM 故障链路 V1 仅文档预留，不实际取数，不影响当前快照同步
-- =============================================

-- =============================================
-- 2.9.1 MESWorkOrderSnapshot（MES 工单快照）
-- =============================================

CREATE TABLE MESWorkOrderSnapshot (
    Id                       BIGINT        PRIMARY KEY IDENTITY(1,1),
    ScheduleRunId            INT           NOT NULL,                    -- 逻辑关联 ScheduleRun.Id；数据库未建立物理外键，由应用服务校验引用有效性（MES进度快照必须绑定具体排程运行）
    ProductionInstructionNo  NVARCHAR(100) NOT NULL,                    -- 生产指示号，对应 APS Order 中的生产指示号
    MESWorkOrderNo           NVARCHAR(100) NOT NULL,                    -- MES 中的工单号
    MaterialCode             NVARCHAR(100) NOT NULL,                    -- MES 工单生产的物料编码
    PlannedQty               DECIMAL(18,4) NOT NULL,                    -- MES 工单计划数量
    WorkOrderStatus          NVARCHAR(50)  NOT NULL,                    -- MES 工单当前状态
    SourceUpdatedAt          DATETIME2     NULL,                        -- MES 中该工单最后更新时间
    DataCutoffTime           DATETIME2     NOT NULL,                    -- 本次排程使用的数据截止时间
    CreatedAt                DATETIME2     NOT NULL DEFAULT GETDATE(),  -- APS 生成该快照的时间

    CONSTRAINT UQ_MESWorkOrderSnapshot_Key
        UNIQUE (ScheduleRunId, ProductionInstructionNo, MESWorkOrderNo, MaterialCode)
);
GO

CREATE INDEX IX_MESWorkOrderSnapshot_Run_InstructionNo
    ON MESWorkOrderSnapshot (ScheduleRunId, ProductionInstructionNo);
GO

-- =============================================
-- 2.9.2 OperationProgressSnapshot（工序进度快照）
-- =============================================
-- 汇总颗粒度：生产指示号 + MES工单号 + 物料编码 + 工序名称 + 大工艺阶段码
-- V1 工序识别主字段：OperationName（MES工序名称）；不以 MES 工序编码为主，编码不跨大工艺稳定
-- v5.1.2消费：OperationName + StageCode作为5号位PI Position/执行事实判定输入之一；不得由1号位直接据原始Progress建立第二套PI位置真相。
-- 注意：StageScopeType（EDGE / ROOT）属于BOM侧路径；2/5号位闭合Position后再向1号位提供已解析执行上下文。

CREATE TABLE OperationProgressSnapshot (
    Id                       BIGINT        PRIMARY KEY IDENTITY(1,1),
    ScheduleRunId            INT           NOT NULL,                    -- 逻辑关联 ScheduleRun.Id；数据库未建立物理外键，由应用服务校验引用有效性（MES进度快照必须绑定具体排程运行）
    ProductionInstructionNo  NVARCHAR(100) NOT NULL,                    -- 生产指示号
    MESWorkOrderNo           NVARCHAR(100) NOT NULL,                    -- MES 工单号
    MaterialCode             NVARCHAR(100) NOT NULL,                    -- 工序对应物料编码
    OperationName            NVARCHAR(200) NOT NULL,                    -- ⚠️ MES 工序名称；V1 工序识别主字段；不以 MES 工序编码为准（编码不跨大工艺稳定）
    StageCode                NVARCHAR(20)  NOT NULL,                    -- APS 大工艺阶段编码；格式 {工厂}_{类别}；与 OperationName 联合识别工序
    StageName                NVARCHAR(100) NULL,                        -- 大工艺中文名称
    PlannedQty               DECIMAL(18,4) NOT NULL,                    -- 该工序计划数量
    GoodQty                  DECIMAL(18,4) NOT NULL DEFAULT 0,          -- 截至数据截止时间，该工序累计良品完成数量
    ScrapQty                 DECIMAL(18,4) NULL,                        -- 累计报废数量（可选字段）
    ReworkQty                DECIMAL(18,4) NULL,                        -- 累计返工数量（可选字段）
    RemainingQty             AS (CASE
                                     WHEN PlannedQty IS NULL THEN NULL
                                     WHEN PlannedQty - ISNULL(GoodQty, 0) < 0 THEN CAST(0 AS DECIMAL(18,4))
                                     ELSE PlannedQty - ISNULL(GoodQty, 0)
                                 END) PERSISTED,                               -- 工序剩余数量（计算列，不低于 0）
    LastReportTime           DATETIME2     NULL,                        -- 该工序最后一次报工时间
    SourceUpdatedAt          DATETIME2     NULL,                        -- MES 汇总数据最后更新时间
    DataCutoffTime           DATETIME2     NOT NULL,                    -- 本次排程使用的数据截止时间
    CreatedAt                DATETIME2     NOT NULL DEFAULT GETDATE(),  -- APS 生成该快照的时间

    CONSTRAINT UQ_OperationProgressSnapshot_Key
        UNIQUE (ScheduleRunId, ProductionInstructionNo, MESWorkOrderNo, MaterialCode, OperationName, StageCode)
);
GO

CREATE INDEX IX_OperationProgressSnapshot_Run_InstructionNo
    ON OperationProgressSnapshot (ScheduleRunId, ProductionInstructionNo);
GO

-- =============================================
-- 2.9.3 StageProgressSnapshot（大工艺进度快照）
-- =============================================
-- 汇总颗粒度：生产指示号 + 物料编码 + 大工艺阶段码

CREATE TABLE StageProgressSnapshot (
    Id                       BIGINT        PRIMARY KEY IDENTITY(1,1),
    ScheduleRunId            INT           NOT NULL,                    -- 逻辑关联 ScheduleRun.Id；数据库未建立物理外键，由应用服务校验引用有效性（MES进度快照必须绑定具体排程运行）
    ProductionInstructionNo  NVARCHAR(100) NOT NULL,                    -- 生产指示号
    MaterialCode             NVARCHAR(100) NOT NULL,                    -- 大工艺阶段对应物料编码
    StageCode                NVARCHAR(20)  NOT NULL,                    -- APS 使用的大工艺阶段编码
    StageName                NVARCHAR(100) NULL,                        -- 大工艺中文名称
    PlannedQty               DECIMAL(18,4) NOT NULL,                    -- 该大工艺阶段计划数量
    GoodCompletedQty         DECIMAL(18,4) NOT NULL DEFAULT 0,          -- 截至数据截止时间，该阶段累计良品完成数量
    ScrapQty                 DECIMAL(18,4) NULL,                        -- 阶段累计报废数量（可选字段）
    ReworkQty                DECIMAL(18,4) NULL,                        -- 阶段累计返工数量（可选字段）
    RemainingQty             AS (CASE
                                     WHEN PlannedQty IS NULL THEN NULL
                                     WHEN PlannedQty - ISNULL(GoodCompletedQty, 0) < 0 THEN CAST(0 AS DECIMAL(18,4))
                                     ELSE PlannedQty - ISNULL(GoodCompletedQty, 0)
                                 END) PERSISTED,                               -- 阶段剩余数量（计算列，不低于 0）
    LastReportTime           DATETIME2     NULL,                        -- 该阶段最后一次报工时间
    SourceUpdatedAt          DATETIME2     NULL,                        -- MES 汇总数据最后更新时间
    DataCutoffTime           DATETIME2     NOT NULL,                    -- 本次排程使用的数据截止时间
    CreatedAt                DATETIME2     NOT NULL DEFAULT GETDATE(),  -- APS 生成该快照的时间

    CONSTRAINT UQ_StageProgressSnapshot_Key
        UNIQUE (ScheduleRunId, ProductionInstructionNo, MaterialCode, StageCode)
);
GO

CREATE INDEX IX_StageProgressSnapshot_Run_InstructionNo
    ON StageProgressSnapshot (ScheduleRunId, ProductionInstructionNo);
GO

-- =============================================
-- sp_SyncMESWorkOrderSnapshot（v5.0.41 新增）
-- =============================================
-- 从 ODS.MES_APS_WorkOrder_View 同步到 MESWorkOrderSnapshot
-- 分工：2号位实现（APS 本地快照落库）；ODS 统一视图由 5号位负责收口
CREATE OR ALTER PROCEDURE sp_SyncMESWorkOrderSnapshot
    @ScheduleRunId   INT,
    @DataCutoffTime  DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RowsAffected INT = 0;
    DECLARE @ErrorMessage NVARCHAR(MAX);

    BEGIN TRY
        BEGIN TRANSACTION;

        DELETE FROM MESWorkOrderSnapshot WHERE ScheduleRunId = @ScheduleRunId;

        INSERT INTO MESWorkOrderSnapshot (
            ScheduleRunId, ProductionInstructionNo, MESWorkOrderNo, MaterialCode,
            PlannedQty, WorkOrderStatus, SourceUpdatedAt, DataCutoffTime, CreatedAt
        )
        SELECT
            @ScheduleRunId,
            v.ProductionInstructionNo,
            v.MESWorkOrderNo,
            v.MaterialCode,
            v.PlannedQty,
            v.WorkOrderStatus,
            v.SourceUpdatedAt,
            @DataCutoffTime,
            GETDATE()
        FROM [MES_Integration].[dbo].[MES_APS_WorkOrder_View] v
        WHERE v.SourceUpdatedAt IS NULL            -- NULL 时允许透传；ODS 视图暂无来源时间时已登记 ETL 日志
           OR v.SourceUpdatedAt <= @DataCutoffTime;

        SET @RowsAffected = @@ROWCOUNT;
        COMMIT TRANSACTION;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (CAST(@ScheduleRunId AS NVARCHAR(50)), 'sp_SyncMESWorkOrderSnapshot',
                CONCAT('WorkOrderSnapshot rows=', @RowsAffected), 'SUCCESS', GETDATE());

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (CAST(@ScheduleRunId AS NVARCHAR(50)), 'sp_SyncMESWorkOrderSnapshot', @ErrorMessage, 'FAILED', GETDATE());
    END CATCH
END;
GO

-- =============================================
-- sp_SyncOperationProgressSnapshot（v5.0.41 新增）
-- =============================================
-- 从 ODS.MES_APS_OperationProgress_View 同步到 OperationProgressSnapshot
-- 分工：2号位实现；ODS 统一视图由 5号位 UNION ALL 收口（各大工艺子视图）
-- V1 工序识别主字段 = OperationName；不以 MES 工序编码为主匹配字段
CREATE OR ALTER PROCEDURE sp_SyncOperationProgressSnapshot
    @ScheduleRunId   INT,
    @DataCutoffTime  DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RowsAffected INT = 0;
    DECLARE @ErrorMessage NVARCHAR(MAX);

    BEGIN TRY
        BEGIN TRANSACTION;

        DELETE FROM OperationProgressSnapshot WHERE ScheduleRunId = @ScheduleRunId;

        INSERT INTO OperationProgressSnapshot (
            ScheduleRunId, ProductionInstructionNo, MESWorkOrderNo, MaterialCode,
            OperationName, StageCode, StageName, PlannedQty, GoodQty,
            ScrapQty, ReworkQty, LastReportTime, SourceUpdatedAt, DataCutoffTime, CreatedAt
        )
        SELECT
            @ScheduleRunId,
            v.ProductionInstructionNo,
            v.MESWorkOrderNo,
            v.MaterialCode,
            v.OperationName,
            v.StageCode,
            v.StageName,
            v.PlannedQty,
            v.GoodQty,
            v.ScrapQty,
            v.ReworkQty,
            v.LastReportTime,
            v.SourceUpdatedAt,
            @DataCutoffTime,
            GETDATE()
        FROM [MES_Integration].[dbo].[MES_APS_OperationProgress_View] v
        WHERE COALESCE(v.SourceUpdatedAt, v.LastReportTime) IS NULL   -- 来源时间均为 NULL 时允许透传
           OR COALESCE(v.SourceUpdatedAt, v.LastReportTime) <= @DataCutoffTime;

        SET @RowsAffected = @@ROWCOUNT;
        COMMIT TRANSACTION;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (CAST(@ScheduleRunId AS NVARCHAR(50)), 'sp_SyncOperationProgressSnapshot',
                CONCAT('OperationProgressSnapshot rows=', @RowsAffected), 'SUCCESS', GETDATE());

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (CAST(@ScheduleRunId AS NVARCHAR(50)), 'sp_SyncOperationProgressSnapshot', @ErrorMessage, 'FAILED', GETDATE());
    END CATCH
END;
GO

-- =============================================
-- sp_SyncStageProgressSnapshot（v5.0.41 新增）
-- =============================================
-- 从 ODS.MES_APS_StageProgress_View 同步到 StageProgressSnapshot
-- 分工：2号位实现；ODS 统一视图由 5号位 UNION ALL 收口（各大工艺子视图）
CREATE OR ALTER PROCEDURE sp_SyncStageProgressSnapshot
    @ScheduleRunId   INT,
    @DataCutoffTime  DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RowsAffected INT = 0;
    DECLARE @ErrorMessage NVARCHAR(MAX);

    BEGIN TRY
        BEGIN TRANSACTION;

        DELETE FROM StageProgressSnapshot WHERE ScheduleRunId = @ScheduleRunId;

        INSERT INTO StageProgressSnapshot (
            ScheduleRunId, ProductionInstructionNo, MaterialCode,
            StageCode, StageName, PlannedQty, GoodCompletedQty,
            ScrapQty, ReworkQty, LastReportTime, SourceUpdatedAt, DataCutoffTime, CreatedAt
        )
        SELECT
            @ScheduleRunId,
            v.ProductionInstructionNo,
            v.MaterialCode,
            v.StageCode,
            v.StageName,
            v.PlannedQty,
            v.GoodCompletedQty,
            v.ScrapQty,
            v.ReworkQty,
            v.LastReportTime,
            v.SourceUpdatedAt,
            @DataCutoffTime,
            GETDATE()
        FROM [MES_Integration].[dbo].[MES_APS_StageProgress_View] v
        WHERE COALESCE(v.SourceUpdatedAt, v.LastReportTime) IS NULL   -- 来源时间均为 NULL 时允许透传
           OR COALESCE(v.SourceUpdatedAt, v.LastReportTime) <= @DataCutoffTime;

        SET @RowsAffected = @@ROWCOUNT;
        COMMIT TRANSACTION;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (CAST(@ScheduleRunId AS NVARCHAR(50)), 'sp_SyncStageProgressSnapshot',
                CONCAT('StageProgressSnapshot rows=', @RowsAffected), 'SUCCESS', GETDATE());

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (CAST(@ScheduleRunId AS NVARCHAR(50)), 'sp_SyncStageProgressSnapshot', @ErrorMessage, 'FAILED', GETDATE());
    END CATCH
END;
GO

-- =============================================
-- 2.7 计划版本表（修改：新增快照字段）
-- =============================================

CREATE TABLE PlanVersion (
    Id INT PRIMARY KEY IDENTITY(1,1),
    VersionCode NVARCHAR(50) NOT NULL UNIQUE,
    VersionType NVARCHAR(50) NOT NULL,
    DomainKey NVARCHAR(100) NULL,                       -- ⚠️ V1 必填语义：DAILY_BASELINE / RESCHEDULE_CANDIDATE / LOCAL_RESCHEDULE_CANDIDATE 类正式版本，DomainKey 业务上必须有值（V1 不强制改库 NOT NULL，但创建/激活服务须校验非空）
    PlanHorizonStart DATE NOT NULL,
    PlanHorizonEnd DATE NOT NULL,
    ComputeMode NVARCHAR(50) NOT NULL,               -- 兼容/诊断字段；V1正式90天计划统一进入同一有限产能Solver，ROUGH_CUT/CRITICAL_PATH历史值不得选择第二套远期引擎
    Status NVARCHAR(50) NOT NULL,
    StartedAt DATETIME2 NULL,
    CompletedAt DATETIME2 NULL,
    DurationSeconds INT NULL,
    TotalOrders INT NULL,
    TotalTasks INT NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    -- ⚠️ 新增：快照封存字段
    BatchNo NVARCHAR(50) NULL,                       -- BOM批次号
    SnapshotFilePath NVARCHAR(500) NULL,             -- 快照文件路径
    SnapshotFileSize BIGINT NULL,                    -- 快照文件大小（字节）
    SnapshotFileHash NVARCHAR(64) NULL,              -- 快照文件哈希（SHA256）
    SnapshotCompressedSize BIGINT NULL,              -- 压缩后大小（字节）
    SnapshotCreatedAt DATETIME2 NULL,                -- 快照创建时间
    CreatedBy NVARCHAR(100) NOT NULL,
    CreatedByUserId INT NULL,                        -- ⚠️ v2.6新增：发起排程的用户ID（关联APS_Auth.User）
    CreatedByUserName NVARCHAR(100) NULL,            -- ⚠️ v2.6新增：发起排程的用户名（冗余）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    ArchivedAt DATETIME2 NULL
);
GO

CREATE INDEX IX_PlanVersion_Query 
    ON PlanVersion(Status, VersionType, CreatedAt DESC);
CREATE INDEX IX_PlanVersion_Domain 
    ON PlanVersion(DomainKey, CreatedAt DESC) WHERE DomainKey IS NOT NULL;
CREATE INDEX IX_PlanVersion_Snapshot 
    ON PlanVersion(SnapshotCreatedAt DESC) 
    WHERE SnapshotFilePath IS NOT NULL;
CREATE INDEX IX_PlanVersion_Batch 
    ON PlanVersion(BatchNo) 
    WHERE BatchNo IS NOT NULL;
CREATE INDEX IX_PlanVersion_CreatedByUser
    ON PlanVersion(CreatedByUserId)
    WHERE CreatedByUserId IS NOT NULL;
GO

-- v5.0.x：PlanVersion 唯一约束（SQL Server 过滤唯一索引；要求 SQL Server 2008+ 支持 WHERE 过滤索引）
--   ⚠️ 若目标库不支持过滤索引，等价实现：改用普通 UNIQUE 约束并将 NULL 域版本 DomainKey 置为占位值（如 'N/A'），
--      或建唯一计算列 ISNULL(DomainKey,'#NULL#')；此处采用过滤索引以保留 DomainKey 可为 NULL 的语义。
-- UQ_PlanVersion_OneActivePerDomain：每 Domain 同时只有一个 ACTIVE 版本（业务：每域单一正式采用版本）
CREATE UNIQUE INDEX UQ_PlanVersion_OneActivePerDomain
    ON PlanVersion(DomainKey)
    WHERE Status = 'ACTIVE' AND DomainKey IS NOT NULL;
GO

-- v5.0.40: 延迟 FK（OrderBomRequestLink.PlanVersionId → PlanVersion.Id）
ALTER TABLE OrderBomRequestLink
    ADD CONSTRAINT FK_OBRL_PlanVersion FOREIGN KEY (PlanVersionId) REFERENCES PlanVersion(Id);
GO

-- =============================================
-- 2.7.1 订单标准化表（Order_Canonical）
-- =============================================
-- ⚠️ v2.5新增：订单标准化表，用于存储从ERP同步的原始订单数据
-- 业务用途：
--   1. 订单标准化存储：统一存储来自ERP的销售订单(SO)和生产指示(MTS)
--   2. 防腐层核心表：作为ERP和APS本地库之间的桥梁
--   3. 活跃根集合划定：每天00:00从此表筛选活跃订单（v5.0.21：不再按BOMNO去重，含无BOMNO订单）
--   4. 数据源：为Order分区表提供标准化的订单数据

CREATE TABLE Order_Canonical (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    OrderNo NVARCHAR(50) NOT NULL UNIQUE,            -- 订单号（业务主键）
    MaterialCode NVARCHAR(100) NOT NULL,             -- 物料编码（业务主键）
    BOMNO NVARCHAR(50) NULL,                          -- v5.0.21 改可空；NULL=待5号位Workset阶段解析BOM入口
    SourceModel NVARCHAR(100) NULL,                  -- ⚠️ v5.0.27新增：ERP原始型号追溯（透传自ERP_Order_Staging.Model）；不替代MaterialCode
    Quantity DECIMAL(18,6) NOT NULL,                 -- 订单数量
    DueDate DATETIME2 NOT NULL,                      -- 交期/计划日期
    Status NVARCHAR(20) NOT NULL,                    -- 状态：Open/Released/Closed/Cancelled
    OrderType NVARCHAR(20) NOT NULL,                 -- 订单类型：SALES_ORDER（客户订单）/PRODUCTION_INSTRUCTION（生产指示）（v5.0.24重分类，原SO/MTO/MTS废弃）
    Priority INT NOT NULL DEFAULT 100,               -- 优先级
    CustomerCode NVARCHAR(50) NULL,                  -- 客户编码（SO订单）
    SourceSystem NVARCHAR(50) NOT NULL DEFAULT 'ERP',-- 来源系统
    SourceOrderId NVARCHAR(100) NULL,                -- 源系统订单ID
    SourceMasterID INT NULL,                         -- ERP的masterID
    FactoryCode NVARCHAR(50) NULL,                   -- APS标准化工厂编码（⚠️ 2026-04-09业务澄清：ERP原字段需规则转换，2026-04-03审计补充）
    UOM NVARCHAR(20) NULL,                           -- 计量单位（2026-04-03审计补充）
    -- ⚠️ 2026-04-09 v5.0.3 新增：源事实字段
    TransportMode NVARCHAR(20) NULL,                 -- 运输方式（海运/空运/陆运）
    CustomerName NVARCHAR(200) NULL,                 -- 客户名称
    MTS_InstructionNo NVARCHAR(50) NULL,             -- 生产指示号（来源于ERP生产指示表InstructionNo，≠OrderNo）
    -- ⚠️ 2026-04-09 v5.0.4 新增：源事实字段（v1.2增补）
    IssueDate DATE NULL,                             -- 订单发行/下发日期
    OriginalDueDate DATE NULL,                       -- 原始纳期（客户最初要求交期）；MTS时=DueDate
    ReceivedQty DECIMAL(18,4) NULL,                  -- 已入库数量（仅MTS）；SO订单为NULL
    -- ⚠️ 2026-04-09 v5.0.3 新增：APS衍生/标准化字段
    CustomerSegment NVARCHAR(50) NULL,               -- 客户区分（JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER/UNKNOWN）；由sp_ValidateAndPromoteOrders通过CustomerCode查CustomerCodeMap（IsActive=1）得到；CustomerCode为空→NULL；有值无匹配→UNKNOWN（v5.0.27口径收口）
    SalesOrderCategory NVARCHAR(50) NULL,            -- 销售类别（DIRECT_SALES/SALES_REPLENISHMENT）
    DemandMaturityStatus NVARCHAR(50) NULL,          -- 需求成熟度（PRE_CONFIRMED=事前确认/FORECAST=预测SHIKOMI）；v5.0.24收窄，DELAYED已拆出为独立字段DelayStatus
    -- ⚠️ 2026-04-09 v5.0.5 新增：APS衍生字段（客户分级）
    CustomerTier NVARCHAR(20) NULL,                   -- 客户分级（VIP>KEY_ACCOUNT>STANDARD>GENERAL）；当前主要启用VIP/GENERAL两档，KEY_ACCOUNT/STANDARD预留（v5.0.24补充）
    -- ⚠️ 2026-05-13 v5.0.24 新增：延迟状态（独立维度，与DemandMaturityStatus不同维度，禁止混用）
    DelayStatus NVARCHAR(20) NULL,                   -- 延迟状态：ON_TIME（未延迟）/FIRST_DELAY（首次延迟）/REPEATED_DELAY（二次及以上延迟）；APS衍生字段
    -- ⚠️ v5.0.27 新增：APS标准化订单业务属性（影响APS规则，消费方须识别UNKNOWN保守处理）
    NonStockShipmentType NVARCHAR(50) NULL,          -- 非在库出荷区分（APS标准化）：FULL_PURPLE_SLIP/DIFF_PURPLE_SLIP/UNKNOWN/NULL；来源：RawNonStockShipmentType映射
    OriginalOrderSource NVARCHAR(50) NULL,           -- 订单原始来源（APS标准化）：DAT/PO/UNKNOWN/NULL；来源：RawOrderSource映射
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    FOREIGN KEY (MaterialCode) REFERENCES Material(MaterialCode)
);
GO

CREATE INDEX IX_Order_Canonical_Material ON Order_Canonical(MaterialCode, Status);
CREATE INDEX IX_Order_Canonical_DueDate ON Order_Canonical(DueDate, Status);
CREATE INDEX IX_Order_Canonical_Type ON Order_Canonical(OrderType, Status);
CREATE INDEX IX_Order_Canonical_BOMNO ON Order_Canonical(BOMNO, Status) WHERE BOMNO IS NOT NULL;  -- v5.0.21 过滤NULL
-- v5.0.21 活跃根集合按活跃订单划定（不再依赖BOMNO去重）
CREATE INDEX IX_Order_Canonical_ActiveRoots 
    ON Order_Canonical(Status, OrderType, DueDate) 
    INCLUDE (BOMNO, MaterialCode, SourceMasterID);
-- ⚠️ 2026-04-03审计补充：sp_ValidateAndPromoteOrders的Upsert键索引
CREATE UNIQUE INDEX IX_Order_Canonical_UpsertKey 
    ON Order_Canonical(SourceSystem, SourceOrderId) 
    WHERE SourceOrderId IS NOT NULL;
GO

-- =============================================
-- 2.8 订单表（分区表）
-- =============================================
-- ⚠️ 说明：此表从Order_Canonical表同步数据，用于排产计算
-- ⚠️ 重组DDL执行时序：先建表+所有字段 → 数据初始化 → 创建索引

-- 1. 先确保Order表本身建好并包含所需基础字段
CREATE TABLE [Order] (
    Id BIGINT IDENTITY(1,1),
    PlanVersionId INT NOT NULL,                       -- ⚠️ 2026-04-03审计补充：分区键，与Task/Pegging一致
    OrderNo NVARCHAR(50) NOT NULL,
    OrderType NVARCHAR(20) NOT NULL,                  -- 订单类型：SALES_ORDER（客户订单）/PRODUCTION_INSTRUCTION（生产指示）（v5.0.24重分类）
    MaterialId INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductFamilyId INT NOT NULL FOREIGN KEY REFERENCES ProductFamily(Id),
    FactoryId INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    Quantity DECIMAL(18,4) NOT NULL,                  -- 需求净数量；SALES_ORDER已由ERP完成成品库存净额处理，APS不再二次扣成品库存
    UOM NVARCHAR(20) NOT NULL,
    CustomerDueDate DATE NOT NULL,
    PromisedDate DATE NULL,
    Priority INT NOT NULL DEFAULT 50,
    PriorityScore DECIMAL(10,2) NULL,                 -- 历史/展示兼容；V1需求排序权威=计算层→有序规则段→第一命中→段内排序，禁止重新用全局加权Score决定Pegging
    Status NVARCHAR(50) NOT NULL,
    DomainKey NVARCHAR(100) NULL,
    SourceSystem NVARCHAR(50) NOT NULL DEFAULT 'ERP',
    SourceOrderId NVARCHAR(100) NULL,
    -- ⚠️ 提前将所有业务防腐字段在此处一次性定义好
    MaterialCode NVARCHAR(100) NOT NULL,             -- ⚠️ 冗余字段，避免频繁JOIN Material表
    BOMNO NVARCHAR(50) NULL,                         -- ⚠️ 关联的BOMNO
    SourceMasterID INT NULL,                         -- ⚠️ 接收ERP的masterID
    MTS_InstructionNo NVARCHAR(50) NULL,             -- ⚠️ 生产指示号（2026-04-09修正：来源于Canonical真实值，≠OrderNo）
    -- ⚠️ 2026-04-09 v5.0.3 新增：订单业务字段
    TransportMode NVARCHAR(20) NULL,                 -- 运输方式（海运/空运/陆运）
    CustomerName NVARCHAR(200) NULL,                 -- 客户名称
    CustomerSegment NVARCHAR(50) NULL,               -- 客户区分（JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER/UNKNOWN）；透传自Order_Canonical（v5.0.27口径：CustomerCode为空→NULL；有值无匹配→UNKNOWN）
    SalesOrderCategory NVARCHAR(50) NULL,            -- 销售类别（DIRECT_SALES/SALES_REPLENISHMENT），APS衍生字段
    DemandMaturityStatus NVARCHAR(50) NULL,          -- 需求成熟度（PRE_CONFIRMED=事前确认/FORECAST=预测SHIKOMI）；v5.0.24收窄，DELAYED已拆出为DelayStatus
    -- ⚠️ 2026-04-09 v5.0.5 新增：APS衍生字段（客户分级）
    CustomerTier NVARCHAR(20) NULL,                   -- 客户分级（VIP>KEY_ACCOUNT>STANDARD>GENERAL）；当前主要启用VIP/GENERAL两档，KEY_ACCOUNT/STANDARD预留（v5.0.24补充）
    -- ⚠️ 2026-05-13 v5.0.24 新增：延迟状态（独立维度，与DemandMaturityStatus禁止混用）
    DelayStatus NVARCHAR(20) NULL,                   -- 延迟状态：ON_TIME（未延迟）/FIRST_DELAY（首次延迟）/REPEATED_DELAY（二次及以上延迟）；APS衍生字段
    -- ⚠️ 2026-04-09 v5.0.4 新增：源事实字段（v1.2增补）
    IssueDate DATE NULL,                             -- 订单发行/下发日期
    OriginalDueDate DATE NULL,                       -- 原始纳期（客户最初要求交期）；MTS时=DueDate
    ReceivedQty DECIMAL(18,4) NULL,                  -- 已入库数量（仅MTS）；SO订单为NULL
    -- ⚠️ 2026-05-16 v5.0.27 新增：排程快照V1进入展示字段
    SourceModel NVARCHAR(100) NULL,                  -- ERP原始型号（透传自Order_Canonical）
    NonStockShipmentType NVARCHAR(50) NULL,          -- 非在库出荷区分（APS标准化：FULL_PURPLE_SLIP/DIFF_PURPLE_SLIP/UNKNOWN）
    OriginalOrderSource NVARCHAR(50) NULL,           -- 订单原始来源（APS标准化：DAT/PO/UNKNOWN）
    -- ⚠️ v5.0.34 新增：APS本地快照与 ODS 订单稳定关联字段
    OrderCanonicalId BIGINT NULL,                    -- v5.0.34: 来源 Order_Canonical.Id；允许 NULL 兼容历史数据；新版本 sp_SyncOrdersToPartitionTable 必须写入
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_Order_No UNIQUE (OrderNo, PlanVersionId),
    CONSTRAINT PK_Order PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

-- 2. 若涉及存量数据更新，修正字段引用
-- ⚠️ v2.5修复：Material表字段名已统一为MaterialCode
UPDATE o
SET o.MaterialCode = m.MaterialCode
FROM [Order] o
INNER JOIN Material m ON o.MaterialId = m.Id;
GO

-- 3. 最后再建基于业务字段的复合索引
CREATE INDEX IX_Order_Query 
    ON [Order](Status, ProductFamilyId, FactoryId, CustomerDueDate);
GO

CREATE INDEX IX_Order_BOMNO 
    ON [Order](BOMNO) WHERE BOMNO IS NOT NULL;
GO

-- ⚠️ 支撑00:00活跃根集合的高效提取
CREATE INDEX IX_Order_ActiveRoots 
    ON [Order](Status, OrderType, CustomerDueDate) 
    INCLUDE (BOMNO, MaterialCode, SourceMasterID);
GO

-- v5.0.34: 支撑 OrderBomRequestLink 生成时按 PlanVersionId+OrderCanonicalId 找 OrderId
CREATE INDEX IX_Order_PlanVersion_OrderCanonical
    ON [Order](PlanVersionId, OrderCanonicalId)
    WHERE OrderCanonicalId IS NOT NULL;
GO

-- =============================================
-- 2.9 任务表（分区表）
-- =============================================

CREATE TABLE Task (
    Id BIGINT IDENTITY(1,1),
    PlanVersionId INT NOT NULL,
    TaskNo NVARCHAR(50) NOT NULL,
    OrderId BIGINT NULL,                              -- 兼容主展示字段；真实Demand归属由AllocationTaskShare多对多承接，不得作为唯一业务真相
    MaterialId INT NOT NULL,
    OperationSeq INT NOT NULL,
    OperationCode NVARCHAR(50) NOT NULL,
    ResourceId INT NULL,
    ResourceGroupId INT NULL,
    Quantity DECIMAL(18,4) NOT NULL,                  -- FinalTask净合格产出数量（NetOutputQty）
    PlannedProcessQty DECIMAL(18,4) NULL,                -- v5.1.2：计划加工数量；考虑计划良率后用于有限产能占用。新V1生产Task必须有值，历史记录可NULL兼容
    UOM NVARCHAR(20) NOT NULL,
    PlannedStartTime DATETIME2 NULL,
    PlannedEndTime DATETIME2 NULL,
    Duration DECIMAL(18,4) NULL,
    Status NVARCHAR(50) NOT NULL,
    IsLocked BIT NOT NULL DEFAULT 0,
    IsCriticalPath BIT NOT NULL DEFAULT 0,
    TaskType NVARCHAR(50) NOT NULL,                   -- V1新生成只允许PRODUCTION；TRANSFER/PROCUREMENT仅历史兼容，不建设有限物流/采购Task
    MTS_InstructionNo NVARCHAR(50) NULL,          -- ⚠️ 2026-04-09 v5.0.3：生产指示号，从Order冗余避免反查
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_Task PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

CREATE INDEX IX_Task_Query 
    ON Task(PlanVersionId, ResourceId, PlannedStartTime, PlannedEndTime)
    INCLUDE (Status, Duration)
    ON PS_PlanVersion(PlanVersionId);

CREATE INDEX IX_Task_Order 
    ON Task(OrderId, PlanVersionId)
    WHERE OrderId IS NOT NULL
    ON PS_PlanVersion(PlanVersionId);
GO

-- =============================================
-- 2.9b AllocationTaskShare（Allocation↔FinalTask数量份额，v5.1.2新增）
-- =============================================
-- 权威关系：FinalTask真实Demand归属。一个Task可承接多个Demand，一个Demand可拆至多个Task。
-- 不因此新增PeggingAllocationLedger物理表；AllocationSequence在2号位Demand/Supply原子扣减成功时生成。
CREATE TABLE AllocationTaskShare (
    Id                  BIGINT IDENTITY(1,1),
    PlanVersionId       INT NOT NULL,
    AllocationSequence  BIGINT NOT NULL,
    DemandType          NVARCHAR(50) NOT NULL,
    DemandKey           NVARCHAR(200) NOT NULL,
    RootOrderId         BIGINT NULL,
    TaskId              BIGINT NOT NULL,
    ShareQty            DECIMAL(18,4) NOT NULL,
    CreatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_AllocationTaskShare PRIMARY KEY (Id, PlanVersionId),
    CONSTRAINT CK_AllocationTaskShare_Qty CHECK (ShareQty > 0)
) ON PS_PlanVersion(PlanVersionId);
GO
CREATE INDEX IX_ATS_Allocation
    ON AllocationTaskShare(PlanVersionId, AllocationSequence)
    INCLUDE (TaskId, ShareQty, DemandType, DemandKey)
    ON PS_PlanVersion(PlanVersionId);
CREATE INDEX IX_ATS_Task
    ON AllocationTaskShare(TaskId, PlanVersionId)
    INCLUDE (AllocationSequence, ShareQty, DemandKey)
    ON PS_PlanVersion(PlanVersionId);
GO

-- =============================================
-- 2.10 Pegging表（分区表）
-- =============================================

CREATE TABLE Pegging (
    Id BIGINT IDENTITY(1,1),
    PlanVersionId INT NOT NULL,
    UpstreamTaskId BIGINT NOT NULL,
    DownstreamTaskId BIGINT NOT NULL,
    UpstreamMaterialId INT NOT NULL,
    DownstreamMaterialId INT NOT NULL,
    Quantity DECIMAL(18,4) NOT NULL,
    UOM NVARCHAR(20) NOT NULL,
    PeggingType NVARCHAR(50) NOT NULL,
    LeadTimeDays INT NOT NULL DEFAULT 0,
    IsCrossDomain BIT NOT NULL DEFAULT 0,              -- ⚠️ 2026-04-03审计补充：1=跨域，0=单域
    AllocatedQuantity DECIMAL(18,4) NULL,              -- ⚠️ 2026-04-03审计补充：该Pegging分配的具体数量
    InheritedPriority INT NULL,                        -- ⚠️ 2026-04-03审计补充：继承的优先级
    AllocationReason NVARCHAR(200) NULL,               -- ⚠️ 2026-04-03审计补充：分配理由
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_Pegging PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

CREATE INDEX IX_Pegging_Upstream 
    ON Pegging(UpstreamTaskId, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);

CREATE INDEX IX_Pegging_Downstream 
    ON Pegging(DownstreamTaskId, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);
GO

-- =============================================
-- 2.10b PeggingSupplyAllocation（非Task供给分配极简结果，v5.1.2补齐物理DDL）
-- =============================================
-- 只记录已确认分配结果，不是候选Supply表，也不是第二真相源。
-- PI内部Stage/XC/PI级在途属于PI Position，禁止作为额外Supply重复写入。
CREATE TABLE PeggingSupplyAllocation (
    Id                       BIGINT IDENTITY(1,1),
    PlanVersionId            INT NOT NULL,
    ScheduleRunId            INT NOT NULL,
    AllocationSequence       BIGINT NOT NULL,
    BatchNo                  NVARCHAR(50) NULL,
    RootOrderId              BIGINT NULL,
    RootOrderNo              NVARCHAR(100) NULL,
    CurrentOrderId           BIGINT NULL,
    CurrentOrderNo           NVARCHAR(100) NULL,
    OrderType                NVARCHAR(50) NULL,
    WorksetId                BIGINT NULL,
    MaterialId               INT NOT NULL,
    MaterialCode             NVARCHAR(100) NOT NULL,
    DemandFactoryCode        NVARCHAR(50) NULL,
    DemandStageCode          NVARCHAR(50) NULL,
    DemandQty                DECIMAL(18,4) NULL,
    AllocatedQty             DECIMAL(18,4) NOT NULL,
    SupplyType               NVARCHAR(50) NOT NULL,
    SupplyFactoryCode        NVARCHAR(50) NULL,
    SupplyWarehouseCode      NVARCHAR(50) NULL,
    ERPProperty              NVARCHAR(20) NULL,
    AttachStageCode          NVARCHAR(50) NULL,
    CompletedStageCode       NVARCHAR(50) NULL,
    NextRequiredStageCode    NVARCHAR(50) NULL,
    RemainingStagePathJson   NVARCHAR(MAX) NULL,
    SupplyMode               NVARCHAR(50) NULL,
    CrossFactoryEdgeId       BIGINT NULL,
    TransportLeadTimeHours   INT NULL,
    ETA                      DATETIME2 NULL,
    KnownAvailableTime       DATETIME2 NULL,
    CommitmentStatus         NVARCHAR(30) NULL,
    SupplyDocumentType       NVARCHAR(50) NULL,
    SupplyDocumentNo         NVARCHAR(100) NULL,
    CreatedAt                DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_PeggingSupplyAllocation PRIMARY KEY (Id, PlanVersionId),
    CONSTRAINT CK_PSA_AllocatedQty CHECK (AllocatedQty > 0)
) ON PS_PlanVersion(PlanVersionId);
GO
CREATE UNIQUE INDEX UX_PSA_AllocationSequence
    ON PeggingSupplyAllocation(PlanVersionId, AllocationSequence)
    ON PS_PlanVersion(PlanVersionId);
CREATE INDEX IX_PSA_Material
    ON PeggingSupplyAllocation(PlanVersionId, MaterialId, SupplyType)
    INCLUDE (AllocatedQty, KnownAvailableTime, CommitmentStatus)
    ON PS_PlanVersion(PlanVersionId);
CREATE INDEX IX_PSA_Document
    ON PeggingSupplyAllocation(SupplyDocumentNo, PlanVersionId)
    WHERE SupplyDocumentNo IS NOT NULL
    ON PS_PlanVersion(PlanVersionId);
GO

-- =============================================
-- 2.11 集成数据同步表
-- =============================================

-- ERP订单同步表（Staging）
-- ⚠️ 2026-04-03 订单链路审计修正：补SourceSystem/SourceMasterID，统一DueDate（BOMNO旧口径"必填"已废除，见v5.0.21）
-- ⚠️ 2026-04-09 v5.0.3 订单业务字段补充：+6列（TransportMode/CustomerName/MTS_InstructionNo/CustomerSegment/SalesOrderCategory/DemandMaturityStatus）
-- ⚠️ 2026-04-09 v5.0.4 订单ETL v1.2增补：+3列（IssueDate/OriginalDueDate/ReceivedQty）
-- ⚠️ v5.0.21 2026-05-08：BOMNO改可空 + 新增FailureCode/NextActionCode（两个独立维度，禁止混用）
CREATE TABLE ERP_Order_Staging (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    SourceOrderId NVARCHAR(100) NOT NULL,            -- 源系统订单ID
    SourceSystem NVARCHAR(50) NOT NULL DEFAULT 'ERP',-- 来源系统（2026-04-03审计补充）
    SourceMasterID INT NULL,                         -- ERP物理主键（2026-04-03审计补充）
    OrderNo NVARCHAR(50) NOT NULL,
    OrderType NVARCHAR(20) NOT NULL,                 -- APS标准化订单类型：SALES_ORDER（客户订单，原SO/MTO）/PRODUCTION_INSTRUCTION（生产指示，原MTS/SS/SS_U）；由sp_ValidateAndPromoteOrders根据ZPQF映射得到（v5.0.24重分类）
    MaterialCode NVARCHAR(100) NULL,                 -- ⚠️ v5.0.27改可空：SP Step 0 三级解析链写入前可为空；进入Order_Canonical前SP强制验证非空
    Model NVARCHAR(100) NULL,                        -- ⚠️ v5.0.27新增：ERP原始型号（透传）；用于BOMNO=NULL时5号位BOM入口解析辅助；不替代MaterialCode
    FactoryCode NVARCHAR(50) NULL,                   -- ⚠️ v5.0.27改可空：APS衍生字段，V1 TODO桩（当前透传ERP原始值，NULL时允许进入Canonical）；V2补充规则转换（见STEP 2e）
    Quantity DECIMAL(18,4) NOT NULL,
    UOM NVARCHAR(20) NOT NULL,
    DueDate DATE NOT NULL,                           -- ⚠️ 2026-04-03修正：原CustomerDueDate统一为DueDate
    Priority INT NOT NULL DEFAULT 50,
    BOMNO NVARCHAR(50) NULL,                          -- v5.0.21 改可空；有值=显式BOMNO；NULL=待5号位解析BOM入口（2026-04-03旧"必填"口径废除）
    -- ⚠️ 2026-04-09 v5.0.3 新增：源事实字段
    TransportMode NVARCHAR(20) NULL,                 -- 运输方式（海运/空运/陆运），源事实字段
    CustomerName NVARCHAR(200) NULL,                 -- 客户名称，源事实字段
    CustomerCode NVARCHAR(50) NULL,                  -- ⚠️ v5.0.27新增：ERP原始客户代码（透传）；用于通过CustomerCodeMap派生CustomerSegment/CustomerTier
    MTS_InstructionNo NVARCHAR(50) NULL,             -- 生产指示号（来源于ERP生产指示表InstructionNo，≠OrderNo），源事实字段
    -- ⚠️ 2026-04-09 v5.0.4 新增：源事实字段（v1.2增补）
    IssueDate DATE NULL,                             -- 订单发行/下发日期，源事实字段
    OriginalDueDate DATE NULL,                       -- 原始纳期（客户最初要求交期），源事实字段；MTS时=DueDate
    ReceivedQty DECIMAL(18,4) NULL,                  -- 已入库数量（仅MTS），源事实字段；SO订单为NULL
    -- ⚠️ v5.0.27 新增：ERP原始值字段（Raw前缀=未标准化；由sp_ValidateAndPromoteOrders标准化后写入Order_Canonical对应字段）
    RawNonStockShipmentType NVARCHAR(50) NULL,       -- ERP原始非在库出荷区分（全额紫票/差额紫票等）；标准化→Canonical.NonStockShipmentType
    RawOrderSource NVARCHAR(50) NULL,                -- ERP原始订单来源（DAT/P/O等）；标准化→Canonical.OriginalOrderSource
    -- ⚠️ 2026-04-09 v5.0.3 新增：APS衍生/标准化字段（由sp_ValidateAndPromoteOrders标准化）
    CustomerSegment NVARCHAR(50) NULL,               -- 客户区分（JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER/UNKNOWN）；APS衍生，由sp_ValidateAndPromoteOrders通过CustomerCode查CustomerCodeMap（IsActive=1）得到；CustomerCode为空→NULL；有值无匹配→UNKNOWN（v5.0.27口径收口）
    SalesOrderCategory NVARCHAR(50) NULL,            -- 销售类别（DIRECT_SALES/SALES_REPLENISHMENT），APS衍生字段
    DemandMaturityStatus NVARCHAR(50) NULL,          -- 需求成熟度（PRE_CONFIRMED=事前确认/FORECAST=预测SHIKOMI）；v5.0.24收窄，DELAYED已拆出为独立字段DelayStatus
    -- ⚠️ 2026-04-09 v5.0.5 新增：APS衍生字段（客户分级）
    CustomerTier NVARCHAR(20) NULL,                   -- 客户分级（VIP>KEY_ACCOUNT>STANDARD>GENERAL）；当前主要启用VIP/GENERAL两档，KEY_ACCOUNT/STANDARD预留；由sp_ValidateAndPromoteOrders推导（v5.0.24补充等级说明）
    -- ⚠️ 2026-05-13 v5.0.24 新增：延迟状态（独立维度，与DemandMaturityStatus不同维度，禁止混用）
    DelayStatus NVARCHAR(20) NULL,                   -- 延迟状态：ON_TIME（未延迟）/FIRST_DELAY（首次延迟）/REPEATED_DELAY（二次及以上延迟）；APS衍生字段，由sp_ValidateAndPromoteOrders推导
    RawData NVARCHAR(MAX) NULL,                      -- ERP原始报文JSON
    SyncStatus NVARCHAR(50) NOT NULL DEFAULT 'PENDING', -- PENDING/VALIDATED/FAILED/PROCESSED（技术流转状态）
    FailureCode NVARCHAR(50) NULL,                   -- v5.0.21 失败原因维度（独立）：ORDER_FIELD_INVALID / MASTER_NOT_READY
    NextActionCode NVARCHAR(50) NULL,                -- v5.0.21 后续动作维度（独立）：BOM_REQUEST_SUBMITTED / BOM_REQUEST_RETRY_PENDING / MANUAL_ACTION_REQUIRED_NEW_MODEL / MANUAL_ACTION_DEFERRED / WAIT_NIGHTLY_RESYNC
    -- ⚠️ FailureCode与NextActionCode相互独立：FailureCode=原因，NextActionCode=动作；可同时有值，也可只有其一；禁止混用
    ErrorMessage NVARCHAR(MAX) NULL,                 -- 人类可读错误详情（补充FailureCode用）
    SyncedAt DATETIME2 NOT NULL DEFAULT GETDATE(),   -- 从ERP同步的时间
    ProcessedAt DATETIME2 NULL,                      -- 提升到Canonical的时间
    CONSTRAINT UQ_ERP_Order_Staging UNIQUE (SourceOrderId, SyncedAt)
);
GO

CREATE INDEX IX_ERP_Order_Staging_Status 
    ON ERP_Order_Staging(SyncStatus, SyncedAt DESC);
CREATE INDEX IX_ERP_Order_Staging_NextAction  -- v5.0.21 夜间重校/自动重试扫描用
    ON ERP_Order_Staging(NextActionCode, SyncStatus) WHERE NextActionCode IS NOT NULL;
GO

-- =============================================
-- 2.11b CustomerCodeMap（APS本地客户编码映射表）⚠️ 2026-05-13 v5.0.24 新增
-- =============================================
-- 定位：APS本地维护字典，不是ODS契约共享字典，不通过视图对外暴露
-- 来源：CustomerCode.xlsx 初始化导入，后续由APS系统管理员人工维护
-- 消费方：仅 sp_ValidateAndPromoteOrders，用于推导 ERP_Order_Staging.CustomerSegment
-- 映射规则（来源数据的"客户区分"字段）：
--   日本 → JAPAN    国内 → DOMESTIC    海外 → OVERSEAS    越南 → VIETNAM
--   跨厂 → INTER_FACTORY    其他 → OTHER
--   ⚠️ v5.0.27 口径修正（废止旧 "失效→OVERSEAS" 规则）：
--      IsActive=0 的行不参与 sp_ValidateAndPromoteOrders 派生（JOIN 过滤 ccm.IsActive=1）；
--      订单提升时 CustomerCode 有值但无 IsActive=1 有效匹配 → CustomerSegment=UNKNOWN；
--      追加 CUSTOMER_SEGMENT_UNKNOWN 诊断；不默认 OVERSEAS
CREATE TABLE CustomerCodeMap (
    CustomerCode    NVARCHAR(20)  NOT NULL,               -- ERP客户代码（PK，与订单中的CustomerCode对应）
    CustomerSegment NVARCHAR(50)  NOT NULL,               -- APS客户区分（JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER）
    IsActive        BIT           NOT NULL DEFAULT 1,     -- 是否有效（0=客户已失效，来源：ERP Activity字段）
    DescriptionChn  NVARCHAR(200) NULL,                   -- 中文名称（可读性，非业务字段）
    UpdatedAt       DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_CustomerCodeMap PRIMARY KEY (CustomerCode)
);
GO

CREATE INDEX IX_CustomerCodeMap_Segment ON CustomerCodeMap(CustomerSegment, IsActive);
GO

-- =============================================
-- 2.12 ExplainTrace（可解释性追踪表 - 分区表）⚠️ 2026-04-03审计补充
-- =============================================

CREATE TABLE ExplainTrace (
    Id BIGINT IDENTITY(1,1),
    PlanVersionId INT NOT NULL,
    TaskId BIGINT NOT NULL,
    TraceType NVARCHAR(50) NOT NULL,                  -- RESOURCE_SELECTION/TIME_CALCULATION/CONSTRAINT_VIOLATION
    TraceLevel NVARCHAR(20) NOT NULL DEFAULT 'INFO',  -- INFO/WARNING/ERROR
    Message NVARCHAR(MAX) NULL,                       -- 可读的决策说明
    ContextData NVARCHAR(MAX) NULL,                   -- JSON格式的详细数据
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_ExplainTrace PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

CREATE INDEX IX_ExplainTrace_Task 
    ON ExplainTrace(TaskId, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);

CREATE INDEX IX_ExplainTrace_Type 
    ON ExplainTrace(TraceType, TraceLevel, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);
GO

-- =============================================
-- 2.13 FenceConfig（冻结区/锁定区配置表）⚠️ 2026-04-03审计补充
-- =============================================

CREATE TABLE FenceConfig (
    Id INT PRIMARY KEY IDENTITY(1,1),
    ProductFamilyId INT NOT NULL FOREIGN KEY REFERENCES ProductFamily(Id),
    FactoryId INT NULL FOREIGN KEY REFERENCES Factory(Id),
    ProcessType NVARCHAR(50) NOT NULL,                -- ASSEMBLY/MACHINING/PROCUREMENT_DOMESTIC/PROCUREMENT_IMPORT
    FrozenDays INT NOT NULL,                          -- 冻结区天数：不允许修改
    FirmDays INT NOT NULL,                            -- Firm锁定区天数；调整需最小人工授权/确认边界，不强制完整OA审批流
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,                            -- NULL表示永久有效
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_FenceConfig_Query 
    ON FenceConfig(ProductFamilyId, FactoryId, ProcessType);
GO

-- =============================================
-- 2.14 TaskSplitRuleConfig（拆批参数来源；v5.1.2：由1号位Solver执行拆/合批，不代表2/5号位提前生成SplitVoucher/FinalTask）
-- =============================================

CREATE TABLE TaskSplitRuleConfig (
    Id INT PRIMARY KEY IDENTITY(1,1),
    MaterialId INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ResourceGroupId INT NULL,                         -- ⚠️ v5.0已废弃ResourceGroup，保留仅为兼容
    MinimumOrderQuantity DECIMAL(18,4) NULL,          -- MOQ：最小加工批量
    EconomicOrderQuantity DECIMAL(18,4) NULL,         -- EOQ：最大加工批量
    BottleneckSplitStrategy NVARCHAR(50) NULL,        -- PREFER_SPLIT/PREFER_MERGE
    NonBottleneckStrategy NVARCHAR(50) NULL,           -- PREFER_LARGE_BATCH/PREFER_SMALL_BATCH
    IsActive BIT NOT NULL DEFAULT 1,
    EffectiveFrom DATETIME2 NULL,
    EffectiveTo DATETIME2 NULL,                       -- NULL表示永久有效
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_TaskSplitRuleConfig_Query 
    ON TaskSplitRuleConfig(MaterialId, IsActive);
GO

-- =============================================
-- 2.15 StageLeadTimeParam（阶段提前期参数表）⚠️ v5.0.7新增
-- 用途：为无小工序的外协阶段、以及Routing数据不完整的阶段提供参数化提前期
-- 1号位消费：读取StageDetail阶段顺序 → 对无RoutingOperation的阶段查此表生成标准Task
-- 命中顺序（从细到粗）：
--   1) MaterialCode + FactoryCode + StageCode
--   2) ProductFamilyCode + FactoryCode + StageCode
--   3) ProductionDeptCode + FactoryCode + StageCode  -- v5.0.16 RENAME from WorkshopCode
--   4) FactoryCode + StageCode
--   5) 全局阶段默认值（IsDefault=1）
-- =============================================

CREATE TABLE StageLeadTimeParam (
    Id INT PRIMARY KEY IDENTITY(1,1),
    FactoryCode NVARCHAR(50) NOT NULL,                   -- 工厂编码
    StageCode NVARCHAR(50) NOT NULL,                      -- 大工艺阶段码（如TJ_OUTS/BJ_SURF）
    ProductionDeptCode NVARCHAR(50) NULL,                 -- 🔄 v5.0.16 RENAME from WorkshopCode；APS 自定义命中细粒度（纯字符串，不强 FK）
    MaterialCode NVARCHAR(50) NULL,                       -- 物料编码（可选，物料级精确匹配）
    ProductFamilyCode NVARCHAR(50) NULL,                  -- 产品族编码（可选，产品族级匹配）
    LeadTimeDays DECIMAL(18,2) NULL,                      -- 提前期（天）
    LeadTimeHours DECIMAL(18,2) NULL,                     -- 提前期（小时，更细粒度）
    Priority INT NOT NULL DEFAULT 100,                    -- 命中优先级（数值越小优先级越高）
    EffectiveFrom DATETIME2 NULL,                         -- 生效起始时间
    EffectiveTo DATETIME2 NULL,                           -- 生效截止时间（NULL=永久有效）
    IsDefault BIT NOT NULL DEFAULT 0,                     -- 1=全局阶段默认值（最低优先级兜底）
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_StageLeadTime_Match
    ON StageLeadTimeParam(StageCode, FactoryCode, IsActive)
    INCLUDE (MaterialCode, ProductFamilyCode, ProductionDeptCode, Priority, LeadTimeDays, LeadTimeHours);  -- v5.0.16 RENAME

CREATE INDEX IX_StageLeadTime_Default
    ON StageLeadTimeParam(StageCode, IsDefault) WHERE IsActive = 1 AND IsDefault = 1;
GO

-- =============================================
-- 2.6.12 跨产品族域依赖表（v5.0.9新增，2026-04-18）
-- 用途：01:50静态扫描跨产品族BOM血缘，固化域调度依赖关系
-- 消费方：3号位在02:00读取此表构建拓扑排序（Kahn算法），决定域调度顺序
-- 刷新频率：每日01:50全量TRUNCATE+INSERT（2号位执行）
-- 数据来源：APS_BOM_RAW + Material + ProductFamily
-- =============================================

CREATE TABLE Domain_Dependency (
    UpstreamDomainCode   NVARCHAR(50) NOT NULL,  -- 上游域（ProductFamily.Code，供应侧）
    DownstreamDomainCode NVARCHAR(50) NOT NULL,  -- 下游域（ProductFamily.Code，消耗侧）
    ChildMaterialCode    NVARCHAR(50) NOT NULL,  -- 关联的半成品物料编码（Material.MaterialCode）
    DefaultLeadTimeDays  INT NOT NULL DEFAULT 0,  -- 兼容缓存字段；0=尚未按真实工厂/Stage转运LT派生。V1禁止再把固定2天作为权威；调度只可使用已解析真实LT
    ScannedAt            DATETIME2 NOT NULL DEFAULT GETDATE(),  -- 扫描时间戳
    PRIMARY KEY (UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode)
);
GO

-- 3号位拓扑排序快速查询：按域对聚合
CREATE INDEX IX_DomainDep_Downstream
    ON Domain_Dependency(DownstreamDomainCode)
    INCLUDE (UpstreamDomainCode);

-- 2号位扫描时按物料查重
CREATE INDEX IX_DomainDep_Material
    ON Domain_Dependency(ChildMaterialCode)
    INCLUDE (UpstreamDomainCode, DownstreamDomainCode);
GO

-- =============================================
-- 第三部分: ODS库存储过程
-- =============================================

USE MES_Integration;
GO

-- =============================================
-- MES_BOM_Source_View（v5.0.26 新增，2026-05-15）
-- BOM 多源原始输入视图 — sp_RefreshBOMEdgeActive 的唯一合法数据来源
-- ⚠️ 与 MES_BOM_View 的关系（方向相反，不可互换）：
--   MES_BOM_Source_View = ERP/MES 物理源表的 ODS 归一化包装（刷新输入，只写）
--   MES_BOM_View        = MES_BOM_Edge_Active 的向下兼容包装（刷新输出，只读）
--   ❌ sp_RefreshBOMEdgeActive 禁止读 MES_BOM_View：TRUNCATE Edge_Active 后再读 MES_BOM_View 会读到空表
-- ⚠️ V1 骨架占位：5号位负责将 SELECT 替换为实际 ERP/MES 物理 BOM 源表的合并逻辑
--   字段契约必须与 MES_BOM_Edge_Active 一一对齐
-- 数据库：MES_Integration（ODS库）
-- =============================================
CREATE OR ALTER VIEW MES_BOM_Source_View AS
-- ⚠️ TODO（5号位实现）: 替换 placeholder 为实际 ERP + MES BOM 物理表的 UNION ALL + 裁决逻辑
-- 示例结构（实际 SELECT 来源由5号位按 ERP/MES 物理表路径实现；字段名变更由此视图吸震）：
SELECT
    CAST(NULL AS NVARCHAR(50))    AS BOMNO,
    CAST(NULL AS NVARCHAR(50))    AS ParentMaterialCode,
    CAST(NULL AS NVARCHAR(50))    AS ChildMaterialCode,
    CAST(NULL AS DECIMAL(18,6))   AS Quantity,
    CAST(1    AS BIT)             AS IsActive,
    CAST(1    AS BIT)             AS IsDefaultVersion,   -- V1: 裁决逻辑在此视图或SP中完成
    CAST(NULL AS NVARCHAR(50))    AS ParentProcRefCode,
    CAST(NULL AS NVARCHAR(50))    AS ChildProcRefCode,
    CAST(NULL AS NVARCHAR(50))    AS ChildSourceHintCode,
    CAST(NULL AS NVARCHAR(20))    AS SourceSystem,       -- 'ERP' / 'MES'
    CAST(NULL AS NVARCHAR(100))   AS SourceBOMId,
    CAST(NULL AS DATETIME2)       AS EffectiveFrom,
    CAST(NULL AS DATETIME2)       AS EffectiveTo
WHERE 1 = 0;   -- 占位：5号位替换为实际多源 UNION ALL + WHERE IsActive=1
GO

-- =============================================
-- 3.0 BOM边表刷新存储过程（v5.0.26 新增，2026-05-14；v5.0.26b 修订，2026-05-15）
-- sp_RefreshBOMEdgeActive: 从 MES_BOM_Source_View 全量刷新 MES_BOM_Edge_Active
-- 调用时机：每日凌晨（sp_ExpandBOMBatch_vNext 前驱）
-- RefreshLog 写入：RUNNING → COMPLETED / FAILED
-- ⚠️ FAILED 时不清空旧数据（保留上次成功版本），阻止 vNext 展开（前置校验）
-- ⚠️ 禁止读 MES_BOM_View（会循环读空表）；唯一来源 = MES_BOM_Source_View
-- =============================================
CREATE OR ALTER PROCEDURE sp_RefreshBOMEdgeActive
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @StartTime  DATETIME2    = GETDATE();
    DECLARE @RowCount   INT          = 0;
    DECLARE @LogId      BIGINT;

    -- 生成本次刷新批次号（格式 REF-{yyyyMMdd}-{NNN}，当日内自增序号）
    DECLARE @DateStr        NVARCHAR(8)  = FORMAT(@StartTime, 'yyyyMMdd');
    DECLARE @SeqNo          INT;
    SELECT @SeqNo = COUNT(*) + 1
    FROM MES_BOM_Edge_RefreshLog
    WHERE RefreshBatchNo LIKE 'REF-' + @DateStr + '-%';
    DECLARE @RefreshBatchNo NVARCHAR(50) =
        'REF-' + @DateStr + '-' + RIGHT('000' + CAST(@SeqNo AS NVARCHAR(3)), 3);

    -- 1. 写入 RefreshLog RUNNING（使用表定义字段名：RefreshBatchNo/RefreshType/StartTime）
    INSERT INTO MES_BOM_Edge_RefreshLog (RefreshBatchNo, RefreshType, Status, StartTime)
    VALUES (@RefreshBatchNo, 'FULL', 'RUNNING', @StartTime);
    SET @LogId = SCOPE_IDENTITY();

    -- 2. 用显式事务包住 TRUNCATE + INSERT（ROLLBACK 可恢复旧数据，解决"注释说保留旧数据实际已清空"的矛盾）
    BEGIN TRY
        BEGIN TRANSACTION;

        -- 2a. 全量替换（V1简化；V2 改 MERGE ON (BOMNO, ParentMaterialCode, ChildMaterialCode)）
        --     ⚠️ TRUNCATE 在显式事务内是可回滚的（SQL Server）
        TRUNCATE TABLE MES_BOM_Edge_Active;

        -- 2b. 从 MES_BOM_Source_View 写入（⚡ 唯一合法来源，绝对不走 MES_BOM_View）
        INSERT INTO MES_BOM_Edge_Active (
            BOMNO, ParentMaterialCode, ChildMaterialCode, Quantity,
            IsActive, IsDefaultVersion,
            ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
            SourceSystem, SourceBOMId,
            RefreshBatchNo, RefreshedAt
        )
        SELECT
            BOMNO, ParentMaterialCode, ChildMaterialCode, Quantity,
            IsActive, IsDefaultVersion,
            ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
            SourceSystem, SourceBOMId,
            @RefreshBatchNo, @StartTime
        FROM MES_BOM_Source_View;                   -- ⚡ 唯一合法来源

        SET @RowCount = @@ROWCOUNT;

        -- 2c. Fix2: RowCount=0 保护 —— 空结果拒绝标记 COMPLETED，回滚恢复旧数据
        --     场景：MES_BOM_Source_View 尚未实现（WHERE 1=0 占位）或源库无数据
        IF @RowCount = 0
        BEGIN
            ROLLBACK TRANSACTION;
            UPDATE MES_BOM_Edge_RefreshLog
            SET Status       = 'FAILED',
                EndTime      = GETDATE(),
                ErrorMessage = 'MES_BOM_Source_View 返回 0 行，已回滚（视图未实现或源数据为空）'
            WHERE Id = @LogId;
            RAISERROR('sp_RefreshBOMEdgeActive 中止：MES_BOM_Source_View 返回 0 行，拒绝写入空边表。', 16, 1);
            RETURN;
        END

        -- 3. 写入成功：COMMIT 并更新 RefreshLog COMPLETED
        COMMIT TRANSACTION;

        UPDATE MES_BOM_Edge_RefreshLog
        SET Status   = 'COMPLETED',
            EndTime  = GETDATE(),
            RowCount = @RowCount
        WHERE Id = @LogId;

    END TRY
    BEGIN CATCH
        -- 4. 任何异常：回滚（TRUNCATE 前未提交，旧数据完整保留）→ 标记 FAILED
        --    ⚠️ FAILED 状态阻止 sp_ExpandBOMBatch_vNext 执行（前置校验）
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        UPDATE MES_BOM_Edge_RefreshLog
        SET Status       = 'FAILED',
            EndTime      = GETDATE(),
            ErrorMessage = ERROR_MESSAGE()
        WHERE Id = @LogId;
        RAISERROR('sp_RefreshBOMEdgeActive 失败: %s', 16, 1, ERROR_MESSAGE());
    END CATCH;
END;
GO

-- =============================================
-- 3.1 批次BOM展开存储过程（⚠️ deprecated — 递归CTE版，v5.0.26起由 sp_ExpandBOMBatch_vNext 替代）
-- 保留兼容运行；待 vNext 全量验证后下线
-- =============================================
-- 3.1 批次BOM展开存储过程
CREATE PROCEDURE sp_ExpandBOMBatch
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @StartTime DATETIME2 = GETDATE();
    DECLARE @ExpandedRowCount INT = 0;
    
    -- 1. 更新批次状态为PROCESSING
    UPDATE MES_API_BOM_Request
    SET Status = 'PROCESSING',
        ProcessingStartTime = @StartTime
    WHERE BatchNo = @BatchNo;
    
    -- 2. 混合寻址Recursive CTE（v5.0.7：透传3个ERP BOM原始辅助字段）
    WITH BOM_Recursive AS (
        -- 第1层：基于BOMNO展开（定制配置）
        SELECT 
            b.BOMNO,
            b.ParentMaterialCode,
            b.ChildMaterialCode,
            b.Quantity,  -- ⚠️ 单位用量，不累乘！
            1 AS Level,
            CAST(b.ChildMaterialCode AS NVARCHAR(MAX)) AS Path,
            b.ParentProcRefCode,                         -- v5.0.7 父件工序参考码
            b.ChildProcRefCode,                          -- v5.0.7 子件工序参考码
            b.ChildSourceHintCode                        -- v5.0.7 子件来源提示码（ERP produce字段）
        FROM MES_BOM_View b
        INNER JOIN MES_API_BOM_Request_Detail d 
            ON b.BOMNO = d.RequestedBOMNO
        WHERE d.BatchNo = @BatchNo
          AND b.IsActive = 1
        
        UNION ALL
        
        -- 第2~N层：基于MaterialCode展开（标准版本）
        SELECT 
            r.BOMNO,
            b.ParentMaterialCode,
            b.ChildMaterialCode,
            b.Quantity,  -- ⚠️ 单位用量，不累乘！
            r.Level + 1 AS Level,
            r.Path + ' -> ' + b.ChildMaterialCode AS Path,
            b.ParentProcRefCode,                         -- v5.0.7 每层取当前边的值
            b.ChildProcRefCode,                          -- v5.0.7
            b.ChildSourceHintCode                        -- v5.0.7
        FROM BOM_Recursive r
        INNER JOIN MES_BOM_View b 
            ON r.ChildMaterialCode = b.ParentMaterialCode
        WHERE b.IsActive = 1
          AND b.IsDefaultVersion = 1
          AND r.Level < 10
          AND r.Path NOT LIKE '%' + b.ChildMaterialCode + '%'
    )
    INSERT INTO MES_APS_BOM_Workset (
        BatchNo,
        BOMNO,
        ParentMaterialCode,
        ChildMaterialCode,
        Quantity,
        Level,
        Path,
        ParentProcRefCode,                               -- v5.0.7
        ChildProcRefCode,                                -- v5.0.7
        ChildSourceHintCode,                             -- v5.0.7
        CreatedAt
    )
    SELECT 
        @BatchNo,
        BOMNO,
        ParentMaterialCode,
        ChildMaterialCode,
        Quantity,
        Level,
        Path,
        ParentProcRefCode,                               -- v5.0.7
        ChildProcRefCode,                                -- v5.0.7
        ChildSourceHintCode,                             -- v5.0.7
        GETDATE()
    FROM BOM_Recursive
    OPTION (MAXRECURSION 10);
    
    SET @ExpandedRowCount = @@ROWCOUNT;
    
    -- 3. 【v5.0.7双层结果 + v5.0.8 ROOT路径 + v5.0.10 工厂映射 + v5.0.11 降级容错】5号位后置回填
    -- ⚠️ 此处由5号位追加实现（建议独立 SP：sp_EnrichBOMWorkset）：
    --    a) EDGE路径：基于 ParentProcRefCode + ChildProcRefCode + ChildSourceHintCode + 对照表 综合推导
    --    b) UPDATE MES_APS_BOM_Workset 
    --       SET ChildRequiredStageCode = <推导结果>,
    --           ChildRequiredFactory  = <R17 推导：查 ProduceToFactoryMap 配置表 (v5.0.11)>
    --       WHERE BatchNo = @BatchNo;
    --    c) INSERT INTO MES_APS_BOM_Workset_StageDetail (...)
    --       VALUES (<StageScopeType='EDGE', 每条BOM边的完整大工艺顺序明细>)
    --    d) ROOT路径（v5.0.8）：取Level=1的ParentProcRefCode → 映射标准化阶段路径
    --       若映射后多条不一致 → 取最长路径 + 记WARNING日志（不静默并集）
    --       INSERT INTO MES_APS_BOM_Workset_StageDetail (...)
    --       VALUES (<StageScopeType='ROOT', ParentMaterialCode=NULL, ChildMaterialCode=根产品编码, IsSupplyThreshold=0>)
    --    e) 降级容错（v5.0.11，R27）：异常一律"降级 + 登记"，**永不阻塞批次**
    --       写入 MES_APS_BOM_Workset_Issues（含 DegradeAction 标签）：
    --       - LEAF                     → Severity=INFO   DegradeAction=STAGE_NULL
    --       - FACTORY_MISMATCH(_MULTI) → Severity=WARN   DegradeAction=FACTORY_FALLBACK（保留 BOM 原生链）
    --       - NO_STAGE / UNKNOWN_PROCCODE → Severity=WARN DegradeAction=STAGE_NULL（或部分链）
    --       - QUANTITY_INVALID          → Severity=WARN   DegradeAction=QTY_DEFAULT_1（按 1 兜底）
    --       - MISSING_PRODUCE           → Severity=WARN   DegradeAction=PRODUCE_DEFAULT_1（按 1 兜底）
    --       - CYCLIC_BOM                → Severity=ERROR  DegradeAction=CYCLE_SKIP（v5.0.11：首次访问保留 + 重复循环节点跳过，通过 visited 集防环；上面 CTE 的 `Path NOT LIKE '%Child%'` 即此策略）
    --       - EXPAND_FAILED             → Severity=CRITICAL DegradeAction=BOMNO_SKIP（try-catch 单个 BOMNo 异常，该树作废，其他 BOMNo 继续）
    -- ⚠️ v5.0.11：**取消"Severity IN ('ERROR','CRITICAL') 阻塞批次"策略**；批次状态机永远 → READY
    --             Issues 仅为事后处置优先级，不决定批次放行
    --             运营 SLA：INFO 忽略 / WARN 月度巡检（业务复核人员）/ ERROR 次日晨会 / CRITICAL 追责 SP 本身
    --             FAILED 分支仅保留给 SP 进程崩溃（tempdb 满、连接中断等极端情况）
    -- ✅ v5.0.18：调用 sp_EnrichBOMWorkset 执行回填（R17工厂映射 + 阶段链推导 + StageDetail + Issues）
    EXEC sp_EnrichBOMWorkset @BatchNo;
    
    -- 4. 更新批次状态为READY（v5.0.11：永远走到 READY，除非 SP 进程崩溃）
    -- ⚠️ 生产部署时，应在 ChildRequiredStageCode + ChildRequiredFactory 回填 + StageDetail + Issues 全部写入完成后才执行
    UPDATE MES_API_BOM_Request
    SET Status = 'READY',
        CompletedAt = GETDATE(),
        ProcessingDuration = DATEDIFF(SECOND, @StartTime, GETDATE()),
        ExpandedRowCount = @ExpandedRowCount
    WHERE BatchNo = @BatchNo;
    
    -- 5. 记录日志
    INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
    VALUES (@BatchNo, 'BOM展开完成，展开行数: ' + CAST(@ExpandedRowCount AS NVARCHAR(20)), GETDATE());
END;
GO

-- =============================================
-- 3.1b BOM批次迭代展开存储过程（v5.0.26 新增，2026-05-14；v5.0.26c 修订，2026-05-15）
-- ⚡ sp_ExpandBOMBatch_vNext — 完整管道：
--   #Request → #EntryCandidates → #EntryResolved → #WorksetRaw(WHILE迭代) → Workset
-- ⚠️ 禁止在此SP中引用 MES_BOM_View；直读 MES_BOM_Edge_Active
-- ⚠️ BOMNO NULL策略A：无订单BOMNO时生成 ResolvedBOMNO='MAT:{MaterialCode}'（Workset.BOMNO NOT NULL兼容）
-- ⚠️ #EntryCandidates 按入口（DISTINCT BOMNO）排名，不按边排名（避免误判 BOM_ENTRY_AMBIGUOUS）
-- ⚠️ 幂等：同BatchNo重跑自动清理旧 Workset/StageDetail/Issues 后重建
-- ⚠️ sp_ExpandBOMBatch（§3.1）已标记 deprecated，稳定后下线
-- =============================================
CREATE OR ALTER PROCEDURE sp_ExpandBOMBatch_vNext
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @StartTime        DATETIME2 = GETDATE();
    DECLARE @ExpandedRowCount INT       = 0;
    DECLARE @CurrentLevel     INT;
    DECLARE @RowsInserted     INT;

    -- 0. 前置校验：RefreshLog 最新记录必须 COMPLETED
    IF NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_RefreshLog
        WHERE Id = (SELECT MAX(Id) FROM MES_BOM_Edge_RefreshLog)
          AND Status = 'COMPLETED'
    )
    BEGIN
        RAISERROR('MES_BOM_Edge_Active 未完成刷新，禁止展开。请检查 MES_BOM_Edge_RefreshLog。', 16, 1);
        RETURN;
    END

    -- P7: 幂等保护 — 同 BatchNo 重跑时，清理旧 Workset/StageDetail/Issues 后重建
    IF EXISTS (SELECT 1 FROM MES_APS_BOM_Workset WHERE BatchNo = @BatchNo)
    BEGIN
        DELETE sd
        FROM MES_APS_BOM_Workset_StageDetail sd
        INNER JOIN MES_APS_BOM_Workset w ON sd.WorksetId = w.Id
        WHERE w.BatchNo = @BatchNo;

        DELETE FROM MES_APS_BOM_Workset        WHERE BatchNo = @BatchNo;
        DELETE FROM MES_APS_BOM_Workset_Issues WHERE BatchNo = @BatchNo;

        INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
        VALUES (@BatchNo, N'幂等重跑：已清理旧 Workset/StageDetail/Issues，准备重建', GETDATE());
    END

    BEGIN TRY
        -- 1. 初始化 PROCESSING 状态
        UPDATE MES_API_BOM_Request
        SET Status = 'PROCESSING', ProcessingStartTime = @StartTime
        WHERE BatchNo = @BatchNo;

        -- ── Stage A: 收集请求明细（#Request）────────────────────────────────────
        -- v5.0.32: RequestDetail 已删除 Model 字段；MaterialCode 为 NULL 则跳过并登记 MISSING_MATERIALCODE ERROR
        CREATE TABLE #Request (
            DetailId     BIGINT        NOT NULL,
            BOMNO        NVARCHAR(50)  NULL,
            MaterialCode NVARCHAR(50)  NOT NULL, -- 来自 RequestDetail.MaterialCode，保证非空
            FactoryCode  NVARCHAR(20)  NULL,
            OrderType    NVARCHAR(50)  NULL
        );

        INSERT INTO #Request (DetailId, BOMNO, MaterialCode, FactoryCode, OrderType)
        SELECT d.Id, NULLIF(NULLIF(d.RequestedBOMNO, N''), N'0'),  -- R33: 空字符串/'0'等价NULL
               d.MaterialCode,  -- v5.0.32: Model 已删除，直接取 MaterialCode
               d.FactoryCode, d.OrderType
        FROM MES_API_BOM_Request_Detail d
        WHERE d.BatchNo = @BatchNo
          AND d.MaterialCode IS NOT NULL; -- 跳过 MaterialCode 为空的行

        -- v5.0.32: MaterialCode 为 NULL → MISSING_MATERIALCODE ERROR（不阻塞批次）
        INSERT INTO MES_APS_BOM_Workset_Issues
            (BatchNo, BOMNO, IssueType, Severity, Detail, RequestDetailId, CreatedAt)
        SELECT @BatchNo,
               ISNULL(d.RequestedBOMNO, 'MAT:UNKNOWN'),   -- P1+P2: BOMNO NOT NULL 兼容
               'MISSING_MATERIALCODE', 'ERROR',
               'DetailId=' + CAST(d.Id AS NVARCHAR(20)) + ' RequestDetail.MaterialCode 为空，无法解析 BOM 入口，已跳过',
               d.Id,                              -- P1: 正确字段名 RequestDetailId
               GETDATE()
        FROM MES_API_BOM_Request_Detail d
        WHERE d.BatchNo = @BatchNo
          AND d.MaterialCode IS NULL;

        -- ── Stage B: 入口候选（#EntryCandidates）────────────────────────────────
        -- P4: 按入口（DISTINCT BOMNO per DetailId）排名，不按 BOM 边排名
        --     每个唯一 (DetailId, CandidateBOMNO) 组合是一个候选入口
        --     Case A（BOMNO IS NOT NULL）：检查入口存在性，无歧义，CandidateRank=1
        --     Case B（BOMNO IS NULL）：按 DISTINCT CandidateBOMNO 排名选最优 BOM 版本
        CREATE TABLE #EntryCandidates (
            DetailId       BIGINT        NOT NULL,
            CandidateBOMNO NVARCHAR(50)  NULL,     -- 来自 Edge_Active.BOMNO（可能为NULL=纯物料路由边）
            EntryParent    NVARCHAR(50)  NOT NULL,
            OrderType      NVARCHAR(50)  NULL,
            CandidateRank  INT           NOT NULL   -- 1=最优候选入口
        );

        -- Case A：有显式 BOMNO — 验证入口存在性即可，每个 DetailId 唯一候选
        INSERT INTO #EntryCandidates (DetailId, CandidateBOMNO, EntryParent, OrderType, CandidateRank)
        SELECT r.DetailId, r.BOMNO, r.MaterialCode, r.OrderType, 1
        FROM #Request r
        WHERE r.BOMNO IS NOT NULL
          AND EXISTS (
              SELECT 1 FROM MES_BOM_Edge_Active e  -- ⚡ 直读物化边表，不走 MES_BOM_View
              WHERE e.BOMNO = r.BOMNO
                AND e.ParentMaterialCode = r.MaterialCode
              -- R34: 显式BOMNO不过滤IsActive，含历史版本
          );

        -- Case B：无显式 BOMNO — 按 OrderType+MaterialCode前缀 分流（R28/R29/R31）

        -- Case B1（R28）: SALES_ORDER + ASSY% → ProcessCodeDict出口库过滤
        --   Step1: 查订单工厂出口库ProcessCode；若无（CN6课）→ 取母体工厂CN的出口库
        --   Step2: 按ParentProcRefCode IN @ExportCodes过滤首层BOMNO候选
        --   Step1兜底: 代理后仍无出口库 → 降级走R29+R17（下方B2/B3/B4处理）
        --   #R28ExportCodes 用临时表而非 CTE，以便 B2/B3/B4 可引用（判断降级）
        SELECT r.DetailId, p.ProcessCode
        INTO #R28ExportCodes
        FROM #Request r
        INNER JOIN ProcessCodeDict p
                ON p.FactoryCode    = r.FactoryCode
               AND p.WarehouseRole  = N'出口库'
               AND p.IsActive       = 1
        WHERE r.BOMNO IS NULL
          AND r.OrderType   = N'SALES_ORDER'
          AND r.MaterialCode LIKE N'ASSY%'
        UNION ALL
        SELECT r.DetailId, p.ProcessCode
        FROM #Request r
        INNER JOIN ProcessCodeDict p
                ON p.FactoryCode   = CASE r.FactoryCode WHEN N'CN6课' THEN N'CN' ELSE r.FactoryCode END
               AND p.WarehouseRole = N'出口库'
               AND p.IsActive      = 1
        WHERE r.BOMNO IS NULL
          AND r.OrderType   = N'SALES_ORDER'
          AND r.MaterialCode LIKE N'ASSY%'
          AND NOT EXISTS (
              SELECT 1 FROM ProcessCodeDict p2
              WHERE p2.FactoryCode   = r.FactoryCode
                AND p2.WarehouseRole = N'出口库'
                AND p2.IsActive      = 1
          );

        ;WITH R28_EntryGroups AS (
            SELECT r.DetailId, e.BOMNO AS CandidateBOMNO, r.MaterialCode AS EntryParent, r.OrderType,
                   MAX(CAST(e.IsDefaultVersion AS INT))                     AS HasDefault,
                   MIN(CASE WHEN e.SourceSystem = N'MES' THEN 0 ELSE 1 END) AS MESFirst
            FROM #Request r
            INNER JOIN #R28ExportCodes ec ON ec.DetailId = r.DetailId
            INNER JOIN MES_BOM_Edge_Active e  -- ⚡ 直读物化边表
                    ON e.ParentProcRefCode  = ec.ProcessCode
                   AND e.ParentMaterialCode = r.MaterialCode
                   AND e.IsActive = 1
            WHERE r.BOMNO IS NULL
              AND r.OrderType   = N'SALES_ORDER'
              AND r.MaterialCode LIKE N'ASSY%'
            GROUP BY r.DetailId, e.BOMNO, r.MaterialCode, r.OrderType
        )
        INSERT INTO #EntryCandidates (DetailId, CandidateBOMNO, EntryParent, OrderType, CandidateRank)
        SELECT DetailId, CandidateBOMNO, EntryParent, OrderType,
               ROW_NUMBER() OVER (
                   PARTITION BY DetailId
                   ORDER BY HasDefault DESC,
                            CASE WHEN CandidateBOMNO IS NOT NULL THEN 0 ELSE 1 END,
                            CandidateBOMNO DESC
               )
        FROM R28_EntryGroups;

        -- Case B2/B3/B4（R29/R31/other）: 按MaterialCode查入口；R29+降级R28需R17工厂过滤
        --   R29: SALES_ORDER + WIP%/RAW%  — 全量查 + R17工厂过滤（务必同工厂，空→BOM_ENTRY_NOT_FOUND）
        --   R28降级: SALES_ORDER + ASSY% + 无出口库 — 同R29逻辑，R17工厂过滤
        --   R31: PRODUCTION_INSTRUCTION   — 直查不做工厂过滤；后续 Stage C必写BOMNO_MISSING_PRODUCTION
        --   其他OrderType                 — 原有逻辑；后续登记ORDER_TYPE_UNKNOWN
        ;WITH EntryGroups AS (
            SELECT r.DetailId,
                   e.BOMNO                   AS CandidateBOMNO,  -- 入口级 BOMNO（可能NULL）
                   r.MaterialCode            AS EntryParent,
                   r.OrderType,
                   MAX(CAST(e.IsDefaultVersion AS INT))                     AS HasDefault,
                   MIN(CASE WHEN e.SourceSystem = N'MES' THEN 0 ELSE 1 END) AS MESFirst
            FROM #Request r
            INNER JOIN MES_BOM_Edge_Active e  -- ⚡ 直读物化边表，不走 MES_BOM_View
                   ON e.ParentMaterialCode = r.MaterialCode
            WHERE r.BOMNO IS NULL AND e.IsActive = 1
              -- 排除R28已实际写入候选的：SALES_ORDER+ASSY%+已在#EntryCandidates有结果
              -- ⚠️ 不能用#R28ExportCodes判断：有出口库码但BOM边ParentProcRefCode不在出口库集合时
              --    R28_EntryGroups同样找不到结果，#EntryCandidates无此DetailId，需降级B2/B3/B4
              AND NOT (r.OrderType = N'SALES_ORDER' AND r.MaterialCode LIKE N'ASSY%'
                       AND EXISTS (SELECT 1 FROM #EntryCandidates ec WHERE ec.DetailId = r.DetailId))
              -- R17 工厂过滤：SALES_ORDER+(WIP%/RAW%/降级ASSY%) 必须同工厂匹配
              -- PRODUCTION_INSTRUCTION 及其他 OrderType 不做工厂过滤
              AND (
                  NOT (r.OrderType = N'SALES_ORDER'
                       AND (r.MaterialCode LIKE N'WIP%' OR r.MaterialCode LIKE N'RAW%'
                            OR r.MaterialCode LIKE N'ASSY%'))
                  OR EXISTS (
                      SELECT 1 FROM ProcessCodeDict p
                      WHERE p.ProcessCode = e.ParentProcRefCode
                        AND p.FactoryCode  = r.FactoryCode
                        AND p.IsActive     = 1
                  )
              )
            GROUP BY r.DetailId, e.BOMNO, r.MaterialCode, r.OrderType
        )
        INSERT INTO #EntryCandidates (DetailId, CandidateBOMNO, EntryParent, OrderType, CandidateRank)
        SELECT DetailId, CandidateBOMNO, EntryParent, OrderType,
               ROW_NUMBER() OVER (
                   PARTITION BY DetailId
                   ORDER BY
                       CASE WHEN OrderType = N'PRODUCTION_INSTRUCTION' THEN MESFirst ELSE 0 END,
                       HasDefault DESC,
                       CASE WHEN CandidateBOMNO IS NOT NULL THEN 0 ELSE 1 END, -- 有BOMNO优先
                       CandidateBOMNO DESC
               )
        FROM EntryGroups;

        -- R37: R29 降级兜底 — SALES_ORDER+WIP%/RAW% R17工厂过滤后仍无入口时，
        --      去掉工厂过滤再查一次，并写 FACTORY_MISMATCH_FALLBACK WARN（不阻塞展开）
        -- Step1: 记录 R17 命中为空的 DetailId（此时 #EntryCandidates 尚无对应行）
        CREATE TABLE #R29FallbackIds (DetailId BIGINT NOT NULL PRIMARY KEY);
        INSERT INTO #R29FallbackIds (DetailId)
        SELECT r.DetailId
        FROM #Request r
        WHERE r.BOMNO IS NULL
          AND r.OrderType = N'SALES_ORDER'
          AND (r.MaterialCode LIKE N'WIP%' OR r.MaterialCode LIKE N'RAW%')
          AND NOT EXISTS (SELECT 1 FROM #EntryCandidates ec WHERE ec.DetailId = r.DetailId);

        -- Step2: 降级查入口（去工厂过滤，仅对 #R29FallbackIds 中的 DetailId）
        ;WITH R29_Fallback AS (
            SELECT r.DetailId, e.BOMNO AS CandidateBOMNO, r.MaterialCode AS EntryParent, r.OrderType,
                   MAX(CAST(e.IsDefaultVersion AS INT)) AS HasDefault
            FROM #Request r
            INNER JOIN #R29FallbackIds fb ON fb.DetailId = r.DetailId
            INNER JOIN MES_BOM_Edge_Active e  -- ⚡ 直读物化边表
                    ON e.ParentMaterialCode = r.MaterialCode AND e.IsActive = 1
            GROUP BY r.DetailId, e.BOMNO, r.MaterialCode, r.OrderType
        )
        INSERT INTO #EntryCandidates (DetailId, CandidateBOMNO, EntryParent, OrderType, CandidateRank)
        SELECT DetailId, CandidateBOMNO, EntryParent, OrderType,
               ROW_NUMBER() OVER (
                   PARTITION BY DetailId
                   ORDER BY HasDefault DESC,
                            CASE WHEN CandidateBOMNO IS NOT NULL THEN 0 ELSE 1 END,
                            CandidateBOMNO DESC
               )
        FROM R29_Fallback;

        -- Step3: 为降级命中的条目写 FACTORY_MISMATCH_FALLBACK WARN
        INSERT INTO MES_APS_BOM_Workset_Issues
            (BatchNo, BOMNO, IssueType, Severity, Detail, RequestDetailId, CreatedAt)
        SELECT @BatchNo,
               ISNULL(c.CandidateBOMNO, 'MAT:' + c.EntryParent),
               'FACTORY_MISMATCH_FALLBACK', 'WARN',
               'MaterialCode=' + c.EntryParent + ' 订单FactoryCode=' + ISNULL(r.FactoryCode,'NULL')
                   + ' R17工厂无匹配BOM边，已降级为无工厂过滤入口 BOM=' + ISNULL(c.CandidateBOMNO,'NULL-Edge'),
               r.DetailId, GETDATE()
        FROM #R29FallbackIds fb
        JOIN #EntryCandidates c ON c.DetailId = fb.DetailId AND c.CandidateRank = 1
        JOIN #Request r ON r.DetailId = fb.DetailId;

        DROP TABLE #R29FallbackIds;

        -- ── Stage C: 入口裁决（#EntryResolved）──────────────────────────────────
        -- P2: BOMNO NULL策略A — CandidateBOMNO IS NULL 时生成 'MAT:{MaterialCode}'
        --     BOMNO 列永远非空，保证 Workset.BOMNO NOT NULL 约束；OriginalBOMNO 保留真实值供 Stage D JOIN
        CREATE TABLE #EntryResolved (
            DetailId         BIGINT        NOT NULL,
            BOMNO            NVARCHAR(50)  NOT NULL, -- ResolvedBOMNO，永远非空（MAT: 前缀兜底）
            OriginalBOMNO    NVARCHAR(50)  NULL,     -- Edge_Active 实际 BOMNO（NULL=纯物料路由）
            EntryParent      NVARCHAR(50)  NOT NULL,
            RootMaterialCode NVARCHAR(50)  NOT NULL,
            RootFactoryCode  NVARCHAR(20)  NULL
        );

        INSERT INTO #EntryResolved (DetailId, BOMNO, OriginalBOMNO, EntryParent, RootMaterialCode, RootFactoryCode)
        SELECT c.DetailId,
               ISNULL(c.CandidateBOMNO, 'MAT:' + c.EntryParent), -- P2: Strategy A 兜底
               c.CandidateBOMNO,       -- 真实 BOMNO（NULL 保留，Stage D 按此决定 JOIN 分支）
               c.EntryParent,
               r.MaterialCode,
               r.FactoryCode
        FROM #EntryCandidates c
        INNER JOIN #Request r ON r.DetailId = c.DetailId
        WHERE c.CandidateRank = 1;

        -- P1+P2: 入口未找到 → BOM_ENTRY_NOT_FOUND ERROR
        --        字段名修正：BOMNO（非空兼容）/ Detail / RequestDetailId
        INSERT INTO MES_APS_BOM_Workset_Issues
            (BatchNo, BOMNO, IssueType, Severity, Detail, RequestDetailId, CreatedAt)
        SELECT @BatchNo,
               ISNULL(r.BOMNO, 'MAT:' + r.MaterialCode),   -- P2: BOMNO NOT NULL 兼容
               'BOM_ENTRY_NOT_FOUND', 'ERROR',
               'BOMNO=' + ISNULL(r.BOMNO, 'NULL') + ' MaterialCode=' + r.MaterialCode
                   + ' 未在 MES_BOM_Edge_Active 找到入口',
               r.DetailId,                                  -- P1: RequestDetailId
               GETDATE()
        FROM #Request r
        WHERE NOT EXISTS (SELECT 1 FROM #EntryResolved er WHERE er.DetailId = r.DetailId)
          -- R30: SALES_ORDER+RAW%+完全无BOM边 → 正常外购件兜底，静默PURCHASE（不登记BOM_ENTRY_NOT_FOUND）
          -- 若有BOM边但R17过滤后为空（无匹配工厂边）属ERP数据异常，仍登记BOM_ENTRY_NOT_FOUND
          AND NOT (
              r.OrderType = N'SALES_ORDER'
              AND r.MaterialCode LIKE N'RAW%'
              AND NOT EXISTS (
                  SELECT 1 FROM MES_BOM_Edge_Active e
                  WHERE e.ParentMaterialCode = r.MaterialCode AND e.IsActive = 1
              )
          );

        -- P1+P4: 多候选入口 → BOM_ENTRY_AMBIGUOUS WARN（按入口数量判断，不是边数量）
        INSERT INTO MES_APS_BOM_Workset_Issues
            (BatchNo, BOMNO, IssueType, Severity, Detail, RequestDetailId, CreatedAt)
        SELECT @BatchNo,
               ISNULL(c.CandidateBOMNO, 'MAT:' + c.EntryParent), -- P2: BOMNO NOT NULL 兼容
               'BOM_ENTRY_AMBIGUOUS', 'WARN',
               'MaterialCode=' + c.EntryParent + ' 有多个BOM入口候选，已取最优（Rank=1 BOMNO='
                   + ISNULL(c.CandidateBOMNO, 'NULL') + '）',
               r.DetailId,                                   -- P1: RequestDetailId
               GETDATE()
        FROM #EntryCandidates c
        INNER JOIN #Request r ON r.DetailId = c.DetailId
        WHERE c.CandidateRank = 1
          AND c.DetailId IN (
              SELECT DetailId FROM #EntryCandidates GROUP BY DetailId HAVING COUNT(*) > 1
          );

        -- R31: PRODUCTION_INSTRUCTION + BOMNO IS NULL → 必写Issues（无论是否找到入口）
        --      Severity=WARN（找到候选）/ ERROR（未找到） [对齐BOM_Workset方案v1.7 §1.4]
        INSERT INTO MES_APS_BOM_Workset_Issues
            (BatchNo, BOMNO, IssueType, Severity, Detail, RequestDetailId, CreatedAt)
        SELECT @BatchNo,
               ISNULL(er.BOMNO, N'MAT:' + r.MaterialCode),
               N'BOMNO_MISSING_PRODUCTION',
               CASE WHEN er.DetailId IS NOT NULL THEN N'WARN' ELSE N'ERROR' END,
               N'PRODUCTION_INSTRUCTION 订单 BOMNO 为空/0，MaterialCode=' + r.MaterialCode
                   + CASE WHEN er.DetailId IS NOT NULL
                          THEN N'，已按 MaterialCode 推导首层入口（BOMNO 应由 ERP 明确填写，请核查）'
                          ELSE N'，未找到任何 BOM 入口，展开跳过，请核查 MES BOM 数据与 ERP 生产计划'
                     END,
               r.DetailId,
               GETDATE()
        FROM #Request r
        LEFT JOIN #EntryResolved er ON er.DetailId = r.DetailId
        WHERE r.BOMNO IS NULL
          AND r.OrderType = N'PRODUCTION_INSTRUCTION';

        -- ── Stage D: WHILE 迭代展开（#WorksetRaw）───────────────────────────────
        CREATE TABLE #WorksetRaw (
            DetailId            BIGINT        NOT NULL,
            BOMNO               NVARCHAR(50)  NOT NULL, -- ResolvedBOMNO，永远非空（MAT: 兜底）
            ParentMaterialCode  NVARCHAR(50)  NOT NULL,
            ChildMaterialCode   NVARCHAR(50)  NOT NULL,
            Quantity            DECIMAL(18,6) NOT NULL,
            Level               INT           NOT NULL,
            Path                NVARCHAR(MAX) NOT NULL,
            ParentProcRefCode   NVARCHAR(50)  NULL,
            ChildProcRefCode    NVARCHAR(50)  NULL,
            ChildSourceHintCode NVARCHAR(50)  NULL,
            RequestDetailId     BIGINT        NOT NULL,
            RootMaterialCode    NVARCHAR(50)  NOT NULL,
            RootFactoryCode     NVARCHAR(20)  NULL
        );

        -- L1: OriginalBOMNO IS NOT NULL — 按 BOMNO+EntryParent 精确寻址（IX_BOMEdgeActive_BOMNO）
        INSERT INTO #WorksetRaw
        SELECT er.DetailId, er.BOMNO,                       -- ResolvedBOMNO（非空）
               e.ParentMaterialCode, e.ChildMaterialCode, e.Quantity,
               1, CAST(e.ChildMaterialCode AS NVARCHAR(MAX)),
               e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode,
               er.DetailId, er.RootMaterialCode, er.RootFactoryCode
        FROM #EntryResolved er
        INNER JOIN MES_BOM_Edge_Active e              -- ⚡ 直读物化边表，不走 MES_BOM_View
               ON e.BOMNO = er.OriginalBOMNO
              AND e.ParentMaterialCode = er.EntryParent
        WHERE er.OriginalBOMNO IS NOT NULL; -- R34: 显式BOMNO不过滤IsActive，含历史版本

        -- L1: OriginalBOMNO IS NULL (MAT:前缀) — 按 EntryParent + e.BOMNO IS NULL 精确过滤
        --     P4: e.BOMNO IS NULL 确保只拿同一入口的所有 L1 边，不跨 BOMNO 版本混入
        INSERT INTO #WorksetRaw
        SELECT er.DetailId, er.BOMNO,                       -- ResolvedBOMNO='MAT:...'（非空）
               e.ParentMaterialCode, e.ChildMaterialCode, e.Quantity,
               1, CAST(e.ChildMaterialCode AS NVARCHAR(MAX)),
               e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode,
               er.DetailId, er.RootMaterialCode, er.RootFactoryCode
        FROM #EntryResolved er
        INNER JOIN MES_BOM_Edge_Active e              -- ⚡ 直读物化边表，不走 MES_BOM_View
               ON e.ParentMaterialCode = er.EntryParent
              AND e.BOMNO IS NULL                    -- P4: 精确匹配 NULL BOMNO 入口
        WHERE er.OriginalBOMNO IS NULL
          AND e.IsActive = 1 AND e.IsDefaultVersion = 1;

        SET @CurrentLevel = 1;
        SELECT @RowsInserted = COUNT(*) FROM #WorksetRaw;

        -- L2~LN：WHILE 迭代（R32 多BOMNO收敛 ERP>MES + R35 MES空Produce代入）
        -- R32 V1简化：Step1(ERP>MES) + MAX BOMNO兜底；Step2(出口库过滤)留V2
        WHILE @CurrentLevel < 10 AND @RowsInserted > 0
        BEGIN
            SET @CurrentLevel = @CurrentLevel + 1;

            -- R32 S1: 收集本层待展开物料及候选BOMNO（ERP>MES收敛，多ERP取MAX）
            SELECT DISTINCT ChildMaterialCode INTO #LvMat
            FROM #WorksetRaw WHERE Level = @CurrentLevel - 1;

            SELECT e.ParentMaterialCode                                            AS ChildMaterialCode,
                   COUNT(DISTINCT CASE WHEN e.SourceSystem = N'ERP'
                                        AND e.BOMNO IS NOT NULL THEN e.BOMNO END)  AS ERPNNBOMCnt,
                   MAX(CASE WHEN e.SourceSystem = N'ERP'
                             AND e.BOMNO IS NOT NULL THEN e.BOMNO END)             AS MaxERPBOMNO,
                   MAX(CASE WHEN e.BOMNO IS NOT NULL THEN e.BOMNO END)             AS MaxAnyBOMNO
            INTO #LvSel
            FROM #LvMat lm
            INNER JOIN MES_BOM_Edge_Active e ON e.ParentMaterialCode = lm.ChildMaterialCode
            WHERE e.IsActive = 1 AND e.IsDefaultVersion = 1
            GROUP BY e.ParentMaterialCode;

            -- R32 S2: 多ERP BOMNO共存 → MULTI_BOMNO_UNRESOLVED WARN（取MAX BOMNO兜底）
            INSERT INTO MES_APS_BOM_Workset_Issues
                (BatchNo, BOMNO, ChildMaterialCode,
                 IssueType, Severity, Detail, DegradeAction, CreatedAt)
            SELECT DISTINCT @BatchNo, prev.BOMNO, ls.ChildMaterialCode,
                   N'MULTI_BOMNO_UNRESOLVED', N'WARN',
                   N'物料' + ls.ChildMaterialCode + N' 存在' + CAST(ls.ERPNNBOMCnt AS NVARCHAR(10))
                       + N' 个ERP BOMNO均为IsDefaultVersion=1，已取MAX BOMNO='
                       + ISNULL(ls.MaxERPBOMNO, N'?') + N'，建议ODS版本裁决',
                   N'USE_MAX_BOMNO', GETDATE()
            FROM #LvSel ls
            INNER JOIN #WorksetRaw prev
                    ON prev.ChildMaterialCode = ls.ChildMaterialCode
                   AND prev.Level = @CurrentLevel - 1
            WHERE ls.ERPNNBOMCnt > 1;

            -- R32 S3: 按选定BOMNO展开（ERP非空优先；无ERP则用最大非空；全NULL则匹配NULL边）
            INSERT INTO #WorksetRaw
            SELECT prev.DetailId, prev.BOMNO,
                   e.ParentMaterialCode, e.ChildMaterialCode,
                   e.Quantity,            -- ⚠️ 单位用量，绝不累乘
                   @CurrentLevel,
                   prev.Path + ' -> ' + e.ChildMaterialCode,
                   e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode,
                   prev.RequestDetailId,
                   prev.RootMaterialCode, prev.RootFactoryCode
            FROM #WorksetRaw prev
            INNER JOIN #LvSel ls ON ls.ChildMaterialCode = prev.ChildMaterialCode
            INNER JOIN MES_BOM_Edge_Active e      -- ⚡ 直读物化边表，不走 MES_BOM_View
                   ON e.ParentMaterialCode = prev.ChildMaterialCode
                  AND (
                      (ls.ERPNNBOMCnt > 0 AND e.BOMNO = ls.MaxERPBOMNO)
                   OR (ls.ERPNNBOMCnt = 0 AND ls.MaxAnyBOMNO IS NOT NULL
                       AND e.BOMNO = ls.MaxAnyBOMNO)
                   OR (ls.ERPNNBOMCnt = 0 AND ls.MaxAnyBOMNO IS NULL AND e.BOMNO IS NULL)
                  )
            WHERE prev.Level = @CurrentLevel - 1
              AND e.IsActive = 1
              AND prev.Path NOT LIKE '%' + e.ChildMaterialCode + '%'; -- 防环

            SET @RowsInserted = @@ROWCOUNT;

            DROP TABLE #LvMat;
            DROP TABLE #LvSel;
        END

        -- R35: MES边Produce代入 — ChildSourceHintCode为NULL/字符串NULL时，从ERP边继承或按物料前缀推断
        --      经R32收敛后有ERP BOM的物料均已用ERP边（含Produce值）；此处处理仅MES BOM残留NULL
        UPDATE w
        SET w.ChildSourceHintCode = COALESCE(
            (SELECT TOP 1 ee.ChildSourceHintCode
             FROM MES_BOM_Edge_Active ee
             WHERE ee.ParentMaterialCode = w.ParentMaterialCode
               AND ee.ChildMaterialCode  = w.ChildMaterialCode
               AND ee.SourceSystem       = N'ERP'
               AND ee.ChildSourceHintCode IS NOT NULL
               AND ee.IsActive = 1
             ORDER BY ee.IsDefaultVersion DESC, ee.BOMNO DESC),
            CASE WHEN w.ChildMaterialCode LIKE N'RAW%' THEN N'2' ELSE N'1' END
        )
        FROM #WorksetRaw w
        WHERE w.ChildSourceHintCode IS NULL OR w.ChildSourceHintCode = N'NULL';

        -- ── Stage E: 落地 MES_APS_BOM_Workset ──────────────────────────────────
        INSERT INTO MES_APS_BOM_Workset (
            BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
            Quantity, Level, Path,
            ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
            RequestDetailId, CreatedAt
        )
        SELECT @BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
               Quantity, Level, Path,
               ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
               RequestDetailId, GETDATE()
        FROM #WorksetRaw;

        SET @ExpandedRowCount = @@ROWCOUNT;

        DROP TABLE #WorksetRaw;
        DROP TABLE #EntryResolved;
        DROP TABLE #EntryCandidates;
        DROP TABLE #Request;
        DROP TABLE #R28ExportCodes;

        -- 5a. 后置回填（Stage/Factory/StageDetail[含WorksetId]/Issues）
        --     【v5.0.32】ResolvedBOMNO 不再回写到 RequestDetail；
        --     由 2号位在 Workset+StageDetail 同步完成后，从 Level=1 Workset.BOMNO 写入 OrderBomRequestLink.ResolvedBOMNO
        EXEC sp_EnrichBOMWorkset @BatchNo;

        -- 6. 更新 READY
        UPDATE MES_API_BOM_Request
        SET Status             = 'READY',
            CompletedAt        = GETDATE(),
            ExpandedRowCount   = @ExpandedRowCount,
            ProcessingDuration = DATEDIFF(SECOND, @StartTime, GETDATE())
        WHERE BatchNo = @BatchNo;

        INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
        VALUES (@BatchNo,
                'sp_ExpandBOMBatch_vNext 完成，展开行数: ' + CAST(@ExpandedRowCount AS NVARCHAR(20)),
                GETDATE());

    END TRY
    BEGIN CATCH
        -- P6: 未捕获异常 → Status=FAILED + EXPAND_FAILED Issue（CRITICAL）+ THROW 向上传播
        --     设计：FAILED 仅给 SP 进程崩溃（tempdb满/连接中断等）；数据质量降级走 Issues，不到这里
        UPDATE MES_API_BOM_Request
        SET Status             = 'FAILED',
            CompletedAt        = GETDATE(),
            ProcessingDuration = DATEDIFF(SECOND, @StartTime, GETDATE()),
            ErrorMessage       = ERROR_MESSAGE()
        WHERE BatchNo = @BatchNo;

        INSERT INTO MES_APS_BOM_Workset_Issues
            (BatchNo, BOMNO, IssueType, Severity, Detail, CreatedAt)
        VALUES (@BatchNo, 'BATCH', 'EXPAND_FAILED', 'CRITICAL',
                'SP进程异常: ' + ERROR_MESSAGE(), GETDATE());

        DROP TABLE IF EXISTS #WorksetRaw;
        DROP TABLE IF EXISTS #EntryResolved;
        DROP TABLE IF EXISTS #EntryCandidates;
        DROP TABLE IF EXISTS #Request;
        DROP TABLE IF EXISTS #R28ExportCodes;
        DROP TABLE IF EXISTS #LvMat;
        DROP TABLE IF EXISTS #LvSel;

        THROW;
    END CATCH;
END;
GO

-- =============================================
-- 3.2 实时BOM展开存储过程（⚠️ deprecated — 递归CTE版，v5.0.26起由 sp_ExpandBOMRealtime_vNext 替代）
-- 保留兼容运行；待 vNext 全量验证后下线
-- =============================================
-- 3.2 实时BOM展开存储过程
CREATE PROCEDURE sp_ExpandBOMRealtime
    @BOMNO NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @RequestId BIGINT;
    DECLARE @StartTime DATETIME2 = GETDATE();
    
    -- 1. 检查是否已存在
    IF EXISTS (SELECT 1 FROM MES_API_BOM_Request_Realtime 
               WHERE BOMNO = @BOMNO AND Status = 'READY')
    BEGIN
        RETURN;
    END;
    
    -- 2. 插入请求记录
    INSERT INTO MES_API_BOM_Request_Realtime (BOMNO, RequestTime, Status)
    VALUES (@BOMNO, @StartTime, 'PROCESSING');
    
    SET @RequestId = SCOPE_IDENTITY();
    
    -- 3. 执行展开（v5.0.7：透传3个ERP BOM原始辅助字段）
    BEGIN TRY
        WITH BOM_Recursive AS (
            -- 第1层：基于BOMNO展开
            SELECT 
                b.BOMNO,
                b.ParentMaterialCode,
                b.ChildMaterialCode,
                b.Quantity,  -- ⚠️ 单位用量，不累乘！
                1 AS Level,
                b.ParentProcRefCode,                     -- v5.0.7
                b.ChildProcRefCode,                      -- v5.0.7
                b.ChildSourceHintCode                    -- v5.0.7
            FROM MES_BOM_View b
            WHERE b.BOMNO = @BOMNO
              AND b.IsActive = 1
            
            UNION ALL
            
            -- 第2~N层：基于MaterialCode展开
            SELECT 
                r.BOMNO,
                b.ParentMaterialCode,
                b.ChildMaterialCode,
                b.Quantity,  -- ⚠️ 单位用量，不累乘！
                r.Level + 1 AS Level,
                b.ParentProcRefCode,                     -- v5.0.7
                b.ChildProcRefCode,                      -- v5.0.7
                b.ChildSourceHintCode                    -- v5.0.7
            FROM BOM_Recursive r
            INNER JOIN MES_BOM_View b 
                ON r.ChildMaterialCode = b.ParentMaterialCode
            WHERE b.IsActive = 1
              AND b.IsDefaultVersion = 1
              AND r.Level < 10
        )
        INSERT INTO MES_APS_BOM_Workset_Realtime (
            BOMNO,
            ParentMaterialCode,
            ChildMaterialCode,
            Quantity,
            Level,
            ParentProcRefCode,                           -- v5.0.7
            ChildProcRefCode,                            -- v5.0.7
            ChildSourceHintCode,                         -- v5.0.7
            CreatedAt
        )
        SELECT 
            BOMNO,
            ParentMaterialCode,
            ChildMaterialCode,
            Quantity,
            Level,
            ParentProcRefCode,                           -- v5.0.7
            ChildProcRefCode,                            -- v5.0.7
            ChildSourceHintCode,                         -- v5.0.7
            GETDATE()
        FROM BOM_Recursive
        OPTION (MAXRECURSION 10);
        
        -- 4. 【v5.0.7双层结果 + v5.0.8 ROOT路径 + v5.0.10 工厂映射 + v5.0.11 降级容错】5号位后置回填（逻辑同批量链路）
        -- ⚠️ 此处由5号位追加实现（建议独立 SP：sp_EnrichBOMWorksetRealtime）：
        --    a) UPDATE MES_APS_BOM_Workset_Realtime 
        --       SET ChildRequiredStageCode = <推导结果>,
        --           ChildRequiredFactory  = <R17 推导：查 ProduceToFactoryMap 配置表 (v5.0.11)>
        --       WHERE BOMNO = @BOMNO;
        --    b) INSERT INTO StageDetail_Realtime: StageScopeType='EDGE' 记录（BOM边级）
        --    c) INSERT INTO StageDetail_Realtime: StageScopeType='ROOT' 记录（根产品完工路径，ParentMaterialCode=NULL）
        --    d) 降级容错（v5.0.11 / v5.1.0 修正）：写入 MES_APS_BOM_Workset_Issues；正式路径 BatchNo=RT:RD:{RequestDetailId}，deprecated 兼容路径为 RT:{ResolvedBOMNO}；含 DegradeAction 标签
        --       处置策略同批量链路——**永不阻塞**，全部降级 + 登记；状态机永远 READY
        -- ✅ v5.0.18：调用 sp_EnrichBOMWorksetRealtime 执行回填（逻辑同批量链路，操作 _Realtime 表）
        EXEC sp_EnrichBOMWorksetRealtime @BOMNO;
        
        -- 5. 更新状态为READY（v5.0.11：实时链路同样永不阻塞，除非 SP 进程崩溃走 CATCH 分支）
        UPDATE MES_API_BOM_Request_Realtime
        SET Status = 'READY',
            CompletedTime = GETDATE(),
            ExpandedRowCount = @@ROWCOUNT
        WHERE Id = @RequestId;
        
    END TRY
    BEGIN CATCH
        -- 5. 展开失败，记录错误
        UPDATE MES_API_BOM_Request_Realtime
        SET Status = 'FAILED',
            CompletedTime = GETDATE(),
            ErrorMessage = ERROR_MESSAGE()
        WHERE Id = @RequestId;
    END CATCH;
END;
GO

-- =============================================
-- 3.2b 实时BOM迭代展开存储过程（v5.0.26 新增，2026-05-14；v5.0.26b 增加 @RequestDetailId，2026-05-15）
-- ⚡ sp_ExpandBOMRealtime_vNext — WHILE迭代，直读 MES_BOM_Edge_Active
-- ⚠️ 禁止在此SP中引用 MES_BOM_View（递归CTE）
-- ⚠️ sp_ExpandBOMRealtime（§3.2）已标记 deprecated，稳定后下线
-- 参数说明：
--   @BOMNO           — 直接指定 BOMNO（兼容旧调用方式；@RequestDetailId IS NOT NULL 时可省略）
--   @RequestDetailId — 订单粒度触发时传入（v5.0.26b）；自动反查 BOMNO/MaterialCode/FactoryCode/OrderType
--                      推荐：以 RequestDetailId 为入口，支持 BOMNO IS NULL 场景（纯物料路由BOM）
-- =============================================
CREATE OR ALTER PROCEDURE sp_ExpandBOMRealtime_vNext
    @BOMNO           NVARCHAR(50) = NULL,  -- 直接指定BOMNO（兼容旧调用；@RequestDetailId 时自动填充）
    @RequestDetailId BIGINT       = NULL   -- v5.0.26b：订单粒度触发，自动反查入口
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @StartTime        DATETIME2    = GETDATE();
    DECLARE @MaterialCode     NVARCHAR(50) = NULL;
    DECLARE @FactoryCode      NVARCHAR(20) = NULL;
    DECLARE @OrderType        NVARCHAR(50) = NULL;
    DECLARE @OrderCanonicalId BIGINT       = NULL;   -- v5.0.33: 反查 RequestDetail.OrderCanonicalId

    -- v5.1.0: 统一实时 Issues 切片号（先声明，@RequestDetailId 非空即可确定 RT:RD 正式路径）
    -- 正式路径：RT:RD:{RequestDetailId}
    -- deprecated 兼容路径：RT:{ResolvedBOMNO}（在 @ResolvedBOMNO 确定后补赋值）
    -- Issues.BatchNo 列长 NVARCHAR(50)，@RequestDetailId 场景固定 <=30 字符，deprecated 路径用 LEFT 截断
    DECLARE @SyntheticBatchNo NVARCHAR(50) =
        CASE WHEN @RequestDetailId IS NOT NULL
             THEN CONCAT(N'RT:RD:', CAST(@RequestDetailId AS NVARCHAR(20)))
             ELSE NULL
        END;

    -- 0. 前置校验：RefreshLog 最新记录必须 COMPLETED
    IF NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_RefreshLog
        WHERE Id = (SELECT MAX(Id) FROM MES_BOM_Edge_RefreshLog)
          AND Status = 'COMPLETED'
    )
    BEGIN
        RAISERROR('MES_BOM_Edge_Active 未完成刷新，禁止实时展开。', 16, 1);
        RETURN;
    END

    -- 1. 参数解析：@RequestDetailId 优先，自动反查 BOMNO/MaterialCode/FactoryCode/OrderType
    IF @RequestDetailId IS NOT NULL
    BEGIN
        SELECT @BOMNO             = NULLIF(NULLIF(d.RequestedBOMNO, N''), N'0'),  -- R33: 空字符串/'0'等价NULL
               @MaterialCode      = d.MaterialCode,       -- v5.0.32: Model 已删除，直接取 MaterialCode
               @FactoryCode       = d.FactoryCode,
               @OrderType         = d.OrderType,
               @OrderCanonicalId  = d.OrderCanonicalId   -- v5.0.33: 传入 Request_Realtime
        FROM MES_API_BOM_Request_Detail d
        WHERE d.Id = @RequestDetailId;

        -- 行不存在（@@ROWCOUNT=0 时所有 @ 变量仍 NULL，BOMNO 也 NULL）
        IF @@ROWCOUNT = 0
        BEGIN
            RAISERROR('RequestDetailId=%I64d 未找到对应 Detail 行，终止。', 16, 1, @RequestDetailId);
            RETURN;
        END

        -- 找到行但 MaterialCode 为 NULL：登记 Error + 跳过（v5.0.32: Model 已删除；对齐批量链路降级策略）
        IF @MaterialCode IS NULL
        BEGIN
            INSERT INTO MES_APS_BOM_Workset_Issues
                (BatchNo, BOMNO, IssueType, Severity, Detail, DegradeAction, RequestDetailId, CreatedAt)
            VALUES
                (@SyntheticBatchNo,
                 ISNULL(@BOMNO, N''), N'MISSING_MATERIALCODE', N'ERROR',
                 N'RequestDetailId=' + CAST(@RequestDetailId AS NVARCHAR(20))
                     + N': RequestDetail.MaterialCode 为空，无法解析 BOM 入口，已跳过',
                 N'SKIP', @RequestDetailId, GETDATE());
            RETURN;
        END
    END
    ELSE IF @BOMNO IS NULL
    BEGIN
        RAISERROR('@BOMNO 和 @RequestDetailId 不能同时为 NULL。', 16, 1);
        RETURN;
    END

    -- 1b. P8: 参数解析完成后，计算 ResolvedBOMNO（Strategy A：无BOMNO时生成 MAT:{MaterialCode}）
    --     保证 Workset_Realtime.BOMNO NOT NULL 约束 + sp_EnrichBOMWorksetRealtime 能按非空BOMNO过滤
    DECLARE @ResolvedBOMNO NVARCHAR(50) = ISNULL(@BOMNO, 'MAT:' + @MaterialCode);

    -- v5.1.0: 补赋值 @SyntheticBatchNo（deprecated 路径，@RequestDetailId 为 NULL 时使用 RT:{ResolvedBOMNO}）
    -- 用 LEFT 显式限制到 Issues.BatchNo 列长 NVARCHAR(50)
    IF @SyntheticBatchNo IS NULL
    BEGIN
        SET @SyntheticBatchNo = LEFT(CONCAT(N'RT:', @ResolvedBOMNO), 50);
    END

    -- 2. 幂等保护（v5.0.33：@RequestDetailId 不为空时按 RequestDetailId 判断；旧调用按 ResolvedBOMNO）
    IF @RequestDetailId IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM MES_API_BOM_Request_Realtime
                   WHERE RequestDetailId = @RequestDetailId AND Status = 'READY')
            RETURN;
    END
    ELSE
    BEGIN
        -- 旧兼容调用（无 RequestDetailId），按 ResolvedBOMNO 判断
        IF EXISTS (SELECT 1 FROM MES_API_BOM_Request_Realtime
                   WHERE BOMNO = @ResolvedBOMNO AND Status = 'READY')
            RETURN;
    END

    -- 3. 写请求记录（v5.0.33：同时写入 RequestDetailId/OrderCanonicalId/ResolvedBOMNO）
    INSERT INTO MES_API_BOM_Request_Realtime
        (BOMNO, RequestDetailId, OrderCanonicalId, ResolvedBOMNO, RequestTime, Status)
    VALUES
        (@ResolvedBOMNO, @RequestDetailId, @OrderCanonicalId, @ResolvedBOMNO, @StartTime, 'PROCESSING');
    DECLARE @RequestId BIGINT = SCOPE_IDENTITY();

    BEGIN TRY
        -- 4. 构造展开工作表 #RT_Expand
        CREATE TABLE #RT_Expand (
            BOMNO               NVARCHAR(50)  NULL,    -- 可 NULL（纯物料路由BOM边）
            ParentMaterialCode  NVARCHAR(50)  NOT NULL,
            ChildMaterialCode   NVARCHAR(50)  NOT NULL,
            Quantity            DECIMAL(18,6) NOT NULL,
            Level               INT           NOT NULL,
            Path                NVARCHAR(MAX) NOT NULL,
            ParentProcRefCode   NVARCHAR(50)  NULL,
            ChildProcRefCode    NVARCHAR(50)  NULL,
            ChildSourceHintCode NVARCHAR(50)  NULL
        );

        -- L1: BOMNO IS NOT NULL — 按 BOMNO 寻址（IX_BOMEdgeActive_BOMNO）
        IF @BOMNO IS NOT NULL
        BEGIN
            INSERT INTO #RT_Expand
            SELECT e.BOMNO, e.ParentMaterialCode, e.ChildMaterialCode,
                   e.Quantity, 1, CAST(e.ChildMaterialCode AS NVARCHAR(MAX)),
                   e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode
            FROM MES_BOM_Edge_Active e              -- ⚡ 直读物化边表，不走 MES_BOM_View
            WHERE e.BOMNO = @BOMNO; -- R34: 显式BOMNO不过滤IsActive，含历史版本
        END
        ELSE
        BEGIN
            -- L1: BOMNO IS NULL — 先选最优入口 BOMNO，再抓该 BOMNO 下全部 L1 边（对齐批量链路 Case B）
            -- Step B1: #RT_EntryCandidates — 按R28/R29/R31分流选最优入口BOMNO
            --   R28: SALES_ORDER+ASSY% → 出口库过滤；代理后仍空→降级R29+R17
            --   R29: SALES_ORDER+WIP%/RAW% → 全量查+R17工厂过滤（务必同工厂）
            --   R31/other: 直接按MaterialCode查入口，不做工厂过滤
            CREATE TABLE #RT_EntryCandidates (
                CandidateBOMNO NVARCHAR(50) NULL,          -- NULL = 纹物料路由 BOM 边
                CandidateRank  INT          NOT NULL
            );
            IF @OrderType = N'SALES_ORDER' AND @MaterialCode LIKE N'ASSY%'
            BEGIN
                -- R28: Step1 查出口库ProcessCode（含CN6课→CN代理）
                DECLARE @EffectiveFactory NVARCHAR(20) = @FactoryCode;
                IF NOT EXISTS (
                    SELECT 1 FROM ProcessCodeDict
                    WHERE FactoryCode = @FactoryCode AND WarehouseRole = N'出口库' AND IsActive = 1
                )
                    SET @EffectiveFactory = CASE @FactoryCode WHEN N'CN6课' THEN N'CN' ELSE @FactoryCode END;

                -- R28: Step2 按出口库ParentProcRefCode过滤首层BOMNO候选
                INSERT INTO #RT_EntryCandidates (CandidateBOMNO, CandidateRank)
                SELECT BOMNO,
                       ROW_NUMBER() OVER (ORDER BY IsDefaultVersion DESC, BOMNO)
                FROM (
                    SELECT DISTINCT e.BOMNO, e.IsDefaultVersion
                    FROM MES_BOM_Edge_Active e             -- ⚡ 直读物化边表
                    INNER JOIN ProcessCodeDict p ON p.ProcessCode = e.ParentProcRefCode
                    WHERE p.FactoryCode   = @EffectiveFactory
                      AND p.WarehouseRole = N'出口库'
                      AND p.IsActive      = 1
                      AND e.ParentMaterialCode = @MaterialCode
                      AND e.IsActive = 1
                ) DistinctBOMs;

                -- R28 降级：出口库过滤后无候选（代理后仍空）→ 降级走R29+R17（用@FactoryCode，不用@EffectiveFactory）
                IF NOT EXISTS (SELECT 1 FROM #RT_EntryCandidates)
                BEGIN
                    INSERT INTO #RT_EntryCandidates (CandidateBOMNO, CandidateRank)
                    SELECT BOMNO, ROW_NUMBER() OVER (ORDER BY IsDefaultVersion DESC, BOMNO)
                    FROM (
                        SELECT DISTINCT e.BOMNO, e.IsDefaultVersion
                        FROM MES_BOM_Edge_Active e
                        INNER JOIN ProcessCodeDict p ON p.ProcessCode = e.ParentProcRefCode
                        WHERE p.FactoryCode = @FactoryCode   -- 订单原始FactoryCode
                          AND p.IsActive    = 1
                          AND e.ParentMaterialCode = @MaterialCode
                          AND e.IsActive = 1
                    ) DistinctBOMs;
                END
            END
            ELSE IF @OrderType = N'SALES_ORDER'
                 AND (@MaterialCode LIKE N'WIP%' OR @MaterialCode LIKE N'RAW%')
            BEGIN
                -- R29: 全量查+R17工厂过滤
                INSERT INTO #RT_EntryCandidates (CandidateBOMNO, CandidateRank)
                SELECT BOMNO, ROW_NUMBER() OVER (ORDER BY IsDefaultVersion DESC, BOMNO)
                FROM (
                    SELECT DISTINCT e.BOMNO, e.IsDefaultVersion
                    FROM MES_BOM_Edge_Active e             -- ⚡ 直读物化边表
                    INNER JOIN ProcessCodeDict p ON p.ProcessCode = e.ParentProcRefCode
                    WHERE p.FactoryCode = @FactoryCode
                      AND p.IsActive    = 1
                      AND e.ParentMaterialCode = @MaterialCode
                      AND e.IsActive = 1
                ) DistinctBOMs;

                -- R37: R17过滤后仍无候选 → 降级去工厂过滤再查一次+写FACTORY_MISMATCH_FALLBACK WARN
                IF NOT EXISTS (SELECT 1 FROM #RT_EntryCandidates)
                BEGIN
                    INSERT INTO #RT_EntryCandidates (CandidateBOMNO, CandidateRank)
                    SELECT BOMNO, ROW_NUMBER() OVER (ORDER BY IsDefaultVersion DESC, BOMNO)
                    FROM (
                        SELECT DISTINCT BOMNO, IsDefaultVersion
                        FROM MES_BOM_Edge_Active          -- ⚡ 直读物化边表
                        WHERE ParentMaterialCode = @MaterialCode AND IsActive = 1
                    ) DistinctBOMs;

                    IF EXISTS (SELECT 1 FROM #RT_EntryCandidates)
                        INSERT INTO MES_APS_BOM_Workset_Issues
                            (BatchNo, BOMNO, IssueType, Severity, Detail, RequestDetailId, CreatedAt)
                        VALUES (@SyntheticBatchNo, @ResolvedBOMNO, 'FACTORY_MISMATCH_FALLBACK', 'WARN',
                                'MaterialCode=' + @MaterialCode + ' 订单FactoryCode=' + ISNULL(@FactoryCode,'NULL')
                                    + ' R17工厂无匹配BOM边，已降级为无工厂过滤入口',
                                @RequestDetailId, GETDATE());
                END
            END
            ELSE
            BEGIN
                -- R31/other: 直接按MaterialCode查入口，不做工厂过滤
                INSERT INTO #RT_EntryCandidates (CandidateBOMNO, CandidateRank)
                SELECT BOMNO,
                       ROW_NUMBER() OVER (
                           ORDER BY
                               CASE WHEN @OrderType = N'PRODUCTION_INSTRUCTION' AND SourceSystem = N'MES' THEN 0 ELSE 1 END,
                               IsDefaultVersion DESC,
                               BOMNO
                       )
                FROM (
                    SELECT DISTINCT BOMNO, SourceSystem, IsDefaultVersion
                    FROM MES_BOM_Edge_Active          -- ⚡ 直读物化边表，不走 MES_BOM_View
                    WHERE ParentMaterialCode = @MaterialCode AND IsActive = 1
                ) DistinctBOMs;
            END

            -- Step B2: #RT_EntryResolved — 取 CandidateRank=1 的最优入口
            --          OriginalBOMNO = 原始 BOMNO（可 NULL）
            --          ResolvedBOMNO = 非空（Strategy A 兜底：ISNULL(OriginalBOMNO, 'MAT:'+@MaterialCode)）
            CREATE TABLE #RT_EntryResolved (
                OriginalBOMNO NVARCHAR(50) NULL,
                ResolvedBOMNO NVARCHAR(50) NOT NULL
            );
            INSERT INTO #RT_EntryResolved (OriginalBOMNO, ResolvedBOMNO)
            SELECT CandidateBOMNO,
                   ISNULL(CandidateBOMNO, 'MAT:' + @MaterialCode)
            FROM #RT_EntryCandidates WHERE CandidateRank = 1;

            -- 同步 @ResolvedBOMNO：Enrich SP 须与 Workset_Realtime.BOMNO 用同一键过滤
            SELECT @ResolvedBOMNO = ResolvedBOMNO FROM #RT_EntryResolved;

            -- Step B3: 按 OriginalBOMNO 是否为 NULL 分两支，支持纯物料路由 BOM
            -- Sub-branch A: 有真实 BOMNO → 按 e.BOMNO = OriginalBOMNO 精确匹配
            IF EXISTS (SELECT 1 FROM #RT_EntryResolved WHERE OriginalBOMNO IS NOT NULL)
            BEGIN
                INSERT INTO #RT_Expand
                SELECT e.BOMNO, e.ParentMaterialCode, e.ChildMaterialCode,
                       e.Quantity, 1, CAST(e.ChildMaterialCode AS NVARCHAR(MAX)),
                       e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode
                FROM MES_BOM_Edge_Active e          -- ⚡ 直读物化边表，不走 MES_BOM_View
                INNER JOIN #RT_EntryResolved r ON r.OriginalBOMNO = e.BOMNO
                WHERE e.ParentMaterialCode = @MaterialCode AND e.IsActive = 1;
            END
            -- Sub-branch B: 纯物料路由（BOM 边 BOMNO 为 NULL）→ e.BOMNO IS NULL 精确过滤
            ELSE IF EXISTS (SELECT 1 FROM #RT_EntryResolved WHERE OriginalBOMNO IS NULL)
            BEGIN
                INSERT INTO #RT_Expand
                SELECT e.BOMNO, e.ParentMaterialCode, e.ChildMaterialCode,
                       e.Quantity, 1, CAST(e.ChildMaterialCode AS NVARCHAR(MAX)),
                       e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode
                FROM MES_BOM_Edge_Active e          -- ⚡ 直读物化边表，不走 MES_BOM_View
                WHERE e.BOMNO IS NULL
                  AND e.ParentMaterialCode = @MaterialCode
                  AND e.IsActive = 1;
            END

            DROP TABLE #RT_EntryCandidates;
            DROP TABLE #RT_EntryResolved;
        END

        DECLARE @CurrentLevel INT = 1;
        DECLARE @RowsInserted INT;
        -- 用 COUNT 而非 @@ROWCOUNT，避免 DROP TABLE 将 @@ROWCOUNT 清零导致 WHILE 不执行
        SELECT @RowsInserted = COUNT(*) FROM #RT_Expand WHERE Level = 1;

        -- 5b. 入口没有找到任何 L1 边 → 登记 BOM_ENTRY_NOT_FOUND
        --     R30排除：SALES_ORDER+RAW%无BOM属外购件兜底，静默跳过不报错
        IF @RowsInserted = 0
           AND NOT (@OrderType = N'SALES_ORDER' AND @MaterialCode LIKE N'RAW%')
        BEGIN
            INSERT INTO MES_APS_BOM_Workset_Issues
                (BatchNo, BOMNO, IssueType, Severity, Detail, DegradeAction, RequestDetailId, CreatedAt)
            VALUES
                (@SyntheticBatchNo, @ResolvedBOMNO,
                 N'BOM_ENTRY_NOT_FOUND', N'ERROR',
                 N'MaterialCode=' + ISNULL(@MaterialCode, N'NULL')
                     + N' 在 MES_BOM_Edge_Active 未找到任何 L1 BOM 边，展开结果为空',
                 N'SKIP', @RequestDetailId, GETDATE());
        END

        -- R31: PRODUCTION_INSTRUCTION + @BOMNO IS NULL → 必写Issues（无论是否找到入口）
        IF @BOMNO IS NULL AND @OrderType = N'PRODUCTION_INSTRUCTION'
        BEGIN
            INSERT INTO MES_APS_BOM_Workset_Issues
                (BatchNo, BOMNO, IssueType, Severity, Detail, DegradeAction, RequestDetailId, CreatedAt)
            VALUES
                (@SyntheticBatchNo, @ResolvedBOMNO,
                 N'BOMNO_MISSING_PRODUCTION',
                 CASE WHEN @RowsInserted > 0 THEN N'WARN' ELSE N'ERROR' END,
                 N'PRODUCTION_INSTRUCTION 订单 BOMNO 为空/0，MaterialCode=' + ISNULL(@MaterialCode, N'NULL')
                     + CASE WHEN @RowsInserted > 0
                            THEN N'，已按 MaterialCode 推导首层入口（BOMNO 应由 ERP 明确填写，请核查）'
                            ELSE N'，未找到任何 BOM 入口，展开跳过，请核查 MES BOM 数据与 ERP 生产计划'
                       END,
                 NULL, @RequestDetailId, GETDATE());
        END

        -- 5. WHILE 迭代（L2~LN，R32 多BOMNO收敛 ERP>MES + R35 MES空Produce代入）
        -- R32 V1简化：Step1(ERP>MES) + MAX BOMNO兜底；Step2(出口库过滤)留V2
        WHILE @CurrentLevel < 10 AND @RowsInserted > 0
        BEGIN
            SET @CurrentLevel = @CurrentLevel + 1;

            -- R32 S1: 收集本层待展开物料及候选BOMNO（ERP>MES收敛，多ERP取MAX）
            SELECT DISTINCT ChildMaterialCode INTO #LvMat
            FROM #RT_Expand WHERE Level = @CurrentLevel - 1;

            SELECT e.ParentMaterialCode                                            AS ChildMaterialCode,
                   COUNT(DISTINCT CASE WHEN e.SourceSystem = N'ERP'
                                        AND e.BOMNO IS NOT NULL THEN e.BOMNO END)  AS ERPNNBOMCnt,
                   MAX(CASE WHEN e.SourceSystem = N'ERP'
                             AND e.BOMNO IS NOT NULL THEN e.BOMNO END)             AS MaxERPBOMNO,
                   MAX(CASE WHEN e.BOMNO IS NOT NULL THEN e.BOMNO END)             AS MaxAnyBOMNO
            INTO #LvSel
            FROM #LvMat lm
            INNER JOIN MES_BOM_Edge_Active e ON e.ParentMaterialCode = lm.ChildMaterialCode
            WHERE e.IsActive = 1 AND e.IsDefaultVersion = 1
            GROUP BY e.ParentMaterialCode;

            -- R32 S2: 多ERP BOMNO共存 → MULTI_BOMNO_UNRESOLVED WARN（取MAX BOMNO兜底）
            INSERT INTO MES_APS_BOM_Workset_Issues
                (BatchNo, BOMNO, ChildMaterialCode,
                 IssueType, Severity, Detail, DegradeAction, CreatedAt)
            SELECT DISTINCT @SyntheticBatchNo, @ResolvedBOMNO, ls.ChildMaterialCode,
                   N'MULTI_BOMNO_UNRESOLVED', N'WARN',
                   N'物料' + ls.ChildMaterialCode + N' 存在' + CAST(ls.ERPNNBOMCnt AS NVARCHAR(10))
                       + N' 个ERP BOMNO均为IsDefaultVersion=1，已取MAX BOMNO='
                       + ISNULL(ls.MaxERPBOMNO, N'?') + N'，建议ODS版本裁决',
                   N'USE_MAX_BOMNO', GETDATE()
            FROM #LvSel ls
            WHERE ls.ERPNNBOMCnt > 1;

            -- R32 S3: 按选定BOMNO展开（ERP非空优先；无ERP则用最大非空；全NULL则匹配NULL边）
            INSERT INTO #RT_Expand
            SELECT prev.BOMNO, e.ParentMaterialCode, e.ChildMaterialCode,
                   e.Quantity,           -- ⚠️ 单位用量，绝不累乘
                   @CurrentLevel,
                   prev.Path + ' -> ' + e.ChildMaterialCode,
                   e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode
            FROM #RT_Expand prev
            INNER JOIN #LvSel ls ON ls.ChildMaterialCode = prev.ChildMaterialCode
            INNER JOIN MES_BOM_Edge_Active e    -- ⚡ 直读物化边表，不走 MES_BOM_View
                   ON e.ParentMaterialCode = prev.ChildMaterialCode
                  AND (
                      (ls.ERPNNBOMCnt > 0 AND e.BOMNO = ls.MaxERPBOMNO)
                   OR (ls.ERPNNBOMCnt = 0 AND ls.MaxAnyBOMNO IS NOT NULL
                       AND e.BOMNO = ls.MaxAnyBOMNO)
                   OR (ls.ERPNNBOMCnt = 0 AND ls.MaxAnyBOMNO IS NULL AND e.BOMNO IS NULL)
                  )
            WHERE prev.Level = @CurrentLevel - 1
              AND e.IsActive = 1
              AND prev.Path NOT LIKE '%' + e.ChildMaterialCode + '%'; -- 防环

            SET @RowsInserted = @@ROWCOUNT;

            DROP TABLE #LvMat;
            DROP TABLE #LvSel;
        END

        -- R35: MES边Produce代入 — ChildSourceHintCode为NULL/字符串NULL时从ERP边继承或按物料前缀推断
        UPDATE w
        SET w.ChildSourceHintCode = COALESCE(
            (SELECT TOP 1 ee.ChildSourceHintCode
             FROM MES_BOM_Edge_Active ee
             WHERE ee.ParentMaterialCode = w.ParentMaterialCode
               AND ee.ChildMaterialCode  = w.ChildMaterialCode
               AND ee.SourceSystem       = N'ERP'
               AND ee.ChildSourceHintCode IS NOT NULL
               AND ee.IsActive = 1
             ORDER BY ee.IsDefaultVersion DESC, ee.BOMNO DESC),
            CASE WHEN w.ChildMaterialCode LIKE N'RAW%' THEN N'2' ELSE N'1' END
        )
        FROM #RT_Expand w
        WHERE w.ChildSourceHintCode IS NULL OR w.ChildSourceHintCode = N'NULL';

        -- 6. 落地至 MES_APS_BOM_Workset_Realtime（含 RequestDetailId）
        --    P8: BOMNO 使用 ISNULL(t.BOMNO, @ResolvedBOMNO)，Strategy A 兜底，保证非空
        INSERT INTO MES_APS_BOM_Workset_Realtime (
            BOMNO, ParentMaterialCode, ChildMaterialCode, Quantity, Level,
            ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
            RequestDetailId, CreatedAt
        )
        SELECT ISNULL(BOMNO, @ResolvedBOMNO),  -- P8: Strategy A兜底，NULL BOMNO边用 'MAT:{MaterialCode}'
               ParentMaterialCode, ChildMaterialCode, Quantity, Level,
               ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
               @RequestDetailId, GETDATE()
        FROM #RT_Expand;

        DROP TABLE #RT_Expand;

        -- 7. 后置回填（Stage/Factory/StageDetail_Realtime/Issues）
        --    P8: 传 ResolvedBOMNO（非空）+ @RequestDetailId，Enrich SP 按 ResolvedBOMNO 精确过滤
        EXEC sp_EnrichBOMWorksetRealtime @ResolvedBOMNO, @RequestDetailId;

        -- 8. 更新 READY（v5.1.0: 回填 ExpandedRowCount 仅用于诊断，不参与 READY 判断）
        DECLARE @ExpandedRowCount INT = (
            SELECT COUNT(*)
            FROM MES_APS_BOM_Workset_Realtime
            WHERE (@RequestDetailId IS NOT NULL AND RequestDetailId = @RequestDetailId)
               OR (@RequestDetailId IS NULL AND BOMNO = @ResolvedBOMNO)
        );
        UPDATE MES_API_BOM_Request_Realtime
        SET Status = 'READY',
            CompletedTime = GETDATE(),
            ExpandedRowCount = @ExpandedRowCount
        WHERE Id = @RequestId;

    END TRY
    BEGIN CATCH
        DROP TABLE IF EXISTS #RT_Expand;
        DROP TABLE IF EXISTS #RT_EntryCandidates;
        DROP TABLE IF EXISTS #RT_EntryResolved;
        DROP TABLE IF EXISTS #LvMat;
        DROP TABLE IF EXISTS #LvSel;
        UPDATE MES_API_BOM_Request_Realtime
        SET Status       = 'FAILED',
            CompletedTime = GETDATE(),
            ErrorMessage  = ERROR_MESSAGE()
        WHERE Id = @RequestId;
        THROW;
    END CATCH;
END;
GO

-- =============================================
-- 3.4 BOM 回填存储过程（5号位核心逻辑，v5.0.18 新增）
-- sp_EnrichBOMWorkset: 对已展开的 Workset 进行 R17/R24/R25/R26/R27 回填
-- 调用时机：sp_ExpandBOMBatch 完成 CTE 展开后调用（或由外部流程独立调用）
-- 输入：@BatchNo（已由 sp_ExpandBOMBatch 展开完成的批次）
-- 产出：
--   1) UPDATE MES_APS_BOM_Workset: ChildRequiredFactory + ChildRequiredStageCode
--   2) INSERT MES_APS_BOM_Workset_StageDetail: EDGE（子件阶段链）+ ROOT（根产品完工路径）
--   3) INSERT MES_APS_BOM_Workset_Issues: 各类异常降级登记（永不阻塞批次）
-- 依赖视图/表：ProduceToFactoryMap / MES_ProcessCode_View / MES_BOM_View / StageDict
-- 设计依据：《BOM_Workset_生成与错误处理技术方案_v1.0》§3（5号位回填流程）
-- 核心算法对应 Python 参考实现：_workset_excel.py → resolve_child_chain + material_stage_chain
-- ⚠️ 降级哲学：全部"降级 + 登记"，永不因数据质量阻塞批次
-- ⚠️ 实时链路版（sp_EnrichBOMWorksetRealtime）逻辑相同，操作 _Realtime 表；Issues 正式路径 BatchNo=RT:RD:{RequestDetailId}（v5.1.0），RT:{BOMNO}仅deprecated兼容
-- =============================================
CREATE PROCEDURE sp_EnrichBOMWorkset
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = GETDATE();
    DECLARE @EnrichedCount INT = 0;
    DECLARE @StageDetailCount INT = 0;
    DECLARE @IssueCount INT = 0;

    -- ================================================================
    -- Step 0: 前置校验 — QUANTITY_INVALID / MISSING_PRODUCE 降级
    -- ================================================================

    -- 0a. QUANTITY_INVALID: Quantity ≤ 0 或 NULL → 按 1 兜底（DegradeAction=QTY_DEFAULT_1）
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, CreatedAt)
    SELECT @BatchNo, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'QUANTITY_INVALID', N'WARN',
           N'Quantity=' + ISNULL(CAST(w.Quantity AS NVARCHAR(30)), N'NULL') + N'，按1兜底',
           N'QTY_DEFAULT_1', @Now
    FROM MES_APS_BOM_Workset w
    WHERE w.BatchNo = @BatchNo
      AND (w.Quantity <= 0 OR w.Quantity IS NULL);

    UPDATE MES_APS_BOM_Workset SET Quantity = 1.0
    WHERE BatchNo = @BatchNo AND (Quantity <= 0 OR Quantity IS NULL);

    -- 0b. MISSING_PRODUCE: ChildSourceHintCode 为空 → 按 Produce=1(内制·继承) 兜底
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, CreatedAt)
    SELECT @BatchNo, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           NULL, N'MISSING_PRODUCE', N'WARN',
           N'Produce字段为空，按Produce=1(内制·继承)兜底',
           N'PRODUCE_DEFAULT_1', @Now
    FROM MES_APS_BOM_Workset w
    WHERE w.BatchNo = @BatchNo
      AND (w.ChildSourceHintCode IS NULL OR w.ChildSourceHintCode = N'' OR w.ChildSourceHintCode = N'NULL');

    UPDATE MES_APS_BOM_Workset SET ChildSourceHintCode = N'1'
    WHERE BatchNo = @BatchNo
      AND (ChildSourceHintCode IS NULL OR ChildSourceHintCode = N'' OR ChildSourceHintCode = N'NULL');

    -- ================================================================
    -- Step 1: R17 工厂映射 → ChildRequiredFactory
    -- ================================================================

    SELECT ProduceCode, SourceCategory, FactoryStrategy, TargetFactory,
           ShouldDrilldown, CrossOrgHandoffFlag
    INTO #PFM
    FROM ProduceToFactoryMap WHERE IsActive = 1;

    -- 1a. FIXED 策略（Produce=5/6/7/8/9/11）：ChildRequiredFactory = TargetFactory
    UPDATE w
    SET w.ChildRequiredFactory = pfm.TargetFactory
    FROM MES_APS_BOM_Workset w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    WHERE w.BatchNo = @BatchNo AND pfm.FactoryStrategy = N'FIXED';

    -- 1b. INHERIT 策略（Produce=1）：parent_factory(ChildProcRefCode, ParentProcRefCode)
    --     R21：优先领料位(ChildProcRefCode) → FactoryCode，回退完成位(ParentProcRefCode)
    UPDATE w
    SET w.ChildRequiredFactory = COALESCE(
        NULLIF(pc_m.FactoryCode, N''),
        NULLIF(pc_g.FactoryCode, N'')
    )
    FROM MES_APS_BOM_Workset w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    LEFT JOIN MES_ProcessCode_View pc_m
        ON pc_m.ProcessCode = RIGHT(N'000000' + ISNULL(w.ChildProcRefCode, N''), 6)
    LEFT JOIN MES_ProcessCode_View pc_g
        ON pc_g.ProcessCode = RIGHT(N'000000' + ISNULL(w.ParentProcRefCode, N''), 6)
    WHERE w.BatchNo = @BatchNo AND pfm.FactoryStrategy = N'INHERIT';

    -- 1c. NONE 策略（外购 Produce=0/2/3/4/10）：ChildRequiredFactory 保持 NULL，无需操作

    -- ================================================================
    -- Step 2: 阶段链推导 + StageDetail EDGE（R24 原生序 / R25 异厂收敛 / R26 受托隔离）
    -- 算法参考：_workset_excel.py → material_stage_chain() + resolve_child_chain()
    -- ================================================================

    -- 2a. 收集所有内制子件候选（非外购 且 已推导出工厂）
    SELECT DISTINCT w.ChildMaterialCode, w.ChildRequiredFactory
    INTO #Candidates
    FROM MES_APS_BOM_Workset w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    WHERE w.BatchNo = @BatchNo
      AND pfm.SourceCategory <> N'PURCHASE'
      AND w.ChildRequiredFactory IS NOT NULL;

    -- 2b. material_stage_chain（R26 过滤版）
    --     子件作为"父件"查 MES_BOM_View → 取两个 ProcessCode → JOIN MES_ProcessCode_View 得 StageCode
    --     R24：按 BOM 边原生序遍历 ChildProcRefCode(领料位) → ParentProcRefCode(完成位)
    --     R26：按 FactoryCode = ChildRequiredFactory 过滤（受托隔离：按账面厂，不按实际生产厂）
    --     去重：同一 StageCode 只保留首次出现
    ;WITH EdgeCodes AS (
        SELECT c.ChildMaterialCode, c.ChildRequiredFactory,
               bv.ChildProcRefCode  AS EdgeChildPC,
               bv.ParentProcRefCode AS EdgeParentPC,
               ROW_NUMBER() OVER (
                   PARTITION BY c.ChildMaterialCode
                   ORDER BY bv.BOMNO, bv.ParentMaterialCode, bv.ChildMaterialCode
               ) AS EdgeOrd
        FROM #Candidates c
        INNER JOIN MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
            ON bv.ParentMaterialCode = c.ChildMaterialCode
           AND bv.IsActive = 1 AND bv.IsDefaultVersion = 1
    ),
    UnpivotPC AS (
        -- R24 原生序：每条 BOM 边先取领料位(ChildProcRefCode)再取完成位(ParentProcRefCode)
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeChildPC AS PC, EdgeOrd * 2 - 1 AS Seq
        FROM EdgeCodes WHERE ISNULL(EdgeChildPC, N'') <> N''
        UNION ALL
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeParentPC AS PC, EdgeOrd * 2 AS Seq
        FROM EdgeCodes WHERE ISNULL(EdgeParentPC, N'') <> N''
    ),
    WithStage AS (
        SELECT u.ChildMaterialCode, u.ChildRequiredFactory, u.Seq,
               pc.StageCode, pc.FactoryCode
        FROM UnpivotPC u
        INNER JOIN MES_ProcessCode_View pc
            ON pc.ProcessCode = RIGHT(N'000000' + u.PC, 6)
        WHERE ISNULL(pc.StageCode, N'') <> N''            -- 跳过无 StageCode 映射的码（入库/出口/未映射）
          AND pc.FactoryCode = u.ChildRequiredFactory     -- R26：受托隔离，按账面厂过滤
    ),
    Deduped AS (
        SELECT *, ROW_NUMBER() OVER (
            PARTITION BY ChildMaterialCode, StageCode ORDER BY Seq
        ) AS DupRank
        FROM WithStage
    )
    SELECT ChildMaterialCode, ChildRequiredFactory, StageCode, FactoryCode,
           ROW_NUMBER() OVER (PARTITION BY ChildMaterialCode ORDER BY Seq) AS StageOrd
    INTO #Chain_R26
    FROM Deduped WHERE DupRank = 1;

    -- 2c. 识别 R26 过滤后链为空的子件 → 需要 R25 回退或登记 Issue
    SELECT c.ChildMaterialCode, c.ChildRequiredFactory
    INTO #NeedFallback
    FROM #Candidates c
    WHERE NOT EXISTS (SELECT 1 FROM #Chain_R26 cr WHERE cr.ChildMaterialCode = c.ChildMaterialCode);

    -- 2c-1. LEAF 检测：子件作为父件在 MES_BOM_View 中无任何 BOM 边
    --       Severity=INFO，因为叶子节点无下阶是正常现象；1号位保守策略兜底
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, ExpectedFactory, CreatedAt)
    SELECT DISTINCT @BatchNo, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'LEAF', N'INFO',
           N'Produce声明内制但物料' + nf.ChildMaterialCode + N'无下阶BOM',
           N'STAGE_NULL', nf.ChildRequiredFactory, @Now
    FROM #NeedFallback nf
    INNER JOIN MES_APS_BOM_Workset w
        ON w.ChildMaterialCode = nf.ChildMaterialCode AND w.BatchNo = @BatchNo
       AND w.ChildRequiredFactory = nf.ChildRequiredFactory -- 防止同物料不同Produce误记导Produce
    WHERE NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
        WHERE bv.ParentMaterialCode = nf.ChildMaterialCode AND bv.IsActive = 1
    );

    -- 移除 LEAF，不进入 R25 回退
    DELETE nf FROM #NeedFallback nf
    WHERE NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
        WHERE bv.ParentMaterialCode = nf.ChildMaterialCode AND bv.IsActive = 1
    );

    -- 2c-2. R25 回退：不过滤工厂的原生链（为剩余候选子件）
    ;WITH EdgeCodes_NF AS (
        SELECT c.ChildMaterialCode, c.ChildRequiredFactory,
               bv.ChildProcRefCode  AS EdgeChildPC,
               bv.ParentProcRefCode AS EdgeParentPC,
               ROW_NUMBER() OVER (
                   PARTITION BY c.ChildMaterialCode
                   ORDER BY bv.BOMNO, bv.ParentMaterialCode, bv.ChildMaterialCode
               ) AS EdgeOrd
        FROM #NeedFallback c
        INNER JOIN MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
            ON bv.ParentMaterialCode = c.ChildMaterialCode
           AND bv.IsActive = 1 AND bv.IsDefaultVersion = 1
    ),
    UnpivotPC_NF AS (
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeChildPC AS PC, EdgeOrd * 2 - 1 AS Seq
        FROM EdgeCodes_NF WHERE ISNULL(EdgeChildPC, N'') <> N''
        UNION ALL
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeParentPC AS PC, EdgeOrd * 2 AS Seq
        FROM EdgeCodes_NF WHERE ISNULL(EdgeParentPC, N'') <> N''
    ),
    WithStage_NF AS (
        SELECT u.ChildMaterialCode, u.ChildRequiredFactory, u.Seq,
               pc.StageCode, pc.FactoryCode
        FROM UnpivotPC_NF u
        INNER JOIN MES_ProcessCode_View pc
            ON pc.ProcessCode = RIGHT(N'000000' + u.PC, 6)
        WHERE ISNULL(pc.StageCode, N'') <> N''
        -- ⚠️ 不过滤 FactoryCode（R25 回退：不限工厂，保留 BOM 原生链）
    ),
    Deduped_NF AS (
        SELECT *, ROW_NUMBER() OVER (
            PARTITION BY ChildMaterialCode, StageCode ORDER BY Seq
        ) AS DupRank
        FROM WithStage_NF
    )
    SELECT ChildMaterialCode, ChildRequiredFactory, StageCode, FactoryCode,
           ROW_NUMBER() OVER (PARTITION BY ChildMaterialCode ORDER BY Seq) AS StageOrd
    INTO #Chain_Fallback
    FROM Deduped_NF WHERE DupRank = 1;

    -- 2c-3. NO_STAGE：回退链也为空（有 BOM 但全为入库/出口码，无大工艺段）
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, ExpectedFactory, CreatedAt)
    SELECT DISTINCT @BatchNo, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'NO_STAGE', N'WARN',
           N'物料' + nf.ChildMaterialCode + N'有BOM但无可识别的大工艺段（全为入库/出口码）',
           N'STAGE_NULL', nf.ChildRequiredFactory, @Now
    FROM #NeedFallback nf
    INNER JOIN MES_APS_BOM_Workset w
        ON w.ChildMaterialCode = nf.ChildMaterialCode AND w.BatchNo = @BatchNo
       AND w.ChildRequiredFactory = nf.ChildRequiredFactory -- 防止同物料不同Produce误记导Produce
    WHERE NOT EXISTS (
        SELECT 1 FROM #Chain_Fallback fb WHERE fb.ChildMaterialCode = nf.ChildMaterialCode
    );

    -- 2c-4. FACTORY_MISMATCH / FACTORY_MISMATCH_MULTI：回退链有段但工厂不匹配
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction,
         ExpectedFactory, ActualFactory, CreatedAt)
    SELECT DISTINCT @BatchNo, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode,
           CASE WHEN (SELECT COUNT(DISTINCT fb2.FactoryCode)
                      FROM #Chain_Fallback fb2
                      WHERE fb2.ChildMaterialCode = nf.ChildMaterialCode) > 1
                THEN N'FACTORY_MISMATCH_MULTI' ELSE N'FACTORY_MISMATCH' END,
           N'WARN',
           N'Produce=' + ISNULL(w.ChildSourceHintCode, N'?')
               + N' 应归' + ISNULL(nf.ChildRequiredFactory, N'?')
               + N'，BOM实际在' + ISNULL(
                   STUFF((SELECT DISTINCT N',' + fb3.FactoryCode
                          FROM #Chain_Fallback fb3
                          WHERE fb3.ChildMaterialCode = nf.ChildMaterialCode
                          FOR XML PATH(N'')), 1, 1, N''), N'?'),
           N'FACTORY_FALLBACK',
           nf.ChildRequiredFactory,
           STUFF((SELECT DISTINCT N',' + fb4.FactoryCode
                  FROM #Chain_Fallback fb4
                  WHERE fb4.ChildMaterialCode = nf.ChildMaterialCode
                  FOR XML PATH(N'')), 1, 1, N''),
           @Now
    FROM #NeedFallback nf
    INNER JOIN MES_APS_BOM_Workset w
        ON w.ChildMaterialCode = nf.ChildMaterialCode AND w.BatchNo = @BatchNo
       AND w.ChildRequiredFactory = nf.ChildRequiredFactory -- 防止同物料不同Produce误记导Produce
    WHERE EXISTS (
        SELECT 1 FROM #Chain_Fallback fb WHERE fb.ChildMaterialCode = nf.ChildMaterialCode
    );

    -- 2d. 合并 R26 过滤链 + R25 回退链 → 最终链
    SELECT * INTO #Chain_Final FROM #Chain_R26
    UNION ALL
    SELECT * FROM #Chain_Fallback;

    SELECT ChildMaterialCode, MAX(StageOrd) AS MaxOrd
    INTO #ChainMax FROM #Chain_Final GROUP BY ChildMaterialCode;

    -- 2e. 写 StageDetail EDGE 记录
    --     每条 Workset 行写一组 EDGE（WorksetId = w.Id，对应单行 BOM 路径；不去重）
    INSERT INTO MES_APS_BOM_Workset_StageDetail (
        WorksetId, BatchNo, BOMNO, StageScopeType, ParentMaterialCode, ChildMaterialCode,
        StageSeq, StageCode, IsSupplyThreshold, CreatedAt)
    SELECT
        w.Id,
        @BatchNo, w.BOMNO, N'EDGE', w.ParentMaterialCode, w.ChildMaterialCode,
        cf.StageOrd * 10,
        cf.StageCode,
        CASE WHEN cf.StageOrd = cm.MaxOrd THEN 1 ELSE 0 END,
        @Now
    FROM MES_APS_BOM_Workset w
    INNER JOIN #Chain_Final cf ON cf.ChildMaterialCode = w.ChildMaterialCode
    INNER JOIN #ChainMax cm ON cm.ChildMaterialCode = w.ChildMaterialCode
    WHERE w.BatchNo = @BatchNo;

    SET @StageDetailCount = @@ROWCOUNT;

    -- 2f. 更新 ChildRequiredStageCode = 阶段链最后一段的 StageCode
    UPDATE w
    SET w.ChildRequiredStageCode = cf.StageCode
    FROM MES_APS_BOM_Workset w
    INNER JOIN #Chain_Final cf ON cf.ChildMaterialCode = w.ChildMaterialCode
    INNER JOIN #ChainMax cm ON cm.ChildMaterialCode = w.ChildMaterialCode
    WHERE w.BatchNo = @BatchNo AND cf.StageOrd = cm.MaxOrd;

    SET @EnrichedCount = @@ROWCOUNT;

    -- ================================================================
    -- Step 3: ROOT 路径推导（Level=1 → 根产品自身完工阶段路径）
    -- v5.0.29 修正：同时 Unpivot ChildProcRefCode(领料位) + ParentProcRefCode(完成位)，
    --   与 Step 2 material_stage_chain 逻辑对称。
    --   原因：ASSY 类根产品 ParentProcRefCode=出口库码（无 StageCode），
    --         真正的装配工序在 ChildProcRefCode（有 StageCode）；
    --         仅取 ParentProcRefCode 导致 ASSY 根产品 ROOT 行全空。
    -- ROOT 行：StageScopeType='ROOT', ParentMaterialCode=NULL, IsSupplyThreshold=0
    -- ================================================================
    ;WITH RootEdges AS (
        SELECT DISTINCT w.BOMNO,
               w.RequestDetailId,                          -- v5.0.30 追溯锚点：ROOT粒度升级为BOMNO+RequestDetailId
               w.ParentMaterialCode AS RootMaterial,
               w.ChildProcRefCode   AS EdgeChildPC,        -- 领料位（ASSY装配工序）
               w.ParentProcRefCode  AS EdgeParentPC,       -- 完成位（出口库 / 完工工序）
               ROW_NUMBER() OVER (
                   PARTITION BY w.BOMNO, w.RequestDetailId -- 按 RequestDetail 分区，保证每个订单独立生成ROOT
                   ORDER BY w.ParentMaterialCode, w.ChildMaterialCode
               ) AS EdgeOrd
        FROM MES_APS_BOM_Workset w
        WHERE w.BatchNo = @BatchNo AND w.Level = 1
    ),
    RootUnpivot AS (
        -- R24 原生序：先领料位(ChildProcRefCode) 再完成位(ParentProcRefCode)
        -- ASSY 根产品：ChildProcRefCode=装配工序(有StageCode)；ParentProcRefCode=出口库(无StageCode，自动跳过)
        SELECT BOMNO, RequestDetailId, RootMaterial, EdgeChildPC AS PC, EdgeOrd * 2 - 1 AS Seq
        FROM RootEdges WHERE ISNULL(EdgeChildPC, N'') <> N''
        UNION ALL
        SELECT BOMNO, RequestDetailId, RootMaterial, EdgeParentPC AS PC, EdgeOrd * 2 AS Seq
        FROM RootEdges WHERE ISNULL(EdgeParentPC, N'') <> N''
    ),
    RootWithStage AS (
        SELECT ru.BOMNO, ru.RequestDetailId, ru.RootMaterial, ru.Seq,
               pc.StageCode, pc.FactoryCode
        FROM RootUnpivot ru
        INNER JOIN MES_ProcessCode_View pc
            ON pc.ProcessCode = RIGHT(N'000000' + ru.PC, 6)
        WHERE ISNULL(pc.StageCode, N'') <> N''
    ),
    RootDeduped AS (
        SELECT *, ROW_NUMBER() OVER (
            PARTITION BY BOMNO, RequestDetailId, StageCode ORDER BY Seq
        ) AS DupRank
        FROM RootWithStage
    ),
    RootChain AS (
        SELECT BOMNO, RequestDetailId, RootMaterial, StageCode, FactoryCode,
               ROW_NUMBER() OVER (PARTITION BY BOMNO, RequestDetailId ORDER BY Seq) AS StageOrd
        FROM RootDeduped WHERE DupRank = 1
    )
    INSERT INTO MES_APS_BOM_Workset_StageDetail (
        WorksetId, BatchNo, BOMNO, StageScopeType, ParentMaterialCode, ChildMaterialCode,
        StageSeq, StageCode, IsSupplyThreshold, CreatedAt)
    SELECT
        -- ROOT 不是具体BOM边，WorksetId取该RequestDetail下Level=1的代表性锚点（MIN Id）
        -- 语义：归属于某RequestDetail下的根产品展开结果，非"由某条BOM边派生"
        (SELECT MIN(w2.Id) FROM MES_APS_BOM_Workset w2
         WHERE w2.BatchNo = @BatchNo AND w2.BOMNO = rc.BOMNO
           AND w2.RequestDetailId = rc.RequestDetailId AND w2.Level = 1),
        @BatchNo, rc.BOMNO, N'ROOT', NULL, rc.RootMaterial,
        rc.StageOrd * 10, rc.StageCode, 0, @Now
    FROM RootChain rc;

    SET @StageDetailCount = @StageDetailCount + @@ROWCOUNT;

    -- ================================================================
    -- Step 4: UNKNOWN_PROCCODE 检测（工序码不在 ProcessCodeDict 中）
    -- 仅检测内制件的 ParentProcRefCode / ChildProcRefCode
    -- ================================================================
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, CreatedAt)
    SELECT DISTINCT @BatchNo, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'UNKNOWN_PROCCODE', N'WARN',
           N'工序码 ChildProcRef=' + ISNULL(w.ChildProcRefCode, N'NULL')
               + N' ParentProcRef=' + ISNULL(w.ParentProcRefCode, N'NULL') + N' 查不到工序字典',
           N'STAGE_NULL', @Now
    FROM MES_APS_BOM_Workset w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    WHERE w.BatchNo = @BatchNo
      AND pfm.SourceCategory <> N'PURCHASE'
      AND (
          (ISNULL(w.ChildProcRefCode, N'') <> N''
           AND NOT EXISTS (SELECT 1 FROM MES_ProcessCode_View pc
                           WHERE pc.ProcessCode = RIGHT(N'000000' + w.ChildProcRefCode, 6)))
          OR
          (ISNULL(w.ParentProcRefCode, N'') <> N''
           AND NOT EXISTS (SELECT 1 FROM MES_ProcessCode_View pc
                           WHERE pc.ProcessCode = RIGHT(N'000000' + w.ParentProcRefCode, 6)))
      );

    -- ================================================================
    -- Step 5: 日志记录
    -- ================================================================
    SET @IssueCount = (SELECT COUNT(*) FROM MES_APS_BOM_Workset_Issues WHERE BatchNo = @BatchNo);

    -- v5.0.46：生成跨厂交接边
    EXEC dbo.sp_GenerateBOMCrossFactoryEdge @BatchNo;
    INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
    VALUES (@BatchNo,
        N'sp_EnrichBOMWorkset 完成: 回填 ' + CAST(@EnrichedCount AS NVARCHAR(20))
        + N' 行 ChildRequiredStageCode/Factory; StageDetail 写入 '
        + CAST(@StageDetailCount AS NVARCHAR(20))
        + N' 行(EDGE+ROOT); Issues 登记 ' + CAST(@IssueCount AS NVARCHAR(20)) + N' 条',
        @Now);

    -- ================================================================
    -- Cleanup
    -- ================================================================
    DROP TABLE IF EXISTS #PFM;
    DROP TABLE IF EXISTS #Candidates;
    DROP TABLE IF EXISTS #Chain_R26;
    DROP TABLE IF EXISTS #NeedFallback;
    DROP TABLE IF EXISTS #Chain_Fallback;
    DROP TABLE IF EXISTS #Chain_Final;
    DROP TABLE IF EXISTS #ChainMax;

GO

-- =============================================
-- 3.5 BOM 实时链路回填存储过程（v5.0.18 新增；v5.0.26c P8 修订）
-- sp_EnrichBOMWorksetRealtime: 与 sp_EnrichBOMWorkset 逻辑完全相同
-- 差异点：
--   1) 输入：@BOMNO（始终为 ResolvedBOMNO，非空；Strategy A 已由调用方处理）
--   2) 工作集表：MES_APS_BOM_Workset_Realtime（无 BatchNo 列，按 BOMNO 过滤）
--   3) StageDetail 表：MES_APS_BOM_Workset_StageDetail_Realtime（无 BatchNo 列）
--   4) Issues 表：MES_APS_BOM_Workset_Issues；v5.1.0 正式路径 BatchNo=RT:RD:{RequestDetailId}；RT:{BOMNO} 仅 deprecated 兼容
--   5) Log 表：BatchNo = NULL
--   6) @BOMNO 由调用方传入 ResolvedBOMNO（非空），Strategy A 已在 sp_ExpandBOMRealtime_vNext 处理
--   7) @RequestDetailId（可选，v5.0.33 全面推广）：
--      @RequestDetailId IS NOT NULL 时，Step 0-4 所有读写 Workset_Realtime 处均加
--      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId) 过滤，
--      防止多个实时订单共享同一 BOMNO 时互相污染彼此的 StageDetail/Issues/回填数据
-- =============================================
CREATE OR ALTER PROCEDURE sp_EnrichBOMWorksetRealtime
    @BOMNO           NVARCHAR(50),       -- 始终为 ResolvedBOMNO（非空；MAT:前缀已由调用方处理）
    @RequestDetailId BIGINT = NULL       -- v5.0.26c P8：订单粒度追溯锚点（可选）
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = GETDATE();
    -- v5.1.0: 统一实时切片号，与 sp_ExpandBOMRealtime_vNext 和 sp_GenerateBOMCrossFactoryEdgeRealtime 保持一致
    --   正式路径：RT:RD:{RequestDetailId}
    --   deprecated 兼容路径（无 RequestDetailId）：RT:{BOMNO}，用 LEFT 限制到 Issues.BatchNo 列长 NVARCHAR(50)
    DECLARE @SyntheticBatch NVARCHAR(50) =
        CASE WHEN @RequestDetailId IS NOT NULL
             THEN CONCAT(N'RT:RD:', CAST(@RequestDetailId AS NVARCHAR(20)))
             ELSE LEFT(CONCAT(N'RT:', @BOMNO), 50)
        END;
    DECLARE @EnrichedCount INT = 0;
    DECLARE @StageDetailCount INT = 0;
    DECLARE @IssueCount INT = 0;

    -- ================================================================
    -- Step 0: 前置校验
    -- ================================================================

    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, CreatedAt)
    SELECT @SyntheticBatch, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'QUANTITY_INVALID', N'WARN',
           N'Quantity=' + ISNULL(CAST(w.Quantity AS NVARCHAR(30)), N'NULL') + N'，按1兜底',
           N'QTY_DEFAULT_1', @Now
    FROM MES_APS_BOM_Workset_Realtime w
    WHERE w.BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
      AND (w.Quantity <= 0 OR w.Quantity IS NULL);

    UPDATE MES_APS_BOM_Workset_Realtime SET Quantity = 1.0
    WHERE BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR RequestDetailId = @RequestDetailId)
      AND (Quantity <= 0 OR Quantity IS NULL);

    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, CreatedAt)
    SELECT @SyntheticBatch, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           NULL, N'MISSING_PRODUCE', N'WARN',
           N'Produce字段为空，按Produce=1(内制·继承)兜底',
           N'PRODUCE_DEFAULT_1', @Now
    FROM MES_APS_BOM_Workset_Realtime w
    WHERE w.BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
      AND (w.ChildSourceHintCode IS NULL OR w.ChildSourceHintCode = N'');

    UPDATE MES_APS_BOM_Workset_Realtime SET ChildSourceHintCode = N'1'
    WHERE BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR RequestDetailId = @RequestDetailId)
      AND (ChildSourceHintCode IS NULL OR ChildSourceHintCode = N'');

    -- ================================================================
    -- Step 1: R17 工厂映射 → ChildRequiredFactory
    -- ================================================================

    SELECT ProduceCode, SourceCategory, FactoryStrategy, TargetFactory,
           ShouldDrilldown, CrossOrgHandoffFlag
    INTO #PFM
    FROM ProduceToFactoryMap WHERE IsActive = 1;

    UPDATE w
    SET w.ChildRequiredFactory = pfm.TargetFactory
    FROM MES_APS_BOM_Workset_Realtime w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    WHERE w.BOMNO = @BOMNO AND pfm.FactoryStrategy = N'FIXED'
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId);

    UPDATE w
    SET w.ChildRequiredFactory = COALESCE(
        NULLIF(pc_m.FactoryCode, N''),
        NULLIF(pc_g.FactoryCode, N'')
    )
    FROM MES_APS_BOM_Workset_Realtime w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    LEFT JOIN MES_ProcessCode_View pc_m
        ON pc_m.ProcessCode = RIGHT(N'000000' + ISNULL(w.ChildProcRefCode, N''), 6)
    LEFT JOIN MES_ProcessCode_View pc_g
        ON pc_g.ProcessCode = RIGHT(N'000000' + ISNULL(w.ParentProcRefCode, N''), 6)
    WHERE w.BOMNO = @BOMNO AND pfm.FactoryStrategy = N'INHERIT'
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId);

    -- ================================================================
    -- Step 2: 阶段链推导 + StageDetail EDGE
    -- ================================================================

    SELECT DISTINCT w.ChildMaterialCode, w.ChildRequiredFactory
    INTO #Candidates
    FROM MES_APS_BOM_Workset_Realtime w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    WHERE w.BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
      AND pfm.SourceCategory <> N'PURCHASE'
      AND w.ChildRequiredFactory IS NOT NULL;

    -- 2b. R26 过滤版 material_stage_chain
    ;WITH EdgeCodes AS (
        SELECT c.ChildMaterialCode, c.ChildRequiredFactory,
               bv.ChildProcRefCode  AS EdgeChildPC,
               bv.ParentProcRefCode AS EdgeParentPC,
               ROW_NUMBER() OVER (
                   PARTITION BY c.ChildMaterialCode
                   ORDER BY bv.BOMNO, bv.ParentMaterialCode, bv.ChildMaterialCode
               ) AS EdgeOrd
        FROM #Candidates c
        INNER JOIN MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
            ON bv.ParentMaterialCode = c.ChildMaterialCode
           AND bv.IsActive = 1 AND bv.IsDefaultVersion = 1
    ),
    UnpivotPC AS (
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeChildPC AS PC, EdgeOrd * 2 - 1 AS Seq
        FROM EdgeCodes WHERE ISNULL(EdgeChildPC, N'') <> N''
        UNION ALL
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeParentPC AS PC, EdgeOrd * 2 AS Seq
        FROM EdgeCodes WHERE ISNULL(EdgeParentPC, N'') <> N''
    ),
    WithStage AS (
        SELECT u.ChildMaterialCode, u.ChildRequiredFactory, u.Seq,
               pc.StageCode, pc.FactoryCode
        FROM UnpivotPC u
        INNER JOIN MES_ProcessCode_View pc
            ON pc.ProcessCode = RIGHT(N'000000' + u.PC, 6)
        WHERE ISNULL(pc.StageCode, N'') <> N''
          AND pc.FactoryCode = u.ChildRequiredFactory
    ),
    Deduped AS (
        SELECT *, ROW_NUMBER() OVER (
            PARTITION BY ChildMaterialCode, StageCode ORDER BY Seq
        ) AS DupRank
        FROM WithStage
    )
    SELECT ChildMaterialCode, ChildRequiredFactory, StageCode, FactoryCode,
           ROW_NUMBER() OVER (PARTITION BY ChildMaterialCode ORDER BY Seq) AS StageOrd
    INTO #Chain_R26
    FROM Deduped WHERE DupRank = 1;

    -- 2c. R25 回退
    SELECT c.ChildMaterialCode, c.ChildRequiredFactory
    INTO #NeedFallback
    FROM #Candidates c
    WHERE NOT EXISTS (SELECT 1 FROM #Chain_R26 cr WHERE cr.ChildMaterialCode = c.ChildMaterialCode);

    -- LEAF
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, ExpectedFactory, CreatedAt)
    SELECT DISTINCT @SyntheticBatch, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'LEAF', N'INFO',
           N'Produce声明内制但物料' + nf.ChildMaterialCode + N'无下阶BOM',
           N'STAGE_NULL', nf.ChildRequiredFactory, @Now
    FROM #NeedFallback nf
    INNER JOIN MES_APS_BOM_Workset_Realtime w
        ON w.ChildMaterialCode = nf.ChildMaterialCode AND w.BOMNO = @BOMNO
       AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
    WHERE NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
        WHERE bv.ParentMaterialCode = nf.ChildMaterialCode AND bv.IsActive = 1
    );

    DELETE nf FROM #NeedFallback nf
    WHERE NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
        WHERE bv.ParentMaterialCode = nf.ChildMaterialCode AND bv.IsActive = 1
    );

    -- R25 不过滤回退
    ;WITH EdgeCodes_NF AS (
        SELECT c.ChildMaterialCode, c.ChildRequiredFactory,
               bv.ChildProcRefCode  AS EdgeChildPC,
               bv.ParentProcRefCode AS EdgeParentPC,
               ROW_NUMBER() OVER (
                   PARTITION BY c.ChildMaterialCode
                   ORDER BY bv.BOMNO, bv.ParentMaterialCode, bv.ChildMaterialCode
               ) AS EdgeOrd
        FROM #NeedFallback c
        INNER JOIN MES_BOM_Edge_Active bv  -- ⚡ 直读物化边表，不走 MES_BOM_View
            ON bv.ParentMaterialCode = c.ChildMaterialCode
           AND bv.IsActive = 1 AND bv.IsDefaultVersion = 1
    ),
    UnpivotPC_NF AS (
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeChildPC AS PC, EdgeOrd * 2 - 1 AS Seq
        FROM EdgeCodes_NF WHERE ISNULL(EdgeChildPC, N'') <> N''
        UNION ALL
        SELECT ChildMaterialCode, ChildRequiredFactory,
               EdgeParentPC AS PC, EdgeOrd * 2 AS Seq
        FROM EdgeCodes_NF WHERE ISNULL(EdgeParentPC, N'') <> N''
    ),
    WithStage_NF AS (
        SELECT u.ChildMaterialCode, u.ChildRequiredFactory, u.Seq,
               pc.StageCode, pc.FactoryCode
        FROM UnpivotPC_NF u
        INNER JOIN MES_ProcessCode_View pc
            ON pc.ProcessCode = RIGHT(N'000000' + u.PC, 6)
        WHERE ISNULL(pc.StageCode, N'') <> N''
    ),
    Deduped_NF AS (
        SELECT *, ROW_NUMBER() OVER (
            PARTITION BY ChildMaterialCode, StageCode ORDER BY Seq
        ) AS DupRank
        FROM WithStage_NF
    )
    SELECT ChildMaterialCode, ChildRequiredFactory, StageCode, FactoryCode,
           ROW_NUMBER() OVER (PARTITION BY ChildMaterialCode ORDER BY Seq) AS StageOrd
    INTO #Chain_Fallback
    FROM Deduped_NF WHERE DupRank = 1;

    -- NO_STAGE
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, ExpectedFactory, CreatedAt)
    SELECT DISTINCT @SyntheticBatch, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'NO_STAGE', N'WARN',
           N'物料' + nf.ChildMaterialCode + N'有BOM但无可识别的大工艺段（全为入库/出口码）',
           N'STAGE_NULL', nf.ChildRequiredFactory, @Now
    FROM #NeedFallback nf
    INNER JOIN MES_APS_BOM_Workset_Realtime w
        ON w.ChildMaterialCode = nf.ChildMaterialCode AND w.BOMNO = @BOMNO
       AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
    WHERE NOT EXISTS (
        SELECT 1 FROM #Chain_Fallback fb WHERE fb.ChildMaterialCode = nf.ChildMaterialCode
    );

    -- FACTORY_MISMATCH
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction,
         ExpectedFactory, ActualFactory, CreatedAt)
    SELECT DISTINCT @SyntheticBatch, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode,
           CASE WHEN (SELECT COUNT(DISTINCT fb2.FactoryCode)
                      FROM #Chain_Fallback fb2
                      WHERE fb2.ChildMaterialCode = nf.ChildMaterialCode) > 1
                THEN N'FACTORY_MISMATCH_MULTI' ELSE N'FACTORY_MISMATCH' END,
           N'WARN',
           N'Produce=' + ISNULL(w.ChildSourceHintCode, N'?')
               + N' 应归' + ISNULL(nf.ChildRequiredFactory, N'?')
               + N'，BOM实际在' + ISNULL(
                   STUFF((SELECT DISTINCT N',' + fb3.FactoryCode
                          FROM #Chain_Fallback fb3
                          WHERE fb3.ChildMaterialCode = nf.ChildMaterialCode
                          FOR XML PATH(N'')), 1, 1, N''), N'?'),
           N'FACTORY_FALLBACK',
           nf.ChildRequiredFactory,
           STUFF((SELECT DISTINCT N',' + fb4.FactoryCode
                  FROM #Chain_Fallback fb4
                  WHERE fb4.ChildMaterialCode = nf.ChildMaterialCode
                  FOR XML PATH(N'')), 1, 1, N''),
           @Now
    FROM #NeedFallback nf
    INNER JOIN MES_APS_BOM_Workset_Realtime w
        ON w.ChildMaterialCode = nf.ChildMaterialCode AND w.BOMNO = @BOMNO
       AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
    WHERE EXISTS (
        SELECT 1 FROM #Chain_Fallback fb WHERE fb.ChildMaterialCode = nf.ChildMaterialCode
    );

    -- 2d. 合并链
    SELECT * INTO #Chain_Final FROM #Chain_R26
    UNION ALL
    SELECT * FROM #Chain_Fallback;

    SELECT ChildMaterialCode, MAX(StageOrd) AS MaxOrd
    INTO #ChainMax FROM #Chain_Final GROUP BY ChildMaterialCode;

    -- 2e. 写 StageDetail EDGE（实时表无 BatchNo 列）
    --     每条 Workset_Realtime 行写一组 EDGE（WorksetId = w.Id；不去重）
    INSERT INTO MES_APS_BOM_Workset_StageDetail_Realtime (
        WorksetId, BOMNO, StageScopeType, ParentMaterialCode, ChildMaterialCode,
        StageSeq, StageCode, IsSupplyThreshold, CreatedAt)
    SELECT
        w.Id,
        w.BOMNO, N'EDGE', w.ParentMaterialCode, w.ChildMaterialCode,
        cf.StageOrd * 10, cf.StageCode,
        CASE WHEN cf.StageOrd = cm.MaxOrd THEN 1 ELSE 0 END,
        @Now
    FROM MES_APS_BOM_Workset_Realtime w
    INNER JOIN #Chain_Final cf ON cf.ChildMaterialCode = w.ChildMaterialCode
    INNER JOIN #ChainMax cm ON cm.ChildMaterialCode = w.ChildMaterialCode
    WHERE w.BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId);

    SET @StageDetailCount = @@ROWCOUNT;

    -- 2f. 更新 ChildRequiredStageCode
    UPDATE w
    SET w.ChildRequiredStageCode = cf.StageCode
    FROM MES_APS_BOM_Workset_Realtime w
    INNER JOIN #Chain_Final cf ON cf.ChildMaterialCode = w.ChildMaterialCode
    INNER JOIN #ChainMax cm ON cm.ChildMaterialCode = w.ChildMaterialCode
    WHERE w.BOMNO = @BOMNO AND cf.StageOrd = cm.MaxOrd
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId);

    SET @EnrichedCount = @@ROWCOUNT;

    -- ================================================================
    -- Step 3: ROOT 路径（v5.0.29 Unpivot修正；v5.0.30 补WorksetId；v5.0.32 加RequestDetailId过滤）
    -- 实时SP单次处理一个BOMNO+RequestDetailId，粒度天然是RequestDetail级
    -- WorksetId取该BOMNO+RequestDetailId下Level=1的代表性锚点（MIN Id）
    -- @RequestDetailId IS NULL 时兼容旧调用（仅按BOMNO过滤）
    -- ================================================================
    ;WITH RootEdges AS (
        SELECT DISTINCT w.BOMNO,
               w.ParentMaterialCode AS RootMaterial,
               w.ChildProcRefCode   AS EdgeChildPC,
               w.ParentProcRefCode  AS EdgeParentPC,
               ROW_NUMBER() OVER (
                   PARTITION BY w.BOMNO
                   ORDER BY w.ParentMaterialCode, w.ChildMaterialCode
               ) AS EdgeOrd
        FROM MES_APS_BOM_Workset_Realtime w
        WHERE w.BOMNO = @BOMNO AND w.Level = 1
          AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
    ),
    RootUnpivot AS (
        SELECT BOMNO, RootMaterial, EdgeChildPC AS PC, EdgeOrd * 2 - 1 AS Seq
        FROM RootEdges WHERE ISNULL(EdgeChildPC, N'') <> N''
        UNION ALL
        SELECT BOMNO, RootMaterial, EdgeParentPC AS PC, EdgeOrd * 2 AS Seq
        FROM RootEdges WHERE ISNULL(EdgeParentPC, N'') <> N''
    ),
    RootWithStage AS (
        SELECT ru.BOMNO, ru.RootMaterial, ru.Seq,
               pc.StageCode, pc.FactoryCode
        FROM RootUnpivot ru
        INNER JOIN MES_ProcessCode_View pc
            ON pc.ProcessCode = RIGHT(N'000000' + ru.PC, 6)
        WHERE ISNULL(pc.StageCode, N'') <> N''
    ),
    RootDeduped AS (
        SELECT *, ROW_NUMBER() OVER (
            PARTITION BY BOMNO, StageCode ORDER BY Seq
        ) AS DupRank
        FROM RootWithStage
    ),
    RootChain AS (
        SELECT BOMNO, RootMaterial, StageCode, FactoryCode,
               ROW_NUMBER() OVER (PARTITION BY BOMNO ORDER BY Seq) AS StageOrd
        FROM RootDeduped WHERE DupRank = 1
    )
    INSERT INTO MES_APS_BOM_Workset_StageDetail_Realtime (
        WorksetId, BOMNO, StageScopeType, ParentMaterialCode, ChildMaterialCode,
        StageSeq, StageCode, IsSupplyThreshold, CreatedAt)
    SELECT
        (SELECT MIN(w2.Id) FROM MES_APS_BOM_Workset_Realtime w2
         WHERE w2.BOMNO = @BOMNO AND w2.Level = 1
           AND (@RequestDetailId IS NULL OR w2.RequestDetailId = @RequestDetailId)),
        rc.BOMNO, N'ROOT', NULL, rc.RootMaterial,
        rc.StageOrd * 10, rc.StageCode, 0, @Now
    FROM RootChain rc;

    SET @StageDetailCount = @StageDetailCount + @@ROWCOUNT;

    -- ================================================================
    -- Step 4: UNKNOWN_PROCCODE
    -- ================================================================
    INSERT INTO MES_APS_BOM_Workset_Issues
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Produce,
         IssueType, Severity, Detail, DegradeAction, CreatedAt)
    SELECT DISTINCT @SyntheticBatch, w.BOMNO, w.ParentMaterialCode, w.ChildMaterialCode,
           w.ChildSourceHintCode, N'UNKNOWN_PROCCODE', N'WARN',
           N'工序码 ChildProcRef=' + ISNULL(w.ChildProcRefCode, N'NULL')
               + N' ParentProcRef=' + ISNULL(w.ParentProcRefCode, N'NULL') + N' 查不到工序字典',
           N'STAGE_NULL', @Now
    FROM MES_APS_BOM_Workset_Realtime w
    INNER JOIN #PFM pfm ON TRY_CAST(w.ChildSourceHintCode AS TINYINT) = pfm.ProduceCode
    WHERE w.BOMNO = @BOMNO
      AND (@RequestDetailId IS NULL OR w.RequestDetailId = @RequestDetailId)
      AND pfm.SourceCategory <> N'PURCHASE'
      AND (
          (ISNULL(w.ChildProcRefCode, N'') <> N''
           AND NOT EXISTS (SELECT 1 FROM MES_ProcessCode_View pc
                           WHERE pc.ProcessCode = RIGHT(N'000000' + w.ChildProcRefCode, 6)))
          OR
          (ISNULL(w.ParentProcRefCode, N'') <> N''
           AND NOT EXISTS (SELECT 1 FROM MES_ProcessCode_View pc
                           WHERE pc.ProcessCode = RIGHT(N'000000' + w.ParentProcRefCode, 6)))
      );

    -- ================================================================
    -- v5.1.0 Step 4.5: 生成实时跨厂交接边（仅在有 RequestDetailId 时调用）
    -- 必须在 Step 5 日志之前调用，让 STAGE_DICT_NOT_FOUND 计入本次 IssueCount
    -- 旧 BOMNO 兼容路径（RequestDetailId IS NULL）不调用 Realtime CrossFactoryEdge SP
    -- ================================================================
    IF @RequestDetailId IS NOT NULL
    BEGIN
        EXEC dbo.sp_GenerateBOMCrossFactoryEdgeRealtime
            @BOMNO           = @BOMNO,
            @RequestDetailId = @RequestDetailId;
    END

    -- ================================================================
    -- Step 5: 日志
    -- ================================================================
    SET @IssueCount = (SELECT COUNT(*) FROM MES_APS_BOM_Workset_Issues WHERE BatchNo = @SyntheticBatch);

    INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
    VALUES (NULL,
        N'sp_EnrichBOMWorksetRealtime(' + @BOMNO + N') 完成: 回填 '
        + CAST(@EnrichedCount AS NVARCHAR(20))
        + N' 行; StageDetail 写入 ' + CAST(@StageDetailCount AS NVARCHAR(20))
        + N' 行(EDGE+ROOT); Issues 登记 ' + CAST(@IssueCount AS NVARCHAR(20)) + N' 条',
        @Now);

    -- Cleanup
    DROP TABLE IF EXISTS #PFM;
    DROP TABLE IF EXISTS #Candidates;
    DROP TABLE IF EXISTS #Chain_R26;
    DROP TABLE IF EXISTS #NeedFallback;
    DROP TABLE IF EXISTS #Chain_Fallback;
    DROP TABLE IF EXISTS #Chain_Final;
    DROP TABLE IF EXISTS #ChainMax;
END;
GO

-- 3.3 批次清理存储过程（v5.0.26b 修订，2026-05-15：Fix7 #ArchiveWorksetIds 修复WorksetId/Archive.Id混淆）
CREATE OR ALTER PROCEDURE sp_CleanupBOMWorkset
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now           DATETIME2 = GETDATE();
    DECLARE @Archive30Days DATETIME2 = DATEADD(DAY, -30,  @Now);
    DECLARE @Delete90Days  DATETIME2 = DATEADD(DAY, -90,  @Now);

    -- Fix7: 先将待归档 Workset 行的原始 Id 保存到临时表，
    --       后续 StageDetail 归档和删除都用此表，
    --       ⚠️ 绝不用 MES_APS_BOM_Workset_Archive.Id（那是归档后新生成的 IDENTITY，与 StageDetail.WorksetId 无关）
    CREATE TABLE #ArchiveWorksetIds (WorksetId BIGINT NOT NULL PRIMARY KEY);

    INSERT INTO #ArchiveWorksetIds (WorksetId)
    SELECT Id
    FROM MES_APS_BOM_Workset
    WHERE BatchNo IN (
        SELECT BatchNo FROM MES_API_BOM_Request
        WHERE CompletedAt < @Archive30Days AND Status = 'READY'
    );

    -- 1. 归档 Workset 行（v5.0.26：含 RequestDetailId）
    INSERT INTO MES_APS_BOM_Workset_Archive (
        BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
        Quantity, Level, Path,
        ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
        ChildRequiredStageCode, RequestDetailId, CreatedAt
    )
    SELECT BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
           Quantity, Level, Path,
           ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
           ChildRequiredStageCode, RequestDetailId, CreatedAt
    FROM MES_APS_BOM_Workset
    WHERE Id IN (SELECT WorksetId FROM #ArchiveWorksetIds);

    -- 1b. 归档 StageDetail（使用原始 WorksetId，与 StageDetail.WorksetId 精确匹配）
    --     ⚠️ StageDetail_Archive.WorksetId = 原始 Workset.Id（SourceWorksetId语义，无FK，仅追溯用）
    INSERT INTO MES_APS_BOM_Workset_StageDetail_Archive (
        BatchNo, BOMNO, StageScopeType, ParentMaterialCode,
        ChildMaterialCode, StageSeq, StageCode,
        IsSupplyThreshold, WorksetId, CreatedAt
    )
    SELECT sd.BatchNo, sd.BOMNO, sd.StageScopeType, sd.ParentMaterialCode,
           sd.ChildMaterialCode, sd.StageSeq, sd.StageCode,
           sd.IsSupplyThreshold,
           sd.WorksetId,   -- 原始 Workset.Id（透传到归档表，不改值）
           sd.CreatedAt
    FROM MES_APS_BOM_Workset_StageDetail sd
    WHERE sd.WorksetId IN (SELECT WorksetId FROM #ArchiveWorksetIds); -- ✅ 原始 Id，不是 Archive.Id

    -- 2. 删除 StageDetail（先于 Workset 删除，避免悬挂引用）
    --    ⚠️ 必须用 #ArchiveWorksetIds，而非 MES_APS_BOM_Workset_Archive.Id
    DELETE FROM MES_APS_BOM_Workset_StageDetail
    WHERE WorksetId IN (SELECT WorksetId FROM #ArchiveWorksetIds); -- ✅ 原始 Id

    -- 2b. 删除 Workset 主行
    DELETE FROM MES_APS_BOM_Workset
    WHERE Id IN (SELECT WorksetId FROM #ArchiveWorksetIds);

    DROP TABLE #ArchiveWorksetIds;

    -- 3. 删除90天前的归档（物理清除）
    DELETE FROM MES_APS_BOM_Workset_Archive          WHERE CreatedAt < @Delete90Days;
    DELETE FROM MES_APS_BOM_Workset_StageDetail_Archive WHERE CreatedAt < @Delete90Days;

    -- 4. 记录清理日志
    INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
    VALUES ('CLEANUP', '批次清理完成', @Now);
END;
GO

-- =============================================
-- 第四部分: APS本地库存储过程
-- =============================================

USE APS_Production;
GO

-- 4.1 BOM数据拉取存储过程
CREATE PROCEDURE sp_PullBOMFromODS
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    -- 1. 清空旧数据
    DELETE FROM APS_BOM_RAW WHERE BatchNo = @BatchNo;
    
    -- 2. 从ODS拉取数据（使用SqlBulkCopy在应用层执行）
    -- 此存储过程仅用于标记拉取完成
    
    -- 3. 记录日志
    INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
    VALUES (@BatchNo, 'PullBOM', 'BOM数据拉取完成', 'SUCCESS', GETDATE());
END;
GO

-- 4.2 LLC计算存储过程
CREATE PROCEDURE sp_CalculateLLC
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    -- 1. 初始化LLC为NULL
    UPDATE APS_BOM_RAW
    SET LLC = NULL,
        IsLeaf = 0
    WHERE BatchNo = @BatchNo;
    
    -- 2. 标记顶层成品（LLC = 0）
    UPDATE APS_BOM_RAW
    SET LLC = 0
    WHERE BatchNo = @BatchNo
      AND ParentMaterialCode NOT IN (
          SELECT DISTINCT ChildMaterialCode 
          FROM APS_BOM_RAW 
          WHERE BatchNo = @BatchNo
      );
    
    -- 3. 迭代计算LLC（最多10层）
    DECLARE @Level INT = 0;
    DECLARE @RowsAffected INT = 1;
    
    WHILE @RowsAffected > 0 AND @Level < 10
    BEGIN
        UPDATE child
        SET child.LLC = @Level + 1
        FROM APS_BOM_RAW child
        INNER JOIN APS_BOM_RAW parent 
            ON child.ParentMaterialCode = parent.ChildMaterialCode
           AND child.BatchNo = parent.BatchNo
        WHERE child.BatchNo = @BatchNo
          AND parent.LLC = @Level
          AND child.LLC IS NULL;
        
        SET @RowsAffected = @@ROWCOUNT;
        SET @Level = @Level + 1;
    END;
    
    -- 4. 标记叶子节点（采购件）
    UPDATE APS_BOM_RAW
    SET IsLeaf = 1
    WHERE BatchNo = @BatchNo
      AND ChildMaterialCode NOT IN (
          SELECT DISTINCT ParentMaterialCode 
          FROM APS_BOM_RAW 
          WHERE BatchNo = @BatchNo
      );
    
    -- 5. 记录日志
    INSERT INTO APS_ETL_Log (BatchNo, Step, Message, CreatedAt)
    VALUES (@BatchNo, 'CalculateLLC', 
            'LLC计算完成，最大层级: ' + CAST(@Level AS NVARCHAR(10)), 
            GETDATE());
END;
GO

-- =====================================================================
-- §4.3a sp_ResolveMaterialProductFamily
-- ⚠️ V2 预留：V1 不需要独立解析 SP
-- V1 产品族判断逻辑封装在 ERP_Master_View 内部（5号位 ODS 内部逻辑）
-- V2 若规则增多或需要定时批量解析，再实现本 SP
-- =====================================================================

-- (V1 不执行 CREATE OR ALTER PROCEDURE)

-- =====================================================================
-- 4.3 双源同构三表协同同步存储过程（v4.0 重构）（2026-04-01 更新）
-- =====================================================================
-- 重构日期：2026-04-01
-- 设计依据：双源同构契约 + 三表协同同步
-- 职责：ERP/MES 主数据 → APS 三层承接（Material + MaterialMapping + MaterialSupplyContext）
-- v4.0 核心变更：
--   1. 合并 sp_SyncERPMasterData + sp_SyncMESMaterialData 为统一参数化SP
--   2. MaterialMapping 统一为 SourceID + Warehouse（消除 ERP/MES 字段分叉）
--   3. MaterialType 由 APS 按 MaterialCode 前缀统一推导（FG/RAW/WIP/ASSY/UNKNOWN）
--   4. MES 也执行三表协同（不再区分"ERP三表、MES两表"）
--   5. 新增 InventoryManagementMode 字段同步
--   6. 双源视图契约同构：ERP和MES视图字段完全一致
-- v5.0.38 修订（2026-05-30 V1口径修正）：
--   7. 步骤0快照新增 IsProductFamilyRequired / ProductFamilyCode / FamilyResolveStatus
--      （来自 ERP_Master_View v1.5 / MES_Material_View v1.5 升级同步）
--   8. 步骤1c 四规则映射写入 Material.ProductFamilyId：
--      规则1: IsProductFamilyRequired=0 或 FamilyResolveStatus='NOT_REQUIRED' → 清空为 NULL
--      规则2: IsProductFamilyRequired=1 且 FamilyResolveStatus='RESOLVED' → ProductFamilyCode→ProductFamily.Id
--      规则3: FamilyResolveStatus IN ('NO_RULE','AMBIGUOUS','SOURCE_FIELD_MISSING') → 不写，登记 ETL Issue
--      观则4: FamilyResolveStatus='FAMILY_CODE_NOT_FOUND' → 不写，登记 ETL Issue（提示维护字典）
-- 调用示例：
--   EXEC sp_SyncMasterData @SourceType='ERP', @BatchNo='DAILY', @RowsAffected OUTPUT, @ErrorMessage OUTPUT;
--   EXEC sp_SyncMasterData @SourceType='MES', @BatchNo='DAILY', @RowsAffected OUTPUT, @ErrorMessage OUTPUT;
-- =====================================================================

CREATE OR ALTER PROCEDURE sp_SyncMasterData
    @SourceType NVARCHAR(20),                -- 'ERP' 或 'MES'
    @BatchNo NVARCHAR(50) = 'DAILY',
    @RowsAffected INT OUTPUT,
    @ErrorMessage NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @SyncTime DATETIME2 = GETDATE();
    DECLARE @StartTime DATETIME2 = @SyncTime;
    DECLARE @StepName NVARCHAR(100);

    -- 计数器
    DECLARE @Material_New INT = 0, @Material_Updated INT = 0, @Material_Deactivated INT = 0;
    DECLARE @Mapping_New INT = 0, @Mapping_Closed INT = 0, @Mapping_SCD2 INT = 0;
    DECLARE @Supply_New INT = 0, @Supply_Closed INT = 0, @Supply_Updated INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- =================================================================
        -- 步骤0：提取源端当前有效快照到临时表
        -- 双源同构契约：ERP和MES视图字段完全一致，仅FROM不同
        -- 粒度：MaterialCode + Warehouse（一物一仓一行）
        -- =================================================================
        SET @StepName = N'步骤0-提取' + @SourceType + N'快照';

        DROP TABLE IF EXISTS #Source_Snapshot;

        IF @SourceType = 'ERP'
        BEGIN
            SELECT 
                MaterialCode, MaterialName, Spec,
                MasterID AS SourceID,
                Warehouse, ISNULL(Warehouse, 'N/A') AS Warehouse_Norm,
                SupplyMode, ProductionDeptCode,
                UOM, LeadTimeDays, SafetyStock,
                InventoryManagementMode, IsActive,
                IsProductFamilyRequired,     -- v5.0.38：是否需要产品族（5号位 ODS 判断）
                ProductFamilyCode,           -- v5.0.38：ODS 解析出的产品族编码
                FamilyResolveStatus          -- v5.0.38：解析状态
            INTO #Source_Snapshot
            FROM ext_ERP_Master_View
            WHERE IsActive = 1;
        END
        ELSE IF @SourceType = 'MES'
        BEGIN
            SELECT 
                MaterialCode, MaterialName, Spec,
                MasterID AS SourceID,
                Warehouse, ISNULL(Warehouse, 'N/A') AS Warehouse_Norm,
                SupplyMode, ProductionDeptCode,
                UOM, LeadTimeDays, SafetyStock,
                InventoryManagementMode, IsActive,
                IsProductFamilyRequired,     -- v5.0.38：是否需要产品族（MES 侧 V1 固定为 0）
                ProductFamilyCode,           -- v5.0.38：ODS 解析出的产品族编码
                FamilyResolveStatus          -- v5.0.38：解析状态
            INTO #Source_Snapshot
            FROM ext_MES_Material_View
            WHERE IsActive = 1;
        END

        -- 消歧：同一 MaterialCode + Warehouse 有多行时，取 SourceID 最大的
        ;WITH Ranked AS (
            SELECT *,
                   ROW_NUMBER() OVER (
                       PARTITION BY MaterialCode, Warehouse_Norm 
                       ORDER BY SourceID DESC
                   ) AS RowNum
            FROM #Source_Snapshot
        )
        DELETE FROM Ranked WHERE RowNum > 1;

        -- =================================================================
        -- 步骤1：同步 Material（物料主身份）
        -- 规则：仅按 MaterialCode 维护，不因仓库不同拆分
        -- MaterialType 由 APS 按 MaterialCode 前缀统一推导
        -- =================================================================
        SET @StepName = N'步骤1-同步Material';

        -- 1a. 按 MaterialCode 去重取物料本体属性
        DROP TABLE IF EXISTS #Material_Source;

        ;WITH MaterialDedup AS (
            SELECT 
                MaterialCode, MaterialName, Spec, UOM,
                ROW_NUMBER() OVER (
                    PARTITION BY MaterialCode 
                    ORDER BY Warehouse_Norm
                ) AS RowNum
            FROM #Source_Snapshot
        )
        SELECT MaterialCode, MaterialName, Spec, UOM
        INTO #Material_Source
        FROM MaterialDedup
        WHERE RowNum = 1;

        -- 1b. MERGE Material 表
        MERGE INTO Material AS target
        USING #Material_Source AS source
        ON target.MaterialCode = source.MaterialCode

        -- 已存在且本体属性有变化 → 更新
        WHEN MATCHED AND (
            target.MaterialName <> source.MaterialName
            OR ISNULL(target.Spec, '') <> ISNULL(source.Spec, '')
            OR target.UOM <> source.UOM
            OR target.IsActive = 0
        )
        THEN UPDATE SET 
            target.MaterialName = source.MaterialName,
            target.Spec = source.Spec,
            target.UOM = source.UOM,
            target.IsActive = 1,
            target.UpdatedAt = @SyncTime

        -- 全新物料 → 插入，MaterialType 由 APS 按前缀推导
        WHEN NOT MATCHED BY TARGET
        THEN INSERT (MaterialCode, MaterialName, Spec, MaterialType, UOM, 
                     IsActive, CreatedAt, UpdatedAt)
             VALUES (source.MaterialCode, source.MaterialName, source.Spec,
                     CASE 
                         WHEN source.MaterialCode LIKE 'FG-%' THEN 'FINISHED_GOOD'
                         WHEN source.MaterialCode LIKE 'RAW-%' THEN 'RAW_MATERIAL'
                         WHEN source.MaterialCode LIKE 'WIP-%' THEN 'SEMI_FINISHED'
                         WHEN source.MaterialCode LIKE 'ASSY-%' THEN 'ASSY'
                         ELSE 'UNKNOWN'
                     END,
                     source.UOM, 1, @SyncTime, @SyncTime);

        SET @Material_New = @@ROWCOUNT;

        -- =================================================================
        -- 步骤1c：ProductFamilyCode → ProductFamilyId 映射写入 Material（v5.0.38 四规则）
        -- 原则：APS 层只做码表映射，不保存 ERP ProcessCode / ModelSort 等原始字段
        -- 规则1: IsProductFamilyRequired=0 或 FamilyResolveStatus='NOT_REQUIRED' → 清空为 NULL
        -- 规则2: IsProductFamilyRequired=1 且 'RESOLVED' → ProductFamilyCode→ProductFamily.Code→ProductFamilyId
        -- 规则3: 'NO_RULE'/'AMBIGUOUS'/'SOURCE_FIELD_MISSING' → 不写，登记 ETL Issue
        -- 观则4: 'FAMILY_CODE_NOT_FOUND' → 不写，登记 ETL Issue（提示维护 ProductFamily 字典）
        -- =================================================================
        SET @StepName = N'步骤1c-ProductFamilyId映射';

        -- 规则2：RESOLVED → 码表映射（INNER JOIN 确保 ProductFamily.Code 存在）
        UPDATE m
        SET m.ProductFamilyId = pf.Id,
            m.UpdatedAt       = @SyncTime
        FROM Material m
        INNER JOIN (
            SELECT DISTINCT MaterialCode, ProductFamilyCode
            FROM #Source_Snapshot
            WHERE IsProductFamilyRequired = 1
              AND FamilyResolveStatus = N'RESOLVED'
              AND ProductFamilyCode IS NOT NULL
        ) AS src ON src.MaterialCode = m.MaterialCode
        INNER JOIN ProductFamily pf
            ON pf.Code = src.ProductFamilyCode
           AND pf.IsActive = 1
        WHERE ISNULL(m.ProductFamilyId, -1) <> pf.Id;

        -- 规则1：NOT_REQUIRED 或 IsProductFamilyRequired=0 → 清空 ProductFamilyId（如有残留値）
        UPDATE m
        SET m.ProductFamilyId = NULL,
            m.UpdatedAt       = @SyncTime
        FROM Material m
        INNER JOIN (
            SELECT DISTINCT MaterialCode
            FROM #Source_Snapshot
            WHERE IsProductFamilyRequired = 0
               OR FamilyResolveStatus = N'NOT_REQUIRED'
        ) AS src ON src.MaterialCode = m.MaterialCode
        WHERE m.ProductFamilyId IS NOT NULL;

        -- 规则3+4：解析异常 + RESOLVED 但字典缺失 → 登记 ETL Issue（不写 ProductFamilyId）
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, CreatedAt)
        SELECT DISTINCT
            @BatchNo,
            N'步骤1c-产品族解析异常',
            N'[' + src.FamilyResolveStatus + '] MaterialCode=' + src.MaterialCode
            + CASE WHEN src.ProductFamilyCode IS NOT NULL
                   THEN N' ProductFamilyCode=' + src.ProductFamilyCode
                   ELSE N'' END
            + CASE src.FamilyResolveStatus
                WHEN N'FAMILY_CODE_NOT_FOUND'
                THEN N' → 请维护 ProductFamily.Code=' + ISNULL(src.ProductFamilyCode, 'NULL') + N'（字典缺失）'
                WHEN N'RESOLVED'
                THEN N' → RESOLVED 但 ProductFamily.Code 在字典中未找到，请维护字典'
                ELSE N''
              END,
            @SyncTime
        FROM #Source_Snapshot src
        WHERE src.IsProductFamilyRequired = 1
          AND (
              src.FamilyResolveStatus IN (N'NO_RULE', N'AMBIGUOUS', N'SOURCE_FIELD_MISSING', N'FAMILY_CODE_NOT_FOUND')
              OR (
                  src.FamilyResolveStatus = N'RESOLVED'
                  AND src.ProductFamilyCode IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM ProductFamily pf
                      WHERE pf.Code = src.ProductFamilyCode AND pf.IsActive = 1
                  )
              )
          );

        -- =================================================================
        -- 步骤2：同步 MaterialMapping（来源映射桥表，SCD Type 2）
        -- 业务键：MaterialCode + Source + Warehouse_Norm
        -- 追踪字段：SourceID（变更时开新版本）
        -- =================================================================
        SET @StepName = N'步骤2-同步MaterialMapping';

        -- 2a. MERGE — 处理匹配/新增/源端消失
        MERGE INTO MaterialMapping AS target
        USING (
            SELECT MaterialCode, SourceID, Warehouse, Warehouse_Norm
            FROM #Source_Snapshot
        ) AS source
        ON target.MaterialCode = source.MaterialCode 
           AND target.Source = @SourceType 
           AND target.IsCurrent = 1
           AND target.Warehouse_Norm = source.Warehouse_Norm

        -- 同物料+同仓库+同SourceID → 仅刷新时间戳
        WHEN MATCHED AND target.SourceID = source.SourceID
        THEN UPDATE SET target.UpdatedAt = @SyncTime

        -- 同物料+同仓库，SourceID变化 → 关闭旧记录（后续插入新版本）
        WHEN MATCHED AND (target.SourceID <> source.SourceID 
                          OR target.SourceID IS NULL AND source.SourceID IS NOT NULL
                          OR target.SourceID IS NOT NULL AND source.SourceID IS NULL)
        THEN UPDATE SET 
            target.ValidTo = @SyncTime, 
            target.IsCurrent = 0, 
            target.UpdatedAt = @SyncTime

        -- 全新物料或新仓库 → 插入新记录
        WHEN NOT MATCHED BY TARGET 
        THEN INSERT (MaterialCode, SourceID, Warehouse, Source, 
                     ValidFrom, ValidTo, IsCurrent, CreatedAt, UpdatedAt)
             VALUES (source.MaterialCode, source.SourceID, source.Warehouse,
                     @SourceType, @SyncTime, NULL, 1, @SyncTime, @SyncTime)

        -- 源端消失 → 关闭该仓库映射
        WHEN NOT MATCHED BY SOURCE 
             AND target.Source = @SourceType 
             AND target.IsCurrent = 1
        THEN UPDATE SET 
            target.ValidTo = @SyncTime, 
            target.IsCurrent = 0, 
            target.UpdatedAt = @SyncTime;

        SET @Mapping_New = @@ROWCOUNT;

        -- 2b. SCD Type 2闭环：为因SourceID变化被关闭的旧记录插入新版本
        INSERT INTO MaterialMapping (
            MaterialCode, SourceID, Warehouse, Source,
            ValidFrom, ValidTo, IsCurrent, CreatedAt, UpdatedAt
        )
        SELECT 
            source.MaterialCode, source.SourceID, source.Warehouse,
            @SourceType, @SyncTime, NULL, 1, @SyncTime, @SyncTime
        FROM #Source_Snapshot AS source
        WHERE EXISTS (
            SELECT 1 FROM MaterialMapping AS old
            WHERE old.MaterialCode = source.MaterialCode
              AND old.Source = @SourceType
              AND old.Warehouse_Norm = source.Warehouse_Norm
              AND old.IsCurrent = 0
              AND old.ValidTo = @SyncTime
              AND old.SourceID <> source.SourceID
        );

        SET @Mapping_SCD2 = @@ROWCOUNT;

        -- 2c. 全部仓库映射都被关闭的物料 → Material 标记 IsActive = 0
        UPDATE Material
        SET IsActive = 0, UpdatedAt = @SyncTime
        WHERE IsActive = 1
          AND NOT EXISTS (
              SELECT 1 FROM MaterialMapping mm
              WHERE mm.MaterialCode = Material.MaterialCode
                AND mm.IsCurrent = 1
          );

        SET @Material_Deactivated = @@ROWCOUNT;

        -- =================================================================
        -- 步骤3：同步 MaterialSupplyContext（仓库级供给上下文，SCD Type 2）
        -- 业务键：MaterialCode + WarehouseCode
        -- 追踪字段：SupplyMode, DefaultProductionDeptCode, LeadTimeDays,
        --           SafetyStock, InventoryManagementMode
        -- =================================================================
        SET @StepName = N'步骤3-同步MaterialSupplyContext';

        -- 3a. 准备仓库级供给属性源数据（仅已有当前映射的物料-仓库组合）
        DROP TABLE IF EXISTS #Supply_Source;

        SELECT 
            snap.MaterialCode,
            snap.Warehouse AS WarehouseCode,
            snap.SupplyMode,
            snap.ProductionDeptCode AS DefaultProductionDeptCode,
            snap.LeadTimeDays,
            snap.SafetyStock,
            snap.InventoryManagementMode
        INTO #Supply_Source
        FROM #Source_Snapshot snap
        INNER JOIN MaterialMapping mm
            ON mm.MaterialCode = snap.MaterialCode
            AND mm.Source = @SourceType
            AND mm.Warehouse_Norm = snap.Warehouse_Norm
            AND mm.IsCurrent = 1;

        -- 3b. 供给属性变化 → 关闭旧版本（SCD Type 2）
        UPDATE ctx
        SET ctx.ValidTo = @SyncTime,
            ctx.IsCurrent = 0,
            ctx.UpdatedAt = @SyncTime
        FROM MaterialSupplyContext ctx
        INNER JOIN #Supply_Source src
            ON ctx.MaterialCode = src.MaterialCode
            AND ctx.WarehouseCode = src.WarehouseCode
        WHERE ctx.IsCurrent = 1
          AND ctx.SourceSystem = @SourceType
          AND (
              ISNULL(ctx.SupplyMode, '') <> ISNULL(src.SupplyMode, '')
              OR ISNULL(ctx.DefaultProductionDeptCode, '') <> ISNULL(src.DefaultProductionDeptCode, '')
              OR ISNULL(ctx.LeadTimeDays, -1) <> ISNULL(src.LeadTimeDays, -1)
              OR ISNULL(ctx.SafetyStock, -1) <> ISNULL(src.SafetyStock, -1)
              OR ISNULL(ctx.InventoryManagementMode, '') <> ISNULL(src.InventoryManagementMode, '')
          );

        SET @Supply_Updated = @@ROWCOUNT;

        -- 3c. 属性变化的记录 + 全新记录 → 插入新版本
        INSERT INTO MaterialSupplyContext (
            MaterialCode, WarehouseCode, SupplyMode, 
            DefaultProductionDeptCode, LeadTimeDays, SafetyStock,
            InventoryManagementMode, SourceSystem,
            ValidFrom, ValidTo, IsCurrent, CreatedAt, UpdatedAt
        )
        SELECT 
            src.MaterialCode, src.WarehouseCode, src.SupplyMode,
            src.DefaultProductionDeptCode, src.LeadTimeDays, src.SafetyStock,
            src.InventoryManagementMode, @SourceType,
            @SyncTime, NULL, 1, @SyncTime, @SyncTime
        FROM #Supply_Source src
        WHERE NOT EXISTS (
            SELECT 1 FROM MaterialSupplyContext ctx
            WHERE ctx.MaterialCode = src.MaterialCode
              AND ctx.WarehouseCode = src.WarehouseCode
              AND ctx.IsCurrent = 1
              AND ctx.SourceSystem = @SourceType
        );

        SET @Supply_New = @@ROWCOUNT;

        -- 3d. 源端消失的仓库 → 关闭对应的 MaterialSupplyContext
        UPDATE ctx
        SET ctx.ValidTo = @SyncTime,
            ctx.IsCurrent = 0,
            ctx.UpdatedAt = @SyncTime
        FROM MaterialSupplyContext ctx
        WHERE ctx.IsCurrent = 1
          AND ctx.SourceSystem = @SourceType
          AND NOT EXISTS (
              SELECT 1 FROM #Supply_Source src
              WHERE src.MaterialCode = ctx.MaterialCode
                AND src.WarehouseCode = ctx.WarehouseCode
          );

        SET @Supply_Closed = @@ROWCOUNT;

        -- =================================================================
        -- 步骤4：记录 ETL 日志
        -- =================================================================
        SET @StepName = N'步骤4-记录日志';

        SET @RowsAffected = @Material_New + @Mapping_New + @Mapping_SCD2 
                           + @Supply_New + @Supply_Updated + @Supply_Closed;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (
            @BatchNo, 
            'sp_SyncMasterData[' + @SourceType + ']', 
            N'三表同步完成[' + @SourceType + N'] | ' +
            N'Material(新增/更新:' + CAST(@Material_New AS NVARCHAR(10)) + 
            N',停用:' + CAST(@Material_Deactivated AS NVARCHAR(10)) + N') | ' +
            N'Mapping(变更:' + CAST(@Mapping_New AS NVARCHAR(10)) + 
            N',SCD2新增:' + CAST(@Mapping_SCD2 AS NVARCHAR(10)) + N') | ' +
            N'SupplyCtx(新增:' + CAST(@Supply_New AS NVARCHAR(10)) + 
            N',属性变更:' + CAST(@Supply_Updated AS NVARCHAR(10)) + 
            N',仓库关闭:' + CAST(@Supply_Closed AS NVARCHAR(10)) + N')', 
            N'SUCCESS', 
            GETDATE()
        );

        -- 清理临时表
        DROP TABLE IF EXISTS #Source_Snapshot;
        DROP TABLE IF EXISTS #Material_Source;
        DROP TABLE IF EXISTS #Supply_Source;

        COMMIT TRANSACTION;
        SET @ErrorMessage = NULL;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        SET @ErrorMessage = ERROR_MESSAGE();
        SET @RowsAffected = 0;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (
            @BatchNo, 
            @StepName, 
            N'同步失败[' + @SourceType + N']: ' + @ErrorMessage, 
            N'FAILED', 
            GETDATE()
        );
    END CATCH
END;
GO

-- =====================================================================
-- 4.3b 资源主数据同步存储过程（v5.0.13 新增，v5.0.16 部门维度升级）
-- ⚠️ 占位 SP：v1 仅实现 'MES' 分支，'EAM' 分支预留 NOT_IMPLEMENTED
-- ⚠️ 与 sp_SyncMasterData(@SourceType) 同构：双源视图契约一致时 SP 逻辑零分叉
-- 数据流：
--   @SourceType='MES' → ext_MES_APS_Resource_View → Resource（全量刷新 MERGE）
--   @SourceType='EAM' → ext_EAM_APS_Resource_View → Resource（v1 未实现）
-- 调用时机：每天 00:10（与 sp_SyncMasterData 同窗口并行；Resource 变化频率低，全量刷新，不做增量）
-- 双字典映射（v5.0.16 升级）：
--   FactoryCode             → Factory.Id              （查不到的行登记 APS_ETL_Log 并跳过）
--   ProductionDeptCode      → ProductionDepartment.Id（查不到的行登记 APS_ETL_Log 并跳过）
--   v5.0.16 删除：WorkshopCode 字段映射（业务确认 MES 也无此概念）
-- 调用示例：
--   EXEC sp_SyncResourceData @SourceType='MES', @BatchNo='DAILY', @RowsAffected OUTPUT, @ErrorMessage OUTPUT;
-- =====================================================================
CREATE OR ALTER PROCEDURE sp_SyncResourceData
    @SourceType    NVARCHAR(20),                  -- 'MES'（v1） / 'EAM'（v1 未实现）
    @BatchNo       NVARCHAR(50) = 'DAILY',
    @RowsAffected  INT OUTPUT,
    @ErrorMessage  NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StepName NVARCHAR(100) = N'sp_SyncResourceData[' + @SourceType + N']';
    DECLARE @Inserted INT = 0, @Updated INT = 0, @Deactivated INT = 0, @Skipped INT = 0;

    BEGIN TRY
        -- 参数校验
        IF @SourceType NOT IN (N'MES', N'EAM')
        BEGIN
            SET @ErrorMessage = N'Invalid @SourceType: ' + ISNULL(@SourceType, N'NULL') + N'. Expected MES or EAM.';
            SET @RowsAffected = 0;
            INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
            VALUES (@BatchNo, @StepName, @ErrorMessage, N'FAILED', GETDATE());
            RETURN;
        END

        -- v1 占位：EAM 分支未实现
        IF @SourceType = N'EAM'
        BEGIN
            SET @ErrorMessage = N'NOT_IMPLEMENTED: EAM branch reserved; create ext_EAM_APS_Resource_View first and extend this SP.';
            SET @RowsAffected = 0;
            INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
            VALUES (@BatchNo, @StepName, @ErrorMessage, N'SKIPPED', GETDATE());
            RETURN;
        END

        BEGIN TRANSACTION;

        -- ============================================================
        -- MES 分支：从 ext_MES_APS_Resource_View 拉源快照（含 FactoryCode → FactoryId 映射）
        -- ============================================================
        IF OBJECT_ID('tempdb..#Resource_Source') IS NOT NULL DROP TABLE #Resource_Source;

        -- v5.0.16 双字典 LEFT JOIN：FactoryCode → Factory.Id；ProductionDeptCode → ProductionDepartment.Id
        SELECT
            v.ResourceCode,
            v.ResourceName,
            v.ExternalResourceId,
            v.SourceSystem,
            f.Id                AS FactoryId,
            v.FactoryCode       AS SourceFactoryCode,
            d.Id                AS ProductionDepartmentId,        -- 🆕 v5.0.16
            v.ProductionDeptCode AS SourceProductionDeptCode,     -- 🆕 v5.0.16（替代 WorkshopCode）
            v.ResourceType,
            v.Status,
            v.CapacityFactor,
            v.IsActive
        INTO #Resource_Source
        FROM ext_MES_APS_Resource_View v
        LEFT JOIN Factory f              ON f.Code = v.FactoryCode
        LEFT JOIN ProductionDepartment d ON d.DeptCode    = v.ProductionDeptCode AND d.IsActive = 1;  -- 🆕 v5.0.16

        -- 登记 FactoryCode 映射失败行（不阻塞批次）
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        SELECT
            @BatchNo, @StepName,
            N'FactoryCode not found in Factory table, row skipped. ResourceCode=' + s.ResourceCode
                + N', FactoryCode=' + ISNULL(s.SourceFactoryCode, N'NULL'),
            N'WARN', GETDATE()
        FROM #Resource_Source s
        WHERE s.FactoryId IS NULL;

        SET @Skipped = @@ROWCOUNT;

        -- 🆕 v5.0.16 登记 ProductionDeptCode 映射失败行（不阻塞批次）
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        SELECT
            @BatchNo, @StepName,
            N'ProductionDeptCode not found in ProductionDepartment dict, row skipped. ResourceCode=' + s.ResourceCode
                + N', ProductionDeptCode=' + ISNULL(s.SourceProductionDeptCode, N'NULL'),
            N'WARN', GETDATE()
        FROM #Resource_Source s
        WHERE s.FactoryId IS NOT NULL AND s.ProductionDepartmentId IS NULL;

        SET @Skipped = @Skipped + @@ROWCOUNT;

        -- ============================================================
        -- MERGE：新增/更新/停用（只处理 FactoryId + ProductionDepartmentId 双映射成功的行）
        -- ============================================================
        MERGE Resource AS tgt
        USING (SELECT * FROM #Resource_Source WHERE FactoryId IS NOT NULL AND ProductionDepartmentId IS NOT NULL) AS src
           ON tgt.ResourceCode = src.ResourceCode
        WHEN MATCHED AND (
                ISNULL(tgt.ResourceName, N'')                <> ISNULL(src.ResourceName, N'')
             OR ISNULL(tgt.ExternalResourceId, N'')          <> ISNULL(src.ExternalResourceId, N'')
             OR ISNULL(tgt.SourceSystem, N'')                <> ISNULL(src.SourceSystem, N'')
             OR tgt.FactoryId                                 <> src.FactoryId
             OR tgt.ProductionDepartmentId                    <> src.ProductionDepartmentId               -- 🆕 v5.0.16
             OR ISNULL(tgt.SourceProductionDeptCode, N'')    <> ISNULL(src.SourceProductionDeptCode, N'') -- 🆕 v5.0.16
             OR ISNULL(tgt.ResourceType, N'')                <> ISNULL(src.ResourceType, N'')
             OR ISNULL(tgt.Status, N'')                      <> ISNULL(src.Status, N'')
             OR ISNULL(tgt.CapacityFactor, 0)                <> ISNULL(src.CapacityFactor, 0)
             OR tgt.IsActive                                  <> src.IsActive
        ) THEN UPDATE SET
            ResourceName             = src.ResourceName,
            ExternalResourceId       = src.ExternalResourceId,
            SourceSystem             = src.SourceSystem,
            FactoryId                = src.FactoryId,
            ProductionDepartmentId   = src.ProductionDepartmentId,                                       -- 🆕 v5.0.16
            SourceProductionDeptCode = src.SourceProductionDeptCode,                                     -- 🆕 v5.0.16
            ResourceType             = src.ResourceType,
            Status                   = src.Status,
            CapacityFactor           = src.CapacityFactor,
            IsActive                 = src.IsActive,
            UpdatedAt                = GETDATE()
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (ResourceCode, ResourceName, ExternalResourceId, SourceSystem, FactoryId,
                    ProductionDepartmentId, SourceProductionDeptCode,                                    -- 🆕 v5.0.16
                    ResourceType, Status, CapacityFactor, IsActive, CreatedAt, UpdatedAt)
            VALUES (src.ResourceCode, src.ResourceName, src.ExternalResourceId, src.SourceSystem, src.FactoryId,
                    src.ProductionDepartmentId, src.SourceProductionDeptCode,                            -- 🆕 v5.0.16
                    src.ResourceType, src.Status, src.CapacityFactor, src.IsActive, GETDATE(), GETDATE())
        -- ⚠️ v1 占位策略：源端没有的旧资源暂**不**自动停用（避免误删），交由 2 号位审阅后手工处置
        -- 未来若改为"源为权威"，在此补：WHEN NOT MATCHED BY SOURCE AND tgt.SourceSystem = @SourceType THEN UPDATE SET IsActive=0
        ;

        SET @RowsAffected = @@ROWCOUNT;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (
            @BatchNo, @StepName,
            N'资源同步完成[' + @SourceType + N'] | 影响行数=' + CAST(@RowsAffected AS NVARCHAR(10))
                + N' | 跳过(FactoryCode/ProductionDeptCode 任一未命中)=' + CAST(@Skipped AS NVARCHAR(10)),
            N'SUCCESS', GETDATE()
        );

        DROP TABLE IF EXISTS #Resource_Source;
        COMMIT TRANSACTION;
        SET @ErrorMessage = NULL;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        SET @RowsAffected = 0;
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (@BatchNo, @StepName, N'同步失败[' + @SourceType + N']: ' + @ErrorMessage, N'FAILED', GETDATE());
    END CATCH
END;
GO

-- =====================================================================
-- 4.3c MaterialStageDeptContext 重建存储过程（v5.0.16 新增）
-- =====================================================================
-- ⚠️ 占位 SP（v1 骨架）：本版本仅勾勒接口与日志契约，实际推导逻辑标记 TODO
-- 业务定位：2 号位组装"物料×阶段→默认生产部门"的最终结果，供 1 号位排程消费
-- 输入来源：
--   1) MaterialSupplyContext (IsCurrent=1) ：仓库级原始上下文，含 DefaultProductionDepartmentId / DefaultProductionDeptCode
--   2) MES_ProcessCode_View.StageCode      ：ProcessCode → StageCode 共享基础映射（与 5 号位 sp_EnrichBOMWorkset 共用）
--   3) ProductionDepartment                ：APS 部门字典（含 StageCode 单值归属规则）
--   4) MaterialStageDeptOverride (IsCurrent=1) ：人工维护补丁；优先级高于自动草稿
--   5) MTS（如可用）                        ：一致性校验信号；不参与主推导（详见 Step 4）
-- 触发模式（@TriggerMode）：
--   FULL    ：每日定时全量重建（如 02:30）
--   INCR    ：MSC 同步后增量重建（仅处理 MSC 在本批次变更的 (MaterialId, StageCode)）
--   PARTIAL ：人工 Override 提交后局部重建（@TargetMaterialIds + @TargetStageCodes 二维过滤）
-- 降级原则（v5.0.16 红线）：批次永不阻塞；冲突/缺失登记到 MaterialStageDeptContext_Issues，旧 IsCurrent=1 记录不动；待人工修正 Override 后局部重建切换新版本
-- 步骤：
--   Step 1: 同步原始基础数据（确保 MSC/Override/字典都最新）
--   Step 2: 按 MSC 自动归一化推导 (Material/Model + StageCode + ProductionDept) 草稿；多解登记 MULTI_DEPT_CONFLICT_FOR_STAGE
--   Step 3: 应用人工 Override；记录 SourceType=MANUAL/MIXED；Model 1:N 拒收登记 OVERRIDE_MODEL_AMBIGUOUS
--   Step 4: 与 MTS 做一致性校验；不一致登记 MTS_INCONSISTENT，但不改主结果
--   Step 5: 写入 MaterialStageDeptContext（SCD Type 2）：旧 IsCurrent=1 失效化、新版本 IsCurrent=1
--   Step 6: 重建 BatchNo 写回 LastRebuildBatchNo；登记 APS_ETL_Log 汇总
-- 调用示例：
--   EXEC sp_RebuildMaterialStageDeptContext @TriggerMode='FULL', @BatchNo='DAILY-20260429', @RowsAffected OUTPUT, @ErrorMessage OUTPUT;
-- =====================================================================
CREATE OR ALTER PROCEDURE sp_RebuildMaterialStageDeptContext
    @TriggerMode         NVARCHAR(20),                    -- 'FULL' / 'INCR' / 'PARTIAL'
    @BatchNo             NVARCHAR(50) = 'DAILY',
    @TargetMaterialIds   NVARCHAR(MAX) = NULL,            -- 仅 PARTIAL：物料 ID 列表（CSV 或 JSON，由实现选择）
    @TargetStageCodes    NVARCHAR(MAX) = NULL,            -- 仅 PARTIAL：阶段码列表
    @RowsAffected        INT OUTPUT,
    @ErrorMessage        NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StepName NVARCHAR(100) = N'sp_RebuildMaterialStageDeptContext[' + @TriggerMode + N']';

    BEGIN TRY
        -- 参数校验
        IF @TriggerMode NOT IN (N'FULL', N'INCR', N'PARTIAL')
        BEGIN
            SET @ErrorMessage = N'Invalid @TriggerMode: ' + ISNULL(@TriggerMode, N'NULL') + N'. Expected FULL / INCR / PARTIAL.';
            SET @RowsAffected = 0;
            INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
            VALUES (@BatchNo, @StepName, @ErrorMessage, N'FAILED', GETDATE());
            RETURN;
        END

        -- v1 占位：完整实现待立项
        -- TODO Step 1：同步原始基础数据
        -- TODO Step 2：MSC 自动归一化（含 ProcessCode→StageCode 共享映射）
        -- TODO Step 3：应用人工 Override（含 Model 1:N 拒收登记）
        -- TODO Step 4：MTS 一致性校验
        -- TODO Step 5：SCD Type 2 写入 MaterialStageDeptContext（旧值不动、冲突时仅登记 Issues）
        -- TODO Step 6：日志汇总

        SET @RowsAffected = 0;
        SET @ErrorMessage = NULL;

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (
            @BatchNo, @StepName,
            N'v1 占位 SP：实际推导逻辑待立项实现 (TODO Step 1-6)',
            N'INFO', GETDATE()
        );
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = ERROR_MESSAGE();
        SET @RowsAffected = 0;
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (@BatchNo, @StepName, N'Context 重建失败: ' + @ErrorMessage, N'FAILED', GETDATE());
    END CATCH
END;
GO

-- 4.4a 订单验证与提升存储过程（v5.0.27 全量重写，2026-05-16）
-- ⚠️ 业务用途：将ERP_Order_Staging中PENDING记录校验、标准化、派生后提升到Order_Canonical
-- 调用时机：白天每小时增量同步后 / 凌晨全量同步后
-- 状态机：PENDING → VALIDATED → PROCESSED（成功）/ PENDING → FAILED（校验失败）
-- ⚠️ v5.0.27重构要点：
--   - #TargetStagingIds 锁定本批次 ID，防并发误操作；禁止裸 WHERE SyncStatus 操作全表
--   - MaterialCode 三级解析链：SourceMasterID→MaterialMapping / Model→MaterialMapping(SourceModel) / EmergencyOverride
--   - OrderType 未知值 → FAILED + ORDER_TYPE_UNKNOWN；禁止 ERP 原始值写入 Canonical
--   - SourceModel 一对多 → FAILED + MATERIAL_MAPPING_AMBIGUOUS
--   - BOMNO=NULL 非阻断：FailureCode=BOMNO_MISSING（最高优先级诊断，不互覆硬失败）
--   - CustomerSegment 口径：CustomerCode为空→NULL；CustomerCode有值但无匹配→UNKNOWN（不默认OVERSEAS）+ CUSTOMER_SEGMENT_UNKNOWN 追加 ErrorMessage
--   - DemandMaturityStatus V1 严格 NULL：禁止从任何字段临时推断
--   - FactoryCode V1 允许 NULL 进入 Canonical（TODO 桩）
--   - NonStockShipmentType / OriginalOrderSource inline CASE 标准化写入 Canonical
--   - @OnlyPending 参数删除（V1 只处理 PENDING）
CREATE OR ALTER PROCEDURE [dbo].[sp_ValidateAndPromoteOrders]
    @PromotedCount        INT          = 0 OUTPUT,
    @FailedCount          INT          = 0 OUTPUT,
    @BOMNOMissingCount    INT          = 0 OUTPUT,
    @MaxRows              INT          = NULL   -- NULL=不限；分批/限流用
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ProcessTime    DATETIME2    = GETDATE();
    DECLARE @BatchNo        NVARCHAR(50) = FORMAT(@ProcessTime, 'yyyyMMdd_HHmmss');
    DECLARE @ValidatedCount INT          = 0;
    DECLARE @ErrorMessage   NVARCHAR(MAX);

    SET @FailedCount       = 0;
    SET @PromotedCount     = 0;
    SET @BOMNOMissingCount = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- =================================================================
        -- PHASE 0: 锁定本次处理 ID 集合 → #TargetStagingIds
        -- ⚠️ 所有后续步骤必须 JOIN 此表；禁止裸 WHERE SyncStatus=... 操作全表
        -- ⚠️ UPDLOCK+ROWLOCK：防止并发 SP 实例同时处理相同行
        -- =================================================================
        CREATE TABLE #TargetStagingIds (StagingId BIGINT PRIMARY KEY);

        INSERT INTO #TargetStagingIds (StagingId)
        SELECT TOP (ISNULL(@MaxRows, 2147483647)) Id
        FROM ERP_Order_Staging WITH (UPDLOCK, ROWLOCK)
        WHERE SyncStatus = 'PENDING'
        ORDER BY SyncedAt ASC;   -- FIFO

        -- =================================================================
        -- STEP 0: MaterialCode 三级解析链（均仅填 NULL，不覆盖已有值）
        -- =================================================================

        -- STEP 0a: SourceMasterID → MaterialMapping（最高优先级）
        UPDATE stg
        SET stg.MaterialCode = mm.MaterialCode
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        JOIN MaterialMapping mm
            ON mm.SourceID  = stg.SourceMasterID   -- ✅ 同类INT直接比较，删去CAST
           AND mm.Source    = stg.SourceSystem
           AND mm.IsCurrent = 1
        WHERE stg.SyncStatus      = 'PENDING'
          AND stg.MaterialCode   IS NULL
          AND stg.SourceMasterID IS NOT NULL;

        -- STEP 0b: Model → MaterialMapping.SourceModel（次级）
        -- ⚠️ 一对多风险：先置 FAILED，再唯一命中才更新
        UPDATE stg
        SET stg.SyncStatus     = 'FAILED',
            stg.FailureCode    = 'MATERIAL_MAPPING_AMBIGUOUS',
            stg.NextActionCode = 'MATERIAL_MAPPING_REQUIRED',
            stg.ErrorMessage   = N'Model在MaterialMapping中命中多条MaterialCode，无法唯一解析: Model='
                                 + ISNULL(stg.Model, N'NULL'),
            stg.ProcessedAt    = @ProcessTime
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus    = 'PENDING'
          AND stg.MaterialCode IS NULL
          AND stg.Model        IS NOT NULL
          AND (SELECT COUNT(DISTINCT mm2.MaterialCode)
               FROM MaterialMapping mm2
               WHERE mm2.SourceModel = stg.Model
                 AND mm2.Source      = stg.SourceSystem
                 AND mm2.IsCurrent   = 1) > 1;

        SET @FailedCount = @FailedCount + @@ROWCOUNT;

        -- 唯一命中才更新
        UPDATE stg
        SET stg.MaterialCode = (SELECT TOP 1 mm3.MaterialCode
                                FROM MaterialMapping mm3
                                WHERE mm3.SourceModel = stg.Model
                                  AND mm3.Source      = stg.SourceSystem
                                  AND mm3.IsCurrent   = 1)
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus    = 'PENDING'
          AND stg.MaterialCode IS NULL
          AND stg.Model        IS NOT NULL
          AND (SELECT COUNT(DISTINCT mm4.MaterialCode)
               FROM MaterialMapping mm4
               WHERE mm4.SourceModel = stg.Model
                 AND mm4.Source      = stg.SourceSystem
                 AND mm4.IsCurrent   = 1) = 1;

        -- STEP 0c: OrderEmergencyMaterialOverride（急单覆盖兜底）
        UPDATE stg
        SET stg.MaterialCode = emo.OverrideMaterialCode
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        JOIN OrderEmergencyMaterialOverride emo
            ON emo.SourceOrderId = stg.SourceOrderId
           AND emo.IsActive      = 1
        WHERE stg.SyncStatus    = 'PENDING'
          AND stg.MaterialCode IS NULL;

        -- =================================================================
        -- STEP 1a: 硬失败 — 必填字段缺失 / OrderType 无法识别
        -- ⚠️ OrderType 未知值不允许写入 Canonical，必须 FAILED
        -- ⚠️ FailureCode 优先级：按 CASE 顺序取第一个匹配
        -- =================================================================
        UPDATE stg
        SET stg.SyncStatus     = 'FAILED',
            stg.FailureCode    = CASE
                WHEN stg.SourceSystem   IS NULL OR stg.SourceSystem   = '' THEN 'SOURCESYSTEM_MISSING'
                WHEN stg.SourceOrderId  IS NULL OR stg.SourceOrderId  = '' THEN 'SOURCEORDERID_MISSING'
                WHEN stg.OrderNo        IS NULL OR stg.OrderNo        = '' THEN 'ORDERNO_MISSING'
                WHEN stg.MaterialCode   IS NULL OR stg.MaterialCode   = '' THEN 'MATERIALCODE_MISSING'
                WHEN stg.Quantity       IS NULL OR stg.Quantity       <= 0 THEN 'QUANTITY_INVALID'
                WHEN stg.DueDate        IS NULL                            THEN 'DUEDATE_MISSING'
                WHEN stg.OrderType NOT IN ('SO','MTO','MTS','SS','SS_U','1','2',
                                           'SALES_ORDER','PRODUCTION_INSTRUCTION')
                                                                           THEN 'ORDER_TYPE_UNKNOWN'
                ELSE 'VALIDATION_FAILED'
            END,
            stg.NextActionCode = CASE
                WHEN stg.MaterialCode IS NULL OR stg.MaterialCode = '' THEN 'MATERIAL_MAPPING_REQUIRED'
                ELSE 'MANUAL_REVIEW'
            END,
            stg.ErrorMessage   = CASE
                WHEN stg.SourceSystem   IS NULL OR stg.SourceSystem   = '' THEN N'SourceSystem不能为空'
                WHEN stg.SourceOrderId  IS NULL OR stg.SourceOrderId  = '' THEN N'SourceOrderId不能为空'
                WHEN stg.OrderNo        IS NULL OR stg.OrderNo        = '' THEN N'OrderNo不能为空'
                WHEN stg.MaterialCode   IS NULL OR stg.MaterialCode   = '' THEN N'MaterialCode经三级解析链后仍为空，需补充MaterialMapping'
                WHEN stg.Quantity       IS NULL OR stg.Quantity       <= 0 THEN N'Quantity必须大于0'
                WHEN stg.DueDate        IS NULL                            THEN N'DueDate不能为空'
                WHEN stg.OrderType NOT IN ('SO','MTO','MTS','SS','SS_U','1','2',
                                           'SALES_ORDER','PRODUCTION_INSTRUCTION')
                                                                           THEN N'OrderType无法识别，禁止写入Canonical: '
                                                                                + ISNULL(stg.OrderType, N'NULL')
                ELSE N'未知校验错误'
            END,
            stg.ProcessedAt    = @ProcessTime
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus = 'PENDING'
          AND (
              stg.SourceSystem   IS NULL OR stg.SourceSystem   = ''
              OR stg.SourceOrderId  IS NULL OR stg.SourceOrderId  = ''
              OR stg.OrderNo        IS NULL OR stg.OrderNo        = ''
              OR stg.MaterialCode   IS NULL OR stg.MaterialCode   = ''
              OR stg.Quantity       IS NULL OR stg.Quantity       <= 0
              OR stg.DueDate        IS NULL
              OR stg.OrderType NOT IN ('SO','MTO','MTS','SS','SS_U','1','2',
                                       'SALES_ORDER','PRODUCTION_INSTRUCTION')
          );

        SET @FailedCount = @FailedCount + @@ROWCOUNT;

        -- =================================================================
        -- STEP 1b: 硬失败 — MaterialCode 不在 Material 主数据中
        -- =================================================================
        UPDATE stg
        SET stg.SyncStatus     = 'FAILED',
            stg.FailureCode    = 'MATERIAL_NOT_FOUND',
            stg.NextActionCode = 'MASTER_DATA_FIX',
            stg.ErrorMessage   = N'MaterialCode不在APS Material主数据中: ' + stg.MaterialCode,
            stg.ProcessedAt    = @ProcessTime
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus   = 'PENDING'
          AND stg.MaterialCode IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM Material m WHERE m.MaterialCode = stg.MaterialCode
          );

        SET @FailedCount = @FailedCount + @@ROWCOUNT;

        -- =================================================================
        -- STEP 1c: 非阻断诊断 — BOMNO=NULL
        -- ⚠️ 不置 FAILED；FailureCode 此处 = 诊断告警，非阻断原因
        -- ⚠️ 语义规则（文档写死）：
        --    阻断以 SyncStatus='FAILED' 为准；FailureCode 可记录阻断原因，也可记录非阻断诊断
        --    BOMNO_MISSING + WAIT_BOM_WORKSET = 明确非阻断组合，订单仍可 PROCESSED
        -- ⚠️ 仅当 FailureCode 尚未被 Step 0b/1a/1b 写入时才写（不覆盖高优先级原因）
        -- =================================================================
        UPDATE stg
        SET stg.FailureCode    = 'BOMNO_MISSING',
            stg.NextActionCode = 'WAIT_BOM_WORKSET',
            stg.ErrorMessage   = N'BOMNO为空，等待5号位BOM Workset阶段解析BOM入口'
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus   = 'PENDING'
          AND (stg.BOMNO IS NULL OR stg.BOMNO = '')
          AND stg.FailureCode  IS NULL;   -- 不覆盖已写入的硬失败原因

        -- =================================================================
        -- STEP 1d: 剩余 PENDING → VALIDATED（通过全部校验的行）
        -- =================================================================
        UPDATE stg
        SET stg.SyncStatus = 'VALIDATED'
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus = 'PENDING';

        SET @ValidatedCount = @@ROWCOUNT;

        -- ╔══ 以下步骤均作用于 #TargetStagingIds ∩ SyncStatus='VALIDATED' ══╗

        -- =================================================================
        -- STEP 2a: OrderType 标准化
        -- 已通过 STEP 1a 过滤，此处只剩已知可映射的 ERP 原始值
        -- ZPQF=1 (SO/MTO/1) → SALES_ORDER；ZPQF=2 (MTS/SS/SS_U/2) → PRODUCTION_INSTRUCTION
        -- =================================================================
        UPDATE stg
        SET stg.OrderType = CASE
            WHEN stg.OrderType IN ('SO','MTO','1')         THEN 'SALES_ORDER'
            WHEN stg.OrderType IN ('MTS','SS','SS_U','2')  THEN 'PRODUCTION_INSTRUCTION'
            ELSE stg.OrderType   -- SALES_ORDER/PRODUCTION_INSTRUCTION 已是标准值，原样保留
        END
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus = 'VALIDATED';

        -- =================================================================
        -- STEP 2b: CustomerSegment 派生（CustomerCodeMap）
        -- ⚠️ 收口规则：
        --    CustomerCode IS NULL → CustomerSegment 保持 NULL（ERP未提供，不强制默认）
        --    CustomerCode 有值但 Map 无匹配 → 'UNKNOWN'（不默认 OVERSEAS，两者语义不同）
        --    UNKNOWN ≠ OVERSEAS，消费方须识别 UNKNOWN 走保守路径
        -- =================================================================
        UPDATE stg
        SET stg.CustomerSegment = CASE
            WHEN stg.CustomerCode IS NULL OR stg.CustomerCode = '' THEN NULL
            WHEN ccm.CustomerSegment IS NOT NULL                   THEN ccm.CustomerSegment
            ELSE 'UNKNOWN'
        END
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        LEFT JOIN CustomerCodeMap ccm
            ON ccm.CustomerCode = stg.CustomerCode
           AND ccm.IsActive     = 1
        WHERE stg.SyncStatus      = 'VALIDATED'
          AND stg.CustomerSegment IS NULL;

        -- 非阻断诊断：CustomerCode 有值但 Map 无匹配 → 追加 ErrorMessage（不写 FailureCode，不阻断）
        UPDATE stg
        SET stg.ErrorMessage = ISNULL(stg.ErrorMessage + N'; ', N'')
                             + N'CUSTOMER_SEGMENT_UNKNOWN: CustomerCode=' + stg.CustomerCode
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus      = 'VALIDATED'
          AND stg.CustomerSegment = 'UNKNOWN'
          AND stg.CustomerCode   IS NOT NULL;

        -- =================================================================
        -- STEP 2c: CustomerTier 默认值（V1 GENERAL 兜底）
        -- TODO: VIP 识别规则待业务确认后补充
        -- =================================================================
        UPDATE stg
        SET stg.CustomerTier = ISNULL(stg.CustomerTier, 'GENERAL')
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus   = 'VALIDATED'
          AND stg.CustomerTier IS NULL;

        -- =================================================================
        -- STEP 2d: DelayStatus 推导（V1 简化）
        -- V1：超期 → FIRST_DELAY；其余 → ON_TIME
        -- REPEATED_DELAY 需历史延迟次数追踪，V2 实现
        -- =================================================================
        UPDATE stg
        SET stg.DelayStatus = CASE
            WHEN stg.DueDate < CAST(GETDATE() AS DATE) THEN 'FIRST_DELAY'
            ELSE 'ON_TIME'
        END
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus  = 'VALIDATED'
          AND stg.DelayStatus IS NULL;

        -- =================================================================
        -- STEP 2e: FactoryCode TODO 桩（V1 允许 NULL 进入 Order_Canonical）
        -- TODO: FactoryCode = APS 派生字段，由 ERP 原始工厂/部门/工序经映射规则生成
        -- 映射表/规则待业务确认后实现；当前有值保留，无值留空
        -- ⚠️ 禁止使用 ProcessCode 左补0查 ext_MES_ProcessCode_View 旧逻辑
        -- ⚠️ BOM Workset 5号位必须能接受 FactoryCode=NULL，按 MaterialCode/BOMNO 入口解析
        -- =================================================================

        -- =================================================================
        -- STEP 2f: DemandMaturityStatus TODO 桩（V1 严格留 NULL）
        -- ⚠️ 值域只允许：PRE_CONFIRMED（事前确认）/ FORECAST（SHIKOMI预测）
        -- ⚠️ 严格禁止从以下任何字段临时推断：
        --    OrderType / RawOrderSource / RawNonStockShipmentType / 备注 / CustomerName / OrderNo 模式匹配
        -- 后续确认来源字段后补充派生逻辑
        -- =================================================================

        -- =================================================================
        -- STEP 3: MERGE → Order_Canonical
        -- ✅ Upsert 键：SourceSystem + SourceOrderId（技术幂等键）
        -- ❌ 禁止使用 OrderNo 作 Upsert 键
        -- ⚠️ NonStockShipmentType / OriginalOrderSource 在 source SELECT 中 inline CASE 计算
        --    标准化结果不回写 Staging（Staging 只保留 Raw 原始值）
        -- ⚠️ #TargetStagingIds JOIN 为双重保险，防误处理历史残留 VALIDATED 行
        -- =================================================================
        MERGE INTO Order_Canonical AS target
        USING (
            SELECT
                stg.SourceSystem,
                stg.SourceOrderId,
                stg.SourceMasterID,
                stg.OrderNo,
                stg.MaterialCode,
                stg.Model           AS SourceModel,   -- ERP原始型号追溯
                stg.BOMNO,
                stg.Quantity,
                stg.UOM,
                stg.DueDate,
                stg.OrderType,                        -- 已 STEP 2a 标准化
                stg.Priority,
                stg.Status,
                stg.FactoryCode,                      -- V1 允许 NULL
                stg.CustomerCode,
                stg.TransportMode,
                stg.CustomerName,
                stg.MTS_InstructionNo,
                stg.IssueDate,
                stg.OriginalDueDate,
                stg.ReceivedQty,
                stg.CustomerSegment,                  -- NULL / 枚举值 / UNKNOWN
                stg.SalesOrderCategory,
                stg.DemandMaturityStatus,             -- V1 保持 NULL
                stg.CustomerTier,
                stg.DelayStatus,
                -- NonStockShipmentType 标准化（inline CASE，不回写 Staging）
                CASE stg.RawNonStockShipmentType
                    WHEN N'全額紫票' THEN 'FULL_PURPLE_SLIP'
                    WHEN N'全额紫票' THEN 'FULL_PURPLE_SLIP'
                    WHEN N'差額紫票' THEN 'DIFF_PURPLE_SLIP'
                    WHEN N'差额紫票' THEN 'DIFF_PURPLE_SLIP'
                    ELSE CASE
                        WHEN stg.RawNonStockShipmentType IS NULL
                          OR stg.RawNonStockShipmentType = '' THEN NULL
                        ELSE 'UNKNOWN'
                    END
                END AS NonStockShipmentType,
                -- OriginalOrderSource 标准化（inline CASE，不回写 Staging）
                CASE UPPER(TRIM(stg.RawOrderSource))
                    WHEN 'DAT' THEN 'DAT'
                    WHEN 'P/O' THEN 'PO'
                    WHEN 'PO'  THEN 'PO'
                    ELSE CASE
                        WHEN stg.RawOrderSource IS NULL
                          OR stg.RawOrderSource = '' THEN NULL
                        ELSE 'UNKNOWN'
                    END
                END AS OriginalOrderSource
            FROM ERP_Order_Staging stg
            JOIN #TargetStagingIds t ON t.StagingId = stg.Id
            WHERE stg.SyncStatus = 'VALIDATED'   -- 双重保险
        ) AS source
        ON  target.SourceSystem  = source.SourceSystem
        AND target.SourceOrderId = source.SourceOrderId

        -- 已存在且有变更 → 更新
        WHEN MATCHED AND (
            target.Quantity      <> source.Quantity
            OR target.DueDate    <> source.DueDate
            OR ISNULL(target.BOMNO, '')                     <> ISNULL(source.BOMNO, '')
            OR ISNULL(target.OrderType, '')                 <> ISNULL(source.OrderType, '')
            OR ISNULL(target.Priority, 0)                   <> ISNULL(source.Priority, 0)
            OR ISNULL(target.Status, '')                    <> ISNULL(source.Status, '')
            OR ISNULL(target.FactoryCode, '')               <> ISNULL(source.FactoryCode, '')
            OR ISNULL(target.TransportMode, '')             <> ISNULL(source.TransportMode, '')
            OR ISNULL(target.CustomerSegment, '')           <> ISNULL(source.CustomerSegment, '')
            OR ISNULL(target.SalesOrderCategory, '')        <> ISNULL(source.SalesOrderCategory, '')
            OR ISNULL(target.DemandMaturityStatus, '')      <> ISNULL(source.DemandMaturityStatus, '')
            OR ISNULL(target.CustomerTier, '')              <> ISNULL(source.CustomerTier, '')
            OR ISNULL(target.DelayStatus, '')               <> ISNULL(source.DelayStatus, '')
            OR ISNULL(target.IssueDate, '1900-01-01')       <> ISNULL(source.IssueDate, '1900-01-01')
            OR ISNULL(target.OriginalDueDate, '1900-01-01') <> ISNULL(source.OriginalDueDate, '1900-01-01')
            OR ISNULL(target.ReceivedQty, 0)                <> ISNULL(source.ReceivedQty, 0)
            OR ISNULL(target.NonStockShipmentType, '')      <> ISNULL(source.NonStockShipmentType, '')
            OR ISNULL(target.OriginalOrderSource, '')       <> ISNULL(source.OriginalOrderSource, '')
            OR ISNULL(target.SourceModel, '')               <> ISNULL(source.SourceModel, '')
            OR ISNULL(target.CustomerCode, '')               <> ISNULL(source.CustomerCode, '')
        )
        THEN UPDATE SET
            target.Quantity             = source.Quantity,
            target.DueDate              = source.DueDate,
            target.BOMNO                = source.BOMNO,
            target.OrderType            = source.OrderType,
            target.Priority             = source.Priority,
            target.Status               = source.Status,
            target.FactoryCode          = source.FactoryCode,
            target.TransportMode        = source.TransportMode,
            target.CustomerName         = source.CustomerName,
            target.MTS_InstructionNo    = source.MTS_InstructionNo,
            target.CustomerSegment      = source.CustomerSegment,
            target.SalesOrderCategory   = source.SalesOrderCategory,
            target.DemandMaturityStatus = source.DemandMaturityStatus,
            target.CustomerTier         = source.CustomerTier,
            target.DelayStatus          = source.DelayStatus,
            target.IssueDate            = source.IssueDate,
            target.OriginalDueDate      = source.OriginalDueDate,
            target.ReceivedQty          = source.ReceivedQty,
            target.NonStockShipmentType = source.NonStockShipmentType,
            target.OriginalOrderSource  = source.OriginalOrderSource,
            target.SourceModel          = source.SourceModel,
            target.CustomerCode         = source.CustomerCode,
            target.UpdatedAt            = @ProcessTime

        -- 新订单 → 插入
        WHEN NOT MATCHED BY TARGET
        THEN INSERT (
            SourceSystem, SourceOrderId, SourceMasterID,
            OrderNo, MaterialCode, SourceModel,
            BOMNO, Quantity, UOM, DueDate,
            OrderType, Priority, Status, FactoryCode,
            CustomerCode, TransportMode, CustomerName, MTS_InstructionNo,
            IssueDate, OriginalDueDate, ReceivedQty,
            CustomerSegment, SalesOrderCategory, DemandMaturityStatus,
            CustomerTier, DelayStatus,
            NonStockShipmentType, OriginalOrderSource,
            CreatedAt, UpdatedAt
        )
        VALUES (
            source.SourceSystem, source.SourceOrderId, source.SourceMasterID,
            source.OrderNo, source.MaterialCode, source.SourceModel,
            source.BOMNO, source.Quantity, source.UOM, source.DueDate,
            source.OrderType, source.Priority, source.Status, source.FactoryCode,
            source.CustomerCode, source.TransportMode, source.CustomerName, source.MTS_InstructionNo,
            source.IssueDate, source.OriginalDueDate, source.ReceivedQty,
            source.CustomerSegment, source.SalesOrderCategory, source.DemandMaturityStatus,
            source.CustomerTier, source.DelayStatus,
            source.NonStockShipmentType, source.OriginalOrderSource,
            @ProcessTime, @ProcessTime
        );

        SET @PromotedCount = @@ROWCOUNT;

        -- =================================================================
        -- STEP 4: VALIDATED → PROCESSED（仅 #TargetStagingIds 范围）
        -- =================================================================
        UPDATE stg
        SET stg.SyncStatus  = 'PROCESSED',
            stg.ProcessedAt = @ProcessTime
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus = 'VALIDATED';

        -- =================================================================
        -- STEP 5: 统计 BOMNO_MISSING（已 PROCESSED 的非阻断诊断行）
        -- =================================================================
        SELECT @BOMNOMissingCount = COUNT(*)
        FROM ERP_Order_Staging stg
        JOIN #TargetStagingIds t ON t.StagingId = stg.Id
        WHERE stg.SyncStatus  = 'PROCESSED'
          AND stg.FailureCode = 'BOMNO_MISSING';

        -- =================================================================
        -- STEP 6: APS_ETL_Log
        -- =================================================================
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (
            @BatchNo,
            'sp_ValidateAndPromoteOrders',
            N'批次:' + @BatchNo
            + N' | 校验通过:' + CAST(@ValidatedCount     AS NVARCHAR(10))
            + N' | 提升Canonical:' + CAST(@PromotedCount AS NVARCHAR(10))
            + N' | 硬失败:' + CAST(@FailedCount          AS NVARCHAR(10))
            + N' | BOMNO缺失(非阻断):' + CAST(@BOMNOMissingCount AS NVARCHAR(10)),
            N'SUCCESS',
            GETDATE()
        );

        COMMIT TRANSACTION;

        DROP TABLE IF EXISTS #TargetStagingIds;

        -- 返回统计
        SELECT
            @ValidatedCount     AS ValidatedCount,
            @FailedCount        AS FailedCount,
            @PromotedCount      AS PromotedCount,
            @BOMNOMissingCount  AS BOMNOMissingCount,
            @ProcessTime        AS ProcessTime;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DROP TABLE IF EXISTS #TargetStagingIds;

        SET @ErrorMessage = ERROR_MESSAGE();

        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (
            @BatchNo,
            'sp_ValidateAndPromoteOrders',
            N'订单验证提升失败: ' + @ErrorMessage,
            N'FAILED',
            GETDATE()
        );

        THROW;
    END CATCH
END;
GO

-- 4.4b 订单同步存储过程（v2.5新增，2026-04-03审计修正去硬编码）
-- ⚠️ v2.5新增：从Order_Canonical表同步数据到Order分区表
-- 业务用途：每次排产前，将标准化订单数据同步到分区表中
CREATE PROCEDURE sp_SyncOrdersToPartitionTable
    @PlanVersionId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @SyncTime DATETIME2 = GETDATE();
    DECLARE @InsertCount INT = 0;
    
    -- 1. 从Order_Canonical同步到Order分区表
    -- ⚠️ 只同步 Status = 'OPEN' 的订单/生产指示（v3.12 窄口径；CLOSED/CANCELLED 不进入 Order 分区表）
    -- ⚠️ 2026-04-09 v5.0.3：补充业务字段 + MTS_InstructionNo改为从Canonical读真实值
    -- ⚠️ 2026-04-09 v5.0.4：补充IssueDate/OriginalDueDate/ReceivedQty
    INSERT INTO [Order] (
        PlanVersionId,
        OrderNo,
        OrderType,
        MaterialId,
        ProductFamilyId,
        FactoryId,
        Quantity,
        UOM,
        CustomerDueDate,
        PromisedDate,
        Priority,
        PriorityScore,
        Status,
        DomainKey,
        SourceSystem,
        SourceOrderId,
        MaterialCode,
        BOMNO,
        SourceMasterID,
        MTS_InstructionNo,
        -- v5.0.3 新增字段
        TransportMode,
        CustomerName,
        CustomerSegment,
        SalesOrderCategory,
        DemandMaturityStatus,
        -- v5.0.5 新增字段
        CustomerTier,
        -- v5.0.4 新增字段
        IssueDate,
        OriginalDueDate,
        ReceivedQty,
        -- v5.0.27 新增字段
        SourceModel,
        NonStockShipmentType,
        OriginalOrderSource,
        -- v5.0.34 新增字段
        OrderCanonicalId,
        CreatedAt,
        UpdatedAt
    )
    -- ⚠️ 2026-04-03审计修正：FactoryId从Factory表查FactoryCode，不再硬编码
    -- ⚠️ 2026-05-16 v5.0.27：补 SourceModel/NonStockShipmentType/OriginalOrderSource
    -- ⚠️ v5.0.34：补 oc.Id AS OrderCanonicalId，将 APS 本地快照与 ODS 订单稳定关联
    SELECT 
        @PlanVersionId,
        oc.OrderNo,
        oc.OrderType,
        m.Id AS MaterialId,
        m.ProductFamilyId,
        ISNULL(f.Id, 1) AS FactoryId,  -- 通过FactoryCode关联，找不到时降级为默认工厂
        oc.Quantity,
        m.UOM,
        oc.DueDate AS CustomerDueDate,
        NULL AS PromisedDate,
        oc.Priority,
        NULL AS PriorityScore,  -- v5.1.2：历史/展示兼容字段；V1主链不再计算全局PriorityScore
        oc.Status,
        NULL AS DomainKey,      -- ⚠️ TODO: 由1号位域划分逻辑（sp_AssignDomainKeys）填充
        oc.SourceSystem,
        oc.SourceOrderId,
        oc.MaterialCode,
        oc.BOMNO,
        oc.SourceMasterID,
        oc.MTS_InstructionNo,   -- ⚠️ 2026-04-09修正：从Canonical读真实值（原 CASE WHEN OrderType='MTS' THEN OrderNo 与业务不符）
        -- v5.0.3 新增字段
        oc.TransportMode,
        oc.CustomerName,
        oc.CustomerSegment,
        oc.SalesOrderCategory,
        oc.DemandMaturityStatus,
        -- v5.0.5 新增字段
        oc.CustomerTier,
        -- v5.0.4 新增字段
        oc.IssueDate,
        oc.OriginalDueDate,
        oc.ReceivedQty,
        -- v5.0.27 新增字段
        oc.SourceModel,
        oc.NonStockShipmentType,
        oc.OriginalOrderSource,
        -- v5.0.34 新增字段
        oc.Id AS OrderCanonicalId,              -- v5.0.34: Order_Canonical.Id
        oc.CreatedAt,
        @SyncTime
    FROM Order_Canonical oc
    INNER JOIN Material m ON oc.MaterialCode = m.MaterialCode
    LEFT JOIN Factory f ON f.Code = oc.FactoryCode  -- ⚠️ 2026-04-03修正：从Factory表查；⚠️ 2026-05-03修正：f.FactoryCode→f.Code（Factory表列名是Code）
    WHERE oc.Status = 'OPEN'  -- v3.12: 窄口径，只允许 OPEN 状态订单同步至 Order 分区表；CLOSED/CANCELLED 不参与排程
      AND oc.DueDate BETWEEN GETDATE() AND DATEADD(DAY, 90, GETDATE())
      AND NOT EXISTS (
          SELECT 1 FROM [Order] o 
          WHERE o.OrderNo = oc.OrderNo 
            AND o.PlanVersionId = @PlanVersionId
      );
    
    SET @InsertCount = @@ROWCOUNT;
    
    -- 2. 记录日志
    INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
    VALUES ('SYNC', 'SyncOrdersToPartitionTable', 
            '订单同步完成，PlanVersionId: ' + CAST(@PlanVersionId AS NVARCHAR(10)) + 
            '，同步订单数: ' + CAST(@InsertCount AS NVARCHAR(10)), 
            'SUCCESS', GETDATE());
    
    -- 3. 返回同步统计
    SELECT 
        @PlanVersionId AS PlanVersionId,
        @InsertCount AS InsertCount,
        @SyncTime AS SyncTime;
END;
GO

-- 4.5 活跃根集合划定存储过程（v2.5新增）
-- ⚠️ DEPRECATED（v3.11 废弃）：此 SP 按 BOMNO 聚合，不符合当前订单粒度口径。
-- 保留仅用于旧调用兼容；新代码请使用 sp_GetActiveRootOrders（见下方 §4.5a）。
-- 旧口径：GROUP BY BOMNO，不再作为活跃根集合划定主口径
CREATE OR ALTER PROCEDURE sp_GetActiveRootBOMNOs
    @StartDate DATE = NULL,
    @EndDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- 旧 GROUP BY BOMNO 逻辑已完全废弃；不再返回结果
    THROW 50001, 'DEPRECATED: sp_GetActiveRootBOMNOs has been removed in v3.11. Use sp_GetActiveRootOrders instead.', 1;
END;
GO

-- =============================================
-- 4.5a sp_GetActiveRootOrders（v3.11 新增，取代 sp_GetActiveRootBOMNOs）
-- =============================================
-- 按订单/生产指示粒度返回活跃根集合；不再按 BOMNO 聚合
-- 准入口径：Status = 'OPEN'（v3.12 窄口径）
-- 消费方：2号位在00:00写入 MES_API_BOM_Request_Detail
-- 返回字段：订单身份信息 + MaterialCode + FactoryCode + BOMNO + DueDate + Quantity
CREATE OR ALTER PROCEDURE sp_GetActiveRootOrders
    @StartDate DATE = NULL,
    @EndDate   DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @StartDate IS NULL SET @StartDate = CAST(GETDATE() AS DATE);
    IF @EndDate   IS NULL SET @EndDate   = DATEADD(DAY, 90, @StartDate);

    SELECT
        oc.Id               AS OrderCanonicalId,
        oc.OrderNo,
        oc.SourceSystem,
        oc.SourceOrderId,
        oc.MaterialCode,
        oc.FactoryCode,
        oc.OrderType,
        oc.BOMNO            AS RequestedBOMNO,    -- 可空；无BOMNO订单由5号位Workset阶段解析入口
        oc.DueDate,
        oc.Quantity
    FROM Order_Canonical oc
    WHERE oc.Status = 'OPEN'  -- v3.12: 窄口径，只允许 OPEN 状态订单进入活跃根集合
      AND oc.DueDate BETWEEN @StartDate AND @EndDate
    ORDER BY oc.DueDate, oc.OrderNo;
END;
GO

-- =============================================
-- 第五部分: 使用说明与版本历史
-- =============================================

/*
【版本历史】
v5.0 (2026-04-01) - 资源与工艺数据模型重设计：
🔴 结构性重构：
- Resource表从"手工维护主数据"改为"外部主数据镜像"：新增ExternalResourceId、SourceSystem、WorkshopCode；Code→ResourceCode，Name→ResourceName，Capacity→CapacityFactor；移除ResourceGroupId外键
- ResourceGroup废弃：静态资源组无法表达真实设备可替代性（取决于物料+路径+工序动态组合）
- Routing废弃：线性OperationSeq无法支撑并行/串行混合工艺
🟢 新增表（7个）：
- ResourceOrgGroup：替代ResourceGroup的组织/统计/筛选功能
- ResourcePlanningContext：APS本地排程参数（日历策略、派工优先级、覆盖产能系数）
- RoutingOperation：工序节点表（替代线性Routing，预留RouteCode/PathId支持V2多路径）
- RoutingDependency：工序依赖边表（支持并行/串行混合工艺，V1先ES类型）
- OperationResourceEligibility：工序资源能力关系表（替代ResourceGroup的排程能力功能）
- RoutingPlanningParam：排程规划参数（MinBatchSize/MaxBatchSize从Routing拆出）
🟢 新增ODS视图（4个）：
- MES_APS_Resource_View：资源统一出口（v5.0.13 命名统一，原名 APS_Resource_View；未来 EAM 上线同构新增 EAM_APS_Resource_View）
- MES_APS_Routing_Operation_View：工序节点（替代MES_APS_Routing_View）
- MES_APS_Routing_Dependency_View：工序依赖关系
- APS_OperationResourceEligibility_View：工序资源能力关系
🟢 新增ext_包装视图（4个）：ext_MES_APS_Resource_View（v5.0.13命名统一）、ext_MES_APS_Routing_Operation_View、ext_MES_APS_Routing_Dependency_View、ext_APS_OperationResourceEligibility_View
🟢 设计依据：资源镜像 + 工艺图 + 工序资源能力关系表（详见 APS_资源与工艺数据模型重设计方案_v5.0.md）

v4.0 (2026-04-01) - 双源同构主数据三表协同同步重构：
🔴 架构变更：
- 合并 sp_SyncERPMasterData + sp_SyncMESMaterialData → 统一参数化 sp_SyncMasterData(@SourceType)
- MaterialMapping 重构：消除 ERP/MES 字段分叉，ERP_MasterID+MES_ID→统一SourceID，ERP_Warehouse+MES_Location→统一Warehouse
- MaterialMapping 唯一索引简化：6列→4列（MaterialCode, Source, Warehouse_Norm, IsCurrent）
- MaterialType 由 APS 按 MaterialCode 前缀统一推导：FG→FINISHED_GOOD, RAW→RAW_MATERIAL, WIP→SEMI_FINISHED, ASSY→ASSY, 其他→UNKNOWN
- MES 升级为三表协同（原仅同步 MaterialMapping，现同步 Material+MaterialMapping+MaterialSupplyContext）
- MaterialSupplyContext 新增 InventoryManagementMode 字段（STOCKED/NON_STOCKED）
- MaterialSupplyContext.SourceSystem 移除默认值 'ERP'（双源都会写入）
- InventorySourceRule 默认规则新增 ASSY-% 前缀
- 双源视图契约同构：ERP和MES视图字段完全一致，SP逻辑零分叉
🟢 设计依据：双源同构契约 + 三表协同同步

v3.0 (2026-04-01) - ERP主数据三表协同同步重构（已被v4.0替代）：
- 此版本已合并入v4.0，不再独立存在

v2.5 (2026-03-18) - 修复Order_Canonical表缺失问题：
🔴 P0修复：
- 新增Order_Canonical表：订单标准化表，用于存储从ERP同步的原始订单数据
- 统一Material表字段命名：Code→MaterialCode，Name→MaterialName
- 新增sp_SyncOrdersToPartitionTable存储过程：从Order_Canonical同步到Order分区表
- 新增sp_GetActiveRootBOMNOs存储过程：划定活跃根集合，支持每天00:00的BOM展开
🟡 P1修复：
- 更新Order表的MaterialCode字段引用：从m.Code改为m.MaterialCode
- 完善Order_Canonical表索引：支持活跃根集合的高效提取
- 明确数据流向：ERP→Order_Canonical→Order分区表

v2.4 (2026-03-11) - P0/P1终极修复完全体（七大SQL硬伤+五大架构洁癖）：
🔴 P0终极修复：
- P0-1: MaterialMapping唯一索引SQL Server语法兼容性：采用持久化计算列（ERP_Warehouse_Norm、MES_ID_Norm）
- P0-2&3: Order表字段依赖顺序与名称错误：重组DDL执行时序，修正字段引用（m.Code而非m.MaterialCode）
- P0-4: sp_SyncMaterialMapping中MES来源视图错位：使用MES_Material_View而非MES_BOM_View（边表）
- P0-5: MaterialMapping的源端失效收口（幽灵物料终结者）：使用WHEN NOT MATCHED BY SOURCE处理源端失效物料
- P0-6: 跨库视图的物理寻址写死（消灭部署歧义）：创建跨库包装视图（ext_ERP_Master_View、ext_MES_Material_View）
- P0-7: sp_SyncMaterialMapping的MERGE条件重构（一物多仓精确制导）：ON条件加入Warehouse维度，避免"同一行被多次更新"异常
🟡 P1终极修复：
- P1-1: 彻底删除旧Inventory表，切断后路，倒逼规范落地
- P1-2: 口径降级（防篡改→高可信历史追溯）
- P1-3: sp_SyncMaterialMapping统一使用MERGE INTO语句，具有原子性
- P1-4: MES_API_BOM_Request_Detail增加去重约束（防止BOMNO重复膨胀）：强制同一批次内BOMNO绝对唯一
- P1-5: 物料映射API返回结构重构（单对象→列表）：支持一物多仓场景

v2.3 (2026-03-11) - P0终极修复补完（六大SQL硬伤+三大架构洁癖）：
🔴 P0终极修复：
- P0-1: MaterialMapping唯一索引SQL Server语法兼容性：采用持久化计算列（ERP_Warehouse_Norm、MES_ID_Norm）
- P0-2&3: Order表字段依赖顺序与名称错误：重组DDL执行时序，修正字段引用（m.Code而非m.MaterialCode）
- P0-4: sp_SyncMaterialMapping中MES来源视图错位：使用MES_Material_View而非MES_BOM_View（边表）
- P0-5: MaterialMapping的源端失效收口（幽灵物料终结者）：使用WHEN NOT MATCHED BY SOURCE处理源端失效物料
- P0-6: 跨库视图的物理寻址写死（消灭部署歧义）：创建跨库包装视图（ext_ERP_Master_View、ext_MES_Material_View）
🟡 P1终极修复：
- P1-1: 彻底删除旧Inventory表，切断后路，倒逼规范落地
- P1-2: 口径降级（防篡改→高可信历史追溯）
- P1-3: sp_SyncMaterialMapping统一使用MERGE INTO语句，具有原子性

v2.2 (2026-03-11) - P0/P1终极修复（四大SQL硬伤+三大架构洁癖）：
🔴 P0终极修复：
- P0-1: MaterialMapping唯一索引SQL Server语法兼容性：采用持久化计算列（ERP_Warehouse_Norm、MES_ID_Norm）
- P0-2&3: Order表字段依赖顺序与名称错误：重组DDL执行时序，修正字段引用（m.Code而非m.MaterialCode）
- P0-4: sp_SyncMaterialMapping中MES来源视图错位：使用MES_Material_View而非MES_BOM_View（边表）
🟡 P1终极修复：
- P1-1: 彻底删除旧Inventory表，切断后路，倒逼规范落地
- P1-2: 口径降级（防篡改→高可信历史追溯）
- P1-3: sp_SyncMaterialMapping统一使用MERGE INTO语句，具有原子性

v2.1 (2026-03-11) - P0/P1架构修复：
🔴 P0修复：
- 修复MaterialMapping唯一索引冲突：将ERP_Warehouse和MES_ID纳入唯一约束，兼容一物多码/多地现象
- 修复库存表结构：拆分为InventoryFact_ERP、InventoryFact_MES、InventoryBalance三层架构
- 修复sp_SyncMaterialMapping的SQL语法错误：改用MERGE INTO语句，废弃有问题的UPDATE/INSERT混合写法
🟡 P1修复：
- 修复Order表建模：新增SourceMasterID、MTS_InstructionNo、MaterialCode字段
- 新增IX_Order_ActiveRoots复合索引，支撑00:00活跃根集合的高效提取

v2.0 (2026-03-10):
- 新增MES_Integration ODS库（7张表）
- 新增APS_BOM_RAW表（本地BOM缓存）
- 新增MaterialMapping表（SCD Type 2拉链表）
- 新增InventorySourcePriority表（库存来源优先级配置）
- 新增APS_ETL_Log表（ETL日志）
- 修改PlanVersion表（新增快照字段）
- 修改Material表（新增LLC字段）
- 修改BOM表（明确Quantity为单位用量）
- 修改Inventory表（新增Source字段）
- 修改Order表（新增BOMNO字段）
- 新增ODS库存储过程（批次展开、实时展开、批次清理）
- 新增APS库存储过程（BOM拉取、LLC计算、物料映射同步）

v1.0 (2026-03-05):
- 初始版本

【部署说明】
1. 修改数据库文件路径（第一部分、第二部分）
2. 确保SSD已挂载到E:\SSD（或修改为实际路径）
3. 创建MES_BOM_View和ERP_Master_View视图（防腐层契约）
4. 执行ODS库DDL（第一部分）
5. 执行APS库DDL（第二部分）
6. 执行存储过程（第三、四部分）
7. 配置SQL Server Agent Job（定时任务）

【关键架构红线】
⚠️ BOM的Quantity字段必须是单位用量，绝对不能累乘！
⚠️ ODS库的BOM展开必须在独立库执行，不能在MES生产库执行！
⚠️ APS排程时必须物理断网，基于本地APS_BOM_RAW表！
⚠️ 快照封存到4.76T机械硬盘，不占用SSD空间！

【性能优化建议】
1. ODS库和APS库的数据文件和日志文件都放在SSD上
2. 快照文件放在机械硬盘上（路径在应用层配置）
3. 定期执行sp_CleanupBOMWorkset清理ODS库历史批次
4. 定期更新统计信息和重建索引

【监控指标】
1. ODS库批次展开耗时（目标：15分钟内完成80万BOMNO）
2. APS库BOM拉取耗时（目标：5分钟内完成350万行）
3. LLC计算耗时（目标：5分钟内完成）
4. SSD使用率（预警阈值：80%）
5. 快照文件大小（单个文件约50MB压缩后）

【故障应急】
1. ODS库展开失败：检查MES_API_BOM_Request_Log表
2. SSD容量不足：执行sp_CleanupBOMWorkset清理历史批次
3. 快照文件损坏：检查SnapshotFileHash字段
*/

-- =============================================
-- Batch 3：排程运行编排 + 结果读模型 + 阶段二骨架（v5.0.25，2026-05-13）
-- 对齐：总表 v3.17 / 防腐层 v1.20 / 字段说明 v5.0.25
-- =============================================

-- -----------------------------------------------
-- 3.1 ScheduleRun（运行编排主表，阶段一即用）
-- -----------------------------------------------
-- 定位：记录这次运行怎么跑。与 PlanVersion（结果版本）分离。
-- 产出版本通过 PlanVersion.SourceScheduleRunId 反查。
-- ScheduleRun/PlanVersion具体创建分钟点不作为V1业务强约束；由2号位现有编排把握。
-- 只要求：在调用需要RunId的MES快照SP前已取得本次ScheduleRunId+统一DataCutoffTime，相关快照共享同一运行切片，并在正式求解消费前完成。
-- 02:00排程启动时读取已创建记录，创建PlanVersion（SourceScheduleRunId=当前Id）。
-- RunType 值域：FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF
-- Status 值域：RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED
--   ⚠️ V1 口径（Domain 独立发布 + 运行汇总终态）：
--     · 各 Domain 通过各自独立事务创建并激活 PlanVersion（各域独立发布，非 ALL_OR_NOTHING）；
--     · ScheduleRun 终态由本次运行对所有 ExpectedDomainKeys 的汇总结果决定（非"创建 PlanVersion 后直接 COMPLETED"）。
--   · RUNNING        = 本次运行仍有预期 Domain 未进入终态
--   · COMPLETED      = 所有 ExpectedDomainKeys 对应 Domain 均「计算成功 + 落盘成功 + 对应 PlanVersion 进入运行成功终态」（夜间 FULL_SCHEDULE = 对应 PlanVersion 已 ACTIVE；白天 Candidate = 对应 PlanVersion 已 CANDIDATE；完整 RunType 状态矩阵见防腐层 §2.8.7）
--   · PARTIAL_SUCCESS= 至少一个预期 Domain 已成功发布 且 至少一个预期 Domain 失败/未启动/未完成发布
--   · FAILED         = 运行级致命错误 或 本次没有任何预期 Domain 成功发布
--   · CompletedAt 在所有终态（含 PARTIAL_SUCCESS）写入
CREATE TABLE ScheduleRun (
    Id                  INT PRIMARY KEY IDENTITY(1,1),
    RunType             NVARCHAR(50)  NOT NULL,          -- 运行类型
    Status              NVARCHAR(20)  NOT NULL,          -- RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED（运行状态；定义见表头注释）
    ScenarioId          INT           NULL,              -- 逻辑关联 Scenario.Id；数据库未建立物理外键，由应用服务校验引用有效性（仿真/插单分析关联；凌晨全量为空）
    BasePlanVersionId   INT           NULL,              -- 逻辑关联 PlanVersion.Id，表示白天 Candidate 所基于的当前 ACTIVE 版本；数据库未建立物理外键，由创建服务校验版本存在、状态和 Domain 一致性（凌晨全量可为空）
    TriggeredBy         NVARCHAR(100) NOT NULL,          -- 'Hangfire' / UserId / 'API' / 'Agent'
    DataCutoffTime      DATETIME2     NOT NULL,          -- 本次运行统一数据切片边界
    ScopeJson           NVARCHAR(MAX) NULL,              -- 局部重排范围JSON（⚠️预期DomainKey集合已迁移至独立字段 ExpectedDomainKeysJson，本字段不再承载 ExpectedDomainKeys）
    ExpectedDomainKeysJson NVARCHAR(MAX) NULL,           -- 运行启动时冻结的预期DomainKey集合，JSON数组格式。是ScheduleRun终态判定的唯一权威来源。创建后不可修改；不得根据已创建PlanVersion反推。
    StartedAt           DATETIME2     NOT NULL DEFAULT GETDATE(),
    CompletedAt         DATETIME2     NULL,              -- 2号位回填；⚠️在 ScheduleRun 所有终态（COMPLETED / PARTIAL_SUCCESS / FAILED）均写入
    ErrorMessage        NVARCHAR(MAX) NULL,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CK_ScheduleRun_ExpectedDomainKeysJson_IsJson CHECK (ExpectedDomainKeysJson IS NULL OR ISJSON(ExpectedDomainKeysJson) = 1)
);
GO

CREATE INDEX IX_ScheduleRun_RunType_Status
    ON ScheduleRun(RunType, Status, StartedAt DESC);
CREATE INDEX IX_ScheduleRun_Scenario
    ON ScheduleRun(ScenarioId)
    WHERE ScenarioId IS NOT NULL;
CREATE INDEX IX_ScheduleRun_BasePlanVersion
    ON ScheduleRun(BasePlanVersionId)
    WHERE BasePlanVersionId IS NOT NULL;
GO

-- -----------------------------------------------
-- 3.2 PlanVersion（计划版本表）字段调整
-- -----------------------------------------------
-- 定位：记录跑出来的每一套结果版本。与 ScheduleRun（运行过程）分离。
-- 一套结果一条 PlanVersion；多方案仿真产生多条 PlanVersion。
-- 正式采用直接通过 PlanVersion.Status = ACTIVE 表示。

-- v5.0.43 字段更名：VersionType → VersionCategory（避免与 RunType 混淆）
EXEC sp_rename 'PlanVersion.VersionType', 'VersionCategory', 'COLUMN';
GO

-- 新增来源追溯列
ALTER TABLE PlanVersion
    ADD SourceScheduleRunId   INT           NOT NULL;               -- FK→ScheduleRun.Id；具体分钟点由现有编排把握，不在DDL注释中强制00:38/02:00时序
ALTER TABLE PlanVersion
    ADD SourceSimulationRunId INT           NULL;                 -- 逻辑引用 SimulationRun.Id；物理 FK 阶段二仿真实装后补建
ALTER TABLE PlanVersion
    ADD ActivatedAt           DATETIME2     NULL;                 -- Status 变为 ACTIVE 时记录
ALTER TABLE PlanVersion
    ADD ActivatedBy           NVARCHAR(100) NULL;                 -- 激活人/来源
GO

-- 外键：SourceScheduleRunId（ScheduleRun 先于 PlanVersion 创建，可安全建 FK）
ALTER TABLE PlanVersion
ADD CONSTRAINT FK_PlanVersion_SourceScheduleRun
    FOREIGN KEY (SourceScheduleRunId) REFERENCES ScheduleRun(Id);
GO

-- Status 值域（版本生命周期状态，非运行状态）：
--   BUILDING  = 版本壳已创建，结果尚未完整落库
--   CANDIDATE = 已生成，但未正式采用（仿真/重排候选版本默认状态）
--   ACTIVE    = 当前正式采用版本
--   ARCHIVED  = 历史已归档
--   FAILED    = 版本构建失败或不可用

-- 历史兼容字段（不作为权威来源）：
--   StartedAt / CompletedAt / DurationSeconds / ErrorMessage
--   权威运行状态已归 ScheduleRun；此处仅保留为历史兼容。

CREATE INDEX IX_PlanVersion_SourceScheduleRun
    ON PlanVersion(SourceScheduleRunId);
CREATE INDEX IX_PlanVersion_Status
    ON PlanVersion(Status)
    WHERE Status = 'ACTIVE';
GO

-- v5.1.1：UQ_PlanVersion_ScheduleRun_Domain 必须在此处创建（ScheduleRun 已建、SourceScheduleRunId 已 ALTER 增加、
--   SourceSimulationRunId 已增加、FK_PlanVersion_SourceScheduleRun 已创建之后）。V1 同一 ScheduleRun + 同一 DomainKey
--   最多一个 PlanVersion；阶段二 SimulationRun 同域多候选不受阻止（排除 SourceSimulationRunId IS NOT NULL 的行）。
CREATE UNIQUE INDEX UQ_PlanVersion_ScheduleRun_Domain
    ON PlanVersion(SourceScheduleRunId, DomainKey)
    WHERE DomainKey IS NOT NULL AND SourceSimulationRunId IS NULL;
GO

-- -----------------------------------------------
-- 3.2b ProductionInstructionPositionSnapshot（PI位置最小快照，v5.1.2新增）
-- -----------------------------------------------
-- 一张PI可多行表示Stage WIP/XC/PI级跨厂在途/等待入库/UNLOCATED，所有位置数量互斥闭合。
-- PI总剩余边界仍由ERP生产指示Quantity-ReceivedQty定义；Position只解释“在哪里”，不能创造额外Supply身份。
CREATE TABLE ProductionInstructionPositionSnapshot (
    Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
    ScheduleRunId            INT NOT NULL,
    PlanVersionId            INT NOT NULL,
    ProductionInstructionNo  NVARCHAR(100) NOT NULL,
    MaterialId               INT NOT NULL,
    MaterialCode             NVARCHAR(100) NOT NULL,
    PositionType             NVARCHAR(50) NOT NULL,
    Quantity                 DECIMAL(18,4) NOT NULL,
    CurrentStageCode         NVARCHAR(50) NULL,
    NextStageCode            NVARCHAR(50) NULL,
    AvailableTime            DATETIME2 NULL,
    SourceType               NVARCHAR(50) NULL,
    SourceKey                NVARCHAR(200) NULL,
    IssueCode                NVARCHAR(100) NULL,
    Confidence               NVARCHAR(30) NULL,
    CreatedAt                DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CK_PIPosition_Qty CHECK (Quantity >= 0)
);
GO
CREATE INDEX IX_PIPosition_Plan_PI
    ON ProductionInstructionPositionSnapshot(PlanVersionId, ProductionInstructionNo)
    INCLUDE (PositionType, Quantity, CurrentStageCode, NextStageCode, AvailableTime);
CREATE INDEX IX_PIPosition_Run
    ON ProductionInstructionPositionSnapshot(ScheduleRunId, ProductionInstructionNo);
GO

-- -----------------------------------------------
-- 3.2c DemandSupplyHardLock（Strict Binding / Demand Protection份额锁，v5.1.2新增）
-- -----------------------------------------------
-- 不另建DemandProtection第二张表。Execution现实事实/Firm-Frozen排程不可移动约束保持独立。
CREATE TABLE DemandSupplyHardLock (
    Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
    LockType                 NVARCHAR(30) NOT NULL,
    DemandType               NVARCHAR(50) NOT NULL,
    DemandKey                NVARCHAR(200) NOT NULL,
    SupplyType               NVARCHAR(50) NOT NULL,
    SupplyKey                NVARCHAR(200) NOT NULL,
    LockedQty                DECIMAL(18,4) NOT NULL,
    SourcePlanVersionId      INT NULL,
    SourceAllocationSequence BIGINT NULL,
    Status                   NVARCHAR(20) NOT NULL DEFAULT N'ACTIVE',
    CreatedAt                DATETIME2 NOT NULL DEFAULT GETDATE(),
    CreatedBy                NVARCHAR(100) NULL,
    ReleasedAt               DATETIME2 NULL,
    ReleasedBy               NVARCHAR(100) NULL,
    ReleaseReason            NVARCHAR(500) NULL,
    CONSTRAINT CK_DemandSupplyHardLock_Type CHECK (LockType IN (N'STRICT_BINDING',N'DEMAND_PROTECTION')),
    CONSTRAINT CK_DemandSupplyHardLock_Status CHECK (Status IN (N'ACTIVE',N'RELEASED',N'BROKEN')),
    CONSTRAINT CK_DemandSupplyHardLock_Qty CHECK (LockedQty > 0)
);
GO
CREATE INDEX IX_DSHL_Demand
    ON DemandSupplyHardLock(DemandKey, LockType, Status)
    INCLUDE (SupplyKey, LockedQty);
CREATE INDEX IX_DSHL_Supply
    ON DemandSupplyHardLock(SupplyKey, LockType, Status)
    INCLUDE (DemandKey, LockedQty);
GO

-- v5.1.2 明确不建：PeggingAllocationLedger / FrozenZoneSnapshot / VirtualInventoryBalance /
-- PI Header+Slice双表 / PlanningPurchasePlaceholder持久表 / 有限物流Task平台。
-- 若历史环境已有FrozenZoneSnapshot/VirtualInventoryBalance，只标deprecated并退出生成/消费链，不在本DDL新增或扩展。

-- -----------------------------------------------
-- 3.3 ScheduleExplanationFact（结构化原因事实，阶段一最小骨架，分区表）
-- -----------------------------------------------
-- 1号位在内存推演中以 ExplanationFactDraft 形态产出，由2号位与 Task/Pegging 同批次落盘。
-- EvidenceJson 外壳稳定（保证向后兼容），各 ReasonCode 内部 schema 随阶段演进填充。
-- ⚠️ 1号位禁止直接写此表；统一由2号位批量落盘。
CREATE TABLE ScheduleExplanationFact (
    Id              BIGINT        IDENTITY(1,1) NOT NULL,
    PlanVersionId   INT           NOT NULL,              -- 分区键；逻辑关联 PlanVersion.Id；数据库未建立物理外键，由应用服务校验引用有效性
    ScheduleRunId   INT           NULL,                  -- FK → ScheduleRun.Id（本次运行；跨域失败风险定位用，可空以兼容历史/非运行触发数据）
    ObjectType      NVARCHAR(50)  NOT NULL,              -- ORDER / TASK / RESOURCE / STAGE / DOMAIN（DOMAIN 用于跨域版本不一致风险 CROSS_DOMAIN_VERSION_MISMATCH_RISK）
    OrderId         BIGINT        NULL,              -- 对齐 Order.Id(BIGINT)
    TaskId          BIGINT        NULL,              -- 对齐 Task.Id(BIGINT)
    ResourceId      INT           NULL,
    StageCode       NVARCHAR(50)  NULL,
    ReasonCode      NVARCHAR(100) NOT NULL,              -- 结构化原因码，阶段二扩充
    Severity        NVARCHAR(20)  NOT NULL,              -- INFO / WARN / ERROR
    ImpactHours     DECIMAL(10,2) NULL,
    EvidenceJson    NVARCHAR(MAX) NULL,                  -- 外壳稳定；内部结构按ReasonCode演进
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_ScheduleExplanationFact PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

CREATE INDEX IX_SEF_Order
    ON ScheduleExplanationFact(OrderId, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);
CREATE INDEX IX_SEF_ReasonCode
    ON ScheduleExplanationFact(ReasonCode, Severity, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);
GO

-- v5.1.1：ScheduleExplanationFact.ScheduleRunId → ScheduleRun.Id（本次运行追溯；跨域失败每受影响域写一条
--   ScheduleRunId=本次RunId, PlanVersionId=该域本次PlanVersion.Id, ObjectType=DOMAIN, ReasonCode=CROSS_DOMAIN_VERSION_MISMATCH_RISK）
ALTER TABLE ScheduleExplanationFact
    ADD CONSTRAINT FK_SEF_ScheduleRun FOREIGN KEY (ScheduleRunId) REFERENCES ScheduleRun(Id);
GO
CREATE INDEX IX_SEF_ScheduleRun
    ON ScheduleExplanationFact(ScheduleRunId, ReasonCode);
GO

-- -----------------------------------------------
-- 3.4 OrderScheduleSummary（订单级读模型，阶段一即用，分区表）
-- -----------------------------------------------
-- 由2号位在 Task/Pegging 落库后后处理异步生成；禁止进入1号位排程内核。
CREATE TABLE OrderScheduleSummary (
    Id              INT           IDENTITY(1,1) NOT NULL,
    PlanVersionId   INT           NOT NULL,              -- 分区键；逻辑关联 PlanVersion.Id；数据库未建立物理外键
    OrderId         BIGINT        NOT NULL,              -- 逻辑关联 Order.Id(BIGINT)；数据库未建立物理外键
    PlannedEndDate  DATETIME2     NULL,                  -- 计划完工时间
    DelayHours      DECIMAL(10,2) NULL,                  -- 延期小时数（负=提前）
    RiskLevel       NVARCHAR(20)  NULL,                  -- LOW / MEDIUM / HIGH / CRITICAL
    MainReasonCode  NVARCHAR(100) NULL,                  -- 主因 ReasonCode（来自 ScheduleExplanationFact）
    IsVipImpacted   BIT           NOT NULL DEFAULT 0,
    GeneratedAt     DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_OrderScheduleSummary PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

CREATE UNIQUE INDEX UQ_OrderScheduleSummary_Order
    ON OrderScheduleSummary(OrderId, PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);
GO

-- -----------------------------------------------
-- 3.5 ResourceLoadSummary（资源×日期读模型，阶段一即用）
-- -----------------------------------------------
CREATE TABLE ResourceLoadSummary (
    Id              INT           PRIMARY KEY IDENTITY(1,1),
    PlanVersionId   INT           NOT NULL,              -- 逻辑关联 PlanVersion.Id；数据库未建立物理外键
    ResourceId      INT           NOT NULL,              -- 逻辑关联 Resource.Id；数据库未建立物理外键
    LoadDate        DATE          NOT NULL,
    LoadHours       DECIMAL(10,2) NULL,
    AvailableHours  DECIMAL(10,2) NULL,
    LoadRate        DECIMAL(7,4)  NULL,                  -- LoadHours / AvailableHours（可超1.0=过载）
    IsBottleneck    BIT           NOT NULL DEFAULT 0,
    GeneratedAt     DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

CREATE UNIQUE INDEX UQ_ResourceLoadSummary_Key
    ON ResourceLoadSummary(PlanVersionId, ResourceId, LoadDate);
CREATE INDEX IX_ResourceLoadSummary_Bottleneck
    ON ResourceLoadSummary(PlanVersionId, IsBottleneck)
    WHERE IsBottleneck = 1;
GO

-- -----------------------------------------------
-- 3.6 PlanKpiSummary（版本级 KPI 读模型，阶段一即用，分区表）
-- -----------------------------------------------
CREATE TABLE PlanKpiSummary (
    Id                  INT           IDENTITY(1,1) NOT NULL,
    PlanVersionId       INT           NOT NULL,          -- 分区键；逻辑关联 PlanVersion.Id；数据库未建立物理外键（1:1）
    OnTimeRate          DECIMAL(7,4)  NULL,              -- 准交率 0.0000-1.0000
    DelayedOrderCount   INT           NULL,
    MaxDelayHours       DECIMAL(10,2) NULL,
    VipDelayedCount     INT           NULL,
    AvgLoadRate         DECIMAL(7,4)  NULL,
    BottleneckCount     INT           NULL,
    WipEstimate         DECIMAL(15,2) NULL,
    GeneratedAt         DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_PlanKpiSummary PRIMARY KEY (Id, PlanVersionId)
) ON PS_PlanVersion(PlanVersionId);
GO

CREATE UNIQUE INDEX UQ_PlanKpiSummary_Version
    ON PlanKpiSummary(PlanVersionId)
    ON PS_PlanVersion(PlanVersionId);
GO

-- -----------------------------------------------
-- 3.7 Scenario（what-if 场景表，V1 白天实时评估正式业务容器）
-- -----------------------------------------------
-- 定位：记录一个业务试算场景的假设、目标和最终选中版本。不是运行表，不是结果版本表。
-- v5.1.2：Scenario仅为可选业务容器；Candidate/CTP主链不得因表存在而强制先建Scenario（ScheduleRun.ScenarioId可空）。
-- ⚠️ 阶段二：SimulationRun（仿真算法执行骨架）/ ScenarioObjectiveScore（多方案评分骨架）另设，不在本表实装。
CREATE TABLE Scenario (
    Id                    INT           PRIMARY KEY IDENTITY(1,1),
    ScenarioName          NVARCHAR(200) NOT NULL,              -- 场景名称（v5.0.44 rename from Name）
    Description           NVARCHAR(MAX) NULL,
    ScenarioType          NVARCHAR(50)  NOT NULL,              -- SIMULATION / INSERT_ORDER_WHATIF（v5.0.44 rename from RunType）
    Status                NVARCHAR(20)  NOT NULL DEFAULT 'DRAFT',  -- DRAFT/RUNNING/COMPLETED/SELECTED/SUBMITTED
    AssumptionJson        NVARCHAR(MAX) NULL,                  -- what-if 假设参数
    ObjectiveJson         NVARCHAR(MAX) NULL,                  -- 优化目标JSON（v5.0.43新增）
    SelectedPlanVersionId INT           NULL,                  -- 逻辑关联 PlanVersion.Id；数据库未建立物理外键，用于记录人工选中的候选版本；候选版本生成后回填；可空
    UpdatedAt             DATETIME2     NULL,
    CreatedByUserId       INT           NULL,
    CreatedAt             DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

-- -----------------------------------------------
-- 3.8 SimulationRun（仿真算法运行表，阶段二骨架，阶段一不实装）
-- -----------------------------------------------
-- 定位：记录某个场景下的一次具体算法执行。一次执行可产生多个候选PlanVersion。
CREATE TABLE SimulationRun (
    Id                  INT           PRIMARY KEY IDENTITY(1,1),
    ScenarioId          INT           NOT NULL,             -- 逻辑关联 Scenario.Id；阶段二骨架字段，当前未建立物理外键
    ScheduleRunId       INT           NULL,                 -- 逻辑关联 ScheduleRun.Id；阶段二骨架字段，当前未建立物理外键
    AlgorithmType       NVARCHAR(50)  NULL,                 -- RULE_HEURISTIC / GENETIC / SIMULATED_ANNEALING / HYBRID
    AlgorithmVersion    NVARCHAR(50)  NULL,
    AlgorithmConfigJson NVARCHAR(MAX) NULL,                 -- 算法参数JSON，便于复盘和重跑
    Status              NVARCHAR(20)  NOT NULL DEFAULT 'RUNNING',  -- RUNNING / COMPLETED / FAILED
    ErrorMessage        NVARCHAR(MAX) NULL,
    StartedAt           DATETIME2     NULL,
    CompletedAt         DATETIME2     NULL,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

-- -----------------------------------------------
-- 3.9 ScenarioObjectiveScore（多目标评分，阶段二骨架，阶段一不实装）
-- -----------------------------------------------
-- 阶段二 Scenario 比较时直接对比汇总结果，不重扫 Task 明细。
-- Score 来源：对应 PlanVersion 的 PlanKpiSummary 中指定指标的归一化值。
CREATE TABLE ScenarioObjectiveScore (
    Id              INT           PRIMARY KEY IDENTITY(1,1),
    ScenarioId      INT           NOT NULL,              -- 逻辑关联 Scenario.Id；当前未建立物理外键
    PlanVersionId   INT           NOT NULL,              -- 逻辑关联 PlanVersion.Id；当前未建立物理外键
    ObjectiveName   NVARCHAR(100) NOT NULL,              -- ON_TIME_RATE / RESOURCE_EFFICIENCY / WIP_ESTIMATE 等
    Score           DECIMAL(10,4) NULL,                  -- 原始指标值
    NormalizedScore DECIMAL(7,4)  NULL,                  -- 0.0-1.0 归一化，用于多目标比较
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

CREATE UNIQUE INDEX UQ_ScenarioObjectiveScore_Key
    ON ScenarioObjectiveScore(ScenarioId, PlanVersionId, ObjectiveName);
GO

-- =============================================
-- 3.10 规则与参数引擎（v5.0.45 新增 2026-06-23）
-- =============================================
-- 定位：APS 业务策略中枢，统一管理规则集、参数集、策略包的版本、发布、运行绑定和追溯。
-- 设计原则：主题规则表保留；引擎负责版本/发布/组合/绑定/追溯。
-- V1 克制：不做万能脚本引擎，不做 RuleCondition/RuleAction/RuleExpression，不做完整审批流闭环。

-- -----------------------------------------------
-- 3.10.1 RuleSet（规则集主表）
-- -----------------------------------------------
CREATE TABLE RuleSet (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleSetCode     NVARCHAR(50)  NOT NULL UNIQUE,
    RuleSetName     NVARCHAR(200) NOT NULL,
    Description     NVARCHAR(1000) NULL,
    IsActive        BIT           NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy       NVARCHAR(100) NULL,
    UpdatedAt       DATETIME2     NULL,
    UpdatedBy       NVARCHAR(100) NULL
);
GO

-- -----------------------------------------------
-- 3.10.2 RuleSetVersion（规则集版本表）
-- -----------------------------------------------
-- 红线：已发布版本不可原地修改，须创建新版本。正式排程只允许 PUBLISHED 状态。
CREATE TABLE RuleSetVersion (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleSetId       BIGINT         NOT NULL,
    VersionCode     NVARCHAR(50)   NOT NULL,
    Status          NVARCHAR(20)   NOT NULL DEFAULT 'DRAFT',
    EffectiveFrom   DATETIME2      NULL,
    EffectiveTo     DATETIME2      NULL,
    PublishedAt     DATETIME2      NULL,
    PublishedBy     NVARCHAR(100)  NULL,
    ApprovedAt      DATETIME2      NULL,
    ApprovedBy      NVARCHAR(100)  NULL,
    CreatedAt       DATETIME2      NOT NULL DEFAULT GETDATE(),
    CreatedBy       NVARCHAR(100)  NULL,
    CONSTRAINT FK_RuleSetVersion_RuleSet FOREIGN KEY (RuleSetId) REFERENCES RuleSet(Id),
    CONSTRAINT UQ_RuleSetVersion UNIQUE (RuleSetId, VersionCode),
    CONSTRAINT CK_RuleSetVersion_Status CHECK (Status IN ('DRAFT','SUBMITTED','APPROVED','PUBLISHED','DISABLED','ARCHIVED'))
);
GO

-- -----------------------------------------------
-- 3.10.3 ParameterSet（参数集主表）
-- -----------------------------------------------
CREATE TABLE ParameterSet (
    Id                BIGINT IDENTITY(1,1) PRIMARY KEY,
    ParameterSetCode  NVARCHAR(50)  NOT NULL UNIQUE,
    ParameterSetName  NVARCHAR(200) NOT NULL,
    Description       NVARCHAR(1000) NULL,
    IsActive          BIT           NOT NULL DEFAULT 1,
    CreatedAt         DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy         NVARCHAR(100) NULL,
    UpdatedAt         DATETIME2     NULL,
    UpdatedBy         NVARCHAR(100) NULL
);
GO

-- -----------------------------------------------
-- 3.10.4 ParameterSetVersion（参数集版本表）
-- -----------------------------------------------
-- 红线：已发布版本不可原地修改。正式排程只允许 PUBLISHED 状态。
CREATE TABLE ParameterSetVersion (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    ParameterSetId  BIGINT         NOT NULL,
    VersionCode     NVARCHAR(50)   NOT NULL,
    Status          NVARCHAR(20)   NOT NULL DEFAULT 'DRAFT',
    EffectiveFrom   DATETIME2      NULL,
    EffectiveTo     DATETIME2      NULL,
    PublishedAt     DATETIME2      NULL,
    PublishedBy     NVARCHAR(100)  NULL,
    ApprovedAt      DATETIME2      NULL,
    ApprovedBy      NVARCHAR(100)  NULL,
    CreatedAt       DATETIME2      NOT NULL DEFAULT GETDATE(),
    CreatedBy       NVARCHAR(100)  NULL,
    CONSTRAINT FK_ParameterSetVersion_ParameterSet FOREIGN KEY (ParameterSetId) REFERENCES ParameterSet(Id),
    CONSTRAINT UQ_ParameterSetVersion UNIQUE (ParameterSetId, VersionCode),
    CONSTRAINT CK_ParameterSetVersion_Status CHECK (Status IN ('DRAFT','SUBMITTED','APPROVED','PUBLISHED','DISABLED','ARCHIVED'))
);
GO

-- -----------------------------------------------
-- 3.10.5 StrategyProfile（策略包主表）
-- -----------------------------------------------
CREATE TABLE StrategyProfile (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    StrategyProfileCode NVARCHAR(50)  NOT NULL UNIQUE,
    StrategyProfileName NVARCHAR(200) NOT NULL,
    Description         NVARCHAR(1000) NULL,
    RunType             NVARCHAR(50)  NULL,
    IsActive            BIT           NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy           NVARCHAR(100) NULL,
    UpdatedAt           DATETIME2     NULL,
    UpdatedBy           NVARCHAR(100) NULL,
    CONSTRAINT CK_StrategyProfile_RunType CHECK (
        RunType IS NULL OR RunType IN (
            'FULL_SCHEDULE','MANUAL_RESCHEDULE','LOCAL_RESCHEDULE',
            'SIMULATION','INSERT_ORDER_WHATIF'
        )
    )
);
GO

-- -----------------------------------------------
-- 3.10.6 StrategyProfileVersion（策略包版本表）
-- -----------------------------------------------
-- 关键表：将规则集版本和参数集版本组合为可发布、可追溯、可被 ScheduleRun 引用的策略包版本。
-- 红线：已发布版本不可原地修改。同一策略包下仅一个 IsDefault=1 的 PUBLISHED 版本。
CREATE TABLE StrategyProfileVersion (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    StrategyProfileId      BIGINT        NOT NULL,
    VersionCode            NVARCHAR(50)  NOT NULL,
    RuleSetVersionId       BIGINT        NOT NULL,
    ParameterSetVersionId  BIGINT        NOT NULL,
    Status                 NVARCHAR(20)  NOT NULL DEFAULT 'DRAFT',
    EffectiveFrom          DATETIME2     NULL,
    EffectiveTo            DATETIME2     NULL,
    IsDefault              BIT           NOT NULL DEFAULT 0,
    PublishedAt            DATETIME2     NULL,
    PublishedBy            NVARCHAR(100) NULL,
    ApprovedAt             DATETIME2     NULL,
    ApprovedBy             NVARCHAR(100) NULL,
    CreatedAt              DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy              NVARCHAR(100) NULL,
    CONSTRAINT FK_StrategyProfileVersion_Profile
        FOREIGN KEY (StrategyProfileId) REFERENCES StrategyProfile(Id),
    CONSTRAINT FK_StrategyProfileVersion_RuleSetVersion
        FOREIGN KEY (RuleSetVersionId) REFERENCES RuleSetVersion(Id),
    CONSTRAINT FK_StrategyProfileVersion_ParameterSetVersion
        FOREIGN KEY (ParameterSetVersionId) REFERENCES ParameterSetVersion(Id),
    CONSTRAINT UQ_StrategyProfileVersion UNIQUE (StrategyProfileId, VersionCode),
    CONSTRAINT CK_StrategyProfileVersion_Status
        CHECK (Status IN ('DRAFT','SUBMITTED','APPROVED','PUBLISHED','DISABLED','ARCHIVED'))
);
GO

-- 同一策略包下仅一个 PUBLISHED 默认版本
CREATE UNIQUE INDEX UQ_StrategyProfileVersion_DefaultPublished
ON StrategyProfileVersion(StrategyProfileId)
WHERE IsDefault = 1 AND Status = 'PUBLISHED';
GO

-- =============================================
-- 3.11 ScheduleRun 追加规则参数策略包引用（v5.0.45）
-- =============================================
ALTER TABLE ScheduleRun
ADD StrategyProfileVersionId BIGINT NULL;
GO

ALTER TABLE ScheduleRun
ADD CONSTRAINT FK_ScheduleRun_StrategyProfileVersion
    FOREIGN KEY (StrategyProfileVersionId) REFERENCES StrategyProfileVersion(Id);
GO

CREATE INDEX IX_ScheduleRun_StrategyProfileVersion
ON ScheduleRun(StrategyProfileVersionId);
GO

-- =============================================
-- =============================================
-- 3.12 ProcessCodeDict ERPProperty（v5.0.46 新增）
-- ERPProperty 已内建于 ProcessCodeDict CREATE TABLE（行 1314），无需重复 ALTER
-- =============================================

-- =============================================
-- 3.13 ERP Received 按单据汇总视图（v5.0.46 新增）
-- =============================================
-- ODS 层（MES_Integration）：ERP_Received_ByDocument_View
-- 负责：5号位。粒度：工厂+仓库+物料+单据类型+单据号
-- 不保留单据行号、SourceDocumentNo、SourceLineNo
USE [MES_Integration];
GO

-- v5.1.2：ERP_Received_ByDocument_View是V1正式事实接口，不再由通用DDL创建WHERE 1=0空骨架。
-- 5号位/ERP DBA必须按已冻结粒度“工厂+仓库+物料+单据类型+单据号”提供真实ODS View：
-- FactoryCode, WarehouseCode, MasterID, MaterialCode, DocumentType, DocumentNo,
-- ReceivedQty, LastReceivedAt, SourceUpdatedAt, IsActive。
-- 本通用DDL不猜ERP源表。

-- APS 层（APS_Production）：ext_ERP_Received_ByDocument_View
-- 负责：2号位。V1 不建本地快照，排程装载时通过本视图读取
USE [APS_Production];
GO

IF EXISTS (
    SELECT 1
    FROM [MES_Integration].sys.views v
    JOIN [MES_Integration].sys.schemas s ON s.schema_id = v.schema_id
    WHERE s.name = N'dbo' AND v.name = N'ERP_Received_ByDocument_View'
)
BEGIN
    EXEC(N'CREATE OR ALTER VIEW dbo.ext_ERP_Received_ByDocument_View AS
          SELECT FactoryCode, WarehouseCode, MasterID, MaterialCode,
                 DocumentType, DocumentNo, ReceivedQty,
                 LastReceivedAt, SourceUpdatedAt, IsActive
          FROM [MES_Integration].[dbo].[ERP_Received_ByDocument_View];');
END
ELSE
BEGIN
    PRINT N'V1_DEPLOY_BLOCKER: MES_Integration.dbo.ERP_Received_ByDocument_View尚未由5号位绑定真实ERP Received来源；未创建空包装视图。';
END;
GO

-- =============================================
-- 3.14 MES_APS_BOM_Workset_CrossFactoryEdge（ODS 跨厂交接边表 v5.0.46 新增）
-- =============================================
-- 负责：5号位。基于 StageDetail 生成，只记录发生跨厂的段。
USE [MES_Integration];
GO

CREATE TABLE dbo.MES_APS_BOM_Workset_CrossFactoryEdge (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchNo             NVARCHAR(50)  NOT NULL,            -- BOM 展开批次
    WorksetId           BIGINT        NOT NULL,            -- 对应 MES_APS_BOM_Workset 行
    BOMNO               NVARCHAR(50)  NULL,                -- BOM 号
    ParentMaterialCode  NVARCHAR(100) NOT NULL,            -- 父件物料
    ChildMaterialCode   NVARCHAR(100) NOT NULL,            -- 子件物料
    FromStageCode       NVARCHAR(50)  NOT NULL,            -- 发出大工艺
    FromFactoryCode     NVARCHAR(50)  NOT NULL,            -- 发出工厂
    ToStageCode         NVARCHAR(50)  NOT NULL,            -- 接收大工艺
    ToProcessCode       NVARCHAR(50)  NULL,                -- 接收工序/仓库码(可选)
    ToFactoryCode       NVARCHAR(50)  NOT NULL,            -- 接收工厂
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_CrossFactoryEdge_Batch
ON MES_APS_BOM_Workset_CrossFactoryEdge(BatchNo, WorksetId);
GO
-- =============================================
-- 3.17 sp_GenerateBOMCrossFactoryEdge（跨厂边生成存储过程，v5.0.46 新增）
-- =============================================
-- 负责：5号位。由 sp_EnrichBOMWorkset 末尾调用：EXEC dbo.sp_GenerateBOMCrossFactoryEdge @BatchNo；实时链路暂不强制
-- 输入：MES_APS_BOM_Workset_StageDetail（仅 EDGE）
-- 输出：MES_APS_BOM_Workset_CrossFactoryEdge
-- 红线：FromFactoryCode / ToFactoryCode 必须通过 StageDict.FactoryCode 取得，禁止截取 StageCode 前缀
CREATE OR ALTER PROCEDURE dbo.sp_GenerateBOMCrossFactoryEdge
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        DELETE FROM dbo.MES_APS_BOM_Workset_CrossFactoryEdge
        WHERE BatchNo = @BatchNo;

        ;WITH OrderedStage AS (
            SELECT
                sd.BatchNo, sd.WorksetId, sd.BOMNO,
                sd.ParentMaterialCode, sd.ChildMaterialCode,
                sd.StageCode AS FromStageCode,
                sd.StageSeq  AS FromStageSeq,
                LEAD(sd.StageCode) OVER (
                    PARTITION BY sd.WorksetId ORDER BY sd.StageSeq
                ) AS ToStageCode
            FROM dbo.MES_APS_BOM_Workset_StageDetail sd
            WHERE sd.BatchNo = @BatchNo
              AND sd.StageScopeType = 'EDGE'
              AND sd.WorksetId IS NOT NULL
        ),
        ResolvedFactory AS (
            SELECT
                os.*,
                sf.FactoryCode AS FromFactoryCode,
                st.FactoryCode AS ToFactoryCode
            FROM OrderedStage os
            INNER JOIN dbo.StageDict sf
                ON sf.StageCode = os.FromStageCode AND sf.IsActive = 1
            INNER JOIN dbo.StageDict st
                ON st.StageCode = os.ToStageCode   AND st.IsActive = 1
            WHERE os.ToStageCode IS NOT NULL
        )
        INSERT INTO dbo.MES_APS_BOM_Workset_CrossFactoryEdge (
            BatchNo, WorksetId, BOMNO,
            ParentMaterialCode, ChildMaterialCode,
            FromStageCode, FromFactoryCode,
            ToStageCode, ToProcessCode, ToFactoryCode,
            CreatedAt
        )
        SELECT
            BatchNo, WorksetId, BOMNO,
            ParentMaterialCode, ChildMaterialCode,
            FromStageCode, FromFactoryCode,
            ToStageCode, NULL AS ToProcessCode, ToFactoryCode,
            SYSUTCDATETIME()
        FROM ResolvedFactory
        WHERE FromFactoryCode <> ToFactoryCode;

        -- StageCode 未命中 StageDict 登记 Issues
        WITH OrderedStage AS (
            SELECT sd.BatchNo, sd.WorksetId, sd.BOMNO,
                   sd.ParentMaterialCode, sd.ChildMaterialCode,
                   sd.StageCode AS FromStageCode,
                   LEAD(sd.StageCode) OVER (
                       PARTITION BY sd.WorksetId ORDER BY sd.StageSeq
                   ) AS ToStageCode
            FROM dbo.MES_APS_BOM_Workset_StageDetail sd
            WHERE sd.BatchNo = @BatchNo AND sd.StageScopeType = 'EDGE' AND sd.WorksetId IS NOT NULL
        )
        INSERT INTO dbo.MES_APS_BOM_Workset_Issues (
            BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
            Produce, IssueType, Severity, Detail, DegradeAction, CreatedAt
        )
        SELECT os.BatchNo, os.BOMNO, os.ParentMaterialCode, os.ChildMaterialCode,
            NULL AS Produce,
            N'STAGE_DICT_NOT_FOUND' AS IssueType, N'WARN' AS Severity,
            CONCAT(N'CrossFactoryEdge 跳过：StageCode未命中StageDict。FromStageCode=',
                   COALESCE(os.FromStageCode,N'NULL'), N', ToStageCode=',
                   COALESCE(os.ToStageCode,N'NULL'), N', WorksetId=',
                   COALESCE(CAST(os.WorksetId AS NVARCHAR(20)), N'NULL')) AS Detail,
            N'CROSS_FACTORY_EDGE_SKIP' AS DegradeAction, SYSUTCDATETIME()
        FROM OrderedStage os
        LEFT JOIN dbo.StageDict sf ON sf.StageCode = os.FromStageCode AND sf.IsActive = 1
        LEFT JOIN dbo.StageDict st ON st.StageCode = os.ToStageCode   AND st.IsActive = 1
        WHERE os.ToStageCode IS NOT NULL AND (sf.StageCode IS NULL OR st.StageCode IS NULL);

        INSERT INTO dbo.MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
        VALUES (
            @BatchNo,
            CONCAT(N'sp_GenerateBOMCrossFactoryEdge SUCCESS: CrossFactoryEdge 生成完成，批 ', @BatchNo),
            SYSUTCDATETIME()
        );

    END TRY
    BEGIN CATCH
        DECLARE @Err NVARCHAR(MAX) = ERROR_MESSAGE();
        INSERT INTO dbo.MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
        VALUES (
            @BatchNo,
            CONCAT(N'sp_GenerateBOMCrossFactoryEdge FAILED: ', @Err),
            SYSUTCDATETIME()
        );
        THROW;
    END CATCH
END;
GO

-- =============================================
-- 3.15 APS_BOM_CROSS_FACTORY_EDGE_RAW（APS 跨厂边缓存表 v5.0.46 新增）
-- =============================================
-- 负责：2号位。从 ODS 搬运，供 Pegging 直接读取。
USE [APS_Production];
GO

CREATE TABLE dbo.APS_BOM_CROSS_FACTORY_EDGE_RAW (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchNo             NVARCHAR(50)  NOT NULL,
    WorksetId           BIGINT        NOT NULL,
    BOMNO               NVARCHAR(50)  NULL,
    ParentMaterialCode  NVARCHAR(100) NOT NULL,
    ChildMaterialCode   NVARCHAR(100) NOT NULL,
    FromStageCode       NVARCHAR(50)  NOT NULL,
    FromFactoryCode     NVARCHAR(50)  NOT NULL,
    ToStageCode         NVARCHAR(50)  NOT NULL,
    ToProcessCode       NVARCHAR(50)  NULL,
    ToFactoryCode       NVARCHAR(50)  NOT NULL,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_APS_CrossFactoryEdge_Batch
ON dbo.APS_BOM_CROSS_FACTORY_EDGE_RAW(BatchNo, WorksetId);
GO

-- =============================================
-- 3.18 MES_APS_BOM_Workset_CrossFactoryEdge_Realtime（实时跨厂交接边表 v5.1.0 新增）
-- =============================================
-- 负责：5号位
-- 说明：业务语义与批量表 dbo.MES_APS_BOM_Workset_CrossFactoryEdge 对齐；
--       实时表使用 RequestDetailId 替代 BatchNo 作为隔离键；
--       只表达结构事实，不判断 STAGE_HANDOFF / INTER_FACTORY_ORDER；
--       0 行合法；不生成 Task、不写 Pegging。
USE [MES_Integration];
GO

CREATE TABLE dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    RequestDetailId     BIGINT        NOT NULL,           -- 实时请求隔离键
    WorksetId           BIGINT        NOT NULL,           -- 逻辑关联 MES_APS_BOM_Workset_Realtime.Id
    BOMNO               NVARCHAR(50)  NULL,
    ParentMaterialCode  NVARCHAR(100) NOT NULL,
    ChildMaterialCode   NVARCHAR(100) NOT NULL,
    FromStageCode       NVARCHAR(50)  NOT NULL,
    FromFactoryCode     NVARCHAR(50)  NOT NULL,
    ToStageCode         NVARCHAR(50)  NOT NULL,
    ToProcessCode       NVARCHAR(50)  NULL,
    ToFactoryCode       NVARCHAR(50)  NOT NULL,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

CREATE INDEX IX_CrossFactoryEdge_RT_RequestDetail
ON dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime(RequestDetailId);
GO

CREATE INDEX IX_CrossFactoryEdge_RT_Workset
ON dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime(WorksetId);
GO

CREATE INDEX IX_CrossFactoryEdge_RT_BOMNO
ON dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime(BOMNO);
GO

-- =============================================
-- 3.19 sp_GenerateBOMCrossFactoryEdgeRealtime（实时跨厂边生成 SP v5.1.0 新增）
-- =============================================
-- 负责：5号位。由 sp_EnrichBOMWorksetRealtime 在 Step 5 日志前调用。
-- 逻辑与批量 sp_GenerateBOMCrossFactoryEdge 一致：
--   StageDetail_Realtime(EDGE) → LEAD 窗口函数 → StageDict.FactoryCode
-- 红线：
--   1) @RequestDetailId 必须非空
--   2) 先删除该 RequestDetailId 旧结果，保证幂等
--   3) 通过 WorksetId JOIN MES_APS_BOM_Workset_Realtime 并过滤 w.RequestDetailId=@RequestDetailId
--   4) FromFactoryCode / ToFactoryCode 必须通过 StageDict.FactoryCode 取得，禁止截取 StageCode 前缀
--   5) 只插入 FromFactoryCode <> ToFactoryCode 的边
--   6) StageDict 未命中时写 STAGE_DICT_NOT_FOUND WARN，Issue 切片 RT:RD:{RequestDetailId}，不生成边
--   7) 0 行合法，0 行不抛异常
--   8) SP 异常必须 THROW
CREATE OR ALTER PROCEDURE dbo.sp_GenerateBOMCrossFactoryEdgeRealtime
    @BOMNO           NVARCHAR(50),
    @RequestDetailId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    IF @RequestDetailId IS NULL
    BEGIN
        THROW 50000, N'sp_GenerateBOMCrossFactoryEdgeRealtime: @RequestDetailId 不允许为 NULL', 1;
    END

    DECLARE @SyntheticBatch NVARCHAR(50) = CONCAT(N'RT:RD:', CAST(@RequestDetailId AS NVARCHAR(20)));

    BEGIN TRY
        -- 幂等：清理本请求旧结果
        DELETE FROM dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime
        WHERE RequestDetailId = @RequestDetailId;

        -- 幂等：清理本请求上次的 STAGE_DICT_NOT_FOUND Issues（只清理本 SP 的 Issue）
        DELETE FROM dbo.MES_APS_BOM_Workset_Issues
        WHERE RequestDetailId = @RequestDetailId
          AND IssueType = N'STAGE_DICT_NOT_FOUND'
          AND DegradeAction = N'CROSS_FACTORY_EDGE_SKIP';

        -- 生成跨厂边
        ;WITH OrderedStage AS (
            SELECT
                w.RequestDetailId,
                sd.WorksetId,
                sd.BOMNO,
                sd.ParentMaterialCode,
                sd.ChildMaterialCode,
                sd.StageCode AS FromStageCode,
                sd.StageSeq  AS FromStageSeq,
                LEAD(sd.StageCode) OVER (
                    PARTITION BY sd.WorksetId ORDER BY sd.StageSeq
                ) AS ToStageCode
            FROM dbo.MES_APS_BOM_Workset_StageDetail_Realtime sd
            INNER JOIN dbo.MES_APS_BOM_Workset_Realtime w
                ON w.Id = sd.WorksetId
               AND w.RequestDetailId = @RequestDetailId
            WHERE sd.StageScopeType = 'EDGE'
              AND sd.WorksetId IS NOT NULL
        ),
        ResolvedFactory AS (
            SELECT
                os.*,
                sf.FactoryCode AS FromFactoryCode,
                st.FactoryCode AS ToFactoryCode
            FROM OrderedStage os
            INNER JOIN dbo.StageDict sf
                ON sf.StageCode = os.FromStageCode AND sf.IsActive = 1
            INNER JOIN dbo.StageDict st
                ON st.StageCode = os.ToStageCode   AND st.IsActive = 1
            WHERE os.ToStageCode IS NOT NULL
        )
        INSERT INTO dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime (
            RequestDetailId, WorksetId, BOMNO,
            ParentMaterialCode, ChildMaterialCode,
            FromStageCode, FromFactoryCode,
            ToStageCode, ToProcessCode, ToFactoryCode,
            CreatedAt
        )
        SELECT
            RequestDetailId, WorksetId, BOMNO,
            ParentMaterialCode, ChildMaterialCode,
            FromStageCode, FromFactoryCode,
            ToStageCode, NULL, ToFactoryCode,
            GETDATE()
        FROM ResolvedFactory
        WHERE FromFactoryCode <> ToFactoryCode;

        -- StageDict 未命中登记 Issues
        ;WITH OrderedStage AS (
            SELECT
                w.RequestDetailId,
                sd.WorksetId,
                sd.BOMNO,
                sd.ParentMaterialCode,
                sd.ChildMaterialCode,
                sd.StageCode AS FromStageCode,
                LEAD(sd.StageCode) OVER (
                    PARTITION BY sd.WorksetId ORDER BY sd.StageSeq
                ) AS ToStageCode
            FROM dbo.MES_APS_BOM_Workset_StageDetail_Realtime sd
            INNER JOIN dbo.MES_APS_BOM_Workset_Realtime w
                ON w.Id = sd.WorksetId
               AND w.RequestDetailId = @RequestDetailId
            WHERE sd.StageScopeType = 'EDGE'
              AND sd.WorksetId IS NOT NULL
        )
        INSERT INTO dbo.MES_APS_BOM_Workset_Issues (
            BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
            IssueType, Severity, Detail, DegradeAction, RequestDetailId, CreatedAt
        )
        SELECT
            @SyntheticBatch,
            os.BOMNO, os.ParentMaterialCode, os.ChildMaterialCode,
            N'STAGE_DICT_NOT_FOUND', N'WARN',
            CONCAT(N'Realtime CrossFactoryEdge 跳过：StageCode 未命中 StageDict。FromStageCode=',
                   COALESCE(os.FromStageCode, N'NULL'),
                   N', ToStageCode=',
                   COALESCE(os.ToStageCode, N'NULL'),
                   N', WorksetId=',
                   COALESCE(CAST(os.WorksetId AS NVARCHAR(20)), N'NULL')),
            N'CROSS_FACTORY_EDGE_SKIP',
            os.RequestDetailId,
            GETDATE()
        FROM OrderedStage os
        LEFT JOIN dbo.StageDict sf
            ON sf.StageCode = os.FromStageCode AND sf.IsActive = 1
        LEFT JOIN dbo.StageDict st
            ON st.StageCode = os.ToStageCode   AND st.IsActive = 1
        WHERE os.ToStageCode IS NOT NULL
          AND (sf.StageCode IS NULL OR st.StageCode IS NULL);

        INSERT INTO dbo.MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
        VALUES (
            NULL,
            CONCAT(N'sp_GenerateBOMCrossFactoryEdgeRealtime SUCCESS: BOM=', @BOMNO,
                   N', RequestDetailId=', CAST(@RequestDetailId AS NVARCHAR(20))),
            GETDATE()
        );
    END TRY
    BEGIN CATCH
        DECLARE @Err NVARCHAR(MAX) = ERROR_MESSAGE();
        INSERT INTO dbo.MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
        VALUES (
            NULL,
            CONCAT(N'sp_GenerateBOMCrossFactoryEdgeRealtime FAILED: BOM=', @BOMNO,
                   N', RequestDetailId=', CAST(@RequestDetailId AS NVARCHAR(20)),
                   N', Err=', @Err),
            GETDATE()
        );
        THROW;
    END CATCH
END;
GO

-- 切回 APS_Production 上下文
USE [APS_Production];
GO

-- =============================================
-- 数据库DDL脚本结束
-- =============================================

-- =============================================
-- v5.1.2 V1部署前Timed Supply / Received契约校验（手工/CI执行）
-- =============================================
-- 说明：源系统物理View由5号位/ERP/采购DBA负责，本通用DDL不虚构源表。
-- 以下校验用于阻止“来源没接通却当0行业务数据继续跑”。
USE [APS_Production];
GO
CREATE OR ALTER PROCEDURE dbo.sp_ValidateV1FrozenDataContracts
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.ext_PipelineSupply_Source_View', N'V') IS NULL
        THROW 51101, 'V1 deployment blocker: ext_PipelineSupply_Source_View is missing', 1;

    IF OBJECT_ID(N'dbo.ext_ERP_Received_ByDocument_View', N'V') IS NULL
        THROW 51102, 'V1 deployment blocker: ext_ERP_Received_ByDocument_View is missing', 1;

    -- 注意：0行本身不报错，因为某时点确实可以没有在途/Received。
    -- “是否真实绑定”应由部署清单核对View定义来源，不再通过WHERE 1=0占位实现。
    SELECT N'OK' AS FrozenContractStatus;
END;
GO

-- =============================================
-- v5.1.2 结束
-- =============================================

