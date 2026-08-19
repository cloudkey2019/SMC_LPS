# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
> 本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指导。

---

## Project Overview / 项目概述

LPS.APS is an enterprise Advanced Planning & Scheduling system for manufacturing — finite-capacity scheduling, BOM explosion, ERP order sync, and resource optimization. Built on .NET 8.0 / C# 11 with Domain-Driven Design. UI language and all comments/logs are in Chinese (zh-CN). Version control is SVN (not git).

> LPS.APS 是一套面向制造业的企业级高级计划与排程（APS）系统——包含有限产能排程、BOM 展开、ERP 订单同步及资源优化。基于 .NET 8.0 / C# 11 构建，采用领域驱动设计（DDD）。界面语言及所有注释/日志均为中文（zh-CN）。版本控制使用 SVN（非 git）。

## Build & Run Commands / 构建与运行命令

```bash
dotnet restore LPS.APS.sln
dotnet build LPS.APS.sln
cd LPS.APS.Web && dotnet run    # http://localhost:5163
```

- Swagger UI: `/swagger`
- Health check: `/health` (checks `database-aps`, `database-ods`, `database-auth`)
> 健康检查端点（检查 `database-aps`、`database-ods`、`database-auth`）
- Hangfire dashboard: `/hangfire`
> Hangfire 仪表盘
- No test projects exist yet (xUnit planned but not scaffolded)
> 暂无测试项目（计划使用 xUnit，尚未搭建）

**Prerequisites:** .NET 8.0 SDK, SQL Server 2019+ with databases `APS_Production`, `MES_Integration`, `APS_Auth`, `APS_Hangfire`. Configure connection strings in `LPS.APS.Web/appsettings.json`.

> **前置条件：** .NET 8.0 SDK、SQL Server 2019+，需创建数据库 `APS_Production`、`MES_Integration`、`APS_Auth`、`APS_Hangfire`。在 `LPS.APS.Web/appsettings.json` 中配置连接字符串。

## Solution Structure (7 projects) / 解决方案结构（7 个项目）

| Project / 项目 | Role / 职责 | Key Constraint / 关键约束 |
|---|---|---|
| `LPS.APS.Web` | ASP.NET Core host, controllers, Hangfire / 宿主、控制器、后台任务 | No business logic / 禁止业务逻辑 |
| `LPS.APS.Application` | Use-case orchestration / 用例编排 | No computation logic / 禁止计算逻辑 |
| `LPS.APS.Core` | Domain entities, interfaces / 领域实体、接口 | No I/O at all / 严禁任何 I/O 操作 |
| `LPS.APS.Scheduling` | In-memory scheduling algorithms / 内存排程算法 | **Zero DB dependencies — pure computation / 零数据库依赖——纯计算** |
| `LPS.APS.Engine` | Data access (Dapper + EF Core) / 数据访问 | No business rules / 禁止业务规则 |
| `LPS.APS.BusinessRules` | Pegging, LotSizing, priority rules (skeleton) / 业务规则（骨架） | Only rule plugins / 仅限规则插件 |
| `LPS.APS.Shared` | Cross-cutting models, config, DI helpers / 跨层模型、配置、DI 辅助 | Generic abstractions only / 仅提供通用抽象 |

Dependency flow is strictly unidirectional: `Web → Application → Core ← Engine/Scheduling/BusinessRules → Shared`.

> 依赖方向严格单向：`Web → Application → Core ← Engine/Scheduling/BusinessRules → Shared`，无循环引用。

## Three-Database Architecture / 三库架构

- **APS_Production** — scheduling results, master data, orders, tasks, pegging (Dapper)
> 排程计算结果、主数据、订单、任务、Pegging（使用 Dapper）
- **MES_Integration (ODS)** — BOM explosion stored procedures, ERP/MES contract views (Dapper)
> BOM 展开存储过程、ERP/MES 契约视图（使用 Dapper）
- **APS_Auth** — RBAC, approval workflows, audit logs (EF Core with `AuthDbContext`)
> RBAC 权限、审批流、审计日志（使用 EF Core + `AuthDbContext`）
- **APS_Hangfire** — background job storage
> 后台任务存储

Database operations go through `DatabaseConnectionManager` which dispatches to the correct DB via `DatabaseId` enum. Auth repositories use EF Core; everything else uses Dapper + SqlBulkCopy.

> 数据库操作通过 `DatabaseConnectionManager` 统一管理，依据 `DatabaseId` 枚举分发到目标库。Auth 仓储使用 EF Core；其余均使用 Dapper + SqlBulkCopy。

## Architecture Red Lines (mandatory) / 架构红线（必须遵守）

