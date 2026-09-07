# APS V1 1号位有限产能排程开发实施包（PI Position执行起点上下文冻结对齐版）

**版本**：v1.2  
**原冻结日期**：2026-08-14  
**引用治理对齐日期**：2026-09-06（北京时间）  
**适用对象**：1号位及其开发AI  
**文档性质**：从零开发实施说明  
**当前上位依据**：APS V1最终业务基线v1.4 + Pegging v1.3 + 有限产能 v1.3 + Domain专项裁决v1.0 + 集成接口设计v1.30 + 核心排产全流程走查V3.20。原《关键接口冻结》继续作为历史追溯，不再作为独立当前基线。

> **本版专项口径**：1号位继续只负责有限产能与时间/资源真相。本轮新增的`NextOperationContext`由5号位基于MES执行事实、Routing与PI Position解释，2号位在Pegging数量关系确定后组装进`LogicalProductionDemand / DomainSolveRequest`；1号位只消费已经确定的`StartStageCode / StartOperationCode`或等价执行起点上下文，不重新读取MES、不重新判断PI做到哪里。

---


> **治理说明**：本次只更新上位文档引用并补充Domain专项当前解释，不改变1号位既有职责、算法边界或接口。1号位继续只消费单Domain `DomainSolveRequest`，不负责排程域定义（DomainDefinition）或实际归域。

# 一、1号位唯一核心职责

1号位负责：

> **把2号位已经完成Pegging后的“逻辑生产需求”转换成真实有限产能下可执行的FinalTask。**

1号位拥有的真实权威是：

- FinalTask；
- Resource选择；
- PlannedStartTime / PlannedEndTime；
- 工序先后依赖；
- 有限产能资源互斥；
- 拆批/合批；
- Setup邻接；
- Forward / Backward / Mixed；
- Stage overlap / transfer batch；
- Candidate局部影响传播；
- Task物理依赖；
- ScheduleExplanationFacts；
- Unscheduled / Unfulfilled结果。

1号位**不负责**：

- Demand排序；
- Pegging；
- Supply选择；
- Inventory/PO/VMI扣减；
- PI Position解释；
- MES原始执行事实解释；
- NextOperationContext推导；
- Demand Protection业务触发判定；
- ScheduleRun/PlanVersion数据库生命周期；
- 数据库持久化；
- MES下发；
- 采购ETA推导；
- 跨Domain MultiDomain Candidate。

---

# 二、开发原则

## 2.1 只开发一套有限产能Solver

90天计划使用：

> **同一套有限产能语义、同一个Solver。**

允许近端和远端：

- 搜索深度不同；
- 修复尝试次数不同；
- 候选资源数量不同；

但不允许：

- 近端详细Solver；
- 远端ROUGH_CUT第二套Solver；
- 两套不同容量真相。

只有未来真实性能测试证明90天无法完成，才重新裁决是否需要远期降级；V1现在不预建第二Solver。

---

## 2.2 纯内存计算

1号位：

- 接收2号位完整`DomainSolveRequest`；
- 内存求解；
- 返回`DomainSolveResult`；
- 不直接访问/修改APS数据库。

允许为算法性能使用内部内存索引结构，但不得形成新的数据库事实源。

---

## 2.3 不重复业务判断

2号位传来的：

- DemandSequence；
- Allocation；
- Lock；
- LogicalProductionDemand；
- Material Quantity-Time；
- PI Position / NextOperationContext解析后的`StartStageCode`；
- 必要时的`StartOperationCode`；
- UNLOCATED保守执行起点；

均视为业务输入事实。

1号位不得重新：

- 对订单排序；
- 换Supply；
- 修改Allocation；
- 判断哪个PI给哪个订单；
- 读取MES原始报工或进度表；
- 根据MES重新判断当前工序/下一工序；
- 把Operation重新变成Pegging层级。

---

# 三、冻结入口：IFiniteCapacityScheduler

优先使用已有方向：

```csharp
public interface IFiniteCapacityScheduler
{
    Task<DomainSolveResult> SolveAsync(
        DomainSolveRequest request,
        CancellationToken cancellationToken);
}
```

如果2号位当前接口签名已有轻微差异，以“最小兼容修改”为原则，不新建第二套正式Solver接口。

