# Setup 维护 API · 3号位 致 4号位 交付回执（13 端点 + 权限码三件套 + dev seed 落地）

> 发件：3号位（Governance 后端 / Setup 规则治理）／收件：4号位（前端）／抄送：0号位（治理留档）
> 日期：2026-09-20
> 触发：4号位《4号位-2026-09-20-Setup契约源补发-给3号位.md》§十（权限码）/ §五（dev seed）
> 基底：3号位《3号位书面回复_致4号位_Setup维护API职责与契约确认_v1.1_20260920.md》（口径终定，仍有效）
> 性质：**落地交付回执**——v1.1 为实现前契约确认，本函为其落地态报告 + 实现偏差清单

---

## 〇、一句话

**13 端点全部落地（#10 待 2号位 C2 `SolveTraceNotes` DTO，明确延期）+ 3 权限码 + 角色绑定矩阵 + dev seed 齐，可启动联调。**

---

## 一、§十 权限码三件套（全部落地）

| 件 | 落地位置 |
|---|---|
| ① 常量 | `LPS.APS.Core\Authorization\PermissionCodes.cs:76-80`（`aps.setup.view` / `aps.setup.edit` / `aps.setup.publish`） |
| ② seed 行 | `LPS.APS.Engine\Services\Auth\PermissionSeedService.cs:49-52`（启动幂等落库，37 码） |
| ③ 角色绑定矩阵 | `LPS.APS.Web\Sql\APS_Setup_dev_seed_20260920.sql` **段 A**（幂等差集补齐，同 bootstrap 范式） |

**角色绑定矩阵**（与 `auth.ts` MOCK_ROLE_PRESETS + `verify-rbac.mjs` 期望严格一致）：

| 角色 | aps.setup.view | aps.setup.edit | aps.setup.publish |
|---|---|---|---|
| aps.viewer.management | ✅ | — | — |
| aps.planner | ✅ | — | — |
| aps.admin.aps | ✅ | ✅ | ✅ |
| aps.admin.system | ✅ | ✅ | ✅ |

> ⚠️ **admin 生效前置**：`APS_Auth_v1.3_admin_bootstrap_20260910.sql` 段 B 执行于 09-10（Setup 码未存）；admin.system 持有 Setup 码需**重跑 bootstrap 段 B** 或执行本 seed 段 A。

---

## 二、§十一 13 端点交付清单（路径前缀 `/api/governance`）

| # | 方法 + 路径 | 权限 | 状态 |
|---|---|---|---|
| 1 | `GET  /api/governance/setup-rules/exact` | view | ✅ |
| 2 | `POST /api/governance/setup-rules/exact` | edit | ✅ |
| 3 | `PUT  /api/governance/setup-rules/exact/{id}` | edit | ✅ |
| 4 | `DELETE /api/governance/setup-rules/exact/{id}` | edit | ✅ |
| 5 | `GET  /api/governance/setup-rules/default` | view | ✅ |
| 6 | `POST /api/governance/setup-rules/default` | edit | ✅ |
| 7 | `PUT  /api/governance/setup-rules/default/{id}` | edit | ✅ |
| 8 | `DELETE /api/governance/setup-rules/default/{id}` | edit | ✅ |
| 9 | `GET  /api/governance/operation-resource-eligibility` | view | ✅（from∩to 合法设备交集，Code 回带） |
| 10 | `GET  /api/governance/setup-rules/uncovered-stats` | view | ⏳ **延期**（依赖 2号位 C2 `SolveTraceNotes`；2号位 已选 A 载体，待 0号位 点头后落 DTO，3号位 即补，聚合已备） |
| 11 | `GET  /api/governance/rule-set-versions` | view | ✅（`ruleSetId`/`status` 过滤） |
| 12 | `GET  /api/governance/rule-set-versions/{id}/diff` | view | ✅（含 `setupRuleChanges` 三维 + before/after 快照） |
| 13 | `POST /api/governance/rule-set-versions/{id}/publish` | publish | ✅（**发布前 Setup 冲突全量预校验**） |

