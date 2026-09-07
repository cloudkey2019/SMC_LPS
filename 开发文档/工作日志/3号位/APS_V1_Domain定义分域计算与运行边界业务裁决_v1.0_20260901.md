# APS V1 Domain 定义、分域计算与运行边界业务裁决

**版本**：v1.0  
**日期**：2026-09-01  
**适用范围**：APS V1  
**裁决性质**：业务/职责边界补充裁决，需回写当前有效冻结业务基线及相关技术文档  
**主要涉及号位**：0号位、1号位、2号位、3号位、4号位  
**不改变事项**：Pegging主体业务、90天统一有限产能Solver、Candidate单Domain、跨Domain Quantity-Time、失败链、共享资源互斥等既有冻结规则

---

# 1. 裁决目的

当前冻结文档已经明确：

- 一个夜间 FULL ScheduleRun 可包含多个 Domain；
- 每个 Domain 有独立 PlanVersion；
- 白天 Candidate 严格单 Domain；
- 跨 Domain 依赖按 `Domain_Dependency` 处理；
- 跨 Domain 传递 `Quantity + AvailableTime`；
- 上游 Domain 失败时，依赖下游本次不得发布新 ACTIVE；
- 不建设 MultiDomain Candidate；
- 不建设 VirtualInventoryBalance 持久化状态平台；
- 不建设跨 Domain 共享资源配额/借用平台。

但当前文档与代码仍存在以下未完全闭合点：

1. **Domain 到底是什么、在哪里定义、由谁维护，没有唯一明确口径；**
2. 部分旧文档仍把 Domain 近似等同于 ProductFamily，不能覆盖现行业务；
3. 当前实际业务允许：
   - 仅按 ProductFamily 定义一个 Domain；
   - 按 Factory + ProductFamily 定义一个 Domain；
4. 2号位当前执行层仍存在“整版一次 Solve”的遗留，尚未真正做到逐 Domain 独立求解；
5. 未来是否允许 Domain 并行、当前是否必须并行，需要明确边界。

本裁决只补齐上述缺口，不重新设计APS V1。

---

# 2. Domain 的最终业务定义

## 2.1 Domain 是什么

APS V1 中，Domain 定义为：

> **APS用于隔离排程计算、计划版本、白天Candidate、失败传播与发布边界的稳定业务范围。**

Domain 是 APS 自己定义的排程边界，不等同于：

- ProductFamily；
- Factory；
- ProductionDepartment；
- Stage；
- Resource Group。

上述维度可以参与 Domain 的范围定义，但 Domain 本身是一个独立业务对象，以稳定 `DomainKey` 标识。

---

## 2.2 V1允许的Domain范围定义

APS V1只允许以下两种Scope：

### A. FAMILY

一个 ProductFamily 整体形成一个 Domain。

示例：

```text
DomainKey = FAMILY_X
ScopeType = FAMILY
ProductFamily = X
Factory = NULL
```

### B. FACTORY_FAMILY

一个 Factory + 一个 ProductFamily 形成一个 Domain。

示例：

```text
DomainKey = BJ_FAMILY_Y
ScopeType = FACTORY_FAMILY
Factory = BJ
ProductFamily = Y
```

---

## 2.3 V1明确不允许

V1不得把Domain做成任意表达式或通用规则平台。

不允许系统管理员随意增加：

- Customer；
- Stage；
- ProductionDepartment；
- Resource；
- OrderType；
- 任意SQL条件；
- 任意脚本表达式；

来动态拼装Domain。

如未来确需超过 `FAMILY` / `FACTORY_FAMILY` 两种范围，必须重新打开Domain业务定义裁决。

---

# 3. Domain 在哪里定义

## 3.1 不进入规则引擎 RuleSet / ParameterSet

Domain Definition **不是业务规则（Rule）**，也不是参数（Parameter）。

因此：

> **Domain定义不得放入 RuleSet / ParameterSet 作为运行时规则执行。**

