# APS 权限与审批设计方案 v1.0

**文档定位**：APS 系统权限控制、数据范围控制、审批流与审计留痕的统一设计方案  
**适用范围**：APS 单体解决方案（Web 前端 + 后端 API + 排程/批处理服务）  
**适用版本**：V1.0 / V1.x  
**关联基线**：当前已确定的 APS 数据架构、防腐层、BatchNo、PlanVersion、ODS 批次预展开、Frozen 区下发 MES、02:00 本地快照排程等口径

---

# 1. 设计目标

APS 不只是一个查询系统，而是一个会对生产执行产生直接影响的系统。  
因此权限控制必须覆盖以下四类风险：

1. **动作风险**：谁能发起排程、发布版本、冻结区下发、插单评估、人工调度、版本回滚。
2. **数据风险**：谁能看到或操作哪些工厂、产品族、资源组的数据。
3. **审批风险**：谁有权放行高风险动作，例如牺牲 SO、改冻结区、变更已开工任务。
4. **追责风险**：事后要能追溯“谁、在何时、对什么对象、做了什么”。

本方案的目标是建立一套适合当前 APS **单体架构**的权限体系，满足：

- 业务可控
- 架构简单
- 审计闭环
- 可逐步演进，不一次性过度设计

---

# 2. 总体设计原则

## 2.1 四层权限模型

本方案采用四层模型：

- **RBAC（角色权限）**：决定“能不能做某个动作”
- **DataScope（数据范围）**：决定“能看/能改哪些数据”
- **Approval Matrix（审批矩阵）**：决定“哪些动作必须审批后才能生效”
- **Audit（审计留痕）**：决定“所有关键动作都能事后追溯”

## 2.2 不采用超复杂 ABAC 规则引擎

V1 不建议引入复杂的 ABAC/策略表达式引擎。  
原因：

- 你们当前是单体架构，优先追求稳定和可落地
- APS 的核心风险动作是固定的，可通过权限码 + 数据范围 + 审批矩阵覆盖
- 过早引入通用规则引擎，会提高开发和维护成本

## 2.3 业务主键与权限分离

权限控制依赖用户、角色、数据范围和审批关系。  
排程核心领域模型依然只关注业务对象本身，不直接依赖用户角色判断。  
即：

- **接口层**：校验动作权限
- **应用服务层**：校验数据范围 + 是否需要审批
- **领域层/排程内核**：只接收已授权命令

## 2.4 默认粒度

V1 建议默认做到以下粒度：

- **动作级**
- **数据范围级**
- **审批级**
- **审计级**

V1 不建议先做：

- 单订单逐条授权
- 字段级复杂权限
- 动态表达式规则平台

---

# 3. 权限控制范围

## 3.1 需要纳入权限控制的业务模块

1. 计划版本与排程
2. 插单评估（ATP/CTP）
3. 冻结区计划发布 / 下发 MES
4. 人工调度与任务调整
5. ExplainTrace 与版本对比
6. ODS 批次监控与重试
7. 映射表/库存规则/参数配置
8. 导出与报表
9. 审批流
10. 审计日志

## 3.2 关键高风险动作

以下动作为高风险动作，不能只做普通按钮权限，必须有审批和审计：

- 修改冻结区任务
- 牺牲 SO / 占用他单能力
- 改动已开工任务
- 发布正式版本
- 回滚到历史版本
- 强制重跑 ODS 关键批次
- 手工覆盖 MaterialMapping
- 手工覆盖库存来源优先级
- 直接下发 MES

---

# 4. 角色设计

V1 建议控制在 7 类角色以内。

## 4.1 角色清单

### 4.1.1 系统管理员
职责：
- 用户、角色、权限维护
- 系统参数管理
- 审计日志查看
- 接口账号管理

说明：
- 不默认具备生产计划调整权
- 可作为超级管理员临时授权

### 4.1.2 APS 管理员 / 计划平台主管
职责：
- 全局查看 APS 数据
- 发起正式排程
- 发布计划版本
- 配置排程参数/Fence/策略
- 查看和处理批次异常
- 审批部分高风险动作