---

# 四、DomainSolveRequest必须支持的输入

## 4.1 RunBoundary

- ScheduleRunId
- PlanVersionId
- DomainKey
- DataCutoffTime
- HorizonStart
- HorizonEnd
- RunType
- Candidate/Base标识（适用时）

---

## 4.2 LogicalProductionDemands

每条至少：

- AllocationSequence
- DemandKey
- OrderId（可空）
- MaterialId
- FactoryId
- StartStageCode
- StartOperationCode（必要时）
- NetOutputQty
- PlannedProcessQty
- RequiredAvailableTime
- DemandSequence
- ProductionInstructionNo（可空）
- IsUnlocated

### 语义

`NetOutputQty`：

> 需要满足Demand的净合格产出。

`PlannedProcessQty`：

> 计算资源能力时使用的计划加工量。

1号位不能用NetOutputQty代替PlannedProcessQty算加工时长，也不能反过来用PlannedProcessQty修改Demand满足数量。

`StartStageCode`：

> 表示该逻辑生产需求从哪个大工艺继续进入排程。

`StartOperationCode`：

> 当执行事实已经能精确到小工序时，表示1号位应从Routing中的哪个Operation开始展开后续工序。

若`StartOperationCode`存在：

- 该Operation之前已经被执行事实证明完成的工序不得再次生成Task；
- 1号位不得回到Routing首工序重新排；
- 1号位不得根据资源负荷或交期自行把StartOperation前移/后移。

若仅提供StartStage：

- 按该Stage内既有合法Routing规则展开；
- 不自行读取MES补猜具体Operation。

---

## 4.3 AllocationLineage

至少保留：

- AllocationSequence
- DemandKey
- SupplyType
- SupplyKey
- Qty
- AvailableTime

用途：

- FinalTask回溯；
- AllocationTaskShare；
- Explanation；
- Candidate比较。

---

## 4.4 RoutingNetwork

至少包括：

- RoutingOperation
- RoutingDependency
- RouteCode / PathId
- ProductionDepartment
- StageCode
- OperationCode
- 工序标准时间/批量参数
- Stage边界信息

### 路径选择边界

如果V1同一业务只允许一条默认路线：

> 直接按默认路线。

不要为了未来扩展建立复杂多路径全局优化平台。

只有业务已经明确存在等价替代路线时，才在合法候选中选择。

### 执行起点裁剪规则

1号位构建Routing求解子图时必须先应用执行起点：

```text
完整Routing
↓
定位StartStage / StartOperation
↓
裁掉已完成前序工序
↓
只对剩余合法Routing进行有限产能求解
```

这只是求解输入裁剪，不是Operation级Pegging。

同一ProductionInstruction可以因为真实执行进度不同，被2号位拆成多个LogicalProductionDemand排程片段；1号位按各片段自己的执行起点求解，但不得因此创建新的Supply身份。

---

## 4.5 ResourceContext

- OperationResourceEligibility
- Resource
- Calendar
- Capacity
- ResourceGroup
- Setup必要属性
- 设备状态/不可用窗口
- ProductionDepartment约束

资源选择必须满足Eligibility，不得为了交期突破资格约束。

---

## 4.6 Material Quantity-Time Constraints

支持多段Quantity-Time：

例如：

- 40件 8月15日可用；
- 60件 8月17日可用。

不能压平为：

> 100件 8月17日。

下游工序只要数量阈值满足即可按真实AvailableTime推进。

---

## 4.7 StrategySnapshot

由3号位冻结、2号位装载后传入。

1号位只消费，不直接查规则维护表。

至少支持：

- Forward / Backward / Mixed；
- On-time目标；
- Dynamic Bottleneck参数；
- Bottleneck Override；
- Setup规则；
- Split上限；
- Stage overlap；
- Candidate Guardrail；
- 早做惩罚/稳定性偏好等已冻结次级目标。

---

## 4.8 ImmovableFacts

包括：

- 已执行/不可逆Task；
- Firm；
- Frozen；
- 已锁Resource/时间；
- 设备不可用；
- Demand Protection最终形成的不可移动约束；
- Candidate中其它Domain ACTIVE共享资源阻挡。

这些是硬边界，不得通过优化目标惩罚代替硬约束。


