# APS V1 Candidate统一裁决 · Business Scope 载体契约定案 + 各方落点收口（3号位）

**版本**：**v1.1**（v1.0 + 采纳 2号位 载体路径澄清：并入 `ScopeJsonV2` + 键形态定案 + 纠正 `DomainKey`→`DataScopeTypes` 真实常量）

**文档性质**：3号位 落地收口函——将 0号位《APS_V1_Candidate业务范围订单操作权限及落地对齐统一裁决回复》落地为可落码契约与各方职责，重点 = **真空④ `RunScope.BusinessScope` 载体契约定案**（红线#5「接口即契约」先落文档）。

**发送方**：3号位（权限治理 / Business Scope 生成 Owner / ScopeJson Schema Owner）｜ **日期**：2026-09-23
**接收方**：1号位（Solver 遵守 + 范围不足返回）、2号位（透传 + 投影）｜ **抄送**：0号位（裁决备查）、4号位（前端备查）
**依据**：0号位《统一裁决回复》（§五~§七、§十一）｜`IDataScopeService`（`LPS.APS.Core/Interfaces/IDataScopeService.cs`）｜`RunScope`（`LPS.APS.Core/Dto/DomainSolveRequest.cs:279`）｜`ScopeJsonV2`（`LPS.APS.Core/DTOs/Governance/ScopeJsonV2.cs`，含 SchemaVersion 版本化扩展）｜`DataScopeTypes`（`LPS.APS.Core/Authorization/DataScopeTypes.cs`）｜2号位《BusinessScope载体传递路径澄清 v1.0》

---

## 〇、裁决收口表

| 裁决项 | 0号位 口径 | 3号位 落地判定 |
|---|---|---|
| 真空① 新订单承载 | 不增 `NEW_ORDER` 字段，统 `OrderTargets[].OrderCanonicalId` | ✅ 与 P1-2 定案一致，无改 |
| 真空② `OrderCanonicalIds` | 删除独立概念，统一 `OrderTargets`，冻结改 | ✅ 无动作（登记更正已随 P1-2）|
| **真空④ Business Scope** | §五/§七：白天 Candidate 操作允许影响的业务对象范围；3号位 生成 / 2号位 透传 / 1号位 遵守 + 范围外返回「业务范围不足」 | ✅ **本函落 `RunScope.BusinessScope` 载体契约定案 + 传递路径（采纳 2号位 A）** |
| 真空③ ChangeSeed | 2号位 变化需求集合；1号位 受影响 Task/资源/可移动 + 范围不足返回 | ✅ 职责已裁 |
| 真空⑤ AllocationSequence | V1 不依赖 | ✅ 闭环 |
| 真空⑥ 白天 BOM | 按需获取（5号位 事实/接口、2号位 加载）| ✅ 闭环（归 5号位）|
| EXPEDITE 授权 | §六/§十一：订单类操作 = **产品族 PMC** 发起、产品族范围 | ✅ 记录（见 §四）|

---

## 一、`RunScope.BusinessScope` 载体契约定案（真空④）

> **红线#5**：RunScope 为冻结契约（1号位 消费）。本契约为**新增属性**，不改既有 6 字段签名；经 0号位 裁决 + 各相关号位确认后落码。

### 1.1 DTO（新增，真源对齐 `DataScopeTypes`）

```csharp
// LPS.APS.Core/DTOs/Scope/BusinessScopeDto.cs（拟）
/// <summary>白天 Candidate 操作允许影响的业务对象范围（0号位 §五，作用「谁可发起什么变化」）。</summary>
public sealed class BusinessScopeDto
{
    /// <summary>全局放行（DataScopeTypes.Global），即无维度限制。</summary>
    public bool IsGlobal { get; init; }

    /// <summary>业务范围维度（DataScopeTypes 常量）：订单类=ProductFamily（产品族）、Task 局部=ResourceOrgGroup（生产组织）；Global 放行时忽略。</summary>
    public string ScopeType { get; init; } = string.Empty;

    /// <summary>允许影响的业务对象键集合：业务键字符串（产品族码 / ResourceOrgGroup 码，与 EnsureInScopeAsync 的 value 同口径）。</summary>
    public IReadOnlyList<string> ScopeValues { get; init; } = Array.Empty<string>();
}
```

**`ScopeType` 取值（定案，2号位 落码据此）**：`DataScopeTypes` 常量——订单类=`ProductFamily`；Task 局部=`ResourceOrgGroup`（备选 `Department`，依生产组织主键口径）；系统/中断级=`Global`。**不使用 `DomainKey`（该值是 EnsureInScopeAsync 的目标值形态，非 ScopeType 维度）。**

