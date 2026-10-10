# 回执（v2.0 更正）：待办清单六项处置 —— 3号位 致 2号位

> 收件：2号位（数据引擎+编排）／抄 0号位（PM）、1号位、5号位
> 依据：2号位《答复 v1.1》＋《Lean APS 研发职责与执行任务包 v1.2》＋ 冻结基线《核心排产全流程走查 V3.22 §41.2/§41.5》
> 日期：2026-09-18
> 性质：认领「定责三分法」（更正 v1.0 对 #3/#4 的错误退回）；ScopeJson v2 定稿；#3/#4 判定接受并给方法与取证需求
> 术语约定：英文契约词一律「中文名称（英文）」；文档述引「§符号锚」，代码引 `文件:行`。

---

## 〇、一句话

我 v1.0 把 #3/#4 退回 2号位 **是错的**。核对职责任务包 v1.2 明文（v1.1 更新说明 + §MES 工艺视图创建）——

- **契约插座（Socket）**：MES DBA 维护 28 张离散工艺表（源）；
- **数据插头（Plug）**：**3号位 创建 `MES_APS_Routing_View`**，统一映射为标准格式；
- **数据装载（Loader）**：2号位 从 `ext_MES_APS_Routing_View` 拉取 + `sp_SyncRoutingData` 落 Routing 表。

故 2号位 的「搬运=2号位 / 视图=3号位 / 源数据=PM·MES DBA」**三分法成立**，我照办。#2 改派 PM 无异议、销账。

---

## 一、Item 1 —— ScopeJson v2 定稿

依 §41.5（复用 `ScheduleRun.ScopeJson NVARCHAR(MAX)`，不新增表）字段清单定稿：

| 字段 | 类型 | 取值/枚举 | 锚 |
|---|---|---|---|
| `SchemaVersion` | int | 固定 `2` | §41.5 |
| `BusinessTriggerType` | enum（10 值） | 见下表 10 场景码 | §41.2 |
| `PriorityMode` | enum | `NORMAL` / `EXPEDITE` / `FORMAL` | §41.2 |
| `OrderTargets[]` | record[] | `{ OrderNo, ManualTargetDueDate }` | §41.5 |
| `TaskTargets[]` | record[] | `{ TaskNo, TargetTime }` | §41.5 |
| `ChangedResourceIds[]` | string[] | 变更资源 Id | §41.5 |
| （范围/授权字段） | — | 原 11 字段仍适用者 | §41.5 |

**关键更正（对齐 §41.5）**：`BasePlanVersionId` 与 `ExpectedDomainKeysJson` 属于 ScheduleRun 独立字段，**不写回 ScopeJson**——故 ScopeJsonV2 **不内嵌** `CandidateRunCreateSpec`（其含 `BasePlanVersionId`）。RunType/Purpose/DomainKey 亦走 ScheduleRun 侧校验（§41.3「一个 Run 只允许一个业务发起场景」）。

**10 场景 → BusinessTriggerType 码**（§41.2 全表；3号位 写死，请 2号位 验收）：

| # | BusinessTriggerType（建议码） | RunType + Purpose | PriorityMode |
|---|---|---|---|
| 1 | `CTP_NORMAL` | INSERT_ORDER_WHATIF + CTP | NORMAL |
| 2 | `CTP_EXPEDITE` | INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS | EXPEDITE |
| 3 | `INSERT_RESCHEDULE_NORMAL` | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | NORMAL |
| 4 | `INSERT_RESCHEDULE_EXPEDITE` | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | EXPEDITE |
| 5 | `ORDER_PULL_IN_NORMAL` | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 6 | `ORDER_PULL_IN_EXPEDITE` | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | EXPEDITE |
| 7 | `GANTT_TASK_ADJUST` | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 8 | `RESOURCE_FAILURE_RESCHEDULE` | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 9 | `CALENDAR_CHANGE_RESCHEDULE` | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL |
| 10 | `DOMAIN_MANUAL_RESCHEDULE` | MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT | FORMAL |

