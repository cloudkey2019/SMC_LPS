# APS V1 Auth数据库DDL质疑单正式裁决回复

**日期：2026-09-09（北京时间）**
**对象：3号位（Auth / RBAC / Scope治理）**
**依据：当前APS V1最新冻结权限设计、Auth字段基线及既有Auth DDL收口裁决**
**性质：冻结基线下的结构收口裁决，不重新设计权限系统**

---

# 一、最终裁决

| 编号  | 3号位请求                                                                           | 正式裁决                                                   |
| --- | ------------------------------------------------------------------------------- | ------------------------------------------------------ |
| D-1 | `RolePermission` / `RoleDataScope`增加`AssignedBy`                                | **不批准**                                                |
| D-2 | `Role` / `Permission` / `DataScopePolicy`统一增加`IsDeleted`，可再加DeletedAt/DeletedBy | **原方案不批准；仅批准DataScopePolicy补启停字段`IsEnabled`或当前代码等价字段** |
| D-3 | 关联表继续联合主键，删除代码残留代理`Id`                                                          | **批准**                                                 |

简化成一句话：

> **V1保留“操作级审计”，不扩张成“授权关系历史平台”；主数据采用各自最小生命周期状态，不统一建设软删除体系；四张关联表继续使用自然联合主键。**

---

# 二、D-1：不批准 RolePermission / RoleDataScope 增加 AssignedBy

3号位指出当前四张关联表存在结构不对称，这个事实成立：`UserRole`、`UserDataScope`有`AssignedBy`，而`RolePermission`、`RoleDataScope`没有。

但是：

> **字段不对称本身，不等于业务缺口。**

当前冻结权限设计对追溯的正式要求是：

> “谁在什么时候，对什么对象，做了什么，结果如何？”

并明确要求用户、角色、权限、Scope变更等关键动作进入审计（Audit）。

当前字段冻结中，`RolePermission`正式字段就是：

```text
RoleId
PermissionId
AssignedAt
```

`RoleDataScope`正式字段也是：

```text
RoleId
ScopePolicyId
AssignedAt
```

没有冻结`AssignedBy`。

## 2.1 为什么不增加

### 第一，真正的“谁操作的”权威是AuditLog，不是关联行

角色增加权限、撤销权限、调整Scope，本质上是一个**业务动作**。

正式追溯应来自：

```text
Actor
+ Action
+ Object
+ Result
+ OccurredAt
```

即审计事实。

`AssignedBy`只能回答：

> 当前这条仍存在的关联最初/最近是谁写进去的。

它不能回答：

* 谁撤销过；
* 谁重新分配过；
* 中间曾经是什么；
* 为什么改变；
* 操作成功还是失败。

因此它不能替代AuditLog。

### 第二，增加AssignedBy也解决不了3号位所担心的“历史丢失”

因为关联被DELETE以后：

```text
AssignedBy
```

也随这一行一起消失。

所以如果目的真是回答：

> “这个角色过去曾经被谁授予过什么权限？”

那么真正需要建设的是：

* 授权历史表；
* RevokedAt / RevokedBy；
* 事件溯源；

这已经明显超过当前V1冻结范围。

当前V1明确不建设通用事件溯源、复杂ACL/权限平台。

### 第三，不能以“防AuditLog丢失”为理由复制审计真相

如果审计日志可能被静默漏写或丢失，应修正：

> **Audit写入可靠性**

而不是在不同业务表里复制一部分审计字段形成多个半真相。

---

## 2.2 D-1最终实施要求

因此正式裁决：

### DDL

不增加：

```text
RolePermission.AssignedBy
RoleDataScope.AssignedBy
```

### Entity

如果当前代码中：

```csharp
RolePermission.AssignedBy
```

只是未被业务使用的残留字段：

> **删除，与冻结DDL对齐。**

### Service

以下动作必须继续写正式审计：

