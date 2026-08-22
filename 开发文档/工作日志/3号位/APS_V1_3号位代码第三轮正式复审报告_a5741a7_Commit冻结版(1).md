# APS V1 3号位代码第三轮正式复审报告（a5741a7 Commit冻结版）

**报告版本**：v1.0  
**审核日期**：2026-08-22  
**审核对象仓库**：`https://github.com/cloudkey2019/SMC_LPS`  
**审核分支**：`main`  
**锁定 Commit**：`a5741a7afd055e718479394ce81670b0666f45c8`  
**短 SHA**：`a5741a7`  
**Commit 说明**：`第三次推送代码`  
**父提交**：`02a6b28`  
**GitHub提交事实**：18 files changed，+1,548 / -118  
**审核性质**：基于冻结业务/技术基线、上一轮3号位正式审核基线及当前完整代码状态的第三轮正式复审。  
**当前结论**：**未通过最终验收；整改方向正确且大量上一轮P0已关闭，但仍有2项P0必须闭环。无需重新打开APS V1业务基线，不需要推倒重写。**

---

# 0. 审核依据与防漂移原则

本轮继续沿用既有冻结文档的权威关系，不重新定义、不重新打开已冻结业务。

主要依据包括：

- 《APS V1最终全部流程与业务基线》
- 《APS Pegging供需承接与分层计算业务说明》
- 《APS有限产能排产与滚动90天计划业务说明》
- 六份冻结技术文档
- 《APS V1关键接口冻结：1↔2、2↔5、2↔3》
- 《APS V1 3号位规则、参数与运行生命周期开发实施包》
- 《APS V1 0号位总体项目验收包》
- 上一轮3号位正式代码复审报告
- 当前冻结DDL：`APS_数据库表结构设计_v5.1.2_冻结对齐版(1).sql`
- 当前 Commit `a5741a7` 完整代码

本轮严格执行：

1. 不因代码实现方便修改冻结业务；
2. 不因3号位AI提出方案就默认接受；
3. 不给2号位新增第二套规则真相或重复接口；
4. 不建设DSL、脚本平台、插件市场、通用规则平台；
5. 已经正确关闭的上一轮问题不无依据重新打开；
6. 真正存在的冻结DDL技术遗漏，可以做**最小技术基线修正**，但不得借机扩大表、接口和职责。

---

# 1. 总体审核结论

本轮代码有明显实质进展。

上一轮多个关键问题已经真实进入代码闭环，包括：

- RuleSetVersion / ParameterSetVersion 的“子默认版本”逻辑已退出；
- Planning Yield（计划良率）已经进入冻结策略快照；
- 需求优先级、锁定、供给、采购四块快照缺失/损坏时已经改为直接失败，不再静默使用空配置；
- RuleSetVersion / ParameterSetVersion / StrategyProfileVersion 正式发布均已经强制执行发布前校验；
- StrategyProfileVersion 默认版本、引用版本必须已发布、生效区间和追溯能力基本形成；
- ScheduleRun 的 ExpectedDomainKeys 规则、失败后新建运行、Candidate最小人工确认和正式激活边界取得实质闭环；
- Candidate正式激活已改成同Domain旧ACTIVE与新Candidate的原子替换方向；
- 新增 Solver Strategy（排程策略）和 Candidate Guardrail（候选方案保护参数）校验器，本身逻辑方向正确。

但仍有两个P0未关闭：

> **P0-01：当前版本Repository/实体仍依赖冻结DDL不存在的字段，同时冻结DDL确实缺少“已发布版本内容可重放”的最小物理载体。**

> **P0-02：Solver Strategy（排程策略）和 Candidate Guardrail（候选方案保护参数）仍然没有真实版本来源，FrozenStrategySnapshot实际返回空对象；新建的两个校验器也尚未接入正式发布链。**

因此当前不能判定3号位完成。

### 本轮最终状态

