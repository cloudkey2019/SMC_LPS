# C1 回执：ScopeJsonV2 十码验收 —— 2号位 致 3号位

> 发件：2号位（数据引擎）／收件：3号位（运行生命周期治理）／抄：0号位
> 日期：2026-09-20
> 验收对象：3号位《待2号位协助任务清单》C1 + 《待办清单处置回执》§一 ScopeJson v2 草案
> 结论：**验收不过（3 处需改）**，改完即过。
> 依据：V3.22 §41.2/§41.5 + 3号位实施包 §十九 + Delta §4.3/4.4/4.5/4.6（2026-09-15 冻结基线）

---

## 〇、一句话

**「十码」实为「八码」**——`BusinessTriggerType` 只有 **8** 个枚举值（`NEW_ORDER_CTP` ~ `DOMAIN_MANUAL_RESCHEDULE`），不是 10 个。「10」是 §41.2 的 10 个**场景行**，因 `NEW_ORDER_INSERT` / `EXISTING_ORDER_ADVANCE` 各拆 NORMAL/EXPEDITE 两行，trigger 轴坍缩为 8 码，`PriorityMode` 独立成轴。

---

## 一、权威八码（冻结 §十九 / Delta §4.3-4.4）

| BusinessTriggerType | RunType | Purpose | PriorityMode |
|---|---|---|---|
| NEW_ORDER_CTP | INSERT_ORDER_WHATIF | CTP | 固定 NORMAL |
| NEW_ORDER_IMPACT | INSERT_ORDER_WHATIF | INSERT_IMPACT_ANALYSIS | 固定 EXPEDITE |
| NEW_ORDER_INSERT | LOCAL_RESCHEDULE | INSERT_RESCHEDULE | NORMAL 或 EXPEDITE |
| EXISTING_ORDER_ADVANCE | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | NORMAL / EXPEDITE |
| GANTT_ADJUSTMENT | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | 固定 NORMAL |
| EQUIPMENT_FAILURE | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | 固定 NORMAL |
| RESOURCE_CALENDAR_CHANGE | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | 固定 NORMAL |
| DOMAIN_MANUAL_RESCHEDULE | MANUAL_RESCHEDULE | MANUAL_ADJUSTMENT | 既有正式优先规则（非 NORMAL/EXPEDITE） |

（一次 LOCAL 只允许一个 BusinessTriggerType；`GANTT/EQUIPMENT/RESOURCE_CALENDAR + EXPEDITE` 必须拒绝，§十九。）

---

## 二、三处需改

1. **「十码」→「八码」+ 首码名错**：任务清单 C1 写「`CTP_NORMAL` ~ `DOMAIN_MANUAL_RESCHEDULE`」——首码应为 **`NEW_ORDER_CTP`**，且共 **8 码非 10 码**。`CTP_NORMAL` 是把 Purpose `CTP` 误当成了 trigger 码名。
2. **`OrderTargets` / `TaskTargets` 需结构化（非 flat `string[]`）**：Delta §4.5 明确——
   - `OrderTargets = [{ OrderCanonicalId, ManualTargetDueDate }]`（ManualTargetDueDate 只属本次 Run、不改 ERP/Order_Canonical 正式 DueDate、NORMAL 下不得偷换为 Demand 排序字段）；
   - `TaskTargets = [{ TaskId, TargetTime }]`（TargetTime 软目标，Solver 可返回其它可行时间并给 Explanation）。
   - 草案 `IReadOnlyList<string>` 丢了 `ManualTargetDueDate` / `TargetTime` 两个关键载荷。
3. **`ChangedResourceIds` 类型**：Delta §4.5 为 `[12, 18, 33]` int 数组，草案写 `string[]` 应改 int/long。

---

## 三、两问答复（§一 待 2号位 对齐）

1. **承载方式**：**独立 DTO 确认**（`ScopeJsonV2` 独立 record，与 `CandidateRunCreateSpec` 分离）。但见「附加边界」——`RunSpec` 内嵌需处理 `BasePlanVersionId`。
2. **枚举取值**：**以 §41.2 + Delta §4.3/4.4 为准，8 码**（上表），`PriorityMode` 独立轴 `NORMAL / EXPEDITE`（DOMAIN_MANUAL_RESCHEDULE 除外）。

---

## 四、附加边界（3号位 定稿时一并处理）

- **`BasePlanVersionId` / `ExpectedDomainKeysJson` 不写回 ScopeJson**（§4.6 + §41.5 均明令：继续用 `ScheduleRun` 独立物理列）。草案 `RunSpec: CandidateRunCreateSpec` 内嵌含 `BasePlanVersionId`，若 `ScopeJsonV2` 直接序列化进 `ScheduleRun.ScopeJson` 会重复落此列——请 3号位 明确 `ScopeJsonV2` 是「创建入参 DTO」（可含 BasePlanVersionId）还是「ScopeJson 载荷」（须剔除 BasePlanVersionId / ExpectedDomainKeysJson）。
- **`DOMAIN_MANUAL_RESCHEDULE` 的 PriorityMode** 是「既有正式优先规则」，非 `NORMAL/EXPEDITE`——`PriorityMode` 枚举需处理该情况（可空 / 第三哨兵值），不能硬套二值。
- **单一真相约束**（§4.6）：若 ScopeJson 同时含旧 `OrderCanonicalIds` 与新 `OrderTargets[]`，必须 `OrderCanonicalIds == OrderTargets[].OrderCanonicalId 去重投影`，3号位 校验侧需加这条。

---

## 回执表

| 问 | 答复 |
|---|---|
| BusinessTriggerType 枚举数 | **8 码**（非 10），首码 `NEW_ORDER_CTP` |
| PriorityMode | 独立轴 NORMAL / EXPEDITE（DOMAIN_MANUAL_RESCHEDULE 走既有规则） |
| OrderTargets/TaskTargets 形态 | 结构化对象数组（含 ManualTargetDueDate / TargetTime） |
| ChangedResourceIds 类型 | int 数组 |
| 承载方式 | 独立 DTO（RunSpec 内嵌需处理 BasePlanVersionId 边界） |
| 验收结论 | 不过 → 3 处改完即过，3号位 即可落实体 + RunLifecycleService（M1） |

**发送人**：2号位 ｜ **日期**：2026-09-20
