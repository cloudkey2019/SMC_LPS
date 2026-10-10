# APS 集成接口设计规范 v1.31（20260913 跨版本排程连续性 / 跨厂SH联合冻结对齐版）

**版本**：v1.31  
**日期**：2026-09-13（北京时间）  
**基于**：
- 《APS_集成接口设计_v1.30_20260904_PI_Position执行起点上下文冻结对齐版》
- 《APS_集成接口设计_v1.27_冻结对齐版》
- 《APS_集成接口设计_v1.26_冻结对齐版》
- 《APS V1关键接口冻结说明：1↔2、2↔5、2↔3》v1.0
- 《APS V1关键接口冻结补充说明 Manual ETA / SupplyFact》v1.0
- 《APS V1 20260901 职责、前端接口与权限归属调整正式裁决》v1.0

**上位业务依据**：《APS V1最终全部流程与业务基线 v1.5》+《APS Pegging供需承接与分层计算业务说明 v1.4》+《APS 有限产能排产与滚动90天计划业务说明 v1.4》+《APS 核心排产全流程走查 V3.21》。

## 当前版本变化说明（2026-09-13）

**修改依据**：业务基线v1.5 + 核心走查V3.21 + 两份专项裁决  
**修订原则**：完整继承上一正式母版，只修改本轮已裁决影响范围；未明确列出的既有冻结业务保持不变。  
**影响边界**：不重新设计Pegging、Solver、Candidate、PlanVersion、PI Position、Manual ETA。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | 接口补充 | 第18.1～18.4 | 补齐MES工序级资源事实、ContinuationKey及FinalTask后三类身份映射。 | 跨版本连续性裁决 | 不改变当前Q/Pegging | 新增5→2与2→1运行契约 | 工序级资源字段 | 影响1/2/5号位 |
| 2 | 接口修订 | 第18.5～18.7 | 补齐SourceFactory、SH整单源厂Demand及SourceReadyTime时间链。 | 跨厂SH裁决 | 替换旧多Supply接口语义 | 影响2↔5、1↔2 | SourceFactory字段链 | 影响1/2/3/5号位 |
| 3 | 字段保护 | 第18.1 | 明确WorkOrderStatus/GoodQty/GoodCompletedQty既有名称，工单级不新增RemainingNetQty/LastReportResourceCode。 | 前序接口契约保护 | 无业务扩大 | MES接口稳定 | 工序级新增字段 | 影响5号位 |
| 4 | 治理修复 | 第0章/历史章节 | 顶部变更控制补入20260913 Delta；当前接口契约移入正文并删除重复文末补丁。 | 工作流复核 | 无业务变化 | 提升单一权威 | 无 | 无 |

---

# 0. 本版变更控制声明

本版本技术接口调整范围如下：

1. 完整吸收原《1↔2、2↔5、2↔3关键接口冻结》有效内容；
2. 完整吸收Manual ETA / SupplyFact补充冻结的有效接口语义；
3. 按2026-09-01正式裁决调整4号位对外接口边界；
4. 增加3号位认证/权限接口职责；
5. 增加5号位面向4号位的非治理类业务接口接入/中转职责；
6. 明确2号位退出4号位HTTP/API层，但不改变2号位业务Owner；
7. G4/G7/G8及Schedule结果查询逐步移交5号位业务接口接入层；
8. 明确数据库结构按业务Owner负责所带来的接口Owner变化；
9. 增加跨版本连续性接口语义：当前Q先形成，2号位逐MES工单形成Continuation Slice并向1号位传递ContinuationKey；
10. 增加工序级`LastReportResourceCode`契约，并明确工单级不得新增同名字段/RemainingNetQty；
11. 增加厂间SH源/目标工厂契约：`FactoryId=目标工厂`、`SourceFactoryId=源工厂`；
12. 增加SH第二层整单源厂履约Demand及`RequiredQty=Order.Quantity`接口语义；
13. 增加SH同Domain/跨Domain的`SourceReadyTime`与标准跨厂LT时间链。

**除本章明确列出的调整外，上一正式版本中其余ERP、MES、BOM、Routing、采购、VMI、ODS、防腐层、Realtime BOM、ScopeJson、ReasonCode、PlanVersion、ScheduleRun、Candidate及外部系统接口保持原冻结含义。历史接口原文若与本章或当前正文冲突，以v1.31当前契约为准。**

