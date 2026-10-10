# APS V1 0号位总体项目验收包（20260915 白天业务场景 / Candidate人工调整冻结收口版）

**版本**：v1.4  
**日期**：2026-09-15（北京时间）  
**适用对象**：项目总负责人、技术负责人、各号位开发负责人、最终验收人员


**当前专项上位基线（2026-09-15）**：

- 《APS V1最终全部流程与业务基线 v1.6》；
- 《APS Pegging供需承接与分层计算业务说明 v1.5》；
- 《APS 有限产能排产与滚动90天计划业务说明 v1.5》；
- 《APS 集成接口设计 v1.32》；
- 《APS 核心排产全流程走查 V3.22》；
- 《APS V1 2号位增量开发实施包 v1.7》；
- 《APS V1 5号位复杂业务事实、ODS与业务接口接入开发实施包 v1.5》；
- 《APS V1 1号位有限产能排程开发实施包 v1.4》；
- 《APS V1 3号位规则、参数、认证权限与运行生命周期开发实施包 v1.5》；
- 《APS 数据架构与防腐层设计方案 v1.43》；
- 《APS 数据库字段说明文档 v5.1.7》；
- 《APS 数据库表结构设计 DDL v5.1.7》；
- 《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》。

> 本版完整继承v1.3、2026-09-14 SH纠错及此前全部验收口径；本轮新增白天业务场景/Candidate人工调整验收。既有PI/SH、跨版本连续性、Domain、90天唯一Solver、Candidate单Domain、权限与接口Owner不重新打开。

## 当前版本变化说明（2026-09-15）

**修改依据**：业务基线v1.6 + 核心走查V3.22 + 1～5号位当前实施包 + 字段/DDL v5.1.7  
**修订原则**：本包只定义验收，不重新设计业务；新增白天业务场景、Candidate Input v2、ACTIVE Base与人工调整回归。  
**影响边界**：跨版本连续性、SH、PI Position、Domain等历史验收继续有效。

| 编号 | 类型 | 章节 | 内容 | 原因/依据 | 业务影响 | 接口影响 | 数据库影响 | 代码影响 |
|---|---|---|---|---|---|---|---|---|
| 1 | 验收新增 | 第26章 | 增加10类白天业务场景与单场景多对象验收。 | v1.1最终论证 | 白天操作闭环 | 覆盖4→3→2→1→5 | ScopeJson v2 | 全体 |
| 2 | Candidate治理 | 第26章 | 当前ACTIVE Base、禁止Candidate叠Candidate、旧Base拒绝激活。 | 版本一致性 | 防旧计划覆盖 | Run/激活门禁 | 复用Base字段 | 2/3/4 |
| 3 | Scope/Seed | 第26章 | Business Scope外不可移动、Seed不屏蔽其它真实事实。 | 权限/求解边界 | 防越权和假局部 | 2→1上下文 | 无 | 1/2/3 |
| 4 | 数据库验收 | 第26章 | ScopeJson v2但不新建Candidate输入表/物理场景列。 | 防过度设计 | 无第二平台 | DTO/JSON契约 | DDL轻量升级 | 2/3 |


> **2026-09-14 P0文档纠错（业务版本保持v1.3不变）**：本次只清理旧SH三段式模型残留，不改变2026-09-13已冻结业务。厂间SH继续保持“一张SH一个Supply + 整单状态判断 + 必要时一条RequiredQty=Order.Quantity源厂Demand”；`ERP_InterplantInTransit_View`只作为跨厂物流事实/SH号存在性判断，不得进入`SupplyFact_Pipeline`或以`INTERPLANT_IN_TRANSIT`形成独立Supply；Received/ZP/BP如源系统存在，仅可用于追溯/数据质量，不参与SH数量切片、Supply或Allocation。

---

# 1. 项目定位

APS V1目标：

> 建设面向制造现场的订单驱动、供需闭环、有限产能约束、多目标优化排程能力，实现从需求、供给、Pegging到生产任务的完整计划闭环。

V1重点解决：

- 多来源需求统一；
- 多层BOM供给追溯；
- 库存、在制、采购、跨厂等供给统一承接；
- 交期可靠性判断；
- 有限产能约束排程；
- 生产任务生成；
- MES执行资格控制。

---

# 2. V1最终业务范围

## 已纳入

### 供需匹配

包括：

- Sales Order；
- Production Instruction；
- 库存；
- 在制；
- 采购；
- VMI；
- 已到厂未入库；
- 跨厂供给。

### Pegging

包括：

- Demand/Supply匹配；
- Allocation；
- PI Position承接；
- Lock；
- Demand Protection；
- 数量闭环。

### 有限产能

包括：

- 90天滚动计划；
- Firm近端计划；
- Resource约束；
- Setup；
- Overlap；
- Multi-objective优化；
- FinalTask。

### 执行连接

包括：

- MES资格判断；
- 任务追溯；
- Explanation；
- 异常原因。

---

# 3. V1明确不包含

不纳入V1：

- 通用APS平台化建设；
- V2 AI优化能力；
- 通用规则DSL平台；
- 第二套Pegging引擎；
- 第二套Solver；
- Logistics Solver；
- MultiDomain Candidate平台；
- 通用事件驱动平台；
- 第二套权限系统。

---

# 4. 总体架构冻结

## 数据链路

```
ERP/MES/WMS
      ↓
ODS防腐层
      ↓
5号位 PI Position + NextOperationContext
      + MES工单/工序/Stage标准化执行事实
      + 跨厂物流事实
      ↓
2号位 第一层Pegging
      ↓
（适用时）SH单一Supply / 第二层源厂履约Demand
      ↓
2号位 LogicalProductionDemand Q
      ↓
2号位 Continuation Slice + Free Slice
      ↓
1号位 有限产能Solver
      ↓
FinalTask + ContinuationKey
      ↓
2号位 TaskNo / MESWorkOrderNo身份治理
      ↓
MES继续执行或创建新工单
```

---

# 5. 角色职责冻结

## 0号位

负责：

- 总体架构；
- 业务冻结；
- 跨团队协调；
- 最终验收。

不替代各号位开发Owner。

---

## 1号位

负责：

- 有限产能Solver；
- 消费StartStage / StartOperation执行起点；
- 从指定Routing节点继续排程；
- Resource能力模型；
- Task计算；
- 时间资源约束求解；
- Continuation Slice与Free Slice联合有限产能求解；
- ContinuationKey原样回传；
- 工序级LastReportResourceCode作为PreferredResource消费；
- SH整单SourceReadyTime计算。