---

## 三、§五 dev seed（Q6 最终答复：**是**，位置如下）

- **位置**：`LPS.APS.Web\Sql\APS_Setup_dev_seed_20260920.sql`（dev 库手工执行；同 bootstrap 范式）
- **执行顺序**：先启动应用一次（`PermissionSeedService` 落 Setup 3 码）→ 再执行脚本。
- **段 B**：幂等确保 ≥1 `RuleSet`（`DEV_SETUP`）+ ≥1 **DRAFT** `RuleSetVersion`（VersionCode 带日期后缀，规避 UQ 冲突）。
- **段 C**：幂等确保 ≥1 `COMPLETED` `FULL_SCHEDULE` `ScheduleRun`。

> ⚠️ **术语偏差（须知悉）**：契约 §五「ACTIVE Run」→ 落库为 **`COMPLETED`**。
> 依据：`ScheduleRun.Status` 冻结四态 = `RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED`（v5.1.2 §3.1），**无 ACTIVE 态**（DDL 中 "ACTIVE" 指 `BasePlanVersionId` 关联的当前 ACTIVE 计划版本）。`COMPLETED` 为 uncovered-stats 的 runId 来源，404 语义一致。

---

## 四、实现偏差清单（前端需知悉 / 适配）

| # | 偏差 | 说明 |
|---|---|---|
| 1 | **`remark` 字段不返回** | `SetupRuleDto` 不含 `remark`（用户口径终定：不返回）；契约 §11.1 表格字段若前端依赖，请确认从契约撤除 |
| 2 | **创建返回 200 非 201** | `POST` 返 `200` + `ApiResponse<SetupRuleDto>`（无 GET-by-id 路由，不设 Location header） |
| 3 | **`SetupMinutes > 0`** | 严于 DDL CK ≥ 0；`≤0` → 422（消息原文） |
| 4 | **EXACT×DEFAULT 跨类冲突不强制** | 冻结校验器仅同类判重（EXACT 七元组 / DEFAULT 五元组分别）；DEFAULT 为 EXACT 无命中兜底，二者并存合理——**契约 §8.3 跨类冲突条款建议撤回**（如需强制须走 0号位 裁决） |
| 5 | **#9 materialId = `Material.Id`** | 与 v1.1 §三 终定一致（内部主键非 Code）；`toMaterialId` 可选（不传 = 仅 from 物料合法设备） |
| 6 | **status 派生三态** | 版本六态 → `DRAFT`（DRAFT/SUBMITTED/APPROVED）/ `ACTIVE`（PUBLISHED）/ `DEPRECATED`（DISABLED/ARCHIVED） |
| 7 | **错误映射** | `400` 状态/参数红线（`InvalidOperationException`）/ `422` 数据红线（`SetupRuleDataRedLineException`，业务消息可读原文）/ `403` 权限（`[Authorize]`）/ `404` 不存在——`ApiResponse<T>` 信封 + 真实 HTTP code |

---

## 五、联调注意事项

1. **Setup 发布请统一走 #13**：旧发布路径 `GovernanceController rule-set/version/{versionId}/publish` **不校验 Setup 冲突**（旁路缺口，3号位 新端点已补齐；旧路径处置另报）。
2. **#10 上线依赖**：2号位 `SolveTraceNotes`（选 A，待 0号位 点头）；`SetupUncoveredStatDto` 聚合已备，DTO 落库即补。
3. **dev seed 幂等可重跑**：全段差集补齐/存在即跳过。

---

## 六、待 4号位

1. `verify-integration.mjs GROUP=setup` 12 断言 + `verify-rbac.mjs` 84/84 验收。
2. 前端 DTO 改 Id 回执确认（v1.1 §三 双返回机制）。
3. 偏差清单确认：**#1 remark 撤除**、**#4 跨类冲突条款撤回**（如认同）。

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 4号位 联调验收 ② 0号位 点头 C2 A → #10 补发 ③ 偏差清单 3 项确认
