# APS 资源与工艺数据模型重设计方案 v5.0

**版本**：v5.2  
**日期**：2026-04-29  
**文档定位**：针对 APS 当前 Resource / ResourceGroup / Routing 数据模型与企业真实业务约束不一致的问题，给出重设计方案、废弃旧表说明与 V1/V2 演进路径。  
**适用范围**：APS 单体解决方案；面向 ODS 契约层、APS 本地标准层、排程内存消费层。

---

**版本历史**：

| 版本 | 日期 | 变更说明 |
|------|------|---------|
| v5.0 | 2026-04-01 | 初版。Resource 改为外部主数据镜像；ResourceGroup 废弃，降级为 ResourceOrgGroup 组织维度；Routing 线性表废弃，拆为 RoutingOperation + RoutingDependency 图模型；新增 OperationResourceEligibility 工序资源能力关系表；MinBatchSize/MaxBatchSize 拆出到 RoutingPlanningParam |
| v5.1 | 2026-04-25 | 资源 ODS 契约视图命名统一：`APS_Resource_View` → `MES_APS_Resource_View`；`ext_APS_Resource_View` → `ext_MES_APS_Resource_View`（与 MES_APS_Routing_*_View 对齐）。新增 `sp_SyncResourceData(@SourceType)` 占位 SP（v1 仅 MES 分支，'EAM' 分支 NOT_IMPLEMENTED），用法与 `sp_SyncMasterData` 同构；未来 EAM 上线并行新增 `EAM_APS_Resource_View` 同构契约，字段零分叉。对齐 DDL v5.0.13 + 防腐层 v1.14。 |
| v5.2 | 2026-04-29 | **生产部门主链注入 + WorkshopCode 全局清理**。Resource 删 `WorkshopCode` + 加 `ProductionDepartmentId NOT NULL`（FK → ProductionDepartment.Id）+ `SourceProductionDeptCode`（审计）。Routing 三件套加 `ProductionDepartmentId NOT NULL` + 唯一键升级三元组（业务事实：同物料同 StageCode 下不同部门可有不同小工序集合）。新增 `ProductionDepartment` 字典（APS 排程责任部门，1:1 归属 StageCode）。`sp_SyncResourceData` 加 `ProductionDepartment` 双字典映射 JOIN（任一未命中即跳过登记日志）。`StageLeadTimeParam.WorkshopCode → ProductionDeptCode`。1 号位排程主链定调 `(MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套`。对齐 DDL v5.0.16 + 字段说明 v5.0.16 + 防腐层 v1.15 + 架构总表 v3.12。 |

---

## 一、问题诊断与结论（2026-04-01）

### 1.1 当前模型存在的 5 个结构性问题

| # | 问题 | 当前模型 | 业务真实情况 | 结论 |
|---|------|---------|-------------|------|
| 1 | Resource 来源定位错误 | APS 手工维护为主 | 设备主数据已在 MES 中存在，未来可能切换至 EAM | 改为**外部主数据镜像表** |
| 2 | ResourceGroup 能力建模不成立 | 静态资源组，Routing 引用 ResourceGroup 即可组内选设备 | 设备可替代性取决于**物料 + 工艺路径 + 工序**的动态组合 | **废弃**，降级为组织/统计维度 ResourceOrgGroup |
| 3 | Routing 线性模型无法支撑并行/串行 | OperationSeq 线性排序，无法表达多前驱/多后继/并行支路/汇合点 | 装配车间大量存在并行工艺，V1 就必须支持 | **废弃**，拆为 RoutingOperation + RoutingDependency 图模型 |
| 4 | MinBatchSize/MaxBatchSize 无 ODS 来源 | 放在 Routing 事实层 | 属于排程规划参数，非 MES 工艺事实 | 拆出到 **RoutingPlanningParam** |
| 5 | V2 多路径无法只改 ODS | APS 下游是线性结构 | V2 多路径必然影响 APS 全链路 | V1 就预留 RouteCode/PathId，先只启用默认路径 |

### 1.2 决策依据