理由：

- Domain决定PlanVersion隔离边界；
- Domain决定Candidate隔离边界；
- Domain决定失败传播与发布边界；
- Domain必须在一次运行开始前已经稳定存在；
- Domain不能因为发布一条新规则而在同一次Run中变化。

---

## 3.2 DomainDefinition属于APS基础配置/主数据级配置

APS必须存在一个稳定的“Domain Definition”配置来源，用于回答：

- 有哪些Domain；
- 每个Domain的DomainKey；
- 是FAMILY还是FACTORY_FAMILY；
- 对应哪个ProductFamily；
- FACTORY_FAMILY时对应哪个Factory；
- 是否启用；
- 稳定执行顺序（如需要）。

当前冻结文档尚未明确一个完整、唯一的Domain Definition物理实现，因此本次裁决补齐：

> **系统必须具备持久化、可维护、可查询的DomainDefinition配置能力。**

物理实现优先顺序：

1. 若当前数据库已有等价且职责清晰的正式配置结构，则复用；
2. 若不存在等价结构，再增加最小DomainDefinition物理表；
3. 不为Domain建设DSL、规则链、配置表达式平台。

本裁决冻结的是**业务能力和职责**，不是强制表名。

---

# 4. Domain 的维护权限和号位职责

## 4.1 系统管理员

Domain定义由**系统管理员**维护。

系统管理员可以：

- 新建Domain；
- 启用/停用Domain；
- 选择ScopeType；
- 选择ProductFamily；
- FACTORY_FAMILY时选择Factory；
- 维护DomainName；
- 必要时维护稳定排序号。

系统管理员不能：

- 在运行中的ScheduleRun内修改Domain范围并即时生效；
- 使用任意脚本定义Domain；
- 将一个Candidate直接定义成多个Domain。

---

## 4.2 4号位

4号位负责Domain Definition的维护页面/查询页面。

V1不要求新增复杂“Domain设计器”。

页面只需提供最小维护能力：

- DomainKey；
- DomainName；
- ScopeType；
- ProductFamily；
- Factory（按需要）；
- IsActive；
- 基本校验；
- 变更审计。

---

## 4.3 3号位

3号位负责Domain Definition的**治理与运行引用边界**，但不把Domain定义变成RuleSet。

3号位负责：

- Domain Definition后端维护能力；
- 合法性校验；
- 启用/停用；
- 运行开始时提供/冻结本次有效Domain集合；
- `ScheduleRun.ExpectedDomainKeysJson`冻结；
- PlanVersion与DomainKey生命周期一致性；
- Candidate严格单Domain校验。

3号位不负责：

- 给每条Order逐笔执行Domain匹配；
- Domain内Pegging；
- Domain内Solver。

---

## 4.4 2号位

2号位是Domain运行执行Owner。

2号位负责：

1. 根据当前有效DomainDefinition，将Order / Demand / Supply / Material上下文归属到正确Domain；
2. 形成真实DomainKey；
3. 按Domain进行Pegging；
4. 按Domain形成LogicalProductionDemand；
5. 按Domain调用1号位Solver；
6. 按Domain持久化结果；
7. 跨Domain Quantity-Time传递；
8. 跨Domain失败传播；
9. 夜间共享资源互斥上下文传递。

2号位不能：

- 自行创建新的Domain定义；
- 用 `PlanVersionId.ToString()` 等技术Id冒充DomainKey；
- 把Domain重新硬编码成ProductFamily；
- 在代码中另建一套Domain定义规则。

---

## 4.5 1号位

1号位只消费：

- DomainKey；
- 当前Domain完整 `DomainSolveRequest`；
- 上游Quantity-Time；
- 资源不可移动Block；
- 当前Domain的Routing / Resource / Material / Strategy上下文。

1号位不关心：

> 当前Domain是按Family定义，还是按Factory+Family定义。

1号位也不得重新决定Domain归属。

---

# 5. DomainKey规则

DomainKey必须：

