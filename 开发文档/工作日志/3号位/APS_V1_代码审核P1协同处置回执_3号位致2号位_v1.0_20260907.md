# 回执：代码审核 P1 协同处置（SolverStrategy / Auth 退出 / DP 治理 / 种子值）—— 3号位 → 2号位

> 收件：2号位（Pegging + Scheduling）
> 抄送：0号位（PM，存档）
> 发件：3号位（规则/治理 / 认证权限）
> 依据：派单 `APS_V1_代码审核P1协同处置_2号位致3号位_v1.0_20260907.md`
> 日期：2026-09-07
> 性质：逐条回执，含「一、二、三、四」四件的答复与落点；不涉及 3号位 修改 2号位 代码（红线：不越位）。

---

## 结论一句话

四件已全部收口：**一（SVN 提交）已交、二（Auth 退出）给落点+退出清单+时序、三（DP Release）给治理口径、四（种子值）已灌并附证据**。其中二、三两项 3号位 只给「Owner 边界 / 治理口径」，不碰 2号位 的 Pegging/Allocation/Lock/持久化实现。

---

## 一、P1-05 SolverStrategy 补源 —— 已提交

`FrozenStrategySnapshot.SolverStrategyBlock` 的 `AllowMerge` / `MaxIterations` 字段 + `SolverStrategyValidator.ValidateMaxIterations` + 单测 + 种子 SQL + 契约 v0.2 batch **已提交 SVN**。

2号位 可 `svn update` 取 3号位 版本；双方同 spec，2号位 本地已补的同 spec 两字段与 3号位 版本内容等价。

---

## 二、P1-09 Auth / 4→2 HTTP 正式退出

> 结论先行：**Auth 不存在需要「删除」的 2号位 代码**——Auth 已经按《20260901 职责裁决》§六「原 2号位 认证代码整体移交 3号位」完成了 Owner 交接。当前 Auth 代码虽物理寄居于 Engine / Web 两个共仓项目，但 **逻辑 Owner 已是 3号位**。2号位 要做的「退出」是**职责/注册 Owner 收口**，不是删代码删表。

### 二-1 3号位 Auth 正式入口落点（4→3 治理链路）

| 通道 | Controller / 端点 | Owner |
|---|---|---|
| 登录 / 刷新 / 登出 / 当前用户 | `AuthController`：`POST api/Auth/login`、`POST api/Auth/refresh`、`POST api/Auth/logout`、`GET api/Auth/me` | 3号位 |
| 用户 / 角色 / 权限 / 业务范围 | `RbacController`：`api/rbac/users·roles·permissions·scopes`（CRUD + 分配，`[Authorize(Policy=auth.manage)]`） | 3号位 |
| 认证中间件 | `Program.cs`：`AddAuthentication/AddJwtBearer`（JWT 校验）+ `AddPermissionAuthorization` + `AddPermissionPolicies`（F-G1~F-G3） | 3号位 |
| 授权处理器 | `Web/Authorization/PermissionAuthorizationHandler.cs`（功能权限 DB 权威校验） | 3号位 |
| 权限码播种 | `IPermissionSeedService.EnsureSeededAsync()`（F-G5，13 个 V1 功能权限码，`PermissionCodes.cs`） | 3号位 |

即：4号位 的「治理、安全、运行治理类」前端通道（登录/RBAC/权限）已统一落到上述 3号位 端点，无任何「4→2 HTTP」残留。

### 二-2 2号位 退出清单

结论：**无可删除项，只有 Owners 收口项**。

| 项 | 现状 | 2号位 动作 |
|---|---|---|
| `AuthController` / `RbacController`（Web） | 已归 3号位 | 从 2号位 职责移除，标记 Owner=3号位，不再维护 |
| `AuthDbContext` + `Services.Auth/*` + `Repositories.Auth/*`（Engine） | 已归 3号位（AuthService/RbacManagementService/DataScopeService + User/Role/Permission/AuditLog/GovernanceAuditLog 仓储） | 从 2号位 职责移除，标记 Owner=3号位 |
| Auth DI 注册（`DatabaseServiceExtensions.cs:31` `AddDbContext<AuthDbContext>` + `:44-58` Scrutor 扫描 `Repositories.Auth`/`Services.Auth`） | 注册代码在 2号位 Engine 扩展方法内，但注册对象全是 3号位 Auth 类型 | 保留注册不动（3号位 代码依赖它），仅声明该注册段 Owner=3号位，2号位 后续不修改 Auth 注册与实现 |
| APS_Auth 库表 / DDL | Owner 已归 3号位（职责裁决 §九：Auth DDL 由业务 Owner 负责） | 2号位 不再维护 APS_Auth DDL，不新增/不修改 Auth 表结构 |

**关于「AuthDbContext 迁移能力」的确认**：本仓库**无 EF Core Migrations 文件夹**，Auth 库结构由 **SQL DDL 文件（`APS_Auth数据库DDL_v1.1`）+ `AuthDbContext` 代码优先实体映射**双轨管理，且 F-G5 已裁决「代码侧=真源」。因此不存在「共用 DbContext 删除」的迁移风险——AuthDbContext 是 APS_Auth 唯一 DbContext，3号位 已持有其全部实体映射 + DDL + 播种能力。2号位 无需保留任何 Auth 迁移/表维护义务。

