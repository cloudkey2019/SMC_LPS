# APS V1 2号位增量开发实施包（20260915 白天业务场景 / Candidate人工调整冻结对齐版）

**版本**：v1.7  
**日期**：2026-09-15（北京时间）  
**适用对象**：2号位及其开发AI  
**文档性质**：基于现有代码和既有整改结论的增量修订实施说明，不是新项目重构方案  

**母版与整改基线**：

1. 《APS V1 2号位增量开发实施包 v1.1（执行对齐版，2026-08-14）》；
2. 《APS V1 2号位最终整改统一审核报告（冻结基线版，2026-08-24）》；
3. 2号位此前已经通过/关闭的代码审核结论。

**当前有效上位依据（2026-09-13）**：

- 《APS V1最终全部流程与业务基线 v1.6（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS Pegging供需承接与分层计算业务说明 v1.5（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS 有限产能排产与滚动90天计划业务说明 v1.4（跨版本排程连续性 / 跨厂SH联合冻结对齐版）》；
- 《APS 集成接口设计规范 v1.32（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS 核心排产全流程走查 V3.22（白天业务场景 / Candidate人工调整冻结对齐版）》；
- 《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》。

> 本版继续完整继承v1.6及此前职责、Domain、Supply/PI Position、跨版本连续性、跨厂SH与8月24日整改关闭项；本轮只增加Candidate Input v2、单场景多对象、变化Seed、PriorityMode、Business Scope移动集合及ACTIVE Base运行配合。若历史章节与本版冲突，以v1.7当前口径为准。

**新增上位依据**：

- 当前APS V1最终业务冻结基线；
- Pegging、有限产能两份专项冻结说明当前有效版；
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决 v1.0》；
- 《APS 集成接口设计规范 v1.28（20260901职责与前端接口冻结对齐版）》；
- 《APS 权限与审批设计方案 v1.1（20260901冻结对齐版）》；
- 《APS V1 3号位规则、参数、认证权限与运行生命周期开发实施包 v1.2（Domain专项完整合并版）》；
- 《APS V1 4号位页面与业务操作开发实施包 v1.2》；
- 《APS V1 5号位复杂业务事实、ODS与业务接口接入开发实施包 v1.1》。

## 当前版本变化说明（2026-09-15）

**修改依据**：业务基线v1.6 + Pegging v1.5 + 接口v1.32 + 字段/DDL v5.1.7  
**修订原则**：继续复用SchedulingOrchestrator、Pegging、Allocation、DomainSolveRequest现有外壳；只补白天Candidate结构化运行输入和执行闭环。  
**影响边界**：不建Scenario平台，不重写夜间FULL，不改PI/SH/Continuation。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | Run输入 | §13/§16 | 消费ScopeJson v2：BusinessTriggerType/PriorityMode/OrderTargets/TaskTargets/ChangedResourceIds。 | 单场景多对象 | 结构化白天运行 | 3→2请求变化 | ScopeJson列不变 | 2号位 |
| 2 | Seed与Scope | §13/§14 | 形成多Seed、允许移动集合和Scope外不可移动Anchor/Block；Seed不屏蔽其它当前真实事实。 | v1.1原则 | 局部调整真实可行 | 2→1上下文增强 | 无 | 2号位 |
| 3 | 优先语义 | §13 | NORMAL/EXPEDITE进入当前Demand/Pegging上下文；ManualTargetDueDate不改正式DueDate。 | 已有订单提前/急单 | 防止DueDate偷换优先级 | 内部上下文变化 | 无 | 2号位 |
| 4 | Candidate Base | §16 | 配合3号位使用当前ACTIVE Base，结果持久化保留Base关系。 | Candidate一致性 | 防止旧Base覆盖 | Run治理配合 | 复用BasePlanVersionId | 2号位 |


> **2026-09-14 P0文档纠错（业务版本保持v1.6不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

---

# 0. 使用方式与最高原则

本实施包只回答：

> **在2号位现有代码和8月24日已冻结整改结论上，哪些继续保留，哪些因9月1日职责调整退出，哪些必须最小修改，哪些只需提供内部服务，以及怎样完成最终联调验收。**

禁止把本版理解为重新设计2号位、重写 `SchedulingOrchestrator`，或借接口调整重新讨论已经冻结的Pegging、Candidate、PlanVersion业务。

## 0.1 权威顺序

1. 当前APS V1业务冻结基线；
2. Pegging、有限产能专项冻结说明；
3. 2026-09-01职责、接口、权限正式裁决；
4. 当前有效技术冻结文档；
5. 2号位8月24日最终整改统一审核结论中未被9月1日新裁决修正的部分；
6. 本增量实施包；
7. 2号位当前代码。

## 0.2 整改总原则

继续沿用：

> **保留外壳，退出冲突旧路径，补齐数量闭环、事务闭环和接口闭环。**

本版新增：

> **退出不再属于2号位的对外接口和Auth职责，但绝不因此迁走2号位核心业务Owner。**

---

# 一、2号位最新正式定位

2号位仍是APS V1核心运行主流程Owner，负责：

- SchedulingOrchestrator；
- Demand构建；
- Supply构建；
- Demand排序执行；
- Pegging；
- DemandBalance / SupplyBalance；
- Allocation；
- Lock执行；
- Strict Binding；
- Demand Protection实际数量创建、消费、释放；
- PI选择与Position消费；
- 两类跨厂业务主流程消费；
- Planning-only Purchase Placeholder；
- Effective ETA；
- AvailableTime；
- LogicalProductionDemand；
- 当前Q形成后的IN_PROGRESS执行覆盖识别；
- 逐MES工单Continuation Slice / Free Slice分桶；
- ContinuationKey与OldTaskNo / MESWorkOrderNo身份映射；
- SH第一层单一Supply Allocation、第二层源厂履约Demand、SourceFactoryId重新归域；
- 计划良率数量反算；
- DomainSolveRequest组装；
- 调用1号位有限产能Solver；
- Solver结果校验；
- FinalTask等运行结果统一持久化；
- ScheduleRun / PlanVersion运行协作；
- Candidate主流程；
- MES正式业务资格与受控执行；
- Quantity-Time传递。

> **2号位仍拥有APS V1“数量真相 + Pegging真相 + 运行主流程真相 + 结果持久化真相”。**

---

# 二、本版开始明确退出的职责

## 2.1 退出4号位-facing HTTP/API

正式架构不再允许：

```text
4号位 → 2号位 HTTP/API
```

如当前已有面向4号位的Plan、Schedule、Order、Gantt、Demand Protection、MES等Controller：

- 能安全下线的退出正式路由；
- 迁移期确需兼容的仅作短期兼容；
- 新功能不得继续扩展2号位对4号位HTTP层。

## 2.2 Auth整体移交3号位

包括：

- Authentication；
- Login / Refresh / Logout / Current User；
- User / Role / Permission；
- UserRole / RolePermission；
- Business Scope；
- Auth Audit；
- APS_Auth Entity / Repository / Service / Controller；
- Migration / DDL / Seed。

2号位不再是APS_Auth Owner。

## 2.3 规则/参数表DDL责任修正

8月24日报告中 `RuleSetVersion.ContentSnapshotJson`、`ParameterSetVersion.ContentSnapshotJson` 曾写为“2号位/数据库”负责迁移。

9月1日后正式改为：

> **3号位Own Rule / Parameter / Strategy治理结构及其DDL。**

2号位只负责：

- 运行时读取；
- FrozenSnapshot装载；
- 历史重放；
- 2↔3联调。

---

# 三、必须保留的2号位保护区

