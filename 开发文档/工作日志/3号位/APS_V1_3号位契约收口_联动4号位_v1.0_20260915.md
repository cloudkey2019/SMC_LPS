# APS_V1 3号位契约收口_联动4号位_v1.0_20260915

> 致：4号位
> 由：3号位
> 日期：2026-09-15
> 主题：《3号位契约-依赖与扩展项(1)》②⑥ 两契约点的前端联动（3 项）

## 背景

3号位 已收口契约 ②（DomainDefinition 字段契约 = 方案A）与 ⑥（Governance 响应统一 = ApiResponse&lt;T&gt;）。以下 3 项需 4号位 前端配合，请按序落地。

---

## ① Governance API 响应统一（契约⑥）

后端 `GovernanceController` 全部端点（~45 个）响应体已从三套格式统一为 `ApiResponse<T>`：

- 成功：`{ "code": 200, "message": "success", "data": <原数据>, "timestamp": "…", "traceId": null }`
- 失败：`{ "code": 400 | 404, "message": "<错误信息>", "data": null, … }`

**前端动作：**

1. `apsHttp` 删除对 Governance 的「双解包」兼容分支，统一按 `code` + `message` 解析。
2. 失败提示字段由 **`error` → `message`**（原 `body.error` 改读 `body.message`）。
3. `data` 字段位置与内容不变，仅外层信封由 `{ success, data }` 变为 `{ code, message, data }`。
4. 特殊：`POST /api/governance/run/{id}/recover` 的 `data` 仍为 `{ "newScheduleRunId": <int> }`（未改名）。

## ② Domain 维护 DTO 字段类型（契约② = 方案A）

`types/domain.ts`：

- `productFamilyId`：`string` → **`number`**
- `factoryId`：保持可空，类型 **`number | undefined`**

后端不再做字符串兼容（方案A，前端为数字选择器）。

## ③ Domain 维护下拉数据源（契约②）

新增两个只读端点，Domain 维护页改用它们渲染下拉，用数字 `id` 绑定 `productFamilyId` / `factoryId`：

| 端点 | `data` 元素字段 |
|---|---|
| `GET /api/governance/product-families` | `id, code, name, description, isActive, createdAt, updatedAt` |
| `GET /api/governance/factories` | `id, code, name, location, timeZone, isActive, createdAt, updatedAt` |

两者均按 `code` 稳定升序、返回全量（含停用），前端自行按 `isActive` 过滤。

---

## 验证

- Swagger `/swagger`（governance 分类）可直接查看统一后的响应样例，无需启动联调即可核对信封结构。