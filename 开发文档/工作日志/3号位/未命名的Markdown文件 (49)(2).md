我看完了1、2、3号位的反馈。整体判断：

**原方案方向没有问题，但需要补充几个关键裁决，否则确实会导致1、2、3号位在落地时各自理解。**

三方反馈里，真正需要0号位拍板的不是人工资源模型，而是：

1. **无资源工序（非瓶颈/等待型）到底如何进入1号位排程链；**
2. **工序三分类由谁定义；**
3. **等待/转运规则如何进入1号位接口；**
4. **人工资源槽的数据来源与维护边界。**

下面逐项裁决。

---

# APS V1 无设备小工序与人工有限产能模型补充裁决

依据：

* 2号位提出的问题主要集中在：配品分类、无资源工序消费、人工资源槽来源、Transition Rule落地。
* 1号位确认人工Resource模型、Calendar机制与现有Solver一致，但提出无资源工序如果没有资源会触发失败，需要明确消费方式。
* 3号位提出规则治理、分类归属、接口契约以及Resource Calendar装载等影响项。

---

# 一、关于总体模型：原方案保持，不调整

先明确：

## 1. 人工资源模型不变

保持：

```
ProductionDepartment
        ↓
Operation
        ↓
OperationResourceEligibility
        ↓
Resource
        ↓
ResourceCalendar
```

例如：

```
A部门

  精修Operation

      ↓

  精修人工01
  精修人工02
  ...
  精修人工15
```

这个方向三方没有异议。

1号位确认：

> 人工复用Resource模型，只要作为普通Resource下发，Solver无需感知人工或设备。

3号位也确认：

Resource.ResourceType已经支持MANUAL_STATION，OperationResourceEligibility具备关联基础。

因此：

**不新增人工排程模型，不修改Routing。**

---

# 二、关于配品（M101）分类：需要0号位明确

2号位提出：

配品目前：

* EquipNo=NULL；
* IfhasEquips=0。

需要判断属于：

①有限资源
②非瓶颈
③等待型



这里裁决：

## 配品暂不默认归入有限资源。

原因：

“无设备”并不能直接推出“人工有限”。

但是：

配品也不能简单认为无限。

因此采用：

## 分类原则：

由业务确认。

不是根据：

```
EquipNo是否为空
```

自动判断。

---

例如：

## 情况A：配品需要人员逐件操作

例如：

* 按订单配套；
* 人员一次只能处理一个任务。

则：

```
FINITE_RESOURCE
```

建立：

```
配品人工01~N
```

---

## 情况B：配品只是系统动作/物料齐套确认

不形成现场瓶颈：

```
UNCONSTRAINED
```

---

所以：

**2号位不能自动推断。**

三号位提出：

Routing里的ProcessType不能直接作为这个分类，因为它只是工序类型，不是产能模型分类。这个判断正确。

最终：

> 工序产能分类属于APS业务规则，不属于MES Routing事实。

---

# 三、关于非瓶颈/等待型工序如何进入1号位

这是最关键的问题。

1号位提出：

目前Solver：

```
没有Eligible Resource
=
失败
```

如果清洗没有资源：

会被判断为数据异常。



所以之前方案“只告诉1号位它没有资源”是不完整的。

这里需要修正。

---

## 不采用1号位建议的A方案

1号位建议：

> 2号位把非瓶颈/等待工序剥离，不进入Solver。



我不建议作为正式方案。

原因：

这会导致：

Routing链被2号位改变。

例如：

原始：

```
粗加工
 ↓
清洗
 ↓
精修
```

变成：

```
粗加工
 ↓
精修
```

那么：

清洗时间如何影响：

* 交期；
* CTP；
* 后续工序最早开始时间；

会变复杂。

---

## 推荐方案：

增加明确的Operation Planning Mode。

例如：

```
FINITE_RESOURCE

UNCONSTRAINED

WAIT_ONLY
```

---

这样：

### 精修

```
FINITE_RESOURCE
```

需要Resource。

### 清洗

```
UNCONSTRAINED

Duration=30分钟
```

不竞争资源。

### 冷却

```
WAIT_ONLY

WaitTime=8小时
```

不占资源。

---

这里需要说明：

这不是MES Routing字段。

而是：

APS规划属性。

3号位提出：

