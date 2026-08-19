# APS V1 3号位规则、参数与运行生命周期开发实施包（冻结版）

**版本**：v1.0  
**日期**：2026-08-14  
**适用对象**：3号位及其开发AI  
**文档性质**：从零开发实施说明  
**上位依据**：APS V1三份业务冻结文档 + 六份技术冻结文档 +《APS V1关键接口冻结：1↔2、2↔5、2↔3》

---

# 一、3号位在APS V1中的定位

3号位不是Pegging执行器，也不是“每笔业务判断都来问一次”的在线规则服务。

3号位在V1中的核心职责是：

> **治理规则、参数、策略版本，并在一次ScheduleRun开始时把本次运行要使用的规则与参数冻结下来。**

同时，3号位负责：

- ScheduleRun / PlanVersion生命周期元数据边界；
- ExpectedDomainKeysJson冻结；
- StrategyProfileVersion绑定；
- Candidate最小人工确认/激活边界；
- 规则、参数、策略的维护与发布。

3号位不负责：

- Demand逐笔排序执行；
- Supply逐笔选择；
- Pegging Allocation；
- DemandBalance / SupplyBalance；
- PI Position；
- 采购/VMI/跨厂复杂事实；
- FinalTask；
- 有限产能Solver；
- Task持久化；
- MES下发。

一句话：

> **3号位负责“规则是什么、参数是多少、这次Run用哪个版本”；2号位负责“按这些规则执行Pegging”；1号位负责“按这些策略执行有限产能”。**

---

# 二、V1规则引擎总体原则

## 2.1 采用“全局默认 + 少量例外覆盖”

V1不建设：

- 复杂DSL；
- 脚本平台；
- 任意表达式引擎；
- 通用事件驱动规则引擎；
- 插件市场；
- 任意命令链编排器。

规则配置应尽量保持：

> **默认值 + 明确维度覆盖 + 第一命中。**

---

## 2.2 配置对象分三类

### A. RuleSet

回答：

> “遇到某种业务情况，采用哪条业务规则？”

例如：

- Demand Priority Segment；
- Demand Protection触发；
- Inventory Availability；
- PI简单排序；
- Candidate影响阈值是否告警。

---

### B. ParameterSet

回答：

> “规则中的数值是多少？”

例如：

- 默认采购LT；
- Warehouse Arrival Offset；
- On-time Target；
- Candidate 60/90/180秒Guardrail；
- Split最大候选数；
- Setup相关参数；
- Stage overlap阈值。

---

### C. StrategyProfile

回答：

> “这一类Domain/Factory/ProductFamily/OrderType使用哪组RuleSet + ParameterSet，并选择什么排程策略？”

例如：

- Forward / Backward / Mixed；
- 动态瓶颈模式；
- Default Solver Strategy。

---

# 三、现有六张治理表继续使用

正式结构继续围绕：

1. `RuleSet`
2. `RuleSetVersion`
3. `ParameterSet`
4. `ParameterSetVersion`
5. `StrategyProfile`
6. `StrategyProfileVersion`

不为每一种规则再建一套版本表。

---

# 四、版本生命周期

建议最小生命周期：

```text
DRAFT
→ PUBLISHED
→ RETIRED
```

如果现有DDL枚举略有不同，以现有兼容结构为准，不为命名重新建表。

### 规则

- DRAFT可编辑；
- PUBLISHED不可直接修改业务内容；
- 修改必须产生新Version；
- RETIRED不能再被新ScheduleRun选中；
- 历史PlanVersion仍要能追溯自己当时使用的版本。

---

# 五、一次ScheduleRun中的冻结规则

Run开始时，必须冻结：

- StrategyProfileVersionId；
- 对应RuleSetVersion；
- 对应ParameterSetVersion；
- ExpectedDomainKeysJson；
- DataCutoffTime。

一旦Run开始：

> 即使维护页面发布了新规则，本次Run仍继续使用启动时冻结版本。

不能出现：

```text
A Domain按上午规则
B Domain按中午刚发布的新规则
```

同一Run必须版本一致。

---

# 六、2↔3接口冻结

