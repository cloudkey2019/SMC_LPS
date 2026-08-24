# APS V1 3号位代码第四轮正式复审报告（7455e28 Commit冻结版）

**报告版本**：v1.0  
**审核日期**：2026-08-22  
**审核对象**：3号位——规则、参数、策略版本与运行生命周期  
**仓库**：`https://github.com/cloudkey2019/SMC_LPS`  
**分支**：`main`  
**锁定 Commit**：`7455e28d2a7a1a9525d4523276f2ec8dbce3c172`  
**短 SHA**：`7455e28`  
**Commit说明**：`第四次推送代码`  
**父提交**：`a5741a7`  
**GitHub提交事实**：154 files changed，+2,222 / -170  
**开发方本轮总结**：《APS V1 3号位 工作汇总报告》v1.0（2026-08-22）

---

# 一、最终审核结论

## 1.1 总体判定

> **本轮仍未通过3号位V1最终验收。**

本轮相较 `a5741a7` 有实质进展，上一轮两个核心P0中的多项内部实现已经补上：

- `RuleSetVersion / ParameterSetVersion` Repository 已不再直接依赖 `DemandPriorityJson / LockJson / SupplyJson / ProcurementJson` 等冻结DDL不存在的主题列；
- 两张版本实体已经增加 `ContentSnapshotJson`；
- `FrozenStrategySnapshotProvider` 已从 `ContentSnapshotJson` 读取六块真实内容；
- `Solver Strategy（排程策略）` 和 `Candidate Guardrail（候选方案保护参数）` 不再返回空对象；
- 发布前已经接入 `SolverStrategyValidator` 和 `CandidateGuardrailValidator`；
- 发布时会聚合规则/参数内容形成 `ContentSnapshotJson`；
- R14～R17 单元测试已经增加具体值重放断言。

这些整改方向均正确，应保留。

但是，重新按照真实数据库链路逐层核查后，发现开发方“#1～#20代码层全部完成、集成失败全部是2号位外部原因”的结论**不能成立**。

当前仍有 **3项P0代码问题**：

1. **方案A只完成了“发布后重放”，没有完成DRAFT（草稿）内容的真实持久化，导致真实数据库下发布链无法成立；**
2. **治理CRUD接口仍可绕过状态机和发布校验，甚至可直接创建/修改PUBLISHED版本；**
3. **FrozenStrategySnapshot的“当前有效性防御”放错层级，会阻断历史/基准版本按明确VersionId重放，同时又没有校验StrategyProfileVersion本身。**

此外还有若干P1/P2。

因此：

> **3号位不能结束代码整改，也不能把当前剩余问题全部转给2号位。**

---

# 二、本轮已确认关闭/显著改善的事项

## PASS-01：Repository已对齐方案A的物理字段方向

当前：

`RuleSetVersionRepository`

正式INSERT/UPDATE写入：

- RuleSetId
- VersionCode
- Status
- ContentSnapshotJson
- EffectiveFrom / EffectiveTo
- PublishedAt / PublishedBy
- ApprovedAt / ApprovedBy
- CreatedAt / CreatedBy

`ParameterSetVersionRepository`同样只写正式治理字段 + `ContentSnapshotJson`。

上一轮存在的：

- DemandPriorityJson直接落版本表；
- LockJson直接落版本表；
- SupplyJson直接落版本表；
- ProcurementJson直接落版本表；
- UpdatedAt / UpdatedBy / Remarks直接进入版本表SQL；

已经退出正式Repository SQL。

**结论：这一整改方向正确。**

---

## PASS-02：六块冻结策略已经有真实ContentSnapshotJson来源

当前 `FrozenStrategySnapshotProvider`：

### RuleSet侧

从：

`RuleSetVersion.ContentSnapshotJson.DemandPriority`

装载：

- Demand Priority（需求优先级）

### ParameterSet侧

从：

`ParameterSetVersion.ContentSnapshotJson`

装载：

- Lock
- Supply
- Procurement（含Planning Yield）
- SolverStrategy
- CandidateGuardrail

并且：

- 子块缺失 → 失败；
- JSON损坏 → 失败；
- 反序列化为空 → 失败；
- 不再静默回退空Block。

上一轮：

```text
SolverStrategy = new SolverStrategyBlock()
CandidateGuardrail = new CandidateGuardrailBlock()
```

