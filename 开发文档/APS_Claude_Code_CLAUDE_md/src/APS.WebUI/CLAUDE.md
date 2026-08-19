# CLAUDE.md — 4号位前端规则

## 职责

本目录属于 4号位，负责 Vue 3 + TypeScript 前端。

允许实现：

- 排程运行触发页面
- ScheduleRun 历史列表
- PlanVersion 激活操作页面
- KPI 看板
- 订单级摘要列表
- 资源负荷看板
- 甘特图和计划结果展示

## 禁止事项

- 禁止直接访问数据库。
- 禁止绕过 API 调用后端能力。
- 禁止在前端实现排程逻辑。
- 禁止在前端实现业务规则。
- 禁止前端自行判断 PlanVersion 激活规则，必须调用后端 API。
- 禁止只靠前端隐藏按钮作为权限控制。

## API 使用规则

新排程触发统一调用：

```text
POST /api/scheduling/runs
```

不要调用旧接口：

```text
POST /api/v1/planning/schedule/full
```

读模型查询调用：

```text
GET /api/scheduling/plan-versions/{id}/kpi
GET /api/scheduling/plan-versions/{id}/order-summary
GET /api/scheduling/plan-versions/{id}/resource-load
```

PlanVersion 激活调用：

```text
POST /api/scheduling/plan-versions/{id}/activate
```

## 页面规则

- Summary 页面只读。
- CANDIDATE 版本必须明确标识。
- ACTIVE 版本必须明确标识。
- 仿真相关页面阶段一不实现，只允许预留菜单或隐藏入口。
- Scenario / SimulationRun 阶段一不做业务页面。
- 权限不足时显示无权限状态，不自行拼接后端未授权数据。
- 高风险操作必须显示确认提示，并调用后端审批/激活 API。

## 输出要求

每次修改本目录代码时，必须说明：

1. 新增或修改了哪些页面
2. 调用了哪些 API
3. 是否涉及 ACTIVE / CANDIDATE 状态显示
4. 是否涉及权限显示或高风险操作
5. 是否需要后端配合
