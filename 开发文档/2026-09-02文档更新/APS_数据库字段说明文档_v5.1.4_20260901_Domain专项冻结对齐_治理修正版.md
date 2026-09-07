# APS 数据库字段说明文档 v5.1.4（20260901 Domain专项冻结对齐版）

**当前版本**：v5.1.4  
**当前版本日期**：2026-09-01（北京时间）  
**基于版本**：v5.1.3冻结对齐版  
**文档性质**：APS核心数据库字段当前冻结说明  
**适用范围**：APS V1

## 0. v5.1.4变更摘要

本版本在v5.1.3基础上只做两组已经冻结的增量对齐。

### A. 继续继承v5.1.3既有字段口径

- `ProcurementManualEtaOverride`正式纳入APS数据库模型；
- `ReleaseDate`补充ERP来源语义；
- `AvailableTime`由2号位统一计算；
- `CommitmentStatus`保持正式供应事实承诺状态；
- Planning-only Purchase Placeholder继续只存在于运行内存，不进入正式SupplyFact承诺。

### B. 2026-09-01 Domain专项新增

- 增加排程域定义（DomainDefinition）最小字段说明；
- 统一 `DomainKey` 为APS稳定真实排程域标识，不再视为 `ProductFamily.Code` 同义词；
- 明确 `ScheduleRun.ExpectedDomainKeysJson`、`PlanVersion.DomainKey`、`Order.DomainKey` 的统一Domain语义；
- `Domain_Dependency`上下游节点改为真实DomainKey；
- 不新增DomainRuntime、DomainContext、MultiDomain Candidate、VirtualInventoryBalance、Domain资源配额等平台化结构。

> 除以上两组增量外，其余字段定义继续继承v5.1.3冻结语义，不重新设计。


------------------------------------------------------------------------

# 一、ProcurementManualEtaOverride

## 表定位

ProcurementManualEtaOverride用于保存APS人工维护的采购ETA覆盖事实。

属于APS人工事实，不属于ODS事实，不修改ERP数据。

## 字段

  字段                 说明
  -------------------- --------------
  PONo                 采购订单号
  LineNo               采购订单行号
  MaterialId           物料ID
  MaterialCode         物料编码
  ReceivingWarehouse   收货仓库
  ManualEta            人工维护ETA
  IsActive             是否有效
  UpdatedBy            更新人
  UpdatedAt            更新时间
  CreatedBy            创建人
  CreatedAt            创建时间
  Remark               备注

业务唯一键：

PONo + LineNo + MaterialId + ReceivingWarehouse

------------------------------------------------------------------------

# 二、SupplyFact_Pipeline字段

## ETA

ERP/ODS供应事实提供的原始预计时间。

## ReleaseDate

ERP供应事实中的单据发行/创建时间，用于APS供应时间计算辅助。

## AvailableTime

APS统一可用时间。

由2号位根据： - ETA； - Manual ETA Override； - ReleaseDate； -
DefaultPurchaseLT及相关偏移参数；

计算形成。

## CommitmentStatus

V1表示正式供应事实承诺状态。

Planning-only Purchase Placeholder不进入SupplyFact，不使用该字段。

------------------------------------------------------------------------

# 三、职责边界

5号位： - ProcurementManualEtaOverride数据库支持； - ODS供应事实； -
ERP字段映射。

4号位： - Manual ETA维护页面。

2号位： - 消费人工ETA； - Effective ETA计算； - AvailableTime计算； -
Pegging消费。

------------------------------------------------------------------------

# 四、保持冻结

不改变： - SupplyFact业务模型； - Pegging模型； - 有限产能模型； -
Planning-only Purchase Placeholder规则。

------------------------------------------------------------------------

# 五、Domain专项字段说明（v5.1.4新增）

## 5.1 DomainDefinition

### 表定位

DomainDefinition用于持久化维护APS稳定排程域定义。

属于APS内部基础配置/主数据级配置，由3号位治理；不属于ODS事实，不属于RuleSet、ParameterSet或StrategyProfile。

2号位读取当前有效DomainDefinition，对Order/Demand/Supply/Material上下文执行真实归域。

### 字段

