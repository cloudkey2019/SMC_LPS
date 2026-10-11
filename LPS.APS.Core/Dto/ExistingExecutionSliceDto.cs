namespace LPS.APS.Core.Dto;

/// <summary>
/// 既存MES执行上下文 的单个执行起点切片（5号位 → 2号位）
///
/// 职责（接口 v1.35 §18.3 / 5号位实施包 v1.7）：
/// 一个 MES 工单可同时处于多道工序的不同进度（真并行），`ExistingExecutionContextDto.Slices[]`
/// 承载每个「执行起点切片」——从哪道 Stage/Operation 继续、多少数量、该工序最后报工设备。
///
/// 同一 MESWorkOrderNo 的所有 Slices 共享同一个 ContinuationKey（由2号位生成，5号位不产）。
///
/// 字段边界（各改各的）：
/// - 本 DTO 只放 5号位 解释出的「位置起点 + 执行剩余」事实：StartStageCode / StartOperationCode / SliceQty / LastReportResourceCode。
/// - 求解字段（NetOutputQty / PlannedProcessQty / NoSplitMerge / PreferredResourceCode / EligibleResources /
///   Material Quantity-Time 等）由 2号位 按接口 §18.3 装配，不在本 DTO。
///
/// ⚠️ SliceQty 语义（T5-02 澄清）：是该起点「需继续生产的 MES 执行剩余量」，与 PI 位置量（NextOperationContext.SliceQty）
///    不同维度；不得直接当作 C 桶可新增 MES 工单量 / Free 上限，二者可能重叠、须由 2号位 以权威 PI 剩余统一闭合。
/// </summary>
public sealed class ExistingExecutionSliceDto
{
    /// <summary>
    /// 该执行起点所在 Stage 代码
    ///
    /// 来源：PI Position 或 OperationProgress 当前 Stage
    /// </summary>
    public string StartStageCode { get; init; } = string.Empty;

    /// <summary>
    /// 该执行起点工序代码（APS Routing OperationCode；可空：仅能确定到 Stage 级时为 null）
    ///
    /// 来源：DAG 拓扑前沿 / OperationProgress 已开工未完工工序 → 反查 RoutingOperation.OperationCode
    /// ⚠️ 必须为 APS Routing 的 OperationCode（非 OperationName）；1号位入口按码查。
    /// </summary>
    public string? StartOperationCode { get; init; }

    /// <summary>
    /// 该执行起点工序名（MES 工序名；可空）
    ///
    /// 用途：DAG 拓扑前沿为空（如"前道完工、后道未开工"——唯一未完工工序 GoodQty=0 不构成前沿）时，
    ///      StartOperationCode 为 null，消费方（2号位）按「该 PI 路由集内 名→码 + 本工单 RouteCode 消歧」反查。
    /// ⚠️ 只装工序名，不得在此填码；取不到就留 null。
    /// </summary>
    public string? StartOperationName { get; init; }

    /// <summary>
    /// 该执行起点要继续生产的数量（MES 执行剩余切片的份额）
    ///
    /// 来源：OperationProgress.RemainingQty 或 StageProgress 比例拆分
    /// ⚠️ 是该工单该起点的执行剩余量，不等价 PI 位置量；不得直接当 C 桶 Free 上限（见类注释 T5-02）。
    /// </summary>
    public decimal SliceQty { get; init; }

    /// <summary>
    /// 该工序最后报工设备编码（可空）
    ///
    /// 来源：OperationProgressSnapshot.LastReportResourceCode
    /// 用途：资源连续性偏好原始事实（5号位 只给事实，2号位 校验 Eligibility 后形成 PreferredResourceCode）
    /// </summary>
    public string? LastReportResourceCode { get; init; }

    /// <summary>
    /// 该切片的降级/歧义标记（可空）
    ///
    /// 例：NEXT_OPERATION_AMBIGUOUS / UNLOCATED_STAGE 等
    /// </summary>
    public string? IssueCode { get; init; }
}