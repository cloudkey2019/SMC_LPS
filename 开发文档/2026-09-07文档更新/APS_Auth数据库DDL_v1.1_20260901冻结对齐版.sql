-- =============================================
-- APS_Auth 数据库 DDL 脚本
-- 版本: v1.1（20260901冻结对齐版）
-- 日期: 2026-09-01
-- Owner: 3号位
-- 用途: APS V1 身份认证、RBAC、业务范围权限、审计
-- 说明:
-- 1) 基于 v1.0 增量对齐，不推倒现有13张表。
-- 2) 审批四表保留兼容，但不作为当前 V1 默认强制主链。
-- 3) Business Scope 至少支持 Factory / ProductFamily / Department / Domain。
-- 4) ResourceOrgGroup 作为兼容维度保留。
-- 5) 不通过本脚本修改 APS 排程核心库（PlanVersion / ScheduleRun / Task 等）。
-- =============================================

USE master;
GO

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
END
GO

-- =============================================
-- 4. UserRole
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
END
GO

-- =============================================
-- 5. RolePermission
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
END
GO

-- =============================================
-- 6. DataScopePolicy
-- v1.1: 增加 Department / Domain；保留 ResourceOrgGroup 兼容
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DataScopePolicy')
BEGIN
    CREATE TABLE DataScopePolicy (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ScopeType NVARCHAR(50) NOT NULL,
        ScopeValue NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT CK_DataScope_Type CHECK (
            ScopeType IN ('Factory','ProductFamily','Department','Domain','ResourceOrgGroup','Global')
        ),
        CONSTRAINT UQ_DataScope_Type_Value UNIQUE (ScopeType, ScopeValue)
    );

    CREATE INDEX IX_DataScope_Type ON DataScopePolicy(ScopeType);
    CREATE INDEX IX_DataScope_Value ON DataScopePolicy(ScopeValue);
END
ELSE
BEGIN
    -- 对已有 v1.0 数据库做增量升级：仅扩充 ScopeType 值域
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_DataScope_Type'
          AND parent_object_id = OBJECT_ID('DataScopePolicy')
    )
    BEGIN
        ALTER TABLE DataScopePolicy DROP CONSTRAINT CK_DataScope_Type;
    END;

    ALTER TABLE DataScopePolicy WITH CHECK
    ADD CONSTRAINT CK_DataScope_Type CHECK (
        ScopeType IN ('Factory','ProductFamily','Department','Domain','ResourceOrgGroup','Global')
    );
END
GO

-- =============================================
-- 7. UserDataScope
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
END
GO

-- =============================================
-- 8. RoleDataScope
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
END
GO

-- =============================================
-- 9. AuditLog
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
        CONSTRAINT CK_AuditLog_Result CHECK (Result IN ('Success','Failed','Denied'))
    );

    CREATE INDEX IX_AuditLog_UserId ON AuditLog(UserId);
    CREATE INDEX IX_AuditLog_ActionCode ON AuditLog(ActionCode);
    CREATE INDEX IX_AuditLog_OccurredAt ON AuditLog(OccurredAt);
    CREATE INDEX IX_AuditLog_PlanVersionId ON AuditLog(PlanVersionId);
    CREATE INDEX IX_AuditLog_BatchNo ON AuditLog(BatchNo);
    CREATE INDEX IX_AuditLog_Result ON AuditLog(Result);
END
GO

-- =============================================
-- 10~13. Approval 兼容结构
-- 注意：保留 != 当前 V1 强制启用
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
        CONSTRAINT CK_ApprovalFlow_Status CHECK (Status IN ('Pending','Approved','Rejected','Cancelled'))
    );

    CREATE INDEX IX_ApprovalFlow_Status ON ApprovalFlow(Status);
    CREATE INDEX IX_ApprovalFlow_ApplicantUserId ON ApprovalFlow(ApplicantUserId);
    CREATE INDEX IX_ApprovalFlow_Type ON ApprovalFlow(ApprovalType);
    CREATE INDEX IX_ApprovalFlow_CreatedAt ON ApprovalFlow(CreatedAt);
END
GO

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
        CONSTRAINT CK_ApprovalNode_Status CHECK (Status IN ('Pending','Approved','Rejected','Skipped')),
        CONSTRAINT UQ_ApprovalNode_Flow_Seq UNIQUE (ApprovalFlowId, NodeSeq)
    );

    CREATE INDEX IX_ApprovalNode_FlowId ON ApprovalNode(ApprovalFlowId);
    CREATE INDEX IX_ApprovalNode_Status ON ApprovalNode(Status);
    CREATE INDEX IX_ApprovalNode_RoleId ON ApprovalNode(ApproverRoleId);
    CREATE INDEX IX_ApprovalNode_UserId ON ApprovalNode(ApproverUserId);
