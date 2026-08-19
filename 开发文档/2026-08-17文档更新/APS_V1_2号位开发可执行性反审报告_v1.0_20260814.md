# APS V1 2号位开发可执行性反审报告

**版本**：v1.0  
**日期**：2026-08-14  
**审查对象**：《APS V1 2号位增量开发实施包 v1.1 执行对齐版》  
**审查目的**：检查实施包是否既符合冻结业务，又能基于2号位现有代码做增量整改；禁止把业务正确但无法落到当前代码的“理想架构”直接交给开发。

---

## 一、审查结论

> **通过，可交2号位继续整改。**

本轮没有发现需要重新打开任何APS V1业务冻结决策的问题。

但是本轮确认了一个重要事实：

> 2号位AI此前把“Task由谁执行INSERT SQL”与“FinalTask由谁在算法上生成”混成了同一个问题。

现已在v1.1实施包中明确：

- 1号位负责**生成FinalTask的物理排程结果**；
- 2号位负责**最终数据库持久化**；
- PeggingOrchestrator不得在Solver前生成/持久化FinalTask；
- SchedulingOrchestrator仍属于2号位现有主流程外壳。

---

## 二、现有代码可保留性复核

### 2.1 可直接保留

- SchedulingOrchestrator总入口与编排外壳；
- Hangfire触发方式；
- ScheduleRun / PlanVersion现有框架；
- SchedulingContext数据装载方式；
- SupplyPool / RemainingQty原型；
- MES/Stage进度装载；
- IFiniteCapacityScheduler接口方向；
- DomainSolveRequest/Result方向；
- PeggingLedgerEntry / AllocationTaskShareDto等DTO方向；
- Dapper/现有事务模板。

**结论**：没有必要重写工程骨架。

### 2.2 旧路径应退出主链但不要求物理删除

- DefaultBatchSplitter；
- Solver前TaskDraft/Task INSERT；
- 旧外层FiniteCapacitySolver；
- PassThrough Stub生产路径；
- FrozenZoneSnapshot主链；
- VirtualInventoryBalance持久平台方向；
- InventoryBalance.AllocatedQty运行期UPDATE；
- PSA事务外重复写。

**结论**：采用“停止调用优先，物理删除后置”的增量方式。

---

## 三、四个开发歧义已经消除

### 3.1 LogicalProductionDemand

已明确为：

> 轻量内存DTO，不建表，不是Task，不复用PeggingLedgerEntry。

开发可直接新增DTO，对现有数据库无破坏。

### 3.2 Pegging结果持久化

已明确：

> Pegging阶段只形成Draft；Solver成功后一次性正式持久化。

避免为了事务整洁要求大改ScheduleRun/PlanVersion外壳。

### 3.3 AllocationTaskShare

已明确：

> 连接通用AllocationSequence与FinalTask，不等同于PSA。

这允许继续复用现有AllocationTaskShare表与DTO，只需改持久化语义。

### 3.4 TryAtomicAllocation

已明确：

> 双边余额扣减 + 校验 + AllocationRecord；不生成Task。

可以在现有AllocateSupplyToDemand周边增量改造，不需要另建Allocation Engine。

---

## 四、对当前2号位AI修改方向的约束

后续如果2号位AI再次提出以下方案，应直接驳回：

1. PeggingOrchestrator独占FinalTask INSERT，并让Solver只UPDATE时间/资源；
2. 把SchedulingOrchestrator归到3号位；
3. Task.Status使用`Scheduled`；
4. Pegging先COMMIT PSA，Solver以后再单独写Task；
5. 用`Priority int`重新做全局排序；
6. 为LogicalProductionDemand建新表；
7. 为Allocation新建第二套Ledger平台；
8. 让2号位提前合并多个逻辑生产需求以减少Task；
9. 让TryAtomicAllocation直接生成Task；
10. 通过DELETE优化掩盖错误Task生命周期。

---

## 五、允许的最小代码动作

2号位当前可以直接开始：

1. 新增`LogicalProductionDemand`内存DTO；
2. 将现有Allocation成功结果收敛成通用`AllocationRecord`；
3. `TryAtomicAllocation`同时接收并扣减DemandBalance/SupplyBalance；
4. AllocationSequence在成功Allocation时生成；
5. 移除Pegging阶段FinalTask INSERT；
6. 扩展DomainSolveRequest承载LogicalProductionDemand；
7. 1号位接口未完成前可以用Stub做联调，但Stub结果不得作为正式生产排程；
8. 将PSA改为Draft，延迟到最终结果事务；
9. AllocationTaskShare改真实多对多；
10. 最终结果持久化层统一Task清理/INSERT/PSA/TaskShare/Pegging/Explanation。

---

## 六、实施风险判断

| 风险 | 等级 | 判断 |
|---|---|---|
| 改Task生命周期影响现有代码 | 中 | 通过停止旧调用+复用Orchestrator可控，不需重写 |
| 1号位Solver尚未完成 | 中 | 可先冻结Request/Result接口，用Stub联调 |
| PSA延迟持久化影响现有查询 | 中 | 需要全文搜索消费者，但不改变业务 |
| Task.OrderId历史依赖 | 中 | 保留兼容字段，真实归属改TaskShare |
| DemandBalance改造 | 中 | 已有SupplyPool原型，按对称结构增量实现 |
| 新增过多表 | 低 | 本轮明确禁止，LogicalProductionDemand/AllocationRecord均不建表 |
| 业务再次漂移 | 低 | 4个实现歧义已写死，无需重新裁决 |

---

## 七、反审结论

> **2号位实施包现在已经从“业务正确”进一步收敛到“基于现有代码可执行”。**

下一步不应继续扩写2号位架构设计，而应锁死三个跨号位接口：

- 1↔2：有限产能Solver；
- 2↔5：PI Position / Complex Facts；
- 2↔3：规则参数与运行生命周期。

这些接口一旦冻结，2号位即可按阶段A～F持续开发，不需要等待其它号位全部完成。
