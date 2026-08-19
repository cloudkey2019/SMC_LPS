# 双源同构主数据三表协同同步设计（2026-04-01 v2.0更新）

**版本**：v2.1  
**日期**：2026-04-29  
**适用项目**：Lean APS V1.0  
**负责人**：2号位（技术负责人）  
**审核依据**（v2.1 升级，2026-04-29）：
- 《APS_数据库字段说明文档_v5.0.md》→ **v5.0.16**（权威字段定义）  
- 《APS_数据架构与防腐层设计方案_v5.0.md》→ **v1.15**（数据管道设计）  
- 《APS_数据库表结构设计_v5.0.sql》→ **v5.0.16**（DDL 基线）  
- 《APS_各类基础数据分层承接与演变总表_v5.0.md》→ **v3.12**

---

## v2.1 变更说明（2026-04-29 MSC 部门维度补充 + ProcessCodeDict 取消同步分支）

| 变更点 | v2.0 | v2.1 |
|---|---|---|
| `MaterialSupplyContext` 字段 | `SupplyMode/DefaultProductionDeptCode/LeadTimeDays/SafetyStock/InventoryManagementMode` 5 字段追踪 | 同上 + 新增 **`DefaultProductionDepartmentId`**（FK → ProductionDepartment.Id；与 DeptCode 双轨；2 号位 `sp_RebuildMaterialStageDeptContext` 优先用此 ID 组装 Context） |
| MSC SCD Type 2 追踪字段 | 5 个 | 6 个（新增 `DefaultProductionDepartmentId`） |
| MSC 同步流程 | `sp_SyncMasterData` 输出 `DefaultProductionDeptCode` | 同上 + 同步 JOIN `ProductionDepartment` 字典映射 `DefaultProductionDepartmentId`（任一字典项未命中即跳过登记日志） |
| 下游消费 | 1 号位/3 号位直接消费 MSC 仓库级上下文 | **MSC 不再作为 1 号位入口**——必须经 2 号位 `sp_RebuildMaterialStageDeptContext` 组装为 `MaterialStageDeptContext`（键 `(MaterialId, StageCode)` → `DefaultProductionDepartmentId`）后由 1 号位消费 |
| `sp_SyncMasterData(@SourceType)` 出口 | `'ERP' / 'MES' / 'ProcessCode'`（v5.0.15）| **`'ERP' / 'MES'` 两个分支**（v5.0.16 取消 `'ProcessCode'` 分支，因 ProcessCodeDict 改为 APS 自维护字典） |

**关键设计决策（v2.1 新增）**：
1. **MSC 仍然是双源同构主数据三表协同的最末端**——只承载"仓库级原始上下文"；部门维度的"物料 × 阶段"组装由独立 SP 完成（`sp_RebuildMaterialStageDeptContext`）
2. **MSC 不直接作为 1 号位排程入口**——架构总表红线 #15：1 号位主链 `(MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套`，MSC 是其上游输入源之一，不直接对接 1 号位
3. **`sp_SyncMasterData` 范围收窄**——只负责 `Material` / `MaterialMapping` / `MaterialSupplyContext` 三表协同；`ProcessCodeDict`（v5.0.16 改为 APS 自维护）和 `Resource`（独立 `sp_SyncResourceData`）不在本 SP 范围内

---

## 核心定位

> **双源同构契约，双源三表协同同步，MaterialType 由 APS 统一推导。**
>
> ERP 和 MES 主数据进入 APS 的承接模型，统一拆成**“物料主身份、来源映射、仓库级供给上下文”**三层。
> 两个源系统视图字段完全一致（同构契约），同步逻辑零分叉，由同一个参数化 SP `sp_SyncMasterData(@SourceType)` 处理。

---

# 一、三张实体职责边界（2026-04-01 v2.0更新）

## 1.1 Material — APS 统一业务物料主表

| 维度 | 说明 |
|------|------|
| **唯一键** | `MaterialCode`（一个物料编码只有一行） |
| **职责** | 承载物料的**本体属性**：名称、规格、类型、单位、低阶码等 |
| **不承载** | 任何随仓库/工厂变化的属性（供给方式、安全库存、提前期等已下沉到 MaterialSupplyContext） |
| **生命周期** | 只要任一仓库的 MaterialMapping 仍然有效，Material 就保持 `IsActive = 1`；全部仓库映射关闭时标记为 `IsActive = 0`（软删除，不物理删除） |
| **红线** | ❌ 禁止按仓库拆分物料行；❌ 禁止全量删除重建；✅ 必须增量 Upsert，保持 `Material.Id` 稳定 |