## 4.9 ExecutionStartContext

`DomainSolveRequest`必须能够让1号位明确知道每个LogicalProductionDemand的排程执行起点。

可采用：

- `StartStageCode`；
- `StartOperationCode`；
- 或与`NextOperationContext`等价的扁平字段/DTO。

不强制为本专项新增独立持久化实体。

### 数量切片

例如Routing：

```text
NC → 挤丝 → 研磨
```

同一PI剩余1000，其中：

- 800件已完成NC；
- 200件尚未完成NC。

2号位可传入：

```text
LogicalDemand A: Qty=800, StartOperation=挤丝
LogicalDemand B: Qty=200, StartOperation=NC
```

1号位必须：

- A只从挤丝开始占用能力；
- B从NC开始；
- 不给A重复安排NC；
- 保证各片段FinalTask/TaskShare数量闭合。

### 合批限制

两个逻辑生产片段只有在：

- Material / Operation兼容；
- 执行起点一致或合并后不会重排已完成工序；
- 其它既有合批条件满足；

时才能合批。

不得为了提高设备利用率，把不同执行起点片段提前合并到更早工序重新生产。

---

# 五、DomainSolveResult必须返回

## 5.1 FinalTask

每个FinalTask至少包含：

- 临时TaskKey（数据库Id由2号位持久化后生成）
- TaskNo Draft / StableKey
- MaterialId
- FactoryId
- StageCode
- OperationCode
- ResourceId
- PlannedStartTime
- PlannedEndTime
- Quantity
- PlannedProcessQty
- Status=`PLANNED`
- 是否Firm/Frozen/Execution继承
- 相关Route/Path

### Task.Quantity

表示：

> 净合格产出数量。

### Task.PlannedProcessQty

表示：

> 有限产能加工数量。

---

## 5.2 AllocationTaskShare

返回：

- AllocationSequence
- FinalTaskKey
- ShareQty

支持：

- 一个Allocation → 多个Task；
- 多个Allocation → 一个Task。

### 闭合

对需要生产的Allocation：

> Σ ShareQty = 该Allocation需制造的NetOutputQty。

---

## 5.3 TaskDependencies

表达真实FinalTask之间的物理依赖：

- FromTaskKey
- ToTaskKey
- DependencyType
- Quantity/Overlap条件（适用时）

物理Pegging/依赖只在FinalTask形成后产生。

---

## 5.4 ScheduleExplanationFacts

1号位生成“绑定约束事实”，例如：

- MATERIAL_NOT_AVAILABLE
- RESOURCE_CAPACITY_SHORTAGE
- PREDECESSOR_DELAY
- FIRM_FROZEN_CONSTRAINT
- SHARED_RESOURCE_BLOCK
- LOCK_CONSTRAINT
- CROSS_DOMAIN_AVAILABILITY
- SETUP_CONSTRAINT
- ROUTING_ELIGIBILITY

正式ReasonCode必须使用冻结字典。

### 红线

`DUE_DATE_RISK`是结果，不是根因。

不能所有延期都只返回：

> “交期风险”。

必须指出真正阻塞因素。

---

## 5.5 Unscheduled / Unfulfilled

如果物理上无法排入：

- 不得静默减少数量；
- 不得丢Task；
- 必须返回未排数量和原因。

“无法按期完成”不是Solver失败：

> 如果Solver正常完成并证明最早只能晚于DueDate，这是合法业务结果。

只有算法/数据完整性/硬约束求解本身出错，才是技术失败。

---

# 六、Solver外层固定五阶段

V1固定：

## Phase 1：硬约束构建

建立：

- Routing；
- Resource Eligibility；
- Calendar；
- Material AvailableTime；
- Execution/Firm/Frozen；
- Shared-resource blocks；
- Quantity-Time；
- 工序先后关系；
- StartStage / StartOperation执行起点边界。

---

## Phase 2：初始有限产能排程

按冻结策略：

- Forward；
- Backward；
- Mixed。

优先形成一版可行计划。

---

## Phase 3：可行性与延期诊断

识别：

- 哪些Demand未满足；
- 哪些Task晚于RequiredAvailableTime；
- 真实瓶颈；
- 物料/资源/前序/锁约束。

---

