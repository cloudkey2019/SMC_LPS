# CLAUDE.md — 数据库开发规则

## 职责

本目录用于维护 APS 数据库脚本，包括：

- DDL
- ALTER 脚本
- 回滚脚本
- 存储过程
- 视图
- 索引
- 执行前检查
- 执行后验证

## 当前 DDL 基线

当前基线为：

```text
APS_数据库表结构设计_v5.0.sql
DDL v5.0.25
```

## Batch 3 DDL 对象

本轮新增或调整：

```text
ScheduleRun
PlanVersion.ScheduleRunId
ScheduleExplanationFact
OrderScheduleSummary
ResourceLoadSummary
PlanKpiSummary
Scenario
SimulationRun
ScenarioObjectiveScore
```

## 执行顺序

必须按以下顺序执行：

```text
3.1 ScheduleRun
3.2 PlanVersion 追列 ScheduleRunId
3.3 ScheduleExplanationFact
3.4 OrderScheduleSummary
3.5 ResourceLoadSummary
3.6 PlanKpiSummary
3.7 Scenario
3.8 SimulationRun
3.9 ScenarioObjectiveScore
```

## 每次数据库修改必须包含

1. up 脚本
2. rollback 脚本
3. 执行前检查 SQL
4. 执行后验证 SQL
5. 版本说明
6. 影响范围说明

## 视图与数据库位置规则

- 所有 ext_ 跨库包装视图必须创建在 APS_Production 库。
- 禁止在 ODS 库创建 ext_ 视图。
- ODS 契约视图字段变化必须同步字段说明和 Loader。
- 权限相关对象如独立 APS_Auth 库存在，应通过明确的跨库访问或服务接口调用，不得混入业务表。

## SQL 规范

- SQL Server 对象名使用 PascalCase。
- 字段名使用 PascalCase。
- 索引命名：`IX_<表名>_<字段名>`。
- 批量写入前，应用层必须先去重、校验、清洗。
- 不允许靠唯一约束处理大规模脏数据。

## 禁止事项

- 禁止直接修改已执行历史脚本。
- 禁止无版本号修改。
- 禁止字段说明文档没有的字段进入 DDL。
- 禁止 DDL 与 POCO 不一致。
- 禁止 DDL 与 API DTO 不一致。
- 禁止在阶段一为 Scenario / SimulationRun / ScenarioObjectiveScore 增加业务逻辑触发器。

## 验证要求

执行后必须验证：

- 表是否存在
- 字段是否存在
- 主键是否存在
- 外键是否存在
- 索引是否存在
- 默认值是否符合字段说明
- CHECK 约束是否符合字段说明
- 回滚脚本是否可执行