| 分类 | 数量/状态 |
|---|---|
| 已关闭的上一轮核心问题 | 多项，见第3章 |
| 当前P0 | **2项** |
| 当前P1 | **2项** |
| 是否需修改业务基线 | **否** |
| 是否需最小DDL技术修正 | **是，建议直接采用方案A** |
| 是否需要2号位重新设计主流程 | **否** |
| 是否允许3号位自行扩表 | **否** |
| 是否建议重构规则平台 | **否** |
| 最终验收 | **不通过，继续增量整改** |

---

# 2. 当前代码事实确认

## 2.1 当前Commit确认为本轮审核对象

GitHub 当前提交：

- Commit：`a5741a7afd055e718479394ce81670b0666f45c8`
- Commit说明：第三次推送代码
- Parent：`02a6b28`
- 修改18个文件

本轮代码变化重点集中在：

- `CandidateGuardrailValidator.cs`
- `RunLifecycleService.cs`
- `SolverStrategyValidator.cs`
- `IPlanVersionRepository.cs`
- `PlanVersionRepository.cs`
- 生命周期及策略校验相关测试
- 3号位工作日志和联调文档

同时本轮不是只看Diff；对与当前P0直接相关的既有正式代码也进行了复核，包括：

- `FrozenStrategySnapshotProvider.cs`
- `GovernanceVersionService.cs`
- `RuleSetVersionRepository.cs`
- `ParameterSetVersionRepository.cs`
- StrategyProfileVersion相关治理代码
- FrozenStrategySnapshot相关测试

---

# 3. 上一轮问题关闭情况

## PASS-01：RuleSetVersion / ParameterSetVersion 不再拥有“默认版本真相”

**状态：关闭。**

上一轮要求：

> 默认运行策略包的唯一真相只能是 StrategyProfileVersion，不允许 RuleSetVersion、ParameterSetVersion 自己再各有一个 IsDefault。

当前代码复核未再发现：

- RuleSetVersion.IsDefault正式治理；
- ParameterSetVersion.IsDefault正式治理；
- GetDefaultByRuleSet；
- GetDefaultByParameterSet；
- 发布时对子版本清默认等第二套默认真相。

当前默认选择已经收敛到 StrategyProfileVersion。

**结论：保持关闭，不得重新打开。**

---

## PASS-02：Planning Yield（计划良率）已进入冻结快照

**状态：关闭。**

`FrozenStrategySnapshotProvider` 当前把 Planning Yield 纳入 Procurement（采购参数块）一起反序列化。

这符合冻结接口的核心要求：

> 2号位从3号位冻结参数中取得计划良率，再形成 PlannedProcessQty；1号位只消费加工数量，不自行重新计算计划良率。

**结论：保持。**

---

## PASS-03：四块已有真实来源的快照已改成“缺失/损坏即失败”

**状态：关闭。**

当前 Provider 对以下四块：

1. Demand Priority（需求优先级）
2. Lock（锁定/保护参数）
3. Supply（供给选择参数）
4. Procurement（采购参数，含Planning Yield）

均已经：

- 空值失败；
- JSON损坏失败；
- 反序列化为空失败；
- 不再静默创建空Block。

这是正确修复。

> 需要注意：本结论只覆盖这四块，不代表 Solver Strategy / Candidate Guardrail 已关闭，见P0-02。

---

## PASS-04：正式发布已强制经过发布前校验

**状态：主体关闭，但六块完整校验仍受P0-02影响。**

当前 `GovernanceVersionService` 中：

- 发布 RuleSetVersion 前先 `ValidateRuleSetVersionForPublishAsync`；
- 发布 ParameterSetVersion 前先 `ValidateParameterSetVersionForPublishAsync`；
- 发布 StrategyProfileVersion 前先 `ValidateStrategyProfileVersionForPublishAsync`；
- 校验失败直接拒绝发布。

同时已经校验：

- 版本状态；
- 生效区间；
- 需求排序配置；
- Lock；
- Supply；
- Procurement；
- Planning Yield；
- 采购LT；
- 仓库到可用时间偏移；
- 逾期Margin；
- StrategyProfileVersion引用的Rule/Parameter版本必须为PUBLISHED。

这说明“Publish必须先Validate”的主框架已经建立。

但当前新建的：

