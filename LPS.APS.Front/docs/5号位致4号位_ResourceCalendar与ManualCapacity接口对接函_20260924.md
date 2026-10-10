# 5号位 → 4号位：ResourceCalendar / ManualCapacity 接口对接函（2026-09-24）

> **发件**：5号位
> **致**：4号位
> **抄**：0号位、3号位（权限码已落）
> **日期**：2026-09-24
> **依据**：0号位 ResourceCalendar v1.3 冻结版 + 权限码回执（3号位 6 码已落）；5号位 ResourceCalendar 后端已完成，编译 0 错误 0 警告
> **性质**：接口对接契约函 —— 供 4号位 前端联调资源日历页维护入口。

---

## 〇、一句话

5号位 ResCalifornia 后端已就绪，提供**设备资源日历** 与 **人工能力槽** 两套维护接口（3号位 已落对应权限码）。本文为 4号位 联调的接口契约。

---

## 一、URL 总览

### 模块 A：设备资源日历（`/api/resource-calendar`）

| 方法 | 路径 | 权限码 | 说明 |
|---|---|---|---|
| POST | `/api/resource-calendar/slots` | `aps.resource_calendar.edit` | **批量铺窗**（一次生成连续多天窗口）|
| GET | `/api/resource-calendar/{resourceId}` | `aps.resource_calendar.view` | 查询某设备所有窗口（可按时间过滤）|
| DELETE | `/api/resource-calendar/slots/{id}` | `aps.resource_calendar.delete` | 物理删除某窗口 |

### 模块 B：人工能力槽（`/api/manual-capacity`）

| 方法 | 路径 | 权限码 | 说明 |
|---|---|---|---|
| POST | `/api/manual-capacity/slots` | `aps.manual_capacity.edit` | 新增人工槽主档 |
| GET | `/api/manual-capacity/slots` | `aps.manual_capacity.view` | 查询人工槽主档（部门/工序过滤，含软删开关）|
| DELETE | `/api/manual-capacity/slots/{manualSlotId}` | `aps.manual_capacity.delete` | **软删**主档（IsActive=0）|
| POST | `/api/manual-capacity/calendar` | `aps.manual_capacity.edit` | 批量铺人工槽窗口 |
| GET | `/api/manual-capacity/calendar/{manualSlotId}` | `aps.manual_capacity.view` | 查询某人工槽所有窗口 |
| DELETE | `/api/manual-capacity/calendar/{id}` | `aps.manual_capacity.delete` | 物理删除某窗口 |

---

## 二、请求 / 响应契约

### 1. 设备批量铺窗 `POST /api/resource-calendar/slots`

**请求体（`ResourceCalendarBulkRequest`）：**
```json
{
  "resourceId": 6444,
  "startDate": "2026-09-25T00:00:00",
  "days": 7,
  "startTime": "08:00:00",
  "endTime": "17:00:00",
  "availableFlag": true,
  "remark": "加班"
}
```
> `days` 范围 1~370；`days=1` 即"逐条加一天"；`endTime` 须 `> startTime`；同一设备逐天生成一个窗口。

**响应：** `ApiResponse<int>`（返回生成窗口数）
```json
{ "code": 0, "success": true, "data": 7 }
```

### 2. 查询设备窗口 `GET /api/resource-calendar/{resourceId}`

**Query 参数：** `from` / `to`（可选，ISO 时间）

**响应（`ResourceCalendarEntryDto[]`）：**
```json
[
  {
    "id": 12345,
    "resourceId": 6444,
    "resourceCode": "1508-1",
    "resourceName": "1508-1",
    "startTime": "2026-09-25T08:00:00",
    "endTime": "2026-09-25T17:00:00",
    "availableFlag": true,
    "remark": "测试默认白班 08-17"
  }
]
```

### 3. 新增人工槽主档 `POST /api/manual-capacity/slots`

**请求体（`ManualCapacitySlotDto`，必填）：**
```json
{
  "productionDepartmentId": 870,
  "operationName": "印4",
  "slotCode": "印4_SLOT_01"
}
```
> 唯一键 `(ProductionDepartmentId, OperationName, SlotCode)`，重复报错；
> 后端 `ManualSlotId` 由库自增生成，返回时带出。

**响应：** `ApiResponse<ManualCapacitySlotDto>`（含生成的 `manualSlotId`）

### 4. 查询人工槽主档 `GET /api/manual-capacity/slots`

**Query 参数：** `departmentId` / `operationName`（可选）、`includeInactive`（默认 false，不含软删）

**响应（`ManualCapacitySlotDto[]`，含 `manualSlotId/departmentName/operationName/slotCode/isActive`）**

### 5. 人工槽铺窗 `POST /api/manual-capacity/calendar`

**请求体（`ManualSlotCalendarBulkRequest`）：** 与设备铺窗同构，`manualSlotId` 为键
```json
{
  "manualSlotId": 18376,
  "startDate": "2026-09-25T00:00:00",
  "days": 7,
  "startTime": "08:00:00",
  "endTime": "17:00:00",
  "availableFlag": true
}
```

### 6. 查询人工槽窗口 `GET /api/manual-capacity/calendar/{manualSlotId}`

**响应（`ManualSlotCalendarDto[]`）：** `id/manualSlotId/startTime/endTime/availableFlag/remark`

### 7. 删除语义提醒（重要）

| 对象 | 方式 |
|---|---|
| 设备窗口 | 物理删（DELETE，行消失）|
| 人工槽主档 | **软删**（DELETE → `IsActive=0`，行保留，查询默认不含）|
| 人工槽窗口 | 物理删（DELETE，行消失）|

---

## 三、联调注意事项

1. **权限码**：6 码均已由 3号位 落种子（43 码）。前端用户需被授予对应权限，否则 403 `Sorry, no permission`.
2. **字段名 PascalCase**（后端项目默认 System.Text.Json 配置），前端 TS 类型按 PascalCase 对齐（与 ManualETA / Overview 先例一致）。
3. **时间**：`startTime/endTime` 为 `TimeSpan`，`startDate` 为 `DateTime`；`availableFlag` 布尔。
4. **软删主档后**：该人工槽 Solver 不再装载（`IsActive=0`）；已有窗口不自动清，前端可自行决定是否清理。
5. **空日历=不可用**：若某资源/人工槽无窗口，Solver 判不可排——前端应展示"Calendar 已配/未配"状态（呼应 v1.3 §九）。

---

## 四、字段契约速查表

| DTO | 字段 |
|---|---|
| `ResourceCalendarEntryDto` | id, resourceId, resourceCode, resourceName, startTime, endTime, availableFlag, remark |
| `ManualCapacitySlotDto` | manualSlotId, productionDepartmentId, departmentName, operationName, slotCode, isActive |
| `ManualSlotCalendarDto` | id, manualSlotId, startTime, endTime, availableFlag, remark |

---

**发件**：5号位 ｜ **致**：4号位 ｜ **抄**：0号位、3号位 ｜ **日期**：2026-09-24