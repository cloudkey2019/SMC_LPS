using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// ERP库存事实表
/// 对应 APS_Production.InventoryFact_ERP
/// </summary>
[Table("InventoryFact_ERP")]
public class InventoryFactERP
{
    public long Id { get; set; }
    public int MasterID { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTime SyncedAt { get; set; }
}