### 4.1.3 计划员
职责：
- 查看自己负责范围内的数据
- 发起试算/排程
- 发起插单评估
- 进行人工微调
- 发起冻结区调整申请
- 导出报表

说明：
- 默认不能直接发布全局正式版本
- 默认不能绕过审批修改高风险对象

### 4.1.4 车间主管 / 制造主管
职责：
- 查看所属工厂/资源范围内计划
- 审批冻结区变更
- 审批已开工任务变更
- 审批牺牲 SO 等高风险场景
- 查看车间级 ExplainTrace 与负荷

### 4.1.5 采购/物料协同人员
职责：
- 查看缺料、库存、latest need time
- 查看采购相关预警
- 查看物料映射与来源解释
- 不参与排程核心动作

### 4.1.6 管理层 / 经营分析
职责：
- 只读查看 KPI、甘特、负荷、PSI、版本对比、ExplainTrace 摘要
- 导出管理报表
- 不允许修改计划和配置

### 4.1.7 系统接口账号
职责：
- 供 ERP / MES / ODS / 批处理服务调用
- 不参与人工登录
- 权限最小化

---

# 5. 权限码设计

## 5.1 设计原则

权限码必须以**业务动作**为中心，而不是以页面名称为中心。  
命名规则建议：

`aps.<模块>.<动作>`

例如：
- `aps.plan.view`
- `aps.plan.run`
- `aps.plan.publish`

## 5.2 建议权限码清单

### 5.2.1 计划版本与排程
- `aps.plan.view`
- `aps.plan.run`
- `aps.plan.publish`
- `aps.plan.rollback`
- `aps.plan.compare`
- `aps.plan.export`

### 5.2.2 任务调整与冻结区
- `aps.task.view`
- `aps.task.adjust`
- `aps.task.freeze.override`
- `aps.task.started.override`

### 5.2.3 插单评估
- `aps.ctp.view`
- `aps.ctp.evaluate`
- `aps.ctp.commit`

### 5.2.4 ExplainTrace / 审计 / 对比
- `aps.trace.view`
- `aps.trace.compare`
- `aps.audit.view`

### 5.2.5 参数与规则配置
- `aps.config.fence.edit`
- `aps.config.strategy.edit`
- `aps.config.leadtime.edit`
- `aps.config.inventory.rule.edit`

### 5.2.6 映射与库存
- `aps.mapping.view`
- `aps.mapping.edit`
- `aps.inventory.view`
- `aps.inventory.override`

### 5.2.7 ODS/批次运维
- `aps.ods.batch.view`
- `aps.ods.batch.retry`
- `aps.ods.batch.force.complete`

### 5.2.8 审批
- `aps.approval.freeze.approve`
- `aps.approval.so_sacrifice.approve`
- `aps.approval.started_task.approve`
- `aps.approval.publish.approve`
- `aps.approval.rollback.approve`

### 5.2.9 导出
- `aps.report.export`
- `aps.gantt.export`
- `aps.psi.export`

---

# 6. 数据范围（DataScope）设计

## 6.1 为什么需要 DataScope

APS 的风险不只是“有没有按钮权限”，还包括“是否越权看到了不该看的工厂/产品族/资源组数据”。

因此 V1 必须具备数据范围能力。

## 6.2 默认范围维度

V1 建议最少支持：

- **Factory（工厂）**
- **ProductFamily（产品族）**

可扩展支持：

- **ResourceOrgGroup（资源组织维度，v5.0替代原ResourceGroup）**

## 6.3 推荐数据范围模型

一个用户最终生效的数据范围 =

> 用户直接分配的数据范围  
> + 角色继承的数据范围  
> + 特殊放行规则（如管理员）

## 6.4 生效规则

### 6.4.1 默认交集原则
对于“修改类动作”，建议默认取交集，即：
- 既有动作权限
- 又在数据范围内

