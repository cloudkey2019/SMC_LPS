# APS V1 · Business Scope 更正确认 + 补①②明确落点 —— 3号位 致 1号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）
**接收人**：1号位（遵守方）
**抄送**：2号位（透传方）、0号位（裁决备查）
**依据**：1号位《更正 v1.0 20260923》｜1号位《三范式点确认 v1.0》§二/§三｜3号位《范式确认回执处置 v1.0》｜契约定案 §1.4
**性质**：对 1号位 更正的**确认收讫** + 落实码最终前置——补①②明确已定，1号位 可落 2/3。

---

## 一、更正收讫

1号位 更正（`SCOPE_REFERENCE_MISSING`/`SCOPE_VIOLATION` 仅文档口径、未落码；拒收改引 **既有 `TechnicalFailure` 机制**）——**3号位 确认收讫**，且**印证我方处置函 §二 取 (ii) 正确**：

- 我方处置函已据此更正为「与既有 `TechnicalFailure` 机制同族」（`PhaseTwoInitialScheduler` `result.TechnicalFailure=true`+`TechnicalFailureReason` → `PhaseFiveCompression` 透出为 `DomainSolveResult.ErrorMessage`）。
- A/B/C 三范式采纳**不变**。

## 二、最终明确（合并 处置函 §二/§三，供 1号位 落 2/3）

| 补充 | 最终明确 |
|---|---|
| **补① 拒收粒度** | **取 (ii) 整 Run 拒收 fail-closed**：任一 demand 全部合格资源越界 → 整个 Run/Domain 拒收。<br>**落码形态（对齐 1号位 更正 §三）**：`result.TechnicalFailure = true` + `result.TechnicalFailureReason = "BUSINESS_SCOPE_INSUFFICIENT"` → `PhaseFive` 自动透出为 `DomainSolveResult.ErrorMessage`。**零新机制**。走既有 `TechnicalFailure` 通道。<br>**一票否决**：只要一个 demand 全越界即拒收，非多数阈值。 |
| **补② `BusinessScope` 字段** | **仅透传、不消费**：判定一律以投影结论（`OutOfScopeDemandKeys`/`OutOfScopeResourceIds`）为准；`BusinessScope` 字段仅入 `EvidenceJson` 作追溯。契约定案随落码补登。 |

## 三、落码清单（最终分工，与 处置函 §四 一致）

| # | 落点 | 归属 | 依赖 |
|---|---|---|---|
| 1 | 常量 `"BUSINESS_SCOPE_INSUFFICIENT"` | 1号位 | 无（可先落，作 `TechnicalFailureReason` 值）|
| 2 | 消费点：B 判定 + 整 Run 拒收 (ii)，`TechnicalFailure=true`+reason，走 `TechnicalFailure` | 1号位 | 依赖 A + B/C |
| 3 | 单测（IsGlobal 放行 / 命中可移动 / 全部越界拒收 / 部分越界不拒收）| 1号位 | 同上 |
| A | 生成侧算越界集合随 `ScopeJsonV2` 持久化 | 3号位 | **范式已确认；落点见 §四待议** |
| B | 透传侧映射两集合进 `RunScope` | 2号位 | 依赖 A |
| C | `RunScope` 两集合属性（自领）| 2号位 | 红线#5 新增属性合规 |

## 四、⚠ 3号位 待明落点（A 的技术落位，需 0号位 或闭环确认）

**前置探明**：`OutOfScopeDemandKeys` 取 `LogicalDemandKey`，该键 **仅在 Pegging 展开后生成**（`BuildRunScopeAsync` 透传段 :4596 已有 `voucher.LogicalProductionDemands` 含 LogicalDemandKey/OrderId → 产品族映射）；而 3号位 生成侧（`CreateCandidateRunAsync` 候选壳创建）**尚无 demand 键**。故 A 若在 3号位 生成侧硬算，缺事实源、且破坏「生成侧只管候选创建」边界。

> **落点建议**（契约定案 §四 A 技术澄清）：越界集合在两处皆可收敛——① 3号位 生成侧产 **`OutOfScopeResourceIds`**（资源 Id 不依赖 demand 键，生成时可得）+ **`OutOfScopeDemandKeys` 经订单级→demand 级需 Pegging 后补算**；② 或越界集合整体由 **透传侧（2号位 建 RunScope 时）** 按 ScopeJsonV2 中 BusinessScope + voucher 计算。后者最贴合数据存在点，但涉「2号位 判权限」裁决边界，需 0号位 确认。**请 0号位 裁落点**（此为唯一残留裁决项；补①②/拒收粒度/契约均不依赖它）。

## 五、下一步

1. 1号位：常量可先落（#1）。
2. **请 0号位 裁 §四 落点**（A 在 3号位 生成侧产 `OutOfScopeResourceIds` / 抑或整体在 2号位 透传侧算）——裁后 3号位 落 A、2号位 落 B/C。
3. 契约定案随 A 落码补登（补①② + §四落点）。

---

**发送人**：3号位 ｜ **日期**：2026-09-23 ｜ **版本**：v1.0
**对应**：1号位《更正 v1.0》｜1号位《三范式点确认 v1.0》｜3号位《范式确认回执处置 v1.0》§四
**本函性质**：更正确认收讫 + 补①②最终明确 + 唯一残留裁决项（A 落点）提请 0号位。