namespace LPS.APS.Core.Dto;

/// <summary>
/// 标准化既存MES执行上下文（5号位 → 2号位，跨版本连续性专用）
///
/// 职责（v1.4 §8.5）：
/// 5号位结合 WorkOrder + OperationProgress + StageProgress + PI Position + NextOperationContext，
/// 输出2号位可直接消费的"该MES工单当前执行到哪里、下一步从哪里继续"上下文。
///
/// 消费方：2号位在当前逻辑生产需求（LogicalProductionDemand）形成后，做 Continuation 分桶：
///   - 该工单可承接的连续份额 = DerivedRemainingQty（受当前Q上限约束）
///   - 承接后进入1号位有限产能求解
///
/// 5号位不做：当前Q形成 / E<=Q分桶 / ContinuationKey/TaskNo身份 / FinalTask持久化。
///
/// 冻结红线：
/// - DerivedRemainingQty 是运行时派生结果，不是MES工单级原生字段
/// - 不要求也不允许在 MESWorkOrderSnapshot 新增 RemainingNetQty / RemainingQty / LastReportResourceCode
/// - WorkOrderStatus 沿用既有值：RELEASED / IN_PROGRESS / CLOSED / DELETED
/// - LastReportResourceCode 仅存在于工序级（OperationProgressSnapshot），不来自工单级
/// </summary>
public sealed class ExistingExecutionContextDto
{
    /// <summary>
    /// 生产指示号
    /// </summary>
    public string ProductionInstructionNo { get; init; } = string.Empty;

    /// <summary>
    /// 物料编码（该 MES 工单对应的物料）
    ///
    /// 来源：WorkOrderSnapshotFact.MaterialCode（2号位装载已填充，见 LoadWorkOrderSnapshotFactsAsync）
    /// 用途：2号位 消费 ExistingExecutionContext 时按物料身份区分；避免同 PI 下跨物料工单串味（T5-03）。
    /// ⚠️ 计算器内部可从 input.WorkOrders 还原，添加本字段是为让输出侧（Result 无顶层 MaterialId）可追溯物料身份。
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// MES工单号
    /// </summary>
    public string MESWorkOrderNo { get; init; } = string.Empty;

    /// <summary>
    /// 工单状态（冻结值域：RELEASED / IN_PROGRESS / CLOSED / DELETED）
    ///
    /// ⚠️ 禁止改名为 ExecutionStatus，禁止使用 NOT_STARTED / STARTED_INCOMPLETE / COMPLETED。
    /// </summary>
    public string WorkOrderStatus { get; init; } = string.Empty;

    /// <summary>
    /// 当前有效执行起点 Stage 代码
    ///
    /// 来源：PI Position 最前 Stage 或 OperationProgress 当前 Stage
    /// </summary>
    public string StartStageCode { get; init; } = string.Empty;

    /// <summary>
    /// 当前有效执行起点工序代码（可空：仅能确定到Stage级时为null）
    ///
    /// 来源：OperationProgress 最后报工工序的下一工序（如有OperationProgress数据）
    /// </summary>
    public string? StartOperationCode { get; init; }

    /// <summary>
    /// 当前有效执行起点工序名（兼容投影：N=1 时镜像唯一 Slice 的 StartOperationName；N>1 时为 null）
    ///
    /// 来源：Slices[].StartOperationName
    /// 用途：同 slice 级——DAG 前沿为空时供 2号位 按 名→码 反查；只装名、不装码。
    /// </summary>
    public string? StartOperationName { get; init; }

    /// <summary>
    /// 工单创建部门（APS ProductionDepartment.Id，可空）
    ///
    /// 来源：OperationProgress.ProductionDepartmentId
    ///       ← OperationProgressSnapshot.ProductionDepartmentId ← HolonCode → ProductionDepartment.SourceDeptCode
    /// 用途：路线定位的部门维度（与 RoutingOperation.ProductionDepartmentId 同空间）。
    /// ⚠️ 与 RoutingOperationFact 的部门（MaterialStageDeptContext 计划归属）可能是两条推导路径，
    ///    不一致时应视为需消解信号，不得静默取一侧。
    /// </summary>
    public int? ProductionDepartmentId { get; init; }

    /// <summary>
    /// 真实工艺路径编码（该工单实际要走的完整工序清单，逗号分隔工序名，非 "DEFAULT"）
    ///
    /// 来源：OperationProgress.RouteCode ← OperationProgressSnapshot.RouteCode
    ///       ← MES T_ProduceJoinTech_Data.ProNameGroup
    /// 语义：与 RoutingOperationFact.RouteCode 同形态；是工单实际路线，未必与 APS 标准路线逐字相同。
    /// 用途：定位该工单走哪条 Routing（与部门、Stage 共同锁定工艺路径）。
    /// </summary>
    public string RouteCode { get; init; } = string.Empty;

    /// <summary>
    /// 路径序号（与 RouteCode 成对；PathId 为 APS 内部概念，MES 侧无此维度）
    ///
    /// 说明：MES 的 RouteCode 是工序名串，表达不了 PathId；当同一 RouteCode 文本挂多个 PathId 时（C桶假歧义），
    ///      5号位 缺 Path 上下文，本字段可能为空或取默认值，由 2号位 结合 Routing 展开时消解。
    /// </summary>
    public int? PathId { get; init; }

    /// <summary>
    /// 运行时派生的剩余数量（DerivedRemainingQty）
    ///
    /// 来源：OperationProgressSnapshot.RemainingQty 或 StageProgressSnapshot.RemainingQty + PI Position
    /// 语义：该工单在当前有效执行起点上的剩余数量
    /// ⚠️ 这是运行时派生结果，不是MES工单级原生字段
    /// </summary>
    public decimal DerivedRemainingQty { get; init; }

    /// <summary>
    /// 工序级最后报工设备编码（可空，v5.1.6 新增）
    ///
    /// 来源：OperationProgressSnapshot.LastReportResourceCode
    /// 用途：资源连续性偏好事实（求解偏好，不自动形成Hard Lock）
    /// ⚠️ 只存在于工序级；禁止来自工单级
    /// </summary>
    public string? LastReportResourceCode { get; init; }

    /// <summary>
    /// 执行起点切片列表（一工单可多起点，接口 v1.35 §18.3 / 5号位实施包 v1.7）
    ///
    /// 5号位 只产「位置起点 + 执行剩余」事实（StartStageCode/StartOperationCode/SliceQty/LastReportResourceCode）；
    /// 求解字段（NetOutputQty/NoSplitMerge/PreferredResourceCode/EligibleResources 等）由 2号位 装配。
    /// 同一 MESWorkOrderNo 的所有 Slices 共享同一 ContinuationKey（2号位 生成）。
    ///
    /// 兼容口径：N=1 时本集合 1 个元素，与工单级单值字段（StartOperationCode/DerivedRemainingQty）镜像一致；
    /// N>1 时才出现多起点（数据源刷新后并行剩余场景）。旧单值字段保留作兼容投影。
    /// </summary>
    public IReadOnlyList<ExistingExecutionSliceDto> Slices { get; init; } = Array.Empty<ExistingExecutionSliceDto>();

    /// <summary>
    /// 数据截止时间（本次快照对应的DataCutoffTime）
    /// </summary>
    public DateTime DataCutoffTime { get; init; }

    /// <summary>
    /// 是否存在异常/降级（如工单状态与进度不一致等）
    /// </summary>
    public bool HasIssue { get; init; }

    /// <summary>
    /// 异常/降级描述（HasIssue=true时非空）
    /// </summary>
    public string? IssueDescription { get; init; }
}