- 稳定；
- 唯一；
- 可追溯；
- 不依赖PlanVersionId；
- 不依赖ScheduleRunId；
- 不在一次Run执行过程中变化。

推荐形式可读即可，例如：

```text
FAMILY_X
BJ_FAMILY_Y
TJ_FAMILY_Y
```

具体编码格式属于技术实现，不作为业务硬规则。

---

# 6. 夜间FULL的Domain执行方式

## 6.1 固定业务模型

一次夜间FULL：

```text
1 ScheduleRun
    ↓
多个 Domain
    ↓
每个 Domain 独立 PlanVersion
```

例如：

```text
ScheduleRun R001
├─ Domain D01 → PlanVersion PV01
├─ Domain D02 → PlanVersion PV02
├─ Domain D03 → PlanVersion PV03
└─ ...
```

不得继续使用：

```text
一个PlanVersion承载全部Domain
```

---

## 6.2 当前V1采用串行Domain执行

本次正式裁决：

> **APS V1 当前先采用多Domain串行执行，不要求并行Domain。**

执行顺序：

1. 读取 `ExpectedDomainKeysJson`；
2. 读取 `Domain_Dependency`；
3. 拓扑排序；
4. 同一拓扑层内按稳定顺序执行；
5. 每个Domain执行完整Pegging → Solver → Persist；
6. 再进入后续Domain。

---

## 6.3 不恢复旧Domain多Job架构

当前不要求恢复旧的：

- DomainSchedulingJob；
- DomainLayerCoordinatorJob；
- 多个Hangfire Domain Job并发执行；

等旧实现。

正确方向：

> **保留当前Nightly/SchedulingOrchestrator主外壳，在内部形成明确的单Domain执行单元。**

建议形成等价于：

```text
ExecuteDomainAsync(domainExecutionContext)
```

的稳定内部边界。

---

# 7. 为什么V1当前不直接做并行Domain

用户当前实际Domain结构约为：

- 总计约6～7个Domain；
- 其中1～2个为超大Domain；
- 其余为较小Domain。

现阶段直接并行会同时增加：

- 共享Resource协调；
- Domain状态并发更新；
- 事务隔离；
- 内存峰值；
- 失败传播；
- 可重复性控制；

等复杂度。

同时，分域后单个Solver问题规模下降，`Σ Solve(小Domain)` 不一定比一次大Solve更慢。

因此：

> **在没有真实分域压测数据前，不因15分钟目标提前引入并行。**

---

# 8. 15分钟性能目标与必须增加的测量

V1性能目标继续保持：

> **约10万FinalTask / 90天 / 夜间FULL约15分钟收敛目标。**

当前采用串行Domain后，必须按Domain记录性能：

- DomainKey；
- DemandCount；
- LogicalProductionDemandCount；
- FinalTaskCount；
- ContextBuildMs；
- PeggingMs；
- SolverMs；
- PersistMs；
- TotalMs。

同时记录：

- ScheduleRunTotalMs。

目的是明确判断：

1. 是否一个超大Domain本身就是瓶颈；
2. 是否多个小Domain累计导致超时；
3. 是否数据库重复I/O造成浪费；
4. 是否未来并行真正有收益。

---

# 9. 未来Domain并行的裁决边界

未来如果真实压测证明串行超过目标，可优化Domain并行。

但只允许对满足以下条件的Ready Domain并行：

> **无未完成的上游依赖 AND 无共享Resource。**

即：

```text
Dependency(D1, D2) = NONE
AND
Resources(D1) ∩ Resources(D2) = ∅
```

才允许简单并行。

未来并行不改变：

- Domain定义；
- Pegging；
- PlanVersion；
- Solver接口；
- Quantity-Time；
- Candidate规则。

因此当前V1实现时必须做到：

> **串行运行，但单Domain执行边界天然可并行。**

这可以显著降低未来并行改造成本。

---

# 10. 夜间FULL共享资源规则

