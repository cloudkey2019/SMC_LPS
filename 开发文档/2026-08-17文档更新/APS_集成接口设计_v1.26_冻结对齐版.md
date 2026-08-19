# APS 集成接口设计规范

**版本**：v1.26  
**日期**：2026-08-12  
**基于**：《APS V1最终全部流程与业务基线 v1.0》+《APS Pegging供需承接与分层计算业务说明 v1.1 冻结对齐版》+《APS有限产能排产与滚动90天计划业务说明 v1.1 冻结对齐版》+《APS 核心排产全流程走查 V3.17 冻结对齐版》+《APS 各类基础数据分层承接与演变总表 v3.32 冻结对齐版》+《APS数据架构与防腐层设计方案 v1.36 冻结对齐版》  
**更新**：冻结对齐0～5号位内部接口、Pegging↔有限产能边界、PI Position、采购/VMI与规划性采购占位、Candidate动态影响传播、跨Domain Quantity-Time、失败人工重算、MES下发资格及最小人工确认；不重新设计已冻结业务。


**v1.26更新内容**（2026-08-12，冻结对齐版）：

> 🔒 **冻结声明**：本版本只把已冻结业务落实为接口契约，不重新打开业务设计。若本文件旧章节、示例代码、OA流程或历史版本说明与三份冻结业务文档冲突，以冻结业务为准；旧内容只能作为历史/兼容参考，不能反向定义V1业务。

- 🔄 **2号位→1号位边界重写**：2号位不再预生成最终Operation Task/ShippingTask；2号位输出逻辑生产需求、Allocation、Routing/资源/物料/规则/执行约束等完整 `DomainSolveRequest`，1号位通过现有 `IFiniteCapacityScheduler.SolveAsync` 返回 FinalTask、AllocationTaskShare、Task物理依赖、ExplanationFact及不可排结果。
- 🆕 **PI Position契约**：5号位负责复杂位置推导，输入ERP PI总量边界 + MES Stage/Operation进度 + XC + PI级跨厂在途 + Stage路径等潜在事实，输出一组互斥Position份额；2号位先选PI，再消费该PI内部Position。V1不建设Header+Slice双生命周期平台。
- 🔄 **采购/VMI进入V1正式供给**：库存、已到厂未入库、采购在途/未结PO、VMI均以 `Quantity + AvailableTime + SourceKey + CommitmentStatus` 进入Supply Context；ETA优先级为人工ETA→ERP ETA→DefaultLT。旧“V1 PipelineSupplies为空”只保留在历史说明中。
- 🆕 **Planning-only Purchase Placeholder**：无库存/已到厂/PO/VMI正式供给时，允许2号位在内存生成数量=缺口的估算供给，标记 `ESTIMATED/NOT_COMMITTED`；不生成采购单、不生成Task、不下发ERP。CTP依赖此供给时必须返回“估算、非确定性承诺”。
- 🔄 **Candidate剩余供给接口修正**：不再把Base ACTIVE全部Allocation永久扣死；Candidate使用“当前有效物理Supply - 已真实消耗 - Strict Binding - Demand Protection - 不可逆执行份额 - 已失效份额”形成可竞争池，普通未锁Allocation可重新竞争。
- 🔄 **ScopeJson语义修正**：11字段Schema保持不变，但只表示发起/初始业务范围；真实物料/工艺/资源影响允许超出初始范围。`MaxImpactedOrders`仅作警戒/人工确认阈值，不得硬截断；PlanHorizonStart/End不是影响传播硬边界。
- 🆕 **白天共享资源阻挡**：单Domain Candidate装载其它Domain当前ACTIVE共享资源占用为只读、不可移动阻挡块，不允许挤动外域计划；不建设跨Domain资源配额/借用平台。
- 🆕 **跨Domain WHATIF编排**：保持每次Candidate单Domain；后台按 `Domain_Dependency` 拓扑串行多个单Domain WHATIF，并用多段 `Quantity + AvailableTime` 传递上游结果，前端合并成一次CTP+影响答案；不建设MultiDomain Candidate/CandidateGroup。
- 🆕 **失败人工重算契约**：失败历史不可改写；人工恢复创建新的ScheduleRun，范围自动包含失败根Domain及因其失败未发布的下游，必要时补入仍未恢复的上游，按拓扑重算。复用既有FULL调度语义，不建Retry平台。
- 🔄 **Candidate采用边界**：CTP/INSERT_IMPACT_ANALYSIS永不激活；正式采用只要求最小人工授权确认并记录“谁/何时/采用哪个Candidate”。完整OA审批不作为V1主链硬依赖；现有OA Adapter可保留为可选企业流程。
- 🔄 **MES下发资格**：只有ACTIVE正式版本且具备真实执行依据的Task才可下发。Candidate、无正式PI号规划占位Task、UNLOCATED保守Task，以及仍依赖Planning-only Purchase Placeholder而无真实物料承诺的Task不得下发MES。
- 🔒 **保护区**：`ScheduleRun/PlanVersion`、`ExpectedDomainKeysJson`、`PARTIAL_SUCCESS`、Realtime BOM RequestDetail链、`OrderBomRequestLink`、Routing三件套、`MaterialStageDeptContext`、MES五态、规则治理6表及现有 `IFiniteCapacityScheduler` 接口方向继续保留。

> ⚠️ **历史说明规则**：v1.25及更早版本说明继续保留用于追溯。其中出现的“V1 Pipeline为空”“2号位Task/ShippingTask实例化”“MaxImpactedOrders达到上限即停止”“Candidate激活必须完整OA审批”等，只代表当时版本，不再具有当前V1权威性。

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
- 🆕 **§11.4 完整白天实时主链**：人工发起 → ScheduleRun → CANDIDATE PlanVersion → Candidate Order 快照 → RequestDetail 实时 BOM → 三 Realtime → READY → 三 RAW → OrderBomRequestLink → RemainingSupply → ScheduleContext → 2号位实例化 Task/ShippingTask → 1号位有限产能排定 → 2号位持久化
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
- 📌 **MES_Actual_Staging 保护**：保留表名和既有暂存主链（INSERT/MERGE SQL、`_dbContext.MES_Actual_Staging` 查询）；资源故障/修复处理方法消费 `ResourceEventDto`（从暂存行已有字段派生，不新增 DDL 列）

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
- **《APS_各类基础数据分层承接与演变总表_v5.0》**（当前 v3.31）：数据演进全景图
- **《APS_数据架构与防腐层设计方案_v5.0》**（当前 v1.35）：防腐层设计详解
- **《APS_数据库字段说明文档_v5.0》**（当前 v5.1.1）：字段清单与业务口径
- **《APS_数据库表结构设计_v5.0.sql》**（当前 v5.1.1）：DDL 脚本
- **《APS 核心排产全流程走查（完整版）》**（当前 V3.16）：端到端流程走查
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：Socket-Plug职责分工

---

## 一、集成架构总览

### 1.1 集成系统清单

| 系统 | 类型 | 接口方式 | 同步频率 | 数据方向 |
|------|------|---------|---------|---------|
| **ERP** | 自研 | 数据库视图（时间戳轮询） | 每小时/每日凌晨 | 双向 |
| **MES** | 自研 | MQ消息队列 + REST API | 实时/5分钟 | 双向 |
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
│  │  ERP_Order_Staging, MES_Actual_Staging, SyncCheckpoint│  │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
    ↓ DB视图(轮询)  ↓ MQ+REST API    ↓ DB视图    ↓ REST API
┌──────────────┐ ┌──────────────┐ ┌──────────┐ ┌──────────────┐
│ ERP Database │ │  MES System  │ │Procurement│ │  OA/Email/   │
│ (SQL Server) │ │ (MQ+HTTP API)│ │  Database │ │  SMS Gateway │
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
  - 执行 `sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, @RowsAffected OUTPUT, @ErrorMessage OUTPUT)` 管道供给链同步（DDL v5.0.42；详见防腐层 §2.5）
  - `IDataLoader.LoadPipelineSupplies()` 方法签名（见下方）：从 `SupplyFact_Pipeline` 装载 `PipelineSupplyItem` 列表，注入 `ScheduleContext.PipelineSupplies`

