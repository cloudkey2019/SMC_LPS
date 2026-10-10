# APS V1 Resource Calendar资源日历能力补充方案

## 一、目的

补充APS V1有限产能排程中的Resource Calendar（资源日历）能力。

本方案明确：
- Resource Calendar属于APS资源能力事实；
- 不属于规则引擎；
- 不来源于MES资源日历；
- 不建设人员技能管理体系。

## 二、最终业务模型

APS不维护真实人员信息。

人工有限产能采用能力槽模型：

例如：

A部门：
- 精修01
- 精修02
- 精修03

表示三个有限人工能力槽，不代表真实员工。

人工资源定义方式：

生产部门 + 小工序 + 资源槽名称。

例如：

|生产部门|小工序|资源|
|-|-|-|
|A部门|精修|精修01|
|A部门|精修|精修02|

不同部门可以存在同名小工序。

## 三、设备与人工

设备：

由Routing确定Operation，再寻找对应资源。

不建设设备技能矩阵。

人工：

直接以部门和小工序定义能力槽。

V1不建设：
- 人员技能表；
- HR接口；
- 人工能力矩阵。

## 四、Resource Calendar

Resource Calendar统一服务设备和人工。

表达：

“资源什么时候可用”。

建议模型：

Resource：
- ResourceId
- ResourceName
- ResourceType
- ProductionDepartmentCode
- OperationCode（人工能力槽使用）

ResourceCalendarSlot：
- ResourceId
- StartTime
- EndTime
- AvailableFlag

## 五、业务链路

Routing
→ Operation
→ 生产部门/小工序
→ Resource
→ Resource Calendar
→ 有限产能排程

不是：

Resource反向寻找Operation。

## 六、职责

生产部门：
- 维护资源槽数量；
- 维护工作时间；
- 维护加班和临时调整。

5号位：
- APS资源能力事实治理；
- 资源日历业务数据管理。

说明：
Resource Calendar不是MES事实。

4号位：
- 页面维护；
- 查询；
- 编辑。

2号位：
- 数据库和API实现。

3号位：
- 不负责Resource Calendar；
- 不纳入规则引擎。

## 七、与重排关系

Resource Calendar支撑：

设备故障：
修改资源不可用时间，发起Candidate局部重排。

加班：
增加资源可用时间，发起Candidate局部重排。

## 八、冻结文档影响

需要补充：
- Resource Calendar定义；
- 资源维护职责；
- 人工能力槽模型。

不需要修改：
- Routing模型；
- OperationPlanningMode；
- Setup模型；
- 有限产能职责边界。

## 九、最终结论

APS V1采用：

统一Resource + Resource Calendar模型。

设备和人工均作为Resource。

人工采用：
生产部门 + 小工序 + 能力槽。

Resource Calendar负责：
资源可用时间。

Resource Calendar属于APS资源能力事实，不属于规则引擎。
