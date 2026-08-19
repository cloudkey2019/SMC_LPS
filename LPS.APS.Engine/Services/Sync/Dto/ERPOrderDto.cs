namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// ERP订单DTO（对应 ODS.ext_v_APS_SalesOrder 视图输出）
/// </summary>
public class ERPOrderDto
{
    public string SourceOrderId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string MaterialCode { get; set; } = string.Empty;
    public string BOMNO { get; set; } = string.Empty;
    public string FactoryCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public DateTime? OriginalDueDate { get; set; }
    public decimal? ReceivedQty { get; set; }
    public int Priority { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SourceSystem { get; set; }
    public int? SourceMasterID { get; set; }

    // v5.0.3 源事实字段（来自 ERP 3 表）
    public string? TransportMode { get; set; }
    public string? CustomerName { get; set; }
    public string? MTS_InstructionNo { get; set; }

    // v5.0.24 原始字段（视图输出ERP原值，由 sp_ValidateAndPromoteOrders 派生标准化）
    public string? CustomerCode { get; set; }
    public string? JPOrderNo { get; set; }
    public string? SalesOrderCategory { get; set; }
    public string? DemandMaturityStatus { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