- **MES 源端（新结构/老结构）已具备并行/串行工序依赖数据** → ODS 可输出 Dependency 视图
- **MES 已有结构化的物料×路径×工序×资源能力关系** → ODS 可输出 Eligibility 视图
- **1号位排程引擎尚未开发** → 直接面向图模型开发，零迁移成本
- **装配车间 V1 阶段大量并行工艺** → 图模型是 V1 刚需

### 1.3 最终结论

> 将资源与工艺模型从"静态资源组 + 线性 Routing"升级为"**资源镜像 + 工艺图 + 工序资源能力关系表**"。

---

## 二、设计原则（2026-04-01）

| 原则 | 说明 |
|------|------|
| **两段式来源控制** | 源头主数据只存在于 MES/EAM；ODS 提供稳定契约视图；APS 只接契约不接源表 |
| **事实与策略分离** | 工艺事实来自 ODS（RoutingOperation/RoutingDependency），规划参数 APS 本地维护（RoutingPlanningParam/ResourcePlanningContext） |
| **组织维度与能力维度分离** | 组织/统计用 ResourceOrgGroup；排程能力用 OperationResourceEligibility |
| **线性顺序与依赖图分离** | 工序节点 RoutingOperation + 工序依赖边 RoutingDependency |
| **V1 支持并行/串行，V2 扩展多路径** | V1 只启用默认路径（RouteCode='DEFAULT', PathId=1），V2 在同结构上增加多路径选择 |

---

## 三、废弃旧表说明（2026-04-01）

### 3.1 ResourceGroup → 废弃

**原因**：静态资源组无法表达真实的设备可替代性（取决于物料+路径+工序的动态组合）。

**替代**：
- 组织/统计/前端筛选功能 → `ResourceOrgGroup`
- 排程能力建模 → `OperationResourceEligibility`

**DDL 处理**：在 DDL 中保留原表定义但标记 `⚠️ v5.0废弃`，不再有新代码引用。

### 3.2 Routing → 废弃

**原因**：线性 OperationSeq 无法表达并行/串行混合工艺；MinBatchSize/MaxBatchSize 无 ODS 来源不应在工艺事实层。

**替代**：
- 工序节点 → `RoutingOperation`
- 工序依赖 → `RoutingDependency`
- 批量参数 → `RoutingPlanningParam`
- 工序资源关系 → `OperationResourceEligibility`

**DDL 处理**：同上，保留但标记废弃。

### 3.3 Resource.ResourceGroupId → 废弃

**原因**：Resource 不再通过外键关联静态资源组。组织归属改为通过 ResourceOrgGroup 独立维护。

---

## 四、新模型——资源侧（2026-04-01）

### 4.1 ODS 契约层：MES_APS_Resource_View（v5.1 重命名，原名 `APS_Resource_View`）

**所在库**：MES_Integration（ODS）  
**负责人**：MES DBA（源系统侧）  
**来源**：当前来源于 MES 设备表，未来可切换至 EAM 或由 MES/EAM 汇总输出

```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01；v5.1 重命名 2026-04-25（原名 APS_Resource_View）
-- 负责人：MES DBA（源系统侧）
-- 设计理由：避免 APS 直接依赖 MES 设备表；未来 EAM 上线时并行新增 EAM_APS_Resource_View（双源同构契约零分叉）
-- 命名对齐：与 MES_APS_Routing_Operation_View / MES_APS_Routing_Dependency_View 系列一致
CREATE VIEW MES_APS_Resource_View AS
SELECT 
    ResourceCode,           -- 资源编码（契约字段，APS 统一业务键）
    ResourceName,           -- 资源名称（契约字段）
    ExternalResourceId,     -- 源系统物理主键（MES 设备ID 或 EAM 资产ID）
    SourceSystem,           -- 来源系统：MES / EAM（契约字段）
    FactoryCode,            -- 工厂编码（契约字段）
    ProductionDeptCode,     -- 🔄 v5.0.16 RENAME from WorkshopCode；APS 排程责任部门码（契约字段）
    ResourceType,           -- 资源类型：MACHINE / LINE / MANUAL_STATION（契约字段）
    Status,                 -- 设备状态：AVAILABLE / MAINTENANCE / DECOMMISSIONED（契约字段）
    CapacityFactor,         -- 产能系数（契约字段，1.0=标准）
    IsActive,               -- 是否有效（契约字段）
    UpdatedAt               -- 最后更新时间（契约字段）
FROM MES.dbo.Equipment      -- ⚠️ 实际物理表名，由 MES DBA 适配
WHERE IsDeleted = 0;

-- ⚠️ 契约承诺：无论 MES/EAM 内部表结构如何变更，此视图的列名和数据类型永不变更
```