* AssignPermissions；
* Revoke / ReplacePermissions；
* AssignRoleScope；
* Revoke / ReplaceRoleScope。

**审计不得因为关联表没有AssignedBy而取消。**

---

# 三、D-2：不批准统一 IsDeleted；但 DataScopePolicy 生命周期需要最小补齐

3号位提出的问题中，这一项确实存在一个真实缺口，但解决方法不是给所有表统一增加：

```text
IsDeleted
DeletedAt
DeletedBy
```

当前冻结设计的目标是：

> Authentication + RBAC + Business Scope + Audit

而不是建设统一主数据生命周期框架。

因此要分对象处理。

---

# 四、Role：继续使用 IsActive，不增加 IsDeleted

角色（Role）的业务语义是：

> 角色模板当前是否还允许继续被使用。

所以：

```text
IsActive
```

已经足以表达：

* 启用；
* 停用；
* 不再参与新的权限计算。

没有必要再叠加：

```text
IsDeleted
```

形成：

```text
IsActive = 0
IsDeleted = 0/1
```

两套容易产生歧义的生命周期。

例如未来马上会出现问题：

> `IsActive=1 AND IsDeleted=1`到底算什么？

V1没有必要制造这个状态组合。

正式裁决：

> **Role继续采用IsActive生命周期，不增加IsDeleted / DeletedAt / DeletedBy。**

---

# 五、Permission：继续使用 IsActive，不增加 IsDeleted

Permission属于系统功能权限定义。

它的主要业务行为应该是：

```text
启用
↓
停用/退出使用
```

不是普通业务数据“删除”。

因此同样：

> **继续使用IsActive，不增加IsDeleted。**

历史兼容Permission即使退出当前主链，也可以：

```text
IsActive = 0
```

保留历史数据和审计解释，而不需要再创造Delete状态。

---

# 六、DataScopePolicy：批准补“启停”，但不是IsDeleted

这里3号位指出的是一个真实的不一致：

当前`DataScopePolicy`如果完全没有生命周期状态，而Service直接：

```text
DELETE DataScopePolicy
```

同时清理：

```text
UserDataScope
RoleDataScope
```

确实容易造成业务范围配置被一次物理删除整体清空。

而更早已经冻结的权限设计其实已经给出了DataScopePolicy建议结构：

```text
Id
ScopeType
ScopeValue
Description
IsEnabled
CreatedAt
UpdatedAt
```

因此这里不需要发明新的软删除模型。

正确收口是：

> **给DataScopePolicy补最小启停生命周期。**

建议正式字段：

```text
IsEnabled BIT NOT NULL DEFAULT 1
```

如果当前v1.2代码已经存在语义完全相同的：

```text
IsActive
Status
```

则直接复用现有字段，不要求为了名称一致再增加第二列。

### 正式业务删除行为

后台“删除Scope”操作在正式业务路径中应理解为：

> **停用该Scope Policy。**

而不是：

```text
删除Scope
→ 删除所有UserDataScope
→ 删除所有RoleDataScope
→ 物理删除Policy
```

这样既保持当前权限模型简单，也避免误删造成范围定义和关联整体消失。

---

# 七、User：保持当前已经存在的生命周期，不借本单重构

质疑单说明当前v1.2的`User`已经存在`IsDeleted`。

这一项本单不重新打开。

因此：

> **User现有IsDeleted保持。**

不因为Role/Permission没有IsDeleted，就强制所有主数据做成相同模型。

“字段整齐”不是业务目标。

---

# 八、关联表：不增加软删除，不增加RevokedAt

这一点同意3号位质疑单自己的克制方案。

以下四张关系表：

```text
UserRole
RolePermission
UserDataScope
RoleDataScope
```

继续表达：

> **当前有效关系。**

不建设：

```text
IsDeleted
IsActive
RevokedAt
RevokedBy
HistoryVersion
```

等关系历史模型。

关系发生变化：

