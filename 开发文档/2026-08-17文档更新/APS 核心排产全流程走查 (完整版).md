# APS 核心排产全流程走查（完整版 V3.23）

**版本**：V3.23  
**日期**：2026-07-31  
**覆盖范围**：主流程 + 跨厂协同 + 分域计算 + 异常处理 + 人工干预 + 数据同步 + 监控容错 + 计划发布 + 跨版本执行恢复（共32个完整流程）



**v3.23更新说明**（2026-07-31 1号位纯内存接口边界澄清；对齐 演变总表 v3.38 / 防腐层 v1.42 / 集成接口 v1.32 / 字段说明与DDL v5.2.5）：

- 🔒 **TaskDraft不是数据库表**：`TaskDraft / FinalTaskDraft / ScheduledTaskDraft / AllocationShare`均为单次Domain计算中的内存DTO或领域对象，不建立物理表，不提前落库。
- 🔒 **1号位零数据库I/O**：1号位只接收2号位通过方法参数传入的内存`DomainSolveRequest`，在内存中完成有限产能、合并拆分和时间资源排定，再返回内存结果；不得直接读取或写入ODS、APS_Production或任何其他数据库。
- 🔒 **正式Task唯一落库方**：2号位负责从数据库/快照装载事实、构造`ScheduleContext`和TaskDraft、调用1号位，并在统一事务中将`FinalTaskDraft`实例化为正式`[Task]`，同时写Ledger、PSA和物理Pegging。
- 📝 **措辞统一**：后文“1号位消费/读取”均指消费2号位已装载并传入的内存对象，不代表1号位自行查询数据库；“实例化正式Task”仅指2号位持久化动作。

> **v3.23边界声明**：本轮仅消除职责措辞歧义，不改变TaskDraft接口字段、Pegging算法、统一持久化事务、数据库结构或1—5号位既定分工。

**v3.22更新说明**（2026-07-31 Pegging→有限产能→统一持久化接口收口；对齐 演变总表 v3.37 / 防腐层 v1.41 / 集成接口 v1.31 / 字段说明与DDL v5.2.4）：

- 🔧 **库存净可用量不扩字段**：5号位在APS专用ODS `ERP_Inventory_View`内直接将`Quantity`定义为`max(0, ERP总量-WasterQty)`；下游六层库存链继续只消费现有`Quantity`。ERP暂不能提供客户级专属库存数量，V1由`InventoryAvailabilityRule`整体排除无法区分客户数量的专属仓库，不新增客户或质量等级字段。
- 🔧 **SupplyBusinessKey格式冻结**：PI=`PI|生产指示号`；库存=`INV|ERP|工厂|仓库|物料`；单据供给=`DOC|ERP|单据类型|单据号|物料|目的仓库`；采购=`PO|ERP|采购单号|物料|收货仓库`；无PI历史MES工单=`EXEC|MES|工单号`。各Loader生成，Pegging循环只复制。
- 🔧 **编排顺序收口**：`SchedulingOrchestrator`继续作为单Domain总入口；Phase 1.6只生成内存TaskDraft，随后Pegging纯内存分配，再由1号位有限产能排定，最后2号位统一事务落盘。禁止先INSERT占位Task、Pegging再DELETE/重建。
- 🔧 **AllocationSequence生成规则冻结**：每个PlanVersion的一次Pegging调用使用局部递增计数器，只有需求与供给余额均扣减成功后才从1递增；单Domain内顺序分配，不使用数据库Sequence，不放入全局SchedulingContext。
- 🔧 **1号位V1最小接口冻结**：输入TaskDraft及`AllocationSequence+ComponentQty`组成份额；输出最终Task草稿、`AllocationSequence→FinalTaskDraftKey→ComponentQty`映射及原有求解摘要。类名可调整，数量份额和守恒能力不可缺失。
- 🔧 **Domain结果事务冻结**：同一显式事务内依次写Task、建立DraftKey→TaskId映射、回填并写Ledger、写非Task分配、写Task-to-Task Pegging并校验；失败整域回滚。PlanVersion激活仍是后续独立事务。

> **v3.22边界声明**：本轮不新增表、不新增库存字段、不建设新协调平台、事件溯源或锁管理平台；只把已确认的V1主链写到可直接编码程度。

**v3.21更新说明**（2026-07-30 现有代码对象映射澄清；对齐 演变总表 v3.36 / 防腐层 v1.40 / 集成接口 v1.30 / 字段说明与DDL v5.2.3）：

- 🔧 **复用现有内存记录，不新增同义类**：现有代码中的`PeggingLedgerEntry`继续作为每笔成功分配的内存记录，逻辑上承担“待持久化Ledger草稿”的职责；文档中的`PeggingAllocationLedgerDraft`不再被理解为必须新建的平行代码类。
- 🔧 **兼容现有Decision名称**：现有`PeggingRuleVoucher`可继续作为5号位返回分配判断的代码对象；在本套文档中其业务契约等同于`PeggingAllocationDecision`。本轮不强制改类名，也不新增第二套Decision/Voucher对象。
- 🔧 **三类结果职责不变**：`PeggingAllocationLedger`是数据库中的统一分配总账；`PeggingSupplyAllocation`继续只保存非Task供给的已确认分配结果；物理`Pegging`继续只保存Task-to-Task血缘。三者不是互相替代关系。
- 🔧 **ExecutionLock保持最小新增范围**：当前代码尚无`ExecutionLock`时，按DDL v5.2.3实现最小实体和跨版本关联即可；不新增`ExecutionLockTaskLink`、完整事件溯源、独立锁管理平台或新的插件框架。

> **v3.21边界声明**：本轮只澄清“现有代码对象如何承接已冻结设计”，不修改Pegging算法、数据库表结构、MES双向视图、软硬锁业务规则、Task生成时机或1—5号位职责。

**v3.20更新说明**（2026-07-30 定点补丁；对齐 演变总表 v3.35 / 防腐层 v1.39 / 集成接口 v1.29 / 字段说明与DDL v5.2.3）：

- 🔧 **MES快照服务调用关系澄清**：`INightlyBatchOrchestrator`只负责预创建`ScheduleRun`并冻结`ScheduleRunId + DataCutoffTime`；00:40/00:45/00:50三个MES快照同步任务保持独立定时执行，不由`INightlyBatchOrchestrator`直接调用。`ISchedulingOrchestrator`在02:00只校验、装载并消费三类快照，不在计算期间访问MES实时视图或再次同步。
- 🔧 **撤销无业务意义的人工差额字段**：MES工序状态4只表示该小工序执行记录已人工完结，不能证明PI剩余数量被取消。PI总剩余量仍以`Order.Quantity - Order.ReceivedQty`为边界；各小工序剩余加工量以`OperationProgressSnapshot`为权威输入。
- 🔧 **ExecutionLock数量语义修正**：`RemainingExecutionQty`改为2号位维护的当前MES现实工单未来Stage产出承诺上限，不用于生成小工序Task。只有MES工单视图明确显示整张现实工单终结时才置0；未完成且未正式取消的差额退出当前执行锁后，返回PI未承诺剩余池重新排程。

> **v3.20覆盖声明**：本轮只修正MES快照服务调用关系和ExecutionLock人工差额数量语义，不改变MES双向视图、Pegging、PlanVersion、Candidate、HardLock及1—5号位总体职责架构。


**v3.19更新说明**（2026-07-29 第三轮定点修订；对齐 演变总表 v3.34 / 防腐层 v1.38 / 集成接口 v1.28 / 字段说明与DDL v5.2.2）：

- 🔄 **APS↔MES统一为双向视图集成**：APS不主动调用MES建单/取消接口，不建设MQ事件累计与`MES_Actual_Staging`主链。APS通过`MESPlanRelease`固化发布单元并提供`APS_MES_PlanRelease_View`；MES通过实时工单、工序进度和Stage进度契约视图向APS提供当前累计事实。
- 🆕 **发布单元稳定键与数量口径**：`ReleaseItemKey`由APS生成、MES原样保存并在`MES_APS_WorkOrder_View`回传；它是跨系统唯一幂等键，`TaskNo`仅用于展示和诊断。一条MESPlanRelease对应一张未来MES现实工单，可关联同一PI、同一Stage下的多个小工序Task；发布数量取Stage级执行批次的单一流转量，禁止累加关联小工序Task数量。
- 🔄 **Task状态触发点修正**：写入发布视图时Task仍为`PLANNED`；APS从MES实时工单视图确认已建单后，才将`MESPlanRelease=PUBLISHED→CONSUMED`、创建ExecutionLock并把相关Task迁移为`RELEASED`。
- ⚠️ **该数量处理已由v3.20覆盖**：MES工序状态4只保留为小工序人工完结来源事实，不再派生独立人工差额数量，也不得改变PI总剩余边界。
- 🔄 **HardLock允许部分解除**：部分履约或审批释放后，只要`RemainingLockedQty>0`仍保持ACTIVE并继续跨版本恢复；已履约数量不得再次释放。
- 🔄 **Candidate现实执行切片**：Candidate继续以Base供给切片保护库存/在途边界，但必须按Candidate自己的`DataCutoffTime`重读MES实时累计事实并形成独立执行快照；这不等于重新开放当前全部库存。
- 🔒 **PI数量双维闭合**：PI位置/ExecutionLock/MESPlanRelease属于互斥物理数量身份；HardLock/SOFT/未分配属于归属状态。两维不得直接相加，HardLock只能约束已有物理供给，不能创造额外数量。
- 📦 **APS_Auth部署边界关闭**：APS_Auth由独立脚本`APS_Auth数据库DDL_v1.0.sql`部署，不属于APS_Production主DDL，测试库部署包必须同时包含两套脚本。

> **v3.19覆盖声明**：以下v3.18及更早版本中的主动下发、逐项回执、MQ事件、出站台账和`MES_Actual_Staging`仅作历史追溯；当前正文一律执行MES拉取发布视图与运行快照口径。

**v3.18更新说明**（2026-07-29 六份文档全局机械审计与场景反向审计修正；对齐 演变总表 v3.33 / 防腐层 v1.37 / 集成接口 v1.27 / 字段说明与DDL v5.2.1）：

- 🔧 **Task执行身份闭合**：正式生产Task新增并持久化 `ProductionInstructionNo + StageCode`；`MTS_InstructionNo`仅保留为历史兼容字段，不能替代跨需求合并后真实承接PI。MES下发与ExecutionLock创建必须直接消费Task执行身份。
- 🔧 **Ledger幂等与Task份额分离**：Ledger新增 `AllocationSequence` 作为PlanVersion内稳定幂等序号；新增 `TaskComponentQty`，明确 `AllocatedQty`是供需分配数量，`TaskComponentQty`是最终Task对该需求的组成份额。`vw_TaskDemandAllocation`只汇总后者，避免多层BOM或多来源分配重复累计。
- ⚠️ **历史口径（已由v3.19废止）—MES逐项回执**：v3.18曾按主动下发模式要求整项接受；当前APS→MES改为发布视图拉取，不再使用`acceptedQuantity`或逐项回执。
- 🔧 **锁生命周期与冻结概念分离**：ExecutionLock状态与完成/取消数量建立严格闭合；`Task.IsLocked`仅表示计划冻结区/人工排程锁，不等同于ExecutionLock，也不产生需求HardLock。
- 🔧 **管道枚举统一**：正式SupplyType统一为 `INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / VMI_ONSITE / ARRIVED_NOT_RECEIVED`。
- 🔧 **非Task分配一对一防重**：每条非Task Ledger分配最多生成一条PeggingSupplyAllocation；重复持久化必须按PlanVersion+LedgerId拒绝。

**v3.17更新说明**（2026-07-28 Pegging数量闭合、TaskDraft、执行锁/硬归属/软分配与跨版本重新Pegging收口；唯一方案来源：《APS Pegging与跨版本供给分配详细设计方案 v1.1（V1最小实现版）》）：

- ✅ **生产指示总量边界统一**：生产指示本次可生产数量固定为 `Order.Quantity - Order.ReceivedQty`；MES在制、Stage等待、XC、厂间在途只用于定位这部分总量，禁止再次扣减或扩张生产指示总量。
- 🆕 **生产指示位置快照**：每 `ScheduleRun + DomainKey + ProductionInstructionNo` 保存总量快照及互斥位置切片；Stage累计完成量下游超过上游时保守下修，下游Stage可证明中间缺失Stage的最小完成量；`UNLOCATED`从承载路径最早Stage保守排程。
- 🔄 **第6/7章算法接缝落入走查**：先确定“需求由哪张生产指示承接多少”，再把承接份额绑定到Stage/XC/在途等具体位置切片；已有执行事实/HardLock先固定，其余按 `AvailableTime → 剩余路径最短 → 稳定来源键`绑定。
- 🆕 **统一分配账本**：所有分配必须由2号位在同一原子动作中同步扣减需求余额和供给余额，并追加 `PeggingAllocationLedger`；Ledger是需求份额、优先级继承、逻辑块组成及最终Task需求数量映射的权威。
- 🔄 **Task正式生成时机收口**：Pegging阶段只形成 `LogicalBlock / TaskDraft`；1号位在有限产能、交期和资格条件下完成合并/拆分及时间排定，返回 `ScheduledTaskDraft + ComponentShares`；2号位随后批量持久化正式Task。V1允许不同需求合并为一个Task，但不同现实MES工单不得伪装合并。
- 🆕 **执行锁与需求归属分离**：MES实时工单视图确认已建单后创建 `ExecutionLock`，固定生产指示、Stage、MES工单和剩余执行量；普通通用产出默认属于SOFT分配，次日按最新优先级重新Pegging；只有特殊出荷指示类型、客户专属、质量/环保资格等命中规则时才创建 `DemandSupplyHardLock`。
- 🆕 **新TaskId跨版本关联**：新PlanVersion仍全量生成新TaskId；同一现实执行对象通过 `Task.ExecutionLockId`关联，不复用旧TaskId，也不得重复下发MES。
- 🔄 **Candidate剩余供给算法重写**：不再使用“Base原始供给－Base全部已分配”；按已消耗、HardLock、ExecutionLock投入需求、ExecutionLock未来产出、Scope外SOFT和Scope内SOFT分别恢复，只有Scope内可释放SOFT重新竞争。
- 🔄 **统一管道供给进入V1正式主链**：v3.12“PipelineSupplies固定空集合/空跑”仅保留为历史口径；现行V1按统一包装视图重建 `SupplyFact_Pipeline`，厂间在途、采购在途/VMI/已到厂未入库按来源业务键去重，Received继续按单据严格消费。
- 🔄 **虚拟占位Task边界**：无正式生产指示可生成可排程的虚拟占位Task，但绝对禁止下发MES、不形成ExecutionLock、不跨版本保留；正式生产指示到达后由次日全量重算自然替换。
- 🔄 **插件/Voucher最小化**：2号位保持余额、Ledger、状态和持久化唯一修改权；5号位以少数策略接口和普通领域计算模块返回只读Result/Decision。普通计算结果不再一律称Voucher；Voucher仅保留正式审批或状态变化场景。V1不建设动态热插拔插件平台。
- 📌 **保护区域不变**：一Run多Domain独立发布、`PARTIAL_SUCCESS`、`ExpectedDomainKeysJson`、ScopeJson固定11字段、白天Candidate严格单Domain、Task五态、BOM/Stage/Routing主链、设备故障不自动暂停等既有口径全部保持。

**v3.16更新说明**（2026-07-20 ExpectedDomainKeysJson 独立字段收口 + 失败终态 FAILED 收口 + ReasonCode 字典统一，对齐 演变总表 v3.31 / 集成接口 v1.25 / 数据架构与防腐层 v1.35 / 数据库字段说明 v5.1.1 / DDL v5.1.1）：

- ✅ **步骤5.2 夜间主链口径（v3.15 初版）**：明确 `FULL_SCHEDULE` 中 2号位为每个 `DomainKey` 创建 `PlanVersion(BUILDING)`；步骤5.1 落盘后 PlanVersion 保持 BUILDING，由步骤5.2 用 Serializable 事务按域原子完成"同 DomainKey 旧 ACTIVE→ARCHIVED + 本次 BUILDING→ACTIVE"（`FULL_SCHEDULE` 无需 CANDIDATE_ACTIVATION 审批，但仍须走事务切换）；`ScheduleRun` 最终状态由步骤5.3 运行汇总统一决定，不在单域发布时直接置 COMPLETED
- ✅ **白天 Candidate 闭环（新增）**：3号位创建 `PlanVersion(BUILDING)`，2号位负责数据构造、Task/ShippingTask 实例化、结果持久化，完成后更新为 `CANDIDATE`；激活须 3号位显式调用版本激活 API
- ✅ **红线40（禁止自动重排）口径清理**：第四部分场景2/5 中"自动触发重排"措辞统一修正为"通知 PMC，由 PMC 决策是否触发重排"；补入红线40声明
- ✅ **第八部分新增场景4**：白天实时评估与 Candidate 闭环完整流程
- ✅ **流程总数重新计算**：第一部分主流程（1）+ 第二至八部分场景（31）= 共32个完整流程
- ✅ **历史版本说明**：v3.13 四表收敛口径保留，加注"历史口径仅供追溯"

**v3.15 冻结前一致性收口（2026-07-15，对齐 演变总表 v3.30 / 集成接口 v1.24 / 防腐层 v1.34 / 字段说明 v5.1.0 / DDL v5.1.0）**：

- ✅ **00:05 Order 版本快照 PlanVersionId 时序收口**：补 `Order` 为 PlanVersion 隔离版本快照表、写入必须持有合法 PlanVersionId 的红线；列出三种合法绑定实现方式（提前 PlanVersion 壳 / ScheduleRun 级临时快照后复制 / PlanVersion 创建后再生成），文档不强制指定，由2号位按现有实现定稿
- ✅ **MES EventType 与 Task.Status 概念分离**：新增概念分离红线；Task 状态机校验改为"收到 `EventType=START`→校验后迁移 `IN_PROGRESS` / `EventType=COMPLETE`→迁移 `COMPLETED`"，不再把 START/COMPLETE 当作 Task 状态
- ✅ **MES 事件不假定携带 TaskId**：新增映射红线（MES TaskNo / WorkOrderNo → APS TaskId 正式映射），TaskId 为事件匹配后解析得到，非 MES 直报字段
- ✅ **ShippingTask 物流执行状态收口**：移除 `Task.Status=IN_TRANSIT` / `ETA` / `ActualArrivalTime` 等错误写法，改由独立物流状态/实绩对象承载，并标注 ShippingTask 正式物流字段待 DDL 补齐；物流延迟改生成 `ExplanationFactDraft(ReasonCode=LOGISTICS_DELAY)`
- ✅ **异常高亮组合读模型**：甘特图异常着色改为 Task 执行状态 + Order.DelayStatus + `ScheduleExplanationFact.ReasonCode` 组合，不再只读 `Task.Status`
- ✅ **跨域虚拟库存 SQL 收口**：原可执行 SQL 改为正式业务伪代码，强制 `Domain_Dependency` 按 `ChildMaterialCode` 关联、防 BOM 多边重复放大、只统计最终产出 Task、AvailableTime 使用 `DefaultLeadTimeDays`
- ✅ **一次 FULL_SCHEDULE ScheduleRun 与多个 Domain PlanVersion 关系明确**：每个 DomainKey 产生一个 PlanVersion，V1 采用 **Domain 独立计算、独立落盘、独立发布**（废止旧 ALL_OR_NOTHING 全域一致发布；一个无关 Domain 失败不得阻止其他成功 Domain 发布；ScheduleRun 终态由步骤5.3 汇总为 COMPLETED / PARTIAL_SUCCESS / FAILED）
- ⚠️ **历史口径（已由v3.19废止）—MES主动下发台账**：v3.15曾要求主动下发查出站台账；当前由`MESPlanRelease.ReleaseItemKey`承担拉取式发布幂等，且不建设`MES_Actual_Staging`事件链。
- ✅ **缺料建议对象统一为 `PurchaseSuggestionDraft`（内存）**：清除 PurchaseRequisition 的 RequiredDate 落库写法 / 缺料建议随 Task 批量落盘 / "PR 存储" 等旧口径；V1 缺料建议非采购申请，不产生采购单据状态，不直接发送 ERP
- ✅ **旧激活口径清理**：v3.13 "步骤5.1 直接置 ACTIVE / ScheduleRun=COMPLETED" 标注废止，统一为步骤5.1 保持 BUILDING、步骤5.2 Serializable 事务原子切换；`ReasonCode` 只存在于 ExplanationFact 链，不写入 Task

**v3.15 V1 最终决策同步（2026-07-15，0号位确认；对齐 演变总表 v3.30 / 集成接口 v1.24 / 防腐层 v1.34 / 字段说明 v5.1.0 / DDL v5.1.0）**：

- 🔁 **废止 ALL_OR_NOTHING，改为 Domain 独立发布**：一次 FULL_SCHEDULE 对应多个 Domain PlanVersion（每个 DomainKey 一个，经 `SourceScheduleRunId` 关联）；各 Domain 在自己的事务内独立计算、落盘、发布；任一无关 Domain 失败不得阻止其他成功 Domain 发布
- ✅ **新增 ScheduleRun 终态 PARTIAL_SUCCESS**：`ScheduleRun.Status` 值域调整为 `RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED`；全部预期 Domain 成功=COMPLETED，部分成功+部分失败/缺失=PARTIAL_SUCCESS，运行级致命错误或零成功=FAILED；运行启动时冻结 `ScheduleRun.ExpectedDomainKeysJson`（**独立字段，不属于 ScopeJson**），不参与"按已建 PlanVersion 反推"
- ✅ **步骤5.3 运行结果汇总（新增）**：各 Domain 进入终态后由运行汇总统一决定 COMPLETED / PARTIAL_SUCCESS / FAILED，步骤5.1/5.2 均不得直接将整个 ScheduleRun 置 COMPLETED
- ✅ **跨域依赖 V1 边界**：跨域仍按 Domain 独立发布；某 Domain 失败产生 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 原因事实 + `RescheduleRecommendation`，由 PMC/0号位人工选择相关 Domain 重算；V1 不自动回滚已成功上游、不建跨域多 Domain Candidate、不建原子激活组
- 🔁 **删除 V1 暂停闭环**：移除 `TaskPauseVoucher` / `TaskResumeVoucher` / `PAUSE`→`PAUSED` / `RESUME`→`RUNNING` 正式流程；`Task.Status` 保持 `PLANNED / RELEASED / IN_PROGRESS / COMPLETED / CANCELLED`，不新增 `PAUSED` / `SUSPENDED` / `WAITING` / `PENDING` / `RUNNING`
- ✅ **MES 五态（0-4）↔ Task.Status 映射表新增**：MES 工序报工 0待开工/1开工中/2完工报工/3未完工报工/4未完工报工已完结，统一映射为 APS `PLANNED/RELEASED`/`IN_PROGRESS`/`COMPLETED`/`IN_PROGRESS`/`COMPLETED`，状态3非完成、状态4须保留"手动完工、数量不足"来源事实
- 🔁 **设备故障改风险事实**：`RESOURCE_BREAKDOWN`/`RESOURCE_REPAIRED` 仅产生资源不可用/可用事实 + `ScheduleExplanationFact` + `RescheduleRecommendation` + 看板告警，不再自动暂停 Task、不再生成 TaskPauseVoucher

**v3.16 V1 冻结前一致性修正（2026-07-20，0号位确认 ExpectedDomainKeysJson 独立字段方案A）**：

- ✅ **`ExpectedDomainKeysJson` 独立字段方案A 收口**：废止 `ScopeJson.ExpectedDomainKeys` 旧口径；`ExpectedDomainKeysJson` 为 `ScheduleRun` 独立运行级不可变字段（`NVARCHAR(MAX) NULL` + JSON 合法性 CHECK），**不属于 ScopeJson**；ScopeJson 固定 11 字段红线（#37）保持不变；删除"ScopeJson 中冻结 ExpectedDomainKeys""ScopeJson 只能包含一个 DomainKey""FULL_SCHEDULE 通过 ScopeJson 保存 ExpectedDomainKeys"等矛盾描述
- ✅ **各 RunType 校验规则**：FULL_SCHEDULE 要求 `ExpectedDomainKeysJson` 为非空 JSON 数组（≥1 不重复 DomainKey，ScopeJson 可为 NULL）；白天 Candidate（LOCAL_RESCHEDULE/MANUAL_RESCHEDULE/INSERT_ORDER_WHATIF）要求**恰好 1 个元素 = BasePlanVersion.DomainKey = Candidate.DomainKey**；SIMULATION 阶段二骨架同样用独立 `ExpectedDomainKeysJson`
- ✅ **多 Domain 独立发布模型不变**：一次 FULL_SCHEDULE → 每 DomainKey 一个 PlanVersion（经 SourceScheduleRunId 关联）→ 独立计算/落盘/发布；ScheduleRun 终态 `RUNNING/COMPLETED/PARTIAL_SUCCESS/FAILED` 由步骤5.3 按独立字段汇总；一个无关域失败不得阻止其他成功域发布
- ✅ **BUILDING 失败终态收口**：重试（≤3 次）/降级后仍无法产出 → 该域 PlanVersion 必须转 `FAILED` 并写 ErrorMessage，不得无限期停留 BUILDING（BUILDING 非失败终态）；运行级致命错误下已创建未完成的域 PlanVersion 统一标记 FAILED，不得让 ScheduleRun 永久 RUNNING；步骤5.3 仅在全部 ExpectedDomainKeysJson 对应 PlanVersion 进入 ACTIVE/FAILED 后汇总
- ✅ **PlanVersion 唯一索引修复**：`UQ_PlanVersion_ScheduleRun_Domain` 移至 SourceScheduleRunId/SourceSimulationRunId/FK 全部创建之后，并加 `AND SourceSimulationRunId IS NULL` 过滤（阶段二 SimulationRun 同域多候选不受阻止）；`UQ_PlanVersion_OneActivePerDomain` 保持（每 Domain 唯一 ACTIVE）；两索引仅创建一次
- ✅ **ScheduleExplanationFact 字段对齐 DDL**：新增 `ScheduleRunId INT NULL`（FK + IX_SEF_ScheduleRun）；字段类型统一 `PlanVersionId/OrderId/TaskId/ResourceId INT`；ObjectType 值域 `ORDER/TASK/RESOURCE/STAGE/DOMAIN`（DOMAIN 用于跨域版本不一致风险）；跨域失败每个受影响域写一条 `ObjectType=DOMAIN, ReasonCode=CROSS_DOMAIN_VERSION_MISMATCH_RISK`，PlanVersionId 保持 NOT NULL
- ✅ **V1 暂停流程残留清理**：演变总表/接口/职责中 `TaskPauseVoucher`/`TaskResumeVoucher`/PAUSE→PAUSED/RESUME→RUNNING 旧流程全部改为"资源不可用/恢复事实 + ScheduleExplanationFact + RescheduleRecommendation + 看板告警 + PMC 决策"，历史口径标注"已废止仅供追溯"
- ✅ **Task 状态残留清理**：`EventType=START → 迁移 IN_PROGRESS`（原 RUNNING 口径废止）；全文按对象语境核查，Task.Status 仅可为 `PLANNED/RELEASED/IN_PROGRESS/COMPLETED/CANCELLED`，`RUNNING` 仅用于 ScheduleRun/SimulationRun/Hangfire/ETL
- ✅ **ReasonCode 字典统一**：以 `DUE_DATE_RISK` 为正式值，全文 `DUE_DATE_VIOLATION`→`DUE_DATE_RISK`；删除未登记示例 `DUE_DATE_TIGHT`/`UPSTREAM_DELAY`（上游延期并入 `CROSS_DOMAIN_VERSION_MISMATCH_RISK`，ObjectType=DOMAIN）；确立唯一权威 ReasonCode 列表，ReasonCode 只写入 ScheduleExplanationFact 链路

**v3.12更新说明**（2026-06-15 管道供给链路完整骨架 + 00:55同步步骤 + ODS空契约视图 + 分层语义修正，对齐 DDL v5.0.42 / 演变总表 v3.27 / 防腐层 v1.31 / 字段说明 v5.0.42）：

- 🆕 **00:55 管道供给同步**：新增步骤 `sp_SyncPipelineSupply`（V1 空跑：清空 `SupplyFact_Pipeline` + 写 SUCCESS 日志）
- 🔄 **数据流向总览更新**：补充 00:55 管道供给同步行；02:00 ScheduleContext 装载补充 `PipelineSupplies`（V1 空集合）
- 📌 **V1 空跑声明**：00:55 V1 不读取 ODS 空视图；`PipelineSupplies` 为空属于正常结果，不是 ETL 失败
- 📌 **分层语义统一**：`ERP_InterplantInTransit_View` = ODS 层（MES_Integration/来源ERP/5号位）；`ext_ERP_InterplantInTransit_View` = APS 层（APS_Production/2号位）

**v3.11更新说明**（2026-06-12 MES生产进度汇总链路 + 订单状态准入 + Task/Pegging重算口径 + EAM预留，对齐 DDL v5.0.41 / 演变总表 v3.26 / 防腐层 v1.30 / 字段说明 v5.0.41）：

- 🆕 **00:40/00:45/00:50 三个新步骤**：MES工单快照同步 → 工序进度快照同步 → 大工艺进度快照同步（`sp_SyncMESWorkOrderSnapshot` / `sp_SyncOperationProgressSnapshot` / `sp_SyncStageProgressSnapshot`）
- 📌 **00:00 活跃根集合**：补充 `Order_Canonical.Status` 过滤声明（CLOSED/CANCELLED 不进入 BOM Request）
- 📌 **数据流向总览更新**：补充 00:40/00:45/00:50 三行；02:00 ScheduleContext 装载补充 MES 进度快照
- 📌 **步骤1.2/1.3 注记**：Task/Pegging 全量重算口径写死；MES 进度不匹配历史 TaskId；Pegging 不跨版本复用
- 📌 **EAM V1 预留**：`EAM_APS_Resource_View` 预留占位声明，V1 不读取 EAM 数据，不生成资源不可用窗口

**v3.10更新说明**（2026-05-21 BOM入口分流R28/R29/R30/R31，对齐 DDL v5.0.28 / 字段说明 v5.0.28 / 防腐层 v1.23）：

- 🔄 **00:20 BOM批次展开 — 步骤3**：补充BOMNO IS NULL分流描述（R28/R29/R30/R31详见防腐层文档§2.3.2）

**v3.9更新说明**（2026-05-16 订单提升链路重构，对齐 DDL v5.0.27 / 字段说明 v5.0.27 / 防腐层 v1.22）：

- 🔄 **数据流向总览**：白天行推进`sp_ValidateAndPromoteOrders`说明更新对齐v5.0.27重写要点
- 🔄 **场景1 步骤1.2**：订单验证动作说明全量更新（MaterialCode三级解析链、OrderType未知→FAILED、BOMNO_MISSING非阻断、CustomerSegment无匹配→UNKNOWN）
- 🔄 **v5.0.27设计红线**：FailureCode单值分层语义写死

**v3.8更新说明**（2026-05-13 阶段二三接缝：ScheduleRun包装 + 落库与激活分离 + 仿真入口预留，对齐总表 v3.17 / 字段说明 v5.0.25 / 防腐层 v1.20）：

