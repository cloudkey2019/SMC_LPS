# 3号位 → 4号位：RBAC 列表分页接口契约（v1.0）

> **发件**：3号位（Auth / RBAC / Scope Owner）
> **致**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-28 ｜ **版本**：v1.0
> **性质**：R2 接口契约（红线 #5「接口即契约」先行；后端 10-08 前合入）
> **用途**：4号位 据此改 `src/api/aps-v1/rbac.ts` + 4 页面 `<ElPagination>`，可 10-01 起 mock 联调

---

## 〇、一句话

4 个 RBAC 列表 GET 端点由「一次性全量 `IReadOnlyList<T>`」改为**分页**，响应统一为 `ApiResponse<PageResult<T>>`：`data` 内承载 `{ items, total, page, pageSize }`。

---

## 一、通用响应信封（既有，不变）

所有 RBAC 端点继续走既有 `ApiResponse<T>` 信封，仅 `data` 由「列表」变为「分页对象」：

```json
{
  "code": 200,
  "message": "success",
  "data": { "items": [ ... ], "total": 156, "page": 1, "pageSize": 20 },
  "traceId": "…",
  "timestamp": "2026-09-28T12:00:00"
}
```

失败时 `code != 200`、`data = null`、`message` 为原因，与现状完全一致（不新增字段语义）。

## 二、分页容器 `PageResult<T>`

```csharp
public sealed class PageResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>(); // 当前页数据
    public int Total { get; set; }                                  // 命中总数（表头 total 联动）
    public int Page { get; set; }                                   // 当前页码（从 1 起）
    public int PageSize { get; set; }                               // 每页条数
}
```

- `page` 默认 `1`，`pageSize` 默认 `20`；`pageSize` 允许 `20/50/100`，**上限 200**（超限按 200 截断，防拉全表）。
- `page` / `pageSize` 非法（≤0 或非整数）→ 回落默认值，不报错（宽容解析）。

## 三、4 端点明细

### 3.1 `GET /api/rbac/users`

| 参数 | 类型 | 默认 | 语义 |
|---|---|---|---|
| `page` | int | 1 | 页码 |
| `pageSize` | int | 20 | 每页条数 |
| `keyword` | string? | 空 | 模糊匹配 `UserCode`/`UserName`/`Email`（三字段 `LIKE %kw%`） |
| `status` | string? | 空 | 精确匹配 `Status`（`Active`/`Disabled`/`Deleted`；空＝不过滤） |

`items` 元素 = `UserSummaryDto`（字段不变：`Id/UserCode/UserName/Email/PhoneNumber/Status/LastLoginTime/CreatedAt`）。

### 3.2 `GET /api/rbac/roles`

| 参数 | 类型 | 默认 | 语义 |
|---|---|---|---|
| `page` / `pageSize` | int | 1 / 20 | 同上 |
| `keyword` | string? | 空 | 模糊匹配 `RoleCode`/`RoleName` |
| `isSystem` | bool? | 空 | 精确过滤 `IsSystemRole`（空＝不过滤） |

`items` 元素 = `RoleSummaryDto`（`Id/RoleCode/RoleName/Description/IsSystemRole/IsActive/CreatedAt`）。

### 3.3 `GET /api/rbac/permissions`

| 参数 | 类型 | 默认 | 语义 |
|---|---|---|---|
| `page` / `pageSize` | int | 1 / 20 | 同上 |
| `module` | string? | 空 | 精确匹配 `Module`（如 `Rule`/`Setup`/`verify`） |
| `actionType` | string? | 空 | 精确匹配 `ActionType` |
| `keyword` | string? | 空 | 模糊匹配 `PermissionCode`/`PermissionName` |

`items` 元素 = `PermissionSummaryDto`（`Id/PermissionCode/PermissionName/Description/Module/ActionType/IsActive/CreatedAt`）。

### 3.4 `GET /api/rbac/scopes`

| 参数 | 类型 | 默认 | 语义 |
|---|---|---|---|
| `page` / `pageSize` | int | 1 / 20 | 同上 |
| `scopeType` | string? | 空 | 精确匹配 `ScopeType` |
| `keyword` | string? | 空 | 模糊匹配 `ScopeValue`/`Description` |

`items` 元素 = `DataScopePolicyDto`（`Id/ScopeType/ScopeValue/Description/CreatedAt`）。

---

## 四、排序规则（本轮固定，不加 sort 参数）

| 端点 | 默认排序 |
|---|---|
| `users` | `Id ASC` |
| `roles` | `Id ASC` |
| `permissions` | `Module ASC, Id ASC` |
| `scopes` | `Id ASC` |

（如需按列排序，后续补 `sortField`/`sortOrder` 参数，**本轮不做**——YAGNI。）

---

## 五、兼容性声明（破坏性变更，一次性切换）

- 旧契约 `GET /users` 等返回 `{ code, data: [ ...flat list... ] }` → **废弃**。
- 新契约 `data` 恒为 `{ items, total, page, pageSize }`，**不回退旧 list 形态**（不回兼容双态）。
- 依据：RBAC 4 页面为 admin 内网工具、单版本部署，无第三方消费者；贵方 `rbac.ts` 一次性切换即可，无并行期。

---

## 六、前端切换要点（提示，非要求）

- `listUsers()` 等 7 个方法中，仅 **4 个列表方法**（`listUsers/listRoles/listPermissions/listScopes`）加 `page/pageSize/…` 参数并解包 `data.items`；**读回端点**（`GET /users/{id}/roles` 等，AssignDialog 预勾选用）**仍返回扁平 `IReadOnlyList`，不改**。
- 4 页面 `<ElPagination>` 的 `total` 取用 `data.total`，`page-size` 默认 20、可切 50/100。

---

**发件**：3号位 ｜ **致**：4号位（抄送 0号位）｜ **日期**：2026-09-28 ｜ **版本**：v1.0
**本件性质**：R2 分页接口契约——4 GET 端点参数 + `PageResult<T>` 响应 + 兼容性声明。