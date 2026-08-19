# APS 应用层API接口规范 v2.4

**版本**：v2.4  
**日期**：2026-05-13  
**基于**：《APS数据架构与防腐层设计方案 v1.20》+ 《APS权限与审批系统实施方案 v1.0》+《APS_数据库字段说明文档 v5.0.25》+《APS DDL v5.0.25》  
**技术栈**：ASP.NET Core Web API + RESTful + Swagger + JWT  
**更新**：新增排程运行管理 API + PlanVersion 激活 API + 读模型查询 API（对齐 DDL v5.0.25 / 总表 v3.17）

---

## 📋 v2.4更新说明（2026-05-13 排程运行管理 + PlanVersion 激活 + 读模型查询）

### 新增排程运行管理 API
- `POST /api/scheduling/runs` — 触发新 ScheduleRun（阅读下方 §六。一）
- `GET /api/scheduling/runs` — 历史运行记录列表
- `POST /api/scheduling/plan-versions/{id}/activate` — 激活 CANDIDATE 版本

### 新增读模型查询 API
- `GET /api/scheduling/plan-versions/{id}/kpi` — `PlanKpiSummary`
- `GET /api/scheduling/plan-versions/{id}/order-summary` — `OrderScheduleSummary`（分页）
- `GET /api/scheduling/plan-versions/{id}/resource-load` — `ResourceLoadSummary`（按资源/日期过滤）

### 版本引用更新
- 防腐层 v1.15 → v1.20；字段说明 v5.0.16 → v5.0.25；DDL v5.0.16 → v5.0.25

---

## 📋 v2.3更新说明（2026-04-29 生产部门主链 + ProcessCodeDict 重定位）

### 新增字典 CRUD API（3 类）

#### 1. `ProductionDepartment` 字典 CRUD（v5.0.16 新增）

| 方法 | 路径 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/dictionary/production-departments` | 列表（按 FactoryId / StageCode 过滤） | RBAC: dict.read |
| GET | `/api/dictionary/production-departments/{id}` | 详情 | RBAC: dict.read |
| POST | `/api/dictionary/production-departments` | 新增（**走 0 号位审批流**） | RBAC: dict.write + 审批 |
| PUT | `/api/dictionary/production-departments/{id}` | 修改（**走审批**；StageCode 改动需双重确认） | RBAC: dict.write + 审批 |
| DELETE | `/api/dictionary/production-departments/{id}` | 软删除（IsActive=0；走审批） | RBAC: dict.write + 审批 |

**业务校验**：StageCode 1:1 归属（同一 DeptCode 不允许多 StageCode）；FactoryId 可空；StageCode 必须存在于 StageDict。

#### 2. `ProcessCodeDict` 字典 CRUD（v5.0.16 翻转：从自动同步改为人工维护）

| 方法 | 路径 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/dictionary/process-codes` | 列表（按 FactoryCode / StageCode / IsActive 过滤） | RBAC: dict.read |
| GET | `/api/dictionary/process-codes/{processCode}` | 详情 | RBAC: dict.read |
| POST | `/api/dictionary/process-codes` | 新增（**APS 系统管理员录入** + 0 号位审批） | RBAC: admin + 审批 |
| PUT | `/api/dictionary/process-codes/{processCode}` | 修改（含 StageCode 增强列改动；走审批） | RBAC: admin + 审批 |
| DELETE | `/api/dictionary/process-codes/{processCode}` | 软删除（IsActive=0；走审批） | RBAC: admin + 审批 |

**业务校验**：`CodeOrigin` ∈ {ERP, MES, MANUAL}；StageCode 软引用 StageDict（不强 FK 但导入时校验）。

#### 3. `MaterialStageDeptOverride` CRUD（v5.0.16 新增）

| 方法 | 路径 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/material-stage-dept-overrides` | 列表（按 Model / MaterialCode / StageCode 过滤；只列 IsCurrent=1） | RBAC: dict.read |
| GET | `/api/material-stage-dept-overrides/{id}` | 详情 | RBAC: dict.read |
| POST | `/api/material-stage-dept-overrides` | 新增（业务录入 / 批量导入） | RBAC: dict.write |
| **POST** | `/api/material-stage-dept-overrides/import` 🆕 | 批量导入（Excel/CSV）；**导入侧做 Model → MaterialCode 1:1 检查**；1:N 拒收并返回明细 | RBAC: dict.write |
| PUT | `/api/material-stage-dept-overrides/{id}` | 修改（SCD Type 2：关闭旧版本 + 插入新版本） | RBAC: dict.write |
| DELETE | `/api/material-stage-dept-overrides/{id}` | 软删除（IsCurrent=0） | RBAC: dict.write |

**导入响应示例（Model 1:N 拒收）**：
```json
{
  "status": "REJECTED",
  "rejectedRows": [
    {
      "row": 12,
      "model": "MGGMB40",
      "stageCode": "BJ_MACH",
      "reason": "Model maps to multiple MaterialCode",
      "ambiguousMaterialCodes": ["MGGMB40-450", "MGGMB40-500", "MGGMB40-600"]
    }
  ]
}
```

### 新增 Context 重建 API（v5.0.16 新增）

⚠️ **实现状态说明**：底层 `sp_RebuildMaterialStageDeptContext` 当前为**占位骨架，未实现**（DDL Step1~6 全 TODO）。下表 POST `/rebuild` 端点设计已定，但**实装前请勿在生产调用**，调用会执行空逻辑并立即返回；GET 端点可正常查询（默认空表）。

| 方法 | 路径 | 说明 | 权限 | 状态 |
|---|---|---|---|---|
| POST | `/api/material-stage-dept-context/rebuild` | 触发 `sp_RebuildMaterialStageDeptContext`；body 含 `triggerMode` ∈ {FULL, INCR, PARTIAL} 与 PARTIAL 过滤参数 | RBAC: scheduler + 审批 | ⚠️ 占位未实现 |
| GET | `/api/material-stage-dept-context` | 查询当前有效 Context（按 MaterialId / StageCode / DeptId 过滤；分页） | RBAC: dict.read | ✅ 可用 |
| GET | `/api/material-stage-dept-context/{materialId}/{stageCode}` | 单条详情 | RBAC: dict.read | ✅ 可用 |

### 新增 Issues 复核 API（v5.0.16 新增）

| 方法 | 路径 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/material-stage-dept-context-issues` | 列表（按 BatchNo / IssueType / Severity / ReviewStatus 过滤；默认 ReviewStatus=PENDING） | RBAC: dict.read |
| PATCH | `/api/material-stage-dept-context-issues/{id}/review` | 复核状态扭转（PENDING → CONFIRMED / IGNORED / FIXED） | RBAC: reviewer |

### Resource API 字段调整

- 🔄 Resource 列表/详情响应：DROP `workshopCode` + ADD **`productionDepartmentId`** + **`sourceProductionDeptCode`**
- 🔄 Routing 三件套相关 API（如有）响应：ADD **`productionDepartmentId`**
- 🔄 MSC 相关 API 响应：ADD **`defaultProductionDepartmentId`**

### 1 号位排程查询 API 红线

排程查询 API（如已存在）**禁止暴露**直接读 MSC / ProcessCodeDict / MaterialStageDeptOverride 的端点；查询应通过 `MaterialStageDeptContext` 间接进行。

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

