namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// Run 引用追溯结果（P0-06：3号位治理域）
/// StrategyProfileVersion → 父 StrategyProfile + 引用的 RuleSetVersion / ParameterSetVersion。
/// 供 ScheduleRun.StrategyProfileVersionId → 版本 → 规则集/参数集 完整链路审计追溯。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class RunStrategyProfileTrace
{
    /// <summary>策略包版本 ID</summary>
    public long StrategyProfileVersionId { get; set; }

    /// <summary>版本编码</summary>
    public string VersionCode { get; set; } = string.Empty;

    /// <summary>父策略包 ID</summary>
    public long StrategyProfileId { get; set; }

    /// <summary>父策略包编码</summary>
    public string? StrategyProfileCode { get; set; }

    /// <summary>父策略包 RunType（StrategyProfile.RunType）</summary>
    public string? RunType { get; set; }

    /// <summary>引用的规则集版本 ID</summary>
    public long RuleSetVersionId { get; set; }

    /// <summary>引用的规则集版本编码</summary>
    public string? RuleSetVersionCode { get; set; }

    /// <summary>引用的参数集版本 ID</summary>
    public long ParameterSetVersionId { get; set; }

    /// <summary>引用的参数集版本编码</summary>
    public string? ParameterSetVersionCode { get; set; }

    /// <summary>策略包版本状态</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>生效起始</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>生效截止</summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>是否默认版本</summary>
    public bool IsDefault { get; set; }

    /// <summary>发布时间</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>发布人</summary>
    public string? PublishedBy { get; set; }
}