> 本版本不得利用“接口重构”重新设计Pegging、有限产能、Candidate、PlanVersion、跨厂、PI Position、Manual ETA或MES下发业务。

---

# 1. 当前正式跨号位接口总图

```text
                         ┌────────────────────┐
                         │      4号位 UI      │
                         └─────────┬──────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
              治理/安全类                    非治理APS业务类
                    │                             │
                    ▼                             ▼
             ┌────────────┐                ┌────────────┐
             │   3号位     │                │   5号位     │
             │治理+认证权限│                │事实+业务接口│
             └──────┬─────┘                └──────┬─────┘
                    │                              │
                    │ Run治理/策略                 │ Query/Application Service
                    ▼                              ▼
                  ┌──────────────────────────────────┐
                  │            2号位                 │
                  │ Pegging/数量/主流程/结果持久化   │
                  └────────────────┬─────────────────┘
                                   │
                                   ▼
                              ┌──────────┐
                              │  1号位   │
                              │ Solver   │
                              └──────────┘
```

正式红线：

- 4号位不直接调用1号位；
- 4号位不直接调用2号位HTTP/API；
- 3号位不重新成为全部APS业务代理层；
- 5号位的中转权不等于2号位业务所有权；
- 2号位继续是排程数量与主流程Owner；
- 1号位继续是唯一有限产能求解Owner。

---

# 2. 1↔2 有限产能接口冻结

本节完整吸收原关键接口冻结有效内容，接口方向不因本次职责调整而变化。

## 2.1 责任边界

### 2号位负责

- 完成需求排序和供需匹配（Pegging）；
- DemandBalance / SupplyBalance；
- Allocation；
- Lock；
- PI Position消费；
- 计划良率数量反算；
- 形成LogicalProductionDemand；
- 当前Q形成后消费5号位标准化既存执行上下文，逐MES工单形成Continuation Slice / Free Slice；
- 维护ContinuationKey与TaskNo / MESWorkOrderNo内部身份映射；
- 形成SH第二层源厂履约Demand并按SourceFactoryId重新归域；
- 准备Routing / Resource / Calendar / Material Quantity-Time / Execution约束；
- 调用1号位；
- 校验并持久化1号位结果。

### 1号位负责

- FinalTask生成；
- Resource选择；
- Start / End；
- 有限产能；
- Forward / Backward / Mixed；
- 拆批/合批；
- Setup邻接；
- Stage overlap / transfer batch；
- Candidate局部传播与同Domain兜底；
- FinalTask物理依赖；
- Explanation事实；
- Unscheduled / Unfulfilled结果。

## 2.2 调用入口

优先保留现有：

```csharp
IFiniteCapacityScheduler.SolveAsync(
    DomainSolveRequest request,
    CancellationToken ct)
```

不新建第二套Solver。

## 2.3 DomainSolveRequest 九类冻结输入

1. **RunBoundary**
   - ScheduleRunId
   - PlanVersionId
   - DomainKey
   - DataCutoffTime
   - HorizonStart / HorizonEnd

2. **LogicalProductionDemands**
   - AllocationSequence
   - DemandKey / OrderId
   - MaterialId / FactoryId
   - SourceFactoryId（SH源厂Demand适用）
   - StartStageCode
   - NetOutputQty
   - PlannedProcessQty
   - RequiredAvailableTime
   - DemandSequence
   - PI No（可空）
   - IsUnlocated
   - ContinuationKey（可空）
   - IsContinuation / NoSplitMerge（或现有等价语义）
   - PreferredResourceCode（可空；来自工序级LastReportResourceCode）

3. **AllocationLineage**
   - AllocationSequence
   - DemandKey
   - SupplyKey / SupplyType
   - Qty
   - AvailableTime

4. **RoutingNetwork**
   - RoutingOperation
   - RoutingDependency
   - 合法Path

5. **ResourceContext**
   - Eligibility
   - Capability
   - Calendar
   - Setup必要事实

6. **MaterialConstraints**
   - Material Quantity-Time Slice
   - 上游/跨Domain/跨厂AvailableTime
   - SH同Domain源厂→跨厂LT→目标侧后续依赖（适用时）

