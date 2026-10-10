using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 阶段提前期参数表（v5.0.7新增）
/// 对应 APS_Production.StageLeadTimeParam
/// 
/// 为无小工序的外协阶段、以及 Routing 数据不完整的阶段提供参数化提前期
/// 1号位消费：读取 StageDetail 阶段顺序 → 对无 RoutingOperation 的阶段查此表生成标准 Task
/// 
/// 命中顺序（V1 正常匹配层级，v1.7 实施包「本轮新增治理项」+ 0号位 2026-09-29 裁决）：
/// 1. DEPT_EXACT             —— ProductionDeptCode + FactoryCode + StageCode
/// 2. FACTORY_STAGE_DEFAULT  —— FactoryCode + StageCode
/// 3. GLOBAL_STAGE_DEFAULT   —— 全局阶段默认值（IsDefault=1）
/// 旧 MaterialCode / ProductFamilyCode 层仅历史兼容，不参与 V1 正常匹配
/// （1号位 消费侧 StageTimingNodeBuilder 仅认上述三级码，不再认 Material/ProductFamily 级）。
/// </summary>
[Table("StageLeadTimeParam")]
public class StageLeadTimeParam
{
    public int Id { get; set; }

    /// <summary>
    /// 工厂编码
    /// </summary>
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 大工艺阶段码（如TJ_OUTS/BJ_PAINT）
    /// </summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>
    /// 生产部门编码（可选，细粒度匹配）
    /// v5.0.16 重命名自 WorkshopCode
    /// </summary>
    public string? ProductionDeptCode { get; set; }

    /// <summary>
    /// 物料编码（可选，旧层——仅历史兼容，不参与 V1 正常匹配）
    /// </summary>
    public string? MaterialCode { get; set; }

    /// <summary>
    /// 产品族编码（可选，旧层——仅历史兼容，不参与 V1 正常匹配）
    /// </summary>
    public string? ProductFamilyCode { get; set; }

    /// <summary>
    /// 提前期（天）
    /// </summary>
    public decimal LeadTimeDays { get; set; }

    /// <summary>
    /// 提前期（小时），更细粒度
    /// </summary>
    public decimal LeadTimeHours { get; set; }

    /// <summary>
    /// 命中优先级（数值越小优先级越高）
    /// </summary>
    public int Priority { get; set; }

    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// 是否全局阶段默认值（最低优先级兜底）
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