## 📋 v2.2更新说明

### **v2.2核心变更**（2026-03-24）：
1. ✅ 新增**用户认证API**（登录、登出、Token刷新）
2. ✅ 新增**用户管理API**（用户CRUD、角色分配、数据范围分配）
3. ✅ 新增**角色权限管理API**（角色CRUD、权限分配）
4. ✅ 新增**审批流API**（发起审批、审批操作、查询审批）
5. ✅ 新增**审计日志API**（查询审计日志）
6. ✅ 所有业务API新增权限校验说明

### **v2.1核心变更**（2026-03-19）：
1. ✅ 新增**主数据同步API**（MaterialMapping同步、Routing同步、执行时机说明）
2. ✅ 补充**Socket-Plug职责分工说明**
3. ✅ 更新文档引用（基于防腐层设计v1.1）

### **v2.0核心变更**（2026-03-10）：
1. ✅ 新增**ODS库批次管理API**（批次BOM展开请求、状态查询、实时展开）
2. ✅ 新增**快照管理API**（快照封存、读取、完整性校验）
3. ✅ 新增**物料映射API**（MaterialMapping同步、时间点查询）
4. ✅ 新增**库存来源优先级配置API**
5. ✅ 修改**Planning API**（增加BatchNo关联）

### **架构红线**：
- ⚠️ ODS API仅供APS内部调用，不对外暴露
- ⚠️ BOM展开的Quantity字段必须是单位用量，不累乘
- ⚠️ 快照文件存储在4.76T机械硬盘，不占用SSD

**相关文档**：
- **《APS_各类基础数据分层承接与演变总表_v3》**：数据演进全景图
- **《APS_数据架构与防腐层设计方案_v1.1》**：防腐层设计详解
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：Socket-Plug职责分工

---

## 一、ODS库批次管理API（内部API）

### 1.1 请求批次BOM展开

**端点**：`POST /api/internal/v1/ods/bom/batch/request`

**描述**：触发批次BOM展开（发令枪模式）

**⚠️ 架构红线（修改日期：2026-03-11）**：
- **严禁在HTTP JSON中传输80万BOMNO数组**（会导致15-20MB报文，引发LOH内存分配和Full GC）
- **必须先通过SqlBulkCopy写入**：APS必须先通过ADO.NET的SqlBulkCopy，将80万个BOMNO直接写入ODS库的`MES_API_BOM_Request_Detail`表（耗时<1秒）
- **API仅作为发令枪**：此接口纯粹是一个"触发指令"，不传输数据

**调用前置条件**：
1. APS已通过SqlBulkCopy将BOMNO写入`MES_API_BOM_Request_Detail`表
2. 确保`MES_API_BOM_Request_Detail.BatchNo = {batchNo}`的记录已存在

**请求**：
```json
{
  "batchNo": "REQ_20260310_01",
  "rootCount": 800000,
  "requestedBy": "APS_SCHEDULER",
  "instruction": "请确保已通过SqlBulkCopy写入Detail表后再调用此接口触发CTE展开"
}
```

**注意**：已删除`bomnoList`字段，数据已通过SqlBulkCopy直接写入数据库

**响应**：
```json
{
  "code": 200,
  "message": "BOM展开请求已提交",
  "data": {
    "batchNo": "REQ_20260310_01",
    "rootCount": 800000,
    "status": "PENDING",
    "estimatedDuration": 900,
    "createdAt": "2026-03-10T00:00:05Z"
  },
  "timestamp": "2026-03-10T00:00:05Z"
}
```

---

### 1.2 查询批次展开状态

**端点**：`GET /api/internal/v1/ods/bom/batch/{batchNo}/status`

**描述**：查询批次BOM展开状态（批次状态机）

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "batchNo": "REQ_20260310_01",
    "status": "READY",
    "rootCount": 800000,
    "expandedRowCount": 3500000,
    "createdAt": "2026-03-10T00:00:05Z",
    "processingStartTime": "2026-03-10T00:05:00Z",
    "completedAt": "2026-03-10T00:20:00Z",
    "processingDuration": 900,
    "retryCount": 0,
    "errorMessage": null
  },
  "timestamp": "2026-03-10T00:25:00Z"
}
```

**状态机说明**：
- `PENDING`：已提交，等待处理
- `PROCESSING`：正在展开
- `READY`：展开完成，可拉取
- `CONSUMED`：已被APS拉取消费
- `FAILED`：展开失败

---

### 1.3 拉取批次BOM数据

**端点**：`POST /api/internal/v1/ods/bom/batch/{batchNo}/pull`

**描述**：APS从ODS库拉取展开完成的BOM数据（使用SqlBulkCopy在应用层执行）

**请求**：
```json
{
  "batchNo": "REQ_20260310_01",
  "targetDatabase": "APS_Production",
  "targetTable": "APS_BOM_RAW",
  "pulledBy": "APS_SCHEDULER"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "BOM数据拉取完成",
  "data": {
    "batchNo": "REQ_20260310_01",
    "rowCount": 3500000,
    "pulledAt": "2026-03-10T00:25:00Z",
    "status": "CONSUMED"
  },
  "timestamp": "2026-03-10T00:25:30Z"
}
```

---

### 1.4 请求实时BOM展开（紧急插单）

**端点**：`POST /api/internal/v1/ods/bom/realtime/request`

**描述**：白天紧急插单时，实时展开单个BOMNO

**请求**：
```json
{
  "bomno": "BOM-NEW-001",
  "priority": 1,
  "requestedBy": "APS_INSERTORDER"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "实时BOM展开请求已提交",
  "data": {
    "requestId": 12345,
    "bomno": "BOM-NEW-001",
    "status": "PENDING",
    "priority": 1,
    "estimatedDuration": 300,
    "createdAt": "2026-03-10T14:00:00Z"
  },
  "timestamp": "2026-03-10T14:00:00Z"
}
```

---

### 1.5 查询实时BOM展开状态

**端点**：`GET /api/internal/v1/ods/bom/realtime/{requestId}/status`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "requestId": 12345,
    "bomno": "BOM-NEW-001",
    "status": "READY",
    "expandedRowCount": 150,
    "requestTime": "2026-03-10T14:00:00Z",
    "completedTime": "2026-03-10T14:05:00Z",
    "retryCount": 0,
    "errorMessage": null
  },
  "timestamp": "2026-03-10T14:05:30Z"
}
```

---

### 1.6 获取ODS库批次列表

**端点**：`GET /api/internal/v1/ods/bom/batch/list`

