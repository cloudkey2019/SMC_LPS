# APS 集成接口设计规范

**版本**：v1.23  
**日期**：2026-07-06  
**基于**：《APS数据架构与防腐层设计方案 v1.33》+《APS_数据库字段说明文档 v5.0.46》+《APS_数据库表结构设计 v5.0.46》+《APS_各类基础数据分层承接与演变总表 v3.29》  
**更新**：跨厂Pegging补强、规则与参数引擎、四表职责收敛、管道供给完整骨架（对齐 DDL v5.0.46 / 防腐层 v1.33 / 字段说明 v5.0.46 / 演变总表 v3.29 / 核心走查 V3.14）

**v1.23更新内容**（2026-06-23 跨厂Pegging补强）：
- 🆕 **MES_ProcessCode_View.ERPProperty**：透出仓库/工序位置业务属性（M/XC/ZP/BP），来源于ERP真实属性
- 🆕 **ERP_Received_ByDocument_View / ext 包装视图**：ERP Received 按单据汇总接口（粒度=工厂+仓库+物料+单据类型+单据号）
- 🆕 **MES_APS_BOM_Workset_CrossFactoryEdge / APS_BOM_CROSS_FACTORY_EDGE_RAW**：BOM跨厂交接边表接口
- 📌 **PeggingSupplyAllocation**：APS 内部结果表，非外部接口，为内部数据消费边界；记录已确认可用的非Task供给分配细账
- 📌 **物理 Pegging 表**：仅记录 Task-to-Task 供需血缘，不承接库存/在途/Received 等非Task供给

**v1.21更新内容**（2026-06-23 四表职责收敛）：
- 🔄 **§1.3 ScheduleRun**：删除 OutputPlanVersionId，新增 BasePlanVersionId + ScopeJson；产出版本通过 PlanVersion.SourceScheduleRunId 反查
- 🔄 **PlanVersion**：VersionType→VersionCategory；新增 SourceScheduleRunId/SourceSimulationRunId/ActivatedAt/ActivatedBy
- 🔄 **Scenario**：Name→ScenarioName；RunType→ScenarioType；新增 ObjectiveJson/SelectedPlanVersionId/Status/UpdatedAt
- 🔄 **SimulationRun**：删除 PlanVersionId；新增 AlgorithmType/AlgorithmConfigJson/Status/ErrorMessage

**v1.19更新内容**（2026-06-15 管道供给链路完整骨架 + ODS契约视图14字段升级 + 分层语义修正，对齐 DDL v5.0.42 / 防腐层 v1.31 / 字段说明 v5.0.42 / 演变总表 v3.27）：

- 🔄 **§2.1.6 管道供给契约视图全面升级**：
  - ODS 层 `ERP_InterplantInTransit_View` 升级为14字段完整契约（新增 `MasterID`/`SourceFactoryCode`/`SourceDocumentLineNo`/`SourceUpdatedAt`）
  - 分层表述修正：ODS 视图=「ODS层/MES_Integration/来源ERP/5号位」；APS 包装视图=「APS层/APS_Production/2号位」
  - 新增字段语义红线：`FactoryCode`=目的工厂，`Quantity`=剩余在途数量，`MasterID`=物料映射主字段
  - 新增 APS 包装视图 `ext_ERP_InterplantInTransit_View`（显式列字段，禁止 SELECT *）
  - 新增契约锁定规则：V1.1/V2 允许调整视图内部实现逻辑，对外14字段投影不变
- 🔄 **§8.1 接口清单**：`ERP_InterplantInTransit_View` 类型修正为「ODS契约视图」，负责人修正为「5号位（ODS实现）」
  - `ext_ERP_InterplantInTransit_View` 类型修正为「APS跨库包装视图」，负责人修正为「2号位」
- 📌 **V1 空链路声明**：V1 返回 0 行，`ScheduleContext.PipelineSupplies` 为空集合

**v1.18更新内容**（2026-05-14 BOM防腐层物化边表架构调整，对齐 DDL v5.0.26 / 防腐层 v1.21）：
- ⚠️ **§1.3 Socket 更新**：`MES_BOM_Edge_Active`（物化边表）为 V1 正式 BOM 防腐合同层 Socket；`MES_BOM_View` v5.0.26 降为兼容视图
- ⭐ **§1.3 Plug 更新**：5号位新增 `sp_RefreshBOMEdgeActive`；StageDetail 全变体新增 `WorksetId`；`sp_CleanupBOMWorkset` 改按 WorksetId 清理
- ⭐ **§2.1.2 BOM视图设计说明升级**：补 `MES_BOM_Edge_Active` / WHILE迭代展开 / RefreshLog前置校验 / RequestDetailId投传记录
- 📌 **版本引用**：防腐层 v1.20 → v1.21；字段说明 v5.0.25 → v5.0.26；DDL v5.0.25 → v5.0.26

**v1.17更新内容**（2026-05-13 版本引用对齐 DDL v5.0.25 / 防腐层 v1.20）：
- 📌 **版本引用**：防腐层 v1.19 → v1.20；字段说明 v5.0.24 → v5.0.25；DDL v5.0.24 → v5.0.25
- 📌 **无结构性新增**：Batch 3 新增表（ScheduleRun / ScheduleExplanationFact / Summary三张表 / 阶段二骨架三张表）均为 APS 内部对象，不跨越 ODS 边界，不影响外部集成契约

**v1.16更新内容**（2026-05-13 OrderType重构+衍生字段澄清+Routing SourceSystem，对齐 DDL v5.0.24）：
- 🔄 **§2.1.1 v_APS_SalesOrder**：`OrderType` 注释更新（此层为ERP原始值，APS标准化在SP执行）；明确新枚举值映射 `SO/MTO → SALES_ORDER`，`MTS/SS/SS_U → PRODUCTION_INSTRUCTION`
- 🔄 **§2.1.3 视图1 RoutingOperation**：补 `SourceSystem`（追溯增强字段，非运行必需，`'MES'`；与 MES_BOM_View 模式对齐）
- 🔄 **§2.1.3 视图2 RoutingDependency**：补 `SourceSystem`（追溯增强字段）
- 📌 **设计决策写死**：`DemandMaturityStatus` 收窄为 `PRE_CONFIRMED/FORECAST`；`DELAYED` 已拆出为独立字段 `DelayStatus`，视图层无需感知（APS内部衍生）
- 📌 **设计决策写死**：`CustomerSegment` 由 `sp_ValidateAndPromoteOrders` 通过 `CustomerCodeMap` 推导，ODS视图不需要加列

**v1.15更新内容**（2026-05-09 管道供给链，对齐 DDL v5.0.23）：
- 🆕 §2.1.6 新增 `ERP_InterplantInTransit_View` 契约视图字段口径（当前管道供给链唯一 ODS 来源）
- 🆕 §8.1 接口清单新增 `ERP_InterplantInTransit_View` / `ext_ERP_InterplantInTransit_View` 行
- 🆕 §1.3 Socket-Plug Loader 清单新增 `LoadPipelineSupplies()` 方法签名
- 📌 **设计决策写死**：`InventoryBalance` 定义不变；管道供给链是独立并行链，结果为空不影响现有排程

---

**v1.14更新内容**（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21）：
- ✅ §2.1.1 v_APS_SalesOrder：`BOMNO` 字段说明改可空（v5.0.21废除"必填"要求，NULL=待5号位解析）
- ✅ §2.1.2 BOM双层结果：补 `RequestDetailId` 追溯锚点说明 + `MES_API_BOM_Request_Detail` 新结构说明
- ✅ §2.3.1 WriteToStagingAsync：BOMNO注释改可空；补 `sp_ValidateAndPromoteOrders` 写入 `FailureCode`/`NextActionCode` 双维度说明
- 📌 **设计决策写死**：`FailureCode`=原因维度，`NextActionCode`=动作维度，两个独立字段，禁止混用；BOM入口解析分流在5号位Workset阶段执行

**v1.13更新内容**（2026-05-04 BOM 回填 SP 完整实现，对齐 DDL v5.0.18 / 防腐层 v1.16）：
- ✅ §BOM 双层结果说明：“5号位后置回填”升级为明确 `sp_EnrichBOMWorkset` SP 名称，补充 `ChildRequiredFactory` + Issues 降级登记
- ✅ 修正“回填完成后批次才标记READY”→“永不阻塞批次”（对齐 v5.0.11 决策）
- ✅ 相关权威文档引用版本升级

---

**v1.12更新内容**（2026-04-29 生产部门主链 + ProcessCodeDict 重定位 + WorkshopCode 全局清理）：

### 契约视图字段升级（4 + 1 个视图）

| 契约视图 | 字段变更 | 受影响小节 |
|---|---|---|
| `MES_APS_Resource_View` | DROP `WorkshopCode` + ADD **`ProductionDeptCode`** | §2.1.4 视图5 + §8.1 接口清单 |
| `MES_APS_Routing_Operation_View` | ADD **`ProductionDeptCode`** | §2.1.3 视图1 |
| `MES_APS_Routing_Dependency_View` | ADD **`ProductionDeptCode`** | §2.1.3 视图2 |
| `APS_OperationResourceEligibility_View` | ADD **`ProductionDeptCode`** | §2.1.3 视图3 |
| `MES_ProcessCode_View` | ADD **`StageCode`**（APS 增强列）+ RENAME `SourceSystem` → **`CodeOrigin`** | §2.1.5 视图（ProcessCode 契约） |

**字段契约升级流程**：DBA 提交契约视图变更 PR → 0 号位审批 → 上线；任何下游消费方代码零改动（`ext_*` 跨库包装视图自动透传）。

### 新增同步 SP / 新增配置接口

| 接口 | 负责人 | 说明 |
|---|---|---|
| `sp_RebuildMaterialStageDeptContext(@TriggerMode, @BatchNo, ...)` 🆕 | 2 号位 | ⚠️ **占位骨架，当前未实现**（DDL Step1~6 全 TODO，v1 仅有签名与日志骨架）。设计意图：三触发 `FULL` / `INCR` / `PARTIAL`；产出 `MaterialStageDeptContext` + `MaterialStageDeptContext_Issues`。实装前下游消费方调用应做空表/降级处理 |
| `sp_SyncResourceData` 升级 | 2 号位 | MERGE 逻辑加 `ProductionDepartment` 双字典映射 JOIN（FactoryCode + ProductionDeptCode 任一未命中即跳过） |
| `MaterialStageDeptOverride` 维护 API 🆕 | 0 号位 / 业务 | UI/SQL 入口；导入时做 Model → MaterialCode 1:1 检查；1:N 拒收并返回明细 |
| `ProductionDepartment` 字典 CRUD 🆕 | 0 号位审批 + 业务 | 建/改部门记录走审批；1:1 归属 StageCode（不允许多阶段） |
| `ProcessCodeDict` 字典 CRUD 🔄 | APS 系统管理员 + 0 号位审批 | v5.0.16 翻转：取消 `sp_SyncMasterData(@SourceType='ProcessCode')` 自动同步分支；改为 UI/SQL 人工维护 |

### 1 号位排程消费契约定调

```text
排程主链（v5.0.16 红线 #15）：
  FROM StageDetail              -- 5 号位派生（已存在）
    取 (MaterialId, StageCode)
  →  MaterialStageDeptContext   -- 2 号位 sp_RebuildMaterialStageDeptContext 产出（v5.0.16 新增）
    按 (MaterialId, StageCode) 唯一索引取 DefaultProductionDepartmentId
  →  RoutingOperation / RoutingDependency / OperationResourceEligibility  -- 三件套
    按 (MaterialId, ProductionDepartmentId, StageCode) 三元组锁定（v5.0.16 唯一键升级）
    → 生成 Task（小工序 / 依赖图 / 资源能力）
```

**1 号位消费红线**：
- ❌ 1 号位**禁止直接读** `MaterialSupplyContext` / `ProcessCodeDict` / `MaterialStageDeptOverride`（这些是 2 号位组装 Context 的输入源，不是 1 号位入口）
- ❌ 1 号位**禁止跳过 Context** 直查 Routing 三件套（必须先经 `MaterialStageDeptContext` 锁部门）

### EAM 扩展路径预留（不变）

未来 EAM 上线时：
- 并行新增 `EAM_APS_Resource_View`（同构契约，含 `ProductionDeptCode`）
- `sp_SyncResourceData(@SourceType='EAM')` 实现 NOT_IMPLEMENTED 分支
- 双字典映射逻辑零分叉

**相关权威文档（v1.12 基线，2026-04-29）**：
- `APS_数据库表结构设计_v5.0.sql` → **v5.0.18**（含 sp_EnrichBOMWorkset / sp_EnrichBOMWorksetRealtime 完整实现）
- `APS_数据库字段说明文档_v5.0.md` → **v5.0.18**
- `APS_数据架构与防腐层设计方案_v5.0.md` → **v1.16**
- `APS_资源与工艺数据模型重设计方案_v5.0.md` → **v5.2**
- `APS_各类基础数据分层承接与演变总表_v5.0.md` → **v3.12**

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

**v1.11更新内容**（2026-04-25 资源 ODS 契约视图命名统一 + sp_SyncResourceData 占位 SP）：
- ✅ §1.3 Socket-Plug + Loader 清单：`ext_APS_Resource_View` → `ext_MES_APS_Resource_View`；新增执行体 `sp_SyncResourceData(@SourceType)`（DDL v5.0.13）
- ✅ §8.1 接口清单：`APS_Resource_View` 条目重命名为 `MES_APS_Resource_View`（与 MES_APS_Routing_*_View 对齐）
- ✅ EAM 扩展路径明确：未来 EAM 上线时并行新增 `EAM_APS_Resource_View` + `ext_EAM_APS_Resource_View`（双源同构契约零分叉）
- 【设计决策】ODS 契约视图命名永久统一为“源系统_消费方_实体_View”三段式（单源可省略消费方）

