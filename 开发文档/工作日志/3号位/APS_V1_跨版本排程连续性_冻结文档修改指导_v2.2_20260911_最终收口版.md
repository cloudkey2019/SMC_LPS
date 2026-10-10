# APS V1 跨版本排程连续性——冻结文档修改指导

**版本：v2.2（最终收口版）**  
**日期：2026-09-11（北京时间）**  
**依据：**《APS V1 跨版本排程连续性最终裁决 v1.2（PI Position先行 / MES状态机 / 单PI单工序单活跃资源版）》  
**性质：正式冻结文档增量修改执行指导**

---

# 一、修改总原则

后续修改必须基于项目来源中的最新有效母版做增量升版。

不得：

- 从零重写现有冻结母版；
- 借本次跨版本连续性重新打开PI Position、Pegging、Supply、Domain等已冻结主模型；
- 把尚未最终确定的计划下发物理技术方案自行写死；
- 为实现跨版本连续性增加无必要的平台、表、版本体系。

所有文档必须统一采用本指导的“先当前生产真相、后Task连续性”顺序。

---

# 二、必须统一修改的核心主链

正式流程必须改成：

```text
ERP PI RemainingQty
+
全部MES事实
+
Routing / XC / Transit
        ↓
5号位
PI Position
+
NextOperationContext
        ↓
2号位
Pegging / Allocation
        ↓
当前LogicalProductionDemand
        ↓
2号位再检查上一ACTIVE Task / MES状态
        ↓
识别：
APS旧Task连续份额
外部MES执行份额
自由生产份额
        ↓
2号位组装DomainSolveRequest
        ↓
1号位有限产能Solver
        ↓
FinalTask
        ↓
2号位继承旧TaskNo或生成新TaskNo
```

禁止任何文档写成：

```text
先根据上一TaskNo确定今天要生产多少
```

---

# 三、MES四状态必须统一

受影响文档统一使用业务标准状态：

```text
DELETED
NOT_STARTED
STARTED_INCOMPLETE
COMPLETED
```

规则：

| 状态 | TaskNo执行身份 | 下一版本 |
|---|---|---|
| DELETED | 结束 | 若仍有净需求，生成新FinalTask/新TaskNo/新MES工单 |
| NOT_STARTED | 不继承 | 原工单原则上删除/作废，重新排程并新TaskNo/新MES工单 |
| STARTED_INCOMPLETE | 唯一必须继承 | 保持TaskNo + MESWorkOrderNo，识别连续份额 |
| COMPLETED | 结束 | 已完成事实进入PI Position；仍有新缺口则新Task |

注意：

> NOT_STARTED不继承MES执行身份，不等于自动解除Firm/Frozen。

---

# 四、TaskNo ↔ MESWorkOrderNo一对一

正式文档必须写明：

```text
一个TaskNo只对应一个MESWorkOrderNo
一个正式APS MESWorkOrderNo只对应一个TaskNo
```

因此：

- 旧TaskNo不得绑定新MES工单；
- 新MES工单必须使用新TaskNo；
- STARTED_INCOMPLETE继续使用原TaskNo + 原MESWorkOrderNo；
- DELETED / NOT_STARTED / COMPLETED后如需新工单，使用新TaskNo。

1号位只形成FinalTask；正式TaskNo由2号位持久化时治理。

---

# 五、当前净需求与旧Task连续份额

2号位必须在当前 `LogicalProductionDemand` 已经形成后，再匹配旧Task。

对同PI、同当前执行起点片段：

```text
当前净需求Q
旧STARTED_INCOMPLETE MES净剩余M
```

正常：

```text
C = min(Q, M)
R = Q - C
```

其中：

```text
C → 原TaskNo连续
R → 新自由生产需求
```

如果：

```text
M > Q
```

则：

- 业务连续份额最多认Q；
- 记录Data Issue；
- 不允许旧Task把当前净需求抬高。

---

# 六、人工/外部MES工单

## 6.1 无TaskNo但已有真实报工

必须进入5号位：

```text
MES事实
→ PI Position
→ NextOperationContext
```

不允许因为没有TaskNo就忽略真实制造进度。

## 6.2 人工未开工MES工单

V1：

- 不建立TaskNo连续性；
- 原则上删除/作废；
- 若未删除导致新工单冲突，依赖MES现有同PI数量校验拒绝；
- APS必须收到FAILED/FailureReason。