**查询参数**：
- `status`：状态（PENDING/PROCESSING/READY/CONSUMED/FAILED）
- `startDate`：开始日期
- `endDate`：结束日期
- `pageIndex`：页码
- `pageSize`：每页数量

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "items": [
      {
        "batchNo": "REQ_20260310_01",
        "status": "CONSUMED",
        "rootCount": 800000,
        "expandedRowCount": 3500000,
        "processingDuration": 900,
        "createdAt": "2026-03-10T00:00:05Z",
        "completedAt": "2026-03-10T00:20:00Z"
      }
    ],
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 30,
    "totalPages": 2
  },
  "timestamp": "2026-03-10T16:00:00Z"
}
```

---

## 二、快照管理API

### 2.1 封存排程快照

**端点**：`POST /api/v1/planning/snapshot/save`

**描述**：将256G内存中的ScheduleContext序列化为快照文件

**请求**：
```json
{
  "planVersionId": 12345,
  "batchNo": "REQ_20260310_01",
  "snapshotDirectory": "D:\\APS_Snapshots\\2026\\03",
  "compressionLevel": "Optimal",
  "savedBy": "APS_SCHEDULER"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "快照封存完成",
  "data": {
    "planVersionId": 12345,
    "batchNo": "REQ_20260310_01",
    "snapshotFilePath": "D:\\APS_Snapshots\\2026\\03\\Snapshot_12345_REQ_20260310_01.json.gz",
    "fileSize": 52428800,
    "compressedSize": 5242880,
    "fileHash": "a1b2c3d4e5f6...",
    "compressionRatio": 0.1,
    "createdAt": "2026-03-10T02:15:00Z"
  },
  "timestamp": "2026-03-10T02:15:30Z"
}
```

---

### 2.2 读取排程快照

**端点**：`GET /api/v1/planning/snapshot/{planVersionId}`

**描述**：从快照文件读取历史排程上下文

**响应**：
```json
{
  "code": 200,
  "message": "快照读取完成",
  "data": {
    "planVersionId": 12345,
    "batchNo": "REQ_20260310_01",
    "snapshotTime": "2026-03-10T02:15:00Z",
    "context": {
      "orders": [...],
      "materials": [...],
      "bom": [...],
      "routings": [...],
      "inventory": [...],
      "tasks": [...],
      "peggingLinks": [...]
    }
  },
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

### 2.3 校验快照完整性

**端点**：`POST /api/v1/planning/snapshot/{planVersionId}/verify`

**描述**：校验快照文件的完整性（文件大小、哈希值）

**响应**：
```json
{
  "code": 200,
  "message": "快照完整性校验通过",
  "data": {
    "planVersionId": 12345,
    "snapshotFilePath": "D:\\APS_Snapshots\\2026\\03\\Snapshot_12345_REQ_20260310_01.json.gz",
    "expectedFileSize": 52428800,
    "actualFileSize": 52428800,
    "expectedFileHash": "a1b2c3d4e5f6...",
    "actualFileHash": "a1b2c3d4e5f6...",
    "isValid": true,
    "verifiedAt": "2026-03-10T10:00:00Z"
  },
  "timestamp": "2026-03-10T10:00:05Z"
}
```

---

### 2.4 获取快照列表

**端点**：`GET /api/v1/planning/snapshot/list`

**查询参数**：
- `startDate`：开始日期
- `endDate`：结束日期
- `pageIndex`：页码
- `pageSize`：每页数量

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "items": [
      {
        "planVersionId": 12345,
        "versionCode": "V20260310_Daily_PFA_F1",
        "batchNo": "REQ_20260310_01",
        "snapshotFilePath": "D:\\APS_Snapshots\\2026\\03\\Snapshot_12345_REQ_20260310_01.json.gz",
        "fileSize": 52428800,
        "compressedSize": 5242880,
        "fileHash": "a1b2c3d4e5f6...",
        "createdAt": "2026-03-10T02:15:00Z"
      }
    ],
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 90,
    "totalPages": 5
  },
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

## 三、物料映射API

### 3.1 同步物料映射（SCD Type 2）

**端点**：`POST /api/internal/v1/material-mapping/sync`

**描述**：从ERP/MES同步物料映射关系，支持SCD Type 2拉链表

**请求**：
```json
{
  "source": "ERP",
  "syncedBy": "APS_ETL"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "物料映射同步完成",
  "data": {
    "source": "ERP",
    "newCount": 150,
    "updateCount": 30,
    "syncedAt": "2026-03-10T00:35:00Z"
  },
  "timestamp": "2026-03-10T00:35:30Z"
}
```

---

### 3.2 查询物料映射（时间点查询）

**端点**：`GET /api/internal/v1/material-mapping/{materialCode}`

**查询参数**：
- `pointInTime`：时间点（可选，默认当前时间）
- `source`：来源（ERP/MES_CUSTOM，可选）

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": [
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100001,
      "erpWarehouse": "WH-01",
      "mesID": null,
      "source": "ERP",
      "validFrom": "2026-01-01T00:00:00Z",
      "validTo": null,
      "isCurrent": true
    },
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100002,
      "erpWarehouse": "WH-02",
      "mesID": null,
      "source": "ERP",
      "validFrom": "2026-01-01T00:00:00Z",
      "validTo": null,
      "isCurrent": true
    }
  ],
  "timestamp": "2026-03-10T10:00:00Z"
}
```

**⚠️ P1-5修复（修复日期：2026-03-11）**：
- **返回结构从单对象改为列表**：支持一物多仓场景（同一个MaterialCode在不同仓库有不同的MasterID）
- **业务逻辑端必须配合《库存双源汇聚与优先级判定表》进行二次筛查**，才能准确决定到底用哪个MasterID去扣库存
- **如果只需要单个仓库的映射**，请在查询参数中指定`warehouse`参数（见下方扩展参数）

**扩展查询参数**（可选）：
- `warehouse`：仓库编码（可选，用于精确查询特定仓库的映射）

**精确查询示例**：
```bash
GET /api/internal/v1/material-mapping/RAW-STEEL-001?warehouse=WH-01
```

**响应**（单仓库精确查询）：
```json
{
  "code": 200,
  "message": "Success",
  "data": [
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100001,
      "erpWarehouse": "WH-01",
      "mesID": null,
      "source": "ERP",
      "validFrom": "2026-01-01T00:00:00Z",
      "validTo": null,
      "isCurrent": true
    }
  ],
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

### 3.3 获取物料映射历史

**端点**：`GET /api/internal/v1/material-mapping/{materialCode}/history`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "materialCode": "RAW-STEEL-001",
    "history": [
      {
        "erpMasterID": 100001,
        "erpWarehouse": "WH-01",
        "validFrom": "2026-01-01T00:00:00Z",
        "validTo": null,
        "isCurrent": true
      },
      {
        "erpMasterID": 100000,
        "erpWarehouse": "WH-01",
        "validFrom": "2025-01-01T00:00:00Z",
        "validTo": "2025-12-31T23:59:59Z",
        "isCurrent": false
      }
    ]
  },
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

## 四、库存来源优先级配置API

### 4.1 获取库存来源优先级配置

**端点**：`GET /api/v1/config/inventory-source-priority`

**查询参数**：
- `materialCode`：物料编码（支持通配符）
- `isActive`：是否启用

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "items": [
      {
        "id": 1,
        "materialCode": "RAW-%",
        "preferredSource": "ERP",
        "reason": "原材料统一以ERP库存为准",
        "isActive": true,
        "createdAt": "2026-01-01T00:00:00Z",
        "updatedAt": "2026-01-01T00:00:00Z"
      },
      {
        "id": 2,
        "materialCode": "ASSY-%",
        "preferredSource": "MES",
        "reason": "装配件默认以MES库存为准",
        "isActive": true,
        "createdAt": "2026-01-01T00:00:00Z",
        "updatedAt": "2026-01-01T00:00:00Z"
      }
    ]
  },
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

### 4.2 创建库存来源优先级配置

**端点**：`POST /api/v1/config/inventory-source-priority`

