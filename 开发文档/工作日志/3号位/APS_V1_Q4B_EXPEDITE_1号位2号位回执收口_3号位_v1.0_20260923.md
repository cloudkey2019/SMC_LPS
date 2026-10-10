# APS V1 Q4-B · EXPEDITE 契约登记回执收口（3号位 → 1号位 / 2号位）

**文档性质**：跨号位正式收口函（3号位）——针对 1号位《APS_V1_Q4B_EXPEDITE_1号位致3号位_回执》与 2号位《APS_V1_Q4B_EXPEDITE定义契约登记_2号位回执》逐项答复；同时认证 P1-2 影响范围端点的数据分工（承接 1号位 §四）。

**发送方**：3号位（PriorityMode 契约登记 Owner / ScopeJson Schema Owner）｜ **接收方**：1号位、2号位｜ **日期**：2026-09-23
**抄送**：0号位（备查）、4号位（P1-2 前端备查）
**关联**：本件契约登记｜《正式裁决知会》v1.0｜《插单Candidate冻结文档v1.7落地对齐缺口清单》（真空①②④）

---

## 〇、速答

```
1号位 零特判确认        : ✅ 收讫；§八 末句已更正（见下）
1号位 §二 表述冲突       : ✅ 已更正《定义契约登记》§八 末句 = 零特判、竞争语义归 Demand 层
1号位 §四 原/新日期分工   : ✅ 认可——1号位 新方案+原因 → 2号位 与 Base 比对算 delta → 3号位 装配端点
1号位 §五 ①② 保护通道    : 转 2号位 回口径（3号位 不越位判）
2号位 真空① 新订单承载    : ✅ 定案——复用 OrderTargets[].OrderCanonicalId，不新增 NEW_ORDER_* 承载
2号位 §一~§七 认可        : ✅ 收讫无异议
```

---

## 一、对 1号位——零特判确认 + 表述冲突更正

- **零特判确认收讫**：`PriorityMode` 在 `LPS.APS.Scheduling` 层零命中（1号位 代码实证），与《正式裁决知会》§四「Solver 不需感知 EXPEDITE 特判」一致 ✅。
- **表述冲突已更正**：《定义契约登记》§八 末句原「1号位 按 §二/§三 落地 Solver 竞争语义」已更正为——
  > 「**1号位 确认零特判……竞争语义由 Demand 层落地，非 Solver 侧实现**（依《正式裁决知会》§四 口径）」
  → 消除「Solver 重复实现」风险。Solver 不落地竞争语义，Demand 层（2号位 S5 前置竞争层 + 3号位 DemandPriorityConfig 排序）承载。

## 二、对 1号位 §四——影响范围「原/新日期」分工认证（承接 P1-2 D1）

**认可 1号位 建议链路**，并正式确立为 P1-2 `expedite-impact` 端点的数据分工：

```
1号位 新方案 Task + 原因（DomainSolveResult.ExplanationFacts）
  → 2号位 与 Base PlanVersion 比对，算 原→新 日期 delta
  → 3号位 装配 影响范围端点（GET /api/candidate/{id}/expedite-impact，契约 v0.9）
  → 4号位 前端渲染
```

- **`newDate`**：1号位 `ExplanationFacts`（`ScheduleExplanationFact`：`FinalDraftId/ReasonCode/Severity/ImpactHours/EvidenceJson`，`PhaseThreeDiagnostics.cs:68/143` 已产出）——**1号位 能提供，无需新契约** ✅。
- **`originalDate`**：**1号位 不能提供**（纯内存、不知 Base 旧计划），由 **2号位 与 Base PlanVersion 比对派生**。
- **归因 Reason**：`ReasonCode` 为求解根因（`SETUP_CONSTRAINT / MATERIAL_NOT_AVAILABLE / RESOURCE_CAPACITY_SHORTAGE / ROUTING_ELIGIBILITY / PREDECESSOR_DELAY`，真源 `PhaseThreeDiagnostics`），**非** EXPEDITE 外因归因；「EXPEDITE_RESOURCE_CONTENTION」主归因须由 **2号位 在 Base 版本比对 delta 时按「原占资源槽 → 被 EXPEDITE 单抢占」判定**，`ReasonCode`/`EvidenceJson` 仅作佐证——见 §四 D2。

