# Setup 换型主链 · 2号位 回执（合并件）—— 致 1号位 & 3号位

> 发件：2号位（数据引擎+编排）／收件：1号位（排程算法）、3号位（规则参数治理）／抄送：0号位（PM）
> 日期：2026-09-20
> 依据：3号位《待2号位协助任务清单 v1.0 20260920》＋ 1号位《询问单 v2.0 20260920》＋ PM v1.2 冻结《Setup换型规则与有限产能优化》§19.3
> 性质：一份文件同时答复 3号位 清单 7 项 + 1号位 询问单 7 项（两处同源项合并答复，不重复）

---

## 〇、一句话

主链两级（3号位 A1/A2 = 1号位 1a/1b）**当日已全部落地**，`dotnet build` 0 错、相关单测 39 条全绿；其余项逐条答复如下（二/C2 ExplainTrace 给出 A 选型 + 字段终态，待 0号位 点头后落 DTO）。

---

## 一、主链两级（3号位 A1/A2 ⇄ 1号位 1a/1b）—— ✅ 已落地

### A1 / 1a　`ISetupTransitionRuleRepository` Dapper 实现

**已闭环（预计落地时间 = 已完成）**。仓库 `LPS.APS.Engine\Repositories\Governance\SetupTransitionRuleRepository.cs` 已落（CRUD 五方法对 `dbo.SetupTransitionRule` 表，审计时间戳 `CreatedAt/UpdatedAt` 用 `ISNULL(...,SYSDATETIME())` 兜底因 3号位 service 不填），`GovernanceServiceExtensions.AddGovernanceRepositories()` 已补 `AddScoped` 注册。Web 启动 DI 验证（`ValidateOnBuild`）报错即此修复。

### A2 / 1b　第⑦块 `SetupTransitionRules` 投影进 Solver 消费入口

**已落地 + 字段终态如下**（1号位 1b 指定位置，非 `DomainSolveRequest` 顶层）：

- 装载：`PeggingOrchestrator.LoadSetupTransitionRulesAsync`（`LPS.APS.Application\Services\PeggingOrchestrator.cs:3009`）按本 Run `RuleSetVersionId` 一次性 `GetByRuleSetVersionAsync` 取表 → 筛 `IsActive=true` → 投影填 `FrozenStrategySnapshot.SetupTransitionRules`（第⑦块）。`RuleSetVersionId<=0`（fixture/未冻结）时早退留空、不查库。
- 投影 + 裁剪：新文件 `LPS.APS.Application\Models\SetupTransitionRuleProjector.cs`（纯静态）——`Project`（实体→`SetupTransitionRuleSnapshot`，EXACT/DEFAULT 按 `RuleType` 分流、From/To null 保守）＋ `CropToDomain`（按当前 Domain 的 `(ProductionDepartmentId, StageCode)` 去重集合裁剪，§19.3「只加载本 Domain」）。
- **字段终态**（`LPS.APS.Core\Dto\DomainSolveRequest.cs:133-137`，`SolverStrategySnapshot` 内，参照 ⑤⑥ 整块透传先例）：

```csharp
public sealed class SolverStrategySnapshot
{
    public long? StrategyProfileVersionId { get; init; }
    public long? ParameterSetVersionId { get; init; }
    public FiniteCapacityParameters Parameters { get; init; } = new();
    public SolverStrategyBlock SolverStrategy { get; init; } = new();        // ⑤
    public CandidateGuardrailBlock CandidateGuardrail { get; init; } = new(); // ⑥
    // ⑦ 换型规则：2号位 装载投影后按 Domain（Dept+Stage）裁剪，只传本域涉及规则
    public IReadOnlyList<SetupTransitionRuleSnapshot> SetupTransitionRules { get; init; }
        = Array.Empty<SetupTransitionRuleSnapshot>();
}
```

- 透传：`PeggingOrchestrator.cs` 组装 `StrategySnapshot` 处填 `SetupTransitionRules = SetupTransitionRuleProjector.CropToDomain(frozenSnapshot.SetupTransitionRules, materialStageDeptContexts)`。Solver 运行中不查 3号位 库（0号位 裁决项2）。
- **1号位 接线点就绪**：`SetupOptimizer.BuildRuleLookups` 已按第⑦块字段形态实现，通道落地即可接。请 1号位 按【附】接线序列开始。