**请求**：
```json
{
  "materialCode": "RAW-SPECIAL-001",
  "preferredSource": "MES",
  "reason": "特殊原材料，MES线边库存更准确",
  "createdBy": "admin001"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "库存来源优先级配置创建成功",
  "data": {
    "id": 5,
    "materialCode": "RAW-SPECIAL-001",
    "preferredSource": "MES",
    "reason": "特殊原材料，MES线边库存更准确",
    "isActive": true,
    "createdAt": "2026-03-10T10:00:00Z"
  },
  "timestamp": "2026-03-10T10:00:05Z"
}
```

---

### 4.3 更新库存来源优先级配置

**端点**：`PUT /api/v1/config/inventory-source-priority/{id}`

**请求**：
```json
{
  "preferredSource": "ERP",
  "reason": "已切换回ERP库存",
  "updatedBy": "admin001"
}
```

---

### 4.4 删除库存来源优先级配置

**端点**：`DELETE /api/v1/config/inventory-source-priority/{id}`

**响应**：
```json
{
  "code": 200,
  "message": "库存来源优先级配置已删除",
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

## 五、修改后的Planning API（增加BatchNo关联）

### 5.1 触发全量排程（修改）

> ⚠️ **废弃声明（v2.4）**：`POST /api/v1/planning/schedule/full` 为旧版入口，V1 阶段可保留兼容，但**内部必须转调 `POST /api/scheduling/runs`**（RunType=FULL_SCHEDULE），并逐步废弃。**新增开发一律使用 `/api/scheduling/runs`**。

**端点（旧）**：`POST /api/v1/planning/schedule/full`（⚠️ 废弃，见上方声明）  
**端点（新）**：`POST /api/scheduling/runs`（见 §六。一）

**描述**：触发全量滚动排程，增加BatchNo关联

**请求**：
```json
{
  "planHorizonStart": "2026-03-10",
  "planHorizonEnd": "2026-06-10",
  "computeMode": "FULL_DETAIL",
  "batchNo": "REQ_20260310_01",
  "domainKeys": ["ProductFamilyA_Factory1"],
  "parallelExecution": true,
  "createdBy": "planner001"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Scheduling job submitted",
  "data": {
    "jobId": "JOB202603100001",
    "batchNo": "REQ_20260310_01",
    "versionIds": [12345],
    "estimatedDuration": 900,
    "status": "RUNNING"
  }
}
```

---

### 5.2 查询排程任务状态（修改）

**端点**：`GET /api/v1/planning/schedule/jobs/{jobId}`

**响应**：
```json
{
  "code": 200,
  "data": {
    "jobId": "JOB202603100001",
    "batchNo": "REQ_20260310_01",
    "status": "COMPLETED",
    "startedAt": "2026-03-10T02:00:00Z",
    "completedAt": "2026-03-10T02:15:00Z",
    "durationSeconds": 900,
    "domains": [
      {
        "domainKey": "ProductFamilyA_Factory1",
        "versionId": 12345,
        "status": "COMPLETED",
        "totalOrders": 45000,
        "totalTasks": 100000,
        "snapshotFilePath": "D:\\APS_Snapshots\\2026\\03\\Snapshot_12345_REQ_20260310_01.json.gz"
      }
    ]
  }
}
```

---

## 六、ETL日志API

### 6.1 获取ETL日志

**端点**：`GET /api/internal/v1/etl/logs`

**查询参数**：
- `batchNo`：批次号
- `step`：步骤（CalculateLLC/SyncMapping/LoadInventory/PullBOM）
- `status`：状态（SUCCESS/FAILED）
- `startDate`：开始日期
- `endDate`：结束日期
- `pageIndex`：页码
- `pageSize`：每页数量

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "items": [
      {
        "id": 1,
        "batchNo": "REQ_20260310_01",
        "step": "PullBOM",
        "message": "BOM数据拉取完成，拉取行数: 3500000",
        "status": "SUCCESS",
        "createdAt": "2026-03-10T00:25:00Z"
      },
      {
        "id": 2,
        "batchNo": "REQ_20260310_01",
        "step": "CalculateLLC",
        "message": "LLC计算完成，最大层级: 8",
        "status": "SUCCESS",
        "createdAt": "2026-03-10T00:30:00Z"
      },
      {
        "id": 3,
        "batchNo": "REQ_20260310_01",
        "step": "SyncMapping",
        "message": "物料映射同步完成，新增: 150，更新: 30",
        "status": "SUCCESS",
        "createdAt": "2026-03-10T00:35:00Z"
      }
    ],
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 3,
    "totalPages": 1
  },
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

## 七、系统监控API

### 7.1 获取系统健康状态

**端点**：`GET /api/v1/system/health`

**响应**：
```json
{
  "code": 200,
  "message": "System is healthy",
  "data": {
    "status": "HEALTHY",
    "components": {
      "database": {
        "status": "HEALTHY",
        "responseTime": 5,
        "details": {
          "apsDatabase": "CONNECTED",
          "odsDatabase": "CONNECTED"
        }
      },
      "ssd": {
        "status": "HEALTHY",
        "usagePercent": 65,
        "totalSpace": 1099511627776,
        "freeSpace": 384849395712
      },
      "hdd": {
        "status": "HEALTHY",
        "usagePercent": 45,
        "totalSpace": 5242880000000,
        "freeSpace": 2883584000000
      },
      "memory": {
        "status": "HEALTHY",
        "usagePercent": 40,
        "totalMemory": 274877906944,
        "freeMemory": 164926744166
      },
      "odsService": {
        "status": "HEALTHY",
        "lastBatchNo": "REQ_20260310_01",
        "lastBatchStatus": "CONSUMED",
        "lastBatchCompletedAt": "2026-03-10T00:20:00Z"
      }
    },
    "timestamp": "2026-03-10T10:00:00Z"
  }
}
```

---

### 7.2 获取性能指标

**端点**：`GET /api/v1/system/metrics`

**查询参数**：
- `metricType`：指标类型（BOM_EXPANSION/LLC_CALCULATION/SNAPSHOT_SAVE/SCHEDULING）
- `startDate`：开始日期
- `endDate`：结束日期

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "metricType": "BOM_EXPANSION",
    "metrics": [
      {
        "date": "2026-03-10",
        "batchNo": "REQ_20260310_01",
        "rootCount": 800000,
        "expandedRowCount": 3500000,
        "duration": 900,
        "avgDurationPerRoot": 0.001125,
        "status": "SUCCESS"
      },
      {
        "date": "2026-03-09",
        "batchNo": "REQ_20260309_01",
        "rootCount": 750000,
        "expandedRowCount": 3200000,
        "duration": 850,
        "avgDurationPerRoot": 0.001133,
        "status": "SUCCESS"
      }
    ],
    "summary": {
      "avgDuration": 875,
      "maxDuration": 900,
      "minDuration": 850,
      "successRate": 1.0
    }
  },
  "timestamp": "2026-03-10T10:00:00Z"
}
```

---

## 八、API调用流程示例

### 8.1 每日排程完整流程