## 6.3 人工已开工未完成MES工单

不恢复旧TaskNo。

应作为：

> 外部MES执行事实（External MES Execution Fact）

在本次Run中避免同一份仍在执行的数量/能力被重复安排。

如当前技术阶段只能由MES创建校验最终拦截，文档必须标记为V1降级路径，不得描述成正常等价闭环。

---

# 七、LastReportResourceCode

`OperationProgressSnapshot`或等价MES标准化事实需要提供：

```text
LastReportTime
LastReportResourceCode
```

两者对应同一最后报工事实。

定义：

> LastReportResourceCode是最后一笔已完成报工使用的设备，不是当前实时开放执行设备。

对STARTED_INCOMPLETE Task的资源顺序：

```text
LastReportResource
→ 上一ACTIVE原Task资源
→ 其它合法Eligibility / 同资源组设备
```

如果LastReportResource为空：

```text
原Task资源
→ 其它合法资源
```

并记录当前实际资源未知Issue。

---

# 八、新增V1有限产能红线：同一PI同一工序不自动多设备并行

所有有限产能文档必须增加：

> **同一PI同一Operation在任一时点最多一个APS自动排定的活跃资源。**

禁止：

```text
同一PI / 同一Operation
同时分到MC01和MC02生产
```

允许：

- 同一资源拆批；
- 时间上非重叠的顺序资源切换；
- 前一设备报工结束后换同资源组其它合法设备；
- 不同Operation按既有Stage Overlap规则重叠。

如果现场必须多设备并行：

> MES人工拆分，APS下一FULL吸收事实。

该红线只限制APS自动计划；已经发生的人工MES并行事实必须照实纳入PI Position。

---

# 九、计划良率与物料Quantity-Time

正式口径必须完整继承现有冻结规则。

## 9.1 Task业务数量

```text
Task.Quantity / NetOutputQty
= 净合格业务数量
```

也是计划员主显示数量。

## 9.2 PlannedProcessQty

由2号位按：

- 净需求；
- 当前NextOperationContext；
- 尚未完成Stage；
- 版本化Planning Yield；

反算。

1号位仅按PlannedProcessQty占设备能力。

## 9.3 物料约束也考虑Planning Yield

现有冻结已经明确：

> 计划良率改变上游投入和材料需求。

因此2号位同时形成：

```text
Material Quantity + AvailableTime
```

具体物料数量按：

- 物料消耗点；
- 当前执行起点；
- 尚未完成Stage；
- 计划良率；
- Supply/Allocation；

反算。

不得简单写成：

```text
MaterialQty = Task.PlannedProcessQty
```

1号位不得自己重算。

## 9.4 已完成Stage

不得重复套Yield，也不得重复要求已经消耗完毕的上游生产步骤。

## 9.5 BOM ScrapRate

V1不因本次修改引入BOM ScrapRate额外损耗链。

---

# 十、Firm/Frozen

3号位治理：

```text
ParameterSetVersion
→ StrategyProfileVersion
→ FrozenStrategySnapshot.FenceBlock
```

2号位运行时按：

```text
HorizonStart
FrozenDays
FirmDays
PlannedStartTime
```

重新分类。

不新增：

```text
IsFirm
IsFrozen
FenceSnapshot第二平台
ExecutionLock平台
```

Firm重开继续使用：

```text
aps.reschedule.manual
+
确认
+
审计
```

---

# 十一、同TaskNo旧块处理

必须修正文档中容易产生歧义的“同TaskNo旧块剔除”。

准确写法：

> 同TaskNo上一版本旧计划块不得作为“其它Task”的ResourceBlock再次阻挡自己；但该Task自身仍有效的Execution/Frozen/Firm约束必须转为本Task自己的连续性/锚点约束，不能被删除。

由2号位在DomainSolveRequest组装前完成。

---

# 十二、计划下发中间表

业务要求固定，物理技术实现待2/5最终确认。

至少支持：

```text
TaskNo
MESWorkOrderNo
PENDING / CREATED / FAILED
FailureReason
SourcePlanVersionId
SourceTaskId
必要下发字段
```

关键生命周期：

```text
STARTED_INCOMPLETE
→ 复用旧TaskNo / MESWorkOrderNo
→ 不创建第二张工单

DELETED / NOT_STARTED / COMPLETED后新需求
→ 新TaskNo
→ 尝试创建新MESWorkOrderNo
```