不负责：

- Pegging；
- MES原始执行事实解释；
- PI Position计算；
- NextOperationContext推导；
- 权限；
- 前端；
- MESWorkOrderNo / TaskNo身份解释；
- SH第一层/第二层Pegging；
- ERP跨厂在途视图读取。

---

## 2号位

核心运行Owner。

负责：

- SchedulingOrchestrator；
- Demand/Supply运行；
- Pegging；
- Allocation；
- Demand Protection业务执行；
- Effective ETA；
- AvailableTime；
- LogicalProductionDemand；
- 当前Q形成后的IN_PROGRESS既存执行覆盖识别；
- 逐MES工单Continuation Slice / Free Slice分桶；
- ContinuationKey与TaskNo/MESWorkOrderNo身份映射；
- SH第一层单一Supply Allocation、第二层源厂履约Demand及SourceFactoryId重新归域；
- 消费PI Position / NextOperationContext并组装执行起点；
- Solver调用；
- FinalTask持久化；
- ScheduleRun；
- PlanVersion。

退出：

- Auth Owner；
- 4号位直接HTTP/API。

---

## 3号位

负责：

- Authentication；
- RBAC；
- Business Scope；
- Rule；
- Parameter；
- Strategy；
- Run/Candidate治理；
- 权限审批；
- SH第二层整单优先级规则治理；
- SH厂间订单型标准跨厂运输/检验/转运LT参数治理。

不负责：

- Pegging；
- FinalTask；
- 普通计划结果。

---

## 4号位

负责：

- 前端页面；
- 用户操作体验。

正式访问：

```
4 → 3
4 → 5
```

禁止：

```
4 → 1
4 → 2 HTTP/API
```

---

## 5号位

负责：

- ODS复杂事实；
- PI Position；
- NextOperationContext；
- MES执行事实与Routing的执行起点解释；
- Procurement/VMI/Received/Transit；
- Manual ETA；
- 非治理类业务接口接入层。

不负责：

- Pegging；
- Allocation；
- Solver。

---

# 6. 最终接口架构

## 治理链

```
4号位
 ↓
3号位
 ↓
2号位运行服务
 ↓
1号位Solver
```

适用于：

- CTP；
- 重排；
- Candidate治理；
- FAILED恢复。

---

## 非治理业务链

```
4号位
 ↓
5号位
 ↓
2号位Query/Application Service
```

适用于：

- Overview；
- Order；
- Gantt；
- Explanation；
- Supply Trace；
- Demand Protection；
- MES查询。

---

# 7. 数据Owner冻结

| 内容 | Owner |
|---|---|
| APS_Auth | 3号位 |
| Rule/Parameter/Strategy | 3号位 |
| ODS复杂事实 | 5号位 |
| PI Position事实 | 5号位 |
| NextOperationContext计算事实 | 5号位 |
| PI Position / NextOperationContext运行消费与必要快照持久化 | 2号位 |
| Manual ETA事实 | 5号位 |
| Pegging/Allocation | 2号位 |
| ScheduleRun | 2号位 |
| PlanVersion | 2号位 |
| FinalTask | 2号位 |

---

# 8. 关键业务验收项

## 数量闭环

必须满足：

- Demand数量闭合；
- Supply数量闭合；
- Allocation不重复；
- TaskShare多对多闭合。

---

## 时间闭环

必须满足：

- Effective ETA正确；
- AvailableTime正确；
- Quantity-Time不可压平。

---

## 版本闭环

必须满足：

- ScheduleRun正确；
- PlanVersion正确；
- Candidate不可污染ACTIVE；
- FAILED恢复创建新Run。

---

## 执行闭环

必须满足：

- PI已完成前序工序不得被重复排产；
- 同一PI不同执行进度可形成互斥排程起点数量切片；
- 5号位负责NextOperationContext事实解释；
- 2号位消费并组装LogicalProductionDemand，不重新解析MES；
- 1号位从StartStage / StartOperation继续Routing，不读取MES重新判断执行位置；
- 非法Task不可下MES；
- Placeholder不可下MES；
- UNLOCATED保守Task不可下MES；
- 权限通过不等于业务资格通过。

---

# 9. 验收边界

验收重点：

1. 业务流程正确；
2. 数量闭环正确；
3. 时间逻辑正确；
4. 接口边界正确；
5. 数据Owner正确；
6. 权限边界正确；
7. 无冻结范围外设计。

---

# 10. 当前已冻结风险控制

禁止：

- 因开发方便修改业务规则；
- 因接口迁移复制业务逻辑；
- 因页面需求建立第二真相；
- 因未来版本提前设计V2能力。

---

# 11. V1最终完成定义

APS V1完成必须满足：

## 业务

- 订单到任务闭环；
- 供需匹配闭环；
- Pegging闭环；
- 有限产能闭环；
- 执行闭环。

## 技术

- 统一接口；
- 单一业务真相；
- 正确Owner；
- 稳定运行；
- 可追溯。

## 管理

- 冻结文档有效；
- 责任清晰；
- 验收标准统一。

---

# 12. 最终项目结论

> APS V1当前进入冻结收口阶段。业务基线保持不变，2026-09-01调整完成了职责、接口、权限和数据库Owner治理收口。2号位继续作为核心排程运行Owner；1号位负责有限产能求解；3号位负责治理和权限；5号位负责复杂事实及非治理业务接口；4号位按照新的访问边界进行页面集成。后续开发、测试和验收均以当前冻结基线体系为唯一依据。


---

# 13. Domain专项验收补充（v1.1新增）

## 13.1 排程域（Domain）定义验收

必须满足：

- 排程域（Domain）是APS用于隔离排程计算、计划版本（PlanVersion）、白天候选计划（Candidate）、失败传播与发布边界的稳定业务范围；
- Domain不是产品族（ProductFamily）、工厂（Factory）、生产部门（ProductionDepartment）、大工艺（Stage）、资源组（Resource Group）的同义词；
- V1只支持：
  - 产品族域（FAMILY）；
  - 工厂+产品族域（FACTORY_FAMILY）；
- 不支持任意表达式、SQL、脚本、DSL或Customer/Stage/Department/Resource/OrderType组合域。

验收失败条件：

- 代码仍默认 `DomainKey = ProductFamily.Code`；
- 页面或数据库把Domain直接定义成ProductFamily别名；
- V1出现任意条件Domain平台。

---

## 13.2 排程域定义（DomainDefinition）治理验收

必须满足：

