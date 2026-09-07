# 白天候选排程运行 —— 提交 2号位的需求说明

> **文档性质**：3号位 → 2号位 正式需求提报（跨号位协作）
> **版本**：v1.2（修订：2号位 状态词表回执落地——壳初始改 `Created`、候选靠 `VersionCategory` 标识；新增"七、三项交付"）
> **日期**：2026-09-02
> **状态**：已按 2号位 回执更新（事项二词表已定）；三项交付已提交（见"七"），待 2号位 动 C 线代码
> **关联文档**：`APS_V1_白天Candidate运行创建_B1契约草案_v1.0_20260902.md`（3号位工作日志目录，本说明为其面向 2号位 的需求正文）

---

## 一、背景：白天候选排程是什么

系统目前只有"夜间批排程"：每天凌晨由后台任务创建计划版本壳并自动执行。

现在要支持**白天由用户即时发起的一次"候选排程"**：

1. 用户在界面上发起一次白天候选排程，指定运行类型、用途、目标域；
2. 排程结果**先不直接生效**（不替换当前生效计划），而是生成一个"候选"结果供用户查看、对比、确认；
3. 用户确认后，候选结果才正式采用；其中"CTP/插入影响分析"类的候选结果**永远只查看、不可采用**。

**职责划分（已由 0号位 裁决）**：
- **创建白天候选运行**（建运行记录 + 建候选计划版本壳）→ **3号位** 负责，已完成；
- **执行排程主流程并收口运行状态**（RUNNING → COMPLETED/FAILED）→ **2号位** 主流程负责，**触发方式已回执选定（方案 A）**。

**白天候选的一条原则**：以当前"生效计划（ACTIVE 计划版本）"为只读基线，**不修改基线**，在基线上算一版候选。

---

## 二、3号位 已完成的创建动作（请 2号位 知悉）

新增对外接口：`POST api/Governance/run/candidate`（4号位 前端调用）。

调用后，在 **APS_Production** 库的**同一个事务**内写两张表（任一步失败整体回滚，不会留下孤立的 RUNNING 运行）：

### 2.1 表 `dbo.ScheduleRun`（新增一行，运行记录）

| 列 | 写入值 |
|---|---|
| RunType | 白天候选运行类型：`MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` / `INSERT_ORDER_WHATIF` |
| Status | `RUNNING` |
| TriggeredBy | 操作人 |
| DataCutoffTime | 数据截止时刻（调用方未传时取当前时刻） |
| **BasePlanVersionId** | 本次候选所锚定的**当前生效计划版本 Id**（只读基线） |
| StrategyProfileVersionId | 解析出的**默认生效策略包版本 Id**（需唯一，缺失/多个歧义都拒绝创建） |
| **ExpectedDomainKeysJson** | 冻结的域清单，严格单域，如 `["D1"]` |
| StartedAt / CreatedAt | 当前时刻 |

### 2.2 表 `dbo.PlanVersion`（新增一行，候选计划版本"壳"）

| 列 | 写入值 |
|---|---|
| VersionCode | `CANDIDATE_{域}_{时间}`，如 `CANDIDATE_D1_20260902153000` |
| VersionCategory | `CANDIDATE` |
| DomainKey | 目标域 |
| PlanHorizonStart / PlanHorizonEnd | **复制基线的计划窗口**（只读基线，不改基线） |
| ComputeMode | `FULL` |
| **Status** | **`Created`**（壳初始态，已按 2号位 回执统一为执行词表，见"事项二"） |
| **SourceScheduleRunId** | 2.1 中新创建的 ScheduleRun Id（两张表通过它关联） |
| CreatedBy / CreatedAt | 操作人 / 当前时刻 |

### 2.3 创建时 3号位 已做的校验

- 操作人必填；
- 运行类型必须属于白天候选类（`FULL_SCHEDULE`/`SIMULATION` 等整体排程拒绝走候选创建）；
- **用途（Purpose）× 运行类型**必须符合冻结合法组合（见"事项三"）；
- 目标域必填，且严格单域；
- 必须基于当前 ACTIVE 计划版本（基线只读）；
- 默认策略包版本必须唯一有效。

### 2.4 审计