1. **Scheduling layer must have zero DB package references** — enforced at project level, no Dapper/EF/SQL packages
> **排程算法层严禁引用任何数据库包**——项目级强制，不含 Dapper/EF/SQL 相关包
2. **No LINQ in Scheduling hot paths** — use `struct`, `ref`, and raw arrays for performance
> **排程热点路径禁用 LINQ**——使用 `struct`、`ref` 和原始数组以保证性能
3. **SqlBulkCopy must deduplicate first** (`.Distinct()`) — never rely on DB constraints for data integrity
> **SqlBulkCopy 前必须去重**（`.Distinct()`）——禁止依赖数据库约束来保证数据完整性
4. **Material mapping APIs return lists, not single objects** — never `.First()` blindly, use priority lookup
> **物料映射 API 返回列表而非单对象**——禁止盲目 `.First()`，须结合优先级判定选择
5. **Interfaces are contracts** — never modify signatures without updating contract docs first
> **接口即契约**——未经更新契约文档，禁止修改接口签名
6. **DB schema changes are 2号位 exclusive** — all others submit change requests
> **数据库结构变更由 2 号位专属执行**——其他号位须提交变更申请

## Developer Role System ("号位") / 开发者角色体系（"号位"）

Roles define strict code ownership boundaries. Never modify code owned by another role:

> 各角色定义了严格的代码归属边界，禁止修改其他号位负责的代码：

- **1号位** — Scheduling algorithms only (pure computation, zero I/O) / 仅排程算法（纯计算、零 I/O）
- **2号位** — Data engine/framework only (no business logic) / 仅数据引擎/框架（不含业务逻辑）
- **3号位** — Application orchestration only (no computation logic) / 仅应用编排（不含计算逻辑）
- **4号位** — Frontend only (Vue 3 + TypeScript, not yet built) / 仅前端（Vue 3 + TypeScript，尚未启动）
- **5号位** — Business rules only (Pegging, LotSizing, Priority) / 仅业务规则（Pegging、LotSizing、Priority）

## Key Patterns / 关键模式

**DI Registration / DI 注册：** Each project has `Extensions/AddXxxServices()` extension methods. Engine layer uses Scrutor for namespace-based auto-scanning. Adding a new repository or service in the right namespace requires zero DI configuration.

> 每个项目在 `Extensions/` 目录下提供 `AddXxxServices()` 扩展方法。Engine 层使用 Scrutor 按命名空间自动扫描注册，在正确命名空间下新增仓储或服务无需手动配置 DI。

Auto-scanned namespaces / 自动扫描的命名空间：
- `LPS.APS.Engine.Repositories.APS` and `.Auth` → Scoped
- `LPS.APS.Engine.Services.Sync` → Scoped
- `LPS.APS.Application.Services` → Scoped

**Repository pattern / 仓储模式：** `BaseRepository` provides retry logic (transient SQL errors only). APS repos use Dapper; Auth repos use EF Core with `AuthDbContext`.

> `BaseRepository` 提供重试机制（仅针对瞬态 SQL 错误）。APS 仓储使用 Dapper；Auth 仓储使用 EF Core + `AuthDbContext`。

**Configuration / 配置：** Strongly-typed options in `Shared/Configuration/` with `IValidateOptions<T>` validators, bound from `appsettings.json` sections.

> `Shared/Configuration/` 中定义强类型选项类，配合 `IValidateOptions<T>` 验证器，从 `appsettings.json` 配置节绑定。

**Hangfire jobs / Hangfire 定时任务：** Registered in `LPS.APS.Web/Extensions/HangfireServiceExtensions.cs`. Development uses "never fire" cron (`0 0 31 2 *`); trigger manually via Hangfire dashboard.

> 在 `LPS.APS.Web/Extensions/HangfireServiceExtensions.cs` 中集中注册。开发环境使用"永不触发"的 cron 表达式（`0 0 31 2 *`），通过 Hangfire 仪表盘手动触发。

## Coding Conventions / 编码规范

- Nullable reference types enabled everywhere / 所有项目启用可空引用类型
- XML doc comments on all public members (in Chinese) / 所有公共成员须有 XML 文档注释（中文）
- SQL table/column names: PascalCase. Index naming: `IX_<Table>_<Column>` / SQL 表名/字段名使用 PascalCase，索引命名 `IX_<表名>_<字段名>`
- Commit format: `<type>(<scope>): <subject>` (types: feat, fix, docs, style, refactor, test, chore) / 提交格式：`<类型>(<范围>): <主题>`（类型：feat、fix、docs、style、refactor、test、chore）

## Design Documents / 设计文档

Key docs in `.windsurf/docs/`（`.windsurf/docs/` 下的关键文档）：
- `APS_数据架构与防腐层设计方案_v5.0.md` — three-layer architecture & data pipeline / 三层物理架构与数据管道
- `APS 核心排产全流程走查 (完整版).md` — 30 core flows, 6-stage scheduling walkthrough / 30 个核心流程、6 阶段排程走查
- `Lean APS - 研发职责与执行任务包 (2).md` — role responsibilities & red lines / 各号位职责与红线
- `APS_应用层API接口规范_v2.0.md` — API contract definitions / API 接口契约定义
- `APS_数据库表结构设计_v5.0.sql` — APS/ODS DDL / APS 与 ODS 库建表脚本
- `APS_Auth数据库DDL_v1.0.sql` — Auth DDL / Auth 库建表脚本
- `Auth库EF_Core使用指南.md` — EF Core usage guide for Auth DB / Auth 库 EF Core 使用指南
