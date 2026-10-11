# 3号位 → 0号位：代码审核 REWORK 整改完成报告（v1.0）

> 发件：3号位（规则/参数/Auth/RBAC/Scope 治理 Owner）｜ 致：0号位（PM/仲裁）
> 日期：2026-10-10 ｜ 版本：v1.0
> 依据：0号位 对 Commit `ccb2b15` 再次专项审核（`3号位致0号位_代码审核修复完成报告_0号位再次代码审核结果_B1_20261010.md`，REWORK/BLOCKED）
> 契约件：`3号位致0号位_代码审核REWORK_接口签名与快照语义变更登记_v1.2_20261010.md`（红线 #5 契约先行，唯一口径）
> 范围：0号位 §六 优先次序 7 个确认问题全部 3号位 责任内整改；需 0号位 重新业务裁决：0 项

---

## 一、整改总览

| # | 0号位 确认问题 | 落码位置 | 反证测试 | 状态 |
|---|---|---|---|---|
| 1 | **3-NEW-P0-01** SetupRule 查询无 Scope | `ISetupRuleService` 签名增 `actorUserId`；`SetupRuleService.ListAsync` 按 Department Scope 过滤（无授权空列表 fail-closed）、`GetByIdAsync` 越权 403；`SetupRuleController` 2 处补 `GetCurrentUserId()` | `SetupRuleServiceScopeTests` +5（List 三态 / GetById 越权/Global） | ✅ |
| 2 | **3-NEW-P0-02** OPM 列表只按 MaterialId | `IRoutingOperationGovernanceService.ListOperationsAsync` 签名增 `actorUserId`；`RoutingOperationGovernanceService` SQL 部门 IN 过滤 + `ResolveAllowedDepartmentIdsAsync`（无授权空列表不查库）；`RoutingOperationController` 补参 | `RoutingOperationGovernanceServiceTests` +3（P0-02 三态）+ 既有适配 | ✅ |
| 3 | **3-NEW-P0-03** IsSnapshotEligible 缺 Min/Max 非负 | `TaskSplitRuleConfigGovernanceService.IsSnapshotEligible` 补 Min/Max<0 → false | 集成反证 `GetActiveRulesForSnapshotAsync_存量Min负值行_阻断发布` | ✅ |
| 4 | **3-NEW-P0-04** 损坏 JSON 静默空列表 | `TaskSplitRuleConfigProjector.ExtractRules`：损坏 → 抛 `SetupRuleDataRedLineException`（B-010 三态：缺失/null/空配置→空列表，损坏→显式失败） | 单测 `ExtractRules_B010三态` | ✅ |
| 5 | **3-NEW-P1-01** 显式部门参数未等值过滤 | `TaskSplitRuleConfigGovernanceService.ListAsync` 非 Global 分支补 `ProductionDepartmentId =` 等值过滤（IN∩=，COUNT 与页结果一致） | 集成反证 `ListAsync_非Global授权多部门显式传部门_仅返回该部门` | ✅ |
| 6 | **3-NEW-P1-02** Setup/OPM Controller 缺 403 映射 | `SetupRuleController.HandleError` + `RoutingOperationController.HandleError` 补 `ScopeViolationException→403` | 服务层越权抛 `ScopeViolationException`（既有 + 新增） | ✅ |
| 7 | **3-NEW-P1-03** EnsureWritableAsync 无真实写 | `AuditLogRepository.EnsureWritableAsync` 改**真实写探针**（事务内 INSERT PROBE → SaveChanges → ROLLBACK → ChangeTracker.Clear，零留痕） | 既有 P1-03 注入反证 ×2（前置预检失败 → 业务零落库） | ✅ |

## 二、关键设计说明

### 1. R02 窗口重叠校验（0号位 反证项）
- 落码：`TaskSplitRuleConfigProjector.FindOverlappingWindowIds`（**纯函数**，半开区间 `[EffectiveFrom, EffectiveTo)`、NULL 无界、严格 `<` 相切不重叠，与写路径 `EnsureUniqueEffectiveWindowAsync` SQL 口径一致）+ `GetActiveRulesForSnapshotAsync` 接线。
- 反证：**单测 ×3**（重叠列双方 Id / 相切不重叠空 / 不同键空）。
- 说明：原设计为集成反证，但 dev 环境存在存量非法行（`IsActive=1` 且 Preferred 非法，seed/历史残留），`GetActiveRulesForSnapshotAsync` 的非法行校验（CANDIDATE-P0-01）**恒先于**重叠校验触发，集成层无法隔离重叠分支 → 改为纯函数单测钉死（**行为不变**，仅实现位置归纯函数类便于零 DB 反证）。

### 2. P1-03 残余窗口（如实文档化，不改变既有口径）
- 跨库（APS_Production DML + APS_Auth 审计）无 2PC，`EnsureWritableAsync` 真实写探针 + 回滚仅缩小「预检通过但后续失败」窗口，残余并发窗口仍存在——与既有 `3号位致0号位_审计一致性契约与失败补偿说明_P1-03_20261009.md` 文档化口径一致，本轮不改变。