7. **StrategySnapshot**
   - 本次Run冻结的3号位策略/参数

8. **ImmovableFacts**
   - Execution
   - Firm
   - Frozen
   - Demand Protection对应物理约束

9. **CandidateContext（仅Candidate）**
   - Base稳定锚点
   - 变化Seed
   - 外Domain ACTIVE共享资源只读阻挡块

## 2.4 DomainSolveResult 冻结输出

至少包含：

- FinalTasks
- AllocationTaskShares
- TaskDependencies
- ScheduleExplanationFacts
- UnscheduledItems
- Evaluation / KPI摘要
- ContinuationKey原样回传（适用时）
- SH SourceReadyTime（适用时）

红线：

- 2号位不得收到结果后重新改变Resource / Start / End；
- 2号位不得在Solver前生成最终FinalTask；
- 1号位不得写数据库；
- 1号位不得重新做Demand排序或Pegging。

---

# 3. 2↔5 复杂事实接口冻结

## 3.1 5号位原业务定位保持

5号位继续负责：

- PI Position；
- NextOperationContext；
- BOM入口/Stage相关复杂事实；
- MES工单/工序/Stage标准化执行事实；
- 工序级LastReportResourceCode；
- 跨厂来源事实；
- Procurement / VMI / Received / Transit的ODS标准化事实；
- Manual ETA事实维护；
- 其它需要源系统复杂业务知识的派生事实。

5号位不负责：

- 最终Demand排序；
- Supply选择顺序执行；
- Allocation；
- Demand Protection实际数量扣减；
- Task；
- Solver；
- 2号位主排程流程；
- Explanation最终业务裁决。

## 3.2 PI Position批量接口语义

继续采用批量交接，不采用逐PI在线RPC。

建议接口保持：

```csharp
Task<IReadOnlyList<ProductionInstructionPositionResult>>
CalculateProductionInstructionPositionsAsync(
    IReadOnlyList<ProductionInstructionPositionInput> inputs,
    FrozenFactParameters parameters,
    CancellationToken ct);
```

输入至少包括：

- ProductionInstructionNo
- MaterialId
- FactoryId
- ERP RemainingQty
- StageProgress
- OperationProgress（必要时）
- PI级Inventory
- XC
- PI级InterplantTransit
- Routing / StagePath
- Received / Strong Evidence
- 本次冻结参数

输出至少包括：

- ProductionInstructionNo
- TotalRemainingQty
- PositionType
- StageCode / LocationKey
- PositionQty
- AvailableTime（适用时）
- IsStrongEvidence
- IsUnlocated
- Issues

硬闭合：

> 对每个PI：Σ PositionQty = ERP RemainingQty

红线：

- 5号位不修改ERP/MES源事实；
- 5号位不决定PI最终分给哪个Demand；
- 同一物理份额不得返回多个可消费Position；
- 2号位先选PI，再消费PI Position；
- 无法定位可降级UNLOCATED；总量无法闭合属于Domain失败级问题。

---

# 4. Manual ETA / SupplyFact 正式接口语义

本节吸收v1.27及原Manual ETA / SupplyFact补充冻结。

## 4.1 ProcurementManualEtaOverride

定位：

> APS人工维护采购ETA覆盖事实。

属性：

- 不属于ERP原生事实；
- 不修改ERP；
- 不属于ODS源事实。

职责：

### 5号位

负责：

- ProcurementManualEtaOverride数据结构；
- Repository / Service / API；
- ERP/ODS原始ETA、ReleaseDate等事实接口支持。

### 4号位

负责：

- Manual ETA维护页面；
- 通过5号位接口查询、新增/修改、取消。

### 2号位

负责：

- 排程运行读取Manual ETA；
- Effective ETA计算；
- AvailableTime计算；
- Pegging及Quantity-Time使用。

## 4.2 有效ETA优先级

正式冻结：

```text
Manual ETA
    >
ERP ETA
    >
ReleaseDate + DefaultPurchaseLT
```

## 4.3 SupplyFact主要语义

正式供给事实至少承载：