**v1.10更新内容**（2026-04-25 工艺数据三层模型收敛）：
- ✅ §2.1.3 视图1 RoutingOperation：`ProcessType` 注释改为"辅助分类标签，不参与排程对接"；`StageCode` 注释强化"BOM↔Routing 对接主键之二，必须取自 `StageDict`"
- ✅ §2.1.3 视图4 RoutingStage：**删除 `StageSeq` 字段**；定位收敛为"该物料在哪些大工艺阶段存在配置"字典；顺序权威唯一信 `StageDetail.StageSeq`
- ✅ §2.1.3 新增"三层模型硬红线"小节：OperationCode/OperationName ≠ ProcessType ≠ StageCode
- ✅ §2.1.3 新增"BOM↔Routing 对接主键（业务 vs 物理）"小节：业务 `(MaterialCode, StageCode)` / 物理 `(MaterialId, StageCode)` 等价
- ✅ §2.1.3 `ProcessType` 值域配置化：新增 `ProcessTypeDict`（骨架 `IsActive=0`）说明；停用硬编码 `MACHINING/ASSEMBLY/INSPECTION/MANUAL` 的表述
- ✅ §2.1.2 BOM视图：StageDetail.StageCode **R20 目标工厂视角**约定（父件 TJ + R20 指派 BJ → 直接写 `BJ_MACH`）
- 【设计决策】OperationCode 不全局字典化（MES 不可控 + 新增频繁；跨厂对接靠 StageCode 足够）

**v1.9更新内容**（2026-04-15 StageDetail升级为统一阶段路径结果表，支持ROOT）：
- ✅ §1.3 Socket-Plug：5号位职责补StageDetail写入；2号位Loader补APS_BOM_STAGE_PATH_RAW搬运；RoutingStage标注阶段字典定位
- ✅ §2.1.2 BOM视图：StageDetail说明升级为统一阶段路径（EDGE+ROOT），补ROOT推导规则和1号位查询约定
- 【设计决策】ROOT记录：ParentMaterialCode=NULL，IsSupplyThreshold恒为0
- 【设计决策】1号位消费查询必须显式按StageScopeType区分

**v1.8更新内容**（2026-04-13 BOM双层结果+阶段提前期参数化，基于《BOM阶段顺序与Workset双层结果设计建议_v1.0》）：
- ▸ 本版替代v1.7，方案升级：单一StageHintCode → 3原始辅助字段 + StageDetail双层结果
- ✅ §1.3 Socket-Plug：`MES_BOM_View` 加列改为 `ParentProcRefCode` + `ChildProcRefCode` + `ChildSourceHintCode`
- ✅ §2.1.2 BOM视图：字段替换 + StageDetail双层结果说明
- ✅ §2.1.3 工艺路线视图：视图4「大工艺阶段」定位调整为阶段字典
- ℹ️ 【设计决策】ChildRequiredStageCode=NULL时按保守策略：子件必须全工艺完成后才可供给父件
- ℹ️ 【设计决策】RoutingStage=阶段字典，StageDetail=5号位派生结果，职责分离
- ℹ️ 【V1不建】BomProcessBinding / StageHintMapping 留V2

**v1.6更新内容**（2026-04-09 客户分级字段）：
- ℹ️ CustomerTier为APS衍生字段（VIP/KEY_ACCOUNT/STANDARD/GENERAL），由sp_ValidateAndPromoteOrders推导
- ℹ️ 视图无需加列（输入信号已在CustomerCode/RawData中），ERPOrderSyncService MERGE无需改动

**v1.5更新内容**（2026-04-09 订单ETL v1.2增补，基于《仅1.2增补内容v1.0》）：
- ✅ §2.1.2 v_APS_SalesOrder视图加列：+IssueDate/OriginalDueDate/ReceivedQty
- ✅ §2.1.2 字段说明补充：IssueDate/OriginalDueDate/ReceivedQty说明
- ✅ §2.3.1 ERPOrderSyncService WriteToStagingAsync MERGE语句补新3字段

**v1.4更新内容**（2026-04-09 订单业务字段补充，基于《订单ETL补充字段设计建议v1.1》）：
- ✅ §2.1.2 v_APS_SalesOrder视图加列：+TransportMode/CustomerName（源事实）、MTS补InstructionNo（≠OrderNo）
- ✅ §2.1.2 字段说明补充：业务澄清OrderType/FactoryCode为APS衍生字段，TransportMode/CustomerName/InstructionNo说明
- ✅ §2.3.1 ERPOrderSyncService WriteToStagingAsync MERGE语句补新字段

**v1.3更新内容**（2026-04-08 集成接口审计修正）：
- ✅ §1.1 ERP同步频率修正：5分钟/实时 → 每小时/每日凌晨，接口方式 CDC → 时间戳轮询
- ✅ §2.2 CDC整节废弃，改为时间戳增量轮询机制说明，补ODS侧ext_v_APS_SalesOrder包装视图
- ✅ §2.3.1 ERPOrderSyncService重写：CDC→时间戳轮询、直连ERP→ODS ext_视图、EF Core→Dapper
- ✅ §2.3.2 承诺交期回写SQL：PostgreSQL ON CONFLICT → SQL Server MERGE
- ✅ 章节编号修正：两个"四"→四/五依次递增至八
- ✅ §3.3 MES Adapter全部代码EF Core→Dapper（MQ消费者/REST轮询/计划下发/工艺路线同步）
- ✅ §4.2 采购Adapter代码EF Core→Dapper
- ✅ §5.2 OA Adapter代码EF Core→Dapper（含事务写法）
- ✅ §8.1 接口清单移除CDC配置行

**v1.2更新内容**（2026-04-03 订单链路审计）：
- ✅ §2.3.1 ERPOrderSyncService全面对齐：SyncStatus "NEW"→"PENDING"、补BOMNO/SourceSystem/SourceMasterID字段、增量频率改每小时、加sp_ValidateAndPromoteOrders调用、方法重命名WriteToStagingAsync
- ✅ v_APS_SalesOrder视图补BOMNO/SourceSystem/SourceMasterID/DueDate字段，补MTS UNION ALL来源说明

**v1.1更新内容**（2026-03-19）：
- 补充Socket-Plug集成模式、更新ERP/MES集成视图定义、明确职责分工

---

**相关文档**：
- **《APS_各类基础数据分层承接与演变总表_v5.0》**（当前 v3.29）：数据演进全景图
- **《APS_数据架构与防腐层设计方案_v5.0》**（当前 v1.33）：防腐层设计详解
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：Socket-Plug职责分工

---

## 一、集成架构总览

### 1.1 集成系统清单

| 系统 | 类型 | 接口方式 | 同步频率 | 数据方向 |
|------|------|---------|---------|---------|
| **ERP** | 自研 | 数据库视图（时间戳轮询） | 每小时/每日凌晨 | 双向 |
| **MES** | 自研 | MQ消息队列 + REST API | 实时/5分钟 | 双向 |
| **采购系统** | 自研 | 数据库视图 | 5分钟 | 单向（读取） |
| **OA审批** | 第三方 | REST API | 实时 | 双向 |
| **邮件网关** | 第三方 | REST API | 实时 | 单向（发送） |
| **短信网关** | 第三方 | REST API | 实时 | 单向（发送） |

### 1.2 集成模式

```
┌─────────────────────────────────────────────────────────────┐
│                        APS System                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │           Integration Layer (Adapters)                │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │   │
│  │  │   ERP    │  │   MES    │  │Procurement│           │   │
│  │  │ Adapter  │  │ Adapter  │  │  Adapter  │           │   │
│  │  └──────────┘  └──────────┘  └──────────┘           │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │   │
│  │  │    OA    │  │  Email   │  │   SMS    │           │   │
│  │  │ Adapter  │  │ Adapter  │  │ Adapter  │           │   │
│  │  └──────────┘  └──────────┘  └──────────┘           │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Staging Tables (ETL)                     │   │
│  │  ERP_Order_Staging, MES_Actual_Staging, SyncCheckpoint│  │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
    ↓ DB视图(轮询)  ↓ MQ+REST API    ↓ DB视图    ↓ REST API
┌──────────────┐ ┌──────────────┐ ┌──────────┐ ┌──────────────┐
│ ERP Database │ │  MES System  │ │Procurement│ │  OA/Email/   │
│ (SQL Server) │ │ (MQ+HTTP API)│ │  Database │ │  SMS Gateway │
└──────────────┘ └──────────────┘ └──────────┘ └──────────────┘
```

### 1.3 Socket-Plug集成模式

**架构原则**：采用Socket-Plug模式实现防腐层，确保APS系统与源系统解耦。

**契约插座（Socket）** - 源系统侧防腐：
- **ERP DBA负责**：创建`ERP_Master_View`（ERP主数据契约视图）
- **MES DBA / 5号位负责**：`MES_BOM_Edge_Active`（ODS库物化边表，**V1 BOM防腐合同层+执行优化层**，v5.0.26新增 2026-05-14）；`MES_BOM_View`（v5.0.26降为兼容视图 `SELECT * FROM MES_BOM_Edge_Active WHERE IsActive=1`）；`MES_Material_View`（MES物料契约视图）
- **职责红线**：契约视图的字段名和数据类型在v1.x版本中永不变更，只能追加字段

**数据插头（Plug）** - ODS侧防腐：
- **5号位负责**：
  - 创建`ext_ERP_Master_View`（ERP主数据跨库包装视图）
  - 创建`ext_MES_Material_View`（MES物料跨库包装视图）
  - `sp_RefreshBOMEdgeActive`：刷新 `MES_BOM_Edge_Active` 物化边表，写 RefreshLog RUNNING→COMPLETED/FAILED（v5.0.26新增 2026-05-14）
  - `sp_ExpandBOMBatch_vNext`：WHILE 迭代展开，直读 `MES_BOM_Edge_Active`，透传 RequestDetailId；旧 `sp_ExpandBOMBatch` 标记 deprecated
  - 后置回填`ChildRequiredStageCode` + 写入`MES_APS_BOM_Workset_StageDetail`（双层结果，v1.8新增）；**v5.0.26 +1列 `WorksetId`**（含Archive/Realtime变体）；`sp_CleanupBOMWorkset` 改按 WorksetId 清理
- **3号位负责**（v5.0更新）：
  - ~~创建`MES_APS_Routing_View`~~（v5.0废弃，已拆分为以下3个视图）
  - 创建`MES_APS_Routing_Operation_View`（工序节点视图）
  - 创建`MES_APS_Routing_Dependency_View`（工序依赖视图）
  - 创建`APS_OperationResourceEligibility_View`（工序资源能力视图）
  - 创建`MES_APS_Routing_Stage_View`（大工艺阶段视图，v1.7新增，v1.8定位调整为阶段字典 2026-04-15 更新）

**数据装载（Loader）** - APS侧（v5.0更新，v1.8更新 2026-04-15）：
- **2号位负责**：
  - 从ODS库拉取数据到APS库
  - 执行`sp_SyncMasterData(@SourceType)`（主数据同步）
  - 执行数据结构标准化（MaterialMapping → Material表）
  - 执行`APS_BOM_RAW → BOM表`转换
  - `MES_APS_BOM_Workset_StageDetail → APS_BOM_STAGE_PATH_RAW`：搬运阶段顺序明细（v1.8新增，与APS_BOM_RAW同批次拉取）
  - 从3个ext_包装视图分别装载到`RoutingOperation`/`RoutingDependency`/`OperationResourceEligibility`（v5.0新增）
  - 从ext_MES_APS_Routing_Stage_View装载到`RoutingStage`阶段字典（v1.7新增，v1.8定位调整为阶段字典）
  - 从`ext_MES_APS_Resource_View`全量刷新`Resource`表（v5.0新增；v1.11 命名统一，原名 `ext_APS_Resource_View`）
  - 执行 `sp_SyncResourceData(@SourceType)` 资源主数据同步（DDL v5.0.13 新增；v1 仅 'MES' 分支，'EAM' 分支预留）
  - 执行 `sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, @RowsAffected OUTPUT, @ErrorMessage OUTPUT)` 管道供给链同步（DDL v5.0.42；详见防腐层 §2.5）
  - `IDataLoader.LoadPipelineSupplies()` 方法签名（见下方）：从 `SupplyFact_Pipeline` 装载 `PipelineSupplyItem` 列表，注入 `ScheduleContext.PipelineSupplies`

**PipelineSupplyItem 契约定义**（V1.1/V2 完整版；V1 为空集合）：

| 字段 | 类型 | 来源 |
|------|------|------|
| Id | BIGINT | SupplyFact_Pipeline.Id |
| MaterialCode | string | mat.MaterialCode（APS 标准编码） |
| MaterialId | int | mat.Id（NOT NULL） |
| FactoryCode | string | SupplyFact_Pipeline.FactoryCode |
| FactoryId | int | SupplyFact_Pipeline.FactoryId（NOT NULL） |
| ProductFamilyId | int? | mat.ProductFamilyId（物料未配置时可为空） |
| SupplyType | string | INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / ... |
| OwnershipType | string | OWNED / CONSIGNMENT / SUPPLIER |
| QualityStatus | string | AVAILABLE / PENDING_INSPECTION / HOLD |
| Quantity | decimal | 剩余在途数量 |
| ETA | DateTime? | ERP 原始预计到达时间 |
| AvailableTime | DateTime? | ETA + LeadTimeOffset（规则裁决后） |
| StorageCode | string | 目的仓库编码 |
| SupplierCode | string | 供应方编码 |
| SourceSystem | string | 来源系统（值域：ERP / PROCUREMENT / VMI；由 ext_PipelineSupply_Source_View 各分支派生） |
| SourceDocumentNo | string | ERP 来源单据号 |
| SourceDocumentLineNo | string | ERP 来源单据行号 |
| SourceMasterID | int | ODS.MasterID 直通 |
| SourceFactoryCode | string | 发出工厂编码 |
| SupplyAvailabilityRuleId | int? | 命中规则 ID（无命中为 NULL） |
| AppliedLeadTimeOffset | int? | 命中的 LeadTimeOffset（小时） |
| RulePriority | int? | 命中规则的 Priority 值 |
| RuleEvaluatedAt | DateTime? | 规则裁决时间（= @DataCutoffTime） |

