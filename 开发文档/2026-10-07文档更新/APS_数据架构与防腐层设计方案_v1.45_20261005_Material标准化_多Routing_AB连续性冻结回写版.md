# APS 数据架构与防腐层设计方案 v1.45（20261005 Material标准化 / 多Routing / A-B连续性冻结回写版）

## 版本说明

**当前版本**：v1.45  
**当前版本日期**：2026-10-05（北京时间）  
**基于版本**：v1.44（20260920 Setup换型规则 / 无设备小工序 / 人工有限产能 / 用户输出联合冻结版）  
**文档性质**：数据架构与防腐层正式变更正文；本版回写Material标准化、A/B连续性、多Routing与运行身份链，不新增第二Material/Routing/Continuation平台。  
**当前上位业务基线**：《APS V1最终全部流程与业务基线 v1.8》  
**当前技术对齐基线**：《APS集成接口设计 v1.34》+《APS数据库字段说明文档 v5.1.9》+《DDL v5.1.8.3》  
**当前状态**：正式变更态；待指导位Step 9复审，尚未冻结生效。

## 历史版本变化说明（v1.43，2026-09-15）

**修改依据**：业务基线v1.6 + 接口v1.32 + 核心走查V3.22  
**修订原则**：`ScheduleRun.ScopeJson`继续作为一次Run的运行输入快照，物理列不变；只升级内部Schema，不新建CandidateInput/ScenarioInput持久化平台。  
**影响边界**：ODS、PI Position、SH、DomainDefinition、Auth/RBAC、FinalTask真相结构不变。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | 运行输入治理 | §4.4/§十八 | ScopeJson升级为Candidate Input Schema v2。 | 单场景多对象 | 支持结构化白天输入 | 3→2运行契约 | NVARCHAR(MAX)列不变 | 影响2/3号位 |
| 2 | 单一真相 | §二十四 | BasePlanVersionId、ExpectedDomainKeysJson继续独立权威；ScopeJson不得重复制造第二真相。 | 数据治理 | 防止输入冲突 | 接口校验 | 无新表 | 影响2/3号位 |
| 3 | Owner边界 | §4/§7/§8 | 4号位采集，3号位校验冻结，2号位运行消费，1号位不解析页面Schema，5号位不拥有Candidate输入。 | 职责边界 | 防止平台扩张 | 跨号位边界明确 | 无 | 影响1～5号位 |


> **2026-09-14 P0文档纠错（业务版本保持v1.42不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

---


# 2026-10-05 本轮数据架构回写

**本轮状态**：执行修改完成，待“冻结文档修改指导对话”一致性审核；本文件尚未进入已冻结生效态。


## 本轮统一执行红线（Q1～Q4）

1. **真实RouteCode / PathId**：凡属业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的`RouteCode / PathId`必须保存真实值；`DEFAULT / 1`不得作为归一化后的业务真值。`NormalizeToSingleRoute`如存在，仅允许作为1号位内部技术适配，且必须能够恢复真实Route/Path。
2. **MES工单创建作用域**：A桶继续原MES工单；B桶FinalTask后由2号位生成新TaskNo并绑定原MES工单，不创建第二MES工单；只有C桶在FinalTask后创建新MES工单，并保存实际RouteCode/PathId/Operation身份。
3. **ContinuationKey作用域**：一个ScheduleRun内，一个MESWorkOrderNo一个且仅一个ContinuationKey；同一MES工单多个Slice共享同一Key；不得把LogicalDemandKey拼入Key造成同一MES工单裂分；不新增Continuation永久状态平台。
4. **PreferredResource软偏好**：5号位保留Operation级`LastReportResourceCode`真实事实；2号位关联当前Continuation Slice并校验当前Operation Eligibility，合法可靠时形成`PreferredResourceCode`，否则为NULL；1号位对非空值必须作为资源连续性软偏好消费，但不得升级为Hard Lock，不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。


## 新增架构链1：Material标准化

```text
ERP/MES源Material + SourceID/Warehouse/业务出口层级证据
→ 5号位ODS/防腐层标准化事实
→ APS Material标准化（普通MaterialCode / FINAL_+标准MaterialCode）
→ MaterialMapping保留源追溯
→ 2号位按标准化MaterialId进入Demand/Supply/Pegging/Routing
```

边界与落地规则：

- 普通物料保持原标准MaterialCode；出口库/最终可销售层级按既有裁决生成`FINAL_ + 原标准MaterialCode`。
- FINAL判定必须在ODS/Material标准化层基于既有出口库/最终可销售层级源事实完成；Routing、Operation名称、包装工序、路径长度不得参与FINAL猜测。
- MaterialMapping继续使用SourceID + Warehouse/Location保存源追溯，并映射到普通或FINAL标准MaterialId；一般Warehouse差异不自动生成新Material。
- MaterialType不新增FINAL枚举，统一先Strip `FINAL_`再按FG/WIP/RAW/ASSY解析。
- `ERP_Master_View / MES_Material_View`对APS暴露的MaterialCode必须已经标准化；Routing Operation / Dependency / Eligibility三件套**显式携带同一标准化MaterialCode**，并保留MES_ID/Model仅作源追溯。
- 2号位按`标准化MaterialCode → MaterialMapping → MaterialId`装载Routing；不得只靠MES_ID绑定普通/FINAL身份，不得在Routing加载后反推FINAL。
- Inventory / PI / Order / Demand / Supply / Routing统一消费标准化后的MaterialId；Routing直接绑定标准化MaterialId。
- 不建设第二Material主数据平台，不新增`IsFinal / MaterialState / WarehouseType / FinalFlag`作为第二业务真相；不新增FINAL BOM层。

## 新增架构链2：多Routing与跨版本回接

```text
5号位MES事实（WO + 真实Routing + Slices[] + LastReportResourceCode）
→ 2号位Q→A/B/C、ContinuationKey、PreferredResourceCode校验
→ A/B固定Path；C装载RoutingCandidates[]
→ 1号位内存求解（零DB）
→ FinalTask返回ExecutionBatchDraftKey + ContinuationKey? + 真实Route/Path
→ 2号位Task/TaskNo/MES持久化
→ 后续Run按历史APS下发映射或5号位事实回接
```

