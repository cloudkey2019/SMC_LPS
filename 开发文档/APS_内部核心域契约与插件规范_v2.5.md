# APS 内部核心域契约与插件规范

**版本**：v2.11  
**日期**：2026-05-13  
**基于**：《APS 核心排产全流程走查 (完整版)》V3.8 +《APS_数据架构与防腐层设计方案 v1.20》+《APS_数据库字段说明文档 v5.0.25》+《BOM_Workset v1.4》+《架构总表 v3.17》  
**技术栈**：C# 10.0 + .NET 6.0 + ASP.NET Core 6.0  
**适用范围**：APS系统内部各号位之间的函数调用契约、凭证结构、数据结构、插件接口规范

**v2.11 更新说明**（2026-05-13 排程运行编排 POCO+读模型 POCO，对齐 DDL v5.0.25 / 总表 v3.17）：
- 🆕 **§20** 新增排程运行编排与读模型类型：`RunType` enum、`ScheduleRunRecord` POCO、`ExplanationFactDraft`（内存）、`ScheduleExplanationFact` POCO、三张读模型 POCO
- 📌 **设计决策写死**：`ExplanationFactDraft` 只在 1号位内存中存在，**禁止直接写 DB**；由 2号位批量落 `ScheduleExplanationFact`

**v2.10 更新说明**（2026-05-13 OrderType重构+DelayStatus新增，对齐 DDL v5.0.24）：
- 🔄 **`ERP_Order_Staging` POCO**：`OrderType` 注释更新（v5.0.24重分类：SALES_ORDER/PRODUCTION_INSTRUCTION）；`DemandMaturityStatus` 注释更新（收窄为PRE_CONFIRMED/FORECAST）；**ADD `DelayStatus` `string?`**（ON_TIME/FIRST_DELAY/REPEATED_DELAY；独立维度，禁止与DemandMaturityStatus混用）
- 🔄 **`BomRequestDetailDto.OrderType` 常见値说明**：更新为重分类后的 `SALES_ORDER`/`PRODUCTION_INSTRUCTION`
- 📌 **设计决策写死**：`DelayStatus` 与 `DemandMaturityStatus` 是两个独立维度，**禁止混用**

**v2.9 更新说明**（2026-05-09 管道供给链，对齐 DDL v5.0.23）：
- 🆕 **`PipelineSupplyItem`** POCO：管道供给条目（对应 `SupplyFact_Pipeline` 表字段）
- 🆕 **`ScheduleContext.PipelineSupplies`**：`IReadOnlyList<PipelineSupplyItem>`，默认空数组（结果为空时不影响现有排程）
- 🆕 新增 §§19 `PipelineSupplyItem` POCO 完整定义（在 §18 后附加）
- 📌 **设计决策写死**：`ETA`=ODS原始事实，1号位禁止修改；`AvailableTime`=本地派生字段；`BatchNo` nullable 支持夜间快照

**v2.8 更新说明**（2026-05-09 BomRequestDetailDto 补 OrderType，对齐 DDL v5.0.22）：
- ✅ `BomRequestDetailDto`：ADD **`OrderType`** `string?`（从 `ERP_Order_Staging.OrderType` 透传；BOMNO=NULL 时 5 号位据此分支选取 BOM 入口规则）
- 📌 **典型值**：`MO`=生产指示（优先 MES 工艺 BOM）/ `SO`=客户订单（取 IsDefaultVersion=1）/ NULL=未知（降级+记 Issues）

**v2.7 更新说明**（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21）：

### POCO 字段升级

- ✅ `ERP_Order_Staging` POCO：`BOMNO` 改 `string?`（可空）；ADD **`FailureCode`** `string?` + **`NextActionCode`** `string?`（两个独立维度，见§18）
- ✅ `BOMWorkset` POCO：ADD **`RequestDetailId`** `long?`（追溯锚点，FK→`MES_API_BOM_Request_Detail.Id`；非业务键，1号位不消费）
- ✅ `BOMWorksetIssue` POCO：ADD **`RequestDetailId`** `long?`（同上）

### 新增 DTO（1 个）

- 🆕 **`BomRequestDetailDto`**：2号位推送BOM展开请求时使用（OrderStagingId/Model/MaterialCode/FactoryCode；BOMNO nullable）

### 接口说明更新

- ✅ §18（新增）：`ERP_Order_Staging` POCO + `BomRequestDetailDto` + `BOMWorkset` POCO 完整定义
- ✅ §4.2 2号位接口：BOM入口解析从2号位移交给5号位；2号位只推送基础字段
- 📌 **设计决策写死**：FailureCode/NextActionCode 两个独立维度，禁止混用；BOM入口解析（有BOMNO/无BOMNO分流）由**5号位Workset处理阶段**负责

**v2.6 更新说明**（2026-05-04 对齐 DDL v5.0.18 / 防腐层 v1.16）：
- ✅ 基于文档引用版本升级（走查 V3.6、防腐层 v1.16、字段说明 v5.0.18）
- ✅ SP 实现状态更新：`sp_EnrichBOMWorkset` / `sp_EnrichBOMWorksetRealtime` 已在 DDL v5.0.18 完整实现（非占位）

---

**v2.5 更新说明**（2026-04-29 生产部门主链注入 + ProcessCodeDict 重定位 + WorkshopCode 全局清理）：

### POCO 字段升级

- ✅ `Resource` POCO：DROP `WorkshopCode` + ADD **`ProductionDepartmentId`** (INT, NOT NULL, FK→ProductionDepartment) + **`SourceProductionDeptCode`** (string?, 审计用)
- ✅ `RoutingOperation` POCO：ADD **`ProductionDepartmentId`** (INT, NOT NULL, FK→ProductionDepartment)
- ✅ `RoutingDependency` POCO：ADD **`ProductionDepartmentId`** (INT, NOT NULL, FK→ProductionDepartment)
- ✅ `OperationResourceEligibility` POCO：ADD **`ProductionDepartmentId`** (INT, NOT NULL, FK→ProductionDepartment)
- ✅ `MaterialSupplyContext` POCO：ADD **`DefaultProductionDepartmentId`** (INT?, FK→ProductionDepartment；与 DefaultProductionDeptCode 双轨)

### 新增 POCO（4 个）

- 🆕 **`ProductionDepartment`** POCO：APS 排程责任部门字典（DeptCode/DeptName/FactoryId/StageCode 单值/IsActive）
- 🆕 **`MaterialStageDeptOverride`** POCO：人工维护表（Model/MaterialCode/StageCode/ProductionDeptCode/Reason；SCD Type 2）
- 🆕 **`MaterialStageDeptContext`** POCO：1 号位排程消费入口（MaterialId/StageCode/DefaultProductionDepartmentId/SourceType ∈ AUTO/MANUAL/MIXED；SCD Type 2）
- 🆕 **`MaterialStageDeptContextIssue`** POCO：降级登记（IssueType ∈ MULTI_DEPT_CONFLICT_FOR_STAGE/MISSING_DEPT_IN_MSC/...；Severity ∈ INFO/WARN/ERROR；ReviewStatus ∈ PENDING/CONFIRMED/IGNORED/FIXED）

### `ProcessCodeDict` POCO（v5.0.16 翻转）

- 🔄 **DROP `LastSyncedAt`** 字段
- 🔄 **RENAME `SourceSystem` → `CodeOrigin`**（CHECK：ERP/MES/MANUAL）
- 🆕 **ADD `StageCode`**（APS 增强列；ProcessCode → StageCode 共享基础映射）
- 🆕 ADD `UpdatedBy`

### 1 号位插件接口契约升级

- 🔄 排程引擎主链取数：`(MaterialId, StageCode) → MaterialStageDeptContext.DefaultProductionDepartmentId → Routing 三件套 (MaterialId, ProductionDepartmentId, StageCode)`
- ❌ **禁止** 1 号位插件直接读 `MaterialSupplyContext` / `ProcessCodeDict` / `MaterialStageDeptOverride`（这些是 2 号位组装 Context 的输入源）
- ❌ **禁止** 跳过 Context 直查 Routing 三件套

### 2 号位 IDataLoader 接口增量

- 🆕 `RebuildMaterialStageDeptContextAsync(TriggerMode mode, string batchNo, ...)` — 调 `sp_RebuildMaterialStageDeptContext` ⚠️ **占位骨架，当前未实现**（底层 SP Step1~6 全 TODO）；接口签名先冻结供调用方编程，实装后行为对齐"三触发模式 + 产出 Context + Issues"
- 🆕 `LoadProductionDepartmentsAsync()` — 加载 `ProductionDepartment` 字典
- 🆕 `LoadMaterialStageDeptOverridesAsync()` — 加载人工维护表（导入侧做 Model→MaterialCode 1:1 检查；1:N 拒收）
- 🔄 `LoadResourcesAsync()` — 内部调 `sp_SyncResourceData`，已升级双字典映射（无对外契约变化，但需注意 `WorkshopCode` 字段从 POCO 删除）

### 5 号位 / 2 号位 共享映射约定（v5.0.16 红线）

`MES_ProcessCode_View.StageCode` 是两边共享的基础映射来源；插件层封装统一查询函数 `GetStageCodeByProcessCodeAsync(string processCode)` 供两个号位调用，**禁止各自实现一份**。

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

**v2.4 更新说明**（2026-04-25 工艺数据三层模型收敛）：
- ✅ `Task` POCO：`ProcessType` 注释重写为"辅助分类标签，不参与排程对接"；**新增 `StageCode` 字段**（第 3 层：BOM↔Routing 对接主键之二；1 号位生成 Task 时必填；取自 `StageDict`）
- ✅ `RoutingOperation` POCO 口径：`ProcessType` 辅助分类 / `StageCode` BOM↔Routing 对接主键 / `OperationCode` 执行粒度——三层互不替换
- ✅ 新增 §17 "工艺数据三层模型与 BOM↔Routing 对接主键"（本契约文档统一口径）
- ✅ 1 号位实现约定：排程读 `StageDetail (MaterialCode, StageCode, StageSeq)` → 去 `RoutingOperation (MaterialId, StageCode)` 找小工序 → 结合 `RoutingDependency` 生成 Task；`StageSeq` 权威唯一信 `StageDetail`（`RoutingStage.StageSeq` 已从 DDL 中删除）
- ✅ R20 跨组织：`StageDetail.StageCode` 采用目标工厂视角（父件 TJ + 指派 BJ → `BJ_MACH`），1 号位按此直接去目标工厂 RoutingOperation 排 Task，Task 自动落在目标工厂产能队列，**无需跨厂翻译**
- ✅ 业务主键 vs 物理主键：业务口径 `(MaterialCode, StageCode)` / 物理 `(MaterialId, StageCode)` 等价；日志/Issues 用 MaterialCode，物理表 JOIN 用 MaterialId

