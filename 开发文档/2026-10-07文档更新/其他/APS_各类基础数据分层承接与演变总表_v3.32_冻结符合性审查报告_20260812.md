# APS《各类基础数据分层承接与演变总表》v3.32 冻结符合性审查报告

**审查日期**：2026-08-12  
**审查对象**：`APS_各类基础数据分层承接与演变总表_v3.32_冻结对齐版.md`  
**上位标准**：
1. 《APS V1最终全部流程与业务基线_v1.0_20260812》
2. 《APS Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版》
3. 《APS有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版》
4. 《APS_三份冻结业务到六份权威技术文档_差异与修改矩阵_v1.0_20260812》
5. 已完成冻结对齐的《APS核心排产全流程走查_V3.17_冻结对齐版》

---

## 1. 审查原则

本轮只做Conformance Review（冻结符合性审查），不重新评审业务方案。

允许：补缺失、清冲突、消歧义、校正技术承接关系。  
禁止：因“更优雅、更标准、未来V2、更容易编码”而改变冻结业务。

如果发现冻结基线本身无法运行，必须显式提出“重新打开冻结决策 F-xxx”。本轮**未发现需要重新打开任何冻结决策**。

---

## 2. 本轮修改性质

v3.31当前正文存在多项已被后续冻结业务覆盖的技术表达，因此v3.32采用“历史变更记录保留 + 当前正文整体冻结对齐”的方式处理。

历史v3.31及更早changelog继续保留追溯，但已经在文档顶部增加明确覆盖声明：历史中的旧口径不能作为当前实现依据。

本轮没有删除BOM Realtime、OrderBomRequestLink、ScheduleRun/PlanVersion、ExpectedDomainKeysJson、MaterialStageDeptContext、Routing三件套、MES五态和规则治理6表等保护对象。

---

## 3. 差异矩阵逐项复核

| 对齐项 | v3.32处理结果 | 结论 |
|---|---|---|
| F-004 Demand排序退出全局PriorityScore | Order行与红线明确PriorityScore仅历史/展示兼容 | 通过 |
| F-006 FinalTask由1号位生成 | Task行、职责表明确2号位仅逻辑生产需求/Context | 通过 |
| F-008/F-009 计划良率与PlannedProcessQty | 新增计划良率横向承接行，区分净量/加工量 | 通过 |
| F-019/F-020 PI Position最小快照与异常降级 | 新增PI Position行，一张最小快照，多行互斥，UNLOCATED | 通过 |
| F-022 StageProgress职责 | 改为5号位PI Position输入，1号位只收解析后的执行起点 | 通过 |
| F-023 采购/VMI/Received/Pipeline进入V1 | Pipeline不再定义固定空链路 | 通过 |
| F-025 Planning-only Purchase Placeholder | 新增运行时内存占位行；不落承诺、不生成Task/采购单 | 通过 |
| F-026 STAGE_HANDOFF顺序 | 目标M→PI→PI Position→缺口新增生产 | 通过 |
| F-027 INTER_FACTORY_ORDER顺序 | 目标BS/KS→SH；SH内在途→同SH Received→未生产 | 通过 |
| F-028 有限物流Task退出 | 当前正文明确ShippingTask不进入V1有限产能主链 | 通过 |
| F-030 跨Domain Quantity-Time分段 | 新增多段供给承接，不建VirtualInventoryBalance | 通过 |
| F-031 跨Domain真实LT | 明确真实工厂/Stage LT，固定2天退出业务权威 | 通过 |
| F-035A 夜间普通Allocation重新竞争 | 红线明确普通关系无跨版本稳定性偏好 | 通过 |
| F-036 Candidate普通未锁Allocation可释放 | RemainingSupply语义改为当前物理供给减不可重竞争份额 | 通过 |
| F-037/F-039 影响传播不被Scope/Horizon截断 | ScopeJson仅发起边界；真实物理传播由1号位决定 | 通过 |
| F-041 白天共享资源阻挡 | 外域ACTIVE作为不可移动阻挡块 | 通过 |
| F-041A 夜间跨Domain共享资源互斥 | 明确Domain独立版本≠资源隔离，禁止设备双占 | 通过 |
| F-042 跨Domain WHATIF串行 | C→B→A多个单Domain Candidate，前端汇总 | 通过 |
| F-043 CTP/影响复用一次WHATIF | 红线明确不为Purpose重复跑Solver | 通过 |
| F-044 Scenario非必选主链 | 当前正文不再把Scenario定义为普通CTP强制容器 | 通过 |
| F-045 Candidate最小人工确认 | 明确不把完整OA审批作为V1硬前置 | 通过 |
| F-047/F-048 Demand Protection | 新增Strict/Protection/Execution三区分和份额锁语义 | 通过 |
| F-051 FrozenZoneSnapshot不建平台 | Firm/Frozen行改为跨版本锚点 | 通过 |
| F-052 VirtualInventoryBalance不持久化 | 跨Domain行明确只在内存传Quantity-Time | 通过 |
| F-055 MES下发资格 | Candidate/无PI/UNLOCATED不得下发 | 通过 |
| F-065 排程终点 | 当前正文未再以ShippingTask扩展客户生产终点 | 通过 |

