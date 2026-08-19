# APS V1 2号位增量开发实施包（冻结版）

**版本**：v1.1  
**日期**：2026-08-14  
**适用对象**：2号位及其开发AI  
**文档性质**：基于现有代码的增量整改实施说明，不是新项目重构方案  
**上位依据**：APS V1三份业务冻结文档 + 六份技术冻结文档 +《APS 2号位代码事实复核报告 v1.1 闭环版》

---

## 0. 使用方式与最高原则

本实施包只回答一件事：

> **在2号位现有代码上，哪些保留、哪些退出正式V1主链、哪些必须修改、哪些只做最小新增，以及完成后怎样验收。**

禁止把本实施包理解为“重新按一套新架构开发APS”。

### 0.1 权威顺序

1. 0号位明确冻结的APS V1业务基线；
2. Pegging、有限产能两份专项冻结说明；
3. 六份技术冻结文档；
4. 本增量实施包；
5. 2号位当前代码实现。

代码和上位冻结文档冲突时，优先做**最小代码整改**，不得反向修改冻结业务。

### 0.2 2号位整改总原则

> **保留外壳，退出冲突旧路径，补齐数量闭环、事务闭环和接口闭环。**

特别禁止：

- 为了“架构漂亮”重写 `SchedulingOrchestrator`；
- 重做已经存在的 `ScheduleRun / PlanVersion` 外壳；
- 另起一套Pegging Engine；
- 重建第二套有限产能Solver；
- 为PI Position、Demand Protection、Frozen/Firm、跨Domain等重新建设平台型架构；
- 因现有代码实现困难而要求0号位重新讨论已冻结业务。

只有发现“冻结业务规则在真实代码/数据事实下必然互相矛盾，且无法通过实现修正”时，才允许明确提出：

> **需要重新打开冻结决策 F-xxx。**

除此之外不得重新讨论业务方案。

---

# 一、当前代码事实基线

本节以《APS 2号位代码事实复核报告 v1.1 闭环版》为基线。历史行号只用于帮助定位，开发前应以当前冻结分支全文搜索实际调用点，不得只依赖旧行号。

## 1.1 当前正式主链事实

当前代码主入口仍以：

`SchedulingOrchestrator.RunSchedulingAutoAsync`

为核心。

已有能力包括：

- PlanVersion状态管理；
- SchedulingContext装载；
- Hangfire定时触发；
- 当前版本Task/Pegging清理与落盘框架；
- `SupplyPool`及`RemainingQty`余额原型；
- `LoadMESProgressAsync` / `StageProgressSnapshot`装载；
- `IFiniteCapacityScheduler.SolveAsync`接口；
- `DomainSolveRequest / DomainSolveResult`方向；
- `PeggingLedgerEntry` DTO；
- `AllocationTaskShareDto`；
- 已有事务持久化模板。

当前仍存在的正式主链问题包括：

1. `DefaultBatchSplitter`仍在正式主链提前拆Task；
2. Solver前已经生成并落盘Operation Task；
3. `IFiniteCapacityScheduler`目前正式实现仍是PassThrough性质的Stub；
4. 其后又调用旧`FiniteCapacitySolver.Solve()`，形成两条排程路径；
5. Supply侧已有余额原型，但没有DemandBalance；
6. Demand/Supply扣减没有形成统一原子Allocation；
7. `AllocationSequence`生成时机不符合冻结口径；
8. HardLock / ExecutionLock未真正进入Allocation；
9. Ledger DTO存在，但没有形成完整`LedgerEntries.Add`闭环；
10. `AllocationTaskShare`方向存在，但持久化仍有一对一假设；
11. `InventoryBalance.AllocatedQty`存在运行期UPDATE；
12. `PeggingSupplyAllocation`存在事务内外重复写入风险；
13. `FrozenZoneSnapshot`调用存在，但实现是TODO骨架；
14. `VirtualInventoryBalance`为预留注入对象，不应发展成V1持久平台；
15. Stage/WIP装载已有，但PI Position尚未形成完整总量/位置闭合；
16. Pegging失败后仍可能继续进入后续排程；
17. 5号位调用曾存在“传空候选/调用时机无效”的问题。

---

# 二、第一类：直接保留，不要重写

以下是2号位现有代码的**保护区**。

| 现有对象/能力 | 处理方式 | 允许调整范围 |
|---|---|---|
| `SchedulingOrchestrator` | **保留** | 调整阶段顺序、正式调用路径和错误终止逻辑；不换总入口 |
| Hangfire调度外壳 | **保留** | 只需适配冻结后的Run/Domain执行语义 |
| `ScheduleRun / PlanVersion`现有框架 | **保留** | 补冻结状态语义；具体创建分钟点不强制重构 |
| `SchedulingContext`装载框架 | **保留** | 增加冻结所需Context项 |
| `SupplyPool` | **优先复用增强** | 演化为本次运行SupplyBalance，不另建第二套供给引擎 |
| `RemainingQty`扣减原型 | **保留增强** | 纳入原子Allocation和回滚 |
| `LoadMESProgressAsync` / Stage进度装载 | **保留输入能力** | 不再由1号位直接据此扣Task；作为PI Position / Execution事实输入 |
| `IFiniteCapacityScheduler.SolveAsync` | **保持接口方向稳定** | 扩充请求/结果字段；不重新发明第二套Solver接口 |
| `DomainSolveRequest / Result` | **保留扩展** | 对齐冻结后的逻辑生产需求、FinalTask、TaskShare、Explanation |
| `PeggingLedgerEntry` DTO | **保留** | 补真实生成逻辑 |
| `AllocationTaskShareDto` | **保留** | 改成真实多对多持久化 |
| 当前事务模板 | **保留结构** | 扩大到正确的单Domain原子边界 |
| 现有数据Loader / ext视图装载模式 | **保留** | 只补新冻结输入，不直连源系统 |