即使Domain独立PlanVersion，也不能导致真实共享设备双占。

当前V1串行执行时：

```text
Domain A Solve
    ↓
得到共享Resource占用
    ↓
作为后续Domain不可占用时间块
    ↓
Domain B Solve
```

后续Domain必须感知本次ScheduleRun中前面已成功Domain形成的共享资源占用。

V1不建设：

- Domain Resource Quota；
- 资源配额；
- 跨Domain借用；
- 虚拟子日历平台。

---

# 11. 跨Domain Quantity-Time

跨Domain依赖必须传递真实的：

```text
Material + Quantity + AvailableTime
```

必须保留分段：

```text
40件 @ 15日
60件 @ 17日
```

禁止压平成：

```text
100件 @ 17日
```

跨厂/Stage LeadTime在Quantity-Time传递过程中继续按现有真实LeadTime处理。

---

## 11.1 不建设VirtualInventoryBalance

跨Domain Quantity-Time在当前ScheduleRun内通过内存上下文/DTO传递。

不得重新建设：

- VirtualInventoryBalance持久化表；
- VirtualInventoryBalanceRepository作为跨版本权威状态；
- 虚拟库存状态机。

若现有代码仍保留旧 `VirtualInventoryItem` DTO名称，可为降低改造量暂时保留，只要：

- 仅当前Run内存使用；
- 不落成跨版本真相；
- 语义严格等于Quantity-Time Slice。

---

# 12. 夜间FULL失败传播

如果：

```text
C → B → A
```

本次：

- C成功；
- B失败；

则：

- B新PlanVersion = FAILED；
- A本次不得发布新ACTIVE；
- A保留上一ACTIVE；
- A应标识为被上游阻断；
- 与B无关的其它Domain继续；
- ScheduleRun根据结果汇总为PARTIAL_SUCCESS或FAILED。

原FAILED历史不得改回RUNNING。

人工恢复仍通过新建ScheduleRun，按必要依赖链重算。

---

# 13. 白天Candidate边界

Candidate继续严格单Domain。

一个Candidate：

```text
1 ScheduleRun
→ 1 DomainKey
→ 1 Base ACTIVE PlanVersion
→ 1 Candidate PlanVersion
```

其它Domain当前ACTIVE在共享Resource上的占用：

> 必须作为不可移动 `ExternalDomainResourceBlocks` 传给1号位。

当前Candidate不得：

- 推动其它Domain；
- 删除其它Domain任务；
- 跨Domain借资源；
- 形成MultiDomain Candidate。

---

# 14. 跨Domain CTP

若：

```text
C → B → A
```

白天CTP按：

```text
C WHATIF
→ Quantity-Time
→ B WHATIF
→ Quantity-Time
→ A WHATIF
→ 汇总客户CTP答案
```

实现。

本质仍然是多个串行单Domain Candidate/WHATIF。

不得建设MultiDomain Candidate。

---

# 15. Domain_Dependency语义修正

当前旧文档/DDL中若仍存在：

```text
UpstreamDomainCode / DownstreamDomainCode = ProductFamily.Code
```

的描述，需要修订。

本次裁决后：

> `Domain_Dependency` 的上下游节点必须表达真实 `DomainKey`，不得再默认Domain=ProductFamily。

如果一个Family按Factory拆成：

```text
BJ_FAMILY_Y
TJ_FAMILY_Y
```

则两者是两个独立Domain节点。

Domain_Dependency必须针对真实DomainKey建图。

---

# 16. StrategyProfile与Domain的关系

三者职责必须区分：

```text
DomainDefinition
= 这个Domain是谁、范围是什么

StrategyProfile
= 这个Domain使用什么RuleSet / ParameterSet / Solver Strategy

ScheduleRun.ExpectedDomainKeysJson
= 这一次Run具体计算哪些Domain
```

DomainDefinition不是StrategyProfile。

StrategyProfile可以按Domain绑定策略，但不得反向定义Domain的范围。

