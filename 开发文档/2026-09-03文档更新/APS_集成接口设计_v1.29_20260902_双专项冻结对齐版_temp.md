# APS集成接口设计 v1.29
## 20260902 双专项冻结对齐版

> 本版本替换《APS_集成接口设计 v1.28》。
>
> 本次仅同步Domain Resolve与Supply / ProductionInstruction Position接口职责边界，不新增业务接口，不改变原有接口冻结关系。

---

# 一、本次修订范围

本版本补充：

1. Domain Resolve运行职责；
2. 2号位与5号位PI Position计算交互；
3. SupplyFact_Pipeline当前V1消费边界。

不新增：

- Domain实时计算接口；
- Position事件接口；
- 新Supply统一接口。

---

# 二、Domain Resolve接口边界

DomainDefinition属于APS治理配置。

流程：

```
ScheduleRun启动
↓
冻结DomainDefinition配置语义
↓
2号位批量执行Domain Resolve
↓
形成Order / Demand DomainKey
↓
进入Pegging
```

禁止：

- ERP同步携带DomainKey；
- ProductFamily.Code直接转换DomainKey；
- 逐订单调用Domain计算接口。

---

# 三、2号位与5号位PI Position接口

5号位提供：

生产指示位置计算能力。

2号位负责：

- 事实准备；
- 调用；
- Snapshot保存；
- Pegging消费。

流程：

```
ERP/MES/ODS事实
↓
2号位组织输入
↓
调用5号位PI Position计算
↓
返回Position结果
↓
2号位保存ProductionInstructionPositionSnapshot
↓
Pegging消费
```

---

# 四、PI Position接口语义

输入：

- ProductionInstructionNo；
- MaterialId；
- ERP PI RemainingQty；
- Stage执行事实；
- MES事实；
- 生产指示别库存余额事实；
- 跨厂事实；
- Routing / Stage路径；
- DataCutoffTime。

输出：

- ProductionInstructionNo；
- PositionType；
- Position Quantity；
- StageCode；
- AvailableTime；
- Issue信息；
- UNLOCATED标识。

---

# 五、职责边界

## 5号位

负责：

- Position计算规则；
- ODS事实解释。

不负责：

- Allocation；
- Pegging；
- Task生成；
- Snapshot生命周期。

## 2号位

负责：

- 运行编排；
- Snapshot保存；
- Pegging消费。

不负责：

- 重新实现Position计算规则。

---

# 六、SupplyFact_Pipeline接口边界

当前V1：

承接：

- 采购在途；
- 未结PO剩余；
- 已到厂未入库。

不作为Pipeline Supply：

- PI Position；
- PI XC；
- PI INTERPLANT_TRANSIT；
- SH Transit；
- SH Received。

---

# 七、跨厂事实接口

ERP_InterplantInTransit_View作为跨厂事实来源。

PI号：

用于ProductionInstructionPositionSnapshot计算。

SH号：

用于出荷指示履行状态判断。

禁止：

因存在Transit字段直接生成Transit Supply。

---

# 八、冻结说明

本版本属于APS V1接口一致性修订。

保持：

- 原接口Owner边界；
- 原调用关系；
- 原系统职责。

仅统一业务语义。
