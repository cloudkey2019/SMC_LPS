# APS V1 跨版本排程连续性最终裁决

**版本：v1.6（工单字段不扩展 / 工序进度派生连续份额最终冻结版）**  
**日期：2026-09-12（北京时间）**  
**性质：0号位最终业务裁决**  
**替代版本：v1.5及更早版本中与本版冲突的表述**  
**适用范围：APS V1 夜间全量排程（FULL）的跨版本连续性、MES既存执行承接、TaskNo生命周期、首次上线历史工单、人工MES工单、固定区（Firm）/冻结区（Frozen）、计划良率、物料数量-时间约束（Quantity-Time）、有限产能资源连续性。**

---

# 一、本版修订目的

本版对v1.5做字段层面的最终更正。更正原则不是改变跨版本连续性业务主链，而是撤销两个不必要、且会破坏既有MES契约的“工单级字段假设”：

1. **不在 `MES_APS_WorkOrder_View` / `MESWorkOrderSnapshot` 增加工单级剩余净数量字段。** 工单级继续保持既有字段；连续份额数量从工序级进度、Stage级进度及生产指示位置（PI Position）运行时推导。
2. **不在工单级增加最后报工设备字段（LastReportResourceCode）。** 当前冻结数据契约没有可靠的工序实际报工设备来源，因此V1不把该字段作为P0输入；已有APS Task连续时以上一ACTIVE Task计划资源作为资源连续性首选，无TaskNo人工/历史工单则由1号位在合法设备集合内正常求解。
3. **5号位提供给2号位的是“组合后的既存执行上下文”，不是要求一张工单表必须新增所有字段。**
4. **既有字段名继续严格继承。** 工单状态使用 `WorkOrderStatus`；工序级良品数量使用 `GoodQty`；Stage级良品完成数量使用 `GoodCompletedQty`；工序/Stage剩余数量继续使用APS本地快照计算列 `RemainingQty`。

本版不改变以下已确认主线：先生产指示位置（PI Position）和当前需求，再识别开工中（IN_PROGRESS）既存执行连续份额；连续份额与自由份额共同进入1号位；最终排程任务（FinalTask）返回后由2号位治理TaskNo和MES绑定。

本版新增并冻结以下核心结论：

1. **每次FULL仍然先建立本次生产数量真相，再处理跨版本执行连续性。** 旧TaskNo、历史MES工单均不得反向决定本次应生产总量。
2. **所有真实MES报工先进入5号位事实链。** 无TaskNo不影响其进入生产指示位置（PI Position）和下一工序上下文（NextOperationContext）。
3. **5号位负责把MES工单状态、工序进度、Stage进度和生产指示位置组合成标准化既存执行上下文；2号位只消费该上下文，不重新读取或解释MES原始事实。该上下文是运行时组合结果，不要求在工单级新增剩余数量或最后报工设备字段。**
4. **2号位在当前逻辑生产需求（LogicalProductionDemand）已经形成以后，才把5号位提供的“开工中/已开工未完成（IN_PROGRESS）”既存执行事实与当前需求做承接。** 数量无需完全相等。
5. **既存执行剩余量合计（E）只用于总量闭合；真正进入1号位时，每个MES工单必须保持独立连续份额，不能因为合计E而把多个MES工单合成一个求解对象。**
6. **既存执行份额不能只从当前需求中扣除后从有限产能世界消失。** 其未来未完成部分仍会占资源，必须和新增自由需求一起进入1号位有限产能求解。
7. **无TaskNo的人工/历史已开工未完成MES工单，也必须以“连续份额”形式进入1号位。** 但1号位无需理解MES工单号（MESWorkOrderNo）、TaskNo来源或MES生命周期，只需消费不透明连续性关联键和排程数据。
8. **1号位仍然负责生成有限产能排程结果（最终排程任务（FinalTask））；2号位不在求解器（Solver）前直接创建正式Task。**
9. **1号位返回最终排程任务（FinalTask）以后，由2号位统一决定正式TaskNo身份：**
   - 原APS Task连续份额：继承原TaskNo；
   - 无TaskNo既存MES连续份额：生成新TaskNo，并仅在APS侧建立“新TaskNo ↔ 原MES工单号”的映射，不要求回写MES原工单，也不创建新MES工单；
   - 自由生产份额：生成新TaskNo，并正常创建新MES工单。
10. **首次APS上线不再单独建设“历史工单迁移平台”。** 已有历史“开工中/已开工未完成（IN_PROGRESS）”工单直接复用无TaskNo既存MES连续份额机制。
11. **同一生产指示（PI）同一工序中，已有执行连续份额必须先于本次新增自由份额排程。** 不能只保证两者不并行，却允许新增自由份额被排到既存执行之前。
12. **APS V1禁止1号位自动将同一PI、同一工序（Operation）拆到多台设备并行生产。** 已经由现场人工形成的MES并行事实不否认，但APS不得进一步自动制造新的并行。
13. **当既存MES执行量超过当前有效需求，或者当前需求为0但MES仍在执行时，不得反向创造需求（Demand）。** 进入执行超量/需求不一致异常（Execution Over-Commit）。
14. **本版坚持中文业务语义优先。** 英文术语、字段名、接口名只作为括注或代码标识使用，不作为正文主要表达。

除上述内容外，本版不重新打开已冻结的生产指示位置（PI Position）、供需承接（Pegging）、供给（Supply）、排程域（Domain）、工艺路线（Routing）、候选方案（Candidate）、计划良率（Planning Yield）及0～5号位职责边界。

