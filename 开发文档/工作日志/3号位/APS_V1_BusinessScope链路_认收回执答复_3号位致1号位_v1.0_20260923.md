# APS V1 · Business Scope 认收回执答复（请确认范式）—— 3号位 致 1号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）
**接收人**：1号位（遵守方）
**抄送**：2号位（透传方）
**依据**：1号位《Business Scope 消费端认收回执 v1.0 20260923》｜3号位《链路就绪致1号位认收消费端 v1.0》｜0号位《统一裁决回复》§七｜契约定案 v1.1 §1.4/§二
**性质**：对 1号位 认收回执的**实质答复** 之范式确认件——请 1号位 确认本项目后落码，无需 0号位 裁决。

---

## 〇、总览

1号位 回执三处均成立，本答复**无异议推翻项**，仅请 1号位 就以下三范式点**答复「采纳 / 需调整」**：

| 1号位 回执项 | 3号位 处置 | 请 1号位 确认 |
|---|---|---|
| §二 键缺口（ProductFamily/ResourceOrgGroup 缺失）| 认收为真 → **采信投影结论范式**（不投判据）| 请确认 A |
| §三 「移动」→「资源可行性」转换 | 采信 + 确认等价 | 请确认 B |
| §四 落码清单 | 常量 1号位 可先落；判定依赖新增字段 | 请确认 C |

## 一、载体认收 —— 收讫

1号位 对 `RunScope.BusinessScope` 的认收（形状/层级），3号位 收讫，无异议。

## 二、A —— 键缺口 → 采信「投影结论」范式

1号位 实证成立（`LPS.APS.Scheduling` 全层 `ProductFamily`/`ResourceOrgGroup` 零命中；唯可判键 `ProductionDepartmentId`）。

**3号位 采信 1号位 §2.2 建议**——照 `DueDateOverrides` 既有范式，投影侧把越界**判定前置为结论**：

```csharp
// RunScope 新增（2号位 透传）：
IReadOnlyList<string> OutOfScopeDemandKeys    // 越界 demand 的 LogicalDemandKey（1号位 内存键）
IReadOnlyList<int>    OutOfScopeResourceIds   // 越界资源（与 ResourceDefinition.ResourceId 同域）
```

**职责归属**：判定=3号位 `RunLifecycleService.CreateCandidateRunAsync` 装配时经 `ResolveScopeAsync` 算 → 随 `ScopeJsonV2` 持久化；透传=2号位 `BuildRunScopeAsync` 原样映射（**不判权限**，符合契约定案 §二）；1号位 仅消费结论。红线 #5/#6 合规（只加字段/属性，零 DDL）。

## 三、B —— 资源可行性判定（确认与契约定案 §1.4 等价）

1号位 无「移动已有 Task」概念，提出转换「资源可行性」。**3号位 确认等价且可实现**：

```text
对每 demand：合格资源集(= OperationResourceEligibility 命中 ∩ 本 candidate 资源集) ⊆ OutOfScopeResourceIds
            → 拒收 BUSINESS_SCOPE_INSUFFICIENT
```

细粒度：合格资源部分越界、部分在界内 → 用界内资源可排，不拒收；仅当**全部**合格资源越界才拒收（match 契约定案 §1.4「必须移动才能成可行方案且范围外」语义）。

## 四、C —— 落码清单

| # | 落点 | 归属 | 依赖 |
|---|---|---|---|
| 1 | 常量 `BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT"` | 1号位 | 无（可先落）|
| 2 | 消费点：`all 合格资源 ⊆ OutOfScopeResourceIds → 拒收` | 1号位 | 依赖新增字段 |
| 3 | 单测 | 1号位 | 依赖新增字段 |
| A | 生成侧算越界集合随 ScopeJsonV2 持久化 | **3号位** | 依赖本函确认 |
| B | 透传侧映射进 RunScope | **2号位** | 依赖 A |
| C | `RunScope` 两集合属性（自领）| **2号位** | 红线#5 新增属性合规 |

## 五、请 1号位 确认

请 1号位 就 **A（投影结论范式）、B（资源可行性判定式）、C（落码清单）** 回「采纳 / 需调整」。确认后 3号位 落 A，2号位 落 B/C，1号位 落 1/2/3，Business Scope 全链路闭环。

---

**发送人**：3号位 ｜ **日期**：2026-09-23 ｜ **版本**：v1.0
**对应**：1号位《Business Scope 消费端认收回执 v1.0》｜3号位《链路就绪致1号位认收消费端 v1.0》｜契约定案 v1.1 §1.4/§二
**本函性质**：认收回执的范式确认件——三范式点请 1号位 确认后即可落码，不依赖 0号位 裁决。