## Phase 4：有界局部修复

允许：

- 换合法资源；
- 附近空档；
- 局部重排；
- 有限拆批；
- 低优先级任务后移；
- Setup邻接修复。

不做：

- 无限搜索；
- 全局数学最优；
- TSP式全工厂Setup优化。

---

## Phase 5：压缩空隙与最终评价

在不破坏高优先级交期的情况下：

- 减少不必要等待；
- 减少WIP；
- 减少Setup；
- 提升利用率；
- 避免过早生产；
- 尽量保持计划稳定。

---

# 七、目标函数不是一个巨大加权分数

优先级固定：

## Level 0：硬约束

绝对不能违反：

- Capacity；
- Calendar；
- Eligibility；
- Routing；
- Material AvailableTime；
- Execution/Firm/Frozen；
- Strict物理约束。

---

## Level 1：业务履约

优先：

- Demand满足；
- Protection；
- DemandSequence；
- 高业务优先需求。

---

## Level 2：整单准交

例如目标：

> 95%整单按期。

如果该目标物理可实现：

> 不允许为了Setup/利用率牺牲整单准交。

---

## Level 3：次级优化

才考虑：

- Delay总量；
- LeadTime；
- WIP；
- Setup；
- 利用率；
- 稳定性；
- 避免太早生产。

---

# 八、Forward / Backward / Mixed

## 8.1 Forward

适合：

- 物料到货驱动；
- 当前已开工；
- 强AvailableTime约束；
- 供应开始时间明确。

---

## 8.2 Backward

适合：

- DueDate驱动；
- 有足够可用产能；
- 需要减少过早生产/WIP。

---

## 8.3 Mixed

建议V1主方式：

1. 从需求DueDate倒推理想窗口；
2. 遇物料/执行硬边界转Forward；
3. 在资源冲突处做有限局部修复；
4. 输出真实最早/最晚可行结果。

Strategy由3号位参数决定，1号位实现机制。

---

# 九、动态瓶颈

V1不把瓶颈永久写死为某一Stage。

基础判断：

> Load / AvailableCapacity

可以支持：

- `AUTO`
- `PREFER_ANCHOR`
- `FORCE_ANCHOR`
- `NOT_ANCHOR`

但不建设复杂瓶颈知识图谱。

---

# 十、拆批与合批

## 10.1 强制物理拆批

因：

- 设备最大批量；
- 工装容量；
- 工艺限制；

必须拆时直接拆。

---

## 10.2 优化型拆批

只尝试有限候选：

- 不拆；
- 2份；
- 3份。

不枚举无限组合。

---

## 10.3 合批

不同Demand可以合并成一个FinalTask，只要：

- Material/Operation/工艺相容；
- Resource能力允许；
- 交期不被破坏；
- AllocationTaskShare完整保留。

---

# 十一、Setup

只做局部相邻Task Setup计算。

当Task插入/移动时：

> 重新计算它与前后邻居的Setup。

不做全工厂全序列TSP优化。

Setup属性可包括：

- 模具；
- 刀具；
- 材质；
- 颜色；
- 其它冻结配置维度。

---

# 十二、Stage overlap / Transfer Batch

如果配置：

- 上游完成达到阈值数量；
- 下游即可开始；

则下游无需等待上游整批全部完成。

1号位必须支持：

> Quantity-Time阈值驱动的Stage overlap。

这也是保留40件/60件分段AvailableTime的重要原因。

---

# 十三、Candidate局部重排

## 13.1 输入

2号位提供：

- Base相关稳定事实；
- 本次Candidate完整逻辑生产需求；
- 变化Seed；
- 不可移动Task；
- 外Domain ACTIVE共享资源阻挡。

---

## 13.2 传播方式

冻结为：

```text
Seed
→ 找到直接受影响Task
→ 做最小修改
→ 只有Resource/Start/End/Qty真正变化才继续传播
→ 直到稳定
```

影响可以沿：

- 前序/后序；
- 物料Quantity-Time；
- 共享Resource时间轴；
- Setup邻居；

传播。

---

## 13.3 最小扰动顺序

优先：

1. 原Resource；
2. 邻近时间；
3. 空闲槽；
4. 有限合法替代Resource；
5. 后移低优先级；
6. 有限Split；
7. 局部重排。