- `SolverStrategyValidator`
- `CandidateGuardrailValidator`

**尚未接入正式 ParameterSetVersion 发布链**，因此六块完整发布校验仍需要随P0-02一并收口。

---

## PASS-05：StrategyProfileVersion治理主链已基本闭环

**状态：关闭。**

当前代码已经具备：

- 引用 RuleSetVersion / ParameterSetVersion；
- 被引用版本必须PUBLISHED；
- 生效时间区间校验；
- 默认版本冲突校验；
- 发布后状态追溯；
- RunType下默认策略包解析；
- 默认PUBLISHED策略包歧义拒绝；
- Run使用的Strategy/Rule/Parameter版本追溯。

这一方向符合冻结设计：

> StrategyProfileVersion 是一次Run选择哪套规则和参数的运行级组合真相。

---

## PASS-06：默认策略包解析已属于3号位治理，不应再要求2号位实现第二套默认SQL

**状态：本轮明确冻结。**

当前3号位代码已经提供：

`ResolveDefaultStrategyProfileVersionAsync(runType, asOf, ...)`

它负责：

- 按RunType找默认PUBLISHED策略包；
- 按EffectiveFrom / EffectiveTo过滤；
- 无候选返回null；
- 多候选直接报歧义。

因此3号位AI本轮同步消息中提出：

> “默认版本取数SQL由2号位实现”

**不应采纳。**

这会形成第二套默认策略选择真相，违反职责收敛。

正确边界：

- 3号位负责“哪个StrategyProfileVersion是本次可用默认版本”；
- 2号位在Run启动时按既定服务/接口取得并装载一次冻结快照；
- 2号位不得再自行写另一套“默认版本选择SQL”。

这不需要新增业务，也不需要重新讨论接口。

---

## PASS-07：ExpectedDomainKeys / FAILED恢复 / Candidate确认与激活取得实质闭环

**状态：关闭。**

当前 `RunLifecycleService` 已体现：

### FULL运行
- ExpectedDomainKeys至少1个；
- 重复DomainKey拒绝。

### Candidate
- 确认动作只记录最小人工确认事实；
- 确认本身不直接激活；
- 正式激活必须先确认；
- `INSERT_ORDER_WHATIF` 不允许激活；
- Candidate激活采用同Domain旧ACTIVE归档 + 新Candidate转ACTIVE的原子替换方向。

### FAILED恢复
- 不把历史FAILED改回RUNNING；
- 恢复新建ScheduleRun；
- 保留历史追溯。

均符合冻结口径。

---

## PASS-08：Candidate Guardrail校验器本身没有越权

**状态：通过。**

新增 `CandidateGuardrailValidator` 只负责参数合法性，例如：

- 正常/软/本地硬时间阈值为正；
- Normal ≤ Soft ≤ LocalHard；
- 受影响Task警戒百分比0～100；
- 修复次数、传播轮次、候选资源数、拆分候选数非负。

没有：

- 自己排Task；
- 自己决定影响范围；
- 修改2号位Pegging；
- 修改1号位Solver结果。

属于3号位合理职责。

---

# 4. P0-01：版本内容物理落点与冻结DDL仍未闭环

**状态：P0，未关闭。**

这是本轮最优先问题。

## 4.1 当前代码事实

当前 `RuleSetVersionRepository` 的正式INSERT/UPDATE直接使用：

- `DemandPriorityJson`
- `UpdatedAt`
- `UpdatedBy`
- `Remarks`

当前 `ParameterSetVersionRepository` 的正式INSERT/UPDATE直接使用：

- `LockJson`
- `SupplyJson`
- `ProcurementJson`
- `UpdatedAt`
- `UpdatedBy`
- `Remarks`

而当前冻结DDL `v5.1.2` 中：

### RuleSetVersion
正式字段是：

- Id
- RuleSetId
- VersionCode
- Status
- EffectiveFrom
- EffectiveTo
- PublishedAt
- PublishedBy
- ApprovedAt
- ApprovedBy
- CreatedAt
- CreatedBy

### ParameterSetVersion
同样只有治理元数据字段。