---

## 4. 保护区检查

以下结构在本轮只调整职责/语义说明，未做删除式重构：

- ScheduleRun / PlanVersion；
- ExpectedDomainKeysJson；
- PARTIAL_SUCCESS；
- Realtime BOM三表与READY语义；
- OrderBomRequestLink；
- RoutingOperation / RoutingDependency / OperationResourceEligibility；
- MaterialStageDeptContext；
- MES五态；
- 规则与参数治理6表；
- Inventory现有资格/可用明细/Balance链；
- 2号位现有Orchestrator/Context/SupplyPool等代码外壳。

**结论：保护区未被本轮“为简化而重构”。**

---

## 5. 过度设计回流检查

当前v3.32正文没有把以下内容重新引入V1：

- MultiDomain Candidate / CandidateGroup；
- 有限物流Task/车辆/月台/班次资源模型；
- FrozenZoneSnapshot平台；
- VirtualInventoryBalance持久化平台；
- 第二套90天远期Solver；
- 跨Domain共享资源配额/借用平台；
- 动态DSL/脚本/通用插件平台；
- 全局多域原子激活平台；
- PI Position Header+Slice双生命周期平台。

**结论：未发现过度设计回流。**

---

## 6. 与第一份权威文档V3.17的交叉检查

关键接缝一致：

1. V3.17中的夜间FULL拓扑、失败阻断下游、人工新Run恢复，在v3.32对应到ScheduleRun/PlanVersion和跨Domain数据承接；
2. V3.17中的2号位逻辑生产需求→1号位FinalTask，在v3.32 Task横向行一致；
3. V3.17中的PI Position、采购/VMI、Planning-only Purchase Placeholder，在v3.32分别有独立数据承接位置；
4. V3.17中的Candidate动态传播和外域ACTIVE阻挡，在v3.32 Candidate/共享资源行一致；
5. V3.17中的不建有限物流Task、FrozenZoneSnapshot、VirtualInventoryBalance，在v3.32均保持一致。

**未发现V3.17与v3.32之间需要重新裁决的业务冲突。**

---

## 7. 尚未处理、必须留给后续文档的技术落点

本轮只修“数据演变总表”，因此下列内容仍然需要后续权威文档落实，不能误认为已经完成代码/DDL改造：

- Pipeline/采购/VMI/Received具体ODS契约字段与Loader实现；
- PI Position最小快照的字段、索引和DDL；
- Demand Protection的字段/LockType；
- Task.PlannedProcessQty；
- AllocationTaskShare多对多数量约束；
- Candidate RemainingSupply接口语义；
- ScopeJson字段仍可物理保留，但接口说明必须取消“物理传播硬截断”解释；
- ShippingTask历史DDL/接口如果仍存在，后续标deprecated或退出主链；
- FrozenZoneSnapshot若环境历史存在，后续只做deprecated/停止生成链；
- `Domain_Dependency.DefaultLeadTimeDays`若物理字段保留，语义必须改成真实LT缓存/兼容，不得硬编码2天；
- Candidate采用的最小人工确认接口与审计字段。

这些是后续《数据架构与防腐层》《集成接口》《字段说明》《DDL》的技术对齐任务，**不是未决业务问题**。

---

## 8. 最终结论

**《APS 各类基础数据分层承接与演变总表》v3.32通过冻结符合性审查。**

- 未发现需要重新打开冻结业务决策的问题；
- 未发现对两份专项冻结说明的回退；
- 未发现与《核心排产全流程走查》V3.17的实质业务冲突；
- 未发现过度设计回流；
- 历史changelog中的旧口径已经明确降为追溯信息，不能再作为当前开发依据。

下一步按冻结工作流进入第三份权威技术文档：**《APS 数据架构与防腐层设计方案》冻结对齐修订**。
