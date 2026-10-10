# APS《核心排产全流程走查》V3.17 冻结符合性审查报告

**日期**：2026-08-12  
**审查性质**：只做冻结符合性审查，不重新设计业务。  
**上位依据**：
1. 《APS V1最终全部流程与业务基线_v1.0_20260812》；
2. 《APS Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版》；
3. 《APS有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版》；
4. 《APS_三份冻结业务到六份权威技术文档_差异与修改矩阵_v1.0_20260812》。

---

## 1. 审查结论

**通过。**

本轮没有发现需要“重新打开冻结决策”的新业务问题。

V3.17已经把旧V3.16中与冻结业务冲突的关键主链全部收敛，并保留了未被推翻的技术保护区。

---

## 2. P0项核对结果

| 主题 | 结果 | 说明 |
|---|---|---|
| SALES_ORDER不二次扣成品库存 | 通过 | 顶层数量从ERP已净掉成品库存后的需生产量开始 |
| PI总量边界 | 通过 | ERP Quantity-ReceivedQty为总边界；MES/XC/在途只解释位置 |
| Demand排序 | 通过 | 计算层→有序规则段→第一命中→段内排序；PriorityScore退出主链 |
| 2/3/5职责 | 通过 | 3治理、2执行Pegging、5复杂事实；无逐笔插件主链 |
| FinalTask职责 | 通过 | 2号位逻辑生产需求→1号位FinalTask |
| AllocationTaskShare | 通过 | 明确Task与Demand多对多，Task.OrderId非权威 |
| 计划良率 | 通过 | NetRequiredQty与PlannedProcessQty分开；PI不膨胀 |
| 90天统一Solver | 通过 | 不再用8～90天ROUGH_CUT作为正式第二套Solver |
| Demand/Supply双边闭合 | 通过 | 原子Allocation、余额闭合、硬错误Domain失败 |
| PI Position | 通过 | 先选PI再Position；UNLOCATED；PI自消费禁止 |
| 采购/VMI/Pipeline | 通过 | 正式V1事实；不再为空链路 |
| Planning-only Purchase Placeholder | 通过 | 仅内存、ESTIMATED/NOT_COMMITTED、非确定CTP |
| 两类跨厂 | 通过 | Stage Handoff与Inter-factory Order顺序均已重写 |
| ShippingTask退出 | 通过 | 运输只形成LeadTime→AvailableTime |
| 跨Domain Quantity-Time | 通过 | 明确保留分段，不压成单一AvailableTime |
| 跨Domain真实LT | 通过 | 统一2天不再是业务权威 |
| 夜间上游失败门禁 | 通过 | 下游不发布新ACTIVE；无关Domain继续发布 |
| 人工失败恢复 | 通过 | 新建ScheduleRun，原FAILED历史不改 |
| 普通Allocation跨版本重新竞争 | 通过 | 无普通关系稳定性偏好 |
| Candidate RemainingSupply | 通过 | 只扣已消耗/严格绑定/保护/不可逆/失效 |
| Candidate动态影响传播 | 通过 | Scope不截断；MaxImpactedOrders仅提示阈值 |
| 白天共享资源 | 通过 | 外域ACTIVE为不可移动阻挡块 |
| 夜间共享资源 | 通过 | 同Run共享资源必须真实互斥，禁止双占 |
| 跨Domain插单CTP | 通过 | 多个单Domain WHATIF按拓扑串行 |
| Candidate采用 | 通过 | WHATIF不自动正式，仅最小人工确认 |
| Demand Protection | 通过 | 与Strict Binding、Execution Constraint分开 |
| Firm/Frozen | 通过 | 与Pegging Lock分开；不建FrozenZoneSnapshot平台 |
| Explanation | 通过 | 1号位原生产出，2号位聚合持久化 |
| MES下发资格 | 通过 | Candidate/UNLOCATED/无PI占位不得下发 |
| MES五态 | 通过 | 保留0～4，不建PAUSE/RESUME正式闭环 |
| 规则引擎两模式 | 通过 | 第一命中；默认+例外覆盖；冻结快照内存消费 |
| PSA | 通过 | 内部镜像，不是第二真相源，同事务持久化 |
| InventoryBalance.AllocatedQty | 通过 | 不作为跨PlanVersion永久占用真相 |
| 无法按期≠Solver失败 | 通过 | 返回最佳可行日期和根因 |
| 排程终点 | 通过 | 最终生产/装配入ZP/BP |

---

## 3. 保护区核对

以下旧V3.16中正确且未被冻结业务推翻的结构在V3.17仍被保护：

- ScheduleRun + PlanVersion；
- ExpectedDomainKeysJson独立字段；
- PARTIAL_SUCCESS；
- 白天Candidate严格单Domain；
- OrderCanonical→Order版本快照；
- Realtime BOM RequestDetail / Workset / StageDetail / CrossFactoryEdge / RAW链；
- OrderBomRequestLink；
- RoutingOperation / RoutingDependency / OperationResourceEligibility；
- MaterialStageDeptContext；
- ProcessCodeDict.ERPProperty真实来源；
- MES五态；
- Resource故障不自动重排；
- RuleSet / ParameterSet / StrategyProfile六张治理表；
- 2号位SchedulingOrchestrator、ScheduleContext、SupplyPool、IFiniteCapacityScheduler等外壳。

未因“简化”而擅自删除。

---

## 4. 已清除的主要旧口径

V3.17正文不再把以下内容视为V1当前方案：

- 5号位通用Voucher/插件驱动Pegging；
- 全局PriorityScore；
- 2号位提前Operation Task；
- TaskSplitVoucher提前拆批；
- ShippingTask有限物流主链；
- PipelineSupplies V1为空；
- StageProgress直接由1号位重新判断PI位置；
- 8～90天ROUGH_CUT作为正式远期Solver；
- Base ACTIVE全部Allocation不可重用；
- ScopeJson不可扩大而截断真实影响；
- MaxImpactedOrders硬停止；
- Candidate必须完整OA审批才能采用；
- FrozenZoneSnapshot正式平台；
- VirtualInventoryBalance持久平台；
- 上游Domain失败但下游仍发布新ACTIVE；
- FAILED PlanVersion原地改回RUNNING/BUILDING。

---

## 5. 本次未触发业务重新打开

审查过程中没有发现：

- 两条冻结业务规则互相不可兼容；
- 冻结业务与现有系统事实存在不可实现的硬冲突；
- 必须为了2号位代码而改变业务；
- 必须新增V1平台性架构才能跑通主链。

因此：

> **本轮不重新打开任何冻结决策。**

---

## 6. 下一步

按已冻结工作流，下一份进入正式修订的是：

> **《APS 各类基础数据分层承接与演变总表》**

本轮只把该文档当前态横向承接关系向冻结业务对齐，历史版本说明可保留但必须明确历史已废止，不重新讨论业务。

