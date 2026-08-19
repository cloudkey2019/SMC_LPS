# APS V1 3号位交付契约 v0.1（FrozenStrategySnapshot / Provider / FrozenFactParameters）

**版本**：v0.1（供 2号位联调前确认）
**日期**：2026-08-17
**适用对象**：3号位（及其开发 AI）、2号位、5号位
**性质**：《APS_V1_3号位代码开发前置准备清单》第 2 步（接口/DTO 定稿）产出。把 0号位裁决 A/C 的接口语义落为可联调的精确契约。
**上位依据**：
- 《APS_V1_关键接口冻结_1-2_2-5_2-3_v1.0_20260814.md》§4.2（Snapshot 最小内容）、§3.2（FrozenFactParameters）
- 《APS_V1_3号位视角_23和35接口缺口清单_v1.2_20260817.md》第四章裁决 A（Snapshot 归属）、裁决 C（FrozenFactParameters 投影）
- 《APS_V1_3号位工作条目清单_v1.0_20260817.md》第 13、14~35、51 条
- 现有代码：`LPS.APS.Core/Models/Scheduling/SchedulingContext.cs`（消费侧）、`LPS.APS.Core/Dto/DomainSolveRequest.cs`（DTO 风格参照）

**契约红线**：不新增表、不新增独立版本号（裁决 C）；一次 Run 一份规则真相；Cache Key 必须含 VersionId（清单 51 条）。

---

# 一、契约范围

本契约定义 3号位交付给 2号位（→ 经 2号位转交 5号位）的**冻结配置载体**，共 4 件：

| 交付物 | 用途 | 消费方 |
|---|---|---|
| `FrozenStrategySnapshot` | 一次 Run 的完整冻结配置（六块） | 2号位（Run 启动装载一次） |
| `GetFrozenStrategySnapshotAsync` Provider | 3号位构建 Snapshot 的入口 | 2号位 Run 启动调用 |
| `FrozenFactParameters` | Snapshot 中供 5号位复杂事实计算的最小参数投影 | 5号位（经 2号位转交） |
| Cache Key 约定 | Snapshot 内存缓存/命名的唯一性保证 | 2号位 |

---

# 二、FrozenStrategySnapshot（六块 DTO 定义）

**落位建议**：`LPS.APS.Core/Dto/FrozenStrategySnapshot.cs`（与 DomainSolveRequest 同级的 2↔3 传输 DTO；待 2号位确认后定稿落位）
**六块结构**：清单第 13 条"六块"为冻结文档 4.2"三块"的细拆——Demand排序↔①，Supply/Lock 拆为 ②③④，Solver 策略拆为 ⑤⑥。双向对齐无遗漏。

```csharp
/// 一次 Run 启动时的完整冻结配置（不可变，Run 内不刷新）
public sealed class FrozenStrategySnapshot
{
    public long StrategyProfileVersionId { get; set; }        // 归属版本（冻结锚点）
    public long RuleSetVersionId { get; set; }                // 同一包内
    public long ParameterSetVersionId { get; set; }           // 同一包内
    public DateTime FrozenAt { get; set; }                    // 冻结时点（Run 启动）

    public DemandPriorityBlock DemandPriority { get; set; } = new();  // ① Demand 排序
    public LockBlock Lock { get; set; } = new();                        // ② Demand Protection / 锁定
    public SupplyBlock Supply { get; set; } = new();                    // ③ Inventory Availability + PI 排序
    public ProcurementBlock Procurement { get; set; } = new();          // ④ 采购/良率参数
    public SolverStrategyBlock SolverStrategy { get; set; } = new();    // ⑤ Solver 策略
    public CandidateGuardrailBlock CandidateGuardrail { get; set; } = new(); // ⑥ Candidate 技术 Guardrail
}
```

## ① DemandPriorityBlock（Demand 排序）— 清单 14~17 / 冻结 4.2-Demand排序

