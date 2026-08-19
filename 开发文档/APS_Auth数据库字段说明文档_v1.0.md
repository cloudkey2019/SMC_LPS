# APS_Auth 数据库字段说明文档 v1.0

**文档定位**：APS_Auth 权限与审批数据库的完整字段说明  
**适用范围**：APS V1.0 单体解决方案  
**创建日期**：2026-03-24  
**数据库名称**：`APS_Auth`

---

## 📋 数据库概述

**库名**：`APS_Auth`  
**用途**：存储 APS 系统的权限、审批、审计相关数据  
**表数量**：13 张表  
**字符集**：NVARCHAR（支持中文）  
**时间类型**：DATETIME2（精度到微秒）

---

## 📊 表清单

| 序号 | 表名 | 中文名称 | 用途 | 记录数量级 |
|------|------|----------|------|-----------|
| 1 | User | 用户表 | 存储系统用户信息 | 10-100 |
| 2 | Role | 角色表 | 存储角色定义 | 7-20 |
| 3 | Permission | 权限表 | 存储权限定义 | 50-200 |
| 4 | UserRole | 用户角色关联表 | 用户与角色的多对多关系 | 10-200 |
| 5 | RolePermission | 角色权限关联表 | 角色与权限的多对多关系 | 100-500 |
| 6 | DataScopePolicy | 数据范围策略表 | 定义数据范围策略 | 10-100 |
| 7 | UserDataScope | 用户数据范围表 | 用户与数据范围的关联 | 10-200 |
| 8 | RoleDataScope | 角色数据范围表 | 角色与数据范围的关联 | 10-50 |
| 9 | AuditLog | 审计日志表 | 记录所有关键操作 | 10K-1M |
| 10 | ApprovalFlow | 审批流表 | 存储审批流实例 | 100-10K |
| 11 | ApprovalNode | 审批节点表 | 存储审批流的节点 | 200-30K |
| 12 | ApprovalRecord | 审批记录表 | 存储审批操作记录 | 200-30K |
| 13 | ApprovalRule | 审批规则表 | 定义审批规则 | 10-100 |

---

## 1️⃣ User（用户表）

### 业务用途
存储 APS 系统的用户账号信息，支持用户登录、权限校验、审计追踪。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 用户唯一标识 | 1 |
| LoginName | NVARCHAR(50) | NOT NULL, UNIQUE | 登录名，至少3个字符 | zhang.san |
| DisplayName | NVARCHAR(100) | NOT NULL | 显示名称（中文名） | 张三 |
| PasswordHash | NVARCHAR(500) | NOT NULL | 密码哈希值（BCrypt） | $2a$10$... |
| Email | NVARCHAR(100) | NULL | 邮箱地址 | zhang.san@company.com |
| PhoneNumber | NVARCHAR(20) | NULL | 手机号码 | 13800138000 |
| IsEnabled | BIT | NOT NULL, DEFAULT 1 | 是否启用 | 1 |
| LastLoginAt | DATETIME2 | NULL | 最后登录时间 | 2026-03-24 08:30:00 |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-20 10:00:00 |
| UpdatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 更新时间 | 2026-03-24 09:00:00 |

### 索引
- `IX_User_LoginName`：登录名索引（唯一）
- `IX_User_IsEnabled`：启用状态索引

### 约束
- `CK_User_LoginName`：登录名至少3个字符

### 业务规则
1. 登录名不可重复
2. 密码必须使用 BCrypt 等安全算法加密存储
3. 禁用用户（IsEnabled=0）无法登录，但保留历史数据
4. 删除用户会级联删除 UserRole、UserDataScope 关联

---

## 2️⃣ Role（角色表）

### 业务用途
定义系统角色，角色是权限的集合，用户通过角色获得权限。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 角色唯一标识 | 1 |
| RoleCode | NVARCHAR(50) | NOT NULL, UNIQUE | 角色编码，必须以 aps. 开头 | aps.planner |
| RoleName | NVARCHAR(100) | NOT NULL | 角色名称 | 计划员 |
| Description | NVARCHAR(500) | NULL | 角色描述 | 发起排程、查看计划、调整任务 |
| IsSystemRole | BIT | NOT NULL, DEFAULT 0 | 是否系统预置角色 | 1 |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-20 10:00:00 |

### 索引
- `IX_Role_RoleCode`：角色编码索引（唯一）

