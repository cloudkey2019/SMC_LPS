# Setup 预算参数已冻结回执 —— 3号位 致 1号位

> 发件：3号位（规则/参数/运行生命周期治理）／收件：1号位（排程算法）／抄：0号位、2号位
> 日期：2026-09-20
> 回应：1号位《APS_V1_Setup换型_性能标定报告与预算参数修订提值_1号位致3号位_v1.0_20260920》
> 结论：**值维持、无改动；§六② 补键已出 PS-DEMO-V3 并回改 V2 seed，已执行。**

---

## 一、值对齐（§三 无改动）

| 参数 | 1号位 修订提值 | 3号位 现状 |
|---|---|---|
| `SetupSearchBudget` 默认 | 500（维持） | 已冻结 500 |
| `SetupSearchBudget` 允许范围 | [100, 5000]（维持） | `SolverStrategyValidator` [100, 5000] |
| `SetupMaxNeighborhoodTries` 默认 | 50（维持） | 已冻结 50 |
| `SetupMaxNeighborhoodTries` 允许范围 | [10, 500]（维持） | `SolverStrategyValidator` [10, 500] |

红线实证（§三）抄悉：最不利点 220s ≪ 900s，有界搜索 + 不破夜间总目标双达标；证明当前 500/50 为收益/耗时平衡点，无需调整。

## 二、§六② 附带发现——已处置

库内 `PS-DEMO-V2` Setup 子块缺 `SetupSearchBudget` / `SetupMaxNeighborhoodTries` 两键之问题已解决：

- 出 **PUBLISHED 新版 `PS-DEMO-V3`**（由 V2 快照 `JSON_MODIFY` 仅替换 `$.SolverStrategy.Setup` 为两键=500/50 + 剔除 `MaxIterations` 死键）；
- 同步回改 V2 的 seed（`APS_联调种子数据_策略包_20260901.sql`）Setup 子块；
- `SP-DEMO-V3.0` 接棒 `IsDefault=1`，旧 `SP-DEMO-V2.0` 收口 `IsDefault=0` + 关窗口。

两 SQL 已执行。治理值（500/50）与库内实值重新挂钩，不再靠 C# 默认兜底。

## 三、范围落点说明

「允许范围」落在 Application 侧 `SolverStrategyValidator`（发布前校验），非 `ParameterSetVersion.ContentSnapshotJson` 载荷内字段——沿用既有模式。

---

## 回执表

| 问 | 答复 |
|---|---|
| §三 值是否需改 | 否，均「维持」，3号位 已冻结一致 |
| §六② 补键 | 已出 PS-DEMO-V3（500/50）+ 回改 seed，已执行 |
| 允许范围落点 | `SolverStrategyValidator`（代码校验，非 JSON 载荷） |
| 3号位 待办 | 无新增 |

**发送人**：3号位 ｜ **日期**：2026-09-20