## 1.2 MaterialMapping — 来源系统映射桥表（2026-04-01 v2.0更新）

| 维度 | 说明 |
|------|------|
| **唯一键** | `MaterialCode + Source + Warehouse_Norm`（持久化计算列），同一物料在不同源/不同仓库分别一行 |
| **职责** | 物理身份桥接——记录 APS 的 `MaterialCode` 与源系统物理主键（`SourceID`，ERP的MasterID / MES的MES_ID）的对应关系 |
| **字段简化** | v4.0重构：消除 ERP/MES 字段分叉，ERP_MasterID+MES_ID→统一`SourceID`，ERP_Warehouse+MES_Location→统一`Warehouse` |
| **不承载** | 任何业务属性（供给方式、安全库存等属于 MaterialSupplyContext） |
| **变更模式** | **SCD Type 2 拉链表**：旧记录关闭（`IsCurrent=0, ValidTo=@SyncTime`），新记录插入（`IsCurrent=1`） |
| **红线** | ❌ 禁止物理删除；✅ 源端消失时只关闭该仓库的映射，不影响同物料其他仓库，不删除 Material |

## 1.3 MaterialSupplyContext — 仓库级供给上下文表

| 维度 | 说明 |
|------|------|
| **唯一键** | `MaterialCode + WarehouseCode`（当 `IsCurrent = 1` 时唯一） |
| **职责** | 承载同一物料在不同仓库下的**供给方式、责任归属、计划参数**：SupplyMode、DefaultProductionDeptCode、ProcurementDeptCode、OutsourceDeptCode、LeadTimeDays、SafetyStock、**InventoryManagementMode**（v4.0新增：STOCKED/NON_STOCKED） |
| **不承载** | 物料本体属性（那是 Material 的事）；物理身份映射（那是 MaterialMapping 的事） |
| **变更模式** | **SCD Type 2 拉链表**：供给属性变化时关闭旧记录、插入新记录；仓库消失时关闭记录 |
| **红线** | ❌ 禁止退回到仅按 MaterialCode 维护（必须按仓库级）；❌ 禁止把仓库级属性重新塞回 Material 表 |

---

## 1.4 三张表关系图

```
    ext_ERP_Master_View / ext_MES_Material_View（双源同构契约视图）
    ┌──────────────────────────────────────────────┐
    │ MaterialCode                                  │
    │ MaterialName, Spec, UOM                       │  ← 物料本体属性
    │ MasterID, Warehouse                           │  ← 物理身份（MES中MES_ID→MasterID别名）
    │ SupplyMode, ProductionDeptCode                │  ← 仓库级供给
    │ LeadTimeDays, SafetyStock                     │  ← 仓库级计划参数
    │ InventoryManagementMode                       │  ← 库存管理方式（v4.0新增）
    │ IsActive                                      │
    └──────────────┬───────────────────────────────┘
                   │  sp_SyncMasterData(@SourceType)
      ┌────────────┼────────────────┐
      ▼            ▼                ▼
    ┌───────────────┐ ┌──────────────┐ ┌──────────────────────┐
    │   Material    │ │MaterialMapping│ │ MaterialSupplyCtx    │
    │───────────────│ │──────────────│ │──────────────────────│
    │MaterialCode(UK)│ │MaterialCode  │ │MaterialCode          │
    │MaterialName   │ │SourceID      │ │WarehouseCode         │
    │Spec           │ │Warehouse     │ │SupplyMode            │
    │MaterialType   │ │Source(ERP/MES)│ │DefaultProdDept       │
    │  (APS前缀推导) │ │ValidFrom     │ │LeadTimeDays          │
    │UOM            │ │ValidTo       │ │SafetyStock           │
    │LowLevelCode   │ │IsCurrent     │ │InventoryMgmtMode    │
    │IsActive       │ │              │ │ValidFrom/To          │
    │               │ │              │ │IsCurrent             │
    │按MaterialCode │ │按MaterialCode│ │按MaterialCode        │
    │唯一，不拆仓库 │ │+Source       │ │+WarehouseCode        │
    │只存本体属性   │ │+Warehouse    │ │SCD Type 2            │
    │               │ │SCD Type 2   │ │                      │
    └───────────────┘ └──────────────┘ └──────────────────────┘
```

