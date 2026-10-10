/*
===============================================================================
APS 数据库 v5.1.1 → v5.1.2 冻结兼容升级补丁
日期：2026-08-12
用途：在已有APS数据库上，以“最小ALTER、保护存量数据”为原则对齐V1冻结业务。

重要：
1. 本补丁不是重新建库脚本；正式全量DDL见《APS_数据库表结构设计_v5.1.2_冻结对齐版.sql》。
2. 不删除历史表/历史字段；旧对象若退出V1主链，只停止生成/消费，不在本补丁DROP。
3. 不创建任何假数据/WHERE 1=0 的采购、VMI、Received、厂间在途契约。
4. Planning-only Purchase Placeholder只存在内存，本补丁绝不建表。
5. 若实际数据库与本补丁假设不一致，先停止执行并做对象级比对；不得为了让脚本跑通而修改冻结业务。
===============================================================================
*/

USE APS_Production;
GO

SET XACT_ABORT ON;
GO

-------------------------------------------------------------------------------
-- 1. Task：OrderId降为可空兼容字段；新增PlannedProcessQty
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Task', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Task', N'OrderId') IS NOT NULL
        ALTER TABLE dbo.Task ALTER COLUMN OrderId BIGINT NULL;

    IF COL_LENGTH(N'dbo.Task', N'PlannedProcessQty') IS NULL
        ALTER TABLE dbo.Task ADD PlannedProcessQty DECIMAL(18,4) NULL;
END
GO

-------------------------------------------------------------------------------
-- 2. SupplyFact_Pipeline：补CommitmentStatus；不把NULL解释为确定承诺
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SupplyFact_Pipeline', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.SupplyFact_Pipeline', N'CommitmentStatus') IS NULL
BEGIN
    ALTER TABLE dbo.SupplyFact_Pipeline
        ADD CommitmentStatus NVARCHAR(30) NULL;
END
GO

-------------------------------------------------------------------------------
-- 3. AllocationTaskShare：FinalTask真实Demand份额，多对多最小结构
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.AllocationTaskShare', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllocationTaskShare (
        Id                  BIGINT IDENTITY(1,1),
        PlanVersionId       INT NOT NULL,
        AllocationSequence  BIGINT NOT NULL,
        DemandType          NVARCHAR(50) NOT NULL,
        DemandKey           NVARCHAR(200) NOT NULL,
        RootOrderId         BIGINT NULL,
        TaskId              BIGINT NOT NULL,
        ShareQty            DECIMAL(18,4) NOT NULL,
        CreatedAt           DATETIME2 NOT NULL CONSTRAINT DF_ATS_CreatedAt DEFAULT GETDATE(),
        CONSTRAINT PK_AllocationTaskShare PRIMARY KEY (Id, PlanVersionId),
        CONSTRAINT CK_AllocationTaskShare_Qty CHECK (ShareQty > 0)
    ) ON PS_PlanVersion(PlanVersionId);

    CREATE INDEX IX_ATS_Allocation
        ON dbo.AllocationTaskShare(PlanVersionId, AllocationSequence)
        INCLUDE (TaskId, ShareQty, DemandType, DemandKey)
        ON PS_PlanVersion(PlanVersionId);

    CREATE INDEX IX_ATS_Task
        ON dbo.AllocationTaskShare(TaskId, PlanVersionId)
        INCLUDE (AllocationSequence, ShareQty, DemandKey)
        ON PS_PlanVersion(PlanVersionId);
END
GO