### 约束
- `CK_Role_RoleCode`：角色编码必须以 `aps.` 开头

### 预置角色

| RoleCode | RoleName | 说明 |
|----------|----------|------|
| aps.admin.system | 系统管理员 | 最高权限，管理所有系统配置和用户 |
| aps.admin.aps | APS管理员 | 管理APS系统配置、参数、规则 |
| aps.planner | 计划员 | 发起排程、查看计划、调整任务 |
| aps.supervisor.workshop | 车间主管 | 审批冻结区任务变更、查看车间计划 |
| aps.coordinator.material | 物料协同人员 | 查看物料需求、库存信息 |
| aps.viewer.management | 管理层查看 | 查看所有计划和报表，无修改权限 |
| aps.service.api | 系统接口账号 | 用于系统间接口调用 |

### 业务规则
1. 系统预置角色（IsSystemRole=1）不可删除
2. 删除角色会级联删除 RolePermission、RoleDataScope 关联

---

## 3️⃣ Permission（权限表）

### 业务用途
定义系统的所有权限点，权限是最小的访问控制单元。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 权限唯一标识 | 1 |
| PermissionCode | NVARCHAR(100) | NOT NULL, UNIQUE | 权限编码，必须以 aps. 开头 | aps.plan.run |
| PermissionName | NVARCHAR(100) | NOT NULL | 权限名称 | 发起排程 |
| Module | NVARCHAR(50) | NOT NULL | 所属模块 | Plan |
| ActionType | NVARCHAR(50) | NOT NULL | 动作类型 | Execute |
| Description | NVARCHAR(500) | NULL | 权限描述 | 发起新的排程计算 |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-20 10:00:00 |

### 索引
- `IX_Permission_Code`：权限编码索引（唯一）
- `IX_Permission_Module`：模块索引

### 约束
- `CK_Permission_Code`：权限编码必须以 `aps.` 开头

### 权限编码规范
格式：`aps.<module>.<action>`

**Module（模块）**：
- `plan`：计划模块
- `task`：任务模块
- `ctp`：插单评估模块
- `config`：配置模块
- `approval`：审批模块
- `audit`：审计模块
- `user`：用户管理模块
- `role`：角色管理模块

**ActionType（动作类型）**：
- `View`：查看
- `Edit`：编辑
- `Execute`：执行

### 核心权限清单

| PermissionCode | PermissionName | Module | ActionType |
|----------------|----------------|--------|------------|
| aps.plan.view | 查看计划 | Plan | View |
| aps.plan.run | 发起排程 | Plan | Execute |
| aps.plan.publish | 发布计划 | Plan | Execute |
| aps.task.view | 查看任务 | Task | View |
| aps.task.adjust | 调整任务 | Task | Edit |
| aps.task.freeze.override | 修改冻结区任务 | Task | Edit |
| aps.task.started.override | 修改已开工任务 | Task | Edit |
| aps.ctp.view | 查看插单评估 | CTP | View |
| aps.ctp.evaluate | 发起插单评估 | CTP | Execute |
| aps.ctp.commit | 提交插单方案 | CTP | Execute |
| aps.config.supply_context.view | 查看供给上下文 | Config | View |
| aps.config.supply_context.edit | 修改供给上下文 | Config | Edit |
| aps.config.inventory_rule.view | 查看库存规则 | Config | View |
| aps.config.inventory_rule.edit | 修改库存规则 | Config | Edit |
| aps.approval.view | 查看审批 | Approval | View |
| aps.approval.approve | 审批 | Approval | Execute |
| aps.audit.view | 查看审计日志 | Audit | View |
| aps.user.view | 查看用户 | User | View |
| aps.user.manage | 管理用户 | User | Edit |
| aps.role.view | 查看角色 | Role | View |
| aps.role.manage | 管理角色 | Role | Edit |

---

## 4️⃣ UserRole（用户角色关联表）

### 业务用途
建立用户与角色的多对多关系，一个用户可以拥有多个角色。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| UserId | INT | PK, FK | 用户ID | 1 |
| RoleId | INT | PK, FK | 角色ID | 3 |
| AssignedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 分配时间 | 2026-03-20 10:00:00 |
| AssignedBy | INT | FK | 分配人ID | 1 |

### 索引
- `IX_UserRole_UserId`：用户ID索引
- `IX_UserRole_RoleId`：角色ID索引

