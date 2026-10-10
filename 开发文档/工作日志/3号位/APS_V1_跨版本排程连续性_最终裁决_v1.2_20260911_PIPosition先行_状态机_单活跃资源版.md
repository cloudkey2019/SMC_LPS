# APS V1 跨版本排程连续性最终裁决

**版本：v1.2（PI Position先行 / MES状态机 / 单PI单工序单活跃资源版）**  
**日期：2026-09-11（北京时间）**  
**性质：0号位最终业务裁决**  
**适用范围：APS V1 夜间FULL跨版本排程连续性、TaskNo生命周期、MES工单绑定、PI Position / NextOperationContext、Firm/Frozen、计划良率、物料Quantity-Time、有限产能资源连续性**

---

# 一、本版裁决目的

本版在 v1.1 基础上，对以下新确认事实做最终收口：

1. APS `TaskNo` 与 MES `MESWorkOrderNo` 在正式APS下发链中始终保持一对一；
2. 每次FULL必须先由5号位基于全部ERP/MES事实计算 `PI Position + NextOperationContext`，再由2号位形成当前 `LogicalProductionDemand`；旧TaskNo不得反向决定本次生产数量；
3. 只有“已开工未完成”的APS关联MES工单才具备TaskNo执行身份连续性；
4. MES工单“已删除、未开工、已完成”均不继续沿用旧TaskNo；若本次仍有净需求，由1号位形成新FinalTask，2号位生成新TaskNo并重新下发MES工单；
5. 同一MES工单同一工序同一时点只在一台设备生产；MES允许在资源组内按“上一设备报工结束后，再到下一设备开工”的顺序切换；
6. APS V1禁止1号位自动把同一PI同一工序拆到两台及以上设备并行生产；确需并行时由现场MES人工拆分，APS下一FULL通过MES事实重新吸收；
7. `LastReportResourceCode`是已开工未完成Task跨版本资源连续性的首选资源，不是硬资源锁；
8. Task业务数量始终是净合格需求；计划良率由2号位反算 `PlannedProcessQty`，同时影响对应未完成工艺链的物料需求；1号位只消费计算结果，不自行重算良率和材料数量。

除上述内容外，不重新打开PI Position、Pegging、Supply、Domain、Routing、Candidate及现有1/2/3/5号位冻结职责。

---

# 二、总原则：先重建“今天的生产真相”，再判断“昨天的Task还能否继续”

跨版本连续性的主链正式冻结为“两阶段”。

## 2.1 第一阶段：本次生产真相重建

每次夜间FULL先执行：

```text
ERP PI RemainingQty
+
全部MES事实
（包括APS下发工单、人工MES工单、无TaskNo工单的真实报工）
+
Routing
+
XC / Transit等强位置事实
        ↓
5号位
PI Position
+
NextOperationContext
        ↓
2号位
Pegging / Allocation
        ↓
本次LogicalProductionDemand
```

到这里回答：

> **本次到底还需要生产多少净合格数量、从哪个Stage/Operation继续。**

这一阶段：

- 不以旧TaskNo作为数量来源；
- 不为了保护上一版本Task而改变PI数量真相；
- 人工MES工单即使没有TaskNo，只要已有真实报工，也必须进入5号位事实解释。

## 2.2 第二阶段：给本次净需求寻找执行连续性承载

本次 `LogicalProductionDemand` 确定以后，2号位再检查上一ACTIVE Task及其MES工单状态：

```text
当前净需求Q
        ↓
2号位检查上一ACTIVE TaskNo ↔ MESWorkOrderNo
        ↓
识别：
A. APS旧Task执行连续份额
B. 外部MES已执行连续份额（适用时）
C. 尚无执行身份的自由生产份额
        ↓
1号位只对需要排程的份额做有限产能求解
```

正式原则：

> **旧TaskNo只能承接本次已经确定的净需求，不能反过来决定或抬高本次净需求。**

---

# 三、TaskId、TaskNo、MESWorkOrderNo

## 3.1 TaskId