冻结DDL中**没有**：

- DemandPriorityJson
- LockJson
- SupplyJson
- ProcurementJson
- UpdatedAt
- UpdatedBy
- Remarks

因此：

> 当前3号位Repository如果直接连接按冻结DDL建设的正式APS库，会发生字段不匹配。

---

## 4.2 当前代码还有一个反向遗漏

当前Repository虽然使用了多项DDL不存在的字段，却没有在INSERT/UPDATE中完整保存冻结DDL已经存在且服务层实际使用的：

- EffectiveFrom
- EffectiveTo
- ApprovedAt
- ApprovedBy

这会造成：

> Application层校验“生效区间/审批信息”，但Repository新建或修改版本时没有把这些事实完整落库。

因此不能只理解成“DDL少几个JSON字段”。

这是：

> **代码与冻结DDL双向不一致。**

---

## 4.3 对3号位AI本轮声明的审核

3号位AI同步消息写道：

> “3号位已遵守承诺：未ALTER、未扩写依赖非冻结字段的Repository SQL。”

其中“未自行ALTER正式DDL”可以认可。

但是：

> **“未扩写依赖非冻结字段的Repository SQL”与当前源码事实不一致。**

因为当前Repository SQL明确直接依赖：

- DemandPriorityJson；
- LockJson；
- SupplyJson；
- ProcurementJson；
- UpdatedAt/UpdatedBy；
- Remarks。

因此这条自述不能作为验收证据。

---

# 5. 对DDL A/B/C方案的正式裁决建议

## 5.1 是否需要修改业务基线？

**不需要。**

这是冻结技术实现中的真实物理落点缺口：

冻结业务已经明确要求：

> 已发布规则/参数版本不可变，并且历史Run可以按StrategyProfileVersionId恢复当时完整规则参数。

当前DDL没有给出完整“发布内容可重放”载体。

所以这是：

> **冻结技术基线的最小修正，不是重新打开业务。**

---

## 5.2 本报告建议0号位直接裁决：采用方案A

### 方案A：每张版本表只增加一个通用发布内容快照字段

建议：

### RuleSetVersion
最小新增：

`ContentSnapshotJson NVARCHAR(MAX) NULL`

用于保存该RuleSetVersion发布时的完整规则内容。

### ParameterSetVersion
最小新增：

`ContentSnapshotJson NVARCHAR(MAX) NULL`

用于保存该ParameterSetVersion发布时的完整参数内容。

字段名可由开发统一命名，但业务语义固定：

> **发布内容快照/可重放载体。**

---

## 5.3 内容归属建议

### RuleSetVersion.ContentSnapshotJson

至少承载：

- Demand Priority（需求优先级规则）；
- 后续属于RuleSet的其它冻结规则内容。

### ParameterSetVersion.ContentSnapshotJson

承载：

- Lock / Demand Protection（锁定/需求保护参数）；
- Supply（供给参数）；
- Procurement（采购参数）；
- Planning Yield（计划良率）；
- Solver Strategy（排程策略）；
- Candidate Guardrail（候选方案保护参数）。

### StrategyProfileVersion

**不改核心结构。**

仍然只负责：

`StrategyProfileVersion`
→ `RuleSetVersionId`
→ `ParameterSetVersionId`

一次Run绑定StrategyProfileVersionId后，即可完整重放当时两份内容快照。

---

## 5.4 为什么不推荐B

方案B需要：

- 更多主题表；
- 更多版本外键；
- 更复杂版本关系；
- 更大的DDL变化。

对V1没有必要。

容易重新走向：

> 每个规则主题一套独立版本平台。

与V1“六张治理表 + 最小必要复杂度”方向不符。

**本轮不推荐。**

---

## 5.5 为什么不推荐C

当前C本质上继续保留/扩大：

- DemandPriorityJson；
- LockJson；
- SupplyJson；
- ProcurementJson；
- SolverStrategyJson；
- CandidateGuardrailJson……

这会让版本表持续增加主题专用列，并固化当前代码与冻结DDL漂移。