**Loader 契约**：

```csharp
namespace APS.Core.Loaders
{
    /// <summary>
    /// 管道供给加载器。夜间按 BatchNo 读取，白天按 BatchNo IS NULL 读取。
    /// </summary>
    public interface IPipelineSupplyLoader
    {
        /// <summary>
        /// 加载管道供给到内存集合。
        /// </summary>
        /// <param name="batchNo">夜间快照批次号；白天实时传 null</param>
        /// <param name="dataCutoffTime">数据切片边界</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>管道供给只读列表。V1 返回空集合。</returns>
        Task<IReadOnlyList<PipelineSupplyItem>> LoadPipelineSuppliesAsync(
            string? batchNo,
            DateTime dataCutoffTime,
            CancellationToken cancellationToken);
    }
}
```

**消费查询**（V1.1/V2）：
- 夜间快照：`WHERE BatchNo = @CurrentBatchNo AND IsActive = 1 AND AvailableTime IS NOT NULL`（ETA=NULL 的记录不进入正式供给扣减，另查询进入"待确认管道清单"）
- 白天实时：`WHERE BatchNo IS NULL AND IsActive = 1`（仅为字段预留，V1/V1.1不启用）
- **禁止**只按 `IsActive = 1` 查询（跨批次污染）

**职责红线**：
- ❌ 源系统DBA不得修改契约视图的字段名和数据类型（只能追加字段）
- ❌ 5号位不得修改源系统的物理表结构（只能创建跨库包装视图）
- ❌ 2号位不得直接访问源系统表（只能通过ODS库的ext视图拉取）

---

## 二、ERP 集成接口设计

### 2.1 ERP数据库视图定义（由ERP侧提供）

#### 2.1.1 ERP主数据契约视图（ERP_Master_View）

**负责人**：ERP DBA（源系统侧）

**所属库**：ERP生产库

**用途**：暴露ERP主数据标准字段，供ODS库跨库访问

```sql
-- 契约版本：v1.0
-- 最后修改：2026-03-19
-- 负责人：ERP DBA
CREATE VIEW ERP_Master_View AS
SELECT 
    MaterialCode,          -- 物料编码（契约字段）
    MaterialName,          -- 物料名称（契约字段）
    MasterID,              -- ERP物理主键（契约字段）
    Warehouse,             -- 仓库（契约字段）
    UOM,                   -- 计量单位（契约字段）
    LeadTimeDays,          -- 提前期天数（契约字段）
    SafetyStock,           -- 安全库存（契约字段）
    IsActive               -- 是否有效（契约字段）
FROM ERP.dbo.master
WHERE IsDeleted = 0;

-- ⚠️ 契约承诺：无论ERP内部表结构如何变更，此视图的列名和数据类型永不变更
```

#### 2.1.2 订单视图（v_APS_SalesOrder）（2026-04-03 订单链路审计修正）

**负责人**：ERP DBA（源系统侧）

**⚠️ 数据来源说明**（2026-04-03补充）：
- 此视图统一暴露**两类订单来源**：SO/MTO客户订单（来自订单中间表）+ MTS生产指示（来自生产指示中间表）
- 由 `OrderType` 字段区分（视图层为ERP原始值）：`SO`=销售订单, `MTO`=按单生产, `MTS`=库存生产指示, `SS`=安全库存, `SS_U`=紧急安全库存；`sp_ValidateAndPromoteOrders` 负责标准化（v5.0.24重分类：SO/MTO→`SALES_ORDER`；MTS/SS/SS_U→`PRODUCTION_INSTRUCTION`）
- ERP DBA负责在视图中 UNION ALL 两类中间表，对APS透明

```sql
-- ERP侧创建视图（只读）
-- ⚠️ 2026-04-03审计修正：补BOMNO/SourceSystem/SourceMasterID/DueDate，统一SO+MTS
-- ⚖️ 2026-04-09 v1.4：补TransportMode/CustomerName（源事实字段），MTS补InstructionNo（≠OrderNo）
-- ⚖️ 2026-04-09 v1.5：补IssueDate/OriginalDueDate/ReceivedQty（源事实字段，v1.2增补）
--   【业务澄清】OrderType/FactoryCode在此视图中为“原始值/简化映射”，真正的APS标准化由sp_ValidateAndPromoteOrders执行
--   本视图可包含“辅助字段”（用于衍生字段的输入信号），它们会进入RawData JSON但不作为Staging显式列
CREATE VIEW v_APS_SalesOrder AS
-- 客户订单（SO/MTO/SS/SS_U）
SELECT 
    CAST(OrderId AS NVARCHAR(100)) AS SourceOrderId,  -- ERP订单主键
    OrderNo,                            -- 订单号
    OrderType,                          -- SO/MTO/SS/SS_U（⚖️ 2026-04-09业务澄清：此处为ERP原始值，APS标准化由SP执行）
    MaterialCode,                       -- 物料编码
    BOMNO,                              -- BOM编号（⚠️ 2026-04-03审计补充）
    FactoryCode,                        -- 工厂编码（⚖️ 2026-04-09业务澄清：此处为ERP原始值，APS标准化由SP执行）
    Quantity,                           -- 数量
    UOM,                                -- 单位
    CustomerDueDate AS DueDate,         -- 交期（⚠️ 统一为DueDate）
    Priority,                           -- 优先级（1-100）
    Status,                             -- 订单状态
    'ERP' AS SourceSystem,              -- 来源系统（⚠️ 2026-04-03审计补充）
    MasterID AS SourceMasterID,         -- ERP物理主键（⚠️ 2026-04-03审计补充）
    -- ⚖️ 2026-04-09 v1.4 新增：源事实字段
    TransportMode,                      -- 运输方式（海运/空运/陆运）
    CustomerName,                       -- 客户名称
    NULL AS MTS_InstructionNo,          -- SO/MTO无生产指示号
    -- ⚖️ 2026-04-09 v1.5 新增：源事实字段（v1.2增补）
    OrderDate AS IssueDate,             -- 订单发行日期
    OriginalDueDate,                    -- 原始纳期（客户最初要求交期）
    NULL AS ReceivedQty,                -- SO订单无入库数量
    CreatedAt,                          -- 创建时间
    UpdatedAt                           -- 更新时间
FROM ERP.dbo.SalesOrder
WHERE Status NOT IN ('CANCELLED', 'CLOSED')
AND CreatedAt >= DATEADD(DAY, -90, GETDATE())

UNION ALL

-- MTS生产指示
SELECT 
    CAST(InstructionId AS NVARCHAR(100)) AS SourceOrderId,
    InstructionNo AS OrderNo,
    'MTS' AS OrderType,
    MaterialCode,
    BOMNO,
    FactoryCode,
    Quantity,
    UOM,
    PlannedDate AS DueDate,             -- MTS用PlannedDate作为DueDate
    Priority,
    Status,
    'ERP' AS SourceSystem,
    MasterID AS SourceMasterID,
    -- ⚖️ 2026-04-09 v1.4 新增：源事实字段
    TransportMode,                      -- 运输方式
    NULL AS CustomerName,               -- MTS生产指示无客户名称
    InstructionNo AS MTS_InstructionNo, -- ⚖️ 生产指示号（≠OrderNo，独立InstructionNo字段）
    -- ⚖️ 2026-04-09 v1.5 新增：源事实字段（v1.2增补）
    IssuedDate AS IssueDate,            -- 生产指示发行日期
    PlannedDate AS OriginalDueDate,     -- MTS原始纳期 = DueDate
    ReceivedQty,                        -- 已入库数量（MTS累计入库）
    CreatedAt,
    UpdatedAt
FROM ERP.dbo.ProductionInstruction      -- ⚠️ 生产指示中间表
WHERE Status NOT IN ('CANCELLED', 'CLOSED')
AND CreatedAt >= DATEADD(DAY, -90, GETDATE());
```

**字段说明**：
- `SourceOrderId`：ERP系统订单主键（NVARCHAR(100)，兼容INT和字符串主键）
- `OrderType`：订单类型（SO=销售订单, MTO=按单生产, MTS=库存生产指示, SS=安全库存, SS_U=紧急安全库存）（⚖️ 2026-04-09业务澄清：视图中为ERP原始值，APS标准化由`sp_ValidateAndPromoteOrders`执行）
- `FactoryCode`：工厂编码（⚖️ 2026-04-09业务澄清：视图中为ERP原始值，APS标准化由SP执行）
- `BOMNO`：BOM编号（⚠️ 2026-04-03审计补充；⚠️ v1.14/v5.0.21：改可空——有值=显式BOMNO，NULL=待5号位Workset解析BOM入口；ERP视图侧允许NULL透传）
- `SourceSystem`：来源系统标识（⚠️ 2026-04-03审计补充，固定为'ERP'）
- `SourceMasterID`：ERP物理主键（⚠️ 2026-04-03审计补充，用于物理追溯）
- `TransportMode`：运输方式（海运/空运/陆运），源事实字段（⚖️ 2026-04-09 v1.4新增）
- `CustomerName`：客户名称，ERP订单表直接提供，源事实字段（⚖️ 2026-04-09 v1.4新增）
- `MTS_InstructionNo`：生产指示号，仅MTS订单有值，来源于ProductionInstruction表的InstructionNo字段（≠OrderNo）（⚖️ 2026-04-09 v1.4新增）
- `IssueDate`：订单发行/下发日期，源事实字段；SO用OrderDate，MTS用IssuedDate（⚖️ 2026-04-09 v1.5新增）
- `OriginalDueDate`：原始纳期（客户最初要求交期），MTS时=PlannedDate（⚖️ 2026-04-09 v1.5新增）
- `ReceivedQty`：已入库数量，仅MTS有值（累计入库），SO订单为NULL（⚖️ 2026-04-09 v1.5新增）
- `DueDate`：统一交期（SO用CustomerDueDate，MTS用PlannedDate）
- `Priority`：优先级，1-100，数字越小优先级越高
- `Status`：订单状态（Open, Released, Scheduled, Completed, Cancelled）

**视图辅助字段边界说明**（2026-04-09 v1.4新增）：
- 本视图可包含“仅用于生成APS衍生字段的辅助字段”（如用于判断OrderType的源字段）
- 辅助字段会被`ERPOrderSyncService`读取，序列化到`RawData` JSON中保留
- 辅助字段**不作为Staging/Canonical的显式列**，仅衍生结果字段才落列

#### 2.1.2 BOM视图（v_APS_BOM）

```sql
CREATE VIEW v_APS_BOM AS
SELECT 
    BOMId AS SourceBOMId,
    ParentMaterialCode,
    ChildMaterialCode,
    Quantity,
    ScrapRate,
    LeadTimeOffset,
    BOMLevel,
    LowLevelCode,                   -- ⚠️ 新增：低阶码（LLC），用于拓扑排序
    EffectiveFrom,
    EffectiveTo,
    IsActive,
    ParentProcRefCode,         -- v1.8 父件工序参考码（ERP BOM原始辅助字段）
    ChildProcRefCode,          -- v1.8 子件工序参考码（同上）
    ChildSourceHintCode        -- v1.8 子件来源提示码（当前来源=ERP BOM的produce字段，0/1/2编码）
FROM ERP.dbo.BOM
WHERE IsActive = 1;
```

**⚠️ v1.8 BOM双层结果与阶段提前期参数化说明**（2026-04-13 更新，2026-04-15 v1.9补ROOT）：
- **3原始辅助字段**：`ParentProcRefCode` + `ChildProcRefCode` + `ChildSourceHintCode`，通过`MES_BOM_View`契约字段承接，APS不绑定ERP物理字段名
- **追溯增强字段**（v1.9）：`SourceSystem`（ERP/MES）+ `SourceBOMId`（源系统BOM物理主键）— 非运行必需，建议加入以支持版本裁决排查
- **⚠️ 唯一默认版本裁决原则（v1.9写死）**：`MES_BOM_View` 不是简单并表，ODS内部必须先裁决出唯一胜出版再暴露为`IsDefaultVersion=1`；VersionPriority不暴露为正式契约字段
- **双层结果Workset**：
  - 主层：`MES_APS_BOM_Workset` 携带 3辅助字段 + `ChildRequiredStageCode`（最终供给阈值，不承载根产品自身路径）
  - 明细层：`MES_APS_BOM_Workset_StageDetail` — **统一阶段路径结果表**（v1.9升级 2026-04-15），通过 `StageScopeType` 区分：
    - `EDGE`：子件供给路径（`ParentMaterialCode`=父件编码）
    - `ROOT`：根产品完工路径（`ParentMaterialCode=NULL`，`IsSupplyThreshold=0`）
