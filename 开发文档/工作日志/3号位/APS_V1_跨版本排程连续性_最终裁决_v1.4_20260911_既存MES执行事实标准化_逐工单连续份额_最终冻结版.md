# APS V1 跨版本排程连续性最终裁决

**版本：v1.4（既存MES执行事实标准化 / 逐工单连续份额 / Task身份后置落盘最终冻结版）**  
**日期：2026-09-11（北京时间）**  
**性质：0号位最终业务裁决**  
**替代版本：v1.3及更早版本中与本版冲突的表述**  
**适用范围：APS V1 夜间全量排程（FULL）的跨版本连续性、MES既存执行承接、TaskNo生命周期、首次上线历史工单、人工MES工单、固定区（Firm）/冻结区（Frozen）、计划良率、物料数量-时间约束（Quantity-Time）、有限产能资源连续性。**

---

# 一、本版修订目的

本版在v1.3基础上完成最后一轮职责与闭环复核，重点收口四个容易产生实现歧义的问题：  
1）MES既存执行事实仍由5号位标准化，2号位只消费，不重新解析MES；  
2）既存执行剩余量合计只用于数量闭合，进入1号位时必须按每个MES工单保持独立连续份额；  
3）无TaskNo人工/历史MES工单的“绑定”仅指APS侧建立TaskNo与现有MES工单号的映射，不要求回写MES原工单；  
4）同一生产指示（PI）同一工序中，已有执行连续份额必须先于本次新增自由份额，不能只做到“不并行”而允许顺序反转。

本版新增并冻结以下核心结论：

1. **每次FULL仍然先建立本次生产数量真相，再处理跨版本执行连续性。** 旧TaskNo、历史MES工单均不得反向决定本次应生产总量。
2. **所有真实MES报工先进入5号位事实链。** 无TaskNo不影响其进入生产指示位置（PI Position）和下一工序上下文（NextOperationContext）。
3. **5号位负责把MES原始工单/报工状态整理成标准化既存执行事实（Existing MES Execution Fact）；2号位只消费该标准事实，不重新读取或解释MES原始状态。**
4. **2号位在当前逻辑生产需求（LogicalProductionDemand）已经形成以后，才把5号位提供的“已开工未完成（STARTED_INCOMPLETE）”既存执行事实与当前需求做承接。** 数量无需完全相等。
5. **既存执行剩余量合计（E）只用于总量闭合；真正进入1号位时，每个MES工单必须保持独立连续份额，不能因为合计E而把多个MES工单合成一个求解对象。**
6. **既存执行份额不能只从当前需求中扣除后从有限产能世界消失。** 其未来未完成部分仍会占资源，必须和新增自由需求一起进入1号位有限产能求解。
7. **无TaskNo的人工/历史已开工未完成MES工单，也必须以“连续份额”形式进入1号位。** 但1号位无需理解MES工单号（MESWorkOrderNo）、TaskNo来源或MES生命周期，只需消费不透明连续性关联键和排程数据。
8. **1号位仍然负责生成有限产能排程结果（最终排程任务（FinalTask））；2号位不在求解器（Solver）前直接创建正式Task。**
9. **1号位返回最终排程任务（FinalTask）以后，由2号位统一决定正式TaskNo身份：**
   - 原APS Task连续份额：继承原TaskNo；
   - 无TaskNo既存MES连续份额：生成新TaskNo，并仅在APS侧建立“新TaskNo ↔ 原MES工单号”的映射，不要求回写MES原工单，也不创建新MES工单；
   - 自由生产份额：生成新TaskNo，并正常创建新MES工单。
10. **首次APS上线不再单独建设“历史工单迁移平台”。** 已有历史“已开工未完成（STARTED_INCOMPLETE）”工单直接复用无TaskNo既存MES连续份额机制。
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
2. STARTED_INCOMPLETE既存MES执行份额识别；
3. 连续份额/自由份额拆分；
4. 向1号位组装求解输入；
5. 最终排程任务（FinalTask）持久化；
6. 决定正式TaskNo继承或新建；
7. TaskNo ↔ MESWorkOrderNo绑定与MES下发治理。