| 对象/能力 | 处理 |
|---|---|
| `SchedulingOrchestrator` | 保留，不重写 |
| `RunSchedulingAutoAsync` | 保留主入口 |
| Hangfire/运行外壳 | 保留 |
| `ScheduleRun / PlanVersion`运行框架 | 保留 |
| `SchedulingContext`装载 | 保留增强 |
| `SupplyPool` | 保留增强 |
| `RemainingQty`原型 | 保留增强 |
| MES Progress装载 | 保留为事实输入 |
| `IFiniteCapacityScheduler.SolveAsync` | 保留唯一Solver接口方向 |
| `DomainSolveRequest / Result` | 保留扩展 |
| `PeggingLedgerEntry` | 保留并完成闭环 |
| `AllocationTaskShareDto` | 保留并实现多对多 |
| 当前事务模板 | 保留并收口 |
| ext/ODS Loader模式 | 保留 |

禁止因9月1日接口变化改写上述核心。

---

# 四、原v1.1已经要求退出主链的旧路径继续退出

- `DefaultBatchSplitter`：不得在Solver前提前形成最终物理Task；
- 第二套旧 `FiniteCapacitySolver.Solve()`：正式生产路径只能经 `IFiniteCapacityScheduler`；
- `PassThroughSchedulerStub`：仅用于测试，不是正式Solver；
- `FrozenZoneSnapshot`：不发展为V1平台；
- `VirtualInventoryBalance`：不发展为持久化状态机。

这些结论不重新打开。

---

# 五、正式V1主链保持不变

```text
SchedulingOrchestrator
    ↓
Run / Domain上下文
    ↓
装载冻结规则参数与事实切片
    ↓
Demand构建
    ↓
Supply构建
    ↓
5号位 PI Position / NextOperationContext / 标准化MES执行事实 / 跨厂物流事实
    ↓
第一层Pegging
    ↓
（适用时）SH单一Supply承接
    ↓
（适用时）SH第二层源厂履约Demand
    ↓
DemandBalance + SupplyBalance + Lock + Allocation
    ↓
消费PI Position与NextOperationContext
    ↓
形成当前LogicalProductionDemand Q
    ↓
消费5号位IN_PROGRESS既存执行上下文
    ↓
逐MES工单 Continuation Slice + Free Slice
    ↓
IFiniteCapacityScheduler.SolveAsync
    ↓
FinalTask + TaskShare + 物理依赖 + Explanation + Unscheduled
    ↓
2号位校验
    ↓
单Domain事务统一持久化
    ↓
PlanVersion状态
    ↓
ScheduleRun汇总
```

9月1日调整的是接口层和Auth Owner，不改变主链。

---

# 六、Pegging数量核心继续保持冻结整改

## 6.1 DemandBalance / SupplyBalance

继续作为本次运行数量真相，优先复用 `SupplyPool / RemainingQty`，不另建第二套引擎。

## 6.2 原子Allocation

成功Allocation必须同一原子动作完成：

1. Demand余额；
2. Supply余额；
3. Eligibility；
4. Strict Binding；
5. Demand Protection；
6. Execution不可逆事实；
7. 扣DemandBalance；
8. 扣SupplyBalance；
9. 生成AllocationSequence；
10. 生成Ledger/Allocation。

## 6.3 Priority Segment

仍执行：

```text
计算层
→ 有序Priority Segment
→ 第一命中
→ Segment内排序
→ 稳定键
```

不得恢复全局PriorityScore。

---

# 七、PI Position、NextOperationContext与5号位职责保持冻结

5号位负责：

- PI Position计算；
- MES复杂进度事实解释；
- XC/Transit位置解释；
- Routing工序关系关联；
- NextOperationContext计算；
- PI Position / NextOperationContext Issue；
- 采购在途/未结PO、VMI、采购已到厂未入库等正式Supply事实；
- 跨厂Transit/Received/ZP/BP只作为物流/位置追溯与SH号存在性事实，不形成独立Supply；
- MES工单/工序/Stage标准化执行事实；
- 工序级LastReportResourceCode；
- SH号在跨厂在途视图中的存在性事实；
- Manual ETA事实。

5号位回答：

> **该PI剩余数量已经做到哪里，以及下一步从哪个Stage/Operation继续。**

2号位负责：

- 选PI；
- 按已选PI消费Position；
- 消费5号位返回的NextOperationContext；
- SupplyBalance；
- Allocation；
- 将执行起点上下文组装进LogicalProductionDemand；
- 根据严重Issue决定Domain是否失败；
- 调用1号位有限产能Solver。

2号位回答：

> **哪个Demand承接哪个PI Supply，并把已经确定的执行起点上下文交给1号位。**

冻结红线：

- 禁止把PI Position Calculator搬回2号位；
- 禁止2号位根据MES原始报工自行重新推导StartOperation；
- 禁止把NextOperationContext变成新的Supply身份；
- 禁止把Demand↔PI Pegging扩展成Demand↔PI↔Operation三级Pegging。

---


# 七A、跨版本排程连续性实施链

## 7A.1 当前Q必须先形成

正式顺序：

```text
当前Pegging
→ LogicalProductionDemand Q
→ 消费5号位标准化IN_PROGRESS执行事实
→ Continuation / Free分桶
```

禁止旧TaskNo、MESWorkOrderNo或历史计划数量反向决定Q。

## 7A.2 E与Q闭合

```text
E <= Q:
ContinuationQty = E
FreeQty = Q-E

E > Q:
FreeQty = 0
ExecutionOverCommit = E-Q

Q=0且E>0:
不创建正常Demand/Task，只登记异常
```

不得把Q提高成E。

## 7A.3 多MES工单逐单连续

```text
WO101剩20 → C101 Qty20
WO102剩30 → C102 Qty30
```

可以合计E=50，但不能合并为一个Qty50 Continuation对象。

## 7A.4 MES字段边界

2号位不得绕过5号位重新解释MES原始报工。

工单级不新增：

```text
RemainingQty
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

工序级：

```text
MES_APS_OperationProgress_View.LastReportResourceCode
→ OperationProgressSnapshot.LastReportResourceCode
```

2号位将其映射成`PreferredResourceCode`，不是Hard Lock。

## 7A.5 ContinuationKey与三类身份

APS旧Task连续：

```text
ContinuationKey → OldTaskNo → Existing MESWorkOrderNo
FinalTask返回后继承OldTaskNo
```

无TaskNo外部MES连续：

```text
ContinuationKey → Existing MESWorkOrderNo
FinalTask返回后新建APS TaskNo并绑定原MES工单
不创建第二MES工单
```

自由份额：

```text
FinalTask → 新TaskNo → 正常MES下发 → 新MESWorkOrderNo
```

APS侧绑定不等于强制回写MES原工单TaskNo字段。

## 7A.6 WorkOrderStatus

继续使用：

```text
RELEASED
IN_PROGRESS
CLOSED
DELETED
```

- RELEASED：不建立执行连续性；
- IN_PROGRESS：有当前Q时形成连续份额；
- CLOSED：结束执行身份；
- DELETED：结束执行身份，有需求则重新排。

## 7A.7 同PI同Operation顺序

```text
既存执行连续份额
→ 新增自由份额
```

不得反转，也不得自动多设备时间重叠。


# 八、Manual ETA / Effective ETA / AvailableTime

正式责任链：

```text
5号位
  ├─ ERP ETA
  ├─ ReleaseDate
  └─ Manual ETA
        ↓
2号位
  ├─ 读取本次Run冻结参数
  ├─ Effective ETA
  └─ AvailableTime最终计算
        ↓
Pegging
```

冻结优先级：

```text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

Manual ETA维护归5号位；最终Effective ETA和AvailableTime归2号位。

---

# 九、Planning-only Purchase Placeholder

仍由2号位在当前运行内存形成：

- Qty = Gap；
- ESTIMATED；
- NOT_COMMITTED；
- AvailableTime按冻结规则估算。

不得落成PO、正式承诺、采购Task、MES Task或ERP订单。

---

# 十、Demand Protection

2号位继续拥有：

- 实际数量锁定；
- 数量消费；
- 数量释放；
- Lock/Allocation一致性；
- 最终业务可释放条件。

