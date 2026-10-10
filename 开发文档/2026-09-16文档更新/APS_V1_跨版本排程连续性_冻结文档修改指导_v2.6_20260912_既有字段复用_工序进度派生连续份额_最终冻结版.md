# APS V1 跨版本排程连续性——冻结文档修改指导

**版本：v2.6（既有字段复用 / 工序进度派生连续份额最终冻结版）**  
**日期：2026-09-11（北京时间）**  
**依据：**《APS V1 跨版本排程连续性最终裁决 v1.6（工单字段不扩展 / 工序进度派生连续份额最终冻结版）》  
**替代版本：v2.5及更早版本中与本版冲突的表述**  
**性质：正式冻结文档增量修改执行指导**

---

# 一、修改总原则

本次修改必须采用“增量修订”，不得借跨版本连续性重新设计已冻结主架构。

所有受影响文档必须统一采用以下主线：

```text
全部ERP/MES事实
→ 5号位生产指示位置（PI Position） / 下一工序上下文（NextOperationContext）
→ 2号位当前逻辑生产需求（LogicalProductionDemand）
→ 5号位输出标准化既存MES执行事实
→ 2号位在当前需求形成后做承接判断
→ 逐MES工单连续份额 + 自由份额
→ 1号位有限产能求解器（Solver）
→ 最终排程任务（FinalTask）
→ 2号位继承/生成TaskNo
→ 继续既有MES工单或创建新MES工单
```

禁止任何文档写成：

```text
先看旧TaskNo或MES工单还剩多少
→ 再决定今天应该生产多少
```

---

# 二、正式文档修订前的字段命名继承检查（强制）

后续修改数据库字段说明、DDL、集成接口、数据架构、防腐层及各号位实施包时，必须先对照上一有效版本的字段说明和DDL。

冻结规则：

> **业务含义不变，英文技术名不变。不得为了本轮文档“看起来更统一”而重新命名已有字段、状态值或接口字段。**

本轮已确认的继承项如下：

| 对象 | 中文含义 | 已有/继续使用的英文技术名 | 本轮处理 |
|---|---|---|---|
| `MES_APS_WorkOrder_View.WorkOrderStatus` | 待开工 | `RELEASED` | 保留，不改名 |
| `MES_APS_WorkOrder_View.WorkOrderStatus` | 开工中/已开工未完成 | `IN_PROGRESS` | 保留，不改名 |
| `MES_APS_WorkOrder_View.WorkOrderStatus` | 已完工 | `CLOSED` | 保留，不改名 |
| `MES_APS_WorkOrder_View.WorkOrderStatus` | 已删除 | `DELETED` | **本轮新增状态值** |
| `MES_APS_OperationProgress_View` | 工序级累计良品数量 | `GoodQty` | 保留 |
| `MES_APS_StageProgress_View` | 大工艺阶段级累计良品完成数量 | `GoodCompletedQty` | 保留 |
| APS本地工序/Stage进度快照 | 剩余数量 | `RemainingQty` | 保留；仅存在于进度快照层，不新增到工单级，不改为`RemainingNetQty` |
| MES工单状态字段 | 工单状态 | `WorkOrderStatus` | 保留，不新增同义`ExecutionStatus` |
| MES工单级 | 剩余净数量 | **不新增** | 禁止新增`RemainingNetQty`或工单级`RemainingQty` |
| MES工单级 | 最后报工设备 | **不新增** | 当前无可靠来源，禁止新增`LastReportResourceCode` |

特别强调：

1. 不再把 `RELEASED / IN_PROGRESS / CLOSED` 改写为 `RELEASED / IN_PROGRESS / CLOSED` 作为数据库/接口状态值；
2. 文档正文可写中文业务语义，例如“开工中/已开工未完成（IN_PROGRESS）”，但括号里的英文必须沿用既有值；
3. `GoodQty`与`GoodCompletedQty`虽然中文都涉及“良品数量”，但分别属于工序级和Stage级既有契约，**不得为了统一命名互相替换**；
4. 若确需新增新字段，必须先证明现有字段语义不能承载，并在字段说明、DDL、接口文档中同步说明；
5. 正式修改每一份母版前，应先做“旧字段名 → 新文档字段名”对照，发现同义改名时默认判定为错误，除非有明确裁决。

---

# 三、本版必须补严的业务重点

## 2.0 工单级字段不扩展（本版新增P0）

正式冻结：

```text
MES_APS_WorkOrder_View
MESWorkOrderSnapshot
```

不因为跨版本连续性新增：

```text
RemainingNetQty
RemainingQty
LastReportResourceCode
ExecutionStatus
```

