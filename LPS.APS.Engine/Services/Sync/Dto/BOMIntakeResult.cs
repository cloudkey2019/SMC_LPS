namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 独立接货（BOM展开结果接货）结果（IBOMResultPullService.IntakeLatestReadyBatchAsync 返回）。
/// 与夜间批次编排器（NightlyBatchOrchestrator Step 5）解耦：可单独触发「找最近 READY 批次 → 接货」，
/// 供白天补接货 / 联调使用。
/// </summary>
public class BOMIntakeResult
{
    /// <summary>接货批次号（格式：REQ_yyyyMMdd_xxxxxxxx）；无 READY 批次时为空</summary>
    public string? BatchNo { get; set; }

    /// <summary>本次接货行数（APS_BOM_RAW 行数）；未接货为 0</summary>
    public int PulledCount { get; set; }

    /// <summary>是否实际执行了接货（找到 READY 批次并成功拉取）</summary>
    public bool IntakePerformed => !string.IsNullOrEmpty(BatchNo);
}