已经退出。

**结论：上一轮P0-02的“Provider真实来源”部分已经实质关闭。**

---

## PASS-03：Solver Strategy / Candidate Guardrail发布前校验已接入

当前 `GovernanceVersionService.ValidateParameterSetVersionForPublishAsync()` 已调用：

- `ValidateSolverStrategyJson`
- `ValidateCandidateGuardrailJson`

并分别进入：

- `SolverStrategyValidator`
- `CandidateGuardrailValidator`

非法配置会加入 `result.Errors` 并阻止正式Publish。

该方向符合冻结要求。

---

## PASS-04：发布时聚合ContentSnapshotJson的方向正确

当前：

### RuleSetVersion发布

```text
DemandPriority
→ BuildRuleSetContentSnapshot()
→ RuleSetVersion.ContentSnapshotJson
→ PUBLISHED
```

### ParameterSetVersion发布

```text
Lock
Supply
Procurement
SolverStrategy
CandidateGuardrail
→ BuildParameterSetContentSnapshot()
→ ParameterSetVersion.ContentSnapshotJson
→ PUBLISHED
```

这符合0号位已经批准的“方案A”：

> 两张版本表各保留一个通用内容载体，不新增Snapshot表，不新增主题专用列，不新增第二套版本体系。

---

## PASS-05：R14～R17单元测试已增加真实值断言

当前 `FrozenStrategySnapshotProviderTests` 已不是只断言 `NotNull`，而是检查：

- SolverStrategy.Mode；
- OnTimeTarget.TargetPercent；
- Setup参数；
- Candidate Normal/Soft/Hard；
- MaxRepairAttempts；
- MaxPropagationRounds；

具体值是否等于对应版本内容。

这是正确的测试补强。

---

## PASS-06：上一轮生命周期/Candidate已关闭项本轮不重新打开

本轮没有发现新证据需要重新打开上一轮已经确认的：

- ExpectedDomainKeys基本规则；
- Candidate确认与激活分离；
- CTP/INSERT_IMPACT_ANALYSIS不得激活；
- 同Domain旧ACTIVE→ARCHIVED、新Candidate→ACTIVE原子替换；
- FAILED恢复新建ScheduleRun；
- StrategyProfileVersion默认真相；
- Priority Segment / First Match / Stable Tie-break；
- 禁止PriorityScore；
- 禁止DSL/插件平台。

这些继续保持冻结。

---

# 三、P0-01：方案A的DRAFT内容持久化链断裂

**状态：P0，3号位代码必须继续修改。**

这是本轮最重要的新发现，也是开发方总结中“DDL一落地，6个集成测试自动转绿”不成立的根本原因。

---

## 3.1 当前代码的逻辑矛盾

方案A当前定义：

- `DemandPriorityJson`
- `LockJson`
- `SupplyJson`
- `ProcurementJson`
- `SolverStrategyJson`
- `CandidateGuardrailJson`

都是**发布装配中间态**，不再作为数据库正式主题列。

正式数据库只新增：

- `RuleSetVersion.ContentSnapshotJson`
- `ParameterSetVersion.ContentSnapshotJson`

这个方向本身没有问题。

问题在于：

> 当前Repository只持久化ContentSnapshotJson，却没有任何代码把DRAFT阶段用户编辑的六块内容保存进ContentSnapshotJson。

---

## 3.2 RuleSetVersion真实断链

当前创建/更新API接收：

`RuleSetVersion`

其中可能包含：

`DemandPriorityJson`

但是 `RuleSetVersionRepository.AddAsync / UpdateAsync` 完全不写：

`DemandPriorityJson`

只写：

`ContentSnapshotJson`

而当前发布流程又是：

```text
GetByIdAsync()
↓
ValidateRuleSetVersionForPublishAsync()
↓
读取 version.DemandPriorityJson
↓
BuildRuleSetContentSnapshot(version)
```

真实数据库重新读取后：

> `DemandPriorityJson` 没有数据库列，也没有从 `ContentSnapshotJson` 反解析回来，因此为空。

结果：

> 发布前校验会报 `EMPTY_DEMAND_PRIORITY`，正式发布无法完成。

---

## 3.3 ParameterSetVersion同样断链

当前DRAFT实体可能有：

