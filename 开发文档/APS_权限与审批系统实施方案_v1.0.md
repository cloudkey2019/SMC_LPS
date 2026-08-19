# APS 权限与审批系统实施方案 v1.0

**文档定位**：基于用户需求确认的 APS 权限与审批系统实施方案  
**适用范围**：APS V1.0 单体解决方案  
**创建日期**：2026-03-24  
**基于文档**：`APS_权限与审批设计方案_v1.0.md`（另一个AI的设计）

---

## 📋 需求确认摘要

基于与用户的需求澄清，确认以下关键决策：

### 业务范围
- ✅ V1.0 先做核心高风险动作的权限控制
- ✅ 需要多级审批（不需要会签/或签、超时升级、审批撤回）
- ✅ 数据范围：工厂级 + 产品族级 + 资源组级
- ✅ 用户自己维护（不集成企业统一认证）
- ✅ 审批流自己实现（V1.0 不集成 OA）
- ✅ 审计日志基本功能（数据量不大）

### 架构决策
- ✅ 权限表单独创建 `APS_Auth` 数据库
- ✅ 每次排程记录发起人（PlanVersion.CreatedByUserId）
- ✅ 版本发布不需要审批
- ✅ ODS 批次重跑不需要审批
- ✅ MaterialSupplyContext 修改只需权限控制和日志，不需要审批
- ✅ 库存筛选规则暂时只有 APS 管理员可修改，不需要审批

### 技术选型
- ✅ 后端：ASP.NET Core Identity + 自定义扩展
- ✅ 前端：Vue 3 + JWT Token（2小时过期，无 Refresh Token）
- ✅ 密码策略：简单策略（V1.0）

---

## 🎯 实施阶段规划

### 阶段一：基础权限与鉴权（2-3周）

**目标**：建立基础的用户、角色、权限体系，实现接口层鉴权和数据范围控制

**交付物**：
1. `APS_Auth` 数据库及表结构
2. 用户、角色、权限管理功能
3. 工厂/产品族/资源组数据范围
4. 接口层声明式鉴权（`[ApsAuthorize]` 特性）
5. 审计日志（关键动作）
6. 前端登录页面和路由守卫

**核心表**：
- User（用户表）
- Role（角色表）
- Permission（权限表）
- UserRole（用户角色关联表）
- RolePermission（角色权限关联表）
- DataScopePolicy（数据范围策略表）
- UserDataScope（用户数据范围表）
- RoleDataScope（角色数据范围表）
- AuditLog（审计日志表）

**核心功能**：
- 用户登录/登出（JWT Token）
- 接口层权限校验
- 数据范围校验
- 审计日志记录

---

### 阶段二：审批流（2周）

**目标**：实现多级审批流，支持冻结区任务变更和牺牲 SO 审批

**交付物**：
1. 审批流表结构
2. 审批流引擎
3. 冻结区任务变更审批
4. 牺牲 SO / 挤占他单能力审批
5. 前端审批管理页面

**核心表**：
- ApprovalFlow（审批流表）
- ApprovalNode（审批节点表）
- ApprovalRecord（审批记录表）
- ApprovalRule（审批规则表）

**核心功能**：
- 发起审批
- 多级审批流转
- 审批通过/拒绝
- 审批状态查询

---

### 阶段三：扩展审批场景（1-2周）

**目标**：扩展更多审批场景

**交付物**：
1. 已开工任务变更审批
2. 审批超时策略（可选）

---

## 🗄️ 数据库设计

### APS_Auth 数据库

**库名**：`APS_Auth`  
**用途**：存储权限、审批、审计相关数据

### 核心表设计

#### 1. User（用户表）

```sql
CREATE TABLE [User] (
    Id INT PRIMARY KEY IDENTITY(1,1),
    LoginName NVARCHAR(50) NOT NULL UNIQUE,
    DisplayName NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(500) NOT NULL,
    Email NVARCHAR(100),
    PhoneNumber NVARCHAR(20),
    IsEnabled BIT NOT NULL DEFAULT 1,
    LastLoginAt DATETIME2,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CK_User_LoginName CHECK (LEN(LoginName) >= 3)
);

CREATE INDEX IX_User_LoginName ON [User](LoginName);
CREATE INDEX IX_User_IsEnabled ON [User](IsEnabled);
```