**APS 侧跨库包装视图**：

```sql
-- 2号位在 APS 库创建（v5.1 重命名，原名 ext_APS_Resource_View）
CREATE VIEW ext_MES_APS_Resource_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Resource_View];

-- 未来 EAM 上线：再建一张 ext_EAM_APS_Resource_View 指向 [EAM_Integration].[dbo].[EAM_APS_Resource_View]
-- sp_SyncResourceData(@SourceType='EAM') 直接读此视图
```

### 4.2 APS 本地标准层：Resource（重定位为外部主数据镜像）

**新定位**：外部设备主数据在 APS 的本地镜像表（非手工自建主数据）

```sql
-- v5.0 重构：从"手工维护主数据"改为"外部主数据镜像"
-- 数据来源：ext_MES_APS_Resource_View（ODS 契约视图，v5.1 命名统一，原名 ext_APS_Resource_View）
-- 同步执行体：sp_SyncResourceData(@SourceType)（DDL v5.0.13 新增）；v1 仅 'MES' 分支，'EAM' 分支预留
-- 同步方式：每天定时全量刷新（设备主数据变化频率低）
CREATE TABLE Resource (
    Id              INT PRIMARY KEY IDENTITY(1,1),
    ResourceCode    NVARCHAR(50) NOT NULL UNIQUE,       -- APS 统一业务键
    ResourceName    NVARCHAR(200) NOT NULL,
    ExternalResourceId NVARCHAR(50) NULL,               -- v5.0新增：源系统物理主键
    SourceSystem    NVARCHAR(20) NOT NULL DEFAULT 'MES', -- v5.0新增：MES / EAM
    FactoryId               INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    ProductionDepartmentId  INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：FK → ProductionDepartment
    SourceProductionDeptCode NVARCHAR(50) NULL,         -- 🆕 v5.0.16：源系统部门码（审计用）
    ResourceType    NVARCHAR(50) NOT NULL,               -- MACHINE / LINE / MANUAL_STATION
    Status          NVARCHAR(20) NOT NULL DEFAULT 'AVAILABLE',
    CapacityFactor  DECIMAL(18,4) NOT NULL DEFAULT 1.0, -- v5.0重命名（原Capacity）
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- ⚠️ v5.0废弃：ResourceGroupId 外键已移除，组织归属改由 ResourceOrgGroup 维护
```

### 4.3 APS 本地组织维度：ResourceOrgGroup（替代原 ResourceGroup）

**定位**：仅用于统计切片、前端筛选、组织归类。**不再用于** Routing 能力分组或工序资源可替代性判断。

```sql
-- v5.0新增：替代原 ResourceGroup，降级为纯组织维度
CREATE TABLE ResourceOrgGroup (
    Id          INT PRIMARY KEY IDENTITY(1,1),
    Code        NVARCHAR(50) NOT NULL UNIQUE,
    Name        NVARCHAR(200) NOT NULL,
    FactoryId   INT NOT NULL FOREIGN KEY REFERENCES Factory(Id),
    Description NVARCHAR(500) NULL,
    IsActive    BIT NOT NULL DEFAULT 1,
    CreatedAt   DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt   DATETIME2 NOT NULL DEFAULT GETDATE()
);
```

### 4.4 APS 本地扩展层：ResourcePlanningContext

**定位**：保存 APS 本地排程控制信息，不污染外部资源事实层。

