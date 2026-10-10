# APS核心排产全流程走查 V3.25

**当前版本**：V3.25  
**当前版本日期**：2026-10-05（北京时间）  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》  
**当前技术对齐基线**：《APS数据架构与防腐层设计方案 v1.45》+《APS集成接口设计 v1.34》+《APS数据库字段说明文档 v5.1.9》  
**当前状态**：正式变更态；待指导位Step 9复审，尚未冻结生效。


# 2026-10-05 本轮正式变更回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 端到端唯一读取顺序

```text
Material标准化
→ Pegging/Q
→ MES执行事实
→ 2号位A/B/C识别
→ A/B固定真实Path / C多Routing候选
→ 1号位同一Solver：Batch + Direction + Routing + Resource
→ FinalTask
→ 2号位Task.Id持久化 + TaskNo/MES身份闭环
→ A/B继续原MES工单；C创建新MES工单
→ MES执行反馈
→ 下一Run回接真实Route/Path/StartOperation
```

## 模拟走查场景（本轮新增验收用）

- **A桶**：上一ACTIVE已由APS下发WO-A/TASK-A，当前WO-A有两个未完成Slice；2号位形成一个ContinuationKey，两个Slice共享该Key并固定历史真实Route/Path；1号位不重新拆合批、不切路径，非空PreferredResource仅作软偏好；FinalTask沿用TASK-A和WO-A。
- **B桶**：MES存在外部WO-B但APS无旧TaskNo；5号位唯一回接真实Route/Path并输出多Slice事实；2号位Solver前TaskNo为空、同WO一个ContinuationKey；FinalTask后生成一个新TaskNo绑定WO-B，不创建第二WO。
- **C桶**：Q中自由份额无MES执行身份；2号位提供全部合法RoutingCandidates；1号位形成Execution Batch并选择真实Route/Path；2号位持久化Operation级Task、生成TaskNo并创建新MES工单，保存Route/Path/Operation回接证据。
- **FINAL**：源系统/ODS对出口库或最终可销售层级先生成`FINAL_ + 原标准MaterialCode`（如`FINAL_WIP_xxx`），MaterialType解析先Strip `FINAL_`；Inventory/PI/Order/Demand/Supply/Routing随后统一消费该FINAL MaterialId。5号位不得通过Routing或包装工序反推FINAL，且不新增FINAL BOM层。

### Step 9定向模拟场景（P1-12）

#### 场景1：两个C Execution Batch选择不同Routing
**输入事实**：同一标准化Material存在`Route A / Path1`与`Route B / Path2`两条合法候选；自由份额形成EB001、EB002。  
**责任位**：2号位装载完整`RoutingCandidates[]`；1号位进行Batch/Direction/Routing/Resource联合求解。  
**中间身份/上下文**：EB001、EB002各自形成独立`ExecutionBatchDraftKey`，每批只绑定一条真实Route/Path。  
**Solver行为**：允许EB001→Route A/Path1、EB002→Route B/Path2；每个Execution Batch内部Operations/Dependencies/Eligibility必须来自同一路径。  
**持久化/输出**：FinalTask逐Operation返回各自真实RouteCode/PathId；2号位按批生成TaskNo并持久化对应路径。  
**禁止行为**：不得把两个Path的Operation拼成一个Execution Batch；不得回写`DEFAULT/1`。

#### 场景2：同OperationCode跨Path隔离
**输入事实**：Path1与Path2均存在`OperationCode=OP-010`，但Dependency、Eligibility或CapacityFactor不同。  
**责任位**：2号位按`MaterialId + ProductionDepartment + RouteCode + PathId`装载路径上下文；1号位按候选路径隔离求解。  
**中间身份/上下文**：`OP-010`必须始终携带所属RouteCode/PathId。  
**Solver行为**：Dependency、Eligibility、CapacityFactor只在同一Route/Path内关联。  
**持久化/输出**：FinalTask/Task保存真实RouteCode/PathId/OperationCode。  
**禁止行为**：不得仅按OperationCode跨Path JOIN；不得把Path1的Eligibility用于Path2。

