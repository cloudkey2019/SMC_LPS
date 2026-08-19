# APS V1 0号位总体项目验收包（冻结版）

**版本**：v1.0  
**日期**：2026-08-14  
**适用对象**：0号位（项目负责人/业务负责人）  
**文档性质**：项目总体验收清单，不是开发设计说明  
**上位依据**：APS V1三份业务冻结文档 + 六份技术冻结文档 + 1～5号位实施包 + 三个关键接口冻结说明

---

# 一、这份验收包的目的

0号位不需要再参与实现细节讨论。

本文件只回答三个问题：

1. **1～5号位到底各自必须交付什么？**
2. **什么情况下APS V1才算真正闭环，而不是“局部功能做出来了”？**
3. **哪些问题属于必须上线前解决，哪些可以作为V1已知限制保留？**

总体原则：

> **不因为技术实现困难重新打开已冻结业务；不因为文档写得完整就认定代码已完成；最终必须以真实端到端运行结果验收。**

---

# 二、验收优先级

## P0：不通过就不能进入V1正式上线

P0主要包括：

- 数量不闭合；
- 同一物理Supply重复消费；
- 2号位仍在Solver前生成FinalTask；
- 1号位仍只是Stub；
- Task/Allocation/TaskShare追溯断裂；
- 共享资源双占；
- 上游Domain失败后下游仍错误发布新ACTIVE；
- Candidate能够挤动其它Domain ACTIVE；
- Planning-only Purchase Placeholder被当成正式承诺；
- Candidate/UNLOCATED/无PI规划Task错误下发MES；
- 同一PlanVersion被多个Worker并发重复执行；
- 事务失败却留下部分正式结果；
- 规则版本在同一次Run中漂移；
- PI Position总量无法闭合；
- 跨厂Transit/Received重复计算。

## P1：正式上线前原则上应闭合

包括：

- Explanation根因不完整；
- Candidate影响摘要不完整；
- 部分参数仍靠硬编码Fixture；
- UI部分页面尚未完全完善；
- 某些Source Fact仍需要人工补录；
- 性能距离目标存在一定差距但可稳定完成。

## P2：可以进入V1已知限制

包括：

- UI样式优化；
- 次级统计报表；
- 更复杂Setup优化；
- 更高级规则维护体验；
- V2才需要的跨Domain更复杂优化；
- 年度详细排程；
- 高级仿真/Scenario；
- 通用插件/DSL/图平台。

---

# 三、0号位最终只看五条总红线

1. **业务是否仍然是冻结业务**
2. **数量是否闭合**
3. **时间是否真实有限产能**
4. **版本/事务是否可追溯且不产生半结果**
5. **各号位是否没有越界重新创造第二套真相**

只要其中任一条失败：

> 不允许因为“基本能跑”而判通过。

---

# 四、1号位验收

## 4.1 必须交付

- 真实`IFiniteCapacityScheduler`；
- DomainSolveRequest/Result正式实现；
- FinalTask；
- AllocationTaskShare；
- TaskDependencies；
- Explanation；
- Unscheduled/Unfulfilled；
- Candidate局部修复；
- 性能测试报告；
- S01～S22测试结果。

## 4.2 必须确认

### A. 不是Stub

正式环境：

> `PassThroughSchedulerStub`不得作为最终Solver。

### B. FinalTask真正由1号位形成

不能只是：

> 旧TaskDraft + 补ResourceId + 补Start/End。

必须由：

- LogicalProductionDemand；
- Routing；
- Resource；
- Calendar；
- Material Quantity-Time；
- Firm/Frozen/Execution；
- Strategy；

共同形成。

### C. 真有限产能

同一资源同一时间：

> 不能双占。

### D. 拆批/合批

允许：

- 一个Allocation拆多个Task；
- 多个Allocation合一个Task；

但TaskShare必须闭合。

### E. 不能静默丢数量

排不进去：

> 返回Unscheduled/Unfulfilled。

不能把100件悄悄排成80件。

---

# 五、2号位验收

2号位是整个V1最关键的主链验收对象。

## 5.1 必须交付

- 保留现有Orchestrator主外壳；
- DemandBalance；
- SupplyBalance；
- 原子Allocation；
- AllocationSequence；
- Lock执行；
- PI Position消费；
- Timed Supply；
- Planning-only Placeholder；
- LogicalProductionDemand；
- 完整DomainSolveRequest；
- Solver结果统一持久化；
- AllocationTaskShare多对多；
- 单Domain事务；
- Candidate重新竞争；
- FULL失败链；
- MES下发资格；
- T01～T22测试结果。

---