#### 2. Role（角色表）

```sql
CREATE TABLE [Role] (
    Id INT PRIMARY KEY IDENTITY(1,1),
    RoleCode NVARCHAR(50) NOT NULL UNIQUE,
    RoleName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500),
    IsSystemRole BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CK_Role_RoleCode CHECK (RoleCode LIKE 'aps.%')
);

CREATE INDEX IX_Role_RoleCode ON [Role](RoleCode);
```

**预置角色**：
- `aps.admin.system`：系统管理员
- `aps.admin.aps`：APS 管理员
- `aps.planner`：计划员
- `aps.supervisor.workshop`：车间主管
- `aps.coordinator.material`：物料协同人员
- `aps.viewer.management`：管理层查看
- `aps.service.api`：系统接口账号

#### 3. Permission（权限表）

```sql
CREATE TABLE [Permission] (
    Id INT PRIMARY KEY IDENTITY(1,1),
    PermissionCode NVARCHAR(100) NOT NULL UNIQUE,
    PermissionName NVARCHAR(100) NOT NULL,
    Module NVARCHAR(50) NOT NULL,
    ActionType NVARCHAR(50) NOT NULL,
    Description NVARCHAR(500),
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CK_Permission_Code CHECK (PermissionCode LIKE 'aps.%')
);

CREATE INDEX IX_Permission_Module ON [Permission](Module);
```

**核心权限码**（阶段一）：
- `aps.plan.view`：查看计划
- `aps.plan.run`：发起排程
- `aps.task.view`：查看任务
- `aps.task.adjust`：调整任务
- `aps.task.freeze.override`：修改冻结区任务
- `aps.ctp.view`：查看插单评估
- `aps.ctp.evaluate`：发起插单评估
- `aps.ctp.commit`：提交插单方案
- `aps.config.supply_context.view`：查看供给上下文
- `aps.config.supply_context.edit`：修改供给上下文
- `aps.config.inventory_rule.view`：查看库存规则
- `aps.config.inventory_rule.edit`：修改库存规则
- `aps.audit.view`：查看审计日志

#### 4. UserRole（用户角色关联表）

```sql
CREATE TABLE UserRole (
    UserId INT NOT NULL,
    RoleId INT NOT NULL,
    AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    AssignedBy INT,
    PRIMARY KEY (UserId, RoleId),
    FOREIGN KEY (UserId) REFERENCES [User](Id) ON DELETE CASCADE,
    FOREIGN KEY (RoleId) REFERENCES [Role](Id) ON DELETE CASCADE
);
```

#### 5. RolePermission（角色权限关联表）

```sql
CREATE TABLE RolePermission (
    RoleId INT NOT NULL,
    PermissionId INT NOT NULL,
    AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    PRIMARY KEY (RoleId, PermissionId),
    FOREIGN KEY (RoleId) REFERENCES [Role](Id) ON DELETE CASCADE,
    FOREIGN KEY (PermissionId) REFERENCES [Permission](Id) ON DELETE CASCADE
);
```

#### 6. DataScopePolicy（数据范围策略表）

```sql
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
```

#### 7. UserDataScope（用户数据范围表）

```sql
CREATE TABLE UserDataScope (
    UserId INT NOT NULL,
    ScopePolicyId INT NOT NULL,
    AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    AssignedBy INT,
    PRIMARY KEY (UserId, ScopePolicyId),
    FOREIGN KEY (UserId) REFERENCES [User](Id) ON DELETE CASCADE,
    FOREIGN KEY (ScopePolicyId) REFERENCES DataScopePolicy(Id) ON DELETE CASCADE
);
```

#### 8. RoleDataScope（角色数据范围表）

```sql
CREATE TABLE RoleDataScope (
    RoleId INT NOT NULL,
    ScopePolicyId INT NOT NULL,
    AssignedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    PRIMARY KEY (RoleId, ScopePolicyId),
    FOREIGN KEY (RoleId) REFERENCES [Role](Id) ON DELETE CASCADE,
    FOREIGN KEY (ScopePolicyId) REFERENCES DataScopePolicy(Id) ON DELETE CASCADE
);
```

#### 9. AuditLog（审计日志表）

```sql
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
```

#### 10. ApprovalFlow（审批流表）

```sql
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
```

