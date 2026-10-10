# Setup 两预算参数已发布，请投影 —— 3号位 致 2号位

> 发件：3号位（规则/参数/运行生命周期治理）／收件：2号位（数据引擎）／抄：1号位、0号位
> 日期：2026-09-20
> 触发：1号位《Setup 预算参数性能标定报告》§六（2号位 投影动作）＋ 2号位《Q-C 回执》§四附带发现（PS-DEMO-V2 缺两键）
> 请求：**请 2号位 按冻结契约投影/透传含两预算参数的 Setup 块**

---

## 一、参数侧已收口（3号位 完成）

| 项 | 状态 |
|---|---|
| 新参数集版本 | `PS-DEMO-V3`（PUBLISHED，两键 `SetupSearchBudget`=500 / `SetupMaxNeighborhoodTries`=50） |
| 策略包版本 | `SP-DEMO-V3.0` 接棒 `IsDefault=1`；旧 `SP-DEMO-V2.0`/`PS-DEMO-V2` 关窗口收口 |
| 数据侧 | 库内已实落两键，不再靠 C# 默认兜底（治理与实值重新挂钩） |
| 契约锚 | `FrozenStrategySnapshot.cs:237-238`（`SetupParams` 两 int 字段，默认 500/50） |

## 二、请 2号位 动作

1. **投影/透传**：将 `FrozenStrategySnapshot.SolverStrategyBlock.Setup` 的两预算参数（`SetupSearchBudget` / `SetupMaxNeighborhoodTries`）按冻结快照透传至 1号位 消费入口（`SetupOptimizer` 换读点）。
2. **收口知会**：完成后知会 1号位 进行换读验证（对齐其标定工装 `LPS.APS.Tests/Benchmarks/SolverBenchmark.cs` 复跑三点矩阵）。

## 三、口径备忘

- 默认值：`SetupSearchBudget`=**500**、`SetupMaxNeighborhoodTries`=**50**（1号位 标定平衡点，未改动）。
- 允许范围：`[100, 5000]` / `[10, 500]`（`SolverStrategyValidator` 发布前校验，代码侧）。
- 量纲：均为「次数」，非毫秒（保证同种子可重放）。

---

## 回执表

| 问 | 答复 |
|---|---|
| 参数侧是否已发布 | 是，`PS-DEMO-V3`（500/50）+ `SP-DEMO-V3.0` IsDefault |
| 2号位 待动作 | 投影/透传 Setup 两预算参数至 1号位 消费入口 + 知会换读验证 |
| 值口径 | 500/50，范围 [100,5000]/[10,500]，次数量纲 |

**发送人**：3号位 ｜ **日期**：2026-09-20