- **流程**：`sp_RefreshBOMEdgeActive` 刷新边表（RefreshLog前置校验）→ `sp_ExpandBOMBatch_vNext` WHILE迭代展开+透传 RequestDetailId；`sp_EnrichBOMWorkset(@BatchNo)` 后置回填 `ChildRequiredStageCode` + `ChildRequiredFactory`（R17 工厂映射）+ 写入 StageDetail（EDGE+ROOT，WorksetId透传）+ 异常降级登记到 Issues（**永不阻塞批次**，v5.0.11 决策）
- **v1.14 RequestDetailId 追溯锚点**：`MES_APS_BOM_Workset.RequestDetailId` 和 `Issues.RequestDetailId` FK→`MES_API_BOM_Request_Detail.Id`；nullable；非业务键；**1号位主链不消费**；2/5号位追溯、回写、运营闭环用
- **v1.14 MES_API_BOM_Request_Detail 新结构**（v5.0.21）：新增 `OrderStagingId`/`Model`/`MaterialCode`/`FactoryCode`；`BOMNO` 改可空；唯一约束变更为 `(BatchNo, OrderStagingId)`——按订单粒度写入，BOM入口解析分流由**5号位Workset阶段负责**，2号位仅透传基础字段
- **ROOT推导规则**（v1.9）：5号位取Level=1的`ParentProcRefCode` → 映射标准化阶段路径 → 多条不一致时取最长路径+记WARNING（不静默并集）
- **1号位消费查询约定**（v1.9）：**必须按`StageScopeType`区分查询**，不得混查EDGE+ROOT
- **⚠️ NULL降级策略（写死）**：当 `ChildRequiredStageCode = NULL` 时，1号位排程引擎按**保守策略**处理——子件必须全工艺完成后才可供给父件
- **StageLeadTimeParam**：参数化外协阶段提前期，1号位对无RoutingOperation的阶段查此表生成标准Task
- **⚠️ v1.10 StageDetail.StageCode 取值约定（R20 目标工厂视角）**：
  - `StageDetail.StageCode` 必须取自 `StageDict`（字段文档 §1.9）
  - **R20 跨组织场景采用目标工厂视角**：父件在 TJ 工厂、子件 Produce=6（R20 指派到 BJ）→ `StageCode = BJ_MACH`（不是 TJ_MACH）
  - 1 号位读 StageCode 直接去目标工厂的 `RoutingOperation` 匹配 `(MaterialId, StageCode)` 找小工序生成 Task，Task 自动落在目标工厂产能队列，**无需跨厂翻译**
  - 落地责任：5 号位 `sp_EnrichBOMWorkset` 按 `ChildRequiredFactory`（查 `ProduceToFactoryMap` 得出）决定 StageCode 的工厂前缀
- **V1不建**：`BomProcessBinding` / `StageHintMapping` 留V2；结果字段已预埋在 Workset / Archive / APS_BOM_RAW 中

**⚠️ 重要说明 - LowLevelCode（低阶码）**：
- **定义**：物料在BOM树中的最大深度，用于支持APS步骤2.6的级联扫尾与拓扑排序
- **计算规则**：
  - 成品（无父件）：LLC = 0
  - 半成品/原料：LLC = MAX(父件LLC) + 1
  - 如果一个物料同时用于多个层级，取最大值
- **示例**：
  ```
  成品A (LLC=0)
    ├─ 半成品B (LLC=1)
    │   └─ 原料D (LLC=2)
    └─ 半成品C (LLC=1)
        └─ 原料D (LLC=2)  // D的LLC取最大值2
  ```
- **维护方式**：ERP侧需要在BOM变更时自动计算并更新LLC，或提供存储过程供APS调用计算
- **业务意义**：用于瀑布式扫尾时按LLC降序处理孤儿单据，避免多级工艺链下的负荷重复计算

#### 2.1.3 工艺路线视图（v5.0重构：拆分为3个视图）

> ⚠️ v5.0更新：原`v_APS_Routing`单视图已废弃，拆分为工序节点、工序依赖、工序资源能力三个独立视图。
> 详细字段定义见`APS_数据库字段说明文档_v5.0.md`。

**视图1：工序节点**（v5.0.1更新 2026-04-02：MaterialCode → MES_ID + Model）
```sql
CREATE VIEW MES_APS_Routing_Operation_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,            -- 默认'DEFAULT'
    PathId,               -- 默认1
    OperationCode,        -- 工序编码（唯一，如 NC / MC / 切断 / 精修）— 第1层：具体工序（执行粒度）
    OperationName,        -- 工序中文名
    ProcessType,          -- 第2层：辅助分类标签（值域见 ProcessTypeDict 骨架，骨架期 IsActive=0）；⚠️ v1.10：不参与 BOM↔Routing 对接，仅用于报表/粗分组/统计
    StageCode,            -- 第3层：大工艺阶段码（如 TJ_MACH / BJ_PAINT）；⚠️ v1.10：BOM↔Routing 对接主键之二；必须取自 StageDict（字段文档 §1.9）
    StandardTime,         -- 标准工时（分钟）
    SetupTime,
    IsActive,
    SourceSystem          -- v1.16新增（v5.0.24）：追溯增强字段，非运行必需；'MES'（当前唯一来源）；未来EAM上线时扩充；与 MES_BOM_View.SourceSystem 模式对齐
FROM ...;  -- 3号位负责梳理28张离散工艺表（老结构由3号位ETL处理为MES_ID）
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping(Source='MES', SourceID=MES_ID) → MaterialId
```

**视图2：工序依赖边**（v5.0.1更新 2026-04-02：MaterialCode → MES_ID + Model）
```sql
CREATE VIEW MES_APS_Routing_Dependency_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,
    PathId,
    FromOperationCode,    -- 前置工序
    ToOperationCode,      -- 后置工序
    DependencyType,       -- ES(End-to-Start)/SS(Start-to-Start)
    LagTime,             -- 等待时间（分钟）
    SourceSystem          -- v1.16新增（v5.0.24）：追溯增强字段，非运行必需；'MES'；与 MES_APS_Routing_Operation_View 对齐
FROM ...;  -- 3号位负责（老结构由3号位ETL处理为MES_ID）
-- ⚠️ APS侧装载：同上，2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

**视图3：工序资源能力（替代原ResourceGroupCode）**（v5.0.1更新 2026-04-02：MaterialCode → MES_ID + Model）
```sql
CREATE VIEW APS_OperationResourceEligibility_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,
    PathId,
    OperationCode,
    ResourceCode,         -- 可执行该工序的资源编码
    Priority,             -- 优先级(1=首选)
    CapacityFactor        -- 该资源执行该工序的产能系数
FROM ...;  -- 3号位负责（老结构由3号位ETL处理为MES_ID）
-- ⚠️ APS侧装载：同上，2号位通过 MES_ID 关联 MaterialMapping → MaterialId
```

**视图4：大工艺阶段（阶段字典）**（v1.7新增，v1.8定位调整，v1.10 删除 StageSeq + 补 StageDict 映射责任）
```sql
CREATE VIEW MES_APS_Routing_Stage_View AS
SELECT 
    MES_ID,               -- MES物料主键（INT NOT NULL）
    Model,                -- MES物料型号（NVARCHAR）
    RouteCode,            -- 默认'DEFAULT'
    PathId,               -- 默认1
    StageCode,            -- ⚠️ v1.10：大工艺阶段码，必须已映射标准化为 StageDict 值（如 TJ_MACH/BJ_PAINT）；MES 本地叫法 → StageDict 的映射责任在本视图层完成
    StageName,            -- 阶段中文名（如机加/外协/涂装）
    -- v1.10 删除：StageSeq（跨物料/跨根产品语境下 MES 给不出正确值；权威唯一信 StageDetail.StageSeq）
    IsOutsource,          -- 是否外协阶段
    IsStockPoint,         -- 是否半成品库存断点
    IsActive
FROM ...;  -- 3号位负责（MES原始大工艺阶段数据 + StageDict 标准化映射）
-- ⚠️ APS侧装载：2号位通过 MES_ID 关联 MaterialMapping → MaterialId → RoutingStage
-- ⚠️ v1.10：契约视图层禁止直接输出 MES 原生字符串；必须已完成到 StageDict 的映射
```

**⚠️ v1.10 关键设计决策 — 工艺数据三层模型**（2026-04-25，见字段文档 §1.9b/§1.9c）：

| 层 | 字段 | 粒度 | 值域举例 | 是否参与 BOM↔Routing 对接 |
|---|---|---|---|---|
| 第 1 层：具体工序 | `OperationCode` / `OperationName` | 执行粒度 | NC / MC / 切断 / 精修 | ❌ 仅在 Routing 侧内部按 RoutingDependency 串联 |
| 第 2 层：辅助分类 | `ProcessType`（值域见 `ProcessTypeDict` 骨架）| 报表粒度 | MACHINING / ASSEMBLY | ❌ **完全不参与**；仅统计/粗分组 |
| 第 3 层：大工艺 | `StageCode`（值域权威在 `StageDict`）| 对接粒度 | TJ_MACH / BJ_PAINT | ✅ **BOM↔Routing 对接主键之二** |

**三层硬红线**（不可违反）：
- ❌ 禁止把 `OperationName` 值塞进 `ProcessType`（"NC" 不能当 ProcessType）
- ❌ 禁止把 `ProcessType` 当 `StageCode`（"MACHINING" 不能当 StageCode）
- ❌ 禁止把 `StageCode` 当 `OperationCode`（"TJ_MACH" 不是小工序）

**BOM↔Routing 对接主键（业务口径 vs 物理实现）**：

| 视角 | 主键 | 使用场景 |
|---|---|---|
| **业务口径主键** | `(MaterialCode, StageCode)` | 跨文档/跨号位沟通、经验库规则、日志/异常/Issues 登记（人可读）|
| **物理实现主键** | `(MaterialId, StageCode)` | 数据库表 JOIN/索引/约束；`MaterialId` 是 `MaterialCode` 经 `MaterialMapping` 得到的标准化代理键 |
| **等价性** | 二者等价，不是两套键 | 详见字段文档 §1.9c |

**RoutingStage / StageDetail / StageLeadTimeParam 职责分离**：
- `RoutingStage` = **阶段字典**（该物料在哪些大工艺阶段存在配置；v1.10 起**不承载任何顺序信息**）
  - 已知限制：MES工艺侧不包含外协阶段，数据可能不完整
- `MES_APS_BOM_Workset_StageDetail` = **排程权威阶段顺序源**（5号位基于ERP BOM辅助字段推导的完整阶段链；**`StageSeq` 权威唯一此处**）
- `StageLeadTimeParam` = **外协阶段参数化提前期**（1号位对无 RoutingOperation 的 StageCode 查此表生成标准 Task）
- `StageCode` 值域**三表统一**：`RoutingOperation.StageCode` / `RoutingStage.StageCode` / `StageDetail.StageCode` 必须均取自 `StageDict`

#### 2.1.4 库存视图（v_APS_Inventory）

```sql
CREATE VIEW v_APS_Inventory AS
SELECT 
    MaterialCode,
    FactoryCode,
    OnHandQty,
    AllocatedQty,
    (OnHandQty - AllocatedQty) AS AvailableQty,
    LastUpdatedAt
FROM ERP.dbo.Inventory
WHERE OnHandQty > 0 OR AllocatedQty > 0;
```

#### 2.1.5 承诺交期回写表（t_APS_PromisedDate）

```sql
-- ERP侧创建表（APS写入）
CREATE TABLE t_APS_PromisedDate (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    SourceOrderId NVARCHAR(100) NOT NULL,
    PromisedDate DATE NOT NULL,
    PlanVersionCode NVARCHAR(50) NOT NULL,
    UpdatedBy NVARCHAR(100) NOT NULL DEFAULT 'APS',
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    SyncStatus NVARCHAR(50) NOT NULL DEFAULT 'PENDING', -- PENDING, SYNCED, FAILED
    CONSTRAINT UQ_APS_PromisedDate UNIQUE (SourceOrderId, PlanVersionCode)
);

-- ERP侧定时任务读取此表，更新到SalesOrder表
```

#### 2.1.6 管道供给契约视图（ERP_InterplantInTransit_View）（v1.19 2026-06-15 升级为14字段完整契约）

**ODS层契约视图**（5号位负责）：

| 属性 | 内容 |
|------|------|
| 视图名 | `ERP_InterplantInTransit_View` |
| 所属层 | ODS 层 |
| 所属库 | MES_Integration |
| 来源系统 | ERP |
| 维护责任人 | 5号位（ODS 契约实现）；ERP DBA / ERP 业务负责人（来源字段确认） |
| 用途 | 暴露 ERP 厂间物流运输在途数据，当前为管道供给链的唯一 ODS 来源 |
| V1状态 | WHERE 1=0 返回 0 行的空契约骨架 |
| V1.1/V2计划 | 5号位替换内部 SELECT 和 ERP 源表 JOIN，不改变对外契约 |

**链路**：ERP 源系统厂间在途数据 → **ODS.`ERP_InterplantInTransit_View`**（ODS层/MES_Integration/5号位）→ **APS.`ext_ERP_InterplantInTransit_View`**（APS单来源包装/2号位）→ **APS.`ext_PipelineSupply_Source_View`**（多来源UNION ALL统一输入视图，V1已建立/2号位；15列）→ `sp_SyncPipelineSupply` → `SupplyFact_Pipeline` → `ScheduleContext.PipelineSupplies`

**⚠️ 契约锁定规则**：
ODS 契约视图字段结构为强契约。V1.1/V2 启用真实数据时，**仅允许**替换视图内部的 FROM 和 JOIN 逻辑，**禁止修改**字段顺序、字段类型、字段名称。

**字段契约（14字段，最终契约）**：

| 字段名 | 类型 | 是否必须 | 业务说明 |
|--------|------|---------|---------|
| MasterID | INT | 实际数据必须 | ERP物料主数据物理ID（→ MaterialMapping.SourceID → MaterialId） |
| MaterialCode | NVARCHAR(100) | 建议必须 | ERP物料编码（业务追溯；不替代 MasterID 权威映射） |
| SourceFactoryCode | NVARCHAR(50) | 可空 | 发出工厂编码（仅物流追溯） |
| FactoryCode | NVARCHAR(50) | 实际数据必须 | 目的工厂/收货工厂（→ SupplyFact_Pipeline.FactoryId） |
| SupplyType | NVARCHAR(50) | 必须 | 本视图固定为 INTERPLANT_IN_TRANSIT |
| OwnershipType | NVARCHAR(20) | 必须 | 厂间在途默认 OWNED |
| QualityStatus | NVARCHAR(20) | 必须 | 默认 AVAILABLE |
| Quantity | DECIMAL(18,4) | 必须 | **剩余在途数量**（非原始发货数量） |
| ETA | DATETIME2 | 可空 | ERP原始预计到达时间；**ODS不得加入APS提前期偏移** |
| StorageCode | NVARCHAR(50) | 可空 | 目的仓库编码 |
| SupplierCode | NVARCHAR(50) | 可空 | 厂间在途可为空；采购在途/VMI扩展预留 |
| SourceDocumentNo | NVARCHAR(100) | 建议必须 | ERP来源单据号 |
| SourceDocumentLineNo | NVARCHAR(50) | 可空 | ERP来源单据行号 |
| SourceUpdatedAt | DATETIME2 | 可空 | ERP来源更新时间（增量同步/新鲜度检查） |

**字段语义红线**：
```text
FactoryCode = 目的工厂 / 收货工厂 / 可使用该供给的工厂；
SourceFactoryCode = 发出工厂。