```sql
-- v5.0新增：APS 本地排程参数，与外部资源事实分离
CREATE TABLE ResourcePlanningContext (
    Id                      INT PRIMARY KEY IDENTITY(1,1),
    ResourceId              INT NOT NULL FOREIGN KEY REFERENCES Resource(Id),
    CalendarPolicyId        INT NULL,                       -- 排程日历策略ID
    DispatchPriority        INT NOT NULL DEFAULT 100,       -- 派工优先级（越小越优先）
    LocalDisableFlag        BIT NOT NULL DEFAULT 0,         -- APS 本地禁用标记
    OverrideCapacityFactor  DECIMAL(18,4) NULL,             -- APS 侧覆盖产能系数
    EffectiveFrom           DATE NOT NULL DEFAULT '1900-01-01',
    EffectiveTo             DATE NULL,
    UpdatedBy               NVARCHAR(50) NULL,              -- 维护人
    CreatedAt               DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt               DATETIME2 NOT NULL DEFAULT GETDATE()
);
```

---

## 五、新模型——工艺侧（2026-04-01）

### 5.1 ODS 契约层：拆分为三个视图

#### 5.1.1 MES_APS_Routing_Operation_View（工序节点视图）

**所在库**：MES_Integration（ODS）  
**负责人**：3号位（协同域开发，梳理 MES 28 张离散工艺表）

```sql
-- 契约版本：v2.0（v5.0重构：从线性顺序表升级为工序节点表）
-- 创建日期：2026-04-01
-- 负责人：3号位
-- 替代原 MES_APS_Routing_View 的工序节点部分
-- ⚠️ v5.0.1变更（2026-04-02）：ODS视图不再输出MaterialCode，改为输出MES_ID + Model
--   原因：防止多视图各自独立映射MaterialCode导致不一致
--   APS侧2号位装载时通过 MaterialMapping/Material 统一关联得到 MaterialId
-- V1：只输出默认路径（RouteCode='DEFAULT', PathId=1）
-- V2：输出多条路径，通过 IsDefaultPath/PathPriority 选择
CREATE VIEW MES_APS_Routing_Operation_View AS
SELECT 
    MES_ID,             -- MES物料主键（契约字段，INT NOT NULL）
    Model,              -- MES物料型号（契约字段，NVARCHAR）
    RouteCode,          -- 工艺路径编码（V1固定为'DEFAULT'，V2扩展）
    PathId,             -- 路径序号（V1固定为1，V2扩展）
    OperationCode,      -- 工序编码（契约字段，路径内唯一）
    OperationName,      -- 工序名称（契约字段）
    ProcessType,        -- 工序类型：辅助分类标签，不参与排程对接（契约字段）
    StageCode,          -- 🆕 v5.0.16：BOM↔Routing 对接主键之二（契约字段，取自 StageDict）
    ProductionDeptCode, -- 🆕 v5.0.16：APS 排程责任部门码（契约字段，必填）
    StandardTime,       -- 标准工时（分钟）（契约字段）
    SetupTime,          -- 准备时间（分钟）（契约字段）
    IsActive            -- 是否有效（契约字段）
FROM (
    -- 新结构工艺（优先）
    SELECT 
        r.MES_ID,
        m.Model,
        'DEFAULT' AS RouteCode,
        1 AS PathId,
        r.OperationCode,
        r.OperationName,
        r.ProcessType,
        r.StandardTime,
        r.SetupTime,
        r.IsActive
    FROM MES_Routing_New r
    INNER JOIN MES_Material m ON r.MES_ID = m.MES_ID
    WHERE r.IsActive = 1
    
    UNION ALL
    
    -- 老旧结构工艺（降级兼容，3号位ETL处理MaterialModel→MES_ID）
    SELECT 
        m.MES_ID,
        r.MaterialModel AS Model,
        'DEFAULT' AS RouteCode,
        1 AS PathId,
        r.OpCode AS OperationCode,
        r.OpName AS OperationName,
        r.ProcessType,
        r.StdTime AS StandardTime,
        r.SetupTime,
        1 AS IsActive
    FROM MES_Routing_Old r
    INNER JOIN MES_Material m ON m.Model = r.MaterialModel  -- 3号位ETL：老结构通过Model关联得到MES_ID
    WHERE NOT EXISTS (
        SELECT 1 FROM MES_Routing_New n WHERE n.MES_ID = m.MES_ID
    )
) AS UnifiedOperation;

-- ⚠️ 契约承诺：列名和数据类型永不变更
-- ⚠️ V1约束：RouteCode='DEFAULT', PathId=1（只输出默认路径）
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping(Source='MES', SourceID=MES_ID) → MaterialId
```

