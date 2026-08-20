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
        // P0-04：四块有来源的 JSON 一律禁止静默回退空 Block——缺失/损坏直接装载失败，
        // 避免"数据库显示本 Run 使用某版本、程序却按空规则执行"的版本追溯失真。
        var snapshot = new FrozenStrategySnapshot
        {
            StrategyProfileVersionId = strategyProfileVersionId,
            RuleSetVersionId = ruleSetVersion.Id,
            ParameterSetVersionId = parameterSetVersion.Id,
            FrozenAt = DateTime.UtcNow,

            // ① Demand Priority（来自 RuleSetVersion.DemandPriorityJson）
            DemandPriority = DeserializeDemandPriorityBlock(ruleSetVersion.DemandPriorityJson, ruleSetVersion.Id),

            // ② Lock（来自 ParameterSetVersion.LockJson）
            Lock = DeserializeLockBlock(parameterSetVersion.LockJson, parameterSetVersion.Id),

            // ③ Supply（来自 ParameterSetVersion.SupplyJson）
            Supply = DeserializeSupplyBlock(parameterSetVersion.SupplyJson, parameterSetVersion.Id),

            // ④ Procurement（来自 ParameterSetVersion.ProcurementJson，含 PlanningYield）
            Procurement = DeserializeProcurementBlock(parameterSetVersion.ProcurementJson, parameterSetVersion.Id),

            // ⑤ Solver Strategy（P0-03 待办：P0-01 DDL 方案确认前暂无真实版本来源，保持空对象）
            SolverStrategy = new SolverStrategyBlock(),

            // ⑥ Candidate Guardrail（P0-03 待办：同上，保持空对象）
            CandidateGuardrail = new CandidateGuardrailBlock()
        };

        return snapshot;
    }

    /// <summary>反序列化 DemandPriorityJson → DemandPriorityBlock（P0-04：缺失/损坏一律失败，不静默回退）</summary>
    private DemandPriorityBlock DeserializeDemandPriorityBlock(string? json, long versionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"规则集版本 {versionId} 的 DemandPriorityJson 为空/缺失，Snapshot 装载失败");
        }

        try
        {
            return JsonSerializer.Deserialize<DemandPriorityBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException($"规则集版本 {versionId} 的 DemandPriorityJson 反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"规则集版本 {versionId} 的 DemandPriorityJson 格式无效，Snapshot 装载失败", ex);
        }
    }

    /// <summary>反序列化 LockJson → LockBlock（P0-04：缺失/损坏一律失败，不静默回退）</summary>
    private LockBlock DeserializeLockBlock(string? json, long versionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 LockJson 为空/缺失，Snapshot 装载失败");
        }

        try
        {
            return JsonSerializer.Deserialize<LockBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException($"参数集版本 {versionId} 的 LockJson 反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 LockJson 格式无效，Snapshot 装载失败", ex);
        }
    }

    /// <summary>反序列化 SupplyJson → SupplyBlock（P0-04：缺失/损坏一律失败，不静默回退）</summary>
    private SupplyBlock DeserializeSupplyBlock(string? json, long versionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 SupplyJson 为空/缺失，Snapshot 装载失败");
        }

        try
        {
            return JsonSerializer.Deserialize<SupplyBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException($"参数集版本 {versionId} 的 SupplyJson 反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 SupplyJson 格式无效，Snapshot 装载失败", ex);
        }
    }

    /// <summary>反序列化 ProcurementJson → ProcurementBlock（含 PlanningYield，契约 C2-5；P0-04：缺失/损坏一律失败）</summary>
    private ProcurementBlock DeserializeProcurementBlock(string? json, long versionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 ProcurementJson 为空/缺失，Snapshot 装载失败");
        }

        try
        {
            return JsonSerializer.Deserialize<ProcurementBlock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException($"参数集版本 {versionId} 的 ProcurementJson 反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 ProcurementJson 格式无效，Snapshot 装载失败", ex);
        }
    }
}
