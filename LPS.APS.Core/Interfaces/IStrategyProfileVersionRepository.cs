using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 策略包版本仓储接口（阶段 B-3：3号位）
/// 对应表：APS_Production.dbo.StrategyProfileVersion
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IStrategyProfileVersionRepository
{
    /// <summary>获取策略包版本详情</summary>
    System.Threading.Tasks.Task<StrategyProfileVersion?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>获取策略包的所有版本</summary>
    System.Threading.Tasks.Task<IReadOnlyList<StrategyProfileVersion>> GetByStrategyProfileIdAsync(long strategyProfileId, CancellationToken ct = default);

    /// <summary>获取策略包的默认版本（IsDefault=1 且 PUBLISHED）</summary>
    System.Threading.Tasks.Task<StrategyProfileVersion?> GetDefaultByStrategyProfileIdAsync(long strategyProfileId, CancellationToken ct = default);

    /// <summary>创建策略包版本</summary>
    System.Threading.Tasks.Task<StrategyProfileVersion> AddAsync(StrategyProfileVersion version, CancellationToken ct = default);

    /// <summary>更新策略包版本</summary>
    System.Threading.Tasks.Task UpdateAsync(StrategyProfileVersion version, CancellationToken ct = default);

    /// <summary>清除同一策略包内其他版本的 IsDefault 标志（A-6 不变量）</summary>
    System.Threading.Tasks.Task ClearDefaultFlagAsync(long strategyProfileId, long exceptVersionId, CancellationToken ct = default);

    /// <summary>
    /// 按 RunType 获取所有候选默认版本（P0-06：歧义检测用）
    /// 语义：RunType 匹配 IsActive=1 父 StrategyProfile 的 IsDefault=1 + PUBLISHED 版本全集。
    /// 返回 IReadOnlyList 而非单对象（红线 #4：禁止盲目 First()），
    /// 由 Application 层结合 EffectiveFrom/EffectiveTo 生效窗口过滤后判定 0/1/多（多 → 歧义报错）。
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<StrategyProfileVersion>> GetDefaultByRunTypeAsync(string runType, CancellationToken ct = default);
}
