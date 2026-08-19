-- =============================================
-- APS_Auth 数据库 DDL 脚本
-- 版本: v1.0
-- 创建日期: 2026-03-24
-- 用途: APS 权限与审批系统数据库
-- =============================================

USE master;
GO

-- 创建数据库
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'APS_Auth')
BEGIN
    CREATE DATABASE APS_Auth;
    PRINT 'Database APS_Auth created successfully.';
END
ELSE
BEGIN
    PRINT 'Database APS_Auth already exists.';
END
GO

USE APS_Auth;
GO

-- =============================================
-- 1. User（用户表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'User')
BEGIN
    CREATE TABLE [User] (
        Id INT PRIMARY KEY IDENTITY(1,1),
        LoginName NVARCHAR(50) NOT NULL,
        DisplayName NVARCHAR(100) NOT NULL,
        PasswordHash NVARCHAR(500) NOT NULL,
        Email NVARCHAR(100),
        PhoneNumber NVARCHAR(20),
        IsEnabled BIT NOT NULL DEFAULT 1,
        LastLoginAt DATETIME2,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_User_LoginName UNIQUE (LoginName),
        CONSTRAINT CK_User_LoginName CHECK (LEN(LoginName) >= 3)
    );

    CREATE INDEX IX_User_LoginName ON [User](LoginName);
    CREATE INDEX IX_User_IsEnabled ON [User](IsEnabled);
    
    PRINT 'Table [User] created successfully.';
END
GO

-- =============================================
-- 2. Role（角色表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Role')
BEGIN
    CREATE TABLE [Role] (
        Id INT PRIMARY KEY IDENTITY(1,1),
        RoleCode NVARCHAR(50) NOT NULL,
        RoleName NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500),
        IsSystemRole BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_Role_RoleCode UNIQUE (RoleCode),
        CONSTRAINT CK_Role_RoleCode CHECK (RoleCode LIKE 'aps.%')
    );

    CREATE INDEX IX_Role_RoleCode ON [Role](RoleCode);
    
    PRINT 'Table [Role] created successfully.';
END
GO

-- =============================================
-- 3. Permission（权限表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Permission')
BEGIN
    CREATE TABLE [Permission] (
        Id INT PRIMARY KEY IDENTITY(1,1),
        PermissionCode NVARCHAR(100) NOT NULL,
        PermissionName NVARCHAR(100) NOT NULL,
        Module NVARCHAR(50) NOT NULL,
        ActionType NVARCHAR(50) NOT NULL,
        Description NVARCHAR(500),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_Permission_Code UNIQUE (PermissionCode),
        CONSTRAINT CK_Permission_Code CHECK (PermissionCode LIKE 'aps.%')
    );

    CREATE INDEX IX_Permission_Module ON [Permission](Module);
    CREATE INDEX IX_Permission_Code ON [Permission](PermissionCode);
    
    PRINT 'Table [Permission] created successfully.';
END
GO

-- =============================================
-- 4. UserRole（用户角色关联表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserRole')
BEGIN
    CREATE TABLE UserRole (
        UserId INT NOT NULL,
        RoleId INT NOT NULL,
        AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        AssignedBy INT,
        PRIMARY KEY (UserId, RoleId),
        FOREIGN KEY (UserId) REFERENCES [User](Id) ON DELETE CASCADE,
        FOREIGN KEY (RoleId) REFERENCES [Role](Id) ON DELETE CASCADE,
        FOREIGN KEY (AssignedBy) REFERENCES [User](Id)
    );

    CREATE INDEX IX_UserRole_UserId ON UserRole(UserId);
    CREATE INDEX IX_UserRole_RoleId ON UserRole(RoleId);
    
    PRINT 'Table UserRole created successfully.';
END
GO