- SupplyType
- PhysicalSourceKey
- MaterialId
- FactoryId
- WarehouseCode
- RemainingQty
- ETA
- ReleaseDate
- AvailableTime
- CommitmentStatus
- Confidence
- SourceDocumentNo / SourceDocumentLineNo（适用时）
- SourceUpdatedAt

说明：

- ETA保持ERP/ODS来源事实；
- ReleaseDate保持ERP来源事实；
- AvailableTime是APS统一可用时间；
- AvailableTime最终计算责任属于2号位；
- 5号位只提供计算基础事实。

## 4.4 CommitmentStatus

只用于正式供给事实。

不得用于：

- Planning-only Purchase Placeholder；
- 虚拟采购；
- 虚拟PI。

## 4.5 Planning-only Purchase Placeholder

当库存、已到厂未入库、正式PO/VMI及其它正式供给均无可承诺供给时，允许2号位在运行内存形成规划性采购占位：

- Quantity = 缺口；
- 时间按冻结参数估算；
- 标记ESTIMATED / NOT_COMMITTED。

允许：

- 参与90天计划推演；
- 提供估算交期。

禁止：

- 生成采购单；
- 写ERP采购承诺；
- 生成可下发MES的Task依据；
- 作为确定性CTP承诺；
- 作为5号位正式SupplyFact落表。

---

# 5. 2↔3 规则、参数与运行治理接口冻结

## 5.1 Frozen Strategy Snapshot

3号位不按每个Demand实时返回规则裁决。

正式方式：

```text
RuleSetVersion
+
ParameterSetVersion
+
StrategyProfileVersion
↓
本次Run冻结Snapshot
↓
2号位一次装载，在内存执行
```

禁止逐笔Demand/Pegging RPC调用3号位。

## 5.2 Snapshot最小内容

### Demand排序

- Priority Segment定义
- First-match条件
- Segment内部排序字段及方向
- 稳定Tie-break规则

### Supply / Lock

- InventoryAvailabilityRule
- Demand Protection触发参数
- PI简单排序参数
- Procurement默认LT及相关参数

### Solver策略

- Forward / Backward / Mixed策略
- 动态瓶颈参数/Override
- On-time目标阈值
- Setup / Split / Overlap等必要参数
- Candidate技术Guardrail

## 5.3 运行治理调用

3号位负责：

- Run元数据创建/治理；
- ExpectedDomainKeysJson冻结；
- StrategyProfileVersion绑定；
- 白天Candidate Run创建；
- RunType / Purpose合法性；
- Candidate最小人工确认与激活边界；
- FAILED恢复时创建新的Run；
- 不改写旧FAILED历史。

2号位负责：

- 实际主流程执行；
- 每Domain计算；
- Pegging；
- 调1号位；
- 结果持久化；
- ScheduleRun结果状态回填/闭合。

不强制为了接口形式改变ScheduleRun/PlanVersion具体创建分钟，只要求现有冻结关系可追溯、DataCutoffTime一致、ExpectedDomainKeysJson正确、终态闭合。

---

# 6. 4↔3 治理、安全与运行发起接口

本节为v1.28新增正式接口边界。

## 6.1 认证（Authentication）

原2号位现有认证壳整体移交3号位。

4号位通过3号位接口完成：

- Login
- Refresh
- Logout
- Current User

原有登录接口路径如可兼容应优先保持，避免无业务价值的前端返工。

## 6.2 用户、角色、权限（RBAC）

3号位对4号位提供：

- 用户查询/维护；
- 角色查询/维护；
- Permission目录；
- 用户-角色分配；
- 角色-权限分配；
- 当前用户有效功能权限；
- 当前用户有效业务范围；
- 审计查询（按权限开放）。

## 6.3 V1业务范围权限

正式最小维度：

- Factory
- ProductFamily
- Department / ProductionDepartment
- Domain

允许组合。

不建设通用逐记录、逐字段授权DSL。

## 6.4 规则、参数、策略

4号位继续通过3号位接口维护：

- RuleSet
- ParameterSet
- StrategyProfile
- Demand Priority配置
- Diff
- Publish
- Retire
- Publish Validation
- Version History

## 6.5 白天运行发起

4号位通过3号位治理接口发起：

### 承诺交期/插单试算

```text
RunType = INSERT_ORDER_WHATIF
Purpose = CTP
或
Purpose = INSERT_IMPACT_ANALYSIS
```

