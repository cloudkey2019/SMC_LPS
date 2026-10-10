# APS V1 插单/Candidate 冻结文档 v1.7 落地对齐缺口 — ①②④ 三项回执（3号位）

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（ScopeJson Schema Owner / Auth·Business Scope Owner）
**接收人**：2号位（数据引擎）、0号位（PM，④ 需补裁定）
**抄送**：1号位（Solver，③⑤ 归其）、5号位、4号位（备查）
**性质**：**缺口回执件**——认领 2号位 缺口清单（v1.0_20260923）中归 3号位 的 ①②④ 三项；红线 #5「接口即契约」：本单**不改任何冻结签名**，② 补契约登记更正、④ 载体方案交 0号位 补裁决后才落码
**依据**：2号位 缺口清单 v1.0_20260923；基线 v1.7 §67/§68.1/§68.2/§68.3/§68.7；业务说明 v1.5 §16.4

---

## 一、回执总览

| 缺口 | 归主 | 3号位 回执 |
|---|---|---|
| ① CTP/急插新订单承载字段 | 3号位 | **复用 `OrderTargets[].OrderCanonicalId` 显式声明**，不新增 `NEW_ORDER_*` 字段（§三）|
| ② OrderCanonicalIds 并存字段 | 3号位 | **已合并进 OrderTargets**，冻结 §16.4 表述过时需登记更正（§四）|
| ④ Business Scope 载体缺失 | 3号位（来源）+0号位（载体字段裁定）| 来源=`IDataScopeService`，结构=`DataScopeContext`；载体方案见 §五，**待 0号位 补裁定** |
| ③ 变化 Seed 消费+拒收触发层 | 1号位+0号位 | 3号位 出边界登记，不落消费实现（不越位）|
| ⑤ AllocationSequence 跨版本依赖 | 1号位 | 归 1号位 |
| ⑥ 白天 BOM 就绪时机 | 0号位+5号位 | 归 0号位/5号位 |

---

## 二、现状（3号位 已核）

1. `ScopeJsonV2`（`LPS.APS.Core/DTOs/Governance/ScopeJsonV2.cs`）**只有 `OrderTargets[]`**，`OrderTarget` 内嵌 `OrderCanonicalId`（:38）+ `ManualTargetDueDate`（:40，可空）。**无独立 `OrderCanonicalIds` 字段**。
2. 八码 Trigger（`BusinessTriggerType.cs`）已含 `NEW_ORDER_CTP`/`NEW_ORDER_IMPACT`/`NEW_ORDER_INSERT`（§一 类别回执），已能区分「新订单」场景。
3. Business Scope 来源已实现：`IDataScopeService.ResolveScopeAsync(userId)`（F-G4，Auth 域）→ `DataScopeContext`（含 Domain/Factory/ProductFamily/Department/ResourceOrgGroup 维度、Global）。
4. `DomainSolveRequest.RunScope`（:278）现含 Trigger/PriorityMode/InScopeLogicalDemandKeys/DueDateOverrides/TaskTargetOverrides/ChangedResourceIds，**无 BusinessScope 字段**。

---

## 三、① 回执：新订单承载 = 复用 OrderTargets[]，不新增 NEW_ORDER_* 字段

**口径（3号位 定）**：

> CTP / 急插的「本次明确选择的新订单」，通过 `OrderTargets[].OrderCanonicalId` **显式声明**进入候选，不新增 `NEW_ORDER_*` 承载字段。订单集合**唯一载体 = `OrderTargets[]`**（含全局的「已有订单提前」与「新订单」两类订单）。

**理由**：
1. 基线 v1.7 §67 已把 `OrderTargets` 抽象为「多订单目标集合」，**并未限定为「已有订单」**——§68.1 CTP「针对已进入事实的新订单 / 明确选择的新订单」、§68.3「一次选择多个订单」均为同一 `OrderTargets[]` 语义；`OrderTarget.ManualTargetDueDate` 为 `DateTime?`（CTP 新订单无人工交期可空，已有订单提前可填），天然兼容两类。
2. 「新订单 vs 已有订单」的场景差异**已由八码 Trigger 轴区分**（NEW_ORDER_* vs EXISTING_ORDER_ADVANCE），**无需再用一个独立字段表达**——再加 `NEW_ORDER_*` 字段会造成双源冗余，违反单一真相。
3. 「§67『明确选择』字段形态」=`OrderTargets[].OrderCanonicalId`——**凡进入本次 OrderTargets 集合的订单即「被明确选择」**；不在此集合的新订单不自动进入（§67「本次没有被明确选择的其它新订单不自动进入」）。

**对 2号位**：CTP/急插的新订单作为 `OrderTargets[].OrderCanonicalId` 传入即显式声明；2号位 据此投影 `InScopeLogicalDemandKeys` / `ChangeSeedKeys` 差异集（与现有「订单数据源钉基线 + 重新 Pegging + 差异集捕获」衔接，仅补显式集合入口）。

---

## 四、② 回执：OrderCanonicalIds 已合并进 OrderTargets，冻结 §16.4 表述登记更正