```
1. 00:00 - APS划定活跃根集合
   POST /api/internal/v1/ods/bom/batch/request
   
2. 00:05 - ODS开始批次展开
   （ODS库内部定时作业触发）
   
3. 00:20 - 检查批次展开状态
   GET /api/internal/v1/ods/bom/batch/{batchNo}/status
   
4. 00:25 - APS拉取BOM数据
   POST /api/internal/v1/ods/bom/batch/{batchNo}/pull
   
5. 00:30 - APS计算LLC
   （内部调用，无API）
   
6. 00:35 - APS同步物料映射
   POST /api/internal/v1/material-mapping/sync
   
7. 02:00 - APS开始排程
   POST /api/scheduling/runs  （RunType=FULL_SCHEDULE，v2.4新入口；旧入口 /api/v1/planning/schedule/full 已废弃）
   
8. 02:15 - APS封存快照
   POST /api/v1/planning/snapshot/save
   
9. 02:20 - 查询排程任务状态
   GET /api/v1/planning/schedule/jobs/{jobId}
```

---

### 8.2 紧急插单流程

```
1. 14:00 - 检测到新BOMNO
   （订单增量同步时发现）
   
2. 14:00 - 请求实时BOM展开
   POST /api/internal/v1/ods/bom/realtime/request
   
3. 14:05 - 检查实时展开状态
   GET /api/internal/v1/ods/bom/realtime/{requestId}/status
   
4. 14:05 - 拉取实时展开结果
   （应用层SqlBulkCopy）
   
5. 14:06 - CTP评估
   POST /api/v1/insertorder/ctp/evaluate
   
6. 14:10 - 确认插单方案
   POST /api/v1/insertorder/ctp/confirm
```

---

## 九、错误码说明

### 9.1 ODS库错误码

| 错误码 | 说明 | 处理建议 |
|--------|------|----------|
| ODS_1001 | 批次号已存在 | 使用新的批次号 |
| ODS_1002 | 批次展开失败 | 检查MES_API_BOM_Request_Log表 |
| ODS_1003 | 批次状态不正确 | 等待批次状态变为READY |
| ODS_1004 | BOMNO不存在 | 检查MES_BOM_View视图 |
| ODS_1005 | 循环BOM检测 | 修复BOM数据 |
| ODS_1006 | 实时展开超时 | 增加超时时间或重试 |

---

### 9.2 快照错误码

| 错误码 | 说明 | 处理建议 |
|--------|------|----------|
| SNAP_2001 | 快照文件不存在 | 检查文件路径 |
| SNAP_2002 | 快照文件损坏 | 检查文件哈希 |
| SNAP_2003 | 快照文件大小不匹配 | 重新生成快照 |
| SNAP_2004 | 快照反序列化失败 | 检查文件格式 |
| SNAP_2005 | 磁盘空间不足 | 清理历史快照 |

---

### 9.3 物料映射错误码

| 错误码 | 说明 | 处理建议 |
|--------|------|----------|
| MAP_3001 | 物料编码不存在 | 检查Material表 |
| MAP_3002 | 映射关系冲突 | 检查MaterialMapping表 |
| MAP_3003 | 时间点查询无结果 | 检查ValidFrom和ValidTo |
| MAP_3004 | ERP/MES数据源不可用 | 检查数据源连接 |

---

## 十、安全与认证

### 10.1 内部API认证

**内部API**（`/api/internal/v1/*`）仅供APS内部服务调用，使用以下认证方式：

1. **API Key认证**：
```http
Authorization: ApiKey YOUR_API_KEY
X-Service-Name: APS_SCHEDULER
```

2. **IP白名单**：
- 仅允许APS服务器IP访问
- ODS服务器IP访问

---

### 10.2 外部API认证

**外部API**（`/api/v1/*`）供前端和其他系统调用，使用JWT认证：

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

---

## 十一、API版本管理

### 11.1 版本策略

- **v1.0**：初始版本（2026-03-05）
- **v2.0**：新增ODS库API、快照API、物料映射API（2026-03-10）
- **向后兼容**：v1.0 API继续支持，但建议迁移到v2.0

---

### 11.2 弃用通知

以下v1.0 API将在v3.0中弃用：
- 无（v2.0完全向后兼容）

---

## 十二、Swagger文档

### 12.1 访问地址

- **开发环境**：`https://aps-dev.example.com/swagger`
- **测试环境**：`https://aps-test.example.com/swagger`
- **生产环境**：`https://aps.example.com/swagger`（仅内网访问）

### 12.2 API分组

- **Planning API**：排程计划相关
- **ODS API**：ODS库批次管理（内部）
- **Snapshot API**：快照管理
- **Material Mapping API**：物料映射（内部）
- **InsertOrder API**：插单评估
- **Visualization API**：可视化
- **Config API**：配置管理
- **System API**：系统监控

---

## 附录A：数据库视图契约

### A.1 MES_BOM_View（ODS库）

**契约版本**：v1.0  
**最后修改**：2026-03-10

```sql
CREATE VIEW MES_BOM_View AS
SELECT 
    BOMNO,                  -- BOM编号（契约字段）
    ParentMaterialCode,     -- 父件物料编码（契约字段）
    ChildMaterialCode,      -- 子件物料编码（契约字段）
    Quantity,               -- 单位用量（契约字段）⚠️ 不累乘！
    IsActive,               -- 是否有效（契约字段）
    IsDefaultVersion        -- 是否默认版本（契约字段）
FROM MES_BOM_Physical_Table;
```

**⚠️ 契约承诺**：
- 以上字段名、数据类型在v1.x版本中永不变更
- Quantity字段必须是单位用量，不能累乘
- 如需修改，必须升级到v2.0并提供兼容层

---

### A.2 ERP_Master_View（APS库）

**契约版本**：v1.0  
**最后修改**：2026-03-10

```sql
CREATE VIEW ERP_Master_View AS
SELECT 
    MaterialCode,           -- 物料编码（契约字段）
    MasterID,               -- ERP主键（契约字段）
    Warehouse,              -- 仓库（契约字段）
    IsActive                -- 是否有效（契约字段）
FROM ERP_Master_Physical_Table;
```

---

## 附录B：性能基准

### B.1 ODS库性能基准

| 指标 | 目标值 | 实际值 | 备注 |
|------|--------|--------|------|
| 批次BOM展开耗时 | 15分钟 | 15分钟 | 80万BOMNO  350万行 |
| 实时BOM展开耗时 | 5分钟 | 3分钟 | 单个BOMNO |
| BOM拉取耗时 | 5分钟 | 5分钟 | SqlBulkCopy 350万行 |
| LLC计算耗时 | 5分钟 | 5分钟 | 350万行 |

---

### B.2 快照性能基准

| 指标 | 目标值 | 实际值 | 备注 |
|------|--------|--------|------|
| 快照封存耗时 | 1分钟 | 1分钟 | 50MB压缩文件 |
| 快照读取耗时 | 30秒 | 20秒 | 解压+反序列化 |
| 快照压缩比 | 10:1 | 10:1 | gzip Optimal |

---

## 附录C：故障排查

### C.1 ODS库展开失败

**现象**：批次状态为FAILED

**排查步骤**：
1. 查看`MES_API_BOM_Request_Log`表
2. 检查错误消息
3. 检查是否有循环BOM
4. 检查是否有无效的BOMNO

**API调用**：
```bash
GET /api/internal/v1/ods/bom/batch/{batchNo}/status
GET /api/internal/v1/etl/logs?batchNo={batchNo}&step=ExpandBOM
```

---

### C.2 快照文件损坏

**现象**：快照哈希校验失败

**排查步骤**：
1. 检查文件是否存在
2. 检查文件大小是否匹配
3. 检查文件哈希是否匹配