### 保护红线

如果上述能力已经可以满足冻结契约，不允许因为：

- 命名不够漂亮；
- 想换框架；
- 想统一成“更标准”的DDD/CQRS；
- AI建议重构；
- 想一次性解决V2；

而重写。

---

# 三、第二类：代码可保留，但必须退出正式V1主链

这些代码不要求立即物理删除，避免无谓风险；但**正式V1执行路径不得再依赖它们**。

## 3.1 `DefaultBatchSplitter`

### 当前事实
仍在正式主链中由 `GenerateAndPersistTasksAsync` 调用，Solver前提前拆Task。

### V1处理
- 类/文件可以暂时保留；
- 从正式FULL/Candidate主链移除调用；
- 不再由2号位Solver前提前拆Operation Task；
- 拆批/合批的最终物理决定归1号位Solver。

### 验收
正式FULL和Candidate运行调用链中：

> `DefaultBatchSplitter.Split()` = **0次**

---

## 3.2 旧外层 `FiniteCapacitySolver.Solve()`

### 当前事实
在 `IFiniteCapacityScheduler.SolveAsync` 之后仍存在第二次旧求解调用。

### V1处理
- 旧类可以暂时保留；
- 正式生产运行路径只允许进入一次1号位有限产能入口；
- 不允许“PassThrough一遍 + 旧Solver再排一遍”。

### 验收
一笔Domain运行只能出现一个权威Solver Result。

---

## 3.3 `PassThroughSchedulerStub`

### 当前事实
接口方向正确，但只是Stub，不能作为正式有限产能实现。

### V1处理
- 可保留用于接口联调/单元测试；
- 正式环境必须由1号位真实实现替换；
- 2号位不能在Stub后再自己修正Resource/Start/End来“补成正式结果”。

---

## 3.4 `FrozenZoneSnapshot`

### 当前事实
Repository已注入，主链调用存在，但方法体为TODO骨架。

### V1处理
- **停止继续实现平台**；
- 正式主链不再生成或依赖FrozenZoneSnapshot；
- 方法/表如果现实数据库已有，不要求DROP；
- Firm/Frozen通过上一ACTIVE仍有效任务事实 → 本次不可移动锚点进入Solver。

### 验收
FULL/Candidate成功不依赖FrozenZoneSnapshot存在或有数据。

---

## 3.5 `VirtualInventoryBalance`

### V1处理
- 不发展为持久化状态机；
- 如果代码已注入但未调用，可暂保注册或在安全确认后清理；
- 跨Domain Quantity-Time只存在于当前运行内存/接口上下文。

---

# 四、第三类：必须修改的核心主链

# 4.1 主链顺序改造

正式V1主链应收敛为：

```text
SchedulingOrchestrator
    ↓
Run / Domain上下文
    ↓
加载冻结规则参数 + 当前事实切片
    ↓
Demand构建
    ↓
Supply构建
    ↓
PI Position / 跨厂 / 采购VMI等复杂事实
    ↓
Pegging：DemandBalance + SupplyBalance + Lock + Allocation
    ↓
形成逻辑生产需求（不是FinalTask）
    ↓
IFiniteCapacityScheduler.SolveAsync
    ↓
1号位返回 FinalTask + TaskShare + Task物理依赖 + Explanation + Unscheduled
    ↓
2号位校验
    ↓
单Domain事务统一持久化
    ↓
PlanVersion发布/失败
    ↓
ScheduleRun汇总
```

禁止再出现：

```text
2号位先生成最终Task
→ Split
→ Pegging
→ PassThrough
→ 旧Solver再排
```

---

# 五、Pegging内存核心：优先复用SupplyPool，不重新造引擎

## 5.1 SupplyBalance

### 处理原则
优先在现有`SupplyPool / SupplyLedgerEntry.RemainingQty`上增强。

需要补：

- Supply唯一身份；
- OriginalQty；
- RemainingQty；
- AvailableTime；
- SupplyType；
- PhysicalSourceKey；
- Confidence / Commitment；
- Eligibility结果；
- Lock份额；
- 本次Allocation列表。

### 核心红线
同一物理数量在同一PlanVersion中只能有一个Supply身份。

例如同一PI：

> PI总量、PI的XC、PI的在途、PI的Stage WIP

不能被当成四份Supply重复消费。Position只是这个PI Supply内部位置解释。

---

## 5.2 DemandBalance

当前没有正式DemandBalance，需要**最小新增**。

建议仅做运行时内存对象，不新增持久平台。

最小字段：

- DemandKey；
- OrderId / ProductionInstructionNo等稳定业务锚点；
- MaterialId；
- RequiredQty；
- RemainingQty；
- DueDate；
- DomainKey；
- CalculationLayer；
- DemandType；
- Priority排序上下文；
- 是否严格绑定/保护；
- 来源Allocation追溯。

### 红线
DemandBalance只负责本次运行数量真相，不建设跨版本Demand状态机。

---

## 5.3 原子Allocation

一笔供需Allocation成功时，必须在同一内存动作中完成：

1. 校验Demand还有余额；
2. 校验Supply还有余额；
3. 校验资格；
4. 校验Strict Binding；
5. 校验Demand Protection；
6. 校验Execution不可逆事实；
7. 扣DemandBalance；
8. 扣SupplyBalance；
9. **此时生成AllocationSequence**；
10. 生成逻辑Allocation/LedgerEntry。

任何一步失败：

> Demand/Supply余额均不得部分修改。

---

## 5.4 AllocationSequence

必须改成：

> **Allocation真正成功时生成。**

禁止：