### 1.2 RunScope 新增属性

```csharp
// 既有 RunScope（DomainSolveRequest.cs:279）之上，仅新增：
/// <summary>本次 Candidate 操作允许影响的业务对象范围；null=未投影（按无授权 fail-safe）。</summary>
public BusinessScopeDto? BusinessScope { get; init; }
```

### 1.3 维度 ↔ 操作类型映射（裁决 §六）

| BusinessTriggerType（RunScope.Trigger）| 操作类型 | 权限角色 | BizScope 维度（DataScopeTypes）|
|---|---|---|---|
| NEW_ORDER_CTP / NEW_ORDER_IMPACT / NEW_ORDER_INSERT / EXISTING_ORDER_ADVANCE | 订单类 | 产品族 PMC | `ProductFamily`（产品族）|
| GANTT_ADJUSTMENT / DOMAIN_MANUAL_RESCHEDULE | Task 局部调整 | 一般计划员 | `ResourceOrgGroup`（生产组织）|
| 其余（EQUIPMENT_FAILURE / RESOURCE_CALENDAR 中断级）| 系统级 | — | `Global`（不涉用户业务范围）|

### 1.4 1号位 消费契约（范围不足返回）

```csharp
// 语义（裁决 §七 + 真空③）：求解时遵守 BusinessScope；某可调整需求/资源命中范围外
// 且须移动才能成可行方案 → 停止并返回「当前业务范围不足」拒收信号（BUSINESS_SCOPE_INSUFFICIENT）。
public const string BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT";
```

- **判定**：对象对应产品族（ProductFamily）/ 生产组织（ResourceOrgGroup）的**业务键 ∈ `BusinessScope.ScopeValues`**，或 `IsGlobal=true` → 可移动；否则不移动，若必要移动则返回范围不足。
- 与 `InScopeLogicalDemandKeys`（既有，留空）关系：`BusinessScope`=权限框（谁可动），InScope=计划细粒度（M5 详设范围）；两者并发，BusinessScope 优先为硬边界。

---

## 二、载体传递路径（定案：采纳 2号位 建议 A —— 并入 `ScopeJsonV2`）

> 解决契约「3号位 生成 / 2号位 透传」的**中间载体存哪、从哪读**。采纳 2号位 (A)：**并入 `ScopeJsonV2`，随 `ScheduleRun.ScopeJson` 持久化，无 DDL**。

```
ScopeJsonV2（新增 BusinessScope?，对齐 BusinessScopeDto：IsGlobal / ScopeType / ScopeValues）
  └─ 持久化 ScheduleRun.ScopeJson（既有列，无 DDL；利用 ScopeJsonV2.SchemaVersion 版本化扩展）
  └─ 2号位 BuildRunScopeAsync 反序列化 ScopeJsonV2 → RunScope.BusinessScope → 1号位 消费
```

- **载体归属**：BusinessScope 并入 `ScopeJsonV2`（3号位 Schema Owner 落），随 `ScopeJson` 持久化（既有列、零 DDL）。`ScopeJsonV2.SchemaVersion` 允许版本化扩展（0号位 2026-09-22 裁决），新增字段不破坏既有 v2 语义；历史缺字段载荷缺省 `IsGlobal=false` + 空集兜底。
- **透传**：2号位 `BuildRunScopeAsync` 已从 `ScopeJson` 反序列化 `ScopeJsonV2`，原样映射进 `DomainSolveRequest.RunScope.BusinessScope`，**不判权限、不新建列、不新增 DDL**（裁决 §七「2号位 不负责权限判断」）。
- **`ScopeValues` 键形态**：业务键字符串集合（产品族码 / ResourceOrgGroup 码），与 `IDataScopeService.EnsureInScopeAsync` 的 `value`（DomainKey/FactoryCode 等业务键）**同口径**；3号位 生成侧由 `ResolveScopeAsync` 的权限值直接映射。

---

## 三、各方落点

