# 3号位 → 4号位：RBAC 管理 4 页面——数据清理与 UI 精简 回执（v1.0）

> **发件**：3号位（Auth / RBAC / Scope Owner）
> **致**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-28 ｜ **版本**：v1.0
> **依据**：`4号位-2026-09-28-RBAC管理4页面-数据清理与UI精简-给3号位.md`
> **性质**：正式回执（R1-R5 决策 + 分工 + 时间）

---

## 〇、速答清单（回应贵方 §〇）

```
R1 测试数据清理方案（后端兜底）   : 方案 = a（新建 POST /api/rbac/test-data/cleanup，admin only，按命名规则软删）
R2 后端分页支持（4 GET 端点）      : 排期 = 10-08 前合入 dev；契约发出时间 = 09-30（随本回执附独立契约文档）
R3 测试数据标记字段 IsTestData     : 拒绝（YAGNI；方案 a 命名规则已覆盖，不做 4 表加列）
R4 批量删除接口                   : 同意（4 个批量端点，复刻单删红线）
R5 4 页面 UI 精简（前端自闭环）     : 确认（F1-F4 零后端配合，贵方自闭环）
```

---

## 一、R1 测试数据清理 —— 方案 a（3号位 落地，09-30 前）

承接贵方 §三，采纳**方案 a**（最低改动 + 一周上线 + 软删可逆），并确认后端兜底为**持久端点**而非一次性脚本：既做当前扫尾，也供后续每周/按需触发，杜绝 verify 残留再次堆积。

### 1.1 端点契约

- **路由**：`POST /api/rbac/test-data/cleanup`
- **鉴权**：`[Authorize(Policy = PermissionCodes.AuthManage)]`（复用 `auth.manage`，admin only）
- **语义**：按命名规则**软删**测试残留（与现有单条 `DeleteUser`/`DeleteRole` 同机制的软删除），不物理删、不带任意 id 列表。

### 1.2 软删命名规则（与 verify 脚本命名约定一一对应）

| 实体 | 规则 | 处置 |
|---|---|---|
| 用户 `User` | `UserCode LIKE 'TEST-USER-%'` 或 `UserCode LIKE 'aps.auth.negate.%'` | 软删（`Status='Deleted'`） |
| 角色 `Role` | `RoleCode LIKE 'aps.verify.role.%'` | 软删 |
| 权限 `Permission` | `PermissionCode LIKE 'aps.verify.%'`（module='verify'） | 停用（`IsActive=false`；权限当前无 DELETE 端点，先停用） |

### 1.3 安全边界（fail-closed）

- **自删保护**：跳过当前登录账号（绝不软删操作者自身）。
- **只软删不物理删**：可逆，误判可恢复。
- **仅按命名规则匹配**，不提供「任意 id 列表」入参，杜绝误删正常数据。
- 命中 0 条＝幂等成功（返回 0，不报错）。

### 1.4 响应结构（初拟，与贵方 V1 对齐）

```json
{ "code": 200, "message": "success",
  "data": { "users": 30, "roles": 20, "permissions": 41, "failed": [] } }
```

`failed` 元素 `{ "kind": "user|role|permission", "id": 123, "reason": "..." }`（正常情况下为空）。

> 目标对齐贵方 V1：清理后用户/角色/权限码 3 页残留归零。

---

## 二、R2 后端分页支持 —— 同意（契约先行）

- **排期**：契约 **09-30** 发出（随本回执附独立契约文档），后端实现 **10-08** 前合入 dev（不阻塞贵方 10-01 mock 联调）。
- **响应结构**：采纳贵方前端偏好 `{ items, total, page, pageSize }`，**包在既有 `ApiResponse<T>` 信封内**（`data` 字段承载分页对象），即 `ApiResponse<PageResult<T>>`。
- **契约详情**：完整契约另附独立文档 `3号位致4号位_RBAC列表分页接口契约_v1.0_20260928.md`。

---

## 三、R3 测试数据标记字段 `IsTestData` —— 拒绝

采纳贵方倾向，**拒绝**做 R3。理由：

1. **YAGNI**：方案 a 命名规则识别 + cleanup 端点已覆盖「清残留」需求，无需额外标记字段。
2. **成本高**：需 `User`/`Role`/`Permission`/`DataScopePolicy` 4 张表加 `IsTestData BIT` 列 + migration + List 端点 `includeTestData` 参数 + 前端复选框，与收益不成比例。
3. **贵方 verify 脚本 cleanup 补齐后**，命名规则即唯一真源，永久字段反而引入「标记与命名不一致」的第二真源风险。

---

## 四、R4 批量删除接口 —— 同意

承接贵方 §六，4 个批量端点，**复刻现有单删红线**。

| 端点 | body | 返回 |
|---|---|---|
| `POST /api/rbac/users/batch-delete` | `{ "ids": [1,2,3] }` | `{ succeeded: number[], failed: [{ id, reason }] }` |
| `POST /api/rbac/roles/batch-delete` | `{ "ids": [...] }` | 同上 |
| `POST /api/rbac/permissions/batch-delete` | `{ "ids": [...] }` | 同上 |
| `POST /api/rbac/scopes/batch-delete` | `{ "ids": [...] }` | 同上 |

### 4.1 复刻红线（逐条校验，失败项入 `failed`，其余照常成功）

- **自删保护**：批量中若含当前登录账号 → 该条剔除并记 `failed: { id, reason: "不能删除当前登录账号" }`。
- **系统角色保护**：`IsSystemRole=true` 的角色不删（`failed`）。
- **内置权限码保护**：种子内置权限码（非 `aps.verify.*`）不删（`failed`）。
- **引用范围静默失效**：与单删一致，删除后相关用户/角色引用静默失效（沿用现有不清告警）。

权限「删除」当前仅有停用语义（见 R1）；R4 落地时一并明确权限批量删除＝`IsActive=false` 停用（同 R1 口径），或补真删——**待贵方确认 UI 需求**（是否要「真删已停用权限码」）。

---

## 五、R5 4 页面 UI 精简 —— 确认

F1（删 RbacPermissions 顶部 ElAlert）、F2（删 RbacScopes 顶部 ElAlert）、F3（4 页加重置筛选）、F4（RbacUsers 加「仅看启用」），**全部贵方自闭环，3号位 零配合**，无异议。

---

## 六、配合时间（对齐贵方 §十二）

| 项 | 3号位 承诺 | 4号位 依赖 |
|---|---|---|
| R1 回执（方案 a） | **本件即回** | 09-30 前已满足 |
| R1 cleanup 端点实现 | 09-30 前合入 | V1 验收 |
| R2 契约文档 | **09-30**（随本回执附） | 前端 10-01 mock 联调 |
| R2 后端分页实现 | **10-08** 前合入 | V4 验收 |
| R3 回执 | **本件即回（拒绝）** | 无需跟进 |
| R4 回执 | **本件即回（同意）** | 无需跟进 |
| R4 批量接口实现 | 10-08 批次（随 R2） | V5 验收 |

**遗留待贵方确认**：R4 权限批量删除的「停用 vs 真删」口径（见 §4.1 末行）。

---

**发件**：3号位 ｜ **致**：4号位（抄送 0号位）｜ **日期**：2026-09-28 ｜ **版本**：v1.0
**本件性质**：正式回执——R1 方案 a、R3 拒绝、R4 同意、R5 确认；R2 契约另附独立文档。