其中：

- 工单状态继续用 `WorkOrderStatus`；
- 工序级良品数量继续用 `GoodQty`；
- Stage级良品完成数量继续用 `GoodCompletedQty`；
- `RemainingQty`继续只作为 `OperationProgressSnapshot` / `StageProgressSnapshot` 的APS本地计算列；
- 资源连续性在当前V1不依赖MES最后报工设备字段。

5号位给2号位的“既存执行上下文”必须被理解为**跨表组合结果/运行上下文**，而不是要求工单表新增这些字段。


## 2.1 MES事实解释仍归5号位

不得写成：

```text
2号位直接读取MES原始工单
→ 自己判断IN_PROGRESS / RemainingQty / 当前工序
```

统一改为：

```text
5号位
→ 标准化MES既存执行事实
→ 2号位消费
→ 与当前逻辑生产需求（LogicalProductionDemand）做承接
```

中文业务定义：

> **5号位负责把MES事实解释清楚；2号位负责决定这些既存执行事实能够承接当前需求中的多少。**

2号位不得建设第二套MES进度解释器。

## 2.2 既存执行剩余量合计（E）只用于总量闭合

多个MES工单可以合计得到既存执行剩余总量（E），但进入1号位时必须逐MES工单保持独立连续份额。

例如：

```text
WO101剩20，最后报工设备MC01
WO102剩30，最后报工设备MC03
E=50
```

求解输入必须保留：

```text
C101 → WO101 → Qty20
C102 → WO102 → Qty30
```

不得合并成一个`Qty50`连续对象。

## 2.3 无TaskNo既存MES工单的“绑定”只发生在APS侧

无TaskNo人工/历史MES工单承接有效需求时：

```text
2号位形成连续份额
→ 1号位生成最终排程任务（FinalTask）
→ 2号位生成新TaskNo
→ APS侧记录“TaskNo ↔ 原MES工单号”
```

除非后续MES接口另有明确能力和必要性，本轮不要求回写MES原工单的TaskNo字段，也不重新创建MES工单。

## 2.4 已有执行连续份额必须先于新增自由份额

对同一PI同一工序：

```text
已有执行连续份额
→ 新增自由份额
```

不仅禁止不同设备时间重叠，也禁止求解器为了优化把“尚未创建的新MES工单”排到“已经开工的既存MES工单”之前。

---

# 四、事实链与身份链必须分开写

所有文档需明确：

## 3.1 生产事实链

```text
MES真实报工
→ 生产指示位置（PI Position） / 下一工序上下文（NextOperationContext）
→ 当前逻辑生产需求（LogicalProductionDemand）
```

无TaskNo不影响事实进入。

## 3.2 Task身份链

只在当前净需求形成以后判断：

- 旧TaskNo是否继承；
- 无TaskNo外部MES工单是否在最终排程任务（FinalTask）后新建TaskNo接管；
- 自由份额是否需要新MES工单。

不得用TaskNo决定PI数量真相。

---

# 五、5号位标准化既存MES执行上下文

所有受影响文档必须明确：跨版本连续性使用的是5号位基于既有事实组合出来的运行上下文，而不是新增一张“工单连续性事实表”。

数据来源保持：

### 工单级

```text
MES_APS_WorkOrder_View / MESWorkOrderSnapshot
→ ProductionInstructionNo
→ MESWorkOrderNo
→ MaterialCode
→ PlannedQty
→ WorkOrderStatus
→ SourceUpdatedAt / DataCutoffTime
```

### 工序级

```text
MES_APS_OperationProgress_View
→ OperationName
→ StageCode
→ PlannedQty
→ GoodQty
→ LastReportTime
```

APS本地计算：

```text
OperationProgressSnapshot.RemainingQty
= max(PlannedQty - GoodQty, 0)
```

### Stage级

```text
MES_APS_StageProgress_View
→ StageCode
→ PlannedQty
→ GoodCompletedQty
→ LastReportTime
```

APS本地计算：

```text
StageProgressSnapshot.RemainingQty
= max(PlannedQty - GoodCompletedQty, 0)
```

5号位结合生产指示位置（PI Position）和下一工序上下文（NextOperationContext），把这些既有事实组合成可供2号位承接判断的上下文。

正式要求：`MES_APS_OperationProgress_View`与`MES_APS_StageProgress_View`保持现有字段契约，不因本轮业务新增字段。

禁止：

- 给工单表新增`RemainingNetQty`；
- 给工单表新增`LastReportResourceCode`；
- 为了跨版本连续性修改现有 `MES_APS_OperationProgress_View` 字段契约；
- 2号位绕过5号位直接重新解释MES原始报工。