- 持久化时临时编号；
- Task生成后再编号；
- 一次失败Allocation占用有效Sequence后又当成功记录使用。

Sequence用于：

- PSA；
- AllocationTaskShare；
- Explanation追溯；
- Candidate Base vs Candidate变化识别。

---

## 5.5 LedgerEntries

现有`PeggingLedgerEntry` DTO继续用。

需要补齐真正的：

`LedgerEntries.Add(...)`

调用。

V1不因此新增一张新的`PeggingAllocationLedger`平台表。

持久化优先复用：

- `PeggingSupplyAllocation`；
- `AllocationTaskShare`；
- 物理`Pegging`；
- 现有结果结构。

---

# 六、Demand排序：2号位执行，禁止恢复全局PriorityScore

2号位执行3号位冻结的排序规则。

正确模型：

```text
计算层
  → 有序Priority Segment
  → 第一命中
  → Segment内Sort
  → 稳定键
```

不是：

```text
所有Demand → 一个PriorityScore → 全局从大到小
```

排序可消费：

- OrderType；
- DueDate；
- CustomerTier；
- DelayStatus；
- IssueDate；
- Firm/保护等冻结规则允许字段。

### 验收
同一计算层不同Demand类型可以按照冻结Priority Segment交错，不被一个全局分数重新洗牌。

---

# 七、PI承接与PI Position接入

## 7.1 2号位仍负责“先选PI”

同Material多个PI：

- 默认按已冻结的PI排序规则；
- 先选择PI；
- 再在该PI内部消费Position；
- 不允许把多个PI的Stage/XC/在途位置先打散后全局混排。

---

## 7.2 PI RemainingQty总量边界

2号位必须遵守：

> ERP定义的PI RemainingQty = 尚未最终进入目标M库的全部剩余数量。

MES WIP、Stage、XC、PI级跨厂在途：

> 只用于定位这批RemainingQty在哪里，不能再叠加成额外Supply。

---

## 7.3 2↔5号位 PI Position接口

2号位负责准备输入事实并调用5号位复杂事实能力。

### 输入最小集合

- PI Header / ProductionInstructionNo；
- Material；
- ERP RemainingQty；
- StageProgress；
- OperationProgress（需要时）；
- PI级库存；
- XC；
- PI级跨厂在途；
- Routing/StagePath；
- Received/强事实；
- 相关冻结参数。

### 5号位返回

一组互斥Position：

- STAGE；
- XC；
- INTERPLANT_IN_TRANSIT；
- WAIT/必要等待位置；
- UNLOCATED；
- Issue列表。

满足：

> 所有PositionQty之和 = PI RemainingQty。

### 2号位消费
- 先锁定选中的PI；
- 按Allocation所需数量从该PI Position中取份额；
- 把“已经做到哪里、还剩哪些工艺”形成逻辑生产需求给1号位。

### 禁止
- 新建PI Header + Slice双生命周期平台；
- 让1号位直接解释原始StageProgress；
- 用Position再制造第二套PI总量。

---

## 7.4 PI自消费防护

必须有硬校验：

> 当某PI剩余量被转换为该PI自己的下阶需求时，同一个PI Supply不能再满足这个Demand本身。

否则会出现数量看似闭合、实际自循环。

建议以：

- SourceProductionInstructionNo；
- DemandOriginPI；
- SupplyPI；

做最小比较即可，不需要建图平台。

---

# 八、Lock：复用最小实体，不建新平台

运行Allocation前必须统一识别：

1. `STRICT_BINDING`
2. `DEMAND_PROTECTION`
3. Execution不可逆事实

其中前两类共用已冻结的需求—供给Lock实体/结构，用`LockType`区分。

### 2号位必须硬校验
- 不得超锁数量；
- 已取消Supply不能保留Lock；
- 同一物理份额不能多Demand重复锁；
- Lock不能绕过资格规则；
- 执行事实不能被普通重新Pegging逆转。

### Candidate
普通未锁Allocation：

> 可以释放重新竞争。

Strict / Protection / irreversible：

> 不释放。

---

# 九、库存、采购、VMI和Planning-only供给

## 9.1 InventoryBalance

`InventoryBalance.AllocatedQty`不得再承担本次运行的永久数量真相。

### 修改
删除/停用运行期：

`UPDATE InventoryBalance.AllocatedQty ...`

本次运行：

> SupplyBalance.RemainingQty + Allocation

才是权威数量状态。

如果`AllocatedQty`字段为兼容/快照需要保留，可以保留字段，不再作为主链动态扣减。

---

## 9.2 Timed Supply正式进入V1

2号位统一装载：

- Inventory；
- Arrived-not-inbound；
- PO remaining；
- VMI；
- interplant transit；
- Received（按相应业务模式）；
- 其它冻结文档定义的真实Timed Supply。

### AvailableTime
优先消费5号位/ODS已经标准化的AvailableTime。

2号位不得在主Pegging中重新猜采购ETA规则。

---

## 9.3 Planning-only Purchase Placeholder

当：

> Inventory=0、Arrived=0、PO=0、VMI=0，仍有采购件缺口

允许2号位在**当前运行内存**建立：

- Material；
- Qty = Gap；
- AvailableTime = planning basis + DefaultLT；
- Confidence=`ESTIMATED`；
- Commitment=`NOT_COMMITTED`。

禁止：

- INSERT采购占位表；
- 生成采购单；
- 生成Task；
- 下发ERP；
- 当成确定CTP承诺。

真实PO/VMI出现后，下次运行自然不再需要该Placeholder。

---

# 十、跨厂两类业务在2号位中的增量处理

## 10.1 STAGE_HANDOFF

2号位Supply搜索顺序：

1. 目标M库直接可用库存；
2. 选PI；
3. 该PI内部Position（XC / PI级跨厂在途 / Stage WIP等）；
4. 不足形成新生产逻辑需求。