---

# 二、最终总原则：四步单向链

跨版本排程连续性最终冻结为以下四个阶段，顺序不可颠倒：

```text
第一步：事实重建
ERP PI RemainingQty + 全部MES事实 + Routing + XC/Transit等
        ↓
5号位
生产指示位置（PI Position） + 下一工序上下文（NextOperationContext）

第二步：当前需求形成
        ↓
2号位
供需承接（Pegging）/数量分配（Allocation）
        ↓
当前逻辑生产需求（LogicalProductionDemand）（净合格数量Q）

第三步：执行连续性分桶
        ↓
5号位提供标准化既存MES执行事实
        ↓
2号位在当前需求形成后做承接判断
        ↓
逐MES工单的已有执行连续份额 + 自由生产份额

第四步：有限产能与身份落盘
        ↓
1号位有限产能求解 → 最终排程任务（FinalTask）
        ↓
2号位持久化并决定继承/生成TaskNo
        ↓
MES既有工单继续 or 新建MES工单
```

正式原则：

> **先决定“今天还需要生产什么、多少、从哪里开始”，再决定“这些数量中哪些已经有现实执行身份、哪些需要新建执行身份”。**

不得写成：

```text
先保护上一版本Task或MES工单
→ 再推今天应生产多少
```

---

# 三、生产事实连续性与Task身份连续性必须分开

APS V1必须明确区分两条链。

## 3.1 生产事实连续性

```text
MES真实执行事实
→ 5号位生产指示位置（PI Position） / 下一工序上下文（NextOperationContext）
→ 2号位当前逻辑生产需求（LogicalProductionDemand）
```

这条链与TaskNo是否存在无关。

人工工单、历史工单、APS工单，只要产生真实报工，就必须进入事实链。

## 3.2 Task身份连续性

TaskNo只用于说明：

> 当前PlanVersion中的某个最终排程任务（FinalTask），是否继续承载一个既有APS执行身份。

TaskNo不能决定：

- PI RemainingQty；
- 生产指示位置（PI Position）；
- 当前逻辑生产需求（LogicalProductionDemand）；
- 当前应新增多少生产数量。

因此允许出现：

```text
生产事实连续 = 是
旧TaskNo连续 = 否
```

例如：APS原工单被人工删除后，现场另建MES工单继续生产。

---

# 四、TaskId、TaskNo、MESWorkOrderNo和连续性关联键

## 4.1 TaskId

```text
TaskId
= 当前PlanVersion内数据库记录身份
```

新PlanVersion重新生成TaskId。

## 4.2 TaskNo

```text
TaskNo
= APS正式生产Task在可连续执行生命周期内的业务身份
```

TaskNo由2号位在最终排程任务（FinalTask）持久化阶段治理。

## 4.3 MESWorkOrderNo

```text
MESWorkOrderNo
= MES真实执行工单身份
```

正式APS下发关系保持：

```text
一个TaskNo ↔ 一个MESWorkOrderNo
```

## 4.4 连续性关联键（ContinuationKey）

为了让1号位既能识别“这是既存执行连续份额”，又不耦合MES业务，2号位必须为每一个需要连续求解的执行份额提供一个**不透明关联键**。

逻辑名称可称：

```text
ContinuationKey
```

若现有1↔2契约已有可复用的临时键/稳定关联键，可复用，不强制新增同名物理字段。

1号位只需遵守：

> 收到哪个连续性关联键，生成的对应最终排程任务（FinalTask）必须原样带回该关联键。

1号位不需要知道：

- 该Key背后是否已有TaskNo；
- 对应哪个MESWorkOrderNo；
- 是APS创建工单还是人工/历史工单；
- 为什么它需要连续。

---

# 五、正式Task由谁生成：职责最终澄清

## 5.1 1号位职责

1号位负责：

> 根据2号位提交的连续份额、自由需求、资源资格、硬约束、PlannedProcessQty、Material Quantity-Time等，生成有限产能排程结果（最终排程任务（FinalTask））。

1号位负责计算：

- Resource；
- PlannedStartTime；
- PlannedEndTime；
- 必要拆批/顺序关系（受本版单活跃资源红线限制）；
- Unscheduled；
- 其它已冻结Solver输出。

## 5.2 2号位职责

2号位负责：

1. 当前逻辑生产需求（LogicalProductionDemand）形成；
2. IN_PROGRESS既存MES执行份额识别；
3. 连续份额/自由份额拆分；
4. 向1号位组装求解输入；
5. 最终排程任务（FinalTask）持久化；
6. 决定正式TaskNo继承或新建；
7. TaskNo ↔ MESWorkOrderNo绑定与MES下发治理。

因此：

> **2号位在Solver前不直接制造一个跳过1号位排程的正式Task。**

---

# 六、MES工单四状态与正常跨版本生命周期

MES工单状态字段继续沿用既有字段：

```text
WorkOrderStatus
```

状态值继续沿用既有英文值，仅增加“已删除”：

```text
RELEASED      待开工
IN_PROGRESS   开工中 / 已开工未完成
CLOSED        已完工
DELETED       已删除
```

本裁决不把既有状态改名为新的英文状态。

## 6.1 已删除（DELETED）

如果上一APS关系为：

```text
T001 ↔ WO001
WO001.WorkOrderStatus = DELETED
```

则旧执行生命周期结束。

若本次仍有当前净需求：

