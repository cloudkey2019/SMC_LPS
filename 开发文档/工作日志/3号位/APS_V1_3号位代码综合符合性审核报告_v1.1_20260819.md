# APS V1 3号位代码综合符合性审核报告（冻结基线核对版）

**版本**：v1.1  
**日期**：2026-08-19  
**审核对象**：3号位当前 GitHub 仓库 `cloudkey2019/SMC_LPS` 中本轮3号位新增/修改代码  
**补充输入**：《APS_V1_3号位代码审查问题跟踪_v1.0_20260819》  
**报告性质**：3号位首次正式整改冻结基线（综合版）  

---

# 0. 本次审核为什么重新生成

本报告不是重新设计3号位，也不是推翻前一次审核。

本次重新生成的原因有三个：

1. 3号位补交了其AI自审问题跟踪表，需要把其中有效问题纳入；
2. 需要重新逐项对照APS V1最终冻结业务、关键2↔3接口和3号位实施包；
3. 需要再次与**已经冻结的2号位代码审核基线**核对，避免因为审3号位又反向给2号位追加或改变要求。

本报告生成后：

> **以本报告作为3号位当前这一版代码的正式冻结整改基线。**

后续除非：
- 3号位提交新代码；
- 或明确证明本报告存在审核遗漏/误判；

否则审核1/2/4/5号位时，不再随意反向给3号位新增或改变要求。

---

# 1. 权威依据与优先级

本次按以下顺序审核：

1. `APS_V1_最终全部流程与业务基线_v1.0_20260812`
2. `APS_Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版`
3. `APS_有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版`
4. 六份技术冻结文档 v20260812
5. `APS_V1_关键接口冻结_1-2_2-5_2-3_v1.0_20260814`
6. `APS_V1_3号位规则参数与运行生命周期开发实施包_v1.0_20260814`
7. 已冻结的2号位代码审核结论
8. 3号位当前代码
9. 3号位AI自审文档

**代码、旧README、旧字段说明残留、早期接口不得反向修改上位冻结业务。**

特别提醒：

当前v5.1.2字段说明中仍残留“5号位插件执行Priority/Pegging规则”的旧文字，这与最终业务冻结、关键接口冻结及3号位v1.1接口核对结论冲突。该旧文字只能视为历史残留，不能作为本轮开发依据。

---

# 2. 3号位最终职责重新确认

3号位负责：

- RuleSet / ParameterSet / StrategyProfile治理；
- 版本创建、校验、发布、停用、追溯；
- `FrozenStrategySnapshot`构建；
- Demand Priority Segment配置；
- Demand Protection触发配置；
- Inventory / PI / Procurement相关规则与参数；
- Planning Yield参数；
- Solver Strategy参数；
- Candidate Guardrail；
- ScheduleRun生命周期元数据边界；
- ExpectedDomainKeysJson冻结边界；
- StrategyProfileVersion绑定；
- Candidate最小人工确认 / 激活边界。

3号位**不负责**：

- Demand逐笔排序执行；
- Supply逐笔选择；
- Pegging / Allocation；
- DemandBalance / SupplyBalance；
- PI Position；
- PO/VMI/跨厂复杂事实；
- FinalTask；
- Solver；
- Task持久化；
- MES下发；
- 每笔业务在线RPC判断。

最终运行关系：

```text
3号位治理并发布规则/参数
        ↓
StrategyProfileVersion
        ↓
FrozenStrategySnapshot
        ↓
2号位一次Run装载一次
        ↓
2号位执行Demand排序 / Supply选择 / Protection / Pegging
        ↓
1号位消费Solver Strategy执行有限产能
```

对5号位：

```text
3号位 FrozenStrategySnapshot
        ↓
2号位抽取最小 FrozenFactParameters
        ↓
5号位复杂事实计算
```

**不存在3→5运行时规则决策接口。**

---

# 3. 与2号位冻结审核基线的核对结果

这是本轮最重要的防漂移检查。

