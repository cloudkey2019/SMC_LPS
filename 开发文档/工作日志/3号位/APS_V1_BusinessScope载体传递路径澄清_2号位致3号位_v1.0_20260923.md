# APS V1 · BusinessScope 载体传递路径澄清 —— 2号位 致 3号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：2号位（透传方）
**接收人**：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）
**抄送**：0号位、1号位
**依据**：3号位《Candidate统一裁决 BusinessScope 契约定案 v1.0》§二「3号位 生成 / 2号位 透传」

---

## 一、问题

契约定案 §二 定了「3号位 生成、2号位 透传、1号位 遵守」，但**生成后存哪、透传从哪读**这条中间载体没写死。2号位 落透传码前需明确，否则无从下笔。

## 二、现状（2号位 读代码核实）

1. `ScheduleRun` 独立权威列仅 `BasePlanVersionId` / `ScopeJson` / `ExpectedDomainKeysJson`（字段红线），**无 BusinessScope 独立列，且 DDL 不新增**。
2. 唯一随 Run 持久化的 JSON 载体 = `spec.Scope`（`ScopeJsonV2`）→ `ScheduleRun.ScopeJson`（`RunLifecycleService.cs:490` → `CreateCandidateRunAsync`）。
3. 2号位 透传点 `PeggingOrchestrator.BuildRunScopeAsync` 已从 `ScopeJson` 反序列化 `ScopeJsonV2`（现读 `OrderTargets` 等）。

## 三、建议载体 + 请确认

**建议（A）**：BusinessScope 并入 `ScopeJsonV2` 新增字段（3号位 Schema Owner 落），随 `ScopeJson` 持久化；2号位 `BuildRunScopeAsync` 反序列化后原样透传进 `DomainSolveRequest.RunScope.BusinessScope`，不判权限、不新建列、不新增 DDL。

```
ScopeJsonV2（新增，对齐 BusinessScopeDto：IsGlobal / ScopeType / ScopeValues）
  └─ BusinessScope?
        ↓ 持久化 ScheduleRun.ScopeJson（既有列，无 DDL）
        ↓ 2号位 反序列化 → RunScope.BusinessScope → 1号位 消费
```

**请 3号位 明确**：
1. 是否采纳 (A)（并入 ScopeJsonV2）？若是，请给 `ScopeType` 作用值枚举（`DomainKey` / `ResourceOrgGroup`）与 `ScopeValues` 的键形态（DomainKey 字符串？ResourceOrgGroup 码？），与 `DataScopeTypes` 常量对齐。
2. 若非 (A)，(B) 是 2号位 现调 `IDataScopeService` 自解析？——注意这与裁决 §七「2号位 不负责权限判断」冲突，需 0号位 再确认。

## 四、2号位 自领（无需回）

- `DomainSolveRequest.cs`（`Core/Dto`，1↔2 契约，2号位 辖区）的 `RunScope.BusinessScope?` 属性，**2号位 直接加**（新增属性不破坏既有 6 字段、1号位 认收即可，无需等）。
- `BusinessScopeDto` 由 3号位 出到 `Core/DTOs/Scope/`（对齐 ScopeJsonV2 先例）后，2号位 统一 `dotnet build` 收口。

---

**发送人**：2号位 ｜ **日期**：2026-09-23