Quantity = 当前仍在途、尚未收货的剩余数量；
不得直接使用原始发货数量导致重复供给。

ETA = ERP原始事实；
AvailableTime 不允许在 ODS 中计算；
AvailableTime 由 APS 的 sp_SyncPipelineSupply 根据
ETA + SupplyAvailabilityRule.LeadTimeOffset 计算。
```

**视图行范围**（未来接入真实数据后）：
```sql
-- 仅输出：尚未全部收货 / 尚未关闭 / 尚未取消 / 剩余数量>0 / 能明确目的工厂 / 有效厂间调拨数据
WHERE 收货状态 NOT IN ('全部收货', '已关闭', '已取消') AND 剩余数量 > 0
```

**APS层包装视图**（2号位负责）：

| 属性 | 内容 |
|------|------|
| 视图名 | `ext_ERP_InterplantInTransit_View` |
| 所属层 | APS 层 |
| 所属库 | APS_Production |
| 来源 | MES_Integration.dbo.ERP_InterplantInTransit_View |
| 维护责任人 | 2号位 |

```sql
-- APS 跨库包装视图（⚠️ 禁止 SELECT *，必须显式列字段）
CREATE OR ALTER VIEW dbo.ext_ERP_InterplantInTransit_View
AS
SELECT
    MasterID,
    MaterialCode,
    SourceFactoryCode,
    FactoryCode,
    SupplyType,
    OwnershipType,
    QualityStatus,
    Quantity,
    ETA,
    StorageCode,
    SupplierCode,
    SourceDocumentNo,
    SourceDocumentLineNo,
    SourceUpdatedAt
