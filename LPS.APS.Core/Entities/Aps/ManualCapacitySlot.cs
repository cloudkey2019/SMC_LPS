namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 人工能力槽主档实体（5号位维护，对应 dbo.ManualCapacitySlot）
///
/// 语义：某生产部门某小工序(OperationName)下的一个可并行能力位置（非员工）
/// 唯一口径：(ProductionDepartmentId, OperationName, SlotCode)
/// 软删：IsActive=0
/// 数据建模/持久化 Owner：2号位；5号位负责业务维护。
/// 注意：表已删除 OperationCode 列，主键为 ManualSlotId，与 Solver 合成键(10亿+ManualSlotId)对齐。
/// </summary>
public class ManualCapacitySlot
{
    /// <summary>人工槽 Id（主键，Solver 合成键=10亿+ManualSlotId）</summary>
    public int ManualSlotId { get; set; }

    /// <summary>生产部门 Id</summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>小工序名称（如 精修 / 装配），取代原 OperationCode</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>能力槽编码（如 精修_SLOT_01）</summary>
    public string SlotCode { get; set; } = string.Empty;

    /// <summary>是否生效（0=软删，Solver 不装载）</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}