```csharp
public sealed class DemandPriorityBlock
{
    /// 有序 Segment 列表，按 SegmentOrder 升序；第一命中即止（清单 16）
    public IReadOnlyList<PrioritySegment> Segments { get; set; } = [];
}

public sealed class PrioritySegment
{
    public int SegmentOrder { get; set; }                 // 排序序号（升序）
    public string SegmentName { get; set; } = "";         // 可解释名
    public bool IsEnabled { get; set; }

    /// 命中条件（AND 列表；全部满足才命中本 Segment）——强类型表达，不建 DSL（清单 17 红线）
    public IReadOnlyList<SegmentMatchCondition> MatchConditions { get; set; } = [];
    /// 命中后本 Segment 内部排序字段（有序，依次生效）
    public IReadOnlyList<SegmentSortField> SortFields { get; set; } = [];
    /// 稳定 Tie-break 字段（并列时最终稳定顺序，避免随机）
    public IReadOnlyList<string> StableTieBreakFields { get; set; } = [];
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

/// Demand 可匹配字段（强类型；V1 最小集，扩展需 0号位裁决）
public enum DemandField
{
    RemainingTimeHours,       // 与 NormalLT 的剩余时间
    DelayStatus,              // Delayed / OnTrack
    CustomerTier,             // VIP / A / B / C
    OrderType,                // SO / WO / Transfer ...
    IsPmcProtected,           // PMC 人工强保护
    PriorityLevel,            // 业务优先等级（若冻结允许）
}

public enum ConditionOperator { Equals, NotEquals, LessThan, LessOrEqual, GreaterThan, GreaterOrEqual, In }
public enum SortDirection { Asc, Desc }
```

**红线**：无全局 PriorityScore（清单 C2）；第一命中后不再匹配后续 Segment（清单 16）。

## ② LockBlock（Demand Protection / 锁定）— 清单 18~19 / 冻结 4.2-Supply/Lock

```csharp
public sealed class LockBlock
{
    /// 触发条件治理（3号位配置，2号位执行触发与扣减）
    public ProtectionTriggerParams Trigger { get; set; } = new();
    /// Sticky 语义治理（保护持续到 完成/取消/Supply失效/PMC 显式释放）
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
```

## ③ SupplyBlock（Inventory Availability + PI 排序）— 清单 20~22 / 冻结 4.2-Supply/Lock

```csharp
public sealed class SupplyBlock
{
    public InventoryAvailabilityRule Inventory { get; set; } = new();
    /// PI 简单排序（不建复杂评分引擎，清单 21）
    public PiSortParams PiSort { get; set; } = new();
}

public sealed class InventoryAvailabilityRule
{
    public bool IsEnabled { get; set; }
    /// 参与可用的 Warehouse 有序列表（Priority 隐含于顺序）
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

/// 采购排序冻结链（清单 22）：Eligibility → Warehouse Priority → AvailableTime → PO Release Time → PO+Line。
/// 3号位不重排该冻结链，只治理其中参数。
public sealed class ProcurementSortParams
{
    public bool WarehousePriorityEnabled { get; set; }
    public bool DefaultLtAsTieBreak { get; set; }         // 是否以 DefaultLT 参与排序
    public bool MarginApplied { get; set; }
    public bool ArrivalOffsetApplied { get; set; }
}
```

## ④ ProcurementBlock（采购/良率参数）— 清单 23~27 / 3.3

```csharp
public sealed class ProcurementBlock
{
    /// Default Purchase LT（按 Receiving Warehouse + 必要 Material 维度，清单 23；不增加 ProductFamily 维度）
    public IReadOnlyList<PurchaseLtRule> DefaultPurchaseLt { get; set; } = [];
    /// 逾期 Margin（保守修正，清单 25）
    public OverdueMarginParams OverdueMargin { get; set; } = new();
    /// Arrival-to-Usable Offset（按 Receiving Warehouse，清单 26）
    public IReadOnlyList<WarehouseOffsetRule> ArrivalToUsableOffsets { get; set; } = [];
    /// Planning Yield（Material/Stage 维度，清单 27；红线：已有 PI Supply 不得按 Yield 再次放大）
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
```

> **ETA 优先级为冻结业务 Invariant**（裁决 D）：`Manual ETA > ERP ETA > DefaultLT`，不做可配置排序。契约中不出现"ETA 排序规则"，由 5号位/ODS 按固定优先级计算（3.3）；3号位仅治理 DefaultLT / Margin / Offset。

## ⑤ SolverStrategyBlock（Solver 策略）— 清单 28~33 / 冻结 4.2-Solver策略

