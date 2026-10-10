# APS V1 用户输出专项 — 代码整改项评估报告

**版本**：v1.0
**日期**：2026-09-20（北京时间）
**编制**：3号位（只读评估，未修改任何代码）
**评估对象**：《APS V1 用户输出能力冻结文档修改指导 v1.0 最终收口版》§8「当前代码必须整改的具体漏洞」全部 14 条（P0-OUT-01~10、P1-OUT-01~04）
**评估方法**：4 个只读并行侦查代理，全仓（`*.cs` + `*.sql`）核对，以「当前工作树」为准（非文档所称基线 commit `6a681a08`，两者之间已存在大量未提交改动）
**语言规范**：中文业务解释为主，必要英文术语放在中文后的括号中

---

# 0. 结论速览

| 序号 | 编号 | 一句话主题 | 判定 | 主要号位 |
|---|---|---|---|---|
| 1 | P0-OUT-01 | Candidate Diff 重复实现 | ✅ 属实未修 | 3号位 + 5号位 |
| 2 | P0-OUT-02 | 订单交付摘要误用 `Order.DelayStatus` | ✅ 属实未修 | 5号位 |
| 3 | P0-OUT-03 | `DelayStatus='RISK'` 混入冻结值域 | ✅ 属实未修 | 5号位 |
| 4 | P0-OUT-04 | `Order.cs` 实体与 DDL/DTO 字段漂移 | ✅ 属实未修 | 2号位(DDL/实体) + 5号位(查询) |
| 5 | P0-OUT-05 | Resource Load 假「负荷」 | ✅ 属实未修 | 5号位 |
| 6 | P0-OUT-06 | 三张 Summary 只有 DDL 无应用链路 | ✅ 属实（且不应补） | ——（缺位，应属 2号位） |
| 7 | P0-OUT-07 | Candidate Diff「新增延期」按 Task 比、非订单级 | ✅ 属实未修 | 3号位（源实现） |
| 8 | P0-OUT-08 | `DelayedTasks`/`DelayedOrders` 语义 | ⚠️ 部分到位 | 3号位 |
| 9 | P0-OUT-09 | Explanation 事实完整性 | ⚠️ 部分到位 | 1/3/5号位 |
| 10 | P0-OUT-10 | Version Trace 信息不足 | ✅ 属实（BasePlanVersionId 已点滴补） | 3号位 |
| 11 | P1-OUT-01 | Setup KPI 未形成正式查询 | ✅ 属实缺位 | 5号位 |
| 12 | P1-OUT-02 | 执行偏差风险无统一模型 | ✅ 属实缺位 | 5号位 |
| 13 | P1-OUT-03 | Overview `RiskCount` 旧口径 | ✅ 属实未修 | 5号位 |
| 14 | P1-OUT-04 | `Bottleneck` 命名未收口 | ✅ 属实未修 | 5号位 |

**总判定**：14 项中 **0 项已收口修复**；12 项「属实未修/缺位」、2 项「部分到位」。文档 §8 的全部缺陷描述与当前工作树**基本吻合**，未发现「文档过时到结论性错误」的情况，仅有几处「工作树已先于文档改善」或「层归属与文档所述不符」的细节出入（见 §5）。

---

# 1. 分项详述

## 1.1 P0-OUT-01 — Candidate Diff 重复实现 ✅ 属实未修

**文档声称**：3号位 `ScheduleQueryService.GetCandidateComparisonAsync()` 与 5号位 `OverviewQueryRepository.ComputeCandidateComparisonAsync()`（注释自称移植自 3号位）构成两套同一业务 Diff 算法。

**当前真相**：属实，且更糟——5号位是**自算一套**，未调用 3号位。

