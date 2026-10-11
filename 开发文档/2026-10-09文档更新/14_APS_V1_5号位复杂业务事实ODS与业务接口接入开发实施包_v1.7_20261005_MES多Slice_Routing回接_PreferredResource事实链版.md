# APS V1_5号位复杂业务事实ODS与业务接口接入开发实施包 v1.7

**当前版本**：v1.7  
**当前版本日期**：2026-10-05（北京时间）  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》  
**当前技术对齐基线**：《APS集成接口设计 v1.34》+《APS数据架构与防腐层设计方案 v1.45》  
**当前状态**：正式变更态；待指导位Step 9复审，尚未冻结生效。

> **勘误候选登记（2026-10-11 0号位 职责调整，待正式冻结回写与控制层收口）**：Routing ODS **事实**（Routing ODS 数据接入/同步、Routing Operation/Dependency/Eligibility 事实、真实 RouteCode/PathId 完整性、有效期与启停事实字段、数据质量）**Owner=3号位 特殊例外**，不属于本实施包 5号位 ODS 职责范围；5号位 继续承担 MES 执行事实与 Routing **回接**（ExistingExecution 工单级+Routing级+Slices[]）、多Slice、LastReportResourceCode、其余 ODS 及查询接口。本文件相关 Routing 契约表述（如 MaterialCode 标准化输出）属接口契约要求继续有效，不因 Owner 调整而改变。


# 2026-10-05 本轮复杂事实与Routing回接回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## FINAL Material标准化技术落地

- 5号位在ODS/防腐层按既有“出口库/最终可销售层级”源事实输出标准化Material身份：普通物料保持原标准MaterialCode，出口层级加`FINAL_`前缀。
- `FINAL_`不是MaterialType；下游类型解析必须Strip `FINAL_`后再按FG/WIP/RAW/ASSY原规则处理。
- 5号位不得依据Routing、OperationName、是否存在包装工序、路径长度等运行事实猜测FINAL。
- MaterialMapping保留SourceID与Warehouse/Location追溯；Inventory、PI、Order等出口层级事实必须使用同一FINAL标准Material身份。
- Routing Operation / Dependency / Eligibility ODS契约必须**显式输出与Material主数据同一套标准化MaterialCode**，同时保留MES_ID/Model作源追溯；下游以该MaterialCode进入MaterialMapping并绑定唯一MaterialId。不得只靠MES_ID区分普通/FINAL，也不得在Routing加载后再做FINAL反推。
- 不新增FINAL BOM层，禁止生成`FINAL_xxx → xxx`虚构BOM关系。

## 5号位本轮新增/替换要求

- ExistingExecution输出按“工单级 + Routing级 + `Slices[]`”组织；一个MESWorkOrder允许多个执行起点Slice。
- 每个可定位Slice至少提供ProductionDepartment、真实RouteCode、PathId、StageCode、StartOperationCode/Operation上下文及Operation级`LastReportResourceCode`。
- `LastReportResourceCode`只提供真实事实，不判断最终资源偏好；2号位负责Slice关联与Eligibility校验后形成PreferredResourceCode。
- APS历史下发工单优先从历史APS下发映射恢复真实Route/Path；外部人工MES工单按生产部门、Stage、工序顺序、Routing证据唯一匹配；不唯一时登记Issue，并按冻结Stage LeadTime规则降级，不得猜相似Operation。
- FINAL只消费上游已经标准化的Material身份；5号位不得通过Routing长度、包装工序、仓库猜测自行创造FINAL业务规则。
- 5号位不做A/B分类、不生成TaskNo、不计算Pegging/Batch/Solver，不写2号位运行结果真相。


## 复杂业务事实、ODS与业务接口联合冻结版

版本：v1.7\
适用对象：5号位及开发AI

------------------------------------------------------------------------

# 一、文档定位

本版本基于9.15冻结实施包及最终职责边界调整，用于指导5号位复杂业务事实、ODS、防腐层和业务接口开发。

本版本不重新设计APS业务模型，只同步：