#### 5.1.2 MES_APS_Routing_Dependency_View（工序依赖视图）

**所在库**：MES_Integration（ODS）  
**负责人**：3号位

```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01
-- 负责人：3号位
-- 输出工序间的依赖关系（有向边），支持并行/串行混合工艺
-- DependencyType：ES=结束-开始（默认），SS=开始-开始，FF=结束-结束
-- V1：先最小实现 ES 类型，字段预留 SS/FF
-- ⚠️ v5.0.1变更（2026-04-02）：MaterialCode → MES_ID + Model（同Operation视图）
CREATE VIEW MES_APS_Routing_Dependency_View AS
SELECT 
    MES_ID,                 -- MES物料主键（契约字段，INT NOT NULL）
    Model,                  -- MES物料型号（契约字段，NVARCHAR）
    RouteCode,              -- 工艺路径编码（契约字段）
    PathId,                 -- 路径序号（契约字段）
    StageCode,              -- 🆕 v5.0.16：阶段码（契约字段）
    ProductionDeptCode,     -- 🆕 v5.0.16：APS 排程责任部门码（契约字段，必填）
    FromOperationCode,      -- 前驱工序编码（契约字段）
    ToOperationCode,        -- 后继工序编码（契约字段）
    DependencyType,         -- 依赖类型：ES / SS / FF（契约字段，V1先只用ES）
    LagTime,                -- 延迟时间（分钟，0=紧跟前驱完成）（契约字段）
    IsActive                -- 是否有效（契约字段）
FROM MES.dbo.RoutingDependency  -- ⚠️ 实际物理表名，由 3号位适配（含老结构ETL处理MES_ID）
WHERE IsActive = 1;

-- ⚠️ 契约承诺：列名和数据类型永不变更
-- ⚠️ 并行表达：若工序B和C都依赖工序A（A→B, A→C），则B和C可并行执行
-- ⚠️ 汇合表达：若工序D依赖B和C（B→D, C→D），则D必须等B和C都完成
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

#### 5.1.3 APS_OperationResourceEligibility_View（工序资源能力视图）

**所在库**：MES_Integration（ODS）  
**负责人**：3号位（从 MES 工序-设备能力关系表输出）

```sql
-- 契约版本：v1.0
-- 创建日期：2026-04-01
-- 负责人：3号位
-- 定义：某物料、某路径、某工序，允许使用哪些资源
-- 这是排程能力模型的核心，替代原 ResourceGroup 的能力分组功能
-- ⚠️ v5.0.1变更（2026-04-02）：MaterialCode → MES_ID + Model（同Operation视图）
CREATE VIEW APS_OperationResourceEligibility_View AS
SELECT 
    MES_ID,             -- MES物料主键（契约字段，INT NOT NULL）
    Model,              -- MES物料型号（契约字段，NVARCHAR）
    RouteCode,          -- 工艺路径编码（契约字段）
    PathId,             -- 路径序号（契约字段）
    StageCode,          -- 🆕 v5.0.16：阶段码（契约字段）
    ProductionDeptCode, -- 🆕 v5.0.16：APS 排程责任部门码（契约字段，必填）
    OperationCode,      -- 工序编码（契约字段）
    ResourceCode,       -- 资源编码（契约字段）
    Priority,           -- 优先级（1=最优，越小越优先）（契约字段）
    CapacityFactor,     -- 该资源执行该工序的产能系数（契约字段，1.0=标准）
    IsPrimary,          -- 是否首选资源（契约字段）
    IsActive            -- 是否有效（契约字段）
FROM MES.dbo.OperationResourceMapping  -- ⚠️ 实际物理表名，由 3号位适配（含老结构ETL处理MES_ID）
WHERE IsActive = 1;

-- ⚠️ 契约承诺：列名和数据类型永不变更
-- ⚠️ 关键语义：同样两台设备，生产不同产品或走不同路径时，可替代性可能不同
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

**APS 侧跨库包装视图**（3 个，2号位创建）：