**v2.2 更新说明**（2026-03-23）：
- ✅ 新增库存五层架构相关类型定义（InventorySupplyCandidate、InventoryBalance等）
- ✅ 新增MaterialSupplyContext类型（物料供给上下文，仓库级别）
- ✅ 更新Material类型（新增Spec字段，标记废弃字段）
- ✅ 更新MaterialMapping类型（新增Spec、ERP_Warehouse、MES_Location字段）
- ✅ 更新库存事实表主键定义（使用物理主键）
- ✅ 新增MaterialCode编码规则说明
- ✅ 新增IInventoryFilterRule接口
- ✅ 补充sp_SyncMaterialMapping的MaterialSupplyContext同步逻辑

**v2.1 更新说明**（2026-03-19）：
- ✅ 补充IDataLoader接口定义（2号位数据加载器）
- ✅ 补充Socket-Plug职责分工说明
- ✅ 更新文档引用（基于防腐层设计v1.1）

**v2.0 更新说明**（2026-03-18）：
- ✅ 补充完整的核心数据结构定义（第二章）
- ✅ 补充完整的凭证契约规范（第三章）
- ✅ 补充完整的2号位对外契约（第四章）
- ✅ 补充完整的5号位对外契约（第五章）
- ✅ 补充完整的3号位和1号位对外契约（第六、七章）
- ✅ 新增异常处理规范（第十章）
- ✅ 新增性能要求与SLA（第十一章）

---

## 文档说明

本文档定义了APS系统**内部各号位之间**的完整契约体系，与《应用层API接口规范》共同构成内外部契约的完整体系。

**相关文档**：
- **《APS_各类基础数据分层承接与演变总表_v3》**：数据演进全景图
- **《APS_数据架构与防腐层设计方案_v1.1》**：防腐层设计详解
- **《职责分工变更说明_v3.0_Socket-Plug模式》**：Socket-Plug职责分工
- **《APS_应用层API接口规范_v2.1》**：HTTP API契约

**契约体系**：

| 文档 | 适用范围 | 通信方式 |
|------|---------|---------|
| **《应用层API接口规范》** | 3号位 ↔ 4号位（外部契约） | HTTP RESTful API |
| **《内部核心域契约与插件规范》**（本文档） | 1/2/3/5号位之间（内部契约） | C#函数调用、凭证传递 |

---

## 一、契约总览

### 1.0 IDataLoader接口定义（2号位数据加载器）

**接口说明**：2号位负责实现数据加载器接口，从ODS库和APS库加载数据到内存。

**Socket-Plug职责分工**：
- **契约插座（Socket）**：源系统DBA创建契约视图（ERP_Master_View、MES_Material_View等）
- **数据插头（Plug）**：5号位创建跨库包装视图（ext_ERP_Master_View、ext_MES_Material_View等）
- **数据装载（Loader）**：2号位实现IDataLoader接口，拉取数据到APS库并加载到内存

**接口定义**：

```csharp
namespace APS.Core.Contracts
{
    /// <summary>
    /// 数据加载器接口（2号位负责实现）
    /// 负责从ODS库和APS库加载数据到内存
    /// </summary>
    public interface IDataLoader
    {
        /// <summary>
        /// 加载物料映射数据（支持时间点查询）
        /// </summary>
        /// <param name="asOfDate">时间点（null表示当前有效版本）</param>
        /// <returns>物料映射数组</returns>
        Task<MaterialMapping[]> LoadMaterialMappingAsync(DateTime? asOfDate = null);
        
        /// <summary>
        /// 加载物料主数据（从Material表）
        /// </summary>
        /// <returns>物料数组</returns>
        Task<Material[]> LoadMaterialsAsync();
        
        /// <summary>
        /// 加载工序节点数据（v5.0：从RoutingOperation表，替代原Routing表）
        /// v5.0.1变更（2026-04-02）：ODS视图输出MES_ID+Model，装载时通过MaterialMapping映射为MaterialId
        /// </summary>
        Task<RoutingOperation[]> LoadRoutingOperationsAsync();
        
        /// <summary>
        /// 加载工序依赖边数据（v5.0新增：从RoutingDependency表）
        /// v5.0.1：同上，ODS视图输出MES_ID+Model
        /// </summary>
        Task<RoutingDependency[]> LoadRoutingDependenciesAsync();
        
        /// <summary>
        /// 加载工序资源能力关系（v5.0新增：从OperationResourceEligibility表）
        /// v5.0.1：同上，ODS视图输出MES_ID+Model
        /// </summary>
        Task<OperationResourceEligibility[]> LoadOperationResourceEligibilitiesAsync();
        
        /// <summary>
        /// 加载订单数据（按时间范围）
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>订单数组</returns>
        Task<Order[]> LoadOrdersAsync(DateTime startDate, DateTime endDate);
        
        /// <summary>
        /// 加载BOM数据（按批次号）
        /// </summary>
        /// <param name="batchNo">批次号</param>
        /// <returns>BOM关系数组</returns>
        Task<BOM[]> LoadBOMAsync(string batchNo);
        
        /// <summary>
        /// 加载资源数据（从Resource表）
        /// </summary>
        /// <returns>资源数组</returns>
        Task<Resource[]> LoadResourcesAsync();
        
        /// <summary>
        /// 加载库存数据（从InventoryBalance表）
        /// </summary>
        /// <returns>库存数组</returns>
        Task<InventoryBalance[]> LoadInventoryAsync();
        
        /// <summary>
        /// 同步主数据（调用sp_SyncMaterialMapping）
        /// </summary>
        /// <returns>同步结果</returns>
        Task<SyncResult> SyncMasterDataAsync();
        
        /// <summary>
        /// 同步工艺路线（v5.0：从3个ext_视图分别装载到RoutingOperation/RoutingDependency/OperationResourceEligibility）
        /// </summary>
        /// <returns>同步结果</returns>
        Task<SyncResult> SyncRoutingGraphAsync();
    }
    
    /// <summary>
    /// 同步结果
    /// </summary>
    public class SyncResult
    {
        public bool Success { get; set; }
        public int RecordCount { get; set; }
        public double DurationSeconds { get; set; }
        public string ErrorMessage { get; set; }
    }
}
```

**实现要点**：
- ✅ 使用Dapper或EF Core进行数据访问
- ✅ 支持异步操作，避免阻塞主线程
- ✅ 实现连接池管理，避免连接泄漏
- ✅ 实现超时控制，避免长时间等待
- ✅ 实现异常处理，记录详细日志

---

## 一、契约总览（续）

### 1.1 号位通信拓扑图

```

                     APS 内部通信拓扑图                            
                                                                   
                                                      
    0号位     业务策略配置、架构红线把控                         
   架构师     (不参与编码，制定规则)                            
                                                      
                                                                  
        (配置规则)                                               
         
                3号位：调度编排器 (Orchestrator)                 
    - Hangfire定时任务触发                                       
    - 分域拓扑排序                                               
    - 域调度编排                                                 
         
                                                                
        (调用)                  (调用)                          
                                                                
                                         
    2号位   凭证  5号位                              
   数据基础              业务规则                            
   设施     数据 引擎                                
                                         
                                                                
        (传递数据结构)          (插件接口)                      
                                                                
                                                    
    1号位                                                     
   排程引擎                                  
                                                      
                                                                   
                                                      
    4号位     前端UI (通过HTTP API与3号位通信)                  
   前端开发   (外部契约，见《应用层API接口规范》)               
                                                      

```

### 1.2 契约类型分类

| 契约类型 | 说明 | 示例 | 文档位置 |
|---------|------|------|---------|
| **HTTP API契约** | 3号位与4号位之间的RESTful API | `POST /api/v1/planning/full-schedule` | 《应用层API接口规范》 |
| **函数调用契约** | 号位之间的C#函数调用接口 | `IDataLoader.LoadDomainData(domainKey)` | 本文档 第四~七章 |
| **凭证结构契约** | 5号位返回给2号位的凭证数据结构 | `PeggingVoucher`, `SplitVoucher` | 本文档 第三章 |
| **数据结构契约** | 号位之间传递的核心实体结构 | `Task`, `Resource`, `Order` | 本文档 第二章 |
| **插件接口契约** | 5号位实现的业务规则插件接口 | `IOrderPrioritizationRule` | 本文档 第五章 |

### 1.3 凭证交互模式（核心设计原则）

**⚠️ 架构红线**：5号位绝对禁止直接修改数据，只能返回凭证；2号位根据凭证统一执行状态变更。

```
5号位.业务规则计算 → 返回Voucher凭证 → 2号位.统一执行状态变更 → 沙盘更新
```

**典型应用场景**：
- 库存扣减与Pegging连线
- 订单优先级打分
- 拆批裁决
- 冻结区打标
- 厂间发货Task生成

---

## 二、核心数据结构契约

### 2.1 ScheduleContext（排产沙盘）

**业务用途**：排产沙盘是整个APS系统的核心数据容器，包含了一次完整排产所需的所有数据。