虽然短期代码改动少，但长期会不断把新参数变成新列。

与之前已经冻结的“不要为了每个业务主题继续扩版本表”方向不一致。

**本轮不推荐。**

---

## 5.6 DDL最小变更边界

本轮只建议增加：

1. `RuleSetVersion.ContentSnapshotJson`
2. `ParameterSetVersion.ContentSnapshotJson`

不要借此新增：

- 独立Snapshot表；
- 第二套版本号；
- RuleCondition / RuleAction表；
- DSL；
- 插件注册表；
- 每主题专用版本表。

同时Repository应回归冻结字段：

- 保存EffectiveFrom / EffectiveTo；
- 保存ApprovedAt / ApprovedBy（按现有治理流程适用）；
- 不为了迁就当前代码再给版本表增加UpdatedAt/UpdatedBy/Remarks。

### ChangeReason如何处理

V1不建议再为此扩版本表。

当前已有 `GovernanceAuditLog`，可继续用于：

- 操作人；
- 操作时间；
- 发布；
- 备注/变更原因。

避免因为审计再次扩DDL。

---

# 6. P0-02：Solver Strategy和Candidate Guardrail仍不是“冻结版本真相”

**状态：P0，未关闭。**

这是本轮第二个P0。

## 6.1 当前代码事实

`FrozenStrategySnapshotProvider` 当前六块中：

前四块：

- DemandPriority
- Lock
- Supply
- Procurement

有真实版本JSON来源。

但是：

### Solver Strategy
当前直接：

`new SolverStrategyBlock()`

### Candidate Guardrail
当前直接：

`new CandidateGuardrailBlock()`

源码注释本身也明确标记：

> DDL方案确认前暂无真实版本来源，保持空对象。

所以当前：

> 数据库虽然记录了某个 StrategyProfileVersionId，但该Run真正使用的正排/倒排/混合、瓶颈、准交目标、拆批、Setup、Stage重叠、Candidate时间阈值等，并不能按该版本重放。

这直接违反“一次Run一份冻结策略真相”。

---

## 6.2 新增Validator不等于问题关闭

本次增加：

- `SolverStrategyValidator`
- `CandidateGuardrailValidator`

方向正确。

但是当前 `GovernanceVersionService` 正式发布链中：

- 没有调用 `SolverStrategyValidator`；
- 没有调用 `CandidateGuardrailValidator`。

原因也很清楚：

> 当前这两块尚无真实发布内容来源。

因此不能把“Validator类已写好”视为：

> Solver Strategy / Candidate Guardrail 已完成。

---

## 6.3 R14～R17当前不能判通过

对应冻结验收：

- R14：MIXED Solver Strategy可被1号位读取；
- R15：On-time Target取值正确；
- R16：Candidate 60/90/180等参数在Snapshot中正确；
- R17：受影响范围阈值只作Warning，不截断正确性。

当前Provider返回空Block。

因此：

> **R14～R17仍未形成真实版本值重放。**

3号位AI报告写“18/22”，从代码状态上看，与“R14～R17尚未闭环”的判断一致。

但是本次审查没有在本地实际执行 `dotnet test`，因此：

> **本报告只确认测试源码和代码证据，不把开发方自报的“测试全绿”升级为审核方实测结论。**

---

# 7. P0-01与P0-02如何一次解决

采用方案A后，建议一次收口，不要再分散改。

## 7.1 发布阶段

RuleSetVersion发布：

1. 从主题维护对象装配规则内容；
2. 校验；
3. 形成 RuleSet 内容快照；
4. 写入 `ContentSnapshotJson`；
5. 状态转PUBLISHED；
6. PUBLISHED后内容不可原地修改。

ParameterSetVersion发布：

1. 装配 Lock；
2. Supply；
3. Procurement；
4. Planning Yield；
5. Solver Strategy；
6. Candidate Guardrail；
7. 全部校验；
8. 聚合形成 ParameterSet 内容快照；
9. 写入 `ContentSnapshotJson`；
10. 状态转PUBLISHED。

---

## 7.2 Run装载阶段

保持现有接口方向：

