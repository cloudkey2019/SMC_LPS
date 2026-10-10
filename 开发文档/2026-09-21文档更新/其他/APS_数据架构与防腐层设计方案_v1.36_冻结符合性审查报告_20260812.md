# APS 数据架构与防腐层设计方案 v1.36 冻结符合性审查报告

**审查日期**：2026-08-12  
**审查对象**：《APS 数据架构与防腐层设计方案 v1.36 冻结对齐版》  
**审查性质**：冻结符合性审查（Conformance Review），不是业务重新设计

---

## 1. 上位依据

本轮只允许以下三份冻结业务作为业务判断来源：

1. 《APS V1最终全部流程与业务基线_v1.0_20260812》；
2. 《APS Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版》；
3. 《APS有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版》。

同时用已经完成冻结对齐的两份技术文档做横向交叉检查：

- 《APS核心排产全流程走查 V3.17 冻结对齐版》；
- 《APS各类基础数据分层承接与演变总表 v3.32 冻结对齐版》。

审查中禁止以旧DDL、旧接口、旧代码示例、历史版本说明或“未来更优雅”为理由改变冻结业务。

---

## 2. 总结论

> **PASS：v1.36当前正文已经与三份冻结业务和前两份冻结技术文档对齐，没有发现需要重新打开任何冻结业务决策的问题。**

本轮发现的问题均属于旧技术文档同步问题，已在v1.36中修正或明确历史化；没有产生新的业务方案。

---

## 3. P0/P1关键项复核

| 冻结项 | v1.35主要问题 | v1.36处理 | 结论 |
|---|---|---|---|
| F-004 需求排序 | PriorityScore仍出现在当前Order装载语义 | 当前正文取消PriorityScore权威；排序归3号位治理、2号位执行 | 通过 |
| F-006 FinalTask职责 | 2号位Task/ShippingTask实例化 | 改为2号位逻辑生产需求+Context，1号位FinalTask | 通过 |
| F-007/008 Task多对多与数量语义 | 单Order归属、只有Task.Quantity | 当前架构明确AllocationTaskShare，多需求合并；Task.Quantity=净产出，PlannedProcessQty=产能量 | 通过 |
| F-009/010 计划良率 | 主链缺失 | 加入NetRequired/NetOutput与PlannedProcessQty；明确不假设BOM ScrapRate | 通过 |
| F-011~015 90天Solver | 近7天详细、8~90天LeadTime粗排；拆批边界旧 | 删除双轨语义；90天统一Solver；拆合批归1号位 | 通过 |
| F-016/017/062/063 Pegging数量真相 | 余额/PSA/事务边界容易双真相 | Demand/Supply双边余额、原子Allocation、PSA极简镜像、同Domain事务一次闭合；不永久改InventoryBalance.AllocatedQty | 通过 |
| F-018~022 PI Position | StageProgress被1号位直接消费；无正式Position主链 | 5号位Position计算→一张最小快照→2号位消费→1号位只收解析起点；含UNLOCATED和PI自消费禁止 | 通过 |
| F-023~025A 采购/VMI | PipelineSupplies V1固定空 | 正式纳入V1；ETA优先级、Quantity-Time、Planning-only Purchase Placeholder、采购参考需用日全部落架构 | 通过 |
| F-026~029 跨厂 | 顺序旧、ShippingTask仍在主链 | 两类跨厂顺序冻结；运输只LeadTime→AvailableTime；ShippingTask退出V1有限产能主链 | 通过 |
| F-030/031 跨Domain Supply | 单一虚拟库存、默认2天 | 多段Quantity-Time；不建VirtualInventoryBalance；真实工厂/Stage LT | 通过 |
| F-032~034 夜间失败恢复 | 仅Mismatch Risk+人工建议，缺下游发布门禁/新Run | 上游失败阻断依赖下游新ACTIVE；无关域继续；人工恢复新建ScheduleRun并按拓扑自动扩范围 | 通过 |
| F-035A/036 Candidate重新Pegging | Base ACTIVE全部Allocation扣死 | 普通未锁Allocation释放重竞争；只固定已消耗/严格绑定/Protection/不可逆执行 | 通过 |
| F-037~040 Candidate影响传播 | Scope/MaxImpactedOrders容易硬截断 | Scope只初始范围；真实影响动态传播；过大时同Solver单Domain全可移动重排 | 通过 |
| F-041/041A 共享资源 | 跨Domain共享资源边界不完整 | 白天外域ACTIVE不可移动；夜间同一物理设备真实互斥；不建配额/借用平台 | 通过 |
| F-042~045 跨Domain WHATIF/采用 | 多域重排与Scenario/审批较重 | 多个单Domain WHATIF拓扑串行；Scenario可选；WHATIF一次同时产CTP+影响；最小人工授权确认 | 通过 |
| F-047~051 Lock/Firm/Frozen | Protection缺失；Freeze/Voucher混义 | Strict/Protection/Execution三分；Protection按份额；Firm/Frozen只是排程锚点；不建FrozenZoneSnapshot | 通过 |
| F-052~054 Explanation | 跨域/物流容易平台化，Reason来源职责散 | 不持久化虚拟库存；1号位原生产Explanation，2号位聚合；DUE_DATE_RISK为结果 | 通过 |
| F-055/056 MES下发 | 下发资格过多依赖Freeze/Voucher | Candidate、无PI占位、UNLOCATED、缺真实采购执行依据的占位计划均不得正式下发 | 通过 |
| F-059~061 规则参数 | 5号位逐笔插件/审批味道仍重 | 3号位治理冻结版本；1/2号位内存消费；5号位只复杂事实；6表保护但不新增重审批 | 通过 |
| F-064/065 失败与终点 | 业务延期与计算失败容易混；物流扩展终点 | 数量/约束完整性失败阻断发布；无法按期是正常结果；客户生产终点到ZP/BP | 通过 |