- 3号位实现：`LPS.APS.Application\Services\Query\ScheduleQueryService.cs:246-401`（含 404/400 与域越权校验，返回完整 `CandidateComparisonDto`）。
- 5号位实现：`LPS.APS.BusinessRules\Repositories\OverviewQueryRepository.cs:178-268`（`private`，注释 :177 明写「从 ScheduleQueryService 差集逻辑移植」），由 `GetCandidateSummaryAsync()`（:139）调用。
- 两处对同一批 6 指标（added / removed / timeShifted / resourceChanged / impactedOrders / newDelays）使用**逐字相同 SQL**，主键片段同为 `OrderId+MaterialId+OperationSeq+OperationCode+RouteCode+PathId`（3号位 :404-410 vs 5号位 :182-188）。
- 5号位 `OverviewQueryRepository` 构造函数（:13-18）仅注入 `DatabaseConnectionManager`，**无 `IScheduleQueryService` 引用**，SQL 全在本私有方法内自算；仅额外多算 `AvgDeltaHours`（平均完工时间，:269-279）。

**号位**：3号位 + 5号位（双向重复）。按文档整改方向「只保留 3号位」——3号位侧**无需改实现**，是保留方；5号位侧应删除副本或改为轻量组合调用 3号位（归 5号位，3号位不协调）。

## 1.2 P0-OUT-02 — 订单交付摘要误用 `Order.DelayStatus` ✅ 属实未修

**证据**：`LPS.APS.BusinessRules\Repositories\OrderQueryRepository.cs:201-231` 的 `GetSummaryAsync()` 100% 基于 `[Order]` 表持久化 `DelayStatus` 列做 CASE 聚合，未 JOIN 任何 PlanVersion/Task 排程结果表：

```sql
SUM(CASE WHEN o.DelayStatus IS NULL OR o.DelayStatus = 'ON_TIME' THEN 1 ELSE 0 END) AS OnTimeCount,
SUM(CASE WHEN o.DelayStatus IN ('FIRST_DELAY','REPEATED_DELAY') THEN 1 ELSE 0 END) AS DelayedCount,
SUM(CASE WHEN o.DelayStatus = 'RISK' THEN 1 ELSE 0 END) AS RiskCount,
SUM(CASE WHEN o.Status = 'UNSCHEDULED' THEN 1 ELSE 0 END) AS UnscheduledCount
```

调用链：`OrderQueryController.GetSummary`(:127-141) → `OrderQueryService.GetSummaryAsync`(:65-74) → `OrderQueryRepository.GetSummaryAsync`。`DelayStatus` 是 `sp_ValidateAndPromoteOrders` 推导的同步/透传字段，非当前 PlanVersion 的 APS 计划交付状态——文档担心成立。

**号位**：5号位（`OrderQueryRepository` / `OrderQueryService` / `IOrderQueryRepository` / `OrderQueryController`）。

## 1.3 P0-OUT-03 — `DelayStatus='RISK'` 混入冻结值域 ✅ 属实未修

**证据**：`'RISK'` 作为统计桶在 2 处 `.cs` 出现：

- `OrderQueryRepository.cs:212` `SUM(CASE WHEN o.DelayStatus = 'RISK' ...)`
- `LPS.APS.Core\Dto\OrderSummaryDto.cs:20` `public int RiskCount { get; init; }`（注释「DelayStatus = RISK」）

而 DDL 冻结值域为 `ON_TIME / FIRST_DELAY / REPEATED_DELAY`；SQL 推导逻辑 `sp_ValidateAndPromoteOrders`（DDL v5.1.7 :7870-7883）只产 `FIRST_DELAY` 与 `ON_TIME`，注释明写「REPEATED_DELAY 需历史延迟追踪，V2 实现」——**根本不产 `RISK`**。故 `RISK` 是代码自造、游离于冻结值域之外，`RiskCount` 在真实数据下恒为 0 但语义冲突。

**号位**：5号位。

## 1.4 P0-OUT-04 — `Order.cs` 实体与 DDL/DTO 漂移 ✅ 属实未修

三个子项逐一坐实：