---

# 二、完整同步流程（2026-04-01 v2.0更新）

**存储过程名**：`sp_SyncMasterData(@SourceType)`（v4.0合并 sp_SyncERPMasterData + sp_SyncMESMaterialData）  
**执行时机**：每天 00:10（ERP），每天 00:20（MES）  
**数据来源**：`ext_ERP_Master_View` 或 `ext_MES_Material_View`（双源同构契约，字段完全一致）  
**事务边界**：三步在同一事务内，全部成功或全部回滚  
**MaterialType**：由 APS 按 MaterialCode 前缀统一推导，不依赖源系统

### 调用示例

```sql
-- ERP 同步
EXEC sp_SyncMasterData @SourceType='ERP', @BatchNo='DAILY', @RowsAffected OUTPUT, @ErrorMessage OUTPUT;
-- MES 同步
EXEC sp_SyncMasterData @SourceType='MES', @BatchNo='DAILY', @RowsAffected OUTPUT, @ErrorMessage OUTPUT;
```

### 同步顺序

```
步骤0：提取源端当前有效快照到临时表
       → 按 @SourceType 读取 ext_ERP_Master_View 或 ext_MES_Material_View
       → 按 MaterialCode + Warehouse_Norm 消歧（一物一仓一行）

步骤1：同步 Material（物料主身份）
       → 仅按 MaterialCode 维护 APS 统一业务物料
       → 新物料 INSERT，MaterialType 由 APS 按前缀推导
       → 已有物料 UPDATE 本体属性
       → 全部仓库映射均消失的物料标记 IsActive = 0

步骤2：同步 MaterialMapping（来源映射桥表）
       → 业务键：MaterialCode + Source + Warehouse_Norm
       → 追踪字段：SourceID（变更时开新版本）
       → 包含：刷新、变更关闭+新增、新仓库新增、源端消失关闭

步骤3：同步 MaterialSupplyContext（仓库级供给上下文）
       → 业务键：MaterialCode + WarehouseCode
       → 追踪字段：SupplyMode, DefaultProductionDeptCode, LeadTimeDays,
                   SafetyStock, InventoryManagementMode
       → 供给属性变化时关闭旧记录+插入新记录
       → 源端消失的仓库关闭对应记录

步骤4：记录 ETL 日志
```

---

# 三、各类变化场景处理矩阵（2026-04-01 v2.0更新）

> 以下场景对 ERP 和 MES 完全一致，仅 `@SourceType` 参数不同。

## 3.1 场景总览

| 场景 | Material | MaterialMapping | MaterialSupplyContext |
|------|----------|-----------------|----------------------|
| **A. 全新物料+全新仓库** | INSERT 新物料行（MaterialType由前缀推导） | INSERT 新映射行 | INSERT 新上下文行 |
| **B. 已有物料+新增仓库** | UPDATE 本体属性（若变化） | INSERT 新仓库映射行 | INSERT 新仓库上下文行 |
| **C. 同物料+同仓库+同SourceID，属性无变化** | 刷新 UpdatedAt | 刷新 UpdatedAt | 不变 |
| **D. 同物料+同仓库+同SourceID，本体属性变化** | UPDATE MaterialName/Spec/UOM等 | 刷新 UpdatedAt | 不变 |
| **E. 同物料+同仓库+同SourceID，供给属性变化** | 不变 | 刷新 UpdatedAt | 关闭旧记录 + INSERT 新记录（SCD Type 2） |
| **F. 同物料+同仓库，SourceID变化** | 不变 | 关闭旧记录 + INSERT 新记录（SCD Type 2） | 同步刷新（如属性也变则 SCD Type 2） |
| **G. 某仓库在源端消失** | 不变（仍有其他仓库） | 仅关闭该仓库映射 | 仅关闭该仓库上下文 |
| **H. 该物料全部仓库消失** | 标记 IsActive = 0 | 关闭全部映射 | 关闭全部上下文 |

## 3.2 场景详解

### 场景A：全新物料 + 全新仓库

**触发条件**：源视图中出现了一个 APS 从未见过的 `MaterialCode`

