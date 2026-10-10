using System;
using System.Collections.Generic;
using System.Linq;
using LPS.APS.Application.Models;

namespace LPS.APS.Application.Services;

// ════════════════════════════════════════════════════════════════════════════
// BOM 结构展开（Explosion）纯函数层 —— 阶段1 / 阶段2 共用的第一段
//
// PM 0918-1 / 0918-2 裁决：把「BOM 结构展开（纯、可缓存）」从「供给承接（Allocation，
// 有状态、不可缓存）」里拆出来。本服务只回答「这个需求沿某条 BOM 路径需要哪些下层物料、
// 各要多少」—— 与订单优先级无关、与 Supply 状态无关、与 Allocation 顺序无关。
//
// 当前实现 = 阶段1：
//   * 产出「逐路径毛需求行」（共享子件 D 经 B/C 两路径 → 两行，各带 Path 标识完整血缘）；
//   * 不净额（netting）、不合并 —— 合并数量保留血缘是阶段2 Allocation 层的事；
//   * 环检测 = 路径级 visited（与旧 TraverseBomNode 同语义，只防真实环，不阻断共享展开）。
//
// 缓存边界（阶段2 接线时）：
//   * 可缓存单元 = GetUnitVector（每物料一层展开）；缓存键 = MaterialId + FactoryId +
//     BOMVersion + EffectiveDate（PM 0918-2 §七）。
//   * 本服务保持无状态；缓存由调用方按 BOM 版本建一次 BomStructure 复用实现。
// ════════════════════════════════════════════════════════════════════════════

/// <summary>BOM 结构展开纯函数层。</summary>
public interface IBomExplosionService
{
    /// <summary>单位需求向量：某物料往下一层展开的 (子件, 单位配比)。可缓存的最小单元。</summary>
    IReadOnlyList<BomComponent> GetUnitVector(BomStructure structure, string materialCode);

    /// <summary>单订单毛需求爆炸：根需求 × 单位配比递推，产出逐路径需求行（不净额、不合并）。</summary>
    IReadOnlyList<BomExplosionLine> ExplodeOrder(BomStructure structure, BomOrderDemand root);

    /// <summary>
    /// 去重逐层结构（阶段2 Allocation 骨架）：共享子件合并为一行带父边列表，每物料取其最深出现层
    /// （LLC 语义，保证所有父件均已先净额），采购件即叶（对齐旧 TraverseBomNode 不下钻）。
    /// </summary>
    IReadOnlyList<BomLevelNode> ExplodeOrderStructure(BomStructure structure, BomOrderDemand root);
}