前端链路变为：

```text
查看：4 → 5 → 2 Query Service
释放：4 → 5 → 2 Application Service
```

即使权限通过，最终是否允许释放仍由2号位业务硬校验。

---

# 十一、MES业务资格

前端链路：

```text
4 → 5 → 2号位MES Query/Application Service
```

2号位仍负责正式业务资格：

- ACTIVE PlanVersion；
- 非Candidate；
- 有合法PI/执行身份；
- 非UNLOCATED；
- 非无PI规划占位Task；
- 不依赖未承诺Placeholder；
- 合法时间窗/Firm；
- 未取消/失效。

权限通过不能替代业务资格。

---

# 十二、2号位需要整理的内部服务

目标不是新建平台，而是把现有核心能力形成供3号位、5号位调用的稳定内部契约。

## 12.1 提供给5号位的Query Service

至少覆盖：

- Plan Overview；
- Order / Demand Plan；
- Gantt / Resource Plan；
- Explanation；
- Supply/Pegging Trace；
- Demand Protection Query；
- MES Query；
- G4 / G7 / G8 / Schedule普通查询所需真实运行事实。

5号位可以做页面DTO和接入，但不得重算2号位业务。

## 12.2 提供给5号位的Application Service

至少：

- Demand Protection Release；
- MES受控操作。

2号位必须再次执行业务校验。

## 12.3 提供给3号位的Run/Candidate执行服务

3号位负责治理入口，2号位负责真实运行主流程。

至少：

- CTP；
- INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate业务执行；
- FAILED恢复所需业务执行协作。

---

# 十三、Candidate运行输入与白天业务执行边界

## 13.1 Candidate Input v2消费

2号位从3号位接收已经校验冻结的运行输入，不自行重新定义Schema。

至少消费：

```text
SchemaVersion
BusinessTriggerType
PriorityMode
OrderTargets[]
TaskTargets[]
ChangedResourceIds[]
```

以及原ScopeJson仍适用范围字段。

一次LOCAL只能一个BusinessTriggerType，但可以多同类对象。

## 13.2 变化Seed

2号位把业务输入转换为求解Seed：

- 新订单/已有订单 → Demand Seed；
- 甘特TaskTargets → Task软目标Seed；
- 故障/Calendar资源 → Resource Seed。

这些Seed决定从哪里开始传播，不得作为“只装载这些事实”的过滤器。

## 13.3 NORMAL / EXPEDITE

NORMAL：

- 当前Pegging继续按正式业务优先规则；
- ManualTargetDueDate不得覆盖正式DueDate或排序真相。

EXPEDITE：

- 只提升本次明确选择订单及其派生需求链；
- 其它Demand保持原规则；
- 不突破Demand Protection、Strict Binding、Firm/Frozen、Material AvailableTime等硬约束。

不得通过修改Order正式DueDate实现EXPEDITE。

## 13.4 已有订单提前

支持：

```text
OrderTargets[] = 多订单 + 每订单独立ManualTargetDueDate
```

如果同时存在`OrderCanonicalIds`，必须与`OrderTargets[].OrderCanonicalId`投影一致，否则拒绝执行。

## 13.5 甘特多Task调整

`TaskTargets[]`允许多个Task及各自TargetTime。

2号位负责把这些Target转为1号位软目标上下文，不直接UPDATE正式Task。

甘特固定NORMAL。

## 13.6 故障 / Calendar

`ChangedResourceIds[]`只标记变化Seed。

真实故障/Calendar状态必须从当前正式资源事实加载。

不得创建Candidate专属假设Calendar或仅按ChangedResourceIds过滤ResourceContext。

## 13.7 LOCAL / MANUAL

LOCAL影响传播即使扩大到当前Domain全部可移动Task：

```text
RunType仍为LOCAL_RESCHEDULE
```

MANUAL只在用户一开始选择整Domain重排时使用。

2号位不得根据影响数量自行改RunType。

# 十四、Business Scope在2号位中的定位

2号位必须把当前用户允许移动范围转换成运行约束：

- Scope内可移动Task按Candidate规则参与求解；
- Scope外Task不得移动；
- Scope外Task仍作为真实Resource/时间占用进入1号位；
- 如果可行方案必须移动Scope外Task，返回范围不足，不得静默越权。

Business Scope不是查询过滤器，更不是Solver事实删除器。

# 十五、禁止内部服务演变成新平台

不要求：

- 新微服务；
- 新Service Mesh；
- 新API Gateway；
- 新RPC框架；
- 全量CQRS化；
- 所有Repository再包一层空Service。

优先复用现有Application/Domain Service，只形成必要跨号位稳定契约。

---

# 十六、ScheduleRun / PlanVersion保持冻结

继续保持：

- FULL一次Run可有多个Domain PlanVersion；
- Candidate严格单Domain；
- ExpectedDomainKeys运行开始冻结；
- 上游失败阻断依赖下游本次发布；
- 无关Domain继续；
- COMPLETED / PARTIAL_SUCCESS / FAILED正确汇总；
- FAILED历史不改回RUNNING；
- 人工恢复创建新ScheduleRun。

3号位治理增强不等于2号位退出运行结果持久化职责。

---



## 16.1 白天Candidate Base关系

执行白天Candidate前必须确认3号位传入：

```text
BasePlanVersionId = 本Domain创建时当前ACTIVE
ExpectedDomainKeysJson = 单一目标Domain
```

2号位不得用Candidate作为Base，也不得自行选择另一个Base。

Candidate结果持久化时必须保留SourceScheduleRun/Base关系供3号位激活复核。

多个Candidate可以共同基于同一ACTIVE并存；其中一个先激活后，其它旧Base Candidate由3号位拒绝激活并要求重跑。
# 十七、1↔2 Solver接口保持冻结

继续：

```text
SchedulingOrchestrator
→ IFiniteCapacityScheduler.SolveAsync(DomainSolveRequest)
→ DomainSolveResult
```

2号位准备九类输入：

1. RunBoundary；
2. LogicalProductionDemands；
   - AllocationSequence / DemandKey；
   - ProductionInstructionNo（适用时）；
   - NetOutputQty / PlannedProcessQty；
   - StartStageCode；
   - StartOperationCode（必要时）；
   - NextOperationContext或其等价执行起点字段；
   - RequiredAvailableTime；
3. AllocationLineage；
4. RoutingNetwork；
5. ResourceEligibility/Capability；
6. Material Quantity-Time；
7. Frozen Strategy/Parameters；
8. Execution/Firm/Frozen约束；
9. Candidate约束。

1号位返回：

- FinalTask；
- TaskShare；
- 物理依赖；
- Explanation Facts；
- Unscheduled/Unfulfilled；
- KPI/诊断。

2号位不得收到结果后重新修改Resource、Start、End或Split/Merge。

---

# 十八、8月24日最终整改事项的继承

## 18.1 P0-01继续有效

```text
SchedulingOrchestrator
↓
IFiniteCapacityScheduler
↓
1号位真实Solver
```

禁止绕过接口直接依赖具体 `FiniteCapacitySolver`。

## 18.2 原P0-02责任修正

`RuleSetVersion.ContentSnapshotJson` 与 `ParameterSetVersion.ContentSnapshotJson`：

- DDL / Migration：3号位；
- 运行时读取和FrozenSnapshot装载：2号位；
- 历史重放：2↔3联调。

## 18.3 真实集成测试继续有效

```text
DRAFT
→ 保存
→ 读取
→ 修改
→ Validate
→ Publish
→ FrozenSnapshot
→ 2号位Run装载
→ 历史版本重放
```

## 18.4 SolverStrategy传递继续有效

```text
3号位
→ Frozen Strategy Snapshot
→ 2号位一次Run装载
→ DomainSolveRequest
→ 1号位Solver
```

不得逐Demand远程读取3号位。

---

# 十九、Auth代码移交实施要求