---

## 13.4 Guardrail

默认技术参数可按冻结建议：

- Normal约60s；
- Soft 90s；
- Local Hard 180s；
- 受影响Task约30%警戒；
- 单Task修复尝试≤5；
- 传播轮次≤10；
- Resource候选Top5；
- Split候选≤3。

这些是技术Guardrail，不是业务规则。

---

## 13.5 Fallback

局部修复超限后：

> 使用同一Solver对本Domain全部可移动Task重新求解。

仍固定：

- Execution；
- Firm；
- Frozen；
- Protection；
- 其它不可逆事实；
- 外Domain共享资源阻挡。

不跨Domain移动其它计划。

---

# 十四、白天共享资源

Candidate严格单Domain。

如果A Domain和B Domain共享设备：

> B Domain当前ACTIVE占用作为不可移动资源时间块输入A Candidate。

A不得：

- 推动B；
- 删除B；
- 借用B配额；
- 建跨Domain资源协调平台。

夜间FULL则必须保证不同Domain最终计划在真实共享资源上不双占。

---

# 十五、跨Domain

1号位不建设MultiDomain Solver。

跨Domain CTP由2号位串联：

> 单Domain WHATIF → Quantity-Time输出 → 下一Domain WHATIF。

1号位每次只求解一个Domain。

---

# 十六、Firm/Frozen跨版本

不依赖FrozenZoneSnapshot平台。

2号位把上一ACTIVE仍有效的Firm/Frozen任务转成：

> ImmovableFacts / Anchor Tasks

传给1号位。

1号位：

- 固定其资源和时间；
- 新PlanVersion仍生成新的Task结果；
- 不要求复用旧TaskId。

ExecutionLock也是硬现实。

---

# 十七、UNLOCATED和规划占位

UNLOCATED PI份额：

- 使用2号位已经传入的冻结保守执行起点；
- 若上位规则只能确定最早可信Stage，则从该Stage开始；
- 1号位不得读取MES自行猜测更精确Operation；
- 可占用产能；
- 返回Explanation；
- 不得MES下发。

无正式PI的规划性生产：

- 可形成计划Task；
- 必须有明确不可下发标识；
- 等正式PI出现后次日全量重算替换。

采购Planning-only Placeholder：

- 只作为Material AvailableTime约束；
- **绝不生成采购Task**。

---

# 十八、失败定义

## 18.1 Solver技术失败

例如：

- 输入Routing图结构非法；
- 硬资源互斥被内部算法破坏；
- 数量丢失；
- 运行异常；
- Result无法通过2号位完整性校验。

返回Failure。

---

## 18.2 正常业务不可满足

例如：

- 资源不足；
- 材料太晚；
- DueDate物理不可达；
- 只能依赖Estimated采购。

返回：

> Success + Delayed/Unscheduled/Explanation。

不要抛技术异常替代业务结果。

---

# 十九、性能要求

目标：

> 约10万FinalTask / 90天 / 夜间约15分钟收敛目标。

设计要求：

- Interval Timeline，不用分钟Grid；
- Resource时间轴内存索引；
- 批量构建约束；
- 避免O(N²)全Task扫描；
- Setup只局部更新；
- Candidate只传播实际变化；
- Explanation不记录每次搜索Trace。

---

# 二十、明确不做

1号位V1禁止建设：

- 第二套ROUGH_CUT Solver；
- ShippingTask/车辆/月台有限能力；
- MultiDomain Solver；
- 全局数学最优器；
- 无限拆批组合；
- 全局TSP Setup；
- SolverTrace数据库；
- 动态插件市场；
- DSL排程脚本平台；
- Persisted Impact Graph；
- 通用因果图平台；
- 1号位MES Loader；
- 1号位PI Position / NextOperationContext解析器；
- Operation级Pegging或Operation Supply；
- NextOperationContext独立业务平台。

---

