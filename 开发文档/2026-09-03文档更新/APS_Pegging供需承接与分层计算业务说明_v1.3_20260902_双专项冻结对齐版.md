# APS Pegging供需承接与分层计算业务说明 v1.3

## 20260902 双专项冻结对齐版

> 本文件基于v1.2冻结版本修订，仅同步Domain Resolve运行前置与Supply /
> ProductionInstruction
> Position边界，不改变原有Demand排序、Allocation、Demand
> Protection等冻结业务。

------------------------------------------------------------------------

# 一、v1.3修订范围

本版本补充：

1.  Domain Resolve作为Pegging前置步骤；
2.  Supply身份与Position事实分离；
3.  ProductionInstruction Position规则；
4.  SH、Transit、Received边界；
5.  当前V1供给管道边界。

除以上内容外，原Pegging冻结规则继续有效。

------------------------------------------------------------------------

# 二、Domain Resolve与Pegging关系

Pegging开始前必须完成：

    ScheduleRun
    ↓
    冻结DomainDefinition配置语义
    ↓
    Domain Resolve
    ↓
    Order / Demand DomainKey
    ↓
    单Domain Demand/Supply上下文
    ↓
    Pegging

约束：

-   ERP/ODS不直接生成DomainKey；
-   DomainKey属于APS运行归域结果；
-   Material和Supply不永久绑定Domain。

------------------------------------------------------------------------

# 三、Supply身份定义

## 1. Supply

Supply表示：

> 可以被Demand直接承接的供给身份。

包括：

-   库存；
-   ProductionInstruction（生产指示）；
-   出荷指示；
-   正式外部供给事实。

------------------------------------------------------------------------

## 2. Position

Position表示：

> 已存在Supply当前执行位置、状态和时间约束。

Position不是Supply。

不参与：

-   Supply身份创建；
-   Supply排序；
-   独立Allocation。

------------------------------------------------------------------------

# 四、ProductionInstruction与Position

ProductionInstruction：

作为Supply参与Pegging。

ProductionInstructionPositionSnapshot：

描述：

-   当前Stage；
-   XC状态；
-   跨厂状态；
-   AvailableTime。

关系：

    ProductionInstruction Supply
            |
            +---- ProductionInstructionPositionSnapshot

------------------------------------------------------------------------

# 五、PI Position类型

当前冻结类型：

  PositionType          说明
  --------------------- --------------
  FIRST_STAGE_PENDING   首工序待开始
  STAGE_WAITING         Stage等待
  XC                    生产指示级XC
  INTERPLANT_TRANSIT    PI跨厂途中
  UNLOCATED             未定位

------------------------------------------------------------------------

# 六、PI数量闭环

PI剩余数量：

以ERP ProductionInstruction RemainingQty为唯一边界。

Position只解释：

> 剩余数量在哪里。

必须满足：

    Σ Position.Quantity = ERP PI RemainingQty

禁止：

-   Position重新计算PI剩余数量；
-   Transit重复增加数量；
-   Received重复扣减数量。

------------------------------------------------------------------------

# 七、跨厂规则

## 1. 大工艺接续型跨厂

正确：

    ProductionInstruction Supply

    +

    INTERPLANT_TRANSIT Position

禁止：

    ProductionInstruction Supply

    +

    Transit Supply

------------------------------------------------------------------------

## 2. 厂间订单型

出荷指示（SH）保持单一Supply身份。

运输中、到达待处理属于SH履行状态。

禁止拆分：

-   Transit Supply；
-   Received Supply。

------------------------------------------------------------------------

# 八、Received边界

Received属于ERP事实。

用途：

-   状态追溯；
-   时间判断。

不作为：

-   独立Supply；
-   新Allocation来源；
-   PI Position额外扣减。

------------------------------------------------------------------------

# 九、SupplyFact_Pipeline边界

当前V1：

SupplyFact_Pipeline用于外部未来供给事实。

包括：

-   采购在途；
-   未结PO剩余；
-   已到厂未入库。

当前不启用真实VMI数据链。

以下不作为Pipeline Supply：

-   PI Position；
-   PI XC；
-   PI Transit；
-   SH Transit；
-   SH Received。

------------------------------------------------------------------------

# 十、职责边界

## 2号位

负责：

-   Domain Resolve运行；
-   Pegging；
-   Allocation；
-   Position Snapshot保存和消费。

不负责：

-   MES事实解释；
-   Position计算规则。

------------------------------------------------------------------------

## 5号位

负责：

-   ERP/MES/ODS事实解析；
-   PI Position计算。

不负责：

-   Allocation；
-   Task生成；
-   运行快照保存。

------------------------------------------------------------------------

## 1号位

负责：

-   有限产能Solver。

不感知：

-   DomainDefinition；
-   Position；
-   Supply事实解析。

------------------------------------------------------------------------

# 十一、冻结说明

本次修订不改变：

-   Demand排序；
-   PriorityScore；
-   Allocation规则；
-   Demand Protection；
-   Planning-only Purchase Placeholder；
-   有限产能求解流程。

仅澄清供需事实边界。
