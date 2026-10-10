# APS V1 OPM 治理 API（S2/S3）· 3号位 交付 4号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（规则/参数治理、OPM 工艺属性治理 Owner）
**接收人**：4号位（前端配置页面对接）
**抄送**：1号位（OPM 消费）、2号位（落库暂不涉及）
**性质**：**治理 API 契约交付件**（T1）——`RoutingOperation.OperationPlanningMode`（OPM）维护端点，供 4号位 配置页面消费
**依据**：
- 0号位 2026-09-23 裁决 Q1：OPM 属 **APS 工艺规划属性（治理配置）**，唯一主路径 = 治理侧直维护；撤销 ODS 透列/MERGE
- S1《DDL变更申请_v5.1.9终版》（RoutingOperation 加列，已落地）
- S4《无设备小工序_OPM消费契约草案》§五-5（S2/S3 另件，即本单）
- 三值语义：FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY（S4 §2.1 消费映射）

---

## 〇、速览

```
端点   GET  /api/governance/routing-operations?materialId={物料Id}
       PUT  /api/governance/routing-operations/{id}/planning-mode   （body 传 OperationPlanningMode 三态）
权限   GET=aps.rule.view；PUT=aps.rule.edit（复用 Rule 码，零权限种子变更）
主路径 治理侧直维护（3号位 API → 本页面 → 业务拍板值 → RoutingOperation.OperationPlanningMode）
红线   #5 接口即契约：本单为新建治理端点，不改动既有冻结签名；#6 无 DDL（仅 UPDATE 值）
```

---

## 一、端点一：列出工序（GET）

### 1.1 请求

```
GET /api/governance/routing-operations?materialId=100
Authorization: Bearer <token>          # 需 aps.rule.view
```

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| materialId | int | 是 | 物料主键（Material.Id），须 > 0 |

### 1.2 响应 200

```json
{
  "success": true,
  "data": [
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
  ]
}
```

- `operationPlanningMode` 为当前值（三态之一，缺省默认 `FINITE_RESOURCE`）。

### 1.3 错误

| HTTP | 场景 |
|---|---|
| 400 | materialId 缺失或 ≤ 0 |
| 403 | 无 `aps.rule.view` 权限 |

---

## 二、端点二：维护 OPM（PUT）

### 2.1 请求

```
PUT /api/governance/routing-operations/{id}/planning-mode
Authorization: Bearer <token>          # 需 aps.rule.edit
Content-Type: application/json
```

```json
{
  "operationId": 1,
  "operationPlanningMode": "WAIT_ONLY"
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| operationId | long | 是 | 工序主键（RoutingOperation.Id），须与路径 {id} 一致 |
| operationPlanningMode | string | 是 | 三态之一：`FINITE_RESOURCE` / `UNCONSTRAINED` / `WAIT_ONLY` |

### 2.2 响应 200

```json
{
  "success": true,
  "data": {
    "id": 1,
    "materialId": 100,
    "productionDepartmentId": 3,
    "routeCode": "DEFAULT",
    "pathId": 1,
    "operationCode": "OP10",
    "operationName": "精修",
    "processType": "MACHINING",
    "stageCode": "SMT",
    "operationPlanningMode": "WAIT_ONLY"
  }
}
```

### 2.3 错误

| HTTP | 场景 |
|---|---|
| 400 | 路径 id 与 body OperationId 不一致；id 或 materialId 非法 |
| 422 | `operationPlanningMode` 非三态之一（数据红线）|
| 404 | 工序（RoutingOperation.Id）不存在 |
| 403 | 无 `aps.rule.edit` 权限 |

---

## 三、三态值域（业务含义）

| 值 | 语义 | 1号位 消费（S4 §2.1）|
|---|---|---|
| `FINITE_RESOURCE` | 需资源（人工有限产能）| 正常资源找槽；无合格资源 → 业务 Unscheduled（fail-closed）|
| `UNCONSTRAINED` | 无约束（无设备工序）| 跳过资源，叠加 标准工时×数量 到工艺链最早位置，不判资源失败 |
| `WAIT_ONLY` | 仅等待/转运占位 | 跳过资源，生成占位 Task（时间节点），等待默认 0 |

> **维护边界（字段三来源原则，0号位 裁决七）**：OPM 属 APS 治理属性（第三类）→ APS 治理侧维护，**不依赖 MES/ODS 供给**；`sp_SyncRoutingData` 不 MERGE OPM，治理值不被同步覆盖。

---

## 四、红线与对齐

- **红线 #5**：本单新建端点，**不改动任何既有冻结签名/契约**；
- **红线 #6**：仅 UPDATE 值（DML），**无 DDL**（DB 结构变更 2号位 专属不受影响）；
- **权限复用**：view=`aps.rule.view`、edit=`aps.rule.edit`——**不新增权限码**（避免 Auth 种子变更）；
- **审计**：每次 PUT 写统一 AuditLog（EntityType=`RoutingOperation`，记录 OldValue→NewValue），可在审计页追溯。

---

## 五、交付状态

| 项 | 状态 |
|---|---|
| 后端实现（DTO/服务/Controller）| ✅ 已落库（构建 0 错误）|
| 三态校验单测 | ✅ 12/12 通过 |
| 权限码 | 复用 aps.rule.*（已存在）|
| 4号位 对接页面 | 待 4号位 按本契约实现配置页面 |

---

**发送人**：3号位 ｜ **日期**：2026-09-23