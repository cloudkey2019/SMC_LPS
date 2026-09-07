# APS V1 5号位复杂业务事实、ODS与业务接口接入开发实施包（冻结对齐版）

**版本**：v1.1  
**日期**：2026-09-01（北京时间）  
**适用对象**：5号位及其开发AI  
**文档性质**：在《APS V1 5号位复杂业务事实与ODS开发实施包 v1.0（2026-08-14）》基础上的职责与接口增量修订版  

**上位依据**：

- 当前APS V1最终业务冻结基线；
- 《APS Pegging供需承接与分层计算业务说明》当前冻结版；
- 《APS 有限产能排产与滚动90天计划业务说明》当前冻结版；
- 《APS 集成接口设计规范 v1.28（20260901职责与前端接口冻结对齐版）》；
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》；
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》；
- 《APS V1 4号位页面与业务操作开发实施包 v1.2（20260901 Domain专项冻结对齐版）》。

---

# 0. 本版变更控制

本版不是重新设计5号位，而是在原v1.0基础上只做已经冻结的增量修正。

原v1.0以下核心职责全部继续保留：

1. BOM入口和Workset复杂派生；
2. PI Position（生产指示位置）；
3. MES工序/大工艺进度事实标准化；
4. 跨厂模式相关复杂事实；
5. 采购、VMI、已到厂未入库、厂间在途等供应事实；
6. Received按单据号等强事实；
7. ProcessCode / ERPProperty等ODS防腐事实；
8. 数据问题/异常Issue识别与返回；
9. 5号位不做Pegging、不做Allocation、不做Solver、不做FinalTask、不做最终Explanation ReasonCode裁决。

本版新增/修正：

1. 5号位新增面向4号位的**非治理类APS业务接口接入/中转层**职责；
2. 4号位非治理业务统一通过5号位访问，不再直接调用2号位HTTP/API；
3. 5号位必须消费3号位提供的身份、功能权限和业务范围上下文；
4. 5号位后端必须校验Factory / ProductFamily / Department / Domain业务范围；
5. G4 / G7 / G8 / Schedule普通结果查询逐步由3号位迁移到5号位；
6. Manual ETA事实/API正式归5号位，4号位通过5号位维护；
7. **AvailableTime最终计算责任正式归2号位**，修正原v1.0中“5号位/ODS尽量算Effective ETA / AvailableTime”的旧表述；
8. `ProcurementManualEtaOverride`及5号位自属事实结构的DDL/Repository/Service/API归5号位Owner；
9. 旧“全部数据库DDL都由2号位执行”的泛化理解废止；
10. 5号位的“中转权”不得扩张为对2号位业务的重新实现。

**本版不得借前端接口调整重新设计Pegging、Demand Protection、CrossFactory、AvailableTime、Candidate、PlanVersion、MES下发或有限产能业务。**

---

# 一、5号位在APS V1中的最新定位

5号位仍然不是“规则插件中心”，也不是Pegging执行器。

原核心定位继续成立：

> **把ERP/MES/跨厂/采购等复杂、易变、需要业务知识解释的源事实，整理成2号位可以稳定消费的标准事实。**

2026-09-01后，5号位增加第二个技术角色：

> **作为4号位非治理类APS业务的统一接口接入/中转层。**

因此，5号位V1职责正式分成两条线。

## 1.1 复杂业务事实线

负责：

- BOM防腐与Workset；
- PI Position；
- MES进度事实；
- 跨厂复杂事实；
- Procurement / VMI / Received / Transit；
- ODS契约；
- ERPProperty；
- Manual ETA事实；
- BusinessFactIssue。

## 1.2 面向4号位业务接口接入线

负责：

- Overview；
- Order/需求计划；
- Gantt；
- Explanation；
- Supply/Pegging Trace；
- PI Position；
- Manual ETA；
- Demand Protection查看；
- Demand Protection释放中转；
- MES资格/状态；
- MES受控操作中转；
- G4 Run列表；
- G7 Domain依赖；
- G8 Domain状态；
- Schedule普通结果查询；
- 其它非治理类APS业务查询/受控操作。

一句话：

> **5号位负责“事实算清楚 + 非治理业务接口接进来”，2号位负责“怎么分、怎么算数量、怎么形成正式运行结果”，1号位负责“怎么排”。**

---

# 二、5号位明确不负责

即使新增前端业务接口接入职责，以下内容仍然不属于5号位：