---

# 十三、各冻结文档具体修改点

## 13.1 《APS V1 最终全部流程与业务基线》

新增/修订：

- PI Position先行、Task连续性后置；
- MES四状态；
- TaskNo ↔ MESWorkOrderNo一对一；
- STARTED_INCOMPLETE才继承TaskNo；
- 人工MES事实不依赖TaskNo；
- 同一PI同一Operation不自动多设备并行；
- Task净业务数量 / PlannedProcessQty；
- Yield对材料需求的影响；
- Firm/Frozen与MES状态边界。

## 13.2 《APS 有限产能排产与滚动90天计划业务说明》

新增/修订：

- 2号位提供连续份额与自由份额；
- LastReportResource资源优先；
- 单PI单Operation单活跃资源红线；
- 不允许1号位自动多设备并行；
- 允许顺序换机；
- PlannedProcessQty占能力；
- Material Quantity-Time由2号位计算；
- 1号位不重算Yield和材料需求。

## 13.3 《APS 核心排产全流程走查》

新增完整时序图：

```text
Facts
→ PI Position
→ LogicalProductionDemand
→ Continuation Bucketing
→ Solver
→ TaskNo Lifecycle
→ MES Dispatch
```

并加入：

- 人工MES工单；
- NOT_STARTED重建；
- STARTED_INCOMPLETE连续；
- 单活跃资源。

## 13.4 《APS Pegging供需承接与分层计算业务说明》

最小补充：

- TaskNo不参与本次Supply数量真相形成；
- TaskNo连续不冻结Demand↔PI Pegging；
- 当前净需求先于Task连续性；
- MES RemainingQty不得重复叠加LogicalDemand；
- Yield不放大Allocation和PI Supply。

## 13.5 《APS 集成接口设计》

修改：

- APS先有TaskNo，MES后生成MESWorkOrderNo；
- 一对一；
- 四状态；
- LastReportResourceCode；
- PENDING/CREATED/FAILED；
- NOT_STARTED重发新TaskNo/新工单；
- STARTED_INCOMPLETE不重新创建MES工单；
- 人工无TaskNo MES工单事实仍进入ODS。

## 13.6 《APS 数据架构与防腐层设计方案》

增加：

- MES事实链与Task身份链分离；
- 人工MES工单无TaskNo也可进入PI Position；
- LastReportResource聚合事实；
- 不复制完整报工历史；
- 外部MES执行事实边界。

## 13.7 《APS 数据库字段说明文档》

明确：

```text
Task.Quantity
= 净合格业务数量

Task.PlannedProcessQty
= Yield反算后的产能加工数量
```

新增/确认：

```text
LastReportResourceCode
```

MES工单状态增加/映射：

```text
DELETED
NOT_STARTED
STARTED_INCOMPLETE
COMPLETED
```

不新增：

```text
IsFirm
IsFrozen
ContinuationOutputQty
ContinuationProcessQty
```

## 13.8 《APS 数据库表结构设计 DDL》

预计最小变化：

- MES标准化快照增加 `LastReportResourceCode`；
- 必要时补DELETED状态映射支撑；
- 计划下发中间表待2/5最终技术方案确认后落DDL。

不因本轮增加：

- ExecutionLock；
- Fence专属版本表；
- IsFirm / IsFrozen。

## 13.9 1号位实施包

明确：

- 不解析MES；
- 不决定TaskNo；
- 连续Task禁止拆/合身份；
- LastReportResource优先排序；
- 同一PI同一Operation禁止不同资源时间重叠；
- 不自动并行拆设备；
- 仅按PlannedProcessQty占能力；
- 仅消费Material Quantity-Time。

## 13.10 2号位实施包

明确：

- 先LogicalProductionDemand，后连续性匹配；
- 三类分桶；
- STARTED_INCOMPLETE连续份额；
- M>Q数据异常；
- 新/旧TaskNo治理；
- PlannedProcessQty；
- Material Quantity-Time；
- Fence运行时判定；
- 同TaskNo自身约束转换；
- 下发中间表协同。

## 13.11 3号位实施包

继续：

- Planning Yield参数治理；
- FenceBlock；
- Firm重开权限/审计。

不参与运行时数量和TaskNo判断。

## 13.12 5号位实施包

增加/明确：

