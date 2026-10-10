# 3号位 → 0号位：DomainKey 规则集域维度裁决 · 收口回执

> 发件：3号位（Application / RBAC / Scope / 契约治理 Owner）｜ 收件：**0号位（PM）**
> 抄送：1号位、2号位、4号位、5号位
> 日期：2026-09-28 ｜ 版本：v1.0
> 对象：0号位《关于 RuleSet 与 Domain Scope 二次校验的正式回复》（2026-09-28，裁决 D1）
> 性质：认收回执 ＋ 执行口径落地（**零 DDL、零改码**——现有实现已对象化，仅补「对象化 Scope 校验说明」）

---

## 〇、一句话

**认收 0号位 裁决**：`RuleSet / RuleSetVersion` 不增加 `DomainKey`、不改「按 Domain 归属的规则集」；`EnsureInScopeAsync` 按「**被操作对象的真实业务范围**」分流校验；Domain 映射唯一权威 = `DomainDefinition`。查证后确认：**3号位 现有 `EnsureInScopeAsync(userId, scopeType, value)` 已是对象化签名，「RuleSet.DomainKey」从未落码**——本项**零 DDL、零改码，仅补说明即可收口**。

---

## 一、裁决认收

| 0号位 结论 | 3号位 认收 |
|---|---|
| V1 不在 RuleSet / RuleSetVersion 新增 DomainKey | ✅ 认收，零 DDL |
| RuleSet = 规则治理/版本容器，非 Domain 对象 | ✅ 认收（例证：标准全量排程规则集 / 插单影响分析规则集是主题/用途容器，非「BJ 某族域规则集」） |
| 不建第二套 RuleSet→Domain 映射（RuleSetDomain / Mapping / Scope / DomainKeyJson） | ✅ 认收，不新增 |
| Domain 唯一权威映射源 = DomainDefinition（3号位 治理） | ✅ 认收 |
| 更正前提：「RuleSet 无 DomainKey ⇒ Scope 做不了」不成立 | ✅ 认收，见 §二核实 |

---

## 二、现状核实（代码已符合裁决方向，无需改码）

`IDataScopeService.EnsureInScopeAsync(int userId, string scopeType, string value, ...)` 已是**对象化签名**——不假设治理对象天然带 DomainKey。现有调用点已按对象正确分流：

| 对象类别（0号位 §四） | 现状调用点 | 传入 scopeType/value |
|---|---|---|
| ① Run / Candidate / PlanVersion | `RunLifecycleService.cs:341`、`:348`；`CtpController.cs:42` | `(Domain, domainKey)` ——真实 DomainKey ✅ |
| ② 具体规则内容 | 尚无调用点（未落码） | 后续按 Department/Factory/ProductFamily 分流 |
| ③ RuleSetVersion 整体发布 | 尚无调用点（未落码） | 后续按「有效规则业务范围集合」逐项校验 |
| 对象化旁证 | `MesDispatchController.cs:42` | `(Factory, factoryCode)` ✅ |

结论：**「RuleSet.DomainKey」在 3号位 侧从未落码**，裁决方向与现有实现一致，无需改签名、无需改调用点、无需 DDL。

---

## 三、执行口径（对象化 Scope 校验——并入开发包说明，后续落码据此执行）

1. **Run / Candidate / PlanVersion** → `(Domain, BasePlanVersion.DomainKey / PlanVersion.DomainKey / ScheduleRun.ExpectedDomainKeysJson)`；白天 Candidate 单域，ExpectedDomainKeysJson 必须等于 BasePlanVersion.DomainKey。
2. **具体规则内容**（如 Setup 规则）→ 按规则自身业务字段校验：有 Department 查 Department、有 Factory/ProductFamily 查对应维度；确需 Domain 换算时经 DomainDefinition 解析，**不回写 RuleSet**。
3. **RuleSetVersion 整体发布** → ① 取该版本本次有效规则；② 汇总其实际业务 Scope；③ 校验当前用户对每一范围是否有权治理；④ 任一越权 => 整体 Fail-Closed 返回 403。
4. **Domain 映射** → 仅 `DomainDefinition` 为唯一权威；禁止按 RuleSet 名称推 Domain、禁止 ProductFamily 字符串拼 DomainKey、禁止建 RuleSet→Domain 桥表。
5. **无法解析 → Fail-Closed / 403**。
6. 两层关系（用户 Business Scope vs 运行 Domain 边界）分别校验，**不通过 RuleSet.DomainKey 合并成一层**。

---

## 四、我侧本轮零改动清单（边界明示）

| 项 | 结论 |
|---|---|
| DDL | 零改动（不增 DomainKey、不建映射表） |
| IDataScopeService 签名 | 零改动（已对象化） |
| 现有调用点 | 零改动（已按对象分流） |
| 对象化 Scope 校验说明 | 并入 3号位 开发包文档（契约/实现说明补档，非本次单独落码） |

---

## 五、D1 闭环

本件为 D1 收口凭证：0号位 已裁决、3号位 已认收并核实代码合规。**D1 闭环**；后续「③ RuleSetVersion 发布 / ② 规则内容维护」若落码，按 §三 执行口径实施（届时单开一批，不动本裁决）。

---

**发件**：3号位 ｜ 日期：2026-09-28 ｜ 版本：v1.0
**本件性质**：认收 0号位 D1 裁决 ＋ 落地对象化 Scope 校验口径；零 DDL、零改码，D1 闭环。