FROM [MES_Integration].[dbo].[ERP_InterplantInTransit_View];
```

**⚠️ 重要说明**：
- `ETA` 是 ERP 侧原始事实字段，APS 不修改
- `AvailableTime` 在 `sp_SyncPipelineSupply` 装载时本地计算（`ETA + SupplyAvailabilityRule.LeadTimeOffset`），不在 ODS 视图提供
- 未来新增供给类型（VMI_ONSITE / PURCHASE_IN_TRANSIT 等），只需新增对应契约视图，不改此视图
- V1 空链路预留：ODS 视图返回 0 行，APS 包装视图同步返回 0 行
- 字段契约升级必须通过显式改列，禁止 `SELECT *` 静默传播字段变化

---

### 2.2 增量同步机制（时间戳轮询）（2026-04-08 审计修正）

> ⚠️ **2026-04-08修正**：原§2.2为CDC（变更数据捕获）配置，已废弃。ERP实际环境未启用CDC，改为基于`UpdatedAt`时间戳的增量轮询，与防腐层文档v1.3 §2.2.1对齐。

#### 2.2.1 增量检测原理

**时间戳轮询**：APS侧维护`SyncCheckpoint`表记录上次同步时间，每次查询 `ext_v_APS_SalesOrder WHERE UpdatedAt >= @lastSyncTime` 获取增量变更。

**优势**：
- 无需ERP侧启用CDC，对ERP零侵入
- 通过ODS的`ext_`包装视图间接访问，遵守Socket-Plug红线（2号位不直连源系统）
- 与防腐层文档v1.3 §2.2.1完全一致，无实现分歧

**局限**：
- 物理删除无法感知（ERP侧应使用逻辑删除/状态标记，由`sp_ValidateAndPromoteOrders`检测源端消失）
- 时间戳精度依赖ERP侧`UpdatedAt`字段的更新纪律

#### 2.2.2 ODS侧订单包装视图（ext_v_APS_SalesOrder）

```sql
-- ODS库（MES_Integration），5号位负责创建
-- 跨库包装视图，封装ERP→ODS的物理链路，遵循Socket-Plug模式
CREATE VIEW ext_v_APS_SalesOrder AS
SELECT *
FROM ERP_Production.dbo.v_APS_SalesOrder;
-- ⚠️ 以上为同实例跨库场景。若跨实例，改用 LinkedServer 或 Synonym
-- ⚠️ 契约承诺：列名和数据类型随 v_APS_SalesOrder 契约视图版本锁定
```

#### 2.2.3 同步检查点表（SyncCheckpoint）

```sql
-- APS本地库，2号位负责
CREATE TABLE SyncCheckpoint (
    Id INT PRIMARY KEY IDENTITY(1,1),
    SyncType NVARCHAR(50) NOT NULL,      -- 'ERP_Order', 'ERP_Inventory', ...
    CheckpointTime DATETIME2 NOT NULL,   -- 本次同步截止时间
    RowCount INT NOT NULL DEFAULT 0,     -- 本次拉取行数
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
CREATE INDEX IX_SyncCheckpoint_Type ON SyncCheckpoint(SyncType, CheckpointTime DESC);
```

### 2.3 ERP Adapter实现（APS侧）

#### 2.3.1 订单同步服务（2026-04-08 集成接口审计修正）

> ⚠️ **2026-04-08修正**（覆盖2026-04-03版本）：
> - CDC依赖废弃 → 改为`UpdatedAt`时间戳轮询（M1/M2）
> - 直连ERP → 改为通过ODS `ext_v_APS_SalesOrder`包装视图（M4 Socket-Plug红线）
> - EF Core `ApsDbContext` → 改为Dapper `SqlConnection`（M6 技术栈对齐）
> - 同步路径不变：`ERP_Order_Staging`（PENDING）→ `sp_ValidateAndPromoteOrders` → `Order_Canonical`

```csharp
public class ERPOrderSyncService
{
    private readonly ILogger<ERPOrderSyncService> _logger;
    private readonly string _odsConnectionString;           // ⚠️ ODS库，非ERP库（Socket-Plug红线）
    private readonly string _apsConnectionString;           // APS本地库
    private readonly IOrderPromotionService _promotionService;

    // 全量同步（每日凌晨执行）
    public async Task FullSyncAsync()
    {
        // ⚠️ 通过ODS的ext_包装视图间接访问ERP契约视图（不直连ERP）
        using var odsConn = new SqlConnection(_odsConnectionString);
        var orders = await odsConn.QueryAsync<ERPOrderDto>(@"
            SELECT * FROM ext_v_APS_SalesOrder
            WHERE UpdatedAt >= DATEADD(DAY, -90, GETDATE())
        ");

        using var apsConn = new SqlConnection(_apsConnectionString);
        foreach (var order in orders)
        {
            await WriteToStagingAsync(apsConn, order);
        }

        // 批量写入Staging后，统一执行验证与提升
        await _promotionService.ValidateAndPromoteAsync(); // 调用 sp_ValidateAndPromoteOrders

        _logger.LogInformation("Full sync completed: {Count} orders written to Staging", orders.Count());
    }

    // 增量同步（每小时执行，与防腐层文档v1.3 §2.2.1对齐）
    public async Task IncrementalSyncAsync()
    {
        // ⚠️ 时间戳轮询，从SyncCheckpoint取上次同步时间
        var lastSyncTime = await GetLastSyncCheckpointAsync();

        using var odsConn = new SqlConnection(_odsConnectionString);
        var orders = await odsConn.QueryAsync<ERPOrderDto>(@"
            SELECT * FROM ext_v_APS_SalesOrder
            WHERE UpdatedAt >= @LastSyncTime
        ", new { LastSyncTime = lastSyncTime });

        if (!orders.Any())
        {
            _logger.LogDebug("No incremental changes since {LastSync}", lastSyncTime);
            return;
        }

        using var apsConn = new SqlConnection(_apsConnectionString);
        foreach (var order in orders)
        {
            await WriteToStagingAsync(apsConn, order);
        }

        // 增量写入Staging后，统一执行验证与提升
        await _promotionService.ValidateAndPromoteAsync();

        // 更新同步检查点
        await UpdateSyncCheckpointAsync(DateTime.Now, orders.Count());

        _logger.LogInformation("Incremental sync completed: {Count} changes since {LastSync}",
            orders.Count(), lastSyncTime);
    }

    // Upsert到ERP_Order_Staging（Dapper + MERGE）
    // ⚠️ 2026-04-09 v1.4：补充TransportMode/CustomerName/MTS_InstructionNo源事实字段
    private async Task WriteToStagingAsync(SqlConnection apsConn, ERPOrderDto o)
    {
        await apsConn.ExecuteAsync(@"
            MERGE ERP_Order_Staging AS target
            USING (SELECT @SourceOrderId AS SourceOrderId, @SourceSystem AS SourceSystem) AS source
            ON target.SourceOrderId = source.SourceOrderId
               AND target.SourceSystem = source.SourceSystem
            WHEN MATCHED THEN UPDATE SET
                OrderNo = @OrderNo, OrderType = @OrderType, MaterialCode = @MaterialCode,
                Quantity = @Quantity, DueDate = @DueDate, Priority = @Priority,
                TransportMode = @TransportMode, CustomerName = @CustomerName,
                MTS_InstructionNo = @MTS_InstructionNo,
                IssueDate = @IssueDate, OriginalDueDate = @OriginalDueDate,
                ReceivedQty = @ReceivedQty,
                RawData = @RawData, SyncStatus = 'PENDING', SyncedAt = GETDATE()
            WHEN NOT MATCHED THEN INSERT
                (SourceOrderId, OrderNo, OrderType, MaterialCode, FactoryCode,
                 Quantity, UOM, DueDate, Priority, BOMNO, SourceSystem, SourceMasterID,
                 TransportMode, CustomerName, MTS_InstructionNo,
                 IssueDate, OriginalDueDate, ReceivedQty,
                 RawData, SyncStatus, SyncedAt)
            VALUES
                (@SourceOrderId, @OrderNo, @OrderType, @MaterialCode, @FactoryCode,
                 @Quantity, @UOM, @DueDate, @Priority, @BOMNO, @SourceSystem, @SourceMasterID,
                 @TransportMode, @CustomerName, @MTS_InstructionNo,
                 @IssueDate, @OriginalDueDate, @ReceivedQty,
                 @RawData, 'PENDING', GETDATE());
        ", new {
            o.SourceOrderId, o.OrderNo, o.OrderType, o.MaterialCode, o.FactoryCode,
            o.Quantity, o.UOM, DueDate = o.DueDate, o.Priority,
            o.BOMNO, o.SourceSystem, o.SourceMasterID,
            o.TransportMode, o.CustomerName, o.MTS_InstructionNo,
            o.IssueDate, o.OriginalDueDate, o.ReceivedQty,
            RawData = JsonSerializer.Serialize(o)
        });
        // 后续由 sp_ValidateAndPromoteOrders 统一校验并提升到 Order_Canonical
        // ⚠️ v1.14：校验失败时 SP 写入 FailureCode（原因维度）+ NextActionCode（动作维度），两个独立字段；
        //          SyncStatus=FAILED 仅表达技术流转；BOMNO 可为 NULL（NULL=待5号位Workset解析BOM入口）
    }

    private async Task<DateTime> GetLastSyncCheckpointAsync()
    {
        using var apsConn = new SqlConnection(_apsConnectionString);
        return await apsConn.QuerySingleOrDefaultAsync<DateTime?>(
            "SELECT MAX(CheckpointTime) FROM SyncCheckpoint WHERE SyncType = 'ERP_Order'"
        ) ?? DateTime.Today.AddHours(-1);
    }

    private async Task UpdateSyncCheckpointAsync(DateTime checkpointTime, int rowCount)
    {
        using var apsConn = new SqlConnection(_apsConnectionString);
        await apsConn.ExecuteAsync(@"
            INSERT INTO SyncCheckpoint (SyncType, CheckpointTime, RowCount, CreatedAt)
            VALUES ('ERP_Order', @CheckpointTime, @RowCount, GETDATE())
        ", new { CheckpointTime = checkpointTime, RowCount = rowCount });
    }
}
```

#### 2.3.2 承诺交期回写服务（2026-04-08 审计修正）

> ⚠️ **2026-04-08修正**：原SQL使用PostgreSQL的`ON CONFLICT`语法，SQL Server不支持，改为`MERGE`。

```csharp
public class ERPPromisedDateWritebackService
{
    private readonly string _erpConnectionString; // ⚠️ 回写场景：APS→ERP，需直连ERP写入表

    public async Task WritebackPromisedDateAsync(
        string sourceOrderId, 
        DateTime promisedDate, 
        string planVersionCode)
    {
        using var erpConn = new SqlConnection(_erpConnectionString);

        var sql = @"
            MERGE t_APS_PromisedDate AS target
            USING (SELECT @SourceOrderId AS SourceOrderId, 
                          @PlanVersionCode AS PlanVersionCode) AS source
            ON target.SourceOrderId = source.SourceOrderId
               AND target.PlanVersionCode = source.PlanVersionCode
            WHEN MATCHED THEN UPDATE SET
                PromisedDate = @PromisedDate,
                UpdatedAt = GETDATE(),
                SyncStatus = 'PENDING'
            WHEN NOT MATCHED THEN INSERT
                (SourceOrderId, PromisedDate, PlanVersionCode, UpdatedBy, UpdatedAt, SyncStatus)
            VALUES
                (@SourceOrderId, @PromisedDate, @PlanVersionCode, 'APS', GETDATE(), 'PENDING');
        ";

        await erpConn.ExecuteAsync(sql, new 
        { 
            SourceOrderId = sourceOrderId, 
            PromisedDate = promisedDate, 
            PlanVersionCode = planVersionCode 
        });
    }
}
```

---

## 三、MES 系统集成接口设计

### 3.1 MES集成架构概览

**MES系统类型**：自研MES  
**集成模式**：双通道（MQ消息队列 + REST API）  
**数据方向**：双向

```
┌─────────────────────────────────────────────────────────────┐
│                        APS System                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                  MES Adapter                          │   │
│  │  ┌──────────────────┐    ┌──────────────────┐       │   │
│  │  │  MQ Consumer     │    │  REST Client     │       │   │
│  │  │  (实绩回写)      │    │  (计划下发/      │       │   │
│  │  │                  │    │   工艺路线)      │       │   │
│  │  └──────────────────┘    └──────────────────┘       │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              MES_Actual_Staging                       │   │
│  │  (实绩数据暂存表，含开工/完工/报废/暂停)             │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
         ↑ MQ消息推送              ↓ REST API调用
         ↑ REST API轮询            ↑ REST API回调
┌─────────────────────────────────────────────────────────────┐
│                      MES System (自研)                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │  MQ Producer │  │  REST API    │  │  工艺路线    │     │
│  │  (实绩推送)  │  │  (计划接收)  │  │  数据库      │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 MES侧接口定义（由MES团队提供）

#### 3.2.1 实绩回写接口（MQ消息队列）

**消息队列配置**：
- **MQ类型**：RabbitMQ / Kafka（待MES团队确认）
- **Topic/Queue名称**：`mes.actual.report`
- **消息格式**：JSON
- **推送频率**：实时（开工/完工/报废/暂停事件触发）

**消息结构**：
```json
{
  "messageId": "MSG_20260226_001",
  "messageType": "ACTUAL_REPORT",
  "timestamp": "2026-02-26T10:30:00Z",
  "data": {
    "workOrderNo": "WO202602260001",
    "taskNo": "TASK_001",
    "resourceCode": "MC001",
    "operatorId": "EMP001",
    "eventType": "START|COMPLETE|SCRAP|PAUSE|RESUME",
    "eventTime": "2026-02-26T10:30:00Z",
    "quantity": 100,
    "scrapQuantity": 5,
    "scrapReason": "质量问题",
    "remarks": "正常生产"
  }
}
```

**字段说明**：
- `eventType`：事件类型
  - `START`：开工
  - `COMPLETE`：完工
  - `SCRAP`：报废
  - `PAUSE`：暂停
  - `RESUME`：恢复
  - `RESOURCE_BREAKDOWN`：设备故障
  - `RESOURCE_REPAIRED`：设备修复完成
- `workOrderNo`：MES工单号（对应APS的TaskNo）
- `quantity`：本次报工数量
- `scrapQuantity`：报废数量

**设备故障事件示例**：
```json
{
  "messageId": "MSG_20260226_002",
  "messageType": "ACTUAL_REPORT",
  "timestamp": "2026-02-26T15:00:00Z",
  "data": {
    "resourceCode": "MC001",
    "eventType": "RESOURCE_BREAKDOWN",
    "eventTime": "2026-02-26T15:00:00Z",
    "breakdownType": "MECHANICAL_FAILURE",
    "estimatedRepairTime": 120,
    "breakdownReason": "主轴故障",
    "remarks": "需要更换轴承"
  }
}
```

**设备修复完成事件示例**：
```json
{
  "messageId": "MSG_20260226_003",
  "messageType": "ACTUAL_REPORT",
  "timestamp": "2026-02-26T17:00:00Z",
  "data": {
    "resourceCode": "MC001",
    "eventType": "RESOURCE_REPAIRED",
    "eventTime": "2026-02-26T17:00:00Z",
    "actualRepairTime": 110,
    "remarks": "已更换轴承并测试正常"
  }
}
```

#### 3.2.2 实绩回写接口（REST API轮询，备用通道）

**API端点**：`GET /api/v1/mes/actuals`

**请求参数**：
```json
{
  "startTime": "2026-02-26T00:00:00Z",
  "endTime": "2026-02-26T23:59:59Z",
  "pageIndex": 1,
  "pageSize": 100
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "total": 250,
    "items": [
      {
        "actualId": "ACT_001",
        "workOrderNo": "WO202602260001",
        "taskNo": "TASK_001",
        "resourceCode": "MC001",
        "operatorId": "EMP001",
        "eventType": "COMPLETE",
        "eventTime": "2026-02-26T10:30:00Z",
        "quantity": 100,
        "scrapQuantity": 5,
        "scrapReason": "质量问题"
      }
    ]
  }
}
```

#### 3.2.3 计划下发接口（REST API）

**API端点**：`POST /api/v1/mes/plans`

**请求体**：
```json
{
  "planVersionCode": "PV_20260226_001",
  "tasks": [
    {
      "taskNo": "TASK_001",
      "workOrderNo": "WO202602260001",
      "materialCode": "MAT001",
      "resourceCode": "MC001",
      "plannedStartTime": "2026-02-27T08:00:00Z",
      "plannedEndTime": "2026-02-27T16:00:00Z",
      "quantity": 100,
      "priority": 10,
      "remarks": "紧急订单"
    }
  ]
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "planVersionCode": "PV_20260226_001",
    "receivedCount": 150,
    "acceptedCount": 148,
    "rejectedCount": 2,
    "rejectedTasks": [
      {
        "taskNo": "TASK_099",
        "reason": "资源MC001不存在"
      }
    ]
  }
}
```

#### 3.2.4 工艺路线数据接口（REST API）

**API端点**：`GET /api/v1/mes/routings/{materialCode}`

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "materialCode": "MAT001",
    "routingVersion": "V1.0",
    "effectiveFrom": "2026-01-01",
    "operations": [
      {
        "operationCode": "OP010",
        "processCode": "MACHINING",
        "processName": "机加工",
        "standardTime": 2.5,
        "setupTime": 0.5
      },
      {
        "operationCode": "OP020",
        "processCode": "ASSEMBLY",
        "processName": "装配",
        "standardTime": 1.5,
        "setupTime": 0.2
      }
    ],
    "dependencies": [
      { "fromOperationCode": "OP010", "toOperationCode": "OP020", "dependencyType": "FS", "lagTime": 0.1 }
    ],
    "resourceEligibilities": [
      { "operationCode": "OP010", "resourceCode": "MC-001", "priority": 1, "efficiencyFactor": 1.0 },
      { "operationCode": "OP010", "resourceCode": "MC-002", "priority": 2, "efficiencyFactor": 0.9 },
      { "operationCode": "OP020", "resourceCode": "ASM-001", "priority": 1, "efficiencyFactor": 1.0 }
    ]
  }
}
```

### 3.3 MES Adapter实现（APS侧）

#### 3.3.1 MQ消费者（实绩回写）

```csharp
public class MESActualMQConsumer : BackgroundService
{
    private readonly ILogger<MESActualMQConsumer> _logger;
    private readonly IConnection _mqConnection;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = _mqConnection.CreateModel();
        channel.QueueDeclare(queue: "mes.actual.report", durable: true, exclusive: false, autoDelete: false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var actualReport = JsonSerializer.Deserialize<MESActualMessage>(message);

                // 写入暂存表（Dapper）
                using var conn = new SqlConnection(_apsConnectionString);
                await conn.ExecuteAsync(@"
                    INSERT INTO MES_Actual_Staging 
                        (MessageId, WorkOrderNo, TaskNo, ResourceCode, OperatorId,
                         EventType, EventTime, Quantity, ScrapQuantity, ScrapReason,
                         Remarks, ReceivedAt, Status)
                    VALUES 
                        (@MessageId, @WorkOrderNo, @TaskNo, @ResourceCode, @OperatorId,
                         @EventType, @EventTime, @Quantity, @ScrapQuantity, @ScrapReason,
                         @Remarks, GETDATE(), 'NEW')
                ", actualReport.Data);

                // 确认消息
                channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);

                _logger.LogInformation($"MES actual received: {actualReport.MessageId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MES actual message");
                // 拒绝消息，重新入队
                channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        channel.BasicConsume(queue: "mes.actual.report", autoAck: false, consumer: consumer);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

#### 3.3.2 REST API轮询服务（备用通道）

```csharp
public class MESActualPollingService
{
    private readonly ILogger<MESActualPollingService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    // 每5分钟轮询一次（作为MQ的备用通道）
    [RecurringJob("*/5 * * * *")]
    public async Task PollActualsAsync()
    {
        var lastPollTime = await GetLastPollTimeAsync();
        var now = DateTime.Now;

        var response = await _httpClient.GetAsync($"/api/v1/mes/actuals?startTime={lastPollTime:O}&endTime={now:O}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MESActualResponse>();

        using var conn = new SqlConnection(_apsConnectionString);
        foreach (var actual in result.Data.Items)
        {
            // 去重插入（Dapper + MERGE）
            await conn.ExecuteAsync(@"
                MERGE MES_Actual_Staging AS target
                USING (SELECT @ActualId AS MessageId) AS source
                ON target.MessageId = source.MessageId
                WHEN NOT MATCHED THEN INSERT
                    (MessageId, WorkOrderNo, TaskNo, ResourceCode,
                     EventType, EventTime, Quantity, ScrapQuantity, ReceivedAt, Status, Source)
                VALUES
                    (@ActualId, @WorkOrderNo, @TaskNo, @ResourceCode,
                     @EventType, @EventTime, @Quantity, @ScrapQuantity, GETDATE(), 'NEW', 'POLLING');
            ", actual);
        }

        await UpdateLastPollTimeAsync(now);

        _logger.LogInformation($"MES polling completed: {result.Data.Items.Count} actuals");
    }
}
```

#### 3.3.3 计划下发服务

```csharp
public class MESPlanPublishService
{
    private readonly ILogger<MESPlanPublishService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    public async Task PublishPlanAsync(int planVersionId)
    {
        using var conn = new SqlConnection(_apsConnectionString);

        // 读取计划版本的所有Task（Dapper）
        var tasks = (await conn.QueryAsync<TaskEntity>(
            "SELECT * FROM Task WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId })).ToList();

        var planVersion = await conn.QuerySingleAsync<PlanVersionEntity>(
            "SELECT * FROM PlanVersion WHERE Id = @Id",
            new { Id = planVersionId });

        var request = new MESPlanRequest
        {
            PlanVersionCode = planVersion.VersionCode,
            Tasks = tasks.Select(t => new MESTaskDto
            {
                TaskNo = t.TaskNo,
                WorkOrderNo = t.WorkOrderNo,
                MaterialCode = t.MaterialCode,
                ResourceCode = t.ResourceCode,
                PlannedStartTime = t.PlannedStartTime,
                PlannedEndTime = t.PlannedEndTime,
                Quantity = t.Quantity,
                Priority = t.Priority,
                Remarks = t.Remarks
            }).ToList()
        };

        var response = await _httpClient.PostAsJsonAsync("/api/v1/mes/plans", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MESPlanResponse>();

        // 记录下发结果（Dapper）
        await conn.ExecuteAsync(@"
            INSERT INTO MES_Plan_Publish_Log 
                (PlanVersionId, PublishedAt, TotalCount, AcceptedCount, RejectedCount, RejectedTasks, Status)
            VALUES 
                (@PlanVersionId, GETDATE(), @TotalCount, @AcceptedCount, @RejectedCount, @RejectedTasks, @Status)
        ", new {
            PlanVersionId = planVersionId,
            TotalCount = tasks.Count,
            result.Data.AcceptedCount,
            result.Data.RejectedCount,
            RejectedTasks = JsonSerializer.Serialize(result.Data.RejectedTasks),
            Status = result.Data.RejectedCount > 0 ? "PARTIAL_SUCCESS" : "SUCCESS"
        });

        _logger.LogInformation($"MES plan published: {planVersion.VersionCode}, Accepted: {result.Data.AcceptedCount}, Rejected: {result.Data.RejectedCount}");
    }
}
```

#### 3.3.4 工艺路线同步服务

```csharp
public class MESRoutingSyncService
{
    private readonly ILogger<MESRoutingSyncService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core
    private readonly string _odsConnectionString;           // ODS库（读ext_视图）

    // v5.0更新：每日凌晨通过ext_包装视图同步工艺图模型（3张表）
    // v5.0.1变更（2026-04-02）：ODS视图输出MES_ID+Model，需先映射为MaterialId
    [RecurringJob("0 2 * * *")]
    public async Task SyncRoutingGraphAsync()
    {
        using var apsConn = new SqlConnection(_apsConnectionString);
        using var odsConn = new SqlConnection(_odsConnectionString);

        // 0. 预加载MES物料映射：MES_ID → MaterialId（单一真相源，Dapper）
        var mappings = await apsConn.QueryAsync<(string SourceID, int MaterialId)>(
            "SELECT SourceID, MaterialId FROM MaterialMapping WHERE Source = 'MES' AND IsCurrent = 1");
        var mesMaterialMap = mappings.ToDictionary(m => m.SourceID, m => m.MaterialId);

        // 1. 同步工序节点（ext_视图输出MES_ID+Model，需映射MaterialId）
        var rawOperations = (await odsConn.QueryAsync<RoutingOperationDto>(
            "SELECT * FROM ext_MES_APS_Routing_Operation_View")).ToList();
        var operations = rawOperations
            .Where(r => mesMaterialMap.ContainsKey(r.MES_ID))
            .Select(r => r.ToEntity(mesMaterialMap[r.MES_ID]))
            .ToList();
        await UpsertRoutingOperationsAsync(operations);
        _logger.LogInformation($"RoutingOperation synced: {operations.Count} rows (skipped {rawOperations.Count - operations.Count} unmapped)");

        // 2. 同步工序依赖边（同上映射逻辑）
        var rawDependencies = (await odsConn.QueryAsync<RoutingDependencyDto>(
            "SELECT * FROM ext_MES_APS_Routing_Dependency_View")).ToList();
        var dependencies = rawDependencies
            .Where(r => mesMaterialMap.ContainsKey(r.MES_ID))
            .Select(r => r.ToEntity(mesMaterialMap[r.MES_ID]))
            .ToList();
        await UpsertRoutingDependenciesAsync(dependencies);
        _logger.LogInformation($"RoutingDependency synced: {dependencies.Count} rows");

        // 3. 同步工序资源能力（同上映射逻辑）
        var rawEligibilities = (await odsConn.QueryAsync<OperationResourceEligibilityDto>(
            "SELECT * FROM ext_APS_OperationResourceEligibility_View")).ToList();
        var eligibilities = rawEligibilities
            .Where(r => mesMaterialMap.ContainsKey(r.MES_ID))
            .Select(r => r.ToEntity(mesMaterialMap[r.MES_ID]))
            .ToList();
        await UpsertOperationResourceEligibilitiesAsync(eligibilities);
        _logger.LogInformation($"OperationResourceEligibility synced: {eligibilities.Count} rows");
    }
}
```

### 3.4 容错与监控

#### 3.4.1 双通道容错机制

```csharp
public class MESActualProcessingService
{
    // 定时处理暂存表中的实绩数据
    [RecurringJob("*/1 * * * *")] // 每分钟执行一次
    public async Task ProcessStagingActualsAsync()
    {
        var pendingActuals = await _dbContext.MES_Actual_Staging
            .Where(a => a.Status == "NEW")
            .OrderBy(a => a.EventTime)
            .Take(1000)
            .ToListAsync();

        foreach (var actual in pendingActuals)
        {
            try
            {
                // 查找对应的Task
                var task = await _dbContext.Task
                    .FirstOrDefaultAsync(t => t.TaskNo == actual.TaskNo);

                if (task == null)
                {
                    actual.Status = "ERROR";
                    actual.ErrorMessage = $"Task not found: {actual.TaskNo}";
                    continue;
                }

                // 更新Task状态
                switch (actual.EventType)
                {
                    case "START":
                        task.ActualStartTime = actual.EventTime;
                        task.Status = "IN_PROGRESS";
                        break;
                    case "COMPLETE":
                        task.ActualEndTime = actual.EventTime;
                        task.ActualQuantity = actual.Quantity;
                        task.Status = "COMPLETED";
                        break;
                    case "SCRAP":
                        task.ScrapQuantity = (task.ScrapQuantity ?? 0) + actual.ScrapQuantity;
                        break;
                    case "PAUSE":
                        task.SuspendedAt = actual.EventTime;
                        task.SuspendReason = actual.Remarks;
                        task.Status = "SUSPENDED";
                        break;
                    case "RESUME":
                        task.ResumedAt = actual.EventTime;
                        task.Status = "IN_PROGRESS";
                        break;
                    case "RESOURCE_BREAKDOWN":
                        await HandleResourceBreakdownAsync(actual);
                        break;
                    case "RESOURCE_REPAIRED":
                        await HandleResourceRepairedAsync(actual);
                        break;
                }

                actual.Status = "PROCESSED";
                actual.ProcessedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                actual.Status = "ERROR";
                actual.ErrorMessage = ex.Message;
                _logger.LogError(ex, $"Error processing MES actual: {actual.MessageId}");
            }
        }

        await _dbContext.SaveChangesAsync();
    }
}
```

#### 3.4.2 监控指标

| 指标 | 说明 | 告警阈值 |
|------|------|---------|
| **MQ消息积压** | MQ队列中未消费消息数 | >1000 |
| **暂存表积压** | Status=NEW的记录数 | >5000 |
| **处理延迟** | ReceivedAt到ProcessedAt的时间差 | >5分钟 |
| **错误率** | Status=ERROR的记录占比 | >5% |
| **MES API可用性** | REST API健康检查 | 连续3次失败 |

---

## 四、采购系统集成接口设计（⚠️ v1.19 重对齐管道供给统一链路）

> ⚠️ **v1.19 架构修正**：V1 旧版将采购在途直接写入 `Inventory` 表（`UPDATE Inventory SET InTransitQty=...`），
> 与 V1 已确立的"**管道供给独立于 `InventoryBalance`**"架构冲突。
> 本节已统一改为通过 `SupplyFact_Pipeline` 承载采购在途管道供给。

### 4.1 采购在途管道供给视图（V1.1/V2 预留）

采购系统在途数据与厂间在途采用**同构管道供给链**：

```text
采购系统在途数据
  → ODS 采购在途契约视图（V1.1/V2 5号位新建）
  → APS 单来源跨库包装视图（2号位）
  → ext_PipelineSupply_Source_View 追加 UNION ALL 分支
  → sp_SyncPipelineSupply 主流程保持不变
  → SupplyFact_Pipeline
  → ScheduleContext.PipelineSupplies
```

V1 状态：
- 采购在途 ODS 契约视图尚未建立（V1.1/V2 预留）
- `ext_PipelineSupply_Source_View` 中采购在途分支当前为 placeholder（WHERE 1=0），V1 返回0行
- `sp_SyncPipelineSupply` V1 不查询任何管道供给视图；只执行 TRUNCATE + SUCCESS 日志。`ext_PipelineSupply_Source_View` 作为统一输入视图已在 V1 建立，但 V1 SP 不实际读取；V1.1/V2 启用真实数据后才开始查询该视图
- `ext_PipelineSupply_Source_View` 内 UNION ALL 所有来源分支；新增采购来源只需追加分支，不改 SP 主流程
- **V1 不将采购数据写入 `InventoryBalance`**；管道供给进入 `SupplyFact_Pipeline`，与现货库存六层链严格独立

### 4.2 未来采购管道供给扩展模式

```sql
-- V1.1/V2：替换 ext_PipelineSupply_Source_View 中采购在途的 placeholder 分支（当前 WHERE 1=0）
-- 不需要重新 CREATE VIEW，只需 ALTER VIEW 将 placeholder 替换为真实 ODS 视图引用
-- 单来源包装视图保持 14 字段；统一输入视图输出 15 列（14 业务字段 + SourceSystem 派生）
SELECT
    MasterID,
    MaterialCode,
    NULL            AS SourceFactoryCode,  -- 采购无发出工厂
    TargetFactoryCode AS FactoryCode,
    'PURCHASE_IN_TRANSIT'  AS SupplyType,
    'OWNED'                AS OwnershipType,
    'AVAILABLE'            AS QualityStatus,
    InTransitQty           AS Quantity,
    PromisedDeliveryDate   AS ETA,
    NULL                   AS StorageCode,   -- ODS须根据收货地推导
    SupplierCode,
    PONo                   AS SourceDocumentNo,
    NULL                   AS SourceDocumentLineNo,
    UpdatedAt              AS SourceUpdatedAt,
    'PROCUREMENT'          AS SourceSystem    -- ⚠️ 第15列：APS 派生来源标识
FROM ext_ERP_PurchaseInTransit_View;  -- APS 跨库包装视图（2号位维护）；对应 ODS 契约视图 ERP_PurchaseInTransit_View（5号位/采购 DBA）
```

⚠️ **采购在途 StorageCode 要求**：
采购在途同样参与 `MasterID + StorageCode → MaterialMapping.Warehouse_Norm` 多仓映射。
若采购系统未提供目的仓库，ODS 须根据采购收货地（如 `TargetFactoryCode` + 收货库位）推导出 `StorageCode`。
不允许在多仓环境下只按 MasterID 降级匹配。

- 统一输入视图（`ext_PipelineSupply_Source_View`）V1 已建立并包含所有 placeholder 分支；
  每个分支输出 15 列（14 业务字段 + SourceSystem 派生），ODS 单来源契约保持 14 字段；
- V1.1/V2 只需 ALTER VIEW 将各 placeholder 分支的 WHERE 1=0 替换为真实 ODS 视图引用；
- `sp_SyncPipelineSupply` 只读统一输入视图，不直接读任何单来源视图；
- **不得再用 `UPDATE Inventory SET InTransitQty` 的旧模式**。

---

## 五、OA审批系统集成接口设计

### 5.1 OA系统API规范（假设）

#### 5.1.1 创建审批流实例

**请求**：
```http
POST https://oa.example.com/api/v1/workflow/instances
Content-Type: application/json
Authorization: Bearer {access_token}

{
  "workflowCode": "APS_FROZEN_CHANGE",
  "title": "冻结区变更审批 - 订单SO202602260001",
  "applicant": {
    "userId": "planner001",
    "userName": "张三"
  },
  "approvers": [
    {
      "level": 1,
      "role": "WORKSHOP_SUPERVISOR",
      "scope": "Factory1"
    },
    {
      "level": 2,
      "role": "FACTORY_MANAGER",
      "scope": "Factory1"
    }
  ],
  "formData": {
    "orderNo": "SO202602260001",
    "changeReason": "客户紧急插单",
    "impactedOrders": ["MTS202602250010", "MTS202602250011"],
    "requestedBy": "planner001",
    "requestedAt": "2026-02-26T10:30:00Z"
  },
  "callbackUrl": "https://aps.example.com/api/v1/approval/callback",
  "timeoutHours": 24,
  "timeoutAction": "AUTO_APPROVE"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "workflowInstanceId": "WF202602260001",
    "status": "PENDING",
    "createdAt": "2026-02-26T10:30:05Z",
    "currentApprover": {
      "userId": "supervisor001",
      "userName": "李四",
      "role": "WORKSHOP_SUPERVISOR"
    }
  }
}
```

#### 5.1.2 查询审批流状态

**请求**：
```http
GET https://oa.example.com/api/v1/workflow/instances/{workflowInstanceId}
Authorization: Bearer {access_token}
```

**响应**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "workflowInstanceId": "WF202602260001",
    "status": "APPROVED",
    "approvalRecords": [
      {
        "level": 1,
        "approverUserId": "supervisor001",
        "approverName": "李四",
        "action": "APPROVE",
        "comment": "同意变更",
        "actionAt": "2026-02-26T11:00:00Z"
      },
      {
        "level": 2,
        "approverUserId": "manager001",
        "approverName": "王五",
        "action": "APPROVE",
        "comment": "同意",
        "actionAt": "2026-02-26T11:30:00Z"
      }
    ],
    "completedAt": "2026-02-26T11:30:05Z"
  }
}
```

#### 5.1.3 审批回调接口（APS侧提供）

**OA系统回调APS**：
```http
POST https://aps.example.com/api/v1/approval/callback
Content-Type: application/json
Authorization: Bearer {callback_token}

{
  "workflowInstanceId": "WF202602260001",
  "status": "APPROVED",
  "finalApprover": {
    "userId": "manager001",
    "userName": "王五"
  },
  "completedAt": "2026-02-26T11:30:05Z"
}
```

### 5.2 OA Adapter实现（APS侧）

```csharp
public class OAApprovalAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly string _apsConnectionString;           // ⚠️ Dapper替代EF Core

    public async Task<string> CreateApprovalAsync(ApprovalInstance instance)
    {
        var request = new
        {
            workflowCode = GetWorkflowCode(instance.FlowId),
            title = $"{instance.FlowId} - {instance.InstanceNo}",
            applicant = new
            {
                userId = instance.RequestedBy,
                userName = await GetUserNameAsync(instance.RequestedBy)
            },
            approvers = await GetApproversAsync(instance.FlowId),
            formData = JsonSerializer.Deserialize<object>(instance.RequestData),
            callbackUrl = _config["OA:CallbackUrl"],
            timeoutHours = 24,
            timeoutAction = "AUTO_APPROVE"
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_config["OA:BaseUrl"]}/api/v1/workflow/instances", 
            request
        );

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OAResponse>();

        // 更新ApprovalInstance表（Dapper）
        using var conn = new SqlConnection(_apsConnectionString);
        await conn.ExecuteAsync(@"
            UPDATE ApprovalInstance SET OAWorkflowId = @OAWorkflowId, Status = 'PENDING'
            WHERE Id = @Id
        ", new { OAWorkflowId = result.Data.WorkflowInstanceId, instance.Id });

        return result.Data.WorkflowInstanceId;
    }

    // 审批回调处理
    [HttpPost("api/v1/approval/callback")]
    public async Task<IActionResult> ApprovalCallback([FromBody] OACallbackDto callback)
    {
        using var conn = new SqlConnection(_apsConnectionString);

        var instance = await conn.QueryFirstOrDefaultAsync<ApprovalInstance>(
            "SELECT * FROM ApprovalInstance WHERE OAWorkflowId = @WorkflowInstanceId",
            new { callback.WorkflowInstanceId });

        if (instance == null)
            return NotFound();

        // 更新审批状态 + 记录审批结果（Dapper，事务）
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            UPDATE ApprovalInstance SET Status = @Status, CompletedAt = @CompletedAt
            WHERE Id = @Id
        ", new { callback.Status, callback.CompletedAt, instance.Id }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO ApprovalRecord (InstanceId, StepSeq, ApproverUserId, ApproverName, Action, ActionAt)
            VALUES (@InstanceId, 999, @UserId, @UserName, @Action, @ActionAt)
        ", new {
            InstanceId = instance.Id,
            callback.FinalApprover.UserId,
            callback.FinalApprover.UserName,
            Action = callback.Status,
            ActionAt = callback.CompletedAt
        }, tx);
        tx.Commit();

        // 触发后续业务逻辑（如重新排程）
        if (callback.Status == "APPROVED")
        {
            await TriggerReschedulingAsync(instance);
        }

        return Ok();
    }
}
```

---

## 六、预警通知集成接口设计

### 6.1 邮件网关API

**请求**：
```http
POST https://mail-gateway.example.com/api/send
Content-Type: application/json
Authorization: Bearer {api_key}

