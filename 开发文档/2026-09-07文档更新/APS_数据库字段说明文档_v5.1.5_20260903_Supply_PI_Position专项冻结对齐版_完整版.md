APS_数据库字段说明文档_v5.1.2_冻结对齐版(1).md

APS 数据库表结构字段说明文档（完整版）

版本：v5.1.2 冻结对齐版 日期：2026-08-12
目的：供全体开发团队（0-5号位）参考的完整数据库字段与业务语义基线
上位业务基线：《APS V1最终全部流程与业务基线》+《APS
Pegging供需承接与分层计算业务说明 v1.1
冻结对齐版》+《APS有限产能排产与滚动90天计划业务说明 v1.1 冻结对齐版》
技术对齐基线：《核心排产全流程走查 V3.17
冻结对齐版》+《各类基础数据分层承接与演变总表 v3.32
冻结对齐版》+《数据架构与防腐层 v1.36 冻结对齐版》+《集成接口 v1.26
冻结对齐版》
DDL状态：当前物理DDL仍为v5.1.1；本文v5.1.2先冻结字段目标语义，下一步由DDL文档按本文实施，不允许DDL反向改业务。
适用人员：0号位（业务负责人）、1-5号位（开发团队）、DBA

冻结声明

本文是字段层技术对齐文档，无权重新设计已冻结业务。旧字段、旧表、历史代码或已有审批结构如与上位冻结业务冲突，应由字段/DDL/代码向冻结业务收敛。只有出现真实新业务事实或冻结规则无法实现时，才允许显式"重新打开冻结决策"，并由0号位重新裁决。

v5.1.1及更早版本说明继续保留用于历史追溯；其中"PriorityScore为主排序权威""Pipeline
V1固定为空""1号位直接按StageProgress扣Task""Task只能归属一个Order""ROUGH_CUT为90天远期主链""完整OA审批是Candidate激活硬前提"等均不再具有当前V1业务权威性。

相关文档：

《APS 核心排产全流程走查》V3.17 冻结对齐版：端到端运行流程与0～5号位接力

《APS_各类基础数据分层承接与演变总表》v3.32 冻结对齐版：数据演进全景图

《APS_数据架构与防腐层设计方案》v1.36 冻结对齐版：物理架构、防腐与数据流

《APS_集成接口设计》v1.26 冻结对齐版：内部服务与数据契约

《APS_数据库表结构设计_v5.0.sql》：当前v5.1.1，下一步按本文v5.1.2对齐

《APS_权限与审批系统实施方案_v1.0》：企业可选审批能力；不等于V1
Candidate激活主链硬依赖

《APS_Auth数据库字段说明文档_v1.0》：权限/审计字段参考

⚖️ 重要更新（v5.1.2 冻结对齐，2026-08-12）：

🔄 Order.PriorityScore降为历史/展示兼容字段，不再作为V1需求排序权威；V1排序由"计算层→有序规则段→第一命中→段内排序"执行。

🔄 PlanVersion.ComputeMode不再用ROUGH_CUT/CRITICAL_PATH选择第二套远期排程引擎；90天正式计划统一进入有限产能Solver。

🔄 Task.OrderId降为可空兼容主展示字段；真实Demand归属改由AllocationTaskShare多对多承接。

🆕 Task.PlannedProcessQty：与Task.Quantity（净合格产出）分离，专供有限产能占用。

🆕 AllocationTaskShare最小表：保存Allocation↔FinalTask净合格数量份额，不新增独立PeggingAllocationLedger物理表。

🆕 ProductionInstructionPositionSnapshot：V1只建一张最小PI位置快照，多行互斥闭合，不建Header+Slice平台。

🔄 StageProgressSnapshot/OperationProgressSnapshot定位改为5号位PI
Position输入；1号位不再直接据其形成第二套PI位置解释。

🔄 PeggingSupplyAllocation补AllocationSequence与供给承诺语义；PI内部XC/Stage/在途不再被当成额外Supply。

🆕 沿用同一需求---供给锁实体表达Strict Binding与Demand
Protection，通过LockType=STRICT_BINDING/DEMAND_PROTECTION区分；Execution事实独立。

🔄 采购在途、未结PO、VMI、已到厂未入库、厂间在途正式进入V1 Timed
Supply；SupplyFact_Pipeline不再定义为固定空链路。

🔄 SupplyAvailabilityRule保留兼容，但不再作为V1必须调用的"唯一管道规则平台"；真实AvailableTime按冻结ETA/DefaultLT口径归一。

🔄 TaskType新生成任务V1只允许生产Task；TRANSFER/PROCUREMENT仅历史兼容，不建设有限物流Task/采购Task。