- LockJson
- SupplyJson
- ProcurementJson
- SolverStrategyJson
- CandidateGuardrailJson

但是Repository不持久化它们。

重新从数据库GetById以后：

这些字段全部没有真实物理来源。

而正式Publish又先校验这些字段，再聚合成ContentSnapshotJson。

因此：

> 参数集版本真实数据库发布链同样无法成立。

---

## 3.4 当前集成测试源码已经证明这个问题

### DemandPriorityIntegrationTests

测试：

1. 创建 `RuleSetVersion`；
2. 设置 `DemandPriorityJson`；
3. 调 `RuleSetVersionRepository.AddAsync()`；
4. 再从数据库 `GetByIdAsync()`；
5. 断言 `DemandPriorityJson` 非空。

但是当前Repository根本不持久化 `DemandPriorityJson`。

所以：

> 即使2号位明天把ContentSnapshotJson列加到数据库，这3个测试也不会自动转绿。

---

### GovernanceVersionServiceIntegrationTests

`SetupBaseVersionsAsync()`：

- 给RuleSetVersion填DemandPriorityJson；
- 给ParameterSetVersion填Lock/Supply/ProcurementJson；
- 然后调用Repository.AddAsync；
- 随后Service重新从真实数据库GetById并做Validate/Publish。

这些主题JSON不会被Repository保存。

所以：

> DDL补完以后仍会进入“配置为空”失败。

---

### FrozenStrategySnapshotProviderIntegrationTests

当前测试数据仍然：

- 写DemandPriorityJson；
- 写Lock/Supply/ProcurementJson；

却没有正确形成：

- RuleSetVersion.ContentSnapshotJson；
- ParameterSetVersion.ContentSnapshotJson完整五块。

而Provider现在已经只读ContentSnapshotJson。

因此：

> 这批测试同样不是“DDL一加列自动转绿”。

---

## 3.5 开发方总结需要正式修正

开发方写：

> “失败/跳过全部为外部原因，无3号位代码缺陷；2号位落地DDL后A/B/C的6个失败自动转绿。”

**本次审核不认可。**

准确结论：

> 2号位DDL未落地确实是当前第一层阻塞，但DDL落地以后，3号位当前DRAFT持久化/发布链仍会继续失败。

---

# 四、P0-01推荐最小修复方案

本问题**不需要重新打开业务，也不需要新增任何表/列**。

建议继续坚持方案A，但把技术生命周期补完整：

## 4.1 ContentSnapshotJson成为该版本唯一内容真相

### DRAFT阶段

允许编辑：

```text
RuleSetVersion.ContentSnapshotJson
{
  "DemandPriority": {...}
}
```

```text
ParameterSetVersion.ContentSnapshotJson
{
  "Lock": {...},
  "Supply": {...},
  "Procurement": {...},
  "SolverStrategy": {...},
  "CandidateGuardrail": {...}
}
```

DRAFT时：

> ContentSnapshotJson可修改。

### Publish阶段

对ContentSnapshotJson内所有子块：

- 完整校验；
- 必填检查；
- 规范化（必要时）；
- 状态转PUBLISHED。

### PUBLISHED以后

> ContentSnapshotJson不可再修改。

这样：

- 不增加主题列；
- 不增加Snapshot表；
- 不增加第二套真相；
- DRAFT可编辑；
- PUBLISHED可重放；
- 4号位以后有真实维护落点。

---

## 4.2 当前临时主题JSON属性如何处理

可以二选一，均不改变业务：

### 方案A1（更简单）

Service / Controller直接使用结构化内容DTO，最终统一读写 `ContentSnapshotJson`。

`DemandPriorityJson / LockJson / ...` 不再承担Repository实体真相。

### 方案A2

保留这些属性作为API/内存DTO字段，但：

- GET时从ContentSnapshotJson投影出来；
- CREATE/UPDATE时再合并回ContentSnapshotJson；
- Repository永远只存ContentSnapshotJson。

无论采用哪种：

> 数据库只能有一个内容真相。

---

## 4.3 需要同步修改

- RuleSetVersion CRUD；
- ParameterSetVersion CRUD；
- 发布校验；
- 版本Diff；
- 集成测试Setup；
- 4号位后续维护API返回结构。

**不需要2号位新增任何新DDL。**

