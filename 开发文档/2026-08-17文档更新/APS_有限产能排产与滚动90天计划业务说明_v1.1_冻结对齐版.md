# APS 有限产能排产与滚动90天计划业务说明 v1.1（冻结对齐版）

**适用版本**：APS V1  
**文档性质**：业务与算法口径专项说明  
**冻结对齐日期**：2026-08-12  
**主要读者**：0号位、1号位、2号位、3号位，兼供4号位和5号位理解上下游边界  
**上位基线**：《APS V1最终全部流程与业务基线_v1.0_20260812》

> **冻结声明**：本文是最终业务冻结基线的有限产能专项解释文档，不拥有独立修改冻结业务的权力。本文只允许补充说明、案例、边界和与最终基线的一致性修正。若与最终业务冻结基线冲突，以最终业务冻结基线为准。任何业务口径变更必须明确提出“重新打开已冻结决策”，经0号位重新裁决后形成新版本。禁止因为代码现状、旧文档、实现方便、未来扩展或“更优雅”而静默修改本文已冻结业务。

## v1.1相对v1.0的对齐范围

本次不是重新设计，只回写v1.0之后已经冻结的口径：

1. 白天单Domain Candidate遇到跨Domain共享资源时，其它Domain ACTIVE占用作为不可移动阻挡块；
2. 无正式PO/VMI时允许Planning-only Purchase Placeholder，但不构成确定性CTP承诺；
3. 跨Domain Quantity-Time必须保留分段；
4. FAILED人工恢复新建ScheduleRun；
5. Firm/Frozen跨版本由上一ACTIVE转换为本次不可移动锚点，不建设FrozenZoneSnapshot平台；
6. Candidate正式采用只保留最小人工授权确认，不建设完整审批主链；
7. 强化“绝不建设有限物流Task体系”，删除任何“未来再把物流放进1号位Solver”的暗示；
8. 补充正式MES发布资格边界。

其它v1.0主体业务不重新打开。

---

## 1. 为什么需要单独说明有限产能排产

有限产能排产（Finite Capacity Scheduling）不是把预生成Task简单塞进日历，而是把Pegging已经确认的业务数量关系，结合完整工艺路线、资源资格、日历、物料AvailableTime、Firm/Frozen/Lock约束，在真实有限资源上形成可执行时间计划。

必须回答：

1. 哪些逻辑生产需求形成哪些最终Operation Task；
2. 每个Task在哪个合法Resource上、何时开始、何时结束；
3. 多Demand竞争资源时如何排序；
4. 物料何时齐套；
5. 是否能按客户交期完成；
6. 不能按期时真正根因是什么；
7. 白天插单影响了哪些既有计划。

> **1号位拥有时间和有限资源真相；2号位拥有Pegging业务数量真相。**

---

## 2. V1总体原则

### 2.1 90天只有一套正式有限产能计划

APS V1采用滚动90天统一有限产能计划。

90天内活跃Demand原则上全部：

- 完整BOM；
- 完整Pegging；
- 计划良率；
- 进入同一套1号位有限产能求解器。

明确简化为Stage LeadTime的工艺继续按LeadTime处理，不因为远期再造另一套日产粗排算法。

### 2.2 近期和远期不是两套算法

可以优化深度不同，但资源与时间语义必须一致。

不能：

- 近期设备级；
- 远期日产量级；
- 再把结果拼起来。

### 2.3 统一Solver优先

V1以约10万Task、夜间FULL约15分钟左右收敛作为工程目标。

只有真实压测和正常优化后仍不达标，才允许重新评估远期降级策略；**不能预建第二套Solver。**

---

## 3. 角色边界

### 3.1 1号位

负责：

- FinalTask；
- 最终Resource；
- Start/End；
- 最终拆批/合批；
- 工序物理依赖；
- 物料数量—时间约束；
- Firm/Frozen/Lock执行；
- 动态瓶颈；
- 正排/倒排/混合；
- Candidate局部影响传播；
- Explanation Facts。