🔄 FenceConfig保留Firm/Frozen参数语义，但不再把Firm调整绑定到完整OA审批；跨版本锚点从上一ACTIVE读取，不建设FrozenZoneSnapshot。

🔄
Scenario/ApprovalFlow等既有表保留，但标为可选企业能力，不能反向成为V1主链前置条件。

⚖️ 重要更新（v5.1.0）（2026-07-13 白天实时评估与局部重排链路，对齐 DDL
v5.1.0 / 防腐层 v1.34 / 演变总表 v3.30 / 集成接口 v1.24 / 核心走查
V3.15）：

🆕 新增
MES_APS_BOM_Workset_CrossFactoryEdge_Realtime：实时跨厂交接边表，业务语义与批量表 MES_APS_BOM_Workset_CrossFactoryEdge 对齐；使用 RequestDetailId 替代 BatchNo 作为隔离键；三个索引；0
行合法；只表达结构事实

🆕 新增
sp_GenerateBOMCrossFactoryEdgeRealtime：由 sp_EnrichBOMWorksetRealtime 在
Step 5 日志前调用；按 RequestDetailId 严格隔离；StageDict.FactoryCode
权威取值

🔄 MES_API_BOM_Request_Realtime：字段表按 DDL v5.1.0 完整落实：Id /
BOMNO / RequestDetailId(NULL) / OrderCanonicalId(NULL) / ResolvedBOMNO /
RequestTime / Status / CompletedTime / ExpandedRowCount / ErrorMessage /
RetryCount / Priority；无物理 FK；READY
以 Status='READY' 为唯一权威；ExpandedRowCount 仅诊断；同一
RequestDetailId 存在多请求时按 Id DESC 取最新

🔄 MES_APS_BOM_Workset_Issues.BatchNo 口径：批量链路使用正式
BatchNo；实时链路正式路径 RT:RD:{RequestDetailId}；RT:{ResolvedBOMNO} 仅
deprecated 兼容

🔄 CompletedTime vs
CompletedAt：仅 MES_API_BOM_Request_Realtime.CompletedTime；MES_API_BOM_Request.CompletedAt、PlanVersion.CompletedAt、ScheduleRun.CompletedAt、SimulationRun.CompletedAt 保持不变

⚖️ 重要更新（v5.0.46）（2026-06-23 跨厂Pegging补强 + 规则与参数引擎）：

🆕 新增
ProcessCodeDict.ERPProperty：仓库/工序位置业务属性（M/XC/ZP/BP）；MES_ProcessCode_View
透出

🆕 新增 ERP_Received_ByDocument_View /
ext_ERP_Received_ByDocument_View：ERP Received 按单据汇总 ODS契约视图 +
APS跨库包装视图

🆕 新增 MES_APS_BOM_Workset_CrossFactoryEdge /
APS_BOM_CROSS_FACTORY_EDGE_RAW：BOM跨厂交接边表（ODS+APS）

🆕 新增
PeggingSupplyAllocation：Pegging供给分配细账表（库存/在途/Received等非Task供给）；澄清物理
Pegging 表为 Task-to-Task 血缘表

🆕 新增规则与参数引擎6张表：RuleSet/RuleSetVersion/ParameterSet/ParameterSetVersion/StrategyProfile/StrategyProfileVersion

🔄 ScheduleRun：新增 StrategyProfileVersionId（策略包版本绑定）

📌 SupplyFact_Pipeline：支持 MOCK 数据联调（SourceSystem='MOCK'）

⚖️ 重要更新（v5.0.42）（2026-06-15 管道供给链路完整骨架 +
ODS空契约视图 + 字段契约锁定）：

🆕 新增 §1.9 ERP_InterplantInTransit_View（ODS 层）：14字段 ERP 厂间在途
ODS 契约视图；V1 WHERE 1=0 返回0行

🆕 新增 §1.9a ext_ERP_InterplantInTransit_View（APS 层）：APS
跨库包装视图，显式列字段，禁止 SELECT \*

🔄 §6.6 SupplyFact_Pipeline：新增 4 个来源追溯字段（SourceMasterID /
SourceFactoryCode / SourceDocumentLineNo / SourceUpdatedAt）

🔄 §6.7 SupplyAvailabilityRule：V1 不调用声明补强；V1.1/V2 TODO 口径修正

📌 分层语义统一：ODS视图=「ODS层/MES_Integration/来源ERP/5号位」；APS包装视图=「APS层/APS_Production/2号位」

📌 契约锁定规则：ODS 契约视图字段结构为强契约；V1.1/V2
允许调整视图内部实现逻辑，对外14字段投影不变

⚖️ 重要更新（v5.0.41）（2026-06-12 MES生产进度快照三表 +
三个同步SP，对齐演变总表 v3.26 / 防腐层 v1.30）：