| 事项 | 2号位冻结口径 | 3号位本轮应保持 | 本轮结论 |
|---|---|---|---|
| 主流程Owner | 2号位SchedulingOrchestrator | 3号位不得另建主排程Orchestrator | ✅ 不新增2号位要求 |
| Demand排序 | 3配置、2执行 | 3只提供Segment Snapshot；Matcher若保留只能作为纯函数共享实现 | ✅ |
| PriorityScore | V1退出权威 | 3 Validator禁止PriorityScore | ✅ |
| FrozenSnapshot | Run开始由2一次装载 | 3提供`GetFrozenStrategySnapshot(versionId)` | ✅ |
| Snapshot刷新 | Run中不刷新 | 3不得按Domain/逐Demand重新取当前规则 | ✅ |
| Protection | 3配置、2执行份额锁 | 不建3/5 Protection决策插件 | ✅ |
| Inventory/PI/Procurement排序 | 3配置、2执行 | 3不返回逐笔Supply决定 | ✅ |
| 5号位 | 复杂事实 | 3不调用5做Priority/Pegging裁决 | ✅ |
| Planning Yield | 3配置、2反算 | 3 Snapshot必须提供，1只消费PlannedProcessQty | ⚠️ 当前Snapshot遗漏 |
| Candidate | 2主流程执行，3生命周期/确认边界 | 3不得实现MultiDomain Candidate或改2主流程 | ✅边界；实现未完成 |
| ScheduleRun | 2已有外壳受保护 | 3只补生命周期元数据/创建确认边界，不重写2现有外壳 | ✅ |
| Default策略 | Run绑定一个StrategyProfileVersionId | 3治理可选默认PUBLISHED策略；2实现细节不由3反向规定 | ⚠️治理未闭合 |

**结论：本报告不会新增任何新的2号位P0。**

本轮3号位整改必须做到：

> **3号位自己补齐治理与Snapshot能力，不以“3号位实现需要”为由要求2号位改变已冻结主链。**

---

# 4. 当前代码已经做对的部分——冻结保留

以下方向本轮确认正确，后续不要再反复改。

## PASS-01：`IFrozenStrategySnapshotProvider`方向正确

当前采用：

```text
StrategyProfileVersionId
→ GetFrozenStrategySnapshotAsync(...)
→ 返回冻结Snapshot
```

符合：

- 一次Run一份规则真相；
- 由指定VersionId读取；
- 2号位一次装载；
- 不逐笔RPC。

**保留。**

---

## PASS-02：Snapshot拥有版本锚点

当前Snapshot已携带：

- StrategyProfileVersionId
- RuleSetVersionId
- ParameterSetVersionId

方向正确。

一次Run各Domain应使用同一个StrategyProfileVersion绑定出的三版本组合。

---

## PASS-03：六块Snapshot结构方向正确

当前已经形成：

- DemandPriority
- Lock
- Supply
- Procurement
- SolverStrategy
- CandidateGuardrail

这是关键接口冻结“三大类”的合理细拆。

**不要合并回一个万能ConfigJson DTO，也不要新建第二套Snapshot版本体系。**

但当前仍缺PlanningYield，见P0-03。

---

## PASS-04：没有建立3→5运行时决策接口

当前代码未把Snapshot做成：

```text
3号位 → 5号位逐笔Priority/Pegging调用
```

这是正确的。

`FrozenFactParameters`由2号位抽取，不应让3号位DTO依赖5号位接口类型。

---

## PASS-05：Demand Priority总体模型正确

当前模型为：

```text
Priority Segment
→ First Match
→ Segment内部多字段Sort
→ Stable Tie-break
```

这是冻结V1模型。

当前Validator也已经针对`PriorityScore`做禁止性检查。

---

## PASS-06：3号位AI自审已修的局部问题大多有效

自审文档中以下修正可认可并保留：

- JsonElement数值/字符串归一化；
- IN列表项归一化；
- CompareValues类型兼容；
- SegmentOrder正整数；
- Warning与Error分离；
- PriorityScore检测从粗糙`Contains("Score")`收敛；
- First-match避免无意义重复排序。

这些属于有效代码质量修复，不改变业务。

---

# 5. P0——正式开发继续前必须整改

本轮综合后冻结为 **8个P0**。

---

# P0-01 六张治理表代码与冻结DDL不一致，且暴露出“版本内容如何可重放”的技术冻结缺口

这是当前最优先问题。