1号位纯内存求解，不直接写APS数据库，不重新Pegging Supply。

### 3.2 2号位

进入1号位前完成：

- Pegging数量闭合；
- Supply选择；
- Demand Protection/Lock；
- 计划良率反算；
- 逻辑生产需求；
- Routing/资源资格/Calendar/AvailableTime组装；
- Candidate变化种子；
- 最终结果统一持久化。

2号位不预先锁死最终Operation Task、设备和时间槽。

### 3.3 3号位

治理并冻结：

- 正倒排策略；
- 瓶颈策略；
- 拆合批参数；
- Stage重叠/转运；
- Setup；
- 资源偏好；
- 计划良率；
- 履约确保率；
- 次级优化参数。

不建设任意Command脚本链。

### 3.4 4号位和5号位

- 4号位负责CTP、插单、甘特图、Candidate比较、影响展示与人工确认；
- 5号位负责复杂事实，不做有限产能求解和最终排程ReasonCode。

---

## 4. 2号位交给1号位的是完整求解问题，不是FinalTask

正式输入包括九类：

### 4.1 求解边界

- ScheduleRun / PlanVersion；
- DomainKey；
- FULL / Candidate；
- DataCutoffTime；
- 90天Horizon；
- Base ACTIVE；
- 可移动/不可移动边界。

### 4.2 生产需求与业务优先关系

至少包括：

- Material；
- Factory；
- ProductFamily；
- OrderType；
- FromStage/ToStage；
- NetRequiredQty；
- DueDate；
- 业务优先层级；
- Demand Protection / Lock；
- Allocation/Order稳定血缘。

### 4.3 Net Required Qty与Planned Process Qty

必须分开：

- **Net Required Qty**：交付给需求的合格净数量；
- **Planned Process Qty**：考虑计划良率后实际预计投入加工的数量。

### 4.4 Allocation数量血缘

1号位需要知道FinalTask最终为哪些Allocation服务，以便返回多对多TaskShare。

### 4.5 Routing候选图

包括：

- Stage；
- Operation；
- Dependency；
- RouteCode / PathId；
- Stage LeadTime；
- 重叠/转运关系。

### 4.6 资源资格与日历

包括Resource、OperationResourceEligibility、设备/部门日历、已有占用等。

### 4.7 物料Quantity-Time约束

所有已选Supply转成：

> **Quantity + AvailableTime。**

来源包括库存、PI、自制上游、采购、VMI、已到厂未入库、跨厂、跨Domain等。

1号位不能自行替换2号位已经Pegging确认的Supply。

### 4.8 排程策略包

正倒排、瓶颈、拆批、Stage重叠、Setup、资源偏好、局部修复、履约目标等。

### 4.9 执行与不可移动事实

包括：

- 已完成；
- 已开工；
- 已下发且不可移动；
- Firm；
- Frozen；
- Demand Protection / Lock带来的不可移动关系；
- 固定Resource/Start/End等。

---

## 5. FinalTask由1号位形成

### 5.1 2号位不能提前生成最终Operation Task

否则1号位失去：

- 是否拆批；
- 拆几批；
- 合批；
- 替代资源；
- 等价路径；
- 时间槽联合优化。

正式口径：

> **2号位形成逻辑生产需求；1号位形成FinalTask。**

### 5.2 不同Demand可以合并为一个FinalTask

在工艺、资源、交期、Lock/Firm/Frozen都允许时可以合并。

必须保留Task→多个Allocation份额的数量映射。

### 5.3 Task与Demand不是一对一

必须支持：

- 一个Allocation→多个FinalTask；
- 一个FinalTask→多个Allocation份额。

---

## 6. Routing、等价路径与资源选择

2号位负责“合法候选”，1号位负责“实际选择”。

V1现阶段如果只有DEFAULT Path1，则不为了未来先实现复杂等价Route全局搜索。