```csharp
/// <summary>
/// 排产沙盘上下文
/// 包含一次完整排产所需的所有数据
/// </summary>
public class ScheduleContext
{
    // ============ 基础标识 ============
    
    /// <summary>
    /// 域标识（如"ProductFamily_A"、"Factory_TJ"）
    /// 用于分域计算的核心标识
    /// </summary>
    public string DomainKey { get; set; }
    
    /// <summary>
    /// 计划版本ID
    /// 对应数据库PlanVersion表的主键
    /// </summary>
    public int PlanVersionId { get; set; }
    
    /// <summary>
    /// 计划期间开始日期
    /// </summary>
    public DateTime PlanHorizonStart { get; set; }
    
    /// <summary>
    /// 计划期间结束日期
    /// </summary>
    public DateTime PlanHorizonEnd { get; set; }
    
    /// <summary>
    /// 当前时间（用于冻结区判定）
    /// </summary>
    public DateTime CurrentTime { get; set; }
    
    // ============ 订单数据 ============
    
    /// <summary>
    /// 独立需求订单列表（MTS生产指示）
    /// 这些订单是排产的起点，需要进行Pegging和拆批
    /// </summary>
    public List<Order> IndependentDemands { get; set; }
    
    /// <summary>
    /// 所有订单列表（包括MTS和MTO）
    /// 包含独立需求和相关需求
    /// </summary>
    public List<Order> Orders { get; set; }
    
    // ============ 任务数据 ============
    
    /// <summary>
    /// 任务列表（排程的基本单位）
    /// 由拆批凭证生成，是1号位排程引擎的输入
    /// </summary>
    public List<Task> Tasks { get; set; }
    
    /// <summary>
    /// 历史锚点任务（已下发MES的冻结Task）
    /// 用于约束排程，确保已下发任务不被修改
    /// </summary>
    public List<Task> HistoricalAnchors { get; set; }
    
    // ============ 资源数据 ============
    
    /// <summary>
    /// 资源日历（设备可用时间）
    /// 包含设备的工作时间、维护时间、停机时间等
    /// </summary>
    public ResourceCalendar ResourceCalendar { get; set; }
    
    /// <summary>
    /// 资源列表（设备、产线、工作中心）
    /// </summary>
    public List<Resource> Resources { get; set; }
    
    // ============ 物料数据 ============
    
    /// <summary>
    /// 物料主数据列表
    /// </summary>
    public List<Material> Materials { get; set; }
    
    /// <summary>
    /// BOM展开结果（物料清单）
    /// 从ODS库拉取的BOM数据
    /// </summary>
    public BOMWorkset BOMData { get; set; }
    
    // ============ 库存数据 ============
    
    /// <summary>
    /// 库存记录列表
    /// 包含ERP库存和MES库存的统一视图
    /// </summary>
    public List<InventoryRecord> Inventories { get; set; }
    
    /// <summary>
    /// 在途库存列表（采购在途、生产在途）
    /// </summary>
    public List<InTransitInventory> InTransitInventories { get; set; }
    
    // ============ 管道供给数据（v5.0.23 新增） ============
    
    /// <summary>
    /// 管道供给列表（在途/外部供给）
    /// 包含厂间在途、VMI等管道供给条目，并行于现货五层主链
    /// ❗ 结果为空时不影响现有排程
    /// ❗ 应由 IDataLoader.LoadPipelineSupplies() 装载，默认空数组
    /// </summary>
    public IReadOnlyList<PipelineSupplyItem> PipelineSupplies { get; set; } = Array.Empty<PipelineSupplyItem>();
    
    // ============ Pegging数据 ============
    
    /// <summary>
    /// Pegging连线关系
    /// 记录需求与供给的匹配关系
    /// </summary>
    public List<PeggingLink> PeggingLinks { get; set; }
    
    /// <summary>
    /// Pegging账本（订单级别的物料需求账本）
    /// 用于拆批裁决时查询物料需求
    /// </summary>
    public Dictionary<long, PeggingLedger> PeggingLedgers { get; set; }
    
    // ============ 配置数据 ============
    
    /// <summary>
    /// 业务规则配置
    /// 包含优先级规则、拆批规则、冻结区规则等
    /// </summary>
    public BusinessRuleConfig RuleConfig { get; set; }
    
    /// <summary>
    /// 排程参数配置
    /// 包含排程算法参数、性能参数等
    /// </summary>
    public SchedulingParameters SchedulingParams { get; set; }
    
    // ============ 运行时数据 ============
    
    /// <summary>
    /// 执行日志（用于调试和审计）
    /// </summary>
    public List<ExecutionLog> Logs { get; set; }
    
    /// <summary>
    /// 性能指标（用于监控和优化）
    /// </summary>
    public PerformanceMetrics Metrics { get; set; }
}
```

**说明**：由于篇幅限制，完整的数据结构定义（包括Task、Order、Resource、PeggingLink等）请参考独立文档 `契约文档/01_核心数据结构契约.md`。

---

## 三、凭证（Voucher）契约规范

### 3.1 凭证设计原则

**⚠️ 核心原则**：5号位绝对禁止直接修改数据，只能返回凭证；2号位根据凭证统一执行状态变更。

**设计理念**：
- **职责分离**：5号位负责"决策"，2号位负责"执行"
- **可追溯性**：所有状态变更都有凭证记录，便于审计和调试
- **可回滚性**：凭证可以被撤销或重做
- **可测试性**：5号位的业务逻辑可以独立测试，不依赖数据库

### 3.2 核心凭证类型

1. **PeggingVoucher**（库存扣减凭证）
2. **SplitVoucher**（拆批凭证）
3. **OrderScoreVoucher**（订单打分凭证）
4. **FrozenMarkVoucher**（冻结区打标凭证）
5. **MaterialShortageVoucher**（物料短缺凭证）
6. **ShippingTaskVoucher**（厂间发货Task凭证）

**说明**：完整的凭证定义（包括VoucherBase基类、6种凭证的完整C#定义、验证方法、使用示例）请参考独立文档 `契约文档/02_凭证契约规范.md`。

---

## 四、2号位对外契约（数据基础设施）

### 4.1 概述

2号位（数据基础设施）是APS系统的"引擎底座"，负责：
- 数据加载与持久化
- 凭证执行与状态变更
- BOM遍历与静态扫描
- 批量操作与性能优化

**核心原则**：2号位只做"苦力活"，不做业务决策。所有业务逻辑由5号位插件决定，2号位负责执行。

### 4.2 核心接口

- **IDataLoader**：数据加载接口
- **IVoucherExecutor**：凭证执行接口
- **IBulkPersistence**：批量持久化接口
- **IBOMTraverser**：BOM遍历引擎接口
- **IStaticScanner**：静态扫描接口

**说明**：完整的接口定义（包括方法签名、参数说明、实现示例、性能优化建议）请参考独立文档 `契约文档/03_2号位对外契约.md`。

---

## 五、5号位对外契约（业务规则引擎）

### 5.1 概述

5号位（业务规则引擎）是APS系统的"大脑"，负责所有业务决策逻辑。

**核心原则**：
- 5号位只做决策，不做执行
- 所有决策以凭证形式返回给2号位
- 5号位必须是无状态的，可插拔的

### 5.2 核心插件接口

- **IOrderPrioritizationRule**：订单打分插件
- **IPeggingRule**：库存扣减策略插件
- **ITaskSplitRule**：拆批裁决插件
- **IFrozenZoneRule**：冻结区判定插件
- **IMaterialShortageRule**：物料短缺识别插件
- **ISetupAttributeRule**：换型属性标注插件
- **ICrossDomainRule**：跨域协同规则插件

**说明**：完整的插件接口定义（包括接口方法、默认实现示例、依赖注入配置）请参考独立文档 `契约文档/04_5号位对外契约.md`。

---

## 六、3号位对外契约（调度编排器）

### 6.1 核心接口

- **IDomainScheduler**：域调度接口
- **ITopologicalSorter**：拓扑排序接口
- **IOrchestrator**：总编排器接口

---

## 七、1号位对外契约（排程引擎）

### 7.1 核心接口

- **ISchedulingEngine**：排程引擎接口
- **SchedulingInput**：排程输入数据结构
- **SchedulingOutput**：排程输出数据结构

**说明**：完整的3号位和1号位接口定义请参考独立文档 `契约文档/05_3号位和1号位对外契约.md`。

---

## 八、契约使用示例

### 8.1 完整排产流程代码契约示例（核心主程序骨架）

以下代码展现了 2号位（引擎底座）如何通过接口调度 5号位（大脑）和 1号位（算法），实现严密的沙盘推演：

```csharp
// =================================================================
// 阶段1：2号位 - O(1)极速加载数据与捞取历史锚点
// =================================================================
var context = await dataLoader.LoadDomainDataAsync(domainKey, planVersionId);

// =================================================================
// 阶段2.0：5号位 - 阵前阅兵与订单纯粹打分
// =================================================================
var scoreVoucher = businessRules.OrderPrioritization
    .CalculateOrderPriority(context.IndependentDemands, context);
// 2号位执行打分凭证（写入内存沙盘）
voucherExecutor.ExecuteOrderScoreVoucher(scoreVoucher, context);

// =================================================================
// 阶段2.1：5号位 & 2号位 - 第一波BOM遍历 + Pegging动态连线
// ⚠️红线：此处只记账（PeggingLedger），绝对不 new Task！
// =================================================================
foreach (var order in context.IndependentDemands.OrderByDescending(o => o.PriorityScore))
{
    var peggingVoucher = businessRules.Pegging.CalculatePegging(order, context);
    voucherExecutor.ExecutePeggingVoucher(peggingVoucher, context);
}

// =================================================================
// 阶段2.5：2号位 & 5号位 - 第N波孤儿单据级联扫尾
// =================================================================
var orphanVoucher = businessRules.Pegging.SweepOrphans(context);
voucherExecutor.ExecutePeggingVoucher(orphanVoucher, context);

// =================================================================
// 阶段2.6：5号位 & 2号位 - 统一拆批与 Task 物理实例化（两阶段生成法落地）
// =================================================================
foreach (var mtsOrder in context.Orders)
{
    // 5号位基于血缘账本与设备负荷，动态裁决拆批策略
    var splitVoucher = businessRules.TaskSplit.CalculateSplit(mtsOrder, mtsOrder.PeggingLedger);
    
    // 2号位当苦力：根据拆批凭证，真正 new 出底层 Task 并放入沙盘，赋予准确分数
    voucherExecutor.ExecuteSplitVoucher(splitVoucher, context);
}

// =================================================================
// 阶段3 & 4：1号位 - 纯数学排程推演（排俄罗斯方块）
// ⚠️红线：1号位只看 Task 上的 IsWip/IsFrozen 标签，不看任何业务逻辑
// =================================================================
var schedulingOutput = schedulingEngine.Schedule(context.Tasks, context.ResourceCalendar);

// =================================================================
// 阶段5.0：5号位 & 2号位 - 落库前的滑动窗口时间审判（下发MES的唯一阀门）
// =================================================================
var frozenVoucher = businessRules.FrozenZone.Evaluate(context.Tasks, currentDate);
// 2号位执行凭证：为滑入极昼区的 Task 打上 IsFrozen = true，准备下发
voucherExecutor.ExecuteFrozenMarkVoucher(frozenVoucher, context);

// =================================================================
// 阶段5.1：2号位 - 极速批量持久化（SqlBulkCopy）
// =================================================================
await persistence.BulkPersistAsync(context);
```

