# ScopeJsonV2 载荷契约与消费对接 —— 3号位 致 1号位

> 发件：3号位（规则/参数/运行生命周期治理）／收件：1号位（排程算法，ScopeJsonV2 消费方）／抄：0号位、2号位
> 日期：2026-09-21
> 背景：① 2号位《ScopeJsonV2 统一回执》——投影已落 `DomainSolveRequest.RunScope?`（四契约点），本回合 DTO+投影+build 完成；② 3号位 已按 A 口径落 `ScheduleRun.ScopeJson` 写入（12/12 绿）；③ 1号位《ScopeJsonV2骨架契约_评审回执》四项确认
> 性质：**载荷契约正式移交**（写侧 A 口径为 3号位 治理域，供 1号位 消费对齐）
> 依据：V3.22 §41.2/§41.5 + 实施包 §十九 + Delta §4.3~4.6（2026-09-15 冻结基线）

---

## 〇、一句话

ScopeJsonV2 载荷已按 **A 口径（契约字符串码）** 落库 `ScheduleRun.ScopeJson`；2号位 已投影为 `RunScope`（构建 0 错误，四契约点核验通过）。请 1号位 按本文件载荷契约启动消费：`DueDateOverrides` → Phase2/3、`TaskTargetOverrides` → Phase4。

## 一、写链路（3号位 已闭环）

```
CreateCandidateRunRequest.Scope（4号位/API 传入）
  → CandidateRunCreateSpec.Scope（M1）
  → RunLifecycleService.CreateCandidateRunAsync（校验：八码/轴/去重）
  → ScheduleRunRepository 候选 INSERT [ScopeJson]（A 口径序列化，12/12 单测锁）
```

- 存储位：`ScheduleRun.ScopeJson`（NVARCHAR(MAX) NULL，DDL v5.1.7 带 `ISJSON` CHECK）。
- 序列化口径：**A = 契约字符串码**。写= `EnumMemberJsonConverter`（读 `EnumMember` 字面量）；读= 容忍字面量/成员名/数字（忽略大小写，fail-closed）。

## 二、载荷契约（写侧权威，示例为合成值）

```json
{
  "Trigger": "EXISTING_ORDER_ADVANCE",
  "PriorityMode": "EXPEDITE",
  "OrderTargets": [ { "OrderCanonicalId": 1001, "ManualTargetDueDate": "2026-10-01T00:00:00" } ],
  "TaskTargets": [ { "TaskId": 7, "TargetTime": "2026-09-22T09:00:00" } ],
  "ChangedResourceIds": [12, 18, 33]
}
```

| 字段 | 契约要点 |
|---|---|
| `Trigger` | 八码：`NEW_ORDER_CTP` / `NEW_ORDER_IMPACT` / `NEW_ORDER_INSERT` / `EXISTING_ORDER_ADVANCE` / `GANTT_ADJUSTMENT` / `EQUIPMENT_FAILURE` / `RESOURCE_CALENDAR_CHANGE` / `DOMAIN_MANUAL_RESCHEDULE`（非数字） |
| `PriorityMode` | 独立二值轴 `NORMAL` / `EXPEDITE`；**`DOMAIN_MANUAL_RESCHEDULE` 具现为 null**（走既有正式优先规则） |
| `OrderTargets` | `OrderCanonicalId`(long) + `ManualTargetDueDate`(DateTime?，仅本次 Run、不改正式 DueDate)；`OrderCanonicalId` 去重唯一（单一真相） |
| `TaskTargets` | `TaskId`(long) + `TargetTime`(DateTime，**软目标**，Solver 可返回其它可行时间并给 Explanation) |
| `ChangedResourceIds` | int 数组 |

Trigger → RunType×Purpose 固定映射见 `ScopeJsonV2Validator`（八码 fail-closed，创建时已校验）。

## 三、投影消费对齐（2号位 已落 `RunScope`）

| 投影字段 | 源 | 1号位 应对 |
|---|---|---|
| `RunScope.InScopeLogicalDemandKeys` | （M5 详设前留空） | M5 前忽略；`EXPEDITE` 强度待 0号位 裁决（`DomainSolveRequest.cs:74` 注） |
| `DueDateOverrides` | `OrderTargets` 中 **`ManualTargetDueDate != null`** 行 → `LogicalDemandKey` | **Phase2/3** 交期覆盖；null 行自然回落 `RequiredAvailableTime` |
| `TaskTargetOverrides` | `TaskTargets` → 复合键 `(DraftId, OperationCode)` | **Phase4** 软目标（可违约并说明） |
| `ChangedResourceIds` | `ScopeJsonV2.ChangedResourceIds`（1号位 已核：`PeggingOrchestrator.cs:4575` `scope.ChangedResourceIds ?? []`） | **批次二** Phase1 资源种子（消费口径待三方详设） |
| `RunScope` 本身 | null = FULL 零改动 / 非 null = 新 Candidate+V2 | 判别器：`RunScope != null` 即 V2 载荷 |

Trigger 零值已由投影层八码 fail-closed（`PeggingOrchestrator.BuildRunScopeAsync`），1号位 无需重复校验。

## 四、请 1号位 动作

1. 按 §二/§三 启动第一批消费：`DueDateOverrides` → Phase2/3、`TaskTargetOverrides` → Phase4。
2. 回执确认：载荷契约口径无疑义 / 消费启动计划（含排期）。

---

## 回执表

| 问 | 答复 |
|---|---|
| 载荷契约（A 口径）是否有疑义 | （待 1号位） |
| 消费启动（DueDateOverrides→Phase2/3、TaskTargetOverrides→Phase4）计划 | （待 1号位） |

**发送人**：3号位 ｜ **日期**：2026-09-21
