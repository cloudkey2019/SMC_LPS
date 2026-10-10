namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 任务表（分区表）
/// 对应 APS_Production.Task
/// v5.0变更：移除 ResourceGroupId（已废弃），保留 OperationSeq 用于向前兼容
/// </summary>
public class Task
{
    public long Id { get; set; }
    public int PlanVersionId { get; set; }
    public string TaskNo { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public int MaterialId { get; set; }

    /// <summary>
    /// 工序序号（向前兼容保留，新逻辑应使用 OperationCode + RoutingDependency 图模型）
    /// </summary>
    public int OperationSeq { get; set; }

    public string OperationCode { get; set; } = string.Empty;
    public int? ResourceId { get; set; }

    /// <summary>
    /// ⚠️ v5.0废弃，保留仅为DDL兼容（不再参与排程逻辑）
    /// </summary>
    [Obsolete("v5.0废弃，保留仅为DDL兼容")]
    public int? ResourceGroupId { get; set; }

    /// <summary>
    /// 工艺路径编码 —— **真实值**（冻结 DDL v5.1.8.3 + 红线 Q1：`DEFAULT` 不得作为归一化后的业务真值）。
    /// 来源 = 1号位 `FinalTaskDraft.RouteCode`（其自身取自 `RoutingOperation.RouteCode`）原样落库。
    /// null = 无路径身份（历史行 / 上游未给值），**不得回填 'DEFAULT'**。
    /// ⚠️ 2026-10-08 前此列在库里根本不存在，实体上的 `"DEFAULT"` 默认值只是内存假象。
    /// </summary>
    public string? RouteCode { get; set; }

    /// <summary>
    /// 路径序号 —— **真实值**（与 <see cref="RouteCode"/> 成对，红线 Q1：`1` 不得作为归一化后的业务真值）。
    /// 类型对齐 DDL `PathId INT NULL`；上游契约 `FinalTaskDraft.PathId` 为 `long?`，落库前做范围校验转换。
    /// null = 无路径身份（历史行 / 上游未给值），**不得回填 1**。
    /// </summary>
    public int? PathId { get; set; }

    /// <summary>
    /// MES 工单号（冻结 DDL v5.1.8.3）：A/B 桶 = 原 MES 工单；C 桶 = FinalTask 后新建的工单。
    /// 用途：TaskNo ↔ MES 执行身份追溯（T-002）。null = 尚未下发 / 无 MES 工单身份。
    /// </summary>
    public string? MESWorkOrderNo { get; set; }

    /// <summary>
    /// 真实生产部门（冻结 DDL v5.1.8.3 + DB-002）：FinalTask 实际落在哪个部门。
    /// 历史记录可 NULL 兼容。**不得由查询层反推**。
    /// </summary>
    public int? ProductionDepartmentId { get; set; }

    /// <summary>
    /// Stage 内 MES 执行批归组键（T-002/T-005）：同一 `StageExecutionBatchDraftKey` 的 N 条 Operation Task
    /// 共享同一个 <see cref="TaskNo"/>。null = 无归组键（历史行 / 1号位 未给值）。
    /// </summary>
    public string? StageExecutionBatchDraftKey { get; set; }

    /// <summary>
    /// Stage 内 MES 执行批数量（与 <see cref="StageExecutionBatchDraftKey"/> 成对）。
    /// </summary>
    public decimal? StageExecutionBatchQty { get; set; }

    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? PlannedEndTime { get; set; }
    public decimal? Duration { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsCriticalPath { get; set; }
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 生产指示号（v5.0.3：从Order冗余，避免反查）
    /// </summary>
    public string? MTS_InstructionNo { get; set; }

    /// <summary>
    /// 换型来源（唯一落库值 = 4 态：INITIAL_SETUP_STATE / EXACT / DEFAULT / SETUP_RULE_MISSING_ZERO_FALLBACK，
    /// 0号位 2026-09-22 裁决）。
    /// 实体列（非计算属性）；1号位 填充、2号位 原样落库不映射；禁止查询层反推。
    /// </summary>
    public string? SetupSource { get; set; }

    /// <summary>
    /// 大工艺阶段码（= RoutingOperation.StageCode，1号位 FinalTaskDraft.StageCode 透传）。
    /// 同一物料可跨多个大工艺（机加工/表面处理…），Task 落库必须带 StageCode，
    /// 否则下发 MES 时区分不出「这是哪个 Stage 的指示」（2026-09-21，TaskDispatch 引申缺口）。
    /// </summary>
    public string? StageCode { get; set; }

    /// <summary>
    /// 人工能力槽号（PM 0923 资源模型裁决）：人工能力槽在运行时投影为合成 ResourceId，结果落库时拆回——
    /// 存 ManualSlotId、ResourceId 置 NULL（不落负号/不落合成大数）。设备 Task 此列为 NULL。
    /// 逻辑 FK → ManualCapacitySlot.ManualSlotId。
    /// </summary>
    public int? ManualSlotId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
