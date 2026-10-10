# APS V1 跨版本排程连续性、Firm/Frozen与MES执行事实衔接业务裁决建议稿

**版本：v1.0**  
**日期：2026-09-08**  
**定位：0号位架构收口建议稿，用于1/2/3/5号位共同评审、质疑与最终冻结**  
**状态：建议作为下一轮业务/技术确认基线，不直接覆盖现有冻结文档**

---

## 1. 本文要解决的问题

APS V1每天夜间进行新的全量排程（FULL），会形成新的PlanVersion和新的Task记录。

与此同时，制造现场又是连续运行的：

- Demand优先级每天可能变化；
- Demand ↔ PI的Pegging关系每天可能变化；
- 一个PI可以承接多个Demand；
- 一个Demand也可能被多个PI承接；
- 一个PI可以被1号位拆成多个Task；
- 多个逻辑生产需求也可以被1号位合并为Task；
- Task进入Firm/Frozen以后又要求近期计划稳定；
- Task下发MES以后，会出现提前、延后、换机、部分完成、超量、未执行、现场强制插单等真实偏差；
- 下一次FULL必须同时尊重最新Pegging、上一ACTIVE的固定计划和MES真实执行。

本文给出一个**不引入新的“执行承诺平台”或第四套身份体系**的最小闭环方案。

---

## 2. 最终总原则

本方案冻结建议为：

> **PI数量真相、Demand↔PI承接、Task排程连续性、MES执行事实四件事必须分层。**

四层分别回答：

1. **PI还有多少、在哪里、下一步从哪里继续？**  
   → ERP PI RemainingQty + 5号位PI Position / NextOperationContext。

2. **今天哪个Demand承接哪个PI、承接多少？**  
   → 2号位Pegging / Allocation。

3. **上一ACTIVE中哪些Task今天仍然必须保持？**  
   → 2号位根据Firm/Frozen规则和上一ACTIVE形成固定排程锚点。

4. **现实到底已经发生了什么？**  
   → MES执行事实；Execution优先于Frozen/Firm计划。

任何一层都不得反向替代另一层。

---

## 3. 正式主链顺序

```text
ERP PI RemainingQty
+
MES执行事实
+
Routing / Stage
+
XC / Transit等强位置事实
        ↓
5号位
PI Position
+
NextOperationContext
        ↓

2号位
最新Demand排序
+
最新Supply
+
Strict Binding
+
Demand Protection
+
Execution不可逆供需关系
        ↓
Demand ↔ PI Supply Pegging
+
Allocation
        ↓

2号位
消费PI Position / NextOperationContext
形成LogicalProductionDemand
        ↓

2号位
读取上一ACTIVE
识别仍有效Firm/Frozen Task
+
消费MES执行事实形成Execution约束
        ↓

DomainSolveRequest
=
LogicalProductionDemand
+
Allocation血缘
+
Execution约束
+
Firm/Frozen固定锚点
+
Routing / Resource / Calendar / Strategy
        ↓

1号位有限产能Solver
        ↓
FinalTask
+
AllocationTaskShare
+
TaskDependencies
+
Explanation / Unscheduled
        ↓
2号位校验并持久化新PlanVersion
```

**关键红线：**

> Firm/Frozen/Task执行事实不决定PI Supply到底有多少，也不得直接修改SupplyBalance。  
> PI数量边界仍以ERP PI RemainingQty为权威，5号位负责PI Position数量闭合。

---

## 4. 三种身份必须彻底区分

### 4.1 TaskId

> 某一个PlanVersion内Task数据库记录的技术主键。

- 每个PlanVersion可以重新生成；
- 不承担跨天稳定业务身份；
- 不允许靠“昨天TaskId = 今天TaskId”实现Firm/Frozen连续性。

### 4.2 TaskNo

建议正式冻结为：

> **APS Operation级生产Task的业务身份。**

TaskNo不是Demand身份，也不是PI身份，更不是PlanVersion身份。

TaskNo只在“仍然是一对一连续存在的同一个生产Task”时跨PlanVersion保持。

### 4.3 MESWorkOrderNo

> MES侧真实生产执行工单身份。

Task正式下发MES后，应形成：

```text
APS TaskNo
↔
MESWorkOrderNo
```

的权威映射。

以后执行事实应优先通过：