1. **实体缺列**：`LPS.APS.Core\Entities\Aps\Order.cs`（:1-64）无 `DelayStatus`，仅有同期 `DemandMaturityStatus`(:40)/`CustomerTier`(:45)。而 DDL v5.1.7 `[Order]` 表（:4034-4035）确有 `DelayStatus NVARCHAR(20) NULL`。同病：`OrderCanonical.cs` 亦缺该列（DDL `Order_Canonical` :3971 有）；唯一对齐的是 `ERPOrderStaging.cs:39`。
2. **查询按列名直读**：`OrderQueryRepository.cs` 在 :55 / :68（过滤）/ :123 / :210-212 直接 `SELECT o.DelayStatus`，映射到 `OrderQueryDtos.cs:26` `string? DelayStatus`——Dapper 手写 SQL 不依赖实体属性，形成「SQL 用了、实体没有」的隐性约定漂移。
3. **语义不一致**：`OrderSummaryDto.cs:17` 注释写「DelayStatus = DELAYED」，但实际 SQL(:211) 用 `IN ('FIRST_DELAY','REPEATED_DELAY')`；`DELAYED` 是旧口径，仅在 DemandPriority 域残留。

**关键旁证——跨号位认知分裂**：`LPS.APS.Application\Services\PeggingOrchestrator.cs:3694`（3号位）注释写「`DelayStatus / ProtectionStatus：Order 表暂无对应列，保持 null（不造假）`」——与 DDL（有列）、5号位查询（有 SELECT）**三方矛盾**，说明各号位对 `[Order].DelayStatus` 是否存在认知不一致，需 2号位 DDL 权威一次性对齐。

**号位**：`Order.cs`/`OrderCanonical.cs`/`ERPOrderStaging.cs` 为 Core 领域实体（镜像 DDL，DB 结构变更受 DB 红线 #6 约束，归 **2号位** Schema 权威 + 变更申请）；`OrderQueryRepository`/`OrderQueryDtos`/`OrderSummaryDto` 为 **5号位** 查询侧。

## 1.5 P0-OUT-05 — Resource Load 假「负荷」 ✅ 属实未修

**证据**：`LPS.APS.BusinessRules\Repositories\OverviewQueryRepository.cs:76-92` 的 `GetResourceBottleneckAsync()`：

```sql
COUNT(*) AS TaskCount,
SUM(ISNULL(t.Duration,0))/3600.0 AS TotalPlannedHours,
NULL AS UtilizationRate,        -- 假负荷
0 AS IsBottleneck              -- 恒 0
```

DTO `LPS.APS.Core\Dto\OverviewDtos.cs:90-96` 仅 `TaskCount/TotalPlannedHours/UtilizationRate(decimal?)/IsBottleneck(bool)`，无 Calendar 可用时间、无独立「计划占用时间」、无 LoadRate。

**重要补充（工作树已先动）**：`git diff` 显示 HEAD 曾有一段「假算」——`UtilizationRate = SUM(Duration)/(7.0*24*3600)`（注释「假设7天可用工时」）并把排序首条标 `IsBottleneck=true`；**工作树已删除该假算**，改为显式 `NULL/0` + 注释「判定应由 2号位正式 Query 提供」。即：工作树**消除了假数据，但未实现真负荷**，属「诚实化」而非「完成整改」。

**号位**：5号位。

## 1.6 P0-OUT-06 — 三张 Summary 只有 DDL 无应用链路 ✅ 属实（且不应补）

全仓 `.cs`：`OrderScheduleSummary|ResourceLoadSummary|PlanKpiSummary` 及其去前缀变体均**无命中**（唯一 `ResourceLoadDto` 是 `SchedulingOrchestrator.cs:576,1034` 加载资源的私有临时 DTO，非读模型）。