不建设Continuation永久平台、ExecutionBatch状态机、第二Routing平台；5号位不复制2号位运行真相。

---
## 0A. 历史v1.39 Domain修订范围（当前v1.45继续继承）

以下为v1.39历史形成记录；当前v1.45继续继承其有效Domain规则：

1. 排程域（Domain）作为稳定独立业务边界，不再等同于产品族（ProductFamily）；
2. 增加排程域定义（DomainDefinition）治理层定位；
3. 2号位按真实Domain执行归域、Pegging、逐Domain调用Solver和跨Domain运行闭环；
4. 3号位负责DomainDefinition、当前有效Domain集合、ExpectedDomainKeysJson冻结及依赖治理；
5. 1号位继续只消费单Domain求解上下文；
6. 不新增Domain运行平台、并行Worker池、资源配额或VirtualInventoryBalance。

> v1.39不改变v1.38已经冻结的4→3、4→5、Auth/RBAC/Business Scope、5号位业务接口接入及数据库Owner原则。

---

## 0. v1.38母版变更范围（历史继承）

v1.38只同步2026-09-01已经正式裁决的以下技术变化：

1. 4号位前端正式只直接调用3号位和5号位；
2. 4→2正式HTTP/API关系退出；
3. 3号位完整Own身份认证、角色权限、业务范围权限与APS_Auth；
4. 5号位增加面向4号位的非治理类APS业务接口接入/中转层；
5. G4 / G7 / G8 / Schedule普通结果查询逐步从3号位迁移至5号位；
6. 数据库结构Owner从“泛化由2号位执行”修正为“由对应业务/技术Owner负责”；
7. Manual ETA、Effective ETA、AvailableTime责任链继续按v1.37冻结，不改变；
8. 补充Business Scope在接口/查询层的数据过滤位置；
9. 明确中转层不得形成第二套运行结果真相。

**本版不改变：**

- ODS防腐架构；
- APS数据分层原则；
- BOM Workset链；
- Pegging数据模型；
- Demand/Supply数量闭环；
- SupplyFact整体模型；
- PI Position业务；
- 跨厂业务；
- 有限产能模型；
- ScheduleRun / PlanVersion业务语义；
- Candidate业务语义；
- Planning-only Purchase Placeholder原则；
- MES五态与MES业务资格；
- 原v1.37及其上位版本中未被本版明确修改的其它内容。

因此：

> **v1.38不是一次数据架构重构，而是对前端访问层、Auth归属、内部服务边界和数据库Owner的冻结补充。**

---

# 一、总体分层架构保持不变

APS V1继续采用：

```text
┌─────────────────────────────┐
│       源系统事实层            │
│ ERP / MES / WMS / 其它系统   │
└──────────────┬──────────────┘
               ↓
┌─────────────────────────────┐
│       ODS / 防腐层            │
│ MES_Integration              │
│ 5号位复杂事实Owner           │
└──────────────┬──────────────┘
               ↓
┌─────────────────────────────┐
│       APS业务事实与运行层      │
│ APS_Production / Runtime     │
│ 2号位核心排程运行Owner        │
└──────────────┬──────────────┘
               ↓
┌─────────────────────────────┐
│       有限产能求解层           │
│ 1号位纯内存Solver             │
└──────────────┬──────────────┘
               ↓
┌─────────────────────────────┐
│       运行结果持久化层         │
│ PlanVersion / FinalTask等     │
│ 2号位Owner                   │
└─────────────────────────────┘
```

本版只在其上补充：

- 治理/权限层；
- 前端业务接入层。

---

# 二、新增治理与认证权限层

2026-09-01起，3号位完整Own：

```text
Authentication
RBAC
Business Scope
Auth Audit
Rule / Parameter / Strategy
Run / Candidate Governance
```

对应正式数据结构归属：

```text
APS_Auth
    ↓
3号位Owner
```

包括：

- User；
- Role；
- Permission；
- UserRole；
- RolePermission；
- Business Scope；
- Auth Audit；
- Approval兼容结构；
- Auth相关Migration / DDL / Seed。

同时，规则、参数、策略治理结构：

- RuleSet；
- RuleSetVersion；
- ParameterSet；
- ParameterSetVersion；
- StrategyProfile；
- StrategyProfileVersion；
- 相关ContentSnapshot；

其数据库Owner也归3号位。

---

# 三、前端访问架构调整

原来可能存在：

```text
4 → 2
4 → 3
4 → 5
```

本版以后正式冻结为：

```text
                  ┌───────────────┐
                  │     4号位      │
                  │   APS Frontend │
                  └───────┬───────┘
                          │
              ┌───────────┴───────────┐
              ↓                       ↓
      ┌───────────────┐       ┌───────────────┐
      │     3号位      │       │     5号位      │
      │治理/认证/权限层 │       │业务接口接入层   │
      └───────┬───────┘       └───────┬───────┘
              │                       │
              └──────────┬────────────┘
                         ↓
                 ┌───────────────┐
                 │     2号位      │
                 │ 核心排程运行层 │
                 └───────┬───────┘
                         ↓
                 ┌───────────────┐
                 │     1号位      │
                 │ 有限产能Solver │
                 └───────────────┘
```

正式禁止：

```text
4 → 1
4 → 2 HTTP/API
```

---

# 四、4→3数据与接口边界

4号位经3号位访问：

## 4.1 身份认证

- Login；
- Refresh；
- Logout；
- Current User。

## 4.2 权限

- User；
- Role；
- Permission；
- Business Scope；
- Audit。

## 4.3 规则、参数、策略

- RuleSet；
- ParameterSet；
- StrategyProfile；
- Priority Segment；
- Diff / Publish / Retire / Validation。

## 4.4 Run / Candidate治理

4号位只负责采集用户业务输入并提交给3号位，不直接持久化Candidate运行语义。

3号位负责：

- CTP / INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate Input Schema v2校验与冻结；
- BusinessTriggerType / PriorityMode合法矩阵；
- 当前ACTIVE Base校验；
- Candidate确认/激活；
- 激活时Base过期复核；
- FAILED恢复。

