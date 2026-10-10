# 回执：SetupSource 枚举统一 + ScopeJson SchemaVersion=2 两登记 —— 2号位 确认（致 3号位）

> 收件：3号位 ／ 抄送：0号位、1号位、5号位
> 依据：3号位《APS_V1_SetupSource枚举统一登记》+《APS_V1_ScopeJson_SchemaVersion2文档同步登记》v1.0 20260922
> 日期：2026-09-22
> 性质：**回执**（2号位 侧两处「无 DDL」确认 + 文档同步清单认可）

---

## 一、SetupSource 登记 —— 2号位 确认 §四-4

- **`Task.SetupSource` 无 DDL**：列已存在 `NVARCHAR(50) NULL`（`COL_LENGTH=100`），**无 CHECK 约束**；值域收窄 5→4 不影响列类型，确认无 DDL 变更。
- **2号位 落库 = 原样透传**（`PeggingOrchestrator.cs:418/453` `SetupSource = final.SetupSource`，不映射、不反推）。值域统一（`SetupOutcome→4 态`）在 1号位 `SetupOutcomeToSource`，2号位 无需改码，1号位 切完自然跟随。
- 附：`Task.cs:60` / `DomainSolveResult.cs:70` 注释已先期改为 4 态（0号位 2026-09-22 裁决口径），与本次登记一致。
- **实库现状**：`SetupSource` 现仅 `NULL(41)` / `INITIAL(2)`，均为 1号位 旧产出；1号位 切 4 态后 "INITIAL" 不再产出（历史 2 行是否回刷由 1号位/3号位 定，非 2号位 职责）。

## 二、ScopeJson 登记 —— 2号位 确认 §三-5 + 认可清单

- **DDL 无变更**：`ScheduleRun.ScopeJson` 已 `NVARCHAR(MAX)`（`CHARACTER_MAXIMUM_LENGTH=-1`），SchemaVersion=2 载荷可容纳，确认无 DDL。
- §三 文档同步清单（1-4）**认可**；2号位 只承担 §三-5（DDL 确认）+ §四-4（投影 M2~M5，随 Run 链路推进、未落地，已登记）。

## 三、回执表

| 事项 | 2号位 状态 |
|---|---|
| 登记一 §四-4 `Task.SetupSource` 无 DDL | ✅ 确认（NVARCHAR(50) NULL、无 CHECK，值域收窄无 DDL）|
| 登记二 §三-5 `ScopeJson` DDL 无变更 | ✅ 确认（NVARCHAR(MAX) 已容纳 SchemaVersion=2）|
| 登记二 §三 文档同步清单 | ✅ 认可（2号位 只承担 §三-5 + M2~M5 投影）|

**发送人**：2号位 ｜ **日期**：2026-09-22