不找上游M库，不生成ShippingTask。

工厂间运输只形成：

> 上游完成时间 + 实际Transport/Inspection/Transfer LT = 下游AvailableTime。

---

## 10.2 INTER_FACTORY_ORDER

顺序：

1. 接收侧BS/KS正常库存；
2. 既有SH；
3. SH对应厂间在途；
4. 同SH号的ZP/BP Received；
5. SH未生产部分；
6. 未生产部分进入下一计算层的源工厂生产Demand。

### 红线
- Received必须按同SH号绑定；
- 在途与Received不得重复计算同一物理份额；
- 顶层Demand Pegging到SH即停止，本层不继续递归SH生产；
- 下一计算层再解决SH未生产部分。

---

# 十一、计划良率：2号位算数量，1号位占能力

2号位逻辑生产需求必须至少包含：

- `NetOutputQty`
- `PlannedProcessQty`

### 含义
`NetOutputQty`：

> 真正要满足Demand/Allocation的合格数量。

`PlannedProcessQty`：

> 按计划良率后需要投入排程、消耗能力的计划加工数量。

### 责任
- 2号位反算PlannedProcessQty；
- Allocation闭合NetOutputQty；
- 1号位按PlannedProcessQty计算产能。

### 禁止
已有PI Supply不能因为计划良率再次膨胀。

---

# 十二、2号位 → 1号位：保持现有Solver接口方向，扩展内容

## 12.1 不改总接口方向

继续使用：

`IFiniteCapacityScheduler.SolveAsync(...)`

优先扩展现有`DomainSolveRequest / Result`，不要创建第二套入口。

---

## 12.2 DomainSolveRequest至少应承载九类信息

1. Run边界  
   `ScheduleRunId / PlanVersionId / DomainKey / DataCutoffTime / Horizon`

2. 逻辑生产需求  
   Material、Stage、NetOutputQty、PlannedProcessQty、DueDate、Priority上下文

3. Allocation追溯  
   AllocationSequence、Demand来源、Supply来源

4. Routing候选网络  
   Operation、Dependency、合法路径

5. Resource资格与能力  
   Eligibility、Capability、Calendar

6. 物料Quantity-Time约束  
   包括跨Domain/跨厂的分段AvailableTime

7. Strategy/Parameters  
   由3号位冻结后、2号位装载

8. Execution/Firm/Frozen约束  
   不可移动Task/时间窗/资源占用

9. Candidate特有约束  
   外Domain ACTIVE共享资源阻挡块、变化种子等

---

## 12.3 1号位返回

`DomainSolveResult`应能够表达：

- FinalTask；
- `AllocationTaskShare`；
- FinalTask↔FinalTask物理依赖；
- ScheduleExplanationFacts；
- Unscheduled / Unfulfilled；
- KPI/诊断摘要。

### 2号位不得做
收到Solver Result后再次改变：

- Resource；
- StartTime；
- EndTime；
- Split/Merge；

来形成“第二套真实结果”。

---

# 十三、AllocationTaskShare：从现有一对一假设改成真正多对多

## 13.1 当前问题
现有DTO方向正确，但持久化曾使用类似：

`ToDictionary(s => s.AllocationSequence)`

导致一笔Allocation只允许对应一个Task。

## 13.2 修改
按集合持久化：

> 一个Allocation可对应多个FinalTask；一个FinalTask可对应多个Allocation。

### 数量校验
对一个Allocation：

> `Σ ShareQty = 该Allocation需要由Task承接的净产出量`

对一个FinalTask：

> `Σ ShareQty <= Task.Quantity`

合并批场景正常应闭合到Task净合格产出。

### Task.OrderId
只能作为兼容/主展示字段，不得再作为真实Demand归属。

---

# 十四、PeggingSupplyAllocation：保留薄镜像，消除重复写

## 14.1 定位
PSA只记录：

- Inventory；
- PO/VMI；
- Transit；
- Received；

等**非Task供给**的已确认Allocation结果。

它不是第二套Supply余额真相，也没有独立生命周期。

## 14.2 当前整改
- 删除事务外的重复`PersistSupplyAllocationAsync`路径；
- 统一放入单Domain结果事务；
- 不在PSA持久化后反向UPDATE InventoryBalance；
- 如现实仍有外部查询消费者，保持字段兼容；
- 不因为PSA存在再增加一套Pegging Ledger表。

---

# 十五、统一单Domain事务

当前存在：

- 外层DELETE；
- 内层事务；
- 事务外UPDATE；
- 重复写PSA；

必须收口。

## 15.1 正式事务边界

一个Domain结果发布前，至少要保证：

- FinalTask；
- 物理Pegging；
- PeggingSupplyAllocation；
- AllocationTaskShare；
- ScheduleExplanationFact；
- 必要Summary；
- PlanVersion状态变更；

在正确的原子边界内。

### 原则
若持久化失败：

> 该Domain不得发布新ACTIVE/CANDIDATE成功态。

旧ACTIVE继续有效。

### 禁止
Pegging失败后：

> 记录warning然后继续排程/发布。

数量完整性失败必须使该Domain失败。

---

# 十六、Candidate：不要沿用Base全部Allocation扣死

## 16.1 BuildRemainingSupplyContext修正

Candidate可竞争Supply应按：

```text
当前一致物理Supply
- 已真实消耗
- Strict Binding
- Demand Protection
- 不可逆执行份额
- 已失效/不可用份额
= Candidate可竞争Supply
```

**普通Base ACTIVE Allocation不永久扣死。**

---

## 16.2 Candidate只给1号位变化种子

2号位负责：

