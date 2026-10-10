# 回执：P1-02 item 4 两项确认 —— 3号位 致 1号位

> 收件：1号位（排程算法）／抄 0号位（PM）、2号位（数据引擎+编排）
> 发件：3号位（策略参数治理：冻结 Snapshot + 发布前校验）
> 依据：1号位《P1-02 回执答复》（2026-09-16）中之「给 3号位（2 项确认）」
> 日期：2026-09-16
> 性质：两项确认落定，1号位 据此补齐 item 4（BottleneckMode 四模式）实现

---

## 一、确认 1：锚点资源源 = 策略参数，字段 `AnchorResourceCode`（`string?`，非 `int?`）

- **归属**：策略参数，归 3号位 冻结（0号位 补充裁决「Bottleneck Override 属 3号位冻结、2号位传入、1号位消费」）。
- **字段**：`AnchorResourceCode`（`string?`，资源**业务编码**），冻结默认 `null`（未指定 → 等价 `Auto`）。
- **不采纳 `AnchorResourceId int?`**：冻结快照全体实体引用均为业务编码 `string`（`WarehouseCode` / `MaterialId` / `StageCode`）；`int ResourceId` 是同步期代理键、跨环境不稳定、无法做参数治理；`code→id` 反转由 1号位 在 Phase3 自建（其回执 §四已认领）。

四模式 → 字段语义：

| `BottleneckMode` | `AnchorResourceCode` 语义 |
|---|---|
| `Auto` | 忽略该字段，按 `BottleneckUtilizationThreshold`(0.85) 自动识别 |
| `PreferAnchor` | 优先锚点＝该编码资源（不可行时回退 Auto）|
| `ForceAnchor` | 强制锚点＝该编码资源 |
| `NotAnchor` | 从锚点候选排除该编码资源 |

字段已落地于 `FrozenStrategySnapshot.SolverStrategyBlock.AnchorResourceCode`（`dotnet build` 0 错误），待提交推送后随 2号位 整块透传，1号位 Phase3 消费。

## 二、确认 2：AllowSplit 真源

- 真源 = `Split.MaxOptimizationSplitCount > 1`（推导布尔，**非独立冻结字段**）。
- 冻结值 `MaxOptimizationSplitCount = 3` → `AllowSplit = true`。
- 投影 = `Parameters.AllowSplit`（`DomainSolveRequest.FiniteCapacityParameters.AllowSplit`）。
- 改行为走 `MaxOptimizationSplitCount` / `MinBatchQty`（后者已落 `0.1m`），不另设 `AllowSplit` 开关。

## 三、早做惩罚 / 计划稳定性

已由 0号位 裁决（Level 3 次级目标、不新增业务权重参数），3号位 不补权重字段。措辞红线沿用：不得写成「从 V1 删除」。

## 四、回执表

| 收件 | 动作 |
|---|---|
| 1号位 | 据此补齐四模式：`Auto` 忽略 `AnchorResourceCode`；`Prefer/Force/NotAnchor` 读编码转 `ResourceId` |
| 2号位 | 随 `SolverStrategy` 整块透传（无需新增投影行）+ 校验编码合法性 |
| 3号位 | 字段已落地、待提交推送 |

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）