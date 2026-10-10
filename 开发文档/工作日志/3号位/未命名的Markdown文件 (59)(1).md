APS V1 Stage、生产部门、Routing、Dependency、StageLeadTimeParam接口裁决回复
——回复2号位关于BOM Stage与1号位排程衔接问题
版本：v1.0
日期：2026-09-28
一、总体裁决原则
本次问题的核心，是需要明确：
BOM展开后的Stage事实，如何转换为1号位有限产能排程所需要的Routing、Dependency、Resource输入。

最终冻结链路如下：
BOM StageDetail
        ↓
StageCode
        ↓
MaterialStageDeptContext
        ↓
ProductionDepartment
        ↓
RoutingOperation
        ↓
RoutingDependency
        ↓
OperationResourceEligibility
        ↓
ResourceCalendar
        ↓
1号位有限产能排程

必须明确：
- Stage决定“大工艺阶段”；
- MaterialStageDeptContext决定“这个物料这个Stage由哪个生产部门负责”；
- Routing决定“该Stage下面有哪些小工序”；
- Dependency决定“小工序之间的先后关系”；
- StageLeadTimeParam负责“没有Routing小工序时的阶段时间承接”。
不能简化为：
StageCode
↓
Routing

中间的生产部门维度不能省略。
二、问题1：Stage过滤和ProductionDepartment过滤分别由谁负责？
2号位问题
当前理解：
2号位负责Stage过滤。
1号位负责ProductionDepartment过滤。
是否合理？
裁决：
合理。
但需要明确两者职责不同。
2号位负责：
确定：
当前需求需要经过哪些Stage，以及每个Stage对应哪个生产部门。

来源：
订单
↓
BOM展开
↓
StageDetail
↓
StageCode
↓
MaterialStageDeptContext

输出给1号位：
例如：
物料：
ASSY-AD48A-D

Stage：
BJ_ASSY

部门：
BJ装配一课

以及：
BJ_FINAL

部门：
BJ成品处理部门

1号位负责：
在已经确定：
Material
+
Stage
+
ProductionDepartment

基础上：
查找：
RoutingOperation
RoutingDependency
OperationResourceEligibility
ResourceCalendar

并进行有限产能排程。
因此：
不是2号位负责找到所有Routing。
也不是1号位自己猜Stage对应哪个部门。
正确分工：
2号位：
这个Stage属于哪个部门

↓

1号位：
这个部门下面如何排

三、问题2：Stage顺序应该如何传递给1号位？
这是本次最关键问题。
裁决：
Stage顺序事实来源：
StageDetail.StageSeq

不是：
- Routing顺序；
- OperationCode排序；
- Material全局Stage集合。
例如：
某物料：
ASSY-AD48A-D

StageDetail：
StageSeq	StageCode
10	BJ_ASSY
20	BJ_FINAL


业务含义：
BJ_ASSY
 ↓
BJ_FINAL

2号位需要提供什么？
提供业务事实：
MaterialId
StageCode
StageSeq
ProductionDepartmentId

形成：
EffectiveStagePath

例如：
[
 {
  "StageCode":"BJ_ASSY",
  "StageSeq":10,
  "ProductionDepartment":"装配部门"
 },
 {
  "StageCode":"BJ_FINAL",
  "StageSeq":20,
  "ProductionDepartment":"成品部门"
 }
]

1号位如何使用？
1号位可以根据自己的Solver实现：
转换为：
StageDependency

或者：
有序Stage列表

均可以。
但是：
2号位不负责生成Solver内部对象。
原因：
业务事实属于2号位。
算法内部结构属于1号位。
不能倒置。
四、问题3：跨Stage Dependency如何确定生产部门？
2号位问题
例如：
BJ机加
 ↓
BJ表面

如何知道两个阶段对应哪个部门？
裁决：
必须通过：
MaterialStageDeptContext

确定。
不能跳过。
例如：
物料A：
Stage1：
BJ_MACH

查询：
MaterialStageDeptContext

得到：
BJ机加部门

Stage2：
BJ_SURF

查询：
得到：
BJ表面处理部门

最终：
BJ机加部门工序
        ↓
BJ表面处理部门工序

这是正确业务。
五、跨Stage Dependency与同Stage Dependency区别
这里需要区分。
1. 同Stage Dependency
例如：
粗车
 ↓
精车

属于：
BJ_MACH

同一个生产部门。
此时：
ProductionDepartment可以作为校验条件。
2. 跨Stage Dependency
例如：
BJ_MACH
 ↓
BJ_SURF

两个Stage可能：
部门A
部门B

不同。
此时：
不能要求：
FromDepartment = ToDepartment

否则会错误限制真实制造流程。
因此：
跨Stage Dependency：
保留：
Operation A
Operation B
先后关系