```text
TaskId
= 当前PlanVersion内数据库身份
```

新PlanVersion重新生成TaskId。

## 3.2 TaskNo

```text
TaskNo
= APS生产Task在允许连续的跨版本执行生命周期中的业务身份
```

TaskNo不是PI数量真相，也不是Firm/Frozen类别。

## 3.3 MESWorkOrderNo

```text
MESWorkOrderNo
= MES创建的执行工单身份
```

## 3.4 一对一关系

正式APS下发链始终保证：

```text
一个TaskNo ↔ 一个MESWorkOrderNo
```

因此：

- 同一TaskNo不得重新绑定第二个MESWorkOrderNo；
- 一个新MESWorkOrderNo必须对应新的TaskNo；
- 已经结束的TaskNo/MES工单关系只保留历史追溯，不复活。

---

# 四、Formal TaskNo由谁生成

1号位负责：

> 形成有限产能求解后的 `FinalTask`。

2号位负责：

> 持久化FinalTask，并根据本裁决决定“继承旧TaskNo”还是“生成新TaskNo”。

因此准确口径是：

```text
旧TaskNo不能继续
        ↓
1号位形成新的FinalTask
        ↓
2号位生成新的正式TaskNo
```

1号位不负责正式TaskNo生命周期治理。

---

# 五、MES工单四状态与TaskNo生命周期

MES ODS需标准化至少四类业务状态：

```text
DELETED            已删除
NOT_STARTED        未开工
STARTED_INCOMPLETE 已开工未完成
COMPLETED          已完成
```

具体MES原始状态码可由5号位映射，但业务语义必须一致。

## 5.1 DELETED —— 旧TaskNo执行身份结束

如果上一关系为：

```text
T001 ↔ WO001
```

而本次：

```text
WO001 = DELETED
```

则：

```text
T001旧执行生命周期结束
```

如果本次PI Position / Pegging后仍有净生产需求：

```text
1号位形成新FinalTask
→ 2号位生成新TaskNo
→ MES创建新MESWorkOrderNo
```

不得：

```text
旧T001 ↔ 新MESWorkOrderNo
```

## 5.2 NOT_STARTED —— 不做MES执行身份跨版本继承

MES工单存在但尚未产生真实执行：

```text
WorkOrderStatus = NOT_STARTED
```

本轮裁决：

> **旧TaskNo/MESWorkOrderNo不作为下一版本执行身份继续。**

下一FULL按最新生产真相重新排：

```text
旧TaskNo结束
→ 新FinalTask
→ 新TaskNo
→ 新MESWorkOrderNo
```

MES侧原则上删除/作废旧未开工工单。

如果旧工单因操作异常未删除，MES已有的同PI数量校验可以拒绝新工单创建；创建失败必须通过计划下发中间表或等价机制回到APS：

```text
FAILED + FailureReason
```

该异常不改变本次PI Position数量真相，但必须可见、可追溯。

### 与Firm/Frozen的关系

“未开工所以新TaskNo”只表示：

> MES执行身份不继承。

它不自动解除APS计划围栏。

如果上一Task按本次ScheduleRun的Fence仍属于Firm/Frozen，则新计划仍须遵守对应不可移动约束；只有合法Firm重开等授权场景才允许调整。

## 5.3 STARTED_INCOMPLETE —— 唯一必须保持旧TaskNo的MES状态

如果：

```text
T001 ↔ WO001
WO001 = STARTED_INCOMPLETE
```

则：

```text
TaskNo = T001 保持
MESWorkOrderNo = WO001 保持
```

并且：

- 禁止普通FULL拆掉该TaskNo；
- 禁止与其它TaskNo合并；
- 连续份额以本次净需求为上限；
- 资源和未来时间允许按本裁决重新求解。

## 5.4 COMPLETED —— 执行生命周期结束

```text
WO001 = COMPLETED
→ T001执行生命周期结束
```

已完成事实进入5号位PI Position。

若本次仍存在其它合法净缺口：

```text
新FinalTask
→ 新TaskNo
→ 新MESWorkOrderNo
```