```text
1号位形成新最终排程任务（FinalTask）
→ 2号位生成新TaskNo
→ MES创建新MES工单号（MESWorkOrderNo）
```

旧T001不得重新绑定新MES工单。

## 6.2 待开工（RELEASED）

待开工MES工单不构成跨版本执行事实连续性。

下一次全量排程（FULL）：

```text
旧TaskNo执行身份不继承
→ 原待开工MES工单原则上删除/作废
→ 当前需求重新进入1号位有限产能求解
→ 新最终排程任务（FinalTask）
→ 2号位新TaskNo
→ 新MESWorkOrderNo
```

若旧待开工工单没有及时删除：

> MES现有同PI数量校验作为最终安全门，允许拒绝新工单创建；APS必须接收失败状态（FAILED）与失败原因（FailureReason）。

## 6.3 开工中 / 已开工未完成（IN_PROGRESS）

`IN_PROGRESS`说明存在真实执行。

分两类处理：

### A. 已有APS TaskNo绑定

```text
T001 ↔ WO001
WO001.WorkOrderStatus = IN_PROGRESS
```

若本次当前净需求仍有对应有效份额：

```text
该份额进入连续求解
→ 1号位返回最终排程任务（FinalTask）
→ 2号位持久化时继承T001
→ 继续使用WO001
```

### B. 无TaskNo的人工/历史MES工单

```text
WO101
TaskNo = NULL
WorkOrderStatus = IN_PROGRESS
```

若本次当前净需求仍有对应有效份额：

```text
2号位识别该份额
→ 分配连续性关联键（ContinuationKey）
→ 作为既存MES连续份额交给1号位
→ 1号位生成最终排程任务（FinalTask）并返回ContinuationKey
→ 2号位持久化时生成新TaskNo
→ APS侧建立“新TaskNo ↔ 既有WO101”的映射
→ 不创建新的MES工单
```

因此“接管”发生在最终排程任务（FinalTask）产生以后，不是2号位跳过1号位直接创建排程Task。

## 6.4 已完工（CLOSED）

已完工MES工单的执行生命周期结束。

其报工事实继续进入5号位生产指示位置（PI Position）。

若本次仍有其它合法净缺口：

```text
新最终排程任务（FinalTask）
→ 新TaskNo
→ 新MESWorkOrderNo
```

---

# 七、5号位到2号位的既存MES执行上下文

本版进一步冻结职责边界：

> **5号位负责“MES事实解释与组合”；2号位负责“当前需求与既存执行如何承接”。**

这里的“既存MES执行上下文”不是一张新的ODS表，也不是要求 `MES_APS_WorkOrder_View` / `MESWorkOrderSnapshot` 增加所有连续性字段。

## 7.1 工单级继续保持现有字段

`MES_APS_WorkOrder_View` / `MESWorkOrderSnapshot`继续使用既有工单级事实，主要包括：

```text
生产指示号（ProductionInstructionNo）
MES工单号（MESWorkOrderNo）
物料编码（MaterialCode）
工单计划数量（PlannedQty）
工单状态（WorkOrderStatus）
源更新时间 / 数据截止时间
```

工单状态继续使用：

```text
RELEASED      待开工
IN_PROGRESS   开工中 / 已开工未完成
CLOSED        已完工
DELETED       已删除（本轮新增状态）
```

明确禁止为了跨版本连续性在工单级新增：

```text
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

其中 `ExecutionStatus` 如历史说明中仅用于中文业务归类，可保留为解释性概念；不得替代正式字段 `WorkOrderStatus`。

## 7.2 连续份额数量来自工序/Stage进度与PI Position，不来自工单级剩余字段

工序级现有事实继续使用：

```text
MES_APS_OperationProgress_View
→ PlannedQty
→ GoodQty
→ LastReportTime
```

APS本地：

```text
OperationProgressSnapshot.RemainingQty
= max(PlannedQty - GoodQty, 0)
```

大工艺阶段级现有事实继续使用：

```text
MES_APS_StageProgress_View
→ PlannedQty
→ GoodCompletedQty
→ LastReportTime
```

APS本地：

```text
StageProgressSnapshot.RemainingQty
= max(PlannedQty - GoodCompletedQty, 0)
```

因此：

> **连续份额所需的“还剩多少”是运行时派生语义，不是MES工单表的物理字段。**

5号位结合：

- 工单状态（`WorkOrderStatus`）；
- 工序级 `GoodQty / RemainingQty`；
- Stage级 `GoodCompletedQty / RemainingQty`；
- 生产指示位置（PI Position）；
- 下一工序上下文（NextOperationContext）；
- 统一数据截止时间（DataCutoffTime）；

形成可供2号位承接判断的既存执行上下文。

## 7.3 现阶段不要求最后报工设备字段

当前冻结的MES工单视图和工序进度视图中没有可靠的实际报工设备字段，因此：

> **V1不要求新增工单级 `LastReportResourceCode`，也不因为跨版本连续性强行修改现有工序进度视图。**

如果未来MES能够稳定提供“某工序最后实际报工设备”，应优先作为**工序级执行事实**接入，并另行确认既有字段名和契约；不得把它错误提升为整张MES工单唯一设备。

2号位不得因为当前缺少实际设备事实，就自行从最后报工时间、工单状态或其它字段猜测实际设备。

---
# 八、2号位如何计算既存执行份额与自由份额

设：

```text
Q = 当前逻辑生产需求（LogicalProductionDemand）的净合格数量
```

对于每个工单状态为开工中（`IN_PROGRESS`）的MES工单，2号位消费5号位已经组合好的工序/Stage执行上下文，识别该工单在**当前有效执行起点**上可承接的连续份额。

单个MES工单的候选连续数量，不从工单级字段读取，而应以：

```text
当前有效工序的 OperationProgressSnapshot.RemainingQty
+
生产指示位置（PI Position）/下一工序上下文校验
```

为主要执行数量依据，再受到当前需求Q的上限约束。

如果当前只能在Stage级解释位置，则以现有PI Position切片和Stage级事实完成数量闭合，不新增一条工单级剩余数量真相。

设各个有效MES工单最终识别出的连续份额为：

```text
C1, C2, ... Cn
```

则：

```text
E = C1 + C2 + ... + Cn
自由生产份额 = max(Q - E, 0)
```

其中 `E` 只是总量校验值，不是数据库字段，也不是一个可以合并执行身份的求解对象。

例如：

```text
当前需求 Q = 100

