# 3号位契约收口总结汇报 — 致 4号位

**发送**：3号位
**接收**：4号位（前端 Vue 3 + TypeScript）
**日期**：2026-09-16
**主题**：`3号位契约-依赖与扩展项(1).md` §二 全部待办收口 + 4号位 待办任务清单

---

## 一、3号位 已干完（7 项全部收口 + 2 项骨架）

来源 §二「剩余待办」，提出方为 4号位。3号位 侧已全部处置完毕，无遗留阻塞。

| # | 4号位提出的问题 | 3号位 处置结果 |
|---|---|---|
| ① | 业务 scope 后端二次校验缺失（P0 越权，绕过前端即可越权写） | ✅ 新增 `IDataScopeService.EnsureInScopeAsync`（fail-closed）+ `ScopeViolationException`；confirm-candidate / activate-candidate 挂 403 兜底 |
| ② | DomainDefinition 字段契约（前端 string code vs 后端 int FK，保存 100% 400） | ✅ 裁方案A + 补 `GET /api/governance/product-families` / `factories` 两个下拉读端点 |
| ③ | RoleCode / permissionCode 无 `aps.` 前缀时 500 SQL CHECK 约束 | ✅ DTO 已有 `^aps\..+` 正则；补 `ConfigureApiBehaviorOptions` → 422 + ApiResponse 信封 |
| ④ | RBAC「读回当前分配」4 个 GET 缺失（AssignDialog 无法预勾选） | ✅ 已实现 4 个读回端点 |
| ⑤ | GovernanceController 类级 Authorize 缺失（28 GET 裸奔） | ✅ 28 GET 挂码、类级 PlanView 撤销 |
| ⑥ | Governance 响应包装与 RBAC 不一致 | ✅ 裁方案A（全量迁 ApiResponse，失败字段 error→message） |
| ⑦ | audit-logs 查询端点缺失 | ✅ 已实现 `GET /api/rbac/audit-logs` |
| ⑧ | CTP 评估 / MES 下发端点缺失（权限码 `aps.ctp.evaluate` / `aps.mes.dispatch` 早已冻结在库） | ✅ 已建骨架端点，挂权限码 + scope 二次校验；核心能力留 501（见 §二.5） |

§八 联调实测的 5 个问题（int FK 400 / CHECK 500 / 201 vs 200 / 包装不一致 / 28 GET 裸奔）已被上表 ②③⑤⑥ 全部覆盖。

**build 0 错误，RunLifecycleServiceTests 43/43 通过。**

---

## 二、4号位 待办任务清单（讲清楚每一项）

> 以下 1、2 项是**必须改的前端代码**，不改则功能不可用；3、4 项是**确认兼容**；5 项**暂不接**。

### 1. ② Domain 维护页 DTO 改 number + 接下拉（必须）

- 前端 DTO：`productFamily: string` → **`productFamilyId: number`**；`factory?: string` → **`factoryId?: number`**
- 维护页新增 ProductFamily / Factory 选择器，数据源调：
  - `GET /api/governance/product-families`
  - `GET /api/governance/factories`
- **不改的后果**：Domain 维护页「保存」按钮生产模式 100% 400（model binder 拒收 string）。

### 2. ④ AssignDialog 预勾选（建议改，解除体验痛点）

打开分配弹窗时先拉当前分配并预勾选，替代「默认全不勾选」：

| 端点 | 返回 |
|---|---|
| `GET /api/rbac/users/{id}/roles` | `RoleSummaryDto[]` |
| `GET /api/rbac/users/{id}/scopes` | `DataScopePolicyDto[]` |
| `GET /api/rbac/roles/{id}/permissions` | `PermissionSummaryDto[]` |
| `GET /api/rbac/roles/{id}/scopes` | `DataScopePolicyDto[]` |

预勾选上线后，顶部红条警告「本弹窗不代表当前生效配置」**降级为普通提示**。

### 3. ①③ 403 / 422 信封兼容（确认）

- 403（scope 越界）：`{ code: 403, message: "业务范围越界（domain）：…", data: null, traceId, timestamp }`
- 422（RoleCode/permissionCode 无前缀）：`{ code: 422, message: "角色编码必须以 aps. 前缀开头", data: null, … }`
- 前端 `apsHttp` 按 **`json.code`** 判定即可直接兼容；**不要再按 `{ success:false, error }` 结构解析**。
- ③ 表单层建议仍加 `pattern: /^aps\..+/` 实时校验（体验优化，非必须）。

### 4. ⑥ Governance ApiResponse 统一（确认）

前端 `apsHttp` 对 Governance 与 RBAC 两套响应做双解包兼容即可（已裁方案A，后端不再两套）。

### 5. CTP/MES 骨架端点（暂不接）

`POST /api/ctp/evaluate`、`POST /api/mes/dispatch` 已挂权限码 + scope 校验，但核心能力**返回 501**（试算归 1/2号位、下发归 2号位）。待对应号位接入核心能力后，前端再对接。

---

## 三、遗留跨号位项（非 4号位 动作，仅供知悉）

| 项 | 归属 | 状态 |
|---|---|---|
| RuleSet 发布/停用 scope 校验 | 已裁方案A | 不补校验，仅保留 `RulePublish` 权限码，结案 |
| DemandProtection 释放 Factories 校验 | 5号位 | 已书面澄清，待 5号位 接 |
| CTP 试算 / MES 下发核心能力 | 1号位 / 2号位 | 骨架已就位，核心待接入 |

---

## 四、待 4号位 回复的 3 个联调点

1. ① 的 403 信封与 `apsHttp` 解包是否兼容？
2. ② 前端 DTO 改 `number` 后，维护页是否已接入下拉？
3. ④ 预勾选上线后，红条警告是否已降级？

请 4号位 回复以上三点，3号位 按需配合联调。