### 局部重排

```text
RunType = LOCAL_RESCHEDULE
Purpose按冻结合法组合
```

### 人工重排

```text
RunType = MANUAL_RESCHEDULE
Purpose按冻结合法组合
```

白天Candidate严格单Domain。

3号位创建/治理Run后调用2号位内部排程服务；4号位不直接调用2号位。

## 6.6 Candidate确认/激活

3号位负责：

- Candidate合法性校验；
- `CanActivate`判断；
- 人工确认；
- 激活；
- 审计。

CTP和INSERT_IMPACT_ANALYSIS形成的WHATIF候选版本不得激活为ACTIVE。

## 6.7 FAILED恢复

4号位通过3号位发起恢复。

3号位：

- 校验恢复范围；
- 创建新的ScheduleRun；
- 冻结必要治理信息；
- 调用2号位主流程。

禁止：

> 将旧FAILED ScheduleRun改回RUNNING。

---

# 7. 4↔5 非治理APS业务接口

本节为v1.28新增。

## 7.1 总原则

> 4号位除3号位治理/权限接口外，其它APS业务查询和非治理类业务操作统一通过5号位业务接口接入层。

5号位可：

- 提供HTTP Controller；
- 提供前端DTO；
- 做只读聚合；
- 调2号位Query Service；
- 调2号位Application Service；
- 调5号位自身事实服务。

5号位不可复制2号位业务逻辑。

## 7.2 查询接口范围

5号位向4号位提供或中转：

- 排产总览（Overview）；
- 订单/需求计划；
- 甘特图/资源计划；
- Explanation；
- Supply/Pegging追溯；
- PI Position；
- Manual ETA；
- Demand Protection查看；
- MES资格/状态展示；
- 其它非治理APS业务结果。

其中：

- PI Position、Manual ETA属于5号位自属事实；
- Pegging、Allocation、FinalTask持久化结果等真实Owner仍是2号位；
- 5号位只负责查询接入与DTO封装。

## 7.3 Demand Protection释放

正式调用链：

```text
4号位
↓
5号位 API
↓
权限/Scope校验
↓
2号位 Demand Protection Application Service
↓
2号位实际释放
```

5号位不得直接修改保护数量或复制需求保护算法。

## 7.4 MES受控操作

需要由页面发起且业务Owner属于2号位的MES资格/受控操作：

```text
4号位
↓
5号位 API
↓
权限/Scope校验
↓
2号位 Application Service
```

仍必须遵守既有MES下发资格冻结：

- 只有ACTIVE正式版本；
- 有真实执行依据；
- Candidate不得下发；
- 无正式PI规划占位Task不得下发；
- UNLOCATED保守Task不得下发；
- 仍依赖Planning-only Purchase Placeholder且没有真实物料承诺的Task不得下发。

---

# 8. 5↔2 新增“前端业务中转调用”接口类别

本次调整不改变原5→2复杂事实输入，而是增加第二类内部调用用途。

## 8.1 第一类：复杂事实输入

继续保持：

```text
5号位复杂事实
→
2号位主流程消费
```

例如：

- PI Position；
- Procurement；
- VMI；
- Received；
- Transit；
- Manual ETA事实。

## 8.2 第二类：4号位业务接口中转

新增：

```text
4号位
→ 5号位HTTP/API
→ 2号位Query/Application Service
```

例如：

- Plan/Order/Gantt查询；
- Pegging/Allocation追溯；
- Demand Protection释放；
- MES受控操作。

## 8.3 2号位应提供的内部服务特征

要求：

- 稳定；
- 面向业务用途；
- 不直接暴露Repository；
- 不要求4号位理解2号位内部表结构；
- 查询支持合法Scope过滤；
- 写操作保持2号位事务边界。

不要求为了本次调整新增第二套远程微服务平台；在当前单体/同进程结构中可采用应用服务调用。

---

# 9. 权限上下文在4↔3↔5↔2之间的传递

## 9.1 权限Owner

3号位负责：

- 身份认证；
- 角色；
- 功能权限；
- 业务范围权限；
- 权限审计基础能力。

## 9.2 前端

4号位消费当前用户：