### 外键
- `FK_UserRole_User`：关联 User 表，级联删除
- `FK_UserRole_Role`：关联 Role 表，级联删除
- `FK_UserRole_AssignedBy`：关联 User 表（分配人）

---

## 5️⃣ RolePermission（角色权限关联表）

### 业务用途
建立角色与权限的多对多关系，一个角色可以拥有多个权限。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| RoleId | INT | PK, FK | 角色ID | 3 |
| PermissionId | INT | PK, FK | 权限ID | 10 |
| AssignedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 分配时间 | 2026-03-20 10:00:00 |

### 索引
- `IX_RolePermission_RoleId`：角色ID索引
- `IX_RolePermission_PermissionId`：权限ID索引

### 外键
- `FK_RolePermission_Role`：关联 Role 表，级联删除
- `FK_RolePermission_Permission`：关联 Permission 表，级联删除

---

## 6️⃣ DataScopePolicy（数据范围策略表）

### 业务用途
定义数据范围策略，用于控制用户可以访问哪些工厂、产品族、资源组的数据。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 策略唯一标识 | 1 |
| ScopeType | NVARCHAR(50) | NOT NULL | 范围类型 | Factory |
| ScopeValue | NVARCHAR(100) | NOT NULL | 范围值 | 1 |
| Description | NVARCHAR(500) | NULL | 描述 | 工厂1 |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-20 10:00:00 |

### 索引
- `IX_DataScope_Type`：范围类型索引
- `IX_DataScope_Value`：范围值索引

### 约束
- `CK_DataScope_Type`：范围类型必须是 Factory、ProductFamily、ResourceOrgGroup、Global 之一
- `UQ_DataScope_Type_Value`：(ScopeType, ScopeValue) 组合唯一

### ScopeType 说明

| ScopeType | 说明 | ScopeValue 示例 | 用途 |
|-----------|------|----------------|------|
| Global | 全局范围 | * | 可访问所有数据（系统管理员） |
| Factory | 工厂级 | 1, 2, 3 | 限制访问特定工厂的数据 |
| ProductFamily | 产品族级 | 101, 102 | 限制访问特定产品族的数据 |
| ResourceOrgGroup | 资源组织维度级（v5.0替代原ResourceGroup） | 201, 202 | 限制访问特定资源组织维度的数据 |

### 业务规则
1. Global 范围的 ScopeValue 固定为 `*`
2. Factory/ProductFamily/ResourceOrgGroup 的 ScopeValue 为对应表的 ID
3. 用户的数据范围 = 用户数据范围 ∪ 角色数据范围

---

## 7️⃣ UserDataScope（用户数据范围表）

### 业务用途
建立用户与数据范围策略的多对多关系，直接为用户分配数据范围。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| UserId | INT | PK, FK | 用户ID | 1 |
| ScopePolicyId | INT | PK, FK | 数据范围策略ID | 5 |
| AssignedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 分配时间 | 2026-03-20 10:00:00 |
| AssignedBy | INT | FK | 分配人ID | 1 |

### 索引
- `IX_UserDataScope_UserId`：用户ID索引
- `IX_UserDataScope_ScopePolicyId`：策略ID索引

### 外键
- `FK_UserDataScope_User`：关联 User 表，级联删除
- `FK_UserDataScope_DataScopePolicy`：关联 DataScopePolicy 表，级联删除
- `FK_UserDataScope_AssignedBy`：关联 User 表（分配人）

---

## 8️⃣ RoleDataScope（角色数据范围表）

### 业务用途
建立角色与数据范围策略的多对多关系，为角色分配数据范围。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| RoleId | INT | PK, FK | 角色ID | 3 |
| ScopePolicyId | INT | PK, FK | 数据范围策略ID | 5 |
| AssignedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 分配时间 | 2026-03-20 10:00:00 |

### 索引
- `IX_RoleDataScope_RoleId`：角色ID索引
- `IX_RoleDataScope_ScopePolicyId`：策略ID索引

### 外键
- `FK_RoleDataScope_Role`：关联 Role 表，级联删除
- `FK_RoleDataScope_DataScopePolicy`：关联 DataScopePolicy 表，级联删除

---

## 9️⃣ AuditLog（审计日志表）