**测试**：新增 `SetupTransitionRuleProjectionTests` 5 条（投影映射 / IsActive 过滤 / DEFAULT null From/To / CropToDomain 过滤 / 空上下文空）+ SolverStrategy/ParameterSet/FrozenSnapshot 相关 34 条全绿，`dotnet build` 0 错。

---

## 二、3号位 清单其余 4 项

### B1　「视图丢 vs 源缺」取证

**⏳ 非本次 M 段主链范围，未在本次落地**。3号位 静态判定已出（决定性证据在 MES_Integration 库）。2号位 供两样（`MES_APS_Routing_*_View` / `MiddleRoutingData(J)` 真实 DDL + 代跑 `MiddleRoutingData WHERE MES_ID='3231940'` 取证 SQL）需另排窗口，与 Setup 主链无耦合。**落责**按定责三分法（视图丢→3号位 / 源缺→PM 职能部门），2号位 只搬运 + 未映射日志=证据。

### C1　ScopeJsonV2 十码验收

**⏳ 待验收，非本次范围**。3号位 v2.0 附 DTO 骨架 + `BusinessTriggerType` 十码枚举送验中；2号位 验收十码后 3号位 落 `ScopeJsonV2` 实体 + `RunLifecycleService` 解析/校验（M1 消费侧开工）。

### C2 / 1号位·二　⑨ ExplainTrace 载体契约 —— **选 A**

**选 A（1号位 建议，且与 0号位「两层不混用」口径一致，不降级走 EvidenceJson）**。字段终态（`DomainSolveResult`，Core 层 2号位 辖区）：

```csharp
public sealed class SolveTraceNote
{
    public string? FinalDraftId { get; init; }     // FinalTask DraftId（可空）
    public int ResourceId { get; init; }
    public string TraceType { get; init; }          // 统一 = "TIME_CALCULATION"
    public string TraceLevel { get; init; }         // INFO / WARNING
    public string ExplanationType { get; init; }    // INITIAL_SETUP_STATE / DEFAULT_SETUP_FALLBACK / SETUP_RULE_MISSING_ZERO_FALLBACK
    public string Message { get; init; }
    public string? ContextDataJson { get; init; }   // dept/stage/operation/resource/from/to/minutes 七要素
}

// DomainSolveResult 增：
public IReadOnlyList<SolveTraceNote> SolveTraceNotes { get; init; }
    = Array.Empty<SolveTraceNote>();
```

- WARNING 级（`SETUP_RULE_MISSING_ZERO_FALLBACK`）流入 4号位「未维护 Setup 规则 / 0分钟兜底」数据质量查询（0号位 §5.3 + v1.2 §十八）。
- **落 DTO 前请 0号位 点头 A 选型**；`explanationType` 三符号按 Q4 已定两项（`INITIAL_SETUP_STATE` / `SETUP_RULE_MISSING_ZERO_FALLBACK`），第三项 DEFAULT 回退建议定名 `DEFAULT_SETUP_FALLBACK`，**三方（含 3号位）对齐词表后 2号位 即落 DTO**，1号位 接线写入、4号位 查询页按符号消费。

### D1 / 1号位·四　`SchedulingContext.SetupAttribute` 废弃处理

**✅ 已删**（按 §21「物理字段兼容废弃」取删除，1号位 已零引用）。同批清理关联死字段：`StrategyConfig.DefaultSetupMinutes` + `SetupLookAheadSize`（`LPS.APS.Core\Models\Scheduling\SchedulingContext.cs`）、`FrozenStrategySnapshot.SetupParams.DefaultSetupMinutes` + `SetupLookAheadSize`（保留 `SetupSearchBudget`/`SetupMaxNeighborhoodTries`，1号位 提值、`SolverStrategyValidator` 活读）。`ro.SetupTime` **不删**（1号位 占用时间轴活链路），已标「兼容废弃·新代码禁止使用」，真正删除等 1号位 切新模型。

### E1　`RealSchedulingIntegrationTest`（Task 空 / Pegging 未落库）