## 5.2 必须确认的主链

正式流程必须是：

```text
Demand / Supply
→ Pegging
→ Allocation
→ LogicalProductionDemand
→ DomainSolveRequest
→ 1号位Solver
→ FinalTask
→ 2号位统一事务持久化
```

禁止仍存在：

```text
Pegging
→ Task INSERT
→ Solver只补时间/资源
```

---

## 5.3 Task数据库写入

允许：

> 2号位是Task表唯一数据库写入方。

但必须满足：

> Task内容来自`solveResult.FinalTasks`。

“谁执行INSERT SQL”和“谁生成FinalTask”必须区分。

---

## 5.4 Allocation

必须确认：

- DemandBalance和SupplyBalance同一Allocation中同步扣减；
- AllocationSequence在成功Allocation时生成；
- Strict Binding；
- Demand Protection；
- Execution不可逆事实；
- 资格校验；
- 失败不留下半扣。

---

## 5.5 事务

Solver成功后，至少统一持久化：

- FinalTask；
- PeggingSupplyAllocation；
- AllocationTaskShare；
- 物理Pegging/TaskDependency；
- ScheduleExplanationFact；
- 必要Summary；
- PlanVersion状态。

任何关键一步失败：

> Domain不得发布新ACTIVE。

---

## 5.6 并发

同一PlanVersion：

> 只能被一个Worker执行。

必须有原子Claim/锁机制。

不得通过：

- 删除唯一索引；
- 全局AllocationSequence；

掩盖并发Bug。

---

# 六、3号位验收

## 6.1 必须交付

- 六张规则/参数治理表后端；
- 版本发布；
- FrozenStrategySnapshot；
- Priority Segment；
- Demand Protection参数；
- Procurement参数；
- Solver Strategy；
- Candidate Guardrail；
- ScheduleRun生命周期；
- Candidate最小确认；
- R01～R22测试结果。

## 6.2 必须确认

### A. 一次Run只用一份冻结版本

运行中页面发布新规则：

> 当前Run不得变化。

### B. 不恢复PriorityScore

需求排序必须是：

> CalculationLayer → Priority Segment → First Match → Segment Sort。

### C. 不做逐笔在线规则调用

不能：

> 每次Allocation都RPC问3号位。

### D. 不建设万能规则平台

不得出现：

- DSL；
- 脚本；
- 任意SQL；
- Plugin Marketplace；
- 通用流程编排。

---

# 七、4号位验收

## 7.1 必须交付

- 排产总览；
- 订单详情；
- 资源甘特图；
- CTP/插单；
- Candidate对比与确认；
- Explanation；
- PI Position/供给追溯；
- Rule/Parameter页面；
- Run/Version/MES状态；
- U01～U22测试结果。

## 7.2 必须确认

### A. UI不直接写运行库

所有状态改变必须走后端服务。

### B. CTP必须区分确定与估算

依赖Planning-only Purchase Placeholder：

> 必须明确`ESTIMATED / NOT_COMMITTED`。

### C. Candidate严格单Domain

跨Domain只是链式WHATIF汇总展示。

### D. CTP/Impact Analysis永不激活

页面不得提供“采用”按钮。

### E. FAILED恢复

必须创建新ScheduleRun。

---

# 八、5号位验收

## 8.1 必须交付

- PI Position；
- Timed Supply；
- 两类跨厂事实；
- PO/VMI/Arrived；
- BOM/Workset；
- ERPProperty；
- Issue；
- F01～F22测试结果。

## 8.2 必须确认

### A. PI总量边界

> PI Position合计 = ERP RemainingQty。

不能把MES WIP/XC/Transit加到RemainingQty上。

### B. Position互斥

同一物理份额不能同时存在多个Position。

### C. UNLOCATED

定位不了：

> 进入UNLOCATED，不丢数量。

### D. 跨厂两类模式

- STAGE_HANDOFF
- INTER_FACTORY_ORDER

必须分开。

### E. Received

必须按同SH号绑定。

### F. Procurement

真实PO/VMI/Arrived进入Timed Supply。

5号位不得生成Planning-only Placeholder表或采购Task。

---

# 九、三个关键接口总验收

# 9.1 1↔2

检查：

- 2号位给LogicalProductionDemand；
- 1号位返回FinalTask；
- AllocationTaskShare完整；
- Material Quantity-Time完整；
- Routing三件套完整；
- Execution/Firm/Frozen完整；
- Candidate外Domain Resource Block完整。

如果1号位仍要求2号位先生成Task：

> 接口未通过。

---

# 9.2 2↔5

检查：

