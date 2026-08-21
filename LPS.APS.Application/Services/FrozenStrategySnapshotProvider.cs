using System.Collections.Concurrent;
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
/// <remarks>
/// B-5 缓存（清单 #51 性能红线 / 契约 C2-4）：Snapshot 按 StrategyProfileVersionId 进程级缓存。
/// 语义：Cache Key 即 VersionId（不同版本天然不污染）；PUBLISHED 版本业务内容不可变（修改必产生新 Version），
/// 故缓存无需失效、一次装载后 Run 内不刷新、跨 Run 复用同一份不可变快照。
/// 开发者：3号位
/// </remarks>
public class FrozenStrategySnapshotProvider : IFrozenStrategySnapshotProvider
{
    /// <summary>
    /// B-5 快照缓存：key = StrategyProfileVersionId（契约 C2-4：Cache Key 必须含 VersionId；不同版本不污染）。
    /// 值 = 装配完成后的 Snapshot JSON 快照（不可变字符串）——命中时反序列化重建独立对象，
    /// 杜绝共享可变 Block 实例被消费者就地修改后污染所有并发/后续 Run（code-review MEDIUM：不可变性）。
    /// </summary>
    internal static readonly ConcurrentDictionary<long, string> Cache = new();

    /// <summary>JSON 反序列化设置（与装配路径一致，PascalCase 属性名大小写不敏感）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    /// <summary>清空快照缓存（供测试隔离；生产代码不调用）</summary>
    internal static void ClearCache() => Cache.Clear();

    public async Task<FrozenStrategySnapshot> GetFrozenStrategySnapshotAsync(
        long strategyProfileVersionId,
        CancellationToken ct)
    {
        // B-5 缓存命中（契约 C2-4：Cache Key = VersionId；一次 Run 只加载一次，Run 内不刷新）。
        // 命中时从 JSON 快照反序列化重建独立对象并刷新 FrozenAt（冻结时点=本次调用）——
        // 每次返回独立实例，消费者就地修改返回对象不会污染缓存（code-review MEDIUM：不可变性）。
        if (Cache.TryGetValue(strategyProfileVersionId, out var cachedJson))
        {
            var cachedSnapshot = JsonSerializer.Deserialize<FrozenStrategySnapshot>(cachedJson, JsonOptions)
                ?? throw new InvalidOperationException($"策略包版本 {strategyProfileVersionId} 的缓存快照反序列化失败");
            cachedSnapshot.FrozenAt = DateTime.UtcNow;
            return cachedSnapshot;
        }

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

        // B-5：写入缓存（失败路径已在上述反序列化抛异常退出，不会写入坏快照）。
        // 序列化为 JSON 字符串快照（不可变）；命中路径反序列化重建，FrozenAt 在命中处覆盖为本次时点。
        Cache[strategyProfileVersionId] = JsonSerializer.Serialize(snapshot);

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
            return JsonSerializer.Deserialize<DemandPriorityBlock>(json, JsonOptions)
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
            return JsonSerializer.Deserialize<LockBlock>(json, JsonOptions)
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
            return JsonSerializer.Deserialize<SupplyBlock>(json, JsonOptions)
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
            return JsonSerializer.Deserialize<ProcurementBlock>(json, JsonOptions)
            ?? throw new InvalidOperationException($"参数集版本 {versionId} 的 ProcurementJson 反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"参数集版本 {versionId} 的 ProcurementJson 格式无效，Snapshot 装载失败", ex);
        }
    }
}