```text
TaskNo + MESWorkOrderNo
```

找到原计划Task，而不是用PI + Operation + Qty做模糊匹配。

---

## 5. TaskNo生命周期正式规则

| 场景 | TaskId | TaskNo处理 |
|---|---|---|
| 1号位首次产生普通FinalTask | 新 | 生成新TaskNo |
| 普通Movable Task第二天重新排程 | 新 | 不要求继承旧TaskNo；旧TaskNo生命周期结束 |
| Firm/Frozen Task一对一原样继承 | 新 | 继承原TaskNo |
| Firm经正式授权只调整资源/时间，但仍保持同PI、同Operation、同Qty的一对一Task | 新 | 可保持原TaskNo |
| Task拆批 | 新 | 原TaskNo结束，各子Task生成新TaskNo |
| 多Task合批 | 新 | 原TaskNo全部结束，合并Task生成新TaskNo |
| Task下发MES | 当前版本TaskId | TaskNo固定，并建立MESWorkOrderNo映射 |
| MES工单仍OPEN/执行中 | 新版本TaskId可以变化 | TaskNo保持 |
| MES部分完成但同一工单仍未完结 | 新版本TaskId可以变化 | TaskNo保持 |
| MES工单正式结束且数量不足 | 原Task结束 | 原TaskNo结束；剩余生产需求以后生成新TaskNo |
| MES全部完成 | 原Task结束 | TaskNo关闭 |
| Demand第二天重新Pegging | TaskId按版本 | 不因Demand归属变化而改变TaskNo |

### 5.1 TaskNo何时必须终止

一旦发生下列任一结构变化，旧TaskNo生命周期终止：

- 原Task被拆成多个Task；
- 多个Task合成一个新Task；
- 原MES工单已结束，剩余数量重新形成新的生产Task；
- 原Task被正式释放回Movable池并发生结构性重排；
- 原生产Task不再具有一对一连续性。

原则：

> **不允许一个旧TaskNo同时代表多个新Task，也不允许多个旧TaskNo强行合并后继续沿用其中一个TaskNo。**

---

## 6. Demand ↔ PI ↔ Task多对多如何与TaskNo共存

TaskNo只代表物理生产Task连续性，不代表Demand归属。

例如第一天：

```text
PI=P1001，1000件

Pegging：
A ← P1001 600
B ← P1001 400
```

1号位生成：

```text
TK001 400 Frozen
TK002 600 Movable
```

第一天AllocationTaskShare：

```text
TK001 → A 400

TK002
├ A 200
└ B 400
```

第二天重新Pegging：

```text
C ← P1001 500
A ← P1001 500
```

如果TK001仍然Frozen，则其TaskNo、PI、Operation、Qty、Resource、Start/End保持，但本版本AllocationTaskShare可以改成：

```text
TK001 → C 400
```

因此：

> **TaskNo稳定，不代表昨天的Demand归属稳定。**

真实Demand归属始终由当前PlanVersion的AllocationTaskShare表达。

---

## 7. Firm / Frozen参数及2号位运行时判定

### 7.1 职责

建议冻结：

- **3号位**：维护/治理FenceConfig中的FrozenDays、FirmDays及版本；
- **2号位**：每次ScheduleRun运行时读取冻结参数快照，计算上一ACTIVE Task当前属于Frozen、Firm还是Movable；
- **1号位**：不计算Fence，只消费2号位已经形成的Execution/Firm/Frozen约束。

### 7.2 推荐公式

建议V1采用连续、不重叠时间带：

```text
FenceBasisTime = 本次FULL排程基准时间

FrozenEnd = FenceBasisTime + FrozenDays
FirmEnd   = FrozenEnd + FirmDays
```

判定：

```text
Task.PlannedStartTime < FrozenEnd
→ Frozen

FrozenEnd <= Task.PlannedStartTime < FirmEnd
→ Firm

Task.PlannedStartTime >= FirmEnd
→ Movable
```

且前提为：

```text
Task.PlannedStartTime >= FenceBasisTime
```

例如：

```text
FULL基准时间：2026-09-08 02:00
FrozenDays = 3
FirmDays = 2

Frozen：
[09/08 02:00, 09/11 02:00)

Firm：
[09/11 02:00, 09/13 02:00)

Movable：
>= 09/13 02:00
```

