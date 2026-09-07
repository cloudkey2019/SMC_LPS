using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 生产指示位置计算器接口（5号位核心能力）
///
/// 职责边界：
///   - 接收2号位装载好的完整事实包（ProductionInstructionPositionInput）
///   - 进行纯计算：Stage差分、XC/Transit互斥、UNLOCATED、总量闭合、Issue生成
///   - 返回Position结果（ProductionInstructionPositionResult）
///   - 不访问数据库，不注入Repository
///   - 不决定PI最终分配给哪个Demand（由2号位负责）
///
/// 设计原则：
///   - DTO进、Result出，纯计算逻辑
///   - 2号位负责数据装载和DataCutoffTime一致性
///   - 5号位只负责复杂位置判断
/// </summary>
public interface IProductionInstructionPositionCalculator
{
    /// <summary>
    /// 批量计算生产指示位置
    ///
    /// 对每个PI必须保证：Σ PositionQty = ErpRemainingQty（总量闭合）
    /// Position之间必须互斥（同一物理份额不能同时算在Stage和XC）
    /// </summary>
    /// <param name="inputs">PI位置计算输入列表（2号位已装载完整事实包）</param>
    /// <param name="parameters">冻结事实参数（3号位Snapshot → 2号位装载 → 5号位消费）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>PI位置计算结果列表</returns>
    Task<IReadOnlyList<ProductionInstructionPositionResult>> CalculateProductionInstructionPositionsAsync(
        IReadOnlyList<ProductionInstructionPositionInput> inputs,
        FrozenFactParameters parameters,
        CancellationToken cancellationToken = default);
}
