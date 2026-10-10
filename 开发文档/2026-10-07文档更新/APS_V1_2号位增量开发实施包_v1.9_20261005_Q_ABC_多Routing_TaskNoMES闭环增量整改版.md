# APS V1_2号位增量开发实施包 v1.9

**当前版本**：v1.9  
**当前版本日期**：2026-10-05（北京时间）  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》+《APS Pegging供需承接与分层计算业务说明 v1.6》+《APS有限产能排产与滚动90天计划业务说明 v1.7》  
**当前技术对齐基线**：《APS集成接口设计 v1.34》+《APS数据库字段说明文档 v5.1.9》+《DDL v5.1.8.3》  
**当前状态**：正式变更态；待指导位Step 9复审，尚未冻结生效。


# 2026-10-05 本轮增量整改回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。

本版继续保护现有SchedulingOrchestrator、Demand/Supply、Pegging、Allocation、PlanVersion、ScheduleRun、Solver入口和事务框架；按增量整改实施，不按新项目重写。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 2号位本轮主链

1. 完成当前LogicalProductionDemand Q数量真相后，再消费5号位ExistingExecution事实识别A/B/C。
2. 一个ScheduleRun内按MESWorkOrderNo生成一个ContinuationKey；同WO多Slice共享Key，不拼接LogicalDemandKey。
3. 对每个Continuation Slice关联5号位Operation级LastReportResourceCode并校验当前Operation Eligibility；合法可靠时赋`PreferredResourceCode`，否则NULL。
4. A：继承旧TaskNo、原MES工单、真实Route/Path；B：Solver前TaskNo=NULL，FinalTask后生成一个新TaskNo并绑定原MES工单；C：Solver后按ExecutionBatchDraftKey生成TaskNo并创建新MES工单。
5. A/B向1号位发送固定Route/Path；C发送全部合法`RoutingCandidates[]`；禁止预先压成DEFAULT/1。
6. FinalTask持久化Operation级Task，保存真实ProductionDepartment/Stage/Route/Path/Operation，并建立TaskNo↔MESWorkOrder可追溯关系。
7. Material进入Pegging前完成标准化：出口库/最终可销售层级使用既有裁决形成`FINAL_ + 原标准MaterialCode`，普通物料保持原MaterialCode；FINAL不是MaterialType。2号位只消费5号位/防腐层已标准化Material身份。Routing Operation / Dependency / Eligibility装载必须以ODS显式携带的**标准化MaterialCode**进入`MaterialMapping → MaterialId`链；`MES_ID / Model`仅保留源追溯与一致性校验，不得再作为无法区分普通/FINAL层级的唯一Material绑定依据。MaterialMapping继续保留SourceID+Warehouse/Location源追溯；不得从Routing反推FINAL，也不得把一般Warehouse差异全部拆成新Material。
8. 2号位拥有APS运行结果数据库写入；不接管5号位ODS事实、不接管3号位Auth/规则治理、不让1号位直接碰数据库。


## Candidate、接口收口、运行服务联合冻结版

版本：v1.9\
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
-   FinalTask结果校验与持久化；
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