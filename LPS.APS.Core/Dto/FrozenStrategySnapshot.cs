namespace LPS.APS.Core.Dto;

/// <summary>
/// 一次 Run 启动时的完整冻结配置（不可变，Run 内不刷新）
/// 2↔3 接口传输 DTO（契约 v0.2 §二；阶段 B 实现依据）。
/// 冻结语义（C2-1/C2-2）：按 Run 已冻结的指定 StrategyProfileVersionId 装载一次，
/// 本 Run 所有 Domain 使用同一份 Snapshot，不因后续新版本发布/停用而漂移。
/// 六块为冻结文档 4.2 三块（Demand排序 / Supply-Lock / Solver策略）的细拆（C2-5 确认）。
/// 不提供 ToFrozenFactParameters()（C2-6：2号位在集成层从 Snapshot 抽取并转交 5号位）。
/// </summary>
public sealed class FrozenStrategySnapshot
{
    public long StrategyProfileVersionId { get; set; }        // 冻结锚点（C2-1：Run 绑定后按此获取，不再漂移）
    public long RuleSetVersionId { get; set; }                // 同一包内（C2-2）
    public long ParameterSetVersionId { get; set; }           // 同一包内（C2-2）
    public DateTime FrozenAt { get; set; }                    // 冻结时点（Run 启动）

    public DemandPriorityBlock DemandPriority { get; set; } = new();  // ① Demand 排序
    public LockBlock Lock { get; set; } = new();                        // ② Demand Protection / 锁定
    public SupplyBlock Supply { get; set; } = new();                    // ③ Inventory Availability + PI 排序
    public ProcurementBlock Procurement { get; set; } = new();          // ④ 采购/良率参数（含 PlanningYield，C2-5）
    public SolverStrategyBlock SolverStrategy { get; set; } = new();    // ⑤ Solver 策略
    public CandidateGuardrailBlock CandidateGuardrail { get; set; } = new(); // ⑥ Candidate 技术 Guardrail
    public List<SetupTransitionRuleSnapshot> SetupTransitionRules { get; set; } = []; // ⑦ 产品转换换型规则本 Run 冻结内容（v1.2 §九/§十；0号位 裁决项2）
    public List<BatchPolicyRuleSnapshot> BatchPolicy { get; set; } = []; // ⑧ 批量策略（Batch Policy）本 Run 冻结内容（0号位 2026-10-07 裁决：本轮落码；Material+ProductionDepartment 粒度）
}

/// <summary>
/// ① Demand 排序（清单 14~17 / 冻结 4.2-Demand排序）
/// 红线：无全局 PriorityScore；第一命中即止，命中后不再匹配后续 Segment。
/// </summary>
public sealed class DemandPriorityBlock
{
    /// <summary>有序 Segment 列表，按 SegmentOrder 升序；第一命中即止（清单 16）</summary>
    public List<PrioritySegment> Segments { get; set; } = [];
}

public sealed class PrioritySegment
{
    public int SegmentOrder { get; set; }                 // 排序序号（升序）
    public string SegmentName { get; set; } = "";         // 可解释名
    public bool IsEnabled { get; set; }

    /// <summary>命中条件（AND 列表；全部满足才命中本 Segment）——强类型表达，不建 DSL（清单 17 红线）</summary>
    public List<SegmentMatchCondition> MatchConditions { get; set; } = [];
    /// <summary>命中后本 Segment 内部排序字段（有序，依次生效）</summary>
    public List<SegmentSortField> SortFields { get; set; } = [];
    /// <summary>稳定 Tie-break 字段（并列时最终稳定顺序，避免随机）</summary>
    public List<string> StableTieBreakFields { get; set; } = [];
}

public sealed class SegmentMatchCondition
{
    public DemandField Field { get; set; }                // 强类型字段枚举
    public ConditionOperator Operator { get; set; }       // 强类型操作符枚举
    public object? Value { get; set; }                    // 标量；Operator=In 时为 List<object>
}

public sealed class SegmentSortField
{
    public DemandField Field { get; set; }
    public SortDirection Direction { get; set; }
}