- ✅ **阶段0**：`ScheduleRun` 优先创建（`RunType=FULL_SCHEDULE`，`Status=RUNNING`）作为运行编排包装，再生成 `PlanVersionId`；阶段一不改排程内核；数据流向图同步更新
- ✅ **步骤5.1 落盘**：补 `ScheduleExplanationFact` 批量落库（2号位从 1号位 `ExplanationFactDraft` 接收，与 Task/Pegging 同批次）；【v3.15 修正】v3.13 原表述"步骤5.1 同时补 `ScheduleRun.Status=COMPLETED` + `PlanVersion.Status=ACTIVE`"**已废止**——当前口径为：步骤5.1 落库后 `PlanVersion` 保持 `BUILDING`；步骤5.2 按域独立激活（同 DomainKey 旧 ACTIVE→ARCHIVED + 本次→ACTIVE），**不**在该步置整个 `ScheduleRun` 状态；`ScheduleRun` 终态（`COMPLETED`/`PARTIAL_SUCCESS`/`FAILED`）由步骤5.3 汇总统一决定
- ✅ **步骤5.2 版本指针切换**：区分 `FULL_SCHEDULE`（完成后自动激活）vs 其他 RunType（产出 CANDIDATE 版本，须显式触发激活）；**落库与激活分离**红线写死
- ✅ **步骤5.5.5（新增）仿真/人工重排入口预留**：描述阶段二 SIMULATION / INSERT_ORDER_WHATIF 入口位置；阶段一此步为骨架注解，不实装

**v3.7更新说明**（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21 / 字段说明 v5.0.21 / 防腐层 v1.17 / 集成接口 v1.14）：

- ✅ **00:00 活跃根集合划定**：描述从"提取去重BOMNO"改为"按活跃订单粒度推送BOM请求"（含无BOMNO订单；不再去重）
- ✅ **00:20 BOM批次展开**：更新动作描述对齐新 `MES_API_BOM_Request_Detail` 结构 + `RequestDetailId` 追溯锚点
- ✅ **数据流向总览**：00:00 行 + 00:20 行更新
- ✅ **场景1 步骤1.2**：sp_ValidateAndPromoteOrders 补 `FailureCode`/`NextActionCode` 说明；BOMNO改可空（废除必填校验）
- 📌 **设计决策写死**：BOM入口解析分流在**5号位Workset处理阶段**；活跃根集合不再依赖BOMNO去重

**v3.6更新说明**（2026-05-04 BOM 回填 SP 完整实现，对齐 DDL v5.0.18 / 字段说明 v5.0.18 / 防腐层 v1.16 / 集成接口 v1.13 / 内部契约 v2.6）：

- ✅ **00:20 BOM批次展开**：5号位后置回填从笼统描述升级为明确 SP 名称 `sp_EnrichBOMWorkset`，补充 `ChildRequiredFactory` + Issues 降级登记
- ✅ **数据流向总览**：00:20 行补 SP 名称
- ✅ 相关权威文档引用版本升级

---

**v3.5更新说明**（2026-04-29 生产部门主链注入 + ProcessCodeDict 重定位，对齐 DDL v5.0.16 / 字段说明 v5.0.16 / 防腐层 v1.15 / 资源重设计 v5.2 / 架构总表 v3.12 / 集成接口 v1.12）：

### 阶段01 数据备料 时序补入两个新步骤

| 时间 | 步骤 | 说明 |
|---|---|---|
| 00:10 | `sp_SyncResourceData(@SourceType='MES')` 升级 | MERGE 加 `ProductionDepartment` 双字典映射 JOIN（FactoryCode + ProductionDeptCode 任一未命中即跳过登记日志） |
| **02:30 🆕** | `sp_RebuildMaterialStageDeptContext(@TriggerMode='FULL')` | v5.0.16 新增——⚠️ **占位骨架，当前未实现**（DDL Step1~6 全 TODO）；设计意图：2 号位每日定时全量重建 `MaterialStageDeptContext`，冲突/缺失登记 `MaterialStageDeptContext_Issues`，旧 IsCurrent=1 不动；本步骤在实装前**走查不实际执行**，1 号位临时空跑 / 退化为按 LeadTime 降级 |

### 1 号位排程主链口径升级

```text
v5.0.13 主链：StageDetail (MaterialId, StageCode) → RoutingOperation (MaterialId, StageCode)
v5.0.16 主链：StageDetail (MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套 (MaterialId, ProductionDepartmentId, StageCode)
```

**1 号位消费红线**（详见集成接口 v1.12 §1 号位消费契约）：
- ❌ 1 号位禁止直接读 `MaterialSupplyContext` / `ProcessCodeDict` / `MaterialStageDeptOverride`
- ❌ 1 号位禁止跳过 Context 直查 Routing 三件套（必须先经 `MaterialStageDeptContext` 锁部门）

### 字段契约升级（4 + 1 个 ODS 视图）

`MES_APS_Resource_View` / `MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `APS_OperationResourceEligibility_View` 全部加 **`ProductionDeptCode`**（`MES_APS_Resource_View` 同时 DROP `WorkshopCode`）；`MES_ProcessCode_View` 加 **`StageCode`** 增强列 + RENAME `SourceSystem` → `CodeOrigin`。

### EAM 扩展路径不变

走查步骤零修改（SP 直接传 `@SourceType='EAM'` 即可）；双字典映射逻辑零分叉。

【设计决策】**部门 = 物料 × 阶段联合属性**：不进 StageDict / 不进 StageDetail；2 号位 SP 组装后由 1 号位消费。  
【设计决策】**Routing 三件套 `ProductionDepartmentId NOT NULL`**：业务确认 MES 工艺数据全部带部门，不引入 `_UNSPECIFIED` 哨兵；映射失败行登记 `APS_ETL_Log` 跳过（不阻塞批次）。

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

**v3.4更新说明**（2026-04-25 对齐 DDL v5.0.13 + 防腐层 v1.14，补资源主数据同步链路）：
- ✅ **阶段01 数据备料** 时序中补入新步骤 **00:10 资源主数据同步**（`sp_SyncResourceData(@SourceType='MES')` → `Resource` 表），填补 v5.0 重构后 Resource 改为外部镜像的回路缺失
- ✅ ODS 契约视图命名统一：`APS_Resource_View` / `ext_APS_Resource_View` → `MES_APS_Resource_View` / `ext_MES_APS_Resource_View`（与 `MES_APS_Routing_*_View` 对齐）
- ✅ **数据流向总览** 时序表新增 00:10 Resource 同步行；前置条件表述由 “00:05-00:35” 扩展为 “00:05-00:35（含资源镜像刷新）”
- ✅ 时间表在全文档统一对齐：Resource 同步落在 **00:10** （与主数据同窗口，二者同属外部主数据镜像且执行时间均为秒级）；同步更新 DDL v5.0.13 注释 / 防腐层 v1.14 changelog / 资源重设计 v5.1 §9 / 字段说明 v5.0.13 的时间表
- 【设计决策】EAM 扩展路径演练点：未来 EAM 上线只需在 ODS 同构新建 `EAM_APS_Resource_View`，走查步骤无需修改（SP 直接传 `@SourceType='EAM'`）

**v3.3更新说明**（2026-04-18 对齐v5.0/v5.0.8数据架构，11处过时内容修订）：
- ✅ **#1** 00:10 `sp_SyncMaterialMapping` → `sp_SyncMasterData(@SourceType)` + 三表协同（Material+MaterialMapping+MaterialSupplyContext）
- ✅ **#2/#4** 00:15 Routing同步：原单一`Routing`表+视图 → v5.0拆分后5表3视图（RoutingOperation/Dependency/Eligibility/PlanningParam/Stage）
- ✅ **#3** 00:20 BOM展开：负责人从3号位修正为2号位(发起)+5号位(展开+回填)，补StageDetail(EDGE/ROOT)搬运步骤
- ✅ **#5/#6** 步骤1.2/1.3：所有对已废弃`BOM`/`Routing`表的引用替换为v5.0正式表名
- ✅ **#7** 阶段0.5跨域扫描：DDL和SQL完全重写，对齐v5.0表名/字段/枚举值，补`Domain_Dependency`正式DDL到SQL文件(v5.0.9)
- ✅ **#8** 步骤4.2：`Material.LeadTime` → `MaterialSupplyContext.LeadTimeDays`（v5.0仓库级上下文）
- ✅ **#9** 步骤2.6 Task实例化：补StageDetail/StageScopeType=EDGE/ROOT双层路径说明
- ✅ **#10** 附录A CDC代码：`MergeOrders`直合 → `InsertToOrderStaging`三层路径
- ✅ **#11** 总结第三部分场景3标题对齐正文（分域结果合并 → 单向硬约束传递）

**v3.2更新说明**（2026-04-03 订单链路审计）：
- ✅ 数据备料时序补00:00活跃根集合划定步骤，00:05改为Order_Canonical→sp_SyncOrdersToPartitionTable→Order三层路径
- ✅ 数据流向总览补白天增量行（ERP_Order_Staging→sp_ValidateAndPromoteOrders→Order_Canonical）
- ✅ 场景1"ERP增量订单同步"全面重写：步骤1.1改为增量拉取写入Staging、步骤1.2改为sp_ValidateAndPromoteOrders校验提升到Canonical

**v3.1更新说明**（2026-03-19）：
- ✅ 补充数据准备阶段详细时序（00:05-00:35）
- ✅ 明确Socket-Plug模式下的数据同步流程
- ✅ 补充主数据同步、工艺路线同步、BOM批次展开的详细步骤
- ✅ 更新相关文档引用

---

## 前言：架构思想说明

为了达到“10万级Task在目标窗口内收敛”并适应持续变化的制造规则，本系统采用成熟APS的两条核心思想：**机制与策略分离**、**物料数量分配与有限产能时间推演分离**。

**核心设计原则**：
- ✅ **稳定内核 + 少数策略扩展点**：2号位提供余额、Ledger、状态机、版本和批量持久化内核；5号位负责供给资格、排序、位置计算、部分份额绑定和不足处理等领域判断。
- ✅ **规则变化优先参数化**：日常差异优先由 `RuleSetVersion / ParameterSetVersion / StrategyProfileVersion` 承载；只有确实存在不同算法实现时，才通过.NET接口和依赖注入选择策略实现。
- ✅ **不建设动态插件平台**：V1不做程序集目录扫描、运行时卸载、插件注册市场或“每条规则一个插件”。所谓插件在V1仅表示有稳定接口、可替换实现的策略模块。
- ✅ **唯一修改权**：5号位返回只读计算结果或分配决策，不直接修改余额、Task、ExecutionLock、HardLock或物理Pegging；2号位负责校验并原子执行。
- ✅ **分域隔离与全内存计算**：外部事实和版本切片在阶段1装载；阶段2—4禁止Pegging循环逐行访问数据库。
- ✅ **多版本隔离**：Task、物理Pegging和普通SOFT分配随PlanVersion重建；MES现实执行事实、HardLock及已消耗事实跨版本延续。

### 计算结果、分配决策与Voucher的边界

5号位输出分为三类，禁止全部混称Voucher：

1. **普通计算结果（Result）**：如 `PriorityResult`、`PositionCalculationResult`、`ShortageResult`、`ImpactAssessmentResult`。只表达计算结果，不带状态变更语义。
2. **Pegging分配决策（PeggingAllocationDecision）**：表达“需求D使用供给S数量Q及原因R”。2号位必须重新校验余额、资格、HardLock和来源唯一键后，才可原子扣减并写Ledger。
3. **正式Voucher**：仅用于确实需要审批或正式状态变化的场景，如人工冻结、容差结案、HardLock人工创建/解除等。普通排序、位置计算、缺口识别和换型属性提取不使用Voucher。

```text
5号位.领域计算/策略判断
      ├─ 普通Result（只读）
      ├─ PeggingAllocationDecision（待2号位原子执行）
      └─ 正式Voucher（仅审批/正式状态变化）
                    ↓
2号位.校验余额与状态 → 原子执行 → Ledger/Task/锁/结果持久化
```

**红线**：
- 5号位绝不直接扣库存、扣生产指示份额、创建正式Task、修改Task状态或写物理Pegging。
- 2号位不能把5号位返回结果当作已执行事实；必须校验需求余额、供给余额、来源唯一性和版本范围。
- 1号位只消费 `TaskDraft / ScheduledTaskDraft` 及资源、工艺、时间约束，不读取原始库存、Received或生产指示位置明细直接做Pegging。

---

## 第一部分：凌晨全量排程主流程（6个阶段）

以下是每日凌晨 02:00，系统触发全量排程时的全流程接力过程：

### 🏁 阶段0：触发起点

**时间**：凌晨 02:00  
**触发方式**：Hangfire 定时任务

**动作**：
- **【3号位】** Hangfire 后台定时任务准时触发，按下本次排产的主控按钮
- **【3号位】** 读取已在数据准备阶段（00:38 前）预创建的 `ScheduleRun` 记录，获取 `ScheduleRunId`、`DataCutoffTime`、`StrategyProfileVersionId`（v3.14 策略包绑定）与运行时冻结的预期域集合 `ScheduleRun.ExpectedDomainKeysJson`（v3.16 决策：独立字段，运行启动即冻结，后续不参与"按已建 PlanVersion 反推"，不进入 11 字段 ScopeJson）
- **【3号位】** 根据 `StrategyProfileVersionId` 加载 `StrategyProfileVersion → RuleSetVersion + ParameterSetVersion`，初始化 `ScheduleContext.RuleConfig / SchedulingParams`；1号位/5号位只消费已装载的规则参数结果，不直接读维护表
- **【2号位】** 调度器根据本次冻结的 `ScheduleRun.ExpectedDomainKeysJson`，**为每个 `DomainKey` 创建一个独立的 `PlanVersion`**（夜间 `FULL_SCHEDULE` 不得描述为"创建一个全局 PlanVersion 承载所有 Domain 结果"）：
  - `SourceScheduleRunId` = 当前 ScheduleRun.Id
  - `DomainKey` = 当前域（每个 PlanVersion 必须写入自己的 DomainKey；同一 `ScheduleRun` + 同一 `DomainKey` 最多一个 PlanVersion）
  - `SourceSimulationRunId` = NULL
  - `VersionCategory` = `DAILY_BASELINE`
  - `Status` = `BUILDING`（版本壳已创建，结果尚未落库）
  - `PlanHorizonStart/End` = 计划窗口
  - `BatchNo` = 本次数据批次号
- **【3号位】** 在服务器内存中初始化"排产沙盘（`ScheduleContext`）"，将 `ScheduleRunId` 注入内存对象（分域计算时逐域装载）

**数据流向**：
```
Hangfire 定时器 → 读取已创建的 ScheduleRun（Id + DataCutoffTime + ExpectedDomainKeysJson）
→ 2号位按每个 DomainKey 创建 PlanVersion（SourceScheduleRunId, DomainKey, Status=BUILDING）
→ 逐域初始化 ScheduleContext（内存）并执行该域计算
```

**各域发布成功后**（每个 Domain 在自己的事务内由步骤5.2 原子切换激活，PlanVersion 仍为 `BUILDING` 直到该域发布）：
```
PlanVersion.Status     = ACTIVE / ActivatedAt = 当前时间 / ActivatedBy = 'SYSTEM'
（同 DomainKey 旧 ACTIVE → ARCHIVED 在同事务内完成）
```

**某 Domain 计算/落盘/发布失败时**：
```
该 Domain 的 PlanVersion.Status = FAILED / ErrorMessage = 错误信息
该 Domain 原有 ACTIVE 版本继续保持 ACTIVE
不得影响其他独立 Domain 的新版本发布
```

**所有预期 Domain 进入终态后**（由步骤5.3 运行汇总统一决定，不在单域发布时直接置位）：
```
ScheduleRun.Status = COMPLETED / PARTIAL_SUCCESS / FAILED（见步骤5.3 定义）
ScheduleRun.CompletedAt = 最终状态汇总完成时间
```

> **v3.13 架构说明**：四表职责收敛——`ScheduleRun` 记录运行过程（运行状态归它），`PlanVersion` 记录结果版本（版本生命周期状态归它：BUILDING→ACTIVE/FAILED）。正式采用直接看 `PlanVersion.Status = ACTIVE`。`PlanVersion.SourceScheduleRunId` 反向追溯到运行记录。v3.12 时序修正：`ScheduleRun` 必须在 00:38 前预创建。

---

### � 阶段0.5：跨域依赖静态扫描（解决拓扑排序死循环）

**时间**：凌晨 01:50（主排程 02:00 前的预处理阶段）  
**负责人**：2号位（数据基础设施）

**⚠️ 架构红线说明**：
- **问题**：如果"跨域依赖图"是在排程子任务启动后动态识别的，那么3号位在发车前无法知道依赖关系，就无法进行拓扑排序（谁先跑、谁后跑），导致逻辑死循环。
- **解决**：必须在排程启动前，通过静态SQL扫描全局BOM，将跨域血缘关系提前固化到数据库表中。

#### **步骤0.5.1：区分"跨厂同族"与"跨域异族"**

**动作**：
- **【2号位】** 明确两类跨域场景：
  - **跨厂同产品族**（内政）：A厂和B厂都生产产品族X，半成品在厂间流转
    - 这类依赖由 **【5号位】** 在排程时，基于真实厂间订单动态生成物流发货Task（ShippingTask）
    - 不影响域调度顺序（因为都是同一个产品族域）
  - **跨产品族依赖**（外交）：产品族A消耗产品族B的半成品
    - 这类依赖必须**提前静态扫描**，否则无法确定域的执行顺序

#### **步骤0.5.2：静态扫描跨域BOM血缘关系**

**动作**：
- **【2号位】** 在 01:50 执行SQL暴力扫描，生成 `DomainDependency` 表

**DDL（数据定义）**（2026-04-18 更新，对齐v5.0 DDL）：
```sql
-- =============================================
-- 跨产品族域依赖表（01:50静态扫描产物）
-- 用途：3号位在02:00读取此表构建拓扑排序，决定域调度顺序
-- 刷新频率：每日01:50全量TRUNCATE+INSERT
-- =============================================
CREATE TABLE Domain_Dependency (
    UpstreamDomainCode   NVARCHAR(50) NOT NULL,  -- 上游域（ProductFamily.Code，如：产品族B）
    DownstreamDomainCode NVARCHAR(50) NOT NULL,  -- 下游域（ProductFamily.Code，如：产品族A）
    ChildMaterialCode    NVARCHAR(50) NOT NULL,  -- 关联的半成品物料编码（Material.MaterialCode）
    DefaultLeadTimeDays  INT NOT NULL DEFAULT 2,  -- 跨域物流默认提前期（天），V1硬编码=2，V2可配置化
    ScannedAt            DATETIME2 NOT NULL DEFAULT GETDATE(),  -- 扫描时间戳
    PRIMARY KEY (UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode)
);
```

**SQL扫描逻辑**（2号位需要编写的脚本）（2026-04-18 更新，对齐v5.0表名和字段）：
```sql
-- 清空旧数据
TRUNCATE TABLE Domain_Dependency;

-- 扫描跨域BOM依赖（基于APS_BOM_RAW + Material + ProductFamily）
INSERT INTO Domain_Dependency (UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode, DefaultLeadTimeDays)
SELECT DISTINCT
    供应域.Code           AS UpstreamDomainCode,
    消耗域.Code           AS DownstreamDomainCode,
    子件.MaterialCode     AS ChildMaterialCode,
    2                     AS DefaultLeadTimeDays   -- V1硬编码2天，V2可从配置表读取
FROM 
    APS_BOM_RAW AS BOM
    INNER JOIN Material AS 父件 ON BOM.ParentMaterialCode = 父件.MaterialCode
    INNER JOIN Material AS 子件 ON BOM.ChildMaterialCode  = 子件.MaterialCode
    INNER JOIN ProductFamily AS 消耗域 ON 父件.ProductFamilyId = 消耗域.Id
    INNER JOIN ProductFamily AS 供应域 ON 子件.ProductFamilyId = 供应域.Id
WHERE 
    供应域.Code <> 消耗域.Code                      -- 只保留跨域依赖
    AND 子件.MaterialType = 'SEMI_FINISHED';        -- 只关注半成品（v5.0枚举值）
```

**⚠️ V1已知简化**：
- `DefaultLeadTimeDays` 硬编码为2天。V2阶段可新增 `DomainLogisticsConfig` 配置表，按域对精确化
- 原文档中的 `LogisticsConfig` 表在当前DDL中不存在，V1先用硬编码兜底

**数据来源**：APS.APS_BOM_RAW + APS.Material + APS.ProductFamily  
**数据去处**：APS.Domain_Dependency 表

**数据流向**：
```
APS.APS_BOM_RAW + Material + ProductFamily → 2号位.SQL静态扫描 → Domain_Dependency表（固化跨域血缘）
```

#### **步骤0.5.3：3号位读取静态依赖图，构建拓扑排序**

**动作**：
- **【3号位】** 在 02:00 排程启动时，执行一句简单查询：
  ```sql
  SELECT * FROM Domain_Dependency;
  ```
- **【3号位】** 基于这张静态表，使用图算法（如 Kahn 算法）构建拓扑排序
- **【3号位】** 决定域的执行顺序（哪个域先跑、哪个域后跑）

**示例**：
```
扫描结果：
- 产品族B（电机） → 产品族A（整机）
- 产品族C（轴承） → 产品族B（电机）

拓扑排序结果：
Layer 0: 产品族C（无依赖，可先跑）
Layer 1: 产品族B（依赖C，C跑完后跑）
Layer 2: 产品族A（依赖B，B跑完后跑）
```

**⚠️ 架构契约**：
- 3号位的调度器**只能基于这张静态表**画图
- **绝对禁止**在内存沙盘中动态发现新的跨域调度依赖
- 如果业务需要新增跨域依赖，必须修改BOM后，等下一轮01:50扫描生效

**架构收益**：
- 彻底消灭"死循环悖论"
- 3号位在02:00启动时，瞬间获得拓扑排序结果
- 分域计算可以按正确顺序并发执行

---

### 阶段1：数据备料与配置装载（瞬间快照）

**负责人**：2号位（数据基础设施）

**⚠️ 数据准备阶段时序（Socket-Plug模式）**：

为了确保02:00排程时数据已就绪，系统在凌晨执行以下数据准备流程：

**00:00 - 活跃根集合划定**（2026-04-03 订单链路审计补充；2026-05-08 v3.7：订单级粒度；2026-06-12 v3.11：状态过滤）：
- **负责人**：2号位
- **动作**：从`Order_Canonical`划定90天活跃根集合，按**订单粒度**（含无BOMNO订单）推送BOM展开请求到`MES_API_BOM_Request_Detail`（v5.0.21：不再去重BOMNO；含Model/MaterialCode/FactoryCode；BOMNO可空）
- **⚠️ v3.12 状态准入过滤（写死）**：生成活跃根集合前**必须筛选** `WHERE Order_Canonical.Status = 'OPEN'`（v3.12 窄口径）；只有 OPEN 状态的订单/生产指示进入 BOM Request 并生成 Task/Pegging；**CLOSED/CANCELLED 不得进入 BOM Request**
- **⚠️ v5.0.21 变更**：BOM入口解析（有BOMNO直接展开 / 无BOMNO从Model推导）由5号位Workset阶段负责；2号位仅推送基础字段
- **前提**：白天每小时增量已通过 `ERP_Order_Staging` → `sp_ValidateAndPromoteOrders` → `Order_Canonical` 路径持续更新
- **执行时间**：约1分钟

**00:05 - Order版本快照装载**（2026-04-03 订单链路审计修正；2026-07-15 v3.15 PlanVersionId 时序收口）：
- **负责人**：2号位
- **动作**：执行`sp_SyncOrdersToPartitionTable`，从`Order_Canonical`补齐MaterialId/ProductFamilyId/FactoryId/DomainKey/PriorityScore后装载到`Order`版本快照表
- **数据路径**：`Order_Canonical`（防腐层核心表）→ `Order`（业务版本快照表，按合法 PlanVersionId 绑定写入并隔离）
- **⚠️ PlanVersionId 绑定红线**：**Order写入必须持有合法PlanVersionId**；`Order` 为 **PlanVersion 隔离的版本快照表**。任何写入 `Order` 的动作必须持有合法 PlanVersionId；在 PlanVersion 尚未创建时，只允许写入 `Order_Canonical`（Canonical）或明确的运行级临时承载（如 ScheduleRun 级临时快照），**不得写入无版本归属的 `Order` 记录**。
- **绑定机制（由2号位实现，文档不强制指定某一种）**：
  - 方案A：提前创建 `PlanVersion` 壳（BUILDING 预创建），00:05 直接写入本次 PlanVersion 分区；
  - 方案B：00:05 先写 ScheduleRun 级临时快照，PlanVersion 创建后由其复制/迁移到对应版本分区；
  - 方案C：00:05 只准备 Canonical 或中间数据，PlanVersion 创建后再生成 `Order` 版本快照；
  - 三方案均可，最终写入 `Order` 版本表时**必须有合法 `PlanVersionId`**；具体由2号位按现有实现定稿（本次不擅自强制改变2号位的实现方式）。
- **执行时间**：约3-5分钟

**00:10 - 主数据三表协同同步**（2026-04-18 更新）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **契约插座（Socket）**：ERP DBA创建`ERP_Master_View`，MES DBA创建`MES_Material_View`
  - **数据插头（Plug）**：5号位创建`ext_ERP_Master_View`和`ext_MES_Material_View`
  - **数据装载（Loader）**：2号位执行`sp_SyncMasterData(@SourceType='ERP')`和`sp_SyncMasterData(@SourceType='MES')`
- **动作**：从ODS库的ext视图同步主数据到`Material`+`MaterialMapping`+`MaterialSupplyContext`三表协同（v4.0统一参数化SP，双源同构契约）
- **执行时间**：约15秒（18000条记录）

**00:10 - 资源主数据同步**（v3.4 新增 2026-04-25，与主数据同窗口并行）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **契约插座（Socket）**：MES DBA创建 `MES_APS_Resource_View`（v5.0.13 命名统一，原名 `APS_Resource_View`）；预留 `EAM_APS_Resource_View`（未来 EAM 上线时由 EAM DBA 同构新建）
  - **数据插头（Plug）**：2号位在 APS 库创建 `ext_MES_APS_Resource_View`
  - **数据装载（Loader）**：2号位执行 `sp_SyncResourceData(@SourceType='MES')`（DDL v5.0.13 新增；与 `sp_SyncMasterData(@SourceType)` 同构）
- **动作**：从 ODS 的 `ext_MES_APS_Resource_View` MERGE 全量刷新 `Resource` 表（外部设备主数据镜像，v5.0 重构后 Resource 不再手工维护）
- **FactoryCode→FactoryId 映射**：SP 内部 JOIN Factory 表完成映射；查不到的行登记 `APS_ETL_Log` 跳过（不阻塞批次，与防腐层“永不阻塞”红线一致）
- **执行时间**：< 2 秒（Resource 变化频率低，全量刷新即可，不做增量）
- **删除策略**：v1 暂**不**自动停用源端没有的旧资源（避免误删），由 2 号位审阅后手工处置；未来业务确认可扩展为“源为权威”策略
- **EAM 扩展点**：EAM 上线时用 `sp_SyncResourceData(@SourceType='EAM')`指向 `ext_EAM_APS_Resource_View`，走查步骤无需改动（双源同构契约零分叉）

**00:15 - Routing同步（v5.0拆分为5表3视图）**（2026-04-18 更新）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **契约插座（Socket）**：MES DBA维护28张离散工艺表
  - **数据插头（Plug）**：3号位创建3个ODS视图：
    - `ext_MES_APS_Routing_Operation_View`（工序节点，输出MES_ID+Model）
    - `ext_MES_APS_Routing_Dependency_View`（工序依赖DAG）
    - `ext_APS_OperationResourceEligibility_View`（工序-设备能力关联）
  - **数据装载（Loader）**：2号位分别拉取到5个APS落地表：
    - `RoutingOperation`（工序节点，2号位通过MaterialMapping将MES_ID映射为MaterialId）
    - `RoutingDependency`（工序间有向依赖）
    - `OperationResourceEligibility`（工序-设备动态能力矩阵）
    - `RoutingPlanningParam`（排程规划参数：MinBatch/MaxBatch等）
    - `RoutingStage`（阶段字典/标准阶段码，3号位契约→2号位装载）
- **动作**：从ODS库同步v5.0拆分后的工艺路线数据（原单一`Routing`表已废弃）
- **执行时间**：约8秒（8000条路线，35000道工序）
- **⚠️ v5.0架构说明**：原`Routing`表已废弃，静态ResourceGroup也已废弃。组织归属改由`ResourceOrgGroup`维护，排程能力由`OperationResourceEligibility`动态建模

**00:20 - BOM批次展开请求**（2026-04-18 更新；2026-05-08 v3.7：订单级粒度 + RequestDetailId）：
- **负责人**：2号位（发起请求）+ 5号位（ODS递归展开+后置回填）
- **动作**：
  - **【2号位】** 调用ODS库API触发批次BOM展开（`POST /api/internal/v1/ods/bom/batch/request`）；明细已在00:00写入`MES_API_BOM_Request_Detail`（订单粒度）
  - **【5号位】** 在ODS侧执行`sp_ExpandBOMBatch`递归展开，产出写入`MES_APS_BOM_Workset`（含`RequestDetailId`追溯锚点）
  - **【5号位】** 调用 `sp_EnrichBOMWorkset(@BatchNo)` 后置回填 `ChildRequiredStageCode` + `ChildRequiredFactory`（R17 工厂映射），写入 `MES_APS_BOM_Workset_StageDetail`（含 EDGE+ROOT 双层路径），异常降级登记到 `MES_APS_BOM_Workset_Issues`（含`RequestDetailId`；永不阻塞批次）
	  - **【5号位】** `sp_EnrichBOMWorkset` 末尾调用 `sp_GenerateBOMCrossFactoryEdge(@BatchNo)`，基于 StageDetail(EDGE) 按 `StageSeq` 排序 + `LEAD`窗口函数生成跨厂边，`FromFactoryCode/ToFactoryCode` 通过 `StageCode→StageDict.FactoryCode` 取得（v3.14 跨厂边生成）
  - **⚠️ v5.0.21**：无BOMNO订单在本步由5号位从 `Model`/`MaterialCode` 推导BOM入口后展开
  - **⚠️ v5.0.28 R28/R29/R30/R31**：BOMNO IS NULL时按OrderType+MaterialCode前缀分流：
    - **R28**：SALES_ORDER+ASSY% → `ProcessCodeDict`出口库过滤首层BOMNO；CN6课无出口库时取CN出口库代理
    - **R29**：SALES_ORDER+WIP%/RAW% → 直接按MaterialCode查边（原行为）
    - **R30**：SALES_ORDER+RAW%+无BOM → 外购件兜底，静默跳过
    - **R31**：PRODUCTION_INSTRUCTION+BOMNO IS NULL → 直查+必写`BOMNO_MISSING_PRODUCTION` Issues（WARN=找到/ERROR=未找到）
- **执行时间**：约15分钟（活跃订单数 → 350万行 + StageDetail派生）

**00:30 - APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW + APS_BOM_CROSS_FACTORY_EDGE_RAW 拉取**（v3.14 更新 2026-06-23）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **数据插头（Plug）**：5号位负责ODS库的BOM递归展开+StageDetail双层结果
  - **数据装载（Loader）**：2号位从ODS库同时拉取到APS库：
    - `MES_APS_BOM_Workset` → `APS_BOM_RAW`（BOM展开主表）
    - `MES_APS_BOM_Workset_StageDetail` → `APS_BOM_STAGE_PATH_RAW`（阶段路径，含StageScopeType=EDGE/ROOT）
	    - `MES_APS_BOM_Workset_CrossFactoryEdge` → `APS_BOM_CROSS_FACTORY_EDGE_RAW`（跨厂边缓存，v3.14 新增）
- **动作**：使用SqlBulkCopy拉取350万行BOM数据 + 阶段路径明细 + 跨厂边
- **执行时间**：约5分钟

**00:35 - LLC计算**：
- **负责人**：2号位
- **动作**：执行`sp_CalculateLLC`，计算低阶码（Low Level Code）
- **执行时间**：约5分钟

**00:38 - ScheduleRun 预创建**（2026-06-12 v3.12 时序修正）：
- **责任人**：3号位 / `INightlyBatchOrchestrator`
- **动作**：只负责创建 `ScheduleRun` 记录（`RunType=FULL_SCHEDULE`，`Status=RUNNING`），确定并记录 `DataCutoffTime`；落库得到 `ScheduleRunId`
- **⚠️ 约束**：必须在 00:40 MES 快照同步前完成；`INightlyBatchOrchestrator`不直接调用三个MES同步服务。`DataCutoffTime` 一经确定，00:40 / 00:45 / 00:50 三个独立定时任务必须使用同一值；02:00 排程启动时**不再重新创建** ScheduleRun
- **执行时间**：＜1分钟

**00:40 - MES工单快照同步**（2026-06-12 v3.11 新增）：
- **负责人**：2号位
- **动作**：独立定时Job执行`sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime)`，从 `ODS.MES_APS_WorkOrder_View`（5号位收口）同步到 `APS.MESWorkOrderSnapshot`
- **V1 口径**：只接 ODS 汇总后的工单级关系，不接每条工单变更日志；`ScheduleRunId` 作为快照分区键，全量替换本次运行旧快照
- **⚠️ @DataCutoffTime 来源（三个快照 SP 统一规则）**：由调度器（Hangfire）在 `ScheduleRun` 记录创建时统一确定并传入；00:40 / 00:45 / 00:50 三次调用必须使用**同一个 `@DataCutoffTime`**。如 ODS 视图提供 `SourceUpdatedAt` 或 `LastReportTime`，应按 `<= @DataCutoffTime` 控制数据切片；无法提供来源时间的字段允许透传，差异登记 `APS_ETL_Log`
- **执行时间**：约1-2分钟

**00:45 - MES工序进度快照同步**（2026-06-12 v3.11 新增）：
- **负责人**：2号位
- **动作**：独立定时Job执行`sp_SyncOperationProgressSnapshot(@ScheduleRunId, @DataCutoffTime)`，从 `ODS.MES_APS_OperationProgress_View`（5号位 UNION ALL 收口，将加工+组装大工艺子视图合并）同步到 `APS.OperationProgressSnapshot`
- **V1 口径**：不接每条报工明细，只接 ODS 汇总后工序级进度；**工序识别主字段 = `OperationName`**（不以 MES 工序编码为主）；`RemainingQty` 为持久化计算列：`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END`
- **执行时间**：约2-3分钟

**00:50 - MES大工艺进度快照同步**（2026-06-12 v3.11 新增）：
- **负责人**：2号位
- **动作**：独立定时Job执行`sp_SyncStageProgressSnapshot(@ScheduleRunId, @DataCutoffTime)`，从 `ODS.MES_APS_StageProgress_View`（5号位 UNION ALL 收口，将加工+组装大工艺子视图合并）同步到 `APS.StageProgressSnapshot`
- **失败边界**：三类MES运行快照任一出现源不可访问、事务失败或契约字段错误，存储过程记录FAILED后必须`THROW`，对应ScheduleRun不得以空快照继续排程；业务上确实0行与技术失败必须区分。
- **V1 口径**：汇总颗粒度 = 生产指示号+物料编码+大工艺阶段码；`RemainingQty` 为持久化计算列：`CASE WHEN PlannedQty - ISNULL(GoodCompletedQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodCompletedQty,0) END`
- **⚠️ Task/Pegging 全量重算口径（写死）**：Task 和 Pegging 随新的 `PlanVersionId` 每日全量重新生成；MES 进度只用于计算当日剩余 Task 数量，**不匹配历史 TaskId**；Pegging 不跨版本复用
- **⚠️ EAM V1 预留**：`EAM_APS_Resource_View` 预留占位，V1 不读取 EAM 数据，不生成设备不可用窗口，此快照链路不受影响
- **执行时间**：约2-3分钟

**00:55 - 统一管道供给与Received事实同步**（v3.17现行口径）：
- **负责人**：2号位同步与标准化；5号位维护ODS来源契约和业务映射
- **动作**：执行 `sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, ...)`，从APS统一包装视图按 `UNION ALL` 读取已启用来源并重建本批次 `SupplyFact_Pipeline`：
  - 厂间在途（`INTERPLANT_IN_TRANSIT`，仅表达工厂间真实在途）；
  - 采购在途（`PURCHASE_IN_TRANSIT`）；
  - VMI现场/寄售可用供给（`VMI_ONSITE`，适用时）；
  - 已到厂未入库（`ARRIVED_NOT_RECEIVED`）。
- ERP Received按单据汇总视图独立同步/读取，严格按 `DocumentType + DocumentNo + Material + Factory` 使用，不与通用管道池混为一项来源。
- `ETA`保留来源事实，`AvailableTime`按ETA和规则参数派生；来源缺少有效ETA时按已确认默认LT降级并登记Issue。
- 管道/单据型供给按 `DOC|ERP|DocumentType|DocumentNo|MaterialCode|DestinationWarehouseCode` 生成`SupplyBusinessKey`；ODS按同一单据、物料和目的仓库汇总，V1不带行号。同一数量在Pipeline/Received表达之间不得重复；真正失去单据身份进入普通库存池后改用INV键，严格绑定供给不得在转换前丢失原DOC身份。
- 当前批次0行是合法业务结果，但不是通过固定空视图或强制TRUNCATE空跑实现。
- **DataCutoffTime一致性**：与00:38创建的ScheduleRun同一截止时间。
- **执行时间**：按本批次增量/重建规模控制，必须在02:00前完成。

**01:50 - 跨域依赖静态扫描**：
- **负责人**：2号位
- **动作**：扫描跨产品族BOM依赖，生成`Domain_Dependency`表
- **执行时间**：约5分钟

**02:00 - 排程启动**：
- **负责人**：3号位
- **动作**：Hangfire触发全量排程，快照读取APS数据库

**数据流向总览**（2026-06-15 v3.12 更新）：
```
白天  ODS.ext_v_APS_SalesOrder → APS.ERP_Order_Staging → sp_ValidateAndPromoteOrders（v5.0.27：#TargetStagingIds锁定+三级MaterialCode解析链+OrderType未知→FAILED+FactoryCode缺失→FAILED+FACTORY_CODE_MISSING+BOMNO_MISSING非阻断+CustomerSegment无匹配→UNKNOWN） → APS.Order_Canonical
00:00 APS.Order_Canonical（WHERE Status = 'OPEN'）→ 活跃根集合（订单粒度）→ ODS.MES_API_BOM_Request + MES_API_BOM_Request_Detail（v5.0.21：含无BOMNO订单）
00:05 APS.Order_Canonical → sp_SyncOrdersToPartitionTable → APS.Order（版本快照表，按合法 PlanVersionId 绑定写入；PlanVersion 未创建前只写 Canonical 或运行级临时承载）
00:10 ODS.ext_ERP_Master_View + ext_MES_Material_View → sp_SyncMasterData → APS.Material + MaterialMapping + MaterialSupplyContext
00:10 ODS.ext_MES_APS_Resource_View → sp_SyncResourceData(@SourceType='MES') → APS.Resource（与主数据并行，v3.4 新增）
00:15 ODS.3个Routing视图 → APS.RoutingOperation + RoutingDependency + OperationResourceEligibility + RoutingPlanningParam + RoutingStage
00:20 2号位请求 → ODS.sp_ExpandBOMBatch → sp_EnrichBOMWorkset（展开+R17工厂映射+阶段链+Issues） → ODS.MES_APS_BOM_Workset + StageDetail + Issues
00:30 ODS.MES_APS_BOM_Workset → APS.APS_BOM_RAW + ODS.StageDetail → APS.APS_BOM_STAGE_PATH_RAW (SqlBulkCopy)；ODS.MES_APS_BOM_Workset_CrossFactoryEdge → APS.APS_BOM_CROSS_FACTORY_EDGE_RAW（v3.14 跨厂边缓存）
00:35 APS.sp_CalculateLLC → APS.APS_BOM_RAW.LLC字段
00:38 INightlyBatchOrchestrator → 只创建 ScheduleRun（RunType/Status=RUNNING/DataCutoffTime/StrategyProfileVersionId/ExpectedDomainKeysJson 冻结确定）；不直接调用MES快照同步服务
00:40 ODS.MES_APS_WorkOrder_View（5号位收口）→ sp_SyncMESWorkOrderSnapshot → APS.MESWorkOrderSnapshot（v3.11 新增）
00:45 ODS.MES_APS_OperationProgress_View（5号位UNION ALL收口）→ sp_SyncOperationProgressSnapshot → APS.OperationProgressSnapshot（v3.11 新增）
00:50 ODS.MES_APS_StageProgress_View（5号位UNION ALL收口）→ sp_SyncStageProgressSnapshot → APS.StageProgressSnapshot（v3.11 新增）
00:52 sp_SyncInventorySnapshot(@BatchNo, @DataCutoffTime) → 六层库存正式链装载（按 @DataCutoffTime 切片，使用同一 @BatchNo）：
       InventoryFact_ERP（ERP Quantity已由ODS扣除WasterQty，表示净可用量） / InventoryFact_MES（源头事实）
       → InventorySupplyCandidate（候选供给，按批次/仓库/物料）
       → 应用 InventoryAvailabilityRule（无法区分客户数量的专属仓库整体排除；其他仓库按既有可用规则裁决）
       → 重建 InventoryAvailableSupplyDetail（扣减优先级明细，含来源解释）
       → 汇总 InventoryBalance（总量视图）
