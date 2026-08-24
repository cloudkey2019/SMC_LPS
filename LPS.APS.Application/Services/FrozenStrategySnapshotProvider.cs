using System.Collections.Concurrent;
using System.Text.Json;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
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

        // P0-03 收口：补父策略包版本装载前置校验——仅 PUBLISHED 可装载（新 Run 首次装载防御；
        // 历史重放命中 B-5 缓存路径不校验，进程重启后 miss 路径仅校验 PUBLISHED 不校验时间窗口，可重放历史不可变内容）
        if (strategyProfileVersion.Status != GovernanceVersionStatus.Published)
        {
            throw new InvalidOperationException($"策略包版本 {strategyProfileVersionId} 状态为 {strategyProfileVersion.Status}，非 PUBLISHED，Snapshot 装载失败");
        }

        // 2. 获取规则集版本和参数集版本
        var ruleSetVersion = await _ruleSetVersionRepo.GetByIdAsync(strategyProfileVersion.RuleSetVersionId, ct)
            ?? throw new InvalidOperationException($"规则集版本不存在：{strategyProfileVersion.RuleSetVersionId}");

        var parameterSetVersion = await _parameterSetVersionRepo.GetByIdAsync(strategyProfileVersion.ParameterSetVersionId, ct)
            ?? throw new InvalidOperationException($"参数集版本不存在：{strategyProfileVersion.ParameterSetVersionId}");

        // 3. P1-01 防御：仅 PUBLISHED 且处于有效区间内的版本可装载（防止 Run 装载未发布/失效版本）
        EnsureLoadable(ruleSetVersion, strategyProfileVersionId, "规则集");
        EnsureLoadable(parameterSetVersion, strategyProfileVersionId, "参数集");

        // 4. 装配 Snapshot（六块 + 三 VersionId 元数据）
        // 方案 A（0号位 §7.2）：六块统一从 ContentSnapshotJson 结构化子块反序列化（契约 §6.10.5）。
        // 缺失/损坏一律装载失败（P0-02 六块统一），不静默回退空 Block——
        // 避免"数据库显示本 Run 使用某版本、程序却按空规则执行"的版本追溯失真。
        var snapshot = new FrozenStrategySnapshot
        {
            StrategyProfileVersionId = strategyProfileVersionId,
            RuleSetVersionId = ruleSetVersion.Id,
            ParameterSetVersionId = parameterSetVersion.Id,
            FrozenAt = DateTime.UtcNow,

            // ① Demand Priority（来自 RuleSetVersion.ContentSnapshotJson.DemandPriority 子块）
            DemandPriority = DeserializeRuleSetBlock(ruleSetVersion, strategyProfileVersionId),

            // ②~④ 来自 ParameterSetVersion.ContentSnapshotJson 子块
            Lock = DeserializeParameterSetBlock<LockBlock>(parameterSetVersion, "Lock", strategyProfileVersionId),
            Supply = DeserializeParameterSetBlock<SupplyBlock>(parameterSetVersion, "Supply", strategyProfileVersionId),
            Procurement = DeserializeParameterSetBlock<ProcurementBlock>(parameterSetVersion, "Procurement", strategyProfileVersionId),

            // ⑤ Solver Strategy（P0-02 收口：真实来源，替代原空对象）
            SolverStrategy = DeserializeParameterSetBlock<SolverStrategyBlock>(parameterSetVersion, "SolverStrategy", strategyProfileVersionId),

            // ⑥ Candidate Guardrail（P0-02 收口：真实来源，替代原空对象）
            CandidateGuardrail = DeserializeParameterSetBlock<CandidateGuardrailBlock>(parameterSetVersion, "CandidateGuardrail", strategyProfileVersionId)
        };

        // B-5：写入缓存（失败路径已在上述反序列化抛异常退出，不会写入坏快照）。
        // 序列化为 JSON 字符串快照（不可变）；命中路径反序列化重建，FrozenAt 在命中处覆盖为本次时点。
        Cache[strategyProfileVersionId] = JsonSerializer.Serialize(snapshot);

        return snapshot;
    }

    /// <summary>
    /// 装载前置校验（P0-03 收口）：仅 PUBLISHED 版本可装载。
    /// 语义：Provider 只读历史不可变内容（契约 §6.10.5）——移除 EffectiveFrom/EffectiveTo 当前时间窗口检查，
    /// 该职责归新 Run 准入（ResolveDefaultStrategyProfileVersionAsync 已按生效窗口过滤默认版本）；
    /// 历史版本按显式 VersionId 重放不受当前时间影响。非 PUBLISHED 拒绝（未发布内容不可作为 Run 冻结来源）。
    /// </summary>
    private static void EnsureLoadable(
        LPS.APS.Core.Entities.APS.RuleSetVersion version,
        long strategyProfileVersionId,
        string kind)
    {
        if (version.Status != GovernanceVersionStatus.Published)
        {
            throw new InvalidOperationException($"策略包版本 {strategyProfileVersionId} 的{kind}版本状态为 {version.Status}，非 PUBLISHED，Snapshot 装载失败");
        }
    }

    /// <summary>
    /// 装载前置校验（P0-03 收口）：仅 PUBLISHED 版本可装载（同 RuleSet 语义）。
    /// 移除 EffectiveFrom/EffectiveTo 当前时间窗口检查——历史版本按显式 VersionId 重放不受当前时间影响。
    /// </summary>
    private static void EnsureLoadable(
        LPS.APS.Core.Entities.APS.ParameterSetVersion version,
        long strategyProfileVersionId,
        string kind)
    {
        if (version.Status != GovernanceVersionStatus.Published)
        {
            throw new InvalidOperationException($"策略包版本 {strategyProfileVersionId} 的{kind}版本状态为 {version.Status}，非 PUBLISHED，Snapshot 装载失败");
        }
    }

    /// <summary>从 RuleSetVersion.ContentSnapshotJson 反序列化 DemandPriority 子块（P0-02 六块统一失败）</summary>
    private DemandPriorityBlock DeserializeRuleSetBlock(
        LPS.APS.Core.Entities.APS.RuleSetVersion version,
        long strategyProfileVersionId)
    {
        if (string.IsNullOrWhiteSpace(version.ContentSnapshotJson))
        {
            throw new InvalidOperationException($"规则集版本 {version.Id} 的 ContentSnapshotJson 为空/缺失，Snapshot 装载失败");
        }

        try
        {
            using var doc = JsonDocument.Parse(version.ContentSnapshotJson);
            if (!doc.RootElement.TryGetProperty("DemandPriority", out var blockElement))
            {
                throw new InvalidOperationException($"规则集版本 {version.Id} 的 ContentSnapshotJson 缺少 DemandPriority 子块，Snapshot 装载失败");
            }

            return blockElement.Deserialize<DemandPriorityBlock>(JsonOptions)
                ?? throw new InvalidOperationException($"规则集版本 {version.Id} 的 DemandPriority 子块反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"规则集版本 {version.Id} 的 ContentSnapshotJson 格式无效，Snapshot 装载失败", ex);
        }
    }

    /// <summary>从 ParameterSetVersion.ContentSnapshotJson 反序列化指定子块（P0-02 六块统一失败）</summary>
    private TBlock DeserializeParameterSetBlock<TBlock>(
        LPS.APS.Core.Entities.APS.ParameterSetVersion version,
        string blockName,
        long strategyProfileVersionId)
        where TBlock : class
    {
        if (string.IsNullOrWhiteSpace(version.ContentSnapshotJson))
        {
            throw new InvalidOperationException($"参数集版本 {version.Id} 的 ContentSnapshotJson 为空/缺失，Snapshot 装载失败");
        }

        try
        {
            using var doc = JsonDocument.Parse(version.ContentSnapshotJson);
            if (!doc.RootElement.TryGetProperty(blockName, out var blockElement))
            {
                throw new InvalidOperationException($"参数集版本 {version.Id} 的 ContentSnapshotJson 缺少 {blockName} 子块，Snapshot 装载失败");
            }

            return blockElement.Deserialize<TBlock>(JsonOptions)
                ?? throw new InvalidOperationException($"参数集版本 {version.Id} 的 {blockName} 子块反序列化结果为空，Snapshot 装载失败");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"参数集版本 {version.Id} 的 ContentSnapshotJson 格式无效，Snapshot 装载失败", ex);
        }
    }
}