只需要2号位按已经批准的变更申请增加两列。

---

# 五、P0-02：治理CRUD接口可以绕过发布状态机

**状态：P0，3号位必须修改。**

这一问题直接否定了开发方所称：

> “Publish正式无绕过路径。”

`PublishXXXAsync()`方法内部确实强制Validate，这一点是正确的。

但是Web API存在另一条绕过路径。

---

## 5.1 创建API没有强制DRAFT

当前：

```text
POST rule-set/version
POST parameter-set/version
POST strategy-profile/version
```

都直接接收实体对象。

代码只设置：

`CreatedAt`

却没有强制：

`Status = DRAFT`

因此客户端理论上可以直接提交：

```text
Status = PUBLISHED
```

Repository会直接保存。

这意味着：

> 可以完全绕过Publish服务和发布前校验。

---

## 5.2 更新API没有禁止PUBLISHED

三个Update接口注释都写着：

> “仅限非PUBLISHED状态”

但是实现只有：

1. 查existing；
2. 判断是否存在；
3. `version.Id = versionId`；
4. 直接Repository.UpdateAsync。

没有检查：

`existing.Status`

因此：

> 已PUBLISHED版本仍可直接通过PUT原地修改。

这违反冻结红线：

> PUBLISHED业务内容不可原地修改；历史Run使用的版本必须保持不变。

---

## 5.3 更新API还允许直接改Status

Repository.UpdateAsync会更新：

- Status；
- ContentSnapshotJson；
- EffectiveFrom / EffectiveTo；
- PublishedAt / PublishedBy；
- ApprovedAt / ApprovedBy；

客户端可以通过普通Update直接把：

```text
DRAFT → PUBLISHED
```

也可以修改：

```text
PUBLISHED → 其它状态
```

而不经过治理Service。

所以：

> 当前“Publish必经Validate”只保护Publish接口本身，没有保护整个系统的版本状态机。

---

## 5.4 必须修复

最小整改：

1. Create服务端强制 `Status=DRAFT`；
2. 普通CRUD不得接受客户端直接决定PublishedAt/PublishedBy等治理字段；
3. Update必须根据existing.Status执行冻结约束；
4. PUBLISHED历史版本禁止通过普通Update修改内容；
5. DRAFT→PUBLISHED必须且只能经过专用Publish服务；
6. DISABLED/ARCHIVED等历史版本不得被普通编辑接口改写业务内容；
7. 增加绕过测试：
   - Create传PUBLISHED → 仍只能DRAFT或直接拒绝；
   - PUT DRAFT时试图Status=PUBLISHED → 拒绝；
   - PUT PUBLISHED内容 → 拒绝；
   - 只有Publish接口可转PUBLISHED。

不需要新增数据库对象。

---

# 六、P0-03：Snapshot当前“有效性防御”破坏历史版本重放

**状态：P0，需要修正。**

本轮新增 `EnsureLoadable()`：

对RuleSetVersion和ParameterSetVersion强制：

- Status必须PUBLISHED；
- EffectiveFrom不能晚于当前时间；
- EffectiveTo不能早于当前时间。

开发方将其定义为：

> “P1-01防御”。

但这个防御层级不正确。

---

## 6.1 冻结要求

冻结版本治理的核心是：

- 新Run不能选择无效/未发布版本；
- 已发布历史版本内容不可修改；
- 历史PlanVersion必须能追溯当时版本；
- Candidate/重排可能继承Base版本使用过的策略；
- 按明确VersionId应能恢复当时不可变内容。

因此：

> “这个版本现在还能不能被新Run选中”

与：

> “按明确VersionId能不能重放历史内容”

是两个不同问题。

---

## 6.2 当前Provider会错误拒绝历史版本

如果某旧Run在8月1日使用：

`ParameterSetVersion V10`

8月20日：

- V10有效期结束；
- 或状态进入DISABLED/ARCHIVED；

历史Run仍然必须能恢复V10的快照内容。

但当前Provider按**当前时间**检查EffectiveFrom/To和Status：

> 会直接拒绝装载。

这样方案A最重要的：

> “历史版本内容可重放”

反而被阻断。

---

## 6.3 当前防御又是不完整的

Provider当前只检查：

- RuleSetVersion；
- ParameterSetVersion；

却没有同样检查：

- StrategyProfileVersion本身。