### 业务用途
记录所有关键操作的审计日志，用于安全审计、问题追溯、合规要求。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | BIGINT | PK, IDENTITY | 日志唯一标识 | 1 |
| UserId | INT | NOT NULL, FK | 操作用户ID | 1 |
| UserName | NVARCHAR(100) | NOT NULL | 操作用户名（冗余） | zhang.san |
| ActionCode | NVARCHAR(100) | NOT NULL | 动作编码 | aps.plan.run |
| ObjectType | NVARCHAR(50) | NULL | 操作对象类型 | PlanVersion |
| ObjectId | NVARCHAR(100) | NULL | 操作对象ID | 123 |
| Result | NVARCHAR(20) | NOT NULL | 操作结果 | Success |
| OccurredAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 发生时间 | 2026-03-24 08:30:00 |
| ClientIp | NVARCHAR(50) | NULL | 客户端IP | 192.168.1.100 |
| PlanVersionId | INT | NULL | 关联的计划版本ID | 123 |
| BatchNo | NVARCHAR(50) | NULL | 关联的批次号 | B20260324001 |
| ApprovalId | INT | NULL | 关联的审批流ID | 5 |
| RequestData | NVARCHAR(MAX) | NULL | 请求数据（JSON） | {"factoryId":1} |
| ResponseData | NVARCHAR(MAX) | NULL | 响应数据（JSON） | {"versionId":123} |
| ErrorMessage | NVARCHAR(MAX) | NULL | 错误信息 | NULL |

### 索引
- `IX_AuditLog_UserId`：用户ID索引
- `IX_AuditLog_ActionCode`：动作编码索引
- `IX_AuditLog_OccurredAt`：发生时间索引
- `IX_AuditLog_PlanVersionId`：计划版本ID索引
- `IX_AuditLog_BatchNo`：批次号索引
- `IX_AuditLog_Result`：结果索引

### 约束
- `CK_AuditLog_Result`：结果必须是 Success、Failed、Denied 之一

### Result 说明

| Result | 说明 | 场景 |
|--------|------|------|
| Success | 成功 | 操作成功执行 |
| Failed | 失败 | 操作执行失败（技术错误） |
| Denied | 拒绝 | 权限不足，操作被拒绝 |

### 需要记录审计日志的关键动作

| ActionCode | 说明 |
|------------|------|
| aps.plan.run | 发起排程 |
| aps.plan.publish | 发布计划 |
| aps.task.adjust | 调整任务 |
| aps.task.freeze.override | 修改冻结区任务 |
| aps.task.started.override | 修改已开工任务 |
| aps.ctp.evaluate | 发起插单评估 |
| aps.ctp.commit | 提交插单方案 |
| aps.config.supply_context.edit | 修改供给上下文 |
| aps.config.inventory_rule.edit | 修改库存规则 |
| aps.config.resource.edit | 修改资源配置 |
| aps.approval.approve | 审批操作 |
| aps.user.manage | 管理用户 |
| aps.role.manage | 管理角色 |

### 业务规则
1. 审计日志只增不改不删
2. 敏感数据（如密码）不记录到 RequestData/ResponseData
3. 审计日志保留期：至少1年，建议3年
4. 数据量大时考虑按月分表或归档

---

## 🔟 ApprovalFlow（审批流表）

### 业务用途
存储审批流实例，每个需要审批的操作创建一个审批流。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 审批流唯一标识 | 1 |
| ApprovalType | NVARCHAR(50) | NOT NULL | 审批类型 | FreezeTaskAdjust |
| ObjectType | NVARCHAR(50) | NOT NULL | 审批对象类型 | Task |
| ObjectId | NVARCHAR(100) | NOT NULL | 审批对象ID | 12345 |
| ApplicantUserId | INT | NOT NULL, FK | 申请人ID | 5 |
| ApplicantUserName | NVARCHAR(100) | NOT NULL | 申请人名称（冗余） | zhang.san |
| Status | NVARCHAR(20) | NOT NULL, DEFAULT 'Pending' | 审批状态 | Pending |
| CurrentNodeSeq | INT | NOT NULL, DEFAULT 1 | 当前审批节点序号 | 1 |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-24 08:30:00 |
| CompletedAt | DATETIME2 | NULL | 完成时间 | NULL |
| PlanVersionId | INT | NULL | 关联的计划版本ID | 123 |
| BatchNo | NVARCHAR(50) | NULL | 关联的批次号 | B20260324001 |
| Reason | NVARCHAR(1000) | NULL | 申请理由 | 紧急插单需要调整冻结区任务 |