{
  "to": ["planner001@example.com", "supervisor001@example.com"],
  "cc": [],
  "subject": "【APS预警】订单SO202602260001延期风险",
  "body": "<html><body><h3>预警详情</h3><p>订单SO202602260001存在延期风险...</p></body></html>",
  "isHtml": true,
  "priority": "HIGH"
}
```

**响应**：
```json
{
  "code": 200,
  "message": "Email sent successfully",
  "data": {
    "messageId": "MSG202602260001"
  }
}
```

### 6.2 短信网关API

**请求**：
```http
POST https://sms-gateway.example.com/api/send
Content-Type: application/json
Authorization: Bearer {api_key}

{
  "phoneNumbers": ["+86138****0001", "+86139****0002"],
  "content": "【APS预警】订单SO202602260001存在延期风险，请及时处理。",
  "templateCode": "APS_ALERT_001"
}
```

### 6.3 通知Adapter实现

```csharp
public class NotificationAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public async Task SendEmailAsync(AlertInstance alert, List<string> recipients)
    {
        var request = new
        {
            to = recipients,
            subject = $"【APS预警】{alert.AlertMessage}",
            body = GenerateEmailBody(alert),
            isHtml = true,
            priority = alert.Severity == "CRITICAL" ? "HIGH" : "NORMAL"
        };

        var response = await _httpClient.PostAsJsonAsync(
            _config["Email:GatewayUrl"], 
            request
        );

        // 记录发送日志
        await LogNotificationAsync(alert.Id, "EMAIL", recipients, response.IsSuccessStatusCode);
    }

    public async Task SendSMSAsync(AlertInstance alert, List<string> phoneNumbers)
    {
        var request = new
        {
            phoneNumbers = phoneNumbers,
            content = alert.AlertMessage,
            templateCode = "APS_ALERT_001"
        };

        var response = await _httpClient.PostAsJsonAsync(
            _config["SMS:GatewayUrl"], 
            request
        );

        await LogNotificationAsync(alert.Id, "SMS", phoneNumbers, response.IsSuccessStatusCode);
    }
}
```

---

## 七、集成容错与监控

### 7.1 重试策略

```csharp
public class IntegrationRetryPolicy
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    Log.Warning($"Retry {retryCount} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                }
            );
    }
}
```

### 7.2 集成监控指标

| 指标 | 说明 | 告警阈值 |
|------|------|---------|
| **ERP同步延迟** | SyncCheckpoint最后一次同步时间距现在的分钟数 | >65分钟（超过1个周期） |
| **Staging表积压** | ERP_Order_Staging表中SyncStatus='PENDING'的记录数 | >500条 |
| **Staging失败率** | ERP_Order_Staging表中SyncStatus='FAILED'的记录占比 | >5% |
| **OA审批超时率** | 超过24小时未审批的比例 | >20% |
| **邮件发送失败率** | 最近1小时邮件发送失败比例 | >5% |

### 7.3 集成健康检查

```csharp
public class IntegrationHealthCheck : IHealthCheck
{
    private readonly string _erpConnectionString;
    private readonly HttpClient _oaHttpClient;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, bool>();