```
Material      → INSERT (MaterialCode, MaterialName, Spec, MaterialType=按前缀推导, UOM, IsActive=1)
MaterialMapping → INSERT (MaterialCode, SourceID, Warehouse, Source=@SourceType, IsCurrent=1)
MaterialSupplyContext → INSERT (MaterialCode, WarehouseCode, SupplyMode, DefaultProductionDeptCode,
                                LeadTimeDays, SafetyStock, InventoryManagementMode,
                                SourceSystem=@SourceType, IsCurrent=1)
```

### 场景B：已有物料 + 新增仓库

**触发条件**：`MaterialCode` 已存在于 Material，但 `MaterialCode + Source + Warehouse` 组合在 MaterialMapping 中不存在

```
Material      → UPDATE 本体属性（如果变了）
MaterialMapping → INSERT 新仓库映射行（不影响其他仓库的映射行）
MaterialSupplyContext → INSERT 新仓库上下文行（不影响其他仓库的上下文行）
```

### 场景F：同物料 + 同仓库，SourceID变化

**触发条件**：同一 `MaterialCode + Warehouse`，但 `SourceID`（ERP的MasterID / MES的MES_ID）从 1001 变成了 2002

```
Material      → 不变（MaterialCode 没变，本体属性由 Material 自身管理）
MaterialMapping → 关闭旧记录 (IsCurrent=0, ValidTo=@SyncTime)
                  INSERT 新记录 (SourceID=2002, IsCurrent=1, ValidFrom=@SyncTime)
MaterialSupplyContext → 如果供给属性也变了 → SCD Type 2（关闭旧+插入新）
                        如果供给属性没变 → 不变
```

### 场景G：某仓库在源端消失

**触发条件**：物料 `RAW-STEEL-001` 原来在 `WH-A`、`WH-B` 两个仓库都有，现在 `WH-B` 从源端消失了

```
Material      → 不变（WH-A 仍然有效，物料本身仍然存在）
MaterialMapping → 仅关闭 WH-B 的映射行，WH-A 不受影响
MaterialSupplyContext → 仅关闭 WH-B 的上下文行，WH-A 不受影响
```

### 场景H：该物料全部仓库消失

**触发条件**：物料 `FG-TV-002` 的所有仓库都从源端消失了

```
Material      → 标记 IsActive = 0（软删除，不物理删除，保持 Id 稳定）
MaterialMapping → 关闭全部仓库的映射行
MaterialSupplyContext → 关闭全部仓库的上下文行
```

---

# 四、完整同步伪代码（2026-04-01 v2.0更新）

> 完整实现见 DDL 文件 `APS_数据库表结构设计_v4.0.sql` 中的 `sp_SyncMasterData`。
> 以下为精简伪代码，突出核心逻辑。