## 5.1 当前冻结DDL事实

`RuleSetVersion`正式字段只有：

- Id
- RuleSetId
- VersionCode
- Status
- EffectiveFrom / EffectiveTo
- PublishedAt / PublishedBy
- ApprovedAt / ApprovedBy
- CreatedAt / CreatedBy

`ParameterSetVersion`同理。

只有`StrategyProfileVersion`有：

> `IsDefault`

并通过：

```text
StrategyProfileVersion
→ RuleSetVersionId
→ ParameterSetVersionId
```

组合成本次可运行的策略包。

## 5.2 当前代码问题

当前3号位代码给`RuleSetVersion`增加了类似：

- IsDefault
- Remarks
- UpdatedAt / UpdatedBy
- DemandPriorityJson

给`ParameterSetVersion`增加了类似：

- IsDefault
- Remarks
- UpdatedAt / UpdatedBy
- LockJson
- SupplyJson
- ProcurementJson

Repository的SQL已经直接读写这些字段。

如果直接连冻结v5.1.2数据库：

> **SQL字段不匹配，正式运行会失败。**

## 5.3 但不能简单粗暴地“删掉JSON就完事”

这里需要特别修正前一次审核中可能过于简单的表述。

冻结技术文档同时要求：

- 六张治理表负责版本/发布/组合/运行绑定/追溯；
- 主题规则表负责业务字段；
- 发布后必须可追溯、不可原地修改；
- 3号位实施包还要求保留“内容Snapshot/可重放数据”。

但当前冻结DDL里：

> **主题规则表与RuleSetVersion/ParameterSetVersion之间没有明确版本绑定字段；六张治理表自身又没有“版本内容快照”字段。**

因此这是一个**真实的技术冻结遗漏**：

> 当前冻结业务要求“历史版本可重放”，但DDL没有完整给出内容版本化落点。

这不是重新打开业务，也不是3号位代码可以自行决定。

## 5.4 本轮冻结整改动作

3号位先停止继续扩写这套Repository，形成一张：

### 《六块Snapshot持久化来源映射表》

至少列：

| Snapshot块 | 当前业务字段来源 | 当前是否可按VersionId重放 | 缺口 |
|---|---|---|---|
| DemandPriority | ? | 是/否 | |
| Lock / Protection | ? | | |
| Supply / PI Sort | ? | | |
| Procurement | ? | | |
| PlanningYield | ? | | |
| SolverStrategy | ? | | |
| CandidateGuardrail | ? | | |

然后由0/2/3只做一次**最小技术DDL对齐**。

### 推荐的最小方向

优先考虑：

> **主题表继续负责可维护业务字段；RuleSetVersion / ParameterSetVersion只增加最小“发布内容快照/可重放载体”，而不是增加大量主题专用列或新建多套版本表。**

例如可以评估：

- RuleSetVersion：一个规则内容Snapshot字段；
- ParameterSetVersion：一个参数内容Snapshot字段；
- 必要的ChangeReason/审计落点。

具体字段名由技术实现定，但必须满足：

1. 发布后不可变；
2. 能按StrategyProfileVersionId恢复当次Run完整Snapshot；
3. 不新增独立Snapshot表；
4. 不新增新的版本号体系；
5. 不把所有主题规则表废掉；
6. 不建设DSL/RuleCondition/RuleAction平台。

**在这个最小技术对齐确认前，3号位不要自行ALTER正式DDL。**

---

# P0-02 RuleSetVersion / ParameterSetVersion不应拥有自己的“默认版本真相”

当前代码给：

- RuleSetVersion
- ParameterSetVersion

都建立了`IsDefault`/ClearDefault/GetDefault逻辑。

这是错误的。

冻结数据库只有：

> `StrategyProfileVersion.IsDefault`

正确真相：

```text
StrategyProfileVersion
  = 某个RuleSetVersion
  + 某个ParameterSetVersion
```

ScheduleRun绑定的是：

> StrategyProfileVersionId

因此：

- RuleSetVersion不决定Run默认；
- ParameterSetVersion不决定Run默认；
- 只有StrategyProfileVersion是策略包选择真相。

### 必须修改

删除RuleSetVersion / ParameterSetVersion：