| 字段 | 中文说明 | 业务语义 |
|---|---|---|
| Id | 主键 | 技术主键 |
| DomainKey | 排程域标识 | APS稳定、唯一、可追溯的Domain业务Key；不是ProductFamily.Code、PlanVersionId或ScheduleRunId |
| DomainName | 排程域名称 | 面向管理员/业务人员的显示名称 |
| ScopeType | 范围类型 | V1仅允许FAMILY / FACTORY_FAMILY |
| ProductFamilyId | 产品族ID | FAMILY与FACTORY_FAMILY均必填 |
| FactoryId | 工厂ID | FAMILY必须为空；FACTORY_FAMILY必须有值 |
| IsActive | 是否启用 | 当前是否允许被新ScheduleRun引用 |
| SortOrder | 稳定排序号 | 仅作为稳定排序辅助，不代表Domain依赖优先级 |
| CreatedBy / CreatedAt | 创建审计 | 创建人、创建时间 |
| UpdatedBy / UpdatedAt | 修改审计 | 修改人、修改时间 |

### V1范围规则

- FAMILY：一个ProductFamily整体形成一个Domain；FactoryId为空。
- FACTORY_FAMILY：一个Factory + 一个ProductFamily形成一个Domain；FactoryId必填。
- V1不支持Customer、Stage、ProductionDepartment、Resource、OrderType、SQL、脚本或DSL动态拼装Domain。

------------------------------------------------------------------------

## 5.2 DomainKey统一语义

以下对象中的DomainKey统一表示：

> APS稳定排程域标识，对应真实DomainDefinition定义。

包括但不限于：

- Order.DomainKey；
- PlanVersion.DomainKey；
- ScheduleRun.ExpectedDomainKeysJson中的每个DomainKey；
- Candidate所属DomainKey；
- Domain_Dependency上下游Domain字段。

不得再解释为ProductFamily.Code同义词。

------------------------------------------------------------------------

## 5.3 ScheduleRun.ExpectedDomainKeysJson

表示：

> ScheduleRun启动时冻结的本次预期DomainKey集合。

规则：

- 由Run启动时当前有效Domain集合形成；
- 当前Run启动后不可因DomainDefinition修改而漂移；
- 当前Run只执行该冻结集合；
- 不再使用ScopeJson承载该权威集合；
- 不根据后续已生成PlanVersion反向推导。

------------------------------------------------------------------------

## 5.4 PlanVersion.DomainKey

表示：

> 当前PlanVersion所属真实排程域。

V1正式运行版本业务上必须有值。

数据库继续允许NULL仅用于历史兼容，不代表新V1主链允许无Domain计划版本。

一个ScheduleRun可以对应多个Domain，每个Domain形成独立PlanVersion。

------------------------------------------------------------------------

## 5.5 Order.DomainKey

表示：

> 2号位在当前运行中依据有效DomainDefinition对订单/需求完成真实归域后形成的DomainKey。

FAMILY按ProductFamily匹配；FACTORY_FAMILY按Factory + ProductFamily匹配。

不得用ProductFamily.Code直接硬编码。

------------------------------------------------------------------------

## 5.6 Domain_Dependency

表定位：

> 保存真实排程域之间的业务依赖关系。

字段：

| 字段 | 中文说明 | 业务语义 |
|---|---|---|
| UpstreamDomainCode | 上游排程域 | 字段名历史兼容，实际值必须为真实DomainKey |
| DownstreamDomainCode | 下游排程域 | 字段名历史兼容，实际值必须为真实DomainKey |
| ChildMaterialCode | 关联半成品物料 | 跨Domain依赖关联物料 |
| DefaultLeadTimeDays | 兼容LT缓存 | 不是固定2天权威；真实转运LT按已冻结工厂/Stage事实 |
| ScannedAt | 生成/刷新时间 | 依赖关系刷新时间 |

治理/执行边界：

- 3号位负责Domain依赖合法性、节点有效性治理；
- 2号位运行时消费依赖关系并执行拓扑顺序、失败传播和跨Domain运行闭环。

不得继续使用ProductFamily.Code作为默认Domain节点。

------------------------------------------------------------------------

## 5.7 本次明确不新增的字段/表

V1不新增：

- DomainRuntime；
- DomainContext持久化表；
- MultiDomain Candidate表；
- VirtualInventoryBalance；
- Domain资源配额/借用表；
- Domain DSL或表达式字段。

------------------------------------------------------------------------

# 六、Domain专项职责边界

3号位：DomainDefinition治理、启停、校验、审计、Run启动Domain集合冻结。

2号位：真实归域、Domain内Pegging、逐Domain执行、跨Domain Quantity-Time、共享Resource占用传递、失败传播。

1号位：只消费真实DomainKey和单Domain求解上下文，不解释Domain范围。

4号位：最小Domain维护页面，调用3号位。

5号位：原复杂事实/ODS/业务接口接入职责保持，不接管Domain排程运行。
