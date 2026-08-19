# APS 数据库表结构字段说明文档（完整版）

**版本**：v5.0.46  
**日期**：2026-06-23  
**目的**：供全体开发团队（0-5号位）参考的完整数据库设计文档  
**基于：DDL v5.0.46；相关文档：演变总表 v3.29 / 防腐层 v1.33**  
**适用人员**：0号位（业务负责人）、1-5号位（开发团队）、DBA

**相关文档**：
- **《APS_各类基础数据分层承接与演变总表_v5.0》**（当前 v3.29）：数据演进全景图（架构总纲），建议先读此文档理解数据流向
- **《APS_数据架构与防腐层设计方案_v5.0》**（当前 v1.33）：防腐层设计、存储过程、数据管道详细说明
- **《APS_权限与审批系统实施方案_v1.0》**：权限与审批系统设计方案
- **《APS_Auth数据库字段说明文档_v1.0》**：APS_Auth数据库详细字段说明

**⚖️ 重要更新（v5.0.46）**（2026-06-23 跨厂Pegging补强 + 规则与参数引擎）：
- 🆕 **新增 ProcessCodeDict.ERPProperty**：仓库/工序位置业务属性（M/XC/ZP/BP）；MES_ProcessCode_View 透出
- 🆕 **新增 ERP_Received_ByDocument_View / ext_ERP_Received_ByDocument_View**：ERP Received 按单据汇总 ODS契约视图 + APS跨库包装视图
- 🆕 **新增 MES_APS_BOM_Workset_CrossFactoryEdge / APS_BOM_CROSS_FACTORY_EDGE_RAW**：BOM跨厂交接边表（ODS+APS）
- 🆕 **新增 PeggingSupplyAllocation**：Pegging供给分配细账表（库存/在途/Received等非Task供给）；澄清物理 Pegging 表为 Task-to-Task 血缘表
- 🆕 **新增规则与参数引擎6张表**：RuleSet/RuleSetVersion/ParameterSet/ParameterSetVersion/StrategyProfile/StrategyProfileVersion
- 🔄 **ScheduleRun**：新增 StrategyProfileVersionId（策略包版本绑定）
- 📌 **SupplyFact_Pipeline**：支持 MOCK 数据联调（SourceSystem='MOCK'）

**⚖️ 重要更新（v5.0.42）**（2026-06-15 管道供给链路完整骨架 + ODS空契约视图 + 字段契约锁定）：
- 🆕 **新增 §1.9 `ERP_InterplantInTransit_View`（ODS 层）**：14字段 ERP 厂间在途 ODS 契约视图；V1 WHERE 1=0 返回0行
- 🆕 **新增 §1.9a `ext_ERP_InterplantInTransit_View`（APS 层）**：APS 跨库包装视图，显式列字段，禁止 SELECT *
- 🔄 **§6.6 SupplyFact_Pipeline**：新增 4 个来源追溯字段（SourceMasterID / SourceFactoryCode / SourceDocumentLineNo / SourceUpdatedAt）
- 🔄 **§6.7 SupplyAvailabilityRule**：V1 不调用声明补强；V1.1/V2 TODO 口径修正
- 📌 **分层语义统一**：ODS视图=「ODS层/MES_Integration/来源ERP/5号位」；APS包装视图=「APS层/APS_Production/2号位」
- 📌 **契约锁定规则**：ODS 契约视图字段结构为强契约；V1.1/V2 允许调整视图内部实现逻辑，对外14字段投影不变

**⚖️ 重要更新（v5.0.41）**（2026-06-12 MES生产进度快照三表 + 三个同步SP，对齐演变总表 v3.26 / 防腐层 v1.30）：
- 🆕 **新增 §九 MES生产进度快照**：`MESWorkOrderSnapshot`（§9.1）+ `OperationProgressSnapshot`（§9.2）+ `StageProgressSnapshot`（§9.3）
- 📌 **V1 工序识别主字段**：`OperationName`，不以 MES 工序编码为主匹配字段
- 📌 **Task/Pegging 全量重算口径**：MES 进度不匹配历史 TaskId；Pegging 不跨版本复用
- 📌 **EAM V1 预留**：字段说明文档不新增 EAM 相关表，占位声明见防腐层设计方案 §2.9

**⚖️ 重要更新（v5.0.40）**（2026-06-08 可用供给明细层新增 + DDL 建表顺序修复）：
- 🔧 **DDL 建表顺序修复**：`ProductFamily`/`Factory` 提前至 §2.4a，修复 `ProductionDepartment`/`InventoryAvailabilityRule` 外键引用顺序失败问题
- 🔆 **新增 `InventoryAvailableSupplyDetail`（§6.4.5）**：规则命中后、余额汇总前的可用库存明细层；保留 `AvailabilityRuleId`+`RulePriority`+`InventorySupplyCandidateId`；`InventoryBalance` 从该表汇总生成
- 🔄 **库存六层架构定稿**：事实→候选→规则→明细→余额→内存
- ⚠️ **v5.0.23 changelog 修正**：`SupplyFact_Pipeline` 描述补充“六层”说明；`SupplyAvailabilityRule` 去掉已删旧表引用，补 V1 不调用声明

**⚖️ 重要更新（v5.0.39）**（2026-05-31 库存规则 V1口径收敛）：
- 🗑️ **删除 `ProductFamilyInventoryScope`（旧§7.1）**：从当前版本正式删除
- 🗑️ **删除 `InventorySourceRule`（旧§7.2）**：从当前版本正式删除
- 🗑️ **删除 `InventorySourcePriority`（旧§7.3）**：v2.8已废弃，同步从文档删除
- 🆕 **新增 `InventoryAvailabilityRule`（§7.1）**：统一库存可用规则表；`IsAvailable`+`Priority` 替代旧 `RuleAction(PREFER/EXCLUDE)`
- 🔄 **`InventoryFact_ERP`（§6.2）**：新增 `FactoryCode`；字段名 `Warehouse`→`WarehouseCode`
- 🔄 **`InventoryFact_MES`（§6.3）**：新增 `WarehouseCode`（V1主链）+`FactoryCode`；`Location` 字段名历史保留
- 🔄 **`InventoryBalance.ProductFamilyId`（§6.5）**：口径修正 = 库存使用上下文，非物料自身产品族
- 🔄 **`InventoryBalance.BatchNo`（§6.5）**：口径修正 = 快照批次标签，非订单消耗记录
- 🆕 **`sp_SyncInventorySnapshot(@BatchNo)` 六步 ETL**：新建库存快照同步存储过程
- 🆕 **库存链路包装视图**：`ext_ERP_Inventory_View` / `ext_MES_Inventory_View` / `ext_ERP_InterplantInTransit_View`（V1 空链路预留）
- 📌 **FactoryId 红线修正**：库存/供给事实+规则+余额表均可持有 `FactoryId FK`
- 📌 **InventoryAllocationResult**：V1.1/V2 预留，V1 不建表

**⚖️ 重要更新（v5.0.38）**（2026-05-30 产品族 V1 口径修正）：
- 🔄 **`ERP_Master_View` 契约升级 v1.5**：新增 `IsProductFamilyRequired`（BIT）/ `ProductFamilyCode`（NULLABLE）/ `FamilyResolveStatus`（NOT NULL）三字段；5号位在 ODS 内部判断（可读取 ERP ModelSort），不暴露 ERP 原始字段
- 🔄 **`MES_Material_View` 契约升级 v1.5**：同名三字段，MES 侧 V1 固定返回 `0 / NULL / 'NOT_REQUIRED'`
- 🔄 **`ext_ERP_Master_View` / `ext_MES_Material_View`**：显式列列表，透传三新字段
- 🔄 **`FamilyResolveStatus` 值域升级**：RESOLVED / NOT_REQUIRED / NO_RULE / AMBIGUOUS / FAMILY_CODE_NOT_FOUND / SOURCE_FIELD_MISSING
- 🔄 **`sp_SyncMasterData` 步骤1c 四规则**：规则1 NOT_REQUIRED→NULL；规则2 RESOLVED→码表映射；规则3 NO_RULE/AMBIGUOUS/SOURCE_FIELD_MISSING→ETL Issue；观则4 FAMILY_CODE_NOT_FOUND→ETL Issue
- 🔄 **`Material.ProductFamilyId`**：允许为空；仅对 IsProductFamilyRequired=1 且 FamilyResolveStatus='RESOLVED' 的物料写入
- ⚠️ **§4.7/4.8/4.9 ODS 三表**：均改标记为 V2 预留，V1 不建
- 📌 **设计决策**：V1 不新增 MaterialProductFamilyScopeRule；`sp_ResolveMaterialProductFamily` V2 预留；APS 层禁止保存 ModelSort/ProcessCode
- 🛑 **红线**：`Order.ProductFamilyId` 从 `Material.ProductFamilyId` 继承，禁止在订单层另建解析链

**⚖️ 重要更新（v5.0.32）**（2026-05-25 RequestDetail字段收敛，对齐 DDL v5.0.32）：
- 🗑️ **删除 `MES_API_BOM_Request_Detail.Model`**：5号位不再依赖 ERP 原始 Model；Model 只保留在 `ERP_Order_Staging.Model` / `Order_Canonical.SourceModel`
- 🗑️ **删除 `MES_API_BOM_Request_Detail.OrderStagingId`**：ERP_Order_Staging 仅作同步缓冲层；BOM主链锚点已确定为 `OrderCanonicalId`
- 🗑️ **删除 `MES_API_BOM_Request_Detail.ResolvedBOMNO`**：本表定位为纯 BOM 请求输入表；Workset 解析结果归 `OrderBomRequestLink.ResolvedBOMNO`（由 2号位写入）
- 🔄 **`sp_ExpandBOMBatch_vNext` 删除步骤5a**：不再回写 `ResolvedBOMNO` 到 RequestDetail
- 📌 **职责分离**：5号位只写 `MES_APS_BOM_Workset`/`StageDetail`/`Issues`；2号位从 Level=1 `Workset.BOMNO` 取值写入 `OrderBomRequestLink.ResolvedBOMNO`
- 📌 **最终字段**：Id / BatchNo / OrderCanonicalId / OrderNo / SourceSystem / SourceOrderId / MaterialCode / FactoryCode / OrderType / RequestedBOMNO / CreatedAt

**⚖️ 重要更新（v5.0.31）**（2026-05-25 Order→BOM追溯链闭合，对齐 DDL v5.0.31）：
- 🔄 **表结构变更**：`MES_API_BOM_Request_Detail` 核心锚点从 `OrderStagingId` 升级为 `OrderCanonicalId`（`Order_Canonical` 是 APS 防腐层核心表，跨库逻辑引用，无FK约束）
- 🔄 **字段改名**：`BOMNO` → `RequestedBOMNO`（订单原始携带值）；🆕 **新增** `OrderNo`、`SourceSystem`、`SourceOrderId`
- 🔄 **唯一约束**：`UQ_BOMRequestDetail_BatchOrder(BatchNo, OrderStagingId)` → `UQ_BOMRequestDetail_BatchCanonical(BatchNo, OrderCanonicalId)`
- 🆕 **新增 §1.2b `OrderBomRequestLink`**（APS本地订单-BOM解析结果索引表）：`PlanVersionId/OrderId(NULL允许)/OrderCanonicalId/RequestDetailId/ResolvedBOMNO/RepWorksetId/LinkStatus`；UNIQUE(PlanVersionId, **OrderCanonicalId**)（v5.0.34 升级）；4个查询索引
- 📌 **查询链路收敛**：Order→`OrderBomRequestLink.ResolvedBOMNO`→`APS_BOM_RAW`（BOM结构）；Order→`OrderBomRequestLink.RepWorksetId`→`APS_BOM_STAGE_PATH_RAW.WorksetId`（大工艺路径）
- 📌 **设计决策**：`APS_BOM_RAW` 保持BOMNO级共享（不订单化）；`APS_BOM_STAGE_PATH_RAW` 无需改动

**⚖️ 重要更新（v5.0.28）**（2026-05-21 BOM入口分流R28/R29/R30/R31，对齐BOM_Workset方案v1.7）：
- 🔄 **§1.5 IssueType 新增5条**（BOM入口阶段 Issues，v5.0.28对齐 sp_ExpandBOMBatch_vNext Stage B/C）：
  - `BOM_ENTRY_NOT_FOUND`（ERROR）：BOMNO=NULL时无BOM入口，展开跳过
  - `BOM_ENTRY_AMBIGUOUS`（WARN）：BOMNO=NULL时多BOMNO候选，取Rank=1
  - `MISSING_MATERIALCODE`（ERROR）：RequestDetail.MaterialCode 为空，无法解析 BOM 入口（v5.0.32：Model 已删除）
  - `ORDER_TYPE_UNKNOWN`（WARN）：OrderType未知，按默认入口规则处理
  - `BOMNO_MISSING_PRODUCTION`（WARN/ERROR）：**R31新增**；PRODUCTION_INSTRUCTION+BOMNO=NULL；WARN=找到候选，ERROR=未找到

**⚖️ 重要更新（v5.0.27）**（2026-05-16 订单提升链路重构，对齐 sp_ValidateAndPromoteOrders v3）：
- 🆕 **MaterialMapping**: +`SourceModel NVARCHAR(100) NULL`（ERP原始型号，用于Step 0b Model→MaterialCode解析链）+ `IX_MaterialMapping_SourceModel` 索引
- 🔄 **ERP_Order_Staging**: `MaterialCode` NOT NULL→NULL（SP三级解析链写入前可为空）；+`Model`（ERP原始型号透传）；+`CustomerCode`（ERP原始客户代码）；+`RawNonStockShipmentType`/`RawOrderSource`（ERP原始值，标准化后→Canonical）
- 🆕 **Order_Canonical**: +`SourceModel`（ERP原始型号追溯）；+`NonStockShipmentType`（APS标准化：`FULL_PURPLE_SLIP/DIFF_PURPLE_SLIP/UNKNOWN`）；+`OriginalOrderSource`（APS标准化：`DAT/PO/UNKNOWN`）
- 🔄 **sp_ValidateAndPromoteOrders 全量重写**：
  - `#TargetStagingIds` 锁定本批次ID，防并发误操作
  - MaterialCode三级解析链：SourceMasterID→MaterialMapping / Model→MaterialMapping(SourceModel) / EmergencyOverride
  - Model一对多→`MATERIAL_MAPPING_AMBIGUOUS` FAILED；OrderType未知→`ORDER_TYPE_UNKNOWN` FAILED
  - `FailureCode` 单值最高优先级；`BOMNO_MISSING` 非阻断诊断（订单仍 PROCESSED）
  - `CustomerSegment` 口径收口：CustomerCode为空→NULL；CustomerCode有值但CustomerCodeMap无匹配→`UNKNOWN`（⚠️ 不再默认OVERSEAS）+ `CUSTOMER_SEGMENT_UNKNOWN` 诊断追加ErrorMessage（不阻断）
  - `DemandMaturityStatus` V1严格NULL：禁止从任何字段临时推断
  - `@OnlyPending` 参数删除（V1只处理PENDING）
- 🔄 **ERP_Order_Staging**: `FactoryCode` NOT NULL→NULL（V1 TODO桩，允许NULL透传进Canonical；不迫使ERPOrderSyncService写占位值；V2补规则转换）
- 🔄 **CustomerCodeMap表注释修正**：废止旧"失效→OVERSEAS"口径；IsActive=0行在JOIN时被过滤（`AND ccm.IsActive=1`）；订单提升时CustomerCode有值但无有效匹配→`UNKNOWN`；不默认OVERSEAS

**⚖️ 重要更新（v5.0.26）**（2026-05-14 BOM防腐层物化边表架构调整，对齐演变总表 v3.18）：
- 🆕 **新增 §4.1b `MES_BOM_Edge_Active`**（物化BOM防腐边表，V1兼任合同层+执行优化层）：17字段，4索引，专为 sp_ExpandBOMBatch_vNext WHILE循环每层JOIN优化
- 🆕 **新增 §4.1c `MES_BOM_Edge_RefreshLog`**（刷新控制日志表）：9字段；FAILED时阻止 Workset 消费半刷新数据
- ⚠️ **§4.1 `MES_BOM_View` 降为兼容视图**：不再直接 UNION ERP/MES 源表，改为 `SELECT * FROM MES_BOM_Edge_Active` 兼容包装；sp_ExpandBOMBatch_vNext 禁止对此视图做递归CTE
- ▸ **§1.3b StageDetail + Archive/Realtime 变体 + §2.1b APS_BOM_STAGE_PATH_RAW**：+1列 `WorksetId BIGINT NULL`（FK->Workset.Id；级联追溯锚点；sp_CleanupBOMWorkset 级联清理用；NULL=兼容旧批次）；StageDetail 主表+Realtime 各增 WorksetId 索引
- ▸ **§1.4 `MES_APS_BOM_Workset_Archive`** +1列 `RequestDetailId BIGINT NULL`（归档时从Workset透传，直查来源）
- ▸ **§2.2 `MES_APS_BOM_Workset_Realtime`** +1列 `RequestDetailId BIGINT NULL`（实时插单来源直接可追溯）
- 【设计决策】RequestDetailId **不进** StageDetail 表；经 WorksetId→Workset.RequestDetailId 反查即可，避免冗余
- 【设计决策】V1 MES_BOM_Edge_Active 兼任合同层+执行优化层；V2视需要拆出 MES_BOM_Edge_Contract

**⚖️ 重要更新（v5.0.25）**（2026-05-13 阶段二三接缝：运行编排+Scenario骨架+原因事实+读模型）：
- 🆕 **§4.2 PlanVersion 架构关系说明补充**：新增与 `ScheduleRun` 关系的注解；`Status` 值域补 CANDIDATE（仿真/WHATIF/MANUAL 产出默认态）
- 🆕 **§4.5 ScheduleRun**（排程运行编排表）：统一包装所有运行类型（FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF）；**阶段一即用**
- 🆕 **§4.6 Scenario**（仿真场景表）：what-if 假设+目标容器；**阶段二预留骨架**
- 🆕 **§4.7 SimulationRun**（仿真算法执行记录表）：Scenario 下具体算法执行元数据；**阶段二预留骨架**
- 🆕 **§4.8 ScenarioObjectiveScore**（场景多目标评分表）：多方案比较评分，依赖 PlanKpiSummary；**阶段二预留骨架**
- 🆕 **§5.2 ExplainTrace 职责边界注解**：写死 `ExplainTrace`（现有，轻量 Task 级追踪日志）≠ `ScheduleExplanationFact`（v5.0.25 新增，结构化原因事实层）共存不替代
- 🆕 **新增 §八、排程运行编排与结果读模型（阶段一骨架 + 阶段二预留）**：
  - §八.1 ScheduleExplanationFact（结构化原因事实，1号位产出 ExplanationFactDraft，2号位落库）
  - §八.2 OrderScheduleSummary（订单级摘要读模型）
  - §八.3 ResourceLoadSummary（资源×日期读模型）
  - §八.4 PlanKpiSummary（版本级 KPI 读模型）

**⚖️ 重要更新（v5.0.24）**（2026-05-13 OrderType重构+衍生字段澄清+DelayStatus新增）：
- 🔄 **OrderType 枚举重分类**：`SO/MTO → SALES_ORDER`；`MTS/SS/SS_U → PRODUCTION_INSTRUCTION`；由 `sp_ValidateAndPromoteOrders` 根据 ZPQF 映射，适用三表（ERP_Order_Staging / Order_Canonical / Order）及 MES_API_BOM_Request_Detail
- 🆕 **新增表** `CustomerCodeMap`：APS本地维护的客户编码映射表（来源：CustomerCode.xlsx）；`sp_ValidateAndPromoteOrders` 通过此表推导 `CustomerSegment`；非ODS共享字典，不通过视图对外暴露
- 🔄 **CustomerSegment 来源澄清**：値域扩展为 `JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER`；~~失效客户（IsActive=0）统一给 `OVERSEAS`；无匹配默认 `OVERSEAS`~~（⚠️ **v5.0.27已废止此口径**：IsActive=0行不参与派生；CustomerCode有值但无IsActive=1有效匹配→`UNKNOWN`；当前权威口径以v5.0.27为准）
- 🔄 **DemandMaturityStatus 收窄**：值域由三值收为两值 `PRE_CONFIRMED/FORECAST`；原 `DELAYED` 已拆出为独立字段 `DelayStatus`（禁止混用）
- 🆕 **新增字段** `DelayStatus`（三表同步新增）：`ON_TIME/FIRST_DELAY/REPEATED_DELAY`；V1简化：FIRST_DELAY/REPEATED_DELAY 暂不区分，超期均记为 `FIRST_DELAY`
- 🔄 **CustomerTier 等级关系补充**：`VIP > KEY_ACCOUNT > STANDARD > GENERAL`；当前主要启用 VIP/GENERAL 两档，KEY_ACCOUNT/STANDARD 预留
- 🔄 **Routing视图 SourceSystem 字段**：`MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` 补充 `SourceSystem`（追溯增强字段，非运行必需，与 MES_BOM_View 模式对齐）

**⚖️ 重要更新（v5.0.23）**（2026-05-09 管道供给链 新增两张表）：
- 🆕 **新增表** `SupplyFact_Pipeline`：APS 本地标准化供给事实层（允许少量本地派生字段）；并行于现货库存主链（v5.0.40 后为六层）；当前来源：ERP 厂间在途；`InventoryBalance` 定义不变
- 🆕 **新增表** `SupplyAvailabilityRule`：供给主题独立规则表（仅负责管道供给主题；现货链统一使用 `InventoryAvailabilityRule`，旧 `ProductFamilyInventoryScope`/`InventorySourceRule` 已于 v5.0.39 删除）；**⚠️ V1 不调用本表**，为 V1.1/V2 激活预留
- 📌 **设计决策**：`ETA`=ODS原始事实；`AvailableTime`=本地派生（ETA+LeadTimeOffset）；`BatchNo` nullable 支持夜间快照

**⚖️ 重要更新（v5.0.22）**（2026-05-09 MES_API_BOM_Request_Detail 补 OrderType）：
- 🆕 **新增字段**：`MES_API_BOM_Request_Detail.OrderType NVARCHAR(20) NULL`（从 ERP_Order_Staging.OrderType 透传）
- 📌 **设计决策**：BOMNO=NULL 时，5 号位据 OrderType 区分生产指示 vs 客户订单，选取不同 BOM 入口规则；OrderType 不参与唯一约束

**⚖️ 重要更新（v5.0.21）**（2026-05-08 订单BOM入口解析重构）：
- 🔄 **表结构变更**：`MES_API_BOM_Request_Detail`升级为订单级粒度；`ERP_Order_Staging`/`Order_Canonical` BOMNO改可空
- 🆕 **新增字段**：ERP_Order_Staging 新增 `FailureCode`（失败原因维度）+ `NextActionCode`（后续动作维度）两个独立字段
- 🆕 **新增字段**：`MES_APS_BOM_Workset` + `Issues` 新增 `RequestDetailId`（来源追溯锚点）
- 🆕 **新增3张急单临时桥接表**：`OrderEmergencyMaterialOverride` / `OrderEmergencyBomWorkset` / `OrderEmergencyBomStageDetail`
- 📌 **设计决策**：BOM入口解析业务分流归属5号位Workset处理阶段；2号位只做最基础字段透传

**⚖️ 重要更新（v5.0.20）**（2026-05-06 StageDict 阶段细化拆分）：
- 🆕 **新增 5 种 StageCategory**：MOLD(注塑) / CAST(铸造) / DRAW(冷拔) / FORGE(锻造) / EXTRU(型材押出)
- 🆕 **新增 7 条 StageCode**（总数 22→29）：BJ_MOLD / BJ_CAST / BJ_DRAW / BJ_FORGE / BJ_EXTRU / TJ_CAST / CN_FORGE
- 🔄 **PAINT→SURF 重命名**：BJ_PAINT→BJ_SURF / CN_PAINT→CN_SURF / TJ_PAINT→TJ_SURF（表面处理 = 涂装+氧化+喷丸）
- 🔄 **ProcessCodeDict 24 条 StageCode 改映射**：按 Process 列关键词自动推断（注塑×7/铸造×12/锻造×3/型材×2）
- 🔄 **MACH 语义收窄**：BJ_MACH 从 40→20 条，仅保留一般机加（车削/铣削/钻孔等）
- §1.9 StageDict：枚举表 + 初始化数据表全面更新

**⚖️ 重要更新（v5.0.19）**（2026-05-06 StageDict/ProcessCodeDict 业务数据初始化）：
- 📊 **StageDict 种子数据重建**：基于工序对照表.xlsx 生成初始 22 条（5 厂 × 5 类别）
- 📊 **ProcessCodeDict 首次录入**：152 条初始化数据（155 行去重 3 行重复码）
- 📊 **命名规范确认**：CN6课 → 前缀 `CN6_`；FINAL 类别中文名 = “出口”
- §1.9d ProcessCodeDict：初始化数据汇总表 + 重复码处理说明

**⚖️ 重要更新（v5.0.16）**（2026-04-29 生产部门主链 + ProcessCodeDict 重定位 + WorkshopCode 全局清理）：
- 🔄 **业务事实定调**：部门 = 「物料 × 阶段」联合属性（**不进** StageDict、**不进** StageDetail）
- 🔄 **1 号位排程主链升级**：`(MaterialId, StageCode)` → `MaterialStageDeptContext` → `ProductionDepartmentId` → Routing 三件套
- 🔄 **ProcessCodeDict 定位翻转**（v5.0.15 错位：定为「ERP 工序对照表 ODS 镜像」）：本版改为 **「APS 自维护的 ODS 增强工序字典」**
  - 维护方：APS 系统管理员人工维护 + 0 号位审批；**不参与** `sp_SyncMasterData` 自动同步
  - 删除 `LastSyncedAt`；`SourceSystem` → **`CodeOrigin`**（值域 `ERP`/`MES`/`MANUAL`）；新增 **`StageCode`** 列（APS 增强）+ `UpdatedBy`
  - `MES_ProcessCode_View` 同步暴露 `StageCode` + `CodeOrigin`
- 🔑 **`ProcessCode → StageCode` 基础映射全链路共享**：5 号位 `sp_EnrichBOMWorkset` 与 2 号位 `sp_RebuildMaterialStageDeptContext` **统一查** `MES_ProcessCode_View.StageCode`，禁止各写一套规则
- 🆕 **新增 §1.11 `ProductionDepartment`**（APS 排程责任部门字典；`DeptCode/DeptName/FactoryId/StageCode 单值/IsActive`；不做组织树/不接审批）
- 🆕 **新增 §1.12 `MaterialStageDeptOverride`**（人工维护表；`Model/MaterialCode + StageCode + ProductionDeptCode`；导入时 Model→MaterialCode 1:N **拒收**）
- 🆕 **新增 §1.13 `MaterialStageDeptContext`**（2 号位组装结果；键 `(MaterialId, StageCode)` → `DefaultProductionDepartmentId`；SCD Type 2；1 号位**唯一**消费入口）
- 🆕 **新增 `MaterialStageDeptContext_Issues`**（Context 重建降级登记；旧值不动、新问题登记，待人工修正后局部重建）
- 🆕 **新增 `sp_RebuildMaterialStageDeptContext`**（三触发：`FULL` 每日定时全量 / `INCR` MSC 同步后增量 / `PARTIAL` 人工 Override 提交后局部）
- ✅ **`MaterialSupplyContext` 加 `DefaultProductionDepartmentId`**（FK → ProductionDepartment.Id；与 `DefaultProductionDeptCode` 双轨）
- ✅ **`Resource` 字段调整**：删 `WorkshopCode`（业务确认 MES 也无此概念）+ 加 `ProductionDepartmentId NOT NULL` + 加 `SourceProductionDeptCode`
- ✅ **Routing 三件套唯一键升级**（业务事实：同物料同 StageCode 下不同部门可有不同小工序集合）：
  - `UQ_RoutingOperation`: `(MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode)`
  - `UQ_RoutingDep`:       `(MaterialId, ProductionDepartmentId, RouteCode, PathId, FromOperationCode, ToOperationCode)`
  - `UQ_OpResElig`:        `(MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, ResourceId)`
- ✅ **`sp_SyncResourceData` 升级**：`WorkshopCode` → `ProductionDeptCode`；MERGE 加 `ProductionDepartmentId` 双映射 JOIN
- ✅ **`StageLeadTimeParam.WorkshopCode` → `ProductionDeptCode`**（口径统一；APS 自定义命中维度，纯字符串无 FK）
- 🚫 **`sp_SyncMasterData` 枚举不含 `'ProcessCode'`**（v5.0.15 误传将取消，ProcessCodeDict 不参与自动同步）
- 🧭 **R20 跨组织视角零特殊逻辑**：StageCode 已采目标工厂视角，按 `(MaterialId, StageCode)` 查 Context 天然得到目标工厂部门
- 🧭 **NULL 处理放弃**：业务确认 MES 工艺数据全部带部门，Routing 三件套 `ProductionDepartmentId NOT NULL`，不引入 `_UNSPECIFIED` 哨兵
- 【设计决策】部门维度只在 APS_Production 主链注入；`StageDetail` / `StageDict` 永远不动
- 【升级策略】现有环境：① 建 `ProductionDepartment` + 3 张新表 ② `ALTER ProcessCodeDict` ③ `ALTER MSC` ④ `ALTER Resource` ⑤ `ALTER Routing` 三件套（数据回填 → 改 UQ）⑥ 重建 `MES_ProcessCode_View` ⑦ `sp_SyncResourceData` 升级 ⑧ 4 个 ODS 契约视图字段升级（走审批，DBA 执行）

**⚖️ 重要更新（v5.0.15）**（2026-04-28 ProcessCode 防腐三件套补齐 + StageDict 字段净化）：
- ▸ **原则**：ProcessCode（6 位 ERP 工序码）是 ERP 易变字段，**严格只在 ODS 层活**；APS_Production 库永不出现
- ▸ **原则**：两本字典职责分离——`StageDict`=APS 大工艺阶段字典（APS 自主语义）；`ProcessCodeDict`=ERP 工序对照表的 ODS 物理镜像
- ▸ **原则**：字典只承载"阶段自身属性"；"物料×阶段"联合决定的属性（是否入库/入库角色/LeadTime）一律放 RoutingStage / StageDetail / StageLeadTimeParam
- 🆕 **新增 §1.9e `ProcessCodeDict`**：ERP 工序对照表的 ODS 镜像；主键 ProcessCode；字段 ProcessName / FactoryCode / ActualFactoryCode / TrusteeProcCode / IsOutsource / IsRetouch / WarehouseRole / SourceSystem / LastSyncedAt
- 🆕 **新增 §1.9f `MES_ProcessCode_View`** Socket-Plug 契约视图：消费方一律查视图，ERP 升级改视图 SELECT 别名吸震
- ♻️ **重写 §1.6 `vw_MES_BOM_Stage_Enriched`**：原 v5.0.10 的 JOIN 键 `sd.StageCode = pc.StageCode` 无法成立（聚合阶段 vs 6 位工序码，值域不同 + 1:N）；改为**BOM 边粒度**——`MES_BOM_View × MES_ProcessCode_View` 按 GoodsProcCode / MaterialProcCode JOIN，可选 LEFT JOIN StageDetail 带聚合 StageCode
- 🔻 **§1.9 StageDict 字段净化**：删除 `IsStockPoint`（物料×阶段属性，错位）+ `IsOutsource`（与 `StageCategory='OUTS'` 重复）；初始化数据同步瘦身；外协判定改查 `StageCategory='OUTS'`
- 🧭 **IsStockPoint 分层承接**：字典级删除 → 物料级 RoutingStage.IsStockPoint（3 号位维护，如需）→ BOM 边级 StageDetail.IsSupplyThreshold（已存在，5 号位回填）
- 🛠️ `sp_SyncMasterData(@SourceType)` 枚举补 `'ProcessCode'` 分支（v1 占位骨架）
- 【升级策略】现有环境：① 建 ProcessCodeDict 表 + MES_ProcessCode_View ② ALTER TABLE StageDict DROP COLUMN IsStockPoint, IsOutsource ③ DROP + 重建 vw_MES_BOM_Stage_Enriched

**⚖️ 重要更新（v5.0.14）**（2026-04-28 ProduceToFactoryMap 崩溃修复：以用户提供的 Produce 权威照片为准纠正 5 处错误映射）：
- ⚠️ **严重 Bug修复**：§1.8 ProduceToFactoryMap 初始化数据中 Produce=**7** 的 TargetFactory 从 `TJ` 纠正为 **`CN`**（中国公司内制他用）
- ⚠️ **严重 Bug修复**：§1.8 ProduceToFactoryMap 初始化数据中 Produce=**11** 的 TargetFactory 从 `SH` 纠正为 **`TJ`**（天津工厂内制他用）
- ⚠️ **分类体系收敛**：SourceCategory 的 `INHOUSE_SPECIAL`（自创概念）取消；Produce=5/8/9 从“内制·特注”纠正为“**内制·自用**”（与照片“内制(自用)”业务分类一致）；值域由 4 类收敛为 3 类：`PURCHASE` / `INHOUSE_SELF` / `INHOUSE_CROSS`（正交于 R07/R20 业务约束）
- ✅ §1.3 `ChildRequiredFactory` 字段注释同步纠正：`6=BJ/7=TJ/9,11=SH` → `1=继承父件 / 5,8=CN制造6课 / 9=SH / 6=BJ / 7=CN / 11=TJ`
- ✅ §1.7 R20 说明同步纠正【Produce ∈ {6,7,11} → BJ/CN/TJ】
- ✅ ProduceName 按照片权威重写：
  - 0 日本保税/国内保税；3 海外保税（日本以外）
  - 2 国内课税；4 海外课税；10 海外课税（适用范围不同）
  - 1 中国/北京/天津内制自用；5 CN制造6课内制自用；8 CN制造6课内制ASSY自用；9 SH上海公司内制自用
  - 6 BJ北京工厂内制他用；7 CN中国公司内制他用；11 TJ天津工厂内制他用
- 【设计决策】SourceCategory 不处理照片的“购入(保税/课税)”细分（ERP 内部口径，对 APS 排程透明）；保税/课税业务语义保留在 Description 列

**⚖️ 重要更新（v5.0.13）**（2026-04-25 资源 ODS 契约视图命名统一 + sp_SyncResourceData 占位 SP）：
- ▸ **命名统一**：`APS_Resource_View` → `MES_APS_Resource_View`；`ext_APS_Resource_View` → `ext_MES_APS_Resource_View`（与 `MES_APS_Routing_*_View` 系列对齐；历史按消费方命名的孤例收敛）
- ▸ **同步 SP 统一出口**：`sp_SyncResourceData(@SourceType)`（DDL v5.0.13 新增）；v1 仅 'MES' 分支走 MERGE `ext_MES_APS_Resource_View` → Resource，'EAM' 分支 RAISERROR `NOT_IMPLEMENTED`
- ▸ **EAM 扩展路径**：未来并行新增 `EAM_APS_Resource_View` + `ext_EAM_APS_Resource_View`（同构契约零分叉）
- ▸ **命名口径红线**：ODS 契约视图一律“源系统_消费方_实体_View”三段式（单源时可省略消费方）
- §4.4e 重命名为 `MES_APS_Resource_View`；§1.5 重命名为 `ext_MES_APS_Resource_View`；部署检查清单和验证 SQL 同步更新

**⚖️ 重要更新（v5.0.12）**（2026-04-24 工艺数据三层模型收敛 + Stage 顺序唯一权威）：
- ▸ **三层分层模型确立**：`OperationName`/`OperationCode`（具体工序，如 NC/MC/切断/精修）/ `ProcessType`（**辅助分类标签**，不参与排程对接）/ `StageCode`（**BOM↔Routing 对接的业务大工艺码**）——三者互不替换
- ▸ **BOM↔Routing 对接主键 = `(MaterialCode, StageCode)`**：1 号位按此二元组从 RoutingOperation 取该阶段下的小工序，再结合 RoutingDependency 生成 Task
- ▸ **StageSeq 权威源唯一化**：**`StageDetail.StageSeq` 是唯一权威**；`RoutingStage.StageSeq` **已删除**（跨物料/跨根产品语境下 MES 给不出正确值）
- ▸ **R20 跨组织视角统一**：BOM 侧 `StageDetail.StageCode` 采用**目标工厂视角**——父件在 TJ、R20 指派到 BJ 时直接写 `BJ_MACH`（不是 TJ_MACH），1 号位消费无需跨厂翻译
- ▸ **ProcessType 配置化**：新增 `ProcessTypeDict`（§1.9b，预留骨架 IsActive=0）；硬编码值域作废，业务启用时 UPDATE IsActive=1
- §3.6b RoutingOperation：ProcessType/StageCode 字段注释重写；强调 StageCode 必须取自 StageDict
- §3.6c2 RoutingStage：**删除 StageSeq 字段**；去除"阶段顺序号"相关所有表述；定位收敛为"该物料在哪些大工艺阶段存在配置"
- §1.3b StageDetail：StageCode 注释补 R20 他用方视角说明
- §1.9 StageDict：补"BOM↔Routing 对接主键之二" + R20 视角约定
- 新增 §1.9b `ProcessTypeDict`（工序级分类标签字典；预留骨架）
- OperationCode **不**引入全局字典（MES 侧不可控 + 新增频繁；跨厂对接靠 StageCode 足够）

**⚖️ 重要更新（v5.0.11）**（2026-04-24 规则资产化 + 批次永不阻塞降级策略）：
- ▸ **设计哲学升级**：防腐层只做"吸震 + 登记"，**不做"生产准入判断"**；批次永不因数据质量阻塞（状态机永远 → READY，FAILED 仅保留 SP 自身崩溃）
- ▸ **规则资产化**：R17 Produce→工厂映射从硬编码 → 落配置表 `ProduceToFactoryMap`（§1.8）
- ▸ **StageCode 全局字典**：新增 `StageDict` 表（§1.9），采用方案 B（工厂+阶段码），统一 RoutingStage / StageDetail / StageLeadTimeParam 的 StageCode 值域
- §1.5 Issues 表：**+1 列** `DegradeAction NVARCHAR(100) NULL`（降级动作标签）；处置矩阵重写为"降级 + 登记"，去掉阻塞语义
- §1.5 消费方职责更新：从"批次放行校验" → "事后巡检" + "统计驱动 ERP 源端修复"
- §1.7 Produce 附录改为指向 `ProduceToFactoryMap` 配置表（§1.8），值域同步
- 新增 §1.8 `ProduceToFactoryMap`（R17 规则资产化配置表）
- 新增 §1.9 `StageDict`（StageCode 全局字典表，方案 B）
- IssueType 值域调整：NO_STAGE / UNKNOWN_PROCCODE / QUANTITY_INVALID 严重级从 ERROR → WARN；CYCLIC_BOM 从 CRITICAL → ERROR；全部配 DegradeAction 标签
- CYCLIC_BOM 策略明确：**首次访问保留 + 重复循环节点跳过**（visited 集防环；实现即现有 CTE 的 `Path NOT LIKE '%Child%'`）
- 运营 SLA 简化：INFO 忽略 / WARN 月度巡检（业务复核人员）/ ERROR 次日晨会 / CRITICAL 追责 SP 本身
- 【设计决策】Issues 不决定批次 READY，只决定事后处置优先级
- 【设计决策】Issues 统计驱动 ERP 源端数据质量改进：FACTORY_MISMATCH/NO_STAGE 积累阈值后反馈 ERP 维护方修正

**⚖️ 重要更新（v5.0.10）**（2026-04-23 R17/R25/R26/R27 工厂映射 + BOM错误容错，基于《BOM_Workset_生成与错误处理技术方案_v1.0》）：
- ▸ **稳定合同原则**：Workset/StageDetail 核心表最小改动；ERP 特征字段不下沉到 L1/L2 合同层
- §1.3 `MES_APS_BOM_Workset` + §1.4 Archive + §2.1 Realtime: **+1列** `ChildRequiredFactory NVARCHAR(20) NULL`（R17 推导子件账面工厂；值域 APS 自定义枚举 CN/CN6课/BJ/TJ/SH/NULL，ERP 升级不影响）
- §1.3 `ChildSourceHintCode` 注释订正：`"0/1/2编码"` → `"0-11编码（详见 §1.7 Produce 值域）"`
- §2.2 `APS_BOM_RAW`: 同步 +1列 `ChildRequiredFactory`（从 Workset 透传）
- **§1.3b StageDetail 核心表零改动**：扩展语义（ActualFactory/TrusteeProcCode/ProcessCode 等 ERP 特征字段）全部走 **§1.6 vw_MES_BOM_Stage_Enriched** 视图
- **新增 §1.5 `MES_APS_BOM_Workset_Issues`**：BOM 解析错误登记表（诊断独立表，演进不影响 L1 合同）
- **新增 §1.6 `vw_MES_BOM_Stage_Enriched`**：ODS 派生查询便利视图（**非防腐层**，仅 ODS 内部使用；APS 本地不做对称视图以避免 ERP 特征下沉）
- 新增 §1.7 附录：Produce 字段值域枚举（0-11 完整表 + R17/R20/R25/R26/R27 推导规则汇总）
- 【设计决策】`ChildRequiredFactory` 值域是 APS 自定义 5 厂实体枚举，由 R17 Produce→厂 映射规则产出；ERP 升级只需 5 号位调整映射表
- 【设计决策】诊断/错误信息不进 Workset/StageDetail 核心表，单独进 Issues 表
- 【设计决策】APS 本地**不新增派生视图**：APS 排程需要委外信息时由 2 号位预计算落独立配置表，不直接下沉 ERP 字段语义
- 【放行策略】批次 READY 前检查 Issues：CRITICAL/ERROR 阻塞；WARN 登记放行；INFO 静默登记

**⚖️ 重要更新（v5.0）**（2026-04-15 StageDetail升级为统一阶段路径结果表，支持ROOT根产品完工路径）：
- MES_APS_BOM_Workset_StageDetail / Archive / Realtime / APS_BOM_STAGE_PATH_RAW: +1列 `StageScopeType`（EDGE/ROOT），`ParentMaterialCode` 改为可空
- 【设计决策】ROOT记录：`ParentMaterialCode=NULL`，`IsSupplyThreshold` 恒为0（V1不泛化此字段语义）
- 【设计决策】ROOT路径推导：Level=1的ParentProcRefCode → 映射标准化 → 多条不一致取最长+记WARNING
- 【设计决策】1号位消费查询必须显式按 `StageScopeType` 区分（不得混查EDGE+ROOT）
- 【结构调整 2026-04-15】StageDetail迁至§1.3b、APS_BOM_STAGE_PATH_RAW迁至§2.1b、StageLeadTimeParam编号调整为§3.6c3
- §4.1 MES_BOM_View：补齐3辅助字段(ParentProcRefCode/ChildProcRefCode/ChildSourceHintCode) + 追溯增强字段(SourceSystem/SourceBOMId)
- §4.1 MES_BOM_View：新增**唯一默认版本裁决原则**（v5.0写死）：ODS内部裁决，IsDefaultVersion=1全局唯一，VersionPriority不暴露

**⚖️ 重要更新（v4.9）**（2026-04-13 BOM双层结果+阶段提前期参数化，基于《BOM阶段顺序与Workset双层结果设计建议_v1.0》）：
- ▸ 本版替代v4.8，方案升级：单一StageHintCode → 3原始辅助字段 + StageDetail双层结果
- MES_APS_BOM_Workset / Archive / Realtime: StageHintCode → ParentProcRefCode + ChildProcRefCode + ChildSourceHintCode
- APS_BOM_RAW: 去StageHintCode，仅保留ChildRequiredStageCode（APS侧只带最终结果）
- 新增 MES_APS_BOM_Workset_StageDetail / Archive / Realtime（5号位派生：BOM边的完整大工艺顺序明细）
- 新增 APS_BOM_STAGE_PATH_RAW（APS本地缓存，2号位搬运）
- 新增 StageLeadTimeParam（阶段提前期参数表，参数化外协Task + 多级降级命中）
- RoutingStage: 定位调整为“阶段字典/标准阶段语言”（不作为排程权威阶段顺序源）
- 【设计决策】ChildRequiredStageCode=NULL时按保守策略：子件必须全工艺完成后才可供给父件
- 【设计决策】RoutingStage=阶段字典，StageDetail=5号位派生结果，职责分离不混写
- 【设计决策】外协阶段不塞RoutingOperation虚拟工序，由StageLeadTimeParam参数化驱动Task生成
- 【V1不建】BomProcessBinding / StageHintMapping 留V2，结果字段已预埋
- 【结构调整 2026-04-15】StageDetail迁至§1.3b（与Workset同阶段）、APS_BOM_STAGE_PATH_RAW迁至§2.1b（与APS_BOM_RAW同阶段）、StageLeadTimeParam编号调整为§3.6c3

**⚖️ 重要更新（v4.7）**（2026-04-09 客户分级字段）：
- ERP_Order_Staging / Order_Canonical / Order: +1字段 CustomerTier
- CustomerTier：客户分级（VIP/KEY_ACCOUNT/STANDARD/GENERAL），APS衍生字段，默认GENERAL
- 衍生来源：由sp_ValidateAndPromoteOrders从RawData中客户编码/备注等+对照表推导

**v4.6更新内容**（2026-04-09 订单ETL v1.2增补，基于《仅1.2增补内容v1.0》）：
- ERP_Order_Staging / Order_Canonical / Order: +3字段（IssueDate/OriginalDueDate/ReceivedQty）
- IssueDate：订单发行/下发日期，源事实字段
- OriginalDueDate：原始纳期（客户最初要求交期），MTS时=DueDate
- ReceivedQty：已入库数量（仅MTS），SO订单为NULL

**⚠️ 重要更新（v4.5）**（2026-04-09 订单业务字段补充，基于《订单ETL补充字段设计建议v1.1》）：
- ERP_Order_Staging: +6字段（TransportMode/CustomerName/MTS_InstructionNo/CustomerSegment/SalesOrderCategory/DemandMaturityStatus）
- Order_Canonical: +6字段（同上）
- Order: +5字段（TransportMode/CustomerName/CustomerSegment/SalesOrderCategory/DemandMaturityStatus，MTS_InstructionNo已有）
- Task: +1字段（MTS_InstructionNo，从Order冗余）
- 【业务澄清】OrderType/FactoryCode为APS衍生/标准化字段（非ERP直接源字段），修正字段说明
- MTS_InstructionNo明确来源于ERP生产指示表InstructionNo（≠OrderNo）

**历史更新（v4.4）**（2026-04-03 订单链路审计）：
- ERP_Order_Staging字段表对齐DDL：补SourceSystem/SourceMasterID字段、CustomerDueDate→DueDate、OrderType补MTO、BOMNO改必填、SyncStatus补PROCESSED
- Order_Canonical字段表对齐DDL：补FactoryCode/UOM字段、Upsert键定义、状态枚举与流转说明
- ERP_Order_Staging状态机说明（PENDING→VALIDATED→PROCESSED / FAILED）
- MTS来源说明补充、SourceSystem/SourceMasterID字段说明
- 补4个ext_跨库包装视图字段说明章节
- 更新相关文档引用：防腐层设计方案版本号 v1.2 → v1.3

**⚠️ 重要更新（v4.3）**（2026-03-25）：
- 补充 ResourceGroup 表的完整字段说明（第3.4节）
- 更新相关文档引用：防腐层设计方案版本号 v1.1 → v1.2

**⚠️ 重要更新（v4.2）**（2026-03-24）：
- 修复ERP_Master_View字段定义：补全MaterialName、UOM、LeadTimeDays、SafetyStock四个缺失字段
- 修复MaterialSupplyContext同步逻辑：删除错误引用的MinOrderQty、MaxOrderQty字段，补全完整的MERGE语句
- 对齐防腐层设计文档，确保字段定义一致性

**⚠️ 重要更新（v4.1）**（2026-03-24）：
- 修正PlanVersion表字段：CreatedByUserId + CreatedByUserName（替代原CreatedBy字段）
- 修正MES_API_BOM_Request表：新增TriggeredByUserId + TriggeredByUserName字段
- 新增第三部分：APS_Auth数据库（13张权限与审批相关表）
- 更新相关文档引用，对齐权限系统设计

**⚠️ 重要更新（v4.0）**（2026-03-18）：
- v3.0基础上补充了APS库所有主数据表、订单表、任务表的完整字段说明
- 不再需要查阅业务确认版文档，本文档即为完整版
- 同步v2.5 DDL修复：Material表字段名统一为MaterialCode/MaterialName
- 新增Order_Canonical表的完整说明

**历史更新（v3.0）**：
- 补充了ODS库（MES_Integration）的所有表和视图
- 补充了防腐层视图契约（MES_BOM_View、ERP_Master_View、MES_Material_View）
- 补充了APS库的跨库包装视图（ext_ERP_Master_View、ext_MES_Material_View）
- 补充了ETL相关表（APS_BOM_RAW、APS_ETL_Log、ERP_Order_Staging）

---

## 📋 文档说明

本文档将数据库表结构转换为易读的字段说明格式，每个表包含：
- **表名**：数据库表名称
- **所属库**：MES_Integration（ODS库）或 APS_Production（APS库）
- **业务用途**：该表在业务中的作用
- **字段清单**：包含英文字段名、中文含义、数据类型、业务说明、示例值

**文档组织方式**：
- **第一部分**：ODS库（MES_Integration）- 防腐层与数据集成
- **第二部分**：APS库（APS_Production）- 核心业务表

---

# 第一部分：ODS库（MES_Integration）

## 📌 ODS库概述

**库名**：`MES_Integration`  
**用途**：作为MES生产系统与APS排程系统之间的**防腐层**（Anti-Corruption Layer），负责：
1. 接收MES系统的BOM展开请求
2. 执行BOM递归展开计算
3. 提供标准化的数据视图契约给APS库
4. 隔离MES系统的物理表结构变化

**核心设计原则**：
- ✅ ODS库只负责数据转换和缓存，不包含业务逻辑
- ✅ 通过视图契约（View Contract）向APS库暴露数据
- ✅ APS库通过跨库包装视图访问ODS库，不直接访问物理表

---

## 一、BOM展开相关表（批次模式）

### 1.1 MES_API_BOM_Request（BOM展开请求表 - 批次）

**所属库**：MES_Integration  
**业务用途**：记录每天00:00触发的批量BOM展开请求（批次模式），支持80万订单的BOM展开

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 请求ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 唯一标识，格式：YYYYMMDD_HHMMSS | 20260305_000000 |
| Status | 批次状态 | NVARCHAR(20) | PENDING（待处理）、PROCESSING（处理中）、READY（就绪）、CONSUMED（已消费）、FAILED（失败） | READY |
| RootCount | 活跃根数量 | INT | 本批次包含的订单数（如80万） | 800000 |
| ExpandedRowCount | 展开后行数 | INT | BOM展开后的总行数（如350万） | 3500000 |
| CreatedAt | 创建时间 | DATETIME2 | 批次创建时间 | 2026-03-05 00:00:00 |
| ProcessingStartTime | 开始处理时间 | DATETIME2 | 开始展开BOM的时间 | 2026-03-05 00:00:05 |
| CompletedAt | 完成时间 | DATETIME2 | BOM展开完成时间 | 2026-03-05 00:15:30 |
| ProcessingDuration | 处理耗时（秒） | INT | 展开耗时，单位：秒 | 925 |
| RetryCount | 重试次数 | INT | 失败后的重试次数 | 0 |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) | 失败时的错误描述 | NULL |
| TriggeredByUserId | 触发用户ID | INT | 触发批次的用户ID（关联APS_Auth.User表） | 5 |
| TriggeredByUserName | 触发用户名 | NVARCHAR(100) | 触发批次的用户名（冗余字段） | zhang.san |

**技术要点**：
- 批次号格式：`YYYYMMDD_HHMMSS`，确保唯一性
- 状态流转：PENDING → PROCESSING → READY → CONSUMED
- 性能目标：80万订单展开到350万行，控制在15分钟内
- **权限追踪**：记录触发批次的用户，用于审计和权限控制

---

### 1.2 MES_API_BOM_Request_Detail（BOM展开请求明细表）（v5.0.21 升级；v5.0.22 补 OrderType；v5.0.31 锚点升级至 OrderCanonicalId）

**所属库**：MES_Integration  
**业务用途**：记录批次中每条订单的 BOM 展开请求（v5.0.32：定位收敛为纯请求输入表；字段 = OrderCanonicalId + MaterialCode + FactoryCode + OrderType + RequestedBOMNO；Workset解析结果不回写本表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 明细ID（RequestDetailId） | BIGINT | 主键，自增；作为Workset/Issues的来源追溯锚点 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | FK→MES_API_BOM_Request — 关联批次 | 20260305_000000 |
| **OrderCanonicalId** | **防腐层核心订单ID** | **BIGINT** | **v5.0.31 唯一约束主锚点；逻辑引用 Order_Canonical.Id（跨库不加FK约束）；夜间活跃根集合由此驱动** | **1001** |
| OrderNo | 订单号 | NVARCHAR(100) | v5.0.31 冗余，便于ODS侧排查/日志/人工核对 | SO-20260305-001 |
| SourceSystem | 来源系统 | NVARCHAR(20) | v5.0.31 值域：'ERP'/'MES' | ERP |
| SourceOrderId | 来源系统订单ID | NVARCHAR(100) | v5.0.31 ERP订单编号或MES内部ID | ERP-88001 |
| **MaterialCode** | **物料编码** | **NVARCHAR(100)** | **v5.0.21 5号位BOM入口解析主键；RequestedBOMNO=NULL时据此推导BOM入口** | **MAT-10001** |
| FactoryCode | 工厂编码 | NVARCHAR(50) | v5.0.21 5号位按厂分流用 | TJ |
| **OrderType** | **订单类型** | **NVARCHAR(20)** | **v5.0.22 从 ERP_Order_Staging.OrderType 透传；值域：SALES_ORDER/PRODUCTION_INSTRUCTION（v5.0.24重分类）；RequestedBOMNO=NULL 时 5 号位据此选取 BOM 入口规则；不参与唯一约束** | **SALES_ORDER** |
| **RequestedBOMNO** | **订单原始BOMNO** | **NVARCHAR(50)** | **v5.0.31 订单原始携带的BOMNO（请求输入字段，可空=待5号位解析）；原字段名 BOMNO** | **BOM-10001** |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 00:00:01 |

**唯一约束**：`UNIQUE (BatchNo, OrderCanonicalId)`（`UQ_BOMRequestDetail_BatchCanonical`）— 同批次内每个 Order_Canonical 最多一条明细

**技术要点**：
- **v5.0.32 字段收敛**：本表定位为纯 BOM 请求输入表；`Model`/`OrderStagingId`/`ResolvedBOMNO` 已删除；ERP原始 Model 保留在 `ERP_Order_Staging.Model`/`Order_Canonical.SourceModel`；Workset 解析结果归 `OrderBomRequestLink.ResolvedBOMNO`（2号位写入）
- **v5.0.31 锚点升级**：`OrderCanonicalId` 是核心锚点，逻辑引用 APS 防腐层 `Order_Canonical.Id`（跨库无FK约束）
- **RequestedBOMNO 语义**：订单原始携带值（请求输入），可空=待5号位解析；**禁止与 `OrderBomRequestLink.ResolvedBOMNO`（解析结果）混用**
- **RequestDetailId用途**：作为 `MES_APS_BOM_Workset.RequestDetailId` 和 `MES_APS_BOM_Workset_Issues.RequestDetailId` 的来源追溯锚点；不是业务主键；1号位主链不消费
- **BOM入口解析责任归属**：5号位Workset处理阶段，只写 Workset/StageDetail/Issues；2号位在同步完成后生成 OrderBomRequestLink

---

### 1.2b OrderBomRequestLink（订单-BOM解析结果索引表）（v5.0.31新增）

**所属库**：APS_Production（APS本地）  
**业务用途**：APS本地读模型/索引表；记录某PlanVersion/BatchNo下，某APS Order最终使用的BOM解析结果；提供 Order→BOM结构（`APS_BOM_RAW`）/ 大工艺路径（`APS_BOM_STAGE_PATH_RAW`）的快速查询入口；由2号位在 BOM Workset + StageDetail 同步完成后生成

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 排程版本ID | INT | FK→PlanVersion.Id（同库）；INT 对齐 PlanVersion.Id 类型；版本级索引 | 5 |
| BatchNo | BOM展开批次号 | NVARCHAR(50) | 对应ODS侧BOM展开批次号 | REQ_20260305_01 |
| **OrderId** | **APS本地订单ID** | **BIGINT NULL** | **v5.0.34: 允许 NULL；找不到时 LinkStatus='SKIPPED'；不建 FK 约束** | **2001** |
| OrderCanonicalId | ODS侧订单ID | BIGINT | 逻辑引用 Order_Canonical.Id（跨库，不加FK） | 1001 |
| OrderNo | 订单号 | NVARCHAR(100) | 冗余，便于人工核对 | SO-20260305-001 |
| SourceSystem | 来源系统 | NVARCHAR(20) | 'ERP'/'MES' | ERP |
| SourceOrderId | 来源系统订单ID | NVARCHAR(100) | ERP订单编号或MES内部ID | ERP-88001 |
| RequestDetailId | BOM请求明细ID | BIGINT | 逻辑引用 MES_API_BOM_Request_Detail.Id（跨库，不加FK） | 501 |
| RequestedBOMNO | 订单原始BOMNO | NVARCHAR(50) | 订单原始携带的BOMNO（可空） | BOM-10001 |
| **ResolvedBOMNO** | **BOM解析结果BOMNO** | **NVARCHAR(50)** | **由 2号位从 ODS `MES_APS_BOM_Workset` Level=1 行聚合生成（非5号位写入）；成功后非空；`APS_BOM_RAW` 查询入口键** | **BOM-10001** |
| **RepWorksetId** | **代表性WorksetId** | **BIGINT** | **Level=1代表性 Workset.Id（MIN Id，与ROOT StageDetail规则一致）；`APS_BOM_STAGE_PATH_RAW` 查询入口；NULL=展开失败** | **8801** |
| **LinkStatus** | **解析状态** | **NVARCHAR(30)** | **RESOLVED/NO_BOM/FAILED/SKIPPED** | **RESOLVED** |
| ErrorMessage | 失败原因 | NVARCHAR(1000) | FAILED时记录原因，其余NULL | NULL |
| SyncedAt | 同步时间 | DATETIME2 | 2号位生成时间（SYSUTCDATETIME） | 2026-03-05 00:30:00 |

**唯一约束**：`UNIQUE (PlanVersionId, OrderCanonicalId)` （`UQ_OrderBomRequestLink_Plan_Canonical`）— 业务锚点 = PlanVersionId + **OrderCanonicalId**（v5.0.34 升级，不再是 OrderId）；同一版本内每个 Canonical 订单最多一条

**LinkStatus 值域**：
- `RESOLVED`：找到 OrderId 且有 Workset 结果，正常可用
- `NO_BOM`：外购件/无需展开/业务规则判定无 BOM
- `FAILED`：RequestDetail 有记录但 Workset 解析失败（`ErrorMessage` 记录原因）
- `SKIPPED`：RequestDetail 存在，但该订单未进入当前 PlanVersion 的 [Order] 快照（订单在 00:00 活跃根集合中，但装载时已取消/关闭/超出窗口）

**查询链路**（核心用途）：
- BOM结构：`Order → OrderBomRequestLink.ResolvedBOMNO → APS_BOM_RAW(BatchNo + BOMNO)`
- 大工艺路径：`Order → OrderBomRequestLink.RepWorksetId → APS_BOM_STAGE_PATH_RAW.WorksetId`

**技术要点**：
- **生成时机**：2号位在 BOM Workset + StageDetail 同步完成后生成（`PullBOMResultFromODSAsync(batchNo, planVersionId)` Step 4）
- **数据源**：ODS `MES_APS_BOM_Workset` 聚合（**禁止从 APS_BOM_RAW 反查**）；`ResolvedBOMNO` = Level=1 `Workset.BOMNO`
- **RepWorksetId规则**：= `MIN(MES_APS_BOM_Workset.Id) WHERE RequestDetailId+Level=1`，与 ROOT StageDetail.WorksetId 赋值规则一致
- **OrderId 可空处理**：按 `PlanVersionId + OrderCanonicalId` 在 `[Order]` 找不到时，写 `OrderId=NULL, LinkStatus='SKIPPED'`，不阻断批次
- **PlanVersionId 显式传参**：由 NightlyBatchOrchestrator 创建并持有，禁止 BOMResultPullService 内部自查最新 PlanVersion
- **APS_BOM_RAW 保持BOMNO级共享**：不因引入本表而新增订单级字段；此表承担 Order→BOMNO 桥接
- **索引**：`(BatchNo, RequestDetailId)` / `(BatchNo, ResolvedBOMNO)` / `RepWorksetId` / `(PlanVersionId, OrderId) WHERE OrderId IS NOT NULL`

---

### 1.3 MES_APS_BOM_Workset（BOM展开结果工作集）

**所属库**：MES_Integration  
**业务用途**：存储BOM递归展开后的结果（父子关系），供APS库读取

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联批次 | 20260305_000000 |
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | 成品或半成品编码 | MAT-10001 |
| ChildMaterialCode | 子物料编码 | NVARCHAR(50) | 半成品或原材料编码 | MAT-20001 |
| Quantity | 用量 | DECIMAL(18,6) | ⚠️ 单位用量（生产1个父件需要几个子件），**不累乘** | 2.5 |
| Level | BOM层级 | INT | 1=直接子件，2=孙件，依此类推 | 1 |
| Path | 路径 | NVARCHAR(MAX) | 从根到当前节点的路径（用于环路检测） | MAT-10001 -> MAT-20001 |
| ParentProcRefCode | 父件工序参考码 | NVARCHAR(50) | v5.0.7 ERP BOM原始辅助字段，经MES_BOM_View契约承接 | OP-030 |
| ChildProcRefCode | 子件工序参考码 | NVARCHAR(50) | v5.0.7 同上 | OP-020 |
| ChildSourceHintCode | 子件来源提示码 | NVARCHAR(50) | v5.0.7 当前来源=ERP BOM的produce字段（0-11编码，v5.0.10订正；详见 §3.1 Produce 值域） | 1 |
| ChildRequiredStageCode | 子件供给所需阶段码 | NVARCHAR(50) | v5.0.7 5号位后置回填；NULL=保守策略（全工艺完成后才可供给） | TJ_OUTS |
| **ChildRequiredFactory** | **子件应归属账面工厂** | **NVARCHAR(20)** | **v5.0.10 R17推导：Produce→厂映射（v5.0.14 照片权威纠正：1=继承父件 / 5,8=CN6课 / 9=SH / 6=BJ / 7=CN / 11=TJ）；值域 APS 自定义枚举 CN/CN6课/BJ/TJ/SH/NULL；外购件=NULL；5号位后置回填；ERP 升级不影响本字段** | **TJ** |
| RequestDetailId | 来源追溯锚点 | BIGINT | v5.0.21 FK→MES_API_BOM_Request_Detail.Id；nullable；非业务键；1号位主链不消费；2/5号位追溯、回写、运营闭环用 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 00:05:00 |

**技术要点**：
- `Quantity` 字段存储的是**单位用量**，不是累乘后的总用量
- `Path` 字段用于检测BOM环路（循环依赖）
- v5.0.7：3个原始辅助字段由 sp_ExpandBOMBatch 透传；`ChildRequiredStageCode` + `StageDetail` 由 `sp_EnrichBOMWorkset` 后置回填（双层结果 EDGE+ROOT）
- v5.0.10：`ChildRequiredFactory` 与 `ChildRequiredStageCode` 同步由 `sp_EnrichBOMWorkset` 回填（R17/R25/R26 推导）；回填过程中的异常降级登记到 `MES_APS_BOM_Workset_Issues`（§1.8），**永不阻塞批次**（v5.0.11 决策）
- v5.0.21：`RequestDetailId` 为来源追溯锚点，追溯回写和运营闭环用；不是业务键；1号位主链不消费
- 索引优化：聚集索引 `(BatchNo, ParentMaterialCode)`

---

### 1.3b MES_APS_BOM_Workset_StageDetail（统一阶段路径结果表）⭐ **v5.0.7新增，v5.0.8升级（2026-04-15 支持ROOT根产品完工路径）**

**所属库**：MES_Integration  
**业务用途**：统一阶段路径结果表（5号位派生），通过 `StageScopeType` 区分两类记录：
- **EDGE**（子件供给路径）：某条BOM边对应的子件，在供给父件之前的完整大工艺顺序
- **ROOT**（根产品完工路径）：最上层产品自身完工所需的完整大工艺顺序（如最终组装）

同时有Archive和Realtime变体。

**⚠️ 职责分离**：此表为BOM侧派生结果（5号位产出），RoutingStage为阶段字典/标准阶段语言（3号位契约），二者不混写。

**技术要点（数据流向）**：
- **EDGE数据来源**：5号位基于 ParentProcRefCode + ChildProcRefCode + ChildSourceHintCode + 对照表综合推导
- **ROOT数据来源**（v5.0.8）：5号位取Level=1的ParentProcRefCode → 映射标准化阶段路径 → 若多条不一致取最长路径+记WARNING（不静默并集）
- **消费方**：1号位**必须按StageScopeType区分查询**；读取 StageSeq + StageCode → 串接 RoutingOperation 中每个阶段的小工序排Task；5号位读取完整阶段链 → 结合 StageLeadTimeParam 做库存/供给判断
- **1号位查询约定**：`WHERE ChildMaterialCode=@Mat AND StageScopeType='ROOT'`（根产品路径）；`WHERE ParentMaterialCode=@Parent AND ChildMaterialCode=@Child AND StageScopeType='EDGE'`（边级路径）
- **2号位**：搬运到 APS_BOM_STAGE_PATH_RAW（与 APS_BOM_RAW 同批次拉取）
- **归档**：由 sp_CleanupBOMWorkset 同步归档到 StageDetail_Archive（含StageScopeType）
- **生命周期**：与 MES_APS_BOM_Workset 完全一致（同批次生成、同批次归档、同批次拉取）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联批次 | 20260305_000000 |
| **WorksetId** | **追溯锚点** | **BIGINT** | **v5.0.26** FK->MES_APS_BOM_Workset.Id；每条Workset行单独写其StageDetail；sp_CleanupBOMWorkset级联清理用；NULL=兼容旧批次 | **12345** |
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| StageScopeType | 阶段路径类型 | NVARCHAR(10) | v5.0.8 EDGE=子件供给路径 / ROOT=根产品完工路径，DEFAULT 'EDGE' | EDGE |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | EDGE=父件编码；ROOT=NULL（v5.0.8可空） | MAT-10001 |
| ChildMaterialCode | 子/根物料编码 | NVARCHAR(50) | EDGE=子件编码；ROOT=根产品自身编码 | MAT-20001 |
| StageSeq | 阶段顺序号 | INT | 10/20/30，间隔10；**v5.0.12：排程唯一权威顺序源**（1 号位读此字段，不读 RoutingStage） | 10 |
| StageCode | 大工艺阶段码 | NVARCHAR(50) | 如TJ_MACH/TJ_OUTS/BJ_SURF；**v5.0.12：必须取自 `StageDict`（§1.9）**；**R20 跨组织场景采用目标工厂视角**（父件 TJ + R20 指派 BJ 时直接写 BJ_MACH）| BJ_MACH |
| IsSupplyThreshold | 是否供给阈值点 | BIT | **仅EDGE有效**：1=供给阈值点（对应ChildRequiredStageCode）；ROOT恒为0 | 0 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 00:05:00 |

**索引**：聚集索引 `(BatchNo, BOMNO, ChildMaterialCode)`，非聚集索引 `(ChildMaterialCode, StageSeq)`，非聚集索引 `(WorksetId)` WHERE WorksetId IS NOT NULL（v5.0.26，`sp_CleanupBOMWorkset` 级联清理用）

> **Archive/Realtime变体**：Archive表额外含 `ArchivedAt`；Realtime表无 `BatchNo`（按BOMNO组织），供插单场景实时消费。所有变体均含 `StageScopeType`、可空 `ParentMaterialCode` 和 `WorksetId`（v5.0.26）。Realtime变体另有 `IX_StageDetailRT_WorksetId` 索引用于实时链路清理。

---

### 1.4 MES_APS_BOM_Workset_Archive（BOM展开结果归档表）

**所属库**：MES_Integration  
**业务用途**：归档历史批次的BOM展开结果，用于审计和问题追溯

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 归档的批次号 | 20260304_000000 |
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | 成品或半成品编码 | MAT-10001 |
| ChildMaterialCode | 子物料编码 | NVARCHAR(50) | 半成品或原材料编码 | MAT-20001 |
| Quantity | 用量 | DECIMAL(18,6) | 单位用量 | 2.5 |
| Level | BOM层级 | INT | BOM层级 | 1 |
| Path | 路径 | NVARCHAR(MAX) | 路径 | MAT-10001 -> MAT-20001 |
| ParentProcRefCode | 父件工序参考码 | NVARCHAR(50) | v5.0.7 同Workset | OP-030 |
| ChildProcRefCode | 子件工序参考码 | NVARCHAR(50) | v5.0.7 同Workset | OP-020 |
| ChildSourceHintCode | 子件来源提示码 | NVARCHAR(50) | v5.0.7 同Workset（0-11编码） | 1 |
| ChildRequiredStageCode | 子件供给所需阶段码 | NVARCHAR(50) | v5.0.7 同Workset | TJ_OUTS |
| **ChildRequiredFactory** | **子件应归属账面工厂** | **NVARCHAR(20)** | **v5.0.10 同Workset** | **SH** |
| **RequestDetailId** | **来源追溯锚点** | **BIGINT** | **v5.0.26** 归档时从 Workset 透传；FK->MES_API_BOM_Request_Detail.Id；nullable | **102** |
| CreatedAt | 创建时间 | DATETIME2 | 原始创建时间 | 2026-03-04 00:05:00 |
| ArchivedAt | 归档时间 | DATETIME2 | 归档时间 | 2026-03-05 00:00:00 |

**技术要点**：
- 归档策略：每天00:00归档前一天的数据
- 保留周期：建议保留30天
- v5.0.7：归档时同步保留 3辅助字段 + ChildRequiredStageCode + StageDetail归档，供V2追溯审计
- v5.0.10：归档同步保留 `ChildRequiredFactory`
- v5.0.26：归档时从 Workset 透传 `RequestDetailId`，方便直接按请求明细追溯历史归档行

---

### 1.5 MES_APS_BOM_Workset_Issues（BOM解析错误登记表）⭐ **v5.0.10 新增，v5.0.11 处置策略修订，v5.0.21 补RequestDetailId**

**所属库**：MES_Integration  
**业务用途**：登记 5 号位 BOM 推导/展开过程中发现的数据异常与**降级动作**。**诊断信息不进 Workset/StageDetail 核心表**（保障 L1 合同稳定），本表结构可自由演进不影响下游。

**🎯 核心定位（v5.0.11）**：
- 本表是 **"事后追溯与 ERP 数据质量镜子"**，**不决定批次是否 READY**
- 批次状态机：`PENDING → EXPANDED → ENRICHED → READY`（永不因数据问题进 FAILED）
- `FAILED` 分支仅保留给 SP 自身崩溃（进程异常、tempdb 满、连接中断等极端情况）
- Severity 字段含义：**事后处置优先级**，非批次阻塞开关

**⚠️ 职责分离**：
- **写入方**：5 号位（回填过程中的 9 类异常）、sp_ExpandBOMBatch / sp_ExpandBOMRealtime（展开层面的异常）
- **消费方**：
  - 5 号位：**回填时写入 + 降级**（不阻塞批次）
  - 0 号位 / 业务复核人员：按 Severity + `ReviewStatus='PENDING'` **周度/月度巡检**
  - 运维：按 Severity 汇总告警；**统计驱动 ERP 源端数据质量改进**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 批量链路=批次号；实时链路可为 NULL（按 BOMNO 登记）| 20260305_000000 |
| BOMNO | BOM编号 | NVARCHAR(50) | 涉及的 BOM 编号 | BOM-10001 |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | 涉及父件（EXPAND_FAILED 等可能为 NULL）| MAT-10001 |
| ChildMaterialCode | 子物料编码 | NVARCHAR(50) | 涉及子件 | MAT-20001 |
| Produce | 子件 Produce 值 | NVARCHAR(5) | ERP BOM produce 字段（0-11）| 11 |
| IssueType | 错误类型 | NVARCHAR(40) | 见下方值域表 | FACTORY_MISMATCH |
| Severity | 严重程度 | NVARCHAR(10) | INFO/WARN/ERROR/CRITICAL；**v5.0.11：仅为事后处置优先级，不决定批次 READY** | WARN |
| Detail | 诊断详情 | NVARCHAR(1000) | 人类可读的诊断说明 | Produce=11 应归SH，BOM 实际在TJ (BOMNo=102407056) |
| **DegradeAction** | **降级动作** | **NVARCHAR(100)** | **v5.0.11 新增**：降级动作标签（STAGE_NULL / FACTORY_FALLBACK / QTY_DEFAULT_1 / PRODUCE_DEFAULT_1 / CYCLE_SKIP / BOMNO_SKIP）；为 NULL 表示无降级（纯登记）| FACTORY_FALLBACK |
| ExpectedFactory | 预期工厂 | NVARCHAR(20) | R17 推导的预期工厂（FACTORY_MISMATCH 用）| SH |
| ActualFactory | 实际工厂 | NVARCHAR(20) | BOM 实际所在工厂（FACTORY_MISMATCH 用）| TJ |
| RawRefJson | 原始快照 | NVARCHAR(MAX) | ERP 字段原始快照（JSON），**ERP 升级时字段名不影响本表结构**，供复核溯源 | {"produce":"11","GoodsProcCode":"310499",...} |
| ReviewStatus | 复核状态 | NVARCHAR(20) | PENDING / CONFIRMED / IGNORED / FIXED，DEFAULT 'PENDING' | PENDING |
| ReviewedBy | 复核人 | NVARCHAR(100) | 复核操作人员工号 | U001 |
| ReviewedAt | 复核时间 | DATETIME2 | 复核完成时间 | 2026-03-05 14:00:00 |
| Resolution | 复核结论 | NVARCHAR(500) | 复核结论文字描述 | ERP Produce 录错，已改为 9 |
| RequestDetailId | 来源追溯锚点 | BIGINT | v5.0.21 可通过 Workset.Id 间接回溯，此处冗余方便直查；nullable | NULL |
| CreatedAt | 登记时间 | DATETIME2 | 错误登记时间 | 2026-03-05 00:05:10 |

**IssueType × DegradeAction 处置矩阵**（v5.0.11 修订：全部降级 + 登记，**永不阻塞批次**）：

| IssueType | Severity | 触发条件 | DegradeAction | 降级行为说明 |
|---|---|---|---|---|
| `LEAF` | INFO | Produce 声明内制但物料无下阶 BOM | `STAGE_NULL` | `ChildRequiredStageCode=NULL`；1号位按保守策略兜底 |
| `FACTORY_MISMATCH` | WARN | Produce 厂 ≠ BOM 实际厂（单厂）| `FACTORY_FALLBACK` | 保留 BOM 原生链；`ChildRequiredFactory` 仍填 R17 推导值但登记冲突 |
| `FACTORY_MISMATCH_MULTI` | WARN | Produce 厂 ≠ BOM 实际厂（BOM 跨多厂）| `FACTORY_FALLBACK` | 同上；建议优先复核 |
| `NO_STAGE` | WARN | 有 BOM 但全为入库/出口码，无大工艺段 | `STAGE_NULL` | `ChildRequiredStageCode=NULL`；1号位按保守策略兜底（v5.0.10→v5.0.11：ERROR→WARN）|
| `UNKNOWN_PROCCODE` | WARN | 工序码查不到对照表 | `STAGE_NULL` / 部分链 | 未知码跳过不参与 Stage 推导；可识别部分正常推导（v5.0.10→v5.0.11：ERROR→WARN）|
| `QUANTITY_INVALID` | WARN | Quantity ≤ 0 或 NULL | `QTY_DEFAULT_1` | 按 Quantity=1 兜底写入 Workset（v5.0.10→v5.0.11：ERROR→WARN）|
| `MISSING_PRODUCE` | WARN | Produce 字段空 | `PRODUCE_DEFAULT_1` | 按 Produce=1 兜底（等同内制继承父件工厂）|
| `CYCLIC_BOM` | ERROR | BOM 环路 | `CYCLE_SKIP` | **首次访问保留 + 重复循环节点跳过**（visited 集防环；即 CTE 的 `Path NOT LIKE '%Child%'`）；该 BOMNo 已发现的父子边数据仍有效（v5.0.10→v5.0.11：CRITICAL→ERROR）|
| `EXPAND_FAILED` | CRITICAL | 单个 BOMNo 展开 SP 抛异常 | `BOMNO_SKIP` | try-catch 捕获该 BOMNo 异常；该 BOMNo 整棵树作废；其他 BOMNo 正常继续 |
| `BOM_ENTRY_NOT_FOUND` | ERROR | BOMNO=NULL 时无法找到任何 BOM 入口（Stage B/C）| NULL | 该 DetailId 展开跳过，不阻塞批次；月度巡检复核 |
| `BOM_ENTRY_AMBIGUOUS` | WARN | BOMNO=NULL 时有多个 BOMNO 候选，已取 Rank=1 最优 | NULL | 正常展开 Rank=1 候选；业务可验证其他候选是否有效 |
| `MISSING_MATERIALCODE` | ERROR | RequestDetail.MaterialCode 为空，无法解析 BOM 入口（v5.0.32：Model 已删除） | NULL | 该 DetailId 跳过展开 |
| `ORDER_TYPE_UNKNOWN` | WARN | OrderType 为 NULL 或未知枚举值 | NULL | 按默认 SALES_ORDER 入口规则降级处理 |
| `BOMNO_MISSING_PRODUCTION` | WARN/ERROR | **R31**：OrderType=PRODUCTION_INSTRUCTION + BOMNO 为空/0 | NULL | WARN=已按 MaterialCode 推导入口（可展开）；ERROR=未找到入口（跳过展开）；**必写，无论找没找到** |

**⚠️ v5.0.11 处置原则**：
- 所有 IssueType 均"降级 + 登记"，**批次状态机永远走到 READY**
- 不再有"阻塞"概念；Severity 只表达**事后处置优先级**
- `FAILED` 状态仅保留给 SP 进程崩溃（tempdb 满、连接中断等极端情况）

**运营 SLA**（v5.0.11 新增）：

| Severity | 响应机制 | 承接人 |
|---|---|---|
| INFO | 不关注，BI 报表汇总 | — |
| WARN | 月度巡检；累积高频 IssueType 反馈 ERP 源端修复 | 业务复核人员 / 0 号位 |
| ERROR | 次日晨会过一遍（知道降级丢了什么）| 业务复核人员 + 5 号位 |
| CRITICAL | 追责 SP 本身（不是数据问题，是展开程序问题）| 5 号位 / DBA |

**索引**：`(BatchNo, IssueType, Severity)` / `(ReviewStatus, Severity, CreatedAt)` / `(ChildMaterialCode, BatchNo)`

**降级数据追溯查询**（示例）：

```sql
-- 查询某批次中"走了降级路径"的子件
SELECT w.BOMNO, w.ChildMaterialCode, w.ChildRequiredStageCode, w.ChildRequiredFactory,
       i.IssueType, i.Severity, i.DegradeAction, i.Detail
FROM MES_APS_BOM_Workset w
JOIN MES_APS_BOM_Workset_Issues i
  ON w.BatchNo = i.BatchNo AND w.BOMNO = i.BOMNO
 AND w.ChildMaterialCode = i.ChildMaterialCode
WHERE w.BatchNo = @BatchNo;
```

---

### 1.6 vw_MES_BOM_Stage_Enriched（ODS 派生查询便利视图）⭐ **v5.0.10 新增（2026-04-23）；v5.0.15 重写（2026-04-28）**

**所属库**：MES_Integration  
**视图类型**：**派生查询便利视图**（非防腐层，非跨库包装视图；C 类）  
**业务用途**：基于 BOM 边粒度，JOIN ProcessCode 契约视图派生工序级扩展字段，供 ODS 内部消费。

**⚠️ v5.0.15 重写原因**：
原 v5.0.10 设计以 `StageDetail.StageCode = ProcessCodeDict.StageCode` 为 JOIN 键，但两者值域完全不同：
- `StageDetail.StageCode` = 聚合大阶段码（如 `TJ_MACH`），值域来自 `StageDict`
- `ProcessCodeDict` 主键 = 6 位 ProcessCode（如 `010496`）

且一个大阶段对应 N 个 ProcessCode（1:N 关系），无法 1:1 JOIN 派生。工序级字段只在 **BOM 行级上下文（GoodsProcCode / MaterialProcCode）** 可达，不在 StageDetail 聚合层可达。v5.0.15 改为 BOM 边粒度重建。

---

**🧭 三类视图定位对比**：

| 对比维度 | Socket-Plug 契约视图（A类）| ext_ 跨库包装视图（B类）| **vw_ 派生便利视图（C类，本视图）**|
|---|---|---|---|
| 位置 | ODS 库（ERP/MES DBA 创建）| APS_Production 库 | **ODS 库内部** |
| 职责 | 承接源系统字段契约 | 跨库透传（SELECT *）| **JOIN 派生扩展语义** |
| ERP 升级时 | **改这里**（防腐入口）| 不改 | 组合消费 A 类视图，自身无需 ERP 直连 |
| 消费方 | 全链路 | APS 排程内核 | ODS 内部（委外 ShippingTask 生成器、运维诊断、BI 报表）|
| 是否防腐层 | ✅ 是 | ✅ 是（跨库隔离）| ❌ 非防腐层 |

---

**📚 两本字典职责对比（v5.0.15 强化）**：

| 维度 | `StageDict`（§1.9c）| `ProcessCodeDict`（§1.9e，v5.0.15 新增）|
|---|---|---|
| 粒度 | **大工艺阶段**（如 `TJ_MACH`）| **6 位工序码**（如 `010496`）|
| 归属 | APS 自主业务语义字典 | ERP 工序对照表的 ODS 物理镜像 |
| 维护方 | APS 团队（0 号位审批 + 3/5 号位） | ERP/MES DBA + `sp_SyncMasterData` 同步 |
| 消费方 | APS 排程内核 + ODS | **仅 ODS 内部**（APS 永不消费）|
| ERP 升级影响 | **零影响** | 字段可能改名，由 `MES_ProcessCode_View` 契约视图吸震 |
| 是否防腐层 | 非（APS 内部字典）| 配套 Socket-Plug 视图 `MES_ProcessCode_View` 才是防腐入口 |

两本字典职责严格分离，不可混淆。

---

**APS 本地不做对称视图的原因**：`ActualFactoryCode / TrusteeProcCode / ProcessCode` 等是 ERP 特征字段，若下沉到 APS 本地排程内核，ERP 升级时 APS 排程代码直接被影响，等于打穿防腐墙。APS 排程若需委外/受托信息，由 2 号位预计算落独立配置表（如 `StageLeadTimeParam` 扩展），**不直接暴露 ERP 字段语义**。

---

**视图定义**（v5.0.15 新版，BOM 边粒度）：

```sql
CREATE VIEW vw_MES_BOM_Stage_Enriched AS
SELECT
    -- BOM 边稳定字段（来自 MES_BOM_View Socket-Plug 契约）
    bv.BOMNO, bv.ParentMaterialCode, bv.ChildMaterialCode, bv.Quantity,
    bv.ParentProcRefCode, bv.ChildProcRefCode, bv.ChildSourceHintCode,
    -- 父件工序派生（GoodsProcCode → ProcessCode 字典）
    pc_g.ProcessCode        AS ParentProcessCode,
    pc_g.FactoryCode        AS ParentStageFactory,
    pc_g.ActualFactoryCode  AS ParentActualFactory,
    pc_g.TrusteeProcCode    AS ParentTrusteeProcCode,
    pc_g.IsOutsource        AS ParentIsOutsource,
    -- 子件工序派生（MaterialProcCode → ProcessCode 字典）
    pc_m.ProcessCode        AS ChildProcessCode,
    pc_m.FactoryCode        AS ChildStageFactory,
    pc_m.ActualFactoryCode  AS ChildActualFactory,
    pc_m.TrusteeProcCode    AS ChildTrusteeProcCode,
    pc_m.IsRetouch          AS ChildIsRetouch,
    pc_m.WarehouseRole      AS ChildWarehouseRole,
    -- 可选附带聚合 StageCode（5 号位派生结果，便于审计关联回 StageDetail）
    sd.BatchNo, sd.StageScopeType, sd.StageSeq,
    sd.StageCode            AS AggregatedStageCode,
    sd.IsSupplyThreshold
FROM MES_BOM_View bv
LEFT JOIN MES_ProcessCode_View pc_g ON pc_g.ProcessCode = bv.GoodsProcCode
LEFT JOIN MES_ProcessCode_View pc_m ON pc_m.ProcessCode = bv.MaterialProcCode
LEFT JOIN MES_APS_BOM_Workset_StageDetail sd
    ON  sd.BOMNO              = bv.BOMNO
    AND sd.ParentMaterialCode = bv.ParentMaterialCode
    AND sd.ChildMaterialCode  = bv.ChildMaterialCode
    AND sd.StageScopeType     = 'EDGE';
```

**字段分类**：

| 类别 | 字段 | 防腐风险 |
|---|---|---|
| BOM 边稳定字段 | `BOMNO / ParentMaterialCode / ChildMaterialCode / Quantity / *ProcRefCode / ChildSourceHintCode` | ✅ 稳定合同（MES_BOM_View 契约）|
| 父/子件工序派生 | `Parent*` / `Child*`（通过 MES_ProcessCode_View 契约视图吸震）| 🟡 字段改名由契约视图吸震，下游零改动 |
| 聚合大阶段关联 | `AggregatedStageCode / StageSeq / IsSupplyThreshold` | ✅ StageDetail 稳定合同（APS 语义）|

**使用场景**：
- ODS 内部运维诊断：排查 BOM 边对应的工序/工厂信息
- 委外 ShippingTask 生成器：按 `ParentStageFactory ≠ ParentActualFactory`（或子件侧同理）识别委外段
- BI 报表 / 数据审计：按工厂/工艺口径汇总

**禁止用途**：
- ❌ APS 排程内核直接查此视图（必须通过本地配置表）
- ❌ APS_Production 本地库创建对称视图（`vw_APS_BOM_Stage_Enriched` 不存在）
- ❌ 任何 APS 代码路径引用 `ProcessCode / ActualFactoryCode / TrusteeProcCode` 字段（防腐墙红线）

---

### 1.7 附录：Produce 字段值域枚举（v5.0.10 补全，v5.0.11 改为指向配置表）

**来源**：ERP BOM 的 `produce` 字段，通过 `MES_BOM_View.ChildSourceHintCode` 承接。

**⚠️ v5.0.11 变更**：Produce 值域与 R17 映射规则从"硬编码在代码/文档"升级为"配置表驱动"，权威表为 **§1.8 `ProduceToFactoryMap`**。本节仅列出**业务含义简表**，值域变更请以 `ProduceToFactoryMap` 表数据为准。

**R17/R20/R25/R26/R27 推导规则**：

| 规则 | 语义 |
|---|---|
| **R17** | Produce→账面工厂精确映射（实现：查 `ProduceToFactoryMap` 配置表）|
| **R20** | Produce ∈ {6,7,11} 内制他用→**本厂继续下钻 BOM**（拿下阶物料明细）+ 打 `CROSS_ORG_HANDOFF` 标记（v5.0.11：`ShouldDrilldown=1` + `CrossOrgHandoffFlag=1`；本厂不占产能，他用方工厂自行排产）。**目标工厂映射**（v5.0.14 照片权威纠正）：**6→BJ、7→CN、11→TJ**（原文档误为 6→BJ、7→TJ、11→SH，已修复）|
| **R25** | 异厂替代路径收敛：Produce=1 取父件工厂唯一路径；Produce ∈ {6,7,11} 保留全部候选 |
| **R26** | 过滤按 `代码所属工厂` 维度（账面），受托关系（`实际生产工厂/受托process`）不穿透，只作委外元数据 |
| **R27** | 异常分治降级（9 类 IssueType，详见 §1.5 处置矩阵）|

**引用经验库**：完整规则与案例见 `BOM展开经验库.md` R01~R27。  
**技术方案**：详见《BOM_Workset_生成与错误处理技术方案_v1.1》。

---

### 1.8 ProduceToFactoryMap（R17 规则资产化配置表）⭐ **v5.0.11 新增**

**所属库**：MES_Integration  
**业务用途**：将 R17 Produce→工厂映射规则从硬编码 → 配置表驱动；ERP 语义变化时 UPDATE 本表即可，不改代码不改 DDL。

**🎯 定位**：这是 R17 规则的**物理资产宿主**。5 号位 `sp_EnrichBOMWorkset` 回填 `ChildRequiredFactory` 时查询本表。

**维护责任**：0 号位审批 + 5 号位维护；**本表变更必须走评审流程**（影响所有 BOM 展开结果）。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| ProduceCode | Produce 编码 | TINYINT | 主键，0-11（ERP BOM 的 produce 值）| 11 |
| ProduceName | 业务名称 | NVARCHAR(50) | 业务可读名（v5.0.14 按照片重写）| TJ天津工厂内制他用 |
| SourceCategory | 来源分类 | NVARCHAR(30) | **v5.0.14 收敛为 3 类**：`PURCHASE`（购入，R07 不下钻）/ `INHOUSE_SELF`（内制自用，本厂排产）/ `INHOUSE_CROSS`（内制他用，R20 跨厂交接） | INHOUSE_CROSS |
| FactoryStrategy | 工厂推导策略 | NVARCHAR(20) | **INHERIT**（继承父件工厂）/ **FIXED**（固定工厂）/ **NONE**（外购不推导）| FIXED |
| TargetFactory | 目标工厂 | NVARCHAR(20) | Strategy=FIXED 时生效（CN/CN6课/BJ/TJ/SH）；Strategy=INHERIT/NONE 时为 NULL | TJ |
| ShouldDrilldown | 是否下钻展开 | BIT | 1=继续展开子 BOM（本厂需要下阶物料明细）；0=BOM 下钻终止（**仅外购 R07**）| 1 |
| CrossOrgHandoffFlag | 跨组织交接标签 | BIT | **v5.0.11 新增**：1=打 `CROSS_ORG_HANDOFF` 标签（R20 内制他用：本厂仍下钻 BOM 拿到下阶明细，但该链最终归他用方工厂排产，不占本厂产能）；0=本厂自排 | 1 |
| Description | 说明 | NVARCHAR(500) | 含义 + 典型案例 | R20 跨厂跨域：本厂下钻+打标签，他用方排产 |
| IsActive | 是否启用 | BIT | 0=该 Produce 值废弃 | 1 |
| RuleVersion | 规则版本 | NVARCHAR(20) | R17 规则版本号；升级时递增便于追溯 | v1.0 |
| UpdatedBy | 更新人 | NVARCHAR(100) | 最后修改人工号 | U001 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-24 10:00:00 |

**初始化数据（v1.0，2026-04-24）**：

| ProduceCode | ProduceName | SourceCategory | Strategy | TargetFactory | ShouldDrilldown | CrossOrgHandoffFlag |
|---:|---|---|---|:---:|:---:|:---:|
| 0 | 日本保税/国内保税 | PURCHASE | NONE | NULL | 0 | 0 |
| 1 | 中国/北京/天津内制自用 | INHOUSE_SELF | INHERIT | NULL | 1 | 0 |
| 2 | 国内课税 | PURCHASE | NONE | NULL | 0 | 0 |
| 3 | 海外保税（日本以外）| PURCHASE | NONE | NULL | 0 | 0 |
| 4 | 海外课税 | PURCHASE | NONE | NULL | 0 | 0 |
| 5 | CN 制造 6 课内制自用 | INHOUSE_SELF | FIXED | CN6课 | 1 | 0 |
| **6** | BJ北京工厂内制他用 | INHOUSE_CROSS | FIXED | **BJ** | 1 | **1** |
| **7** | CN中国公司内制他用 | INHOUSE_CROSS | FIXED | **CN** | 1 | **1** |
| 8 | CN 制造 6 课内制 ASSY 自用 | INHOUSE_SELF | FIXED | CN6课 | 1 | 0 |
| 9 | SH上海公司内制自用 | INHOUSE_SELF | FIXED | SH | 1 | 0 |
| 10 | 海外课税（适用范围与 Produce=4 不同）| PURCHASE | NONE | NULL | 0 | 0 |
| **11** | TJ天津工厂内制他用 | INHOUSE_CROSS | FIXED | **TJ** | 1 | **1** |

**v5.0.11 口径收敛说明（R20 正确语义）**：
- **本厂仍下钻 BOM**（`ShouldDrilldown=1`）：业务上本厂需要拿到下阶物料明细，用于成本核算、提前期估计、物料需求汇总
- **打跨厂交接标签**（`CrossOrgHandoffFlag=1`）：标记"该链最终归他用方工厂排产"，本厂**不占自身产能**
- 两个字段**职责正交**：
  - `ShouldDrilldown` = "BOM 展开控制"（仅外购 R07 终止）
  - `CrossOrgHandoffFlag` = "排产归属标签"（R20 内制他用 vs 本厂自排）
- 5 号位消费方：`IF CrossOrgHandoffFlag=1` → 在 Workset/Issues 标记该子件；1 号位看到此标签 → **不在本厂产生 Task**，由他用方工厂自行排产（或产出"他用方产能占用"虚拟需求）

**字段语义详解**：
- **`SourceCategory` 3 种**（v5.0.14 从 4 类收敛为 3 类，取消自创的 `INHOUSE_SPECIAL`，严格对齐照片业务分类 + 保持与 R07/R20 业务约束正交）：
  - `PURCHASE`：购入件（对应 Produce ∈ {0,2,3,4,10}，含保税/课税两子分类，对 APS 排程透明），BOM 下钻终止（R07）
  - `INHOUSE_SELF`：内制·自用（对应 Produce ∈ {1,5,8,9}），本厂排产；Produce=1 INHERIT 父工厂，5/8→CN6课，9→SH
  - `INHOUSE_CROSS`：内制·他用（对应 Produce ∈ {6,7,11}），R20 跨厂交接（6→BJ / 7→CN / 11→TJ）
- **`FactoryStrategy` 3 种**：
  - `INHERIT`：查 R21 算法——优先父件 GoodsProcCode 所属工厂，回退 MaterialProcCode 所属工厂
  - `FIXED`：直接使用 `TargetFactory` 列值
  - `NONE`：外购不推导工厂，`ChildRequiredFactory=NULL`

**ERP 语义升级示例**：
- 场景 1：ERP 新增 Produce=12（某新特注类别）→ `INSERT INTO ProduceToFactoryMap VALUES (12, ..., ...)`，不改代码
- 场景 2：ERP 调整 Produce=6 的目标工厂从 BJ 改到新厂 → `UPDATE ProduceToFactoryMap SET TargetFactory='新厂' WHERE ProduceCode=6`，历史已展开 Workset 不变，下次批次自动应用新规则

---

### 1.9 StageDict（StageCode 全局字典表）⭐ **v5.0.11 新增，v5.0.12 补 R20 视角约定，v5.0.19 业务数据初始化，v5.0.20 阶段细化拆分（2026-05-06 更新）**

**所属库**：MES_Integration  
**业务用途**：StageCode（大工艺阶段码）的**唯一权威字典**；命名方案 = **工厂+阶段码**（方案 B）；同时是 **BOM↔Routing 对接主键之二**（主键之一为 MaterialCode）。

**🎯 定位**：
- 现状 `RoutingStage` 是物料级阶段配置（每个物料的阶段列表），StageCode 本身过去**没有全局字典**
- 本表补齐全局字典，成为以下表的**权威引用源**：
  - `RoutingStage.StageCode`（物料级阶段配置）
  - `RoutingOperation.StageCode`（v5.0.12 显式引用；BOM↔Routing 对接主键）
  - `MES_APS_BOM_Workset_StageDetail.StageCode`（BOM 派生结果）
  - `APS_BOM_STAGE_PATH_RAW.StageCode`（APS 本地缓存）
  - `StageLeadTimeParam.StageCode`（阶段提前期参数）
  - `ProcessCodeDict.StageCode`（🆕 v5.0.16：ERP 工序码→大阶段的映射基础）

**维护责任**：0 号位审批 + 3 号位/5 号位协同维护。  
**扩展策略**：新增阶段由业务审定后 `INSERT` 一行即可，不改 DDL/代码。

**命名规范**：`{工厂短码}_{阶段类别}`，如 `CN_MACH`、`TJ_OUTS`、`BJ_SURF`；CN6课 → 前缀 `CN6_`（技术键不含中文）

**⚠️ v5.0.12 跨组织视角约定（R20 专项）**：
- BOM 侧 `StageDetail.StageCode` 采用**目标工厂视角**
- 场景示例：父件在 TJ 工厂，子件 Produce=6（R20 指派到 BJ 工厂）
  - ✅ 正确：`StageDetail.StageCode = BJ_MACH`（他用方视角）
  - ❌ 错误：`StageDetail.StageCode = TJ_MACH`（本厂视角）
- 理由：1 号位读到 BJ_MACH 可直接去 BJ 工厂的 `RoutingOperation` 找小工序，**无需再做跨厂翻译**
- 落地位置：5 号位 `sp_EnrichBOMWorkset` 回填时，读 `ChildRequiredFactory` 决定 StageCode 的工厂前缀

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| StageCode | 阶段码 | NVARCHAR(20) | 主键，格式 `{工厂}_{阶段类别}` | CN_MACH |
| StageName | 阶段中文名 | NVARCHAR(50) | 业务可读名 | CN机加 |
| FactoryCode | 工厂代号 | NVARCHAR(20) | 独立字段便于按厂过滤；值域 BJ/CN/CN6课/TJ/SH（v5.0.19 基于工序对照表实际数据，不含 JP） | CN |
| StageCategory | 阶段类别 | NVARCHAR(20) | MACH/MOLD/CAST/DRAW/FORGE/EXTRU/OUTS/ASSY/SURF/FINAL + 预留 INSP/CLEAN | MACH |
| CategoryName | 类别中文名 | NVARCHAR(50) | 机加/注塑/铸造/冷拔/锻造/型材押出/外协/组立/表面处理/出口 | 机加 |
| SortHint | 默认排序 | INT | 同工厂内的默认顺序（仅参考；实际顺序以 StageDetail.StageSeq 为准）| 10 |
| Description | 说明 | NVARCHAR(200) | 业务说明 | CN 工厂机加阶段 |
| IsActive | 是否启用 | BIT | 0=该 StageCode 废弃 | 1 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-24 10:00:00 |

**StageCategory 枚举（v5.0.20 更新）**：

| StageCategory | 类别中文 | 语义 | 当前状态 |
|---|---|---|---|
| MACH | 机加 | 一般机械加工（车削/铣削/钻孔等）| ✅ 5 厂均有 |
| **MOLD** | **注塑** | 注塑/树脂成型（8课）| 🆕 BJ |
| **CAST** | **铸造** | 铝铸造/锌铸造/熔铸（7课）| 🆕 BJ/TJ |
| **DRAW** | **冷拔** | 冷拔/拉拔 | 🆕 BJ（暂无 ProcessCode） |
| **FORGE** | **锻造** | 锻造/热处理/缸筒挤压 | 🆕 BJ/CN |
| **EXTRU** | **型材押出** | 型材挤压/押出成型 | 🆕 BJ |
| OUTS | 外协 | 外包加工（**外协判定：StageCategory='OUTS'**；StageLeadTimeParam 参数化）| ✅ 5 厂均有 |
| ASSY | 组立 | 组装/装配 | ✅ 5 厂均有 |
| **SURF** | **表面处理** | 涂装/氧化/喷丸（原 PAINT 重命名）| ✅ BJ/CN/TJ 有 |
| FINAL | 出口 | 出口完工（制品/部品出库）| ✅ BJ/CN/TJ/SH 有 |
| INSP | 检查 | 质量检查 | ⏸️ 预留 |
| CLEAN | 清扫 | 清洗/清洁处理 | ⏸️ 预留 |

**初始化数据（v5.0.20，2026-05-06，29 条）**：

| StageCode | StageName | FactoryCode | Category | SortHint | 对应 ProcessCode 数 |
|---|---|---|---|---:|---:|
| BJ_MACH | BJ机加 | BJ | MACH | 10 | 20 |
| **BJ_MOLD** | **BJ注塑** | BJ | **MOLD** | 11 | 7 |
| **BJ_CAST** | **BJ铸造** | BJ | **CAST** | 12 | 10 |
| **BJ_DRAW** | **BJ冷拔** | BJ | **DRAW** | 13 | 0 |
| **BJ_FORGE** | **BJ锻造** | BJ | **FORGE** | 14 | 1 |
| **BJ_EXTRU** | **BJ型材押出** | BJ | **EXTRU** | 15 | 2 |
| BJ_OUTS | BJ外协 | BJ | OUTS | 20 | 11 |
| **BJ_SURF** | **BJ表面处理** | BJ | **SURF** | 35 | 5 |
| BJ_ASSY | BJ组立 | BJ | ASSY | 40 | 7 |
| BJ_FINAL | BJ出口 | BJ | FINAL | 60 | 2 |
| CN_MACH | CN机加 | CN | MACH | 10 | 11 |
| **CN_FORGE** | **CN锻造** | CN | **FORGE** | 14 | 2 |
| CN_OUTS | CN外协 | CN | OUTS | 20 | 5 |
| **CN_SURF** | **CN表面处理** | CN | **SURF** | 35 | 5 |
| CN_ASSY | CN组立 | CN | ASSY | 40 | 7 |
| CN_FINAL | CN出口 | CN | FINAL | 60 | 2 |
| CN6_MACH | CN6课机加 | CN6课 | MACH | 10 | 5 |
| CN6_OUTS | CN6课外协 | CN6课 | OUTS | 20 | 1 |
| CN6_ASSY | CN6课组立 | CN6课 | ASSY | 40 | 2 |
| TJ_MACH | TJ机加 | TJ | MACH | 10 | 13 |
| **TJ_CAST** | **TJ铸造** | TJ | **CAST** | 12 | 2 |
| TJ_OUTS | TJ外协 | TJ | OUTS | 20 | 12 |
| **TJ_SURF** | **TJ表面处理** | TJ | **SURF** | 35 | 4 |
| TJ_ASSY | TJ组立 | TJ | ASSY | 40 | 6 |
| TJ_FINAL | TJ出口 | TJ | FINAL | 60 | 2 |
| SH_MACH | SH机加 | SH | MACH | 10 | 2 |
| SH_OUTS | SH外协 | SH | OUTS | 20 | 1 |
| SH_ASSY | SH组立 | SH | ASSY | 40 | 2 |
| SH_FINAL | SH出口 | SH | FINAL | 60 | 2 |

**⚠️ v5.0.20 变更说明（相对 v5.0.19）**：
- **新增 7 条 StageCode**：BJ_MOLD / BJ_CAST / BJ_DRAW / BJ_FORGE / BJ_EXTRU / TJ_CAST / CN_FORGE
- **PAINT→SURF 重命名**：BJ_PAINT→BJ_SURF / CN_PAINT→CN_SURF / TJ_PAINT→TJ_SURF（表面处理 = 涂装+氧化+喷丸）
- **MACH 语义收窄**：BJ_MACH 40→20、TJ_MACH 15→13、CN_MACH 13→11（特殊制造工艺已独立）
- **未拆分暂留**：TJ 型材(340198/340199/341198)→TJ_MACH；CN 拉拔(510899)→CN_MACH

**🧭 v5.0.15 字段净化说明**（StageDict 只承载阶段自身属性）：

| 原字段 | 处置 | 替代承接位置 |
|---|---|---|
| ~~`IsOutsource`~~ | **删除** | 下游改查 `StageCategory = 'OUTS'`（语义重复）|
| ~~`IsStockPoint`~~ | **删除**（物料×阶段属性错位）| 物料稳态配置：`RoutingStage.IsStockPoint`（3 号位维护）<br>BOM 边级派生：`StageDetail.IsSupplyThreshold`（5 号位回填，已存在）|

**分层承接原则**：
- 「阶段自身属性」（不依赖物料）→ StageDict
- 「物料×阶段联合属性」（同一阶段对不同物料取值不同）→ RoutingStage / StageDetail / StageLeadTimeParam
- 任何字段要进 StageDict 前，先问：**同一个 StageCode 对所有物料取值都相同吗？** 若否，字段错位，不应进入本字典。

**⚠️ 初始化数据说明**：v5.0.20 已完成初始化（29 条 = v5.0.19 的 22 条 + v5.0.20 阶段细化拆分 7 条）。后续新增阶段按业务实际 INSERT 补行。

**索引**：`(FactoryCode, SortHint) WHERE IsActive=1` / `(StageCategory) WHERE IsActive=1`

**外键引用策略**：当前版本**不强制外键约束**（避免 BOM 展开时引用未注册 StageCode 导致整批失败，而是触发 UNKNOWN_PROCCODE Issue 降级）；v2 视稳定性决定是否加 FK。

---

### 1.9b ProcessTypeDict（工序级分类标签字典）⭐ **v5.0.12 新增（预留骨架）**

**所属库**：MES_Integration  
**业务用途**：`RoutingOperation.ProcessType` 的**配置化值域字典**；用于报表/粗分组/统计；**不参与 BOM↔Routing 对接，不作为 1 号位排程主键**。

**🎯 定位**：
- v5.0.12 之前 ProcessType 值域硬编码（MACHINING / ASSEMBLY / INSPECTION），扩展需改代码/DDL
- 本表让 ProcessType 值域可配置化，与 `ProduceToFactoryMap` / `StageDict` 形成统一的"规则资产化"风格
- **骨架期（v1.0）`IsActive=0` 预留**，业务确认启用后 `UPDATE IsActive=1` 生效；**骨架期不加 CHECK/FK**，不影响现有数据

**维护责任**：0 号位审批 + 3 号位维护。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| ProcessType | 分类标签值 | NVARCHAR(50) | 主键，如 MACHINING / ASSEMBLY | MACHINING |
| ProcessTypeName | 中文名 | NVARCHAR(100) | 业务可读名 | 机加工类 |
| Category | 一级归类 | NVARCHAR(30) | PRODUCTION / SUPPORT / QA / LOGISTICS（可扩展）| PRODUCTION |
| Description | 说明 | NVARCHAR(500) | 典型包含的 OperationName 示例 | NC / MC / 切断 / 精修 / 铣 / 钻 |
| IsActive | 是否启用 | BIT | **v1.0 骨架默认 0**；业务启用时改 1 | 0 |
| UpdatedBy | 更新人 | NVARCHAR(100) | 最后修改人工号 | U001 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-24 10:00:00 |

**初始化骨架（v1.0，2026-04-24，`IsActive=0`）**：

| ProcessType | ProcessTypeName | Category | 典型 OperationName 举例 |
|---|---|---|---|
| MACHINING | 机加工类 | PRODUCTION | NC / MC / 切断 / 精修 / 铣 / 钻 |
| ASSEMBLY | 装配类 | PRODUCTION | 组立 / 压装 / 拧紧 / 接合 |
| INSPECTION | 检验类 | QA | 首件检查 / 终检 / 抽检 / 测量 |
| OUTSOURCE | 外协类 | PRODUCTION | 外协加工相关工序（对应 StageDict.StageCategory='OUTS'；v5.0.15 IsOutsource 字段已删除）|
| PAINT | 涂装类 | PRODUCTION | 底漆 / 面漆 / 烘烤 |
| CLEANING | 清洗/清扫类 | SUPPORT | 超声清洗 / 去毛刺 / 擦净 |

**与三层模型的关系**：

| 层 | 字段 | 粒度 | 值域示例 | 是否参与排程对接 |
|---|---|---|---|---|
| 第 1 层：具体工序 | `OperationCode` / `OperationName` | 执行粒度 | NC / MC / 切断 / 精修 | ✅ 1 号位按 RoutingDependency 生成 Task |
| 第 2 层：辅助分类 | `ProcessType` | 报表粒度 | MACHINING / ASSEMBLY（本字典）| ❌ **不参与**；仅统计/粗分组 |
| 第 3 层：大工艺 | `StageCode` | BOM↔Routing 对接粒度 | TJ_MACH / BJ_SURF（StageDict）| ✅ BOM↔Routing 对接主键之二 |

**三层硬红线**（不可违反）：
- ❌ 禁止把 `OperationName` 值塞进 `ProcessType`（"NC" 不能当 ProcessType）
- ❌ 禁止把 `ProcessType` 当 `StageCode`（"MACHINING" 不能当 StageCode）
- ❌ 禁止把 `StageCode` 当 `OperationCode`（"TJ_MACH" 不是小工序）

---

### 1.9d ProcessCodeDict（工序码字典·**APS 自维护 ODS 增强字典**）⭐ **v5.0.15 新增；v5.0.16 定位翻转；v5.0.19 业务数据初始化（2026-05-06 更新）**

**所属库**：MES_Integration（ODS 层）  
**维护方式**：ProcessCodeDict 是 ODS 增强字典，字段分两类：

1. **APS 增强维护字段**：StageCode、CodeOrigin、UpdatedBy、IsActive 等由 APS 系统管理员维护，0号位审批。这些字段不得被自动同步覆盖。`sp_SyncMasterData(@SourceType='ProcessCode')` 不恢复，不做整表自动同步。

2. **ERP 真实属性字段**：ERPProperty 来源于 ERP 真实仓库/工序属性，由 5号位同步/透出维护。2号位只通过 `MES_ProcessCode_View.ERPProperty` 消费。不根据 WarehouseRole / ProcessName 推导。ERPProperty 的专用同步只允许更新 ERPProperty，不得覆盖 StageCode 等 APS 增强字段。

**🔄 口径演进**：v5.0.15 曾错位为「ERP镜像」→ v5.0.16 改为自维护字典 → v5.0.46 新增 ERPProperty（5号位从ERP真实属性同步）。
- 同步删除：`LastSyncedAt`（无同步时间戳）；`SourceSystem` 重命名为 `CodeOrigin`（值域 ERP/MES/MANUAL）；新增 `StageCode` 列（APS 增强）+ `UpdatedBy`

**🎯 定位**：
- 上游：APS 系统管理员（参考 ERP/MES 工序对照表语义但**不自动同步**）
- 本表：APS 自维护增强字典（含 ERP 业务字段镜像 + APS 增强映射列）
- 下游：一律通过 §1.9e `MES_ProcessCode_View` 契约视图消费，**不直查本物理表**

**⭐ APS 增强列**：`StageCode`（v5.0.16 新增，软引用 `StageDict.StageCode`）—— 把 ProcessCode → StageCode 的基础映射沉淀在本字典；
- 5 号位 `sp_EnrichBOMWorkset` 与 2 号位 `sp_RebuildMaterialStageDeptContext` **统一查** `MES_ProcessCode_View.StageCode`，**确保两边映射一致**（防止静默断裂）

**🔒 消费边界**：
- ✅ ODS 内部：5 号位 `material_stage_chain` 推导、2 号位 `MaterialStageDeptContext` 组装、委外 ShippingTask 生成器、`vw_MES_BOM_Stage_Enriched`、运维诊断、BI 报表
- ❌ **APS_Production 库严禁查询本表及其视图**——ProcessCode 是 ERP 易变维度，下沉 APS 将打穿防腐墙

**维护责任**：APS 增强字段（StageCode/CodeOrigin/UpdatedBy/IsActive等）由系统管理员维护+0号位审批，不得被自动同步覆盖；ERPProperty 由5号位专用同步/透出维护，仅更新ERPProperty。`sp_SyncMasterData(@SourceType='ProcessCode')` 不恢复

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| ProcessCode | 工序码 | NVARCHAR(20) | 主键；6 位左补 0（与 ERP 工序对照表主键一致）| 010496 |
| ProcessName | 工序名称 | NVARCHAR(100) | 业务可读名 | 外协机加 |
| FactoryCode | 代码所属工厂 | NVARCHAR(20) | 账面工厂（R26 过滤维度）| CN |
| ActualFactoryCode | 实际生产工厂 | NVARCHAR(20) | 含受托；与 FactoryCode 不同即委外 | BJ |
| TrusteeProcCode | 受托对方工艺 | NVARCHAR(20) | 仅受托场景非空 | 370694 |
| IsOutsource | 是否外协工序 | BIT | 工序级外协判定 | 1 |
| IsRetouch | 是否追加工 | BIT | ERP 原生"是否追加工" | 0 |
| WarehouseRole | 仓库角色 | NVARCHAR(30) | 领料位 / ASSY品库 / 追加工现场库 等 | 领料位 |
| **StageCode** 🆕 | **大工艺阶段码** | NVARCHAR(20) | **v5.0.16 新增 APS 增强列**：ProcessCode → StageCode 共享基础映射；软引用 StageDict.StageCode（不强 FK，避免阶段未注册时阻塞） | TJ_MACH |
| **CodeOrigin** 🔄 | **条目业务来源标记** | NVARCHAR(20) | **v5.0.16 RENAME from SourceSystem**；CHECK 约束值域 `ERP / MES / MANUAL`；语义=这条 ProcessCode 在业务上是 ERP 原生 / MES 补充 / APS 手工创建 | ERP |
| **ERPProperty** 🆕 | **仓库/工序位置业务属性** | NVARCHAR(20) | **v5.0.46 新增**：值域 M/XC/ZP/BP；来自 ERP 真实属性，5号位同步透出维护，2号位通过 MES_ProcessCode_View 消费；不根据 WarehouseRole/ProcessName 推导 | M |
| IsActive | 是否启用 | BIT | 1=启用 | 1 |
| **UpdatedBy** 🆕 | **维护人工号** | NVARCHAR(100) | **v5.0.16 新增**：替代 LastSyncedAt 同步时间戳的语义 | U001 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-29 10:00:00 |

> ⚠️ **v5.0.16 删除**：`LastSyncedAt` 字段（无自动同步机制，不需要同步时间戳）

**索引**：
- `IX_ProcessCodeDict_Factory` ON `(FactoryCode) WHERE IsActive=1`
- `IX_ProcessCodeDict_Actual` ON `(ActualFactoryCode) WHERE IsActive=1 AND ActualFactoryCode IS NOT NULL`
- 🆕 `IX_ProcessCodeDict_StageCode` ON `(StageCode) WHERE IsActive=1 AND StageCode IS NOT NULL`（v5.0.16：5 号位反查"哪些 ProcessCode 归属 X StageCode"）

**⚠️ 维护方式**：字段分两类——（1）APS增强字段（StageCode/CodeOrigin/UpdatedBy/IsActive）：系统管理员人工维护，0号位审批，禁止自动同步覆盖；（2）ERP真实属性字段（ERPProperty）：5号位从ERP同步/透出，专用同步只更新ERPProperty，不覆盖APS增强字段。`sp_SyncMasterData(@SourceType='ProcessCode')` 不恢复。
- 维护流程：管理员录入条目 → 0 号位审批（含 StageCode 归属确认）→ IsActive=1 启用
- APS 增强字段变更（含 StageCode / CodeOrigin / UpdatedBy / IsActive 等）必须经0号位审批，且不得被自动同步覆盖。ERPProperty 来源于ERP真实仓库/工序属性，由5号位专用同步/透出维护；ERPProperty 专用同步只允许更新 ERPProperty，不得覆盖 StageCode 等 APS增强字段。`sp_SyncMasterData(@SourceType='ProcessCode')` 不恢复，不做整表自动同步。

**📊 初始化数据（v5.0.20，2026-05-06 基于工序对照表.xlsx 生成，含阶段细化拆分）**：

| 工厂 | 条数 | StageCode 分布 | 示例 ProcessCode |
|---|---:|---|---|
| BJ | 65 | MACH×20 / **MOLD×7** / **CAST×10** / **FORGE×1** / **EXTRU×2** / OUTS×11 / **SURF×5** / ASSY×7 / FINAL×2 | 010499(机加) / 010399(注塑) / 011299(铸造) / 010599(表面) |
| CN | 32 | MACH×11 / **FORGE×2** / OUTS×5 / **SURF×5** / ASSY×7 / FINAL×2 | 510499(机加) / 510298(锻造) / 510599(表面) / 511498(外协) |
| CN6课 | 9 | MACH×5 / OUTS×1 / ASSY×2 / 无SURF·FINAL | 540489(机加) / 540495(电镀外协) / 540789(组装) |
| TJ | 39 | MACH×13 / **CAST×2** / OUTS×12 / **SURF×4** / ASSY×6 / FINAL×2 | 310499(机加) / 311299(铸造) / 310599(表面) / 310494(外协) |
| SH | 7 | MACH×2 / OUTS×1 / ASSY×2 / FINAL×2 / 无SURF | 640489(机加) / 640495(电镀外协) / 640789(组装) |
| **合计** | **152** | — | 3 个重复码（010593/020593/070693）取第一条 |

**⚠️ 重复码处理**：工序对照表中 010593/020593/070693 各有 2 行（原工序 + 受托库版本），INSERT 取第一条，受托库版本以注释保留于 DDL 末尾

---

### 1.9e MES_ProcessCode_View（工序码契约视图·Socket-Plug）⭐ **v5.0.15 新增；v5.0.16 字段契约升级**

**所属库**：MES_Integration（ODS 层）  
**视图类型**：Socket-Plug **契约视图**（A 类，防腐入口）  
**业务用途**：`ProcessCodeDict` 物理表的**防腐契约投影**；所有 ODS 内部消费方一律查本视图。

**🎯 吸震机制**：业务字段语义升级时，由 DBA **改本视图 SELECT 别名**吸收，下游消费方代码**零改动**。

**同级防腐视图**：`MES_BOM_View` / `ERP_Master_View` / `MES_Material_View`（同属 Socket-Plug 契约视图家族）

**字段契约（对 ODS 内部承诺稳定，v5.0.16 升级）**：
```sql
ProcessCode / ProcessName / FactoryCode / ActualFactoryCode / TrusteeProcCode
IsOutsource / IsRetouch / WarehouseRole
StageCode    -- 🆕 v5.0.16：APS 增强列；ProcessCode → StageCode 共享基础映射
CodeOrigin   -- 🔄 v5.0.16 RENAME from SourceSystem；值域 ERP/MES/MANUAL
ERPProperty  -- 🆕 v5.0.46：仓库/工序位置业务属性；值域 M/XC/ZP/BP
```

**视图定义**：
```sql
CREATE OR ALTER VIEW dbo.MES_ProcessCode_View AS
SELECT ProcessCode, ProcessName, FactoryCode, ActualFactoryCode,
       TrusteeProcCode, IsOutsource, IsRetouch, WarehouseRole,
       StageCode,    -- 🆕 v5.0.16
       CodeOrigin,   -- 🔄 v5.0.16 RENAME
       ERPProperty   -- 🆕 v5.0.46
FROM ProcessCodeDict
WHERE IsActive = 1;
```

**🔑 v5.0.16 关键约定**：本视图的 `StageCode` 列是 5 号位与 2 号位**共享的基础映射来源**——两边的 ProcessCode→StageCode 必须查同一列，**禁止各写一套规则**（防止静默断裂）。

**消费方一览**：

| 消费方 | 用途 |
|---|---|
| 5 号位 `sp_EnrichBOMWorkset` | 按工序码过滤 BOM 边、判定工厂归属、查 `StageCode` 共享映射 |
| 2 号位 `sp_RebuildMaterialStageDeptContext` 🆕 | 组装 MaterialStageDeptContext 时查 `StageCode` 共享映射 |
| 委外 ShippingTask 生成器 | 识别 `FactoryCode ≠ ActualFactoryCode` 段 |
| `vw_MES_BOM_Stage_Enriched`（§1.6）| BOM 边级派生字段 |
| 运维诊断 / BI 报表 | 按工序口径查询 |

**⚠️ 消费红线**：
- ❌ 任何 APS_Production 库存储过程/视图不得引用本视图
- ❌ 禁止绕过本视图直查 `ProcessCodeDict` 物理表（防腐契约失效）
- ❌ 5 号位 / 2 号位**禁止各自维护一份独立 ProcessCode→StageCode 映射**（必须查本视图 StageCode 列，否则两边映射不一致会造成 1 号位按 (Material, StageCode) 锁部门时静默断裂）

---

### 1.9c BOM↔Routing 对接模型（v5.0.12 新增说明）

**⚠️ 业务主键 vs 物理实现主键（开发必读）**：

| 视角 | 主键 | 说明 |
|---|---|---|
| **业务口径对接主键** | `(MaterialCode, StageCode)` | 跨文档/跨号位沟通、经验库规则、BOM↔Routing 语义锚点统一用此二元组 |
| **数据库运行态实际键** | `(MaterialId, StageCode)` | `RoutingOperation` / `RoutingStage` 等物理表实际落 `MaterialId`（INT 代理键）|
| **桥接关系** | `MaterialId = MaterialCode 的标准化代理键` | `MaterialCode` 通过 `MaterialMapping(Source, SourceID) → MaterialId` 做标准化入库（2 号位装载时完成）|

**开发实现一致性约定**：
- 文档写 `(MaterialCode, StageCode)` ≡ 代码写 `(MaterialId, StageCode)`（MaterialId 是 MaterialCode 的物理代理）
- 1 号位/2 号位实现时：**日志/异常/Issues 登记一律带上 MaterialCode**（人可读）；**物理表 JOIN / 索引 / 约束用 MaterialId**（性能）
- 不存在"以 MaterialCode 为物理键"的表；也不存在"以 MaterialId 为业务沟通键"的文档

**对接主键 = `(MaterialCode, StageCode)`**（二元组，业务口径）

**1 号位消费流程**：

```
BOM 侧（StageDetail）给出"某物料经过哪些大工艺，顺序如何"：
  FOR each (MaterialCode, StageCode, StageSeq) IN StageDetail
    ORDER BY StageSeq:                                         -- BOM 权威顺序
  
      Routing 侧（RoutingOperation）给出"该大工艺下有哪些小工序"：
      ops = SELECT * FROM RoutingOperation
            WHERE MaterialId = @MatId AND StageCode = @Stage   -- ← 对接主键
  
      FOR each op IN ops:
          生成 Task（op.OperationCode / op.StandardDuration / op.SetupTime）
          按 RoutingDependency 决定小工序之间的串并行
  
      若该 StageCode 在 RoutingOperation 中无记录：
          查 StageLeadTimeParam 按参数化 LeadTime 生成单一 Task（外协阶段标准流程）
```

**R20 跨组织场景特殊处理**：
- 子件 `CrossOrgHandoffFlag=1` 时，`StageDetail.StageCode` 已采用目标工厂视角（如 BJ_MACH）
- 1 号位直接按上述流程去 BJ 工厂的 `RoutingOperation` 找小工序——**无需跨厂翻译逻辑**
- 本厂（TJ）不为此子件生成任何 Task；BJ 工厂的 Task 队列里出现该子件

---

## 二、BOM展开相关表（实时模式）

### 2.1 MES_API_BOM_Request_Realtime（实时BOM展开请求表）

**所属库**：MES_Integration  
**业务用途**：记录插单场景下的实时BOM展开请求（单个BOMNO）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 请求ID | BIGINT | 主键，自增 | 1 |
| BOMNO | BOM编号 | NVARCHAR(50) | 单个BOM编号 | BOM-10001 |
| RequestTime | 请求时间 | DATETIME2 | 插单触发时间 | 2026-03-05 10:30:00 |
| Status | 状态 | NVARCHAR(20) | PENDING、PROCESSING、READY、FAILED | READY |
| CompletedTime | 完成时间 | DATETIME2 | 展开完成时间 | 2026-03-05 10:30:05 |
| ExpandedRowCount | 展开后行数 | INT | 单个BOM展开后的行数 | 50 |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) | 失败时的错误描述 | NULL |
| RetryCount | 重试次数 | INT | 失败后的重试次数 | 0 |
| Priority | 优先级 | INT | 0=普通，1=紧急 | 1 |

**技术要点**：
- 性能目标：单个BOM展开控制在5秒内
- 优先级：紧急插单优先处理

---

### 2.2 MES_APS_BOM_Workset_Realtime（实时BOM展开结果工作集）

**所属库**：MES_Integration  
**业务用途**：存储实时BOM展开的结果

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | 成品或半成品编码 | MAT-10001 |
| ChildMaterialCode | 子物料编码 | NVARCHAR(50) | 半成品或原材料编码 | MAT-20001 |
| Quantity | 用量 | DECIMAL(18,6) | 单位用量，不累乘 | 2.5 |
| Level | BOM层级 | INT | BOM层级 | 1 |
| ParentProcRefCode | 父件工序参考码 | NVARCHAR(50) | v5.0.7 同Workset | OP-030 |
| ChildProcRefCode | 子件工序参考码 | NVARCHAR(50) | v5.0.7 同Workset | OP-020 |
| ChildSourceHintCode | 子件来源提示码 | NVARCHAR(50) | v5.0.7 同Workset（0-11编码） | 1 |
| ChildRequiredStageCode | 子件供给所需阶段码 | NVARCHAR(50) | v5.0.7 同Workset | TJ_OUTS |
| **ChildRequiredFactory** | **子件应归属账面工厂** | **NVARCHAR(20)** | **v5.0.10 同Workset** | **SH** |
| **RequestDetailId** | **来源追溯锚点** | **BIGINT** | **v5.0.26** 实时插单来源追溯；FK->MES_API_BOM_Request_Detail.Id；nullable | **102** |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 10:30:03 |

**技术要点**：
- 数据生命周期：展开完成后立即被APS库消费，消费后可删除
- v5.0.7：实时链路同样携带3辅助字段 + 回填结果 + StageDetail，供插单排程消费
- v5.0.10：同步回填 `ChildRequiredFactory`；异常登记到 `MES_APS_BOM_Workset_Issues`（§1.8），放行判定同批量链路
- v5.0.26：新增 `RequestDetailId`，实时插单来源直接可追溯，无需经 Request 表反查
- 索引优化：聚集索引 `(BOMNO, Level)`

---

## 三、日志表

### 3.1 MES_API_BOM_Request_Log（BOM展开日志表）

**所属库**：MES_Integration  
**业务用途**：记录BOM展开过程中的关键日志（用于问题排查）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 日志ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联批次（可为NULL） | 20260305_000000 |
| Message | 日志消息 | NVARCHAR(MAX) | 日志内容 | 批次20260305_000000开始处理 |
| CreatedAt | 创建时间 | DATETIME2 | 日志时间 | 2026-03-05 00:00:05 |

**技术要点**：
- 日志级别：INFO、WARNING、ERROR
- 保留周期：建议保留7天

---

## 四、ODS视图契约（防腐层核心）

### 4.1 MES_BOM_View（MES BOM视图契约）⚠️ v5.0.26降为兼容视图（2026-05-14更新）

**所属库**：MES_Integration  
**视图类型**：兼容视图（v5.0.26 已降级） 正式 BOM 防腐合同层已迁移到 `MES_BOM_Edge_Active` 物化边表；本视图保留为向后兼容  
**业务用途**：⚠️ v5.0.26起此视图为 `SELECT * FROM MES_BOM_Edge_Active` 兼容包装，不再直接 UNION ERP/MES 源表；字段契约不变，下游无需修改查询；**禁止对本视图做递归 CTE 展开**（sp_ExpandBOMBatch_vNext 直接读 MES_BOM_Edge_Active）

**⚠️ 重要说明**：此视图需要由**MES系统的DBA**在ODS库中创建，指向MES生产库的物理表。

**⚠️ 唯一默认版本裁决原则（v5.0 写死）**：
- MES_BOM_View **不是简单并表**，而是 ODS 已经完成胜出版裁决后的唯一 BOM 契约视图
- 如果 ERP BOM 和 MES BOM 同时存在有效版本，**必须先按明确优先级规则裁决出唯一一套**，再暴露为 `IsDefaultVersion=1`
- **绝不允许**让两个来源对同一型号（ParentMaterialCode + ChildMaterialCode）都成为默认版本
- 裁决规则由 ODS 内部实现（建议：MES BOM 优先于 ERP BOM；同来源按版本生效日期取最新），**不暴露 VersionPriority 为正式契约字段**
- 此原则直接影响 `sp_ExpandBOMBatch` 第2~N层递归展开（`WHERE IsDefaultVersion = 1`），若裁决不唯一则展开结果不确定

**视图定义示例**：
```sql
CREATE VIEW MES_BOM_View AS
SELECT 
    BOMNO,                      -- BOM编号
    ParentMaterialCode,         -- 父物料编码
    ChildMaterialCode,          -- 子物料编码
    Quantity,                   -- 单位用量
    IsActive,                   -- 是否启用
    IsDefaultVersion,           -- 是否默认版本（ODS裁决后唯一）
    ParentProcRefCode,          -- v5.0.7 父件工序参考码（ERP BOM原始辅助字段）
    ChildProcRefCode,           -- v5.0.7 子件工序参考码（同上）
    ChildSourceHintCode,        -- v5.0.7 子件来源提示码（当前来源=ERP BOM的produce字段，0-11编码；v5.0.10订正）
    SourceSystem,               -- v5.0 追溯增强：来源系统（'ERP' / 'MES'）
    SourceBOMId                 -- v5.0 追溯增强：源系统中的物理BOM主键/版本标识
FROM [MES_Production].[dbo].[T_BOM_Physical_Table]  -- ⚠️ 实际物理表名（ODS内部可能是多表UNION+裁决逻辑）
WHERE IsDeleted = 0;
```

**字段清单**：

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | 成品或半成品编码 | MAT-10001 |
| ChildMaterialCode | 子物料编码 | NVARCHAR(50) | 半成品或原材料编码 | MAT-20001 |
| Quantity | 用量 | DECIMAL(18,6) | 单位用量（不累乘） | 2.5 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| IsDefaultVersion | 是否默认版本 | BIT | ODS裁决后唯一胜出版=1；**同一型号绝不允许出现两条=1** | 1 |
| ParentProcRefCode | 父件工序参考码 | NVARCHAR(50) | v5.0.7 ERP BOM原始辅助字段，透传到Workset供5号位推导StageDetail | OP-030 |
| ChildProcRefCode | 子件工序参考码 | NVARCHAR(50) | v5.0.7 同上 | OP-020 |
| ChildSourceHintCode | 子件来源提示码 | NVARCHAR(50) | v5.0.7 当前来源=ERP BOM的produce字段（0-11编码，v5.0.10订正；详见字段说明文档 §3.1 Produce 值域枚举） | 1 |
| SourceSystem | 来源系统 | NVARCHAR(10) | v5.0 追溯增强字段（非运行必需）；'ERP' 或 'MES' | MES |
| SourceBOMId | 源系统BOM物理主键 | NVARCHAR(100) | v5.0 追溯增强字段；源系统中的原始BOM主键/版本标识，用于排查"胜出版来自哪条物理记录" | ERP-BOM-V3-20260401 |

**技术要点**：
- ⚠️ **v5.0.26变更**：`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` **禁止对此视图做递归 CTE**；请直接读 `MES_BOM_Edge_Active`（可索引，专为迭代展开优化）；`MES_BOM_View` 仅为字段契约的向后兼容包装
- 视图最小运行必需字段（9个）：BOMNO、ParentMaterialCode、ChildMaterialCode、Quantity、IsActive、IsDefaultVersion、ParentProcRefCode、ChildProcRefCode、ChildSourceHintCode
- 追溯增强字段（2个）：SourceSystem、SourceBOMId — 非运行必需，但强烈建议加入以支持版本裁决排查
- **VersionPriority 不暴露为正式契约字段**：版本优先级是 ODS 内部裁决逻辑的过程参数，不应泄露给下游

---

### 4.1b MES_BOM_Edge_Active（BOM物化防腐边表）⭐ **v5.0.26新增**（2026-05-14）

**所属库**：MES_Integration  
**视图类型**：物化边表（V1兼任防腐合同层 + 执行优化层）  
**业务用途**：V1 BOM 防腐层核心承载对象。由 `sp_RefreshBOMEdgeActive`（5号位实现业务映射逻辑）从 ERP/MES 多源 BOM 物理表刷新；面向 `sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` 迭代展开优化（专用索引，WHILE 循环每层 JOIN）。`MES_BOM_View` 降为兼容视图 `SELECT * FROM MES_BOM_Edge_Active`。

**⚠️ V1/V2边界**：V1 `MES_BOM_Edge_Active` 兼任合同层+执行优化层；V2 视需要拆出 `MES_BOM_Edge_Contract`（多源历史/非活跃版本/裁决过程/审计追溯）

**刷新控制**：展开前校验 `MES_BOM_Edge_RefreshLog` 最新记录 `Status='COMPLETED'`；FAILED 状态时禁止 Workset 消费半刷新数据

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BOMNO | BOM编号 | NVARCHAR(50) | 订单显式 BOMNO；NULL=纯物料路由 BOM 边 | BOM-10001 |
| ParentMaterialCode | 父件物料编码 | NVARCHAR(50) | 父件物料编码（标准化后） | MAT-10001 |
| ChildMaterialCode | 子件物料编码 | NVARCHAR(50) | 子件物料编码（标准化后） | MAT-20001 |
| Quantity | 单位用量 | DECIMAL(18,6) | 生产1父件需几子件，**不累乘** | 2.5 |
| ParentProcRefCode | 父件工序参考码 | NVARCHAR(50) | 原 ERP BOM 辅助字段（透传） | OP-030 |
| ChildProcRefCode | 子件工序参考码 | NVARCHAR(50) | 原 ERP BOM 辅助字段（透传） | OP-020 |
| ChildSourceHintCode | 子件来源提示码 | NVARCHAR(50) | 0-11 编码（原 ERP produce 字段标准化后）| 1 |
| SourceSystem | 数据来源系统 | NVARCHAR(20) | 'ERP' 或 'MES' | ERP |
| SourceBOMId | 源系统BOM主键 | NVARCHAR(100) | 源系统中的原始 BOM 物理主键（追溯用）| ERP-BOM-V3-20260401 |
| SourceLineNo | 源系统行号 | NVARCHAR(50) | 源系统中的原始行号（追溯用）| 10 |
| IsActive | 是否有效 | BIT | 1=有效；刷新时仅保留 IsActive=1 行 | 1 |
| IsDefaultVersion | 是否默认版本 | BIT | 双源裁决后唯一胜出版=1；**同一型号全局唯一** | 1 |
| EffectiveFrom | 有效期开始 | DATETIME2 | 源系统下发；NULL=不限开始 | 2026-01-01 |
| EffectiveTo | 有效期结束 | DATETIME2 | NULL=永久有效 | NULL |
| RefreshBatchNo | 刷新批次号 | NVARCHAR(50) | 写入本行的刷新批次号（格式 REF-{yyyyMMdd}-{seq}） | REF-20260514-001 |
| RefreshedAt | 最后刷新时间 | DATETIME2 | 本行最后刷新时间 | 2026-05-14 00:05:00 |
| CreatedAt | 创建时间 | DATETIME2 | 记录首次创建时间 | 2026-05-14 00:05:00 |

**索引**：
- 聚集 `(ParentMaterialCode, IsActive)`：`sp_ExpandBOMBatch_vNext` 每层 JOIN 主键
- 非聚集 `(BOMNO, ParentMaterialCode, IsActive)` WHERE BOMNO IS NOT NULL：BOMNO 寻址第1层
- 非聚集 `(ChildMaterialCode)` INCLUDE (ParentMaterialCode, BOMNO)：反向追溯
- 非聚集 `(SourceSystem, SourceBOMId)` WHERE SourceBOMId IS NOT NULL：追溯定位

**技术要点**：
- 裁决原则：`IsDefaultVersion=1` 全局唯一，`VersionPriority` 不暴露为契约字段
- `MES_BOM_View` 兼容包装：`SELECT BOMNO,ParentMaterialCode,ChildMaterialCode,Quantity,IsActive,IsDefaultVersion,ParentProcRefCode,ChildProcRefCode,ChildSourceHintCode,SourceSystem,SourceBOMId FROM MES_BOM_Edge_Active WHERE IsActive=1`
- **禁止**对 `MES_BOM_View` 做递归 CTE 展开；直接读 `MES_BOM_Edge_Active`（已建专项索引）

---

### 4.1c MES_BOM_Edge_RefreshLog（BOM边刷新控制日志表）⭐ **v5.0.26新增**（2026-05-14）

**所属库**：MES_Integration  
**业务用途**：记录每次 `sp_RefreshBOMEdgeActive` 的刷新状态，防止 Workset 消费半刷新数据。`sp_ExpandBOMBatch_vNext` 展开前必须查本表最新记录 `Status='COMPLETED'`，否则抛出异常阻止展开。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| RefreshBatchNo | 刷新批次号 | NVARCHAR(50) | 格式 REF-{yyyyMMdd}-{seq}，唯一约束 | REF-20260514-001 |
| RefreshType | 刷新类型 | NVARCHAR(20) | FULL=全量刷新 / INCREMENTAL=增量刷新 | FULL |
| Status | 状态 | NVARCHAR(20) | RUNNING=刷新中 / COMPLETED=成功 / FAILED=失败 | COMPLETED |
| StartTime | 开始时间 | DATETIME2 | 刷新启动时间 | 2026-05-14 00:00:00 |
| EndTime | 结束时间 | DATETIME2 | 完成/失败时间；NULL=仍在运行 | 2026-05-14 00:04:53 |
| RowCount | 写入行数 | INT | 本次刷新写入 MES_BOM_Edge_Active 的行数 | 15820 |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) | FAILED 时记录详细错误；COMPLETED 时 NULL | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-05-14 00:00:00 |

**索引**：非聚集 `(Status, StartTime DESC)`：查最新状态记录

---

### 4.2 ERP_Master_View（ERP主数据视图契约）⭐ 核心契约

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：向APS库暴露ERP系统的物料主数据，支持物料映射同步

**⚠️ 重要说明**：此视图需要由**ERP系统的DBA**在ODS库中创建，指向ERP数据库的物理表。

**视图定义示例**：
```sql
-- 契约版本：v1.5
-- v1.2更新：增加Spec、SupplyMode、ProductionDeptCode字段
-- v1.5更新（v5.0.38 V1口径）：5号位在ODS内部直接判断产品族；新增IsProductFamilyRequired/ProductFamilyCode/FamilyResolveStatus
CREATE VIEW ERP_Master_View AS
SELECT 
    m.MaterialCode,
    m.MaterialName,
    m.Spec,                  -- v1.2新增
    m.MasterID,
    m.Warehouse,
    m.SupplyMode,            -- v1.2新增
    m.ProductionDeptCode,    -- v1.2新增
    m.UOM,
    m.LeadTimeDays,
    m.SafetyStock,
    m.InventoryManagementMode,
    m.IsActive,
    -- v1.5新增：产品族解析三字段（5号位 ODS 内部判断，可读 ERP.ModelSort，不暴露给 APS 层）
    CAST(CASE WHEN m.ProdFinshProcCode IS NOT NULL THEN 1 ELSE 0 END AS BIT)
                             AS IsProductFamilyRequired,
    CASE
        WHEN m.ModelSort LIKE N'FRL%' THEN N'FRL'
        WHEN m.ModelSort LIKE N'CYL%' THEN N'CYL'
        ELSE NULL
    END                      AS ProductFamilyCode,
    CASE
        WHEN m.ProdFinshProcCode IS NULL              THEN N'NOT_REQUIRED'
        WHEN m.ModelSort LIKE N'FRL%'
          OR m.ModelSort LIKE N'CYL%'                THEN N'RESOLVED'
        WHEN m.ModelSort IS NULL OR m.ModelSort = N'' THEN N'SOURCE_FIELD_MISSING'
        ELSE N'NO_RULE'
    END                      AS FamilyResolveStatus
FROM [ERP_Database].[dbo].[ITMASTER] m   -- ⚠️ 实际表名由 5号位 ERP DBA 确认
WHERE m.IsDeleted = 0;
-- ⚠️ 以上为示意 SQL；实际字段名、判断条件由 5号位根据 ERP 实际结构定义
```

**字段清单**：

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务主键 | RAW-STEEL-001 |
| MaterialName | 物料名称 | NVARCHAR(200) | 物料描述 | 冷轧钢板 |
| Spec | 物料型号/规格 | NVARCHAR(100) | v1.2新增，如"C25ILB-005" | C25ILB-005 |
| MasterID | ERP主数据ID | INT | ERP系统中的物料物理主键 | 100001 |
| Warehouse | 仓库编码 | NVARCHAR(50) | ERP仓库（支持一物多仓） | WH-01 |
| SupplyMode | 供给方式 | NVARCHAR(20) | v1.2新增：PURCHASE/MAKE/OUTSOURCE | MAKE |
| ProductionDeptCode | 生产责任部门 | NVARCHAR(50) | v1.2新增：自制件的生产部门编码 | DEPT-PROD-A |
| UOM | 计量单位 | NVARCHAR(20) | 如：PCS、KG、M | PCS |
| LeadTimeDays | 提前期天数 | INT | 该物料在该仓库的提前期 | 7 |
| SafetyStock | 安全库存 | DECIMAL(18,4) | 该物料在该仓库的安全库存 | 500.0000 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| IsProductFamilyRequired | 是否需要产品族 | BIT NOT NULL | v1.5新增：5号位在ODS内部按ERP ProcessCode判断；1=需要，0=不需要 | 1 |
| ProductFamilyCode | 产品族编码 | NVARCHAR(50) NULL | v1.5新增：ODS内部解析出的APS标准产品族编码；RESOLVED时有值，其余为NULL | CYL |
| FamilyResolveStatus | 产品族解析状态 | NVARCHAR(30) NOT NULL | v1.5新增：RESOLVED/NOT_REQUIRED/NO_RULE/AMBIGUOUS/FAMILY_CODE_NOT_FOUND/SOURCE_FIELD_MISSING | RESOLVED |

**技术要点**：
- ⚠️ **P0-6修复**：存储过程 `sp_SyncMasterData(@SourceType='ERP')`（v4.0双源统一，2026-04-01 更新）必须通过跨库包装视图 `ext_ERP_Master_View` 访问此视图
- 支持一物多仓：同一个 `MaterialCode` 可以有多个 `Warehouse`
- v1.5（v5.0.38）：`IsProductFamilyRequired` / `ProductFamilyCode` / `FamilyResolveStatus` 由5号位在 ERP_Master_View 内部直接计算输出；`ext_ERP_Master_View` 显式列透传三新字段（不依赖 MaterialProductFamilyResolved 表）

---

### 4.3 MES_Material_View（MES物料视图契约）⭐ 核心契约（2026-04-01 v1.3同构化）

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：向APS库暴露MES系统的自建物料数据（不在ERP中的物料）

**⚠️ 重要说明**：此视图需要由**MES系统的DBA**在ODS库中创建，指向MES生产库的物理表。

**视图定义示例**（2026-05-30 v1.4产品族解析字段更新）：
```sql
-- 契约版本：v1.5
-- v1.3更新：字段与 ERP_Master_View 对齐，支持双源同构三表协同同步
--   保留 MES_ID、Location 原始列名（由 ext_MES_Material_View 做别名映射）
-- v1.5更新（v5.0.38 V1口径）：MES侧固定返回 IsProductFamilyRequired=0 / ProductFamilyCode=NULL / FamilyResolveStatus='NOT_REQUIRED'
CREATE VIEW MES_Material_View AS
SELECT 
    m.MaterialCode,
    m.MaterialName,
    m.Spec,
    m.MES_ID,                     -- MES物理主键（ext层别名为MasterID）
    m.Location,                   -- MES库位/仓库（ext层别名为Warehouse）
    m.SupplyMode,
    m.ProductionDeptCode,
    m.UOM,
    m.LeadTimeDays,
    m.SafetyStock,
    m.InventoryManagementMode,
    m.IsActive,
    -- v1.5新增：MES侧 V1 固定返回（同构占位，与 ERP_Master_View 字段对齐）
    CAST(0 AS BIT)               AS IsProductFamilyRequired,  -- MES V1固定=0
    CAST(NULL AS NVARCHAR(50))   AS ProductFamilyCode,        -- MES V1固定=NULL
    N'NOT_REQUIRED'              AS FamilyResolveStatus       -- MES V1固定
FROM [MES_Production].[dbo].[T_Material_Physical_Table] m   -- ⚠️ 实际表名由 5号位 MES DBA 确认
WHERE m.IsDeleted = 0;
-- ⚠️ 以上为示意 SQL；实际字段名由 5号位根据 MES 实际结构定义
```

**字段清单**（2026-04-01 v1.3同构化更新）：

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务主键 | WIP-ASSY-001 |
| MaterialName | 物料名称 | NVARCHAR(200) | 显示名称 | MES自制半成品 |
| Spec | 物料型号/规格 | NVARCHAR(100) | 如"C25ILB-005" | C25ILB-005 |
| MES_ID | MES物料ID | INT | MES物理主键（ext层别名为MasterID） | 5001 |
| Location | MES库位 | NVARCHAR(100) | MES库位/仓库（ext层别名为Warehouse） | 车间A-货架01 |
| SupplyMode | 供给方式 | NVARCHAR(20) | v1.3同构化新增 | MAKE |
| ProductionDeptCode | 生产部门代码 | NVARCHAR(50) | v1.3同构化新增 | DEPT-MES-01 |
| UOM | 计量单位 | NVARCHAR(20) | PCS/KG/M等 | PCS |
| LeadTimeDays | 提前期 | INT | v1.3同构化新增 | 3 |
| SafetyStock | 安全库存 | DECIMAL(18,4) | v1.3同构化新增 | 100 |
| InventoryManagementMode | 库存管理方式 | NVARCHAR(20) | STOCKED/NON_STOCKED（v1.3同构化新增） | STOCKED |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| IsProductFamilyRequired | 是否需要产品族 | BIT NOT NULL | v1.5新增：MES侧V1固定=0（MES自建物料不参与产品族解析） | 0 |
| ProductFamilyCode | 产品族编码 | NVARCHAR(50) NULL | v1.5新增：MES侧V1固定=NULL | NULL |
| FamilyResolveStatus | 产品族解析状态 | NVARCHAR(30) NOT NULL | v1.5新增：MES侧V1固定='NOT_REQUIRED'；与ERP侧同构占位 | NOT_REQUIRED |

**技术要点**（2026-05-30 v1.5更新）：
- ⚠️ 存储过程 `sp_SyncMasterData(@SourceType='MES')`（v4.0双源统一）通过 `ext_MES_Material_View` 访问此视图
- MES自建物料：通常是半成品或在制品，不在ERP系统中
- **v1.3同构化**：字段与 ERP_Master_View 对齐，MaterialType 移除（由APS前缀推导），新增供给属性字段
- **v1.5（v5.0.38）**：`IsProductFamilyRequired`=0 / `ProductFamilyCode`=NULL / `FamilyResolveStatus`='NOT_REQUIRED' 由 MES_Material_View 内部固定输出；`ext_MES_Material_View` 显式列透传三新字段（同构占位）

---

### 4.4 MES_APS_Routing_View（MES工艺路线视图契约）⚠️ **v5.0废弃（2026-04-01）**

> **⚠️ v5.0废弃**：原线性视图已拆分为三个独立视图（见 4.4b/4.4c/4.4d），支持工艺图模型。  
> 保留此节描述仅为兼容参考，新代码禁止引用 `MES_APS_Routing_View` 和 `ext_MES_APS_Routing_View`。

---

### 4.4b MES_APS_Routing_Operation_View（工序节点视图契约）⭐ **v5.0新增（2026-04-01）**

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：向APS库暴露工序节点数据，替代原 `MES_APS_Routing_View` 的工序定义部分  
**负责人**：**3号位**（梳理 MES 28 张离散工艺表）

**⚠️ 架构原则**：
- **v5.0.1变更（2026-04-02）**：ODS视图不再输出MaterialCode，改为输出`MES_ID`+`Model`（MES原生标识），2号位装载时通过`MaterialMapping(Source='MES', SourceID=MES_ID)`统一映射得到`MaterialId`
- **V1默认路径约束**：RouteCode='DEFAULT', PathId=1（只输出默认路径）
- **V2扩展**：输出多条路径，通过 IsDefaultPath/PathPriority 选择

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MES_ID | MES物料主键 | INT | MES物理主键（NOT NULL，3号位老结构ETL处理） | 500001 |
| Model | MES物料型号 | NVARCHAR(100) | MES原生型号 | MAT-A-001 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | V1固定'DEFAULT' | DEFAULT |
| PathId | 路径序号 | INT | V1固定1 | 1 |
| OperationCode | 工序编码 | NVARCHAR(50) | 路径内唯一 | OP-010 |
| OperationName | 工序名称 | NVARCHAR(200) | 工序显示名称 | 注塑成型 |
| ProcessType | 工序类型 | NVARCHAR(50) | MACHINING/ASSEMBLY/INSPECTION | MACHINING |
| StandardTime | 标准工时 | DECIMAL(18,4) | 单位：分钟 | 150.0 |
| SetupTime | 准备时间 | DECIMAL(18,4) | 换型时间，单位：分钟 | 30.0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| SourceSystem | 来源系统 | NVARCHAR(10) | **v5.0.24 追溯增强字段**（非运行必需）；`'MES'`（当前唯一来源）/ 未来EAM扩展时补充；与 `MES_BOM_View.SourceSystem` 模式对齐 | MES |

**数据流程**：
1. **3号位**：在ODS库创建 `MES_APS_Routing_Operation_View`（输出MES_ID+Model，老结构ETL处理为MES_ID）
2. **2号位**：在APS库创建 `ext_MES_APS_Routing_Operation_View`（跨库包装视图）
3. **2号位**：通过 `IDataLoader` 加载，先通过MaterialMapping映射MES_ID→MaterialId，再写入 `RoutingOperation` 表

---

### 4.4c MES_APS_Routing_Dependency_View（工序依赖视图契约）⭐ **v5.0新增（2026-04-01）**

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：输出工序间的有向依赖关系，支持并行/串行混合工艺  
**负责人**：**3号位**

**⚠️ 并行/串行表达**：
- 并行：工序A→B 和 A→C（B、C 可并行）
- 汇合：B→D 和 C→D（D 等 B+C 都完成）

**v5.0.1变更（2026-04-02）**：同 Operation 视图，MaterialCode → MES_ID + Model

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MES_ID | MES物料主键 | INT | 同 Operation 视图 | 500001 |
| Model | MES物料型号 | NVARCHAR(100) | 同 Operation 视图 | MAT-A-001 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | 同 Operation 视图 | DEFAULT |
| PathId | 路径序号 | INT | 同 Operation 视图 | 1 |
| FromOperationCode | 前驱工序编码 | NVARCHAR(50) | 前驱工序 | OP-010 |
| ToOperationCode | 后继工序编码 | NVARCHAR(50) | 后继工序 | OP-020 |
| DependencyType | 依赖类型 | NVARCHAR(10) | ES（V1）/ SS / FF | ES |
| LagTime | 延迟时间 | DECIMAL(18,4) | 分钟，0=紧跟 | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| SourceSystem | 来源系统 | NVARCHAR(10) | **v5.0.24 追溯增强字段**（非运行必需）；`'MES'`（当前唯一来源）；与 `MES_APS_Routing_Operation_View.SourceSystem` 对齐 | MES |

**数据流程**：
1. **3号位**：在ODS库创建 `MES_APS_Routing_Dependency_View`（输出MES_ID+Model）
2. **2号位**：在APS库创建 `ext_MES_APS_Routing_Dependency_View`
3. **2号位**：通过 `IDataLoader` 加载，MES_ID→MaterialId映射后写入 `RoutingDependency` 表

---

### 4.4d APS_OperationResourceEligibility_View（工序资源能力视图契约）⭐ **v5.0新增（2026-04-01）**

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：定义某物料、某路径、某工序允许使用哪些资源，替代原 ResourceGroup 的排程能力功能  
**负责人**：**3号位**（从 MES 工序-设备能力关系表输出）

**⚠️ 关键语义**：同样两台设备，生产不同产品或走不同路径时，可替代性可能不同。

**v5.0.1变更（2026-04-02）**：同 Operation 视图，MaterialCode → MES_ID + Model

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MES_ID | MES物料主键 | INT | 同 Operation 视图 | 500001 |
| Model | MES物料型号 | NVARCHAR(100) | 同 Operation 视图 | MAT-A-001 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | 同 Operation 视图 | DEFAULT |
| PathId | 路径序号 | INT | 同 Operation 视图 | 1 |
| OperationCode | 工序编码 | NVARCHAR(50) | 对应工序 | OP-010 |
| ResourceCode | 资源编码 | NVARCHAR(50) | APS统一资源业务键 | MC-001 |
| Priority | 优先级 | INT | 1=最优 | 1 |
| CapacityFactor | 产能系数 | DECIMAL(18,4) | 该资源执行该工序的产能系数 | 1.0 |
| IsPrimary | 是否首选资源 | BIT | 1=首选 | 1 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |

**数据流程**：
1. **3号位**：在ODS库创建 `APS_OperationResourceEligibility_View`（输出MES_ID+Model）
2. **2号位**：在APS库创建 `ext_APS_OperationResourceEligibility_View`
3. **2号位**：通过 `IDataLoader` 加载，MES_ID→MaterialId映射后写入 `OperationResourceEligibility` 表

---

### 4.4e MES_APS_Resource_View（资源主数据视图契约）⭐ **v5.0新增（2026-04-01）；v5.0.13 重命名（原名 `APS_Resource_View`）**

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：向APS库暴露设备主数据，避免APS直接依赖MES设备表  
**负责人**：**MES DBA**（源系统侧）

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| ResourceCode | 资源编码 | NVARCHAR(50) | APS统一业务键 | MC-001 |
| ResourceName | 资源名称 | NVARCHAR(200) | 设备名称 | 注塑机001号 |
| ExternalResourceId | 源系统物理主键 | NVARCHAR(50) | MES设备ID或EAM资产ID | 60001 |
| SourceSystem | 来源系统 | NVARCHAR(20) | MES / EAM | MES |
| FactoryCode | 工厂编码 | NVARCHAR(50) | 工厂编码 | F01 |
| **ProductionDeptCode** 🔄 | **生产部门编码** | NVARCHAR(50) | **v5.0.16 RENAME from WorkshopCode**（业务确认 MES 也无"车间"概念）；APS 自维护字典 `ProductionDepartment.DeptCode` 的源系统码；2 号位装载时 JOIN ProductionDepartment 映射成 ProductionDepartmentId | DEPT-MACH-01 |
| ResourceType | 资源类型 | NVARCHAR(50) | MACHINE/LINE/MANUAL_STATION | MACHINE |
| Status | 设备状态 | NVARCHAR(20) | AVAILABLE/MAINTENANCE/DECOMMISSIONED | AVAILABLE |
| CapacityFactor | 产能系数 | DECIMAL(18,4) | 1.0=标准 | 1.2 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| UpdatedAt | 最后更新时间 | DATETIME2 | 设备最后变更时间 | 2026-03-05 15:00:00 |

**数据流程**：
1. **MES DBA**：在ODS库创建 `MES_APS_Resource_View`（v5.0.13 命名统一，原名 `APS_Resource_View`）
2. **2号位**：在APS库创建 `ext_MES_APS_Resource_View`（跨库包装视图，v5.0.13 命名统一）
3. **2号位**：通过 `IDataLoader` 全量刷新到 `Resource` 表（每天 00:10，v5.0.13.1 对齐走查 V3.4：与 sp_SyncMasterData 同窗口并行）

---

### 4.5 ERP_Inventory_View（ERP库存视图契约）⭐ 核心契约（v2.8新增）

**所属库**：ERP数据库（源系统侧）  
**视图类型**：防腐层视图契约  
**业务用途**：ERP库存标准化视图，供APS系统拉取  
**负责人**：ERP DBA

**⚠️ 重要说明**：
- 此视图需要由**ERP DBA**在ERP数据库中创建，指向ERP库存物理表
- 2号位在APS库创建跨库包装视图 `ext_ERP_Inventory_View`
- 2号位通过跨库包装视图拉取到APS库

**⚠️ 架构原则**：
- 视图中的 `MaterialCode` 属于**辅助业务键字段**，用于展示与诊断
- 库存链路中的**权威挂接仍以 MaterialMapping 为准**，通过 MasterID + Warehouse 的当前有效映射进行物理身份桥接
- MaterialCode 的定义权在主数据契约层，库存View不承担"定义统一业务键"的责任

**视图定义示例**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-21
-- 负责人：ERP DBA
CREATE VIEW ERP_Inventory_View AS
SELECT 
    MaterialCode,          -- 物料编码（契约字段）
    MasterID,              -- ERP主键（契约字段）
    WarehouseCode,         -- 仓库编码（契约字段）
    Quantity,              -- 库存数量（契约字段）
    FactoryCode,           -- 工厂编码（契约字段）
    SnapshotTime,          -- 快照时间（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM ERP.dbo.Inventory
WHERE IsDeleted = 0;
```

**字段清单**：

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MaterialCode | 物料编码 | NVARCHAR(50) | 辅助字段，用于展示与诊断 | RAW-STEEL-001 |
| MasterID | ERP主键 | INT | ERP物理主键，用于权威挂接 | 100001 |
| WarehouseCode | 仓库编码 | NVARCHAR(50) | ERP仓库 | WH-01 |
| Quantity | 库存数量 | DECIMAL(18,4) | 当前库存 | 1500 |
| FactoryCode | 工厂编码 | NVARCHAR(50) | 所属工厂 | FACTORY-A |
| SnapshotTime | 快照时间 | DATETIME2 | 数据同步时间 | 2026-03-21 16:00:00 |
| IsActive | 是否有效 | BIT | 是否纳入APS | 1 |

**技术要点**：
- ⚠️ 存储过程必须通过跨库包装视图 `ext_ERP_Inventory_View` 访问此视图
- 支持一物多仓：同一个 `MaterialCode` 可以有多个 `WarehouseCode`
- 契约承诺：无论ERP内部表结构如何变更，此视图的列名和数据类型永不变更

---

### 4.6 MES_Inventory_View（MES库存视图契约）⭐ 核心契约（v2.8新增）

**所属库**：MES_Integration  
**视图类型**：防腐层视图契约  
**业务用途**：向APS库暴露MES系统的库存数据  
**负责人**：MES DBA

**⚠️ 重要说明**：
- 此视图需要由**MES DBA**在ODS库中创建，指向MES数据库的物理表
- 2号位在APS库创建跨库包装视图 `ext_MES_Inventory_View`
- 2号位通过跨库包装视图拉取到APS库

**⚠️ 架构原则**：
- 视图中的 `MaterialCode` 属于**辅助业务键字段**，用于展示与诊断
- 库存链路中的**权威挂接仍以 MaterialMapping 为准**，通过 SourceID（MES_ID）+ Warehouse（Location）的当前有效映射进行物理身份桥接（2026-04-01 v4.0更新）
- MaterialCode 的定义权在主数据契约层，库存View不承担"定义统一业务键"的责任

**视图定义示例**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-21
-- 负责人：MES DBA
CREATE VIEW MES_Inventory_View AS
SELECT 
    MaterialCode,          -- 物料编码（契约字段）
    MES_ID,                -- MES物料主键（契约字段）
    LocationCode,          -- 库位编码（契约字段）
    WarehouseCode,         -- 仓库编码（契约字段）
    Quantity,              -- 库存数量（契约字段）
    FactoryCode,           -- 工厂编码（契约字段）
    SnapshotTime,          -- 快照时间（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM MES.dbo.Inventory
WHERE IsActive = 1;
```

**字段清单**：

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| MaterialCode | 物料编码 | NVARCHAR(50) | 辅助字段，用于展示与诊断 | MES-CUSTOM-001 |
| MES_ID | MES主键 | INT | MES物理主键，用于权威挂接 | 5001 |
| LocationCode | 库位编码 | NVARCHAR(50) | MES库位 | LOC-A-01 |
| WarehouseCode | 仓库编码 | NVARCHAR(50) | MES仓库（v1.0新增） | WH-MES-A |
| Quantity | 库存数量 | DECIMAL(18,4) | 当前库存 | 500 |
| FactoryCode | 工厂编码 | NVARCHAR(50) | 所属工厂 | FACTORY-A |
| SnapshotTime | 快照时间 | DATETIME2 | 数据同步时间 | 2026-03-21 16:05:00 |
| IsActive | 是否有效 | BIT | 是否纳入APS | 1 |

**技术要点**：
- ⚠️ 存储过程必须通过跨库包装视图 `ext_MES_Inventory_View` 访问此视图
- **MES库存同时包含 `LocationCode`（库位）和 `WarehouseCode`（仓库）**
- 支持一物多仓：同一个 `MaterialCode` 可以有多个 `LocationCode`
- 契约承诺：无论MES内部表结构如何变更，此视图的列名和数据类型永不变更

**数据流程**：
1. **MES DBA**：在ODS库创建 `MES_Inventory_View`（指向MES库存物理表）
2. **2号位**：在APS库创建 `ext_MES_Inventory_View`（跨库包装视图）
3. **2号位**：通过 `ext_MES_Inventory_View` 拉取到APS库的 `InventoryFact_MES` 表

---

### 4.7 MaterialProductFamilyScopeRule（产品族解析范围规则表）🔒 **V2预留，V1不建**

> **V1不建此表**：V1产品族判断逻辑由5号位封装在 `ERP_Master_View` 内部（ODS层，不暴露给APS）；V2若规则增多或需业务可配置，再建此表。

**所属库**：MES_Integration（ODS 库）  
**业务用途**：（V2）定义哪些 MaterialCode 模式的物料需要做产品族解析；未命中则标记 FamilyResolveStatus='NOT_REQUIRED'  
**维护方**：5号位配置 + 0号位审批  
**判定逻辑**：MaterialCode LIKE IncludePattern 且不命中任何 ExcludePattern → 需要解析

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| Id | 规则ID | INT | 主键，自增 | 1 |
| RuleName | 规则描述 | NVARCHAR(100) | 人工维护用描述 | 缸类物料 |
| IncludePattern | 包含模式 | NVARCHAR(200) | MaterialCode LIKE 模式（如 'CYL%'） | CYL% |
| ExcludePattern | 排除模式 | NVARCHAR(200) | NULL=无排除；排除时用 NOT LIKE 过滤 | %TEST% |
| Priority | 优先级 | INT | 数值越小越高；用于冲突时取高优先级 | 100 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| UpdatedBy | 操作人 | NVARCHAR(100) | 最后修改人（审计用） | admin |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-05-30 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-05-30 |

---

### 4.8 MaterialProductFamilyRule（产品族匹配规则表）🔒 **V2预留，V1不建**

> **V1不建此表**：V1产品族判断逻辑（ModelSort LIKE 规则）内嵌在 `ERP_Master_View` 中；V2若规则需要业务可配置，再抽象为本表。

**所属库**：MES_Integration（ODS 库）  
**业务用途**：（V2）从物料特征字段（Spec 或 MaterialCode）截位比较，输出 ProductFamilyCode  
**维护方**：5号位 + 0号位审批  
**匹配逻辑**：SUBSTRING(来源字段, PositionStart, PositionLength) = MatchValue → 命中，多条命中取 Priority 最小

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| Id | 规则ID | INT | 主键，自增 | 1 |
| RuleName | 规则描述 | NVARCHAR(100) | 人工维护用描述 | Spec第4位为I→缸类 |
| MatchSource | 匹配来源字段 | NVARCHAR(50) | 'SPEC' 或 'MATERIALCODE'；CHECK 约束 | SPEC |
| PositionStart | 截取起始位 | INT | 1-based | 4 |
| PositionLength | 截取长度 | INT | 截取字符数 | 1 |
| MatchValue | 命中比较值 | NVARCHAR(100) | SUBSTRING 结果等于此值时命中 | I |
| ProductFamilyCode | 输出产品族编码 | NVARCHAR(50) | 命中时输出，对应 ProductFamily.Code | CYL |
| Priority | 规则优先级 | INT | 数值越小越优先；多条命中取最小值 | 10 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| UpdatedBy | 操作人 | NVARCHAR(100) | 最后修改人（审计用） | admin |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-05-30 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-05-30 |

---

### 4.9 MaterialProductFamilyResolved（产品族解析结果表）🔒 **V2预留，V1不建**

> **V1不建此表**：V1不设独立解析结果表；`ERP_Master_View` 直接内联计算 ProductFamilyCode / FamilyResolveStatus 输出；`sp_ResolveMaterialProductFamily` V2预留。

**所属库**：MES_Integration（ODS 库）  
**业务用途**：（V2）存储 sp_ResolveMaterialProductFamily 的全量解析结果；每次全量 TRUNCATE+INSERT  
**刷新时机**：（V2）每天 00:05（先于 sp_SyncMasterData）  
**消费方**：（V2）ERP_Master_View / MES_Material_View 通过 LEFT JOIN 获取  
**⚠️ 设计红线**：`APS_Production` 库禁止直接读取此表；只能通过 `ext_ERP_Master_View` / `ext_MES_Material_View` 获取

**`FamilyResolveStatus` 值域**：

| 状态值 | 含义 |
|--------|------|
| RESOLVED | 成功解析出 ProductFamilyCode |
| NOT_REQUIRED | 不在 ScopeRule 范围内，无需解析 |
| NO_RULE | 在判断范围内但无匹配规则（ModelSort不命中任何规则） |
| AMBIGUOUS | 多条规则命中且无法唯一确定（V2扩展；V1不赋此值） |
| FAMILY_CODE_NOT_FOUND | 命中规则但 ProductFamilyCode 在 ProductFamily 码表中不存在 |
| SOURCE_FIELD_MISSING | 来源字段（如 ModelSort）为空，无法判断（原 NO_SPEC） |

| 字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|--------|---------|---------|---------|--------|
| Id | 解析结果ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 业务主键（UNIQUE 约束） | CYL-10001 |
| Spec | Spec快照 | NVARCHAR(100) | 解析时使用的 Spec 值 | C25ILB-005 |
| ProductFamilyCode | 产品族编码 | NVARCHAR(50) | RESOLVED 时有值，其余 NULL | CYL |
| FamilyResolveStatus | 解析状态 | NVARCHAR(30) | 见上文值域说明 | RESOLVED |
| MatchedRuleId | 命中规则ID | INT | 软引用 MaterialProductFamilyRule.Id；NULL=未命中 | 3 |
| ResolvedAt | 解析时间 | DATETIME2 | 本次解析执行时间 | 2026-05-30 00:05:00 |
| BatchNo | 解析批次号 | NVARCHAR(50) | 用于追溯（如 'DAILY'） | DAILY |

---

## 七、MES生产进度汇总视图（v5.0.41 新增，5号位收口）

> ⚠️ **定位说明**：以下三条视图是 ODS 层（`MES_Integration` 库）的**契约视图**（Socket），由5号位负责建立和维护。APS侧（2号位）只通过这三个统一视图读取 MES 进度数据，不直接访问 MES 各大工艺物理表。
>
> **视图命名规范**：统一契约视图由各大工艺标准化子视图（`MES_APS_{Topic}_{大工艺}_View`）UNION ALL 汇总而来；子视图由2号位（加工类）或5号位（组装类）分工建立，统一收口由5号位负责。
>
> **V1 约束**：只暴露 ODS 汇总后的工单级 / 工序级 / 大工艺级进度，**不暴露每条报工明细**；视图字段列表变更必须走审批，禁止 APS 侧改变字段语义。

---

### 7.1 MES_APS_WorkOrder_View（MES工单汇总契约视图）

**所属库**：`MES_Integration`（ODS库）  
**维护责任**：5号位（收口）  
**用途**：记录"生产指示号→MES工单号"的工单追溯关系；APS 侧 `sp_SyncMESWorkOrderSnapshot` 从此视图同步到 `MESWorkOrderSnapshot`

| 字段名 | 中文名 | 数据类型 | 说明 | 示例 |
|--------|--------|----------|------|------|
| ProductionInstructionNo | 生产指示号 | NVARCHAR(100) | APS Order 中的生产指示号（主关联键） | PI-2026-00123 |
| MESWorkOrderNo | MES工单号 | NVARCHAR(100) | MES 系统中的工单号 | WO-MES-9988 |
| MaterialCode | 物料编码 | NVARCHAR(100) | MES 工单生产的物料编码 | MAT-A001 |
| PlannedQty | 计划数量 | DECIMAL(18,4) | MES 工单计划生产数量 | 100.0000 |
| WorkOrderStatus | 工单状态 | NVARCHAR(50) | MES 工单当前状态（如 RELEASED / IN_PROGRESS / CLOSED） | IN_PROGRESS |
| SourceUpdatedAt | 源更新时间 | DATETIME2 NULL | MES 中该工单最后更新时间 | 2026-06-12 01:30:00 |

---

### 7.2 MES_APS_OperationProgress_View（工序进度汇总契约视图）

**所属库**：`MES_Integration`（ODS库）  
**维护责任**：5号位（收口，各大工艺子视图 UNION ALL）  
**用途**：提供工序级生产进度汇总；APS 侧 `sp_SyncOperationProgressSnapshot` 从此视图同步到 `OperationProgressSnapshot`  
**汇总颗粒度**：生产指示号 + MES工单号 + 物料编码 + 工序名称 + 大工艺阶段码

| 字段名 | 中文名 | 数据类型 | 说明 | 示例 |
|--------|--------|----------|------|------|
| ProductionInstructionNo | 生产指示号 | NVARCHAR(100) | APS Order 中的生产指示号 | PI-2026-00123 |
| MESWorkOrderNo | MES工单号 | NVARCHAR(100) | 所属 MES 工单号 | WO-MES-9988 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 工序对应物料编码 | MAT-A001 |
| OperationName | 工序名称 | NVARCHAR(200) | **V1 工序识别主字段**（MES工序名称）；APS通过此字段+StageCode联合识别工序；**不以 MES 工序编码为主** | 车削 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | APS 使用的大工艺阶段编码；格式 `{工厂}_{类别}`，取自 StageDict | TJ_MACH |
| StageName | 大工艺名称 | NVARCHAR(100) NULL | 大工艺中文名称（冗余便于展示） | 机加工 |
| PlannedQty | 计划数量 | DECIMAL(18,4) | 该工序在该工单内的计划数量 | 100.0000 |
| GoodQty | 良品完成数量 | DECIMAL(18,4) | 截至数据截止时间，该工序累计良品完成数量 | 60.0000 |
| ScrapQty | 报废数量 | DECIMAL(18,4) NULL | 累计报废数量（可选字段；来源不提供时视图返回 NULL） | 2.0000 |
| ReworkQty | 返工数量 | DECIMAL(18,4) NULL | 累计返工数量（可选字段；来源不提供时视图返回 NULL） | NULL |
| LastReportTime | 最后报工时间 | DATETIME2 NULL | 该工序最后一次报工时间 | 2026-06-12 01:15:00 |
| SourceUpdatedAt | 源更新时间 | DATETIME2 NULL | MES 汇总数据最后更新时间 | 2026-06-12 01:20:00 |

> ⚠️ `ScrapQty` / `ReworkQty` 为可选字段——各大工艺子视图能提供则填写，无法提供则返回 NULL。APS 快照同步直接透传，**NULL 不阻断同步**。

---

### 7.3 MES_APS_StageProgress_View（大工艺进度汇总契约视图）

**所属库**：`MES_Integration`（ODS库）  
**维护责任**：5号位（收口，各大工艺子视图 UNION ALL）  
**用途**：提供大工艺阶段级进度汇总（工序进度的上层聚合）；APS 侧 `sp_SyncStageProgressSnapshot` 从此视图同步到 `StageProgressSnapshot`  
**汇总颗粒度**：生产指示号 + 物料编码 + 大工艺阶段码

| 字段名 | 中文名 | 数据类型 | 说明 | 示例 |
|--------|--------|----------|------|------|
| ProductionInstructionNo | 生产指示号 | NVARCHAR(100) | APS Order 中的生产指示号 | PI-2026-00123 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 大工艺阶段对应物料编码 | MAT-A001 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | APS 使用的大工艺阶段编码；格式 `{工厂}_{类别}`，取自 StageDict | TJ_MACH |
| StageName | 大工艺名称 | NVARCHAR(100) NULL | 大工艺中文名称（冗余便于展示） | 机加工 |
| PlannedQty | 计划数量 | DECIMAL(18,4) | 该大工艺阶段计划数量 | 100.0000 |
| GoodCompletedQty | 良品完成数量 | DECIMAL(18,4) | 截至数据截止时间，该阶段累计良品完成数量；原则上应与该阶段下各工序 `GoodQty` 之和保持同口径一致；如因 MES 来源表或报工完整性导致不一致，以本字段为准，差异登记 APS_ETL_Log | 60.0000 |
| ScrapQty | 报废数量 | DECIMAL(18,4) NULL | 阶段累计报废数量（可选字段） | 2.0000 |
| ReworkQty | 返工数量 | DECIMAL(18,4) NULL | 阶段累计返工数量（可选字段） | NULL |
| LastReportTime | 最后报工时间 | DATETIME2 NULL | 该阶段最后一次报工时间 | 2026-06-12 01:15:00 |
| SourceUpdatedAt | 源更新时间 | DATETIME2 NULL | MES 汇总数据最后更新时间 | 2026-06-12 01:20:00 |

> **一致性说明**：原则上，同一 `ProductionInstructionNo + MaterialCode + StageCode` 下，`StageProgressSnapshot.GoodCompletedQty` 应与 `OperationProgressSnapshot.GoodQty` 的阶段汇总保持同口径一致。如因 MES 来源表、报工完整性或阶段关闭状态导致不一致，V1 以 `StageProgressSnapshot` 作为大工艺级扣减优先输入，差异登记 `APS_ETL_Log` 或数据质量巡检结果，不阻断排程。

---
### 1.9 ERP_InterplantInTransit_View（ERP厂间在途ODS契约视图 - v5.0.42 新增 2026-06-15）

**所属层**：ODS 层  
**所属库**：MES_Integration  
**来源系统**：ERP  
**维护责任人**：5号位  
**业务用途**：ODS 层 ERP 厂间物流运输在途数据契约视图；当前为管道供给链的唯一 ODS 来源视图  
**V1 状态**：WHERE 1=0 返回 0 行的空契约骨架  
**V1.1/V2 计划**：5号位替换内部 SELECT 和 ERP 源表 JOIN，不改变对外契约  
**字段语义红线**：
- `FactoryCode` = 目的工厂 / 收货工厂 / 可使用该供给的工厂
- `SourceFactoryCode` = 发出工厂
- `Quantity` = 当前仍在途、尚未收货的剩余数量（不得直接使用原始发货数量）
- `ETA` = ERP 原始事实（ODS 不得加入 APS 提前期偏移）
- `AvailableTime` 不在本视图提供，由 APS `sp_SyncPipelineSupply` 计算

**⚠️ 契约锁定规则**：
ODS 契约视图字段结构为强契约。V1.1/V2 启用真实数据时，仅允许替换视图内部的 FROM 和 JOIN 逻辑，**禁止修改字段顺序、字段类型、字段名称**。

| 字段名 | 中文含义 | 数据类型 | 是否必须 | 业务说明 |
|--------|---------|---------|---------|---------|
| MasterID | ERP物料主键 | INT | 实际数据必须 | ERP物料主数据物理ID，用于通过 `MaterialMapping.SourceID` 映射 APS 物料 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 建议必须 | ERP物料编码，业务追溯字段；不能替代 MasterID 的权威映射作用 |
| SourceFactoryCode | 发出工厂编码 | NVARCHAR(50) | 可空 | 物料从哪个工厂发出，仅用于物流追溯 |
| FactoryCode | 目的工厂编码 | NVARCHAR(50) | 实际数据必须 | 收货工厂、供给可使用工厂；后续映射为 `SupplyFact_Pipeline.FactoryId` |
| SupplyType | 供给类型 | NVARCHAR(50) | 必须 | 本视图固定为 `INTERPLANT_IN_TRANSIT` |
| OwnershipType | 所有权类型 | NVARCHAR(20) | 必须 | 厂间在途默认 `OWNED` |
| QualityStatus | 质量状态 | NVARCHAR(20) | 必须 | 默认 `AVAILABLE` |
| Quantity | 在途剩余数量 | DECIMAL(18,4) | 必须 | 必须是尚未收货、尚未关闭的剩余在途数量，不是原始发货总数量 |
| ETA | 预计到达时间 | DATETIME2 | 可空 | ERP原始预计到达时间；ODS不得加入APS提前期偏移 |
| StorageCode | 目的仓库编码 | NVARCHAR(50) | 可空 | 预计收货仓库、目的库存点或接收库位编码 |
| SupplierCode | 供应方编码 | NVARCHAR(50) | 可空 | 厂间在途可为空；为未来采购在途/VMI 同构扩展预留 |
| SourceDocumentNo | ERP来源单据号 | NVARCHAR(100) | 实际数据建议必须 | 调拨单、出库单、运输单等ERP源单据号 |
| SourceDocumentLineNo | ERP来源单据行号 | NVARCHAR(50) | 可空 | 与来源单据号共同定位ERP明细行 |
| SourceUpdatedAt | ERP来源更新时间 | DATETIME2 | 可空 | 用于未来增量同步、数据新鲜度检查和问题追溯 |

**视图行范围**（未来接入真实数据后）：
```sql
-- 仅输出：尚未全部收货 / 尚未关闭 / 尚未取消 / 剩余数量>0 / 能够明确目的工厂 / 属于有效厂间调拨的数据
WHERE 收货状态 NOT IN ('全部收货', '已关闭', '已取消') AND 剩余数量 > 0
```
已收货、已关闭、已取消、剩余数量为0的数据不得继续作为管道供给输出。

---


---

### 1.9c ERP_Received_ByDocument_View（ERP Received按单据汇总ODS视图 - v5.0.46 新增 2026-06-23）

**所属层**：ODS 层
**所属库**：MES_Integration
**来源系统**：ERP
**维护责任人**：5号位
**粒度**：工厂+仓库+物料+单据类型+单据号。不保留单据行号。

| 字段名 | 中文含义 | 数据类型 | 业务说明 |
|--------|---------|---------|---------|
| FactoryCode | 工厂编码 | NVARCHAR(50) | 入库发生工厂 |
| WarehouseCode | 仓库编码 | NVARCHAR(50) | ZP/BP等出口库 |
| MasterID | ERP物料主键 | INT | 映射APS物料 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 展示和追溯 |
| DocumentType | 单据类型 | NVARCHAR(50) | SHIPPING_INSTRUCTION / PRODUCTION_INSTRUCTION / UNKNOWN |
| DocumentNo | 单据号 | NVARCHAR(100) | ERP Received 原始单据号 |
| ReceivedQty | 入库汇总数量 | DECIMAL(18,4) | 按上述维度汇总 |
| LastReceivedAt | 最近入库时间 | DATETIME2 | MAX |
| SourceUpdatedAt | 来源更新时间 | DATETIME2 | 数据新鲜度 |
| IsActive | 是否有效 | BIT | 排除作废/冲销 |

**业务假设**：该出荷指示号处于未完成状态时，ReceivedQty 默认视为尚未被使用的可供给数量。

# 第二部分：APS库（APS_Production）

## 📌 APS库概述

**库名**：`APS_Production`  
**用途**：APS排程系统的核心业务库，包含：
1. 主数据表（物料、BOM、工艺路线、资源等）
2. 计划版本与订单表
3. 排程结果表（Task、Pegging）
4. 配置表（冻结区、拆批规则等）
5. 跨库包装视图（访问ODS库）

---

## 一、跨库包装视图（访问ODS库）

### 1.1 ext_ERP_Master_View（ERP主数据包装视图）

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `ERP_Master_View`，避免直接跨库访问

**视图定义**：
```sql
CREATE VIEW ext_ERP_Master_View AS 
SELECT * FROM [MES_Integration].[dbo].[ERP_Master_View];
```

**技术要点**：
- ⚠️ **P0-6修复**：所有APS库的存储过程必须使用此视图，不能直接访问ODS库
- 权限管理：APS库的执行账号需要对ODS库有SELECT权限

---

### 1.2 ext_MES_Material_View（MES物料包装视图）（2026-04-01 v1.3同构化更新）

**所属库**：APS_Production  
**视图类型**：跨库包装视图（双源同构契约）  
**业务用途**：APS库通过此视图访问ODS库的 `MES_Material_View`，字段与 `ext_ERP_Master_View` 完全一致

**视图定义**（v5.0.37 升级：SELECT * → 显式列列表）：
```sql
-- v1.3同构化：MES_ID→MasterID别名，Location→Warehouse别名
-- v5.0.37升级：SELECT * → 显式列列表，新增 ProductFamilyCode/FamilyResolveStatus 同构透传
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
    ProductFamilyCode,                     -- 🆕 v5.0.37：ODS 解析产出的产品族编码（同构透传）
    FamilyResolveStatus                    -- 🆕 v5.0.37：ODS 解析状态（同构透传）
FROM [MES_Integration].[dbo].[MES_Material_View];
```

**技术要点**：
- ⚠️ 存储过程 `sp_SyncMasterData(@SourceType='MES')`（v4.0双源统一，2026-04-01 更新）使用此视图
- 同构契约：字段与 `ext_ERP_Master_View` 完全一致，SP逻辑零分叉
- v5.0.37：显式列列表确保 `MES_ID→MasterID`/`Location→Warehouse` 别名正确，同时透传 `ProductFamilyCode`/`FamilyResolveStatus`
- 跨库访问方式：同实例跨库（推荐）或 Linked Server

---

### 1.3 ext_ERP_Inventory_View（ERP库存包装视图）

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `ERP_Inventory_View`

**视图定义**：
```sql
CREATE VIEW ext_ERP_Inventory_View AS 
SELECT * FROM [MES_Integration].[dbo].[ERP_Inventory_View];
```

**技术要点**：
- ⚠️ 2号位负责在APS库创建此跨库包装视图
- 用于拉取ERP库存数据到APS库的 `InventoryFact_ERP` 表

---

### 1.4 ext_MES_Inventory_View（MES库存包装视图）

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `MES_Inventory_View`

**视图定义**：
```sql
CREATE VIEW ext_MES_Inventory_View AS 
SELECT * FROM [MES_Integration].[dbo].[MES_Inventory_View];
```

**技术要点**：
- ⚠️ 2号位负责在APS库创建此跨库包装视图
- 用于拉取MES库存数据到APS库的 `InventoryFact_MES` 表

---

### 1.5 ext_MES_APS_Resource_View（资源主数据包装视图）⭐ **v5.0新增（2026-04-03审计补充）；v5.0.13 重命名（原名 `ext_APS_Resource_View`）**

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `MES_APS_Resource_View`，获取设备主数据。执行体：`sp_SyncResourceData(@SourceType='MES')`（DDL v5.0.13 新增）。未来 EAM 上线时并行新增 `ext_EAM_APS_Resource_View`（同构契约）。

**视图定义**：
```sql
CREATE VIEW ext_MES_APS_Resource_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Resource_View];
```

**字段契约**（v5.0.16 升级）：与 ODS 层 `MES_APS_Resource_View`（4.4e 节）完全一致：
`ResourceCode`, `ResourceName`, `ExternalResourceId`, `SourceSystem`, `FactoryCode`, **`ProductionDeptCode`** 🔄 (v5.0.16 RENAME from WorkshopCode), `ResourceType`, `Status`, `CapacityFactor`, `IsActive`, `UpdatedAt`

**技术要点**：
- 2号位负责在APS库创建此跨库包装视图
- 全量刷新到 `Resource` 表（每天 00:10，v5.0.13.1 对齐走查 V3.4：与 sp_SyncMasterData 同窗口并行）
- 通过 `IDataLoader.LoadResourcesAsync()` 加载

---

### 1.6 ext_MES_APS_Routing_Operation_View（工序节点包装视图）⭐ **v5.0新增（2026-04-03审计补充）**

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `MES_APS_Routing_Operation_View`，获取工序节点数据

**视图定义**：
```sql
-- v5.0.1变更（2026-04-02）：ODS视图输出MES_ID+Model（非MaterialCode），
-- 2号位装载时通过MaterialMapping映射为MaterialId
CREATE VIEW ext_MES_APS_Routing_Operation_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Operation_View];
```

**字段契约**：与ODS层 `MES_APS_Routing_Operation_View`（4.4b节）完全一致：
`MES_ID`, `Model`, `RouteCode`, `PathId`, `OperationCode`, `OperationName`, `ProcessType`, `StandardTime`, `SetupTime`, `IsActive`

**技术要点**：
- 2号位负责在APS库创建此跨库包装视图
- 增量Upsert到 `RoutingOperation` 表（每天 00:30）
- ⚠️ 装载时需先通过 `MaterialMapping(Source='MES', SourceID=MES_ID)` 映射得到 `MaterialId`

---

### 1.7 ext_MES_APS_Routing_Dependency_View（工序依赖包装视图）⭐ **v5.0新增（2026-04-03审计补充）**

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `MES_APS_Routing_Dependency_View`，获取工序依赖关系

**视图定义**：
```sql
-- v5.0.1变更（2026-04-02）：同上，MES_ID+Model
CREATE VIEW ext_MES_APS_Routing_Dependency_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Dependency_View];
```

**字段契约**：与ODS层 `MES_APS_Routing_Dependency_View`（4.4c节）完全一致：
`MES_ID`, `Model`, `RouteCode`, `PathId`, `FromOperationCode`, `ToOperationCode`, `DependencyType`, `LagTime`, `IsActive`

**技术要点**：
- 2号位负责在APS库创建此跨库包装视图
- 增量Upsert到 `RoutingDependency` 表（每天 00:30，与 RoutingOperation 同批次）
- ⚠️ 装载时需先通过 `MaterialMapping` 映射 MES_ID→MaterialId

---

### 1.8 ext_APS_OperationResourceEligibility_View（工序资源能力包装视图）⭐ **v5.0新增（2026-04-03审计补充）**

**所属库**：APS_Production  
**视图类型**：跨库包装视图  
**业务用途**：APS库通过此视图访问ODS库的 `APS_OperationResourceEligibility_View`，获取工序-资源能力映射

**视图定义**：
```sql
-- v5.0.1变更（2026-04-02）：同上，MES_ID+Model
CREATE VIEW ext_APS_OperationResourceEligibility_View AS
SELECT * FROM [MES_Integration].[dbo].[APS_OperationResourceEligibility_View];
```

**字段契约**：与ODS层 `APS_OperationResourceEligibility_View`（4.4d节）完全一致：
`MES_ID`, `Model`, `RouteCode`, `PathId`, `OperationCode`, `ResourceCode`, `Priority`, `CapacityFactor`, `IsPrimary`, `IsActive`

**技术要点**：
- 2号位负责在APS库创建此跨库包装视图
- 增量Upsert到 `OperationResourceEligibility` 表（每天 00:35）
- ⚠️ 装载时需先通过 `MaterialMapping` 映射 MES_ID→MaterialId

---

### 1.9a ext_ERP_InterplantInTransit_View（ERP厂间在途APS包装视图 - v5.0.42 新增 2026-06-15）

**所属层**：APS 层  
**所属库**：APS_Production  
**来源**：MES_Integration.dbo.ERP_InterplantInTransit_View  
**维护责任人**：2号位  
**业务用途**：APS跨库包装视图，为 `sp_SyncPipelineSupply` 提供稳定的 APS 侧读取入口  

**视图定义**（⚠️ **禁止 SELECT ***，必须显式列字段）：
```sql
CREATE VIEW ext_ERP_InterplantInTransit_View AS
SELECT
    MasterID, MaterialCode, SourceFactoryCode, FactoryCode,
    SupplyType, OwnershipType, QualityStatus, Quantity, ETA,
    StorageCode, SupplierCode, SourceDocumentNo,
    SourceDocumentLineNo, SourceUpdatedAt
FROM [MES_Integration].[dbo].[ERP_InterplantInTransit_View];
```

**技术要点**：
- 字段契约与 ODS 契约视图完全同构（14字段）
- ⚠️ **契约锁定**：V1.1/V2 仅允许替换视图的 FROM 逻辑，禁止修改字段顺序、类型、名称
- ⚠️ V1 空链路预留：源视图返回 0 行，本视图同步返回 0 行
- 字段契约升级必须通过显式改列，禁止 `SELECT *` 静默传播字段变化

---


---

### 1.9d ext_ERP_Received_ByDocument_View（ERP Received APS包装视图 - v5.0.46 新增 2026-06-23）

**所属层**：APS 层
**所属库**：APS_Production
**来源**：MES_Integration.dbo.ERP_Received_ByDocument_View
**维护责任人**：2号位
**说明**：V1 不建 APS 本地 Received 快照表。排程装载时通过本视图读取。

```sql
CREATE OR ALTER VIEW dbo.ext_ERP_Received_ByDocument_View
AS
SELECT FactoryCode, WarehouseCode, MasterID, MaterialCode,
       DocumentType, DocumentNo, ReceivedQty,
       LastReceivedAt, SourceUpdatedAt, IsActive
FROM [MES_Integration].[dbo].[ERP_Received_ByDocument_View];
GO
```

### 1.9b ext_PipelineSupply_Source_View（多来源管道供给统一输入视图 - v5.0.43 2026-06-18）

**所属层**：APS 层  
**所属库**：APS_Production  
**维护责任人**：2号位  
**输出列数**：15列（前14列与ODS契约同构 + 第15列 `SourceSystem` 为APS派生标识）  
**定位**：将 ODS 层所有管道供给来源的 APS 单来源包装视图通过 UNION ALL 收敛为统一输入。**V1**：`sp_SyncPipelineSupply` 不读取本视图或任何单来源视图，仅执行 TRUNCATE+SUCCESS 日志。**V1.1/V2 启用后**：`sp_SyncPipelineSupply` 只读取本视图，不直接读任何单来源包装视图。  
**V1 状态**：所有分支均返回0行。分支1已连接 `ext_ERP_InterplantInTransit_View`（其下层 ODS 契约视图 WHERE 1=0）；分支2~4为本视图内的类型化 placeholder（WHERE 1=0）。**sp_SyncPipelineSupply V1 不读取本视图。**  
**V1.1/V2 扩展**：新增管道供给来源时，在 ODS 层新建对应契约视图 + APS 层新建单来源包装视图，然后在**本视图**追加 UNION ALL 分支；`sp_SyncPipelineSupply` 主流程零改动。

**分支枚举**：
| 分支 | SourceSystem | SupplyType | V1 来源 |
|------|-------------|------------|---------|
| 1 厂间在途 | ERP | INTERPLANT_IN_TRANSIT | ext_ERP_InterplantInTransit_View |
| 2 采购在途 | PROCUREMENT | PURCHASE_IN_TRANSIT | placeholder (WHERE 1=0) |
| 3 VMI | VMI | VMI_ONSITE | placeholder (WHERE 1=0) |
| 4 已到厂未入库 | ERP | ARRIVED_NOT_RECEIVED | placeholder (WHERE 1=0) |

---

## 二、ETL相关表

### 2.1 APS_BOM_RAW（BOM原始数据表）

**所属库**：APS_Production  
**业务用途**：从ODS库拉取的BOM展开结果的本地缓存，用于计算低阶码（LLC）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联ODS批次 | 20260305_000000 |
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | 成品或半成品编码 | MAT-10001 |
| ChildMaterialCode | 子物料编码 | NVARCHAR(50) | 半成品或原材料编码 | MAT-20001 |
| Quantity | 用量 | DECIMAL(18,6) | 单位用量 | 2.5 |
| Level | BOM层级 | INT | BOM层级 | 1 |
| LLC | 低阶码 | INT | ⚠️ 计算后的低阶码（0=顶层） | 2 |
| ChildRequiredStageCode | 子件供给所需阶段码 | NVARCHAR(50) | v5.0.7 从Workset原样透传；NULL=保守策略（全工艺完成后才可供给） | TJ_OUTS |
| **ChildRequiredFactory** | **子件应归属账面工厂** | **NVARCHAR(20)** | **v5.0.10 从Workset原样透传；APS 自定义枚举 CN/CN6课/BJ/TJ/SH/NULL；外购件=NULL** | **SH** |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 00:16:00 |

**技术要点**：
- 数据来源：从 `MES_APS_BOM_Workset` 拉取（v5.0.7：APS仅拉取ChildRequiredStageCode；v5.0.10：同步拉取 `ChildRequiredFactory`）
- LLC计算：通过存储过程 `sp_CalculateLLC` 计算
- v5.0.7：2号位 BOMDataLoader 的 SqlBulkCopy.ColumnMappings 需同步增加1项
- v5.0.10：ColumnMappings 再加 1 项 `ChildRequiredFactory`
- 数据生命周期：每天更新一次

---

### 2.1b APS_BOM_STAGE_PATH_RAW（统一阶段路径本地缓存）⭐ **v5.0.7新增，v5.0.8升级（2026-04-15 支持ROOT）**

**所属库**：APS_Production  
**业务用途**：从ODS库 MES_APS_BOM_Workset_StageDetail 拉取的本地缓存，供1号位排程消费。含EDGE（子件供给路径）和ROOT（根产品完工路径）两类记录。

**技术要点（数据流向）**：
- **数据来源**：2号位负责搬运（与 APS_BOM_RAW 同批次拉取，含StageScopeType列）
- **消费方**：1号位（**必须按StageScopeType区分查询**；读取StageSeq+StageCode→对每个阶段串接RoutingOperation小工序排Task；对无小工序的外协阶段查StageLeadTimeParam生成标准Task）
- **生命周期**：与 APS_BOM_RAW 完全一致（同批次拉取、同批次刷新）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联ODS批次 | 20260305_000000 |
| **WorksetId** | **ODS追溯锚点** | **BIGINT** | **v5.0.26** 对应ODS侧Workset.Id（跨库引用，非FK）；2号位搬运时透传；NULL=兼容旧批次 | **12345** |
| BOMNO | BOM编号 | NVARCHAR(50) | MES系统的BOM编号 | BOM-10001 |
| StageScopeType | 阶段路径类型 | NVARCHAR(10) | v5.0.8 EDGE=子件供给路径 / ROOT=根产品完工路径 | EDGE |
| ParentMaterialCode | 父物料编码 | NVARCHAR(50) | EDGE=父件编码；ROOT=NULL（v5.0.8可空） | MAT-10001 |
| ChildMaterialCode | 子/根物料编码 | NVARCHAR(50) | EDGE=子件编码；ROOT=根产品自身编码 | MAT-20001 |
| StageSeq | 阶段顺序号 | INT | 10/20/30，间隔10 | 10 |
| StageCode | 大工艺阶段码 | NVARCHAR(50) | 如TJ_MACH/TJ_OUTS/BJ_SURF | TJ_MACH |
| IsSupplyThreshold | 是否供给阈值点 | BIT | **仅EDGE有效**；ROOT恒为0 | 0 |
| SyncedAt | 同步时间 | DATETIME2 | 2号位拉取时间 | 2026-03-05 00:16:00 |

**索引**：聚集索引 `(BatchNo, ChildMaterialCode)`，非聚集索引 `(ChildMaterialCode, StageSeq)`；v5.0.26：2号位搬运时透传 `WorksetId`（跨库引用，不建索引；查询时经 ODS 反查 Workset 即可）

---

### 2.2 APS_ETL_Log（ETL日志表）

**所属库**：APS_Production  
**业务用途**：记录ETL过程的关键日志（BOM同步、物料映射同步、库存加载等）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 日志ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联批次 | 20260305_000000 |
| Step | ETL步骤 | NVARCHAR(100) | CalculateLLC、SyncMapping、LoadInventory等 | CalculateLLC |
| Status | 状态 | NVARCHAR(20) | SUCCESS、FAILED | SUCCESS |
| RowsAffected | 影响行数 | INT | 处理的行数 | 3500000 |
| DurationSeconds | 耗时（秒） | INT | 步骤耗时 | 120 |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) | 失败时的错误描述 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 日志时间 | 2026-03-05 00:16:00 |

**技术要点**：
- 日志级别：INFO、WARNING、ERROR
- 保留周期：建议保留30天

---

### 2.3 ERP_Order_Staging（ERP订单同步表）（2026-05-16 v5.0.27 MaterialCode可空 + 补5新字段）

**所属库**：APS_Production  
**业务用途**：ERP订单同步的临时表（Staging），用于数据验证和清洗

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| SourceOrderId | 源系统订单ID | NVARCHAR(100) | ERP中的订单主键 | 123456 |
| SourceSystem | 来源系统 | NVARCHAR(50) | 固定'ERP'（2026-04-03审计补充，与Order_Canonical对齐） | ERP |
| SourceMasterID | ERP物理主键 | INT | ERP物料主数据ID（2026-04-03审计补充） | 100001 |
| OrderNo | 订单号 | NVARCHAR(50) | 订单编号 | SO-20260305-001 |
| OrderType | 订单类型 | NVARCHAR(20) | APS标准化订单类型：`SALES_ORDER`（客户订单，原SO/MTO）/ `PRODUCTION_INSTRUCTION`（生产指示，原MTS/SS/SS_U）；由 `sp_ValidateAndPromoteOrders` 根据ZPQF映射得到（v5.0.24重分类） | SALES_ORDER |
| MaterialCode | 物料编码 | NVARCHAR(100) | **⚠️ v5.0.27改可空**；SP Step 0 三级解析链写入前可为空；进入Order_Canonical前SP强制验证非空 | MAT-10001 |
| **Model** | **ERP原始型号** | **NVARCHAR(100)** | **⚠️ v5.0.27新增；ERP原始型号透传；BOMNO=NULL时5号位BOM入口解析辅助；不替代MaterialCode** | C25ILB-005 |
| FactoryCode | 工厂编码 | NVARCHAR(50) | APS标准化工厂编码（⚠️ 2026-04-09业务澄清：ERP原字段需规则转换） | F001 |
| Quantity | 数量 | DECIMAL(18,4) | 订单数量 | 1000 |
| UOM | 计量单位 | NVARCHAR(20) | PCS、KG、M等 | PCS |
| DueDate | 交期 | DATE | 统一交期（2026-04-03修正：原CustomerDueDate） | 2026-03-20 |
| Priority | 优先级 | INT | 1-100，默认50 | 50 |
| BOMNO | BOM编号 | NVARCHAR(50) | v5.0.21 改可空；有值=显式BOMNO；NULL=待5号位Workset阶段解析BOM入口（2026-04-03旧"必填"口径废除） | BOM-10001 |
| TransportMode | 运输方式 | NVARCHAR(20) | 海运/空运/陆运，源事实字段（2026-04-09 v4.5新增） | 海运 |
| CustomerName | 客户名称 | NVARCHAR(200) | ERP订单表直接提供，源事实字段（2026-04-09 v4.5新增） | 某某客户株式会社 |
| **CustomerCode** | **ERP客户代码** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；ERP原始客户代码透传；用于通过CustomerCodeMap派生CustomerSegment/CustomerTier；SP不使用CustomerName推段** | CUST-001 |
| MTS_InstructionNo | 生产指示号 | NVARCHAR(50) | 来源于ERP生产指示表InstructionNo（≠OrderNo），源事实字段（2026-04-09 v4.5新增） | PI-20260305-001 |
| IssueDate | 订单发行日期 | DATE | 订单/生产指示正式下发日期，源事实字段（2026-04-09 v4.6新增） | 2026-03-01 |
| OriginalDueDate | 原始纳期 | DATE | 客户最初要求交期，MTS时=DueDate，源事实字段（2026-04-09 v4.6新增） | 2026-03-15 |
| ReceivedQty | 已入库数量 | DECIMAL(18,4) | 仅MTS：当前已完成并入库的累计数量，SO订单为NULL，源事实字段（2026-04-09 v4.6新增） | 500.0000 |
| **RawNonStockShipmentType** | **ERP原始非在库出荷区分** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；ERP原始值（全额紫票/差额紫票等），标准化后→`Order_Canonical.NonStockShipmentType`；SP不回写此字段的标准化结果** | 全额紫票 |
| **RawOrderSource** | **ERP原始订单来源** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；ERP原始值（DAT/P/O等），标准化后→`Order_Canonical.OriginalOrderSource`；SP不回写此字段的标准化结果** | DAT |
| CustomerSegment | 客户区分 | NVARCHAR(50) | `JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER/UNKNOWN`；APS衍生，由 SP 通过 CustomerCode 查 `CustomerCodeMap` 得到；**⚠️ v5.0.27变更：无匹配→`UNKNOWN`（不再默认OVERSEAS），消费方须识别UNKNOWN走保守路径；CustomerCode为NULL→NULL** | JAPAN |
| SalesOrderCategory | 销售类别 | NVARCHAR(50) | DIRECT_SALES/SALES_REPLENISHMENT，APS衍生字段（2026-04-09 v4.5新增） | DIRECT_SALES |
| DemandMaturityStatus | 需求成熟度 | NVARCHAR(50) | `PRE_CONFIRMED`（事前确认）/ `FORECAST`（预测SHIKOMI）；v5.0.24收窄，DELAYED已拆出为独立字段 DelayStatus，禁止混用（2026-04-09 v4.5新增） | PRE_CONFIRMED |
| CustomerTier | 客户分级 | NVARCHAR(20) | `VIP > KEY_ACCOUNT > STANDARD > GENERAL`；当前主要启用 VIP/GENERAL 两档，KEY_ACCOUNT/STANDARD 预留；由SP推导，默认GENERAL（v5.0.24补充等级关系说明） | GENERAL |
| **DelayStatus** | **延迟状态** | **NVARCHAR(20)** | **v5.0.24新增；独立维度，与DemandMaturityStatus禁止混用；`ON_TIME`（未延迟）/ `FIRST_DELAY`（首次延迟）/ `REPEATED_DELAY`（二次及以上延迟）；V1简化：超期均记为FIRST_DELAY** | ON_TIME |
| RawData | 原始数据 | NVARCHAR(MAX) | ERP原始报文JSON（2026-04-03审计补充） | NULL |
| SyncStatus | 同步状态（技术流转） | NVARCHAR(50) | PENDING/VALIDATED/FAILED/PROCESSED；仅表达技术流转状态，不含业务语义 | PENDING |
| **FailureCode** | **失败原因/诊断** | **NVARCHAR(50)** | **⚠️ v5.0.27更新：单值，只记最高优先级；硬失败：`SOURCESYSTEM_MISSING/SOURCEORDERID_MISSING/ORDERNO_MISSING/MATERIALCODE_MISSING/QUANTITY_INVALID/DUEDATE_MISSING/ORDER_TYPE_UNKNOWN/MATERIAL_MAPPING_AMBIGUOUS/MATERIAL_NOT_FOUND/VALIDATION_FAILED`；非阻断诊断：`BOMNO_MISSING`（订单仍 PROCESSED）；nullable** | MATERIALCODE_MISSING |
| **NextActionCode** | **后续动作** | **NVARCHAR(50)** | **⚠️ v5.0.27更新：`MATERIAL_MAPPING_REQUIRED/MASTER_DATA_FIX/MANUAL_REVIEW/WAIT_BOM_WORKSET`；nullable** | WAIT_BOM_WORKSET |
| ErrorMessage | 错误详情 | NVARCHAR(MAX) | 人类可读错误详情（补充FailureCode用） | NULL |
| SyncedAt | 同步时间 | DATETIME2 | 从ERP同步的时间 | 2026-03-05 01:00:00 |
| ProcessedAt | 处理时间 | DATETIME2 | 提升到Order_Canonical的时间（2026-04-03修正：原写入Order表） | 2026-03-05 01:05:00 |

**唯一约束**：`(SourceOrderId, SyncedAt)`（2026-04-03审计补充）

**⚠️ SyncStatus 状态机**（2026-04-03 审计补充；v5.0.21：强调仅为技术流转状态）：

| 状态 | 含义 | 迁移条件 |
|------|------|---------|
| **PENDING** | 待校验 | 从ERP写入时的初始状态 |
| **VALIDATED** | 校验通过 | `sp_ValidateAndPromoteOrders` 步骤1校验通过 |
| **FAILED** | 技术校验失败 | 关键字段非法或主数据未就绪；具体原因看FailureCode |
| **PROCESSED** | 已提升 | 成功Upsert到Order_Canonical后标记 |

```
PENDING → VALIDATED → PROCESSED（成功提升到Canonical）
PENDING → FAILED（技术校验失败；FailureCode=原因；NextActionCode=后续动作）
FAILED → PENDING（人工修正或自动重试后回归）
```

**⚠️ FailureCode × NextActionCode 双维度说明**（v5.0.21 新增）：

| 字段 | 维度 | 值域 |
|------|------|------|
| FailureCode | 失败**原因**/诊断 | 硬失败：`SOURCESYSTEM_MISSING/SOURCEORDERID_MISSING/ORDERNO_MISSING/MATERIALCODE_MISSING/QUANTITY_INVALID/DUEDATE_MISSING/ORDER_TYPE_UNKNOWN/MATERIAL_MAPPING_AMBIGUOUS/MATERIAL_NOT_FOUND/VALIDATION_FAILED`；非阻断：`BOMNO_MISSING`（订单仍可PROCESSED） |
| NextActionCode | 后续**动作** | `MATERIAL_MAPPING_REQUIRED/MASTER_DATA_FIX/MANUAL_REVIEW/WAIT_BOM_WORKSET` |

> **⚠️ v5.0.27设计红线**：
> - `FailureCode` 单值，只记最高优先级；`BOMNO_MISSING` = 非阻断诊断（SyncStatus可为PROCESSED）
> - 阻断以 `SyncStatus='FAILED'` 为准；`FailureCode` 可记录阻断原因，也可记录非阻断诊断
> - `BOMNO_MISSING + WAIT_BOM_WORKSET` = 明确非阻断组合
> - `CustomerSegment='UNKNOWN'` ≠ `'OVERSEAS'`，消费方须识别UNKNOWN走保守路径
> - SyncStatus 只表达技术流转，不包含业务分流语义

**数据来源说明**（2026-04-03 订单链路审计补充）：
- **SO/MTO客户订单**：来自ERP订单中间表，通过 `v_APS_SalesOrder` 视图暴露
- **MTS生产指示**：来自ERP生产指示中间表，同样通过 `v_APS_SalesOrder` 视图暴露（`OrderType = 'MTS'`）
- 两类来源统一走同一条同步路径，由 `OrderType` 字段区分

**技术要点**：
- 数据验证：检查物料编码是否存在、数量是否合法、必填字段是否完整
- 数据清洗：标准化日期格式、去除空格等
- 提升逻辑：由 `sp_ValidateAndPromoteOrders` 执行校验 → Upsert到Order_Canonical
- 数据生命周期：PROCESSED记录保留30天后可归档清理

---

### 2.4 CustomerCodeMap（APS本地客户编码映射表）⭐ **v5.0.24新增（2026-05-13）**

**所属库**：APS_Production  
**业务用途**：APS本地维护的客户编码→客户区分映射字典；由 `sp_ValidateAndPromoteOrders` 查询以推导 `CustomerSegment`

**定位说明**：
- ⚠️ **不是ODS共享字典**：不通过视图对外暴露，不参与 `sp_SyncMasterData` 自动同步
- **来源**：`CustomerCode.xlsx` 初始化导入，后续由APS系统管理员人工维护
- **消费方**：仅 `sp_ValidateAndPromoteOrders`

**映射规则**（来源Excel"客户区分"字段）：

| Excel原始值 | CustomerSegment枚举值 | 说明 |
|------------|----------------------|------|
| 日本 | JAPAN | 日本本土客户 |
| 国内 | DOMESTIC | 中国大陆客户 |
| 海外 | OVERSEAS | 除日本/越南外海外客户 |
| 越南 | VIETNAM | 越南客户 |
| 跨厂 | INTER_FACTORY | 厂内跨厂供给 |
| 其他 | OTHER | 其他类别（独立值，不并入INTER_FACTORY） |
| ~~失效（Activity=0）~~ | ~~OVERSEAS~~ | ⚠️ v5.0.27废止：IsActive=0行被JOIN过滤（`AND ccm.IsActive=1`），不写OVERSEAS；订单提升时视为无匹配→`UNKNOWN` |

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| CustomerCode | ERP客户代码 | NVARCHAR(20) | 主键；与订单 `CustomerCode` 字段对应 | CUST-001 |
| CustomerSegment | 客户区分 | NVARCHAR(50) | JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER | JAPAN |
| IsActive | 是否有效 | BIT | 0=客户已失效（来源：ERP Activity字段）；⚠️ v5.0.27修正：IsActive=0的行在JOIN时被过滤（`AND ccm.IsActive=1`），不参与CustomerSegment派生，订单提升时视为无匹配→UNKNOWN | 1 |
| DescriptionChn | 中文名称 | NVARCHAR(200) | 可读性字段，非业务字段 | 某某客户株式会社 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-05-13 00:00:00 |

**⚠️ v5.0.27无匹配规则变更**：若 `CustomerCode` 有值但在本表无记录，SP给 `UNKNOWN`（不再默认 `OVERSEAS`）；`CustomerCode` 为NULL时 `CustomerSegment` 保持NULL。消费方须识别 `UNKNOWN` 走保守路径。

---

## 三、主数据表

### 3.1 ProductFamily（产品族配置表）

**所属库**：APS_Production  
**业务用途**：定义7个产品族的基础信息，用于分域计算

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 产品族ID | INT | 主键，自增 | 1 |
| Code | 产品族代码 | NVARCHAR(50) | 唯一标识 | X1_MACHINE |
| Name | 产品族名称 | NVARCHAR(200) | 显示名称 | 整机产品族 |
| Description | 描述 | NVARCHAR(500) | 详细说明 | 包含最终装配的整机产品 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

---

### 3.2 Factory（工厂表）

**所属库**：APS_Production  
**业务用途**：定义多工厂信息，支持跨厂协同排程

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 工厂ID | INT | 主键，自增 | 1 |
| Code | 工厂代码 | NVARCHAR(50) | 唯一标识 | F001 |
| Name | 工厂名称 | NVARCHAR(200) | 显示名称 | 深圳工厂 |
| Location | 地理位置 | NVARCHAR(200) | 详细地址 | 广东省深圳市宝安区 |
| TimeZone | 时区 | NVARCHAR(50) | 用于跨时区排程 | China Standard Time |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

---

### 3.3 Material（物料主数据表）

**所属库**：APS_Production  
**业务用途**：定义所有物料（成品、半成品、原材料）的基础信息

**技术要点（数据流向）**（2026-04-01 v4.0更新）：
- **数据来源**：从ODS库的 `ext_ERP_Master_View` 和 `ext_MES_Material_View` 双源同构视图同步
- **同步逻辑**：通过 `sp_SyncMasterData(@SourceType)` 存储过程（v4.0双源统一），步骤1按 MaterialCode 增量 Upsert 同步至此表，MaterialType 由 APS 按前缀推导
- **负责人**：2号位（通过 `IDataLoader` 或定时Job执行）
- **同步时机**：每天00:10（ERP）、00:20（MES）

**⚠️ 维护策略（重要）**：
- **更新机制**：**增量Upsert**（非全量重建）
- **触发条件**：由 `sp_SyncMasterData` 的步骤1 MERGE 语句驱动
  - 新物料出现时 INSERT，MaterialType 由 CASE 表达式按 MaterialCode 前缀推导（FG→FINISHED_GOOD, RAW→RAW_MATERIAL, WIP→SEMI_FINISHED, ASSY→ASSY, 其他→UNKNOWN）
  - 已有物料本体属性变化时 UPDATE（MaterialName、Spec、UOM）
  - 全部仓库映射消失时标记 IsActive=0（2026-04-01 v4.0更新）
- **更新范围**：只更新发生变化的物料记录，不影响未变更的物料
- **实现方式**：
  ```sql
  -- sp_SyncMasterData 存储过程的步骤1（2026-04-01 v4.0更新）
  MERGE INTO Material AS target
  USING #Material_Source AS source  -- 按 MaterialCode 去重后的本体属性
  ON target.MaterialCode = source.MaterialCode
  WHEN MATCHED AND (属性变化 OR IsActive=0) THEN UPDATE SET ...
  WHEN NOT MATCHED BY TARGET THEN INSERT (MaterialType = CASE前缀推导) ...;
  ```
- **架构红线**：
  - ❌ **禁止**每天全量删除重建 `Material` 表
  - ✅ **必须**采用增量Upsert，保持物料ID稳定性
  - ✅ **必须**基于源端快照驱动同步，全部仓库映射消失时标记 IsActive=0

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 物料ID | INT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 唯一标识，v2.5统一字段名 | MAT-10001 |
| MaterialName | 物料名称 | NVARCHAR(200) | 显示名称，v2.5统一字段名，如"缸盖" | 缸盖 |
| Spec | 物料型号/规格 | NVARCHAR(100) | v2.7新增，如"C25ILB-005" | C25ILB-005 |
| ProductFamilyId | 产品族ID | INT NULL | v5.0.38更新：由 `sp_SyncMasterData` 步骤1c 四规则写入；仅 IsProductFamilyRequired=1 且 FamilyResolveStatus='RESOLVED' 时写入有效值；其余场景置 NULL；外键关联 ProductFamily 表 | 1 |
| MaterialType | 物料类型 | NVARCHAR(50) | FINISHED_GOOD/SEMI_FINISHED/RAW_MATERIAL | SEMI_FINISHED |
| UOM | 计量单位 | NVARCHAR(20) | PCS/KG/M等 | PCS |
| LowLevelCode | 低阶码（LLC） | INT | 用于BOM拓扑排序，0=顶层 | 2 |
| IsSimpleItem | 是否简单物料 | BIT | 1=简单物料 | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| LeadTimeDays | 提前期天数 | INT | ⚠️ v2.7废弃，请使用MaterialSupplyContext.LeadTimeDays | 7 |
| SafetyStock | 安全库存 | DECIMAL(18,4) | ⚠️ v2.7废弃，请使用MaterialSupplyContext.SafetyStock | 500 |
| IsPurchased | 是否采购件 | BIT | ⚠️ v2.7废弃，请使用MaterialSupplyContext.SupplyMode | 0 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**⚠️ v2.5更新**：字段名从`Code`/`Name`统一为`MaterialCode`/`MaterialName`

**⚠️ v2.7架构重构**：物料本体属性与仓库级上下文分离
- **新增字段**：`Spec`（物料型号/规格）
- **废弃字段**：`IsPurchased`、`SafetyStock`、`LeadTimeDays` 已下沉到 `MaterialSupplyContext` 表
- **原因**：这些字段会随仓库/工厂变化，不再作为物料本体的单值属性

**MaterialCode 与 Spec 字段的关系**：
- **MaterialCode**：APS 的统一业务键，采用 `{类型前缀}-{物料型号}-{版本号(可选)}` 格式编码
  - 参与跨系统对齐、主数据映射、订单、BOM、工艺、库存、排程等业务关系
  - 示例：`RAW-STEEL-10X20`、`FG-A900`、`WIP-C25ILB-005-V2`
- **Spec**：原始型号/规格展示字段
  - 用于展示、对照 ERP/MES 原始业务字段，保留更贴近源系统的业务表达
  - 示例：`C25ILB-005`、`STEEL-10X20`、`A900`
- **关系说明**：MaterialCode 允许与 Spec 出现部分重复（如 MaterialCode 为 `WIP-C25ILB-005`，Spec 为 `C25ILB-005`），但二者语义不同，不视为重复设计
  - MaterialCode 是 APS 统一业务键，需要类型前缀和版本号管理
  - Spec 是原始型号展示字段，保留源系统原始表达

**数据一致性保证**：
- `Material` 表始终反映 `MaterialMapping` 表中 `IsCurrent = 1` 的当前有效映射关系
- 物料ID（`Id`）在首次创建后保持稳定，不因映射关系变更而改变
- 历史版本的映射关系保留在 `MaterialMapping` 表中，支持时间点查询

---

### 3.4 ResourceGroup（资源组表）⚠️ **v5.0废弃（2026-04-01）**

> **⚠️ v5.0废弃**：静态资源组无法表达真实设备可替代性（取决于物料+路径+工序的动态组合）。  
> **替代方案**：组织/统计/前端筛选 → `ResourceOrgGroup`；排程能力建模 → `OperationResourceEligibility`。  
> 保留此表定义仅为兼容，新代码禁止引用。

（原字段清单见 archived 版本，此处省略）

---

### 3.4b ResourceOrgGroup（资源组织维度表）⭐ **v5.0新增（2026-04-01）**

**所属库**：APS_Production  
**业务用途**：仅用于统计切片、前端筛选、组织归类。**不再用于** Routing 能力分组或工序资源可替代性判断。

**技术要点**：
- **数据来源**：APS 本地手工维护
- **负责人**：PMC 或车间工艺员
- **更新频率**：组织结构调整时低频更新

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 资源组ID | INT | 主键，自增 | 1 |
| Code | 组织组代码 | NVARCHAR(50) | 唯一编号 | RG-MC |
| Name | 组织组名称 | NVARCHAR(200) | 显示名称 | 注塑机组 |
| FactoryId | 所属工厂ID | INT | 外键关联Factory表 | 1 |
| Description | 描述 | NVARCHAR(500) | 组织组说明 | 包含所有注塑机设备 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 15:00:00 |

---

### 3.5 Resource（资源表 - 外部主数据镜像）（2026-04-01 v5.0重构；v5.0.16 部门维度升级）

**所属库**：APS_Production  
**业务用途**：外部设备主数据在 APS 的本地镜像表（**非手工自建主数据**）

**技术要点（数据流向）**（2026-04-01 v5.0更新；v5.0.16 部门维度升级）：
- **数据来源**：从ODS库的 `ext_MES_APS_Resource_View` 同步（v5.0.13 命名统一；源头为 MES 设备表；未来 EAM 上线时 `ext_EAM_APS_Resource_View` 双源并存）
- **同步执行体**：`sp_SyncResourceData(@SourceType)`（DDL v5.0.13 新增；v5.0.16 升级双字典映射）；v1 仅 'MES' 分支走 MERGE，'EAM' 分支 NOT_IMPLEMENTED
- **同步方式**：每天全量刷新（设备主数据变化频率低）
- **负责人**：2号位（通过 `IDataLoader` 定时同步）
- **同步时机**：每天 00:10（v5.0.13.1 对齐走查 V3.4：与 sp_SyncMasterData 同窗口并行）
- **v5.0 变更**：ResourceGroupId 外键已移除；Code→ResourceCode，Name→ResourceName，Capacity→CapacityFactor；新增 ExternalResourceId、SourceSystem
- **🔄 v5.0.16 变更**：**删除 `WorkshopCode`**（业务确认 MES 也无"车间"概念）；**新增 `ProductionDepartmentId NOT NULL`**（FK → ProductionDepartment.Id）+ **`SourceProductionDeptCode`**（审计用）；`sp_SyncResourceData` MERGE 加 ProductionDepartment 双字典映射 JOIN

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 资源ID | INT | 主键，自增 | 1 |
| ResourceCode | 资源编码 | NVARCHAR(50) | APS统一业务键（v5.0重命名，原Code） | MC-001 |
| ResourceName | 资源名称 | NVARCHAR(200) | 设备名称（v5.0重命名，原Name） | 注塑机001号 |
| ExternalResourceId | 源系统物理主键 | NVARCHAR(50) | v5.0新增：MES设备ID或EAM资产ID | 60001 |
| SourceSystem | 来源系统 | NVARCHAR(20) | v5.0新增：MES / EAM | MES |
| FactoryId | 所属工厂ID | INT | 外键关联Factory表；工厂归属（汇总/跨厂/日历/物流边界，**不作为 Routing 选择主条件**） | 1 |
| **ProductionDepartmentId** 🆕 | **生产部门 ID** | INT | **v5.0.16 新增 NOT NULL**；FK → ProductionDepartment.Id；排程责任部门归属（汇总/资源能力归属维度） | 12 |
| **SourceProductionDeptCode** 🆕 | **源系统部门码** | NVARCHAR(50) | **v5.0.16 新增**；审计用；由 sp_SyncResourceData 从契约视图 ProductionDeptCode 字段带入 | DEPT-MACH-01 |
| ResourceType | 资源类型 | NVARCHAR(50) | MACHINE / LINE / MANUAL_STATION | MACHINE |
| Status | 设备状态 | NVARCHAR(20) | AVAILABLE / MAINTENANCE / DECOMMISSIONED | AVAILABLE |
| CapacityFactor | 产能系数 | DECIMAL(18,4) | 产能倍率（v5.0重命名，原Capacity），1.0=标准 | 1.2 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 15:00:00 |

> 🔻 **v5.0.16 删除字段**：`WorkshopCode`（业务确认 MES 中也无"车间/Workshop"概念；改为 ProductionDepartmentId）

**索引**：
- `IX_Resource_Factory` ON `(FactoryId, ResourceType) WHERE IsActive=1`
- `IX_Resource_Source` ON `(SourceSystem, ExternalResourceId) WHERE IsActive=1`
- 🆕 `IX_Resource_Dept` ON `(ProductionDepartmentId, ResourceType) WHERE IsActive=1`（v5.0.16：部门粒度资源筛选）

### 3.5b ResourcePlanningContext（资源排程参数表）⭐ **v5.0新增（2026-04-01）**

**所属库**：APS_Production  
**业务用途**：APS 本地排程控制信息，不污染外部资源事实层

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 参数ID | INT | 主键，自增 | 1 |
| ResourceId | 资源ID | INT | 外键关联Resource表 | 5 |
| CalendarPolicyId | 排程日历策略ID | INT | 关联日历策略 | 1 |
| DispatchPriority | 派工优先级 | INT | 越小越优先，默认100 | 50 |
| LocalDisableFlag | APS本地禁用标记 | BIT | 1=APS侧禁用该资源 | 0 |
| OverrideCapacityFactor | 覆盖产能系数 | DECIMAL(18,4) | APS侧覆盖，NULL=使用Resource原值 | 0.8 |
| EffectiveFrom | 生效开始日期 | DATE | 参数生效起始 | 2026-01-01 |
| EffectiveTo | 生效结束日期 | DATE | 参数失效日期，NULL=永久有效 | NULL |
| UpdatedBy | 维护人 | NVARCHAR(50) | 最后修改人 | admin |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 15:00:00 |

---

### 3.5 BOM（物料清单表）

**所属库**：APS_Production  
**业务用途**：定义物料之间的父子关系（成品由哪些半成品/原材料组成）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | BOM ID | BIGINT | 主键，自增 | 1 |
| ParentMaterialId | 父物料ID | INT | 外键关联Material表（成品） | 100 |
| ChildMaterialId | 子物料ID | INT | 外键关联Material表（半成品/原材料） | 200 |
| Quantity | 用量 | DECIMAL(18,6) | ⚠️ 单位用量，不累乘 | 2.5 |
| ScrapRate | 损耗率 | DECIMAL(5,4) | 0.05表示5%损耗 | 0.05 |
| LeadTimeOffset | 提前期偏移 | INT | 单位：天，负数表示提前 | -2 |
| BOMLevel | BOM层级 | INT | 1-10，1表示直接子件 | 1 |
| EffectiveFrom | 生效日期 | DATE | BOM生效开始日期 | 2026-01-01 |
| EffectiveTo | 失效日期 | DATE | BOM失效日期，NULL表示永久有效 | NULL |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务定位**：
- APS当前计划窗口内的标准BOM关系缓存表
- 用于当前窗口范围内的BOM树展示、What-If插单演练、结构查询
- **不作为企业全量BOM主数据维护总表**

**数据来源**：
- 基于**当前计划版本对应BatchNo**的 `APS_BOM_RAW` 标准化生成
- `APS_BOM_RAW` 本身来自ODS库 `MES_APS_BOM_Workset` 的批次展开结果

**转换逻辑**：
1. 按当前计划版本**指定的BatchNo**读取 `APS_BOM_RAW`
2. 对父子边关系做去批次语义下的去重处理
3. 将 `ParentMaterialCode` / `ChildMaterialCode` 映射为本地 `MaterialId`
4. 保留 `Quantity` 为单位用量，严禁累乘
5. 仅覆盖当前90天活跃计划窗口

**⛔ 架构红线**：
- 严禁将全量4000万BOM同步到本表
- 严禁按"时间最新批次"直接取数，**必须按指定BatchNo + READY状态校验**
- `MES_API_BOM_Request_Detail` 写入前必须应用层 `.Distinct()` 去重，防止批次展开膨胀
- `Quantity` 只允许存单位用量，不允许累乘展开值

**负责人**：
- **5号位**负责 `APS_BOM_RAW` 的拉取与装载
- 2号位负责BOM表的结构标准化
- 5号位负责BOM业务口径校验

**同步时机**：
- 夜间ODS拉取完成、当前批次LLC计算完成后生成
- 具体时间以调度配置为准（预计00:35左右）

---

### 3.6 Routing（工艺路线表）⚠️ **v5.0废弃（2026-04-01）**

> **⚠️ v5.0废弃**：线性 OperationSeq 无法支撑装配车间并行/串行混合工艺；MinBatchSize/MaxBatchSize 无 ODS 来源不应在工艺事实层。  
> **替代方案**：工序节点 → `RoutingOperation`；工序依赖 → `RoutingDependency`；批量参数 → `RoutingPlanningParam`；工序资源能力 → `OperationResourceEligibility`。  
> 保留此表定义仅为兼容，新代码禁止引用。

（原字段清单见 archived 版本，此处省略）

---

### 3.6b RoutingOperation（工序节点表）⭐ **v5.0新增（2026-04-01）；v5.0.16 部门维度升级**

**所属库**：APS_Production  
**业务用途**：承接 ODS 工序节点，替代原线性 Routing 的工序定义部分

**🔄 v5.0.16 变更**：新增 `ProductionDepartmentId NOT NULL` （FK → ProductionDepartment.Id）；唯一键升级为 `(MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode)`。业务事实：同物料同 StageCode 下不同部门可有不同小工序集合。

**技术要点（数据流向）**：
- **数据来源**：从ODS库的 `ext_MES_APS_Routing_Operation_View` 增量 Upsert
- **v5.0.1变更（2026-04-02）**：ODS视图输出`MES_ID`+`Model`（MES原生标识），非 MaterialCode。2号位装载时通过 `MaterialMapping(Source='MES', SourceID=MES_ID)` 统一映射得到 `MaterialId`
- **负责人**：2号位（定时拉取与装载 + MES_ID→MaterialId映射）；3号位（负全责在ODS库编写视图，梳理 MES 28 张离散工艺表，老结构ETL处理为MES_ID）
- **同步时机**：每天 00:30
- **V1约束**：RouteCode='DEFAULT', PathId=1（只启用默认路径）
- **V2扩展**：RouteCode/PathId 支持多路径

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 工序ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| **ProductionDepartmentId** 🆕 | **生产部门 ID** | INT | **v5.0.16 新增 NOT NULL**；FK → ProductionDepartment.Id；部门版本路由锁定维度；来源：ODS 契约视图输出 ProductionDeptCode → 2 号位装载时 JOIN ProductionDepartment.Id 映射 | 12 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | V1固定'DEFAULT'，V2多路径 | DEFAULT |
| PathId | 路径序号 | INT | V1固定1，V2多路径 | 1 |
| OperationCode | 工序编码 | NVARCHAR(50) | 路径内唯一 | OP-010 |
| OperationName | 工序名称 | NVARCHAR(200) | 显示名称 | 注塑成型 |
| ProcessType | 工序类型 | NVARCHAR(50) | **辅助分类标签**（值域见 §1.9b `ProcessTypeDict`，预留骨架）；**v5.0.12 口径：仅用于报表/粗分组/统计，不参与 BOM↔Routing 对接，不作为 1 号位排程主键** | MACHINING |
| StageCode | 所属大工艺阶段码 | NVARCHAR(50) | v5.0.6 引入；**v5.0.12：BOM↔Routing 对接主键之一**（二元组 = MaterialCode + StageCode）；**必须取自 §1.9 `StageDict`**；MES 本地叫法由 `MES_APS_Routing_Stage_View` 负责映射标准化 | TJ_MACH |
| StandardDuration | 标准工时 | DECIMAL(18,4) | 单位：分钟 | 150.0 |
| SetupTime | 准备时间 | DECIMAL(18,4) | 换型时间，单位：分钟 | 30.0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**唯一索引**（v5.0.16 三元组升级）：`(MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode)`

**补助索引**（v5.0.16 新增）：`IX_RoutingOp_MaterialDept` ON `(MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId) WHERE IsActive=1` —— 1 号位主消费索引

---

### 3.6c RoutingDependency（工序依赖边表）⭐ **v5.0新增（2026-04-01）；v5.0.16 部门维度升级**

**所属库**：APS_Production  
**业务用途**：表达工序间的有向依赖关系，支持并行/串行混合工艺。让 1号位引擎面对工艺图而非序列表。

**技术要点（数据流向）**：
- **数据来源**：从ODS库的 `ext_MES_APS_Routing_Dependency_View` 增量 Upsert
- **v5.0.1变更（2026-04-02）**：同 RoutingOperation，ODS视图输出`MES_ID`+`Model`，2号位装载时映射MaterialId
- **负责人**：2号位（拉取装载 + 映射）；3号位（ODS视图，老结构ETL处理MES_ID）
- **同步时机**：每天 00:30（与 RoutingOperation 同批次）

**并行/串行表达**：
- **并行**：工序A→B 和 A→C（B、C 可并行执行）
- **汇合**：B→D 和 C→D（D 必须等 B 和 C 都完成）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 依赖ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| **ProductionDepartmentId** 🆕 | **生产部门 ID** | INT | **v5.0.16 新增 NOT NULL**；FK → ProductionDepartment.Id；与 RoutingOperation 同维度，避免不同部门依赖关系混垍 | 12 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | 同 RoutingOperation | DEFAULT |
| PathId | 路径序号 | INT | 同 RoutingOperation | 1 |
| FromOperationCode | 前驱工序编码 | NVARCHAR(50) | 前驱工序 | OP-010 |
| ToOperationCode | 后继工序编码 | NVARCHAR(50) | 后继工序 | OP-020 |
| DependencyType | 依赖类型 | NVARCHAR(10) | ES（V1）/ SS / FF（V2扩展） | ES |
| LagTime | 延迟时间 | DECIMAL(18,4) | 单位：分钟，0=紧跟前驱完成 | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**唯一索引**（v5.0.16 四元组升级）：`(MaterialId, ProductionDepartmentId, RouteCode, PathId, FromOperationCode, ToOperationCode)`

**补助索引**（v5.0.16 新增）：`IX_RoutingDep_MaterialDept` ON `(MaterialId, ProductionDepartmentId, RouteCode, PathId) WHERE IsActive=1`

---

### 3.6c2 RoutingStage（大工艺阶段表）⭐ **v5.0.6新增，v5.0.7定位调整，v5.0.12 删除 StageSeq**

**所属库**：APS_Production  
**业务用途**：MES工艺侧的大工艺阶段**字典**（业务管理级粒度），记录"某物料在哪些大工艺阶段存在配置"。

**⚠️ v5.0.12 定位强化**：
- 此表仅承载"该物料在哪些大工艺阶段存在配置"的**字典**信息，**不承载任何顺序信息**
- 排程权威阶段顺序**唯一**来自 `MES_APS_BOM_Workset_StageDetail.StageSeq`（BOM 派生结果）
- **已知限制**：MES 工艺侧不包含外协阶段，数据不完整；完整阶段链由 StageDetail 承载
- **职责分离**：RoutingStage=阶段字典（3 号位契约 → 2 号位装载），StageDetail=BOM 派生结果（5 号位产出 → 2 号位搬运），不混写

**v5.0.12 重大变更**：
- **删除 `StageSeq` 字段** + 同步删除 `IX_RoutingStage_Seq` 索引
- 理由：跨物料/跨根产品语境下 MES 工艺侧给不出正确的跨大工艺顺序号；保留字段会诱导误用
- 影响面：任何下游代码若读取 `RoutingStage.StageSeq` 视为 bug，改读 `StageDetail.StageSeq`
- `StageCode` 取值**必须**来自 `StageDict`（§1.9）；MES 本地叫法由 `MES_APS_Routing_Stage_View` 负责映射标准化

**技术要点（数据流向）**：
- **数据来源**：从ODS库的 `ext_MES_APS_Routing_Stage_View` 增量 Upsert（MES原始大工艺阶段数据 + StageDict 标准化映射）
- ODS视图输出`MES_ID`+`Model`，2号位装载时通过 `MaterialMapping(Source='MES', SourceID=MES_ID)` 映射为 `MaterialId`
- **负责人**：2号位（定时拉取与装载 + MES_ID→MaterialId映射）；3号位（ODS视图 + MES 本地阶段 → StageDict 的映射责任）
- **同步时机**：每天 00:30（与 RoutingOperation 同批次）
- **V1约束**：RouteCode='DEFAULT', PathId=1
- **V2扩展**：RouteCode/PathId 支持多路径

**⚠️ 三层模型（v5.0.12 更新）**：
- `OperationName` / `OperationCode` = 具体工序（如 NC / MC / 切断 / 精修）——**见 RoutingOperation 表**
- `ProcessType` = 工序级**辅助分类标签**（如 MACHINING / ASSEMBLY）——不参与排程对接
- `StageCode` = 业务管理级**大工艺阶段码**（如 TJ_MACH / BJ_SURF）——**BOM↔Routing 对接主键**
- 三者**互不替换、互不等同**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 阶段ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | 与RoutingOperation键体系对齐 | DEFAULT |
| PathId | 路径序号 | INT | 与RoutingOperation键体系对齐 | 1 |
| StageCode | 大工艺阶段码 | NVARCHAR(50) | **必须取自 `StageDict`（§1.9）** | TJ_MACH |
| StageName | 阶段名称 | NVARCHAR(200) | 中文名（如机加/外协/涂装） | 机加 |
| ~~StageSeq~~ | ~~阶段顺序号~~ | ~~INT~~ | **v5.0.12 已删除**——权威在 `StageDetail.StageSeq` | — |
| IsOutsource | 是否外协阶段 | BIT | 1=外协 | 0 |
| IsStockPoint | 是否半成品库存断点 | BIT | 1=断点（可入库暂存） | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**唯一索引**：`(MaterialId, RouteCode, PathId, StageCode)`

---

### 3.6c3 StageLeadTimeParam（阶段提前期参数表）⭐ **v5.0.7新增（2026-04-13）**

**所属库**：APS_Production  
**业务用途**：为无小工序的外协阶段、以及Routing数据不完整的阶段提供参数化提前期。1号位消费：读取StageDetail阶段顺序 → 对无RoutingOperation的阶段查此表生成标准Task。

**命中顺序（从细到粗降级）**：
1. MaterialCode + FactoryCode + StageCode
2. ProductFamilyCode + FactoryCode + StageCode
3. ProductionDeptCode + FactoryCode + StageCode  🔄 **v5.0.16 RENAME from WorkshopCode**
4. FactoryCode + StageCode
5. 全局阶段默认值（IsDefault=1）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 参数ID | INT | 主键，自增 | 1 |
| FactoryCode | 工厂编码 | NVARCHAR(50) | 工厂编码 | FAC-TJ |
| StageCode | 大工艺阶段码 | NVARCHAR(50) | 如TJ_OUTS/BJ_SURF | TJ_OUTS |
| **ProductionDeptCode** 🔄 | **生产部门编码** | NVARCHAR(50) | **v5.0.16 RENAME from WorkshopCode**；APS 自定义命中细粒度（纯字符串，不强 FK） | DEPT-MACH-01 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 可选，物料级精确匹配 | MAT-20001 |
| ProductFamilyCode | 产品族编码 | NVARCHAR(50) | 可选，产品族级匹配 | PF-HOUSING |
| LeadTimeDays | 提前期（天） | DECIMAL(18,2) | 日级提前期 | 3.0 |
| LeadTimeHours | 提前期（小时） | DECIMAL(18,2) | 小时级提前期（更细粒度） | 72.0 |
| Priority | 命中优先级 | INT | 数值越小优先级越高 | 10 |
| EffectiveFrom | 生效起始时间 | DATETIME2 | 生效起始 | 2026-01-01 00:00:00 |
| EffectiveTo | 生效截止时间 | DATETIME2 | NULL=永久有效 | NULL |
| IsDefault | 是否全局默认 | BIT | 1=全局阶段默认值（最低优先级兜底） | 0 |
| IsActive | 是否启用 | BIT | 1=启用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-04-13 00:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-04-13 00:00:00 |

**索引**：`(StageCode, FactoryCode, IsActive)` INCLUDE 匹配字段；`(StageCode, IsDefault)` 过滤启用的默认值

**数据维护**：由5号位或运维人员配置，支持时效管理（EffectiveFrom/EffectiveTo）

---

### 3.6d OperationResourceEligibility（工序资源能力关系表）⭐ **v5.0新增（2026-04-01）；v5.0.16 部门维度升级**

**所属库**：APS_Production  
**业务用途**：定义某物料、某路径、某工序允许使用哪些资源。替代原 ResourceGroup 的排程能力分组功能。

**技术要点（数据流向）**：
- **数据来源**：从ODS库的 `ext_APS_OperationResourceEligibility_View` 增量 Upsert
- **v5.0.1变更（2026-04-02）**：同 RoutingOperation，ODS视图输出`MES_ID`+`Model`，2号位装载时映射MaterialId
- **负责人**：2号位（拉取装载 + 映射）；3号位（从 MES 工序-设备能力关系表输出 ODS 视图，老结构ETL处理MES_ID）
- **同步时机**：每天 00:35
- **关键语义**：同样两台设备，生产不同产品或走不同路径时，可替代性可能不同

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 能力ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| **ProductionDepartmentId** 🆕 | **生产部门 ID** | INT | **v5.0.16 新增 NOT NULL**；FK → ProductionDepartment.Id；资源能力关系按部门场景划分（资源归属部门与能力关系适用部门不一定等价） | 12 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | 同 RoutingOperation | DEFAULT |
| PathId | 路径序号 | INT | 同 RoutingOperation | 1 |
| OperationCode | 工序编码 | NVARCHAR(50) | 对应 RoutingOperation 的工序 | OP-010 |
| ResourceId | 资源ID | INT | 外键关联Resource表 | 5 |
| Priority | 优先级 | INT | 1=最优，越小越优先 | 1 |
| CapacityFactor | 产能系数 | DECIMAL(18,4) | 该资源执行该工序的产能系数 | 1.0 |
| IsPrimary | 是否首选资源 | BIT | 1=首选 | 1 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| EffectiveFrom | 生效开始日期 | DATE | 能力生效起始 | 2026-01-01 |
| EffectiveTo | 生效结束日期 | DATE | 能力失效日期，NULL=永久有效 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**唯一索引**（v5.0.16 六元组升级）：`(MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, ResourceId)`

**补助索引**（v5.0.16 新增）：
- `IX_OpResElig_MaterialDept` ON `(MaterialId, ProductionDepartmentId, OperationCode) WHERE IsActive=1`
- `IX_OpResElig_Resource` ON `(ResourceId) WHERE IsActive=1`

---

### 3.6e RoutingPlanningParam（排程规划参数表）⭐ **v5.0新增（2026-04-01）**

**所属库**：APS_Production  
**业务用途**：承接不在 ODS 工艺事实中的排程规划参数（从原 Routing 表拆出 MinBatchSize/MaxBatchSize）

**技术要点**：
- **数据来源**：APS 本地手工维护（未来如 MES 提供，可通过 ODS 视图接回）
- **负责人**：2号位
- **更新频率**：按需维护

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 参数ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| RouteCode | 工艺路径编码 | NVARCHAR(50) | 同 RoutingOperation | DEFAULT |
| PathId | 路径序号 | INT | 同 RoutingOperation | 1 |
| OperationCode | 工序编码 | NVARCHAR(50) | 对应 RoutingOperation 的工序 | OP-010 |
| MinBatchSize | 最小批量 | DECIMAL(18,4) | 最小加工批量 | 10 |
| MaxBatchSize | 最大批量 | DECIMAL(18,4) | 最大加工批量 | 1000 |
| TransferBatchSize | 转移批量 | DECIMAL(18,4) | 工序间流转单位 | 50 |
| SourceSystem | 来源系统 | NVARCHAR(20) | MES / APS_LOCAL | APS_LOCAL |
| MaintainedBy | 维护人 | NVARCHAR(50) | 最后修改人 | admin |
| EffectiveFrom | 生效开始日期 | DATE | 参数生效起始 | 2026-01-01 |
| EffectiveTo | 生效结束日期 | DATE | 参数失效日期，NULL=永久有效 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**唯一索引**：`(MaterialId, RouteCode, PathId, OperationCode)`

---

### 3.7 ProductionDepartment（生产部门字典表）⭐ **v5.0.16 新增**

**所属库**：APS_Production  
**业务定位**：APS 自维护的"排程责任部门"标准字典；不是审批组织树，不承担行政组织语义。

**🔑 核心业务规则**（业务确认）：
- 一个 `ProductionDepartment` **只归属一个 `StageCode`**（部门 vs 阶段 1:1）
- 一个 `StageCode` 可对应多个 `ProductionDepartment`（阶段 vs 部门 1:N）
- `StageCode` 必须取自 §1.9 `StageDict`（不允许自由填写新阶段）

**消费方一览**：

| 消费方 | 用途 |
|---|---|
| `Resource.ProductionDepartmentId`（§3.5） | 资源归属部门（汇总/能力归属） |
| `RoutingOperation.ProductionDepartmentId`（§3.6b） | 部门版本路由锁定 |
| `RoutingDependency.ProductionDepartmentId`（§3.6c） | 部门版本依赖锁定 |
| `OperationResourceEligibility.ProductionDepartmentId`（§3.6d） | 部门场景能力关系 |
| `MaterialSupplyContext.DefaultProductionDepartmentId`（§6.6） | 仓库级默认部门 |
| `MaterialStageDeptContext.DefaultProductionDepartmentId`（§3.9） | **1 号位排程主链入口** |

**与审批系统解耦**：未来审批可有 OrgUnit 表 → 与本表做映射；本表**不接审批组织**。  
**与 ResourceOrgGroup 区分**：本表=排程主链维度；ResourceOrgGroup=看板筛选切片，**职责不同，不可合并**。

**维护方**：0 号位审批 + 业务侧维护

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 部门 ID | INT | 主键，自增 | 12 |
| DeptCode | 部门业务键 | NVARCHAR(50) | UNIQUE；APS 业务键 | DEPT-MACH-01 |
| DeptName | 部门中文名 | NVARCHAR(200) | 业务可读名 | 加工一部 |
| FactoryId | 工厂归属 | INT | FK → Factory.Id；可空（兼容早期未明确归厂的部门） | 1 |
| StageCode | 单值归属阶段 | NVARCHAR(20) | NOT NULL；业务约束 1:1；软引用 StageDict.StageCode | TJ_MACH |
| DeptType | 业务标签 | NVARCHAR(50) | 可选：MACHINING/ASSEMBLY/SURFACE/OUTSOURCE/SPECIAL/OTHER | MACHINING |
| SourceSystem | 来源标记 | NVARCHAR(20) | ERP / MES / APS 自建 | ERP |
| SourceDeptCode | 源系统部门码 | NVARCHAR(50) | 审计用 | 0301 |
| IsSchedulingDept | 是否参与排程 | BIT | 0=仅作汇总维度，不承担 Routing 路由职责 | 1 |
| IsActive | 是否启用 | BIT | 1=启用 | 1 |
| UpdatedBy | 维护人 | NVARCHAR(100) | 最后修改人工号 | U001 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-04-29 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-29 10:00:00 |

**索引**：
- `IX_ProductionDepartment_Factory` ON `(FactoryId) WHERE IsActive=1`
- `IX_ProductionDepartment_Stage` ON `(StageCode) WHERE IsActive=1`
- `IX_ProductionDepartment_DeptType` ON `(DeptType) WHERE IsActive=1`

---

### 3.8 MaterialStageDeptOverride（人工维护/覆盖表）⭐ **v5.0.16 新增**

**所属库**：APS_Production  
**业务定位**：弥补 MSC 数据缺失/冲突的人工维护入口；2 号位 `sp_RebuildMaterialStageDeptContext` 的输入源之一。

**适用场景**：
1. MSC 中没有生产部门
2. MSC 自动归一化后出现歧义/冲突，无法自动拍板
3. ERP/MES 信息不全，需业务显式指定

**🔑 维护粒度**：必须维护到 **(Model 或 MaterialCode) × StageCode → ProductionDeptCode**（部门是物料 × 阶段联合属性）
- ⚠️ **不能只维护 Model → Department**

**输入键策略**：
- 业务人员可用 `Model` 录入（更熟悉）；2 号位导入时做 `Model → MaterialCode` 1:1 检查
- `Model` 1:N 多个 `MaterialCode` 时**拒收**，返回明细，要求业务确认到 `MaterialCode`（避免误覆盖整串规格）

**优先级**：人工维护 > 自动草稿（详见 `sp_RebuildMaterialStageDeptContext` Step 3）

**维护方**：业务人员（含 0 号位审批补丁）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 覆盖记录 ID | BIGINT | 主键，自增 | 1 |
| Model | 业务录入键 | NVARCHAR(100) | 与 MaterialCode 至少填一项（CHECK 约束） | MGGMB40 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务能直接给出时优先填这里 | MGGMB40-450 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | NOT NULL；必须取自 StageDict | BJ_MACH |
| ProductionDeptCode | 指定部门码 | NVARCHAR(50) | NOT NULL；必须存在于 ProductionDepartment.DeptCode | DEPT-MACH-01 |
| Reason | 维护原因 | NVARCHAR(500) | 说明为何人工维护 | MSC 缺该物料 |
| ValidFrom | 生效开始时间 | DATETIME2 | NOT NULL DEFAULT GETDATE() | 2026-04-29 00:00:00 |
| ValidTo | 生效结束时间 | DATETIME2 | NULL=当前有效 | NULL |
| IsCurrent | 是否当前版本 | BIT | 1=当前有效 | 1 |
| CreatedBy | 录入人 | NVARCHAR(100) | 工号 | U007 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-04-29 08:00:00 |
| UpdatedBy | 维护人 | NVARCHAR(100) | 最后修改人 | U007 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-29 10:00:00 |

**约束**：`CHECK (Model IS NOT NULL OR MaterialCode IS NOT NULL)` —— 至少填一项业务键

**索引**：
- `IX_MaterialStageDeptOverride_CurrentByMaterial` UNIQUE ON `(MaterialCode, StageCode) WHERE IsCurrent=1 AND MaterialCode IS NOT NULL`
- `IX_MaterialStageDeptOverride_CurrentByModel` UNIQUE ON `(Model, StageCode) WHERE IsCurrent=1 AND Model IS NOT NULL AND MaterialCode IS NULL`
- `IX_MaterialStageDeptOverride_History` ON `(MaterialCode, StageCode, ValidFrom, ValidTo)`

---

### 3.9 MaterialStageDeptContext（物料×阶段→默认部门 上下文表）⭐ **v5.0.16 新增 / 1 号位排程消费入口**

**所属库**：APS_Production  
**业务定位**：2 号位 `sp_RebuildMaterialStageDeptContext` 的正式产出；**1 号位排程唯一消费入口**。

**🔑 消费键**：`(MaterialId, StageCode) → DefaultProductionDepartmentId`  
含义：某物料在某大工艺阶段下，**当前默认**由哪个生产部门生产。

**数据来源**（`SourceType` 字段）：
- `AUTO` = MSC 自动归一化（多数）
- `MANUAL` = `MaterialStageDeptOverride` 人工覆盖
- `MIXED` = 自动草稿 + 人工补丁混合

**当前有效约束**：同一时点同 `(MaterialId, StageCode)` 只能有 1 条 `IsCurrent=1`（SCD Type 2）。

**🔧 重建触发**（详见 `sp_RebuildMaterialStageDeptContext`）：
- `FULL` = 每日定时全量重建（如 02:30）
- `INCR` = MSC 同步后增量重建（仅处理 MSC 在本批次变更的 (MaterialId, StageCode)）
- `PARTIAL` = 人工 Override 提交后局部重建（@TargetMaterialIds + @TargetStageCodes 二维过滤）

**📋 1 号位接口契约**（v5.0.16 红线）：
```text
排程从 StageDetail 拿 (MaterialId, StageCode)
  → 查本表得 DefaultProductionDepartmentId
    → 按 (MaterialId, ProductionDepartmentId, StageCode) 锁定 Routing 三件套（§3.6b/c/d）
```

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | Context 记录 ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料 ID | INT | NOT NULL；FK → Material.Id | 100 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | NOT NULL；必须存在于 StageDict | BJ_MACH |
| DefaultProductionDepartmentId | 默认生产部门 ID | INT | NOT NULL；FK → ProductionDepartment.Id | 12 |
| SourceType | 数据来源类型 | NVARCHAR(10) | NOT NULL；CHECK：AUTO / MANUAL / MIXED | AUTO |
| SourceDetail | 组装来源说明 | NVARCHAR(500) | 如"MSC 唯一推导" / "Override#123 覆盖" / "MSC+Override 混合" | MSC 唯一推导 |
| ValidFrom | 生效开始时间 | DATETIME2 | NOT NULL DEFAULT GETDATE() | 2026-04-29 02:30:00 |
| ValidTo | 生效结束时间 | DATETIME2 | NULL=当前有效 | NULL |
| IsCurrent | 是否当前版本 | BIT | 1=当前有效 | 1 |
| LastRebuildBatchNo | 最近重建批次号 | NVARCHAR(50) | 用于追溯 | DAILY-20260429 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-04-29 02:30:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后修改时间 | 2026-04-29 02:30:00 |

**索引**：
- `IX_MaterialStageDeptContext_Current` UNIQUE ON `(MaterialId, StageCode) WHERE IsCurrent=1` —— **1 号位主消费索引**
- `IX_MaterialStageDeptContext_History` ON `(MaterialId, StageCode, ValidFrom, ValidTo)`
- `IX_MaterialStageDeptContext_Dept` ON `(DefaultProductionDepartmentId, IsCurrent) WHERE IsCurrent=1` —— 部门反查

---

### 3.9b MaterialStageDeptContext_Issues（Context 重建降级登记）⭐ **v5.0.16 新增**

**所属库**：APS_Production  
**业务定位**：`sp_RebuildMaterialStageDeptContext` 重建时遇到无法自动拍板的情况，登记到本表。

**降级哲学**（v5.0.16 红线，与 BOM_Workset_Issues 同向）：旧值不动（`IsCurrent=1` 上一版本继续供 1 号位使用），新问题登记到本表，待人工修正 Override 后局部重建切换新版本。

**典型 IssueType**：

| IssueType | 含义 | 默认 Severity |
|---|---|---|
| `MULTI_DEPT_CONFLICT_FOR_STAGE` | MSC 同物料同阶段对应多部门，无法自动拍板 | WARN |
| `MISSING_DEPT_IN_MSC` | MSC 该物料该仓库无 DefaultProductionDept | WARN |
| `DEPT_NOT_IN_DICT` | MSC 部门码在 ProductionDepartment 字典中找不到 | ERROR |
| `STAGE_NOT_IN_DICT` | 推导出的 StageCode 在 StageDict 中找不到 | ERROR |
| `MTS_INCONSISTENT` | MTS 中部门与 MSC/Override 结果不一致（一致性校验降级） | INFO |
| `OVERRIDE_MODEL_AMBIGUOUS` | Override 维护时 Model 1:N 多个 MaterialCode（导入拒收） | ERROR |

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 问题记录 ID | BIGINT | 主键，自增 | 1 |
| BatchNo | 触发批次 | NVARCHAR(50) | 触发本次重建的批次 | DAILY-20260429 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 部分场景（如 OVERRIDE_MODEL_AMBIGUOUS）仅有 Model | MGGMB40-450 |
| Model | 物料型号 | NVARCHAR(100) | 业务输入键 | MGGMB40 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | 可空（如 OVERRIDE_MODEL_AMBIGUOUS 不限定阶段） | BJ_MACH |
| IssueType | 问题类型 | NVARCHAR(50) | 见上枚举 | MULTI_DEPT_CONFLICT_FOR_STAGE |
| Severity | 严重程度 | NVARCHAR(20) | CHECK：INFO / WARN / ERROR | WARN |
| Detail | 明细描述 | NVARCHAR(2000) | 含冲突部门列表 / Model 多解列表等 | MSC 给出 [DEPT-A, DEPT-B] |
| DegradeAction | 降级动作 | NVARCHAR(100) | 如"沿用旧值" / "跳过此条" / "拒收 Override" | 沿用旧值 |
| ReviewStatus | 复核状态 | NVARCHAR(20) | CHECK：PENDING / CONFIRMED / IGNORED / FIXED | PENDING |
| ReviewedBy | 复核人 | NVARCHAR(100) | 业务复核工号 | U005 |
| ReviewedAt | 复核时间 | DATETIME2 | 复核完成时间 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 登记时间 | 2026-04-29 02:30:00 |

**索引**：
- `IX_MSDeptContext_Issues_Batch` ON `(BatchNo)`
- `IX_MSDeptContext_Issues_Material` ON `(MaterialCode, StageCode)`
- `IX_MSDeptContext_Issues_Pending` ON `(ReviewStatus, Severity) WHERE ReviewStatus='PENDING'`

---

## 四、计划版本与订单表

### 4.1 Order_Canonical（订单标准化表）⭐ v2.5新增

**所属库**：APS_Production  
**业务用途**：存储从ERP同步的原始订单数据，作为防腐层核心表

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| OrderNo | 订单号 | NVARCHAR(50) | 唯一标识（业务主键） | SO-20260305-001 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 业务主键 | MAT-10001 |
| BOMNO | BOM编号 | NVARCHAR(50) | v5.0.21改可空；NULL=待5号位Workset阶段解析BOM入口 | BOM-10001 |
| **SourceModel** | **ERP原始型号** | **NVARCHAR(100)** | **⚠️ v5.0.27新增；透传自ERP_Order_Staging.Model；BOM入口解析辅助追溯；不替代MaterialCode** | C25ILB-005 |
| Quantity | 订单数量 | DECIMAL(18,6) | 订单数量 | 1000 |
| DueDate | 交期/计划日期 | DATETIME2 | 客户要求交期 | 2026-03-20 00:00:00 |
| Status | 状态 | NVARCHAR(20) | 当前业务値只有三种：**OPEN**（活跃，未关闭未取消）/ **CLOSED**（已关闭）/ **CANCELLED**（已取消）；APS 准入条件 = `WHERE Status = 'OPEN'`（v3.12 窄口径）；只有 OPEN 状态的订单/生产指示进入活跃根集合、BOM Request、Order 分区表 | OPEN |
| OrderType | 订单类型 | NVARCHAR(20) | `SALES_ORDER`（客户订单，原SO/MTO）/ `PRODUCTION_INSTRUCTION`（生产指示，原MTS/SS/SS_U）（v5.0.24重分类） | SALES_ORDER |
| Priority | 优先级 | INT | 1-100 | 100 |
| CustomerCode | 客户编码 | NVARCHAR(50) | SO订单的客户编码 | CUST-001 |
| SourceSystem | 来源系统 | NVARCHAR(50) | 固定值：ERP | ERP |
| SourceOrderId | 源系统订单ID | NVARCHAR(100) | ERP中的订单主键 | 123456 |
| SourceMasterID | ERP的masterID | INT | ERP物料主数据ID | 100001 |
| FactoryCode | APS标准化工厂编码 | NVARCHAR(50) | 供Order装载时查Factory表（⚠️ 2026-04-09业务澄清：ERP原字段需规则转换） | F001 |
| UOM | 计量单位 | NVARCHAR(20) | PCS、KG、M等（2026-04-03审计补充） | PCS |
| TransportMode | 运输方式 | NVARCHAR(20) | 海运/空运/陆运，源事实字段（2026-04-09 v4.5新增） | 海运 |
| CustomerName | 客户名称 | NVARCHAR(200) | ERP订单表直接提供（2026-04-09 v4.5新增） | 某某客户株式会社 |
| MTS_InstructionNo | 生产指示号 | NVARCHAR(50) | 来源于ERP生产指示表InstructionNo（≠OrderNo）（2026-04-09 v4.5新增） | PI-20260305-001 |
| IssueDate | 订单发行日期 | DATE | 订单/生产指示正式下发日期（2026-04-09 v4.6新增） | 2026-03-01 |
| OriginalDueDate | 原始纳期 | DATE | 客户最初要求交期，MTS时=DueDate（2026-04-09 v4.6新增） | 2026-03-15 |
| ReceivedQty | 已入库数量 | DECIMAL(18,4) | 仅MTS：累计入库数量，SO为NULL（2026-04-09 v4.6新增） | 500.0000 |
| CustomerSegment | 客户区分 | NVARCHAR(50) | `JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER`；由 `sp_ValidateAndPromoteOrders` 通过 CustomerCode 查 `CustomerCodeMap` 本地映射表得到（v5.0.24澄清） | JAPAN |
| SalesOrderCategory | 销售类别 | NVARCHAR(50) | DIRECT_SALES/SALES_REPLENISHMENT，APS衍生字段（2026-04-09 v4.5新增） | DIRECT_SALES |
| DemandMaturityStatus | 需求成熟度 | NVARCHAR(50) | `PRE_CONFIRMED`（事前确认）/ `FORECAST`（预测SHIKOMI）；v5.0.24收窄，DELAYED已拆出为独立字段 DelayStatus（2026-04-09 v4.5新增） | PRE_CONFIRMED |
| CustomerTier | 客户分级 | NVARCHAR(20) | `VIP > KEY_ACCOUNT > STANDARD > GENERAL`；当前主要启用 VIP/GENERAL 两档，KEY_ACCOUNT/STANDARD 预留；默认GENERAL（v5.0.24补充等级关系） | GENERAL |
| **DelayStatus** | **延迟状态** | **NVARCHAR(20)** | **v5.0.24新增；`ON_TIME` / `FIRST_DELAY` / `REPEATED_DELAY`；独立维度，禁止与DemandMaturityStatus混用** | ON_TIME |
| **NonStockShipmentType** | **非在库出荷区分** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；APS标准化值：`FULL_PURPLE_SLIP`（全额紫票）/ `DIFF_PURPLE_SLIP`（差额紫票）/ `UNKNOWN`（ERP有值但无法识别）/ `NULL`（ERP未提供）；来源：RawNonStockShipmentType映射** | FULL_PURPLE_SLIP |
| **OriginalOrderSource** | **订单原始来源** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；APS标准化值：`DAT` / `PO` / `UNKNOWN`（ERP有值但无法识别）/ `NULL`（ERP未提供）；来源：RawOrderSource映射** | DAT |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 01:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 02:00:00 |

**Upsert键**（2026-04-03 订单链路审计补充）：`SourceSystem + SourceOrderId`
- `sp_ValidateAndPromoteOrders` 使用此组合键判断是插入新订单还是更新已有订单
- `OrderNo` 虽然有UNIQUE约束，但Upsert逻辑以源系统标识为准

**Status 状态枚举与流转**（2026-04-03 订单链路审计补充；v3.12 口径锁定）：

当前 APS V1 `Order_Canonical.Status` 只承接三种业务値：

| 状态 | 含义 | 是否进入 APS 排程 |
|------|------|------|
| **OPEN** | 活跃（未关闭、未取消） | ✅ 进入活跃根集合、BOM Request、Order 分区表 |
| **CLOSED** | 已关闭（终态） | ❌ 不进入 BOM Request，不生成 Task/Pegging |
| **CANCELLED** | 已取消（终态） | ❌ 不进入 BOM Request，不生成 Task/Pegging |

```
OPEN → CLOSED（ERP关闭）
OPEN → CANCELLED（ERP取消或APS检测源端消失）
```

> **活跃根集合口径（v3.12 窄口径）**：`WHERE Order_Canonical.Status = 'OPEN'`；只有 OPEN 状态的订单/生产指示可推送 BOM Request 并生成 Task/Pegging。CLOSED / CANCELLED 不进入 BOM Request。

> ⚠️ `RELEASED` / `SCHEDULED` / `COMPLETED` **不是当前 APS V1 的 Order_Canonical 业务枚举**，不得出现在当前正文、SQL 或字段说明的当前准入判断中（历史 changelog 保留）。

**业务用途**：
1. 订单标准化存储：统一存储来自ERP的销售订单(SO/MTO)和生产指示(MTS)
2. 防腐层核心表：作为ERP和APS本地库之间的桥梁
3. 活跃根集合划定：每天00:00从此表筛选 `Status = 'OPEN'` 的活跃订单/生产指示（v3.12 窄口径）；按订单粒度划定，不再按 BOMNO 去重；读取下游：`sp_GetActiveRootOrders` / BOM Request 写入 / `sp_SyncOrdersToPartitionTable`
4. 数据源：为Order分区表提供标准化的订单数据（`sp_SyncOrdersToPartitionTable`）

**数据流向**（2026-04-03 订单链路审计补充）：
- **写入**：由 `sp_ValidateAndPromoteOrders` 从 `ERP_Order_Staging` 校验通过后Upsert
- **读取（下游1）**：每天00:00 2号位从此表划定活跃订单/生产指示根集合（`WHERE Status = 'OPEN'`，v3.12 窄口径）
- **读取（下游2）**：每天00:05 `sp_SyncOrdersToPartitionTable` 装载到Order分区表

---

### 4.2 PlanVersion（计划版本表）

**所属库**：APS_Production  
**业务用途**：记录每次排程的版本信息（支持多版本对比和回滚）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 版本ID | INT | 主键，自增 | 1 |
| VersionCode | 版本编号 | NVARCHAR(50) | 唯一标识 | 20260305_020000 |
| VersionCategory | 版本类别 | NVARCHAR(50) | DAILY_BASELINE / RESCHEDULE_CANDIDATE / SIMULATION_CANDIDATE / WHATIF_CANDIDATE（v5.0.44 rename from VersionType） | DAILY_BASELINE |
| SourceScheduleRunId | 来源排程运行ID | INT NOT NULL | FK→ScheduleRun.Id；这套版本由哪次运行产生 | 1 |
| SourceSimulationRunId | 来源仿真运行ID | INT NULL | 逻辑引用 SimulationRun.Id；物理 FK 阶段二仿真实装后补建 | NULL |
| DomainKey | 分域标识 | NVARCHAR(100) | 产品族_工厂 | X1_F001 |
| PlanHorizonStart | 计划开始日期 | DATE | 排程时间范围起始 | 2026-03-05 |
| PlanHorizonEnd | 计划结束日期 | DATE | 排程时间范围结束 | 2026-04-05 |
| ComputeMode | 计算模式 | NVARCHAR(50) | FULL_DETAIL/CRITICAL_PATH/ROUGH_CUT/DOMAIN_SPLIT | DOMAIN_SPLIT |
| Status | 版本生命周期状态 | NVARCHAR(50) | BUILDING / CANDIDATE / ACTIVE / ARCHIVED / FAILED（v5.0.44：版本生命周期，非运行状态；正式采用=ACTIVE） | ACTIVE |
| ActivatedAt | 正式采用时间 | DATETIME2 NULL | Status 变为 ACTIVE 时记录（v5.0.44新增） | 2026-03-05 02:12:30 |
| ActivatedBy | 正式采用人/来源 | NVARCHAR(100) NULL | 激活来源（v5.0.44新增） | SYSTEM |
| TotalOrders | 订单总数 | INT | 本次排程处理的订单数 | 5000 |
| TotalTasks | 任务总数 | INT | 本次排程生成的任务数 | 50000 |
| BatchNo | BOM批次号 | NVARCHAR(50) | 关联BOM展开批次 | BOM-20260305 |
| SnapshotFilePath | 快照文件路径 | NVARCHAR(500) | 快照封存文件路径 | /snapshots/20260305.bin |
| SnapshotFileSize | 快照文件大小 | BIGINT | 快照文件大小（字节） | 1048576 |
| SnapshotFileHash | 快照文件哈希 | NVARCHAR(64) | SHA256哈希值 | a1b2c3... |
| SnapshotCompressedSize | 压缩后大小 | BIGINT | 压缩后大小（字节） | 524288 |
| SnapshotCreatedAt | 快照创建时间 | DATETIME2 | 快照封存时间 | 2026-03-05 02:15:00 |
| CreatedByUserId | 创建用户ID | INT | 发起排程的用户ID（关联APS_Auth.User表） | 5 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:00:00 |
| ArchivedAt | 归档时间 | DATETIME2 | 版本归档时间，NULL表示未归档 | NULL |

**历史兼容字段**（不作为权威来源，权威运行状态已归 ScheduleRun）：
| StartedAt | DATETIME2 | 排程开始时间（历史兼容） |
| CompletedAt | DATETIME2 | 排程完成时间（历史兼容） |
| DurationSeconds | INT | 耗时秒数（可由 ScheduleRun 计算） |
| ErrorMessage | NVARCHAR(MAX) | 错误信息（权威归于 ScheduleRun.ErrorMessage） |

> **v5.0.44 架构关系说明（2026-06-23）**：`PlanVersion` 代表"每套结果版本"，与 `ScheduleRun` 分离。`PlanVersion.SourceScheduleRunId` 反向追溯到运行记录。`VersionCategory` 标识版本类别。`Status` 为版本生命周期状态：`BUILDING`→`CANDIDATE`或`ACTIVE`，正式采用直接看 `Status = ACTIVE`。`SourceSimulationRunId` 仅仿真版本填写。

---

### 4.3 Order（订单表 - 分区表）

**所属库**：APS_Production  
**业务用途**：记录所有订单（销售订单、MTS备货单、安全库存单），从Order_Canonical同步

**技术要点（数据流向）**：
- **数据来源**：从防腐暂存表 `Order_Canonical` 洗净后同步
- **同步逻辑**：由APS订单装载过程按业务规则转换生成（例如执行 `MaterialCode → MaterialId` 映射、产品族/工厂归属判定、`DomainKey` 计算），并落入对应的物理分区
- **负责人**：2号位（主责开发装载逻辑）；0号位（提供活跃根划定与订单类型的业务口径）
- **同步时机**：每天00:05（触发后续ODS库BOM递归展开的第一块多米诺骨牌）。白天若有增量插单，先入 `Order_Canonical` 暂存

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 订单ID | BIGINT | 主键（复合主键之一），自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 分区键，复合主键之一（2026-04-03审计补充） | 1 |
| OrderNo | 订单号 | NVARCHAR(50) | 唯一标识 | SO-20260305-001 |
| OrderType | 订单类型 | NVARCHAR(20) | `SALES_ORDER`（客户订单）/ `PRODUCTION_INSTRUCTION`（生产指示）（v5.0.24重分类） | SALES_ORDER |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| ProductFamilyId | 产品族ID | INT | 外键关联ProductFamily表 | 1 |
| FactoryId | 工厂ID | INT | 外键关联Factory表 | 1 |
| Quantity | 数量 | DECIMAL(18,4) | 订单数量 | 1000 |
| UOM | 计量单位 | NVARCHAR(20) | PCS/KG/M等 | PCS |
| CustomerDueDate | 客户要求交期 | DATE | 客户要求的交货日期 | 2026-03-20 |
| PromisedDate | 承诺交期 | DATE | APS承诺的交货日期 | 2026-03-18 |
| Priority | 基础优先级 | INT | 1-100 | 80 |
| PriorityScore | 综合优先级分数 | DECIMAL(10,2) | 计算的综合分数 | 950.5 |
| Status | 订单状态 | NVARCHAR(50) | NEW/PLANNED/CONFIRMED/IN_PROGRESS/COMPLETED/CANCELLED | PLANNED |
| DomainKey | 分域标识 | NVARCHAR(100) | 产品族_工厂 | X1_F001 |
| SourceSystem | 来源系统 | NVARCHAR(50) | ERP/MES/MANUAL | ERP |
| SourceOrderId | 源系统订单ID | NVARCHAR(100) | ERP中的订单主键（2026-04-03审计补充） | 123456 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 冗余字段，避免频繁JOIN | MAT-10001 |
| BOMNO | BOM编号 | NVARCHAR(50) | 关联的BOMNO | BOM-10001 |
| SourceMasterID | ERP的masterID | INT | ERP物料主数据ID | 100001 |
| MTS_InstructionNo | MTS生产指示号 | NVARCHAR(50) | 来源于Canonical真实值（⚠️ 2026-04-09修正：≠OrderNo，来源于ERP生产指示表InstructionNo） | PI-20260305-001 |
| TransportMode | 运输方式 | NVARCHAR(20) | 海运/空运/陆运，从Canonical透传（2026-04-09 v4.5新增） | 海运 |
| CustomerName | 客户名称 | NVARCHAR(200) | 从Canonical透传（2026-04-09 v4.5新增） | 某某客户株式会社 |
| CustomerSegment | 客户区分 | NVARCHAR(50) | `JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER/UNKNOWN/NULL`；从Canonical透传（v5.0.24值域更新） | JAPAN |
| SalesOrderCategory | 销售类别 | NVARCHAR(50) | DIRECT_SALES/SALES_REPLENISHMENT，APS衍生字段（2026-04-09 v4.5新增） | DIRECT_SALES |
| DemandMaturityStatus | 需求成熟度 | NVARCHAR(50) | `PRE_CONFIRMED` / `FORECAST`；v5.0.24收窄，DELAYED已拆出为DelayStatus（2026-04-09 v4.5新增） | PRE_CONFIRMED |
| CustomerTier | 客户分级 | NVARCHAR(20) | `VIP > KEY_ACCOUNT > STANDARD > GENERAL`；当前主要启用VIP/GENERAL两档；从Canonical透传（v5.0.24补充等级关系） | GENERAL |
| **DelayStatus** | **延迟状态** | **NVARCHAR(20)** | **v5.0.24新增；从Canonical透传；`ON_TIME` / `FIRST_DELAY` / `REPEATED_DELAY`；禁止与DemandMaturityStatus混用** | ON_TIME |
| IssueDate | 订单发行日期 | DATE | 从Canonical透传（2026-04-09 v4.6新增） | 2026-03-01 |
| OriginalDueDate | 原始纳期 | DATE | 客户最初要求交期，从Canonical透传（2026-04-09 v4.6新增） | 2026-03-15 |
| ReceivedQty | 已入库数量 | DECIMAL(18,4) | 仅MTS：累计入库数量，从Canonical透传（2026-04-09 v4.6新增） | 500.0000 |
| **SourceModel** | **ERP原始型号** | **NVARCHAR(100)** | **⚠️ v5.0.27新增；从Canonical透传；ERP原始型号，供排程规则参考（V1进入快照）** | X100 |
| **NonStockShipmentType** | **非在库出荷区分** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；从Canonical透传；`FULL_PURPLE_SLIP`/`DIFF_PURPLE_SLIP`/`UNKNOWN`/NULL** | FULL_PURPLE_SLIP |
| **OriginalOrderSource** | **订单原始来源** | **NVARCHAR(50)** | **⚠️ v5.0.27新增；从Canonical透传；`DAT`/`PO`/`UNKNOWN`/NULL** | DAT |
| **OrderCanonicalId** | **ODS订单关联ID** | **BIGINT NULL** | **v5.0.34新增；来源 Order_Canonical.Id；允许 NULL 兼容历史数据；新版本 sp_SyncOrdersToPartitionTable 必须写入；用于按 PlanVersionId+OrderCanonicalId 生成 OrderBomRequestLink** | 1001 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 01:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 02:00:00 |

**分区方式**：按 `PlanVersionId` 分区（与Task/Pegging一致）（2026-04-03审计补充）

---

### 4.4 Task（任务表 - 分区表）

**所属库**：APS_Production  
**业务用途**：记录排程生成的所有加工任务（分区表，按PlanVersionId分区）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 任务ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联PlanVersion表 | 1 |
| TaskNo | 任务编号 | NVARCHAR(50) | 唯一标识 | TASK-20260305-000001 |
| OrderId | 订单ID | BIGINT | 外键关联Order表 | 100 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 200 |
| OperationSeq | 工序序号 | INT | 1-20 | 10 |
| OperationCode | 工序代码 | NVARCHAR(50) | 工序标识 | OP-010 |
| ResourceId | 资源ID | INT | 外键关联Resource表 | 5 |
| ResourceGroupId | 资源组ID | INT | ⚠️ v5.0废弃，保留仅为兼容（2026-04-03审计补充） | NULL |
| Quantity | 数量 | DECIMAL(18,4) | 任务数量 | 500 |
| UOM | 计量单位 | NVARCHAR(20) | PCS/KG/M等（2026-04-03审计补充） | PCS |
| PlannedStartTime | 计划开始时间 | DATETIME2 | APS排程的开始时间 | 2026-03-06 08:00:00 |
| PlannedEndTime | 计划结束时间 | DATETIME2 | APS排程的结束时间 | 2026-03-06 10:30:00 |
| Duration | 工时 | DECIMAL(18,4) | 单位：小时 | 2.5 |
| Status | 任务状态 | NVARCHAR(50) | PLANNED/RELEASED/IN_PROGRESS/COMPLETED/CANCELLED | PLANNED |
| IsLocked | 是否在冻结区 | BIT | 1=冻结区（不可修改） | 1 |
| IsCriticalPath | 是否关键路径 | BIT | 1=关键路径 | 1 |
| TaskType | 任务类型 | NVARCHAR(50) | PRODUCTION/TRANSFER/PROCUREMENT | PRODUCTION |
| MTS_InstructionNo | 生产指示号 | NVARCHAR(50) | 从Order冗余，避免Task反查Order（2026-04-09 v4.5新增） | PI-20260305-001 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:05:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-06 10:40:00 |

---

### 4.5 ScheduleRun（排程运行编排表）（v5.0.44 四表职责收敛 2026-06-18）

**所属库**：APS_Production  
**定位**：记录这次运行怎么跑。与 PlanVersion（结果版本）分离；产出版本通过 `PlanVersion.SourceScheduleRunId` 反查。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例値 |
|-----------|---------|---------|---------|--------|
| Id | 运行ID | INT | 主键，自增 | 1 |
| RunType | 运行类型 | NVARCHAR(50) | `FULL_SCHEDULE` / `MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` / `SIMULATION` / `INSERT_ORDER_WHATIF` | FULL_SCHEDULE |
| Status | 运行状态 | NVARCHAR(20) | `RUNNING` / `COMPLETED` / `FAILED`（运行状态，非版本状态） | COMPLETED |
| ScenarioId | 关联场景ID | INT NULL | FK→Scenario；仅 SIMULATION/INSERT_ORDER_WHATIF 类型填充 | NULL |
| BasePlanVersionId | 基准版本ID | INT NULL | FK→PlanVersion；重排/仿真基于的已有版本；凌晨全量为 NULL | NULL |
| TriggeredBy | 触发来源 | NVARCHAR(100) | 'Hangfire' / UserId / 'API' / 'Agent' | Hangfire |
| DataCutoffTime | 数据截止时间 | DATETIME2 NOT NULL | 本次运行统一数据切片边界；00:38前确定 | 2026-06-18 00:00:00 |
| ScopeJson | 重排范围JSON | NVARCHAR(MAX) NULL | 局部重排时记录范围 | NULL |
| StrategyProfileVersionId | 策略包版本ID | BIGINT NULL | FK→StrategyProfileVersion(Id)；本次运行采用的策略包版本；V1可NULL兼容历史，新运行强制写入（v5.0.45新增） | 1 |
| StartedAt | 运行开始时间 | DATETIME2 | 默认 GETDATE() | 2026-06-18 02:00:00 |
| CompletedAt | 运行完成时间 | DATETIME2 NULL | 2号位回填 | 2026-06-18 02:12:30 |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) NULL | 失败时记录 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 默认 GETDATE() | 2026-06-18 01:38:00 |

**架构说明**：
- 00:38 前预创建（Status=RUNNING），确定 ScheduleRunId+DataCutoffTime；00:40/45/50 三个 MES 快照 SP 使用同值
- 02:00 排程启动时读取已创建记录，创建 PlanVersion（SourceScheduleRunId=当前Id，Status=BUILDING）
- 删除了 `OutputPlanVersionId`（v5.0.44）；产出版本通过 PlanVersion.SourceScheduleRunId 反查

---

### 4.6 Scenario（仿真场景表）（v5.0.44 2026-06-18）

**所属库**：APS_Production  
**定位**：记录一个业务试算场景的假设、目标和最终选中版本。不是运行表，不是结果版本表。阶段一建骨架，不写入数据。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 场景ID | INT | 主键，自增 | 1 |
| ScenarioName | 场景名称 | NVARCHAR(200) | 业务可读名称（v5.0.44 rename from Name） | 插单影响评估 |
| Description | 场景描述 | NVARCHAR(MAX) NULL | 自由文本 | NULL |
| ScenarioType | 场景类型 | NVARCHAR(50) | `SIMULATION` / `INSERT_ORDER_WHATIF`（v5.0.44 rename from RunType） | SIMULATION |
| Status | 场景状态 | NVARCHAR(20) | `DRAFT`/`RUNNING`/`COMPLETED`/`SELECTED`/`SUBMITTED`；默认 DRAFT | DRAFT |
| AssumptionJson | 场景假设 | NVARCHAR(MAX) NULL | JSON格式假设条件 | NULL |
| ObjectiveJson | 优化目标 | NVARCHAR(MAX) NULL | JSON格式优化目标（v5.0.44新增） | NULL |
| SelectedPlanVersionId | 选中版本ID | INT NULL | FK→PlanVersion；候选版本生成后回填；可空 | NULL |
| UpdatedAt | 更新时间 | DATETIME2 NULL | 场景修改时间 | NULL |
| CreatedByUserId | 创建用户ID | INT NULL | 发起场景的用户 | 5 |
| CreatedAt | 创建时间 | DATETIME2 | 默认 GETDATE() | 2026-06-18 10:00:00 |

---

### 4.7 SimulationRun（仿真算法运行表）（v5.0.44 2026-06-18）

**所属库**：APS_Production  
**定位**：记录某个场景下的一次具体算法执行。一次执行可产生多个候选 PlanVersion。删除了 `PlanVersionId`（v5.0.44）；结果版本通过 `PlanVersion.SourceSimulationRunId` 反查。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 仿真运行ID | INT | 主键，自增 | 1 |
| ScenarioId | 关联场景ID | INT | FK→Scenario | 1 |
| ScheduleRunId | 关联排程运行ID | INT NULL | FK→ScheduleRun | 5 |
| AlgorithmType | 算法类型 | NVARCHAR(50) NULL | `RULE_HEURISTIC`/`GENETIC`/`SIMULATED_ANNEALING`/`HYBRID`（v5.0.44新增） | GENETIC |
| AlgorithmVersion | 算法版本 | NVARCHAR(50) NULL | 阶段二填充 | v2.0.0 |
| AlgorithmConfigJson | 算法参数 | NVARCHAR(MAX) NULL | JSON格式参数（v5.0.44新增） | NULL |
| Status | 仿真运行状态 | NVARCHAR(20) | `RUNNING`/`COMPLETED`/`FAILED`；默认 RUNNING（v5.0.44新增） | RUNNING |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) NULL | 失败时记录（v5.0.44新增） | NULL |
| StartedAt | 开始时间 | DATETIME2 NULL | 算法开始时间 | NULL |
| CompletedAt | 完成时间 | DATETIME2 NULL | 算法完成时间 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 默认 GETDATE() | NULL |

---

### 4.8 ScenarioObjectiveScore（场景多目标评分表）（2026-05-13 v5.0.25 阶段二预留骨架）

**所属库**：APS_Production  
**业务用途**：记录一个 `Scenario` 内各优化目标维度的评分；供多方案比较时直接读取，**不重新扫 Task 明细**；评分数据来源于 `PlanKpiSummary`（§八.4）的聚合计算；**阶段一建骨架表**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 评分ID | INT | 主键，自增 | 1 |
| ScenarioId | 关联场景ID | INT | 外键关联 Scenario 表 | 1 |
| PlanVersionId | 关联版本ID | INT | 外键关联 PlanVersion 表 | 2 |
| ObjectiveName | 目标名称 | NVARCHAR(100) | 优化目标标识，阶段二定义标准值域（如 `ON_TIME_RATE` / `MAX_DELAY_HOURS` 等） | ON_TIME_RATE |
| Score | 评分值 | DECIMAL(10,4) | 对应目标维度的量化值 | 0.9250 |
| NormalizedScore | 归一化评分 | DECIMAL(7,4) | 0−1 归一化后分值；用于多目标比较；阶段二计算 | 0.9250 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间；默认 GETDATE() | 2026-03-05 10:15:00 |

---

---

## 五、规则与参数引擎（v5.0.45 新增 2026-06-23）

**定位**：APS/LAPS 业务策略中枢。把订单优先级、库存可用、管道供给、Firm冻结、合批拆批、资源选择、换型、插单重排等业务策略，统一转化为排程引擎可消费的规则、参数、权重、约束和候选集。

**设计原则**：
- 业务人员按主题维护规则和参数；
- 系统内部统一进行版本管理、发布、策略包组合、运行绑定和追溯；
- 5号位插件只负责执行规则，不负责维护规则；
- 1号位排程引擎只消费解析后的规则结果和参数结果；
- 3号位负责规则参数引擎后端、API、发布、策略包和运行绑定；
- 4号位负责规则参数维护前端和解释展示；
- 2号位负责排程链路中的数据装载、落库和状态回填配合。

**V1 不做**：万能脚本式规则引擎 / RuleCondition/RuleAction/RuleExpression / ScheduleConfigSnapshot / 完整审批流闭环（仅保留审批/发布字段为后续预留）。

**与现有主题规则表的关系**：`InventoryAvailabilityRule` / `SupplyAvailabilityRule` / `TaskSplitRuleConfig` / `StageLeadTimeParam` / `RoutingPlanningParam` 等仍保留各自主题语义。规则参数引擎负责版本、发布、组合、运行绑定和追溯；主题规则表负责业务字段。

---

### 5.0 ScheduleRun.StrategyProfileVersionId（2026-06-23 新增）

在 §4.5 ScheduleRun 字段表中追加：

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| StrategyProfileVersionId | 策略包版本ID | BIGINT NULL | FK→StrategyProfileVersion(Id)；本次运行采用的策略包版本；V1可NULL兼容历史数据，新运行应用层强制写入 |

规则参数是运行输入，不是 PlanVersion 输出。StrategyProfileVersionId 必须在创建 ScheduleRun 时（00:38）同步写入。

---

### 5.1 RuleSet（规则集主表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| Id | 规则集ID | BIGINT | 主键，自增 |
| RuleSetCode | 规则集编码 | NVARCHAR(50) | 唯一标识 |
| RuleSetName | 规则集名称 | NVARCHAR(200) | 如"标准全量排程规则集""插单影响分析规则集" |
| Description | 描述 | NVARCHAR(1000) | 可空 |
| IsActive | 是否启用 | BIT | 默认1 |
| CreatedAt | 创建时间 | DATETIME2 | 默认GETDATE() |
| CreatedBy | 创建人 | NVARCHAR(100) | |
| UpdatedAt | 更新时间 | DATETIME2 | |
| UpdatedBy | 更新人 | NVARCHAR(100) | |

---

### 5.2 RuleSetVersion（规则集版本表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| Id | 版本ID | BIGINT | 主键，自增 |
| RuleSetId | 规则集ID | BIGINT | FK→RuleSet(Id) |
| VersionCode | 版本编号 | NVARCHAR(50) | 同一规则集内唯一 |
| Status | 版本状态 | NVARCHAR(20) | DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED |
| EffectiveFrom | 生效起始 | DATETIME2 | |
| EffectiveTo | 生效截止 | DATETIME2 | |
| PublishedAt | 发布时间 | DATETIME2 | |
| PublishedBy | 发布人 | NVARCHAR(100) | |
| ApprovedAt | 审批时间 | DATETIME2 | |
| ApprovedBy | 审批人 | NVARCHAR(100) | |
| CreatedAt | 创建时间 | DATETIME2 | |
| CreatedBy | 创建人 | NVARCHAR(100) | |

红线：已发布版本不可原地修改；正式排程只允许 PUBLISHED 状态。

---

### 5.3 ParameterSet（参数集主表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| Id | 参数集ID | BIGINT | 主键，自增 |
| ParameterSetCode | 参数集编码 | NVARCHAR(50) | 唯一标识 |
| ParameterSetName | 参数集名称 | NVARCHAR(200) | |
| Description | 描述 | NVARCHAR(1000) | |
| IsActive | 是否启用 | BIT | 默认1 |
| CreatedAt | 创建时间 | DATETIME2 | |
| CreatedBy | 创建人 | NVARCHAR(100) | |
| UpdatedAt | 更新时间 | DATETIME2 | |
| UpdatedBy | 更新人 | NVARCHAR(100) | |

---

### 5.4 ParameterSetVersion（参数集版本表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| Id | 版本ID | BIGINT | 主键，自增 |
| ParameterSetId | 参数集ID | BIGINT | FK→ParameterSet(Id) |
| VersionCode | 版本编号 | NVARCHAR(50) | 同一参数集内唯一 |
| Status | 版本状态 | NVARCHAR(20) | DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED |
| EffectiveFrom | 生效起始 | DATETIME2 | |
| EffectiveTo | 生效截止 | DATETIME2 | |
| PublishedAt | 发布时间 | DATETIME2 | |
| PublishedBy | 发布人 | NVARCHAR(100) | |
| ApprovedAt | 审批时间 | DATETIME2 | |
| ApprovedBy | 审批人 | NVARCHAR(100) | |
| CreatedAt | 创建时间 | DATETIME2 | |
| CreatedBy | 创建人 | NVARCHAR(100) | |

红线：已发布版本不可原地修改；正式排程只允许 PUBLISHED 状态。

---

### 5.5 StrategyProfile（策略包主表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| Id | 策略包ID | BIGINT | 主键，自增 |
| StrategyProfileCode | 策略包编码 | NVARCHAR(50) | 唯一标识 |
| StrategyProfileName | 策略包名称 | NVARCHAR(200) | 如"凌晨全量标准策略""急单插单分析策略" |
| Description | 描述 | NVARCHAR(1000) | |
| RunType | 适用运行类型 | NVARCHAR(50) | FULL_SCHEDULE/MANUAL_RESCHEDULE/LOCAL_RESCHEDULE/SIMULATION/INSERT_ORDER_WHATIF；可空=适用所有 |
| IsActive | 是否启用 | BIT | 默认1 |
| CreatedAt | 创建时间 | DATETIME2 | |
| CreatedBy | 创建人 | NVARCHAR(100) | |
| UpdatedAt | 更新时间 | DATETIME2 | |
| UpdatedBy | 更新人 | NVARCHAR(100) | |

---

### 5.6 StrategyProfileVersion（策略包版本表）

**关键表**：将规则集版本和参数集版本组合为可发布、可追溯、可被 ScheduleRun 引用的策略包版本。

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 |
|-----------|---------|---------|---------|
| Id | 版本ID | BIGINT | 主键，自增 |
| StrategyProfileId | 策略包ID | BIGINT | FK→StrategyProfile(Id) |
| VersionCode | 版本编号 | NVARCHAR(50) | 同一策略包内唯一 |
| RuleSetVersionId | 规则集版本ID | BIGINT | FK→RuleSetVersion(Id) |
| ParameterSetVersionId | 参数集版本ID | BIGINT | FK→ParameterSetVersion(Id) |
| Status | 版本状态 | NVARCHAR(20) | DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED |
| EffectiveFrom | 生效起始 | DATETIME2 | |
| EffectiveTo | 生效截止 | DATETIME2 | |
| IsDefault | 是否默认 | BIT | 同一策略包下仅一个 PUBLISHED 版本 IsDefault=1 |
| PublishedAt | 发布时间 | DATETIME2 | |
| PublishedBy | 发布人 | NVARCHAR(100) | |
| ApprovedAt | 审批时间 | DATETIME2 | |
| ApprovedBy | 审批人 | NVARCHAR(100) | |
| CreatedAt | 创建时间 | DATETIME2 | |
| CreatedBy | 创建人 | NVARCHAR(100) | |

红线：已发布版本不可原地修改。正式运行只能选择 PUBLISHED 且在有效期内的版本。

---

### 5.7 职责分工

| 号位 | 职责 |
|------|------|
| 0号位 | 规则口径、枚举值、业务红线、审批口径确认 |
| 3号位 | 规则参数引擎后端/API、发布、策略包绑定 ScheduleRun、版本激活接口衔接 |
| 4号位 | 规则参数维护界面、策略包选择界面、仿真参数界面、规则命中解释展示 |
| 5号位 | 规则执行插件，只消费规则和参数，不维护。执行判断/打分/裁决/Voucher/Decision/Score 生成 |
| 2号位 | 排程链路数据装载、PlanVersion创建、Task/Pegging落库、ScheduleRun/PlanVersion状态回填 |
| 1号位 | 排程算法消费方，只消费 ScheduleContext 中已装载的规则结果和参数结果。禁止直接读取规则参数维护表 |

---

## 六、Pegging与追溯表

---

### BOM 补充：MES_APS_BOM_Workset_CrossFactoryEdge（ODS 跨厂边表 - v5.0.46）

**所属层**：ODS 层 / **所属库**：MES_Integration / **维护责任人**：5号位
基于 StageDetail 按 StageSeq 排序生成（`LEAD`窗口函数），只记录 `FromFactoryCode <> ToFactoryCode` 的跨厂段。该表只表示结构事实，不判断跨厂模式。

**生成规则**：`FromFactoryCode/ToFactoryCode` 必须通过 `StageCode→StageDict.StageCode→StageDict.FactoryCode` 取得，禁止截取 StageCode 前缀。StageCode 未命中 StageDict 时不生成该边，登记 Issues。详见 `sp_GenerateBOMCrossFactoryEdge`。

| 字段 | 中文 | 类型 | 说明 |
|------|------|------|------|
| Id | 主键 | BIGINT | 自增 |
| BatchNo | 批次号 | NVARCHAR(50) | BOM展开批次 |
| WorksetId | Workset行ID | BIGINT | FK→MES_APS_BOM_Workset |
| BOMNO | BOM号 | NVARCHAR(50) | 追溯 |
| ParentMaterialCode | 父件物料 | NVARCHAR(100) | 消耗方 |
| ChildMaterialCode | 子件物料 | NVARCHAR(100) | 被供给物料 |
| FromStageCode | 发出大工艺 | NVARCHAR(50) | |
| FromFactoryCode | 发出工厂 | NVARCHAR(50) | |
| ToStageCode | 接收大工艺 | NVARCHAR(50) | |
| ToProcessCode | 接收工序/仓库码 | NVARCHAR(50) NULL | |
| ToFactoryCode | 接收工厂 | NVARCHAR(50) | |
| CreatedAt | 创建时间 | DATETIME2 | GETDATE() |

### APS 缓存补充：APS_BOM_CROSS_FACTORY_EDGE_RAW（v5.0.46）

**所属层**：APS 层 / **所属库**：APS_Production / **维护责任人**：2号位
从 ODS 搬运到 APS 本地，供 Pegging 直接读取。

| 字段 | 中文 | 类型 | 说明 |
|------|------|------|------|
| Id | 主键 | BIGINT | 自增 |
| BatchNo | 批次号 | NVARCHAR(50) | |
| WorksetId | Workset行ID | BIGINT | |
| BOMNO | BOM号 | NVARCHAR(50) NULL | |
| ParentMaterialCode | 父件物料 | NVARCHAR(100) | |
| ChildMaterialCode | 子件物料 | NVARCHAR(100) | |
| FromStageCode | 发出大工艺 | NVARCHAR(50) | |
| FromFactoryCode | 发出工厂 | NVARCHAR(50) | |
| ToStageCode | 接收大工艺 | NVARCHAR(50) | |
| ToProcessCode | 接收工序/仓库码 | NVARCHAR(50) NULL | |
| ToFactoryCode | 接收工厂 | NVARCHAR(50) | |
| SyncedAt | 同步时间 | DATETIME2 | GETDATE() |

---

### Pegging 补充：PeggingSupplyAllocation（v5.0.46 新增）

**定位**：不是候选供给表，只记录已确认可用于当前需求的供给分配结果。不可用库存、未匹配出荷指示号的 ZP/BP 库存不进入本表。

| 字段 | 中文 | 类型 | 说明 |
|------|------|------|------|
| Id | 主键 | BIGINT | 自增 |
| PlanVersionId | 计划版本ID | INT NOT NULL | FK→PlanVersion.Id |
| ScheduleRunId | 排程运行ID | INT NOT NULL | FK→ScheduleRun.Id |
| BatchNo | 数据批次号 | NVARCHAR(50) | |
| RootOrderId | 根订单ID | BIGINT NULL | 顶层需求 |
| RootOrderNo | 根订单号 | NVARCHAR(100) | |
| CurrentOrderId | 当前单据ID | BIGINT NULL | 当前生产指示/厂间出荷指示 |
| CurrentOrderNo | 当前单据号 | NVARCHAR(100) | |
| OrderType | 订单类型 | NVARCHAR(50) NULL | SALES_ORDER/PRODUCTION_INSTRUCTION |
| WorksetId | Workset行ID | BIGINT NULL | BOM边 |
| MaterialId | 物料ID | INT NOT NULL | |
| MaterialCode | 物料编码 | NVARCHAR(100) | |
| DemandFactoryCode | 需求工厂 | NVARCHAR(50) NULL | |
| DemandStageCode | 需求阶段 | NVARCHAR(50) NULL | |
| DemandQty | 需求数量 | DECIMAL(18,4) | |
| AllocatedQty | 分配数量 | DECIMAL(18,4) | 本条供给覆盖数量 |
| SupplyType | 供给类型 | NVARCHAR(50) | INVENTORY/WIP/PIPELINE/INTER_FACTORY_ORDER/PRODUCTION_INSTRUCTION/PURCHASE_ORDER/NEW_REQUIREMENT |
| SupplyFactoryCode | 供给所在工厂 | NVARCHAR(50) NULL | |
| SupplyWarehouseCode | 供给仓库 | NVARCHAR(50) NULL | |
| ERPProperty | ERP仓库属性 | NVARCHAR(20) NULL | M/XC/ZP/BP（来自 ERP 真实属性） |
| AttachStageCode | 挂接大工艺 | NVARCHAR(50) NULL | |
| CompletedStageCode | 已完成大工艺 | NVARCHAR(50) NULL | |
| NextRequiredStageCode | 下一步大工艺 | NVARCHAR(50) NULL | |
| RemainingStagePathJson | 剩余大工艺路径 | NVARCHAR(MAX) NULL | |
| SupplyMode | 跨厂供给方式 | NVARCHAR(50) NULL | STAGE_HANDOFF/INTER_FACTORY_ORDER/PURCHASE_IN_TRANSIT/NULL |
| CrossFactoryEdgeId | 跨厂边ID | BIGINT NULL | FK→CrossFactoryEdge |
| TransportLeadTimeHours | 运输提前期 | INT NULL | |
| ETA | 预计到达时间 | DATETIME2 NULL | |
| KnownAvailableTime | 已知可用时间 | DATETIME2 NULL | 库存=当前，在途=ETA |
| SupplyDocumentType | 供给单据类型 | NVARCHAR(50) NULL | STOCK/SHIPPING_INSTRUCTION/PURCHASE_ORDER/PRODUCTION_INSTRUCTION/PIPELINE |
| SupplyDocumentNo | 供给单据号 | NVARCHAR(100) NULL | 真正匹配的业务单据号 |
| CreatedAt | 创建时间 | DATETIME2 | GETDATE() |

### Pegging 表定位澄清

物理 Pegging 表（§5.1）是 Task-to-Task 供需血缘表，记录 `UpstreamTaskId/DownstreamTaskId`。库存、ZP/BP 出口库、Received 汇总、管道在途等非 Task 供给写入 `PeggingSupplyAllocation`，不强行写入 Pegging 表。


### 5.1 Pegging（供需关系表 - 分区表）

**所属库**：APS_Production  
**业务用途**：记录任务之间的供需血缘关系（分区表，按PlanVersionId分区）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | Pegging ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联PlanVersion表 | 1 |
| UpstreamTaskId | 上游任务ID | BIGINT | 供给方Task | 100 |
| DownstreamTaskId | 下游任务ID | BIGINT | 需求方Task | 200 |
| UpstreamMaterialId | 上游物料ID | INT | 供给物料 | 50 |
| DownstreamMaterialId | 下游物料ID | INT | 需求物料 | 100 |
| Quantity | 数量 | DECIMAL(18,4) | Pegging数量 | 500 |
| UOM | 计量单位 | NVARCHAR(20) | PCS/KG/M等 | PCS |
| PeggingType | Pegging类型 | NVARCHAR(50) | BOM/ROUTING/TRANSFER | BOM |
| LeadTimeDays | 提前期天数 | INT | 物流提前期 | 2 |
| IsCrossDomain | 是否跨域 | BIT | 1=跨域，0=单域 | 0 |
| AllocatedQuantity | 分配数量 | DECIMAL(18,4) | 该Pegging分配的具体数量 | 500 |
| InheritedPriority | 继承的优先级 | INT | 如：900分来自VIP客户 | 900 |
| AllocationReason | 分配理由 | NVARCHAR(200) | 如："来自VIP客户"、"基础备货" | 来自VIP客户 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:05:00 |

---

### 5.2 ExplainTrace（可解释性追踪表 - 分区表）

**所属库**：APS_Production  
**业务用途**：记录排程决策的可解释性信息（分区表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 追踪ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联PlanVersion表 | 1 |
| TaskId | 任务ID | BIGINT | 外键关联Task表 | 100 |
| TraceType | 追踪类型 | NVARCHAR(50) | RESOURCE_SELECTION/TIME_CALCULATION/CONSTRAINT_VIOLATION | RESOURCE_SELECTION |
| TraceLevel | 追踪级别 | NVARCHAR(20) | INFO/WARNING/ERROR | INFO |
| Message | 追踪消息 | NVARCHAR(MAX) | 可读的决策说明 | 选择资源MC-001，因为负荷率最低（65%） |
| ContextData | 上下文数据 | NVARCHAR(MAX) | JSON格式的详细数据 | {"resourceId":5,"loadRate":0.65} |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:05:00 |

> **v5.0.25 职责边界说明（2026-05-13）**：`ExplainTrace` 是**轻量 Task 级追踪日志**——记录排程决策的可读说明（自由文本 Message + ContextData JSON），主要供文本战报输入；它与 v5.0.25 新增的 `ScheduleExplanationFact`（§八.1，**结构化原因事实层**——含 ObjectType / ReasonCode / ImpactHours / EvidenceJson）**共存不替代**，职责层次不同：前者是追踪日志，后者是可被 AI / 前端 / 多版本比较复用的结构化原因事实。**禁止合并或混用**。

---

## 七、库存表（v2.5新增）

### 6.1 MaterialMapping（物料映射表 - SCD Type 2拉链表）（2026-04-01 v4.0重构）

**所属库**：APS_Production  
**业务用途**：记录物料在ERP和MES系统中的映射关系，支持一物多仓、历史追溯  
**v4.0重构**：消除 ERP/MES 字段分叉，ERP_MasterID+MES_ID→统一`SourceID`，ERP_Warehouse+MES_Location→统一`Warehouse`

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 映射ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 核心业务键 | RAW-STEEL-001 |
| SourceID | 源系统物理主键 | INT | v4.0统一：ERP的MasterID / MES的MES_ID（2026-04-03审计修正：与DDL对齐为INT） | 100001 |
| **SourceModel** | **ERP原始型号** | **NVARCHAR(100)** | **⚠️ v5.0.27新增；ERP原始型号；用于 `sp_ValidateAndPromoteOrders` Step 0b Model→MaterialCode解析链；`Source='ERP'` 行填写，`Source='MES'` 通常为NULL** | C25ILB-005 |
| Warehouse | 仓库编码 | NVARCHAR(50) | v4.0统一：ERP仓库 / MES库位 | WH-01 |
| Source | 来源系统 | NVARCHAR(20) | ERP/MES | ERP |
| ValidFrom | 生效开始时间 | DATETIME2 | SCD Type 2：记录生效时间 | 2026-01-01 00:00:00 |
| ValidTo | 生效结束时间 | DATETIME2 | SCD Type 2：记录失效时间，NULL表示当前有效 | NULL |
| IsCurrent | 是否当前版本 | BIT | 1=当前有效，0=历史版本 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 10:00:00 |
| Warehouse_Norm | 仓库标准化 | NVARCHAR(50) | 持久化计算列 ISNULL(Warehouse,'N/A')，用于唯一索引 | WH-01 |

**唯一索引**：`(MaterialCode, Source, Warehouse_Norm, IsCurrent)` WHERE `IsCurrent = 1`（v4.0简化：6列→4列）  
**⚠️ v5.0.27新增索引**：`IX_MaterialMapping_SourceModel(Source, SourceModel, IsCurrent)` WHERE `SourceModel IS NOT NULL`（Step 0b Model查找用）

---

### 6.2 InventoryFact_ERP（ERP库存事实表）

**所属库**：APS_Production  
**业务用途**：记录从ERP系统同步的库存数据（原始数据，不做修改）  
**⚠️ 架构原则**：保留源系统物理真相（MasterID + WarehouseCode），不直接存 MaterialCode；MaterialCode 的统一挂接由 MaterialMapping 负责  
**v5.0.39**：新增 `FactoryCode`（来自 `ext_ERP_Inventory_View`，用于 Step3 映射 FactoryId）；字段名 `Warehouse` → `WarehouseCode`

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MasterID | ERP物料主键 | INT | ERP物理主键，保留用于物理追溯 | 100001 |
| WarehouseCode | 仓库编码 | NVARCHAR(50) | ERP仓库；V1 统一字段名 | WH-01 |
| **FactoryCode** | **工厂编码** | **NVARCHAR(50) NULL** | **v5.0.39新增；来自 `ext_ERP_Inventory_View`；`sp_SyncInventorySnapshot` Step3 据此映射 FactoryId** | TJ |
| Quantity | 库存数量 | DECIMAL(18,4) | ERP系统的实际库存 | 1000 |
| SyncedAt | 同步时间 | DATETIME2 | 从ERP同步的时间戳 | 2026-03-05 16:00:00 |

---

### 6.3 InventoryFact_MES（MES库存事实表）

**所属库**：APS_Production  
**业务用途**：记录从MES系统同步的库存数据（原始数据，不做修改）  
**⚠️ 架构原则**：保留源系统物理真相（MES_ID + WarehouseCode），不直接存 MaterialCode；MaterialCode 的统一挂接由 MaterialMapping 负责  
**v5.0.39**：新增 `WarehouseCode`（V1 主链字段）+ `FactoryCode`；`Location` 字段名历史保留，V1 实际写入 `MES_Inventory_View.WarehouseCode`  
**MES 库存主链说明**：V1 统一使用 `WarehouseCode` 作为库存 StorageCode 参与规则匹配；`LocationCode`（ODS 视图原始字段）仅追溯，不参与 `InventoryAvailabilityRule` 匹配

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MES_ID | MES物料主键 | INT | MES物理主键，保留用于物理追溯 | 5001 |
| Location | 库位（字段名历史保留） | NVARCHAR(50) | 字段名历史保留；V1 实际写入 `MES_Inventory_View.WarehouseCode` 值（非 LocationCode） | WH-MES-01 |
| **WarehouseCode** | **仓库编码（V1 主链）** | **NVARCHAR(50) NULL** | **v5.0.39新增；V1 主链字段 = `MES_Inventory_View.WarehouseCode`；参与 InventoryAvailabilityRule 规则匹配** | WH-MES-01 |
| **FactoryCode** | **工厂编码** | **NVARCHAR(50) NULL** | **v5.0.39新增；来自 `ext_MES_Inventory_View`；`sp_SyncInventorySnapshot` Step3 据此映射 FactoryId** | TJ |
| Quantity | 库存数量 | DECIMAL(18,4) | MES系统的实际库存 | 500 |
| SyncedAt | 同步时间 | DATETIME2 | 从MES同步的时间戳 | 2026-03-05 16:05:00 |

---

### 6.4 InventorySupplyCandidate（库存候选供给池 - v2.8新增）

**所属库**：APS_Production  
**业务用途**：通过MaterialMapping折算后的候选库存，保留来源和仓库维度用于规则筛选  
**⚠️ 架构定位**：库存链路中第一次正式形成统一MaterialCode的候选供给层  
**说明**：InventoryFact_ERP/MES保留物理主键，本表通过MaterialMapping进行物理身份挂接，是库存链路中第一次真正进入APS统一业务语义的地方

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 统一业务键 | RAW-STEEL-001 |
| FactoryId | 工厂ID | INT | 外键关联Factory表 | 1 |
| SourceSystem | 来源系统 | NVARCHAR(20) | ERP/MES | ERP |
| StorageCode | 仓库/库位代码 | NVARCHAR(50) | ERP仓库或MES库位 | WH-01 |
| Quantity | 候选库存数量 | DECIMAL(18,4) | 原始候选值 | 1500 |
| ERP_MasterID | ERP主键 | INT | ERP来源时有值 | 100001 |
| MES_ID | MES主键 | INT | MES来源时有值 | NULL |
| IsEligible | 是否可用 | BIT | 规则筛选后的状态，1=可用 | 1 |
| RejectReason | 剔除原因 | NVARCHAR(500) | 被规则剔除时记录原因 | NULL |
| SyncedAt | 同步时间 | DATETIME2 | 候选记录生成时间 | 2026-03-21 16:00:00 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-21 16:00:00 |

**技术要点**：
- 保留来源（ERP/MES）和仓库/库位维度，不过早聚合
- 规则筛选后，`IsEligible=0` 的记录会被剔除，并记录 `RejectReason`
- 最终只有 `IsEligible=1` 的候选库存写入 `InventoryAvailableSupplyDetail`（保留规则+仓库+优先级明细），再从该表汇总生成 `InventoryBalance`

---

### 6.4.5 InventoryAvailableSupplyDetail（可用供给明细层 - v5.0.40 新增）

**所属库**：APS_Production  
**业务用途**：规则命中后、余额汇总前的可用库存明细层。从 `InventorySupplyCandidate` 经 `InventoryAvailabilityRule` 裁决后生成，保留仓库级、来源级、规则级、优先级明细，供 `InventoryBalance` 汇总和排程引擎扣减使用。  
**⚠️ 架构定位**：

> **`InventoryBalance` 负责"总量够不够"；`InventoryAvailableSupplyDetail` 负责"这些总量从哪里来、按什么顺序扣"。**

| 字段 | 含义 | 数据类型 | 业务说明 |
|------|------|---------|---------|
| `Id` | 主键 | BIGINT | 自增 |
| `BatchNo` | 库存快照批次 | NVARCHAR(50) | 与 `sp_SyncInventorySnapshot @BatchNo` 同批 |
| `InventorySupplyCandidateId` | 候选供给追溯 | BIGINT NULL | 对应 `InventorySupplyCandidate.Id`；**仅逻辑追溯，不加外键**（加 FK 会阻塞 TRUNCATE） |
| `MaterialCode` | APS 统一物料编码 | NVARCHAR(50) | 来自候选供给池 |
| `ProductFamilyId` | 库存使用上下文产品族 | INT | ⚠️ 来自 `InventoryAvailabilityRule.ProductFamilyId`，**不是物料自身产品族** |
| `FactoryId` | 库存所属工厂 | INT | FK → `Factory.Id` |
| `SourceSystem` | 来源系统 | NVARCHAR(20) | ERP / MES |
| `StorageCode` | 仓库代码 | NVARCHAR(50) | V1 统一使用 WarehouseCode |
| `Quantity` | 当前明细可用数量 | DECIMAL(18,4) | 本行候选库存数量 |
| `AvailabilityRuleId` | 命中规则 | BIGINT | FK → `InventoryAvailabilityRule.Id` |
| `RulePriority` | 扣减优先级 | INT | 来自命中规则 `Priority`；数值越小越优先；**排程扣减顺序必须读本字段** |
| `ERP_MasterID` | ERP 来源主键追溯 | INT NULL | ERP 来源时有值 |
| `MES_ID` | MES 来源主键追溯 | INT NULL | MES 来源时有值 |
| `CreatedAt` | 创建时间 | DATETIME2 | 记录创建时间 |

**与相邻表的区别**：

| 表 | 定位 |
|----|------|
| `InventorySupplyCandidate` | ERP/MES 事实经 `MaterialMapping` 后的**候选**库存池，尚未完成规则裁决 |
| `InventoryAvailableSupplyDetail` | 候选库存经规则裁决后，**真正可用于排程**的可用供给明细（本表） |
| `InventoryBalance` | 从本表按 `(MaterialCode, ProductFamilyId, FactoryId)` 汇总生成的余额表 |

**它不是订单消耗明细表**：  
本表记录"本次快照哪些库存经规则裁决后可被排程使用"，**不记录**哪个订单实际消耗了哪条库存。  
订单消耗、库存分配、Pegging 关系后续由 `InventoryAllocationResult` 承接（V1.1/V2 预留）。

**技术要点**：
- 每次 `sp_SyncInventorySnapshot`：Step 3 开头先 `TRUNCATE TABLE InventoryAvailableSupplyDetail`（先于 `InventorySupplyCandidate`），Step 4c 写入，Step 5 汇总到 `InventoryBalance`
- `InventorySupplyCandidateId` 不加外键；通过 `IX_IASD_Candidate` 普通索引支持追溯查询
- 索引 `IX_IASD_Deduction(MaterialCode, ProductFamilyId, FactoryId, RulePriority)` 为排程扣减主查询索引

---

### 6.5 InventoryBalance（库存余额表 - v2.8重构；v5.0.39口径修正）

**所属库**：APS_Production  
**业务用途**：`sp_SyncInventorySnapshot` 规则筛选后、按（MaterialCode + ProductFamilyId + FactoryId）汇总的可用库存快照  
**v5.0.39 口径修正**：
- `ProductFamilyId` = **库存使用上下文**，不等于库存物料自身的 `Material.ProductFamilyId`；同一物料可在不同产品族上下文下形成不同的可用库存池
- `BatchNo` = **库存快照批次标签**，标识"本行余额由哪次 `sp_SyncInventorySnapshot` 生成"；不是订单消耗记录（订单消耗追溯由 `InventoryAllocationResult` 承接，V1.1/V2 预留）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务键 | RAW-STEEL-001 |
| **ProductFamilyId** | **产品族ID（库存使用上下文）** | **INT** | **⚠️ 库存使用上下文，非物料自身产品族；对应 `InventoryAvailabilityRule.ProductFamilyId`** | 3 |
| FactoryId | 工厂ID | INT | 外键 Factory.Id | 1 |
| OnHandQty | 现有量 | DECIMAL(18,4) | `InventoryAvailabilityRule` 筛选后汇总的可用库存量 | 1500 |
| AllocatedQty | 已分配量 | DECIMAL(18,4) | 已被订单占用的数量（V1 由排程引擎写入） | 200 |
| AvailableQty | 可用量 | DECIMAL(18,4) | 计算列：现有量-已分配量 | 1300 |
| Source | 主要来源 | NVARCHAR(20) | ERP/MES/BOTH | BOTH |
| **BatchNo** | **快照批次标签** | **NVARCHAR(50) NULL** | **⚠️ `sp_SyncInventorySnapshot` 写入快照标签；非订单消耗记录；可为 NULL（手动调整记录）** | 20260531_000000 |
| LastUpdatedAt | 最后更新时间 | DATETIME2 | 快照最后更新时间 | 2026-03-21 16:10:00 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-21 16:00:00 |

**架构要点**：
- **唯一约束**：`(MaterialCode, ProductFamilyId, FactoryId)`
- **InventoryAllocationResult**：订单级消耗明细表（V1.1/V2 预留，V1 不建表；参见 §2.6.8.5 注释）
- 排程前必须一次性全量预加载 `IsActive` 库存入内存（V1 无 IsActive 字段，全量即全部有效行）

---

### 6.6 SupplyFact_Pipeline（管道供给事实层 - v5.0.23 新增 2026-05-09；v5.0.42 2026-06-15 新增4追溯字段）

**所属库**：APS_Production  
**业务用途**：APS 本地标准化供给事实层（允许少量本地派生字段），承接在途/管道供给；并行于现货库存六层主链，不替代 `InventoryBalance`  
**当前来源**：ERP 厂间物流运输在途（`ERP_InterplantInTransit_View`）；未来按需扩展  
**定位说明**：`ETA` 是 ODS 原始事实字段；`AvailableTime` 是本地派生字段（= ETA + `SupplyAvailabilityRule.LeadTimeOffset`，由 `sp_SyncPipelineSupply` 装载时计算落库）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(100) | **APS 标准编码**（mat.MaterialCode，通过 MasterID+StorageCode→MaterialMapping→Material 桥接获取）；ODS.v_MaterialCode 仅交叉校验 | MAT-10001 |
| MaterialId | 物料ID | INT NOT NULL | 必须有效映射（MasterID→MaterialMapping.SourceID→Id）；映射失败则不写入本表，登记 APS_ETL_Log | 42 |
| FactoryCode | 工厂编码 | NVARCHAR(50) | ODS原始工厂编码 | TJ |
| FactoryId | 工厂ID | INT NOT NULL | INNER JOIN Factory 映射；映射失败整行跳过不写入；FK→Factory(Id)；参见映射红线说明 | 3 |
| ProductFamilyId | 产品族ID | INT NULL | 装载时映射；FK→ProductFamily.Id | 7 |
| SupplyType | 供给类型 | NVARCHAR(50) | INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / ARRIVED_NOT_RECEIVED / VMI_ONSITE / SUPPLIER_RESERVED | INTERPLANT_IN_TRANSIT |
| OwnershipType | 所有权类型 | NVARCHAR(20) | OWNED / CONSIGNMENT / SUPPLIER；默认 OWNED | OWNED |
| QualityStatus | 质量状态 | NVARCHAR(20) | AVAILABLE / PENDING_INSPECTION / HOLD；默认 AVAILABLE | AVAILABLE |
| Quantity | 数量 | DECIMAL(18,4) | 供给数量 | 500.0000 |
| **ETA** | **预计到达时间** | **DATETIME2** | **⚠️ ODS原始事实字段**：源系统（ERP）预计到厂时间；不在本地修改 | 2026-05-15 08:00:00 |
| **AvailableTime** | **排程可用时间** | **DATETIME2** | **⚠️ 本地派生字段**：= ETA + `SupplyAvailabilityRule.LeadTimeOffset`（小时）；`sp_SyncPipelineSupply` 装载时计算并落库 | 2026-05-15 16:00:00 |
| StorageCode | 存储位置编码 | NVARCHAR(50) | **目的仓库编码 / 预计收货仓库**（发出仓由 SourceFactoryCode 单独追溯） | WH-TJ-01 |
| SupplierCode | 供应商编码 | NVARCHAR(50) | VMI/采购在途时有效 | NULL |
| SourceSystem | 来源系统 | NVARCHAR(50) | 数据来源系统；默认 ERP | ERP |
| SourceDocumentNo | 来源单据号 | NVARCHAR(100) | ERP 运输单号 / 采购单号等 | TR-20260514-001 |
| SourceMasterID | ERP物料来源主键 | INT NULL | v5.0.42 — ODS MasterID 直通，与 StorageCode 共同参与 MaterialMapping(SourceID+Warehouse_Norm) 物理身份挂接；保留 ERP 直通追溯 | 10042 |
| SourceFactoryCode | 发出工厂编码 | NVARCHAR(50) NULL | v5.0.42 — ODS SourceFactoryCode 直通字段；仅用于物流追溯，非可用工厂判定依据 | BJ |
| SourceDocumentLineNo | ERP来源单据行号 | NVARCHAR(50) NULL | v5.0.42 — 与 SourceDocumentNo 共同定位 ERP 明细行 | 10 |
| SourceUpdatedAt | ERP来源更新时间 | DATETIME2 NULL | v5.0.42 — ERP 源记录最后更新时间；用于增量同步与数据新鲜度检查 | 2026-06-14 23:30:00 |
| SourceRowKey | 来源幂等键 | AS CONCAT(...) PERSISTED | v5.0.42 P0-11 — 计算列：SourceSystem/SupplyType/SourceDocumentNo/SourceDocumentLineNo/SourceMasterID/StorageCode/FactoryCode 拼接；防止同来源记录重复同步 | ERP\|INTERPLANT_IN_TRANSIT\|TR-001\|10\|10042\|WH-TJ-01\|TJ |
| SupplyAvailabilityRuleId | 命中规则ID | INT NULL | v5.0.42 P1-3 — 规则裁决追溯；FK→SupplyAvailabilityRule(Id)；无命中为NULL | 5 |
| AppliedLeadTimeOffset | 命中的LeadTimeOffset | INT NULL | v5.0.42 P1-3 — 命中规则的提前期偏移（小时）；用于解释 AvailableTime 计算过程 | 8 |
| RulePriority | 规则优先级 | INT NULL | v5.0.42 P1-3 — 命中规则的 Priority 值 | 10 |
| RuleEvaluatedAt | 规则裁决时间 | DATETIME2 NULL | v5.0.42 P1-3 — 与 @DataCutoffTime 同值；禁止使用 GETDATE() 判定 | 2026-06-16 02:00:00 |
| BatchNo | 排程批次号 | NVARCHAR(50) NULL | **nullable**；夜间全量排程=当日批次号（形成快照）；白天实时=NULL（读最新 IsActive=1 记录） | 20260515_000000 |
| IsActive | 是否有效 | BIT | 1=有效；软删除标记 | 1 |
| SyncedAt | 同步时间 | DATETIME2 | 本条记录写入 APS 的时间 | 2026-05-15 00:05:00 |

**索引说明**：
- `IX_SupplyFact_Pipeline_Query`：(MaterialCode, FactoryId, ProductFamilyId, SupplyType) WHERE IsActive=1 — 主查询索引
- `IX_SupplyFact_Pipeline_Batch`：(BatchNo) WHERE BatchNo IS NOT NULL — 批次快照查询索引

**来源追溯映射链**（v5.0.42 新增；v5.0.42 final 补多仓维度）：
```text
ODS.MasterID + ODS.StorageCode
  → MaterialMapping.SourceID + Warehouse_Norm = ISNULL(v.StorageCode, 'N/A')
    （Source='ERP' AND IsCurrent=1）
  → MaterialMapping.MaterialCode
  → Material.MaterialCode
  → Material.Id → SupplyFact_Pipeline.MaterialId（NOT NULL；映射失败不写入本表）
  → Material.ProductFamilyId → SupplyFact_Pipeline.ProductFamilyId
```
该映射链确保一物多仓场景下每一行管道供给都经过正确的仓库级物料身份映射。

**映射红线说明**：
- `MasterID` 无法映射 `MaterialId`：登记 `APS_ETL_Log`，该行**不写入** `SupplyFact_Pipeline`，不进入 `SupplyAvailabilityRule`，不进入 `ScheduleContext.PipelineSupplies`
- `FactoryCode` 无法映射 `FactoryId`：同上，登记日志，该行不写入
- 物料身份失败与工厂身份失败的行**不得**通过 `ProductFamilyId=NULL` 通配规则兜底

**产品族空值口径**：
- `ProductFamilyId` 可以为空，但**仅限于** `MaterialId` 已成功映射而该物料本身未配置产品族时
- 此时允许 `ProductFamilyId=NULL` 并参与 `ProductFamilyId=NULL` 的通配规则匹配

**工厂语义**（v5.0.42 统一）：
- `FactoryCode` / `FactoryId` = **目的工厂**（收货工厂、可使用该供给的工厂）
- `SourceFactoryCode` = **发出工厂**（仅用于物流追溯）

**时间标准**（v5.0.42 统一）：
- 全链统一使用中国工厂本地时间（UTC+8）
- `ETA`、`AvailableTime`、`DataCutoffTime`、`SyncedAt`、`RuleEvaluatedAt` 禁止混用 UTC

**ETA=NULL 策略**（v5.0.42）：
- `AvailableTime` 仍为 NULL
- 不得作为有确定时间的供给解除缺料或承诺交期
- 可进入"待确认管道供给"异常清单
- 默认不参与确定性排程供给扣减
- 若某供给类型允许无 ETA 使用，须由明确规则单独授权

**StorageCode 业务口径**（v5.0.42 P0-3）：
- 厂间在途（`INTERPLANT_IN_TRANSIT`）：业务视为必填；为空则跳过不写入，登记 `PIPELINE_STORAGECODE_MISSING`
- 其他供给类型：按各自业务规则单独定义

**来源幂等**（v5.0.42 P0-11）：
- 逻辑键：`SourceSystem + SupplyType + SourceDocumentNo + SourceDocumentLineNo + SourceMasterID + StorageCode + FactoryCode`
- 通过计算列 `SourceRowKey` 实现
- 唯一约束：`UNIQUE(SourceRowKey（持久化计算列）, BatchNo)` 防止同批次同来源重复写入
- 同一 `MaterialCode` 出现在多个仓库/单据/ETA 是正常事实，禁止按 MaterialCode 去重

**消费口径**（V1.1/V2）：
- 夜间快照：`WHERE BatchNo = @CurrentBatchNo AND IsActive = 1 AND AvailableTime IS NOT NULL`（正式消费唯一路径；ETA=NULL记录不进入）
- 白天实时：`WHERE BatchNo IS NULL AND IsActive = 1`（仅为字段预留，V1/V1.1不启用；当前正式调用须传入非空BatchNo）
- 禁止只按 `IsActive=1` 查询（会同时读到多个历史批次）

---

### 6.7 SupplyAvailabilityRule（管道供给规则表 - v5.0.23 新增 2026-05-09）

**所属库**：APS_Production  
**业务用途**：供给主题独立规则表，控制哪些在途/管道供给可纳入排程，以及应用的提前期偏移  
**定位说明**：仅负责管道供给主题；现货链规则由 `InventoryAvailabilityRule`（§7.1）统一管理，本表不介入  
**规则引擎说明**：本表不是"统一万能规则引擎总表"；APS 规则体系方向是"5号位插件接口 + 主题规则表"，本表是管道供给主题的规则表  
**NULL 语义**：各匹配维度 NULL = 通配（适用所有）；规则按 Priority 升序匹配（小=高优先）

**裁决顺序**（V1.1/V2 启用时按以下顺序严格裁决）：
1. `IsActive=1`；
2. `@DataCutoffTime` 位于 `EffectiveFrom`/`EffectiveTo` 有效期内（禁止使用 GETDATE()）；
3. `Priority ASC`（数值越小越优先）；
4. 非 NULL 匹配维度数量 DESC（具体规则优先于通配规则）；
5. `Id ASC`（最终稳定排序）；
6. 只取第一条胜出规则。

**⚠️ V1 状态：表已建立，但 V1 不调用本规则**：
- V1 中 `sp_SyncPipelineSupply` 只执行 `TRUNCATE TABLE SupplyFact_Pipeline` + 写 SUCCESS 日志（空跑）
- V1 不读取正式 ERP 在途数据，不执行字段映射，**不应用本规则**
- `SupplyFact_Pipeline` 结果始终为空，`ScheduleContext.PipelineSupplies` 结果为空集合，不影响现有排程
- **V1 排程只使用现货库存链路**：`InventoryBalance` / `InventoryAvailableSupplyDetail`
- 本规则表为 **V1.1/V2 激活预留**，待 ERP 管道供给来源字段明确后再启用
- ⚠️ **规则版本管理**：同一五维组合（ProductFamilyId, FactoryId, SupplyType, OwnershipType, QualityStatus）同时只允许一条 `IsActive=1` 的规则。`EffectiveFrom`/`EffectiveTo` 用于限定规则的生效时间窗口；新规则发布时必须先停用（`IsActive=0`）旧规则，不得通过唯一索引静默覆盖。未来规则不能提前启用。
- V1.1/V2 真实同步时：`sp_SyncPipelineSupply` 按 `(ProductFamilyId, FactoryId, SupplyType, OwnershipType, QualityStatus)` 五维匹配，`IncludeFlag=1` 的规则计算 `AvailableTime = DATEADD(HOUR, LeadTimeOffset, ETA)`；无命中规则时默认不可用并登记 APS_ETL_Log；**映射失败（MaterialId 或 FactoryId 无法解析）的行直接跳过，不进入规则**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 规则ID | INT | 主键，自增 | 1 |
| ProductFamilyId | 产品族ID | INT NULL | 规则适用产品族；NULL=所有产品族 | 7 |
| FactoryId | 工厂ID | INT NULL | 规则适用工厂；NULL=所有工厂 | NULL |
| SupplyType | 供给类型 | NVARCHAR(50) NULL | 规则适用供给类型；NULL=所有类型 | INTERPLANT_IN_TRANSIT |
| OwnershipType | 所有权类型 | NVARCHAR(20) NULL | NULL=所有；OWNED / CONSIGNMENT / SUPPLIER | NULL |
| QualityStatus | 质量状态 | NVARCHAR(20) NULL | NULL=所有；AVAILABLE / PENDING_INSPECTION / HOLD | AVAILABLE |
| IncludeFlag | 是否纳入排程 | BIT | 1=纳入排程可用供给 / 0=排除 | 1 |
| Priority | 优先级 | INT | 值越小优先级越高；默认50；裁决时按 Priority ASC → 非NULL维度数量 DESC → Id ASC 取第一条 | 10 |
| LeadTimeOffset | 提前期偏移 | INT | 单位：小时；`AvailableTime = ETA + LeadTimeOffset`；默认0 | 8 |
| EffectiveFrom | 生效起始时间 | DATETIME2 NULL | NULL=立即生效 | 2026-01-01 00:00:00 |
| EffectiveTo | 生效截止时间 | DATETIME2 NULL | NULL=永久有效 | NULL |
| IsActive | 是否启用 | BIT | 1=启用；软删除标记 | 1 |
| Remark | 备注 | NVARCHAR(500) | 规则说明 | 厂间在途默认+8小时提前期 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-05-09 |
| UpdatedAt | 更新时间 | DATETIME2 | 记录最后更新时间 | 2026-05-09 |

**唯一索引**：`UX_SupplyAvailabilityRule_NoDupRule`：  
`UNIQUE (ProductFamilyId, FactoryId, SupplyType, OwnershipType, QualityStatus) WHERE IsActive=1`  
⚠️ SQL Server NULL 语义：NULL=NULL 对唯一索引成立，故本索引有效防止完全相同的五维规则组合重复写入；`WHERE IsActive=1` 允许软删除后以相同维度重建规则。

---

### 6.8 MaterialSupplyContext（物料供给与责任上下文表 - v2.7新增）

**所属库**：APS_Production  
**业务用途**：记录物料在不同仓库/工厂下的供给方式、责任归属、计划参数  
**核心理念**：同一物料在不同仓库下，业务语义会变化（采购/自制、生产部门等）  
**架构定位**：承载"仓库级业务上下文"，而非"物料本体属性"

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 上下文ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 统一业务键 | RAW-STEEL-001 |
| WarehouseCode | 仓库编码 | NVARCHAR(50) | 关键维度，决定业务上下文 | WH-01 |
| FactoryId | 工厂ID | INT | 可选，关联Factory表 | 1 |
| SupplyMode | 供给方式 | NVARCHAR(20) | PURCHASE/MAKE/OUTSOURCE/MIXED | MAKE |
| DefaultProductionDeptCode | 默认生产责任部门 | NVARCHAR(50) | 自制件时有效；源系统/业务码（便于追溯） | DEPT-PROD-A |
| **DefaultProductionDepartmentId** 🆕 | **默认生产部门 ID** | INT | **v5.0.16 新增**；FK → ProductionDepartment.Id；APS 标准字典 FK，与 DeptCode 双轨；2 号位 `sp_RebuildMaterialStageDeptContext` 优先用此 ID 组装 Context | 12 |
| ProcurementDeptCode | 采购责任部门 | NVARCHAR(50) | 采购件时有效，由APS维护 | DEPT-PROC-B |
| OutsourceDeptCode | 委外责任部门 | NVARCHAR(50) | 委外工序责任归属 | DEPT-PROD-A |
| LeadTimeDays | 提前期天数 | INT | 该上下文的提前期（天） | 7 |
| SafetyStock | 安全库存 | DECIMAL(18,4) | 该仓库的安全库存 | 500 |
| InventoryManagementMode | 库存管理模式 | NVARCHAR(50) | STOCKED/NON_STOCKED（2026-04-03审计补充，v4.0新增） | STOCKED |
| SourceSystem | 数据来源系统 | NVARCHAR(20) | ERP/APS/MIXED | ERP |
| ValidFrom | 生效开始时间 | DATETIME2 | SCD Type 2：记录生效时间 | 2026-01-01 00:00:00 |
| ValidTo | 生效结束时间 | DATETIME2 | SCD Type 2：记录失效时间，NULL表示当前有效 | NULL |
| IsCurrent | 是否当前版本 | BIT | 1=当前有效，0=历史版本 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-21 10:00:00 |

**业务场景示例**：
- 同一物料 `RAW-STEEL-001` 在仓库 `WH-A` 是采购件（`SupplyMode=PURCHASE`），采购部门负责
- 同一物料 `RAW-STEEL-001` 在仓库 `WH-B` 是自制件（`SupplyMode=MAKE`），生产部门负责
- 不同仓库的安全库存、提前期也可能不同

**技术要点（数据流向）**（2026-04-01 v4.0更新）：
- **数据来源**：从ODS库的 `ext_ERP_Master_View` 和 `ext_MES_Material_View` 双源同构视图同步
- **同步逻辑**：通过 `sp_SyncMasterData(@SourceType)` 存储过程的步骤3（v4.0双源统一），从源端快照同步到此表
- **负责人**：2号位（通过 `IDataLoader` 或定时Job执行）
- **同步时机**：每天00:10（ERP）、00:20（MES）

**⚠️ 同步机制**（2026-04-01 v4.0更新）：
- **更新机制**：**SCD Type 2 拉链**（非简单Upsert）
- **追踪字段**：SupplyMode, DefaultProductionDeptCode, LeadTimeDays, SafetyStock, InventoryManagementMode
- **同步逻辑**：
  - 供给属性变化 → 关闭旧版本（IsCurrent=0, ValidTo=@SyncTime）+ 插入新版本
  - 全新的物料-仓库组合 → 直接插入新记录
  - 源端消失的仓库 → 关闭对应记录
- **v4.0新增字段**：`InventoryManagementMode`（STOCKED/NON_STOCKED）
- **v4.0变更**：`SourceSystem` 移除默认值 'ERP'（双源都会写入）
- **实现方式**：
  ```sql
  -- sp_SyncMasterData 存储过程的步骤3（2026-04-01 v4.0更新）
  -- 3a. 准备源数据（#Source_Snapshot JOIN MaterialMapping WHERE IsCurrent=1）
  -- 3b. 供给属性变化（SupplyMode/DefaultProductionDeptCode/LeadTimeDays/SafetyStock/InventoryManagementMode）
  --     → 关闭旧版本（IsCurrent=0, ValidTo=@SyncTime）
  -- 3c. 属性变化的记录 + 全新记录 → 插入新版本（IsCurrent=1）
  -- 3d. 源端消失的仓库 → 关闭对应的 MaterialSupplyContext 记录
  -- 完整代码见 DDL 文件：APS_数据库表结构设计_v4.0.sql 第 4.3 节
  -- 完整设计见：双源同构主数据三表协同同步设计_v2.0
  ```
- **架构红线**：
  - ❌ **禁止**每天全量删除重建 `MaterialSupplyContext` 表
  - ✅ **必须**采用 SCD Type 2 拉链，保持历史可追溯
  - ✅ **必须**基于源端快照 JOIN MaterialMapping(IsCurrent=1) 驱动同步

**技术要点**：
- 唯一索引：`(MaterialCode, WarehouseCode, IsCurrent)` WHERE `IsCurrent = 1`
- 支持 SCD Type 2，可追溯历史变化
- 与 `MaterialMapping` 表职责分离：`MaterialMapping` 只做物理身份桥接（SourceID+Warehouse），`MaterialSupplyContext` 承载业务上下文（供给方式、计划参数）

---

## 七、配置表

### 7.1 InventoryAvailabilityRule（统一库存可用规则表）⭐ v5.0.39 V1口径

**所属库**：APS_Production  
**业务用途**：统一定义哪些仓库的库存允许进入可用库存池，以及对应的扣减优先级  
**V1 变更说明**：替代旧 `ProductFamilyInventoryScope`（§7.1旧）+ `InventorySourceRule`（§7.2旧）+ `InventorySourcePriority`（§7.3旧，v2.8已废弃），以上三表均已从当前版本删除

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 规则ID | BIGINT | 主键，自增 | 1 |
| ProductFamilyId | 产品族ID（库存使用上下文） | INT | 外键 ProductFamily.Id；代表"在哪个产品族场景中消耗此库存"，非物料自身产品族 | 3 |
| FactoryId | 工厂ID | INT | 外键 Factory.Id；规则所属工厂 | 1 |
| MaterialCodePattern | 物料编码匹配模式 | NVARCHAR(100) | 支持 LIKE 通配符（CYL-%）；NULL=该工厂+产品族下所有物料通用 | CYL-% |
| SourceSystem | 来源系统 | NVARCHAR(20) | ERP / MES | ERP |
| StorageCode | 仓库代码 | NVARCHAR(50) | V1 统一使用 WarehouseCode；ERP 或 MES 仓库 | WH-01 |
| IsAvailable | 是否允许 | BIT | 1=允许进入可用库存池；0=排除（直接拒绝） | 1 |
| Priority | 扣减优先级 | INT | 数值越小越优先；同一物料+产品族+工厂下按此排序扣减 | 100 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| Remark | 备注 | NVARCHAR(500) | 业务配置原因（可空） | 主仓库允许 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间（UTC） | 2026-05-31 00:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间（UTC） | 2026-05-31 00:00:00 |

**关键字段语义**：

- **ProductFamilyId = 库存使用上下文**：同一物料可在不同产品族上下文中形成不同的可用库存池（因 InventoryAvailabilityRule 范围不同）；与 `InventoryBalance.ProductFamilyId` 一致
- **IsAvailable=0 vs 无匹配规则**：IsAvailable=0 表示主动排除（胜出规则为排除规则）；无匹配规则时 `IsEligible` 保持 0 + `RejectReason='NoRuleMatch'`，**不进 `InventoryBalance`**（v5.0.40 白名单模式，已删除"无匹配兜底"旧口径）
- **废弃口径**：旧 `RuleAction(PREFER/EXCLUDE)` 已废弃；IsAvailable=1 对应原 PREFER，IsAvailable=0 对应原 EXCLUDE

**技术要点**：
- `sp_SyncInventorySnapshot` Step 4 按胜出规则裁决：每 `(CandidateId, ProductFamilyId)` 选唯一胜出规则（精确 > 通配；Priority ASC；r.Id tiebreak）；胜出规则 `IsAvailable=1` → 写 `InventoryAvailableSupplyDetail`；`IsAvailable=0` → `RejectReason`；无匹配 → `NoRuleMatch` WARN
- `MaterialCodePattern IS NULL` 表示通配（匹配该工厂+产品族下所有物料）
- 索引：`IX_InventoryAvailabilityRule_Context(ProductFamilyId, FactoryId, SourceSystem, StorageCode, IsActive)`
- 索引：`IX_InventoryAvailabilityRule_Priority(ProductFamilyId, FactoryId, Priority)`
- 负责人：5号位负责规则配置；2号位负责 `sp_SyncInventorySnapshot` 装载

---

### 7.2 FenceConfig（冻结区/锁定区配置表）

**所属库**：APS_Production  
**业务用途**：定义不同工艺类型的冻结区和锁定区天数

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 配置ID | INT | 主键，自增 | 1 |
| ProductFamilyId | 产品族ID | INT | 外键关联ProductFamily表 | 1 |
| FactoryId | 工厂ID | INT | 外键关联Factory表，NULL表示全局 | NULL |
| ProcessType | 工艺类型 | NVARCHAR(50) | ASSEMBLY/MACHINING/PROCUREMENT_DOMESTIC/PROCUREMENT_IMPORT | ASSEMBLY |
| FrozenDays | 冻结区天数 | INT | 冻结区：不允许修改 | 3 |
| FirmDays | 锁定区天数 | INT | 锁定区：需要审批才能修改 | 2 |
| EffectiveFrom | 生效日期 | DATE | 配置生效开始日期 | 2026-01-01 |
| EffectiveTo | 失效日期 | DATE | 配置失效日期，NULL表示永久有效 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

---

### 7.3 TaskSplitRuleConfig（拆批规则配置表）

**所属库**：APS_Production  
**业务用途**：PMC配置拆批规则（MOQ、EOQ、瓶颈设备策略）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 规则ID | INT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| ResourceGroupId | 资源组ID | INT | 外键关联ResourceGroup表 | 10 |
| MinimumOrderQuantity | 最小经济批量（MOQ） | DECIMAL(18,4) | 最小加工批量 | 100 |
| EconomicOrderQuantity | 最大经济批量（EOQ） | DECIMAL(18,4) | 最大加工批量 | 1000 |
| BottleneckSplitStrategy | 瓶颈设备拆批策略 | NVARCHAR(50) | PREFER_SPLIT/PREFER_MERGE | PREFER_SPLIT |
| NonBottleneckStrategy | 非瓶颈设备策略 | NVARCHAR(50) | PREFER_LARGE_BATCH/PREFER_SMALL_BATCH | PREFER_LARGE_BATCH |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| EffectiveFrom | 生效时间 | DATETIME2 | 规则生效开始时间 | 2026-01-01 00:00:00 |
| EffectiveTo | 失效时间 | DATETIME2 | 规则失效时间，NULL表示永久有效 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

---

## 📋 附录A：表清单总览

### ODS库（MES_Integration）

| 序号 | 表名 | 类型 | 用途 | 负责人 |
|------|------|------|------|--------|
| 1 | MES_API_BOM_Request | 表 | BOM展开请求（批次） | 2号位 |
| 2 | MES_API_BOM_Request_Detail | 表 | BOM展开请求明细 | 2号位 |
| 3 | MES_APS_BOM_Workset | 表 | BOM展开结果工作集 | 2号位 |
| 4 | MES_APS_BOM_Workset_Archive | 表 | BOM展开结果归档 | 2号位 |
| 5 | MES_API_BOM_Request_Realtime | 表 | 实时BOM展开请求 | 2号位 |
| 6 | MES_APS_BOM_Workset_Realtime | 表 | 实时BOM展开结果 | 2号位 |
| 7 | MES_API_BOM_Request_Log | 表 | BOM展开日志 | 2号位 |
| 8 | MES_BOM_View | 视图契约 | MES BOM视图 | MES DBA |
| 9 | ERP_Master_View | 视图契约 | ERP主数据视图 | ERP DBA |
| 10 | MES_Material_View | 视图契约 | MES物料视图 | MES DBA |
| 11 | **MES_APS_Routing_View** | **视图契约** | **MES工艺路线视图（3号位负责）** | **3号位** ⭐ |

### APS库（APS_Production）

| 序号 | 表名 | 类型 | 用途 | 负责人 |
|------|------|------|------|--------|
| 1 | ext_ERP_Master_View | 跨库包装视图 | ERP主数据包装视图 | 2号位 |
| 2 | ext_MES_Material_View | 跨库包装视图 | MES物料包装视图 | 2号位 |
| 3 | **ext_MES_APS_Routing_View** | **跨库包装视图** | **MES工艺路线包装视图** | **2号位** ⭐ |
| 4 | APS_BOM_RAW | 表 | BOM原始数据缓存 | 2号位 |
| 5 | APS_ETL_Log | 表 | ETL日志 | 2号位 |
| 6 | ERP_Order_Staging | 表 | ERP订单同步临时表 | 2号位 |
| 7 | MaterialMapping | 表 | 物料映射表（SCD Type 2） | 2号位 |
| 8 | ~~InventorySourcePriority~~ | ~~表~~ | ~~库存来源优先级配置（v2.8废弃，v5.0.39删除）~~ | ~~2号位~~ |
| 9-30 | ... | 表 | 主数据、订单、任务等（见完整文档） | 各号位 |

---

## 📋 附录B：视图契约部署检查清单

### 部署前检查

- [ ] **MES_BOM_View** 已在ODS库中创建（MES DBA负责）
- [ ] **ERP_Master_View** 已在ODS库中创建（ERP DBA负责）
- [ ] **MES_Material_View** 已在ODS库中创建（MES DBA负责）
- [ ] ~~**MES_APS_Routing_View**~~ ⚠️ v5.0废弃，已拆分为以下3个视图
- [ ] **MES_APS_Routing_Operation_View** 已在ODS库中创建（**3号位负责** ⭐）（v5.0新增）
- [ ] **MES_APS_Routing_Dependency_View** 已在ODS库中创建（**3号位负责** ⭐）（v5.0新增）
- [ ] **APS_OperationResourceEligibility_View** 已在ODS库中创建（**3号位负责** ⭐）（v5.0新增）
- [ ] **MES_APS_Resource_View** 已在ODS库中创建（**MES DBA负责**）（v5.0新增；v5.0.13 命名统一，原名 `APS_Resource_View`）
- [ ] **ext_ERP_Master_View** 已在APS库中创建（2号位负责）
- [ ] **ext_MES_Material_View** 已在APS库中创建（2号位负责）
- [ ] **ext_MES_APS_Resource_View** 已在APS库中创建（2号位负责）（v5.0新增；v5.0.13 命名统一）
- [ ] **ext_MES_APS_Routing_Operation_View** 已在APS库中创建（2号位负责）（v5.0新增）
- [ ] **ext_MES_APS_Routing_Dependency_View** 已在APS库中创建（2号位负责）（v5.0新增）
- [ ] **ext_APS_OperationResourceEligibility_View** 已在APS库中创建（2号位负责）（v5.0新增）
- [ ] APS库的执行账号对ODS库有SELECT权限

### 视图字段验证

```sql
-- 验证 MES_BOM_View 字段（v5.0：9个运行必需 + 2个追溯增强）
SELECT TOP 1 
    BOMNO, ParentMaterialCode, ChildMaterialCode, 
    Quantity, IsActive, IsDefaultVersion,
    ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,  -- v5.0.7 运行必需
    SourceSystem, SourceBOMId                                   -- v5.0 追溯增强
FROM [MES_Integration].[dbo].[MES_BOM_View];

-- 验证 ERP_Master_View 字段
SELECT TOP 1 
    MaterialCode, MasterID, Warehouse, IsActive
FROM [MES_Integration].[dbo].[ERP_Master_View];

-- 验证 MES_Material_View 字段
SELECT TOP 1 
    MaterialCode, MES_ID, IsActive
FROM [MES_Integration].[dbo].[MES_Material_View];

-- ⚠️ v5.0废弃：MES_APS_Routing_View 已拆分为以下3个视图

-- 验证 MES_APS_Routing_Operation_View 字段（3号位负责创建）（v5.0新增，v5.0.1变更：MaterialCode→MES_ID+Model）
SELECT TOP 1 
    MES_ID, Model, RouteCode, PathId, OperationCode, OperationName,
    ProcessType, StandardTime, SetupTime, IsActive
FROM [MES_Integration].[dbo].[MES_APS_Routing_Operation_View];

-- 验证 MES_APS_Routing_Dependency_View 字段（3号位负责创建）（v5.0新增，v5.0.1变更：MaterialCode→MES_ID+Model）
SELECT TOP 1 
    MES_ID, Model, RouteCode, PathId, FromOperationCode, ToOperationCode,
    DependencyType, LagTime, IsActive
FROM [MES_Integration].[dbo].[MES_APS_Routing_Dependency_View];

-- 验证 APS_OperationResourceEligibility_View 字段（3号位负责创建）（v5.0新增，v5.0.1变更：MaterialCode→MES_ID+Model）
SELECT TOP 1 
    MES_ID, Model, RouteCode, PathId, OperationCode, ResourceCode,
    Priority, CapacityFactor, IsPrimary, IsActive
FROM [MES_Integration].[dbo].[APS_OperationResourceEligibility_View];

-- 验证 MES_APS_Resource_View 字段（MES DBA负责创建）（v5.0新增；v5.0.13 命名统一，原名 APS_Resource_View）
SELECT TOP 1 
    ResourceCode, ResourceName, ExternalResourceId, SourceSystem,
    FactoryCode, ProductionDeptCode, ResourceType, Status, CapacityFactor, IsActive  -- v5.0.16: WorkshopCode → ProductionDeptCode
FROM [MES_Integration].[dbo].[MES_APS_Resource_View];
```

---

## 📋 附录C：枚举值汇总

### BOM展开状态（MES_API_BOM_Request.Status）
- `PENDING`：待处理
- `PROCESSING`：处理中
- `READY`：就绪（可被APS消费）
- `CONSUMED`：已消费
- `FAILED`：失败

### ETL步骤（APS_ETL_Log.Step）
- `CalculateLLC`：计算低阶码
- `SyncMapping`：同步物料映射
- `LoadInventory`：加载库存
- `SyncOrders`：同步订单

### 订单同步状态（ERP_Order_Staging.Status）
- `PENDING`：待验证
- `VALIDATED`：验证通过
- `FAILED`：验证失败

---

## 📋 附录D：开发团队使用指南

### 0号位（业务负责人）
- 重点关注：主数据表、订单表、配置表
- 确认内容：字段含义、枚举值、业务规则

### 1号位（计算域核心开发）
- 重点关注：Task表、Pegging表、ExplainTrace表
- 技术要点：分区表、索引优化、内存管理

### 2号位（技术负责人/稳定引擎架构师）
- 重点关注：所有表、视图契约、跨库访问
- 技术要点：DDL部署、权限配置、性能优化

### 3号位（调度编排器）
- 重点关注：PlanVersion表、Order表、ETL相关表
- 技术要点：批次管理、数据同步、异常处理

### 4号位（前端UI）
- 重点关注：Order表、Task表、PlanVersion表
- 技术要点：API接口、数据展示、状态枚举

### 5号位（业务规则引擎）
- 重点关注：配置表（FenceConfig、TaskSplitRuleConfig等）
- 技术要点：规则插件、优先级计算、约束检查

---

## 📋 附录E：与其他文档的对应关系

| 本文档章节 | 对应DDL位置 | 对应API文档 |
|-----------|------------|------------|
| ODS库表 | DDL第一部分（第55-230行） | 无（内部表） |
| 视图契约 | DDL第268-283行 | 无（内部视图） |
| 跨库包装视图 | DDL第271-282行 | 无（内部视图） |
| ETL相关表 | DDL第312-420行 | 无（内部表） |
| 主数据表 | DDL第422-665行 | 《APS_应用层API接口规范_v2.3.md》第3-5节 |
| 订单与任务表 | DDL第667-823行 | 《APS_应用层API接口规范_v2.3.md》第6-8节 |

---

## ✅ 文档确认清单

### 业务确认（0号位）
- [ ] 所有表的业务用途是否清晰？
- [ ] 字段含义是否与业务理解一致？
- [ ] 枚举值是否覆盖所有业务场景？

### 技术确认（1-5号位）
- [ ] 所有表的字段清单是否完整？
- [ ] 视图契约的字段定义是否明确？
- [ ] 跨库访问方式是否清晰？
- [ ] ETL流程是否理解？

### DBA确认
- [ ] 视图契约是否已创建？
- [ ] 跨库权限是否已配置？
- [ ] 索引策略是否合理？

---

**文档版本历史**：
- v1.0（2026-03-11）：初始版本，仅包含APS库的核心业务表
- v2.0（2026-03-11）：同步v2.4架构修复，新增库存三层架构和物料映射表
- v3.0（2026-03-18）：补充ODS库、视图契约、跨库包装视图、ETL相关表，形成完整版
- v4.0（2026-03-19）：补充InventorySourcePriority表的字段说明

---

## 八、排程运行编排与结果读模型（2026-05-13 v5.0.25 新增）

> **阶段说明**：§八.1 `ScheduleExplanationFact` 阶段一即用（最小骨架）；§八.2~八.4 三张读模型表阶段一即用（Task 落库后异步生成）。Batch 3 DDL 补齐完整约束与索引。

### 八.1 ScheduleExplanationFact（结构化原因事实表）（2026-05-13 v5.0.25 阶段一最小骨架）

**所属库**：APS_Production  
**业务用途**：排程决策的**结构化原因事实层**；1号位在内存推演过程中产出 `ExplanationFactDraft`（不直接写 DB），由 2号位与 Task/Pegging 同批次批量落库；供 AI / 前端深钻 / 多版本比较复用；⚠️ 与 `ExplainTrace`（轻量 Task 级追踪日志）**共存不替代**（详见 §5.2 注解）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 事实ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联 PlanVersion 表；分区键 | 1 |
| ScheduleRunId | 排程运行ID | INT | 外键关联 ScheduleRun 表 | 1 |
| ObjectType | 对象类型 | NVARCHAR(50) | `ORDER`（订单）/ `TASK`（任务）/ `RESOURCE`（资源）/ `STAGE`（阶段）；说明这条原因事实作用于哪类对象 | ORDER |
| OrderId | 订单ID | BIGINT | 外键关联 Order 表；ObjectType=ORDER/TASK 时填充 | 100 |
| TaskId | 任务ID | BIGINT | 外键关联 Task 表；ObjectType=TASK 时填充 | 200 |
| ResourceId | 资源ID | INT | 外键关联 Resource 表；ObjectType=RESOURCE/TASK 时填充 | 5 |
| StageCode | 阶段代码 | NVARCHAR(50) | 相关阶段代码；ObjectType=STAGE 时填充 | ASSEMBLE |
| ReasonCode | 原因代码 | NVARCHAR(100) | 结构化枚举（阶段一初始10个，口径须 0号位审批冻结）：`RESOURCE_CAPACITY_WAIT`（资源等容）/ `MATERIAL_SHORTAGE`（物料短缺）/ `PRECEDENCE_WAIT`（前序等待）/ `FROZEN_ZONE_LOCK`（冻结区锁定）/ `ROUTING_FALLBACK`（工艺降级）/ `STAGE_LEADTIME_FALLBACK`（阶段提前期降级）/ `BOM_DEGRADE`（BOM降级）/ `CROSS_ORG_HANDOFF`（跨组织移交）/ `PRIORITY_LOWER_THAN_OTHERS`（优先级抢占）/ `DUE_DATE_RISK`（纳期风险） | RESOURCE_CAPACITY_WAIT |
| Severity | 严重等级 | NVARCHAR(20) | `INFO` / `WARNING` / `ERROR`；对应影响程度 | WARNING |
| ImpactHours | 影响小时数 | DECIMAL(18,4) | 该原因导致的延误或影响小时数；可为 0 | 4.5 |
| EvidenceJson | 原因证据 | NVARCHAR(MAX) | JSON 格式，外壳结构稳定（含 evidenceType / summary / details）；各 ReasonCode 的 details 字段 schema 随阶段演进，阶段一不冻结 | {"evidenceType":"CAPACITY","summary":"资源MC-001等待4.5h","details":{}} |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间（与 Task 批次同步） | 2026-03-05 02:05:00 |

**技术要点**：
- `EvidenceJson` 外壳字段：`evidenceType`（NVARCHAR）/ `summary`（可读摘要）/ `details`（内部 schema 随阶段演进）
- 分区键与 Task / Pegging 对齐（按 `PlanVersionId`）；批量落库由 2号位统一处理
- 1号位**禁止直接写 DB**——内存产出 `ExplanationFactDraft` 列表传递给 2号位

---

### 八.2 OrderScheduleSummary（订单级排程摘要表）（2026-05-13 v5.0.25 阶段一即用）

**所属库**：APS_Production  
**业务用途**：每个订单在当前 PlanVersion 下的排程结果摘要；2号位在 Task 落库后异步后处理生成；供阶段一页面/战报即用，阶段二 Scenario 比较直接对比此表；**不参与排程内核计算**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 摘要ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联 PlanVersion 表；分区键 | 1 |
| OrderId | 订单ID | BIGINT | 外键关联 Order 表 | 100 |
| OrderNo | 订单号 | NVARCHAR(50) | 冗余字段，避免 JOIN | SO-20260305-001 |
| PlannedCompletionTime | 计划完工时间 | DATETIME2 | 该订单最后一道工序的 PlannedEndTime | 2026-03-18 16:00:00 |
| CustomerDueDate | 客户要求交期 | DATE | 从 Order 冗余 | 2026-03-20 |
| DelayHours | 延期小时数 | DECIMAL(18,4) | PlannedCompletionTime - CustomerDueDate 换算；提前为负值 | -48.0 |
| RiskLevel | 风险等级 | NVARCHAR(20) | `ON_TRACK`（按期）/ `AT_RISK`（有风险）/ `DELAYED`（已延期）；由 DelayHours 计算 | ON_TRACK |
| PrimaryReasonCode | 主因代码 | NVARCHAR(100) | 该订单最主要的延期/风险原因代码，来源 ScheduleExplanationFact；按 ImpactHours 取最大值 | RESOURCE_CAPACITY_WAIT |
| IsVipImpacted | 是否影响VIP | BIT | 1=该订单属于 VIP 客户且存在风险 | 0 |
| CustomerTier | 客户分级 | NVARCHAR(20) | 从 Order 冗余，供排序 | VIP |
| CreatedAt | 创建时间 | DATETIME2 | 后处理生成时间 | 2026-03-05 02:20:00 |

---

### 八.3 ResourceLoadSummary（资源负荷摘要表）（2026-05-13 v5.0.25 阶段一即用）

**所属库**：APS_Production  
**业务用途**：每个资源在当前 PlanVersion 下按日期粒度的负荷摘要；2号位在 Task 落库后异步后处理生成；供阶段一资源负荷看板 / 瓶颈识别即用；**不参与排程内核计算**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 摘要ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联 PlanVersion 表；分区键 | 1 |
| ResourceId | 资源ID | INT | 外键关联 Resource 表 | 5 |
| ResourceCode | 资源代码 | NVARCHAR(50) | 冗余字段，避免 JOIN | MC-001 |
| SummaryDate | 汇总日期 | DATE | 粒度：每日 | 2026-03-06 |
| LoadHours | 负荷小时数 | DECIMAL(18,4) | 该资源当日所有 Task 的 Duration 合计 | 7.5 |
| AvailableHours | 可用小时数 | DECIMAL(18,4) | 该资源当日日历可用时间（扣除维护/假期） | 8.0 |
| LoadRate | 负荷率 | DECIMAL(10,4) | LoadHours / AvailableHours；> 1.0 表示超载 | 0.9375 |
| IsBottleneck | 是否瓶颈 | BIT | 1=当日负荷率 >= 瓶颈阈值（默认 0.90）；阈值在 BusinessRuleConfig 配置 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 后处理生成时间 | 2026-03-05 02:22:00 |

---

### 八.4 PlanKpiSummary（计划版本 KPI 汇总表）（2026-05-13 v5.0.25 阶段一即用）

**所属库**：APS_Production  
**业务用途**：每个 PlanVersion 的版本级总指标；2号位在 Task 落库后异步后处理生成；供阶段一 KPI 仪表盘、阶段二 ScenarioObjectiveScore 直接引用，**不重新扫 Task 明细**；**不参与排程内核计算**

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | KPI记录ID | INT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联 PlanVersion 表；唯一索引 | 1 |
| ScheduleRunId | 排程运行ID | INT | 外键关联 ScheduleRun 表 | 1 |
| TotalOrders | 订单总数 | INT | 本次排程覆盖的订单数 | 5000 |
| OnTimeOrders | 准时订单数 | INT | RiskLevel = ON_TRACK 的订单数 | 4620 |
| OnTimeRate | 准交率 | DECIMAL(10,4) | OnTimeOrders / TotalOrders | 0.9240 |
| DelayedOrders | 延期订单数 | INT | RiskLevel = DELAYED 的订单数 | 180 |
| MaxDelayHours | 最大延期小时 | DECIMAL(18,4) | 所有延期订单中最大 DelayHours | 72.5 |
| VipDelayedOrders | VIP延期订单数 | INT | CustomerTier=VIP 且 RiskLevel=DELAYED 的订单数 | 2 |
| AvgLoadRate | 平均负荷率 | DECIMAL(10,4) | 所有资源所有日期 LoadRate 的加权平均 | 0.7830 |
| BottleneckResourceCount | 瓶颈资源数 | INT | 至少存在一天 IsBottleneck=1 的资源数量 | 3 |
| WipEstimateHours | WIP估算工时 | DECIMAL(18,4) | 计划窗口内在制品总工时估算；阶段一简化计算 | 12500.0 |
| CreatedAt | 创建时间 | DATETIME2 | 后处理生成时间 | 2026-03-05 02:25:00 |

**技术要点**：
- 阶段二 `ScenarioObjectiveScore` 直接引用本表的 `OnTimeRate` / `DelayedOrders` / `MaxDelayHours` / `VipDelayedOrders` / `AvgLoadRate` / `BottleneckResourceCount` 填充评分值
- 每个 `PlanVersionId` 唯一一条记录；若重算则 UPSERT

---

---

## 第九节：MES 生产进度快照（v5.0.41 新增）

**定位**：APS 本地存储从 ODS MES 进度汇总视图同步的工单/工序/大工艺进度快照，供 1号位排程引擎全量重算前扣减已完成量，独立于现货库存链路。

**V1 设计决策（不可随意修改）**：
- 不接 MES 每条报工明细，只接 ODS 汇总后的工序级/大工艺级进度
- 工序识别主字段 = `OperationName`（MES工序名称），不以 MES 工序编码为主
- Task/Pegging 全量重算口径：随新 `PlanVersionId` 每日全量重生成；MES 进度不匹配历史 TaskId
- 生产进度快照绝对不写入 `InventoryBalance`

**负责人**：ODS 统一视图由5号位收口；APS本地快照同步（sp_Sync*）由2号位实现

**StageScopeType / RepWorksetId 与 MES 报工链路的设计边界**：
- `StageScopeType`（EDGE / ROOT）是 BOM 侧 `APS_BOM_STAGE_PATH_RAW` 的字段，**不属于** MES 进度快照三表；1号位消费 MES 快照时直接按 `StageCode` + `OperationName` 匹配 Task，不依赖 `StageScopeType`；
- `RepWorksetId`（= `MIN(Workset.Id) WHERE RequestDetailId+Level=1`）存储在 `OrderBomRequestLink`，保证每个 `RequestDetailId` 对应唯一一条 ROOT 大工艺路径，**避免多订单共享同一 BOMNO 时大工艺路径查询断链**；MES 进度快照通过 `ProductionInstructionNo` 关联订单，不直接引用 `RepWorksetId`；
- **V1 不建** `ERP_APS_OrderStatus_View`：订单完工/取消状态由 `Order_Canonical.Status`（OPEN / CLOSED / CANCELLED）控制，CLOSED/CANCELLED 订单不进入 Task/Pegging；MES 进度快照仅提供 `RemainingQty` 用于当日剩余量计算，不替代订单状态管理；MES 报工仅用于 APS 剩余任务计算，不用于订单关闭判断。

---

### §9.1 MESWorkOrderSnapshot（MES工单快照）

**定位**：记录"一个生产指示号对应哪些 MES 工单"的追溯关系快照。消费方：1号位（工单追溯）；3号位（进度看板）。

**来源**：`sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime)` 从 `ODS.MES_APS_WorkOrder_View` 同步。

| 字段名 | 中文名 | 数据类型 | 说明 | 示例 |
|--------|--------|----------|------|------|
| Id | 主键 | BIGINT | 自增主键 | 1 |
| ScheduleRunId | 排程运行ID | INT NOT NULL | 关联 ScheduleRun.Id；MES进度快照必须绑定具体排程运行；快照分区键，每次排程全量替换 | 101 |
| ProductionInstructionNo | 生产指示号 | NVARCHAR(100) | 对应 APS Order 中的生产指示号；Task 透传此字段 | PI-2026-00123 |
| MESWorkOrderNo | MES工单号 | NVARCHAR(100) | MES 系统中的工单号 | WO-MES-9988 |
| MaterialCode | 物料编码 | NVARCHAR(100) | MES 工单生产的物料编码 | MAT-A001 |
| PlannedQty | 计划数量 | DECIMAL(18,4) | MES 工单计划生产数量 | 100.0000 |
| WorkOrderStatus | 工单状态 | NVARCHAR(50) | MES 工单当前状态（如 RELEASED / IN_PROGRESS / CLOSED） | IN_PROGRESS |
| SourceUpdatedAt | 源更新时间 | DATETIME2 NULL | MES 中该工单最后更新时间 | 2026-06-12 01:30:00 |
| DataCutoffTime | 数据截止时间 | DATETIME2 | 由调度器在 ScheduleRun 创建时统一确定并传入；三个快照 SP 必须使用同一个值 | 2026-06-12 00:40:00 |
| CreatedAt | 快照生成时间 | DATETIME2 | APS 生成该快照记录的时间（`DEFAULT GETDATE()`） | 2026-06-12 00:40:12 |

**索引**：`IX_MESWorkOrderSnapshot_Run_InstructionNo (ScheduleRunId, ProductionInstructionNo)`

---

### §9.2 OperationProgressSnapshot（工序进度快照）

**定位**：记录工序级生产进度，供1号位按工序维度扣减已完成 Task 数量。**汇总颗粒度**：生产指示号 + MES工单号 + 物料编码 + 工序名称 + 大工艺阶段码。

**来源**：`sp_SyncOperationProgressSnapshot(@ScheduleRunId, @DataCutoffTime)` 从 `ODS.MES_APS_OperationProgress_View` 同步。

| 字段名 | 中文名 | 数据类型 | 说明 | 示例 |
|--------|--------|----------|------|------|
| Id | 主键 | BIGINT | 自增主键 | 1 |
| ScheduleRunId | 排程运行ID | INT NOT NULL | 关联 ScheduleRun.Id；MES进度快照必须绑定具体排程运行；快照分区键 | 101 |
| ProductionInstructionNo | 生产指示号 | NVARCHAR(100) | 对应 APS Order 中的生产指示号 | PI-2026-00123 |
| MESWorkOrderNo | MES工单号 | NVARCHAR(100) | 所属 MES 工单号 | WO-MES-9988 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 工序对应物料编码 | MAT-A001 |
| OperationName | 工序名称 | NVARCHAR(200) | **V1 工序识别主字段**（MES工序名称）；不以 MES 工序编码为主匹配 | 车削 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | APS 使用的大工艺阶段编码；格式 `{工厂}_{类别}`，取自 StageDict | TJ_MACH |
| StageName | 大工艺名称 | NVARCHAR(100) NULL | 大工艺中文名称（冗余便于展示） | 机加工 |
| PlannedQty | 计划数量 | DECIMAL(18,4) | 该工序计划数量 | 100.0000 |
| GoodQty | 良品完成数量 | DECIMAL(18,4) | 截至数据截止时间，该工序累计良品完成数量 | 60.0000 |
| ScrapQty | 报废数量 | DECIMAL(18,4) NULL | 累计报废数量（可选字段；来源不提供则 NULL） | 2.0000 |
| ReworkQty | 返工数量 | DECIMAL(18,4) NULL | 累计返工数量（可选字段；来源不提供则 NULL） | NULL |
| RemainingQty | 剩余数量 | DECIMAL(18,4) | **计算列（PERSISTED）**：`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END`；1号位消费此字段裁剪剩余 Task | 40.0000 |
| LastReportTime | 最后报工时间 | DATETIME2 NULL | 该工序最后一次报工时间 | 2026-06-12 01:15:00 |
| SourceUpdatedAt | 源更新时间 | DATETIME2 NULL | MES 汇总数据最后更新时间 | 2026-06-12 01:20:00 |
| DataCutoffTime | 数据截止时间 | DATETIME2 | 由调度器在 ScheduleRun 创建时统一确定并传入；三个快照 SP 必须使用同一个值；如 ODS 视图提供 SourceUpdatedAt，按 SourceUpdatedAt <= @DataCutoffTime 控制切片 | 2026-06-12 00:45:00 |
| CreatedAt | 快照生成时间 | DATETIME2 | APS 生成该快照记录的时间 | 2026-06-12 00:45:18 |

**唯一约束**：`UQ_OperationProgressSnapshot_Key (ScheduleRunId, ProductionInstructionNo, MESWorkOrderNo, MaterialCode, OperationName, StageCode)`

**索引**：`IX_OperationProgressSnapshot_Run_InstructionNo (ScheduleRunId, ProductionInstructionNo)`

**技术要点**：
- `RemainingQty` 为持久化计算列，不需 SP 显式计算；1号位直接读取
- `ScrapQty` / `ReworkQty` 可选字段：ODS 来源能提供则填写，不能提供则保留 NULL，**不阻断快照同步**
- `OperationName` 是 APS 与 MES 工序对应的唯一主匹配字段（不以 MES 工序编码为准，编码不跨大工艺稳定）；1号位按 `OperationName + StageCode` 联合识别工序，将 `GoodQty` 对应到排程生成的 Task 上扣减剩余量；
- 生产进度快照不影响 `InventoryBalance`——二者在 `ScheduleContext` 中并存但严格独立

---

### §9.3 StageProgressSnapshot（大工艺进度快照）

**定位**：记录大工艺级别生产进度（工序进度的上层汇总），供1号位按大工艺阶段维度扣减已完成量（性能优先级高于工序级）。**汇总颗粒度**：生产指示号 + 物料编码 + 大工艺阶段码。

**来源**：`sp_SyncStageProgressSnapshot(@ScheduleRunId, @DataCutoffTime)` 从 `ODS.MES_APS_StageProgress_View` 同步。

| 字段名 | 中文名 | 数据类型 | 说明 | 示例 |
|--------|--------|----------|------|------|
| Id | 主键 | BIGINT | 自增主键 | 1 |
| ScheduleRunId | 排程运行ID | INT NOT NULL | 关联 ScheduleRun.Id；MES进度快照必须绑定具体排程运行；快照分区键 | 101 |
| ProductionInstructionNo | 生产指示号 | NVARCHAR(100) | 对应 APS Order 中的生产指示号 | PI-2026-00123 |
| MaterialCode | 物料编码 | NVARCHAR(100) | 大工艺阶段对应物料编码 | MAT-A001 |
| StageCode | 大工艺阶段码 | NVARCHAR(20) | APS 使用的大工艺阶段编码；格式 `{工厂}_{类别}`，取自 StageDict | TJ_MACH |
| StageName | 大工艺名称 | NVARCHAR(100) NULL | 大工艺中文名称（冗余便于展示） | 机加工 |
| PlannedQty | 计划数量 | DECIMAL(18,4) | 该大工艺阶段计划数量 | 100.0000 |
| GoodCompletedQty | 良品完成数量 | DECIMAL(18,4) | 截至数据截止时间，该阶段累计良品完成数量 | 60.0000 |
| ScrapQty | 报废数量 | DECIMAL(18,4) NULL | 阶段累计报废数量（可选字段） | 2.0000 |
| ReworkQty | 返工数量 | DECIMAL(18,4) NULL | 阶段累计返工数量（可选字段） | NULL |
| RemainingQty | 剩余数量 | DECIMAL(18,4) | **计算列（PERSISTED）**：`CASE WHEN PlannedQty - ISNULL(GoodCompletedQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodCompletedQty,0) END`；1号位**优先**消费此字段（粒度粗、性能好） | 40.0000 |
| LastReportTime | 最后报工时间 | DATETIME2 NULL | 该阶段最后一次报工时间 | 2026-06-12 01:15:00 |
| SourceUpdatedAt | 源更新时间 | DATETIME2 NULL | MES 汇总数据最后更新时间 | 2026-06-12 01:20:00 |
| DataCutoffTime | 数据截止时间 | DATETIME2 | 由调度器在 ScheduleRun 创建时统一确定并传入；三个快照 SP 必须使用同一个值；如 ODS 视图提供 SourceUpdatedAt / LastReportTime，按 <= @DataCutoffTime 控制切片 | 2026-06-12 00:50:00 |
| CreatedAt | 快照生成时间 | DATETIME2 | APS 生成该快照记录的时间 | 2026-06-12 00:50:22 |

**唯一约束**：`UQ_StageProgressSnapshot_Key (ScheduleRunId, ProductionInstructionNo, MaterialCode, StageCode)`

**索引**：`IX_StageProgressSnapshot_Run_InstructionNo (ScheduleRunId, ProductionInstructionNo)`

**技术要点**：
- `RemainingQty` 为持久化计算列；1号位消费优先级：`StageProgressSnapshot > OperationProgressSnapshot`
- 与 `OperationProgressSnapshot` 的关系：同一 `ProductionInstructionNo + MaterialCode + StageCode` 下，`StageProgressSnapshot.GoodCompletedQty` 原则上应与 `OperationProgressSnapshot.GoodQty` 的阶段汇总保持同口径一致；如因 MES 来源表、报工完整性或阶段关闭状态导致不一致，V1 以 `StageProgressSnapshot` 作为大工艺级扣减优先输入，差异登记 `APS_ETL_Log`，不阻断排程

---

**END OF DOCUMENT**

---

# 第三部分：APS_Auth 库（权限与审批）

## 📌 APS_Auth 库概述

**库名**：`APS_Auth`  
**用途**：独立的权限与审批数据库，负责：
1. 用户认证与授权
2. 角色与权限管理
3. 数据范围控制（工厂/产品族/资源组）
4. 审批流管理
5. 审计日志记录

**核心设计原则**：
- ✅ 独立部署，与业务库物理隔离
- ✅ APS_Production 库通过跨库查询访问
- ✅ 支持 RBAC + 数据范围 + 审批 + 审计四层模型
- ✅ 审计日志只增不改不删

**相关文档**：
- **《APS_权限与审批系统实施方案_v1.0》**：完整的实施方案
- **《APS_Auth数据库字段说明文档_v1.0》**：详细的字段说明

---

## 一、用户与角色管理

### 1.1 User（用户表）

**所属库**：APS_Auth  
**业务用途**：存储系统用户信息，支持用户认证和授权

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| UserId | 用户ID | INT | 主键，自增 | 5 |
| LoginName | 登录名 | NVARCHAR(100) | 唯一，用于登录 | zhang.san |
| DisplayName | 显示名称 | NVARCHAR(100) | 用户真实姓名 | 张三 |
| PasswordHash | 密码哈希 | NVARCHAR(500) | BCrypt 加密存储 | $2a$10$... |
| Email | 邮箱 | NVARCHAR(200) | 用户邮箱 | zhang.san@company.com |
| PhoneNumber | 手机号 | NVARCHAR(50) | 用户手机号 | 13800138000 |
| IsEnabled | 是否启用 | BIT | 1=启用，0=禁用 | 1 |
| LastLoginAt | 最后登录时间 | DATETIME2 | 记录最后登录时间 | 2026-03-24 08:30:00 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-20 10:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 记录更新时间 | 2026-03-24 08:30:00 |

**唯一约束**：`UNIQUE (LoginName)`

---

### 1.2 Role（角色表）

**所属库**：APS_Auth  
**业务用途**：定义系统角色，支持 RBAC 权限模型

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| RoleId | 角色ID | INT | 主键，自增 | 3 |
| RoleCode | 角色编码 | NVARCHAR(100) | 唯一标识 | aps.planner |
| RoleName | 角色名称 | NVARCHAR(100) | 角色显示名称 | 计划员 |
| Description | 角色描述 | NVARCHAR(500) | 角色职责说明 | 发起排程、查看计划、调整任务 |
| IsSystemRole | 是否系统角色 | BIT | 1=系统预置，0=自定义 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-20 10:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 记录更新时间 | 2026-03-24 08:30:00 |

**唯一约束**：`UNIQUE (RoleCode)`

**预置角色**：
- `aps.admin.system`：系统管理员
- `aps.admin.aps`：APS 管理员
- `aps.planner`：计划员
- `aps.supervisor.workshop`：车间主管
- `aps.coordinator.material`：物料协同人员
- `aps.viewer.management`：管理层查看
- `aps.service.api`：系统接口账号

---

### 1.3 Permission（权限表）

**所属库**：APS_Auth  
**业务用途**：定义系统权限点，支持细粒度权限控制

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| PermissionId | 权限ID | INT | 主键，自增 | 1 |
| PermissionCode | 权限编码 | NVARCHAR(100) | 唯一标识，格式：aps.module.action | aps.plan.run |
| PermissionName | 权限名称 | NVARCHAR(100) | 权限显示名称 | 发起排程 |
| Module | 所属模块 | NVARCHAR(50) | Plan/Task/CTP/Config/Approval等 | Plan |
| Description | 权限描述 | NVARCHAR(500) | 权限说明 | 允许用户发起排程计算 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-20 10:00:00 |

**唯一约束**：`UNIQUE (PermissionCode)`

**核心权限码**（23个）：
- 计划模块：`aps.plan.view`、`aps.plan.run`、`aps.plan.publish`
- 任务模块：`aps.task.view`、`aps.task.adjust`、`aps.task.freeze.override`、`aps.task.started.override`
- CTP模块：`aps.ctp.view`、`aps.ctp.evaluate`、`aps.ctp.commit`
- 配置模块：`aps.config.supply_context.view`、`aps.config.supply_context.edit`、`aps.config.inventory_rule.view`、`aps.config.inventory_rule.edit`、`aps.config.resource.view`、`aps.config.resource.edit`
- 审批模块：`aps.approval.view`、`aps.approval.approve`
- 审计模块：`aps.audit.view`
- 用户管理：`aps.user.view`、`aps.user.manage`、`aps.role.view`、`aps.role.manage`

---

### 1.4 UserRole（用户角色关联表）

**所属库**：APS_Auth  
**业务用途**：记录用户与角色的多对多关系

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 关联ID | BIGINT | 主键，自增 | 1 |
| UserId | 用户ID | INT | 外键关联User表 | 5 |
| RoleId | 角色ID | INT | 外键关联Role表 | 3 |
| AssignedAt | 分配时间 | DATETIME2 | 角色分配时间 | 2026-03-20 10:00:00 |

**唯一约束**：`UNIQUE (UserId, RoleId)`

---

### 1.5 RolePermission（角色权限关联表）

**所属库**：APS_Auth  
**业务用途**：记录角色与权限的多对多关系

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 关联ID | BIGINT | 主键，自增 | 1 |
| RoleId | 角色ID | INT | 外键关联Role表 | 3 |
| PermissionId | 权限ID | INT | 外键关联Permission表 | 1 |
| AssignedAt | 分配时间 | DATETIME2 | 权限分配时间 | 2026-03-20 10:00:00 |

**唯一约束**：`UNIQUE (RoleId, PermissionId)`

---

## 二、数据范围控制

### 2.1 DataScopePolicy（数据范围策略表）

**所属库**：APS_Auth  
**业务用途**：定义数据范围策略（工厂/产品族/资源组织维度）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| PolicyId | 策略ID | INT | 主键，自增 | 5 |
| ScopeType | 范围类型 | NVARCHAR(50) | Factory/ProductFamily/ResourceOrgGroup（v5.0替代原ResourceGroup） | Factory |
| ScopeValue | 范围值 | NVARCHAR(100) | 具体的工厂ID/产品族ID/资源组织维度ID | 1 |
| Description | 描述 | NVARCHAR(200) | 范围说明 | 工厂1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-20 10:00:00 |

**唯一约束**：`UNIQUE (ScopeType, ScopeValue)`

---

### 2.2 UserDataScope（用户数据范围表）

**所属库**：APS_Auth  
**业务用途**：记录用户的数据范围权限

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 关联ID | BIGINT | 主键，自增 | 1 |
| UserId | 用户ID | INT | 外键关联User表 | 5 |
| PolicyId | 策略ID | INT | 外键关联DataScopePolicy表 | 5 |
| AssignedAt | 分配时间 | DATETIME2 | 数据范围分配时间 | 2026-03-20 10:00:00 |

**唯一约束**：`UNIQUE (UserId, PolicyId)`

---

### 2.3 RoleDataScope（角色数据范围表）

**所属库**：APS_Auth  
**业务用途**：记录角色的数据范围权限

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 关联ID | BIGINT | 主键，自增 | 1 |
| RoleId | 角色ID | INT | 外键关联Role表 | 3 |
| PolicyId | 策略ID | INT | 外键关联DataScopePolicy表 | 5 |
| AssignedAt | 分配时间 | DATETIME2 | 数据范围分配时间 | 2026-03-20 10:00:00 |

**唯一约束**：`UNIQUE (RoleId, PolicyId)`

---

## 三、审批流管理

### 3.1 ApprovalFlow（审批流表）

**所属库**：APS_Auth  
**业务用途**：记录审批流实例

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| ApprovalFlowId | 审批流ID | BIGINT | 主键，自增 | 5 |
| ApprovalType | 审批类型 | NVARCHAR(100) | FreezeTaskAdjust/SOSacrifice/StartedTaskAdjust | FreezeTaskAdjust |
| ObjectType | 对象类型 | NVARCHAR(50) | Task/Order/Config | Task |
| ObjectId | 对象ID | NVARCHAR(100) | 被审批对象的ID | 12345 |
| ApplicantUserId | 申请人ID | INT | 外键关联User表 | 5 |
| Status | 审批状态 | NVARCHAR(50) | Pending/Approved/Rejected | Pending |
| CurrentNodeSeq | 当前节点序号 | INT | 当前审批到第几级 | 1 |
| Reason | 申请理由 | NVARCHAR(1000) | 申请说明 | 紧急插单需要调整冻结区任务 |
| PlanVersionId | 计划版本ID | INT | 关联的排程版本 | 123 |
| RequestData | 请求数据 | NVARCHAR(MAX) | JSON格式的请求数据 | {"taskId":12345,...} |
| CreatedAt | 创建时间 | DATETIME2 | 审批流创建时间 | 2026-03-24 09:45:00 |
| CompletedAt | 完成时间 | DATETIME2 | 审批流完成时间 | 2026-03-24 10:00:00 |

**索引**：`IX_ApprovalFlow_Status`、`IX_ApprovalFlow_ApplicantUser`

---

### 3.2 ApprovalNode（审批节点表）

**所属库**：APS_Auth  
**业务用途**：定义审批流的节点（多级审批）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| NodeId | 节点ID | BIGINT | 主键，自增 | 1 |
| ApprovalFlowId | 审批流ID | BIGINT | 外键关联ApprovalFlow表 | 5 |
| NodeSeq | 节点序号 | INT | 审批顺序（1, 2, 3...） | 1 |
| ApproverRoleId | 审批角色ID | INT | 外键关联Role表 | 4 |
| Status | 节点状态 | NVARCHAR(50) | Pending/Approved/Rejected | Pending |
| CreatedAt | 创建时间 | DATETIME2 | 节点创建时间 | 2026-03-24 09:45:00 |

**索引**：`IX_ApprovalNode_Flow`

---

### 3.3 ApprovalRecord（审批记录表）

**所属库**：APS_Auth  
**业务用途**：记录每个节点的审批操作

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| RecordId | 记录ID | BIGINT | 主键，自增 | 1 |
| NodeId | 节点ID | BIGINT | 外键关联ApprovalNode表 | 1 |
| ApproverUserId | 审批人ID | INT | 外键关联User表 | 10 |
| Decision | 审批决定 | NVARCHAR(50) | Approved/Rejected | Approved |
| Comment | 审批意见 | NVARCHAR(1000) | 审批说明 | 同意调整，注意控制影响范围 |
| ApprovedAt | 审批时间 | DATETIME2 | 审批操作时间 | 2026-03-24 09:50:00 |

**索引**：`IX_ApprovalRecord_Node`

---

### 3.4 ApprovalRule（审批规则表）

**所属库**：APS_Auth  
**业务用途**：定义审批规则（基于工厂/产品族/资源组织维度）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| RuleId | 规则ID | INT | 主键，自增 | 1 |
| ApprovalType | 审批类型 | NVARCHAR(100) | FreezeTaskAdjust/SOSacrifice/StartedTaskAdjust | FreezeTaskAdjust |
| ScopeType | 范围类型 | NVARCHAR(50) | Factory/ProductFamily/ResourceOrgGroup/ALL（v5.0替代原ResourceGroup） | Factory |
| ScopeValue | 范围值 | NVARCHAR(100) | 具体值，ALL表示全局 | 1 |
| NodeSeq | 节点序号 | INT | 第几级审批 | 1 |
| ApproverRoleId | 审批角色ID | INT | 外键关联Role表 | 4 |
| IsActive | 是否启用 | BIT | 1=启用，0=禁用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 规则创建时间 | 2026-03-20 10:00:00 |

**索引**：`IX_ApprovalRule_Type_Scope`

---

## 四、审计日志

### 4.1 AuditLog（审计日志表）

**所属库**：APS_Auth  
**业务用途**：记录所有关键操作的审计日志（只增不改不删）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 日志ID | BIGINT | 主键，自增 | 12345 |
| UserId | 用户ID | INT | 外键关联User表 | 5 |
| ActionCode | 动作编码 | NVARCHAR(100) | 权限码，如 aps.plan.run | aps.plan.run |
| ObjectType | 对象类型 | NVARCHAR(50) | PlanVersion/Task/Order/Config | PlanVersion |
| ObjectId | 对象ID | NVARCHAR(100) | 被操作对象的ID | 123 |
| Result | 操作结果 | NVARCHAR(50) | Success/Failed/Denied | Success |
| RequestData | 请求数据 | NVARCHAR(MAX) | JSON格式的请求数据（不含敏感信息） | {"factoryId":1,...} |
| ResponseData | 响应数据 | NVARCHAR(MAX) | JSON格式的响应数据（不含敏感信息） | {"planVersionId":123,...} |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) | 失败时的错误描述 | NULL |
| ClientIp | 客户端IP | NVARCHAR(50) | 请求来源IP | 192.168.1.100 |
| UserAgent | 用户代理 | NVARCHAR(500) | 浏览器信息 | Mozilla/5.0... |
| PlanVersionId | 计划版本ID | INT | 关联的排程版本（可选） | 123 |
| BatchNo | 批次号 | NVARCHAR(50) | 关联的批次号（可选） | B20260324001 |
| OccurredAt | 发生时间 | DATETIME2 | 操作发生时间 | 2026-03-24 08:30:00 |

**索引**：
- `IX_AuditLog_User`：按用户查询
- `IX_AuditLog_Action`：按动作查询
- `IX_AuditLog_Time`：按时间查询
- `IX_AuditLog_Result`：按结果查询

**技术要点**：
- 审计日志只增不改不删
- 敏感数据（如密码）不记录到 RequestData/ResponseData
- 按月分表或归档历史数据（可选）

---

**文档版本历史**：
- v1.0（2026-03-11）：初始版本，仅包含APS库的核心业务表
- v2.0（2026-03-11）：同步v2.4架构修复，新增库存三层架构和物料映射表
- v3.0（2026-03-18）：补充ODS库、视图契约、跨库包装视图、ETL相关表，形成完整版
- v4.0（2026-03-19）：补充InventorySourcePriority表的字段说明
- v4.1（2026-03-24）：修正PlanVersion和MES_API_BOM_Request表字段，新增APS_Auth数据库完整说明
- v4.2（2026-03-24）：修复ERP_Master_View字段定义不一致问题，补全4个缺失字段，修正MaterialSupplyContext同步逻辑

### SupplyFact_Pipeline MOCK 数据（v5.0.46）

开发/测试环境可使用 `SourceSystem='MOCK'`、`BatchNo='MOCK_YYYYMMDD'` 写入正式字段联调。后续真实数据接入不改 Pegging 主流程。

---

**编制人**：2号位（技术负责人）  
**审核人**：0号位（业务负责人）  
**批准日期**：2026年3月24日
