using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Loaders;


/// <summary>
/// Timed Supply事实加载器接口
/// 从SupplyFact_Pipeline装载原始采购事实
/// 职责边界：5号位仅负责加载Raw事实，不负责SupplyPool集成（2号位职责）
/// </summary>
public interface ITimedSupplyFactLoader
{
    /// <summary>
    /// 加载原始采购事实
    /// </summary>
    /// <param name="scope">供给事实范围（物料ID列表、工厂ID列表）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>原始采购事实列表</returns>
    Task<IReadOnlyList<RawProcurementFact>> LoadRawFactsAsync(
        SupplyFactScope scope,
        CancellationToken ct);
}
