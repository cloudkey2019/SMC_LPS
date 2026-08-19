# APS V1 3号位交付契约 v0.2（FrozenStrategySnapshot / Provider / FrozenFactParameters）

**版本**：v0.2（**经 0号位统一确认后定稿**）
**日期**：2026-08-17
**适用对象**：3号位（及其开发 AI）、2号位（代码联调输入）、5号位（开发输入）
**性质**：《APS_V1_3号位代码开发前置准备清单》第 2 步（接口/DTO 定稿）产出，v0.1 经 0号位统一确认（代 2号位/5号位）收敛后定稿。**阶段 B 实现依据**。
**版本沿革**：v0.1（3号位提出稿）→ v0.2（0号位逐项收敛，见《0号位对交付契约做出的答复.md》）。**以 v0.2 为准。**
**上位依据**：
- 《APS_V1_关键接口冻结_1-2_2-5_2-3_v1.0_20260814.md》§4.2（Snapshot 最小内容）、§3.2（FrozenFactParameters）、§3.3（Procurement/Timed Supply）
- 《APS_V1_3号位视角_23和35接口缺口清单_v1.2_20260817.md》第四章裁决 A/C/D
- 《0号位对交付契约做出的答复.md》（2026-08-17，统一确认结论）
- 《APS_V1_3号位工作条目清单_v1.0_20260817.md》第 13、14~35、51 条

**契约红线（0号位确认不改）**：不新增表、不新增独立版本号（裁决 C/C5-2）；一次 Run 一份规则真相；Snapshot 可缓存且 Cache Key 必须含 VersionId（清单 51 / C2-4）；ETA `Manual ETA > ERP ETA > DefaultLT` 为固定 Invariant 不可配置（裁决 D / C2-5）；不新增 UNLOCATED 业务容忍率（C5-1 / 裁决 B）。

---

# 〇、0号位统一确认记录（2026-08-17）

0号位依据已冻结业务基线、接口冻结、实施包及已冻结的 2号位代码审核结论，**统一代 2号位/5号位确认**，不再等待人员到岗二次裁决。逐项结论：

| # | 结论 | 冻结后的准确口径 |
|---|---|---|
| C2-1 | **需修改** | Run 创建/绑定时只允许选择当时有效的 PUBLISHED StrategyProfileVersion；一旦 `ScheduleRun.StrategyProfileVersionId` 冻结，2号位按该**指定 VersionId** 一次获取 Snapshot；本 Run 不再重新选择 Default、不逐笔 RPC、不因后续新版本发布/停用而漂移 |
| C2-2 | **确认** | Snapshot 必须明确携带 `StrategyProfileVersionId / RuleSetVersionId / ParameterSetVersionId`，三者来自同一个 StrategyProfileVersion；一个 Run 所有 Domain 使用同一版本 |
| C2-3 | **需修改文字** | 不把 2号位具体 SQL 冻结为跨号位契约。正式语义：未显式指定 StrategyProfileVersionId 时，按 RunType 取得当前有效、唯一无歧义的默认 PUBLISHED 版本；3号位负责默认版本治理。具体 SQL 归 2号位实现，满足该语义即可，**不新增 2号位整改要求** |
| C2-4 | **需修改文字** | Cache Key **必须包含 StrategyProfileVersionId** 且不同版本不能污染；本 Run 不刷新。具体字符串（如 `frozen-strategy-snapshot:{id}`）**不是跨号位冻结内容** |
| C2-5 | **需补充后确认** | 六块顶层结构保留；**必须明确 PlanningYield 进入 FrozenStrategySnapshot**（不新增表/版本体系，归属见 §二-④）；明确三个 VersionId 元数据；ETA 不做成可配置顺序 |
| C2-6 | **2号位抽** | 3号位只提供完整 FrozenStrategySnapshot；**2号位在集成/编排层从 Snapshot 抽取 5号位所需 FrozenFactParameters 并转交**；不让 3号位 Snapshot DTO 直接依赖 5号位接口 DTO |
| C5-1 | **需修改** | FrozenFactParameters 最小范围 = `StrategyProfileVersionId + DefaultPurchaseLt + OverdueMargin + ArrivalToUsableOffsets`；**删除** `ProtectionTrigger / InventoryAvailability / ProcurementSort`（属 3号位治理→2号位执行的 Pegging 规则，非 5号位事实计算参数）；当前不新增 UNLOCATED 业务容忍率 |
| C5-2 | **确认** | FrozenFactParameters 无独立版本体系，以父 StrategyProfileVersionId 追溯；不新增表、不新增版本号 |
| C5-3 | **确认（按 C5-1 修正后执行）** | 不含 Demand 排序、Solver Strategy、Candidate Guardrail，也不含 Protection/Inventory/Procurement 排序等 2号位执行规则；5号位不直接 RPC 3号位，只消费 2号位转交的投影 |

