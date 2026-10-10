# APS V1_1号位有限产能排程开发实施包 v1.6

**当前版本**：v1.6  
**当前版本日期**：2026-10-05（北京时间）  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》+《APS有限产能排产与滚动90天计划业务说明 v1.7》  
**当前技术对齐基线**：《APS集成接口设计 v1.34》+《APS数据库字段说明文档 v5.1.9》  
**当前状态**：正式变更态；待指导位Step 9复审，尚未冻结生效。


# 2026-10-05 本轮实施边界回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 1号位新增/替换实施要求

- **数据库红线保持**：1号位不读写APS数据库，不直读MES；只消费2号位装载的DomainSolveRequest。
- A/B输入必须带真实固定`RouteCode / PathId / StartOperation`与完整指定Path DAG，并按`NoSplitMerge`处理；不得切换Routing。
- `PreferredResourceCode?`属于V1正式输入；非空时必须作为资源连续性软偏好进入目标函数/排序，不得Hard Lock。
- C桶输入保留`RoutingCandidates[]`，每个候选包含RouteCode、PathId、Operations、Dependencies、Eligibility；不得在进入Solver前归一化成`DEFAULT / 1`。
- C桶Execution Batch形成与Routing选择、Direction、Resource排程联合求解；每个Execution Batch只允许一条完整Path。
- `ExecutionBatchDraftKey`用于把多Operation FinalTask归为同一执行批；FinalTask必须回传真实RouteCode/PathId并原样回传ContinuationKey。
- PathId必须进入Graph/Dependency/Eligibility节点和匹配键，禁止跨Path连边或Eligibility混用。


## Setup换型规则、无设备小工序与人工有限产能、用户输出联合冻结版

版本：v1.6\
适用对象：1号位及开发AI

------------------------------------------------------------------------

# 一、文档定位

本版本基于9.15冻结实施包，在不改变APS
V1核心业务模型的前提下，同步本轮冻结调整。

本实施包用于指导1号位有限产能排程模块开发、整改和验收。

------------------------------------------------------------------------

# 二、1号位最终职责

1号位负责：

-   有限产能Solver；
-   Resource能力计算；
-   排程优化；
-   Task时间计算；
-   Setup换型计算；
-   资源冲突处理；
-   排程解释结果生成。

1号位不负责：

-   Pegging；
-   Allocation；
-   Supply构建；
-   PI Position计算；
-   Auth/RBAC；
-   Candidate治理。

------------------------------------------------------------------------

# 三、Operation Planning Mode

## 3.1 业务定义

工序是否进入有限能力求解，不再根据是否存在设备判断。

由：

Operation Planning Mode

决定。

------------------------------------------------------------------------

## 3.2 模式

  模式              处理
  ----------------- ----------------------------
  FINITE_RESOURCE   进入有限资源求解
  UNCONSTRAINED     保留工艺时间，不占有限资源
  WAIT_ONLY         形成等待约束

------------------------------------------------------------------------

# 四、人工有限产能支持

人工与设备统一作为Resource。

模型：

Resource

-   

ResourceCalendar

-   

OperationResourceEligibility

------------------------------------------------------------------------

## 4.1 支持内容

Solver需要支持：

-   人工Resource能力；
-   人工可用时间窗口；
-   人工资源冲突；
-   人工Slot占用。

------------------------------------------------------------------------

## 4.2 禁止设计

禁止：

-   HumanSolver；
-   Worker排产模型；
-   独立人工能力平台。

------------------------------------------------------------------------

# 五、Setup换型规则

## 5.1 定位

Setup属于：

Resource Timeline约束。

影响：

-   Task开始时间；
-   Task结束时间；
-   Resource占用；
-   交期风险解释。

------------------------------------------------------------------------

## 5.2 Solver输入

支持：

-   当前任务物料；
-   下一任务物料；
-   Resource；
-   Setup时间规则。

------------------------------------------------------------------------

## 5.3 禁止

不建设：

-   SetupTask；
-   SetupRuntime；
-   SetupSupply。

------------------------------------------------------------------------

# 六、1号位Solver接口

保持：

2号位：

DomainSolveRequest

↓

IFiniteCapacityScheduler.SolveAsync

↓

1号位Solver

------------------------------------------------------------------------

1号位只消费冻结输入，不重新计算：

-   Pegging；
-   Allocation；
-   PI Position。

------------------------------------------------------------------------

# 七、跨版本连续性输入

1号位消费2号位提供的：

-   LogicalProductionDemand；
-   Continuation Slice；
-   Free Slice；
-   StartOperation上下文。

1号位不直接解释MES原始报工。

------------------------------------------------------------------------

# 八、用户输出与解释

1号位负责产生：

-   延期原因；
-   Resource瓶颈原因；
-   Setup影响原因；
-   Unscheduled原因。

责任链：

1号位产生

↓

2号位保存

↓

5号位查询展示

------------------------------------------------------------------------

# 九、代码实施要求

保留：

-   IFiniteCapacityScheduler；
-   Solver外壳；
-   DomainSolveRequest；
-   DomainSolveResult。

禁止：

-   绕过2号位直接读取运行事实；
-   新建Supply/Pegging模型；
-   新建权限模型；
-   新建Candidate运行模型。

------------------------------------------------------------------------

# 十、验收重点

必须验证：

1.  Operation Planning Mode生效；
2.  无设备工序可正确进入有限能力逻辑；
3.  人工Resource可参与排程；
4.  Setup时间影响甘特和交期；
5.  Solver结果可被2号位正确消费；
6.  延期原因可追溯。

------------------------------------------------------------------------

# 十一、最终冻结原则

1号位是APS V1有限产能算法Owner。

但：

-   不拥有业务数量真相；
-   不拥有Pegging真相；
-   不拥有权限真相；
-   不拥有复杂事实真相。

只负责：

> 在2号位提供的业务约束和输入基础上，计算可执行生产计划。