- Base与Candidate重新Pegging；
- 比较逻辑生产需求；
- 找出新增、删除、数量变化的生产块；
- 形成Seed。

2号位**不预计算完整资源影响图**。

真实影响传播由1号位在资源时间轴中发现。

---

## 16.3 MaxImpactedOrders

只能：

- 告警；
- 提示人工确认。

不得：

- 达到阈值就停止正确传播；
- 静默截断受影响订单；
- 得出伪“可插单”。

---

## 16.4 白天共享资源

单Domain Candidate中：

> 其它Domain当前ACTIVE在共享设备上的占用 = 不可移动资源阻挡块。

2号位负责把这些占用装入Candidate求解上下文。

不建：

- 跨Domain资源配额；
- 借用平台；
- MultiDomain Candidate。

---

# 十七、跨Domain Quantity-Time

上游结果不能压成一个“总数量+最晚时间”。

例如：

- 40件 8月15日可用；
- 60件 8月17日可用；

必须传成两个Quantity-Time Slice。

2号位负责保持这些切片并传递给下游Domain。

禁止变成：

> 100件 8月17日可用。

---

# 十八、ScheduleRun / PlanVersion：保留现有外壳，只补冻结语义

## 18.1 不强制分钟点重构

冻结文档已经明确：

> 不因为文档写法强迫2号位改成某一个00:38/02:00固定创建顺序。

只要求：

- Run可追溯；
- DataCutoffTime一致；
- FULL一次Run可有多个Domain PlanVersion；
- Candidate严格单Domain；
- ExpectedDomainKeysJson运行开始冻结；
- 失败/成功终态闭合。

---

## 18.2 FULL失败链

如果上游Domain失败：

- 该Domain PlanVersion → FAILED；
- 原ACTIVE保持；
- 依赖它的直接/间接下游本次**不得发布新ACTIVE**；
- 无关Domain继续。

ScheduleRun最终：

- 全成功 `COMPLETED`
- 部分成功 `PARTIAL_SUCCESS`
- 零成功/致命错误 `FAILED`

---

## 18.3 人工恢复

失败历史不可改回RUNNING。

人工恢复：

> 新建ScheduleRun。

范围：

- 失败根Domain；
- 因它失败被阻断发布的下游；
- 若必要，补仍未恢复的上游。

不建设Retry平台。

---

# 十九、2↔3号位接口边界

3号位负责：

- RuleSet / ParameterSet / StrategyProfile治理；
- 版本发布；
- 本次运行冻结快照；
- ScheduleRun/PlanVersion生命周期边界和最小激活授权。

2号位负责：

- 装载已冻结版本；
- 在Demand/Supply/Pegging中执行这些规则；
- 不逐笔远程调用3号位；
- 不让5号位成为规则执行插件中心。

### 红线
一次Run开始后：

> 规则/参数版本不得因维护页面变化而中途改变。

---

# 二十、MES下发前2号位硬校验

只有满足全部条件的Task才能进入MES下发：

- 来自正式`ACTIVE` PlanVersion；
- 不是Candidate；
- 有合法生产执行身份；
- 不是无正式PI号的规划占位Task；
- 不是UNLOCATED保守Task；
- 不再依赖尚未转真实承诺的Planning-only Purchase Placeholder；
- 满足下发时间窗/Firm规则；
- 未被取消/失效。

MES五态保持现有冻结口径，不新增PAUSE/RESUME闭环。

---

# 二十一、数据库和EF最小同步项

以冻结DDL v5.1.2及兼容升级补丁为准，不允许2号位自己再设计一套Schema。

需要重点核对：

1. `Task.PlannedProcessQty`
2. `Task.OrderId`兼容语义/nullable
3. `AllocationTaskShare`
4. 最小PI Position Snapshot
5. LockType
6. PSA相关字段
7. `ScheduleExplanationFact.OrderId / TaskId` BIGINT
8. `OrderScheduleSummary.OrderId` BIGINT

### EF/DTO
对应BIGINT必须核成：

- C# `long`
- C# `long?`

不得继续使用`int`造成溢出或类型不一致。

### 兼容原则
现实数据库已有：

- FrozenZoneSnapshot；
- VirtualInventoryBalance；

如果存在，不因为业务退出就直接DROP。

先停止正式主链生成/消费。

---

# 二十二、建议实施顺序（按依赖关系，不按“重构美感”）

## 阶段A：主链止血

优先完成：

1. `DefaultBatchSplitter`退出正式调用；
2. Solver前最终Task生成退出；
3. 旧外层`FiniteCapacitySolver`退出正式调用；
4. `FrozenZoneSnapshot`正式调用退出；
5. 事务外PSA写入退出；
6. `InventoryBalance.AllocatedQty`运行期UPDATE退出；
7. Pegging失败后停止继续排程；
8. DELETE调整进统一正确事务边界。

### 阶段A验收
即使1号位真实Solver尚未完成，主链也不能再同时运行两套逻辑。

---

## 阶段B：Pegging数量核心

1. SupplyPool增强为SupplyBalance；
2. 新增DemandBalance；
3. Demand排序按Segment执行；
4. 原子双边扣减；
5. AllocationSequence；
6. LedgerEntries；
7. Strict Binding / Demand Protection / Execution校验；
8. PI自消费防护。

---

## 阶段C：复杂Supply和PI Position

1. 接入5号位PI Position；
2. PI总量/位置闭合；
3. 采购/PO/VMI/Arrived/Transit Timed Supply；
4. Planning-only Placeholder；
5. 两类跨厂；
6. Quantity-Time切片。

---

## 阶段D：1号位接口真实闭环

1. 完整DomainSolveRequest；
2. 真实`IFiniteCapacityScheduler`；
3. FinalTask；
4. AllocationTaskShare；
5. Task物理Pegging；
6. Explanation；
7. Unscheduled结果。