- IsDefault语义；
- ClearDefaultFlag；
- GetDefaultByRuleSet；
- GetDefaultByParameterSet；
- 发布时对子版本“清默认”的逻辑。

---

# P0-03 FrozenStrategySnapshot当前不完整：缺PlanningYield，Solver/Candidate也没有真实版本来源

这是本轮与8月17日最终接口裁决再次核对后必须补上的重点。

当前六块里：

- DemandPriority：有装载逻辑；
- Lock：有装载逻辑；
- Supply：有装载逻辑；
- Procurement：有装载逻辑；
- SolverStrategy：当前仍主要是空/default对象；
- CandidateGuardrail：当前仍主要是空/default对象。

同时：

> **PlanningYield没有被明确放入FrozenStrategySnapshot。**

这是不完整的。

## 冻结口径

3号位必须治理：

- Planning Yield；
- Solver Mode：FORWARD / BACKWARD / MIXED；
- Dynamic Bottleneck；
- On-time Target；
- Split；
- Setup；
- Stage overlap；
- Candidate Guardrail。

2号位必须能从同一Run Snapshot取得：

> PlanningYield → 反算 PlannedProcessQty。

1号位只能消费2号位传来的PlannedProcessQty。

### 必须修改

Snapshot必须显式包含PlanningYield。

可以：

- 独立`PlanningYield`子块；
- 或放入明确的现有参数块；

但语义必须显式、可版本追溯。

不得：

- 让2号位再查另一套PlanningYield当前表；
- 让1号位自己查；
- 让SolverStrategy / CandidateGuardrail继续依赖代码默认值冒充“冻结配置”。

---

# P0-04 Snapshot装载失败不能静默回退空Block

当前Provider对部分JSON存在类似：

```text
配置为空 → new Block()
JSON解析异常 → catch → new Block()
```

这会造成：

```text
ScheduleRun绑定StrategyProfileVersion 18
↓
18号版本内容损坏
↓
程序偷偷按空规则/默认规则执行
↓
数据库仍显示“本Run使用18号版本”
```

这样版本追溯失真。

## 正确边界

正式新Run：

- 必填Block缺失 → Snapshot装载失败；
- JSON/内容损坏 → Snapshot装载失败；
- 业务约束不合法 → 发布前就应拒绝；
- 只有**明确允许缺省的单个字段**才允许使用冻结默认值。

### 历史版本读取注意

`GetFrozenStrategySnapshot(strategyProfileVersionId)`是按明确ID读取。

如果一个旧Run引用的版本后来被DISABLED/ARCHIVED：

> 历史追溯仍应允许按ID读取其不可变内容。

“新Run只能选当前合法PUBLISHED版本”属于**Run绑定/默认选择边界**，不要错误地在历史Provider里把旧版本读不出来。

---

# P0-05 正式Publish必须强制执行发布前校验，不能有“校验API”和“发布API”两条可绕路径

当前代码已经有：

- ValidateRuleSetVersionForPublish
- ValidateParameterSetVersionForPublish
- DemandPriorityValidator

但正式Publish路径没有完整强制串起来。

冻结要求是：

```text
Validate
↓
有Error：拒绝
↓
通过
↓
Publish
```

不能：

> 页面可选择“先校验”，但直接调用Publish仍可发布坏配置。

## 发布前至少校验

### Priority

- SegmentOrder > 0；
- SegmentOrder唯一；
- MatchCondition字段合法；
- Operator和值类型匹配；
- SortField字段合法；
- SortDirection合法；
- StableTieBreak字段合法；
- 禁止PriorityScore；
- 未知Tie-break字段不得运行时静默回退OrderId。

### 参数

- Planning Yield：`0 < Yield <= 1`或统一百分比口径；
- SolverMode只允许FORWARD/BACKWARD/MIXED；
- On-time Target合法；
- Candidate Guardrail > 0；
- Normal ≤ Soft ≤ Hard；
- Split参数不超过冻结V1边界；
- Overlap阈值合法；
- Warehouse等引用对象合法。

### StrategyProfileVersion

- 引用RuleSetVersion合法；
- 引用ParameterSetVersion合法；
- 组合可以形成完整Snapshot。