---

# 六、2号位如何从当前净需求中识别旧Task连续份额

对每个已经由5号位确定执行起点的当前 `LogicalProductionDemand` 片段：

```text
PI + StartStage/StartOperation + NetOutputQty
```

2号位再匹配上一ACTIVE中同PI、同当前执行语义的APS关联Task。

只有：

```text
对应MES工单 = STARTED_INCOMPLETE
```

才允许形成旧Task连续份额。

设：

```text
当前该排程片段净需求 = Q
旧MES工单标准化剩余净合格量 = M
```

正常情况下：

```text
连续份额 C = min(Q, M)
自由份额  R = Q - C
```

其中：

```text
C
→ 保留旧TaskNo

R
→ 无旧执行身份
→ 交给1号位正常形成新FinalTask
→ 2号位生成新TaskNo
```

## 6.1 M > Q

如果：

```text
MES RemainingQty > 当前权威净需求
```

不得用旧Task把当前需求抬高到M。

处理：

```text
连续业务份额最多认Q
+
记录Data Issue
```

多出的MES执行量属于执行/数量不一致，不得伪装成正常Demand。

## 6.2 M < Q

例如：

```text
Q = 100
M = 40
```

则：

```text
40 → 旧TaskNo继续
60 → 新自由生产需求
```

这正是跨版本连续性的正常分桶。

---

# 七、MES RemainingQty标准化数量语义

为了避免计划良率重复放大，进入APS业务主链的MES `RemainingQty` 必须解释为：

> **该MES工单尚需完成的净合格目标数量。**

也就是与Task净业务数量同口径。

不得把：

```text
MES剩余加工投入量
```

直接当成：

```text
NetOutputQty
```

再由APS套一次Yield。

如果MES原始字段并非净合格剩余语义，5号位应在防腐层标准化后再交2号位。

---

# 八、人工创建、无TaskNo的MES工单

人工MES工单不强行补造TaskNo，也不追溯到已经结束的旧TaskNo。

核心原则：

> **TaskNo身份可以断，生产事实不能断。**

## 8.1 已有真实报工

无论人工工单是否有TaskNo，其真实报工都必须进入：

```text
5号位
→ PI Position
→ NextOperationContext
```

因此下一FULL的净需求会自然扣除已经真实完成的数量。

## 8.2 人工未开工MES工单

V1治理口径：

> 人工/历史未开工MES工单原则上在新一轮正式下发前删除或作废，APS不为其建立跨版本Task连续性。

如果未及时删除：

- APS仍可按本次最新计划尝试下发；
- MES自身同PI数量校验负责阻止重复/超量创建；
- APS接收 `FAILED + FailureReason`；
- 不因此恢复旧TaskNo关系。

## 8.3 人工已开工未完成MES工单

如果无TaskNo但已经真实开工：

> 它是“外部MES执行事实”，不是APS旧Task连续身份。

为避免APS在同一Run中重复占用数量/能力，2号位应在当前净需求中识别其仍有效执行份额；可使用MESWorkOrderNo作为外部执行引用，不伪造TaskNo。

其作用是：

- 已完成量通过PI Position进入数量真相；
- 尚未完成且仍有效的执行份额不得再次作为新MES工单重复下发；
- 其实际资源事实可作为外部执行占用传给1号位。

如果当前技术阶段暂不能完整识别外部未完成份额，允许由MES创建校验兜底，但必须明确这是V1降级路径，会造成“APS计划生成后MES拒绝”的短时计划/执行不一致，不能把它描述成完全等价于正常闭环。

---

# 九、资源连续性：LastReportResourceCode的最终定位

MES设备资源组允许：

```text
设备A生产并报工
↓
结束本次生产段
↓
设备B再开工
```

但同一MES工单、同一工序不允许A/B同时生产。

因此对 `STARTED_INCOMPLETE` 的APS绑定Task：

```text
第一优先：
LastReportResourceCode

第二优先：
上一ACTIVE中该Task原计划ResourceId

第三及以后：
OperationResourceEligibility / 对应资源组中的其它合法资源
```