只有等价路径不改变BOM、Material、Factory、资格和产出定义时，1号位才可在求解内直接选择；否则回到2号位业务层决定。

---

## 7. 计划良率（Planning Yield）

### 7.1 计划良率必须进入V1

客户需要100件合格品，某Stage计划良率98%，计划加工量可能为102～103件。

设备能力按PlannedProcessQty占用，不能只按100件。

### 7.2 2号位反算，1号位占能力

计划良率改变上游投入和材料需求，因此由2号位反算；1号位只按PlannedProcessQty排产。

### 7.3 Allocation仍按净合格数量闭合

Allocation=100，PlannedProcessQty=103时，多出的3是计划损失，不属于客户Demand。

### 7.4 已完成Stage不重复应用良率

PI当前位置已经过MACH，则不能重新对MACH应用良率。

### 7.5 ERP PI不能因为计划良率膨胀成更大Supply

PI剩余60仍只有60 Supply。若预期损耗导致还需额外投入，形成新增生产需求，而不是把PI改成62。

---

## 8. 物料Quantity-Time约束

### 8.1 Purchased与Self-made统一为AvailableTime约束

下游真正关心的是：

> 某数量什么时候可用。

### 8.2 采购件不生成制造Task

采购、VMI、库存只提供Supply和AvailableTime，不生成制造Task。

### 8.3 Quantity-Time可分段消费

如果200件库存当前可用、100件PO 16号可用，且下游允许拆批，1号位可以利用分段AvailableTime形成不同生产批次。

### 8.4 1号位不能自行更换Supply

更换PO/PI/库存等会改变Allocation，必须回2号位Pegging。

### 8.5 Planning-only Purchase Placeholder

若采购件没有库存、PO、VMI、已到厂未入库，2号位可以提供Planning-only Purchase Placeholder：

- Quantity=缺口；
- AvailableTime按冻结DefaultLT估算；
- `ESTIMATED / NOT_COMMITTED`。

1号位可以用它继续完成90天产能时间推演。

但是：

> **依赖该占位得到的日期只能是估算，不是确定性CTP承诺。**

该占位不生成Task、不生成PO、不下发ERP/MES。

---

## 9. 跨厂与跨Domain时间处理

### 9.1 有限物流Task永久不进入当前冻结V1设计

跨厂运输/检验/转运统一通过确定性LeadTime形成下游AvailableTime。

> **当前冻结方向绝不建设ShippingTask、车辆、月台、班次有限资源或物流Solver。**

如果未来业务真的要求改变这一点，必须明确重新打开冻结决策，不能在技术文档中顺手预留成1号位未来能力。

### 9.2 跨Domain仍传Quantity + AvailableTime

上游Domain最终产出Task完成，加必要转运LeadTime，形成下游供给时间。

不建设VirtualInventoryBalance状态平台。

### 9.3 Quantity-Time必须保留分段

若上游实际：

- 40件15号可用；
- 60件17号可用；

下游必须收到两个切片，不能汇总成100件17号。

### 9.4 跨DomainLeadTime不统一硬编码2天

使用真实工厂/Stage已有LeadTime口径。

---

## 10. 动态瓶颈

默认使用Load/Capacity动态识别，可由3号位通过AUTO / PREFER_ANCHOR / FORCE_ANCHOR / NOT_ANCHOR等有限策略覆盖。

V1不建设复杂全局数学多瓶颈最优器。

---

## 11. 正排、倒排与混合

正排适合最早承诺、物料刚可用等场景；倒排适合靠近DueDate减少WIP。

V1在同一个Solver内混合使用，不是两套Solver，也不是全正排/全倒排二选一。

---

## 12. Solver固定五阶段

### A. 建问题与硬约束

Routing、Resource Eligibility、Material AvailableTime、Firm/Frozen/Lock、强制拆批、初步瓶颈。

### B. 初始有限产能计划

联合考虑优先关系、瓶颈锚点、正倒排、资源、批次、时间、Setup。

### C. 可行性与延期诊断