`ScheduleRun.ScopeJson`定位：

> **一次ScheduleRun的运行输入快照。**

不是：

- 业务主数据；
- 第二套Scenario真相；
- Candidate长期配置；
- 人工DueDate Override主表。

新建白天Candidate使用`SchemaVersion=2`；历史缺失SchemaVersion的记录按旧v1兼容解释。

`BasePlanVersionId`与`ExpectedDomainKeysJson`继续使用ScheduleRun独立字段，不回写ScopeJson。

3号位不因此成为普通计划结果查询Gateway。

---

# 五、4→5数据与接口边界

5号位新增“非治理类APS业务接口接入/中转层”。

4号位通过5号位访问：

- Overview；
- Order / Demand Plan；
- Gantt；
- Explanation；
- Supply/Pegging Trace；
- PI Position；
- Manual ETA；
- Demand Protection查看；
- Demand Protection释放中转；
- MES资格/状态；
- MES受控操作中转；
- G4；
- G7；
- G8；
- Schedule普通结果查询；
- 其它非治理类APS业务查询。

这一层的定位是：

> **稳定前端业务接口 + 权限/Scope执行 + 页面DTO聚合 + 对2号位内部服务的调用。**

不是：

> **第二套APS业务运行层。**

---

# 六、5号位业务接入层的物理原则

5号位可以建设：

- HTTP Controller；
- Query Handler；
- Application Adapter；
- Page DTO；
- Scope Filter；
- API Contract。

不得因此新建第二套：

- PlanVersion真相表；
- ScheduleRun真相表；
- FinalTask真相表；
- Allocation真相表；
- Demand Protection真相表；
- Pegging结果镜像表。

正式运行结果仍以2号位Owner的APS运行结构为准。

---

# 七、5→2内部服务边界

5号位作为前端接入层时，通过2号位稳定内部服务获取正式运行结果。

推荐结构：

```text
4号位
    ↓ HTTP
5号位 Controller / API
    ↓
5号位 Scope / Permission Adapter
    ↓
2号位 Query Service / Application Service
    ↓
APS Runtime Truth
```

## 7.1 Query Service

至少支持：

- Plan Overview；
- Order / Demand Plan；
- Gantt / Resource Plan；
- Explanation；
- Supply/Pegging Trace；
- Demand Protection；
- MES；
- Run / Domain / Schedule普通查询。

## 7.2 Application Service

至少支持：

- Demand Protection Release；
- MES受控业务操作。

5号位不得跨库直接UPDATE 2号位核心运行表来替代Application Service。

---

# 八、3→2内部服务边界

3号位治理入口调用2号位真实运行主流程：

```text
4
↓
3号位 Governance
↓
2号位 Runtime Application Service
↓
1号位 Solver（需要时）
```

覆盖：

- CTP；
- INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate业务执行；
- FAILED恢复协作。

3号位负责：

- 身份；
- Permission；
- Business Scope；
- RunType/Purpose合法性；
- Candidate确认/激活治理；
- FAILED恢复治理。

2号位负责：

- Pegging；
- Allocation；
- Demand Protection；
- LogicalProductionDemand；
- Solver调用；
- 运行结果持久化。

---

# 九、Business Scope数据架构

V1正式支持以下业务范围：

- Factory；
- ProductFamily；
- Department / ProductionDepartment；
- Domain；
- Global。

ResourceOrgGroup如保留，为兼容维度。

## 9.1 Scope真值

Scope真值位于：

```text
APS_Auth
↓
3号位
```

5号位不建立第二套Scope表。

2号位不建立第二套Scope配置体系。

## 9.2 查询链路

推荐：

```text
3号位认证/权限上下文
        ↓
4号位持有前端展示上下文
        ↓
4 → 5请求
        ↓
5号位后端重新校验Permission + Scope
        ↓
将合法Scope传给2号位Query/Application Service
        ↓
2号位仅在合法范围内执行
```

## 9.3 Scope过滤位置

禁止：

```text
DB全量查询
→ 浏览器
→ 前端过滤
```

Scope应尽早下推到：

- 5号位Query；
- 2号位Query；
- 或相应数据库查询条件。

---

# 十、Business Scope组合规则

同一维度：

> 并集。

例如：

```text
Factory = BJ + TJ
```

不同维度：

> 共同约束。

例如：

```text
Factory IN (BJ, TJ)
AND Department = MC
AND ProductFamily IN (...)
```

V1不建设：

- 任意记录级ACL；
- 任意字段级ACL；
- ABAC表达式引擎；
- 通用权限DSL。

---

# 十一、数据库Owner总原则修正

旧的泛化理解：

> “APS数据库DDL统一由2号位执行”

正式废止。

新原则：

> **谁Own业务/技术事实，谁Own相应数据库结构；跨Owner修改必须评审。**

---

# 十二、数据库Owner矩阵

| 数据/结构 | Owner | 说明 |
|---|---|---|
| ERP/MES源系统原表 | 源系统Owner | APS不反向修改 |
| ODS复杂事实View/SP | 5号位 | 防腐层/复杂事实 |
| BOM Workset ODS侧 | 5号位 | 按既有冻结 |
| PI Position自属事实 | 5号位 | 复杂事实 |
| NextOperationContext | 5号位 | 执行起点复杂事实 |
| MES工序级LastReportResourceCode | 5号位 | 工序执行事实；不提升为工单级字段 |
| SH跨厂物流事实 | 5号位 | ODS物流事实；不是SH第二主档 |
| Procurement/VMI/Received/Transit ODS | 5号位 | 复杂事实 |
| ProcurementManualEtaOverride | 5号位 | APS人工事实 |
| APS_Auth | 3号位 | Authentication/RBAC/Scope |
| Rule/Parameter/Strategy治理表 | 3号位 | 版本与Snapshot |
| SupplyFact_Pipeline APS层 | 2号位 | 运行装载与最终供应时间 |
| PeggingSupplyAllocation | 2号位 | Pegging结果 |
| Lock / Demand Protection运行结构 | 2号位 | 核心运行真值 |
| ScheduleRun运行结果结构 | 2号位 | 运行真值 |
| PlanVersion | 2号位 | 运行真值 |
| FinalTask / TaskShare | 2号位 | 排程结果 |
| ContinuationKey运行映射 | 2号位 | 求解/身份连续性运行上下文，优先复用现有稳定键 |
| Order.SourceFactoryId | 2号位核心Order链 | SH源工厂业务身份 |
| ScheduleExplanationFact运行结果 | 2号位 | 正式结果 |
| 1号位Solver运行内对象 | 1号位 | 不作为APS DB真值 |
| 4号位 | 无核心业务DB Owner | 通过API操作 |