- 5号位返回PI Position事实；
- 2号位先选PI再消费Position；
- 5号位不替2号位分配Demand；
- 采购/跨厂事实Stable；
- 复杂源字段没有泄漏到Pegging主流程。

---

# 9.3 2↔3

检查：

- 3号位返回版本化Snapshot；
- 2号位一次加载；
- Run中不变；
- 2号位实际执行Demand/Supply规则；
- 3号位不成为逐笔决策服务。

---

# 十、夜间FULL端到端验收

建议至少跑一套包含以下事实的真实测试数据：

- SALES_ORDER；
- PRODUCTION_INSTRUCTION；
- 多层BOM；
- 多个PI；
- MES Stage WIP；
- XC；
- 跨厂Transit；
- Inventory；
- Arrived-not-inbound；
- PO；
- VMI；
- 无PO采购缺口；
- Demand Protection；
- Firm/Frozen；
- 跨Domain依赖；
- 共享资源；
- 资源不足；
- 延期订单。

必须从头走到：

```text
数据准备
→ BOM
→ PI Position
→ Demand/Supply
→ Pegging
→ LogicalProductionDemand
→ Solver
→ FinalTask
→ 事务持久化
→ Domain发布
→ ScheduleRun汇总
→ UI展示
→ MES下发资格
```

---

# 十一、数量闭环验收

必须至少验证：

## 11.1 Demand

对每个Demand：

> AllocatedQty + RemainingUnfulfilledQty = RequiredQty

## 11.2 Supply

每个物理Supply：

> AllocatedQty <= AvailableQty

## 11.3 PI

> Σ PositionQty = ERP RemainingQty

## 11.4 Production

对需要生产的Allocation：

> Σ AllocationTaskShare.ShareQty = NetOutputQty

## 11.5 FinalTask

对FinalTask：

> Σ ShareQty <= Task.Quantity

正常合批场景应闭合到净合格产出。

---

# 十二、时间闭环验收

必须验证：

- Task不早于Material AvailableTime；
- 后工序不早于前工序可用；
- Resource不双占；
- Calendar有效；
- Setup有效；
- Firm/Frozen不移动；
- Quantity-Time分段不被压平；
- 跨厂LT正确；
- 采购AvailableTime正确；
- Delay是可解释结果。

---

# 十三、版本闭环验收

## 13.1 FULL

一次ScheduleRun：

> 多个Domain PlanVersion。

## 13.2 上游失败

直接/间接依赖下游：

> 本次不得发布新ACTIVE。

无关Domain：

> 可以继续发布。

## 13.3 PARTIAL_SUCCESS

必须准确反映部分成功。

## 13.4 人工恢复

> 新建ScheduleRun。

历史FAILED/PARTIAL不可修改。

---

# 十四、Candidate闭环验收

必须验证：

1. Candidate严格单Domain；
2. Base ACTIVE不被直接改；
3. 普通未锁Allocation释放重新竞争；
4. Strict/Protection/Execution不释放；
5. 其它Domain共享Resource ACTIVE占用为硬阻挡；
6. 真实影响动态传播；
7. MaxImpactedOrders不截断正确性；
8. 超局部范围时Fallback同Domain全可移动重排；
9. WHATIF不自动激活；
10. 正式采用经过最小人工确认。

---

# 十五、跨Domain CTP验收

必须验证：

> 多个单Domain WHATIF串行。

例如：

```text
C Domain
→ 40件15日 + 60件17日
→ B Domain
→ A Domain
```

不能：

> 压成100件17日。

不能建设MultiDomain Candidate作为V1替代方案。

---

# 十六、采购场景验收

至少测试三类：

## A. 有真实PO

使用真实AvailableTime。

## B. 有VMI

作为独立SupplyType。

## C. 没有任何正式供给

生成：

> Planning-only Purchase Placeholder

要求：

- 不落库成正式供给；
- 不生成采购单；
- 不生成生产Task；
- CTP标记非确定承诺；
- 正式PO出现后下一次运行自然替换。

---

# 十七、MES闭环验收

只有符合全部条件的Task才能下MES：

- ACTIVE；
- 有合法执行身份；
- 非Candidate；
- 非UNLOCATED；
- 非无PI规划占位；
- 不依赖未承诺采购占位；
- 在合法下发窗口。

MES仍保持五态，不建设PAUSE/RESUME。

设备故障：

> 只形成事实、影响、建议，由PMC决定是否发起重排。

---

# 十八、Explanation验收

随机抽取延期订单，必须能回答：

> **为什么延期？**

可接受答案：

