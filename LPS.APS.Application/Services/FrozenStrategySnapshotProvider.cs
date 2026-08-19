using System.Text.Json;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.Application.Services;

/// <summary>
/// 冻结策略快照提供者实现（阶段 B-3：3号位 Application 装配）
/// 按已冻结的 StrategyProfileVersionId 一次装配完整 FrozenStrategySnapshot。
/// 契约语义（C2-1/C2-2）：2号位 Run 启动时按指定 VersionId 调用一次，本 Run 内存使用，不逐笔调用。
/// 装配流程：StrategyProfileVersion → RuleSetVersion + ParameterSetVersion → 六块 Snapshot。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class FrozenStrategySnapshotProvider : IFrozenStrategySnapshotProvider
{
    private readonly IStrategyProfileVersionRepository _strategyProfileVersionRepo;
    private readonly IRuleSetVersionRepository _ruleSetVersionRepo;
    private readonly IParameterSetVersionRepository _parameterSetVersionRepo;

    public FrozenStrategySnapshotProvider(
        IStrategyProfileVersionRepository strategyProfileVersionRepo,
        IRuleSetVersionRepository ruleSetVersionRepo,
        IParameterSetVersionRepository parameterSetVersionRepo)
    {
        _strategyProfileVersionRepo = strategyProfileVersionRepo;
        _ruleSetVersionRepo = ruleSetVersionRepo;
        _parameterSetVersionRepo = parameterSetVersionRepo;
    }

    public async Task<FrozenStrategySnapshot> GetFrozenStrategySnapshotAsync(
        long strategyProfileVersionId,
        CancellationToken ct)
    {
        // 1. 获取策略包版本（含 RuleSetVersionId + ParameterSetVersionId）
        var strategyProfileVersion = await _strategyProfileVersionRepo.GetByIdAsync(strategyProfileVersionId, ct)
            ?? throw new InvalidOperationException($"策略包版本不存在：{strategyProfileVersionId}");

        // 2. 获取规则集版本和参数集版本
        var ruleSetVersion = await _ruleSetVersionRepo.GetByIdAsync(strategyProfileVersion.RuleSetVersionId, ct)
            ?? throw new InvalidOperationException($"规则集版本不存在：{strategyProfileVersion.RuleSetVersionId}");

        var parameterSetVersion = await _parameterSetVersionRepo.GetByIdAsync(strategyProfileVersion.ParameterSetVersionId, ct)
            ?? throw new InvalidOperationException($"参数集版本不存在：{strategyProfileVersion.ParameterSetVersionId}");

        // 3. 装配 Snapshot（六块 + 三 VersionId 元数据）
        var snapshot = new FrozenStrategySnapshot
        {
            StrategyProfileVersionId = strategyProfileVersionId,
            RuleSetVersionId = ruleSetVersion.Id,
            ParameterSetVersionId = parameterSetVersion.Id,
            FrozenAt = DateTime.UtcNow,

            // ① Demand Priority（来自 RuleSetVersion.DemandPriorityJson）
            DemandPriority = DeserializeDemandPriorityBlock(ruleSetVersion.DemandPriorityJson),

            // ② Lock（来自 ParameterSetVersion.LockJson）
            Lock = DeserializeLockBlock(parameterSetVersion.LockJson),

            // ③ Supply（来自 ParameterSetVersion.SupplyJson）
            Supply = DeserializeSupplyBlock(parameterSetVersion.SupplyJson),

            // ④ Procurement（来自 ParameterSetVersion.ProcurementJson，含 PlanningYield）
            Procurement = DeserializeProcurementBlock(parameterSetVersion.ProcurementJson),

            // ⑤ Solver Strategy（来自 RuleSetVersion 的 Solver 相关字段，暂用空对象）
            SolverStrategy = new SolverStrategyBlock(),

            // ⑥ Candidate Guardrail（来自 ParameterSetVersion 的 Candidate 相关字段，暂用空对象）
            CandidateGuardrail = new CandidateGuardrailBlock()
        };

        return snapshot;
    }

    /// <summary>反序列化 DemandPriorityJson → DemandPriorityBlock</summary>
    private DemandPriorityBlock DeserializeDemandPriorityBlock(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DemandPriorityBlock();
        }

        try
        {
            var block = JsonSerializer.Deserialize<DemandPriorityBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return block ?? new DemandPriorityBlock();
        }
        catch (JsonException)
        {
            return new DemandPriorityBlock();
        }
    }

    /// <summary>反序列化 LockJson → LockBlock</summary>
    private LockBlock DeserializeLockBlock(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LockBlock();
        }

        try
        {
            var block = JsonSerializer.Deserialize<LockBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return block ?? new LockBlock();
        }
        catch (JsonException)
        {
            return new LockBlock();
        }
    }

    /// <summary>反序列化 SupplyJson → SupplyBlock</summary>
    private SupplyBlock DeserializeSupplyBlock(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SupplyBlock();
        }

        try
        {
            var block = JsonSerializer.Deserialize<SupplyBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return block ?? new SupplyBlock();
        }
        catch (JsonException)
        {
            return new SupplyBlock();
        }
    }

    /// <summary>反序列化 ProcurementJson → ProcurementBlock（含 PlanningYield，契约 C2-5）</summary>
    private ProcurementBlock DeserializeProcurementBlock(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProcurementBlock();
        }

        try
        {
            var block = JsonSerializer.Deserialize<ProcurementBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return block ?? new ProcurementBlock();
        }
        catch (JsonException)
        {
            return new ProcurementBlock();
        }
    }
}