创建动作写 Auth 库 `GovernanceAuditLog` 表（操作类型 `CreateCandidateRun`，记录运行 Id、用途、域、基线 Id、策略包版本 Id、新壳 Id）。

---

## 三、需要 2号位 确认/配合的事项

### 事项一：【最重要】白天候选运行的"触发执行方式"（已回执：选 A）

白天候选的壳建好后，**排程主流程由 2号位 执行**，并把 `ScheduleRun.Status` 从 `RUNNING` 收口为 `COMPLETED`（成功）或 `FAILED`（失败）。

3号位 目前在创建动作末尾留了调用点（注释标记），**尚未接线**——按 2号位 回执选定方案 A 后，3号位 补接。

| 方案 | 做法 | 说明 |
|---|---|---|
| **A（已选定）** | 2号位 提供**能同时收口 Run 的单域执行入口**（planVersionId + scheduleRunId + strategyProfileVersionId → ExecuteDomainAsync → 单域结束收口 Run 为 COMPLETED/FAILED），3号位 建 Run+壳后调用 | **2号位 已具备 80% 基础**：`RunSchedulingAsync(int)`（SchedulingOrchestrator.cs:196）与 `RunSchedulingAsync(int, long)`（:202）均按 Id 执行但 scheduleRunId=0 不收口 Run，底层走 `ExecuteDomainAsync`（:211，单 Domain 执行单元，明确"不碰 ScheduleRun 终态"）。**只缺"执行后收口 Run"一小段**，非从零写。接口签名改动请先更新《APS_集成接口设计》（红线 #5，接口即契约） |
| B | 2号位 提供**按需触发的后台任务**（Hangfire Enqueue），3号位 建完 Run+壳后投递，由任务异步执行并收口 | 备选（未选） |
| C | **复用**现有 `RunSchedulingAutoAsync` 自动发现逻辑 | **与白天候选壳初始状态 `BUILDING` 冲突**，不适用（见"六、出入①"） |

### 事项二：白天候选壳的初始状态（已按 2号位 回执定为 `Created`）

**已回执（2026-09-02，2号位 状态词表配合）**：

- `PlanVersion.Status` **不写** 3号位 RunLifecycle 的 `BUILDING / CANDIDATE / ACTIVE / FAILED`；
- 候选的 `Status` 走 2号位 执行词表：`Created → Computing → Computed / ComputeFailed`；
- **候选靠 `VersionCategory='CANDIDATE'` 标识**（3号位 创建时本就要写 CANDIDATE，与 2号位 一致 ✅）；
- **壳的初始 Status 用 `Created`**（方案 A 按 Id 执行、不检查初始值，采纳 `Created` 台账更干净）；
- "候选被采用→正式生效"用 `Status='ACTIVE'`（v5.1.4 已有 `UQ_PlanVersion_OneActivePerDomain` 筛选唯一索引，L3861）——**属治理动作，2号位 主流程不管，由 3号位 治理侧负责**。

> **与 2号位 回执对齐（出入③ + 本次词表回执）**：`PlanVersion.Status` 为 `NVARCHAR(50) NOT NULL` 无 CHECK（v5.1.4 DDL L3820），写任何值不报错。**词表分裂已收口**：Status 统一为 2号位 执行词表 + 治理 ACTIVE，候选语义由 `VersionCategory` 承载，不再双词表并跑。
>
> **备案 0号位**：v5.1.4 L174 变更日志曾注"Status 值域改为版本生命周期 BUILDING/CANDIDATE/ACTIVE/ARCHIVED/FAILED"，属早期设计意图；本次按 2号位 执行口径（Created/Computing/Computed/ComputeFailed + 治理 ACTIVE）+ `VersionCategory='CANDIDATE'` 标识落地，差异记录在案供 0号位 知情，不阻塞排程。

### 事项三：用途（Purpose）是否需要落库

冻结的"用途×运行类型"合法组合（示例：手动重排+手动调整、插入订单预演+CTP、插入订单预演+插入影响分析、本地重排+插入重排、本地重排+手动调整），其中 **CTP / 插入影响分析 永远不可采用（激活）**。

当前 `ScheduleRun` 表**没有"用途"列**。本轮 3号位 只在创建时校验合法组合并写审计，**不落库**。

