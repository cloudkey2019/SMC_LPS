# 致 2号位：MaxIterations 已从 2↔3 冻结块摘除 —— 请同步清投影 + FiniteCapacityParameters 字段

> 收件：2号位（数据引擎+编排）／抄 0号位（PM）、1号位（排程算法）
> 发件：3号位（策略参数治理）
> 依据：0号位 裁决「工程阈值归 1号位、不搬 3号位」；1号位 2026-09-16 另提示「请 3号位 一并摘除」
> 日期：2026-09-16
> 性质：3号位 侧已摘除契约字段，2号位 请同步清两处投影/字段，完场三层一致

---

## 一、3号位 侧已完成（就绪）

| 落点 | 动作 | 状态 |
|---|---|---|
| `FrozenStrategySnapshot.SolverStrategyBlock.MaxIterations` | 删除字段 | ✅ 已摘除 |
| `SolverStrategyValidatorTests.E4_MaxIterations_非正_拒绝` | 删除测试 | ✅ 已摘除 |

> 整块透传 `PeggingOrchestrator.cs:193`（`SolverStrategy = frozenSnapshot.SolverStrategy`）在字段摘除后**自动不再携带 MaxIterations**，该行无需改动。

## 二、2号位 侧待办（请一并清，否则 Application 层编译红）

| # | 落点 | 动作 |
|---|---|---|
| ① | `LPS.APS.Application/Services/PeggingOrchestrator.cs:199` | 删除投影行 `MaxIterations = solverStrategy.MaxIterations,` |
| ② | `LPS.APS.Core/Dto/DomainSolveRequest.cs:237` | 删除 `FiniteCapacityParameters.MaxIterations` 字段 |
| ③ | `DomainSolveRequest.cs:119、123` | 同步清注释里对 `MaxIterations` 的提及 |

## 三、编译预期（三方交接窗口）

摘除后 `Application` 层在 `PeggingOrchestrator.cs:199` 会短暂编译红（`solverStrategy.MaxIterations` 已不存在），**待 2号位 删 ① 即绿**；`Core` 层 3号位 侧自洽（字段与测试同删）。

## 四、顺带清理（可选，建议同批）

联调种子 `开发文档/2026-09-01文档更新/APS_联调种子数据_策略包_20260901.sql` 两处（:148、:180）`SolverStrategy` JSON 含 `"MaxIterations":1000`，字段摘除后建议一并剔除，避免反序列化遇未知键（视 `JsonSerializerOptions` 是否忽略未知成员而定，属种子数据一致性，非编译阻断）。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）