### 6.4.2 查询类可取并集
对于“查询/报表查看”，可采用用户范围 + 角色范围并集。

### 6.4.3 全局角色例外
系统管理员、APS 管理员可配置为全局范围。

## 6.5 典型例子

### 例 1：A 工厂装配计划员
- 角色：计划员
- 数据范围：工厂 A、产品族 装配类
- 可做：A 工厂装配类排程、插单评估、导出
- 不可做：B 工厂计划调整

### 例 2：机加车间主管
- 角色：车间主管
- 数据范围：工厂 A、资源组 机加组
- 可做：审批机加冻结区调整
- 不可做：审批装配任务变更

---

# 7. 审批设计

## 7.1 审批与普通权限的边界

- **普通权限**：决定用户能否“发起”
- **审批**：决定高风险动作能否“生效”

## 7.2 必须审批的场景

### 7.2.1 冻结区任务变更
场景：
- 修改冻结区任务时间
- 修改冻结区资源
- 删除/替换冻结区任务

建议审批人：
- 车间主管 → APS 管理员（可多级）

### 7.2.2 牺牲 SO / 挤占他单能力
场景：
- 插单方案选择会造成他单延期或牺牲

建议审批人：
- 车间主管 / 制造主管
- 必要时经营负责人

### 7.2.3 已开工任务变更
场景：
- 开工后暂停、改机、改顺序

建议审批人：
- 车间主管

### 7.2.4 正式发布计划版本
场景：
- 将版本设为正式版本
- 下发 MES

建议审批人：
- APS 管理员 / 计划平台主管

### 7.2.5 历史版本回滚
场景：
- 将历史版本恢复为生效版本

建议审批人：
- APS 管理员
- 必要时制造管理层

### 7.2.6 ODS 关键批次强制重跑
场景：
- BOM 工作集重跑
- Mapping 强制刷新
- Workset 强制置 READY

建议审批人：
- APS 管理员 / 系统管理员

## 7.3 审批超时策略

V1 建议默认支持三种策略：

- `AUTO_PASS`
- `AUTO_REJECT`
- `MANUAL_ESCALATE`

默认建议：
- 对生产风险动作，优先 `MANUAL_ESCALATE`
- 不建议大范围使用自动通过

## 7.4 审批单关联对象

审批单必须关联到具体业务对象，例如：
- `PlanVersionId`
- `TaskId`
- `BatchNo`
- `OrderNo`
- `AdjustmentRequestId`

---

# 8. 审计设计

## 8.1 审计记录范围

以下动作必须进审计：

- 登录/退出
- 发起排程
- 发布/回滚版本
- 插单评估与提交方案
- 冻结区调整
- 已开工任务变更
- Mapping 修改
- 库存规则修改
- Fence / 参数配置修改
- 批次重试 / 强制完成
- 导出敏感报表

## 8.2 审计最小字段

建议记录：

- `AuditId`
- `UserId`
- `UserName`
- `ActionCode`
- `ObjectType`
- `ObjectId`
- `OldValue`
- `NewValue`
- `OccurredAt`
- `ClientIp`
- `ApprovalId`
- `PlanVersionId`
- `BatchNo`
- `Result`

## 8.3 为什么 APS 审计不能省

APS 的动作会直接影响：
- 计划版本
- 冻结区执行
- 订单交付
- MES 下发
- 历史追责

因此审计必须和审批、计划版本、BatchNo 关联起来。

---

# 9. 单体架构下的后端实现建议

## 9.1 总体建议

采用：

- **认证统一**
- **接口层声明式鉴权**
- **应用服务层数据范围校验**
- **审批前置**
- **审计自动落地**

## 9.2 接口层

每个 API 只声明所需的权限码。  
示例：

- `POST /api/plan/run` 需要 `aps.plan.run`
- `POST /api/plan/publish` 需要 `aps.plan.publish`
- `POST /api/ctp/evaluate` 需要 `aps.ctp.evaluate`

## 9.3 应用服务层

在服务层做：
- 数据范围判断
- 是否进入审批流判断
- 是否写审计日志

