# APS V1 主线待裁决清单汇总 · 3号位 回执（致 2号位）

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（规则/参数治理、PriorityMode 契约登记 Owner）
**接收人**：2号位（收口）
**抄送**：0号位（PM 备查）、1号位、4号位、5号位（备查）
**引用**：2号位《APS_V1_主线待裁决清单汇总_2号位汇总_致各号位_v1.0_20260923(1).md》——**C1 / C2 段**（A/B/D 段分别致 PM/1号位/5号位，3号位 不代答）
**依据**：
- 0号位《Q4-B：PriorityMode=EXPEDITE强度定义裁决》（2026-09-23 正式裁决）
- 2号位 本汇总清单 §A2/§C1（EXPEDITE 实现边界 + PriorityMode 治理落点）
- 3号位 代码核实（2026-09-23）：`SetupTransitionRuleController` 已不存在

---

## 一、C1 · PriorityMode=EXPEDITE 治理落点 —— ✅ 确认 2号位 倾向

- **认**：PriorityMode 归 3号位 **契约登记**（`ScopeJsonV2.PriorityMode` NORMAL/EXPEDITE 枚举语义），**无额外运行时治理位落地责任**。
- **依据（0号位 Q4-B 正式裁决）**：**无需新增 EXPEDITE_WEIGHT / SCORE / PriorityFactor**；EXPEDITE 以 Demand 级优先级调整表达（需求订单优先级调整 → 重新 Demand 排序 → 重新 Pegging → 重新生成 Task → 重新有限产能排程）。
- **DEMAND 级调整消费链归 2号位（Demand 层加）**，3号位 不拦、不越位。
- 3号位 唯一相关动作：**契约文档补登 PriorityMode 语义**（3号位 v4.0 任务 T3，**不改签名，红线 #5**）——**纯文档登记，不产生新端点/策略位，不挡 2号位 动码**。

## 二、C2 · 旧 Setup API 退役 —— 后端已自满足；前端切换确认属 4号位

- **后端 au 事实（2026-09-23 核实）**：`SetupTransitionRuleController` + 旧路由 `api/SetupTransitionRule` 在仓库已不存在——2号位 提议的「删旧 controller」已无对象，自动满足、无需执行。
- **当前维护端点** = `SetupRuleController`（前缀 `api/governance`，`setup-rules/exact` / `setup-rules/...` 等，Setup 契约 §11.1 #1-13）。注：前缀为 `api/governance`，**非** 2号位 汇总表所写字面的 `api/SetupRule`。
- **旧 SetupTransitionRule 承载已并入 RuleSetVersion 快照**（9-22/9-23 Setup 承载重构链路：5号位 R-1 / 1号位 P-1 / 2号位 D-1/D-2 / 4号位 F-1 均已回执）。
- **「前端是否已切」确认属 4号位**（前端切换=4号位 视野）——经 3号位 主通道核（已有 9-23 4号位 F-1 Setup承载重构确认件），正式一句确认待 4号位；确认后即可由 2号位 在执行侧收口（若涉表/DDL 按红线 #6 归 2号位）。

---

## 三、统一回执表登记（3号位 二列，供 2号位 收口）

| 编号 | 号位 | 结论 | 备注 |
|---|---|---|---|
| C1 | 3号位 | **确认** | PriorityMode 契约登记归 3号位、无运行时治理位；Q4-B 无需权重字段；DEMAND 消费链归 2号位 |
| C2 | 3号位 | **后端已自满足；前端待 4号位** | 旧 `SetupTransitionRuleController` 已不在仓库；新端点 `api/governance/setup-rules/...`；前端是否已切 需 4号位 一句确认 |

---

**发送人**：3号位 ｜ **日期**：2026-09-23