- Permissions
- Factory Scope
- ProductFamily Scope
- Department Scope
- Domain Scope

用于菜单、页面、按钮和筛选范围展示。

## 9.3 后端硬校验

不得只靠前端隐藏按钮。

3号位接口：

> 由3号位直接校验身份、功能权限、业务范围。

5号位接口：

> 使用3号位认证/权限上下文，校验功能权限并限定业务Scope。

5号位调用2号位时，将已经合法限定的：

- Factory
- ProductFamily
- Department
- Domain

作为业务查询/执行Scope传入。

2号位不维护用户角色规则，但必须严格按收到的合法Scope查询/执行，不自行扩大。

---

# 10. G4 / G7 / G8 / Schedule查询迁移

## 10.1 原状态

此前3号位已实现/计划封装：

- G4：ScheduleRun列表；
- G7：Domain Dependency；
- G8：Domain状态；
- Schedule versions / gantt / summary等查询。

## 10.2 v1.28正式归属

为避免长期形成两套非治理查询入口：

> **上述结果查询逐步移交5号位业务接口接入层。**

3号位保留真正的：

- Run创建；
- RunType/Purpose治理；
- Strategy绑定；
- Candidate确认/激活；
- FAILED恢复；
- 规则/参数/权限治理。

## 10.3 迁移原则

- 优先保持原URL和DTO兼容；
- 若必须变更路径/DTO，先修改接口契约；
- 不要求4号位为了Owner迁移重写页面；
- 不新建中转数据表；
- 5号位通过2号位Query Service或只读事实来源获取结果；
- 3号位停止新增同类普通计划结果查询。

---

# 11. 错误处理规范

继续继承原关键接口冻结。

## 11.1 可降级

- 单条PI Position非关键字段缺失，可UNLOCATED；
- 单条数据质量Issue可记录Issue/Explanation；
- Candidate替代资源不可用可继续搜索。

## 11.2 必须导致Domain失败

- Demand/Supply数量不闭合；
- 同一物理Supply重复消费；
- Allocation事务无法闭合；
- FinalTask违反硬资源互斥；
- Solver返回数量丢失；
- PI Position总量无法与ERP RemainingQty闭合且无法保守定位；
- 关键持久化事务失败。

## 11.3 不是技术运行失败

- 订单延期；
- 资源不足导致无法按期；
- 缺料导致最早日期变晚；
- 依赖ESTIMATED采购占位。

以上应返回业务风险/Explanation/Unscheduled等，不应把ScheduleRun技术状态错误置为FAILED。

---

# 12. 批量交接与性能红线

以下既有原则继续冻结：

- 夜间主链不采用逐Demand/逐Supply远程RPC；
- 3号位Strategy Snapshot一次冻结、批量装载；
- 5号位复杂事实批量准备；
- 2号位在内存完成Pegging主循环；
- 1号位一次Domain级Solver调用；
- 4号位接口中转职责不得把内部批量主链改成N+1远程调用；
- 5号位面向4号位的页面API可以是常规HTTP，但其内部不得逐Task调用2号位业务服务造成主链性能退化。

---

# 13. 外部系统、ODS、BOM、Routing、Realtime与MES接口继承声明

以下内容**本版不修改**，完整继承v1.26及v1.27已冻结定义：

- ERP订单/生产指示接口；
- ODS订单视图及包装视图；
- BOM Workset / Realtime BOM / RequestDetail；
- `sp_ExpandBOMRealtime_vNext`正式入口；
- RoutingOperation / RoutingDependency / Eligibility三件套；
- MES_ProcessCode_View及StageCode/ERPProperty语义；
- ProductionDepartment相关契约；
- Resource契约；
- Inventory相关接口；
- 跨厂Transit / Received接口；
- Procurement / VMI ODS接口；
- ScheduleRun / PlanVersion字段语义；
- ScopeJson 11字段Schema；
- ExpectedDomainKeysJson；
- ReasonCode字典；
- MES工序报工五态；
- Task正式五态；
- Candidate共享资源阻挡；
- 跨Domain WHATIF串行单Domain编排；
- Realtime BOM READY判断；
- 失败人工重算；
- MES下发资格。

如本节所列内容与本次新增“接口Owner”发生技术承接关系，只改变由谁向4号位暴露接口，不改变其业务语义和底层来源。