```sql
-- =====================================================================
-- 存储过程：sp_SyncMasterData（v4.0 双源统一）
-- 职责：ERP/MES 主数据 → APS 三层承接（Material + MaterialMapping + MaterialSupplyContext）
-- 执行时机：每天 00:10(ERP) / 00:20(MES)
-- 数据来源：ext_ERP_Master_View 或 ext_MES_Material_View（双源同构契约）
-- 负责人：2号位（技术负责人）
-- =====================================================================

CREATE OR ALTER PROCEDURE sp_SyncMasterData
    @SourceType NVARCHAR(20),           -- 'ERP' 或 'MES'
    @BatchNo NVARCHAR(50) = 'DAILY',
    @RowsAffected INT OUTPUT,
    @ErrorMessage NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @SyncTime DATETIME2 = GETDATE();
    DECLARE @StepName NVARCHAR(100);

    -- 计数器
    DECLARE @Material_New INT = 0, @Material_Deactivated INT = 0;
    DECLARE @Mapping_New INT = 0, @Mapping_SCD2 INT = 0;
    DECLARE @Supply_New INT = 0, @Supply_Closed INT = 0, @Supply_Updated INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- =================================================================
        -- 步骤0：提取源端当前有效快照到临时表
        -- 双源同构契约：ERP和MES视图字段完全一致，仅FROM不同
        -- =================================================================
        SET @StepName = N'步骤0-提取' + @SourceType + N'快照';

        DROP TABLE IF EXISTS #Source_Snapshot;

        IF @SourceType = 'ERP'
        BEGIN
            SELECT 
                MaterialCode, MaterialName, Spec,
                MasterID AS SourceID,
                Warehouse, ISNULL(Warehouse, 'N/A') AS Warehouse_Norm,
                SupplyMode, ProductionDeptCode,
                UOM, LeadTimeDays, SafetyStock,
                InventoryManagementMode, IsActive
            INTO #Source_Snapshot
            FROM ext_ERP_Master_View
            WHERE IsActive = 1;
        END
        ELSE IF @SourceType = 'MES'
        BEGIN
            SELECT 
                MaterialCode, MaterialName, Spec,
                MasterID AS SourceID,       -- MES视图中 MES_ID 已别名为 MasterID
                Warehouse, ISNULL(Warehouse, 'N/A') AS Warehouse_Norm,
                SupplyMode, ProductionDeptCode,
                UOM, LeadTimeDays, SafetyStock,
                InventoryManagementMode, IsActive
            INTO #Source_Snapshot
            FROM ext_MES_Material_View
            WHERE IsActive = 1;
        END

        -- 消歧：同一 MaterialCode + Warehouse 有多行时，取 SourceID 最大的
        ;WITH Ranked AS (
            SELECT *, ROW_NUMBER() OVER (
                PARTITION BY MaterialCode, Warehouse_Norm ORDER BY SourceID DESC
            ) AS RowNum
            FROM #Source_Snapshot
        )
        DELETE FROM Ranked WHERE RowNum > 1;

        -- =================================================================
        -- 步骤1：同步 Material（物料主身份）
        -- MaterialType 由 APS 按 MaterialCode 前缀统一推导
        -- =================================================================
        SET @StepName = N'步骤1-同步Material';

        DROP TABLE IF EXISTS #Material_Source;

        ;WITH MaterialDedup AS (
            SELECT MaterialCode, MaterialName, Spec, UOM,
                   ROW_NUMBER() OVER (PARTITION BY MaterialCode ORDER BY Warehouse_Norm) AS RowNum
            FROM #Source_Snapshot
        )
        SELECT MaterialCode, MaterialName, Spec, UOM
        INTO #Material_Source
        FROM MaterialDedup WHERE RowNum = 1;

        MERGE INTO Material AS target
        USING #Material_Source AS source
        ON target.MaterialCode = source.MaterialCode

        WHEN MATCHED AND (
            target.MaterialName <> source.MaterialName
            OR ISNULL(target.Spec, '') <> ISNULL(source.Spec, '')
            OR target.UOM <> source.UOM
            OR target.IsActive = 0
        )
        THEN UPDATE SET 
            target.MaterialName = source.MaterialName,
            target.Spec = source.Spec,
            target.UOM = source.UOM,
            target.IsActive = 1,
            target.UpdatedAt = @SyncTime

        -- 全新物料 → MaterialType 由 APS 按前缀推导
        WHEN NOT MATCHED BY TARGET
        THEN INSERT (MaterialCode, MaterialName, Spec, MaterialType, UOM, 
                     IsActive, CreatedAt, UpdatedAt)
             VALUES (source.MaterialCode, source.MaterialName, source.Spec,
                     CASE 
                         WHEN source.MaterialCode LIKE 'FG-%' THEN 'FINISHED_GOOD'
                         WHEN source.MaterialCode LIKE 'RAW-%' THEN 'RAW_MATERIAL'
                         WHEN source.MaterialCode LIKE 'WIP-%' THEN 'SEMI_FINISHED'
                         WHEN source.MaterialCode LIKE 'ASSY-%' THEN 'ASSY'
                         ELSE 'UNKNOWN'
                     END,
                     source.UOM, 1, @SyncTime, @SyncTime);

        SET @Material_New = @@ROWCOUNT;

        -- =================================================================
        -- 步骤2：同步 MaterialMapping（SCD Type 2）
        -- 业务键：MaterialCode + Source + Warehouse_Norm
        -- 追踪字段：SourceID
        -- =================================================================
        SET @StepName = N'步骤2-同步MaterialMapping';

        MERGE INTO MaterialMapping AS target
        USING (
            SELECT MaterialCode, SourceID, Warehouse, Warehouse_Norm
            FROM #Source_Snapshot
        ) AS source
        ON target.MaterialCode = source.MaterialCode 
           AND target.Source = @SourceType 
           AND target.IsCurrent = 1
           AND target.Warehouse_Norm = source.Warehouse_Norm

        WHEN MATCHED AND target.SourceID = source.SourceID
        THEN UPDATE SET target.UpdatedAt = @SyncTime

        WHEN MATCHED AND (target.SourceID <> source.SourceID
                          OR target.SourceID IS NULL AND source.SourceID IS NOT NULL
                          OR target.SourceID IS NOT NULL AND source.SourceID IS NULL)
        THEN UPDATE SET 
            target.ValidTo = @SyncTime, target.IsCurrent = 0, target.UpdatedAt = @SyncTime

        WHEN NOT MATCHED BY TARGET 
        THEN INSERT (MaterialCode, SourceID, Warehouse, Source, 
                     ValidFrom, ValidTo, IsCurrent, CreatedAt, UpdatedAt)
             VALUES (source.MaterialCode, source.SourceID, source.Warehouse,
                     @SourceType, @SyncTime, NULL, 1, @SyncTime, @SyncTime)

        WHEN NOT MATCHED BY SOURCE 
             AND target.Source = @SourceType AND target.IsCurrent = 1
        THEN UPDATE SET 
            target.ValidTo = @SyncTime, target.IsCurrent = 0, target.UpdatedAt = @SyncTime;

        SET @Mapping_New = @@ROWCOUNT;

        -- 2b. SCD Type 2闭环：为因SourceID变化被关闭的旧记录插入新版本
        INSERT INTO MaterialMapping (MaterialCode, SourceID, Warehouse, Source,
            ValidFrom, ValidTo, IsCurrent, CreatedAt, UpdatedAt)
        SELECT source.MaterialCode, source.SourceID, source.Warehouse,
            @SourceType, @SyncTime, NULL, 1, @SyncTime, @SyncTime
        FROM #Source_Snapshot AS source
        WHERE EXISTS (
            SELECT 1 FROM MaterialMapping AS old
            WHERE old.MaterialCode = source.MaterialCode
              AND old.Source = @SourceType
              AND old.Warehouse_Norm = source.Warehouse_Norm
              AND old.IsCurrent = 0 AND old.ValidTo = @SyncTime
              AND old.SourceID <> source.SourceID
        );

        SET @Mapping_SCD2 = @@ROWCOUNT;

        -- 2c. 全部仓库映射都被关闭 → Material.IsActive = 0
        UPDATE Material SET IsActive = 0, UpdatedAt = @SyncTime
        WHERE IsActive = 1
          AND NOT EXISTS (
              SELECT 1 FROM MaterialMapping mm
              WHERE mm.MaterialCode = Material.MaterialCode AND mm.IsCurrent = 1
          );

        SET @Material_Deactivated = @@ROWCOUNT;

        -- =================================================================
        -- 步骤3：同步 MaterialSupplyContext（SCD Type 2）
        -- 业务键：MaterialCode + WarehouseCode
        -- 追踪字段：SupplyMode, DefaultProductionDeptCode, LeadTimeDays,
        --           SafetyStock, InventoryManagementMode
        -- =================================================================
        SET @StepName = N'步骤3-同步MaterialSupplyContext';

        DROP TABLE IF EXISTS #Supply_Source;

        SELECT snap.MaterialCode, snap.Warehouse AS WarehouseCode,
            snap.SupplyMode, snap.ProductionDeptCode AS DefaultProductionDeptCode,
            snap.LeadTimeDays, snap.SafetyStock, snap.InventoryManagementMode
        INTO #Supply_Source
        FROM #Source_Snapshot snap
        INNER JOIN MaterialMapping mm
            ON mm.MaterialCode = snap.MaterialCode
            AND mm.Source = @SourceType
            AND mm.Warehouse_Norm = snap.Warehouse_Norm
            AND mm.IsCurrent = 1;

        -- 3b. 属性变化 → 关闭旧版本
        UPDATE ctx SET ctx.ValidTo = @SyncTime, ctx.IsCurrent = 0, ctx.UpdatedAt = @SyncTime
        FROM MaterialSupplyContext ctx
        INNER JOIN #Supply_Source src
            ON ctx.MaterialCode = src.MaterialCode AND ctx.WarehouseCode = src.WarehouseCode
        WHERE ctx.IsCurrent = 1 AND ctx.SourceSystem = @SourceType
          AND (
              ISNULL(ctx.SupplyMode, '') <> ISNULL(src.SupplyMode, '')
              OR ISNULL(ctx.DefaultProductionDeptCode, '') <> ISNULL(src.DefaultProductionDeptCode, '')
              OR ISNULL(ctx.LeadTimeDays, -1) <> ISNULL(src.LeadTimeDays, -1)
              OR ISNULL(ctx.SafetyStock, -1) <> ISNULL(src.SafetyStock, -1)
              OR ISNULL(ctx.InventoryManagementMode, '') <> ISNULL(src.InventoryManagementMode, '')
          );

        SET @Supply_Updated = @@ROWCOUNT;

        -- 3c. 变化的 + 全新的 → 插入新版本
        INSERT INTO MaterialSupplyContext (
            MaterialCode, WarehouseCode, SupplyMode, DefaultProductionDeptCode,
            LeadTimeDays, SafetyStock, InventoryManagementMode, SourceSystem,
            ValidFrom, ValidTo, IsCurrent, CreatedAt, UpdatedAt)
        SELECT src.MaterialCode, src.WarehouseCode, src.SupplyMode,
            src.DefaultProductionDeptCode, src.LeadTimeDays, src.SafetyStock,
            src.InventoryManagementMode, @SourceType,
            @SyncTime, NULL, 1, @SyncTime, @SyncTime
        FROM #Supply_Source src
        WHERE NOT EXISTS (
            SELECT 1 FROM MaterialSupplyContext ctx
            WHERE ctx.MaterialCode = src.MaterialCode
              AND ctx.WarehouseCode = src.WarehouseCode
              AND ctx.IsCurrent = 1 AND ctx.SourceSystem = @SourceType
        );

        SET @Supply_New = @@ROWCOUNT;

        -- 3d. 源端消失的仓库 → 关闭
        UPDATE ctx SET ctx.ValidTo = @SyncTime, ctx.IsCurrent = 0, ctx.UpdatedAt = @SyncTime
        FROM MaterialSupplyContext ctx
        WHERE ctx.IsCurrent = 1 AND ctx.SourceSystem = @SourceType
          AND NOT EXISTS (
              SELECT 1 FROM #Supply_Source src
              WHERE src.MaterialCode = ctx.MaterialCode AND src.WarehouseCode = ctx.WarehouseCode
          );

        SET @Supply_Closed = @@ROWCOUNT;

        -- =================================================================
        -- 步骤4：记录 ETL 日志
        -- =================================================================
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (@BatchNo, 'sp_SyncMasterData[' + @SourceType + ']', 
            N'三表同步完成[' + @SourceType + N'] | Material(新增/更新:' + 
            CAST(@Material_New AS NVARCHAR(10)) + N',停用:' + 
            CAST(@Material_Deactivated AS NVARCHAR(10)) + N') | Mapping(变更:' + 
            CAST(@Mapping_New AS NVARCHAR(10)) + N',SCD2:' + 
            CAST(@Mapping_SCD2 AS NVARCHAR(10)) + N') | SupplyCtx(新增:' + 
            CAST(@Supply_New AS NVARCHAR(10)) + N',变更:' + 
            CAST(@Supply_Updated AS NVARCHAR(10)) + N',关闭:' + 
            CAST(@Supply_Closed AS NVARCHAR(10)) + N')', 
            N'SUCCESS', GETDATE());

        DROP TABLE IF EXISTS #Source_Snapshot, #Material_Source, #Supply_Source;
        COMMIT TRANSACTION;
        SET @ErrorMessage = NULL;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        SET @RowsAffected = 0;
        INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
        VALUES (@BatchNo, @StepName, 
            N'同步失败[' + @SourceType + N']: ' + @ErrorMessage, N'FAILED', GETDATE());
    END CATCH
END
GO
```