### 3. 红线合规
- **红线 #5 接口即契约**：签名变更全部先登记 v1.2（Setup/OPM 查询签名、403 语义、P1-01 语义修正、P0-03/R02 发布资格、P0-04 三态），HTTP 层零参数变化（`actorUserId` 服务层受信主体注入）。
- **红线 #6**：零 DDL（全部内存/既有表操作）。
- **红线 #1/#3/#4**：未触碰排程层；无 SqlBulkCopy 变更；master 查询保持返回列表。

## 三、构建与测试结果

| 项 | 结果 |
|---|---|
| `dotnet build LPS.APS.sln` | **0 错误** |
| 单测 `LPS.APS.Tests`（Unit） | **555 通过 / 3 跳过 / 0 失败**（含新增 P0-01 ×5、P0-02 ×3、P0-04 三态、R02 ×3） |
| 单测 `LPS.APS.BusinessRules.Tests` | 74 通过 / 0 失败 |
| 集成 `TaskSplitRuleConfigGovernance` | **19 通过 / 0 失败**（含新增 P0-03、P1-01 反证） |
| 全量集成 `LPS.APS.Tests.Integration` | 4 失败，**全为排程/BOM 域 pre-existing**（见下） |

### 全量集成 4 失败说明（与本轮改动零交集）
失败用例均属 1号位/2号位 排程领域，依赖真实排程数据/BOM SP/5号位 SP：

| 失败测试 | 类 | 失败点 |
|---|---|---|
| 跨版本连续性红线：328 触发 + Continuation/Free 同现 | `ContinuityRedLineIntegrationTest.cs:75` | 排程连续性判定 |
| 白天BOM实时展开：补Detail → 调5号位SP → 读结果 + 三重校验 | `BomRealtimeExpandIntegrationTest.cs:67` | BOM SP 结果断言（Assert.Equal 数据差异） |
| v5.1.2: 完整排程流程集成测试 | `RealSchedulingIntegrationTest.cs:270` | 测试数据准备 DB 执行错误 |
| 合成自洽夹具：全链排程产 Task | `SyntheticSchedulingIntegrationTest.cs:104` | 排程链断言 |

本轮 `git diff` 改动 **13 文件全在治理域**（Setup/OPM/TaskSplit/Projector/AuditLogRepository），与上述排程测试类零引用关系；失败为环境/数据依赖（pre-existing），**建议转 1号位/2号位 核验**（与「无设备小工序」等既有排程集成缺口同源）。

## 四、文件清单（本轮改动 13 + 文档 2）

| 文件 | 变更 |
|---|---|
| `LPS.APS.Core/Interfaces/ISetupRuleService.cs` | ListAsync/GetByIdAsync 增 `actorUserId`（P0-01） |
| `LPS.APS.Application/Services/SetupRuleService.cs` | 列表/详情 Scope 过滤（P0-01） |
| `LPS.APS.Application/Services/IRoutingOperationGovernanceService.cs` | ListOperationsAsync 增 `actorUserId`（P0-02） |
| `LPS.APS.Application/Services/RoutingOperationGovernanceService.cs` | 部门 IN 过滤 + ResolveAllowedDepartmentIdsAsync（P0-02） |
| `LPS.APS.Application/Services/TaskSplitRuleConfigGovernanceService.cs` | IsSnapshotEligible 非负（P0-03）+ ListAsync 等值过滤（P1-01）+ 重叠校验接线（R02） |
| `LPS.APS.Application/Models/TaskSplitRuleConfigProjector.cs` | ExtractRules 三态（P0-04）+ FindOverlappingWindowIds（R02）+ 注释 UTC→本地（R03） |
| `LPS.APS.Engine/Repositories/Auth/AuditLogRepository.cs` | EnsureWritableAsync 真实写探针（P1-03） |
| `LPS.APS.Web/Controllers/SetupRuleController.cs` | 403 映射（P1-02）+ 列表补参（P0-01） |
| `LPS.APS.Web/Controllers/RoutingOperationController.cs` | 403 映射（P1-02）+ 列表补参（P0-02） |
| `LPS.APS.Tests/Unit/SetupRuleServiceScopeTests.cs` | +5（P0-01） |
| `LPS.APS.Tests/Unit/RoutingOperationGovernanceServiceTests.cs` | +3（P0-02）+ 既有适配 |
| `LPS.APS.Tests/Unit/TaskSplitRuleConfigProjectorTests.cs` | 三态（P0-04）+ R02 ×3 |
| `LPS.APS.Tests/Integration/TaskSplitRuleConfigGovernanceServiceIntegrationTests.cs` | +2（P0-03 / P1-01） |
| `3号位致0号位_代码审核REWORK_接口签名与快照语义变更登记_v1.2_20261010.md` | 契约登记（红线 #5） |
| `3号位致0号位_代码审核REWORK整改完成报告_v1.0_20261010.md` | 本报告 |

## 五、遗留项

| 项 | 归属 | 状态 |
|---|---|---|
| SVN 提交（本轮 15 文件） | 用户（renjunchang） | 待提交 |
| 4 个排程/BOM 集成失败 | 1号位/2号位 | 非本轮范围，建议核验 |
| P1-03 残余跨库窗口 | 3号位 | 已文档化（说明件口径不变） |

**发件**：3号位 ｜ 日期：2026-10-10 ｜ 版本：v1.0
