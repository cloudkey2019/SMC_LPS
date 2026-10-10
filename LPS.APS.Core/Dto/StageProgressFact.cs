namespace LPS.APS.Core.Dto;

/// <summary>
/// Stage进度事实（2号位提供给5号位的输入数据）
/// </summary>
public sealed class StageProgressFact
{
    /// <summary>
    /// Stage代码
    /// </summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>
    /// Stage累计良品完成数量（GoodCompletedQty，冻结字段名，不得改名）
    ///
    /// 来源：StageProgressSnapshot.GoodCompletedQty
    /// 5号位 PI Position 计算器消费此字段做 Stage 链位置推导。
    /// </summary>
    public decimal GoodCompletedQty { get; init; }

    /// <summary>
    /// Stage计划数量
    ///
    /// 来源：StageProgressSnapshot.PlannedQty
    /// 用途：Stage 级 RemainingQty = max(PlannedQty - GoodCompletedQty, 0) 运行时派生。
    /// </summary>
    public decimal PlannedQty { get; init; }

    /// <summary>
    /// Stage剩余数量（= max(PlannedQty - GoodCompletedQty, 0)）
    ///
    /// 来源：StageProgressSnapshot.RemainingQty（PERSISTED 计算列）
    /// 用途：无工序进度数据时，ExistingExecutionContext 的 Stage 级兜底 DerivedRemainingQty。
    /// ⚠️ 只存在于进度快照层，不新增到工单级（28-0 v2.6 §十七 冻结）。
    /// </summary>
    public decimal RemainingQty { get; init; }

    /// <summary>
    /// Stage序号（用于排序）
    /// </summary>
    public int StageSequence { get; init; }

    /// <summary>
    /// 数据来源快照ID（用于追溯）
    /// </summary>
    public long? SnapshotId { get; init; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