00:55 各ODS管道契约视图 → APS统一包装视图(UNION ALL) → sp_SyncPipelineSupply(@BatchNo,@DataCutoffTime) → SupplyFact_Pipeline
       另：ERP_Received_ByDocument_View → ext_ERP_Received_ByDocument_View（按单据严格消费，不池化）
01:50 APS.APS_BOM_RAW + Material + ProductFamily → APS.Domain_Dependency
02:00 APS数据库 → ScheduleContext（内存）[含MES三个快照、统一PipelineSupplies、ReceivedSupplies、ExecutionLock/HardLock；Task/Pegging/SOFT分配随新PlanVersionId重建，现实执行事实跨版本延续]
```

---

**⚠️ 架构红线说明**：
- **问题**：如果在02:00排程时等待 ERP 增量轮询完成，会被网络I/O和数据库写锁阻塞，15分钟高性能排程目标破产。
- **解决**：ERP 订单同步采用**时间戳增量轮询**（每小时/凌晨，详见附录A）；02:00 排程读取 00:38 已创建的 ScheduleRun，按其 `DataCutoffTime` 做**时间切片快照**，不等待任何未完成的同步任务。

#### 步骤1.1：瞬间快照读取（不等待下一轮轮询任务）

**⚠️ 前置条件**：数据准备阶段（00:05-00:55，含资源镜像刷新 v3.4、MES工单快照 v3.11、工序进度快照 v3.11、大工艺进度快照 v3.11、管道供给同步 v3.12）已完成，所有主数据、资源镜像、BOM、工艺路线、LLC、MES生产进度快照、统一管道供给和按单据Received事实已同步到APS库。

**动作**：
- **【２号位】** 在02:00:00读取00:38已创建的ScheduleRun，按其`DataCutoffTime`对APS数据库做时间切片快照（已包含00:05-00:35同步的所有数据）
- **【2号位】** 使用 SQL Server 快照读取避免被并发增量写入阻塞（详见附录A "数据库锁隔离"章节）
- **【2号位】** 如果此时增量轮询任务仍在写入，排程引擎**不等待**；但所有具备来源时间戳的数据必须严格按本次 `ScheduleRun.DataCutoffTime` 切片，不读取 DataCutoffTime 之后的数据；暂无来源时间戳的字段仅按已记录快照批次读取，并登记 `APS_ETL_Log`

**数据来源**：APS数据库当前快照  
**数据去处**：ScheduleContext（内存）

**数据流向**：
```
APS数据库（快照读取，RCSI隔离） → 2号位.瞬间抽取 → ScheduleContext（内存）
```

**⚠️ 架构契约**：
- 排程引擎**绝对不检查**下一轮轮询任务同步到了哪里；只按本次 `DataCutoffTime` 切片
- 如果 ERP 在 02:00:01 过来一个急单，只能等白天 Candidate 主链或明天的全量排程
- ERP 时间戳增量轮询与排程主流程在**物理调度上撕裂**，互不阻塞

---

#### 步骤1.2：主数据快照加载（2026-04-18 更新）

**动作**：
- **【2号位】** 直接读取当前数据库中最新且生效的主数据版本：
  - BOM：`APS_BOM_RAW` + `APS_BOM_STAGE_PATH_RAW`（含 EDGE/ROOT 双层阶段路径）
  - 工艺路线：`RoutingOperation` + `RoutingDependency` + `OperationResourceEligibility`（v5.0拆分后，原`Routing`已废弃）
  - 物料主数据：`Material` + `MaterialMapping` + `MaterialSupplyContext`
  - 排程参数：`RoutingPlanningParam`、`RoutingStage`（阶段字典）
- **【2号位】** 将该版本的主数据全量无脑加载到内存沙盘 `ScheduleContext` 中，供阶段 2 拆单使用

**数据来源**：APS 主数据表（APS_BOM_RAW、APS_BOM_STAGE_PATH_RAW、RoutingOperation、RoutingDependency、OperationResourceEligibility、Material、MaterialSupplyContext等）  
**数据去处**：ScheduleContext（内存）

**数据流向**：
```
APS.APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW + RoutingOperation + RoutingDependency + Material等 → 2号位.直接加载 → ScheduleContext（内存）
```

---

#### **步骤1.3：全量数据抽取与物理分池（供需分离）**

**动作**：
- **【2号位】** 按当前 `ScheduleRun.DataCutoffTime` 和当前 `DomainKey` 一次性装载：
  - 当前PlanVersion的 `Order` 快照；
  - BOM三张RAW及Routing三件套；
  - 资源、日历、生产部门Context；
  - 六层现货库存正式对象；
  - 管道供给、Received、采购在途/未结采购单等已启用供给主题；
  - `MESWorkOrderSnapshot / OperationProgressSnapshot / StageProgressSnapshot`；
  - 当前有效 `ExecutionLock` 和 `DemandSupplyHardLock`；
  - RuleSet、ParameterSet和StrategyProfile解析结果。
- **【2号位】** 在内存中建立互相隔离的集合：
  1. **初始顶层需求池**：仅客户订单和无父节点的顶层成品MTS/预测生产指示；厂间出荷指示和中间生产指示不得进入初始顶层需求池。
  2. **普通供给池**：现货库存、统一管道供给、Received、采购供给及尚未形成执行锁的生产指示总量边界。
  3. **现实执行与发布承诺供给池**：已被MES建单确认的ExecutionLock未来产出，以及尚未建单的PUBLISHED MESPlanRelease；两者固定其PI、Stage、数量和AvailableTime，普通产出归属仍可SOFT重排。
  4. **HardLock预占集合**：特殊出荷指示类型、客户专属、质量/环保资格、已拣选包装等不可换单数量，夜间先恢复并从竞争池移除。
  5. **运行时余额集合**：DemandBalance、SupplyBalance、PositionSliceBalance及本域现有内存记录集合 `PeggingLedgerEntries`；这些记录在逻辑上承担待持久化Ledger草稿职责，不新增平行的`PeggingAllocationLedgerDraft`代码类。
- **【2号位】** 明确生产指示的双层语义：
  - `Order.Quantity - Order.ReceivedQty` 是生产指示尚未最终入目标M库的唯一总量边界；
  - MES、XC、在途和Stage等待只说明该总量“在哪里”，不得作为额外供给再次加入总量池。

**数据去处**：`ScheduleContext`（当前Domain内存沙盘）

```text
外部/APS快照事实
→ 顶层需求池
→ 普通供给池
→ 执行锁定未来产出
→ HardLock预占
→ Demand/Supply/Position余额 + Ledger草稿
```

**架构红线**：
- 同一物理数量在任一时点只能进入一个供给事实分支；Pipeline与Received仍保留单据身份时使用同一DOC键防重复，进入普通池化库存并失去单据身份后使用INV键；严格绑定数量不得先丢失DOC身份再池化。
- `PeggingSupplyAllocation`是本次PlanVersion的结果，不得在阶段1重新当作候选供给。
- 初始需求池、厂间外部契约扫尾和中间生产指示孤儿扫尾必须分阶段执行，禁止在开始时混成一个需求池。

---

#### **步骤1.3.5：生产指示总量闭合与互斥位置快照**

**负责人**：2号位（装载、余额和落库）+ 5号位（Stage位置计算模块）

### 1. 总量边界

对每张生产指示：

```text
AvailableProductionQty = max(0, Order.Quantity - Order.ReceivedQty)
```

- `Order.Quantity`：ERP生产指示总数量；
- `Order.ReceivedQty`：最终已入目标M库的累计数量；
- 差额已经包含未开工、MES在制、Stage等待、生产指示级XC、厂间在途和末Stage完工待入库数量。

若 `ReceivedQty > Quantity`，本次可生产量按0处理，登记 `PI_TOTAL_BOUNDARY_INVALID`；不得产生负供给。

### 2. Stage累计量互斥化

- 大工艺判断以 `StageProgressSnapshot` 为权威；`OperationProgressSnapshot`只用于Stage内部小工序裁剪和诊断。
- 每个Stage累计完成量先转换为本次剩余总量范围内的完成量，并限制在 `[0, AvailableProductionQty]`。
- 下游Stage完成量超过上游时，**保守下修下游**并登记 `PI_STAGE_PROGRESS_NON_MONOTONIC`。
- 中间Stage缺失时，只采用下游已证明的最小完成量，并登记 `PI_STAGE_PROGRESS_MISSING`。
- 相邻累计完成量作差，形成互斥位置：未完成当前Stage、已完成当前Stage等待下一Stage、末Stage完成待入M等。

### 3. XC、在途与Stage位置

- `ProcessCodeDict.StageCode=<某Stage>` 且 `ERPProperty='XC'` 时，V1默认该数量位于该Stage投料前或Stage内，该Stage**尚未完成**：
  - `CurrentStageCode = NextRequiredStageCode = StageCode`；
  - 使用该XC供给后，仍须生成该Stage及后续Stage Task。
- 厂间在途、Stage等待、XC等只对互斥位置做进一步定位；定位后必须从原位置份额扣除，禁止双重保留。
- 无法定位的剩余数量写为 `UNLOCATED`，从生产指示承载路径的最早Stage保守生成Task，并登记 `PI_POSITION_UNLOCATED`。

### 4. 快照落库边界

每 `ScheduleRunId + DomainKey + ProductionInstructionNo` 保存：

- `ProductionInstructionSupplySnapshot`：指示总量、ReceivedQty、可生产量、已定位量、未定位量、DataCutoffTime；
- `ProductionInstructionPositionSlice`：PositionType、数量、Completed/Current/Next Stage、RemainingStagePath、AvailableTime、SourceBusinessKey和异常标记。

所有位置切片必须满足：

```text
SUM(PositionSlice.Quantity) = AvailableProductionQty
```

不闭合时登记 `PI_POSITION_NOT_CLOSED`，当前Domain不得发布正式版本。

### 5. 与历史Task的关系

- Task和物理Pegging仍随新PlanVersion全量生成；本步骤不尝试匹配历史TaskId。
- MES已建单的现实执行对象由`ExecutionLock`跨版本延续；PUBLISHED但尚未建单的发布承诺由`MESPlanRelease`跨版本延续。Stage快照不得覆盖或取消这两类真实承诺。
- V1报废、返工暂不额外折算，直接消费MES提供的大工艺汇总结果。

```text
Order总量边界
+ MES Stage累计事实
+ XC/在途定位事实
→ 5号位.PositionCalculationResult（只读）
→ 2号位.校验闭合并写PI Snapshot/PositionSlice
→ ScheduleContext.PositionSliceBalances
```

---

#### **步骤1.4：策略包解析与主题配置装载**

**动作**：
- **【2号位】** 读取当前 `ScheduleRun.StrategyProfileVersionId`，解析对应 `StrategyProfileVersion` 记录
- **【2号位】** 按 `StrategyProfileVersion` 装载：
  - 对应的 `RuleSetVersion`（规则集：Frozen/Firm 区天数、在途库存启用、插单优先级规则等）
  - 对应的 `ParameterSetVersion`（参数集：最小批量 MinBatchSize、换型时间 SetupTime 等）
- **【2号位】** 将解析结果写入 `ScheduleContext.RuleConfig` 和 `ScheduleContext.SchedulingParams`（内存）

**⚠️ 红线**：
- 1号位和 5号位**只消费**装载结果（`RuleConfig` / `SchedulingParams`），不得再从任何"统一策略表"另读一套规则
- 旧的 `APS.SchedulingStrategy` 单表模式已废弃；策略配置由 `StrategyProfileVersion` + `RuleSetVersion` + `ParameterSetVersion` 三张表共同承载，与 ScheduleRun 版本绑定

**数据来源**：`ScheduleRun.StrategyProfileVersionId` → `StrategyProfileVersion` → `RuleSetVersion` + `ParameterSetVersion`  
**数据去处**：ScheduleContext.RuleConfig / SchedulingParams（内存）

**数据流向**：
```
ScheduleRun.StrategyProfileVersionId → 2号位.策略包解析 → RuleSetVersion + ParameterSetVersion → ScheduleContext.RuleConfig/SchedulingParams
```

**业务意义**：策略以版本化的方式与 ScheduleRun 绑定，保证同一 ScheduleRun 内规则/参数不变；1号位纯内存推演不再触发额外 I/O。

---

### ⚙️ 阶段2：供需匹配与逻辑块生成（Pegging，不直接持久化正式Task）

**负责人**：2号位（余额、Ledger、状态与原子执行） + 5号位（少数策略接口与领域计算模块）

#### **步骤2.0：顶层需求排序（Order Prioritization）**

**动作**：
- **【2号位】** 将初始顶层需求池交给5号位的 `DemandPriorityPolicy`；该策略只消费已装载的订单字段、库存摘要和规则参数，不向数据库查询，不向下遍历BOM。
- **【5号位】** 返回只读 `PriorityResult`：排序键、优先级档位、证据和稳定次序键；这不是Voucher，也不改变任何订单或余额。
- **【2号位】** 根据本次流程层级的正式排序规则建立需求队列。V1不强制所有角色共用一个万能数字分数：
  - 客户订单按交期、客户等级、订单类别、Firm/Frozen等排序；
  - 顶层补库生产指示按消费月份、安全库存缺口等排序；
  - 不设置“SO永远先于所有生产指示”的硬编码层级；需要跨角色比较时由节点规则输出统一排序键。
- 同一层级排序必须使用稳定尾键，保证相同输入得到可重复结果。

```text
顶层需求池 → DemandPriorityPolicy → PriorityResult（只读） → 2号位.稳定排序 → DFS处理队列
```

---

#### **步骤2.1：DFS净需求、生产指示承接、位置绑定与原子Ledger**

**根需求身份红线**：
- DFS入口将顶层需求的`Order.OrderCanonicalId`写入`RootDemandOrderCanonicalId`和初始`DemandOrderCanonicalId`；
- BOM子节点原样继承根需求CanonicalId，不重新查库、不使用`Order.Id`、不得填0；
- 孤儿生产指示或厂间订单被提升为独立顶层需求时，使用其自身对应的`Order_Canonical.Id`；
- `ScheduleRunId`只取`PlanVersion.SourceScheduleRunId`，`DomainKey`只取`PlanVersion.DomainKey`，缺失即终止当前Domain。


**⚠️ 红线：Pegging阶段只分配数量，不创建正式Task。**

### 步骤2.1.1：先恢复不可竞争关系

- **【2号位】** 先装载并预扣当前有效 `DemandSupplyHardLock`；HardLock指定的需求、供给业务键和数量跨版本保持，其他需求不得竞争。
- **【2号位】** 恢复 `ExecutionLock`：固定现实MES工单、生产指示、Stage、剩余执行量和AvailableTime；不得重复创建同一执行任务。
- 对普通通用执行产出，只恢复为 `EXECUTION_LOCKED_OUTPUT`未来供给，原需求仅保留为追溯来源，不自动形成HardLock。
- 已实际领料、发料或正式实物预留的ExecutionLock投入供给保持固定；尚未实物预留的投入来源可以替换，但必须优先保证现实工单可继续执行。

### 步骤2.1.2：按需求顺序执行DFS

- **【2号位】** 按步骤2.0排序逐个处理顶层需求，沿BOM执行DFS并计算每个节点的阶段净需求。
- 相同物料跨Stage数量按1:1传递；只有物料编码变化时才按BOM用量展开。
- 5号位通过 `SupplyEligibilityPolicy` 过滤资格，通过 `SupplyRankingPolicy` 对合格供给排序；返回候选和原因，不直接修改余额。

### 步骤2.1.3：生产指示先承接数量，再绑定位置

当使用生产指示供给时分两步：

1. **Header承接**：决定“需求D由生产指示PI承接数量Q”；Q不得超过该PI的 `AvailableProductionQty`运行时余额。
2. **位置绑定**：在该PI的互斥 `PositionSliceBalance` 中，将Q绑定到具体Stage/XC/在途切片：
   - 已有执行事实和HardLock先固定；
   - 其余按 `AvailableTime`优先；
   - AvailableTime相同，优先已完成Stage更多、剩余路径更短；
   - 再按稳定 `SourceBusinessKey` 排序。

因此第6章意义的“哪张生产指示承接多少”与第7章意义的“从哪个Stage继续生产多少”严格分离。

### 步骤2.1.4：5号位返回分配决策，2号位原子执行

5号位返回：

```text
PeggingAllocationDecision
- DemandBusinessKey
- SupplyType / SupplyBusinessKey
- ProductionInstructionNo（适用时）
- PositionSliceId（适用时）
- Quantity
- AllocationMode = SOFT / HARD
- InheritedPriorityKey
- AvailableTime
- DecisionReason
```

2号位在一个原子动作中：

1. 校验需求余额足够；
2. 校验供给/位置切片余额足够；
3. 校验HardLock、资格和来源唯一键；
4. 同步扣减DemandBalance和Supply/PositionBalance；
5. 追加一行现有内存对象 `PeggingLedgerEntry`；该对象保存本次成功分配，后续批量映射到数据库表`PeggingAllocationLedger`；
6. 把优先级按**数量份额**向下游单据继承；
7. 若供给来源为非Task，则基于同一条`PeggingLedgerEntry`生成待持久化的`PeggingSupplyAllocation`结果；不新增第二套分配账本，也不写物理Pegging。

任何一步失败，整笔分配不得部分生效。

### 数量闭合红线

```text
需求原始量 = 已分配量 + 未满足量
供给原始量 = 已分配量 + 剩余量
PI可生产量 = 所有互斥位置切片之和
每笔Ledger数量同时存在于需求扣减和供给扣减两侧
```

同一数量不得同时以PI总量、XC、在途和执行锁产出四种身份重复分配。

---

#### **步骤2.2：不足识别、建议草稿与虚拟占位**

**动作**：
- **【5号位】** 的 `ShortageHandlingPolicy` 根据需求未满足余额和现有未来供给返回只读 `ShortageResult`；不直接创建ERP单据或正式Task。
- **【2号位】** 根据不足类型处理：
  1. **生产指示不足**：按来源订单生成 `ProductionInstructionSuggestionDraft`，保留RootDemand、Demand、Material、Factory、RequiredStage、ShortageQty、RequiredAvailableTime和原因；同时形成无正式生产指示的虚拟逻辑块。
  2. **采购不足**：沿用既定顺序——现货库存→已到厂未入库→采购在途/未明确→未结采购单剩余→虚拟采购占位；形成内存 `PurchaseSuggestionDraft`，V1不创建PR/PO。
  3. **数量足够但到达时间晚**：不生成数量不足建议，登记时间风险并交有限产能排程计算实际交付。
- 虚拟逻辑块后续可以形成`Task.IsVirtual=1`的可排程Task，用于暴露未来产能需求；但不得进入MES发布视图、不形成ExecutionLock、不跨版本保留。

**风险表达**：缺料及交期风险通过 `ScheduleExplanationFact`承载；不得把缺料写入 `Task.Status`。

```text
未满足余额 → ShortageHandlingPolicy → ShortageResult
→ 2号位.建议草稿/虚拟逻辑块/ExplanationFactDraft
```

---

#### **步骤2.3：换型属性提取（普通领域结果）**

- **【2号位】** 在构造LogicalBlock/TaskDraft时调用5号位换型属性模块。
- **【5号位】** 从已装载的工艺路线和物料属性中返回 `SetupAttributeResult`，例如模具、颜色、材质规格；它是只读Result，不是Voucher。
- **【2号位】** 将结果写入TaskDraft的换型属性，供1号位做换型优化。

```text
Routing/Material快照 → 5号位.SetupAttributeResult → 2号位.TaskDraft.SetupAttributes
```

---

#### **步骤2.4：同域跨厂厂间订单的物流逻辑块**

**适用范围**：只处理同产品族的厂间订单型跨厂链路；跨产品族域依赖仍由阶段0.5和第三部分处理。

- **【2号位】** 根据 `APS_BOM_CROSS_FACTORY_EDGE_RAW` 判定 `STAGE_HANDOFF` 或 `INTER_FACTORY_ORDER`。
- **【5号位】** 对候选厂间单据、运输LT、同单据在途和Received资格返回 `ShippingDecisionResult`；普通结果不叫Voucher。
- `INTER_FACTORY_ORDER`严格按同一出荷指示号处理，不进入全局池；特殊出荷指示类型可由HardLock规则固定到指定需求。
- **【2号位】** 原子扣减对应单据供给，并形成 `ShippingLogicalBlock/ShippingTaskDraft`；正式ShippingTask同样在有限产能/时间链评估后统一持久化。

```text
跨厂边 + 同单据供给 → ShippingDecisionResult → 2号位.物流逻辑块/TaskDraft
```

---

#### **步骤2.5：孤儿单据级联扫尾与拓扑拆解（Cascading Sweep）**

**动作**：顶层订单第一波拆解完毕后，**【2号位】** 必须按照严格的业务位阶，对"供给池"分两步执行级联扫尾：

**第一阶梯（外部契约扫尾）**：
- **【2号位】** 优先扫描供给池，将未被绑定的**"厂间销售订单（跨厂调拨单）"**强行转入"独立需求池"，保留其底薪分数（如 10 分）
- **【2号位】** 以它们为根节点启动 DFS 拆解。**（业务意义：它们在向下拆解时会自然扣减内部的加工 MTS 单据，防止跨厂发货与内部备货发生负荷重复计算）**

**第二阶梯（内部多级备货余量的"瀑布式"扫尾）**：
- 厂间订单全部扫尾拆解完成后，**【2号位】** 扫描供给池中依然未被绑定的各层级孤儿单据（如为了凑 OP 点下达的 加工 MTS、锻造 MTS、型材 MTS 等）
- **（⚠️架构红线：基于低阶码的拓扑降维）**：**【2号位】** 必须将这些孤儿单据按 BOM 结构从顶层向底层（低阶码 LLC 从高到低）进行排序
- 排序后，**【2号位】** 开启循环，将它们逐级转入"独立需求池（10 分）"并立即启动 DFS 向下拆解

**业务效果演示**：
```
示例：三级工艺链的瀑布式扫尾
- 供给池中有3个孤儿MTS：加工MTS(100件，LLC=0)、锻造MTS(150件，LLC=1)、型材MTS(200件，LLC=2)
- BOM关系：1件加工成品需要1件锻造半成品，1件锻造半成品需要1件型材原料

按LLC排序后的扫尾循环：

第1轮：加工MTS(100件) 转入独立需求池 → DFS拆解到底
  → 消耗锻造MTS 100件（供给池剩余：锻造MTS 50件）
  → 继续向下拆解，消耗型材MTS 100件（供给池剩余：型材MTS 100件）
  → 形成加工、锻造、型材LogicalBlock，后续统一展开TaskDraft