```text
旧关系DELETE
新关系INSERT
```

当前V1可以接受。

历史追溯依靠正式Audit。

这与当前项目明确禁止“通用事件溯源”等过度设计方向一致。

---

# 九、D-3：批准联合主键，删除实体残留Id

这一项3号位判断正确。

正式保持：

```text
UserRole
PK = (UserId, RoleId)

RolePermission
PK = (RoleId, PermissionId)

UserDataScope
PK = (UserId, ScopePolicyId)

RoleDataScope
PK = (RoleId, ScopePolicyId)
```

不增加代理：

```text
Id IDENTITY
```

原因很明确：

1. 关联关系本身天然唯一；
2. 所有查询都通过两侧FK；
3. 没有其它对象引用关联表自身Id；
4. 增加代理Id后仍需要联合UNIQUE约束；
5. 会形成无实际业务价值的第二套键。

因此D-3批准。

---

# 十、代码收口要求

3号位按以下方式整改即可：

### UserRole

删除实体中残留：

```csharp
public int Id { get; set; }
```

保留：

```text
UserId
RoleId
AssignedAt
AssignedBy
```

### RolePermission

删除：

```csharp
public int Id { get; set; }
```

如果存在未实际使用的：

```csharp
AssignedBy
```

一并删除。

最终保持：

```text
RoleId
PermissionId
AssignedAt
```

### UserDataScope

保持现状：

```text
UserId
ScopePolicyId
AssignedAt
AssignedBy
```

### RoleDataScope

保持：

```text
RoleId
ScopePolicyId
AssignedAt
```

### DataScopePolicy

若当前没有等价启停字段，补：

```text
IsEnabled
```

并将正式删除动作收敛为停用。

---

# 十一、关于Audit的额外红线

这次不增加`AssignedBy`，并不意味着降低审计要求。

相反：

> **权限与Scope变更的Actor追溯必须由统一Audit保证。**

当前冻结权限设计已经明确要求至少审计：

* 用户维护；
* 角色维护；
* 权限维护；
* Scope维护；
* 权限/Scope分配相关关键动作。

所以后续代码审核应重点检查：

> 修改成功了，但Audit没有记录

这种情况应视为真实权限闭环缺口。

不能用“关联表没有AssignedBy”解释为允许不审计。

---

# 十二、最终回执表

| 项目                            | 裁决                         |
| ----------------------------- | -------------------------- |
| D-1 RolePermission补AssignedBy | **不批准**                    |
| D-1 RoleDataScope补AssignedBy  | **不批准**                    |
| RolePermission实体残留AssignedBy  | **删除**                     |
| D-2 Role补IsDeleted            | **不批准，继续IsActive**         |
| D-2 Permission补IsDeleted      | **不批准，继续IsActive**         |
| D-2 DataScopePolicy补IsDeleted | **不批准**                    |
| DataScopePolicy生命周期           | **批准补IsEnabled或复用现有等价字段**  |
| DeletedAt / DeletedBy         | **V1不增加**                  |
| 关联表软删除/RevokedAt              | **V1不增加**                  |
| D-3联合主键                       | **批准维持**                   |
| UserRole / RolePermission残留Id | **删除代码字段，不改DDL**           |
| 权限历史追溯                        | **统一AuditLog负责，不建设授权历史平台** |

---

# 十三、最终裁决结论

> **3号位此次质疑中，D-3完全成立；D-2发现了DataScopePolicy生命周期的真实小缺口，但不能扩大成统一软删除平台；D-1属于审计增强设想，不是当前冻结业务缺口。**

最终结构继续坚持：

> **当前关系表保存“现在有什么权限”；Audit保存“谁做过什么变更”；Role/Permission/Scope使用最小启停生命周期。**

这样既满足APS V1认证、RBAC、业务范围权限和审计闭环，也不把权限模块扩张成V2式历史授权/事件溯源平台。
