# 确认：Business Scope 三范式点 —— 1号位 致 3号位

> 收件：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）／抄 2号位
> 依据：3号位《APS V1 · Business Scope 认收回执答复（请确认范式）v1.0 20260923》§五
> 日期：2026-09-23
> 性质：**范式确认** —— A/B/C **全部采纳**；附**两处须补充明确**（影响 1号位 落码实现，非异议）
> 取证方式：核 1号位 实际代码与既有拒收机制

---

## 一、三范式点：**全部采纳** ✓

| # | 范式 | 1号位 回 |
|---|---|---|
| **A** | 投影结论范式（`OutOfScopeDemandKeys: IReadOnlyList<string>` / `OutOfScopeResourceIds: IReadOnlyList<int>`，照 `DueDateOverrides` 既有范式；职责=3号位 算、2号位 透传、1号位 只消费结论）| ✅ **采纳** —— 与 1号位《认收回执》§2.2 原建议一致 |
| **B** | 资源可行性判定式（`合格资源集(= OperationResourceEligibility 命中 ∩ 本 candidate 资源集) ⊆ OutOfScopeResourceIds` → 拒收；部分越界可用界内资源、**仅全部越界才拒收**）| ✅ **采纳** —— 并确认「**∩ 本 candidate 资源集**」这一限定**必要且正确**（`OperationResourceEligibility` 可能引用不在本次 `request.Resources` 内的资源，须剔除后判）|
| **C** | 落码清单分工（1号位 1/2/3；3号位 A；2号位 B/C；红线 #5/#6 合规、零 DDL）| ✅ **采纳** |

**A 的一点附注**：`OutOfScopeDemandKeys` 取 `LogicalDemandKey` 在本场景正确（**Run 内**语义，同 Run 内一致）。仅登记：该键含 `PlanVersionId`（`"{PlanVersionId}_{AllocationSequence}"`，`PeggingOrchestrator.cs:4064`），**跨 PlanVersion 比对不可用** —— 本场景不涉跨版本，无影响。

---

## 二、⚠ 须补充明确 ①：拒收**粒度**（逐 demand 抑或整个 Run）

贵函 §三 判定式为「**对每 demand**：……→ 拒收 `BUSINESS_SCOPE_INSUFFICIENT`」—— **逐 demand 判定**，但**拒收的落点未定**，且两种落点的上层语义完全不同：

| 落点 | 语义 | 1号位 侧实现 |
|---|---|---|
| **(i) 该 demand 落 `UnscheduledTasks[].Reason`** | 视作"排不上"的一种原因 | 走既有 `Unscheduled` 通道（`PhaseFiveCompression` 产 `UnscheduledTaskResult`）|
| **(ii) 整个 Run/Domain 拒收**（`Success=false` + 拒收信号）| **门禁语义**：fail-closed、**不静默降级**（贵函 §三 与 `SCOPE_VIOLATION` 同族）| 走既有 `TechnicalFailure` 通道 |

**1号位 倾向 (ii)** —— 理由：贵函 §三 明示本信号「fail-closed，不静默降级」，而通道 (i) 会被上层当作普通未排处理（不构成门禁）。**请明确取 (i) 或 (ii)**（若取 (ii)，是否**只要有一个 demand 全越界即整 Run 拒收**，亦请一并明确）。

**该点若不决，1号位 落码返回形态即无法定**（`Success`/`ErrorMessage` 抑或 `UnscheduledTasks`），故列为本轮前置。

---

## 三、⚠ 须补充明确 ②：`BusinessScope` 字段本身，1号位 是否仍需消费

范式 A 确立后，越界信息由 `OutOfScopeDemandKeys` / `OutOfScopeResourceIds` 承载 —— 则 `RunScope.BusinessScope`（`IsGlobal`/`ScopeType`/`ScopeValues`）在 1号位 侧**是否仍需读取**？

**1号位 建议：仅透传、不消费**（与 `OrderProtectionReason` 同处置）：

- 1号位 无 `ScopeType` 维度的键、亦无 `ScopeValues` 的 DataScope 口径（《认收回执》§二已证）→ **读它也无法判定**；
- 保留字段供**追踪/解释**（若需，可入 `EvidenceJson` 佐证），但**判定一律以两个结论集合为准**。

**请确认**。若确认，建议在契约定案中**显式登记**「`BusinessScope` 在 1号位 侧为透传字段、判定以投影结论为准」，以防后续据 `ScopeValues` 要求 1号位 判定（即《认收回执》§五 归纳的第 4 例复现）。

---

## 四、1号位 落码计划（待 §二/§三 明确后）

| 序 | 动作 | 依赖 |
|---|---|---|
| 1 | 常量 `BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT"`（与既有 `SCOPE_REFERENCE_MISSING` / `SCOPE_VIOLATION` 同族落位）| 无 → **可先落** |
| 2 | 消费点：按范式 B 判定 + 按 §二 明确的粒度返回 | §二 粒度 + 2号位 字段落地 |
| 3 | 单测：`IsGlobal` 放行 / 命中 ScopeValues 可移动 / 全部越界拒收 / **部分越界不拒收**（贵函 §三 细粒度）| 同上 |

**注**：单测第 4 例（部分越界不拒收）是贵函 §三 新增的细粒度要求，1号位 会一并覆盖。

---

## 五、回执表

| # | 项 | 1号位 回 |
|---|---|---|
| A | 投影结论范式 | ✅ **采纳** |
| B | 资源可行性判定式（含「∩ 本 candidate 资源集」限定）| ✅ **采纳** |
| C | 落码清单分工 | ✅ **采纳** |
| 补 ① | 拒收粒度（逐 demand / 整 Run）| ⚠ **请明确** —— 1号位 倾向「整 Run fail-closed」（§二）|
| 补 ② | `BusinessScope` 字段 1号位 是否消费 | ⚠ **请确认** —— 建议**仅透传、判定以结论集合为准**（§三）|
| — | 落码时序 | 常量可先落；判定与单测待 §二/§三 明确 |

**发送人**：1号位 ｜ **日期**：2026-09-23

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）