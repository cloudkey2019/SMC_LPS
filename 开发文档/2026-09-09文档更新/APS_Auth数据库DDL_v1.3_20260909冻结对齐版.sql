-- ============================================================================
-- APS_Auth 数据库 DDL 脚本
-- 版本: v1.3（20260909 冻结对齐版）
-- 日期: 2026-09-09
-- Owner: 3号位（Auth / RBAC / Scope / Run 治理）
-- 依据: 0号位《Auth库DDL缺陷与真源裁决》R1=A′、R2=A
-- 用途: APS V1 身份认证、RBAC、业务范围权限、审计
-- v1.3 变更（0号位《Auth DDL 质疑单 v0.1》裁决收口，2026-09-09）:
--   D-2：DataScopePolicy 补 IsEnabled / UpdatedAt 启停生命周期，正式「删除」收敛为「停用」。
--
-- 本版与 v1.1 / 当前代码的关系（0号位 R1=A′ 收口口径）:
--   1) 字段命名以 v1.1 冻结业务为骨架（LoginName/DisplayName/IsEnabled/ActionType），
--      代码中的 UserCode/UserName/Status/Category 反向对齐回冻结命名。
--   2) 保留「已裁定需保护的现有 Auth 能力」字段：RefreshToken/RefreshTokenExpiry/
--      FailedLoginAttempts/LockoutEnd/LastLoginIP（User）、IsActive/UpdatedAt（Role/Permission）。
--   3) User.FactoryId / User.DepartmentId 不得成为第二套业务范围真值 → 本版不收录，
--      业务范围唯一由 DataScopePolicy 体系承载（P2-01）。
--   4) PermissionCode / RoleCode 统一 `aps.` 前缀（CHECK 约束强制）。
--   5) 治理审计（原第 14 张 GovernanceAuditLog）收敛回现有表 AuditLog，本版仅 13 张表，
--      不新增 GovernanceAuditLog（P0-04）。
--   6) 审批四表保留兼容，不作为当前 V1 默认强制主链。
--   7) R2=A：v1.2 先冻结，开发/测试期直接重建 APS_Auth，不维护 ALTER 链。
--   8) 不通过本脚本修改 APS 排程核心库（PlanVersion / ScheduleRun / Task 等）。
-- ============================================================================

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

-- ============================================================================
-- 1. User（用户表）
-- v1.2：LoginName/DisplayName/IsEnabled 冻结骨架 + Auth 能力字段；移除 FactoryId/DepartmentId
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'User')
BEGIN
    CREATE TABLE [User] (
        Id INT PRIMARY KEY IDENTITY(1,1),
        LoginName NVARCHAR(50) NOT NULL,                 -- 登录标识（代码 UserCode 对齐为 LoginName）
        DisplayName NVARCHAR(100) NOT NULL,              -- 显示名（代码 UserName 对齐为 DisplayName）
        PasswordHash NVARCHAR(500) NOT NULL,
        Email NVARCHAR(100),
        PhoneNumber NVARCHAR(20),
        IsEnabled BIT NOT NULL DEFAULT 1,                -- 启用标志（代码 Status 对齐为 IsEnabled）
        IsDeleted BIT NOT NULL DEFAULT 0,                -- 软删除标志（保留代码 SoftDelete 能力）
        LastLoginAt DATETIME2,                           -- 最近登录时间（代码 LastLoginTime 对齐）
        LastLoginIP NVARCHAR(50),                        -- 现有 Auth 能力（保留）
        FailedLoginAttempts INT NOT NULL DEFAULT 0,      -- 登录失败次数（锁定能力，保留）
        LockoutEnd DATETIME2,                            -- 锁定截止（锁定能力，保留）
        RefreshToken NVARCHAR(500),                      -- 刷新令牌（Refresh 能力，保留）
        RefreshTokenExpiry DATETIME2,                    -- 刷新令牌过期（Refresh 能力，保留）
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_User_LoginName UNIQUE (LoginName),
        CONSTRAINT CK_User_LoginName CHECK (LEN(LoginName) >= 3)
    );

    CREATE INDEX IX_User_LoginName ON [User](LoginName);
    CREATE INDEX IX_User_IsEnabled ON [User](IsEnabled);
