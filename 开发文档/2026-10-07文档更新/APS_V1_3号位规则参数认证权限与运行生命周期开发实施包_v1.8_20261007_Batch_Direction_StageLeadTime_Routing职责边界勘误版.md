# APS V1_3号位规则参数认证权限与运行生命周期开发实施包 v1.8

**当前版本**：v1.8  
**当前版本日期**：2026-10-07（北京时间）  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》  
**当前技术对齐基线**：《APS集成接口设计 v1.34》+《APS数据架构与防腐层设计方案 v1.45》  
**当前状态**：冻结后正式文档一致性最小勘误提交态；仅纠正3号位Routing职责越界，待指导位定向审核。审核确认前，2026-10-05已生效基线中的3号位v1.7仍为当前正式版本。


# 2026-10-07 冻结后Routing职责边界最小勘误

**勘误性质**：冻结后正式文档一致性最小勘误，不重新打开多Routing业务裁决。

**唯一勘误点**：删除原“3号位负责RouteCode/PathId启停、优先级/偏好等静态Routing治理”的越界表述，收敛为既有正式运行链职责边界。

**保持不变**：A/B固定真实RouteCode+PathId；C桶由2号位装载合法`RoutingCandidates[]`，1号位按已冻结多Routing规则为Execution Batch选择完整合法Path；不新增Routing治理/选择平台，不新增Routing字段、表、接口或Path=1优选规则。



# 2026-10-05 本轮治理参数同步

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。

本版只补充治理参数能力，不改变3号位Auth/RBAC/Candidate/Run治理主体，不把3号位扩成Solver、Batch执行器或FINAL判断服务。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 本轮新增治理项

- Batch Policy：以`Material + ProductionDepartment`为主要业务粒度，治理Min/Max、Preferred、AllowSplit/AllowMerge、优化拆分预算等参数版本。
- Direction策略：治理AUTO/FORWARD/BACKWARD/MIXED选择策略，不按OrderType硬编码。
- StageLeadTimeParam：V1正常匹配层级为`DEPT_EXACT → FACTORY_STAGE_DEFAULT → GLOBAL_STAGE_DEFAULT`；旧Material/ProductFamily层仅历史兼容。
- Routing职责边界：3号位不拥有Routing事实，不负责RouteCode/PathId的启用/停用、路径优先级或路径偏好治理，不建设独立Routing治理/选择平台，也不执行动态路径求解。合法Routing候选以当前正式ODS/Routing事实契约为准，由2号位按正式运行链装载并向1号位提供RoutingCandidates[]，由1号位按已冻结多Routing规则完成动态路径选择。
- FINAL Material身份由已裁决的标准化链确定；3号位不新增FINAL规则判断引擎。
- 3号位只动自身治理/Auth数据库，不写2号位Task/PlanVersion/ScheduleRun结果真相。


## Candidate治理、Auth/RBAC、规则参数联合冻结版

版本：v1.8\
适用对象：3号位及开发AI

------------------------------------------------------------------------

# 一、文档定位

本版本基于9.15冻结实施包，在不改变APS
V1核心运行模型的前提下，同步本轮职责、接口和治理边界调整。

本实施包用于指导3号位：

-   权限认证治理；
-   规则参数治理；
-   Candidate治理；
-   Run生命周期治理。

------------------------------------------------------------------------

# 二、3号位最终职责

3号位负责：

-   Authentication；
-   RBAC；
-   Business Scope；
-   Rule/Parameter/Strategy治理；
-   Candidate治理；
-   Run治理；
-   Frozen Snapshot生命周期管理。

3号位不负责：

-   Pegging；
-   Allocation；
-   Supply计算；
-   Solver；
-   FinalTask生成。

------------------------------------------------------------------------

# 三、Auth与RBAC治理

## 3.1 正式Owner

APS_Auth统一归3号位管理。

包括：

-   User；
-   Role；
-   Permission；
-   UserRole；
-   RolePermission；
-   Business Scope；
-   Auth Audit；
-   Token生命周期。

