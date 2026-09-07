# APS V1最终全部流程与业务基线 v1.3

## 20260902 双专项冻结修订版

> 说明：本文件为本轮Supply / PI Position与Domain
> Resolve专项合并后的业务基线修订稿框架。正式替换项目来源中的同名文件时，应以原v1.2正文为母版，仅合并本文件明确的增量内容，禁止删除原冻结章节。

------------------------------------------------------------------------

# 一、本次修订范围

本版本仅吸收两项已冻结专项：

1.  排程域归域（Domain Resolve）运行闭环补充。
2.  Supply身份与ProductionInstruction Position边界澄清。

除上述两项外，原APS V1冻结业务保持不变。

------------------------------------------------------------------------

# 二、Domain Resolve冻结口径

## 1. DomainDefinition

排程域定义（DomainDefinition）属于APS治理配置。

职责：

-   由3号位治理；
-   在ScheduleRun启动阶段确定本次运行使用的配置语义；
-   运行期间保持一致。

------------------------------------------------------------------------

## 2. Domain Resolve

正式流程：

    ScheduleRun启动
    ↓
    冻结本Run DomainDefinition配置语义
    ↓
    2号位批量执行Domain Resolve
    ↓
    形成Order / Demand DomainKey
    ↓
    进入Pegging

约束：

-   ERP/ODS同步阶段不得生成DomainKey；
-   ProductFamily.Code不得作为DomainKey替代；
-   Material不永久绑定Domain；
-   Supply不永久绑定Domain。

------------------------------------------------------------------------

# 三、Supply与Position边界

## 1. Supply定义

Supply表示可以被Demand直接承接的供给身份。

包括：

-   库存；
-   ProductionInstruction（生产指示）；
-   出荷指示；
-   正式外部供给事实。

------------------------------------------------------------------------

## 2. ProductionInstruction Position

ProductionInstruction本身是Supply。

ProductionInstructionPositionSnapshot用于描述：

-   当前执行位置；
-   状态；
-   数量分布；
-   AvailableTime。

Position不是Supply。

------------------------------------------------------------------------

关系：

    ProductionInstruction Supply
            |
            +---- ProductionInstructionPositionSnapshot

------------------------------------------------------------------------

# 四、数量闭环

PI剩余数量边界：

以ERP ProductionInstruction RemainingQty为准。

Position仅解释：

"剩余数量在哪里"。

必须满足：

    Σ Position.Quantity = ERP PI RemainingQty

禁止：

-   Position重新计算PI剩余数量；
-   Transit重复增加数量；
-   Received重复扣减数量。

------------------------------------------------------------------------

# 五、跨厂事实边界

## 大工艺接续型跨厂

保持：

    ProductionInstruction Supply
    +
    INTERPLANT_TRANSIT Position

禁止：

    ProductionInstruction Supply
    +
    Transit Supply

------------------------------------------------------------------------

## 厂间订单型

出荷指示（SH）保持单一Supply身份。

运输中、到达待处理属于履行状态。

禁止拆分：

-   Transit Supply；
-   Received Supply。

------------------------------------------------------------------------

# 六、V1职责边界

## 2号位

负责：

-   ScheduleRun编排；
-   Domain Resolve运行；
-   Pegging；
-   Position Snapshot保存与消费。

不负责：

-   MES事实解释；
-   Position规则计算。

------------------------------------------------------------------------

## 5号位

负责：

-   ODS事实；
-   PI Position计算规则。

不负责：

-   Allocation；
-   Task生成；
-   运行快照保存。

------------------------------------------------------------------------

## 1号位

继续只负责有限产能Solver。

不感知：

-   DomainDefinition；
-   Position；
-   Supply事实解析。

------------------------------------------------------------------------

# 七、版本原则

本版本不是重新设计APS V1。

属于：

"冻结边界澄清与文档一致性修订"。

原有：

-   Demand排序；
-   Pegging规则；
-   Demand Protection；
-   有限产能求解；
-   Candidate机制；

全部保持冻结。
