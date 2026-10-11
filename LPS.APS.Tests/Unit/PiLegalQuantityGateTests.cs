using System;
using System.Collections.Generic;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// **V1_3 F-02 / F-03 判别性单测**（0号位 2026-10-10《APS_V1_3_20261010.md》§3，REWORK/BLOCKED）。
///
/// 复审判词（§7「1号位必须整改」，逐字）：
///   「F-02/F-03 在 2/5号位 供给正式事实后消费**四项 PI 限量**，核对**唯一真实 MTS PI**、
///     **单 Stage 合法自由量**、**同 PI 同 Stage 新增 Batch 累计**；
///     **不能以 PI Remaining 冒充 Stage Free**；**位置未知须显式未满足**。」
///
/// 本文件把上述四条判据**逐条**落成反证：
///   · 唯一性（0 行 = 缺失 / ≥2 行 = 来源不唯一，**不得取首条**）→ ①②③
///   · Stage 位置未知（上下文缺失 / PI·物料不匹配 / StageCode 空）→ ④⑤⑥
///   · 四项限量 min 取值的**逐项**判别（PI Original / PI Remaining / StageFree − 已占 / 配置 Max）→ ⑦⑧⑨⑩
///   · 边界相等合法、越限**不静默截断** → ⑪⑫
///   · 正常 Policy 路径同样过门禁；输入未投影则**登记缺口不拦截** → ⑬⑭
///   · B-010 兜底与 B-009 共用同一门禁（可靠 Stage 自由量 ⇒ 成立）→ ⑮
///   · 跨工序**不重复计数**（门禁消费需求总量一次）→ ⑯
///
/// 全部纯内存，不触库（用户红线：Integration 直连生产库，只跑 Unit）。
/// </summary>
public class PiLegalQuantityGateTests
{
    private const int MaterialId = 1;
    private const int FactoryId = 1;
    private const string PiNo = "PI-1";
    private const string Stage = "STAGE1";

    private static PiRemainingFact Fact(
        string piNo = PiNo, int materialId = MaterialId,
        decimal original = 100m, decimal remaining = 100m, int factoryId = FactoryId,
        string orderType = "")
        => new()
        {
            ProductionInstructionNo = piNo,
            MaterialId = materialId,
            FactoryId = factoryId,
            OrderType = orderType,
            PiQuantity = original,
            PiReceivedQty = original - remaining,
            PiRemainingQty = remaining
        };

    /// <summary>
    /// V1_4 NEW-03：门禁签名的**夹具适配层**（补默认 `factoryId` / 目标 `StageCode`）。
    ///   保留原「位置参数 + `configuredMax:` 命名参数」的调用形状，使既有 17 条用例逐字不变。
    /// </summary>
    private static string? Gate(
        decimal quantity,
        string? productionInstructionNo,
        int materialId,
        IReadOnlyList<PiRemainingFact>? piFacts,
        PhaseTwoInitialScheduler.PiStageQuantityContext? stageContext,
        decimal? configuredMax,
        int factoryId = FactoryId,
        string? targetStageCode = Stage)
        => PhaseTwoInitialScheduler.EvaluatePiLegalQuantityGate(
            quantity, productionInstructionNo, materialId, factoryId, targetStageCode,
            piFacts, stageContext, configuredMax);

    private static PhaseTwoInitialScheduler.PiStageQuantityContext Ctx(
        string piNo = PiNo, int materialId = MaterialId, string stage = Stage,
        decimal stageFree = 100m, decimal committed = 0m)
        => new(piNo, materialId, stage, stageFree, committed);

    private static LogicalProductionDemand Demand(
        decimal netQty = 10m, string? piNo = PiNo, int materialId = MaterialId,
        decimal? plannedProcessQty = null)
        => new()
        {
            LogicalDemandKey = "D1",
            ProductionInstructionNo = piNo,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = 1,
            DemandKey = "D1",
            MaterialId = materialId,
            FactoryId = FactoryId,
            StartStageCode = Stage,
            NetOutputQty = netQty,
            PlannedProcessQty = plannedProcessQty ?? netQty,
            RequiredAvailableTime = new DateTime(2026, 9, 21, 0, 0, 0),
            DemandSequence = 1
        };

    private static BatchPolicyRuleSnapshot Policy(decimal? max = null, decimal? min = null)
        => new()
        {
            MaterialId = MaterialId,
            ProductionDepartmentId = 100,
            MinExecutionBatchQty = min,
            MaxExecutionBatchQty = max,
            AllowSplit = true,
            AllowMerge = false
        };

    // ══════════════════════════════════════════════════════════════════
    //  ①②③ 唯一性：禁止取首条
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ① 无明确 PI 来源 ⇒ B-009 的 PI 量限**不适用**（与 B-010 末句「无明确 PI 来源不适用该兜底」同向）⇒ 通过。
    /// ② 非正数量不构成批 ⇒ 不适用 ⇒ 通过。
    /// </summary>
    [Fact]
    public void F02_无明确PI来源或非正数量_门禁不适用()
    {
        Assert.Null(Gate(
            10m, productionInstructionNo: null, materialId: MaterialId,
            piFacts: new[] { Fact() }, stageContext: Ctx(), configuredMax: null));

        Assert.Null(Gate(
            10m, productionInstructionNo: "   ", materialId: MaterialId,
            piFacts: new[] { Fact() }, stageContext: Ctx(), configuredMax: null));

        Assert.Null(Gate(
            0m, productionInstructionNo: PiNo, materialId: MaterialId,
            piFacts: new[] { Fact() }, stageContext: Ctx(), configuredMax: null));
    }

