# 3号位 回执 — 致 4号位（Audit 页落地通报 5 项沟通/确认项答复）

> **发送人**：3号位（RBAC / 治理后端 / 审计 Owner）
> **接收人**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-20
> **触发**：《4号位-2026-09-20-Audit页落地与沟通项-给3号位(1).md》
> **性质**：**沟通确认函回执**（非契约变更；答复含 2 项 P1 落地承诺 + 1 项补绑 SQL + 2 项书面结论）
> **效力**：以冻结文档为准；涉及契约/DDL 变更处已标注待 0号位/2号位 裁决

---

## 〇、收悉

4号位 于 09-20 落地 `/aps/audit` 审计页（U42）、未改 lps/ 任何文件，后端契约实测确认（`GROUP=audit` 3 断言全绿 + `/me` 34 码含 `aps.audit.view`）——收悉且认可。以下 5 项逐条答复。

---

## 一、[P1] Login / Logout 审计写入点 —— **确认补，09-25 前落地**

**核实**（09-20 代码走读）：`LPS.APS.Engine\Services\Auth\AuthService.cs:48/221/342`（LoginAsync / LogoutAsync / HandleFailedLoginAsync）全链路无 AuditLog 写入点；dev 库 13 个 actionCode 实测吻合。冻结 §22.6 覆盖缺口成立，**属验收缺口，补**。

**落地设计**（字段映射采纳 4号位 建议，微调以 3号位 设计为准）：

| actionCode | 写入点 | entityType/entityId | result | 说明 |
|---|---|---|---|---|
| `Login` | LoginAsync 成功分支 | User / userCode | Success | 认证通过即记 |
| `Login` | HandleFailedLoginAsync | User / userCode | Failed | 失败也记，errorMessage=原因类别（密码错/停用/不存在/被锁），**记类别不记原文** |
| `Logout` | LogoutAsync | User / userCode | Success | 登出即记 |

- **module**=`Auth`；**clientIp / userAgent** 由 AuthController 从 HttpContext 传入 service（AuthService 不直接依赖 HTTP）。
- **U42 脱敏红线**：写入点**严禁**序列化含密 DTO；requestData 不写 password / token / refreshToken，仅留必要业务上下文。
- **复用**：`IAuditLogRepository.AddAsync`（`LPS.APS.Core\Interfaces\IAuditLogRepository.cs:12`）已具备，无新接口。
- **4号位 配合**：`frontNew/src/api/aps-v1/types/audit.ts` `AUDIT_ACTION_CODES` 追加 `Login`/`Logout` 2 枚举 + 中文 label（≈0.1 人天），Audit.vue 零返工 —— 按此口径执行。

---

## 二、[P1] dev 库 4 角色 `aps.audit.view` 绑定 —— **需补绑，SQL 见附录 A**

**核实**（seed 走读）：
- **admin.system**：`APS_Auth_v1.3_admin_bootstrap_20260910.sql` 段 B（L46-52）全 34 码含 `aps.audit.view` ✅；
- **viewer.management / planner / admin.aps**：所有 seed 脚本**无绑定** ❌ → 09-25 切真实模式（`VITE_USE_MOCK=false`）后将 403。

**补绑**：`aps.audit.view` = **4 角色均持**（采纳 `rbac.md` §1.2 audit 口径：审计为监督能力，viewer 管理层可查看）。附录 A 为幂等差集补齐 SQL（4 角色一并补，admin.system 已有则跳过）。可并入 RBAC UI 或手工执行，二选一即可。

---

## 三、[P2 可选] audit-logs 两项增强 —— **同意方向，排期 09-25 后**

1. **`result` 过滤参数**：确认可行，需扩 `IAuditLogRepository.QueryPagedAsync` 契约（`IAuditLogRepository.cs:39-46`）+ `AuditLogRepository.QueryPagedAsync`（L106-145）加 `result` 条件。**红线 #5（接口即契约）**：改签前先更新契约文档（RbacController GET /api/rbac/audit-logs 同步 + `[FromQuery] result`）。前端「Result 列展示不做筛选」现状正确，落地前维持。
2. **响应带 total**：现 `ApiResponse<IReadOnlyList<AuditLog>>` 纯数组。补 total 需信封变更（`data + total`）→ 契约确认后改。前端 hasMore 分页现状可用，落地后前端换 ElPagination 即可。

