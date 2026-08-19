using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 跨产品族域依赖表（v5.0 阶段 0.5 / 走查文档 v3.3）
/// 对应 APS_Production.Domain_Dependency
///
/// 业务用途：
///   扫描 APS_BOM_RAW + Material + ProductFamily 的跨产品族血缘关系，
///   每日 01:50 由 DomainDependencyScanService 执行 sp_ScanDomainDependency
///   TRUNCATE + INSERT 全量刷新，供 3 号位 Kahn 拓扑排序决定排程顺序。
///
/// 核心约束（架构红线）：
///   - 上游域必须先排完、落盘，下游域才能启动（单向硬约束传递）
///   - 下游域启动前，从 Task 表读上游完工时间 + DefaultLeadTimeDays 构建虚拟库存
///   - 虚拟库存的 AvailableTime 作为时间墙，1号位算法自动"撞墙"顺延
///
/// 复合主键：(UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode)
/// </summary>
[Table("Domain_Dependency")]
public class DomainDependency
{
    /// <summary>
    /// 上游域（供应侧 ProductFamily.Code）
    /// </summary>
    public string UpstreamDomainCode { get; set; } = string.Empty;

    /// <summary>
    /// 下游域（消耗侧 ProductFamily.Code）
    /// </summary>
    public string DownstreamDomainCode { get; set; } = string.Empty;

    /// <summary>
    /// 关联的半成品物料编码（Material.MaterialCode）
    /// </summary>
    public string ChildMaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 跨域物流默认提前期（天）
    /// V1 硬编码 = 2；V2 可配置化（从 MaterialSupplyContext 读取）
    /// </summary>
    public int DefaultLeadTimeDays { get; set; } = 2;

    /// <summary>
    /// 扫描时间戳（sp_ScanDomainDependency 执行时刻）
    /// </summary>
    public DateTime ScannedAt { get; set; }
}