---
# 六、MES工单状态值继承

所有相关正式文档必须继续使用既有字段：

```text
WorkOrderStatus
```

状态值统一按现有值表达：

| 中文业务含义 | WorkOrderStatus | 本轮处理 |
|---|---|---|
| 待开工 | `RELEASED` | 沿用 |
| 开工中 / 已开工未完成 | `IN_PROGRESS` | 沿用 |
| 已完工 | `CLOSED` | 沿用 |
| 已删除 | `DELETED` | 本轮新增 |

跨版本处理规则：

| 状态 | APS旧Task身份 | 当前FULL处理 |
|---|---|---|
| 待开工（`RELEASED`） | 不作为执行连续 | 原工单原则上删除/作废，当前需求重新排；新TaskNo/新MES工单 |
| 开工中/已开工未完成（`IN_PROGRESS`） | 存在现实执行 | 有有效需求份额时形成连续份额进入1号位有限产能求解 |
| 已完工（`CLOSED`） | 结束 | 已完工事实进入生产指示位置（PI Position）；其它缺口新排 |
| 已删除（`DELETED`） | 结束 | 有当前净需求则重新进入1号位有限产能求解，新TaskNo/新MES工单 |

补充：

> `IN_PROGRESS`并不意味着“无论是否有当前需求都创造Task”。当前Q=0或E>Q时按执行超量/需求不一致异常（Execution Over-Commit）处理。

禁止在正式字段说明、DDL或接口契约中把上述状态重新命名为：

```text
NOT_STARTED
STARTED_INCOMPLETE
COMPLETED
```

这些词如果历史讨论中出现，只能作为业务语义解释，不再作为正式技术值。

---

# 七、TaskNo、MES工单号与连续性关联键（ContinuationKey）

## 7.1 正式绑定

继续保持：

```text
一个TaskNo ↔ 一个MESWorkOrderNo
```

## 7.2 1号位不直接消费MES身份

1号位不得依赖MESWorkOrderNo判断生命周期。

2号位应通过现有可复用稳定关联键或最小新增：

```text
ContinuationKey
```

表达连续求解关联。

1号位只需把该Key原样回传最终排程任务（FinalTask）。

## 5.3 2号位内部映射

### APS旧Task连续

```text
ContinuationKey
→ OldTaskNo
→ MESWorkOrderNo
```

### 无TaskNo外部MES连续

```text
ContinuationKey
→ Existing MESWorkOrderNo
→ OldTaskNo = NULL
```

---

# 八、当前净需求与既存执行覆盖算法

设：

```text
Q = 当前逻辑生产需求（LogicalProductionDemand）净合格数量
E = 各个有效IN_PROGRESS MES工单经工序/Stage进度与PI Position解释后形成的连续份额数量合计
```

正常：

```text
E <= Q
```

则：

```text
ExistingContinuationQty = E
FreeQty = Q - E
```

所有文档必须强调：

> **某一MES工单对应的工序剩余数量不要求与当前Q完全相等。**

同时必须补充：

> **既存执行剩余量合计（E）只用于数量闭合和自由需求计算；进入1号位时必须按MES工单号逐工单形成独立连续份额，不得把多个MES工单合并成一个连续求解对象。**

每个独立连续份额均需有独立的连续性关联键（ContinuationKey）或现有等价稳定键，以保留：

- 原MES工单身份；
- 当前有效工序/Stage上下文；
- 由工序/Stage进度与PI Position派生出的连续份额数量；
- 1号位返回后2号位的TaskNo身份落盘关系。

当前V1不要求该连续份额携带MES最后报工设备。

例如：

```text
Q=100
已有执行=30
→ 30连续 + 70自由
```

而不是要求旧工单必须刚好100。

---

# 九、E > Q / Q = 0异常处理

## 7.1 E > Q

```text
NewQty = 0
执行超量（Execution Over-Commit） = E - Q
```

不得：

- 把Q提高成E；
- 自动修改MES工单数量；
- 继续生成新自由Task。

V1不新增多工单超量自动分摊算法。

## 7.2 Q = 0但MES仍IN_PROGRESS

不得创造正常需求（Demand）或为“补TaskNo”而创建正常Task。

仅：

- 保留事实；
- 登记执行超量/需求不一致；
- 有可靠实时资源事实时形成资源现实阻挡。

当前MES契约没有可靠的实际设备字段，因此不得为了Q=0异常场景猜测或补造实时硬锁资源。已有APS Task的上一计划资源最多只能作为求解偏好，不能冒充当前实际设备。