```csharp
public sealed class SolverStrategyBlock
{
    public SolverStrategyMode Mode { get; set; }          // FORWARD / BACKWARD / MIXED（冻结 E1）
    public DynamicBottleneckMode BottleneckMode { get; set; }
    public OnTimeTargetParams OnTimeTarget { get; set; } = new();
    public SplitParams Split { get; set; } = new();
    public SetupParams Setup { get; set; } = new();
    public StageOverlapParams StageOverlap { get; set; } = new();
}

public enum SolverStrategyMode { Forward, Backward, Mixed }
public enum DynamicBottleneckMode { Auto, PreferAnchor, ForceAnchor, NotAnchor }

public sealed class OnTimeTargetParams
{
    public int TargetPercent { get; set; }                // 0~100（发布前校验）
    /// On-time 为业务优化目标，不被 Setup/WIP/Utilization 反向压过（清单 30 红线）
    public bool IsPrimaryObjective { get; set; } = true;
}

public sealed class SplitParams
{
    public int MaxOptimizationSplitCount { get; set; } = 3;   // 如 3（清单 31）
    public bool LimitMandatorySplit { get; set; }
    public decimal MinBatchQty { get; set; }                  // 不限无限拆分
}

public sealed class SetupParams
{
    /// 冻结换型维度：Mold / Tool / Material / Color（清单 32；不做全局 TSP 权重矩阵平台）
    public IReadOnlyList<string> Dimensions { get; set; } = [];
    public double DefaultSetupMinutes { get; set; } = 30;
    public int SetupLookAheadSize { get; set; } = 5;
}

public sealed class StageOverlapParams
{
    public bool AllowOverlap { get; set; }
    public decimal TransferBatchQty { get; set; }
    public decimal ThresholdQty { get; set; }
    public decimal ThresholdPercent { get; set; }
}
```

**与现有 `StrategyConfig`（Core）的映射**（盘点结论 §三.4）：`SchedulingMode`（Backward/Forward/BackwardThenForward）→ `SolverStrategyMode`（Forward/Backward/Mixed）映射关系在阶段 E 定义，契约不静默改 `StrategyConfig` 语义。

## ⑥ CandidateGuardrailBlock（Candidate 技术 Guardrail）— 清单 34~35 / 冻结 4.2-Solver策略

```csharp
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
    /// 红线（清单 35）：MaxImpactedOrders 超阈值仅 Warning + 人工确认，不得停止传播返回伪可行结果
    public bool WarnOnlyOnMaxImpacted { get; set; } = true;
}
```

---

# 三、Provider 签名（裁决 A）

3号位提供、2号位在 ScheduleRun 启动时调用**一次**（冻结文档 4.1；裁决 A 执行链）：

```csharp
/// 按已冻结 StrategyProfileVersionId 构建一次 Run 的完整冻结配置
/// 3号位提供；2号位 Run 启动装载一次，本 Run 内存使用，不逐笔调用
Task<FrozenStrategySnapshot> GetFrozenStrategySnapshotAsync(
    long strategyProfileVersionId,
    CancellationToken ct);
```

- **实现位置**：3号位（Application 层服务，装配 RuleSetVersion + ParameterSetVersion → Snapshot）
- **接口定义位置**：`LPS.APS.Core`（接口）→ Application 实现（清单 52 交付物 2）
- **执行链（裁决 A 原文）**：ScheduleRun → StrategyProfileVersionId 已冻结 → 2号位调用 Provider 一次 → FrozenStrategySnapshot → 本 Run 内存缓存 → 2号位执行 Demand/Pegging → Solver 部分放 DomainSolveRequest 给 1号位
- **禁止**：2号位自读六张治理表拼 Snapshot；为形式统一塞进 `IDataLoader`；新建 Snapshot 物理表；修改《关键接口冻结》（裁决 A"不需要做"）

---

# 四、FrozenFactParameters（投影定义，裁决 C）

**定义**：`FrozenStrategySnapshot` 中供 5号位复杂事实计算所需参数的**最小投影/子集**，**非独立版本体系**（裁决 C：不新增 FrozenFactParametersVersion、不新增表、不新增独立版本号）。