### 索引
- `IX_ApprovalFlow_Status`：状态索引
- `IX_ApprovalFlow_ApplicantUserId`：申请人ID索引
- `IX_ApprovalFlow_Type`：审批类型索引
- `IX_ApprovalFlow_CreatedAt`：创建时间索引

### 约束
- `CK_ApprovalFlow_Status`：状态必须是 Pending、Approved、Rejected、Cancelled 之一

### ApprovalType 说明

| ApprovalType | 说明 | 审批场景 |
|--------------|------|----------|
| FreezeTaskAdjust | 冻结区任务变更 | 修改冻结区内的任务时间或资源 |
| SOSacrifice | 牺牲SO | 挤占他单能力、牺牲其他订单 |
| StartedTaskAdjust | 已开工任务变更 | 修改已开工的任务 |

### Status 说明

| Status | 说明 | 终态 |
|--------|------|------|
| Pending | 待审批 | 否 |
| Approved | 已通过 | 是 |
| Rejected | 已拒绝 | 是 |
| Cancelled | 已取消 | 是 |

### 业务规则
1. 审批流创建后，根据 ApprovalRule 自动生成 ApprovalNode
2. 多级审批按 NodeSeq 顺序执行
3. 任一节点拒绝，整个审批流状态变为 Rejected
4. 所有节点通过，审批流状态变为 Approved

---

## 1️⃣1️⃣ ApprovalNode（审批节点表）

### 业务用途
存储审批流的各个审批节点，支持多级审批。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 节点唯一标识 | 1 |
| ApprovalFlowId | INT | NOT NULL, FK | 审批流ID | 1 |
| NodeSeq | INT | NOT NULL | 节点序号（1, 2, 3...） | 1 |
| ApproverRoleId | INT | FK | 审批人角色ID | 4 |
| ApproverUserId | INT | FK | 审批人用户ID | 8 |
| Status | NVARCHAR(20) | NOT NULL, DEFAULT 'Pending' | 节点状态 | Pending |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-24 08:30:00 |

### 索引
- `IX_ApprovalNode_FlowId`：审批流ID索引
- `IX_ApprovalNode_Status`：状态索引
- `IX_ApprovalNode_RoleId`：角色ID索引
- `IX_ApprovalNode_UserId`：用户ID索引

### 约束
- `CK_ApprovalNode_Status`：状态必须是 Pending、Approved、Rejected、Skipped 之一
- `UQ_ApprovalNode_Flow_Seq`：(ApprovalFlowId, NodeSeq) 组合唯一

### 外键
- `FK_ApprovalNode_ApprovalFlow`：关联 ApprovalFlow 表，级联删除
- `FK_ApprovalNode_Role`：关联 Role 表
- `FK_ApprovalNode_User`：关联 User 表

### Status 说明

| Status | 说明 |
|--------|------|
| Pending | 待审批 |
| Approved | 已通过 |
| Rejected | 已拒绝 |
| Skipped | 已跳过（前序节点拒绝） |

### 业务规则
1. ApproverRoleId 和 ApproverUserId 至少有一个不为空
2. 如果指定了 ApproverRoleId，则该角色的所有用户都可以审批
3. 如果指定了 ApproverUserId，则只有该用户可以审批
4. NodeSeq 从 1 开始，按顺序执行

---

## 1️⃣2️⃣ ApprovalRecord（审批记录表）

### 业务用途
记录每个审批节点的审批操作，包括审批人、决策、意见。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 记录唯一标识 | 1 |
| ApprovalNodeId | INT | NOT NULL, FK | 审批节点ID | 1 |
| ApproverUserId | INT | NOT NULL, FK | 审批人ID | 8 |
| ApproverUserName | NVARCHAR(100) | NOT NULL | 审批人名称（冗余） | li.si |
| Decision | NVARCHAR(20) | NOT NULL | 审批决策 | Approved |
| Comment | NVARCHAR(1000) | NULL | 审批意见 | 同意调整，注意控制影响范围 |
| ApprovedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 审批时间 | 2026-03-24 09:00:00 |

### 索引
- `IX_ApprovalRecord_NodeId`：节点ID索引
- `IX_ApprovalRecord_ApproverUserId`：审批人ID索引
- `IX_ApprovalRecord_ApprovedAt`：审批时间索引