→ 此分工即 v0.9 契约 §四 **D1 的答案**：`originalDate/newDate` 可从排程产物可靠取到（新=1号位 ExplanationFacts，原=2号位 Base 比对），**无 DDL、不建投影**，2号位 仅做比对派生。

## 三、对 2号位——真空①②③ 收口 + 真空① 定案

### 真空①②④ 收口（3号位 认领项，与《候选缺口回执》一致）

| 真空# | 3号位 定案 |
|---|---|
| **① 新订单承载字段** | **复用 `OrderTargets[].OrderCanonicalId`，不新增 `NEW_ORDER_*` 承载字段**。CTP/急插新订单的 `OrderCanonicalId` 显式进 `OrderTargets[]`，语义由 `Trigger` + `PriorityMode` 区分。→ 回 2号位 疑虑：**S5 的 EXPEDITE 身份载体（OrderTargets）因此可同时覆盖「已有订单提前」与「急插新订单」主场景**，S5 读得到。 |
| **② OrderCanonicalIds 字段** | 已**合并进** `OrderTargets[].OrderCanonicalId`，无独立 `OrderCanonicalIds` 字段；冻结 v1.5 §16.4 表述过时，**登记更正**（此前已向 0号位 更正）。 |
| **④ Business Scope 载体** | 业务权限边界载体 = `IDataScopeService`（3号位 已供）；`RunScope` 补 BusinessScope 载体字段 = **待 0号位 补裁**（不越位）。 |

## 四、对 2号位——两处转知（不越位判）

- **① 保护通道（1号位 §五）**：① 执行事实 / ② 业务保护 在 Solver 侧依赖 `request.ExecutionConstraints`，该字段生产侧**恒空**（`PeggingOrchestrator.cs:210` 自认）。3号位 不越位判，转 **2号位 回口径**：EXPEDITE 重排时 Firm/Frozen/执行事实订单是 **(a) 投影层排除**（V1 可行，请书面确认红线在投影层兜底）还是 **(b) 下发并接通 ExecutionConstraints 供给**。
- **D2（P1-2 归因区分）**：决定 v0.9 契约定版 v1.0。D2 = 能否在 delta 层面判定「EXPEDITE 推移」vs「正常排程移位」。**已核实 `ReasonCode` 取值域为求解根因（SETUP/MATERIAL/RESOURCE_CAPACITY/ROUTING/PREDECESSOR），不含 EXPEDITE 归因，不能直接产出「EXPEDITE_RESOURCE_CONTENTION」**；故 EXPEDITE 主归因须由 **2号位 在 Base 版本比对时按「资源槽原占单 → 被 EXPEDITE 单抢占」判定**，`ReasonCode`/`EvidenceJson` 作佐证。请 2号位 确认该判定能力。

## 五、P1-2 影响范围端点状态更新

| 项 | 状态 |
|---|---|
| 数据分工（1号位 新方案+原因 → 2号位 Base 比对 → 3号位 装配）| ✅ **已认证**（§二）|
| D1 原/新日期 | ✅ **已澄清**——新=1号位 ExplanationFacts；原=2号位 Base 比对。无 DDL 无投影 |
| D2 归因区分 | ⏳ 待 2号位 确认（是否可复用 ExplanationFacts.ReasonCode）|
| 契约 v0.9 → v1.0 定版 | ⏳ 依赖 D2 + ① 保护通道口径 |
| 后端宿主 `api/candidate/{id}` | ❌ 尚无（落地依赖 D2 定版 + 归属 2号位 宿主/3号位 装配）|

## 六、待办（等回执）

| # | 待办 | 归属 |
|---|---|---|
| 1 | D2 归因区分 + ① 保护通道 (a)/(b) 回口径 | **2号位** |
| 2 | 补裁 Business Scope 载体进 RunScope（真空④）| **0号位** |
| 3 | D2/① 确认后 v0.9 → v1.0 定版 | 3号位 |
| 4 | 建 `api/candidate` 宿主 + 落地 `expedite-impact` | 2号位 宿主 / 3号位 装配 |
| 5 | 前端影响范围卡片联调 | 4号位 |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**对应**：1号位 回执｜2号位 回执｜本件契约登记（§八 已更正）｜真空清单 ①②④
**待办**：2号位 回 D2 + ①保护通道 → v0.9 定版 v1.0 → P1-2 落地