# 5号位 → 3号位：ResourceCalendar / ManualCapacity 6 权限码角色绑定申请（G5 转达，Setup 同范式）

> **发件**：5号位（业务 Owner）
> **致**：3号位（权限治理 Owner）
> **抄**：0号位、2号位、4号位
> **日期**：2026-09-24 ｜ **版本**：v1.0
> **依据**：《3号位致5号位_ResourceCalendar与ManualCapacity权限码回执_v1.0_20260924.md》（6 码已落 seed）+ 4号位《落地缺口与冻结同步_给5号位_20260924》§八
> **性质**：**权限角色绑定申请** —— 6 码已 seed 但无角色绑定，真实模式全角色 403，验收前必须补绑定脚本。

---

## 〇、一句话

6 个权限码（`aps.resource_calendar.*` + `aps.manual_capacity.*`）已由 3号位 落码 + 落种子（43 码），但 **`RolePermission` 无任何绑定**——实测 `aps.admin.system` 也不持有，访问 `/aps/resource-calendar` 全部 403，验收无法进行。请 3号位 按 Setup 同范式补角色绑定脚本。

---

## 一、现状（4号位 09-24 实测证据）

| 项 | 实查结果 |
|---|---|
| 权限码 seed | ✅ `PermissionSeedService.cs:54-62` 已 seed 6 码；日志有"播种权限码成功" |
| 角色绑定 | ❌ `LPS.APS.Web/Sql/` 5 个脚本无任何 `aps.resource_calendar.*` / `aps.manual_capacity.*` 的 `RolePermission`；grep `RolePermission` + 该 6 码 = 0 命中 |
| 实际访问 | ❌ 本机 admin 实测 `GET /api/manual-capacity/slots`、`GET /api/resource-calendar/1` 均 **403**（端点存在非 404） |

> 严重程度等同 Setup 模块当初的 `aps.setup.view` 未绑定。

---

## 二、8 个端点按码分布（供绑定参照）

| 权限码 | 挂载端点 |
|---|---|
| `aps.resource_calendar.view` | `GET /api/resource-calendar/{resourceId}`、`GET /api/resource`、`GET /api/production-departments` |
| `aps.resource_calendar.edit` | `POST /api/resource-calendar/slots` |
| `aps.resource_calendar.delete` | `DELETE /api/resource-calendar/slots/{id}` |
| `aps.manual_capacity.view` | `GET /api/manual-capacity/slots`、`GET /api/manual-capacity/calendar/{manualSlotId}` |
| `aps.manual_capacity.edit` | `POST /api/manual-capacity/slots`、`POST /api/manual-capacity/calendar` |
| `aps.manual_capacity.delete` | `DELETE /api/manual-capacity/slots/{manualSlotId}`、`DELETE /api/manual-capacity/calendar/{id}` |

> 注：`GET /api/resource`、`GET /api/production-departments` 为本轮 G1/G2 新增列表端点，已挂 **ResourceCalendarView** 复用既有码，未新增权限码。

---

## 三、请 3号位 补（2 项建议方向，角色映射裁决权在 3号位）

1. **新增 `Sql/APS_Auth_resource_calendar_role_binding_{version}.sql`**，按 Setup 同范式（**幂等差集补齐**）绑定 6 码。
2. **角色映射建议**（沿用 Setup/mock 先例，3号位 有最终裁决权）：
   - `viewer.management` / `planner`：`view`（planner 是否需 `edit/delete` 请 3号位 定）
   - `admin.aps` / `admin.system`：全 6 码

---

## 四、配合时间

| 动作 | 时间 |
|---|---|
| 角色绑定脚本 | **09-29 前**（4号位 待此切换正式实现） |

---

**发件**：5号位 ｜ **致**：3号位 ｜ **抄**：0号位、2号位、4号位 ｜ **日期**：2026-09-24 ｜ **版本**：v1.0
**本件性质**：权限角色绑定申请（G5 转达）。