---

## 九、总结

### 9.1 核心价值

本文档定义了APS系统内部各号位之间的完整契约体系，包括：

1. **数据结构契约**：ScheduleContext、Task、Order等核心实体的完整C#定义
2. **凭证结构契约**：PeggingVoucher、SplitVoucher等6种凭证的完整定义
3. **函数调用契约**：IDataLoader、IVoucherExecutor等接口的完整方法签名
4. **插件接口契约**：IOrderPrioritizationRule等7个业务规则插件接口
5. **异常处理规范**：异常分类、处理策略、重试机制
6. **性能要求**：各接口的性能SLA、优化建议、监控指标

### 9.2 与《应用层API接口规范》的关系

| 文档 | 适用范围 | 通信方式 | 核心内容 |
|------|---------|---------|---------|
| **《应用层API接口规范》** | 3号位 ↔ 4号位 | HTTP RESTful API | Planning API、InsertOrder API、Config API等 |
| **《内部核心域契约与插件规范》** | 1/2/3/5号位 | C#函数调用、凭证传递 | 数据结构、凭证、插件接口 |

两者共同构成APS系统的**完整契约体系**，确保：
- **外部契约**：前端与后端的HTTP API通信规范
- **内部契约**：后端各模块之间的函数调用规范

### 9.3 契约变更流程

1. **提出变更**：在团队会议中讨论契约变更需求
2. **影响分析**：评估变更对各号位的影响
3. **版本决策**：决定是否需要创建新版本接口
4. **文档更新**：更新本契约文档
5. **代码实现**：各号位按新契约实现
6. **集成测试**：验证契约变更的正确性

### 9.4 v2.0版本改进总结

相比v1.0版本，v2.0版本进行了以下重大改进：

| 改进项 | v1.0 | v2.0 | 价值 |
|--------|------|------|------|
| **数据结构定义** | 仅列出名称 | 完整C#类定义 + 字段说明 | 可直接用于编码 |
| **凭证定义** | 仅列出类型 | 完整定义 + 验证方法 + 示例 | 可直接实现凭证系统 |
| **接口定义** | 仅列出接口名 | 完整方法签名 + 参数说明 | 可直接实现接口 |
| **异常处理** | 无 | 完整异常分类 + 处理策略 | 提升系统健壮性 |
| **性能要求** | 无 | 详细SLA + 优化建议 | 确保系统性能 |
| **代码示例** | 仅有流程示例 | 每个接口都有实现示例 | 降低开发难度 |

---

## 十、快速导航

### 10.1 按角色查阅

| 角色 | 需要查阅的章节 |
|------|--------------|
| **架构师** | 第一章（契约总览）、第十章（异常处理）、第十一章（性能要求） |
| **2号位开发** | 第二章（数据结构）、第三章（凭证）、第四章（2号位契约） |
| **5号位开发** | 第二章（数据结构）、第三章（凭证）、第五章（5号位契约） |
| **1号位开发** | 第七章（1号位契约） |
| **3号位开发** | 第六章（3号位契约） |
| **测试工程师** | 第八章（使用示例）、第十章（异常处理）、第十一章（性能要求） |

### 10.2 按任务查阅

| 任务 | 需要查阅的章节 |
|------|--------------|
| **实现Pegging逻辑** | 第三章（PeggingVoucher）、第五章（IPeggingRule） |
| **实现拆批逻辑** | 第三章（SplitVoucher）、第五章（ITaskSplitRule） |
| **实现数据加载** | 第二章（ScheduleContext）、第四章（IDataLoader） |
| **实现排程引擎** | 第七章（ISchedulingEngine） |
| **性能优化** | 第十一章（性能要求与SLA） |
| **异常处理** | 第十章（异常处理规范） |

---

## 十、异常处理规范

### 10.1 异常分类

- **业务异常**：VoucherValidationException、DataLoadException、SchedulingFailedException
- **系统异常**：DatabaseConnectionException、TimeoutException

### 10.2 各号位异常处理策略

| 号位 | 异常处理策略 | 重试机制 | 回滚策略 |
|------|------------|---------|---------|
| **1号位** | 抛出异常，不处理 | 不重试 | 无需回滚（无状态） |
| **2号位** | 捕获并记录日志，事务回滚 | 数据库操作重试3次 | 事务自动回滚 |
| **3号位** | 捕获并记录，继续处理其他域 | 失败域重试1次 | 标记失败域 |
| **5号位** | 抛出异常，不处理 | 不重试 | 无需回滚（无状态） |

**说明**：完整的异常处理规范（包括异常类定义、处理代码示例、重试机制、日志规范）请参考独立文档 `契约文档/06_异常处理和性能要求.md`。

---

## 十一、性能要求与SLA

### 11.1 性能SLA摘要

| 接口类型 | 典型数据量 | SLA要求 |
|---------|-----------|---------|
| **数据加载** | 100万行 | < 5秒 |
| **凭证执行** | 单个凭证 | < 100ms |
| **排程计算** | 10万Task | < 60秒 |
| **批量持久化** | 10万行 | < 10秒 |

### 11.2 优化建议

- **数据库优化**：索引优化、分区表、定期维护
- **代码优化**：并行加载、批量操作、避免N+1查询
- **内存优化**：及时释放、流式处理、对象池

**说明**：完整的性能要求与SLA（包括详细性能指标、优化代码示例、监控方案、压力测试要求）请参考独立文档 `契约文档/06_异常处理和性能要求.md`。

---

## 📚 附件文档清单

为便于查阅和维护，本文档的详细内容已拆分为以下独立文件：

| 序号 | 文件名 | 主要内容 | 行数 |
|------|--------|---------|------|
| 1 | `01_核心数据结构契约.md` | ScheduleContext、Task、Order、Resource等7个核心类的完整C#定义 | ~920行 |
| 2 | `02_凭证契约规范.md` | VoucherBase基类、6种凭证的完整定义、验证方法、使用示例 | ~1070行 |
| 3 | `03_2号位对外契约.md` | IDataLoader、IVoucherExecutor等5个接口的完整定义和实现示例 | ~726行 |
| 4 | `04_5号位对外契约.md` | 7个业务规则插件接口的完整定义和默认实现 | ~525行 |
| 5 | `05_3号位和1号位对外契约.md` | 调度编排器和排程引擎接口定义 | ~236行 |
| 6 | `06_异常处理和性能要求.md` | 异常处理规范、性能SLA、监控指标、优化建议 | ~511行 |

**文档位置**：`d:\CascadeProjects\APS\契约文档\`

**总代码量**：约4000行C#代码 + 详细注释和说明

---

## 十二、库存五层架构（v2.2 新增）

### 12.1 架构概述

库存数据采用五层架构，从物理事实层逐步演进到内存消费层：

```
Layer 1: 事实层 (InventoryFact_ERP, InventoryFact_MES)
    ↓ 保留物理主键，MaterialCode为辅助字段
Layer 2: 候选供给池 (InventorySupplyCandidate)
    ↓ 首次统一到MaterialCode，通过MaterialMapping桥接
Layer 3: 规则筛选层 (ProductFamilyInventoryScope, InventorySourceRule)
    ↓ 产品族级别的仓库范围和来源规则
Layer 4: 可用库存 (InventoryBalance)
    ↓ 规则筛选后的排程可用库存
Layer 5: 内存消费层 (ScheduleContext.InventorySupplies)
    ↓ 排程引擎内存中的库存消费
```

### 12.2 核心类型定义

#### InventoryFact_ERP（事实层）

```csharp
public class InventoryFact_ERP
{
    // ⚠️ 主键：MasterID + Warehouse（物理主键）
    public int MasterID { get; set; }              // 主键字段
    public string Warehouse { get; set; }          // 主键字段
    
    // 辅助字段（通过MaterialMapping桥接获得）
    public string MaterialCode { get; set; }       // 非主键
    
    public decimal Quantity { get; set; }
    public string FactoryCode { get; set; }
    public DateTime SnapshotTime { get; set; }
    public bool IsActive { get; set; }
}
```

#### InventoryFact_MES（事实层）

```csharp
public class InventoryFact_MES
{
    // ⚠️ 主键：MES_ID + Location（物理主键）
    public int MES_ID { get; set; }                // 主键字段
    public string Location { get; set; }           // 主键字段
    
    // 辅助字段（通过MaterialMapping桥接获得）
    public string MaterialCode { get; set; }       // 非主键
    
