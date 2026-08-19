namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// BOM展开请求明细DTO（v5.0.32 收敛结构）
/// 对应 MES_API_BOM_Request_Detail 表
/// </summary>
public class BOMRequestDetailDto
{
    /// <summary>Order_Canonical.Id（主锚点）</summary>
    public long OrderCanonicalId { get; set; }

    /// <summary>订单号（冗余，便于ODS侧排查）</summary>
    public string? OrderNo { get; set; }

    /// <summary>来源系统（ERP/MES）</summary>
    public string? SourceSystem { get; set; }

    /// <summary>来源系统订单ID</summary>
    public string? SourceOrderId { get; set; }

    /// <summary>物料编码（5号位BOM入口解析主键）</summary>
    public string? MaterialCode { get; set; }

    /// <summary>工厂编码（5号位按厂分流）</summary>
    public string? FactoryCode { get; set; }

    /// <summary>订单类型：SALES_ORDER/PRODUCTION_INSTRUCTION</summary>
    public string? OrderType { get; set; }

    /// <summary>请求BOMNO（可空，NULL时由5号位解析BOM入口）</summary>
    public string? RequestedBOMNO { get; set; }
}
