# APS V1 Q4-B · PriorityMode=EXPEDITE 定义契约登记（3号位）

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（规则/参数治理、PriorityMode 契约登记 Owner）
**接收人**：1号位（Solver 落地）、2号位（Demand 前置竞争层已落地）
**抄送**：0号位（PM，裁决备查）、5号位（查询侧备查）、4号位（前端备查）
**性质**：**契约登记件**（T3）——登记 0号位 Q4-B 裁决的 EXPEDITE 契约语义；**不改任何冻结签名（红线 #5）**，不做代码改动，纯文档登记
**依据**：
- 0号位《Q4-B：PriorityMode=EXPEDITE强度定义裁决》（2026-09-23 正式裁决，8 节全文）
- `LPS.APS.Core/Enum/PriorityMode.cs`（NORMAL/EXPEDITE 二值，ScopeJsonV2.PriorityMode 引用，已就位）
- 2号位《ResourceCalendar人工能力槽及EXPEDITE_落码完成回执》（S5 EXPEDITE 前置竞争层，2026-09-23）
- 3号位 v4.0 任务 T3（本单履约）

---

## 〇、速览

```
变更对象    PriorityMode 语义补登契约文档（ScopeJsonV2.PriorityMode 二值轴 NORMAL/EXPEDITE）
不改签名    红线 #5：不新增 EXPEDITE_WEIGHT/SCORE/PriorityFactor（0号位 七）
运行时落点   2号位 已落地（S5 前置竞争层）；本单 = 语义契约登记，3号位 无代码动作
业务含义    EXPEDITE = 用户指定需求订单进入加急优先模式（白天 Candidate 重排中重竞争资源）
```

---

## 一、PriorityMode 契约定义（登记固化）

`ScopeJsonV2.PriorityMode`（`LPS.APS.Core/Enum/PriorityMode.cs`）二值轴：

| 值 | 语义 | 适用 |
|---|---|---|
| `NORMAL` | 常规优先级（默认）| GANTT / EQUIPMENT / RESOURCE_CALENDAR / NEW_ORDER_CTP（固定此值）|
| `EXPEDITE` | 加急优先：该需求订单进入加急优先模式，白天 Candidate 重排中调整其优先级并允许与可调整需求重新竞争资源 | NEW_ORDER_IMPACT（固定）；NEW_ORDER_INSERT / EXISTING_ORDER_ADVANCE（可选）|

> **不预设**「新订单自动等于 EXPEDITE」（0号位 一）：`PriorityMode=EXPEDITE` 仅表示本次 Candidate 计算中该需求进入加急模式，**非**默认。

## 二、调整对象与业务链路（登记固化，0号位 二）

- **调整对象 = 需求订单（Demand），不是 Task；不采用「直接移动某几个 Task」作为插单逻辑**。
- 业务链路：

```
需求订单优先级调整
  ↓ 重新进行 Demand 排序
  ↓ 重新 Pegging 供需承接
  ↓ 重新生成 Task
  ↓ 重新有限产能排程
```

- 与 NORMAL CTP 区别（0号位 三）：CTP=按正常逻辑评估最早可承诺；EXPEDITE=允许调整当前可调整计划重新寻找满足方案。EXPEDITE **不是**「排在 CTP 新增订单前面」，而是「允许进入当前计划重新优化范围」。

## 三、不可插入订单 / 保护边界（登记固化，0号位 四/五）

**不可插入 ≠ 不参与 Candidate 计算**：受保护订单（已形成执行事实 / 已锁定计划 / 业务明确不可调整）仍作为输入存在，但 **EXPEDITE 不能强行改变其保护优先级或执行约束**。

| 保护边界（三类，不可突破）| 内容 |
|---|---|
| ① 执行事实约束 | 已完工 / 已开工 / 已发生领料等执行证据 → 排程不可被 EXPEDITE 重新安排 |
| ② 业务保护约束 | Firm/Frozen 计划、已确认不可调整需求 → EXPEDITE 不能强行抢占保护位置；注意「不是不允许其他订单延期，而是不能无视既有业务承诺」|
| ③ 技术硬约束 | 物料 AvailableTime / Resource Calendar 资源可用 / Routing 工艺约束 / 有限产能约束 → 必须满足 |

## 四、V1 损失控制（登记固化，0号位 六）

- V1 **不建设自动损失优化模型**：不增加延期成本 / 订单保护权重 / 损失评分。
- 白天重排须**输出变化与影响范围**（EXPEDITE 订单 + 影响订单 + 原/新计划日期 + 影响原因如 EXPEDITE 资源竞争），由业务人员判断是否接受。

## 五、3号位 实现要求（登记固化，0号位 七）

**无需新增** `EXPEDITE_WEIGHT` / `EXPEDITE_SCORE` / `PriorityFactor`；只需明确 `PriorityMode = NORMAL | EXPEDITE` 并定义作用范围 / 与 Demand 排序关系 / 不突破的保护边界——本单即登记以上 §一~§四，履约完毕。

## 六、与既有落码对齐（2号位 S5）

2号位 已按终版口径落地「EXPEDITE 前置竞争层」（Demand 层）：命中集合作独立桶整体前置，桶内仍走 3号位 冻结 `DemandPriorityConfig` 排序，非恒先 / 不新增 Task 优先级 / 不突破硬约束。与本单登记一致。

## 七、一句话冻结（0号位 八，转载登记）

> EXPEDITE 表示用户指定需求订单进入加急优先模式。在白天 Candidate 重排中，系统调整需求订单优先级，允许 EXPEDITE 需求与当前计划中的可调整需求重新竞争资源，并在新的需求排序基础上重新执行 Pegging 和排程。EXPEDITE 不能突破已形成执行事实、业务保护约束以及物料、资源、工艺等硬约束。V1 不自动计算牺牲成本，仅输出影响范围供业务确认。

---

## 八、回执表

| 问 | 1号位 | 2号位 | 0号位 |
|---|---|---|---|
| §一~§七 EXPEDITE 契约登记认可 | （待回执）| （待回执）| 裁决已给 |
| 落点：05 契约 §7.0 注释补充登记 | （待确认）| — | （待批准）|

*回执确认后：本登记件可选并入 05 契约（补注释，不改签名）；**1号位 确认零特判（PriorityMode 在 Scheduling 层零命中，代码实证）——竞争语义由 Demand 层落地，非 Solver 侧实现**（依《正式裁决知会》§四 口径，1号位 回执 §二 更正）。*

---

**发送人**：3号位 ｜ **日期**：2026-09-23