因此即使站在“新Run装载防御”的角度：

> 这个防御也没有完整覆盖三版本。

---

## 6.4 正确最小边界

建议：

### 新Run绑定/默认选择阶段

负责检查：

- StrategyProfileVersion可用于新Run；
- 引用RuleSetVersion / ParameterSetVersion合法；
- 当前有效期；
- 默认版本无歧义。

这里已有：

`ResolveDefaultStrategyProfileVersionAsync`

以及发布/引用校验，可继续增强而不是再建平台。

### FrozenStrategySnapshotProvider

按明确：

`StrategyProfileVersionId`

负责：

> 读取该不可变版本的真实历史内容。

不应因为它今天已经过期/归档，就无法恢复历史快照。

这不需要新接口体系，只需把“新Run准入”和“历史内容读取”职责分开。

---

# 七、P1-01：集成测试需要按方案A重写，不能等DDL后“自动转绿”

当前单元测试104/104即使属实，也只能证明：

> Mock/In-memory对象下的逻辑可以工作。

例如 `ParameterSetVersionPublishTests`：

直接让Mock Repository返回一个内存对象，其中：

- LockJson；
- SupplyJson；
- ProcurementJson；
- SolverStrategyJson；
- CandidateGuardrailJson；

天然都还在。

所以不会暴露真实数据库“Add后丢主题内容”的问题。

本轮必须补/修真正的集成链：

```text
Create DRAFT
→ DB重新读取
→ 修改DRAFT
→ DB重新读取
→ Validate
→ Publish
→ DB重新读取
→ FrozenStrategySnapshot
→ 六块具体值一致
→ 历史版本再次读取仍一致
```

这是R01～R17真正的端到端证据。

---

# 八、P1-02：版本Diff仍在读取已经退出正式持久化的主题JSON

当前：

`CompareRuleSetVersionsAsync()`

仍比较：

`DemandPriorityJson`

`CompareParameterSetVersionsAsync()`

仍比较：

- LockJson
- SupplyJson
- ProcurementJson

但方案A正式持久化已经收敛到：

`ContentSnapshotJson`

因此真实数据库重新读取后，Diff很可能只看到空主题字段。

实施包明确要求3号位后端提供：

- Diff；
- Run引用追溯。

所以Diff也必须同步迁移：

> 从ContentSnapshotJson结构化子块比较，而不是继续依赖已退出数据库真相的临时字段。

---

# 九、P1-03：仓库内“3号位和1号位对外契约”仍保留明显旧接口

当前：

`开发文档/契约文档/05_3号位和1号位对外契约.md`

前半部分仍以正式形式写有：

- `ISchedulingEngine.ScheduleAsync(SchedulingInput)`
- 输入是预生成 `Tasks`
- `HistoricalAnchors`
- `AlgorithmType = FIFO / PRIORITY / GENETIC / SIMULATED_ANNEALING`

这与当前冻结接口明显冲突：

正式1↔2仍应是：

`IFiniteCapacityScheduler.SolveAsync(DomainSolveRequest, ...)`

并且V1：

- 2号位不预生成FinalTask；
- 不建设GENETIC/SIMULATED_ANNEALING可选算法平台；
- 90天只有一套有限产能Solver。

虽然该文档后半已经增加新治理契约，但旧章节仍以“对外契约”形式存在。

### 整改

只做文档去旧：

- 删除旧正式契约；
- 或明确标记 `HISTORICAL / DEPRECATED / 不进入V1正式主链`。

**不得因此修改现有冻结接口。**

---

# 十、P2：本次Commit提交大量bin/obj编译产物

本次Commit 154 files changed，其中大量是：

- bin/Debug/net8.0
- obj/Debug/net8.0
- dll
- pdb
- cache

这不会直接造成APS业务错误，但会：

- 放大Commit；
- 干扰代码审核；
- 容易造成二进制噪声；
- 让后续差异审查困难。

建议补 `.gitignore` / 清理编译产物。

**P2，不阻断V1。**

---

# 十一、对3号位AI本轮总结逐项判定

## 11.1 “#1～#20代码层全部完成”

**不认可。**

至少P0-01/P0-02/P0-03仍属于3号位代码问题。

---

## 11.2 “P0-01/P0-02已全部收口”

**只能部分认可。**