**API调用**：
```bash
POST /api/v1/planning/snapshot/{planVersionId}/verify
```

---

### C.3 物料映射不一致

**现象**：物料映射关系错误

**排查步骤**：
1. 检查`MaterialMapping`表
2. 检查`ValidFrom`和`ValidTo`
3. 检查`IsCurrent`字段

**API调用**：
```bash
GET /api/internal/v1/material-mapping/{materialCode}/history
```

---

## 四、主数据同步API（内部API）

### 4.1 同步物料映射

**端点**：`POST /api/internal/v1/data/material-mapping/sync`

**描述**：从ODS库的ext_ERP_Master_View和ext_MES_Material_View同步主数据到MaterialMapping和Material表

**负责人**：2号位（技术负责人）

**执行时机**：每天00:10

**Socket-Plug职责分工**：
- **契约插座（Socket）**：ERP DBA创建`ERP_Master_View`，MES DBA创建`MES_Material_View`
- **数据插头（Plug）**：5号位创建`ext_ERP_Master_View`和`ext_MES_Material_View`
- **数据装载（Loader）**：2号位执行`sp_SyncMaterialMapping`，同步到MaterialMapping和Material表

**请求**：
```json
{
  "syncTime": "2026-03-19T00:10:00Z",
  "syncScope": "FULL",
  "requestedBy": "APS_SCHEDULER"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "物料映射同步成功",
  "data": {
    "syncTime": "2026-03-19T00:10:00Z",
    "erpMaterialCount": 15000,
    "mesMaterialCount": 3000,
    "totalMappingCount": 18000,
    "newMappingCount": 50,
    "updatedMappingCount": 20,
    "materialTableCount": 18000,
    "duration": 15.5
  },
  "timestamp": "2026-03-19T00:10:15Z"
}
```

---

### 4.2 同步工艺路线（2026-04-01 v5.0重构）

**端点**：`POST /api/internal/v1/data/routing-graph/sync`

**描述**：从ODS库的3个ext_包装视图（输出MES_ID+Model）同步工艺图数据，经MaterialMapping映射MaterialId后Upsert到RoutingOperation/RoutingDependency/OperationResourceEligibility表（v5.0.1变更 2026-04-02）

**负责人**：2号位（技术负责人）

**执行时机**：每天00:15

**Socket-Plug职责分工**：
- **契约插座（Socket）**：MES DBA维护28张离散工艺表（MES_Routing_New、MES_Routing_Old等）
- **数据插头（Plug）**：3号位创建3个独立视图：`MES_APS_Routing_Operation_View` + `MES_APS_Routing_Dependency_View` + `APS_OperationResourceEligibility_View`
- **数据装载（Loader）**：2号位通过3个ext_包装视图分别装载到`RoutingOperation`/`RoutingDependency`/`OperationResourceEligibility`

**请求**：
```json
{
  "syncTime": "2026-03-19T00:15:00Z",
  "syncScope": "FULL",
  "requestedBy": "APS_SCHEDULER"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "工艺图同步成功",
  "data": {
    "syncTime": "2026-03-19T00:15:00Z",
    "routingOperationCount": 35000,
    "routingDependencyCount": 28000,
    "operationResourceEligibilityCount": 52000,
    "newOperationCount": 50,
    "updatedOperationCount": 120,
    "duration": 12.5
  },
  "timestamp": "2026-03-19T00:15:13Z"
}
```

---

### 4.3 查询同步状态

**端点**：`GET /api/internal/v1/data/sync-status`

**描述**：查询主数据同步状态

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "materialMapping": {
      "lastSyncTime": "2026-03-19T00:10:15Z",
      "status": "SUCCESS",
      "recordCount": 18000
    },
    "routingGraph": {
      "lastSyncTime": "2026-03-19T00:15:13Z",
      "status": "SUCCESS",
      "routingOperationCount": 35000,
      "routingDependencyCount": 28000,
      "operationResourceEligibilityCount": 52000
    },
    "bomRaw": {
      "lastSyncTime": "2026-03-19T00:30:25Z",
      "status": "SUCCESS",
      "batchNo": "REQ_20260319_01",
      "recordCount": 3500000
    }
  },
  "timestamp": "2026-03-19T00:35:00Z"
}
```

---

## 五、权限与审批API（v2.2新增）

### 5.1 用户认证API

#### 5.1.1 用户登录

**端点**：`POST /api/v1/auth/login`

**描述**：用户登录，返回 JWT Token

**权限要求**：无（公开接口）

**请求**：
```json
{
  "loginName": "zhang.san",
  "password": "Password@123"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "登录成功",
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresIn": 7200,
    "userId": 5,
    "userName": "zhang.san",
    "displayName": "张三",
    "roles": ["aps.planner"],
    "permissions": ["aps.plan.view", "aps.plan.run", "aps.task.view"]
  },
  "timestamp": "2026-03-24T08:30:00Z"
}
```

**错误响应**：
```json
{
  "code": 401,
  "message": "用户名或密码错误",
  "timestamp": "2026-03-24T08:30:00Z"
}
```

---

#### 5.1.2 用户登出

**端点**：`POST /api/v1/auth/logout`

**描述**：用户登出（可选实现，JWT 无状态）

**权限要求**：已登录

**请求头**：
```
Authorization: Bearer {token}
```

**响应**：
```json
{
  "code": 200,
  "message": "登出成功",
  "timestamp": "2026-03-24T09:00:00Z"
}
```

---

#### 5.1.3 获取当前用户信息

**端点**：`GET /api/v1/auth/current-user`

**描述**：获取当前登录用户的详细信息

**权限要求**：已登录

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "userId": 5,
    "loginName": "zhang.san",
    "displayName": "张三",
    "email": "zhang.san@company.com",
    "roles": [
      {
        "roleId": 3,
        "roleCode": "aps.planner",
        "roleName": "计划员"
      }
    ],
    "permissions": ["aps.plan.view", "aps.plan.run", "aps.task.view"],
    "dataScopes": [
      {
        "scopeType": "Factory",
        "scopeValue": "1",
        "description": "工厂1"
      }
    ]
  },
  "timestamp": "2026-03-24T08:35:00Z"
}
```

---

### 5.2 用户管理API

#### 5.2.1 查询用户列表

**端点**：`GET /api/v1/users`

**描述**：查询用户列表（分页）

**权限要求**：`aps.user.view`