因此：

> **2号位在Solver前不直接制造一个跳过1号位排程的正式Task。**

---

# 六、MES工单四状态与正常跨版本生命周期

MES ODS需标准化至少四类业务状态：

```text
DELETED            已删除
NOT_STARTED        未开工
STARTED_INCOMPLETE 已开工未完成
COMPLETED          已完成
```

## 6.1 DELETED

如果上一APS关系为：

```text
T001 ↔ WO001
WO001 = DELETED
```

则：

```text
T001执行生命周期结束
```

若本次仍有当前净需求：

```text
1号位形成新最终排程任务（FinalTask）
→ 2号位生成新TaskNo
→ MES创建新MESWorkOrderNo
```

旧T001不得重新绑定新MES工单。

## 6.2 NOT_STARTED

未开工MES工单不构成跨版本执行事实连续性。

下一FULL：

```text
旧TaskNo执行身份不继承
→ 原未开工MES工单原则上删除/作废
→ 当前需求重新进入1号位有限产能求解
→ 新最终排程任务（FinalTask）
→ 2号位新TaskNo
→ 新MESWorkOrderNo
```

若旧未开工工单没有及时删除：

> MES现有同PI数量校验作为最终安全门，允许拒绝新工单创建；APS必须接收FAILED + FailureReason。

这属于下发并发/残留异常兜底，不替代APS主计算。

## 6.3 STARTED_INCOMPLETE

STARTED_INCOMPLETE说明存在真实执行。

但需要分两类：

### A. 已有APS TaskNo绑定

```text
T001 ↔ WO001
WO001 = STARTED_INCOMPLETE
```

若本次当前净需求仍有对应有效份额，则：

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
STARTED_INCOMPLETE
```

若本次当前净需求仍有对应有效份额，则：

```text
2号位识别该份额
→ 分配ContinuationKey
→ 作为外部MES连续份额交给1号位
→ 1号位生成最终排程任务（FinalTask）并返回ContinuationKey
→ 2号位持久化时生成新TaskNo
→ APS侧建立“新TaskNo ↔ 既有WO101”的映射
→ 不创建新的MES工单
```

因此“接管”发生在最终排程任务（FinalTask）产生以后，不是2号位跳过1号位直接创建排程Task。

## 6.4 COMPLETED

已完成MES工单的执行生命周期结束。

其报工事实继续进入5号位生产指示位置（PI Position）。

若本次仍有其它合法净缺口：

```text
新最终排程任务（FinalTask）
→ 新TaskNo
→ 新MES工单
```

不得复活已完成TaskNo。

---

# 七、5号位到2号位的既存MES执行标准事实

本版进一步冻结职责边界：

> **5号位负责“MES事实解释”；2号位负责“当前需求与既存执行如何承接”。**

因此2号位不得为了跨版本连续性再建立第二套MES解释器，也不得直接依赖MES原始状态码重新推导“是否已开工未完成”。

5号位应向2号位提供标准化既存执行事实（Existing MES Execution Fact）或现有等价DTO/运行上下文。每个MES工单至少应能表达以下业务语义：

```text
MES工单号（MESWorkOrderNo）
生产指示号（ProductionInstructionNo）
当前Stage / Operation
标准化执行状态（ExecutionStatus）
剩余净合格数量（RemainingNetQty）
最后报工资源（最后报工资源（LastReportResourceCode），如有）
数据截止时间（DataCutoffTime）
```

其中：

- 是否存在APS TaskNo绑定，不要求由5号位判断；2号位可根据APS侧已有绑定关系判断；
- 剩余净合格数量必须与当前Task净业务数量同口径；
- 如果现有5号位DTO/服务能够承载上述语义，应优先扩展/复用，不要求为了本裁决新建长期持久化平台；
- 2号位只消费5号位标准化结果，不重新读取MES原始报工推导状态或下一工序。

---

# 八、2号位如何计算既存执行份额与自由份额

设：

```text
Q = 当前逻辑生产需求（LogicalProductionDemand）净合格数量
E = 与当前PI + 当前执行起点相对应的“已开工未完成（STARTED_INCOMPLETE）”既存MES工单剩余净合格量合计
```

正常场景：

```text
E <= Q
```

则：

```text
已有执行连续份额 = E
自由生产份额 = Q - E
```

例如：

```text
Q = 100

