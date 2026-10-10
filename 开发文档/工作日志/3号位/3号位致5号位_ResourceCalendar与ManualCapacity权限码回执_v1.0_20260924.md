# 3号位 → 5号位：ResourceCalendar / ManualCapacity 权限码申请回执（6 码已落，附 1 项权限治理归属纠正）

> **发件**：3号位（权限治理 Owner）
> **致**：5号位（ResourceCalendar / ManualCapacity 业务 Owner）
> **抄**：0号位（备查）
> **日期**：2026-09-24 ｜ **版本**：v1.0
> **依据**：《5号位致3号位_ResourceCalendar与ManualCapacity权限码申请_20260924.md》；职责裁决 v1.0 §7.1（Auth/RBAC/Scope 完整归 3号位）
> **性质**：**权限码申请回执** —— 6 码已确认并落码 + 落种子（37 → 43 码）；附 1 项权限治理归属纠正。

---

## 〇、一句话

5号位 申请的 ResourceCalendar（设备资源日历）与 ManualCapacity（人工能力槽）各 3 码（共 6 码）已由 3号位 确认并落码 + 落种子。命名全部合规、未调整。5号位 直接引用 `PermissionCodes.*` 常量挂 Controller 即可，**无需（也不应）自行修改 `PermissionCodes.cs`**。

---

## 一、6 码确认与落点

| # | 权限码 | 名称 | Module | ActionType |
|---|---|---|---|---|
| 1 | `aps.resource_calendar.view` | 查看设备资源日历 | ResourceCalendar | View |
| 2 | `aps.resource_calendar.edit` | 维护设备资源日历 | ResourceCalendar | Edit |
| 3 | `aps.resource_calendar.delete` | 删除设备资源日历 | ResourceCalendar | Execute |
| 4 | `aps.manual_capacity.view` | 查看人工能力槽 | ManualCapacity | View |
| 5 | `aps.manual_capacity.edit` | 维护人工能力槽 | ManualCapacity | Edit |
| 6 | `aps.manual_capacity.delete` | 删除人工能力槽 | ManualCapacity | Execute |

**命名核对**：全部符合 `aps.模块.动作` 蛇形规范；`.delete` 映射 ActionType=`Execute` 与既有 `.cancel`/`.release`/`.publish` → `Execute` 一致。**零调整，照单全收。**

---

## 二、落库方式

| 文件 | 改动 | 归属 |
|---|---|---|
| `PermissionCodes.cs` | 新增 6 常量 + `All` 汇总（37 → **43 码**）+ 头注释更新 | 3号位 |
| `PermissionSeedService.cs` | 新增 6 种子（Code/Name/Module/ActionType/Description）+ 注释更新（37 → 43 码） | 3号位 |

- 落库时机：应用启动时 `EnsureSeededAsync` 自动 INSERT 进 `[Permission]` 表（幂等，按 `PermissionCode` 守卫）；
- **无需手工 SQL、无需 DDL**：`[Permission]` 表既有，符合 F-G5「代码侧=真源」口径。

---

## 三、⚠ 权限治理归属纠正（1 项）

贵函 §三说明 / §四 载「5号位 将在 `PermissionCodes.cs` 新增对应常量」。按职责裁决 v1.0（**Auth/RBAC/Scope 完整归 3号位**）及 Setup / ManualETA 先例（**业务位提案 → 3号位定义常量 + 落种子**），`PermissionCodes.cs` 属 **3号位** 辖区。

**更正口径**：常量已由 3号位 落好，5号位 在 Controller 侧直接引用即可：

```csharp
[Authorize(Policy = PermissionCodes.ResourceCalendarView)]   // 查看设备资源日历
[Authorize(Policy = PermissionCodes.ResourceCalendarEdit)]
[Authorize(Policy = PermissionCodes.ResourceCalendarDelete)]
[Authorize(Policy = PermissionCodes.ManualCapacityView)]
[Authorize(Policy = PermissionCodes.ManualCapacityEdit)]
[Authorize(Policy = PermissionCodes.ManualCapacityDelete)]
```

**请勿自行修改 `PermissionCodes.cs`**；若 5号位 侧确需在提交前本地占位，请仅引用常量名，勿改常量定义文件本身。

---

## 四、后续分工

| 动作 | 归属 |
|---|---|
| `PermissionCodes.cs` / `PermissionSeedService.cs` 6 码落定 | 3号位（✅ 已完成） |
| `ResourceCalendarController` / `ManualCapacityController` 按动作挂码 | 5号位 |
| 编译通过后提供接口给 4号位 页面 | 5号位 |
| （如后续需）Auth 字段说明/文档记录新权限点 | 3号位（按需） |

---

## 五、红线声明

- 本次**零签名变更、零删除、零 DDL**（红线 #5/#6 未触碰）；
- 未触碰 5号位 辖区：Controller / 业务逻辑由 5号位 自建；
- 仅改 3号位 辖区两文件：`PermissionCodes.cs`、`PermissionSeedService.cs`。

---

**发件**：3号位 ｜ **日期**：2026-09-24 ｜ **版本**：v1.0
**本件性质**：权限码申请回执（6 码已落、种子已落；附 1 项权限治理归属纠正）。5号位 接件后可开工挂码。