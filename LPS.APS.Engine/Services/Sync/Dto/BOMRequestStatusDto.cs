namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// ODS库 MES_API_BOM_Request 表状态查询映射
/// </summary>
public class BOMRequestStatusDto
{
    public string BatchNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RootCount { get; set; }
    public int? ExpandedRowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
