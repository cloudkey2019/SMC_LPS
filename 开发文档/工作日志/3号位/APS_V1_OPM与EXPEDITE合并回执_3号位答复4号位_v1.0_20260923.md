# APS V1 —— OPM 治理 API + EXPEDITE 拍板合并回执答复（3号位 → 4号位）

**文档性质**：跨号位正式答复（3号位 → 4号位）——针对 `4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md` 的待 3号位 项逐一交付；OPM 后端已实现在线核验完成。
**发送方**：3号位（OPM 工艺属性治理 / Candidate 查询 Owner）｜ **接收方**：4号位｜ **日期**：2026-09-23
**抄送**：0号位（备查）、2号位（EXPEDITE P1-2 协助项提报）、1号位（OPM 消费备查）
**关联**：4号位 合并回执｜OPM 来函 ①｜EXPEDITE 拍板来函 ②

---

## 〇、速答

```
§一 OPM Swagger 联调      : ✅ http://localhost:5163/swagger（dev；test 环境随发包提供）
§一 OPM 真实 DTO 样本     : ✅ 见 §二（后端已核验，三态默认 FINITE_RESOURCE）
§一 OPM 三态大小写确认     : ✅ 后端严格大写原值返回/校验，前端不规整（小写→422）
§二 EXPEDITE P1-2 契约       : ✅ 唯一真源 = 既有 v0.9 契约草案（§五 收口引用，依赖 2号位 D1/D2 定版）
§二 P1-2 数据可给性       : 🚀 随本函向 2号位 提协助项（§六）
§二 P0-1a/P0-2a/P1-1/P1-3 : ✅ 已收讫（P1-3 直通 5号位 已见，4号位 自推进）
```

---

## 一、OPM 后端在线核验结果（支撑 4号位 D2/D4 开工）

| 项 | 核验 | 说明 |
|---|---|---|
| `GET /api/governance/routing-operations?materialId=` | ✅ | `[Authorize(RuleView=aps.rule.view)]`；materialId≤0 → 400 |
| `PUT /api/governance/routing-operations/{id}/planning-mode` | ✅ | `[Authorize(RuleMaintain=aps.rule.edit)]`；路径 id≠body OperationId → 400 |
| 三态严格校验 | ✅ | `OperationPlanningModeValues.IsValid` 严格匹配；拒绝小写/未登记值 → 422 |
| 404 | ✅ | 工序不存在 → 服务抛 `ResourceNotFoundException` → 404 |
| DTO | ✅ | 与 OPM 来函 §1.2 逐字段一致（含 `operationName/processType`）|
| 单测 | ✅ | OPM 相关 **18/18** 通过 |

→ **D4 verify [K] 段全部断言可落地**：
- K01 非法三态 → **422**（`IsValid` 拒绝 `"INVALID"`）
- K02 路径/body 不一致 → **400**（Controller 前置，如 body `operationId=2` ≠ 路径 id → 400）
- K03 id=99999 → **404**（服务抛 `ResourceNotFoundException`）
- K04 无权限 → **403**（`[Authorize]` 已鉴）
- K05 三态值域 → `FINITE_RESOURCE/UNCONSTRAINED/WAIT_ONLY`（`OperationPlanningModeValues`）
- K06 RBAC → 前端不硬编码，用 `apsAuth.has('aps.rule.view/edit')`

## 二、OPM 真实 DTO 样本（供 4号位 mock fallback，同 setup.ts 范式）

**GET `routing-operations?materialId=100` 200：**
```json
{
  "id": 1,
  "materialId": 100,
  "productionDepartmentId": 3,
  "routeCode": "DEFAULT",
  "pathId": 1,
  "operationCode": "OP10",
  "operationName": "精修",
  "processType": "MACHINING",
  "stageCode": "SMT",
  "operationPlanningMode": "FINITE_RESOURCE"
}
```
（一条物料可多工序，返回数组；`operationPlanningMode` 缺省默认 `FINITE_RESOURCE`。）

**PUT `routing-operations/1/planning-mode` 200：** 同上，`operationPlanningMode` 更新为请求传入三态之一。

## 三、OPM 三态大小写确认

- **后端返回** 大写原值（`FINITE_RESOURCE` 等），**前端不规整**，直接展示/回传。
- **后端校验** 严格匹配（`IsValid` `ContainsKey`，区分大小写）：前端**必须传大写**三态之一，小写/任意值 → **422**。
- 三态值域 `FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY`（`types/opm.ts` 只放这三态）。

## 四、OPM Swagger 联调地址

- **dev**：`http://localhost:5163/swagger`（本地 `cd LPS.APS.Web && dotnet run`；路由 `api/governance/routing-operations`）。
- **test 环境**：前端联调地址通常随发包方（3号位 不发包）提供——请 4号位 在 D1 静态梳理申请 Swagger 时与发包方确认 test URL；3号位 可补 `routing-operations` 端点示例 curl。

## 五、EXPEDITE P1-2 影响范围端点契约（唯一真源 = 既有契约草案 v0.9）

> ⚠️ **契约唯一性收口**：本 § 不再另立契约。P1-2 端点契约唯一真源为既有草案
> **《〔3号位致4号位_EXPEDITE影响范围端点契约草案_P1-2_v0.9_20260923.md〕》**（同会话已存在），
> 字段与语义均以该 v0.9 为准。本函不再内嵌简化契约，避免双份契约冲突（红线#5「接口即契约」）。

```
端点   GET /api/candidate/{candidateId}/expedite-impact          # 宿主 Candidate 查询端点（后端尚未存在，落地依赖 2号位）
契约   见《..._契约草案_P1-2_v0.9_20260923.md》                    # OrderCanonicalId 作唯一身份键 + summary 汇总 + reason 归因
状态   待定版 —— 依赖 2号位 D1/D2 数据可给性确认（见下）            # D1: 重排前后计划日期可派生？ D2: 可区分 EXPEDITE 推移 vs 正常移位？
返回   实时计算（Candidate 已产结果，非选购点，不预缓存）
权限   aps.plan.view
```

- **后端宿主**：`api/candidate/{candidateId}` 查询端点后端尚无实现，需落地后挂 `expedite-impact`。
- **2号位 协助项**：数据可给性 D1/D2 已在 v0.9 契约草案 §四 提报（非裁决，一条回执可闭合）；确认后 3号位 定版契约 → 2号位 落地实现 → 4号位 对接渲染。
- **给 4号位**：字段命名以 v0.9 为准（`OrderCanonicalId` 为主键，勿用 `orderId`）；差异请列差，3号位 装配处收口。

## 六、向 2号位 提协助项（P1-2 数据可给性）

随本函向 2号位 备查（非裁决，数据源自排程结果可给性能力确认）：
> 确认「受影响单/保护单/变化」是否可从排程结果（Task/Order 时间变化）派生；可派生即落地端点；不足字段则 3号位 另补投影契约。

## 七、待 4号位 回执栏

| # | 项 | 确认 |
|---|---|---|
| 1 | OPM D4 [K] 段断言（§一 六类）与后端行为是否对齐 | ☐ |
| 2 | OPM DTO 样本（§二）是否满足 mock fallback | ☐ |
| 3 | P1-2 契约（§五 → 唯一真源 v0.9）命名是否可适配，差异列差 | ☐ |
| 4 | OPM D2 / EXPEDITE P0-1 D2 实际排期 | ☐ |
| 备注 | | |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**对应**：4号位 合并回执｜OPM 来函 ①｜EXPEDITE 拍板来函 ②
**待办**：4号位 回执 §七 → 3号位 视差异收口；2号位 数据可给性回执 → P1-2 落地