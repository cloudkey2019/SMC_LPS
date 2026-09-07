# APS 数据架构与防腐层设计方案 v1.40

## 20260902 双专项冻结对齐版

> 本版本基于v1.39修订，仅同步Domain Resolve与Supply /
> ProductionInstruction Position数据边界，不改变APS V1总体数据架构。

------------------------------------------------------------------------

# 一、本次修订范围

本版本只增加：

1.  Domain Resolve数据职责边界；
2.  Order.DomainKey生命周期定义；
3.  SupplyFact_Pipeline边界收敛；
4.  ProductionInstructionPositionSnapshot定位说明。

不新增：

-   DomainRuntime平台；
-   Position事件平台；
-   新Supply统一中心；
-   新历史版本体系。

------------------------------------------------------------------------

# 二、Domain数据边界

## 1. DomainDefinition

DomainDefinition属于APS治理配置。

来源：

APS配置域。

职责：

-   定义排程域；
-   维护域规则；
-   支撑ScheduleRun运行。

------------------------------------------------------------------------

## 2. DomainKey生命周期

Order.DomainKey：

表示：

> 某次ScheduleRun中APS执行Domain Resolve后的运行归域结果。

不是：

-   ERP来源字段；
-   ODS原始字段；
-   ProductFamily.Code映射结果；
-   永久主数据。

------------------------------------------------------------------------

数据流程：

    ERP/ODS Order Fact
            ↓
    APS订单事实
            ↓
    Domain Resolve
            ↓
    Order.DomainKey
            ↓
    Pegging

------------------------------------------------------------------------

# 三、Domain Resolve职责

## 3号位

负责：

-   DomainDefinition治理；
-   配置权限；
-   审计。

------------------------------------------------------------------------

## 2号位

负责：

-   Run启动后批量执行Domain Resolve；
-   形成Order/Demand运行归域结果。

------------------------------------------------------------------------

## ERP/ODS

不负责：

-   生成DomainKey；
-   判断排程域。

------------------------------------------------------------------------

# 四、SupplyFact_Pipeline边界

## 当前V1定位

SupplyFact_Pipeline不是所有Supply统一表。

当前用于：

> 外部未来供给事实管道。

包括：

-   采购在途；
-   未结PO剩余；
-   已到厂未入库。

------------------------------------------------------------------------

## 当前不进入Pipeline Supply

包括：

-   ProductionInstructionPositionSnapshot；
-   PI XC；
-   PI INTERPLANT_TRANSIT；
-   SH Transit；
-   SH Received。

------------------------------------------------------------------------

# 五、ProductionInstructionPositionSnapshot定位

## 1. 定位

ProductionInstructionPositionSnapshot：

表示某次ScheduleRun/PlanVersion中：

生产指示剩余数量的位置事实快照。

------------------------------------------------------------------------

关系：

    ProductionInstruction Supply
              |
              +---- ProductionInstructionPositionSnapshot

------------------------------------------------------------------------

## 2. 职责

Position Snapshot表达：

-   当前Stage；
-   执行位置；
-   状态；
-   AvailableTime；
-   数量分布。

------------------------------------------------------------------------

不表达：

-   新Supply；
-   Allocation关系；
-   独立需求。

------------------------------------------------------------------------

# 六、PI数量闭环

生产指示剩余数量：

以ERP RemainingQty为边界。

Position用于解释：

> 剩余量在哪里。

必须：

    Σ Position.Quantity = ERP PI RemainingQty

禁止：

-   Position重新计算PI数量；
-   XC、Transit重复累计；
-   Received重复扣减。

------------------------------------------------------------------------

# 七、跨厂事实边界

ERP_InterplantInTransit_View：

作为跨厂事实来源。

根据业务对象分流：

## PI号

进入：

ProductionInstructionPositionSnapshot。

## SH号

进入：

SH履行状态。

------------------------------------------------------------------------

禁止：

因为存在Transit事实：

直接创建Transit Supply。

------------------------------------------------------------------------

# 八、VMI边界

当前V1：

不启用真实VMI数据链。

保留：

未来扩展能力。

不存在：

VMI正式Supply消费闭环。

------------------------------------------------------------------------

# 九、数据库设计原则

本次修订：

不新增：

-   DomainRuntime表；
-   PositionHistory表；
-   Supply统一超级表。

继续使用：

-   当前SupplyFact结构；
-   当前ProductionInstructionPositionSnapshot结构；
-   当前ScheduleRun/PlanVersion追溯机制。

------------------------------------------------------------------------

# 十、冻结说明

v1.40不是新架构。

属于：

> APS V1冻结数据边界澄清。

后续实现必须遵守：

-   Domain由运行归域产生；
-   Supply与Position分离；
-   PI Position不进入Supply池。