# 二十一、最低验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| S01 | 单工序单设备 | 时间合法、不超容量 |
| S02 | 多工序前后依赖 | 后工序不早于前工序可用 |
| S03 | 多候选设备 | 只选Eligibility合法Resource |
| S04 | Resource Calendar停机 | 不排入停机窗口 |
| S05 | Material AvailableTime | 不早于物料可用 |
| S06 | 40+60分段供给 | 下游可按40先启动，不压成100最晚 |
| S07 | PlannedProcessQty>NetQty | 能力按PlannedProcessQty占用 |
| S08 | 两Demand合批 | 一个FinalTask+两条TaskShare |
| S09 | 一个Demand拆批 | 多TaskShare数量闭合 |
| S10 | Firm Anchor | 不移动 |
| S11 | Execution Task | 不逆转 |
| S12 | Setup切换 | 局部Setup时间正确 |
| S13 | Stage overlap | 达阈值后可下游启动 |
| S14 | DueDate无法满足 | 返回延期原因，不技术失败 |
| S15 | Candidate同Resource插单 | 最小扰动 |
| S16 | Candidate外Domain共享设备 | 外Domain占用不可移动 |
| S17 | Candidate局部超限 | fallback同Domain全可移动重排 |
| S18 | Unlocated | 保守排产并Explanation |
| S19 | 无PI规划Task | 可排但标记不可MES |
| S20 | 采购Placeholder | 只限制物料时间，不生成Task |
| S21 | Solver数量完整性 | FinalTask/TaskShare数量不丢失 |
| S22 | 10万Task性能 | 达到约15分钟目标或给出可复现实测瓶颈 |
| S23 | PI全部未开工 | 从Routing首个合法Operation开始 |
| S24 | PI前序Operation已完成 | 从StartOperation继续，已完成工序不重复生成Task |
| S25 | 同PI两种执行起点 | 两个排程片段分别从各自StartOperation求解，数量闭合 |
| S26 | StartOperation不属于Routing | 返回输入/求解失败，不自行修正成别的工序 |
| S27 | 仅有StartStage | 在该Stage合法Routing内展开，不读取MES补猜 |
| S28 | UNLOCATED保守起点 | 按2号位输入保守排产，仍保持不可MES下发 |
| S29 | 不同StartOperation错误合批 | 不得导致已完成工序被重新排产 |
| S30 | 1号位尝试根据MES重新推导NextOperation | 验收失败 |
| S31 | NextOperationContext被当作Supply/Allocation层 | 验收失败 |

---

# 二十二、1号位交付物

1. `IFiniteCapacityScheduler`正式实现；
2. DomainSolveRequest/Result DTO确认；
3. Solver内部设计说明（只到必要算法机制，不做学术论文）；
4. S01～S31自动化/集成测试；
5. 性能测试报告；
6. Candidate局部重排测试；
7. Explanation ReasonCode映射清单；
8. 仍未完成项和真实技术风险。

---

# 二十三、与2号位联调顺序

建议：

## 联调1：接口形状

先用固定2～5条LogicalProductionDemand：

> 2号位 → Solver → FinalTask/TaskShare。

先不追求复杂优化。

## 联调2：执行起点与Routing裁剪

先验证：

- StartStageCode；
- StartOperationCode；
- 同PI不同执行起点数量切片；
- 已完成Operation不重复生成Task；
- 1号位不读取MES。

## 联调3：硬约束

加入：

- Routing；
- Resource；
- Calendar；
- Material AvailableTime。

## 联调4：计划良率与拆合批

加入：

- NetOutputQty；
- PlannedProcessQty；
- AllocationTaskShare。

## 联调5：Firm/Frozen/Execution

验证不可移动。

## 联调6：Candidate

验证：

- Seed；
- 动态传播；
- 外Domain阻挡；
- fallback。

## 联调7：10万Task

最终性能回归。

---

# 二十四、一句话完成定义

> **1号位的完成标准不是“写出一个排程算法”，而是：在2号位已经冻结的Pegging、数量关系和执行起点上下文基础上，从指定StartStage / StartOperation开始，用唯一一套90天有限产能Solver生成真实可执行FinalTask，并完整保留Allocation追溯、资源互斥、物料时间、Firm/Frozen、拆合批、Candidate局部传播和可解释结果；同时不重新做Pegging、不重新读取MES判断下一工序、不写数据库、不建设V1外的平台。**

---

# v1.2 PI Position执行起点上下文专项冻结增量

## 一、专项链路

1号位在本专项中的位置固定为：

