# APS V1 · Business Scope 全链路就绪，致 1号位 认收消费端 —— 3号位 致 1号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）
**接收人**：1号位（遵守方）
**抄送**：2号位（透传方）、0号位（裁决备查）、4号位（前端备查）
**依据**：3号位《Candidate统一裁决_BusinessScope契约定案及收口 v1.1》§1.4 / §四 归属表 #2｜0号位《统一裁决回复》§七｜2号位《BusinessScope载体传递路径澄清 v1.0》

---

## 一、知会要点

Business Scope **生成侧（3号位）+ 透传侧（2号位）已全部落码闭环**，编译 0 错误。现在全链路唯一缺口指向 **1号位 遵守侧**：认收 `RunScope.BusinessScope` 并落码返回 `BUSINESS_SCOPE_INSUFFICIENT` 的消费契约（契约定案 §四 归属表 **#2 后半**）。请 1号位 据此落码，无需等其余号位。

## 二、链路现状（供 1号位 定位）

| 环节 | 落点 | 归属 | 状态 |
|---|---|---|---|
| 生成 | `RunLifecycleService.CreateCandidateRunAsync`（3a' 装配段 + `BuildBusinessScopeAsync`） | 3号位 | ✅ |
| 载体 | `ScopeJsonV2.BusinessScope`（随 `ScheduleRun.ScopeJson` 持久化） | 3号位 | ✅ |
| 属性 | `DomainSolveRequest.RunScope.BusinessScope { get; init; }`（`LPS.APS.Core/Dto/DomainSolveRequest.cs:301`，新增属性，6 字段签名零改） | 2号位 | ✅ |
| 透传 | `PeggingOrchestrator.BuildRunScopeAsync`（:4671 `BusinessScope = scope.BusinessScope`，原样透传 null→null，无判权限） | 2号位 | ✅ |
| **遵守** | **待 1号位**：认收 + `BUSINESS_SCOPE_INSUFFICIENT` 消费 | 1号位 | ⬜ **本函** |

## 三、待 1号位 认收与落码（契约定案 §1.4 消费契约）

```csharp
// LPD/资源侧遵守范围边界：某可调整需求/资源命中范围外，且必须移动才能成可行方案 →
// 停止并返回「当前业务范围不足」拒收信号（BUSINESS_SCOPE_INSUFFICIENT）。
public const string BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT";
```

**判定语义**（契约定案 §1.4）：
- 对象对应 **产品族（ProductFamily）/ 生产组织（ResourceOrgGroup）** 的业务键 ∈ `BusinessScope.ScopeValues` **或** `IsGlobal=true` → 可移动；否则不可移动。
- 若某需求/资源命中范围外且不移动则不可行 → 停止并返回 `BUSINESS_SCOPE_INSUFFICIENT`（拒收信号，门禁语义同 `SCOPE_VIOLATION` 一致——fail-closed，不静默降级）。
- `BusinessScope` 与既有 `InScopeLogicalDemandKeys`（重建后）关系：BusinessScope=**权限硬框**（谁可动），InScope=计划细粒度（可动范围）；两者并发，BusinessScope 优先为**硬边界**。

## 四、边界说明（1号位 无需代判，防越位）

- **本知会不强求**判断业务保护识别字段 **(i) IsInFrozenZone / (ii) ScopeJson 保护集合**——那是 0号位 待裁的正交项（契约定案 §五/§六），P1-2 过渡期仍以 2号位 `FirmHorizonDays` 兜底。1号位 本轮只需认收 + 落 `BUSINESS_SCOPE_INSUFFICIENT` 常量与判定入口即可。

## 五、1号位 落码清单

| # | 落点 | 动作 |
|---|---|---|
| 1 | 常量定义 | `BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT"`（Scheduling 或 Solver 契约侧） |
| 2 | 消费点 | `RunScope.BusinessScope` 非 null 时，遵守判定；范围外必要移动 → 返回拒收信号 |
| 3 | 单测 | 覆盖：IsGlobal 放行 / 命中 ScopeValues 可移动 / 范围外必要移动拒收 |

---

**发送人**：3号位 ｜ **日期**：2026-09-23 ｜ **版本**：v1.0
**对应**：契约定案 v1.1 §1.4 / §四 #2｜0号位《统一裁决回复》§七｜2号位《载体传递路径澄清 v1.0》
**本函性质**：链路就绪知会 + 1号位 遵守侧认收催办——其余号位无缺口，1号位 落码后 Business Scope 全链路闭环。