`StrategyProfileVersionId`
→ RuleSetVersion.ContentSnapshotJson
→ ParameterSetVersion.ContentSnapshotJson
→ FrozenStrategySnapshot

一次得到：

- Demand Priority；
- Lock；
- Supply；
- Procurement；
- Planning Yield；
- Solver Strategy；
- Candidate Guardrail。

然后：

- 2号位一次Run装载一次；
- 2号位在内存执行Pegging相关规则；
- 1号位消费Solver Strategy；
- 不逐Demand、逐Allocation、逐Task调用3号位。

---

## 7.3 当前四个主题JSON字段如何处理

当前代码中的：

- DemandPriorityJson
- LockJson
- SupplyJson
- ProcurementJson

建议退出版本表正式持久化字段。

改为：

> 在 `ContentSnapshotJson` 中作为结构化子块保存。

这不是删除业务内容，而是统一它们的版本化物理载体。

---

# 8. P1问题

## P1-01：FrozenStrategySnapshotProvider缺少直接的PUBLISHED防御检查

当前Provider按传入的StrategyProfileVersionId：

- 查StrategyProfileVersion；
- 查RuleSetVersion；
- 查ParameterSetVersion；
- 直接装配Snapshot。

本身没有再次确认三个版本均为PUBLISHED。

当前正常默认选择和StrategyProfileVersion发布路径已经有PUBLISHED校验，所以这不是新的业务缺失。

但为了防止未来：

- 手工指定错误VersionId；
- 调用方绕过默认解析；
- 误把DRAFT版本绑定到Run；

建议在：

- Run绑定StrategyProfileVersion时，或
- Provider入口

至少有一个硬校验：

> StrategyProfileVersion、RuleSetVersion、ParameterSetVersion必须为PUBLISHED且处于有效期。

只需要一处权威防御，不要两边建设两套规则。

---

## P1-02：跨号位联调仍需真实端到端证据，但不能把责任倒灌给2号位

3号位AI要求2号位回执六项，这个联调方向可以保留。

但其中应修正：

### 不应要求2号位自行实现
“默认版本取数SQL”。

因为3号位当前已经实现：

`ResolveDefaultStrategyProfileVersionAsync`

正确联调应是：

1. 3号位提供版本解析/冻结快照能力；
2. 2号位在Run启动时调用一次；
3. 2号位缓存并在当前Run内使用；
4. 2号位抽取需要传给5号位的事实参数；
5. 不形成3→5隐形逐笔依赖；
6. 不让2号位再维护第二套默认策略选择SQL。

这属于已有冻结接口落实，不是新增接口。

---

# 9. 对3号位AI本轮问题的正式答复

## 9.1 DDL A/B/C怎么选？

**答复：选A。**

不是重新打开业务，而是对已冻结“版本可重放”要求做最小技术落位。

具体裁决：

- RuleSetVersion增加一个通用发布内容快照字段；
- ParameterSetVersion增加一个通用发布内容快照字段；
- 不新增Snapshot表；
- 不新增版本体系；
- 不给每个主题新增一列；
- StrategyProfileVersion继续只组合RuleSetVersion + ParameterSetVersion。

---

## 9.2 是否需要ChangeReason新字段？

**本轮不需要。**

已有 GovernanceAuditLog 可以承载：

- 谁；
- 何时；
- 做了什么；
- 备注/变更原因。

V1不要为了“更完整”再扩版本表。

---

## 9.3 Solver Strategy / Candidate Guardrail怎么办？

方案A落地后：

统一进入 `ParameterSetVersion.ContentSnapshotJson`。

然后：

- 发布前调用对应Validator；
- Provider真实反序列化；
- 缺失/损坏直接失败；
- 不再 `new ...Block()` 空对象。

---

## 9.4 2号位是否需要重新确认契约？

**不需要重新确认业务契约。**

只需要完成代码联调。

尤其：

> 不让2号位重新决定默认策略包选择规则，也不让2号位再写第二套默认版本SQL。

---

# 10. 本轮已关闭项——下一轮不得无依据重新打开

下一轮继续保护以下结论：