```sql
CREATE VIEW ext_MES_APS_Routing_Operation_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Operation_View];

CREATE VIEW ext_MES_APS_Routing_Dependency_View AS
SELECT * FROM [MES_Integration].[dbo].[MES_APS_Routing_Dependency_View];

CREATE VIEW ext_APS_OperationResourceEligibility_View AS
SELECT * FROM [MES_Integration].[dbo].[APS_OperationResourceEligibility_View];
```

### 5.2 APS 本地标准层：RoutingOperation（工序节点表）

**定位**：承接 ODS 工序节点，作为工艺事实节点表。替代原 Routing 主模型。

```sql
-- v5.0新增：替代原线性 Routing 表
-- 数据来源：ext_MES_APS_Routing_Operation_View
CREATE TABLE RoutingOperation (
    Id              BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId      INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductionDepartmentId INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：部门主链维度（NOT NULL）
    RouteCode       NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',  -- V1固定'DEFAULT'，V2多路径
    PathId          INT NOT NULL DEFAULT 1,                    -- V1固定1，V2多路径
    StageCode       NVARCHAR(50) NOT NULL,                     -- 🆕 v5.0.16：BOM↔Routing 对接主键之二（取自 StageDict）
    OperationCode   NVARCHAR(50) NOT NULL,                     -- 工序编码（路径内唯一）
    OperationName   NVARCHAR(200) NOT NULL,
    ProcessType     NVARCHAR(50) NOT NULL,                     -- 辅助分类标签，不参与排程对接
    StandardDuration DECIMAL(18,4) NOT NULL,                   -- 标准工时（分钟）
    SetupTime       DECIMAL(18,4) NOT NULL DEFAULT 0,          -- 准备时间（分钟）
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- 🆕 v5.0.16 唯一键升级：加入 ProductionDepartmentId + StageCode（同物料同阶段下不同部门可有不同小工序集合）
    CONSTRAINT UQ_RoutingOperation UNIQUE (MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId, OperationCode)
);

CREATE INDEX IX_RoutingOp_Material ON RoutingOperation(MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId) WHERE IsActive = 1;
```

### 5.3 APS 本地标准层：RoutingDependency（工序依赖边表）

**定位**：承接 ODS 工序依赖，形成工艺有向图。让 1号位面对工艺图而非序列表。

```sql
-- v5.0新增：工序依赖图边表
-- 数据来源：ext_MES_APS_Routing_Dependency_View
CREATE TABLE RoutingDependency (
    Id                  BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId          INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductionDepartmentId INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：部门主链维度（NOT NULL）
    RouteCode           NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',
    PathId              INT NOT NULL DEFAULT 1,
    StageCode           NVARCHAR(50) NOT NULL,     -- 🆕 v5.0.16：阶段码
    FromOperationCode   NVARCHAR(50) NOT NULL,     -- 前驱工序
    ToOperationCode     NVARCHAR(50) NOT NULL,     -- 后继工序
    DependencyType      NVARCHAR(10) NOT NULL DEFAULT 'ES',  -- ES/SS/FF（V1先ES）
    LagTime             DECIMAL(18,4) NOT NULL DEFAULT 0,     -- 延迟时间（分钟）
    IsActive            BIT NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- 🆕 v5.0.16 唯一键升级
    CONSTRAINT UQ_RoutingDep UNIQUE (MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId, FromOperationCode, ToOperationCode)
);

CREATE INDEX IX_RoutingDep_Material ON RoutingDependency(MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId) WHERE IsActive = 1;
```

**工艺图表达示例**：

```
场景：产品X的装配工艺，工序A完成后B和C可并行，B和C都完成后D才能开始

RoutingOperation 数据：
| MaterialCode | OperationCode | OperationName |
|-------------|---------------|---------------|
| FG-X-001    | OP10          | 备料          |
| FG-X-001    | OP20          | 装配支路A     |
| FG-X-001    | OP30          | 装配支路B     |
| FG-X-001    | OP40          | 总装          |

RoutingDependency 数据：
| FromOperationCode | ToOperationCode | DependencyType |
|-------------------|-----------------|----------------|
| OP10              | OP20            | ES             |
| OP10              | OP30            | ES             |  ← OP20和OP30可并行
| OP20              | OP40            | ES             |
| OP30              | OP40            | ES             |  ← OP40等待OP20+OP30都完成
```