### 7.3 一个Task跨Fence边界

只以PlannedStartTime判定。

Task开始时间落入Frozen，则整个Task按Frozen处理，不从中间切Task。

---

## 8. Firm和Frozen的区别

### Frozen

普通夜间FULL中：

- TaskNo继承；
- PI不变；
- Operation不变；
- Qty不变；
- Resource不变；
- Start/End不变；
- 不拆；
- 不合；
- 不自动移动。

Frozen只能被MES真实执行事实或明确异常恢复/正式业务操作覆盖。

### Firm

普通夜间FULL中同样作为固定锚点，不自动移动。

但业务允许通过人工重排、局部重排、Candidate、授权确认正式重新打开。

若Firm只调整时间/资源且仍保持一对一Task连续性，可保留TaskNo；若发生拆批/合批/结构变化，则旧TaskNo结束。

---

## 9. MES执行事实与计划Task如何关联

### 9.1 必须建立权威Task↔MES映射

建议增加或确认现有持久化事实：

**MES Task Dispatch Mapping（MES任务下发映射）**

至少包含：

```text
PlanVersionId
TaskId
TaskNo
ProductionInstructionNo
MESWorkOrderNo
DispatchTime
DispatchStatus
```

职责仅为：

> 证明“哪个APS Task正式下发成了哪张MES工单”。

它不参与Pegging、Supply排序、Solver、PI Position、Candidate。

### 9.2 MES标准执行事实建议至少补充

```text
TaskNo
MESWorkOrderNo
ProductionInstructionNo
OperationCode
ActualResourceCode
ActualStartTime
ActualEndTime
GoodQty
RemainingQty
ExecutionStatus
LastReportTime
```

5号位负责MES事实标准化及PI Position / NextOperationContext解释；2号位消费解释结果并形成Execution约束；1号位不直接读取原始MES。

---

## 10. Execution优先级

正式建议：

```text
Execution真实事实
>
Frozen
>
Firm
>
普通Movable计划
```

Execution覆盖Frozen，不表示改写昨天Frozen历史，而表示新PlanVersion不再机械继承已经被真实执行改变的旧Frozen计划。

---

## 11. MES执行偏差场景处理矩阵

假设上一ACTIVE：

```text
TaskNo=TK001
PI=P1001
Operation=NC
Qty=400
Resource=MC01
PlanStart=08:00
PlanEnd=10:00
Frozen
```

| 场景 | 新Run处理 |
|---|---|
| 尚未执行且计划时间仍在未来 | TK001完整Frozen继承 |
| 按计划完成400 | TK001执行生命周期结束；NC400退出未来排程 |
| 提前执行 | Actual时间覆盖Plan时间；后序按实际完成继续 |
| 延后执行 | 同上，以Actual事实为准 |
| 实际换机 | ActualResource成为Execution事实；原计划资源不再作为真实执行占用 |
| 完成150且MES工单仍OPEN | TaskNo继续；150为已完成事实，250仍属于当前执行对象 |
| 完成150但MES工单已正式结束 | 原TaskNo结束；若仍需250，由5号位解释后形成新逻辑生产需求并生成新TaskNo |
| 实际450 > 计划400 | 不得压回400；记录真实450并触发数量一致性异常 |
| 计划时间已过去但完全未执行 | 过去时间块不能继续占未来能力；进入计划偏差/异常重排 |
| 现场中断Frozen并强插急单 | APS承认MES现实；Execution覆盖原Frozen剩余计划，并产生计划偏差说明 |

---

## 12. Firm/Frozen固定锚点与LogicalProductionDemand数量闭合

例如：

```text
PI=P1001
本次需要生产1000
```

上一ACTIVE仍有效Frozen：

```text
TK001 / NC / 400
```

这400不是额外Supply，也不是额外生产需求。

对1号位的业务语义必须是：

```text
P1001当前需排的1000
=
已固定排程份额400
+
可自由排程份额600
```

禁止：

```text
LogicalProductionDemand 1000
+
Frozen Anchor 400
→ Solver再排1400
```

因此固定Anchor必须明确关联：

```text
TaskNo
ProductionInstructionNo
OperationCode
Quantity
Resource
Start/End
LockType
```

并参与当前LogicalProductionDemand对应Operation层的数量闭合。

