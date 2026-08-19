# CLAUDE.md — Lean APS 项目共同开发规则

## 1. 项目背景

本项目为 Lean APS V1.0，自研高级计划排程系统，用于支持制造现场的订单、BOM、工艺、库存、资源、有限产能排程、计划版本管理，以及后续仿真与 AI Agent / Skill API 扩展。

当前阶段定位：

- 阶段一：规则与精益化，先把 APS 主链做实。
- 阶段二：仿真、多方案比较、元启发式算法。
- 阶段三：AI Agent / Skill API，自然语言调用 APS 能力。

本文件是 Claude Code 在本仓库工作的项目级约束。所有局部 CLAUDE.md 不得违反本文件。

## 2. 当前开发基线

当前以以下文档为开发基线：

- 总表：v3.17
- 数据架构与防腐层：v1.20
- 数据库字段说明：v5.0.25
- DDL：v5.0.25
- 应用层 API：v2.4
- 内部核心域契约：v2.11
- 开发准备清单：v2.3

如文档之间存在冲突，优先级如下：

1. DDL / 数据库字段说明
2. 内部核心域契约
3. 应用层 API
4. 数据架构与防腐层
5. 总表
6. 开发准备清单

如发现冲突，不要自行猜测，不要擅自补字段，应先输出冲突说明。

## 3. 技术栈

- 后端：C# + .NET 8.0
- 数据库：SQL Server 2019
- 前端：Vue 3 + TypeScript
- API：ASP.NET Core Web API + RESTful + Swagger + JWT
- 版本管理：SVN

代码规范：

- C# 遵循 Microsoft C# 编码规范；热点路径避免 LINQ 和隐式分配。
- TypeScript 使用 Vue 3 Composition API，优先 `<script setup>`。
- SQL Server 对象命名使用 PascalCase；索引命名使用 `IX_<表名>_<字段名>`。
- 单元测试优先 xUnit；关键流程必须有集成测试。

## 4. 号位职责边界

### 1号位：排程计算域

- 只做内存排程计算。
- 禁止数据库、文件、网络 I/O。
- 禁止直接访问 ODS、ERP、MES。
- 禁止直接写数据库。
- 可以产出 ExplanationFactDraft，但不得落库。

### 2号位：稳定引擎 / 数据基础设施

- 负责数据库结构、数据加载、批量持久化。
- 负责 Task / Pegging / ScheduleExplanationFact / Summary 落库。
- 负责 ScheduleRun 状态回填和 OutputPlanVersionId 回填。
- 不写业务规则，不写排程算法。

### 3号位：调度编排 / API

- 负责 Hangfire、API、ScheduleRun 初始创建、PlanVersion 激活。
- 不写排程算法。
- 不写业务规则。
- 不直接处理 BOM、库存、Routing 推导逻辑。

### 4号位：前端

- 只写前端页面和 API 调用。
- 不直接访问数据库。
- 不绕过 API 调用后端能力。
- 不在前端实现排程逻辑或业务规则。

### 5号位：业务规则

- 只写业务规则、策略、凭证生成。
- 不写框架代码。
- 不直接落库。
- 不修改 2号位框架代码。

## 5. Batch 3 核心口径

### 5.1 ScheduleRun 与 PlanVersion

ScheduleRun 表示“一次计算运行”。
PlanVersion 表示“这次运行产生的结果版本”。

禁止混用两者。

基本流程：

```text
触发运行
→ 3号位创建 ScheduleRun 初始记录
→ 2号位接收 ScheduleRunId
→ 2号位执行排程与结果持久化
→ 2号位回填 ScheduleRun.Status / OutputPlanVersionId
```

### 5.2 RunType

RunType 枚举包括：

```text
FULL_SCHEDULE
MANUAL_RESCHEDULE
LOCAL_RESCHEDULE
SIMULATION
INSERT_ORDER_WHATIF
```

规则：

- FULL_SCHEDULE：阶段一主链，默认自动激活。
- MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF：默认产出 CANDIDATE，禁止自动激活。

### 5.3 阶段二骨架表

以下三张表阶段一只建表和代码骨架，不实现业务逻辑：

```text
Scenario
SimulationRun
ScenarioObjectiveScore
```

禁止在阶段一实现仿真算法、方案比较业务逻辑、Agent 业务逻辑。

### 5.4 结构化原因事实

1号位可以产出：

```text
ExplanationFactDraft
```

但只能在内存中产生。

2号位负责转换并批量落库：

```text
ScheduleExplanationFact
```

禁止 1号位直接写 ScheduleExplanationFact。

### 5.5 结果读模型

以下三张表为结果读模型，不参与排程内核：

```text
PlanKpiSummary
OrderScheduleSummary
ResourceLoadSummary
```

它们由 2号位在 Task / Pegging 落库后异步生成，供页面、战报、API、未来 Skill API 查询使用。

## 6. 数据与 ETL 主链红线

以下红线从旧 Windsurf rules 中保留并转为 Claude Code 项目规则。

### 6.1 ext_ 视图建库位置

所有 ext_ 跨库包装视图必须在 APS_Production 库创建，用于 APS 库跨库访问 ODS 库契约视图。

禁止在 ODS 库创建 ext_ 视图。

### 6.2 库存事实层主键

- InventoryFact_ERP 主键使用 `MasterID + Warehouse`。
- InventoryFact_MES 主键使用 `MES_ID + Location`。
- 禁止使用 MaterialCode 作为库存事实表主键。
- MaterialCode 的统一在 InventorySupplyCandidate 层完成。

### 6.3 库存五层架构

库存数据必须按五层架构演进：