---

## 阶段E：事务与版本闭环

1. PSA薄镜像；
2. TaskShare多对多；
3. 统一Domain事务；
4. FAILED / ACTIVE / CANDIDATE正确状态；
5. 上游失败阻断下游发布；
6. 新ScheduleRun人工恢复。

---

## 阶段F：Candidate和MES闭环

1. Candidate普通Allocation释放；
2. Seed识别；
3. 外Domain共享资源阻挡；
4. 跨Domain WHATIF串行数据传递；
5. Candidate采用最小确认；
6. MES下发资格；
7. 次日全量重算回归。

---

# 二十三、逐代码对象整改清单

## 23.1 `SchedulingOrchestrator.cs`

### 保留
- `RunSchedulingAutoAsync`
- Context加载框架
- Run/Version状态外壳

### 修改
- 正式主链不再调用提前FinalTask生成；
- 停掉`DefaultBatchSplitter`正式调用；
- 不再在Solver前持久化权威Task；
- 保留MES Progress装载，但用途改为PI Position/Execution输入；
- 移除第二次旧Solver调用；
- Pegging失败必须终止该Domain；
- 单Domain持久化收口到统一事务。

---

## 23.2 `PeggingOrchestrator.cs`

### 保留
- BOM遍历骨架；
- SupplyPool；
- RemainingQty扣减原型；
- 现有结果DTO方向。

### 必须补
- DemandBalance；
- SupplyBalance原子动作；
- Demand Segment排序；
- Lock校验；
- AllocationSequence；
- LedgerEntries；
- PI选择/Position消费；
- 自消费防护；
- Timed Supply；
- Candidate重新竞争逻辑。

### 必须退出
- `InventoryBalance.AllocatedQty`运行期UPDATE；
- FrozenZoneSnapshot主链调用；
- PSA事务外重复写。

---

## 23.3 `PassThroughSchedulerStub.cs`

### 保留
仅用于：

- 接口联调；
- 单测；
- 1号位实现未合入前的测试环境。

### 禁止
作为生产正式Solver。

---

## 23.4 Task/Allocation持久化代码

必须改：

- 不以`OrderId`作为Task真实归属；
- 支持`AllocationTaskShare`多对多；
- 不使用AllocationSequence→单Task的Dictionary压缩；
- FinalTask只能来自1号位Result；
- PSA和TaskShare在统一事务落盘。

---

# 二十四、2号位必须完成的验收用例

以下不是“建议测试”，而是V1实施验收的最低集合。

| 编号 | 场景 | 必须验证 |
|---|---|---|
| T01 | SALES_ORDER已ERP扣成品库存 | APS不再二次扣FG |
| T02 | 一个Demand分配多个Supply | Demand/Supply余额同时闭合 |
| T03 | 一个Supply竞争多个Demand | 不超分、不重复物理Supply |
| T04 | 同Material多个PI | 先选PI，再消费该PI Position |
| T05 | PI剩余量进入下层 | PI不能消费自己 |
| T06 | Stage/XC/Transit并存 | Position互斥，合计=PI RemainingQty |
| T07 | 普通Allocation跨版本 | 夜间可按新优先级重新Pegging |
| T08 | Demand Protection | 被保护份额不能被新高优先级普通Demand抢走 |
| T09 | 无PO/VMI采购缺口 | 生成内存Estimated Placeholder，不落库 |
| T10 | 一个FinalTask合并多个Demand | TaskShare多对多数量闭合 |
| T11 | 计划良率 | Allocation按NetQty，能力按PlannedProcessQty |
| T12 | STAGE_HANDOFF | 不生成ShippingTask，只传AvailableTime |
| T13 | INTER_FACTORY_ORDER | BS/KS→SH→Transit/Received→未生产层次正确 |
| T14 | Candidate普通Base Allocation | 可释放重新竞争 |
| T15 | Candidate共享设备 | 外Domain ACTIVE占用不可移动 |
| T16 | MaxImpactedOrders超阈值 | 继续正确传播，只报警 |
| T17 | 跨Domain40+60分段 | 保留两个Quantity-Time Slice |
| T18 | 上游Domain失败 | 下游依赖Domain不发布新ACTIVE，无关Domain继续 |
| T19 | 人工恢复 | 新建ScheduleRun，不修改失败历史 |
| T20 | Candidate/UNLOCATED/占位Task | 不下MES |
| T21 | Pegging数量不闭合 | Domain失败，不继续Solver/发布 |
| T22 | Solver返回延期 | 作为可行业务结果，不当作运行失败 |

---

# 二十五、性能与回归验收

## 25.1 夜间FULL

目标仍是：

> 约10万FinalTask规模，整体约15分钟目标。

2号位重点负责：

- I/O前置；
- 一次加载；
- 内存Pegging；
- 批量落盘；
- 避免逐Allocation DB往返；
- 不因新DemandBalance/Lock产生N+1查询。

## 25.2 Candidate

2号位不负责Solver内部局部传播算法，但必须保证：

- Candidate数据构造不全量重跑无关ETL；
- Realtime BOM只对必要新订单；
- Supply/Allocation比较在内存完成；
- Seed准确；
- 外Domain阻挡一次性加载。

---

# 二十六、2号位交付物

2号位最终不是只提交代码，需要一起提交以下材料：

1. **增量修改清单**  
   格式：文件/类/方法 → 原行为 → 新行为 → 对应冻结条款。

2. **退出主链清单**  
   至少明确：
   - DefaultBatchSplitter；
   - 旧FiniteCapacitySolver；
   - FrozenZoneSnapshot；
   - VirtualInventoryBalance；
   - InventoryBalance.AllocatedQty运行UPDATE；
   - 事务外PSA写。