采取“迁移，不重写”。

1. 盘点现有Auth Controller、Service、Entity、Repository、DTO、Token/Refresh、Scope、Audit、Migration、配置；
2. 可直接复用的原样迁3号位；
3. 仅做必要Namespace/DI/配置调整；
4. 迁移完成后2号位停止Auth Controller正式注册；
5. 2号位不再执行APS_Auth Migration/DDL；
6. 2号位不再维护Role/Permission Seed；
7. 2号位不再维护Business Scope规则。

如部署上短期需要兼容，可保留桥接，但必须Deprecated，不能形成两套Auth真值。

---

# 二十、数据库Owner最新规则

## 20.1 2号位Own

- PeggingSupplyAllocation；
- AllocationTaskShare；
- Lock核心运行结构；
- Demand Protection核心运行结构；
- PlanVersion；
- ScheduleRun运行结果关联；
- FinalTask；
- ScheduleExplanationFact运行结果；
- SupplyFact_Pipeline APS层最终运行装载结构（仅采购/VMI类Timed Supply；禁止`INTERPLANT_IN_TRANSIT`）；
- 其它明确属于核心排程运行真值的结构。

## 20.2 3号位Own

- APS_Auth；
- User/Role/Permission/Scope/Auth Audit；
- RuleSet / ParameterSet / StrategyProfile；
- 对应Version/Snapshot治理结构；
- 其DDL/Migration。

## 20.3 5号位Own

- ODS复杂事实结构；
- PI Position自属事实；
- `ProcurementManualEtaOverride`；
- 其它5号位复杂事实表/View/SP。

跨Owner修改必须由Owner评审执行。

---

# 二十一、2↔5最新边界

2号位从5号位消费复杂事实：

- PI Position；
- NextOperationContext；
- MES执行进度解释结果；
- Procurement/VMI；
- Received；
- Transit；
- Manual ETA；
- ERP ETA；
- ReleaseDate；
- MES复杂事实；
- ODS Issue。

5号位同时调用2号位内部Query/Application Service为4号位提供接口。

> **双向技术协作，不改变业务Owner。**

---

# 二十二、2↔3最新边界

3号位向2号位提供：

- 冻结规则版本；
- 冻结参数版本；
- Strategy Snapshot；
- 合法Run治理上下文；
- 已验证Identity/Permission/Business Scope上下文。

2号位向3号位提供：

- Run真实执行入口；
- Candidate业务执行结果；
- Run/Domain执行状态事实；
- FAILED恢复所需业务执行能力。

3号位不做Pegging、Allocation、FinalTask和普通结果查询Gateway。

---

# 二十三、4号位与2号位关系

正式不存在直接HTTP/API集成：

```text
治理类：4 → 3 → 2
非治理类：4 → 5 → 2
```

2号位不得再因“页面方便”新增4-facing Controller。

---

# 二十四、原事务、TaskShare、Candidate数量闭环继续有效

以下旧整改不重新打开：

- 单Domain统一事务；
- Pegging失败不得warning后继续发布；
- AllocationTaskShare真实多对多；
- PSA薄镜像不重复写；
- InventoryBalance.AllocatedQty不作为运行数量真相；
- Candidate普通Base Allocation可重新竞争；
- Strict Binding / Protection / Execution不可逆份额不释放；
- MaxImpactedOrders只告警不截断；
- 外Domain ACTIVE共享资源作为阻挡；
- Quantity-Time切片不得压平。

---

# 二十五、建议实施顺序（v1.2）

## 阶段A：完成8月24日剩余P0/P1

1. SchedulingOrchestrator只依赖IFiniteCapacityScheduler；
2. 配合3号位完成ContentSnapshotJson迁移后的读取验证；
3. Frozen Strategy Snapshot真实重放；
4. 1↔2 SolverStrategy联调。

## 阶段B：Auth迁出

1. 盘点现有Auth；
2. 整体移交3号位；
3. 停止2号位Auth正式注册；
4. 停止APS_Auth DDL/Migration；
5. 验证3号位Login/Refresh/Me；
6. 验证2号位不维护Permission/Scope。

## 阶段C：4→2直接API退出

1. 盘点当前4→2接口；
2. 治理类归3；
3. 非治理类归5；
4. 2号位保留底层Application/Query逻辑；
5. Controller兼容入口逐步退出。

## 阶段D：给5号位Query Service

- Overview；
- Order；
- Gantt；
- Explanation；
- Supply/Pegging Trace；
- Demand Protection；
- MES；
- G4/G7/G8/Schedule。

## 阶段E：给5号位Application Service

- Demand Protection Release；
- MES受控操作。

## 阶段F：给3号位Run/Candidate执行服务

- CTP；
- INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate业务执行；
- FAILED恢复业务执行协作。

## 阶段G：总联调回归

- 1↔2 Solver；
- 2↔3 Rule/Strategy/Run/Auth上下文；
- 2↔5复杂事实；
- 5↔2前端业务中转；
- 4→3→2治理链；
- 4→5→2业务链。

---

# 二十六、新增接口/Auth/Owner验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| N01 | 4号位直接调用2号位HTTP | 正式路径不存在 |
| N02 | 新前端非治理功能 | 不新增2号位Controller |
| N03 | Overview | 4→5→2正常 |
| N04 | Order | 4→5→2正常 |
| N05 | Gantt | 4→5→2正常 |
| N06 | Explanation | 5不重算2号位ReasonCode |
| N07 | Supply Trace | 5不重算Allocation |
| N08 | Demand Protection查看 | 4→5→2 |
| N09 | Demand Protection释放 | 2号位最终业务校验 |
| N10 | MES查看/操作 | 4→5→2，2号位校验业务资格 |
| N11 | CTP | 4→3→2 |
| N12 | INSERT_IMPACT_ANALYSIS | 4→3→2 |
| N13 | LOCAL_RESCHEDULE | 4→3→2 |
| N14 | MANUAL_RESCHEDULE | 4→3→2 |
| N15 | Candidate确认/激活治理 | 3号位负责 |
| N16 | CTP尝试Activate | 拒绝 |
| N17 | FAILED恢复 | 新Run |
| N18 | Auth Login/Refresh/Me | 3号位提供 |
| N19 | 2号位Auth Controller | 正式退出 |
| N20 | 2号位APS_Auth Migration | 不执行 |
| N21 | User/Role/Permission/Scope | 2号位不维护 |
| N22 | Factory/ProductFamily/Department/Domain Scope | 2号位不扩大合法范围 |
| N23 | ContentSnapshotJson DDL | 3号位Owner |
| N24 | ContentSnapshotJson读取 | 2号位正常 |
| N25 | Historical Snapshot Replay | 2/3联调通过 |
| N26 | Manual ETA维护 | 5号位Owner |
| N27 | Effective ETA / AvailableTime | 2号位计算 |
| N28 | PI Position | 5算、2消费 |
| N29 | G4/G7/G8/Schedule普通查询 | 5对前端，2提供真实结果 |
| N30 | Run治理 | 3负责，不转5 |
| N31 | 5直接UPDATE 2核心表 | 禁止 |
| N32 | 3重做Pegging | 禁止 |
| N33 | 2重建Auth表 | 禁止 |
| N34 | 权限通过但Protection业务不合法 | 2拒绝 |
| N35 | 权限通过但MES业务不合法 | 2拒绝 |
| N36 | 一次Run策略版本 | 中途不变化 |
| N37 | 大量Task查询 | 批量/分页，不逐Task调用 |
| N38 | APS_Auth Seed | 3号位 |
| N39 | Manual ETA DDL | 5号位 |
| N40 | 2号位核心运行DDL | 仍归2号位 |
| N41 | 跨Owner数据库变更 | Owner评审 |
| N42 | 兼容旧4→2路由 | 仅Deprecated，不新增依赖 |
| N43 | 5号位复制Allocation逻辑 | 验收失败 |
| N44 | 3号位复制Run业务主流程 | 验收失败 |
| N45 | 2号位建立第二套权限 | 验收失败 |
| N46 | 4→5→2 Scope传递 | 不越权 |
| N47 | 4→3→2 Scope传递 | 不越权 |
| N48 | 5号位中转失败 | 不改变2号位业务真值 |
| N49 | Auth迁移后原用户/角色数据 | 连续可用 |
| N50 | 最终锁定Commit | 与本包一致 |

