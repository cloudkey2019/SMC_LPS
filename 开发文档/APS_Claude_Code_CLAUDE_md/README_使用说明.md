# APS Claude Code CLAUDE.md 使用说明

## 1. 这套文件的定位

这套文件用于把原来 Windsurf 协作规则迁移为 Claude Code 可直接读取和遵守的项目规则。

使用原则：

1. **以 `CLAUDE.md` 为 Claude Code 的主执行规则。**
2. 旧 `.windsurf/rules.md`、`.windsurf/contracts`、`.windsurf/workflows` 仅作为历史参考，不再作为日常执行入口。
3. 根目录 `CLAUDE.md` 是全项目共同规则。
4. 各目录下的 `CLAUDE.md` 是局部规则，只约束对应模块。
5. 局部规则不得违反根目录总规则。
6. 后续如果 DDL、字段说明、API、内部契约变更，必须同步更新相关 `CLAUDE.md`。

本版已经参考旧 `.windsurf` 文件中仍然有效的内容，包括：

- 权限、审计、审批、数据范围红线
- ext_ 视图建库位置规则
- 库存五层架构规则
- InventoryFact 主键规则
- MaterialCode 编码规则
- SqlBulkCopy 前应用层去重和清洗规则
- SVN 日常开发、提交、代码审查规则
- C# / TypeScript / SQL 基础代码规范
- 单元测试与集成测试要求

未迁移内容包括：

- Windsurf 专属 `.windsurf/rules.md`、contracts、workflows 路径依赖
- Windsurf 专属提示词和执行流程
- 已过时的旧版本文档引用
- 与当前 DDL v5.0.25 / 内部契约 v2.11 / API v2.4 冲突的旧口径

---

## 2. 文件放置结构

将本压缩包解压到 APS 代码仓库根目录，保持目录结构不变：

```text
APS-Code/
├── CLAUDE.md                         ← 根目录总规则，全员共用
├── README_使用说明.md                 ← 给团队看的使用说明
├── CLAUDE_迁移说明.md                 ← 从 Windsurf 迁移到 Claude Code 的说明
├── src/
│   ├── APS.Core/
│   │   └── CLAUDE.md                 ← 1号位：排程计算域
│   ├── APS.Engine/
│   │   └── CLAUDE.md                 ← 2号位：稳定引擎 / 数据基础设施
│   ├── APS.Orchestrator/
│   │   └── CLAUDE.md                 ← 3号位：调度编排 / API
│   ├── APS.WebUI/
│   │   └── CLAUDE.md                 ← 4号位：前端
│   └── APS.BusinessRules/
│       └── CLAUDE.md                 ← 5号位：业务规则
├── database/
│   └── CLAUDE.md                     ← 数据库 / DDL / SP
└── docs/
    └── CLAUDE.md                     ← 文档维护
```

---

## 3. 哪个文件给谁用

### 3.1 `APS-Code/CLAUDE.md`

**放置位置：**

```text
APS-Code/CLAUDE.md
```

**使用对象：**

全体成员、所有 Claude Code 会话。

**作用：**

这是全项目的“总规则”或“总宪法”，包括：

- 项目背景
- 当前开发基线
- 技术栈
- 1～5号位职责边界
- Batch 3 核心口径
- `ScheduleRun / PlanVersion / RunType` 规则
- 阶段二骨架表只建表、不实装业务逻辑
- 数据库规则
- API 规则
- 开发工作方式
- 全项目禁止事项

**要求：**

必须放在仓库根目录。任何局部 `CLAUDE.md` 都不得违反这个文件。

---

### 3.2 `src/APS.Core/CLAUDE.md`

**给谁用：**

1号位，排程计算域。

**主要约束：**

- 只做内存排程计算
- 禁止数据库、文件、网络 I/O
- 禁止直接写数据库
- 可以产出 `ExplanationFactDraft`
- 不允许直接写 `ScheduleExplanationFact`
- 不创建 `ScheduleRun`
- 不回填 `ScheduleRun.Status`
- 不激活 `PlanVersion`
- 热点路径避免 LINQ 和隐式分配

**典型开发内容：**

- Task 排布
- 资源时间窗计算
- 有限产能计算
- 排程输出对象生成
- 内存原因事实草稿生成

---

### 3.3 `src/APS.Engine/CLAUDE.md`

**给谁用：**

2号位，稳定引擎、数据加载、批量持久化。

**主要约束：**