只有全部合法资源不可行时才 `UNSCHEDULED`。

## 9.1 LastReportResource不是硬锁

它表示：

> 最后一次已经形成报工事实的资源。

它是最可信的资源连续性首选，但不是“当前实时正在生产资源”。

因此不得突破：

- Resource Eligibility；
- Calendar；
- 其它真正Execution/Frozen/Firm硬约束。

## 9.2 LastReportResource为空

可能存在：

```text
MES状态已开工
但截至数据截止时间尚未形成第一笔报工
```

此时：

```text
第一优先 = 上一ACTIVE原Task资源
```

并登记“当前实际资源未知”类Issue。

这是V1数据能力限制下的明确降级，不伪造当前实际设备。

---

# 十、V1新增红线：同一PI同一工序不得由APS自动多设备并行

正式冻结：

> **APS V1中，同一生产指示（PI）的同一工序，在任一时点最多只能有一个APS自动排定的活跃生产资源。**

1号位不得为了：

- 缩短交期；
- 提高设备利用率；
- 优化拆批；

把同一PI同一Operation的数量自动拆到两台及以上设备同时生产。

## 10.1 允许的情况

允许：

- 同一资源上的强制/优化拆批；
- 不同时间段顺序使用资源；
- 上一资源段结束后切到同资源组另一台合法设备；
- 不同Operation按原Routing / Stage Overlap规则合法重叠。

## 10.2 禁止的情况

禁止APS自动生成：

```text
PI001 / NC
MC01 08:00~12:00 做60
MC03 08:00~12:00 做40
```

## 10.3 现场确需并行

确需两台设备同时生产时：

> 由MES现场人工拆分/建工单处理。

APS不尝试反向恢复人工TaskNo关系。

下一FULL：

```text
MES事实
→ PI Position / NextOperationContext
→ 当前LogicalProductionDemand
```

重新吸收真实执行结果。

## 10.4 外部人工并行事实不否认

该红线限制的是：

> APS V1自动生成计划。

如果现场已经人工形成并行MES工单：

- APS不得否认或删除真实执行事实；
- 5号位仍照实进入PI Position；
- 1号位后续把其作为外部执行事实处理。

---

# 十一、良率、Task业务数量与有限产能数量

## 11.1 Task业务数量

```text
Task.Quantity / NetOutputQty
= 净合格业务数量
```

用于：

- Demand；
- Pegging；
- Allocation；
- Task业务数量；
- 计划员主页面计划数量；
- 跨版本连续净数量。

## 11.2 计划加工数量

```text
PlannedProcessQty
```

由2号位根据：

- 当前净需求；
- PI Position；
- NextOperationContext；
- 尚未完成Stage；
- 版本化计划良率；

反算。

它只作为：

> 1号位有限产能负荷/加工时间输入。

例如：

```text
Task.Quantity = 100
Yield = 95%
PlannedProcessQty ≈ 106
```

计划员看100，1号位按约106占设备能力。

## 11.3 已完成Stage不重复套良率

若当前执行起点已在NC之后：

> 已完成Stage不得重新应用Yield。

## 11.4 PI Supply不因良率放大

ERP PI RemainingQty仍是PI Supply数量真相。

不得因为Yield把：

```text
PI Supply 60
```

改成62/63。

---

# 十二、物料约束与计划良率

原冻结口径继续成立：

> **计划良率不仅影响设备加工投入，也影响相应上游投入和材料需求。**

因此2号位必须同时形成：

```text
各Stage PlannedProcessQty
+
完整Material Quantity-Time约束
```

## 12.1 物料数量不是机械等于Task.PlannedProcessQty

具体Material Required Qty取决于：

- 物料实际消耗点；
- 当前NextOperationContext；
- 尚未完成Stage；
- 计划良率链；
- 已有Supply与Allocation。

因此不得简单规定：

```text
所有物料需求 = 当前Task.PlannedProcessQty
```

## 12.2 1号位职责

1号位只消费2号位提供的：

