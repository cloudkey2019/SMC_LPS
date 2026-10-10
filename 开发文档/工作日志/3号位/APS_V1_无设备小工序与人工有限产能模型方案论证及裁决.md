# APS V1 无设备小工序与人工有限产能模型方案论证及裁决

## 一、核心结论

无设备小工序不能简单按照"没有设备=无限产能"处理。

APS V1应按照是否存在有限能力约束分类：

1.  有限资源工序（Finite Resource Operation）
2.  非瓶颈工序（Unconstrained Operation）
3.  等待型工序（Wait Only Operation）

人工资源不建设独立模型，复用现有Resource、Resource
Calendar、OperationResourceEligibility模型。

------------------------------------------------------------------------

## 二、正确业务层级

正确关系：

Factory\
→ ProductionDepartment\
→ Operation（小工序）\
→ OperationResourceEligibility\
→ Resource\
→ ResourceCalendar

例如：

BJ工厂A部门：

A部门\
→ 精修Operation\
→ 精修人工资源01\~15

精修不是直接挂员工，而是通过OperationResourceEligibility关联允许使用的资源。

------------------------------------------------------------------------

## 三、有限能力小工序

例如精修：

A部门：

-   白班10个人
-   夜班5个人

建立人工产能槽：

白班：

FINISH_DAY_01 \~ FINISH_DAY_10

Calendar：

08:00-17:00

夜班：

FINISH_NIGHT_01 \~ FINISH_NIGHT_05

Calendar：

22:00-06:00

1号位无需区分人工和设备，只根据Resource Calendar进行有限产能排程。

------------------------------------------------------------------------

## 四、为什么不用一个虚拟资源

不采用：

一个Resource + CapacityFactor=10

原因：

CapacityFactor不能表达同一时间10个任务并行。

当前Solver按Resource时间轴互斥占用。

因此10个人需要表达为10个并行Resource Slot。

------------------------------------------------------------------------

## 五、MES Routing是否修改

不修改MES Routing。

MES继续提供：

-   Operation
-   Sequence
-   StandardDuration

APS通过：

OperationResourceEligibility

建立：

精修Operation → 精修人工01\~15

关系。

------------------------------------------------------------------------

## 六、无设备工序分类

### 1. 有限资源工序

例如：

精修。

进入Resource有限产能排程。

### 2. 非瓶颈工序

例如：

清洗。

有处理时间，但不形成有限资源竞争。

表达：

Operation Duration。

### 3. 等待型工序

例如：

自然冷却。

表达：

Wait Duration。

------------------------------------------------------------------------

## 七、工序间等待和转运

V1不建设完整工艺物流模型。

不维护每个工序之间复杂路线。

增加简化规则：

### Operation Transition Rule

按部门/Stage维护默认工序间等待、转运规则。

例如：

精加工→精修：

默认等待30分钟。

### Stage Transfer Time

维护大工艺之间转运时间。

例如：

机加工Stage→表面处理Stage：

4小时。

上述规则由3号位规则参数维护。

2号位加载。

1号位消费。

------------------------------------------------------------------------

## 八、最终裁决

1.  无设备不等于无限产能。
2.  人工复用设备Resource模型。
3.  人工通过产能槽表达，不维护真实员工。
4.  白班夜班通过Resource Calendar表达。
5.  不修改MES Routing。
6.  Operation与Resource通过OperationResourceEligibility关联。
7.  等待和转运采用3号位规则参数简化表达。

最终模型：

Operation定义生产动作；\
ProductionDepartment定义业务归属；\
Resource定义执行能力；\
Calendar定义可用时间；\
Eligibility定义工序可使用哪些资源。