---

# 十三、跨Owner数据库变更规则

例如：

2号位需要 `RuleSetVersion` 新字段：

```text
2号位提出运行需求
↓
3号位评审
↓
3号位修改DDL / Migration
↓
2↔3联调
```

5号位需要2号位Plan查询新字段：

```text
5号位提出页面/接口需求
↓
2号位评审运行结果契约
↓
优先扩展Query DTO
↓
确需表字段时由2号位修改
```

禁止跨Owner直接修改对方核心表。

---

# 十四、Manual ETA数据架构继续继承v1.37

v1.37冻结内容保持不变。

## 14.1 ProcurementManualEtaOverride

定位：

> APS人工维护采购ETA覆盖事实。

属性：

- 属于APS内部事实；
- 不属于ODS；
- 不修改ERP；
- 数据库Owner为5号位。

## 14.2 正式数据流

```text
ERP ETA / ReleaseDate
        ↓
ODS供应事实（5号位）
        ↓
SupplyFact_Pipeline（2号位）
        ↑
ProcurementManualEtaOverride（5号位）
        ↓
2号位Effective ETA
        ↓
2号位AvailableTime
        ↓
Pegging
```

## 14.3 优先级

```text
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

---

# 十五、SupplyFact责任链保持冻结

## 15.1 ODS侧

5号位负责提供：

- ERP ETA；
- ReleaseDate；
- Storage/Warehouse映射；
- Quantity；
- SourceDocument；
- Procurement（采购在途/未结PO）；
- VMI；
- 采购已到厂未入库；
- 其它已冻结、确实具有独立Supply身份的供应事实。

跨厂物流事实单独保留：

- `ERP_InterplantInTransit_View`用于SH号存在性/真实物流追溯；
- PI跨厂在途用于PI Position；
- Received/ZP/BP等如源系统存在，只用于追溯/数据质量。

以上跨厂事实**不进入SupplyFact_Pipeline、不形成独立Supply/Allocation**。

## 15.2 APS运行侧

2号位负责：

- 装载/形成SupplyFact_Pipeline（仅采购在途/未结PO、VMI、采购已到厂未入库等正式管道Supply；禁止装载`INTERPLANT_IN_TRANSIT`）；
- 消费Manual ETA；
- Effective ETA；
- AvailableTime；
- SupplyBalance；
- Pegging。

## 15.3 Planning-only Purchase Placeholder

仍为2号位运行内存对象。

不得由5号位作为ODS正式SupplyFact生成。

不得进入正式PO/ERP/MES承诺。

---

# 十六、PI Position分层继续保持冻结

5号位：

> 负责“PI当前物理剩余量在哪里”。

2号位：

> 负责“选哪个PI、消费哪个Position、承接哪个Demand”。

不新建PI Header/Slice通用生命周期平台。

---

# 十七、G4 / G7 / G8 / Schedule查询迁移

正式目标：

```text
旧：
4 → 3 → 普通计划结果

新：
4 → 5 → 2
```

迁移原则：

1. 优先保持URL兼容；
2. 优先保持DTO兼容；
3. 3号位停止新增普通结果Gateway职责；
4. 5号位不复制运行结果；
5. 2号位仍是真实运行结果Owner；
6. 迁移不改变业务语义。

---

# 十八、普通查询与治理动作必须分开

例如：

## Run列表

```text
4 → 5
```

## FAILED恢复

```text
4 → 3 → 2
```

## PlanVersion普通结果

```text
4 → 5
```

## Candidate确认/激活

```text
4 → 3 → 2
```

不得为了接口统一，把治理全部迁5号位。

---

# 十八A、Candidate运行输入Schema v2数据架构

## 18A.1 结构

物理层继续：

```text
ScheduleRun.ScopeJson NVARCHAR(MAX)
```

v2可包含：

```text
SchemaVersion
BusinessTriggerType
PriorityMode
OrderCanonicalIds
OrderTargets[]
TaskTargets[]
ChangedResourceIds[]
FactoryIds
ProductFamilyIds
ResourceGroupIds
PlanHorizonStart
PlanHorizonEnd
LockedTaskIds
AllowTouchFrozenZone
AllowDelaySalesOrder
MaxImpactedOrders
```

## 18A.2 单一真相

- `BasePlanVersionId`：Candidate基准ACTIVE的唯一权威；
- `ExpectedDomainKeysJson`：本次预期Domain集合唯一权威；
- `OrderTargets[]`：人工计划订单+目标日期权威输入；
- `TaskTargets[]`：甘特Task+目标时间权威输入；
- `ChangedResourceIds[]`：本次变化Seed，不是资源事实真相。

如果`OrderCanonicalIds`与`OrderTargets[]`同时存在，前者只能是后者订单ID的兼容投影。

## 18A.3 Owner

```text
4号位：采集
3号位：校验/冻结/审计
2号位：运行消费/形成Seed
1号位：消费2号位组装后的DomainSolveRequest
5号位：只查询Candidate结果，不拥有输入
```

## 18A.4 明确不新增

- CandidateInput表；
- ScenarioInput表；
- PriorityMode物理列；
- BusinessTriggerType物理列；
- OrderTarget长期表；
- TaskTarget长期表；
- Candidate专属Calendar表。

# 十九、权限与业务资格必须分层

例如Demand Protection Release：

```text
3号位
→ 用户身份 / Permission / Scope

5号位
→ 后端再次校验并中转