**结论**：三张表（订单/资源负荷/计划 KPI）当前确无 Entity / Repository / Generator / RefreshService / Orchestrator 刷新调用 / Query 消费代码，仅存在于 DDL。与文档 §5「降级为可选性能缓存、不补 2号位生成链」的最终定位**一致**——本项是「确认无需整改」项，非「待修复」项。

## 1.7 P0-OUT-07 — Candidate Diff「新增延期」按 Task 比、非订单级 ✅ 属实未修

**证据**：3号位 `newDelays` SQL（`ScheduleQueryService.cs:362-378`）：

```sql
WHERE ... AND t_c.PlannedEndTime > o.CustomerDueDate   -- 逐 Task 比较，无 GROUP BY OrderId、无订单级 MAX
```

判定条件是逐 Task 的 `PlannedEndTime > CustomerDueDate`，**无订单级 `MAX(PlannedEndTime)`**，故「一个订单多 Task 时中间 Task 超交期被误判为订单新增延期」风险成立。5号位副本（`OverviewQueryRepository.cs:237-249`）同口径。DTO 注释自证为 Task 口径（`CandidateComparisonDto.cs:53-54`，「未延期的任务数」）。

**号位**：3号位（源实现）+ 5号位（移植副本同口径）。按「只保留 3号位」方向，源实现归 3号位修。

## 1.8 P0-OUT-08 — `DelayedTasks`/`DelayedOrders` 语义 ⚠️ 部分到位

**证据**（`ScheduleQueryService.cs`；DTO `ScheduleSummaryDto.cs:15/18`）：

- `DelayedTasks`(:215-221) = 「结束晚于订单交期的**任务数**」——符合「技术统计」定位 ✅
- `DelayedOrders`(:225-231) = `COUNT(DISTINCT o.Id) WHERE t.PlannedEndTime > o.CustomerDueDate` —— 是「**至少存在一个**晚交 Task 的去重订单数」，**并非按订单最终完成时间（`MAX`）判定** ❌

即当前两字段的语义区分是「任务计数 vs 去重订单计数（任意 Task 晚交即算）」，而非文档要求的「`DelayedOrders` 按订单最终完成时间」。中间工序滑期但末序追回、最终仍准时的订单会被误计为延期订单——**未修复到位**。

**号位**：3号位。

## 1.9 P0-OUT-09 — Explanation 事实完整性 ⚠️ 部分到位

三问逐一核：

- ① 实体 + 落库链路：**已完备**。实体 `ScheduleExplanationFact`（`Core\Dto\DomainSolveResult.cs:117-129`，含 `ReasonCode`）；生成（1号位 `Scheduling\Solvers\PhaseThreeDiagnostics.cs:67-77` 延期诊断、:142-151 瓶颈根因码）；落库（`PeggingOrchestrator.cs:614-656` INSERT，另 :696 Unscheduled、:739 Over-Commit）；查询（5号位 `ExplanationQueryRepository`/`ExplanationQueryService`/`Web\Controllers\ExplanationController`）。
- ② 「DELAYED 缺 Explanation 自动记数据完整性 Issue」：**缺失**。全仓无该检查；`BusinessFactIssueService/Repository` 只聚合两类 ODS 事实 Issue（`BusinessFactIssueRepository.cs:49/105`），与 Explanation 完整性无关。
- ③ 5号位自行猜补 ReasonCode：**无（已满足）**。`ReasonCode` 在 `LPS.APS.BusinessRules/**` 下均为只读查询/分组，无赋值；真源为 1号位 `DiagnoseDelayReason` 与 3号位 Unscheduled/Over-Commit 记账。

**⚠️ 层归属偏差（需上游注意）**：文档 §3.2 表述为「1号位产生 → **2号位持久化** → 5号位查询」，但当前落库实现是 **3号位 `PeggingOrchestrator` 直接 `ExecuteAsync INSERT`**，并非 2号位。这是文档口径与代码的一处偏差（非本次整改判定项，仅提示）。

