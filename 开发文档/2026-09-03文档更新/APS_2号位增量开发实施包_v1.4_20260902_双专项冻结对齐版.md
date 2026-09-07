# APS 2号位增量开发实施包 v1.4
## 20260902 双专项冻结对齐版

> 本版本替换《APS 2号位增量开发实施包 v1.3》。
>
> 本次仅同步Domain Resolve与Supply / ProductionInstruction Position边界，不改变2号位整体职责，不引入新的业务范围。

---

# 一、本次修订范围

本版本补充：

1. Domain Resolve运行职责；
2. ProductionInstructionPositionSnapshot保存职责；
3. Pegging消费Position边界；
4. 与5号位Position计算职责分离。

不新增：

- Position计算规则；
- 新Supply模型；
- 新Solver逻辑。

---

# 二、2号位职责定位

2号位负责：

- ScheduleRun运行编排；
- Domain Resolve执行；
- Pegging；
- Allocation；
- PlanVersion运行管理；
- ProductionInstructionPositionSnapshot保存与消费。

2号位不负责：

- ERP/MES事实解释；
- Position计算规则；
- Stage状态推导。

---

# 三、Domain Resolve实现要求

## 1. 运行时序

必须：

```
ScheduleRun启动
↓
冻结DomainDefinition配置语义
↓
执行Domain Resolve
↓
生成Order / Demand DomainKey
↓
进入Pegging
```

---

## 2. 禁止实现

禁止：

- 从ERP直接读取DomainKey；
- 使用ProductFamily.Code替代DomainKey；
- 在Pegging过程中动态修改Domain归属；
- 每个订单实时调用Domain规则计算。

---

# 四、ProductionInstructionPositionSnapshot实现要求

## 1. 调用链

```
ERP/MES/ODS事实
↓
2号位组织输入
↓
调用5号位Position计算
↓
返回Position结果
↓
2号位保存Snapshot
↓
Pegging消费
```

---

## 2. 2号位负责内容

负责：

- 接收计算结果；
- 校验运行关联；
- 保存ScheduleRunId；
- 保存PlanVersionId；
- 提供Pegging读取。

---

## 3. 2号位不负责内容

禁止：

在2号位代码中实现：

- Stage判断；
- XC判断规则；
- 跨厂位置判断；
- MES报工解释。

这些属于5号位事实计算职责。

---

# 五、Pegging消费边界

Pegging消费：

ProductionInstruction Supply。

同时读取：

ProductionInstructionPositionSnapshot。

Position用于：

- AvailableTime判断；
- 执行位置判断；
- 数量闭环校验。

Position不是：

- 独立Supply；
- 新Allocation来源。

---

# 六、数量闭环校验

2号位需要保证：

```
Σ Position.Quantity = ERP PI RemainingQty
```

发现异常：

- 数量不一致；
- Position缺失；
- 未定位；

应登记异常。

不得自行修正事实。

---

# 七、SupplyFact_Pipeline消费边界

2号位消费：

当前V1允许的外部未来供给：

- 采购在途；
- 未结PO剩余；
- 已到厂未入库。

不将以下对象转换为Supply：

- PI Position；
- PI XC；
- PI INTERPLANT_TRANSIT；
- SH Transit；
- SH Received。

---

# 八、与其他号位接口

## 与3号位

输入：

DomainDefinition治理结果。

---

## 与5号位

输入：

事实数据。

输出：

Position计算结果。

---

## 与1号位

输出：

Pegging后的排程输入。

不向Solver传递：

- Domain规则；
- Position计算逻辑。

---

# 九、冻结说明

本版本属于：

APS V1实施边界一致性修订。

目标：

保证代码实现符合冻结业务。

不改变：

- Pegging核心规则；
- 有限产能流程；
- Task生成流程；
- Candidate机制。