> 注：#7/#8/#9 三者 RunType+Purpose+PriorityMode 完全相同（LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT + NORMAL），仅发起场景不同，故 `BusinessTriggerType` 必须是独立场景码（不能由 RunType/Purpose 派生）。

**DTO 骨架**（`record` 不可变，中文 XML 注释补齐）：

```csharp
namespace LPS.APS.Core.DTOs.Governance;

/// <summary>白天候选业务触发类型（§41.2 十场景码，3号位 治理枚举）</summary>
public enum BusinessTriggerType
{
    CtpNormal, CtpExpedite,
    InsertRescheduleNormal, InsertRescheduleExpedite,
    OrderPullInNormal, OrderPullInExpedite,
    GanttTaskAdjust, ResourceFailureReschedule, CalendarChangeReschedule,
    DomainManualReschedule
}

/// <summary>优先级模式（§41.2）</summary>
public enum PriorityMode { Normal, Expedite, Formal }

/// <summary>订单目标（多订单及各自 ManualTargetDueDate，§41.5）</summary>
public sealed record OrderTargetV2(string OrderNo, DateTime? ManualTargetDueDate);

/// <summary>工序任务目标（多 Task 及各自 TargetTime，§41.5）</summary>
public sealed record TaskTargetV2(string TaskNo, DateTime? TargetTime);

/// <summary>白天候选 ScopeJson v2（§41.5；复用 ScheduleRun.ScopeJson，不落新表）</summary>
public sealed record ScopeJsonV2(
    int SchemaVersion,
    BusinessTriggerType BusinessTriggerType,
    PriorityMode PriorityMode,
    IReadOnlyList<OrderTargetV2> OrderTargets,
    IReadOnlyList<TaskTargetV2> TaskTargets,
    IReadOnlyList<string> ChangedResourceIds);
```

待 2号位 验收 `BusinessTriggerType` 十码后，3号位 即落 `ScopeJsonV2` 实体 + `RunLifecycleService` 解析/校验（M1 消费侧即可开工）。

---

## 二、Item 3/4 —— 判定接受 + 方法 + 取证需求

**接受**：`MES_APS_Routing_View`（契约视图）归 3号位，「视图丢 vs 源缺」判定是 3号位 职责。

**方法（三分法逐层取证）**：
1. 取 `MES_APS_Routing_View` 定义 + 输出行数（按 6,175 成品 MaterialCode 抽样）；
2. 比对源 28 张离散工艺表 / `ext_MES_APS_Routing_View` 侧行数；
3. 判定：**视图过滤/连接丢行 → 3号位 修视图**；**源本就缺 routing → 开清单给 PM/职能补**。

**取证需求（两样，返回前可开工）**：
- `MES_APS_Routing_View` 当前 DDL（我可自取 MES_Integration 库，或 2号位 顺手贴一下）；
- 源侧按成品抽取的 routing 行数（或 2号位 已有的「仅 7 有 routing」取证明细）。

属「源缺」的，我出清单给 PM、按台账 K「阻塞 B」记账，不阻塞主链。

---

## 三、Item 5/6 —— 维持销账

- Item 5（Setup §19.1 → 集成回归）：等 5/2号位 闭环后 3号位 跑全量 `dotnet test LPS.APS.sln`。
- Item 6（10 场景口径）：已确认，V3.22 §41.2 为准。

---

## 四、汇总（3号位 下一步）

| 项 | 3号位 动作 | 前置 | 状态 |
|---|---|---|---|
| 1 | ScopeJson v2 定稿（本回执已附）；待验码后落实体+解析 | 2号位 验收十码 | ⏳ 送验 |
| 2 | 销账（改派 PM） | — | ✅ |
| 3/4 | 出「视图丢 vs 源缺」结论（源缺开清单给 PM） | 视图 DDL + 源行数 | ⏳ 取证中 |
| 5/6 | 销账 | — | ✅ |