第2轮：锻造MTS(50件) 转入独立需求池 → DFS拆解到底
  → 消耗型材MTS 50件（供给池剩余：型材MTS 50件）
  → 形成锻造、型材LogicalBlock

第3轮：型材MTS(50件) 转入独立需求池 → DFS拆解到底
  → 形成型材LogicalBlock

业务效果：由于严格按层级循环，排在前面的高层级余量（如 加工 MTS）变为需求后，在向下拆解时，会自然去供给池"吃掉"一部分低层级的余量（如 锻造 MTS、型材 MTS）。等循环真正走到下一级（锻造 MTS）时，它可能已经被消耗光了。此机制完美避免了 N 层复杂工艺链下的"负荷虚假翻倍"，实现一波收敛。
```

**数据来源**：供给池中未被绑定的剩余单据（内存）  
**数据去处**：ScheduleContext.LogicalBlocks（内存，低优先级逻辑块）

**数据流向**：
```
第一阶梯：供给池.厂间订单 → 2号位.转入独立需求池 → 2号位.DFS拆解到底 → 消耗内部MTS
第二阶梯：供给池.多级生产指示 → 2号位.按LLC排序 → 逐级转入+DFS拆解到底 → 瀑布式消耗 → 低优先级LogicalBlock
```

**业务意义**：
- **外部契约优先**：厂间订单优先扫尾，确保跨厂协同不受内部备货干扰
- **避免负荷重复**：通过拓扑排序和瀑布式扫尾，避免多级工艺链下的机床负荷虚假翻倍
- **一波收敛**：高层级余量自然消耗低层级余量，确保所有 ERP 单据都被转化为底层 Task
- **见缝插针**：低分单据将在阶段3被1号位塞入机床的"产能白地"中

---

#### **步骤2.6：内存TaskDraft、有限产能接口与正式Task生成**

`SchedulingOrchestrator.RunSchedulingAsync`继续作为单Domain完整计算总入口。本步骤不得创建第二个总控服务，也不得在最终排程结果形成前写正式Task。

### 第一层：Phase 1.6只形成内存工艺任务骨架

- 复用现有工艺展开、Routing和依赖图生成逻辑；
- 产物改为内存`TaskDraft`，不INSERT数据库；
- TaskDraft使用本次PlanVersion内稳定的`TaskDraftKey`，它不是数据库TaskId；
- 每张TaskDraft携带`ProductionInstructionNo + StageCode + Operation + Quantity + DueTime + Routing + EligibleResources + Dependencies`。

### 第二层：Pegging纯内存分配并形成数量组成

每个PlanVersion的一次Pegging调用内部维护：

```text
nextAllocationSequence = 0
ledgerEntries = []
```

一笔分配只有在需求余额和供给余额均成功扣减后，才执行：

```text
AllocationSequence = ++nextAllocationSequence
追加PeggingLedgerEntry
```

规则：
- 每个PlanVersion从1开始且PlanVersion内唯一；
- 单Domain内按最新优先级顺序单线程扣减；不同Domain仍可并行；
- 不使用数据库Sequence，不在批量INSERT阶段生成，也不放入全局`SchedulingContext`；
- 失败分配不得留下半笔扣减或正式序号；
- Pegging结束后，TaskDraft的`Components`至少包含`AllocationSequence + ComponentQty`，并满足`SUM(ComponentQty)=TaskDraft.Quantity`。

### 第三层：1号位有限产能排程

> **接口边界**：本层没有`TaskDraft`数据库表。2号位将内存TaskDraft及全部排程约束组装为方法参数；1号位算法项目不得依赖`DbContext`、Repository、Dapper、`SqlConnection`或任何数据库读写组件。

1号位通过`SolveAsync(DomainSolveRequest)`接收由2号位组装并作为方法参数传入的TaskDraft、资源、日历、Routing、批量、交期、资格及ExecutionLock约束。上述对象全部位于内存中；1号位不读取任何数据库，也不持久化任何结果，只负责：

- 排定资源和时间；
- 按既定条件合并或拆分Task；
- 保留并转换每项`AllocationSequence`的数量份额；
- 返回原有求解摘要、最终Task草稿和份额映射。

最小输出语义：

```text
FinalTaskDraft
- FinalTaskDraftKey
- ProductionInstructionNo
- StageCode
- Operation
- Quantity
- PlannedStart/End
- ResourceId

AllocationShare
- AllocationSequence
- FinalTaskDraftKey
- ComponentQty
```

类名可以按代码约定调整，但上述业务字段不可缺失。

**数量守恒红线**：
- 每张最终Task的ComponentQty之和必须等于Task.Quantity；
- 一项AllocationSequence拆分到多张Task时，拆分前后总ComponentQty不变；
- 多项AllocationSequence合并为一张Task时，不得改变2号位已确定的需求优先级和供给归属。

**禁止合并条件**：
- 不同`ExecutionLockId`的现实MES工单；
- ExecutionLock TaskDraft与普通待下发TaskDraft；
- HardLock或客户/质量资格不兼容；
- 合并导致任一组成需求交期或产能约束失效。

### 第四层：2号位准备统一落盘

- 按`AllocationSequence`将`FinalTaskDraftKey + TaskComponentQty`回填到现有`PeggingLedgerEntry`；
- 正式Task尚未写库时只保存DraftKey；同一事务写入Task取得TaskId后再解析`FinalTaskId`；
- `AllocatedQty`继续表示供需分配量，`TaskComponentQty`表示最终Task中的需求组成份额；
- `vw_TaskDemandAllocation`只汇总`TaskComponentQty`。

```text
内存工艺TaskDraft
→ PeggingOrchestrator纯内存分配
→ PeggingLedgerEntry + 数量化TaskDraft
→ 1号位有限产能排定/合并/拆分
→ FinalTaskDraft + AllocationShares（仍为内存结果）
→ 2号位将FinalTaskDraft实例化并统一事务持久化为正式[Task]
```

**架构红线**：
- 禁止“先INSERT占位Task→Pegging DELETE→再INSERT正式Task”；
- PeggingOrchestrator不得INSERT/DELETE/UPDATE Task，不得直接写Ledger、PSA或物理Pegging；
- `Task.OrderId`仅为代表订单/兼容字段，需求数量归属以Ledger为权威；
- `Task.IsLocked`只表示计划冻结，现实MES执行只由ExecutionLock表达。

#### **步骤2.7：现有代码对象、统一Ledger与SupplyBusinessKey边界**

现有代码对象直接承接冻结设计：

| 对象 | 当前职责 | 本轮处理 |
|---|---|---|
| `PeggingRuleVoucher` | 5号位返回分配判断 | 可继续保留现有名称，业务契约等同AllocationDecision |
| `PeggingLedgerEntry` | 成功扣减后的内存分配记录 | 继续复用并补足持久化字段，不新建同义Draft类 |
| `PeggingAllocationLedger` | 数据库统一分配总账 | 由全部成功Entry在最终Domain事务中批量写入 |
| `PeggingSupplyAllocation` | 非Task供给分配结果 | 由对应Ledger生成，通过LedgerId关联；不能替代统一Ledger |
| 物理`Pegging` | Task-to-Task血缘 | 仅在正式Task生成后写入 |
| `ExecutionLock` | 跨版本现实MES工单身份 | 仅实现DDL规定的V1最小实体，不建设Link表或事件平台 |

### SupplyBusinessKey统一格式

各Supply Loader在装载来源数据时生成，通用候选对象和Pegging循环只复制，不重新拼接：

| 供给 | 格式 |
|---|---|
| 生产指示 | `PI|ProductionInstructionNo` |
| 普通库存 | `INV|ERP|FactoryCode|WarehouseCode|MaterialCode` |
| 单据型供给 | `DOC|ERP|DocumentType|DocumentNo|MaterialCode|DestinationWarehouseCode` |
| 采购单剩余 | `PO|ERP|PurchaseOrderNo|MaterialCode|ReceivingWarehouseCode` |
| 无PI历史MES工单 | `EXEC|MES|MESWorkOrderNo` |
| 虚拟PI占位 | `VIRTUAL_PI|RootDemandOrderCanonicalId|MaterialCode|StageCode` |

规范：
- 字符串Trim，英文代码转大写，使用`|`分隔；
- 不含数量、时间、TaskId、PlanVersionId或数据库自增Id；
- PI号全公司唯一，因此PI键不带工厂；
- ODS已按“单据+物料+目的仓库”汇总，DOC/PO键V1不带行号；
- 虚拟PI键只用于当前版本内存追踪，不进入HardLock、ExecutionLock、MES下发或现实非Task供给表；
- Pipeline与Received仍保留原单据身份时沿用DOC键且旧表示先退出；进入普通池化库存、失去单据身份后使用INV键。命中严格绑定的数量不得先丢失DOC身份再池化。

```text
5号位.PeggingRuleVoucher
→ 2号位原子扣减
→ PeggingLedgerEntry（内存）
→ 1号位最终Task及份额映射
→ 同一Domain事务：
   Task → Ledger → 非Task分配 → Task-to-Task Pegging
```

#### **步骤2.8：跨厂供给模式与当前V1供给顺序**

两类跨厂模式：

| 模式 | 判定 | 供给处理 |
|---|---|---|
| `STAGE_HANDOFF` | `ChildMaterialCode + ToFactoryCode`存在M库承接点 | 以生产指示总量闭合为边界，结合目标厂M/XC、同单据在途、Stage位置和上游生产确定剩余Stage Task |
| `INTER_FACTORY_ORDER` | 无M库承接点，按出荷指示形成厂间外部契约 | 严格同出荷指示号：先同单据在途→同单据ZP/BP Received→生产工厂内部供给/生产，不池化 |

**M库判定**：2号位消费 `MES_ProcessCode_View` 中由5号位同步维护的真实 `ERPProperty`，建立 `MaterialCode + FactoryCode → HasMStock` 索引；不得使用临时WarehouseRole或名称猜测。

### STAGE_HANDOFF顺序

在同一PI总量边界内定位：

```text
目标厂可直接使用M库
→ 目标厂当前Stage的XC/Stage等待
→ 同一生产指示/单据厂间在途
→ 上游Stage等待/在制/未开工
→ 正式PI不足建议或虚拟占位
```

- 使用 `CN_SURF_XC` 表示该数量尚未完成 `CN_SURF`，必须生成 `CN_SURF`及后续Task；
- 在途到达后从ETA/AvailableTime开始进入目标Stage；
- 所有位置份额之和不得超过PI的 `Quantity-ReceivedQty`。

### INTER_FACTORY_ORDER顺序

1. 锁定未完成的当前出荷指示号；
2. 查 `SupplyFact_Pipeline` 中相同 `SourceDocumentNo` 的厂间在途；
3. 查 `ext_ERP_Received_ByDocument_View` 中相同出荷指示号、物料、来源/收货工厂及ZP/BP属性的Received；
4. 不足部分进入生产工厂内部供给和生产；
5. 特殊出荷指示类型命中HardLock时，该供给数量跨版本固定给指定需求。

**红线**：
- 先在途、后Received，避免已发出数量仍留在出口库时重复计算；
- 出荷指示供给不进入全局公共池；
- 无法识别生产指示号或出荷指示号时，按“无该严格供给”处理，不猜测池化；
- 非Task供给分配写 `PeggingSupplyAllocation`，物流和生产Task之间的执行血缘才写物理Pegging。

---

#### **步骤2.9：物理身份与Hard/Soft归属的夜间双维恢复**

夜间全量不能把ExecutionLock、MESPlanRelease、HardLock和SOFT当成同一组可相加数量。必须先恢复互斥的**物理身份**，再在每项物理供给内部恢复**需求归属**：

**第一维：物理身份互斥重建**

1. **实际消耗事实**：永远不可重新使用；
2. **ExecutionLock现实执行**：现实工单必须继续，剩余投入需求和未来产出只计算一次；
3. **PUBLISHED MESPlanRelease发布承诺**：已向MES公布但尚未确认建单的数量跨版本保持，不生成新的`ReleaseItemKey`；
4. **普通PI位置切片、库存、在途和Received**：仅未被前三类物理身份占用的剩余数量进入普通竞争；
5. **未下发虚拟占位**：不跨版本恢复，本次重新生成。

**第二维：在每项物理供给内部恢复归属**

1. `DemandSupplyHardLock`先恢复严格绑定的需求—供给—数量关系；
2. Candidate的Scope外SOFT暂时保留，Scope内SOFT释放；夜间全量的普通SOFT全部按本次最新优先级重算；
3. 未被HardLock或有效SOFT占用的余额进入当前竞争池。

> HardLock只是归属属性，不产生新的物理数量；同一数量不得同时作为ExecutionLockedOutput、PUBLISHED发布承诺和普通PI竞争供给。

例如，昨日低优先级SO-L触发 `PI-1001/CN_SURF/WO-9001` 生产30件，今日来了高优先级SO-H：

- `WO-9001`仍继续生产30件；
- 若无HardLock，可将未来产出SOFT分配为SO-H 20、SO-L 10；
- 若其中10件命中特殊出荷指示HardLock，则该10件仍归原需求，只有剩余20件参与SOFT竞争。

这就是“执行做到底，普通产出归属可重排；严格绑定归属做到底”。

---

### ⚔️ 阶段3：纯粹的时空时序推演（排俄罗斯方块）

**负责人**：1号位（排程算法核心）

#### **步骤3.1：任务优先级排序**

**动作**：
- **【1号位】** 通过方法参数接收 **【2号位】** 传递的工序级 `TaskDraft`、依赖图、ComponentShare及ExecutionLock/HardLock约束（全部为内存对象；不存在TaskDraft表；1号位不查库）
- **【1号位】** 解析策略配置中的优先级规则
- **【1号位】** 对任务进行优先级降序排列（Priority DESC）

**数据来源**：2号位从`ScheduleContext`组装`DomainSolveRequest.TaskDrafts`并通过方法参数传入（内存；不存在TaskDraft表）  
**数据去处**：1号位内部优先级队列（内存）

**数据流向**：
```
2号位.DomainSolveRequest(TaskDrafts等内存对象) → 1号位.优先级队列排序 → 合并/拆分候选与内部优先级队列
```

---

#### **步骤3.2：时间槽寻址与排程**

**动作**：
- **【1号位】** 对每个TaskDraft及允许的合并/拆分候选，在设备日历空闲时间槽中执行“倒排寻址”或“撞墙翻转正排”
- **【1号位】** 使用 `IntervalTree`（时间线段树）进行极速检索
- **【1号位】** 考虑前置约束（上游ScheduledTaskDraft必须完成）
- **【1号位】** 考虑设备日历（班次、节假日、维修时间）

**数据来源**：1号位内部优先级队列 + ScheduleContext.ResourceCalendar（内存）  
**数据去处**：ScheduledTaskDraft.PlannedStartTime / PlannedEndTime、ResourceId及ComponentShares（内存）

**数据流向**：
```
优先级队列 + ResourceCalendar → 1号位.合并拆分评估与时间槽寻址 → ScheduledTaskDraft
```

---

#### **步骤3.3：换型优化启发式**

**动作**：
- **【1号位】** 当算法在某台设备的时间槽中寻找下一个可排产的TaskDraft/合并候选时
- **【1号位】** 如果存在多个候选任务，优先选择与上一任务 `SetupAttribute` 相同的任务
- **【1号位】** 这种局部微调不会破坏优先级大框架，但能显著提升瓶颈设备产能（5-15%）

**示例**：
```
注塑机刚完成模具A的任务
队列中有：模具A的任务、模具B的任务
→ 算法优先排模具A，避免换模时间损失
```

**数据来源**：TaskDraft.SetupAttribute（内存）  
**数据去处**：ScheduledTaskDraft排序及合并方案（内存）

**数据流向**：
```
TaskDraft.SetupAttribute → 1号位.换型优化算法 → ScheduledTaskDraft排序/合并微调
```

---

**阶段3产出**：
- ✅ 将TaskDraft在有限产能约束下形成ScheduledTaskDraft并填入时间轴
- ✅ 所有ScheduledTaskDraft获得精确开完工时间、资源和组成需求份额
- ✅ 通过换型优化，瓶颈设备（注塑、表面处理、型材挤压）的换型次数显著减少，产能利用率提升

**业务意义**：
- 让算法引擎保持绝对的纯粹性，专心压榨 CPU 算力
- 这是实现秒级/分钟级排程的核心底座
- 换型优化在不增加算法复杂度的前提下，通过简单的启发式规则即可获得显著的产能提升

---

### 🧠 阶段4：业务回填与战报生成（后置翻译）

**负责人**：5号位（业务规则） + 3号位（战报生成）

#### **步骤4.1：交期违约检查**

**动作**：
- **【5号位】** 调用 `IValidationRule` 策略接口，对排程结果进行业务校验
- **【5号位】** 检查订单交期违约：比较 `Task.PlannedEndTime` 与 **本 PlanVersion 隔离的 Order 快照的 `Order.CustomerDueDate`**（从 `ScheduleContext.Orders` 读取）
- **⚠️ 版本快照红线**：
  - 排程结果校验**必须**读取本 PlanVersion 分区内的 `Order` 快照字段；**禁止**读取实时 `Order_Canonical.DueDate`
  - Candidate 主链已明确"仅对 ScopeJson.OrderCanonicalIds 指定订单叠加 Order_Canonical 变化"；阶段4 若读取实时 `Order_Canonical` 会把未进入本次 Scope 的订单变更静默带入校验，破坏 Candidate 快照隔离
  - `Order_Canonical.DueDate` **只用于**"订单变更检测"场景（第四部分场景5）
- **【5号位】** 对延期订单生成 `ExplanationFactDraft`（`ReasonCode=DUE_DATE_RISK`），并回填 Order 快照的 `DelayStatus` 字段
- **【5号位】** 计算延期天数，作为 `ExplanationFactDraft.EvidenceJson` 的一部分

**⚠️ 架构说明**：1号位的有限产能寻址算法（Finite Capacity Scheduling）已保证设备负荷率≤100%。如果出现>100%，那是算法Bug，不是业务验证的范畴。业务验证关注的是"订单是否延期"，而非"设备是否超负荷"。

**数据来源**：ScheduleContext.Tasks + ScheduleContext.Orders（内存）  
**数据去处**：ScheduleContext.ValidationResult（内存）

**数据流向**：
```
ScheduleContext.Tasks + Orders → 5号位.交期违约检查 → ValidationResult（延期订单清单）
```

---

#### **步骤4.2：最晚需料时间推算**

**动作**：
- **【5号位】** 根据 Task 的开工时间减去采购提前期
- **【5号位】** 推算出 `latest_need_time`（最晚需料时间）
- **【5号位】** 更新采购建议的交期要求

**数据来源**：Task.PlannedStartTime（内存） + MaterialSupplyContext.LeadTimeDays（内存，v5.0仓库级上下文）  
**数据去处**：ScheduleContext.PurchaseSuggestionDrafts（内存对象，含 RequiredDate；V1 只作缺料建议）

**数据流向**：
```
Task.PlannedStartTime + MaterialSupplyContext.LeadTimeDays → 5号位.时间推算 → PurchaseSuggestionDraft.RequiredDate（内存）
```

---

#### **步骤4.3：ExplanationFactDraft 结构化原因事实生成**

**⚠️ 架构红线说明**：
- **问题**：在阶段4让3号位（接口域）直接在 `ScheduleContext`（内存沙盘）里生成文本战报，违反了模块边界。`ScheduleContext` 是1/2/5号位的私有领地，3号位不应触碰推演内存。
- **解决**：1号位/5号位在规则执行过程中生成独立的 `ExplanationFactDraft`（不打在 Task 对象上）；文本战报和 Summary 读模型由 2号位落库后异步生成，3号位/页面从 `ScheduleExplanationFact` 读取。
- **⚠️ 字段红线**：Task 表**不存在** `ReasonCode` / `ReasonMaterialId` / `ReasonResourceId` 字段；原因事实独立以 `ExplanationFactDraft` 承载，不写入 Task。

**动作**：
- **【1号位/规则执行过程】** 在推演中生成 `ExplanationFactDraft`（内存对象，一条对应一个"解释事实"），字段包括：
  - `ObjectType`：解释对象类型（Order / Task / Resource / StageCode）
  - `OrderId` / `TaskId` / `ResourceId` / `StageCode`：关联主键
  - `ReasonCode`：结构化原因枚举（正式值域见下文；如 MATERIAL_SHORTAGE / DUE_DATE_RISK / LOGISTICS_DELAY / CROSS_DOMAIN_VERSION_MISMATCH_RISK 等，全部取自权威 ReasonCode 列表，不得出现未登记值如 DUE_DATE_TIGHT / UPSTREAM_DELAY）
  - `Severity`：严重度
  - `ImpactHours`：影响时长
  - `EvidenceJson`：证据快照（如缺料的物料ID、产能不足的资源ID、上游延期天数等）
- **【5号位】** 参与规则判定，产出的原因信息以 `ExplanationFactDraft` 承载
- **⚠️ 红线**：Task 表**不存在** `ReasonCode` / `ReasonMaterialId` / `ReasonResourceId` 字段；原因不打在 Task 上，而是独立的 `ExplanationFactDraft` 集合

**数据来源**：ScheduleContext.Tasks + 规则判定结果（内存）  
**数据去处**：`ScheduleContext.ExplanationFactDrafts`（内存 IReadOnlyList，等待阶段5落盘为 `ScheduleExplanationFact`）

**数据流向**：
```
1号位/5号位规则执行 → 生成ExplanationFactDraft（内存） → ScheduleContext.ExplanationFactDrafts
```

**⚠️ 架构契约**：
- 原因结构化写入独立的 `ExplanationFactDraft`，不占用 Task 行；一个 Task 可对应多个 Fact，也可无 Fact
- 3号位**不触碰**推演期的内存沙盘
- 落盘后由 2号位批量写入 `ScheduleExplanationFact` 物理表

**业务意义**：结构化原因事实与 Task 解耦，支持一对多解释、按维度统计和跨版本对比。

---

### 💾 阶段5：极速落盘与零停机发布（完美收尾）

**负责人**：2号位（数据落盘） + 3号位（版本切换） + 5号位（冻结判定）

---

#### **步骤5.0：MES发布资格与发布单元草稿**

**负责人**：5号位（资格计算）+ 2号位（发布单元组装）+ 3号位（发布编排）

- **【5号位】** 根据冻结窗口、任务时间、规则参数和业务资格返回只读 `ReleaseEligibilityResult`；普通判断不称Voucher。
- **【2号位】** 为可发布任务计算发布分组：同一正式生产指示、同一Stage、同一执行批次的小工序Task可以组成一个 `MESPlanReleaseDraft`；不得跨PI、跨Stage或把两张未来MES工单伪装为一条发布记录。
- **发布数量红线**：`MESPlanReleaseDraft.Quantity`取该Stage级执行批次的单一流转数量，来源于发布草稿/LogicalBlock的Stage执行份额；**不得把NC、MC、精修等关联小工序Task.Quantity相加**。同组Task必须表达同一批流转数量；若存在换算、损耗或数量不一致，必须先按规则显式换算并闭合，否则拒绝组装发布单元。
- **【2号位】** 以下任务不得形成发布草稿：
  1. `Task.IsVirtual=1`；
  2. 已关联`ExecutionLockId`，表示已有现实MES工单；
  3. 已关联仍为`PUBLISHED/CONSUMED`的`MESPlanReleaseId`；
  4. 未进入冻结窗口或资格校验失败。
- 本阶段只形成内存草稿，不在PlanVersion尚未激活时向MES公开。正式发布发生在第八部分场景1。

```text
ScheduledTaskDraft
→ ReleaseEligibilityResult
→ 按PI+Stage+执行批次形成MESPlanReleaseDraft
→ 当前Domain落盘并激活
→ 激活后固化MESPlanRelease并通过视图供MES读取
```

---

#### **步骤5.1：当前Domain结果统一事务持久化**

**输入**：
- 最终Task/ShippingTask草稿；
- `FinalTaskDraftKey → AllocationSequence → ComponentQty`映射；
- 现有`PeggingLedgerEntry`集合；
- 非Task分配草稿；
- Task-to-Task Pegging草稿；
- PI快照、ExplanationFact及其他本Domain结果。

**显式事务顺序**：

```text
BEGIN TRANSACTION

1. 批量INSERT最终Task / ShippingTask
2. 建立FinalTaskDraftKey → TaskId映射
3. 按AllocationShares解析并回填Ledger.FinalTaskId + TaskComponentQty
4. 批量INSERT PeggingAllocationLedger
5. 仅对非Task供给批量INSERT PeggingSupplyAllocation
6. 批量INSERT Task-to-Task Pegging
7. 批量写PI快照、ExplanationFact及同批结果
8. 执行数量、引用、PlanVersion和Domain完整性检查

COMMIT
```

任一步失败：

```text
ROLLBACK当前Domain全部新结果
原ACTIVE版本保持不变
其他Domain不受影响
```

**落库前硬校验**：
- 每个PlanVersion内`AllocationSequence`唯一；
- Ledger需求侧、供给侧均不超分；
- PI位置切片之和等于PI可生产量；
- 每个FinalTask的ComponentQty之和等于Task.Quantity；
- 每个AllocationSequence在合并/拆分前后数量守恒；
- `FinalTaskId`与`TaskComponentQty`同时为空或同时有效；
- 一个Task最多关联一个ExecutionLock和一个MESPlanRelease；
- 虚拟Task不得携带ExecutionLock/MESPlanRelease；
- ScheduleRunId来自`PlanVersion.SourceScheduleRunId`，DomainKey来自`PlanVersion.DomainKey`，CanonicalId来自`Order.OrderCanonicalId`。

使用TVP、SqlBulkCopy、批量SP或等价集合写法；禁止在Pegging循环逐行写库，也不强制新建复杂Repository层。

成功落盘后，本域PlanVersion仍为`BUILDING`，等待步骤5.2独立发布事务。结果持久化事务不得与“旧ACTIVE归档+新版本激活”合并成长事务。

```text
FinalTaskDrafts + AllocationShares
+ PeggingLedgerEntries
+ 非Task/物理Pegging草稿
+ PI/Explanation结果
→ 单Domain显式事务
→ PlanVersion仍BUILDING
```

#### **步骤5.2：版本激活（v3.15 夜间主链口径）**

**动作**（`FULL_SCHEDULE` 凌晨主链）：
- **【2号位】** 阶段0已按每个 `DomainKey` 创建 `PlanVersion(Status=BUILDING, SourceScheduleRunId=ScheduleRun.Id, DomainKey=<当前域>)`
- **【2号位】** 步骤5.1 落库成功后，使用 **Serializable 事务**（或等价串行化控制）按 `DomainKey` 加锁执行原子切换：
  1. 幂等检查：本次 PlanVersion 已为 ACTIVE 直接返回成功
  2. 校验 `PlanVersion.Status = BUILDING`
  3. 将同 `DomainKey` 旧 ACTIVE 版本 `Status` 更新为 `ARCHIVED`
  4. 将本次 `PlanVersion.Status` 从 `BUILDING` 更新为 `ACTIVE`，同步写入 `ActivatedAt` + `ActivatedBy='SYSTEM'`
  5. 事务提交；任何一步失败全部回滚，旧 ACTIVE 继续生效（该域本次 PlanVersion 在有限重试期间保持 BUILDING，重试耗尽或确认不可恢复后必须转 FAILED）
- 正式采用 `PlanVersion.Status = ACTIVE` 表示当前生效版本，不使用独立的 `System_Active_Version` 表

**⚠️ 不写 ScheduleRun 全局状态**：本步骤只切换"当前域"的版本，绝不把整个 `ScheduleRun` 直接置 `COMPLETED`；`ScheduleRun` 终态由步骤5.3 汇总

**⚠️ 红线**：
- 凌晨路径也必须保证同 `DomainKey` 同时只有一个 ACTIVE，不得出现短暂或永久双 ACTIVE
- 归档与激活必须在同一事务内完成；不允许"先激活再单独归档"
- Domain 独立发布：一个 Domain 的激活事务失败，不影响其他已成功 Domain 的 ACTIVE 版本；不得因某域失败而回滚其他域

**数据流向**：
```
2号位按DomainKey逐个创建PlanVersion(BUILDING, SourceScheduleRunId, DomainKey)
→ 逐域步骤5.1落库（在有限重试期间保持 BUILDING，重试耗尽或确认不可恢复后必须转 FAILED）
→ 逐域[同一Serializable事务] 同DomainKey旧ACTIVE→ARCHIVED + 本次PlanVersion.Status=ACTIVE(ActivatedBy=SYSTEM)
→ 每域发布结果登记（成功/失败），交步骤5.3汇总
```

**⚠️ 红线**：`FULL_SCHEDULE` 是唯一无需 CANDIDATE_ACTIVATION 审批即可激活的 RunType；激活仍须走本步骤事务原子切换（不允许步骤5.1 落库时预先直接置 ACTIVE）。白天 Candidate 链路见第八部分场景4。

**⚠️ 一次 FULL_SCHEDULE ScheduleRun 与多个 Domain PlanVersion 的关系（V1 采用 Domain 独立发布）**：
- 一次 `FULL_SCHEDULE` 类型的 `ScheduleRun` 编排多个 Domain 计算，**每个 `DomainKey` 产生一个 `PlanVersion`**；一个 `ScheduleRun` 通过 `PlanVersion.SourceScheduleRunId` 关联多个 `PlanVersion`。
- 归属关系：每个 PlanVersion 通过 `SourceScheduleRunId` + `DomainKey` 归属于同一次运行和对应域。
- **V1 发布策略：Domain 独立计算、独立落盘、独立发布（废止旧 ALL_OR_NOTHING 全域一致发布）**：
  - 各 Domain 在自己的事务内完成"原 ACTIVE→ARCHIVED + 本次 BUILDING→ACTIVE"切换；
  - 一个无关 Domain 失败，**不得**阻止其他已成功 Domain 发布，已成功域的 ACTIVE 版本正式生效；
  - 失败域的本次 PlanVersion 在有限重试期间保持 BUILDING，重试耗尽或确认不可恢复后必须转 FAILED，其原有 ACTIVE 版本继续保持 ACTIVE；
  - **不采用**"某个 Domain 失败导致已经成功的无关 Domain 全部回滚"。
- **ScheduleRun 终态不在本步骤决定**，由步骤5.3 运行汇总统一判定为 `COMPLETED` / `PARTIAL_SUCCESS` / `FAILED`。

> **v3.13 四表收敛（2026-06-23，历史口径仅供追溯）**：
> - `FULL_SCHEDULE` 完成后，2号位在步骤5.2 事务原子切换阶段更新 `PlanVersion.Status=ACTIVE`（v3.15 时序修正：不在步骤5.1 落库时预激活）
> - `MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` / `SIMULATION` / `INSERT_ORDER_WHATIF` 类产出的 `PlanVersion` 默认状态为 **CANDIDATE**
> - 正式激活须 3号位通过版本激活 API 将 `PlanVersion.Status` 改为 `ACTIVE`
> - **落盘（步骤5.1）** 与 **激活（步骤5.2）** 是两个解耦的独立动作——任何 RunType 完成落盘后版本都存在，但只有 `FULL_SCHEDULE` 默认走步骤5.2；其余 RunType 停在步骤5.1，等待用户或系统显式激活
> - **⚠️ 禁止**：仿真版本 / WHATIF 版本 / 人工重排版本 自动覆盖当前正式版本

---

#### **步骤5.3：FULL_SCHEDULE 多Domain运行结果汇总（运行收口，仅适用于夜间 FULL_SCHEDULE）**

**负责人**：2号位 + 3号位

**⚠️ 适用范围**：本步骤**仅适用于夜间 `FULL_SCHEDULE` 多 Domain 运行**。白天 Candidate（单 Domain）的运行完成规则不同——其正常计算终点是 `PlanVersion.Status=CANDIDATE` 而非 `ACTIVE`，运行收口见第八部分场景4「白天 Candidate 运行完成规则」，`ScheduleRun` 仅进入 `COMPLETED` / `FAILED`，**不产生 `PARTIAL_SUCCESS`**。本步骤不得被描述为所有 RunType 的统一收口规则。

**触发**：所有 `ScheduleRun.ExpectedDomainKeysJson` 对应的 Domain 计算、落盘与发布均进入终态（ACTIVE 或 FAILED）后（**悬空域收口见下方 ⚠️**）。

**动作**：
- **【2号位】** 汇总本次 `ScheduleRun` 下各 Domain PlanVersion 的发布结果：
  - 全部预期 Domain 均：计算成功 + 落盘成功 + 对应 PlanVersion 已 ACTIVE → `ScheduleRun.Status = COMPLETED`
  - 至少一个预期 Domain 已成功发布（ACTIVE）**且**至少一个预期 Domain 失败 / 未启动 / 未完成发布 → `ScheduleRun.Status = PARTIAL_SUCCESS`
  - 运行级致命错误，或本次**没有任何**预期 Domain 成功发布 → `ScheduleRun.Status = FAILED`
- **【2号位】** 写入 `ScheduleRun.CompletedAt`（所有预期 Domain 进入终态并完成最终状态汇总时写入；`PARTIAL_SUCCESS` 同样写 `CompletedAt`）
- **【2号位】** 将汇总结果通知 3号位；若存在失败域，一并携带失败域清单
- **【3号位】** 若任一预期 Domain 失败，检查 `Domain_Dependency` 识别关联 Domain，产生 `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 原因事实（`ScheduleExplanationFact`）+ `RescheduleRecommendation`（列出建议一并重新计算的关联 Domain），交 PMC/0号位人工选择相关 Domain 重算（V1 不自动回滚已成功上游、不建跨域多 Domain Candidate、不建原子激活组）
- **⚠️ 定义约束**：`RUNNING` = 本次运行仍有预期 Domain 未进入终态；`COMPLETED` / `PARTIAL_SUCCESS` / `FAILED` 均为终态且均写 `CompletedAt`
- **⚠️ 悬空域收口**：调度器必须为 `ExpectedDomainKeysJson` 中的**每个** Domain 建立对应 `PlanVersion` 版本壳；某 Domain 未能启动，或版本创建后未能继续执行时，该 Domain 版本最终**必须转 `FAILED`**（不得长期保持 `BUILDING`，也不得以"无 PlanVersion 记录"作为悬空状态）。唯有全部预期 Domain 均进入 `ACTIVE`/`FAILED` 终态，本步骤汇总才能触发，保证 `ScheduleRun` 最终收口。