3号位给2号位的不是逐笔“决策结果”，而是一次性Snapshot。

建议DTO：

```csharp
public sealed class FrozenStrategySnapshot
{
    public long StrategyProfileVersionId { get; init; }

    public DemandPriorityConfig DemandPriority { get; init; } = default!;
    public LockConfig Lock { get; init; } = default!;
    public SupplyRuleConfig Supply { get; init; } = default!;
    public ProcurementConfig Procurement { get; init; } = default!;
    public SolverStrategyConfig Solver { get; init; } = default!;
    public CandidateGuardrailConfig Candidate { get; init; } = default!;
}
```

具体命名可按现有工程风格调整，但语义必须保持。

---

# 七、Demand排序规则

这是3号位最重要的规则治理之一。

## 7.1 不允许全局PriorityScore

正式模型：

```text
CalculationLayer
→ Priority Segment（有序）
→ 第一命中
→ Segment内部Sort
→ Stable Tie-break
```

### 示例

同一层里：

Segment 1：
- Delayed SALES_ORDER
- Sort：DueDate ASC → CustomerTier DESC → IssueDate ASC

Segment 2：
- Normal SALES_ORDER
- Sort：DueDate ASC → IssueDate ASC

Segment 3：
- PRODUCTION_INSTRUCTION
- Sort：IssueDate ASC

规则本身由3号位维护，2号位执行。

---

## 7.2 建议配置结构

每个Priority Segment至少包括：

- SegmentOrder；
- MatchConditions；
- SortFields；
- SortDirection；
- StableTieBreakFields；
- IsEnabled。

### 第一命中

一条Demand进入第一个匹配Segment后：

> 不再继续匹配后面的Segment。

避免多规则叠加导致不可解释。

---

## 7.3 不做什么

不建设：

- 任意布尔表达式DSL；
- 动态C#脚本；
- SQL片段规则；
- 全局Weighted Score。

---

# 八、Demand Protection规则

3号位只负责“触发条件治理”。

例如：

- RemainingTime < NormalLT；
- DelayStatus = DELAYED；
- CustomerTier = VIP；
- 特定OrderType；
- PMC人工强保护。

输出应是：

> 哪类Demand在什么条件下需要保护，以及保护到什么份额/时点。

最终实际Lock创建和数量校验：

> 由2号位执行。

---

## 8.1 Sticky规则

Demand Protection默认持续到：

- Demand完成；
- Demand取消；
- Supply失效；
- PMC显式释放。

如果支持手工释放，需要记录：

- Actor；
- Time；
- Reason。

3号位负责配置入口，2号位执行。

---

# 九、Supply相关规则

## 9.1 Inventory Availability

继续使用`InventoryAvailabilityRule`。

3号位治理：

- Warehouse是否可用；
- Priority；
- 必要Factory/ProductFamily上下文。

2号位执行实际库存选择。

---

## 9.2 PI简单排序

同Material多个PI，V1只需要简单可配置排序：

默认：

> Issue/Create Time ASC

必要时允许：

- CreatedAt；
- IssueDate；
- Stable PI No。

不要建设复杂PI评分引擎。

---

## 9.3 Procurement排序

正式排序原则属于冻结业务：

```text
Eligibility
→ Warehouse Priority
→ AvailableTime
→ PO Release Time
→ PO + Line Stable Sort
```

3号位可治理：

- Warehouse Priority；
- 默认LT；
- Margin；
- Arrival Offset。

2号位执行实际Supply分配。

---

# 十、采购参数

## 10.1 Default Purchase LT

主要维度：

- Receiving Warehouse；
- 必要Material属性。

不增加ProductFamily维度。

如果WarehouseCode全局唯一，不再重复加Factory作为配置维度。

---

## 10.2 ETA优先级

冻结为：

1. Manual ETA；
2. ERP ETA；
3. Default LT。

这是业务规则，不需要做成可任意重排的Rule Chain。

---

## 10.3 逾期Margin

当：

> ReleaseDate + DefaultLT < Now

使用冻结参数做保守修正。

例如可维护：

- MarginPercent；
- MinimumExtraDays。

具体默认值由业务配置，3号位只实现治理，不替0号位创造新口径。