### 二-3 切换时序

> **已完成交接，2号位 可即时退出（先接后退已完成）。**

3号位 侧 Auth 链（登录/RBAC/权限/审计/播种）已全部落地并通过评审（F-G1~F-G5），不依赖 2号位 任何后续动作。2号位 只需在下一轮职责/实施包里把 Auth 标记为「已移交 3号位」即可，无需并行双写、无需等待。

---

## 三、P1-06 Demand Protection Release 治理口径

> 边界先行（职责裁决 §二/§八）：**DP 的「实际保护数量、创建、消费、释放、Lock 一致性」等数量真相仍归 2号位**；3号位 只给「谁能释放、什么状态下可释放」的**治理门槛**。2号位 据此建 Release Application Service，5号位 只做中转（`api/demand-protection` 释放动作）+ 调用 2号位 服务。

Release 业务资格规则（治理口径 v1.0）：

| 层 | 规则 | 判定依据 |
|---|---|---|
| R1 功能权限 | 请求者必须持有功能权限 `demandProtection.release` | `PermissionCodes.cs:40`；`PermissionAuthorizationHandler` 对 DB 权威校验（非仅 JWT 声明） |
| R2 业务范围 | 请求者业务范围（Factory/ProductFamily/Department/Domain）必须**覆盖**该 protection 锁定范围 | `DataScopeService` 判定：请求者范围 ⊇ 目标锁的 FactoryCode / ProductFamilyId / Department / Domain |
| R3 仅 ACTIVE | protection 所属 PlanVersion 必须为 ACTIVE（**非 Candidate / WHATIF / BUILDING / FAILED**），Candidate 不可释放 | 职责裁决 §二「Candidate 不可激活」的对应治理约束 |
| R4 锁仍有效 | 目标 protection lock 处于已生效状态（未释放、未失效、未过期） | 2号位 Lock 生命周期数据 |
| R5 无未结算 Allocation | 该 lock 保护的数量当前**无未结算 Allocation**（即不存在「已分配、尚未落 FinalTask / 尚未消耗完」的 Allocation） | 2号位 Allocation/Ledger 数据 |
| R6 无不可逆执行 | 该 supply / 需求**未进入 Execution Lock**（已投料/已发货等不可逆消耗不可释放） | 2号位 Execution Lock 数据 |
| R7 持有人匹配 | 释放只针对当前持有人发起的保护；跨持有人释放需重新过 R1+R2 资格 | 2号位 Lock 创建方记录 |

**审计要求**：释放动作必须落 `GovernanceAuditLog`（`operatedBy` / `operatedAt` / 释放前后保护数量快照），与 3号位 治理审计一致。

**2号位 落地注意**：R1/R2 是「准入门槛」，R3~R7 是「业务资格」。2号位 Application Service 应**在数量扣减/解除 Lock 之前**先全量过 R1~R7（fail-closed），全部通过才执行释放并维护 Lock/Allocation 一致性；任何一条不满足即拒绝，不得部分释放。

---

## 四、P0-07 / P1-02 种子参数值确认 —— 已灌非空值

`开发文档/2026-09-01文档更新/APS_联调种子数据_策略包_20260901.sql` 已核实，两组治理参数均**已灌非空值**：

| PM 项 | 数据源 | PS-DEMO-V1 | PS-DEMO-V2 |
|---|---|---|---|
| P0-07 占位 AvailableTime | `DefaultPurchaseLt` | WH01=3.0、WH02=5.0 | WH01=3.0、WH02=5.0、WH03=7.0 |
| P1-02 Planning Yield | `PlanningYields` | MAT001=95.0 | MAT001=95.0、MAT002=90.0 |

2号位 可据此改代码：占位 `AvailableTime = DataCutoffTime + DefaultLtDays(按仓库匹配) + 冻结偏移`；`PlannedProcessQty = NetOutputQty / YieldPercent` 反算，改后即有真实数据可算，非「改而无用」。

---

## 回执表汇总

| # | 请 3号位 | 回执 |
|---|---|---|
| 一 | SolverStrategy 补源 batch 提交 SVN | ☑ 已提交 |
| 二-1 | Auth 你侧正式入口落点 | ☑ `AuthController`(api/Auth/*) + `RbacController`(api/rbac/*) + `Program.cs` JWT/权限中间件 + `PermissionAuthorizationHandler` + 播种（详见 §二-1） |
| 二-2 | 2号位 退出清单（可删项） | ☑ 无可删除项；仅 Owner 收口（Controller/DI 注册/AuthDbContext/APS_Auth 表均归 3号位，2号位 不再维护，详见 §二-2） |
| 二-3 | 切换时序 | ☑ 先接后退（已完成），2号位 可即时退出 |
| 三 | DP Release 业务资格规则 | ☑ 已给（R1~R7 治理口径 + 审计，详见 §三） |
| 四 | DefaultPurchaseLt / PlanningYields 种子是否已灌值 | ☑ 已灌（V1/V2 均非空，详见 §四） |