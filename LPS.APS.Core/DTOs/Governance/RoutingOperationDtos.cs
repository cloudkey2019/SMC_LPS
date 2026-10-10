namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// OperationPlanningMode（OPM）三态合法值（与 RoutingOperation 实体注释 + 冻结 DDL
/// CHK_RoutingOperation_PlanningMode 逐字一致）。OPM 属 APS 工艺规划属性（治理配置，0号位 09-23 Q1 裁决），
/// 由治理侧直维护，不依赖 MES/ODS 供给。
/// </summary>
public static class OperationPlanningModeValues
{
    /// <summary>需资源（人工有限产能）——默认值，正常资源找槽</summary>
    public const string FiniteResource = "FINITE_RESOURCE";

    /// <summary>无约束（无设备工序）——跳过资源，叠加标准工时到工艺链</summary>
    public const string Unconstrained = "UNCONSTRAINED";

    /// <summary>仅等待/转运占位——跳过资源，生成占位 Task（时间节点）</summary>
    public const string WaitOnly = "WAIT_ONLY";

    /// <summary>全部合法三态（校验用）</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [FiniteResource] = FiniteResource,
        [Unconstrained] = Unconstrained,
        [WaitOnly] = WaitOnly,
    };

    /// <summary>判断是否合法三态（严格匹配，拒绝任意未登记值）</summary>
    public static bool IsValid(string? mode) => mode is not null && All.ContainsKey(mode);
}

/// <summary>
/// 工序（RoutingOperation）治理投影——供 4号位 配置页面列工序并展示当前 OPM 值。
/// 只读；维护 OPM 走 <see cref="UpdateOperationPlanningModeRequest"/>。
/// </summary>
public sealed class RoutingOperationDto
{
    public long Id { get; init; }
    public int MaterialId { get; init; }
    public int ProductionDepartmentId { get; init; }
    public string RouteCode { get; init; } = "DEFAULT";
    public int PathId { get; init; } = 1;
    public string OperationCode { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
    public string ProcessType { get; init; } = string.Empty;
    public string? StageCode { get; init; }

    /// <summary>OPM 当前值（三态之一，默认 FINITE_RESOURCE）</summary>
    public string OperationPlanningMode { get; init; } = OperationPlanningModeValues.FiniteResource;
}

/// <summary>
/// 维护工序 OPM 值请求（治理侧直维护）。三态校验由控制平面/服务执行，拒绝任意未登记值。
/// </summary>
public sealed class UpdateOperationPlanningModeRequest
{
    /// <summary>目标工序主键（RoutingOperation.Id）</summary>
    public long OperationId { get; init; }

    /// <summary>OPM 三态之一：FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY</summary>
    public string OperationPlanningMode { get; init; } = string.Empty;
}