```text
Material Quantity + AvailableTime
```

进行物料可用时间约束。

1号位不得：

- 自己重新按Yield算材料；
- 自己更换Supply；
- 自己修改Pegging。

## 12.3 BOM ScrapRate

V1仍不因为未来可能存在BOM ScrapRate而再叠加一层材料损耗放大。

计划良率（Planning Yield）与BOM ScrapRate保持不同业务语义。

---

# 十三、Firm/Frozen与MES状态的关系

每次ScheduleRun仍由2号位按冻结Fence参数重新判断：

```text
Frozen
Firm
Movable
```

## 13.1 STARTED_INCOMPLETE

真实执行优先：

```text
Execution > Frozen > Firm
```

TaskNo/MESWorkOrderNo执行身份保持；LastReportResource作为资源连续性首选。

## 13.2 NOT_STARTED / DELETED

MES执行身份不延续，但：

> 并不因此自动解除上一Task仍有效的Firm/Frozen计划约束。

如果本次Fence仍要求固定，则新TaskNo/新MES工单仍须在对应固定位置上重建。

## 13.3 Firm重开

继续采用：

```text
aps.reschedule.manual
+
人工确认
+
审计
```

不新增独立OA主链。

---

# 十四、同TaskNo“不得自阻挡”的准确含义

同TaskNo上一版本自己的旧块不能被作为：

> “其它任务的资源阻挡块”

再次挡住自己。

但不能错误理解成：

> 删除该Task自身应继续遵守的Firm/Frozen/Execution约束。

正确处理：

```text
同TaskNo自身有效约束
→ 转成本Task自己的连续性/锚点约束

其它TaskNo有效约束
→ 外部ResourceBlock
```

由2号位在组装DomainSolveRequest前完成。

---

# 十五、2号位给1号位的最终输入分层

1号位不解析MES、不计算PI Position、不治理TaskNo。

2号位组装：

### A. 硬约束

- Execution；
- Frozen；
- Firm；
- 外Domain ACTIVE资源块；
- 其它不可移动事实。

### B. APS旧Task连续需求

至少表达：

```text
ContinuationKey
NetOutputQty
PlannedProcessQty
NoSplitMerge = true
PreferredResource = LastReportResource（如有）
FallbackResource = OriginalResource
其它Eligible Resources
```

具体字段名可按现有契约最小实现。

### C. 外部MES执行事实

无TaskNo但已真实执行的MES工单，按当前技术能力作为：

- 外部执行份额；
- 资源占用/约束；
- 数量扣减依据；

传入本次运行上下文。

### D. 自由逻辑生产需求

没有执行身份的剩余净需求：

> 由1号位正常形成新FinalTask。

---

# 十六、计划下发中间表业务要求

当前2/5号位倾向：

> 中间表放APS库。

物理技术实现仍由2/5最终确认，但业务至少需要：

```text
TaskNo
MESWorkOrderNo
DispatchStatus
FailureReason
来源PlanVersionId / TaskId
必要计划下发字段
```

状态至少：

```text
PENDING
CREATED
FAILED
```

## 16.1 跨版本继续Task

`STARTED_INCOMPLETE`：

```text
旧TaskNo + 旧MESWorkOrderNo
→ 不创建第二张MES工单
```

## 16.2 新Task

以下场景产生新TaskNo并尝试新MES工单：

- DELETED后仍有需求；
- NOT_STARTED按本裁决重新生成；
- COMPLETED后仍有新的合法缺口；
- 当前净需求中未被旧执行连续份额承接的自由部分。

---

# 十七、最小端到端示例

上一日：

```text
PI001
T001 ↔ WO001
净业务数量100
```

MES实际：

```text
WO001 已开工未完成
已完成40
RemainingQty=60
LastReportResource=MC03
```

第二天：

```text
5号位
→ PI Position / NextOperationContext

2号位
→ 当前LogicalProductionDemand = 80
```

然后才做连续性匹配：

```text
旧WO001可连续 = 60
当前需求 = 80

60 → T001继续
20 → 自由需求
```

2号位：

