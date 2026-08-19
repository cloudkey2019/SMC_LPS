namespace LPS.APS.Core.Dto;

/// <summary>
/// 域 Job 参数（跨 Job 传递产品族域标识，序列化为 JSON 通过 Hangfire 参数传递）
/// </summary>
public sealed record DomainJobParam(string DomainKey, int ProductFamilyId);
