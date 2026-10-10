# APS_V1 3号位 第一批 P0 整改应答报告

| 项 | 值 |
|---|---|
| 文档版本 | v1.0 |
| 日期 | 2026-09-08 |
| 提交人 | 3号位 |
| 提交对象 | 0号位 |
| 触发 | 0号位代码审核结论「不通过」（Commit 53f35fc）、答复（2026-09-07 F-G4 等） |
| 结论 | 第一批 P0 整改完成，请求 0号位复核锁定 |

---

## 一、整改范围与顺序

按 0号位既定顺序执行，并在「按你的推荐来」下补入 0号位 2026-09-07 答复中点名的两项 F-G4 / P0-03 遗留：

| 序 | 项 | 对应裁决 | 状态 |
|---|---|---|---|
| 1 | 5a · v1.2 Schema 冻结对齐 | R1=A′（冻结对齐版） | ✅ |
| 2 | 5b · 代码对齐（Auth 双栈） | R2=A（v1.2 冻结后重建） | ✅ |
| 3 | 5c · GovernanceAuditLog → AuditLog 收敛 | 否决第14张表 | ✅ |
| 4 | 5d · 补功能权限（治理写端点挂 Policy） | P0-02 | ✅ |
| 5 | 5e · 补 Business Scope（Candidate 确认/激活） | P0-03 | ✅ |
| 6 | F-G4 · Factory→FACTORY_{id} 退出 | 答复 §三/§九 | ✅ |
| 7 | P0-03 · DomainDefinition 治理 scope | P0-03（补全） | ✅ |

---

## 二、逐项落实

### 1. 5a/5b · Schema 与代码对齐（P0-01）

字段命名/类型统一为冻结 DDL v1.2 口径：`UserCode→LoginName`、`UserName→DisplayName`、`Status→IsEnabled(+IsDeleted)`、`LastLoginTime→LastLoginAt`、`Category→ActionType`；并补漏 `UserRepository.cs`。Auth DDL 归属 3号位（2026-09-01 职责裁决）。

### 2. 5c · GovernanceAuditLog → AuditLog 收敛（P0-04）

删除 `GovernanceAuditLog` 实体/接口/仓储，统一收敛为 3号位属有的 `AuditLog`（EF Core），治理链路全部落 `AuditLog`，治理审计 Actor 归 3号位。

### 3. 5d · 补功能权限（P0-02）

治理写端点补齐 `[Authorize(Policy=...)]` 22 处；`activate-candidate` 由 `CandidateConfirm` 修正为 `CandidateActivate`。

### 4. 5e + P0-03 · 补 Business Scope

- **Candidate 确认/激活**（`IRunLifecycleService`）：签名增加 `actorUserId`，`EnsureDomainInScopeAsync` 校验 `Domain` 维度（fail-closed）。
- **DomainDefinition 治理**（`IDomainDefinitionGovernanceService`）：`CreateAsync`/`UpdateAsync`/`SetActiveAsync` 增加 `actorUserId`，`EnsureDomainAllowsAsync` 校验 `Domain` 维度（fail-closed）；`GovernanceController` 4 个域端点从 JWT 取 `GetCurrentUserId()`。

> 语义统一：仅 `Domain` 维度门禁；DomainKey 唯一权威源 = DomainDefinition；Factory/ProductFamily 不得作为 Domain 别名放行。

### 5. F-G4 · Factory→FACTORY_{id} 退出

| 文件 | 变更 |
|---|---|
| `ScheduleQueryService.ResolveAllowedDomainKeys` | 删除 `FACTORY_{id}` 拼接；仅 `Domain` 维度无损映射，其余维度无 Domain 授权时 fail-closed |
| `ScheduleQueryService.GetGanttAsync` 资源 SQL | `'FACTORY_' + CAST(r.FactoryId...)` → `@DomainKey`（`PlanVersion.DomainKey` 真实域） |
| `GanttDataDto.DomainKey` | 注释对齐 F-G4 |

---

## 三、验证结果