检查资源、物料、工艺依赖、Firm/Frozen/Lock和交期。

### D. 有界局部修复

尝试替代资源、有限拆批、前后移动、多瓶颈协调、Setup邻域优化等。

### E. Gap Compaction与最终评价

形成FinalTask、TaskShare、Task-to-Task物理依赖、Explanation Facts和KPI。

V1不是任意可配置Command链。

---

## 13. 不是“订单A整单排完再订单B”

1号位面对整个Domain的Demand、Routing、资源、物料和依赖。

2号位业务排序表达“冲突时谁优先保护”，并不意味着高优先订单所有Task全部完成后低优先订单才能开始。

如果高优先Task因物料未到暂时不可生产，低优先Task在不损害更高层履约的前提下可以先占空闲资源。

---

## 14. Setup与换型

Setup是资源序列关系。

V1采用局部邻域启发式，不做全局旅行商式优化。

Setup/WIP/利用率属于次级优化，不能为了少换型而让高优先客户订单明显延期。

---

## 15. Stage重叠、转运与批次

### 15.1 Stage重叠

业务允许时，下游可以在满足数量门槛后提前开始。

### 15.2 Stage间转运

非有限物流资源的Stage转运直接增加LeadTime。

### 15.3 强制拆批与优化拆批分开

- 物理强制拆批在阶段A执行；
- 优化拆批在阶段B/D只尝试少量候选。

不做无限组合。

---

## 16. 目标层级

### 第一层：物理与业务硬约束

资源冲突、Dependency、AvailableTime、Eligibility、Firm/Frozen、Lock、执行事实、数量闭合绝不能违反。

### 第二层：履约优先关系

Demand Protection和高优先客户需求优先。

### 第三层：客户承诺确保率目标

如果存在可行方案达到冻结目标，不得为了Setup/利用率选择更差履约方案。

物理上达不到目标时返回最佳可行结果，不把“无法达标”当Solver失败。

### 第四层：次级优化

总延期、WIP、Setup、利用率、计划稳定性等。

---

## 17. 客户按期率

核心KPI建议使用：

> **客户订单整单按时进入目标ZP/BP的比例。**

数量按期率可作为辅助，但不能混成一个指标。

---

## 18. 排程终点

APS V1客户生产交期判断终点：

> **最终生产/装配完成并进入目标ZP/BP。**

客户外部运输不进入有限产能生产Solver。

如果业务只需在生产完成后追加固定物流时间，可作为生产之外的确定性LeadTime解释；**不能因此生成有限物流Task。**

---

## 19. 白天Candidate为什么不无条件全Domain重排

正常Candidate采用：

> **Base ACTIVE + 变化种子 + 动态影响传播 + 最小扰动局部修复。**

影响过大时自动升级为同一Solver的单Domain完整重排。

---

## 20. 变化种子由2号位确定

2号位比较Candidate Pegging和Base ACTIVE，形成：

- 新增生产需求；
- 减少需求；
- 数量变化；
- 其它真实逻辑生产变化。

2号位不预先计算完整产能影响范围。

---

## 21. 影响范围由1号位动态传播

沿：

1. 工艺Dependency；
2. 实际物料数量—时间关系；
3. 同一资源时间序列；
4. Setup直接邻居；

传播。

无真实变化就停止。

不按固定产品族、固定天数、固定订单数裁切。

---

## 22. 最小扰动优先

先尝试：

1. 原Resource；
2. 原附近时间；
3. 不移动其它Task的空档；
4. 少量合法替代Resource；
5. 最后才挤动普通低优先Task或做优化拆批。

计划稳定性不能阻止正确业务优先关系。

---

## 23. 资源传播

变化Task真正改变Start/End/Resource后才继续传播到资源邻居。

Setup插入/移除至少检查原前驱、原后继、新前驱、新后继。

---

## 24. 物料传播按真实份额

利用AllocationTaskShare和真实物料消费关系传播。