```text
MES执行事实
+
Routing
+
PI Position
        ↓
5号位 NextOperationContext
        ↓
2号位 Pegging / LogicalProductionDemand
        ↓
DomainSolveRequest
        ↓
1号位有限产能Solver
        ↓
FinalTask
```

1号位不参与前两步业务解释。

---

## 二、1号位新增输入语义

1号位只需要知道：

- 该逻辑生产需求数量；
- 从哪个Stage继续；
- 必要时从哪个Operation继续；
- 后续合法Routing；
- 可选资源；
- 日历；
- 物料Quantity-Time；
- Firm/Frozen/Execution等不可移动事实。

无需知道MES原始报工明细如何推导出该起点。

---

## 三、StartOperation是Routing裁剪边界，不是新Pegging层

正式Pegging仍为：

```text
Demand ↔ ProductionInstruction Supply
```

StartOperation只用于：

```text
LogicalProductionDemand
↓
有限产能Routing展开起点
```

禁止把它实现为：

```text
Demand ↔ PI ↔ Operation
```

也禁止新建：

- Operation Supply；
- Operation Allocation；
- Operation Position平台。

---

## 四、已完成工序不重复排产

对于：

```text
NC → 挤丝 → 研磨
```

若800件已经完成NC：

```text
Qty=800
StartOperation=挤丝
```

1号位必须从挤丝开始。

若另有200件NC未完成：

```text
Qty=200
StartOperation=NC
```

则200件从NC开始。

这两个片段可以来自同一PI，但不能因为同PI就强行在NC前合并。

---

## 五、资源选择仍由1号位决定

`NextOperationContext`只确定下一合法工序，不确定具体设备。

例如：

```text
StartOperation = 挤丝
```

若挤丝可由Resource A/B执行：

- 哪台设备；
- 何时开始；
- 是否拆批；
- 是否与其它Demand合批；

仍由1号位根据有限产能求解决定。

因此：

> 5号位确定“从哪道工序继续”，1号位确定“这道工序在哪台设备、什么时候做”。

---

## 六、异常输入处理

若StartOperation：

- 不存在；
- 不属于当前Routing；
- 与StartStage冲突；
- 导致Routing断裂；

1号位不得静默改成首工序或其它工序。

应返回明确输入/求解错误，由2号位按Domain完整性规则处理。

若为UNLOCATED：

- 只消费2号位给出的保守执行起点；
- 不读取MES补猜；
- 保留不可MES下发语义。

---

## 七、本版不要求新增DDL

本专项只改变：

- DomainSolveRequest / LogicalProductionDemand输入字段；
- Solver Routing裁剪逻辑；
- 测试场景。

不要求1号位新增：

- 数据库表；
- MES连接；
- Position持久化；
- NextOperationContext持久化。

---

## 八、代码整改检查点

必须检查1号位当前代码：

1. 是否默认所有LogicalProductionDemand都从Routing首工序开始；
2. 是否已有StartStage但没有StartOperation处理；
3. 是否会忽略2号位传入的执行起点；
4. 是否会把已完成前序Operation再次生成Task；
5. 合批逻辑是否可能把不同执行起点片段错误合回更早工序；
6. Routing校验是否能识别非法StartOperation；
7. Solver内部是否存在MES/PI Position推导逻辑；
8. 是否保持FinalTask/TaskShare数量闭合。

---

## 九、v1.2完成定义

本专项完成必须满足：

- 1号位能消费StartStage / StartOperation；
- 能从正确Routing节点继续排程；
- 已完成Operation不重复占用产能；
- 同PI不同执行进度可分别排程；
- Resource仍由1号位有限产能求解；
- 不读取MES；
- 不计算PI Position；
- 不计算NextOperationContext；
- 不改变Demand↔PI Pegging；
- 不新增Operation级Supply/Allocation；
- 不新增DDL；
- FinalTask仍由1号位生成。

---

## 十、一句话交付要求

> **1号位本轮只需让现有90天有限产能Solver真正尊重2号位传入的执行起点：从StartStage/StartOperation开始展开Routing，跳过已经完成的前序工序，再按原有资源、日历、物料、Setup、Firm/Frozen和优化逻辑形成FinalTask；绝不能为了确定起点再去读取MES、解释PI Position或新增Operation级Pegging。**