| 项 | 结果 |
|---|---|
| `dotnet build` | 0 错误 / 56 警告（全解决方案） |
| 单元测试 | 178 通过（无回归） |
| 集成测试 | 通过 11 / 跳过 12 / 失败 1 |

**集成测试说明：**
- 12 skip 属 2号位 DDL 未迁移（`AuditLog` 表、`ScheduleRun.ExpectedDomainKeysJson` 列、Auth 库），转绿后自动解锁；
- 1 fail（`RealSchedulingIntegrationTest`，`Task表没有数据`）为预存根因（公司部门码↔holon 缺映射桥，2号位已回执并升级待 PM 裁决 Q1/Q2/Q3），**非本批引入**。

---

## 四、遗留与后续（按 0号位既定顺序）

| 批次 | 项 | 状态 |
|---|---|---|
| 第二批 | `/me` 返完整 Scope（P1-01） | ✅ 完成（UserInfoDto + AuthController.GetCurrentUser） |
| 第二批 | 3→2 Candidate Run 触发接线（P1-02） | ✅ 完成（RunLifecycleService 接 RunSchedulingAndFinalizeAsync + 测试） |
| 第二批 | 3→2 FAILED 恢复接线（P1-03） | ⛔ 阻塞（跨号位，见下 P1-03 协助项） |
| 第二批 | 审计 Actor 从 JWT（P1-04） | ✅ 完成（GovernanceController 全量改 GetCurrentUserCode） |
| 第二批 | 审计写入原子性（P1-05） | ✅ 完成（RbacManagementService / DomainDefinitionGovernanceService 改 fail-closed） |
| 第三批 | 权限种子 fail-fast（P1-06） | ✅ 完成（permission-baseline 健康检查 + FindMissingPermissionCodesAsync） |
| 第三批 | G4/G7/G8/Schedule 查询移交 5号位 | ✅ 已移交（《给5号位代码接管迁移清单_v1.0》已交付；S1~S4/协助项回执由 5号位 主导；3号位 旧端点 S4 收敛待 0号位 确认后执行） |
| 第三批 | 清理旧 Owner 注释（P2-02） | ✅ 完成（`Program.cs:58/61` 两条点名旧职责描述已修正；CLAUDE.md/README 层架构表属 0号位 契约，未擅改、已标注） |
| 第三批 | 跨 scope 越权测试 | ✅ 完成（新增 DomainDefinitionGovernanceServiceTests +6，覆盖 Create/Update/SetActive fail-closed） |
| 第三批 | 新 commit 锁评审 | 待做 |

> 补充修正（2026-09-08）：5c 收敛此前漏删遗留文件 `GovernanceAuditLogRepository.cs` / `IGovernanceAuditLogRepository.cs` / `GovernanceAuditLog.cs`（仍引用已移除的 `AuthDbContext.GovernanceAuditLogs`，阻断 Engine 编译），本次已实际删除并解除编译阻断。
> 测试基线：全量 195 通过 / 12 跳过（2号位 DDL 未迁移）/ 1 失败（`RealSchedulingIntegrationTest`「Task表没有数据」= 预存根因，公司部门码↔holon 缺映射桥，已升级待 PM 裁决）。

### 第二批 遗留 · P1-03 协助项（待 2号位/0号位 裁定）

`RecoverFailedRunAsync` 仅通过 `InsertForRecoveryAsync` 新建 ScheduleRun（**无 PlanVersion 壳**），故无法直接调用 `RunSchedulingAndFinalizeAsync`（需 `planVersionId` 作为 2号位执行与结果写回目标）。且 FULL_SCHEDULE 恢复可能多 Domain，而 `RunSchedulingAndFinalizeAsync` 系「白天候选专用单域 + 单次收口」，逐域调用会多次 `CompleteAsync/FailAsync` 同一 Run。恢复壳的 `VersionCategory / Status` 语义属 2号位执行词表，3号位 无既定裁决。需 2号位/0号位 裁定：恢复收口 seam（是否新增按 Run 多域执行 + 一次性收口）、恢复 PlanVersion 壳由谁建及词表取值。

请求 0号位复核第一批整改并锁定，确认后按既定顺序启动第二批。