需要定义分类表达载体，这个判断正确。

---

# 四、关于这个分类谁维护？

裁决：

## 3号位负责规则治理。

原因：

这是规划规则，不是事实数据。

例如：

同一个MES工序：

清洗

未来可能：

* 普通清洗池：无限；
* 高精清洗线：有限。

所以不能写死在MES。

---

职责：

3号位：

维护：

```
OperationPlanningMode
```

例如：

```
OP_CLEAN

UNCONSTRAINED
```

---

2号位：

加载。

---

1号位：

消费。

---

# 五、关于Operation Transition Rule / Stage Transfer Time

三号位提出：

需要新增规则实体，并且要考虑接口契约。

这个判断正确。

裁决：

## 保留原方案：

3号位维护。

包括：

### 1. Operation Transition Rule

例如：

```
粗加工→精修

等待30分钟
```

---

### 2. Stage Transfer Time

例如：

```
机加工
 ↓
表面处理

4小时
```

---

但是：

不要放入已经冻结的六块Snapshot。

原因：

3号位指出：

六块FrozenStrategySnapshot目前没有等待/转运块。

建议：

参考SetupTransitionRule方式：

单独进入DomainSolveRequest。

原因：

避免扩大冻结接口。

---

# 六、关于人工资源槽谁建立？

2号位提出两个方案：

A：
业务提供人数，2号位生成资源。

B：
业务直接提供Resource数据。



裁决：

采用：

## B为正式生产方式。

原因：

Resource是主数据，不应该由2号位代码动态创造。

---

正确链：

业务/资源管理：

提供：

```
A部门
精修
白班10
夜班5
```

↓

资源主数据：

生成：

```
FINISH_DAY_01~10
FINISH_NIGHT_01~05
```

↓

Calendar：

维护：

```
白班时间
夜班时间
```

↓

2号位：

加载。

---

测试阶段：

可以允许2号位临时Seed。

但是不能作为正式V1设计。

---

# 七、关于Resource Calendar

这里3号位提到：

目前有7×24占位，需要真正Calendar装载。

裁决：

这个必须补。

因为：

如果没有真实Calendar：

白班10人、夜班5人无法表达。

---

所以：

Resource Calendar装载属于V1必须闭环项。

---

# 八、最终给1、2、3号位的统一裁决表

| 问题                      | 裁决                                |
| ----------------------- | --------------------------------- |
| 人工是否新建模型                | 否，复用Resource                      |
| 是否修改MES Routing         | 否                                 |
| 人工如何表达                  | Resource Slot                     |
| 白夜班如何表达                 | Resource Calendar                 |
| 10个人是否CapacityFactor=10 | 否，10个Resource                     |
| Operation分类是否依据有无设备     | 否                                 |
| 分类是否MES事实               | 否，APS规则                           |
| 有限/非瓶颈/等待表达             | 增加OperationPlanningMode           |
| 分类维护责任                  | 3号位规则治理                           |
| 2号位职责                   | 加载并传递                             |
| 1号位职责                   | 消费                                |
| 配品分类                    | 由业务确认，不自动推断                       |
| Transition Rule         | 3号位维护                             |
| Stage Transfer Time     | 3号位维护                             |
| 等待/转运接口                 | 建议独立DomainSolveRequest，不扩Frozen六块 |
| 人工资源正式创建                | 资源主数据侧，不由2号位代码生成                  |

---

# 九、下一步文档修改建议

这一轮需要修改冻结文档的地方：

1. 增加：

《有限产能排程业务说明》

补充：

* OperationPlanningMode；
* 有限资源/非瓶颈/等待型定义。

2. 增加：

《规则参数设计》

补充：

* Operation Transition Rule；
* Stage Transfer Time。

3. 增加：

《Resource模型说明》

补充：

* MANUAL_STATION；
* 人工Resource Slot；
* Calendar表达班次。

4. 增加：

《2号位接口设计》

补充：

* OperationPlanningMode输入；
* TransitionRule输入；
* StageTransferTime输入。

---

总体结论：

这次1、2、3号位反馈不是推翻方案，而是把原方案从“概念正确”推进到了“可落地”。

真正需要补的是：

**Operation能力分类 + 规则治理 + Calendar资源装载。**

人工资源模型本身不用改，继续沿用设备资源模型即可。