### 已收口

- Repository不再写旧主题专用列；
- ContentSnapshotJson实体/Repository已对齐；
- Provider六块真实读取；
- Solver/Candidate Validator已接线；
- 发布聚合快照已实现。

### 未收口

- DRAFT真实内容无法持久化；
- Publish从真实DB重读后拿不到临时主题JSON；
- Diff仍依赖旧临时字段；
- Snapshot历史重放被当前有效期检查阻断；
- CRUD状态机可被绕过。

---

## 11.3 “单元测试104/104全绿”

可以作为：

> **开发方自测证据。**

但不能作为最终验收证据，因为当前测试大量使用Mock实体，没有穿过真实Repository持久化边界。

---

## 11.4 “7个集成失败/跳过全部是外部原因”

**不认可。**

### 确实属于外部阻塞

1. 正式数据库目前尚缺 `ContentSnapshotJson` 两列；
2. `FiniteCapacitySolver` DI属于2号位/1号位集成问题；
3. APS_Auth / ExpectedDomainKeys真实环境缺口属于联调环境问题。

### 但3号位自身也有代码问题

即使DDL落地：

- 当前DemandPriority集成测试仍会因为DemandPriorityJson不持久化而失败；
- Governance发布集成链仍会因为主题JSON重读为空而失败；
- FrozenSnapshot集成测试仍未按ContentSnapshotJson真实结构准备数据。

所以不能说：

> “DDL落地后6个失败自动转绿”。

---

# 十二、对跨号位依赖的正式裁决

## 12.1 ContentSnapshotJson DDL执行

**责任：2号位/数据库责任方。**

继续按已经批准的方案A执行：

- RuleSetVersion + `ContentSnapshotJson NVARCHAR(MAX) NULL`
- ParameterSetVersion + `ContentSnapshotJson NVARCHAR(MAX) NULL`

不新增其它字段/表。

但是：

> 3号位不能等待DDL后就宣告完成；3号位仍需先修正自己的DRAFT/Publish/CRUD/Diff代码。

---

## 12.2 FiniteCapacitySolver DI失败

**不属于3号位代码问题。**

由2号位在现有主流程/DI中修复。

不得借此修改1↔2冻结接口。

---

## 12.3 2号位Snapshot联调

属于正常跨号位联调。

3号位应先把本报告P0关闭，再要求2号位回执。

否则2号位接入的是一个DRAFT/Publish链尚未闭合的Provider来源。

---

## 12.4 1号位Solver Strategy消费联调

属于正常联调。

3号位职责是：

> 提供冻结的Solver Strategy值。

1号位负责消费。

3号位不进入Solver内部。

---

# 十三、0号位对E-1问题的正式裁决

开发方提出：

> `SchedulingMode → SolverStrategyMode` 运行时映射是否由2号位负责？

本报告裁决：

> **不属于3号位继续开发的职责。**

冻结职责是：

```text
3号位
治理并输出冻结Solver Strategy

↓

2号位
一次Run装载 + 组装DomainSolveRequest

↓

1号位
消费策略执行有限产能
```

因此：

- 3号位只保证 `SolverStrategyBlock.Mode` 的业务语义正确；
- 2号位如果现有 `SchedulingContext / SchedulingMode` 与1号位请求类型存在技术类型差异，可以在**现有Request组装/适配边界做一对一映射**；
- 2号位不得重新决定Forward/Backward/Mixed；
- 如果1号位接口已经能直接消费 `SolverStrategyMode`，则根本不需要额外映射；
- 不新增3↔1直接运行时接口；
- 不新增第二套策略配置。

这只是接口适配，不是业务变更。

---

# 十四、本轮P0/P1清单

## P0-01 方案A DRAFT内容持久化断链

3号位必须修改。

---

## P0-02 CRUD可绕过治理状态机/Publish校验

3号位必须修改。

---

## P0-03 Provider当前有效期检查破坏历史版本重放

3号位必须修改。

---

## P1-01 集成测试按方案A完整重写/补齐

3号位必须完成后再提最终验收。

---

## P1-02 Diff迁移到ContentSnapshotJson真实内容

3号位整改。

---

## P1-03 活跃契约文档清理旧1号位接口

3号位文档整改，不改变冻结接口。

---

## 外部阻塞-01

2号位执行已经批准的两列DDL。