不要把审批逻辑散落到前端按钮里。

## 9.4 领域层 / 排程内核

排程内核不直接判断用户角色。  
只接收经过授权、审批通过的命令对象。

这能避免权限逻辑侵入算法核心。

---

# 10. 数据库表设计建议

## 10.1 基础权限表

### User
- Id
- LoginName
- DisplayName
- PasswordHash / ExternalIdentity
- IsEnabled
- CreatedAt

### Role
- Id
- RoleCode
- RoleName
- IsSystemRole

### Permission
- Id
- PermissionCode
- PermissionName
- Module
- ActionType

### UserRole
- UserId
- RoleId

### RolePermission
- RoleId
- PermissionId

## 10.2 数据范围表

### DataScopePolicy
- Id
- ScopeType（Factory/ProductFamily/ResourceOrgGroup）
- ScopeValue
- Description

### UserDataScope
- UserId
- ScopePolicyId

### RoleDataScope
- RoleId
- ScopePolicyId

## 10.3 审批表

### ApprovalFlow
- Id
- ApprovalType
- ObjectType
- ObjectId
- ApplicantUserId
- Status
- CreatedAt

### ApprovalNode
- Id
- ApprovalFlowId
- NodeSeq
- ApproverRole / ApproverUser
- TimeoutStrategy
- Status

### ApprovalRecord
- Id
- ApprovalNodeId
- ApproverUserId
- Decision
- Comment
- ApprovedAt

### ApprovalRule
- Id
- ApprovalType
- FactoryId
- ProductFamilyId
- ResourceOrgGroupId
- RuleExpression / FixedApproverRole

## 10.4 审计表

### AuditLog
- Id
- UserId
- ActionCode
- ObjectType
- ObjectId
- Result
- OccurredAt
- ClientIp
- PlanVersionId
- BatchNo
- ApprovalId

### AuditEntityChange
- Id
- AuditLogId
- FieldName
- OldValue
- NewValue

---

# 11. 权限与审批在 APS 关键场景中的落地方式

## 11.1 发起排程
- 权限：`aps.plan.run`
- 数据范围：必须在允许工厂/产品族范围内
- 审计：必须记录
- 审批：一般不需要

## 11.2 正式发布版本
- 权限：`aps.plan.publish`
- 数据范围：版本所属工厂/产品族
- 审批：建议需要
- 审计：必须记录
- 影响：允许下发 MES

## 11.3 冻结区微调
- 权限：`aps.task.adjust`
- 若对象在冻结区：必须走审批
- 审计：必须记录旧值/新值

## 11.4 插单评估方案提交
- 权限：`aps.ctp.commit`
- 若导致牺牲 SO：必须审批
- 审计：记录受影响订单清单

## 11.5 Mapping 覆盖
- 权限：`aps.mapping.edit`
- 建议始终需要审批或双人复核
- 审计：必须记录版本变化

## 11.6 ODS 批次强制完成/重试
- 权限：`aps.ods.batch.retry` / `aps.ods.batch.force.complete`
- 建议需要审批
- 审计：必须记录 `BatchNo`

---

# 12. 实施顺序建议

## 阶段一（必须）
- 用户、角色、权限码
- 接口层鉴权
- 工厂/产品族数据范围
- 审计日志

## 阶段二（建议）
- 审批矩阵
- 冻结区/插单/版本发布审批
- ODS 批次审批

## 阶段三（可扩展）
- 资源组级数据范围
- 更细粒度例外规则
- 外部 OA 集成审批

---

# 13. 最终建议

对你们当前 APS 项目，最适合的权限体系不是“大而全的权限平台”，而是：

> **RBAC + DataScope + Approval + Audit**

这套模型已经足够覆盖：

- 单体架构实现方式
- APS 高风险动作控制
- 工厂/产品族范围隔离
- 冻结区 / 已开工 / 插单 / 发布 / 回滚 等关键场景
- 事后追溯与责任闭环

---

**文档结束**