不能因为“MaterialCode相同”就把所有使用该Material的订单全部加入影响范围。

---

## 25. 防振荡与局部修复预算

V1可在1号位内部使用：

- Task revision stamp；
- 单Task修复次数；
- 全局修复轮次；
- 时间预算；
- 受影响Task比例。

这是Solver技术参数，不是PMC业务规则。

经验初值可沿用v1.0：

- 正常Candidate目标60秒；
- 软预算90秒；
- 局部硬预算180秒；
- 影响比例参考30%；
- 单Task修复5次；
- 全局轮次10；
- 替代资源初筛5；
- 优化拆批候选最多3。

这些值后续只按压测调优，不改变业务模型。

---

## 26. 局部模式兜底：单Domain完整重排

局部影响过大、不收敛或超过预算时，自动升级当前单Domain全部可移动Task重排。

仍固定：

- 已执行；
- Firm/Frozen；
- Demand Protection；
- 不可逆Lock。

仍使用同一个Solver。

---

## 27. Scope字段不能硬截断真实影响

旧接口里的MaxImpactedOrders、PlanHorizonStart/End等只能作为：

- 发起范围；
- 提示/警戒阈值；

不能作为“超过就不算”的物理截止线。

真实影响必须传播到闭合或触发单Domain完整重排。

---

## 28. 白天共享资源：其它Domain ACTIVE是不可移动阻挡块

如果当前Candidate Domain与其它Domain共享同一设备/资源：

> **V1不允许为了本Domain急单去挤动其它Domain当前ACTIVE计划。**

其它Domain ACTIVE占用在当前Candidate Resource Calendar中作为不可移动Block。

这样保证：

- Candidate仍严格单Domain；
- 不产生跨Domain产能影响传播；
- 不需要共享资源配额、借用、虚拟子日历平台。

这会使白天CTP在少数场景偏保守，但属于V1明确选择。

夜间FULL再根据全局实际共享资源情况形成新的正式计划。

---

## 29. 白天跨Domain插单CTP

一个Candidate仍严格一个Domain。

如果 `C → B → A`：

1. C WHATIF Candidate；
2. C的Quantity-Time传B；
3. B WHATIF Candidate；
4. B的Quantity-Time传A；
5. A WHATIF Candidate；
6. 汇总成一次客户CTP答案。

不建设MultiDomain Candidate和多域原子激活组。

---

## 30. 夜间跨Domain失败与人工重算

如果上游B失败：

- B新PlanVersion=FAILED；
- 依赖B的下游A本次不发布新ACTIVE；
- A保留旧ACTIVE；
- 无关Domain继续发布。

人工恢复：

- 原FAILED历史保留；
- 新建ScheduleRun；
- 自动识别必要失败上游和被阻断下游；
- 只重算必要依赖链；
- 按拓扑顺序执行。

不建设独立Retry Workflow平台。

---

## 31. Candidate采用与审批边界

WHATIF Candidate永远不能自动成为正式计划。

正式采用只要求最小人工授权确认和审计：

- 谁确认；
- 何时确认；
- 采用哪个Candidate。

如果2号位当前代码没有审批逻辑，V1不新增完整OA/BPM审批平台。

如果已有审批体系过重且阻碍V1，可简化或旁路，不把审批变成排程主链依赖。

---

## 32. Firm/Frozen跨版本保持

V1停止建设完整FrozenZoneSnapshot平台，但Firm/Frozen业务语义必须继续存在。

夜间新PlanVersion生成新TaskId时：

> 2号位从上一ACTIVE读取仍有效的Firm/Frozen执行/业务事实，转换为本次1号位不可移动锚点。

新版本仍生成新TaskId。

MES真实执行事实继续由ExecutionLock等现实约束承接。

不依赖FrozenZoneSnapshot重新建立一套第二状态平台。

---

## 33. Explanation Facts由1号位原生产出

1号位在求解过程中已经知道真正约束，因此应产生结构化Explanation Fact，包括：