---

# 十、连续份额必须和自由份额一起进入1号位

这是本版新增最重要审核红线。

错误：

```text
Q=100
已有执行30
→ 只给1号位70
```

正确：

```text
连续30
+
自由70
→ 一起进入求解器（Solver）
```

因为连续30仍然会占：

- 设备能力；
- 未来时间；
- 同PI同Operation串行关系；
- 对其它任务的资源可用性。

因此审核必须同时检查：

```text
净业务数量闭合
+
未来资源能力闭合
```

---

# 十一、2号位传给1号位的数据语义

连续份额最小需要表达：

```text
ContinuationKey（或已有等价键）
PI身份
StartStage / StartOperation
NetOutputQty
PlannedProcessQty
NoSplitMerge = true（或等价语义）
PreferredResource（可选）
EligibleResources
Material Quantity-Time
```

这里的 `NetOutputQty` 是2号位基于5号位提供的工序/Stage进度与生产指示位置（PI Position）形成的**连续份额运行时数量**，不是MES工单级字段。

具体字段名由1/2号位结合现有契约最小落地，不要求为了文档概念机械新增全部同名字段。

## 11.1 APS旧Task连续

如果该开工中（IN_PROGRESS）MES工单已经存在APS TaskNo绑定，可以把上一ACTIVE Task的计划资源作为连续性偏好：

```text
上一ACTIVE Task计划资源（如仍合法）
→ 其它合法资源（Eligible Resource）
```

它只是求解偏好，不是当前实际设备硬锁。

## 11.2 无TaskNo人工/历史MES连续

当前MES契约没有可靠实际设备字段，也没有上一APS Task资源，因此：

```text
不设置虚构PreferredResource
→ 由1号位在全部合法资源中正常求解
```

## 11.3 1号位无需知道

1号位无需知道：

- 是否有旧TaskNo；
- MESWorkOrderNo；
- 是否人工工单；
- 是否首次上线历史工单；
- 连续份额数量具体来自哪张MES事实表。

1号位只处理“连续份额求解语义”。

---

# 十二、1号位返回以后2号位身份落盘

## 10.1 APS旧Task连续份额

```text
最终排程任务（FinalTask） + ContinuationKey
→ 2号位识别OldTaskNo
→ 新TaskId + 继承OldTaskNo
→ 继续原MESWorkOrderNo
```

## 10.2 无TaskNo外部MES连续份额

```text
最终排程任务（FinalTask） + ContinuationKey
→ 2号位识别Existing MESWorkOrderNo
→ 新TaskId + 新TaskNo
→ APS侧建立“新TaskNo ↔ 原MESWorkOrderNo”的映射
→ 不创建新MES工单
```

### APS侧映射说明

上述“绑定/映射”默认指APS侧关系记录。MES中的人工/历史工单本体可以继续保持原样。

不得把本裁决理解为：

```text
必须回写MES原工单TaskNo
```

也不得为了建立APS映射重新创建第二张MES工单。

## 10.3 自由生产份额

```text
最终排程任务（FinalTask）
→ 新TaskId + 新TaskNo
→ 正常MES下发
→ MES创建新MESWorkOrderNo
```

---

# 十三、首次APS上线

所有文档统一：

> APS首次上线时，历史IN_PROGRESS MES工单如果能够承接本次有效需求，按“无TaskNo外部MES连续份额”处理。

不另建上线迁移平台。

历史：

- CLOSED只进事实；
- DELETED不接管；
- RELEASED原则上删除/作废后重排；
- IN_PROGRESS进入连续求解并在最终排程任务（FinalTask）后补建APS TaskNo绑定。

---

# 十四、人工MES工单

## 12.1 已有真实报工

无TaskNo也必须进入5号位生产指示位置（PI Position） / 下一工序上下文（NextOperationContext）。

## 12.2 人工RELEASED

不建立跨版本执行连续性。

原则上删除/作废；未清理导致下发冲突时，由MES数量校验拒绝并返回FAILED + FailureReason。

## 12.3 人工IN_PROGRESS

如果承接当前有效需求：

```text
外部MES连续份额
→ 进入1号位
→ 最终排程任务（FinalTask）后由2号位新建TaskNo绑定原MES工单
```

如果无当前有效需求：

```text
不创造需求（Demand）
→ 执行超量（Execution Over-Commit）异常
```

---

# 十五、资源连续性：当前V1不新增最后报工设备字段

当前冻结的MES工单视图、工序进度视图及APS本地快照中，没有可靠的“实际报工设备”字段。

