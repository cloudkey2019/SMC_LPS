# APS 5号位复杂业务事实ODS与业务接口接入实施包 v1.2

## 20260902 双专项冻结对齐版

> 本版本替换《APS 5号位复杂业务事实ODS与业务接口接入实施包 v1.1》。
>
> 本次仅同步Domain Resolve与Supply / ProductionInstruction
> Position边界，不扩大5号位职责范围。

------------------------------------------------------------------------

# 一、本次修订范围

本版本补充：

1.  ProductionInstruction Position计算职责；
2.  ERP/MES事实解释边界；
3.  与2号位Snapshot保存职责分离；
4.  SupplyFact_Pipeline事实输出边界。

不新增：

-   Allocation逻辑；
-   Pegging逻辑；
-   Solver逻辑；
-   Supply统一模型。

------------------------------------------------------------------------

# 二、5号位职责定位

5号位负责：

-   ERP/MES/ODS事实接入；
-   复杂业务事实解析；
-   ProductionInstruction Position计算；
-   Supply相关事实契约输出。

5号位不负责：

-   ScheduleRun编排；
-   Domain Resolve运行；
-   Pegging；
-   Allocation；
-   Task生成；
-   Snapshot生命周期管理。

------------------------------------------------------------------------

# 三、ProductionInstruction Position计算职责

## 1. 输入事实

5号位消费：

-   ERP ProductionInstruction RemainingQty；
-   MES Stage执行事实；
-   MES报工事实；
-   生产指示相关库存事实；
-   跨厂事实；
-   Routing / Stage路径；
-   DataCutoffTime。

------------------------------------------------------------------------

## 2. 输出结果

输出：

-   ProductionInstructionNo；
-   PositionType；
-   Quantity；
-   StageCode；
-   AvailableTime；
-   Issue信息；
-   UNLOCATED标识。

------------------------------------------------------------------------

# 四、Position计算规则边界

Position表示：

> 已存在ProductionInstruction剩余数量的位置事实。

不是：

-   新Supply；
-   Allocation结果；
-   独立需求。

关系：

    ProductionInstruction Supply
            |
            +---- ProductionInstructionPositionSnapshot

------------------------------------------------------------------------

# 五、数量闭环要求

Position结果必须满足：

    Σ Position.Quantity = ERP PI RemainingQty

禁止：

-   Position重新定义PI剩余量；
-   Transit重复增加数量；
-   Received重复扣减。

------------------------------------------------------------------------

# 六、与2号位职责边界

## 5号位负责：

-   事实解释；
-   Position计算；
-   计算结果输出。

------------------------------------------------------------------------

## 2号位负责：

-   调用；
-   保存ProductionInstructionPositionSnapshot；
-   ScheduleRun关联；
-   Pegging消费。

------------------------------------------------------------------------

5号位不保存运行快照。

------------------------------------------------------------------------

# 七、SupplyFact_Pipeline事实输出边界

当前V1：

SupplyFact_Pipeline承接外部未来供给事实：

包括：

-   采购在途；
-   未结PO剩余；
-   已到厂未入库。

------------------------------------------------------------------------

以下不输出为Pipeline Supply：

-   PI Position；
-   PI XC；
-   PI INTERPLANT_TRANSIT；
-   SH Transit；
-   SH Received。

------------------------------------------------------------------------

# 八、跨厂事实处理

ERP_InterplantInTransit_View：

作为跨厂事实来源。

根据单据类型：

## PI

用于：

ProductionInstruction Position计算。

## SH

用于：

出荷指示履行状态判断。

禁止：

将Transit字段直接转换为Transit Supply。

------------------------------------------------------------------------

# 九、冻结说明

本版本属于：

APS V1事实计算职责一致性修订。

保持：

-   ODS契约；
-   接口Owner；
-   业务职责边界。

不改变：

-   Pegging；
-   Solver；
-   Task生成。
