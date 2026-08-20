# APS V1 六块 Snapshot 持久化来源映射表 + 最小 DDL 差异

> 版本：v1.0
> 日期：2026-08-19
> 提出方：3号位
> 依据：《APS_V1_3号位代码综合符合性审核报告_v1.1_20260819》P0-01
> 性质：技术确认文档，**非业务重新打开**。供 0/2/3 号位确认最小技术落位，确认前 3号位不自行 ALTER 正式 DDL。

---

## 一、冻结 DDL 事实（v5.1.2 冻结对齐版）

六张治理表（RuleSet / RuleSetVersion / ParameterSet / ParameterSetVersion / StrategyProfile / StrategyProfileVersion）均为**版本、发布、组合、状态元数据**，无业务内容字段：

| 表 | 关键字段 | 有无业务内容 |
|---|---|---|
| RuleSet | RuleSetCode/Name/Description/IsActive/审计 | 无 |
| RuleSetVersion | VersionCode/Status/EffectiveFrom/To/Published/Approved/Created | **无** |
| ParameterSet | ParameterSetCode/Name/Description/IsActive/审计 | 无 |
| ParameterSetVersion | 同 RuleSetVersion 结构 | **无** |
| StrategyProfile | Code/Name/RunType/IsActive/审计 | 无 |
| StrategyProfileVersion | RuleSetVersionId + ParameterSetVersionId + **IsDefault** + 版本/发布元数据 | **无（仅组合引用）** |

冻结 DDL 中**不存在**独立的主题规则表（如 DemandPriorityConfig / LockConfig / SupplyConfig 等），也**不存在**版本内容快照字段。

---

## 二、当前代码事实（3号位现有实现）

| Snapshot 块 | 当前代码来源字段 | 表 | 该字段是否存在于冻结 DDL |
|---|---|---|---|
| DemandPriority | `RuleSetVersion.DemandPriorityJson` | RuleSetVersion | ❌ 不存在 |
| Lock / Protection | `ParameterSetVersion.LockJson` | ParameterSetVersion | ❌ 不存在 |
| Supply / PI Sort | `ParameterSetVersion.SupplyJson` | ParameterSetVersion | ❌ 不存在 |
| Procurement | `ParameterSetVersion.ProcurementJson` | ParameterSetVersion | ❌ 不存在 |
| SolverStrategy | 空对象 `new SolverStrategyBlock()` | — | —（无真实来源） |
| CandidateGuardrail | 空对象 `new CandidateGuardrailBlock()` | — | —（无真实来源） |
| PlanningYield | 隐含于 ProcurementJson | ParameterSetVersion | ❌ 不存在 |

---

## 三、映射表（要求：P0-01 表格）

| Snapshot 块 | 当前业务字段来源（语义） | 当前是否可按 VersionId 重放 | 缺口 |
|---|---|---|---|
| DemandPriority | Segment 列表：SegmentOrder/MatchConditions/SortFields/StableTieBreak | ⚠️ 代码可重放，但物理字段不在冻结 DDL | 冻结 DDL 无承载列 |
| Lock / Protection | Trigger/Sticky 参数（阈值、VIP 值、受保护 OrderType 等） | ⚠️ 同上 | 冻结 DDL 无承载列 |
| Supply / PI Sort | Warehouse 优先级、PI SortBy、工厂/产品族上下文要求 | ⚠️ 同上 | 冻结 DDL 无承载列 |
| Procurement | DefaultPurchaseLt/OverdueMargin/ArrivalToUsableOffsets | ⚠️ 同上 | 冻结 DDL 无承载列 |
| PlanningYield | Material/Stage 级 YieldPercent | ⚠️ 同上 | 冻结 DDL 无承载列 |
| SolverStrategy | Mode/Bottleneck/OnTimeTarget/Split/Setup/StageOverlap | ❌ 当前空对象，无来源 | 无来源、无承载列 |
| CandidateGuardrail | Normal/Soft/Hard Ms、Repair/Propagation、ResourceTopN 等 | ❌ 当前空对象，无来源 | 无来源、无承载列 |

---

## 四、技术冻结缺口确认

> **冻结业务要求"历史版本可重放"（发布后不可变、可按 StrategyProfileVersionId 恢复当次 Run 完整 Snapshot），但冻结 DDL 中：主题规则表与 RuleSetVersion/ParameterSetVersion 之间无版本绑定字段；六张治理表自身又无"版本内容快照"字段。**

此缺口为**技术性冻结遗漏**，非业务重新打开，也非 3号位代码可自行决定。

---

## 五、推荐最小技术方向（供 0/2/3 确认）

优先考虑（0号位报告 §5.4 推荐方向）：

> **主题表继续负责可维护业务字段；RuleSetVersion / ParameterSetVersion 只增加最小"发布内容快照/可重放载体"，不增加大量主题专用列，不新建多套版本表。**

候选落位方案：

| 方案 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| A. 版本表加内容快照字段 | RuleSetVersion 增 1 个规则内容 Snapshot 列；ParameterSetVersion 增 1 个参数内容 Snapshot 列 | 最简、满足全部约束（发布不可变/按 VersionId 重放/不新增表/不新增版本体系） | 各块内容聚合成一个 JSON 大字段 |
| B. 主题规则表 + 版本外键 | 建 DemandPriorityConfig 等主题表，带 RuleSetVersionId 外键 | 字段级可维护、可查询 | 新增多张表，DDL 改动大 |
| C. 保持现状（3号位现行 JSON 列） | 直接把 JSON 列补进冻结 DDL | 代码改动最小 | 与 0 号位"不增加大量主题专用列"方向冲突 |

**0 号位约束（必须全部满足）**：
1. 发布后不可变；
2. 能按 StrategyProfileVersionId 恢复当次 Run 完整 Snapshot；
3. 不新增独立 Snapshot 表；
4. 不新增新的版本号体系；
5. 不把主题规则表废掉；
6. 不建设 DSL/RuleCondition/RuleAction 平台。

---

## 六、待 0 号位确认项

1. 采用方案 A / B / C 中哪一种；
2. 若 A：Snapshot 内容字段命名与格式（单一 JSON 聚合 vs 分块）；
3. 若 B：主题表清单与版本绑定字段；
4. 是否需要在版本表补充 ChangeReason / 审计落点。

---

## 七、确认前 3号位承诺

- 不自行 ALTER 正式 DDL；
- 不继续扩写依赖非冻结字段的 Repository SQL；
- 待 0 号位确认后一次性补字段/文档，再继续对应代码整改。

> 关联：`APS_V1_3号位代码审查问题跟踪_v1.0_20260819.md`（A-2/A-3 裁决）、`APS_V1_3号位代码综合符合性审核报告_v1.1_20260819.md`（P0-01）。