#### 场景3：普通WIP与FINAL_WIP同源型号
**输入事实**：同一源型号同时存在普通生产/半成品状态与出口库/最终可销售状态。  
**责任位**：5号位在ODS/Material标准化层输出`WIP_A001`与`FINAL_WIP_A001`；2号位经MaterialMapping获得不同MaterialId。  
**中间身份/上下文**：两者MaterialType解析均为WIP/SEMI_FINISHED，但MaterialCode/MaterialId不同；Routing三件套携带对应标准化MaterialCode。  
**Solver行为**：分别消费各自MaterialId对应的Routing，不做FINAL推断。  
**持久化/输出**：Inventory/PI/Order/Demand/Supply/Routing/Task全链使用各自标准MaterialId。  
**禁止行为**：不得因同源型号合并普通/FINAL身份；不得新增IsFinal/FinalFlag等第二真相。

#### 场景4：FINAL无虚构BOM
**输入事实**：源BOM没有`普通Material → FINAL_Material`生产关系。  
**责任位**：5号位只同步真实源BOM；2号位消费标准化后的Material身份。  
**中间身份/上下文**：FINAL仅是Material身份差异，不自动形成新BOM层。  
**Solver行为**：按真实BOM与Routing求解；如果源系统确有真实FINAL相关BOM边，则按真实事实正常同步。  
**持久化/输出**：BOM Workset只保存真实源BOM派生结果。  
**禁止行为**：不得自动生成`FINAL_xxx → xxx`或`xxx → FINAL_xxx`虚构BOM边。

#### 场景5：人工MES工单无法唯一匹配Routing
**输入事实**：外部人工MES工单已有执行事实，但按生产部门、Stage、工序顺序等证据仍对应多条Routing，无法唯一定位。  
**责任位**：5号位登记Routing回接Issue并输出可确认事实；2号位按冻结降级链消费。  
**中间身份/上下文**：不得伪造RouteCode/PathId/OperationCode；无法形成可靠Continuation Routing身份时保留Issue。  
**Solver行为**：按Stage/Dept与已发布StageLeadTimeParam形成StageTiming降级约束，不用猜测路径进入Operation级连续性。  
**持久化/输出**：记录Issue/Explanation；StageTiming按既有链路保存/输出。  
**禁止行为**：不得猜`DEFAULT/1`，不得按相似OperationName强配，5号位不得自行创造Routing。

#### 场景6：E > Q
**输入事实**：当前LogicalProductionDemand数量真相`Q=80`，识别到既存执行量`E=100`。  
**责任位**：2号位先形成Q，再识别A/B执行事实并做数量切片。  
**中间身份/上下文**：`ContinuationQty = min(E,Q)=80`，`FreeQty=0`，`Overcommit=E-Q=20`。  
**Solver行为**：仅对Q范围内的Continuation数量进入当前需求求解；Overcommit作为异常/解释事实，不扩大Q。  
**持久化/输出**：保存Continuation/Allocation相关数量与Overcommit解释。  
**禁止行为**：不得因MES已执行100把LogicalProductionDemand从80扩大到100；不得制造额外Demand。

#### 场景7：无Routing Stage
**输入事实**：BOM/Stage链存在某Stage，但不存在可用RoutingOperation；已发布StageLeadTimeParam可命中或缺失。  
**责任位**：2号位按`DEPT_EXACT → FACTORY_STAGE_DEFAULT → GLOBAL_STAGE_DEFAULT`装载StageLeadTimeParam；1号位消费StageTimingNode。  
**中间身份/上下文**：有参数时形成StageTimingNode/时间约束；无参数时形成`STAGE_LEADTIME_MISSING`。  
**Solver行为**：保持Stage时间与上下游依赖，不生成虚假有限资源Operation。  
**持久化/输出**：无Routing Stage不生成Task、TaskNo、MES工单；缺失参数进入Issue/Explanation。  
**禁止行为**：不得伪造OperationCode/Resource，不得创建假Task绕过StageLeadTime缺失。


## Setup换型规则、人工有限产能、用户输出联合冻结版

版本：V3.25

------------------------------------------------------------------------

# 一、文档定位

本版本基于APS
V1最终冻结业务基线、数据架构、接口设计及各号位实施包，用于最终排产流程走查。

本次更新仅同步：

-   Setup换型约束；
-   无设备小工序处理；
-   人工有限产能；
-   Candidate治理边界；
-   用户输出解释链路。

