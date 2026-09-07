# APS数据库字段说明文档 v5.1.5
## 20260902 双专项冻结对齐版

本版本替换：
《APS_数据库字段说明文档_v5.1.4_20260901_Domain专项冻结对齐_治理修正版.md》

本次仅同步：
- Domain Resolve字段语义；
- ProductionInstructionPositionSnapshot定位；
- SupplyFact_Pipeline边界。

不新增数据库字段，不改变表结构、主键、外键。

---

# 1. Order.DomainKey字段语义

Order.DomainKey表示：

当前ScheduleRun中，APS执行Domain Resolve后的运行归域结果。

不是：
- ERP来源字段；
- ODS原始字段；
- ProductFamily.Code。

来源：

DomainDefinition
↓
ScheduleRun冻结配置
↓
Domain Resolve
↓
Order.DomainKey
↓
Pegging

---

# 2. ProductionInstructionPositionSnapshot定位

ProductionInstructionPositionSnapshot表示：

某次ScheduleRun / PlanVersion中，ProductionInstruction剩余数量的位置事实快照。

不是：
- Supply表；
- 独立Supply来源；
- 历史事件流水表。

关系：

ProductionInstruction Supply
|
+---- ProductionInstructionPositionSnapshot

---

# 3. PositionType

|类型|说明|
|-|-|
|FIRST_STAGE_PENDING|首工序待开始|
|STAGE_WAITING|Stage等待|
|XC|生产指示级XC位置|
|INTERPLANT_TRANSIT|生产指示跨厂途中位置|
|UNLOCATED|无法准确定位的位置|

INTERPLANT_TRANSIT属于Position，不属于Transit Supply。

---

# 4. Quantity

Quantity表示：

ERP ProductionInstruction RemainingQty在不同Position中的互斥分布。

必须满足：

Σ Position.Quantity = ERP PI RemainingQty

禁止：
- Position重新计算PI剩余量；
- Transit重复增加数量；
- Received重复扣减数量。

---

# 5. AvailableTime

表示该Position对应数量最早可以被Demand承接的时间。

---

# 6. SupplyFact_Pipeline边界

当前V1承接：
- 采购在途；
- 未结PO剩余；
- 已到厂未入库。

不作为Pipeline Supply：
- ProductionInstructionPositionSnapshot；
- PI XC；
- PI INTERPLANT_TRANSIT；
- SH Transit；
- SH Received。

---

# 7. 冻结说明

本版本属于APS V1字段语义一致性修订。

保持：
- 表结构不变；
- 主键不变；
- 外键关系不变。

仅统一业务解释。
