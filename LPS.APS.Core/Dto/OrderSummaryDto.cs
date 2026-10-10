namespace LPS.APS.Core.Dto;

/// <summary>
/// 订单状态汇总DTO（5号位提供给4号位 Order.vue 顶部 KPI）
/// </summary>
public sealed class OrderSummaryDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>总订单数</summary>
    public int TotalCount { get; init; }

    /// <summary>按时交付（DelayStatus 为空或 ON_TIME）</summary>
    public int OnTimeCount { get; init; }

    /// <summary>已延期（DelayStatus = DELAYED）</summary>
    public int DelayedCount { get; init; }

    /// <summary>有风险（DelayStatus = RISK）</summary>
    public int RiskCount { get; init; }

    /// <summary>未排程（Status = UNSCHEDULED 或无 ProductionPlan）</summary>
    public int UnscheduledCount { get; init; }
}
