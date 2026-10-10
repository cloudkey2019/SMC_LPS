# APS 集成接口设计 v1.26 冻结符合性审查报告

**日期**：2026-08-12  
**审查对象**：《APS 集成接口设计规范 v1.26 冻结对齐版》  
**审查性质**：冻结符合性审查，不重新设计业务

---

## 一、审查基线

本轮只允许接口层向以下冻结层收敛：

1. 《APS V1最终全部流程与业务基线 v1.0》；
2. 《APS Pegging供需承接与分层计算业务说明 v1.1 冻结对齐版》；
3. 《APS有限产能排产与滚动90天计划业务说明 v1.1 冻结对齐版》；
4. 已完成的《核心排产全流程走查 V3.17 冻结对齐版》；
5. 已完成的《各类基础数据分层承接与演变总表 v3.32 冻结对齐版》；
6. 已完成的《数据架构与防腐层设计方案 v1.36 冻结对齐版》；
7. 《三份冻结业务到六份权威技术文档差异与修改矩阵》。

审查规则：不因旧接口、旧Adapter、OA流程、旧DTO、已有表或“更标准”的接口形式重新打开业务冻结。

---

## 二、差异矩阵逐项核对

| 冻结项 | v1.26处理 | 结果 |
|---|---|---|
| `BuildRemainingSupplyContextAsync`不能把Base全部Allocation扣死 | 改为当前物理Supply减真实消耗/Strict Binding/Demand Protection/不可逆份额；普通未锁Allocation可重新竞争 | ✅ |
| `BuildScheduleContextAsync`交给1号位的是逻辑生产需求而非最终Task | 补完整Supply、Lock、PI Position、计划良率、执行锚点、外域共享资源阻挡等Context语义 | ✅ |
| 1号位统一Solver接口 | 保留现有 `IFiniteCapacityScheduler.SolveAsync`，新增明确Request/Result业务契约 | ✅ |
| FinalTask由1号位生成 | `DomainSolveResult`返回FinalTask、AllocationTaskShare、TaskDependency、Explanation | ✅ |
| AllocationTaskShare多对多 | 接口明确支持多对多，禁止一对一压缩 | ✅ |
| PI Position服务 | 增加2→5 PI Position Calculator契约；先选PI、再消费PI内Position | ✅ |
| 采购/VMI正式进入V1 | 第四章从“V1.1/V2预留”改为V1正式Supply契约 | ✅ |
| Planning-only Purchase Placeholder | 定义为仅内存ESTIMATED/NOT_COMMITTED Supply；不建单、不生Task、不下ERP | ✅ |
| ScopeJson不截断真实影响 | 11字段Schema保留，但改为初始/发起范围；真实影响允许动态扩展 | ✅ |
| MaxImpactedOrders非硬上限 | 改为警戒/人工确认阈值，禁止截断依赖链 | ✅ |
| Candidate局部影响传播 | 明确2号位给变化种子，1号位沿工艺/物料/资源关系传播，必要时单Domain完整重排 | ✅ |
| 白天共享资源 | 其它Domain ACTIVE占用作为不可移动阻挡块 | ✅ |
| 跨Domain WHATIF | 复用多个单Domain WHATIF，Quantity-Time逐层传递；不新增MultiDomain Candidate/API体系 | ✅ |
| 跨Domain Quantity-Time | 新增 `DomainSupplySlice` 最小运行时语义，多段数量-时间不得汇总丢失 | ✅ |
| Candidate采用 | 取消完整ApprovalRecord硬依赖，只保留最小人工确认与激活审计 | ✅ |
| OA审批 | 保留为可选/兼容外围流程，不作为V1主链硬前置 | ✅ |
| FAILED人工恢复 | 复用现有FULL ScheduleRun入口创建新Run；旧FAILED/PARTIAL_SUCCESS不改写；不建Retry平台 | ✅ |
| MES下发资格 | ACTIVE + 真实执行依据；Candidate/无PI占位/UNLOCATED/未转真实采购承诺任务拒绝下发 | ✅ |
| ShippingTask | 从生产有限产能接口体系退出；仅历史/“禁止生成”语义保留 | ✅ |
| Rule/Parameter | 3号位发布冻结快照，1/2号位运行期一次装载；不逐Task回调动态插件 | ✅ |

---

