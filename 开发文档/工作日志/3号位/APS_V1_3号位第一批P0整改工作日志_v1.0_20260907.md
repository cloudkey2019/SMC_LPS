# APS_V1_3号位 第一批 P0 整改工作日志

| 项 | 值 |
|---|---|
| 文档版本 | v1.0 |
| 日期 | 2026-09-07 |
| 开发者 | 3号位 |
| 触发 | 0号位 代码审核结论「不通过」（Commit 53f35fc） |
| 范围 | 第一批 P0×4 + 收口（5a → 5e） |
| 状态 | 第一批收口完成（5a→5e + F-G4 Factory→FACTORY 退出 + P0-03 DomainDefinition 治理 scope） |

---

## 一、背景

0号位对 Commit 53f35fc 作出「不通过」裁决（详见 `APS_V1_3号位代码审核报告_Commit_53f35fc_20260907(1).md` 与 `答复 (2)(1).md`），P0 整改分三批执行。本日志记录第一批（P0×4）执行进度与裁决落地情况。

**第一批整改顺序（0号位 指定）：**
1. v1.2 Schema（字段命名/类型冻结对齐）
2. 代码对齐（Auth 双栈对齐冻结字段）
3. GovernanceAuditLog → AuditLog（收敛，否决「第14张表」）
4. 补功能权限（治理写端点挂 `[Authorize]` Policy）
5. 补 Business Scope（写入消费 `DataScopePolicy`）

---

## 二、已完成明细

### 5a · v1.2 Schema 冻结对齐

字段命名/类型统一为冻结 DDL v1.2 口径：

| 旧（代码漂移） | 新（冻结 DDL v1.2） |
|---|---|
| UserCode | LoginName |
| UserName | DisplayName |
| Status | IsEnabled（+ IsDeleted） |
| LastLoginTime | LastLoginAt |
| Category | ActionType |

GovernanceAuditLog 收敛字段映射：

| 旧 | 新 |
|---|---|
| OperationType | ActionCode |
| BeforeStatus | OldValue |
| AfterStatus | NewValue |
| OperatedBy | UserCode |
| OperatedAt | OccurredAt |
| Remarks | Remark |
| EntityId（long） | EntityId（NVARCHAR(100) string） |

### 5b · 代码对齐（含补漏）

- `AuthService.cs` / `RbacManagementService.cs`（Dapper 原始 SQL 硬编码字段）对齐冻结字段。
- **补漏 `UserRepository.cs`**（上一批遗漏导致 6 个构建错误）：`Status→IsDeleted(bool)`、`UserName→LoginName`。
- 全部 Dapper 查询保持参数化，符合安全红线。

### 5c · GovernanceAuditLog → AuditLog 收敛

| 操作 | 文件 |
|---|---|
| 删除 | `Core/Interfaces/IGovernanceAuditLogRepository.cs`、`Core/Entities/Auth/GovernanceAuditLog.cs`、`Engine/Repositories/Auth/GovernanceAuditLogRepository.cs` |
| 新建 | `Core/Interfaces/IAuditLogRepository.cs`（统一审计接口） |
| 重写 | `Engine/Repositories/Auth/AuditLogRepository.cs`（EF Core，单参构造 `AuthDbContext`） |
| 迁移 | `GovernanceVersionService`、`RunLifecycleService`、`DomainDefinitionGovernanceService`、`RbacManagementService`、`GovernanceController` |
| 更新 | 9 个测试文件 + `TestEnvironment.cs`（`GovernanceAuditLog`→`AuditLog`、`HasAuditLogTable`、auth 底表 DELETE 断言） |

**结果核实：** GovernanceAuditLog（0号位否决的「第14张表」）已收敛为 3号位属有的 `AuditLog`；审计 Actor 归 3号位，治理链路写审计全部落到 `AuditLog`。

---

### F-G4 · Factory→FACTORY_{id} 退出（0号位 2026-09-07 答复 §三/§九）

| 文件 | 变更 |
|---|---|
| `ScheduleQueryService.ResolveAllowedDomainKeys` | 删除 `factory.Select(f => $"FACTORY_{f}")`；仅 `Domain` 维度无损映射到 `PlanVersion.DomainKey`，Factory/ProductFamily/Department 无 Domain 授权时 fail-closed |
| `ScheduleQueryService.GetGanttAsync` 资源 SQL | `'FACTORY_' + CAST(r.FactoryId AS NVARCHAR(10))` → `@DomainKey`（= `PlanVersion.DomainKey` 真实域） |
| `GanttDataDto.DomainKey` | 文档注释改为「资源域 = 所属 PlanVersion.DomainKey，DomainDefinition 唯一权威源」 |

### P0-03 · DomainDefinition 治理 scope（补 Business Scope）