### 约束
- `CK_ApprovalRecord_Decision`：决策必须是 Approved、Rejected 之一

### 外键
- `FK_ApprovalRecord_ApprovalNode`：关联 ApprovalNode 表，级联删除
- `FK_ApprovalRecord_User`：关联 User 表

### Decision 说明

| Decision | 说明 |
|----------|------|
| Approved | 通过 |
| Rejected | 拒绝 |

---

## 1️⃣3️⃣ ApprovalRule（审批规则表）

### 业务用途
定义审批规则，根据审批类型和数据范围自动生成审批节点。

### 字段说明

| 字段名 | 类型 | 约束 | 说明 | 示例值 |
|--------|------|------|------|--------|
| Id | INT | PK, IDENTITY | 规则唯一标识 | 1 |
| ApprovalType | NVARCHAR(50) | NOT NULL | 审批类型 | FreezeTaskAdjust |
| FactoryId | INT | NULL | 工厂ID（NULL表示所有工厂） | 1 |
| ProductFamilyId | INT | NULL | 产品族ID（NULL表示所有产品族） | NULL |
| ResourceOrgGroupId | INT | NULL | 资源组织维度ID（NULL表示所有，v5.0替代原ResourceGroupId） | NULL |
| NodeSeq | INT | NOT NULL | 节点序号 | 1 |
| ApproverRoleId | INT | NOT NULL, FK | 审批人角色ID | 4 |
| IsEnabled | BIT | NOT NULL, DEFAULT 1 | 是否启用 | 1 |
| CreatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 创建时间 | 2026-03-20 10:00:00 |
| UpdatedAt | DATETIME2 | NOT NULL, DEFAULT GETDATE() | 更新时间 | 2026-03-24 09:00:00 |

### 索引
- `IX_ApprovalRule_Type`：审批类型索引
- `IX_ApprovalRule_IsEnabled`：启用状态索引
- `IX_ApprovalRule_FactoryId`：工厂ID索引
- `IX_ApprovalRule_ProductFamilyId`：产品族ID索引
- `IX_ApprovalRule_ResourceOrgGroupId`：资源组织维度ID索引

### 外键
- `FK_ApprovalRule_Role`：关联 Role 表

### 业务规则
1. 规则匹配优先级：FactoryId + ProductFamilyId + ResourceOrgGroupId > FactoryId + ProductFamilyId > FactoryId > 全局（所有为NULL）
2. 同一审批类型可以有多个规则，按 NodeSeq 排序生成审批节点
3. 禁用的规则（IsEnabled=0）不参与审批流生成

### 示例规则

**冻结区任务变更审批规则**：
1. NodeSeq=1, ApproverRoleId=车间主管（aps.supervisor.workshop）
2. NodeSeq=2, ApproverRoleId=APS管理员（aps.admin.aps）

**牺牲SO审批规则**：
1. NodeSeq=1, ApproverRoleId=计划员（aps.planner）
2. NodeSeq=2, ApproverRoleId=车间主管（aps.supervisor.workshop）
3. NodeSeq=3, ApproverRoleId=APS管理员（aps.admin.aps）

---

## 📊 表关系图

```
User ──┬── UserRole ── Role ──┬── RolePermission ── Permission
       │                      │
       └── UserDataScope      └── RoleDataScope
                   │                      │
                   └──── DataScopePolicy ─┘

User ── AuditLog

User ── ApprovalFlow ──┬── ApprovalNode ── ApprovalRecord ── User
                       │
                       └── (关联 PlanVersionId, BatchNo)

Role ── ApprovalRule
```

---

## 🔐 安全建议

1. **密码安全**：
   - 使用 BCrypt 算法加密密码（成本因子至少10）
   - 禁止明文存储密码
   - 定期提醒用户修改密码

2. **Token 安全**：
   - JWT Token 过期时间：2小时
   - Token 中不包含敏感信息
   - 使用 HTTPS 传输 Token

3. **审计日志**：
   - 所有关键操作必须记录审计日志
   - 审计日志不可修改、不可删除
   - 定期备份审计日志

4. **数据范围**：
   - 所有查询和修改操作必须校验数据范围
   - 禁止跨数据范围访问数据

---

**文档结束**

**交付时间**：2026-03-24  
**维护责任人**：2号位（技术负责人）  
**文档版本**：v1.0