END
GO

-- ============================================================================
-- 2. Role（角色表）
-- v1.2：+ IsActive（角色启停能力）+ UpdatedAt；RoleCode 统一 aps. 前缀
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Role')
BEGIN
    CREATE TABLE [Role] (
        Id INT PRIMARY KEY IDENTITY(1,1),
        RoleCode NVARCHAR(50) NOT NULL,
        RoleName NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500),
        IsSystemRole BIT NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1,                 -- 现有 Auth 能力（保留）
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_Role_RoleCode UNIQUE (RoleCode),
        CONSTRAINT CK_Role_RoleCode CHECK (RoleCode LIKE 'aps.%')
    );

    CREATE INDEX IX_Role_RoleCode ON [Role](RoleCode);
END
GO

-- ============================================================================
-- 3. Permission（权限表）
-- v1.2：ActionType（代码 Category 对齐为 ActionType）+ IsActive + UpdatedAt；PermissionCode 统一 aps. 前缀
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Permission')
BEGIN
    CREATE TABLE [Permission] (
        Id INT PRIMARY KEY IDENTITY(1,1),
        PermissionCode NVARCHAR(100) NOT NULL,
        PermissionName NVARCHAR(100) NOT NULL,
        Module NVARCHAR(50) NOT NULL,                    -- 模块（v1.1 已有）
        ActionType NVARCHAR(50) NOT NULL,                -- 动作类型（代码 Category 对齐为 ActionType）
        Description NVARCHAR(500),
        IsActive BIT NOT NULL DEFAULT 1,                 -- 现有 Auth 能力（保留）
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_Permission_Code UNIQUE (PermissionCode),
        CONSTRAINT CK_Permission_Code CHECK (PermissionCode LIKE 'aps.%')
    );

    CREATE INDEX IX_Permission_Module ON [Permission](Module);
    CREATE INDEX IX_Permission_Code ON [Permission](PermissionCode);
END
GO

-- ============================================================================
-- 4. UserRole
-- ============================================================================
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

-- ============================================================================
-- 5. RolePermission
-- ============================================================================
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

-- ============================================================================
-- 6. DataScopePolicy
-- Business Scope: Factory / ProductFamily / Department / Domain / ResourceOrgGroup(兼容) / Global
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DataScopePolicy')
BEGIN
    CREATE TABLE DataScopePolicy (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ScopeType NVARCHAR(50) NOT NULL,
        ScopeValue NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500),
        IsEnabled BIT NOT NULL DEFAULT 1,                -- 启停（D-2 裁决：停用即不再参与分配/授权）
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT CK_DataScope_Type CHECK (
            ScopeType IN ('Factory','ProductFamily','Department','Domain','ResourceOrgGroup','Global')
        ),
        CONSTRAINT UQ_DataScope_Type_Value UNIQUE (ScopeType, ScopeValue)
    );

    CREATE INDEX IX_DataScope_Type ON DataScopePolicy(ScopeType);
    CREATE INDEX IX_DataScope_Value ON DataScopePolicy(ScopeValue);
END
GO

-- ============================================================================
-- 7. UserDataScope
-- ============================================================================
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

-- ============================================================================
-- 8. RoleDataScope
-- ============================================================================
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

