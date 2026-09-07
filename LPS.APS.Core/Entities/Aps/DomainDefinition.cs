using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 域定义表（Domain 专项 / 冻结 DDL v5.1.4 §2.4aa）
/// 对应 APS_Production.dbo.DomainDefinition
///
/// 业务定位：排程归域的唯一事实源。1号位按域（DomainKey）拆域排程，
/// 2号位归域执行 + 订单装载按 IsActive=1 筛选。
///
/// 治理职责（Owner=3号位）：
///   - 新建 / 编辑 / 启用 / 停用
///   - DomainKey 唯一性 + 稳定性（启用后不得变更）
///   - ScopeType 合法性（V1 仅 FAMILY / FACTORY_FAMILY）
///   - ProductFamily / Factory 引用合法性
///   - 审计（APS_Auth.GovernanceAuditLog，EntityType=DomainDefinition）
///
/// 消费方（2号位只读，IDomainDefinitionService）：
///   SELECT DomainKey, ScopeType, ProductFamilyId, FactoryId
///   FROM DomainDefinition WHERE IsActive = 1 ORDER BY SortOrder, DomainKey
/// </summary>
[Table("DomainDefinition")]
public class DomainDefinition
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>
    /// 域业务键（稳定 / 唯一 / 可追溯）。
    /// 不得与 ProductFamily.Code / PlanVersionId / ScheduleRunId 复用。
    /// </summary>
    public string DomainKey { get; set; } = string.Empty;

    /// <summary>域显示名称（中文，可随展示需求调整，不影响 DomainKey）</summary>
    public string DomainName { get; set; } = string.Empty;

    /// <summary>
    /// 域范围类型。V1 仅支持：
    ///   FAMILY           = 产品族级域（全工厂归域，FactoryId 必须为空）
    ///   FACTORY_FAMILY   = 工厂+产品族域（FactoryId 必填）
    /// </summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>关联产品族 Id（ProductFamily.Id，引用合法性校验）</summary>
    public int ProductFamilyId { get; set; }

    /// <summary>关联工厂 Id（Factory.Id）。FACTORY_FAMILY 必填；FAMILY 必须为空。</summary>
    public int? FactoryId { get; set; }

    /// <summary>是否有效（1=启用，0=停用）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>排序号（越小越靠前，默认 100）</summary>
    public int SortOrder { get; set; } = 100;

    /// <summary>创建人</summary>
    public string? CreatedBy { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后更新人</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; }
}