不建通用Validation平台。

---

# P0-06 StrategyProfileVersion治理没有形成完整闭环

当前已经有Entity/Repository，但Application治理还不完整。

而StrategyProfileVersion才是ScheduleRun真正绑定的策略包真相。

必须至少提供：

- ValidateStrategyProfileVersionForPublish；
- PublishStrategyProfileVersion；
- 当前有效默认PUBLISHED策略包查询/解析；
- RuleSetVersion/ParameterSetVersion引用合法性检查；
- RunType匹配；
- EffectiveFrom / EffectiveTo；
- 默认策略唯一性和歧义检查；
- Run引用追溯。

## 与2号位冻结审核的边界

这里**不要求2号位修改已有主流程或重写SQL**。

跨号位语义只冻结：

> 新Run没有显式StrategyProfileVersionId时，必须得到一个当前有效、无歧义的PUBLISHED策略包；如果配置存在歧义，应报配置错误，而不是随机取一个。

Exact SQL / Cache前缀：

> 仍然是实现细节，不作为3号位反向修改2号位的理由。

---

# P0-07 Demand Priority虽然框架正确，但还不能表达冻结的真实默认排序

当前`DemandField`/Matcher主要有：

- RemainingTimeHours
- DelayStatus
- CustomerTier
- OrderType
- IsPmcProtected
- PriorityLevel

但冻结示例的核心排序需要：

- DueDate
- IssueDate

例如：

```text
Delayed SALES_ORDER
→ DueDate ASC
→ CustomerTier DESC
→ IssueDate ASC
```

因此当前模型还不完整。

### 必须补

- DemandField.DueDate；
- DemandField.IssueDate；
- DemandRecord对应字段；
- Matcher Sort映射；
- Validator合法字段映射；
- 测试。

### CalculationLayer不要塞进3号位在线Matcher

冻结执行方式：

```text
2号位先按CalculationLayer组织当前层Demand
→ 再对该层应用3号位Priority Segment
```

不要让3号位重新管理全局Demand池。

---

# P0-08 3号位V1交付还缺运行生命周期与Candidate最小确认边界

当前提交主体仍集中在：

- 治理骨架；
- Snapshot骨架；
- Demand Priority。

但3号位实施包正式交付还包括：

- ExpectedDomainKeysJson冻结规则；
- StrategyProfileVersion绑定；
- Candidate最小确认；
- Candidate Activation边界；
- FAILED恢复新建Run；
- Rule/Parameter/Strategy与Run引用追溯。

## 必须遵守与2号位边界

### FULL

- ExpectedDomainKeysJson：≥1 Domain；
- 多Domain由2号位主流程实际计算；
- 3号位不重写SchedulingOrchestrator；
- FULL结果持久化/Domain计算仍归2号位。

### Candidate

- ExpectedDomainKeysJson严格1个Domain；
- CTP / INSERT_IMPACT_ANALYSIS不得激活；
- 正式Reschedule Candidate只做最小人工确认：
  - Actor
  - ConfirmedAt
  - CandidatePlanVersionId
  - 必要Remark
- 不强制OA；
- 不建MultiDomain Candidate。

### FAILED恢复

- 新建ScheduleRun；
- 不把旧FAILED改回RUNNING。

这些属于3号位生命周期边界，不允许借此重写2号位已经冻结的运行状态执行逻辑。

---

# 6. P1——上线前建议闭合，但不需要扩架构

## P1-01 FrozenSnapshot集合可变性

3号位自审A-2指出：

> List<T> + setter允许外部修改Snapshot内部集合。

这个问题真实存在，但不应定为“需要0号位重新打开契约”的CRITICAL业务问题。

### 处理

建议：

- `IReadOnlyList<T>`；
- 或Provider构建后做防御性Copy；
- 尽量用`init`。

目标只是：

> 防止Run内存Snapshot被误修改。

这属于工程加固，不是业务决策。

---

## P1-02 SortField / SegmentName可读性校验

3号位自审：

- B-6 单个SortField未充分校验；
- B-7 SegmentName可重复；
- C-7 未知Tie-break静默回退。

其中：

