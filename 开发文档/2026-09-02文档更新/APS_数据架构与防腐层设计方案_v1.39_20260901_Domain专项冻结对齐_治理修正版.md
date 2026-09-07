# APS 数据架构与防腐层设计方案 v1.39（20260901 Domain专项冻结对齐版）

## 版本说明

**当前版本**：v1.39  
**当前版本日期**：2026-09-01（北京时间）  
**基于版本**：v1.38（20260901职责与接口冻结对齐版）；v1.38基于v1.37  
**文档性质**：数据架构与防腐层当前冻结文档（完整继承v1.38并追加Domain专项）

---

## 0A. v1.39当前版本修订范围

v1.39只在v1.38完整内容上同步2026-09-01 Domain专项：

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

- CTP Run发起；
- INSERT_IMPACT_ANALYSIS；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- Candidate确认；
- Candidate激活；
- FAILED恢复。

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
- Procurement；
- VMI；
- Received；
- Transit；
- 其它冻结供应事实。

## 15.2 APS运行侧

2号位负责：

- 装载/形成SupplyFact_Pipeline；
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
