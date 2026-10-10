namespace LPS.APS.Scheduling.Algorithms;

/// <summary>
/// 拓扑排序（Kahn 算法）
/// 用于确定产品族域的执行顺序（哪个域先排、哪个域后排）
///
/// 【真实调用方：3号位】本类由 3号位在 Application 层使用
/// （LPS.APS.Application/Services/SchedulingOrchestrator.cs:931 调用 SortByLayers&lt;string&gt;），
/// 用于域(domain)级别的拓扑分层，不属于 1号位五阶段流程的调用链。
///
/// ⚠️ 注意：1号位自己的跨物料时序 DAG 分层（父子物料，子先父后）**不使用**本类，
/// 而是在 PhaseOneConstraintBuilder.BuildCrossMaterialDag（Line 230）内联实现了一份 Kahn。
/// 两者用途不同：本类排"域"，1号位排"物料"，请勿混淆。
/// </summary>
public static class TopologicalSort
{
    /// <summary>
    /// 对有向无环图(DAG)执行拓扑排序，返回分层执行顺序
    /// </summary>
    /// <param name="nodes">所有节点</param>
    /// <param name="edges">有向边集合（from → to 表示 from 必须在 to 之前执行）</param>
    /// <returns>分层结果：Layer 0 无依赖可先跑，Layer 1 依赖 Layer 0，以此类推</returns>
    /// <exception cref="InvalidOperationException">检测到循环依赖时抛出</exception>
    public static List<List<T>> SortByLayers<T>(
        IReadOnlyList<T> nodes,
        IReadOnlyList<(T From, T To)> edges) where T : notnull
    {
        var inDegree = new Dictionary<T, int>();
        var adjacency = new Dictionary<T, List<T>>();

        foreach (var node in nodes)
        {
            inDegree[node] = 0;
            adjacency[node] = new List<T>();
        }

        foreach (var (from, to) in edges)
        {
            adjacency[from].Add(to);
            inDegree[to]++;
        }

        var layers = new List<List<T>>();
        var queue = new Queue<T>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        int processed = 0;

        while (queue.Count > 0)
        {
            var currentLayer = new List<T>();
            int layerSize = queue.Count;

            for (int i = 0; i < layerSize; i++)
            {
                var node = queue.Dequeue();
                currentLayer.Add(node);
                processed++;

                foreach (var neighbor in adjacency[node])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            layers.Add(currentLayer);
        }

        if (processed != nodes.Count)
            throw new InvalidOperationException(
                $"检测到循环依赖：{nodes.Count} 个节点中只有 {processed} 个可排序。" +
                "请检查 DomainDependency 表是否存在环路。");

        return layers;
    }

    /// <summary>
    /// 扁平化拓扑排序（不分层）
    /// </summary>
    public static List<T> Sort<T>(
        IReadOnlyList<T> nodes,
        IReadOnlyList<(T From, T To)> edges) where T : notnull
    {
        var layers = SortByLayers(nodes, edges);
        var result = new List<T>(nodes.Count);
        foreach (var layer in layers)
            result.AddRange(layer);
        return result;
    }
}
