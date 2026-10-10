# APS V1 Setup 规则承载重构——1号位 P-1 零改动确认件

**文档性质**：跨号位确认件（1号位 收）——Setup 规则承载重构（独立物理表 → `RuleSetVersion.ContentSnapshotJson.SetupTransitionRules` 子块）对排程消费端的影响确认。
**发送方**：3号位 ｜ **日期**：2026-09-23
**依据**：重构方案 `APS_V1_SetupTransitionRule重构方案_复用RuleSetVersion快照承载_3号位_v1.0_20260922.md`（S-1~S-9 已全绿）；契约登记件 `APS_V1_Setup规则承载契约登记件_3号位_v1.0_20260923.md`
**抄送**：0号位（备查）

---

## 一、结论

**1号位 零代码改动。** `SetupOptimizer` 消费链路（BuildRuleLookups / ResolveSetup / SetupOutcomeToSource）不读物理表，只消费 `FrozenStrategySnapshot` 第⑦块快照（`SetupTransitionRuleSnapshot`）；本次重构仅改快照装载端的数据来源（物理表 → 子块），对 1号位 完全透明。需 1号位 **P-1 回执确认**。

## 二、证据链（代码现场）

| 消费点 | 位置 | 重构前 | 重构后 | 1号位 影响 |
|---|---|---|---|---|
| 快照消费入口 | `PhaseOneConstraintBuilder` :703-705 `SetupOptimizer.BuildRuleLookups(request.StrategySnapshot?.SetupTransitionRules)` | 第⑦块（快照） | 第⑦块（快照） | **无感** |
| 规则解析骨架 | `SetupOptimizer.cs` :26-34 头注释「本骨架不自建规则读取、不读 3号位 数据库」 | `BuildRuleLookups(IEnumerable<SetupTransitionRuleSnapshot>)` + `ResolveSetup` | 不变 | **无感** |
| 命中语义 | `SetupOptimizer`：EXACT 产品对优先 → DEFAULT 工序+设备 → 无规则 0 分钟 + 追踪 | 快照 | 快照 | **无感** |
| 快照装载 | `FrozenStrategySnapshotProvider` :116-119 第⑦块 = `SetupTransitionRuleProjector.ProjectFromSnapshot(ContentSnapshotJson)`（S-4/S-8） | 原由 PeggingOrchestrator 从物理表读取 | 子块反序列化 + IsActive=true 过滤 | **无感**（装载源改道） |
| SetupSource 填充 | `PhaseTwoInitialScheduler` / `PhaseFourLocalRepair` / `PhaseFiveCompression`（`SetupOutcomeToSource`） | 计算产物 | 计算产物 | **无感** |

## 三、语义不变点（P-1 确认内容）

1. **命中三层不变**：EXACT 产品对 → DEFAULT 工序+设备 → 无规则 0 分钟 + 「规则缺失」解释；同产品 A→A 默认 0 分钟。
2. **快照结构不变**：`SetupTransitionRuleSnapshot`（业务键 + SetupMinutes + RuleType），仅投影 `IsActive=true`，无审计字段。
3. **fail-open 语义**：子块缺失/为空/损坏 → 空规则列表 → **不阻断求解**；`SetupSource` 落库按上下文落 4 态之一（非统一 DEFAULT）：无上一产品→`INITIAL_SETUP_STATE`；同产品连续→`DEFAULT`；有前后产品且 EXACT/DEFAULT 均缺失→`SETUP_RULE_MISSING_ZERO_FALLBACK`（WARNING，进 4号位 数据质量查询）。与 DemandPriority 等六块 fail-closed 区分——Setup 为可选块。
4. **SetupSource 落库为 4 态**（0号位 20260922 裁决）：`SetupOutcomeToSource` 做 **5→4 归并**——`SetupOutcome` 内部枚举 5 值（ExactHit/DefaultHit/SameProductZero/InitialState/RuleMissing）→ 落库 `SetupSource` 4 态（`SameProductZero→DEFAULT`）；填充链路结构不变、值域为 4 态。

## 四、回执栏（1号位 填写）

| 项 | 确认 |
|---|---|
| P-1 快照第⑦块消费语义不变（命中三层/结构/fail-open） | ☐ 确认 |
| 零代码改动无工作项 | ☐ 确认 |
| 备注 |  |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**待办**：1号位 P-1 回执 → 2号位 D-1 DROP 闭环