因此正式冻结：

> **V1不新增工单级最后报工设备字段（LastReportResourceCode），也不要求为了跨版本连续性修改现有工序进度视图。**

资源连续性按以下方式处理：

### 已有APS TaskNo绑定的开工中工单

```text
上一ACTIVE Task计划资源（如仍满足资源资格）
→ 其它OperationResourceEligibility / 同资源组合法资源
```

上一APS计划资源仅作为连续性偏好，不作为当前实际设备硬锁。

### 无TaskNo人工/历史开工中工单

```text
无可靠MES实际设备事实
→ 不设置虚构资源偏好
→ 由1号位在全部合法资源中正常求解
```

如果未来MES能够稳定提供“MES工单 + 具体工序 + 实际设备”事实，可以另行作为工序级资源连续性偏好接入；本轮不把它作为V1上线P0条件，也不得把工序设备错误提升为整张MES工单唯一设备。

---

# 十六、单PI单工序（Operation）单活跃资源红线

所有有限产能相关文档必须加入：

> **已有执行连续份额的先后优先级：对同一生产指示（PI）同一工序（Operation），既存执行连续份额必须先于本次新增自由份额。**

因此1号位既要保证“不并行”，也要保证“不反转”：

```text
正确：
既存执行连续份额
→ 新增自由份额

错误：
新增自由份额
→ 既存执行连续份额

错误：
既存执行连续份额 || 新增自由份额在另一设备并行
```

这条规则只约束APS自动求解；如果现场MES已经人工形成并行执行，APS接受真实事实，但不得再主动新增并行。



> **APS V1不得自动把同一PI同一Operation拆到两台及以上设备并行生产。**

允许：

- 顺序换机；
- 时间不重叠的资源切换；
- 不同Operation的既有工艺重叠；
- 已经由现场MES人工形成的并行事实。

特别强调：

> 连续份额和自由份额也不能被1号位排成不同设备的时间重叠。

---

# 十七、计划良率（Planning Yield）、Task.Quantity与PlannedProcessQty

继续保持已冻结口径：

```text
Task.Quantity / NetOutputQty
= 净合格业务数量

PlannedProcessQty
= 2号位根据当前未完成Stage和计划良率（Planning Yield）反算的有限产能加工数量
```

连续份额也必须按剩余工艺链计算PlannedProcessQty。

已经完成Stage不得重复套Yield。

PI Supply不得因计划良率（Planning Yield）被放大。

---

# 十八、物料数量-时间约束（Quantity-Time）

2号位继续负责形成：

```text
Material RequiredQty + AvailableTime
```

基于：

- 消耗点；
- 当前下一工序上下文（NextOperationContext）；
- 未完成Stage；
- 计划良率（Planning Yield）；
- 已选Supply/Allocation。

1号位只消费结果，不重算Yield，不替换Supply。

不得机械规定：

```text
物料数量（MaterialQty） = PlannedProcessQty
```

V1不新增BOM ScrapRate损耗链。

---

# 十九、固定区（Firm）/冻结区（Frozen）

继续采用运行时围栏分类，不新增永久IsFirm/IsFrozen字段。

IN_PROGRESS真实执行身份与普通Fence分类分开处理：

- 执行身份是否继续：按本版连续性规则；
- 是否允许移动时间/位置：按Execution/Frozen/Firm硬约束；
- 资源不因已开工自动硬锁，仍按合法资源Preference求解。

RELEASED / DELETED执行身份结束，不表示自动解除Frozen/Firm。

Firm重开继续复用：

```text
aps.reschedule.manual
+ 人工确认
+ 审计
```

---

# 二十、同TaskNo自阻挡

文档必须统一写为：

> 同TaskNo上一版本自己的旧计划块不得作为“其它Task外部ResourceBlock”再次阻挡自己；但该Task自身仍有效的Execution/Frozen/Firm/连续性约束必须保留并转换为本Task锚点约束。

不得简单写“删除同TaskNo旧块”。

---

# 二十一、MES下发与并发安全

APS主计算必须在求解器（Solver）前完成既存IN_PROGRESS识别。

MES下发阶段的同PI数量校验只作为最后安全门，用于处理：

- DataCutoff后现场又创建工单；
- RELEASED旧工单未清理；
- 其它下发竞态。

必须能够回传：

```text
PENDING
CREATED
FAILED
FailureReason
```

不能把“MES会拒绝重复创建”替代成APS不做执行连续性识别的理由。

---

# 二十二、各冻结文档具体修改点

## 20.1 《APS V1 最终全部流程与业务基线》

新增/修订：