/// <summary>Demand 可匹配字段（强类型；V1 最小集，扩展需 0号位裁决）</summary>
/// <remarks>
/// P0-07：补充 DueDate / IssueDate（冻结文档 §4.2 日期排序；0 号位冻结示例
/// "Delayed SALES_ORDER → DueDate ASC → CustomerTier DESC → IssueDate ASC"）。
/// 新增枚举值必须同步：DemandRecord 字段 + DemandPriorityMatcher 三处 switch + DemandPriorityValidator 白名单。
/// </remarks>
public enum DemandField
{
    RemainingTimeHours,       // 与 NormalLT 的剩余时间
    DelayStatus,              // Delayed / OnTrack
    CustomerTier,             // VIP / A / B / C
    OrderType,                // SO / WO / Transfer ...
    IsPmcProtected,           // PMC 人工强保护
    PriorityLevel,            // 业务优先等级（若冻结允许）
    DueDate,                  // 统一交期（DATE/DATETIME2，客户要求交期）——P0-07
    IssueDate,                // 订单发行/下发日期（源事实字段，MTS 无值）——P0-07
}

public enum ConditionOperator { Equals, NotEquals, LessThan, LessOrEqual, GreaterThan, GreaterOrEqual, In }
public enum SortDirection { Asc, Desc }

/// <summary>
/// ② LockBlock（Demand Protection / 锁定）（清单 18~19 / 冻结 4.2-Supply/Lock）
/// 触发条件治理（3号位配置，2号位执行触发与扣减——C5-1：不传给 5号位）
/// </summary>
public sealed class LockBlock
{
    public ProtectionTriggerParams Trigger { get; set; } = new();
    /// <summary>Sticky 语义治理（保护持续到 完成/取消/Supply失效/PMC 显式释放）</summary>
    public StickyProtectionParams Sticky { get; set; } = new();
}

public sealed class ProtectionTriggerParams
{
    public bool UseRemainingTimeThreshold { get; set; }    // RemainingTime < NormalLT 触发
    public double RemainingTimeThresholdHours { get; set; }
    public bool ProtectDelayed { get; set; }              // DelayStatus = DELAYED 触发
    public bool ProtectVipTier { get; set; }              // CustomerTier = VIP 触发
    public string? VipTierValue { get; set; }
    public IReadOnlyList<string> ProtectedOrderTypes { get; set; } = [];  // 指定 OrderType 触发
    public bool AllowPmcManualProtection { get; set; }    // PMC 人工强保护入口
}

public sealed class StickyProtectionParams
{
    public bool RequireReleaseRecord { get; set; }        // 手工释放须记录 Actor/Time/Reason（清单 19）
    public bool ProtectUntilCompletion { get; set; }
    public bool ProtectUntilSupplyInvalid { get; set; }   // Supply 失效才解除
}

/// <summary>③ SupplyBlock（Inventory Availability + PI 排序）（清单 20~22 / 冻结 4.2-Supply/Lock）</summary>
public sealed class SupplyBlock
{
    public InventoryAvailabilityRule Inventory { get; set; } = new();
    /// <summary>PI 简单排序（不建复杂评分引擎，清单 21）</summary>
    public PiSortParams PiSort { get; set; } = new();
}

public sealed class InventoryAvailabilityRule
{
    public bool IsEnabled { get; set; }
    /// <summary>参与可用的 Warehouse 有序列表（Priority 隐含于顺序）</summary>
    public IReadOnlyList<string> WarehousePriority { get; set; } = [];
    public bool RequireFactoryContext { get; set; }       // 必要 Factory 上下文
    public bool RequireProductFamilyContext { get; set; } // 必要 ProductFamily 上下文
}

public sealed class PiSortParams
{
    public PiSortBy SortBy { get; set; }                  // 默认 Issue/Create Time ASC
    public bool UseStablePiNoTieBreak { get; set; }
}

public enum PiSortBy { IssueDateAsc, CreatedAtAsc, StablePiNoAsc }

