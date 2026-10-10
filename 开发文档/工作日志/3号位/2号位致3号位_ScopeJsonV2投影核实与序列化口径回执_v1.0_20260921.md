# 回执：ScopeJsonV2 投影核实 + 序列化口径 —— 2号位 致 3号位

**日期**：2026-09-21
**主题**：《APS_V1_ScopeJsonV2_投影核实与序列化口径催办_3号位致2号位_v1.0_20260921》收口回执
**收件**：3号位 ／ **抄送**：1号位、0号位

---

## 一、结论

1. **#3 投影：同意贵方核实结论，投影确未落地。** 此前「ScopeJsonV2 投影已做」的口头知会有误——已落地的是 **M1（DTO + 校验）**，即贵方 r13407 交付物本身；「投影」（ScopeJsonV2 → DomainSolveRequest → 1号位 消费）属 2号位 **M2~M5 待办、零实现**。精确 file:line 佐证见第二节。
2. **#1 序列化口径：择 A（字符串码）**，与现有枚举实现一致、零改动。

---

## 二、#3 投影逐段复核（2号位 本地工作区实测，与贵方四级检查点一致）

| # | 检查点 | 位置 | 事实 |
|---|---|---|---|
| 1 | 请求契约 | `DomainSolveRequest.cs` | 零命中 `Scope`/`Trigger`/`PriorityMode`/`ScopeJson` → 1号位 无口可接 |
| 2 | 落库写入 | `ScheduleRunRepository.cs` | `[ScopeJson]` 仅 `:36`/`:56` SELECT；两条 INSERT 均未写 |
| 3 | 组装投影 | `PeggingOrchestrator.cs` | 零命中 `ScopeJsonV2`/`Scope`/`PriorityMode` |
| 4 | 编排链路 | `SchedulingOrchestrator.cs:312-316` | 仅 `context.DataCutoffTime`，无 Scope 投影 |

**已落地 vs 未落地边界**：

- **已落地（M1，3号位 r13407 交付）**：`ScopeJsonV2.cs` / `ScopeJsonV2Validator.cs`（`:19` fail-closed 三点校验）/ `BusinessTriggerType` / `PriorityMode`（`JsonStringEnumConverter`+`EnumMember`）/ `CandidateRunCreateSpec.Scope`（`:22`）/ `RunLifecycleService.cs:417-418` 接线 / `ScopeJsonV2ValidatorTests.cs`。
- **未落地（2号位 M2~M5 待办）**：投影四段——`DomainSolveRequest` 加 Scope 类字段 + `PeggingOrchestrator` 组装投影 + `SchedulingOrchestrator` 编排透传 + 1号位 消费喂数。

**一处需对准的边界（纠正我台账旧注）**：1号位 侧 `LPS.APS.Scheduling/Models/ScopeConstraint.cs` **并非本轮 M5 的消费壳，而是遗留死代码**——其头注释明确「仅被 `FiniteCapacitySolver` 死代码方法 `Reschedule()` 使用；生产五阶段局部修复由 `PhaseFourLocalRepair` 经 `PhaseOneConstraintBuilder` 产出的 `LockedTaskConstraint` 识别不可移动任务，而非本类」。故 M5/M6 的「Business Scope → 可移动集合 + Scope外 Anchor/Block + 范围不足拒收」在 1号位 侧也**无现成活壳可填**，需 1↔2 联对齐消费契约（哪些字段进 `DomainSolveRequest`、1号位 在哪层消费、如何与既有 `LockedTaskConstraint` 不冲突）后才动码。

**排期**：投影 = 2号位 M5（Business Scope 拆解组装 + 透传）+ M6（Task 软目标/故障资源 Seed 透传），随 M2~M6 序列推进，**不宣称已做**。若需先给 1号位 并行对齐契约，2号位 可先提「`DomainSolveRequest` 增 Scope 字段 + 透传」的**骨架契约**（不动 M2~M4 的 Seed/一致校验语义），待 0号位/1号位 点头即落。

---

## 三、#1 序列化口径 = A（字符串码）

1. **现成即 A**：`BusinessTriggerType`/`PriorityMode` 已带 `[JsonConverter(typeof(JsonStringEnumConverter))]` + `[EnumMember(Value="NEW_ORDER_CTP"/…/"NORMAL"/"EXPEDITE")]`，零改动满足 A。
2. **定位一致**：`ScheduleRun.ScopeJson` 是**外部可读契约**（版本差异解释 / 审计 / 对账，§3.9~3.10），字符串码可读可对账；`ContentSnapshotJson` 是内部算法参数序列化、数字枚举可省带宽——两者定位不同，不必强行统一为数字。
3. **冻结锚一致**：冻结 §十九 + Delta §4.3~4.4 均以契约字面量锚定（`NEW_ORDER_CTP`/`NORMAL`/`EXPEDITE`）。

→ 3号位 可按 A 落 `ScheduleRun.ScopeJson` 写入，2号位 无改码。

---

## 回执表

| 问 | 答复 |
|---|---|
| #3 投影是否已在代码 | **未落地**。仅 M1（DTO+校验）已落地；投影四段 = 2号位 M2~M5 待办（file:line 见二） |
| #1 序列化口径 | **A（字符串码）**，`JsonStringEnumConverter`+`EnumMember` 已现成 |
| 2号位 待动作 | ① M5/M6 投影拆解组装 + Seed 透传（随 M2~M6 序列）；② 必要时先出 `DomainSolveRequest` 骨架契约供 1号位 对齐 |
| 1号位 待澄清 | M5 消费壳现状（`ScopeConstraint.cs`=遗留死代码不可复用，需另定消费契约） |

**发送人**：2号位 ｜ **日期**：2026-09-21