WO001 当前有效工序剩余可承接30
WO101 当前有效工序剩余可承接20

E = 50
自由生产份额 = 50
```

进入1号位时仍必须保持：

```text
连续份额C001 → WO001 → 净数量30
连续份额C002 → WO101 → 净数量20
自由生产份额 → 净数量50
```

不得把WO001与WO101合成一个“净数量50”的连续对象。

正式冻结：

> **总量可以合计，执行身份不可合并；连续份额数量是基于工序/Stage进度和PI Position形成的运行时结果，不要求工单表存在RemainingNetQty或RemainingQty。**

## 8.1 数量不要求完全相等

APS不要求：

```text
某一MES工单的工序剩余数量 == 当前逻辑生产需求（LogicalProductionDemand）
```

既存执行工单只负责覆盖当前需求中的一部分。

## 8.2 既存执行份额不能重复叠加

必须保证：

```text
当前Q
= 连续份额总量
+ 自由份额总量
```

禁止：

```text
当前Q
+ 连续份额
```

否则会把同一份生产数量重复送入1号位有限产能求解。

---
# 九、E > Q或Q = 0时的执行超量异常

## 9.1 E > Q

例如：

```text
当前Q = 50
IN_PROGRESS MES剩余量合计E = 70
```

不得：

- 把当前Demand从50提高到70；
- APS擅自修改MES工单把70压成50；
- 再产生新的自由生产Task。

正确处理：

```text
NewQty = 0
执行超量（Execution Over-Commit） = 20
```

当前50是业务需求上限；多出的20属于现场执行/数量不一致异常。

由于此时存在多个已经真实执行的对象，V1不新增复杂自动分摊算法去决定“哪个MES工单削减多少”。由现场/业务处理。

## 9.2 当前需求为0但MES仍已开工未完成

不得因为MES还有剩余量而创造新的APS Demand。

处理：

```text
正常连续业务份额 = 0
→ 记录执行超量（Execution Over-Commit） / Demand Mismatch
```

如果已有APS TaskNo ↔ MESWorkOrderNo绑定，历史绑定关系仍保留用于追溯，但不得因此在当前PlanVersion创造正常需求。

如果是无TaskNo外部MES工单，也不因为“想补TaskNo”而凭空创建正常Task。

## 9.3 资源占用的V1边界

如果未来MES能够提供可靠的“当前实际正在占用资源”，即使Q=0，也可以作为现实资源阻挡块。

但当前冻结的MES工单和工序进度契约中没有可靠的实际资源字段，因此V1：

- 不凭空新增工单级最后报工设备；
- 不从最后报工时间或工单状态猜测设备；
- 不把上一APS计划设备当成“当前实际设备硬锁”。

对于已有APS Task绑定的开工中工单，上一ACTIVE Task资源可以作为**求解偏好**；对于无TaskNo人工/历史工单，没有可靠实际设备事实时不设置资源连续性偏好，由1号位在合法设备集合内正常求解。

这是明确的数据能力边界，不为此新增实时MES执行平台。

---

# 十、为什么连续份额必须进入1号位有限产能求解

这是本版P0红线。

假设：

```text
Q = 100
既存执行 = 30
自由需求 = 70
```

错误做法：

```text
2号位把30从Q中扣掉
→ 只把70交给1号位
```

这会漏掉未来30仍然占用的：

- 设备能力；
- 加工时间；
- 同PI同Operation顺序关系；
- 对其它Task的资源影响。

正确做法：

```text
连续30
+
自由70
↓
一起进入1号位有限产能世界
```

区别仅在身份属性：

```text
连续30：已有执行身份，不重新创建MES工单
自由70：无执行身份，后续需要新TaskNo/新MES工单
```

因此本方案同时闭合：

> **数量闭环 + 产能闭环。**

---

# 十一、2号位传给1号位的连续份额数据

1号位需要知道“这是连续份额”，但不需要理解MES业务来源。

每个连续份额至少需要表达以下语义（物理字段名按现有契约最小实现）：

```text
ContinuationKey        连续性不透明关联键
ProductionInstruction  PI身份
StartStage/Operation   当前执行起点
NetOutputQty           当前连续净合格数量
PlannedProcessQty      2号位按计划良率反算后的产能数量
NoSplitMerge           连续执行身份不可被普通拆/合掉
PreferredResource      资源连续性首选（可选；仅在存在可靠来源时提供）
EligibleResources      全部合法资源
Material Quantity-Time 物料数量-可用时间约束
```

如需要已有执行顺序/依赖，也由2号位按既有DomainSolveRequest契约表达。

## 10.1 APS旧Task连续份额

2号位内部维护：

```text
ContinuationKey
→ OldTaskNo
→ MESWorkOrderNo
```

1号位返回最终排程任务（FinalTask）后：

```text
2号位继承OldTaskNo
```

## 10.2 无TaskNo外部MES连续份额

2号位内部维护：

```text
ContinuationKey
→ Existing MESWorkOrderNo
→ OldTaskNo = NULL
```

1号位返回最终排程任务（FinalTask）后：

```text
2号位生成新TaskNo
→ APS侧建立“新TaskNo ↔ Existing MESWorkOrderNo”的映射
→ 不创建新MES工单
```

## 10.3 为什么不直接把MESWorkOrderNo交给1号位做业务判断

因为1号位不得出现：

```text
if (MESWorkOrderNo != null) { ...MES业务逻辑... }
```

1号位只认连续性求解语义，不认MES生命周期语义。

---

# 十二、无TaskNo外部MES工单“接管”的准确含义

“接管”不是：

```text
2号位看到WO101
→ 直接创建正式排程Task
```

而是：

```text
2号位识别WO101可承接当前需求30
        ↓