/// <summary>
/// ④ ProcurementBlock（采购/良率参数，含 PlanningYield）（清单 23~27 / 3.3 / C2-5）
/// C2-5：PlanningYield 必须进入 Snapshot，归属本块；2号位读取算 NetOutputQty → PlannedProcessQty；
/// 红线：已有 PI Supply 不得按 Yield 再次放大。
/// 0号位 2026-08-28 最终裁决：采购/Timed Supply 排序链（Eligibility → Warehouse Priority → AvailableTime
/// → PO Release Time → PO+Line）固定、不可重排、**无 Enable/Disable 开关**；3号位 只治理本块四个参数值：
/// WarehousePriority / DefaultPurchaseLt / OverdueMargin / ArrivalToUsableOffsets
/// （原 ProcurementSortParams 四开关类型已按裁决删除）。
/// </summary>
public sealed class ProcurementBlock
{
    /// <summary>采购排序链"Warehouse Priority"步的仓库优先级（有序列表，隐含优先级；0号位 2026-08-28 裁决补齐）</summary>
    public IReadOnlyList<string> WarehousePriority { get; set; } = [];
    /// <summary>Default Purchase LT（按 Receiving Warehouse + 必要 Material 维度，清单 23；不增加 ProductFamily 维度）</summary>
    public IReadOnlyList<PurchaseLtRule> DefaultPurchaseLt { get; set; } = [];
    /// <summary>逾期 Margin（保守修正，清单 25）</summary>
    public OverdueMarginParams OverdueMargin { get; set; } = new();
    /// <summary>Arrival-to-Usable Offset（按 Receiving Warehouse，清单 26）</summary>
    public IReadOnlyList<WarehouseOffsetRule> ArrivalToUsableOffsets { get; set; } = [];
    /// <summary>Planning Yield（Material/Stage 维度，清单 27）——C2-5：必须进入 Snapshot，归属本块</summary>
    public IReadOnlyList<PlanningYieldRule> PlanningYields { get; set; } = [];
}

public sealed class PurchaseLtRule
{
    public string WarehouseCode { get; set; } = "";
    public string? MaterialId { get; set; }               // 空 = Warehouse 级默认
    public double DefaultLtDays { get; set; }
}

public sealed class OverdueMarginParams
{
    public decimal MarginPercent { get; set; }            // 保守修正比例
    public int MinimumExtraDays { get; set; }             // 最小加天
}

public sealed class WarehouseOffsetRule
{
    public string WarehouseCode { get; set; } = "";
    public double OffsetHours { get; set; }               // ArrivalTime → AvailableTime 偏移
}

public sealed class PlanningYieldRule
{
    public string MaterialId { get; set; } = "";
    public string? StageCode { get; set; }                // 空 = Material 级默认
    public decimal YieldPercent { get; set; }             // 0 < YieldPercent <= 100
}

/// <summary>
/// ⑤ SolverStrategyBlock（Solver 策略）（清单 28~33 / 冻结 4.2-Solver策略）
/// 与现有 StrategyConfig 的映射（盘点 §三.4）：SchedulingMode → SolverStrategyMode 在阶段 E 定义，不静默改 StrategyConfig 语义。
/// </summary>
public sealed class SolverStrategyBlock
{
    public SolverStrategyMode Mode { get; set; }          // AUTO / FORWARD / BACKWARD / MIXED（冻结 E1；AUTO=1号位按求解上下文决定最终方向，0号位 2026-10-07 裁决）
    public bool AllowMerge { get; set; } = false;         // 是否允许合批（1号位 PhaseTwoInitialScheduler.cs:158 消费；3号位 2026-09-04 补源）
    public DynamicBottleneckMode BottleneckMode { get; set; }   // 选「哪个资源当瓶颈锚点」的枚举（Auto/PreferAnchor/ForceAnchor/NotAnchor）
    public string? AnchorResourceCode { get; set; }        // 锚点资源编码（业务编码，非 int ResourceId）；BottleneckMode=PreferAnchor/ForceAnchor/NotAnchor 时生效，Auto 忽略；空=未指定→等价 Auto。P1-02 归 3号位 冻结、2号位 传入、1号位 消费
    public OnTimeTargetParams OnTimeTarget { get; set; } = new();
    public SplitParams Split { get; set; } = new();
    public SetupParams Setup { get; set; } = new();
    public StageOverlapParams StageOverlap { get; set; } = new();