---

## 10.4 Arrival-to-Usable Offset

主要按：

- Receiving Warehouse。

用于：

> ArrivalTime → AvailableTime。

---

# 十一、计划良率参数

3号位维护：

- Material / Stage /必要维度的Planning Yield；
- 默认值；
- 少量例外。

2号位计算：

> NetOutputQty → PlannedProcessQty。

1号位只消费PlannedProcessQty。

### 红线

已有PI Supply不能按Yield再次放大。

---

# 十二、Solver策略参数

3号位只维护策略，不实现Solver。

## 12.1 StrategyMode

支持：

- FORWARD；
- BACKWARD；
- MIXED。

---

## 12.2 Dynamic Bottleneck

支持：

- AUTO；
- PREFER_ANCHOR；
- FORCE_ANCHOR；
- NOT_ANCHOR。

不建设复杂瓶颈知识图谱。

---

## 12.3 On-time Target

例如：

> Whole Order On-time Target = 95%

这是业务优化层目标。

不能被Setup/WIP/Utilization目标反向压过。

---

## 12.4 Split参数

维护：

- MaxOptimizationSplitCount，例如3；
- Mandatory Split限制；
- MinBatchQty等必要参数。

不提供无限拆分组合。

---

## 12.5 Setup参数

允许配置：

- Mold；
- Tool；
- Material；
- Color；
- 其它已冻结维度。

不做全局TSP权重矩阵平台。

---

## 12.6 Stage Overlap

可配置：

- 是否允许；
- Transfer Batch Qty；
- Threshold Qty/Percent。

---

# 十三、Candidate Guardrail

这些属于技术参数，建议由3号位治理但不作为业务规则：

- Normal Time Limit ≈ 60s；
- Soft Limit ≈ 90s；
- Local Hard Limit ≈ 180s；
- Impacted Task警戒比例 ≈ 30%；
- Max Repair Attempts / Task ≈ 5；
- Max Propagation Rounds ≈ 10；
- Resource Candidate TopN ≈ 5；
- Split Alternative Max ≈ 3。

### 红线

Guardrail不能截断正确性。

例如：

> MaxImpactedOrders超阈值

只能：

- Warning；
- 要求人工确认。

不能停止影响传播并返回伪可行结果。

---

# 十四、ScopeJson与ExpectedDomainKeysJson

## 14.1 ScopeJson

继续保持冻结的11字段Schema。

不能为了后续需求随意往里加：

- ExpectedDomainKeys；
- Solver Trace；
- CrossDomain集合；
- 任意规则快照。

---

## 14.2 ExpectedDomainKeysJson

是ScheduleRun独立字段。

规则：

- FULL：一个或多个Domain；
- Candidate：严格一个Domain；
- Run开始后不可变；
- 不能根据已经创建的PlanVersion反推。

---

# 十五、ScheduleRun生命周期

3号位负责生命周期元数据和启动边界，但不需要强制重写2号位现有代码时序。

## 15.1 FULL

状态：

- RUNNING
- COMPLETED
- PARTIAL_SUCCESS
- FAILED

所有终态写CompletedAt。

---

## 15.2 Domain PlanVersion

典型状态：

- BUILDING
- ACTIVE
- FAILED
- CANDIDATE
- ARCHIVED

具体已有枚举以冻结DDL为准。

---

## 15.3 不强制分钟点

不要求：

- 必须00:38创建ScheduleRun；
- 必须02:00创建PlanVersion。

只要求：

- Run可追溯；
- DataCutoffTime一致；
- StrategyProfileVersion一致；
- ExpectedDomainKeysJson冻结；
- 每Domain PlanVersion可追溯；
- 终态闭合。

---

# 十六、FULL失败链

如果上游Domain失败：

- 上游PlanVersion → FAILED；
- 原ACTIVE保持；
- 直接/间接依赖下游本次不得发布新ACTIVE；
- 无关Domain继续。

3号位需要支持：

- Dependency关系查询；
- Run状态汇总；
- 被阻断Domain状态/原因展示所需元数据。

不建设：

- 全域ALL_OR_NOTHING；
- 原子多Domain激活组；
- 自动跨Domain回滚。