🆕 新增 §九
MES生产进度快照：MESWorkOrderSnapshot（§9.1）+ OperationProgressSnapshot（§9.2）+ StageProgressSnapshot（§9.3）

📌 V1 工序识别主字段：OperationName，不以 MES 工序编码为主匹配字段

📌 Task/Pegging 全量重算口径：MES 进度不匹配历史 TaskId；Pegging
不跨版本复用

📌 EAM V1 预留：字段说明文档不新增 EAM 相关表，占位声明见防腐层设计方案
§2.9

⚖️ 重要更新（v5.0.40）（2026-06-08 可用供给明细层新增 + DDL
建表顺序修复）：

🔧 DDL 建表顺序修复：ProductFamily/Factory 提前至
§2.4a，修复 ProductionDepartment/InventoryAvailabilityRule 外键引用顺序失败问题

🔆 新增 InventoryAvailableSupplyDetail（§6.4.5）：规则命中后、余额汇总前的可用库存明细层；保留 AvailabilityRuleId+RulePriority+InventorySupplyCandidateId；InventoryBalance 从该表汇总生成

🔄 库存六层架构定稿：事实→候选→规则→明细→余额→内存

⚠️ v5.0.23 changelog
修正：SupplyFact_Pipeline 描述补充"六层"说明；SupplyAvailabilityRule 去掉已删旧表引用，补
V1 不调用声明

⚖️ 重要更新（v5.0.39）（2026-05-31 库存规则 V1口径收敛）：

🗑️ 删除 ProductFamilyInventoryScope（旧§7.1）：从当前版本正式删除

🗑️ 删除 InventorySourceRule（旧§7.2）：从当前版本正式删除

🗑️ 删除 InventorySourcePriority（旧§7.3）：v2.8已废弃，同步从文档删除

🆕 新增 InventoryAvailabilityRule（§7.1）：统一库存可用规则表；IsAvailable+Priority 替代旧 RuleAction(PREFER/EXCLUDE)

🔄 InventoryFact_ERP（§6.2）：新增 FactoryCode；字段名 Warehouse→WarehouseCode

🔄 InventoryFact_MES（§6.3）：新增 WarehouseCode（V1主链）+FactoryCode；Location 字段名历史保留

🔄 InventoryBalance.ProductFamilyId（§6.5）：口径修正 =
库存使用上下文，非物料自身产品族

🔄 InventoryBalance.BatchNo（§6.5）：口径修正 =
快照批次标签，非订单消耗记录

🆕 sp_SyncInventorySnapshot(@BatchNo) 六步 ETL：新建库存快照同步存储过程

🆕 库存链路包装视图：ext_ERP_Inventory_View / ext_MES_Inventory_View / ext_ERP_InterplantInTransit_View（V1
空链路预留）

📌 FactoryId 红线修正：库存/供给事实+规则+余额表均可持有 FactoryId FK

📌 InventoryAllocationResult：V1.1/V2 预留，V1 不建表

⚖️ 重要更新（v5.0.38）（2026-05-30 产品族 V1 口径修正）：

🔄 ERP_Master_View 契约升级
v1.5：新增 IsProductFamilyRequired（BIT）/ ProductFamilyCode（NULLABLE）/ FamilyResolveStatus（NOT
NULL）三字段；5号位在 ODS 内部判断（可读取 ERP ModelSort），不暴露 ERP
原始字段

🔄 MES_Material_View 契约升级 v1.5：同名三字段，MES 侧 V1 固定返回 0 /
NULL / 'NOT_REQUIRED'

🔄 ext_ERP_Master_View / ext_MES_Material_View：显式列列表，透传三新字段

🔄 FamilyResolveStatus 值域升级：RESOLVED / NOT_REQUIRED / NO_RULE /
AMBIGUOUS / FAMILY_CODE_NOT_FOUND / SOURCE_FIELD_MISSING

🔄 sp_SyncMasterData 步骤1c 四规则：规则1 NOT_REQUIRED→NULL；规则2
RESOLVED→码表映射；规则3 NO_RULE/AMBIGUOUS/SOURCE_FIELD_MISSING→ETL
Issue；观则4 FAMILY_CODE_NOT_FOUND→ETL Issue

🔄 Material.ProductFamilyId：允许为空；仅对 IsProductFamilyRequired=1 且
FamilyResolveStatus='RESOLVED' 的物料写入

⚠️ §4.7/4.8/4.9 ODS 三表：均改标记为 V2 预留，V1 不建

📌 设计决策：V1 不新增
MaterialProductFamilyScopeRule；sp_ResolveMaterialProductFamily V2
预留；APS 层禁止保存 ModelSort/ProcessCode

🛑 红线：Order.ProductFamilyId 从 Material.ProductFamilyId 继承，禁止在订单层另建解析链