- DomainDefinition属于APS内部稳定基础配置；
- 不属于规则集（RuleSet）；
- 不属于参数集（ParameterSet）；
- 不属于策略配置（StrategyProfile）；
- 不属于ODS事实；
- 3号位负责治理、启停、校验、审计；
- 2号位负责实际运行归域和执行；
- 若数据库已有等价配置承载则复用，不重复建表；
- 若无等价承载，只允许最小DomainDefinition结构。

不得建设：

- Domain DSL；
- Domain规则表达式；
- Domain运行状态平台；
- Domain资源配额平台。

---

## 13.3 排程运行（ScheduleRun）Domain冻结验收

一次夜间FULL必须满足：

```text
一个ScheduleRun
↓
冻结ExpectedDomainKeysJson
↓
多个真实Domain
↓
每个Domain独立PlanVersion
```

必须满足：

- Run启动时冻结当前有效Domain集合；
- 运行过程中DomainDefinition修改不影响当前Run；
- 修改从下一次Run生效；
- ExpectedDomainKeysJson是当前Run预期Domain集合的冻结依据；
- 不允许运行中动态改写；
- 不允许重新放回ScopeJson作为权威来源。

---

## 13.4 真实归域验收

2号位必须根据当前有效DomainDefinition执行真实归域。

### FAMILY

按产品族（ProductFamily）确定Domain。

### FACTORY_FAMILY

按工厂（Factory）+产品族（ProductFamily）确定Domain。

必须通过以下反例：

1. 同ProductFamily、不同Factory，在FAMILY配置下可以属于同Domain；
2. 同ProductFamily、不同Factory，在FACTORY_FAMILY配置下可以属于不同Domain；
3. 跨Factory不能自动推导为跨Domain；
4. 同ProductFamily不能自动推导为同Domain。

---

## 13.5 Domain依赖验收

排程域依赖（Domain_Dependency）必须满足：

- 上游节点是真实DomainKey；
- 下游节点是真实DomainKey；
- 不再使用ProductFamily.Code作为默认节点；
- 依赖关系可进行拓扑检查；
- 循环依赖必须在正式运行前或运行准备阶段被识别。

3号位负责治理和合法性。

2号位负责实际消费、拓扑执行和失败传播。

---

## 13.6 FULL执行方式验收

当前V1正式方式：

```text
ExpectedDomainKeysJson
↓
Domain_Dependency
↓
拓扑排序
↓
稳定排序
↓
串行逐Domain执行
```

必须满足：

- 当前正式V1采用串行逐Domain；
- 每个Domain形成稳定单Domain执行单元；
- 单Domain内部继续使用既有Pegging和同一套有限产能求解器（Solver）。

不得：

- 恢复旧多Hangfire Domain Job并发架构；
- 建立第二套Solver；
- 为每个Domain创建独立ScheduleRun。

---

## 13.7 每Domain计划版本（PlanVersion）验收

必须满足：

- 每个Domain独立PlanVersion；
- PlanVersion.DomainKey指向真实DomainKey；
- 一个ScheduleRun可关联多个Domain PlanVersion；
- DomainKey不得解释为ProductFamilyCode；
- 一个PlanVersion不得承载全部Domain结果。

---

## 13.8 跨Domain数量—时间（Quantity-Time）验收

跨Domain传递必须保留：

```text
Material
+
Quantity
+
AvailableTime
```

例如：

```text
40件 @ 15日
60件 @ 17日
```

必须保持两个切片。

禁止：

- 压平成100件@某一单一时间；
- 建设持久化虚拟库存余额（VirtualInventoryBalance）作为第二业务真相。

---

## 13.9 FULL共享资源验收

若多个Domain共享同一真实资源（Resource）：

前序Domain成功后：

```text
共享Resource实际占用
↓
进入当前ScheduleRun共享资源上下文
↓
后续Domain作为不可用时间块消费
```

验收必须确认：

- 后续Domain不能重复占用前序成功Domain已经占用的共享资源时段；
- 不需要建设Domain资源配额；
- 不需要建设资源借用平台；
- 不需要建设跨Domain子日历平台。

---

## 13.10 候选计划（Candidate）验收

白天Candidate继续严格单Domain。

必须满足：

- 一个Candidate只有一个DomainKey；
- 有唯一Base ACTIVE PlanVersion；
- 其它Domain当前ACTIVE共享资源占用作为外部排程域资源阻挡块（ExternalDomainResourceBlocks）传入；
- 其它Domain占用不可移动；
- Candidate不得污染其它Domain ACTIVE计划。

禁止：

- MultiDomain Candidate；
- 白天Candidate推动其它Domain重新排程。

---

## 13.11 Domain失败传播验收

示例：

```text
C → B → A
```

若B失败：

必须满足：

- B本次新PlanVersion失败；
- A不发布本次新ACTIVE；
- A保留上一ACTIVE；
- A被标记为因上游失败阻断；
- 与B无依赖的其它Domain继续执行和发布；
- ScheduleRun最终按实际结果进入PARTIAL_SUCCESS或FAILED。

不得：

- 因一个Domain失败回滚所有无关Domain；
- 把FAILED原Run恢复成RUNNING。

---

## 13.12 跨Domain CTP验收

跨Domain CTP继续：

> 通过多个串行单Domain WHATIF / Candidate链完成。

不得：

- 建立MultiDomain Candidate；
- 直接对多个Domain做一个联合Candidate Solver。

---

## 13.13 Domain权限验收

Domain正式作为业务范围（Business Scope）维度。

必须满足：

- 3号位后端执行权限校验；
- 4号位UI隐藏不能替代后端权限；
- 同维度多个值取并集；
- 不同维度取交集；
- Domain查看、维护、Run触发权限可以区分；
- 不建设任意记录级ACL、字段级权限或通用ABAC DSL。

---

## 13.14 前端接口验收

Domain专项不得破坏2026-09-01已经冻结的前端接口边界。

继续：

```text
4 → 3
```

用于：

- DomainDefinition治理；
- ScheduleRun治理；
- Candidate治理；
- Authentication / RBAC / Business Scope。

继续：

```text
4 → 5
```

用于：

- 普通计划结果；
- Order；
- Gantt；
- Explanation；
- Supply Trace；
- FinalTask等业务结果。

禁止：

```text
4 → 2 HTTP/API
4 → 1
```

---

## 13.15 各号位Domain职责验收

### 1号位

负责：

- 消费单Domain求解上下文；
- 消费外部Domain资源阻挡块；
- 按现有限产能逻辑求解。

不得：

