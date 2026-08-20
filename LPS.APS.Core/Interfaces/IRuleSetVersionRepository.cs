using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 规则集版本仓储接口（阶段 A-2：Engine 治理仓储，Dapper + DatabaseConnectionManager 实现）
/// 对应 APS_Production.RuleSetVersion。
/// 红线：已发布版本不可原地修改，须创建新版本（R01 验收场景）。
/// </summary>
public interface IRuleSetVersionRepository
{
    /// <summary>按 Id 查询规则集版本</summary>
    Task<RuleSetVersion?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按规则集 Id 查询全部版本（按 VersionCode 排序）</summary>
    Task<IReadOnlyList<RuleSetVersion>> GetByRuleSetIdAsync(long ruleSetId, CancellationToken ct = default);

    /// <summary>新增版本（新版本须为新记录，历史不可覆盖）</summary>
    Task<RuleSetVersion> AddAsync(RuleSetVersion version, CancellationToken ct = default);

    /// <summary>更新版本（仅限非 PUBLISHED 状态的可编辑字段）</summary>
    Task UpdateAsync(RuleSetVersion version, CancellationToken ct = default);
}
