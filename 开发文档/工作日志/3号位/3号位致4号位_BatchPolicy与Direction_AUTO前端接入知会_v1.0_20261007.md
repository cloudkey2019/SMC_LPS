# 3号位 → 4号位：BatchPolicy + Direction(AUTO) 前端接入知会（v1.0）

> 发件：3号位（规则参数治理 Owner）｜ 致：4号位（前端）｜ 抄：0号位、1号位、2号位、5号位
> 日期：2026-10-07 ｜ 版本：v1.0
> 依据：0号位 2026-10-07《批量正倒排（Batch Policy + Direction）裁决》= 本轮落码
> 性质：前端接入知会——后端已落码并放行，请 4号位 按 §一 / §二 对接

---

## 〇、速览

```
本轮两处前端接入：
A. strategyProfile「求解方向 Mode」补 AUTO（自动）选项 —— SolverStrategyMode += Auto（Direction）
B. 新增「执行批拆分规则」维护页接入 4 端点 —— TaskSplitRuleConfig 治理 CRUD（契约件已另发）
```

---

## 一、A · strategyProfile 求解方向补 AUTO 选项

后端 `SolverStrategy.Mode`（求解方向）现接受 4 值，`SolverStrategyValidator` 已放行 `Auto`：

| Mode | 提交值 | 语义 |
|---|---|---|
| Forward | `"FORWARD"` | 正排 |
| Backward | `"BACKWARD"` | 倒排 |
| Mixed | `"MIXED"` | 混合正/倒排 |
| **Auto（新增）** | `"AUTO"` | 排程引擎按求解上下文自决方向（**不按订单类型硬编码**） |

**前端改动**：strategyProfile 编辑页「求解方向」下拉补「自动（AUTO）」；提交 `SolverStrategy.Mode="AUTO"`。后端不再拒绝，投影为 `SchedulingDirection="AUTO"` 交 1号位 消费（详情见 05 契约 §7.0.2）。

---

## 二、B · 新增「执行批拆分规则」维护页（TaskSplitRuleConfig 4 端点）

契约件：**《3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md》**（已另发，含端点/请求体/校验矩阵）。

| 项 | 说明 |
|---|---|
| 前缀/基路径 | `/api/governance/task-split-rule-configs` |
| 端点 | GET（列表，可过滤 materialId/productionDepartmentId/isActive）/ POST（新增）/ PUT `{id}`（整对象替换）/ DELETE `{id}`（软删 IsActive=0） |
| 鉴权 | 查看 `aps.rule.view`；维护 `aps.rule.edit`（复用 Rule 码） |
| 请求体 | 13 字段（MaterialId 必填 + ProductionDepartmentId + 10 参数 + EffectiveFrom/To） |

---

## 三、前端对接提示

- 执行批拆分规则维护页数据源即 §二 4 端点，**无需 mock**（表按 dev 库实际行显示）。
- **兼容字段不暴露**（ResourceGroupId / MinimumOrderQuantity / EconomicOrderQuantity 仅历史兼容）：维护页只呈现契约件 §2.2 的 13 字段。
- 后端现返全量（未分页），排序/分页由前端决定是否需要。

---

**发件**：3号位 ｜ **日期**：2026-10-07 ｜ **版本**：v1.0