### 5.4 APS 本地能力层：OperationResourceEligibility

**定位**：定义某物料、某路径、某工序允许使用哪些资源。这是排程能力模型核心，直接回应"设备可替代性不是静态资源组"。

```sql
-- v5.0新增：工序资源能力关系表
-- 数据来源：ext_APS_OperationResourceEligibility_View
-- 替代原 ResourceGroup 的能力分组功能
CREATE TABLE OperationResourceEligibility (
    Id              BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId      INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    ProductionDepartmentId INT NOT NULL FOREIGN KEY REFERENCES ProductionDepartment(Id),  -- 🆕 v5.0.16：部门主链维度（NOT NULL）
    RouteCode       NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',
    PathId          INT NOT NULL DEFAULT 1,
    StageCode       NVARCHAR(50) NOT NULL,               -- 🆕 v5.0.16：阶段码
    OperationCode   NVARCHAR(50) NOT NULL,
    ResourceId      INT NOT NULL FOREIGN KEY REFERENCES Resource(Id),
    Priority        INT NOT NULL DEFAULT 1,              -- 1=最优
    CapacityFactor  DECIMAL(18,4) NOT NULL DEFAULT 1.0,  -- 该资源执行该工序的产能系数
    IsPrimary       BIT NOT NULL DEFAULT 0,              -- 是否首选资源
    IsActive        BIT NOT NULL DEFAULT 1,
    EffectiveFrom   DATE NOT NULL DEFAULT '1900-01-01',
    EffectiveTo     DATE NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- 🆕 v5.0.16 唯一键升级
    CONSTRAINT UQ_OpResElig UNIQUE (MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId, OperationCode, ResourceId)
);

CREATE INDEX IX_OpResElig_Operation ON OperationResourceEligibility(MaterialId, ProductionDepartmentId, StageCode, RouteCode, PathId, OperationCode) WHERE IsActive = 1;
CREATE INDEX IX_OpResElig_Resource ON OperationResourceEligibility(ResourceId) WHERE IsActive = 1;
```

### 5.5 APS 本地规划参数层：RoutingPlanningParam

**定位**：承接不在 ODS 工艺事实中的排程规划参数。

```sql
-- v5.0新增：排程规划参数（从原 Routing 表拆出）
-- MinBatchSize / MaxBatchSize 当前无 ODS 来源，不应污染工艺事实层
-- 未来如 MES 提供，可通过 ODS 视图接回（SourceSystem='MES'）
CREATE TABLE RoutingPlanningParam (
    Id                  BIGINT PRIMARY KEY IDENTITY(1,1),
    MaterialId          INT NOT NULL FOREIGN KEY REFERENCES Material(Id),
    RouteCode           NVARCHAR(50) NOT NULL DEFAULT 'DEFAULT',
    PathId              INT NOT NULL DEFAULT 1,
    OperationCode       NVARCHAR(50) NOT NULL,
    MinBatchSize        DECIMAL(18,4) NOT NULL DEFAULT 1,
    MaxBatchSize        DECIMAL(18,4) NOT NULL DEFAULT 999999,
    TransferBatchSize   DECIMAL(18,4) NULL,              -- 转移批量（工序间流转单位）
    SourceSystem        NVARCHAR(20) NOT NULL DEFAULT 'APS_LOCAL', -- MES / APS_LOCAL
    MaintainedBy        NVARCHAR(50) NULL,               -- 维护人
    EffectiveFrom       DATE NOT NULL DEFAULT '1900-01-01',
    EffectiveTo         DATE NULL,
    CreatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt           DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_RoutingParam UNIQUE (MaterialId, RouteCode, PathId, OperationCode)
);
```

---

## 六、职责分工（2026-04-01）

