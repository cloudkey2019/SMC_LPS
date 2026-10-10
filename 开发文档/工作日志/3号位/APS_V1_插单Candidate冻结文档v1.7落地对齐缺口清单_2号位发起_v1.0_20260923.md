# 插单 / Candidate 冻结文档 v1.7 落地对齐缺口清单（2号位 发起）

**版本**：v1.0
**日期**：2026-09-23
**发起人**：2号位（数据引擎 · 主执行窗口）
**致**：0号位（PM）、1号位（Solver）、3号位（治理 / ScopeJson Schema Owner / Auth·Business Scope Owner）
**抄送**：4号位（对象采集）、5号位（BOM / 数据）
**性质**：插单 / Candidate 相关冻结文档**已冻结过一次**，2号位 逐条对照后发现若干**落地对齐真空点**（文档口径未写死或与实现漂移），请各号位认领配合。非「重开冻结决策」，均为「把已冻结口径补成可落码的字段 / 触发层 / 载体」。

---

## 依据（冻结文档范围）

- 《APS V1 最终全部流程与业务基线 v1.7》§67 Candidate运行输入与变化Seed、§68 各业务场景运行语义（68.1 CTP / 68.2 急插 / 68.3 已有订单提前 / 68.7 Business Scope）、§69 影响范围由1号位动态判断
- 《APS V1 2号位增量开发实施包 v1.8》§六 Seed与Scope处理、§七 PriorityMode处理
- 《APS Pegging供需承接与分层计算业务说明 v1.5（白天业务场景 / Candidate人工调整冻结对齐版）》§16.3 NORMAL/EXPEDITE、§16.4 已有订单人工计划目标日期

---

## 一、真空点清单

| # | 缺口 | 冻结依据 | 现状（2号位 已核） | 待配合方 / 动作 |
|---|---|---|---|---|
| ① | **CTP / 急插「新订单」在 ScopeJson 的承载字段未写死** | v1.7 §67「多订单目标集合」+ §68.1 CTP「针对已进入事实的新订单」+ v1.5 §16.4 OrderTargets 仅定义「已有订单提前」；§67「其它新订单不自动进入」与 §68.1「靠事实识别」两句间有张力 | 2号位 现按「订单数据源钉基线 + 重新 Pegging + ChangeSeed 差异集捕获」让新订单进候选，**无显式 NEW_ORDER 字段** | **3号位**：定 CTP/急插新订单是复用 `OrderTargets[].OrderCanonicalId` 显式声明，还是新增 `NEW_ORDER_*` 承载；并明确 §67「明确选择」的字段形态 |
| ② | **§16.4「`OrderCanonicalIds` 与 `OrderTargets[]` 并存须一致」，但 ScopeJsonV2 无 `OrderCanonicalIds` 字段** | v1.5 §16.4 末「如果 `OrderCanonicalIds` 与 `OrderTargets[]` 同时存在，两者订单集合必须一致」 | 3号位 交付 `ScopeJsonV2` 仅 `OrderTargets[]`（OrderCanonicalId 内嵌），**无独立 `OrderCanonicalIds` 字段** | **3号位**：明确 `OrderCanonicalIds` 是已合并进 `OrderTargets`（则冻结文档 §16.4 表述过时、需登记更正），还是遗漏字段需补 |
| ③ | **变化 Seed 的消费契约 + 「范围不足」拒收触发层未写死** | v1.7 §69「影响范围由1号位动态判断」+ §68.7「真实可行方案须移动无权 Task → 返回『当前 Business Scope 不足』」 | 2号位 已传 `CandidateContext.ChangeSeedKeys` + `RunScope.TaskTargetOverrides`/`ChangedResourceIds`；但「从 Seed 传播出可移动集合 + Scope外 Anchor/Block」算法、「范围不足」拒收挂哪层（1号位 vs 2号位）**均未冻结** | **1号位**：确认消费 `ChangeSeedKeys` 入口 + InScope→Anchor 补集推导 + 拒收归属（1号位回执 §七 已点名 (a)(b)）；**0号位**：补冻结「范围不足拒收」触发层 |
| ④ | **Business Scope 边界如何进求解契约，载体缺失** | v1.7 §68.7「局部 Candidate 不得越过当前用户 Business Scope；Scope外 Task 不得被移动、仍作真实占用」 | `DomainSolveRequest.RunScope` / `CandidateContext` **均无 BusinessScope 字段**，2号位 无从把用户权限边界投影给 1号位 | **3号位**：确认 Business Scope（用户权限边界）来源与数据结构；**0号位**：补「Business Scope 载体字段进 RunScope」口径 |
| ⑤ | **Candidate 差异集是否跨 PlanVersion 依赖 AllocationSequence（稳定键）** | PM 0918-2「先让 1号位 确认差异集是否跨 PlanVersion 依赖 AllocationSequence，不存在不加 BusinessDemandKey」 | 2号位 `ChangeSeedKeys` 现按 DemandKey 差异集生成；跨版本对齐是否受 AllocationSequence 重排影响未确认 | **1号位**：回「Candidate 差异集是否跨 PlanVersion 依赖 AllocationSequence」→ 决定是否需 `BusinessDemandKey` 稳定键 |
| ⑥ | **新订单白天 BOM 何时就绪（外围前置）** | P5（台账登记，非插单文档本身） | BOM=5号位 做（`PullBOMResultFromODSAsync` 仅夜间 FULL）；新订单白天插单时 BOM 就绪时机未拍 | **0号位**：拍「白天增量 vs 插单按需拉」；**5号位**：按裁定实现 |

---

## 二、2号位 已就绪（供各方核对，非缺口）

- `CandidateContext{BasePlanVersionId, ChangeSeedKeys, ExternalDomainResourceBlocks}`（`PeggingOrchestrator.BuildCandidateContextAsync`）
- `RunScope{Trigger, PriorityMode, InScopeLogicalDemandKeys(留空), DueDateOverrides, TaskTargetOverrides, ChangedResourceIds}`（`BuildRunScopeAsync`）——`InScopeLogicalDemandKeys` 留空为冻结 v1.7 §69 正确语义（范围由 1号位 从 Seed 动态推导，2号位 只转 Seed）
- EXPEDITE 前置竞争层（`DemandPriorityExecutor`，S5 已落 + 单测 349/349 绿）
- ScopeJson v2 反序列化 + OrderTargets 存在性校验（M4）

---

## 三、期望

1. **1号位** 回 ③⑤ 两项（消费契约 + AllocationSequence 依赖），并给出「从 Seed 传播出可移动集合」的最小契约。
2. **3号位** 回 ①②④ 三项（新订单承载字段、OrderCanonicalIds 字段去留、Business Scope 来源与结构）。
3. **0号位** 对 ③④⑥ 中「触发层 / 载体字段 / BOM 白天机制」三处补冻结文档或裁决。

以上为 2号位 逐条对照冻结文档后的**落地对齐真空点**，不涉及重开已冻结业务；请各方按 # 认领，回执落 `02_派单与回执/`。

---

**发起人**：2号位 ｜ **日期**：2026-09-23