- Demand最终排序；
- Supply最终挑选顺序；
- DemandBalance；
- SupplyBalance；
- Allocation数量扣减；
- Demand Protection实际数量创建、消费和释放逻辑；
- Strict Binding最终执行；
- CrossFactory主流程决策；
- Effective ETA最终裁决；
- AvailableTime最终计算；
- LogicalProductionDemand；
- Task生成；
- FinalTask；
- 有限产能Solver；
- ScheduleRun主流程编排；
- PlanVersion核心运行真值；
- Candidate求解；
- Candidate激活条件裁决；
- Explanation最终ReasonCode裁决；
- MES正式下发业务资格裁决；
- APS运行结果最终持久化；
- Authentication / RBAC / Business Scope规则维护；
- RuleSet / ParameterSet / StrategyProfile治理。

新增接口层后尤其禁止：

> **因为Controller在5号位，就把2号位业务复制到5号位。**

---

# 三、总体开发原则

## 3.1 复杂事实前置，主流程保持稳定

只有满足以下条件的逻辑放到5号位复杂事实层：

- 源系统字段复杂；
- 需要ERP/MES业务知识解释；
- 未来可能随源系统变化；
- 放进2号位会污染Pegging主流程。

例如：

- “先选PI再消费Position”属于2号位；
- “该PI当前剩余量分别位于XC/Stage/Transit多少”属于5号位。

---

## 3.2 事实输出与业务中转严格分层

### 事实层可以形成

- Position；
- SourceDocument；
- WarehouseRole；
- ERP ETA；
- ReleaseDate；
- Manual ETA；
- StrongEvidence；
- Issue；
- CrossFactoryMode Fact；
- Procurement Fact。

### 接入层可以做

- HTTP Controller；
- 身份/权限上下文接入；
- Scope校验；
- Query DTO；
- 页面聚合；
- 调用2号位Query Service；
- 调用2号位Application Service；
- 将结果转换为4号位稳定DTO。

### 接入层不得做

- 重算Pegging；
- 重算Allocation；
- 重算Demand Protection；
- 重算AvailableTime；
- 改Resource/Start/End；
- 重建FinalTask；
- 重做ScheduleRun状态机。

---

## 3.3 尽量批量，不逐笔远程调用

原v1.0原则继续有效。

夜间全量目标约10万Task，不允许：

```text
2号位每处理一笔Demand
→ RPC调用5号位一次
```

复杂事实优先：

- 批量SP；
- 批量视图；
- 批量事实DTO；
- 一次装载2号位内存。

前端查询也要避免：

```text
一个页面1000行Task
→ 5号位对2号位发1000次调用
```

应使用：

- 服务端分页；
- 批量Query；
- 一次聚合；
- 按页面/订单/版本/Domain范围请求。

---

# 四、5号位正式交付模块

在原六个模块基础上新增一个接口接入模块：

## 模块A：BOM防腐与Workset

## 模块B：PI Position Calculator

## 模块C：MES进度事实标准化

## 模块D：跨厂事实

## 模块E：采购 / VMI / Timed Supply事实

## 模块F：ODS契约、Manual ETA与Issue治理

## 模块G：面向4号位的非治理业务接口接入/中转层

不要求拆成七个服务进程，可按现有工程结构组织。

---

# 五、模块A：BOM防腐与Workset

原v1.0冻结内容保持。

## 5.1 正式输入

来自：

- ERP/MES BOM源；
- `MES_BOM_Edge_Active`；
- `MES_ProcessCode_View`；
- StageDict；
- RequestDetail；
- OrderType；
- MaterialCode；
- FactoryCode；
- RequestedBOMNO。

## 5.2 正式链路

```text
源BOM
→ MES_BOM_Edge_Active
→ sp_ExpandBOMBatch_vNext / sp_ExpandBOMRealtime_vNext
→ MES_APS_BOM_Workset
→ StageDetail
→ CrossFactoryEdge
→ APS RAW
```

5号位负责ODS侧：

- `sp_RefreshBOMEdgeActive`
- `sp_ExpandBOMBatch_vNext`
- `sp_ExpandBOMRealtime_vNext`
- `sp_EnrichBOMWorkset`
- `sp_EnrichBOMWorksetRealtime`
- CrossFactoryEdge相关派生
- Issues登记

2号位负责：

- 发起Request；
- 搬运/消费进入APS主流程；
- OrderBomRequestLink；
- PlanVersion运行内消费。

## 5.3 BOMNO为空

继续使用已冻结入口解析逻辑。

5号位可以使用：

- OrderType；
- MaterialCode；
- FactoryCode；
- ProcessCode；
- ERPProperty；
- BOM边；

解析入口。

只返回：

- ResolvedBOMNO；
- Workset；
- Issue。

不返回订单优先级或Pegging决策。

---

# 六、模块B：PI Position Calculator

PI Position仍是5号位V1核心能力之一。

## 6.1 业务目标

给定PI的ERP RemainingQty：

> 5号位回答这批剩余量当前分别位于哪个Stage、XC、在途、等待或UNLOCATED。

必须满足：

```text
Σ PositionQty = ERP RemainingQty
```

