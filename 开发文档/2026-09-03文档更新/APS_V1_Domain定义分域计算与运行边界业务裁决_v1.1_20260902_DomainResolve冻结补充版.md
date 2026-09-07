# APS V1 Domain定义分域计算与运行边界业务裁决 v1.1

## 20260902 Domain Resolve冻结补充版

> 本文件为Domain专项冻结修订版。基于原v1.0，仅补充DomainDefinition、Domain
> Resolve与运行边界闭环，不改变原有Domain业务原则。

------------------------------------------------------------------------

# 一、修订范围

本版本只补充：

1.  排程域定义（DomainDefinition）治理边界；
2.  ScheduleRun启动时的配置冻结；
3.  Domain Resolve运行流程；
4.  Order / Demand DomainKey生命周期。

不新增：

-   DomainRuntime平台；
-   DomainContext持久化平台；
-   新算法；
-   新业务域。

------------------------------------------------------------------------

# 二、DomainDefinition治理

## 1. 定位

DomainDefinition属于APS治理配置。

负责定义：

-   当前有效排程域；
-   Domain规则；
-   Domain依赖关系。

------------------------------------------------------------------------

## 2. 职责

3号位负责：

-   DomainDefinition维护；
-   权限控制；
-   配置审计；
-   Run启动时配置语义冻结。

------------------------------------------------------------------------

# 三、Domain Resolve运行闭环

正式流程：

    ScheduleRun启动
    ↓
    冻结ExpectedDomainKeysJson
    ↓
    冻结本Run使用的DomainDefinition配置语义
    ↓
    2号位批量执行Domain Resolve
    ↓
    生成Order / Demand DomainKey
    ↓
    进入Pegging

------------------------------------------------------------------------

# 四、DomainKey生命周期

Order.DomainKey：

表示：

> 当前ScheduleRun中APS归域后的运行结果。

不是：

-   ERP字段；
-   永久主数据；
-   ProductFamily.Code别名。

------------------------------------------------------------------------

规则：

-   新ScheduleRun可以依据新的DomainDefinition重新计算；
-   当前Run内必须保持一致；
-   Demand继承Order归域结果。

------------------------------------------------------------------------

# 五、禁止事项

## 1. ERP直接提供DomainKey

禁止：

    ERP Order
       |
       + DomainKey

------------------------------------------------------------------------

## 2. ProductFamily.Code替代DomainKey

禁止：

    ProductFamily.Code
            |
            ↓
    DomainKey

原因：

ProductFamily属于业务分类，不等价于排程域。

------------------------------------------------------------------------

## 3. Material永久绑定Domain

禁止：

    Material
       |
     Domain

原因：

同一Material可能服务多个Domain。

------------------------------------------------------------------------

## 4. Supply永久绑定Domain

禁止：

    Supply
       |
     Domain

原因：

Domain归属属于Demand运行上下文。

------------------------------------------------------------------------

# 六、失败处理

以下情况属于运行一致性错误：

1.  无法匹配DomainDefinition；
2.  DomainKey不属于ExpectedDomainKeysJson；
3.  当前Run配置语义漂移。

处理：

-   FULL排程失败；
-   Candidate失败；
-   不允许生成不确定计划。

------------------------------------------------------------------------

# 七、与号位职责关系

## 3号位

负责：

-   DomainDefinition治理。

不负责：

-   订单实时归域；
-   Pegging；
-   Solver。

------------------------------------------------------------------------

## 2号位

负责：

-   在Pegging前批量执行Domain Resolve。

不负责：

-   创建Domain规则；
-   修改DomainDefinition。

------------------------------------------------------------------------

## 1号位

只消费：

-   已完成Domain归属后的单Domain求解上下文。

不解释Domain。

------------------------------------------------------------------------

# 八、冻结说明

本修订不改变：

-   Demand排序；
-   Pegging；
-   Allocation；
-   有限产能Solver；
-   PlanVersion机制。

仅补齐Domain运行闭环。
