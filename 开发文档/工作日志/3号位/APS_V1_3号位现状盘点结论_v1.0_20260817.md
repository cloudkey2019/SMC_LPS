# APS V1 3号位现状盘点结论（代码开发前置准备·第 0 步产出）

**版本**：v1.0
**日期**：2026-08-17
**性质**：《APS_V1_3号位代码开发前置准备清单_v1.0_20260817.md》第 0 步（现状盘点）产出，供阶段 A 开发与 2号位联调使用
**盘点范围**：
- 代码：LPS.APS.Engine / Core / Application / Web（7 项目结构齐全）
- DDL：`开发文档/2026-08-17文档更新/APS_数据库表结构设计_v5.1.2_冻结对齐版.sql`（权威三级）

---

# 一、结论总览

| 项 | 现状 | 结论 |
|---|---|---|
| 六张治理表 DDL | 已落地（v5.1.2 冻结对齐版 3.10 章节，8731-8866 行） | ✅ 数据库侧就绪 |
| 六表代码侧（实体/仓储/服务/控制器） | **均不存在** | ❌ 从零新建 |
| ScheduleRun 外壳 | `Engine/Services/Sync/ScheduleRunService.cs` 已存在（2号位职责） | ✅ 复用外壳 |
| ExpectedDomainKeysJson | DDL 已含独立字段（ISJSON CHECK），**代码未使用** | ⚠️ 阶段 F 3号位实现 |
| 治理 Controller（Web） | 不存在 | ❌ 从零新建 |

---

# 二、复用 / 从零清单

## 从零新建（3号位核心开发）

| 项 | 说明 |
|---|---|
| 六表实体 | RuleSet / RuleSetVersion / ParameterSet / ParameterSetVersion / StrategyProfile / StrategyProfileVersion（Core 或 Engine 按现有分层定） |
| 治理仓储 | Dapper（APS_Production）；Repositories.APS 命名空间下新增，Scrutor 自动扫描注册 |
| 治理服务 | 版本 CRUD、发布（DRAFT→SUBMITTED→APPROVED→PUBLISHED）、默认版本管理、Diff、追溯、发布前校验 |
| 治理 Controller | Web 层为 4号位提供列表/版本查看/发布/停用/Diff/当前 PUBLISHED/追溯接口 |
| Snapshot Provider | `GetFrozenStrategySnapshotAsync(strategyProfileVersionId)`（3号位提供，2号位一次调用） |
| ExpectedDomainKeysJson 读写 | ScheduleRun 生命周期冻结/校验（阶段 F） |
| xUnit 测试工程 | 当前无测试项目，需新建 |

## 复用 / 不重复造（现有代码资产）

| 项 | 位置 | 用途 |
|---|---|---|
| `RuleConfig` / `SchedulingParamConfig` / `StrategyConfig` | `Core/Models/Scheduling/SchedulingContext.cs` | Snapshot 消费侧参考（RuleSetVersion→RuleSet、ParameterSetVersion→ParameterSet 加载结构） |
| `ScheduleRunService` + `ScheduleRunDto(Id, DataCutoffTime, StrategyProfileVersionId)` | `Engine/Services/Sync` | 2号位外壳，3号位不重写；Run 绑定 StrategyProfileVersionId 已实现 |
| `DatabaseConnectionManager` + `DatabaseId` | `Engine/Data` | Dapper 统一访问入口（DB=APS） |
| 2号位现有 Dapper 仓储模式 | `Engine/Repositories/Pegging` | 新仓储实现风格参照 |

---

# 三、关键差异与需对齐点（实现前必须明确）

## 1. 版本状态：冻结 DDL 为 6 态，非实施包 3 态

```sql
-- v5.1.2 冻结对齐版
CHECK (Status IN ('DRAFT','SUBMITTED','APPROVED','PUBLISHED','DISABLED','ARCHIVED'))
```

- 实施包：DRAFT → PUBLISHED → RETIRED
- **冻结 DDL：DRAFT / SUBMITTED / APPROVED / PUBLISHED / DISABLED / ARCHIVED**
- 处理：**按 DDL 6 态实现**（印证清单附二约束 1"不为命名重新建表"）；实施包"RETIRED 不能再被新 Run 选中"语义由 PUBLISHED→DISABLED/ARCHIVED 承载

## 2. StrategyProfile.RunType：DDL 单值枚举

```sql
CHECK (RunType IS NULL OR RunType IN ('FULL_SCHEDULE','MANUAL_RESCHEDULE','LOCAL_RESCHEDULE','SIMULATION','INSERT_ORDER_WHATIF'))
```