## 6.2 最高数量红线

> **PI RemainingQty由ERP定义，是该PI尚未最终进入目标M库的全部剩余数量。**

禁止：

```text
ERP RemainingQty
+ MES WIP
+ XC
+ Transit
```

MES、XC、Transit只解释位置，不增加总量。

## 6.3 PositionType

至少支持：

- `STAGE`
- `XC`
- `INTERPLANT_IN_TRANSIT`
- `WAITING`
- `UNLOCATED`

现有DDL若有兼容枚举，可以映射，不要求重建。

## 6.4 Stage累计异常

下游完成量 > 上游完成量：

> 保守下修下游有效完成量，不制造数量。

## 6.5 中间Stage缺失

采用：

> 下游已证明的最小完成量。

## 6.6 强事实校正

XC、Transit、Received等强事实可以修正规划位置解释，但：

- 不回写MES；
- 不篡改ERP；
- 不改变PI RemainingQty。

## 6.7 Position互斥

同一物理份额不能同时出现在：

- Stage；
- XC；
- Transit。

## 6.8 UNLOCATED

无法可靠定位但总量可闭合：

> 剩余进入UNLOCATED。

后续由2号位按冻结规则保守形成计划需求。

## 6.9 严重Issue

至少：

- 总量无法闭合；
- 同一物理份额重复；
- PI/Material/Factory主键冲突；
- 强事实数量超过RemainingQty且不可解释。

是否使Domain失败由2号位决定。

---

# 七、PI Position持久化边界

原“最小快照”思想保留。

不建设：

- PI Header + Slice双表生命周期平台；
- Position事件溯源平台；
- Position独立工作流。

数据库Owner原则按最新裁决执行：

- 5号位Own自身PI Position事实/ODS事实结构；
- 如某快照属于2号位PlanVersion运行事实，则由2号位Own；
- 不再机械套用“所有DDL都归2号位”。

跨Owner修改必须评审。

---

# 八、模块C：MES进度事实标准化

继续保留：

- `MESWorkOrderSnapshot`
- `OperationProgressSnapshot`
- `StageProgressSnapshot`

目标：

> 将MES原始/汇总事实转成PI Position和APS复杂事实稳定输入。

## 8.1 MES五态

继续使用：

- 0 待开工；
- 1 开工中；
- 2 完工报工；
- 3 未完工报工；
- 4 未完工报工已完结（手动完工）。

禁止新增：

- PAUSED；
- SUSPENDED；
- RESUME。

## 8.2 历史Task

MES进度不依赖历史APS Task作为事实真值。

Task版本可以变化，MES物理生产事实不能因此丢失。

---

# 九、模块D：跨厂事实

两类跨厂业务继续保持冻结：

## 9.1 大工艺接续型（Cross-stage / Process Continuation）

5号位负责提供：

- Stage跨厂边；
- SourceFactory；
- TargetFactory；
- PI对应Transit；
- ERPProperty；
- Warehouse/Process位置事实；
- TransportLT；
- InspectionLT；
- TransferLT；
- Issue。

不生成：

- ShippingTask；
- Logistics Solver；
- 车辆/月台Task。

## 9.2 厂间订单型（Inter-factory Order）

5号位标准化：

- SH号；
- Material；
- SourceFactory；
- ReceivingFactory；
- SH总量/剩余；
- Transit；
- 同SH的Received；
- ZP/BP事实；
- Document关系。

### Received最高红线

Received只能按已冻结单据关系匹配：

> **同一个SH号。**

禁止按“同物料”串用。

## 9.3 Transit / Received去重

同一物理份额到货后，不得继续同时留在Transit。

应提供稳定的：

- PhysicalSourceKey；
- SourceDocument；
- SourceLine（适用时）；
- SourceUpdatedAt / 状态；

支持2号位去重和消费。

## 9.4 跨厂最终业务决策

5号位只提供事实。

以下仍属于2号位：

- 跨厂模式在主流程中的最终消费；
- Supply选择；
- Allocation；
- 下游Quantity-Time；
- Pegging。

---

# 十、模块E：采购 / VMI / Timed Supply事实

V1必须真实提供：

- 已到厂未入库；
- 采购在途；
- 正式PO剩余；
- VMI；
- 其它冻结的供应事实。

不能固定返回空集合。

## 10.1 标准事实字段

至少能够稳定提供：

- SupplyType
- PhysicalSourceKey
- MaterialId
- FactoryId
- WarehouseCode / StorageCode映射
- RemainingQty
- ERP ETA
- ReleaseDate
- SourceDocumentNo
- SourceDocumentLineNo（适用时）
- SourceUpdatedAt
- Commitment相关原始事实
- 来源系统标识

## 10.2 ReleaseDate