- TargetTime / ActualTime；
- ConstraintType；
- Material / Resource / Blocking Task；
- ImpactHours；
- Direct Cause；
- Root Cause；
- Evidence。

V1不保存完整Solver搜索轨迹。

### 33.1 根因不能机械累加所有等待

只沿最终有效关键路径识别真正影响最终完工的约束。

### 33.2 DUE_DATE_RISK只是结果

必须继续落到MATERIAL_SHORTAGE、RESOURCE_CAPACITY_WAIT、PRECEDENCE_WAIT、FROZEN_ZONE_LOCK、LOGISTICS_DELAY等真实根因。

LOGISTICS_DELAY可来自外部运输事实延误，但不意味着物流进入有限产能Task体系。

---

## 34. 插单结果必须服务销售与PMC

一次Candidate至少回答：

- 是否按期；
- 最早日期；
- 是否依赖ESTIMATED采购占位；
- 影响订单数；
- 哪些仍按期；
- 哪些延期；
- 是否破坏Demand Protection；
- 主要瓶颈；
- 主要根因。

依赖Planning-only Purchase Placeholder时必须显著标记：

> **估算日期，非确定性承诺。**

---

## 35. 正常案例

客户订单100件，需要自制C100、采购P100。

Pegging：

- C已有PI60，新增40；
- P库存60，PO40在8月12日可用。

2号位反算计划良率、组装Quantity-Time约束；1号位按Routing、Eligibility、Calendar和PlannedProcessQty形成FinalTask并判断交期。

---

## 36. 白天插单案例

A受保护60、B普通100、PI-C01剩余140。

急单U70进入：

- A60不动；
- U获得70；
- B只剩10，新增90生产缺口。

1号位从Base ACTIVE做局部影响传播；如果共享设备上还有其它Domain ACTIVE，则那些占用只作为不可移动阻挡块，不被本次Candidate挤走。

---

## 37. 跨Domain CTP案例

A最终装配依赖B电机，B又依赖C轴承。

后台C→B→A三个单Domain Candidate依次计算。

如果C分40件15号、60件17号完成，B应收到两个Quantity-Time切片，而不是100件17号单点供给。

---

## 38. 延期解释案例

SO-A要求8月18日18:00：

- 自制C 8月15日12:00齐；
- 采购P最后100件8月16日08:00可用；
- ASSY唯一合法资源被受保护订单占用到8月17日12:00；
- 最终8月19日10:00完成。

最终解释应突出：

1. 采购P决定最早物料齐套；
2. 齐套后ASSY资源又被受保护任务占用；
3. 最终晚16小时。

无关的历史等待不能机械叠加为主根因。

---

## 39. 90天计划的周/月/季度视图

周、月、自然季度只是同一90天FinalTask结果的聚合视图。

自然季度只看90天窗口与季度区间交集。

不另跑周排/月排/季度排；年度详细排程不进入V1。

---

## 40. 90天计划与采购参考需用日期

采购件参考Required Date应来自真实消费Task计划开始时间，而不是简单用客户DueDate减固定LeadTime。

V1只提供采购计划参考，不自动创建正式采购单。

---

## 41. 异常与失败

业务数据异常在5号位/2号位按规则先保守降级后再进入1号位。

有限产能硬错误，例如：

- 非法Resource；
- 无法解释的资源冲突；
- TaskShare数量不闭合；
- 跨版本非法引用；
- 工艺依赖被破坏；

必须使Domain失败。

> **无法按客户DueDate完成不是Solver失败，而是正常业务结果。**

---

## 42. 正式结果输出与闭合

1号位至少输出：

1. FinalTask；
2. Allocation↔FinalTask数量份额；
3. Task↔Task物理依赖；
4. Explanation Facts；
5. 求解状态和关键KPI。

满足：

`AllocationQty = Σ AllocationTaskShare中的预计合格产出份额`

PlannedProcessQty不进入Allocation净需求闭合。

