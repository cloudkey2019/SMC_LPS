# APS 数据库表结构设计 v5.1.2 冻结符合性审查报告

**审查日期**：2026-08-12  
**审查性质**：冻结符合性审查（Conformance Review），不是业务重新设计  
**目标DDL**：《APS_数据库表结构设计_v5.1.2_冻结对齐版.sql》  
**兼容升级脚本**：《APS_数据库_v5.1.1到v5.1.2_冻结兼容升级补丁.sql》

---

## 1. 审查结论

本轮仅检查DDL是否忠实承接已冻结的APS V1业务，以及是否会因“数据库结构完整性”重新引入已明确不做的V1平台。

**结论：通过冻结符合性审查。未发现需要重新打开任何已冻结业务决策的问题。**

同时保留一个部署前技术依赖：采购/VMI/已到厂未入库/厂间在途与ERP Received的**真实ODS来源绑定**必须由5号位/来源系统负责人提供。本DDL不虚构源表名，不再用 `WHERE 1=0` 伪装“接口存在但V1永久无数据”。这是实施依赖，不是业务未决项。

---

## 2. 本次DDL收口的P0/P1项目

| 冻结项 | v5.1.2处理 | 结论 |
|---|---|---|
| Demand排序不得再由全局PriorityScore决定 | `Order.PriorityScore`保留兼容列，但注释明确退出V1排序权威 | 通过 |
| 90天只有一套正式有限产能Solver | `ComputeMode/ROUGH_CUT`只保留历史兼容，不允许选择第二套远期Solver | 通过 |
| FinalTask由1号位生成 | DDL不建设2号位“提前FinalTask”所需第二套Task结构 | 通过 |
| Task可承接多个Demand | `Task.OrderId`降为可空兼容展示；新增最小`AllocationTaskShare` | 通过 |
| 净合格产出与计划加工量分离 | `Task.Quantity`=NetOutputQty；新增`Task.PlannedProcessQty` | 通过 |
| PSA仅是内部非Task供给Allocation镜像 | 补最小`PeggingSupplyAllocation`，不引入独立生命周期/第二真相源 | 通过 |
| 不强制新增PeggingAllocationLedger物理表 | 未创建该表 | 通过 |
| PI Position只建一张最小快照 | 新增`ProductionInstructionPositionSnapshot`，未建Header+Slice双生命周期 | 通过 |
| Strict Binding / Demand Protection共用最小锁实体 | 新增`DemandSupplyHardLock.LockType`区分两类；未另建DemandProtection平台 | 通过 |
| 采购/VMI/Received/厂间在途正式进入V1 | `SupplyFact_Pipeline`升级为真实Timed Supply承载；`sp_SyncPipelineSupply`不再空跑 | 通过 |
| Planning-only Purchase Placeholder只在内存 | DDL明确禁止建表；不生成采购Task/采购单 | 通过 |
| InventoryBalance不是运行期永久扣减真相 | `AllocatedQty`只保留兼容快照语义；运行期Allocation由2号位内存Balance维护 | 通过 |
| 跨Domain固定2天LT退出 | `Domain_Dependency.DefaultLeadTimeDays`默认改为0/兼容缓存；真实LT来自工厂/Stage事实 | 通过 |
| 不建设有限物流Task | 无`ShippingTask`表；新V1 `TaskType`仅允许业务上生成PRODUCTION，TRANSFER/PROCUREMENT仅历史兼容 | 通过 |
| 不建设FrozenZoneSnapshot/VirtualInventoryBalance平台 | DDL不新增；历史环境若存在只标deprecated并退出生成/消费链 | 通过 |
| Candidate不强制完整OA审批/Scenario | 保留既有兼容骨架，但DDL注释明确不得反向形成V1硬依赖 | 通过 |

---

## 3. Timed Supply真实数据契约的处理

旧版本存在“V1空链路”技术骨架：采购、VMI、已到厂未入库等分支以placeholder/空视图存在，`sp_SyncPipelineSupply`可以空跑成功。

v5.1.2改为：

1. `SupplyFact_Pipeline`是V1正式Timed Supply事实表；
2. `sp_SyncPipelineSupply`若统一输入View根本不存在，直接失败，禁止伪装为0行成功；
3. **真实View存在但本次确实0行**属于合法业务结果；
4. DDL不猜测采购/VMI系统的物理源表名；
5. 新增`sp_ValidateV1FrozenDataContracts`作为部署前显式契约检查；
6. `ERP_Received_ByDocument_View`同样要求绑定真实ERP来源，不再创建空骨架。

