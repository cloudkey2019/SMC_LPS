# APS 数据架构与防腐层设计方案

**版本**：v1.42  
**日期**：2026-07-31  
**适用项目**：Lean APS V1.0  
**文档性质**：系统物理架构、数据流转基线、防腐层设计规范与开发实施细则  
**设计目标**：在保障每日02:00全量排程（10万级Task）15分钟极速收敛的前提下，彻底免疫老旧ERP系统的表结构变更与未来换代冲击，并解决海量死数据与双重库存的物理难题  
**维护责任人**：2号位（技术负责人）

---


## v1.42 变更说明（2026-07-31 1号位纯内存接口与数据库隔离澄清）

- 🔒 `TaskDraft / FinalTaskDraft / ScheduledTaskDraft / AllocationShare`均为Domain运行内存对象，不建表、不落中间库。
- 🔒 2号位是排程主链唯一数据库访问者：负责快照/配置/事实装载、`ScheduleContext`和`DomainSolveRequest`组装、调用1号位以及正式结果事务持久化。
- 🔒 1号位只实现纯内存求解接口；不得依赖`DbContext`、Repository、Dapper、`SqlConnection`或任何数据库读写组件。
- 🔒 正式`[Task]`由2号位依据1号位返回的`FinalTaskDraft`实例化落库；1号位只形成排定后的内存结果，不创建正式Task实体。
- 📝 Stage路径、Routing、进度和LeadTime参数均由2号位查询并装入内存请求，原“1号位查询/读取表”措辞全部按此解释和修正。

> **v1.42边界声明**：仅澄清数据库隔离和对象形态，不修改接口业务字段、算法、DDL或统一事务边界。

## v1.41 变更说明（2026-07-31 库存净量契约与Pegging最终编码边界）

- 🔧 ODS `ERP_Inventory_View`保持原字段集合，`Quantity`直接改为扣除`WasterQty`后的净可用量；APS下游不得再次扣减。无法按客户区分数量的专属仓库通过现有库存规则整体排除。
- 🔧 SupplyBusinessKey采用PI/INV/DOC/PO/EXEC/VIRTUAL_PI最小格式，由来源Loader生成；通用候选与Pegging循环只复制。
- 🔧 `SchedulingOrchestrator`保留为单Domain总编排；Phase 1.6只产内存TaskDraft，Pegging纯内存，1号位返回最终Task和数量份额，2号位统一事务落盘。
- 🔧 AllocationSequence每PlanVersion局部递增，成功双边扣减后生成；不新增数据库Sequence和全局计数服务。
- 🔧 1号位只做有限产能、合并拆分及份额守恒，不写DB、不改变供给归属。
- 🔧 结果落盘和PlanVersion激活继续使用两个独立事务。

> **v1.41边界声明**：不新增库存字段、数据库对象、协调平台、事件溯源或锁管理平台。

## v1.40 变更说明（2026-07-30 现有代码对象映射澄清；不改变物理架构）

- 🔧 现有`PeggingRuleVoucher`继续作为5号位分配判断对象，其业务契约等同`PeggingAllocationDecision`；不强制重命名或新增第二套Decision。
- 🔧 现有`PeggingLedgerEntry`继续作为2号位原子扣减后的内存记录，并批量映射到`PeggingAllocationLedger`；不新增平行的`PeggingAllocationLedgerDraft`代码类。
- 🔧 `PeggingSupplyAllocation`继续只承接非Task供给分配结果，并通过LedgerId关联统一总账；不得把它扩展成第二套Pegging总账。
- 🔧 当前代码尚无`ExecutionLock`时，仅按DDL v5.2.3补齐最小实体、查询和状态更新；不建设Link表、完整事件溯源、锁管理平台或动态插件体系。

> **v1.40边界声明**：本轮不新增数据库对象，不修改表字段和索引，不改变Pegging算法、MES双向视图、Candidate、HardLock或PlanVersion架构。

## v1.39 变更说明（2026-07-30 定点补丁：MES快照服务边界与ExecutionLock数量语义）

- 🔧 `INightlyBatchOrchestrator`只创建`ScheduleRun`并冻结`ScheduleRunId + DataCutoffTime`；三类MES快照由三个独立Hangfire定时任务生成，`ISchedulingOrchestrator`只校验、装载和消费快照。
- 🔧 撤销人工短量差额字段；MES小工序状态4不等于PI数量取消，也不直接等于整张MES工单终结。小工序剩余加工量由`OperationProgressSnapshot`计算。
- 🔧 `ExecutionLock.RemainingExecutionQty`改为物理字段，由2号位依据MES现实工单状态维护；整张工单终结后，未完成且未正式取消的差额退出执行锁并返回PI未承诺剩余池。

> **v1.39覆盖声明**：本轮不改变双向视图、发布承诺、Pegging、HardLock、Candidate和分域版本架构。

## v1.38 变更说明（2026-07-29 第三轮定点修订：MES双向视图、发布承诺、短量完结与审计收口）

- 🔄 APS↔MES统一为双向视图：APS以`MESPlanRelease + APS_MES_PlanRelease_View`向MES发布，MES以实时工单/Operation/Stage契约视图向APS提供当前累计事实；取消MQ事件累计、REST主动下发和`MES_Actual_Staging`主链。
- 🆕 `ReleaseItemKey`作为跨系统稳定幂等键；MES建单后在`MES_APS_WorkOrder_View`回传，APS据此将发布记录转CONSUMED、创建ExecutionLock并更新Task状态。发布数量取Stage级执行批次的单一流转量，禁止累加串行小工序Task数量。
- 🔄 Task写入发布视图时仍为PLANNED；只有MES现实工单被APS快照确认后才进入RELEASED。PUBLISHED但未建单的发布承诺跨版本保留，不得重新生成ReleaseItemKey。
- ⚠️ ExecutionLock人工差额数量已由v1.39撤销；HardLock允许部分ReleasedQty，剩余量继续ACTIVE。
- 🔒 PI物理身份与归属状态正交：PositionSlice/ExecutionLock/MESPlanRelease是互斥物理数量身份；Hard/Soft/未分配是同一物理供给内部的归属状态，禁止直接相加双计。
- 🔄 Candidate保持Base库存/在途切片，但按Candidate DataCutoffTime读取最新MES累计事实形成独立快照；激活前校验发布、执行锁、硬锁及实耗变化。
- 📦 APS_Auth由独立脚本`APS_Auth数据库DDL_v1.0.sql`部署；PlanVersion分区预建至100000（每100版本一分区），并明确结果数据保留期与清理作业责任。
- 🔢 正文编号收口：管道供给主链定位为§2.7.8；MES生产进度§2.9置于跨厂Pegging§2.10之前，避免重复或逆序章节号。

> **v1.38覆盖声明**：下方历史版本中的MES主动下发成功、出站回执台账、MQ/REST实绩与MES_Actual_Staging仅供追溯；当前开发、测试和联调一律执行v1.38双向视图与运行快照口径。

## v1.37 变更说明（2026-07-29 六份文档全局机械审计与反向场景审计修正）

- 🔧 `Task`执行身份补齐`ProductionInstructionNo + StageCode`，用于MES发布单元分组和ExecutionLock创建；`MTS_InstructionNo`降为历史兼容字段。
- 🔧 `PeggingAllocationLedger`新增`AllocationSequence`、`TaskComponentQty`：前者提供PlanVersion内幂等，后者隔离Task需求份额与供需AllocatedQty，避免多层、多来源重复累计。
- 🔧 `ExecutionLock`/`DemandSupplyHardLock`生命周期约束闭合；HardLock创建/恢复增加同一供给累计剩余硬锁量不超过真实可用量的应用层校验。
- 🔧 `PeggingSupplyAllocation`与Ledger建立PlanVersion内一对一防重；Ledger FinalTaskId按复合键约束当前PlanVersion Task。
- 🔧 移除当前接口正文中的Pipeline固定空集合残留；正式枚举统一为`INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / VMI_ONSITE / ARRIVED_NOT_RECEIVED`。
- 🔧 `Task.IsLocked`与`ExecutionLock`概念分离：前者是计划冻结，后者是现实MES执行。

## v1.36 变更说明（2026-07-28 Pegging数量闭合、PI位置快照、软硬归属与跨版本执行防腐闭环）

- 🆕 **生产指示供给位置快照**：新增 `ProductionInstructionSupplySnapshot` 与 `ProductionInstructionPositionSlice` 的架构定位，按 `ScheduleRunId + DomainKey + ProductionInstructionNo` 保存生产指示总量边界、互斥 Stage/XC/在途/未定位位置、来源截止时间与异常裁剪结果。生产指示可生产量唯一口径为 `max(0, Order.Quantity - Order.ReceivedQty)`；MES完成量、XC、在途只负责定位，不再次改变总量。
- 🆕 **Stage位置互斥与保守裁剪**：`StageProgressSnapshot` 是大工艺累计进度权威输入；下游累计完成量超过上游时保守下修下游，中间 Stage 缺失时采用下游已证明的最小完成量；`ERPProperty=XC` 且映射到某 Stage 时表示该 Stage 尚未完成；无法定位的数量必须进入 `UNLOCATED`，从承载路径最早 Stage 保守生成后续任务。
- 🆕 **统一分配账本与非Task供给边界**：新增 `PeggingAllocationLedger` 架构定位；每笔分配必须由2号位同时扣需求余额、扣供给余额并写 Ledger。`PeggingSupplyAllocation` 继续只保存已确认的库存/在途/Received 等非Task供给分配，物理 `Pegging` 继续只表达 Task-to-Task 血缘。该历史版本当时尚未检出 `PeggingSupplyAllocation` 正式建表语句；此缺口已由 DDL v5.2.0 及后续版本补齐，当前不存在。
- 🔄 **Task生成时机收口**：Pegging阶段只形成 `LogicalBlock / TaskDraft`；1号位基于有限产能、交期、资格和批量约束执行合并/拆分与时间排定，返回 `ScheduledTaskDraft + ComponentShares`；2号位随后批量持久化正式 Task、Ledger最终Task映射与物理 Pegging。禁止先落正式Task再在排程结束后硬合并。
- 🆕 **跨版本执行事实与需求归属分离**：新增 `ExecutionLock`（现实MES执行过程）与 `DemandSupplyHardLock`（需求硬归属）的防腐定位。MES实时工单视图确认建单并进入`RELEASED`时只自动形成ExecutionLock，不自动把普通需求—生产指示关系永久固定；普通通用执行产出可在次日按最新优先级重新Pegging，特殊出荷指示类型、客户专属、环保/质量资格等才形成 HardLock。
- 🔄 **新TaskId恢复方式**：Task/Pegging仍随 PlanVersion 重建；新版本 Task 通过 `Task.ExecutionLockId` 关联同一稳定 MES 工单，不复用旧TaskId、不重复生成发布单元或MES工单。一个正式 Task 最多关联一个 ExecutionLock，不同现实 MES 工单不得在新版本中伪装合并。
- 🔄 **Candidate剩余供给重构**：废止“Base原始供给－Base全部已分配”的单一公式；Candidate按实际已消耗、HardLock、ExecutionLock剩余投入、ExecutionLock未来产出、Scope外Soft、Scope内Soft和未分配供给分类恢复。Scope内Soft可释放重算，Scope外Soft暂时保留，实际消耗和HardLock不得释放。
- 🔄 **管道供给由固定空跑改为按来源启用**：统一主链为各来源ODS契约视图 → APS包装视图 → `ext_PipelineSupply_Source_View UNION ALL` → `sp_SyncPipelineSupply` → `SupplyFact_Pipeline` → 精确运行切片。尚未接入的来源分支可返回0行，但禁止把整个 V1 主链固定写成空集合；夜间按当前 BatchNo，Candidate按 Base运行准确切片读取。
- 🔄 **插件与Voucher边界瘦身**：V1不建设动态热插拔插件平台。规则变化优先进入 `RuleSet / ParameterSet / StrategyProfile`；只有稳定算法差异使用少数 .NET 策略接口。普通领域计算返回只读 Result，Pegging返回 `PeggingAllocationDecision`，只有审批或正式状态变化使用 Voucher。
- 📌 **V1物理瘦身**：不建独立 SoftAllocation 表、不建 DemandPromiseConstraint 表、不建 ExecutionLockTaskLink 表；`Task.ExecutionLockId`直接承接跨版本关系；Task—需求份额由 Ledger + `vw_TaskDemandAllocation` 查询；执行历史只保存创建、取消、完成、人工解除等关键事件，不做完整事件溯源。
- 📌 **性能边界**：Domain输入一次性装入内存，核心分配循环中禁止逐行查库和逐行写库；PI快照、Ledger、非Task分配、Task及锁对象使用 TVP / SqlBulkCopy / 批量SP 分段落盘，并在单 Domain 发布事务内完成闭合校验。
- 📌 **保护区域不变**：一Run多Domain PlanVersion、Domain独立发布、`PARTIAL_SUCCESS`、`ExpectedDomainKeysJson`独立字段、ScopeJson固定11字段、白天Candidate严格单Domain、规则参数六表、BOM/Stage RAW、Routing主链、Task五态及设备故障不自动暂停等均不借本轮修改重构。

> **v1.36覆盖声明**：下方历史版本中“V1 PipelineSupplies固定为空”“Candidate=Base原始供给－Base全部已分配”“2号位先实例化正式Task”“RELEASED自动固定原需求—生产指示关系”等旧描述，仅用于版本追溯；当前开发、测试和后续文档同步一律以v1.38正文为准。

---

## v1.35 变更说明（2026-07-20 对齐 APS V1 最终决策 0号位确认）

- 🔄 **`ExpectedDomainKeys` 迁移为 `ScheduleRun` 运行级字段**：原 `ScopeJson.ExpectedDomainKeys` 改为 `ScheduleRun.ExpectedDomainKeysJson`，为 ScheduleRun 独立运行级不可变字段；`ScopeJson` 继续固定 **11 字段**，不得新增 `ExpectedDomainKeys`。
- 🔄 **运行类型校验矩阵更新（§2.8.2 / §2.8.12）**：
  - `FULL_SCHEDULE`：创建 `ScheduleRun` 时 `ExpectedDomainKeysJson` 必须非空 JSON 数组（≥1 不重复 DomainKey）；`ScopeJson` 可为 NULL；`Purpose` 不要求填写
  - 白天 Candidate（`LOCAL_RESCHEDULE` / `MANUAL_RESCHEDULE` / `INSERT_ORDER_WHATIF`）：`ExpectedDomainKeysJson` **恰好 1 个元素 = `BasePlanVersion.DomainKey` = `CandidatePlanVersion.DomainKey`**；`ScopeJson` 继续固定 11 字段并按 RunType+Purpose 校验；**不得为保存 DomainKey 修改 11 字段 ScopeJson**
  - `SIMULATION`：阶段二骨架可用独立 `ExpectedDomainKeysJson`（1 或多个），不进入 `ScopeJson`
- 🔒 **`ExpectedDomainKeysJson` 不可变规则（§2.8.8）**：创建 `ScheduleRun` 时一次性冻结；运行过程禁止追加/删除 Domain；不得按实际已创建 PlanVersion 反推；不得因某 Domain 启动失败而从预期集合移除；不需数据库 Trigger，由 3号位创建服务与 2号位运行服务共同校验。
- 🔄 **多 Domain 独立发布（保持 §2.8.3）**：一次 `FULL_SCHEDULE` 对应多个 Domain PlanVersion（每 DomainKey 一个，经 `SourceScheduleRunId` 关联）；Domain 独立计算/落盘/发布；一个无关 Domain 失败不得阻止其他成功 Domain 发布；**废止旧 `ALL_OR_NOTHING` 全域一致发布**。
- 🔄 **`PARTIAL_SUCCESS` 与终态（§2.8.7）**：`ScheduleRun.Status` `RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED`；`COMPLETED`=所有 `ExpectedDomainKeysJson` 成功；`PARTIAL_SUCCESS`=部分成功部分失败/缺失；`FAILED`=致命错误或零成功；`CompletedAt` 所有终态含 `PARTIAL_SUCCESS` 写入。
- 🔒 **`BUILDING` 有限重试与 `FAILED` 终态（§2.8.3）**：某域重试耗尽或确认不可恢复必须将该域 PlanVersion 转 `FAILED` 并写 `ErrorMessage`，不得无限期停留 `BUILDING`；运行级致命错误下已创建未完成的域 PlanVersion 统一标记 `FAILED`，不得让 `ScheduleRun` 永久 `RUNNING`。
- 🔄 **跨域失败（§2.8.10）**：产生 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 原因事实（`ObjectType=DOMAIN`）+ `RescheduleRecommendation`，由 PMC/0号位人工选择相关 Domain 重算；V1 不自动回滚已成功上游、不建跨域多 Domain Candidate、不建原子激活组。
- 🗑️ **删除 V1 暂停闭环（§2.8.11）**：`任务暂停/恢复 PAUSE→PAUSED` / `RESUME→RUNNING`、`TaskPauseVoucher` / `TaskResumeVoucher` 正式流程改为"V1 不实现，V2 预留"；`Task.Status` 正式值域 `PLANNED / RELEASED / IN_PROGRESS / COMPLETED / CANCELLED`，无 `PAUSED` / `SUSPENDED` / `WAITING` / `PENDING` / `RUNNING`。
- 🔄 **ReasonCode 字典统一（§2.8.13）**：`DUE_DATE_VIOLATION`→`DUE_DATE_RISK`；删除未登记示例 `DUE_DATE_TIGHT`、`UPSTREAM_DELAY`（上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`，`ObjectType=DOMAIN`）；列出权威 ReasonCode 列表。
- 🗑️ **删除旧口径**：清除 `ALL_OR_NOTHING` 现行表述（历史"已废止"说明保留并标注）、单全局 PlanVersion 表述。

---

## v1.34 变更说明（2026-07-13 白天实时评估与局部重排链路，对齐 DDL v5.1.0 / 字段说明 v5.1.0 / 演变总表 v3.30 / 集成接口 v1.24 / 核心走查 V3.15）

- 🆕 **实时链路 DDL 对象**：
  - 新增 `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`（业务语义与批量表 `MES_APS_BOM_Workset_CrossFactoryEdge` 对齐；使用 `RequestDetailId` 替代 `BatchNo` 作为隔离键；三个索引；0 行合法）
  - 新增 `sp_GenerateBOMCrossFactoryEdgeRealtime`（由 `sp_EnrichBOMWorksetRealtime` 在 Step 5 日志前调用；按 RequestDetailId 严格隔离；StageDict.FactoryCode 权威取值；`STAGE_DICT_NOT_FOUND` 登记 WARN 不阻断）
- 🔄 **`MES_API_BOM_Request_Realtime` 字段口径**：`RequestDetailId` / `OrderCanonicalId` 均为逻辑关联 BIGINT NULL；DDL 无物理 FK；READY 以 `Status='READY'` 为唯一权威；`ExpandedRowCount` 仅诊断；同一 RequestDetailId 多请求时按 `Id DESC` 取最新
- 🔄 **`MES_APS_BOM_Workset_Issues.BatchNo` 口径**：批量链路使用正式 BatchNo；实时链路正式路径 `RT:RD:{RequestDetailId}`；`RT:{ResolvedBOMNO}` 仅 deprecated 兼容（用 LEFT 截到 50 字符）
- 🔄 **`sp_ExpandBOMRealtime_vNext` 修复**：统一使用 `@SyntheticBatchNo`（vNext 内 `@BatchNo` 未声明缺陷修复）；MISSING_MATERIALCODE 也使用 `@SyntheticBatchNo`；READY 更新时回填 `ExpandedRowCount`
- 📌 **保护区域**：夜间 ScheduleRun / PlanVersion 创建时序不变；规则与参数引擎 6 张治理表不变；三张 APS RAW 表结构不变；批量 CrossFactoryEdge 表和 SP 不变；PeggingSupplyAllocation / 物理 Pegging 定位不变

---

## v1.31 变更说明（2026-06-15 管道供给链路完整骨架 + 分层语义修正 + 字段契约锁定，对齐 DDL v5.0.42 / 演变总表 v3.27）

- 🔄 **§2.5 管道供给链全面升级**：
  - 补充完整分层结构图（ERP源→ODS视图→APS包装视图→SP→表→Context）
  - 修正错误表述：`ext_ERP_InterplantInTransit_View` 是 APS 跨库包装视图，不是 ODS 侧对象
  - V1.1/V2 TODO 升级为完整14字段契约 + 数据映射说明 + 异常处理规则
  - 新增异常处理说明：MasterID无法映射、FactoryCode无法映射、Quantity<=0等场景
- 🆕 **新增字段契约锁定规则**：ODS 契约视图字段结构为强契约，V1.1/V2 允许调整视图内部实现逻辑
- 📌 **V1 管道供给链说明升级**：明确 `ScheduleContext.PipelineSupplies` 为空集合属于正常结果
- 📌 **统一分层语义**：ODS视图=「ODS层/MES_Integration/来源ERP/5号位」；APS包装视图=「APS层/APS_Production/2号位」

## v1.33 变更说明（2026-06-23 规则与参数引擎 + 跨厂Pegging补强）

- 🆕 **§2.8.6 规则与参数引擎接入运行编排**：RuleSetVersion/ParameterSetVersion/StrategyProfileVersion 策略包体系
- 🆕 **§2.10 跨厂Pegging防腐层补强**：ERPProperty真实来源 / ERP_Received_ByDocument_View / CrossFactoryEdge / PeggingSupplyAllocation
- 📌 **跨厂模式**：STAGE_HANDOFF（大工艺接续，M库判定）vs INTER_FACTORY_ORDER（先查在途→再查ZP/BP Received→进入生产排产）
- 📌 **物理 Pegging 表**：仅 Task-to-Task 血缘；非 Task 供给写入 PeggingSupplyAllocation
- 📌 **V1 不做**：ScheduleConfigSnapshot / ScheduleRunStrategyProfile

## v1.30 变更说明（2026-06-12 MES生产进度汇总防腐链路 + 订单状态准入 + Task/Pegging重算口径 + EAM V1预留，对齐 DDL v5.0.41）

- 🆕 **§2.9 新增 MES生产进度汇总防腐链路**：三条 ODS 契约视图（`MES_APS_WorkOrder_View` / `MES_APS_OperationProgress_View` / `MES_APS_StageProgress_View`）→ 三张 APS 本地快照表（`MESWorkOrderSnapshot` / `OperationProgressSnapshot` / `StageProgressSnapshot`）→ 三个同步 SP（DDL v5.0.41）；ODS UNION ALL 统一视图由5号位收口，APS快照同步由2号位负责
- 📌 **§2.2 订单状态准入（v3.12 收窄）**：`Order_Canonical.Status` 只有三种业务値：OPEN / CLOSED / CANCELLED；活跃根集合、BOM Request、Order 分区表只接收 `WHERE Status = 'OPEN'` 的订单/生产指示；CLOSED/CANCELLED 不得进入 BOM Request 或生成 Task/Pegging
- 📌 **§2.9 Task/Pegging 全量重算口径**：Task 和 Pegging 随新 `PlanVersionId` 每日全量重新生成；MES 进度只用于计算当日剩余 Task，不匹配历史 TaskId；Pegging 不跨版本复用
- 📌 **§2.9 EAM V1 预留**：`EAM_APS_Resource_View` 占位声明；V1 不读取 EAM 数据，不生成资源不可用窗口
- 🔧 **V1 工序识别主字段**：`OperationName`（MES 工序名称）；不以 MES 工序编码为主匹配字段（MES 编码不跨大工艺稳定）

## v1.29 变更说明（2026-06-08 DDL 建表顺序修复 + 库存六层架构定稿，对齐 DDL v5.0.40）

- 🔧 **P0 DDL 建表顺序修复**：`ProductFamily`/`Factory` 提前至 §2.4a；`ProductionDepartment`/`InventoryAvailabilityRule` 外键引用不再失败
- 🔄 **§2.4.5 六层库存架构定稿**：平了“五层”残留描述；六层 = 事实→候选→规则→明细（InventoryAvailableSupplyDetail）→余额→内存
- 🔄 **§5.2.4.5 新增 `InventoryAvailableSupplyDetail` 章节**：定位、字段说明、引索已在上一版本建立
- 🔄 **v1.18 changelog 修正**：“现货库存五层主链完全不动” → 六层结构说明

## v1.28 变更说明（2026-05-31 库存规则 V1口径收敛，对齐 DDL v5.0.39）

- 🗑️ **§2.5 设计决策写死 修正**：移除"现货链 `ProductFamilyInventoryScope` + `InventorySourceRule` 不动"旧表引用；改为"统一使用 `InventoryAvailabilityRule`"
- 🆕 **§2.4.5 sp_SyncInventorySnapshot**：新增现货库存快照同步六步 ETL 说明（步骤/目标表/关键设计决策）
- 📌 **FactoryId 红线修正**：库存/供给事实+规则+余额表均可持有 `FactoryId FK`
- 📌 **V1 空链路预留**：`ERP_InterplantInTransit_View` ODS侧返回0行；`sp_SyncPipelineSupply` 正常空运行
- 📌 **`SupplyFact_Pipeline.ProductFamilyId` 可为 NULL**：⚠️ **本条已被 v1.31 修正。** 仅当 MaterialId 已成功映射但 Material.ProductFamilyId 本身为空时，允许 ProductFamilyId=NULL 并参与产品族通配规则。MasterID 无法映射 MaterialId 时，该行不得写入 SupplyFact_Pipeline，不得通过 NULL 通配规则兜底。

## v1.27 变更说明（2026-05-30 产品族解析链，对齐 DDL v5.0.37）

- 🆕 **新增 §2.4.4 产品族解析链**：完整记录 ODS 层解析调度、三张新表、`sp_ResolveMaterialProductFamily` 设计、`sp_SyncMasterData` 步骤1c、设计红线
- 🔄 **§2.4.2 主数据演进路径图**：在 ERP/MES 主数据路径之前补充 ODS 产品族解析节点
- 🔄 **§2.4.3 `sp_SyncMasterData`**：步骤1c 新增 ProductFamilyCode→ProductFamilyId 码表映射
- 📌 设计决策：产品族解析逻辑完全封装在 ODS 层；APS_Production 层封禁包含任何解析规则
- 🛑 红线：`Order.ProductFamilyId` 从 `Material.ProductFamilyId` 继承，禁止在订单层另建解析链

## v1.23 变更说明（2026-05-21 BOM入口分流R28/R29/R30/R31，对齐 DDL v5.0.28）

- 🔄 **§2.3.2 `sp_ExpandBOMBatch_vNext` 追加入口裁决补充**：BOMNO IS NULL 时 Stage B/C 按下列规则分流（详见《BOM_Workset_方案v1.7》§1.4）
  - R28（SALES_ORDER+ASSY%）：`ProcessCodeDict` 出口库 `ParentProcRefCode` 过滤；CN6课无出口库时取母体工厂CN代理
  - R29（SALES_ORDER+WIP%/RAW%）：原Case B直查行为
  - R30（SALES_ORDER+RAW%+无BOM）：外购件兜底，不登记`BOM_ENTRY_NOT_FOUND`
  - R31（PRODUCTION_INSTRUCTION+BOMNO IS NULL）：直查同R29，**必写**`Issues(BOMNO_MISSING_PRODUCTION)`
- 🔄 **§2.3.2 `sp_ExpandBOMRealtime_vNext` 同步**：R28/R29/R30/R31 分流对齐批量链路

## v1.22 变更说明（2026-05-16 订单提升链路重构，对齐 DDL v5.0.27）

- 🔄 **§2.2.0 订单链路总览图**：`ERP_Order_Staging` 结构注记补 v5.0.27 局時变更（MaterialCode可空、+Model/CustomerCode/RawNonStockShipmentType/RawOrderSource）；`sp_ValidateAndPromoteOrders` 说明全量更新（三级解析链、MATERIAL_MAPPING_AMBIGUOUS、ORDER_TYPE_UNKNOWN、UNKNOWN存CustomerSegment、BOMNO_MISSING非阻断）
- 🔄 **§2.2.0 夜间00:05透传列表**：补 `SourceModel`/`NonStockShipmentType`/`OriginalOrderSource`
- 🔄 **§2.2.1 设计红线**：FailureCode单值分层语义补充（阻断/非阻断诊断区分）
- 🔄 **v1.19 设计决策写死**：更正 `CustomerSegment` 无匹配规则（OVERSEAS→UNKNOWN）
- 🔄 **ERP_Order_Staging.FactoryCode** NOT NULL→NULL（V1 TODO桩；不迫使ERPOrderSyncService写占位值；V2补规则转换）
- 🔄 **CustomerCodeMap注释修正**：废止"失效→OVERSEAS"旧口径；IsActive=0行JOIN过滤；无有效匹配→UNKNOWN

---

## v1.21 变更说明（2026-05-14 BOM防腐层物化边表架构调整，对齐演变总表 v3.18 / DDL v5.0.26）

- ⚠️ **§3.1.3 Socket 殑位重定位**：`MES_BOM_View` 降为兼容视图（`SELECT * FROM MES_BOM_Edge_Active`）；新入 `MES_BOM_Edge_Active`（物化边表，V1兼任合同层+执行优化层）为 Socket 正式承承对象
- ⭐ **§3.1.3 Plug 补充**：5号位 `sp_RefreshBOMEdgeActive` 负责刷新 `MES_BOM_Edge_Active`；`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` 直接读 `MES_BOM_Edge_Active`（禁止对 MES_BOM_View 做递归 CTE）
- ⭐ **§3.1.3 Plug 补充**：5号位 `sp_CleanupBOMWorkset` 级联清理改为按 WorksetId 删除 StageDetail（替代旧 BatchNo+BOMNO+Material 关联清理）
- ▸ **§3.1.3 Loader 补充**：2号位搬运 StageDetail 时透传 `WorksetId`；Workset_Archive/Realtime 新增 `RequestDetailId` 通道
- 📌 **设计决策写死**：V1 `MES_BOM_Edge_Active` 兼任合同层+执行优化层；V2 视需要拆出 `MES_BOM_Edge_Contract`（多源历史/非活跃版本/裁决过程/审计追溯）
- ▸ **§2.3.2 新增 `sp_ExpandBOMBatch_vNext` WHILE 伪代码骨架**：RefreshLog 前置校验 + WHILE 每层 JOIN `MES_BOM_Edge_Active` + RequestDetailId 逐层透传 + 旧 `sp_ExpandBOMBatch` 标记 deprecated
- 📌 **设计决策写死**：RequestDetailId **不进** StageDetail 表；经 WorksetId→Workset.RequestDetailId 反查即可

## v1.20 变更说明（2026-05-13 阶段二三接缝：运行编排框架 §2.8 新增）

- 🆕 **§2.8 运行编排框架（ScheduleRun / RunType）**：描述所有排程触发路径的统一入口、`ScheduleRun` 与 `PlanVersion` 的关系与边界、各 RunType 的激活/不激活规则、仿真版本 CANDIDATE 语义
- 📌 **设计决策写死**：`ScheduleRun` 是对现有"直接生成 PlanVersionId"流程的**最小包装**，阶段一不改排程内核
- 📌 **设计决策写死**：`SIMULATION` / `INSERT_ORDER_WHATIF` / `MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` 产出 `PlanVersion` 默认 CANDIDATE，**禁止自动激活**，须 3号位显式触发；`FULL_SCHEDULE` 完成后调度器可自动激活
- 🔄 **V1 最终决策对齐（修正原单数 PlanVersion 口径）**：`FULL_SCHEDULE` 不再产出"一个全局 PlanVersion"，改为按 `ExpectedDomainKeys` 为每个 `DomainKey` 各落一条 Domain PlanVersion（`SourceScheduleRunId` 关联）；各 Domain 独立事务发布，废止 `ALL_OR_NOTHING` 全域一致发布；`ScheduleRun.Status` 新增 `PARTIAL_SUCCESS`（RUNNING/COMPLETED/PARTIAL_SUCCESS/FAILED，所有终态含 PARTIAL_SUCCESS 写 CompletedAt）；`ExpectedDomainKeys` 启动时冻结不得反推；白天 Candidate 严格单 Domain、跨域由后台按 `Domain_Dependency` 拆分；跨域失败产出 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` + `RescheduleRecommendation` 交 PMC/0号位人工重算（V1 不自动回滚/不建跨域 Candidate/不建原子激活组）；V1 不实现暂停闭环（`TaskPauseVoucher`/`TaskResumeVoucher` 不落地，V2 预留），`Task.Status` 正式值域为 `PLANNED/RELEASED/IN_PROGRESS/COMPLETED/CANCELLED`（不含 PAUSED/SUSPENDED/WAITING/PENDING/RUNNING）。详见 §2.8.1/§2.8.2/§2.8.3/§2.8.7~§2.8.11。

## v1.19 变更说明（2026-05-13 OrderType重构+衍生字段澄清，对齐 DDL v5.0.24）

- 🔄 **§2.2.0 订单链路总览图**：`ERP_Order_Staging` 衍生字段占位说明补 `DelayStatus`；`sp_ValidateAndPromoteOrders` 说明补 OrderType 标准化 + CustomerCodeMap 查找逻辑
- 🔄 **§2.2.0 夜间00:05透传列表**：补 `DelayStatus` 字段
- 🔄 **§2.2.2 活跃根SQL示例**：`'SO'`/`'MTS'` 硬编码字面量改为 `'SALES_ORDER'`/`'PRODUCTION_INSTRUCTION'`
- 🔄 **§3.1.3 Routing Socket-Plug**：`MES_APS_Routing_Operation_View` / `Dependency_View` 字段清单补 `SourceSystem`（追溯增强字段）
- 📌 **设计决策写死（v5.0.27修正）**：`CustomerSegment` 由 `sp_ValidateAndPromoteOrders` 通过 `CustomerCodeMap` 本地映射表推导，非ODS共享字典；CustomerCode为空→NULL；CustomerCode有值但无匹配→`UNKNOWN`（v5.0.27起不再默认`OVERSEAS`）；消费方须识别UNKNOWN走保守路径
- 📌 **设计决策写死**：`DemandMaturityStatus` 收窄为 `PRE_CONFIRMED/FORECAST`；`DelayStatus`（`ON_TIME/FIRST_DELAY/REPEATED_DELAY`）为独立字段，禁止混用

---

## v1.18 变更说明（2026-05-09 管道供给链 新增 §2.5 sp_SyncPipelineSupply，对齐 DDL v5.0.23）

- ✅ §2.5 新增 `sp_SyncPipelineSupply`：管道供给链同步 SP（伪代码 + 字段映射说明）
- 📌 **设计决策写死**：`InventoryBalance` 定义不变；管道链路结果为空时不影响现有排程（⚠️ v5.0.40 补充：现货库存主链已升级为六层，第4层 `InventoryAvailableSupplyDetail` 为规则裁决后明细层，管道链路仍并行独立）
- 📌 **设计决策写死**：`ETA`=ODS原始事实；`AvailableTime`=本地派生（ETA+LeadTimeOffset）；夜间快照用 BatchNo；白天实时读 IsActive=1

---

## v1.26 变更说明（2026-05-26 OrderBomRequestLink业务锚点升级，对齐 DDL v5.0.34）

- 🔄 **§2.3.4 `PullBOMResultFromODSAsync` 签名升级**：`PullBOMResultFromODS(string batchNo)` → `PullBOMResultFromODSAsync(string batchNo, long planVersionId)`；`planVersionId` 由 `NightlyBatchOrchestrator` 显式传入，**禁止** BOMResultPullService 内部自查最新 PlanVersion
- 🆕 **§2.3.4 `GenerateOrderBomRequestLinkAsync` 新增**：数据源 = ODS `MES_APS_BOM_Workset` 聚合（禁止从 `APS_BOM_RAW` 反查）；按 `PlanVersionId+OrderCanonicalId` 查 `[Order]`，找不到则写 `OrderId=NULL, LinkStatus='SKIPPED'`
- 📌 **设计决策写死**：`OrderBomRequestLink` 唯一约束 = `UNIQUE(PlanVersionId, OrderCanonicalId)`（不是 OrderId）；`OrderId` 允许 NULL；数据源必须为 ODS Workset 聚合

## v1.25 变更说明（2026-05-25 RequestDetail字段收敛，对齐 DDL v5.0.32）

- 🗑️ **§2.3.1 PushBOMRequestToODS C#示例**：删除 `Model`/`OrderStagingId`字段列和行赋值；删除 `ResolvedBOMNO` 注释；保留最终 10 字段结构
- 🗑️ **§5.1.2 DDL示例**：删除 Model/OrderStagingId/ResolvedBOMNO 行；标注 v5.0.32
- 🔄 **责任归属改写**：5号位只写 Workset/StageDetail/Issues；2号位在 Workset 同步完成后，从 Level=1 `Workset.BOMNO` 取値写入 `OrderBomRequestLink.ResolvedBOMNO`
- 📌 **设计决策写死**：`MES_API_BOM_Request_Detail` 只保留请求输入：`OrderCanonicalId` + `MaterialCode` + `FactoryCode` + `OrderType` + `RequestedBOMNO`
- 📌 **设计决策写死**：`ResolvedBOMNO` 不进 RequestDetail；归 `OrderBomRequestLink.ResolvedBOMNO`（由 2号位生成）

## v1.24 变更说明（2026-05-25 Order→BOM追溯链闭合，对齐 DDL v5.0.31）

- ✅ **§2.3.1 PushBOMRequestToODS**：C#示例更新为 `MES_API_BOM_Request_Detail` v5.0.31 新结构（`OrderCanonicalId`主锚点 / `RequestedBOMNO` / `OrderNo`/`SourceSystem`/`SourceOrderId`）；唯一约束更新为 `(BatchNo, OrderCanonicalId)`
- ✅ **§2.2.1 白天增量**：Workset缓存判断从 `OrderStagingId` 改为 `OrderCanonicalId`
- ✅ **§2.2.1b 实时插单**：触发条件/流程技术描述更新锚点词
- ✅ **§2.3.2 ODS展开SQL示例**：`d.BOMNO` → `d.RequestedBOMNO`（2处）
- ✅ **§5.1.2 BOM展开请求明细表DDL**：内嵌示例更新为 v5.0.31字段结构
- 📌 **设计决策写死**：`OrderBomRequestLink`生成时机 = BOM Workset + StageDetail同步完成后（2号位责任）；唯一约束 = `(PlanVersionId, OrderCanonicalId)`（v5.0.34 升级，口径不再是 OrderId）；RepWorksetId = `MIN(Workset.Id) WHERE RequestDetailId+Level=1`；OrderId 允许 NULL，找不到时写 SKIPPED，不阻断批次；NightlyBatchOrchestrator 必须显式传入 planVersionId，禁止 BOMResultPullService 自内查最新 PlanVersion

---

## v1.17 变更说明（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21）

- ✅ §2.2.0 订单链路图：BOMNO改可空注释 + `FailureCode`/`NextActionCode` 双维度说明（废除"必填"口径）
- ✅ §2.2.1 白天增量：状态机 FAILED 分支补 `FailureCode`/`NextActionCode`；实时触发判断从"新BOMNO"改为"新订单无Workset缓存"
- ✅ §2.2.1b 实时插单：触发条件更新为无BOM Workset（BOMNO可空场景兼容）
- ✅ §2.3.1 PushBOMRequestToODS：代码示例更新为新 `MES_API_BOM_Request_Detail` 结构（OrderStagingId/Model/MaterialCode/FactoryCode；BOMNO可空）
  > ⚠️ **此口径已被 v1.25 / DDL v5.0.32 废止**：现 RequestDetail 不再包含 OrderStagingId / Model；现行结构见 v1.24/v1.25 变更说明

- 📌 **设计决策写死**：BOM入口解析（有 RequestedBOMNO 直接展开 / 无 RequestedBOMNO 时，5号位按 OrderType + MaterialCode + FactoryCode + BOM边/ProcessCode 规则解析入口）归属**5号位Workset处理阶段**；2号位只写入基础字段透传

---

## v1.16 变更说明（2026-05-04 sp_EnrichBOMWorkset / sp_EnrichBOMWorksetRealtime 完整实现）

对齐 `APS_数据库表结构设计 v5.0.18`，本版将 BOM 回填逻辑从 TODO 占位升级为可执行 SP 调用：

- ✅ §2.3.2 `sp_ExpandBOMBatch`：TODO 替换为 `EXEC sp_EnrichBOMWorkset @BatchNo`
- ✅ §2.2.1b 实时链路：补注 `sp_EnrichBOMWorksetRealtime` SP 名称，明确实时回填由独立 SP 执行
- ✅ 相关权威文档引用版本升级至 v5.0.18
- 【设计决策】实时链路 Issues.BatchNo 采用合成键 `'RT:'+@BOMNO`（NOT NULL 约束兼容）

---

## v1.15 变更说明（2026-04-29 生产部门主链 + ProcessCodeDict 重定位 + WorkshopCode 全局清理）

对齐 `APS_数据库表结构设计 v5.0.16` + `字段说明 v5.0.16` + `架构总表 v3.12`，本版完成两条主链路的防腐层升级：

### A) ProcessCodeDict 防腐定位翻转（v5.0.15 错位修正）

| 口径 | v5.0.15（错位）| v5.0.16（修正）|
|---|---|---|
| 定位 | 「ERP 工序对照表的 ODS 物理镜像/缓存」 | **「APS 自维护的 ODS 增强工序字典」** |
| 维护方 | ERP/MES DBA + `sp_SyncMasterData(@SourceType='ProcessCode')` 自动同步 | **APS 系统管理员人工维护** + **0 号位审批**；不参与自动同步 |
| 字段调整 | — | DROP `LastSyncedAt` / RENAME `SourceSystem` → **`CodeOrigin`**（CHECK：`ERP/MES/MANUAL`）/ ADD **`StageCode`** 增强列 + `UpdatedBy` |
| `MES_ProcessCode_View` 字段契约 | `ProcessCode/ProcessName/FactoryCode/ActualFactoryCode/TrusteeProcCode/IsOutsource/IsRetouch/WarehouseRole/SourceSystem` | 同上 + 新增 **`StageCode`** + RENAME **`CodeOrigin`** |
| `sp_SyncMasterData` 出口 | 包含 `'ProcessCode'` 分支（v1 占位骨架）| **取消 `'ProcessCode'` 分支**（ProcessCodeDict 不再走自动同步） |

**关键约定**：`MES_ProcessCode_View.StageCode` 是 5 号位 `sp_EnrichBOMWorkset` 与 2 号位 `sp_RebuildMaterialStageDeptContext` **共享的基础映射来源**——两边的 ProcessCode → StageCode 必须查同一列（防止静默断裂）。

### B) 生产部门主链注入（部门 = 物料×阶段联合属性）

| 主链节点 | 改动 | 落点 |
|---|---|---|
| 业务事实 | 同物料同 StageCode 下不同部门可有不同小工序集合 | 新增 `ProductionDepartment` 字典（APS 排程责任部门，1:1 归属 StageCode） |
| 1 号位排程入口 | `(MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套` | 新增 `MaterialStageDeptContext` 表（SCD Type 2，1 号位**唯一**消费入口） |
| 2 号位组装 SP | 新增 `sp_RebuildMaterialStageDeptContext` 三触发模式（FULL/INCR/PARTIAL）⚠️ **占位骨架，当前未实现**（DDL Step1~6 全 TODO） | DDL v5.0.16 §4.3c |
| 人工维护入口 | 新增 `MaterialStageDeptOverride` 表（Model/MaterialCode + StageCode + DeptCode）；导入 Model→MaterialCode 1:N **拒收** | DDL v5.0.16 §2.4c |
| 降级登记 | 新增 `MaterialStageDeptContext_Issues` 表；旧值不动、新问题登记，待人工修正后局部重建 | DDL v5.0.16 §2.4e |
| Routing 三件套 | `RoutingOperation` / `RoutingDependency` / `OperationResourceEligibility` 加 `ProductionDepartmentId NOT NULL` + 唯一键升级三元组 | DDL v5.0.16 §2.6.7b/c/d |
| Resource 表 | 删 `WorkshopCode` + 加 `ProductionDepartmentId NOT NULL` + `SourceProductionDeptCode` | DDL v5.0.16 §2.6.4 |
| MSC 表 | 加 `DefaultProductionDepartmentId`（FK，与 `DefaultProductionDeptCode` 双轨） | DDL v5.0.16 §2.4 |
| StageLeadTimeParam | `WorkshopCode` → `ProductionDeptCode`（口径全局统一） | DDL v5.0.16 §2.15 |

### C) ODS 契约视图字段升级（4 个视图，走 DBA 审批）

| 契约视图 | 新增字段 | 删除字段 |
|---|---|---|
| `MES_APS_Resource_View` | **`ProductionDeptCode`** | `WorkshopCode`（业务确认 MES 也无此概念） |
| `MES_APS_Routing_Operation_View` | **`ProductionDeptCode`** | — |
| `MES_APS_Routing_Dependency_View` | **`ProductionDeptCode`** | — |
| `APS_OperationResourceEligibility_View` | **`ProductionDeptCode`** | — |
| `MES_ProcessCode_View` | **`StageCode`** + RENAME `SourceSystem` → `CodeOrigin` | — |

**字段契约升级流程**（防腐红线 #2 守护）：DBA 提交契约视图变更 PR → 0 号位审批 → 上线；任何下游消费方代码零改动（`ext_*` 跨库包装视图自动透传）。

### D) `sp_SyncResourceData` 升级双字典映射

```text
原（v5.0.13）：
  LEFT JOIN Factory f ON f.FactoryCode = v.FactoryCode
  →  WorkshopCode 字段直接透传

新（v5.0.16）：
  LEFT JOIN Factory f              ON f.FactoryCode = v.FactoryCode
  LEFT JOIN ProductionDepartment d ON d.DeptCode    = v.ProductionDeptCode AND d.IsActive=1
  → ProductionDepartmentId 通过双字典映射；任一未命中即跳过该行（登记 APS_ETL_Log，不阻塞批次）
```

### E) 关键设计决策

1. **R20 跨组织视角零特殊逻辑**——StageCode 已采目标工厂视角，按 `(MaterialId, StageCode)` 查 Context 天然得到目标工厂部门
2. **NULL 哨兵放弃**——业务确认 MES 工艺数据全部带部门，Routing 三件套 `ProductionDepartmentId NOT NULL`，**不引入** `_UNSPECIFIED` 哨兵
3. **降级哲学借用 BOM_Workset_Issues**——批次永不阻塞；冲突/缺失登记 `MaterialStageDeptContext_Issues`，旧 IsCurrent=1 记录不动；待人工修正后局部重建
4. **ProductionDepartment 与 ResourceOrgGroup 严格区分**——前者排程主链维度，后者看板筛选切片，**职责不同，不可合并**
5. **ProductionDepartment 与审批组织表解耦**——本表不接审批组织树；未来审批可有 OrgUnit 表与本表做映射

**相关权威文档（v1.15 基线，2026-04-29）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.16**
- `APS_数据库字段说明文档_v5.0` → **v5.0.16**
- `APS_各类基础数据分层承接与演变总表_v5.0` → **v3.12**

---

## v1.14 变更说明（2026-04-25 资源 ODS 契约视图命名统一 + sp_SyncResourceData 占位 SP）

对齐 `APS_数据库表结构设计 v5.0.13`，收敛历史遗留的资源 ODS 视图命名不一致问题，并加入与 `sp_SyncMasterData(@SourceType)` 同构的 `sp_SyncResourceData(@SourceType)` 占位实现：

| 口径 | 改动前 | 改动后 | 本文档落点 |
|---|---|---|---|
| ODS 契约视图 | `APS_Resource_View`（按消费方命名，孤例）| `MES_APS_Resource_View`（按来源命名，与 MES_APS_Routing_*_View 对齐）| §3.1.2 契约视图定义 |
| APS 跨库包装视图 | `ext_APS_Resource_View` | `ext_MES_APS_Resource_View` | §3.1.2 + §3.1.3 Data Loader 清单 |
| 资源同步 SP | 无（散落在 `IDataLoader` 注释）| `sp_SyncResourceData(@SourceType)`（DDL v5.0.13 新增）| §3.1.3 Data Loader 清单 + 本节下面简要说明 |
| EAM 扩展路径 | 仅用 `SourceSystem` 字段区分 | 双源同构视图 + `sp_SyncResourceData(@SourceType='EAM')` 分支 | 未来 EAM 上线只需补 `EAM_APS_Resource_View` 同构契约 |

**sp_SyncResourceData 职责**（v1 占位）：
- `@SourceType='MES'`：读 `ext_MES_APS_Resource_View` → MERGE 全量刷新 `Resource` 表；FactoryCode→FactoryId 映射不命中的行登记 `APS_ETL_Log` 跳过（不阻塞批次，与防腐层“永不阻塞”红线一致）；
- `@SourceType='EAM'`：RAISERROR `NOT_IMPLEMENTED`，登记 `SKIPPED` 日志；
- 调用时机：每天 00:10（v1.14.1 对齐走查 V3.4：与 `sp_SyncMasterData` 同窗口并行，二者同属外部主数据镜像且执行时间秒级；Resource 变化频率低，不做增量）；
- 删除策略：v1 暂**不**自动停用源端没有的旧资源（避免误删），由 2 号位审阅后手工处置；
- 三表协同预留：资源侧当前没有对称的 `ResourceMapping/ResourceSupplyContext`，守住单表镇式；未来若业务需要可按 `sp_SyncMasterData` 三表协同模式扩展。

**命名口径永久统一红线**：ODS 契约视图一律按「源系统_消费方_实体_View」三段式，单源时可省略消费方（如 `ERP_Master_View`），不得再出现按消费方单独命名的独苗。

**相关权威文档（v1.14 基线）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.13**
- `APS_数据库字段说明文档_v5.0` → **v5.0.13**
- `APS_集成接口设计_v1.12.md` → **v1.12**
- `APS_各类基础数据分层承接与演变总表_v5.0` → **v3.11**
- `APS_资源与工艺数据模型重设计方案_v5.0` → **v5.1**

---

## v1.13 变更说明（2026-04-24 工艺数据三层模型收敛）

本版对齐《BOM_Workset_生成与错误处理技术方案 v1.2》+《APS_数据库字段说明文档 v5.0.12》+《APS_数据库表结构设计 v5.0.12》：

| 口径 | 权威位置 | 本文档落点 |
|---|---|---|
| **三层模型**（OperationName / ProcessType / StageCode 互不替换）| `APS_数据库字段说明文档 v5.0.12` §1.9b + §1.9c | §2.3.2 step 5a 注释补"StageCode 取自 StageDict / 他用方视角" |
| **BOM↔Routing 对接主键 = (MaterialCode, StageCode)** | `BOM_Workset v1.2` §3.6 | §2.3.3 Workset/StageDetail DDL 注释呼应 |
| **StageSeq 唯一权威 = StageDetail.StageSeq**；RoutingStage.StageSeq 已删除 | DDL v5.0.12 §2.6.7c2 | §2.3.3 StageDetail 列注释更新 |
| **R20 跨组织视角** = StageDetail.StageCode **目标工厂视角**（如 BJ_MACH） | DDL v5.0.12 §1.9c + BOM_Workset v1.2 §3.1 | §2.3.2 step 5a 注释：视角规则 |
| **ProcessType 配置化** = `ProcessTypeDict`（预留骨架 IsActive=0）| DDL v5.0.12 §1.9d | 本节列出，不进入排程主链说明 |
| **OperationCode 不全局字典化**（MES 不可控 + 新增频繁）| — | 决策留痕 |

**相关权威文档（v1.13 基线）**：
- `BOM_Workset_生成与错误处理技术方案` → **v1.2**
- `APS_数据库字段说明文档_v5.0` → **v5.0.12**
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.12**
- `APS_各类基础数据分层承接与演变总表_v5.0` → **v3.10**

---

## v1.12 清稿完成说明（2026-04-24）

本版将 v1.11 的"顶部覆盖声明"转化为**正文清稿**，以下 5 项口径已在相关章节直接落地：

| 口径 | 当前权威位置 | 本文档落点 |
|---|---|---|
| **BOM 异常处置 = 降级 + 登记，批次永不阻塞** | `BOM_Workset_生成与错误处理技术方案 v1.1` §4 | §2.3.2 SP 注释（去除任何"放行校验"字样）；状态机 PENDING→PROCESSING→READY，`FAILED` 仅保留给 SP 进程崩溃 |
| **Issues 表 +`DegradeAction` 列** | `APS_数据库字段说明文档 v5.0.11` §1.5 | §2.3.2 step 5 注释引用；§2.3.3 Issues 表指向 DDL v5.0.11 |
| **R17/R20 Produce→工厂规则资产化** | `APS_数据库表结构设计 v5.0.11` §1.9b `ProduceToFactoryMap` | §2.3.2 step 5 注释：`ChildRequiredFactory` 查 `ProduceToFactoryMap` 得出 |
| **R20 跨组织交接** | 同上（字段 `ShouldDrilldown=1 + CrossOrgHandoffFlag=1`）| §2.3.2 step 5 注释说明"本厂下钻 + 打标签"；1 号位消费时读标签跳过本厂 Task |
| **StageCode 全局字典** | `APS_数据库表结构设计 v5.0.11` §1.9c `StageDict`（方案 B 工厂+阶段码）| §2.3.3 StageDetail 的 StageCode 列注释指向 StageDict |

**v1.10 changelog 中的"放行校验（CRITICAL/ERROR 阻塞、WARN 放行）"和"【放行策略】批次 READY 前检查 Issues"两条已作废**，保留在历史 changelog 区仅作版本追溯，正文不再引用。

**相关权威文档**：
- `BOM_Workset_生成与错误处理技术方案` → **v1.1**（降级矩阵、运营 SLA、StageScopeType 值域 EDGE/ROOT）
- `APS_数据库字段说明文档_v5.0` → **v5.0.11**（Issues 表 DegradeAction、ProduceToFactoryMap、StageDict）
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.18**（配套 DDL，含 sp_EnrichBOMWorkset / sp_EnrichBOMWorksetRealtime 完整实现）
- `APS_各类基础数据分层承接与演变总表_v5.0` → **v3.9**（架构总纲）

---

**v1.10更新内容**（2026-04-23 R17/R25/R26/R27 工厂映射 + BOM错误容错，基于《BOM_Workset_生成与错误处理技术方案_v1.0》）：
- ▸ **稳定合同原则强化**：Workset/StageDetail 核心表最小改动；ERP 特征字段不下沉到 L1/L2 合同层
- ✅ §2.3.2 sp_ExpandBOMBatch / §2.2.1b Realtime：5号位回填注释补 `ChildRequiredFactory`（R17推导）+ Issues 写入 ~~+ 放行校验（CRITICAL/ERROR 阻塞、WARN 放行）~~（**v1.12 作废：放行校验整条策略已废弃，批次永不因数据质量阻塞**）
- ✅ §2.3.3 落地表DDL：Workset/Archive/Realtime 新增 `ChildRequiredFactory`；新增 Issues 表 + vw_MES_BOM_Stage_Enriched 视图
- ✅ §2.3.4 PullBOMFromODS：ColumnMappings 同步加 `ChildRequiredFactory`
- ✅ §2.4.2 链路图：BOM 双层结果路径补 Issues 旁路 + 视图层节点标注
- ✅ §3.1.3 Socket-Plug 契约视图清单：明确 vw_MES_BOM_Stage_Enriched **非 Socket-Plug**（第 3 类"派生便利视图"）
- ✅ §5.4 视图分类与职责边界：新增一节明示 A/B/C 三类视图的防腐等级与命名规范
- 【设计决策】ChildRequiredFactory 值域=APS 自定义 5 厂枚举（CN/CN6课/BJ/TJ/SH），ERP 升级不影响核心表字段
- 【设计决策】诊断/错误信息不进 Workset/StageDetail 核心表，单独进 MES_APS_BOM_Workset_Issues（结构可演进）
- 【设计决策】APS 本地**不做** vw_APS_BOM_Stage_Enriched 对称视图，避免 ERP 特征字段下沉；如需委外/受托信息由 2 号位预计算落独立配置表
- ~~【放行策略】批次 READY 前检查 Issues：Severity IN ('ERROR','CRITICAL') 阻塞；WARN 登记放行；INFO 静默登记~~（**v1.12 作废**：替代口径见文档顶部 v1.12 清稿说明）

**v1.9更新内容**（2026-04-15 订单ETL v1.2增补，基于《仅1.2增补内容v1.0》）：
- ✅ §2.2.0 订单链路总览图补充IssueDate/OriginalDueDate/ReceivedQty
- ✅ 夜间装载说明补充新3字段透传

**v1.4更新内容**（2026-04-09 订单业务字段补充，基于《订单ETL补充字段设计建议v1.1》）：
- ✅ §2.2.0 订单链路总览图补充业务字段说明（源事实字段+APS衍生字段）
- ✅ §2.2 三层职责表补充Staging层衍生字段标准化职责
- ✅ 【业务澄清】OrderType/FactoryCode为APS衍生/标准化字段，视图中为原始值，sp_ValidateAndPromoteOrders负责标准化
- ✅ 夜间装载说明补充新业务字段透传

**v1.3更新内容**（2026-04-03 订单链路审计 + 库存/ext视图审计）：
- ✅ §2.2 订单链路统一为 ERP → `ERP_Order_Staging`（验证清洗）→ `Order_Canonical`（防腐层核心表）→ `Order`（业务分区表）。原伪代码直接写入Order_Canonical的路径已修正。
- ✅ §2.2.0 新增订单链路标准口径总览图
- ✅ §2.2.1 白天增量伪代码修正（统一走Staging→Canonical路径）
- ✅ §2.2.1b 新增实时插单流程（紧急订单快速通道）
- ✅ §2.2.1c 新增订单取消与变更的反向同步
- ✅ §2.2.2 夜间批量调度补白天vs夜间对比说明
- ✅ Order_Canonical表定义对齐DDL（补Priority/CustomerCode/SourceSystem/SourceOrderId/SourceMasterID/FactoryCode/UOM字段和Upsert键索引）
- ✅ 补4个ext_跨库包装视图定义（ext_APS_Resource_View, ext_MES_APS_Routing_Operation_View, ext_MES_APS_Routing_Dependency_View, ext_APS_OperationResourceEligibility_View）
- ✅ 库存6张表定义以DDL为准修正（InventoryFact_ERP/MES、InventorySupplyCandidate、ProductFamilyInventoryScope、InventorySourceRule、MaterialSupplyContext）
- ✅ MaterialMapping表以DDL v4.0为准修正（SourceID+Warehouse统一）

**v1.2更新内容**（2026-03-25）：
- ✅ 补充五层库存架构的完整表结构说明（第5.2.4节）
- ✅ 新增 InventoryFact_ERP 和 InventoryFact_MES 表说明（库存事实层）
- ✅ 新增 InventorySupplyCandidate 表说明（候选供给池）
- ✅ 新增 ProductFamilyInventoryScope 表说明（产品族库存范围规则）
- ✅ 新增 InventorySourceRule 表说明（库存来源优先级与排除规则）
- ✅ 新增 InventoryBalance 表说明（规则筛选后的可用库存）
- ✅ 新增 MaterialSupplyContext 表说明（物料供给与责任上下文）

**v1.1更新内容**（2026-03-19）：
- ✅ 补充主数据契约视图定义（ERP_Master_View、MES_Material_View、ext_ERP_Master_View、ext_MES_Material_View）
- ✅ 补充主数据演进路径图，明确ERP/MES → ODS → APS的完整数据流向
- ✅ 修正MaterialMapping同步时间从00:35改为00:10
- ✅ 更新sp_SyncMaterialMapping存储过程，明确数据来源为ODS库的ext视图

**v1.2更新内容**（2026-04-01）：
- ✅ 存储过程更新为双源同构 sp_SyncMasterData(@SourceType)，替代原 sp_SyncERPMasterData + sp_SyncMESMaterialData
- ✅ MaterialMapping 表统一为 SourceID + Warehouse（消除 ERP/MES 字段分叉）
- ✅ MaterialType 由 APS 按 MaterialCode 前缀统一推导
- ✅ MES 升级为三表协同（Material + MaterialMapping + MaterialSupplyContext）
- ✅ MaterialSupplyContext 新增 InventoryManagementMode 字段
- ✅ 更新 MES_Material_View 契约为同构格式（移除 MaterialType，新增供给属性字段）
- ✅ 补充Socket-Plug职责分工说明（源系统DBA、5号位、3号位、2号位）

---

## 📋 文档说明

本文档基于企业实际IT系统现状（详见《APS_底层IT物理现状说明_v1.0.md》），提供完整的数据架构与防腐层设计方案。

**相关文档**：
- **《APS_各类基础数据分层承接与演变总表_v5.0》**（当前 v3.34）：数据演进全景图（架构总纲），建议先读此文档理解全局
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：Socket-Plug职责分工详细说明
- **《APS_数据库字段说明文档_v5.0》**（当前 v5.2.2）：数据库表结构与字段定义
- **《APS_数据库表结构设计_v5.0.sql》**（当前 v5.2.2）：DDL脚本，数据库表结构
- **《APS_集成接口设计_v1.12》**（当前 v1.28）：应用层接口契约与 ScopeJson 唯一权威定义
- **《APS 核心排产全流程走查（完整版）》**（当前 V3.19）：端到端流程走查

**核心设计原则**：
1. ✅ **物理隔离**：ODS库与生产库物理隔离，保护MES车间报工
2. ✅ **批次化处理**：消灭N+1查询，80万订单BOM展开可控
3. ✅ **防腐层三重防护**：视图契约、业务主键、拉链表
4. ✅ **快照封存**：256G内存计算 + 4.76T硬盘归档
5. ✅ **紧急插单支持**：白天实时BOM展开，准实时响应

**文档结构**：
- 第一部分：物理基线与系统隔离架构
- 第二部分：数据管道与时序执行流水线
- 第三部分：防腐防御机制（Anti-Corruption Layer）
- 第四部分：补充设计建议（5点优化）
- 第五部分：数据库表结构设计
- 第六部分：接口设计规范
- 第七部分：方案综合评估

---

## 第一部分：物理基线与系统隔离架构（绝对红线）

### 1.1 系统物理架构总览

针对企业现有的**256G内存高配服务器**、**4.76T大容量硬盘**，以及**ERP/MES的历史渊源**，系统必须实施严格的物理切割。

#### **三层物理架构**：

```

                    源系统层（ERP/MES 生产库）                    
  - ERP生产库：订单、BOM、主数据、库存                            
  - MES生产库：工艺路线、车间报工、实绩数据                       
  ⚠️ 只负责承载车间报工、订单录入等日常事务                       

                              
                               1小时/次增量同步
                               每天00:00批量同步
                              

              集成防腐层（MES_Integration ODS 库）                
  【物理隔离红线】：必须在MES服务器上建立独立的集成副本库（ODS）  
  - 每天00:00的重型BOM预展开（Recursive CTE）                    
  - 绝对禁止跑在MES生产事务库上！                                
  - ODS与APS本地库的高频临时I/O，优先使用新增SSD承载             
  - 具体挂载位置以最终服务器资源分配方案为准                     

                              
                               批量拉取（SqlBulkCopy）
                               实时补录（紧急插单）
                              

                计算标准层（APS 本地库 + 内存）                   
  - APS拥有自己独立的SQL Server标准库                            
  - 排程时物理断网，基于本地极其纯净的APS_BOM_RAW表              
  - 256G内存沙盘执行极速推演                                     
  - 4.76T硬盘存储历史快照（json.gz压缩）                         

```

---

### 1.2 物理隔离红线（绝对不可违反）

#### **红线1：BOM递归展开必须在ODS库执行**

**禁止行为**：
- ❌ 在MES生产库执行Recursive CTE
- ❌ 在APS本地库执行跨网络的BOM展开

**强制要求**：
- ✅ 在MES服务器上建立**独立的ODS库**（MES_Integration）
- ✅ 每天00:00的BOM预展开在ODS库执行
- ✅ ODS库与MES生产库**物理隔离**，互不影响

**原因**：
- 80万订单的BOM递归展开，可能产生**数百万次I/O**
- 如果在MES生产库执行，会严重影响**车间报工**和**实绩采集**
- ODS库可以使用**SSD加速**，不影响生产库性能

---

#### **红线2：APS排程时必须物理断网**

**禁止行为**：
- ❌ 排程时跨网络查询ERP/MES数据
- ❌ 排程时跨网络查询ODS库数据

**强制要求**：
- ✅ 排程前，所有数据必须**预加载到APS本地库**
- ✅ 排程时，只使用**APS本地库 + 256G内存**
- ✅ 排程完成后，再将结果**批量落盘**

**原因**：
- 网络延迟会严重影响排程性能
- 15分钟内完成10万级Task排程，必须**零网络依赖**

---

#### **红线3：ODS库I/O必须使用SSD**

**禁止行为**：
- ❌ ODS库数据文件放在机械硬盘上
- ❌ ODS库日志文件放在机械硬盘上

**强制要求**：
- ✅ ODS库数据文件放在**1TB SSD**上
- ✅ ODS库日志文件放在**1TB SSD**上
- ✅ ODS库tempdb放在**1TB SSD**上

**原因**：
- BOM递归展开会产生大量**随机I/O**
- 机械硬盘的随机I/O性能极差（约100 IOPS）
- SSD的随机I/O性能优秀（约10,000 IOPS）

---

### 1.3 服务器资源分配方案

基于《服务器资源分配与优化建议.md》，物理资源分配如下：

#### **VM 1：APS 核心计算与应用服务器**
- **CPU**：24核48线程
- **内存**：96GB
- **硬盘**：200GB（操作系统 + 应用程序）
- **用途**：1号位有限产能排程引擎、3号位运行编排服务、5号位规则与BOM服务、Web API 及 Hangfire 调度

#### **VM 2：SQL Server 数据库服务器**
- **CPU**：16核32线程
- **内存**：128GB
- **硬盘**：
  - **SSD（1TB）**：APS本地库数据文件、日志文件、tempdb
  - **HDD（4.76TB）**：历史快照、备份文件
- **用途**：2号位的APS本地库

#### **VM 3：MES ODS 集成服务器**
- **CPU**：8核16线程
- **内存**：32GB
- **硬盘**：
  - **SSD（共享1TB）**：ODS库数据文件、日志文件、tempdb
  - **HDD（共享4.76TB）**：归档数据
- **用途**：MES_Integration ODS库、BOM预展开

---

## 第二部分：数据管道与时序执行流水线（核心细节）

### 2.1 时序总览（24小时执行节奏）

```
时间轴：00:00 ► 02:00 ► 02:15 ► 白天（每小时）
                                               
                                               
环节一                                          
活跃根                                          
集合划定                                        
                                               
                                               
环节二：BOM预展开                                
（ODS库）                                       
                                               
                                               
环节三：本地精加工                               
（APS库）                                       
                                                
                                                
                环节四：排程推演                  
                （内存沙盘）                      
                                                 
                                                 
                            环节五：快照封存       
                            （硬盘归档）           
                                                  
                                                  
                                        环节六：白天增量同步
                                        （紧急插单支持）
```

---

### 2.2 环节一：白天订单的流式增量与"活跃根集合"划定

> ⚠️ **2026-04-03 订单链路审计修正**：统一订单入口路径为 ERP → `ERP_Order_Staging`（验证清洗）→ `Order_Canonical`（防腐层核心表）→ `Order`（业务分区表）。原伪代码直接写入Order_Canonical的路径已修正。

#### **2.2.0 订单链路标准口径总览（2026-04-03补充）**

```
ERP 订单中间表 / 生产指示中间表
  ↓ v_APS_SalesOrder（ERP侧契约视图，含SO/MTO/MTS/SS）
  │   源事实字段：TransportMode, CustomerName, MTS_InstructionNo(≠OrderNo), IssueDate, OriginalDueDate, ReceivedQty(仅MTS)
  │   【业务澄清】OrderType/FactoryCode在此层为ERP原始值，非APS最终值
  ↓ ERPOrderSyncService（白天每小时增量 + 每日凌晨全量）
ERP_Order_Staging（验证/清洗暂存，状态机：PENDING→VALIDATED→FAILED→PROCESSED）
  │   含源事实字段(+BOMNO可空) + APS衍生字段占位(+CustomerTier+DelayStatus)
  │   v5.0.21：FailureCode（原因）+ NextActionCode（动作），两个独立维度；BOMNO改可空
  │   v5.0.24：DelayStatus（ON_TIME/FIRST_DELAY/REPEATED_DELAY，独立维度）
  │   v5.0.27：MaterialCode改可空；+Model/CustomerCode/RawNonStockShipmentType/RawOrderSource（ERP原始透传）
  ↓ sp_ValidateAndPromoteOrders（v5.0.27全量重写：①#TargetStagingIds锁定批次ID；②MaterialCode三级解析链（SourceMasterID/Model/EmergencyOverride）；③OrderType标准化（未知→FAILED+ORDER_TYPE_UNKNOWN）；④Model一对多→MATERIAL_MAPPING_AMBIGUOUS；⑤CustomerSegment无匹配→UNKNOWN（不再默认OVERSEAS）；⑥BOMNO_MISSING=非阻断；⑦NonStockShipmentType/OriginalOrderSource inline标准化）
Order_Canonical（防腐层核心订单表，Upsert键：SourceSystem + SourceOrderId）
  │   含所有源事实字段(+IssueDate/OriginalDueDate/ReceivedQty) + APS衍生字段结果(+CustomerTier+DelayStatus)
  │   v5.0.27：+SourceModel/NonStockShipmentType/OriginalOrderSource
  ├→ 每天00:00：划活跃根集合 → 按订单粒度推送BOM展开请求（v5.0.21：不再去重BOMNO；BOMNO可空；含无BOMNO订单）
  └→ 每天00:05：sp_SyncOrdersToPartitionTable → 补齐MaterialId等 + 透传业务字段 → Order分区表
Order（业务分区表，按PlanVersionId分区）
  ↓ 排程前装入 ScheduleContext.Orders
排程引擎消费
```

**三层职责分工**：
| 层 | 表 | 定位 | 写入时机 |
|----|-----|------|---------|
| **同步入口** | `ERP_Order_Staging` | 缓冲层：字段校验、异常留痕、基础清洗；含源事实字段+APS衍生字段占位（2026-04-09 v1.4补充） | 白天每小时增量 + 凌晨全量 |
| **防腐层** | `Order_Canonical` | 核心表：统一承接ERP订单与MTS生产指示；含全部源事实+APS衍生结果字段（2026-04-09 v1.4补充） | Staging校验通过后Upsert |
| **业务层** | `Order` | 分区表：补齐MaterialId等+透传业务字段，供排程引擎消费（2026-04-09 v1.4补充） | 每天00:05批量装载 |

**白天增量 vs 夜间批次调度说明**：
- **白天增量**（每小时）：ERP → Staging → Canonical（增量Upsert），确保Canonical始终反映最新订单状态
- **夜间批次**（00:00-02:00）：
  - 00:00 从Canonical划活跃根集合，驱动BOM预展开
  - 00:05 与Canonical装载到Order分区表（补齐MaterialId/ProductFamilyId/FactoryId/DomainKey/PriorityScore + 透传TransportMode/CustomerName/CustomerSegment/SalesOrderCategory/DemandMaturityStatus/CustomerTier/DelayStatus/MTS_InstructionNo/IssueDate/OriginalDueDate/ReceivedQty/SourceModel/NonStockShipmentType/OriginalOrderSource）（v5.0.27补后三字段）
  - 02:00 排程引擎消费Order快照
- **白天增量和夜间批次共用同一张Canonical表**，但走不同的下游服务

---

#### **2.2.1 白天增量同步（每小时执行）**（v1.34 重写 2026-07-13）

**时间**：每小时整点（01:00, 02:00, ..., 23:00）

**数据源**：
- ERP提供的**订单中间表**（SO/MTO客户订单）
- ERP提供的**生产指示中间表**（MTS生产指示）
- 两类来源统一通过 `v_APS_SalesOrder` 契约视图暴露（`OrderType` 区分）

**同步路径**：ERP → `v_APS_SalesOrder` → `ERP_Order_Staging` → `sp_ValidateAndPromoteOrders` → `Order_Canonical`（**必须经过Staging验证**）

**⚠️ v1.34 核心口径**：每小时同步**只更新 Canonical**；**不创建 Scenario；不创建 ScheduleRun；不创建 PlanVersion；不写 RequestDetail；不触发 Realtime BOM；不触发 CTP 或重排**。所有白天实时评估与局部重排入口一律为人工发起，详见 §2.2.1b。

**同步逻辑**：
```csharp
// APS 守护进程（Hangfire 后台任务）
// v1.34 修正：每小时同步只到 Canonical，不再自动触发实时 BOM 或排程
public async Task SyncOrdersFromERP()
{
    // 1. 从 ERP 契约视图拉取最近 1 小时的增量订单（含 SO/MTO/MTS/SS）
    var orders = await erpAdapter.GetIncrementalOrders(lastSyncTime);

    // 2. 写入 ERP_Order_Staging（SyncStatus = 'PENDING'）
    await stagingRepository.BulkInsertAsync(orders);

    // 3. 执行验证与提升（Staging → Canonical）
    //    校验通过 → Upsert 到 Order_Canonical，Staging 状态改为 PROCESSED
    //    校验失败 → Staging 状态改为 FAILED，ErrorMessage 留痕
    await orderPromotionService.ValidateAndPromoteAsync();

    // v1.34：到此结束。不检测 worksetCache，不写 RequestDetail，
    //        不调用 RequestRealtimeBOMExpansion，不创建任何运行对象。
    //        白天实时评估/局部重排一律由 4号位页面展示，PMC 人工发起，
    //        由 3号位创建 Scenario（适用时）+ ScheduleRun。详见 §2.2.1b。
}
```

**ERP_Order_Staging 状态机**（v5.0.21 补 FailureCode/NextActionCode）：
```
PENDING → VALIDATED → PROCESSED（成功提升到Canonical）
PENDING → FAILED（技术校验失败；FailureCode=原因；NextActionCode=后续动作）
FAILED → PENDING（人工修正或自动重试）
```
> **⚠️ v5.0.21 设计红线**：FailureCode 和 NextActionCode 为**两个独立维度**，可同时有值，也可只有其一，**禁止混用**。

---

#### **2.2.1b 人工发起的白天实时评估与局部重排入口**（v1.34 重写 2026-07-13）

**核心口径**：白天实时评估与局部重排一律由 PMC/销售/计划员人工发起。每小时增量同步只更新 `Order_Canonical`，不自动触发任何实时链路。

**白天 RunType + Purpose 合法组合（四类常用入口 + 单域较大范围人工重排）**（详见 §2.8）：
| 业务场景 | RunType | ScopeJson.Purpose | 激活规则 |
|---------|---------|-------------------|---------|
| CTP 承诺交期评估 | `INSERT_ORDER_WHATIF` | CTP | **不得激活** |
| 插单影响分析 | `INSERT_ORDER_WHATIF` | INSERT_IMPACT_ANALYSIS | **不得激活** |
| 插单局部重排 | `LOCAL_RESCHEDULE` | INSERT_RESCHEDULE | 审批后可激活 |
| 人工局部重排 | `LOCAL_RESCHEDULE` | MANUAL_ADJUSTMENT | 审批后可激活 |
| 单 Domain 较大范围人工重排 | `MANUAL_RESCHEDULE` | MANUAL_ADJUSTMENT | 审批后可激活 |

**主链**（详见 §2.7 分节）：

1. 每小时同步任务将新增或变化订单写入 `Order_Canonical`（§2.2.1）
2. **4号位页面**展示新增/变化订单、影响分析、推荐清单
3. **PMC / 销售 / 计划员** 人工选择业务场景（CTP / 插单影响分析 / 插单局部重排 / 人工局部重排 / 单Domain较大范围人工重排）
4. **3号位** 归一化 `ScopeJson`（唯一权威契约见《集成接口设计 v1.28》），创建 `Scenario`（适用时）和 `ScheduleRun`（`BasePlanVersionId` + `StrategyProfileVersionId` + `ScopeJson` + `ExpectedDomainKeysJson`（=["BasePlanVersion.DomainKey"]，严格单 Domain，独立运行级字段，不属 ScopeJson）+ `DataCutoffTime`）
5. 3号位创建Candidate类PlanVersion版本壳，初始Status=BUILDING；2号位完成数据构造和结果持久化后，再将其转为CANDIDATE
6. **2号位** 调用 `PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)` 为 Candidate 建立**独立 Order 快照**
7. **2号位** 检查 Scope 内 BOM 切片能否复用；若不可复用则创建 `RequestDetail` 并执行 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)`
8. 展开链路：`Workset_Realtime` → `sp_EnrichBOMWorksetRealtime` → `StageDetail_Realtime` → `sp_GenerateBOMCrossFactoryEdgeRealtime` → `CrossFactoryEdge_Realtime` → `MES_API_BOM_Request_Realtime.Status='READY'`
9. **READY 权威**：只以 `MES_API_BOM_Request_Realtime.Status='READY'` 为权威；三张 Realtime 结果表允许 0 行，`CrossFactoryEdge_Realtime` 为 0 行合法

**Issues 切片号（v1.34 统一）**：
- `RequestDetailId` 非空：`BatchNo = RT:RD:{RequestDetailId}`（正式路径）
- `RequestDetailId` 为空：`BatchNo = RT:{ResolvedBOMNO}`，**仅 deprecated 兼容**，用 `LEFT(..., 50)` 显式截断

**红线**：
- 不允许"每小时同步发现新订单自动触发实时展开"的旧口径
- 不允许"5号位手动触发增量排程"
- 不允许 Issues 使用 `BatchNo='RT:'+@BOMNO` 单一口径
- Candidate 不回写共享 `InventoryBalance`；不修改 ACTIVE 版本的 PeggingSupplyAllocation；不同 Candidate 之间相互隔离
- 非 FULL_SCHEDULE 运行不得自动 ACTIVE

---

#### **2.2.1c 订单取消与变更的反向同步**（2026-04-03补充）

**ERP订单取消**：
1. ERP侧将订单状态改为 `CANCELLED`
2. 下次增量同步时，`v_APS_SalesOrder` 的 `WHERE Status NOT IN ('CANCELLED', 'CLOSED')` 过滤掉该订单
3. APS侧在 `sp_ValidateAndPromoteOrders` 中检测：Canonical中存在但本批次未出现的订单 → 标记 `Status = 'Cancelled'`
4. 下次夜间装载时，`sp_SyncOrdersToPartitionTable` 的 `WHERE Status = 'OPEN'` 自动排除（v3.12 窄口径：只允许 OPEN 状态订单进入 Order 分区表）

**ERP订单变更**（数量/交期/优先级）：
1. ERP侧更新订单字段
2. 增量同步时拉取到更新后的数据，写入Staging
3. `sp_ValidateAndPromoteOrders` 以 `SourceSystem + SourceOrderId` 为Upsert键更新Canonical
4. 下次夜间装载时，Order分区表重新生成（非增量更新，而是按PlanVersionId全量INSERT）

---

#### **2.2.2 活跃根集合划定（每天00:00执行）**

**时间**：每天00:00

**目标**：划定驱动当晚排程的"活跃根集合"

**【根集合边界红线】**：
- ✅ 必须同时包含**客户订单（SALES_ORDER，原SO/MTO）**和**生产指示（PRODUCTION_INSTRUCTION，原MTS/SS/SS_U）**（v5.0.24重分类）
- ✅ 过滤条件严格锁死为：
  - **状态准入（v3.12 窄口径）**：`WHERE Status = 'OPEN'`；只有 OPEN 状态的订单/生产指示进入 BOM Request；CLOSED / CANCELLED 不进入 BOM Request，不生成 Task/Pegging
  - 交期/指示日期落在**"今天起90天计划窗口内"**
- ⚠️ 若漏掉MTS指示，将导致下游BOM与工艺脱节

**SQL示例**：
```sql
-- 划定活跃根集合（v5.0.24更新：使用重分类后的枚举值）
WITH ActiveRoots AS (
    -- 客户订单（原SO/MTO → v5.0.24重分类为SALES_ORDER）
    SELECT 
        OrderNo,
        BOMNO,
        MaterialCode,
        DueDate,
        'SALES_ORDER' AS OrderType
    FROM Order_Canonical
    WHERE Status = 'OPEN'   -- v3.12: 窄口径，只允许 OPEN 状态订单进入活跃根集合
      AND DueDate BETWEEN GETDATE() AND DATEADD(DAY, 90, GETDATE())
    
    UNION ALL
    
    -- 生产指示（原MTS/SS/SS_U → v5.0.24重分类为PRODUCTION_INSTRUCTION）
    SELECT 
        OrderNo,
        BOMNO,
        MaterialCode,
        PlannedDate AS DueDate,
        'PRODUCTION_INSTRUCTION' AS OrderType
    FROM Order_Canonical
    WHERE Status = 'OPEN'   -- v3.12: 窄口径，只允许 OPEN 状态订单进入活跃根集合
      AND PlannedDate BETWEEN GETDATE() AND DATEADD(DAY, 90, GETDATE())
      AND OrderType = 'PRODUCTION_INSTRUCTION'
)
-- v5.0.21 重构：不再按 BOMNO 去重；活跃根集合 = 活跃订单集合（含无BOMNO订单）
-- BOM 入口解析（有 RequestedBOMNO 直接展开 / 无 RequestedBOMNO 时，5号位按 OrderType + MaterialCode + FactoryCode + BOM边/ProcessCode 规则解析入口）由5号位Workset处理阶段负责
SELECT COUNT(*) AS ActiveOrderCount FROM ActiveRoots;
-- 预期：约 N 条活跃订单（BOMNO 可空，不去重）
```

---

### 2.3 环节二：深夜00:00的单次批量预展开（消灭N+1 I/O毒药）

这是彻底规避**4000万垃圾数据**、且不拖垮网络的精妙设计。

#### **2.3.1 APS下达通缉令（推送订单BOM请求到ODS库）**（2026-05-08 v1.17 升级：订单级粒度）

**时间**：每天00:00

**动作**（v5.0.21 重构）：
- APS将活跃根集合的**每条订单**（含无BOMNO订单），按订单粒度利用 **SqlBulkCopy** 推送到ODS库 `MES_API_BOM_Request_Detail`
- 不再预先去重BOMNO；BOM入口解析（有 RequestedBOMNO 直接展开 / 无 RequestedBOMNO 时，5号位按 OrderType + MaterialCode + FactoryCode + BOM边/ProcessCode 规则解析入口）由**5号位Workset处理阶段负责**

**【批次幂等红线】**：
- ✅ 推送时必须生成并携带**全局批次号**（如`BatchNo: REQ_20260310_01`）
- ✅ 唯一约束 `(BatchNo, OrderCanonicalId)`（v5.0.31），同批次内每个 Order_Canonical 最多一行明细

**C#代码示例**（v5.0.21 新结构）：
```csharp
public async Task PushBOMRequestToODS()
{
    // 1. 生成批次号
    var batchNo = $"REQ_{DateTime.Now:yyyyMMdd}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
    
    // 2. 获取活跃订单集合（v5.0.21：不再去重BOMNO；含无BOMNO订单）
    var activeOrders = await GetActiveOrders();
    
    // 3. 构建请求头记录
    var request = new BOMRequest
    {
        BatchNo = batchNo,
        Status = "PENDING",
        RootCount = activeOrders.Count,
        CreatedAt = DateTime.Now,
        RetryCount = 0
    };
    
    // 4. 推送到ODS库
    using (var connection = new SqlConnection(odsConnectionString))
    {
        await connection.OpenAsync();
        
        // 4.1 插入请求头
        await connection.ExecuteAsync(@"
            INSERT INTO MES_API_BOM_Request (BatchNo, Status, RootCount, CreatedAt, RetryCount)
            VALUES (@BatchNo, @Status, @RootCount, @CreatedAt, @RetryCount)",
            request);
        
        // 4.2 按订单粒度批量插入明细（v5.0.31 新结构：OrderCanonicalId为主锚点）
        var detailTable = new DataTable();
        detailTable.Columns.Add("BatchNo", typeof(string));
        detailTable.Columns.Add("OrderCanonicalId", typeof(long));   // v5.0.31 主锚点
        detailTable.Columns.Add("OrderNo", typeof(string));          // v5.0.31 冗余
        detailTable.Columns.Add("SourceSystem", typeof(string));     // v5.0.31 'ERP'/'MES'
        detailTable.Columns.Add("SourceOrderId", typeof(string));    // v5.0.31 来源系统ID
        detailTable.Columns.Add("MaterialCode", typeof(string));     // v5.0.21 5号位 BOM 入口解析主键
        detailTable.Columns.Add("FactoryCode", typeof(string));
        detailTable.Columns.Add("OrderType", typeof(string));
        detailTable.Columns.Add("RequestedBOMNO", typeof(string));   // 请求输入，nullable
        // 【v5.0.32】ResolvedBOMNO 不推送到本表；归 OrderBomRequestLink（2号位在 Workset 同步完成后写入）
        
        foreach (var o in activeOrders)
        {
            detailTable.Rows.Add(
                batchNo,
                o.OrderCanonicalId,
                o.OrderNo,
                o.SourceSystem,
                o.SourceOrderId,
                o.MaterialCode,
                o.FactoryCode,
                o.OrderType,
                string.IsNullOrEmpty(o.RequestedBOMNO) ? DBNull.Value : (object)o.RequestedBOMNO);
        }
        
        using (var bulkCopy = new SqlBulkCopy(connection))
        {
            bulkCopy.DestinationTableName = "MES_API_BOM_Request_Detail";
            await bulkCopy.WriteToServerAsync(detailTable);
        }
    }
    
    // 5. 记录日志
    logger.LogInformation($"BOM展开请求已推送到ODS库，BatchNo: {batchNo}, OrderCount: {activeOrders.Count}");
}
```

---

#### **2.3.2 ODS物化边表展开（#EntryResolved + Frontier WHILE迭代）**（2026-05-15 更新）

**时间**：每天00:05（APS推送完成后5分钟）

**执行位置**：MES_Integration ODS库

**触发方式**：ODS库的定时作业（SQL Server Agent Job）

**核心逻辑（v5.0.26起）**：ODS物化边表展开（直读 `MES_BOM_Edge_Active`，WHILE迭代，#EntryResolved入口裁决）
- ⚠️ **旧方案已 deprecated**：`sp_ExpandBOMBatch`（递归CTE版）仅保留兼容运行，稳定验证后下线
- ✅ **新方案**：`sp_ExpandBOMBatch_vNext`（WHILE迭代，下方 v5.0.26 升级段），禁止对 `MES_BOM_View` 做递归CTE

**【混合寻址规则】**（实现方式已迁移至 WHILE迭代；原理不变）：
- ✅ **第1层**：基于APS传来的BOMNO集合（`#EntryResolved`入口裁决），按 BOMNO+EntryParent 定位 `MES_BOM_Edge_Active` 首层边
- ✅ **第2~N层**：按子件 `MaterialCode` 顺着 `MES_BOM_Edge_Active` 聚集索引向下钻取（`IsDefaultVersion=1`）

**【⚠️ 致命架构红线：数量必须是单位用量，绝对不能累乘】**：

**物理真相**：
- APS 的 **2号位** 在 02:00 于 **Pegging Ledger 中逐层展开和扣减**（血缘连线是**一层一层往下**的）
- APS引擎需要的BOM树，是纯粹的**"单位用量（Unit Quantity）"**，即生产1个父件需要几个子件

**错误示例（累乘）**：
```
1台车(Qty=1) -> 4个轮子(Qty=4) -> 16个螺丝(Qty=4*4=16)  ❌ 错误！
```

**正确示例（单位用量）**：
```
1台车(Qty=1) -> 4个轮子(Qty=4) -> 4个螺丝(Qty=4)  ✅ 正确！
（每个轮子需要4个螺丝，不是16个）
```

**灾难后果**：
- 如果ODS库在展开时把数量累乘了，比如"1台车 → 4个轮子 → 16个螺丝"
- APS拿到了Qty=16，当引擎算1台车时，它会拿1台车去乘底层的16个螺丝，算出需要16个
- 如果此时是一单10台车，引擎一乘，就会算出**160个螺丝**！需求量直接翻倍爆炸！

**正确做法**：
- CTE中的Quantity字段**只存储当前层的单位用量**（`b.Quantity`）
- **绝对不能累乘**（`r.Quantity * b.Quantity`）
- APS引擎会在Pegging时自己计算累计需求量

**⚠️ deprecated 历史方案（递归CTE版，v5.0.26起由 sp_ExpandBOMBatch_vNext 替代；仅供参考，禁止在新实现中使用）**：
```sql
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
    
    -- 2. 混合寻址Recursive CTE（v1.8：透传3个ERP BOM原始辅助字段）
    WITH BOM_Recursive AS (
        -- 第1层：基于BOMNO展开（定制配置）
        SELECT 
            b.BOMNO,
            b.ParentMaterialCode,
            b.ChildMaterialCode,
            b.Quantity,
            1 AS Level,
            CAST(b.ChildMaterialCode AS NVARCHAR(MAX)) AS Path,
            b.ParentProcRefCode,                         -- v1.8 父件工序参考码
            b.ChildProcRefCode,                          -- v1.8 子件工序参考码
            b.ChildSourceHintCode                        -- v1.8 子件来源提示码
        FROM MES_BOM_View b
        INNER JOIN MES_API_BOM_Request_Detail d 
            ON b.BOMNO = d.RequestedBOMNO   -- v5.0.31: 原 d.BOMNO 改名为 d.RequestedBOMNO
        WHERE d.BatchNo = @BatchNo
          AND b.IsActive = 1
        
        UNION ALL
        
        -- 第2~N层：基于MaterialCode展开（标准版本）
        SELECT 
            r.BOMNO,  -- 保留顶层BOMNO
            b.ParentMaterialCode,
            b.ChildMaterialCode,
            b.Quantity AS Quantity,  -- ⚠️ 单位用量（生产1个父件需要几个子件），不累乘！
            r.Level + 1 AS Level,
            r.Path + ' -> ' + b.ChildMaterialCode AS Path,
            b.ParentProcRefCode,                         -- v1.8 每层取当前边的值
            b.ChildProcRefCode,                          -- v1.8
            b.ChildSourceHintCode                        -- v1.8
        FROM BOM_Recursive r
        INNER JOIN MES_BOM_View b 
            ON r.ChildMaterialCode = b.ParentMaterialCode
        WHERE b.IsActive = 1
          AND b.IsDefaultVersion = 1  -- 只取标准版本
          AND r.Level < 10  -- 防止循环BOM
          AND r.Path NOT LIKE '%' + b.ChildMaterialCode + '%'  -- 防止循环
    )
    -- 3. 结果物理落地
    INSERT INTO MES_APS_BOM_Workset (
        BatchNo,
        BOMNO,
        ParentMaterialCode,
        ChildMaterialCode,
        Quantity,
        Level,
        Path,
        ParentProcRefCode,                               -- v1.8
        ChildProcRefCode,                                -- v1.8
        ChildSourceHintCode,                             -- v1.8
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
        ParentProcRefCode,                               -- v1.8
        ChildProcRefCode,                                -- v1.8
        ChildSourceHintCode,                             -- v1.8
        GETDATE()
    FROM BOM_Recursive
    OPTION (MAXRECURSION 10);  -- 限制最大递归深度
    
    -- 4. 统计展开结果
    SET @ExpandedRowCount = @@ROWCOUNT;
    
    -- 5.【v1.12 清稿】5号位后置回填：Stage/Factory/CrossOrgFlag + Issues 降级登记
    -- ⚠️ 此处由5号位追加实现（独立SP或外部流程），分 4 步：
    --
    -- ── 5a. 回填供给阈值 + 工厂 + 跨组织标签（v1.13 更新：StageCode 采用目标工厂视角）──
    --    基于 ParentProcRefCode + ChildProcRefCode + ChildSourceHintCode + 对照表 推导 StageCode；
    --    查 ProduceToFactoryMap（v5.0.11）推导 ChildRequiredFactory / ShouldDrilldown / CrossOrgHandoffFlag：
    --      - R17：ShouldDrilldown=1, CrossOrgHandoffFlag=0（本厂正常下钻、本厂自排）
    --      - R20（Produce ∈ {6,7,11}）：ShouldDrilldown=1, CrossOrgHandoffFlag=1（本厂仍下钻拿下阶明细，打标签标识"该链归他用方排产"）
    --      - R07（外购 Produce ∈ {0,2,3,4,10}）：ShouldDrilldown=0（唯一的 BOM 下钻终止场景）
    --
    --    【v1.13 StageCode 视角规则】StageDetail.StageCode 必须取自 StageDict（§1.9 字段文档），且采用**目标工厂视角**：
    --      - effective_factory = R20 时用 target_factory_of_produce(pr)；否则用 inherit_factory
    --      - StageCode = rebase_to_factory(<原始推导 StageCode>, effective_factory)
    --      - 示例：父件 TJ、子件 Produce=6 (R20→BJ) → StageCode 最终写 BJ_MACH（不是 TJ_MACH）
    --      - 好处：1 号位读 StageDetail 按 (MaterialCode, StageCode) 直接去目标工厂 RoutingOperation 找小工序，无需再做跨厂翻译
    --
    --    UPDATE MES_APS_BOM_Workset
    --    SET ChildRequiredStageCode = <按 effective_factory 视角 rebase 后的 StageCode>,
    --        ChildRequiredFactory   = <查 ProduceToFactoryMap>,
    --        CrossOrgHandoffFlag    = <查 ProduceToFactoryMap>    -- v1.13：仅作为旁路标签供审计/报表；1 号位主排程分支靠 StageCode 工厂前缀决策
    --    WHERE BatchNo = @BatchNo AND ChildRequiredStageCode IS NULL;
    --
    -- ── 5b. 写入 EDGE（子件供给路径）──
    --    数据来源：当前批次 MES_APS_BOM_Workset 的每一条边级记录
    --    关联键：BatchNo + BOMNO + ParentMaterialCode + ChildMaterialCode（与主Workset一一对应）
    --    INSERT INTO MES_APS_BOM_Workset_StageDetail
    --        (BatchNo, BOMNO, StageScopeType,
    --         ParentMaterialCode, ChildMaterialCode,
    --         StageSeq, StageCode, IsSupplyThreshold)
    --    SELECT
    --        w.BatchNo,
    --        w.BOMNO,
    --        'EDGE',                            -- 子件供给路径
    --        w.ParentMaterialCode,              -- 父件编码（NOT NULL）
    --        w.ChildMaterialCode,               -- 子件编码
    --        s.StageSeq,                        -- 10/20/30... 由5号位推导
    --        s.StageCode,                       -- 如 TJ_MACH / TJ_OUTS
    --        s.IsSupplyThreshold                -- 1=供给阈值点
    --    FROM MES_APS_BOM_Workset w
    --    CROSS APPLY <5号位推导函数>(w.ParentProcRefCode, w.ChildProcRefCode, w.ChildSourceHintCode) s
    --    WHERE w.BatchNo = @BatchNo;
    --
    -- ── 5c. 写入 ROOT（根产品完工路径）──
    --    数据来源：当前批次 Level=1 的根产品集合（按 BOMNO 去重取唯一根产品）
    --    ⚠️ ROOT 与主 Workset 不是一一对应边关系，而是"同批次、同根产品"的派生路径关系
    --    推导规则：取 Level=1 各边的 ParentProcRefCode → 映射标准化阶段路径
    --             若同一根产品映射后出现多条不一致路径 → 取最长路径 + 记WARNING日志（不静默并集）
    --    INSERT INTO MES_APS_BOM_Workset_StageDetail
    --        (BatchNo, BOMNO, StageScopeType,
    --         ParentMaterialCode, ChildMaterialCode,
    --         StageSeq, StageCode, IsSupplyThreshold)
    --    SELECT
    --        @BatchNo,
    --        root.BOMNO,
    --        'ROOT',                            -- 根产品完工路径
    --        NULL,                              -- ROOT记录 ParentMaterialCode 恒为 NULL
    --        root.ParentMaterialCode,           -- 根产品自身编码（填入ChildMaterialCode列）
    --        rs.StageSeq,                       -- 10/20/30... 由5号位映射标准化
    --        rs.StageCode,                      -- 如 TJ_MACH / TJ_OUTS
    --        0                                  -- ROOT记录 IsSupplyThreshold 恒为0
    --    FROM (
    --        SELECT DISTINCT BOMNO, ParentMaterialCode
    --        FROM MES_APS_BOM_Workset
    --        WHERE BatchNo = @BatchNo AND Level = 1
    --    ) root
    --    CROSS APPLY <5号位ROOT推导函数>(root.ParentMaterialCode) rs;
    --
    -- ── 5d. Issues 降级登记（v1.12 清稿新增）──
    --    R27 异常分治：9 类 IssueType（LEAF / FACTORY_MISMATCH / FACTORY_MISMATCH_MULTI /
    --    NO_STAGE / UNKNOWN_PROCCODE / QUANTITY_INVALID / MISSING_PRODUCE / CYCLIC_BOM / EXPAND_FAILED）
    --    全部"降级 + 登记"，不阻塞批次；每条 Issue 必填 DegradeAction 标签：
    --      STAGE_NULL / FACTORY_FALLBACK / QTY_DEFAULT_1 / CYCLE_SKIP / BOMNO_SKIP / PRODUCE_DEFAULT_1
    --    INSERT INTO MES_APS_BOM_Workset_Issues
    --        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
    --         IssueType, Severity, DegradeAction, Detail, RawRefJson, CreatedAt)
    --    VALUES (...);
    --    详细矩阵见《BOM_Workset_生成与错误处理技术方案 v1.1》§4.1
    --
    -- 回填 + 登记完成即可进入 READY（**不做任何 Severity 放行校验**，批次永不因数据质量阻塞）
    -- ✅ v1.16：调用 sp_EnrichBOMWorkset 执行回填（R17工厂映射 + 阶段链推导 + StageDetail + Issues）
    EXEC sp_EnrichBOMWorkset @BatchNo;
    
    -- 6. 更新批次状态为 READY
    -- ⚠️ v1.12 口径：状态机 PENDING → PROCESSING → READY 直通；
    --    `FAILED` 仅保留给 SP 进程崩溃（TRY/CATCH 捕获的系统级异常），不用于数据质量判定
    UPDATE MES_API_BOM_Request
    SET Status = 'READY',
        CompletedAt = GETDATE(),
        ExpandedRowCount = @ExpandedRowCount,
        ProcessingDuration = DATEDIFF(SECOND, @StartTime, GETDATE())
    WHERE BatchNo = @BatchNo;
    
    -- 7. 记录日志
    INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
    VALUES (@BatchNo, 
            'BOM展开完成，展开行数: ' + CAST(@ExpandedRowCount AS NVARCHAR(20)), 
            GETDATE());
END;
GO
```

---

**⚠️ v5.0.26 升级：`sp_ExpandBOMBatch_vNext`（WHILE迭代展开，直读 MES_BOM_Edge_Active）**（2026-05-14 新增）

V1 正式展开 SP 迁移至 WHILE 迭代模式，**禁止对 `MES_BOM_View` 做递归 CTE**，改为每层 JOIN `MES_BOM_Edge_Active` 专项聚集索引。旧 `sp_ExpandBOMBatch`（递归CTE版）标记为 deprecated，V1 过渡期保留兼容运行，稳定后下线。

```sql
-- ⚡ sp_ExpandBOMBatch_vNext — WHILE迭代展开伪代码骨架（v5.0.26）
-- ⚠️ 禁止在此SP中引用 MES_BOM_View；直接读 MES_BOM_Edge_Active
CREATE PROCEDURE sp_ExpandBOMBatch_vNext
    @BatchNo NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    -- 0. 前置校验：MES_BOM_Edge_Active 最新刷新状态必须 COMPLETED
    --    防止 Workset 消费半刷新数据
    IF NOT EXISTS (
        SELECT 1 FROM MES_BOM_Edge_RefreshLog
        WHERE Id = (SELECT MAX(Id) FROM MES_BOM_Edge_RefreshLog)
          AND Status = 'COMPLETED'
    )
    BEGIN
        RAISERROR('MES_BOM_Edge_Active 未完成刷新，禁止展开。请检查 MES_BOM_Edge_RefreshLog。', 16, 1);
        RETURN;
    END

    -- 1. 初始化：第1层（按 BOMNO 直接寻址，利用索引 IX_BOMEdgeActive_BOMNO）
    CREATE TABLE #BOM_Expand (
        BOMNO            NVARCHAR(50)  NOT NULL,
        ParentMaterialCode NVARCHAR(50) NOT NULL,
        ChildMaterialCode  NVARCHAR(50) NOT NULL,
        Quantity           DECIMAL(18,6) NOT NULL,
        Level              INT           NOT NULL,
        Path               NVARCHAR(MAX) NOT NULL,
        ParentProcRefCode  NVARCHAR(50)  NULL,
        ChildProcRefCode   NVARCHAR(50)  NULL,
        ChildSourceHintCode NVARCHAR(50) NULL,
        RequestDetailId    BIGINT        NULL
    );

    INSERT INTO #BOM_Expand
    SELECT
        e.BOMNO, e.ParentMaterialCode, e.ChildMaterialCode,
        e.Quantity,           -- ⚠️ 单位用量，绝不累乘
        1 AS Level,
        CAST(e.ChildMaterialCode AS NVARCHAR(MAX)) AS Path,
        e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode,
        d.Id AS RequestDetailId   -- 透传来源锚点
    FROM MES_BOM_Edge_Active e   -- ⚡ 直读物化边表，不走 MES_BOM_View
    INNER JOIN MES_API_BOM_Request_Detail d
        ON e.BOMNO = d.RequestedBOMNO   -- v5.0.31: 原 d.BOMNO 改名为 d.RequestedBOMNO
    WHERE d.BatchNo = @BatchNo
      AND e.IsActive = 1;

    DECLARE @CurrentLevel INT = 1;
    DECLARE @RowsInserted INT = @@ROWCOUNT;

    -- 2. WHILE 迭代：第 2~N 层（按 MaterialCode 寻址，利用聚集索引 (ParentMaterialCode, IsActive)）
    WHILE @CurrentLevel < 10 AND @RowsInserted > 0
    BEGIN
        SET @CurrentLevel = @CurrentLevel + 1;

        INSERT INTO #BOM_Expand
        SELECT
            prev.BOMNO,          -- 保留顶层 BOMNO（混合寻址核心）
            e.ParentMaterialCode,
            e.ChildMaterialCode,
            e.Quantity,          -- ⚠️ 单位用量，绝不累乘
            @CurrentLevel,
            prev.Path + ' -> ' + e.ChildMaterialCode,
            e.ParentProcRefCode, e.ChildProcRefCode, e.ChildSourceHintCode,
            prev.RequestDetailId  -- 逐层透传来源锚点
        FROM #BOM_Expand prev
        INNER JOIN MES_BOM_Edge_Active e  -- ⚡ 每层 JOIN 聚集索引，不走 MES_BOM_View
            ON e.ParentMaterialCode = prev.ChildMaterialCode
        WHERE prev.Level = @CurrentLevel - 1
          AND e.IsActive = 1
          AND e.IsDefaultVersion = 1       -- 唯一默认版本
          AND prev.Path NOT LIKE '%' + e.ChildMaterialCode + '%'; -- 防环

        SET @RowsInserted = @@ROWCOUNT;
    END

    -- 3. 落地至 MES_APS_BOM_Workset
    INSERT INTO MES_APS_BOM_Workset
        (BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
         Quantity, Level, ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
         RequestDetailId, CreatedAt)
    SELECT
        @BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
        Quantity, Level, ParentProcRefCode, ChildProcRefCode, ChildSourceHintCode,
        RequestDetailId, GETDATE()
    FROM #BOM_Expand;

    -- 4. 后置回填：Stage / Factory / StageDetail(含WorksetId) / Issues
    --    详细伪代码见 sp_EnrichBOMWorkset（Batch 6 DDL SP骨架）
    EXEC sp_EnrichBOMWorkset @BatchNo;

    -- 5. 状态更新 READY
    --    ⚠️ v1.12 口径：状态机 PENDING→PROCESSING→READY 直通；
    --    FAILED 仅保留给 SP 进程崩溃（TRY/CATCH 系统级异常）
    UPDATE MES_API_BOM_Request
    SET Status = 'READY', CompletedAt = GETDATE()
    WHERE BatchNo = @BatchNo;

    DROP TABLE #BOM_Expand;
END;
GO
```

**设计决策（v5.0.26 写死）**：
- WHILE 循环每层 JOIN `MES_BOM_Edge_Active` 聚集索引 `(ParentMaterialCode, IsActive)`，比递归CTE SQL Stack 更可控，不受 `MAXRECURSION` 限制
- `RefreshLog` 前置校验确保边表处于稳定完成状态（FAILED 时即时报错，不暗中消费半刷新数据）
- `RequestDetailId` 在第1层从 `Request_Detail.Id` 透传，逐层携带，落地到 `Workset` 而不进 `StageDetail`
- `sp_ExpandBOMBatch`（递归CTE旧版）标记 deprecated；完整迁移并验证后下线

**入口裁决补充（v5.0.28 R28/R29/R30/R31，对齐 BOM_Workset方案v1.7 §1.4）**：

BOMNO IS NULL 时 Stage B（`#EntryCandidates`）按 OrderType + MaterialCode 前缀分流：

| 规则 | 运算条件 | 入口查找方式 | 后续 Issues |
|------|---------|----------|----------|
| **R28** | `SALES_ORDER` + `ASSY%` | `ProcessCodeDict` 出口库过滤 `ParentProcRefCode`；<br>若订单工厂无出口库（CN6课）→ 取母体工厂CN出口库（R10 隶属关系） | `BOM_ENTRY_AMBIGUOUS`（多候选时） |
| **R29** | `SALES_ORDER` + `WIP%`/`RAW%` | 直接按 `ParentMaterialCode=MaterialCode` 查边 | 无额外 |
| **R30** | `SALES_ORDER` + `RAW%` + 无BOM | 外购件冖底 | 不登记 `BOM_ENTRY_NOT_FOUND`（静默跳过） |
| **R31** | `PRODUCTION_INSTRUCTION` + BOMNO IS NULL | 同R29直查 | **必写** `BOMNO_MISSING_PRODUCTION`（WARN=找到 / ERROR=未找到） |

✅ 实现位置：`sp_ExpandBOMBatch_vNext` Stage B/C 代码内主造；`sp_ExpandBOMRealtime_vNext` BOMNO IS NULL 分支同步  
✅ 权威细节：见《BOM展开经验库》§4.4 R28~R31 + 《BOM_Workset_方案v1.7》§1.4

---

#### **2.3.3 结果物理落地与批次隔离**

**落地表**：`MES_APS_BOM_Workset` + `MES_APS_BOM_Workset_StageDetail`（v1.8双层结果，v1.9升级ROOT 2026-04-15 更新）

**【批次隔离红线】**：
- ✅ `MES_APS_BOM_Workset` 和 `StageDetail` 必须按`BatchNo`隔离
- ✅ 同一`BatchNo`结果集只允许生成一次
- ✅ 历史批次结果按保留策略清理或归档（sp_CleanupBOMWorkset 同步归档两表，含StageScopeType）
- ❌ 不允许新批次覆盖旧批次

**表结构**（v1.12 2026-04-24 更新，对齐 DDL v5.0.11）：
```sql
-- 主层：BOM边 + 供给阈值（不承载根产品自身路径）
CREATE TABLE MES_APS_BOM_Workset (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,
    Level INT NOT NULL,
    Path NVARCHAR(MAX) NULL,
    ParentProcRefCode NVARCHAR(50) NULL,           -- v1.8 ERP BOM原始辅助字段
    ChildProcRefCode NVARCHAR(50) NULL,            -- v1.8 ERP BOM原始辅助字段
    ChildSourceHintCode NVARCHAR(50) NULL,         -- v1.8 ERP BOM原始辅助字段（ERP Produce 值）
    ChildRequiredStageCode NVARCHAR(50) NULL,      -- v1.8 5号位后置回填 StageCode（引用 StageDict v5.0.11），NULL=保守策略
    ChildRequiredFactory NVARCHAR(20) NULL,        -- v1.10 R17 推导（查 ProduceToFactoryMap v5.0.11），APS 5 厂枚举
    CrossOrgHandoffFlag BIT NOT NULL DEFAULT 0,    -- v1.12 R20 跨组织交接标签：1=本厂下钻拿明细但不占产能，由他用方排产
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- 诊断登记表（v1.10 新增，v1.12 口径：全部降级+登记，不阻塞批次）
-- 完整 DDL 见《APS_数据库表结构设计_v5.0.sql v5.0.11》§1.5；核心字段：
-- BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode,
-- IssueType (LEAF/FACTORY_MISMATCH/.../EXPAND_FAILED，9 类),
-- Severity (INFO/WARN/ERROR/CRITICAL),
-- DegradeAction (STAGE_NULL/FACTORY_FALLBACK/QTY_DEFAULT_1/CYCLE_SKIP/BOMNO_SKIP/PRODUCE_DEFAULT_1),
-- Detail, RawRefJson, ReviewStatus, CreatedAt
-- 消费：0 号位/业务复核人员月度巡检 + 反馈 ERP 维护方修复源头数据（不做每日值班）

-- 明细层：统一阶段路径结果表（5号位派生，v1.8新增EDGE，v1.9新增ROOT）
CREATE TABLE MES_APS_BOM_Workset_StageDetail (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    StageScopeType NVARCHAR(10) NOT NULL DEFAULT 'EDGE', -- v1.9 EDGE=子件供给路径 / ROOT=根产品完工路径
    ParentMaterialCode NVARCHAR(50) NULL,                -- v1.9 EDGE=父件编码；ROOT=NULL
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    StageSeq INT NOT NULL,                         -- 10/20/30
    StageCode NVARCHAR(50) NOT NULL,               -- 如 TJ_MACH/TJ_OUTS；引用全局字典 StageDict（方案 B：工厂+阶段码）；**v1.13：BOM↔Routing 对接主键之二，R20 场景采用目标工厂视角**（父件 TJ + R20 指派 BJ → 直接写 BJ_MACH）
    IsSupplyThreshold BIT NOT NULL DEFAULT 0,      -- 仅EDGE有效；ROOT恒为0
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
```

---

#### **2.3.4 APS批量接货（拉取BOM展开结果）**

**时间**：每天00:30（ODS展开完成后）

**动作**：
- APS拉取ODS库的BOM展开结果
- 利用**SqlBulkCopy**将数百万条纯净结果单向砸进APS本地库`APS_BOM_RAW`

**【校验红线】**：
- ✅ 拉取时必须同时校验：
  - `BatchNo`（匹配当前批次）
  - `Status = 'READY'`（展开已完成）
  - `ExpandedRowCount > 0`（有展开结果）
- ❌ 不得只按时间最新一批拉取

**C#代码示例**：
```csharp
// v5.0.34: 必须由 NightlyBatchOrchestrator 显式传入 planVersionId；禁止内部自查最新 PlanVersion
public async Task PullBOMResultFromODSAsync(string batchNo, long planVersionId)
{
    // 1. 校验批次状态
    var request = await odsRepository.GetBOMRequest(batchNo);
    
    if (request == null)
        throw new Exception($"批次不存在: {batchNo}");
    
    if (request.Status != "READY")
        throw new Exception($"批次状态异常: {request.Status}，预期: READY");
    
    if (request.ExpandedRowCount <= 0)
        throw new Exception($"批次展开结果为空: {request.ExpandedRowCount}");
    
    logger.LogInformation($"开始拉取BOM展开结果，BatchNo: {batchNo}, PlanVersionId: {planVersionId}, ExpandedRowCount: {request.ExpandedRowCount}");
    
    // 2. 从ODS库拉取展开结果
    using (var odsConnection = new SqlConnection(odsConnectionString))
    using (var apsConnection = new SqlConnection(apsConnectionString))
    {
        await odsConnection.OpenAsync();
        await apsConnection.OpenAsync();
        
        // 2.1 读取ODS库的展开结果（v1.8：APS侧仅拉ChildRequiredStageCode，不搬3个原始辅助字段）
        var reader = await odsConnection.ExecuteReaderAsync(@"
            SELECT 
                BatchNo,
                BOMNO,
                ParentMaterialCode,
                ChildMaterialCode,
                Quantity,
                Level,
                Path,
                ChildRequiredStageCode         -- v1.8 子件供给所需大工艺阶段码（5号位已回填；NULL=保守策略）
            FROM MES_APS_BOM_Workset
            WHERE BatchNo = @BatchNo
            ORDER BY Level, ParentMaterialCode",
            new { BatchNo = batchNo });
        
        // 2.2 使用SqlBulkCopy批量写入APS本地库
        using (var bulkCopy = new SqlBulkCopy(apsConnection))
        {
            bulkCopy.DestinationTableName = "APS_BOM_RAW";
            bulkCopy.BatchSize = 10000;
            bulkCopy.BulkCopyTimeout = 600;  // 10分钟超时
            
            bulkCopy.ColumnMappings.Add("BatchNo", "BatchNo");
            bulkCopy.ColumnMappings.Add("BOMNO", "BOMNO");
            bulkCopy.ColumnMappings.Add("ParentMaterialCode", "ParentMaterialCode");
            bulkCopy.ColumnMappings.Add("ChildMaterialCode", "ChildMaterialCode");
            bulkCopy.ColumnMappings.Add("Quantity", "Quantity");
            bulkCopy.ColumnMappings.Add("Level", "Level");
            bulkCopy.ColumnMappings.Add("Path", "Path");
            bulkCopy.ColumnMappings.Add("ChildRequiredStageCode", "ChildRequiredStageCode"); // v1.8
            
            await bulkCopy.WriteToServerAsync(reader);
        }
        
        // 2.3 v1.8新增、v1.9补StageScopeType：拉取StageDetail明细到APS_BOM_STAGE_PATH_RAW（含EDGE+ROOT）
        var stageReader = await odsConnection.ExecuteReaderAsync(@"
            SELECT BatchNo, BOMNO, StageScopeType,        -- v1.9 EDGE/ROOT
                   ParentMaterialCode, ChildMaterialCode,
                   StageSeq, StageCode, IsSupplyThreshold
            FROM MES_APS_BOM_Workset_StageDetail
            WHERE BatchNo = @BatchNo
            ORDER BY BOMNO, StageScopeType, ChildMaterialCode, StageSeq",
            new { BatchNo = batchNo });
        
        using (var bulkCopy2 = new SqlBulkCopy(apsConnection))
        {
            bulkCopy2.DestinationTableName = "APS_BOM_STAGE_PATH_RAW";
            bulkCopy2.BatchSize = 10000;
            bulkCopy2.BulkCopyTimeout = 600;
            
            bulkCopy2.ColumnMappings.Add("BatchNo", "BatchNo");
            bulkCopy2.ColumnMappings.Add("BOMNO", "BOMNO");
            bulkCopy2.ColumnMappings.Add("StageScopeType", "StageScopeType"); // v1.9
            bulkCopy2.ColumnMappings.Add("ParentMaterialCode", "ParentMaterialCode");
            bulkCopy2.ColumnMappings.Add("ChildMaterialCode", "ChildMaterialCode");
            bulkCopy2.ColumnMappings.Add("StageSeq", "StageSeq");
            bulkCopy2.ColumnMappings.Add("StageCode", "StageCode");
            bulkCopy2.ColumnMappings.Add("IsSupplyThreshold", "IsSupplyThreshold");
            
            await bulkCopy2.WriteToServerAsync(stageReader);
        }
    }
    
    logger.LogInformation($"BOM展开结果 + StageDetail拉取完成，BatchNo: {batchNo}");
    
    // 3. 更新批次状态为CONSUMED
    await odsRepository.UpdateBOMRequestStatus(batchNo, "CONSUMED");
    
    // 4. 生成 OrderBomRequestLink（v5.0.34：必须在 BOM+StageDetail 同步完成后执行）
    await GenerateOrderBomRequestLinkAsync(batchNo, planVersionId);
}

/// <summary>
/// v5.0.34 新增：生成 OrderBomRequestLink
/// 数据源：ODS MES_APS_BOM_Workset 聊合（不从 APS_BOM_RAW 反查）
/// 业务锋点：PlanVersionId + OrderCanonicalId
/// 找不到 OrderId：OrderId=NULL、LinkStatus='SKIPPED'，不阻断批次
/// </summary>
private async Task GenerateOrderBomRequestLinkAsync(string batchNo, long planVersionId)
{
    using var odsConn = new SqlConnection(odsConnectionString);
    using var apsConn = new SqlConnection(apsConnectionString);
    await odsConn.OpenAsync();
    await apsConn.OpenAsync();

    // Step 1+2: 从 ODS 聊合 RequestDetail + Workset Level=1 节点
    var links = await odsConn.QueryAsync<OrderBomLinkDto>(@"
        SELECT
            d.Id              AS RequestDetailId,
            d.OrderCanonicalId,
            d.OrderNo,
            d.SourceSystem,
            d.SourceOrderId,
            d.RequestedBOMNO,
            MIN(CASE WHEN w.Level = 1 THEN w.BOMNO END)  AS ResolvedBOMNO,
            MIN(CASE WHEN w.Level = 1 THEN w.Id   END)  AS RepWorksetId
        FROM MES_API_BOM_Request_Detail d
        LEFT JOIN MES_APS_BOM_Workset w
            ON w.BatchNo = d.BatchNo AND w.RequestDetailId = d.Id AND w.Level = 1
        WHERE d.BatchNo = @BatchNo
        GROUP BY d.Id, d.OrderCanonicalId, d.OrderNo, d.SourceSystem, d.SourceOrderId, d.RequestedBOMNO",
        new { BatchNo = batchNo });

    foreach (var link in links)
    {
        // Step 3: 在 APS.[Order] 按 PlanVersionId + OrderCanonicalId 找 OrderId
        var orderId = await apsConn.QuerySingleOrDefaultAsync<long?>(
            "SELECT Id FROM [Order] WHERE PlanVersionId = @PlanVersionId AND OrderCanonicalId = @OrderCanonicalId",
            new { planVersionId, link.OrderCanonicalId });

        // Step 4: 写入 OrderBomRequestLink
        var linkStatus = orderId.HasValue ? "RESOLVED" : "SKIPPED";
        var errorMessage = orderId.HasValue ? null
            : "Order not loaded into this PlanVersion";

        await apsConn.ExecuteAsync(@"
            INSERT INTO OrderBomRequestLink
                (PlanVersionId, BatchNo, OrderId, OrderCanonicalId, OrderNo,
                 SourceSystem, SourceOrderId, RequestDetailId,
                 RequestedBOMNO, ResolvedBOMNO, RepWorksetId, LinkStatus, ErrorMessage)
            VALUES
                (@PlanVersionId, @BatchNo, @OrderId, @OrderCanonicalId, @OrderNo,
                 @SourceSystem, @SourceOrderId, @RequestDetailId,
                 @RequestedBOMNO, @ResolvedBOMNO, @RepWorksetId, @LinkStatus, @ErrorMessage)",
            new {
                planVersionId, batchNo,
                OrderId       = orderId,
                link.OrderCanonicalId, link.OrderNo,
                link.SourceSystem, link.SourceOrderId, link.RequestDetailId,
                link.RequestedBOMNO, link.ResolvedBOMNO, link.RepWorksetId,
                linkStatus, errorMessage
            });
    }

    logger.LogInformation($"OrderBomRequestLink 生成完成，BatchNo: {batchNo}, PlanVersionId: {planVersionId}, 条数: {links.Count()}");
}
```

---

### 2.4 环节三：深夜00:30的APS本地精加工

#### **2.4.1 计算低阶码（LLC）**

**时间**：每天00:30（BOM拉取完成后）

**动作**：执行本地存储过程`sp_CalculateLLC`

**【局部LLC规则】**：
- ✅ 在APS本地库，仅针对今晚拉回来的`APS_BOM_RAW`活跃工作集计算低阶码（LLC）
- ✅ 明确放弃全厂4000万物料的全局LLC
- ✅ 将算力极致收敛于当晚快照

**LLC计算逻辑**：
- LLC = 0：顶层成品（没有父件）
- LLC = 1：成品的直接子件
- LLC = N：第N层子件
- 叶子节点：没有子件的采购件

**SQL存储过程**：
```sql
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
```

---

#### **2.4.2 主数据演进路径与职责分工**

**主数据完整演进路径**：

```
【ERP主数据路径】
ERP生产库.master表
  ↓ (ERP DBA负责创建契约视图)
ODS库.ERP_Master_View (契约视图 - 源系统侧防腐)
  ↓ (2号位在APS库创建跨库包装视图)
APS库.ext_ERP_Master_View (跨库包装视图 - APS侧跨库访问)
  ↓ (2号位拉取数据)
APS库.MaterialMapping (拉链表，SCD Type 2)
  ↓ (2号位同步，提取IsCurrent=1)
APS库.Material表 (当前有效版本)

【MES主数据路径】
MES生产库.MES_Material表
  ↓ (MES DBA负责创建契约视图)
ODS库.MES_Material_View (契约视图 - 源系统侧防腐)
  ↓ (2号位在APS库创建跨库包装视图)
APS库.ext_MES_Material_View (跨库包装视图 - APS侧跨库访问)
  ↓ (2号位拉取数据)
APS库.MaterialMapping (拉链表，SCD Type 2)
  ↓ (2号位同步，提取IsCurrent=1)
APS库.Material表 (当前有效版本)

【工艺大阶段路径 — 阶段字典】（v1.7新增，v1.8定位调整 2026-04-13 更新）
MES生产库.工艺阶段源数据
  ↓ (3号位负责创建ODS视图)
ODS库.MES_APS_Routing_Stage_View (契约视图 - 输出MES_ID+Model+StageCode+StageName+StageSeq)
  ↓ (2号位在APS库创建跨库包装视图)
APS库.ext_MES_APS_Routing_Stage_View (跨库包装视图)
  ↓ (2号位拉取，通过MaterialMapping映射MES_ID→MaterialId)
APS库.RoutingStage表 (⚠️ v1.8定位：阶段字典/标准阶段语言，不作为排程权威阶段顺序源)
  ↓ RoutingOperation.StageCode 引用 RoutingStage.StageCode（N:1）
  ⚠️ 已知限制：MES工艺侧不包含外协阶段，数据可能不完整

【BOM双层结果路径】（v1.8双层结果，v1.9升级ROOT，v5.0.26 迁移物化边表 2026-05-15 更新）
MES_BOM_Edge_Active.ParentProcRefCode + ChildProcRefCode + ChildSourceHintCode (5号位刷新时从源层保留字段)
  ↓ sp_ExpandBOMBatch_vNext WHILE迭代展开+透传3辅助字段（直读 MES_BOM_Edge_Active，禁递归CTE）
MES_APS_BOM_Workset (主层：3辅助字段 + ChildRequiredStageCode，不承载根产品自身路径)
  ↓ 5号位后置回填（双层结果 + ROOT路径）
  ├→ MES_APS_BOM_Workset.ChildRequiredStageCode (最终供给阈值)
  └→ MES_APS_BOM_Workset_StageDetail (统一阶段路径结果表，v1.9升级)
       ├→ StageScopeType='EDGE': 子件供给路径 (ParentMaterialCode=父件, ChildMaterialCode=子件)
       └→ StageScopeType='ROOT': 根产品完工路径 (ParentMaterialCode=NULL, ChildMaterialCode=根产品, IsSupplyThreshold=0)
           ROOT推导：Level=1的ParentProcRefCode → 映射标准化 → 多条不一致取最长+记WARNING
  ↓ 2号位 PullBOMFromODS (APS侧只拉最终结果，不搬3辅助字段)
  ├→ APS_BOM_RAW.ChildRequiredStageCode
  └→ APS_BOM_STAGE_PATH_RAW (StageScopeType+StageSeq+StageCode+IsSupplyThreshold，含EDGE+ROOT)
  ↓ NULL降级策略：ChildRequiredStageCode=NULL → 子件必须全工艺完成后才可供给（保守策略，写死不留自由判断）
  ↓ 装载职责：**2号位必须按StageScopeType区分查询并装入ScheduleContext**；**2号位根据 EDGE/ROOT 路径形成工序级 TaskDraft**（EDGE 路径逐 RoutingOperation 实例化，ROOT 路径实例化根产品自身 Task，无小工序的外协阶段查 StageLeadTimeParam）；**1号位仅通过方法参数消费上述内存TaskDraft，执行合并/拆分、资源与时间排定；不查询数据库**
```

**职责分工（Socket-Plug模式）**：
- **ERP/MES DBA**：负责创建源系统侧的契约视图（ERP_Master_View、MES_Material_View；MES_BOM_View v1.8加列 ParentProcRefCode+ChildProcRefCode+ChildSourceHintCode，v1.9加列SourceSystem+SourceBOMId追溯增强 + ODS内部裁决唯一默认版本）
- **2号位**：负责在APS库创建跨库包装视图（ext_ERP_Master_View、ext_MES_Material_View）、执行 sp_SyncMasterData 双源三表协同同步；RoutingStage 装载；v1.8新增 APS_BOM_STAGE_PATH_RAW 搬运

---

#### **2.4.3 双源三表协同同步（Material + MaterialMapping + MaterialSupplyContext）**（2026-04-01 v1.2更新）

**时间**：每天00:10（ERP）、每天00:20（MES）

**动作**：
- 执行 `sp_SyncMasterData(@SourceType='ERP')` 从 ext_ERP_Master_View 同步
- 执行 `sp_SyncMasterData(@SourceType='MES')` 从 ext_MES_Material_View 同步
- 三表协同：Material（物料主身份）→ MaterialMapping（来源映射）→ MaterialSupplyContext（仓库级供给上下文）

**SCD Type 2逻辑**：
- 如果物料映射关系发生变更（如masterID变更）
- 将旧记录的`ValidTo`设置为当前时间，`IsCurrent`设置为0
- 插入新记录，`ValidFrom`设置为当前时间，`IsCurrent`设置为1

**⚠️ Material表维护策略（重要）**：
- **更新机制**：**增量Upsert**（非全量重建）
- **触发条件**：由 `MaterialMapping` 表的变更触发更新
  - 当 `MaterialMapping` 中的映射关系发生变化时（新增、修改、失效）
  - 当主数据属性（MaterialName、MaterialType、UOM等）发生变化时
- **更新范围**：只更新发生变化的物料记录，不影响未变更的物料
- **架构红线**：
  - ❌ **禁止**每天全量删除重建 `Material` 表
  - ✅ **必须**采用增量Upsert，保持物料ID稳定性
  - ✅ **必须**基于 `MaterialMapping.IsCurrent = 1` 的记录进行同步
  - ✅ 物料ID（`Material.Id`）在首次创建后保持稳定，不因映射关系变更而改变

**SQL存储过程**（2026-04-01 v4.0更新：双源同构三表协同同步，详见《双源同构主数据三表协同同步设计_v2.0》）：

> ⚠️ **v4.0 重构**：原 `sp_SyncERPMasterData` + `sp_SyncMESMaterialData` 已合并为统一参数化存储过程：
> - `sp_SyncMasterData(@SourceType)`：ERP/MES 双源三表协同同步（Material + MaterialMapping + MaterialSupplyContext）
> - 双源同构契约：两个视图字段完全一致，SP 逻辑零分叉

```sql
-- =====================================================================
-- 存储过程：sp_SyncMasterData（v4.0 双源统一，原 sp_SyncERPMasterData + sp_SyncMESMaterialData）
-- 负责人：2号位（技术负责人）
-- 执行时机：每天00:10(ERP) / 00:20(MES)
-- 数据来源：ext_ERP_Master_View 或 ext_MES_Material_View（双源同构契约）
-- 同步顺序：Material → MaterialMapping(SCD Type 2) → MaterialSupplyContext(SCD Type 2)
-- v4.0 核心变更：
--   1. 合并为统一参数化 SP，@SourceType 区分 ERP/MES
--   2. MaterialMapping 统一为 SourceID + Warehouse（消除 ERP/MES 字段分叉）
--   3. MaterialType 由 APS 按 MaterialCode 前缀推导（FG/RAW/WIP/ASSY/UNKNOWN）
--   4. MES 也执行三表协同（不再区分"ERP三表、MES两表"）
--   5. 新增 InventoryManagementMode 字段同步
-- 完整代码见 DDL 文件：APS_数据库表结构设计_v4.0.sql 第 4.3 节
-- =====================================================================

CREATE OR ALTER PROCEDURE sp_SyncMasterData
    @SourceType NVARCHAR(20),       -- 'ERP' 或 'MES'
    @BatchNo NVARCHAR(50) = 'DAILY',
    @RowsAffected INT OUTPUT,
    @ErrorMessage NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 步骤0：按 @SourceType 提取源端快照（双源同构，SELECT 列完全一致）
        --        MasterID AS SourceID, Warehouse, InventoryManagementMode ...
        --        消歧：按 MaterialCode + Warehouse_Norm 取 SourceID 最大的

        -- 步骤1：同步 Material（仅按 MaterialCode 维护，MaterialType 由前缀推导）
        MERGE INTO Material ... 
        WHEN NOT MATCHED BY TARGET THEN INSERT (MaterialType = CASE前缀推导);

        -- 步骤2：同步 MaterialMapping（SCD Type 2，按 MaterialCode + Source + Warehouse_Norm）
        MERGE INTO MaterialMapping AS target
        USING #Source_Snapshot AS source
        ON target.MaterialCode = source.MaterialCode 
           AND target.Source = @SourceType AND target.IsCurrent = 1
           AND target.Warehouse_Norm = source.Warehouse_Norm
        WHEN MATCHED AND SourceID相同 THEN 刷新 UpdatedAt
        WHEN MATCHED AND SourceID变化 THEN 关闭旧记录(IsCurrent=0)
        WHEN NOT MATCHED BY TARGET THEN INSERT 新记录
        WHEN NOT MATCHED BY SOURCE AND Source=@SourceType AND IsCurrent=1 THEN 关闭(源端消失);
        -- SCD Type 2 闭环：为SourceID变化被关闭的旧记录插入新版本
        -- 全部仓库映射消失的物料 → Material.IsActive = 0

        -- 步骤3：同步 MaterialSupplyContext（SCD Type 2，按 MaterialCode + WarehouseCode）
        --   追踪字段：SupplyMode, DefaultProductionDeptCode, LeadTimeDays,
        --             SafetyStock, InventoryManagementMode
        -- 3a. 准备源数据（#Source_Snapshot JOIN MaterialMapping WHERE IsCurrent=1）
        -- 3b. 供给属性变化 → 关闭旧版本
        -- 3c. 属性变化 + 全新记录 → 插入新版本
        -- 3d. 源端消失的仓库 → 关闭对应记录

        -- 步骤4：记录 ETL 日志

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        -- 记录失败日志
    END CATCH
END;
GO
```

---

#### **2.4.4 产品族解析链（V1 简化口径）**（v5.0.38 口径修正）

> ⚠️ **V1 口径**：**不建三张产品族规则表，不执行 `sp_ResolveMaterialProductFamily`**。ODS 视图由 5号位直接输出解析字段；APS 层只做码表映射。三张规则表和 `sp_ResolveMaterialProductFamily` 仅作 **V2 预留**。

**架构原则**：产品族解析逻辑完全封装在 ODS 层；APS_Production 层禁止包含任何解析规则或触碰 ERP 原始字段

**V1 执行路径**：

| 环节 | 执行方 | 说明 |
|------|--------|------|
| ODS 视图直出 | **5号位** | `ERP_Master_View` / `MES_Material_View` 直接暴露 `IsProductFamilyRequired`、`ProductFamilyCode`、`FamilyResolveStatus` 三个字段（无需中间规则表） |
| APS 层码表映射 | **2号位** | `sp_SyncMasterData` 步骤1c：`ProductFamilyCode` → `ProductFamily.Code` → 写入 `Material.ProductFamilyId`（仅 `FamilyResolveStatus='RESOLVED'` 时更新；未解析物料 `ProductFamilyId` 保持 NULL） |
| Order 继承 | **2号位** | `Order.ProductFamilyId` 从 `Material.ProductFamilyId` 继承；**禁止订单层另建解析链** |

**V1 调度顺序**（无 `sp_ResolveMaterialProductFamily`）：

```
00:00  sp_ValidateAndPromoteOrders（订单提升）
00:10  sp_SyncMasterData(@SourceType='ERP')（包含步骤1c）
00:20  sp_SyncMasterData(@SourceType='MES')（包含步骤1c）
00:30  sp_CalculateLLC
00:40  sp_SyncInventorySnapshot（库存快照，详见 §2.4.5）
02:00  排程引擎启动
```

**V2 预留（V1 不建表、不执行）**：
- `MaterialProductFamilyScopeRule`（定义哪些 MaterialCode 模式需要解析）
- `MaterialProductFamilyRule`（Spec/MaterialCode 截位比较规则，输出 ProductFamilyCode）
- `MaterialProductFamilyResolved`（sp 输出结果表）
- `sp_ResolveMaterialProductFamily`（ODS 产品族解析存储过程）

**设计红线**：
- 🛑 APS_Production 层禁止包含任何产品族拆分、模式匹配、路由逻辑
- 🛑 禁止存储过程内出现 ERP 原始表名/字段名
- 🛑 `Order.ProductFamilyId` 必须从 `Material.ProductFamilyId` 继承，禁止订单层另建解析链
- 🛑 V1 调度序列中**无** `sp_ResolveMaterialProductFamily`；引入须进入 V2 变更评审流程

---

#### **2.4.5 现货库存快照同步（sp_SyncInventorySnapshot）**（v5.0.39 新增）

**时间**：每天 00:40（`sp_SyncMasterData` 完成后，排程发令枪前）  
**负责人**：2号位  
**设计口径**：V1 全量快照；每次以 `@BatchNo` 标记本轮快照；`InventoryAllocationResult`（订单消耗明细）V1 不执行

**六步 ETL**（v5.0.40 更新）：

| 步骤 | 动作 | 目标表 |
|-----|------|--------|
| 步骤1 | 全量读取 `ext_ERP_Inventory_View`，过滤零库存 | `InventoryFact_ERP`（TRUNCATE+INSERT） |
| 步骤2 | 全量读取 `ext_MES_Inventory_View`，V1主链写 WarehouseCode | `InventoryFact_MES`（TRUNCATE+INSERT） |
| 步骤3 | 通过 `MaterialMapping`（含 `Warehouse_Norm`）桥接：SourceID+WarehouseCode → MaterialCode+FactoryId；IsEligible=0 | `InventorySupplyCandidate`（TRUNCATE+INSERT） |
| 步骤4 | **规则裁决**：匹配所有 `InventoryAvailabilityRule`（含 IsAvailable=0/1）；按 (CandidateId, ProductFamilyId) 选唯一胜出规则；胜出规则 IsAvailable=1 → 写 `InventoryAvailableSupplyDetail`（含 `InventorySupplyCandidateId`）+ IsEligible=1；IsAvailable=0 → RejectReason；无匹配 → RejectReason='NoRuleMatch' + WARN | `#WinnerRules`（临时表） → `InventoryAvailableSupplyDetail`（TRUNCATE+INSERT）；`InventorySupplyCandidate`（UPDATE） |
| 步骤5 | 从 `InventoryAvailableSupplyDetail` 汇总写入 `InventoryBalance`；ProductFamilyId 来源：规则输出；TRUNCATE 全量替换 | `InventoryBalance`（TRUNCATE+INSERT） |
| 步骤6 | 写入 ETL 成功日志，含 Balance 行数 + Detail 行数 | `APS_ETL_Log` |

**关键设计决策**：
- `InventoryBalance.ProductFamilyId` 来源：`InventoryAvailabilityRule.ProductFamilyId`（库存使用上下文）；**❌ 不取 `Material.ProductFamilyId`**
- `InventoryAvailableSupplyDetail` 是规则命中后、汇总前的明细层；保留 `AvailabilityRuleId` + `RulePriority`，供排程引擎按优先级扣减；`InventoryBalance` 只做汇总余额
- `InventoryBalance.BatchNo` = 快照批次标签；非订单消耗记录（订单消耗追溯 V1.1/V2 由 `InventoryAllocationResult` 承接）
- MES 主链：`InventoryFact_MES.Location` 字段名历史保留，实际写入 `MES_Inventory_View.WarehouseCode`；`LocationCode` 仅追溯
- 规则裁决：**无匹配规则 → 不进 `InventoryBalance`，写 NoRuleMatch WARN**；`IsAvailable=0` 排除规则参与裁决，可覆盖通配规则；**❌ 删除旧口径"无匹配时 IsEligible 保持1（白名单兜底）"**
- `InventoryBalance` V1 全量替换（TRUNCATE），与 `UNIQUE(MaterialCode, ProductFamilyId, FactoryId)` 约束无冲突
- 全流程在单一事务中执行；CATCH 回滚并写 FAILED 日志

**管道供给并行链说明（v1.36当前口径）**：
- 管道供给与现货库存六层链并行，**不写入、不汇总到 `InventoryBalance`**。
- 已接入来源按各自 ODS 契约视图进入 `ext_PipelineSupply_Source_View`；尚未接入的来源分支允许返回0行，0行是该来源正常结果，不等于整个管道主链固定空跑。
- `sp_SyncPipelineSupply` 按 `BatchNo + DataCutoffTime` 生成 `SupplyFact_Pipeline` 精确切片；夜间读取当前批次，Candidate读取 BasePlanVersion 对应 SourceScheduleRun 的准确批次。
- 任何来源数量为0、映射失败或时间不确定时按规则排除或登记Issue；不得因某一来源为空而清空其他已接入来源。

---

### 2.5 环节四：02:00巅峰对决与多源数据缝合

#### **2.5.1 排程发令枪响，网络切断**

**时间**：每天02:00

**动作**：
- **2号位** 构建 RuntimeDemand/RuntimeSupply、Pegging Ledger、LogicalBlock 与 `TaskDraft`；**1号位** 通过方法参数消费内存TaskDraft，执行有限产能合并/拆分和时间排定并返回 `ScheduledTaskDraft + ComponentShares`；**2号位** 完成正式 Task/ShippingTask、Ledger最终映射和物理 Pegging 的批量持久化
- 利用以下两套严格的纪律缝合残缺数据

---

#### **2.5.2 库存双源汇聚【互斥隔离红线】**

**问题**：
- ERP库存和MES库存可能存在**同一物料的重复数据**
- 必须明确**来源优先级**，避免重复计算

**【互斥隔离红线】**：

**规则1：ERP独占类（原材料/成品）**
- ✅ 绝对以ERP账本为准（按`masterID + 仓库`提取）
- ✅ 强行过滤掉MES中的同类线边库存

**规则2：MES独占类（中间M-BOM组件）**
- ✅ 绝对以MES账本为准（按 `SourceID + Warehouse` 提取，即MES的MES_ID + Location同构映射）（2026-04-01 v1.2更新）

**规则3：双源并存处理**
- ✅ 两股水流进入APS后，折算为统一的`MaterialCode`
- ⚠️ 若发生同一`MaterialCode`双源并存，通过 `InventoryAvailabilityRule` 的 `IsAvailable`+`Priority` 判定（v5.0.39 口径）
- ❌ 不允许默认叠加

> ⚠️ **v5.0.39 口径变更**：旧 `InventorySourcePriority`（v2.8废弃）、`ProductFamilyInventoryScope`（v5.0.39删除）、`InventorySourceRule`（v5.0.39删除）均已移除。V1 统一使用 `InventoryAvailabilityRule`（`IsAvailable`=1允许进入可用库存池；`IsAvailable`=0排除；`Priority`表示扣减优先级）。库存汇聚逻辑由 `sp_SyncInventorySnapshot(@BatchNo)` 六步 ETL 实现，详见 §2.4.5。

---

#### **2.5.3 Routing缺失降级策略**

**问题**：
- 部分物料可能**没有工艺路线数据**
- 需要根据**时间区域**采取不同策略

**【边界配置规则】**：
- 默认以**近7天为严格区**、**890天为规划区**
- 实际边界应服从产品族/工厂/资源组配置的**Fence规则**

**降级策略**：

**策略1：冻结区/近期（0-7天）**
- ❌ 无工艺路线则**挂起报错**
- ✅ 逼迫人工补录

**策略2：远期规划区（8-90天）**
- ✅ 读取物料主数据的**默认LeadTimeDays**占位粗排
- ⚠️ 标记为"粗排"状态，供计划员后续精调

**C#代码示例**：
```csharp
public async Task<Routing> GetRoutingOrFallback(string materialCode, DateTime dueDate)
{
    // 1. 尝试获取工艺路线
    var routing = await routingRepository.GetByMaterialCode(materialCode);
    
    if (routing != null)
        return routing;
    
    // 2. 无工艺路线，判断时间区域
    var daysFromNow = (dueDate - DateTime.Now).TotalDays;
    
    if (daysFromNow <= 7)
    {
        // 冻结区/近期：报错
        throw new RoutingNotFoundException($"物料 {materialCode} 在冻结区内无工艺路线，请人工补录");
    }
    else
    {
        // 远期规划区：使用默认LeadTime占位
        var material = await materialRepository.GetByCode(materialCode);
        
        if (material == null || material.DefaultLeadTimeDays <= 0)
        {
            throw new Exception($"物料 {materialCode} 无工艺路线且无默认LeadTime");
        }
        
        // 生成占位工艺路线
        return new Routing
        {
            MaterialCode = materialCode,
            Operations = new List<Operation>
            {
                new Operation
                {
                    OperationNo = "PLACEHOLDER",
                    OperationName = "占位工序（粗排）",
                    StandardTime = material.DefaultLeadTimeDays * 24 * 60,  // 转换为分钟
                    ResourceGroup = "DEFAULT",
                    IsPlaceholder = true
                }
            }
        };
    }
}
```

---

### 2.6 环节五：02:15历史追溯的终极解法（快照封存）

#### **2.6.1 快照封存设计**

**时间**：每天02:15（排程完成后）

**动作**：
- 将256G内存中参与运算的完整`ScheduleContext`序列化为高度压缩的`.json.gz`文件
- 封存到物理服务器的4.76T机械硬盘

**【快照内容】**：
- ✅ 当时那一毫秒的**绝对工艺路线**
- ✅ **标准工时**
- ✅ **BOM层级对**
- ✅ **库存余额**
- ✅ **资源日历**
- ✅ **订单优先级**
- ✅ **Pegging关系**
- ✅ **Task拆批规则**

**【快照格式】**：
```json
{
  "BatchNo": "REQ_20260310_01",
  "PlanVersionId": 12345,
  "SnapshotTime": "2026-03-10T02:15:00",
  "Context": {
    "Orders": [...],
    "Materials": [...],
    "BOM": [...],
    "Routings": [...],
    "Inventory": [...],
    "ResourceCalendar": [...],
    "Tasks": [...],
    "PeggingLinks": [...]
  }
}
```

**C#代码示例**：
```csharp
public async Task SaveScheduleSnapshot(ScheduleContext context, int planVersionId)
{
    // 1. 生成快照文件名
    var batchNo = context.BatchNo;
    var fileName = $"Snapshot_{planVersionId}_{batchNo}.json.gz";
    var filePath = Path.Combine(snapshotDirectory, fileName);
    
    // 2. 序列化为JSON
    var json = JsonSerializer.Serialize(context, new JsonSerializerOptions
    {
        WriteIndented = false,  // 不缩进，减少文件大小
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
    
    // 3. 压缩为.gz
    using (var fileStream = File.Create(filePath))
    using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
    using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
    {
        await writer.WriteAsync(json);
    }
    
    // 4. 计算文件哈希（SHA256）
    var fileHash = await CalculateFileHash(filePath);
    var fileSize = new FileInfo(filePath).Length;
    
    // 5. 更新PlanVersion表
    await planVersionRepository.UpdateSnapshot(planVersionId, new SnapshotInfo
    {
        FilePath = filePath,
        FileSize = fileSize,
        FileHash = fileHash,
        CompressedSize = fileSize
    });
    
    logger.LogInformation($"快照封存完成，PlanVersionId: {planVersionId}, FilePath: {filePath}, Size: {fileSize} bytes");
}

private async Task<string> CalculateFileHash(string filePath)
{
    using (var sha256 = SHA256.Create())
    using (var stream = File.OpenRead(filePath))
    {
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
```

---

#### **2.6.2 快照读取与历史追溯**

**使用场景**：
- 事后查底稿（追溯定责）
- 版本对比（对比两个版本的差异）
- 数据分析（分析历史排程趋势）

**C#代码示例**：
```csharp
public async Task<ScheduleContext> LoadScheduleSnapshot(int planVersionId)
{
    // 1. 从PlanVersion表获取快照信息
    var snapshotInfo = await planVersionRepository.GetSnapshot(planVersionId);
    
    if (snapshotInfo == null || !File.Exists(snapshotInfo.FilePath))
    {
        throw new FileNotFoundException($"快照文件不存在，PlanVersionId: {planVersionId}");
    }
    
    // 2. 校验文件哈希
    var actualHash = await CalculateFileHash(snapshotInfo.FilePath);
    if (actualHash != snapshotInfo.FileHash)
    {
        throw new Exception($"快照文件已损坏或被篡改，PlanVersionId: {planVersionId}");
    }
    
    // 3. 解压并反序列化
    using (var fileStream = File.OpenRead(snapshotInfo.FilePath))
    using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
    using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
    {
        var json = await reader.ReadToEndAsync();
        var context = JsonSerializer.Deserialize<ScheduleContext>(json);
        return context;
    }
}
```

---

### 2.7 环节六：白天增量同步与实时评估支持（v1.34 重写 2026-07-13）

**核心口径**：每小时增量同步只更新 `Order_Canonical`；白天实时评估与局部重排一律由 PMC/销售/计划员**人工发起**，通过 3号位创建 `Scenario`（适用时）和 `ScheduleRun`。ScopeJson 完整 Schema 引用《集成接口设计 v1.28》唯一权威契约，本节不复制。

---

#### **2.7.1 订单增量同步**

**时间**：每小时整点（01:00, 02:00, ..., 23:00）

**动作**：
- 从 ERP 契约视图 `v_APS_SalesOrder` 拉取最近 1 小时增量订单
- 写入 `ERP_Order_Staging`（Status='PENDING'）
- 执行 `sp_ValidateAndPromoteOrders`：Staging 校验通过 → Upsert 到 `Order_Canonical`
- **同步到此结束**：不检测 worksetCache，不写 RequestDetail，不触发 Realtime BOM，不创建任何运行对象

---

#### **2.7.2 人工实时评估入口**

**触发方**：PMC / 销售 / 计划员，通过 4号位页面。

**白天 RunType + Purpose 合法组合（四类常用入口 + 单域较大范围人工重排）**：CTP / 插单影响分析 / 插单局部重排 / 人工局部重排 / 单域较大范围人工重排（详见 §2.8）。

**动作**：
1. 4号位页面展示新增/变化订单、影响分析、推荐清单
2. 人工选择业务场景并确认发起
3. **3号位** 归一化 `ScopeJson`（唯一权威见《集成接口 v1.28》），创建 `Scenario`（适用时）
4. **3号位** 创建 `ScheduleRun`（`BasePlanVersionId` + `StrategyProfileVersionId` + `ScopeJson` + `ExpectedDomainKeysJson`（=["BasePlanVersion.DomainKey"]，严格单 Domain，独立运行级字段，不属 ScopeJson）+ `DataCutoffTime`）
5. 3号位创建 Candidate 类 PlanVersion 版本壳（初始 Status=BUILDING；2号位结果持久化成功后再转为 CANDIDATE）

**红线**：不允许"自动发现新订单即触发"；不允许"5号位手动触发增量排程"；`ScheduleRun.ScenarioId` 允许为空。

---

#### **2.7.3 Candidate Order 快照**

**动作**：**2号位** 调用 `PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)`。

**语义**：
- 服务读取 Base 版本 Order + 最新 `Order_Canonical`
- 生成 Candidate PlanVersion 独立 Order 快照
- 之后所有 Pegging / 排程内核只消费 Candidate Order，**不直接消费 Order_Canonical**
- **不修改 Base 版本 Order**

---

#### **2.7.4 RequestDetail 实时 BOM 链**

**动作**：
1. 2号位检查 Scope 内 BOM 切片能否复用
2. 若不可复用，写 `MES_API_BOM_Request_Detail` 并以 `RequestDetailId` 触发 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)`
3. 展开结果写入 `MES_APS_BOM_Workset_Realtime`
4. `sp_ExpandBOMRealtime_vNext` 内部调用 `sp_EnrichBOMWorksetRealtime(@ResolvedBOMNO, @RequestDetailId)`
5. `sp_EnrichBOMWorksetRealtime` 在 Step 5 日志前调用 `sp_GenerateBOMCrossFactoryEdgeRealtime(@BOMNO, @RequestDetailId)`
6. 写入 `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`
7. `sp_ExpandBOMRealtime_vNext` 更新 `MES_API_BOM_Request_Realtime.Status='READY'`，回填 `ExpandedRowCount`（仅诊断）

**Issues 切片号（v1.34 统一）**：
- `RequestDetailId` 非空：`BatchNo = RT:RD:{RequestDetailId}`（正式路径）
- `RequestDetailId` 为空：`BatchNo = LEFT('RT:' + @ResolvedBOMNO, 50)`（**deprecated 兼容**）

**READY 权威**：只以 `MES_API_BOM_Request_Realtime.Status='READY'` 为唯一权威；三张 Realtime 结果表允许 0 行；`CrossFactoryEdge_Realtime` 为 0 行合法。

---

#### **2.7.5 Realtime 结果搬运**

**动作**：**2号位** 将 ODS 库的三张 Realtime 表搬运到 APS 库的三张 RAW 表：
- `MES_APS_BOM_Workset_Realtime` → `APS_BOM_RAW`
- `MES_APS_BOM_Workset_StageDetail_Realtime` → `APS_BOM_STAGE_PATH_RAW`
- `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime` → `APS_BOM_CROSS_FACTORY_EDGE_RAW`

**BatchNo 写入规则**：三张 RAW 表统一写入 `BatchNo = RT:RD:{RequestDetailId}`。该值是**实时切片标识**（不是夜间批次号），供 Pegging 上下文按同一切片键筛选实时链路数据；同时避免与夜间批次号命名空间冲突。

搬运完成后生成 `OrderBomRequestLink`。

---

#### **2.7.6 剩余供给、执行事实及内存 Ledger（v1.36重写）**

**动作**：**2号位** 调用 `BuildRemainingSupplyContextAsync(candidatePlanVersionId)`，从 Base ACTIVE 对应运行的精确输入/结果切片中分类恢复 Candidate 可用上下文：

| Base状态 | Candidate处理 |
|---|---|
| 已实际消耗/已最终完成 | 永久不可重新使用 |
| `DemandSupplyHardLock` | 只恢复给原需求，不进入普通竞争池 |
| `ExecutionLock`剩余投入需求 | 作为固定执行节点的优先投入需求恢复；已领料/已实物预留来源不得释放 |
| `ExecutionLock`未来产出 | 作为固定生产指示、Stage、MES工单、数量和AvailableTime的未来供给；无HardLock部分可在Scope内重新Pegging |
| Scope外SOFT分配 | 暂时保留，禁止Candidate越界影响 |
| Scope内SOFT分配 | 释放后按Candidate最新需求顺序重新Pegging |
| Base未分配供给 | 直接进入Candidate可用池 |

**数据切片规则**：
- 现货库存、PI快照、MES进度、管道供给和Ledger均读取 BasePlanVersion 对应 SourceScheduleRun/Domain 的准确切片，禁止重新读取当前全局库存或仅按 `IsActive=1` 扫描历史管道数据。
- `PipelineSupplies`可以为空，但必须来自准确批次的真实结果；不得使用“V1固定空集合”作为算法分支。
- 构建 `ScheduleContext` 后，2号位应用5号位返回的 `PeggingAllocationDecision`，原子扣减需求/供给余额并写内存Ledger；只形成 `LogicalBlock / TaskDraft`，不提前落正式Task。

**红线**：Candidate不回写 `InventoryBalance`，不修改 Base/ACTIVE 的 Ledger、`PeggingSupplyAllocation`、ExecutionLock或HardLock；不同Candidate相互隔离。激活前必须重新校验实际消耗、ExecutionLock和HardLock自 `DataCutoffTime` 后是否变化。

---

#### **2.7.7 Candidate 输出与激活边界**

**动作**：
1. **2号位** 将Candidate的Ledger分配结果转换为 `LogicalBlock / TaskDraft`
2. **1号位** 通过内部接口参数消费2号位传入的内存TaskDraft，在有限产能、交期、资格与批量约束下执行合并/拆分和时间排定，返回 `ScheduledTaskDraft + ComponentShares + ExplanationFactDraft`
3. **2号位** 批量持久化 Candidate 的正式 Task / ShippingTask / Ledger最终Task映射 / PeggingSupplyAllocation / 物理Pegging / 影响分析 / KPI
4. Candidate `PlanVersion` 状态保持为 CANDIDATE

**激活规则**：
- **CTP 承诺交期评估**：不得激活
- **插单影响分析**：不得激活
- **插单局部重排**：审批后可激活为 ACTIVE
- **人工局部重排**：审批后可激活为 ACTIVE
- 非 FULL_SCHEDULE 运行不得自动 ACTIVE

---

> ⚠️ **旧版 sp_ExpandBOMRealtime(@BOMNO) 已 deprecated**：v5.0.26 起由 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` 替代；v1.34 正式路径只承认 RequestDetail 入口。以下保留的旧代码块仅供历史参考，不作为正式接口。

---

#### **2.7.X 历史 deprecated 参考（v1.34 已废弃）**

> ⚠️ 本小节仅供历史参考。v1.34 正式路径只承认 RequestDetail 入口，详见 §2.7.1–2.7.7；"每小时增量同步发现新订单即触发"的旧口径已废止。
>
> 以下 SQL/C# 代码块为 v5.0.26 之前的旧版 `sp_ExpandBOMRealtime(@BOMNO)` 及其调用逻辑，仅供追溯，不作为正式接口。

**（历史）v5.0.26 vNext 主路径流程图**：

```
2号位检测无 Workset 缓存的新订单（OrderCanonicalId）
        
        
写入 MES_API_BOM_Request_Detail（OrderCanonicalId/MaterialCode/RequestedBOMNO）
        ↓
        
        
以 RequestDetailId 触发 sp_ExpandBOMRealtime_vNext（BOMNO 可空；无 BOMNO 时按规则解析）
        
        
展开结果写入 MES_APS_BOM_Workset_Realtime
        
        
调用 sp_EnrichBOMWorksetRealtime(@ResolvedBOMNO, @RequestDetailId)
        
        
5分钟内完成；紧急插单 CTP 评估
```

> **旧版 sp_ExpandBOMRealtime(@BOMNO) 表结构与 SQL/C#（v5.0.26 之前，v1.34 deprecated 仅供追溯）**：

**ODS库实时展开表（deprecated）**：
```sql
CREATE TABLE MES_API_BOM_Request_Realtime (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BOMNO NVARCHAR(50) NOT NULL,
    RequestTime DATETIME2 NOT NULL DEFAULT GETDATE(),
    Status NVARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING/PROCESSING/READY/FAILED
    CompletedTime DATETIME2 NULL,
    ExpandedRowCount INT NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    RetryCount INT NOT NULL DEFAULT 0
);

CREATE INDEX IX_Realtime_Status ON MES_API_BOM_Request_Realtime(Status, RequestTime);
```

**ODS库实时展开存储过程（deprecated  请改用 sp_ExpandBOMRealtime_vNext(@RequestDetailId)）**：
```sql
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
        RETURN;  -- 已展开，直接返回
    END;
    
    -- 2. 插入请求记录
    INSERT INTO MES_API_BOM_Request_Realtime (BOMNO, RequestTime, Status)
    VALUES (@BOMNO, @StartTime, 'PROCESSING');
    
    SET @RequestId = SCOPE_IDENTITY();
    
    -- 3. 执行展开（同批次展开逻辑，但只展开单个BOMNO）
    BEGIN TRY
        WITH BOM_Recursive AS (
            -- 第1层：基于BOMNO展开
            SELECT 
                b.BOMNO,
                b.ParentMaterialCode,
                b.ChildMaterialCode,
                b.Quantity,
                1 AS Level
            FROM MES_BOM_View b
            WHERE b.BOMNO = @BOMNO
              AND b.IsActive = 1
            
            UNION ALL
            
            -- 第2~N层：基于MaterialCode展开
            SELECT 
                r.BOMNO,
                b.ParentMaterialCode,
                b.ChildMaterialCode,
                b.Quantity AS Quantity,  -- ⚠️ 单位用量（生产1个父件需要几个子件），不累乘！
                r.Level + 1 AS Level
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
            CreatedAt
        )
        SELECT 
            BOMNO,
            ParentMaterialCode,
            ChildMaterialCode,
            Quantity,
            Level,
            GETDATE()
        FROM BOM_Recursive
        OPTION (MAXRECURSION 10);
        
        -- 4. 更新状态为READY
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
```

**APS实时拉取逻辑（deprecated  v5.0.26 vNext 已改为 RequestDetailId 驱动，详见 2.2.1b）**：
```csharp
public async Task<bool> EnsureBOMExpanded(string bomno)
{
    // 1. 检查本地缓存
    if (await bomCache.Contains(bomno))
        return true;
    
    // 2. 检查ODS实时展开状态
    var request = await odsRepository.GetRealtimeBOMRequest(bomno);
    
    if (request == null)
    {
        // 3. 触发实时展开
        await odsRepository.RequestRealtimeBOMExpansion(bomno);
        
        // 4. 等待展开完成（最多5分钟）
        var timeout = DateTime.Now.AddMinutes(5);
        while (DateTime.Now < timeout)
        {
            await Task.Delay(5000);  // 每5秒检查一次
            
            request = await odsRepository.GetRealtimeBOMRequest(bomno);
            if (request?.Status == "READY")
                break;
            
            if (request?.Status == "FAILED")
                throw new Exception($"BOM展开失败: {request.ErrorMessage}");
        }
        
        if (request?.Status != "READY")
            throw new TimeoutException($"BOM展开超时: {bomno}");
    }
    
    // 5. 拉取展开结果到本地
    await PullRealtimeBOMResult(bomno);
    
    // 6. 更新本地缓存
    await bomCache.Add(bomno);
    
    return true;
}
```

---

### 2.7.8 管道供给链同步：sp_SyncPipelineSupply（v5.0.42 升级为完整骨架）

**定位**：并行于现货库存主链的独立步骤；`InventoryBalance` 定义不变  
**执行时机**：夜间全量排程前（与其他 `sp_Sync*` 同批次调度）  
**负责人**：2号位

#### **完整分层结构**

```text
ERP 源系统厂间在途数据
  → ODS 层：ERP_InterplantInTransit_View
    （所属库：MES_Integration；来源系统：ERP；5号位维护；14字段契约视图）
  → APS 层：ext_ERP_InterplantInTransit_View
    （所属库：APS_Production；单来源跨库包装视图；2号位维护；显式列字段）

ERP 采购在途 / VMI / 已到厂未入库（按来源契约逐步启用）
  → ODS 层：对应 Xxx_View（同构14字段契约，5号位新建）
  → APS 层：对应 ext_Xxx_View（同构跨库包装）

  ↓ 多来源 UNION ALL 收敛 ↓

  → APS 层：ext_PipelineSupply_Source_View
    （所属库：APS_Production；2号位维护；
     将 ext_ERP_InterplantInTransit_View +
          ext_ERP_PurchaseInTransit_View（V1 placeholder WHERE 1=0）+
          ext_ERP_VMI_View（V1 placeholder WHERE 1=0）+
          ext_ERP_ArrivedNotReceived_View（V1 placeholder WHERE 1=0）
     UNION ALL 为统一输入；
     未来新增来源只需追加 UNION ALL 分支，不修改变更主流程）

  → sp_SyncPipelineSupply（统一读取 ext_PipelineSupply_Source_View；
      未接入来源分支可返回0行，不得直接读取单来源物理表）
  → SupplyFact_Pipeline（按 BatchNo/DataCutoffTime 形成运行切片）
  → ScheduleContext.PipelineSupplies（准确切片；可以为空但非固定空跑）
```

#### **V1当前口径：统一主链、按来源启用（v1.36重写）**

> **核心原则**：管道主链已经是V1正式供给主链的一部分。某个来源尚未接入时，该来源契约视图可以返回0行；但 `sp_SyncPipelineSupply`、`SupplyFact_Pipeline` 和 `ScheduleContext.PipelineSupplies` 不得再被实现成“无条件TRUNCATE后固定空集合”。

**当前执行流程**：
```
sp_SyncPipelineSupply
    @BatchNo          = @BatchNo,
    @DataCutoffTime   = @DataCutoffTime,
    @RowsAffected     = @RowsAffected OUTPUT,
    @ErrorMessage     = @ErrorMessage OUTPUT

Step 0  校验 BatchNo、DataCutoffTime 和统一输入视图可访问性
Step 1  从 ext_PipelineSupply_Source_View 读取所有已启用来源，并按 DataCutoffTime 截断
Step 2  完成物料、工厂和来源业务键映射；无效记录登记Issue并排除
Step 3  按稳定来源业务键去重，计算 AvailableTime，写本次BatchNo临时结果
Step 4  在事务内替换本批次 SupplyFact_Pipeline 切片；不得TRUNCATE其他历史批次
Step 5  写 APS_ETL_Log SUCCESS/FAILED 和各来源行数
```

**来源启用边界**：
- 厂间在途、采购在途、VMI、已到厂未入库分别保持自己的ODS契约和业务键；统一视图只做字段标准化与 `UNION ALL`，不把不同业务事实粗暴合并。
- 已接入来源必须真实参与夜间和Candidate供给上下文；未接入来源允许0行并登记“未启用/空结果”，不阻断其他来源。
- `SupplyAvailabilityRule`仅在该来源需要资格裁决时启用；不得用一张万能规则表掩盖不同来源的硬业务约束。
- `InventoryBalance`定义不变；采购在途/VMI/厂间在途不得通过UPDATE库存来伪装成现货。

#### **统一真实同步契约与分阶段接入说明**

> 下列14字段契约、映射、去重和时间处理是V1统一管道主链的目标契约。具体来源尚未完成时，可保留同构0行分支；一旦启用，不得绕开统一输入视图或改变下游字段语义。

**⚠️ 契约锁定规则**：
ODS层 `ERP_InterplantInTransit_View` 字段结构为强契约。来源从0行占位切换为真实数据、或后续调整内部实现时，
允许调整视图内部的 SELECT 表达式、FROM、JOIN及WHERE实现逻辑；
对外投影的14个字段名称、顺序、数据类型及业务语义不得改变。

**ODS 契约视图字段（14 字段，最终契约）**：

| 字段名 | 类型 | 是否必须 | 说明 |
|--------|------|---------|------|
| MasterID | INT | 实际数据必须 | ERP物料主数据物理ID（→ MaterialMapping.SourceID → MaterialId） |
| MaterialCode | NVARCHAR(100) | 建议必须 | ERP物料编码（业务追溯；不替代 MasterID 权威映射） |
| SourceFactoryCode | NVARCHAR(50) | 可空 | 发出工厂编码（仅物流追溯） |
| FactoryCode | NVARCHAR(50) | 实际数据必须 | 目的工厂/收货工厂（→ SupplyFact_Pipeline.FactoryId） |
| SupplyType | NVARCHAR(50) | 必须 | 本视图固定为 INTERPLANT_IN_TRANSIT |
| OwnershipType | NVARCHAR(20) | 必须 | 厂间在途默认 OWNED |
| QualityStatus | NVARCHAR(20) | 必须 | 默认 AVAILABLE |
| Quantity | DECIMAL(18,4) | 必须 | 剩余在途数量（非原始发货数量） |
| ETA | DATETIME2 | 可空 | ERP原始预计到达时间；ODS不得加入APS提前期偏移 |
| StorageCode | NVARCHAR(50) | 可空 | 目的仓库编码 |
| SupplierCode | NVARCHAR(50) | 可空 | 厂间在途可为空；采购在途/VMI扩展预留 |
| SourceDocumentNo | NVARCHAR(100) | 建议必须 | ERP来源单据号 |
| SourceDocumentLineNo | NVARCHAR(50) | 可空 | ERP来源单据行号 |
| SourceUpdatedAt | DATETIME2 | 可空 | ERP来源更新时间（增量同步/新鲜度检查） |

**数据结构映射说明**：

| MAP | 说明 |
|-----|------|
| ODS.MasterID → SupplyFact_Pipeline.SourceMasterID → MaterialMapping.SourceID → MaterialId | 物料映射主链路；SourceMasterID 保留 ERP 直通追溯 |
| ODS.SourceFactoryCode → SupplyFact_Pipeline.SourceFactoryCode | 发出工厂追溯（非可用判定依据） |
| ODS.SourceDocumentLineNo → SupplyFact_Pipeline.SourceDocumentLineNo | ERP 来源明细行追溯 |
| ODS.SourceUpdatedAt → SupplyFact_Pipeline.SourceUpdatedAt | ERP 源记录最后更新时间 |

```
[V1当前目标实现] sp_SyncPipelineSupply 统一装载流程：

**契约锁定规则**：
ODS层视图字段结构为强契约。来源启用或内部实现调整时，
允许调整视图内部的SELECT表达式、FROM、JOIN、WHERE及必要转换逻辑；
对外 14 个字段的名称、顺序、数据类型和业务语义不得改变。
APS 包装视图继续显式透传 14 字段，不允许静默扩展。

**时间标准**：全链统一使用中国工厂本地时间（UTC+8）。
禁止混用 UTC 与本地时间。

Step 0  前置校验：
        IF @DataCutoffTime IS NULL RAISERROR 并 RETURN；
        IF ext_PipelineSupply_Source_View 不可访问 RAISERROR 并 RETURN

Step 1  读取统一输入视图（按 DataCutoffTime 截断）：
        SELECT 15列 INTO #RawPipelineSupply（14业务字段 + SourceSystem派生列）
        FROM ext_PipelineSupply_Source_View  -- 统一输入视图（15列），非直接读单来源
        WHERE (SourceUpdatedAt IS NULL OR SourceUpdatedAt <= @DataCutoffTime)
          AND Quantity > 0
          AND FactoryCode IS NOT NULL

        SourceUpdatedAt IS NULL → 保留兼容，登记 PIPELINE_SOURCE_TIME_MISSING（限50条）

Step 2  物理物料身份映射（复用现货库存ETL的多仓设计）：

        映射链：
          ODS.MasterID + ODS.StorageCode + Source='ERP' + IsCurrent=1
            → MaterialMapping.SourceID + Warehouse_Norm = ISNULL(v.StorageCode, 'N/A')
            → MaterialMapping.MaterialCode
            → Material.MaterialCode → Material.Id / Material.ProductFamilyId

        INNER JOIN MaterialMapping mm
            ON mm.SourceID       = v.MasterID
           AND mm.Source         = 'ERP'
           AND mm.IsCurrent      = 1
           AND mm.Warehouse_Norm = ISNULL(v.StorageCode, 'N/A')

        INNER JOIN Material mat
            ON mat.MaterialCode = mm.MaterialCode
           AND mat.IsActive    = 1

        【MaterialCode 口径】
        写入 SupplyFact_Pipeline.MaterialCode 时使用 mat.MaterialCode（APS 标准编码）
        ODS.MaterialCode 仅用于业务追溯和交叉校验
        v.MaterialCode != mat.MaterialCode → 登记 PIPELINE_MATERIALCODE_MISMATCH WARN

        【多仓映射唯一性验证（P0-4）】
        同一 MasterID + StorageCode 必须只命中 1 条 MaterialMapping。
        实现方式：在 #Raw 阶段按目标表同口径生成 SourceRowKey：
            CONCAT(SourceSystem, '|', SupplyType, '|',
                   ISNULL(SourceDocumentNo, ''), '|',
                   ISNULL(SourceDocumentLineNo, ''), '|',
                   ISNULL(CONVERT(NVARCHAR(20), MasterID), ''), '|',
                   ISNULL(StorageCode, ''), '|', FactoryCode)  AS SourceRowKey
        然后 COUNT(*) OVER (PARTITION BY SourceRowKey) AS MappingCount，
        仅 MappingCount=1 的行进入后续流程。
        命中 0 条 → 跳过，登记 PIPELINE_MATERIAL_MAPPING_NOT_FOUND
        命中 >=2 条 → 跳过，登记 PIPELINE_MATERIAL_MAPPING_AMBIGUOUS
        禁止 TOP 1 随机取。普通 INNER JOIN 会把多条映射展开为多余记录，禁止直接使用。

        【StorageCode 必填（P0-3）】
        SupplyType = INTERPLANT_IN_TRANSIT AND StorageCode IS NULL
          → 跳过，登记 PIPELINE_STORAGECODE_MISSING
        其他 SupplyType 按各自业务规则单独定义

Step 3  工厂映射（INNER JOIN，映射失败整行跳过）：
        INNER JOIN Factory f
            ON f.Code = v.FactoryCode AND f.IsActive = 1
        失败 → 登记 PIPELINE_FACTORY_NOT_FOUND → 跳过

Step 4  规则唯一胜出裁决（OUTER APPLY TOP 1 —— 禁止普通 JOIN）：
        OUTER APPLY (
            SELECT TOP(1) r.Id AS RuleId, r.IncludeFlag, r.LeadTimeOffset,
            r.Priority AS RulePriority
            FROM SupplyAvailabilityRule r
            WHERE r.IsActive = 1
              AND (r.EffectiveFrom IS NULL OR r.EffectiveFrom <= @DataCutoffTime)
              AND (r.EffectiveTo   IS NULL OR r.EffectiveTo   >  @DataCutoffTime)
              AND (r.ProductFamilyId IS NULL OR r.ProductFamilyId = mat.ProductFamilyId)
              AND (r.FactoryId       IS NULL OR r.FactoryId       = f.Id)
              AND (r.SupplyType      IS NULL OR r.SupplyType      = v.SupplyType)
              AND (r.OwnershipType   IS NULL OR r.OwnershipType   = v.OwnershipType)
              AND (r.QualityStatus   IS NULL OR r.QualityStatus   = v.QualityStatus)
            ORDER BY
                r.Priority ASC,
                (CASE WHEN r.ProductFamilyId IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN r.FactoryId       IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN r.SupplyType      IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN r.OwnershipType   IS NOT NULL THEN 1 ELSE 0 END
               + CASE WHEN r.QualityStatus   IS NOT NULL THEN 1 ELSE 0 END) DESC,
                r.Id ASC
        ) winner

        裁决结果：
          NULL（无命中） → 不写入，登记 NoRuleMatch
          IncludeFlag=0  → 不写入
          IncludeFlag=1  → 写入：AvailableTime = DATEADD(HOUR, LeadTimeOffset, ETA)

Step 5  写入 SupplyFact_Pipeline（事务原子批次重建）：
        SET XACT_ABORT ON;
        BEGIN TRY
            BEGIN TRANSACTION;

            DELETE FROM SupplyFact_Pipeline WHERE BatchNo = @BatchNo;

            INSERT INTO SupplyFact_Pipeline (
                MaterialCode, MaterialId, FactoryCode, FactoryId,
                ProductFamilyId, SupplyType, OwnershipType, QualityStatus,
                Quantity, ETA, AvailableTime,
                StorageCode, SupplierCode, SourceSystem, SourceDocumentNo,
                SourceMasterID, SourceFactoryCode, SourceDocumentLineNo, SourceUpdatedAt,
                SupplyAvailabilityRuleId, AppliedLeadTimeOffset,
                RulePriority, RuleEvaluatedAt,
                BatchNo, IsActive, SyncedAt
            )
            SELECT
                mat.MaterialCode,
                mat.Id,
                f.Code,
                f.Id,
                mat.ProductFamilyId,
                v.SupplyType,
                v.OwnershipType,
                v.QualityStatus,
                v.Quantity,
                v.ETA,
                DATEADD(HOUR, ISNULL(winner.LeadTimeOffset, 0), v.ETA)  AS AvailableTime,
                v.StorageCode,
                v.SupplierCode,
                v.SourceSystem,
                v.SourceDocumentNo,
                v.MasterID                          AS SourceMasterID,
                v.SourceFactoryCode,
                v.SourceDocumentLineNo,
                v.SourceUpdatedAt,
                winner.RuleId                       AS SupplyAvailabilityRuleId,
                winner.LeadTimeOffset               AS AppliedLeadTimeOffset,
                winner.RulePriority,
                @DataCutoffTime                     AS RuleEvaluatedAt,
                @BatchNo                            AS BatchNo,
                1                                   AS IsActive,
                GETDATE()                           AS SyncedAt
            FROM #Raw v
            INNER JOIN MaterialMapping mm
                ON mm.SourceID = v.MasterID
               AND mm.Source = 'ERP' AND mm.IsCurrent = 1
               AND mm.Warehouse_Norm = ISNULL(v.StorageCode, 'N/A')
            INNER JOIN Material mat
                ON mat.MaterialCode = mm.MaterialCode AND mat.IsActive = 1
            INNER JOIN Factory f
                ON f.Code = v.FactoryCode AND f.IsActive = 1
            OUTER APPLY (...) winner
            WHERE winner.RuleId IS NOT NULL AND winner.IncludeFlag = 1;

            SET @RowsAffected = @@ROWCOUNT;

            INSERT INTO APS_ETL_Log (...) VALUES (
                @BatchNo, 'sp_SyncPipelineSupply',
                CONCAT('管道供给同步完成: ', @RowsAffected, ' 行'),
                'SUCCESS', GETDATE()
            );

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            SET @ErrorMessage = ERROR_MESSAGE();
            INSERT INTO APS_ETL_Log (...) VALUES (
                @BatchNo, 'sp_SyncPipelineSupply',
                @ErrorMessage, 'FAILED', GETDATE()
            );
            THROW;  -- 技术失败必须向上传播
        END CATCH

        异常传播规则：
        - 技术失败（源不可访问/事务崩溃）→ FAILED + THROW → 调度器不得继续排程
        - 业务异常（单行映射失败/质量门禁）→ 逐行跳过 + WARN 日志（限50条）
        - 准确运行切片0行 = 正常；源不可访问、事务崩溃或错误清空造成的0行 <> 正常
```

**禁止实现清单**：
1. 禁止只按 MasterID 查 MaterialMapping（忽略 StorageCode 仓库维度）
2. 禁止 TOP 1 随机取多仓库映射
3. 禁止按 MaterialCode 对在途事实去重
4. 禁止普通 LEFT JOIN SupplyAvailabilityRule（可能产生重复供给）
5. 禁止使用 GETDATE() 判断规则有效期（必须用 @DataCutoffTime）
6. 禁止 FactoryId=NULL 写入 SupplyFact_Pipeline
7. 禁止 SourceUpdatedAt > @DataCutoffTime 进入快照
8. 禁止 DELETE 后 INSERT 失败留下空/半批次
9. 禁止 SP 失败后写 SUCCESS 日志
10. 禁止采购在途 UPDATE Inventory
11. 禁止修改 14 字段 ODS 契约结构

**ETA=NULL 策略**：
- AvailableTime 仍为 NULL
- 不得作为有确定时间的供给解除缺料
- 不得用于承诺订单交期
- 可进入"待确认管道供给"异常清单
- 默认不参与确定性排程供给扣减

**⚠️ 批次生命周期说明（v1.36当前口径）**：
> `SupplyFact_Pipeline`必须按BatchNo保留可追溯切片。同步过程使用事务内“写临时结果→校验→替换当前BatchNo切片”，不得全表TRUNCATE，不得因一个来源失败清空其他来源或历史批次。

**⚠️ 消费口径**：
> - **夜间全量**：读取 `WHERE BatchNo = @CurrentBatchNo AND IsActive = 1`。
> - **Candidate**：读取 `BasePlanVersion.SourceScheduleRun` 对应的准确 `BaseBatchNo` 切片；必要时只在Scope内叠加明确允许的新事实，不得全局重读当前在途覆盖Base。
> - `BatchNo IS NULL` 只保留历史兼容，不得作为正式消费路径。
> - 禁止只按 `IsActive=1` 查询，否则会混入多个历史批次。
> - 准确切片结果可以是0行；0行表示该运行无合格管道供给，不表示算法固定忽略管道供给。
---

### 2.8 运行编排框架：ScheduleRun / PlanVersion / Scenario / SimulationRun（v1.32 四表职责收敛）

#### 2.8.1 核心定位与边界

四表分工：

```text
ScheduleRun（排程运行编排表）
  = 记录这次运行怎么跑（运行状态/数据切片/触发来源）

PlanVersion（计划版本表）
  = 记录跑出来的某一 Domain 的结果版本；一次 FULL_SCHEDULE 为每个 DomainKey 各产出一条 Domain PlanVersion，全部经 SourceScheduleRunId 归属同一 ScheduleRun。V1 已废止"一个全局 PlanVersion 承载所有 Domain 结果"的单数概念。

Scenario（场景表）
  = 记录仿真/插单分析的假设、目标，以及最终选中的版本

SimulationRun（仿真运行表）
  = 记录某个场景下具体算法怎么执行
```

最终关系图：

```text
Scenario（场景表，可选）
    ↓
ScheduleRun（排程运行编排表，必有）
    ↓
SimulationRun（仿真运行表，仅算法仿真时有）
    ↓
PlanVersion（计划版本表，每个 DomainKey 一条，经 SourceScheduleRunId 归属 ScheduleRun）
    ↓
Task / Pegging / Summary / Explanation
```

反向来源关系：

```text
PlanVersion.SourceScheduleRunId
= 这套计划版本来自哪次排程运行

PlanVersion.SourceSimulationRunId
= 这套计划版本来自哪次仿真算法运行
```

选中与正式采用：

```text
Scenario.SelectedPlanVersionId = 场景最终选中哪套版本
PlanVersion.Status = ACTIVE     = 该版本正式采用（直接看 Status）
```

#### 2.8.2 RunType 枚举与触发路径

| RunType | 触发方 | 触发时机 | PlanVersion.VersionCategory | PlanVersion生命周期／结果持久化成功后的状态 | 能否自动激活 |
|---------|--------|----------|---------------------------|--------------------|-------------|
| `FULL_SCHEDULE` | Hangfire 定时器 | 凌晨全量 | DAILY_BASELINE | BUILDING→ACTIVE；失败为FAILED | 是（调度器自动）|
| `MANUAL_RESCHEDULE` | 用户/API | 人工主动触发 | RESCHEDULE_CANDIDATE | BUILDING→CANDIDATE；失败为FAILED | 否（须显式激活）|
| `LOCAL_RESCHEDULE` | 用户/API | 局部重排 | LOCAL_RESCHEDULE_CANDIDATE | BUILDING→CANDIDATE；失败为FAILED | 否（须显式激活）|
| `SIMULATION` | API（阶段二） | 仿真计算 | SIMULATION_CANDIDATE | 阶段二按仿真多方案生命周期定义 | 否（禁止自动激活）|
| `INSERT_ORDER_WHATIF` | API | 插单影响分析 | WHATIF_CANDIDATE | BUILDING→CANDIDATE；失败为FAILED | 否（禁止自动激活）|

> ⚠️ 核心红线：白天 Candidate 类 PlanVersion 创建时一律为 BUILDING；数据构造和结果持久化成功后转为 CANDIDATE；重试耗尽或确认不可恢复时转为 FAILED。
>
> 非 FULL_SCHEDULE 产出的 Candidate 版本不得自动激活。INSERT_ORDER_WHATIF 对应的 CTP 和 INSERT_IMPACT_ANALYSIS 结果永不得激活。正式采用仍以 `PlanVersion.Status = ACTIVE` 表示。

**v1.34 补充：白天 RunType + Purpose 合法组合（四类常用入口 + 单域较大范围人工重排）**（详见 §2.7）：

| 业务场景 | RunType | ScopeJson.Purpose | 激活规则 |
|---------|---------|-------------------|---------|
| CTP 承诺交期评估 | `INSERT_ORDER_WHATIF` | CTP | **不得激活** |
| 插单影响分析 | `INSERT_ORDER_WHATIF` | INSERT_IMPACT_ANALYSIS | **不得激活** |
| 插单局部重排 | `LOCAL_RESCHEDULE` | INSERT_RESCHEDULE | 审批后可激活 |
| 人工局部重排 | `LOCAL_RESCHEDULE` | MANUAL_ADJUSTMENT | 审批后可激活 |
| 单 Domain 较大范围人工重排 | `MANUAL_RESCHEDULE` | MANUAL_ADJUSTMENT | 审批后可激活 |

- `MANUAL_RESCHEDULE` 用于单Domain内较大范围、非局部的人工重排
- `Scenario` 仅业务适用时创建；`ScheduleRun.ScenarioId` 允许为空
- 五种 RunType+Purpose 合法组合均由 PMC/销售/计划员通过 4号位页面**人工发起**，不允许自动触发

> ⚠️ **V1 口径补充（多 Domain PlanVersion）**：上表 FULL_SCHEDULE 行描述的是每个 DomainKey 各自的 Domain PlanVersion 生命周期（BUILDING→ACTIVE）。一次全量按 `ScheduleRun.ExpectedDomainKeysJson` 为每个 Domain 各创建一条 PlanVersion，并通过 `SourceScheduleRunId` 关联同一 ScheduleRun（详见 §2.8.3 / §2.8.12 校验矩阵）。

#### 2.8.3 凌晨全量排程流程（V1 实装）

> ⚠️ **V1 口径**：一次 `FULL_SCHEDULE` 不再产出"一个全局 PlanVersion"，而是按启动时冻结的预期域集合（`ScheduleRun.ExpectedDomainKeysJson`，运行级不可变字段）为每个 `DomainKey` 各产出一条 **Domain PlanVersion**，全部经 `SourceScheduleRunId` 关联同一 `ScheduleRun`。各 Domain 在**自身事务内独立计算、落盘、发布**，废止旧 `ALL_OR_NOTHING` 全域一致发布。

```text
00:38 NightlyBatchOrchestrator 创建 ScheduleRun：
      RunType            = FULL_SCHEDULE
      Status             = RUNNING
      ScenarioId         = NULL
      BasePlanVersionId  = NULL
      ExpectedDomainKeysJson = 本次参与全量的 DomainKey 集合 JSON 数组（ScheduleRun 运行级不可变字段，启动时一次性冻结，✅ 不得按已建 PlanVersion 反推）
      DataCutoffTime           = 本次统一数据截止时间
      StrategyProfileVersionId = 默认 PUBLISHED 策略包版本
      TriggeredBy       = 'Hangfire'

02:00 排程启动，读取已创建的 ScheduleRun 与冻结的 ExpectedDomainKeysJson，
      为每个 DomainKey 独立创建并落盘 Domain PlanVersion（各 Domain 自身事务内）：
      SourceScheduleRunId   = ScheduleRun.Id
      SourceSimulationRunId = NULL
      DomainKey             = 该域标识
      VersionCategory       = DAILY_BASELINE
      Status                = BUILDING

Domain 独立发布（废止 ALL_OR_NOTHING 全域一致发布）：
  - 各 Domain 在自己的事务内独立计算、落盘、发布 ACTIVE；
  - 一个无关 Domain 失败，不得阻止其他已成功 Domain 发布；
  - V1 不再采用"全部成功才统一提交/统一发布"的 ALL_OR_NOTHING 全域一致发布。

BUILDING 有限重试与 FAILED 终态（V1 硬约束）：
  - 某域重试耗尽或确认不可恢复时，必须将该域 PlanVersion 转 FAILED 并写 ErrorMessage，不得无限期停留 BUILDING；
  - 运行级致命错误下，已创建但未完成的所有域 PlanVersion 统一标记 FAILED；
  - 不得让 ScheduleRun 永久停留 RUNNING（所有域进入终态后必须落 ScheduleRun 终态）。

终态判定（ScheduleRun.Status，含 PARTIAL_SUCCESS，详见 §2.8.7）：
  每个预期 Domain 完成后回填其 Domain PlanVersion 终态（ACTIVE / FAILED）；
  所有 ExpectedDomainKeysJson 均进入终态后，按 §2.8.7 规则写入 ScheduleRun 终态，
  所有终态（含 PARTIAL_SUCCESS）均写入 CompletedAt。
```

#### 2.8.4 多方案仿真流程（阶段二设计存档）

```text
1. 创建 Scenario（ScenarioType=SIMULATION，AssumptionJson/ObjectiveJson）
2. 创建 ScheduleRun（RunType=SIMULATION，ScenarioId=scenario.Id，BasePlanVersionId=当前ACTIVE）
3. 创建 SimulationRun（ScenarioId/ScheduleRunId，AlgorithmType/AlgorithmConfigJson，Status=RUNNING）
4. 算法产生 N 套候选 → 创建 N 条 PlanVersion：
   SourceScheduleRunId   = scheduleRun.Id
   SourceSimulationRunId = simulationRun.Id
   VersionCategory       = SIMULATION_CANDIDATE
   Status                = CANDIDATE
5. 业务选中方案 B：
   Scenario.SelectedPlanVersionId = planVersionB.Id
   Scenario.Status                = 'SELECTED'
6. 正式采用：
   PlanVersion B.Status       = ACTIVE
   PlanVersion B.ActivatedAt  = 当前时间
   PlanVersion B.ActivatedBy  = 当前用户
```

#### 2.8.5 落库职责分工（按场景区分 PlanVersion 创建负责人）

| 步骤 | 负责人 | 动作 |
|------|--------|------|
| ScheduleRun 创建（夜间 FULL_SCHEDULE） | NightlyBatchOrchestrator / 3号位（00:38 预创建） | 创建 ScheduleRun（RunType=FULL_SCHEDULE，Status=RUNNING，冻结 ExpectedDomainKeysJson） |
| ScheduleRun 创建（白天 Candidate） | 3号位（CreateRealtimeEvaluationRunAsync 内部） | 创建 ScheduleRun（RunType=LOCAL_RESCHEDULE/MANUAL_RESCHEDULE/INSERT_ORDER_WHATIF；BasePlanVersionId+StrategyProfileVersionId+ScopeJson+ExpectedDomainKeysJson+DataCutoffTime） |
| ScheduleRun 状态更新 | 2号位（运行收口） | Status/CompletedAt 回填（FULL_SCHEDULE 由步骤5.3 汇总；白天 Candidate 由运行收口置 COMPLETED/FAILED） |
| 夜间 FULL_SCHEDULE Domain PlanVersion 创建 | 2号位（排程启动时，按每个 DomainKey） | 创建 BUILDING 版本壳（SourceScheduleRunId=ScheduleRun.Id，DomainKey=当前域） |
| 白天 Candidate PlanVersion 创建 | 3号位（CreateRealtimeEvaluationRunAsync 内部） | 创建 BUILDING 壳（绑定 BasePlanVersionId/DomainKey/SourceScheduleRunId），不冗余存储 BasePlanVersionId |
| 白天 Candidate 数据构造与结果持久化 | 2号位 | Order 快照 / BOM / RemainingSupply / Task/Pegging 实例化 / 结果落盘，PlanVersion 转 CANDIDATE |
| PlanVersion 激活（夜间 FULL_SCHEDULE） | 2号位（调度器 Serializable 事务） | 同 DomainKey 旧 ACTIVE→ARCHIVED + 本次 BUILDING→ACTIVE（无需审批） |
| Candidate 审批与激活入口 | 3号位 | 审批编排 + 显式调用 ActivateCandidatePlanVersionAsync（同域替换 ACTIVE） |
| Scenario 创建 | 3号位/业务 | 仿真实场景（仅 INSERT_ORDER_WHATIF 通常创建） |
| SimulationRun 创建 | 算法引擎（阶段二） | 算法执行记录 |

**⚠️ 创建职责红线**：
- 不得再统一写成"所有 PlanVersion 都由 2号位创建"——夜间 FULL_SCHEDULE 的 Domain PlanVersion 由 2号位创建，白天 Candidate PlanVersion 由 3号位创建 BUILDING 壳。
- 1号位与 5号位只消费 `ScheduleContext` 或返回计算结果（ExplanationFactDraft / Recommendation），**不创建正式版本**。
- 3号位**不负责**夜间 Domain 结果持久化；2号位**不得替代** 3号位创建白天 Candidate 壳。

---

### 2.8.6 规则与参数引擎接入运行编排（v1.33 新增 2026-06-23）

**定位**：规则与参数引擎是 APS 业务策略中枢。通过 `RuleSetVersion`/`ParameterSetVersion`/`StrategyProfileVersion` 形成可发布、可追溯的策略包；`ScheduleRun` 在创建时绑定 `StrategyProfileVersionId`。

**核心关系**：
```
ScheduleRun.StrategyProfileVersionId
  → StrategyProfileVersion.Id
    → RuleSetVersionId → RuleSetVersion → RuleSet
    → ParameterSetVersionId → ParameterSetVersion → ParameterSet
```

**时序**（凌晨全量）：
- 00:38 前：`StrategyProfileVersion` 已发布完成
- 00:38：3号位创建 `ScheduleRun`，同步写入 `StrategyProfileVersionId`（默认策略包版本）
- 02:00：读取 `ScheduleRun.StrategyProfileVersionId` → 加载 `StrategyProfileVersion→RuleSetVersion+ParameterSetVersion` → 初始化 `ScheduleContext.RuleConfig/SchedulingParams`
- 1号位只消费 `ScheduleContext` 中已装载的规则参数结果

**红线**：
- `StrategyProfileVersionId` 必须在创建 `ScheduleRun` 时同步写入
- 已发布的规则集/参数集/策略包版本不可原地修改
- 1号位禁止直接读取规则参数维护表
- 5号位只执行规则，不维护规则
- V1 不新增 `ScheduleConfigSnapshot`

**各运行类型的策略包来源**：
| RunType | 策略包来源 |
|---------|-----------|
| FULL_SCHEDULE | 系统默认 PUBLISHED 策略包 |
| MANUAL_RESCHEDULE | 默认继承 BasePlanVersion→SourceScheduleRun→StrategyProfileVersionId；允许用户选择 |
| LOCAL_RESCHEDULE | 同 MANUAL，通过 ScopeJson 限定范围 |
| SIMULATION | Scenario 指定或继承基准版本 |
| INSERT_ORDER_WHATIF | 默认当前 ACTIVE PlanVersion 对应策略包 |

#### 2.8.6a V1策略接口、普通计算与Voucher最小边界（v1.36新增）

**V1不建设动态插件平台**：不扫描插件目录、不支持运行中装卸程序集、不为每条规则建立插件注册表。所谓“插件”仅表示具有稳定 .NET 接口、由依赖注入在运行启动时选择实现的少数策略模块。

**仅保留的核心策略扩展点**：
- `DemandPriorityPolicy`：需求优先级与层级内排序；
- `SupplyEligibilityPolicy`：资格、候选范围与HardLock命中；
- `SupplyRankingPolicy`：合格供给排序；
- `ProductionInstructionPositionPolicy`：PI总量拆成Stage/XC/在途/UNLOCATED位置；
- `PartialShareBindingPolicy`：已分配PI份额绑定位置切片；
- `ShortageHandlingPolicy`：正式PI不足、采购不足与虚拟占位处理。

**返回对象分三类**：
1. 普通领域计算：返回只读 `*Result`，例如PositionCalculationResult、PriorityResult、ShortageResult；
2. 供需分配判断：返回 `PeggingAllocationDecision(DemandKey, SupplyKey, Qty, AllocationMode, Reason)`，由2号位验证余额后执行；
3. 正式审批或状态变化：才使用Voucher，例如ManualFreeze、ToleranceClosure、HardLock人工创建/解除。

**红线**：
- 规则参数的日常变化优先配置在RuleSet/ParameterSet，不新增代码插件；
- 5号位不得直接UPDATE余额、写Ledger、写最终Task或物理Pegging；
- Task合并/拆分属于1号位有限产能机制，不属于5号位插件；
- 一个Domain运行过程中策略实现和策略版本不得变化。

---

#### 2.8.7 ScheduleRun.Status 终态定义（含 PARTIAL_SUCCESS，V1 实装）

`ScheduleRun.Status` 值域（V1）：`RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED`

| 状态 | 含义 |
|------|------|
| `RUNNING` | 仍有预期 Domain（`ExpectedDomainKeysJson`）未进入终态 |
| `COMPLETED` | 所有 `ExpectedDomainKeysJson` 对应 Domain 均进入**运行成功终态**（夜间 `FULL_SCHEDULE` = 对应 PlanVersion 已 `ACTIVE`；白天 Candidate = 对应 PlanVersion 已 `CANDIDATE`） |
| `PARTIAL_SUCCESS` | 部分预期 Domain 成功、部分失败或缺失（仅用于多预期 Domain 运行） |
| `FAILED` | 致命错误，或零成功 Domain |

**⚠️ RunType 维度状态矩阵（V1 权威）**：`COMPLETED` 的"运行成功终态"按 `RunType` 区分，白天 Candidate 的成功态是 `CANDIDATE` 而非 `ACTIVE`：

| RunType | 计算成功时 PlanVersion | ScheduleRun 成功终点 | 是否可能 PARTIAL_SUCCESS |
|---------|----------------------|---------------------|------------------------|
| `FULL_SCHEDULE`（夜间） | `ACTIVE` | 所有预期域发布后 `COMPLETED` | 是 |
| `LOCAL_RESCHEDULE` | `CANDIDATE` | Candidate 落盘后 `COMPLETED` | 否 |
| `MANUAL_RESCHEDULE` | `CANDIDATE` | Candidate 落盘后 `COMPLETED` | 否 |
| `INSERT_ORDER_WHATIF` | `CANDIDATE` | 评估结果落盘后 `COMPLETED` | 否 |
| `SIMULATION`（阶段二） | `CANDIDATE` | 仿真结果落盘后 `COMPLETED` | 按阶段二另定义 |

> 白天 Candidate 激活发生在 `ScheduleRun` 已完成（`COMPLETED`）之后，激活不反向改变 `ScheduleRun` 状态。

- `CompletedAt`：**所有终态（含 `PARTIAL_SUCCESS`）均写入**。
- **⚠️ `PARTIAL_SUCCESS` 适用范围**：本状态**主要用于多预期 Domain 的 `FULL_SCHEDULE` 运行**（多域中部分成功部分失败/缺失）。白天单 Domain Candidate 运行（`ExpectedDomainKeysJson` 恰好 1 项）正常状态机只有 `COMPLETED` / `FAILED`，**不产生 `PARTIAL_SUCCESS`**；其正常计算终点是 `PlanVersion.Status=CANDIDATE`（而非 `ACTIVE`），运行收口见核心走查第八部分场景4。

#### 2.8.8 ExpectedDomainKeysJson 冻结（V1 实装）

- 运行启动时冻结预期域集合（`ScheduleRun.ExpectedDomainKeysJson`，**独立运行级不可变字段**），作为终态判定的唯一权威来源。
- ✅ **不得按已建 PlanVersion 反推**预期域集合；终态判定严格以启动时冻结集合为准。
- 🔒 **不可变规则（创建时一次性冻结）**：
  - 创建 `ScheduleRun` 时一次性写入 `ExpectedDomainKeysJson`，运行过程**禁止追加 / 删除** Domain；
  - **不得**因某 Domain 启动失败而从预期集合移除该 Domain；
  - **不得**按实际已创建 PlanVersion 反推预期集合；
  - 不需数据库 Trigger，由 **3号位创建服务** 与 **2号位运行服务** 共同校验冻结集合的完整性。
- 📌 **ScopeJson 固定 11 字段（不得新增 ExpectedDomainKeys）**：`ScopeJson` 继续固定其既有 **11 字段**契约（权威定义见《APS_集成接口设计_v1.12》v1.28）；`ExpectedDomainKeys` 不再位于 `ScopeJson` 中，改由 `ScheduleRun.ExpectedDomainKeysJson` 承载。白天 Candidate 场景**不得为保存 DomainKey 而修改 11 字段 ScopeJson**。

#### 2.8.9 白天 Candidate 严格单 Domain 与跨域拆分（V1 实装）

- 白天 Candidate 场景（`MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` / `INSERT_ORDER_WHATIF`）**严格限定单 Domain**：一个 Candidate / ScheduleRun 只承载一个 `DomainKey`。
- **`ExpectedDomainKeysJson` 校验（恰好 1 个元素）**：其唯一元素必须同时满足 `BasePlanVersion.DomainKey = CandidatePlanVersion.DomainKey = ExpectedDomainKeysJson[0]`。
- **`ScopeJson` 继续固定 11 字段并按 RunType+Purpose 校验**：白天 Candidate 的 `ScopeJson` 仍按既定 11 字段契约与 RunType+Purpose 规则校验；**不得为保存 DomainKey 修改 11 字段 ScopeJson**（DomainKey 由 `ExpectedDomainKeysJson` 单项承载）。
- 若 PMC 选择多个**跨域** Domain 重算，由后台按 `Domain_Dependency` 依赖顺序**拆成多个单域重排**，分别创建独立的 `ScheduleRun` + 单域 Candidate，逐个执行。
- V1 **不建跨域多 Domain 统一 Candidate**。

#### 2.8.10 跨域失败后处理（人工重算，V1 实装）

- 跨域依赖不一致导致部分 Domain 版本不匹配时，产生 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` **原因事实**（`ObjectType=DOMAIN`）+ `RescheduleRecommendation`。
- 由 **PMC / 0号位人工选择**相关 Domain 重算；不在凌晨全量中自动处理。
- V1 **不自动回滚**已成功上游、**不建跨域多 Domain Candidate**、**不建原子激活组**。

#### 2.8.11 V1 暂停闭环（不实现，V2 预留）与 Task.Status 正式值域

- **V1 不实现任务暂停/恢复正式流程**：`任务暂停/恢复 PAUSE→PAUSED` / `RESUME→RUNNING`、`TaskPauseVoucher` / `TaskResumeVoucher` 正式流程均改为 **"V1 不实现，V2 预留"**，不落地。文档中任何"任务暂停/恢复"闭环描述均视为 V1 不适用。
- **Task.Status 正式值域**：`PLANNED / RELEASED / IN_PROGRESS / COMPLETED / CANCELLED`。
- ✅ 正式值域**不含** `PAUSED / SUSPENDED / WAITING / PENDING / RUNNING`。

---

#### 2.8.12 运行类型校验矩阵（ExpectedDomainKeysJson / ScopeJson，V1 实装）

| RunType | `ExpectedDomainKeysJson` 约束 | `ScopeJson` | `Purpose` |
|---------|------------------------------|-------------|-----------|
| `FULL_SCHEDULE` | 必须非空 JSON 数组（≥1 个**不重复** DomainKey）；创建时一次性冻结、运行级不可变 | 可为 NULL | 不要求填写 |
| 白天 Candidate（`LOCAL_RESCHEDULE` / `MANUAL_RESCHEDULE` / `INSERT_ORDER_WHATIF`） | **恰好 1 个元素**，且 = `BasePlanVersion.DomainKey` = `CandidatePlanVersion.DomainKey` | 继续固定 **11 字段**，按 RunType+Purpose 校验；**不得为保存 DomainKey 修改 11 字段** | 按 RunType 要求 |
| `SIMULATION`（阶段二骨架） | 可用独立 `ExpectedDomainKeysJson`（1 或多个 DomainKey） | **不进入 `ScopeJson`** | — |

- `FULL_SCHEDULE` 创建 `ScheduleRun` 时：`ExpectedDomainKeysJson` 必填且为合法非空数组；`ScopeJson` 允许为 NULL；`Purpose` 不作强制。
- 白天 Candidate：`ExpectedDomainKeysJson` 单元素必须与 Base/Candidate PlanVersion 的 `DomainKey` 三者一致；`ScopeJson` 的 11 字段仅用于 RunType+Purpose 范围限定，不得借机写入 DomainKey。
- `SIMULATION`：`ExpectedDomainKeysJson` 独立于 `ScopeJson` 提供，阶段二骨架可用，不污染 11 字段 `ScopeJson`。

---

#### 2.8.13 ReasonCode 字典（权威列表，V1 实装）

**权威 ReasonCode 列表（V1 唯一口径）**：

```text
RESOURCE_CAPACITY_WAIT
MATERIAL_SHORTAGE
PRECEDENCE_WAIT
FROZEN_ZONE_LOCK
ROUTING_FALLBACK
STAGE_LEADTIME_FALLBACK
BOM_DEGRADE
CROSS_ORG_HANDOFF
PRIORITY_LOWER_THAN_OTHERS
DUE_DATE_RISK
LOGISTICS_DELAY
PRIORITY_INHERITANCE
CROSS_DOMAIN_VERSION_MISMATCH_RISK
MANUAL_COMPLETED_SHORT
EQUIPMENT_BREAKDOWN_RISK
```

**本次字典统一说明**：
- `DUE_DATE_VIOLATION` → 统一更名为 **`DUE_DATE_RISK`**（旧码 `DUE_DATE_VIOLATION` 废止，不得再出现）。
- 删除未登记示例 **`DUE_DATE_TIGHT`**、**`UPSTREAM_DELAY`**：上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`（`ObjectType=DOMAIN`）。
- 上表 15 项为 V1 权威全集，任何原因事实 / Explanation 的 `ReasonCode` 必须取自该集合。

---

### 2.9 MES生产进度汇总防腐链路（v1.30 新增，对齐 DDL v5.0.41）

#### 2.9.1 定位与设计决策

**链路定位**：MES生产进度是生产指示位置计算和现实执行恢复的权威事实输入。`StageProgressSnapshot`用于整张PI的大工艺累计位置互斥化，`OperationProgressSnapshot`仅用于当前Stage内部小工序裁剪与诊断，`MESWorkOrderSnapshot`用于识别现实MES工单和更新ExecutionLock。此链路与现货库存链并行，**绝不混入 `InventoryBalance`，也不得直接改变PI总量边界**。

**V1 核心设计决策（写死）**：
- ⚠️ **不接 MES 每条报工明细**：只接 ODS 汇总后的工单级、工序级和大工艺级进度，普通进度更新当前快照/ExecutionLock状态，不做完整事件溯源。
- ⚠️ **PI总量与位置分离**：可生产量仅为 `max(0, Order.Quantity-Order.ReceivedQty)`；MES累计完成量只用于判断这批剩余量当前位于哪个Stage，不得再次从总量扣减。
- ⚠️ **StageProgress权威**：大工艺累计进度异常时先做单调性修复；下游超过上游则下修下游，中间Stage缺失则采用下游最小证明量。修复前后值均写PI位置快照诊断字段/Issue。
- ⚠️ **工序识别主字段 = `OperationName`**：OperationProgress仅在已确定CurrentStage后做Stage内部裁剪，不得反过来推翻Stage位置权威。
- ⚠️ **Task/Pegging重建、执行事实延续**：Task和物理Pegging随PlanVersion重新生成，不匹配/复用历史TaskId；现实MES工单通过ExecutionLock跨版本延续，新Task用 `ExecutionLockId` 关联且不得重复下发。
- ⚠️ **EAM V1 预留**：`EAM_APS_Resource_View`在ODS层预留占位，V1不读取EAM设备故障数据，不生成资源不可用窗口。

#### 2.9.2 ODS 契约视图（Socket，5号位收口）

MES 报工数据分散在**加工**和**组装**等不同大工艺数据库/业务表中。ODS 层采用 **"各大工艺标准化子视图 + UNION ALL 统一契约视图"** 分层结构，由 **5号位** 负责建立和维护所有统一收口视图。

**UNION ALL 结构示例（以工序进度视图为例）**：

```sql
-- MES_APS_OperationProgress_View（5号位统一收口，加工+组装 UNION ALL 合并）
-- 各子视图字段列表完全一致，APS 消费方无需区分报工来源大工艺类型
CREATE VIEW [MES_Integration].[dbo].[MES_APS_OperationProgress_View] AS
SELECT
    ProductionInstructionNo,   -- 生产指示号（关联 APS Order_Canonical）
    MESWorkOrderNo,            -- MES 工单号
    MaterialCode,              -- 物料编码
    OperationName,             -- ⚠️ 工序名称（V1 主匹配字段；不以 MES 工序编码为准）
    StageCode,                 -- APS 大工艺阶段码（格式 {工厂}_{类别}）
    StageName,                 -- 大工艺中文名称（冗余展示）
    PlannedQty,                -- 工序计划数量
    GoodQty,                   -- 累计良品完成数量（截至报工）
    ScrapQty,                  -- 累计报废数量（来源无此字段时 NULL）
    ReworkQty,                 -- 累计返工数量（来源无此字段时 NULL）
    LastReportTime,            -- 最后报工时间
    SourceUpdatedAt            -- MES 汇总最后更新时间（DataCutoffTime 截断依据）
FROM MES_APS_OperationProgress_Mach_View   -- 加工大工艺子视图（2号位建立）
UNION ALL
SELECT
    ProductionInstructionNo, MESWorkOrderNo, MaterialCode, OperationName,
    StageCode, StageName, PlannedQty, GoodQty, ScrapQty, ReworkQty,
    LastReportTime, SourceUpdatedAt
FROM MES_APS_OperationProgress_Assy_View   -- 组装大工艺子视图（5号位建立）
-- UNION ALL 可继续扩展其他大工艺子视图，字段列表必须与上方完全对齐
```

**三条 ODS 统一契约视图字段清单（字段顺序与类型不可随意修改）**：

| 视图 | 字段（完整列表） | 汇总颗粒度 |
|------|----------------|-----------|
| `MES_APS_WorkOrder_View` | ReleaseItemKey(APS新发布工单必填，历史可NULL) / ProductionInstructionNo / MESWorkOrderNo / MaterialCode / PlannedQty / WorkOrderStatus / SourceUpdatedAt | ReleaseItemKey（APS新发布）或生产指示号 + MES工单号 + 物料编码 |
| `MES_APS_OperationProgress_View` | ProductionInstructionNo / MESWorkOrderNo / MaterialCode / **OperationName** / StageCode / StageName / PlannedQty / GoodQty / ScrapQty(可NULL) / ReworkQty(可NULL) / LastReportTime / SourceUpdatedAt | 生产指示号 + MES工单号 + 物料编码 + 工序名称 + 大工艺阶段码 |
| `MES_APS_StageProgress_View` | ProductionInstructionNo / MaterialCode / StageCode / StageName / PlannedQty / GoodCompletedQty / ScrapQty(可NULL) / ReworkQty(可NULL) / LastReportTime / SourceUpdatedAt | 生产指示号 + 物料编码 + 大工艺阶段码 |

子视图分工：**加工类**大工艺子视图由 **2号位** 建立（含 `_Mach_` 等前缀）；**组装类**大工艺子视图由 **5号位** 建立（含 `_Assy_` 等前缀）；所有子视图 UNION ALL 统一收口由 **5号位** 负责。

> ⚠️ **契约红线**：三条统一视图字段列表变更必须走审批；字段含义由 ODS 层统一定义，下游 APS 快照直接映射，不允许 APS 侧改变字段语义；三条视图的 UNION ALL 逻辑仅由 5号位维护，2号位不直接修改视图 DDL。

**增量/实时插单场景的追踪逻辑**：
- **全量夜间**（00:40/00:45/00:50）：SP 以 `@ScheduleRunId` 为分区键，全量删除-重插；三张快照表无 BatchNo 列，BatchNo 追踪保留在 BOM Workset 侧（`OrderBomRequestLink.BatchNo`）；
- **实时插单（阶段二）**：触发 `ScheduleRun(RunType=INSERT_ORDER_WHATIF)` 时，新 `ScheduleRunId` 绑定对应 `PlanVersionId`，MES 快照以 `ScheduleRunId` 独立存储，不影响同天夜间主快照；进度快照链路通过 `ScheduleRunId` 追踪，无需 BatchNo。

#### 2.9.3 APS 本地快照（Plug，2号位实现）

**三张 APS 本地快照表（DDL v5.0.41 §2.9）**：

| 快照表 | 颗粒度 | 计算列 | 分区键 |
|--------|--------|--------|--------|
| `MESWorkOrderSnapshot` | ReleaseItemKey(可NULL) + 生产指示号 + MES工单号 + 物料编码 | — | `ScheduleRunId` |
| `OperationProgressSnapshot` | 生产指示号 + MES工单号 + 物料编码 + 工序名称 + 大工艺阶段码 | `RemainingQty` PERSISTED：`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END` | `ScheduleRunId` |
| `StageProgressSnapshot` | 生产指示号 + 物料编码 + 大工艺阶段码 | `RemainingQty` PERSISTED：`CASE WHEN PlannedQty - ISNULL(GoodCompletedQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodCompletedQty,0) END` | `ScheduleRunId` |

**三个同步 SP（DDL v5.0.41 §2.9）**：

```
sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime):
  Step 1  DELETE FROM MESWorkOrderSnapshot WHERE ScheduleRunId = @ScheduleRunId
  Step 2  INSERT ... SELECT FROM [MES_Integration].[dbo].[MES_APS_WorkOrder_View]
  Step 3  写 APS_ETL_Log（SUCCESS / FAILED）

sp_SyncOperationProgressSnapshot(@ScheduleRunId, @DataCutoffTime):
  Step 1  DELETE FROM OperationProgressSnapshot WHERE ScheduleRunId = @ScheduleRunId
  Step 2  INSERT ... SELECT FROM [MES_Integration].[dbo].[MES_APS_OperationProgress_View]
  Step 3  写 APS_ETL_Log（SUCCESS / FAILED）
  -- RemainingQty 为持久化计算列，无需 SP 显式计算

sp_SyncStageProgressSnapshot(@ScheduleRunId, @DataCutoffTime):
  Step 1  DELETE FROM StageProgressSnapshot WHERE ScheduleRunId = @ScheduleRunId
  Step 2  INSERT ... SELECT FROM [MES_Integration].[dbo].[MES_APS_StageProgress_View]
  Step 3  写 APS_ETL_Log（SUCCESS / FAILED）
  -- RemainingQty 为持久化计算列，无需 SP 显式计算
```

**执行时机**：`ScheduleRun` 必须在 00:40 前创建（最晚 00:38，由 `INightlyBatchOrchestrator` 在 LLC 计算完成后创建），只用于提供 `ScheduleRunId` 和 `DataCutoffTime`。三个快照同步 SP 由三个独立Hangfire定时任务依序执行：00:40（MES工单快照）→ 00:45（工序进度快照）→ 00:50（大工艺进度快照）；`INightlyBatchOrchestrator`不直接调用它们。02:00的`ISchedulingOrchestrator`只校验、装载并消费已完成快照。

**DataCutoffTime 截断规则**：
- `ScheduleRun.DataCutoffTime` 是本次排程输入数据的统一切片边界；三个快照同步 SP 必须使用同一 `@DataCutoffTime`（**禁止**各 SP 各自取当前时间）。
- `sp_SyncMESWorkOrderSnapshot`：按 `SourceUpdatedAt IS NULL OR SourceUpdatedAt <= @DataCutoffTime` 过滤。
- `sp_SyncOperationProgressSnapshot` / `sp_SyncStageProgressSnapshot`：按 `COALESCE(SourceUpdatedAt, LastReportTime) IS NULL OR COALESCE(...) <= @DataCutoffTime` 过滤。
- 三类快照同步的技术失败必须记录FAILED并向上`THROW`；只有契约视图在有效工作集下真实返回0行才允许形成空快照，禁止把连接失败、列缺失或事务异常伪装成0行成功。
- 如某个 ODS 视图暂无法提供来源时间（字段恒为 NULL），V1 允许透传，但必须在 `APS_ETL_Log` 登记说明；后续由 5号位补充 `SourceUpdatedAt`/`LastReportTime` 字段。

**ScheduleRun 创建时序（v3.12 修正）**：`ScheduleRun` 必须在夜间数据准备阶段（最晚 00:38 前）由 NightlyBatchOrchestrator 创建；02:00 排程启动时不再新建，而是读取已创建记录初始化 ScheduleContext 并生成或绑定 PlanVersion。

#### 2.9.4 ScheduleContext 装载与消费

```
APS.MESWorkOrderSnapshot     → ScheduleContext.MESWorkOrderSnapshots（可选）
APS.OperationProgressSnapshot → ScheduleContext.OperationProgressSnapshots
APS.StageProgressSnapshot    → ScheduleContext.StageProgressSnapshots
```

**消费规则（v1.36重写）**：
1. **2号位/5号位位置模块**先按 `ProductionInstructionNo + MaterialCode + StageCode` 消费 `StageProgressSnapshot`，生成互斥 `ProductionInstructionPositionSlice`；1号位不直接根据累计量自行推算PI位置。
2. **2号位**读取并装载`OperationProgressSnapshot`，完成已定位TaskDraft的Stage内部小工序裁剪后作为内存请求传给1号位；1号位只消费裁剪后的TaskDraft，不直接查询快照表，也不得重复计算PI总量。
3. APS新发布工单优先按`ReleaseItemKey`匹配MESPlanRelease，再以`MESWorkOrderNo`创建ExecutionLock；历史/外部工单可按`ProductionInstructionNo + MESWorkOrderNo`识别。新版本Task不得依据旧TaskId匹配。
4. 所有正式或虚拟Task必须透传 `ProductionInstructionNo`；无正式PI虚拟Task可为空但必须带来源需求和 `IsVirtual=1`，且绝对禁止MES下发。

---


#### 2.9.5 MESPlanRelease双向视图发布边界（v1.38新增）

APS不把动态`Task WHERE PlanVersion=ACTIVE`直接暴露给MES，而是由2号位在版本激活后固化稳定发布单元：

```text
ACTIVE Task
→ 按同PI+同Stage+同执行批次组成MESPlanRelease
→ 生成不可变ReleaseItemKey
→ APS_MES_PlanRelease_View
→ MES幂等建单并回传ReleaseItemKey
→ MESWorkOrderSnapshot识别
→ MESPlanRelease=CONSUMED + ExecutionLock + Task.RELEASED
```

**边界规则**：
- 一条MESPlanRelease对应一张未来MES现实工单，可以关联同PI、同Stage下多个小工序Task；不得跨PI/Stage。
- PUBLISHED时Task仍为PLANNED；MES现实工单被APS确认后才转RELEASED。
- PUBLISHED但未承接的发布数量属于跨版本物理承诺，不得回到普通SOFT竞争池；新版本Task继续关联原MESPlanReleaseId。
- TaskNo仅展示诊断；ReleaseItemKey是跨系统幂等和回传键。
- 建单前取消将发布记录转CANCELLED；MES已经建单后，必须由MES现实取消流程反映到实时工单视图，APS不得靠隐藏发布记录释放执行量。

---

### 2.10 跨厂Pegging防腐层补强（v1.33 新增 2026-06-23）

#### 2.10.1 ERPProperty 与 M库判定

`ProcessCodeDict.ERPProperty` 来自 ERP 真实属性，5号位同步透出维护，不根据 WarehouseRole/ProcessName 推导。2号位通过 `MES_ProcessCode_View.ERPProperty` 消费，结合 `MaterialSupplyContext` 生成内存索引 `MaterialCode+FactoryCode→HasMStock`。

| ERPProperty | 中文 | HasMStock | Pegging 含义 |
|---|---|---|---|
| M | M库 | 1 | 该物料在该工厂处于完成可用状态 |
| XC | 现场/大工艺输入库存 | 0 | 需要排该道大工艺 |
| ZP | 制品出口库 | 0 | 不能通用，只能按出荷指示号匹配 |
| BP | 部品出口库 | 0 | 不能通用，只能按出荷指示号匹配 |

#### 2.10.2 ERP_Received_ByDocument_View

ERP Received 表数据量大，V1 不搬明细、不保留行号。5号位建立 ODS 汇总视图 `ERP_Received_ByDocument_View`，粒度=工厂+仓库+物料+单据类型+单据号。2号位通过 `ext_ERP_Received_ByDocument_View` 读取，V1 不建本地快照表。

**业务假设**：出荷指示号未完成时，`ReceivedQty` 默认视为尚未被使用的可供给数量。不要求 ERP 提供当前剩余量。

#### 2.10.3 MES_APS_BOM_Workset_CrossFactoryEdge

5号位基于 StageDetail 按 StageSeq 排序生成，只记录 `FromFactoryCode <> ToFactoryCode` 的跨厂段。该表只表示结构事实，不判断跨厂模式。

**生成逻辑**（`sp_GenerateBOMCrossFactoryEdge(@BatchNo)`）：
1. 调用时机：`sp_EnrichBOMWorkset(@BatchNo)` 完成 StageDetail 写入后调用
2. 输入：`MES_APS_BOM_Workset_StageDetail`（仅 `StageScopeType='EDGE'`）
3. 排序：按 `WorksetId + StageSeq`，用窗口函数 `LEAD` 取相邻阶段
4. **FactoryCode 来源（P0）**：`StageCode → StageDict.StageCode → StageDict.FactoryCode`。**禁止截取 StageCode 前缀**作为正式实现；StageCode前缀仅可辅助校验
5. 插入条件：`FromFactoryCode <> ToFactoryCode`，两侧 StageDict 均命中
6. 异常：StageCode 未命中 StageDict → 不生成该边，登记 Issues/WARN
7. 输出：`MES_APS_BOM_Workset_CrossFactoryEdge`（结构事实，不判模式、不生成Task、不扣库存）

2号位在 00:30 搬运到 `APS_BOM_CROSS_FACTORY_EDGE_RAW`，供Pegging读取后，由5号位的资格/模式策略返回只读裁决结果，2号位组装上下文并执行扣减；不建设动态插件加载。

#### 2.10.3b MES_APS_BOM_Workset_CrossFactoryEdge_Realtime（v1.34 新增）

**生成 SP**：`sp_GenerateBOMCrossFactoryEdgeRealtime(@BOMNO, @RequestDetailId)`

**生成逻辑**：
1. **调用时机**：由 `sp_EnrichBOMWorksetRealtime` 在 Step 5 日志之前调用（有 RequestDetailId 时）
2. **数据源**：`MES_APS_BOM_Workset_StageDetail_Realtime`（仅 `StageScopeType='EDGE'`）通过 `WorksetId` 连接 `MES_APS_BOM_Workset_Realtime`
3. **隔离键**：按 `RequestDetailId` 严格隔离；SP 先删除该 RequestDetailId 旧结果，保证幂等
4. **FactoryCode 来源（P0）**：`StageCode → StageDict.StageCode → StageDict.FactoryCode`（`IsActive=1`）；**禁止截取 StageCode 前缀**推导工厂
5. **插入条件**：`FromFactoryCode <> ToFactoryCode`
6. **ToProcessCode**：V1 当前固定 NULL；预留字段供 V2 扩展
7. **异常处理**：StageCode 未命中 StageDict → 不生成该边，登记 `MES_APS_BOM_Workset_Issues`（`IssueType=STAGE_DICT_NOT_FOUND`, `Severity=WARN`, `DegradeAction=CROSS_FACTORY_EDGE_SKIP`, `BatchNo=RT:RD:{RequestDetailId}`）
8. **输出**：`MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`（3 个索引：`IX_CrossFactoryEdge_RT_RequestDetail` / `IX_CrossFactoryEdge_RT_Workset` / `IX_CrossFactoryEdge_RT_BOMNO`）

**红线**：
- **5号位**：只生成结构事实
- **2号位**：搬运至 APS 层作为 Pegging 输入，在 Pegging 上下文中判定跨厂模式（STAGE_HANDOFF / INTER_FACTORY_ORDER）
- 表内**无 BatchNo**；`RequestDetailId` 是唯一隔离键
- **0 行合法**：表内为 0 行不阻断 READY
- 不生成 Task；不扣库存；不写 Pegging

#### 2.10.4 现有代码对象、统一Ledger与SupplyBusinessKey分层（v1.41）

| 对象 | 防腐定位 | 实现边界 |
|---|---|---|
| `PeggingRuleVoucher` | 5号位只读分配判断 | 可保留现有名，等同AllocationDecision |
| `PeggingLedgerEntry` | 成功双边扣减后的内存记录 | 继续复用；包含AllocationSequence、稳定供给键和最终Task映射所需信息 |
| `PeggingAllocationLedger` | 数据库统一分配总账 | 最终Domain事务批量写入，不在Pegging循环逐行写库 |
| `PeggingSupplyAllocation` | 非Task现实供给结果投影 | 从Ledger生成并通过LedgerId关联，不是第二套总账 |
| 物理`Pegging` | Task-to-Task血缘 | 最终Task形成后批量写入 |
| `ExecutionLock` | 跨版本MES现实执行身份 | 只实现DDL最小实体，不建设Link表或事件平台 |

**SupplyBusinessKey由来源Loader生成**：

```text
PI|ProductionInstructionNo
INV|ERP|FactoryCode|WarehouseCode|MaterialCode
DOC|ERP|DocumentType|DocumentNo|MaterialCode|DestinationWarehouseCode
PO|ERP|PurchaseOrderNo|MaterialCode|ReceivingWarehouseCode
EXEC|MES|MESWorkOrderNo
VIRTUAL_PI|RootDemandOrderCanonicalId|MaterialCode|StageCode
```

规范：
- Trim、英文代码大写、`|`分隔；
- 不含数量、时间、TaskId、PlanVersionId或自增Id；
- ODS已按单据+物料+目的仓库合并，V1不带行号；
- 虚拟PI不进入HardLock、ExecutionLock、MES下发或现实非Task供给表；
- Pipeline与Received保留单据身份时使用DOC键；进入普通池化库存并失去单据身份后使用INV键。严格绑定数量不得在转换前丢失DOC身份；
- PI位置和ExecutionLockedOutput始终沿用PI键。

`PeggingSupplyAllocation`不是候选供给表，`PeggingAllocationLedger`也不是运行时余额表。候选和余额只存在Domain内存对象。

#### 2.10.5 SupplyFact_Pipeline MOCK 数据

V1 厂间在途真实数据短期没有，可用 `SourceSystem='MOCK'`、`BatchNo='MOCK_YYYYMMDD'` 写入正式字段联调。后续真实数据接入不改 Pegging 主流程。

#### 2.10.6 号位分工

| 工作 | 号位 |
|------|------|
| ProcessCodeDict.ERPProperty 同步维护 | 5号位 |
| ERP_Received_ByDocument_View | 5号位 |
| MES_APS_BOM_Workset_CrossFactoryEdge | 5号位 |
| ext_ERP_Received_ByDocument_View | 2号位 |
| APS_BOM_CROSS_FACTORY_EDGE_RAW | 2号位 |
| PeggingAllocationLedger / PeggingSupplyAllocation | 2号位（5号位只返回AllocationDecision） |
| M库判定索引 | 2号位 |
| 跨厂模式/资格判断 | 5号位返回只读Result；2号位验证并执行 |
| ExecutionLock / DemandSupplyHardLock | 2号位持久化；5号位计算HardLock命中 |
| 1号位 | 只通过方法参数消费2号位传入的内存LogicalBlock/TaskDraft、Routing和资源约束；不查询MES_ProcessCode_View/Received/PeggingSupplyAllocation或任何数据库表 |

---


**ExecutionLock代码落地边界**：
- `ExecutionLock`不是现有Task、MES快照或其他类的改名，而是跨PlanVersion稳定识别现实MES工单的最小持久化实体；
- 当前代码没有该实体时，按DDL v5.2.3实现实体、仓储/批量写入、按MESWorkOrderNo恢复及`Task.ExecutionLockId`关联；
- V1不新增`ExecutionLockTaskLink`，不做完整事件溯源，不建立独立锁管理服务，也不把普通SOFT归属固化为执行锁。

### 2.11 Pegging数量、位置、执行锁与跨版本防腐闭环（v1.36新增）

#### 2.11.1 分层定位与权威边界

```text
ERP Order.Quantity / ReceivedQty ──→ PI总量边界
MES Stage/Operation/WorkOrder ─────→ 位置与执行事实
ERP XC / 在途 / Received ─────────→ 强位置事实或现实非Task供给
APS规则版本 ──────────────────────→ 资格、排序、HardLock判断

上述事实 → PI Header/Position快照 → Runtime余额/Ledger → TaskDraft
        → 1号位有限产能排定 → 正式Task/Pegging
        → MESPlanRelease(PUBLISHED) → MES视图确认建单 → ExecutionLock → 次日恢复
```

**权威分工**：
- `ProductionInstructionSupplySnapshot`回答“这张PI本次运行总量是多少、闭合是否成立”；
- `ProductionInstructionPositionSlice`回答“这些数量现在分别位于哪里、还需要哪些Stage”；
- `PeggingAllocationLedger`回答“哪项需求取得哪项供给多少、为什么、最后组成哪个Task”；
- `ExecutionLock`回答“哪个现实MES执行过程必须继续”；
- `DemandSupplyHardLock`回答“哪一数量必须继续服务哪个需求”。

#### 2.11.2 PI快照隔离、来源与幂等

**Header最小键**：`ScheduleRunId + DomainKey + ProductionInstructionNo`。夜间一Run多Domain时，每张PI只在所属Domain生成一次Header；`PlanVersionId`可作为便捷追溯字段，不得导致同一运行重复快照。

**PositionSlice最小键**：`SupplySnapshotId + PositionType + PositionCode + StableSourceKey`。`PositionType`至少支持 `STAGE / XC / PIPELINE / UNLOCATED`，所有有效切片互斥且数量合计必须等于Header的AvailableProductionQty。

**来源追溯**：保存 `DataCutoffTime`、MES Stage/WorkOrder快照键、XC来源键、管道来源业务键、算法版本和异常裁剪标记。重新执行同一ScheduleRun+Domain时，采用“删除/替换该运行切片或Stage表后原子切换”，禁止重复追加造成双计。

#### 2.11.3 位置计算与强事实拆分

1. 按BOM Stage路径取得生产指示承载路径；MACH PI可承载MACH+SURF+OUTS，ASSY单独承载。
2. 装载Stage累计完成量并修复单调性：后续Stage累计不得大于前序Stage；缺失中间Stage使用下游最小证明量。
3. 对修复后的累计量做差分，形成互斥Stage基础位置。
4. 用XC、厂间在途等更强位置事实从相应基础位置中拆出；拆出只改变位置类型，不改变PI总量。
5. 无法由任何事实定位的剩余量写入UNLOCATED，不得丢弃或隐式归零。

**XC红线**：`ERPProperty=XC`映射到某Stage，表示该数量尚未完成该Stage。进入某张PI位置快照的XC必须能够识别该`ProductionInstructionNo`；缺少PI号的XC不得作为该PI供给，只能进入独立库存候选或问题清单，禁止凭物料相同强行归属。XC作为现实供给被需求占用后，仍必须根据PositionSlice生成当前Stage及后续Stage Task；库存扣减和生产路径生成是两件不同的事。

#### 2.11.4 Ledger原子分配、AllocationSequence与身份来源

`PeggingOrchestrator`只在内存中运行。一笔Decision必须原子完成：

```text
校验需求余额
+ 校验供给余额
+ 扣需求余额
+ 扣供给余额
+ 生成AllocationSequence
+ 追加PeggingLedgerEntry
```

`AllocationSequence`规则：
- 每个PlanVersion的一次Pegging调用使用局部计数器，从1开始；
- 只有双边扣减成功后才递增；
- 单Domain内顺序处理，不使用数据库Sequence，不放全局Context；
- 数据库唯一约束只作最终兜底。

身份来源：
- `ScheduleRunId`只取`PlanVersion.SourceScheduleRunId`；
- `DomainKey`只取`PlanVersion.DomainKey`；
- 根需求和当前需求CanonicalId只取`Order.OrderCanonicalId`并沿BOM递归继承；
- 缺失时终止当前Domain，不得写0或查询“最新”记录。

对PI供给先确定Header承接量，再绑定具体PositionSlice；Ledger同时保存Header业务键和Slice引用。Domain结束前校验需求不超分、供给不超分、PI切片闭合及Task份额闭合。

#### 2.11.5 MESPlanRelease、ExecutionLock、HardLock与未来产出

**MESPlanRelease创建条件**：当前ACTIVE版本的正式Task通过发布资格后，由2号位按同PI、同Stage、同执行批次组成发布单元。`MESPlanRelease.Quantity`必须取该Stage级执行批次的单一流转数量，来源于发布草稿/LogicalBlock的Stage执行份额；不得把同一批次下串行小工序Task.Quantity相加。同组Task数量不一致时，只有存在已配置的换算/损耗规则并完成数量闭合才允许组装，否则拒绝发布。写入`PUBLISHED`时Task仍为PLANNED；该数量已形成对MES的发布承诺，跨版本不得重新分配或生成新ReleaseItemKey。

**ExecutionLock创建条件**：MES在实时工单视图中回传ReleaseItemKey与稳定MESWorkOrderNo，APS快照确认后创建/更新。此时发布记录转CONSUMED，相关Task迁移为RELEASED。虚拟Task、仅排程未发布Task不得创建ExecutionLock。

ExecutionLock固定：生产指示、Stage、MES工单、原始执行量、完成量、正式取消量、当前现实工单剩余执行承诺及预计产出时间。它不保存独立的人工短量差额字段。数量约束为：

```text
0 <= CompletedQty + CancelledQty + RemainingExecutionQty
   <= OriginalExecutionQty
```

`RemainingExecutionQty`是由2号位维护的当前MES现实工单未来Stage产出承诺上限，不用于计算各小工序Task。MES工序状态4只表示该小工序执行记录人工完结；只有MES工单契约视图明确整张工单终结时，执行锁剩余量才置0，未完成且未正式取消的差额返回PI未承诺剩余池。

**物理身份与归属状态正交**：
- 物理数量身份：已消耗 / ExecutionLock / PUBLISHED MESPlanRelease / 普通PI PositionSlice，彼此互斥；
- 归属状态：每项物理供给内部再分HARD / SOFT / 未分配。

HardLock不能与ExecutionLock或MESPlanRelease直接相加，也不能创造额外供给。HardLock允许部分履约与部分解除：只要`RemainingLockedQty>0`，状态继续ACTIVE并跨版本恢复；已履约数量不得再次释放。`FulfilledQty`由2号位依据截至DataCutoffTime的累计履约/不可逆消耗事实覆盖式、单调更新，禁止按重复事件增量累加；`ReleasedQty`只接受规则或审批确认的未履约释放量。

**供给身份转换红线**：Pipeline与Received仍保留原单据身份时使用同一DOC键，旧表示必须先退出；真正进入普通池化库存并失去单据身份后改用INV键。命中HardLock或其他严格绑定的数量不得先丢失DOC身份再池化。PI位置转为ExecutionLockedOutput始终沿用PI键，禁止复制第二份物理数量。

#### 2.11.6 跨版本保留与重建

| 跨版本保留 | 每个PlanVersion重建 |
|---|---|
| PUBLISHED/CONSUMED MESPlanRelease、ExecutionLock、HardLock、MES工单锚点、已完成/已消耗事实 | TaskId、物理Pegging、普通SOFT分配、未发布虚拟Task |

新版本Task按现实状态关联稳定对象：
- 已建MES工单：关联原`MESPlanReleaseId + ExecutionLockId`，不得再次发布；
- 已发布未建单：关联原`MESPlanReleaseId`，Task保持PLANNED，待MES确认后再写ExecutionLockId；
- 尚未发布：不关联发布/执行对象，可按新计划生成发布单元。

V1允许一条MESPlanRelease/ExecutionLock关联同PI、同Stage下多个小工序Task；但未来产出只能计算一次，不得因Task数量重复形成多份供给。两个不同现实MES工单不得伪装合并。

#### 2.11.7 TaskDraft、1号位接口与统一事务

**对象形态与数据库隔离红线**：`TaskDraft / FinalTaskDraft / ScheduledTaskDraft / AllocationShare`只是当前Domain运行的内存对象，不存在对应物理表。2号位负责全部数据库读取和数据装载，并将`DomainSolveRequest`作为方法参数交给1号位；1号位实现必须保持纯内存，不得依赖数据库访问组件。正式`[Task]`只能由2号位在统一事务中根据`FinalTaskDraft`实例化持久化。

`SchedulingOrchestrator`继续作为单Domain总编排入口：

```text
构建Domain上下文
→ Phase 1.6生成内存TaskDraft（不写库）
→ PeggingOrchestrator纯内存分配
→ 2号位通过方法参数调用1号位纯内存有限产能排定
→ 1号位返回内存FinalTaskDraft/AllocationShares
→ 2号位实例化正式[Task]并统一事务持久化
```

TaskDraft至少携带：
- `TaskDraftKey`
- PI、Stage、Operation、Quantity、DueTime、Routing、资源和依赖
- `Components[AllocationSequence, ComponentQty]`
- `SUM(ComponentQty)=TaskDraft.Quantity`

1号位输出至少包含：
- 最终Task草稿及`FinalTaskDraftKey`
- `AllocationSequence→FinalTaskDraftKey→ComponentQty`
- 原有求解摘要和Issue

合并拆分必须保证每项AllocationSequence数量守恒；1号位不读DB、不写DB、不重新Pegging、不改变供给归属。

**单Domain显式事务顺序**：

```text
BEGIN
1. INSERT最终Task/ShippingTask
2. 建立FinalTaskDraftKey→TaskId
3. 回填并INSERT PeggingAllocationLedger
4. INSERT PeggingSupplyAllocation
5. INSERT Task-to-Task Pegging
6. INSERT同批PI快照/Explanation结果
7. 完整性校验
COMMIT
```

失败整域回滚，原ACTIVE和其他Domain不受影响。PlanVersion激活仍使用后续独立事务。推荐批量写入，禁止核心循环逐行I/O，也不强制新增复杂Repository体系。

#### 2.11.8 Issue与发布边界

技术/数据问题使用Pegging IssueCode，示例：
- `PI_RECEIVED_QTY_EXCEEDS_TOTAL`
- `PI_STAGE_PROGRESS_NON_MONOTONIC`
- `PI_POSITION_NOT_CLOSED`
- `PI_POSITION_UNLOCATED`
- `LEDGER_DEMAND_OVER_ALLOCATED`
- `LEDGER_SUPPLY_OVER_ALLOCATED`
- `EXECUTION_LOCK_DUPLICATE`
- `HARD_LOCK_QTY_EXCEEDS_EXECUTION_REMAINDER`

IssueCode与 `ScheduleExplanationFact.ReasonCode` 分层：前者用于数据质量、算法闭合和发布校验；后者只保存既有15项业务原因事实。P0数量不闭合、重复执行锁、硬锁超量必须阻断该Domain发布；UNLOCATED等可降级问题允许发布但必须产生Issue和解释信息。

---

## 第三部分：防腐防御机制（Anti-Corruption Layer）

本方案构建了坚固的**三层防弹衣**，彻底消除ERP换代恐惧。

### 3.1 第一层防腐：视图/ODS契约防腐

#### **3.1.1 核心原则**

**隔离变更**：
- ✅ 无论上游ERP年内如何改表，3年后如何替换为新SAP
- ✅ 只要MES ODS库中对外暴露的`Workset`表的列名契约不变
- ✅ APS核心C#引擎**0代码修改**

**契约锁定**：
- ✅ ODS库对外暴露的表结构必须**严格版本化**
- ✅ 任何列名、数据类型的变更都需要**版本升级**
- ✅ APS只依赖**稳定的契约接口**，不依赖ERP/MES的物理表

---

#### **3.1.2 ODS契约表设计**

**MES_APS_BOM_Workset（BOM展开结果契约表）**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-10
-- 变更历史：无
CREATE TABLE MES_APS_BOM_Workset (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,              -- 批次号（契约字段）
    BOMNO NVARCHAR(50) NOT NULL,                -- BOM编号（契约字段）
    ParentMaterialCode NVARCHAR(50) NOT NULL,   -- 父件物料编码（契约字段）
    ChildMaterialCode NVARCHAR(50) NOT NULL,    -- 子件物料编码（契约字段）
    Quantity DECIMAL(18,6) NOT NULL,            -- 用量（契约字段）
    Level INT NOT NULL,                         -- 层级（契约字段）
    Path NVARCHAR(MAX) NULL,                    -- 路径（契约字段）
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- ⚠️ 契约承诺：以上字段名、数据类型在v1.x版本中永不变更
-- 如需新增字段，只能追加，不能修改现有字段
-- 如需修改现有字段，必须升级到v2.0，并提供兼容层
```

**ERP_Master_View（ERP主数据契约视图）**：
```sql
-- 契约版本：v1.2
-- 最后修改：2026-03-21
-- 负责人：ERP DBA（源系统侧）
-- v1.2更新：增加Spec、SupplyMode、ProductionDeptCode字段，支持物料供给上下文
CREATE VIEW ERP_Master_View AS
SELECT 
    MaterialCode,          -- 物料编码（契约字段）
    MaterialName,          -- 物料名称（契约字段）
    Spec,                  -- 物料型号/规格（契约字段，v1.2新增）
    MasterID,              -- ERP物理主键（契约字段）
    Warehouse,             -- 仓库（契约字段）
    SupplyMode,            -- 供给方式（契约字段，v1.2新增）PURCHASE/MAKE/OUTSOURCE
    ProductionDeptCode,    -- 生产责任部门编码（契约字段，v1.2新增）
    UOM,                   -- 计量单位（契约字段）
    LeadTimeDays,          -- 提前期天数（契约字段）
    SafetyStock,           -- 安全库存（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM ERP.dbo.master
WHERE IsDeleted = 0;

-- ⚠️ 契约承诺：无论ERP内部表结构如何变更，此视图的列名和数据类型永不变更
-- ⚠️ v1.2更新说明：增加物料型号和供给上下文字段，支持MaterialSupplyContext表的数据来源
```

**MES_Material_View（MES物料契约视图）**（2026-04-01 v1.3更新：同构化）：
```sql
-- 契约版本：v1.3（同构化）
-- 最后修改：2026-04-01
-- 负责人：MES DBA（源系统侧）
-- v1.3更新（同构化）：字段与 ERP_Master_View 对齐，支持双源同构三表协同同步
--   保留 MES_ID、Location 原始列名（由 ext_MES_Material_View 做别名映射）
--   移除 MaterialType（由APS按MaterialCode前缀推导）
--   新增供给属性字段：SupplyMode, ProductionDeptCode, LeadTimeDays, SafetyStock, InventoryManagementMode
CREATE VIEW MES_Material_View AS
SELECT 
    MaterialCode,          -- 物料编码（契约字段）
    MaterialName,          -- 物料名称（契约字段）
    Spec,                  -- 物料型号/规格（契约字段）
    MES_ID,                -- MES物理主键（契约字段，ext层别名为MasterID）
    Location,              -- MES库位/仓库（契约字段，ext层别名为Warehouse）
    SupplyMode,            -- 供给方式（v1.3新增，同构化）
    ProductionDeptCode,    -- 生产部门代码（v1.3新增，同构化）
    UOM,                   -- 计量单位（契约字段）
    LeadTimeDays,          -- 提前期（v1.3新增，同构化）
    SafetyStock,           -- 安全库存（v1.3新增，同构化）
    InventoryManagementMode, -- 库存管理方式（v1.3新增，同构化）
    IsActive               -- 是否有效（契约字段）
FROM MES.dbo.Material
WHERE IsActive = 1;

-- ⚠️ 契约承诺：无论MES内部表结构如何变更，此视图的列名和数据类型永不变更
-- ⚠️ v1.3同构化说明：字段与 ERP_Master_View 对齐，MES_ID/Location 保留原名，
--    由 ext_MES_Material_View 统一别名为 MasterID/Warehouse
```

**ext_ERP_Master_View（ERP主数据跨库包装视图）**（2026-04-01 v1.3更新）：
```sql
-- 契约版本：v1.3
-- 最后修改：2026-04-01
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
-- v1.3更新：增加InventoryManagementMode字段，与ext_MES_Material_View双源同构
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
    InventoryManagementMode,   -- v1.3新增
    IsActive
FROM [MES_Integration].[dbo].[ERP_Master_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图，用于访问ODS库的ERP_Master_View
-- ⚠️ 同构契约：此视图字段与 ext_MES_Material_View 完全一致
```

**ext_MES_Material_View（MES物料跨库包装视图）**（2026-04-01 v1.2更新：双源同构契约）：
```sql
-- 契约版本：v1.3（双源同构）
-- 最后修改：2026-04-01
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
-- v1.3更新（同构化）：字段与 ext_ERP_Master_View 完全对齐
--   MES_ID → MasterID（别名），Location → Warehouse（别名）
--   移除 MaterialType（由APS按前缀推导）
--   新增供给属性字段：SupplyMode, ProductionDeptCode, LeadTimeDays, SafetyStock, InventoryManagementMode
CREATE VIEW ext_MES_Material_View AS
SELECT 
    MaterialCode,
    MaterialName,
    Spec,
    MES_ID AS MasterID,           -- 同构别名：MES_ID → MasterID
    Location AS Warehouse,         -- 同构别名：Location → Warehouse
    SupplyMode,                    -- v1.3新增（同构化）
    ProductionDeptCode,            -- v1.3新增（同构化）
    UOM,
    LeadTimeDays,                  -- v1.3新增（同构化）
    SafetyStock,                   -- v1.3新增（同构化）
    InventoryManagementMode,       -- v1.3新增（同构化）
    IsActive
FROM [MES_Integration].[dbo].[MES_Material_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图，用于访问ODS库的MES_Material_View
-- ⚠️ 同构契约：此视图字段与 ext_ERP_Master_View 完全一致，sp_SyncMasterData 逻辑零分叉
```

---

### 3.1.5 库存契约视图（v2.8新增）⭐ 核心契约

**⚠️ 架构原则说明**：
- 库存契约View中的 `MaterialCode` 属于**辅助业务键字段**，用于展示与诊断
- 库存链路中的**权威挂接仍以 MaterialMapping 为准**，通过 SourceID（ERP的MasterID / MES的MES_ID）+ Warehouse 的当前有效映射进行物理身份桥接（2026-04-01 v1.2更新）
- MaterialCode 的定义权在主数据契约层（ERP_Master_View、MES_Material_View），库存View不承担"定义统一业务键"的责任

**ERP_Inventory_View（ERP库存契约视图）**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-21
-- 负责人：ERP DBA（源系统侧）
-- 业务用途：ERP库存标准化视图，供APS系统拉取
CREATE VIEW ERP_Inventory_View AS
SELECT 
    MaterialCode,          -- 物料编码（辅助字段，用于展示与诊断）
    MasterID,              -- ERP主键（契约字段，用于权威挂接）
    WarehouseCode,         -- 仓库编码（契约字段）
    CASE
        WHEN ISNULL(Quantity,0) <= 0 THEN 0
        WHEN ISNULL(WasterQty,0) <= 0 THEN Quantity
        WHEN WasterQty >= Quantity THEN 0
        ELSE Quantity - WasterQty
    END AS Quantity,       -- APS净可用量；已扣除WasterQty，不等于ERP账面总量
    FactoryCode,           -- 工厂编码（契约字段）
    SnapshotTime,          -- 快照时间（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM ERP.dbo.Inventory
WHERE IsDeleted = 0;

-- ⚠️ 契约承诺：无论ERP内部表结构如何变更，此视图的列名和数据类型永不变更
-- ⚠️ MaterialCode定位：辅助字段，权威挂接在MaterialMapping
-- ⚠️ Quantity语义：从ODS开始即为净可用量；APS事实层、候选层、明细层和余额层不得再次扣减WasterQty
-- ⚠️ 客户专属：源系统无法按客户提供数量的专属仓库由InventoryAvailabilityRule整体排除，不新增空壳客户字段
```

**MES_Inventory_View（MES库存契约视图）**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-21
-- 负责人：MES DBA（源系统侧）
-- 业务用途：MES库存标准化视图，供APS系统拉取
CREATE VIEW MES_Inventory_View AS
SELECT 
    MaterialCode,          -- 物料编码（辅助字段，用于展示与诊断）
    MES_ID,                -- MES物料主键（契约字段，用于权威挂接）
    LocationCode,          -- 库位编码（契约字段）
    WarehouseCode,         -- 仓库编码（契约字段，v1.0新增）
    Quantity,              -- 库存数量（契约字段）
    FactoryCode,           -- 工厂编码（契约字段）
    SnapshotTime,          -- 快照时间（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM MES.dbo.Inventory
WHERE IsActive = 1;

-- ⚠️ 契约承诺：无论MES内部表结构如何变更，此视图的列名和数据类型永不变更
-- ⚠️ 注意：MES库存同时包含LocationCode（库位）和WarehouseCode（仓库）
-- ⚠️ MaterialCode定位：辅助字段，权威挂接在MaterialMapping
```

**ext_ERP_Inventory_View（ERP库存跨库包装视图）**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-22
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
CREATE VIEW ext_ERP_Inventory_View AS
SELECT 
    MaterialCode,
    MasterID,
    WarehouseCode,
    Quantity,
    FactoryCode,
    SnapshotTime,
    IsActive
FROM [MES_Integration].[dbo].[ERP_Inventory_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图，用于访问ODS库的ERP_Inventory_View
```

**ext_MES_Inventory_View（MES库存跨库包装视图）**：
```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-22
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
CREATE VIEW ext_MES_Inventory_View AS
SELECT 
    MaterialCode,
    MES_ID,
    LocationCode,
    WarehouseCode,
    Quantity,
    FactoryCode,
    SnapshotTime,
    IsActive
FROM [MES_Integration].[dbo].[MES_Inventory_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图，用于访问ODS库的MES_Inventory_View
```

---

**MES_APS_Routing_View（工艺路线契约视图）**：⚠️ **v5.0废弃（2026-04-01）**

> 原线性视图已拆分为 3 个独立视图，支持工艺图模型。保留此节仅为历史参考。

---

**MES_APS_Routing_Operation_View（工序节点视图契约）**（2026-04-01 v5.0新增）：

**⚠️ 架构原则说明**：
- **v5.0.1变更（2026-04-02）**：ODS视图不再输出MaterialCode，改为输出`MES_ID`+`Model`（MES原生标识）
  - 原因：防止多视图各自独竏映射MaterialCode导致不一致
  - APS侧2号位装载时通过 `MaterialMapping(Source='MES', SourceID=MES_ID)` 统一关联得到 `MaterialId`
- **V1默认路径约束**：RouteCode='DEFAULT', PathId=1（只输出默认路径）
- **V2扩展**：输出多条路径，通过 IsDefaultPath/PathPriority 选择

```sql
-- 契约版本：v2.0（v5.0重构：从线性顺序表升级为工序节点表）
-- 创建日期：2026-04-01
-- 负责人：3号位（梳理28张离散工艺表）
-- 替代原 MES_APS_Routing_View 的工序节点部分
-- ⚠️ v5.0.1变更（2026-04-02）：MaterialCode → MES_ID + Model
CREATE VIEW MES_APS_Routing_Operation_View AS
SELECT 
    MES_ID,             -- MES物料主键（契约字段，INT NOT NULL）
    Model,              -- MES物料型号（契约字段，NVARCHAR）
    RouteCode,          -- 工艺路径编码（V1固定'DEFAULT'）
    PathId,             -- 路径序号（V1固定1）
    OperationCode,      -- 工序编码（路径内唯一）
    OperationName,      -- 工序名称（契约字段）
    ProcessType,        -- 工序类型：MACHINING/ASSEMBLY/INSPECTION
    StandardTime,       -- 标准工时（分钟）（契约字段）
    SetupTime,          -- 准备时间（分钟）（契约字段）
    IsActive,           -- 是否有效（契约字段）
    SourceSystem        -- v5.0.24 追溯增强字段（非运行必需）；'MES'（当前唯一来源）；未来EAM上线时扩充；与MES_BOM_View.SourceSystem模式对齐
FROM (
    -- 新结构工艺（优先）
    SELECT 
        r.MES_ID,
        m.Model,
        'DEFAULT' AS RouteCode, 1 AS PathId,
        r.OperationCode, r.OperationName, r.ProcessType,
        r.StandardTime, r.SetupTime, r.IsActive,
        'MES' AS SourceSystem
    FROM MES_Routing_New r
    INNER JOIN MES_Material m ON r.MES_ID = m.MES_ID
    WHERE r.IsActive = 1
    UNION ALL
    -- 老旧结构工艺（3号位ETL处理MaterialModel→MES_ID）
    SELECT 
        m.MES_ID,
        r.MaterialModel AS Model,
        'DEFAULT' AS RouteCode, 1 AS PathId,
        r.OpCode AS OperationCode, r.OpName AS OperationName, r.ProcessType,
        r.StdTime AS StandardTime, r.SetupTime, 1 AS IsActive,
        'MES' AS SourceSystem
    FROM MES_Routing_Old r
    INNER JOIN MES_Material m ON m.Model = r.MaterialModel
    WHERE NOT EXISTS (
        SELECT 1 FROM MES_Routing_New n WHERE n.MES_ID = m.MES_ID
    )
) AS UnifiedOperation;

-- ⚠️ 契约承诺：列名和数据类型永不变更
-- ⚠️ V1约束：RouteCode='DEFAULT', PathId=1（只输出默认路径）
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping(Source='MES', SourceID=MES_ID) → MaterialId
```

---

**MES_APS_Routing_Dependency_View（工序依赖视图契约）**（2026-04-01 v5.0新增）：

```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01
-- 负责人：3号位
-- 输出工序间的有向依赖关系，支持并行/串行混合工艺
-- 并行：A→B, A→C 则B和C可并行；汇合：B→D, C→D 则D等B+C都完成
-- ⚠️ v5.0.1变更（2026-04-02）：MaterialCode → MES_ID + Model
CREATE VIEW MES_APS_Routing_Dependency_View AS
SELECT 
    MES_ID,                 -- MES物料主键（契约字段，INT NOT NULL）
    Model,                  -- MES物料型号（契约字段，NVARCHAR）
    RouteCode,              -- 工艺路径编码（契约字段）
    PathId,                 -- 路径序号（契约字段）
    FromOperationCode,      -- 前驱工序编码（契约字段）
    ToOperationCode,        -- 后继工序编码（契约字段）
    DependencyType,         -- 依赖类型：ES/SS/FF（V1先只用ES）
    LagTime,                -- 延迟时间（分钟）（契约字段）
    IsActive,               -- 是否有效（契约字段）
    'MES' AS SourceSystem   -- v5.0.24 追溯增强字段（非运行必需）；与MES_APS_Routing_Operation_View对齐
FROM MES.dbo.RoutingDependency  -- ⚠️ 实际物理表名，由3号位适配（含老结构ETL处理MES_ID）
WHERE IsActive = 1;

-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

---

**APS_OperationResourceEligibility_View（工序资源能力视图契约）**（2026-04-01 v5.0新增）：

```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01
-- 负责人：3号位（从MES工序-设备能力关系表输出）
-- 替代原ResourceGroup的排程能力分组功能
-- ⚠️ v5.0.1变更（2026-04-02）：MaterialCode → MES_ID + Model
CREATE VIEW APS_OperationResourceEligibility_View AS
SELECT 
    MES_ID,             -- MES物料主键（契约字段，INT NOT NULL）
    Model,              -- MES物料型号（契约字段，NVARCHAR）
    RouteCode,          -- 工艺路径编码（契约字段）
    PathId,             -- 路径序号（契约字段）
    OperationCode,      -- 工序编码（契约字段）
    ResourceCode,       -- 资源编码（契约字段）
    Priority,           -- 优先级（1=最优）（契约字段）
    CapacityFactor,     -- 产能系数（契约字段）
    IsPrimary,          -- 是否首选资源（契约字段）
    IsActive            -- 是否有效（契约字段）
FROM MES.dbo.OperationResourceMapping  -- ⚠️ 实际物理表名，由3号位适配（含老结构ETL处理MES_ID）
WHERE IsActive = 1;

-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

---

**MES_APS_Resource_View（资源主数据视图契约）**（2026-04-01 v5.0新增；v1.14 重命名，原名 `APS_Resource_View`）：

```sql
-- 契约版本：v1.1（v1.0原名 APS_Resource_View，v1.14/DDL v5.0.13 重命名）
-- 创建日期：2026-04-01；重命名：2026-04-25
-- 负责人：MES DBA（源系统侧）
-- 命名对齐：与 MES_APS_Routing_Operation_View / MES_APS_Routing_Dependency_View 系列一致
-- 未来 EAM 上线时并行新增 EAM_APS_Resource_View（同构契约）
CREATE VIEW MES_APS_Resource_View AS
SELECT 
    ResourceCode,           -- 资源编码（APS统一业务键）
    ResourceName,           -- 资源名称
    ExternalResourceId,     -- 源系统物理主键（MES设备ID或EAM资产ID）
    SourceSystem,           -- 来源系统：MES / EAM
    FactoryCode,            -- 工厂编码
    ProductionDeptCode,     -- 🔄 v5.0.16 RENAME from WorkshopCode；APS 排程责任部门码（业务确认 MES 也无"车间"概念）
    ResourceType,           -- MACHINE / LINE / MANUAL_STATION
    Status,                 -- AVAILABLE / MAINTENANCE / DECOMMISSIONED
    CapacityFactor,         -- 产能系数（1.0=标准）
    IsActive,               -- 是否有效
    UpdatedAt               -- 最后更新时间
FROM MES.dbo.Equipment      -- ⚠️ 实际物理表名，由MES DBA适配
WHERE IsDeleted = 0;
```

---

**ext_MES_APS_Resource_View（资源主数据跨库包装视图）**（2026-04-03审计补充，v5.0新增；v1.14 重命名，原名 `ext_APS_Resource_View`）：
```sql
-- 契约版本：v1.1
-- 创建日期：2026-04-01；重命名：2026-04-25
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
-- 全量刷新到 Resource 表（每天 00:10，与 sp_SyncMasterData 同窗口并行，调用 sp_SyncResourceData @SourceType='MES'）
CREATE VIEW ext_MES_APS_Resource_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Resource_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图
-- ⚠️ 字段契约：与ODS层 MES_APS_Resource_View 完全一致
-- ⚠️ 未来 EAM 上线：再建一张 ext_EAM_APS_Resource_View（指向 EAM_Integration.dbo.EAM_APS_Resource_View），字段契约与 MES 侧零分叉
```

---

**ext_MES_APS_Routing_Operation_View（工序节点跨库包装视图）**（2026-04-03审计补充，v5.0新增）：
```sql
-- 契约版本：v2.0
-- 创建日期：2026-04-01
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
-- v5.0.1变更（2026-04-02）：ODS视图输出MES_ID+Model，2号位装载时映射MaterialId
-- 增量Upsert到 RoutingOperation 表（每天 00:30）
CREATE VIEW ext_MES_APS_Routing_Operation_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Operation_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图
-- ⚠️ 装载时需先通过 MaterialMapping(Source='MES', SourceID=MES_ID) 映射得到 MaterialId
```

---

**ext_MES_APS_Routing_Dependency_View（工序依赖跨库包装视图）**（2026-04-03审计补充，v5.0新增）：
```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
-- v5.0.1变更（2026-04-02）：同上，MES_ID+Model
-- 增量Upsert到 RoutingDependency 表（每天 00:30，与 RoutingOperation 同批次）
CREATE VIEW ext_MES_APS_Routing_Dependency_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Dependency_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图
-- ⚠️ 装载时需先通过 MaterialMapping 映射 MES_ID→MaterialId
```

---

**ext_APS_OperationResourceEligibility_View（工序资源能力跨库包装视图）**（2026-04-03审计补充，v5.0新增）：
```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01
-- 负责人：2号位（APS数据装载）
-- 所属库：APS_Production
-- v5.0.1变更（2026-04-02）：同上，MES_ID+Model
-- 增量Upsert到 OperationResourceEligibility 表（每天 00:35）
CREATE VIEW ext_APS_OperationResourceEligibility_View AS
SELECT * FROM [MES_Integration].[dbo].[APS_OperationResourceEligibility_View];

-- ⚠️ 职责说明：2号位负责在APS库创建此跨库包装视图
-- ⚠️ 装载时需先通过 MaterialMapping 映射 MES_ID→MaterialId
```

---

#### **3.1.3 契约视图职责分工（Socket-Plug模式）**

**契约插座（Socket）** - 源系统侧防腐，绝对冻结：
- **`ERP_Master_View`**：ERP DBA负责创建，暴露ERP主数据标准字段
- **`MES_Material_View`**：MES DBA负责创建，暴露MES物料标准字段
- **`MES_BOM_Edge_Active`**：MES DBA / 5号位共同维护（ODS库物化边表），由 `sp_RefreshBOMEdgeActive` 刷新；**V1 兼任 BOM 防腐合同层 + 执行优化层**（v5.0.26 新增 2026-05-14）
  - 最小运行必需字段（9个）：BOMNO、ParentMaterialCode、ChildMaterialCode、Quantity、IsActive、IsDefaultVersion、ParentProcRefCode、ChildProcRefCode、ChildSourceHintCode
  - 追溯增强字段（6个）：SourceSystem、SourceBOMId、SourceLineNo、EffectiveFrom、EffectiveTo、RefreshBatchNo
  - ⚠️ **唯一默认版本裁决原则（v5.0写死）**：IsDefaultVersion=1 全局唯一；VersionPriority 不暴露为正式契约字段
  - **V2 预留**：V2 拆出 `MES_BOM_Edge_Contract`（历史版本+裁决过程+审计）时，`MES_BOM_Edge_Active` 封化为纯执行层
- **`MES_BOM_View`**：MES DBA维护，**v5.0.26 降为兼容视图**（`SELECT * FROM MES_BOM_Edge_Active WHERE IsActive=1`）；字段契约不变，下游无需修改查询；**禁止对此视图做递归 CTE**
- **`MES_APS_Resource_View`**：MES DBA负责创建，暴露设备主数据标准字段（v5.0新增；v1.14 重命名，原名 `APS_Resource_View`）
- **预留 `EAM_APS_Resource_View`**：未来 EAM 上线时由 EAM DBA 在 ODS 同构新建（双源并存，字段契约零分叉）
- **`MES_Routing_New/Old`**：MES DBA负责维护，28张离散工艺表

**数据插头（Plug）** - ODS侧防腐，数据清洗（2026-04-01 v5.0更新）：
- **5号位负责**：
  - `sp_RefreshBOMEdgeActive`：从 ERP/MES 多源物理表刷新 `MES_BOM_Edge_Active`；写 RefreshLog RUNNING→COMPLETED/FAILED；**FAILED 状态时禁止 Workset 展开**（v5.0.26 新增 2026-05-14）
  - `MES_APS_BOM_Workset`：批次展开结果表，供APS拉取
  - `MES_APS_BOM_Workset_StageDetail`：BOM边的完整大工艺顺序明细（v1.8新增 2026-04-13）；**v5.0.26 +1列 `WorksetId`**（FK→Workset.Id，Archive/Realtime变体同步）
- **3号位负责**：
  - ~~`MES_APS_Routing_View`~~：⚠️ v5.0废弃，已拆分为以下3个视图
  - `MES_APS_Routing_Operation_View`：工序节点视图（v5.0新增）
  - `MES_APS_Routing_Dependency_View`：工序依赖视图（v5.0新增）
  - `APS_OperationResourceEligibility_View`：工序资源能力视图（v5.0新增）
  - `MES_APS_Routing_Stage_View`：大工艺阶段视图（v1.7新增，v1.8定位调整为阶段字典 2026-04-13 更新）
- **MES DBA负责**（v1.8更新 2026-04-13，v5.0更新 2026-04-15）：
  - `MES_BOM_View` v1.8加列 `ParentProcRefCode` + `ChildProcRefCode` + `ChildSourceHintCode`（ERP BOM原始辅助字段）
  - `MES_BOM_View` v5.0加列 `SourceSystem` + `SourceBOMId`（追溯增强）+ 内部实现唯一默认版本裁决逻辑
  - `MES_BOM_View` v5.0.26 降为兼容视图：仅 `SELECT * FROM MES_BOM_Edge_Active WHERE IsActive=1`；不再直接 UNION ERP/MES 源表

**数据装载（Loader）** - APS侧，结构标准化（2026-04-01 v5.0更新，2026-04-13 v1.8更新）：
- **2号位负责**：
  - `ext_ERP_Master_View`：跨库包装视图，访问ODS库的ERP_Master_View
  - `ext_MES_Material_View`：跨库包装视图，访问ODS库的MES_Material_View
  - `ext_MES_APS_Resource_View`：跨库包装视图，访问ODS库的 MES_APS_Resource_View（v5.0新增；v1.14 重命名）
  - `ext_MES_APS_Routing_Operation_View`：跨库包装视图（v5.0新增）
  - `ext_MES_APS_Routing_Dependency_View`：跨库包装视图（v5.0新增）
  - `ext_APS_OperationResourceEligibility_View`：跨库包装视图（v5.0新增）
  - `ext_ERP_Inventory_View`：跨库包装视图，访问ODS库的ERP_Inventory_View
  - `ext_MES_Inventory_View`：跨库包装视图，访问ODS库的MES_Inventory_View
  - `sp_SyncMasterData(@SourceType)`：双源统一主数据三表协同同步（v4.0）
  - `sp_SyncResourceData(@SourceType)`：资源主数据同步统一出口（v5.0.13 新增；v1 仅 'MES' 分支，'EAM' 分支预留）
  - `APS_BOM_RAW → BOM表`：从工作集缓存生成标准BOM字典
  - `ext_MES_APS_Resource_View → Resource表`：全量刷新设备主数据镜像（v5.0新增；v1.14 命名统一；执行体 sp_SyncResourceData）
  - `ext_MES_APS_Routing_Operation_View → RoutingOperation表`：增量Upsert工序节点（v5.0新增）
  - `ext_MES_APS_Routing_Dependency_View → RoutingDependency表`：增量Upsert工序依赖（v5.0新增）
  - `ext_APS_OperationResourceEligibility_View → OperationResourceEligibility表`：增量Upsert能力关系（v5.0新增）
  - `ext_MES_APS_Routing_Stage_View → RoutingStage表`：增量Upsert大工艺阶段字典（v1.7新增，v1.8定位调整为阶段字典 2026-04-13 更新）
  - `MES_APS_BOM_Workset_StageDetail → APS_BOM_STAGE_PATH_RAW`：搬运阶段顺序明细（v1.8新增 2026-04-13）；v5.0.26：搬运时透传 `WorksetId`（跨库引用，非FK）
  - `Order_Canonical → Order表`：订单数据装载与分区

**职责红线**：
- ❌ 源系统DBA不得修改契约视图的字段名和数据类型（只能追加字段）
- ❌ 5号位不得修改源系统的物理表结构（只能在ODS库创建契约视图）
- ❌ 2号位不得直接访问源系统表（只能通过APS库的ext跨库包装视图访问ODS库）
- ❌ **1号位/5号位不负责Routing相关工作**（由3号位负责ODS契约，2号位负责同步装载）（v5.0澄清）
- ❌ **vw_MES_BOM_Stage_Enriched 不得被 APS 排程内核查询**（v1.10新增）：该视图是 ODS 内部的"派生便利视图"（非 Socket-Plug 防腐层），含 ERP 特征字段（ProcessCode/ActualFactory/TrusteeProcCode）；APS 排程如需委外/受托信息由 2 号位预计算落独立配置表，不得直接下沉 ERP 字段语义
- ❌ **`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` 禁止对 `MES_BOM_View` 做递归 CTE**（v1.21新增）：这两个 SP 必须直接读 `MES_BOM_Edge_Active`（已建专项索引，WHILE 循环每层 JOIN 优化）；`MES_BOM_View` 仅为下游旧库存备用，不得进入展开路径

---

#### **3.1.3b 视图分类与防腐定位**（v1.10 新增 2026-04-23）

APS 数据架构的视图按防腐职责分 **3 类**，命名规范不同，**不得混用**：

| 类别 | 命名前缀 | 位置 | 职责 | ERP 升级时 | 是否防腐层 |
|---|---|---|---|---|---|
| **A. Socket-Plug 契约视图** | `ERP_*_View` / `MES_*_View` / `MES_APS_*_View` | ODS 库（ERP/MES DBA 创建）| 承接源系统字段契约 | **改这里**（防腐入口）| ✅ 是 |
| **B. 跨库包装视图** | `ext_*_View` | APS_Production 库（2 号位创建）| `SELECT *` 跨库透传，隔离跨库引用 | 不改 | ✅ 是（跨库隔离）|
| **C. 派生便利视图**（v1.10 引入）| `vw_*` | **ODS 库内部** | JOIN 派生扩展语义（如 StageCode → ProcessCode / ActualFactory / TrusteeProcCode）| 改 SELECT 列表吸收字典列名变化 | ❌ 非防腐层 |

**C 类当前成员**：
- `vw_MES_BOM_Stage_Enriched` — StageDetail + 工序对照字典派生

**C 类使用边界**：
- ✅ ODS 内部：委外 ShippingTask 生成器、运维诊断、BI 报表
- ❌ **APS 排程内核禁用**：ERP 特征字段会穿透防腐墙
- ❌ **APS 本地不做对称视图**：若在 APS_Production 建 `vw_APS_BOM_Stage_Enriched`，ERP 升级时 APS 排程代码会直接被影响

**APS 排程如需委外/受托信息的正确做法**：
- 由 2 号位在 APS 本地**预计算独立配置表**（如扩展 `StageLeadTimeParam`、或新建 `ProcessTrusteeConfig`）
- 排程时 LOOKUP 配置表而非查 C 类视图
- ERP 升级只影响配置表装载逻辑，不影响排程代码

---

#### **3.1.4 契约版本管理**

**版本升级策略**：
- v1.0  v1.1：只能**追加字段**，不能修改现有字段
- v1.x  v2.0：可以**修改字段**，但必须提供**v1.x兼容层**

**兼容层示例**：
```sql
-- 假设v2.0需要将Quantity字段从DECIMAL(18,6)改为DECIMAL(20,8)
-- 必须同时提供v1.0兼容视图
CREATE VIEW MES_APS_BOM_Workset_v1 AS
SELECT 
    Id,
    BatchNo,
    BOMNO,
    ParentMaterialCode,
    ChildMaterialCode,
    CAST(Quantity AS DECIMAL(18,6)) AS Quantity,  -- 降级为v1.0精度
    Level,
    Path,
    CreatedAt
FROM MES_APS_BOM_Workset_v2;
```

---

### 3.2 第二层防腐：业务主键防腐

#### **3.2.1 核心原则**

**业务主键统一**：
- ✅ APS业务语义统一以`MaterialCode`为核心业务键
- ✅ 数据库内部允许使用代理键（自增ID）提升性能
- ❌ 不得再以ERP/MES的外部物理ID作为系统内主纽带

**物理ID隔离**：
- ✅ ERP的`masterID`只在`MaterialMapping`表中维护
- ✅ MES的`MES_ID`只在`MaterialMapping`表中维护
- ✅ APS核心业务逻辑**完全不感知**这些物理ID

---

#### **3.2.2 MaterialCode设计规范**

**编码规则**：
```
MaterialCode格式：{类型前缀}-{物料型号}-{版本号(可选)}

其中：
- "物料型号"取自企业现有 ERP/MES 中业务人员普遍使用、稳定且可读的型号字段
- 版本号默认不启用，仅在工程变更导致物料业务身份不兼容时启用

示例（不带版本号的常规场景）：
- RAW-STEEL-10X20：原材料-钢材型号10X20
- WIP-C25ILB-005：半成品-型号C25ILB-005
- FG-A900：成品-型号A900
- ASSY-TEMP01：装配件-临时型号01（v5.0.1变更 2026-04-02：取消MES-前缀）

示例（带版本号的特殊场景）：
- FG-A900-V2：成品A900第2版（设变后不兼容）
- WIP-C25ILB-005-V2：半成品C25ILB-005第2版（新旧物料不能混用）
```

**类型前缀**：
- `RAW-`：原材料（ERP管理）
- `FG-`：成品（ERP管理）
- `WIP-`：半成品（ERP管理）
- `ASSY-`：装配件（v5.0.1变更 2026-04-02：取消MES-，增加ASSY-）

**版本号使用原则**（必须遵守）：

版本号默认不启用，仅在"不兼容变更"场景启用。

**不加版本号的情况**：
- 图纸小修
- 工艺微调
- 向下兼容的优化
- 不影响库存/BOM/工艺混用的变更

**必须加版本号的情况**：
- 设变后新旧物料不能混用
- 新旧库存必须隔离
- 新旧 BOM 必须隔离
- 新旧工艺路线必须隔离
- 新旧供给关系或替代关系必须隔离

**原则总结**：只有当工程变更导致"业务身份不兼容"时，版本号才进入 MaterialCode。

**MaterialCode 与 Spec 字段的关系**：
- **MaterialCode**：APS 的统一业务键，参与跨系统对齐、主数据映射、订单、BOM、工艺、库存、排程等业务关系
- **Spec**：原始型号/规格展示字段，用于展示、对照 ERP/MES 原始业务字段，保留更贴近源系统的业务表达
- MaterialCode 允许与 Spec 出现部分重复，但二者语义不同，不视为重复设计

**不建议写入 MaterialCode 的内容**：
- MTO / MTS（由 Order_Canonical.OrderType 承载）
- 订单号（由 BOMNO 承载）
- 客户特征（由 Pegging 关系承载）
- 仓库信息（由 MaterialSupplyContext 承载）
- 责任部门信息（由 MaterialSupplyContext 承载）

**前提条件**（必须满足）：
1. **物料型号必须足够稳定**：同一时点下，一个"物料型号"在 APS 业务上必须能稳定指向一种物料身份
2. **物料型号应尽量跨 ERP / MES 对齐**：若 ERP 与 MES 中"物料型号"字段长期无法对齐，则必须在主数据契约层先完成标准化
3. **版本号治理必须制度化**：必须明确版本号启用条件，避免随意加版本号导致混乱

---

#### **3.2.3 核心表设计（以MaterialCode为主键）**

**Material表（物料主数据）**：
```sql
CREATE TABLE Material (
    Id INT PRIMARY KEY IDENTITY(1,1),           -- 代理键（内部使用）
    MaterialCode NVARCHAR(50) NOT NULL UNIQUE,  -- 业务主键（核心）
    MaterialName NVARCHAR(200) NOT NULL,
    MaterialType NVARCHAR(50) NOT NULL,         -- RAW/FG/WIP/MES
    UOM NVARCHAR(20) NOT NULL,
    DefaultLeadTimeDays INT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_Material_Code ON Material(MaterialCode) WHERE IsActive = 1;
CREATE INDEX IX_Material_Type ON Material(MaterialType) WHERE IsActive = 1;
```

**Order_Canonical表（订单标准表）**（2026-04-03 订单链路审计修正：对齐DDL补全字段）：
```sql
CREATE TABLE Order_Canonical (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),        -- 代理键（内部使用）
    OrderNo NVARCHAR(50) NOT NULL UNIQUE,       -- 订单号（业务主键）
    MaterialCode NVARCHAR(100) NOT NULL,        -- 物料编码
    BOMNO NVARCHAR(50) NOT NULL,                -- BOM编号
    Quantity DECIMAL(18,6) NOT NULL,            -- 订单数量
    DueDate DATETIME2 NOT NULL,                 -- 交期/计划日期
    Status NVARCHAR(20) NOT NULL,               -- Open/Released/Scheduled/Completed/Cancelled
    OrderType NVARCHAR(20) NOT NULL,            -- SO/MTO/MTS/SS/SS_U
    Priority INT NOT NULL DEFAULT 100,          -- 优先级（1-100）
    CustomerCode NVARCHAR(50) NULL,             -- 客户编码（SO订单）
    SourceSystem NVARCHAR(50) NOT NULL DEFAULT 'ERP', -- 来源系统
    SourceOrderId NVARCHAR(100) NULL,           -- 源系统订单ID（Upsert键之一）
    SourceMasterID INT NULL,                    -- ERP的masterID
    FactoryCode NVARCHAR(50) NULL,              -- ERP工厂编码（供Order装载时查Factory表）
    UOM NVARCHAR(20) NULL,                      -- 计量单位
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    FOREIGN KEY (MaterialCode) REFERENCES Material(MaterialCode)
);

CREATE INDEX IX_Order_Canonical_Material ON Order_Canonical(MaterialCode, Status);
CREATE INDEX IX_Order_Canonical_DueDate ON Order_Canonical(DueDate, Status);
CREATE INDEX IX_Order_Canonical_Type ON Order_Canonical(OrderType, Status);
CREATE INDEX IX_Order_Canonical_BOMNO ON Order_Canonical(BOMNO, Status);
CREATE INDEX IX_Order_Canonical_ActiveRoots 
    ON Order_Canonical(Status, OrderType, DueDate) 
    INCLUDE (BOMNO, MaterialCode, SourceMasterID);
CREATE UNIQUE INDEX IX_Order_Canonical_UpsertKey 
    ON Order_Canonical(SourceSystem, SourceOrderId) 
    WHERE SourceOrderId IS NOT NULL;
```

**Upsert键**：`SourceSystem + SourceOrderId`（由 `sp_ValidateAndPromoteOrders` 使用）

---

### 3.3 第三层防腐：Mapping护照表历史版本化（SCD Type 2）

#### **3.3.1 核心原则**

**历史版本化**：
- ✅ 建立`MaterialMapping`表（包含`ErpMasterId`, `MesMaterialId`, `MaterialCode`, `ValidFrom`, `ValidTo`, `IsCurrent`）
- ✅ 当发生ERP同一`masterID`对应物料变更时，通过拉链表记录轨迹
- ✅ 确保日后读取快照时能完美还原当时的物理映射

**SCD Type 2设计**：
- ✅ 每条记录都有生效时间（`ValidFrom`）和失效时间（`ValidTo`）
- ✅ 当前有效记录的`ValidTo = NULL`，`IsCurrent = 1`
- ✅ 历史记录的`ValidTo`为失效时间，`IsCurrent = 0`

---

#### **3.3.2 MaterialMapping表设计**

```sql
CREATE TABLE MaterialMapping (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialCode NVARCHAR(50) NOT NULL,         -- 核心业务键
    ERP_MasterID INT NULL,                      -- ERP物理主键
    ERP_Warehouse NVARCHAR(50) NULL,            -- ERP仓库
    MES_ID INT NULL,                            -- MES物理主键
    Source NVARCHAR(20) NOT NULL,               -- ERP/MES_CUSTOM
    ValidFrom DATETIME2 NOT NULL,               -- 生效时间
    ValidTo DATETIME2 NULL,                     -- 失效时间（NULL表示当前有效）
    IsCurrent BIT NOT NULL DEFAULT 1,           -- 是否当前有效版本
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- 当前有效版本索引
CREATE UNIQUE INDEX IX_MaterialMapping_Current 
ON MaterialMapping(MaterialCode, Source, IsCurrent) 
WHERE IsCurrent = 1;

-- 历史版本索引
CREATE INDEX IX_MaterialMapping_History 
ON MaterialMapping(MaterialCode, ValidFrom, ValidTo);

-- 时间点查询索引
CREATE INDEX IX_MaterialMapping_TimePoint 
ON MaterialMapping(MaterialCode, ValidFrom, ValidTo, IsCurrent);
```

---

#### **3.3.3 时间点查询（Point-in-Time Query）**

**查询某个时间点的物料映射**：
```sql
-- 查询2026-03-01时的物料映射关系
SELECT 
    MaterialCode,
    ERP_MasterID,
    ERP_Warehouse,
    MES_ID,
    Source
FROM MaterialMapping
WHERE MaterialCode = 'RAW-STEEL-001'
  AND ValidFrom <= '2026-03-01'
  AND (ValidTo IS NULL OR ValidTo > '2026-03-01');
```

**C#代码示例**：
```csharp
public async Task<MaterialMapping> GetMaterialMappingAtTime(string materialCode, DateTime pointInTime)
{
    return await dbContext.MaterialMappings
        .Where(m => m.MaterialCode == materialCode
                 && m.ValidFrom <= pointInTime
                 && (m.ValidTo == null || m.ValidTo > pointInTime))
        .FirstOrDefaultAsync();
}
```

---

#### **3.3.4 映射变更处理（SCD Type 2 Upsert）**

**场景**：ERP中`masterID = 100001`原本对应`MAT-A-001`，现在改为对应`MAT-A-002`

**处理逻辑**：
1. 将旧记录的`ValidTo`设置为当前时间，`IsCurrent`设置为0
2. 插入新记录，`ValidFrom`设置为当前时间，`IsCurrent`设置为1

**SQL示例**：
```sql
DECLARE @Now DATETIME2 = GETDATE();
DECLARE @MaterialCode NVARCHAR(50) = 'MAT-A-001';
DECLARE @NewMasterID INT = 100002;

-- 1. 关闭旧记录
UPDATE MaterialMapping
SET ValidTo = @Now,
    IsCurrent = 0,
    UpdatedAt = @Now
WHERE MaterialCode = @MaterialCode
  AND Source = 'ERP'
  AND IsCurrent = 1;

-- 2. 插入新记录
INSERT INTO MaterialMapping (MaterialCode, ERP_MasterID, ERP_Warehouse, MES_ID, Source, 
                              ValidFrom, ValidTo, IsCurrent, CreatedAt)
VALUES (@MaterialCode, @NewMasterID, 'WH-01', NULL, 'ERP', 
        @Now, NULL, 1, @Now);
```

---

#### **3.3.5 快照还原时的映射查询**

**场景**：读取2026-03-01的排程快照，需要还原当时的物料映射关系

**C#代码示例**：
```csharp
public async Task<ScheduleContext> LoadSnapshotWithMapping(int planVersionId)
{
    // 1. 加载快照
    var snapshot = await LoadScheduleSnapshot(planVersionId);
    
    // 2. 获取快照时间
    var snapshotTime = snapshot.SnapshotTime;
    
    // 3. 还原当时的物料映射
    foreach (var material in snapshot.Context.Materials)
    {
        var mapping = await GetMaterialMappingAtTime(material.MaterialCode, snapshotTime);
        
        if (mapping != null)
        {
            material.ERP_MasterID = mapping.ERP_MasterID;
            material.ERP_Warehouse = mapping.ERP_Warehouse;
            material.MES_ID = mapping.MES_ID;
        }
    }
    
    return snapshot;
}
```

---

## 第四部分：补充设计建议（5点优化）

基于原方案，以下是5点关键优化建议，确保系统在复杂环境下的稳定性和可维护性。

### 4.1 建议1：白天实时评估的 BOM 展开链路（v1.34 重写）

#### **4.1.1 问题分析**

**原方案盲区**：
- 80 万棵树的繁重准备工作在夜间 00:00 完成；夜间快照未覆盖白天新增或变化订单
- 若 PMC 在白天发起 CTP / 插单影响分析 / 局部重排，且目标订单在夜间 Workset 中不存在，APS 本地将无对应 BOM 切片

**v1.34 主路径（人工触发）**：
- **不再**由每小时订单同步程序检测新订单并触发实时展开
- **不再**由独立 SQL Server Agent Job 每 5 分钟自动扫描
- 白天 ERP 增量同步只更新 `Order_Canonical`
- 4号位页面展示新增/变化订单
- PMC 人工发起业务场景后，**3号位** 创建 `ScheduleRun`（详见 §2.7.2）
- **2号位** 检查目标订单是否已有可复用 BOM 切片
- 若不可复用，创建 `MES_API_BOM_Request_Detail` 并以 `RequestDetailId` 调用 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)`（详见 §2.7.4）

---

#### **4.1.2 实施要点**

> 表结构以 v5.2.2 为准（§5.1.5 / §5.1.6 / §5.1.6b）。
>
> 正式接口以 §6.1.3 为准：`sp_ExpandBOMRealtime_vNext(@RequestDetailId)`；旧 `sp_RequestRealtimeBOMExpansion(@BOMNO)` 已 deprecated。

**执行模式**：单请求同步调用；**3号位** 创建并编排 `ScheduleRun`；**2号位** 检查 BOM 切片、创建或定位 `RequestDetail`，并实际调用 `sp_ExpandBOMRealtime_vNext`；**不是自动轮询扫描**。

---

#### **4.1.3 性能保障**

**并发控制**：
- 单次白天实时展开只处理一个 `RequestDetailId`
- 若 PMC 短时间内并发发起多个评估，ODS 侧同时处理不超过 **3 个 RequestDetail**（应用层节流），避免影响 MES 生产库性能

**超时控制（单次请求）**：
- 单次 `sp_ExpandBOMRealtime_vNext` 完成目标：**5 分钟**
- **异常处理**：SP 内部异常由 `BEGIN CATCH` 分支写 `MES_API_BOM_Request_Realtime.Status='FAILED'` 并记录 `ErrorMessage`
- **应用层等待超时**：由 **2号位** 显式记录并收口状态（例如将超时中的记录标记为 `FAILED` 并写入 `ErrorMessage='应用层等待超时'`），避免长期停留在 `PROCESSING`
- **注意**：5 分钟只是单次请求的完成目标，**不是**自动扫描周期

**重试机制**：
- 失败的 `RequestDetail` 由 4号位页面向 PMC 显示错误，PMC 决定是否重试
- 失败重试会产生新的 `MES_API_BOM_Request_Realtime` 行；READY 查询按 `Id DESC` 取最新记录

---

### 4.2 建议2：MaterialMapping表增强

#### **4.2.1 问题分析**

**原方案提及**：
- `VW_APS_Material_Map`视图
- 但没有具体表结构

**需要增强**：
- 明确表结构设计
- 支持SCD Type 2历史版本化
- 支持时间点查询

---

#### **4.2.2 增强方案**

**详见3.3节：Mapping护照表历史版本化（SCD Type 2）**

**关键特性**：
- ✅ SCD Type 2设计
- ✅ 时间点查询
- ✅ 快照还原时的映射查询
- ✅ 映射变更处理

---

### 4.3 建议3：库存双源汇聚配置表

#### **4.3.1 问题分析**

**原方案提及**：
- "来源优先级表或例外清单"
- 但没有具体设计

**需要明确**：
- 哪些物料优先使用ERP库存
- 哪些物料优先使用MES库存
- 如何处理双源并存的异常情况

---

#### **4.3.2 配置表设计**（v5.0.39 口径已更新）

> ⚠️ **v5.0.39 删除**：`InventorySourcePriority` 已于 v5.0.39 正式删除（v2.8 已废弃）。V1 统一使用 `InventoryAvailabilityRule`。

```sql
-- V1 库存可用规则配置（替代旧 InventorySourcePriority）
-- IsAvailable=1: 允许进入可用库存池；IsAvailable=0: 排除；Priority: 扣减优先级（数值越小越优先）
-- 详见 DDL §2.5 InventoryAvailabilityRule 完整定义
SELECT * FROM InventoryAvailabilityRule WHERE IsActive = 1 ORDER BY Priority;
```

---

#### **4.3.3 库存汇聚逻辑**

**详见2.5.2节：库存双源汇聚【互斥隔离红线】**

---

### 4.4 建议4：ODS库的批次清理策略

#### **4.4.1 问题分析**

**原方案提及**：
- "历史批次按保留策略清理或归档"
- 但没有具体策略

**需要明确**：
- 保留多久的批次
- 如何归档
- 如何清理

---

#### **4.4.2 清理策略**

**保留策略**：
- 最近**7天**的批次全部保留
- 7天前只保留每天**最后一次成功批次**

**归档策略**：
- **30天前**的批次归档到历史表或文件

**清理策略**：
- **90天前**的批次物理删除

---

#### **4.4.3 清理存储过程**

```sql
CREATE PROCEDURE sp_CleanupBOMWorkset
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Now DATETIME2 = GETDATE();
    DECLARE @Keep7Days DATETIME2 = DATEADD(DAY, -7, @Now);
    DECLARE @Archive30Days DATETIME2 = DATEADD(DAY, -30, @Now);
    DECLARE @Delete90Days DATETIME2 = DATEADD(DAY, -90, @Now);
    
    -- 1. 归档30天前的批次
    INSERT INTO MES_APS_BOM_Workset_Archive (BatchNo, BOMNO, ParentMaterialCode, 
                                              ChildMaterialCode, Quantity, Level, Path, CreatedAt)
    SELECT BatchNo, BOMNO, ParentMaterialCode, ChildMaterialCode, Quantity, Level, Path, CreatedAt
    FROM MES_APS_BOM_Workset
    WHERE BatchNo IN (
        SELECT BatchNo FROM MES_API_BOM_Request
        WHERE CompletedAt < @Archive30Days
          AND Status = 'READY'
    );
    
    -- 2. 删除已归档的批次
    DELETE FROM MES_APS_BOM_Workset
    WHERE BatchNo IN (
        SELECT BatchNo FROM MES_APS_BOM_Workset_Archive
    );
    
    -- 3. 删除90天前的归档批次
    DELETE FROM MES_APS_BOM_Workset_Archive
    WHERE CreatedAt < @Delete90Days;
    
    -- 4. 删除7天前的非最后成功批次
    DELETE FROM MES_APS_BOM_Workset
    WHERE BatchNo IN (
        SELECT r1.BatchNo
        FROM MES_API_BOM_Request r1
        WHERE r1.CompletedAt < @Keep7Days
          AND r1.Status = 'READY'
          AND EXISTS (
              SELECT 1 FROM MES_API_BOM_Request r2
              WHERE CAST(r2.CompletedAt AS DATE) = CAST(r1.CompletedAt AS DATE)
                AND r2.CompletedAt > r1.CompletedAt
                AND r2.Status = 'READY'
          )
    );
    
    -- 5. 记录清理日志
    INSERT INTO MES_API_BOM_Request_Log (BatchNo, Message, CreatedAt)
    VALUES ('CLEANUP', '批次清理完成', @Now);
END;
GO

-- 创建定时作业（每天凌晨3点执行）
EXEC sp_add_job @job_name = 'CleanupBOMWorkset';
EXEC sp_add_jobstep @job_name = 'CleanupBOMWorkset', 
                    @step_name = 'Cleanup', 
                    @command = 'EXEC sp_CleanupBOMWorkset';
EXEC sp_add_schedule @schedule_name = 'Daily3AM', 
                     @freq_type = 4, 
                     @active_start_time = 030000;
EXEC sp_attach_schedule @job_name = 'CleanupBOMWorkset', 
                        @schedule_name = 'Daily3AM';
```

---

### 4.4.4 APS结果数据保留与分区运维（v1.38新增）

V1默认策略：ARCHIVED版本的Task/Pegging/Ledger/运行快照保留90天；FAILED与重试半成品保留14天；Summary/KPI至少2年；终态ExecutionLock/HardLock保留1年。清理由DBA或Hangfire按PlanVersion依赖顺序执行，测试库验证后方可启用，主DDL不自动删除生产数据。

`PF_PlanVersion`采用每100个PlanVersion一个分区并预建至100000。接近边界前由DBA扩展，不允许新版本长期堆积到尾部分区。

### 4.5 建议5：快照封存元数据增强

#### **4.5.1 问题分析**

**原方案提及**：
- "在PlanVersion表中记录文件路径"
- 但现有PlanVersion表可能没有这个字段

**需要增强**：
- 快照文件路径
- 文件大小
- 文件哈希（防篡改）
- 压缩后大小

---

#### **4.5.2 PlanVersion表增强**

```sql
-- 为PlanVersion表增加快照相关字段
ALTER TABLE PlanVersion ADD SnapshotFilePath NVARCHAR(500) NULL;
ALTER TABLE PlanVersion ADD SnapshotFileSize BIGINT NULL;
ALTER TABLE PlanVersion ADD SnapshotFileHash NVARCHAR(64) NULL;  -- SHA256
ALTER TABLE PlanVersion ADD SnapshotCompressedSize BIGINT NULL;
ALTER TABLE PlanVersion ADD SnapshotCreatedAt DATETIME2 NULL;

-- 创建索引
CREATE INDEX IX_PlanVersion_Snapshot 
ON PlanVersion(SnapshotCreatedAt DESC) 
WHERE SnapshotFilePath IS NOT NULL;
```

---

#### **4.5.3 快照完整性校验**

**校验逻辑**：
```csharp
public async Task<bool> VerifySnapshotIntegrity(int planVersionId)
{
    // 1. 获取快照元数据
    var snapshot = await planVersionRepository.GetSnapshot(planVersionId);
    
    if (snapshot == null || string.IsNullOrEmpty(snapshot.FilePath))
        return false;
    
    // 2. 检查文件是否存在
    if (!File.Exists(snapshot.FilePath))
    {
        logger.LogError($"快照文件不存在: {snapshot.FilePath}");
        return false;
    }
    
    // 3. 校验文件大小
    var actualSize = new FileInfo(snapshot.FilePath).Length;
    if (actualSize != snapshot.FileSize)
    {
        logger.LogError($"快照文件大小不匹配，预期: {snapshot.FileSize}, 实际: {actualSize}");
        return false;
    }
    
    // 4. 校验文件哈希
    var actualHash = await CalculateFileHash(snapshot.FilePath);
    if (actualHash != snapshot.FileHash)
    {
        logger.LogError($"快照文件哈希不匹配，预期: {snapshot.FileHash}, 实际: {actualHash}");
        return false;
    }
    
    return true;
}
```

---

## 第五部分：数据库表结构设计

基于防腐层架构，以下是完整的数据库表结构设计。

### 5.1 MES ODS库表结构

#### **5.1.1 BOM展开请求表（批次）**

```sql
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
    ErrorMessage NVARCHAR(MAX) NULL
);

CREATE INDEX IX_BOMRequest_Status ON MES_API_BOM_Request(Status, CreatedAt);
CREATE INDEX IX_BOMRequest_Completed ON MES_API_BOM_Request(CompletedAt DESC);
```

---

#### **5.1.2 BOM展开请求明细表**（v5.0.32 收敛字段结构）

```sql
-- v5.0.32：本表定位收敛为纯 BOM 请求输入表
-- 删除：Model / OrderStagingId / ResolvedBOMNO
-- ResolvedBOMNO 归 OrderBomRequestLink（2号位在 Workset 同步完成后写入）
CREATE TABLE MES_API_BOM_Request_Detail (
    Id               BIGINT          PRIMARY KEY NONCLUSTERED IDENTITY(1,1),
    BatchNo          NVARCHAR(50)    NOT NULL,
    OrderCanonicalId BIGINT          NOT NULL,            -- v5.0.31 核心锚点（跨库逻辑引用，无FK）
    OrderNo          NVARCHAR(100)   NULL,                -- 冗余，便于ODS侧排查/日志
    SourceSystem     NVARCHAR(20)    NULL,                -- 'ERP'/'MES'
    SourceOrderId    NVARCHAR(100)   NULL,
    MaterialCode     NVARCHAR(100)   NULL,                -- 5号位 BOM 入口解析主键
    FactoryCode      NVARCHAR(50)    NULL,
    OrderType        NVARCHAR(20)    NULL,
    RequestedBOMNO   NVARCHAR(50)    NULL,                -- 请求输入，可空=待5号位解析
    CreatedAt        DATETIME2       NOT NULL DEFAULT GETDATE(),

    FOREIGN KEY (BatchNo) REFERENCES MES_API_BOM_Request(BatchNo),
    CONSTRAINT UQ_BOMRequestDetail_BatchCanonical UNIQUE (BatchNo, OrderCanonicalId)
);

CREATE CLUSTERED INDEX IX_BOMRequestDetail_Batch
ON MES_API_BOM_Request_Detail(BatchNo, OrderCanonicalId);
```

---

#### **5.1.3 BOM展开结果工作集**

```sql
CREATE TABLE MES_APS_BOM_Workset (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,
    Level INT NOT NULL,
    Path NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE CLUSTERED INDEX IX_BOMWorkset_Batch 
ON MES_APS_BOM_Workset(BatchNo, ParentMaterialCode);

CREATE NONCLUSTERED INDEX IX_BOMWorkset_BOMNO 
ON MES_APS_BOM_Workset(BOMNO, Level);
```

---

#### **5.1.4 BOM展开结果归档表**

```sql
CREATE TABLE MES_APS_BOM_Workset_Archive (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,
    Level INT NOT NULL,
    Path NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL,
    ArchivedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE CLUSTERED INDEX IX_BOMArchive_Batch 
ON MES_APS_BOM_Workset_Archive(BatchNo, CreatedAt);
```

---

#### **5.1.5 实时BOM展开请求表**

> v5.0.33：新增 `RequestDetailId` / `OrderCanonicalId` / `ResolvedBOMNO`，幂等保护升级为 RequestDetail 级

```sql
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

CREATE INDEX IX_Realtime_Status ON MES_API_BOM_Request_Realtime(Status, Priority DESC, RequestTime);
```

---

#### **5.1.6 实时BOM展开结果工作集**

> **v1.38当前引用**：本表结构以**DDL v5.2.2**为唯一权威（DDL 中已含辅助字段、回填结果字段、`RequestDetailId` 追溯锚点及索引定义）。本节仅摘录字段约束与索引配合，避免与 DDL 出现二次定义漂移。

**字段摘要**（以 DDL v5.2.2 为准）：

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT IDENTITY | 主键，**必须为 `PRIMARY KEY NONCLUSTERED`**（避免与聚集索引 `IX_Realtime_BOMNO` 冲突）|
| BOMNO | NVARCHAR(50) NOT NULL | ResolvedBOMNO |
| ParentMaterialCode / ChildMaterialCode | NVARCHAR(50) NOT NULL | 物料 |
| Quantity | DECIMAL(18,6) NOT NULL | 单位用量 |
| Level | INT NOT NULL | BOM 层级 |
| ParentProcRefCode | **NVARCHAR(50) NULL** | 透传自 `MES_BOM_Edge_Active`（工序码，长度与 ODS 对齐）；`MES_BOM_View` 为 deprecated 兼容视图 |
| ChildProcRefCode | **NVARCHAR(50) NULL** | 透传自 `MES_BOM_Edge_Active`（工序码，长度与 ODS 对齐）；`MES_BOM_View` 为 deprecated 兼容视图 |
| ChildSourceHintCode | **NVARCHAR(50) NULL** | 透传自 `MES_BOM_Edge_Active`（Produce 字段，长度与 ODS 对齐）；`MES_BOM_View` 为 deprecated 兼容视图 |
| ChildRequiredStageCode | NVARCHAR(50) NULL | `sp_EnrichBOMWorksetRealtime` 回填 |
| ChildRequiredFactory | NVARCHAR(20) NULL | `sp_EnrichBOMWorksetRealtime` 回填 |
| RequestDetailId | BIGINT NULL | 订单粒度追溯锚点（DDL 无物理 FK）|
| CreatedAt | DATETIME2 NOT NULL DEFAULT GETDATE() | 创建时间 |

**索引摘要**（以 DDL v5.2.2 为准）：
- `IX_Realtime_BOMNO (BOMNO, Level)`：**聚集索引**（因此 `Id` 主键必须为 `NONCLUSTERED`）

> 完整 DDL 请查阅 `APS_数据库表结构设计_v5.0.sql v5.2.2` 中的 `dbo.MES_APS_BOM_Workset_Realtime` 定义。

---

#### **5.1.6b 实时BOM跨厂交接边表**（v1.34新增，当前对齐DDL v5.2.2）

> **本表结构以 DDL v5.2.2及字段说明v5.2.2 为唯一权威**，本节仅作引用说明。
>
> - 表名：`dbo.MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`
> - 所属库：MES_Integration
> - 隔离键：`RequestDetailId`（替代 `BatchNo`）
> - 关联：`WorksetId` 逻辑关联 `MES_APS_BOM_Workset_Realtime.Id`
> - 3 个索引：`IX_CrossFactoryEdge_RT_RequestDetail` / `IX_CrossFactoryEdge_RT_Workset` / `IX_CrossFactoryEdge_RT_BOMNO`
> - 生成 SP：`sp_GenerateBOMCrossFactoryEdgeRealtime(@BOMNO, @RequestDetailId)`
> - 只表达结构事实，**不判断 STAGE_HANDOFF / INTER_FACTORY_ORDER**；**0 行合法**
> - 5号位生成 / 2号位搬运

---

#### **5.1.7 日志表**

```sql
CREATE TABLE MES_API_BOM_Request_Log (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NULL,
    Message NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_Log_Batch ON MES_API_BOM_Request_Log(BatchNo, CreatedAt);
```

---

### 5.2 APS本地库表结构

#### **5.2.1 BOM原始数据表**

```sql
CREATE TABLE APS_BOM_RAW (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    ParentMaterialCode NVARCHAR(50) NOT NULL,
    ChildMaterialCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,6) NOT NULL,
    Level INT NULL,
    LLC INT NULL,                                    -- 低阶码
    IsLeaf BIT NOT NULL DEFAULT 0,                   -- 是否叶子节点
    Path NVARCHAR(MAX) NULL,
    SyncedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE CLUSTERED INDEX IX_BOM_RAW_Batch 
ON APS_BOM_RAW(BatchNo, ParentMaterialCode);

CREATE NONCLUSTERED INDEX IX_BOM_RAW_LLC 
ON APS_BOM_RAW(BatchNo, LLC, IsLeaf);
```

---

#### **5.2.2 物料映射表（SCD Type 2）**

```sql
-- ⚠️ 2026-04-03审计修正：以DDL v4.0为准，ERP_MasterID/MES_ID/ERP_Warehouse
--    统一为 SourceID + Warehouse + Source，消除 ERP/MES 字段分叉
CREATE TABLE MaterialMapping (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialCode NVARCHAR(50) NOT NULL,              -- 核心业务键
    SourceID INT NULL,                               -- v4.0统一：ERP的MasterID / MES的MES_ID
    Warehouse NVARCHAR(50) NULL,                     -- v4.0统一：ERP仓库 / MES库位
    Source NVARCHAR(20) NOT NULL,                    -- ERP/MES
    ValidFrom DATETIME2 NOT NULL DEFAULT GETDATE(),  -- 生效时间
    ValidTo DATETIME2 NULL,                          -- 失效时间（NULL表示当前有效）
    IsCurrent BIT NOT NULL DEFAULT 1,                -- 是否当前有效版本
    Warehouse_Norm AS (ISNULL(Warehouse, 'N/A')) PERSISTED, -- 持久化计算列，用于唯一索引
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- v4.0简化：当前有效版本索引（6列→4列）
CREATE UNIQUE INDEX IX_MaterialMapping_Current 
ON MaterialMapping(MaterialCode, Source, Warehouse_Norm, IsCurrent) 
WHERE IsCurrent = 1;

-- 历史版本索引
CREATE INDEX IX_MaterialMapping_History 
ON MaterialMapping(MaterialCode, ValidFrom, ValidTo);

-- 时间点查询索引
CREATE INDEX IX_MaterialMapping_TimePoint 
ON MaterialMapping(MaterialCode, ValidFrom, ValidTo, IsCurrent);
```

---

#### **5.2.3 统一库存可用规则表**（v5.0.39 替代旧 InventorySourcePriority）

> ⚠️ **v5.0.39 已删除**：`InventorySourcePriority`（v2.8废弃）、`ProductFamilyInventoryScope`、`InventorySourceRule` 均已删除。V1 统一使用 `InventoryAvailabilityRule`。

```sql
-- v5.0.39 新增：统一库存可用规则（详见 DDL §2.5）
CREATE TABLE InventoryAvailabilityRule (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    ProductFamilyId INT           NOT NULL,  -- 库存使用上下文产品族（非物料自身产品族）
    FactoryId       INT           NOT NULL,  -- 规则所属工厂
    MaterialCodePattern NVARCHAR(100) NULL,  -- LIKE通配；NULL=通配全物料
    SourceSystem    NVARCHAR(20)  NOT NULL,  -- ERP / MES
    StorageCode     NVARCHAR(50)  NOT NULL,  -- V1 统一使用 WarehouseCode
    IsAvailable     BIT           NOT NULL DEFAULT 1,  -- 1=允许；0=排除
    Priority        INT           NOT NULL DEFAULT 100, -- 扣减优先级，数值越小越优先
    IsActive        BIT           NOT NULL DEFAULT 1,
    Remark          NVARCHAR(500) NULL,
    CreatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
```

---

#### **5.2.4 库存事实表（双源）** ⚠️ **补充日期：2026-03-25**

**架构说明**：采用六层库存架构，从事实层到可用库存层，确保物理追溯能力和业务规则筛选。六层依次为：①`InventoryFact_ERP/MES`（事实层）→ ②`InventorySupplyCandidate`（候选池）→ ③`InventoryAvailabilityRule`（规则裁决）→ ④`InventoryAvailableSupplyDetail`（**规则命中后、汇总前的明细层**；保留 `AvailabilityRuleId` / `RulePriority` / `InventorySupplyCandidateId`）→ ⑤`InventoryBalance`（汇总余额）→ ⑥内存预加载（排程可用库存）。

##### **5.2.4.1 ERP库存事实表**

```sql
-- ⚠️ v5.0.39更新：Warehouse→WarehouseCode；新增FactoryCode（来自ext_ERP_Inventory_View）
CREATE TABLE InventoryFact_ERP (
    Id           BIGINT PRIMARY KEY IDENTITY(1,1),
    MasterID     INT           NOT NULL,             -- ERP物理主键（保留用于物理追溯）
    WarehouseCode NVARCHAR(50) NOT NULL,             -- ERP仓库；V1统一字段名（原Warehouse）
    FactoryCode  NVARCHAR(50)  NULL,                 -- v5.0.39新增；来自ERP_Inventory_View；用于映射FactoryId
    Quantity     DECIMAL(18,4) NOT NULL,             -- 库存数量
    SyncedAt     DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_Inventory_ERP UNIQUE (MasterID, WarehouseCode, FactoryCode)  -- v5.0.40: 补 FactoryCode
);

CREATE INDEX IX_InventoryFact_ERP_Query
    ON InventoryFact_ERP(MasterID, WarehouseCode)
    INCLUDE (Quantity, FactoryCode, SyncedAt);
```

**架构原则**（v5.0.39）：保留源系统物理真相（MasterID + WarehouseCode + FactoryCode）；不直接存MaterialCode；MaterialCode的统一挂接由MaterialMapping负责；FactoryCode用于步骤3中映射FactoryId。

---

##### **5.2.4.2 MES库存事实表**

```sql
-- ⚠️ v5.0.39更新：新增WarehouseCode（V1主链）+ FactoryCode；Location字段名历史保留
-- V1主链：MES_Inventory_View.WarehouseCode写入Location和WarehouseCode两个字段
-- LocationCode仅追溯，不参与V1主链计算
CREATE TABLE InventoryFact_MES (
    Id           BIGINT PRIMARY KEY IDENTITY(1,1),
    MES_ID       INT           NOT NULL,             -- MES物理主键（保留用于物理追溯）
    Location     NVARCHAR(50)  NOT NULL,             -- 字段名历史保留；V1写入MES_Inventory_View.WarehouseCode
    WarehouseCode NVARCHAR(50) NULL,                 -- v5.0.39新增；V1主链字段=MES_Inventory_View.WarehouseCode
    FactoryCode  NVARCHAR(50)  NULL,                 -- v5.0.39新增；来自MES_Inventory_View；用于映射FactoryId
    Quantity     DECIMAL(18,4) NOT NULL,             -- 库存数量
    SyncedAt     DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_Inventory_MES UNIQUE (MES_ID, WarehouseCode, FactoryCode)  -- v5.0.40: 改为主链字段
);

CREATE INDEX IX_InventoryFact_MES_Query
    ON InventoryFact_MES(MES_ID, WarehouseCode, FactoryCode)
    INCLUDE (Quantity, Location, SyncedAt);  -- v5.0.40: Location 降为 INCLUDE（历史追溯）
```

**架构原则**（v5.0.39）：保留源系统物理真相（MES_ID + Location历史保留 + WarehouseCode主链 + FactoryCode）；V1主链使用WarehouseCode；LocationCode仅作物理追溯；MaterialCode统一挂接由MaterialMapping负责。

---

##### **5.2.4.3 库存候选供给池** ⚠️ **v2.8新增 - 补充日期：2026-03-25**

```sql
-- ⚠️ 2026-04-03审计修正：以DDL为准，使用SourceSystem/StorageCode/IsEligible/RejectReason
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
    
    -- 筛选状态（白名单模式：初始 0；命中 IsAvailable=1 规则后回标 1）
    IsEligible BIT NOT NULL DEFAULT 0,               -- 是否可用（规则筛选后；默认不可用）
    RejectReason NVARCHAR(500),                      -- 被剔除原因
    
    -- 时间戳
    SyncedAt DATETIME2 NOT NULL,                     -- 同步时间
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_InventorySupplyCandidate_Material 
ON InventorySupplyCandidate(MaterialCode, f.Id, SourceSystem);

CREATE INDEX IX_InventorySupplyCandidate_Eligible 
ON InventorySupplyCandidate(MaterialCode, f.Id, IsEligible) 
WHERE IsEligible = 1;
```

**架构定位**：库存链路中第一次正式形成统一MaterialCode的候选供给层。InventoryFact_ERP/MES保留物理主键，本表通过MaterialMapping进行物理身份挂接，是库存链路中第一次真正进入APS统一业务语义的地方。

---

##### **5.2.4.4 统一库存可用规则表**（v5.0.39 替代旧 §5.2.4.4 + §5.2.4.5）

> ⚠️ **v5.0.39 删除**：`ProductFamilyInventoryScope`（旧§5.2.4.4）和 `InventorySourceRule`（旧§5.2.4.5，含 `RuleAction PREFER/EXCLUDE`）已正式删除。V1 统一使用 `InventoryAvailabilityRule`，一张表同时回答"哪些仓可用"和"扣减优先级"两个问题。

```sql
-- v5.0.39 新增：统一库存可用规则
-- ProductFamilyId = 库存使用上下文（非物料自身产品族）
-- IsAvailable=1 允许进入可用库存池；IsAvailable=0 主动排除；Priority 扣减优先级（越小越优先）
-- StorageCode V1 统一使用 WarehouseCode；无匹配规则 → 不进 InventoryBalance，写 NoRuleMatch WARN
CREATE TABLE InventoryAvailabilityRule (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    ProductFamilyId INT           NOT NULL FK -> ProductFamily(Id),
    FactoryId       INT           NOT NULL FK -> Factory(Id),
    MaterialCodePattern NVARCHAR(100) NULL,   -- LIKE通配；NULL=通配全物料
    SourceSystem    NVARCHAR(20)  NOT NULL,   -- ERP / MES
    StorageCode     NVARCHAR(50)  NOT NULL,   -- V1 统一使用 WarehouseCode
    IsAvailable     BIT           NOT NULL DEFAULT 1,   -- 1=允许；0=排除
    Priority        INT           NOT NULL DEFAULT 100, -- 数值越小越优先
    IsActive        BIT           NOT NULL DEFAULT 1,
    Remark          NVARCHAR(500) NULL,
    CreatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_InventoryAvailabilityRule_Context
ON InventoryAvailabilityRule(ProductFamilyId, f.Id, SourceSystem, StorageCode, IsActive);

CREATE INDEX IX_InventoryAvailabilityRule_Priority
ON InventoryAvailabilityRule(ProductFamilyId, f.Id, Priority);
```

**业务用途**（v5.0.39）：定义某工厂+产品族上下文下，哪些 SourceSystem+StorageCode 组合允许进入可用库存池，以及扣减优先级。`sp_SyncInventorySnapshot` 步骤4消费本表筛选 `InventorySupplyCandidate`。

---

##### **5.2.4.5 可用供给明细层（v5.0.40 新增）**

> **`InventoryAvailableSupplyDetail` = 规则命中后、余额汇总前的可用库存明细层**

```sql
-- v5.0.40 新增
-- 定位：规则命中后、汇总前的可用库存明细层
-- 生成：InventorySupplyCandidate 经 InventoryAvailabilityRule 裁决后写入（Step 4c）
-- 用途：保留 SourceSystem / StorageCode / AvailabilityRuleId / RulePriority / InventorySupplyCandidateId
--       InventoryBalance 从本表按 (MaterialCode, ProductFamilyId, FactoryId) 汇总生成（Step 5）
-- ❌ InventorySupplyCandidateId 不加 FK（加 FK 会阻塞 TRUNCATE TABLE InventorySupplyCandidate）
CREATE TABLE InventoryAvailableSupplyDetail (
    Id                        BIGINT        PRIMARY KEY IDENTITY(1,1),
    BatchNo                   NVARCHAR(50)  NOT NULL,
    MaterialCode              NVARCHAR(50)  NOT NULL,
    ProductFamilyId           INT           NOT NULL,   -- 来源：InventoryAvailabilityRule.ProductFamilyId
    FactoryId                 INT           NOT NULL,
    SourceSystem              NVARCHAR(20)  NOT NULL,   -- ERP / MES
    StorageCode               NVARCHAR(50)  NOT NULL,   -- V1 统一使用 WarehouseCode
    Quantity                  DECIMAL(18,4) NOT NULL,
    AvailabilityRuleId        BIGINT        NOT NULL,   -- FK -> InventoryAvailabilityRule(Id)
    RulePriority              INT           NOT NULL,   -- 扣减优先级，数值越小越优先
    ERP_MasterID              INT           NULL,
    MES_ID                    INT           NULL,
    InventorySupplyCandidateId BIGINT       NULL,       -- 逻辑追溯，不加 FK
    CreatedAt                 DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_IASD_ProductFamily FOREIGN KEY (ProductFamilyId) REFERENCES ProductFamily(Id),
    CONSTRAINT FK_IASD_Factory       FOREIGN KEY (FactoryId)       REFERENCES Factory(Id),
    CONSTRAINT FK_IASD_Rule          FOREIGN KEY (AvailabilityRuleId) REFERENCES InventoryAvailabilityRule(Id)
);

CREATE INDEX IX_IASD_Deduction ON InventoryAvailableSupplyDetail(MaterialCode, ProductFamilyId, f.Id, RulePriority);
CREATE INDEX IX_IASD_Batch     ON InventoryAvailableSupplyDetail(BatchNo);
CREATE INDEX IX_IASD_Candidate ON InventoryAvailableSupplyDetail(InventorySupplyCandidateId);
```

**架构定位**：

| 表 | 负责回答 |
|---|---|
| `InventoryBalance` | 总量够不够（`MaterialCode + ProductFamilyId + FactoryId` 汇总） |
| `InventoryAvailableSupplyDetail` | 这些总量从哪里来、按什么顺序扣（仓库级/来源级/规则级/优先级） |

**❗ 红线**：
- `InventorySupplyCandidateId` 不加外键，只建 `IX_IASD_Candidate` 普通索引（加 FK 会阻塞 `TRUNCATE TABLE InventorySupplyCandidate`）
- `RulePriority` 必须保留在本表；`InventoryBalance` 不承接仓库级扣减顺序
- 本表不是订单消耗明细表；订单消耗由 `InventoryAllocationResult` 承接（V1.1/V2 预留）
- `ProductFamilyId` 来自规则输出 = 库存使用上下文，**不等于物料自身产品族**

---

##### **5.2.4.6 库存余额表（规则筛选后）** ⚠️ **v2.8新增 - 补充日期：2026-03-25**

```sql
CREATE TABLE InventoryBalance (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    
    -- 核心业务键（增加产品族上下文）
    MaterialCode NVARCHAR(50) NOT NULL,
    ProductFamilyId INT NOT NULL FOREIGN KEY REFERENCES ProductFamily(Id),
    FactoryId INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    
    -- 库存数量
    OnHandQty DECIMAL(18,4) NOT NULL,                -- 现有量
    AllocatedQty DECIMAL(18,4) NOT NULL DEFAULT 0,   -- V1兼容列：固定0；排程分配不回写本表
    AvailableQty AS (OnHandQty - AllocatedQty) PERSISTED, -- V1等于OnHandQty；运行时扣减在Domain内存余额完成
    
    -- 来源与批次
    Source NVARCHAR(20) NOT NULL,                    -- ERP/MES/BOTH
    BatchNo NVARCHAR(50) NULL,                       -- 库存快照批次标签（由sp_SyncInventorySnapshot写入；非订单消耗记录）
    
    -- 时间戳
    LastUpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    -- 唯一约束（增加产品族维度）
    CONSTRAINT UQ_Inventory_Balance UNIQUE (MaterialCode, ProductFamilyId, FactoryId)
);

CREATE INDEX IX_InventoryBalance_Query 
    ON InventoryBalance(MaterialCode, ProductFamilyId, FactoryId) 
    INCLUDE (OnHandQty, AllocatedQty, Source);

CREATE INDEX IX_InventoryBalance_Batch 
    ON InventoryBalance(BatchNo) 
    WHERE BatchNo IS NOT NULL;
```

**架构定位**：规则筛选后的排程可用库存结果表。从"通用余额表"改造为"带产品族上下文的规则筛选结果表"，是排程唯一真相，不再是简单的MaterialCode+FactoryId聚合。

**六层库存架构总结**（v5.0.40更新）：
1. **事实层**：`InventoryFact_ERP`（WarehouseCode+FactoryCode）、`InventoryFact_MES`（WarehouseCode主链+Location历史保留+FactoryCode）
2. **候选供给池**：`InventorySupplyCandidate`（通过 `MaterialMapping`（含 `Warehouse_Norm`）统一到 MaterialCode+FactoryId；IsEligible=0 初始）
3. **规则裁决**：`InventoryAvailabilityRule`（胜出规则模式：含 IsAvailable=0/1；精确 > 通配；Priority ASC；替代旧 ProductFamilyInventoryScope+InventorySourceRule，v5.0.39删除）
4. **可用供给明细层**：`InventoryAvailableSupplyDetail`（规则命中后、汇总前；保留 `AvailabilityRuleId` + `RulePriority` + `InventorySupplyCandidateId`；`ProductFamilyId` 来源：规则输出；供排程引擎按优先级扣减；v5.0.40新增）
5. **余额汇总层**：`InventoryBalance`（从明细层按 MaterialCode+ProductFamilyId+FactoryId 汇总；`ProductFamilyId`=库存使用上下文；`BatchNo`=快照批次标签；TRUNCATE全量替换）
6. **内存预加载层**：排程前一次性全量预加载进内存；`sp_SyncInventorySnapshot(@BatchNo)` 驱动；需要扣减顺序时读取 `InventoryAvailableSupplyDetail`（详见§2.4.5）

---

#### **5.2.5 物料供给与责任上下文表** ⚠️ **v2.7新增 - 补充日期：2026-03-25**

```sql
-- ⚠️ 2026-04-03审计修正：以DDL为准，移除SourceSystem DEFAULT，加InventoryManagementMode，加供给方式索引
CREATE TABLE MaterialSupplyContext (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    
    -- 核心业务键
    MaterialCode NVARCHAR(50) NOT NULL,              -- 物料编码（统一业务键）
    WarehouseCode NVARCHAR(50) NOT NULL,             -- 仓库编码（关键维度）
    FactoryId INT NULL,                              -- 工厂ID（可选）
    
    -- 供给方式与责任归属
    SupplyMode NVARCHAR(20) NOT NULL,                -- PURCHASE/MAKE/OUTSOURCE/MIXED
    DefaultProductionDeptCode NVARCHAR(50) NULL,     -- 默认生产责任部门编码
    ProcurementDeptCode NVARCHAR(50) NULL,           -- 采购责任部门编码（APS维护）
    OutsourceDeptCode NVARCHAR(50) NULL,             -- 委外责任部门编码
    
    -- 计划参数（仓库级）
    LeadTimeDays INT NULL,                           -- 该上下文提前期（天）
    SafetyStock DECIMAL(18,4) NULL,                  -- 该仓安全库存
    InventoryManagementMode NVARCHAR(20) NULL,       -- 库存管理方式：STOCKED / NON_STOCKED（v4.0新增）
    
    -- 数据来源与版本控制（SCD Type 2）
    SourceSystem NVARCHAR(20) NOT NULL,              -- ERP/MES（v4.0：双源同构，移除默认值）
    ValidFrom DATETIME2 NOT NULL,                    -- 生效时间
    ValidTo DATETIME2 NULL,                          -- 失效时间（NULL表示当前有效）
    IsCurrent BIT NOT NULL DEFAULT 1,                -- 当前是否有效
    
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE UNIQUE INDEX IX_MaterialSupplyContext_Current 
ON MaterialSupplyContext(MaterialCode, WarehouseCode, IsCurrent) 
WHERE IsCurrent = 1;

CREATE INDEX IX_MaterialSupplyContext_History 
ON MaterialSupplyContext(MaterialCode, WarehouseCode, ValidFrom, ValidTo);

CREATE INDEX IX_MaterialSupplyContext_SupplyMode 
ON MaterialSupplyContext(SupplyMode, IsCurrent) 
WHERE IsCurrent = 1;
```

**核心理念**：同一物料在不同仓库下，业务语义会变化（采购/自制、生产部门等）。架构定位：承载"仓库级业务上下文"，而非"物料本体属性"。

---

#### **5.2.6 PlanVersion表增强**

```sql
-- 为现有PlanVersion表增加快照相关字段
ALTER TABLE PlanVersion ADD BatchNo NVARCHAR(50) NULL;
ALTER TABLE PlanVersion ADD SnapshotFilePath NVARCHAR(500) NULL;
ALTER TABLE PlanVersion ADD SnapshotFileSize BIGINT NULL;
ALTER TABLE PlanVersion ADD SnapshotFileHash NVARCHAR(64) NULL;  -- SHA256
ALTER TABLE PlanVersion ADD SnapshotCompressedSize BIGINT NULL;
ALTER TABLE PlanVersion ADD SnapshotCreatedAt DATETIME2 NULL;

-- 创建索引
CREATE INDEX IX_PlanVersion_Snapshot 
ON PlanVersion(SnapshotCreatedAt DESC) 
WHERE SnapshotFilePath IS NOT NULL;

CREATE INDEX IX_PlanVersion_Batch 
ON PlanVersion(BatchNo) 
WHERE BatchNo IS NOT NULL;
```

---

#### **5.2.7 Pegging与跨版本执行对象（v1.36新增，结构摘要）**

> 本节只冻结物理对象边界和关键索引方向；完整字段、类型、CHECK、FK和建表顺序以随后同步的《数据库字段说明》与DDL为准。

| 对象 | 关键隔离/业务键 | 关键索引方向 |
|---|---|---|
| `ProductionInstructionSupplySnapshot` | `ScheduleRunId + DomainKey + ProductionInstructionNo` | 运行域唯一键；PI查询；异常闭合查询 |
| `ProductionInstructionPositionSlice` | `SupplySnapshotId + PositionType + PositionCode + StableSourceKey` | Snapshot下位置顺序；来源业务键；剩余路径 |
| `PeggingAllocationLedger` | `PlanVersionId + AllocationSequence`唯一，并保存DemandKey/SupplyKey | Demand余额追溯；Supply余额追溯；AllocatedQty；FinalTaskId+TaskComponentQty；AllocationMode |
| `PeggingSupplyAllocation` | `PlanVersionId + LedgerId + SupplyType + StableSourceKey` | 非Task来源去重；需求/Task查询；来源单据查询 |
| `ExecutionLock` | 稳定MES执行键（建议`SourceSystem + MESWorkOrderNo + StageCode`） | Active状态；PI+Stage；MES工单唯一性 |
| `DemandSupplyHardLock` | `ExecutionLockId/StableSupplyKey + DemandOrderCanonicalId + Active状态` | 原需求恢复；HardLock有效性；数量校验 |
| `Task`新增字段 | `ExecutionLockId NULL`、`IsVirtual` | PlanVersion+ExecutionLock；禁止虚拟Task下发 |
| `vw_TaskDemandAllocation` | 由Ledger按`FinalTaskId + DemandOrderCanonicalId`汇总`TaskComponentQty` | 页面、MES追溯和摘要查询；禁止汇总AllocatedQty |

**明确不新增**：独立SoftAllocation表、DemandPromiseConstraint表、ExecutionLockTaskLink表。SOFT/HARD是Ledger分配属性；交期承诺由Order+规则/审批约束承载；跨版本Task关系由 `Task.ExecutionLockId` 直接表达。

**批量落盘建议**：上述运行结果均先写临时/Stage表，再在Domain事务内执行集合校验和正式写入。对Ledger与PositionSlice应避免过多宽文本列，来源说明可使用短码+必要追溯键，详细解释写入Issue/Explanation对象。

---

#### **5.2.8 ETL日志表**

```sql
CREATE TABLE APS_ETL_Log (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    Step NVARCHAR(100) NOT NULL,                     -- CalculateLLC/SyncMapping/LoadInventory
    Message NVARCHAR(MAX),
    Status NVARCHAR(20) NOT NULL DEFAULT 'SUCCESS',  -- SUCCESS/FAILED
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_ETL_Log_Batch ON APS_ETL_Log(BatchNo, Step, CreatedAt);
```

---

## 第六部分：接口设计规范

### 6.1 ODS库对外接口

#### **6.1.1 批次BOM展开接口**

**接口名称**：`sp_RequestBOMExpansion`

**输入参数**：
- `@BatchNo`：批次号
- `@BOMNOList`：BOMNO列表（Table-Valued Parameter）

**输出参数**：
- `@RootCount`：活跃根数量
- `@Status`：请求状态

**调用示例**：
```sql
DECLARE @BOMNOList AS TABLE (BOMNO NVARCHAR(50));
INSERT INTO @BOMNOList VALUES ('BOM-001'), ('BOM-002'), ('BOM-003');

DECLARE @RootCount INT;
DECLARE @Status NVARCHAR(20);

EXEC sp_RequestBOMExpansion 
    @BatchNo = 'REQ_20260310_01',
    @BOMNOList = @BOMNOList,
    @RootCount = @RootCount OUTPUT,
    @Status = @Status OUTPUT;

SELECT @RootCount AS RootCount, @Status AS Status;
```

---

#### **6.1.2 批次状态查询接口**

**接口名称**：`sp_GetBOMRequestStatus`

**输入参数**：
- `@BatchNo`：批次号

**输出参数**：
- `@Status`：请求状态
- `@ExpandedRowCount`：展开行数
- `@CompletedAt`：完成时间

**调用示例**：
```sql
DECLARE @Status NVARCHAR(20);
DECLARE @ExpandedRowCount INT;
DECLARE @CompletedAt DATETIME2;

EXEC sp_GetBOMRequestStatus 
    @BatchNo = 'REQ_20260310_01',
    @Status = @Status OUTPUT,
    @ExpandedRowCount = @ExpandedRowCount OUTPUT,
    @CompletedAt = @CompletedAt OUTPUT;

SELECT @Status AS Status, @ExpandedRowCount AS ExpandedRowCount, @CompletedAt AS CompletedAt;
```

---

#### **6.1.3 实时BOM展开接口（v1.34）**

##### **6.1.3.1 物理SP签名（DDL v5.2.2权威）**

**SP 名称**：`sp_ExpandBOMRealtime_vNext`

**物理参数**（DDL 层，两参数均可空，但**不能同时为空**）：
- `@BOMNO NVARCHAR(50) = NULL`：可选 BOM 编号
- `@RequestDetailId BIGINT = NULL`：可选订单级追溯锚点

**物理层校验**：SP 内部检查 `@BOMNO IS NULL AND @RequestDetailId IS NULL` → `RAISERROR('@BOMNO 和 @RequestDetailId 不能同时为 NULL。', 16, 1)`。

---

##### **6.1.3.2 v1.34 正式业务契约（RequestDetail 入口）**

**正式路径**：白天实时评估与局部重排必须传入非空 `@RequestDetailId`。`@BOMNO` 可空（无则由 SP 解析 `MAT:{MaterialCode}`）。

**调用职责链**：
- **3号位** 归一化 `ScopeJson` 并创建 `ScheduleRun`（详见 §2.7.2）
- **2号位** 检查 BOM 切片能否复用；不可复用则创建 `MES_API_BOM_Request_Detail`，取得 `RequestDetailId`，实际调用 `sp_ExpandBOMRealtime_vNext(@RequestDetailId=...)`
- SP 内部调用 `sp_EnrichBOMWorksetRealtime(@ResolvedBOMNO, @RequestDetailId)` 完成回填
- `sp_EnrichBOMWorksetRealtime` 在 Step 5 日志前调用 `sp_GenerateBOMCrossFactoryEdgeRealtime(@BOMNO, @RequestDetailId)`
- 完成后 `MES_API_BOM_Request_Realtime.Status='READY'`（唯一权威）；`ExpandedRowCount` 回填（仅诊断）

**BOMNO-only 调用（deprecated）**：仅 `@BOMNO` 非空、`@RequestDetailId=NULL` 的调用形式是 v5.0.26 之前的旧兼容路径，v1.34 正式路径**不承认**该形式作为业务入口。

**调用示例**（正式）：
```sql
-- 3号位创建 ScheduleRun 后，2号位取得 @RequestDetailId 调用
EXEC dbo.sp_ExpandBOMRealtime_vNext
    @RequestDetailId = 102385;

-- READY 查询（TOP 1 + ORDER BY Id DESC，取最新记录）
SELECT TOP (1) Id, Status, ErrorMessage, CompletedTime, ExpandedRowCount
FROM MES_API_BOM_Request_Realtime
WHERE RequestDetailId = 102385
ORDER BY Id DESC;
```

---

##### **6.1.3.deprecated 旧版 sp_RequestRealtimeBOMExpansion（v1.34 已废弃，仅供追溯）**

> ⚠️ 旧版 `sp_RequestRealtimeBOMExpansion(@BOMNO)` 已从 v5.0.26 起由 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` 替代；v1.34 正式路径**只承认 RequestDetail 入口**，本节仅供历史追溯，**不作为正式接口**。
>
> 旧接口签名（deprecated）：
> - `@BOMNO NVARCHAR(50)`（唯一入口）
> - `@Priority INT`（0=普通，1=紧急）
> - `@RequestId BIGINT OUTPUT` / `@Status NVARCHAR(20) OUTPUT`

---

### 6.2 APS本地库接口

#### **6.2.1 BOM数据拉取接口**

**接口名称**：`sp_PullBOMFromODS`

**输入参数**：
- `@BatchNo`：批次号

**输出参数**：
- `@RowCount`：拉取行数
- `@Status`：拉取状态

**调用示例**：
```sql
DECLARE @RowCount INT;
DECLARE @Status NVARCHAR(20);

EXEC sp_PullBOMFromODS 
    @BatchNo = 'REQ_20260310_01',
    @RowCount = @RowCount OUTPUT,
    @Status = @Status OUTPUT;

SELECT @RowCount AS RowCount, @Status AS Status;
```

---

#### **6.2.2 LLC计算接口**

**接口名称**：`sp_CalculateLLC`

**输入参数**：
- `@BatchNo`：批次号

**输出参数**：
- `@MaxLevel`：最大层级
- `@Status`：计算状态

**详见2.4.1节**

---

#### **6.2.3 物料映射同步接口**（2026-04-01 更新）

**接口名称**：
- `sp_SyncERPMasterData`：ERP主数据三表协同同步（原 `sp_SyncMaterialMapping`）
- `sp_SyncMESMaterialData`：MES物料映射同步（从原 `sp_SyncMaterialMapping` 拆出）

**`sp_SyncERPMasterData` 参数**：
- 输入：`@BatchNo NVARCHAR(50) = 'DAILY'`
- 输出：`@RowsAffected INT OUTPUT`、`@ErrorMessage NVARCHAR(MAX) OUTPUT`

**`sp_SyncMESMaterialData` 参数**：无

**详见2.4.2节及《APS_ERP主数据三表协同同步设计_v1.0》**

---

## 第七部分：方案综合评估

### 7.1 核心架构优势（Pros）

#### **优势1：性能极客**

**批次推送  ODS集合化计算  批量端盘子**：
- ✅ 彻底消灭了N+1网络I/O毒药
- ✅ 完美化解了80万混合规则BOM的展开绝症
- ✅ 15分钟内完成10万级Task排程

**性能数据**：
- 批次BOM展开：80万BOMNO  350万行，耗时约**15分钟**
- SqlBulkCopy拉取：350万行，耗时约**5分钟**
- LLC计算：350万行，耗时约**5分钟**
- 总耗时：约**25分钟**（满足30分钟窗口）

---

#### **优势2：绝对的安全隔离**

**ODS库保护MES生产库**：
- ✅ BOM递归展开不影响车间报工
- ✅ 物理隔离，互不干扰

**批次状态机**：
- ✅ 彻底杜绝了分布式系统的"拉错批次"和"重复展开"灾难
- ✅ BatchNo幂等性保证

---

#### **优势3：降维打击的追溯力**

**快照封存**：
- ✅ 摒弃了SQL Server中复杂庞大的历史拉链表
- ✅ 用极其廉价的4.76T硬盘文件快照
- ✅ 换来了极其高可信的审计追溯能力

**成本对比**：
- 传统拉链表：需要维护10+张历史表，查询复杂，性能差
- 快照封存：单个快照文件约**50MB**（压缩后），查询简单，性能优

---

#### **优势4：防腐层三重防护**

**视图/ODS契约防腐**：
- ✅ ERP换代时APS零代码修改

**MaterialCode业务主键**：
- ✅ 彻底摆脱ERP/MES物理ID依赖

**SCD Type 2拉链表**：
- ✅ 历史追溯完美支持

---

### 7.2 潜在风险与弥补措施（Cons & Mitigations）

#### **风险1：人工实时评估时 BOM 切片尚未准备的盲区**（v1.34 重写）

**问题**：
- 80 万棵树的批量 BOM 展开在夜间 00:00 完成；夜间快照未覆盖白天新增或变化订单
- 当 PMC 在白天发起 CTP / 插单影响分析 / 局部重排时，若目标订单不在夜间 Workset 中，2号位需要额外触发实时 BOM 展开，会引入 5 分钟量级的延迟

**弥补措施（v1.34 人工触发主路径）**：
- ✅ 白天 ERP 增量同步只更新 `Order_Canonical`（不检测缓存、不写 RequestDetail、不触发实时展开）
- ✅ 4号位页面向 PMC 展示新增/变化订单和推荐清单
- ✅ PMC 人工发起业务场景后，**3号位** 归一化 ScopeJson 并创建 ScheduleRun（详见 §2.7.2）
- ✅ **2号位** 检查目标订单是否已有可复用 BOM 切片；不可复用则创建 `MES_API_BOM_Request_Detail` 并以 `RequestDetailId` 调用 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)`（详见 §2.7.4）
- ✅ 单次请求目标 5 分钟内完成；READY 权威以 `MES_API_BOM_Request_Realtime.Status='READY'` 为准


---

#### **风险2：ETL脚本复杂度激增**

**问题**：
- 需要MES团队的DBA具备极高的业务理解力
- 写出包含CTE、拉链表逻辑、批次状态机流转的高难度存储过程

**弥补措施**：
- ✅ 提供详细的存储过程模板（详见本文档）
- ✅ 2号位（技术负责人）负责ODS库开发
- ✅ 充分的单元测试和集成测试

---

#### **风险3：SSD容量不足**

**问题**：
- 1TB SSD需要同时承载：
  - ODS库数据文件
  - APS本地库数据文件
  - 日志文件
  - tempdb

**弥补措施**：
- ✅ 定期清理ODS库历史批次（7天前只保留最后一次成功批次）
- ✅ APS本地库只保留最近3个版本的数据
- ✅ 监控SSD使用率，预警机制

---

#### **风险4：机械硬盘I/O瓶颈**

**问题**：
- 快照封存到4.76T机械硬盘
- 机械硬盘的写入速度可能成为瓶颈

**弥补措施**：
- ✅ 快照封存使用**异步写入**，不阻塞排程
- ✅ 快照文件使用**gzip压缩**，减少I/O
- ✅ 单个快照文件约50MB，写入耗时约**5秒**（可接受）

---

### 7.3 方案适用性评估

#### **适用场景**：
- ✅ 海量BOM数据（千万级以上）
- ✅ 复杂的一物多BOM场景
- ✅ ERP/MES系统频繁变更
- ✅ 需要历史追溯和版本对比
- ✅ 紧急插单频繁

#### **不适用场景**：
- ❌ BOM数据量小（百万级以下）：过度设计
- ❌ 无ERP换代风险：防腐层收益不明显
- ❌ 无历史追溯需求：快照封存多余

---

## 第八部分：实施路线图

### 8.1 第一阶段：基础设施准备（1周）

**任务清单**：
- [ ] 购买1TB SSD
- [ ] 虚拟化配置（VM1/VM2/VM3）
- [ ] 创建MES_Integration ODS库
- [ ] 创建APS本地库增强表

**责任人**：2号位（技术负责人）

---

### 8.2 第二阶段：ODS库开发（2周）

**任务清单**：
- [ ] 开发批次BOM展开存储过程
- [ ] 开发实时BOM展开存储过程
- [ ] 开发批次清理存储过程
- [ ] 单元测试和性能测试

**责任人**：2号位 + MES团队DBA

---

### 8.3 第三阶段：APS本地库开发（2周）

**任务清单**：
- [ ] 开发BOM拉取逻辑
- [ ] 开发LLC计算逻辑
- [ ] 开发MaterialMapping同步逻辑
- [ ] 开发库存双源汇聚逻辑
- [ ] 单元测试和集成测试

**责任人**：2号位

---

### 8.4 第四阶段：快照封存开发（1周）

**任务清单**：
- [ ] 开发快照序列化逻辑
- [ ] 开发快照压缩逻辑
- [ ] 开发快照读取逻辑
- [ ] 开发快照完整性校验逻辑
- [ ] 单元测试

**责任人**：2号位

---

### 8.5 第五阶段：集成测试（2周）

**任务清单**：
- [ ] 端到端测试（00:00  02:15完整流程）
- [ ] 紧急插单测试
- [ ] 性能测试（80万BOMNO）
- [ ] 压力测试
- [ ] 故障恢复测试

**责任人**：全体开发团队

---

### 8.6 第六阶段：试运行（2周）

**任务清单**：
- [ ] 与现有系统并行运行
- [ ] 数据对比验证
- [ ] 性能监控
- [ ] 问题修复

**责任人**：全体开发团队

---

### 8.7 第七阶段：正式上线（1周）

**任务清单**：
- [ ] 切换到新系统
- [ ] 关闭旧系统
- [ ] 724小时监控
- [ ] 应急预案准备

**责任人**：全体开发团队

---

## 附录A：关键SQL脚本

### A.1 ODS库初始化脚本

```sql
-- 创建ODS库
CREATE DATABASE MES_Integration
ON PRIMARY 
(
    NAME = MES_Integration_Data,
    FILENAME = 'E:\SSD\MES_Integration.mdf',  -- SSD路径
    SIZE = 5GB,
    MAXSIZE = UNLIMITED,
    FILEGROWTH = 1GB
)
LOG ON 
(
    NAME = MES_Integration_Log,
    FILENAME = 'E:\SSD\MES_Integration_log.ldf',  -- SSD路径
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

-- 创建表（详见5.1节）
-- ...
```

---

### A.2 APS本地库增强脚本

```sql
USE APS_Production;
GO

-- 创建新表（详见5.2节）
-- ...

-- 为PlanVersion表增加快照字段
ALTER TABLE PlanVersion ADD BatchNo NVARCHAR(50) NULL;
ALTER TABLE PlanVersion ADD SnapshotFilePath NVARCHAR(500) NULL;
ALTER TABLE PlanVersion ADD SnapshotFileSize BIGINT NULL;
ALTER TABLE PlanVersion ADD SnapshotFileHash NVARCHAR(64) NULL;
ALTER TABLE PlanVersion ADD SnapshotCompressedSize BIGINT NULL;
ALTER TABLE PlanVersion ADD SnapshotCreatedAt DATETIME2 NULL;
GO

-- 创建索引
CREATE INDEX IX_PlanVersion_Snapshot 
ON PlanVersion(SnapshotCreatedAt DESC) 
WHERE SnapshotFilePath IS NOT NULL;
GO
```

---

## 附录B：性能基准测试

### B.1 测试环境

- **服务器**：256G内存、48核96线程、1TB SSD + 4.76T HDD
- **数据规模**：80万BOMNO、350万BOM行、45万订单
- **测试时间**：2026-03-10

---

### B.2 测试结果

| 环节 | 耗时 | 备注 |
|------|------|------|
| 活跃根集合划定 | 2分钟 | 80万BOMNO |
| 批次BOM展开（ODS） | 15分钟 | 350万行 |
| BOM拉取（SqlBulkCopy） | 5分钟 | 350万行 |
| LLC计算 | 5分钟 | 350万行 |
| MaterialMapping同步 | 3分钟 | 55万物料 |
| 库存双源汇聚 | 2分钟 | 55万物料 |
| 排程推演 | 10分钟 | 10万Task |
| 快照封存 | 1分钟 | 50MB压缩文件 |
| **总耗时** | **43分钟** | **满足60分钟窗口** |

---

## 附录C：故障应急预案

### C.1 ODS库展开失败

**现象**：批次状态为FAILED

**排查步骤**：
1. 查看`MES_API_BOM_Request_Log`表
2. 检查错误消息
3. 检查是否有循环BOM
4. 检查是否有无效的BOMNO

**应急措施**：
1. 手动修复数据
2. 重新触发展开
3. 如无法修复，跳过该批次，使用前一天的BOM数据

---

### C.2 SSD容量不足

**现象**：ODS库或APS库写入失败

**排查步骤**：
1. 检查SSD使用率
2. 检查是否有历史批次未清理

**应急措施**：
1. 立即清理历史批次
2. 临时将部分数据迁移到HDD
3. 紧急扩容SSD

---

### C.3 快照文件损坏

**现象**：快照哈希校验失败

**排查步骤**：
1. 检查文件是否存在
2. 检查文件大小是否匹配
3. 检查文件哈希是否匹配

**应急措施**：
1. 从备份恢复快照文件
2. 如无备份，使用前一个版本的快照
3. 重新生成快照

---


---

**文档结束。当前基线版本 v1.35（2026-07-13），完整版本历史以前文各版变更说明为准。**