- 实施包十九为 RunType/Purpose 组合（如 INSERT_ORDER_WHATIF+CTP）
- DDL 为单值 RunType；2号位已用 `RunType='FULL_SCHEDULE'`
- 处理：**按 DDL 枚举实现**；组合语义（CTP/Impact 不得激活等）落 Run 校验，不新增列

## 3. 版本绑定关键语义：PUBLISHED + IsDefault

2号位现有 SQL（`ScheduleRunService.CreateScheduleRunAsync`）：

```sql
SELECT TOP 1 v.Id FROM StrategyProfileVersion v
JOIN StrategyProfile p ON p.Id = v.StrategyProfileId
WHERE v.Status = 'PUBLISHED' AND v.IsDefault = 1 AND p.RunType = 'FULL_SCHEDULE'
ORDER BY v.PublishedAt DESC
```

- **3号位默认版本管理（IsDefault=1）直接决定 2号位 Run 绑定结果**
- DDL 唯一索引：同一 StrategyProfile 下仅一个 `IsDefault=1 AND PUBLISHED`（UQ_StrategyProfileVersion_DefaultPublished）
- 处理：发布/默认版本接口必须维护该不变量；与 2号位联调红线

## 4. StrategyConfig.Mode 与 Solver Strategy 的映射

现有 `StrategyConfig`（Core）：

```csharp
public SchedulingMode Mode { get; set; } = SchedulingMode.BackwardThenForward;
// SchedulingMode: Backward / Forward / BackwardThenForward
```

冻结 E1：StrategyMode = FORWARD / BACKWARD / MIXED

- 处理：阶段 E 定义映射/扩展；保留外壳最小整改，不静默改 StrategyConfig 语义

## 5. ScheduleRun 状态权威

- ScheduleRun.Status：RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED（终态均写 CompletedAt）
- PlanVersion.Status：BUILDING / CANDIDATE / ACTIVE / ARCHIVED / FAILED（StartedAt/CompletedAt 仅历史兼容，权威运行状态归 ScheduleRun）
- 处理：与清单 38/39 条一致；阶段 F 按其实现

---

# 四、对后续步骤的影响

- **阶段 A（六表治理）**：按 v5.1.2 DDL 直接建实体/仓储/服务/Controller；状态机按 6 态；IsDefault 不变量优先实现（2号位依赖）
- **阶段 B（Snapshot）**：消费侧参考 Core 现有 RuleConfig/SchedulingParamConfig 结构；Provider 归属已裁决（3号位提供）
- **阶段 F（生命周期）**：实现 ExpectedDomainKeysJson 冻结/校验（代码中暂无，2号位未实现，3号位按 DDL 独立实现）
- **与 2号位联调**：重点对齐 StrategyProfileVersion 的 Status/IsDefault 语义与 ScheduleRun 绑定

---

# 五、第 1 步工程基线执行结果（2026-08-17 补充）

- ✅ **测试工程复用**：`LPS.APS.Tests` 已存在（xUnit 2.6.2 + Moq + FluentAssertions + coverlet + Test SDK），已加入 sln，含 v5.1.2 架构集成测试（RealSchedulingIntegrationTest），**非空壳、无需新建**
- ✅ **连接串确认**：`appsettings.json` 四库齐全（APS/ODS/Auth/Hangfire），与 Test appsettings.Test.json 一致
- ✅ **Dapper 访问确认**：六表治理走 APS 库 Dapper（`DatabaseConnectionManager` + `DatabaseId.APS`）；仅 Auth 库 EF Core
- ✅ **DI 落位决策**：实际 `DatabaseServiceExtensions.cs` 仅扫 `Repositories.Auth/Pegging` + `Services.Sync/Auth`，**无 CLAUDE.md 宣称的 `Repositories.APS` 自动扫描** —— 3号位治理仓储改用**自写扩展方法注册**（不修改 2号位框架代码）
- ⚠️ **Scheduling 阻塞（1号位）**：16:19 写入的 `FiniteCapacitySolver.cs` 引用不存在的 `LPS.APS.Scheduling.Models` 与 `ScopeConstraint`（全仓无定义），阻塞 Application/Web/Tests 编译；分项验证 Core/Engine/Shared/BusinessRules **零错误**；全量 build + 空测试 green 待 1号位补齐

**对阶段 A 开发的影响**：治理仓储落位 `Engine/Repositories/APS/`（命名空间 `LPS.APS.Engine.Repositories.APS`），注册走 3号位自写扩展方法（如 `AddGovernanceServices`），与 2号位 `DatabaseServiceExtensions.cs` 解耦；实体/服务/Controller 落位与盘点结论一致。

---

*本结论基于 2026-08-17 代码库与 v5.1.2 冻结对齐版 DDL 盘点；第 1 步（工程基线）已部分完成，全量 build 验证待 1号位补齐 Scheduling Models 后重试。*