    public decimal Quantity { get; set; }
    public string WarehouseCode { get; set; }
    public string FactoryCode { get; set; }
    public DateTime SnapshotTime { get; set; }
    public bool IsActive { get; set; }
}
```

#### InventorySupplyCandidate（候选供给池）

```csharp
public class InventorySupplyCandidate
{
    // ⚠️ 首次统一到MaterialCode的地方
    public string MaterialCode { get; set; }
    public string SourceSystem { get; set; }       // ERP, MES
    public string WarehouseCode { get; set; }
    public string LocationCode { get; set; }       // MES库位
    public decimal Quantity { get; set; }
    public DateTime SnapshotTime { get; set; }
}
```

#### ProductFamilyInventoryScope（规则筛选层）

```csharp
public class ProductFamilyInventoryScope
{
    public string ProductFamily { get; set; }
    public string WarehouseCode { get; set; }
    public string SourceSystem { get; set; }       // ERP, MES
    public bool IsIncluded { get; set; }
}
```

#### InventorySourceRule（规则筛选层）

```csharp
public class InventorySourceRule
{
    public string ProductFamily { get; set; }
    public string SourceSystem { get; set; }       // ERP, MES
    public int Priority { get; set; }
    public bool IsExcluded { get; set; }
}
```

#### InventoryBalance（可用库存）

```csharp
public class InventoryBalance
{
    public string MaterialCode { get; set; }
    public string WarehouseCode { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public DateTime SnapshotTime { get; set; }
}
```

### 12.3 IInventoryFilterRule接口（5号位实现）

```csharp
namespace APS.BusinessRules
{
    /// <summary>
    /// 库存筛选规则接口（v2.2 新增）
    /// 5号位负责实现
    /// </summary>
    public interface IInventoryFilterRule
    {
        /// <summary>
        /// 判断库存是否应该包含在可用库存中
        /// </summary>
        /// <param name="candidate">候选库存</param>
        /// <param name="productFamily">产品族</param>
        /// <returns>是否包含</returns>
        bool ShouldInclude(InventorySupplyCandidate candidate, string productFamily);
    }
}
```

---

## 十三、MaterialCode 编码规则（v2.2 新增）

### 13.1 编码格式

```
{类型前缀}-{物料型号}-{版本号(可选)}
```

### 13.2 类型前缀

| 前缀 | 含义 | 示例 |
|------|------|------|
| `RAW-` | 原材料 | `RAW-STEEL-10X20` |
| `FG-` | 成品 | `FG-A900` |
| `WIP-` | 半成品 | `WIP-C25ILB-005` |
| `ASSY-` | 装配件 | `ASSY-TEMP01` |

### 13.3 物料型号

取自企业现有 ERP/MES 中业务人员普遍使用、稳定且可读的型号字段。

**示例**：
- `STEEL-10X20`（钢材型号）
- `C25ILB-005`（半成品型号）
- `A900`（成品型号）

### 13.4 版本号使用原则

**默认不启用**，仅在工程变更导致物料业务身份不兼容时启用。

**不加版本号的情况**：
- 图纸小修
- 工艺微调
- 向下兼容的优化
- 不影响库存/BOM/工艺混用的变更

**必须加版本号的情况**：
- 设变后新旧物料不能混用
- 新旧库存必须隔离
- 新旧BOM必须隔离
- 新旧工艺路线必须隔离
- 新旧供给关系或替代关系必须隔离

**示例**：
- `FG-A900-V2`（成品A900第2版，设变后不兼容）
- `WIP-C25ILB-005-V2`（半成品第2版，新旧物料不能混用）

### 13.5 严禁写入MaterialCode的内容

❌ **禁止内容**：
- MTO/MTS（由 `Order_Canonical.OrderType` 承载）
- 订单号（由 `BOMNO` 承载）
- 客户特征（由 Pegging 关系承载）
- 仓库信息（由 `MaterialSupplyContext` 承载）
- 责任部门信息（由 `MaterialSupplyContext` 承载）

---

## 十四、MaterialSupplyContext（v2.2 新增）

### 14.1 设计原理

物料本体属性与仓库级上下文分离，支持一物多仓、一物多供给方式的场景。

### 14.2 类型定义

```csharp
public class MaterialSupplyContext
{
    public string MaterialCode { get; set; }
    public string WarehouseCode { get; set; }
    
    // 供给方式
    public string SupplyMode { get; set; }         // PURCHASE, MAKE, OUTSOURCE
    
    // 生产责任部门
    public string ProductionDeptCode { get; set; }
    
