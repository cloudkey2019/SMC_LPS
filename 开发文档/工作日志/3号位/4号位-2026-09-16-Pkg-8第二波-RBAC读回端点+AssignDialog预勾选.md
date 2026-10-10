# Pkg-8 第二波 — RBAC 读回端点 + AssignDialog 预勾选

> **触发**：2026-09-16 [APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md](APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md) §一.④ 确认 4 个读回端点已实现；解除 Pkg-8-RBAC完成报告.md §六「不动部分：RBAC 读回端点」+ §八「未来工作 #4」阻塞
> **作者**：4号位（前端）
> **日期**：2026-09-16
> **优先级**：🟡 P1（不影响 Pkg-8 闭环验收，但显著提升 UX）

---

## 一、4 个读回端点契约（来自 3号位）

| 端点 | 返回类型 | 用法 |
|---|---|---|
| `GET /api/rbac/users/{id}/roles` | `RoleSummaryDto[]` | 用户→分配角色 预填 |
| `GET /api/rbac/users/{id}/scopes` | `DataScopePolicyDto[]` | 用户→分配范围 预填 |
| `GET /api/rbac/roles/{id}/permissions` | `PermissionSummaryDto[]` | 角色→分配权限 预填 |
| `GET /api/rbac/roles/{id}/scopes` | `DataScopePolicyDto[]` | 角色→分配范围 预填 |

**返回类型已就绪**（Pkg-8 §8.1 [src/api/aps-v1/types/rbac.ts](src/api/aps-v1/types/rbac.ts) 已定义 `RoleSummaryDto` / `PermissionSummaryDto` / `DataScopePolicyDto`），无需新增类型。

---

## 二、改动清单

### 1. `src/api/aps-v1/rbac.ts`（+4 端点，约 30 行）

```ts
export const rbacApi = {
  // ... 现有 17 端点 ...
  /** 读回用户已分配角色（3号位 GET /api/rbac/users/{id}/roles）*/
  getUserRoles: (id: number) => APS_USE_MOCK
    ? Promise.resolve(MOCK_USER_ROLES_BY_ID[id] ?? [])
    : apsHttp.get<RoleSummaryDto[]>({ url: `/api/rbac/users/${id}/roles` }),
  /** 读回用户已分配范围（3号位 GET /api/rbac/users/{id}/scopes）*/
  getUserScopes: (id: number) => APS_USE_MOCK
    ? Promise.resolve(MOCK_USER_SCOPES_BY_ID[id] ?? [])
    : apsHttp.get<DataScopePolicyDto[]>({ url: `/api/rbac/users/${id}/scopes` }),
  /** 读回角色已分配权限（3号位 GET /api/rbac/roles/{id}/permissions）*/
  getRolePermissions: (id: number) => APS_USE_MOCK
    ? Promise.resolve(MOCK_ROLE_PERMS_BY_ID[id] ?? [])
    : apsHttp.get<PermissionSummaryDto[]>({ url: `/api/rbac/roles/${id}/permissions` }),
  /** 读回角色已分配范围（3号位 GET /api/rbac/roles/{id}/scopes）*/
  getRoleScopes: (id: number) => APS_USE_MOCK
    ? Promise.resolve(MOCK_ROLE_SCOPES_BY_ID[id] ?? [])
    : apsHttp.get<DataScopePolicyDto[]>({ url: `/api/rbac/roles/${id}/scopes` })
}
```

**mock fixtures**（仅 mock 模式）：
- `MOCK_USER_ROLES_BY_ID: Record<number, RoleSummaryDto[]>`
- `MOCK_USER_SCOPES_BY_ID: Record<number, DataScopePolicyDto[]>`
- `MOCK_ROLE_PERMS_BY_ID: Record<number, PermissionSummaryDto[]>`
- `MOCK_ROLE_SCOPES_BY_ID: Record<number, DataScopePolicyDto[]>`
- 例如 admin (id=1) 默认有 SYSTEM_ADMIN 角色

### 2. `src/views/Aps/components/AssignDialog.vue`（关键改造）

**新增 props**：
```ts
defineProps<{
  // ... 现有 props ...
  /** 预勾选 id 列表（外部打开 Dialog 时传入） */
  preCheckedIds?: number[]
  /** 加载预勾选中（外部调读回端点时为 true；空状态/失败时不显示勾选） */
  loadingPrecheck?: boolean
}>()
```

**核心逻辑**：
```ts
watch(
  () => [props.modelValue, props.preCheckedIds],
  ([open]) => {
    if (open) {
      checkedIds.value = props.preCheckedIds ? Array.from(new Set(props.preCheckedIds)) : []
    }
  },
  { immediate: true }
)
```

