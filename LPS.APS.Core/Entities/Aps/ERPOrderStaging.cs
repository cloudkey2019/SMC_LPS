using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// ERP订单同步暂存表
/// 对应 APS_Production.ERP_Order_Staging
/// 状态机：PENDING → VALIDATED → PROCESSED（成功）/ PENDING → FAILED（校验失败）
/// </summary>
[Table("ERP_Order_Staging")]
public class ERPOrderStaging
{
    public long Id { get; set; }
    public string SourceOrderId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = "ERP";
    public int? SourceMasterID { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string MaterialCode { get; set; } = string.Empty;
    public string FactoryCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int Priority { get; set; } = 50;
    public string BOMNO { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    // v5.0.3 源事实字段（来自 ERP v_APS_SalesOrder 视图）
    public string? TransportMode { get; set; }
    public string? CustomerName { get; set; }
    public string? MTS_InstructionNo { get; set; }

    // v5.0.24 ERP原始字段（由 sp_ValidateAndPromoteOrders 派生标准化）
    public string? CustomerCode { get; set; }
    public string? JPOrderNo { get; set; }
    public string? CustomerSegment { get; set; }
    public string? SalesOrderCategory { get; set; }
    public string? DemandMaturityStatus { get; set; }
    public string? DelayStatus { get; set; }

    /// <summary>
    /// 客户分级：VIP/KEY_ACCOUNT/STANDARD/GENERAL（v4.7新增，由 sp_ValidateAndPromoteOrders 推导）
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

    public string? RawData { get; set; }
    public string SyncStatus { get; set; } = "PENDING";
    public string? ErrorMessage { get; set; }
    public DateTime SyncedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