**投影来源与范围**（从 ②Lock、④Procurement 中抽取 5号位真正需要的参数）：

```csharp
/// 供 5号位 PI Position / Timed Supply 计算的冻结参数投影（经 2号位一次转交）
public sealed class FrozenFactParameters
{
    public long StrategyProfileVersionId { get; set; }   // 关联到 Snapshot 的唯一锚点（不是独立版本号）

    // —— 来自 ProcurementBlock（3号位治理）——
    public IReadOnlyList<PurchaseLtRule> DefaultPurchaseLt { get; set; } = [];
    public OverdueMarginParams OverdueMargin { get; set; } = new();
    public IReadOnlyList<WarehouseOffsetRule> ArrivalToUsableOffsets { get; set; } = [];

    // —— 来自 LockBlock ——
    public ProtectionTriggerParams ProtectionTrigger { get; set; } = new();
    // —— 来自 SupplyBlock ——
    public InventoryAvailabilityRule InventoryAvailability { get; set; } = new();
    public ProcurementSortParams ProcurementSort { get; set; } = new();
}
```

**5号位消费方式（裁决 C 链路）**：

```text
3号位 FrozenStrategySnapshot
        ↓
2号位一次装载（Provider）
        ↓
抽取 5号位需要的参数子集 → FrozenFactParameters
        ↓
5号位 CalculateProductionInstructionPositionsAsync(inputs, parameters, ct)
（冻结文档 3.2 签名；参数来自本次投影）
```

**明确不含**：Demand 排序（①）、Solver 策略（⑤）、Candidate Guardrail（⑥）——5号位无需；不让 5号位逐笔 RPC 调 3号位（裁决 C）；一个 Run 只有一份规则真相。

---

# 五、Cache Key 约定（清单 51）

- **命名**：`frozen-strategy-snapshot:{strategyProfileVersionId}`
- **必须含 VersionId**：不同 StrategyProfileVersion 不互相污染（裁决 A + 清单 51）
- **生命周期**：Run 启动装载后，**Run 内不刷新**（即使中途有新的 PUBLISHED 版本，本次 Run 仍用冻结的 Snapshot——清单 12/B3）
- **序列化**：如需缓存到 Redis，按现有 `Cache` 配置（appsettings.json，KeyPrefix=LPS.APS）拼接前缀

---

# 六、与 2号位联调检查点（提交确认）

| # | 检查点 | 依据 |
|---|---|---|
| 1 | 2号位只读 PUBLISHED + Run 启动一次装载 + 内存执行 | 清单 53 / 裁决 A |
| 2 | `StrategyProfileVersionId` 冻结锚点：RuleSetVersionId 与 ParameterSetVersionId 必须来自同一 StrategyProfileVersion | 清单 12 |
| 3 | IsDefault=1 且 PUBLISHED 的默认版本选择逻辑不变（2号位 `ScheduleRunService.CreateScheduleRunAsync` 现 SQL 不动） | 盘点结论 §三.3 |
| 4 | Cache Key 含 VersionId；Run 中不刷新 | 清单 51 / 本章五 |
| 5 | 六块 ↔ 冻结文档 4.2 三块双向覆盖，无遗漏 | 本章二 |
| 6 | FrozenFactParameters 经 2号位转交 5号位，5号位不直接调 3号位；不新增表/版本号 | 裁决 C / 冻结 3.2 |

---

# 七、确认与变更控制

- **待确认方**：2号位（联调前确认无异议）；5号位（确认 FrozenFactParameters 覆盖其 PI Position / Timed Supply 所需参数）
- **完成判据（前置准备清单第 2 步）**：契约 v0.1 双方确认无异议；**不新增表、不新增独立版本号**
- **变更控制**：本契约如须修改，走《关键接口冻结》七章变更控制 + 0号位裁决；3号位不静默改契约（红线 5：接口即契约）
- 确认后：字段名/落位定稿 → 阶段 B 实现 `GetFrozenStrategySnapshotAsync` → 与 2号位联调（交付物 11）

---

*本契约 v0.1 为 3号位提出稿，提交 2号位联调前确认；确认后定稿，作为阶段 B（FrozenStrategySnapshot）与阶段 D（Supply/Procurement 参数）的实现依据。*