---

# 17. 文档与代码整改要求

本裁决通过后，需要同步检查并修改：

## 17.1 业务基线

在当前有效《APS V1最终全部流程与业务基线》中补充：

- Domain定义；
- 两种Scope；
- 系统管理员维护；
- 当前串行、未来受控并行；
- DomainKey不是ProductFamily的同义词。

## 17.2 全流程走查

补充：

- DomainDefinition加载；
- Order/Demand Domain归属；
- 逐Domain Execute；
- 性能埋点；
- 夜间共享资源占用传递。

## 17.3 3号位实施包

补充：

- DomainDefinition治理；
- ExpectedDomainKeysJson从有效Domain集合冻结；
- 不纳入RuleSet。

## 17.4 2号位实施包

补充：

- DomainKey实际归属；
- 逐Domain执行；
- 跨DomainQuantity-Time；
- FULL共享资源Block；
- 失败传播；
- Domain性能统计。

## 17.5 4号位实施包

补充最小Domain配置维护入口。

## 17.6 数据库/字段说明

检查是否已有等价Domain配置对象。

如果没有：

> 增加最小DomainDefinition结构。

同时修订仍把DomainCode等同于ProductFamily.Code的字段注释。

## 17.7 代码

2号位重点检查：

- 一个Run是否创建多个Domain PlanVersion；
- DomainKey是否真实业务Key；
- 是否仍用PlanVersionId冒充DomainKey；
- 是否仍一次Solve整版；
- 是否仍有跨Domain Quantity-Time空列表；
- Candidate ExternalDomainResourceBlocks是否为空；
- FULL共享资源是否会双占；
- 上游失败下游是否仍错误发布。

---

# 18. 本次明确不做

本裁决不引入：

- 任意Domain表达式；
- Domain DSL；
- Domain规则脚本；
- MultiDomain Candidate；
- VirtualInventoryBalance平台；
- 跨Domain共享资源配额；
- 自动跨域资源借用；
- 多Domain原子激活；
- 本轮Domain并行；
- 第二套远期Solver。

---

# 19. 最终裁决摘要

APS V1 Domain最终口径如下：

1. **Domain是APS独立排程、版本、Candidate与发布边界，不等同于ProductFamily。**
2. **V1只允许FAMILY、FACTORY_FAMILY两种Domain Scope。**
3. **Domain由系统管理员维护，属于APS基础配置，不进入RuleSet/ParameterSet。**
4. **3号位负责Domain配置治理与Run引用冻结；2号位负责实际Domain归属和分域执行；1号位只消费DomainKey；4号位提供最小维护页面。**
5. **夜间FULL一个ScheduleRun对应多个Domain独立PlanVersion。**
6. **V1当前按Domain串行执行，不恢复旧多Hangfire Job并行架构。**
7. **当前实现必须把单Domain执行封装为稳定边界，为未来并行保留低成本扩展能力。**
8. **未来只对“无依赖且无共享Resource”的Ready Domain做受控并行。**
9. **跨Domain继续传递分段Quantity-Time，不建设VirtualInventoryBalance。**
10. **夜间FULL必须保持共享Resource真实互斥。**
11. **上游Domain失败，依赖下游本次不得发布；无关Domain继续。**
12. **Candidate继续严格单Domain，外Domain ACTIVE共享资源必须作为不可移动Block。**
13. **Domain_Dependency必须使用真实DomainKey，不再默认等于ProductFamily.Code。**
14. **15分钟目标通过真实分域压测和按Domain性能埋点判断，不因理论担忧提前增加并行复杂度。**

---

# 20. 一句话裁决

> **APS V1把Domain正式定义为由系统管理员维护的稳定排程隔离单元，范围最多到“Factory + ProductFamily”；当前一个FULL Run按Domain独立PlanVersion串行求解并保持跨域物料、共享资源和失败链闭合，未来仅在真实压测有必要时，对无依赖且无共享资源的Domain做受控并行。**
