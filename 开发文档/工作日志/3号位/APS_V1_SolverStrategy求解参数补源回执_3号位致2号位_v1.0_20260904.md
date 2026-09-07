# 回执：SolverStrategy 求解参数补源（AllowMerge / MaxIterations）+ AllowSplit 口径

> 收件：2号位（Pegging + Scheduling）、PM（存档）
> 发件：3号位（规则/治理）
> 依据：派单 `APS_V1_SolverStrategy求解参数补源_2号位致3号位_v1.0_20260904(1).md`
> 日期：2026-09-04

---

## 一、结论一句话

3号位 已按「补字段 + 认可 AllowSplit 口径」完成处置：`SolverStrategyBlock` 已补 `AllowMerge` / `MaxIterations` 字段（默认 `false` / `1000`，沿用 2号位 原写死值 = **零行为变化**）。请 2号位 按 §四 映射接上投影，**1号位 零改动**。

---

## 二、回执表（对应派单 §五）

| 项 | 请答复 | 回执 |
|---|---|---|
| 事项1 | 补字段，还是 PM 明示 V1 固定值？ | ☑ **补字段**：`AllowMerge`(bool，默认 `false`) + `MaxIterations`(int，默认 `1000`) |
| 事项2 | `AllowSplit = MaxOptimizationSplitCount>1` 认可否？ | ☑ **认可**（默认 3 → 允许拆批；行为从「永不拆批」变为随冻结配置生效，已确认） |
| 备注 | 补字段落库位置由 3号位 治理 | 代码侧 `FrozenStrategySnapshot.SolverStrategyBlock`（六块⑤）已补；种子 SQL 与交付契约 v0.2 已同步 |

---

## 三、3号位 已完成（2026-09-04，编译 0 错，SolverStrategy 相关 9 测全过）

1. **代码侧真源**：`LPS.APS.Core/Dto/FrozenStrategySnapshot.cs` 的 `SolverStrategyBlock` 补：
   - `public bool AllowMerge { get; set; } = false;` —— 是否允许合批（1号位 `PhaseTwoInitialScheduler.cs:158` 消费）。
   - `public int MaxIterations { get; set; } = 1000;` —— 求解迭代上限。
   - 默认值与 2号位 原写死值、`FiniteCapacityParameters` 默认值三者一致，旧 JSON 缺失字段走属性默认值，反序列化兼容。
2. **发布前校验**：`LPS.APS.Application/Services/SolverStrategyValidator.cs` 补 `ValidateMaxIterations`（`MaxIterations ≤ 0` 拒绝）；配套单测 `E4_MaxIterations_非正_拒绝` 已落 `SolverStrategyValidatorTests.cs`。
3. **种子数据同步**：`开发文档/2026-09-01文档更新/APS_联调种子数据_策略包_20260901.sql` 的 PS-DEMO-V1 / PS-DEMO-V2 两处 `SolverStrategy` JSON 各补 `"AllowMerge":false,"MaxIterations":1000`。
4. **契约文档同步**：`APS_V1_3号位交付契约_v0.2_20260817.md` 的 `SolverStrategyBlock` 代码块补两字段。

---

## 四、请 2号位 下一步（接投影）

`PeggingOrchestrator.cs:189-190` 当前仍写死，请将：

```csharp
AllowMerge = false,        // 六块无源，V1 固定（待 3号位 补字段）
MaxIterations = 1000,      // 六块无源，V1 固定（待 3号位 补字段）
```

改为：

```csharp
AllowMerge = solverStrategy.AllowMerge,
MaxIterations = solverStrategy.MaxIterations,
```

映射关系（1号位 零改动）：

| `FiniteCapacityParameters` | 来源（`frozenSnapshot.SolverStrategy`） |
|---|---|
| `SchedulingDirection` | `Mode → FORWARD / BACKWARD / MIXED`（2号位 已接） |
| `AllowSplit` | `Split.MaxOptimizationSplitCount > 1`（2号位 已接） |
| `AllowMerge` | `AllowMerge`（本次补源，待接） |
| `MaxIterations` | `MaxIterations`（本次补源，待接） |

---

## 五、验证证据

- `dotnet build`（Core / Application / Tests）0 错。
- `SolverStrategyValidatorTests` + `FrozenStrategySnapshotProviderTests` 相关 **9 测全过**（含新增 `E4_MaxIterations_非正_拒绝`）。
- 集成测试基线保持 **197 通过 / 0 跳过 / 1 失败**（唯一失败仍为既有 `v5.1.2: 完整排程流程集成测试 — Task表没有数据`，2号位 Task 落盘缺口，非本派单引入）。