**确认性质**：均为契约文字/字段边界收敛，**没有产生新的 2号位整改项，没有改变已冻结的 2号位审核意见**。2号位回来后仅做代码联调，不再获得"重新决定契约"的权力。

---

# 一、契约范围

本契约定义 3号位交付给 2号位（→ 经 2号位转交 5号位）的**冻结配置载体**，共 4 件：

| 交付物 | 用途 | 消费方 |
|---|---|---|
| `FrozenStrategySnapshot` | 一次 Run 的完整冻结配置（六块 + PlanningYield） | 2号位（按冻结 VersionId 装载一次） |
| `GetFrozenStrategySnapshotAsync` Provider | 3号位构建 Snapshot 的入口 | 2号位 Run 启动按指定 VersionId 调用 |
| `FrozenFactParameters` | Snapshot 中供 5号位复杂事实计算的最小参数投影（**2号位抽取**） | 5号位（经 2号位转交） |
| Cache Key 约定 | Snapshot 缓存唯一性（必须含 VersionId） | 2号位 |

---

# 二、FrozenStrategySnapshot（六块 + PlanningYield）

**落位**：`LPS.APS.Core/Dto/FrozenStrategySnapshot.cs`（与 DomainSolveRequest 同级的 2↔3 传输 DTO）
**头部元数据（C2-2/C2-5）**：显式携带三个 VersionId，供 2、1、5号位联调与问题追溯：

```csharp
/// 一次 Run 启动时的完整冻结配置（不可变，Run 内不刷新）
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
}
```

> **C2-5 补充**：六块顶层结构为冻结文档 4.2 三块的细拆（Demand排序↔①，Supply/Lock 拆 ②③④，Solver 策略拆 ⑤⑥），0号位确认保留。**PlanningYield 必须进入 Snapshot**——本契约归属 **④ ProcurementBlock.PlanningYields**（清单 27 语义：Material/Stage 维度，2号位读取算 NetOutputQty→PlannedProcessQty），不新增表/版本体系。

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
    /// 触发条件治理（3号位配置，2号位执行触发与扣减——C5-1：不传给 5号位）
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

## ④ ProcurementBlock（采购/良率参数，含 PlanningYield）— 清单 23~27 / 3.3 / C2-5

