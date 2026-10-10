# 5号位 → 3号位：ResourceCalendar / ManualCapacity 新增权限码申请（2026-09-24）

> **发件**：5号位
> **致**：3号位（权限治理 Owner）
> **抄**：0号位（备查）
> **日期**：2026-09-24
> **依据**：职责裁决 v1.0 §7.1（V1 权限码 Auth DDL 细粒度、3号位 治理）；5号位 ResourceCalendar v1.3 业务 Owner 职责
> **性质**：新模块权限点申请——ResourceCalendar（设备日历）+ ManualCapacity（人工槽）为新增功能模块，需新增专用权限码并纳入 3号位 治理与 Auth 种子落库。

---

## 〇、一句话

5号位 ResourceCalendar 后端开发需要为两个新模块各新增权限码。按团队惯例（Setup / ManualETA 同为"新增码→核心 permissioncodes + SeedService 落库"），请 3号位 确认并落库以下权限点。

---

## 一、背景

- 5号位 将开发 ResourceCalendar 维护接口（设备日历 + 人工能力槽），供 4号位 页面维护。
- 现有 `PermissionCodes`（37 码）**无 ResourceCalendar / ManualCapacity 任何权限点**。
- 团队既有先例：Setup 换型、ManualETA 均为此类"新模块新增专用权限码"（Setup 3 码、ManualETA 3 码），已由 3号位 纳入 Auth DDL / 种子治理。

---

## 二、申请的新权限码

### 模块 A：ResourceCalendar（设备资源日历）—— 3 码

| 权限码 | 名称 | Action | 用途 |
|---|---|---|---|
| `aps.resource_calendar.view` | 查看设备资源日历 | View | 查询设备日历窗口 |
| `aps.resource_calendar.edit` | 维护设备资源日历 | Edit | 批量铺窗 / 修改 |
| `aps.resource_calendar.delete` | 删除设备资源日历 | Execute | 物理删除窗口 |

### 模块 B：ManualCapacity（人工能力槽）—— 3 码

| 权限码 | 名称 | Action | 用途 |
|---|---|---|---|
| `aps.manual_capacity.view` | 查看人工能力槽 | View | 查询人工槽主档/日历 |
| `aps.manual_capacity.edit` | 维护人工能力槽 | Edit | 新增主档 / 批量铺窗 |
| `aps.manual_capacity.delete` | 删除人工能力槽 | Execute | 软删主档 / 物理删窗口 |

---

## 三、请 3号位 配合（按团队惯例执行）

1. **确认权限码命名**（如上，或 3号位 按 Auth DDL 规范调整）；
2. **纳入 `PermissionSeedService` 种子**：新增以上 6 码到（`"aps.resource_calendar.view", "查看设备资源日历", "ResourceCalendar", "View", ...`）等条目，保证启动时自动落库；
3. **同步 Auth DDL / 字段说明**：记录新权限点（对齐 Setup/ManualETA 当时做法）；
4. 若模块/ActionType 需归组，按现有 34/37 码体系归类。

> 说明：5号位 会在 `PermissionCodes.cs` 侧新增对应常量并挂 Controller，但 **Auth 种子与落库由 3号位 治理**（与 Setup 先例一致——码在 `PermissionCodes.cs` 定义 + `PermissionCodes.All` 汇总，种子由 3号位 SeedService 落库）。**请 3号位 尽快落种子，否则新接口登录签发/校验到不到码会 403。**

---

## 四、5号位 侧将做的（等待 3号位 确认后落地）

- `PermissionCodes.cs` 新增 6 个常量（`ResourceCalendarView/Edit/Delete`、`ManualCapacityView/Edit/Delete`）并加入 `All` 清单；
- ResourceCalendarController / ManualCapacityController 按动作挂对应码；
- 编译通过后提供接口给 4号位。

---

**发件**：5号位 ｜ **致**：3号位 ｜ **抄**：0号位 ｜ **日期**：2026-09-24