2号位
→ 最终业务可释放条件
```

权限允许 ≠ 业务一定允许。

MES同理。

---

# 二十、防腐层原则不因前端接入改变

原防腐层继续承担：

- 屏蔽ERP/MES物理表变化；
- 统一字段契约；
- 批量读取；
- 复杂事实标准化；
- SourceKey稳定；
- 问题数据隔离；
- 业务主键稳定。

5号位新增前端API职责后，不能：

> 把ODS层直接暴露给4号位。

前端业务API应返回业务DTO，而不是ODS裸表/裸View。

---

# 二十一、禁止的数据流

以下正式禁止：

## 21.1 前端直库

```text
4 → APS DB
```

禁止。

## 21.2 前端直2

```text
4 → 2 HTTP
```

不再是正式V1路径。

## 21.3 5号位复制运行真相

```text
2号位FinalTask
→ 复制到5号位FrontendFinalTask
```

禁止作为第二套真相。

## 21.4 3号位复制普通结果

```text
PlanVersion / FinalTask
→ 3号位长期镜像
```

不建设。

## 21.5 Scope浏览器过滤

```text
全量数据
→ 4号位
→ JavaScript过滤
```

禁止。

---

# 二十二、性能原则

本版不改变夜间：

- 02:00 FULL；
- 约10万Task；
- 目标15分钟收敛；

的既有要求。

新增接口层必须遵守：

1. Query服务端分页；
2. Gantt按时间窗/资源加载；
3. Explanation按需；
4. Supply Trace按订单/需求；
5. Scope尽早下推；
6. 不逐Task远程调用；
7. 不在5号位复制90天全量运行数据；
8. Current User / Permission / Scope上下文合理缓存，但不能成为第二权限真值。

---

# 二十三、部署与物理边界

本版不要求为了职责划分强制拆成独立微服务。

“3号位”“5号位”“2号位Owner”描述的是：

> **代码责任、业务责任、数据库Owner和接口边界。**

不强制：

- 每号位一个进程；
- 每号位一个服务器；
- Service Mesh；
- API Gateway；
- 消息总线重构。

可以继续按现有.NET工程和部署实际进行最小调整。

---

# 二十四、数据一致性最高原则

## 24.1 单一业务真相

- Auth真相：3号位 / APS_Auth；
- 规则参数策略真相：3号位；
- 复杂源事实真相：5号位/ODS；
- Manual ETA真相：5号位；
- Pegging/Allocation真相：2号位；
- AvailableTime真相：2号位；
- ScheduleRun/PlanVersion运行真相：2号位；
- FinalTask真相：2号位；
- Solver时间/资源结果：1号位计算，2号位持久化；
- 页面：4号位只消费正式API。

## 24.2 中转不生成第二真相

3号位或5号位中转API：

> 只负责接入、治理、授权、聚合、转译。

不改变底层Owner。

---

# 二十五、V1不新增的架构

本次调整不建设：

- 通用API Gateway；
- BFF平台；
- Service Mesh；
- 通用事件总线；
- 第二套数据仓库；
- 前端运行结果镜像库；
- 第二套RBAC；
- 第二套Business Scope；
- ABAC平台；
- Row-Level ACL平台；
- Field-Level ACL平台；
- MultiDomain Candidate；
- Logistics Solver；
- 第二套Pegging；
- 第二套Allocation；
- 第二套ScheduleRun状态机。

---

# 二十六、架构验收清单

| 编号 | 验收项 | 必须结果 |
|---|---|---|
| DA01 | 4→2 HTTP | 正式路径退出 |
| DA02 | 4→1 | 不存在 |
| DA03 | Auth | 3号位Owner |
| DA04 | APS_Auth DDL | 3号位Owner |
| DA05 | Rule/Parameter/Strategy DDL | 3号位Owner |
| DA06 | ODS复杂事实 | 5号位Owner |
| DA07 | Manual ETA | 5号位Owner |
| DA08 | Effective ETA | 2号位计算 |
| DA09 | AvailableTime | 2号位计算 |
| DA10 | Pegging/Allocation | 2号位Owner |
| DA11 | Solver | 1号位纯内存 |
| DA12 | FinalTask持久化 | 2号位Owner |
| DA13 | Overview | 4→5→2 |
| DA14 | Gantt | 4→5→2 |
| DA15 | Explanation | 4→5→2 |
| DA16 | PI Position | 4→5 |
| DA17 | Demand Protection查看 | 4→5→2 |
| DA18 | Demand Protection释放 | 4→5→2 |
| DA19 | MES业务操作 | 4→5→2 |
| DA20 | CTP | 4→3→2 |
| DA21 | Candidate治理 | 4→3→2 |
| DA22 | G4/G7/G8/Schedule普通查询 | 迁5 |
| DA23 | Business Scope真值 | 3号位 |
| DA24 | Scope后端过滤 | 5/2执行，不只前端 |
| DA25 | 第二权限体系 | 不存在 |
| DA26 | 第二运行结果表 | 不存在 |
| DA27 | Placeholder | 仍为2号位运行内存 |
| DA28 | ODS架构 | 未因本版重构 |
| DA29 | Pegging数据模型 | 未改变 |
| DA30 | 有限产能模型 | 未改变 |

---

# 二十七、与v1.37的关系

v1.37中以下内容继续完全有效：

- `ProcurementManualEtaOverride`定位；
- Manual ETA属于APS内部事实；
- Manual ETA不属于ODS；
- Manual ETA不修改ERP；
- 5号位负责ODS供应事实、ERP字段映射和Manual ETA事实；
- 2号位负责Effective ETA、AvailableTime和Pegging使用；
- ReleaseDate为ERP来源事实字段；
- AvailableTime为APS统一可用时间；
- CommitmentStatus不用于Planning-only Purchase Placeholder。

本版只是增加：

- Auth层；
- 前端接入层；
- 内部Service层；
- Business Scope数据流；
- 数据库Owner矩阵。

---

# 二十八、最终冻结口径

> **APS V1的数据架构仍然以“源系统 → ODS防腐层 → APS业务事实/运行层 → 1号位有限产能Solver → 2号位运行结果持久化”为主干。2026-09-01新增的只是其上方的治理、安全和前端业务接入边界：3号位拥有Authentication、RBAC、Business Scope、Rule/Parameter/Strategy和Run/Candidate治理；5号位在原复杂事实Owner基础上成为4号位非治理APS业务的统一接口接入/中转层；4号位只直接调用3号位和5号位；2号位退出4-facing HTTP和Auth，但继续拥有Pegging、Allocation、Demand Protection、Effective ETA、AvailableTime、ScheduleRun/PlanVersion运行结果和FinalTask等核心真相。任何中转层都不得复制第二套运行结果或第二套权限真相。**

---

# 2026-09-01 Domain专项冻结增量（本版新增，冲突时仅此专项口径覆盖历史Domain表述）

# APS 数据架构与防腐层设计方案 v1.39
## Domain专项冻结对齐版

版本：v1.39  
日期：2026-09-01（北京时间）  
基于版本：v1.38  
修订性质：Domain专项一致性修订

---

# 0. 修订原则

本版严格继承v1.38：

保持不变：

- ODS防腐层；
- APS事实层；
- SupplyFact；
- Manual ETA；
- AvailableTime；
- Pegging数据链；
- 有限产能数据链；
- APS_Auth职责；
- Rule/Parameter/Strategy治理边界；
- 数据Owner原则。

本次只补充Domain运行所需的数据承接。

不建设：

- 通用Domain平台；
- Domain DSL；
- 任意条件表达式；
- 第二套规则引擎；
- VirtualInventoryBalance；
- Domain资源配额平台。

---

# 1. Domain数据定位

Domain属于：

> APS内部排程基础配置。

Domain不是：

- ERP主数据；
- MES事实数据；
- ODS同步事实；
- RuleSet；
- ParameterSet。

因此：

DomainDefinition由APS维护。

---

# 2. DomainDefinition数据职责

DomainDefinition用于描述：

- 一个排程域是什么；
- 包含哪些Factory；
- 包含哪些ProductFamily；
- 当前是否启用。

V1支持：

## FAMILY

范围：

ProductFamily。

## FACTORY_FAMILY

范围：

Factory + ProductFamily。

不支持：

- Customer Domain；
- Stage Domain；
- Resource Domain；
- SQL条件Domain。

---

# 3. DomainDefinition归属

Owner：

3号位。

职责：

- 维护接口；
- 权限控制；
- 审计；
- 启停管理。

3号位不负责：

- Order实际归域；
- Pegging；
- Solver。

---

# 4. Order / Demand归域数据流

正式链路：

```
DomainDefinition
        ↓
