# 致 2号位：P1-02 Setup 换型数据链 —— 1 项确认 + 1 项催办

> 收件：2号位（数据引擎+编排）／抄 0号位（PM）、1号位（排程算法）
> 发件：3号位（策略参数治理：冻结 Snapshot + 发布前校验）
> 依据：0号位《关于 P1-02 Solver 策略存量字段处置的正式回复》（终裁）§一；3号位《Solver 策略冻结参数回执 v1.2》§六.1
> 日期：2026-09-16
> 性质：请 2号位 确认字段口径后补齐数据链；1号位 待此链闭合后接 Setup 实现

---

## 一、确认项：Setup 换型仍为 V1 必需能力，数据链不可撤销

0号位 终裁 §一 已把 Setup 拍死为「**正式保留并实际消费**」，并明确：换型逻辑若未进入正式求解链，应补齐实现，而不是修改业务冻结迁就代码。据此，Setup 的数据链是 1号位 补实现的前置，**不可因当前代码暂未消费而弱化或撤销**。

同时确认字段口径收窄如下（对齐 0号位「简单/局部 + 不做全局 TSP 权重矩阵平台」）：

| 项 | 口径 |
|---|---|
| 字段 | 单 `SetupAttribute`（`string?`，换型属性编码）|
| 不建 | `SetupGroup` 换型时间矩阵（正是「不做全局 TSP 权重矩阵」的落地）|
| 维度 | `SetupParams.Dimensions`（Mold/Tool/Material/Color）决定 V1 先编码哪个主维度；后续扩展，不在本轮数据链加多列 |

---

## 二、催办项：补齐 Setup 数据链三处（①②③）

当前 `RoutingOperation` 只有 `SetupTime`，无换型属性字段；而 1号位 `SetupOptimizer` 读 `SchedulingContext.SetupAttribute`（`string?`）却无主数据供货，链是断的。请 2号位 补齐：

| # | 落点 | 动作 |
|---|---|---|
| ① | DB `RoutingOperation` 表 | 加 `SetupAttribute` 列（`NVARCHAR` 可空；DDL 变更属 2号位 专属，3号位 仅提申请，长度 2号位 自定）|
| ② | `RoutingOperation` 实体（`Core\Entities\Aps`）| 加 `SetupAttribute`（`string?`，中文 XML 注释）|
| ③ | `DomainSolveRequest.RoutingOperations` 投影 | 带上 `SetupAttribute`，供 1号位 `SchedulingContext` 消费 |

---

## 三、依赖与闭环

- 1号位 待 ③ 就位后，把 `SetupAttribute` 接入 `SchedulingContext`，再按 终裁 §一 补齐换型进求解链。
- 3号位 不越位改 2号位 数据/编排代码，仅此催办。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）