3. **接口实现对照**
   - 2↔1；
   - 2↔5；
   - 2↔3。

4. **数据库兼容升级执行记录**
   - 测试库升级结果；
   - EF/DTO BIGINT核查结果。

5. **T01～T22验收结果**

6. **性能测试结果**
   - FULL；
   - Candidate。

7. **未完成项**
   只能记录真实未完成技术项，不能用“业务待确认”代替已经冻结的业务。

---

# 二十七、提交代码时的AI审查规则

2号位每一阶段提交后，AI必须按以下顺序审：

1. 是否违反三份业务冻结；
2. 是否违反六份技术冻结；
3. 是否无意中恢复旧主链；
4. 是否新造重复真相源；
5. 是否出现过度设计；
6. 是否破坏现有Orchestrator/Run外壳；
7. 是否完成数量闭合；
8. 是否完成事务闭合；
9. 是否跨越1/3/5号位职责；
10. 是否通过本阶段验收用例。

如果AI提出：

> “为了更优雅建议重构……”
> “建议先设计通用插件平台……”
> “建议增加跨Domain统一Candidate……”
> “建议增加Ledger平台……”
> “建议把90天远期改成另一套粗排……”

应直接判定为：

> **偏离冻结范围，不采纳。**

---

# 二十八、实施过程中允许2号位向0号位提出的问题

只允许以下两类：

### A. 真实源数据事实缺失
例如：

- 某采购/VMI ODS源到底是哪一个真实字段；
- 某MES WIP字段现实是否存在；
- 某现有外部消费者是否仍依赖历史表。

这类问题是**数据事实确认**，不是重新讨论业务。

### B. 冻结规则出现不可实现的硬冲突
必须明确写：

> “请求重新打开冻结决策 F-xxx。”

并同时提供：

- 冲突的两条冻结规则；
- 真实代码/数据证据；
- 为什么Adapter/兼容实现无法解决；
- 最小业务变更建议。

没有这些证据，不得提出“重新设计”。

---

# 二十九、2号位最终完成定义（Definition of Done）

只有同时满足以下条件，2号位V1主链才算完成：

- 现有Orchestrator等外壳得到复用；
- 旧重复Task/Solver路径退出正式主链；
- DemandBalance + SupplyBalance形成统一数量真相；
- Allocation原子扣减；
- AllocationSequence时机正确；
- Strict Binding / Demand Protection / Execution参与分配；
- PI Position总量与位置闭合；
- 采购/VMI/Timed Supply进入V1；
- Planning-only Placeholder只在内存；
- 两类跨厂闭合；
- 逻辑生产需求正确交给1号位；
- 1号位FinalTask成为唯一物理排程真相；
- AllocationTaskShare真实多对多；
- PSA只做薄镜像且无重复写；
- InventoryBalance不被运行期永久污染；
- Candidate普通Allocation可重新竞争；
- 共享资源外Domain占用正确阻挡；
- 跨Domain Quantity-Time不被压平；
- FULL失败链/人工恢复闭合；
- MES下发资格正确；
- T01～T22全部通过；
- 无新增V1过度设计；
- 没有因为代码实现反向改动冻结业务。

---

# 三十、一句话交付要求

> **2号位不是“重写APS”，而是在现有代码上完成一次可控的主链收口：保留已有Orchestrator、Context、SupplyPool、进度装载、Solver接口和事务骨架；退出提前Task、DefaultBatchSplitter、双Solver、FrozenZoneSnapshot、VirtualInventoryBalance、InventoryBalance运行期扣减等冲突旧路径；补齐Demand/Supply双余额、Allocation/Lock/PI Position/Timed Supply/Candidate/TaskShare/事务和失败恢复闭环。**


---

# 三十一、2026-08-14执行澄清：逻辑生产需求、Allocation与持久化边界

> 本节为v1.1新增的执行澄清。它不改变任何冻结业务，只把2号位AI在实际改代码时提出的4个实现问题写死，避免再次回到“Pegging先生成Task、Solver只补时间资源”的旧主链。

## 31.1 `LogicalProductionDemand`：新建轻量内存DTO，不建表

Pegging完成后，2号位需要把“仍需制造的数量”交给1号位有限产能Solver。该对象不是FinalTask，也不应复用`PeggingLedgerEntry`。

建议新增：

```csharp
public sealed class LogicalProductionDemand
{
    public long PlanVersionId { get; init; }
    public string DomainKey { get; init; } = default!;

    public int AllocationSequence { get; init; }
    public string DemandKey { get; init; } = default!;
    public long? OrderId { get; init; }

    public int MaterialId { get; init; }
    public int FactoryId { get; init; }

    public string StartStageCode { get; init; } = default!;

    public decimal NetOutputQty { get; init; }
    public decimal PlannedProcessQty { get; init; }

    public DateTime RequiredAvailableTime { get; init; }

    // 已经由2号位按“计算层→Priority Segment→段内排序”得到的稳定业务顺序
    public int DemandSequence { get; init; }

    public string? ProductionInstructionNo { get; init; }
    public bool IsUnlocated { get; init; }
}
```

### 约束

- 只存在于本次运行内存/DTO，不新增物理表；
- 不新增或恢复全局`PriorityScore`；
- Routing详细网络不塞进该DTO，由`DomainSolveRequest`另行承载；
- V1优先保持“一条LogicalProductionDemand对应一条AllocationSequence”，不要由2号位提前跨Allocation合批；
- FinalTask的拆批/合批决定权仍属于1号位。

---

## 31.2 Pegging阶段只形成Draft，不正式提交PSA

Pegging阶段允许形成：

- `AllocationRecord`
- `PeggingSupplyAllocationDraft`
- `LogicalProductionDemand`
- Lock结果
- 其它本次运行内存结果