        // 检查ERP连接
        try
        {
            using var conn = new SqlConnection(_erpConnectionString);
            await conn.OpenAsync(cancellationToken);
            checks["ERP_Connection"] = true;
        }
        catch
        {
            checks["ERP_Connection"] = false;
        }

        // 检查OA API
        try
        {
            var response = await _oaHttpClient.GetAsync("/api/health", cancellationToken);
            checks["OA_API"] = response.IsSuccessStatusCode;
        }
        catch
        {
            checks["OA_API"] = false;
        }

        var allHealthy = checks.Values.All(v => v);
        return allHealthy 
            ? HealthCheckResult.Healthy("All integrations healthy", checks)
            : HealthCheckResult.Degraded("Some integrations unhealthy", data: checks);
    }
}
```

#### 3.4.3 设备故障事件处理

```csharp
public class MESActualProcessingService
{
    // 处理设备故障事件
    private async Task HandleResourceBreakdownAsync(MES_Actual_Staging actual)
    {
        // 1. 查找资源
        var resource = await _dbContext.Resource
            .FirstOrDefaultAsync(r => r.Code == actual.ResourceCode);
        
        if (resource == null)
        {
            _logger.LogWarning($"Resource not found: {actual.ResourceCode}");
            return;
        }
        
        // 2. 更新资源状态
        resource.Status = "DOWN";
        resource.BreakdownStartTime = actual.EventTime;
        resource.EstimatedRepairTime = actual.EstimatedRepairTime;
        resource.BreakdownReason = actual.BreakdownReason;
        
        // 3. 查找该资源上正在执行和计划执行的Task
        var affectedTasks = await _dbContext.Task
            .Where(t => t.ResourceId == resource.Id)
            .Where(t => t.Status == "IN_PROGRESS" || t.Status == "PLANNED")
            .Where(t => t.PlannedStartTime <= DateTime.Now.AddHours(24))
            .ToListAsync();
        
        _logger.LogWarning($"Resource {actual.ResourceCode} breakdown, affected {affectedTasks.Count} tasks");
        
        // 4. 暂停正在执行的Task
        foreach (var task in affectedTasks.Where(t => t.Status == "IN_PROGRESS"))
        {
            task.Status = "SUSPENDED";
            task.SuspendedAt = actual.EventTime;
            task.SuspendReason = $"设备故障: {actual.BreakdownReason}";
        }
        
        await _dbContext.SaveChangesAsync();
        
        // 5. 触发重排程（高优先级）
        await _reschedulingService.TriggerReschedulingAsync(
            domainKey: resource.Factory.ProductFamilyCode,
            reason: "RESOURCE_BREAKDOWN",
            priority: "HIGH",
            affectedResourceCode: actual.ResourceCode
        );
        
        // 6. 发送告警
        await _alertService.SendAlertAsync(new Alert
        {
            Level = "CRITICAL",
            Title = $"设备故障: {actual.ResourceCode}",
            Message = $"故障原因: {actual.BreakdownReason}, 预计修复时间: {actual.EstimatedRepairTime}分钟, 影响 {affectedTasks.Count} 个任务",
            ActionRequired = "已触发紧急重排程"
        });
    }
    
    // 处理设备修复完成事件
    private async Task HandleResourceRepairedAsync(MES_Actual_Staging actual)
    {
        // 1. 查找资源
        var resource = await _dbContext.Resource
            .FirstOrDefaultAsync(r => r.Code == actual.ResourceCode);
        
        if (resource == null)
        {
            _logger.LogWarning($"Resource not found: {actual.ResourceCode}");
            return;
        }
        
        // 2. 更新资源状态
        var actualRepairTime = (int)(actual.EventTime - resource.BreakdownStartTime.Value).TotalMinutes;
        
        resource.Status = "AVAILABLE";
        resource.BreakdownStartTime = null;
        resource.EstimatedRepairTime = null;
        resource.BreakdownReason = null;
        
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation($"Resource {actual.ResourceCode} repaired, actual repair time: {actualRepairTime} minutes");
        
        // 3. 触发重排程（正常优先级）
        await _reschedulingService.TriggerReschedulingAsync(
            domainKey: resource.Factory.ProductFamilyCode,
            reason: "RESOURCE_REPAIRED",
            priority: "NORMAL",
            affectedResourceCode: actual.ResourceCode
        );
        
        // 4. 发送通知
        await _alertService.SendAlertAsync(new Alert
        {
            Level = "INFO",
            Title = $"设备修复完成: {actual.ResourceCode}",
            Message = $"实际修复时间: {actualRepairTime}分钟, 已触发重排程",
            ActionRequired = "无"
        });
    }
}
```

---

## 八、集成接口总结

### 8.1 接口清单

| 接口 | 类型 | 实现方 | 状态 |
|------|------|--------|------|
| v_APS_SalesOrder | 数据库视图 | ERP侧 | 待实现 |
| v_APS_BOM | 数据库视图 | ERP侧 | 待实现 |
| ~~v_APS_Routing~~ | 数据库视图 | ERP侧 | v5.0废弃，拆分为下方3个视图 |
| MES_APS_Routing_Operation_View | ODS视图 | 3号位 | v5.0新增 |
| MES_APS_Routing_Dependency_View | ODS视图 | 3号位 | v5.0新增 |
| APS_OperationResourceEligibility_View | ODS视图 | 3号位 | v5.0新增 |
| MES_APS_Resource_View | ODS视图 | MES DBA | v5.0新增；v1.11 命名统一（原名 `APS_Resource_View`） |
| EAM_APS_Resource_View（预留）| ODS视图 | EAM DBA | 未来 EAM 上线时同构新增 |
| v_APS_Inventory | 数据库视图 | ERP侧 | 待实现 |
| t_APS_PromisedDate | 数据库表 | ERP侧 | 待实现 |
| **ERP_InterplantInTransit_View** | **ODS契约视图** | **5号位（ODS实现）** | **v1.19升级14字段合约；`MasterID`=物料映射主字段，`FactoryCode`=目的工厂，`Quantity`=剩余在途数量；V1 WHERE 1=0返回0行** |
| ext_ERP_ERPOrderSync_CdcWrap | CDC包装视图 | 5号位 | ；（v5.0废弃） |
| ext_v_APS_SalesOrder | ODS包装视图 | 5号位 | v1.3新增（替代CDC） |
| **ext_ERP_InterplantInTransit_View** | **APS跨库包装视图** | **2号位** | **v1.19新增；显式列字段，禁止 SELECT *；V1 同步返回0行** |
| v_APS_PurchaseOrder（⚠️ V1.1/V2 管道供给同构化） | ODS契约视图 | 5号位 + 采购DBA | V1.1/V2 新建；进入ext_PipelineSupply_Source_View UNION ALL → sp_SyncPipelineSupply → SupplyFact_Pipeline；不再UPDATE Inventory |
| OA审批API | REST API | OA系统侧 | 待对接 |
| 审批回调API | REST API | APS侧 | 待实现 |
| 邮件网关API | REST API | 第三方 | 待对接 |
| 短信网关API | REST API | 第三方 | 待对接 |

### 8.2 开发工作量估算

| 模块 | 工作量（人天） | 负责人 |
|------|---------------|--------|
| ERP Adapter开发 | 8 | 全栈C#开发 |
| 采购Adapter开发 | 3 | 全栈C#开发 |
| OA Adapter开发 | 5 | 全栈C#开发 |
| 通知Adapter开发 | 2 | 全栈C#开发 |
| ERP侧视图/表创建 | 3 | ERP团队 |
| 采购侧视图创建 | 1 | 采购系统团队 |
| OA侧API对接 | 3 | OA系统团队 |
| 集成测试 | 5 | 全员 |
| **合计** | **30人天** | - |

---

## 九、跨厂Pegging接口方向（v1.22 2026-06-23 新增）

### ERP Received 按单据汇总接口

| 项目 | 内容 |
|------|------|
| ODS 视图 | `ERP_Received_ByDocument_View`（MES_Integration） |
| APS 包装视图 | `ext_ERP_Received_ByDocument_View`（APS_Production） |
| 粒度 | 工厂+仓库+物料+单据类型+单据号 |
| DocumentType | SHIPPING_INSTRUCTION / PRODUCTION_INSTRUCTION / UNKNOWN |
| 不保留 | 单据行号、SourceDocumentNo、SourceLineNo、当前剩余量 |
| 业务假设 | 出荷指示号未完成时 ReceivedQty 视为可用供给 |
| V1 状态 | 不建APS本地Received快照表 |

### MES_ProcessCode_View.ERPProperty 契约补充

| 项目 | 内容 |
|------|------|
| ODS 视图 | `MES_ProcessCode_View`（MES_Integration） |
| 新增字段 | `ERPProperty NVARCHAR(20)` — v5.0.46 |
| 值域 | M / XC / ZP / BP |
| 来源 | ERP 真实属性，5号位同步透出；不根据 WarehouseRole/ProcessName 推导 |
| 消费方 | 2号位，用于生成物料-工厂 M库判定索引（MaterialCode+FactoryCode→HasMStock） |
| 1号位 | 禁止直接查询 |

### MES_APS_BOM_Workset_CrossFactoryEdge 跨厂边接口

| 项目 | 内容 |
|------|------|
| ODS 表 | `MES_APS_BOM_Workset_CrossFactoryEdge`（MES_Integration） |
| 负责人 | 5号位 |
| 生成 | 基于 StageDetail 按 StageSeq 排序，FromFactoryCode <> ToFactoryCode 时生成 |
| APS 缓存 | `APS_BOM_CROSS_FACTORY_EDGE_RAW`（APS_Production，2号位搬运） |
| 用途 | 供 Pegging 阶段读取跨厂边结构事实；不判断跨厂模式（STAGE_HANDOFF/INTER_FACTORY_ORDER 由2号位+5号位在 Pegging 时裁决） |
| V1 状态 | 只表示结构事实，不生成 Task/ShippingTask |

## 十、规则与参数引擎接口方向（v1.22 2026-06-23 新增）

**定位**：规则参数引擎 API 仅供 APS 内部消费，不作为外部集成接口。以下为 V1 接口方向。

| 接口方向 | 说明 |
|---------|------|
| 查询规则集/参数集/策略包列表 | 获取已发布的规则集、参数集、策略包及版本 |
| 获取当前默认策略包版本 | 按 RunType 查询 `IsDefault=1` 且 `Status=PUBLISHED` 的 StrategyProfileVersion |
| 发布策略包版本 | 3号位将 DRAFT/APPROVED 版本发布为 PUBLISHED |
| 查询指定 ScheduleRun 的策略包版本 | 通过 `ScheduleRun.StrategyProfileVersionId` 读取 → 返回完整 RuleSetVersion + ParameterSetVersion |
| 创建 ScheduleRun 时指定策略包 | 传入 `StrategyProfileVersionId`；如未指定，系统按运行类型自动选择默认 PUBLISHED 版本 |

V1 不展开完整 REST API 明细，不新增审批流闭环接口。

---

**文档结束**
