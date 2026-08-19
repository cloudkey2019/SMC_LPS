using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.BusinessRules.Rules.Splitting;

/// <summary>
/// V1 朴素拆批实现（5号位占位，供端到端流程打通用）
///
/// 规则：
///   1. 每个 Order × RoutingOperation → 一个 Task（不切批、不合批）
///   2. DurationMinutes = StandardDuration × Quantity + SetupTime
///   3. 资源指派取 OperationResourceEligibility 中 Priority 最小的首选资源
///   4. OperationSeq 按 OperationCode 字典序排序（等 5号位接入真实顺序规则后替换）
///
/// ⚠️ 5号位接入时需替换的业务规则：
///   - 批量切分策略（MOQ、经济批量、工厂产能上限）
///   - 多路径选择（MTO vs MTS / 正品 vs 返工路径）
///   - 关键工序合批（串联合并、并联拆分）
///   - 换型优先资源指派（根据换型矩阵动态择机）
///   - 双源互斥下的 ResourceId 选择（参见 InventorySourceRule）
/// </summary>
public class DefaultBatchSplitter : IBatchSplitter
{
    private readonly ILogger<DefaultBatchSplitter> _logger;

    public DefaultBatchSplitter(ILogger<DefaultBatchSplitter> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<TaskSpec> Split(BatchSplitInput input)
    {
        if (input.Orders.Count == 0) return Array.Empty<TaskSpec>();

        // (MaterialId, OperationCode) → 首选 ResourceId
        var defaultResource = input.Eligibilities
            .GroupBy(e => (e.MaterialId, e.OperationCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Priority).ThenBy(x => x.ResourceId).First().ResourceId);

        var opsByMaterial = input.Operations
            .GroupBy(o => o.MaterialId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.OperationSeq).ToList());

        var result = new List<TaskSpec>();
        foreach (var order in input.Orders)
        {
            if (!opsByMaterial.TryGetValue(order.MaterialId, out var ops) || ops.Count == 0)
            {
                _logger.LogWarning(
                    "Order {OrderNo} 物料 {MaterialCode} 无工艺路线，跳过拆批",
                    order.OrderNo, order.MaterialCode);
                continue;
            }

            foreach (var op in ops)
            {
                var resId = defaultResource.TryGetValue((order.MaterialId, op.OperationCode), out var r)
                    ? (int?)r : null;

                var duration = op.StandardDuration * order.Quantity + op.SetupTime;

                result.Add(new TaskSpec
                {
                    TaskNo          = $"T-{input.PlanVersionId}-{order.OrderId}-{op.OperationSeq:D3}",
                    OrderId         = order.OrderId,
                    MaterialId      = order.MaterialId,
                    OperationSeq    = op.OperationSeq,
                    OperationCode   = op.OperationCode,
                    OperationName   = op.OperationName,
                    ResourceId      = resId,
                    RouteCode       = "DEFAULT",
                    PathId          = 1,
                    Quantity        = order.Quantity,
                    UOM             = order.UOM,
                    DurationMinutes = duration,
                    TaskType        = "PRODUCTION",
                    OrderPriority   = order.Priority,
                    CustomerDueDate = order.CustomerDueDate
                });
            }
        }

        _logger.LogInformation(
            "DefaultBatchSplitter: 订单={OrderCount}, 生成 Task={TaskCount}",
            input.Orders.Count, result.Count);

        return result;
    }
}