2号位运行加载
        ↓
Order/Demand业务属性
        ↓
Domain匹配
        ↓
DomainKey
        ↓
Domain执行上下文
```

归域结果属于：

> 当前ScheduleRun运行上下文。

不是永久事实。

---

# 5. ScheduleRun Domain冻结

ScheduleRun启动时需要保存：

本次实际计算Domain集合。

例如：

```text
ScheduleRun
    |
    +-- ExpectedDomainKeysJson
```

作用：

- 保证本次运行稳定；
- 防止运行中Domain配置变化影响结果。

---

# 6. Domain_Dependency数据语义

Domain_Dependency表示：

> 两个真实排程域之间的业务依赖关系。

节点必须：

使用：

```text
DomainKey
```

禁止：

```text
ProductFamily.Code
```

代替Domain。

---

# 7. Domain执行上下文

Domain执行过程中需要形成运行上下文。

包含：

- DomainKey；
- Demand范围；
- Supply范围；
- Quantity-Time输入；
- StrategyProfile；
- Resource约束；
- ExternalDomainResourceBlocks。

该上下文：

属于：

当前ScheduleRun内存运行对象。

不作为长期业务事实保存。

---

# 8. 跨Domain Quantity-Time

继续保持原冻结：

跨Domain传递：

- Material；
- Quantity；
- AvailableTime。

不建设：

VirtualInventoryBalance。

Quantity-Time只是：

> 当前ScheduleRun内部传递上下文。

---

# 9. 共享Resource数据边界

FULL运行时：

前序Domain产生：

共享Resource占用。

后续Domain消费：

ExternalDomainResourceBlocks。

该信息：

属于：

当前ScheduleRun运行状态。

不建设：

- Domain资源配额；
- Resource借用关系；
- 虚拟设备日历。

---

# 10. 数据Owner

| 数据 | Owner |
|---|---|
| DomainDefinition | 3号位 |
| Domain权限范围 | 3号位 |
| Order/Demand归域结果 | 2号位运行数据 |
| Domain执行上下文 | 2号位运行内存 |
| Domain Pegging结果 | 2号位 |
| Domain PlanVersion | 2号位 |
| Solver输入输出 | 1号位/2号位边界 |

---

# 11. 与现有架构关系

新增：

```
APS DomainDefinition
        ↓
ScheduleRun Domain Snapshot
        ↓
Domain Execution Context
        ↓
既有Pegging
        ↓
既有限产能Solver
```

不是：

```
Domain
↓
新Pegging
↓
新Solver
```

---

# 12. 本版明确不新增

不新增：

- Domain规则表；
- Domain脚本表；
- Domain表达式引擎；
- Domain自动学习；
- Domain资源池；
- MultiDomain Candidate表；
- VirtualInventoryBalance表。

---

# 13. 最终架构口径

> APS V1中的Domain是APS内部排程基础配置和运行隔离边界。DomainDefinition由3号位治理，ScheduleRun启动时冻结有效Domain集合，2号位根据业务事实完成实际归域并组织Domain执行上下文。Domain数据只服务于既有Pegging和有限产能流程，不形成新的业务真相，不改变原有APS数据架构。


---

# 历史v1.42新增数据架构规则（当前v1.45继续继承）

## A. 跨版本连续性事实链

```text
MES WorkOrder / OperationProgress / StageProgress
↓
ODS / 5号位事实解释
↓
PI Position + NextOperationContext + ExistingExecutionContext
↓
2号位当前Q形成后的Continuation / Free
↓
1号位有限产能Solver
```

TaskNo身份链与MES事实链分开；无TaskNo不影响真实MES报工进入事实解释。

## B. 工单级与工序级字段分层

工单级`MES_APS_WorkOrder_View / MESWorkOrderSnapshot`继续承载PI号、MES工单号、PlannedQty、WorkOrderStatus、SourceUpdatedAt/DataCutoffTime。

工单级明确不新增：

```text
RemainingQty
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