**排期**：09-25 后随 P0 项收口一并评估，不限期答复即指本结论；落地前 4号位 维持现状（页面 hasMore 注明已采纳）。

---

## 四、[P1] ParameterSet / RuleSet 版本种子覆盖度 —— **确认存在 + 补版本链，09-25 前**

1. **`TEST-PSV-SEED-001` / `TEST-RSV-SEED-001` 现状**：09-18 已插入并验证（`3号位回执_致4号位_G4种子已补_B采纳_v1.0_20260918.md:19-24`），DRAFT 各 1 条。**防库重建丢失**：若 dev 库 09-18 后重建过需重跑（附录 B 段 1 幂等补种）。
2. **补版本链种子**：附录 B 段 2 为 1 个 ParameterSet + 1 个 RuleSet 各补 **DRAFT + PUBLISHED + 历史（ARCHIVED）** 3 版本链（幂等，VersionCode 前缀 `CHAIN-`）。
3. **⚠️ 依赖提示（diff 差异上限）**：`RuleSetVersion`/`ParameterSetVersion` 实体含 `ContentSnapshotJson` 等快照列，但**冻结 DDL v5.1.7 无这些列**（实体 vs DDL 漂移，属 P0-01/P0-02 方案 A 收口、待 2号位 DDL）。故版本链数据先补足，**字段级 diff 需 2号位 落快照列后才有真实差异**——请 4号位 缺口 B 演示按「版本链状态/元数据可见」验收，字段级 diff 依赖 2号位 DDL 收口。

---

## 五、[P2] parameter 写端点挂 `aps.rule.edit` 语义 —— **a. 有意为之**

**核实**：`GovernanceController.cs` parameter 写端点（L165/175/270）挂 `RuleMaintain`、publish（L222）挂 `RulePublish`；**且 strategy-profile 写/发布（L421/431/449）同样挂 `RuleMaintain`/`RulePublish`** —— rule/parameter/strategy 三域写操作统一挂 `aps.rule.edit` 聚合码（与 Auth 聚合入口码 `aps.auth.user.edit` 同范式，见 `PermissionCodes.cs:91-92` 说明：端点细分授权归 P0-02 批次）。`aps.parameter.edit`/`aps.strategy.edit` 为细分预留码。

**结论 a（有意为之）**：4号位 前端门控改对齐 `aps.rule.edit`（参数/策略页同用），`rbac.md` 登记语义备注；`aps.parameter.edit` 在细分批次落地前不启用。无 403 错位风险（dev 4 角色 admin.aps 持 rule.edit + parameter.edit 双码，前端 rule.edit 门控可被后端满足）。

---

## 六、速答清单（按 4号位 §六 格式）

```
一  Login/Logout 审计写入  : 补。09-25 前落地（AuthService 三写入点 + U42 脱敏）。4号位 追加 2 枚举。
二  aps.audit.view 4 角色  : admin.system 已持 ✅；viewer.management/planner/admin.aps 需补绑。附录 A SQL 幂等补齐。
三  audit-logs result+total: 同意方向，排期 09-25 后（扩契约 + 信封变更，先更契约文档）。前端维持现状。
四  版本链种子            : TEST-*-SEED-001 09-18 已插（重建需重跑）；补 CHAIN-* 3 版本链（附录 B）。字段级 diff 依赖 2号位 DDL。
五  parameter 挂 rule.edit : a 有意为之（三域统一聚合码）。前端门控对齐 rule.edit，rbac.md 登记。
```

---

## 附录 A：`aps.audit.view` 4 角色补绑 SQL（幂等）

