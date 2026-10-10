# APS V1 Supply_PI_Position专项冻结收口确认单 v1.0

## 一、目的

本确认单用于记录 APS V1 本轮 Supply / PI Position 与
Domain专项冻结修订后的最终收口状态，作为后续开发、代码审核和文档维护基线。

------------------------------------------------------------------------

## 二、本轮修订范围

涉及文档：

-   数据库字段说明文档 v5.1.5
-   数据库表结构设计 DDL v5.1.5
-   数据架构与防腐层设计方案 v1.40
-   集成接口设计 v1.29
-   APS V1最终全部流程与业务基线 v1.3
-   APS核心排产全流程走查 V3.19
-   1号位实施包 v1.1
-   2号位实施包 v1.4
-   3号位实施包 v1.3
-   4号位实施包 v1.3
-   5号位实施包 v1.2

------------------------------------------------------------------------

## 三、已冻结核心结论

### 1. Position与Supply边界

冻结：

-   Supply表示可被Demand直接承接的供给事实。
-   Position表示已有Supply内部执行位置、状态或数量分布解释。
-   Position不是独立Supply。
-   Position不参与Supply排序。
-   Position不单独产生Allocation。

------------------------------------------------------------------------

### 2. ProductionInstruction规则

冻结：

-   ProductionInstruction继续作为Supply参与Pegging。
-   ProductionInstructionPositionSnapshot用于表达PI执行位置事实。
-   Position不得替代ProductionInstruction Supply。
-   不允许通过Position重新计算PI总剩余数量。

------------------------------------------------------------------------

### 3. 跨厂规则

冻结：

厂间订单型： - 使用跨厂Supply表达。

大工艺接续型： - ProductionInstruction Supply； -
PositionType=INTERPLANT_TRANSIT。

不建设Transit Supply模型。

------------------------------------------------------------------------

### 4. Domain边界

冻结：

-   DomainKey作为APS真实稳定排程域标识。
-   DomainDefinition负责Domain治理。
-   ScheduleRun、PlanVersion关联真实Domain。

V1不建设：

-   DomainRuntime；
-   DomainContext；
-   VirtualInventoryBalance；
-   Domain资源配额平台；
-   Domain DSL。

------------------------------------------------------------------------

## 四、职责边界冻结

### 5号位

负责： - ERP/MES事实； - ODS； - 接口数据。

不负责： - APS业务计算。

### 2号位

负责： - 消费事实； - Position解释； - 排程运行闭环。

不负责： - 创建Supply事实。

### 3号位

负责： - DomainDefinition； - 规则治理。

### 1号位

负责： - 排程求解； - 结果消费。

### 4号位

负责： - 页面展示； - 操作入口。

------------------------------------------------------------------------

## 五、禁止重新打开事项

除非出现冻结文档明确冲突，不重新讨论：

-   Position是否作为Supply；
-   Transit Supply是否建设；
-   DomainRuntime类扩展；
-   DomainContext平台化设计；
-   未冻结模型扩展。

------------------------------------------------------------------------

## 六、后续代码审核基线

后续代码审核必须同时依据：

1.  最新业务基线；
2.  最新数据模型；
3.  最新接口设计；
4.  对应号位实施包。

不得基于旧版本文档恢复已关闭设计。