**PipelineSupplyItem 契约定义**（v1.26 V1正式运行契约）：

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
    /// 管道供给加载器（v1.26冻结对齐）
    /// V1正式装载已接通的厂间在途、采购/VMI等真实Supply事实；
    /// batchNo/dataCutoffTime用于锁定本次运行一致的数据切片；
    /// 某来源0行只表示该来源当前无数据或尚未接通，不代表V1业务不支持。
    /// </summary>
    public interface IPipelineSupplyLoader
    {
        /// <summary>
        /// 加载管道供给到内存集合。
        /// </summary>
        /// <param name="batchNo">本次运行选定的事实切片BatchNo；夜间使用当前批次，Candidate使用其DataCutoffTime对应的一致切片</param>
        /// <param name="dataCutoffTime">数据切片边界</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>管道供给只读列表；无记录时可为空集合，但不得把“空集合”写成V1固定业务规则。</returns>
        Task<IReadOnlyList<PipelineSupplyItem>> LoadPipelineSuppliesAsync(
            string? batchNo,
            DateTime dataCutoffTime,
            CancellationToken cancellationToken);
    }
}
```

**消费查询（v1.26）**：
- 夜间FULL：读取本次 `ScheduleRun.DataCutoffTime/BatchNo` 对应的真实供给切片；
- Candidate：读取其DataCutoffTime对应的一致当前物理Supply，再由 `BuildRemainingSupplyContextAsync` 扣除已真实消耗、Strict Binding、Demand Protection和不可逆份额；普通Base Allocation不永久占有Supply；
- `AvailableTime`为空且无法按已冻结ETA/LT规则推导的真实Supply，不得被当成确定性供给；
- 某来源0行允许，但必须区分“当前无数据”和“接口尚未接通”；
- **禁止**仅按 `IsActive=1` 跨批次读取造成切片污染，也禁止永久UPDATE事实表表达PlanVersion内消耗。

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

#### 2.1.2 BOM视图（v_APS_BOM）

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
- **1号位消费查询约定**（v1.9）：**必须按`StageScopeType`区分查询**，不得混查EDGE+ROOT
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
| V1状态 | **正式Supply契约**；应接入真实厂间在途。若源系统尚未接通可临时0行，但这只是数据接入状态，不改变V1业务能力 |
| 后续演进 | 可调整内部SELECT/JOIN或增加来源，但对外业务字段契约保持稳定 |

**链路**：ERP 源系统厂间在途数据 → **ODS.`ERP_InterplantInTransit_View`**（ODS层/MES_Integration/5号位）→ **APS.`ext_ERP_InterplantInTransit_View`**（APS单来源包装/2号位）→ **APS.`ext_PipelineSupply_Source_View`**（多来源UNION ALL统一输入视图，V1已建立/2号位；15列）→ `sp_SyncPipelineSupply` → `SupplyFact_Pipeline` → `ScheduleContext.PipelineSupplies`

**⚠️ 契约锁定规则**：
ODS契约视图字段结构为强契约。V1接入真实数据及后续演进时，可替换视图内部FROM/JOIN/WHERE和必要转换逻辑，但禁止随意破坏已冻结字段语义。

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

## 三、MES 系统集成接口设计

### 3.1 MES集成架构概览

**MES系统类型**：自研MES  
**集成模式**：双通道（MQ消息队列 + REST API）  
**数据方向**：双向

```
┌─────────────────────────────────────────────────────────────┐
│                        APS System                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                  MES Adapter                          │   │
│  │  ┌──────────────────┐    ┌──────────────────┐       │   │
│  │  │  MQ Consumer     │    │  REST Client     │       │   │
│  │  │  (实绩回写)      │    │  (计划下发/      │       │   │
│  │  │                  │    │   工艺路线)      │       │   │
│  │  └──────────────────┘    └──────────────────┘       │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              MES_Actual_Staging                       │   │
│  │  (实绩数据暂存表，含开工/完工/报废；V1 不含暂停)             │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
         ↑ MQ消息推送              ↓ REST API调用
         ↑ REST API轮询            ↑ REST API回调
┌─────────────────────────────────────────────────────────────┐
│                      MES System (自研)                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │  MQ Producer │  │  REST API    │  │  工艺路线    │     │
│  │  (实绩推送)  │  │  (计划接收)  │  │  数据库      │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 MES侧接口定义（由MES团队提供）

#### 3.2.1 实绩回写接口（MQ消息队列）

**消息队列配置**：
- **MQ类型**：RabbitMQ / Kafka（待MES团队确认）
- **Topic/Queue名称**：`mes.actual.report`
- **消息格式**：JSON
- **推送频率**：实时（开工/完工/报废事件触发；资源故障/修复事件亦经此通道）

**消息结构**：
```json
{
  "messageId": "MSG_20260226_001",
  "messageType": "ACTUAL_REPORT",
  "timestamp": "2026-02-26T10:30:00Z",
  "data": {
    "workOrderNo": "WO202602260001",
    "taskNo": "TASK_001",
    "resourceCode": "MC001",
    "operatorId": "EMP001",
    "eventType": "START|COMPLETE|SCRAP|PAUSE|RESUME",
    "eventTime": "2026-02-26T10:30:00Z",
    "quantity": 100,
    "scrapQuantity": 5,
    "scrapReason": "质量问题",
    "remarks": "正常生产"
  }
}
```

**字段说明**：
- `eventType`：事件类型（兼容保留的通用事件字段）
  - `START`：开工（→ Task.Status = IN_PROGRESS）
  - `COMPLETE`：完工（→ Task.Status = COMPLETED）
  - `SCRAP`：报废（仅记报废数量/原因，不改 Task 主状态）
  - `RESOURCE_BREAKDOWN`：设备故障（仅产生资源不可用事实 + 风险事实 + 重排建议，**不改 Task 状态**）
  - `RESOURCE_REPAIRED`：设备修复完成（仅产生资源可用事实 + 风险事实 + 重排建议，**不自动恢复 Task**）
  - `PAUSE`：**V1 不启用 / 不改变 Task 状态**——收到该事件仅登记日志/诊断，不得写 PAUSED/SUSPENDED（V1 无暂停恢复闭环）
  - `RESUME`：**V1 不启用 / 不改变 Task 状态**——收到该事件仅登记日志/诊断，不得写 PAUSED/SUSPENDED/RUNNING（V1 无暂停恢复闭环）
- `workOrderNo`：MES工单号（对应APS的TaskNo）
- `quantity`：本次报工数量
- `scrapQuantity`：报废数量

**MES 生产进度权威输入：工序报工 0-4 状态契约**

> V1 中 MES 生产进度的**权威输入**为「工序报工 0-4 状态」（MES 侧工序级报工状态），上表中的 `eventType`（START/COMPLETE/SCRAP 等）为兼容事件通道，最终统一归并为下列 0-4 状态后再映射到 `Task.Status`。APS 不在 V1 实现暂停恢复闭环。

| MES状态 | 含义 | APS Task.Status | APS处理 |
|------|------|----------------|---------|
| 0 | 待开工 | PLANNED 或 RELEASED | 未下发 MES 为 PLANNED；已正式下发 MES 但未开工为 RELEASED（需结合「是否已正式下发 MES」判定） |
| 1 | 开工中 | IN_PROGRESS | 记录实际开工时间及当前进度 |
| 2 | 完工报工 | COMPLETED | 记录完成数量与完成时间 |
| 3 | 未完工报工 | IN_PROGRESS | 保留累计报工、计算 RemainingQty，工序**未结案**（状态 3 非完成） |
| 4 | 未完工报工已完结 / 手动完工 | COMPLETED | 视为 COMPLETED；保留「手动完工、数量不足」来源事实（如 `ScheduleExplanationFact(ReasonCode=MANUAL_COMPLETED_SHORT)`），**不伪装足量完工** |

**Task.Status 正式值域（V1 唯一合法取值）**：`PLANNED` / `RELEASED` / `IN_PROGRESS` / `COMPLETED` / `CANCELLED`。
> ⚠️ V1 **不**存在 `PAUSED` / `SUSPENDED` / `WAITING` / `PENDING` / `RUNNING` 等 Task 状态取值；任何接口/示例不得写上述非法状态。

**设备故障事件示例**：
```json
{
  "messageId": "MSG_20260226_002",
  "messageType": "ACTUAL_REPORT",
  "timestamp": "2026-02-26T15:00:00Z",
  "data": {
    "resourceCode": "MC001",
    "eventType": "RESOURCE_BREAKDOWN",
    "eventTime": "2026-02-26T15:00:00Z",
    "breakdownType": "MECHANICAL_FAILURE",
    "estimatedRepairTime": 120,
    "breakdownReason": "主轴故障",
    "remarks": "需要更换轴承"
  }
}
```

**设备修复完成事件示例**：
```json
{
  "messageId": "MSG_20260226_003",
  "messageType": "ACTUAL_REPORT",
  "timestamp": "2026-02-26T17:00:00Z",
  "data": {
    "resourceCode": "MC001",
    "eventType": "RESOURCE_REPAIRED",
    "eventTime": "2026-02-26T17:00:00Z",
    "actualRepairTime": 110,
    "remarks": "已更换轴承并测试正常"
  }
}
```

#### 3.2.2 实绩回写接口（REST API轮询，备用通道）

**API端点**：`GET /api/v1/mes/actuals`

**请求参数**：
```json
{
  "startTime": "2026-02-26T00:00:00Z",
  "endTime": "2026-02-26T23:59:59Z",
  "pageIndex": 1,
  "pageSize": 100
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "total": 250,
    "items": [
      {
        "actualId": "ACT_001",
        "workOrderNo": "WO202602260001",
        "taskNo": "TASK_001",
        "resourceCode": "MC001",
        "operatorId": "EMP001",
        "eventType": "COMPLETE",
        "eventTime": "2026-02-26T10:30:00Z",
        "quantity": 100,
        "scrapQuantity": 5,
        "scrapReason": "质量问题"
      }
    ]
  }
}
```

#### 3.2.3 计划下发接口（REST API）

> 🔒 **v1.26 MES正式下发资格**：该REST接口只接收已经通过APS发布资格判断的正式Task。至少满足：
> 1. 来源 `PlanVersion.Status=ACTIVE`，不得从BUILDING/CANDIDATE/FAILED版本下发；
> 2. Task具有真实可执行的生产指示/工单映射基础，不是无正式PI号的规划占位Task；
> 3. PI位置不是 `UNLOCATED` 保守规划状态；
> 4. 若任务物料链仍依赖 `PLANNING_ONLY_PURCHASE_PLACEHOLDER` 且尚无真实采购承诺，不得直接下发；
> 5. 满足既有Firm/Frozen/发布窗口与MES幂等规则。
>
> 具体资格字段如何落DDL在《数据库字段说明/DDL》阶段冻结；本接口只冻结业务判定，不为此额外建设FreezeZoneSnapshot或新审批平台。


**API端点**：`POST /api/v1/mes/plans`