---

# 二十七、交付物（v1.2）

1. 8月24日剩余P0/P1整改结果；
2. `SchedulingOrchestrator → IFiniteCapacityScheduler`正式依赖证据；
3. Frozen Strategy Snapshot运行装载验证；
4. 1↔2 SolverStrategy联调结果；
5. Auth代码移交清单；
6. 3号位Auth迁移完成证据；
7. 2号位Auth入口退出证据；
8. APS_Auth Migration/DDL退出证据；
9. 4→2直接HTTP接口盘点；
10. 治理类接口迁3清单；
11. 非治理类接口迁5清单；
12. 对5号位Query Service契约；
13. 对5号位Application Service契约；
14. 对3号位Run/Candidate执行Service契约；
15. Overview/Order/Gantt/Explanation/Supply Trace联调；
16. Demand Protection释放联调；
17. MES受控操作联调；
18. G4/G7/G8/Schedule迁移联调；
19. ContentSnapshotJson读取/历史重放联调；
20. Manual ETA→Effective ETA→AvailableTime联调；
21. 四类Business Scope执行验证；
22. 原2号位业务验收场景回归；
23. N01～N50测试结果；
24. 未关闭真实风险清单；
25. 最终锁定Commit。

---

# 二十八、严禁新增的过度设计

继续禁止：

- 新Pegging Engine；
- 第二Solver；
- PI Slice平台；
- FrozenZone平台；
- VirtualInventory平台；
- MultiDomain Candidate；
- Logistics Solver；
- Voucher平台；
- 通用规则DSL；
- 通用事件溯源；
- V2预埋平台。

本版新增禁止：

- 新API Gateway平台；
- 新RPC框架只为3/5调用；
- 第二套Auth / RBAC / Business Scope；
- 2号位前端BFF；
- 继续扩展4-facing Controller；
- 为5号位中转复制Pegging逻辑；
- 为3号位治理复制Run业务逻辑；
- 2号位代替3/5执行其Owner数据库DDL。

---

# 二十九、完成定义（Definition of Done）

## 29.1 原业务主链闭环

- Demand/Supply数量闭合；
- Allocation原子；
- PI总量不重复；
- Lock正确；
- Demand Protection正确；
- CrossFactory正确；
- Planning Placeholder正确；
- Effective ETA/AvailableTime正确；
- 计划良率正确；
- 唯一Solver；
- TaskShare多对多；
- Candidate正确；
- Quantity-Time不压平；
- Domain事务闭合；
- MES资格正确。

## 29.2 9月1日职责闭环

- 不再面向4号位新增HTTP/API；
- 4→2正式直接链退出；
- Auth完整移交3号位；
- APS_Auth Owner退出；
- Rule/Parameter DDL Owner退出；
- 2号位核心业务Owner不迁移。

## 29.3 内部接口闭环

- 5号位通过稳定Query Service获取普通计划结果；
- 5号位通过Application Service中转Demand Protection/MES；
- 3号位可治理并调用Run/Candidate执行；
- 3/5不复制2号位逻辑；
- 不形成逐笔远程调用主链。

## 29.4 权限与数据库Owner闭环

- 2号位消费合法Scope；
- 不维护第二套权限；
- 2号位Own核心运行表；
- 3号位Own APS_Auth / Rule / Parameter / Strategy；
- 5号位Own复杂事实 / Manual ETA结构；
- 跨Owner变更有明确Owner。

---

# 三十、一句话交付要求

> **2号位V1不需要重写主流程。本轮只需继续完成既有Pegging、数量闭环、ScheduleRun/PlanVersion、Candidate、Solver调用和结果持久化整改，同时把不再属于自己的Auth和4号位-facing HTTP职责干净退出：Auth整体移交3号位；治理入口由3号位调用2号位内部运行服务；非治理页面由5号位调用2号位Query/Application Service。2号位仍然保持Demand/Supply、Pegging、Allocation、Demand Protection、Effective ETA、AvailableTime、LogicalProductionDemand、Solver调用、Candidate业务执行和运行结果持久化的唯一业务Owner。**

---

# 2026-09-01 Domain专项冻结增量（本版新增，冲突时仅此专项口径覆盖历史Domain表述）

# APS V1 2号位增量开发实施包 v1.3
## Domain专项冻结对齐补充版

**版本**：v1.3  
**日期**：2026-09-01（北京时间）  
**基于版本**：《APS V1 2号位增量开发实施包 v1.2（20260901职责与接口冻结对齐版）》  
**上位业务基线**：《APS V1 最终全部流程与业务基线 v1.2（Domain专项冻结修订版）》  
**专项依据**：《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》

---

# 0. 本版定位

本版是2号位实施包的**Domain专项增量补充**。

继续完整继承v1.2已经冻结的：

- `SchedulingOrchestrator`主外壳；
- Demand / Supply；
- Pegging；
- DemandBalance / SupplyBalance；
- Allocation；
- Lock；
- Demand Protection；
- PI选择与Position消费；
- Planning-only Purchase Placeholder；
- Effective ETA；
- AvailableTime；
- LogicalProductionDemand；
- 计划良率；
- `IFiniteCapacityScheduler`唯一Solver接口；
- ScheduleRun / PlanVersion；
- Candidate；
- MES资格；
- 事务与持久化；
- 4→2正式HTTP退出；
- Auth迁3；
- 2→3 / 2→5内部服务边界。

本版只补Domain专项。

---

# 1. Domain专项对2号位的新增职责

2号位是Domain**运行执行Owner**，新增明确职责：

1. 读取本次Run冻结的有效Domain集合；
2. 读取当前有效DomainDefinition快照/配置；
3. 按业务属性将Order / Demand归入真实DomainKey；
4. 构建单Domain执行上下文；
5. 读取并消费Domain_Dependency；
6. 按拓扑顺序串行逐Domain执行；
7. 前序成功Domain共享Resource占用传给后续Domain；
8. 跨Domain传递分段Quantity-Time；
9. 上游失败时阻断依赖下游本次发布；
10. 记录Domain级性能指标。

---

# 2. Domain不等于ProductFamily

从本版开始，2号位代码不得继续默认：

```text
Domain = ProductFamily
DomainKey = ProductFamily.Code
```

V1只允许两类：

- 产品族域（FAMILY）；
- 工厂+产品族域（FACTORY_FAMILY）。

真实DomainKey必须来自当前有效DomainDefinition。

---

# 3. 归域责任

正式链路：

```text
当前Run冻结Domain集合
        ↓
DomainDefinition
        ↓
Order / Demand业务属性
        ↓
2号位匹配
        ↓
真实DomainKey
```

## 3.1 FAMILY

按ProductFamily匹配。

## 3.2 FACTORY_FAMILY

按Factory + ProductFamily匹配。

2号位不得：

- 再写另一套隐藏归域规则；
- 用PlanVersionId当DomainKey；
- 用ScheduleRunId当DomainKey；
- 看到跨Factory就自动认定跨Domain。

---

# 4. SchedulingOrchestrator保持保护

`SchedulingOrchestrator`继续保留，不重写。

Domain专项只要求在现有主链中形成稳定单Domain执行边界。

推荐技术形态可以类似：

```text
ExecuteDomainAsync(domainExecutionContext)
```

具体名称不强制。

要求：

> **主外壳保持，单Domain执行边界清晰。**

---

# 5. 夜间FULL正式执行方式