```sql
USE APS_Auth;
GO
-- aps.audit.view → 4 角色（audit 为监督能力，4 角色均持；admin.system 已有则跳过）
DECLARE @AuditViewPermId INT = (SELECT Id FROM [Permission] WHERE PermissionCode = 'aps.audit.view');
IF @AuditViewPermId IS NOT NULL
BEGIN
    INSERT INTO RolePermission (RoleId, PermissionId)
    SELECT r.Id, @AuditViewPermId
    FROM [Role] r
    WHERE r.RoleCode IN ('aps.viewer.management', 'aps.planner', 'aps.admin.aps', 'aps.admin.system')
      AND NOT EXISTS (
          SELECT 1 FROM RolePermission rp
          WHERE rp.RoleId = r.Id AND rp.PermissionId = @AuditViewPermId
      );
END
GO
```

---

## 附录 B：版本链种子 SQL（幂等，dev 专用）

```sql
USE APS_Production;
GO

-- 段 1：防重建丢失 —— 幂等补种 TEST-*-SEED-001（与 09-18 同式）
IF NOT EXISTS (SELECT 1 FROM [dbo].[RuleSetVersion] WHERE [VersionCode] = N'TEST-RSV-SEED-001')
    INSERT INTO [dbo].[RuleSetVersion] ([RuleSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'TEST-RSV-SEED-001', N'DRAFT' FROM [dbo].[RuleSet];

IF NOT EXISTS (SELECT 1 FROM [dbo].[ParameterSetVersion] WHERE [VersionCode] = N'TEST-PSV-SEED-001')
    INSERT INTO [dbo].[ParameterSetVersion] ([ParameterSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'TEST-PSV-SEED-001', N'DRAFT' FROM [dbo].[ParameterSet];

-- 段 2：版本链种子（DRAFT + PUBLISHED + 历史 ARCHIVED，VersionCode 前缀 CHAIN-，幂等）
IF NOT EXISTS (SELECT 1 FROM [dbo].[RuleSetVersion] WHERE [VersionCode] LIKE N'CHAIN-RSV-%')
BEGIN
    INSERT INTO [dbo].[RuleSetVersion] ([RuleSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'CHAIN-RSV-001-ARCHIVED', N'ARCHIVED' FROM [dbo].[RuleSet] ORDER BY Id;
    INSERT INTO [dbo].[RuleSetVersion] ([RuleSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'CHAIN-RSV-002-PUBLISHED', N'PUBLISHED' FROM [dbo].[RuleSet] ORDER BY Id;
    INSERT INTO [dbo].[RuleSetVersion] ([RuleSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'CHAIN-RSV-003-DRAFT', N'DRAFT' FROM [dbo].[RuleSet] ORDER BY Id;
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[ParameterSetVersion] WHERE [VersionCode] LIKE N'CHAIN-PSV-%')
BEGIN
    INSERT INTO [dbo].[ParameterSetVersion] ([ParameterSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'CHAIN-PSV-001-ARCHIVED', N'ARCHIVED' FROM [dbo].[ParameterSet] ORDER BY Id;
    INSERT INTO [dbo].[ParameterSetVersion] ([ParameterSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'CHAIN-PSV-002-PUBLISHED', N'PUBLISHED' FROM [dbo].[ParameterSet] ORDER BY Id;
    INSERT INTO [dbo].[ParameterSetVersion] ([ParameterSetId], [VersionCode], [Status])
    SELECT TOP 1 Id, N'CHAIN-PSV-003-DRAFT', N'DRAFT' FROM [dbo].[ParameterSet] ORDER BY Id;
END
GO
```

> ⚠️ 段 2 依赖：1) `RuleSet`/`ParameterSet` 主表至少各 1 行（空则需先补主数据）；2) 冻结 DDL CK 六态须含 `ARCHIVED`（v5.1.x 含）；3) 字段级 diff 差异上限见 §四⚠️。

---

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 项一 Login/Logout 审计写入点实施（09-25 前）② 项二 附录 A SQL 落库 ③ 项四 附录 B SQL 落库 ④ 项三/项五 结论已定待 4号位 登记
