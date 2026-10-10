# APS V1_2号位增量开发实施包 v1.8

## Candidate、接口收口、运行服务联合冻结版

版本：v1.8\
适用对象：2号位及开发AI

------------------------------------------------------------------------

# 一、文档定位

本版本基于9.15冻结实施包，在保持2号位核心运行Owner不变的基础上，同步：

-   Candidate运行输入调整；
-   接口职责收口；
-   Query/Application Service边界；
-   Auth职责退出；
-   3/5号位协同接口。

本版本不是重构方案。

------------------------------------------------------------------------

# 二、2号位最终职责

2号位继续负责：

-   SchedulingOrchestrator；
-   Demand构建；
-   Supply构建；
-   Pegging；
-   Allocation；
-   Demand Protection实际数量闭环；
-   PI Position消费；
-   Cross Factory业务主链消费；
-   Planning-only Purchase Placeholder；
-   LogicalProductionDemand；
-   Continuation/Free Slice处理；
-   调用有限产能Solver；
-   FinalTask生成与持久化；
-   ScheduleRun/PlanVersion运行结果。

------------------------------------------------------------------------

# 三、2号位保持保护区

继续保留：

-   SchedulingOrchestrator；
-   IFiniteCapacityScheduler调用链；
-   Pegging Ledger；
-   AllocationTaskShare；
-   DomainSolveRequest；
-   FinalTask；
-   ScheduleRun；
-   PlanVersion。

禁止因接口调整重新设计核心运行模型。

------------------------------------------------------------------------

# 四、Candidate输入v2支持

2号位消费3号位治理后的Candidate输入。

支持：

-   BusinessTriggerType；
-   PriorityMode；
-   OrderTargets；
-   TaskTargets；
-   ChangedResourceIds；
-   BasePlanVersionId。

------------------------------------------------------------------------

# 五、Candidate运行边界

## 5.1 2号位负责

-   执行Candidate；
-   生成计划结果；
-   保存Base关系；
-   保存运行结果。

------------------------------------------------------------------------

## 5.2 2号位不负责

-   Candidate权限；
-   Candidate激活治理；
-   Scope合法性审批；
-   Base有效性治理。

以上由3号位负责。

------------------------------------------------------------------------

# 六、Seed与Scope处理

2号位将业务输入转换为求解Seed：

包括：

-   Demand Seed；
-   Task Seed；
-   Resource Seed。

Seed用于确定影响传播范围。

不是事实过滤条件。

Scope外资源和任务：

-   不移动；
-   仍作为真实约束进入求解。

------------------------------------------------------------------------

# 七、PriorityMode处理

支持：

## NORMAL

保持正式Demand优先规则。

禁止：

通过修改DueDate改变排序真相。

------------------------------------------------------------------------

## EXPEDITE

仅提升指定对象及派生链。

不得突破：

-   Demand Protection；
-   Strict Binding；
-   Firm/Frozen；
-   Material AvailableTime。

------------------------------------------------------------------------

# 八、接口责任调整

## 8.1 退出Auth职责

2号位不再维护：

-   Authentication；
-   User；
-   Role；
-   Permission；
-   Business Scope；
-   APS_Auth Migration。

------------------------------------------------------------------------

## 8.2 规则参数职责

2号位：

负责读取：

-   Frozen Rule Snapshot；
-   Frozen Parameter Snapshot。

不负责：

-   DDL；
-   Migration；
-   治理发布。

------------------------------------------------------------------------

# 九、Query/Application Service

## 9.1 Query Service

为5号位提供真实运行查询：

包括：

-   Overview；
-   Order Plan；
-   Gantt；
-   Explanation；
-   Supply Trace；
-   Pegging Trace；
-   Demand Protection Query。

------------------------------------------------------------------------

## 9.2 Application Service

提供：

-   Demand Protection Release；
-   MES受控操作。

最终业务校验仍由2号位执行。

------------------------------------------------------------------------

# 十、与5号位接口边界

5号位提供：

-   PI Position；
-   NextOperationContext；
-   MES复杂事实；
-   Manual ETA；
-   ODS事实。

2号位：

-   消费；
-   计算运行结果；
-   不反向重算事实。

------------------------------------------------------------------------

# 十一、与3号位接口边界

3号位提供：

-   Rule Snapshot；
-   Parameter Snapshot；
-   Strategy Snapshot；
-   Candidate治理上下文；
-   权限上下文。

2号位提供：

-   Run执行状态；
-   Candidate执行结果；
-   运行事实。

------------------------------------------------------------------------

# 十二、禁止事项

禁止：

-   5号位复制Pegging；
-   3号位复制排程主流程；
-   4号位直接调用2号位HTTP；
-   新建第二套Supply；
-   新建第二套Pegging；
-   新建CandidateInput物理平台。

------------------------------------------------------------------------

# 十三、验收重点

必须验证：

1.  Candidate可以基于ACTIVE Base执行；
2.  多对象Seed输入正确；
3.  Scope外事实仍参与约束；
4.  Pegging数量闭环不受影响；
5.  Solver接口不漂移；
6.  FinalTask结果正确保存；
7.  Auth退出后运行正常；
8.  5号位Query链路正常。

------------------------------------------------------------------------

# 十四、最终冻结原则

2号位定位：

> APS V1运行主流程Owner，负责数量真相、Pegging真相和排程结果闭环。

2号位不负责：

-   权限治理；
-   规则治理；
-   复杂事实解释。

本版本作为2号位后续开发和代码审核依据。