- Material X直到9月3日才可用；
- MC01在关键窗口没有产能；
- Firm Task占用了资源；
- 上游Domain完成晚。

不可接受：

> “DUE_DATE_RISK”。

这只是结果，不是根因。

---

# 十九、数据库与代码一致性验收

上线前必须核：

- DDL v5.1.2兼容升级脚本已在测试库验证；
- EF/DTO中的BIGINT使用long/long?；
- Task.PlannedProcessQty存在且语义正确；
- AllocationTaskShare真实多对多；
- FrozenZoneSnapshot不在正式主链；
- VirtualInventoryBalance不在正式主链；
- InventoryBalance.AllocatedQty不承担运行期真相；
- PSA没有事务外重复写；
- 一个PlanVersion只有一个Worker；
- 没有第二套旧Solver仍在正式调用。

---

# 二十、性能验收

## 20.1 FULL

目标：

> 约10万FinalTask / 90天 / 约15分钟。

如果未达到15分钟，但：

- 能稳定完成；
- 有真实Profile；
- 瓶颈明确；
- 优化路径明确；

可由0号位决定是否作为上线条件。

但禁止为了速度：

- 删掉有限产能；
- 截断90天；
- 回退第二套粗排；
- 静默忽略Demand。

## 20.2 Candidate

目标：

- Normal约60s；
- Soft 90s；
- Local Hard 180s后Fallback。

---

# 二十一、上线前P0阻断清单

以下任一项存在，建议直接判定：

> **不允许V1上线。**

- [ ] Solver仍是Stub
- [ ] Pegging仍先INSERT FinalTask
- [ ] Demand/Supply数量不闭合
- [ ] PI Position不闭合
- [ ] Supply重复消费
- [ ] Resource双占
- [ ] TaskShare不支持多对多
- [ ] Candidate能推动其它Domain ACTIVE
- [ ] MaxImpactedOrders截断影响传播
- [ ] Planning Placeholder被当正式承诺
- [ ] 上游Domain失败后依赖下游错误发布
- [ ] FAILED Run被原地改回RUNNING
- [ ] Candidate/UNLOCATED错误下MES
- [ ] 同PlanVersion多Worker重复执行
- [ ] PSA/Task等存在部分提交
- [ ] 同一次Run规则版本漂移
- [ ] 旧FiniteCapacitySolver仍和新Solver双路径运行

---

# 二十二、允许带入V1上线的已知限制

如果核心闭环通过，以下可以作为已知限制：

- 不做MultiDomain Candidate；
- 不做跨Domain共享资源借用/配额；
- 不做有限物流Task；
- 不做第二套远期ROUGH_CUT；
- 不做复杂OA审批；
- 不做通用规则DSL；
- 不做Scenario多目标仿真平台；
- 不做全局Setup最优；
- 不做年度详细有限产能；
- 部分采购ETA仍可能依赖默认LT；
- 部分PI位置可出现UNLOCATED但必须可保守排产。

这些不是“未完成Bug”，而是V1冻结边界。

---

# 二十三、0号位最终验收签字建议

建议最终只签四个结论：

### 1. 业务冻结一致性
- 通过 / 不通过

### 2. 技术闭环
- 通过 / 限制通过 / 不通过

### 3. 性能
- 达标 / 可接受 / 不达标

### 4. 上线结论
- 可上线
- 限制上线
- 不可上线

如果选择“限制上线”，必须列：

- 限制项；
- 风险；
- 临时措施；
- V1.1/V2处理计划。

---

# 二十四、最终交付包结构

项目最终建议归档：

## A. 业务冻结
- 最终业务基线
- Pegging专项
- 有限产能专项

## B. 技术冻结
- 六份权威技术文档
- SHA-256冻结清单

## C. 实施包
- 1号位
- 2号位增量包
- 3号位
- 4号位
- 5号位
- 三个关键接口冻结

## D. 测试与验收
- 1号位S01～S22
- 2号位T01～T22
- 3号位R01～R22
- 4号位U01～U22
- 5号位F01～F22
- 夜间FULL总闭环
- Candidate总闭环
- CTP总闭环
- MES总闭环
- 性能报告

---

# 二十五、0号位一句话验收标准

> **APS V1真正完成，不是“每个人的模块都写完了”，而是：同一套冻结业务能够从真实需求和供给进入Pegging，形成可追溯Allocation，经过唯一一套90天有限产能Solver产生FinalTask，在真实资源、物料、跨厂和版本约束下保持数量/时间/事务闭合，白天Candidate不破坏其它Domain，CTP能区分确定与估算承诺，正式计划可安全下发MES，并且任何失败都能被解释、隔离和恢复。**
