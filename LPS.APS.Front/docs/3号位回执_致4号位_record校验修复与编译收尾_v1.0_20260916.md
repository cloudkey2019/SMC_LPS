# 3号位 回执 — 致 4号位（record 校验修复 + 编译收尾）

**发送**：3号位（认证 / RBAC / Domain）
**接收**：4号位（前端）
**日期**：2026-09-16
**触发**：回复 [4号位-2026-09-16-联调回归-给3号位.md](4号位-2026-09-16-联调回归-给3号位.md) §二 P0

---

## 一、§二 P0 已修复（record attribute bug）

| 文件 | 位置 | 改动 |
|---|---|---|
| `RbacManagementDtos.cs` | L74 `CreateRoleRequest.RoleCode` | `[property: RegularExpression]` → 裸 `[RegularExpression]`（target=param:，ASP.NET 才读）|
| `RbacManagementDtos.cs` | L87 `CreatePermissionRequest.PermissionCode` | 同上 |

完全按 §三 最小改动建议执行。预期：
- `POST /api/rbac/roles` body=`{"roleCode":"aps.verify.role.xxxx"}` → 200 + newId
- `POST /api/rbac/roles` body=`{"roleCode":"verify-no-prefix"}` → 422 + `message="角色编码必须以 aps. 前缀开头"`

## 二、附带发现并收尾的编译阻塞（MaxIterations 孤儿引用）

复跑 build 时另发现一处与本次无关的残留编译错误，已一并收尾（不涉及 4号位 断言，仅保证后端可编译）：

| 文件 | 改动 |
|---|---|
| `PeggingOrchestrator.cs` L199 | 删孤儿投影 `MaxIterations = solverStrategy.MaxIterations`（`SolverStrategyBlock` 已无该字段；按 09-15 终裁 MaxIterations 归 1号位 Guardrail）|
| `SolverStrategyValidatorTests.cs` | 删 `E4_MaxIterations_非正_拒绝` 孤儿测试（验证器已不校验 MaxIterations）|

**build 结果**：`dotnet build LPS.APS.sln` → **0 错误**。

## 三、请 4号位 复跑

1. `[B]` 段：`POST /roles` + `POST /permissions` 应 16/18 → **18/18**（含 422 校验路径）。
2. 全量 `verify-integration.mjs`：此前 63 通过 / 2 失败（record）；修后应 → **65 通过 / 0 失败**。
3. `[C.D]` 28 GET 鉴权：代码已就位（上轮回执已确认），本次一并保留在工作区。

## 四、提交状态

本次 record 修复、28 GET 改动、MaxIterations 收尾均**保留在工作区，尚未推送**；提交/推送时机由 3号位 统一安排，推送后请 4号位 复跑。

## 五、[E] DemandProtection（涉及 5号位）

`[E]` 段 release 契约对齐属 5号位 数据模型（单 lock）范畴，**请 4号位 直接与 5号位 沟通**，3号位 不再经手。

---

**发送人**：3号位
**日期**：2026-09-16