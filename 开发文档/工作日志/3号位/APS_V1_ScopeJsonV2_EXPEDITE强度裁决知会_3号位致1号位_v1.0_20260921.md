# ScopeJsonV2 EXPEDITE 强度裁决知会 —— 3号位 致 1号位

> ⚠️ **收口标注（2026-09-24）**：本函已被 0号位《Q4-B：PriorityMode=EXPEDITE 强度定义裁决》（2026-09-23 正式裁决）**收口取代**。EXPEDITE 正式口径以 **Q4-B** 为唯一真源：EXPEDITE = Demand 级优先级调整（需求订单优先级调整 → 重新 Demand 排序 → 重新 Pegging → 重新生成 Task → 重新有限产能排程）；**不新增** `EXPEDITE_WEIGHT`/`SCORE`/`PriorityFactor` 权重字段；并附三类保护边界。本函仅保留「方案 a 保守插队」结论作历史归档，落地细节以 Q4-B 为准。

> **收件**：1号位（Solver，EXPEDITE 强度落地方）
> **抄送**：0号位（PM，裁决方）、2号位（数据引擎，投影透传）
> **日期**：2026-09-21
> **依据**：1号位《ScopeJsonV2 载荷契约消费回执 v1.0 20260921》§三「`PriorityMode = EXPEDITE` 强度｜待 0号位 裁决（1号位 推荐 a 保守插队；裁决前 pass-through）」；`DomainSolveRequest.cs:74` 注释锚「EXPEDITE 强度待 0号位 裁决」
> **性质**：**0号位 裁决知会**——EXPEDITE 强度已按 1号位 推荐方案 a 裁决，请 1号位 落地；裁决前 pass-through 行为维持不变
> **术语约定**：英文契约词一律「中文名称（英文）」；代码引 `文件:行`

---

## 〇、一句话

`PriorityMode = EXPEDITE`（加急）强度，**0号位 采纳 1号位 推荐方案 a（保守插队）**：Solver 在最早可行槽内**保守插入**加急单，不做激进前插、不改变其余作业的既有优先规则；落地前维持 pass-through（EXPEDITE 实值透传、Solver 行为与 NORMAL 一致）。

## 一、裁决结果（0号位，2026-09-21）

| 项 | 内容 |
|---|---|
| 背景 | 1号位 消费回执 §三 提请：`PriorityMode = EXPEDITE` 强度为 a/b/c 三选一裁决项，裁决前 pass-through（不改变现有行为） |
| 裁决 | **方案 a（保守插队）**——即 1号位 推荐项 |
| 口径 | EXPEDITE 单于最早可行时间槽**保守插入**（局部前插、不跳越硬约束/锁定任务）；不激进抢占、不推翻 1号位 既有 `EffectiveDue`/`RunScope` 消费逻辑；`DOMAIN_MANUAL_RESCHEDULE` 恒 null（走既有正式优先规则）不受影响 |
| 生效 | 裁决自本函发出即生效；**1号位 落地前，Solver 维持 pass-through**（`PriorityMode=EXPEDITE` 与 `NORMAL` 行为一致），不因本裁决引入未实现的行为改变 |

## 二、落地分工

| 号位 | 动作 |
|---|---|
| **1号位** | 按方案 a 在 Solver 落地 EXPEDITE 插队强度 + 测试锚（建议命名 `RunScopeConsumptionTests.*EXPEDITE*`，覆盖：保守插队命中最早可行槽 / 不跳越硬约束 / 与 NORMAL 对拍）；落地后回执 3号位 |
| 2号位 | 投影侧维持透传（`RunScope.PriorityMode` 实值不变，`DomainSolveRequest.cs` 无改） |
| 3号位 | 归档本裁决；若方案 a 落地需载荷侧补参（如插队深度/窗口参数），按红线 #5 契约先行提变更 |

## 三、请 1号位 回执

1. 方案 a 落地排期（含测试锚）；
2. 落地是否需载荷/契约补充（若需，3号位 承接契约变更）。

---

**发送人**：3号位 ｜ **日期**：2026-09-21
**待办**：① 1号位 方案 a 落地 + 回执 ②（如需要）契约变更经红线 #5 流程 ③ 3号位 归档