-- ============================================================================
-- 9. AuditLog（统一审计表，v1.2 收敛治理审计）
-- v1.2：将原第 14 张 GovernanceAuditLog 的治理/候选/运行审计收敛回本表，
--       统一字段名一次冻结；同时保留 APS 业务审计（PlanVersionId/BatchNo/ApprovalId）兼容列。
-- 字段映射（收敛口径）:
--   ActionCode      ← 治理 OperationType / 代码 ActionType / RBAC 动作
--   EntityType      ← 治理 EntityType（RuleSetVersion/ParameterSetVersion/StrategyProfileVersion/PlanVersion/ScheduleRun）
--   EntityId        ← 治理 EntityId（NVARCHAR 统一，长整型版本号字符串化）
--   VersionCode     ← 治理 VersionCode
--   OldValue        ← 治理 BeforeStatus
--   NewValue        ← 治理 AfterStatus
--   UserCode        ← 治理 OperatedBy / 代码 OperatedBy
--   OccurredAt      ← 治理 OperatedAt / 代码 CreatedAt
--   Remark          ← 治理 Remarks
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLog')
BEGIN
    CREATE TABLE AuditLog (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        UserId INT,                                      -- 操作人（可空：系统动作）
        UserCode NVARCHAR(50),                           -- 操作人登录标识（收敛 OperatedBy/PublishedBy/Actor）
        ActionCode NVARCHAR(100) NOT NULL,               -- 统一动作码（Publish/Disable/ConfirmCandidate/ActivateCandidate/RecoverFailedRun/CreateCandidateRun/RBAC...）
        Module NVARCHAR(50),                             -- 模块
        EntityType NVARCHAR(50),                         -- 实体类型
        EntityId NVARCHAR(100),                          -- 实体 Id（字符串统一）
        VersionCode NVARCHAR(100),                       -- 治理版本号
        OldValue NVARCHAR(MAX),                          -- 变更前（治理 BeforeStatus）
        NewValue NVARCHAR(MAX),                          -- 变更后（治理 AfterStatus）
        Result NVARCHAR(20) NOT NULL DEFAULT 'Success',  -- 结果（Success/Failed/Denied）
        OccurredAt DATETIME2 NOT NULL DEFAULT GETDATE(), -- 发生时间
        ClientIp NVARCHAR(50),                           -- 客户端 IP（代码 IPAddress 对齐）
        UserAgent NVARCHAR(500),                         -- 客户端 UA
        Remark NVARCHAR(1000),                           -- 备注（治理 Remarks）
        PlanVersionId INT,                               -- APS 业务审计兼容列
        BatchNo NVARCHAR(50),                            -- APS 业务审计兼容列
        ApprovalId INT,                                  -- APS 业务审计兼容列
        RequestData NVARCHAR(MAX),                       -- 请求体（兼容）
        ResponseData NVARCHAR(MAX),                      -- 响应体（兼容）
        ErrorMessage NVARCHAR(MAX),                      -- 错误信息（兼容）
        FOREIGN KEY (UserId) REFERENCES [User](Id),
        CONSTRAINT CK_AuditLog_Result CHECK (Result IN ('Success','Failed','Denied'))
    );

    CREATE INDEX IX_AuditLog_UserId ON AuditLog(UserId);
    CREATE INDEX IX_AuditLog_ActionCode ON AuditLog(ActionCode);
    CREATE INDEX IX_AuditLog_EntityType_EntityId ON AuditLog(EntityType, EntityId);
    CREATE INDEX IX_AuditLog_OccurredAt ON AuditLog(OccurredAt);
    CREATE INDEX IX_AuditLog_PlanVersionId ON AuditLog(PlanVersionId);
    CREATE INDEX IX_AuditLog_BatchNo ON AuditLog(BatchNo);
    CREATE INDEX IX_AuditLog_Result ON AuditLog(Result);
END
GO

-- ============================================================================
-- 10~13. Approval 兼容结构（保留 != 当前 V1 强制启用）
-- ============================================================================
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