END
GO

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
        CONSTRAINT CK_ApprovalRecord_Decision CHECK (Decision IN ('Approved','Rejected'))
    );

    CREATE INDEX IX_ApprovalRecord_NodeId ON ApprovalRecord(ApprovalNodeId);
    CREATE INDEX IX_ApprovalRecord_ApproverUserId ON ApprovalRecord(ApproverUserId);
    CREATE INDEX IX_ApprovalRecord_ApprovedAt ON ApprovalRecord(ApprovedAt);
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApprovalRule')
BEGIN
    CREATE TABLE ApprovalRule (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ApprovalType NVARCHAR(50) NOT NULL,
        FactoryId INT,
        ProductFamilyId INT,
        ResourceOrgGroupId INT,
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
END
GO

-- =============================================
-- 预置角色（兼容 v1.0）
-- =============================================
IF NOT EXISTS (SELECT * FROM [Role] WHERE RoleCode = 'aps.admin.system')
BEGIN
    INSERT INTO [Role] (RoleCode, RoleName, Description, IsSystemRole)
    VALUES
        ('aps.admin.system', '系统管理员', '用户、角色、权限、认证及系统安全管理', 1),
        ('aps.admin.aps', 'APS管理员', 'APS规则、参数、策略等管理角色模板', 1),
        ('aps.planner', '计划员', '计划查询、试算、重排等角色模板', 1),
        ('aps.supervisor.workshop', '车间主管', '车间范围查看与受控业务操作角色模板', 1),
        ('aps.coordinator.material', '物料协同人员', '物料、供给与采购协同角色模板', 1),
        ('aps.viewer.management', '管理层查看', '管理查看角色模板', 1),
        ('aps.service.api', '系统接口账号', '系统间受控接口调用账号', 1);
END
GO

-- =============================================
-- 当前 V1 正式权限码种子
-- 使用逐条 IF NOT EXISTS，兼容已有 v1.0 数据库
-- =============================================

-- Plan
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.view')
INSERT INTO Permission VALUES ('aps.plan.view','查看计划','Plan','View','查看授权业务范围内计划',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.run')
INSERT INTO Permission VALUES ('aps.plan.run','发起排程','Plan','Execute','按授权范围发起排程/运行',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.compare')
INSERT INTO Permission VALUES ('aps.plan.compare','计划对比','Plan','View','查看版本/Candidate对比',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.export')
INSERT INTO Permission VALUES ('aps.plan.export','导出计划','Plan','Execute','导出授权范围内计划',GETDATE());

-- CTP / Insert / Reschedule
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.ctp.view')
INSERT INTO Permission VALUES ('aps.ctp.view','查看承诺交期评估','CTP','View','查看CTP结果',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.ctp.evaluate')
INSERT INTO Permission VALUES ('aps.ctp.evaluate','发起承诺交期评估','CTP','Execute','发起CTP试算',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.insert.impact.evaluate')
INSERT INTO Permission VALUES ('aps.insert.impact.evaluate','发起插单影响分析','Insert','Execute','发起INSERT_IMPACT_ANALYSIS',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.reschedule.local')
INSERT INTO Permission VALUES ('aps.reschedule.local','发起局部重排','Reschedule','Execute','发起LOCAL_RESCHEDULE',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.reschedule.manual')
INSERT INTO Permission VALUES ('aps.reschedule.manual','发起人工重排','Reschedule','Execute','发起MANUAL_RESCHEDULE',GETDATE());

-- Candidate
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.candidate.view')
INSERT INTO Permission VALUES ('aps.candidate.view','查看候选计划','Candidate','View','查看Candidate',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.candidate.confirm')
INSERT INTO Permission VALUES ('aps.candidate.confirm','确认候选计划','Candidate','Execute','人工确认Candidate',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.candidate.activate')
INSERT INTO Permission VALUES ('aps.candidate.activate','激活候选计划','Candidate','Execute','在业务允许时激活Candidate',GETDATE());

-- Rule / Parameter / Strategy
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.rule.view')
INSERT INTO Permission VALUES ('aps.rule.view','查看规则','Rule','View','查看规则集/版本',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.rule.edit')
INSERT INTO Permission VALUES ('aps.rule.edit','编辑规则','Rule','Edit','编辑规则草稿',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.rule.publish')
INSERT INTO Permission VALUES ('aps.rule.publish','发布规则','Rule','Execute','发布规则版本',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.parameter.view')
INSERT INTO Permission VALUES ('aps.parameter.view','查看参数','Parameter','View','查看参数集/版本',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.parameter.edit')
INSERT INTO Permission VALUES ('aps.parameter.edit','编辑参数','Parameter','Edit','编辑参数草稿',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.strategy.view')
INSERT INTO Permission VALUES ('aps.strategy.view','查看策略','Strategy','View','查看策略包/版本',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.strategy.edit')
INSERT INTO Permission VALUES ('aps.strategy.edit','编辑策略','Strategy','Edit','编辑策略草稿',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.strategy.publish')
INSERT INTO Permission VALUES ('aps.strategy.publish','发布策略','Strategy','Execute','发布策略版本',GETDATE());

-- Manual ETA
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.manual_eta.view')
INSERT INTO Permission VALUES ('aps.manual_eta.view','查看人工到货时间','ManualEta','View','查看Manual ETA',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.manual_eta.edit')
INSERT INTO Permission VALUES ('aps.manual_eta.edit','维护人工到货时间','ManualEta','Edit','新增/修改Manual ETA',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.manual_eta.cancel')
INSERT INTO Permission VALUES ('aps.manual_eta.cancel','取消人工到货时间','ManualEta','Execute','取消Manual ETA',GETDATE());

-- Demand Protection
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.demand_protection.view')
INSERT INTO Permission VALUES ('aps.demand_protection.view','查看需求保护','DemandProtection','View','查看Demand Protection状态',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.demand_protection.release')
INSERT INTO Permission VALUES ('aps.demand_protection.release','释放需求保护','DemandProtection','Execute','受控释放Demand Protection',GETDATE());

-- MES
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.mes.view')
INSERT INTO Permission VALUES ('aps.mes.view','查看MES状态','MES','View','查看MES相关状态/资格',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.mes.dispatch')
INSERT INTO Permission VALUES ('aps.mes.dispatch','发起MES受控操作','MES','Execute','按冻结资格执行MES受控操作',GETDATE());

-- Auth / Audit
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.user.view')
INSERT INTO Permission VALUES ('aps.auth.user.view','查看用户','Auth','View','查看用户',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.user.edit')
INSERT INTO Permission VALUES ('aps.auth.user.edit','维护用户','Auth','Edit','创建/修改/禁用用户',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.role.view')
INSERT INTO Permission VALUES ('aps.auth.role.view','查看角色','Auth','View','查看角色',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.role.edit')
INSERT INTO Permission VALUES ('aps.auth.role.edit','维护角色','Auth','Edit','创建/修改角色',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.permission.assign')
INSERT INTO Permission VALUES ('aps.auth.permission.assign','分配功能权限','Auth','Edit','角色权限分配',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.scope.assign')
INSERT INTO Permission VALUES ('aps.auth.scope.assign','分配业务范围','Auth','Edit','用户/角色业务Scope分配',GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.audit.view')
INSERT INTO Permission VALUES ('aps.audit.view','查看审计日志','Audit','View','查看权限及关键业务审计',GETDATE());
GO

-- =============================================
-- 旧 v1.0 权限码说明
-- 下列权限如已存在，不删除；仅作兼容，不应成为当前V1新主链依赖：
-- aps.task.freeze.override
-- aps.task.started.override
-- aps.ctp.commit
-- aps.approval.view
-- aps.approval.approve
-- =============================================

-- =============================================
-- 默认管理员
-- 正式环境不得使用 TEMP_PASSWORD_HASH
-- =============================================
IF NOT EXISTS (SELECT * FROM [User] WHERE LoginName = 'admin')
BEGIN
    INSERT INTO [User] (LoginName, DisplayName, PasswordHash, Email, IsEnabled)
    VALUES ('admin', '系统管理员', 'TEMP_PASSWORD_HASH', 'admin@aps.local', 1);

    DECLARE @AdminUserId INT = SCOPE_IDENTITY();
    DECLARE @SystemAdminRoleId INT =
        (SELECT Id FROM [Role] WHERE RoleCode = 'aps.admin.system');

    INSERT INTO UserRole (UserId, RoleId, AssignedBy)
    VALUES (@AdminUserId, @SystemAdminRoleId, @AdminUserId);

    PRINT 'WARNING: TEMP_PASSWORD_HASH is development placeholder only.';
END
GO

-- =============================================
-- Global Scope
-- =============================================
IF NOT EXISTS (
    SELECT 1 FROM DataScopePolicy
    WHERE ScopeType='Global' AND ScopeValue='*'
)
BEGIN
    INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description)
    VALUES ('Global','*','全局业务范围');
END
GO

DECLARE @GlobalScopeId INT =
    (SELECT Id FROM DataScopePolicy WHERE ScopeType='Global' AND ScopeValue='*');
DECLARE @SystemAdminRoleId INT =
    (SELECT Id FROM [Role] WHERE RoleCode='aps.admin.system');

IF @GlobalScopeId IS NOT NULL
AND @SystemAdminRoleId IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM RoleDataScope
    WHERE RoleId=@SystemAdminRoleId AND ScopePolicyId=@GlobalScopeId
)
BEGIN
    INSERT INTO RoleDataScope(RoleId, ScopePolicyId)
    VALUES (@SystemAdminRoleId, @GlobalScopeId);
END
GO

PRINT '========================================';
PRINT 'APS_Auth v1.1 setup / upgrade completed.';
PRINT 'Owner: Position 3';
PRINT 'Business Scope: Factory / ProductFamily / Department / Domain';
PRINT 'Approval tables retained for compatibility, not forced into V1 main flow.';
PRINT '========================================';
GO
