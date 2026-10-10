# APS V1 Q4-B · EXPEDITE 契约登记 —— 2号位 回执

**版本**：v1.0
**日期**：2026-09-23
**发送人**：2号位（数据引擎 · 主执行窗口）
**接收人**：3号位（契约登记 Owner）
**抄送**：0号位（PM）、1号位（Solver）
**依据**：3号位《APS V1 Q4-B · PriorityMode=EXPEDITE 定义契约登记 v1.0 20260923》（T3）

---

## 一、认可

2号位 **认可 §一~§七 契约登记，无异议**。登记语义与 0号位 Q4-B 裁决、2号位 S5 落码一致，逐项取证如下。

## 二、逐项核对（读工作区实际代码）

| 登记项 | 2号位 取证 | 结论 |
|---|---|---|
| §一 PriorityMode 二值轴 + 适用码 | `PriorityMode.cs:15/18` 注释「NORMAL=GANTT/EQUIPMENT/RESOURCE_CALENDAR/NEW_ORDER_CTP；EXPEDITE=NEW_ORDER_IMPACT 固定、NEW_ORDER_INSERT/EXISTING_ORDER_ADVANCE 可选」+ `BusinessTriggerType` 八码含 `NEW_ORDER_IMPACT=2` | ✅ 码名一致、无漂移 |
| §二 调整对象=Demand 非 Task + 业务链路 | S5 `DemandPriorityExecutor` 前置竞争层（Demand 层），链路=优先级调整→重排→重 Pegging→重 Task→重排程 | ✅ 一致 |
| §三 保护边界三类 | S5 只做排序前置，不突破字段/规则（执行事实/Firm/Frozen/硬约束由既有链路承载） | ✅ 一致 |
| §四 不建损失模型 + §五 不新增权重 | S5 未加 `EXPEDITE_WEIGHT/SCORE/PriorityFactor` | ✅ 一致 |
| §六 与 S5 对齐 | `LoadExpediteOrderCanonicalIdsAsync`（PeggingOrchestrator.cs:3792）读 `ScopeJsonV2`，`PriorityMode==EXPEDITE && OrderTargets 非空` → `OrderCanonicalId` 集合 | ✅ 一致 |

## 三、一处连带真空点（借本件提请 3号位 一并明确）

§一 定 **EXPEDITE 固定于 `NEW_ORDER_IMPACT`（急插影响分析）**，但该场景是**新订单**。2号位 S5 的 EXPEDITE 身份载体 = `OrderTargets[].OrderCanonicalId`，只能覆盖「已有订单提前」(`EXISTING_ORDER_ADVANCE`) 场景；**「新订单」的 `OrderCanonicalId` 是否进 `OrderTargets`、还是另走 `NEW_ORDER_*` 承载，冻结文档 §67/§68.1 未写死**。

→ 即 2号位 真空清单 **①**（见 `APS_V1_插单Candidate冻结文档v1.7落地对齐缺口清单_2号位发起_v1.0_20260923.md`）。建议 3号位（ScopeJson Schema Owner）借本次 EXPEDITE 契约登记**一并定「新订单承载字段」**，否则 EXPEDITE 主场景（急插新订单）的身份载体缺失，S5 读不到。

## 四、回执表

| 问 | 2号位 回口径 |
|---|---|
| §一~§七 EXPEDITE 契约登记认可 | ✅ **认可，无异议**（§二逐项取证一致）|
| 落点：05 契约 §7.0 注释补充登记 | —（非 2号位 动作，随 1号位/0号位）|
| 连带真空点（新订单承载） | ⚠️ 提请 3号位 一并明确（真空清单①）|

---

**发送人**：2号位 ｜ **日期**：2026-09-23