    // 仓库级参数
    public int LeadTimeDays { get; set; }
    public decimal SafetyStock { get; set; }
    public decimal MinOrderQty { get; set; }
    public decimal MaxOrderQty { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 14.3 与Material表的关系

**Material表**：
- 物料本体属性（MaterialCode、MaterialName、Spec、MaterialType、UOM）
- 不再包含供给上下文（LeadTimeDays、SafetyStock等已废弃）

**MaterialSupplyContext表**：
- 仓库级供给上下文
- 支持同一物料在不同仓库有不同的供给方式和参数

### 14.4 同步逻辑

在 `sp_SyncMaterialMapping` 存储过程中，从 `ext_ERP_Master_View` 同步到 `MaterialSupplyContext` 表：

```sql
MERGE INTO MaterialSupplyContext AS target
USING (
    SELECT 
        mm.MaterialCode,
        em.Warehouse AS WarehouseCode,
        em.SupplyMode,
        em.ProductionDeptCode,
        em.LeadTimeDays,
        em.SafetyStock,
        em.MinOrderQty,
        em.MaxOrderQty
    FROM MaterialMapping mm
    INNER JOIN ext_ERP_Master_View em
        ON mm.MaterialCode = em.MaterialCode 
        AND mm.Source = 'ERP'
        AND mm.ERP_Warehouse = em.Warehouse
    WHERE mm.IsCurrent = 1
      AND em.IsActive = 1
) AS source
ON target.MaterialCode = source.MaterialCode
   AND target.WarehouseCode = source.WarehouseCode
...
```

---

## 十五、Material 和 MaterialMapping 更新（v2.2）

### 15.1 Material 类型更新

```csharp
public class Material
{
    public string MaterialCode { get; set; }
    public string MaterialName { get; set; }
    
    // ⚠️ v2.2 新增：物料型号/规格
    public string Spec { get; set; }
    
    public string MaterialType { get; set; }
    public string UOM { get; set; }
    public string ProductFamily { get; set; }
    
    // ⚠️ 以下字段已废弃，下沉到MaterialSupplyContext
    [Obsolete("已废弃，改用 MaterialSupplyContext.SupplyMode")]
    public bool IsPurchased { get; set; }
    
    [Obsolete("已废弃，下沉到 MaterialSupplyContext")]
    public decimal SafetyStock { get; set; }
    
    [Obsolete("已废弃，下沉到 MaterialSupplyContext")]
    public int LeadTimeDays { get; set; }
    
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 15.2 MaterialMapping 类型更新

```csharp
public class MaterialMapping
{
    public int Id { get; set; }
    public string MaterialCode { get; set; }
    
    // ERP物理主键
    public int? ERP_MasterID { get; set; }
    
    // ⚠️ v2.2 新增：支持一物多仓
    public string ERP_Warehouse { get; set; }
    
    // MES物理主键
    public int? MES_ID { get; set; }
    
    // ⚠️ v2.2 新增：支持一物多仓
    public string MES_Location { get; set; }
    
    // ⚠️ v2.2 新增：物料型号/规格
    public string Spec { get; set; }
    
    public int Priority { get; set; }
    
    // 拉链表字段
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsCurrent { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

## 十六、权限与审批接口（v2.3）

### 16.1 权限服务接口

```csharp
/// <summary>
/// 权限校验服务
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// 检查用户是否拥有指定权限
    /// </summary>
    Task<bool> HasPermissionAsync(int userId, string permissionCode);
    
    /// <summary>
    /// 获取用户的所有权限码
    /// </summary>
    Task<List<string>> GetUserPermissionsAsync(int userId);
    
    /// <summary>
    /// 批量检查用户权限
    /// </summary>
    Task<Dictionary<string, bool>> CheckPermissionsAsync(int userId, List<string> permissionCodes);
}
```

### 16.2 数据范围服务接口

```csharp
/// <summary>
/// 数据范围校验服务
/// </summary>
public interface IDataScopeService
{
    /// <summary>
    /// 检查用户是否有权访问指定工厂
    /// </summary>
    Task<bool> CheckFactoryAccessAsync(int userId, int factoryId);
    
    /// <summary>
    /// 检查用户是否有权访问指定产品族
    /// </summary>
    Task<bool> CheckProductFamilyAccessAsync(int userId, int productFamilyId);
    
    /// <summary>
    /// 检查用户是否有权访问指定资源组织维度（v5.0：ResourceGroup→ResourceOrgGroup）
    /// </summary>
    Task<bool> CheckResourceOrgGroupAccessAsync(int userId, int resourceOrgGroupId);
    
    /// <summary>
    /// 获取用户可访问的工厂列表
    /// </summary>
    Task<List<int>> GetUserFactoriesAsync(int userId);
    
    /// <summary>
    /// 获取用户可访问的产品族列表
    /// </summary>
    Task<List<int>> GetUserProductFamiliesAsync(int userId);
    
    /// <summary>
    /// 获取用户可访问的资源组织维度列表（v5.0：ResourceGroup→ResourceOrgGroup）
    /// </summary>
    Task<List<int>> GetUserResourceOrgGroupsAsync(int userId);
}
```

### 16.3 审计服务接口

```csharp
/// <summary>
/// 审计日志服务
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// 记录审计日志
    /// </summary>
    Task LogAsync(AuditLogDto auditLog);
    
    /// <summary>
    /// 查询审计日志
    /// </summary>
    Task<List<AuditLogDto>> QueryAsync(AuditLogQueryDto query);
}

/// <summary>
/// 审计日志DTO
/// </summary>
public class AuditLogDto
{
    public int UserId { get; set; }
    public string UserName { get; set; }
    public string ActionCode { get; set; }
    public string ObjectType { get; set; }
    public string ObjectId { get; set; }
    public string Result { get; set; }  // Success/Failed/Denied
    public string ClientIp { get; set; }
    public int? PlanVersionId { get; set; }
    public string BatchNo { get; set; }
    public string RequestData { get; set; }
    public string ResponseData { get; set; }
    public string ErrorMessage { get; set; }
}

/// <summary>
/// 审计日志查询DTO
/// </summary>
public class AuditLogQueryDto
{
    public int? UserId { get; set; }
    public string ActionCode { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Result { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

### 16.4 审批服务接口

```csharp
/// <summary>
/// 审批流服务
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// 发起审批流
    /// </summary>
    Task<int> StartApprovalAsync(ApprovalFlowDto approvalFlow);
    
    /// <summary>
    /// 审批（通过或拒绝）
    /// </summary>
    Task ApproveAsync(int approvalFlowId, int approverUserId, ApprovalDecisionDto decision);
    
    /// <summary>
    /// 查询待审批列表
    /// </summary>
    Task<List<ApprovalFlowDto>> GetPendingApprovalsAsync(int userId);
    
    /// <summary>
    /// 查询审批流详情
    /// </summary>
    Task<ApprovalFlowDto> GetApprovalFlowAsync(int approvalFlowId);
    
    /// <summary>
    /// 取消审批流
    /// </summary>
    Task CancelApprovalAsync(int approvalFlowId, int userId);
}

/// <summary>
/// 审批流DTO
/// </summary>
public class ApprovalFlowDto
{
    public int Id { get; set; }
    public string ApprovalType { get; set; }  // FreezeTaskAdjust/SOSacrifice/StartedTaskAdjust
    public string ObjectType { get; set; }
    public string ObjectId { get; set; }
    public int ApplicantUserId { get; set; }
    public string ApplicantUserName { get; set; }
    public string Status { get; set; }  // Pending/Approved/Rejected/Cancelled
    public int CurrentNodeSeq { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? PlanVersionId { get; set; }
    public string BatchNo { get; set; }
    public string Reason { get; set; }
    public List<ApprovalNodeDto> Nodes { get; set; }
}

/// <summary>
/// 审批节点DTO
/// </summary>
public class ApprovalNodeDto
{
    public int Id { get; set; }
    public int NodeSeq { get; set; }
    public int? ApproverRoleId { get; set; }
    public string ApproverRoleName { get; set; }
    public int? ApproverUserId { get; set; }
    public string ApproverUserName { get; set; }
    public string Status { get; set; }  // Pending/Approved/Rejected/Skipped
    public ApprovalRecordDto Record { get; set; }
}

/// <summary>
/// 审批记录DTO
/// </summary>
public class ApprovalRecordDto
{
    public int ApproverUserId { get; set; }
    public string ApproverUserName { get; set; }
    public string Decision { get; set; }  // Approved/Rejected
    public string Comment { get; set; }
    public DateTime ApprovedAt { get; set; }
}

/// <summary>
/// 审批决策DTO
/// </summary>
public class ApprovalDecisionDto
{
    public string Decision { get; set; }  // Approved/Rejected
    public string Comment { get; set; }
}
```

### 16.5 权限码定义

```csharp
/// <summary>
/// APS 权限码常量
/// </summary>
public static class ApsPermissions
{
    // 计划模块
    public const string PlanView = "aps.plan.view";
    public const string PlanRun = "aps.plan.run";
    public const string PlanPublish = "aps.plan.publish";
    
    // 任务模块
    public const string TaskView = "aps.task.view";
    public const string TaskAdjust = "aps.task.adjust";
    public const string TaskFreezeOverride = "aps.task.freeze.override";
    public const string TaskStartedOverride = "aps.task.started.override";
    
    // CTP模块
    public const string CtpView = "aps.ctp.view";
    public const string CtpEvaluate = "aps.ctp.evaluate";
    public const string CtpCommit = "aps.ctp.commit";
    
    // 配置模块
    public const string ConfigSupplyContextView = "aps.config.supply_context.view";
    public const string ConfigSupplyContextEdit = "aps.config.supply_context.edit";
    public const string ConfigInventoryRuleView = "aps.config.inventory_rule.view";
    public const string ConfigInventoryRuleEdit = "aps.config.inventory_rule.edit";
    public const string ConfigResourceView = "aps.config.resource.view";
    public const string ConfigResourceEdit = "aps.config.resource.edit";
    
    // 审批模块
    public const string ApprovalView = "aps.approval.view";
    public const string ApprovalApprove = "aps.approval.approve";
    
    // 审计模块
    public const string AuditView = "aps.audit.view";
    
    // 用户管理模块
    public const string UserView = "aps.user.view";
    public const string UserManage = "aps.user.manage";
    public const string RoleView = "aps.role.view";
    public const string RoleManage = "aps.role.manage";
}
```

### 16.6 审批类型定义

```csharp
/// <summary>
/// APS 审批类型常量
/// </summary>
public static class ApsApprovalTypes
{
    /// <summary>
    /// 冻结区任务变更
    /// </summary>
    public const string FreezeTaskAdjust = "FreezeTaskAdjust";
    
    /// <summary>
    /// 牺牲SO（挤占他单能力）
    /// </summary>
    public const string SOSacrifice = "SOSacrifice";
    
    /// <summary>
    /// 已开工任务变更
    /// </summary>
    public const string StartedTaskAdjust = "StartedTaskAdjust";
}
```

### 16.7 使用示例

#### 16.7.1 权限校验示例

```csharp
// 在应用服务层校验权限
public class PlanService
{
    private readonly IPermissionService _permissionService;
    private readonly IDataScopeService _dataScopeService;
    private readonly IAuditService _auditService;
    
    public async Task<PlanVersionDto> RunPlanAsync(RunPlanRequest request, int userId)
    {
        // 1. 校验权限
        if (!await _permissionService.HasPermissionAsync(userId, ApsPermissions.PlanRun))
        {
            await _auditService.LogAsync(new AuditLogDto
            {
                UserId = userId,
                ActionCode = ApsPermissions.PlanRun,
                Result = "Denied",
                ErrorMessage = "权限不足"
            });
            throw new UnauthorizedAccessException("权限不足");
        }
        
        // 2. 校验数据范围
        if (!await _dataScopeService.CheckFactoryAccessAsync(userId, request.FactoryId))
        {
            await _auditService.LogAsync(new AuditLogDto
            {
                UserId = userId,
                ActionCode = ApsPermissions.PlanRun,
                ObjectType = "Factory",
                ObjectId = request.FactoryId.ToString(),
                Result = "Denied",
                ErrorMessage = "无权访问该工厂"
            });
            throw new UnauthorizedAccessException("无权访问该工厂");
        }
        
        // 3. 执行业务逻辑
        var result = await ExecutePlanAsync(request, userId);
        
        // 4. 记录审计日志
        await _auditService.LogAsync(new AuditLogDto
        {
            UserId = userId,
            ActionCode = ApsPermissions.PlanRun,
            ObjectType = "PlanVersion",
            ObjectId = result.Id.ToString(),
            Result = "Success",
            PlanVersionId = result.Id,
            RequestData = JsonSerializer.Serialize(request)
        });
        
        return result;
    }
}
```

#### 16.7.2 审批流示例

```csharp
// 发起冻结区任务变更审批
public class TaskService
{
    private readonly IApprovalService _approvalService;
    
    public async Task<int> AdjustFreezeTaskAsync(AdjustTaskRequest request, int userId)
    {
        // 1. 发起审批流
        var approvalFlowId = await _approvalService.StartApprovalAsync(new ApprovalFlowDto
        {
            ApprovalType = ApsApprovalTypes.FreezeTaskAdjust,
            ObjectType = "Task",
            ObjectId = request.TaskId.ToString(),
            ApplicantUserId = userId,
            Reason = request.Reason,
            PlanVersionId = request.PlanVersionId
        });
        
        return approvalFlowId;
    }
    
    // 审批操作
    public async Task ApproveTaskAdjustmentAsync(int approvalFlowId, int userId, bool approve, string comment)
    {
        await _approvalService.ApproveAsync(approvalFlowId, userId, new ApprovalDecisionDto
        {
            Decision = approve ? "Approved" : "Rejected",
            Comment = comment
        });
    }
}

---

## 十七、工艺数据三层模型与 BOM↔Routing 对接主键（v2.4 新增 2026-04-25；v2.5 重写 2026-04-29）

本节是 **1/2/5 号位实现 BOM↔Routing 对接的内部契约统一口径**。详细字段说明见《APS_数据库字段说明文档 v5.0.16》§1.9b / §1.9c / §3.7~§3.9b；详细推导规则见《BOM_Workset v1.4》§3.6。

> **v2.5 口径变更声明**（2026-04-29）：
> 自 v5.0.16 起，1 号位 Task 生成的**唯一**取数主链为：
>
> `StageDetail (MaterialId, StageCode)` → `MaterialStageDeptContext.DefaultProductionDepartmentId` → Routing 三件套 `(MaterialId, ProductionDepartmentId, StageCode)`
>
> v2.4 中"按 `(MaterialId, StageCode)` 二元组直接读 RoutingOperation"的口径**已废止**。下面 §17.3 伪代码已按 v2.5 主链口径重写；如需追溯旧版伪代码，请翻阅本文件 git 历史 v2.4 提交。

### 17.1 三层分层模型

| 层 | 字段 | 粒度 | 值域举例 | 是否参与 BOM↔Routing 对接 |
|---|---|---|---|---|
| 第 1 层：具体工序 | `OperationCode` / `OperationName` | 执行粒度 | NC / MC / 切断 / 精修 | ❌ 仅在 Routing 侧内部按 `RoutingDependency` 串联 |
| 第 2 层：辅助分类 | `ProcessType`（值域见 `ProcessTypeDict` 骨架）| 报表粒度 | MACHINING / ASSEMBLY | ❌ **完全不参与**；仅统计/粗分组 |
| 第 3 层：大工艺 | `StageCode`（值域权威在 `StageDict`）| 对接粒度 | TJ_MACH / BJ_PAINT | ✅ **BOM↔Routing 对接主键之二** |

**三层硬红线**（不可违反）：
- ❌ 禁止把 `OperationName` 值塞进 `ProcessType`
- ❌ 禁止把 `ProcessType` 当 `StageCode`
- ❌ 禁止把 `StageCode` 当 `OperationCode`

### 17.2 BOM↔Routing 对接主键（业务 vs 物理）

| 视角 | 主键 | 使用场景 |
|---|---|---|
| **业务口径主键** | `(MaterialCode, StageCode)` | 跨号位沟通、日志/异常/Issues 登记（人可读）|
| **物理实现主键** | `(MaterialId, StageCode)` | 数据库 JOIN/索引/约束；`MaterialId` 是 `MaterialCode` 经 `MaterialMapping` 的标准化代理键 |

**等价性**：二者等价，不是两套键。

**开发实现约定**：
- 日志 / 异常消息 / `Issues` 登记：**一律带 MaterialCode**（人可读）
- 物理表 JOIN / 索引 / 外键约束：**用 MaterialId**（性能 + 数据完整性）

### 17.3 1 号位 Task 生成约定（v2.5 主链口径，2026-04-29）

```csharp
// 伪代码：1 号位排程引擎生成 Task 的标准流程（v5.0.16 主链）
// 主链：StageDetail → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套
//
// ❌ 禁止直接读 MaterialSupplyContext / ProcessCodeDict / MaterialStageDeptOverride（这些是 2 号位组装 Context 的输入源）
// ❌ 禁止跳过 MaterialStageDeptContext 直查 Routing 三件套
foreach (var (materialId, materialCode, stageCode, stageSeq) in stageDetails.OrderBy(x => x.StageSeq))
{
    // 决定 Task 归属哪个工厂的产能队列（R20 场景下直接是目标工厂）
    var factoryCode = ExtractFactoryFromStageCode(stageCode);   // 如 BJ_MACH → BJ

    // 🆕 v2.5 第一步：按 (MaterialId, StageCode) 从 MaterialStageDeptContext 锁定责任部门
    var ctx = materialStageDeptContexts
        .FirstOrDefault(c => c.MaterialId == materialId
                          && c.StageCode  == stageCode
                          && c.IsCurrent  == true);

    if (ctx == null)
    {
        // Context 缺失：登记日志，按 LeadTime 降级（不阻塞批次）
        var leadTime = stageLeadTimeParam.Lookup(materialId, stageCode);
        tasks.Add(CreateTaskFromLeadTime(materialCode, stageCode, factoryCode, leadTime));
        continue;
    }

    var productionDepartmentId = ctx.DefaultProductionDepartmentId;

    // 🆕 v2.5 第二步：按 (MaterialId, ProductionDepartmentId, StageCode) 三元组从 Routing 三件套取小工序
    var ops = routingOperations
        .Where(r => r.MaterialId == materialId
                 && r.ProductionDepartmentId == productionDepartmentId
                 && r.StageCode == stageCode)
        .ToList();

    if (!ops.Any())
    {
        // 外协阶段 / MES 工艺数据不完整：按 LeadTime 降级
        var leadTime = stageLeadTimeParam.Lookup(materialId, stageCode);
        tasks.Add(CreateTaskFromLeadTime(materialCode, stageCode, factoryCode, leadTime));
    }
    else
    {
        // 按 RoutingDependency 串并行生成小工序 Task（同样用三元组过滤）
        var deps = routingDependencies
            .Where(d => d.MaterialId == materialId
                     && d.ProductionDepartmentId == productionDepartmentId
                     && d.StageCode == stageCode)
            .ToList();
        var taskGraph = BuildTaskGraph(ops, deps);
        foreach (var task in taskGraph)
        {
            task.FactoryCode             = factoryCode;
            task.ProductionDepartmentId  = productionDepartmentId;  // 🆕 v2.5 必填
            task.StageCode               = stageCode;
            tasks.Add(task);
        }
    }
}
```

### 17.4 R20 跨组织视角统一

- `StageDetail.StageCode` 采用**目标工厂视角**（5 号位 `sp_EnrichBOMWorkset` 负责落地）
- 示例：父件在 TJ 工厂、子件 `Produce=6`（R20 指派到 BJ）→ `StageDetail.StageCode = BJ_MACH`（不是 TJ_MACH）
- 1 号位按此 StageCode 直接去 BJ 工厂的 `RoutingOperation` 匹配，Task 自动落在 BJ 工厂产能队列
- `CrossOrgHandoffFlag=1` 仅作为**旁路标签**供审计/报表使用，1 号位主排程分支**靠 StageCode 工厂前缀决策**

### 17.5 StageSeq 唯一权威

- **权威唯一源** = `MES_APS_BOM_Workset_StageDetail.StageSeq`（5 号位 BOM 派生结果）
- `RoutingStage.StageSeq` 已从 DDL v5.0.12 中**删除**；任何代码读取该字段视为 bug
- `RoutingStage` 收敛为"该物料在哪些大工艺阶段存在配置"的纯字典，不承载顺序

---

## 十八、v5.0.21 新增/变更 POCO 与 DTO（v2.7 新增 2026-05-08）

### 18.1 ERP_Order_Staging POCO（v5.0.21 变更）

```csharp
/// <summary>
/// ERP订单暂存表 POCO（v5.0.21：BOMNO改可空；新增FailureCode/NextActionCode）
/// </summary>
public class ERP_Order_Staging
{
    public long Id { get; set; }
    public string SourceOrderId { get; set; }
    public string SourceSystem { get; set; }
    public int? SourceMasterID { get; set; }
    public string OrderNo { get; set; }
    public string OrderType { get; set; }
    public string MaterialCode { get; set; }
    public string FactoryCode { get; set; }
    public decimal Quantity { get; set; }
    public string UOM { get; set; }
    public DateTime DueDate { get; set; }
    public int Priority { get; set; }
    // ⚠️ v5.0.21：改可空；有值=显式BOMNO；NULL=待5号位Workset阶段解析BOM入口
    public string? BOMNO { get; set; }
    public string? TransportMode { get; set; }
    public string? CustomerName { get; set; }
    public string? MTS_InstructionNo { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? OriginalDueDate { get; set; }
    public decimal? ReceivedQty { get; set; }
    // v5.0.24澄清：通过CustomerCodeMap本地映射表推导；JAPAN/DOMESTIC/OVERSEAS/VIETNAM/INTER_FACTORY/OTHER；无匹配默认OVERSEAS
    public string? CustomerSegment { get; set; }
    public string? SalesOrderCategory { get; set; }
    // v5.0.24收窄：PRE_CONFIRMED=事前确认 / FORECAST=预测SHIKOMI；DELAYED已拆出为DelayStatus，禁止混用
    public string? DemandMaturityStatus { get; set; }
    // v5.0.24补充等级关系：VIP>KEY_ACCOUNT>STANDARD>GENERAL；当前启用VIP/GENERAL两档
    public string? CustomerTier { get; set; }
    // ⚠️ v5.0.24 新增：延迟状态（独立维度，与DemandMaturityStatus禁止混用）
    // ON_TIME（未延迟）/ FIRST_DELAY（首次延迟）/ REPEATED_DELAY（二次及以上延迟）；V1简化：超期均记为FIRST_DELAY
    public string? DelayStatus { get; set; }
    public string? RawData { get; set; }
    public string SyncStatus { get; set; }  // PENDING/VALIDATED/FAILED/PROCESSED
    // ⚠️ v5.0.21：失败原因维度（独立）：ORDER_FIELD_INVALID / MASTER_NOT_READY
    public string? FailureCode { get; set; }
    // ⚠️ v5.0.21：后续动作维度（独立）：BOM_REQUEST_SUBMITTED / BOM_REQUEST_RETRY_PENDING / ...
    public string? NextActionCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SyncedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
```

> **⚠️ 设计红线**：`FailureCode` 和 `NextActionCode` 为两个独立维度，**禁止混用**。`SyncStatus` 只表达技术流转。

### 18.2 BomRequestDetailDto（v5.0.21 新增）

```csharp
/// <summary>
/// BOM展开请求明细 DTO（v5.0.21：订单粒度；BOMNO可空）
/// 2号位推送到ODS库 MES_API_BOM_Request_Detail 时使用
/// </summary>
public class BomRequestDetailDto
{
    public string BatchNo { get; set; }
    // ⚠️ v5.0.21 新增：FK→ERP_Order_Staging.Id；唯一约束(BatchNo, OrderStagingId)
    public long OrderStagingId { get; set; }
    public string? Model { get; set; }
    public string MaterialCode { get; set; }
    public string FactoryCode { get; set; }
    // ⚠️ v5.0.22 新增：从ERP_Order_Staging.OrderType透传；BOMNO=NULL时5号位据此分支选取BOM入口规则
    // v5.0.24更新典型值：SALES_ORDER=客户订单（取IsDefaultVersion=1）/ PRODUCTION_INSTRUCTION=生产指示（优先MES工艺BOM）/ NULL=未知（降级+记Issues）
    public string? OrderType { get; set; }
    // ⚠️ 可空：有值=显式BOMNO直接展开；NULL=5号位按OrderType+Model/MaterialCode推导BOM入口
    public string? BOMNO { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 18.3 BOMWorkset POCO（v5.0.21 变更）

```csharp
/// <summary>
/// BOM展开结果行 POCO（v5.0.21：新增RequestDetailId追溯锚点）
/// 对应 MES_APS_BOM_Workset 表
/// </summary>
public class BOMWorkset
{
    public long Id { get; set; }
    public string BatchNo { get; set; }
    public string BOMNO { get; set; }
    public string ParentMaterialCode { get; set; }
    public string ChildMaterialCode { get; set; }
    public decimal Quantity { get; set; }
    public int Level { get; set; }
    public string? Path { get; set; }
    public string? ParentProcRefCode { get; set; }
    public string? ChildProcRefCode { get; set; }
    public string? ChildSourceHintCode { get; set; }
    public string? ChildRequiredStageCode { get; set; }
    public string? ChildRequiredFactory { get; set; }
    // ⚠️ v5.0.21 新增：追溯锚点，FK→MES_API_BOM_Request_Detail.Id；非业务键，1号位不消费
    public long? RequestDetailId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

## §19 PipelineSupplyItem POCO（2026-05-09 v2.9 新增）

**业务用途**：管道供给条目（在途/外部供给）的内存对象，对应 `SupplyFact_Pipeline` 表字段。  
**消费方**：1号位排程引擎通过 `ScheduleContext.PipelineSupplies` 读取。  
**装载方**：`IDataLoader.LoadPipelineSupplies()` （执行 `sp_SyncPipelineSupply` 后从 `SupplyFact_Pipeline` 读取）。

```csharp
/// <summary>
/// 管道供给条目
/// 对应数据库表 SupplyFact_Pipeline，并行于现货库存五层主链
/// </summary>
public sealed class PipelineSupplyItem
{
    // ============ 主键 ============
    
    public long Id { get; init; }
    
    // ============ 物料维度 ============
    
    /// <summary>主业务追溯键（ODS原始）</summary>
    public string MaterialCode { get; init; }
    
    /// <summary>增强字段；装载时通过 MaterialMapping 映射；NULL=物料未建档，降级处理</summary>
    public int? MaterialId { get; init; }
    
    // ============ 工厂 / 产品族维度 ============
    
    /// <summary>ODS原始工厂编码</summary>
    public string FactoryCode { get; init; }
    
    /// <summary>装载时映射；可为空</summary>
    public int? FactoryId { get; init; }
    
    /// <summary>装载时映射；可为空</summary>
    public int? ProductFamilyId { get; init; }
    
    // ============ 供给特征 ============
    
    /// <summary>INTERPLANT_IN_TRANSIT / PURCHASE_IN_TRANSIT / ARRIVED_NOT_RECEIVED / VMI_ONSITE / SUPPLIER_RESERVED</summary>
    public string SupplyType { get; init; }
    
    /// <summary>OWNED / CONSIGNMENT / SUPPLIER；默认 OWNED</summary>
    public string OwnershipType { get; init; }
    
    /// <summary>AVAILABLE / PENDING_INSPECTION / HOLD；默认 AVAILABLE</summary>
    public string QualityStatus { get; init; }
    
    public decimal Quantity { get; init; }
    
    // ============ 时间（❗字段语义严格区分） ============
    
    /// <summary>
    /// ❗ ODS原始事实字段：源系统预计到达时间。
    /// 1号位禁止修改此字段。
    /// </summary>
    public DateTime? ETA { get; init; }
    
    /// <summary>
    /// ❗ 本地派生字段：= ETA + SupplyAvailabilityRule.LeadTimeOffset（小时）。
    /// 由 sp_SyncPipelineSupply 装载时计算落库。
    /// 1号位读取此字段作为排程可用时间。
    /// </summary>
    public DateTime? AvailableTime { get; init; }
    
    // ============ 追溯 ============
    
    public string? StorageCode { get; init; }
    public string? SupplierCode { get; init; }
    public string SourceSystem { get; init; }
    public string? SourceDocumentNo { get; init; }
    
    // ============ 快照与状态 ============
    
    /// <summary>
    /// nullable；夜间全量排程=当日BatchNo（快照）；白天实时=NULL（读最新 IsActive=1）
    /// </summary>
    public string? BatchNo { get; init; }
    
    public bool IsActive { get; init; }
    public DateTime SyncedAt { get; init; }
}
```

**IDataLoader 方法签名**（v2.9 新增）：
```csharp
/// <summary>
/// 装载管道供给条目列表，注入 ScheduleContext.PipelineSupplies
/// 执行 sp_SyncPipelineSupply 后，从 SupplyFact_Pipeline 读 IsActive=1 记录
/// ❗ 当 SupplyFact_Pipeline 无记录时返回空集合（不影响现有排程）
/// </summary>
Task LoadPipelineSupplies(ScheduleContext context, CancellationToken cancellationToken = default);
```

---

## §20 排程运行编排与结果读模型类型（v2.11 新增，2026-05-13）

**对齐**：DDL v5.0.25 / 总表 v3.17 / 防腐层 v1.20  
**阶段说明**：`RunType` / `ScheduleRunRecord` / `ExplanationFactDraft` / `ScheduleExplanationFact` 阶段一即用；`Scenario`/`SimulationRun`/`ScenarioObjectiveScore` 阶段二实装，阶段一建骨架。

### 20.1 RunType 枚举

```csharp
/// <summary>
/// 排程运行类型。统一所有运行模式，共用 ScheduleRun 编排对象。
/// FULL_SCHEDULE 产出 PlanVersion.Status=ACTIVE（自动激活）；
/// 其余类型产出 PlanVersion.Status=CANDIDATE，禁止自动激活。
/// </summary>
public enum RunType
{
    FULL_SCHEDULE,          // 凌晨 Hangfire 定时全量排程（阶段一主链）
    MANUAL_RESCHEDULE,      // 人工触发重排（不要求先建 Scenario）
    LOCAL_RESCHEDULE,       // 局部重排（指定范围）
    SIMULATION,             // 仿真（通常关联 Scenario，阶段二实装）
    INSERT_ORDER_WHATIF     // 插单影响分析（阶段二实装）
}
```

### 20.2 ScheduleRunRecord POCO（对应数据库 `ScheduleRun` 表）

```csharp
/// <summary>
/// 排程运行编排主记录。代表"这次计算运行"，与 PlanVersion（结果版本）分离。
/// 3号位创建初始记录（Status=RUNNING），2号位排程完成后回填 Status + OutputPlanVersionId。
/// </summary>
public sealed class ScheduleRunRecord
{
    public int Id { get; init; }

    /// <summary>运行类型</summary>
    public RunType RunType { get; init; }

    /// <summary>运行状态：RUNNING / COMPLETED / FAILED</summary>
    public string Status { get; set; }

    /// <summary>
    /// 产出的版本 ID。排程完成后由 2号位回填。
    /// FULL_SCHEDULE → 对应 Status=ACTIVE 的 PlanVersion；
    /// 其余 RunType → 对应 Status=CANDIDATE 的 PlanVersion。
    /// </summary>
    public int? OutputPlanVersionId { get; set; }

    /// <summary>关联仿真场景（阶段二；SIMULATION/INSERT_ORDER_WHATIF 类才填；阶段一为 null）</summary>
    public int? ScenarioId { get; init; }

    /// <summary>触发来源：'Hangfire' / UserId 字符串 / 'API'</summary>
    public string TriggeredBy { get; init; }

    public DateTime StartedAt { get; init; }

    /// <summary>排程完成后由 2号位回填</summary>
    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; init; }
}
```

### 20.3 ExplanationFactDraft（内存对象，1号位产出，禁止直接写 DB）

```csharp
/// <summary>
/// 排程原因事实草稿。1号位在内存推演中产出，传递给 2号位后由 2号位批量落库为 ScheduleExplanationFact。
/// ⚠️ 1号位禁止直接写 DB；此对象仅在内存中存在。
/// EvidenceJson 外壳稳定（保证向后兼容），各 ReasonCode 内部 schema 随阶段演进填充。
/// </summary>
public sealed class ExplanationFactDraft
{
    /// <summary>对象类型：ORDER / TASK / RESOURCE</summary>
    public string ObjectType { get; init; }

    public int? OrderId { get; init; }
    public int? TaskId { get; init; }
    public int? ResourceId { get; init; }
    public string? StageCode { get; init; }

    /// <summary>结构化原因码，阶段二按业务场景扩充</summary>
    public string ReasonCode { get; init; }

    /// <summary>严重级别：INFO / WARN / ERROR</summary>
    public string Severity { get; init; }

    /// <summary>影响小时数（null = 未量化）</summary>
    public decimal? ImpactHours { get; init; }

    /// <summary>佐证 JSON（外壳 key 固定；value 结构按 ReasonCode 演进）</summary>
    public string? EvidenceJson { get; init; }
}
```

### 20.4 ScheduleExplanationFact POCO（对应数据库 `ScheduleExplanationFact` 表）

```csharp
/// <summary>
/// 结构化排程原因事实（DB 落库版本）。由 2号位从 ExplanationFactDraft 转换后与 Task/Pegging 同批次落盘。
/// 分区键：PlanVersionId。
/// </summary>
public sealed class ScheduleExplanationFact
{
    public long Id { get; init; }
    public int PlanVersionId { get; init; }
    public string ObjectType { get; init; }
    public int? OrderId { get; init; }
    public int? TaskId { get; init; }
    public int? ResourceId { get; init; }
    public string? StageCode { get; init; }
    public string ReasonCode { get; init; }
    public string Severity { get; init; }
    public decimal? ImpactHours { get; init; }
    public string? EvidenceJson { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### 20.5 读模型 POCO（阶段一即用，由 2号位异步生成，禁止进入 1号位排程内核）

```csharp
/// <summary>订单级排程摘要（对应 OrderScheduleSummary 表）</summary>
public sealed class OrderScheduleSummaryDto
{
    public int OrderId { get; init; }
    public int PlanVersionId { get; init; }
    public DateTime? PlannedEndDate { get; init; }
    /// <summary>延期小时数（负值=提前）</summary>
    public decimal? DelayHours { get; init; }
    /// <summary>风险级别：LOW / MEDIUM / HIGH / CRITICAL</summary>
    public string? RiskLevel { get; init; }
    /// <summary>主因原因码（来自 ScheduleExplanationFact）</summary>
    public string? MainReasonCode { get; init; }
    public bool IsVipImpacted { get; init; }
    public DateTime GeneratedAt { get; init; }
}

/// <summary>资源×日期负荷摘要（对应 ResourceLoadSummary 表）</summary>
public sealed class ResourceLoadSummaryDto
{
    public int ResourceId { get; init; }
    public int PlanVersionId { get; init; }
    public DateOnly LoadDate { get; init; }
    public decimal? LoadHours { get; init; }
    public decimal? AvailableHours { get; init; }
    /// <summary>负荷率（可超 1.0 = 过载）</summary>
    public decimal? LoadRate { get; init; }
    public bool IsBottleneck { get; init; }
    public DateTime GeneratedAt { get; init; }
}

/// <summary>版本级 KPI 汇总（对应 PlanKpiSummary 表，与 PlanVersion 1:1）</summary>
public sealed class PlanKpiSummaryDto
{
    public int PlanVersionId { get; init; }
    /// <summary>准交率 0.0000-1.0000</summary>
    public decimal? OnTimeRate { get; init; }
    public int? DelayedOrderCount { get; init; }
    public decimal? MaxDelayHours { get; init; }
    public int? VipDelayedCount { get; init; }
    public decimal? AvgLoadRate { get; init; }
    public int? BottleneckCount { get; init; }
    public decimal? WipEstimate { get; init; }
    public DateTime GeneratedAt { get; init; }
}
```

---

**文档结束**

**交付时间**：2026-05-09  
**适用项目**：Lean APS V1.0  
**维护责任人**：2号位（技术负责人）  
**文档版本**：v2.11（含 v5.0.25 排程运行编排：RunType enum + ScheduleRunRecord + ExplanationFactDraft + 读模型 POCO）

**附件文档**：
- `契约文档/01_核心数据结构契约.md`
- `契约文档/02_凭证契约规范.md`
- `契约文档/03_2号位对外契约.md`
- `契约文档/04_5号位对外契约.md`
- `契约文档/05_3号位和1号位对外契约.md`
- `契约文档/06_异常处理和性能要求.md`
