using System.ComponentModel.DataAnnotations;

namespace LPS.APS.Core.DTOs.Ctp;

/// <summary>
/// CTP 评估请求体（APS V1 CTP 评估接口契约登记 v1.0，2026-09-30）。
/// 字段与前端 CtpInput 对齐（orderCanonicalId/materialCode/quantity/factoryCode/requestedDueDate/customerCode/purpose）。
/// domainKey 可选：缺省由后端按 materialCode+factoryCode 自动 resolve（纯容错兜底，F-G4 scope 校验不撤）。
/// </summary>
/// <remarks>开发者：3号位（CTP 薄层契约）；试算编排归 1号位/2号位，接入前返回 501。</remarks>
public sealed class CtpEvaluateRequest
{
    /// <summary>新订单 CanonicalId（ERP / 内部统一编号），必填</summary>
    [Required(ErrorMessage = "orderCanonicalId 必填")]
    public string OrderCanonicalId { get; set; } = string.Empty;

    /// <summary>物料编码，必填（domainKey 缺省时用于自动 resolve）</summary>
    [Required(ErrorMessage = "materialCode 必填")]
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>数量，必填（服务层校验 &gt; 0）</summary>
    [Required(ErrorMessage = "quantity 必填")]
    public decimal Quantity { get; set; }

    /// <summary>工厂编码，必填（domainKey 缺省时用于自动 resolve）</summary>
    [Required(ErrorMessage = "factoryCode 必填")]
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>客户请求交期（IsoDate），必填</summary>
    [Required(ErrorMessage = "requestedDueDate 必填")]
    public string RequestedDueDate { get; set; } = string.Empty;

    /// <summary>客户编码，可选</summary>
    public string? CustomerCode { get; set; }

    /// <summary>业务用途标签：CTP / INSERT_IMPACT_ANALYSIS（CTP 永远不允许激活，U11），必填</summary>
    [Required(ErrorMessage = "purpose 必填")]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>目标域 Key（FAMILY_INJECTION / BJ_FAMILY_INJECTION 等）。可选：缺省由后端自动 resolve。</summary>
    public string? DomainKey { get; set; }
}
