using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 策略包主表仓储接口（P0-06：3号位治理域）
/// 对应表：APS_Production.dbo.StrategyProfile
/// RunType 匹配语义（C2-3）：StrategyProfileVersion 无 RunType 列，须经父表 StrategyProfile.RunType 匹配。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IStrategyProfileRepository
{
    /// <summary>获取策略包主表记录</summary>
    System.Threading.Tasks.Task<StrategyProfile?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 按 RunType 获取启用的策略包（RunType 匹配候选）
    /// 返回列表而非单对象（红线 #4：禁止盲目 First()），由 Application 层结合版本状态判定。
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<StrategyProfile>> GetByRunTypeAsync(string runType, CancellationToken ct = default);

    /// <summary>
    /// 策略包列表查询（3-4联调接口 G1/A12：4号位策略包列表页）
    /// 支持 启停过滤 + 编码/名称关键字 + 分页。
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<StrategyProfile>> GetListAsync(
        bool? activeOnly = null,
        string? keyword = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default);
}