- 接收 3号位创建的 `ScheduleRunId`
- 注入 `ScheduleContext`
- 回填 `ScheduleRun.Status / OutputPlanVersionId`
- 持久化 `PlanVersion / Task / Pegging / ScheduleExplanationFact / Summary`
- 负责三张 Summary 的异步生成
- 不写业务规则
- 不写排程算法
- 不绕过 DDL / 字段说明新增数据库字段

**典型开发内容：**

- 数据加载器
- SqlBulkCopy
- Repository / UnitOfWork / Transaction 管理
- 结果落库
- `ScheduleExplanationFact` 批量持久化
- `PlanKpiSummary / OrderScheduleSummary / ResourceLoadSummary` 生成

---

### 3.4 `src/APS.Orchestrator/CLAUDE.md`

**给谁用：**

3号位，调度编排、API、Hangfire。

**主要约束：**

- 创建 `ScheduleRun` 初始记录
- 实现 `POST /api/scheduling/runs`
- 实现 `GET /api/scheduling/runs`
- 实现 PlanVersion 激活 API
- 实现 Summary 查询 API
- 调用 2号位服务执行排程
- 不写排程算法
- 不写业务规则
- 不直接处理 BOM 展开、库存扣减、Routing 推导

**典型开发内容：**

- Hangfire 定时触发
- ScheduleRun API
- PlanVersion 激活接口
- Summary 查询接口
- 权限校验与审计调用

---

### 3.5 `src/APS.WebUI/CLAUDE.md`

**给谁用：**

4号位，前端开发。

**主要约束：**

- 只调用 API
- 不直接访问数据库
- 不实现排程逻辑
- 不实现业务规则
- 使用新的 `/api/scheduling/runs`
- 页面明确区分 `ACTIVE / CANDIDATE`
- 阶段一不做仿真业务页面

**典型开发内容：**

- 排程运行触发页面
- ScheduleRun 历史列表
- PlanVersion 激活操作页面
- KPI 看板
- 订单级摘要列表
- 资源负荷看板
- 甘特图和计划结果展示

---

### 3.6 `src/APS.BusinessRules/CLAUDE.md`

**给谁用：**

5号位，业务规则引擎。

**主要约束：**

- 只写业务规则和凭证
- 不直接落库
- 不访问数据库连接
- 不写框架代码
- 不修改 1号位算法和 2号位持久化逻辑
- 可以辅助判断 `ReasonCode`
- 不自行发明 `StageCode`
- 禁止把 `ProcessType` 当 `StageCode`
- 禁止把 `OperationName` 当 `ProcessType`

**典型开发内容：**

- Pegging 规则
- 库存扣减策略
- 订单优先级规则
- 拆批规则
- 冻结区规则
- 缺料识别规则
- 原因码判断规则
- 业务凭证生成

---

### 3.7 `database/CLAUDE.md`

**给谁用：**

2号位、数据库脚本维护人员、以后让 Claude Code 修改 SQL 时使用。

**主要约束：**

- DDL 修改必须有 up 脚本
- 必须有 rollback 脚本
- 必须有执行前检查 SQL
- 必须有执行后验证 SQL
- 禁止直接改已执行历史脚本
- 禁止 DDL 与字段说明、POCO、API DTO 不一致
- 明确 Batch 3 DDL 执行顺序

**典型开发内容：**

- 表结构
- ALTER 脚本
- 回滚脚本
- 存储过程
- 视图
- 索引
- 执行检查脚本
- 验证脚本

---

### 3.8 `docs/CLAUDE.md`

**给谁用：**

文档维护人员、以后让 Claude Code 修改设计文档时使用。

**主要约束：**

- 不推翻已收敛主链
- 不混淆历史说明和当前口径
- 不保留“当前以旧版本为准”的残留句子
- 不把 `ScheduleRun` 写成 `PlanVersion`
- 不把 `Scenario` 写成所有非正式运行的总容器
- 不把阶段二骨架表写成阶段一必须实装
- 不把 Summary 写成排程内核输入
- 文档修改后必须检查版本号、引用关系、旧口径残留

---

### 3.9 `README_使用说明.md`

**给谁看：**

团队成员、项目负责人、Claude Code 使用前的说明文件。

**作用：**

说明这套文件怎么放、给谁用、怎么启动 Claude Code、旧 Windsurf 文件如何处理。

不是 Claude Code 的主要执行规则，但建议保留在仓库根目录。

---

### 3.10 `CLAUDE_迁移说明.md`

**给谁看：**

项目负责人、技术负责人、需要了解从 Windsurf 切换到 Claude Code 的成员。

**作用：**

说明：

