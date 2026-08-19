# APS 各类基础数据分层承接与演变总表（定稿版）

**版本**：v3.29  
**日期**：2026-06-23  
**文档性质**：架构总纲级参考文档  
**维护责任人**：2号位（技术负责人）

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

本文档是 **APS数据架构的总纲级参考文档**，提供5类核心数据（BOM、Material、Routing、Order、Inventory）的分层演进全景图。

**文档特点**：
- ✅ **横向对比**：将5类数据放在同一张表中，清晰展示每类数据的完整演进路径
- ✅ **全局视角**：从源头层→契约层→工作集层→业务落地层→内存消费层，一目了然
- ✅ **职责明确**：每个环节都标注了负责人/Owner，便于协作分工

**与其他文档的关系**：⚠️ **更新日期：2026-06-15**
- **本文档**：横向对比，全局视角，架构总纲（当前 v3.27）
- **《APS_数据架构与防腐层设计方案_v5.0》 v1.31**：纵向深入，实施细节，存储过程设计、防腐层契约
- **《APS_数据库字段说明文档_v5.0》 v5.0.42**：字典参考，表结构定义，字段说明
- **《APS_数据库表结构设计_v5.0.sql》 v5.0.42**：DDL 脚本，数据库表结构
- **《BOM_Workset_生成与错误处理技术方案》 v1.2**：BOM Workset/StageDetail 推导与异常降级矩阵 + BOM↔Routing 三层对接模型（R01~R27 经验库实现）
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：执行分工，Socket-Plug 职责划分

**使用建议**：
1. 新成员入职时，先读本文档理解数据演进全景
2. 开发实施时，参考防腐层设计方案和数据库字段说明文档
3. 协作分工时，参考职责分工变更说明文档

---

> **口径说明**：本表按当前最新管理口径整理，其中 **“ODS 递归展开到 `MES_APS_BOM_Workset` 由 5号位负责实现”** 已写入 **Owner** 列。若正式采纳该调整，建议同步修订《职责分工变更说明》、内部契约和相关实施清单，避免后续分工冲突。

---

## 1. 分层定义

