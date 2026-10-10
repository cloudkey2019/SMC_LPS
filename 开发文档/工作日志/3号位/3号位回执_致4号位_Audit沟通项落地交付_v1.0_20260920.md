# 3号位 回执 — 致 4号位（Audit 沟通项 P1 三项落地交付）

> **发送人**：3号位（RBAC / 治理后端 / 审计 Owner）
> **接收人**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-20
> **触发**：《3号位回执_致4号位_Audit页沟通项答复_v1.0_20260920.md》（答复函）后续落地交付
> **性质**：**落地交付回执**——项一（代码）/ 项二（补绑）/ 项四（种子）已落地，项三 / 项五 结论维持答复函
> **效力**：以冻结文档为准；字段级 diff 依赖项已标注待 2号位

---

## 〇、一句话

**P1 三项全部落地：项一 Login/Logout 审计写入代码已合入（构建 0 错误）；项二 `aps.audit.view` 补绑、项四 版本链种子 两个 SQL 已落库 dev。可启动联调。**

---

## 一、项一 [P1] Login / Logout 审计写入点 —— ✅ 已落地（09-20 合入）

| 层 | 文件 | 改动 |
|---|---|---|
| 接口 | `LPS.APS.Core/Interfaces/IAuthService.cs` | `LoginAsync` / `LogoutAsync` 附加可选 `clientIp` / `userAgent`（Logout 另加 `userCode`），**向后兼容**；红线 #5 契约文档 = 本回执 §一 + 答复函 §一 |
| 实现 | `LPS.APS.Engine/Services/Auth/AuthService.cs` | 注入 `IAuditLogRepository`；6 写入点经私有 `WriteAuthAuditAsync`：登录成功 / 用户不存在 / 账户已禁用 / 账户锁定中 / 密码错误（HandleFailedLogin）/ 登出 |
| 控制器 | `LPS.APS.Web/Controllers/AuthController.cs` | Login/Logout 从 `HttpContext` 读 `RemoteIpAddress` + `UserAgent` 传入 service（**AuthService 不直接依赖 HTTP**） |

**字段映射**（按答复函 §一落地）：`actionCode`=Login/Logout、`module`=Auth、`entityType/entityId`=User/userCode、`result`=Success/Failed、`clientIp`/`userAgent` 落库。

**U42 脱敏确认**：`requestData`/`responseData` 恒为 null——全程零 password/token/refreshToken 序列化；`errorMessage` 仅原因类别（用户不存在/账户已禁用/账户锁定中/密码错误），对用户仍统一返回「用户名或密码错误」（防账户枚举）。

**P1-05 fail-closed**：审计失败即抛（与 `RbacManagementService.WriteAuditAsync` 同范式）；auth 与 audit 同库（APS_Auth），链路自洽。

**4号位 配合（≈0.1 人天）**：`frontNew/src/api/aps-v1/types/audit.ts` `AUDIT_ACTION_CODES` 追加 `Login` / `Logout` 2 枚举 + 中文 label；Audit.vue 零返工。

---

## 二、项二 [P1] `aps.audit.view` 4 角色补绑 —— ✅ 已落库 dev

- **脚本**：`LPS.APS.Web/Sql/APS_Auth_audit_view_role_binding_20260920.sql`（幂等差集补齐，可重跑）
- **状态**：**已执行**（09-20）；4 角色（viewer.management / planner / admin.aps / admin.system）均持；admin.system 已有自动跳过
- **效果**：09-25 切真实模式（`VITE_USE_MOCK=false`）后 planner / admin.aps 访问 `/aps/audit` **不再 403**

---

## 三、项四 [P1] 版本链种子 —— ✅ 已落库 dev

- **脚本**：`LPS.APS.Web/Sql/APS_VersionChain_dev_seed_20260920.sql`（幂等，可重跑）
- **段 1**：`TEST-RSV-SEED-001` / `TEST-PSV-SEED-001`（DRAFT）防库重建丢失补种
- **段 2**：`CHAIN-RSV/PSV-001-ARCHIVED` / `002-PUBLISHED` / `003-DRAFT` 3 版本链
- **⚠️ 字段级 diff 差异上限**：`RuleSetVersion`/`ParameterSetVersion` 实体含 `ContentSnapshotJson` 快照列，冻结 DDL v5.1.7 无这些列（实体 vs DDL 漂移，属 P0-01/P0-02 方案 A 收口、待 2号位 DDL）。故缺口 B 演示按「**版本链状态/元数据可见**」验收；字段级 diff 需 2号位 落快照列后才有真实差异。

---

## 四、项三 / 项五 —— 结论维持答复函

- **项三 [P2] audit-logs result+total**：同意方向，排期 09-25 后（需扩 `IAuditLogRepository.QueryPagedAsync` 契约，红线 #5 先更契约文档）；落地前 4号位 维持 hasMore 现状。
- **项五 [P2] parameter 挂 rule.edit**：**a. 有意为之**（rule/parameter/strategy 三域统一聚合码，strategy-profile 写/发布同挂 RuleMaintain/RulePublish 证）；前端门控对齐 `rule.edit`，rbac.md 登记语义备注。

---

## 五、待 4号位

1. `audit.ts` `AUDIT_ACTION_CODES` 追加 `Login`/`Logout` 2 枚举（项一配合，≈0.1 人天）。
2. Setup 联调验收：`verify-integration.mjs GROUP=setup` 12 断言 + `verify-rbac.mjs` 全量（Setup 侧口径 84/84，以脚本实际运行为准）+ 前端 DTO 改 Id 回执（见《APS_V1_Setup维护API_3号位致4号位_交付回执_v1.0_20260920.md》§六）。

---

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 4号位 audit.ts 枚举 + Setup 验收 ② 项三 09-25 后排期 ③ 0号位 留档
