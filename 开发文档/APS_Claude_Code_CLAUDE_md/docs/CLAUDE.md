# CLAUDE.md — 文档维护规则

## 职责

本目录用于维护 APS 项目设计文档。

## 文档修改原则

文档修改必须遵守：

1. 不随意推翻已收敛主链。
2. 不混淆历史说明和当前口径。
3. 当前口径必须写在正文或顶部变更说明中。
4. 历史版本只用于追溯，不得作为开发依据。
5. 同一术语在所有文档中必须一致。

## 当前关键术语

- ScheduleRun：一次计算运行
- PlanVersion：运行结果版本
- RunType：运行类型
- Scenario：阶段二 what-if 场景骨架
- SimulationRun：阶段二仿真执行骨架
- ScenarioObjectiveScore：阶段二方案评分骨架
- ScheduleExplanationFact：结构化原因事实
- PlanKpiSummary：版本级 KPI 汇总
- OrderScheduleSummary：订单级摘要
- ResourceLoadSummary：资源负荷摘要

## 禁止事项

- 禁止把 ScheduleRun 写成 PlanVersion。
- 禁止把 Scenario 写成所有非正式运行的总容器。
- 禁止把阶段二骨架表写成阶段一必须实装业务逻辑。
- 禁止把 Summary 写成排程内核输入。
- 禁止让 1号位直接写数据库。
- 禁止保留旧版本“当前以 vX 为准”的残留句子。
- 禁止把旧 Windsurf 工具路径作为 Claude Code 当前执行规则。

## 文档修改后必须检查

1. 版本号是否更新
2. 相关文档引用是否更新
3. Changelog 是否准确
4. 正文是否与顶部变更说明一致
5. 是否存在旧口径残留
6. 是否影响 DDL / API / 内部契约
7. 是否需要同步更新根目录或局部 CLAUDE.md