- 哪些内容从旧 Windsurf 规则中迁移了
- 哪些内容没有迁移
- 为什么以后以 `CLAUDE.md` 为主
- 旧 `.windsurf` 目录如何处理

不是 Claude Code 必须读取的核心规则，可以放在根目录或 `docs/` 目录。

---

## 4. Claude Code 实际如何读取这些规则

Claude Code 工作时，通常会读取当前工作目录及上层目录中的 `CLAUDE.md`。

因此建议：

### 4.1 全体成员

所有人都受根目录总规则约束：

```text
APS-Code/CLAUDE.md
```

### 4.2 各号位开发

例如 1号位在：

```text
src/APS.Core/
```

目录下启动 Claude Code 时，应同时遵守：

```text
APS-Code/CLAUDE.md
src/APS.Core/CLAUDE.md
```

2号位在：

```text
src/APS.Engine/
```

目录下启动 Claude Code 时，应同时遵守：

```text
APS-Code/CLAUDE.md
src/APS.Engine/CLAUDE.md
```

3号位、4号位、5号位同理。

### 4.3 数据库修改

让 Claude Code 在：

```text
database/
```

目录下工作，并遵守：

```text
APS-Code/CLAUDE.md
database/CLAUDE.md
```

### 4.4 文档修改

让 Claude Code 在：

```text
docs/
```

目录下工作，并遵守：

```text
APS-Code/CLAUDE.md
docs/CLAUDE.md
```

---

## 5. 推荐首次使用流程

### 第一步：解压文件

把本压缩包解压到 APS 代码仓库根目录。

### 第二步：不要立即改代码

第一次只让 Claude Code 检查规则是否能覆盖当前仓库。

推荐提示词：

```text
请读取当前仓库根目录和子目录中的 CLAUDE.md，检查当前仓库结构、文档引用、代码目录是否符合规则。先只输出检查结果和风险清单，不要修改任何文件。
```

### 第三步：检查是否仍有旧 Windsurf 口径

推荐提示词：

```text
请基于 CLAUDE.md 检查本仓库是否还残留旧 Windsurf 口径，例如 .windsurf 专属路径、旧版本文档引用、旧 API 入口直连等。只输出建议清单，不要修改。
```

### 第四步：按号位启动开发

例如 2号位开发 Batch 3 DDL 执行包时：

```text
请读取根目录 CLAUDE.md 和 database/CLAUDE.md。
基于 DDL v5.0.25，为 Batch 3 生成数据库执行包：
1. up 脚本
2. rollback 脚本
3. 执行前检查 SQL
4. 执行后验证 SQL
5. 执行顺序说明
先输出计划，不要直接改文件。
```

例如 3号位开发 API 骨架时：

```text
请读取根目录 CLAUDE.md 和 src/APS.Orchestrator/CLAUDE.md。
基于 API v2.4，实现 ScheduleRun 相关 API 的 Controller / DTO / Service 骨架。
先输出计划、文件清单、接口清单和风险点，不要直接改代码。
```

---

## 6. 旧 `.windsurf` 目录如何处理

建议保留，但改为历史参考。

### 推荐做法

```text
.windsurf/
```

可以暂时保留在仓库中，但不再作为 Claude Code 的主规则来源。

如果保留，建议在 `.windsurf/README.md` 中标注：

```text
本目录为历史 Windsurf 协作资料，仅供追溯参考。
当前 Claude Code 开发规则以仓库根目录及各模块目录下的 CLAUDE.md 为准。
如内容冲突，以 CLAUDE.md 为准。
```

### 不建议做法

不建议继续让团队同时维护：

```text
.windsurf/rules.md
.windsurf/contracts
.windsurf/workflows
CLAUDE.md
```

两套规则并行会造成口径分裂。

---

## 7. 后续维护规则

当以下文档发生变化时，必须同步检查 `CLAUDE.md`：

- DDL
- 数据库字段说明
- 内部核心域契约
- 应用层 API
- 数据架构与防腐层
- 总表
- 开发准备清单

尤其是以下内容变化时，必须同步更新：

- 新增或删除表
- 新增或删除字段
- RunType 变化
- ScheduleRun / PlanVersion 关系变化
- Scenario / SimulationRun / ScenarioObjectiveScore 范围变化
- Summary 表口径变化
- 各号位职责边界变化
- API 路径变化

---

## 8. 最终原则

一句话：

```text
根目录 CLAUDE.md 是全项目总规则；
各 src/xxx/CLAUDE.md 是各号位局部规则；
database/CLAUDE.md 管 SQL；
docs/CLAUDE.md 管文档；
README 和迁移说明是给人看的说明文件。
```