```csharp
public sealed class ProcurementBlock
{
    /// Default Purchase LT（按 Receiving Warehouse + 必要 Material 维度，清单 23；不增加 ProductFamily 维度）
    public IReadOnlyList<PurchaseLtRule> DefaultPurchaseLt { get; set; } = [];
    /// 逾期 Margin（保守修正，清单 25）
    public OverdueMarginParams OverdueMargin { get; set; } = new();
    /// Arrival-to-Usable Offset（按 Receiving Warehouse，清单 26）
    public IReadOnlyList<WarehouseOffsetRule> ArrivalToUsableOffsets { get; set; } = [];
    /// Planning Yield（Material/Stage 维度，清单 27）——C2-5：**必须进入 Snapshot**，归属本块
    /// 2号位读取算 NetOutputQty → PlannedProcessQty；红线：已有 PI Supply 不得按 Yield 再次放大
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

> **ETA 优先级为冻结业务 Invariant**（裁决 D / C2-5）：`Manual ETA > ERP ETA > DefaultLT`，不做可配置顺序。契约中不出现"ETA 排序规则"；3号位仅治理 DefaultLT / Margin / Offset，5号位/ODS 按固定优先级与冻结参数形成 Effective ETA / AvailableTime，2号位消费 Timed Supply 做 Pegging。

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

# 三、Provider 签名（裁决 A / C2-1 收敛）

3号位提供、2号位在 ScheduleRun 启动时按**已冻结的指定 VersionId** 调用**一次**（C2-1：不重新选择 Default、不因后续发布/停用而漂移）：

```csharp
/// 按已冻结的 StrategyProfileVersionId 构建一次 Run 的完整冻结配置
/// 3号位提供；2号位 Run 启动按该 VersionId 装载一次，本 Run 内存使用，不逐笔调用
Task<FrozenStrategySnapshot> GetFrozenStrategySnapshotAsync(
    long strategyProfileVersionId,
    CancellationToken ct);
```

- **准确过程（C2-1 冻结）**：创建 ScheduleRun → 选择/校验当时有效的 PUBLISHED StrategyProfileVersion → `ScheduleRun.StrategyProfileVersionId` 冻结 → 2号位按该 VersionId 获取 Snapshot **一次** → 整个 Run 使用同一 Snapshot。**禁止**：每个 Domain 开始再去找"现在最新的 PUBLISHED"（理论会漂移）
- **实现位置**：3号位（Application 层服务，装配 RuleSetVersion + ParameterSetVersion → Snapshot）
- **接口定义位置**：`LPS.APS.Core`（接口）→ Application 实现（清单 52 交付物 2）
- **禁止（裁决 A"不需要做"）**：2号位自读六张治理表拼 Snapshot；为形式统一塞进 `IDataLoader`；新建 Snapshot 物理表；修改《关键接口冻结》

---

# 四、FrozenFactParameters（投影定义，裁决 C + C2-6/C5-1 收敛）

**定义**：`FrozenStrategySnapshot` 中供 5号位复杂事实计算所需参数的**最小投影**，**非独立版本体系**（裁决 C / C5-2：不新增 FrozenFactParametersVersion、不新增表、不新增独立版本号）。

**抽取方（C2-6 裁决：2号位抽）**：3号位只提供完整 `FrozenStrategySnapshot`；**2号位在集成/编排层从 Snapshot 抽取** `FrozenFactParameters` 并转交 5号位。**不提供** `Snapshot.ToFrozenFactParameters()` 方法——避免 3号位 DTO 认识 2↔5 接口 DTO 形成隐形 `3号位→5号位` 依赖。2号位侧维护一个小 Mapping（`FrozenStrategySnapshot → FrozenFactParameters`），属 2号位主流程编排正常职责。

**最小范围（C5-1 收缩）**：

```csharp
/// 供 5号位 PI Position / Timed Supply 计算的冻结参数投影（由 2号位从 Snapshot 抽取转交）
public sealed class FrozenFactParameters
{
    public long StrategyProfileVersionId { get; set; }   // 追溯父 Snapshot 的唯一锚点（非独立版本号）

    // —— 来自 ④ ProcurementBlock（3号位治理）——
    public IReadOnlyList<PurchaseLtRule> DefaultPurchaseLt { get; set; } = [];
    public OverdueMarginParams OverdueMargin { get; set; } = new();
    public IReadOnlyList<WarehouseOffsetRule> ArrivalToUsableOffsets { get; set; } = [];
}
```

> **C5-1 删除说明**：`ProtectionTrigger / InventoryAvailability / ProcurementSort` 属"3号位治理 → 2号位执行"的 Pegging 规则，**不是** 5号位复杂事实计算参数，不进入投影。5号位采购事实真正需要的是 DefaultLT / Margin / Offset（用于标准化 Effective ETA 与 AvailableTime）。
> **不新增**：UNLOCATED 容忍比例、Position 允许不闭合阈值（裁决 B / C5-1）——冻结规则始终 `Σ PositionQty = ERP RemainingQty`；无法定位进 UNLOCATED；无法闭合且无法保守定位才是严重 Issue。纯数值 epsilon（`0.000001` 级）是算法技术细节，不建设成业务参数。

**5号位消费方式（裁决 C 链路，C2-6 正式版）**：

```text
3号位 FrozenStrategySnapshot
        ↓
