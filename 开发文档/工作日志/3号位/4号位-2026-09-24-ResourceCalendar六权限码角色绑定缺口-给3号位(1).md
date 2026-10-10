# 4号位 → 3号位：ResourceCalendar / ManualCapacity 六权限码「已播种、未绑定角色」

> **发送人**：4 号位（前端）
> **接收人**：3 号位（认证 / 用户 / 角色 / 权限 / 审计 Owner）
> **抄送**：5 号位（Resource Calendar 业务 Owner，本项在其 09-24 对接函范围内）
> **日期**：2026-09-24
> **性质**：**需求件**（非回执）——需 3 号位 新增一个角色绑定脚本
> **依据**：
> - 5 号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》（9 端点 / 6 权限码）
> - 冻结文档 v1.3《APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3》§八（4 号位 = 维护页面 Owner）
> - 4 号位 2026-09-24《ResourceCalendar与ManualCapacity落地缺口与冻结同步-给5号位》G5（本函为该项的 Owner 直达件）

---

## 一、事实：6 个权限码已播种

`lps/LPS.APS.Engine/Services/Auth/PermissionSeedService.cs:54-62` 已 seed：

| # | 权限码 | 名称 | 类型 |
|---|---|---|---|
| 1 | `aps.resource_calendar.view` | 查看设备资源日历 | View |
| 2 | `aps.resource_calendar.edit` | 维护设备资源日历 | Edit |
| 3 | `aps.resource_calendar.delete` | 删除设备资源日历 | Execute |
| 4 | `aps.manual_capacity.view` | 查看人工能力槽 | View |
| 5 | `aps.manual_capacity.edit` | 维护人工能力槽 | Edit |
| 6 | `aps.manual_capacity.delete` | 删除人工能力槽 | Execute |

运行时实证（2026-09-24 启动日志 `lps/LPS.APS.Web/logs/info/aps-info-20260924.log:310-315`）：

```
播种权限码成功: aps.resource_calendar.view
播种权限码成功: aps.resource_calendar.edit
播种权限码成功: aps.resource_calendar.delete
播种权限码成功: aps.manual_capacity.view
播种权限码成功: aps.manual_capacity.edit
播种权限码成功: aps.manual_capacity.delete
```

→ **播种这一侧已完成，4 号位 确认无误。**

---

## 二、缺口：无 `RolePermission` 绑定

`lps/LPS.APS.Web/Sql/` 现有 5 个脚本：

```
APS_Auth_v1.3_admin_bootstrap_20260910.sql
APS_Auth_audit_view_role_binding_20260920.sql
APS_Setup_dev_seed_20260920.sql
APS_RulesVue_dev_seed_20260922.sql
APS_VersionChain_dev_seed_20260920.sql
```

**无任何一个脚本绑定上述 6 码**（全库 grep `RolePermission` + 这 6 码 = 0 命中）。

→ 即 `aps.admin.system` 也**不持有**这 6 码。

### 实测证据（2026-09-24，本机以 admin 账号）

```
GET  /api/manual-capacity/slots        → 403
GET  /api/resource-calendar/1          → 403
```

两个端点**存在**（非 404），是权限拒绝。`verify-acceptance.mjs GROUP=r-resource-calendar` 的 RC09 / RC10 同此结果（当前断言接受 403 = 端点可达；绑定落地后可收紧为要求 200）。

### 影响（P0 · 阻塞验收）

4 号位 已按 5 号位 对接函挂路由级门控：

```
router/modules/aps.ts：/aps/resource-calendar
meta.apsRequiredPermissions = ['aps.resource_calendar.view', 'aps.manual_capacity.view']
```

后端类级 `[Authorize(Policy = ...)]` 也要求同码。绑定未落地时：

- **真实模式下所有角色访问 `/aps/resource-calendar` 均 403**，页面进不去；
- 设备资源日历 / 人工能力槽 / 人工槽日历三块功能**无法做端到端验收**（写路径更不可能）；
- 严重程度等同 Setup 模块当初 `aps.setup.view` 未绑定的情形。