/// <summary>BOM 结构展开纯函数层实现（无状态、无副作用）。</summary>
public sealed class BomExplosionService : IBomExplosionService
{
    public IReadOnlyList<BomComponent> GetUnitVector(BomStructure structure, string materialCode)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(materialCode);
        return structure.ByParent.TryGetValue(materialCode, out var children)
            ? children
            : Array.Empty<BomComponent>();
    }

    public IReadOnlyList<BomExplosionLine> ExplodeOrder(BomStructure structure, BomOrderDemand root)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(root);

        var lines = new List<BomExplosionLine>();
        // 路径级环检测：与旧 TraverseBomNode 同语义——只防 A→B→A 真实环；共享子件 D 经 B/C
        // 两路径时（D 随各自路径的 visited 进入/退出），仍各产一行，血缘不丢。
        var visited = new HashSet<string>(StringComparer.Ordinal);

        Walk(root.RootMaterialId, root.RootMaterialCode, root.RootQty, level: 0, path: root.RootMaterialCode);
        return lines;

        void Walk(int materialId, string materialCode, decimal grossQty, int level, string path)
        {
            if (!visited.Add(materialCode)) return; // 真实环，截断，不产出

            try
            {
                var isPurchased = structure.IsPurchasedByMaterial.TryGetValue(materialCode, out var purchased) && purchased;
                var children = GetUnitVector(structure, materialCode);

                lines.Add(new BomExplosionLine(
                    RootOrderId: root.RootOrderId,
                    MaterialId: materialId,
                    MaterialCode: materialCode,
                    FactoryId: root.FactoryId,
                    GrossQty: grossQty,
                    Level: level,
                    Path: path,
                    IsPurchased: isPurchased,
                    IsLeaf: children.Count == 0));

                foreach (var child in children)
                {
                    Walk(
                        child.ChildMaterialId,
                        child.ChildCode,
                        grossQty * child.QtyPerUnit,
                        level + 1,
                        path + "/" + child.ChildCode);
                }
            }
            finally
            {
                visited.Remove(materialCode);
            }
        }
    }

    public IReadOnlyList<BomLevelNode> ExplodeOrderStructure(BomStructure structure, BomOrderDemand root)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(root);

        // 两段式（LLC 自底向上记忆化）：把旧「路径级 visited」导致的共享子件整棵子树重走
        // （12× 重复展开）压成每物料子件只枚举一次，且最深层（LLC）不受展开顺序影响。
        //
        // 阶段① 结构发现：expanded 记忆化「该物料子件已完整枚举」，onPath 只拦真实环
        // back-edge（A→B→A 的 B→A 不记父边、不抬层）；父边按 child 累加全集。
        // 阶段② 最深层（LLC 语义）：不再随 DFS 路径抬 level，而是对①产出的 DAG 做单源
        // 最长路径拓扑弛豫——天然排除被截断的环边，且共享子件更深路径自动下推到子件。
        // 两段均 O(节点 + 边)。

        var idByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        var isPurchasedByCode = new Dictionary<string, bool>(StringComparer.Ordinal);
        var parentEdgesByCode = new Dictionary<string, Dictionary<int, BomParentEdge>>(StringComparer.Ordinal);

        var expanded = new HashSet<string>(StringComparer.Ordinal); // 子件已完整枚举（记忆化）
        var onPath = new HashSet<string>(StringComparer.Ordinal);   // 当前 DFS 路径（拦真实环）

        Discover(root.RootMaterialCode, root.RootMaterialId, parentCode: "", parentId: 0, qtyPerUnit: 0m);

        // 阶段② 最深层（LLC）拓扑弛豫：level[child] = max(level[parent] + 1)。
        var levelByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in idByCode.Keys) levelByCode[code] = 0;

        // child → 未处理父件数；parent → 子件列表（均只含①已记的边，环边已被①排除）。
        var remainingParents = new Dictionary<string, int>(StringComparer.Ordinal);
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (child, edgeMap) in parentEdgesByCode)
        {
            remainingParents[child] = edgeMap.Count;
            foreach (var edge in edgeMap.Values)
            {
                if (!childrenByParent.TryGetValue(edge.ParentMaterialCode, out var list))
                    childrenByParent[edge.ParentMaterialCode] = list = new List<string>();
                list.Add(child);
            }
        }

        var queue = new Queue<string>(idByCode.Keys.Where(c => !remainingParents.ContainsKey(c)));
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!childrenByParent.TryGetValue(cur, out var children)) continue;

            foreach (var child in children)
            {
                var candidate = levelByCode[cur] + 1;
                if (candidate > levelByCode[child]) levelByCode[child] = candidate;
                if (--remainingParents[child] == 0) queue.Enqueue(child);
            }
        }

        return levelByCode
            .Select(kv => new BomLevelNode(
                MaterialId: idByCode[kv.Key],
                MaterialCode: kv.Key,
                FactoryId: root.FactoryId,
                Level: kv.Value,
                IsPurchased: isPurchasedByCode[kv.Key],
                ParentEdges: parentEdgesByCode.TryGetValue(kv.Key, out var edgeMap)
                    ? (IReadOnlyList<BomParentEdge>)edgeMap.Values.OrderBy(e => e.ParentMaterialId).ToList()
                    : Array.Empty<BomParentEdge>()))
            .OrderBy(n => n.Level)
            .ThenBy(n => n.MaterialCode, StringComparer.Ordinal)
            .ToList();

        void Discover(string code, int id, string parentCode, int parentId, decimal qtyPerUnit)
        {
            if (onPath.Contains(code)) return; // 真实环 back-edge：不记父边、不展开、不抬层

            // 父边去重（同一父件对同一子件只一条 BOM 边）；root（parentId=0）不记父边。
            if (parentId != 0)
            {
                if (!parentEdgesByCode.TryGetValue(code, out var edgeMap))
                    parentEdgesByCode[code] = edgeMap = new Dictionary<int, BomParentEdge>();
                if (!edgeMap.ContainsKey(parentId))
                    edgeMap[parentId] = new BomParentEdge(parentId, parentCode, qtyPerUnit);
            }

            if (expanded.Contains(code)) return; // 子树已完整枚举：只补记上方父边，不再重走子件

            idByCode[code] = id;
            isPurchasedByCode[code] = structure.IsPurchasedByMaterial.TryGetValue(code, out var purchased) && purchased;
            expanded.Add(code);

            if (isPurchasedByCode[code]) return; // 采购件即叶，不下钻（对齐旧 TraverseBomNode）

            onPath.Add(code);
            foreach (var child in GetUnitVector(structure, code))
                Discover(child.ChildCode, child.ChildMaterialId, code, id, child.QtyPerUnit);
            onPath.Remove(code);
        }
    }
}