---

# 五、双源同构契约说明（2026-04-01 v2.0更新）

v4.0 已将 ERP 和 MES 统一为双源同构三表协同同步：

- **同一个 SP**：`sp_SyncMasterData(@SourceType)` 同时服务 ERP 和 MES
- **同构契约**：`ext_ERP_Master_View` 和 `ext_MES_Material_View` 字段完全一致
- **MES 也执行三表协同**：Material + MaterialMapping + MaterialSupplyContext
- **MES 视图字段对齐**：`MES_ID → MasterID`（别名），`Location → Warehouse`（别名）
- **MaterialType**：不再由 MES 视图提供，统一由 APS 按 MaterialCode 前缀推导

### MaterialType 前缀推导规则

| MaterialCode 前缀 | MaterialType 值 | 说明 |
|-------------------|----------------|------|
| `FG-%` | `FINISHED_GOOD` | 成品 |
| `RAW-%` | `RAW_MATERIAL` | 原材料 |
| `WIP-%` | `SEMI_FINISHED` | 半成品 |
| `ASSY-%` | `ASSY` | 装配类半成品 |
| 其他 | `UNKNOWN` | 未识别，需人工补充 |

---

# 六、DDL 变更记录（2026-04-01 v2.0更新）

本设计对应 DDL 文件 `APS_数据库表结构设计_v4.0.sql` 的 v4.0 版本变更：