```text
Layer 1 事实层：InventoryFact_ERP / InventoryFact_MES
Layer 2 候选供给池：InventorySupplyCandidate
Layer 3 规则筛选层：ProductFamilyInventoryScope / InventorySourceRule
Layer 4 可用库存：InventoryBalance
Layer 5 内存消费层：ScheduleContext 内存库存消费
```

禁止跨层访问或简化层次。

### 6.4 MaterialCode 编码规则

格式：

```text
{类型前缀}-{物料型号}-{版本号(可选)}
```

常用前缀：

```text
RAW-
FG-
WIP-
ASSY-
```

禁止在 MaterialCode 中写入：

- MTO / MTS
- 订单号
- 客户特征
- 仓库信息
- 责任部门信息

这些信息应由订单、Pegging、MaterialSupplyContext、ProductionDepartment 等对象承载。

### 6.5 契约视图与字段变更

ERP_Master_View / MES_Material_View / MES_APS_* 契约视图字段不得私自修改。
如需新增字段，必须先更新契约文档和字段说明，并说明对 DDL、Loader、POCO、API 的影响。

## 7. 权限、审计与审批红线

### 7.1 权限校验

所有业务 API 必须进行权限校验。
高风险或涉及数据范围的接口必须同时校验功能权限和数据范围。

禁止只在前端做权限控制。
禁止只有接口层鉴权而没有数据范围校验。

### 7.2 审计日志

以下动作必须记录审计日志：

- 发起排程
- 激活 PlanVersion
- 调整任务
- 修改冻结区任务
- 插单评估
- 修改配置
- 审批操作
- 用户和权限管理

审计日志必须尽量关联 UserId、PlanVersionId、BatchNo 或 ScheduleRunId。
审计日志只增不改不删。
敏感信息不得写入 RequestData / ResponseData。

### 7.3 审批流

以下动作必须走审批或显式治理流程：

- 冻结区任务变更
- 牺牲 SO / 挤占他单能力
- 已开工任务变更
- CANDIDATE PlanVersion 激活为 ACTIVE

禁止绕过审批或激活规则直接修改正式计划。

### 7.4 数据范围

所有查询和修改操作都必须考虑数据范围，包括：

- Factory
- ProductFamily
- ResourceOrgGroup
- ProductionDepartment

数据范围校验失败应返回 403 或等价错误。

## 8. 数据库修改规则

所有数据库修改必须在 database/ 目录下完成。

每次 DDL 修改必须同时提供：

- up 脚本
- rollback 脚本
- 执行前检查 SQL
- 执行后验证 SQL
- 版本说明
- 影响范围说明

禁止直接修改已执行历史脚本。
禁止无版本号修改数据库结构。
禁止 DDL 与字段说明、POCO、API DTO 不一致。
SqlBulkCopy 前必须在应用层去重和清洗，不能依赖数据库约束“擦屁股”。

## 9. API 开发规则

新增 API 必须先检查应用层 API 文档。

当前 Batch 3 新增 API 包括：

```text
POST /api/scheduling/runs
GET  /api/scheduling/runs
POST /api/scheduling/plan-versions/{id}/activate
GET  /api/scheduling/plan-versions/{id}/kpi
GET  /api/scheduling/plan-versions/{id}/order-summary
GET  /api/scheduling/plan-versions/{id}/resource-load
```

旧接口：

```text
POST /api/v1/planning/schedule/full
```

仅保留兼容，内部必须转调 `/api/scheduling/runs`，新增开发不得继续使用旧入口。

## 10. Claude Code 工作方式

每次修改前，先输出：

1. 计划修改的文件
2. 修改目的
3. 是否影响其它号位
4. 是否涉及 DDL / API / 契约
5. 风险点
6. 验证方式

未经确认，不要大范围重构。

每次修改后，必须输出：

1. 改了什么
2. 是否符合契约
3. 如何验证
4. 是否存在遗留问题

开发原则：

- 小步快跑，一次只做一个小功能。
- 先写或同步测试，再写实现。
- 任何编译/测试失败必须在本轮内处理，不能带错提交。
- 如果需要跨号位修改，必须先输出影响分析。

## 11. SVN 与提交规则

每天开始开发前必须：

```bash
svn update
```

提交前必须再次：

```bash
svn update
dotnet build
dotnet test
```

前端相关提交还需执行对应前端构建或测试命令。

提交信息建议格式：

```text
[号位] 模块: 简短描述
```

示例：

```text
[2号位] Engine: 增加 ScheduleRun 状态回填
[3号位] API: 新增排程运行查询接口
[4号位] WebUI: 增加 PlanVersion KPI 页面
```

## 12. 代码审查规则

以下情况必须审查：

- 修改其它号位负责的代码
- 涉及 DDL / API / 内部契约
- 涉及 ScheduleRun / PlanVersion / Summary / ExplanationFact 主链
- 涉及权限、审批、审计
- 涉及性能热点路径
- 新功能合并到主分支

审查时必须检查：

- 是否符合职责边界
- 是否符合 DDL / 字段说明 / POCO / API
- 是否有跨号位越权
- 是否有性能或安全风险
- 是否有测试
- 是否需要更新文档

## 13. 禁止事项

- 禁止跨号位直接修改代码。
- 禁止绕过契约直接新增字段。
- 禁止在 1号位代码中访问数据库。
- 禁止在 3号位代码中写排程算法。
- 禁止在 5号位代码中直接落库。
- 禁止把 Scenario 当成所有非正式运行的总容器。
- 禁止把 ScheduleRun 当成 PlanVersion。
- 禁止阶段一实装仿真业务逻辑。
- 禁止只修改代码不更新受影响契约。
