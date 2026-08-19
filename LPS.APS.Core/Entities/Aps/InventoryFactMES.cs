using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// MES库存事实表
/// 对应 APS_Production.InventoryFact_MES
/// </summary>
[Table("InventoryFact_MES")]
public class InventoryFactMES
{
    public long Id { get; set; }
    public int MES_ID { get; set; }
    public string Location { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTime SyncedAt { get; set; }
}