1. 需求排序继续：
   - 计算层；
   - 有序优先规则段；
   - 第一命中；
   - 段内排序；
   - 稳定键；
2. 禁止恢复全局PriorityScore；
3. 2号位执行实际Demand排序；
4. 2号位执行Supply选择；
5. 2号位执行Demand Protection；
6. 5号位只提供复杂事实；
7. FrozenFactParameters由2号位抽取；
8. Manual ETA > ERP ETA > Default LT；
9. 不建DSL/脚本/插件市场；
10. 不建MultiDomain Candidate；
11. RuleSetVersion / ParameterSetVersion不再各自拥有默认版本真相；
12. Planning Yield已进入冻结快照结构；
13. 四块已有来源的Snapshot坏配置明确失败；
14. Candidate确认与激活分离；
15. FAILED恢复必须新建Run；
16. StrategyProfileVersion仍是Run级策略包组合真相。

---

# 11. 下一轮只审清单

下一次3号位提交后，只重点复核：

## P0-01收口
1. 冻结DDL是否按方案A完成最小同步；
2. RuleSetVersion / ParameterSetVersion是否各只有一个通用发布内容Snapshot载体；
3. Repository是否完全对齐新冻结DDL；
4. EffectiveFrom / EffectiveTo等正式字段是否真实持久化；
5. 非冻结UpdatedAt/UpdatedBy/Remarks是否退出版本表依赖。

## P0-02收口
6. Solver Strategy是否有真实版本来源；
7. Candidate Guardrail是否有真实版本来源；
8. 两个Validator是否进入ParameterSetVersion正式发布校验；
9. Provider是否不再返回空Block；
10. Snapshot坏配置是否六块全部统一失败；
11. R14～R17是否有具体值重放断言。

## 联调
12. 2号位是否一次Run装载一次；
13. 不新增2号位“默认版本SQL第二套真相”；
14. 1号位能取得冻结Solver Strategy；
15. R01～R22最终完整证据。

---

# 12. 最终审核结论

## 12.1 代码方向

当前3号位代码总体方向正确。

没有发现需要：

- 推倒重写；
- 修改APS V1业务；
- 重新设计2号位主流程；
- 新建规则平台；
- 新建第二套Snapshot版本体系。

---

## 12.2 当前仍不能判最终通过的原因

只有两个核心阻断：

### P0-01
**版本内容可重放的物理落点尚未正式冻结并落地，且当前Repository与冻结DDL仍直接冲突。**

### P0-02
**Solver Strategy / Candidate Guardrail仍为空版本内容，无法证明一次Run使用完整冻结策略。**

---

## 12.3 0号位本轮可直接裁决

> **批准方案A作为最小技术DDL修正。**

即：

- RuleSetVersion：增加一个通用发布内容快照字段；
- ParameterSetVersion：增加一个通用发布内容快照字段；
- 其它治理表结构原则不动；
- 不新增表；
- 不新增版本体系；
- 不改变业务；
- 由数据库责任方执行正式DDL同步；
- 3号位按此对齐Repository、发布校验和Snapshot Provider。

---

## 12.4 最终状态

> **本轮不通过最终验收，但可以立即进入最后一轮收口整改。**

> **本轮没有产生新的2号位业务P0，也不允许以3号位整改为理由修改2号位已冻结主流程。**

---

# 13. 审核方特别说明：关于本轮3号位AI总结

本轮3号位AI总结可以作为开发进度说明，但不能替代源码。

其中以下内容与源码一致，可认可：

- 四块Snapshot真实重放；
- Planning Yield已纳入；
- Cache按VersionId；
- Solver/Candidate仍待DDL裁决；
- 生命周期大量问题已整改。

以下一条需要更正：

> “未扩写依赖非冻结字段的Repository SQL”

**与当前源码不一致。**

当前正式Repository SQL仍直接依赖冻结DDL中不存在的主题JSON、Updated、Remarks字段，因此P0-01仍必须保留。

---

**审核人结论：保持现有架构，批准方案A做最小DDL技术对齐，3号位完成两项P0后再做最终验收。**