- 解释DomainDefinition；
- 分配DomainKey；
- 管理Domain依赖。

### 2号位

负责：

- 真实归域；
- Domain执行；
- Domain内Pegging；
- 跨Domain Quantity-Time；
- FULL共享资源占用传递；
- 失败传播；
- PlanVersion持久化。

### 3号位

负责：

- DomainDefinition治理；
- 有效Domain集合；
- ExpectedDomainKeysJson冻结；
- Domain_Dependency治理；
- Candidate单Domain治理；
- Domain权限。

### 4号位

负责：

- 最小Domain维护页面；
- Run/Candidate Domain展示；
- 按冻结接口调用3/5号位。

### 5号位

保持原复杂事实与业务接口接入职责。

不得因Domain专项迁入：

- Pegging；
- 实际归域；
- Domain调度；
- Solver。

---

## 13.16 Domain专项性能验收

当前业务规模估计约6～7个Domain，其中1～2个较大。

V1不因为理论并行收益提前引入并行。

必须记录：

- DomainKey；
- DemandCount；
- LogicalProductionDemandCount；
- FinalTaskCount；
- ContextBuildMs；
- PeggingMs；
- SolverMs；
- PersistMs；
- TotalMs；
- ScheduleRunTotalMs。

总体目标继续：

> 90天计划窗口、约10万FinalTask、夜间FULL约15分钟收敛目标。

是否需要未来并行，以真实压测为依据。

---

## 13.17 Domain专项明确不纳入V1

以下全部不作为V1验收要求：

- MultiDomain Candidate；
- Domain并行调度平台；
- Domain Worker池；
- Domain资源配额；
- Domain资源借用；
- 跨域影响传播平台；
- VirtualInventoryBalance；
- Domain DSL；
- 任意Scope表达式；
- 第二套Pegging；
- 第二套Solver。

---

# 14. Domain专项最终验收矩阵（v1.1新增）

| 编号 | 验收项 | 责任号位 | 结果要求 |
|---|---|---|---|
| D-01 | Domain定义不是ProductFamily别名 | 0/2/3 | 必须通过 |
| D-02 | FAMILY | 2/3/4 | 必须通过 |
| D-03 | FACTORY_FAMILY | 2/3/4 | 必须通过 |
| D-04 | DomainKey稳定唯一 | 3 | 必须通过 |
| D-05 | Run启动冻结Domain集合 | 2/3 | 必须通过 |
| D-06 | Run中配置变化不影响当前Run | 2/3 | 必须通过 |
| D-07 | Domain_Dependency真实DomainKey | 2/3 | 必须通过 |
| D-08 | FULL串行逐Domain | 2 | 必须通过 |
| D-09 | 每Domain独立PlanVersion | 2 | 必须通过 |
| D-10 | Quantity-Time保留切片 | 2 | 必须通过 |
| D-11 | FULL共享Resource阻挡 | 1/2 | 必须通过 |
| D-12 | Candidate严格单Domain | 2/3/4 | 必须通过 |
| D-13 | Candidate外Domain ACTIVE不可移动 | 1/2 | 必须通过 |
| D-14 | 上游失败阻断依赖下游 | 2/3 | 必须通过 |
| D-15 | 无关Domain继续 | 2/3 | 必须通过 |
| D-16 | PARTIAL_SUCCESS正确 | 2/3/4 | 必须通过 |
| D-17 | Domain业务范围权限 | 3/4 | 必须通过 |
| D-18 | 4→3 / 4→5边界保持 | 3/4/5 | 必须通过 |
| D-19 | 不出现4→2正式HTTP | 2/4 | 必须通过 |
| D-20 | 不建设Domain平台化能力 | 全体 | 必须通过 |
| D-21 | Domain性能指标可测 | 2 | 必须通过 |
| D-22 | 15分钟目标真实压测 | 0/1/2 | 上线前验证 |

---

# 15. v1.1最终完成定义

在v1.0全部验收条件基础上，APS V1还必须满足：

- DomainDefinition稳定且可治理；
- Domain真实归域正确；
- Domain不等于ProductFamily；
- 一个FULL ScheduleRun冻结多个Domain；
- 每Domain独立PlanVersion；
- 当前按依赖串行逐Domain执行；
- 跨Domain Quantity-Time和共享Resource真实闭环；
- Candidate严格单Domain；
- Domain失败传播正确；
- Domain权限正确；
- 无Domain平台化过度设计。

---

# 16. v1.1项目结论

> APS V1的Domain专项不是新增一套排程算法或平台，而是把已经存在的分域运行概念正式收口为稳定业务边界：3号位治理DomainDefinition和Run冻结，2号位完成真实归域、串行逐Domain执行、跨Domain Quantity-Time、共享资源占用和失败传播，1号位继续作为单Domain有限产能时间真相，4号位只提供最小治理与结果展示。原Pegging、SupplyFact、Demand Protection、AvailableTime、90天Solver、Candidate和MES执行边界均保持冻结，不因Domain专项重新设计。

---

# 17. PI Position执行起点上下文专项验收补充（v1.2新增）

## 17.1 专项业务链验收

正式链路必须是：

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
Demand ↔ ProductionInstruction Supply Pegging
+
LogicalProductionDemand
        ↓
1号位
90天有限产能Solver
        ↓