形成连续求解输入C001
        ↓
1号位对30做有限产能排程
        ↓
返回最终排程任务（FinalTask） + C001
        ↓
2号位持久化最终排程任务（FinalTask）
        ↓
生成正式TaskNo T1001
        ↓
绑定：T1001 ↔ WO101
```

MES中的WO101仍是原工单，不重新创建。

这里的“建立绑定/映射”默认是：

> **在APS侧的Task—MES关联/下发关系中记录TaskNo与现有MES工单号（MESWorkOrderNo）的关系。除非MES接口另有明确能力和必要性，本裁决不要求回写或修改MES原工单本体的TaskNo字段。**

从下一版本开始，APS即可通过该映射把它识别为普通的：

```text
APS TaskNo ↔ MESWorkOrderNo
```

连续执行对象。

---

# 十三、首次APS上线的处理

首次正式运行时，MES中可能已经存在大量历史工单。

不再新增单独“上线历史Task迁移平台”。

按MES四状态直接处理：

| 首次上线状态 | 处理 |
|---|---|
| DELETED | 不形成执行连续Task |
| RELEASED | 原则上删除/作废，按当前APS计划重新生成 |
| 开工中/已开工未完成（IN_PROGRESS） | 如果承接当前有效需求，按“无TaskNo既存MES连续份额”进入1号位有限产能求解，最终排程任务（FinalTask）后由2号位生成TaskNo，并在APS侧建立与原MES工单号的映射 |
| CLOSED | 仅作为历史事实进入生产指示位置（PI Position），不补TaskNo |

因此首次上线与后续人工MES工单使用同一机制，无额外架构分支。

---

# 十四、同一PI同一工序的执行先后关系

仅禁止多设备并行还不够。对于同一生产指示（PI）同一工序（Operation），如果当前同时存在：

```text
已有执行连续份额
+
本次新增自由份额
```

1号位必须满足：

```text
已有执行连续份额
→ 本次新增自由份额
```

即：

> **已有执行连续份额具有执行先行性。新增自由份额不得被排到同PI同工序的既存执行连续份额之前，也不得与其在其它设备上重叠。**

原因是既存执行工单已经是现场现实中的有效执行对象；本次新增自由份额尚未创建新的MES工单，不能由Solver为了局部优化把“未来新工单”排到“已经开工的现有工单”之前。

允许：

```text
既存执行在MC01完成
→ 后续自由份额继续MC01
```

或：

```text
既存执行在MC01完成
→ 后续自由份额顺序切换到其它合法设备MC03
```

禁止：

```text
自由份额先排
→ 既存执行后排
```

也禁止：

```text
既存执行MC01
||
自由份额MC03并行
```

如果现场人工已经形成并行MES事实，按真实执行事实处理；本规则只禁止APS自动新增或反转执行顺序。

---

# 十五、资源连续性：V1不新增工单级最后报工设备字段

跨版本连续性需要考虑资源偏好，但当前冻结MES数据契约没有可靠的实际报工设备字段。

因此资源连续性按以下方式处理。

## 15.1 APS旧Task连续份额

若开工中（`IN_PROGRESS`）MES工单已经存在APS TaskNo绑定，则2号位可以取得上一ACTIVE Task的计划资源。

资源选择顺序为：

```text
1. 上一ACTIVE APS Task原计划资源（如仍满足资源资格）
2. 同资源组 / OperationResourceEligibility内其它合法资源
```

上一APS资源只是连续性偏好，不是实际设备硬锁。

## 15.2 无TaskNo人工/历史MES连续份额

由于没有上一APS计划资源，而且现有MES契约也没有可靠实际设备字段，因此：

```text
不设置人为资源首选
→ 由1号位在OperationResourceEligibility / 资源组的全部合法资源中正常求解
```

不得为了“看起来连续”而猜测一台设备。

## 15.3 未来如取得工序级实际设备事实

如果MES后续能够稳定提供：

> 某一具体工序最后一次实际报工所使用的设备

可以另行把它作为**工序级资源连续性偏好**接入。

但必须满足：

- 来源真实可靠；
- 与具体MES工单 + 工序对应；
- 不把工序设备错误提升为整张MES工单唯一设备；
- 沿用既有字段名或经正式字段裁决后新增；
- 仍然只作为偏好，不突破资源资格、日历和其它硬约束。

本轮V1不以此作为上线P0条件。

---
# 十六、V1红线：同一PI同一工序不得由APS自动多设备并行

正式冻结：

> **APS V1中，同一生产指示（PI）的同一工序（Operation），任一时点最多只有一个APS自动排定的活跃生产资源。**

## 14.1 允许

- 同一资源顺序拆批；
- 前一设备段结束后顺序切换至另一合法设备；
- 不同Operation按既有Routing / Stage Overlap规则合法重叠；
- 现场MES人工拆分形成真实并行执行。

## 14.2 禁止APS自动生成

```text
PI001 / NC
MC01 08:00~12:00 做60
同时
MC03 08:00~12:00 做40
```

## 14.3 连续份额与自由份额之间同样受约束

例如：

```text
已有MES连续30
自由需求70
```

1号位不得把自由70排到另一台设备与连续30重叠生产。

这正是连续份额必须进入1号位有限产能求解的另一重要原因。

## 14.4 已存在的人工并行事实

如果现场已经人工形成：

```text
WO101 → MC01
WO102 → MC03
```

APS不得否认现实。

这些工单的真实报工仍进入5号位事实链；本轮执行现实可以被识别，但APS不得再自动增加新的并行Task。

---

# 十七、剩余数量的正确层级与语义

本轮明确：

> **不存在也不新增工单级 `RemainingNetQty`。**

现有剩余数量继续保持在APS本地进度快照层：

```text
OperationProgressSnapshot.RemainingQty
= 工序级剩余数量
= max(PlannedQty - GoodQty, 0)
```

以及：

```text
StageProgressSnapshot.RemainingQty
= Stage级剩余数量
= max(PlannedQty - GoodCompletedQty, 0)
```

其中：

- `GoodQty`继续表示工序级累计良品完成数量；
- `GoodCompletedQty`继续表示Stage级累计良品完成数量；
- 两者不得改名、混用；
- `MES_APS_WorkOrder_View / MESWorkOrderSnapshot`不新增剩余数量字段。

跨版本连续份额的净业务数量由5号位事实解释 + 2号位承接判断在运行时形成，并受当前逻辑生产需求（LogicalProductionDemand）上限约束。

因此：

> **连续份额数量是派生结果，不是新的MES工单数量真相。**

---
# 十八、Task数量、计划良率（Planning Yield）与PlannedProcessQty

## 16.1 Task业务数量

```text
Task.Quantity / NetOutputQty
= 净合格业务数量
```

用于Demand、Pegging、Allocation、计划员显示和连续份额数量闭合。

## 16.2 PlannedProcessQty

由2号位根据：

- 当前净需求；
- 生产指示位置（PI Position）；
- 下一工序上下文（NextOperationContext）；
- 尚未完成Stage；
- 版本化计划良率（Planning Yield）；

反算。

1号位按PlannedProcessQty计算设备负荷/加工时长。

## 16.3 连续份额也必须重新按“剩余工艺链”计算PlannedProcessQty

已有MES连续30不等于设备只按30占能力。

2号位仍需根据当前未完成Stage计算未来PlannedProcessQty，但已经完成的Stage不得重新套良率。

## 16.4 PI Supply不因良率放大

ERP PI RemainingQty仍是PI Supply数量边界。

计划良率（Planning Yield）不得把PI Supply本身放大。

---

# 十九、物料数量-时间约束（Material Quantity-Time）

计划良率（Planning Yield）不仅影响设备加工投入，也影响相应未完成工艺链的上游投入与材料需求。

因此2号位必须形成：

```text
Material RequiredQty + AvailableTime
```

物料数量取决于：

- 实际消耗点；
- 当前下一工序上下文（NextOperationContext）；
- 未完成Stage；
- 计划良率（Planning Yield）；
- 已选Supply与Allocation。

不得机械规定：

```text
MaterialQty = Task.PlannedProcessQty
```

1号位只消费2号位提供的Material Quantity-Time，不重新计算Yield，不替换Pegging选定Supply。

V1仍不额外引入BOM ScrapRate损耗链。

---

# 二十、固定区（Firm）/冻结区（Frozen）与MES执行状态

Fence仍由2号位每次ScheduleRun按：

```text
HorizonStart
FrozenDays
FirmDays
PlannedStartTime
```

运行时分类。

不新增永久：

```text
IsFirm
IsFrozen
ExecutionLock平台
```

## 18.1 IN_PROGRESS

存在真实执行事实时，Task/MES执行身份处理优先于普通重排，但资源仍按本版Preference规则求解，不因为“已开工”自动锁死设备。

## 18.2 RELEASED / DELETED

执行身份不连续，不等于自动解除固定区（Firm）/冻结区（Frozen）计划约束。

如果Fence仍要求固定，新TaskNo仍须在合法围栏语义下重建。

## 18.3 Firm重开

继续复用：

```text
aps.reschedule.manual
+ 人工确认
+ 审计
```

不新增第二权限体系。

---

# 二十一、同TaskNo旧资源块“不得自阻挡”

同TaskNo上一版本自己的旧计划块不得作为“其它Task的ResourceBlock”再次挡住自己。

但不得错误理解成删除本Task自身有效的：

- Execution；
- Frozen；
- Firm；
- 连续性约束。

正确做法：

```text
同TaskNo自身有效约束
→ 转成本Task连续/锚点约束

