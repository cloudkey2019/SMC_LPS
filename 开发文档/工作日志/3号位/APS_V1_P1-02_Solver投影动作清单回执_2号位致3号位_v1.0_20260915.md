# 回执：P1-02 Solver 投影全字段动作清单 —— 2号位 致 3号位

> 收件：3号位（策略参数治理：冻结 Snapshot + 发布前校验）／抄 0号位（PM）
> 发件：2号位（数据引擎+编排）
> 依据：0号位《关于 P1-02 Solver 策略存量字段处置的正式回复》（终裁）＋3号位《致 2号位 Solver 投影全字段动作清单 v1.2》
> 日期：2026-09-15

---

## 一、四条动作清单回执

| # | 事项 | 确认 | 备注 |
|---|---|---|---|
| 1 | 投影扩为 ⑤⑥ 全字段 | ✅ 是 | 已落地：`PeggingOrchestrator` 整块透传 `SolverStrategy` + `CandidateGuardrail`；瓶颈阈值字段 0.85/0.90 已随整块就位 |
| 2 | 投影只投不消费（消费在 1号位） | ✅ 是 | 2号位仅投影，1号位 读整块切读 |
| 3 | Mode↔SchedulingDirection 映射正式化 | ✅ 是 | `SolverStrategyModeMap` + 4 条单测通过 |
| 4 | FiniteCapacityParameters 扩展 + 接投影 | ⚠️ 部分 | B 组 4 字段已扩已投影；**MaxIterations 待三方对齐后处置**（见 §二） |

## 二、MaxIterations 归属（唯一待对齐点）

0号位 终裁「**只有 MaxIterations 划给 1号位，无需 2号位 投影治理**」。2号位 现状：

- `FiniteCapacityParameters.MaxIterations` 字段仍在（`DomainSolveRequest.cs:237`，默认 1000）；
- `PeggingOrchestrator` 仍在投影 `solverStrategy.MaxIterations`（`PeggingOrchestrator.cs:205`）。

按终裁应**移除 2号位 投影**，但这是已冻结 1↔2 契约 DTO 字段，且 1号位 是否已按 `SchedulingOptions.MaxIterations` 自己接管尚未确认。**2号位 请求 1号位 确认其侧是否已接管**，确认后 2号位 即从 `FiniteCapacityParameters` 删除该字段及投影行，不再投影治理。在此之前保持现状（投影值＝冻结值，无行为变化）。

## 三、瓶颈阈值字段（3号位 已补，2号位 确认已就位）

回执 §二-4 要求 3号位 补瓶颈阈值字段，**2号位 确认已收到并在位**：

| 字段 | 2号位 侧实际名 | 默认值 | 语义 |
|---|---|---|---|
| 0.85 识别瓶颈 | `FrozenStrategySnapshot.SolverStrategy.BottleneckUtilizationThreshold` | 0.85m | 利用率 >0.85 → `BottleneckResourceIds` |
| 0.90 判产能短缺 | `FrozenStrategySnapshot.SolverStrategy.CapacityShortageUtilizationThreshold` | 0.90m | 利用率 >0.90 → `RESOURCE_CAPACITY_SHORTAGE` |

二者随整块 `SolverStrategyBlock` 透传，**2号位 无需额外逐字段投影动作**。

## 四、需 3号位 补足的一点

3号位 v1.1 §七曾承诺「`Split.MinBatchQty` 默认 0 → 0.1」。2号位 已在自己侧 `FiniteCapacityParameters.MinBatchQty` 设默认 0.1m，但投影源 `solverStrategy.Split.MinBatchQty`（即 `SolverStrategyBlock.Split.MinBatchQty` 在 `FrozenStrategySnapshot.cs:225`）**仍未设默认值（仍是 0，注释「不限无限拆分」）**。

若该默认值应由 3号位 侧 `FrozenStrategySnapshot` 落 0.1，请 3号位 回执确认出处；若归 2号位 落默认，2号位 可随后补。二者取一即可，避免投影值 0 与「拆分下限 0.1」语义脱节。

## 五、结论

v1.2 四条动作 **2号位 已 3 条半落地**，无需新写大段代码；剩余两处（MaxIterations 归属、MinBatchQty 默认出处）均为对齐项，交 1号位/3号位 回执后收口。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）