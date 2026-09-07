# APS_V1 3号位 F-G3/G4/G5 Auth 域 RBAC 补齐计划 v1.0

- **责任号位**：3号位（自驱补齐，无需 0号位 裁决归属）
- **日期**：2026-09-03
- **主依据**：`职责裁决 v1.0` §六（3号位 完整接管认证与权限，原 2号位 认证代码整体移交）、§七（V1 权限正式范围）、§八（权限执行边界）、§九（数据库 Owner：APS_Auth、用户/角色/权限、业务范围权限、权限审计归 3号位）。
- **废止前提**：原 `提交0号位` 版本假设「Auth 域属 2号位 数据面」与 §六/§九 冲突，已作废删除。

## 已完成（F-G1/F-G2）

- F-G1：`Program.cs` 全局 `FallbackPolicy = RequireAuthenticatedUser()`；公共端点 `[AllowAnonymous]` 放行。build 0 错误。
- F-G2：JWT 已携带 `ClaimTypes.Role` 声明，`[Authorize(Roles=...)]` 机制可用。

## 待补齐（3号位 自驱）

### F-G3 功能权限后端硬校验（§7.1）— ✅ 已完成（2026-09-04）
- 已完成：登录/刷新签发注入 PermissionCode 声明（`PermissionCodes.PermissionClaimType = "permissionCode"`）；权限校验服务 `PermissionService.HasPermissionAsync`；13 权限点策略注册 + `PermissionAuthorizationHandler`（DB 权威校验）；3号位控制器接入（GovernanceController 写端点挂 `RuleMaintain`/`RulePublish`/`CandidateConfirm`/`AuditView`，ScheduleController 查询挂 `PlanView`）；`PermissionCodeRepository` 补 `Role.IsActive` 过滤；`me` 端点返回 Permissions。
- 权限点数量：§七 原文写「14 项」但实际枚举为 13 项（`PermissionCodes.All` 共 13 个），与 4号位 对齐时需确认是否漏 1 项。
- 待衔接（不越位）：5号位 控制器（Overview/OrderQuery/GovernanceQuery/DomainStatus/Explanation/PeggingTrace/SupplyFactTrace/BusinessFactIssue/DemandProtection/ManualEta）按 §八 自行消费权限上下文鉴权；域定义治理写操作暂无对应权限点，待 4号位 对齐归类。

### F-G4 业务范围权限过滤（§7.2）— ✅ 已完成（2026-09-04）
- 补齐：`DataScopeContext`（Core.Authorization，维度组合：同维并集/跨维交集/Global 全放行/空范围拒全部）+ `DataScopeTypes` 标准值；`IDataScopeService.ResolveScopeAsync(userId)` + `DataScopeService`（Engine.Services.Auth，UserDataScope ∪ RoleDataScope）；补 `UserDataScope`/`RoleDataScope` 实体 + AuthDbContext DbSet；`DataScopePolicy` 实体对齐冻结 DDL v1.1（移除 PolicyCode/PolicyName/IsActive/UpdatedAt）。
- 注入：`IScheduleQueryService` 4 方法新增 `userId` 入口，`ScheduleQueryService` 内部解析范围 → 版本列表按 Domain/Factory（DomainKey）过滤，单版本读取按 DomainKey 校验（越权返空/404）。
- 待对齐：ProductFamily/Department 维度在版本列表层面暂无落盘映射，当前按拒绝处理（安全默认），需与 2号位/5号位 对齐 DomainKey→物料/部门 映射口径后再放宽；权限点「14 vs 13」与 4号位 对齐。

### F-G5 RBAC 管理端点（§六）— ✅ 已完成（2026-09-04）
- 补齐：`IRbacManagementService` + `RbacManagementService`（Engine.Services.Auth，Dapper 直连 APS_Auth，密码 SHA256+Base64 与 AuthService 对齐）；`RbacController`（`api/rbac`，类级 `[Authorize(Policy = PermissionCodes.AuthManage)]`）；DTO 集（Core.Dto/RbacManagementDtos.cs）。
- 覆盖：用户/角色/权限/业务范围策略 增删改 + 用户角色/角色权限/用户范围/角色范围 分配（覆盖式，先清后写）；软删除（User.Status='Deleted' / Role.IsActive=0）；密码哈希不外泄（响应 DTO 无 PasswordHash）。
- 关键决策：代码与冻结 Auth DDL v1.1 的 User/Role/Permission 结构不一致（代码用 UserCode/UserName/Status/IsActive/Category，DDL 写 LoginName/DisplayName/IsEnabled/ActionType）。经确认 F-G5 以「代码侧 = 真源」实现（与登录主链互通）；DDL 文档→代码漂移单独立项对齐。
- ✅ Permission 种子已补齐（2026-09-04：`PermissionSeedService` 启动幂等播种 13 个代码侧权限码，Program.cs 启动钩子；build 0 错误）。
- 待办：权限点「14 vs 13」与 4号位 对齐。

## 执行边界（§八）

```
3号位：认证 + 角色 + 功能权限 + 业务范围
        ↓
4号位：菜单/页面/按钮展示
        ↓
3号位或5号位 API：后端再次鉴权
```

- 5号位 业务接口读取 3号位 签发/提供的身份与权限上下文，校验功能权限与 Factory/ProductFamily/Department/Domain 范围；
- 2号位 不维护权限规则，只消费已校验限定后的 Scope。

## 实施顺序建议

1. F-G3：PermissionCode 声明注入 + 权限校验服务 + 14 权限点策略注册；
2. F-G4：DataScope 上下文 + `IDataScopeService` + 查询过滤接入；
3. F-G5：RBAC 管理服务 + Controller 端点（User/Role/Permission/RolePermission/UserRole/DataScopePolicy）。

## 依赖

- 2号位 既有 `Repositories/Auth` 仓储（User/Role/Permission/GovernanceAuditLog）与 `AuthDbContext` 沿用；如需新增 Auth 表/列，由 3号位 按 §九 自行出 DDL。
- RoleCode / PermissionCode 标准值清单需与 4号位（前端按钮/菜单）对齐后冻结。
