-- =============================================
-- APS系统数据库表结构
-- =============================================

-- 作业表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Jobs' AND xtype='U')
BEGIN
    CREATE TABLE Jobs (
        Id NVARCHAR(50) PRIMARY KEY,
        Code NVARCHAR(100) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        Priority INT DEFAULT 0,
        DueDate DATETIME2 NULL,
        Status NVARCHAR(50) DEFAULT 'Created',
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 DEFAULT GETDATE(),
        INDEX IX_Jobs_DueDate (DueDate),
        INDEX IX_Jobs_Status (Status),
        INDEX IX_Jobs_CreatedAt (CreatedAt)
    );
END

-- 机器表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Machines' AND xtype='U')
BEGIN
    CREATE TABLE Machines (
        Id NVARCHAR(50) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Type NVARCHAR(50) NOT NULL,
        IsAvailable BIT DEFAULT 1,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 DEFAULT GETDATE(),
        INDEX IX_Machines_IsAvailable (IsAvailable),
        INDEX IX_Machines_Type (Type)
    );
END

-- 工序表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Operations' AND xtype='U')
BEGIN
    CREATE TABLE Operations (
        Id NVARCHAR(50) PRIMARY KEY,
        JobId NVARCHAR(50) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        MachineId NVARCHAR(50) NOT NULL,
        Duration INT NOT NULL,
        Sequence INT NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE,
        FOREIGN KEY (MachineId) REFERENCES Machines(Id),
        INDEX IX_Operations_JobId (JobId),
        INDEX IX_Operations_MachineId (MachineId)
    );
END

-- 排程结果表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ScheduleResults' AND xtype='U')
BEGIN
    CREATE TABLE ScheduleResults (
        Id NVARCHAR(50) PRIMARY KEY,
        Success BIT NOT NULL,
        Message NVARCHAR(500),
        Makespan DECIMAL(18,2),
        Warnings NVARCHAR(MAX),
        SolveDuration TIME,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        INDEX IX_ScheduleResults_CreatedAt (CreatedAt),
        INDEX IX_ScheduleResults_Success (Success)
    );
END

-- 工序排程表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='OperationSchedules' AND xtype='U')
BEGIN
    CREATE TABLE OperationSchedules (
        Id NVARCHAR(50) PRIMARY KEY,
        ScheduleId NVARCHAR(50) NOT NULL,
        OperationId NVARCHAR(50) NOT NULL,
        JobId NVARCHAR(50) NOT NULL,
        JobCode NVARCHAR(100) NOT NULL,
        JobName NVARCHAR(200) NOT NULL,
        MachineId NVARCHAR(50) NOT NULL,
        StartTime DATETIME2 NOT NULL,
        EndTime DATETIME2 NOT NULL,
        Sequence INT NOT NULL,
        OperationName NVARCHAR(200) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        FOREIGN KEY (ScheduleId) REFERENCES ScheduleResults(Id) ON DELETE CASCADE,
        FOREIGN KEY (OperationId) REFERENCES Operations(Id),
        FOREIGN KEY (JobId) REFERENCES Jobs(Id),
        FOREIGN KEY (MachineId) REFERENCES Machines(Id),
        INDEX IX_OperationSchedules_ScheduleId (ScheduleId),
        INDEX IX_OperationSchedules_JobId (JobId),
        INDEX IX_OperationSchedules_MachineId (MachineId),
        INDEX IX_OperationSchedules_StartTime (StartTime),
        INDEX IX_OperationSchedules_EndTime (EndTime)
    );
END

-- BOM节点表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BomNodes' AND xtype='U')
BEGIN
    CREATE TABLE BomNodes (
        Id NVARCHAR(50) PRIMARY KEY,
        MaterialId NVARCHAR(50) NOT NULL,
        MaterialName NVARCHAR(200) NOT NULL,
        Quantity DECIMAL(18,4) NOT NULL,
        Unit NVARCHAR(20) NOT NULL,
        Level INT NOT NULL,
        ParentId NVARCHAR(50) NULL,
        ProductId NVARCHAR(50) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        FOREIGN KEY (ParentId) REFERENCES BomNodes(Id),
        INDEX IX_BomNodes_MaterialId (MaterialId),
        INDEX IX_BomNodes_ProductId (ProductId),
        INDEX IX_BomNodes_ParentId (ParentId),
        INDEX IX_BomNodes_Level (Level)
    );
END

-- 库存表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Inventory' AND xtype='U')
BEGIN
    CREATE TABLE Inventory (
        Id NVARCHAR(50) PRIMARY KEY,
        MaterialId NVARCHAR(50) NOT NULL,
        WarehouseId NVARCHAR(50) NOT NULL,
        Quantity DECIMAL(18,4) NOT NULL,
        AvailableQuantity DECIMAL(18,4) NOT NULL,
        Unit NVARCHAR(20) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 DEFAULT GETDATE(),
        INDEX IX_Inventory_MaterialId (MaterialId),
        INDEX IX_Inventory_WarehouseId (WarehouseId),
        INDEX IX_Inventory_MaterialWarehouse (MaterialId, WarehouseId)
    );
END

-- 库存分配表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='InventoryAllocations' AND xtype='U')
BEGIN
    CREATE TABLE InventoryAllocations (
        Id NVARCHAR(50) PRIMARY KEY,
        InventoryId NVARCHAR(50) NOT NULL,
        OrderId NVARCHAR(50) NOT NULL,
        AllocatedQuantity DECIMAL(18,4) NOT NULL,
        AllocationDate DATETIME2 DEFAULT GETDATE(),
        Status NVARCHAR(20) DEFAULT 'Allocated',
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        FOREIGN KEY (InventoryId) REFERENCES Inventory(Id),
        INDEX IX_InventoryAllocations_InventoryId (InventoryId),
        INDEX IX_InventoryAllocations_OrderId (OrderId),
        INDEX IX_InventoryAllocations_Status (Status)
    );