---

# 十七、人工恢复

失败后人工恢复：

> 新建ScheduleRun。

禁止：

> 把原FAILED Run改回RUNNING。

恢复范围：

- 失败根Domain；
- 因其失败被阻断的下游；
- 必要时补未恢复上游。

不建设复杂Retry平台。

---

# 十八、Candidate最小人工确认

V1不建设完整审批平台作为排程主链。

3号位只需要支持最小边界：

- Candidate生成；
- 展示；
- Actor确认；
- ConfirmedAt；
- CandidatePlanVersionId；
- Activate动作；
- 审计记录。

如果公司现有OA希望接入：

> 作为可选Adapter。

不能让OA不可用就导致Candidate功能本身不可用。

---

# 十九、RunType / Purpose

V1只保留冻结的合法组合。

3号位负责创建Run时校验。

例如：

- INSERT_ORDER_WHATIF + CTP
- INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS
- LOCAL_RESCHEDULE + INSERT_RESCHEDULE
- LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
- MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT

CTP/Impact Analysis不得激活。

---

# 二十、规则页面与权限边界

3号位后端需要为4号位提供：

- RuleSet列表；
- RuleSetVersion查看；
- ParameterSet列表；
- ParameterSetVersion查看；
- StrategyProfile；
- 发布/停用；
- Diff；
- 当前PUBLISHED版本；
- Run引用追溯。

不要求3号位开发最终前端页面。

---

# 二十一、变更审计

每次规则/参数版本发布至少保留：

- CreatedBy；
- CreatedAt；
- PublishedBy；
- PublishedAt；
- ChangeReason；
- VersionNo；
- ParentVersionId（如果现有结构支持）；
- 内容Snapshot/可重放数据。

不要求建设通用审计平台。

---

# 二十二、验证功能

建议提供：

> “发布前校验”

最少检查：

- 同一StrategyProfile引用的RuleSet/ParameterSet是否PUBLISHED；
- 是否存在循环/重复Priority Segment；
- 是否存在多个同优先级完全相同Match条件；
- Parameter数值是否越界；
- Candidate Guardrail是否为正；
- On-time Target是否0～100%；
- Split Count是否超V1限制；
- Warehouse参数是否引用有效Warehouse；
- StrategyMode是否合法。

---

# 二十三、不要做成规则平台

V1明确禁止：

- 用户输入任意表达式；
- 用户上传脚本；
- 动态编译；
- 任意SQL；
- 任意流程编排；
- 任意Plugin；
- Rule Marketplace；
- 通用CEP；
- 通用决策表平台。

只实现APS当前冻结业务需要的规则与参数。

---

# 二十四、建议开发顺序

## 阶段A：六表治理骨架

- RuleSet
- RuleSetVersion
- ParameterSet
- ParameterSetVersion
- StrategyProfile
- StrategyProfileVersion

完成CRUD、版本、发布、查询。

---

## 阶段B：FrozenStrategySnapshot

形成：

> StrategyProfileVersion → RuleSetVersion + ParameterSetVersion → Snapshot DTO。

支持2号位一次装载。

---

## 阶段C：Demand Priority

实现：

- Segment；
- First-match；
- SortField；
- Stable Tie-break。

先提供Fixture与真实Snapshot。

---

## 阶段D：Supply / Lock / Procurement参数

包括：

- InventoryAvailability；
- PI Sort；
- Demand Protection；
- Default Purchase LT；
- Margin；
- Warehouse Offset。

---

## 阶段E：Solver Strategy

包括：

- Forward/Backward/Mixed；
- Dynamic Bottleneck；
- On-time Target；
- Split；
- Setup；
- Overlap；
- Candidate Guardrail。

---

## 阶段F：ScheduleRun/Candidate生命周期

包括：

- ExpectedDomainKeysJson；
- StrategyProfileVersion绑定；
- Candidate确认；
- Activation；
- FULL/PARTIAL/FAILED汇总配合。

---