> **与 2号位 回执对齐（出入④）**：**选项①（写入 `ScopeJson`）与冻结口径冲突，不建议**——DDL 已裁定 `ScopeJson` "不再承载 ExpectedDomainKeys，预期域集合迁移至独立字段 ExpectedDomainKeysJson"（v5.1.4 DDL L8416），且工作条目清单冻结"`ScopeJson` 保持冻结 11 字段，禁止往里加 ExpectedDomainKeys / Solver Trace / CrossDomain / 任意规则快照"。把 Purpose 落 `ScopeJson` 等于给该列找新用途，**须 0号位 对齐口径后方可**。
>
> 选项②（新增列）属 DB 结构变更（红线 #6），可提交变更申请；
> 选项③（短期不落库，仅校验+审计）**3号位 维持推荐**——与 ScopeJson 冻结口径无冲突，后续需要再定。

### 事项四：执行时以"创建时冻结的基线"为快照

3号位 创建时已校验基线计划版本存在 / ACTIVE / 域一致，并把其 Id 写入 `ScheduleRun.BasePlanVersionId`（v5.1.4 新增列，裁决 §13"1 Base ACTIVE PlanVersion"）。

> **与 2号位 回执对齐（出入④）**："执行时以创建时冻结的 `BasePlanVersionId` 为准、不许随执行时刻 ACTIVE 漂移"是对 2号位 执行侧的**新要求**——2号位 确认当前 `ExecuteDomainAsync` 无此概念。2号位 同意后需在执行入口补快照保护。

### 事项五：DDL 迁移状态确认（影响集成测试转绿）

测试库当前状态：
- `ScheduleRun.ExpectedDomainKeysJson` 列：**已迁移 ✅**；
- Auth 库 `GovernanceAuditLog` 表：**未迁移 ❌**。

> **与 2号位 回执对齐（出入④）**：2号位 确认 `GovernanceAuditLog` 未迁移，将按红线 #6 **出独立 .sql 脚本建表（不动冻结 DDL）**。建表后集成测试自动转绿，无需改测试代码。

### 附：3号位 新增/投影的列（请 2号位 留意）

- 创建路径会写：`ScheduleRun.BasePlanVersionId`、`ScheduleRun.ExpectedDomainKeysJson`；
- 只读查询（按 Id 查单条、列表查询）已投影：`BasePlanVersionId`、`ScopeJson`。

2号位 主流程若读取或写这些列，请留意兼容。

---

## 四、回执表（2号位 回执记录）

| 事项 | 3号位 建议 | 2号位 回执 |
|---|---|---|
| 一 触发执行方式 | 方案 A（显式按 Id 执行并收口 Run） | **A**（已选；2号位 已有 80% 基础，只缺收口 Run 小方法） |
| 二 壳初始状态 | 已按回执改 `Created`；候选靠 `VersionCategory='CANDIDATE'` 标识；词表差异备案 0号位 | **已回执（定）：Status 走执行词表 Created/Computing/Computed/ComputeFailed，不写 BUILDING/CANDIDATE/FAILED；采用时 Status='ACTIVE'（治理动作）** |
| 三 用途是否落库 | 短期不落库（仅校验+审计）；**不写 ScopeJson（冻结口径冲突）** | 认可 ScopeJson 冲突，口径对齐后再定 |
| 四 执行时基线快照保护 | 以创建时冻结的 BasePlanVersionId 为准 | 确认 ExecuteDomainAsync 无此概念，同意后补 |
| 五 `GovernanceAuditLog` 表排期 | 确认排期 | 将按红线 #6 出独立 .sql 建表 |

---

## 五、3号位 收到回执后的动作

1. **事项一**：按 2号位 选定的方案 A 补接触发调用点（不越位调用 2号位 内部方法，只调用双方约定的接口）；
2. **事项二**：已按 2号位 回执落地——壳初始 `Created`、候选靠 `VersionCategory='CANDIDATE'` 标识、采用时 `Status='ACTIVE'`；词表差异已备案（供 0号位 知情）；
3. **事项三**：按 0号位 口径确定 Purpose 是否落库（当前维持 ③ 不落库）；
4. **事项四**：按 2号位 结论补充执行侧基线快照保护；
5. **事项五**：集成测试随 2号位 DDL 落地自动转绿，无需 3号位 动作。

---