- B-6/C-7已并入P0-05发布强校验；
- SegmentName重复建议至少Warning，最好发布时拒绝，避免UI/日志难以区分。

---

## P1-03 测试没有达到R01～R22

当前已有：

- DemandPriorityMatcherTests；
- FrozenStrategySnapshotProviderTests；
- ParameterSetVersionPublishTests；
- RuleSetVersionPublishTests。

这是良好开始，但还不等于3号位交付完成。

最终必须覆盖R01～R22，尤其：

- Run中发布新规则不漂移；
- Priority First-match；
- DueDate / IssueDate；
- Demand Protection；
- PI Sort；
- Warehouse规则；
- ETA优先级不可重排；
- DefaultLT/Margin/Offset；
- Planning Yield；
- MIXED Solver Strategy；
- On-time Target；
- Candidate 60/90/180；
- MaxImpactedOrders只Warning；
- FULL多Domain ExpectedDomain；
- Candidate多Domain拒绝；
- FAILED新Run；
- Candidate确认；
- OA不可用不阻断。

---

## P1-04 README存在明显旧职责残留

README仍有类似：

> 5号位BusinessRules负责Pegging / Priority

以及早期“3号位调用Engine + BusinessRules”的描述。

这些文字容易重新把3号位AI带回已经废止的职责模型。

### 最小修改

不用重写README。

只需在顶部增加醒目标记：

> **当前APS V1职责以2026-08冻结业务基线、关键接口冻结和实施包为准；以下旧阶段说明仅历史参考。**

并把最危险职责修正成：

- 2：主流程/Pegging执行；
- 3：规则参数治理/Snapshot/生命周期边界；
- 5：复杂事实，不是Priority/Pegging决策插件。

---

# 7. P2——当前不要阻断V1

以下来自3号位AI自审，但不应升级为本轮架构整改。

## P2-01 `object? Value`重构成判别联合

当前已有Normalize逻辑兜底。

V1不为了“类型更漂亮”重构成复杂规则AST / ScalarValue / ListValue体系。

只需保证：

- JSON类型归一化；
- 发布校验；
- 常见string/int/decimal/bool/list/date测试。

---

## P2-02 required / nullable / record等代码风格

包括：

- required关键字；
- 空字符串/nullable统一；
- ValidationResult改record。

可以顺手做，但不作为上线阻断。

---

## P2-03 人为创造Segment≤100、Condition≤50等上限

当前没有业务依据，不要为了DOS理论风险凭空冻结100/50这类数字。

如果真实压测证明需要Guardrail，再加技术上限。

---

# 8. 一个需要0/2/3做一次技术确认、但不重新开业务的事项

只有一个：

> **六块规则/参数的“版本内容可重放”物理落点。**

这是当前冻结DDL与3号位实施包之间确实存在的技术缺口。

这不是：

- 新业务；
- 2号位新P0；
- 要增加新表；
- 要改变主链。

建议3号位先提交：

### 《六块Snapshot持久化来源映射表 + 最小DDL差异》

0号位只确认：

> 采用哪一种最小技术落位。

确认后一次性补字段/文档，不要让3号位边写代码边自行扩表。

除此之外，本轮3号位不需要等待2号位重新确认业务。

---

# 9. 3号位下一步整改顺序

## 第一批：先解决“能不能形成一份真实、可重放Snapshot”

1. P0-01 六块持久化来源映射 + 最小DDL差异；
2. P0-02 删除RuleSet/ParameterSet子默认；
3. P0-03 补PlanningYield、SolverStrategy、CandidateGuardrail真实来源；
4. P0-04 Snapshot坏配置明确失败。

## 第二批：版本治理闭环

5. P0-05 Publish强校验；
6. P0-06 StrategyProfileVersion发布/默认/有效期/RunType治理。

## 第三批：业务配置完整性

7. P0-07 DueDate / IssueDate；
8. Demand Protection / Inventory / PI / Procurement / PlanningYield参数完整。

## 第四批：生命周期

9. P0-08 ExpectedDomainKeysJson；
10. Candidate确认/激活；
11. FAILED新Run；
12. Run引用追溯。

## 第五批：测试

13. R01～R22；
14. 与2号位Snapshot联调；
15. 与1号位Solver Strategy联调。