-- ============================================================================
-- 预置角色
-- ============================================================================
IF NOT EXISTS (SELECT * FROM [Role] WHERE RoleCode = 'aps.admin.system')
BEGIN
    INSERT INTO [Role] (RoleCode, RoleName, Description, IsSystemRole, IsActive)
    VALUES
        ('aps.admin.system', '系统管理员', '用户、角色、权限、认证及系统安全管理', 1, 1),
        ('aps.admin.aps', 'APS管理员', 'APS规则、参数、策略等管理角色模板', 1, 1),
        ('aps.planner', '计划员', '计划查询、试算、重排等角色模板', 1, 1),
        ('aps.supervisor.workshop', '车间主管', '车间范围查看与受控业务操作角色模板', 1, 1),
        ('aps.coordinator.material', '物料协同人员', '物料、供给与采购协同角色模板', 1, 1),
        ('aps.viewer.management', '管理层查看', '管理查看角色模板', 1, 1),
        ('aps.service.api', '系统接口账号', '系统间受控接口调用账号', 1, 1);
END
GO

-- ============================================================================
-- V1 正式权限码种子（统一 aps. 前缀）
-- （与 v1.1 同 36 码；代码 PermissionCodes.cs 的 13 个无前缀码在「代码对齐」步骤重新映射到这里）
-- ============================================================================