当前V1：

```text
1 ScheduleRun
↓
ExpectedDomainKeysJson
↓
Domain_Dependency
↓
拓扑排序
↓
同层稳定排序
↓
串行逐Domain执行
```

每个Domain：

```text
构建Domain Context
↓
Demand / Supply装载
↓
Pegging
↓
LogicalProductionDemand
↓
DomainSolveRequest
↓
1号位Solver
↓
结果校验
↓
PlanVersion持久化
↓
发布 / FAILED
```

---

# 6. 不恢复旧Domain并发Job

本版明确禁止重新启用：

- `DomainSchedulingJob`正式并发；
- `DomainLayerCoordinatorJob`并发编排；
- 多Hangfire Domain Job并发；
- 多套Solver实例业务架构。

若历史代码仍保留，可作为历史兼容，但不得进入正式V1主链。

---

# 7. Domain_Dependency消费

2号位必须按真实DomainKey读取依赖。

不得再默认：

```text
Upstream = ProductFamily.Code
Downstream = ProductFamily.Code
```

2号位负责：

- 拓扑排序；
- 循环依赖检测；
- 同层稳定排序；
- 上游完成状态判断；
- 下游是否可执行判断。

---

# 8. 跨Domain Quantity-Time

原冻结规则不变。

上游成功后向下游传：

```text
Material
+
Quantity
+
AvailableTime
```

必须保留切片：

```text
40件 @ 15日
60件 @ 17日
```

禁止压平。

该对象属于当前ScheduleRun内存上下文，不建设VirtualInventoryBalance持久化真相。

---

# 9. FULL共享资源占用传递

这是Domain专项新增的关键运行闭环。

前序Domain成功后：

```text
FinalTask
↓
提取共享Resource占用区间
↓
写入当前ScheduleRun共享资源占用上下文
↓
后续Domain构建DomainSolveRequest时加入不可用Block
```

后续Domain不得与已成功前序Domain在真实共享Resource上重叠占用。

---

# 10. 共享资源规则不扩张

本版不建设：

- Domain资源配额；
- Domain资源借用；
- 跨Domain配额调整；
- Resource Quota；
- 虚拟子日历平台。

当前只做：

> **真实占用时间块传递。**

---

# 11. Candidate保持严格单Domain

原Candidate冻结全部保持。

一个Candidate仍然：

```text
1 ScheduleRun
→ 1 DomainKey
→ 1 Base ACTIVE PlanVersion
→ 1 Candidate PlanVersion
```

其它Domain当前ACTIVE共享资源占用：

> 作为外部Domain不可移动资源阻挡块（ExternalDomainResourceBlocks）进入1号位Solver。

不得挤动其它Domain。

---

# 12. FULL失败传播

例如：

```text
C → B → A
```

若B失败：

- B本次PlanVersion = FAILED；
- A本次不继续发布新ACTIVE；
- A保留上一ACTIVE；
- A记录被上游失败阻断；
- 无依赖Domain继续；
- ScheduleRun最终汇总PARTIAL_SUCCESS或FAILED。

FAILED恢复仍：

> 创建新ScheduleRun。

不得把旧FAILED改回RUNNING。

---

# 13. ScheduleRun开始时的Domain冻结

3号位负责治理并形成合法Domain集合。

2号位运行时必须只消费：

`ExpectedDomainKeysJson`

中的Domain集合。

运行中即使DomainDefinition发生变更：

> 当前Run不得重新读取后改变执行集合。

---

# 14. DomainDefinition的消费边界

3号位是DomainDefinition治理Owner。

2号位只负责：

- 读取；
- 缓存当前Run所需快照；
- 归域；
- 执行。

2号位不得：

- 提供DomainDefinition管理页面；
- 自己维护另一套Domain配置；
- 把DomainDefinition放进RuleSet/ParameterSet；
- 自己设计Domain DSL。

---

# 15. PlanVersion保持每Domain独立

2号位必须继续保证：

```text
一个ScheduleRun
→ 多个Domain
→ 每个Domain一个独立PlanVersion
```

不得：

- 一个PlanVersion承载全部Domain；
- 为每个Domain重新创建独立ScheduleRun；
- 为Domain并行提前改写PlanVersion生命周期。

---

# 16. Domain级性能埋点

新增最低指标：

- DomainKey；
- DemandCount；
- LogicalProductionDemandCount；
- FinalTaskCount；
- ContextBuildMs；
- PeggingMs；
- SolverMs；
- PersistMs；
- TotalMs。

ScheduleRun继续记录总耗时。

目标：

> 用真实数据判断约10万Task / 90天 / 15分钟目标是否满足。

---

# 17. 当前不实现Domain并行

当前正式V1串行逐Domain。

未来若真实压测证明必要，只允许：

```text
无未完成上游依赖
AND
无共享Resource
```

的Ready Domain并行。

本版只要求单Domain执行边界具备未来可并行性，不要求现在开发并行。

---

# 18. 与Pegging原冻结关系

本次只增加：

```text
先归域
↓
再进入原Pegging
```

不改变：

- Demand排序；
- Supply顺序；
- Allocation；
- PI承接；
- Lock；
- Demand Protection；
- Strict Binding。

---

# 19. 与跨厂当前冻结关系

跨厂仍分两类：PI大工艺接续型继续“PI单一Supply + PI Position”；本版重写厂间出荷指示（SH）链。

## 19.1 SH识别

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

第一层普通SALES_ORDER Demand池必须排除INTER_FACTORY SH订单本身。

SH在目标侧作为单一Supply；普通Demand↔SH允许多对多，由2号位本次Pegging形成Allocation。

不得要求5号位提供固定Demand↔SH关系。

## 19.2 第二层整单判断

```text
CLOSED / CANCELLED
→ 退出

OPEN + ERP_InterplantInTransit_View存在该SH号
→ 已进入跨厂履行
→ 不生成源厂履约Demand

OPEN + 在途视图不存在该SH号
→ 生成一条完整源厂履约Demand
→ RequiredQty = Order.Quantity
```

查询成功0行属于合法成功。

不得使用TransitQty倒算未生产量，也不得恢复Transit / Received / ZP / BP多Supply。

## 19.3 源厂履约Demand

一张SH只形成一条完整源厂Demand，不按第一层Allocation份额拆分。

继续执行：

```text
SH源厂Demand
→ PI
→ PI Position
→ 只有缺口形成新增生产
```

`RequiredQty=Order.Quantity`不等于“新增生产Order.Quantity”。

## 19.4 SourceFactoryId重新归域

第二层使用SourceFactoryId重新按DomainDefinition归域。

不得：

- 使用目标FactoryId作为源厂；
- 永久改写原Order.FactoryId；
- 永久覆盖原Order.DomainKey；
- 沿用第一层目标DomainKey。

## 19.5 SH整单优先级

整张SH源厂Demand取：

> SH自身业务优先关系 与 当前全部上层Allocation中最强业务优先关系 的更强者。

3号位配置，2号位执行。

## 19.6 SH整单时间

同一SH只能整单一次入库。

同Domain：源厂Demand + LT + 目标侧后续进入同一个DomainSolveRequest，由1号位一次求解。

跨Domain：源Domain返回真实SourceReadyTime，2号位加冻结SH跨厂LT后形成目标Domain SH AvailableTime。

2号位不得自行估算SourceReadyTime。

> 跨Factory不等于跨Domain。是否跨Domain必须以真实DomainKey为准。

---

# 20. 与AvailableTime原冻结关系

完全不变：