APS已有WO001剩30
人工WO101剩20

E = 50
```

则：

```text
30 → APS旧Task连续份额
20 → 无TaskNo外部MES连续份额
50 → 自由生产份额
```

三部分净业务量合计仍然必须等于100。

### 8.0 既存执行剩余量合计只用于总量计算，进入求解器时必须逐工单保留身份

例如：

```text
WO001 剩余30，最后报工资源MC01
WO101 剩余20，最后报工资源MC03

既存执行剩余量合计E = 50
```

2号位可以用`E=50`计算自由生产份额，但传给1号位时必须保持两个独立连续份额：

```text
连续份额C001
→ 对应WO001
→ 净数量30
→ 资源首选MC01

连续份额C002
→ 对应WO101
→ 净数量20
→ 资源首选MC03

自由生产份额
→ 净数量50
```

不得把WO001和WO101合并成：

```text
一个连续份额
→ 净数量50
```

因为一旦合并，就会丢失：

- 各自MES工单身份；
- 各自最后报工资源；
- 各自剩余数量事实；
- 1号位返回以后2号位应继承哪个TaskNo或应与哪个既有MES工单建立APS侧映射。

正式冻结：

> **既存执行剩余量可以合计用于数量闭合，但既存执行身份不得合并。一个MES工单对应一个独立连续份额和一个独立连续性关联键（ContinuationKey）或现有等价稳定键。**

## 8.1 数量不要求完全相等

APS不要求：

```text
MES工单RemainingQty == 当前逻辑生产需求（LogicalProductionDemand）
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

否则会把同一份生产数量重复送入Solver。

---

# 九、E > Q或Q = 0时的执行超量异常

## 9.1 E > Q

例如：

```text
当前Q = 50
STARTED_INCOMPLETE MES剩余量合计E = 70
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

如果MES能够提供可靠“当前实际正在占用资源”，即使Q=0，也可作为现实资源阻挡块。

但目前已确认可稳定取得的是：

```text
最后报工资源（LastReportResourceCode）
```

它是最后报工设备，不等于实时当前资源。

因此V1不得仅凭最后报工资源（LastReportResourceCode）在Q=0异常场景中把某台设备永久硬锁为当前占用。

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
PreferredResource      资源连续性首选（如有）
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
| NOT_STARTED | 原则上删除/作废，按当前APS计划重新生成 |
| 已开工未完成（STARTED_INCOMPLETE） | 如果承接当前有效需求，按“无TaskNo既存MES连续份额”进入1号位有限产能求解，最终排程任务（FinalTask）后由2号位生成TaskNo，并在APS侧建立与原MES工单号的映射 |
| COMPLETED | 仅作为历史事实进入生产指示位置（PI Position），不补TaskNo |

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

# 十五、资源连续性与最后报工资源（LastReportResourceCode）

MES已确认：

- 同一MES工单同一工序不会同时由两台设备生产；
- 在同一资源组内允许顺序换机；
- 前一设备生产并报工结束后，下一设备才能继续开工；
- 现场可能在未开工/重新开工前修改实际设备。

因此：

## 13.1 APS旧Task连续份额资源顺序

```text
1. 最后报工资源（LastReportResourceCode）（如仍合法）
2. 上一ACTIVE APS Task原计划资源
3. 同资源组 / OperationResourceEligibility内其它合法资源
```

## 13.2 无TaskNo外部MES连续份额资源顺序

因为没有上一APS计划资源：

```text
1. 最后报工资源（LastReportResourceCode）（如仍合法）
2. 其它合法Eligibility / 同资源组资源
```

## 13.3 LastReportResource不是硬锁

它表示最后一次已经形成报工事实的资源，不是实时当前开放执行设备。

因此它是：

> **资源连续性首选（Preference），不是资源硬锁（Hard Lock）。**

全部合法资源均不可行后才允许进入Unscheduled。

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

# 十七、MES RemainingQty数量语义

进入APS连续性主链的MES `RemainingQty` 必须标准化为：

> **该MES工单尚需完成的净合格目标数量。**

它必须与：

```text
Task.Quantity / NetOutputQty
```

处于相同净业务数量口径。

如果MES原始RemainingQty是加工投入量、计划投入量或其它口径，5号位必须先在防腐层标准化，避免APS再次套计划良率（Planning Yield）造成重复放大。

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

## 18.1 STARTED_INCOMPLETE

存在真实执行事实时，Task/MES执行身份处理优先于普通重排，但资源仍按本版Preference规则求解，不因为“已开工”自动锁死设备。

## 18.2 NOT_STARTED / DELETED

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
PreferredResource = LastReportResource（如有）
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
PreferredResource = LastReportResource（如有）
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

APS主计算必须在调用1号位前识别既存STARTED_INCOMPLETE执行，不能等到MES下发阶段才发现。

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
WO001 STARTED_INCOMPLETE
Remaining = 30
LastReportResource = MC01

人工WO101
TaskNo = NULL
STARTED_INCOMPLETE
Remaining = 20
LastReportResource = MC03
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
- 资源首选按LastReportResource执行，但允许顺序换合法设备。

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
STARTED_INCOMPLETE
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
人工WO101 STARTED_INCOMPLETE Remaining = 20
```