**数据流向**：
```
各域发布结果（成功/失败） → 2号位.汇总 → ScheduleRun.Status = COMPLETED / PARTIAL_SUCCESS / FAILED (+ CompletedAt)
→ 3号位.失败域关联分析 → 跨域风险原因事实 + RescheduleRecommendation → PMC/0号位人工决策
```

---

#### **步骤5.4：前端刷新广播**

**动作**：
- **【3号位】** 向全厂发送更新广播（通过 SignalR）
- **【4号位】** 前端甘特图瞬间刷新，显示新版本计划

**数据流向**：
```
3号位.SignalR广播 → 4号位.前端自动刷新
```

**业务意义**：
- 用户在白天看计划、拖拽排产时，即使后台正在进行 10 万级的数据重排
- 前端看板也绝对不会卡顿或锁死
- 实现真正的零停机（Zero-Downtime）体验

---

### 📝 阶段5.5：可解释性战报异步生成（后置翻译）

**时间**：阶段5落盘完成后，异步执行  
**负责人**：3号位（接口域）

**⚠️ 架构说明**：
- 1/2/5号位在阶段5落盘后工作结束，内存释放
- 结构化原因事实以 `ScheduleExplanationFact` 承载；3号位读取该表消费即可
- 彻底保证推演期内存沙盘的纯洁性

#### **步骤5.5.1：2号位批量落库 ScheduleExplanationFact**

**动作**：
- **【2号位】** 阶段5落盘时，将内存中的 `ScheduleContext.ExplanationFactDrafts` 批量写入 `ScheduleExplanationFact` 表（与 Task/Pegging 同一批次）
- **【2号位】** `ScheduleExplanationFact` 字段：`ObjectType` / `OrderId` / `TaskId` / `ResourceId` / `StageCode` / `ReasonCode` / `Severity` / `ImpactHours` / `EvidenceJson` / `PlanVersionId`

**数据来源**：ScheduleContext.ExplanationFactDrafts（内存）  
**数据去处**：APS.ScheduleExplanationFact 表（结构化原因事实承载）

---

#### **步骤5.5.2：2号位异步生成 Summary 读模型**

**动作**：
- **【2号位】** BackgroundService 异步生成三张 Summary 读模型（非阻塞版本激活）：
  - `OrderScheduleSummary`（订单维度：延期原因、影响小时、证据摘要）
  - `ResourceLoadSummary`（资源维度：瓶颈原因）
  - `PlanKpiSummary`（版本级：Top 原因分布）
- 读模型基于 `ScheduleExplanationFact` 聚合，不依赖 Task 表任何 Reason 字段

**数据来源**：APS.ScheduleExplanationFact  
**数据去处**：APS.OrderScheduleSummary / ResourceLoadSummary / PlanKpiSummary

---

#### **步骤5.5.3：3号位/页面消费**

**动作**：
- **【3号位】** 页面查询时读取 `ScheduleExplanationFact` + Summary 读模型，返回结构化事实
- **【4号位】** 前端可选择展示为文本战报（`ExplainTrace` 作为文本展示层，从 `ScheduleExplanationFact` 派生，不作为原因存储对象）

**⚠️ 红线**：
- Task 表**不存在** ReasonCode/ReasonMaterialId/ReasonResourceId 字段；不得从 Task 表读取原因
- `ScheduleExplanationFact` 才是结构化原因事实的唯一承载表
- `ExplainTrace` 若存在，仅作为文本展示层，不承担原因存储职能

**数据来源**：APS.ScheduleExplanationFact + Summary 读模型  
**数据去处**：前端结构化展示 / 可选文本战报视图

**数据流向**：
```
1号位/5号位规则 → ExplanationFactDraft（内存） → 2号位批量落盘 → ScheduleExplanationFact表
→ 2号位异步聚合 → OrderScheduleSummary/ResourceLoadSummary/PlanKpiSummary
→ 3号位/页面消费
```

**业务意义**：
- 结构化事实支持一对多解释、多维度聚合和跨版本对比
- 不占用 Task 行，不污染排程内存沙盘

---

#### **步骤5.5.4（v3.8 新增）：读模型异步后处理**

**时间**：Task/ExplanationFact 落库完成后异步执行（非阻塞）  
**负责人**：2号位（BackgroundService）

**动作**：
- **【2号位】** 异步扫描当前 `PlanVersionId` 下已落库的 Task / ExplanationFact
- **【2号位】** 生成 `OrderScheduleSummary`（订单级计划完工 / 延期 / 风险 / 主因代码）
- **【2号位】** 生成 `ResourceLoadSummary`（资源×日期：负荷小时 / 负荷率 / 是否瓶颈）
- **【2号位】** 生成 `PlanKpiSummary`（版本级：准交率 / 延期订单数 / VIP延期 / 平均负荷率 / 瓶颈数）

**⚠️ 架构红线**：这三张读模型表**不参与排程内核**；生成失败不影响版本激活；阶段一 DDL 骨架即用，Batch 3 补完整索引。

---

#### **步骤5.5.5（v3.8 新增，v3.15 更新）：白天 Candidate 入口与仿真骨架**

**位置**：凌晨主链之外的独立触发路径（白天实时）

**正式实装（v3.15）**：白天 Candidate 主链按既定五种 RunType + Purpose 合法组合执行（完整合同见集成接口 §11.2 与 §11.4；`LOCAL_RESCHEDULE` / `MANUAL_RESCHEDULE` / `INSERT_ORDER_WHATIF` 为 RunType，`CTP` / `INSERT_IMPACT_ANALYSIS` / `INSERT_RESCHEDULE` / `MANUAL_ADJUSTMENT` 为 Purpose，两级枚举不得混写），完整流程见第八部分场景4。

**阶段二骨架预留（未实装）**：
> - `SIMULATION`（仿真）类 `ScheduleRun`：骨架预留，当前未实装
> - `ScenarioObjectiveScore`：多目标比较评分，骨架预留，当前未实装
> - `INSERT_IMPACT_ANALYSIS`（CTP 插单可行性分析）：已实装，产出 CANDIDATE 版本但永不激活，仅供评估
> - 仿真入口复用同一排程内核（1号位），基于 `BasePlanVersionId` + 假设条件加载不同 ScheduleContext 快照
> - `Scenario` 容器：`Purpose=CTP` 或 `Purpose=INSERT_IMPACT_ANALYSIS` 时**通常创建** Scenario，用于保存假设、目标和评估上下文；在无需独立业务场景容器时允许不创建，`ScheduleRun.ScenarioId` 可为空；其余 Purpose 不要求

---

## 第二部分：跨厂协同全流程（4个场景）

跨厂协同是指订单需要在多个工厂或产品族之间流转时的协调机制。以下是4个典型场景：

---

### 🏭 场景1：同域跨厂物流（厂间订单发货Task）vs 异域跨族依赖（虚拟库存硬约束）

**⚠️ 架构红线说明**：
- **同域跨厂**（内政）：A厂和B厂都生产同一产品族X，半成品在厂间流转
  - 机制：5号位返回同单据物流决策，2号位形成ShippingLogicalBlock/ShippingTaskDraft，1号位排定时间后再持久化正式ShippingTask
  - 特点：不影响域调度顺序，且排程结果带有真实 ERP 调拨单号
  - 示例：A厂生产产品族X的半成品 → 运输到B厂 → B厂继续生产产品族X的成品
- **异域跨族**（外交）：产品族A消耗产品族B的半成品
  - 机制：01:50静态扫描生成 `DomainDependency` 表 + 02:00上游落盘后下游读取为**虚拟库存硬约束**
  - 特点：必须提前确定域调度顺序（上游先排，下游后排）
  - 示例：产品族B（电机）先排 → 落盘 → 产品族A（整机）读取为虚拟库存 → 后排

**流程步骤**：

#### **步骤1.1：同域跨厂场景（基于真实单据的发货Task）**

**负责人**：5号位（业务规则）

**触发条件**：订单的BOM树中存在同产品族、跨工厂的物料需求，且供给池中有真实的厂间销售订单

**动作**：
- **【5号位】** 在排程时检测到跨厂需求
- **【5号位】** 在供给池中寻找对应的"厂间销售订单"，计算物流耗时（Duration），并向 **【2号位】** 返回包含**【发货Task生成指令与ERP单号】**的 `ShippingDecisionResult`
- **【2号位】** 接收结果，在内存沙盘中统一建立Ledger、ShippingLogicalBlock和ShippingTaskDraft；正式ShippingTask待1号位排定后持久化

**示例**：
```
订单SO001：产品族X（整机）
- A厂生产半成品（产品族X）
- ERP提前下达厂间销售订单：SO-Inter-001（A厂→B厂）
- B厂继续生产成品（产品族X）

→ 5号位在供给池中找到厂间订单 SO-Inter-001
→ 5号位返回物流决策，2号位形成ShippingTaskDraft：
  - 前置Task：A厂产品族X半成品生产
  - ShippingTask (ERP单号: SO-Inter-001)：运输（2.5天）
  - 后置Task：B厂产品族X成品生产
```

**数据来源**：供给池中的厂间销售订单（内存）  
**数据去处**：ScheduleContext.ShippingTaskDrafts（内存）

**⚠️ 架构契约**：
- 真实发货Task**只用于同域跨厂**场景
- 不影响域调度顺序（因为都是同一个产品族域）
- 在单个域的排程算法内部处理
- 排程结果带有真实 ERP 调拨单号，可直接指导车间/物流发货

---

#### **步骤1.2：异域跨族场景（虚拟库存硬约束）**

**负责人**：2号位（数据基础设施）

**触发条件**：订单的BOM树中存在跨产品族的物料需求

**动作**：
- **【2号位】** 在01:50静态扫描时，已通过SQL将跨产品族依赖固化到 `DomainDependency` 表（见阶段0.5）
- **【2号位】** 在02:00排程时，按拓扑顺序执行：
  - 上游域（产品族B）先排，立即落盘
  - 下游域（产品族A）启动前，读取上游落盘结果，构建**虚拟库存**（带AvailableTime）
  - 下游域排程时，虚拟库存的AvailableTime作为**硬约束**，算法自动"撞墙"推迟

**示例**：
```
订单SO002：产品族A（整机）需要产品族B（电机）的半成品

01:50静态扫描：
- SQL扫描生成：DomainDependency（产品族B → 产品族A）

02:00排程：
- 拓扑排序：产品族B先排，产品族A后排
- 产品族B排完，立即落盘（电机完工时间：3月3日14:00）
- 产品族A启动前，读取产品族B结果，构建虚拟库存：
  - MaterialId = 电机
  - AvailableTime = 3月3日14:00 + 物流2天 = 3月5日14:00
- 产品族A排程时，装配Task尝试排在3月1日→撞墙→自动推迟到3月5日14:00
```

**数据来源**：APS.Task表（上游域已落盘） + DomainDependency表  
**数据去处**：下游域.ScheduleContext.VirtualInventory（内存）

**⚠️ 架构契约**：
- 虚拟库存硬约束**只用于异域跨族**场景
- 必须严格按拓扑顺序执行（上游先排，下游后排）
- 上游的时间自动变成下游的物理时间墙

---

### ⚡ 场景2：跨域优先级继承（01:50预处理刷库）

**时间**：凌晨 01:50（与跨域依赖静态扫描同步执行）  
**负责人**：2号位（数据基础设施）

**⚠️ 架构红线说明**：
- **问题**：如果在02:00内存排程期间动态传递优先级（下游紧急订单提升上游优先级），会导致"时间倒流悖论"——上游域已经排完了，无法再调整优先级。
- **解决**：优先级继承在**本次运行的 ScheduleContext 构建阶段**完成，产出 `OrderPrioritySnapshot` 内存运行级快照，作为本次 PlanVersion 的排程输入；不修改 `APS.Order` 表任何字段（Order 是 Append-Only 版本数据，UPDATE 会污染历史 PlanVersion 分区）。

**流程步骤**：

#### **步骤2.1：识别跨域紧急订单**

**⚠️ 架构红线**：
- `Order` 表按 `PlanVersionId` 分区，是 Append-Only 版本快照；**禁止**在 01:50 无条件全局 UPDATE `APS.Order`，会污染历史 PlanVersion 分区
- 凌晨 PlanVersion 在 02:00 才创建（本次 SourceScheduleRunId 指向 00:38 已建 ScheduleRun）；01:50 时点还没有当前 PlanVersionId 可写
- `Order.Priority` 是 INT（不是字符串）；`Material.MaterialType` 枚举为 `SEMI_FINISHED`（不是 'SemiFinished'）
- `APS.Order` 表**不存在** `PrioritySource` / `UpdatedAt` 字段；`APS.PriorityInheritanceLog` 表**未在 DDL 中定义**
- v5.0 已废弃 `BOM` 表，正式对象是 `APS_BOM_RAW`（字段名以 RAW 表口径为准）

**动作**：
- **【2号位】** 优先级继承在**本次运行的 ScheduleContext 构建阶段**执行，产生**内存运行级快照**：
  1. 读取当前运行 Order 快照 + 当前 BatchNo 的 `APS_BOM_RAW`（按活跃批次过滤）
  2. 识别高优先级订单（`Order.Priority` 落入紧急阈值区间，INT 比较）
  3. 通过 `APS_BOM_RAW` 追溯上游半成品 Material（`Material.MaterialType = 'SEMI_FINISHED'`）
  4. 计算上游订单的 `InheritedPriority` / `PriorityScore`，写入 `ScheduleContext.OrderPrioritySnapshot`（内存）
- **【1号位】** 排程算法从 `ScheduleContext.OrderPrioritySnapshot` 读取继承后优先级，不读取 `Order.Priority` 原始字段
- **审计留痕**：继承事实作为 `ExplanationFactDraft`（`ReasonCode=PRIORITY_INHERITANCE`, `EvidenceJson={UpstreamOrderId, DownstreamOrderId, InheritedPriority}`），阶段5 落盘为 `ScheduleExplanationFact`

---

#### **步骤2.2：Domain_Dependency 扫描按当前 BatchNo 过滤**

**动作**：
- **【2号位】** 01:50 扫描 `APS_BOM_RAW` 生成 `Domain_Dependency` 时，**必须**加入当前有效 BOM `BatchNo` 过滤，不得扫描 `APS_BOM_RAW` 中所有历史/实时 BatchNo
- 结果写入 `Domain_Dependency`（该表为跨域依赖静态扫描结果，非订单表）

**⚠️ 红线**：
- 不使用已废弃的 `BOM` / `ComponentMaterialId` / `Material.Type` 口径
- 不 UPDATE `APS.Order` 全量历史分区
- 不写入 `PriorityInheritanceLog`（DDL 未定义）

---

#### **步骤2.3：02:00 排程直接消费内存 OrderPrioritySnapshot**

**动作**：
- **【2号位】** 阶段1 装载 `ScheduleContext` 时，将 `OrderPrioritySnapshot` 一起装入内存
- **【1号位】** 排程算法直接消费内存 `OrderPrioritySnapshot`，不读取 `Order.Priority` 原始值
- **绝对禁止**在内存排程期间动态调整优先级

**数据流向**：
```
Order快照 + 当前BatchNo APS_BOM_RAW → 2号位.内存计算(InheritedPriority/PriorityScore)
→ ScheduleContext.OrderPrioritySnapshot → 1号位.排程算法消费
优先级继承事实 → ExplanationFactDraft → 阶段5落盘为 ScheduleExplanationFact
```

**架构收益**：
- 不触碰历史 PlanVersion 分区，Append-Only 语义保持
- 继承事实通过 `ScheduleExplanationFact` 结构化留痕，不需要自创 `PriorityInheritanceLog`

---

### 📦 场景3：同域跨厂半成品在途管理（厂间物流发货Task场景）

**触发条件**：同产品族、跨工厂的半成品正在运输中（如：A厂产品族X半成品运往B厂）

**⚠️ 架构说明**：
- 此场景**只适用于同域跨厂**（基于真实厂间订单的发货Task）
- **异域跨族**场景不存在"在途管理"，因为上游落盘后下游直接读取虚拟库存，无需跟踪运输状态

**流程步骤**：

#### **步骤3.1：在途状态更新**

**负责人**：3号位（物流执行编排）+ 2号位（事实快照）

**动作**：
- **【2号位】** 从MES物流/发货实时契约视图读取当前累计物流事实并形成运行快照
- **【3号位】** 根据快照识别半成品已发出、在途或到达状态（同产品族、跨工厂）
- **【3号位】** 更新物流发货 Task（ShippingTask）的**物流执行状态**（正式物流状态/物流实绩对象承载，**非普通生产 Task.Status**；Task 表无 `IN_TRANSIT` 状态字段，也无 `ETA` / `ActualArrivalTime` 字段）
- **【3号位】** 记录物流计划到达时间与预计到货时间（属 ShippingTask 正式**物流计划属性**，须由 ShippingTask 正式契约/DDL 补齐；V1 暂以物流实绩事件与计划属性描述，**不写入现有 Task 物理字段**）

**示例**：
```
A厂产品族X半成品完工 → 发货到B厂
→ MES物流视图显示ShippingTask_12345（ERP单号: SO-Inter-001）已发出
→ APS按稳定物流单据键映射ShippingTask，更新其物流执行状态（发出/在途），并记录物流计划到达时间（物流计划属性）
```

**数据来源**：MES物流/发货实时契约视图的当前累计事实  
**数据去处**：ShippingTask 物流执行状态/物流计划属性（正式承载待 ShippingTask DDL 补齐；V1 不写入普通 Task 表）

**数据流向**：
```
MES物流实时视图 → 2号位.形成截止时间快照 → 3号位.按稳定物流单据键映射ShippingTask → 更新物流执行状态/物流计划属性（非Task.Status）
```

---

#### **步骤3.2：延迟处理（生成原因事实+前端标红+PMC决策）**

**负责人**：5号位 + 4号位（前端）

**⚠️ 架构红线说明**：
- **物理常识**：APS的计划（Plan）是固化在数据库里的静态时间戳。如果在白天的动态执行中，卡车晚点了，下游机床（后续Task）原本计划在10:00开工的数据，**绝对不可能"自动推迟"**，除非启动1号位的排程算法（重排程）。
- **V1.0保守原则**：系统原则上不自动触发重排，避免白天车间计划频繁震荡。决策权交还给PMC。

**动作**：
- **【5号位】** 调用 `IInTransitRule` 策略接口，检测运输延迟
- **【5号位】** 如果实际到货时间 > 预计到货时间，生成 `ExplanationFactDraft(ReasonCode=LOGISTICS_DELAY)`（业务风险由 `ScheduleExplanationFact` 承载，不写入 Task 字段）
- **【5号位】** 生成延迟通知，发送给PMC
- **【5号位】** **该后续Task的计划开工时间在数据库中保持不变**
- **【4号位】** 前端甘特图将该Task标红显示，提示PMC存在物流延迟风险
- **【0号位】** PMC评估后决策：手动调整计划，或主动点击"触发局部重排"

**示例**：
```
预计到货：3月3日10:00
实际到货：3月5日14:00（延迟2.17天）

→ 5号位生成 ExplanationFactDraft：ObjectType=Task, ReasonCode=LOGISTICS_DELAY, ImpactHours=52, EvidenceJson={DelayDays: 2.17}
→ 后续Task的计划开工时间在数据库中保持不变（仍为3月3日12:00）
→ 前端甘特图将该Task标红，显示"物流延迟2.17天"
→ PMC看到告警后决策：
  - 选项1：手动调整该Task的开工时间
  - 选项2：点击"触发局部重排"，由1号位重新计算
  - 选项3：接受风险，等明天凌晨02:00全量排程自动修正
```

**⚠️ 字段红线**：Task 表**不存在** `ETA` / `ActualArrivalTime` 字段。物流到达时间由 `ShippingTask` 正式字段或物流事件/管道供给对象承载；未来若需要，须在 DDL 和字段说明中正式补入，不得只在流程文档中直接使用。

**数据来源**：ShippingTask 计划到达时间 vs 物流实绩事件（含 EventTime）  
**数据去处**：ExplanationFactDraft(ReasonCode=LOGISTICS_DELAY) + 延迟通知 + 前端标红

**数据流向**：
```
ShippingTask 计划到达 vs 物流实绩事件 → 5号位.延迟检测 → 生成 ExplanationFactDraft（数据库时间不变） → 前端标红 → PMC决策
```

**⚠️ 架构契约**：
- **绝对禁止**"不重排但时间自动推迟"的魔法逻辑
- 数据库中的Task开工时间保持不变，只打标签
- 前端甘特图通过标红提示PMC
- 决策权交给PMC，由PMC选择是否触发重排

---

### 🤝 场景4：异域跨族上游延期自动顺延（虚拟库存硬约束场景）

**触发条件**：异域跨族场景中，上游域排程完成，半成品完工时间晚于预期

**⚠️ 架构红线说明**：
- **问题**：如果上游延期后"通知下游、触发下游重排"，会导致多余的重排机制，增加系统复杂度和震荡风险。
- **解决**：在DAG批处理架构中，上游延期会自动转化为下游虚拟库存时间的推迟，下游算法会自动顺延，无需任何重排。

**流程步骤**：

#### **步骤4.1：上游域排程完成，立即落盘**

**负责人**：2号位

**动作**：
- **【2号位】** 上游域（如：产品族B-电机）排程完成
- **【2号位】** 使用 `SqlBulkCopy` 将上游域的Task结果立即落盘到数据库
- **【2号位】** 半成品完工时间已固化（如：原计划3月1日10:00，实际排到3月3日14:00，延期2.17天）

**数据来源**：域B.ScheduleContext（内存）  
**数据去处**：APS.Task表（域B已落盘）

---

#### **步骤4.2：下游域读取上游结果，虚拟库存时间自动推迟**

**负责人**：2号位

**动作**：
- **【2号位】** 下游域（如：产品族A-整机）启动前，从数据库读取上游域刚落盘的Task结果
- **【2号位】** 构建虚拟库存时，`AvailableTime` 自动使用上游的实际完工时间（3月3日14:00 + 物流默认提前期 `DefaultLeadTimeDays` = 3月5日14:00）
- **【2号位】** 虚拟库存的时间已经包含了上游延期，无需额外通知

**示例（正式业务伪代码，V1 落盘前由2号位据实际 DDL 实现；不得直接复制为生产 SQL）**：
```
# 跨域虚拟库存构建（域A下游 读取 域B上游 已落盘 PlanVersion）
# 输入：@UpstreamPlanVersionId, @UpstreamDomainKey, @CurrentDomainKey, @CurrentBatchNo

1. 筛选上游 PlanVersion 中"代表最终产出"的工序 Task
   （每物料唯一一道最终产出工序 Task；多工序情况下仅取最终产出工序，
    不可汇总该物料的全部工序 Task，避免重复放大产出数量）
2. 按 MaterialId 汇总唯一产出数量
   （使用域输出供给对象或最终产出 Task 的 Quantity，不得对全部工序 Task 求和）
3. 关联 Domain_Dependency（必须按物料关联，不能只按上下游 DomainCode）：
     UpstreamDomainCode = @UpstreamDomainKey
     DownstreamDomainCode = @CurrentDomainKey
     ChildMaterialCode  = 该物料的 MaterialCode
4. AvailableTime = 最终产出 Task.PlannedEndTime + Domain_Dependency.DefaultLeadTimeDays
   （使用 DefaultLeadTimeDays，不写死 2 天；DefaultLeadTimeDays用于AvailableTime）
5. 生成 VirtualInventory（MaterialId, AvailableTime, Quantity）

# 红线：
#  - Domain_Dependency 必须按 ChildMaterialCode 关联（跨域虚拟库存只统计最终产出Task，每物料唯一一道最终产出工序）
#  - APS_BOM_RAW 多 BOM 边用 EXISTS 判断，不得直接 JOIN 全部 APS_BOM_RAW 后再 SUM，
#    防止边重复放大 Task 数量
#  - 通过 PlanVersion.DomainKey 识别域（Task 表不含域标识字段）
#  - 通过 Material.MaterialCode 映射得到 MaterialId（APS_BOM_RAW 无 ChildMaterialId / ParentDomain 字段）
```

**⚠️ 字段红线**：
- `Task` 表无 `DomainId` 字段；通过 `PlanVersion.DomainKey` 识别域
- `APS_BOM_RAW` 无 `ChildMaterialId` / `ParentDomain` 字段；通过 `ChildMaterialCode` / `ParentMaterialCode` + `Material.MaterialCode` 映射；跨域关系通过 `Domain_Dependency` 表达

**数据来源**：APS.Task 表（域B已落盘，指定 PlanVersionId）+ APS_BOM_RAW（当前 BatchNo）+ Material 映射 + Domain_Dependency  
**数据去处**：域A.ScheduleContext.VirtualInventory（内存）

---

#### **步骤4.3：下游域排程时自动"撞墙"顺延**

**负责人**：1号位

**动作**：
- **【1号位】** 下游域排程时，检查虚拟库存的 `AvailableTime`（3月5日14:00）
- **【1号位】** 算法自动"撞墙"，将下游Task的开工时间推迟到3月5日14:00之后
- **【1号位】** 无需任何"通知"或"重排"，延期自动传递

**示例**：
```
上游延期：
- 原计划：电机3月1日10:00完工
- 实际排程：电机3月3日14:00完工（延期2.17天）

下游自动顺延：
- 虚拟库存.AvailableTime = 3月5日14:00（自动包含延期）
- 下游装配Task尝试排在3月1日→撞墙→自动推迟到3月5日14:00
- 无需重排，数学上100%自动收敛
```

---

#### **步骤4.4：5号位打标签（供PMC线下决策）**

**负责人**：5号位

**动作**：
- **【5号位】** 生成 `ExplanationFactDraft`：`ObjectType=DOMAIN, ReasonCode=CROSS_DOMAIN_VERSION_MISMATCH_RISK, EvidenceJson={UpstreamDomain: '产品族B', DelayDays: 2.17, RecommendedRecalculateDomainKeys: ['产品族B']}`
- **【5号位】** 不触发任何自动重排或通知
- **【5号位】** PMC在前端查看"延期原因分析"时，看到"因上游产品族B延期2.17天导致顺延"

**数据来源**：域A.Task（内存）  
**数据去处**：ExplanationFactDraft(ReasonCode=CROSS_DOMAIN_VERSION_MISMATCH_RISK, EvidenceJson=...)（内存，后续落盘为 ScheduleExplanationFact）

**数据流向**：
```
上游延期 → 虚拟库存时间推迟 → 下游算法自动顺延 → 5号位打标签 → PMC线下决策
```

**⚠️ 架构契约**：
- **绝对禁止**"通知下游、触发下游重排"的冗余机制
- 上游延期自动转化为虚拟库存时间推迟，下游算法自动顺延
- 只在输出结果中打标签，供PMC人工决策（如：是否需要增加班次、外协加工）

**架构收益**：
- 完全不需要写"下游重排"逻辑
- 延期自动传递，数学上100%收敛
- PMC有完整的延期归因分析，可线下决策

---

## 第三部分：分域计算全流程（3个场景）

分域计算是指将7个产品族的排程任务并发执行，以提升整体排程速度。以下是3个关键场景：

---

### 🚀 场景1：分域任务分配与并发调度

**触发条件**：凌晨全量排程或局部重排

**流程步骤**：

#### **步骤1.1：任务分配**

**负责人**：3号位

**动作**：
- **【3号位】** 根据 **【2号位】** 提供的依赖图，将7个产品族分配到Hangfire任务队列
- **【3号位】** 为每个域创建独立的排程任务（ScheduleDomainJob）
- **【3号位】** 设置任务优先级（有依赖的域优先级更高）

**数据来源**：ScheduleContext.DomainDependencyGraph（内存）  
**数据去处**：Hangfire任务队列

**数据流向**：
```
DomainDependencyGraph → 3号位.任务分配 → Hangfire.7个并发Job
```

---

#### **步骤1.2：并发执行**

**负责人**：3号位（调度） + 1号位（执行）

**动作**：
- **【3号位】** Hangfire同时启动多个域的排程任务（最多7个并发）
- **【1号位】** 每个域独立执行排程算法
- **【3号位】** 监控每个域的执行状态（RUNNING、COMPLETED、FAILED）

**示例**：
```
时间轴：
00:00 - 域A、域B、域C同时开始排程（无依赖）
05:00 - 域A完成，触发域D开始（域D依赖域A）
08:00 - 域B、域C完成，触发域E、域F开始
12:00 - 所有域完成
```

**数据来源**：Hangfire任务队列  
**数据去处**：各域的排程结果（内存）

**数据流向**：
```
Hangfire调度 → 1号位.并发排程 → 各域ScheduleContext
```

---

### 🧩 补充：V1 共享资源的“配额/预留窗口”粗隔离策略（不抽共享资源域）

当不同产品域之间存在少量共享资源（同一资源池被多域使用），但该资源**利用率低或冲突少**时，V1.0阶段不引入“共享资源域统一排程”的复杂协同，而采用“配额/预留窗口”进行粗隔离，降低并发计算的冲突概率。

**适用条件**（满足其一即可纳入粗隔离，而不是抽成共享资源域）：
- **共享但非瓶颈**：资源池利用率长期低于阈值（如 <70%）
- **共享但低冲突**：跨域抢占冲突次数低于阈值（如每日<5次，或冲突占比<1%）

