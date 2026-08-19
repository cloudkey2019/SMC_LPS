# CLAUDE.md — 3号位调度编排 / API 规则

## 职责

本目录属于 3号位，负责调度编排、Hangfire、API、运行入口和版本激活。

允许实现：

- ScheduleRun API
- ScheduleRun 初始记录创建
- Hangfire 定时触发
- 调用 2号位排程执行服务
- PlanVersion 激活 API
- Summary 查询 API
- 权限校验与审计调用

## 禁止事项

- 禁止写排程算法。
- 禁止写业务规则。
- 禁止直接处理 BOM 展开、库存扣减、Routing 推导。
- 禁止直接修改 Task 排程结果。
- 禁止绕过 2号位直接落 Task / Pegging。

## ScheduleRun API

当前新入口：

```text
POST /api/scheduling/runs
GET  /api/scheduling/runs
```

旧入口：

```text
POST /api/v1/planning/schedule/full
```

仅保留兼容，必须内部转调新入口。

## 创建 ScheduleRun 的规则

3号位负责创建 ScheduleRun 初始记录：

```text
RunType
Status = RUNNING
ScenarioId 可空
TriggeredBy
StartedAt
CreatedAt
```

创建后将 ScheduleRunId 传给 2号位。

## PlanVersion 激活规则

激活接口：

```text
POST /api/scheduling/plan-versions/{id}/activate
```

只能激活 CANDIDATE 版本。

FULL_SCHEDULE 产出的正式版本默认自动激活，禁止重复激活。

SIMULATION / INSERT_ORDER_WHATIF / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE 产出的版本默认 CANDIDATE，禁止自动激活。

## Summary 查询 API

以下 API 只读：

```text
GET /api/scheduling/plan-versions/{id}/kpi
GET /api/scheduling/plan-versions/{id}/order-summary
GET /api/scheduling/plan-versions/{id}/resource-load
```

禁止在查询 API 中触发排程计算。

## 权限、审计与长任务规则

- 所有业务 API 必须做权限校验。
- 涉及工厂、产品族、资源组织维度、生产部门的数据必须做数据范围校验。
- 发起排程、激活版本、审批操作、高风险调整必须记录审计日志。
- 高风险动作必须走审批或显式治理流程。
- 长任务必须异步执行，禁止阻塞 API 线程。
- 全局异常必须统一拦截，禁止向前端暴露数据库堆栈或底层异常。

## 输出要求

每次修改本目录代码时，必须说明：

1. 是否新增或修改 API
2. 是否影响 ScheduleRun 创建流程
3. 是否影响 PlanVersion 激活规则
4. 是否调用 2号位服务
5. 是否涉及权限、审计、数据范围
6. 是否需要前端同步修改
