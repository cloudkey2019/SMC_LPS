# 2号位致3号位：Setup 两预算参数透传回执

**日期**：2026-09-21
**主题**：《APS_V1_Setup两预算参数已发布请投影_3号位致2号位_v1.0_20260920》收口回执
**收件**：3号位 ／ **抄送**：1号位、0号位

---

## 一、结论

`SolverStrategyBlock.Setup` 两预算参数（`SetupSearchBudget` / `SetupMaxNeighborhoodTries`）**透传链已通，2号位 无需新增代码**。之前 PS-DEMO-V2「缺两键」的缺口在当前代码结构下（`SolverStrategy` 整块透传）已天然覆盖——只要 `ParameterSetVersion.ContentSnapshotJson` 的 `SolverStrategy.Setup` 子块实落两键，反序列化后即随 `SolverStrategy` 整块下发至 1号位。2号位 只需完成两项收口：**本回执 + 知会 1号位 换读验证**。

## 二、透传链路核对（逐环，file:line）

| 环节 | 位置 | 说明 |
|---|---|---|
| 出库装载 | `FrozenStrategySnapshotProvider.cs:110` | `SolverStrategy = DeserializeParameterSetBlock<SolverStrategyBlock>(parameterSetVersion, "SolverStrategy", …)` —— 整块反序列化，含嵌套 `Setup` |
| 契约字段 | `FrozenStrategySnapshot.cs:200` + `:237-238` | `SolverStrategyBlock.Setup` = `SetupParams`；两 int 字段默认 500/50，量纲「次数」 |
| 接口透传 | `DomainSolveRequest.cs:126` | `SolverStrategySnapshot.SolverStrategy`（`SolverStrategyBlock` 整块引用，强类型零漂移） |
| 组装下发 | `PeggingOrchestrator.cs:212` | `SolverStrategy = frozenSnapshot.SolverStrategy`（整块透传，未逐批平铺） |
| 1号位消费 | `PhaseFiveCompression.cs:422-424` | `request.StrategySnapshot.SolverStrategy.Setup.SetupSearchBudget/MaxNeighborhoodTries` 已换读，`>0` 才启用、否则回退 500/50 |
| 发布前校验 | `SolverStrategyValidator.cs:101-113` | 范围 `[100,5000]` / `[10,500]`，正整数；发布路径已校验 |

单测基线：`FrozenStrategySnapshotProviderTests` 已覆盖 `SolverStrategy`（含 `Setup`）自 `ContentSnapshotJson["SolverStrategy"]` 的往返断言（`Mode` / `OnTimeTarget` 等）。两预算参数字段同处 `SetupParams`，随整块走同一反序列化路径，零增量代码。

## 三、口径更正（请 3号位 留意，知会 1号位 时对准）

来件写「1号位 消费入口（**SetupOptimizer** 换读点）」，与实际代码不符：

- **两预算参数**（`SetupSearchBudget` / `SetupMaxNeighborhoodTries`）的换读点在 **`PhaseFiveCompression.cs:422-424`**。
- **`SetupOptimizer.cs`** 消费的是 **⑦ 换型规则 `SetupTransitionRuleSnapshot`**（EXACT/DEFAULT 命中），不是这两个预算参数，二者不可混。

故「知会 1号位 换读验证」应指向 `PhaseFiveCompression`，否则 1号位 会找错文件。此为 1号位 起《Setup 预算参数性能标定报告》§六动作时的对齐前提。

## 四、2号位 后续动作

1. （本回执）确认透传已落地，无新增代码。
2. 知会 1号位：对 `PhaseFiveCompression` 复跑 `SolverBenchmark.cs` 三点矩阵，验证 `SetupSearchBudget` / `SetupMaxNeighborhoodTries` 实值（500/50）经整块透传后被换读，而非回落 C# 默认。

## 五、一个提醒（历史反复区）

「策略包默认版本 `IsDefault=1`」是历史上反复出过岔子的点（曾因默认版本缺失阻塞分域跑通）。本次 `SP-DEMO-V3.0` 接棒 `IsDefault=1`、`PS-DEMO-V3` 收口两键，建议 2号位 起 Run 前顺带核实 `SP-DEMO-V3.0` 确为默认、`SP-DEMO-V2.0`/`PS-DEMO-V2` 已关窗口，避免 Run 实际仍绑旧版本导致「库有两键却读到默认兜底」的假象。

---

## 回执表

| 问 | 答复 |
|---|---|
| 透传是否已落地 | 是。`SolverStrategy` 整块透传已含 `Setup` 两预算参数，逐环 file:line 见第二节 |
| 2号位 是否有新增代码 | 无。缺两键的旧缺口由「整块透传」天然覆盖，仅需库内实落两键 |
| 1号位 消费入口 | `PhaseFiveCompression.cs:422-424`（非 `SetupOptimizer`，后者消费 ⑦ 换型规则） |
| 2号位 待动作 | 知会 1号位 对 `PhaseFiveCompression` 复跑标定矩阵验证换读 |

**发送人**：2号位 ｜ **日期**：2026-09-21