## 六、与 2号位 回执的出入点对齐（v1.1 新增）

2号位 就 v1.0 提出 4 个出入点，3号位 已全部对照代码核实，结论一致：

| # | 2号位 出入点 | 3号位 核实结论 |
|---|---|---|
| 出入① | v1.0 事项一方案 C 描述过时：`RunSchedulingAutoAsync` 已重写为 **ScheduleRun 级领取**（`WHERE sr.Status='RUNNING' AND EXISTS(pv.Status='Created')`，SchedulingOrchestrator.cs:58-67），不是"直接领壳"；L68 行号也不符（实为 L69 `if (scheduleRun == null)`）。但**核心结论仍成立**：auto 只认 `Created` 壳，Candidate 壳 `BUILDING` 不会被领取 | ✅ 属实。已修正 v1.1 事项一方案 C 描述。**连带隐患属实**：auto 领取 SQL 无 RunType 过滤，夜间 Run 为 `FULL_SCHEDULE`（ScheduleRunService.cs:65），Candidate Run 亦为 `RUNNING`——**唯一挡住 auto 抢走 Candidate 的是"壳是 BUILDING 非 Created"，属脆弱耦合**。2号位 建议给 auto 加 `AND sr.RunType='FULL_SCHEDULE'` 防御（一行 SQL，双保险），**3号位 同意并采纳** |
| 出入② | 方案 A 2号位 已有 80%：`RunSchedulingAsync(int)`(:196) / `RunSchedulingAsync(int,long)`(:202) 均走 `ExecuteDomainAsync`(:211，单 Domain 不碰 Run 终态)；只缺"收口 Run 的单域入口"，非从零写。回执选 A | ✅ 属实。v1.1 事项一已按此修正（方案 A 标注"2号位 已具备 80% 基础"） |
| 出入③ | `PlanVersion.Status` 为 `NVARCHAR(50) NOT NULL` **无 CHECK 约束**（v5.1.4 DDL L3820），BUILDING 不会报错但也非"值域里有它"；真正问题是**两套状态词表并存在跑**（2号位 Created→Computing→Computed/ComputeFailed；3号位 BUILDING/CANDIDATE/ACTIVE/FAILED），Candidate 壳执行后终态会打架，**需 0号位 拍 Candidate 状态机** | ✅ 属实。v1.0 原文"该值在冻结 DDL 的状态值域内"表述不准确，已修正（v1.1 事项二）。词表分裂问题**同意点名，3号位 将汇总提交 0号位** |
| 出入④ | 新事实：RunType/Purpose 概念（CTP 类永不可采用）；`ScopeJson` 已裁定不再承载 Domain 集合（由 `ExpectedDomainKeysJson` 取代），Purpose 落 ScopeJson 选项① 须 0号位 对齐；`BasePlanVersionId` 为新列，执行侧快照保护是新要求；`GovernanceAuditLog` 表 2号位 将独立建表 | ✅ 属实。`ScopeJson` 冻结口径已实证（v5.1.4 DDL L8416 + 工作条目清单"冻结 11 字段禁止加任意规则快照"），v1.1 事项三已强化"不写 ScopeJson"建议。`BasePlanVersionId`（裁决 §13）与 `GovernanceAuditLog` 建表事项已同步进 v1.1 事项四/五 |

> 若 2号位 对以上任何一项仍有异议或需 0号位 介入裁定（尤其事项三 ScopeJson 新用途），请在回执中注明"需 0号位 裁定"，3号位 汇总后提交 0号位。

---

## 七、2号位 等待的"三项交付"（v1.2 新增）

2号位 2026-09-02 回复：收到**方案 A 方法签名、事项五 GovernanceAuditLog 表结构、事项四基线快照**后动 C 线代码。以下为三项交付。

### 7.1 方案 A 方法签名（红线 #5：2号位 先更新《APS_集成接口设计》再实现）

现有 `ISchedulingOrchestrator`（Core/Interfaces/ISchedulingOrchestrator.cs）：

- `RunSchedulingAsync(int planVersionId, ct)`：调 `ExecuteDomainAsync(…, scheduleRunId:0, …)`，**不收口 Run**（SchedulingOrchestrator.cs:198-199）；
- `RunSchedulingAutoAsync(ct)`：自动领取 + 统一收口（夜间路径，不动）。

