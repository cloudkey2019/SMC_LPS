# APS V1_0号位总体项目验收包 v1.6

**当前版本**：v1.6  
**当前版本日期**：2026-10-05（北京时间）  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》+《APS核心排产全流程走查 V3.25》  
**当前技术对齐基线**：《APS数据架构与防腐层设计方案 v1.45》+《APS集成接口设计 v1.34》+《APS数据库字段说明文档 v5.1.9》+《DDL v5.1.8.3》  
**当前状态**：正式变更态；待指导位Step 9复审，尚未冻结生效。


# 2026-10-05 本轮验收项同步

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。

本节只增加验收门槛，不新增业务设计。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 本轮新增验收门槛

- Task.Id、TaskNo、MESWorkOrderNo、ContinuationKey、ExecutionBatchDraftKey五种身份不得混用。
- 一个MES工单允许多Slice；一个ScheduleRun内一个MESWorkOrderNo只能有一个ContinuationKey。
- A/B固定真实Route/Path；C保留多Routing候选且FinalTask/Task/MES追溯保存实际Route/Path。
- B桶Solver前TaskNo为空，FinalTask后新TaskNo绑定原WO；仅C桶FinalTask后新建MES工单。
- PreferredResource链必须闭合：5号位LastReportResourceCode → 2号位Slice/Eligibility校验 → 1号位软偏好消费。
- FINAL不是MaterialType；普通Warehouse不得机械拆Material；FINAL不新增BOM、SupplyType或第二Material平台。
- 1号位零DB、5号位不重算Pegging/Batch/Solver、4号位不直接写核心运行DB。
- 接口、字段说明、DDL必须与上述业务语义一致；FINAL业务裁决已完成，验收必须验证“出口库/最终可销售层级 → `FINAL_`标准Material身份 → MaterialMapping → Inventory/PI/Order/Demand/Supply/Routing”技术链闭合，且不得自行新增`IsFinal/FinalFlag`等第二业务真相。


## 最终职责、流程、数据、接口联合冻结版

版本：v1.6\
适用对象：0号位项目管理、最终验收

------------------------------------------------------------------------

# 一、文档定位

本版本基于APS
V1最终冻结业务、数据、接口及1～5号位实施包，用于项目最终验收。

本版本不重新设计业务，仅同步最终实施边界：

-   业务流程闭环；
-   0～5号位职责；
-   数据责任；
-   接口责任；
-   功能验收范围。

------------------------------------------------------------------------

# 二、APS V1总体验收范围

APS V1验收包括：

1.  需求与供给承接；
2.  Pegging供需匹配；
3.  有限产能排程；
4.  Candidate计划调整；
5.  计划版本管理；
6.  复杂事实接入；
7.  用户查询与业务操作；
8.  权限与治理。

------------------------------------------------------------------------

# 三、最终职责验收

  能力             责任号位
  ---------------- ----------
  有限产能Solver   1号位
  排程运行主流程   2号位
  Pegging          2号位
  Allocation       2号位
  FinalTask生成       1号位
  FinalTask校验/持久化 2号位
  规则参数治理     3号位
  Auth/RBAC        3号位
  页面与用户操作   4号位
  复杂事实ODS      5号位
  PI Position      5号位

------------------------------------------------------------------------

# 四、核心业务闭环验收

## 4.1 排程主链

    需求输入
     ↓
    Supply构建
     ↓
    Pegging
     ↓
    LogicalProductionDemand
     ↓
    有限产能Solver
     ↓
    FinalTask
     ↓
    计划发布

------------------------------------------------------------------------

## 4.2 有限产能验收

必须支持：

-   设备Resource；
-   人工Resource；
-   Resource Calendar；
-   Setup换型影响；
-   Operation Planning Mode。

------------------------------------------------------------------------

## 4.3 Operation Planning Mode

支持：

  模式              含义
  ----------------- ----------------
  FINITE_RESOURCE   有限资源约束
  UNCONSTRAINED     非有限资源约束
  WAIT_ONLY         等待约束

------------------------------------------------------------------------

# 五、数据闭环验收

## Pegging

验收：

-   Demand/Supply数量闭合；
-   Allocation正确；
-   不存在第二套Pegging。

------------------------------------------------------------------------

## PI Position

验收：

-   5号位计算；
-   2号位消费；
-   不作为Supply。

------------------------------------------------------------------------

## 跨厂事实

验收：

-   SH跨厂事实正确；
-   不形成独立Supply模型。

------------------------------------------------------------------------

# 六、用户输出验收

系统应支持：

-   甘特图；
-   订单计划查询；
-   今日/未来任务；
-   延期原因；
-   Resource影响；
-   Setup影响；
-   Supply/Pegging追溯。

------------------------------------------------------------------------

解释责任：

    1号位产生
     ↓
    2号位保存
     ↓
    5号位查询
     ↓
    4号位展示

------------------------------------------------------------------------

# 七、Candidate验收

支持：

-   Candidate创建；
-   Candidate执行；
-   Candidate比较；
-   Base PlanVersion关联；
-   运行结果追溯。

职责：

-   3号位治理；
-   2号位执行；
-   4号位操作。

------------------------------------------------------------------------

# 八、接口验收原则

治理链：

    4号位
     ↓
    3号位
     ↓
    2号位

业务链：

    4号位
     ↓
    5号位
     ↓
    2号位

禁止：

页面绕过正式服务直接修改核心数据。

------------------------------------------------------------------------

# 九、最终冻结红线

不允许新增：

-   OperationSupply；
-   OperationPegging；
-   第二套Supply；
-   第二套Pegging；
-   SetupRuntime平台；
-   CandidateInput独立平台。

------------------------------------------------------------------------

# 十、最终验收结论

APS V1形成：

-   业务闭环；
-   数据闭环；
-   接口闭环；
-   职责闭环；
-   运行闭环。

后续开发、测试、代码审核均以本验收包及对应号位实施包作为依据。