-------------------------------------------------------------------------------
-- 4. PeggingSupplyAllocation：若现实库已存在则只补冻结主链必需列，不破坏历史数据
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.PeggingSupplyAllocation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PeggingSupplyAllocation (
        Id                       BIGINT IDENTITY(1,1),
        PlanVersionId            INT NOT NULL,
        ScheduleRunId            INT NOT NULL,
        AllocationSequence       BIGINT NOT NULL,
        BatchNo                  NVARCHAR(50) NULL,
        RootOrderId              BIGINT NULL,
        RootOrderNo              NVARCHAR(100) NULL,
        CurrentOrderId           BIGINT NULL,
        CurrentOrderNo           NVARCHAR(100) NULL,
        OrderType                NVARCHAR(50) NULL,
        WorksetId                BIGINT NULL,
        MaterialId               INT NOT NULL,
        MaterialCode             NVARCHAR(100) NOT NULL,
        DemandFactoryCode        NVARCHAR(50) NULL,
        DemandStageCode          NVARCHAR(50) NULL,
        DemandQty                DECIMAL(18,4) NULL,
        AllocatedQty             DECIMAL(18,4) NOT NULL,
        SupplyType               NVARCHAR(50) NOT NULL,
        SupplyFactoryCode        NVARCHAR(50) NULL,
        SupplyWarehouseCode      NVARCHAR(50) NULL,
        ERPProperty              NVARCHAR(20) NULL,
        AttachStageCode          NVARCHAR(50) NULL,
        CompletedStageCode       NVARCHAR(50) NULL,
        NextRequiredStageCode    NVARCHAR(50) NULL,
        RemainingStagePathJson   NVARCHAR(MAX) NULL,
        SupplyMode               NVARCHAR(50) NULL,
        CrossFactoryEdgeId       BIGINT NULL,
        TransportLeadTimeHours   INT NULL,
        ETA                      DATETIME2 NULL,
        KnownAvailableTime       DATETIME2 NULL,
        CommitmentStatus         NVARCHAR(30) NULL,
        SupplyDocumentType       NVARCHAR(50) NULL,
        SupplyDocumentNo         NVARCHAR(100) NULL,
        CreatedAt                DATETIME2 NOT NULL CONSTRAINT DF_PSA_CreatedAt DEFAULT GETDATE(),
        CONSTRAINT PK_PeggingSupplyAllocation PRIMARY KEY (Id, PlanVersionId),
        CONSTRAINT CK_PSA_AllocatedQty CHECK (AllocatedQty > 0)
    ) ON PS_PlanVersion(PlanVersionId);

    CREATE UNIQUE INDEX UX_PSA_AllocationSequence
        ON dbo.PeggingSupplyAllocation(PlanVersionId, AllocationSequence)
        ON PS_PlanVersion(PlanVersionId);

    CREATE INDEX IX_PSA_Material
        ON dbo.PeggingSupplyAllocation(PlanVersionId, MaterialId, SupplyType)
        INCLUDE (AllocatedQty, KnownAvailableTime, CommitmentStatus)
        ON PS_PlanVersion(PlanVersionId);

    CREATE INDEX IX_PSA_Document
        ON dbo.PeggingSupplyAllocation(SupplyDocumentNo, PlanVersionId)
        WHERE SupplyDocumentNo IS NOT NULL
        ON PS_PlanVersion(PlanVersionId);
END
ELSE
BEGIN
    -- 已有现实数据时，新增列先允许NULL，避免为历史数据伪造业务值。
    IF COL_LENGTH(N'dbo.PeggingSupplyAllocation', N'AllocationSequence') IS NULL
        ALTER TABLE dbo.PeggingSupplyAllocation ADD AllocationSequence BIGINT NULL;
    IF COL_LENGTH(N'dbo.PeggingSupplyAllocation', N'KnownAvailableTime') IS NULL
        ALTER TABLE dbo.PeggingSupplyAllocation ADD KnownAvailableTime DATETIME2 NULL;
    IF COL_LENGTH(N'dbo.PeggingSupplyAllocation', N'CommitmentStatus') IS NULL
        ALTER TABLE dbo.PeggingSupplyAllocation ADD CommitmentStatus NVARCHAR(30) NULL;
    IF COL_LENGTH(N'dbo.PeggingSupplyAllocation', N'TransportLeadTimeHours') IS NULL
        ALTER TABLE dbo.PeggingSupplyAllocation ADD TransportLeadTimeHours INT NULL;
    IF COL_LENGTH(N'dbo.PeggingSupplyAllocation', N'SupplyDocumentType') IS NULL
        ALTER TABLE dbo.PeggingSupplyAllocation ADD SupplyDocumentType NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.PeggingSupplyAllocation', N'SupplyDocumentNo') IS NULL
        ALTER TABLE dbo.PeggingSupplyAllocation ADD SupplyDocumentNo NVARCHAR(100) NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.PeggingSupplyAllocation')
          AND name = N'UX_PSA_AllocationSequence'
    )
    BEGIN
        -- 历史行可NULL；新代码必须对新Allocation写非NULL且唯一的AllocationSequence。
        CREATE UNIQUE INDEX UX_PSA_AllocationSequence
            ON dbo.PeggingSupplyAllocation(PlanVersionId, AllocationSequence)
            WHERE AllocationSequence IS NOT NULL;
    END