| # | 变更点 | 说明 |
|---|--------|------|
| 1 | MaterialMapping 表重构 | 消除 ERP/MES 字段分叉，统一为 SourceID + Warehouse |
| 2 | MaterialMapping 唯一索引简化 | 6列→4列（MaterialCode, Source, Warehouse_Norm, IsCurrent） |
| 3 | MaterialSupplyContext 新增字段 | InventoryManagementMode（STOCKED/NON_STOCKED） |
| 4 | MaterialSupplyContext.SourceSystem | 移除默认值 'ERP'（双源都会写入） |
| 5 | 合并存储过程 | sp_SyncERPMasterData + sp_SyncMESMaterialData → sp_SyncMasterData(@SourceType) |
| 6 | MaterialType 前缀推导 | INSERT 时由 CASE 表达式按前缀推导，不再硬编码 'UNKNOWN' |
| 7 | InventorySourceRule 默认规则 | 新增 ASSY-% 前缀规则 |

---

# 七、校验检查清单（2026-04-01 v2.0更新）

同步完成后，可通过以下查询校验三表一致性（ERP 和 MES 通用）：

```sql
-- 检查1：所有当前有效的 MaterialMapping 都应该有对应的 Material
SELECT mm.MaterialCode, mm.Warehouse, mm.Source
FROM MaterialMapping mm
WHERE mm.IsCurrent = 1
  AND NOT EXISTS (
      SELECT 1 FROM Material m WHERE m.MaterialCode = mm.MaterialCode
  );
-- 预期结果：0 行

-- 检查2：所有当前有效的 MaterialMapping 都应该有对应的 MaterialSupplyContext
SELECT mm.MaterialCode, mm.Warehouse, mm.Source
FROM MaterialMapping mm
WHERE mm.IsCurrent = 1
  AND NOT EXISTS (
      SELECT 1 FROM MaterialSupplyContext ctx
      WHERE ctx.MaterialCode = mm.MaterialCode
        AND ctx.WarehouseCode = mm.Warehouse
        AND ctx.IsCurrent = 1
  );
-- 预期结果：0 行

-- 检查3：IsActive=0 的 Material 不应该还有当前有效的映射
SELECT m.MaterialCode
FROM Material m
WHERE m.IsActive = 0
  AND EXISTS (
      SELECT 1 FROM MaterialMapping mm
      WHERE mm.MaterialCode = m.MaterialCode AND mm.IsCurrent = 1
  );
-- 预期结果：0 行

-- 检查4：MaterialSupplyContext 中不应有孤儿记录（没有对应的有效 MaterialMapping）
SELECT ctx.MaterialCode, ctx.WarehouseCode, ctx.SourceSystem
FROM MaterialSupplyContext ctx
WHERE ctx.IsCurrent = 1
  AND NOT EXISTS (
      SELECT 1 FROM MaterialMapping mm
      WHERE mm.MaterialCode = ctx.MaterialCode
        AND mm.Warehouse = ctx.WarehouseCode
        AND mm.Source = ctx.SourceSystem
        AND mm.IsCurrent = 1
  );
-- 预期结果：0 行

-- 检查5：MaterialType 不应为 'UNKNOWN'（除非确实是未识别的前缀）
SELECT MaterialCode, MaterialType
FROM Material
WHERE MaterialType = 'UNKNOWN' AND IsActive = 1;
-- 预期结果：尽量为 0 行，如有需人工补充前缀规则
```

---

**文档结束**

> 本文档是 ERP/MES 主数据进入 APS 的双源同构三表协同同步设计。
> 核心思想：双源同构契约 + 统一参数化 SP + MaterialType 由 APS 前缀推导 + 三层职责分离。