```text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

2号位继续计算：

- Effective ETA；
- AvailableTime。

Domain只决定计算边界，不改变时间规则。

---

# 21. 需要增量修改的代码位置

不要求按类名强制实现，以下是职责检查点。

## 21.1 Run入口

确认：

- ExpectedDomainKeysJson读取；
- Domain集合冻结后不漂移。

## 21.2 Domain归域

新增/收口一个稳定归域能力。

输入：

- DomainDefinition；
- Factory；
- ProductFamily。

输出：

- DomainKey。

## 21.3 Domain执行单元

从现有主链抽出清晰单Domain边界。

不重写内部Pegging。

## 21.4 共享资源上下文

在当前ScheduleRun内保存前序成功Domain共享资源占用。

## 21.5 失败传播

按Domain_Dependency判断阻断。

## 21.6 性能记录

增加Domain级计时。

---

# 22. 可以直接保留的代码

若现有代码已经具备以下能力，应优先复用：

- DomainKey字段；
- PlanVersion.DomainKey；
- ExpectedDomainKeysJson；
- Domain_Dependency；
- DomainSolveRequest；
- ExternalDomainResourceBlocks；
- Quantity-Time DTO；
- ScheduleRun汇总；
- PARTIAL_SUCCESS。

禁止因文档升版重新开发等价能力。

---

# 23. 必须退出的旧假设

必须清理正式主链中的：

1. `DomainKey = ProductFamily.Code`；
2. 跨ProductFamily自动等于跨Domain；
3. 同ProductFamily自动等于同Domain；
4. 每个Domain独立Hangfire Job并发；
5. Domain间共享Resource互不感知；
6. 通过VirtualInventoryBalance跨版本传递Quantity-Time。

---

# 24. 新增验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| D01 | FAMILY Domain | 同ProductFamily按定义进入同Domain |
| D02 | FACTORY_FAMILY | 同ProductFamily不同Factory可进入不同Domain |
| D03 | DomainKey | 不再默认等于ProductFamily.Code |
| D04 | Run启动后DomainDefinition变化 | 当前Run不变化 |
| D05 | ExpectedDomainKeysJson | 当前Run只执行冻结集合 |
| D06 | Domain_Dependency | 使用真实DomainKey |
| D07 | FULL | 当前串行逐Domain |
| D08 | 前序共享Resource占用 | 后续Domain不可重叠 |
| D09 | 无共享Resource | 当前仍串行，不提前并行 |
| D10 | Candidate | 严格单Domain |
| D11 | Candidate共享设备 | 外Domain ACTIVE作为不可移动Block |
| D12 | 跨Domain物料40+60 | 保持分段Quantity-Time |
| D13 | 上游失败 | 依赖下游本次不发布 |
| D14 | 无关Domain | 可继续运行 |
| D15 | FAILED恢复 | 创建新Run |
| D16 | 跨Factory但同FAMILY Domain | 不自动认定跨Domain |
| D17 | 同ProductFamily不同FACTORY_FAMILY | 正确认定两个Domain |
| D18 | 性能埋点 | 按Domain和Run均可统计 |
| D19 | VirtualInventoryBalance | 不作为正式持久化真相 |
| D20 | 旧Domain并发Job | 不进入正式主链 |

---

# 25. 本版明确不做

不新增：

- Domain DSL；
- Domain规则引擎；
- Domain自动学习；
- Domain资源配额；
- Domain借用平台；
- MultiDomain Candidate；
- MultiDomain Solver；
- VirtualInventoryBalance平台；
- 当前Domain并行；
- 第二套Pegging；
- 第二套Solver。

---

# 26. 完成定义（Domain专项）

2号位Domain专项完成必须满足：

- 真实DomainDefinition可被消费；
- Order/Demand能归入真实DomainKey；
- Domain不再等同ProductFamily；
- FULL按冻结Domain集合执行；
- 按真实Domain依赖拓扑执行；
- 当前串行逐Domain；
- 单Domain执行边界清晰；
- 每Domain独立PlanVersion；
- 跨Domain Quantity-Time保留切片；
- 前序Domain共享Resource占用可传给后续Domain；
- Candidate外Domain ACTIVE资源不可移动；
- 上游失败正确阻断；
- 无关Domain继续；
- Domain级性能可测；
- 原Pegging、Allocation、AvailableTime、Demand Protection、Solver接口不漂移；
- 不新增Domain平台化设计。

---

# 27. 一句话交付要求

> **2号位本轮不要重写SchedulingOrchestrator和Pegging，只需把现有主链真正收口成“Run启动冻结Domain集合 → 按DomainDefinition真实归域 → 按Domain依赖串行逐Domain执行 → 每Domain独立Pegging、求解和PlanVersion持久化 → 前序共享资源占用与跨Domain Quantity-Time传给后续Domain → 上游失败正确阻断”的稳定运行闭环，同时彻底退出“Domain=ProductFamily”和旧多Job并发假设。**


---

---

# v1.5 PI Position执行起点上下文专项冻结对齐增量

## 一、本版专项定位

本版只补齐以下实施链：

```text
MES执行事实
        ↓
5号位
PI Position
+
NextOperationContext
        ↓
2号位
Pegging / Allocation
+
LogicalProductionDemand
        ↓
1号位
有限产能Solver
        ↓
