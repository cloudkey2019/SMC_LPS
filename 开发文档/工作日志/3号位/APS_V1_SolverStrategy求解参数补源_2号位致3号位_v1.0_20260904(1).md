# 派单：SolverStrategy 求解参数补源（AllowMerge / MaxIterations）+ AllowSplit 口径复核

> 收件：3号位（规则/治理）、PM（口径拍板）
> 发件：2号位（Pegging + Scheduling）
> 依据：需求侧缺口 Q9 核实结论 + 冻结文档 4.2-Solver策略（清单 28~33）
> 日期：2026-09-04

---

## 一、结论一句话

2号位 已把 `SolverStrategySnapshot.Parameters` 从**写死**改为从冻结 `SolverStrategyBlock` **投影**（`ParameterSetVersionId` / `Mode→Direction` / `AllowSplit` 已接，编译通过 0 错）。还剩两件事：

1. **AllowMerge / MaxIterations 在六块快照里没有源**——需 3号位 在 `SolverStrategyBlock` 补字段（或 PM 明确 V1 固定值）。
2. **AllowSplit 的推导口径**（`MaxOptimizationSplitCount>1`）需 3号位/PM 复核一句。

---

## 二、背景（Q9 核实结论）

1号位 已实现且**真读参数**，锁死在 2号位 的写死值上：

| 1号位 消费点 | 读的参数 |
|---|---|
| `PhaseTwoInitialScheduler.cs:27` / `:387-401` | `SchedulingDirection`（三分支 BACKWARD / FORWARD / MIXED） |
| `PhaseTwoInitialScheduler.cs:158` | `AllowMerge` |
| `PhaseFourLocalRepair.cs:802` | `AllowSplit` |

2号位 旧代码（`PeggingOrchestrator.cs`）写死：`BACKWARD` / `AllowSplit=false` / `AllowMerge=false` / `MaxIterations=1000` / `ParameterSetVersionId=null`——方向/拆批/合批 1号位 都撑得住，却一直被 2号位 钉死。

---

## 三、2号位 已完成（2026-09-04，编译通过 0 错）

`PeggingOrchestrator.cs` 现在从 `frozenSnapshot.SolverStrategy`（ContentSnapshotJson 已全量装载）投影：

| 参数 | 现状 |
|---|---|
| `ParameterSetVersionId` | `frozenSnapshot.ParameterSetVersionId` ✅ |
| `SchedulingDirection` | `Mode → FORWARD / BACKWARD / MIXED` 1:1 ✅ |
| `AllowSplit` | `Split.MaxOptimizationSplitCount > 1` 推导 ✅（待复核，见 §四 事项2） |
| `AllowMerge` | 仍 `false`，六块无源 ⛔ |
| `MaxIterations` | 仍 `1000`，六块无源 ⛔ |

---

## 四、请 3号位 处置

### 事项1：`SolverStrategyBlock` 补 `AllowMerge` / `MaxIterations` 字段

六块冻结快照（`FrozenStrategySnapshot`）的 `SolverStrategyBlock` 现有 `Mode / BottleneckMode / OnTimeTarget / Split / Setup / StageOverlap`，**没有 `AllowMerge` 与 `MaxIterations`**。

请 3号位 在冻结文档 4.2-Solver策略 / 清单 28~33 补两个字段，**或 PM 明确 V1 固定值**：

- `AllowMerge`（bool，是否允许合批）——1号位 `PhaseTwoInitialScheduler.cs:158` 已在消费，缺源。
- `MaxIterations`（int，求解迭代上限）——1号位 求解迭代上限，缺源。

补完字段后 2号位 接上投影即可，**1号位 零改动**。

### 事项2：`AllowSplit` 推导口径复核

2号位 现按 `Split.MaxOptimizationSplitCount > 1` 判定 `AllowSplit`（清单 31 默认 3 = 允许拆批）。请复核一句：

- **认可** → 2号位 保持现状，`AllowSplit` 随冻结配置生效。⚠️ 行为变化：以前永远不拆批，现在冻结配置 `MaxOptimizationSplitCount≥2` 时 Phase 4 允许拆批。
- **不认可** → 请给 `AllowSplit` 的判定规则（如与 `LimitMandatorySplit` 联动、或单独 bool 字段）。

---

## 五、3号位 / PM 回执表

| 项 | 请答复 | 回执 |
|---|---|---|
| 事项1 | `SolverStrategyBlock` 补 `AllowMerge` / `MaxIterations` 字段，还是 PM 明示 V1 固定值？ | □ 补字段　□ V1 固定值（值=____） |
| 事项2 | `AllowSplit = MaxOptimizationSplitCount>1` 认可否？ | □ 认可　□ 需调整（规则=____） |
| 备注 | 补字段的落库位置（`ParameterSetVersion.ContentSnapshotJson` 六块 JSON）由 3号位 治理 | |
