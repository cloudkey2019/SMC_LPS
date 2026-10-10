# 3号位 致 4号位 — Setup 联调验收待办收口（12 断言 + verify-rbac + DTO 改 Id + audit.ts 枚举）

> **发送人**：3号位（Governance 后端 / Setup 规则治理 / 审计 Owner）
> **接收人**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-20
> **触发**：Setup 联调验收待办收口（Setup 交付回执《APS_V1_Setup维护API_3号位致4号位_交付回执_v1.0_20260920.md》§六 + Audit 落地交付回执《3号位回执_致4号位_Audit沟通项落地交付_v1.0_20260920.md》§五 合并收口）
> **性质**：**联调验收待办收口函**——请 4号位 按本函 4 项验收并回执
> **效力**：以冻结文档为准；偏差确认项见 §六

---

## 〇、一句话

**Setup 13 端点 + 3 权限码 + dev seed 已交付（Setup 交付回执）；Audit Login/Logout 审计写入已落地（Audit 落地交付回执）。请 4号位 按本函 4 项验收待办执行并回执，即可关闭 Setup 联调验收。**

---

## 一、4 项验收待办总览

| # | 项 | 期望 | 契约源 |
|---|---|---|---|
| 1 | `verify-integration.mjs GROUP=setup` | **12 断言全绿** | Setup 交付回执 §二 13 端点表 |
| 2 | `verify-rbac.mjs` | **Setup 相关断言全绿**（Setup 侧口径 84/84；4号位 附录 A 曾报 88/88，以脚本实际运行为准） | Setup 交付回执 §一 权限矩阵 |
| 3 | 前端 DTO 改 **Id** 回执确认 | 提交 Id、回带 Code（双返回机制） | 记忆/Setup 交付回执 §四 #5 + §六② |
| 4 | `audit.ts` `AUDIT_ACTION_CODES` 追加枚举 | 追加 `Login`/`Logout` 2 枚举 + 中文 label | Audit 落地交付回执 §一 + §五① |

---

## 二、项 1 — `verify-integration.mjs GROUP=setup`（12 断言）

- **命令**（4号位 侧）：`cd frontNew && GROUP=setup node scripts/verify-integration.mjs`
- **期望**：12 断言全绿（Setup 13 端点中 **12 个已落地端点**的契约断言；**#10 uncovered-stats 不在断言内**——依赖 2号位 `SolveTraceNotes` DTO，明确延期）
- **若红**：请把失败断言 + 响应体原文回传，3号位 即修（不经过其他号位）

---

## 三、项 2 — `verify-rbac.mjs`

- **命令**：`cd frontNew && node scripts/verify-rbac.mjs`
- **期望**：Setup 相关角色×路由组合全绿（`aps.setup.view/edit/publish` 3 权限码 × 4 角色绑定矩阵，见 Setup 交付回执 §一）
- **注意**：admin.system 持 Setup 3 码需 **dev 库已执行 Setup seed 段 A**（`APS_Setup_dev_seed_20260920.sql`）或重跑 bootstrap 段 B；如 admin.system 断言红，请先确认 seed 段 A 已执行

---

## 四、项 3 — 前端 DTO 改 Id 回执确认

**背景**（Setup 换型口径终定，2026-09-20）：`SetupTransitionRule` 保留 Id 口径（2号位 主链两级 Id 口径全落地、测试 39 条全绿，翻案放弃 Code 重建）。**混合口径**：

| 字段 | 类型 |
|---|---|
| `ProductionDepartmentId` / `ResourceId` / `FromMaterialId` / `ToMaterialId` | **INT（Id）** |
| `StageCode` / `OperationCode` | **NVARCHAR（Code）** |

**请 4号位 回执确认**：
1. 前端 DTO 已改传 **Id**（#9 `materialId` = `Material.Id`；`toMaterialId` 可选，不传 = 仅 from 物料合法设备）；
2. **双返回机制**：提交 Id、回带 Code（读端点返回 Code 供展示）；
3. `SetupRuleDto` 不含 `remark`（偏差清单 #1，若前端依赖请确认从契约撤除）。

---

## 五、项 4 — `audit.ts` 追加枚举

- **文件**：`frontNew/src/api/aps-v1/types/audit.ts` `AUDIT_ACTION_CODES`
- **改动**：追加 `Login` / `Logout` 2 枚举 + 中文 label（≈0.1 人天）；Audit.vue 零返工，筛选下拉自动多出 2 项
- **后端已就绪**：Login 成功/失败、Logout 均写 `AuditLog`（actionCode=`Login`/`Logout`，module=`Auth`，result=`Success`/`Failed`，U42 脱敏——errorMessage 仅原因类别）

---

## 六、偏差清单确认（关联 Setup 交付回执 §六③，一并回执）

| # | 偏差 | 请 4号位 确认 |
|---|---|---|
| 1 | `remark` 字段不返回 | 撤除契约 §11.1 remark 引用 |
| 2 | 创建返回 200 非 201 | 前端已适配 |
| 3 | `SetupMinutes > 0`（严于 DDL CK ≥ 0） | 前端校验对齐 |
| 4 | EXACT×DEFAULT 跨类冲突**不强制**（契约 §8.3 建议撤回） | 认同撤回 / 或提请 0号位 裁决 |
| 5 | #9 `materialId` = `Material.Id` | 并入项 3 回执 |
| 6 | status 派生三态（DRAFT/ACTIVE/DEPRECATED） | 前端枚举对齐 |
| 7 | 错误映射 400·422·403·404 | 前端消息对齐 |

---

## 七、待 4号位 回执清单

1. 项 1~2 验收结果（12 断言 + verify-rbac 数字与红绿）
2. 项 3 DTO 改 Id 三小点确认
3. 项 4 audit.ts 枚举已追加确认
4. §六 偏差清单 7 项逐条确认

**期望回执**：09-25 合入节点前。回执后 3号位 侧 Setup 联调验收即关闭；#10 待 2号位 `SolveTraceNotes` DTO 后补发（聚合已备，DTO 落库即补）。

---

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 4号位 4 项验收回执 ② 偏差清单 7 项确认 ③ #10 待 2号位 DTO