- 四步主链；
- 生产事实连续性与Task身份连续性分离；
- 5号位标准化既存MES执行事实，2号位只消费不重新解释；
- IN_PROGRESS既存执行覆盖Q；
- 连续份额必须进入求解器（Solver）；
- 无TaskNo既存MES工单按逐工单连续份额进入求解器（Solver），最终排程任务（FinalTask）后由2号位在APS侧建立TaskNo映射；
- 首次上线复用同一机制；
- E>Q / Q=0异常；
- 同PI同Operation既存执行连续份额先于自由份额；
- 同一PI同一工序（Operation）单活跃资源；
- Task.Quantity / PlannedProcessQty / Material Quantity-Time边界。

## 20.2 《APS 有限产能排产与滚动90天计划业务说明》

必须明确：

- 求解器（Solver）同时接收连续份额与自由需求；
- 连续份额不可被普通拆合掉；
- ContinuationKey原样返回；
- 1号位不解析MESWorkOrderNo；
- LastReportResource是Preference；
- 同PI同Operation不同资源时间不得重叠；
- 已存在人工并行事实不等同于APS可自动创建并行；
- PlannedProcessQty占能力；
- Material Quantity-Time由2号位提供。

## 20.3 《APS 核心排产全流程走查》

新增完整流程图：

```text
Facts
→ 生产指示位置（PI Position）
→ 逻辑生产需求（LogicalProduction需求（Demand）） Q
→ Existing Execution Coverage E
→ Continuation / Free Bucketing
→ 求解器（Solver）
→ 最终排程任务（FinalTask）
→ TaskNo Identity Persistence
→ MES Dispatch / Existing WO Continue
```

加入首次上线、人工MES、E>Q、Q=0示例。

## 20.4 《APS 供需承接（Pegging）供需承接与分层计算业务说明》

补充：

- TaskNo/MES工单不能决定当前Q；
- 当前Q先于执行连续性；
- 既存MES执行只覆盖当前Q，不创建需求（Demand）；
- E>Q进入异常；
- Yield不放大Allocation/PI Supply。

## 20.5 《APS 集成接口设计》

明确：

- MES四状态；
- 资源连续性：已有APS Task使用上一ACTIVE Task计划资源作为偏好；无TaskNo人工/历史工单不虚构资源偏好；
- APS TaskNo ↔ MESWorkOrderNo一对一；
- IN_PROGRESS旧APS工单继续原绑定；
- 无TaskNo外部MES工单在最终排程任务（FinalTask）后新增TaskNo绑定原MES工单；
- 自由Task正常创建MES工单；
- RELEASED重发；
- PENDING/CREATED/FAILED；
- 人工无TaskNo报工仍进入ODS。

## 20.6 《APS 数据架构与防腐层设计方案》

明确：

- MES事实链独立于TaskNo；
- 无TaskNo人工/历史MES工单可进入生产指示位置（PI Position）；
- IN_PROGRESS可形成外部连续份额；
- LastReportResource是聚合事实；
- 不复制完整报工历史；
- 不建立首次上线独立迁移平台。

## 20.7 《APS 数据库字段说明文档》

确认：

```text
Task.Quantity = 净合格业务数量
Task.PlannedProcessQty = Yield反算产能数量
```

确认/新增必要语义：

```text
最后报工设备字段（LastReportResourceCode）
MES四状态
```

ContinuationKey如现有稳定键可复用则不要求新增数据库永久字段；它主要是1↔2求解过程关联语义。

不新增：

```text
IsFirm
IsFrozen
CurrentResourceSet
ExecutionLock实体
```

## 20.8 《APS 数据库表结构设计 DDL》

只做最小必要变化：

- MES标准化事实支撑四状态；
- 资源连续性：已有APS Task使用上一ACTIVE Task计划资源作为偏好；无TaskNo人工/历史工单不虚构资源偏好；
- APS-MES绑定/下发中间表如现有设计尚不足，再按2/5最终技术确认补充。

不得因为ContinuationKey概念机械增加长期持久化表，优先复用现有运行/求解关联键。

## 20.9 1号位实施包

必须新增/明确：

- 接收连续份额；
- 原样返回ContinuationKey；
- 不解析MESWorkOrderNo/TaskNo来源；
- 连续份额不可普通拆合身份；
- 与自由份额共同参与有限产能；
- 同PI同Operation禁止多资源时间重叠；
- LastReportResource优先但非硬锁；
- PlannedProcessQty占能力；
- Material Quantity-Time只消费不重算。

## 20.10 2号位实施包

必须新增/明确：

