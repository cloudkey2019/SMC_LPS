namespace LPS.APS.Core.Enum;

/// <summary>
/// 依赖关系类型
/// 对应文档：步骤2.4 中的虚拟库存跨域传递
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// 跨产品族依赖
    /// 例如：产品族A的半成品作为产品族B的原料
    /// </summary>
    CROSS_DOMAIN = 1,

    /// <summary>
    /// 跨工厂依赖
    /// 上游工厂产出供下游工厂使用
    /// </summary>
    CROSS_FACTORY = 2,

    /// <summary>
    /// 同域依赖
    /// 同一产品族内的上下游依赖
    /// </summary>
    SAME_DOMAIN = 3,

    /// <summary>
    /// 跨工段依赖
    /// 同一工厂内不同工段之间的依赖
    /// </summary>
    CROSS_STAGE = 4
}