---

# 14. 接口变更控制

从v1.28开始：

1. 任何字段不足，先证明现有冻结业务无法表达；
2. 优先追加DTO字段，不优先新增物理表；
3. 任何号位不得单方面改变上下游Owner；
4. “现有代码如此”不能反向覆盖接口冻结；
5. 真正业务变化必须重新打开业务冻结，不得借接口升级完成；
6. 4号位不得自行补后端业务语义；
7. 5号位不得因中转职责复制2号位业务；
8. 3号位不得因运行治理扩张成计划结果万能代理；
9. 2号位不因退出HTTP层而减少任何核心业务责任。

---

# 15. 历史关键接口冻结文档废止声明

本v1.28已经吸收：

- 《APS V1关键接口冻结说明：1↔2、2↔5、2↔3》v1.0；
- 《APS V1关键接口冻结补充说明 Manual ETA / SupplyFact》v1.0。

因此自v1.28被0号位确认并登记进“当前有效冻结基线索引”之日起：

> **上述两份关键接口冻结文档不再作为独立有效开发基线，转为历史追溯。**

以后跨号位接口判断统一以：

> **《APS 集成接口设计规范》当前有效版本**

为正式技术接口依据。

---

# 16. v1.28最终冻结摘要

> **1↔2：2号位数量/主流程 → 1号位有限产能Solver，原接口不变。**

> **2↔3：3号位提供冻结规则/参数/策略与运行治理，2号位实际执行主流程。**

> **2↔5：5号位提供复杂事实；同时因前端中转职责，可调用2号位稳定Query/Application Service，但不得接管2号位业务。**

> **4↔3：认证、角色权限、业务范围权限、规则参数、Run/Candidate治理。**

> **4↔5：其它非治理APS业务查询、复杂事实、业务操作中转。**

> **4↔2：正式HTTP/API关系取消。**

> **4↔1：禁止。**

> **V1业务范围权限至少覆盖Factory、ProductFamily、Department/ProductionDepartment、Domain。**

> **本版只调整接口和技术Owner，不改变APS V1冻结业务。**


---

# 十八、跨版本连续性与跨厂SH当前接口契约

## 18.1 MES字段层级

工单级继续使用：

```text
MES_APS_WorkOrder_View / MESWorkOrderSnapshot
ProductionInstructionNo
MESWorkOrderNo
PlannedQty
WorkOrderStatus
SourceUpdatedAt / DataCutoffTime
```

`WorkOrderStatus`正式值：`RELEASED / IN_PROGRESS / CLOSED / DELETED`。

工单级禁止新增：