⚖️ 重要更新（v5.0.32）（2026-05-25 RequestDetail字段收敛，对齐 DDL
v5.0.32）：

🗑️ 删除 MES_API_BOM_Request_Detail.Model：5号位不再依赖 ERP 原始
Model；Model
只保留在 ERP_Order_Staging.Model / Order_Canonical.SourceModel

🗑️ 删除 MES_API_BOM_Request_Detail.OrderStagingId：ERP_Order_Staging
仅作同步缓冲层；BOM主链锚点已确定为 OrderCanonicalId

🗑️ 删除 MES_API_BOM_Request_Detail.ResolvedBOMNO：本表定位为纯 BOM
请求输入表；Workset 解析结果归 OrderBomRequestLink.ResolvedBOMNO（由
2号位写入）

🔄 sp_ExpandBOMBatch_vNext 删除步骤5a：不再回写 ResolvedBOMNO 到
RequestDetail

📌 职责分离：5号位只写 MES_APS_BOM_Workset/StageDetail/Issues；2号位从
Level=1 Workset.BOMNO 取值写入 OrderBomRequestLink.ResolvedBOMNO

📌 最终字段：Id / BatchNo / OrderCanonicalId / OrderNo / SourceSystem /
SourceOrderId / MaterialCode / FactoryCode / OrderType / RequestedBOMNO
/ CreatedAt

⚖️ 重要更新（v5.0.31）（2026-05-25 Order→BOM追溯链闭合，对齐 DDL
v5.0.31）：

🔄 表结构变更：MES_API_BOM_Request_Detail 核心锚点从 OrderStagingId 升级为 OrderCanonicalId（Order_Canonical 是
APS 防腐层核心表，跨库逻辑引用，无FK约束）

🔄 字段改名：BOMNO → RequestedBOMNO（订单原始携带值）；🆕 新增 OrderNo、SourceSystem、SourceOrderId

🔄 唯一约束：UQ_BOMRequestDetail_BatchOrder(BatchNo,
OrderStagingId) → UQ_BOMRequestDetail_BatchCanonical(BatchNo,
OrderCanonicalId)

🆕 新增
§1.2b OrderBomRequestLink（APS本地订单-BOM解析结果索引表）：PlanVersionId/OrderId(NULL允许)/OrderCanonicalId/RequestDetailId/ResolvedBOMNO/RepWorksetId/LinkStatus；UNIQUE(PlanVersionId, OrderCanonicalId)（v5.0.34
升级）；4个查询索引

📌 查询链路收敛：Order→OrderBomRequestLink.ResolvedBOMNO→APS_BOM_RAW（BOM结构）；Order→OrderBomRequestLink.RepWorksetId→APS_BOM_STAGE_PATH_RAW.WorksetId（大工艺路径）

📌 设计决策：APS_BOM_RAW 保持BOMNO级共享（不订单化）；APS_BOM_STAGE_PATH_RAW 无需改动

------------------------------------------------------------------------

# v5.1.5（20260903）Supply / PI Position专项冻结对齐增量

## 1. ProductionInstructionPositionSnapshot定位

ProductionInstructionPositionSnapshot表示一次排程运行中的生产指示位置事实快照。

冻结规则：

-   Position不是独立Supply；
-   Position不参与Supply排序；
-   Position不单独产生Allocation；
-   ProductionInstruction仍作为Supply参与Pegging。

Snapshot绑定本次排程运行事实，不作为永久历史版本平台。

## 2. Position与跨厂规则

跨厂大工艺接续：

-   通过ProductionInstruction Position表达；
-   PositionType=INTERPLANT_TRANSIT。

不新增Transit Supply。

## 3. Received边界

ERP Received属于事实来源：

-   不生成独立Supply；
-   不参与APS Supply链。

------------------------------------------------------------------------

# Domain专项冻结增量

## DomainKey

DomainKey表示APS稳定真实排程域标识。

统一用于：

-   Order.DomainKey；
-   PlanVersion.DomainKey；
-   ScheduleRun.ExpectedDomainKeysJson；
-   Domain_Dependency。

DomainKey不等同于ProductFamilyCode。

## DomainDefinition

V1支持：

-   FAMILY；
-   FACTORY_FAMILY。

V1不新增：

-   DomainRuntime；
-   DomainContext；
-   MultiDomain Candidate；
-   VirtualInventoryBalance；
-   Domain资源配额；
-   Domain DSL。

## 职责边界

3号位： 负责DomainDefinition治理和依赖合法性校验。

2号位： 消费DomainKey并执行归域、计算和运行闭环。

1号位： 消费真实Domain上下文，不重新解释Domain范围。

5号位： 保持事实数据和接口接入职责。
