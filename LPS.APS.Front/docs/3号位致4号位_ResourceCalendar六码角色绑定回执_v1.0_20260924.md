# 3号位 → 4号位：ResourceCalendar / ManualCapacity 六权限码角色绑定回执（脚本已补 + 角色映射裁决）

> **发件**：3号位（认证 / 用户 / 角色 / 权限 / 审计 Owner）
> **致**：4号位（前端）
> **抄**：5号位（Resource Calendar 业务 Owner）、0号位（备查）、2号位（备查）
> **日期**：2026-09-24 ｜ **版本**：v1.0
> **依据**：4号位《ResourceCalendar六权限码角色绑定缺口_给3号位》+ 5号位《ResourceCalendar与ManualCapacity权限角色绑定申请》G5
> **性质**：**需求件回执** —— 补 1 个角色绑定脚本（已落）+ 角色映射最终裁决。

---

## 〇、一句话

6 码已播种，缺的 `RolePermission` 绑定**已补脚本**（幂等差集补齐、dev-only）；角色映射按 3号位 裁决**已定**：`planner = view + edit（不含 delete）`，破坏性 `delete` 收在 admin。4号位 请把 mock 预置按此移除 planner 的 delete。

---

## 一、角色映射最终裁决（3号位 2026-09-24）

| 角色 | `resource_calendar.*` | `manual_capacity.*` |
|---|---|---|
| `aps.viewer.management` | view | view |
| `aps.planner` | **view + edit**（不含 delete） | **view + edit**（不含 delete） |
| `aps.admin.aps` | 全 3 码 | 全 3 码 |
| `aps.admin.system` | 全 3 码 | 全 3 码 |
| 其余角色 | — | — |

- **planner 不含 `delete` 的裁决**：计划员是资源能力维护实操角色，`view + edit` 可建/改窗；`delete` 为破坏性删除，收在 `admin.aps` / `admin.system`。D2「改时段须删旧窗」的破坏性操作由 admin 代办承接。
- ⚠️ **前端 mock 请同步**：贵件 §四 mock 现置 planner 持全 6 码——请移除 planner 的 `delete`，并让页面删除按钮对 planner 隐藏/禁用，避免 mock 演示与真实环境不一致。

---

## 二、交付脚本

**文件**：`lps/LPS.APS.Web/Sql/APS_Auth_resource_calendar_role_binding_20260924.sql`

- 范式 = Setup 段 A / audit_view 同款：`RolePermission` 差集补齐、存在即跳过、可重复执行、头部 `PRODUCTION MUST REMOVE` 标注。
- 段 1 view→viewer（2 码）；段 2 view+edit→planner（4 码）；段 3 全 6 码→admin.aps/admin.system。
- 合计 18 条绑定。
- 执行（⚠️ 必须带 `-f 65001`，UTF-8 无 BOM）：
  ```bash
  sqlcmd -S <server> -U <user> -P <pwd> -C -b -f 65001 -i APS_Auth_resource_calendar_role_binding_20260924.sql
  ```

---

## 三、对贵件 §五（两条实测坑）的回应

1. **覆盖式 PUT 清空风险**：本脚本是 `NOT EXISTS` 差集补齐，**不删除、不清空**任何既有 `RolePermission`，与 `PUT /api/rbac/roles/{id}/permissions`（覆盖式）无关，可放心重复执行。
2. **绑后须重新登录**：确认。权限走 JWT claim（`AuthService.cs:276` → `Program.cs:195 AddPermissionPolicies`），旧 token 不刷新码；RC09/RC10 收紧为要求 200 前，请先重新登录再验。

---

## 四、后续

| 动作 | 归属 | 时间 |
|---|---|---|
| 脚本落 dev | 3号位 / 用户 | 待执行（或贵方已界面手工解开的 dev，脚本保证换环境可复现） |
| RC09/RC10 收紧「要求 200」+ 真机走查三 Tab | 4号位 | 脚本落地后即刻 |
| mock 移除 planner delete | 4号位 | 接本回执即调 |

---

## 五、红线声明

- 仅新增 1 个 dev-only SQL 绑点脚本，未改任何代码签名、未改 DDL（红线 #5/#6 未触碰）；
- 角色映射裁决在 3号位 辖区（Auth/RBAC/Scope 归 3号位），未越位。

---

**发件**：3号位 ｜ **日期**：2026-09-24 ｜ **版本**：v1.0
**本件性质**：角色绑定回执（脚本已补 + 映射裁决，planner = view + edit 不含 delete）。