---

## 三、请 3 号位 补：角色绑定脚本（1 个）

建议新增（命名随你）：

```
lps/LPS.APS.Web/Sql/APS_Auth_resource_calendar_role_binding_2026092X.sql
```

范式照既有两份（**幂等差集补齐 / 存在即跳过 / 可重复执行 / 头部 `PRODUCTION MUST REMOVE` 标注**，仅供 dev）：

- `APS_Auth_audit_view_role_binding_20260920.sql`（单码 → 多角色）
- `APS_Setup_dev_seed_20260920.sql` 段 A（view 码 → viewer/planner；全码 → admin.aps/admin.system）

---

## 四、请 3 号位 定：角色映射

**4 号位 无角色映射裁决权**，仅给建议（按 Setup 先例 + 本页实际使用角色外推），请以你的裁决为准：

| 角色 | `resource_calendar.*` | `manual_capacity.*` | 建议理由 |
|---|---|---|---|
| `aps.viewer.management` | view | view | 只读监督（同 Setup 先例） |
| `aps.planner` | view + edit + **delete** | view + edit + **delete** | 计划员是资源能力维护的实操角色；且需"先删旧窗再铺窗"（发函 D2：铺窗为追加语义，改时段必须能删） |
| `aps.admin.aps` | 全 3 码 | 全 3 码 | 同 Setup 先例 |
| `aps.admin.system` | 全 3 码 | 全 3 码 | 同 Setup 先例 |
| 其余 3 角色 | — | — | 建议不涉及 |

**说明**：`delete` 对 planner 的建议是我方外推——若你认为 planner 不应持 `delete`（只能编辑、不能删窗口），请直接告知最终映射；4 号位 会同步调整 mock 预置与页面按钮文案口径，避免 mock 演示与真实环境不一致。

> mock 现状（仅本地 UI 演示，**不影响真实环境**）：`store/modules/aps/auth.ts` 已按上表建议临时配置（planner 持全 6 码、admin.* 全码、viewer 仅 view），页面右上角有 mock 角色切换器可验证门控。收到你的最终映射后按实调整。

---

## 五、4 号位 已在 dev 自行解锁（**不替代本函，脚本仍请照补**）

排查中发现 RBAC 页面本身可完成绑定，故 4 号位 已在 dev 自行解开 403，**不再阻塞我方走查**：

```
角色管理（/aps/rbac-roles）→ 分配权限 → PUT /api/rbac/roles/{id}/permissions  body { ids: [...] }
```

但界面绑定是**一次性手工动作**：不落版本库、换机器 / 换 dev·test·prod 环境需重来一遍，无法作为可复现的部署件。**故本函请求的绑定脚本仍请按原计划补**（§三 / §四）。此处仅同步进度，避免你误判该项已无需求。

两条实测坑，供你写脚本 / 走查时参考（4 号位 已踩到）：

1. **该 PUT 是覆盖式**（`rbac.ts` 头部注释：「必传完整 id 数组」）——不是增量追加。若绑定前 `GET /api/rbac/roles/{id}/permissions` 读回失败而盲目提交，该角色**原有权限会被全量清空**（`RbacRoles.vue:274` 为此亮红条警告）。
2. **绑定后必须重新登录**才生效——权限是 JWT claim（`AuthService.cs:276`），`Program.cs:195` `AddPermissionPolicies()` 按 claim 建策略，旧 token 内的码不会变。

---

## 六、时间

| 期望 | 时间 |
|---|---|
| 角色绑定脚本落地 | **09-29 前** |
| 4 号位 复验（RC09/RC10 收紧为要求 200 + 真机走查三 Tab） | 脚本落地后即刻 |

---

**发送人**：4 号位 ｜ **接收人**：3 号位 ｜ **抄送**：5 号位 ｜ **日期**：2026-09-24