处理：

```text
不创建逻辑生产需求（LogicalProductionDemand） 20
不为“补TaskNo”而制造正常Task
记录执行超量（Execution Over-Commit） / Demand Mismatch
```

若能够取得可靠实时资源占用，可形成外部现实ResourceBlock；仅有LastReportResource时不冒充实时硬锁。

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
6. APS首次上线时已有历史STARTED_INCOMPLETE MES工单；
7. 当前Q大于既存执行量E；
8. 当前Q小于E；
9. 当前Q为0但MES仍执行；
10. 人工现场已存在同PI同Operation并行MES工单；
11. APS求解器（Solver）试图把同PI同Operation新需求并行到其它设备；
12. 最后报工资源（LastReportResource）与上一APS资源不同；
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

> **APS V1每次夜间全量排程（FULL）必须先由5号位基于全部ERP/MES真实事实形成生产指示位置（PI Position）、下一工序上下文（NextOperationContext）及标准化既存MES执行事实，再由2号位完成供需承接（Pegging）并形成当前逻辑生产需求（LogicalProductionDemand）净数量；随后2号位只消费5号位的标准化事实，按每个MES工单独立识别“已开工未完成（STARTED_INCOMPLETE）”既存执行可承接的当前需求份额。既存执行剩余量合计（E）只用于数量闭合，不能把多个MES工单合并成一个连续求解对象；每个MES工单必须保持独立连续性关联键。已有执行连续份额与自由生产份额均进入1号位有限产能求解，且同一PI同一工序中已有执行连续份额必须先于新增自由份额，不得自动形成多设备并行。1号位只负责资源和时间求解并原样返回连续性关联键，不解析MES、不治理TaskNo。最终排程任务（FinalTask）返回后，2号位对已有APS Task连续份额继承原TaskNo；对无TaskNo但承接有效需求的人工/历史MES工单生成新的TaskNo，并仅在APS侧建立与原MES工单号（MESWorkOrderNo）的一对一映射，不要求回写MES原工单，也不创建新MES工单；对自由份额生成新TaskNo并正常下发MES。既存执行量超过当前需求或当前需求为0时不得反向创造需求（Demand），而进入执行超量/需求不一致异常（Execution Over-Commit）。最后报工资源（LastReportResourceCode）只作为合法资源中的连续性首选，不作为实时资源硬锁。**