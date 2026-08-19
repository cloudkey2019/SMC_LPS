# CLAUDE.md — 2号位稳定引擎 / 数据基础设施规则

## 职责

本目录属于 2号位，负责 APS 稳定引擎、数据加载、数据持久化和数据库相关基础设施。

允许实现：

- 数据加载器
- SqlBulkCopy 批量持久化
- Task / Pegging 落库
- ScheduleRun 状态回填
- OutputPlanVersionId 回填
- ScheduleExplanationFact 落库
- PlanKpiSummary / OrderScheduleSummary / ResourceLoadSummary 生成
- Repository / UnitOfWork / Transaction 管理

## 禁止事项

- 禁止写业务规则。
- 禁止在本目录实现排程算法。
- 禁止直接修改 1号位计算逻辑。
- 禁止直接修改 5号位规则逻辑。
- 禁止绕过 DDL / 字段说明新增数据库字段。

## ScheduleRun 规则

3号位负责创建 ScheduleRun 初始记录。

2号位只负责：

```text
接收 ScheduleRunId
注入 ScheduleContext
排程完成后回填 ScheduleRun.Status
回填 OutputPlanVersionId
持久化 PlanVersion / Task / Pegging / ExplanationFact / Summary
```

## 结果落库规则

推荐落库顺序：

```text
PlanVersion
Task / Pegging
ScheduleExplanationFact
OrderScheduleSummary
ResourceLoadSummary
PlanKpiSummary
ScheduleRun 状态回填
```

如因事务边界调整顺序，必须说明原因。

## ExplanationFact 规则

1号位只产出 ExplanationFactDraft。
2号位负责转换为 ScheduleExplanationFact 并批量落库。

禁止把 ExplanationFactDraft 直接暴露给前端。

## Summary 规则

三张 Summary 是读模型：

```text
PlanKpiSummary
OrderScheduleSummary
ResourceLoadSummary
```

它们不参与排程计算。
必须在 Task / Pegging 落库后生成。

## 数据与批量处理规则

- SqlBulkCopy 前必须在应用层完成去重、清洗、必要校验。
- 禁止依赖数据库约束来兜底大量脏数据。
- 大批量操作必须明确事务边界、批大小、超时、错误日志。
- 任何新增 Repository 字段必须与 DDL、字段说明、POCO 一致。
- ext_ 跨库包装视图统一在 APS_Production 库创建。

## 输出要求

每次修改本目录代码时，必须说明：

1. 是否涉及数据库读写
2. 是否涉及事务
3. 是否涉及 ScheduleRun 状态
4. 是否涉及 Summary 生成
5. 是否改变现有 ETL 主链
6. 是否需要 DDL / 字段说明同步