FinalTask
```

本版不重新打开：

- ProductionInstruction作为Supply；
- PI Position不是Supply；
- Demand↔Supply Pegging；
- 90天统一有限产能Solver；
- Domain定义与串行逐Domain执行；
- FinalTask由1号位生成；
- 2号位现有SchedulingOrchestrator保护区。

---

## 二、2号位在本链路中的唯一新增/明确职责

2号位必须：

1. 在当前Run / Domain上下文中批量调用或消费5号位提供的PI Position与NextOperationContext结果；
2. 保留ProductionInstruction作为唯一PI Supply身份；
3. 先按既有Pegging规则完成Demand↔PI数量承接；
4. 在已选PI内部消费互斥Position和对应NextOperationContext；
5. 将StartStage、必要时StartOperation及其数量份额组装进LogicalProductionDemand；
6. 保留AllocationSequence、PI号和LogicalProductionDemand之间的稳定数量血缘；
7. 将完整LogicalProductionDemand放入DomainSolveRequest；
8. 统一校验并持久化1号位返回结果。

2号位不得：

- 直接根据MES原始报工重新计算PI Position；
- 直接根据OperationProgress自行推导NextOperation；
- 因不同StartOperation创建新的Supply；
- 因不同StartOperation建立新的Pegging Allocation；
- 在Solver前生成最终Operation Task。

---

## 三、PI Position与NextOperationContext的消费规则

### 3.1 先选PI，再消费执行上下文

正式顺序保持：

```text
Demand
↓
选择ProductionInstruction Supply
↓
确定Demand↔PI承接数量
↓
消费PI内部Position / NextOperationContext
↓
形成LogicalProductionDemand
```

禁止：

```text
先把所有PI按Operation打散
↓
再做Demand↔Operation级供需匹配
```

### 3.2 一个PI可以形成多个排程起点片段

如果同一PI剩余数量客观处于不同执行进度，例如：

```text
200件下一工序 = NC
800件下一工序 = 挤丝
```

2号位可以在已确定PI承接关系之后形成多个LogicalProductionDemand排程片段：

```text
PI001 / 200 / StartOperation=NC
PI001 / 800 / StartOperation=挤丝
```

这属于：

> **同一PI Supply内部的排程执行起点切片。**

它不是：

- 两个新的Supply；
- 两条新的Operation级Pegging关系；
- Operation级Position平台。

### 3.3 数量闭合

对同一PI本次参与排程的执行起点切片，必须满足：

```text
Σ ExecutionStartSliceQty
=
本次需要继续生产的PI数量
```

并且每一数量份额只能对应一个当前NextOperationContext。

不得因为：

- PI Position；
- MES OperationProgress；
- NextOperationContext；

重复增加PI RemainingQty。

---

## 四、LogicalProductionDemand字段语义补充

LogicalProductionDemand至少需要能够表达：

- AllocationSequence；
- DemandKey；
- MaterialId；
- FactoryId；
- ProductionInstructionNo（适用时）；
- NetOutputQty；
- PlannedProcessQty；
- StartStageCode；
- StartOperationCode（必要时）；
- RequiredAvailableTime；
- DemandSequence；
- IsUnlocated；
- Routing引用/可定位上下文。

其中：

### StartStageCode

表示本排程片段应从哪个大工艺（Stage）继续。

### StartOperationCode

仅在需要精确到小工序时使用，表示：

> 1号位从Routing中的哪个Operation开始展开后续工序。

StartOperationCode是5号位计算出的NextOperationContext在2号位求解输入中的承载语义，不由2号位重新计算。

---

## 五、DomainSolveRequest组装要求

2号位组装单Domain DomainSolveRequest时，必须保证：

- Continuation Slice与Free Slice都进入同一次求解；
- 每个Continuation保留独立ContinuationKey；
- 工序级LastReportResourceCode只映射为PreferredResourceCode；
- SH源厂Demand携带SourceFactoryId与正确源厂DomainKey；
- 同Domain SH源厂→跨厂LT→目标侧后续依赖一次性进入请求；
- 不把MESWorkOrderNo生命周期逻辑交给1号位；


```text
LogicalProductionDemand
+
执行起点上下文
+
RoutingNetwork
+
ResourceContext
+
Material Quantity-Time
+
StrategySnapshot
+
ImmovableFacts
```

一次性交给1号位。

1号位不需要也不允许：

- 再读取MES；
- 再调用5号位；
- 再解析PI Position；
- 再判断哪个Operation已经完成。

---

## 六、与UNLOCATED的关系

PI Position无法可靠定位时，仍按既有UNLOCATED冻结规则处理。

2号位不得为了“必须给StartOperation”而自行猜测MES进度。

只有在当前冻结规则已有明确保守起点时，才将该保守起点传给1号位，并继续保留：

- IsUnlocated；
- Issue；
- MES不可下发限制。

---

## 七、FULL与Candidate的处理边界

### 7.1 夜间FULL

FULL使用本次DataCutoffTime对应的PI Position与NextOperationContext。

2号位不得在Pegging循环中逐PI实时调用5号位；应按批量事实准备原则一次准备/批量消费。

### 7.2 白天Candidate

Candidate仍严格遵守当前冻结的数据刷新边界。

本次专项不引入：

- 白天逐PI实时MES重算平台；
- Candidate内新的Position生命周期；
- NextOperationContext独立缓存平台。

如Candidate使用Base ACTIVE已冻结的执行上下文或既有新增PI保守规则，沿用上位业务冻结，不由本实施包另行扩张。

---

## 八、与1号位联调新增验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| N51 | PI全部未开工 | LogicalProductionDemand从Routing首个合法Operation开始 |
| N52 | PI部分已完成前序Operation | 已完成份额不重复进入前序Operation排程 |
| N53 | 同PI存在两个NextOperation切片 | 可形成多个排程片段，数量闭合且Supply身份仍只有一个PI |
| N54 | 2号位收到NextOperationContext | 原样/等价映射到LogicalProductionDemand，不重新解析MES |
| N55 | 1号位收到StartOperation | 从该Operation展开后续Routing |
| N56 | 1号位尝试自行读取MES | 验收失败 |
| N57 | 2号位按Operation新建Supply | 验收失败 |
| N58 | Demand↔PI↔Operation三级Allocation | 验收失败 |
| N59 | NextOperationContext切片数量超过PI可排数量 | Domain失败，不得发布 |
| N60 | UNLOCATED | 按既有保守起点处理并保持不可MES下发 |
| N61 | 旧TaskNo/MES工单反向决定当前Q | 验收失败 |
| N62 | IN_PROGRESS只从Q扣掉但不进入Solver | 验收失败 |
| N63 | 多MES工单合并成一个Continuation对象 | 验收失败 |
| N64 | E>Q时抬高Q | 验收失败 |
| N65 | Q=0/E>0仍创建正常Demand | 验收失败 |
| N66 | 2号位直接解释MES原始报工 | 验收失败 |
| N67 | 工单级新增LastReportResourceCode/RemainingNetQty | 验收失败 |
| N68 | 工序级LastReportResourceCode未透传为PreferredResource | 验收失败 |
| N69 | APS旧Task连续错误生成新TaskNo | 验收失败 |
| N70 | 无TaskNo外部MES连续又创建第二MES工单 | 验收失败 |
| N71 | INTER_FACTORY SH进入第一层普通Demand池 | 验收失败 |
| N72 | SH恢复Transit/Received/ZP/BP多Supply | 验收失败 |
| N73 | 用TransitQty倒算SH未生产量 | 验收失败 |
| N74 | OPEN+视图无SH号未生成RequiredQty=Order.Quantity源厂Demand | 验收失败 |
| N75 | OPEN+视图有SH号仍生成源厂Demand | 验收失败 |
| N76 | 查询成功0行被当接口失败 | 验收失败 |
| N77 | SH第二层使用目标FactoryId或原DomainKey | 验收失败 |
| N78 | INTER_FACTORY缺SourceFactory时静默使用目标Factory | 验收失败 |
| N79 | SH第二层按上层Allocation拆多条Demand | 验收失败 |
| N80 | 2号位自行估算SourceReadyTime | 验收失败 |
| N81 | SH目标侧生成多个AvailableTime | 验收失败 |
| N82 | SupplyFact_Pipeline装载INTERPLANT_IN_TRANSIT | 验收失败 |
| N83 | PeggingSupplyAllocation以INTERPLANT_IN_TRANSIT形成独立Supply Allocation | 验收失败 |
| N84 | 根据TransitQty/ReceivedQty推导SH未生产份额 | 验收失败 |

---

## 九、代码整改检查点

不强制新增特定类名，但必须检查：

### 9.1 SchedulingContext / Domain Context

能够批量承接：

- PI Position；
- NextOperationContext。

### 9.2 LogicalProductionDemand构建处

必须：

- 使用5号位已经给出的执行起点；
- 保留PI/Allocation血缘；
- 支持同PI多排程起点片段；
- 不把执行起点片段重新当Supply。

### 9.3 DomainSolveRequest Mapper

必须把：

- StartStage；
- StartOperation（必要时）；
- Routing关联；

正确传给1号位。

### 9.4 结果持久化

本次不要求因NextOperationContext单独新增一套持久化平台。

如现有运行快照/DTO足以表达，优先复用。

任何DDL新增必须等字段说明和数据库设计专项复核后再实施。

---

## 十、本版明确不做

不新增：

- Operation级Pegging；
- Operation Supply；
- Operation Position平台；
- NextOperationContext独立业务表平台；
- 第二套MES进度解释器；
- 2号位PI Position Calculator；
- 1号位MES Loader；
- 新Solver；
- 新Domain模型；
- 新Candidate模型。

---

## 十一、v1.5完成定义

2号位完成本专项必须同时满足：

- 5号位计算PI Position与NextOperationContext；
- 2号位只消费，不重新解释MES；
- ProductionInstruction仍是唯一PI Supply身份；
- Demand↔PI Pegging粒度不变；
- 同PI不同执行进度可以转成多个LogicalProductionDemand排程片段；
- 排程片段数量严格闭合；
- StartStage / StartOperation正确进入DomainSolveRequest；
- 1号位能从指定起点继续Routing；
- 已完成Operation不被重复排产；
- FinalTask仍由1号位形成；
- 不新增Operation级Pegging/Position/Supply；
- 不提前修改DDL。

---

## 十二、一句话交付要求

> **2号位v1.6继续保持数量真相和运行主流程Owner：先按当前事实形成Pegging与LogicalProductionDemand Q，再消费5号位标准化IN_PROGRESS事实形成逐MES工单Continuation/Free切片；SH保持一张SH一个Supply、第一层普通Demand承接、第二层整单源厂Demand按SourceFactoryId重新归域。2号位不直接解释MES、不恢复SH多Supply、不自行估算SourceReadyTime。**



---
