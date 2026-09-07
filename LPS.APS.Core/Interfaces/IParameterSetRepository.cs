using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 参数集主表仓储接口（3号位治理，对应 APS_Production.dbo.ParameterSet）
/// 3-4联调接口 G1（A8）：4号位参数集列表页调用。
/// 红线：物料映射类查询返回列表而非单对象（架构红线#4），本接口全部返回列表。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IParameterSetRepository
{
    /// <summary>
    /// 参数集列表查询（支持 启停过滤 + 编码/名称关键字 + 分页）
    /// </summary>
    /// <param name="activeOnly">true=仅启用（IsActive=1）；null=不区分</param>
    /// <param name="keyword">模糊关键字，匹配 ParameterSetCode 或 ParameterSetName（null=不过滤）</param>
    /// <param name="skip">跳过行数（分页 offset；null=不分页）</param>
    /// <param name="take">返回行数（分页 size；null=不分页）</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<ParameterSet>> GetListAsync(
        bool? activeOnly = null,
        string? keyword = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default);
}