| 号位 | 落点 |
|---|---|
| **3号位**（生成）| `RunLifecycleService` 构建 `CandidateRunCreateSpec`/`ScopeJsonV2` 时，经 `IDataScopeService.ResolveScopeAsync(userId)` 解析 → 依 Trigger 装配 `BusinessScopeDto` 并入 `ScopeJsonV2`（`ScheduleRun.ScopeJson` 持久化）。|
| **2号位**（透传）| `BuildRunScopeAsync` 反序列化 `ScopeJsonV2` → `RunScope.BusinessScope` 原样透传，不判权限。`DomainSolveRequest.RunScope.BusinessScope?` 属性 2号位 直接加（新增属性，自领）。|
| **1号位**（遵守）| 求解遵守 BusinessScope；范围外必要移动 → 返回 `BUSINESS_SCOPE_INSUFFICIENT`。|
| **红线** | 不新增 DDL（随 `ScopeJsonV2` 并入，复用 `ScheduleRun.ScopeJson`）；不改既有 RunScope 6 字段 / ScopeJsonV2 既有块签名。|

---

## 四、代码改动归属（后续，依红线#5 契约确认后）

| 文件 | 号位 | 动作 |
|---|---|---|
| `LPS.APS.Core/DTOs/Scope/BusinessScopeDto.cs` | 3号位（Core DTO）| 新增（对齐 ScopeJsonV2 先例）|
| `LPS.APS.Core/DTOs/Governance/ScopeJsonV2.cs` | 3号位 | +`BusinessScope?`（SchemaVersion 版本化扩展）|
| `LPS.APS.Core/Dto/DomainSolveRequest.cs`（RunScope）| 2号位 自领 | +`BusinessScope?` 属性（1号位 认收）|
| `RunLifecycleService.cs` | 3号位 | 装配注入（构建 ScopeJsonV2 时）|
| `BuildRunScopeAsync` | 2号位 | 透传映射（反序列化 ScopeJsonV2 → RunScope）|
| 1号位 判定 | 1号位 | 遵守 + `BUSINESS_SCOPE_INSUFFICIENT` |

## 五、EXPEDITE 授权边界记录（§六/§十一）

- **EXPEDITE / CTP / 插单影响分析 / 已有订单提前** = 订单类操作 → **产品族 PMC 发起**、范围=产品族范围（`ProductFamily`）。
- 与 P1-2 `expedite-impact`（只读查询，非发起）无冲突；发起侧鉴权经 `IDataScopeService.EnsureInScopeAsync(ProductFamily, ...)`。
- Business Scope 范围外任务 = 「不含 EXPEDITE 重排移动」→ 与 EXPEDITE ① 保护边界**正交叠加**（见 §六）。

## 六、与 EXPEDITE ① 业务保护识别的关系（⚠ 仍待 0号位）

- 本裁决的 **Business Scope ≠ Firm/Frozen 业务保护**：Business Scope=用户权限范围（产品族/生产组织），Firm/Frozen=订单业务属性（已确认不可调整）。产品族 PMC 对该族内单可 EXPEDITE，族内仍可能有 Firm/Frozen 单应对否保护。
- → **业务保护识别字段 (i) IsInFrozenZone / (ii) ScopeJson 保护集合 继续待 0号位 闭合**；本裁决 §七「范围外不移动」仅作**正交补充**（无权限单天然受保护），不替代识别字段。P1-2 过渡期仍以 2号位 FirmHorizonDays 兜底。

## 七、待办

| # | 待办 | 归属 |
|---|---|---|
| 1 | 落码：`BusinessScopeDto.cs` + `ScopeJsonV2.BusinessScope` | 3号位 |
| 2 | 落码：`RunScope.BusinessScope` 属性（2号位 自领）+ 1号位 认收 `BUSINESS_SCOPE_INSUFFICIENT` | 2号位 / 1号位 |
| 3 | 落码：`RunLifecycleService` 装配注入 + `BuildRunScopeAsync` 透传 | 3号位 / 2号位 |
| 4 | 业务保护识别字段 (i)/(ii) | **0号位**（正交评审后）|
| 5 | P1-2 `expedite-impact` 落地（宿主 + Base 比对派生）| 2号位 / 3号位 |

---

**发送方**：3号位 ｜ **日期**：2026-09-23 ｜ **版本**：v1.1
**对应**：0号位《统一裁决回复》｜真空④｜2号位《载体传递路径澄清 v1.0》｜P1-2 v1.0 契约 §五
**v1.1 变更**：① 采纳 2号位 (A)——BusinessScope 并入 `ScopeJsonV2` 随 `ScopeJson` 持久化（无 DDL）；② 键形态定案：`ScopeValues`=业务键字符串（对齐 EnsureInScopeAsync value）；③ 纠正 `ScopeType` 取值 `DomainKey`→`DataScopeTypes` 真实常量（订单类=`ProductFamily`，Task 局部=`ResourceOrgGroup`）。
**待办**：① 各号位按 §四 落码 ② 业务保护识别字段待 0号位 ③ P1-2 落地