**口径（3号位 定，契约登记更正）**：

> 业务说明 v1.5 §16.4 末「如果 `OrderCanonicalIds` 与 `OrderTargets[]` 同时存在，两者订单集合必须一致」中**独立 `OrderCanonicalIds` 字段不再存在**——订单集合唯一载体 = `OrderTargets[].OrderCanonicalId`（3号位 M1 交付，`OrderCanonicalId` 内嵌、去重唯一）。**§16.4 该句为过时表述，本件登记更正**：不再校验「双字段一致」，因已无双字段；语义由 `OrderTargets[]` 单一真相保证。

**理由**：ScopeJsonV2 自 v2 起即「单集合 + 内嵌 OrderCanonicalId」，不设独立 `OrderCanonicalIds`。独立字段属 v1 遗留描述，M1 未实现、也不应补——避免双源冗余与「一致」约束的额外校验负担。

**登记动作**：本件即为登记更正件；后续 3号位 在《业务说明 v1.5 §16.4》所在契约族补更正批注（不改 v1.5 签名，仅登记）。

---

## 五、④ 回执：Business Scope 来源与结构（载体方案，待 0号位 补裁定）

**（一）来源（3号位 可定，Auth 域已有）**：

> Business Scope（用户权限边界）= `IDataScopeService.ResolveScopeAsync(userId)`（LPS.APS.Core/Interfaces/IDataScopeService.cs，3号位 M1 已实现，F-G4）→ `DataScopeContext`（LPS.APS.Core/Authorization/DataScopeContext.cs）。含 `Domain` / `Factory` / `ProductFamily` / `Department` / `ResourceOrgGroup` 维度（每维度并集 OR、跨维度交集 AND、Global 全放行、空 = 拒绝全部）。

**（二）结构（3号位 推荐，供缺接口口径）**：

白天 Candidate **严格单 Domain**（`CandidateRunCreateSpec.DomainKey` 必填），故本 Run Business Scope 可收敛为：

```text
RunScope.BusinessScope（候选字段）：
  DomainKeys     IReadOnlyList<string>   // 用户有效 Domain 集合（白天兜底应含 Run.DomainKey）
  FactoryCode    string?                 // 用户有效工厂（单域轴线简化）
  IsGlobal       bool                    // Global 放行时 = true（全可移动）
```

- v1.7 §68.7「Scope 外 Task 不得移动、仍作真实资源/时间占用」→ 1号位 依 `DomainKeys` 判定 Run 内 Task 的可移动集合（InScope）与补集（Anchor/Block）。
- 与 `InScopeLogicalDemandKeys`（M5 已登记 ④A 组合规则）关系：**BusinessScope 是「用户权限边界」→ 决定 Task 是否可移动（Anchor 补集约）**；InScopeLogicalDemandKeys 是「本次业务范围 Seed」→ 决定 Demand 进入范围。两者正交，可并为一个 Anchor 推导输入（3号位 建议：`DomainKeys` 作硬权限边界、InScopeLogicalDemandKeys 作本次软范围，1号位 求并集可移动）。

**（三）红线处理（需 0号位 补裁决）**：

> 现状 `DomainSolveRequest.RunScope` **无该字段**（2号位 无从投影）。补字段属**改 `RunScope` 契约（红线 #5 接口即契约）**——**3号位 不擅自落码**，本件提交载体方案，请 0号位 补冻结「Business Scope 载体字段进 RunScope」口径后，3号位 补契约文档变更 → 2号位 投影落库。

---

## 六、①②④ 对既有登记的不冲突

| 既有登记 | 与本回执关系 |
|---|---|
| M5 消费壳契约登记（v1.1，2026-09-22）| ② 与其「Ø1 组合规则」一致；④ BusinessScope 作新增载入，不断 ④A |
| ScopeJsonV2 序列化口径（A 字符串码）| 本回执不新增序列化字段（①）或仅登记更正（②），序列化无变化 |
| 0号位 ④A 倾向同意（InScopeLogicalDemandKeys 组合）| ④B 语义沿用；本件⑤不重开 ④A |

---

## 七、不越位声明

- ③（Seed 消费契约 + 拒收触发层）归 1号位 + 0号位——3号位 不替代实现；
- ⑤（AllocationSequence）归 1号位；⑥（白天 BOM）归 0号位 / 5号位；
- ④ 载体字段**待 0号位 补裁定**，3号位 在裁定前不落码 RunScope 变更（红线 #5）。

---

## 八、回执表

| 问 | 2号位 | 0号位 |
|---|---|---|
| ① 复用 OrderTargets 声明新订单，不新增字段 | （待确认）| — |
| ② §16.4 独立 OrderCanonicalIds 为过时表述、登记更正 | （待确认）| — |
| ④ RunScope.BusinessScope 载体方案（DomainKeys + Factory + IsGlobal）| （待确认）| **补冻结「载体字段进 RunScope」** |

---

**发送人**：3号位 ｜ **日期**：2026-09-23