但**不得在Solver之前把PeggingSupplyAllocation作为正式结果单独提交事务**。

正式顺序：

```text
Pegging
→ Allocation/Draft/LogicalProductionDemand
→ 1号位Solver
→ FinalTask + AllocationTaskShare + 物理Task依赖 + Explanation + Unscheduled
→ 2号位完整校验
→ 单Domain统一事务持久化全部正式结果
```

正式事务至少覆盖：

- FinalTask
- PeggingSupplyAllocation
- AllocationTaskShare
- 物理Pegging
- ScheduleExplanationFact
- 必要Summary
- PlanVersion结果状态

### 原因

禁止出现：

```text
PSA已COMMIT
→ Solver失败
→ 数据库留下“供给已正式分配但没有最终计划”的孤儿结果
```

---

## 31.3 `AllocationTaskShare`中的Allocation是通用逻辑Allocation

`AllocationTaskShare`连接的不是“PeggingSupplyAllocation记录本身”，而是Pegging阶段生成的通用逻辑Allocation。

推荐最小语义：

```csharp
public sealed class AllocationRecord
{
    public long PlanVersionId { get; init; }
    public int AllocationSequence { get; init; }

    public string DemandKey { get; init; } = default!;
    public int MaterialId { get; init; }

    public string SupplyType { get; init; } = default!;
    public string SupplyKey { get; init; } = default!;

    public decimal Qty { get; init; }
    public DateTime? AvailableTime { get; init; }

    public bool RequiresProduction { get; init; }
}
```

### `AllocationSequence`作用域

冻结为：

> `PlanVersionId + AllocationSequence`唯一。

不建设全系统Sequence服务。

### 三种典型关系

1. 库存/PO/VMI/Received等直接Supply  
   - 有Allocation；
   - 可能写PeggingSupplyAllocation；
   - `RequiresProduction=false`；
   - **没有AllocationTaskShare**。

2. 自制缺口形成规划生产  
   - 有Allocation；
   - `SupplyType=PLANNED_PRODUCTION`；
   - `RequiresProduction=true`；
   - 同时形成LogicalProductionDemand；
   - Solver后通过AllocationTaskShare连接FinalTask。

3. 多需求合并FinalTask  
   - 多个AllocationSequence可以通过多行AllocationTaskShare连接同一个FinalTask；
   - 一个AllocationSequence也可以因物理拆批连接多个FinalTask。

---

## 31.4 `TryAtomicAllocation`成功后只生成Allocation，不生成Task

`TryAtomicAllocation`的职责必须保持小而确定：

1. 校验Demand与Supply可分配；
2. 校验Eligibility；
3. 校验Strict Binding；
4. 校验Demand Protection；
5. 校验Execution不可逆事实；
6. 计算AllocatedQty；
7. 扣减DemandBalance；
8. 扣减SupplyBalance；
9. 生成AllocationSequence；
10. 生成AllocationRecord。

它：

- **不生成Task**；
- 不决定Resource/Start/End；
- 不负责拆批/合批；
- 不应无条件生成LogicalProductionDemand。

调用层根据Allocation类型决定：

```csharp
var allocation = TryAtomicAllocation(...);

if (allocation is null)
    return;

result.Allocations.Add(allocation);

if (allocation.RequiresProduction)
{
    result.LogicalProductionDemands.Add(
        BuildLogicalProductionDemand(allocation, demand, context));
}
```

### 自制缺口

真实Supply不足时，自制件缺口形成：

> `PLANNED_PRODUCTION`

逻辑Allocation，并产生LogicalProductionDemand。

### 采购缺口

采购件没有真实正式Supply时，形成：

> `Planning-only Purchase Placeholder`

该对象：

- 有Qty/AvailableTime；
- `ESTIMATED / NOT_COMMITTED`；
- 不形成LogicalProductionDemand；
- 不形成生产Task；
- 不落成采购承诺。

---

# 三十二、2号位现有主链Owner最终澄清

`SchedulingOrchestrator`属于**2号位现有主流程/编排外壳**。

3号位的冻结职责是：

- 规则与参数治理；
- Strategy/Rule/Parameter版本；
- ScheduleRun/PlanVersion生命周期元数据与最小激活边界。

因此不得再写成：

> “SchedulingOrchestrator = 3号位服务”。

如果仓库中的README/ARCHITECTURE仍有旧职责描述，应修改说明文件，不得据此修改冻结业务。

---

# 三十三、Task生命周期最终澄清

冻结后的正式Task生命周期为：

```text
2号位Pegging
→ Allocation + LogicalProductionDemand
→ 1号位Solver
→ FinalTask
→ 2号位统一持久化
```

“SQL INSERT由2号位代码执行”与“FinalTask由1号位Solver生成”并不矛盾：

- **业务/算法生成Owner**：1号位；
- **数据库持久化Owner**：2号位。

因此PeggingOrchestrator不得在Solver前把TaskDraft当作FinalTask写入Task表。

Task正式状态值域保持：

- `PLANNED`
- `RELEASED`
- `IN_PROGRESS`
- `COMPLETED`
- `CANCELLED`

不得写`Scheduled`。

---

# 三十四、Task幂等清理责任

两次DELETE需要清理，但不能基于错误Task生命周期优化。

冻结后的原则：

- Pegging阶段不再INSERT FinalTask，因此PeggingOrchestrator不承担Task清理；
- FinalTask持久化集中到2号位最终结果持久化层；
- 同一个BUILDING/CANDIDATE PlanVersion重试时，如需要幂等清理，只在最终结果持久化事务中设置一个清理点；
- 新PlanVersion首次执行通常不存在旧FinalTask，不把DELETE设计成核心业务步骤；
- 不按多个服务各删一遍Task。