FinalTask
```

任何代码、接口或文档如果形成以下替代链路，均视为验收失败：

```text
2号位直接解析MES决定下一工序
```

或：

```text
1号位直接读取MES决定排程起点
```

或：

```text
Demand ↔ PI ↔ Operation三级Pegging
```

---

## 17.2 ProductionInstruction Supply身份验收

必须满足：

- ProductionInstruction继续作为Supply；
- Demand↔PI承接粒度仍是`PI号 + 数量`；
- PI Position只解释剩余数量当前位置；
- NextOperationContext只解释下一执行起点；
- 不因StartOperation不同创建新的Supply身份；
- 不因Operation不同建立新的Allocation层。

验收失败条件：

- 出现Operation Supply；
- 出现Operation级Pegging；
- 出现NC Position、挤丝 Position、研磨 Position等新的Operation Position体系；
- NextOperationContext进入Supply排序或SupplyBalance。

---

## 17.3 5号位验收

5号位必须能够基于：

- ProductionInstruction剩余量；
- MES WorkOrder / OperationProgress / StageProgress；
- RoutingOperation / RoutingDependency；
- PI Position；
- XC / Transit等强位置事实；
- DataCutoffTime；

形成：

- PI Position；
- NextOperationContext；
- 必要Issue / Evidence。

5号位只负责事实解释。

不得：

- 选择Demand；
- 建Allocation；
- 根据Demand优先级改变NextOperation；
- 根据设备负荷改变NextOperation；
- 决定Resource；
- 决定StartTime / EndTime；
- 生成FinalTask。

---

## 17.4 2号位验收

2号位必须：

1. 继续按冻结规则完成Demand↔ProductionInstruction Supply Pegging；
2. 先确定PI承接数量，再消费该PI内部Position与NextOperationContext；
3. 将执行起点组装进LogicalProductionDemand；
4. 保留PI、Allocation、LogicalProductionDemand之间的稳定数量血缘；
5. 将StartStage / 必要时StartOperation传入DomainSolveRequest；
6. 统一持久化1号位返回结果。

2号位不得：

- 重新读取MES原始事实推导NextOperation；
- 为不同Operation建立新的Supply；
- 把Operation插入Pegging主链；
- Solver前提前生成最终Operation Task。

---

## 17.5 1号位验收

1号位必须：

- 消费2号位已经传入的StartStage / StartOperation；
- 从指定Routing节点开始展开后续Operation；
- 跳过已经完成的前序工序；
- 继续按Resource、Calendar、Setup、物料Quantity-Time、Firm/Frozen等既有硬约束求解；
- 决定实际Resource、Start/End、拆批/合批及FinalTask。

1号位不得：

- 读取MES；
- 重新计算PI Position；
- 重新计算NextOperationContext；
- 把StartOperation改造成新的Pegging层；
- 为了资源利用率把已经完成的工序重新排一遍。

---

## 17.6 同PI不同执行进度验收

示例Routing：

```text
NC → 挤丝 → 研磨
```

PI剩余1000：

- 800件NC已完成；
- 200件NC未完成。

必须形成等价于：

```text
800件 → StartOperation=挤丝
200件 → StartOperation=NC
```

验收必须确认：

- 800件不再占用NC能力；
- 200件仍进入NC；
- 两个片段仍属于同一个PI Supply；
- 数量合计闭合；
- 不产生Operation级Allocation。

---

## 17.7 数量闭合验收

必须同时满足：

```text
Σ PI PositionQty = PI RemainingQty
```

以及：

```text
Σ NextOperationContext.SliceQty
=
本次需要继续生产的PI数量
```

禁止：

```text
PI RemainingQty
+ MES WIP
+ XC
+ Transit
```

重复放大数量。

同一物理数量份额不得同时属于两个NextOperationContext切片。

---

## 17.8 StartOperation合法性验收

必须验证：

- StartOperation属于当前合法Routing；
- StartOperation与StartStage不矛盾；
- 从StartOperation向后Routing可连通；
- 已完成前序工序被正确裁掉。

非法StartOperation不得静默回退到Routing首工序。

必须：

- 返回明确Issue / 输入错误；
- 由2号位按Domain完整性规则处理。

---

## 17.9 UNLOCATED验收

PI Position无法可靠定位时：

- 继续使用既有UNLOCATED冻结规则；
- 只能使用已冻结的最早可信Stage/Operation作为保守起点；
- 1号位不得通过MES自行补猜；
- Issue必须保留；
- 对应保守Task不得下发MES。

---

## 17.10 FULL与Candidate验收

### 夜间FULL

必须：

- 按同一DataCutoffTime批量准备PI Position与NextOperationContext；
- 同一Run中保持事实一致；
- 禁止Pegging循环逐Demand实时回调5号位重新解释MES。

### 白天Candidate

本专项不新增实时MES刷新平台。

继续遵守已有Candidate数据刷新和新增PI保守规则。

不得因本专项引入：

- Candidate独立Position生命周期平台；
- NextOperationContext实时订阅平台；
- MultiDomain Candidate。

---

## 17.11 数据库与持久化验收

本专项不要求新增DDL。

允许：

- 复用现有DTO；
- 复用ScheduleContext；
- 复用ProductionInstructionPositionSnapshot等现有运行快照；
- 由2号位持久化属于ScheduleRun/PlanVersion的运行事实。

不得：

- 仅为NextOperationContext新建独立生命周期平台；
- 在未经过字段说明/DDL专项冻结前新增第二套Position表；
- 让5号位直接写2号位PlanVersion运行结果表。

---

## 17.12 接口验收

5→2接口必须能表达：

- PI Position；
- NextOperationContext；
- 数量切片；
- StartStage；
- 必要时StartOperation；
- IsUnlocated；
- DataCutoffTime；
- Issue / Evidence。

2→1接口必须能表达：

- LogicalProductionDemand；
- NetOutputQty；
- PlannedProcessQty；
- StartStage；
- 必要时StartOperation；
- Routing引用；
- Quantity-Time；
- Firm/Frozen/Execution约束。

接口可采用现有DTO扩展，不强制新增平台型接口。

---

# 18. PI Position执行起点上下文专项最终验收矩阵（v1.2新增）

| 编号 | 验收项 | 责任号位 | 结果要求 |
|---|---|---|---|
| PNX-01 | ProductionInstruction仍是Supply | 2/5 | 必须通过 |
| PNX-02 | PI Position不是Supply | 2/5 | 必须通过 |
| PNX-03 | NextOperationContext不是Supply/Allocation | 1/2/5 | 必须通过 |
| PNX-04 | 5号位计算PI Position | 5 | 必须通过 |
| PNX-05 | 5号位计算NextOperationContext | 5 | 必须通过 |
| PNX-06 | 5号位不参与Pegging | 5 | 必须通过 |
| PNX-07 | 2号位不重新解析MES | 2 | 必须通过 |
| PNX-08 | 2号位先Pegging到PI再消费执行上下文 | 2 | 必须通过 |
| PNX-09 | LogicalProductionDemand携带StartStage | 2 | 必须通过 |
| PNX-10 | 必要时携带StartOperation | 2 | 必须通过 |
| PNX-11 | 同PI多执行起点切片数量闭合 | 2/5 | 必须通过 |
| PNX-12 | 1号位从StartOperation继续Routing | 1 | 必须通过 |
| PNX-13 | 已完成Operation不重复排产 | 1 | 必须通过 |
| PNX-14 | 1号位不读取MES | 1 | 必须通过 |
| PNX-15 | StartOperation非法时不静默回退 | 1/2 | 必须通过 |
| PNX-16 | UNLOCATED保守起点保持不可MES下发 | 1/2/5 | 必须通过 |
| PNX-17 | 不出现Operation级Pegging | 全体 | 必须通过 |
| PNX-18 | 不出现Operation Supply | 全体 | 必须通过 |
| PNX-19 | 不建立Operation Position平台 | 全体 | 必须通过 |
| PNX-20 | 不因本专项新增第二套DDL/持久化平台 | 0/2/5 | 必须通过 |
| PNX-21 | FULL批量事实口径同DataCutoffTime一致 | 2/5 | 必须通过 |
| PNX-22 | Candidate不新增实时MES刷新平台 | 2/5 | 必须通过 |
| PNX-23 | 2→1执行起点接口完整 | 1/2 | 必须通过 |
| PNX-24 | 5→2事实接口完整 | 2/5 | 必须通过 |

---

# 19. v1.2最终完成定义

在v1.1全部Domain验收条件基础上，APS V1还必须满足：

- PI Position与NextOperationContext职责边界明确；
- 5号位负责执行事实解释；
- 2号位负责Demand↔PI数量真相与执行上下文组装；
- 1号位负责从已确定执行起点进行时间资源求解；
- ProductionInstruction仍是唯一PI Supply身份；
- 不进入Operation级Pegging；
- 同PI不同执行进度可以正确进入不同有限产能起点；
- 已完成工序不重复排产；
- 数量、时间、资源、版本、接口和MES资格继续闭合；
- 不因本专项引入新的平台化设计或未经冻结的DDL。

---

# 20. v1.2项目结论

> APS V1本次PI Position执行起点上下文专项并未改变Pegging、Supply、Domain或90天有限产能Solver的业务模型，而是补齐“生产指示剩余数量已经做到哪里”到“有限产能下一步从哪里继续”的关键接力链。5号位负责从MES执行事实、Routing与PI Position形成NextOperationContext；2号位继续以ProductionInstruction作为Supply完成Demand↔PI数量承接，并把执行起点装入LogicalProductionDemand；1号位从StartStage/StartOperation继续Routing并形成真实FinalTask。最终验收必须确保已完成工序不重复排产，同时禁止Operation级Pegging、Operation Supply、Operation Position平台和1/2号位重复解析MES。

---

# 21. 跨版本排程连续性专项验收（v1.3新增）

## 21.1 当前Q必须先形成

验收主链：

```text
当前Demand/Supply/Pegging
↓
LogicalProductionDemand Q
↓
5号位标准化IN_PROGRESS执行事实
↓
2号位逐MES工单Continuation Slice
↓
Continuation + Free
↓
1号位Solver
```

验收失败：

```text
旧TaskNo / MESWorkOrderNo / 旧计划剩余量
→ 反向决定当前Q
```

普通未锁Allocation仍可按本次优先级重新竞争。

## 21.2 E/Q数量闭合

设：

```text
Q = 当前净合格需求
E = 可承接当前Q的IN_PROGRESS连续份额合计
```

验收：

```text
E <= Q:
ContinuationQty=E
FreeQty=Q-E
```

异常：

```text
E > Q:
FreeQty=0
ExecutionOverCommit=E-Q
```

```text
Q=0且E>0:
不创造正常Demand/Task
只登记执行超量/需求不一致
```

不得把Q抬高成E。

## 21.3 多MES工单必须保持独立连续身份

例如：

```text
WO101剩20 → C101 Qty20
WO102剩30 → C102 Qty30
```

可以合计E=50做数量闭合，但进入1号位必须是两个独立Continuation对象。

不得合并成单一Qty50连续对象。

## 21.4 连续份额必须进入Solver

错误：

```text
Q=100
E=30
→ 只把70送入Solver
```

正确：

```text
Continuation 30 + Free 70
→ 同一个Solver
```

连续30仍然占未来资源能力。

## 21.5 MES工单状态

正式字段：

```text
WorkOrderStatus
```

状态值：

```text
RELEASED
IN_PROGRESS
CLOSED
DELETED
```

验收：

- RELEASED：不建立执行连续性；
- IN_PROGRESS：有当前Q时形成连续份额；
- CLOSED：结束执行身份，完成事实进入PI Position；
- DELETED：结束旧执行身份，有当前需求则重新排。

不得改成新的同义技术值。

## 21.6 工单级/工序级字段边界

工单级禁止新增：

```text
RemainingQty
RemainingNetQty
LastReportResourceCode
ExecutionStatus
```

工序级必须具备：

```text
MES_APS_OperationProgress_View.LastReportResourceCode
OperationProgressSnapshot.LastReportResourceCode
```

其用途：

> 作为具体Operation的资源连续性偏好（PreferredResource），不是工单级唯一设备，不自动形成Hard Lock。

工序级继续`GoodQty`，Stage级继续`GoodCompletedQty`，不得改名或互换。

## 21.7 TaskNo三分支

APS旧Task连续：

```text
FinalTask + ContinuationKey
→ 继承OldTaskNo
→ 继续原MESWorkOrderNo
```

无TaskNo外部MES连续：

```text
FinalTask + ContinuationKey
→ 新APS TaskNo
→ APS侧绑定原MESWorkOrderNo
→ 不创建第二MES工单
```

自由份额：

```text
FinalTask
→ 新TaskNo
→ 正常MES下发
→ 新MESWorkOrderNo
```

APS侧绑定不得被实现成“必须回写MES原工单TaskNo字段”。

## 21.8 同PI同Operation执行顺序

对同一PI同一Operation：

```text
既存执行连续份额
→ 新增自由份额
```

不得反转。

APS自动求解不得形成不同设备的时间重叠。

现场已存在人工并行事实可作为事实接受，但APS不得主动新增自动并行。

---

# 22. 跨厂SH专项验收（v1.3新增）

## 22.1 SH身份

必须：

```text
OrderType = SALES_ORDER
CustomerSegment = INTER_FACTORY
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