**识别口径**：以“资源池（WorkCenter/ResourceGroup）”为粒度，而不是物料SKU。
- 统计 `Domains(R)` = 该资源池R在计划周期内服务过的产品域集合
- 当 `|Domains(R)| >= 2` 且不满足“瓶颈共享”判定时，进入粗隔离清单

**配置参数**（由0号位/PMC配置，2号位落地配置存储，5号位规则读取）：
- `QuotaMode`：
  - `Percent`：按百分比分配（如A:60%，B:40%）
  - `TimeWindow`：按时间窗预留（如每天08:00-16:00给A，16:00-24:00给B）
- `QuotaHorizonDays`：配额生效范围（如未来7天）
- `BorrowPolicy`：借用策略（`Forbidden` / `AllowedWithPenalty`）
- `BorrowPenalty`：借用惩罚系数（用于排序时降低借用域任务优先级）

**生效方式**（核心思想：把“共享资源池”在内存日历中切成多个“虚拟子日历”）：
- **【2号位】** 在构建 `ResourceCalendar` 时，对粗隔离资源池生成“虚拟产能日历切片”（Virtual Calendar Slice）
- **【1号位】** 进行时间槽寻址时：
  - 域A只在A的切片内寻址
  - 域B只在B的切片内寻址
- **【5号位】** 若启用借用策略，则允许在对方切片里寻址，但会触发惩罚或需要人工审批

**冲突与降级**（粗隔离并不保证最优，仅保证可控）：
- 若出现“配额不足导致大量延期”（如延期订单数超过阈值），则升级处理：
  - **升级策略A**：将该资源池标记为“瓶颈共享”，在下一轮排程中抽成“共享资源域统一排程”
  - **升级策略B（V1保守）**：保持粗隔离，但向PMC输出“配额调整建议”（增加A配额/减少B配额）

**数据流向**：
```
资源池R → 统计Domains(R)与冲突指标 → 0号位/PMC配置Quota → 2号位.构建虚拟日历切片 → 1号位.按切片寻址
```

---

### ⚠️ 场景2：分域失败重试与降级

**触发条件**：某个域的排程任务失败或超时

**流程步骤**：

#### **步骤2.1：失败检测**

**负责人**：3号位

**动作**：
- **【3号位】** 监控Hangfire任务状态
- **【3号位】** 检测失败原因：
  - 算法异常（如：内存溢出、死循环）
  - 超时（单域排程超过30分钟）
  - 数据异常（如：BOM环路）

**数据来源**：Hangfire任务状态  
**数据去处**：失败日志

**数据流向**：
```
Hangfire任务监控 → 3号位.失败检测 → 失败日志
```

---

#### **步骤2.2：自动重试**

**负责人**：3号位

**动作**：
- **【3号位】** 配置Hangfire自动重试策略（最多3次）
- **【3号位】** 第1次失败：立即重试
- **【3号位】** 第2次失败：等待5分钟后重试
- **【3号位】** 第3次失败：触发降级策略

**数据流向**：
```
失败检测 → 3号位.重试策略 → Hangfire重新调度
```

---

#### **步骤2.3：降级为粗排**

**负责人**：3号位 + 0号位（决策）

**动作**：
- **【3号位】** 3次重试仍失败后，通知 **【0号位】**
- **【0号位】** 决策：是否降级为粗排（Rough Scheduling）
- **【3号位】** 执行降级：
  - 关闭换型优化
  - 简化约束条件
  - 使用更大的时间粒度（小时级而非分钟级）

**数据流向**：
```
3次失败 → 0号位.降级决策 → 3号位.粗排执行
```

**业务意义**：
- 确保即使细排失败，也能提供粗略的排程结果
- 避免因单个域失败导致整体排程瘫痪

---

#### **步骤2.4：域 PlanVersion 失败终态与运行收口**

**负责人**：3号位 + 2号位

**动作**：
- **【3号位】** 该域在有限重试（最多3次）+ 可选降级（粗排）后，若仍无法产生可用结果：
  - 将本次该 Domain 的 `PlanVersion.Status` 由 `BUILDING` 置为 **`FAILED`**，写入 `ErrorMessage`
  - 该 Domain 原有 `ACTIVE` 版本**继续保持 ACTIVE**，不得影响其他独立 Domain 的新版本发布
  - **不得**无限期停留 `BUILDING`；`BUILDING` 不是失败终态
- **【2号位】** 运行收口（步骤5.3）仅在 `ExpectedDomainKeysJson` 对应的**全部** Domain PlanVersion 已进入 `ACTIVE` 或 `FAILED` 后汇总 `ScheduleRun` 终态
- **【2号位】** 若发生运行级致命错误导致部分 Domain 尚未开始：将已创建但未完成的该 Domain PlanVersion 统一标记 `FAILED`（或由收口逻辑确认为失败），**不得**让 `ScheduleRun` 永久 `RUNNING`
- **⚠️ 硬性规则**：`BUILDING` 仅允许存在于有限重试期间；重试耗尽或确认不可恢复必须转 `FAILED`，随后进入运行汇总

---

### 🔄 场景3：单向硬约束传递（废除事后修复，避免排程震荡）

**触发条件**：按拓扑顺序执行域排程

**⚠️ 架构红线说明**：
- **问题**：如果采用"事后校验+修复"（域A重排影响域C，域C重排又影响域B），会导致"排程震荡（Scheduling Oscillation）"，系统可能陷入死循环，永远无法输出最终版本。
- **解决**：采用Asprova等专业引擎的"单向硬约束传递"法则：上游域先排，输出的完工时间作为**绝对硬性时间屏障（Hard Constraint）**传递给下游域，下游域只能在这条时间线之后排产。

**流程步骤**：

#### **步骤3.1：上游域先排，立即落盘**

**负责人**：1号位 + 2号位 + 3号位

**动作**：
- **【3号位】** 根据拓扑排序结果，先启动上游域（如：产品族B-电机）
- **【1号位】** 上游域排程完成，生成Task结果
- **【2号位】** 立即使用 `SqlBulkCopy` 将上游域的Task落盘到数据库

**示例**：
```
拓扑排序结果：
Layer 0: 产品族B（电机，无依赖）
Layer 1: 产品族A（整机，依赖B）

→ 产品族B先排程，立即落盘
```

**数据来源**：域B.ScheduleContext（内存）  
**数据去处**：APS.Task表（域B的结果）

**数据流向**：
```
域B.ScheduleContext → 2号位.SqlBulkCopy → APS.Task表（域B落盘）
```

---

#### **步骤3.2：下游域读取上游结果，构建虚拟库存**

**负责人**：2号位

**动作**：
- **【2号位】** 下游域（产品族A）启动前，从数据库读取上游域（产品族B）刚落盘的Task结果
- **【2号位】** 将上游域生产出来的半成品，作为**带时间戳的虚拟库存（Virtual Inventory）**放入下游域的 `ScheduleContext`
- **【2号位】** 虚拟库存包含关键字段：
  - `MaterialId`：半成品物料ID（如：电机）
  - `AvailableTime`：最早可用时间（上游完工时间 + 物流时间）
  - `Quantity`：可用数量

**示例（正式业务伪代码，V1 落盘前由2号位据实际 DDL 实现；不得直接复制为生产 SQL）**：
```
# 跨域虚拟库存构建（域A下游 读取 域B上游 已落盘 PlanVersion）
# 输入：@UpstreamPlanVersionId, @UpstreamDomainKey, @CurrentDomainKey, @CurrentBatchNo

1. 筛选上游 PlanVersion 中"代表最终产出"的工序 Task
   （每物料唯一一道最终产出工序 Task；多工序情况下仅取最终产出工序，
    不可汇总该物料的全部工序 Task，避免重复放大产出数量）
2. 按 MaterialId 汇总唯一产出数量
   （使用域输出供给对象或最终产出 Task 的 Quantity，不得对全部工序 Task 求和）
3. 关联 Domain_Dependency（必须按物料关联，不能只按上下游 DomainCode）：
     UpstreamDomainCode = @UpstreamDomainKey
     DownstreamDomainCode = @CurrentDomainKey
     ChildMaterialCode  = 该物料的 MaterialCode
4. AvailableTime = 最终产出 Task.PlannedEndTime + Domain_Dependency.DefaultLeadTimeDays
   （使用 DefaultLeadTimeDays，不写死 2 天；DefaultLeadTimeDays用于AvailableTime）
5. 生成 VirtualInventory（MaterialId, AvailableTime, Quantity）

# 红线：
#  - Domain_Dependency 必须按 ChildMaterialCode 关联（跨域虚拟库存只统计最终产出Task，每物料唯一一道最终产出工序）
#  - APS_BOM_RAW 多 BOM 边用 EXISTS 判断，不得直接 JOIN 全部 APS_BOM_RAW 后再 SUM，
#    防止边重复放大 Task 数量
#  - 通过 PlanVersion.DomainKey 识别域（Task 表不含域标识字段）
#  - 通过 Material.MaterialCode 映射得到 MaterialId（APS_BOM_RAW 无 ChildMaterialId / ParentDomain 字段）
```

**数据来源**：APS.Task表（域B已落盘）  
**数据去处**：域A.ScheduleContext.VirtualInventory（内存）

**数据流向**：
```
APS.Task表（域B） → 2号位.SQL查询 → 域A.ScheduleContext.VirtualInventory（虚拟库存）
```

---

#### **步骤3.3：下游域排程时自动"撞墙"推迟**

**负责人**：1号位

**动作**：
- **【1号位】** 下游域（产品族A）执行排程算法
- **【1号位】** 在排装配Task时，检查虚拟库存的 `AvailableTime`
- **【1号位】** 如果尝试的开工时间 < `AvailableTime`，算法自动"撞墙"，强制推迟到 `AvailableTime` 之后

**示例**：
```
域B电机完工时间：2026-03-01 10:00
物流时间：2天
→ 虚拟库存.AvailableTime = 2026-03-03 10:00

域A装配Task尝试排在：2026-03-01 14:00
→ 1号位检查：14:00 < AvailableTime (03-03 10:00)
→ 算法撞墙！自动推迟到 2026-03-03 10:00 之后
→ 最终排在：2026-03-03 10:00
```

**数据来源**：域A.ScheduleContext.VirtualInventory（内存）  
**数据去处**：域A.Task.PlannedStartTime（自动推迟）

**数据流向**：
```
域A.Task尝试开工 → 1号位.检查VirtualInventory.AvailableTime → 撞墙推迟 → Task.PlannedStartTime自动调整
```

---

#### **步骤3.4：下游域落盘，天然100%一致**

**负责人**：2号位

**动作**：
- **【2号位】** 下游域（产品族A）排程完成后，立即落盘
- **【2号位】** 由于上游的时间已经作为硬约束传递，下游结果天然满足时间一致性

**⚠️ 架构契约**：
- **绝对禁止**"事后校验+修复"的循环
- 只要严格按拓扑顺序 + 硬约束传递，结果数学上100%绝对收敛
- **V1保留一次性告警（不修复）**：如果下游发现"上游给的时间太晚，导致客户交期违约"，不触发重排，只在**交期违约清单**里体现（由PMC决策是否手动调整）

**数据流向**：
```
域A.ScheduleContext → 2号位.SqlBulkCopy → APS.Task表（域A落盘，天然一致）
```

**架构收益**：
- 完全不需要写任何"校验和修复"逻辑
- 上游的时间自动变成下游的物理时间墙
- 数据在数学上100%绝对收敛，永远不可能出现排程震荡
- 如果出现交期违约，在ValidationResult里体现，由PMC决策

---

## 第四部分：MES实时事实与异常重排（6个场景）

APS从MES读取的是实时权威视图中的**当前累计事实**，不是逐条事件流水。本部分所有排程计算先按`DataCutoffTime`形成APS本地快照，核心循环不得反复读取实时视图。

---

### 📡 场景1：MES实时视图切片、发布承接与ExecutionLock更新

#### **步骤1.1：按运行截止时间形成快照**

- **【2号位】** 以本次`ScheduleRun.DataCutoffTime`、当前Domain活跃PI集合、未终结工单和回溯窗口为条件，一次性读取：
  - `MES_APS_WorkOrder_View`；
  - `MES_APS_OperationProgress_View`；
  - `MES_APS_StageProgress_View`。
- 查询结果分别写入`MESWorkOrderSnapshot / OperationProgressSnapshot / StageProgressSnapshot`，本次运行后续只读这些快照。
- 时间范围用于缩小工作集，读取结果仍是每张工单/工序/Stage的当前累计事实；禁止把时间范围内记录当增量事件再次累计。

#### **步骤1.2：识别MES已承接的发布记录**

- MES读取`APS_MES_PlanRelease_View`建单后，必须在`MES_APS_WorkOrder_View`回传原`ReleaseItemKey`。
- **【2号位】** 按`ReleaseItemKey`匹配`MESPlanRelease(PUBLISHED)`，在同一事务中：
  1. 写入`MESWorkOrderNo`并将发布记录转为`CONSUMED`；
  2. 创建或幂等更新`ExecutionLock`；
  3. 将相关Task写入同一`ExecutionLockId`并迁移`PLANNED→RELEASED`。
- 同一ReleaseItemKey最多形成一张MES现实工单；TaskNo不承担该幂等职责。

#### **步骤1.3：累计进度、取消与短量完结**

- 工单开工后，相关Task状态由2号位迁移为`IN_PROGRESS`；完成后迁移为`COMPLETED`。
- MES工序状态4只表示**该小工序执行记录**已人工完结，不等于整张MES工单终结，更不等于PI剩余数量被取消。
- 各小工序仍需加工的数量由`OperationProgressSnapshot`按小工序累计事实计算；状态4造成的未完成差额不得写入良品完成量或正式取消量。
- 只有`MESWorkOrderSnapshot.WorkOrderStatus`明确显示整张现实工单已终结时，2号位才将该`ExecutionLock.RemainingExecutionQty`置0；其中未完成且未正式取消的差额退出当前执行锁，返回PI未承诺剩余池，由后续Task重建承接。
- MES取消事实只有在实时视图明确反映后才能增加`CancelledQty`并释放未执行量。

---

### 🔧 场景2：设备故障与修复影响评估

#### **步骤2.1：读取资源现实状态**

- 设备故障/修复事实通过MES提供的实时资源状态契约视图或既有设备事实视图读取，由5号位在ODS层收口；APS不依赖MQ事件或`MES_Actual_Staging`。
- **【2号位】** 按评估时的DataCutoffTime形成资源状态切片；**【5号位】** 返回`ImpactAssessmentResult + RescheduleRecommendation`。
- 故障事实只影响资源可用性、风险解释和重排建议，不自动把Task改为PAUSED/SUSPENDED，不自动创建Candidate。

#### **步骤2.2：PMC决策与单Domain重排**

```text
资源状态事实 → ImpactAssessment → ScheduleExplanationFact/Recommendation
→ 看板告警 → PMC确认 → 3号位创建单Domain ScheduleRun
```

设备恢复后同样重新评估，不自动“恢复Task状态”。

---

### ⏸️ 场景3：任务暂停与恢复（V1不实现）

MES报工五态保持0—4。APS V1不建设PAUSE/RESUME闭环，不增加PAUSED/SUSPENDED状态，也不建设TaskPauseVoucher/TaskResumeVoucher。

---

### 📉 场景4：报废与返工事实

- 报废量、返工量来自Operation/Stage实时汇总视图，并随本次DataCutoffTime写入快照。
- 1号位只消费2号位形成的小工序剩余量和约束；5号位返回报废影响Result；2号位统一更新ExecutionLock及原因事实。
- 不按逐条SCRAP消息重复累计，不通过报废事实直接修改PI总量边界；PI总量仍由`Order.Quantity-Order.ReceivedQty`确定。

---

### 📝 场景5：订单取消与变更

#### **步骤5.1：取消处理分层**

1. 未发布且无执行事实的SOFT关系：只在Scope内Candidate或下一次夜间全量中释放；Scope外不得被当前Candidate静默改动。
2. `MESPlanRelease=PUBLISHED`且MES尚未建单：经审批后转`CANCELLED`，MES不得再创建工单。
3. 已形成HardLock：2号位按累计履约/不可逆消耗事实覆盖式、单调更新`FulfilledQty`；按原因规则或审批只增加获准的`ReleasedQty`。同一事实重读不得重复累计，若仍有`RemainingLockedQty>0`，状态继续ACTIVE。
4. 已形成ExecutionLock：订单取消不等于MES现实工单取消。只有MES实时视图确认取消后，2号位才更新`CancelledQty`并释放未执行量。

#### **步骤5.2：数量或交期变化**

- 变化先进入`Order_Canonical`；白天只在用户确认的单Domain Scope内生成Candidate。
- Scope外Soft保持Base保护，执行事实按Candidate DataCutoffTime读取最新MES视图；禁止为订单变化自动创建或激活Candidate。

---

### 🔄 场景6：局部重排发起（Candidate主链入口）

#### **步骤6.1：影响评估与推荐**

5号位只返回影响范围、原因与建议；2号位不直接扩大Scope，3号位不自动创建运行。

#### **步骤6.2：PMC确认与ScopeJson构造**

ScopeJson保持固定11字段、严格单Domain。取消订单位于Scope外时，不允许当前Candidate提前释放其SOFT份额。

#### **步骤6.3：创建运行并形成现实执行快照**

```text
3号位创建ScheduleRun+BUILDING Candidate
→ 2号位复制Base订单/供给保护边界
→ 按Candidate DataCutoffTime读取最新MES实时累计视图并形成独立快照
→ 分类恢复ExecutionLock/MESPlanRelease/HardLock/Scope内外SOFT
→ 重新Pegging与有限产能排程
```

---

## 第五部分：人工干预与调整流程（3个场景）

PMC（生产计划员）需要对系统生成的计划进行人工调整和审批。以下是3个关键场景：

---

### 🖱️ 场景1：PMC手动拖拽调整

**触发条件**：PMC在甘特图上拖拽任务，调整开工时间

**流程步骤**：

#### **步骤1.1：前端拖拽事件**

**负责人**：4号位

**动作**：
- **【4号位】** 监听甘特图的拖拽事件
- **【4号位】** 执行合规性检查：
  - 禁止拖拽Frozen区（冻结区）的任务
  - 禁止拖拽已开工的任务（Task.Status = IN_PROGRESS）
  - 禁止拖拽到设备维修时间段
- **【4号位】** 如果违规，弹出警告并阻止拖拽
- **【4号位】** 如果合规，显示影响范围提示

**数据来源**：甘特图UI事件  
**数据去处**：前端状态

**数据流向**：
```
用户拖拽 → 4号位.合规性检查 → 通过/拒绝
```

---

#### **步骤1.2：影响分析**

**负责人**：5号位

**动作**：
- **【4号位】** 将拖拽请求发送到后端
- **【5号位】** 分析影响范围：
  - 该任务的后续任务（依赖链）
  - 同一设备上的其他任务（时间冲突）
  - 关联订单的交期影响
- **【5号位】** 返回影响订单列表

**数据来源**：Task.Id + 新的StartTime  
**数据去处**：影响分析结果

**数据流向**：
```
拖拽请求 → 5号位.影响分析 → 影响订单列表
```

---

#### **步骤1.3：确认与局部重排**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** 显示确认对话框：
  - "此调整将影响X个订单，是否继续？"
  - 显示受影响订单列表
- **【用户】** 点击"确认"
- **【4号位】** 发送调整请求到后端
- **【3号位】** 归一化构造 `ScopeJson`（`ScopeJson.Purpose=MANUAL_ADJUSTMENT`，填写受影响 OrderCanonicalIds/LockedTaskIds 等11字段）
- **【3号位】** 创建 `ScheduleRun`（`RunType=LOCAL_RESCHEDULE`，Purpose 写入 `ScopeJson.Purpose`）及 Candidate 类 PlanVersion 版本壳（初始 Status=BUILDING，`DomainKey=Base.DomainKey`），进入第八部分场景4

**⚠️ 红线40**：不得由 3号位直接执行局部重排；所有重排必须经完整 Candidate 主链（数据构造 → 排程 → 落盘 → CANDIDATE_ACTIVATION 审批 → 激活）。

**数据流向**：
```
用户确认 → 4号位.调整请求 → 3号位.ScopeJson归一化 → 创建ScheduleRun(LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT) → Candidate主链
```

---

### 🔒 场景2：任务手动锁定与优先级调整

**触发条件**：PMC需要锁定某些任务，或调整优先级

**流程步骤**：

#### **步骤2.1：任务锁定**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** PMC在甘特图上选中任务，点击"锁定"按钮
- **【4号位】** 发送锁定请求到后端
- **【3号位】** 将锁定意图形成 ManualAdjustmentIntent，提示 PMC 确认是否发起新一轮调整排程
- **【PMC/0号位】** 确认后，3号位创建新的 `ScheduleRun`，在新 ScheduleRun 的 `ScopeJson.LockedTaskIds` 中写入目标 TaskId，创建 Candidate 类 PlanVersion 版本壳（初始 `Status=BUILDING`），进入第八部分场景4

**⚠️ 红线**：
- `ScheduleRun.ScopeJson` 创建后不可修改；不得向既有 ScheduleRun 或 PlanVersion 追加 LockedTaskIds
- 锁定约束只能通过创建新 ScheduleRun 的 ScopeJson 传入排程算法

**数据流向**：
```
用户锁定操作 → 4号位.锁定请求 → 3号位.ManualAdjustmentIntent → PMC确认 → 新ScheduleRun(ScopeJson.LockedTaskIds) → Candidate主链
```

---

#### **步骤2.2：优先级调整**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** PMC拖动优先级滑块，调整订单优先级
- **【4号位】** 发送优先级调整请求
- **【3号位】** 更新 `Order_Canonical.Priority` 字段（订单级优先级，非Task级）
- **【3号位】** 如 PMC 决策触发重排，按白天 Candidate 主链执行；新版本排程时算法自动按优先级重新寻址

**⚠️ 职责红线**：不得直接修改 `Task.Priority`；优先级调整通过订单优先级传导，在下一次排程中生效。

**数据流向**：
```
优先级调整 → 4号位.调整请求 → 3号位.Order_Canonical.Priority更新 → [PMC决策触发Candidate主链]
```

---

### ✅ 场景3：计划审批与冻结

**触发条件**：排程计划生成后，需要PMC审批才能写入MES拉取发布视图

**流程步骤**：

#### **步骤3.1：计划审批**

**负责人**：4号位 + 0号位

**动作**：
- **【4号位】** 显示计划审批界面：
  - 排程结果摘要（准时率、产能利用率）
  - 延期订单列表
  - 关键瓶颈设备
- **【0号位】** 或授权的PMC审批计划
- **【4号位】** 填写审批意见
- **【4号位】** 点击"批准"或"驳回"

**数据来源**：PlanVersion + ValidationResult  
**数据去处**：独立审批记录（`RequestType=CANDIDATE_ACTIVATION`，`Status=APPROVED/REJECTED`）；PlanVersion.Status 不变

**数据流向**：
```
审批界面 → 0号位.审批决策 → 3号位.审批记录(CANDIDATE_ACTIVATION, APPROVED/REJECTED)
```

---

#### **步骤3.2：人工强制冻结/解冻（特殊干预）**

**负责人**：5号位 + PMC

**⚠️ 重要说明**：
- **系统的冻结主要由每日 02:00 的滑动窗口自动完成**（见阶段5步骤5.0）
- **PMC 的界面只用于对特殊单据进行"人工强制干预"**

**动作**：
- **【PMC】** 在特殊情况下（如紧急插单、设备临时维护），需要强制冻结或解冻某些 Task
- **【4号位】** 提供人工冻结/解冻界面：
  - 显示当前冻结窗口边界（如：未来3天）
  - 允许 PMC 手动勾选需要强制冻结的 Task（即使在3天外）
  - 允许 PMC 手动解冻冻结窗口内的 Task（需要审批权限）
- **【5号位】** 调用 `IManualFreezeRule` 正式人工冻结规则模块，计算需要强制冻结/解冻的 TaskId 列表，生成 `ManualFreezeVoucher`（含操作人、操作时间、TaskId列表、冻结/解冻方向），**不直接修改任何状态**

**⚠️ V1 保守方案（当前实装）——人工冻结仅作为排程约束**：
- **【PMC/0号位】** 确认后，3号位创建新的 `ScheduleRun`（`RunType=LOCAL_RESCHEDULE`，`ScopeJson.Purpose=MANUAL_ADJUSTMENT`），在**新 ScheduleRun** 的 `ScopeJson.LockedTaskIds` 中写入锁定集合，创建 Candidate 类 PlanVersion 版本壳（初始 `Status=BUILDING`），进入第八部分场景4
- **V1 不支持"突破冻结窗口立即下发"**：由于当前 DDL **不存在** `Task.IsFrozen` / `PlanDispatchEligibility` / `ManualFreezeVoucher` 物理表 / `DispatchOverride`，无法持久化"5天后 Task 立即下发"的资格标记；内存 Voucher 请求结束即失效，不能被下一次下发服务读取
- ManualFreezeVoucher 在 V1 仅作为**规则运行对象**：承载 5号位判定结果 + 触发 PMC 走 Candidate 主链；不承担"下发资格突破"职能

**⚠️ 完整方案（待 DDL 补齐）**：如需支持突破冻结窗口的即时下发，须新增正式持久化对象 `PlanDispatchOverride`（至少含 `PlanVersionId` / `TaskId` / `OverrideType` / `EffectiveFrom` / `EffectiveTo` / `ApprovalRecordId` / `Status` / `CreatedBy` / `CreatedAt`），并同步补齐 DDL / 字段说明 / MES 下发接口消费规则；V1 保守方案不作此承诺。

**⚠️ 红线**：
- `ScheduleRun.ScopeJson` 创建后不可修改；**禁止**修改当前 ACTIVE 版本或既有 ScheduleRun 的 `ScopeJson.LockedTaskIds`
- 锁定约束只能通过创建新 ScheduleRun 的 ScopeJson 传入排程算法
- ManualFreezeVoucher 是规则运行对象，V1 无同名物理表；不因命名相似而自动获得持久化承载

**示例场景（V1 保守方案）**：
```
场景1：紧急插单强制锁定
→ VIP客户紧急插单，PMC 手动将该订单的相关 Task 加入新 ScheduleRun 的 ScopeJson.LockedTaskIds
→ 走 Candidate 主链重排，将该订单前置到冻结窗口内的时段
→ ⚠️ V1 不支持"5天后 Task 立即下发 MES"（无持久化承载对象）；只能通过重排使其落入冻结窗口后再走正常下发

场景2：设备维护临时解冻
→ 某设备临时维护，PMC 从当前 ScheduleRun 派生新 ScheduleRun，在新 ScopeJson.LockedTaskIds 中排除该设备上原冻结 Task
→ 走 Candidate 主链重排，将 Task 转移到其他设备
```

**数据流向**：
```
PMC人工干预 → 5号位.ManualFreezeRule → ManualFreezeVoucher（内存规则运行对象）
  → PMC确认 → 3号位.新ScheduleRun(ScopeJson.LockedTaskIds) → Candidate主链
（V1不支持突破冻结窗口即时下发；待PlanDispatchOverride正式DDL补齐后再扩展）
```

---

#### **步骤3.3：解冻申请**

**负责人**：4号位 + 0号位

**动作**：
- **【4号位】** 如果 PMC 需要允许某 Task 参与后续重排（当前落在冻结窗口内），提交解冻申请
- **【4号位】** 填写解冻理由（如：客户紧急变更）
- **【0号位】** 审批解冻申请
- **【5号位】** 如果批准，生成 `ManualFreezeVoucher`（解冻方向，内存规则运行对象）
- **【3号位】** 由 PMC 确认后创建新 `ScheduleRun`，在新 `ScopeJson.LockedTaskIds` 中重新构造锁定集合（该 Task 不再包含在内），并创建Candidate类PlanVersion版本壳（初始Status=BUILDING），进入第八部分场景4。

**⚠️ 红线**：
- 不得从既有 ScheduleRun 或 PlanVersion 的 `ScopeJson.LockedTaskIds` 中移除 TaskId；ScopeJson 创建后只读
- V1 无 `PlanDispatchOverride` 等持久化承载，"下发资格突破解冻"不作承诺；解冻只作用于后续排程约束

**数据流向**：
```
解冻申请 → 4号位.申请界面 → 0号位.审批 → 5号位.ManualFreezeVoucher（内存） → PMC确认 → 3号位.新ScheduleRun(新ScopeJson.LockedTaskIds不含该Task) → Candidate主链
```

**业务意义**：
- 给予PMC灵活调整计划的能力
- 通过审批和冻结机制，保证计划的稳定性
- 避免频繁变更导致车间执行混乱

---

## 第六部分：数据同步与一致性流程（3个场景）

确保APS与ERP、MES等外部系统的数据一致性。以下是3个关键场景：

---

### 🔄 场景1：ERP增量订单同步（2026-04-03 订单链路审计修正）

**触发条件**：ERP系统有新订单或订单变更

**完整链路**：`ODS.ext_v_APS_SalesOrder` → `ERP_Order_Staging`（PENDING）→ `sp_ValidateAndPromoteOrders` → `Order_Canonical` → 夜间 `sp_SyncOrdersToPartitionTable` → `Order`

**流程步骤**：

#### **步骤1.1：增量拉取与写入Staging**

**负责人**：2号位

**动作**：
- **【2号位】** 每小时通过Hangfire定时任务执行`ERPOrderSyncService.IncrementalSyncAsync()`
- **【2号位】** 从 ODS 契约视图 `ext_v_APS_SalesOrder` 按 `SourceUpdatedAt` 水位拉取增量数据（含SO/MTO/MTS/SS，UNION ALL两类中间表）
- **【2号位】** 通过 `SourceUpdatedAt` 水位识别 ERP 侧增量，登记 `APS_ETL_Log` 追溯轮询批次
- **【2号位】** 将拉取的订单写入`ERP_Order_Staging`表（`SyncStatus = 'PENDING'`）

**数据来源**：ODS.`ext_v_APS_SalesOrder`（契约视图，含BOMNO/SourceSystem/SourceMasterID/SourceUpdatedAt）  
**数据去处**：APS.`ERP_Order_Staging`表

**数据流向**：
```
ODS.ext_v_APS_SalesOrder → 2号位.ERPOrderSyncService（时间戳增量轮询） → APS.ERP_Order_Staging(PENDING)
```

---

#### **步骤1.2：数据质量校验与提升到Canonical**

**负责人**：2号位

**动作**：
- **【2号位】** 调用`sp_ValidateAndPromoteOrders`（v5.0.27全量重写）执行校验与提升：
  - **PHASE 0**：`#TargetStagingIds` 锁定本批次所有PENDING行ID（UPDLOCK+ROWLOCK防并发）
  - **STEP 0：MaterialCode 三级解析链**：0a SourceMasterID→MaterialMapping（最高优先级）→ 0b Model→MaterialMapping.SourceModel（一对多→立即FAILED+MATERIAL_MAPPING_AMBIGUOUS）→ 0c EmergencyOverride兜底
  - **STEP 1a：硬失败校验**：必填字段+OrderType未知（⚠️ 禁止将ERP原始值写入Canonical，必须FAILED+ORDER_TYPE_UNKNOWN）；**FactoryCode缺失或无法唯一映射到Factory.Code→FAILED+FACTORY_CODE_MISSING**（缺FactoryCode的订单不得进入BOM Request；不得静默填写默认工厂；不得从ProcessCode/StageCode/BOM路径反推工厂；白天Candidate Order覆盖时同样执行此校验）
  - **STEP 1b**：MaterialCode不在Material主数据中→MATERIAL_NOT_FOUND
  - **STEP 1c：非阻断诊断**：BOMNO=NULL→FailureCode=`BOMNO_MISSING`+NextActionCode=`WAIT_BOM_WORKSET`（SyncStatus不置FAILED，订单仍可PROCESSED）
  - **STEP 2a**：OrderType标准化（SO/MTO/1→SALES_ORDER；MTS/SS/SS_U/2→PRODUCTION_INSTRUCTION）
  - **STEP 2b**：CustomerSegment通过CustomerCodeMap派生；**⚠️ 无匹配→`UNKNOWN`（不再默认OVERSEAS）**；CustomerCode为NULL→NULL
  - **STEP 3：MERGE→Order_Canonical**：Upsert键=`SourceSystem+SourceOrderId`；NonStockShipmentType/OriginalOrderSource inline标准化；SourceModel透传