-   复杂事实接入；
-   PI Position；
-   NextOperationContext；
-   Manual ETA；
-   Query接口；
-   前端业务中转。

------------------------------------------------------------------------

# 二、5号位最终职责

5号位负责：

-   复杂业务事实ODS；
-   外部系统事实接入；
-   PI Position计算；
-   NextOperationContext计算；
-   MES复杂事实标准化；
-   Manual ETA事实维护；
-   业务查询接口。

5号位不负责：

-   Pegging计算；
-   Allocation计算；
-   Solver计算；
-   FinalTask生成；
-   计划结果修改。

------------------------------------------------------------------------

# 三、PI Position职责边界

## 3.1 5号位负责

计算：

-   PI当前执行位置；
-   已完成Stage解释；
-   下一执行上下文。

输出：

-   Position信息；
-   StartStageCode；
-   StartOperationCode；
-   NextOperationContext。

------------------------------------------------------------------------

## 3.2 2号位负责

消费：

-   PI Position；
-   NextOperationContext。

用于：

-   LogicalProductionDemand；
-   Solver输入准备。

------------------------------------------------------------------------

## 3.3 禁止

禁止：

-   5号位生成Supply；
-   5号位计算Pegging；
-   5号位生成Task。

------------------------------------------------------------------------

# 四、MES复杂事实接入

5号位负责：

-   MES工序事实；
-   Stage事实；
-   报工事实标准化；
-   工序级LastReportResourceCode。

------------------------------------------------------------------------

边界：

5号位提供事实解释。

2号位负责运行消费。

------------------------------------------------------------------------

# 五、Manual ETA

## 5.1 Owner

Manual ETA维护归5号位。

包括：

-   人工调整；
-   ETA事实保存；
-   历史追溯。

------------------------------------------------------------------------

## 5.2 责任链

    5号位
    Manual ETA事实

    ↓

    2号位
    Effective ETA计算

    ↓

    Pegging/Scheduling
    消费

------------------------------------------------------------------------

# 六、Query接口建设

5号位向4号位提供业务查询接口。

包括：

-   Plan Overview；
-   Order Plan；
-   Gantt；
-   Explanation；
-   Supply Trace；
-   Pegging Trace；
-   Demand Protection查询；
-   MES查询。

------------------------------------------------------------------------

# 七、查询接口边界

5号位负责：

-   DTO转换；
-   查询聚合；
-   前端友好输出。

禁止：

-   重算Pegging；
-   重算Allocation；
-   修改计划结果。

------------------------------------------------------------------------

# 八、与2号位接口

5号位提供：

-   PI Position；
-   NextOperationContext；
-   MES复杂事实；
-   ODS事实；
-   ETA事实。

2号位提供：

-   运行结果；
-   计划事实；
-   Pegging结果。

------------------------------------------------------------------------

# 九、与3号位接口

5号位消费：

-   用户权限上下文；
-   Business Scope上下文。

不负责：

-   用户权限；
-   Role管理；
-   Candidate治理。

------------------------------------------------------------------------

# 十、跨厂事实边界

保持：

跨厂物流事实用于：

-   SH存在性判断；
-   物流追溯。

不形成：

-   INTERPLANT_IN_TRANSIT Supply；
-   独立Supply模型。

------------------------------------------------------------------------

# 十一、禁止事项

禁止：

-   复制2号位Pegging；
-   复制2号位Allocation；
-   直接修改核心排程表；
-   成为计划结果Owner；
-   建设第二套运行真相。

------------------------------------------------------------------------

# 十二、验收重点

必须验证：

1.  PI Position计算准确；
2.  NextOperationContext可被2号位消费；
3.  Manual ETA责任清晰；
4.  Query接口满足前端需求；
5.  不产生第二套业务真相；
6.  ODS事实与APS运行边界清晰。

------------------------------------------------------------------------

# 十三、最终冻结原则

5号位定位：

> APS V1复杂业务事实与业务查询能力中心。

5号位负责解释事实、提供接口，不改变APS运行真相。