---

## 外部阻塞-02

2号位修复FiniteCapacitySolver DI。

---

## 外部联调-03

1号位消费Solver Strategy。

---

# 十五、下一轮只审清单

下一轮不重新打开已经确认正确的：

- Priority Segment；
- First Match；
- Stable Tie-break；
- 禁PriorityScore；
- StrategyProfileVersion默认真相；
- Candidate生命周期；
- FAILED新Run；
- Solver/Candidate Validator基本规则；
- 六块DTO结构；
- ContentSnapshotJson方案A。

只检查：

1. DRAFT ContentSnapshotJson是否真实持久化；
2. Create是否强制DRAFT；
3. Update是否不能越权改Status；
4. PUBLISHED/历史版本是否不可原地修改；
5. Publish是否真正成为唯一转PUBLISHED路径；
6. Validate是否直接基于真实持久化内容；
7. Diff是否基于ContentSnapshotJson；
8. Provider是否能按明确VersionId读取历史不可变内容；
9. 新Run版本合法性是否在正确绑定/选择边界校验；
10. 集成测试是否真正跑通：
   - Create
   - Reload
   - Update
   - Validate
   - Publish
   - Reload
   - Snapshot六块具体值
   - 历史重放
11. 2号位DDL落地后的真实测试回执；
12. 旧契约文档是否去旧。

---

# 十六、最终状态

> **3号位第四轮：未通过最终验收。**

但本轮不是方向失败。

方案A整体方向仍然正确：

> **六张治理表 + 两个ContentSnapshotJson通用内容载体 + StrategyProfileVersion组合 + Run一次冻结Snapshot。**

当前问题是：

> **方案A只做完了“发布/重放的后半段”，还没有把“草稿编辑→真实持久化→发布→不可变历史重放”整个生命周期闭成一条链。**

因此：

- 不推倒重写；
- 不新增表；
- 不新增主题专用列；
- 不修改2号位主流程；
- 不修改APS冻结业务；
- 3号位继续做一次最小增量整改即可。

---

# 附录A：关键源码证据

## A1. Commit

`https://github.com/cloudkey2019/SMC_LPS/commit/7455e28d2a7a1a9525d4523276f2ec8dbce3c172`

---

## A2. ContentSnapshot真实读取

`LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs`

已确认：

- DemandPriority → RuleSetVersion.ContentSnapshotJson；
- Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail → ParameterSetVersion.ContentSnapshotJson。

---

## A3. 发布聚合

`LPS.APS.Application/Services/GovernanceVersionService.cs`

已确认：

- `BuildRuleSetContentSnapshot`
- `BuildParameterSetContentSnapshot`
- Solver/Candidate Validator接线。

---

## A4. DRAFT持久化断链

`LPS.APS.Engine/Repositories/Governance/RuleSetVersionRepository.cs`

只持久化：

`ContentSnapshotJson`

不持久化：

`DemandPriorityJson`

`LPS.APS.Engine/Repositories/Governance/ParameterSetVersionRepository.cs`

只持久化：

`ContentSnapshotJson`

不持久化：

- LockJson
- SupplyJson
- ProcurementJson
- SolverStrategyJson
- CandidateGuardrailJson

而发布Service仍然读取上述临时属性进行校验和聚合。

---

## A5. CRUD绕过

`LPS.APS.Web/Controllers/GovernanceController.cs`

Create：

- 不强制Status=DRAFT。

Update：

- 注释写“仅限非PUBLISHED”；
- 实际没有状态校验；
- 直接调用Repository.UpdateAsync。

---

## A6. 集成测试仍按旧主题JSON写法

- `DemandPriorityIntegrationTests.cs`
- `GovernanceVersionServiceIntegrationTests.cs`
- `FrozenStrategySnapshotProviderIntegrationTests.cs`

仍把主题JSON填在实体临时属性后调用Repository Add，未形成真实ContentSnapshotJson链。

---

## A7. 旧契约残留

`开发文档/契约文档/05_3号位和1号位对外契约.md`

仍包含旧：

- ISchedulingEngine
- Tasks输入
- FIFO/PRIORITY/GENETIC/SIMULATED_ANNEALING

需标记历史/删除，不得覆盖当前冻结 `IFiniteCapacityScheduler.SolveAsync`。
