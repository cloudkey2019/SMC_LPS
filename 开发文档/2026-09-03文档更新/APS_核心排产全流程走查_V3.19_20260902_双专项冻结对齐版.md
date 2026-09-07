# APS核心排产全流程走查 V3.19

## 20260902 双专项冻结对齐版

> 本版本基于V3.18修订，仅同步Domain Resolve运行前置以及Supply /
> ProductionInstruction Position边界，不改变APS V1既有排程主链。

------------------------------------------------------------------------

# 一、V3.19修订范围

本次只修订两个方面：

1.  Domain Resolve进入正式排程前置流程；
2.  PI Position作为供给事实解释，不作为独立Supply。

其他冻结内容保持：

-   Demand排序；
-   Pegging；
-   Allocation；
-   有限产能Solver；
-   Task生成；
-   PlanVersion。

------------------------------------------------------------------------

# 二、夜间FULL排程主链

正式流程：

    ScheduleRun创建
    ↓
    冻结本次运行DomainDefinition配置语义
    ↓
    冻结ExpectedDomainKeysJson
    ↓
    Order / Demand同步完成
    ↓
    2号位批量执行Domain Resolve
    ↓
    形成Order / Demand DomainKey
    ↓
    Demand-Supply准备
    ↓
    Pegging
    ↓
    有限产能排程
    ↓
    生成Task
    ↓
    PlanVersion发布

------------------------------------------------------------------------

# 三、Domain Resolve运行边界

## 1. 产生时机

DomainKey必须在Pegging之前形成。

------------------------------------------------------------------------

## 2. 责任

### 3号位

负责：

-   DomainDefinition治理；
-   配置维护；
-   权限与审计。

------------------------------------------------------------------------

### 2号位

负责：

-   Run期间批量执行Domain Resolve；
-   保存运行结果；
-   提供Pegging上下文。

------------------------------------------------------------------------

### 1号位

不负责：

-   Domain判断；
-   Domain规则解释。

------------------------------------------------------------------------

# 四、PI Position计算流程

正式流程：

    ERP/MES/ODS事实加载
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

------------------------------------------------------------------------

# 五、ProductionInstruction Supply模型

生产指示（ProductionInstruction）作为Supply。

位置快照：

ProductionInstructionPositionSnapshot

用于说明：

-   当前Stage；
-   XC；
-   跨厂状态；
-   AvailableTime。

------------------------------------------------------------------------

关系：

    ProductionInstruction
            |
            +---- PositionSnapshot

------------------------------------------------------------------------

禁止：

    PI Supply
    +
    XC Supply
    +
    Transit Supply

------------------------------------------------------------------------

# 六、跨厂处理

## 大工艺接续型跨厂

流程：

    PI Supply
    +
    INTERPLANT_TRANSIT Position

------------------------------------------------------------------------

## 厂间订单型

流程：

    SH Supply
    +
    履行状态

------------------------------------------------------------------------

SH运输、到达：

不创建新的Transit Supply。

------------------------------------------------------------------------

# 七、SupplyFact_Pipeline边界

当前V1：

承接外部未来供给事实：

-   采购在途；
-   未结PO；
-   已到厂未入库。

不承接：

-   PI Position；
-   PI Transit；
-   PI XC；
-   SH Transit；
-   SH Received。

------------------------------------------------------------------------

# 八、数量闭环检查

PI Position必须满足：

    Σ Position.Quantity = ERP PI RemainingQty

Position不得：

-   修改ERP剩余量；
-   重复扣减Received；
-   创建额外Supply。

------------------------------------------------------------------------

# 九、Candidate排程影响

白天Candidate保持：

-   单Domain；
-   不影响其他Domain ACTIVE计划。

本次专项不改变Candidate机制。

------------------------------------------------------------------------

# 十、冻结说明

V3.19不是新架构。

属于：

> APS V1冻结业务一致性修订。

后续代码实现必须以：

-   v3.19；
-   v1.3业务基线；
-   v1.3 Pegging文档；

作为统一依据。