这一区分非常重要：

> “业务上没有供给数据”可以是0行；  
> “技术上根本没接通数据源”必须是部署失败。

---

## 4. 对现有数据库/2号位代码的保护

本轮没有要求对已有数据库做破坏性重建，并额外生成了兼容升级补丁。

升级补丁采用：

- `IF OBJECT_ID / COL_LENGTH / sys.indexes`检测；
- 新字段优先nullable兼容历史数据；
- 已有`PeggingSupplyAllocation`时只补冻结主链必需字段，不清空历史数据；
- `Task.OrderId`只放宽为NULL，不删除列；
- `PriorityScore`、`ComputeMode`等兼容字段不强删；
- 历史`FrozenZoneSnapshot/VirtualInventoryBalance`若现实环境存在，不DROP，只停止V1主链生成/消费；
- 不修改ScheduleRun/PlanVersion、ExpectedDomainKeysJson、PARTIAL_SUCCESS、Realtime BOM、OrderBomRequestLink、Routing三件套、MaterialStageDeptContext、规则治理6表等保护区基本结构。

因此仍贯彻“**保留外壳，替换/补齐核心**”，而不是为了DDL整洁推倒现有实现。

---

## 5. DDL阶段发现并已修正的字段文档残留

在DDL反向核对第五份《数据库字段说明文档 v5.1.2》时，发现一处旧文字仍写：

> `InventoryBalance.AllocatedQty` = “V1由排程引擎写入”。

该句与冻结业务冲突，已在同一v5.1.2字段说明中修正为：

> `AllocatedQty`只保留兼容快照语义；V1运行期不得永久UPDATE，本次运行实际Allocation/剩余量由2号位内存`SupplyBalance`维护。

这属于技术文档残留清理，**没有改变业务冻结**。

---

## 6. 过度设计回流检查

本DDL没有新增以下对象：

- `PeggingAllocationLedger`独立平台；
- PI Header + PositionSlice双表平台；
- 第二套Demand Protection表；
- `FrozenZoneSnapshot`新平台；
- `VirtualInventoryBalance`持久平台；
- Planning-only Purchase Placeholder持久表；
- `ShippingTask`/有限物流Task体系；
- 采购制造Task；
- MultiDomain Candidate/原子跨域激活平台；
- 第二套90天ROUGH_CUT Solver；
- DSL/脚本规则平台。

**过度设计回流检查：通过。**

---

## 7. 静态检查

对目标DDL完成了以下静态检查：

- 括号词法平衡：通过；
- 字符串/注释扫描最终状态：正常；
- 新增四个核心最小对象仅各创建一次；
- `Task.PlannedProcessQty`确认落在`Task`而不是`Order`；
- 未发现`CREATE TABLE ShippingTask / FrozenZoneSnapshot / VirtualInventoryBalance / PeggingAllocationLedger / PlanningPurchasePlaceholder`；
- 未发现V1 Timed Supply正式链仍通过`WHERE 1=0`固定为空；
- `Domain_Dependency`当前默认值不再为2天。

说明：本环境未连接实际SQL Server实例，因此本轮属于**静态DDL审查**，不是生产数据库实机执行验证。正式部署前仍需2号位/DBA先在测试库执行兼容升级脚本并做对象差异检查。

---

## 8. 暂不在本轮擅自修改的技术兼容项

旧v5.1.1技术基线曾明确将`ScheduleExplanationFact.OrderId/TaskId/ResourceId`定义为`INT`，而Order/Task主键存在`BIGINT`结构。由于这涉及现有EF实体、真实数据库和历史数据兼容，本轮**不因为“看起来更统一”就擅自改类型**。

该项进入下一步“六份文档总交叉审查”的**技术一致性核验清单**：先核对真实数据库和2号位实体类型，再决定是否需要无业务含义的类型修正。它不是业务冻结问题，也不构成重新打开业务决策。

---

## 9. 最终结论与下一步

第六份权威技术文档已完成冻结对齐。至此六份权威技术文档均已完成第一轮修订。

下一步按冻结工作流执行：

> **对六份权威技术文档做一次总交叉审查。**

只检查：

1. 业务闭环；
2. 数量闭环；
3. 时间闭环；
4. 版本闭环；
5. 异常恢复闭环；
6. 0～5号位职责闭环；
7. 过度设计是否回流；
8. 六份之间字段/枚举/接口/职责是否真正一致；
9. 与2号位现有代码保护边界是否冲突。

在总交叉审查通过前，**不进入代码大改，也不重新讨论已冻结业务。**
