# 3号位致4号位 — 契约收口回执（①③④⑦ 落地声明）

**发送**：3号位
**接收**：4号位（前端 Vue 3 + TypeScript）
**日期**：2026-09-15
**主题**：`3号位契约-依赖与扩展项(1).md` §二 剩余待办收口回执

---

## 一、收口结论

§二「剩余待办」7 项已全部处置完毕（含「已实现」与「已裁决不落地」两类），无遗留阻塞项。

| # | 任务 | 收口结果 | 4号位需要做的事 |
|---|---|---|---|
| ① | scope 二次校验（P0） | ✅ 已落地 | **可复跑 §五 验证表第 6~9 行**（Postman/curl 直打后端，走 UI 测不出） |
| ② | DomainDefinition 字段契约 + product-families/factories 读端点 | ✅ 已裁方案A + 补端点 | 前端 DTO 改 `productFamilyId: number; factoryId?: number`，维护页接下拉数据源 |
| ③ | RoleCode/permissionCode 422 | ✅ 已落地 | 无需改动（表单层建议仍加 `pattern: /^aps\..+/` 实时校验提升体验） |
| ④ | RBAC 读回 4 GET | ✅ 已实现 | **可启用 AssignDialog 预勾选**，红条警告降级为普通提示 |
| ⑤ | GovernanceController 类级 Authorize | ✅ 已处置 | 无 |
| ⑥ | Governance ApiResponse 统一 | ✅ 已裁方案A | 前端 `apsHttp` 双解包兼容即可 |
| ⑦ | audit-logs 查询 | ✅ 已实现 | 审计页可恢复开发 |

---

## 二、① scope 二次校验的响应格式变更（重要）

原 §三① 草稿是 `{success:false, error:…}`，**落地时统一为**：

```json
{ "code": 403, "message": "业务范围越界（domain）：xxx 不在当前用户授权范围", "data": null, "traceId": "…", "timestamp": "…" }
```

即 **`ApiResponse.Fail(403, …)` 信封**（与 RbacController 一致），HTTP 状态码 = 403。前端 `apsHttp` 若按 `json.code` 判定即可直接兼容；**不要再按 `{success, error}` 结构解析**。

---

## 三、③ 的 422 响应格式

`Role.RoleCode` / `Permission.permissionCode` 违反 `^aps\..+` 时，现在返回：

- **HTTP 状态码**：422（UnprocessableEntity）
- **body**：`{ "code": 422, "message": "角色编码必须以 aps. 前缀开头", "data": null, … }`

前端 RbacRoles/RbacPermissions 表单若用 `apsHttp` 解包，`json.code === 422` 即失败，`message` 为中文提示，可直接展示。

---

## 四、④ 读回端点清单（供 4号位 AssignDialog 对接）

| 端点 | 返回 |
|---|---|
| `GET /api/rbac/users/{id}/roles` | `RoleSummaryDto[]` |
| `GET /api/rbac/users/{id}/scopes` | `DataScopePolicyDto[]` |
| `GET /api/rbac/roles/{id}/permissions` | `PermissionSummaryDto[]` |
| `GET /api/rbac/roles/{id}/scopes` | `DataScopePolicyDto[]` |

---

## 五、待 4号位 确认的联调点

1. ① 的 403 信封（`{code:403, message}`）与 `apsHttp` 解包是否兼容？
2. ② 前端 DTO 改 `number` 后，维护页是否已接入 `GET /api/governance/product-families` / `factories` 下拉？
3. ④ 预勾选上线后，红条警告是否已降级？

请 4号位 回复以上三点，3号位按需配合联调。