**请求体**：
```json
{
  "planVersionCode": "PV_20260226_001",
  "tasks": [
    {
      "taskNo": "TASK_001",
      "workOrderNo": "WO202602260001",
      "materialCode": "MAT001",
      "resourceCode": "MC001",
      "plannedStartTime": "2026-02-27T08:00:00Z",
      "plannedEndTime": "2026-02-27T16:00:00Z",
      "quantity": 100,
      "priority": 10,
      "remarks": "紧急订单"
    }
  ]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "planVersionCode": "PV_20260226_001",
    "receivedCount": 150,
    "acceptedCount": 148,
    "rejectedCount": 2,
    "rejectedTasks": [
      {
        "taskNo": "TASK_099",
        "reason": "资源MC001不存在"
      }
    ]
  }
}
```

#### 3.2.4 工艺路线数据接口（REST API）

**API端点**：`GET /api/v1/mes/routings/{materialCode}`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "materialCode": "MAT001",
    "routingVersion": "V1.0",
    "effectiveFrom": "2026-01-01",
    "operations": [
      {
        "operationCode": "OP010",
        "processCode": "MACHINING",
        "processName": "机加工",
        "standardTime": 2.5,
        "setupTime": 0.5
      },
      {
        "operationCode": "OP020",
        "processCode": "ASSEMBLY",
        "processName": "装配",
        "standardTime": 1.5,
        "setupTime": 0.2
      }
    ],
    "dependencies": [
      { "fromOperationCode": "OP010", "toOperationCode": "OP020", "dependencyType": "FS", "lagTime": 0.1 }
    ],
    "resourceEligibilities": [
      { "operationCode": "OP010", "resourceCode": "MC-001", "priority": 1, "efficiencyFactor": 1.0 },
      { "operationCode": "OP010", "resourceCode": "MC-002", "priority": 2, "efficiencyFactor": 0.9 },
      { "operationCode": "OP020", "resourceCode": "ASM-001", "priority": 1, "efficiencyFactor": 1.0 }
    ]
  }
}
```

### 3.3 MES Adapter实现（APS侧）

#### 3.3.1 MQ消费者（实绩回写）

```csharp
public class MESActualMQConsumer : BackgroundService
{
    private readonly ILogger<MESActualMQConsumer> _logger;
    private readonly IConnection _mqConnection;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = _mqConnection.CreateModel();
        channel.QueueDeclare(queue: "mes.actual.report", durable: true, exclusive: false, autoDelete: false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var actualReport = JsonSerializer.Deserialize<MESActualMessage>(message);

                // 写入暂存表（Dapper）
                using var conn = new SqlConnection(_apsConnectionString);
                await conn.ExecuteAsync(@"
                    INSERT INTO MES_Actual_Staging 
                        (MessageId, WorkOrderNo, TaskNo, ResourceCode, OperatorId,
                         EventType, EventTime, Quantity, ScrapQuantity, ScrapReason,
                         Remarks, ReceivedAt, Status)
                    VALUES 
                        (@MessageId, @WorkOrderNo, @TaskNo, @ResourceCode, @OperatorId,
                         @EventType, @EventTime, @Quantity, @ScrapQuantity, @ScrapReason,
                         @Remarks, GETDATE(), 'NEW')
                ", actualReport.Data);

                // 确认消息
                channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);

                _logger.LogInformation($"MES actual received: {actualReport.MessageId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MES actual message");
                // 拒绝消息，重新入队
                channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        channel.BasicConsume(queue: "mes.actual.report", autoAck: false, consumer: consumer);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

#### 3.3.2 REST API轮询服务（备用通道）

```csharp
public class MESActualPollingService
{
    private readonly ILogger<MESActualPollingService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    // 每5分钟轮询一次（作为MQ的备用通道）
    [RecurringJob("*/5 * * * *")]
    public async Task PollActualsAsync()
    {
        var lastPollTime = await GetLastPollTimeAsync();
        var now = DateTime.Now;

        var response = await _httpClient.GetAsync($"/api/v1/mes/actuals?startTime={lastPollTime:O}&endTime={now:O}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MESActualResponse>();

        using var conn = new SqlConnection(_apsConnectionString);
        foreach (var actual in result.Data.Items)
        {
            // 去重插入（Dapper + MERGE）
            await conn.ExecuteAsync(@"
                MERGE MES_Actual_Staging AS target
                USING (SELECT @ActualId AS MessageId) AS source
                ON target.MessageId = source.MessageId
                WHEN NOT MATCHED THEN INSERT
                    (MessageId, WorkOrderNo, TaskNo, ResourceCode,
                     EventType, EventTime, Quantity, ScrapQuantity, ReceivedAt, Status, Source)
                VALUES
                    (@ActualId, @WorkOrderNo, @TaskNo, @ResourceCode,
                     @EventType, @EventTime, @Quantity, @ScrapQuantity, GETDATE(), 'NEW', 'POLLING');
            ", actual);
        }

        await UpdateLastPollTimeAsync(now);

        _logger.LogInformation($"MES polling completed: {result.Data.Items.Count} actuals");
    }
}
```

#### 3.3.3 计划下发服务（v1.26 冻结对齐）

`MESPlanPublishService`继续复用现有Adapter/出站台账/幂等框架，但读取Task前必须先完成发布资格过滤，不能再简单执行“某PlanVersion全部Task直接下发”。

```csharp
public async Task PublishPlanAsync(int planVersionId)
{
    var planVersion = await _planVersionRepo.GetAsync(planVersionId);
    if (planVersion.Status != "ACTIVE")
        throw new InvalidOperationException("Only ACTIVE plan can be published to MES.");

    var tasks = await _taskRepo.GetByPlanVersionAsync(planVersionId);

    // 资格判断是业务服务，不要求新增独立平台/表。
    var publishable = new List<TaskEntity>();
    foreach (var task in tasks)
    {
        var eligibility = await _mesReleaseEligibility.EvaluateAsync(task, planVersionId);
        if (eligibility.IsPublishable)
            publishable.Add(task);
        else
            await _publishLog.RecordSkippedAsync(task.Id, eligibility.Reason);
    }

    // 仅对 publishable 组装 MESPlanRequest；出站幂等仍查正式MES下发台账。
    await PublishInBatchesAsync(planVersion, publishable);
}
```

**`IMESReleaseEligibilityEvaluator`冻结判定**：
- ACTIVE正式版本；
- 非Candidate；
- 非无正式PI号规划占位Task；
- 非UNLOCATED保守Task；
- 不再依赖未转真实承诺的Planning-only Purchase Placeholder；
- 满足既有执行锁/Firm/Frozen及出站幂等要求。

> 该Evaluator可作为现有发布服务内部方法/轻量服务实现，**不要求新增表、工作流或插件**。

#### 3.3.4 工艺路线同步服务

```csharp
public class MESRoutingSyncService
{
    private readonly ILogger<MESRoutingSyncService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core
    private readonly string _odsConnectionString;           // ODS库（读ext_视图）

    // v5.0更新：每日凌晨通过ext_包装视图同步工艺图模型（3张表）
    // v5.0.1变更（2026-04-02）：ODS视图输出MES_ID+Model，需先映射为MaterialId
    [RecurringJob("0 2 * * *")]
    public async Task SyncRoutingGraphAsync()
    {
        using var apsConn = new SqlConnection(_apsConnectionString);
        using var odsConn = new SqlConnection(_odsConnectionString);

        // 0. 预加载MES物料映射：MES_ID → MaterialId（单一真相源，Dapper）
        var mappings = await apsConn.QueryAsync<(string SourceID, int MaterialId)>(
            "SELECT SourceID, MaterialId FROM MaterialMapping WHERE Source = 'MES' AND IsCurrent = 1");
        var mesMaterialMap = mappings.ToDictionary(m => m.SourceID, m => m.MaterialId);

        // 1. 同步工序节点（ext_视图输出MES_ID+Model，需映射MaterialId）
        var rawOperations = (await odsConn.QueryAsync<RoutingOperationDto>(
            "SELECT * FROM ext_MES_APS_Routing_Operation_View")).ToList();
        var operations = rawOperations
            .Where(r => mesMaterialMap.ContainsKey(r.MES_ID))
            .Select(r => r.ToEntity(mesMaterialMap[r.MES_ID]))
            .ToList();
        await UpsertRoutingOperationsAsync(operations);
        _logger.LogInformation($"RoutingOperation synced: {operations.Count} rows (skipped {rawOperations.Count - operations.Count} unmapped)");

        // 2. 同步工序依赖边（同上映射逻辑）
        var rawDependencies = (await odsConn.QueryAsync<RoutingDependencyDto>(
            "SELECT * FROM ext_MES_APS_Routing_Dependency_View")).ToList();
        var dependencies = rawDependencies
            .Where(r => mesMaterialMap.ContainsKey(r.MES_ID))
            .Select(r => r.ToEntity(mesMaterialMap[r.MES_ID]))
            .ToList();
        await UpsertRoutingDependenciesAsync(dependencies);
        _logger.LogInformation($"RoutingDependency synced: {dependencies.Count} rows");

        // 3. 同步工序资源能力（同上映射逻辑）
        var rawEligibilities = (await odsConn.QueryAsync<OperationResourceEligibilityDto>(
            "SELECT * FROM ext_APS_OperationResourceEligibility_View")).ToList();
        var eligibilities = rawEligibilities
            .Where(r => mesMaterialMap.ContainsKey(r.MES_ID))
            .Select(r => r.ToEntity(mesMaterialMap[r.MES_ID]))
            .ToList();
        await UpsertOperationResourceEligibilitiesAsync(eligibilities);
        _logger.LogInformation($"OperationResourceEligibility synced: {eligibilities.Count} rows");
    }
}
```

### 3.4 容错与监控

#### 3.4.1 双通道容错机制

```csharp
public class MESActualProcessingService
{
    // 定时处理暂存表中的实绩数据
    [RecurringJob("*/1 * * * *")] // 每分钟执行一次
    public async Task ProcessStagingActualsAsync()
    {
        var pendingActuals = await _dbContext.MES_Actual_Staging
            .Where(a => a.Status == "NEW")
            .OrderBy(a => a.EventTime)
            .Take(1000)
            .ToListAsync();

        foreach (var actual in pendingActuals)
        {
            try
            {
                // v1.24 补 v3: 资源事件（不带 TaskNo）在 Task 查找之前分流
                //   RESOURCE_BREAKDOWN / RESOURCE_REPAIRED 只关联 ResourceCode，不要求 TaskNo
                //   从暂存行已有字段（ResourceCode/EventType/EventTime）构造 ResourceEventDto；
                //   故障详情（BreakdownReason/EstimatedRepairTime/ActualRepairTime）因暂存表无对应列暂为 null
                if (actual.EventType == "RESOURCE_BREAKDOWN"
                    || actual.EventType == "RESOURCE_REPAIRED")
                {
                    var eventDto = MapToResourceEventDto(actual);
                    if (actual.EventType == "RESOURCE_BREAKDOWN")
                        await HandleResourceBreakdownAsync(eventDto);
                    else
                        await HandleResourceRepairedAsync(eventDto);

                    actual.Status = "PROCESSED";
                    actual.ProcessedAt = DateTime.Now;
                    continue;
                }

                // START / COMPLETE / SCRAP 要求 TaskNo 并查 Task
                // ⚠️ V1 不启用 PAUSE/RESUME：即便 MES 发出该事件，也只登记日志/诊断，
                //    不进入任何 Voucher、不写 PAUSED/SUSPENDED/RUNNING（V1 无暂停恢复闭环）
                var task = await _dbContext.Task
                    .FirstOrDefaultAsync(t => t.TaskNo == actual.TaskNo);

                if (task == null)
                {
                    actual.Status = "ERROR";
                    actual.ErrorMessage = $"Task not found: {actual.TaskNo}";
                    continue;
                }

                // v1.25: DDL 中 Task 表当前不存在 ActualStartTime / ActualEndTime /
                //        ActualQuantity / ScrapQuantity；
                //        SuspendedAt / SuspendReason / ResumedAt 亦不在 DDL 中，V1 暂停流程不使用、不新增
                //        （无任何 task.Status=SUSPENDED / 字段赋值 / TaskPauseVoucher / TaskResumeVoucher 调用）
                //        本示例统一委托给 2号位实绩事务服务，不直接修改 Task 字段；
                //        Task.Status 由事务服务在合法值域内更新
                //        （仅 PLANNED/RELEASED/IN_PROGRESS/COMPLETED/CANCELLED）
                switch (actual.EventType)
                {
                    case "START":
                        await _actualTxnService.RecordTaskStartAsync(
                            taskNo: actual.TaskNo, eventTime: actual.EventTime);
                        break;
                    case "COMPLETE":
                        await _actualTxnService.RecordTaskCompleteAsync(
                            taskNo: actual.TaskNo, eventTime: actual.EventTime, quantity: actual.Quantity);
                        break;
                    case "SCRAP":
                        await _actualTxnService.RecordTaskScrapAsync(
                            taskNo: actual.TaskNo, eventTime: actual.EventTime,
                            scrapQuantity: actual.ScrapQuantity, scrapReason: actual.ScrapReason);
                        break;
                    // V1 不启用 PAUSE/RESUME：仅登记日志/诊断，绝不改 Task 状态
                    case "PAUSE":
                    case "RESUME":
                        _logger.LogInformation(
                            "V1 不启用暂停/恢复事件，仅登记: EventType={EventType}, TaskNo={TaskNo}",
                            actual.EventType, actual.TaskNo);
                        break;
                }

                actual.Status = "PROCESSED";
                actual.ProcessedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                actual.Status = "ERROR";
                actual.ErrorMessage = ex.Message;
                _logger.LogError(ex, $"Error processing MES actual: {actual.MessageId}");
            }
        }

        await _dbContext.SaveChangesAsync();
    }
}
```

#### 3.4.2 监控指标

| 指标 | 说明 | 告警阈值 |
|------|------|---------|
| **MQ消息积压** | MQ队列中未消费消息数 | >1000 |
| **暂存表积压** | Status=NEW的记录数 | >5000 |
| **处理延迟** | ReceivedAt到ProcessedAt的时间差 | >5分钟 |
| **错误率** | Status=ERROR的记录占比 | >5% |
| **MES API可用性** | REST API健康检查 | 连续3次失败 |

#### 3.4.3 设备故障事件处理

```csharp
public class MESActualProcessingService
{
    // 处理设备故障事件（v1.24 补 v3：接收 ResourceEventDto，不直接依赖 MES_Actual_Staging 物理列）
    private async Task HandleResourceBreakdownAsync(ResourceEventDto eventDto)
    {
        // 1. 查找资源
        var resource = await _dbContext.Resource
            .FirstOrDefaultAsync(r => r.ResourceCode == eventDto.ResourceCode);   // v1.24: DDL 字段名 ResourceCode

        if (resource == null)
        {
            _logger.LogWarning($"Resource not found: {eventDto.ResourceCode}");
            return;
        }

        // v1.24: 只更新 DDL 现有字段（Status / UpdatedAt）
        //   ResourceEventDto 仅携带 ResourceCode / EventType / EventTime；
        //   BreakdownReason / EstimatedRepairTimeMinutes / ActualRepairTimeMinutes 均为 null
        resource.Status    = "DOWN";
        resource.UpdatedAt = DateTime.UtcNow;

        // 5号位 ImpactAssessment：
        // ResourceEventDto 当前仅有 ResourceCode / EventType / EventTime；
        // BreakdownReason / EstimatedRepairTimeMinutes / ActualRepairTimeMinutes 均为 null
        var impact = await _ruleService.AssessResourceBreakdownImpactAsync(eventDto);
        
        _logger.LogWarning(
            "Resource {ResourceCode} breakdown recorded, running impact assessment",
            eventDto.ResourceCode);

        // 4. 资源故障风险事实：写入 ScheduleExplanationFact（V1 风险事实唯一承载表）
        //    ⚠️ 红线：仅记录事实 + 风险，绝不把受影响 Task 改为 PAUSED/SUSPENDED
        await _dbContext.ScheduleExplanationFact.AddAsync(new ScheduleExplanationFact
        {
            ObjectType      = "RESOURCE",
            ResourceCode    = eventDto.ResourceCode,
            ReasonCode      = "EQUIPMENT_BREAKDOWN_RISK",   // v1.25 统一为权威 ReasonCode（见 §11.8）；事件类型仍为 RESOURCE_BREAKDOWN
            Severity        = impact.RiskLevel,          // 如 HIGH / MEDIUM / LOW
            ImpactHours     = null,                       // 影响时长待影响评估补全
            EvidenceJson    = System.Text.Json.JsonSerializer.Serialize(new
            {
                ResourceCode              = eventDto.ResourceCode,
                AffectedInProgressTaskIds = impact.AffectedInProgressTaskIds,
                AffectedPlannedTaskIds   = impact.AffectedPlannedTaskIds,
                BreakdownReason          = eventDto.BreakdownReason ?? "未提供"
            }),
            PlanVersionId   = null                        // 关联当前激活版本（若可取得则填入）
        });

        await _dbContext.SaveChangesAsync();

        // 5. 生成重排建议/影响评估（高优先级）
        //   红线：Recommendation 是"建议"，不自动执行；不创建 ScheduleRun
        var recommendation = new RescheduleRecommendation
        {
            Reason               = "RESOURCE_BREAKDOWN",
            AffectedResourceCode = eventDto.ResourceCode,
            SuggestedScope       = impact.SuggestedScope,
            Priority             = impact.RiskLevel,
            ImpactSummary        = impact.Summary
        };
        await _recommendationService.PublishAsync(recommendation);
        
        // 6. 发送告警
        await _alertService.SendAlertAsync(new Alert
        {
            Level = "CRITICAL",
            Title = $"设备故障: {eventDto.ResourceCode}",
            Message = $"故障原因: {eventDto.BreakdownReason ?? "未提供"}, 预计修复: {(eventDto.EstimatedRepairTimeMinutes.HasValue ? eventDto.EstimatedRepairTimeMinutes + " 分钟" : "未提供")}, "
                      + $"影响在制 Task {impact.AffectedInProgressTaskIds.Count} 个, "
                      + $"计划 Task {impact.AffectedPlannedTaskIds.Count} 个",
            ActionRequired = "已生成资源故障影响评估与重排建议，等待 PMC 确认"
        });
    }

    // 处理设备修复完成事件（v1.24 补 v3：接收 ResourceEventDto，不直接依赖 MES_Actual_Staging 物理列）
    private async Task HandleResourceRepairedAsync(ResourceEventDto eventDto)
    {
        // 1. 查找资源
        var resource = await _dbContext.Resource
            .FirstOrDefaultAsync(r => r.ResourceCode == eventDto.ResourceCode);

        if (resource == null)
        {
            _logger.LogWarning($"Resource not found: {eventDto.ResourceCode}");
            return;
        }

        // v1.24: ResourceEventDto 仅携带 ResourceCode / EventType / EventTime；
        //   ActualRepairTimeMinutes 为 null（MES_Actual_Staging 无对应物理列）
        var actualRepairTime = eventDto.ActualRepairTimeMinutes;

        // v1.24: 只更新 DDL 现有字段（Status / UpdatedAt）
        resource.Status    = "AVAILABLE";
        resource.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Resource {ResourceCode} repaired, actual repair time: {Minutes} minutes",
            eventDto.ResourceCode, actualRepairTime);

        // 3. 修复后影响评估（复杂事实由5号位提供；规则/参数由3号位冻结，最终重排决策不由5号位单独裁决）
        //   红线：不自动恢复 Task，不自动重排
        var impact = await _ruleService.AssessResourceRepairedImpactAsync(eventDto);

        // v1.24: 资源恢复风险事实：写入 ScheduleExplanationFact
        //   ⚠️ 红线：仅记录事实 + 风险，不自动恢复 Task（无 RESUME→RUNNING、无 TaskResumeVoucher）
        await _dbContext.ScheduleExplanationFact.AddAsync(new ScheduleExplanationFact
        {
            ObjectType    = "RESOURCE",
            ResourceCode  = eventDto.ResourceCode,
            ReasonCode    = "EQUIPMENT_BREAKDOWN_RISK",   // v1.25 统一为权威 ReasonCode（见 §11.8）；事件类型仍为 RESOURCE_REPAIRED
            Severity      = impact.RiskLevel,
            ImpactHours   = actualRepairTime,            // 修复耗时（分钟），可能为 null
            EvidenceJson  = System.Text.Json.JsonSerializer.Serialize(new
            {
                ResourceCode        = eventDto.ResourceCode,
                CandidateResumeTaskIds = impact.CandidateResumeTaskIds,
                ActualRepairMinutes = actualRepairTime
            }),
            PlanVersionId = null
        });

        // v1.24: 生成 RescheduleRecommendation → 推送 PMC 推荐清单
        //   红线：Recommendation 是"建议"，不自动执行；不创建 ScheduleRun；不自动恢复 Task
        var recommendation = new RescheduleRecommendation
        {
            Reason               = "RESOURCE_REPAIRED",
            AffectedResourceCode = eventDto.ResourceCode,
            SuggestedScope       = impact.SuggestedScope,
            Priority             = impact.RiskLevel,
            ImpactSummary        = impact.Summary
        };
        await _recommendationService.PublishAsync(recommendation);

        // 4. 发送通知
        await _alertService.SendAlertAsync(new Alert
        {
            Level = "INFO",
            Title = $"设备修复完成: {eventDto.ResourceCode}",
            Message = $"实际修复时间: "
                      + (actualRepairTime.HasValue
                          ? actualRepairTime.Value + " 分钟"
                          : "未提供")
                      + ", "
                      + $"可评估恢复 Task {impact.CandidateResumeTaskIds.Count} 个",
            ActionRequired = "已生成设备修复后影响评估与重排建议，等待 PMC 确认"
        });
    }

    // MapToResourceEventDto: 从 MES_Actual_Staging 行映射为 ResourceEventDto。
    //
    // ⚠️ MES_Actual_Staging 现有物理列（见 §3.3.1 INSERT / §3.3.2 MERGE）：
    //   MessageId / WorkOrderNo / TaskNo / ResourceCode / OperatorId /
    //   EventType / EventTime / Quantity / ScrapQuantity / ScrapReason /
    //   Remarks / ReceivedAt / Status / Source
    //
    // 表中没有 RawData / EventData 原始 JSON 列，也没有
    //   BreakdownReason / EstimatedRepairTime / ActualRepairTime 物理列。
    //
    // 可直接映射：ResourceCode / EventType / EventTime（三列已存在）。
    // 故障/修复详情字段目前无法从暂存表还原——
    //   若 MES 团队未来在 MQ 消息体中增加对应字段并同步写入暂存表（新增列或 JSON 列），
    //   此处再更新映射逻辑；在此之前三个可选字段均返回 null。
    // 禁止在本文档中自行增加 EventData 列或修改 DDL。
    private static ResourceEventDto MapToResourceEventDto(MES_Actual_Staging actual)
    {
        return new ResourceEventDto
        {
            EventType    = actual.EventType,
            ResourceCode = actual.ResourceCode,
            EventTime    = actual.EventTime,
            // 以下三字段：现有暂存表无对应物理列，暂返回 null；
            // 待 MES 侧扩展暂存表后在此补充映射
            BreakdownReason            = null,
            EstimatedRepairTimeMinutes = null,
            ActualRepairTimeMinutes    = null,
        };
    }
}

// ============================================================================
// ResourceEventDto (v1.24 新增)
// MES 资源事件传输对象；从 MES_Actual_Staging 已有列（ResourceCode/EventType/EventTime）构造；
// BreakdownReason / EstimatedRepairTimeMinutes / ActualRepairTimeMinutes 暂为 null
// （暂存表无对应物理列；待 MES 侧扩展后补充映射，禁止自行增加 DDL 列）
// ============================================================================
public class ResourceEventDto
{
    public string    EventType                   { get; set; }   // RESOURCE_BREAKDOWN / RESOURCE_REPAIRED
    public string    ResourceCode                { get; set; }
    public DateTime  EventTime                   { get; set; }
    public string?   BreakdownReason             { get; set; }   // JSON: data.breakdownReason
    public int?      EstimatedRepairTimeMinutes  { get; set; }   // JSON: data.estimatedRepairTime
    public int?      ActualRepairTimeMinutes     { get; set; }   // JSON: data.actualRepairTime（修复事件）
}
```

---

## 四、采购 / VMI / 已到厂未入库集成接口设计（v1.26 冻结对齐）

> **定位**：采购、VMI、已到厂未入库不是“V1.1/V2预留”，而是APS V1交期判断的正式Supply事实。它们不写入现货 `InventoryBalance` 作为库存，也不生成制造Task；统一以供给事实进入2号位Supply Context，由1号位只消费最终的 `Quantity + AvailableTime` 约束。

### 4.1 采购类Supply最小契约

源系统/ODS物理视图名称允许按5号位现有实现适配，但进入APS运行时前必须归一为以下业务语义：

| 字段语义 | 要求 |
|---|---|
| `SupplyType` | `ARRIVED_NOT_RECEIVED / PURCHASE_IN_TRANSIT / OPEN_PO_REMAINING / VMI / PLANNING_ONLY_PURCHASE_PLACEHOLDER` 等受控类型 |
| `MaterialId/MaterialCode` | 必须可映射到APS标准物料 |
| `ReceivingFactory/StorageCode` | 收货工厂/仓库；仓库资格沿用已冻结库存资格规则 |
| `Quantity` | 当前仍可用数量，不得重复包含已收货/已消耗物理量 |
| `ETA` | 原始/人工预计到达时间，可空 |
| `AvailableTime` | 真正可被生产消费的时间；优先使用人工ETA，其次ERP ETA，再次DefaultLT推算 |
| `SourceKey` | 能稳定定位物理Supply；采购至少能区分采购单号+项号+物料+收货仓库 |
| `CommitmentStatus` | `COMMITTED / ESTIMATED / NOT_COMMITTED` 等；用于CTP确定性说明 |
| `SourceSystem` | ERP / PROCUREMENT / VMI / INTERNAL_ESTIMATE 等来源标记 |

**排序/消费原则**：仓库资格与Priority先于同仓库的AvailableTime；同一来源用完一条再下一条，避免同一物理Supply被拆成不可追溯的重复份额。

### 4.2 ETA与AvailableTime契约

采购类 `AvailableTime` 统一按以下优先级形成：

```text
人工维护ETA
  > ERP ETA
  > PO正式发行/下发时间 + DefaultPurchaseLT（必要时叠加已冻结保守余量）
```

如到厂后仍需检验/上架，追加仓库级“到达→可用”偏移。5号位/数据侧负责提供稳定业务事实，3号位治理DefaultLT等参数，2号位装载并归一；**1号位不得自行重新推算采购LT**。

### 4.3 Planning-only Purchase Placeholder（仅内存）

当库存、已到厂未入库、正式PO和VMI均无可用Supply时，2号位可在当前运行内存中生成：

```text
SupplyType       = PLANNING_ONLY_PURCHASE_PLACEHOLDER
Quantity         = 当前缺口
AvailableTime    = 计划基准时间 + DefaultPurchaseLT（及已冻结保守余量）
CommitmentStatus = ESTIMATED / NOT_COMMITTED
SourceSystem     = INTERNAL_ESTIMATE
```

红线：
- 不生成采购单/采购申请；
- 不生成制造Task或物流Task；
- 不落成“已承诺采购供给”事实；
- 不下发ERP；
- CTP如依赖该占位，必须明确返回“估算日期，非确定性承诺”；
- 正式PO/VMI出现后，下一次FULL/Candidate自动以真实Supply重新Pegging并替代占位。

### 4.4 与Pipeline事实层的关系

已有 `SupplyFact_Pipeline` / 相关包装视图可继续作为真实在途事实承接结构，避免为每类采购事实重新建设一套平台。但**不得**再把V1定义为“所有Pipeline为空集合”。如果某来源暂时尚未接通，应把该来源标成“数据尚未接通/无记录”，而不是改变APS业务规则。

`SupplyFact_Pipeline`只表达事实/标准化供给，不表达PlanVersion内分配余额；Candidate/FULL运行中的扣减必须在2号位内存Balance/Allocation中完成，不永久UPDATE事实表的“已分配量”。

---

## 五、OA审批系统集成接口设计

> ⚠️ **v1.26冻结边界**：本章OA接口作为可选企业流程/已有系统兼容能力保留，**不构成APS V1 Candidate采用的硬前置**。V1正式采用只要求最小人工授权确认与激活审计（谁、何时、采用哪个Candidate）；若2号位现有代码没有完整OA审批，不新增重审批平台。以下多级OA示例不得反向要求V1主链实现。

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

### 5.2 OA Adapter实现（APS侧，兼容/可选）

> 本节代码保留给已有OA集成使用。`CANDIDATE_ACTIVATION`回调分支是**可选企业流程**，不是V1唯一激活入口；没有OA时，4号位最小人工确认后可直接调用§11.1.7的显式激活服务。`VOUCHER_APPROVAL`仅兼容现有对象，不得成为所有规则执行的中央总线。

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

> v1.26新增的APS排产内核内部契约统一见**第十二章**；本清单中的外部系统接口继续保留，旧“Pipeline V1空/Task提前实例化/完整OA审批硬依赖”等说明以第十二章冻结接口为准。

| 接口 | 类型 | 实现方 | 状态 |
|------|------|--------|------|
| v_APS_SalesOrder | 数据库视图 | ERP侧 | 待实现 |
| v_APS_BOM | 数据库视图 | ERP侧 | 待实现 |
| ~~v_APS_Routing~~ | 数据库视图 | ERP侧 | v5.0废弃，拆分为下方3个视图 |
| MES_APS_Routing_Operation_View | ODS视图 | 5号位/数据Owner | v5.0新增；ODS事实契约，3号位只治理相关规则/参数 |
| MES_APS_Routing_Dependency_View | ODS视图 | 5号位/数据Owner | v5.0新增；ODS事实契约，3号位只治理相关规则/参数 |
| APS_OperationResourceEligibility_View | ODS视图 | 5号位/数据Owner | v5.0新增；ODS事实契约，3号位只治理相关规则/参数 |
| MES_APS_Resource_View | ODS视图 | MES DBA | v5.0新增；v1.11 命名统一（原名 `APS_Resource_View`） |
| EAM_APS_Resource_View（预留）| ODS视图 | EAM DBA | 未来 EAM 上线时同构新增 |
| v_APS_Inventory | 数据库视图 | ERP侧 | 待实现 |
| t_APS_PromisedDate | 数据库表 | ERP侧 | 待实现 |
| **ERP_InterplantInTransit_View** | **ODS契约视图** | **5号位（ODS实现）** | **v1.26 V1正式厂间在途Supply契约；`MasterID`=物料映射主字段，`FactoryCode`=目的工厂，`Quantity`=剩余在途数量；源未接通时可0行但不得定义成V1固定空链路** |
| ext_ERP_ERPOrderSync_CdcWrap | CDC包装视图 | 5号位 | ；（v5.0废弃） |
| ext_v_APS_SalesOrder | ODS包装视图 | 5号位 | v1.3新增（替代CDC） |
| **ext_ERP_InterplantInTransit_View** | **APS跨库包装视图** | **2号位** | **v1.26 V1正式包装链；显式列字段，禁止 SELECT *；按本次DataCutoff/Batch切片装载真实记录** |
| 采购/VMI供给契约（现有 `v_APS_PurchaseOrder` 或5号位等价ODS契约） | ODS契约/包装视图 | 5号位 + 采购DBA / 2号位Loader | **V1正式能力**；进入统一Timed Supply/SupplyFact链，提供Quantity+AvailableTime+SourceKey+CommitmentStatus；不再UPDATE Inventory |
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
| `BuildRemainingSupplyContextAsync` | 内部服务 | 2号位 | v1.24 新增；见 §11.1 |
| `BuildScheduleContextAsync` | 内部服务 | 2号位 | v1.24 新增；见 §11.1 |
| `ActivateCandidatePlanVersionAsync` | 内部服务 | 3号位 | v1.24 新增；内部硬校验；见 §11.1 |

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

#### **11.1.5 BuildRemainingSupplyContextAsync（v1.26语义修正）**

```csharp
Task<RemainingSupplyContext> BuildRemainingSupplyContextAsync(
    int candidatePlanVersionId);
```

**职责（2号位）**：以Candidate的数据切片和Base ACTIVE为参照，重新构造“当前仍可竞争的物理Supply”，而不是简单执行“Base原始供给 - Base全部Allocation”。

Candidate可用Supply的冻结语义：

```text
当前有效物理Supply
- 已真实消耗
- Strict Binding严格绑定份额
- Demand Protection需求保护份额
- 不可逆执行份额
- 已失效/不可用份额
= Candidate可重新竞争Supply
```

普通、未锁的Base ACTIVE Allocation应释放回竞争池，并按本次最新需求优先规则重新Pegging。真实采购/VMI/已到厂未入库/跨厂在途均进入Supply Context；无正式采购承诺时可加入仅内存的Planning-only Purchase Placeholder。

**不得**：
- 永久UPDATE `InventoryBalance.AllocatedQty`表达本次Candidate消耗；
- 将所有Base ACTIVE Allocation视为不可移动Supply所有权；
- 把 `PipelineSupplies` 固定返回空集合；
- 把Planning-only Placeholder落成正式已承诺Supply。

---
#### **11.1.6 BuildScheduleContextAsync（v1.26语义修正）**

```csharp
Task<ScheduleContext> BuildScheduleContextAsync(
    int candidatePlanVersionId);
```

**职责（2号位）**：构造交给Pegging与1号位Solver的完整运行时上下文。除Candidate Order/BOM/Routing/资源/规则参数外，至少应包含：
- 当前Supply Context及 `Quantity + AvailableTime`；
- Strict Binding / Demand Protection锁份额；
- MES执行不可逆事实；
- 上一ACTIVE仍有效Firm/Frozen不可移动锚点；
- PI Position结果及解析后的生产起点；
- 计划良率反算后的 `NetOutputQty / PlannedProcessQty`；
- Candidate变化种子；
- 其它Domain在共享资源上的ACTIVE占用只读阻挡块；
- 跨Domain上游多段 `Quantity + AvailableTime` 输入（如适用）。

`ScheduleContext`仍是运行期内存对象，不落库；禁止把它演化为VirtualInventoryBalance/FrozenZoneSnapshot等持久化状态平台。

---
#### **11.1.7 ActivateCandidatePlanVersionAsync（v1.26最小人工确认）**

```csharp
Task ActivateCandidatePlanVersionAsync(
    int candidatePlanVersionId,
    string confirmedBy,
    DateTime confirmedAt);
```

**事务内硬校验保持**：
1. Candidate存在且 `Status=CANDIDATE`；
2. `SourceScheduleRunId`存在；
3. `RunType + Purpose`属于允许正式采用的组合；
4. `CTP` 与 `INSERT_IMPACT_ANALYSIS` 永远拒绝激活；
5. `Candidate.DomainKey == BasePlanVersion.DomainKey`，白天仍严格单Domain；
6. 同Domain旧ACTIVE→ARCHIVED与Candidate→ACTIVE在同一事务提交；
7. 记录 `confirmedBy/confirmedAt/candidatePlanVersionId` 的最小采用审计。

**不再作为V1硬前置**：查询 `ApprovalRecord.Status=APPROVED` 或等待完整OA多级审批。若企业已有OA流程且不阻碍V1，可在调用本方法之前作为可选外围流程；不得把OA失败/未接通定义成Candidate计算失败。

**激活边界**：

| Purpose | RunType | 允许激活 |
|---|---|---|
| CTP | INSERT_ORDER_WHATIF | ❌ 永远拒绝 |
| INSERT_IMPACT_ANALYSIS | INSERT_ORDER_WHATIF | ❌ 永远拒绝 |
| INSERT_RESCHEDULE | LOCAL_RESCHEDULE | ✅ 最小人工确认后 |
| MANUAL_ADJUSTMENT | LOCAL_RESCHEDULE | ✅ 最小人工确认后 |
| MANUAL_ADJUSTMENT | MANUAL_RESCHEDULE | ✅ 最小人工确认后 |

> 如果2号位现有代码已经有轻量确认字段/方法，优先Adapter兼容；不要为了本签名大改现有版本框架。

---
### 11.2 ScopeJson 固定Schema与“初始范围”语义（v1.26）

**Schema仍固定11字段，不新增字段**：

> 适用于白天实时评估/Candidate；`FULL_SCHEDULE.ScopeJson`可为NULL。`ExpectedDomainKeysJson`继续是ScheduleRun独立字段，不进入ScopeJson。

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

| 字段 | .NET 类型 | v1.26语义 |
|---|---|---|
| Purpose | `string` | 四类业务目的 |
| OrderCanonicalIds | `long[]` | 发起/变化种子订单范围 |
| FactoryIds | `int[]` | 发起范围提示 |
| ProductFamilyIds | `int[]` | 发起范围提示 |
| ResourceGroupIds | `int[]` | 发起范围提示 |
| PlanHorizonStart/End | `DateTime?` | 初始调整窗口，不是物理影响硬边界 |
| LockedTaskIds | `long[]` | 用户显式要求保持不动的附加输入；仍须服从不可逆执行事实 |
| AllowTouchFrozenZone | `bool` | 兼容字段；不得绕过真实执行/Firm硬约束 |
| AllowDelaySalesOrder | `bool` | 业务许可输入，不改变Demand Protection红线 |
| MaxImpactedOrders | `int?` | **警戒/人工确认阈值**，不是传播硬上限 |

**校验规则**：
1. Purpose必填，RunType+Purpose仍只允许既有五种组合；
2. 入口便捷参数统一归一化到ScopeJson，冲突拒绝；
3. ScheduleRun创建后ScopeJson内容不可被静默改写；但**不可变的是用户发起条件，不是Solver真实影响集合**；
4. 1号位可沿工艺前后继、物料依赖、资源时间轴/换型邻居传播到ScopeJson以外的受影响Task；
5. `MaxImpactedOrders`达到阈值时只返回警戒、要求人工确认或直接升级单Domain完整可移动重排，**不得截断真实依赖链**；
6. `PlanHorizonStart/End`用于初始修复范围/展示，不允许截掉已经被真实依赖传播影响的必要Task；
7. LOCAL_RESCHEDULE仍要求具有明确发起种子/窗口，避免无界请求；真正影响范围由计算结果决定；
8. `ExpectedDomainKeys`严禁新增到ScopeJson。

**明确禁止**：把 `ScopeJson` 解释成“所有受影响对象的最终封闭集合”，或因为达到 `MaxImpactedOrders` 就返回一个不闭合的Candidate。

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

### 11.4 完整白天实时主链（v1.26冻结对齐）

```text
PMC / 销售 / 计划员通过4号位发起
  → 3号位归一化ScopeJson，创建单Domain ScheduleRun + Candidate PlanVersion(BUILDING)
  → 2号位生成Candidate独立Order快照
  → 必要时RequestDetail实时BOM → READY → 三张Realtime → 三张RAW → OrderBomRequestLink
  → 5号位按需要提供PI Position/跨厂/采购等复杂事实
  → 2号位 BuildRemainingSupplyContext
       - 释放普通未锁Base Allocation重新竞争
       - 保留Strict Binding / Demand Protection / 不可逆执行份额
       - 接入真实采购/VMI/在途/Received及必要的Planning-only Placeholder
  → 2号位完成分层Pegging、Allocation、Lock、计划良率反算
  → 2号位 BuildScheduleContext
       - 逻辑生产需求，而非最终Task
       - 外域ACTIVE共享资源占用作为只读阻挡块
       - Candidate变化种子
  → 1号位 IFiniteCapacityScheduler.SolveAsync
       - 动态影响传播
       - 局部修复/必要时单Domain完整可移动重排
       - FinalTask + AllocationTaskShare + Task依赖 + Explanation
  → 2号位统一持久化Candidate结果，PlanVersion=CANDIDATE，ScheduleRun=COMPLETED
  → 同一WHATIF结果同时形成CTP + 影响摘要；不得为了CTP/Impact分别重复跑两个独立Solver
  → 4号位展示
  → 如需正式采用：按当前事实执行必要重算/确认，最小人工授权后显式激活允许的Candidate
```

**白天跨Domain客户CTP**：不得创建一个MultiDomain Candidate。后台按 `Domain_Dependency` 拓扑执行多个单Domain WHATIF，例如 `C Candidate → B Candidate → A Candidate`，每一步只向下游传多段 `Quantity + AvailableTime`，最终聚合成一个客户答案。

**共享资源红线**：白天单Domain Candidate如果与其它Domain共享设备，只能把其它Domain当前ACTIVE占用当不可移动阻挡块；不得挤动外域Task，也不建立配额/借用/跨域影响传播平台。

---
### 11.5 异常/变化处理正式模式（v1.26）

设备故障/修复、订单变化、主数据变化不得自动重排。主链为：

```text
事件/变化事实
  → 影响识别/原因事实（按职责由2/5/1号位形成）
  → ScheduleExplanationFact + RescheduleRecommendation（适用时）
  → 4号位看板/推荐清单
  → PMC决定是否发起新的单Domain ScheduleRun
```

- 设备故障只形成资源不可用/恢复事实和影响建议，不自动暂停/恢复Task，不生成TaskPauseVoucher/TaskResumeVoucher；
- Demand Protection、Strict Binding、Execution Constraint直接进入正式Pegging/排程约束，不要求先包装成Voucher；
- 旧 `ToleranceClosureVoucher / PeggingVoucher / ManualFreezeVoucher / FreezeZoneVoucher` 若现有代码仍有消费者，可以作为兼容/审计对象保留，但**不得作为V1所有业务判断必须经过的中央插件/Voucher总线**；
- `CTP`、`INSERT_IMPACT_ANALYSIS`永远不得激活；允许采用的Candidate仍需显式最小人工确认。

---
### 11.6 0–5号位接口职责表（v1.26冻结）

| 号位 | 接口职责 |
|---|---|
| **0号位** | 冻结业务/验收口径；只裁决真正需要重新打开的冻结决策；不参与运行时技术编排 |
| **1号位** | 消费完整 `DomainSolveRequest/ScheduleContext`；生成FinalTask、Resource、Start/End、最终拆合批、Task物理依赖、AllocationTaskShare、Explanation；Candidate内负责真实影响传播与局部修复；纯内存求解，不写DB、不直接读ODS |
| **2号位** | 总编排与Pegging核心；装载事实、Demand/Supply余额、排序执行、Allocation/Lock、PI选择、计划良率反算、BuildRemainingSupplyContext、BuildScheduleContext、调用1号位、统一事务持久化、业务时间传播；不提前决定最终物理Task |
| **3号位** | Rule/Parameter/Strategy版本治理与冻结快照；ScheduleRun/Candidate壳等既有编排边界按现有实现保留；运行时不逐Demand调用插件、不扣Supply、不生成Task |
| **4号位** | 发起CTP/插单/人工重排，展示CTP+影响+Explanation，执行最小人工确认/显式采用；不直接改DB |
| **5号位** | ODS/BOM/Stage/CrossFactory/采购事实、PI Position等复杂事实推导；不做最终Demand↔Supply Allocation、不生成FinalTask/ShippingTask、不拥有ReasonCode中央插件权威、不创建ScheduleRun |

**接口红线**：
- 5号位不是运行时“所有规则都来问我”的插件中心；
- 2号位不是最终Task构造器；
- 1号位不重新做PeggingSupply选择；
- 4号位不绕过服务直接更新PlanVersion/Task；
- 3号位发布冻结规则/参数快照后，1/2号位在内存消费，不在Solver热路径反复查规则表。

---
### 11.7 ExpectedDomainKeysJson 写入规则（创建 ScheduleRun 服务）

> ScopeJson（§11.2）保持 11 字段固定 Schema，**禁止**把预期域集合塞入 ScopeJson；预期域集合由独立的 **ExpectedDomainKeysJson** 承载。`RealtimeEvaluationRunRequest` 新增独立属性 `IReadOnlyList<string> ExpectedDomainKeys`（见 §11.1.1），**不得**并入 ScopeJson DTO；落库为 `ScheduleRun.ExpectedDomainKeysJson`（JSON 数组字符串）。

**写入规则（创建 ScheduleRun 服务按运行类型分别执行）**：

- **FULL_SCHEDULE（夜间全量排程）**：从夜间预期域集合生成**多元素 JSON 数组**（≥1 个、**不重复**的字符串域标识）；`ScopeJson` 可为 NULL（夜间全量运行不要求填写白天实时评估 Purpose）。
- **白天 Candidate（LOCAL_RESCHEDULE / MANUAL_RESCHEDULE / INSERT_ORDER_WHATIF）**：从 `BasePlanVersion.DomainKey` 生成**单元素数组** `["BasePlanVersion.DomainKey"]`；并校验其唯一元素 `== BasePlanVersion.DomainKey == CandidatePlanVersion.DomainKey`（与 §11.1.7 校验 11 的 DomainKey 一致性同源，三者必须相等）。
- **SIMULATION（阶段二骨架）**：使用独立的 `ExpectedDomainKeysJson`（1 个或多个域），**不进入 ScopeJson**；骨架仅登记，不参与 V1 正式排程。

**红线**：`ExpectedDomainKeys` 与 `ScopeJson` 互不包含；任何运行类型都不得把 `ExpectedDomainKeys` 写入 ScopeJson，也不得在 ScopeJson 中新增 `ExpectedDomainKeys` 字段（§11.2 明确禁止字段已含 `ExpectedDomainKeys`）。

---

### 11.8 权威 ReasonCode 字典（ScheduleExplanationFact权威；Voucher兼容复用）

> 下列为V1唯一权威ReasonCode取值。`ScheduleExplanationFact.ReasonCode`必须取自此表；若旧Voucher对象仍保留ReasonCode字段，也只能兼容复用同一字典，不因此赋予Voucher中央业务裁决权。

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


## 十二、APS排产内核内部接口契约（v1.26 冻结对齐）

> 本章是v1.26新增的**当前V1内部接口权威层**。前文历史代码块如与本章冲突，以本章为准。方法名/DTO类名可在保护2号位现有代码的前提下做Adapter映射，但业务输入输出语义不得改变。

### 12.1 2号位 → 5号位：PI Position Calculator

推荐沿用/落地为现有业务规则层的轻量接口：

```csharp
Task<ProductionInstructionPositionResult> CalculateAsync(
    ProductionInstructionPositionInput input);
```

**输入最小语义**：
- ScheduleRun/PlanVersion切片；
- ProductionInstructionNo、Material、Factory；
- ERP PI总量边界（`Quantity - ReceivedQty`）；
- MES WorkOrder/Operation/Stage累计进度潜在事实；
- PI级XC、PI级跨厂在途、等待入库等强位置事实；
- Stage路径/大工艺顺序。

**输出**：同一PI的一组互斥 `PositionShare`：

```text
PositionType
Quantity
CurrentStage / NextStage
AvailableTime
SourceType / SourceKey
Issue / Confidence
```

红线：Position份额总量必须与PI本次可定位剩余边界闭合；无法可信定位时返回 `UNLOCATED`，不得伪造精确Stage。2号位先选择PI号，再消费PI内部Position；不同PI的Position不得打散后全局混排。

### 12.2 2号位Pegging核心：Demand/Supply Allocation契约

V1不要求新增“通用Pegging平台”，但运行时必须具备：

```text
DemandBalance
SupplyBalance（可复用现有SupplyPool.RemainingQty增强）
AllocationSequence（成功原子扣减时生成）
Allocation / 逻辑Ledger
Strict Binding / Demand Protection锁份额
Execution Constraint输入
```

一次成功Allocation必须同时减少Demand和Supply余额，任何一侧失败都不得形成半笔分配。普通Supply不可跨PlanVersion通过永久 `InventoryBalance.AllocatedQty` UPDATE表达占用。

**Demand Protection**与Strict Binding可复用同一最小需求—供给锁实体，通过 `LockType=STRICT_BINDING / DEMAND_PROTECTION`区分；锁定的是数量份额，不是整个PI/PO。Execution事实独立于Demand所有权锁。

### 12.3 2号位 → 1号位：`IFiniteCapacityScheduler.SolveAsync`

保留2号位现有接口方向，不另建第二套Solver入口：

```csharp
Task<DomainSolveResult> SolveAsync(DomainSolveRequest request);
```

`DomainSolveRequest`至少承载九类语义：
1. 运行边界：ScheduleRun/PlanVersion/Domain/RunType/DataCutoffTime/90天窗口；
2. 已排序的逻辑生产需求与交期/保护信息；
3. `NetOutputQty + PlannedProcessQty`；
4. Allocation份额及需求血缘；
5. 完整Routing Operation/Dependency候选网络；
6. Resource Eligibility/能力/日历；
7. 全部物料 `Quantity + AvailableTime` 约束；
8. 3号位冻结的策略/参数快照；
9. 执行不可逆锚点、Firm/Frozen锚点，以及Candidate场景下其它Domain ACTIVE共享资源阻挡块。

**2号位不得把最终Operation Task作为request前置真相**；逻辑生产需求可以含起始Stage/剩余Routing段/计划加工量，但最终Task拆合、Resource与时间由1号位决定。

### 12.4 1号位 → 2号位：`DomainSolveResult`

至少返回：
- `FinalTask[]`：最终物理Task、实际Resource、Start/End、净产出量及PlannedProcessQty；
- `AllocationTaskShare[]`：Allocation↔FinalTask数量映射，必须支持多对多；
- `TaskDependency[]`：真实Task-to-Task物理依赖；
- `ExplanationFactDraft[]`：绑定约束、延迟原因、影响小时等；
- `Unscheduled/Unfulfilled[]`：不可排/未满足数量与原因；
- Candidate运行的实际影响集合与回退信息。

1号位不得修改Allocation数量、不得自行替换PeggingSupply、不得写数据库。不可行时返回未满足量和原因，不能静默减量或编造日期。

### 12.5 采购/VMI与Planning-only Supply运行时DTO

不强制新增新的持久化表。进入2号位Supply Context的统一最小DTO可表达：

```csharp
public sealed class TimedSupplyItem
{
    public string SupplyType { get; init; }
    public string SourceKey { get; init; }
    public int MaterialId { get; init; }
    public int FactoryId { get; init; }
    public string? StorageCode { get; init; }
    public decimal Quantity { get; init; }
    public DateTime AvailableTime { get; init; }
    public string CommitmentStatus { get; init; } // COMMITTED / ESTIMATED / NOT_COMMITTED
}
```

`PLANNING_ONLY_PURCHASE_PLACEHOLDER`只在内存构造，不要求ID/数据库主键，不进入ERP/MES外部接口。

### 12.6 跨Domain Quantity-Time接口

上游Domain对下游不得只返回一个汇总AvailableTime，接口必须允许多段：

```csharp
public sealed class DomainSupplySlice
{
    public string MaterialCode { get; init; }
    public decimal Quantity { get; init; }
    public DateTime AvailableTime { get; init; }
    public string SourceDomainKey { get; init; }
}
```

例如 `40@8/15 + 60@8/17` 必须保留两条。V1不持久化VirtualInventoryBalance；这些切片作为当前运行内存输入传给下游Domain。

### 12.7 白天Candidate影响传播接口

2号位只提供**变化种子**，1号位决定真实物理影响范围。变化种子至少来自：
- Base vs Candidate Pegging变化；
- 新增/释放/改绑Allocation；
- 交期/数量变化；
- Material AvailableTime变化；
- 显式Locked/Firm/Frozen或外域共享资源阻挡变化。

1号位沿工艺前后继、Allocation物料依赖、资源时间轴/换型邻居传播。传播只在实际时间/资源/依赖发生变化时继续；达到软阈值时升级单Domain完整可移动重排。`MaxImpactedOrders`只用于提示，不可作为截断返回条件。

### 12.8 跨Domain CTP编排（复用单Domain接口，不新增MultiDomain API）

跨Domain CTP不是新的Candidate类型，也不要求新增一个MultiDomain对外API/DTO体系。由现有应用编排层在一次用户请求内复用**单Domain** WHATIF入口：

1. 从最终需求Domain沿 `Domain_Dependency` 找必要上游；
2. 按拓扑上游→下游逐个创建/执行现有单DomainWHATIF；
3. 每步将 `DomainSupplySlice[]` 作为下一Domain的上游Supply Context；
4. 应用层把各单Domain结果汇总为一个客户CTP + 影响答案；
5. 每个Candidate仍拥有独立ScheduleRun/PlanVersion，严禁创建MultiDomain Candidate/CandidateGroup或原子多域事务。

> 实现上可以是现有CTP ApplicationService内部的私有/内部编排方法，不要求增加新的持久化对象、RunType或独立“MultiDomain API”。

### 12.9 夜间失败与人工重算（复用现有FULL ScheduleRun创建/编排入口）

自动重试只处理同一ScheduleRun内的短暂技术失败；耗尽后Domain必须进入FAILED。人工恢复**不定义新的Retry RunType/API体系**，而是复用现有ScheduleRun创建入口和FULL调度语义，以“manual trigger + 指定ExpectedDomainKeys”的方式创建新的ScheduleRun。

运行范围由现有编排服务按 `Domain_Dependency` 自动展开：
- 失败根Domain；
- 因该失败而未发布的直接/间接下游；
- 如目标Domain仍依赖尚未恢复的必要上游，则补入该上游；
- 已有有效当前ACTIVE且无需重算的独立Domain不加入。

新Run按拓扑执行；旧FAILED/PARTIAL_SUCCESS历史不可改写。审计只需记录原失败Run的关联、触发人/时间等最小信息，具体复用现有字段还是在字段/DDL阶段增加一个轻量来源字段再决定；**不得**因此建设FAILED_DOMAIN_RETRY枚举、RetryGroup、Retry Workflow或重试审批平台。

### 12.10 3号位规则/参数接口

3号位职责是治理和发布冻结版本，1/2号位在运行开始一次性装载：

```text
StrategyProfileVersion
  → RuleSetVersion
  + ParameterSetVersion
  → RuntimeRuleParameterSnapshot
```

V1只有两类规则机制：
- 有序第一命中规则：需求排序、Lock Policy等；
- 默认值 + 少量Scoped Override：LT、计划良率、排程方向、瓶颈偏好、拆批参数等。

禁止在Solver热路径逐Task调用动态插件/脚本/SQL规则。

### 12.11 4号位输出接口：一次WHATIF一个答案

单次WHATIF结果至少向4号位提供：
- 是否满足目标交期；
- 最早完成时间；
- 是否依赖 `ESTIMATED/NOT_COMMITTED`采购占位；
- 受影响订单及仍按期/延期摘要；
- 是否触碰Demand Protection；
- 主要瓶颈与原因；
- 是否发生局部→单Domain完整重排fallback。

`CTP`与`INSERT_IMPACT_ANALYSIS`可继续作为Purpose/页面视角，但**同一次WHATIF Solver结果同时具备交期和影响信息**，不得为了两个Purpose重复运行两个独立Solver。

### 12.12 MES正式下发接口资格

4/2号位发起MES发布前调用轻量资格判断：

```csharp
Task<MESReleaseEligibilityResult> EvaluateAsync(
    int planVersionId,
    long taskId);
```

必须拒绝：
- PlanVersion非ACTIVE；
- Candidate；
- 无正式PI号的规划占位Task；
- UNLOCATED保守Task；
- 仍依赖Planning-only Purchase Placeholder且无真实Supply替换的Task；
- 违反不可逆执行/Firm/Frozen/既有MES幂等边界的Task。

本接口是发布服务的轻量业务判断，不要求新增FreezeZoneSnapshot、Voucher审批链或新的状态平台。

### 12.13 v1.26接口层防过度设计红线

V1明确不建设：
- 有限物流Task/ShippingTask Solver接口；
- MultiDomain Candidate/CandidateGroup接口；
- FrozenZoneSnapshot服务平台；
- VirtualInventoryBalance持久化接口；
- 第二套远期Rough-Cut Solver API；
- 动态插件/DSL/脚本执行总线；
- Candidate全量ImpactGraph持久化平台；
- RetryGroup/Retry Workflow；
- 强制OA审批作为Candidate采用前置。

已有旧接口/表/Adapter如删除风险大可以保留，但必须标为兼容/历史，不得进入V1正式调用链。

---

**文档结束**