- Q形成后再识别IN_PROGRESS；
- E<=Q正常分桶；
- E>Q / Q=0异常；
- 连续份额与自由份额数量闭合；
- 连续份额仍进入求解器（Solver）；
- ContinuationKey内部映射；
- 最终排程任务（FinalTask）后TaskNo三分支落盘；
- 无TaskNo外部MES工单绑定原MESWorkOrderNo且不新建MES工单；
- 首次上线复用外部连续机制；
- Fence与自阻挡处理；
- 下发并发安全。

## 20.11 3号位实施包

继续负责：

- 计划良率（Planning Yield）参数治理；
- FenceBlock；
- Firm重开权限/审计。

不参与运行时Q/E分桶和TaskNo身份决策。

## 20.12 5号位实施包

必须明确：

- 四状态标准化；
- 资源连续性：已有APS Task使用上一ACTIVE Task计划资源作为偏好；无TaskNo人工/历史工单不虚构资源偏好；
- 不新增工单级RemainingNetQty；连续份额数量由工序/Stage进度与PI Position运行时派生；
- 无TaskNo真实报工仍进入生产指示位置（PI Position）；
- 不因TaskNo缺失丢掉MES事实；
- 不承担TaskNo接管/持久化职责。

## 20.13 《APS V1 0号位总体项目验收包》

新增验收：

- APS旧Task IN_PROGRESS；
- 无TaskNo外部IN_PROGRESS；
- 首次上线历史工单；
- Q=100/E=30；
- Q=50/E=70；
- Q=0/E>0；
- 连续份额是否进入求解器（Solver）；
- ContinuationKey是否原样返回；
- 外部MES工单最终排程任务（FinalTask）后补TaskNo但不新建MES工单；
- 同一PI同一工序（Operation）并行禁止；
- 当前MES没有可靠实际设备字段；已有APS Task连续时仅以上一ACTIVE Task资源作为偏好；
- RELEASED残留导致MES拒绝；
- Yield能力/材料。

## 20.14 《APS V1 代码审核检查基线》

新增P0/P1检查项：

```text
R1 旧Task/MES反向决定当前Q
R2 IN_PROGRESS只从Q扣掉但不进入求解器（Solver）
R3 连续份额与Q重复叠加
R4 无TaskNo外部MES工单绕过1号位直接创建正式Task
R5 1号位解析MESWorkOrderNo/TaskNo生命周期
R6 ContinuationKey未随最终排程任务（FinalTask）返回
R7 外部MES连续最终排程任务（FinalTask）后又创建第二张MES工单
R8 APS旧Task连续错误生成新TaskNo
R9 RELEASED错误继承旧执行身份
R10 E>Q错误抬高需求（Demand）
R11 Q=0仍从MES剩余量制造需求（Demand）
R12 把上一ACTIVE Task计划资源错误当成当前实际设备硬锁（Hard Lock）
R13 同PI同Operation自动多设备并行
R14 人工MES真实报工因无TaskNo被忽略
R15 首次上线另建不必要复杂迁移平台
R16 1号位重算计划良率（Planning Yield）/物料数量（MaterialQty）
R17 同TaskNo旧块自阻挡
R18 Task.Quantity被PlannedProcessQty覆盖
R19 2号位直接解析MES原始状态/报工，绕过5号位标准化事实
R20 把多个MES工单的既存执行剩余量E合并成一个连续求解对象
R21 将“APS侧TaskNo—MES工单映射”错误实现为强制回写MES原工单TaskNo
R22 同PI同Operation虽未并行，但新增自由份额被排到既存执行连续份额之前
R23 在MES_APS_WorkOrder_View或MESWorkOrderSnapshot新增RemainingNetQty/RemainingQty
R24 在工单级新增LastReportResourceCode，或把它列为V1必需字段
R25 为了跨版本连续性修改GoodQty / GoodCompletedQty等既有英文字段名
R26 把工序/Stage进度派生的连续份额数量误写成MES工单原生剩余数量
```

---

# 二十三、回归测试最低集合

### T01 APS旧Task已开工未完成

Q中对应份额进入连续求解器（Solver）；最终排程任务（FinalTask）后继承TaskNo/MESWorkOrderNo。

### T02 无TaskNo人工MES已开工未完成

进入连续求解器（Solver）；最终排程任务（FinalTask）后新TaskNo，并在APS侧建立与原MESWorkOrderNo的一对一映射；不新建MES工单。

### T03 首次上线历史IN_PROGRESS

与T02同一路径，不走独立迁移平台。

### T04 Q=100，E=30