工序级正式增加：

```text
MES_APS_OperationProgress_View.LastReportResourceCode
OperationProgressSnapshot.LastReportResourceCode
```

继续保留`GoodQty`；Stage级继续`GoodCompletedQty`。

## C. Continuation不建设持久化平台

Continuation Slice属于当前ScheduleRun运行上下文，可通过DTO、LogicalProductionDemand、DomainSolveRequest、ScheduleContext及现有稳定关联键传递。

V1不新增ContinuationHeader、ContinuationSlice永久表、ExecutionContinuation平台、Operation Supply或Operation Allocation。

## D. SH主档与物流事实分层

SH主档来自Order：

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

5号位`ERP_InterplantInTransit_View`只作为跨厂物流事实及SH号存在性事实；不是SH主档、SH总量、Demand↔SH关系或SourceReadyTime权威源。

对OPEN SH：视图有SH号表示已进入跨厂履行；视图无SH号表示尚未进入跨厂履行。查询成功0行合法。Transit Quantity不得用于`Order.Quantity-TransitQty`。

## E. SourceFactory数据链

```text
ERP_Order_Staging.SourceFactoryCode
→ Order_Canonical.SourceFactoryCode
→ Order.SourceFactoryId
```

普通非跨厂订单可`SourceFactoryId=FactoryId`；INTER_FACTORY必须是真实源工厂，缺失时形成Issue/阻断，不静默回填目标工厂。

第二层源厂履约Demand按SourceFactoryId重新归Domain，但不得永久改写原Order.FactoryId/DomainKey。

## F. SH整单时间边界

同一SH目标侧只保留一个整单AvailableTime。SourceReadyTime来自1号位真实有限产能结果；标准SH跨厂LT来自3号位冻结参数；ERP真实发运/到达事实由5号位提供并优先。

普通跨Domain Quantity-Time分段继续保留。

# 2026-09-03 Supply / PI Position专项冻结历史形成记录（有效规则继续继承）

## 一、修订性质

本次修订属于冻结边界澄清，不新增业务，不重新设计APS V1架构。

保持不变：
- Domain归域模型；
- Demand排序；
- Allocation规则；
- Demand Protection；
- 有限产能Solver边界；
- 1号位职责。

仅同步：
- Supply身份；
- ProductionInstructionPositionSnapshot定位；
- 跨厂事实表达；
- Received边界；
- 2/5号位职责。

## 二、Supply与Position边界

Supply表示可以被Demand直接承接的供给对象。

Position表示已有Supply的执行位置、状态或数量分布。

Position不形成新的Supply，不参与Supply排序，不独立产生Allocation。

ProductionInstruction作为Supply参与Pegging。
ProductionInstructionPositionSnapshot用于描述：
- PositionType；
- Quantity；
- AvailableTime；
- 执行状态。

## 三、PI Position事实模型

ProductionInstructionPositionSnapshot定位为：

> 某一次ScheduleRun/PlanVersion计算周期内，生产指示位置状态事实快照。

不是：
- 永久历史事件表；
- Position变化流水表；
- 独立历史版本体系。

V1不新增APS_PI_Position_Fact。

## 四、ODS生产指示事实

增加ODS事实对象：

ODS_ProductionInstructionInventoryBalance

定位：ERP事实来源，用于PI Position解析。

不用于：
- 重新计算PI RemainingQty；
- 形成Supply；
- 保存APS计算结果。

## 五、跨厂事实边界

ERP_InterplantInTransit_View表示跨厂转运事实，不天然形成Supply。

生产指示型跨厂：

ERP_InterplantInTransit_View → PI Position计算 → ProductionInstructionPositionSnapshot

禁止形成Transit Supply。

Received属于ERP事实，不生成独立Supply，不参与Allocation。

## 六、职责补充

5号位负责：
- PI Position计算规则；
- Position生成；
- 数量拆分；
- UNLOCATED判断。

2号位负责：
- 加载ODS事实；
- 调用5号位计算；
- 保存ProductionInstructionPositionSnapshot；
- Pegging消费。

## 七、版本变化说明

|编号|类型|内容|
|-|-|-|
|1|冻结补充|增加Supply与Position边界|
|2|事实模型修正|明确PI Position Snapshot与ODS事实边界|
|3|职责调整|明确2号位保存、5号位计算|

---

# v1.41 PI Position执行起点上下文专项冻结增量（历史形成记录；当前v1.45继续继承）

## 一、本版本修改定位

本版本不改变原有数据架构、防腐层分层、ODS边界和数据库Owner原则。

仅补充：

```text
MES执行事实
+
Routing
+
PI Position
        ↓
5号位
NextOperationContext
        ↓
2号位
LogicalProductionDemand
        ↓
1号位
有限产能Solver
```

的数据架构表达。

---

## 二、PI Position与NextOperationContext职责边界

### 1. 5号位职责

5号位负责复杂制造事实解释：

输入：

- ERP生产指示事实；
- MES工序/Stage执行事实；
- Routing工艺关系；
- XC、跨厂在途等强位置事实。

输出：

- PI Position；
- NextOperationContext；
- Issue / Evidence。

其中：

PI Position回答：

> 当前PI剩余数量在哪里。

NextOperationContext回答：

> 当前数量下一步从哪个Stage，必要时哪个Operation继续。

---

### 2. 2号位职责

2号位负责运行链消费：

- Demand↔ProductionInstruction Supply Pegging；
- Allocation数量闭合；
- 消费PI Position与NextOperationContext；
- 形成LogicalProductionDemand；
- 形成DomainSolveRequest；
- 持久化属于ScheduleRun/PlanVersion的运行快照。

2号位不得重新读取MES事实判断：

- 当前完成工序；
- 下一执行工序；
- PI所在位置。

---

### 3. 1号位职责