END
GO

-------------------------------------------------------------------------------
-- 5. PI位置最小快照：一张表多行互斥闭合，不建Header+Slice平台
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ProductionInstructionPositionSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionInstructionPositionSnapshot (
        Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
        ScheduleRunId            INT NOT NULL,
        PlanVersionId            INT NOT NULL,
        ProductionInstructionNo  NVARCHAR(100) NOT NULL,
        MaterialId               INT NOT NULL,
        MaterialCode             NVARCHAR(100) NOT NULL,
        PositionType             NVARCHAR(50) NOT NULL,
        Quantity                 DECIMAL(18,4) NOT NULL,
        CurrentStageCode         NVARCHAR(50) NULL,
        NextStageCode            NVARCHAR(50) NULL,
        AvailableTime            DATETIME2 NULL,
        SourceType               NVARCHAR(50) NULL,
        SourceKey                NVARCHAR(200) NULL,
        IssueCode                NVARCHAR(100) NULL,
        Confidence               NVARCHAR(30) NULL,
        CreatedAt                DATETIME2 NOT NULL CONSTRAINT DF_PIPosition_CreatedAt DEFAULT GETDATE(),
        CONSTRAINT CK_PIPosition_Qty CHECK (Quantity >= 0)
    );

    CREATE INDEX IX_PIPosition_Plan_PI
        ON dbo.ProductionInstructionPositionSnapshot(PlanVersionId, ProductionInstructionNo)
        INCLUDE (PositionType, Quantity, CurrentStageCode, NextStageCode, AvailableTime);

    CREATE INDEX IX_PIPosition_Run
        ON dbo.ProductionInstructionPositionSnapshot(ScheduleRunId, ProductionInstructionNo);
END
GO

-------------------------------------------------------------------------------
-- 6. Strict Binding + Demand Protection共用一个锁实体；不另建保护平台
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.DemandSupplyHardLock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DemandSupplyHardLock (
        Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
        LockType                 NVARCHAR(30) NOT NULL,
        DemandType               NVARCHAR(50) NOT NULL,
        DemandKey                NVARCHAR(200) NOT NULL,
        SupplyType               NVARCHAR(50) NOT NULL,
        SupplyKey                NVARCHAR(200) NOT NULL,
        LockedQty                DECIMAL(18,4) NOT NULL,
        SourcePlanVersionId      INT NULL,
        SourceAllocationSequence BIGINT NULL,
        Status                   NVARCHAR(20) NOT NULL CONSTRAINT DF_DSHL_Status DEFAULT N'ACTIVE',
        CreatedAt                DATETIME2 NOT NULL CONSTRAINT DF_DSHL_CreatedAt DEFAULT GETDATE(),
        CreatedBy                NVARCHAR(100) NULL,
        ReleasedAt               DATETIME2 NULL,
        ReleasedBy               NVARCHAR(100) NULL,
        ReleaseReason            NVARCHAR(500) NULL,
        CONSTRAINT CK_DSHL_Type CHECK (LockType IN (N'STRICT_BINDING',N'DEMAND_PROTECTION')),
        CONSTRAINT CK_DSHL_Status CHECK (Status IN (N'ACTIVE',N'RELEASED',N'BROKEN')),
        CONSTRAINT CK_DSHL_Qty CHECK (LockedQty > 0)
    );

    CREATE INDEX IX_DSHL_Demand
        ON dbo.DemandSupplyHardLock(DemandKey, LockType, Status)
        INCLUDE (SupplyKey, LockedQty);
    CREATE INDEX IX_DSHL_Supply
        ON dbo.DemandSupplyHardLock(SupplyKey, LockType, Status)
        INCLUDE (DemandKey, LockedQty);
END
GO