30连续 + 70自由；两部分总量=100，且都进入求解器（Solver）。

### T05 Q=50，E=70

新自由量=0；执行超量（OverCommit）=20；不得把Q改成70。

### T06 Q=0，E=20

不产生正常需求（Demand）/Task；登记异常。

### T07 连续30 + 自由70

1号位不得只排70；必须把30未来产能一起考虑。

### T08 ContinuationKey

1号位不得解释其来源，只原样带回。

### T09 外部MES连续身份落盘

最终排程任务（FinalTask）后2号位新TaskNo，并在APS侧建立与原MES工单号的一对一映射；不创建第二工单。

### T10 APS旧Task连续身份落盘

继承原TaskNo，不新建TaskNo。

### T11 RELEASED

不作为执行连续；原工单原则上作废；当前需求重新排。

### T12 DELETED / CLOSED

旧执行身份结束。

### T13 LastReportResource

优先但非硬锁；可顺序换合法资源。

### T14 同一PI同一工序（Operation）多设备并行

APS自动生成必须被禁止。

### T15 人工既有并行

不否认MES现实，但求解器（Solver）不得新增第三份自动并行。

### T16 Yield

Task.Quantity为净业务量，PlannedProcessQty为产能量。

### T17 Material Yield

材料Quantity-Time由2号位按未完成Stage/消耗点计算。

### T18 同TaskNo自阻挡

自身锚点保留，但不作为外部块挡自己。

### T19 FULL后下发前出现新MES工单

MES数量校验拒绝并回失败（FAILED）及失败原因（FailureReason），主链不因此改为“下发时才识别工单”。

### T20 5号位与2号位职责边界

5号位输出标准化既存MES执行事实；2号位不得直接读取MES原始报工重新计算执行状态、当前工序或剩余净数量。

### T21 多个既存MES工单

WO101剩20、WO102剩30时，可以计算E=50；但必须形成两个独立连续份额和两个独立连续性关联键，不得合成一个Qty=50连续对象。

### T22 无TaskNo人工MES工单接管

最终排程任务（FinalTask）形成后，2号位只在APS侧建立“新TaskNo ↔ 原MES工单号”映射；不要求修改MES原工单TaskNo字段，也不创建第二张MES工单。

### T23 同PI同Operation执行先后

已有执行连续份额必须先于新增自由份额。即使两个时间段不重叠，也不得把自由份额排到已开工既存执行之前。

### T24 工单级不新增剩余数量

检查 `MES_APS_WorkOrder_View / MESWorkOrderSnapshot` 不存在新增 `RemainingNetQty` 或工单级 `RemainingQty`；连续份额数量由现有工序/Stage进度及PI Position派生。

### T25 工单级不新增最后报工设备

检查V1不要求 `LastReportResourceCode`；已有APS Task连续使用上一ACTIVE Task资源作为偏好，无TaskNo人工/历史连续份额在合法资源集合内正常求解。

### T26 既有良品字段不改名

工序级继续 `GoodQty`，Stage级继续 `GoodCompletedQty`；不得互换或另造同义字段。

---

# 二十四、文档修订顺序

建议顺序：

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
15. 冻结基线索引 / 变更记录
```

---

# 二十五、给后续修订对话的强制指令

后续文档修改不得拆散以下口径：

> **每次夜间全量排程（FULL）先由5号位使用既有MES工单状态、工序进度和Stage进度形成生产指示位置（PI Position）、下一工序上下文及既存执行上下文，再由2号位形成当前逻辑生产需求Q并逐MES工单识别开工中（IN_PROGRESS）连续份额。连续份额数量由 `OperationProgressSnapshot.RemainingQty`、`StageProgressSnapshot.RemainingQty`及PI Position运行时派生，不要求也不允许在 `MES_APS_WorkOrder_View / MESWorkOrderSnapshot`新增工单级`RemainingNetQty`。现有 `GoodQty / GoodCompletedQty / RemainingQty / WorkOrderStatus`字段名保持不变。连续份额与自由份额共同进入1号位有限产能求解。当前MES契约没有可靠实际报工设备字段，因此V1不新增工单级`LastReportResourceCode`；已有APS Task连续以上一ACTIVE Task计划资源为偏好，无TaskNo人工/历史工单由1号位在全部合法资源内正常求解。最终排程任务返回后由2号位治理TaskNo与MES绑定。**

凡与本口径冲突的v2.3/v1.3及更早旧表述，以v1.6/v2.6为准。v1.6/v2.6完成本轮职责、数量、身份、产能、首次上线和人工MES干预闭环复核，可作为后续冻结母版增量修订依据。