其它TaskNo有效固定占用
→ 外部ResourceBlock
```

由2号位在组装DomainSolveRequest前完成。

---

# 二十二、2号位给1号位的最终输入分层

## A. 硬约束

包括：

- Execution事实；
- Frozen；
- Firm；
- 外Domain ACTIVE资源占用；
- 其它不可移动现实。

## B. 已有APS Task连续份额

最小业务语义：

```text
ContinuationKey
NetOutputQty
PlannedProcessQty
StartOperation
NoSplitMerge = true
PreferredResource = 工序级实际资源事实（如有）
OriginalResource = 上一APS资源（如有）
全部Eligible Resources
Material Quantity-Time
```

## C. 无TaskNo外部MES连续份额

最小业务语义与B基本一致：

```text
ContinuationKey
NetOutputQty
PlannedProcessQty
StartOperation
NoSplitMerge = true
PreferredResource = 工序级实际资源事实（如有）
全部Eligible Resources
Material Quantity-Time
```

区别仅由2号位内部知道：

```text
ContinuationKey → Existing MESWorkOrderNo，OldTaskNo = NULL
```

1号位不区分B/C来源。

## D. 自由逻辑生产需求（LogicalProductionDemand）

没有既存执行身份的剩余净需求，正常交1号位求解，可按既有V1拆/合规则处理，但仍受“同PI同Operation单活跃资源”红线约束。

---

# 二十三、1号位返回以后2号位的身份落盘规则

1号位返回：

```text
最终排程任务（FinalTask） + ContinuationKey（连续份额时）
```

2号位按内部映射处理。

## 21.1 原APS Task连续

```text
ContinuationKey → OldTaskNo T001 → WO001
```

则：

```text
新TaskId
TaskNo = T001
MESWorkOrderNo = WO001
不创建新MES工单
```

## 21.2 无TaskNo外部MES连续

```text
ContinuationKey → WO101，OldTaskNo = NULL
```

则：

```text
新TaskId
新TaskNo = T1001
MESWorkOrderNo = WO101
不创建新MES工单
```

从此以后：

```text
T1001 ↔ WO101
```

进入普通跨版本连续性体系。

## 21.3 自由生产份额

```text
最终排程任务（FinalTask）无既存执行ContinuationKey
```

则：

```text
新TaskId
新TaskNo
→ 正常MES下发
→ MES生成新MESWorkOrderNo
```

---

# 二十四、计划下发与MES最后安全校验

APS主计算必须在调用1号位前识别既存IN_PROGRESS执行，不能等到MES下发阶段才发现。

但DataCutoff到MES实际创建之间仍存在并发窗口：现场可能新建工单或改变状态。

因此MES已有同PI数量/重复校验继续作为：

> **最终执行安全门，而不是APS主计算规则。**

至少需要回传：

```text
PENDING
CREATED
FAILED
FailureReason
```

对于已经存在MES工单的连续份额：

```text
不创建第二张MES工单
```

---

# 二十五、端到端示例一：APS旧Task + 人工MES工单 + 新需求

当前事实：

```text
PI001当前逻辑生产需求（LogicalProductionDemand） Q = 100