不改变原有APS V1核心流程。

------------------------------------------------------------------------

# 二、APS V1核心排产主流程

    需求输入
     ↓
    需求解析
     ↓
    Supply构建
     ↓
    Pegging供需承接
     ↓
    Logical Production Demand
     ↓
    有限产能排程
     ↓
    Task生成
     ↓
    计划发布
     ↓
    执行反馈

------------------------------------------------------------------------

# 三、排程前准备

## 3.1 运行输入

排程运行前准备：

-   Demand；
-   Supply；
-   Pegging结果；
-   PI Position；
-   NextOperationContext；
-   Rule Snapshot；
-   Parameter Snapshot；
-   Strategy Snapshot。

------------------------------------------------------------------------

## 3.2 职责边界

2号位：

负责运行数据准备。

3号位：

负责规则和参数冻结。

5号位：

负责复杂事实提供。

1号位：

负责Solver计算。

------------------------------------------------------------------------

# 四、有限产能排程流程

## 4.1 Operation Planning Mode

工序是否参与有限资源求解，由：

Operation Planning Mode

决定。

------------------------------------------------------------------------

## 模式

  模式              处理
  ----------------- ------------------
  FINITE_RESOURCE   进入有限能力求解
  UNCONSTRAINED     保留工艺时间
  WAIT_ONLY         形成等待约束

------------------------------------------------------------------------

# 五、Resource能力模型

统一：

    Resource
    +
    ResourceCalendar
    +
    OperationResourceEligibility

支持：

-   设备资源；
-   人工资源。

------------------------------------------------------------------------

# 六、Setup换型流程

Setup作为资源时间约束。

计算：

-   当前任务；
-   下一任务；
-   Resource；
-   Setup规则。

影响：

-   Start Time；
-   End Time；
-   Resource占用；
-   交期风险。

------------------------------------------------------------------------

禁止：

-   Setup作为独立生产Task；
-   Setup生成Supply；
-   Setup改变Pegging。

------------------------------------------------------------------------

# 七、无设备小工序处理

不再采用：

"无设备直接跳过"。

根据：

OperationPlanningMode

决定：

-   是否占用有限资源；
-   是否进入Solver；
-   是否形成等待关系。

------------------------------------------------------------------------

# 八、Candidate流程

Candidate流程：

    用户创建Candidate
     ↓
    3号位治理校验
     ↓
    2号位执行
     ↓
    1号位Solver计算
     ↓
    结果保存
     ↓
    用户比较确认

------------------------------------------------------------------------

# 九、解释输出流程

延期及影响原因：

    1号位排程过程产生
     ↓
    2号位保存
     ↓
    5号位查询接口
     ↓
    4号位展示

包括：

-   Material原因；
-   Resource原因；
-   Setup原因；
-   Dependency原因；
-   Firm/Frozen原因。

------------------------------------------------------------------------

# 十、Pegging与PI Position边界

## Pegging

Owner：

2号位。

保持：

-   Demand/Supply承接；
-   Allocation；
-   数量闭环。

------------------------------------------------------------------------

## PI Position

Owner：

5号位计算。

2号位消费。

不作为Supply。

------------------------------------------------------------------------

# 十一、跨厂业务

保持：

跨厂事实用于：

-   Supply承接解释；
-   物流追踪。

不形成：

-   第二Supply模型；
-   INTERPLANT_IN_TRANSIT Supply。

------------------------------------------------------------------------

# 十二、最终流程红线

禁止：

-   页面直接修改排程结果；
-   查询服务复制计算逻辑；
-   5号位重算Pegging；
-   3号位参与Solver；
-   1号位维护权限；
-   新建第二套计划真相。

------------------------------------------------------------------------

# 十三、流程验收重点

必须验证：

1.  排程主链闭环；
2.  Setup正确影响资源时间；
3.  人工资源可参与有限产能；
4.  Candidate可追溯；
5.  解释信息可查询；
6.  Pegging、PI Position职责不漂移。

------------------------------------------------------------------------

# 十四、最终结论

APS V1排产流程形成：

-   业务闭环；
-   数量闭环；
-   时间闭环；
-   版本闭环；
-   职责闭环。

本版本作为核心排产流程走查冻结依据。