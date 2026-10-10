# APS V1 跨版本排程连续性各号位复核 —— 3号位 答复

**版本**：v2.0
**日期**：2026-09-11（北京时间）
**致**：0号位
**性质**：对《APS V1 跨版本排程连续性_各号位复核与冻结文档修改指导 v2.0 20260910 待确认版》§二十一 Q1~Q4 的 3号位 复核答复。回答均在当前代码落地状态上核实，非仅文档口径。

---

## 一、总体意见

原则同意 v2.0 待确认版对 3号位 的职责收口（§十四、§二十一）。本答复对 3号位 **无既有代码冲突**，且四项质疑的落点均与 3号位 已落地的版本治理底座**天然契合**。以下分四部分：代码现状核对（答复依据）、对 Q1~Q4 的正式答复、3号位 最小需改清单、跨号位留口与待 0号位 裁决项。

## 二、代码现状核对（答复依据）

| 事实 | 代码证据 |
|---|---|
| 冻结天数（FrozenDays）/ 锁定天数（FirmDays）**零实现** | 全仓 C# 检索零匹配 |
| 围栏配置（FenceConfig）**无 C# 实体** | 检索 `class FenceConfig` 零匹配；仅存在于冻结数据定义语言（DDL）文档，代码内无实体 |
| 版本治理三件套**已完整落地** | 参数集版本（ParameterSetVersion）、规则集版本（RuleSetVersion）——均带内容快照 JSON（ContentSnapshotJson，即 0号位 方案 A 落点）；策略包版本（StrategyProfileVersion）——组合规则集版本编号、参数集版本编号 |
| 冻结快照**已是六块结构 + 按运行冻结** | 冻结策略快照（FrozenStrategySnapshot）：六块为需求排序（DemandPriority）/ 锁定（Lock）/ 供给（Supply）/ 采购（Procurement）/ 求解策略（SolverStrategy）/ 候选护栏（CandidateGuardrail）；冻结策略快照提供者（FrozenStrategySnapshotProvider）：按策略包版本编号一次装载、运行内不刷新、多域（Domain）同份、进程级缓存 |

## 三、对 §二十一 Q1~Q4 的正式答复

### Q1 —— 冻结天数（FrozenDays）/ 锁定天数（FirmDays）纳入参数集版本（ParameterSetVersion）→ 策略包版本（StrategyProfileVersion）→ 冻结策略快照（FrozenStrategySnapshot），不新建围栏（Fence）专属版本平台？

**答复：同意，且这是唯一最小正确落点。**

代码内无围栏配置（FenceConfig）实体，不存在"要不要新建第二套版本平台"的两难——现有参数集版本（ParameterSetVersion，方案 A 的内容快照 JSON 通用子块）→ 策略包版本（StrategyProfileVersion）→ 冻结策略快照（FrozenStrategySnapshot）三件套**天然就是落点**。

最小增量 = 给冻结策略快照（FrozenStrategySnapshot）增加**第⑦块围栏块（FenceBlock，含冻结天数 / 锁定天数）**，并在参数集版本（ParameterSetVersion）内容快照 JSON 增加「围栏（Fence）」子块 + 发布装配。与现有六块**完全同构**（冻结策略快照提供者现成的 `DeserializeParameterSetBlock<TBlock>` 直接套用）。

结论：**零新平台、零新表**（数据定义语言 DDL 变更申请由 2号位 行事），不新增围栏配置版本（FenceConfigVersion）/ 围栏快照（FenceSnapshot）第二套治理服务。

> 说明：v2.0 §十四.2 提示"现有围栏配置（FenceConfig）代码如仍使用可先兼容保留"。经核对**代码内本无围栏配置实体**，故 3号位 侧无任何"兼容保留"动作；数据定义语言（DDL）中围栏配置（FenceConfig）表是否保留，由 2号位（数据定义语言专属）按变更申请裁定，不在 3号位 范围。

### Q2 —— 一次排程运行（ScheduleRun）冻结一套围栏（Fence）参数，是否符合 3号位 当前治理结构？

**答复：完全符合，治理结构零改动。**

冻结策略快照提供者（FrozenStrategySnapshotProvider）现有语义即本条规则本身：

- 按策略包版本编号（StrategyProfileVersionId）一次装载 → **本运行内存使用，不逐笔调用**；
- 缓存键 = 版本编号，**运行内不刷新**、跨运行复用不可变快照；
- 契约 C2-1/C2-2 明确 **"本运行所有域（Domain）使用同一份快照"**。

围栏参数随第⑦块进入同一份快照后，**按排程运行（ScheduleRun）冻结的语义自动继承，无需新建任何"每运行冻结"机制**。§14.3 的"同一排程运行下多个域的计划版本（PlanVersion）用同一套冻结参数"与现有实现一致。