T001 ↔ WO001
WO001 IN_PROGRESS
Remaining = 30
工序级实际资源事实 = MC01

人工WO101
TaskNo = NULL
IN_PROGRESS
Remaining = 20
工序级实际资源事实 = MC03
```

2号位分桶：

```text
30 → 连续C001 → 旧T001 / WO001
20 → 连续C002 → 外部WO101
50 → 自由需求
```

三部分一起交1号位。

1号位必须：

- 为30、20、50计算未来有限产能；
- 保留C001/C002；
- 不把连续份额普通拆/合掉；
- 不允许同PI同Operation在不同设备时间重叠；
- 资源首选按工序级实际资源事实执行，但允许顺序换合法设备。

返回后2号位：

```text
C001 → 继承T001，继续WO001
C002 → 新TaskNo T002，绑定既有WO101，不新建MES工单
自由50 → 新TaskNo T003，正常下发创建WO102
```

---

# 二十六、端到端示例二：首次APS上线

APS第一次运行，MES已有：

```text
历史WO9001
PI002
IN_PROGRESS
Remaining = 40
无TaskNo
```

5号位先正常形成生产指示位置（PI Position）；2号位得到：

```text
PI002当前Q = 90
```

2号位：

```text
40 → 外部MES连续份额C9001
50 → 自由需求
```

1号位求解后：

```text
C9001对应最终排程任务（FinalTask）
自由50对应最终排程任务（FinalTask）
```

2号位持久化：

```text
C9001 → 新TaskNo T9001 ↔ 原WO9001
自由50 → 新TaskNo T9002 → 创建新MES工单
```

无需另建首次上线历史迁移平台。

---

# 二十七、端到端示例三：无有效需求但MES仍执行

```text
当前Q = 0
人工WO101 IN_PROGRESS Remaining = 20
```

处理：

```text
不创建逻辑生产需求（LogicalProductionDemand） 20
不为“补TaskNo”而制造正常Task
记录执行超量（Execution Over-Commit） / Demand Mismatch
```

若能够取得可靠实时资源占用，可形成外部现实ResourceBlock；仅有工序级实际资源事实时不冒充实时硬锁。

---

# 二十八、V1明确不做

本版不新增：

- 跨版本Task相似度匹配算法；
- 完整MES报工历史复制平台；
- 独立“首次上线历史Task迁移平台”；
- 多资源并行自动拆分；
- 当前资源集合（CurrentResourceSet）/ 多设备执行集合；
- 实时开放执行段平台；
- 第二套Fence版本平台；
- 固定标识（IsFirm）/冻结标识（IsFrozen）永久字段；
- 新执行锁（ExecutionLock）平台；
- 1号位MES状态解析；
- 1号位TaskNo生命周期治理；
- 1号位计划良率（Planning Yield）或材料需求重算；
- BOM ScrapRate额外损耗链。

---

# 二十九、完整论证结论

本版已按以下反例重新验证：

1. 正常上一版本APS Task已开工未完成；
2. APS Task对应MES工单被删除；
3. APS Task对应MES工单未开工；
4. APS Task对应MES工单已完成；
5. 人工删除APS工单后创建无TaskNo MES工单；
6. APS首次上线时已有历史IN_PROGRESS MES工单；
7. 当前Q大于既存执行量E；
8. 当前Q小于E；
9. 当前Q为0但MES仍执行；
10. 人工现场已存在同PI同Operation并行MES工单；
11. APS求解器（Solver）试图把同PI同Operation新需求并行到其它设备；
12. 工序级实际资源事实与上一APS资源不同；
13. 连续份额只扣数量但不进入1号位有限产能求解；
14. 连续份额与自由份额重复计量；
15. 计划良率（Planning Yield）影响能力和材料；
16. 同TaskNo上一版本资源块自阻挡；
17. FULL计算后、MES下发前现场又出现新工单。

结论：

> **“5号位标准化生产事实 → 2号位形成当前净需求 → 2号位按逐MES工单识别既存执行承接 → 连续份额与自由份额共同进入有限产能 → 既存执行先于新增自由份额 → 2号位后置治理TaskNo/MES身份”的架构可以实现业务闭环。**

它实现：

- 数量闭环；
- 有限产能闭环；
- TaskNo/MES工单身份闭环；
- 首次上线闭环；
- 人工MES干预后的恢复闭环；
- V1 MES不支持APS自动多设备并行时的兼容闭环。

本方案新增的复杂度仅为必要业务复杂度：

1. 2号位增加既存执行覆盖/分桶；
2. 1↔2契约增加或复用连续性关联键及不可普通拆合语义；
3. 2号位在最终排程任务（FinalTask）持久化阶段多一种“绑定既有MES工单但新建TaskNo”的落盘分支。

不需要新增第二套求解器、MES历史平台、Task相似度算法或复杂多资源执行模型，因此**不构成过度设计**。

---

# 三十、最终实施复核结论

本版在冻结前再次按职责、数量、身份、产能和MES现场干预进行复核，结论如下：

1. **职责无回流**：5号位解释MES事实；2号位做供需承接、连续份额分桶、求解编排和TaskNo身份落盘；1号位只做有限产能资源/时间求解。
2. **数量无重复**：当前净需求量Q只被拆成“逐工单既存执行连续份额 + 自由生产份额”，两者之和必须等于Q；既存执行剩余量合计E不得再次叠加到Q。
3. **产能不漏算**：既存执行连续份额虽然不需要新建MES工单，但仍进入1号位，占用后续资源与时间。
4. **身份可追溯**：已有APS Task继续原TaskNo；无TaskNo既存MES工单在最终排程任务形成后，由2号位在APS侧补建TaskNo映射；自由份额使用新TaskNo并新建MES工单。
5. **顺序符合现场现实**：同PI同工序先完成既存执行连续份额，再安排新增自由份额；APS不自动制造多设备并行。
6. **异常不制造需求**：既存执行量超过当前需求或当前需求为0时，只形成执行超量/需求不一致异常，不反向抬高需求。
7. **V1复杂度受控**：不新增第二套MES解释器、不新增Task相似度算法、不新增历史迁移平台、不新增多资源并行执行平台。

因此，本版可作为跨版本排程连续性的正式冻结裁决。

---

# 三十一、最终一句话冻结口径

> **APS V1每次夜间全量排程（FULL）必须先由5号位基于既有工单状态、工序进度、Stage进度及生产指示位置（PI Position）形成标准化既存执行上下文，再由2号位完成供需承接（Pegging）并形成当前逻辑生产需求（LogicalProductionDemand）净数量；2号位随后按每个开工中（IN_PROGRESS）MES工单逐一识别其可承接的连续份额。连续份额数量由工序级 `GoodQty / RemainingQty`、Stage级 `GoodCompletedQty / RemainingQty` 和PI Position运行时派生，不要求也不新增工单级 `RemainingNetQty`。已有执行连续份额与自由生产份额均进入1号位有限产能求解；1号位不解析MES、不治理TaskNo。最终排程任务（FinalTask）返回后，2号位对已有APS Task连续份额继承原TaskNo；对无TaskNo但承接有效需求的人工/历史MES工单生成新TaskNo并仅在APS侧建立与原MES工单的一对一映射；对自由份额生成新TaskNo并正常下发MES。当前MES契约没有可靠实际报工设备字段，因此V1不新增工单级 `LastReportResourceCode`：已有APS Task连续时以上一ACTIVE Task资源作为求解偏好，无TaskNo人工/历史连续份额由1号位在全部合法资源中正常求解。既存执行量超过当前需求或当前需求为0时不得反向创造需求（Demand）。**