```text
对60和20分别形成正确NetOutputQty
并按当前未完成Stage计算PlannedProcessQty和Material Quantity-Time
```

1号位：

```text
T001连续60：
首选MC03
→ 原资源
→ 其它合法资源
禁止拆/合身份

新20：
正常排程
但同一PI同一Operation不得与T001在不同设备上时间重叠
```

1号位返回：

```text
T001对应连续FinalTask
+
新20对应FinalTask
```

2号位：

```text
连续FinalTask → 继承T001
新FinalTask   → 新TaskNo T002
```

---

# 十八、人工MES场景示例

昨天：

```text
T001 ↔ WO001
```

现场：

```text
WO001删除
人工创建WO101、WO102
均无TaskNo
```

WO101已报工20。

第二天：

```text
所有MES真实报工
→ 5号位PI Position
→ 2号位得到本次真正净需求
```

此时：

```text
WO001=DELETED
→ T001不再连续
```

所以：

> 不能为了保持T001而忽略人工MES事实。

如果WO101仍为STARTED_INCOMPLETE：

- 不恢复T001；
- 以外部MES执行事实处理；
- 防止同一份正在执行数量再次被APS当作全新MES工单下发。

如果WO102仍未开工：

- 原则上删除/作废；
- APS不建立TaskNo连续；
- 如残留导致新工单创建冲突，由MES校验拒绝并回写FAILED。

---

# 十九、V1明确不做

本裁决不新增：

- MES全部报工历史复制平台；
- 实时开放执行段平台；
- 多资源并行自动拆分；
- Task跨版本相似度匹配；
- 第二套Fence版本平台；
- IsFirm / IsFrozen永久字段；
- ExecutionLock平台；
- 1号位自行解析MES；
- 1号位自行重算Yield；
- 1号位自行计算材料需求；
- BOM ScrapRate损耗链。

---

# 二十、最终论证结论

本版经过以下反例重新验证：

1. 旧TaskNo对应MES工单被删除；
2. 旧MES工单未开工；
3. 旧MES工单已开工未完成且本次需求大于/小于MES RemainingQty；
4. 人工删除APS关联工单后创建无TaskNo MES工单；
5. 现场在资源组内将实际设备从APS原设备切换为另一设备；
6. 同一PI同一Operation希望两台设备并行；
7. Yield同时影响产能与材料；
8. 已完成Stage后跨版本重算；
9. 同TaskNo旧资源块自阻挡；
10. Firm/Frozen与MES状态冲突。

结论：

> **“PI Position/当前净需求先行，TaskNo连续性后置匹配”的架构成立，并且比“先保护旧Task再推数量”更稳定。**

其关键闭环是：

```text
事实真相
→ 当前净需求
→ 执行连续性分桶
→ 有限产能
→ 新/旧TaskNo
→ MES下发
→ 下一次事实回流
```

其中TaskNo身份链允许因人工MES操作而断开，但PI/MES生产事实链不能断；因此系统不会为了追求TaskNo稳定而扭曲真实生产数量。

---

# 二十一、最终一句话冻结口径

> **APS V1每次FULL先由5号位基于全部ERP/MES真实事实计算PI Position和NextOperationContext，2号位据此重新Pegging并确定本次LogicalProductionDemand净数量；随后2号位才检查上一ACTIVE Task对应MES状态，只有已开工未完成的APS关联MES工单允许按本次净需求范围继承原TaskNo和MESWorkOrderNo，其余已删除、未开工、已完成状态均结束旧Task执行身份，剩余净需求由1号位形成新FinalTask、2号位生成新TaskNo。已开工未完成Task以LastReportResource为资源连续性首选，但允许在同资源组/资格范围内顺序换机；APS V1禁止自动把同一PI同一工序拆到多台设备并行生产，确需并行由MES人工处理并在下一FULL通过事实链重新吸收。Task.Quantity始终是净业务数量，PlannedProcessQty和材料Quantity-Time由2号位基于尚未完成Stage及计划良率反算，1号位只做有限产能求解。**