---

## 4. 特别检查：是否重新长出过度设计

本轮对以下对象做了专门反查，v1.36已明确不进入V1正式主链：

- 有限物流Task / ShippingTask Solver；
- 第二套远期Rough-Cut Solver；
- FrozenZoneSnapshot平台；
- VirtualInventoryBalance持久化平台；
- MultiDomain Candidate / CandidateGroup / 原子多域激活；
- 跨Domain资源配额、借用、容量预约平台；
- 通用插件中心、DSL、SQL/Python/JS规则脚本；
- 全搜索SolverTrace/因果图平台；
- PI Position Header+Slice双生命周期状态机。

**结论：未发现为了“架构完整”而重新引入上述平台。**

---

## 5. 保护区复核

以下既有设计继续保护，没有因为本轮减法被误删：

- ScheduleRun / PlanVersion；
- ExpectedDomainKeysJson独立运行级字段；
- PARTIAL_SUCCESS；
- Realtime BOM RequestDetail/Workset/StageDetail/CrossFactoryEdge链；
- OrderBomRequestLink；
- RoutingOperation / RoutingDependency / OperationResourceEligibility；
- MaterialStageDeptContext及现有部门维度；
- MES五态0~4及三类进度快照；
- 规则治理6表；
- 2号位现有Orchestrator / ScheduleContext / SupplyPool / IFiniteCapacityScheduler等外壳。

这些对象后续可以做最小字段/接口对齐，但不因“简化”进行无必要的大范围删除或重构。

---

## 6. 与前两份技术文档的交叉一致性

### 6.1 与《核心排产全流程走查 V3.17》

一致项：
- 2号位逻辑生产需求→1号位FinalTask；
- 90天统一Solver；
- PI Position链；
- 两类跨厂供给顺序；
- Planning-only Purchase Placeholder；
- Candidate动态传播与单Domain兜底；
- 白天共享资源外域ACTIVE阻挡；
- 夜间依赖失败阻断下游发布；
- 新ScheduleRun人工恢复；
- MES下发资格。

未发现流程文档与架构文档出现新的双口径。

### 6.2 与《基础数据分层承接与演变总表 v3.32》

一致项：
- Inventory现货链与未来Supply并行；
- PI Position一张最小快照；
- PSA内部镜像定位；
- NetOutputQty/PlannedProcessQty；
- 跨Domain Quantity-Time；
- 不建FrozenZoneSnapshot/VirtualInventoryBalance；
- 0~5号位职责边界。

未发现横向数据承接与本防腐层设计发生漂移。

---

## 7. 仍需后续技术文档落地、但不需要重新业务裁决的事项

这些不是未决业务，只是后续接口/字段/DDL必须具体化：

1. PI Position最小快照表的最终字段名、主键/唯一键、索引；
2. `PlannedProcessQty`在Task/DTO/接口中的最终字段定义；
3. `AllocationTaskShare`物理表及多对多唯一性/数量闭合约束；
4. Demand Protection与Strict Binding复用锁实体时的`LockType`字段和值域；
5. Pipeline各真实ODS视图的最终字段映射与5号位数据来源；
6. Planning-only Purchase Placeholder只在DTO/内存Context中的结构；
7. 夜间跨Domain共享资源互斥的具体技术实现方式（约束已冻结，禁止演化为配额平台）；
8. 人工重算追溯字段是复用现有字段还是增加一个轻量前序Run引用；
9. Candidate最小人工确认如何映射现有代码/表，原则是不得被完整OA审批硬绑定。

这些应在后续《集成接口设计》《字段说明》《DDL》中按最小实现确定，不需要0号位重新讨论业务。

---

## 8. 最终裁决

**业务冻结状态：保持。**  
**是否需要重新打开冻结决策：否。**  
**《数据架构与防腐层设计方案》是否可以进入冻结对齐技术基线：可以。**

下一步按冻结工作流进入第四份权威技术文档：《APS 集成接口设计》。