1号位只消费：

- StartStageCode；
- StartOperationCode（必要时）；
- Routing上下文；
- Resource能力；
- Calendar；
- Quantity-Time约束。

1号位不得：

- 读取MES；
- 重新计算PI Position；
- 重新计算NextOperationContext。

---

## 三、防腐层调整

新增事实解释边界：

```text
MES原始事实
      ↓
ODS防腐层
      ↓
5号位事实解释层
      ↓
APS运行上下文
```

其中：

MES原始表：

- 保留原始事实；
- 不直接暴露给1号位Solver。

5号位：

- 完成业务语义转换；
- 输出APS可消费事实。

APS运行层：

- 消费稳定业务语义；
- 不依赖MES内部状态编码。

---

## 四、数据库边界说明

本专项不新增：

- NextOperationContext独立生命周期表；
- Operation Position表；
- Operation Supply表；
- Operation级Allocation表。

原因：

NextOperationContext属于：

> 排程执行起点上下文。

不是：

- Supply事实；
- 库存事实；
- Position实体；
- Allocation实体。

可通过：

- DTO；
- ScheduleContext；
- LogicalProductionDemand；
- DomainSolveRequest；

完成运行传递。

---

## 五、数据Owner最终口径

| 数据 | Owner |
|---|---|
| MES原始执行事实 | ODS/5号位输入 |
| PI Position业务计算 | 5号位 |
| NextOperationContext业务计算 | 5号位 |
| ProductionInstruction Supply | 既有Supply Owner |
| Demand↔PI Pegging | 2号位 |
| LogicalProductionDemand | 2号位 |
| Solver输入上下文 | 2号位组装 |
| FinalTask | 1号位 |

---

## 六、禁止回流设计

本专项明确禁止：

1. 2号位建设第二套MES进度解释逻辑；
2. 1号位读取MES判断执行起点；
3. 将NextOperationContext升级为Supply；
4. 将Operation加入Pegging层；
5. 因NextOperationContext新增独立Position平台；
6. 因本专项提前修改DDL。

---

## 七、本版本完成定义

数据架构层满足：

- ODS仍保持事实防腐职责；
- 5号位承担复杂制造事实解释；
- 2号位消费稳定业务事实形成排程输入；
- 1号位只解决资源和时间问题；
- PI Position、NextOperationContext、Pegging、Solver边界清晰；
- 不引入新的数据库实体。

因此：

> 本次PI Position执行起点上下文专项需要修改数据架构与防腐层设计，但不要求修改数据库DDL。

---


---

# v1.44联合冻结增量：Setup换型规则、无设备小工序与人工有限产能、用户输出同步

> 本增量基于v1.43数据架构冻结版本，仅同步已冻结业务变化，不改变原有ODS防腐层、PI Position、SH、Domain、Auth/RBAC、FinalTask真相结构。

## 一、Operation规划模式数据语义

APS V1不再以“是否存在设备”作为工序是否进入有限产能排程的唯一判断。

新增Operation规划模式（Operation Planning Mode）：

|模式|中文含义|数据语义|
|-|-|-|
|FINITE_RESOURCE|有限资源约束模式|进入有限产能Solver，占用Resource Timeline|
|UNCONSTRAINED|非约束模式|保留工艺时间，不占有限资源|
|WAIT_ONLY|等待模式|表达等待时间，不占Resource|

OperationPlanningMode属于工艺排程属性，不属于Solver临时参数。

## 二、Resource与Resource Calendar边界

明确：

- Resource基础事实可以来自MES/设备主数据；
- Resource能力资格来自工艺与资源匹配事实；
- Resource Calendar属于APS排程事实；
- 人工Resource Slot由APS维护。

MES提供资源事实，不等同于拥有APS排程日历。

## 三、Setup换型数据边界

Setup定义为：

同一Resource连续执行两个生产任务时，由前任务状态转换到后任务状态产生的准备时间。

Setup属于Resource Timeline运行约束。

计算依据：

- ProductionDepartment；
- Stage；
- CurrentOperation；
- Resource；
- FromMaterial；
- ToMaterial。

V1不建设独立Setup运行结果平台。

## 四、无设备小工序数据语义

无设备不代表无排程。

规则：

- 有人工能力约束 → FINITE_RESOURCE；
- 仅表达工艺持续时间 → UNCONSTRAINED；
- 仅表达等待 → WAIT_ONLY。

不新增：

- Operation Supply；
- Operation Pegging；
- Operation级库存模型。

## 五、PI Position与NextOperationContext边界补充

保持：

5号位：

- PI Position计算；
- NextOperationContext计算。

2号位：

- 消费；
- Pegging；
- Allocation；
- 运行快照保存。

NextOperationContext属于排程执行起点上下文，不升级为Supply或Position实体。

## 六、执行连续性事实边界补充

保持：

MES工序执行事实：

↓

ODS/5号位事实解释：

↓

Continuation运行上下文：

↓

2号位排程输入：

↓

1号位Solver。

禁止：

- 工单级LastReportResourceCode；
- 工单级RemainingQty；
- 工单级ExecutionStatus。

LastReportResourceCode仅属于工序级执行事实。

## 七、用户输出解释事实边界

新增数据边界：

- 1号位负责排程阶段捕获延期原因；
- 2号位负责结果事实持久化；
- 5号位负责查询接口提供。

解释事实不形成第二套运行真相。

## 八、数据Owner补充

|数据|Owner|
|-|-|
|OperationPlanningMode|工艺/规则治理Owner|
|Setup Rule|3号位治理|
|Resource事实|5号位接入|
|Resource Calendar|APS运行侧维护|
|PI Position|5号位计算、2号位消费|
|NextOperationContext|5号位计算、2号位消费|
|FinalTask|2号位运行结果Owner|

---

# v1.44冻结说明

本版本不新增：

- CandidateInput表；
- ScenarioInput表；
- Continuation永久平台；
- OperationSupply；
- OperationPegging；
- VirtualInventoryBalance；
- SetupRuntime结果表。

本版本仅完成：

业务冻结 → 数据架构 → 接口/字段/DDI后续同步

的数据架构收口。