-- =============================================
-- 5. RolePermission（角色权限关联表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RolePermission')
BEGIN
    CREATE TABLE RolePermission (
        RoleId INT NOT NULL,
        PermissionId INT NOT NULL,
        AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        PRIMARY KEY (RoleId, PermissionId),
        FOREIGN KEY (RoleId) REFERENCES [Role](Id) ON DELETE CASCADE,
        FOREIGN KEY (PermissionId) REFERENCES [Permission](Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_RolePermission_RoleId ON RolePermission(RoleId);
    CREATE INDEX IX_RolePermission_PermissionId ON RolePermission(PermissionId);
    
    PRINT 'Table RolePermission created successfully.';
END
GO

-- =============================================
-- 6. DataScopePolicy（数据范围策略表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DataScopePolicy')
BEGIN
    CREATE TABLE DataScopePolicy (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ScopeType NVARCHAR(50) NOT NULL,
        ScopeValue NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT CK_DataScope_Type CHECK (ScopeType IN ('Factory', 'ProductFamily', 'ResourceOrgGroup', 'Global')),
        CONSTRAINT UQ_DataScope_Type_Value UNIQUE (ScopeType, ScopeValue)
    );

    CREATE INDEX IX_DataScope_Type ON DataScopePolicy(ScopeType);
    CREATE INDEX IX_DataScope_Value ON DataScopePolicy(ScopeValue);
    
    PRINT 'Table DataScopePolicy created successfully.';
END
GO

-- =============================================
-- 7. UserDataScope（用户数据范围表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserDataScope')
BEGIN
    CREATE TABLE UserDataScope (
        UserId INT NOT NULL,
        ScopePolicyId INT NOT NULL,
        AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        AssignedBy INT,
        PRIMARY KEY (UserId, ScopePolicyId),
        FOREIGN KEY (UserId) REFERENCES [User](Id) ON DELETE CASCADE,
        FOREIGN KEY (ScopePolicyId) REFERENCES DataScopePolicy(Id) ON DELETE CASCADE,
        FOREIGN KEY (AssignedBy) REFERENCES [User](Id)
    );

    CREATE INDEX IX_UserDataScope_UserId ON UserDataScope(UserId);
    CREATE INDEX IX_UserDataScope_ScopePolicyId ON UserDataScope(ScopePolicyId);
    
    PRINT 'Table UserDataScope created successfully.';
END
GO

-- =============================================
-- 8. RoleDataScope（角色数据范围表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RoleDataScope')
BEGIN
    CREATE TABLE RoleDataScope (
        RoleId INT NOT NULL,
        ScopePolicyId INT NOT NULL,
        AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        PRIMARY KEY (RoleId, ScopePolicyId),
        FOREIGN KEY (RoleId) REFERENCES [Role](Id) ON DELETE CASCADE,
        FOREIGN KEY (ScopePolicyId) REFERENCES DataScopePolicy(Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_RoleDataScope_RoleId ON RoleDataScope(RoleId);
    CREATE INDEX IX_RoleDataScope_ScopePolicyId ON RoleDataScope(ScopePolicyId);
    
    PRINT 'Table RoleDataScope created successfully.';
END
GO

-- =============================================
-- 9. AuditLog（审计日志表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLog')
BEGIN
    CREATE TABLE AuditLog (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        UserId INT NOT NULL,
        UserName NVARCHAR(100) NOT NULL,
        ActionCode NVARCHAR(100) NOT NULL,
        ObjectType NVARCHAR(50),
        ObjectId NVARCHAR(100),
        Result NVARCHAR(20) NOT NULL,
        OccurredAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        ClientIp NVARCHAR(50),
        PlanVersionId INT,
        BatchNo NVARCHAR(50),
        ApprovalId INT,
        RequestData NVARCHAR(MAX),
        ResponseData NVARCHAR(MAX),
        ErrorMessage NVARCHAR(MAX),
        FOREIGN KEY (UserId) REFERENCES [User](Id),
        CONSTRAINT CK_AuditLog_Result CHECK (Result IN ('Success', 'Failed', 'Denied'))
    );

    CREATE INDEX IX_AuditLog_UserId ON AuditLog(UserId);
    CREATE INDEX IX_AuditLog_ActionCode ON AuditLog(ActionCode);
    CREATE INDEX IX_AuditLog_OccurredAt ON AuditLog(OccurredAt);
    CREATE INDEX IX_AuditLog_PlanVersionId ON AuditLog(PlanVersionId);
    CREATE INDEX IX_AuditLog_BatchNo ON AuditLog(BatchNo);
    CREATE INDEX IX_AuditLog_Result ON AuditLog(Result);
    
    PRINT 'Table AuditLog created successfully.';
END
GO

-- =============================================
-- 10. ApprovalFlow（审批流表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApprovalFlow')
BEGIN
    CREATE TABLE ApprovalFlow (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ApprovalType NVARCHAR(50) NOT NULL,
        ObjectType NVARCHAR(50) NOT NULL,
        ObjectId NVARCHAR(100) NOT NULL,
        ApplicantUserId INT NOT NULL,
        ApplicantUserName NVARCHAR(100) NOT NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        CurrentNodeSeq INT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CompletedAt DATETIME2,
        PlanVersionId INT,
        BatchNo NVARCHAR(50),
        Reason NVARCHAR(1000),
        FOREIGN KEY (ApplicantUserId) REFERENCES [User](Id),
        CONSTRAINT CK_ApprovalFlow_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected', 'Cancelled'))
    );

    CREATE INDEX IX_ApprovalFlow_Status ON ApprovalFlow(Status);
    CREATE INDEX IX_ApprovalFlow_ApplicantUserId ON ApprovalFlow(ApplicantUserId);
    CREATE INDEX IX_ApprovalFlow_Type ON ApprovalFlow(ApprovalType);
    CREATE INDEX IX_ApprovalFlow_CreatedAt ON ApprovalFlow(CreatedAt);
    
    PRINT 'Table ApprovalFlow created successfully.';
END
GO

-- =============================================
-- 11. ApprovalNode（审批节点表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApprovalNode')
BEGIN
    CREATE TABLE ApprovalNode (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ApprovalFlowId INT NOT NULL,
        NodeSeq INT NOT NULL,
        ApproverRoleId INT,
        ApproverUserId INT,
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        FOREIGN KEY (ApprovalFlowId) REFERENCES ApprovalFlow(Id) ON DELETE CASCADE,
        FOREIGN KEY (ApproverRoleId) REFERENCES [Role](Id),
        FOREIGN KEY (ApproverUserId) REFERENCES [User](Id),
        CONSTRAINT CK_ApprovalNode_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected', 'Skipped')),
        CONSTRAINT UQ_ApprovalNode_Flow_Seq UNIQUE (ApprovalFlowId, NodeSeq)
    );

    CREATE INDEX IX_ApprovalNode_FlowId ON ApprovalNode(ApprovalFlowId);
    CREATE INDEX IX_ApprovalNode_Status ON ApprovalNode(Status);
    CREATE INDEX IX_ApprovalNode_RoleId ON ApprovalNode(ApproverRoleId);
    CREATE INDEX IX_ApprovalNode_UserId ON ApprovalNode(ApproverUserId);
    
    PRINT 'Table ApprovalNode created successfully.';
END
GO

-- =============================================
-- 12. ApprovalRecord（审批记录表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApprovalRecord')
BEGIN
    CREATE TABLE ApprovalRecord (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ApprovalNodeId INT NOT NULL,
        ApproverUserId INT NOT NULL,
        ApproverUserName NVARCHAR(100) NOT NULL,
        Decision NVARCHAR(20) NOT NULL,
        Comment NVARCHAR(1000),
        ApprovedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        FOREIGN KEY (ApprovalNodeId) REFERENCES ApprovalNode(Id) ON DELETE CASCADE,
        FOREIGN KEY (ApproverUserId) REFERENCES [User](Id),
        CONSTRAINT CK_ApprovalRecord_Decision CHECK (Decision IN ('Approved', 'Rejected'))
    );

    CREATE INDEX IX_ApprovalRecord_NodeId ON ApprovalRecord(ApprovalNodeId);
    CREATE INDEX IX_ApprovalRecord_ApproverUserId ON ApprovalRecord(ApproverUserId);
    CREATE INDEX IX_ApprovalRecord_ApprovedAt ON ApprovalRecord(ApprovedAt);
    
    PRINT 'Table ApprovalRecord created successfully.';
END
GO

-- =============================================
-- 13. ApprovalRule（审批规则表）
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApprovalRule')
BEGIN
    CREATE TABLE ApprovalRule (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ApprovalType NVARCHAR(50) NOT NULL,
        FactoryId INT,
        ProductFamilyId INT,
        ResourceOrgGroupId INT,  -- v5.0：ResourceGroup→ResourceOrgGroup
        NodeSeq INT NOT NULL,
        ApproverRoleId INT NOT NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        FOREIGN KEY (ApproverRoleId) REFERENCES [Role](Id)
    );

    CREATE INDEX IX_ApprovalRule_Type ON ApprovalRule(ApprovalType);
    CREATE INDEX IX_ApprovalRule_IsEnabled ON ApprovalRule(IsEnabled);
    CREATE INDEX IX_ApprovalRule_FactoryId ON ApprovalRule(FactoryId);
    CREATE INDEX IX_ApprovalRule_ProductFamilyId ON ApprovalRule(ProductFamilyId);
    CREATE INDEX IX_ApprovalRule_ResourceOrgGroupId ON ApprovalRule(ResourceOrgGroupId);
    
    PRINT 'Table ApprovalRule created successfully.';
END
GO

-- =============================================
-- 初始化数据：预置角色
-- =============================================
IF NOT EXISTS (SELECT * FROM [Role] WHERE RoleCode = 'aps.admin.system')
BEGIN
    INSERT INTO [Role] (RoleCode, RoleName, Description, IsSystemRole)
    VALUES 
        ('aps.admin.system', '系统管理员', '最高权限，管理所有系统配置和用户', 1),
        ('aps.admin.aps', 'APS管理员', '管理APS系统配置、参数、规则', 1),
        ('aps.planner', '计划员', '发起排程、查看计划、调整任务', 1),
        ('aps.supervisor.workshop', '车间主管', '审批冻结区任务变更、查看车间计划', 1),
        ('aps.coordinator.material', '物料协同人员', '查看物料需求、库存信息', 1),
        ('aps.viewer.management', '管理层查看', '查看所有计划和报表，无修改权限', 1),
        ('aps.service.api', '系统接口账号', '用于系统间接口调用', 1);
    
    PRINT 'Roles initialized successfully.';
END
GO

-- =============================================
-- 初始化数据：预置权限
-- =============================================
IF NOT EXISTS (SELECT * FROM [Permission] WHERE PermissionCode = 'aps.plan.view')
BEGIN
    INSERT INTO [Permission] (PermissionCode, PermissionName, Module, ActionType, Description)
    VALUES 
        -- 计划模块
        ('aps.plan.view', '查看计划', 'Plan', 'View', '查看排程计划和版本'),
        ('aps.plan.run', '发起排程', 'Plan', 'Execute', '发起新的排程计算'),
        ('aps.plan.publish', '发布计划', 'Plan', 'Execute', '发布计划版本到生产系统'),
        
        -- 任务模块
        ('aps.task.view', '查看任务', 'Task', 'View', '查看生产任务详情'),
        ('aps.task.adjust', '调整任务', 'Task', 'Edit', '调整任务时间和资源'),
        ('aps.task.freeze.override', '修改冻结区任务', 'Task', 'Edit', '修改冻结区内的任务（需审批）'),
        ('aps.task.started.override', '修改已开工任务', 'Task', 'Edit', '修改已开工的任务（需审批）'),
        
        -- CTP模块
        ('aps.ctp.view', '查看插单评估', 'CTP', 'View', '查看插单评估结果'),
        ('aps.ctp.evaluate', '发起插单评估', 'CTP', 'Execute', '发起插单可行性评估'),
        ('aps.ctp.commit', '提交插单方案', 'CTP', 'Execute', '提交插单方案到计划'),
        
        -- 配置模块
        ('aps.config.supply_context.view', '查看供给上下文', 'Config', 'View', '查看MaterialSupplyContext配置'),
        ('aps.config.supply_context.edit', '修改供给上下文', 'Config', 'Edit', '修改MaterialSupplyContext配置'),
        ('aps.config.inventory_rule.view', '查看库存规则', 'Config', 'View', '查看库存筛选规则'),
        ('aps.config.inventory_rule.edit', '修改库存规则', 'Config', 'Edit', '修改库存筛选规则'),
        ('aps.config.resource.view', '查看资源配置', 'Config', 'View', '查看资源和产能配置'),
        ('aps.config.resource.edit', '修改资源配置', 'Config', 'Edit', '修改资源和产能配置'),
        
        -- 审批模块
        ('aps.approval.view', '查看审批', 'Approval', 'View', '查看审批流和审批记录'),
        ('aps.approval.approve', '审批', 'Approval', 'Execute', '审批或拒绝审批请求'),
        
        -- 审计模块
        ('aps.audit.view', '查看审计日志', 'Audit', 'View', '查看系统审计日志'),
        
        -- 用户管理模块
        ('aps.user.view', '查看用户', 'User', 'View', '查看用户列表和详情'),
        ('aps.user.manage', '管理用户', 'User', 'Edit', '创建、修改、禁用用户'),
        ('aps.role.view', '查看角色', 'Role', 'View', '查看角色列表和权限'),
        ('aps.role.manage', '管理角色', 'Role', 'Edit', '创建、修改角色和分配权限');
    
    PRINT 'Permissions initialized successfully.';
END
GO

-- =============================================
-- 初始化数据：默认管理员用户
-- =============================================
IF NOT EXISTS (SELECT * FROM [User] WHERE LoginName = 'admin')
BEGIN
    -- 密码: Admin@123 (实际使用时应该用BCrypt等算法加密)
    INSERT INTO [User] (LoginName, DisplayName, PasswordHash, Email, IsEnabled)
    VALUES ('admin', '系统管理员', 'TEMP_PASSWORD_HASH', 'admin@aps.local', 1);
    
    DECLARE @AdminUserId INT = SCOPE_IDENTITY();
    DECLARE @SystemAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode = 'aps.admin.system');
    
    INSERT INTO UserRole (UserId, RoleId, AssignedBy)
    VALUES (@AdminUserId, @SystemAdminRoleId, @AdminUserId);
    
    PRINT 'Default admin user created successfully.';
    PRINT 'WARNING: Please change the default admin password immediately!';
END
GO

-- =============================================
-- 初始化数据：全局数据范围
-- =============================================
IF NOT EXISTS (SELECT * FROM DataScopePolicy WHERE ScopeType = 'Global')
BEGIN
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description)
    VALUES ('Global', '*', '全局数据范围，可访问所有数据');
    
    DECLARE @GlobalScopeId INT = SCOPE_IDENTITY();
    DECLARE @SystemAdminRoleId INT = (SELECT Id FROM [Role] WHERE RoleCode = 'aps.admin.system');
    
    INSERT INTO RoleDataScope (RoleId, ScopePolicyId)
    VALUES (@SystemAdminRoleId, @GlobalScopeId);
    
    PRINT 'Global data scope initialized successfully.';
END
GO

PRINT '========================================';
PRINT 'APS_Auth database setup completed!';
PRINT '========================================';
PRINT 'Next steps:';
PRINT '1. Update admin user password';
PRINT '2. Create factory/product family/resource group data scopes';
PRINT '3. Assign roles and permissions to users';
PRINT '4. Configure approval rules';
PRINT '========================================';
GO