但是：
两个Stage各自部门仍然由：
MaterialStageDeptContext

确定。
六、关于BJ_SURF特殊处理
BJ_SURF不能直接理解为：
StageCode → Routing

正确：
BJ_SURF
 ↓
MaterialStageDeptContext
 ↓
确定表面处理生产部门
 ↓
查Routing

原因：
同一个工厂、同一个大工艺，可能存在多个部门。
所以：
Stage不是部门。
Stage必须经过Master裁决。
七、问题4：StageLeadTimeParam属于什么数据？谁负责？
2号位问题
StageLeadTimeParam：
- 不来自MES；
- 不属于ODS；
- 那它属于Routing还是规则？
裁决：
StageLeadTimeParam：
不属于Routing数据。
原因：
Routing描述：
具体小工序如何制造

例如：
粗车
精车
磨削
清洗

包括：
- Operation；
- StandardTime；
- Dependency。
StageLeadTimeParam描述：
一个业务阶段需要多少时间

例如：
BJ_OUTS = 5天

BJ_FINAL = 1天

它不是小工序。
因此：
StageLeadTimeParam属于：
APS规则参数体系。
八、StageLeadTimeParam Owner
修正：
不是5号位。
5号位负责：
事实数据：
- BOM；
- Workset；
- StageDetail；
- ODS事实。
StageLeadTimeParam：
属于：
3号位规则参数体系。
3号位负责：
- 参数模型；
- 页面维护；
- 参数版本；
- 生效管理。
但是：
业务时间不是3号位制定。
来源：
生产部门
+
工艺部门
+
计划部门

提供。
流程：
业务部门提供标准时间

↓

3号位维护APS规则参数

↓

2号位消费

↓

1号位排程

九、StageLeadTimeParam粒度
之前：
Factory + Stage

不足。
原因：
不知道谁维护。
最终建议：
Factory
+
StageCode
+
ProductionDepartment

例如：
BJ
+
BJ_SURF
+
表面处理一课
+
2天

这样：
- Owner明确；
- 维护责任明确；
- 解释明确。
未来如果需要：
可以扩展：
MaterialFamily
+
Factory
+
Stage
+
Department

但V1不做。
十、StageLeadTimeParam和跨厂运输LT区别
两个不能合并。
StageLeadTimeParam
表示：
生产阶段时间。
例如：
BJ_SURF
2天

CrossFactory LT
表示：
工厂之间移动时间。
例如：
BJ完成
 ↓
运输
 ↓
TJ可用

3天

完整时间：
上游制造完成

+

跨厂运输LT

+

下游StageLeadTime


三者不能混成一个参数。
十一、问题5：RequiredStageCode保存在哪里？
2号位问题：
RequiredStageCode是否只存在LogicalProductionDemand？
裁决：
两个地方保留。
1. 运行输入
LogicalProductionDemand.RequiredStageCode

作用：
给Solver使用。
2. Pegging结果
保存：
PeggingSupplyAllocation.NextRequiredStageCode

作用：
追溯：
“为什么这个Supply可以承接这个需求？”
原因：
运行需要。
历史解释也需要。
十二、无Routing Stage如何处理？
统一规则：
StageDetail存在Stage

↓

查询RoutingOperation

↓

有Routing

按Operation排


无Routing

保留Stage

↓

查询StageLeadTimeParam

↓

形成阶段时间

禁止：
无Routing
=
Stage错误

十三、BJ_FINAL案例说明
例如：
ASSY-AD48A-D

BJ_ASSY
 ↓
BJ_FINAL

含义：
BJ_ASSY完成

↓

BJ_FINAL完成

↓

达到供给阈值

可能：
BJ_ASSY
有Routing

BJ_FINAL
无Routing

这是允许的。
因为：
BJ_FINAL代表：
- 完成节点；
- 可供给节点。
不是一定需要设备的小工序。
十四、最终职责边界
事项	Owner
BOM展开	5号位
StageDetail生成	5号位
Stage事实消费	2号位
MaterialStageDeptContext	2号位
StageLeadTimeParam规则模型	3号位
StageLeadTime业务输入	生产/工艺部门
Routing加载	1号位消费，来源MES
RoutingDependency执行	1号位
有限产能求解	1号位


十五、最终冻结结论
最终统一：
BOM决定物料需要经过哪些Stage；StageCode不能直接决定生产部门，必须通过MaterialStageDeptContext确定；生产部门确定后才能进入Routing；Routing决定Stage内部小工序；Dependency决定工序先后；跨Stage Dependency允许不同生产部门存在，不允许强制部门一致；StageLeadTimeParam属于3号位规则参数体系，用于无Routing阶段时间承接，粒度采用工厂+Stage+生产部门。