------------------------------------------------------------------------

## 3.2 迁移原则

采用：

> 迁移，不重写。

保留：

-   数据连续性；
-   用户角色连续性；
-   权限关系连续性。

------------------------------------------------------------------------

## 3.3 2号位退出

2号位不再维护：

-   Auth Controller；
-   Auth Entity；
-   Auth Migration；
-   Permission Seed。

------------------------------------------------------------------------

# 四、Business Scope治理

3号位负责：

-   Factory；
-   Product Family；
-   Department；
-   Domain。

Business Scope用于：

-   用户可见范围；
-   Candidate范围校验；
-   Run治理。

------------------------------------------------------------------------

## 4.1 边界

Business Scope：

不是Solver约束。

不是：

-   Demand过滤器；
-   Supply删除条件。

------------------------------------------------------------------------

# 五、Rule / Parameter / Strategy治理

## 5.1 3号位负责

包括：

-   RuleSet；
-   RuleSetVersion；
-   ParameterSet；
-   ParameterSetVersion；
-   StrategyProfile；
-   Strategy Snapshot。

------------------------------------------------------------------------

## 5.2 冻结机制

运行前：

    治理配置
        ↓
    冻结版本
        ↓
    2号位Run加载
        ↓
    Solver执行

------------------------------------------------------------------------

## 5.3 2号位职责

2号位：

-   读取；
-   装载；
-   历史重放。

不负责：

-   发布；
-   修改；
-   DDL。

------------------------------------------------------------------------

# 六、Candidate治理

## 6.1 3号位负责

Candidate生命周期：

-   创建；
-   校验；
-   Base检查；
-   激活；
-   失效管理。

------------------------------------------------------------------------

## 6.2 Candidate Input v2治理

管理：

-   SchemaVersion；
-   BusinessTriggerType；
-   PriorityMode；
-   OrderTargets；
-   TaskTargets；
-   ChangedResourceIds；
-   BasePlanVersionId。

------------------------------------------------------------------------

## 6.3 ACTIVE Base校验

Candidate必须绑定：

当前有效ACTIVE Base。

禁止：

-   CTP直接激活；
-   旧Base覆盖新计划。

------------------------------------------------------------------------

# 七、Run生命周期治理

3号位负责治理：

-   Run创建规则；
-   Run类型合法性；
-   Candidate激活规则；
-   Domain范围。

------------------------------------------------------------------------

## 7.1 保持边界

2号位负责：

-   实际执行；
-   运行结果；
-   计划落库。

3号位负责：

-   是否允许执行。

------------------------------------------------------------------------

# 八、与其他号位接口

## 与2号位

提供：

-   Frozen Rule Snapshot；
-   Parameter Snapshot；
-   Strategy Snapshot；
-   Candidate治理上下文。

消费：

-   Run状态；
-   Candidate执行结果。

------------------------------------------------------------------------

## 与4号位

提供：

-   权限判断；
-   Candidate操作入口；
-   治理页面能力。

------------------------------------------------------------------------

## 与5号位

提供：

-   权限上下文；
-   Scope上下文。

------------------------------------------------------------------------

# 九、禁止事项

禁止：

-   3号位计算Pegging；
-   3号位生成Task；
-   3号位参与Solver；
-   3号位复制运行主流程；
-   3号位建设第二套计划结果。

------------------------------------------------------------------------

# 十、验收重点

必须验证：

1.  Login/Refresh/Permission正常；
2.  原用户角色连续可用；
3.  Rule版本冻结可追溯；
4.  Parameter Snapshot可装载；
5.  Candidate Base校验有效；
6.  权限通过但业务资格不足时仍由2号位拒绝；
7.  3号位不产生排程结果。

------------------------------------------------------------------------

# 十一、最终冻结原则

3号位定位：

> APS V1治理中心，负责规则、权限、范围和生命周期治理。

3号位不拥有：

-   数量真相；
-   Pegging真相；
-   排程结果真相。

本版本作为3号位后续开发和代码审核依据。