**号位**：1/3/5号位链路已通；唯一缺口「DELAYED 缺 Explanation 自动完整性告警」待明确归属（涉及 3号位/5号位边界，需 0号位裁决）。

## 1.10 P0-OUT-10 — Version Trace 信息不足 ✅ 属实（BasePlanVersionId 已点滴补）

`GetVersionsAsync()` 投影 `ScheduleQueryService.cs:44-63`；DTO `PlanVersionSummaryDto.cs:6-29`。

**已有**：Id / VersionCode / VersionCategory / Status / CreatedAt / ComputedAt / ActivatedAt / PlanHorizonStart/End / DomainKey / TotalTasks；`SourceScheduleRunId`（仅 ID，无 Run 详情/Trigger/RunType）；`BasePlanVersionId`（:51-61，已随 v1.2 §四 G9 补入）；`ErrorMessage`（DTO 有但 SQL 未投影，恒 null）。

**仍缺**（文档列举项逐一）：`DataCutoffTime` ❌、`Trigger` ❌、`CreatedBy/CreatedByUserName` ❌（注意：5号位 `CandidateBriefDto.CreatedByUserName` 有，但 3号位版本查询 DTO 无）、`BasePlanVersion` 的 `VersionCode/Category`（仅 ID）⚠️、`Rule/Parameter/Strategy` 版本 ❌、`Candidate 输入摘要` ❌、`VersionDiff` ❌。

**结论**：文档「缺 BasePlanVersion」这条已过时（`BasePlanVersionId` 已补），其余全部仍缺失。

**号位**：3号位。

## 1.11 P1-OUT-01 — Setup KPI 未形成正式查询 ✅ 属实缺位

`SetupCount`/`SetupDuration`/`AvgSetupDuration`/`TotalSetupDuration` 全仓 `.cs` **零命中**。`SetupMinutes` 全部命中均属换型规则配置（`SetupTransitionRule.cs:43`、`SetupRuleService.cs`、`SetupTransitionRuleRepository.cs:53/80`）或 Solver 排程用（`SetupOptimizer.cs`），无统计/查询语义。`BusinessRules` 内**无任何 Setup 相关代码**。

（旁证：`FrozenStrategySnapshot.cs:233` 注释「原 DefaultSetupMinutes / SetupLookAheadSize 已于 2026-09-20 退役」——与换型旧锚退役时间线吻合。）

**号位**：5号位（缺位待建）。

## 1.12 P1-OUT-02 — 执行偏差风险无统一模型 ✅ 属实缺位

`Deviation|ExecutionRisk|DeviationRisk` 全仓 `.cs` **零命中**。唯一 `\bRisk\b` 是 `OverviewDtos.cs:56-57` 的任务状态计数桶 `RiskCount`，**非**「生产未按计划完成 / 采购超 ETA 未到 / 上游实际延期」的执行偏差统一模型。

**号位**：5号位（缺位待建，从正式事实派生）。

## 1.13 P1-OUT-03 — Overview `RiskCount` 旧口径 ✅ 属实未修

`LPS.APS.BusinessRules\Repositories\OverviewQueryRepository.cs:61`（`GetTaskSummaryAsync` 内）：

```sql
SUM(CASE WHEN Status='IN_PROGRESS' AND IsCriticalPath=1 THEN 1 ELSE 0 END) AS RiskCount,
```

DTO `OverviewDtos.cs:57` 仍为 `int RiskCount`。该行在工作树 diff 中无改动（同文件大量其它行有改动），旧口径原地保留。Critical Path ≠ 执行偏差风险，文档担心成立。

**号位**：5号位。

## 1.14 P1-OUT-04 — `Bottleneck` 命名未收口 ✅ 属实未修

全链路仍为单一名 `GetResourceBottleneckAsync`，未拆成 `GetResourceLoadAsync`，且无 `GetResourceDelayReason*`（未消费 1号位 Explanation）：