| 角色 | 职责 |
|------|------|
| **MES DBA** | 创建 `MES_APS_Resource_View`（源系统侧；v5.1 重命名，原名 `APS_Resource_View`） |
| **EAM DBA**（预留）| 未来 EAM 上线时在 ODS 同构新建 `EAM_APS_Resource_View`（双源并存）|
| **3号位** | 创建 `MES_APS_Routing_Operation_View`、`MES_APS_Routing_Dependency_View`、`APS_OperationResourceEligibility_View`（梳理 MES 28 张离散工艺表 + 设备能力关系表） |
| **2号位** | 创建 APS 侧 ext_ 包装视图（4个）；开发 IDataLoader 同步逻辑；维护 RoutingPlanningParam 本地参数 |
| **1号位** | 面向 RoutingOperation + RoutingDependency 图模型开发排程引擎 Task 生成逻辑；使用 OperationResourceEligibility 进行资源分配 |

---

## 七、V1 / V2 演进路径（2026-04-01）

### 7.1 V1 范围（当前）

| 项目 | 说明 |
|------|------|
| 路径 | 只启用默认路径（RouteCode='DEFAULT', PathId=1） |
| 依赖类型 | 先只实现 ES（结束-开始），字段预留 SS/FF |
| 资源来源 | MES（通过 `MES_APS_Resource_View`）；未来扩展 EAM（`EAM_APS_Resource_View`）双源并存 |
| 图模型 | RoutingOperation + RoutingDependency 完整支持并行/串行 |
| 能力关系 | OperationResourceEligibility 完整支持动态能力绑定 |

### 7.2 V2 扩展（未来）

| 项目 | 变更点 |
|------|--------|
| 多路径 | ODS 输出多条路径（RouteCode/PathId 多值），APS 新增路径选择策略 |
| 依赖类型 | 启用 SS/FF 等更丰富的依赖类型 |
| 资源来源 | 可从 MES 平滑切换或扩展到 EAM（SourceSystem 字段已预留） |
| 路径选择 | 1号位引擎支持路径选择或仿真对比 |

**V2 核心收益**：由于 V1 已采用图模型和预留字段，V2 主要改 ODS 输出 + 路径选择策略，无需重构 APS 下游模型。

---

## 八、新旧模型对比总结（2026-04-01）

| 维度 | 旧模型（v2.0-v4.0） | 新模型（v5.0） | 改进理由 |
|------|---------------------|---------------|---------|
| 资源来源 | APS 手工维护为主 | ODS 统一出口 + APS 本地镜像 | 消除双写，来源真实可控 |
| 资源组 | 静态能力组 ResourceGroup | 降级为组织维度 ResourceOrgGroup | 可替代性依赖物料+路径+工序 |
| 工艺结构 | OperationSeq 线性表 | RoutingOperation + RoutingDependency 图模型 | V1 必须支持并行/串行 |
| 工序-设备关系 | Routing 引用 ResourceGroup | OperationResourceEligibility | 真实能力关系是动态的 |
| 批量参数 | 放在 Routing 表里 | 独立 RoutingPlanningParam | 当前无 ODS 来源，不应污染事实层 |
| V2 多路径 | 靠 ODS 扩展 | V1 就预留 RouteCode/PathId | 否则 V2 一定全面重构 |

---

## 九、数据同步方式（2026-04-01）

| APS 本地表 | ODS 数据源 | 同步方式 | 时机 | 负责人 |
|-----------|-----------|---------|------|--------|
| Resource | ext_MES_APS_Resource_View | 全量刷新（设备变化频率低，SP=sp_SyncResourceData @SourceType='MES'） | 每天 00:10（v5.1.1 对齐走查 V3.4：与 sp_SyncMasterData 同窗口并行） | 2号位 |
| RoutingOperation | ext_MES_APS_Routing_Operation_View | 增量 Upsert | 每天 00:30 | 2号位 |
| RoutingDependency | ext_MES_APS_Routing_Dependency_View | 增量 Upsert | 每天 00:30（与 Operation 同批次） | 2号位 |
| OperationResourceEligibility | ext_APS_OperationResourceEligibility_View | 增量 Upsert | 每天 00:35 | 2号位 |
| ResourceOrgGroup | APS 本地维护 | 手工维护 | 按需 | 2号位 |
| ResourcePlanningContext | APS 本地维护 | 手工维护 | 按需 | 2号位 |
| RoutingPlanningParam | APS 本地维护（未来可接 MES） | 手工维护 / 未来可同步 | 按需 | 2号位 |

---

*文档结束*