| 文件 | 变更 |
|---|---|
| `IDomainDefinitionGovernanceService` | `CreateAsync`/`UpdateAsync`/`SetActiveAsync` 签名增加 `int actorUserId` |
| `DomainDefinitionGovernanceService` | 注入 `IDataScopeService`，新增 `EnsureDomainAllowsAsync`（`scope.Allows(Domain, domainKey)` fail-closed） |
| `GovernanceController` | 4 个域端点写入传 `GetCurrentUserId()` |
| `DomainDefinitionGovernanceIntegrationTests` | Mock 默认 `Global` + 15 处调用补 `1` + 新增 `G_D17` 未授权拒绝用例 |

> 语义：仅 `Domain` 维度门禁域治理，DomainKey 唯一权威源 = DomainDefinition；Factory/ProductFamily 不得作为 Domain 别名放行。

---

## 三、进行中（5d 补功能权限）—— 权限码映射方案

> **状态：调查定案完毕，`GovernanceController.cs` 尚未写入任何 `[Authorize]`（中断于读取插入点）。**

7 个治理写端点（无显式 Policy，仅 Global Fallback「已登录」）待补挂：

| 端点 | 拟挂权限 |
|---|---|
| POST /domain-definition（新建） | `RuleMaintain`（aps.rule.edit） |
| PUT /domain-definition/{id}（编辑） | `RuleMaintain`（aps.rule.edit） |
| POST /domain-definition/{id}/enable | `RuleMaintain`（aps.rule.edit） |
| POST /domain-definition/{id}/disable | `RuleMaintain`（aps.rule.edit） |
| POST /run/{id}/validate-domain-keys | `RuleMaintain`（aps.rule.edit） |
| POST /run/candidate（建候选运行） | `PlanRun`（aps.plan.run）**← 需新增码** |
| POST /run/{id}/recover（FAILED 恢复） | `PlanRun`（aps.plan.run）**← 需新增码** |

另需**修正** 1 处现有映射：

| 端点 | 现状 | 修正为 |
|---|---|---|
| POST plan-version/{id}/activate-candidate | `CandidateConfirm` | `CandidateActivate`（aps.candidate.activate） |

**待确认点：** `aps.plan.run`（发起排程/运行）已存在于 DDL v1.2 的 34 码中，但代码侧 `PermissionCodes` / `PermissionSeedService` 仅 14 码、未含此码。若采纳，续接时需同步补 `PermissionCodes.cs` 中的 `PlanRun` 常量与 `PermissionSeedService.cs` 种子项。

> 说明：`CandidateActivate` 常量注释已预示「v1.2 拆细为独立码，供 P0-02 补功能权限时使用」，故 activate-candidate 切分是既定路线。其余端点确切权限码 审核报告未明确指定，上表为语义最贴近推断，续接前请 0号位 / 3号位拍板。

---

## 四、剩余任务清单

| # | 任务 | 批次 | 阻塞/依赖 |
|---|---|---|---|
| 1 | 5d 补功能权限（按上表落 `[Authorize]` + 补 `PlanRun` 码） | 第一批 | 权限码映射待确认 |
| 2 | 5e 补 Business Scope（D01 不可确认/激活 D02；写入消费 `DataScopePolicy`） | 第一批 | 无 |
| 3 | 第一批收口：build + 集成测试 + 应答报告 | 第一批 | 1、2 完成后 |
| 4 | 第二批 P1：`/me` 返完整 Scope、3→2 Candidate Run 触发接线、3→2 FAILED 恢复接线、Actor 从 JWT（防请求体伪造）、审计原子性 | 第二批 | 第一批通过后 |
| 5 | 第三批：G4/G7/G8/Schedule 查询移交 5号位、清理、跨 scope 越权测试、新 commit 锁评审 | 第三批 | 第二批通过后 |

---

## 五、验证状态

| 项 | 结果 |
|---|---|
| `dotnet build` | 0 错误 / 56 警告（全解决方案，F-G4 + P0-03 收口后） |
| 单元测试 | 178 通过（无回归） |
| 集成测试 | 通过 11 / 跳过 12 / 失败 1（RealScheduling 预存根因，非本批引入）；12 skip 属 2号位 DDL 未迁移（AuditLog 表 / ExpectedDomainKeysJson 列 / Auth 库） |

---

## 六、下次续接起点

1. 确认「5d 权限码映射」中的 `PlanRun` 是否新增（见第三节待确认点）。
2. 落地 5d（`GovernanceController.cs` 8 处 `[Authorize]` + `PermissionCodes.cs` / `PermissionSeedService.cs` 补码）。
3. 落地 5e（补 Business Scope）。
4. 第一批收口（build + 集成测试 + 应答报告）。