2号位按冻结 VersionId 一次装载（Provider）
        ↓
2号位集成层 Mapping 抽取 FrozenFactParameters
        ↓
5号位 CalculateProductionInstructionPositionsAsync(inputs, parameters, ct)
（冻结文档 3.2 签名；参数来自本次投影）
```

**明确不含（C5-3 确认）**：Demand 排序（①）、Solver 策略（⑤）、Candidate Guardrail（⑥），以及 Protection/Inventory/Procurement 排序等 2号位执行规则；5号位不直接 RPC 3号位，只消费 2号位转交的投影。

---

# 五、Cache Key 约定（清单 51 / C2-4 收敛）

- **冻结内容（C2-4）**：Cache Key **必须包含 StrategyProfileVersionId**，并保证不同版本**绝不互相污染**；Run 启动装载后 **Run 内不刷新**（即使中途有新的 PUBLISHED 版本，本次 Run 仍用冻结的 Snapshot——清单 12/B3）
- **不冻结内容（C2-4）**：具体字符串格式（如 `frozen-strategy-snapshot:{id}` 或 `APS:StrategySnapshot:{id}`）属 2号位实现细节，业务正确即可
- **序列化**：如需缓存到 Redis，按现有 `Cache` 配置（appsettings.json，KeyPrefix=LPS.APS）拼接前缀

---

# 六、与 2号位联调检查点（0号位确认后）

| # | 检查点 | 依据 |
|---|---|---|
| 1 | 按 Run 已冻结的指定 VersionId 一次装载 + 内存执行；不重新选 Default、不逐笔 RPC、不因后续发布漂移 | 裁决 A / C2-1 |
| 2 | `StrategyProfileVersionId / RuleSetVersionId / ParameterSetVersionId` 来自同一 StrategyProfileVersion；一 Run 所有 Domain 一致 | 清单 12 / C2-2 |
| 3 | 默认版本语义：未显式指定时按 RunType 取唯一无歧义默认 PUBLISHED；3号位负责默认治理；**具体 SQL 归 2号位实现，不冻结** | C2-3 |
| 4 | Cache Key 含 StrategyProfileVersionId + 不污染 + Run 内不刷新；字符串不冻结 | 清单 51 / C2-4 |
| 5 | 六块 + PlanningYield ↔ 冻结 4.2 三块覆盖，无遗漏；三 VersionId 元数据显式 | 冻结 4.2 / C2-5 |
| 6 | 2号位抽 FrozenFactParameters 并转交 5号位；3号位 DTO 不依赖 5号位类型；不新增表/版本号 | 裁决 C / C2-6 / C5-1~3 |

---

# 七、变更控制

- **定稿状态**：本 v0.2 经 0号位统一确认（代 2号位/5号位），**不再等待 2号位/5号位二次裁决**；2号位回来后仅做代码联调
- **变更控制**：如后续真实代码/数据证明现有冻结契约无法实现，走《关键接口冻结》七章变更控制 + 0号位裁决；3号位不静默改契约（红线 5：接口即契约）
- 定稿后：进入阶段 B 实现 `GetFrozenStrategySnapshotAsync` → 与 2号位代码联调（交付物 11）

---

*本契约 v0.2 为 0号位统一确认后的定稿版；v0.1 作废。作为阶段 B（FrozenStrategySnapshot）与阶段 D（Supply/Procurement 参数）的实现依据，并作为 5号位开发的输入。*