- MES四状态标准化；
- LastReportResourceCode；
- 无TaskNo人工MES报工也进入事实解释；
- PI Position先于Task连续性；
- MES RemainingQty净合格剩余语义；
- 不复制完整报工历史。

## 13.13 《APS V1 0号位总体项目验收包》

新增专项验收场景：

- DELETED；
- NOT_STARTED；
- STARTED_INCOMPLETE；
- COMPLETED；
- 人工无TaskNo报工；
- M<Q / M>Q；
- 实际设备与APS原设备不同；
- 单PI单Operation并行禁止；
- Yield产能/材料；
- 同TaskNo自阻挡；
- 未删除NOT_STARTED导致MES拒绝。

## 13.14 《APS V1 代码审核检查基线》

增加红线：

```text
R1 先用旧TaskNo推本次数量
R2 NOT_STARTED错误继承旧TaskNo
R3 STARTED_INCOMPLETE错误创建第二MES工单
R4 同TaskNo绑定多个MESWorkOrderNo
R5 LastReportResource被错误当硬锁
R6 同一PI同一Operation自动多资源并行
R7 人工MES真实报工因无TaskNo被丢弃
R8 RemainingQty与LogicalDemand重复相加
R9 1号位重算Yield
R10 MaterialQty被机械等于PlannedProcessQty
R11 已完成Stage重复套Yield
R12 同TaskNo旧块自阻挡
R13 NOT_STARTED重建错误解除Firm/Frozen
R14 PlannedProcessQty覆盖Task.Quantity
```

---

# 十四、回归测试必须至少覆盖

### T01 DELETED

旧TaskNo结束；有净需求则新TaskNo。

### T02 NOT_STARTED

不继承MES执行身份；新TaskNo/新MES工单；Firm/Frozen仍按Fence。

### T03 STARTED_INCOMPLETE

继承TaskNo/MES工单；按当前需求分连续份额。

### T04 COMPLETED

旧生命周期结束。

### T05 Q=100，M=40

40连续 + 60自由。

### T06 Q=50，M=60

最多50业务连续 + Data Issue。

### T07 实际设备被现场改过

LastReportResource优先于上一APS原设备。

### T08 LastReportResource为空

原APS资源优先 + ResourceUnknown Issue。

### T09 单PI单Operation两设备并行

Solver必须禁止自动生成重叠区间。

### T10 人工MES并行

不否认现实；进入PI Position/外部执行事实。

### T11 人工未开工工单未删除

MES拒绝新工单，APS收到FAILED原因。

### T12 Yield

Task.Quantity=100，PlannedProcessQty>100；计划员看100。

### T13 Material Yield

材料Quantity-Time按消费点/未完成Stage反算，不由1号位计算。

### T14 自阻挡

同TaskNo自己的约束不作为外部阻挡，但其有效Frozen/Execution仍保持。

---

# 十五、修订顺序

建议：

```text
1. 最终全部流程与业务基线
2. 有限产能排产与滚动90天计划业务说明
3. 核心排产全流程走查
4. Pegging供需承接与分层计算业务说明
5. 集成接口设计
6. 数据架构与防腐层设计方案
7. 数据库字段说明
8. DDL
9. 1号位实施包
10. 2号位实施包
11. 3号位实施包
12. 5号位实施包
13. 0号位总体项目验收包
14. 代码审核检查基线
15. 冻结基线索引/变更记录
```

计划下发中间表的具体DDL可以在2/5技术方案最终确认后补入；其它业务规则可以直接按本指导修改。

---

# 十六、给后续文档修订对话的强制指令

后续修订对话必须把以下规则作为同一组不可拆散的冻结口径：

> **每次FULL先由5号位形成PI Position和NextOperationContext，再由2号位形成当前LogicalProductionDemand；随后2号位才依据MES四状态决定旧TaskNo能否承接当前净需求。只有STARTED_INCOMPLETE继承旧TaskNo/MESWorkOrderNo；DELETED、NOT_STARTED、COMPLETED不继承旧执行身份。APS V1禁止同一PI同一Operation自动多设备并行；LastReportResource只作为顺序资源连续性首选。Task.Quantity始终是净业务数量，PlannedProcessQty和Material Quantity-Time由2号位依据未完成Stage与Planning Yield计算。**

不得再使用v1.1/v2.1中与本版冲突的旧表述。