## 三、旧v1.25当前态冲突清理结果

原v1.25当前正文存在几类已经过时的接口语义：

- `PipelineSupplies`固定为空；
- `BuildRemainingSupplyContext`以Base ACTIVE全部Allocation为扣减基础；
- 2号位在Solver前实例化Task/ShippingTask；
- ScopeJson被解释为不可扩大的最终运行边界；
- `MaxImpactedOrders`达到上限即停止传播；
- Candidate激活必须依赖完整OA ApprovalRecord。

v1.26均已从**当前正文权威语义**中移除。历史版本说明仍保留，但文档顶部已明确标记其不再具有当前V1权威性。

自动扫描当前正文确认：

- “PipelineSupplies为空集合”现行口径：0处；
- “V1返回空集合”现行Loader口径：0处；
- “2号位实例化Task/ShippingTask”现行主链：0处；
- “达到MaxImpactedOrders即停止”现行规则：0处；
- “审批必须通过”现行Candidate激活硬校验：0处。

`ShippingTask`仍有少量出现，但均用于“不得生成/退出V1有限物流Task体系”的否定性说明，不是现行调用链。

---

## 四、与前三份已修技术文档交叉核对

### 4.1 与《核心排产全流程走查 V3.17》

一致：
- 2号位Pegging/逻辑生产需求 → 1号位FinalTask；
- 一套90天统一有限产能Solver；
- PI Position由5号位推导；
- Candidate动态传播和单Domain fallback；
- 跨Domain多单域WHATIF；
- 新ScheduleRun恢复失败链；
- MES只下发正式可执行Task。

### 4.2 与《演变总表 v3.32》

一致：
- Pipeline/采购/VMI正式V1；
- Planning-only Placeholder只在运行时内存；
- Demand Protection/Strict Binding/Execution Constraint分层；
- Quantity-Time跨Domain传递；
- 无VirtualInventoryBalance/FrozenZoneSnapshot平台。

### 4.3 与《防腐层 v1.36》

一致：
- PI Position最小一表语义；
- `NetOutputQty / PlannedProcessQty`双数量；
- 外域ACTIVE共享资源阻挡；
- OA审批不硬绑定主链；
- 不建设有限物流Task、第二套远期Solver、MultiDomain Candidate、Retry平台。

未发现三份技术文档之间出现新的业务漂移。

---

## 五、2号位现有代码保护审查

v1.26没有要求推倒2号位现有工程骨架，继续保护：

- `SchedulingOrchestrator`；
- ScheduleContext装载框架；
- `SupplyPool.RemainingQty`余额原型；
- StageProgressSnapshot装载；
- `IFiniteCapacityScheduler.SolveAsync`接口方向；
- DomainSolveRequest/Result方向；
- AllocationTaskShare DTO方向；
- 统一事务持久化框架。

需要2号位后续补齐/调整的是接口内部语义：DemandBalance、双边原子扣减、Lock、Allocation/逻辑Ledger、TaskShare多对多、PI Position接入、Candidate可重竞争Supply、单一真实Solver等。属于“保留外壳、补核心”，不是重写。

---

## 六、防过度设计复核

本轮没有新增以下平台：

- MultiDomain Candidate / CandidateGroup；
- 有限物流Task/ShippingTask Solver；
- FrozenZoneSnapshot平台；
- VirtualInventoryBalance平台；
- 第二套90天Rough-Cut Solver；
- RetryGroup/Retry Workflow；
- 动态插件/DSL/脚本总线；
- Candidate全量ImpactGraph持久化；
- 完整OA审批作为V1主链硬前置。

跨Domain CTP和失败人工重算都明确**复用既有单Domain WHATIF / FULL ScheduleRun编排入口**，没有为业务补充再造新平台。

---

## 七、最终结论

> **《APS 集成接口设计规范 v1.26 冻结对齐版》通过冻结符合性审查。**

本轮没有发现需要“重新打开冻结决策”的问题；发现的差异均属于旧接口文档向冻结业务同步的问题。

因此第四份权威技术文档可以进入冻结状态。下一步按既定顺序进入第五份：

> **《APS 数据库字段说明文档》冻结对齐修订。**

字段阶段只允许把已经冻结的业务/接口语义映射成最小必要字段，不得因“字段完整性”重新增加平台或改变业务。
