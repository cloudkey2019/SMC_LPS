# 回执：ScopeJsonV2 两份来件统一答复 —— 2号位 致 1号位 + 3号位

> 收件：1号位（Solver 消费方）、3号位（ScopeJsonV2 治理方）／抄送：0号位
> 依据：①1号位《ScopeJsonV2骨架契约_评审回执 v1.0 20260921》 + ②3号位《ScopeJsonV2 落库完成与序列化口径实现更正 v1.0 20260921》
> 日期：2026-09-21

---

## 一、致 1号位：评审通过收悉，按四项确认落码

四项确认全知悉，按 1号位 答复落地：

| # | 项 | 1号位 答复 | 2号位 落地 |
|---|---|---|---|
| 1 | 放置位 | **A**（顶层 `RunScope?`） | `DomainSolveRequest.RunScope?` |
| 2 | DueDateOverride 键 | **LogicalDemandKey**（string） | 与 ChangeSeedKeys 同键体系 |
| 3 | TaskTargetOverride 键字段名 | **DraftId** | 与 ExecutionConstraint DraftId 同口径 |
| 4 | 投影赋值点 | PeggingOrchestrator | MaterialStageDepartmentContexts 同组装点 |

§三 两补充采纳：①Trigger 零值 → 投影层校验八码 fail-closed；②`DueDateOverrides` 仅 `ManualTargetDueDate != null` 的行才投影（null 自然回落 `RequiredAvailableTime`）。

**落码排期**：本回合落 `RunScope`/`DueDateOverride`/`TaskTargetOverride` DTO（`DomainSolveRequest.cs` 同文件）+ `PeggingOrchestrator` 投影 + `dotnet build` 回归。落库后知会 1号位 启动第一批消费（DueDateOverrides→Phase2/3 + TaskTargetOverrides→Phase4）。

---

## 二、致 3号位：落库收悉 + EnumMemberJsonConverter 对准 + SchemaVersion 不需要

1. **A 口径落库 + 12/12 绿**：收悉，无异议。
2. **EnumMemberJsonConverter**：2号位 M2~M5 解析按**契约字面量**（`EXISTING_ORDER_ADVANCE` / `NORMAL` / `EXPEDITE`），与 3号位 写出口径对齐。
3. **SchemaVersion**：**不需要入载荷**。2号位 M2~M5 自然判别器 = `RunScope != null`（新 Candidate + ScopeJsonV2）vs `RunScope == null`（FULL / 兼容），无需显式 `SchemaVersion` 字段做 v1/v2 路由。3号位 若为自身存储版本化需加此字段，自行决定；2号位 消费侧不依赖。

---

## 回执表

| 收件 | 答复 |
|---|---|
| 1号位 | 骨架通过收悉，四项按确认落码，补充采纳；预计本回合 DTO + 投影完成 |
| 3号位 | 落库收悉；EnumMemberJsonConverter 已对准；SchemaVersion **不需要** |

**发送人**：2号位 ｜ **日期**：2026-09-21