# 二十五、最低验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| R01 | 发布RuleSetVersion | 历史版本不可被覆盖 |
| R02 | 发布ParameterSetVersion | 新Run可引用，旧Run不变 |
| R03 | Run启动后发布新规则 | 当前Run仍用旧冻结版本 |
| R04 | Priority Segment第一命中 | Demand只进入一个Segment |
| R05 | 同Segment多Sort字段 | 顺序稳定 |
| R06 | 不同DemandType同层竞争 | 按Segment规则交错，不用全局Score |
| R07 | Demand Protection触发 | 2号位能读取触发配置 |
| R08 | PI默认排序 | Issue/Create Time规则正确 |
| R09 | Warehouse Availability | 规则可正确解析 |
| R10 | Manual ETA > ERP ETA > DefaultLT | 参数链可表达 |
| R11 | DefaultLT逾期 | Margin可配置 |
| R12 | Arrival Offset | Warehouse级生效 |
| R13 | Planning Yield | 2号位可取正确版本值 |
| R14 | MIXED Solver Strategy | 1号位能读取 |
| R15 | On-time Target | 取值正确 |
| R16 | Candidate 60/90/180 | Snapshot中正确 |
| R17 | MaxImpactedOrders | 仅Warning语义 |
| R18 | FULL ExpectedDomainKeysJson多Domain | 合法 |
| R19 | Candidate ExpectedDomainKeysJson多于1个 | 拒绝 |
| R20 | FAILED Run人工恢复 | 新建Run |
| R21 | Candidate确认 | Actor/Time/Version可追溯 |
| R22 | OA不可用 | 不阻断最小人工确认能力 |

---

# 二十六、性能要求

规则与参数不是性能瓶颈。

关键要求：

- 一次Run只加载一次FrozenStrategySnapshot；
- 不在Pegging循环中逐笔查数据库；
- 不在Solver每个Task中逐笔读配置表；
- Snapshot可被内存缓存；
- Cache Key必须包含VersionId。

---

# 二十七、3号位交付物

1. 六张治理表对应后端实现；
2. Rule/Parameter/Strategy版本发布机制；
3. FrozenStrategySnapshot DTO；
4. Demand Priority Segment配置；
5. Demand Protection配置；
6. Supply/Procurement参数；
7. Solver Strategy参数；
8. ScheduleRun/ExpectedDomainKeysJson生命周期能力；
9. Candidate最小确认/激活后端；
10. R01～R22测试结果；
11. 与2号位Snapshot联调记录；
12. 与1号位Solver Strategy联调记录。

---

# 二十八、与2号位联调红线

2号位只能：

- 读取PUBLISHED版本；
- Run开始时冻结；
- 在内存执行。

不能：

- 修改RuleSet；
- 临时写回Parameter；
- 每笔Allocation调用3号位在线判断。

3号位不能：

- 返回“订单A优先于订单B”的逐笔结果；
- 替2号位执行Allocation；
- 直接修改DemandBalance/SupplyBalance。

---

# 二十九、与1号位联调红线

1号位只消费Solver Strategy Snapshot。

3号位不能直接控制：

- Task；
- Resource；
- Start/End。

1号位不能：

- 自己读治理表；
- 忽略StrategyProfileVersion；
- 在Run中途刷新参数。

---

# 三十、完成定义（Definition of Done）

3号位完成必须同时满足：

- 六表治理可用；
- 发布版本不可被静默覆盖；
- Run冻结版本可追溯；
- Priority Segment可表达冻结排序；
- 无全局PriorityScore；
- Demand Protection触发配置可表达；
- Procurement参数完整；
- Planning Yield参数可取；
- Solver Strategy可被1号位消费；
- Candidate Guardrail完整；
- ExpectedDomainKeysJson规则正确；
- FULL/PARTIAL/FAILED生命周期可支持；
- Candidate最小人工确认可用；
- 不依赖完整OA；
- 无逐笔RPC规则执行；
- 无DSL/插件平台过度设计；
- R01～R22通过。

---

# 三十一、一句话交付要求

> **3号位的V1任务不是建设一套“万能规则平台”，而是把APS已经冻结的规则、参数和策略做成可版本化、可发布、可冻结、可追溯的配置体系，让2号位和1号位在一次Run中使用同一份不再变化的规则快照。**