ReleaseDate为ERP原始发行/创建日期：

- ODS必须透传；
- 5号位不得自行猜测；
- 2号位用于ETA缺失时计算。

## 10.3 WarehouseCode

如果源字段是StorageCode：

> 按已冻结映射透出WarehouseCode语义。

不得重新创造新的仓库判定体系。

---

# 十一、Manual ETA与AvailableTime正式边界

这是对原v1.0最重要的后续冻结修正之一。

## 11.1 ERP ETA

ERP ETA：

> 原始供应事实。

5号位/ODS负责保持原值，允许为空。

不得被APS人工修改覆盖源值。

## 11.2 Manual ETA

Manual ETA：

> APS人工维护的采购ETA覆盖事实。

由5号位负责：

- `ProcurementManualEtaOverride`结构；
- Repository；
- Service；
- API；
- 查询；
- 新增；
- 修改；
- 取消/失效；
- 审计所需事实。

4号位通过5号位API维护。

## 11.3 有效ETA优先级

冻结顺序：

```text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

## 11.4 最终责任

**5号位不负责最终Effective ETA和AvailableTime计算。**

正式链路：

```text
ERP/ODS原始事实
        ↓
5号位Manual ETA事实
        ↓
2号位生成/装载SupplyFact_Pipeline
        ↓
2号位计算Effective ETA
        ↓
2号位计算AvailableTime
        ↓
Pegging
```

因此原v1.0中类似：

> “5号位/ODS事实层尽量把Effective ETA算好”

的表述正式失效。

## 11.5 AvailableTime

AvailableTime属于APS最终可用时间事实，由2号位计算。

5号位可以返回：

- ERP ETA；
- ReleaseDate；
- Manual ETA；
- 计算所需源事实。

不能把自己推导的AvailableTime作为2号位必须接受的最终真值。

---

# 十二、Planning-only Purchase Placeholder

继续严格保持：

当真实采购/VMI/库存等无承诺供给时：

> 2号位可以在运行内存形成Planning-only Purchase Placeholder。

5号位不得：

- 生成Placeholder正式表；
- 把Placeholder落成正式Procurement SupplyFact；
- 创建PO；
- 创建采购Task；
- 向ERP写采购承诺。

Placeholder必须由2号位标记：

- ESTIMATED；
- NOT_COMMITTED。

---

# 十三、模块F：ODS契约与Issue治理

5号位继续Own复杂事实相关ODS契约。

包括但不限于：

- BOM相关；
- ProcessCode / ERPProperty；
- Stage；
- MES进度；
- Procurement；
- VMI；
- Received；
- Transit；
- PI Position源事实；
- Manual ETA自属APS事实。

## 13.1 ERPProperty

`ERPProperty`必须来自ERP真实业务属性，由5号位同步/透出。

禁止：

- 根据ProcessName猜；
- 根据WarehouseRole猜；
- 用临时硬编码替代真实ERP属性。

## 13.2 StageDict / StageCode

StageDict属于MES_Integration/ODS事实体系。

5号位应：

- 保持StageDict、MES_ProcessCode_View.StageCode等源事实契约稳定；
- 向消费方提供可解释的Stage事实；
- 不承担2号位`MaterialStageDeptContext`最终主流程Owner职责。

## 13.3 Issue

Issue至少有：

- ERROR
- WARN
- INFO

Issue不等于整个ScheduleRun失败。

5号位只报告事实问题和严重程度，2号位根据业务闭环判断是否Fail Domain。

---

# 十四、模块G：面向4号位的业务接口接入/中转层

这是v1.1新增的正式技术职责。

## 14.1 总原则

```text
4号位
   ↓
5号位HTTP/API
   ↓