| 层级 | 定义 | 这一层主要做什么 | 典型对象 |
|---|---|---|---|
| **源头层** | ERP / MES 现有生产库、业务中间表、主数据表 | 保存原始业务事实，不直接给 APS 使用 | ERP 订单中间表、ERP master、MES BOM 物理表、MES 工艺表、MES 库存表 |
| **契约层 / 防腐层** | ODS（`MES_Integration`）中的标准视图或物化边表契约（v3.18：BOM 防腐层已升级为物化边表；其余仍为视图） | 把源系统脏乱差物理表整理成稳定接口，隔离源库表结构变化 | `MES_BOM_Edge_Active`（v3.18新增，物化防腐边表；兼容视图 `MES_BOM_View` = `SELECT * FROM MES_BOM_Edge_Active`，不再作为递归展开对象）、`ERP_Master_View`、`MES_Material_View`、~~`MES_APS_Routing_View`~~(v5.0废弃)→`MES_APS_Routing_Operation_View`+`MES_APS_Routing_Dependency_View`+`APS_OperationResourceEligibility_View`+`MES_APS_Routing_Stage_View`(v3.5新增，v3.6定位调整为阶段字典)、`MES_APS_Resource_View`(v5.0新增；v3.11 命名统一，原名 `APS_Resource_View`）、预留 `EAM_APS_Resource_View`（未来 EAM 上线）、`ERP_Inventory_View`、`MES_Inventory_View` |
| **工作集 / 计算层** | ODS 工作集表 + APS 本地 ETL / 缓存表 | 按当前计划窗口筛选、递归展开、去重、映射、汇聚、局部计算 | `MES_API_BOM_Request*`、`MES_APS_BOM_Workset`(v3.8 +`ChildRequiredFactory`)、`MES_APS_BOM_Workset_StageDetail`(v3.6新增，v3.7升级为统一阶段路径结果表EDGE+ROOT)、**`MES_APS_BOM_Workset_Issues`**(v3.8新增诊断独立表)、`APS_BOM_RAW`(v3.8 +`ChildRequiredFactory`)、`APS_BOM_STAGE_PATH_RAW`(v3.6新增)、`MaterialMapping`、`MaterialSupplyContext`、`InventorySupplyCandidate`、`InventoryBalance`。**v3.8旁路**：`vw_MES_BOM_Stage_Enriched`（ODS 派生便利视图，**非防腐层**，仅 ODS 内部消费，APS 本地不做对称视图） |
| **APS 业务落地层** | APS 本地业务表 | 形成排程和业务查询可直接使用的标准业务实体 | `Material`、~~`Routing`~~(v5.0废弃)→`RoutingOperation`+`RoutingDependency`+`OperationResourceEligibility`+`RoutingPlanningParam`+`RoutingStage`(v3.5新增，v3.6定位调整为阶段字典)+`StageLeadTimeParam`(v3.6新增)、`Order_Canonical`、`Order`、`Resource`(外部镜像)+`ResourcePlanningContext`+`ResourceOrgGroup`；v3.17新增运行编排与结果读模型：`PlanVersion`（结果版本）+`ScheduleRun`（运行编排，阶段一即用）+`Scenario`/`SimulationRun`（仿真场景与算法记录，阶段二预留）+`ScenarioObjectiveScore`（多目标评分，阶段二预留）+`ScheduleExplanationFact`（结构化原因事实，阶段一最小骨架）+`OrderScheduleSummary`/`ResourceLoadSummary`/`PlanKpiSummary`（读模型三张表，阶段一即用） |
| **内存消费层** | `ScheduleContext` / `DataSnapshot` | 将 APS 本地数据装成纯内存快照，供 1号位引擎推演 | `ScheduleContext`、`Order` POCO、`Material` POCO、`RoutingGraph` POCO（v5.0：工序节点+依赖边+资源能力） |

---

## 2. 各类基础数据分层承接与演变总表

| 数据类别 | 源头 | 契约层 / 防腐层处理 | 工作集 / 计算层处理 | APS 落地层 | 输出到哪 | 给谁用 | 负责人 / Owner |
|---|---|---|---|---|---|---|---|
| **BOM**（2026-04-23 v3.8更新） | ERP / MES 现有 BOM 物理表；第一层展开依赖订单/MTS 下发的 `BOMNO`，第二层及以后依赖物料型号的有效版本继续向下展开 | （v3.18）由 `sp_RefreshBOMEdgeActive` 从 ERP/MES 多源 BOM 物理表刷新到 `MES_BOM_Edge_Active`（物化防腐边表）；字段标准化：`BOMNO / ParentMaterialCode / ChildMaterialCode / Quantity / IsActive / IsDefaultVersion / ParentProcRefCode / ChildProcRefCode / ChildSourceHintCode`（值域0-11）+ 追溯字段 `SourceSystem / SourceBOMId / SourceLineNo / RefreshBatchNo / RefreshedAt`；执行 ProcessCode 左补零 6 位 / ChildSourceHintCode 值域标准化 / 双源唯一默认版本裁决（IsDefaultVersion=1 全局唯一）；**`MES_BOM_View` 降为兼容视图** = `SELECT * FROM MES_BOM_Edge_Active`，不再直接 UNION 源表；刷新失败时 `MES_BOM_Edge_RefreshLog` 记录并禁止 Workset 使用半刷新数据；这一层只做标准化+裁决，不做 90 天活跃窗口筛选 | ① 从 `Order_Canonical` 划 90 天活跃根；② 按订单粒度写入 `MES_API_BOM_Request_Detail`（v5.0.31：唯一约束 `(BatchNo, OrderCanonicalId)`；`RequestedBOMNO` 可空；展开完成后 2号位生成 `OrderBomRequestLink` 索引表）；③ 在 ODS 迭代展开到 `MES_APS_BOM_Workset`（`sp_ExpandBOMBatch_vNext` 读 `MES_BOM_Edge_Active`，WHILE 循环 #Frontier，#EntryResolved 入口预解析，透传3辅助字段）；④ **5号位后置回填**：`ChildRequiredStageCode` + **`ChildRequiredFactory`**（v3.8 R17 Produce→厂映射） + 写入`StageDetail`（EDGE子件供给路径+ROOT根产品完工路径，v3.7统一阶段路径） + **异常登记到 `MES_APS_BOM_Workset_Issues`**（v3.8 R27：LEAF/FACTORY_MISMATCH/NO_STAGE/CYCLIC_BOM 等）；⑤ Issues 降级登记（v3.9 口径）：全部异常写入 `MES_APS_BOM_Workset_Issues`（含 `DegradeAction` 标签），**批次永远走 READY，永不阻塞**；⑥ APS 拉取到 `APS_BOM_RAW`（+`ChildRequiredFactory`）+ `APS_BOM_STAGE_PATH_RAW`（阶段顺序明细）；⑦ 本地计算 LLC、叶子、路径等运行时属性 | `APS_BOM_RAW` 作为夜间活跃窗口本地缓存；如保留 3.5 `BOM` 表，建议定位为**当前计划窗口标准 BOM 关系缓存**，而非全量 4000 万主数据总账 | `APS_BOM_RAW`、可选的窗口级 `BOM` 缓存、最终 `ScheduleContext` | 2号位 `IDataLoader`、1号位排程引擎、5号位 Pegging/缺料规则、0号位/业务复核人员（Issues）、当前窗口前端树展示 | **契约 Owner：0号位审批 / 5号位实现 `MES_BOM_View`**；**递归展开+R17回填+Issues写入：5号位**；**APS 拉取 / LLC / 本地缓存 / `vw_MES_BOM_Stage_Enriched` 维护：2号位**；Issues 复核：0号位/业务复核人员 |
| **Master / Material**（2026-04-01 v4.0更新） | ERP `master` 表（`MasterID` + 仓库）+ MES 物料表（`MES_ID`） | 通过 `ERP_Master_View`、`MES_Material_View`（v1.3同构化：MES_ID→MasterID别名、Location→Warehouse别名、移除MaterialType、新增供给属性字段）暴露**双源同构**主数据标准接口；APS 侧通过 `ext_ERP_Master_View`、`ext_MES_Material_View`（字段完全一致）访问 | 通过统一参数化存储过程 `sp_SyncMasterData(@SourceType)`（v4.0双源统一，原 `sp_SyncERPMasterData` + `sp_SyncMESMaterialData` 合并）将双源数据三表协同同步：Material（MaterialType由APS按MaterialCode前缀推导）+ MaterialMapping（统一SourceID+Warehouse，消除ERP/MES字段分叉）+ MaterialSupplyContext（仓库级供给上下文：SupplyMode、DefaultProductionDeptCode、LeadTimeDays、SafetyStock、InventoryManagementMode，SCD Type 2） | `MaterialMapping` 作为桥表（统一SourceID+Warehouse）；`Material` 作为 APS 统一业务物料表（含 Spec，废弃 IsPurchased/SafetyStock/LeadTimeDays，MaterialType由前缀推导）；`MaterialSupplyContext` 承载仓库级业务上下文（含InventoryManagementMode） | `MaterialMapping`、`MaterialSupplyContext`、`Material`、最终 `ScheduleContext.Materials` | 订单装载、BOM 映射、Routing 映射、库存汇聚、1号位引擎、5号位库存/优先级规则 | **契约 Owner：0号位审批 / 5号位实现 ERP/MES 主数据插头**；**映射与 APS 装载：2号位** |
| **Routing / 工艺**（2026-04-01 v5.0重构，v5.0.1变更 2026-04-02，v3.6更新 2026-04-13） | MES 工艺表；存在新旧两套结构：新结构带 `MES_ID`，老结构只有物料型号 | v5.0重构：原 `MES_APS_Routing_View` 废弃，拆分为三个独立视图：`MES_APS_Routing_Operation_View`（工序节点）+ `MES_APS_Routing_Dependency_View`（工序依赖边，支持并行/串行混合）+ `APS_OperationResourceEligibility_View`（工序资源能力关系，替代ResourceGroup）；**v5.0.1变更：ODS视图输出`MES_ID`+`Model`（非MaterialCode），老结构由3号位ETL处理为MES_ID**；V1默认路径约束（RouteCode='DEFAULT', PathId=1）；**v3.6：`MES_APS_Routing_Stage_View`定位调整为阶段字典/标准阶段语言，不作为排程权威阶段顺序源** | 基本不走递归计算；主要是清洗、标准化、按契约对齐、增量 Upsert；**v5.0.1：2号位装载时通过`MaterialMapping(Source='MES', SourceID=MES_ID)`映射得到`MaterialId`** | APS 侧通过 3 个 ext_ 包装视图分别装载到：`RoutingOperation`（工序节点）+ `RoutingDependency`（工序依赖）+ `OperationResourceEligibility`（工序资源能力）；`RoutingPlanningParam` 承接 APS 本地排程参数（MinBatchSize/MaxBatchSize） | `RoutingOperation`、`RoutingDependency`、`OperationResourceEligibility`、`RoutingPlanningParam`、最终 `ScheduleContext.RoutingGraph` | 1号位引擎（面对工艺图而非序列表）、Task 生成、资源分配、前端工艺查询 | **契约与清洗 Owner：3号位**；**APS 包装视图与装载（含MES_ID→MaterialId映射）：2号位** |
| **Order / 订单**（2026-04-09 v3.3补充） | ERP 订单中间表 / 生产指示中间表，按小时增量 Upsert，携带 `MaterialCode / BOMNO / Quantity / DueDate` + 源事实字段（`TransportMode / CustomerName / MTS_InstructionNo / IssueDate / OriginalDueDate / ReceivedQty(仅MTS)`） | 先进入 `ERP_Order_Staging` 做验证与清洗，含源事实字段透传 + APS衍生字段标准化（`CustomerSegment`「通过CustomerCodeMap本地映射推导，v5.0.24澄清」/ `SalesOrderCategory` / `DemandMaturityStatus`「收窄为PRE_CONFIRMED/FORECAST，v5.0.24」 / `CustomerTier` / **`DelayStatus`**「v5.0.24新增」，由`sp_ValidateAndPromoteOrders`负责）；再进入 `Order_Canonical` 作为 APS 防腐层核心订单表【业务澄清：OrderType v5.0.24重分类（SO/MTO→`SALES_ORDER`；MTS/SS/SS_U→`PRODUCTION_INSTRUCTION`）；FactoryCode为APS衍生字段】 | 每天 00:00 从 `Order_Canonical` 划定 90 天活跃根；同时保留白天订单增量进入 `Order_Canonical`，供后续批次或实时插单使用 | 从 `Order_Canonical` 转换到 `Order` 业务表，补齐 `MaterialId / ProductFamilyId / FactoryId / DomainKey / PriorityScore` + 透传业务字段（`TransportMode / CustomerName / CustomerSegment / SalesOrderCategory / DemandMaturityStatus / CustomerTier / **DelayStatus** / MTS_InstructionNo / IssueDate / OriginalDueDate / ReceivedQty`） | `Order_Canonical`、`Order`、最终 `ScheduleContext.Orders` | 2号位快照融合、1号位引擎、5号位 Pegging、前端订单查询与版本追溯 | **订单同步与 APS 装载 Owner：2号位**；**活跃根口径、订单类型口径：0号位** |
| **管道供给 / PipelineSupply**（2026-05-09 v3.15 新增；v3.28 2026-06-18 多仓映射+SourceRowKey+统一视图+DataCutoffTime+白天预留） | ERP 厂间物流运输在途（来源系统 ERP）；未来按需扩展：`ERP_VMI_View` / `ERP_PurchaseInTransit_View` 等 | ODS 层 `ERP_InterplantInTransit_View`（所属库：MES_Integration；5号位维护）暴露14字段 ERP 厂间在途数据契约视图，`FactoryCode`=目的工厂，`Quantity`=剩余在途数量，`ETA`=ODS原始事实，**`MasterID` 为 ERP 物料映射主字段**；APS 层通过 `ext_ERP_InterplantInTransit_View`（所属库：APS_Production；2号位维护；显式列字段，禁止 SELECT *）跨库访问；未来新增供给类型只需新增视图，不改现有契约。**⚠️ 契约锁定规则**：ODS 契约视图字段结构为强契约，V1.1/V2 允许调整ODS视图内部SELECT表达式、FROM、JOIN、WHERE及必要转换逻辑；对外14字段不变，禁止修改字段顺序、类型、名称 | **V1 空跑骨架（当前实装）**：`sp_SyncPipelineSupply` 只执行 `TRUNCATE TABLE SupplyFact_Pipeline` + 写 SUCCESS 日志；**不读取**正式 ERP 在途数据；**不执行** Material / Factory / ProductFamily 映射；**不应用** `SupplyAvailabilityRule`；`ScheduleContext.PipelineSupplies` = 空集合；**V1 排程只使用现货库存链路（`InventoryBalance` / `InventoryAvailableSupplyDetail`）**。**V1.1/V2 预留**：真实链路（Step1读取 `ext_PipelineSupply_Source_View` 统一输入视图 → Step2多仓映射 MasterID+StorageCode→MaterialMapping.Warehouse_Norm → Step3 OUTER APPLY 规则唯一胜出 → Step4事务批次写入 `SupplyFact_Pipeline`） | `SupplyFact_Pipeline`（V1 结果为空，仅保留表框架；v5.0.42 新增 `SourceMasterID` / `SourceFactoryCode` / `SourceDocumentLineNo` / `SourceUpdatedAt` 四个来源追溯字段）；`SupplyAvailabilityRule`（管道供给规则表，**V1 不调用**，为 V1.1/V2 激活预留） | `SupplyFact_Pipeline`，最终 `ScheduleContext.PipelineSupplies`（`IReadOnlyList<PipelineSupplyItem>`） | 1号位引擎（可用供给核算）、5号位规则配置、前端在途供给查询 | **ODS 契约视图 Owner：ERP DBA / 5号位实现**；**APS 包装+装载 Owner：2号位**（`sp_SyncPipelineSupply`）；**规则配置：5号位** |
| **Inventory / 库存** | ERP 库存在 `master` / 库存事实中；MES 自建物料库存存在 MES 库存表 | 通过 `ERP_Inventory_View`（含 WarehouseCode）、`MES_Inventory_View`（含 LocationCode + WarehouseCode）暴露双源库存标准接口；⚠️ 契约View中的 MaterialCode 为辅助字段，权威挂接在 MaterialMapping；APS 侧通过 `ext_ERP_Inventory_View`、`ext_MES_Inventory_View` 访问 | APS 本地采用**六层库存架构**（v5.0.40）：① `InventoryFact_ERP`（保留 MasterID + WarehouseCode + FactoryCode；v5.0.39）、`InventoryFact_MES`（保留 MES_ID + WarehouseCode[V1主链] + Location[历史保留] + FactoryCode；v5.0.39）原始事实层，保持物理追溯能力；② 通过 `MaterialMapping` 进行物理身份挂接到 `InventorySupplyCandidate`（库存链路中第一次统一到 MaterialCode，保留仓库/库位维度；IsEligible 默认 0，白名单模式）；③ 应用 `InventoryAvailabilityRule`（统一库存可用规则表，`IsAvailable`+`Priority`，替代旧 `ProductFamilyInventoryScope`+`InventorySourceRule`），由 `sp_SyncInventorySnapshot(@BatchNo)` 六步 ETL 驱动；④ 命中规则（IsAvailable=1）写入 `InventoryAvailableSupplyDetail`（规则命中后、汇总前的可用供给明细层；保留 AvailabilityRuleId + RulePriority + InventorySupplyCandidateId；`InventoryBalance` 从该表汇总；排程判断总量读 `InventoryBalance`，解释来源和扣减顺序读本表；v5.0.40 新增）；⑤ 从明细层汇总生成 `InventoryBalance`（`ProductFamilyId`=库存使用上下文≠物料自身产品族；`BatchNo`=快照批次标签≠订单消耗记录；TRUNCATE 全量替换）；⑥ 排程前必须一次性全量预加载合法库存入内存（`ScheduleContext.InventoryBalances`） | `InventoryFact_ERP`、`InventoryFact_MES`、`InventorySupplyCandidate`、`InventoryAvailabilityRule`、`InventoryAvailableSupplyDetail`（v5.0.40）、`InventoryBalance` | `InventoryBalance`（总量余额）、`InventoryAvailableSupplyDetail`（明细层，按优先级扣减时用）、最终 `ScheduleContext.InventoryBalances` | 5号位库存分配/缺料规则、2号位快照融合、1号位引擎、前端库存查询 | **契约 Owner：0号位审批 / ERP DBA、MES DBA 实现库存插头**；**规则配置与筛选：5号位**；**库存事实装载与 Balance 生成：2号位** |
| **排程运行编排**（v3.28 2026-06-23 四表职责收敛） | APS 内部触发（Hangfire 定时器 / API 主动触发 / 算法引擎） | N/A（APS 内部对象）；四表分工：`ScheduleRun`（运行过程/RUNNING-COMPLETED-FAILED）→ `PlanVersion`（结果版本/BUILDING-CANDIDATE-ACTIVE-ARCHIVED-FAILED）；`Scenario`（试算场景+假设+目标+选中版本）+ `SimulationRun`（算法执行记录）；`MANUAL_RESCHEDULE` 不要求先建 `Scenario` | 00:38 创建 ScheduleRun(RUNNING, 确定DataCutoffTime) → 02:00 创建 PlanVersion(BUILDING, SourceScheduleRunId) → 排程内核执行 → 落库+ScheduleRun=COMPLETED+PlanVersion=ACTIVE | `ScheduleRun`（阶段一即用，无OutputPlanVersionId）+ `PlanVersion`（SourceScheduleRunId反查）+ `Scenario`（阶段二骨架）+ `SimulationRun`（阶段二骨架，无PlanVersionId）+ `ScenarioObjectiveScore`（阶段二骨架）| `FULL_SCHEDULE` → PlanVersion.Status=ACTIVE（自动）；其他RunType → CANDIDATE（须显式激活）。正式采用直接看 `PlanVersion.Status=ACTIVE` | 统一排程触发入口；阶段二多方案比较；阶段三 Skill API | **ScheduleRun 创建：3号位**；**PlanVersion 创建+Status更新：2号位**；**Scenario管理：3号位**；**RunType/SectionType 枚举：0号位审批** |
| **MES工单链路**（2026-06-12 v3.26 新增） | MES 生产执行系统（各大工艺）工单数据 | ODS 层 5号位建立 `MES_APS_WorkOrder_View`（各大工艺工单视图 UNION ALL 汇总），字段模板：`ProductionInstructionNo / MESWorkOrderNo / MaterialCode / PlannedQty / WorkOrderStatus / SourceUpdatedAt` | 2号位执行 `sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime)` 同步到 APS 本地快照 | `MESWorkOrderSnapshot` | `ScheduleContext.MESWorkOrderSnapshots`（可选，用于生产指示号→MES工单追溯） | 1号位（工单追溯）；3号位（进度看板） | **ODS 统一视图收口：5号位**；**APS 本地快照同步：2号位** |
| **MES工序进度汇总链路**（2026-06-12 v3.26 新增） | MES 各大工艺报工数据（分散在各大工艺数据库/业务表） | ODS 层：各大工艺报工数据 → 各大工艺标准化子视图（`MES_APS_OperationProgress_{大工艺}_View`，加工类由2号位建/组装类由5号位建）→ `UNION ALL` → `ODS.MES_APS_OperationProgress_View`（5号位收口）；字段模板：`ProductionInstructionNo / MESWorkOrderNo / MaterialCode / OperationName / StageCode / StageName / PlannedQty / GoodQty / ScrapQty? / ReworkQty? / LastReportTime / SourceUpdatedAt`；⚠️ **V1 不接每条报工明细，只接 ODS 汇总后的工序级进度**；**工序识别主字段 = OperationName** | 2号位执行 `sp_SyncOperationProgressSnapshot(@ScheduleRunId, @DataCutoffTime)` 同步；`RemainingQty` 持久化计算列：`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END` | `OperationProgressSnapshot` | `ScheduleContext.OperationProgressSnapshots`；1号位全量重算前按工序进度扣减已完成量，只生成剩余 Task | 1号位排程引擎（剩余 Task 裁剪）；3号位（进度看板） | **各大工艺子视图（加工类：2号位 / 组装类：5号位）**；**ODS UNION ALL 统一收口：5号位**；**APS 快照同步 + ScheduleContext 装载：2号位** |
| **MES大工艺进度汇总链路**（2026-06-12 v3.26 新增） | MES 各大工艺报工数据（同上） | ODS 层：各大工艺报工数据 → 各大工艺大工艺进度子视图（`MES_APS_StageProgress_{大工艺}_View`）→ `UNION ALL` → `ODS.MES_APS_StageProgress_View`（5号位收口）；字段模板：`ProductionInstructionNo / MaterialCode / StageCode / StageName / PlannedQty / GoodCompletedQty / ScrapQty? / ReworkQty? / LastReportTime / SourceUpdatedAt`；汇总颗粒度 = 生产指示号+物料编码+大工艺阶段码 | 2号位执行 `sp_SyncStageProgressSnapshot(@ScheduleRunId, @DataCutoffTime)`；`RemainingQty` 持久化计算列：`CASE WHEN PlannedQty - ISNULL(GoodCompletedQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodCompletedQty,0) END` | `StageProgressSnapshot` | `ScheduleContext.StageProgressSnapshots`；1号位全量重算前按大工艺进度扣减已完成量 | 1号位排程引擎；3号位 | **同上**；**ODS 统一视图收口：5号位**；**APS 快照同步：2号位** |
| **排程结果解释与读模型**（2026-05-13 v3.17 阶段二三接缝新增） | 1号位排程引擎内存计算产物（ExplanationFact）+ Task / Pegging 落库后聚合计算（Summary 三张表）——APS 内部计算产生，无外部源 | N/A（APS 内部计算产物）；⚠️ **职责边界**：`ExplainTrace`（现有，轻量 Task 级追踪日志，文本战报输入）≠ `ScheduleExplanationFact`（v3.17 新增，结构化原因事实层，供 AI / 页面 / 比较复用）——共存不替代 | 1号位在排程推演内存中产出 `ExplanationFactDraft`（含 ObjectType / OrderId / TaskId / ResourceId / StageCode / ReasonCode / Severity / ImpactHours / EvidenceJson）；不落 DB，传递给 2号位；`EvidenceJson` 外壳稳定，各 `ReasonCode` 内部 schema 随阶段演进 | `ScheduleExplanationFact`（2号位与 Task/Pegging 同批次落库，阶段一最小骨架即用）；`PlanKpiSummary`（版本级总 KPI）+ `OrderScheduleSummary`（订单级摘要：计划完工/延期/风险/主因/VIP标记）+ `ResourceLoadSummary`（资源×日期：负荷小时/可用小时/负荷率/是否瓶颈）；**首先服务阶段一页面/战报/KPI**，随后扩展到 Scenario 比较 + AI 查询；⚠️ **不参与排程计算**，2号位在 Task 落库后异步后处理生成 | `ScheduleExplanationFact`；`PlanKpiSummary`；`OrderScheduleSummary`；`ResourceLoadSummary`；最终消费：页面 / 战报 / 仿真比较 / Skill API | 3号位/前端（阶段一）；Scenario 多方案比较（阶段二）；Agent（阶段三） | **1号位（内存产出 ExplanationFactDraft，不直接写 DB）**；**2号位（持久化 ExplanationFact + 生成 Summary 读模型）**；**3号位/应用层（消费战报/页面）**；**ReasonCode 枚举口径：0号位审批** |

---

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
库存是 **双源事实 + 契约防腐 + 候选供给池 + 规则裁决 + 可用供给明细 + 余额汇总 + 内存加载**，六层链路，不是单表硬扛；完整链路：`InventoryFact_ERP`/`InventoryFact_MES`（WarehouseCode 主链，物理事实保留）→ `MaterialMapping` 桥接 → `InventorySupplyCandidate`（候选供给池，IsEligible=0 初始）→ `InventoryAvailabilityRule`（统一可用规则，胜出规则裁决，替代旧 `ProductFamilyInventoryScope`+`InventorySourceRule`，v5.0.39 删除）→ **`InventoryAvailableSupplyDetail`（可用供给明细层，v5.0.40 新增；保留 SourceSystem/StorageCode/AvailabilityRuleId/RulePriority/InventorySupplyCandidateId；`InventoryBalance` 负责"总量够不够"，本表负责"从哪里来、按什么顺序扣"）** → `InventoryBalance`（按 MaterialCode+ProductFamilyId+FactoryId 汇总，`ProductFamilyId`=库存使用上下文；`BatchNo`=快照标签）→ 内存预加载；排程判断总量读 `InventoryBalance`，解释来源或分优先级扣减读 `InventoryAvailableSupplyDetail`；V1 管道供给链空运行，不影响余额；排程前必须一次性预加载进内存，排程中严禁再按需查库。  
**`InventoryAvailableSupplyDetail` 定位补充**：它位于 `InventoryAvailabilityRule` 与 `InventoryBalance` 之间，不改变库存规则模型，而是保存规则裁决后的仓库级、来源级、优先级明细。`InventoryBalance` 从该表汇总生成。若未来只做总量判断，理论上可直接从规则生成 `InventoryBalance`；但当前 APS 需支持库存来源解释和扣减优先级，因此 V1 保留本表。

### 3.6 管道供给 / PipelineSupply（2026-05-09 v3.15 新增；v5.0.40 V1口径明确）
管道供给是**并行于现货六层主链的独立供给链，不替代 `InventoryBalance`**。`SupplyAvailabilityRule` 是供给主题独立规则表（控制 IncludeFlag + LeadTimeOffset），**不是统一规则引擎**；现货链统一使用 `InventoryAvailabilityRule`（旧 `ProductFamilyInventoryScope`+`InventorySourceRule` 已于 v5.0.39 删除）。

**V1 实际执行（空跑骨架，当前实装）**：`sp_SyncPipelineSupply` 只执行 `TRUNCATE TABLE SupplyFact_Pipeline` + 写 SUCCESS 日志；不读取正式 ERP 在途数据；不执行 Material / Factory / ProductFamily 映射；**不应用 `SupplyAvailabilityRule`**；`ScheduleContext.PipelineSupplies` = 空集合；不影响现有排程。**V1 排程只使用现货库存链路：`InventoryBalance` / `InventoryAvailableSupplyDetail`。**

**V1.1/V2 完整链路**（v3.28 正式补齐）：

`ext_PipelineSupply_Source_View`（多来源UNION ALL统一输入，含SourceSystem派生列）
→ `sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, @RowsAffected OUTPUT, @ErrorMessage OUTPUT)`
→ MasterID+StorageCode→MaterialMapping.Warehouse_Norm 多仓映射（MappingCount=1门禁）
→ FactoryCode INNER JOIN Factory（失败整行跳过）
→ OUTER APPLY SupplyAvailabilityRule 唯一胜出（IsActive+有效期内+Priority ASC+具体度 DESC+Id ASC）
→ 事务 DELETE+INSERT 批次原子替换
→ `SupplyFact_Pipeline`（SourceRowKey幂等+RuleId/RulePriority规则追溯+4个来源追溯字段）
→ `ScheduleContext.PipelineSupplies`（V1空集合；V1.1/V2夜间按 BatchNo+IsActive=1+AvailableTime IS NOT NULL 加载）

已补：多仓映射、SourceRowKey、DataCutoffTime、规则唯一胜出、规则追溯、事务、ETA空值过滤、多来源统一视图、白天实时仅预留。

### 3.7 排程运行编排（v3.28 四表职责收敛）
四表分工：**`ScheduleRun`**（这次运行怎么跑 / 运行状态 / 数据切片）→ **`PlanVersion`**（每套结果版本 / 版本生命周期状态：BUILDING→ACTIVE）→ Task/Pegging/Summary。仿真扩展：**`Scenario`**（试算假设和目标）→ **`SimulationRun`**（算法执行记录）→ 多个 `PlanVersion`（候选版本）。正式采用直接看 `PlanVersion.Status = ACTIVE`。`PlanVersion.SourceScheduleRunId` 反向追溯到运行记录。`ScheduleRun` 已删除 `OutputPlanVersionId`；凌晨全量：00:38 创建 ScheduleRun(RUNNING)→02:00 创建 PlanVersion(BUILDING)→成功→PlanVersion(ACTIVE)。阶段二 `Scenario/SimulationRun` 只建骨架表，不实装业务逻辑。`Scenario.SelectedPlanVersionId` 候选版本生成后回填。

### 3.7a 规则与参数引擎（v3.29 2026-06-23 新增）
规则与参数引擎是 APS 业务策略中枢。通过 `RuleSetVersion`/`ParameterSetVersion`/`StrategyProfileVersion` 形成可发布、可追溯的策略包。`ScheduleRun.StrategyProfileVersionId` 记录本次运行采用哪套策略包；规则参数是运行输入，不是 PlanVersion 输出。已发布版本不可原地修改。V1 不做万能脚本引擎 / ScheduleConfigSnapshot / 完整审批流闭环。5号位只执行规则不维护；1号位只消费 ScheduleContext 中的规则参数结果。凌晨全量：00:38 绑定默认策略包 → 02:00 加载到 ScheduleContext.RuleConfig/SchedulingParams。

### 3.8 排程结果读模型（2026-05-13 v3.17 新增）
排程结果读模型是 **非排程内核、纯读取用途的物化汇总层**，由 2号位在 Task / Pegging 落库完成后异步后处理生成：`OrderScheduleSummary`（订单级：计划完工时间 / 延期小时 / 风险等级 / 主因代码 / 是否影响 VIP）+ `ResourceLoadSummary`（资源×日期粒度：负荷小时 / 可用小时 / 负荷率 / 是否瓶颈）+ `PlanKpiSummary`（版本级总指标：准交率 / 延期订单数 / 最大延期小时 / VIP延期 / 平均负荷率 / 瓶颈数 / WIP估算）。**首要价值**：阶段一页面、战报、KPI 仪表盘即可受益，不必等仿真功能上线；**延伸价值**：阶段二 Scenario 比较时直接对比汇总结果（不需重扫 Task 明细），阶段三 Skill API 直接查这三张表实现秒级响应。`ScheduleExplanationFact` 是独立的结构化原因事实层——1号位在内存推演中以 `ExplanationFactDraft` 形式产出（含 ObjectType / ReasonCode / ImpactHours / EvidenceJson），由 2号位与 Task/Pegging 同批次落库；`EvidenceJson` 内部 schema 随阶段演进，阶段一不冻结。⚠️ **`ExplainTrace` ≠ `ExplanationFact`**：前者是轻量 Task 级追踪日志（供文本战报输入），后者是结构化原因事实（供 AI / 多版本比较 / 前端深钻）——共存，职责不同，不替代。

### 3.9 MES工单链路（2026-06-12 v3.26 新增）
MES工单链路 **不是排程计算用途，而是"一个生产指示号对应哪些 MES 工单"的追溯桥梁**。链路：`ODS.MES_APS_WorkOrder_View → APS.MESWorkOrderSnapshot → ScheduleContext.MESWorkOrderSnapshots（可选）`。ODS 视图由 5号位收口（整合各大工艺 MES 工单数据），2号位在每次排程前执行 `sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime)` 落本地快照。V1 设计口径：只接汇总后的工单级关系，不接每条工单变更日志；`ScheduleRunId` 作为快照分区键，全量替换本次运行的旧快照。

### 3.10 MES工序进度汇总链路（2026-06-12 v3.26 新增）
MES 报工数据分散在不同大工艺中，因此 **ODS 层采用"各大工艺标准化子视图 + UNION ALL 统一契约视图"的分层结构**。链路：各大工艺报工表 → `MES_APS_OperationProgress_{大工艺}_View`（各大工艺标准化子视图）→ `UNION ALL` → `ODS.MES_APS_OperationProgress_View` → `APS.OperationProgressSnapshot` → `ScheduleContext.OperationProgressSnapshots`。**V1 设计决策三点**：① 工序识别主字段 = `OperationName`，不以 MES 工序编码为主（MES 编码不跨大工艺稳定）；② `RemainingQty` 为持久化计算列（`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END`），1号位全量重算前依此裁剪剩余 Task；③ 生产进度快照**绝对不进入 `InventoryBalance`**，二者在 `ScheduleContext` 中并存但严格独立。

### 3.11 MES大工艺进度汇总链路（2026-06-12 v3.26 新增）
大工艺进度是工序进度的上一层汇总，**颗粒度 = 生产指示号 + 物料编码 + 大工艺阶段码**。链路：各大工艺报工表 → `MES_APS_StageProgress_{大工艺}_View` → `UNION ALL` → `ODS.MES_APS_StageProgress_View` → `APS.StageProgressSnapshot` → `ScheduleContext.StageProgressSnapshots`。**消费优先级**：1号位排程引擎优先按大工艺进度扣减已完成量（粒度粗、性能好），需要工序级精度时再查 `OperationProgressSnapshot`。**Task/Pegging 全量重算口径**（v3.26 写死）：Task 和 Pegging 随新的 `PlanVersionId` 每日全量重新生成；MES 进度只用于计算当日剩余 Task 数量，**不匹配历史 TaskId**；Pegging 只描述当前 `PlanVersionId` 内 Task 与供需关系，不跨版本复用。**EAM V1**：`EAM_APS_Resource_View` 预留占位，V1 不读取 EAM 数据，不生成设备不可用窗口，不影响当前快照同步流程。

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
12. **【v3.11.2 新增；v3.12 修正定位】ProcessCode 防腐墙红线**：6 位 `ProcessCode` 及其派生字段 `ActualFactoryCode / TrusteeProcCode / WarehouseRole / IsRetouch` **严格只在 ODS 层活**；`APS_Production` 库严禁出现这些字段（无论是表列、视图列、还是存储过程变量）。APS 排程只消费"语义字段"：通过 `StageCode → StageDict` 获取 `FactoryCode / StageCategory`；委外/受托等 ERP 特征由 2 号位预计算落 `StageLeadTimeParam` 等配置表吸收。违反即打穿防腐墙。ODS 侧的消费方也一律通过 `MES_ProcessCode_View`（Socket-Plug 契约视图）读取，不得绕过直查 `ProcessCodeDict` 物理表。**🔄 v3.12 修正**：ProcessCodeDict 不是「ERP 工序对照表 ODS 镜像」，而是 **「APS 自维护的 ODS 增强工序字典」**——APS 系统管理员人工维护 + 0 号位审批；不参与 `sp_SyncMasterData` 自动同步流程；新增 `StageCode` 增强列承担 ProcessCode → StageCode 共享映射（5 号位 / 2 号位统一查 `MES_ProcessCode_View.StageCode`）。
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
21. **【v3.13 新增】APS_Production 本地强关联对象才落 `FactoryId`（INT FK）**：仅 `Order.FactoryId` / `Resource.FactoryId` / `ProductionDepartment.FactoryId`（当前可空）/ `MaterialSupplyContext.FactoryId`（可选辅助）/ `ResourceOrgGroup.FactoryId` 这 5 张 APS_Production 本地表持有 `FactoryId` 外键。其余表通过间接路径归厂（详见 §6 工厂归属关系总表）。
22. **【v3.13 新增】排程主链对象不重复存 `FactoryId`**：`MaterialStageDeptContext` / `RoutingOperation` / `RoutingDependency` / `OperationResourceEligibility` / `ResourcePlanningContext` / `Material` / `MaterialMapping` 均**不直接存** `FactoryCode` 或 `FactoryId`——通过 `ProductionDepartmentId → ProductionDepartment.FactoryId` 或 `ResourceId → Resource.FactoryId` 或 `StageCode → StageDict.FactoryCode` 间接归厂，避免冗余字段和一致性维护负担。
23. **【v3.14 新增，v5.0.32 升级】RequestedBOMNO 可空红线与 ResolvedBOMNO 归属红线**：`RequestedBOMNO`（订单原始携带）可空；NULL=待5号位Workset阶段解析BOM入口；2号位推送 `MES_API_BOM_Request_Detail` 时只填 `RequestedBOMNO`（请求输入字段），不预先解析BOMNO；`ResolvedBOMNO`（展开实际使用的BOMNO）不进 RequestDetail，由 **2号位在 Workset 同步完成后写入 `OrderBomRequestLink.ResolvedBOMNO`**；**BOM入口解析分流由5号位Workset处理阶段负责**，5号位只写 Workset/StageDetail/Issues。`FailureCode`（原因维度）和 `NextActionCode`（动作维度）为两个**独立字段**，**禁止混用**；`SyncStatus` 只表达技术流转状态。
24. **【v3.15 新增；v5.0.40 修正】管道供给链独立红线**：`InventoryBalance` 定义不变；现货库存主链为六层结构（v5.0.40 新增第4层 `InventoryAvailableSupplyDetail`，旧"五层"描述已过时）；`SupplyFact_Pipeline` 是并行独立链，结果为空时不影响现有排程；`SupplyAvailabilityRule` 是管道供给主题规则表，**V1 不调用**，为 V1.1/V2 预留；现货链统一使用 `InventoryAvailabilityRule`（旧 `ProductFamilyInventoryScope` / `InventorySourceRule` 已于 v5.0.39 删除，**禁止**引用这两张已删表）；`ETA` 是 ODS 原始事实字段（禁止修改），`AvailableTime` 是 APS 本地派生字段（= ETA + LeadTimeOffset，由 `sp_SyncPipelineSupply` 计算落库）；两字段**禁止混用**。
25. **【v3.17 新增】ScheduleRun 统一运行编排框架**：`FULL_SCHEDULE`（凌晨全量）/ `MANUAL_RESCHEDULE`（人工重排）/ `LOCAL_RESCHEDULE` / `SIMULATION` / `INSERT_ORDER_WHATIF` 均通过 `ScheduleRun.RunType` 表达，共用同一运行编排对象；`ScheduleRun` 是对现有"直接生成 `PlanVersionId`"流程的**最小包装**，阶段一不改排程内核；`Scenario` 是 SIMULATION/WHATIF 类型运行的上层业务容器，**不是所有非正式运行的总容器**——`MANUAL_RESCHEDULE` 不要求先建 `Scenario`；**禁止将 `ScheduleRun` 与 `PlanVersion` 混淆**（运行对象 ≠ 结果版本）。
26. **【v3.28 更新】仿真/重排产出的版本默认不激活**：非 `FULL_SCHEDULE` 类型产出的 `PlanVersion` 默认状态为 CANDIDATE，**禁止自动激活**；正式采用直接通过 `PlanVersion.Status = ACTIVE` 表示（v3.28 四表收敛，不再使用 `System_Active_Version`）。
27. **【v3.17 新增】`Scenario ≠ PlanVersion ≠ SimulationRun`**：`Scenario` 是 what-if 假设与目标容器（业务对象）；`SimulationRun` 是某次具体算法执行记录；`PlanVersion` 是结果版本快照——三者正交，不可混淆；`Scenario` / `SimulationRun` / `ScenarioObjectiveScore` 阶段一预留建表骨架，阶段二实装；未来多方案比较依赖 `ScenarioObjectiveScore` 对 `PlanKpiSummary` 的复用，**不重新扫 Task 明细**。
28. **【v3.17 新增】排程结果读模型不参与排程内核**：`OrderScheduleSummary` / `ResourceLoadSummary` / `PlanKpiSummary` 属于读模型层，由 2号位在 Task/Pegging 落库后后处理生成，**禁止进入 1号位排程内核**；`ScheduleExplanationFact` 在 1号位内存中以 `ExplanationFactDraft` 形态产出，**1号位禁止直接写数据库**，统一由 2号位与 Task/Pegging 同批次批量落盘；`ExplainTrace`（现有轻量追踪日志）与 `ScheduleExplanationFact`（结构化原因事实）共存不替代，职责层次不同，**禁止合并或混用**。
29. **【v3.26 新增 / v3.27 收窄】`Order_Canonical.Status` 准入口径锁定**：`Order_Canonical.Status` 只有三种业务值：OPEN / CLOSED / CANCELLED；活跃根集合、BOM Request 推送、Order 分区表同步、Task/Pegging 生成均以 `WHERE Status = 'OPEN'` 为唯一准入条件（v3.12 窄口径）；CLOSED / CANCELLED 不得进入 BOM Request 或生成 Task / Pegging；`RELEASED` / `SCHEDULED` / `COMPLETED` 不是当前 APS V1 业务枚举，**禁止**出现在当前正文 SQL 或准入判断中。
30. **【v3.26 新增】生产进度快照绝对不混入 InventoryBalance**：`MESWorkOrderSnapshot` / `OperationProgressSnapshot` / `StageProgressSnapshot` 是排程前扣减已完成量的输入，**不是库存供给**；这三张快照表不得写入 `InventoryBalance`；现货库存链路与 MES 进度链路在 `ScheduleContext` 中并存但**严格独立**，混淆即为严重架构违规。
31. **【v3.26 新增】Task 必须透传生产指示号**：所有由某个 Order/生产指示驱动生成的 Task（含根产品、半成品子件及各大工艺阶段 Task）必须携带 `ProductionInstructionNo`（或 `MTS_InstructionNo`）；**禁止只在根产品 Task 上保留生产指示号而在拆解的子件 Task 上丢失**；1号位禁止生成不可追溯到 `ProductionInstructionNo` 的 Task。
32. **【v3.27 新增】ScheduleRun 必须在 MES 快照同步前创建**：`ScheduleRun` 必须在夜间数据准备阶段（最晚 00:38 前）由 NightlyBatchOrchestrator 创建；00:40 / 00:45 / 00:50 三个 MES 快照同步 SP 必须使用同一 `ScheduleRunId` + `DataCutoffTime`；`ScheduleRun.DataCutoffTime` 是本次排程所有输入数据的统一切片边界；**禁止**三个 SP 各自独立取当前时间；02:00 排程启动时读取已创建记录而非新建。
29. **【v3.20 新增，v3.22 升级】OrderBomRequestLink 查询链路红线**：Order→BOM结构必须通过 `OrderBomRequestLink.ResolvedBOMNO`→`APS_BOM_RAW(BatchNo+BOMNO)` 查询（禁止直接用旧 `BOMNO` 字段站 JOIN）；Order→大工艺路径必须通过 `OrderBomRequestLink.RepWorksetId`→`APS_BOM_STAGE_PATH_RAW.WorksetId` 查询；`APS_BOM_RAW` **禁止新增订单级字段**，保持BOMNO级共享；`APS_BOM_STAGE_PATH_RAW` 无需改动；`OrderBomRequestLink` 由 2号位在 BOM Workset + StageDetail 同步完成后生成（`PullBOMResultFromODSAsync(batchNo, planVersionId)` Step 4）；**数据源**必须为 ODS `MES_APS_BOM_Workset` 聊合，**禁止从 `APS_BOM_RAW` 反查**；`ResolvedBOMNO`=Level=1 Workset.BOMNO；`RepWorksetId`=`MIN(Workset.Id) WHERE RequestDetailId+Level=1`；唯一约束 = `UNIQUE(PlanVersionId, OrderCanonicalId)`（不是 OrderId）；找不到 OrderId 时写 `OrderId=NULL, LinkStatus='SKIPPED'`，不阻断批次；**禁止 BOMResultPullService 内部自查最新 PlanVersion**，planVersionId 由 NightlyBatchOrchestrator 显式传入。

---

## 5. 负责人调整记录

- **当前版调整**：ODS 递归展开到 `MES_APS_BOM_Workset` 由 **5号位** 负责实现。
- **2号位保留职责**：ODS / APS 架构、DDL、APS 拉取、`APS_BOM_RAW`、LLC、本地 `IDataLoader`、验收；**v3.8 新增**：`vw_MES_BOM_Stage_Enriched` 视图维护（仅 ODS 内部）；**v3.18 新增**：`MES_BOM_Edge_Active` / `MES_BOM_Edge_RefreshLog` 表结构与索引、`sp_RefreshBOMEdgeActive` SP 骨架（事务框架 + 刷新日志写入；业务映射步骤 STEP 2+ 由 5号位实现）、`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` SP 框架结构（具体展开逻辑由 5号位实现）。
- **5号位保留职责**：`sp_RefreshBOMEdgeActive` 业务映射逻辑（ProcessCode 左补零 / ChildSourceHintCode 值域 / 双源版本裁决规则 / EffectiveFrom 裁剪）、ERP / MES 主数据插头、ODS 迭代展开（`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext`）、业务规则校验；v3.6新增：`ChildRequiredStageCode`回填 + `StageDetail`写入 + `StageLeadTimeParam`配置维护；v3.7新增：ROOT根产品完工路径推导（Level=1 ParentProcRefCode → 映射标准化 → 多条不一致取最长+记WARNING）；v3.8新增：`ChildRequiredFactory` R17 回填 + `MES_APS_BOM_Workset_Issues` 写入；**v3.9口径更新**：不做批次放行校验，改为"异常降级 + 登记 DegradeAction 标签"，批次永远走 READY；新增 `ProduceToFactoryMap` + `StageDict` 配置表维护；**v3.14新增（v3.22口径更新）**：BOM入口解析分流——从 `MES_API_BOM_Request_Detail` 读取 `RequestedBOMNO`（可空）/`MaterialCode`/`FactoryCode`/`OrderType`/`OrderCanonicalId`（⚠️ v5.0.32 已删除 `BOMNO`/`Model` 字段，禁止继续引用）；有 `RequestedBOMNO` 直接定位 `MES_BOM_Edge_Active` 展开，无 `RequestedBOMNO` 时按 `OrderType + MaterialCode + FactoryCode + BOM边/ProcessCode` 规则解析入口（不从 Model 推导）；`RequestDetailId` 回写到 `MES_APS_BOM_Workset` + `MES_APS_BOM_Workset_Issues`；**v3.18新增**：`sp_EnrichBOMWorkset_vNext` 中每条 Workset 行写 StageDetail 时附带 `WorksetId`（=Workset.Id），保证多路径收敛场景 StageDetail 一一追溯；StageDetail 不存 `RequestDetailId`（经 WorksetIdWorkset.IdRequestDetailId 反查）。
- **0号位/业务复核人员职责**（v3.9 口径更新）：**不做每日值班**，改为**月度巡检** `MES_APS_BOM_Workset_Issues`，统计高频 IssueType + 高频物料，反馈 ERP 维护方批量修正源头数据；ReviewStatus 标记 CONFIRMED/IGNORED/FIXED 闭环；ERROR 类（CYCLIC_BOM）次日晨会过一遍。
- **3号位保留职责**（v5.0更新，v3.6更新，v3.10 新增 StageDict 映射责任）：`MES_APS_Routing_Operation_View` + `MES_APS_Routing_Dependency_View` + `APS_OperationResourceEligibility_View` + `MES_APS_Routing_Stage_View`(v3.5新增，v3.6定位调整为阶段字典) 契约与清洗（原 `MES_APS_Routing_View` 废弃）；**v3.10 新增**：`MES_APS_Routing_Stage_View` 负责将 MES 本地阶段名标准化映射到 `StageDict`（契约视图层完成映射，MES 原生字符串不得直接下沉到 APS 本地表）；`ProcessTypeDict` 骨架期维护（业务启用前保持 IsActive=0）。
- **0号位保留职责**：业务口径审批、活跃根规则、策略参数、异常处理口径；**v3.12 新增**：审批 `ProductionDepartment` / `ProcessCodeDict` / `MaterialStageDeptOverride` 三张人工维护字典表的字段变更与启用/停用。
- **APS 系统管理员（v3.12 新增角色）**：负责 `ProcessCodeDict` 的人工维护（业务字段语义来源 ERP/MES，但本表不参与任何自动同步）；变更走 0 号位审批闭环。
- **2号位 v3.12 增量职责**：
  - `sp_RebuildMaterialStageDeptContext`（**v1 占位 SP，当前未实现**——DDL Step1~6 全 TODO）：设计三触发模式 `FULL` / `INCR` / `PARTIAL`；输入 MSC + Override + ProductionDepartment + MES_ProcessCode_View.StageCode；产出 `MaterialStageDeptContext` + `MaterialStageDeptContext_Issues`；实装前下游消费方应做空表/降级处理
  - `sp_SyncResourceData` 升级：`WorkshopCode` → `ProductionDeptCode`；MERGE 加 `ProductionDepartmentId` 双字典映射 JOIN（FactoryCode + ProductionDeptCode）
  - **取消** v3.11.2 的 `sp_SyncMasterData(@SourceType='ProcessCode')` 同步分支责任（ProcessCodeDict 改为 APS 自维护）
  - 4 个 ODS 契约视图字段升级跟进（`MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `APS_OperationResourceEligibility_View` / `MES_APS_Resource_View` 加 `ProductionDeptCode`；走防腐层审批流程，由 DBA 执行 ALTER VIEW）
  - `MaterialStageDeptOverride` 导入工具：实现 Model → MaterialCode 1:1 解析检查；1:N 拒收并返回明细
- **v3.28 更新职责 - 3号位**：`ScheduleRun` 触发（凌晨 Hangfire = FULL_SCHEDULE；API 接口 = MANUAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF 等）；版本激活：`FULL_SCHEDULE` 完成后 2号位同步更新 `PlanVersion.Status=ACTIVE`；其余类型须 3号位显式将 `PlanVersion.Status` 改为 ACTIVE。`Scenario` 管理入口（阶段二启动时实装）。
- **v3.28 更新职责 - 2号位**：`PlanVersion` 创建+落库（Status BUILDING→落库后更新为 ACTIVE）；`ScheduleRun.Status=COMPLETED` 回填；`ScheduleExplanationFact` 与 Task/Pegging 同批次批量落库；`OrderScheduleSummary` / `ResourceLoadSummary` / `PlanKpiSummary` 后处理生成。
- **v3.17 新增职责 - 1号位**：在排程内存推演过程中产出 `ExplanationFactDraft`（不直接写 DB，传递给 2号位批量落盘）；`ReasonCode` 打标逻辑由 1号位内核实现，初始批 10 个枚举：`RESOURCE_CAPACITY_WAIT` / `MATERIAL_SHORTAGE` / `PRECEDENCE_WAIT` / `FROZEN_ZONE_LOCK` / `ROUTING_FALLBACK` / `STAGE_LEADTIME_FALLBACK` / `BOM_DEGRADE` / `CROSS_ORG_HANDOFF` / `PRIORITY_LOWER_THAN_OTHERS` / `DUE_DATE_RISK`（口径需 0号位审批后冻结）。
- **v3.17 新增口径 - 0号位**：`RunType` 枚举值域审批；`ReasonCode` 枚举口径审批；`Scenario` 业务定义与 what-if 场景使用边界（阶段二启动前确认）。

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
| **Order_Canonical** | ✅ `FactoryCode NULL` | 否 | 由 `sp_ValidateAndPromoteOrders` 标准化写入 | 防腐层核心订单表；⚠️ ERP 原字段需规则转换；**建议口径（待确认）**：若源端仅给 ProcessCode，2 号位可参考 `MES_ProcessCode_View.FactoryCode` 补齐 |
| **Order** | 否 | 🔗 `FactoryId NOT NULL FK` | `sp_SyncOrdersToPartitionTable`：`Order_Canonical.FactoryCode` → `Factory.Code` 映射得 `Factory.Id` | 映射失败时降级 `ISNULL(f.Id, 1)` |
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

### 6.3 特别备注：Order_Canonical.FactoryCode 补齐路径（待确认口径）

当前 DDL 注释为"APS标准化工厂编码（ERP原字段需规则转换）"，但**未明确**规则转换的具体实现。建议口径：

- **主路径**：ERP 源端直接提供 FactoryCode 或等价字段 → `sp_ValidateAndPromoteOrders` 标准化映射后写入
- **备选补齐**（待 0 号位审批）：若源端仅提供 ProcessCode → 2 号位可查 `MES_ProcessCode_View.FactoryCode` 推导；但此路径需确认 ProcessCode 与 FactoryCode 的 1:1 关系是否成立

> ⚠️ 本备选路径在当前 DDL/文档中**尚无明文规定**，属于设计建议，落地前需 0 号位审批确认。