订单完成时间取满足该订单全部必要数量份额后的最晚完成时间。

---

## 43. MES发布资格

只有正式、可执行、业务身份完整的Task才能下发MES。

以下不得绕过正式发布资格：

- Candidate Task；
- 无PI号虚拟占位Task；
- UNLOCATED保守规划Task；
- 其它仅用于估算的规划对象。

Planning-only Purchase Placeholder本身不是Task，更不得下发MES。

---

## 44. V1明确不建设

V1有限产能主链不建设：

- 第二套远期粗排Solver；
- 任意Command链；
- 动态脚本/DSL算法平台；
- MultiDomain Candidate；
- 独立LocalSolver；
- 持久化Impact Graph；
- SolverTrace全搜索历史；
- 因果图平台；
- 有限物流Task体系；
- 车辆/月台/班次资源模型；
- 跨Domain共享资源配额/借用平台；
- 通用Capacity Reservation状态机；
- 无限Route搜索；
- 数学全局多瓶颈最优器；
- 无限拆批组合；
- 巨型加权总目标函数；
- 周/月/季度/年度独立排程引擎；
- FrozenZoneSnapshot状态平台；
- 完整BPM审批作为排程主链依赖。

---

## 45. V1实现深度与最终冻结红线

V1实现“完整能力边界 + 简单可靠启发式”：

- 动态瓶颈：Load/Capacity；
- 多瓶颈：有序锚点+局部协调；
- 资源选择：有限候选；
- 拆批：少量候选；
- Setup：邻域启发式；
- 正倒排：组合时间槽搜索；
- Gap Compaction：简单有效版本；
- Candidate：变化传播+有界局部修复；
- 极端Candidate：单Domain全排兜底。

最终冻结：

1. 90天一套统一有限产能计划。  
2. 2号位不提前生成FinalTask。  
3. FinalTask/Resource/Start/End/拆合批由1号位决定。  
4. Net Required Qty与Planned Process Qty分开。  
5. Supply统一以Quantity + AvailableTime约束1号位。  
6. Planning-only Purchase Placeholder只用于估算，不构成确定性CTP承诺。  
7. 1号位不得自行替换2号位已选Supply。  
8. 动态瓶颈为默认，正倒排在同一Solver内混合。  
9. 履约和Demand Protection高于Setup/WIP/利用率。  
10. Candidate按变化种子动态传播，不按固定天数/订单数硬截断。  
11. 局部不稳定时用同一Solver升级单Domain完整重排。  
12. 白天其它Domain ACTIVE共享资源占用是不可移动Block。  
13. V1不建设跨Domain资源配额、借用、影响传播平台。  
14. 跨Domain CTP由多个单Domain Candidate串行完成。  
15. 跨Domain Quantity-Time保留分段。  
16. 上游Domain失败时依赖下游不发布；FAILED人工恢复新建ScheduleRun。  
17. WHATIF不自动激活，正式采用只需最小人工授权确认。  
18. Firm/Frozen跨版本转成新Run不可移动锚点，不建设FrozenZoneSnapshot平台。  
19. V1绝不建设有限物流Task体系。  
20. 无法按期是正常业务结果，必须返回最佳可行日期和根因。  
21. Explanation由1号位原生产出。  
22. Candidate、无PI虚拟Task、UNLOCATED规划Task不得下发MES。  
23. V1采用简单确定可压测的启发式，不为理论最优建设通用优化平台。

---

## 46. 一句话理解

> **2号位先把“做什么、为谁做、多少、物料什么时候可用”算清楚；1号位再在90天真实有限资源上决定“在哪台设备、什么时候做、如何拆合批、最终什么时候完成”。白天插单从变化点动态传播，能局部就局部，影响过大就单Domain全排；跨Domain按多个单Domain Candidate串行评估；最终向销售和PMC给出可承诺日期、影响范围和真实根因。**

---

**状态**：v1.1 冻结对齐版。后续只接受符合性修正，不接受开放式业务再设计。
