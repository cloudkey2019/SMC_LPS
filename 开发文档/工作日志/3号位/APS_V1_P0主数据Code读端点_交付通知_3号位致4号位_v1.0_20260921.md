# P0 主数据 Code→Id 读端点 —— 交付通知（3号位 致 4号位）

> **收件**：4号位（前端，Setup 三 Dialog）
> **抄送**：0号位（PM）、2号位（数据引擎，知会）
> **日期**：2026-09-21
> **依据**：0号位 裁决「3号位 直接读源表，不经 2号位/0号位」；API 规范 **v2.5 附录D**（契约权威登记）；Setup 换型 Id 口径终定
> **性质**：**交付通知**——Setup 三 Dialog（部门/设备/物料下拉）真实数据源就绪，请 4号位 接入、撤 mock 硬编码
> **术语约定**：英文契约词一律「中文名称（英文）」；代码引 `文件:行`

---

## 〇、一句话

`api/governance/lookups/*` 三个 **Code→Id 下拉读端点**已交付（build 0 错 0 警，契约登记 API 规范 v2.5 附录D）。Setup 三 Dialog 请换用，**masterId=表主键 Id（前端 value / 后端落 Id）、code=编码（label 主源）**。

## 一、交付端点（Setup 三 Dialog 数据源）

| 端点 | 查询参数 | 响应 data 元素（合成示例） |
|---|---|---|
| `GET api/governance/lookups/departments` | `search` 可选，模糊 DeptCode | `{ "masterId": 1, "code": "DEPT-01", "stageCode": "S1" }` |
| `GET api/governance/lookups/resources` | `search` 可选，模糊 ResourceCode/ResourceName | `{ "masterId": 12, "code": "RES-01", "name": "CNC-1", "productionDepartmentId": 1 }` |
| `GET api/governance/lookups/materials` | `search` 可选（MaterialCode/MaterialName/Spec）；`activeOnly` 可选，true 仅活动 | `{ "masterId": 1001, "code": "MAT-01", "name": "原料A", "spec": "Φ10", "uom": "KG", "isActive": true }` |

- 统一：`ApiResponse<T>` 信封，数据在 `data` 数组；权限 `aps.setup.view`。
- 全量返回（含 200 上限），当前未做分页——主数据量级可直接全量渲染；如需分页后续可加。

## 二、口径要点（用户裁决，务必遵循）

1. **masterId = 表主键 Id**——下拉 `value` 用 `masterId`，提交/请求业务字段（`departmentId`/`resourceId`/`materialId`）用 Id（Setup 换型 Id 口径终定一致）。
2. **code = 编码**（`DeptCode`/`ResourceCode`/`MaterialCode`）——下拉 `label` 主源，可拼 `name`/`spec`/`uom` 做展示。
3. **红线 #4**：物料 **始终返回列表**，code 命中多条由前端/优先级判定（物料可能重码：多版本/多工厂/历史软删）；建议物料下拉按 `isActive` 优先展示活动项、活动项置灰可辨识或直接 `activeOnly=true` 过滤。
4. 部门/设备当前未过滤启用态（源表无统一 `IsActive` 语义列），全量返回、由前端展示。

## 三、请 4号位 动作

1. Setup 三 Dialog（部门/设备/物料）数据源切换为上述端点，**撤 mock 硬编码**；
2. 下拉 `value` 落 `masterId`（Id）、请求体仍传 Id 字段（对齐 Setup 换型 13 端点契约）；
3. 契约以 API 规范 **v2.5 附录D** 为准；如遇字段疑义回执本函。

## 四、回执表

| 问 | 答复 |
|---|---|
| 三 Dialog 接入是否顺畅（字段/权限/响应结构） | （待 4号位） |
| mock 撤除清单（哪些页面/组件） | （待 4号位） |

---

**发送人**：3号位 ｜ **日期**：2026-09-21
**待办**：① 4号位 接入 + 回执 ② mock 撤除确认 ③（如分页/启用态过滤需要）3号位 补参数