-------------------------------------------------------------------------------
-- 7. Domain_Dependency：移除“固定2天”为数据库默认权威；保留字段兼容
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Domain_Dependency', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Domain_Dependency', N'DefaultLeadTimeDays') IS NOT NULL
BEGIN
    DECLARE @dfName SYSNAME;
    SELECT @dfName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
      ON c.object_id = dc.parent_object_id
     AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Domain_Dependency')
      AND c.name = N'DefaultLeadTimeDays';

    IF @dfName IS NOT NULL
        EXEC(N'ALTER TABLE dbo.Domain_Dependency DROP CONSTRAINT ' + QUOTENAME(@dfName) + N';');

    ALTER TABLE dbo.Domain_Dependency
        ADD CONSTRAINT DF_DomainDependency_DefaultLeadTimeDays DEFAULT (0) FOR DefaultLeadTimeDays;
END
GO

-------------------------------------------------------------------------------
-- 8. InventoryBalance：不改物理结构，只明确不再由排程永久更新AllocatedQty
-------------------------------------------------------------------------------
PRINT N'V1_FREEZE: InventoryBalance.AllocatedQty保留兼容，但运行期Allocation必须在2号位内存SupplyBalance中维护；不得永久UPDATE该字段作为分配真相。';
GO

-------------------------------------------------------------------------------
-- 9. 退出V1主链但不DROP的历史/兼容对象
-------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FrozenZoneSnapshot', N'U') IS NOT NULL
    PRINT N'DEPRECATED_V1: FrozenZoneSnapshot存在但不得继续生成/消费；Firm/Frozen由上一ACTIVE锚点+执行事实承接。';
IF OBJECT_ID(N'dbo.VirtualInventoryBalance', N'U') IS NOT NULL
    PRINT N'DEPRECATED_V1: VirtualInventoryBalance存在但不得继续生成/消费；跨Domain使用内存Quantity-Time事实。';
GO

-------------------------------------------------------------------------------
-- 10. Timed Supply / Received真实数据契约部署阻断检查
--     不在此补丁虚构采购/VMI/到厂未入库源View名。
-------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ValidateV1FrozenDataContracts
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.ext_PipelineSupply_Source_View', N'V') IS NULL
        THROW 51101, 'V1 deployment blocker: ext_PipelineSupply_Source_View is missing; bind real Timed Supply ODS sources first', 1;

    IF OBJECT_ID(N'dbo.ext_ERP_Received_ByDocument_View', N'V') IS NULL
        THROW 51102, 'V1 deployment blocker: ext_ERP_Received_ByDocument_View is missing; bind real ERP Received source first', 1;

    PRINT N'V1 frozen data contracts: required wrapper views exist. 0 business rows is legal; missing view is not.';
END
GO

-------------------------------------------------------------------------------
-- 11. 明确禁止通过补丁新增的V1对象
-------------------------------------------------------------------------------
PRINT N'V1_NO_BUILD: PeggingAllocationLedger / PI Header+Slice / DemandProtection second table / FrozenZoneSnapshot platform / VirtualInventoryBalance platform / PlanningPurchasePlaceholder table / finite logistics Task platform.';
GO

-- 部署完成后由DBA显式执行：
-- EXEC dbo.sp_ValidateV1FrozenDataContracts;

-------------------------------------------------------------------------------
-- 9. 跨表主键类型一致性：Order/Task主键为BIGINT，相关逻辑引用只做安全加宽
-------------------------------------------------------------------------------
-- 纯技术一致性修正，不改变业务语义。INT→BIGINT为安全加宽；若对象不存在则跳过。
IF OBJECT_ID(N'dbo.ScheduleExplanationFact', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.types t ON c.user_type_id=t.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.ScheduleExplanationFact') AND c.name=N'OrderId' AND t.name=N'int'
    )
        ALTER TABLE dbo.ScheduleExplanationFact ALTER COLUMN OrderId BIGINT NULL;

    IF EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.types t ON c.user_type_id=t.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.ScheduleExplanationFact') AND c.name=N'TaskId' AND t.name=N'int'
    )
        ALTER TABLE dbo.ScheduleExplanationFact ALTER COLUMN TaskId BIGINT NULL;
END
GO

IF OBJECT_ID(N'dbo.OrderScheduleSummary', N'U') IS NOT NULL
AND EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id=t.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.OrderScheduleSummary') AND c.name=N'OrderId' AND t.name=N'int'
)
BEGIN
    ALTER TABLE dbo.OrderScheduleSummary ALTER COLUMN OrderId BIGINT NOT NULL;
END
GO

PRINT N'V1_FREEZE: 已完成Order/Task逻辑引用字段INT→BIGINT安全加宽检查。';
GO
