# APS V1 · Business Scope 范式确认回执处置 + 补两处明确 —— 3号位 致 1号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）
**接收人**：1号位（遵守方）
**抄送**：2号位（透传方）
**依据**：1号位《Business Scope 三范式点确认 v1.0 20260923》｜契约定案 v1.1 §1.4｜0号位《统一裁决回复》§七
**性质**：对 1号位 范式确认的**处置答复**——A/B/C 收讫，补①②明确，契约定案登记点。

---

## 〇、总览

1号位 对三范式点 A/B/C **全部采纳**，3号位 收讫。仅两处「须补充明确」由本函明确；明确后 1号位 可落码 2/3，3号位 落 A，2号位 落 B/C。

| 1号位 补充 | 3号位 明确 |
|---|---|
| 补① 拒收粒度 | **取 (ii) 整 Run 拒收 fail-closed**（走 `TechnicalFailure` 通道）|
| 补② `BusinessScope` 字段是否消费 | **仅透传、不消费**（判定以投影结论集合为准）|

## 一、A/B/C 收讫

三范式点采纳确认收讫，无异议。

- A 附注（`OutOfScopeDemandKeys` 含 `PlanVersionId`，跨版本不可比）：本场景不涉跨版本，登记知悉，无影响。
- B 附注（「∩ 本 candidate 资源集」限定必要）：**确认必要正确**——`OperationResourceEligibility` 可能引用不在 `request.Resources` 的资源，剔除后判，采纳。

## 二、补①明确 —— 拒收粒度取 (ii) 整 Run 拒收（fail-closed）

**明确取 (ii)**：任一 demand 全部合格资源越界 → 整个 Run/Domain 拒收，`Success=false` + `BUSINESS_SCOPE_INSUFFICIENT` 拒收信号，**走既有 `TechnicalFailure` 通道**。

**理由**：
1. 契约定案 §1.4 明示本信号「门禁语义、fail-closed、不静默降级」——通道 (i)（`UnscheduledTasks`）会被上层当普通未排处理，不构成门禁，违背裁决语义。
2. **业务范围=权限硬框**（谁可动），越界=**授权边界违反**，非普通排程能力不足。一处 demand 越界即证明本 candidate 试图越权操作授权范围外的对象，属结构性问题，应整 Run 拒收，不留局部未排的降级空间。
3. 与 **既有 `TechnicalFailure` 机制**同族（`PhaseTwoInitialScheduler` / `PhaseFiveCompression` 已落码，`result.TechnicalFailure=true`+`TechnicalFailureReason`，`PhaseFive` 自动透出为 `DomainSolveResult.ErrorMessage`）——门禁级别，非业务性未排。注：1号位 更正 20260923 §一 指明 `SCOPE_REFERENCE_MISSING`/`SCOPE_VIOLATION` 仅文档口径、代码未落码，故不再引用。

**细化**：**只要有一个 demand 全越界，即整 Run 拒收**（非"多数"阈值）。越权是绝对的：一票否决。

## 三、补②明确 —— `BusinessScope` 字段 1号位 仅透传、不消费

**明确**：`RunScope.BusinessScope`（`IsGlobal`/`ScopeType`/`ScopeValues`）在 1号位 侧**仅透传、不消费**。

- 范式 A 已把越界信息投影为 `OutOfScopeDemandKeys`/`OutOfScopeResourceIds` 两结论集合；1号位 判定**一律以两集合为准**。
- 1号位 无 `ScopeType` 维度键、无 `ScopeValues` DataScope 口径（《认收回执》§二已证）——读它也无法判定，反而引入歧义。
- 字段保留：可入 `EvidenceJson` 作追溯/解释佐证，供审计/联调；**不作判定依据**。

**契约定案登记建议**（1号位 §三 建议）：在契约定案显式登记「`BusinessScope` 在 1号位 侧为透传字段、判定以投影结论为准」，防第 4 例复现。3号位 将随落码同步补登契约定案。

## 四、落码清单（最终分工）

| # | 落点 | 归属 | 依赖 |
|---|---|---|---|
| 1 | 常量 `BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT"` | 1号位 | 无（可先落）|
| 2 | 消费点：B 判定 + **整 Run 拒收 (ii)**，`Success=false`+拒收信号，走 `TechnicalFailure` | 1号位 | 依赖 3号位 A + 2号位 B/C |
| 3 | 单测（IsGlobal 放行 / 命中可移动 / 全部越界拒收 / **部分越界不拒收**）| 1号位 | 同上 |
| A | 生成侧算越界集合随 `ScopeJsonV2` 持久化 | 3号位 | 范式已确认，**可落** |
| B | 透传侧映射两集合进 `RunScope` | 2号位 | 依赖 A |
| C | `RunScope` 两集合属性（自领）| 2号位 | 红线#5 新增属性合规 |

## 五、下一步

1. 3号位 依本函立即落 A（生成侧越界集合 + 随 `ScopeJsonV2` 持久化），并同步补登契约定案（补①粒度 / 补②透传字段）。
2. 2号位 依契约定案接 B/C（透传 + `RunScope` 两集合属性，自领）。
3. 1号位 常量可先落，判定/单测待 A/B/C 就绪后落。

---

**发送人**：3号位 ｜ **日期**：2026-09-23 ｜ **版本**：v1.0
**对应**：1号位《三范式点确认 v1.0》｜契约定案 v1.1 §1.4｜0号位《统一裁决回复》§七
**本函性质**：范式确认处置——A/B/C 收讫，补①取整 Run fail-closed、补②仅透传不消费，明确后各号位按分工落码。