---

# 10. 下一轮只检查这些，不重新审已经通过的方向

下一轮代码提交后重点复核：

1. RuleSetVersion / ParameterSetVersion不再访问不存在列；
2. 子版本IsDefault彻底退出；
3. 六块+PlanningYield全部可从StrategyProfileVersion对应内容重放；
4. Snapshot坏配置不静默降级；
5. Publish必经Validate；
6. StrategyProfileVersion治理闭环；
7. DueDate / IssueDate可用；
8. 2号位仍一次Run只装载一次；
9. 无3→5决策接口；
10. ExpectedDomainKeys/Candidate/FAILED边界；
11. R01～R22覆盖情况。

以下不重新打开：

- Priority Segment模型；
- First-match；
- Stable Tie-break；
- 禁PriorityScore；
- 2号位执行Demand排序；
- 2号位执行Supply选择；
- 2号位执行Protection；
- 5号位只做复杂事实；
- FrozenFactParameters由2号位抽；
- ETA优先级Manual > ERP > DefaultLT；
- 不建DSL/插件市场；
- 不建MultiDomain Candidate。

---

# 11. 可直接发给3号位AI的整改指令

> 本次以《APS V1 3号位代码综合符合性审核报告 v1.1》作为唯一整改主基线，不重新讨论APS V1业务。
>
> 保留当前`FrozenStrategySnapshotProvider + GovernanceVersionService + DemandPriorityMatcher/Validator`整体方向。
>
> 第一优先先解决规则/参数版本内容物理落点：当前代码对RuleSetVersion/ParameterSetVersion新增的IsDefault/JSON/Updated字段与冻结DDL不一致；同时冻结DDL本身缺少明确的“版本内容可重放”载体。请先生成《六块Snapshot持久化来源映射表 + 最小DDL差异》，不要自行ALTER正式DDL。RuleSetVersion/ParameterSetVersion的IsDefault必须删除，默认真相只在StrategyProfileVersion。
>
> Snapshot必须补PlanningYield，并让SolverStrategy/CandidateGuardrail来自真实版本内容，不能继续用空/default对象。Snapshot必填内容为空、损坏或非法时必须失败，不得静默回退空Block。
>
> 正式Publish必须强制先Validate；把DemandPriorityValidator、Sort/TieBreak字段合法性、PlanningYield、SolverMode、OnTime、Candidate Guardrail、Split/Overlap等必要校验纳入发布门槛。
>
> 补StrategyProfileVersion发布、校验、有效期、RunType、默认PUBLISHED歧义检查与Run引用追溯。不要反向要求2号位重写已有主流程或固定SQL。
>
> Demand Priority补DueDate/IssueDate；继续保持“CalculationLayer由2号位先分层，Segment→First-match→Segment Sort→Stable Tie-break”，禁止PriorityScore。
>
> 补3号位自己的ExpectedDomainKeysJson、Candidate最小确认/激活、FAILED新Run等生命周期边界；不得重写2号位SchedulingOrchestrator，不得建MultiDomain Candidate。
>
> A-2 Snapshot可变集合按P1做IReadOnlyList/防御Copy即可；A-3弱类型Value不要在V1重构成复杂AST；B-8不要凭空创造100/50等业务上限。
>
> 完成后按R01～R22补测试并重新提交。
>
> 禁止新增：3→5运行时决策接口、5号位Priority/Pegging插件、全局PriorityScore、DSL/脚本、GENETIC/SIMULATED_ANNEALING、UNLOCATED业务容忍阈值、可重排ETA优先级、第二套规则真相。

---

# 12. 最终结论

当前3号位代码不是方向错误，而是：

> **“冻结Snapshot/版本治理/Priority的骨架已经正确，但正式可重放规则内容、PlanningYield/Solver/Candidate完整Snapshot、发布门槛、StrategyProfileVersion治理和生命周期尚未闭合。”**

因此：

> **不通过最终验收；允许继续增量整改；不推倒重写；不重新打开业务冻结。**

最重要的是：

> **本轮3号位整改不产生任何新的2号位业务/代码P0。**

从本报告开始，将其作为3号位当前代码的正式冻结审核基线。
