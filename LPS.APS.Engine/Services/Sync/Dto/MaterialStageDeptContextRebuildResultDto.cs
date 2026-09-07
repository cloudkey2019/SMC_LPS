namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_RebuildMaterialStageDeptContext 存储过程执行结果
/// 对应 MaterialStageDeptContext 表的 SCD Type 2 全量重建统计（新增/变更 + 失效行数）
/// </summary>
public class MaterialStageDeptContextRebuildResultDto
{
    /// <summary>批次号（MSC_CTX_yyyyMMdd_HHmmss）</summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>受影响行数（新增/变更行数 + 失效行数，SP @RowsAffected 输出）</summary>
    public int RowsAffected { get; set; }

    /// <summary>错误信息（null 表示成功）</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