**审批类型**：
- `FreezeTaskAdjust`：冻结区任务变更
- `SOSacrifice`：牺牲 SO
- `StartedTaskAdjust`：已开工任务变更

#### 11. ApprovalNode（审批节点表）

```sql
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
```

#### 12. ApprovalRecord（审批记录表）

```sql
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
```

#### 13. ApprovalRule（审批规则表）

```sql
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
```

---

## 🔧 APS_Production 库的表结构调整

### PlanVersion 表新增字段

```sql
ALTER TABLE PlanVersion
ADD CreatedByUserId INT NULL,
    CreatedByUserName NVARCHAR(100) NULL;

ALTER TABLE PlanVersion
ADD CONSTRAINT FK_PlanVersion_User FOREIGN KEY (CreatedByUserId) 
    REFERENCES APS_Auth.[User](Id);

CREATE INDEX IX_PlanVersion_CreatedByUserId ON PlanVersion(CreatedByUserId);
```

### BatchLog 表新增字段

```sql
ALTER TABLE BatchLog
ADD TriggeredByUserId INT NULL,
    TriggeredByUserName NVARCHAR(100) NULL;

ALTER TABLE BatchLog
ADD CONSTRAINT FK_BatchLog_User FOREIGN KEY (TriggeredByUserId) 
    REFERENCES APS_Auth.[User](Id);

CREATE INDEX IX_BatchLog_TriggeredByUserId ON BatchLog(TriggeredByUserId);
```

---

## 🎨 后端实现要点

### 1. 项目结构调整

```
APS.Shared/
├── Auth/
│   ├── ApsAuthorizeAttribute.cs
│   ├── IPermissionService.cs
│   ├── IDataScopeService.cs
│   ├── IAuditService.cs
│   └── IApprovalService.cs

APS.Engine/
├── Auth/
│   ├── PermissionService.cs
│   ├── DataScopeService.cs
│   ├── AuditService.cs
│   └── ApprovalService.cs

APS.Orchestrator/
├── Controllers/
│   ├── AuthController.cs
│   ├── UserController.cs
│   ├── RoleController.cs
│   └── ApprovalController.cs
```

### 2. 自定义授权特性

```csharp
// APS.Shared/Auth/ApsAuthorizeAttribute.cs
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ApsAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    public string PermissionCode { get; }
    
    public ApsAuthorizeAttribute(string permissionCode)
    {
        PermissionCode = permissionCode;
    }
    
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var permissionService = context.HttpContext.RequestServices
            .GetRequiredService<IPermissionService>();
        
        var userId = GetUserIdFromClaims(context.HttpContext.User);
        
        if (!permissionService.HasPermissionAsync(userId, PermissionCode).Result)
        {
            context.Result = new ForbidResult();
        }
    }
}
```

### 3. 数据范围校验服务

```csharp
// APS.Shared/Auth/IDataScopeService.cs
public interface IDataScopeService
{
    Task<bool> CheckFactoryAccessAsync(int userId, int factoryId);
    Task<bool> CheckProductFamilyAccessAsync(int userId, int productFamilyId);
    Task<bool> CheckResourceOrgGroupAccessAsync(int userId, int resourceOrgGroupId);
    Task<List<int>> GetUserFactoriesAsync(int userId);
    Task<List<int>> GetUserProductFamiliesAsync(int userId);
    Task<List<int>> GetUserResourceOrgGroupsAsync(int userId);
}
```

### 4. 审计服务

```csharp
// APS.Shared/Auth/IAuditService.cs
public interface IAuditService
{
    Task LogAsync(AuditLogDto auditLog);
    Task<List<AuditLogDto>> QueryAsync(AuditLogQueryDto query);
}

public class AuditLogDto
{
    public int UserId { get; set; }
    public string UserName { get; set; }
    public string ActionCode { get; set; }
    public string ObjectType { get; set; }
    public string ObjectId { get; set; }
    public string Result { get; set; }
    public string ClientIp { get; set; }
    public int? PlanVersionId { get; set; }
    public string BatchNo { get; set; }
    public string RequestData { get; set; }
    public string ResponseData { get; set; }
    public string ErrorMessage { get; set; }
}
```

---

## 🎨 前端实现要点

### 1. 用户状态管理（Pinia）