**方案 A 提案**——`ISchedulingOrchestrator` 新增方法（白天候选专用，独立于夜间自动路径）：

```csharp
/// 单域执行并收口 Run（白天候选：建 Run+壳后由 3号位 调用）
/// 语义：RUNNING → COMPLETED（IsSuccess=true）/ FAILED（IsSuccess=false，ErrorMessage 落 Run）
/// 红线 #5：本签名先同步更新《APS_集成接口设计》，2号位 确认后实现
Task<SchedulingRunResult> RunSchedulingAndFinalizeAsync(
    int planVersionId,
    int scheduleRunId,
    long strategyProfileVersionId,
    CancellationToken cancellationToken = default);
```

建议实现：`ExecuteDomainAsync(planVersionId, scheduleRunId, dataCutoffTime: null, strategyProfileVersionId)` → 按返回 `IsSuccess` 调 `ScheduleRunService.CompleteAsync / FailAsync`（Engine 已有，ScheduleRunService.cs:114-158）。

3号位 侧：创建动作末尾注释标记的调用点在签名落定后接线（不越位调用 2号位 内部方法，只调接口）。

### 7.2 事项五：GovernanceAuditLog 表结构（2号位 按红线 #6 独立建表）

表结构源自 3号位 实体 `Core/Entities/Auth/GovernanceAuditLog.cs` + EF 配置 `AuthDbContext.cs:131-141`（Auth 库，EF Core 使用）：

| 列 | 类型 | 说明 |
|---|---|---|
| Id | BIGINT IDENTITY PRIMARY KEY | 主键 |
| OperationType | NVARCHAR(50) NOT NULL | Publish / Disable / Archive / CreateCandidateRun / … |
| EntityType | NVARCHAR(50) NOT NULL | RuleSetVersion / ParameterSetVersion / StrategyProfileVersion / ScheduleRun / … |
| EntityId | BIGINT NOT NULL | 实体 Id |
| VersionCode | NVARCHAR(100) NULL | 实体版本号 |
| BeforeStatus | NVARCHAR(MAX) NULL | 操作前状态 |
| AfterStatus | NVARCHAR(MAX) NULL | 操作后状态 |
| OperatedBy | NVARCHAR(MAX) NULL | 操作人 |
| OperatedAt | DATETIME2 NOT NULL | 操作时间 |
| Remarks | NVARCHAR(MAX) NULL | 备注（原因、审批单号等） |

索引（EF 配置）：`IX_GovernanceAuditLog_Entity(EntityType, EntityId)`；`IX_GovernanceAuditLog_OperatedAt(OperatedAt)`。

建表后：集成测试（RunLifecycleServiceIntegrationTests / GovernanceVersionServiceIntegrationTests 的 GovernanceAuditLog skip 项）自动转绿，无需改测试代码。

### 7.3 事项四：执行侧基线快照保护（3号位 建议）

- **语义**：白天候选以**创建时冻结的 `ScheduleRun.BasePlanVersionId`** 为只读基线；执行时**不许随执行时刻"当前 ACTIVE"漂移**。
- **现状**：`ExecuteDomainAsync` 只读自身 PlanVersion（SchedulingOrchestrator.cs:227-230），无基线概念；沙盘装载 `LoadSchedulingContextAsync` 读本版本数据。
- **建议**：方案 A 入口内按 `scheduleRunId` 反查 `BasePlanVersionId`；候选场景下沙盘数据源（Order/资源/库存）切到基线版本（**只读，不改写基线**），候选壳仅承载结果。基线版本本身不得被修改（只读红线）。
- 集成测试在 2号位 落地后由 3号位 补基线快照断言用例。

### 7.4 状态词表配合（2号位 已写进回执，3号位 确认采纳）

- `PlanVersion.Status` 候选走执行词表 `Created/Computing/Computed/ComputeFailed`，**不写** BUILDING/CANDIDATE/FAILED；
- 候选靠 `VersionCategory='CANDIDATE'` 标识（一致 ✅）；
- 壳初始 `Status='Created'`（采纳，台账更干净）；
- 采用生效 `Status='ACTIVE'`（治理动作，3号位 治理侧负责，2号位 主流程不管）。
