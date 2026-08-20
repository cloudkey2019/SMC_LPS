using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 参数集版本仓储接口（阶段 A-2：Engine 治理仓储，Dapper + DatabaseConnectionManager 实现）
/// 对应 APS_Production.ParameterSetVersion。
/// 红线：已发布版本不可原地修改；新 Run 可引用新版本、旧 Run 引用不变（R02 验收场景）。
/// </summary>
public interface IParameterSetVersionRepository
{
    /// <summary>按 Id 查询参数集版本</summary>
    Task<ParameterSetVersion?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按参数集 Id 查询全部版本（按 VersionCode 排序）</summary>
    Task<IReadOnlyList<ParameterSetVersion>> GetByParameterSetIdAsync(long parameterSetId, CancellationToken ct = default);

    /// <summary>新增版本（新版本须为新记录，历史不可覆盖）</summary>
    Task<ParameterSetVersion> AddAsync(ParameterSetVersion version, CancellationToken ct = default);

    /// <summary>更新版本（仅限非 PUBLISHED 状态的可编辑字段）</summary>
    Task UpdateAsync(ParameterSetVersion version, CancellationToken ct = default);
}