- **【2号位】** 提升成功 → `SyncStatus`=`PROCESSED`（含BOMNO_MISSING诊断的行也为PROCESSED）
- **【2号位】** 校验失败 → `SyncStatus`=`FAILED`，`FailureCode`单值最高优先级，`ErrorMessage`记录人类可读详情
- **【2号位】** ETL日志写入`APS_ETL_Log`表（含ValidatedCount/PromotedCount/FailedCount/BOMNOMissingCount四计数）

**数据流向**：
```
APS.ERP_Order_Staging(PENDING) → sp_ValidateAndPromoteOrders
  → 提升成功: Order_Canonical(Upsert) + Staging(PROCESSED) [可含 BOMNO_MISSING 诊断]
  → 硬失败: Staging(FAILED) + FailureCode单值 + NextActionCode + ErrorMessage留痕
```
> **⚠️ v5.0.27 设计红线**：
> - `FailureCode` 单值，只记最高优先级；`BOMNO_MISSING`=非阻断诊断（订单仍 PROCESSED）
> - 阻断以 `SyncStatus='FAILED'` 为准；`BOMNO_MISSING + WAIT_BOM_WORKSET` = 明确非阻断组合
> - `CustomerSegment='UNKNOWN'` ≠ `'OVERSEAS'`，消费方须识别 UNKNOWN 走保守路径
> - OrderType 未知尤禁写入 Canonical

---

### 📊 场景2：主数据变更与版本管理

**触发条件**：BOM、工艺路线、物料主数据发生变更

**流程步骤**：

#### **步骤2.1：变更检测**

**负责人**：2号位

**动作**：
- **【2号位】** 通过 ODS 契约视图时间戳水位识别主数据变化
- **【2号位】** 检测到变更后，生成新版本号
- **【2号位】** 记录变更事实到 `APS_ETL_Log`（含批次、来源视图、水位、变更计数）；若需要主数据版本审计，使用主数据版本表既有字段

**⚠️ 表名红线**：不使用裸名 `ChangeLog`（DDL 未定义）；变更事实统一走 `APS_ETL_Log` 或主数据版本审计对象；如未来确需独立表，须先补 DDL / 字段说明 / 唯一约束

**数据来源**：ERP 主数据表（ODS 契约视图按时间戳水位轮询）  
**数据去处**：APS 主数据表 + `APS_ETL_Log`

---

#### **步骤2.2：重排触发判断**

**负责人**：5号位

**动作**：
- **【5号位】** 调用 `IMasterDataChangeRule` 影响评估策略接口，执行 `ImpactAssessment`
- **【5号位】** 判断变更对排程的影响：
  - BOM结构变更：显著影响
  - 工艺路线变更：显著影响
  - 物料名称变更：无排程影响
- **【5号位】** 对有显著影响的变更，产出 `RescheduleRecommendation`，推送看板告警
- **【4号位】** 看板展示 Recommendation 摘要
- **【PMC/0号位】** 人工确认是否需要发起 Candidate 重排；确认后由 3号位创建 `ScheduleRun`，走第八部分场景4

**⚠️ 红线40**：主数据变化后 5号位不得直接通知 3号位触发重排；必须经 PMC 人工确认后创建 ScheduleRun。

**数据流向**：
```
主数据变更 → 5号位.ImpactAssessment → RescheduleRecommendation → 4号位.看板告警 → PMC确认 → 3号位.创建ScheduleRun
```

---

### 🔁 场景3：MES实时快照幂等与截止时间一致性

#### **步骤3.1：同一运行快照全量替换**

- 三个同步过程均以`ScheduleRunId`为隔离键：先删除本运行旧快照，再按同一`DataCutoffTime`批量插入当前累计事实。
- 同一运行重复执行不会累计完成量；唯一约束按`ScheduleRunId + 工单/工序/Stage业务键`拒绝重复行。
- APS不要求MES提供EventId/MessageId，不建设事件入站暂存表。

#### **步骤3.2：来源更新时间与状态一致性**

- 有`SourceUpdatedAt`时只读取`<=DataCutoffTime`的事实；来源无法提供统一更新时间时，记录查询开始/结束时间、来源视图和读取条件，作为快照解释元数据。
- 同一运行的工单、工序、Stage快照必须采用同一截止时间；若跨视图数量矛盾，StageProgress是PI大工艺位置权威，OperationProgress只做Stage内部裁剪并登记Issue。

---

## 第七部分：监控反馈与容错流程（7个场景）

系统运行过程中需要实时监控和容错处理。以下是7个关键场景：

---

### 📊 场景1：排程KPI统计与分析

**触发条件**：排程完成后，自动计算KPI指标

**负责人**：3号位

**动作**：
- **【3号位】** 自动计算关键KPI：
  - 准时率：按时完成的订单数 / 总订单数
  - 产能利用率：实际加工时间 / 可用时间
  - 换型次数：总换型次数（越少越好）
  - 平均延期天数：延期订单的平均延期天数
- **【3号位】** 生成KPI报表
- **【3号位】** 与历史KPI对比，识别趋势

**数据来源**：Task + Order + ResourceCalendar  
**数据去处**：KPI报表

**数据流向**：
```
排程结果 → 3号位.KPI计算 → KPI报表 → 4号位.前端展示
```

---

### 🎯 场景2：交期达成率跟踪

**触发条件**：实时跟踪订单交期达成情况

**负责人**：3号位 + 5号位

**动作**：
- **【3号位】** 每日统计：
  - 应交订单数
  - 实际交付订单数
  - 延期订单数
- **【5号位】** 分析延期原因：
  - 设备故障导致
  - 物料短缺导致
  - 订单插单导致
- **【3号位】** 生成交期达成率报告

**数据流向**：
```
Order快照.CustomerDueDate vs MESWorkOrderSnapshot/StageProgressSnapshot中的累计完成与最终完成时间 → 3号位.统计 → 交期达成率报告
```

---

### 🔍 场景3：执行监控与偏差预警

**触发条件**：实时监控计划执行情况

**负责人**：3号位 + 4号位

**动作**：
- **【3号位】** 实时对比：
  - 计划开工时间 vs 实际开工时间
  - 计划完工时间 vs 实际完工时间
- **【3号位】** 计算偏差：
  - 如果偏差 > 阈值（如：延迟2小时），触发预警
- **【4号位】** 前端弹窗预警：
  - "任务T001预计延期4小时，影响订单SO123"
- **【3号位】** 发送通知给PMC

**数据流向**：
```
Task.PlannedStartTime/PlannedEndTime vs 本次DataCutoffTime下MES工单/工序进度快照的实际开始与完成事实 → 3号位.偏差计算 → 预警通知
```

---

### ⏱️ 场景4：排程超时中断与恢复

**触发条件**：单域排程超过30分钟

**负责人**：3号位

**动作**：
- **【3号位】** Hangfire监控任务执行时间
- **【3号位】** 如果超过30分钟，中断任务
- **【3号位】** 保存部分结果（已排程的Task）
- **【3号位】** 记录中断原因和进度
- **【3号位】** 通知 **【0号位】** 决策：
  - 重试（调整参数）
  - 降级为粗排
  - 人工介入

**数据流向**：
```
Hangfire任务监控 → 超时检测 → 中断 → 部分结果保存 → 0号位决策
```

---

### 💾 场景5：内存溢出监控与告警

**触发条件**：排程过程中内存使用超过阈值

**负责人**：2号位

**动作**：
- **【2号位】** 在排程过程中监控内存使用：
  - 每5分钟检查一次GC堆内存
  - 阈值：2GB（可配置）
- **【2号位】** 如果超过阈值：
  - 触发GC.Collect()强制回收
  - 记录内存快照
  - 发送告警通知
- **【2号位】** 如果仍然超过，中断排程

**数据流向**：
```
GC.GetTotalMemory → 2号位.内存监控 → 超阈值告警 → 可能中断
```

---

### 🔌 场景6：数据库连接池管理

**触发条件**：数据库连接池耗尽

**负责人**：2号位

**动作**：
- **【2号位】** 配置连接池参数：
  - 最小连接数：10
  - 最大连接数：100
  - 连接超时：30秒
- **【2号位】** 监控连接池状态：
  - 当前活跃连接数
  - 等待连接的请求数
- **【2号位】** 如果连接池耗尽：
  - 记录错误日志
  - 拒绝新请求（返回503）
  - 发送告警通知
- **【2号位】** 定期检测死锁并自动恢复

**数据流向**：
```
连接池监控 → 2号位.状态检查 → 耗尽告警 + 拒绝请求
```

---

### ⚠️ 场景7：异常任务高亮显示

**触发条件**：任务出现异常状态

**负责人**：4号位

**动作**：
- **【4号位】** 在甘特图上高亮显示异常任务（采用**组合读模型**，不只读 `Task.Status`）：

  | 展示内容 | 正式来源 |
  | --- | --- |
  | 待执行 / 已下发未开工 / 执行中 / 已完成 / 已取消 | Task 正式执行状态（PLANNED / RELEASED / IN_PROGRESS / COMPLETED / CANCELLED） |
  | 暂停 | V1 不支持持久化暂停态；仅 PMC 人工干预上下文中的临时非 Task.Status 标注（V2 预留，不写 Task.Status=PAUSED/SUSPENDED） |
  | 订单取消 | Order 快照 或 Order_Canonical 状态 |
  | 交期延期 | `Order.DelayStatus` / `OrderScheduleSummary` / `ScheduleExplanationFact` |
  | 物料短缺 | `ScheduleExplanationFact`，ReasonCode=MATERIAL_SHORTAGE |
  | 物流延迟 | `ScheduleExplanationFact`，ReasonCode=LOGISTICS_DELAY |
  | 上游延期 | `ScheduleExplanationFact`，ReasonCode=CROSS_DOMAIN_VERSION_MISMATCH_RISK |

- **【4号位】** 提供筛选功能：
  - 只显示异常任务
  - 按异常类型筛选
- **【4号位】** 点击异常任务，显示详细信息（状态 + 对应 `ScheduleExplanationFact.ReasonCode` 证据）

**数据流向**：
```
Task正式执行状态 + Order.DelayStatus/Order_Canonical状态 + ScheduleExplanationFact.ReasonCode → 4号位.组合着色 → 甘特图高亮
```

**业务意义**：
- 实时掌握系统运行状态
- 及时发现并处理异常
- 通过KPI分析持续改进排程质量

---

## 第八部分：计划发布与版本管理（5个场景）

排程计划需要经过审批、下发、版本管理等流程。以下是5个关键场景：

---

### 📤 场景1：MES拉取计划发布、ExecutionLock创建与跨版本幂等

#### **步骤1.1：激活后固化发布单元**

- 只从当前Domain的ACTIVE PlanVersion选择通过资格且进入发布窗口的正式Task。
- 排除虚拟Task、已有ExecutionLock的Task，以及已关联有效MESPlanRelease的Task。
- **【2号位】** 将同一PI、同一Stage、同一执行批次的小工序Task组成一条`MESPlanRelease`：
  - `Quantity`取该Stage级执行批次的单一流转数量，不对关联小工序Task数量求和；数量不一致且无显式换算闭合时拒绝发布；
  - 生成不可变`ReleaseItemKey=RLS:{GUID}`；
  - 写`PublishStatus=PUBLISHED`；
  - 将相关Task写入同一`MESPlanReleaseId`，但Task状态仍为`PLANNED`。
- 一条发布记录对应一张未来MES现实工单；不得跨PI或跨Stage合并。

#### **步骤1.2：MES读取发布视图并幂等建单**

- MES轮询`APS_MES_PlanRelease_View`，按`ReleaseItemKey`保证最多创建一张工单。
- MES必须原样保存ReleaseItemKey，并在`MES_APS_WorkOrder_View`中与`MESWorkOrderNo`一起回传。
- `TaskNo`可以展示给MES，但不承担跨系统幂等与跨版本关联。

#### **步骤1.3：APS确认建单并创建ExecutionLock**

- APS按运行截止时间同步MES工单快照，识别ReleaseItemKey已出现后，在同一事务中：
  1. `MESPlanRelease: PUBLISHED→CONSUMED`；
  2. 创建ExecutionLock并关联MESPlanReleaseId；
  3. 相关Task写入ExecutionLockId并转为`RELEASED`。
- 普通需求归属仍为SOFT；只有特殊出荷指示、客户/质量/环保专属或人工批准场景才创建HardLock。

#### **步骤1.4：建单前取消与竞态保护**

- MES尚未建单时，审批通过后可将发布记录转`CANCELLED`；MES不得创建该工单。
- MES在真正落工单前必须再次读取ReleaseItemKey当前状态，避免“已读PUBLISHED、随后被取消”的竞态。
- MES已经建单后不能靠隐藏发布记录取消，必须由MES现实流程取消并通过实时工单视图反馈。

```text
ACTIVE Task
→ 发布资格与PI+Stage分组
→ MESPlanRelease(PUBLISHED)+ReleaseItemKey
→ MES拉取视图幂等建单
→ MES工单视图回传ReleaseItemKey
→ MESPlanRelease(CONSUMED)+ExecutionLock+Task.RELEASED
```

---

### 🗂️ 场景2：Candidate版本激活与历史版本只读查询

**触发条件**：需要激活新版本或查看历史版本

**流程步骤**：

#### **步骤2.1：版本激活（正式口径）**

**负责人**：3号位

**动作**：
- **【3号位】** 调用 `ActivateCandidatePlanVersionAsync(planVersionId)`，使用 Serializable 事务对 Candidate 行加锁，在同一事务中：
  1. 幂等检查：若目标版本已为 ACTIVE，直接返回成功
  2. 校验 `PlanVersion.Status=CANDIDATE`
  3. 校验存在 `RequestType=CANDIDATE_ACTIVATION` 且 `Status=APPROVED` 的审批记录
  4. 校验 `SourceScheduleRunId` 存在，读取对应 ScheduleRun
  5. 校验 RunType+Purpose 组合：只允许 `LOCAL_RESCHEDULE+INSERT_RESCHEDULE`、`LOCAL_RESCHEDULE+MANUAL_ADJUSTMENT`、`MANUAL_RESCHEDULE+MANUAL_ADJUSTMENT`；永远拒绝 `INSERT_ORDER_WHATIF` 的任何 Purpose
  6. 校验 `BasePlanVersionId` 非空，校验新旧版本 `DomainKey` 一致
  7. 校验无并发激活冲突
  8. 同一事务：旧 ACTIVE→ARCHIVED，本版本→ACTIVE，写入 `ActivatedAt`+`ActivatedBy`；失败全部回滚
- **【3号位】** 版本切换是原子事务操作
- **【3号位】** 通过 SignalR 广播前端刷新

**⚠️ 红线**：不使用 `ActiveVersionId` 字段；不使用 `System_Active_Version` 表；版本激活通过 `PlanVersion.Status` 字段标识，同域同时只有一个 `ACTIVE`。

**数据流向**：
```
CANDIDATE版本 + 审批记录(APPROVED) → 3号位.ActivateCandidatePlanVersionAsync → [同一事务] 旧ACTIVE→ARCHIVED + 新版本→ACTIVE → SignalR广播
```

---

#### **步骤2.2：历史版本查阅（非回滚）**

**负责人**：3号位 + 0号位

**动作**：
- **【0号位】** 需要查阅历史版本数据（如：对比分析、审计追溯）
- **【3号位】** 提供历史版本只读查询 API，返回指定 `PlanVersionId` 的 Task/Order 快照
- **【4号位】** 前端展示历史版本甘特图（只读模式，不影响当前 ACTIVE 版本）

**⚠️ V1 红线**：历史 ARCHIVED 版本**只支持**只读查询、差异对比、审计追溯，**不可**直接激活，**也不可**作为 Candidate 主链的 BasePlanVersionId（步骤4.3 硬约束 `BasePlanVersion.Status = ACTIVE`；BuildRemainingSupplyContextAsync 公式基于 Base ACTIVE 原始供给与已确认分配/消耗，Base 若为 ARCHIVED 无法直接成立）。

**V1 历史方案参考的正确做法**：
- 以当前 ACTIVE 版本为 Base 生成新 Candidate；将历史 ARCHIVED 版本作为**参考输入**（Scenario 或 ScopeJson 的手工调整依据），由 5号位/PMC 从历史查询结果中提炼调整意图，写入新 ScopeJson
- 不复用历史 ARCHIVED 版本的 Task/Pegging；本次 Candidate 依据当前主数据、当前 MES 实绩剩余量重新生成

**示例**：
```
当前ACTIVE版本：V20260301_020000
需要参考历史方案：V20260228_020000（ARCHIVED）
→ 只读查阅 V20260228_020000 的排程数据
→ 如需按其思路重新排程：以当前 ACTIVE V20260301_020000 为 Base，
  由 PMC 从 V20260228_020000 提炼调整意图 → 写入新 ScopeJson → 走 Candidate 主链
→ 禁止将 V20260228_020000 直接作为 BasePlanVersionId
```

**数据流向**：
```
0号位.查阅请求 → 3号位.历史版本只读API → 4号位.只读甘特图展示（含差异对比、审计追溯）
```

**⚠️ V1 历史版本红线**：
- V1 历史 ARCHIVED 版本**只支持**只读查询、差异对比、审计追溯
- **不支持**"以 ARCHIVED 版本为 Base 生成新 Candidate"（`BasePlanVersion.Status = ACTIVE` 是 4.3 的硬约束；RemainingSupplyContext 公式基于 Base ACTIVE 原始供给和已确认分配/消耗，若 Base 是 ARCHIVED，时间语义与当前库存事实无法直接成立）
- 后续扩展"历史方案恢复"必须先定义独立契约：ARCHIVED Base 允许规则、历史 Task 参考语义、当前库存/MES 实绩覆盖规则、RemainingSupply 替代公式、新 DomainKey 与计划窗口来源；在契约就绪前不作为可执行流程

---

### 📋 场景3：版本历史查询与对比

**触发条件**：PMC需要查看历史版本或对比差异

**流程步骤**：

#### **步骤3.1：版本列表查询**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** 显示版本历史列表：
  - 版本号
  - 生成时间
  - 审批状态
  - KPI指标（准时率、产能利用率）
- **【3号位】** 提供版本查询API

**数据流向**：
```
PlanVersion表 → 3号位.查询API → 4号位.版本列表展示
```

---

#### **步骤3.2：版本对比**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** PMC选择两个版本进行对比
- **【3号位】** 计算差异：
  - 哪些Task的时间变了
  - 哪些订单的交期变了
  - KPI指标对比
- **【4号位】** 高亮显示差异

**数据流向**：
```
版本A + 版本B → 3号位.差异计算 → 4号位.差异高亮显示
```

**业务意义**：
- 确保计划正确下发到车间执行
- 支持历史版本只读查阅、差异对比、审计追溯（V1 不支持以历史版本为 Base 生成新 Candidate）
- 通过版本对比，分析计划变化原因

---

### 🔄 场景4：白天实时评估与 Candidate 闭环（v3.15 新增）

**触发条件（三条入口）**：
1. **插单 CTP 触发**：3号位接收插单请求，5号位评估产能可行性后，PMC 决策触发 `INSERT_IMPACT_ANALYSIS` 或 `INSERT_RESCHEDULE`
2. **PMC 手动触发**：PMC 在看板点击"触发局部重排"，选择重排范围后提交
3. **阈值自动推送**：5号位检测到累计延迟/故障影响超过阈值，推送看板告警，PMC 确认后触发

**⚠️ 架构说明**：
- 白天 Candidate 链路与凌晨 `FULL_SCHEDULE` 主链完全分离
- 3号位负责创建 ScheduleRun + PlanVersion 并管理生命周期；2号位只负责数据构造与结果持久化
- Candidate 版本不自动覆盖当前 ACTIVE 版本，须 3号位显式调用激活 API
- BUILDING 超时（默认30分钟）自动转为 FAILED；同域同时只允许一个 BUILDING 状态版本

**五个 RunType + Purpose 精确组合**：

| RunType | Purpose | 说明 | 是否可激活 |
|---|---|---|---|
| `INSERT_ORDER_WHATIF` | `CTP` | 插单产能可行性评估，永不激活，仅供评估 | 永不激活 |
| `INSERT_ORDER_WHATIF` | `INSERT_IMPACT_ANALYSIS` | 插单影响分析，永不激活，仅供评估 | 永不激活 |
| `LOCAL_RESCHEDULE` | `INSERT_RESCHEDULE` | 插单后局部重排，审批后可激活 | 审批后激活 |
| `LOCAL_RESCHEDULE` | `MANUAL_ADJUSTMENT` | PMC 手动调整触发重排，审批后可激活 | 审批后激活 |
| `MANUAL_RESCHEDULE` | `MANUAL_ADJUSTMENT` | 单Domain内较大范围人工重排（范围较大但严格限定单Domain），审批后可激活 | 审批后激活 |

**⚠️ 红线**：`FULL_SCHEDULE` 不属于白天五个组合；夜间 `FULL_SCHEDULE` 不在 ScopeJson 中设置 Purpose。

**Candidate 生命周期状态机（v5.1.1）**：
```
BUILDING ──(落库成功)──→ CANDIDATE ──(审批记录APPROVED + 3号位显式激活)──→ ACTIVE ──→ ARCHIVED
    │                        │
    └──(超时/异常)──→ FAILED  └──(审批记录REJECTED，PlanVersion保持CANDIDATE)
```
- `BUILDING`：排程进行中，超时30分钟自动转 FAILED
- `CANDIDATE`：排程完成待审批；审批驳回后 PlanVersion 仍保持 CANDIDATE（不新增 REJECTED 状态）
- `ACTIVE`：当前生效版本，同域同时只有一个
- `ARCHIVED`：被新版本替换后归档
- `FAILED`：超时或异常，归档保留

**⚠️ 状态机红线**：PlanVersion 不存在 `APPROVED` 或 `REJECTED` 状态。审批结果记录在独立审批记录（`RequestType=CANDIDATE_ACTIVATION`）的 `Status` 字段，不修改 PlanVersion.Status。激活服务校验的是：PlanVersion.Status=CANDIDATE + 审批记录.Status=APPROVED。

**ScopeJson 固定11字段契约（v1.25）**：
```json
{
  "Purpose": "CTP | INSERT_IMPACT_ANALYSIS | INSERT_RESCHEDULE | MANUAL_ADJUSTMENT",
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

**⚠️ ScopeJson 固定 Schema 红线（v1.25 + v3.16 修正）**：
- `ScopeJson` **固定为上述 11 字段**，禁止擅自新增扩展字段。
- `ExpectedDomainKeysJson` 是 `ScheduleRun` 的**独立运行级字段**，**不属于** `ScopeJson`，不违反 ScopeJson 固定 Schema 红线。
- 废止旧表述：`ScopeJson.ExpectedDomainKeys` / "ScopeJson 中冻结 ExpectedDomainKeys" / "ScopeJson 只能包含一个 DomainKey" / "FULL_SCHEDULE 通过 ScopeJson 保存 ExpectedDomainKeys"——均为错误口径，须使用 `ScheduleRun.ExpectedDomainKeysJson`。

**⚠️ ScopeJson 完整校验规则（v1.25）**：

1. **归属红线**：`DomainKey` / `DataCutoffTime` / `BasePlanVersionId` 不属于 ScopeJson，分别属于 PlanVersion 和 ScheduleRun
2. **命名红线**：`FactoryCode` → `FactoryIds`；`OrderIds` → `OrderCanonicalIds`；`ResourceIds` → `ResourceGroupIds`
3. **不可变红线**：`ScheduleRun` 创建后 ScopeJson 不可修改（含 LockedTaskIds/MaxImpactedOrders 等所有字段）
4. **`LOCAL_RESCHEDULE` 硬校验**（同时满足）：
   - `PlanHorizonStart` 非空
   - `PlanHorizonEnd` 非空
   - `PlanHorizonEnd >= PlanHorizonStart`
   - 以下五个范围数组**至少一个非空**：`OrderCanonicalIds` / `FactoryIds` / `ProductFamilyIds` / `ResourceGroupIds` / `LockedTaskIds`
5. **全空范围红线**：所有五个范围数组**和**计划窗口**全部**为空，只允许经过明确审批的 `MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT`；其他组合一律拒绝
6. **页面便捷参数冲突**：页面便捷参数（如"当前工厂快捷选择"）与 ScopeJson 明确写入值冲突时**必须拒绝**，不得静默覆盖；由调用方在提交前对齐
7. **MaxImpactedOrders 语义**：
   - 有值时必须大于 0；null 表示不限（不得用 0 表示不限）
   - 达到上限后**必须停止扩大影响范围**（不再纳入新受影响订单），不得继续排程后再截断
8. **AllowTouchFrozenZone=false 时的 LockedTaskIds 归一化**：
   - 冻结区 Task 必须在**创建 ScheduleRun 之前的归一化阶段**加入 `LockedTaskIds`
   - **不得**在 ScheduleRun 创建后追加（违反不可变红线）

---

**流程步骤**：

#### **步骤4.1：3号位调用 CreateRealtimeEvaluationRunAsync（正式入口）**

**负责人**：3号位

**动作**：
- **【3号位】** 调用 `CreateRealtimeEvaluationRunAsync(RealtimeEvaluationRunRequest request)`，服务内部依次完成：
  1. `ScopeJson` 校验与归一化（见 4.1.1）
  2. `Scenario` 适用性判断与创建（见 4.1.2）
  3. `ScheduleRun` 创建（见 4.2 的内部说明）
  4. Candidate `PlanVersion` 创建（见 4.3 的内部说明）
- **【3号位】** 服务返回：`ScheduleRunId` / `CandidatePlanVersionId` / `ScenarioId`（可空）

**⚠️ 服务契约红线**：本方法是白天 Candidate 主链**正式七个服务契约**中的入口方法：
1. `CreateRealtimeEvaluationRunAsync`（本步骤，入口）
2. `PrepareRealtimeOrderSnapshotAsync`
3. `EnsureRealtimeBomReadyAsync`
4. `PullRealtimeBOMResultFromODSAsync`
5. `BuildRemainingSupplyContextAsync`
6. `BuildScheduleContextAsync`
7. `ActivateCandidatePlanVersionAsync`

---

#### **步骤4.1.1：ScopeJson 校验与归一化（CreateRealtimeEvaluationRunAsync 内部）**

**负责人**：3号位（服务内部）

**动作**：按下方"ScopeJson 完整校验规则"执行；校验通过后归一化为完整11字段 JSON；校验失败拒绝创建并返回错误。

---

#### **步骤4.1.2：Scenario 适用性判断（服务内部）**

**负责人**：3号位 + 5号位

**动作**：
- **【5号位】** 判断是否需要创建 `Scenario`：
  - `ScopeJson.Purpose = CTP` 或 `INSERT_IMPACT_ANALYSIS`：**通常创建** Scenario，记录插单假设条件和优化目标；`ScheduleRun.ScenarioId` 允许为空
  - `ScopeJson.Purpose = INSERT_RESCHEDULE` 或 `MANUAL_ADJUSTMENT`：不要求创建 Scenario，直接进入 ScheduleRun 创建
- **【3号位】** 如需 Scenario，创建 `Scenario` 记录（含假设条件、优化目标、关联订单列表）

**数据流向**：
```
RealtimeEvaluationRunRequest → 3号位.CreateRealtimeEvaluationRunAsync
  → 4.1.1 ScopeJson校验归一化 → 4.1.2 Scenario适用性 → 4.2 ScheduleRun → 4.3 PlanVersion
  → 返回 { ScheduleRunId, CandidatePlanVersionId, ScenarioId }
