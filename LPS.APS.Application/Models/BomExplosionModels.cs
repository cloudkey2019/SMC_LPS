using System.Collections.Generic;

namespace LPS.APS.Application.Models;

// ════════════════════════════════════════════════════════════════════════════
// BOM 结构展开（Explosion）纯函数层的输入/输出数据结构（2号位）。
//
// 这些是纯数据 record，不是 DI 服务——必须住在 LPS.APS.Application.Models（`Services`
// 命名空间之外），否则 ApplicationServiceExtensions 的 Scrutor 扫描会按 record 自动实现
// 的 IEquatable<T> 把它们误注册为 Scoped 服务，导致 DI 容器在 ValidateOnBuild 阶段
// 因「无法解析 String/Int64/Int32 构造函数参数」启动失败。
// ════════════════════════════════════════════════════════════════════════════

/// <summary>某一层的一条 BOM 子件边（单位配比）。</summary>
public sealed record BomComponent(string ChildCode, int ChildMaterialId, decimal QtyPerUnit);

/// <summary>BOM 结构快照（纯数据）：父件 → 子件单位向量；物料 → 是否采购件。</summary>
public sealed record BomStructure(
    IReadOnlyDictionary<string, IReadOnlyList<BomComponent>> ByParent,
    IReadOnlyDictionary<string, bool> IsPurchasedByMaterial);

/// <summary>成品订单的顶层需求（爆炸起点）。</summary>
public sealed record BomOrderDemand(
    long RootOrderId,
    int RootMaterialId,
    string RootMaterialCode,
    int FactoryId,
    decimal RootQty);

/// <summary>逐路径毛需求行。Path 从根开始以 '/' 连接的物料码序列，是「需求血缘」的最小载体。</summary>
public sealed record BomExplosionLine(
    long RootOrderId,
    int MaterialId,
    string MaterialCode,
    int FactoryId,
    decimal GrossQty,
    int Level,
    string Path,
    bool IsPurchased,
    bool IsLeaf);

/// <summary>去重结构中「父件 → 本物料」的一条边（配比=父件单位需求下本物料用量，血缘载体）。</summary>
public sealed record BomParentEdge(int ParentMaterialId, string ParentMaterialCode, decimal QtyPerUnit);

/// <summary>
/// 去重逐层结构节点：本订单内同一物料只一行。Level=该物料最深出现层（LLC 语义），
/// ParentEdges=本订单爆炸中命中的全部父边（共享子件 D 经 B/C → 两父边，路径不丢）。
/// </summary>
public sealed record BomLevelNode(
    int MaterialId,
    string MaterialCode,
    int FactoryId,
    int Level,
    bool IsPurchased,
    IReadOnlyList<BomParentEdge> ParentEdges);