┌───────────────┬────────────────┐
│ 5号位自属事实 │ 2号位内部服务   │
└───────────────┴────────────────┘
```

5号位对4号位提供稳定业务API，但：

> **接口Owner不等于底层业务Owner。**

---

# 十五、5号位直接Own并可对4号位提供的接口

以下是5号位自属事实：

## 15.1 PI Position

```text
4 → 5
```

5号位直接查询自己的PI Position事实/服务。

## 15.2 Manual ETA

```text
4 → 5
```

5号位负责查询、维护、取消。

## 15.3 ODS/复杂事实Issue

```text
4 → 5
```

用于Explanation辅助、事实异常展示。

## 15.4 Procurement / VMI / Received / Transit原始追溯

若页面需要查看源事实：

```text
4 → 5
```

5号位可以直接提供。

---

# 十六、5号位中转2号位业务的接口

以下页面虽由5号位提供HTTP/API，但业务真值属于2号位：

## 16.1 Overview

```text
4
→ 5 Overview Controller
→ 2号位 Query Service
```

5号位可做页面DTO聚合，但不重新计算：

- Delayed；
- Unscheduled；
- Active Plan；
- Domain运行结果；
- Bottleneck业务真值。

## 16.2 Order / Demand Plan

```text
4
→ 5
→ 2号位 Order/Plan Query Service
```

5号位不重算Pegging。

## 16.3 Gantt / Resource Plan

```text
4
→ 5
→ 2号位 Plan Query Service
```

只展示已持久化FinalTask。

5号位不得修改：

- Resource；
- Start；
- End。

## 16.4 Explanation

```text
4
→ 5
→ 2号位 Explanation Query
```

5号位可以补充自己的BusinessFactIssue，但不得覆盖2号位/1号位已形成的正式ReasonCode。

## 16.5 Supply/Pegging Trace

```text
4
→ 5
→ 2号位 Query Service
```

5号位可补充源事实详情，但：

- Allocation；
- Demand-Supply关系；
- 锁定结果；

以2号位结果为准。

---

# 十七、Demand Protection接口边界

## 17.1 查看

```text
4 → 5 → 2号位Query Service
```

5号位只负责查询中转和DTO。

## 17.2 释放

正式链路：

```text
4号位
→ 5号位API
→ 5号位权限/Scope校验
→ 2号位Demand Protection Application Service
→ 2号位执行实际释放
```

5号位不得：

- 自己算释放数量；
- 自己改Lock；
- 自己改Allocation；
- 直接UPDATE Demand Protection核心表；
- 绕过2号位业务条件。

---

# 十八、MES资格与受控操作接口边界

## 18.1 查询

```text
4 → 5 → 2号位Query Service
```

可展示：

- EligibleForMES；
- 不可下发原因；
- MES当前状态。

## 18.2 受控操作

```text
4
→ 5
→ 权限/Scope校验
→ 2号位MES Application Service
```

5号位不自行裁决MES业务资格。

即使用户拥有权限，如果Task业务条件不合法，2号位仍必须拒绝。

---

# 十九、G4 / G7 / G8 / Schedule普通查询迁移

此前3号位已有部分只读封装。

正式迁移至5号位：

- G4：Run列表；
- G7：Domain依赖；
- G8：Domain状态；
- Schedule普通计划结果查询。

## 19.1 迁移原则

- 优先保持现有URL兼容；
- 优先保持DTO兼容；
- 3号位停止新增同类查询；
- 5号位读取2号位事实或调用2号位Query Service；
- 不在5号位新建运行结果镜像表；
- 不允许3和5长期维持两套不同业务语义。

## 19.2 Run治理与普通查询分开

普通查询：

```text
4 → 5
```

治理动作：

```text
4 → 3
```

例如：

- Run列表 → 5；
- FAILED恢复 → 3；
- Candidate确认/激活 → 3。

---

# 二十、认证与权限执行边界

5号位不是Authentication/RBAC Owner。

3号位负责：

- Login；
- Refresh；
- Logout；
- Current User；
- User；
- Role；
- Permission；
- Business Scope；
- Auth Audit。

5号位负责：

> **消费3号位签发/提供的身份与权限上下文，并在自己的业务API执行后端授权。**

---

# 二十一、5号位后端必须实施的权限校验

至少校验两层：

## 21.1 功能权限

例如：

- `aps.plan.view`
- `aps.manual_eta.view`
- `aps.manual_eta.edit`
- `aps.manual_eta.cancel`
- `aps.demand_protection.view`
- `aps.demand_protection.release`
- `aps.mes.view`
- `aps.mes.dispatch`

具体Permission Code以APS_Auth当前冻结定义为准。

## 21.2 业务范围权限

至少：

- Factory
- ProductFamily
- Department / ProductionDepartment
- Domain

## 21.3 组合规则

同一维度允许并集。

不同维度共同限制：

```text
Factory in {BJ,TJ}
AND Department = MC
```

## 21.4 安全红线

禁止：

```text
数据库查询全量
→ 返回浏览器
→ 4号位前端自己过滤
```

必须在5号位或下游Query Service层将Scope带入查询。

---

# 二十二、5号位与2号位内部服务边界

2号位退出4号位HTTP层后，应向5号位提供稳定的：

- Query Service；
- Application Service；
- Domain Service（必要时）；
- 内部Contract/DTO。

5号位不得：

- 直接依赖2号位数据库表结构作为唯一接口；
- 通过跨库UPDATE代替Application Service；
- 复制2号位Domain逻辑；
- 为图方便绕过2号位校验。

允许：

> 对成熟稳定的只读事实，在接口冻结明确允许时采用受控查询方式。

但优先采用正式Query Service。

---

# 二十三、数据库Owner

旧的：

> “所有APS DDL都统一由2号位执行”

不再成立。

## 23.1 5号位Own

- ODS复杂事实结构；
- 5号位自属View/SP；
- PI Position自属事实结构；
- `ProcurementManualEtaOverride`；
- 5号位自属Issue事实结构。

## 23.2 5号位不Own

- DemandBalance；
- SupplyBalance；
- PeggingSupplyAllocation；
- PlanVersion；
- ScheduleRun运行结果；
- FinalTask；
- Demand Protection核心运行结构；
- APS_Auth。

## 23.3 中转接口不新建第二套运行表

尤其禁止为了前端：

- `UiPlanTaskCopy`
- `FrontendScheduleRun`
- `GatewayAllocation`
- `CachedDemandProtectionTruth`

之类第二真相表。

缓存如因性能确有需要，也不能成为业务真值，且本版不要求额外建设缓存平台。

---

# 二十四、错误处理

5号位需区分：

## 24.1 复杂事实Issue

- ODS缺字段；
- 源事实异常；
- PI Position不闭合；
- 单据关系异常；
- ERPProperty缺失。

## 24.2 权限错误

- 401：未认证；
- 403：无功能权限或越出业务Scope。

## 24.3 下游业务拒绝

例如：

- Demand Protection不可释放；
- MES Task不可下发；
- PlanVersion状态不允许操作。

此类业务拒绝应原样转译为稳定业务错误，不要变成“5号位内部异常”。

---

# 二十五、接口DTO原则

面向4号位应提供业务DTO，而不是数据库表裸字段。

建议包括：

- PlanOverviewDto
- OrderScheduleDetailDto
- GanttTaskDto
- ExplanationViewDto
- PiPositionViewDto
- SupplyTraceDto
- ManualEtaDto
- DemandProtectionViewDto
- MesEligibilityDto
- RunStatusDto
- DomainDependencyDto
- DomainStatusDto

具体命名可以沿用现有代码，不要求为了文档改名。

原则：

> **语义稳定优先于命名统一。**

---

# 二十六、原F01～F22事实验收继续有效

原v1.0测试场景继续保留：

| 编号 | 场景 | 必须结果 |
|---|---|---|
| F01 | PI RemainingQty与MES/XC/Transit同时存在 | 不重复加总 |
| F02 | Stage下游完成量大于上游 | 保守下修 |
| F03 | 中间Stage缺失 | 使用下游已证明最小完成量 |
| F04 | 强事实证明后续位置 | 可修正规划位置，不改源事实 |
| F05 | Position无法完全定位 | UNLOCATED补齐 |
| F06 | Position重复 | 不允许重复消费 |
| F07 | 大工艺接续跨厂 | 返回正确CrossFactory事实 |
| F08 | 厂间订单 | SH/Transit/Received关系正确 |
| F09 | Received按单据 | 只按合法单据对应 |
| F10 | 同物料多个SH | Received不可串用 |
| F11 | 同物料不同SH | 不可串用Received |
| F12 | Transit已Received | 不重复计算 |
| F13 | PO有Manual ETA | Manual ETA事实可正确提供 |
| F14 | Manual ETA取消 | 事实层恢复无人工覆盖状态 |
| F15 | ERP ETA为空 | 提供ReleaseDate等计算基础，不由5号位最终算AvailableTime |
| F16 | ETA相关默认参数 | 参数由3号位治理，5号位不自行定义 |
| F17 | 已到厂未入库 | 正确返回来源事实/仓库事实 |
| F18 | VMI | 独立SupplyType事实 |
| F19 | ProcessCode ERPProperty | 来自ERP真实属性，不猜 |
| F20 | 真实采购Supply全空 | 返回真实空，不生成Placeholder |
| F21 | ODS单条坏数据 | Issue登记，不污染其它事实 |
| F22 | PI总量无法闭合 | 严重Issue，2号位可据此Fail Domain |

说明：

原v1.0中F15/F16/F17如把最终AvailableTime计算放到5号位，应按当前业务冻结改为：

> 5号位提供事实，最终AvailableTime由2号位计算。

---

# 二十七、新增前端接入与权限验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| F23 | 4号位Overview | 4→5，5调用2号位Query |
| F24 | Order查询 | 5不重算Pegging |
| F25 | Gantt查询 | 只返回正式FinalTask结果 |
| F26 | Explanation | 5可补事实Issue，但不覆盖正式ReasonCode |
| F27 | Supply Trace | Allocation以2号位结果为准 |
| F28 | PI Position页面 | 4→5直接读取5号位事实 |
| F29 | Manual ETA查看 | 4→5正常 |
| F30 | Manual ETA编辑 | 5号位事实更新，不改ERP ETA |
| F31 | Manual ETA取消 | Override失效，原ERP事实不变 |
| F32 | Effective ETA | 不在5号位作为最终真值计算 |
| F33 | AvailableTime | 不在5号位重新计算最终值 |
| F34 | Demand Protection查看 | 4→5→2 |
| F35 | Demand Protection释放 | 4→5→2，实际逻辑仍2号位 |
| F36 | 5号位直接UPDATE Protection | 禁止 |
| F37 | MES资格查看 | 4→5→2 |
| F38 | MES受控操作 | 权限通过后仍需2号位业务校验 |
| F39 | G4迁移 | Run普通查询由5号位提供 |
| F40 | G7迁移 | Domain依赖查询由5号位提供 |
| F41 | G8迁移 | Domain状态查询由5号位提供 |
| F42 | Schedule查询迁移 | 不产生第二套计划真相 |
| F43 | FAILED恢复 | 不由5号位治理，走3号位 |
| F44 | Candidate确认 | 不由5号位治理，走3号位 |
| F45 | 无功能权限访问Manual ETA | 403 |
| F46 | Factory=TJ用户请求BJ | 后端拒绝/过滤 |
| F47 | ProductFamily=X用户请求Y | 后端拒绝/过滤 |
| F48 | Department=MC用户请求ASSY | 后端拒绝/过滤 |
| F49 | Domain=D01请求D02数据 | 后端拒绝/过滤 |
| F50 | 浏览器篡改Scope | 5号位后端不得扩大权限 |
| F51 | 4号位直接调用2号位HTTP | 不作为正式集成路径 |
| F52 | 5号位重复实现Allocation | 验收失败 |
| F53 | 5号位创建第二套Plan结果表 | 验收失败 |
| F54 | Planning Placeholder | 5号位不落正式SupplyFact |
| F55 | Current User上下文 | 正确消费3号位身份/权限上下文 |
| F56 | 权限通过但MES业务资格失败 | 返回业务拒绝，不越权执行 |
| F57 | 权限通过但Protection不可释放 | 2号位拒绝，5号位正确转译 |
| F58 | G4/G7/G8迁移兼容 | URL/DTO优先保持兼容 |
| F59 | 前端1000 Task列表 | 不逐Task调用2号位 |
| F60 | Auth规则变更 | 5号位不维护第二套Permission规则 |

---

# 二十八、性能要求

原v1.0性能要求继续有效：

## 28.1 PI Position

夜间批量计算所有活跃PI。

避免：

- 每PI多次DB往返；
- 每Position单独远程调用。

## 28.2 Procurement / Timed Supply

批量读取：

- PO；
- VMI；
- Received；
- Transit；
- Arrival。

## 28.3 前端查询

新增要求：

- 服务端分页；
- Scope尽早下推；
- Overview使用聚合Query；
- Gantt按时间窗/资源范围加载；
- Explanation按需；
- Supply Trace按订单/需求查询；
- 不做逐Task N+1；
- 不把90天全部Task一次送前端。

---

# 二十九、5号位最终交付物（v1.1）

原交付物继续保留并扩展：

1. PI Position Calculator；
2. PI Position输入/输出DTO；
3. Procurement / VMI / Received / Transit标准事实DTO；
4. 两类跨厂事实实现；
5. BOM/Workset必要SP与View；
6. ERPProperty真实同步链；
7. ODS契约；
8. Issue结构与日志；
9. `ProcurementManualEtaOverride`结构；
10. Manual ETA Repository / Service / API；
11. 面向4号位的非治理业务HTTP/API；
12. Overview接口；
13. Order/需求计划接口；
14. Gantt接口；
15. Explanation接口；
16. Supply/Pegging Trace接口；
17. PI Position接口；
18. Demand Protection查看/释放中转；
19. MES资格/受控操作中转；
20. G4 / G7 / G8 / Schedule普通查询迁移；
21. 3号位身份/权限上下文接入；
22. Factory/ProductFamily/Department/Domain后端Scope校验；
23. F01～F60测试结果；
24. 批量性能测试报告；
25. 与2号位Query/Application Service联调记录；
26. 与3号位认证/权限上下文联调记录；
27. 与4号位页面API联调记录；
28. 真实源数据尚未接通清单；
29. 旧3号位普通结果查询迁移兼容记录。

---

# 三十、严禁新增的过度设计

原v1.0禁止项继续有效：

- 通用规则Plugin平台；
- Voucher平台；
- PI Header+Slice双表生命周期平台；
- CrossFactory通用状态机；
- ShippingTask；
- Logistics Solver；
- Procurement Task；
- Planning-only Placeholder表；
- SupplyAvailabilityRule DSL；
- 通用事件溯源；
- 通用MDM；
- MultiDomain编排；
- Explanation原因推理平台。

本版新增禁止：

- 通用API Gateway平台；
- 第二套RBAC；
- 第二套Business Scope；
- 第二套Pegging服务；
- 第二套Allocation服务；
- 第二套Demand Protection服务；
- 第二套ScheduleRun/PlanVersion结果库；
- 前端查询镜像运行库；
- 任意Row-Level ACL平台；
- 任意Field-Level ACL平台；
- ABAC/DSL权限引擎；
- 为中转而复制2号位核心代码。

---

# 三十一、5号位与4号位联调红线

4号位可通过5号位访问：

- Overview；
- Order；
- Gantt；
- Explanation；
- Supply Trace；
- PI Position；
- Manual ETA；
- Demand Protection；
- MES；
- G4/G7/G8/Schedule普通查询。

5号位必须保证：

- 权限上下文有效；
- Scope有效；
- DTO稳定；
- 业务Owner不漂移。

不得要求4号位：

- 直接连2号位；
- 自己做Scope过滤；
- 自己判断AvailableTime；
- 自己重算Pegging。

---

# 三十二、5号位与3号位联调红线

5号位从3号位消费：

- Identity；
- Permission；
- Factory Scope；
- ProductFamily Scope；
- Department Scope；
- Domain Scope。

5号位不得：

- 创建另一套User表；
- 创建另一套Role表；
- 创建另一套Permission表；
- 自己解释Role名称得到权限；
- 自己扩大Scope。

---

# 三十三、5号位与2号位联调红线

5号位继续向2号位提供复杂事实。

同时作为4号位中转层调用2号位内部服务。

必须保持：

> **两种关系不要混成“5号位接管2号位业务”。**

2号位仍Own：

- Pegging；
- Allocation；
- Demand/Supply Balance；
- Lock；
- Demand Protection；
- CrossFactory主流程；
- SupplyFact_Pipeline最终装载；
- Effective ETA；
- AvailableTime；
- ScheduleRun运行主流程；
- PlanVersion运行真值；
- FinalTask持久化。

---

# 三十四、5号位与1号位关系

5号位不直接调用1号位Solver。

任何页面需要有限产能结果：

```text
1号位求解
→ 2号位持久化
→ 5号位Query/API
→ 4号位展示
```

---

# 三十五、完成定义（Definition of Done）

5号位完成必须同时满足：

## 35.1 原复杂事实闭环

- BOM/Workset链稳定；
- PI Position总量闭合；
- Position互斥；
- UNLOCATED可追溯；
- MES五态不扩张；
- Received不跨SH串用；
- Transit/Received不重复；
- ERPProperty来自真实ERP属性；
- Procurement/VMI/Received/Transit不再固定空；
- Planning Placeholder不由5号位落表；
- Issue可追溯；
- 批量性能满足夜间运行方式。

## 35.2 Manual ETA / SupplyFact闭环

- ERP ETA保持源事实；
- ReleaseDate正确透传；
- Manual ETA由5号位维护；
- Manual ETA不修改ERP；
- Effective ETA最终计算不在5号位；
- AvailableTime最终计算不在5号位；
- SupplyFact_Pipeline最终业务Owner仍为2号位。

## 35.3 前端业务接口闭环

- 4号位非治理业务统一可经5号位访问；
- Overview/Order/Gantt/Explanation/Supply Trace可用；
- PI Position/Manual ETA可用；
- Demand Protection中转正确；
- MES中转正确；
- G4/G7/G8/Schedule迁移正确；
- 不新增5号位运行结果第二真相。

## 35.4 权限闭环

- 正确消费3号位认证上下文；
- 正确校验功能权限；
- 正确校验Factory；
- 正确校验ProductFamily；
- 正确校验Department；
- 正确校验Domain；
- 浏览器篡改参数不能越权；
- 不建立第二套权限体系。

## 35.5 职责红线

- 不重做Pegging；
- 不重做Allocation；
- 不重做Demand Protection；
- 不重做CrossFactory主流程；
- 不重算最终AvailableTime；
- 不生成FinalTask；
- 不调用Solver；
- 不接管Run/Candidate治理；
- 不直接修改2号位核心运行结果；
- 不因前端中转新增过度设计。

---

# 三十六、一句话交付要求

> **5号位V1必须在保留原“复杂事实Owner”职责不变的基础上，把PI Position、BOM/MES、跨厂、采购/VMI/Received/Transit、ERPProperty和Manual ETA等复杂事实可靠整理出来；同时作为4号位非治理APS业务的统一接口接入层，消费3号位认证权限上下文，按Factory/ProductFamily/Department/Domain执行后端范围校验，并通过2号位稳定Query/Application Service提供Overview、Order、Gantt、Explanation、Supply/Pegging Trace、Demand Protection、MES及G4/G7/G8/Schedule等页面能力。5号位的中转权绝不能变成对Pegging、Allocation、Demand Protection、AvailableTime、ScheduleRun或FinalTask的重新实现。**