END

-- =============================================
-- 示例数据插入
-- =============================================

-- 插入示例机器数据
IF NOT EXISTS (SELECT 1 FROM Machines)
BEGIN
    INSERT INTO Machines (Id, Name, Type, IsAvailable) VALUES
    ('M001', 'CNC机床-001', 'CNC', 1),
    ('M002', 'CNC机床-002', 'CNC', 1),
    ('M003', '车床-001', 'Lathe', 1),
    ('M004', '铣床-001', 'Mill', 1),
    ('M005', '磨床-001', 'Grinder', 1);
END

-- 插入示例作业数据
IF NOT EXISTS (SELECT 1 FROM Jobs)
BEGIN
    INSERT INTO Jobs (Id, Code, Name, Priority, DueDate, Status) VALUES
    ('JOB001', 'ORD-2023-001', '客户A订单-001', 1, DATEADD(DAY, 7, GETDATE()), 'Created'),
    ('JOB002', 'ORD-2023-002', '客户B订单-001', 2, DATEADD(DAY, 5, GETDATE()), 'Created'),
    ('JOB003', 'ORD-2023-003', '客户C订单-001', 3, DATEADD(DAY, 10, GETDATE()), 'Created');
END

-- 插入示例工序数据
IF NOT EXISTS (SELECT 1 FROM Operations)
BEGIN
    INSERT INTO Operations (Id, JobId, Name, MachineId, Duration, Sequence) VALUES
    ('OP001', 'JOB001', '粗加工', 'M001', 120, 1),
    ('OP002', 'JOB001', '精加工', 'M002', 90, 2),
    ('OP003', 'JOB002', '车削加工', 'M003', 60, 1),
    ('OP004', 'JOB002', '铣削加工', 'M004', 80, 2),
    ('OP005', 'JOB003', '磨削加工', 'M005', 100, 1);
END

-- 插入示例库存数据
IF NOT EXISTS (SELECT 1 FROM Inventory)
BEGIN
    INSERT INTO Inventory (Id, MaterialId, WarehouseId, Quantity, AvailableQuantity, Unit) VALUES
    ('INV001', 'MAT001', 'WH001', 1000.0, 800.0, 'KG'),
    ('INV002', 'MAT002', 'WH001', 500.0, 450.0, 'PCS'),
    ('INV003', 'MAT003', 'WH002', 2000.0, 1800.0, 'L');
END

-- =============================================
-- 常用查询视图
-- =============================================

-- 作业详情视图
IF EXISTS (SELECT * FROM sysobjects WHERE name='vJobDetails' AND xtype='V')
    DROP VIEW vJobDetails;
GO

CREATE VIEW vJobDetails AS
SELECT 
    j.Id,
    j.Code,
    j.Name,
    j.Priority,
    j.DueDate,
    j.Status,
    j.CreatedAt,
    j.UpdatedAt,
    COUNT(o.Id) AS OperationCount,
    SUM(o.Duration) AS TotalDuration
FROM Jobs j
LEFT JOIN Operations o ON j.Id = o.JobId
GROUP BY j.Id, j.Code, j.Name, j.Priority, j.DueDate, j.Status, j.CreatedAt, j.UpdatedAt;
GO

-- 机器利用率视图
IF EXISTS (SELECT * FROM sysobjects WHERE name='vMachineUtilization' AND xtype='V')
    DROP VIEW vMachineUtilization;
GO

CREATE VIEW vMachineUtilization AS
SELECT 
    m.Id,
    m.Name,
    m.Type,
    m.IsAvailable,
    COUNT(os.Id) AS ScheduledOperations,
    SUM(DATEDIFF(MINUTE, os.StartTime, os.EndTime)) AS TotalScheduledMinutes
FROM Machines m
LEFT JOIN OperationSchedules os ON m.Id = os.MachineId
GROUP BY m.Id, m.Name, m.Type, m.IsAvailable;
GO

-- =============================================
-- 存储过程示例
-- =============================================

-- 获取作业排程结果的存储过程
IF EXISTS (SELECT * FROM sysobjects WHERE name='spGetJobScheduleResult' AND xtype='P')
    DROP PROCEDURE spGetJobScheduleResult;
GO

CREATE PROCEDURE spGetJobScheduleResult
    @JobId NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        j.Id AS JobId,
        j.Code AS JobCode,
        j.Name AS JobName,
        j.Priority,
        j.DueDate,
        os.OperationId,
        os.OperationName,
        os.MachineId,
        m.Name AS MachineName,
        os.StartTime,
        os.EndTime,
        os.Sequence
    FROM Jobs j
    LEFT JOIN OperationSchedules os ON j.Id = os.JobId
    LEFT JOIN Machines m ON os.MachineId = m.Id
    WHERE j.Id = @JobId
    ORDER BY os.Sequence;
END
GO

-- 批量更新作业状态的存储过程
IF EXISTS (SELECT * FROM sysobjects WHERE name='spBatchUpdateJobStatus' AND xtype='P')
    DROP PROCEDURE spBatchUpdateJobStatus;
GO

CREATE PROCEDURE spBatchUpdateJobStatus
    @JobIds NVARCHAR(MAX),
    @NewStatus NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Jobs
    SET Status = @NewStatus, UpdatedAt = GETDATE()
    WHERE Id IN (
        SELECT value FROM STRING_SPLIT(@JobIds, ',')
    );
END
GO

PRINT 'APS系统数据库结构创建完成！';