不得写成`OrderType=SH`。

## 22.2 第一层Demand池

第一层普通销售订单Demand池必须排除：

```text
CustomerSegment = INTER_FACTORY
```

的SH订单本身。

否则同一SH会同时成为普通顶层Demand和SH Supply，属于重复建模。

## 22.3 SH单一Supply

一张SH只能形成一个Supply身份。

禁止恢复：

```text
Transit Supply
Received Supply
ZP Supply
BP Supply
Unproduced Supply
```

普通Demand↔SH允许多对多，由2号位本次Pegging产生Allocation。

5号位不得提供普通Demand↔SH固定Document关系。

## 22.4 SH第二层整单判断

对SH：

```text
CLOSED / CANCELLED
→ 退出APS活跃计算

OPEN + ERP_InterplantInTransit_View存在SH号
→ 已进入跨厂履行
→ 不生成源厂履约Demand

OPEN + 视图不存在SH号
→ 生成一条完整源厂履约Demand
→ RequiredQty = Order.Quantity
```

跨厂在途视图查询成功返回0行属于合法成功。

不得使用在途Quantity倒算未生产量。

## 22.5 第二层一SH一Demand

第二层一张SH只形成一条完整源厂履约Demand。

不得：

- 按第一层多个Allocation份额拆多条源厂Demand；
- 按Transit/Received/ZP/BP拆多条源厂Demand。