**Alert 降级**（L130-139）：
- 旧：`<ElAlert type="error">` 红条 + 明示「不代表当前生效配置」
- 新：`<ElAlert type="warning">` 黄条（保留覆盖式 PUT 风险提示，移除「不代表当前配置」措辞）
- 加载态：`<ElAlert v-else-if="loadingPrecheck" type="info" :closable="false">正在加载当前分配…</ElAlert>`

### 3. `src/views/Aps/RbacUsers.vue`（打开 Dialog 前拉当前分配）

**改造**（约 20 行）：
```ts
async function openAssignRolesDialog(row: UserSummaryDto): Promise<void> {
  currentTarget.value = row
  assignRolesLoadingPrecheck.value = true
  try {
    const roles = await rbacApi.getUserRoles(row.id)
    preCheckedRoleIds.value = roles.map(r => r.id)
  } catch (err) {
    ElMessage.warning('加载当前角色分配失败，将按未勾选提交')
    preCheckedRoleIds.value = []
  } finally {
    assignRolesLoadingPrecheck.value = false
    assignRolesVisible.value = true
  }
}
```

**类似改造**：openAssignScopesDialog 用 `getUserScopes`。

### 4. `src/views/Aps/RbacRoles.vue`（打开 Dialog 前拉当前分配）

**类似改造**：
- `openAssignPermissionsDialog` 用 `getRolePermissions`
- `openAssignScopesDialog` 用 `getRoleScopes`

### 5. `scripts/verify-integration.mjs` `[B]` 段补 4 断言（RBAC 总数 36 → 40）

---

## 三、关键红线（实现时逐条对照）

| 红线 | 实现点 |
|---|---|
| **预勾选 = 真实当前分配** | 读回失败时**不预勾选**（保持第一版的「错误预勾选比不勾选危险」原则）+ `ElMessage.warning` 提示 |
| **覆盖式 PUT 风险保留** | Alert 降级为 warning 但**不删除**风险描述（只是颜色降级）；二次确认保留 |
| **加载态** | `loadingPrecheck=true` → 顶部「正在加载当前分配…」蓝色提示，避免「空白 Dialog 看起来像未分配」 |
| **写端点覆盖** | 读回失败时已勾选项为空 → 用户提交 = 清空全部；ElMessage.warning 提前警告 |

---

## 四、不动的部分

| 项 | 原因 |
|---|---|
| `lps/` 任何文件 | 4号位 硬约束 |
| `views/Authorization/Role/Role.vue` | 走项目原 `/api/role` mock，不调 3号位 RBAC API |
| `rbacApi` 现有 17 端点 | 仅追加 4 个读回端点，不改写 |
| AssignDialog 二次确认 | 覆盖式 PUT 风险不可消除，二次确认仍必要 |
| `[B]` 段 36 断言 | 仅追加 4 断言，不动现有逻辑 |

---

## 五、验证清单

1. **TS / Lint / Build 三绿**：`pnpm ts:check` / `pnpm lint:eslint` / `pnpm build:pro` 全 0 错误
2. **verify-integration.mjs `[B]` 段**：36 → 40 断言（4 个读回端点各 1 断言），admin 全 200，viewer 全 403
3. **UX 验证**：admin 打开「分配角色」Dialog → 已分配角色自动勾选 → 提交前后 GET 确认一致（避免误删）
4. **失败降级**：手动 mock 读回端点 500 → 顶部「正在加载…」变「加载失败」+ 空白勾选 + 警告消息
5. **二次确认**：覆盖式 PUT 仍触发 `ElMessageBox.confirm`，文案不变
6. **Alert 颜色**：error → warning 降级（视觉上不再像故障，但仍醒目）

---

## 六、相关文档

- [Pkg-8-RBAC完成报告.md](Pkg-8-RBAC完成报告.md)（第一版闭环报告）
- [APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md](APS_V1_3号位契约收口总结汇报_致4号位_v1.0_20260916.md)（3号位 契约收口 + 4 个读回端点落地）
- [4号位-2026-09-16-联调3点回复-给3号位.md](4号位-2026-09-16-联调3点回复-给3号位.md)（联调 3 点回复）
- [rbac.md](rbac.md)（RBAC 矩阵 + dev 验证手册）

---

**作者**：4号位（前端）
**日期**：2026-09-16
**预计工作量**：30-60 min（4 端点 + 1 组件改造 + 2 主页面改造 + 4 断言）