**查询参数**：
- `pageIndex`：页码（默认1）
- `pageSize`：每页数量（默认20）
- `keyword`：关键词搜索（用户名或显示名）
- `isEnabled`：是否启用（可选）

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "total": 25,
    "pageIndex": 1,
    "pageSize": 20,
    "items": [
      {
        "userId": 5,
        "loginName": "zhang.san",
        "displayName": "张三",
        "email": "zhang.san@company.com",
        "isEnabled": true,
        "roles": ["计划员"],
        "lastLoginAt": "2026-03-24T08:30:00Z",
        "createdAt": "2026-03-20T10:00:00Z"
      }
    ]
  },
  "timestamp": "2026-03-24T09:00:00Z"
}
```

---

#### 5.2.2 创建用户

**端点**：`POST /api/v1/users`

**描述**：创建新用户

**权限要求**：`aps.user.manage`

**请求**：
```json
{
  "loginName": "li.si",
  "displayName": "李四",
  "password": "Password@123",
  "email": "li.si@company.com",
  "phoneNumber": "13800138001",
  "roleIds": [3],
  "dataScopeIds": [5]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "用户创建成功",
  "data": {
    "userId": 10,
    "loginName": "li.si",
    "displayName": "李四"
  },
  "timestamp": "2026-03-24T09:05:00Z"
}
```

---

#### 5.2.3 更新用户

**端点**：`PUT /api/v1/users/{userId}`

**描述**：更新用户信息

**权限要求**：`aps.user.manage`

**请求**：
```json
{
  "displayName": "李四（更新）",
  "email": "li.si.new@company.com",
  "phoneNumber": "13800138002",
  "isEnabled": true
}
```

**响应**：
```json
{
  "code": 200,
  "message": "用户更新成功",
  "timestamp": "2026-03-24T09:10:00Z"
}
```

---

#### 5.2.4 分配角色

**端点**：`POST /api/v1/users/{userId}/roles`

**描述**：为用户分配角色

**权限要求**：`aps.user.manage`

**请求**：
```json
{
  "roleIds": [3, 4]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "角色分配成功",
  "timestamp": "2026-03-24T09:15:00Z"
}
```

---

#### 5.2.5 分配数据范围

**端点**：`POST /api/v1/users/{userId}/data-scopes`

**描述**：为用户分配数据范围

**权限要求**：`aps.user.manage`

**请求**：
```json
{
  "scopePolicyIds": [5, 6, 7]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "数据范围分配成功",
  "timestamp": "2026-03-24T09:20:00Z"
}
```

---

### 5.3 角色权限管理API

#### 5.3.1 查询角色列表

**端点**：`GET /api/v1/roles`

**描述**：查询角色列表

**权限要求**：`aps.role.view`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": [
    {
      "roleId": 3,
      "roleCode": "aps.planner",
      "roleName": "计划员",
      "description": "发起排程、查看计划、调整任务",
      "isSystemRole": true,
      "permissionCount": 8,
      "userCount": 5
    }
  ],
  "timestamp": "2026-03-24T09:25:00Z"
}
```

---

#### 5.3.2 查询角色详情

**端点**：`GET /api/v1/roles/{roleId}`

**描述**：查询角色详情（包含权限列表）

**权限要求**：`aps.role.view`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "roleId": 3,
    "roleCode": "aps.planner",
    "roleName": "计划员",
    "description": "发起排程、查看计划、调整任务",
    "isSystemRole": true,
    "permissions": [
      {
        "permissionId": 1,
        "permissionCode": "aps.plan.view",
        "permissionName": "查看计划",
        "module": "Plan"
      },
      {
        "permissionId": 2,
        "permissionCode": "aps.plan.run",
        "permissionName": "发起排程",
        "module": "Plan"
      }
    ],
    "dataScopes": [
      {
        "scopeType": "Factory",
        "scopeValue": "1",
        "description": "工厂1"
      }
    ]
  },
  "timestamp": "2026-03-24T09:30:00Z"
}
```

---

#### 5.3.3 创建角色

**端点**：`POST /api/v1/roles`

**描述**：创建新角色

**权限要求**：`aps.role.manage`

**请求**：
```json
{
  "roleCode": "aps.custom.role1",
  "roleName": "自定义角色1",
  "description": "自定义角色描述",
  "permissionIds": [1, 2, 3],
  "dataScopeIds": [5]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "角色创建成功",
  "data": {
    "roleId": 10,
    "roleCode": "aps.custom.role1",
    "roleName": "自定义角色1"
  },
  "timestamp": "2026-03-24T09:35:00Z"
}
```

---

#### 5.3.4 分配权限

**端点**：`POST /api/v1/roles/{roleId}/permissions`

**描述**：为角色分配权限

**权限要求**：`aps.role.manage`

**请求**：
```json
{
  "permissionIds": [1, 2, 3, 4, 5]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "权限分配成功",
  "timestamp": "2026-03-24T09:40:00Z"
}
```

---

### 5.4 审批流API

#### 5.4.1 发起审批

**端点**：`POST /api/v1/approvals`

**描述**：发起审批流

**权限要求**：根据审批类型而定（如 `aps.task.freeze.override`）

**请求**：
```json
{
  "approvalType": "FreezeTaskAdjust",
  "objectType": "Task",
  "objectId": "12345",
  "reason": "紧急插单需要调整冻结区任务",
  "planVersionId": 123,
  "requestData": {
    "taskId": 12345,
    "originalStartTime": "2026-03-25T08:00:00Z",
    "newStartTime": "2026-03-25T10:00:00Z"
  }
}
```

**响应**：
```json
{
  "code": 200,
  "message": "审批流已发起",
  "data": {
    "approvalFlowId": 5,
    "approvalType": "FreezeTaskAdjust",
    "status": "Pending",
    "currentNodeSeq": 1,
    "createdAt": "2026-03-24T09:45:00Z",
    "nodes": [
      {
        "nodeSeq": 1,
        "approverRoleName": "车间主管",
        "status": "Pending"
      },
      {
        "nodeSeq": 2,
        "approverRoleName": "APS管理员",
        "status": "Pending"
      }
    ]
  },
  "timestamp": "2026-03-24T09:45:00Z"
}
```

---

#### 5.4.2 审批操作

**端点**：`POST /api/v1/approvals/{approvalFlowId}/approve`

**描述**：审批（通过或拒绝）

**权限要求**：`aps.approval.approve`

**请求**：
```json
{
  "decision": "Approved",
  "comment": "同意调整，注意控制影响范围"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "审批成功",
  "data": {
    "approvalFlowId": 5,
    "status": "Approved",
    "completedAt": "2026-03-24T10:00:00Z"
  },
  "timestamp": "2026-03-24T10:00:00Z"
}
```

---

#### 5.4.3 查询待审批列表

**端点**：`GET /api/v1/approvals/pending`

**描述**：查询当前用户的待审批列表

**权限要求**：`aps.approval.view`

**查询参数**：
- `pageIndex`：页码（默认1）
- `pageSize`：每页数量（默认20）

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "total": 3,
    "items": [
      {
        "approvalFlowId": 5,
        "approvalType": "FreezeTaskAdjust",
        "objectType": "Task",
        "objectId": "12345",
        "applicantUserName": "zhang.san",
        "reason": "紧急插单需要调整冻结区任务",
        "currentNodeSeq": 1,
        "createdAt": "2026-03-24T09:45:00Z"
      }
    ]
  },
  "timestamp": "2026-03-24T10:05:00Z"
}
```

---

#### 5.4.4 查询审批流详情

**端点**：`GET /api/v1/approvals/{approvalFlowId}`

**描述**：查询审批流详情