`RequiredQty=Order.Quantity`不等于“新增生产Order.Quantity”。

仍需继续：

```text
PI
→ PI Position
→ 只有未被PI承接的缺口才新增生产
```

## 22.6 SourceFactoryId与重新归域

数据库链必须存在：

```text
ERP_Order_Staging.SourceFactoryCode
→ Order_Canonical.SourceFactoryCode
→ Order.SourceFactoryId
```

语义：

```text
FactoryId = 目标工厂
SourceFactoryId = 源工厂
```

第二层源厂Demand按SourceFactoryId重新归Domain。

禁止：

- 用目标FactoryId冒充源工厂；
- INTER_FACTORY缺SourceFactory时静默兜底；
- 永久改写原Order.FactoryId；
- 永久覆盖原Order.DomainKey；
- 沿用第一层目标DomainKey。

## 22.7 SH整单优先级

整张SH源厂Demand优先关系取：

> SH自身业务优先关系 与 当前全部上层Allocation中最强业务优先关系 的更强者。

3号位负责规则治理，2号位运行时执行。

## 22.8 SH整单时间

同一SH只允许整单一次入库。

验收：

- 目标侧只有一个整单AvailableTime；
- SourceReadyTime由1号位真实有限产能产生；
- 标准SH跨厂LT由3号位参数治理；
- ERP真实发运/到达事实由5号位提供且优先；
- 同Domain一次Solver表达源厂→LT→目标侧后续；
- 跨Domain由2号位传播上游真实完成结果。

不得因为源厂内部分批完成而形成多个SH目标侧AvailableTime。

## 22.9 5号位SH职责收缩

5号位只负责：

- 跨厂物流事实；
- SH号在在途视图中的存在性；
- ERP真实发运/到达事实；
- Strict Binding Evidence（真实存在时）；
- 数据质量Issue。

不得：

- 建SH第二主档；
- 提供SH权威总量；
- 提供普通Demand↔SH固定关系；
- 计算SourceReadyTime；
- 构造Transit/Received/ZP/BP独立Supply。

---

# 23. v1.3联合专项最终验收矩阵

| 编号 | 场景 | 通过标准 |
|---|---|---|
| CVC-01 | Q=100/E=30 | Continuation30 + Free70，共同进入Solver |
| CVC-02 | Q=50/E=70 | Free=0，OverCommit=20，不抬高Q |
| CVC-03 | Q=0/E=20 | 不创建正常Demand/Task |
| CVC-04 | WO101=20、WO102=30 | 两个独立ContinuationKey |
| CVC-05 | IN_PROGRESS只从Q扣除但不入Solver | 验收失败 |
| CVC-06 | 旧TaskNo反向决定当前Q | 验收失败 |
| CVC-07 | 工单级新增RemainingNetQty | 验收失败 |
| CVC-08 | 工单级新增LastReportResourceCode | 验收失败 |
| CVC-09 | 工序级LastReportResourceCode存在 | 正确透传到PreferredResource |
| CVC-10 | PreferredResource不可用 | 可选其它合法资源，不当Hard Lock |
| CVC-11 | APS旧Task连续 | 继承TaskNo与原MES工单 |
| CVC-12 | 无TaskNo外部MES连续 | 新TaskNo绑定原MES工单，不创建第二工单 |
| CVC-13 | 自由份额 | 新TaskNo + 新MES工单 |
| CVC-14 | 同PI同Operation连续+自由 | 连续先于自由 |
| CVC-15 | 同PI同Operation自动双设备并行 | 验收失败 |
| SH-01 | SH识别 | SALES_ORDER + INTER_FACTORY + OrderNo |
| SH-02 | 第一层Demand池 | 排除INTER_FACTORY SH订单自身 |
| SH-03 | 普通Demand↔SH | 允许多对多Allocation |
| SH-04 | OPEN+在途视图有SH号 | 不生成源厂Demand |
| SH-05 | OPEN+在途视图无SH号 | RequiredQty=Order.Quantity |
| SH-06 | 在途视图成功0行 | 合法成功 |
| SH-07 | CLOSED/CANCELLED | 退出活跃计算 |
| SH-08 | TransitQty倒算未生产 | 验收失败 |
| SH-09 | 第二层源厂Demand | 一SH一Demand |
| SH-10 | SourceFactoryId | 真源厂，不得目标工厂静默兜底 |
| SH-11 | 第二层Domain | 按SourceFactoryId重新归域 |
| SH-12 | PI承接 | PI→PI Position→仅缺口新增生产 |
| SH-13 | SH整单优先级 | 取SH自身与上层最强者中的更强者 |
| SH-14 | SH目标侧时间 | 一个整单AvailableTime |
| SH-15 | SourceReadyTime | 1号位有限产能产生 |
| SH-16 | 标准跨厂LT | 3号位ParameterSet治理 |
| SH-17 | 5号位SH第二主档 | 验收失败 |
| SH-18 | Transit/Received/ZP/BP多Supply | 验收失败 |
| SH-19 | SH=250、TransitQty=80、ReceivedQty=70且在途视图存在SH号 | 不生成源厂Demand；禁止推导“100未生产” |
| SH-20 | SH=250且在途视图无SH号，已有PI/PI Position承接150 | 源厂Demand=250，最终新增生产缺口=100 |
| SH-21 | SupplyFact_Pipeline出现INTERPLANT_IN_TRANSIT | 验收失败 |
| SH-22 | PeggingSupplyAllocation出现INTERPLANT_IN_TRANSIT独立Supply | 验收失败 |
| SH-23 | ERP_Received_ByDocument_View被设为SH Supply/部署阻断 | 验收失败 |

---

# 24. v1.3最终完成定义

在v1.2全部验收要求继续满足的前提下，本轮还必须同时满足：