- 接口 `IOverviewQueryRepository.cs:28`
- 实现 `OverviewQueryRepository.cs:76`
- 服务 `OverviewQueryService.cs:45`
- 控制器路由 `OverviewController.cs:103` `[HttpGet("resource-bottleneck")]`（:104）
- DTO `OverviewDtos.cs:72` `OverviewResourceBottleneckDto`

全局 Grep `.cs`：`GetResourceLoad`/`GetResourceDelay` 均无匹配；唯一 `DelayReason` 在 1号位自身 `PhaseThreeDiagnostics.cs:61/162`（`DiagnoseDelayReason`）与 `Application\...\GanttDataDto.cs:156/204`（`GanttDelayReasonDto`），5号位 Overview 侧未消费。

**号位**：5号位。

---

# 2. 跨项共性发现

1. **5号位查询侧是最重灾区的重复面**：P0-OUT-02/03/05、P1-OUT-01/02/03/04 合计 7 项纯属 5号位；加上 P0-OUT-01（重复副本）、P0-OUT-04（查询直读）的 5号位分量，5号位承担了本轮绝大部分整改量。与文档 §7.12「5号位本轮新增开发职责最多」一致。
2. **`[Order].DelayStatus` 是跨号位认知分裂的核心矛盾点**：5号位直读并进 KPI（:210-212）、DDL 有列（v5.1.7 :4035）、3号位注释称「暂无对应列」（`PeggingOrchestrator.cs:3694`）——三方口径互斥，任何下游整改都会先撞上「该列到底存不存在、值域是什么、谁是权威」这一未决点。建议由 2号位 DDL 权威一次性拍板。
3. **工作树已先于文档「诚实化」但远未「实现」**：P0-OUT-05（删假算回退 NULL/0）、P0-OUT-10（补 `BasePlanVersionId`）说明 5号位/3号位已部分动作过，但都停在「不造假」而非「补真数」，14 项无一进入可宣告收口的状态。
4. **文档基线对 2 条已过时**：P0-OUT-10 的「缺 BasePlanVersion」、以及若有按旧 commit 已改的条目——评估以工作树为准，报告中已按当前真相修正。

---

# 3. 号位归属汇总（整改责任视图）

| 号位 | 涉及项 | 动作性质 |
|---|---|---|
| **3号位** | P0-OUT-07、P0-OUT-08、P0-OUT-10 | 纯修（3 项）；P0-OUT-01 为「保留方」不动实现 |
| **5号位** | P0-OUT-01(删副本)、P0-OUT-02、P0-OUT-03、P0-OUT-05、P0-OUT-04(查询侧)、P1-OUT-01、P1-OUT-02、P1-OUT-03、P1-OUT-04 | 主体（8 项分量） |
| **2号位** | P0-OUT-04（`Order.cs`/`OrderCanonical.cs` 实体镜像 + DDL 权威） | DB 红线 #6 约束，需 2号位变更申请 |
| **待裁决** | P0-OUT-09（「DELAYED 缺 Explanation 自动告警」归属 3/5 边界） | 需 0号位裁决 |
| **不需整改** | P0-OUT-06 | 确认保持「仅 DDL、不补生成链」 |

---

# 4. 评估结论（一句话）

> **文档 §8 的 14 条整改项与当前工作树高度吻合——0 项已收口，12 项属实未修/缺位、2 项部分到位；文档本身无结论性错误，仅 P0-OUT-10「缺 BasePlanVersion」一条已过时（`BasePlanVersionId` 已补）、P0-OUT-09 的「2号位持久化」表述与当前 3号位 `PeggingOrchestrator` 直接落库的实况存在层归属偏差；本轮整改的实质工作量集中在 5号位（8 项分量），3号位纯修仅 P0-OUT-07/08/10 三项，另两项跨号位（P0-OUT-04、P0-OUT-09）需 2号位 DDL 权威或 0号位裁决先行。**

---

**报告结束**