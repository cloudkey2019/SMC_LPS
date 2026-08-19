# APS 应用层API接口规范 v2.0

**版本**：v2.0  
**日期**：2026-03-10  
**基于**：《APS数据架构与防腐层设计方案 v1.0》  
**技术栈**：ASP.NET Core Web API + RESTful + Swagger  
**更新**：新增ODS库批次管理API、BOM展开状态机API、快照管理API、物料映射API

---

## 📋 v2.0更新说明

### **核心变更**：
1. ✅ 新增**ODS库批次管理API**（批次BOM展开请求、状态查询、实时展开）
2. ✅ 新增**快照管理API**（快照封存、读取、完整性校验）
3. ✅ 新增**物料映射API**（MaterialMapping同步、时间点查询）
4. ✅ 新增**库存来源优先级配置API**
5. ✅ 修改**Planning API**（增加BatchNo关联）

### **架构红线**：
- ⚠️ ODS API仅供APS内部调用，不对外暴露
- ⚠️ BOM展开的Quantity字段必须是单位用量，不累乘
- ⚠️ 快照文件存储在4.76T机械硬盘，不占用SSD

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
        "materialCode": "MES-%",
        "preferredSource": "MES",
        "reason": "MES自建物料统一以MES库存为准",
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

**端点**：`POST /api/v1/planning/schedule/full`

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
   POST /api/v1/planning/schedule/full
   
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

## 文档结束

**编制人**：2号位（技术负责人）  
**审核人**：0号位（业务负责人）  
**批准日期**：2026年3月10日

**版本历史**：
- v2.0（2026-03-10）：新增ODS API、快照API、物料映射API
- v1.2（2026-03-05）：新增拆批规则配置API
- v1.0（2026-02-26）：初始版本

**下一步行动**：
1. ✅ 实现ODS库批次管理API
2. ✅ 实现快照管理API
3. ✅ 实现物料映射API
4. ✅ 更新Swagger文档

---

**本文档提供完整的应用层API接口规范，供前后端开发团队参考。**