1. 跨版本连续性不改变当前Q的业务真相；
2. 真实IN_PROGRESS执行份额继续占未来产能；
3. 多MES工单连续身份不被合并；
4. 工单级不新增无意义剩余量/设备字段；
5. 工序级LastReportResourceCode链完整；
6. TaskNo三分支闭环；
7. SH一张单据一个Supply；
8. SH第二层一张单据一条完整源厂Demand；
9. SourceFactory数据链、重新归域和强校验闭环；
10. SH整单优先级与整单时间闭环；
11. 1/2/3/5号位职责没有互相侵入；
12. 不借本轮建设新的Continuation平台、SH平台、物流Solver或Operation级Pegging。

# 25. v1.3项目结论

当上述业务、字段、接口、数据库、实施包和代码审核基线均通过一致性审查后，APS V1可以进入下一轮代码审核。

本验收包不授权任何号位因为实现困难重新打开已经冻结的PI、Domain、Pegging、Solver、Candidate或权限架构。

# 26. 白天业务场景 / Candidate人工调整专项验收（v1.4新增）

## 26.1 共同门禁

所有白天Candidate必须：

```text
单Domain
BasePlanVersionId = 创建时当前ACTIVE
ExpectedDomainKeysJson = 恰好一个Domain
```

禁止：

```text
BasePlanVersionId = CANDIDATE
MultiDomain Candidate
第二套Solver
第二套Scenario/Candidate平台
```

一次`LOCAL_RESCHEDULE`只能一个`BusinessTriggerType`，但允许多个同类型对象。

多个对象跨Domain时必须拆Run。

## 26.2 业务场景验收矩阵

| 编号 | 场景 | 通过标准 |
|---|---|---|
| DAY-01 | 多新订单正常CTP | INSERT_ORDER_WHATIF + CTP + NORMAL；一个Domain；WHATIF不可激活 |
| DAY-02 | 多新订单急插影响分析 | INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS + EXPEDITE；只提升选中订单及派生链 |
| DAY-03 | 采用CTP结果 | 不激活原WHATIF；重新基于最新ACTIVE创建LOCAL + INSERT_RESCHEDULE + NORMAL |
| DAY-04 | 采用急插结果 | 不激活原WHATIF；重新基于最新ACTIVE创建LOCAL + INSERT_RESCHEDULE + EXPEDITE |
| DAY-05 | 多已有订单提前NORMAL | 每单独立ManualTargetDueDate；不修改正式DueDate |
| DAY-06 | 多已有订单提前EXPEDITE | EXPEDITE显式表达；不得通过改DueDate实现 |
| DAY-07 | 多Task甘特调整 | LOCAL + MANUAL_ADJUSTMENT + NORMAL；TargetTime为软目标 |
| DAY-08 | 多设备故障 | 设备先正式不可用；ChangedResourceIds只是Seed |
| DAY-09 | 多Calendar变化资源 | Calendar先正式修改；不建设假设Calendar |
| DAY-10 | 整Domain主动重排 | MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT |
| DAY-11 | LOCAL影响扩大到全Domain | 仍保持LOCAL，不自动改MANUAL |
| DAY-12 | 一Run混合订单提前+故障 | 必须拒绝 |
| DAY-13 | “其它明确对象局部调整”入口 | 不得存在 |
| DAY-14 | Candidate B/C同时基于ACTIVE A | 允许并存比较 |
| DAY-15 | B先激活后C仍基于A | C不得直接激活，必须重跑 |
| DAY-16 | Candidate Base指向Candidate | 必须拒绝 |
| DAY-17 | 多对象跨两个Domain | 必须拆成多个单Domain Run |
| DAY-18 | GANTT_ADJUSTMENT + EXPEDITE | 必须拒绝 |
| DAY-19 | EQUIPMENT_FAILURE + EXPEDITE | 必须拒绝 |
| DAY-20 | RESOURCE_CALENDAR_CHANGE + EXPEDITE | 必须拒绝 |
| DAY-21 | OrderTargets与OrderCanonicalIds不一致 | 必须拒绝 |
| DAY-22 | 未选择的新订单 | 不得自动进入本次Candidate业务对象集合 |
| DAY-23 | 本次Seed外但已生效的Calendar/故障事实 | 仍必须进入Solver当前真实上下文 |
| DAY-24 | Scope外Task | 不得移动，但必须作为资源/时间阻挡 |
| DAY-25 | 可行方案必须移动Scope外Task | 返回Business Scope不足，不得越权 |
| DAY-26 | 甘特拖08:00但不可行 | 可返回其它可行时间并Explanation，不得硬锁08:00 |
| DAY-27 | ScopeJson v2 | 新Run有SchemaVersion=2和合法BusinessTriggerType/PriorityMode |
| DAY-28 | 新建CandidateInput/ScenarioInput永久表 | 验收失败 |

## 26.3 ScopeJson / 数据库验收

必须确认：

```text
ScheduleRun.ScopeJson
=
NVARCHAR(MAX)版本化运行输入
```

新建白天Candidate：

```text
SchemaVersion = 2
```

允许结构化：

- OrderTargets[]；
- TaskTargets[]；
- ChangedResourceIds[]。

不得新增：

- CandidateInput永久平台；
- PriorityMode物理列；
- BusinessTriggerType物理列；
- OrderTarget长期表；
- TaskTarget长期表；
- Candidate专属Calendar表。

`BasePlanVersionId`、`ExpectedDomainKeysJson`继续独立权威。

## 26.4 1～5号位职责验收

### 1号位
- 不解析ScopeJson；
- 不判断页面场景权限；
- 多Seed进入同一Solver；
- Seed不是事实过滤器；
- 软目标允许返回真实可行替代时间。

### 2号位
- 消费v2输入；
- 形成Seed与可移动集合；
- NORMAL/EXPEDITE不通过修改正式DueDate实现；
- Scope外Task作为不可移动Block；
- 不自行改LOCAL/MANUAL。

### 3号位
- 校验Schema/场景矩阵；
- 冻结当前ACTIVE Base；
- 激活前复核Base未过期；
- Business Scope后端硬校验；
- 不做Pegging/Solver。

### 4号位
- 提供明确中文业务入口；
- 甘特固定LOCAL+NORMAL；
- 支持多对象结构化输入；
- 不直接写Task/DueDate；
- 不提供万能“其它局部调整”。

### 5号位
- 按PlanVersionId查询Candidate Gantt/结果；
- 不解析Candidate输入；
- 不计算或激活Candidate。

# 27. v1.4专项完成定义

只有DAY-01～DAY-28全部通过，且原v1.3及以前验收矩阵继续通过，才允许本轮白天Candidate业务冻结进入代码审核。


