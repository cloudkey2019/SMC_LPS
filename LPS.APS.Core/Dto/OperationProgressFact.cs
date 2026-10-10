namespace LPS.APS.Core.Dto;

/// <summary>
/// 工序进度事实（2号位提供给5号位的输入数据）
///
/// 来源：OperationProgressSnapshot表
/// V1工序识别主字段：OperationName（MES工序名称）
/// </summary>
public sealed class OperationProgressFact
{
    /// <summary>
    /// 工序代码（MES工序编码，不跨大工艺稳定，仅作辅助）
    /// </summary>
    public string OperationCode { get; init; } = string.Empty;

    /// <summary>
    /// 工序名称（MES工序名称，V1工序识别主字段）
    /// </summary>
    public string OperationName { get; init; } = string.Empty;

    /// <summary>
    /// 所属Stage代码
    /// </summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>
    /// 该 MES 工单的「完整计划工序清单」（逗号分隔的工序名，已剥 MES 内部 (瓶颈) 标记）
    ///
    /// 来源：OperationProgressSnapshot.RouteCode
    ///       ← MES SMC_MOM_Technology.T_ProduceJoinTech_Data.ProNameGroup
    /// 语义：与 RoutingOperationFact.RouteCode **同形态** —— 两者都是「工序名逗号串」，非 "DEFAULT"。
    /// ⚠️ 但这是**该工单实际要走的工序清单**，未必与 APS 存的标准路线逐字相同
    ///    （MES 有临时手工建工艺的功能，进不了工艺库）。
    /// ⚠️ 下方这组数已作废，勿再引用：2026-09-30 5号位 把承接口径改为
    ///    **RouteCode 完全等值 + 部门并入匹配键 + 不剥「配品,」前缀**（见
    ///    Database/Scripts/APS/02_派单与回执/5号位致2号位_工序身份Routing承接回执_等值匹配口径_20260930(1).md），
    ///    旧数是在「子集匹配 + 剥配品」口径下测的，两者不可比。
    ///    【旧口径实测（2026-09-30，CN_MACH）：等值约 46%；子集匹配唯一 60.2% / 零命中 39.8% / 歧义 0】
    ///    ⇒ 新口径下的 A(零命中)/B(唯一)/C(同文本多 PathId) 三桶重测中，出数后回填本注释。
    ///    ⚠️ 但一条不变：下游**不得静默取首条**（零命中即映射失败，须置 Issue）。
    /// 用途：回答「该工单还剩哪几道工序」——已报工行只覆盖**已开工**工序，
    ///       未开工工序在快照里没有行；本字段补上了整条清单。
    /// </summary>
    public string RouteCode { get; init; } = string.Empty;

    /// <summary>
    /// 该工单的「工单创建部门」（APS ProductionDepartment.Id，可空）
    ///
    /// 来源：OperationProgressSnapshot.ProductionDepartmentId
    ///       ← MES 工单创建表 T_ProduceData_Input.NewCode → ProductionDepartment.SourceDeptCode
    ///         （实测命中 100%；取创建源头而非开工表 OrgId —— 同一工单可多部门开工，OrgId 会漂）
    /// ⚠️ **与 RoutingOperationFact 的部门不是同一条推导路径**：后者来自
    ///    MaterialStageDeptContext((MaterialId, StageCode))，本字段来自 MES 工单创建源头。
    ///    **两者可能不一致**；不一致应视为需要消解的信号，**不得静默取一侧**。
    /// 用途：路线定位的部门维度（与被定位的 APS RouteCode 同属 (物料, 部门) 空间）。
    /// </summary>
    public int? ProductionDepartmentId { get; init; }

    /// <summary>
    /// MES工单号（同一PI可1:N分批）
    ///
    /// 来源：OperationProgressSnapshot.MESWorkOrderNo
    /// 用途：5号位 ExistingExecutionContext 按工单独立输出连续份额，不合并多工单。
    /// </summary>
    public string MESWorkOrderNo { get; init; } = string.Empty;

    /// <summary>
    /// 工序计划数量
    ///
    /// 来源：OperationProgressSnapshot.PlannedQty
    /// 用途：DerivedRemainingQty = max(PlannedQty - GoodQty, 0) 运行时派生。
    /// </summary>
    public decimal PlannedQty { get; init; }

    /// <summary>
    /// 工序累计良品完成数量（GoodQty，冻结字段名，不得改名）
    ///
    /// 来源：OperationProgressSnapshot.GoodQty
    /// 5号位 PI Position/NextOperationContext 计算器消费此字段算执行起点。
    /// </summary>
    public decimal GoodQty { get; init; }

    /// <summary>
    /// 工序剩余数量（= max(PlannedQty - GoodQty, 0)）
    ///
    /// 来源：OperationProgressSnapshot.RemainingQty（PERSISTED 计算列）
    /// 用途：5号位 ExistingExecutionContext 逐工单 DerivedRemainingQty 派生。
    /// ⚠️ 只存在于进度快照层，不新增到工单级（28-0 v2.6 §十七 冻结）。
    /// </summary>
    public decimal RemainingQty { get; init; }

    /// <summary>
    /// 工序序号
    /// </summary>
    public int OperationSequence { get; init; }

    /// <summary>
    /// 最后报工时间
    ///
    /// 来源：OperationProgressSnapshot.LastReportTime
    /// 用途：资源连续性辅助参考（不能当"当前工序"判据——各工序都会报工，补报/返工会污染）。
    /// </summary>
    public DateTime? LastReportTime { get; init; }

    /// <summary>
    /// 工序级最后报工设备编码（v5.1.6 新增，DDL 已冻结）
    ///
    /// 来源：OperationProgressSnapshot.LastReportResourceCode
    /// 含义：同一MES工单该工序在DataCutoffTime前最后一次有效报工使用的设备。
    /// 用途：资源连续性偏好事实（求解偏好，不自动形成Hard Lock）。
    /// ⚠️ 只存在于工序级；禁止加入 MES_APS_WorkOrder_View / MESWorkOrderSnapshot（v1.6 裁决 §15）。
    /// </summary>
    public string? LastReportResourceCode { get; init; }

    /// <summary>
    /// 数据来源快照ID
    /// </summary>
    public long? SnapshotId { get; init; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
