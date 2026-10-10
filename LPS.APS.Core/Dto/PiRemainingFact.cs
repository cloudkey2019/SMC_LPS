namespace LPS.APS.Core.Dto;

/// <summary>
/// PI 权威剩余事实（T2-01，2026-10-09；口径由用户 2026-10-09 定盘）。
///
/// 来源：APS <c>[Order]</c> ∪ 本域 BOM 可达物料的订单链（<c>Order_Canonical</c>）中
/// <c>MTS_InstructionNo IS NOT NULL</c> 的行，**一个 PI 一条**。
/// 与 Stage / MES 工单 / PI Position **完全解耦**——真实 PI 即使当前没有任何 Stage 报工行，
/// 也必须在这里出现（PM T2-04.2）。
///
/// ⚠️ 为什么不能只读 <c>[Order]</c>：<c>[Order]</c> 装载带产品族闸门（<c>m.ProductFamilyId = @ProductFamilyId</c>），
/// 而物料归族规则只覆盖**成品**工艺码 ⇒ 下阶加工件物料的 PI 事实全被挡在门外，供给池在下阶恒空
/// （实测 PV2：<c>[Order]</c> 22,144 行 / 9,857 物料 100% 是 <c>FINAL_FG-*</c>、0 个是 BOM 子件）。
/// 故下阶 PI 按本域物料宇宙从 <c>Order_Canonical</c> 补取，见 <c>PeggingOrchestrator.LoadPiRemainingFactsAsync</c>。
///
/// 数量口径（唯一权威，全链只此一份）：
/// <code>PiRemainingQty = max(PiQuantity - ISNULL(PiReceivedQty, 0), 0)</code>
///
/// 其中 <c>PiQuantity</c> = <c>[Order].Quantity</c>（毛量），<c>PiReceivedQty</c> = <c>[Order].ReceivedQty</c>（已完工入库量）。
/// 两列在整条链路上（ODS 视图 → ERPOrderSyncService → ERP_Order_Staging → sp_ValidateAndPromoteOrders →
/// Order_Canonical → sp_SyncOrdersToPartitionTable → [Order]）都是**原样透传、零运算**，
/// 因此本类是「先做减法」的那一步，不是第二来源。
///
/// ⛔ 不得再由 <c>StageProgressSnapshot</c> 的 <c>MAX/SUM(RemainingQty)</c> 充当 PI 总量（PM T2-04.1）。
/// ⛔ 不得用客户订单的 <c>Quantity</c> 去顶没有真实 PI 的行（PM T2-01.1/T2-03.3）——那类行
///    <c>MTS_InstructionNo IS NULL</c>，**不进本事实集**，走 T2-03 无 PI 缺口路径。
/// </summary>
public sealed class PiRemainingFact
{
    /// <summary>生产指示号（= <c>[Order].MTS_InstructionNo</c>，PI 的唯一物理身份）。</summary>
    public string ProductionInstructionNo { get; init; } = string.Empty;

    /// <summary>物料ID（<c>[Order].MaterialId</c>，权威身份）。</summary>
    public int MaterialId { get; init; }

    /// <summary>物料编码。</summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>工厂ID（<c>[Order].FactoryId</c>）。</summary>
    public int FactoryId { get; init; }

    /// <summary>工厂编码。</summary>
    public string FactoryCode { get; init; } = string.Empty;

    /// <summary>PI 原始量（<c>[Order].Quantity</c>，毛量；生产完工不使其变小）。</summary>
    public decimal PiQuantity { get; init; }

    /// <summary>PI 已完工入库量（<c>[Order].ReceivedQty</c>；NULL 表示来源缺失，见 <see cref="HasMissingReceivedQty"/>）。</summary>
    public decimal? PiReceivedQty { get; init; }

    /// <summary>PI 权威剩余量 = <c>max(PiQuantity − ISNULL(PiReceivedQty,0), 0)</c>。全链唯一总量口径。</summary>
    public decimal PiRemainingQty { get; init; }

    /// <summary>承载该 PI 的 <c>[Order]</c> 行 Id（追溯用）。</summary>
    /// <remarks>
    /// 来源若是 <c>Order_Canonical</c> 补取行（下阶加工件 PI，<c>[Order]</c> 因产品族闸门未装载它们）
    /// 则恒 0 —— 那类行没有 <c>[Order]</c> 承载行，追溯请用 <see cref="OrderCanonicalId"/>。
    /// </remarks>
    public long OrderId { get; init; }

    /// <summary>
    /// 承载该 PI 的 <c>Order_Canonical.Id</c>（两种来源都有，追溯与 T2-01.5 缺口样本核验用）。
    /// </summary>
    public long OrderCanonicalId { get; init; }

    /// <summary>APS 标准订单类型（<c>SALES_ORDER</c> / <c>PRODUCTION_INSTRUCTION</c>）。</summary>
    public string OrderType { get; init; } = string.Empty;

    /// <summary>客户交期（可空）。</summary>
    public DateTime? DueDate { get; init; }

    /// <summary>
    /// PI 下达日期（= <c>[Order].IssueDate</c>）。供冻结 <c>SupplyBlock.PiSort.SortBy=IssueDateAsc</c> 的
    /// PI 内排序使用（T2-02.1）；缺失时排序降级为按 PI 号稳定兜底，不引入随机序。
    /// </summary>
    public DateTime? IssueDate { get; init; }

    /// <summary>来源 <c>ReceivedQty</c> 缺失（NULL）——已按 0 参与公式，但必须留痕，不得静默（PM T2-01.2）。</summary>
    public bool HasMissingReceivedQty { get; init; }

    /// <summary>来源 <c>ReceivedQty &gt; Quantity</c>（超量）——剩余已 clamp 到 0，必须留痕（PM T2-01.2）。</summary>
    public bool HasOverReceivedQty { get; init; }
}