### Q3 —— 锁定（Firm）重开继续采用：最小人工授权/确认 + 审计，不建设完整办公自动化（OA）审批，是否无冲突？

**答复：无冲突，且基础件已就位。**

- **候选（Candidate）/ 重排链已存在**：运行生命周期服务（RunLifecycleService）的 `CreateCandidateRunAsync` + 运行类型（RunType，含局部重排 LOCAL_RESCHEDULE / 人工重排 MANUAL_RESCHEDULE / 插单 WHAT-IF 类）+ 审计动作码（ActionCode = CreateCandidateRun）。
- **权限码已覆盖**：`aps.reschedule.local`（局部重排）、`aps.reschedule.manual`（人工重排）、`aps.candidate.*`、`aps.plan.run`。
- **审计底座已落地**：审计日志（AuditLog）实体 + `aps.audit.view` 权限码。

唯一**最小待定项（非阻塞）**：若业务要求"重开锁定（Firm）"与"通用人工重排"分开授权，则需新增一枚专用权限码（如 `aps.firm.reopen`）+ 种子，挂现有基于角色的访问控制（RBAC）；否则可先复用 `aps.reschedule.manual`。是否上完整办公自动化（OA）审批会撞上冻结数据定义语言（DDL）v5.1.2 起"最小人工授权、不强制 OA"的口径——**不建议上 OA**。

### Q4 —— 3号位 是否确认：围栏（Fence）运行时判定、制造执行系统（MES）连续性资源选择、任务号（TaskNo）生命周期均不属于 3号位 执行职责？

**答复：确认归位。**

代码上 3号位（核心层 Core / 应用层 Application 治理侧）没有任何围栏（Fence）运行时判定、制造执行系统（MES）连续性资源、任务号（TaskNo）生命周期的执行实现。三者执行职责分属：2号位（围栏运行时判定 + 任务号生命周期 + 数量闭合）、1号位（求解器 Solver 消费资源连续性偏好）、5号位（制造执行系统事实标准化）。

**3号位 只承接：围栏参数（冻结天数 / 锁定天数）版本治理 + 锁定（Firm）重开授权链/审计。**

## 四、3号位 最小需改清单（全部落在 3号位 自有边界内，不越位）

| # | 变更 | 归属 |
|---|---|---|
| 1 | 冻结策略快照（FrozenStrategySnapshot）增围栏块（FenceBlock，含冻结天数/锁定天数） | 3号位 |
| 2 | 参数集版本（ParameterSetVersion）增围栏 JSON 中间态 + 内容快照 JSON 增「围栏」子块 | 3号位 |
| 3 | 冻结策略快照提供者（FrozenStrategySnapshotProvider）反序列化第⑦块 | 3号位 |
| 4 | 治理版本服务（GovernanceVersionService）发布装配 + 求解策略校验器（SolverStrategyValidator）同类的围栏参数校验器 | 3号位 |
| 5 | 单元测试 / 集成测试对齐（六块同构） | 3号位 |
| 6 | 若第 3 问需专用授权点 → 新增 `aps.firm.reopen` 权限码 + 种子 | 3号位（待 0号位 确认是否需独立码） |

## 五、跨号位留口与待 0号位 裁决项

1. **字段口径对齐（留口给 2号位）**：围栏块（FenceBlock）字段命名须与 2号位 冻结事实参数（FrozenFactParameters）消费侧口径对齐（冻结天数 / 锁定天数命名一致即可）。3号位 在定义第⑦块前会与 2号位 对一次字段名，避免改签接口契约。
2. **锁定（Firm）重开授权口径（待 0号位 裁决）**：是否需为"重开锁定（Firm）"增设独立权限码 `aps.firm.reopen`，还是复用既有 `aps.reschedule.manual`（人工重排）。默认建议复用，保持最小。

## 六、红线声明

3号位 承诺：本答复涉及的落地仅限「围栏参数（冻结天数 / 锁定天数）版本治理 + 锁定（Firm）重开授权链/审计」两件治理事项；不触碰围栏（Fence）运行时判定、任务号（TaskNo）生命周期、制造执行系统（MES）连续性资源选择、执行数量闭合、供需承接（Pegging）、求解器（Solver）等 1/2/5号位 主链；不重新打开已冻结的生产指示位置（PI Position）、下道工序上下文（NextOperationContext）、逻辑生产需求（LogicalProductionDemand）语义。

---

以上。请 0号位 就 §五 两处（字段口径对齐 + 锁定重开授权口径）收口；收口确认后 3号位 再按冻结口径排期落地第⑦块围栏参数治理。