-- Plan
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.view')
INSERT INTO Permission VALUES ('aps.plan.view','查看计划','Plan','View','查看授权业务范围内计划',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.run')
INSERT INTO Permission VALUES ('aps.plan.run','发起排程','Plan','Execute','按授权范围发起排程/运行',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.compare')
INSERT INTO Permission VALUES ('aps.plan.compare','计划对比','Plan','View','查看版本/Candidate对比',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.plan.export')
INSERT INTO Permission VALUES ('aps.plan.export','导出计划','Plan','Execute','导出授权范围内计划',1,GETDATE(),GETDATE());

-- CTP / Insert / Reschedule
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.ctp.view')
INSERT INTO Permission VALUES ('aps.ctp.view','查看承诺交期评估','CTP','View','查看CTP结果',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.ctp.evaluate')
INSERT INTO Permission VALUES ('aps.ctp.evaluate','发起承诺交期评估','CTP','Execute','发起CTP试算',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.insert.impact.evaluate')
INSERT INTO Permission VALUES ('aps.insert.impact.evaluate','发起插单影响分析','Insert','Execute','发起INSERT_IMPACT_ANALYSIS',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.reschedule.local')
INSERT INTO Permission VALUES ('aps.reschedule.local','发起局部重排','Reschedule','Execute','发起LOCAL_RESCHEDULE',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.reschedule.manual')
INSERT INTO Permission VALUES ('aps.reschedule.manual','发起人工重排','Reschedule','Execute','发起MANUAL_RESCHEDULE',1,GETDATE(),GETDATE());

-- Candidate
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.candidate.view')
INSERT INTO Permission VALUES ('aps.candidate.view','查看候选计划','Candidate','View','查看Candidate',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.candidate.confirm')
INSERT INTO Permission VALUES ('aps.candidate.confirm','确认候选计划','Candidate','Execute','人工确认Candidate',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.candidate.activate')
INSERT INTO Permission VALUES ('aps.candidate.activate','激活候选计划','Candidate','Execute','在业务允许时激活Candidate',1,GETDATE(),GETDATE());

-- Rule / Parameter / Strategy
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.rule.view')
INSERT INTO Permission VALUES ('aps.rule.view','查看规则','Rule','View','查看规则集/版本',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.rule.edit')
INSERT INTO Permission VALUES ('aps.rule.edit','编辑规则','Rule','Edit','编辑规则草稿',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.rule.publish')
INSERT INTO Permission VALUES ('aps.rule.publish','发布规则','Rule','Execute','发布规则版本',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.parameter.view')
INSERT INTO Permission VALUES ('aps.parameter.view','查看参数','Parameter','View','查看参数集/版本',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.parameter.edit')
INSERT INTO Permission VALUES ('aps.parameter.edit','编辑参数','Parameter','Edit','编辑参数草稿',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.strategy.view')
INSERT INTO Permission VALUES ('aps.strategy.view','查看策略','Strategy','View','查看策略包/版本',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.strategy.edit')
INSERT INTO Permission VALUES ('aps.strategy.edit','编辑策略','Strategy','Edit','编辑策略草稿',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.strategy.publish')
INSERT INTO Permission VALUES ('aps.strategy.publish','发布策略','Strategy','Execute','发布策略版本',1,GETDATE(),GETDATE());

-- Manual ETA
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.manual_eta.view')
INSERT INTO Permission VALUES ('aps.manual_eta.view','查看人工到货时间','ManualEta','View','查看Manual ETA',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.manual_eta.edit')
INSERT INTO Permission VALUES ('aps.manual_eta.edit','维护人工到货时间','ManualEta','Edit','新增/修改Manual ETA',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.manual_eta.cancel')
INSERT INTO Permission VALUES ('aps.manual_eta.cancel','取消人工到货时间','ManualEta','Execute','取消Manual ETA',1,GETDATE(),GETDATE());

-- Demand Protection
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.demand_protection.view')
INSERT INTO Permission VALUES ('aps.demand_protection.view','查看需求保护','DemandProtection','View','查看Demand Protection状态',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.demand_protection.release')
INSERT INTO Permission VALUES ('aps.demand_protection.release','释放需求保护','DemandProtection','Execute','受控释放Demand Protection',1,GETDATE(),GETDATE());

-- MES
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.mes.view')
INSERT INTO Permission VALUES ('aps.mes.view','查看MES状态','MES','View','查看MES相关状态/资格',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.mes.dispatch')
INSERT INTO Permission VALUES ('aps.mes.dispatch','发起MES受控操作','MES','Execute','按冻结资格执行MES受控操作',1,GETDATE(),GETDATE());

-- Auth / Scope / Audit
IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.user.view')
INSERT INTO Permission VALUES ('aps.auth.user.view','查看用户','Auth','View','查看用户',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.user.edit')
INSERT INTO Permission VALUES ('aps.auth.user.edit','维护用户','Auth','Edit','创建/修改/禁用用户',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.role.view')
INSERT INTO Permission VALUES ('aps.auth.role.view','查看角色','Auth','View','查看角色',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.role.edit')
INSERT INTO Permission VALUES ('aps.auth.role.edit','维护角色','Auth','Edit','创建/修改角色',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.permission.assign')
INSERT INTO Permission VALUES ('aps.auth.permission.assign','分配功能权限','Auth','Edit','角色权限分配',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.auth.scope.assign')
INSERT INTO Permission VALUES ('aps.auth.scope.assign','分配业务范围','Auth','Edit','用户/角色业务Scope分配',1,GETDATE(),GETDATE());

IF NOT EXISTS (SELECT 1 FROM Permission WHERE PermissionCode='aps.audit.view')
INSERT INTO Permission VALUES ('aps.audit.view','查看审计日志','Audit','View','查看权限及关键业务审计',1,GETDATE(),GETDATE());
GO

-- ============================================================================
-- 旧 v1.0 权限码说明（兼容，不成为当前 V1 新主链依赖）
-- aps.task.freeze.override / aps.task.started.override / aps.ctp.commit
-- aps.approval.view / aps.approval.approve
-- ============================================================================

-- ============================================================================
-- 默认管理员（正式环境不得使用 TEMP_PASSWORD_HASH）
-- ============================================================================
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

-- ============================================================================
-- Global Scope
-- ============================================================================
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
PRINT 'APS_Auth v1.2 freeze-aligned setup completed.';
PRINT 'Owner: Position 3';
PRINT 'Business Scope: Factory / ProductFamily / Department / Domain';
PRINT 'Approval tables retained for compatibility, not forced into V1 main flow.';
PRINT 'AuditLog unified (GovernanceAuditLog merged, NOT a 14th table).';
PRINT '========================================';
GO