```typescript
// stores/user.ts
import { defineStore } from 'pinia'

export const useUserStore = defineStore('user', {
  state: () => ({
    userId: 0,
    userName: '',
    displayName: '',
    token: '',
    permissions: [] as string[],
    factories: [] as number[],
    productFamilies: [] as number[],
    resourceOrgGroups: [] as number[]  // v5.0: ResourceGroup→ResourceOrgGroup
  }),
  
  getters: {
    isLoggedIn: (state) => !!state.token,
    hasPermission: (state) => (code: string) => state.permissions.includes(code),
    hasFactoryAccess: (state) => (factoryId: number) => state.factories.includes(factoryId),
    hasProductFamilyAccess: (state) => (pfId: number) => state.productFamilies.includes(pfId),
    hasResourceOrgGroupAccess: (state) => (rogId: number) => state.resourceOrgGroups.includes(rogId)
  },
  
  actions: {
    async login(loginName: string, password: string) {
      const response = await api.post('/auth/login', { loginName, password })
      this.token = response.data.token
      this.userId = response.data.userId
      this.userName = response.data.userName
      this.displayName = response.data.displayName
      this.permissions = response.data.permissions
      this.factories = response.data.factories
      this.productFamilies = response.data.productFamilies
      this.resourceOrgGroups = response.data.resourceOrgGroups
      
      localStorage.setItem('aps_token', this.token)
    },
    
    logout() {
      this.$reset()
      localStorage.removeItem('aps_token')
    }
  }
})
```

### 2. 路由守卫

```typescript
// router/index.ts
router.beforeEach(async (to, from, next) => {
  const userStore = useUserStore()
  
  if (to.meta.requiresAuth && !userStore.isLoggedIn) {
    next('/login')
    return
  }
  
  if (to.meta.permission && !userStore.hasPermission(to.meta.permission as string)) {
    next('/403')
    return
  }
  
  next()
})
```

### 3. 权限指令

```typescript
// directives/permission.ts
export const permission = {
  mounted(el: HTMLElement, binding: DirectiveBinding) {
    const userStore = useUserStore()
    const { value } = binding
    
    if (value && !userStore.hasPermission(value)) {
      el.parentNode?.removeChild(el)
    }
  }
}
```

---

## 📝 架构红线补充

### 权限相关架构红线

**红线14：权限校验必须在接口层和应用层双重校验**
- 接口层：使用 `[ApsAuthorize]` 特性声明式鉴权
- 应用层：使用 `IDataScopeService` 校验数据范围
- ❌ 禁止只在前端做权限控制
- ❌ 禁止只在接口层做权限控制，不做数据范围校验

**红线15：审计日志必须记录关键动作**
- 发起排程、调整任务、插单评估、修改配置等关键动作必须记录审计日志
- 审计日志必须关联 UserId、PlanVersionId、BatchNo
- ❌ 禁止遗漏关键动作的审计日志

**红线16：高风险动作必须走审批流**
- 冻结区任务变更、牺牲 SO、已开工任务变更必须走审批流
- 审批流必须支持多级审批
- ❌ 禁止绕过审批流直接执行高风险动作

**红线17：数据范围必须严格校验**
- 所有查询和修改操作必须校验数据范围
- 数据范围包括：工厂、产品族、资源组
- ❌ 禁止跨数据范围访问或修改数据

---

## 📅 实施时间表

### 阶段一（2-3周）

**Week 1**：
- Day 1-2: 创建 APS_Auth 数据库和表结构
- Day 3-4: 实现用户、角色、权限管理后端 API
- Day 5: 实现数据范围校验服务

**Week 2**：
- Day 1-2: 实现审计日志服务
- Day 3-4: 实现接口层鉴权（`[ApsAuthorize]` 特性）
- Day 5: 前端登录页面和路由守卫

**Week 3**：
- Day 1-2: 前端权限指令和状态管理
- Day 3-4: 集成测试
- Day 5: 文档更新和交付

### 阶段二（2周）

**Week 1**：
- Day 1-2: 创建审批流表结构
- Day 3-4: 实现审批流引擎
- Day 5: 实现冻结区任务变更审批

**Week 2**：
- Day 1-2: 实现牺牲 SO 审批
- Day 3-4: 前端审批管理页面
- Day 5: 集成测试和交付

---

**文档结束**

**交付时间**：2026-03-24  
**维护责任人**：2号位（技术负责人）  
**文档版本**：v1.0