注意：

> 这个闭合属于有限产能输入闭合，不是Pegging SupplyBalance扣减。

---

## 13. 多工序时不得按Frozen Task逐条从PI总量相减

例如同一400件：

```text
NC 400 Frozen
↓
挤丝 400 Frozen
↓
研磨 待排
```

不能计算：

```text
PI 1000 - NC400 - 挤丝400
```

因为NC400和挤丝400可能是同一批400在不同Operation上的固定Task。

Firm/Frozen锚点必须仍作为Routing/TaskDependencies中的Operation级固定节点处理。

PI总数量仍由ERP RemainingQty + PI Position / NextOperationContext闭合。

---

## 14. 2号位给1号位的正式输入补充建议

每个Anchor至少包含：

```text
AnchorType              // EXECUTION / FROZEN / FIRM
SourcePlanVersionId
SourceTaskId
TaskNo
ProductionInstructionNo
MaterialId
StageCode
OperationCode
Quantity
PlannedProcessQty
ResourceId
PlannedStartTime
PlannedEndTime
MESWorkOrderNo
ActualResourceId
ActualStartTime
ActualEndTime
CompletedQty
RemainingQty
ExecutionStatus
```

原则：

- Frozen/Firm锚点保留完整生产Task语义；
- Execution锚点保留现实执行语义；
- 不允许退化成只有Resource+Start/End的匿名黑块。

---

## 15. 1号位处理规则

1号位必须先建立Execution Anchors + Frozen Anchors + Firm Anchors作为硬约束，再对LogicalProductionDemand中的可移动部分做Routing展开、资源选择、正/倒排、拆批、合批、Setup、Stage Overlap等有限产能优化。

1号位不得：

- 自己重判FrozenDays/FirmDays；
- 自己解析MES；
- 修改Pegging SupplyBalance；
- 把固定Anchor重新拆合；
- 把Execution事实退回普通计划。

---

## 16. 2号位持久化新PlanVersion时

对仍然一对一继承的Firm/Frozen/Execution Task：

```text
TaskId = 新版本新Id
TaskNo = 继承原TaskNo
```

对重新生成的Movable Task：

```text
TaskId = 新Id
TaskNo = 新TaskNo
```

AllocationTaskShare全部按本PlanVersion当前Allocation重新形成。

因此可以出现：

```text
TaskNo相同
但昨天服务Demand A
今天服务Demand C
```

只要没有Strict Binding、Demand Protection或不可逆执行关系要求保持昨天Demand。

---

## 17. 不引入的新设计

本方案明确不新增：

- ExecutionCommitment平台；
- ProductionPortion永久份额身份；
- StableExecutionKey第四套身份；
- Operation级Pegging；
- FrozenZoneSnapshot平台；
- 每日旧Task与新Task按PI+Qty智能匹配算法；
- Demand与Task永久一对一绑定。

理由：

> 现有TaskNo + MESWorkOrderNo + PlanVersion + AllocationTaskShare足以完成V1闭环。

---

## 18. 对现有文档/DDL/代码的最小增量影响

### 18.1 业务文档需补充

建议后续对齐：

1. 《APS V1最终全部流程与业务基线》
2. 《APS 有限产能排产与滚动90天计划业务说明》
3. 《APS 核心排产全流程走查》
4. 《APS 集成接口设计》
5. 1号位实施包
6. 2号位实施包
7. 5号位实施包
8. 0号位验收包

### 18.2 DDL需确认/最小补充

当前Task已有TaskId、PlanVersionId、TaskNo、PI号、Operation、Resource、Qty、Start/End；AllocationTaskShare已支持Demand↔Task多对多。

建议确认或补充：

1. TaskNo跨PlanVersion业务语义及必要索引/唯一性策略；
2. MES Task Dispatch Mapping（TaskNo ↔ MESWorkOrderNo）；
3. MES执行事实中的ActualResource / ActualStart / ActualEnd / TaskNo等字段。

不建议新建复杂执行承诺实体。

---

## 19. 供各号位重点质疑的问题

### 1号位

- 现有ImmovableFacts能否表达完整Task Anchor？
- Execution部分完成、资源固定但未来结束时间变化时如何求解？
- Firm/Frozen Anchor是否能保证不拆、不合、不移动？
- 新旧TaskNo继承由1号位输出还是2号位最终生成最合理？

