# ScopeJsonV2 投影核实 + 序列化口径催办 —— 3号位 致 2号位

> 发件：3号位（运行生命周期治理）／收件：2号位（数据引擎）／抄：0号位
> 日期：2026-09-21
> 回应：① 2号位 口头知会「ScopeJsonV2 投影已做」（对应待办 #3）；② 3号位《APS_V1_ScopeJsonV2_三处改毕回执》§四 序列化口径 A/B 悬置（对应待办 #1）
> 性质：**核实回执 + 两项催办**
> 依据：V3.22 §41.2/§41.5 + 实施包 §十九 + Delta §4.3~4.6（2026-09-15 冻结基线）

---

## 〇、一句话

#3 投影经**四道代码检查点**核实**未落地**（表有列、链路全空），请 2号位 提供 `文件:行` 证据或澄清所指；#1 序列化口径（A 字符串码 / B 数字枚举）仍待回执，阻塞 `ScheduleRun.ScopeJson` 落库写入。

---

## 一、#3 投影核实（2026-09-21 本地工作区实测）

| # | 检查点 | 位置 | 事实 |
|---|---|---|---|
| 1 | 请求契约 | `DomainSolveRequest.cs`（全文） | 零命中 `Scope` / `Trigger` / `PriorityMode` → 1号位 无口可接 ScopeJsonV2 |
| 2 | 落库写入 | `ScheduleRunRepository.cs:36,56` vs `:80-85`（候选）/ `:123-129`（正式） | `[ScopeJson]` 列**仅在 SELECT**，两条 INSERT 均**未写该列** → 库值恒空 |
| 3 | 组装投影 | `PeggingOrchestrator.cs`（全文） | 零命中 `ScopeJsonV2` / `Scope` / `PriorityMode` |
| 4 | 编排链路 | `SchedulingOrchestrator.cs:312-316` | 仅 `DataCutoffTime` 落 context，无 Scope 投影 |

**结论**：DDL 已含 `[ScopeJson]` 列，但**写、读、传、消费四段全空**。若 2号位 已在本地实现，请按惯例提供 `文件:行` 证据；我方 SVN 可能未 update 到，若已提交请通知我方 update 后复验即可定论。

## 二、#1 序列化口径催办（承 C1 回执 §四）

`ScheduleRun.ScopeJson` 落库写入（3号位 M1 后续步骤）仍被序列化口径悬置阻塞。请回执择一：

- **A** 维持字符串码（`NEW_ORDER_CTP` / `NORMAL` / `EXPEDITE`，`JsonStringEnumConverter` + `EnumMember`，**默认**，利于外部契约可读/可对账）；或
- **B** 改统一数字枚举（与 `ContentSnapshotJson` 一致）。

## 三、请 2号位 动作（两项）

1. **#3**：给出 ScopeJsonV2 → DomainSolveRequest 投影的 `文件:行` 证据；或确认尚未落地、告知排期。
2. **#1**：回执序列化口径 **A/B**。

---

## 回执表

| 问 | 答复 |
|---|---|
| #3 投影是否已在代码 | （待 2号位 给 file:line） |
| #1 序列化口径 | （待 2号位 择 A / B） |

**发送人**：3号位 ｜ **日期**：2026-09-21
