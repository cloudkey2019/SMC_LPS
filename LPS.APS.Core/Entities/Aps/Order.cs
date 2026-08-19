using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 订单表（分区表）
/// 对应 APS_Production.[Order]
/// </summary>
[Table("Order")]
public class Order
{
    public long Id { get; set; }
    public int PlanVersionId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public int MaterialId { get; set; }
    public int ProductFamilyId { get; set; }
    public int FactoryId { get; set; }
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public DateTime CustomerDueDate { get; set; }
    public DateTime? PromisedDate { get; set; }
    public int Priority { get; set; } = 50;
    public decimal? PriorityScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DomainKey { get; set; }
    public string SourceSystem { get; set; } = "ERP";
    public string? SourceOrderId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string? BOMNO { get; set; }
    public int? SourceMasterID { get; set; }
    public string? MTS_InstructionNo { get; set; }

    // v5.0.3 订单业务字段
    public string? TransportMode { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerSegment { get; set; }
    public string? SalesOrderCategory { get; set; }
    public string? DemandMaturityStatus { get; set; }

    /// <summary>
    /// 客户分级：VIP/KEY_ACCOUNT/STANDARD/GENERAL（v4.7新增）
    /// </summary>
    public string? CustomerTier { get; set; }

    /// <summary>
    /// 订单发行/下发日期（v4.6新增）
    /// </summary>
    public DateTime? IssueDate { get; set; }

    /// <summary>
    /// 原始纳期（客户最初要求交期），MTS时=DueDate（v4.6新增）
    /// </summary>
    public DateTime? OriginalDueDate { get; set; }

    /// <summary>
    /// 已入库数量（仅MTS），SO订单为NULL（v4.6新增）
    /// </summary>
    public decimal? ReceivedQty { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