### 2号位

- 能否读取上一ACTIVE的完整Task？
- 当前TaskNo生成算法是否包含PlanVersionId？
- 如何实现TaskNo继承/终止规则？
- Fence公式实现是否有冲突？
- 如何保证固定Anchor与LogicalProductionDemand数量不重复？
- MES下发映射事实目前落在哪里？

### 3号位

- FenceConfig版本快照和作用域是否支持本方案？
- Firm人工授权/重开边界是否已有现成治理链？

### 5号位

- MES是否实际能提供TaskNo、MESWorkOrderNo、实际设备、实际开始/结束时间？
- 当前OperationProgressSnapshot是否会丢失这些执行细节？
- Task↔MES工单关系由现有接口是否能稳定提供？

---

## 20. 验收不变量

### 数量

```text
Σ PI PositionQty = ERP PI RemainingQty
```

且固定/执行Task不得导致重复生产。

### Pegging

普通Demand↔PI关系每次FULL可按最新优先级重新形成。

### Task

Firm/Frozen一对一连续时，TaskNo稳定跨PlanVersion；TaskId不要求稳定。

### 拆合

Movable可重新拆合；固定Task不得普通拆合。

### MES

```text
Execution > Frozen > Firm > Movable
```

### Demand归属

TaskNo稳定不代表AllocationTaskShare稳定。

### 执行追踪

正式下发MES的Task必须能通过TaskNo / MESWorkOrderNo精确回查，禁止靠PI+Operation+Qty猜测。

### 职责

- 5号位：执行事实标准化、PI Position、NextOperationContext；
- 2号位：Pegging、运行时Fence判定、Anchor组装、Solver编排和持久化；
- 1号位：有限产能资源/时间真相；
- 3号位：Fence参数治理。

---

## 21. 本方案最终判断

经过Demand↔PI多对多、PI↔Task一对多、Task拆合批、Firm/Frozen跨版本、MES提前/延后/换机/部分完成/现场强制插单等压力推演后，本方案当前判断为：

> **业务架构闭环成立。**

核心为：

```text
PI数量与位置
由ERP RemainingQty + PI Position解释

Demand归属
由每个PlanVersion最新Pegging / AllocationTaskShare解释

Task计划连续性
由TaskNo在一对一固定/执行生命周期内保持

版本数据库身份
由TaskId表达

MES现实身份
由TaskNo + MESWorkOrderNo衔接

Execution事实
始终高于Frozen/Firm计划
```

该方案与现有APS V1主架构兼容，且不要求建设新的复杂平台。

本文建议作为各号位下一轮共同质疑稿。各号位如提出冲突，应优先验证本方案的接口和字段闭合，不得借此重新打开已经冻结的PI Supply、Pegging、PI Position、NextOperationContext和1/2/5号位职责主边界。

---

## 22. 依据的当前冻结基线

本建议基于当前有效版本：

- 《APS V1最终全部流程与业务基线 v1.4》
- 《APS Pegging供需承接与分层计算业务说明 v1.3》
- 《APS 核心排产全流程走查 V3.20》
- 《APS V1 1号位有限产能排程开发实施包 v1.2》
- 《APS V1 2号位增量开发实施包》当前有效版
- 《APS V1 5号位复杂业务事实ODS与业务接口接入开发实施包 v1.3》
- 《APS 数据库表结构设计 DDL v5.1.5》

现有基线已经支持：

- PI Position不改变PI作为Supply的身份；
- 5号位负责PI Position / NextOperationContext；
- 2号位负责Demand↔PI数量承接与LogicalProductionDemand；
- FinalTask由1号位生成；
- AllocationTaskShare支持Demand↔FinalTask多对多；
- ImmovableFacts已包含Execution/Firm/Frozen；
- FinalTask输出已预留TaskNo Draft / StableKey及Firm/Frozen/Execution继承语义；
- FenceConfig已有FrozenDays / FirmDays参数。

本文新增收口内容：

1. TaskNo完整生命周期；
2. TaskNo ↔ MESWorkOrderNo权威执行映射；
3. Firm/Frozen运行时公式；
4. MES执行偏差处理矩阵；
5. 固定Anchor与当前LogicalProductionDemand数量闭合规则。