    /// <summary>
    /// ③ **唯一性**：0 行 = 事实缺失 ⇒ 显式未满足；≥2 行 = **来源不唯一** ⇒ 显式未满足
    ///   （旧实现 `break` 取首条 ⇒ 多行时**静默选定一条**，本用例在旧实现下红）。
    /// </summary>
    [Fact]
    public void F02_PI权威行必须唯一_0行缺失与多行不唯一均显式未满足()
    {
        // 0 行（PI 号不匹配）
        var none = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact(piNo: "PI-OTHER") }, stageContext: Ctx(), configuredMax: null);
        Assert.NotNull(none);
        Assert.Contains("找不到 PI 权威行", none);

        // 0 行（物料不匹配）
        var none2 = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact(materialId: 999) }, stageContext: Ctx(), configuredMax: null);
        Assert.NotNull(none2);
        Assert.Contains("找不到 PI 权威行", none2);

        // ≥2 行 ⇒ 不得任取其一
        var dup = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact(original: 100m), Fact(original: 500m) },
            stageContext: Ctx(), configuredMax: null);
        Assert.NotNull(dup);
        Assert.Contains("不唯一", dup);
        Assert.Contains("不得任取其一", dup);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ④⑤⑥ 位置未知须显式未满足（F-02 核心：不得以 PI Remaining 冒充 Stage Free）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ④ **上下文缺失 ⇒ 位置未知**。这是 F-02 的**核心判据**：即便 PI 剩余量（1000）远大于需求量（10），
    ///   **也不得**据此放行 —— 因为「Stage 合法自由量」与「PI 剩余量」是**两个不同的量**。
    ///   （旧实现只卡 `PiRemainingQty`，把二者等同 ⇒ 本用例在旧实现下**通过**（= 缺陷），现实现**拒绝**。）
    /// </summary>
    [Fact]
    public void F02_缺Stage合法自由量上下文_即便PI剩余量充足也显式未满足()
    {
        var reason = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact(original: 1_000m, remaining: 1_000m) },
            stageContext: null, configuredMax: null);

        Assert.NotNull(reason);
        Assert.Contains("缺 Stage 合法自由量上下文", reason);
        Assert.Contains("位置未知", reason);
        Assert.Contains("不得以 PI Remaining 冒充 Stage 自由量", reason);
    }

    /// <summary>⑤ 上下文与需求**不匹配**（PI 号或物料）⇒ 位置不可信 ⇒ 显式未满足。</summary>
    [Fact]
    public void F02_上下文PI或物料不匹配_位置不可信()
    {
        var wrongPi = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact() }, stageContext: Ctx(piNo: "PI-OTHER"), configuredMax: null);
        Assert.NotNull(wrongPi);
        Assert.Contains("位置不可信", wrongPi);

        var wrongMat = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact() }, stageContext: Ctx(materialId: 999), configuredMax: null);
        Assert.NotNull(wrongMat);
        Assert.Contains("位置不可信", wrongMat);
    }

    /// <summary>⑥ 上下文**缺 StageCode**（空白）⇒ 位置未知 ⇒ 显式未满足。</summary>
    [Fact]
    public void F02_上下文缺StageCode_位置未知()
    {
        var reason = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact() }, stageContext: Ctx(stage: "  "), configuredMax: null);

        Assert.NotNull(reason);
        Assert.Contains("缺 StageCode", reason);
        Assert.Contains("位置未知", reason);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑦⑧⑨⑩ 四项限量：**逐项**判别 min 的取值来源
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⑦ **PI Original** 是最紧项 ⇒ 越限。其余三项均给足 ⇒ 越限只可能来自 PI Original。
    /// </summary>
    [Fact]
    public void F03_最紧项为PI_Original_按它判越限()
    {
        var facts = new[] { Fact(original: 40m, remaining: 100m) };   // Original 40 最紧
        Assert.Null(Gate(
            40m, PiNo, MaterialId, facts, Ctx(stageFree: 100m), configuredMax: null));
        var over = Gate(
            41m, PiNo, MaterialId, facts, Ctx(stageFree: 100m), configuredMax: null);
        Assert.NotNull(over);
        Assert.Contains("PI Original 40", over);
    }

    /// <summary>⑧ **PI Remaining** 是最紧项 ⇒ 按它判越限。</summary>
    [Fact]
    public void F03_最紧项为PI_Remaining_按它判越限()
    {
        var facts = new[] { Fact(original: 100m, remaining: 30m) };  // Remaining 30 最紧
        Assert.Null(Gate(
            30m, PiNo, MaterialId, facts, Ctx(stageFree: 100m), configuredMax: null));
        var over = Gate(
            31m, PiNo, MaterialId, facts, Ctx(stageFree: 100m), configuredMax: null);
        Assert.NotNull(over);
        Assert.Contains("PI Remaining 30", over);
    }

    /// <summary>
    /// ⑨ **StageFree − 同PI同Stage已占量** 是最紧项 ⇒ 按它判越限。
    ///   B-009 的「**同 PI 同 Stage 新增 Batch 累计**」正是靠 <c>AlreadyCommittedQty</c> 落地：
    ///   自由量 100、已占 70 ⇒ 可用 30 ⇒ 31 即越限（若忽略已占量，31 ≤ 100 会被误放行）。
    /// </summary>
    [Fact]
    public void F03_同PI同Stage已占量参与累计_最紧时按Stage自由余额判越限()
    {
        var ctx = Ctx(stageFree: 100m, committed: 70m);             // 余额 30
        Assert.Null(Gate(
            30m, PiNo, MaterialId, new[] { Fact() }, ctx, configuredMax: null));
        var over = Gate(
            31m, PiNo, MaterialId, new[] { Fact() }, ctx, configuredMax: null);
        Assert.NotNull(over);
        Assert.Contains("Stage 合法自由 30", over);
        Assert.Contains("同PI同Stage已占 70", over);
    }

    /// <summary>
    /// ⑨b 已占量**超过**自由量 ⇒ 余额钳到 0（**不为负**）⇒ 任何正需求都越限。
    /// </summary>
    [Fact]
    public void F03_已占量超自由量_余额钳零_任何正需求均越限()
    {
        var reason = Gate(
            1m, PiNo, MaterialId, new[] { Fact() },
            Ctx(stageFree: 50m, committed: 80m), configuredMax: null);
        Assert.NotNull(reason);
        Assert.Contains("Stage 合法自由 0", reason);
    }

    /// <summary>⑩ **配置 Max** 是最紧项 ⇒ 按它判越限（正常 Policy 路径把 `MaxExecutionBatchQty` 传进门禁）。</summary>
    [Fact]
    public void F03_最紧项为配置Max_按它判越限()
    {
        Assert.Null(Gate(
            25m, PiNo, MaterialId, new[] { Fact() }, Ctx(stageFree: 100m), configuredMax: 25m));
        var over = Gate(
            26m, PiNo, MaterialId, new[] { Fact() }, Ctx(stageFree: 100m), configuredMax: 25m);
        Assert.NotNull(over);
        Assert.Contains("配置 Max 25", over);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑪⑫ 边界相等合法 / 越限不静默截断
    // ══════════════════════════════════════════════════════════════════

    /// <summary>⑪ **边界相等合法**：`Q == cap` ⇒ 通过（判据是**严格大于**才越限）。</summary>
    [Fact]
    public void F03_需求量恰等于合法量_边界合法()
    {
        Assert.Null(Gate(
            80m, PiNo, MaterialId,
            new[] { Fact(original: 100m, remaining: 80m) }, Ctx(stageFree: 80m), configuredMax: 80m));
    }

    /// <summary>
    /// ⑫ **不静默截断**：越限时返回的是**拒绝原因**（不是把 Q 截到合法量后放行）。
    ///   判据：越限调用返回**非 null**，且消息明示「不静默截断 Q」；同时**不**返回「截断后的数量」——
    ///   门禁的返回值类型是 `string?`，**没有任何数量出口** ⇒ 结构上不可能截断。
    /// </summary>
    [Fact]
    public void F03_越限显式记录未满足_结构上不可能静默截断()
    {
        var reason = Gate(
            100m, PiNo, MaterialId,
            new[] { Fact(original: 100m, remaining: 40m) }, Ctx(stageFree: 100m), configuredMax: null);

        Assert.NotNull(reason);
        Assert.Contains("PI_LEGAL_QTY_OVER_LIMIT", reason);
        Assert.Contains("显式记录未满足", reason);
        Assert.Contains("不静默截断 Q", reason);
        // 返回类型即 `string?`：合法量**没有**任何回传给调用方的通道 ⇒ 无从截断。
        Assert.IsType<string>(reason);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑬⑭ 端到端：正常 Policy 路径过门禁 / 输入未投影登记缺口
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⑬ **正常 Policy 路径**同样受 B-009 门禁约束（F-03：不再只给 B-010 兜底装门禁）：
    ///   · 未越限 ⇒ `IsLegal=true`，批照常产出；
    ///   · 越限 ⇒ `IsLegal=false`（`Conflict`），**0 批**、原因带 `PI_LEGAL_QTY_OVER_LIMIT`；
    ///   · 且 `SolverDiagnostics.PiLegalQuantityGateBlocked` 计数 +1（可观测）。
    /// </summary>
    [Fact]
    public void F03_正常Policy路径同样过B009门禁_越限则冲突且计数()
    {
        var policy = Policy(max: 100m);
        var facts = new[] { Fact(original: 100m, remaining: 100m) };
        var ctx = Ctx(stageFree: 40m);      // Stage 自由 40 最紧

        using var scope = SolverDiagnostics.BeginScope();

        var ok = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 40m), policy, piFacts: facts, piStageContext: ctx);
        Assert.True(ok.IsLegal, ok.ConflictReason);
        Assert.Equal(0, scope.Counters.PiLegalQuantityGateBlocked);

        var blocked = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 41m), policy, piFacts: facts, piStageContext: ctx);
        Assert.False(blocked.IsLegal);
        Assert.Empty(blocked.Batches);
        Assert.Contains("PI_LEGAL_QTY_OVER_LIMIT", blocked.ConflictReason);
        Assert.Equal(1, scope.Counters.PiLegalQuantityGateBlocked);
    }

    /// <summary>
    /// ⑭ **输入未投影**（`piFacts is null`）：门禁**不可评估** ⇒ **不拦截**（拦截会把每条 PI 需求都打死、
    ///   发明一条冻结文档未要求的失败模式，违反 §8「不要求重新冻结业务」），
    ///   但必须用计数器**显式登记缺口**（不静默）⇒ `PiLegalQuantityGateUnevaluated` +1。
    ///   ⚠ 本用例同时如实固定「生产态该门禁当前不可达」这一事实（调用点传 `piFacts: null`）。
    /// </summary>
    [Fact]
    public void F03_输入未投影_不拦截但显式登记缺口()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 10m), Policy(max: 100m), piFacts: null, piStageContext: null);

        Assert.True(formation.IsLegal, formation.ConflictReason);
        Assert.NotEmpty(formation.Batches);
        Assert.Equal(1, scope.Counters.PiLegalQuantityGateUnevaluated);
        Assert.Equal(0, scope.Counters.PiLegalQuantityGateBlocked);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑮ B-010 兜底与 B-009 共用同一门禁
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⑮ **B-010 缺策略兜底**：前置①（明确 PI 来源）②（Policy 完全无匹配）③（Stage 合法自由量可靠）
    ///   **同时**成立 ⇒ 按 `Q_C` 组织**一个** Stage 执行批候选（Q 原值、不伪造 Min/Max/Preferred）。
    ///   ⇒ 与 ⑬ 对照：**同一门禁**（`EvaluatePiLegalQuantityGate`）在两条路径上口径一致。
    /// </summary>
    [Fact]
    public void F03_B010缺策略兜底_前置三项齐备时产一批_Q原值()
    {
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), policy: null,
            piFacts: new[] { Fact(original: 100m, remaining: 80m) },
            piStageContext: Ctx(stageFree: 80m));

        Assert.True(formation.IsLegal, formation.ConflictReason);
        var single = Assert.Single(formation.Batches);
        Assert.Equal(30m, single.NetOutputQty);
        Assert.Equal(30m, single.PlannedProcessQty);
    }

    /// <summary>
    /// ⑮b 前置③**不成立**（缺 Stage 上下文）⇒ 兜底**不适用** ⇒ `IsPiContractPending`、0 批
    ///   （不得凭空造批；也不得回落到笼统的 `BATCH_POLICY_MISSING`）。
    /// </summary>
    [Fact]
    public void F03_B010前置三不成立_契约待核且不造批()
    {
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), policy: null,
            piFacts: new[] { Fact() }, piStageContext: null);

        Assert.False(formation.IsLegal);
        Assert.True(formation.IsPiContractPending);
        Assert.Empty(formation.Batches);
        Assert.Contains("B010_PI_QUANTITY_CONTRACT_PENDING", formation.ConflictReason);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑯ 跨工序不重复计数
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⑯ **跨工序不重复计数**：门禁消费的是**需求总量**（`Σ 批 NetOutputQty`）**一次**，
    ///   不是「每道工序各算一次」。判据：Stage 自由量恰等于需求量时**合法**（= 只算了一次）；
    ///   若按工序数（本例 2 道）重复累计，需求量 30 会变成 60 &gt; 30 而被误判越限 ⇒ 本用例红。
    /// </summary>
    [Fact]
    public void F03_跨工序不重复计数_门禁只消费需求总量一次()
    {
        // PlannedProcessQty = 60（两道工序各 30），NetOutputQty = 30
        var demand = new LogicalProductionDemand
        {
            LogicalDemandKey = "D1",
            ProductionInstructionNo = PiNo,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = 1,
            DemandKey = "D1",
            MaterialId = MaterialId,
            FactoryId = 1,
            StartStageCode = Stage,
            NetOutputQty = 30m,
            PlannedProcessQty = 60m,
            RequiredAvailableTime = new DateTime(2026, 9, 21, 0, 0, 0),
            DemandSequence = 1
        };

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            demand, Policy(max: 100m),
            piFacts: new[] { Fact(original: 100m, remaining: 100m) },
            piStageContext: Ctx(stageFree: 30m));      // 恰等于需求总量

        Assert.True(formation.IsLegal, formation.ConflictReason);
        var total = 0m;
        foreach (var b in formation.Batches) total += b.NetOutputQty;
        Assert.Equal(30m, total);                      // 门禁比较的就是这 30（不是 60）
    }

    // ══════════════════════════════════════════════════════════════════
    //  V1_4 NEW-01：配置 Max 只**逐批**检查，不得当成整条需求的**总量**上限
    //   复审 §8 第 1 项逐字：「追加 C Q60→2×30（Max30、StageFree80）正例，Q60→2×30（StageFree50）反例。」
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// NEW-01 **正例**（复审逐字要求）：C 桶 `Q=60`、`AllowSplit=true`、`Max=30`、`StageFree=80`
    ///   ⇒ 合法方案 `2×30`，**必须合法**。
    ///   旧实现把 `Σ各批 = 60` 与**单批** `Max = 30` 比 ⇒ `60 &gt; 30` ⇒ 误判
    ///   `PI_LEGAL_QTY_OVER_LIMIT`，把合法方案变成 Conflict/0 批（**本用例在旧实现下红**）。
    /// </summary>
    [Fact]
    public void NEW01_单批Max30_两批各30_StageFree80_合法()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 60m), Policy(max: 30m),
            piFacts: new[] { Fact(original: 100m, remaining: 100m) },
            piStageContext: Ctx(stageFree: 80m));

        Assert.True(formation.IsLegal, formation.ConflictReason);
        Assert.Equal(2, formation.Batches.Count);
        Assert.All(formation.Batches, b => Assert.Equal(30m, b.NetOutputQty));
        Assert.Equal(0, scope.Counters.PiLegalQuantityGateBlocked);
    }

    /// <summary>
    /// NEW-01 **反例**（复审逐字要求）：同一 `Q=60 → 2×30`、`Max=30`，但 `StageFree=50`
    ///   ⇒ **两批累计 60 &gt; Stage 自由余额 50** ⇒ 必须拦住。
    ///   判别性：两例的**单批** 30 都 ≤ `Max=30`、也都 ≤ `StageFree`；唯一差别在**累计口径**
    ///   ⇒ 越限**只能**来自需求累计（证明「单批口径」与「需求累计口径」确实是**两个口径**）。
    /// </summary>
    [Fact]
    public void NEW01_单批Max30_两批各30_StageFree50_累计越Stage自由余额_拦住()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 60m), Policy(max: 30m),
            piFacts: new[] { Fact(original: 100m, remaining: 100m) },
            piStageContext: Ctx(stageFree: 50m));

        Assert.False(formation.IsLegal);
        Assert.Empty(formation.Batches);
        Assert.Contains("PI_LEGAL_QTY_OVER_LIMIT", formation.ConflictReason);
        Assert.Equal(1, scope.Counters.PiLegalQuantityGateBlocked);
    }

    /// <summary>
    /// NEW-01 补充判别：`Q=30 / Max=25` ⇒ 合法方案 `2×15`（**配置 Max 只管单批**）。
    ///   旧实现把**总量** 30 与**单批** Max 25 比 ⇒ 误判越限（本用例在旧实现下红）。
    /// </summary>
    [Fact]
    public void NEW01_单批Max25_Q30_拆两批各15_合法()
    {
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), Policy(max: 25m),
            piFacts: new[] { Fact() }, piStageContext: Ctx(stageFree: 100m));

        Assert.True(formation.IsLegal, formation.ConflictReason);
        Assert.Equal(2, formation.Batches.Count);
        Assert.All(formation.Batches, b => Assert.Equal(15m, b.NetOutputQty));
    }

    // ══════════════════════════════════════════════════════════════════
    //  V1_4 NEW-02：A/B 既存 MES 执行批**不参加**普通自由拆合批
    //               ⇒ PI / Stage 数量闸门（只适用 C 桶**新增**批）对其不适用
    // ══════════════════════════════════════════════════════════════════

    /// <summary>A/B 既存执行批夹具（`IsContinuation` / `NoSplitMerge` 显式置位）。</summary>
    private static LogicalProductionDemand AbDemand(
        decimal netQty, bool continuation = true, bool noSplitMerge = true)
        => new()
        {
            LogicalDemandKey = "D-AB",
            ProductionInstructionNo = PiNo,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = 1,
            DemandKey = "D-AB",
            MaterialId = MaterialId,
            FactoryId = FactoryId,
            StartStageCode = Stage,
            NetOutputQty = netQty,
            PlannedProcessQty = netQty,
            RequiredAvailableTime = new DateTime(2026, 9, 21, 0, 0, 0),
            DemandSequence = 1,
            IsContinuation = continuation,
            NoSplitMerge = noSplitMerge
        };

    /// <summary>
    /// NEW-02 **正证**（复审逐字要求「A/B已有执行批、Max小于连续量、事实已投影…确认不会错误改变
    ///   既存执行身份与数量」）：A/B + PI 事实已投影 + `Max=10` &lt; 连续量 30 + `StageFree=10`
    ///   ⇒ 既存执行身份与数量**不得被改写**（仍 1 批、`Q=30` 原值、`IsLegal=true`），
    ///   且门禁**根本不评估**（既不拦截也不记「未评估」）。
    ///   旧实现无 A/B 前置排除 ⇒ 单批 30 与 `min(…, Max 10)` 比 ⇒ 误判 Conflict ⇒ **既存执行批消失**
    ///   （本用例在旧实现下红）。
    /// </summary>
    [Fact]
    public void NEW02_AB既存执行批_配置Max小于连续量_不得改写身份与数量()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            AbDemand(30m), Policy(max: 10m),
            piFacts: new[] { Fact() }, piStageContext: Ctx(stageFree: 10m));

        Assert.True(formation.IsLegal, formation.ConflictReason);
        var single = Assert.Single(formation.Batches);
        Assert.Equal(30m, single.NetOutputQty);                            // 数量**不得**被配置 Max 改写
        Assert.Equal(0, scope.Counters.PiLegalQuantityGateBlocked);
        Assert.Equal(0, scope.Counters.PiLegalQuantityGateUnevaluated);    // 门禁**根本未评估**
    }

    /// <summary>
    /// NEW-02 **反证**：**同一夹具**只把桶从 A/B 换成普通 C 桶 ⇒ 门禁**必须**生效并拦住
    ///   （证明上一条的「合法」确由 A/B 排除带来，而非夹具本身宽松）。
    /// </summary>
    [Fact]
    public void NEW02_同一夹具非AB_C桶_门禁必须生效()
    {
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), Policy(max: 10m),
            piFacts: new[] { Fact() }, piStageContext: Ctx(stageFree: 10m));

        Assert.False(formation.IsLegal);
        Assert.Contains("PI_LEGAL_QTY_OVER_LIMIT", formation.ConflictReason);
    }

    /// <summary>
    /// NEW-02 **边界**：A/B 的两个标记**各自单独**成立即豁免（`IsContinuation` 或 `NoSplitMerge` 任一）。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NEW02_AB两标记任一成立即豁免(bool continuation, bool noSplitMerge)
    {
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            AbDemand(30m, continuation, noSplitMerge), Policy(max: 10m),
            piFacts: new[] { Fact() }, piStageContext: Ctx(stageFree: 10m));

        Assert.True(formation.IsLegal, formation.ConflictReason);
        Assert.Single(formation.Batches);
    }

    // ══════════════════════════════════════════════════════════════════
    //  V1_4 NEW-03：Stage 上下文必须是**本需求当前目标 Stage**
    //               （旧实现只查 `StageCode` **非空** ⇒ 错 Stage 静默放行）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// NEW-03 **反证**（复审逐字要求「补同PI同物料**错Stage**反证」）：
    ///   本批目标 Stage = `STAGE2`，但喂进来的是 **`STAGE1`** 的自由量 100（`STAGE2` 实际只剩 10）
    ///   ⇒ 必须**拒绝**。旧实现只判 `StageCode` 非空 ⇒ `30 ≤ 100` 被**错误放行**（本用例在旧实现下红）。
    /// </summary>
    [Fact]
    public void NEW03_同PI同物料_错Stage_位置不可信_显式未满足()
    {
        var reason = Gate(
            30m, PiNo, MaterialId,
            piFacts: new[] { Fact() },
            stageContext: Ctx(stage: "STAGE1", stageFree: 100m),
            configuredMax: null,
            targetStageCode: "STAGE2");          // ← 本需求目标 Stage

        Assert.NotNull(reason);
        Assert.Contains("错 Stage", reason);
        Assert.Contains("STAGE1", reason);
        Assert.Contains("STAGE2", reason);
    }

    /// <summary>NEW-03 **正证**：目标 Stage 与上下文**一致** ⇒ 正常按合法量判定（不误伤同 Stage 路径）。</summary>
    [Fact]
    public void NEW03_目标Stage一致_按合法量正常判定()
    {
        Assert.Null(Gate(
            30m, PiNo, MaterialId, new[] { Fact() },
            Ctx(stage: "STAGE2", stageFree: 30m), null, targetStageCode: "STAGE2"));
    }

    /// <summary>
    /// NEW-03：**目标 Stage 未知**（需求 `StartStageCode` 为空）⇒ 位置未知
    ///   ⇒ 显式未满足（**不得**默认放行）。
    /// </summary>
    [Fact]
    public void NEW03_目标Stage未知_位置未知_显式未满足()
    {
        var reason = Gate(
            30m, PiNo, MaterialId, new[] { Fact() }, Ctx(), null, targetStageCode: "  ");

        Assert.NotNull(reason);
        Assert.Contains("目标 Stage 未知", reason);
    }

    /// <summary>
    /// NEW-03：**需求级路径同样强制目标 Stage** —— `StartStageCode=STAGE2` + `STAGE1` 上下文
    ///   ⇒ 批形成必须冲突（证明强校验不止在门禁函数里，`FormExecutionBatches` 主路径也吃到）。
    /// </summary>
    [Fact]
    public void NEW03_需求路径_错Stage_批形成冲突()
    {
        var demand = Demand(netQty: 30m);
        demand.StartStageCode = "STAGE2";

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            demand, Policy(max: 100m),
            piFacts: new[] { Fact() },
            piStageContext: Ctx(stage: "STAGE1", stageFree: 100m));

        Assert.False(formation.IsLegal);
        Assert.Contains("错 Stage", formation.ConflictReason);
    }

    /// <summary>
    /// NEW-03 补充（`RISK_UNVERIFIED` 同 PI 号**异工厂**）：
    ///   唯一性键含工厂 ⇒ 只有异厂行时 = **找不到本厂 PI 权威行**（不得**误用异厂剩余量**）。
    /// </summary>
    [Fact]
    public void NEW03补充_同PI号异工厂_不得当作本厂PI权威行()
    {
        var reason = Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact(factoryId: 2) },     // 异厂同号 PI
            stageContext: Ctx(), configuredMax: null);

        Assert.NotNull(reason);
        Assert.Contains("找不到 PI 权威行", reason);
        Assert.Contains("异厂", reason);
    }

    /// <summary>
    /// NEW-03 补充判别：本厂 1 行 + 异厂 1 行 ⇒ **本厂来源唯一** ⇒ 不得因异厂行误判「来源不唯一」。
    ///   旧实现（唯一性键不含工厂）会数到 2 行 ⇒ 误判不唯一（本用例在旧实现下红）。
    /// </summary>
    [Fact]
    public void NEW03补充_同PI号异工厂_不得使本厂来源判定为不唯一()
    {
        Assert.Null(Gate(
            10m, PiNo, MaterialId,
            piFacts: new[] { Fact(factoryId: FactoryId), Fact(factoryId: 2) },
            stageContext: Ctx(), configuredMax: null));
    }

    // ══════════════════════════════════════════════════════════════════
    //  V1_4 NEW-04：本 Run 内「新增 Stage 批」的运行累计账
    //   复审逐字：「新增连续两次出批累计限量 / 合批撤销 / 跨Operation不重复扣账 正反证。」
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// NEW-04 **反证**（复审逐字场景）：同一 Run 内**连续两次出批**（30、20），Stage 合法余额 40
    ///   ⇒ 逐条看都合法（30 ≤ 40、20 ≤ 40），**累计 50 &gt; 40** ⇒ 第二条**必须**被拦。
    ///   无运行累计账时（旧实现）两条都过 ⇒ **本用例红**。
    /// </summary>
    [Fact]
    public void NEW04_连续两次出批_累计越Stage自由余额_第二条被拦()
    {
        var ledger = new PhaseTwoInitialScheduler.PiStageCommitLedger();
        var facts = new[] { Fact(original: 100m, remaining: 100m) };

        var first = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), Policy(max: null),
            piFacts: facts, piStageContext: Ctx(stageFree: 40m), piStageLedger: ledger);

        Assert.True(first.IsLegal, first.ConflictReason);
        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, Stage));      // 首批**已登记**
        Assert.Equal(1, ledger.JournalLength);

        var second = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 20m), Policy(max: null),
            piFacts: facts, piStageContext: Ctx(stageFree: 40m), piStageLedger: ledger);

        Assert.False(second.IsLegal);                                      // 累计 50 > 40
        Assert.Contains("PI_LEGAL_QTY_OVER_LIMIT", second.ConflictReason);
        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, Stage));      // **被拦不登记**
        Assert.Equal(1, ledger.JournalLength);
    }

    /// <summary>
    /// NEW-04 **正证**：同一 Run 连续两次出批（30、10）累计 40 = Stage 余额 40
    ///   ⇒ **边界合法**（证明累计账不是「一律拦第二条」的粗暴实现）。
    /// </summary>
    [Fact]
    public void NEW04_连续两次出批_累计恰等于Stage余额_合法()
    {
        var ledger = new PhaseTwoInitialScheduler.PiStageCommitLedger();
        var facts = new[] { Fact(original: 100m, remaining: 100m) };

        var first = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), Policy(max: null),
            piFacts: facts, piStageContext: Ctx(stageFree: 40m), piStageLedger: ledger);
        var second = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 10m), Policy(max: null),
            piFacts: facts, piStageContext: Ctx(stageFree: 40m), piStageLedger: ledger);

        Assert.True(first.IsLegal, first.ConflictReason);
        Assert.True(second.IsLegal, second.ConflictReason);
        Assert.Equal(40m, ledger.Committed(PiNo, MaterialId, Stage));
    }

    /// <summary>
    /// NEW-04 **候选试排隔离**（复审逐字「候选试排隔离」）：<see cref="PhaseTwoInitialScheduler.PiStageCommitLedger.Clone"/>
    ///   的试排账**不污染**真实账。
    /// </summary>
    [Fact]
    public void NEW04_候选试排隔离_克隆账不污染真实账()
    {
        var ledger = new PhaseTwoInitialScheduler.PiStageCommitLedger();
        ledger.Commit(PiNo, MaterialId, Stage, 30m);

        var trial = ledger.Clone();
        trial.Commit(PiNo, MaterialId, Stage, 999m);

        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, Stage));      // 真实账不受试排影响
        Assert.Equal(1029m, trial.Committed(PiNo, MaterialId, Stage));
        Assert.Equal(1, ledger.JournalLength);
        Assert.Equal(2, trial.JournalLength);
    }

    /// <summary>
    /// NEW-04 **合批撤销**（复审逐字）：<see cref="PhaseTwoInitialScheduler.PiStageCommitLedger.Mark"/> +
    ///   <see cref="PhaseTwoInitialScheduler.PiStageCommitLedger.RollbackTo"/> **按日志身份逆序撤销**，
    ///   撤销后累计量**逐字还原**；撤销到 0 时键被移除（不留 0 值残键）。
    /// </summary>
    [Fact]
    public void NEW04_合批撤销_按身份回滚_累计量逐字还原()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var ledger = new PhaseTwoInitialScheduler.PiStageCommitLedger();
        ledger.Commit(PiNo, MaterialId, Stage, 30m);

        var mark = ledger.Mark();
        ledger.Commit(PiNo, MaterialId, Stage, 20m);
        Assert.Equal(50m, ledger.Committed(PiNo, MaterialId, Stage));

        ledger.RollbackTo(mark);                                           // 撤销第二条（20）
        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, Stage));
        Assert.Equal(1, ledger.JournalLength);
        Assert.Equal(1, scope.Counters.PiStageLedgerRollbacks);

        ledger.RollbackTo(0);                                              // 撤销到空
        Assert.Equal(0m, ledger.Committed(PiNo, MaterialId, Stage));
        Assert.Equal(0, ledger.JournalLength);
    }

    /// <summary>
    /// NEW-04 **跨 Operation 不重复扣账**（复审逐字）：登记粒度 = **需求级批形成**
    ///   ⇒ `PlannedProcessQty=90`（三道工序各 30）、`NetOutputQty=30` 时
    ///   `Committed == 30`（**不是 90**）、`JournalLength == 1`。
    /// </summary>
    [Fact]
    public void NEW04_跨Operation不重复扣账_一次批形成只登记一次()
    {
        var ledger = new PhaseTwoInitialScheduler.PiStageCommitLedger();

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m, plannedProcessQty: 90m), Policy(max: null),
            piFacts: new[] { Fact(original: 100m, remaining: 100m) },
            piStageContext: Ctx(stageFree: 100m), piStageLedger: ledger);

        Assert.True(formation.IsLegal, formation.ConflictReason);
        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, Stage));      // 不是 90
        Assert.Equal(1, ledger.JournalLength);
    }

    /// <summary>
    /// NEW-04 **按 Stage 分账**：同一 PI 同物料但**不同 Stage** ⇒ 各记各的，互不挤占。
    ///   （防止把「按 Stage 累计」退化成「按 PI 累计」。）
    /// </summary>
    [Fact]
    public void NEW04_按Stage分账_不同Stage互不挤占()
    {
        var ledger = new PhaseTwoInitialScheduler.PiStageCommitLedger();
        var facts = new[] { Fact(original: 100m, remaining: 100m) };

        var d2 = Demand(netQty: 30m);
        d2.StartStageCode = "STAGE2";

        var s1 = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 30m), Policy(max: null),
            piFacts: facts, piStageContext: Ctx(stage: Stage, stageFree: 30m), piStageLedger: ledger);
        var s2 = PhaseTwoInitialScheduler.FormExecutionBatches(
            d2, Policy(max: null),
            piFacts: facts, piStageContext: Ctx(stage: "STAGE2", stageFree: 30m), piStageLedger: ledger);

        Assert.True(s1.IsLegal, s1.ConflictReason);
        Assert.True(s2.IsLegal, s2.ConflictReason);
        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, "STAGE1"));
        Assert.Equal(30m, ledger.Committed(PiNo, MaterialId, "STAGE2"));
    }

    /// <summary>
    /// **V1_4 补充（复审 §6 「非真实 MTS PI」RISK_UNVERIFIED）**：`OrderType` **不是**本门禁的判别键。
    ///
    /// 【为什么不能拿 `OrderType` 当判据 —— 2号位 已实测】
    ///   `PeggingOrchestrator.cs:6023`：**「不以 `OrderType` 判别」**——它就是 `ZPQF` 的映射；
    ///   `:6042`：PV2 实测有 **533 行** `OrderType='PRODUCTION_INSTRUCTION'` 却非真实 PI 形态。
    ///   ⇒ 「非真实 MTS PI」的甄别**归装载层**（2号位 按 `MTS_InstructionNo IS NOT NULL` 入集），
    ///     消费端**既无法也不应**用 `OrderType` 判定：误加门禁会**误拒真实 PI** / **误信伪 PI**。
    ///
    /// 本用例反证：同 PI × 物料 × 工厂，**仅 `OrderType` 不同** ⇒ 门禁判定**逐字相同**
    ///   （合法方向都给 `null`；越限方向都给同一 `PI_LEGAL_QTY_OVER_LIMIT`）。
    ///   ⇒ 若有人日后擅自把 `OrderType` 接进判别，本用例立刻红。
    /// </summary>
    [Fact]
    public void V14补充_非真实MTS_PI_甄别不以OrderType为判别键()
    {
        var asSalesOrder = new[] { Fact(orderType: "SALES_ORDER") };
        var asProdInstruction = new[] { Fact(orderType: "PRODUCTION_INSTRUCTION") };
        var asBlank = new[] { Fact(orderType: string.Empty) };

        // 合法方向：三者都必须放行，且判定结果**逐字相同**
        Assert.Null(Gate(10m, PiNo, MaterialId, asSalesOrder, Ctx(), null));
        Assert.Null(Gate(10m, PiNo, MaterialId, asProdInstruction, Ctx(), null));
        Assert.Null(Gate(10m, PiNo, MaterialId, asBlank, Ctx(), null));

        // 越限方向：三者都必须拦住，且理由**逐字相同**（= OrderType 未参与判别）
        var overSales = Gate(120m, PiNo, MaterialId, asSalesOrder, Ctx(), null);
        var overProd = Gate(120m, PiNo, MaterialId, asProdInstruction, Ctx(), null);
        var overBlank = Gate(120m, PiNo, MaterialId, asBlank, Ctx(), null);

        Assert.NotNull(overSales);
        Assert.Contains("PI_LEGAL_QTY_OVER_LIMIT", overSales);
        Assert.Equal(overSales, overProd);
        Assert.Equal(overSales, overBlank);
    }
}
