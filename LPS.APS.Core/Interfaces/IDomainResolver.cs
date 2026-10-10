namespace LPS.APS.Core.Interfaces;

/// <summary>
/// CTP 域键解析器（APS V1 CTP 评估接口契约登记 v1.0 §3.2）。
/// 按 materialCode + factoryCode 自动确定 DomainKey（纯后端容错兜底，缺省才 resolve，F-G4 校验不撤）。
/// </summary>
/// <remarks>
/// 解析链：Material.ProductFamilyId → DomainDefinition（ScopeType FAMILY 全工厂 / FACTORY_FAMILY 工厂+产品族）。
/// 多域歧义 / 无匹配 → 返回 null（调用方转 400，红线 #4：物料映射禁盲目 .First()）。
/// 开发者：3号位（CTP 薄层契约）。
/// </remarks>
public interface IDomainResolver
{
    /// <summary>
    /// 解析物料+工厂 → 域键。
    /// </summary>
    /// <param name="materialCode">物料编码</param>
    /// <param name="factoryCode">工厂编码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>唯一匹配的 DomainKey；无匹配或多域歧义返回 null</returns>
    Task<string?> ResolveAsync(string materialCode, string factoryCode, CancellationToken cancellationToken = default);
}
