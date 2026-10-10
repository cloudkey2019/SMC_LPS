namespace LPS.APS.Core.Dto;

/// <summary>
/// MES工单快照事实（2号位提供给5号位，用于形成 ExistingExecutionContext）
///
/// 来源：MESWorkOrderSnapshot表（2号位 sp_SyncMESWorkOrderSnapshot 同步）
/// 用途：5号位组合工单身份 + 工序进度 + PI Position → 标准化既存执行上下文
///
/// 冻结红线（v1.6/v2.6 裁决 + v1.4 实施包 §8.2）：
/// - 工单级不新增 RemainingQty / RemainingNetQty / LastReportResourceCode / ExecutionStatus
/// - WorkOrderStatus 沿用既有值：RELEASED / IN_PROGRESS / CLOSED / DELETED
/// - 连续份额数量由工序/Stage进度 + PI Position 运行时派生，不要求工单级原生字段
/// </summary>
public sealed class WorkOrderSnapshotFact
{
    /// <summary>
    /// 生产指示号
    /// </summary>
    public string ProductionInstructionNo { get; init; } = string.Empty;

    /// <summary>
    /// MES工单号
    /// </summary>
    public string MESWorkOrderNo { get; init; } = string.Empty;

    /// <summary>
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 工单计划数量
    /// </summary>
    public decimal PlannedQty { get; init; }

    /// <summary>
    /// 工单状态（冻结值域：RELEASED / IN_PROGRESS / CLOSED / DELETED）
    ///
    /// ⚠️ 禁止改名为 ExecutionStatus，禁止使用 NOT_STARTED / STARTED_INCOMPLETE / COMPLETED。
    /// </summary>
    public string WorkOrderStatus { get; init; } = string.Empty;

    /// <summary>
    /// 数据截止时间
    /// </summary>
    public DateTime DataCutoffTime { get; init; }
}
