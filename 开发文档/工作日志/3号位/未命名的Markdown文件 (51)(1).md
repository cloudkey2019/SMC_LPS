我看完了3号位追问，以及1号位最新回执。 

这一轮需要做几个修正。你指出的问题是对的：

1. **等待时间不要现在做MES LagTime事实优先级设计过度扩展。**
2. **OperationPlanningMode应该归属工艺规划属性，而不是独立规则体系。**
3. **Resource Calendar和Resource来源必须分开，不能混淆。**
4. **人工Resource Slot必须在APS资源体系维护。**

另外，1号位提出的消费方式基本正确，需要补充两个边界。

下面重新裁决。

---

# 一、R4'：MES LagTime 与 OperationTransitionRule

3号位原提议：

> MES LagTime事实 > OperationTransitionRule规则默认

这个方向理论上没有错，但是**V1不应该扩大成MES等待事实治理问题。**

当前事实：

* MES Routing只提供StandardDuration；
* LagTime当前没有真实业务来源；
* MES_APS_Routing_View中的LagTime=0只是占位。

所以V1裁决：

## 不建立“MES LagTime优先级模型”。

采用：

```
有效Routing LagTime
        ↓
OperationTransitionRule
        ↓
默认0
```

但这里强调：

当前MES LagTime=0不能作为有效事实。

也就是说：

如果未来MES真正提供：

```
Operation A → Operation B
LagTime=2小时
```

可以消费。

但是目前：

```
LagTime=0
```

不应该覆盖3号位规则。

---

因此给3号位：

> V1阶段不讨论MES等待事实治理。当前以APS规则参数补充缺失事实；未来MES提供真实LagTime后，再纳入优先级体系。

---

# 二、R6：OperationPlanningMode载体

这里我同意你提出的修正。

3号位建议：

> 独立治理表。

我认为过度设计。

1号位也认可：

它只是消费属性。

## 最终裁决：

OperationPlanningMode属于：

> APS工艺规划属性（Operation Planning Attribute）

不是规则引擎。

不是SetupTransitionRule这种动态规则。

---

建议归属：

类似：

```
OperationPlanningParam
```

或者：

```
APS Operation Extension
```

表达：

```
OperationCode

PlanningMode

EffectiveVersion
```

例如：

| Operation | PlanningMode    |
| --------- | --------------- |
| 精修        | FINITE_RESOURCE |
| 清洗        | UNCONSTRAINED   |
| 冷却        | WAIT_ONLY       |

---

责任：

* 3号位：维护；
* 2号位：加载；
* 1号位：消费。

---

# 三、R7：未配置模式怎么办？

3号位A方案正确。

批准。

规则：

## 有ResourceEligibility：

默认：

```
FINITE_RESOURCE
```

---

## 无Resource：

必须：

存在：

```
UNCONSTRAINED
或者
WAIT_ONLY
```

否则：

异常。

---

禁止：

```
无资源
自动认为无限产能
```

原因：

这会掩盖主数据错误。

例如：

精修漏配置资源：

系统却认为无限能力。

这是不可接受的。

---

# 四、关于配品，不再单独展开

你说得对。

配品只是一个例子。

不需要形成专项设计。

统一原则：

> 所有Operation按照实际制造能力确定PlanningMode。

不讨论：

“配品属于什么”。

---

# 五、Resource Calendar问题（重点修正）

这里3号位和前面表达有混淆。

必须拆开：

## 1. Resource来源

例如：

设备：

```
MES/EAM
```

提供：

* Resource基本信息；
* Resource编码；
* 设备属性。

---

## 2. Resource Calendar来源

不是MES。

不是Resource ETL。

而是：

APS资源能力维护。

包括：

* 工作时间；
* 班次；
* 停机；
* 可用窗口。

---

所以：

正确关系：

```
MES/EAM
    ↓
Resource ETL

APS资源管理
    ↓
Resource Calendar
```

---

例如：

设备：

NC001

来源：

MES。

但是：

Calendar：

APS维护：

```
白班
08:00-17:00
```

---

人工：

FINISH01

来源：

APS资源维护。

Calendar：

APS维护：

```
白班
08:00-17:00
```

---

因此：

3号位R9需要修改。

不是：

> MES提供MANUAL_STATION资源+Calendar。

而是：

> MES提供可用Resource事实；APS负责Resource Calendar；人工Resource Slot由APS资源体系维护。

---

# 六、人工Resource Slot最终模型

确认：

必须在APS资源中维护。

例如：

A部门：

精修：

APS维护：

```
FINISH_SLOT_01

FINISH_SLOT_02

...

FINISH_SLOT_15
```

然后：

Calendar：

```
01~10:

白班

11~15:

夜班
```

---

2号位：

只加载。

1号位：

只消费。

---

# 七、关于1号位消费方案

1号位这次回执整体认可。

尤其：

> 不剥离UNCONSTRAINED工序。

这个正确。

否则：

Routing链断裂。

---

最终消费：

## FINITE_RESOURCE

现有逻辑：

```
Operation
 ↓
Eligibility
 ↓
Resource
 ↓
Calendar
```

排程。

---

## UNCONSTRAINED

不找资源。

但是：

保留工序链。

例如：

```
粗加工

结束10:00

↓

清洗30分钟

↓

精修最早10:30
```

清洗：

占时间。

不占资源。

---

## WAIT_ONLY

例如：

冷却。

只：

增加时间。

不占资源。

---

# 八、Transition Rule / StageTransferTime

这里3号位和1号位方向正确。

但是简化。

不做复杂规则引擎。

V1：

3号位维护：

## Operation Transition Rule

例如：

部门/Stage：

```
机加工内部

默认转运30分钟
```

---

## Stage Transfer Time

例如：

```
机加工→表面处理

4小时
```

---

传输：

```
DomainSolveRequest
```

不进入Frozen六块。

同意。

---

# 九、最终给1、2、3号位统一裁决

| 事项                    | 最终口径                      |
| --------------------- | ------------------------- |
| OperationPlanningMode | APS工艺规划属性，不独立规则引擎         |
| 维护责任                  | 3号位维护，2号位加载，1号位消费         |
| MES LagTime           | V1不建设事实治理，未来真实事实可接入       |
| 当前LagTime=0           | 不覆盖APS规则                  |
| Transition Rule       | 3号位规则参数                   |
| Stage Transfer Time   | 3号位规则参数                   |
| 是否进入Frozen六块          | 否，进入DomainSolveRequest    |
| Resource来源            | 设备资源可来自MES/EAM ETL        |
| Resource Calendar     | APS维护，不来自MES              |
| 人工Resource Slot       | APS资源体系维护                 |
| Routing是否修改           | 不修改                       |
| 无资源工序                 | 不能自动无限产能，必须配置PlanningMode |
| UNCONSTRAINED         | 保留工序链，只占时间，不占资源           |
| WAIT_ONLY             | 只增加等待时间，不占资源              |

---

# 十、补充一个架构原则（建议写入冻结）

这次讨论其实明确了一条：

> MES Routing描述“生产要做什么”；APS Planning属性描述“APS如何计划”；Resource描述“由什么能力执行”；Calendar描述“什么时候可以执行”。

四者不要混在一起。

对应：

```
MES Routing
    ↓
Operation

APS Planning Attribute
    ↓
OperationPlanningMode

APS Resource Model
    ↓
Resource + Eligibility

APS Calendar Model
    ↓
Resource Availability
```

这样既不会改MES，也不会让3号位规则引擎无限膨胀。你这次指出Resource和Calendar混淆的问题，是需要马上纠正的，否则后续设计会偏。