```

---

#### **步骤4.2：创建 ScheduleRun**

**负责人**：3号位

**动作**：
- **【3号位】** 创建 `ScheduleRun`，写入以下**物理字段**：
  - `RunType`：三个正式值之一（`INSERT_ORDER_WHATIF` / `LOCAL_RESCHEDULE` / `MANUAL_RESCHEDULE`）
  - `BasePlanVersionId`：当前 ACTIVE 版本 ID
  - `StrategyProfileVersionId`：当前生效的排程策略版本
  - `ScopeJson`：归一化后的完整11字段 JSON（创建后不可修改），其中 **`ScopeJson.Purpose`** 承载 Purpose 值（`CTP` / `INSERT_IMPACT_ANALYSIS` / `INSERT_RESCHEDULE` / `MANUAL_ADJUSTMENT`）
  - `DataCutoffTime`：当前时间（数据快照边界）
  - `ExpectedDomainKeysJson`：`ScheduleRun` **独立运行级字段**（不属 ScopeJson），写入单元素 JSON 数组 `["BasePlanVersion.DomainKey"]`；创建后不可修改；白天 Candidate 严格单 Domain（恰好 1 个元素，等于 `BasePlanVersion.DomainKey` = 本次 Candidate `PlanVersion.DomainKey`）
  - `Status`：`RUNNING`
  - `ScenarioId`：如有 Scenario 则关联
- **⚠️ 红线**：`ScheduleRun` **没有**独立的 `Purpose` 物理字段；Purpose 只能写入 `ScopeJson.Purpose`
- **⚠️ 组合红线**：合法的是"五个 RunType+Purpose 组合"，不是"五个 RunType 值"（三个 RunType × 特定 Purpose 组合出五种合法情况）
- **【3号位】** 并发冲突检测：同域同时只允许一个 `BUILDING` 状态的 PlanVersion；若已存在则拒绝并返回冲突信息

**数据流向**：
```
ScopeJson(含Purpose) + BasePlanVersionId + StrategyProfileVersionId + ExpectedDomainKeysJson(["BasePlanVersion.DomainKey"]) → 3号位.创建ScheduleRun(RUNNING) → ScheduleRunId
```

---

#### **步骤4.3：创建 Candidate 类 PlanVersion 版本壳（初始 Status=BUILDING）（CreateRealtimeEvaluationRunAsync 内部）**

**负责人**：3号位（服务内部）

**动作**：
- **【3号位】** 读取 `BasePlanVersion`（`ScheduleRun.BasePlanVersionId` 指向的版本）
- **【3号位】** 校验 `BasePlanVersion.Status = ACTIVE`；非 ACTIVE 拒绝创建
- **【3号位】** 创建 `PlanVersion`，写入以下**物理字段**（对齐 DDL v5.1.1）：
  - `Status = BUILDING`
  - **`SourceScheduleRunId = ScheduleRun.Id`**（正式字段名）
  - `SourceSimulationRunId = NULL`
  - **`DomainKey = BasePlanVersion.DomainKey`**（激活事务硬约束前置条件）
  - `VersionCategory`：对应 Candidate 类型（服务生成）
  - `VersionCode`：由服务生成
  - `PlanHorizonStart` / `PlanHorizonEnd`：取自 `ScheduleRun.ScopeJson`
  - `ComputeMode`：对应计算模式
  - `CreatedBy`：调用者身份
- **⚠️ PlanVersion 字段红线**：
  - PlanVersion **不使用** `ScheduleRunId` 字段；正式字段名是 `SourceScheduleRunId`
  - PlanVersion 表**不存在** `BasePlanVersionId` 字段；`BasePlanVersionId` 只属于 `ScheduleRun`；读取时通过 `PlanVersion.SourceScheduleRunId → ScheduleRun.BasePlanVersionId` 追溯，**不得**在 PlanVersion 冗余存储
- **【3号位】** 同 `DomainKey` BUILDING 并发检查加锁：同 `DomainKey` 同时只允许一个 `BUILDING` 版本
- **【3号位】** 将 `PlanVersionId` 传递给 2号位，启动数据构造流程
- **【3号位】** 启动超时计时器（30分钟），超时自动将 `PlanVersion.Status` 更新为 `FAILED`

**⚠️ 红线**：
- Candidate `DomainKey` 必须在创建时确定并等于 `BasePlanVersion.DomainKey`；不得留空或延迟填充
- 激活事务 `Candidate.DomainKey = Base.DomainKey` 校验就是本步骤保证的
- 同域唯一 ACTIVE、同域唯一 BUILDING 都基于 `DomainKey`
- **白天 Candidate 严格单 Domain（V1 决策）**：一个白天重排 `ScheduleRun` → 一个 `DomainKey` → 一个 `BasePlanVersionId`（该 Domain 当前 ACTIVE 版本）→ 一个 Candidate PlanVersion；`DomainKey` 由 `ExpectedDomainKeysJson` 独立字段（恰好 1 个元素）承载，**不**写入 11 字段 `ScopeJson`；V1 **不允许**一个 Candidate 跨多个 Domain，**不实现**多域 Candidate 原子激活。若 PMC 需重算两个存在跨域关系的 Domain，由后台按 `Domain_Dependency` 顺序拆成两个单 Domain 重排（分别创建 `ScheduleRun` 和 Candidate PlanVersion）；"人工选择多个 Domain 重算"是多个单域重排任务的编排，不代表 Candidate 本身变成多域
- **⚠️ ExpectedDomainKeysJson（独立字段，不属于 ScopeJson）**：3号位创建白天 Candidate 的 `ScheduleRun` 时，必须写入 `ExpectedDomainKeysJson = ["BasePlanVersion.DomainKey"]`（**恰好 1 个元素**），并校验其唯一元素 = `BasePlanVersion.DomainKey` = 本次 Candidate `PlanVersion.DomainKey`；**不得**为保存 DomainKey 而修改 11 字段 ScopeJson

**数据流向**：
```
ScheduleRun.BasePlanVersionId → 3号位.读取Base(校验ACTIVE)
→ 3号位.创建PlanVersion(BUILDING, SourceScheduleRunId=ScheduleRun.Id, DomainKey=Base.DomainKey, VersionCategory/VersionCode/PlanHorizon/ComputeMode/CreatedBy)
→ PlanVersionId → 2号位
```

**⚠️ 白天 Candidate 运行完成规则（区别于 FULL_SCHEDULE 步骤5.3）**：

白天 Candidate（`LOCAL_RESCHEDULE` / `MANUAL_RESCHEDULE` / `INSERT_ORDER_WHATIF`）是**严格单 Domain** 运行（`ExpectedDomainKeysJson` 恰好 1 项），其运行完成条件**不同于**夜间 `FULL_SCHEDULE` 多 Domain 汇总（步骤5.3）：

- **正常完成**：Candidate 计算成功 + Order/Task/Pegging/ExplanationFact/Summary 等结果落盘成功 + `PlanVersion.Status` 由 `BUILDING` 转为 `CANDIDATE` → `ScheduleRun.Status = COMPLETED`（写 `ScheduleRun.CompletedAt`）
- **失败完成**：计算失败或落盘失败 + 重试耗尽或确认不可恢复 + `PlanVersion.Status` 转 `FAILED` → `ScheduleRun.Status = FAILED`（写 `ScheduleRun.CompletedAt`）
- **审批与激活属于 PlanVersion 版本生命周期**：`CANDIDATE` → 审批记录（`RequestType=CANDIDATE_ACTIVATION`）→ `ACTIVE` 或保持 `CANDIDATE` / 转 `FAILED`；该阶段**不得再次修改已经完成（`COMPLETED`/`FAILED`）的 `ScheduleRun.Status`**
- **白天单域运行不产生 `PARTIAL_SUCCESS`**：`PARTIAL_SUCCESS` 仅用于多预期 Domain 的 `FULL_SCHEDULE` 运行；单 Domain Candidate 正常状态机只有 `COMPLETED` / `FAILED`

---

#### **步骤4.4：2号位准备独立 Order 快照**

**负责人**：2号位

**动作**：
- **【2号位】** 调用 `PrepareRealtimeOrderSnapshotAsync(candidatePlanVersionId, basePlanVersionId, scopeJson)`：
  - 复制 Base 版本（`basePlanVersionId`）的 Order 快照作为基础
  - 仅对 `scopeJson.OrderCanonicalIds` 指定的订单，叠加 `Order_Canonical` 中的最新变化
  - 结果写入本次 `candidatePlanVersionId` 的独立分区，与其他版本数据物理隔离
  - 仅包含 `Status=OPEN` 的订单（CLOSED/CANCELLED 不进入）
- **⚠️ 红线**：不得重新从当前 `Order_Canonical` 构建目标域全量最新订单快照；未被 `OrderCanonicalIds` 指定的订单变化不得静默带入 Candidate

**数据流向**：
```
Base版本Order快照 + OrderCanonicalIds指定订单的最新变化 → PrepareRealtimeOrderSnapshotAsync → Candidate独立Order快照(candidatePlanVersionId分区)
```

---

#### **步骤4.5：2号位实时 BOM 完整链（v1.25）**

**负责人**：2号位

**动作**：
- **【2号位】** 对每个需要 BOM 的订单执行 **BOM 复用合法性判断**：
  - **可复用条件（同时满足）**：
    1. 订单的 `MaterialCode` / `FactoryCode` / `RequestedBOMNO` 与 Base 版本一致
    2. 相关主数据（Material、BOM、工艺路线）在 `DataCutoffTime` 内未发生变化
    3. Base 版本存在完整 `OrderBomRequestLink` 指向合法 `RequestDetail`
    4. `APS_BOM_RAW` / `APS_BOM_STAGE_PATH_RAW` / `APS_BOM_CROSS_FACTORY_EDGE_RAW` 三张 RAW 表均可通过 `OrderBomRequestLink` 追溯到合法 `BatchNo`
  - **可复用** → 直接复用 Base 版本 BOM 切片，写入本次 `candidatePlanVersionId` 的 `OrderBomRequestLink`，跳过 `EnsureRealtimeBomReadyAsync`
  - **不可复用**（任一条件不满足） → 进入下方 RequestDetail 创建流程
  - **⚠️ 复用红线**：
    - 不允许跨订单、跨工厂或跨错误 BOM 版本静默复用
    - 主数据切片变化即视为不可复用，不得放宽判断
    - 任何条件不满足即重新创建 RequestDetail，不允许"部分复用"
- **【2号位】** 对不可复用的订单，创建 `RequestDetail`，获取 `requestDetailId`
- **【2号位】** 调用 `EnsureRealtimeBomReadyAsync(requestDetailId)`：
  - 触发 `sp_ExpandBOMRealtime_vNext(@RequestDetailId)` → 写入 `MES_APS_BOM_Workset_Realtime`
  - 触发 `sp_EnrichBOMWorksetRealtime` → 写入 `MES_APS_BOM_Workset_StageDetail_Realtime`
  - 触发 `sp_GenerateBOMCrossFactoryEdgeRealtime` → 写入 `MES_APS_BOM_Workset_CrossFactoryEdge_Realtime`
  - 轮询 `MES_API_BOM_Request_Realtime.Status`（`TOP 1 ... ORDER BY Id DESC`）：
    - `READY`：唯一完成权威，继续下一步
    - `PROCESSING`：继续轮询（不超时不退出）
    - `FAILED`：不自动重试；显式重试须新建 Request 行
  - 三张 Realtime 表均允许0行；`CrossFactoryEdge` 为0行合法
- **【2号位】** 调用 `PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)`：
  - 将 Realtime 表结果写入 APS 三张 RAW 表：
    - `APS_BOM_RAW`
    - `APS_BOM_STAGE_PATH_RAW`
    - `APS_BOM_CROSS_FACTORY_EDGE_RAW`
  - 三张 RAW 表的 `BatchNo` 格式为 `RT:RD:{requestDetailId}`
  - 写入 `OrderBomRequestLink`（订单与 RequestDetail 的关联记录）

**数据流向**：
```
订单 → 2号位.BOM复用合法性判断
  ├─ 可复用: → 复用Base BOM切片 → 写入candidatePlanVersionId的OrderBomRequestLink
  └─ 不可复用: → 创建RequestDetail → EnsureRealtimeBomReadyAsync(requestDetailId)
                → sp_ExpandBOMRealtime_vNext → MES_APS_BOM_Workset_Realtime
                → sp_EnrichBOMWorksetRealtime → MES_APS_BOM_Workset_StageDetail_Realtime
                → sp_GenerateBOMCrossFactoryEdgeRealtime → MES_APS_BOM_Workset_CrossFactoryEdge_Realtime
                → MES_API_BOM_Request_Realtime.Status=READY
                → PullRealtimeBOMResultFromODSAsync(requestDetailId, candidatePlanVersionId)
                → APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW + APS_BOM_CROSS_FACTORY_EDGE_RAW(BatchNo=RT:RD:{requestDetailId})
                → OrderBomRequestLink
```

---

#### **步骤4.6：2号位按Base供给状态与Candidate最新MES事实构建 RemainingSupplyContext**

**负责人**：2号位

Candidate严格以Base ACTIVE版本和单Domain Scope为边界，不重新读取当前全量库存。构建时逐类处理：

| 状态 | Candidate处理 |
|---|---|
| 已实际消耗/完成 | 永久排除，不可再用 |
| 有效HardLock | 原需求、供给业务键和数量原样保留 |
| ExecutionLock投入侧 | 现实工单必须继续，已实物预留投入保持 |
| ExecutionLock未来产出 | 作为固定数量/Stage/AvailableTime的未来供给；除HardLock部分外可SOFT重分配 |
| Scope外SOFT分配 | 保留，避免单Domain/局部范围越界影响 |
| Scope内SOFT分配 | 释放回候选池，按Candidate最新需求重新Pegging |
| Base未分配供给 | 按Base准确切片进入候选池 |

因此不再使用旧公式“Base原始供给－Base全部已分配”。

**红线**：
- 不得按最新InventoryFact重建全域供给；
- 不得释放Scope外SOFT、HardLock或实际消耗；
- 管道/Received必须读取Base对应ScheduleRun/DataCutoffTime切片；
- Candidate对ExecutionLock只改变普通产出归属，不改变MES工单、PI、Stage或剩余执行量。

```text
Base ACTIVE供给与Ledger
+ ExecutionLock/HardLock当前事实
+ Scope边界
→ 状态分类恢复
→ RemainingSupplyContext
```

---

#### **步骤4.7：Candidate重新Pegging、TaskDraft与有限产能排程**

- **【2号位】** 装载Candidate Order快照、BOM RAW、RemainingSupplyContext、PI位置切片、ExecutionLock/HardLock、资源日历和Scope锚点。
- 仅对Scope内允许变化的需求和SOFT分配重新执行阶段2主链；Scope外Task/分配作为不可越界锚点。
- **【2号位+5号位】** 形成Ledger、LogicalBlock和TaskDraft；不直接持久化正式Task。
- **【1号位】** 在有限产能和交期约束下完成时间排定及允许的跨需求合并/拆分，返回ScheduledTaskDraft和ComponentShares。
- `LockedTaskIds`、HardLock和ExecutionLock均属于硬约束；普通SOFT关系不因Base版本存在而获得稳定性偏好。

```text
Candidate Context
→ 重新Pegging(Scope内SOFT)
→ Ledger/LogicalBlock/TaskDraft
→ 1号位.有限产能合并拆分与排程
→ ScheduledTaskDraft
```

---

#### **步骤4.8：2号位持久化，更新为 CANDIDATE**

**负责人**：2号位

**动作**：
- **【2号位】** 使用批量方式将 Task / ShippingTask / PeggingAllocationLedger / PeggingSupplyAllocation / Task-to-Task Pegging / PI位置快照 / ScheduleExplanationFact 落库（关联 PlanVersionId）；采购建议、延期清单、优先级继承事实统一以 `ScheduleExplanationFact` 承载
- **【2号位】** 落库成功后，在同一事务中：
  - 将 `PlanVersion.Status` 从 `BUILDING` 更新为 `CANDIDATE`
  - 回填 `ScheduleRun.Status=COMPLETED` + `ScheduleRun.CompletedAt`（白天 Candidate 链路：本 ScheduleRun 严格单 Domain，落库完成即该 Run 终态；与夜间多 Domain 须由步骤5.3 汇总不同）
- **【2号位】** 禁止将 PlanVersion 直接更新为 `ACTIVE`

**数据流向**：
```
内存Task → 2号位.SqlBulkCopy → APS.Task表 → [同一事务] PlanVersion.Status=CANDIDATE + ScheduleRun.Status=COMPLETED
```

**⚠️ 红线**：2号位在白天 Candidate 链路中禁止直接激活版本；ACTIVE 状态只能由 3号位通过 `ActivateCandidatePlanVersionAsync` 写入。

---

#### **步骤4.9：4号位展示差异，PMC 审批**

**负责人**：4号位 + PMC/0号位

**动作**：
- **【4号位】** 展示 Candidate 版本与当前 ACTIVE 版本的差异：
  - KPI 对比（准交率、延期订单数、瓶颈负荷率）
  - 受影响订单列表（新增延期 / 延期改善 / 无变化）
  - 甘特图差异高亮
- **【PMC/0号位】** 决策：审批通过或驳回
- **【3号位】** 创建独立审批记录（`RequestType=CANDIDATE_ACTIVATION`），写入审批结果：
  - 通过：审批记录 `Status=APPROVED`；PlanVersion 保持 `CANDIDATE`
  - 驳回：审批记录 `Status=REJECTED`；PlanVersion 保持 `CANDIDATE`（不新增 REJECTED 状态）

**数据流向**：
```
CANDIDATE版本 vs ACTIVE版本 → 4号位.差异展示 → PMC审批 → 3号位.审批记录(CANDIDATE_ACTIVATION, APPROVED/REJECTED)
PlanVersion.Status 始终保持 CANDIDATE，不修改
```

---

#### **步骤4.10：3号位激活事务**

**负责人**：3号位

**动作**：
- **【3号位】** 调用 `ActivateCandidatePlanVersionAsync(planVersionId)`，使用 Serializable 事务（或等价串行化控制）对 Candidate 行加锁，在同一事务中：
  1. 幂等检查：若目标版本已为 ACTIVE，直接返回成功，不重复执行
  2. 校验 `PlanVersion.Status=CANDIDATE`（非 CANDIDATE 拒绝激活）
  3. 校验存在 `RequestType=CANDIDATE_ACTIVATION` 且 `Status=APPROVED` 的审批记录
  4. 校验 `SourceScheduleRunId` 存在，读取对应 ScheduleRun
  5. 校验 RunType + Purpose 组合，**永远拒绝** `INSERT_ORDER_WHATIF+CTP` 和 `INSERT_ORDER_WHATIF+INSERT_IMPACT_ANALYSIS`；**只允许以下三种组合激活**：
     - `LOCAL_RESCHEDULE + INSERT_RESCHEDULE`
     - `LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT`
     - `MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT`
  6. 校验 `BasePlanVersionId` 非空
  7. 校验新旧版本 `DomainKey` 一致
  8. 校验无并发激活冲突（同域无其他正在激活的事务）
  9. 同一事务：将当前同域 ACTIVE 版本 `Status` 更新为 `ARCHIVED`，将本版本 `Status` 更新为 `ACTIVE`，写入 `ActivatedAt` + `ActivatedBy`
  10. 事务提交失败则全部回滚，PlanVersion 保持 CANDIDATE 状态
- **【3号位】** 通过 SignalR 广播前端刷新

**⚠️ 红线**：
- 激活校验的是 `PlanVersion.Status=CANDIDATE` + 审批记录 `Status=APPROVED`，不存在 `PlanVersion.Status=APPROVED` 状态
- 历史版本不能通过修改指针直接重新激活（只能重新走完整 Candidate 链路）
- 不使用 `ActiveVersionId` 字段；不使用 `System_Active_Version` 表
- 激活事务失败不影响当前 ACTIVE 版本的正常运行

**数据流向**：
```
CANDIDATE版本 + 审批记录(CANDIDATE_ACTIVATION, APPROVED) → 3号位.ActivateCandidatePlanVersionAsync → [同一事务] 旧ACTIVE→ARCHIVED + 新版本CANDIDATE→ACTIVE → SignalR广播
```

---

#### **步骤4.11：激活后异步后处理**

**负责人**：2号位（BackgroundService）+ 3号位

**动作**：
- **【2号位】** 异步生成读模型三张表（非阻塞）：
  - `OrderScheduleSummary`（订单级计划完工/延期/风险/主因代码）
  - `ResourceLoadSummary`（资源×日期：负荷小时/负荷率/是否瓶颈）
  - `PlanKpiSummary`（版本级：准交率/延期订单数/VIP延期/平均负荷率/瓶颈数）
- **【2号位】** 落盘阶段已同步写入 `ScheduleExplanationFact`（结构化原因事实）；异步聚合为 Summary 读模型
- 以上后处理失败不影响版本激活状态

**数据流向**：
```
ACTIVE版本Task → 2号位.异步后处理 → OrderScheduleSummary + ResourceLoadSummary + PlanKpiSummary
ScheduleExplanationFact → 2号位.异步聚合 → OrderScheduleSummary/ResourceLoadSummary/PlanKpiSummary → 3号位/页面消费
```

**业务意义**：
- 白天局部重排结果不立即生效，经 PMC 审批后方可替换当前计划
- 与凌晨全量排程的自动激活路径完全隔离，避免白天计划震荡
- 支持多个 Candidate 版本并存，PMC 可对比后择优激活
- `INSERT_IMPACT_ANALYSIS`（CTP 检查）产出的版本永不激活，仅供评估

---

### 🔗 场景5：次日全量恢复发布承诺、现实执行、HardLock与普通产出

**目标**：新PlanVersion生成新TaskId；已向MES发布或已形成MES工单的现实承诺稳定延续；普通通用产出仍按当日最新优先级重新Pegging。

#### **步骤5.1：先恢复互斥的物理数量身份**

对每张PI的`AvailableProductionQty`，2号位只允许以下物理身份互斥分解：

1. 已完成/已消耗事实；
2. 已形成`ExecutionLock`的现实执行投入和未来产出；
3. `MESPlanRelease=PUBLISHED`但尚未被MES建单的发布承诺；
4. 尚未发布的普通PI位置切片。

同一数量不能同时出现在ExecutionLockedOutput、PUBLISHED发布承诺和普通PI竞争池中。新版本对PUBLISHED发布承诺生成新的Task表示，但继续关联原MESPlanReleaseId，不生成新ReleaseItemKey。

#### **步骤5.2：在物理供给上应用Hard/Soft归属**

HardLock是归属维度，不是新增物理数量：

```text
每项物理供给数量
= HARD已归属 + SOFT已分配 + 当前未分配
```

- 先恢复HardLock的`RemainingLockedQty`；部分释放后仍有余额时状态继续ACTIVE。
- 剩余普通供给进入SOFT竞争，不保留上一版本关系稳定性偏好。

#### **步骤5.3：更新ExecutionLock数量**

```text
0 <= CompletedQty + CancelledQty + RemainingExecutionQty
   <= OriginalExecutionQty
```

`RemainingExecutionQty`由2号位依据MES现实工单状态和累计事实维护，只表示当前现实工单仍承诺的未来Stage产出上限。小工序状态4本身不直接关闭ExecutionLock；整张工单终结后，未完成且未正式取消的差额退出ExecutionLock并返回PI未承诺剩余池。

#### **步骤5.4：按当日优先级重新Pegging**

现实工单不因需求归属变化而取消或重建。HardLock份额保持；普通执行产出和未发布PI供给按当前优先级竞争。

#### **步骤5.5：新Task关联稳定对象**

- 已建MES工单：新Task关联原`ExecutionLockId`和原`MESPlanReleaseId`；不得再次发布。
- 已发布未建单：新Task只关联原`MESPlanReleaseId`，状态仍为PLANNED；MES建单后再写ExecutionLockId并转RELEASED。
- 未发布普通任务：不关联上述对象，可按本次计划形成新的发布单元。
- Ledger记录新版本Hard/Soft需求份额，旧Task保留在旧PlanVersion中只读。

---

## 总结：32个完整流程覆盖

本文档完整覆盖了APS系统的32个核心流程：

**第一部分：凌晨全量排程主流程**（1个主流程，含6个阶段）
- 阶段0：触发起点与分域版本壳
- 阶段1：数据备料、生产指示总量闭合与位置快照
- 阶段2：净需求、Ledger、LogicalBlock与TaskDraft
- 阶段3：有限产能排程、合并拆分与ScheduledTaskDraft
- 阶段4：业务校验与原因事实
- 阶段5：批量落盘、独立发布与运行收口

**第二部分：跨厂协同全流程**（4个场景）
1. 同域跨厂物流（厂间订单发货Task）vs 异域跨族依赖（虚拟库存硬约束）
2. 跨域优先级继承（01:50预处理刷库）
3. 同域跨厂半成品在途管理（厂间订单发货Task场景）
4. 异域跨族上游延期自动顺延（虚拟库存硬约束场景）

**第三部分：分域计算全流程**（3个场景 + 1个补充）
1. 分域任务分配与并发调度（含V1共享资源配额/预留窗口粗隔离策略补充）
2. 分域失败重试与降级
3. 单向硬约束传递（废除事后修复，避免排程震荡）

**第四部分：动态实绩与异常重排**（6个场景）
1. MES实绩接收与去重
2. 设备故障与修复影响评估
3. 任务暂停与恢复
4. 报废与补料
5. 订单取消与变更
6. 局部重排执行

**第五部分：人工干预与调整流程**（3个场景）
1. PMC手动拖拽调整
2. 任务手动锁定与优先级调整
3. 计划审批与冻结

**第六部分：数据同步与一致性流程**（3个场景）
1. ERP增量订单同步
2. 主数据变更与版本管理
3. MES实时视图快照与发布幂等处理

**第七部分：监控反馈与容错流程**（7个场景）
1. 排程KPI统计与分析
2. 交期达成率跟踪
3. 执行监控与偏差预警
4. 排程超时中断与恢复
5. 内存溢出监控与告警
6. 数据库连接池管理
7. 异常任务高亮显示

**第八部分：计划发布与版本管理**（5个场景）
1. MES拉取计划发布与现实工单确认
2. Candidate版本激活与历史版本只读查询
3. 版本历史查询与对比
4. 白天实时评估与 Candidate 闭环（v3.15 新增）
5. 次日跨版本恢复ExecutionLock、HardLock与普通产出重新Pegging

**总计**：32个完整流程，每个流程都明确标注了负责岗位（0-5号位）和数据流向。

---

## 附录A：ERP 订单时间戳增量轮询（正式口径，v1.25 对齐）

**负责人**：2号位（数据基础设施）

**⚠️ 架构定位**：
- ERP 订单接口采用**时间戳增量轮询**（每小时/每日凌晨），从 ODS 契约视图 `ext_v_APS_SalesOrder` 读取增量，写入 `ERP_Order_Staging`
- ERP 订单同步与 02:00 排程主流程在**物理调度上撕裂**：夜间排程不等待下一轮增量轮询，按 00:38 已确定的 `ScheduleRun.DataCutoffTime` 切片和快照批次读取
- **CDC 路径已废弃**（详见集成接口 v1.25）：不再使用 CDC 守护进程 / `CdcSyncDaemon` / `_cdcEngine` / `CDC_ChangeLog` / 5 秒轮询 / CDC 积压追赶等旧机制

---

### 📡 时间戳增量轮询实现方式

**技术选型**：Hangfire 定时任务 + Dapper（对齐 v1.25）

**代码契约（C#）**——**持久化复合水位 + 事务边界推进**：
```csharp
public class ERPOrderSyncService
{
    public async Task IncrementalSyncAsync(DateTime dataCutoffTime)
    {
        // 1. 从 APS.SyncCheckpoint 读取上次持久化的复合水位
        var checkpoint = await _apsDb.GetSyncCheckpointAsync("ERP_ORDER");
        var lastUpdatedAt   = checkpoint.LastUpdatedAt;
        var lastSourceOrder = checkpoint.LastSourceOrderId;

        // 2. 复合水位查询，避免相同 SourceUpdatedAt 时间戳的多条记录在分页边界被漏掉
        var orders = await _erpDapper.QueryAsync<ErpOrderRow>(@"
            SELECT TOP (@BatchSize) * FROM ODS.ext_v_APS_SalesOrder
            WHERE (
                    SourceUpdatedAt > @LastUpdatedAt
                 OR (SourceUpdatedAt = @LastUpdatedAt AND SourceOrderId > @LastSourceOrderId)
                  )
              AND SourceUpdatedAt <= @DataCutoffTime
            ORDER BY SourceUpdatedAt, SourceOrderId",
            new {
                LastUpdatedAt = lastUpdatedAt,
                LastSourceOrderId = lastSourceOrder,
                DataCutoffTime = dataCutoffTime,
                BatchSize = 1000
            });

        // 3. 空结果直接返回，不推进水位、不调用 Max()
        if (orders.Count == 0) return;

        // 4. 事务边界：写入 Staging + 提升 Canonical + 推进水位，同一事务
        //    完整路径：ext_v_APS_SalesOrder
        //      → ERP_Order_Staging(PENDING)
        //      → sp_ValidateAndPromoteOrders → Order_Canonical
        //      → 夜间 sp_SyncOrdersToPartitionTable → Order
        using var tx = _apsDb.BeginTransaction();
        await _apsDb.InsertToOrderStaging(orders, tx);
        await _apsDb.ExecuteSpValidateAndPromoteOrders(tx);

        var last = orders.OrderBy(o => o.SourceUpdatedAt).ThenBy(o => o.SourceOrderId).Last();
        await _apsDb.UpdateSyncCheckpointAsync(
            "ERP_ORDER", last.SourceUpdatedAt, last.SourceOrderId, tx);

        tx.Commit();
        _logger.LogInformation($"ERP 增量轮询完成：{orders.Count} 条订单写入 Staging");
    }
}
```

**⚠️ 水位红线**：
- 水位必须使用 **(SourceUpdatedAt, SourceOrderId) 复合键**，单独使用 SourceUpdatedAt 会在同一时间戳跨批次时漏行
- 水位必须**持久化到** `APS.SyncCheckpoint`；不得只保存在服务内存
- 只有 **Staging 写入 + sp_ValidateAndPromoteOrders 提升 + 事务提交** 全部成功后才推进水位
- 空结果直接返回，**不调用 Max()**

---

### ⏱️ 轮询触发频率

**推荐频率**：白天每小时一次（Hangfire Cron `0 * * * *`）；凌晨作为数据备料一环执行

**批量拉取策略**：从 `SourceUpdatedAt` 水位递增读取，分页控制单批规模，避免单次事务过大

---

### 🔒 DataCutoffTime 切片机制（正式时序）

**统一时序**：
- **00:38** 创建 `ScheduleRun`（`RunType=FULL_SCHEDULE`），确定 `DataCutoffTime`
- **02:00** 读取该 `ScheduleRun`，按 `DataCutoffTime` 对 APS 数据库做时间切片快照
- 具备来源时间戳的数据严格按 `SourceUpdatedAt <= DataCutoffTime` 切片；暂无来源时间戳的字段按已记录快照批次读取，差异登记 `APS_ETL_Log`
- 02:00 **不**重新创建 `ScheduleRun`；不读取 `DataCutoffTime` 之后的数据
- 白天每小时轮询新写入 `Order_Canonical` 的订单不纳入本次 `ScheduleRun`；由 PMC 决策是否发起 Candidate 主链或次日 02:00 全量排程处理

**数据库锁隔离（正式方案二选一）**：

SQL Server **没有** `READ COMMITTED SNAPSHOT` 事务隔离级别；下面两种是仅有的合法路径。

**方式1：数据库级 RCSI（推荐）**
```sql
-- 一次性开启，之后普通 READ COMMITTED 查询自动使用版本读取，不被写锁阻塞
ALTER DATABASE APS SET READ_COMMITTED_SNAPSHOT ON;
```

**方式2：会话级 Snapshot 隔离**
```sql
-- 前提：数据库已开启 ALLOW_SNAPSHOT_ISOLATION
ALTER DATABASE APS SET ALLOW_SNAPSHOT_ISOLATION ON;

-- 每个查询会话内使用：
SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
```

**⚠️ 红线**：`.AsNoTracking()` 只是关闭 EF 实体跟踪，**不能**代替数据库快照隔离，也不能保证不被写锁阻塞；两者不是并列替代方案。EF Core 查询示例：
```csharp
// 数据库层已开启 RCSI 或会话已设为 SNAPSHOT 后，EF 查询仍需 AsNoTracking() 避免实体缓存
var orders = await _context.Orders
    .AsNoTracking()
    .Where(o => o.PlanVersionId == currentPlanVersionId
             && o.Status == "OPEN")  // CLOSED/CANCELLED 不进入排程
    .ToListAsync();
```

---

### 🛡️ 增量轮询容错机制

**异常重试**：Hangfire 自带重试策略；同步失败不影响排程主流程

**监控告警**：连续失败或水位长时间未推进时告警运维

**降级策略**：ERP 同步失败时排程主流程仍可运行，使用上一次成功批次的数据；运维修复后下一轮轮询自动追赶

---

### 📊 时间线对比

```
时间轴：
00:38 - 3号位/INightlyBatchOrchestrator 只创建 ScheduleRun 并确定 DataCutoffTime（阶段0）
00:40/00:45/00:50 - 三个独立定时任务分别调用快照 SP，并使用同一 ScheduleRunId + DataCutoffTime
00:55 - 统一管道供给与Received事实同步
01:50 - 2号位执行跨域依赖静态扫描（阶段0.5）
02:00 - 2号位读取已创建的 ScheduleRun，按 DataCutoffTime 切片读取 APS 数据库（阶段1）
02:00:01 - 1号位开始纯内存排程（阶段2-4）
02:15 - 2号位逐域批量落盘（步骤5.1，各域 PlanVersion 保持 BUILDING）
02:15:01 - 2号位逐域执行 Serializable 事务（步骤5.2）：各域独立"同DomainKey旧ACTIVE→ARCHIVED + 本次BUILDING→ACTIVE"（不置整个 ScheduleRun 状态）
02:15:02 - 2号位运行汇总（步骤5.3）：按 ExpectedDomainKeysJson 终态判定 ScheduleRun.Status = COMPLETED / PARTIAL_SUCCESS / FAILED，写入 CompletedAt；若存在失败域，依 Domain_Dependency 生成跨域风险原因事实 + RescheduleRecommendation 交 PMC
02:15:03 - 排程主流程结束

白天并行：
每小时 - ERPOrderSyncService.IncrementalSyncAsync() 将 ext_v_APS_SalesOrder 增量写入 ERP_Order_Staging
        → sp_ValidateAndPromoteOrders → Order_Canonical
白天新增订单不会自动进入本次 ScheduleRun；由 PMC 决策是否发起 Candidate 主链
```

**架构收益**：
- 时间戳增量轮询与批处理排程完全解耦；排程引擎不会被 I/O 阻塞
- 白天新增订单通过每小时轮询同步到 `Order_Canonical`，由 PMC 决策是否发起白天 Candidate 主链（禁止系统自动创建 ScheduleRun 或自动重排）
- 凌晨全量排程以 00:38 确定的 `DataCutoffTime` 为统一切片边界，保证数据一致性

---

## 附录B：架构合理性说明

（保持原有内容不变）