**⏳ 已定位，非本次 Setup 改动 / 非 r13371 引入**。根因=已知「阻塞 B」：6,182 自制成品里 6,175 在 ODS `MES_APS_Routing_Operation_View` 源视图无工序（非 `sp_SyncRoutingData` 重跑能补），Solver 无工序不产 FinalTask → `Task` 表空。**验收口径已改**（0号位/用户 2026-09-17 定）：用有 routing 子集先验「Task 落库 + MTS_InstructionNo 回填」链路正确，等 3号位/职能补 routing 后全量重跑。代码不改、不 hack routing 凑 Task。

---

## 三、1号位 询问单其余 2 项

### 三　ParameterSetVersion 参数投影通道 —— **知悉，暂无动作**

`SetupSearchBudget` 默认 500 [100,5000]、`SetupMaxNeighborhoodTries` 默认 50 [10,500]（量纲=次数，保可重放）已保留在 `FrozenStrategySnapshot.SetupParams`（活读）。3号位 落 ParameterSetVersion 槽位后，投影/透传环节在 2号位（与第⑦块同职责性质），届时知会 1号位 消费入口。

### 五　两个 Integration 测试

1. **`RealSchedulingIntegrationTest`**：「Task 没有数据」= 上面 E1（阻塞 B 连带），非 PeggingOrchestrator 逻辑缺陷，修复依赖 routing 补数，**补数后知会 1号位 复跑**。
2. **`ContinuityRedLineIntegrationTest` flaky**：单跑过、全量偶败，归 2号位 评估。**建议**按 Test Collection 隔离 + 落库清理（`[Task]`/`ScheduleExplanationFact` 按 PlanVersionId 前置清理）消除跨用例落库残留，2号位 排入后续；1号位 可配合复现。

### 六　真实资源日历装载现状 —— **临时 7×24，真实日历未接**

**现状=临时合成日历**：`SchedulingOrchestrator.LoadResourcesAsync` 对每个 Resource 只填单一 `TimeWindow = [PlanHorizonStart, PlanHorizonStart+730天]`（`SchedulingOrchestrator.cs:617-621`），即「整计划期单窗口、7×24 连续可用」；真实班次/节假日/维修停机**未接**（`SchedulingContext.ResourceCalendars` 注释「V1 默认 7x24」）。

- 短期仍临时日历，**1号位 按现状接线**（`BuildProductionWindows`/Phase5 压实消费 `CalendarSlots`，算法对日历形态无假设，数据升级无感切换）。
- **性能标定基准按「整计划期单窗口」口径另行说明**（0号位 Q1 风险已确认）。
- 真实日历装载时间点：随「资源日历数据源 + 合成规则」由 0号位/3号位 提供后 2号位 接 `ResourceCalendar 基础班次 + HolidayCalendar 节假日 + MaintenanceSchedule 停机 + ResourceLock` 四源合成（`SchedulingContext.cs:76` 已预留注释），当前无排期。

---

## 回执表（汇总）

| 项 | 2号位 回复 |
|---|---|
| 3号位 A1 / 1号位 1a　仓储 | ✅ 已落地（`SetupTransitionRuleRepository.cs` + DI） |
| 3号位 A2 / 1号位 1b　第⑦块投影 | ✅ 已落地，字段终态在 `SolverStrategySnapshot.SetupTransitionRules` |
| 3号位 B1　取证 | ⏳ 非本次，另排窗口（供 DDL + 代跑 SQL） |
| 3号位 C1　ScopeJsonV2 十码 | ⏳ 待 2号位 验收（3号位 送验中） |
| 3号位 C2 / 1号位 二　ExplainTrace | 选 **A**（`SolveTraceNotes` 载体），待 0号位 点头 + 三方对齐 explanationType 后落 DTO |
| 3号位 D1 / 1号位 四　SetupAttribute | ✅ 已删（+ 关联死字段；`ro.SetupTime` 标废弃不删） |
| 3号位 E1　RealScheduling Test | ⏳ 阻塞 B 连带（routing 缺），补数后复跑 |
| 1号位 三　参数投影通道 | 知悉（槽位定稿后对齐） |
| 1号位 五　Integration ×2 | Real=同上 E1；Continuity flaky=建议 Collection 隔离+清理，2号位 排后续 |
| 1号位 六　真实日历 | 现状=临时 7×24（单窗口 730 天），真实日历未接，时间点待数据源 |

（工艺约定：只引「值+符号锚」，代码引 `文件:行`。）
