using System.Text.Json;
using System.Text.Json.Serialization;

namespace LPS.APS.Core.DTOs.Setup;

/// <summary>
/// Setup 换型规则读模型 DTO（4号位 Setup 契约 §11.1 #1-8）。
/// 口径（Id-口径终定，v1.1）：部门/设备/物料用内部主键 Id，大工艺/工序用 Code。
/// 双返回机制：请求提交 Id，响应回带 Code（<c>DepartmentCode/ResourceCode/FromMaterialCode/ToMaterialCode</c>），前端零映射负担。
/// Status 为派生版本态：版本 DRAFT/SUBMITTED/APPROVED → DRAFT；PUBLISHED → ACTIVE；DISABLED/ARCHIVED → DEPRECATED。
/// remark 字段按用户裁决不返回（与版本治理状态机同源，避免双写漂移）。
/// </summary>
public sealed class SetupRuleDto
{
    /// <summary>规则主键</summary>
    public long Id { get; set; }

    /// <summary>所属规则集版本</summary>
    public long RuleSetVersionId { get; set; }

    /// <summary>规则类型：EXACT（明确产品转换）/ DEFAULT（当前工序+设备默认）</summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>生产部门主键</summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>生产部门编码（Code 回带）</summary>
    public string? DepartmentCode { get; set; }

    /// <summary>大工艺阶段码（Code）</summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>当前小工序码（Code）</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>当前设备主键</summary>
    public int ResourceId { get; set; }

    /// <summary>当前设备编码（Code 回带）</summary>
    public string? ResourceCode { get; set; }

    /// <summary>前产品主键（Material.Id，EXACT 明确；DEFAULT 为 null）</summary>
    public int? FromMaterialId { get; set; }

    /// <summary>前产品编码（Code 回带，MaterialCode）</summary>
    public string? FromMaterialCode { get; set; }

    /// <summary>后产品主键（Material.Id，EXACT 明确；DEFAULT 为 null）</summary>
    public int? ToMaterialId { get; set; }

    /// <summary>后产品编码（Code 回带，MaterialCode）</summary>
    public string? ToMaterialCode { get; set; }

    /// <summary>换型分钟（&gt; 0）</summary>
    public decimal SetupMinutes { get; set; }

    /// <summary>派生状态：DRAFT / ACTIVE / DEPRECATED（源自所属版本六态）</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>创建人</summary>
    public string? CreatedBy { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>更新人</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>更新时间</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 明确换型规则（EXACT）新增/更新入参（#2 / #3）。
/// 部门/设备/物料传内部主键 Id；大工艺/工序传 Code。EXACT 必须携带前后产品。
/// </summary>
public sealed class SetupRuleExactInput
{
    /// <summary>所属规则集版本（须为 DRAFT，否则 400）</summary>
    public long RuleSetVersionId { get; set; }

    /// <summary>生产部门主键</summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>大工艺阶段码（Code）</summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>当前小工序码（Code）</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>当前设备主键</summary>
    public int ResourceId { get; set; }

    /// <summary>前产品主键（Material.Id；缺省/0 → 422）</summary>
    public int FromMaterialId { get; set; }

    /// <summary>后产品主键（Material.Id；缺省/0 → 422）</summary>
    public int ToMaterialId { get; set; }

    /// <summary>换型分钟（&gt; 0，否则 422）</summary>
    public decimal SetupMinutes { get; set; }
}

/// <summary>
/// 默认换型规则（DEFAULT）新增/更新入参（#6 / #7）。
/// 不允许携带产品字段（含 fromMaterialId/toMaterialId 等）→ 422 数据红线。
/// </summary>
public sealed class SetupRuleDefaultInput
{
    /// <summary>所属规则集版本（须为 DRAFT，否则 400）</summary>
    public long RuleSetVersionId { get; set; }

    /// <summary>生产部门主键</summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>大工艺阶段码（Code）</summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>当前小工序码（Code）</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>当前设备主键</summary>
    public int ResourceId { get; set; }

    /// <summary>换型分钟（&gt; 0，否则 422）</summary>
    public decimal SetupMinutes { get; set; }

    /// <summary>
    /// 未映射请求字段（模型绑定器对未知字段静默忽略，DEFAULT 携带产品字段须由此显式拦截 → 422）。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