```text
RemainingQty
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

工序级继续`GoodQty / LastReportTime`并新增：

```text
MES_APS_OperationProgress_View.LastReportResourceCode
OperationProgressSnapshot.LastReportResourceCode
```

Stage级继续`GoodCompletedQty`。

## 18.2 5→2既存执行上下文

5号位基于WorkOrder/Operation/Stage、PI Position、NextOperationContext形成标准化运行上下文，至少可表达：

```text
ProductionInstructionNo
MESWorkOrderNo
WorkOrderStatus
StartStageCode
StartOperationCode
DerivedRemainingQty
LastReportResourceCode（工序级，可空）
DataCutoffTime
Issue / Evidence
```

`DerivedRemainingQty`是运行时解释，不是MES工单级原生字段。2号位不得绕过5号位重新解释MES报工。

## 18.3 2→1连续性求解契约

当前LogicalProductionDemand Q先形成。每个MES工单独立形成Continuation Slice，最少表达：

```text
ContinuationKey
ProductionInstructionNo
StartStageCode
StartOperationCode
NetOutputQty
PlannedProcessQty
NoSplitMerge（或等价语义）
PreferredResourceCode（可空）
EligibleResources
Material Quantity-Time
```

1号位不解析OldTaskNo/MESWorkOrderNo，只原样回传ContinuationKey。

## 18.4 FinalTask后的身份映射

- APS旧Task连续：继承OldTaskNo并继续原MESWorkOrderNo；
- 无TaskNo外部MES连续：生成新APS TaskNo并绑定原MESWorkOrderNo，不创建第二MES工单；
- 自由份额：新TaskNo并正常创建新MESWorkOrderNo。

## 18.5 SH订单与源工厂契约

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

ETL/标准化契约使用`SourceFactoryCode`。INTER_FACTORY缺失真实源工厂时不得静默以目标工厂兜底。

## 18.6 2↔5 SH跨厂事实

5号位只提供`ERP_InterplantInTransit_View`及真实物流事实。

```text
OPEN + 视图存在SH号 → 已进入跨厂履行，不生成第二层源厂Demand
OPEN + 视图不存在SH号 → 生成一条RequiredQty = Order.Quantity的源厂履约Demand
```

查询成功0行属于合法成功；Transit Quantity不得用于70/30拆分或倒算未生产量。

## 18.7 1↔2 SH时间契约

同Domain在同一DomainSolveRequest表达`源厂Demand → SourceReadyTime → SH跨厂LT → 目标侧后续`。

跨Domain由源Domain返回SourceReadyTime，2号位叠加冻结SH跨厂LT后形成目标Domain输入。1号位不读取ERP跨厂在途视图。

# v1.29 Supply / PI Position专项冻结对齐增量（历史形成记录；当前以v1.31正文为准）

## 1. Supply与Position接口边界

ProductionInstructionPositionSnapshot属于APS内部位置解释事实。

Position：
- 不作为独立Supply接口输出；
- 不参与Supply排序；
- 不独立生成Allocation。

ProductionInstruction仍作为Supply参与Pegging。

## 2. 事实接口职责

5号位负责提供：

- ERP ProductionInstruction事实；
- MES执行事实；
- 库存、采购、跨厂事实。

2号位负责：

- 消费事实；
- 形成APS Position解释；
- 输出Pegging计算输入。

1号位消费：

- 排程结果；
- Task结果；
- Pegging结果。

## 3. 跨厂接口边界

大工艺接续型：

ProductionInstruction Supply
+
PositionType=INTERPLANT_TRANSIT

不新增Transit Supply接口。

## 4. Domain接口语义

DomainKey表示APS真实稳定排程域标识。

涉及：
- ScheduleRun；
- PlanVersion；
- DomainDefinition；
- Domain_Dependency。

不新增：
- DomainRuntime接口；
- DomainContext接口；
- VirtualInventoryBalance接口。

## 5. V1接口冻结原则

本版本不新增未批准接口，仅同步字段语义、Owner边界和数据责任。


---

# 2026-09-04 PI Position执行起点上下文接口补充冻结

## 1. LogicalProductionDemand接口补充

LogicalProductionDemand继续由2号位生成。

新增语义字段：

|字段|责任|
|-|-|
|StartStage|由执行事实解析形成|
|StartOperation|必要时由执行事实解析形成|
|RoutingReference|2号位携带|
|NextOperationContext|来源于5号位执行上下文|

---

## 2. 调用链

```text
MES执行事实
      ↓
5号位
PI Position + NextOperationContext
      ↓
2号位
LogicalProductionDemand
      ↓
1号位
Finite Capacity Solver
```

---

## 3. Owner边界

> **v1.30当前有效职责覆盖声明**：本节覆盖v1.29“5号位仅提供ERP/MES事实、2号位自行形成APS Position解释”的旧表述。最终口径为：5号位负责PI Position业务计算及NextOperationContext事实解释；2号位负责消费结果、Demand↔PI Pegging、LogicalProductionDemand组装，以及属于ScheduleRun/PlanVersion运行事实的必要快照持久化。

5号位：

- ERP/MES/ODS事实标准化；
- PI Position业务计算；
- NextOperationContext计算；
- Issue / Evidence输出；
- 不直接写2号位PlanVersion运行结果表。

2号位：

- 消费PI Position / NextOperationContext；
- ProductionInstruction Supply Pegging；
- Allocation与数量闭合；
- LogicalProductionDemand；
- 必要运行快照持久化；
- 主流程编排。

1号位：

- 有限产能求解。

---

## 4. 接口禁止扩展

本次不新增：

- Operation级Pegging接口；
- Position Supply接口；
- Demand-Operation绑定接口。

保持：

```text
Demand ↔ ProductionInstruction Supply
```

作为V1正式业务关系。

---
