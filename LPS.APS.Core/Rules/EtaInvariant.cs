namespace LPS.APS.Core.Rules;

/// <summary>
/// ETA 来源优先级枚举（D-8 ETA Invariant 固化）
/// 优先级固定：<see cref="Manual"/> &gt; <see cref="Erp"/> &gt; <see cref="DefaultLt"/>。
/// <b>注意：枚举按优先级声明顺序排列（高优先在前），但序数值不代表优先级权重——
/// 消费者不得按序数值排序/比较来源（DefaultLt 序数最高却优先级最低），必须经 <see cref="EtaInvariant.Resolve"/> 解析。</b>
/// </summary>
public enum EtaSource
{
    /// <summary>无任何有效 ETA（Manual/ERP/DefaultLT 均为空）</summary>
    None,

    /// <summary>人工维护 ETA（最高优先；人工取消后回落 ERP）</summary>
    Manual,

    /// <summary>ERP ETA（供应商承诺/系统回复；Manual 为空时回落）</summary>
    Erp,

    /// <summary>默认采购提前期（DefaultLT）推算（仅当 Manual/ERP 均为空；属估算，不得伪装真实承诺）</summary>
    DefaultLt
}

/// <summary>
/// ETA 链解析结果（D-8 不变式输出）
/// 供下游（Pegging/排程）读取有效 ETA 及其来源。
/// </summary>
/// <param name="EffectiveEta">最终生效的 ETA；<see cref="EtaSource.None"/> 时为 null</param>
/// <param name="Source">来源优先级（决定后续语义：估算/承诺）</param>
public sealed record EtaResolution(DateTime? EffectiveEta, EtaSource Source)
{
    /// <summary>是否存在有效 ETA</summary>
    public bool HasEta => EffectiveEta.HasValue;

    /// <summary>
    /// 是否估算日期——由 <see cref="Source"/> 派生（仅 DefaultLT 推算为 true，
    /// 《Pegging业务说明》§10.3：不得伪装成真实供应商承诺）。
    /// 计算属性而非存储字段：杜绝构造"Source=Manual 而 IsEstimated=true"的不一致记录。
    /// </summary>
    public bool IsEstimated => Source == EtaSource.DefaultLt;
}

/// <summary>
/// ETA 优先级链固化不变式（D-8，阶段 D 门 R10）
/// 契约（《3号位实施包》§10.2 /《Pegging业务说明》§10.1）：
/// <c>Manual ETA &gt; ERP ETA &gt; DefaultLT</c> 冻结为业务规则，
/// <b>不可配置化</b>——不做成可任意重排的 Rule Chain，系统不提供任何 ETA 排序配置入口。
/// 语义：
/// 1. Manual 存在即以 Manual 为准（与日期先后无关，来源优先级是唯一决定因素）；
/// 2. Manual 取消（为 null）后回落 ERP；ERP 为空再回落 DefaultLT；
/// 3. DefaultLT 推算结果标记 <see cref="EtaResolution.IsEstimated"/>（估算属性保留）；
/// 4. 纯静态函数：每次调用按当前输入确定性解析，不缓存旧值（"人工取消后回落"即多次调用结果）。
/// 开发者：3号位
/// </summary>
public static class EtaInvariant
{
    /// <summary>
    /// 按冻结优先级链解析有效 ETA。
    /// </summary>
    /// <param name="manualEta">人工维护 ETA（可空）</param>
    /// <param name="erpEta">ERP ETA（可空）</param>
    /// <param name="defaultLtEta">默认采购提前期推算 ETA（可空；由 DefaultLT 与参考日推算后传入）</param>
    /// <returns>确定性解析结果（含来源与估算标记）</returns>
    /// <remarks>
    /// 调用方须以 null 表示"无该来源 ETA"；<c>default(DateTime)</c>（即年份 1 的 MinValue）
    /// 会被视为真实日期，调用方应在传入前将未赋值的 DateTime 规范化为 null（常见于 Dapper/反序列化漏配可空）。
    /// </remarks>
    public static EtaResolution Resolve(DateTime? manualEta, DateTime? erpEta, DateTime? defaultLtEta)
    {
        if (manualEta.HasValue)
        {
            return new EtaResolution(manualEta, EtaSource.Manual);
        }

        if (erpEta.HasValue)
        {
            return new EtaResolution(erpEta, EtaSource.Erp);
        }

        if (defaultLtEta.HasValue)
        {
            return new EtaResolution(defaultLtEta, EtaSource.DefaultLt);
        }

        return new EtaResolution(null, EtaSource.None);
    }
}