    // P1-02 补充（2026-09-14，2号位 出字段 / 3号位 冻结参数值 / 1号位 换读）：
    // BottleneckMode 只定「选哪个锚点」，不覆盖「利用率超多少判瓶颈」的百分比阈值——这是两个可调旋钮。
    // 0.85 判瓶颈资源（1号位 PhaseThreeDiagnostics.cs:92，填 BottleneckResourceIds，影响展示）；
    // 0.90 判延期根因 RESOURCE_CAPACITY_SHORTAGE（PhaseThreeDiagnostics.cs:170，影响产能短缺责任归属）。
    // 拆两个独立阈值（目标不同、可单独调）。归 3号位 冻结。
    public decimal BottleneckUtilizationThreshold { get; set; } = 0.85m;           // 利用率 > 此值 → 判瓶颈资源
    public decimal CapacityShortageUtilizationThreshold { get; set; } = 0.90m;     // 利用率 > 此值 → 判延期根因产能短缺
}

public enum SolverStrategyMode { Forward, Backward, Mixed, Auto }
public enum DynamicBottleneckMode { Auto, PreferAnchor, ForceAnchor, NotAnchor }

public sealed class OnTimeTargetParams
{
    public int TargetPercent { get; set; }                // 0~100（发布前校验）
    /// <summary>On-time 为业务优化目标，不被 Setup/WIP/Utilization 反向压过（清单 30 红线）</summary>
    public bool IsPrimaryObjective { get; set; } = true;
}

public sealed class SplitParams
{
    public int MaxOptimizationSplitCount { get; set; } = 3;   // 如 3（清单 31）
    public bool LimitMandatorySplit { get; set; }
    public decimal MinBatchQty { get; set; } = 0.1m;          // 拆分下限（每批最小数量）；P1-02 由 0→0.1
}

public sealed class SetupParams
{
    // 2026-09-16 v1.2 废止属性式换型维度（Mold/Tool/Material/Color，原 Dimensions 字段）：
    // 换型规则改走 RuleSetVersion 的 SetupTransitionRule（EXACT/DEFAULT），本块不再承载换型维度。
    // 原 DefaultSetupMinutes / SetupLookAheadSize 两个旧锚已于 2026-09-20 退役（2号位删字段，生产零读者）。

    // 预算参数（2026-09-20 1号位 提值，3号位 落 ParameterSetVersion 治理槽位）：
    // 均为「次数」量纲，非毫秒——保证输入+同种子 → 同搜索轨迹 → 结果可重放（审计/回归依赖确定性）。
    public int SetupSearchBudget { get; set; } = 500;              // 有界搜索预算：单「可移动段」邻域算子总尝试次数上限，允许范围 [100, 5000]
    public int SetupMaxNeighborhoodTries { get; set; } = 50;       // 最大邻域尝试次数：连续无改善（Level 3 目标未下降）上限，活跃改善即清零重计，允许范围 [10, 500]
}

public sealed class StageOverlapParams
{
    public bool AllowOverlap { get; set; }
    public decimal TransferBatchQty { get; set; }
    public decimal ThresholdQty { get; set; }
    public decimal ThresholdPercent { get; set; }
}

/// <summary>⑥ CandidateGuardrailBlock（Candidate 技术 Guardrail）（清单 34~35 / 冻结 4.2-Solver策略）</summary>
public sealed class CandidateGuardrailBlock
{
    public int NormalMs { get; set; } = 60_000;          // 正常 ≈ 60s
    public int SoftMs { get; set; } = 90_000;            // 软超时 ≈ 90s
    public int LocalHardMs { get; set; } = 180_000;      // 局部硬 ≈ 180s
    public int ImpactedTaskWarningPercent { get; set; } = 30;   // 受影响 Task 警戒
    public int MaxRepairAttempts { get; set; } = 5;
    public int MaxPropagationRounds { get; set; } = 10;
    public int ResourceTopN { get; set; } = 5;
    public int SplitAlternatives { get; set; } = 3;
    /// <summary>红线（清单 35）：MaxImpactedOrders 超阈值仅 Warning + 人工确认，不得停止传播返回伪可行结果</summary>
    public bool WarnOnlyOnMaxImpacted { get; set; } = true;
}