**权限要求**：`aps.approval.view`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "approvalFlowId": 5,
    "approvalType": "FreezeTaskAdjust",
    "objectType": "Task",
    "objectId": "12345",
    "applicantUserId": 5,
    "applicantUserName": "zhang.san",
    "status": "Approved",
    "currentNodeSeq": 2,
    "createdAt": "2026-03-24T09:45:00Z",
    "completedAt": "2026-03-24T10:00:00Z",
    "reason": "紧急插单需要调整冻结区任务",
    "nodes": [
      {
        "nodeSeq": 1,
        "approverRoleName": "车间主管",
        "status": "Approved",
        "record": {
          "approverUserName": "wang.wu",
          "decision": "Approved",
          "comment": "同意",
          "approvedAt": "2026-03-24T09:50:00Z"
        }
      },
      {
        "nodeSeq": 2,
        "approverRoleName": "APS管理员",
        "status": "Approved",
        "record": {
          "approverUserName": "admin",
          "decision": "Approved",
          "comment": "同意调整",
          "approvedAt": "2026-03-24T10:00:00Z"
        }
      }
    ]
  },
  "timestamp": "2026-03-24T10:10:00Z"
}
```

---

### 5.5 审计日志API

#### 5.5.1 查询审计日志

**端点**：`GET /api/v1/audit-logs`

**描述**：查询审计日志（分页）

**权限要求**：`aps.audit.view`

**查询参数**：
- `pageIndex`：页码（默认1）
- `pageSize`：每页数量（默认20）
- `userId`：用户ID（可选）
- `actionCode`：动作编码（可选）
- `startTime`：开始时间（可选）
- `endTime`：结束时间（可选）
- `result`：结果（Success/Failed/Denied，可选）

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "total": 1250,
    "pageIndex": 1,
    "pageSize": 20,
    "items": [
      {
        "id": 12345,
        "userId": 5,
        "userName": "zhang.san",
        "actionCode": "aps.plan.run",
        "objectType": "PlanVersion",
        "objectId": "123",
        "result": "Success",
        "occurredAt": "2026-03-24T08:30:00Z",
        "clientIp": "192.168.1.100",
        "planVersionId": 123,
        "batchNo": "B20260324001"
      }
    ]
  },
  "timestamp": "2026-03-24T10:15:00Z"
}
```

---

### 5.6 业务API权限校验说明

所有业务 API 都需要添加权限校验，示例如下：

#### 5.6.1 发起排程

**端点**：`POST /api/v1/planning/run`

**权限要求**：`aps.plan.run` + 数据范围校验（工厂级）

**请求头**：
```
Authorization: Bearer {token}
```

**权限校验逻辑**：
1. 校验用户是否拥有 `aps.plan.run` 权限
2. 校验用户是否有权访问请求中的 `factoryId`
3. 记录审计日志（包含 UserId、PlanVersionId）

---

#### 5.6.2 调整任务

**端点**：`PUT /api/v1/tasks/{taskId}`

**权限要求**：
- 普通任务：`aps.task.adjust` + 数据范围校验
- 冻结区任务：`aps.task.freeze.override` + 审批流
- 已开工任务：`aps.task.started.override` + 审批流

**权限校验逻辑**：
1. 判断任务是否在冻结区或已开工
2. 如果是，发起审批流，等待审批通过
3. 如果不是，校验 `aps.task.adjust` 权限和数据范围
4. 记录审计日志

---

#### 5.6.3 修改配置

**端点**：`PUT /api/v1/config/supply-context/{id}`

**权限要求**：`aps.config.supply_context.edit`

**权限校验逻辑**：
1. 校验用户是否拥有 `aps.config.supply_context.edit` 权限
2. 记录审计日志（包含修改前后的数据）

---

## §六。一 排程运行管理 API（v2.4 新增）

对齐：DDL v5.0.25 `ScheduleRun` 表 / 总表 v3.17 红线 #25-#26

### 6.1.1 触发新排程运行

| 项目 | 说明 |
|---|---|
| **方法** | POST |
| **路径** | `/api/scheduling/runs` |
| **权限** | `schedule.write` |
| **职责** | 3号位调用（API 触发路径）；Hangfire 凌晨路径不过此 API |

**Request Body**：
```json
{
  "runType": "FULL_SCHEDULE",  // 枚举：FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF
  "triggeredBy": "user:123",    // 'Hangfire' 或 UserId 字符串
  "scenarioId": null            // 仅 SIMULATION/INSERT_ORDER_WHATIF 填；其余为 null
}
```

**Response 201**：
```json
{
  "scheduleRunId": 42,
  "status": "RUNNING",
  "runType": "FULL_SCHEDULE",
  "startedAt": "2026-05-13T02:00:00Z"
}
```

### 6.1.2 查询运行历史

| 项目 | 说明 |
|---|---|
| **方法** | GET |
| **路径** | `/api/scheduling/runs?runType=&status=&pageSize=20&page=1` |
| **权限** | `schedule.read` |

### 6.1.3 激活 CANDIDATE 版本（落库与激活分离红线）

| 项目 | 说明 |
|---|---|
| **方法** | POST |
| **路径** | `/api/scheduling/plan-versions/{id}/activate` |
| **权限** | `schedule.activate` |
| **前置条件** | `PlanVersion.Status == CANDIDATE`；`FULL_SCHEDULE` 类型已自动激活，禁止重复调用 |

**Response 200**：
```json
{
  "planVersionId": 99,
  "previousStatus": "CANDIDATE",
  "newStatus": "ACTIVE",
  "activatedAt": "2026-05-13T10:30:00Z"
}
```

## §六。二 读模型查询 API（v2.4 新增）

对齐：DDL v5.0.25 `PlanKpiSummary` / `OrderScheduleSummary` / `ResourceLoadSummary` 表
所有读模型 API 为只读端点，**禁止写入**。

### 6.2.1 版本级 KPI

| 项目 | 说明 |
|---|---|
| **方法** | GET |
| **路径** | `/api/scheduling/plan-versions/{id}/kpi` |
| **权限** | `schedule.read` |

**Response 200**：
```json
{
  "planVersionId": 99,
  "onTimeRate": 0.9231,
  "delayedOrderCount": 12,
  "maxDelayHours": 48.5,
  "vipDelayedCount": 1,
  "avgLoadRate": 0.7834,
  "bottleneckCount": 3,
  "wipEstimate": 1250000.00,
  "generatedAt": "2026-05-13T03:15:00Z"
}
```

### 6.2.2 订单级摘要（分页）

| 项目 | 说明 |
|---|---|
| **方法** | GET |
| **路径** | `/api/scheduling/plan-versions/{id}/order-summary?riskLevel=HIGH&isVipImpacted=true&page=1&pageSize=50` |
| **权限** | `schedule.read` |

### 6.2.3 资源负荷（按资源/日期过滤）

| 项目 | 说明 |
|---|---|
| **方法** | GET |
| **路径** | `/api/scheduling/plan-versions/{id}/resource-load?resourceId=&dateFrom=&dateTo=&bottleneckOnly=false` |
| **权限** | `schedule.read` |

---

## 文档结束

**编制人**：2号位（技术负责人）  
**审核人**：0号位（业务负责人）  
**批准日期**：2026年3月24日

**版本历史**：
- v2.2（2026-03-24）：新增权限与审批相关API
- v2.1（2026-03-19）：新增主数据同步API、补充Socket-Plug职责分工
- v2.0（2026-03-10）：新增ODS API、快照API、物料映射API
- v1.2（2026-03-05）：新增拆批规则配置API
- v1.0（2026-02-26）：初始版本

**下一步行动**：
1. ✅ 实现ODS库批次管理API
2. ✅ 实现快照管理API
3. ✅ 实现物料映射API
4. ✅ 更新Swagger文档
5. 🔄 实现权限与审批API（v2.2）
6. 🔄 为所有业务API添加权限校验

---

**本文档提供完整的应用层API接口规范，供前后端开发团队参考。**