/// <summary>
/// ⑦ 产品转换换型规则快照（SetupTransitionRuleSnapshot）——本 Run 冻结内容（v1.2 §九/§十）
/// 由 2号位 从 RuleSetVersion 已发布的 SetupTransitionRule 投影填充（3号位 出字段，2号位 接投影）；
/// 2号位 按 Domain（ProductionDepartmentId + StageCode）裁剪建内存索引，1号位 DomainSolveRequest 消费。
/// 仅业务键 + 分钟 + 类型，不含审计字段（Id/Created/Updated/IsActive——装载时只取有效规则）。
/// 红线：Solver 不得运行中逐 Task 查 3号位规则库（0号位 裁决项2）。
/// </summary>
public sealed class SetupTransitionRuleSnapshot
{
    public int ProductionDepartmentId { get; set; }         // 生产部门（业务范围）
    public string StageCode { get; set; } = "";              // 大工艺阶段码
    public string OperationCode { get; set; } = "";          // 当前小工序
    public int ResourceId { get; set; }                      // 当前设备
    public int? FromMaterialId { get; set; }                 // 前产品（EXACT 明确；DEFAULT 为 null）
    public int? ToMaterialId { get; set; }                   // 后产品（EXACT 明确；DEFAULT 为 null）
    public string RuleType { get; set; } = "DEFAULT";       // "EXACT" / "DEFAULT"（v1.2 §九）
    public decimal SetupMinutes { get; set; }                // 换型分钟（真实资源占用，非负）
}

/// <summary>
/// ⑧ 批量策略（Batch Policy）规则快照——本 Run 冻结内容（0号位 2026-10-07 裁决：本轮落码）。
/// 粒度：Material + ProductionDepartment（MaterialId NOT NULL；ProductionDepartmentId 正式业务键须明确，NULL 仅历史兼容，
/// v5.1.10 收口④：NULL 部门历史记录不默认为所有部门生效，投影端已按 HasValue 排除）。
/// 来源：TaskSplitRuleConfig 物理表 active + 生效区间内规则，由 FrozenStrategySnapshotProvider 装配时投影。
/// 缺策略（某 Material+Dept 无命中规则）由消费侧 fail-closed（BATCH_POLICY_MISSING），本块不承载信号位。
/// 仅业务键 + 参数，不含审计字段（Id/Created/Updated/IsActive——投影时只取有效规则）。
/// v5.1.10 收口①②（2026-10-09 生效）：MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy
/// 投影恒 null、主链不得消费（Solver 技术预算归 1号位 版本化安全默认；策略列仅追溯）。字段保留仅为兼容旧快照反序列化与 1号位 消费侧类型，
/// 不构成有效预算/拆合批倾向来源。
/// </summary>
public sealed class BatchPolicyRuleSnapshot
{
    public int MaterialId { get; set; }                         // 物料（正式业务键）
    public int? ProductionDepartmentId { get; set; }            // 生产部门（正式命中须明确；可空仅历史兼容，不默认为所有部门生效）

    public decimal? MinExecutionBatchQty { get; set; }          // 硬最小批量
    public decimal? MaxExecutionBatchQty { get; set; }          // 硬最大批量（NULL=无硬上限）
    public decimal? PreferredBatchQty { get; set; }             // 软偏好切点（业务生效必填且 >0、处于硬 Min/Max 内，由治理发布校验）
    public bool AllowSplit { get; set; }                        // 是否允许拆分
    public bool AllowMerge { get; set; }                        // 是否允许合并
    public int? MaxOptimizationSplitCount { get; set; }         // v5.1.10 收口①：恒 null 不投（1号位 Solver 技术预算）；仅类型兼容
    public int? MaxBatchCandidates { get; set; }                // v5.1.10 收口①：恒 null 不投（1号位 Solver 技术预算）；仅类型兼容
    public string? BottleneckSplitStrategy { get; set; }        // v5.1.10 收口②：恒 null 不投（历史兼容列，V1 不得消费拆/合批倾向）；仅类型兼容
    public string? NonBottleneckStrategy { get; set; }          // v5.1.10 收口②：恒 null 不投（历史兼容列，V1 不得消费拆/合批倾向）；仅类型兼容
}
