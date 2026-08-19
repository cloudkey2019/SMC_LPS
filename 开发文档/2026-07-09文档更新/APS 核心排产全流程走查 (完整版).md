# APS 核心排产全流程走查（完整版 V3.14）

**版本**：V3.14  
**日期**：2026-06-23  
**覆盖范围**：主流程 + 跨厂协同 + 分域计算 + 异常处理 + 人工干预 + 数据同步 + 监控容错 + 计划发布（共30个完整流程）

**v3.12更新说明**（2026-06-15 管道供给链路完整骨架 + 00:55同步步骤 + ODS空契约视图 + 分层语义修正，对齐 DDL v5.0.42 / 演变总表 v3.27 / 防腐层 v1.31 / 字段说明 v5.0.42）：

- 🆕 **00:55 管道供给同步**：新增步骤 `sp_SyncPipelineSupply`（V1 空跑：清空 `SupplyFact_Pipeline` + 写 SUCCESS 日志）
- 🔄 **数据流向总览更新**：补充 00:55 管道供给同步行；02:00 ScheduleContext 装载补充 `PipelineSupplies`（V1 空集合）
- 📌 **V1 空跑声明**：00:55 V1 不读取 ODS 空视图；`PipelineSupplies` 为空属于正常结果，不是 ETL 失败
- 📌 **分层语义统一**：`ERP_InterplantInTransit_View` = ODS 层（MES_Integration/来源ERP/5号位）；`ext_ERP_InterplantInTransit_View` = APS 层（APS_Production/2号位）

**v3.11更新说明**（2026-06-12 MES生产进度汇总链路 + 订单状态准入 + Task/Pegging重算口径 + EAM预留，对齐 DDL v5.0.41 / 演变总表 v3.26 / 防腐层 v1.30 / 字段说明 v5.0.41）：

- 🆕 **00:40/00:45/00:50 三个新步骤**：MES工单快照同步 → 工序进度快照同步 → 大工艺进度快照同步（`sp_SyncMESWorkOrderSnapshot` / `sp_SyncOperationProgressSnapshot` / `sp_SyncStageProgressSnapshot`）
- 📌 **00:00 活跃根集合**：补充 `Order_Canonical.Status` 过滤声明（CLOSED/CANCELLED 不进入 BOM Request）
- 📌 **数据流向总览更新**：补充 00:40/00:45/00:50 三行；02:00 ScheduleContext 装载补充 MES 进度快照
- 📌 **步骤1.2/1.3 注记**：Task/Pegging 全量重算口径写死；MES 进度不匹配历史 TaskId；Pegging 不跨版本复用
- 📌 **EAM V1 预留**：`EAM_APS_Resource_View` 预留占位声明，V1 不读取 EAM 数据，不生成资源不可用窗口

**v3.10更新说明**（2026-05-21 BOM入口分流R28/R29/R30/R31，对齐 DDL v5.0.28 / 字段说明 v5.0.28 / 防腐层 v1.23）：

- 🔄 **00:20 BOM批次展开 — 步骤3**：补充BOMNO IS NULL分流描述（R28/R29/R30/R31详见防腐层文档§2.3.2）

**v3.9更新说明**（2026-05-16 订单提升链路重构，对齐 DDL v5.0.27 / 字段说明 v5.0.27 / 防腐层 v1.22）：

- 🔄 **数据流向总览**：白天行推进`sp_ValidateAndPromoteOrders`说明更新对齐v5.0.27重写要点
- 🔄 **场景1 步骤1.2**：订单验证动作说明全量更新（MaterialCode三级解析链、OrderType未知→FAILED、BOMNO_MISSING非阻断、CustomerSegment无匹配→UNKNOWN）
- 🔄 **v5.0.27设计红线**：FailureCode单值分层语义写死

**v3.8更新说明**（2026-05-13 阶段二三接缝：ScheduleRun包装 + 落库与激活分离 + 仿真入口预留，对齐总表 v3.17 / 字段说明 v5.0.25 / 防腐层 v1.20）：

- ✅ **阶段0**：`ScheduleRun` 优先创建（`RunType=FULL_SCHEDULE`，`Status=RUNNING`）作为运行编排包装，再生成 `PlanVersionId`；阶段一不改排程内核；数据流向图同步更新
- ✅ **步骤5.1 落盘**：补 `ScheduleExplanationFact` 批量落库（2号位从 1号位 `ExplanationFactDraft` 接收，与 Task/Pegging 同批次）；补 `ScheduleRun.Status=COMPLETED` + `PlanVersion.Status=ACTIVE`（v3.13 四表收敛）
- ✅ **步骤5.2 版本指针切换**：区分 `FULL_SCHEDULE`（完成后自动激活）vs 其他 RunType（产出 CANDIDATE 版本，须显式触发激活）；**落库与激活分离**红线写死
- ✅ **步骤5.5.5（新增）仿真/人工重排入口预留**：描述阶段二 SIMULATION / INSERT_ORDER_WHATIF 入口位置；阶段一此步为骨架注解，不实装

**v3.7更新说明**（2026-05-08 订单BOM入口解析重构，对齐 DDL v5.0.21 / 字段说明 v5.0.21 / 防腐层 v1.17 / 集成接口 v1.14）：

- ✅ **00:00 活跃根集合划定**：描述从"提取去重BOMNO"改为"按活跃订单粒度推送BOM请求"（含无BOMNO订单；不再去重）
- ✅ **00:20 BOM批次展开**：更新动作描述对齐新 `MES_API_BOM_Request_Detail` 结构 + `RequestDetailId` 追溯锚点
- ✅ **数据流向总览**：00:00 行 + 00:20 行更新
- ✅ **场景1 步骤1.2**：sp_ValidateAndPromoteOrders 补 `FailureCode`/`NextActionCode` 说明；BOMNO改可空（废除必填校验）
- 📌 **设计决策写死**：BOM入口解析分流在**5号位Workset处理阶段**；活跃根集合不再依赖BOMNO去重

**v3.6更新说明**（2026-05-04 BOM 回填 SP 完整实现，对齐 DDL v5.0.18 / 字段说明 v5.0.18 / 防腐层 v1.16 / 集成接口 v1.13 / 内部契约 v2.6）：

- ✅ **00:20 BOM批次展开**：5号位后置回填从笼统描述升级为明确 SP 名称 `sp_EnrichBOMWorkset`，补充 `ChildRequiredFactory` + Issues 降级登记
- ✅ **数据流向总览**：00:20 行补 SP 名称
- ✅ 相关权威文档引用版本升级

---

**v3.5更新说明**（2026-04-29 生产部门主链注入 + ProcessCodeDict 重定位，对齐 DDL v5.0.16 / 字段说明 v5.0.16 / 防腐层 v1.15 / 资源重设计 v5.2 / 架构总表 v3.12 / 集成接口 v1.12）：

### 阶段01 数据备料 时序补入两个新步骤

| 时间 | 步骤 | 说明 |
|---|---|---|
| 00:10 | `sp_SyncResourceData(@SourceType='MES')` 升级 | MERGE 加 `ProductionDepartment` 双字典映射 JOIN（FactoryCode + ProductionDeptCode 任一未命中即跳过登记日志） |
| **02:30 🆕** | `sp_RebuildMaterialStageDeptContext(@TriggerMode='FULL')` | v5.0.16 新增——⚠️ **占位骨架，当前未实现**（DDL Step1~6 全 TODO）；设计意图：2 号位每日定时全量重建 `MaterialStageDeptContext`，冲突/缺失登记 `MaterialStageDeptContext_Issues`，旧 IsCurrent=1 不动；本步骤在实装前**走查不实际执行**，1 号位临时空跑 / 退化为按 LeadTime 降级 |

### 1 号位排程主链口径升级

```text
v5.0.13 主链：StageDetail (MaterialId, StageCode) → RoutingOperation (MaterialId, StageCode)
v5.0.16 主链：StageDetail (MaterialId, StageCode) → MaterialStageDeptContext → ProductionDepartmentId → Routing 三件套 (MaterialId, ProductionDepartmentId, StageCode)
```

**1 号位消费红线**（详见集成接口 v1.12 §1 号位消费契约）：
- ❌ 1 号位禁止直接读 `MaterialSupplyContext` / `ProcessCodeDict` / `MaterialStageDeptOverride`
- ❌ 1 号位禁止跳过 Context 直查 Routing 三件套（必须先经 `MaterialStageDeptContext` 锁部门）

### 字段契约升级（4 + 1 个 ODS 视图）

`MES_APS_Resource_View` / `MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `APS_OperationResourceEligibility_View` 全部加 **`ProductionDeptCode`**（`MES_APS_Resource_View` 同时 DROP `WorkshopCode`）；`MES_ProcessCode_View` 加 **`StageCode`** 增强列 + RENAME `SourceSystem` → `CodeOrigin`。

### EAM 扩展路径不变

走查步骤零修改（SP 直接传 `@SourceType='EAM'` 即可）；双字典映射逻辑零分叉。

【设计决策】**部门 = 物料 × 阶段联合属性**：不进 StageDict / 不进 StageDetail；2 号位 SP 组装后由 1 号位消费。  
【设计决策】**Routing 三件套 `ProductionDepartmentId NOT NULL`**：业务确认 MES 工艺数据全部带部门，不引入 `_UNSPECIFIED` 哨兵；映射失败行登记 `APS_ETL_Log` 跳过（不阻塞批次）。

---

> ⚠️ **以下历史版本说明仅用于追溯；当前开发与测试一律以本文档顶部当前版本口径为准。**

---

**v3.4更新说明**（2026-04-25 对齐 DDL v5.0.13 + 防腐层 v1.14，补资源主数据同步链路）：
- ✅ **阶段01 数据备料** 时序中补入新步骤 **00:10 资源主数据同步**（`sp_SyncResourceData(@SourceType='MES')` → `Resource` 表），填补 v5.0 重构后 Resource 改为外部镜像的回路缺失
- ✅ ODS 契约视图命名统一：`APS_Resource_View` / `ext_APS_Resource_View` → `MES_APS_Resource_View` / `ext_MES_APS_Resource_View`（与 `MES_APS_Routing_*_View` 对齐）
- ✅ **数据流向总览** 时序表新增 00:10 Resource 同步行；前置条件表述由 “00:05-00:35” 扩展为 “00:05-00:35（含资源镜像刷新）”
- ✅ 时间表在全文档统一对齐：Resource 同步落在 **00:10** （与主数据同窗口，二者同属外部主数据镜像且执行时间均为秒级）；同步更新 DDL v5.0.13 注释 / 防腐层 v1.14 changelog / 资源重设计 v5.1 §9 / 字段说明 v5.0.13 的时间表
- 【设计决策】EAM 扩展路径演练点：未来 EAM 上线只需在 ODS 同构新建 `EAM_APS_Resource_View`，走查步骤无需修改（SP 直接传 `@SourceType='EAM'`）

**v3.3更新说明**（2026-04-18 对齐v5.0/v5.0.8数据架构，11处过时内容修订）：
- ✅ **#1** 00:10 `sp_SyncMaterialMapping` → `sp_SyncMasterData(@SourceType)` + 三表协同（Material+MaterialMapping+MaterialSupplyContext）
- ✅ **#2/#4** 00:15 Routing同步：原单一`Routing`表+视图 → v5.0拆分后5表3视图（RoutingOperation/Dependency/Eligibility/PlanningParam/Stage）
- ✅ **#3** 00:20 BOM展开：负责人从3号位修正为2号位(发起)+5号位(展开+回填)，补StageDetail(EDGE/ROOT)搬运步骤
- ✅ **#5/#6** 步骤1.2/1.3：所有对已废弃`BOM`/`Routing`表的引用替换为v5.0正式表名
- ✅ **#7** 阶段0.5跨域扫描：DDL和SQL完全重写，对齐v5.0表名/字段/枚举值，补`Domain_Dependency`正式DDL到SQL文件(v5.0.9)
- ✅ **#8** 步骤4.2：`Material.LeadTime` → `MaterialSupplyContext.LeadTimeDays`（v5.0仓库级上下文）
- ✅ **#9** 步骤2.6 Task实例化：补StageDetail/StageScopeType=EDGE/ROOT双层路径说明
- ✅ **#10** 附录A CDC代码：`MergeOrders`直合 → `InsertToOrderStaging`三层路径
- ✅ **#11** 总结第三部分场景3标题对齐正文（分域结果合并 → 单向硬约束传递）

**v3.2更新说明**（2026-04-03 订单链路审计）：
- ✅ 数据备料时序补00:00活跃根集合划定步骤，00:05改为Order_Canonical→sp_SyncOrdersToPartitionTable→Order三层路径
- ✅ 数据流向总览补白天增量行（ERP_Order_Staging→sp_ValidateAndPromoteOrders→Order_Canonical）
- ✅ 场景1"ERP增量订单同步"全面重写：步骤1.1改为增量拉取写入Staging、步骤1.2改为sp_ValidateAndPromoteOrders校验提升到Canonical

**v3.1更新说明**（2026-03-19）：
- ✅ 补充数据准备阶段详细时序（00:05-00:35）
- ✅ 明确Socket-Plug模式下的数据同步流程
- ✅ 补充主数据同步、工艺路线同步、BOM批次展开的详细步骤
- ✅ 更新相关文档引用

---

## 前言：架构思想说明

为了让大家理解系统为什么能达到"15分钟排完 10 万个任务"的极致性能，并能适应未来车间十年不断变化的业务需求，我们采用的是世界顶级高级计划排程系统（如 Asprova）的核心架构思想：**"机制与策略分离"**以及**"物料扣减与时间推演物理隔离"**。

**核心设计原则**：
- ✅ **稳定的框架 + 灵活的插件**：2号位提供稳定的引擎框架，5号位实现频繁变动的业务规则
- ✅ **分域隔离计算**：7个产品族并发排程，单域控制在30分钟内
- ✅ **全量内存计算**：所有I/O操作前置，核心计算在纯内存中进行
- ✅ **多版本隔离**：Append-Only策略，零停机发布
- ✅ **凭证交互模式**：5号位返回凭证（Voucher），2号位统一执行状态变更，确保事务一致性

**⚠️ 凭证交互模式说明**：

为了确保系统的事务一致性和职责边界清晰，本系统采用**"凭证交互模式"**：

```
5号位.业务规则计算 → 返回Voucher凭证 → 2号位.统一执行状态变更 → 沙盘更新
```

**核心原则**：
1. **5号位绝对禁止直接修改数据**：只负责计算和判断，返回凭证（如 `ToleranceClosureVoucher`、`PeggingVoucher`）
2. **2号位统一执行变更**：根据凭证统一执行库存扣减、Task状态修改、连线生成等操作
3. **确保事务一致性**：所有状态变更在2号位的事务控制下进行，避免数据不一致

**典型应用场景**：
- MES实绩防呆（容差结案、倒推结案、超时熔断）
- 库存扣减与Pegging连线
- 冻结区打标（IsFrozen标签）

---

## 第一部分：凌晨全量排程主流程（6个阶段）

以下是每日凌晨 02:00，系统触发全量排程时的全流程接力过程：

### 🏁 阶段0：触发起点

**时间**：凌晨 02:00  
**触发方式**：Hangfire 定时任务

**动作**：
- **【3号位】** Hangfire 后台定时任务准时触发，按下本次排产的主控按钮
- **【3号位】** 读取已在数据准备阶段（00:38 前）预创建的 `ScheduleRun` 记录，获取 `ScheduleRunId`、`DataCutoffTime` 和 `StrategyProfileVersionId`（v3.14 策略包绑定）
- **【3号位】** 根据 `StrategyProfileVersionId` 加载 `StrategyProfileVersion → RuleSetVersion + ParameterSetVersion`，初始化 `ScheduleContext.RuleConfig / SchedulingParams`；1号位/5号位只消费已装载的规则参数结果，不直接读维护表
- **【2号位】** 创建 `PlanVersion`（计划版本表）：
  - `SourceScheduleRunId` = 当前 ScheduleRun.Id
  - `SourceSimulationRunId` = NULL
  - `VersionCategory` = `DAILY_BASELINE`
  - `Status` = `BUILDING`（版本壳已创建，结果尚未落库）
  - `PlanHorizonStart/End` = 计划窗口
  - `BatchNo` = 本次数据批次号
- **【3号位】** 在服务器内存中初始化”排产沙盘（`ScheduleContext`）”，将 `ScheduleRunId` 注入内存对象

**数据流向**：
```
Hangfire 定时器 → 读取已创建的 ScheduleRun（Id + DataCutoffTime）
→ 2号位创建 PlanVersion（SourceScheduleRunId, Status=BUILDING）
→ 初始化 ScheduleContext（内存）
```

**排程成功后**：
```
ScheduleRun.Status     = COMPLETED / CompletedAt = 当前时间
PlanVersion.Status     = ACTIVE / ActivatedAt = 当前时间 / ActivatedBy = 'SYSTEM'
```

**排程失败后**：
```
ScheduleRun.Status     = FAILED / ErrorMessage = 错误信息
PlanVersion.Status     = FAILED
```

> **v3.13 架构说明**：四表职责收敛——`ScheduleRun` 记录运行过程（运行状态归它），`PlanVersion` 记录结果版本（版本生命周期状态归它：BUILDING→ACTIVE/FAILED）。正式采用直接看 `PlanVersion.Status = ACTIVE`。`PlanVersion.SourceScheduleRunId` 反向追溯到运行记录。v3.12 时序修正：`ScheduleRun` 必须在 00:38 前预创建。

---

### � 阶段0.5：跨域依赖静态扫描（解决拓扑排序死循环）

**时间**：凌晨 01:50（主排程 02:00 前的预处理阶段）  
**负责人**：2号位（数据基础设施）

**⚠️ 架构红线说明**：
- **问题**：如果"跨域依赖图"是在排程子任务启动后动态识别的，那么3号位在发车前无法知道依赖关系，就无法进行拓扑排序（谁先跑、谁后跑），导致逻辑死循环。
- **解决**：必须在排程启动前，通过静态SQL扫描全局BOM，将跨域血缘关系提前固化到数据库表中。

#### **步骤0.5.1：区分"跨厂同族"与"跨域异族"**

**动作**：
- **【2号位】** 明确两类跨域场景：
  - **跨厂同产品族**（内政）：A厂和B厂都生产产品族X，半成品在厂间流转
    - 这类依赖由 **【5号位】** 在排程时，基于真实厂间订单动态生成物流发货Task（ShippingTask）
    - 不影响域调度顺序（因为都是同一个产品族域）
  - **跨产品族依赖**（外交）：产品族A消耗产品族B的半成品
    - 这类依赖必须**提前静态扫描**，否则无法确定域的执行顺序

#### **步骤0.5.2：静态扫描跨域BOM血缘关系**

**动作**：
- **【2号位】** 在 01:50 执行SQL暴力扫描，生成 `DomainDependency` 表

**DDL（数据定义）**（2026-04-18 更新，对齐v5.0 DDL）：
```sql
-- =============================================
-- 跨产品族域依赖表（01:50静态扫描产物）
-- 用途：3号位在02:00读取此表构建拓扑排序，决定域调度顺序
-- 刷新频率：每日01:50全量TRUNCATE+INSERT
-- =============================================
CREATE TABLE Domain_Dependency (
    UpstreamDomainCode   NVARCHAR(50) NOT NULL,  -- 上游域（ProductFamily.Code，如：产品族B）
    DownstreamDomainCode NVARCHAR(50) NOT NULL,  -- 下游域（ProductFamily.Code，如：产品族A）
    ChildMaterialCode    NVARCHAR(50) NOT NULL,  -- 关联的半成品物料编码（Material.MaterialCode）
    DefaultLeadTimeDays  INT NOT NULL DEFAULT 2,  -- 跨域物流默认提前期（天），V1硬编码=2，V2可配置化
    ScannedAt            DATETIME2 NOT NULL DEFAULT GETDATE(),  -- 扫描时间戳
    PRIMARY KEY (UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode)
);
```

**SQL扫描逻辑**（2号位需要编写的脚本）（2026-04-18 更新，对齐v5.0表名和字段）：
```sql
-- 清空旧数据
TRUNCATE TABLE Domain_Dependency;

-- 扫描跨域BOM依赖（基于APS_BOM_RAW + Material + ProductFamily）
INSERT INTO Domain_Dependency (UpstreamDomainCode, DownstreamDomainCode, ChildMaterialCode, DefaultLeadTimeDays)
SELECT DISTINCT
    供应域.Code           AS UpstreamDomainCode,
    消耗域.Code           AS DownstreamDomainCode,
    子件.MaterialCode     AS ChildMaterialCode,
    2                     AS DefaultLeadTimeDays   -- V1硬编码2天，V2可从配置表读取
FROM 
    APS_BOM_RAW AS BOM
    INNER JOIN Material AS 父件 ON BOM.ParentMaterialCode = 父件.MaterialCode
    INNER JOIN Material AS 子件 ON BOM.ChildMaterialCode  = 子件.MaterialCode
    INNER JOIN ProductFamily AS 消耗域 ON 父件.ProductFamilyId = 消耗域.Id
    INNER JOIN ProductFamily AS 供应域 ON 子件.ProductFamilyId = 供应域.Id
WHERE 
    供应域.Code <> 消耗域.Code                      -- 只保留跨域依赖
    AND 子件.MaterialType = 'SEMI_FINISHED';        -- 只关注半成品（v5.0枚举值）
```

**⚠️ V1已知简化**：
- `DefaultLeadTimeDays` 硬编码为2天。V2阶段可新增 `DomainLogisticsConfig` 配置表，按域对精确化
- 原文档中的 `LogisticsConfig` 表在当前DDL中不存在，V1先用硬编码兜底

**数据来源**：APS.APS_BOM_RAW + APS.Material + APS.ProductFamily  
**数据去处**：APS.Domain_Dependency 表

**数据流向**：
```
APS.APS_BOM_RAW + Material + ProductFamily → 2号位.SQL静态扫描 → Domain_Dependency表（固化跨域血缘）
```

#### **步骤0.5.3：3号位读取静态依赖图，构建拓扑排序**

**动作**：
- **【3号位】** 在 02:00 排程启动时，执行一句简单查询：
  ```sql
  SELECT * FROM Domain_Dependency;
  ```
- **【3号位】** 基于这张静态表，使用图算法（如 Kahn 算法）构建拓扑排序
- **【3号位】** 决定域的执行顺序（哪个域先跑、哪个域后跑）

**示例**：
```
扫描结果：
- 产品族B（电机） → 产品族A（整机）
- 产品族C（轴承） → 产品族B（电机）

拓扑排序结果：
Layer 0: 产品族C（无依赖，可先跑）
Layer 1: 产品族B（依赖C，C跑完后跑）
Layer 2: 产品族A（依赖B，B跑完后跑）
```

**⚠️ 架构契约**：
- 3号位的调度器**只能基于这张静态表**画图
- **绝对禁止**在内存沙盘中动态发现新的跨域调度依赖
- 如果业务需要新增跨域依赖，必须修改BOM后，等下一轮01:50扫描生效

**架构收益**：
- 彻底消灭"死循环悖论"
- 3号位在02:00启动时，瞬间获得拓扑排序结果
- 分域计算可以按正确顺序并发执行

---

### 阶段1：数据备料与配置装载（瞬间快照）

**负责人**：2号位（数据基础设施）

**⚠️ 数据准备阶段时序（Socket-Plug模式）**：

为了确保02:00排程时数据已就绪，系统在凌晨执行以下数据准备流程：

**00:00 - 活跃根集合划定**（2026-04-03 订单链路审计补充；2026-05-08 v3.7：订单级粒度；2026-06-12 v3.11：状态过滤）：
- **负责人**：2号位
- **动作**：从`Order_Canonical`划定90天活跃根集合，按**订单粒度**（含无BOMNO订单）推送BOM展开请求到`MES_API_BOM_Request_Detail`（v5.0.21：不再去重BOMNO；含Model/MaterialCode/FactoryCode；BOMNO可空）
- **⚠️ v3.12 状态准入过滤（写死）**：生成活跃根集合前**必须筛选** `WHERE Order_Canonical.Status = 'OPEN'`（v3.12 窄口径）；只有 OPEN 状态的订单/生产指示进入 BOM Request 并生成 Task/Pegging；**CLOSED/CANCELLED 不得进入 BOM Request**
- **⚠️ v5.0.21 变更**：BOM入口解析（有BOMNO直接展开 / 无BOMNO从Model推导）由5号位Workset阶段负责；2号位仅推送基础字段
- **前提**：白天每小时增量已通过 `ERP_Order_Staging` → `sp_ValidateAndPromoteOrders` → `Order_Canonical` 路径持续更新
- **执行时间**：约1分钟

**00:05 - Order分区表装载**（2026-04-03 订单链路审计修正）：
- **负责人**：2号位
- **动作**：执行`sp_SyncOrdersToPartitionTable`，从`Order_Canonical`补齐MaterialId/ProductFamilyId/FactoryId/DomainKey/PriorityScore后装载到`Order`分区表
- **数据路径**：`Order_Canonical`（防腐层核心表）→ `Order`（业务分区表，按PlanVersionId分区）
- **执行时间**：约3-5分钟

**00:10 - 主数据三表协同同步**（2026-04-18 更新）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **契约插座（Socket）**：ERP DBA创建`ERP_Master_View`，MES DBA创建`MES_Material_View`
  - **数据插头（Plug）**：5号位创建`ext_ERP_Master_View`和`ext_MES_Material_View`
  - **数据装载（Loader）**：2号位执行`sp_SyncMasterData(@SourceType='ERP')`和`sp_SyncMasterData(@SourceType='MES')`
- **动作**：从ODS库的ext视图同步主数据到`Material`+`MaterialMapping`+`MaterialSupplyContext`三表协同（v4.0统一参数化SP，双源同构契约）
- **执行时间**：约15秒（18000条记录）

**00:10 - 资源主数据同步**（v3.4 新增 2026-04-25，与主数据同窗口并行）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **契约插座（Socket）**：MES DBA创建 `MES_APS_Resource_View`（v5.0.13 命名统一，原名 `APS_Resource_View`）；预留 `EAM_APS_Resource_View`（未来 EAM 上线时由 EAM DBA 同构新建）
  - **数据插头（Plug）**：2号位在 APS 库创建 `ext_MES_APS_Resource_View`
  - **数据装载（Loader）**：2号位执行 `sp_SyncResourceData(@SourceType='MES')`（DDL v5.0.13 新增；与 `sp_SyncMasterData(@SourceType)` 同构）
- **动作**：从 ODS 的 `ext_MES_APS_Resource_View` MERGE 全量刷新 `Resource` 表（外部设备主数据镜像，v5.0 重构后 Resource 不再手工维护）
- **FactoryCode→FactoryId 映射**：SP 内部 JOIN Factory 表完成映射；查不到的行登记 `APS_ETL_Log` 跳过（不阻塞批次，与防腐层“永不阻塞”红线一致）
- **执行时间**：< 2 秒（Resource 变化频率低，全量刷新即可，不做增量）
- **删除策略**：v1 暂**不**自动停用源端没有的旧资源（避免误删），由 2 号位审阅后手工处置；未来业务确认可扩展为“源为权威”策略
- **EAM 扩展点**：EAM 上线时用 `sp_SyncResourceData(@SourceType='EAM')`指向 `ext_EAM_APS_Resource_View`，走查步骤无需改动（双源同构契约零分叉）

**00:15 - Routing同步（v5.0拆分为5表3视图）**（2026-04-18 更新）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **契约插座（Socket）**：MES DBA维护28张离散工艺表
  - **数据插头（Plug）**：3号位创建3个ODS视图：
    - `ext_MES_APS_Routing_Operation_View`（工序节点，输出MES_ID+Model）
    - `ext_MES_APS_Routing_Dependency_View`（工序依赖DAG）
    - `ext_APS_OperationResourceEligibility_View`（工序-设备能力关联）
  - **数据装载（Loader）**：2号位分别拉取到5个APS落地表：
    - `RoutingOperation`（工序节点，2号位通过MaterialMapping将MES_ID映射为MaterialId）
    - `RoutingDependency`（工序间有向依赖）
    - `OperationResourceEligibility`（工序-设备动态能力矩阵）
    - `RoutingPlanningParam`（排程规划参数：MinBatch/MaxBatch等）
    - `RoutingStage`（阶段字典/标准阶段码，3号位契约→2号位装载）
- **动作**：从ODS库同步v5.0拆分后的工艺路线数据（原单一`Routing`表已废弃）
- **执行时间**：约8秒（8000条路线，35000道工序）
- **⚠️ v5.0架构说明**：原`Routing`表已废弃，静态ResourceGroup也已废弃。组织归属改由`ResourceOrgGroup`维护，排程能力由`OperationResourceEligibility`动态建模

**00:20 - BOM批次展开请求**（2026-04-18 更新；2026-05-08 v3.7：订单级粒度 + RequestDetailId）：
- **负责人**：2号位（发起请求）+ 5号位（ODS递归展开+后置回填）
- **动作**：
  - **【2号位】** 调用ODS库API触发批次BOM展开（`POST /api/internal/v1/ods/bom/batch/request`）；明细已在00:00写入`MES_API_BOM_Request_Detail`（订单粒度）
  - **【5号位】** 在ODS侧执行`sp_ExpandBOMBatch`递归展开，产出写入`MES_APS_BOM_Workset`（含`RequestDetailId`追溯锚点）
  - **【5号位】** 调用 `sp_EnrichBOMWorkset(@BatchNo)` 后置回填 `ChildRequiredStageCode` + `ChildRequiredFactory`（R17 工厂映射），写入 `MES_APS_BOM_Workset_StageDetail`（含 EDGE+ROOT 双层路径），异常降级登记到 `MES_APS_BOM_Workset_Issues`（含`RequestDetailId`；永不阻塞批次）
	  - **【5号位】** `sp_EnrichBOMWorkset` 末尾调用 `sp_GenerateBOMCrossFactoryEdge(@BatchNo)`，基于 StageDetail(EDGE) 按 `StageSeq` 排序 + `LEAD`窗口函数生成跨厂边，`FromFactoryCode/ToFactoryCode` 通过 `StageCode→StageDict.FactoryCode` 取得（v3.14 跨厂边生成）
  - **⚠️ v5.0.21**：无BOMNO订单在本步由5号位从 `Model`/`MaterialCode` 推导BOM入口后展开
  - **⚠️ v5.0.28 R28/R29/R30/R31**：BOMNO IS NULL时按OrderType+MaterialCode前缀分流：
    - **R28**：SALES_ORDER+ASSY% → `ProcessCodeDict`出口库过滤首层BOMNO；CN6课无出口库时取CN出口库代理
    - **R29**：SALES_ORDER+WIP%/RAW% → 直接按MaterialCode查边（原行为）
    - **R30**：SALES_ORDER+RAW%+无BOM → 外购件兜底，静默跳过
    - **R31**：PRODUCTION_INSTRUCTION+BOMNO IS NULL → 直查+必写`BOMNO_MISSING_PRODUCTION` Issues（WARN=找到/ERROR=未找到）
- **执行时间**：约15分钟（活跃订单数 → 350万行 + StageDetail派生）

**00:30 - APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW + APS_BOM_CROSS_FACTORY_EDGE_RAW 拉取**（v3.14 更新 2026-06-23）：
- **负责人**：2号位
- **Socket-Plug流程**：
  - **数据插头（Plug）**：5号位负责ODS库的BOM递归展开+StageDetail双层结果
  - **数据装载（Loader）**：2号位从ODS库同时拉取到APS库：
    - `MES_APS_BOM_Workset` → `APS_BOM_RAW`（BOM展开主表）
    - `MES_APS_BOM_Workset_StageDetail` → `APS_BOM_STAGE_PATH_RAW`（阶段路径，含StageScopeType=EDGE/ROOT）
	    - `MES_APS_BOM_Workset_CrossFactoryEdge` → `APS_BOM_CROSS_FACTORY_EDGE_RAW`（跨厂边缓存，v3.14 新增）
- **动作**：使用SqlBulkCopy拉取350万行BOM数据 + 阶段路径明细 + 跨厂边
- **执行时间**：约5分钟

**00:35 - LLC计算**：
- **负责人**：2号位
- **动作**：执行`sp_CalculateLLC`，计算低阶码（Low Level Code）
- **执行时间**：约5分钟

**00:38 - ScheduleRun 预创建**（2026-06-12 v3.12 时序修正）：
- **责任人**：3号位 / NightlyBatchOrchestrator
- **动作**：创建 `ScheduleRun` 记录（`RunType=FULL_SCHEDULE`，`Status=RUNNING`），确定并记录 `DataCutoffTime`；落库得到 `ScheduleRunId`
- **⚠️ 约束**：必须在 00:40 MES 快照同步前完成；`DataCutoffTime` 一经确定，00:40 / 00:45 / 00:50 三个 SP 必须使用同一値；02:00 排程启动时**不再重新创建** ScheduleRun
- **执行时间**：＜1分钟

**00:40 - MES工单快照同步**（2026-06-12 v3.11 新增）：
- **负责人**：2号位
- **动作**：执行`sp_SyncMESWorkOrderSnapshot(@ScheduleRunId, @DataCutoffTime)`，从 `ODS.MES_APS_WorkOrder_View`（5号位收口）同步到 `APS.MESWorkOrderSnapshot`
- **V1 口径**：只接 ODS 汇总后的工单级关系，不接每条工单变更日志；`ScheduleRunId` 作为快照分区键，全量替换本次运行旧快照
- **⚠️ @DataCutoffTime 来源（三个快照 SP 统一规则）**：由调度器（Hangfire）在 `ScheduleRun` 记录创建时统一确定并传入；00:40 / 00:45 / 00:50 三次调用必须使用**同一个 `@DataCutoffTime`**。如 ODS 视图提供 `SourceUpdatedAt` 或 `LastReportTime`，应按 `<= @DataCutoffTime` 控制数据切片；无法提供来源时间的字段允许透传，差异登记 `APS_ETL_Log`
- **执行时间**：约1-2分钟

**00:45 - MES工序进度快照同步**（2026-06-12 v3.11 新增）：
- **负责人**：2号位
- **动作**：执行`sp_SyncOperationProgressSnapshot(@ScheduleRunId, @DataCutoffTime)`，从 `ODS.MES_APS_OperationProgress_View`（5号位 UNION ALL 收口，将加工+组装大工艺子视图合并）同步到 `APS.OperationProgressSnapshot`
- **V1 口径**：不接每条报工明细，只接 ODS 汇总后工序级进度；**工序识别主字段 = `OperationName`**（不以 MES 工序编码为主）；`RemainingQty` 为持久化计算列：`CASE WHEN PlannedQty - ISNULL(GoodQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodQty,0) END`
- **执行时间**：约2-3分钟

**00:50 - MES大工艺进度快照同步**（2026-06-12 v3.11 新增）：
- **负责人**：2号位
- **动作**：执行`sp_SyncStageProgressSnapshot(@ScheduleRunId, @DataCutoffTime)`，从 `ODS.MES_APS_StageProgress_View`（5号位 UNION ALL 收口，将加工+组装大工艺子视图合并）同步到 `APS.StageProgressSnapshot`
- **V1 口径**：汇总颗粒度 = 生产指示号+物料编码+大工艺阶段码；`RemainingQty` 为持久化计算列：`CASE WHEN PlannedQty - ISNULL(GoodCompletedQty,0) < 0 THEN 0 ELSE PlannedQty - ISNULL(GoodCompletedQty,0) END`
- **⚠️ Task/Pegging 全量重算口径（写死）**：Task 和 Pegging 随新的 `PlanVersionId` 每日全量重新生成；MES 进度只用于计算当日剩余 Task 数量，**不匹配历史 TaskId**；Pegging 不跨版本复用
- **⚠️ EAM V1 预留**：`EAM_APS_Resource_View` 预留占位，V1 不读取 EAM 数据，不生成设备不可用窗口，此快照链路不受影响
- **执行时间**：约2-3分钟

**00:55 - 管道供给同步**（v3.13 2026-06-18）：
- **负责人**：2号位
- **V1 动作**：执行 `sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, @RowsAffected OUTPUT, @ErrorMessage OUTPUT)`；清空 `SupplyFact_Pipeline`；写 APS_ETL_Log SUCCESS；`@DataCutoffTime` 传值但 V1 空跑阶段不使用
- **V1 结果**：`SupplyFact_Pipeline` = 空；`ScheduleContext.PipelineSupplies` = 空集合；**空集合是正常结果，不是 ETL 失败**
- **V1.1/V2 计划**：`ext_PipelineSupply_Source_View`（统一输入）→ 多仓映射 → OUTER APPLY 规则唯一胜出 → 事务批次重建
- **执行时间**：秒级（V1 仅为 TRUNCATE + 写日志）
- **DataCutoffTime 一致性**：与 00:38 创建的 `ScheduleRun.DataCutoffTime` 同值，保证管道供给与订单/MES快照同一数据切片

**01:50 - 跨域依赖静态扫描**：
- **负责人**：2号位
- **动作**：扫描跨产品族BOM依赖，生成`Domain_Dependency`表
- **执行时间**：约5分钟

**02:00 - 排程启动**：
- **负责人**：3号位
- **动作**：Hangfire触发全量排程，快照读取APS数据库

**数据流向总览**（2026-06-15 v3.12 更新）：
```
白天  ERP.v_APS_SalesOrder → APS.ERP_Order_Staging → sp_ValidateAndPromoteOrders（v5.0.27：#TargetStagingIds锁定+三级MaterialCode解析链+OrderType未知→FAILED+BOMNO_MISSING非阻断+CustomerSegment无匹配→UNKNOWN） → APS.Order_Canonical
00:00 APS.Order_Canonical（WHERE Status = 'OPEN'）→ 活跃根集合（订单粒度）→ ODS.MES_API_BOM_Request + MES_API_BOM_Request_Detail（v5.0.21：含无BOMNO订单）
00:05 APS.Order_Canonical → sp_SyncOrdersToPartitionTable → APS.Order（分区表）
00:10 ODS.ext_ERP_Master_View + ext_MES_Material_View → sp_SyncMasterData → APS.Material + MaterialMapping + MaterialSupplyContext
00:10 ODS.ext_MES_APS_Resource_View → sp_SyncResourceData(@SourceType='MES') → APS.Resource（与主数据并行，v3.4 新增）
00:15 ODS.3个Routing视图 → APS.RoutingOperation + RoutingDependency + OperationResourceEligibility + RoutingPlanningParam + RoutingStage
00:20 2号位请求 → ODS.sp_ExpandBOMBatch → sp_EnrichBOMWorkset（展开+R17工厂映射+阶段链+Issues） → ODS.MES_APS_BOM_Workset + StageDetail + Issues
00:30 ODS.MES_APS_BOM_Workset → APS.APS_BOM_RAW + ODS.StageDetail → APS.APS_BOM_STAGE_PATH_RAW (SqlBulkCopy)；ODS.MES_APS_BOM_Workset_CrossFactoryEdge → APS.APS_BOM_CROSS_FACTORY_EDGE_RAW（v3.14 跨厂边缓存）
00:35 APS.sp_CalculateLLC → APS.APS_BOM_RAW.LLC字段
00:38 NightlyBatchOrchestrator → 创建 ScheduleRun（RunType/Status/DataCutoffTime/StrategyProfileVersionId 确定）（v3.14 策略包绑定）
00:40 ODS.MES_APS_WorkOrder_View（5号位收口）→ sp_SyncMESWorkOrderSnapshot → APS.MESWorkOrderSnapshot（v3.11 新增）
00:45 ODS.MES_APS_OperationProgress_View（5号位UNION ALL收口）→ sp_SyncOperationProgressSnapshot → APS.OperationProgressSnapshot（v3.11 新增）
00:50 ODS.MES_APS_StageProgress_View（5号位UNION ALL收口）→ sp_SyncStageProgressSnapshot → APS.StageProgressSnapshot（v3.11 新增）
00:55 sp_SyncPipelineSupply(@BatchNo, @DataCutoffTime, ...) → TRUNCATE SupplyFact_Pipeline → SUCCESS（V1空跑，不读取任何视图；已建骨架：[ext_PipelineSupply_Source_View] ← [ext_ERP_InterplantInTransit_View] ← [ODS.ERP_InterplantInTransit_View] — V1不经过）（v3.13）
01:50 APS.APS_BOM_RAW + Material + ProductFamily → APS.Domain_Dependency
02:00 APS数据库 → ScheduleContext（内存）[含 MESWorkOrderSnapshot + OperationProgressSnapshot + StageProgressSnapshot + PipelineSupplies（V1空集合）；Task/Pegging随新PlanVersionId全量重算，MES进度不匹配历史TaskId]
```

---

**⚠️ 架构红线说明**：
- **问题**：如果在02:00排程时等待CDC增量同步完成，会被网络I/O和数据库写锁阻塞，15分钟高性能排程目标破产。
- **解决**：CDC增量同步是**24小时后台常驻守护进程**（见附录），02:00排程只做**瞬间快照（Snapshot）**，直接读取数据库当前最新状态，不等待任何未完成的同步任务。

#### 步骤1.1：瞬间快照读取（不等CDC同步）

**⚠️ 前置条件**：数据准备阶段（00:05-00:55，含资源镜像刷新 v3.4、MES工单快照 v3.11、工序进度快照 v3.11、大工艺进度快照 v3.11、管道供给同步 v3.12）已完成，所有主数据、资源镜像、BOM、工艺路线、LLC、MES 生产进度快照及管道供给（V1 空结果）已同步到APS库。

**动作**：
- **【２号位】** 在02:00:00这一毫秒，直接读取APS数据库当前最新状态（已包含00:05-00:35同步的所有数据）
- **【2号位】** 使用 `SET TRANSACTION ISOLATION LEVEL READ COMMITTED SNAPSHOT (RCSI)` 或 EF Core 的 `.AsNoTracking()`，避免被CDC的写锁阻塞
- **【2号位】** 如果此时ERP正在推送大批量数据，CDC后台进程正在同步，排程引擎**不等待**，直接用"同步到一半"的状态排（下一轮会更新）

**数据来源**：APS数据库当前快照  
**数据去处**：ScheduleContext（内存）

**数据流向**：
```
APS数据库（快照读取，RCSI隔离） → 2号位.瞬间抽取 → ScheduleContext（内存）
```

**⚠️ 架构契约**：
- 排程引擎**绝对不检查**CDC同步到了哪里
- 如果ERP在02:00:01过来一个急单，只能等白天的"动态异常重排"或明天的全量排程
- CDC后台进程与排程主流程在**物理线程上彻底撕裂**

---

#### 步骤1.2：主数据快照加载（2026-04-18 更新）

**动作**：
- **【2号位】** 直接读取当前数据库中最新且生效的主数据版本：
  - BOM：`APS_BOM_RAW` + `APS_BOM_STAGE_PATH_RAW`（含 EDGE/ROOT 双层阶段路径）
  - 工艺路线：`RoutingOperation` + `RoutingDependency` + `OperationResourceEligibility`（v5.0拆分后，原`Routing`已废弃）
  - 物料主数据：`Material` + `MaterialMapping` + `MaterialSupplyContext`
  - 排程参数：`RoutingPlanningParam`、`RoutingStage`（阶段字典）
- **【2号位】** 将该版本的主数据全量无脑加载到内存沙盘 `ScheduleContext` 中，供阶段 2 拆单使用

**数据来源**：APS 主数据表（APS_BOM_RAW、APS_BOM_STAGE_PATH_RAW、RoutingOperation、RoutingDependency、OperationResourceEligibility、Material、MaterialSupplyContext等）  
**数据去处**：ScheduleContext（内存）

**数据流向**：
```
APS.APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW + RoutingOperation + RoutingDependency + Material等 → 2号位.直接加载 → ScheduleContext（内存）
```

---

#### **步骤1.3：全量数据抽取与物理分池（供需分离）**

**动作**：
- **【2号位】** 通过高性能 SQL 从 APS 数据库中，一次性拉取所有的订单（包含 MTO销售单、MTS预测单、厂间销售单等，通过 `OrderType` 字段区分）及物理库存：
  - 销售订单（Order）
  - BOM 树（`APS_BOM_RAW` + `APS_BOM_STAGE_PATH_RAW`）
  - 工艺路线（`RoutingOperation` + `RoutingDependency` + `OperationResourceEligibility`，v5.0拆分后）
  - 设备日历（ResourceCalendar）
  - 现有库存（Inventory）
  - 管道供给（PipelineSupplies，V1 空集合）
  - 在途库存（InTransit）
- **【2号位】** 在拉取库存数据时，必须基于 **【0号位】** 配置的《排产策略字典》（如该域是否允许使用在途/保税库），在 SQL 层面执行粗粒度条件过滤。将该产品族"合法的全量库存"一次性加载到内存沙盘的 `InventoryRecords` 集合中。严禁在后续推演中出现按需查库（N+1 查询）
- **【2号位】** 在拉取管道供给时，V1 读取到的 `ScheduleContext.PipelineSupplies` 为空集合。**空集合是正常结果，不是 ETL 失败**。V1.1/V2 启用真实数据后，`PipelineSupplies` 以 `IReadOnlyList<PipelineSupplyItem>` 形态加载。**V1 排程仍只使用现货库存链路**
- **【2号位】** 在内存沙盘（`ScheduleContext`）中，对拉取到的订单进行逻辑分类：
  1. **独立需求池（Demand Pool）**：仅放入明确的顶层需求，即顶层 MTO 销售单和无父节点的顶层成品 MTS 预测单。**（⚠️架构红线：绝对不能在此刻放入任何厂间订单，因为此时无从判断其血缘归属）**
  2. **供给池（Supply Pool）**：放入现有物理库存，并放入作为预期供给的所有中间工艺 MTS 加工单和所有的跨厂调拨单/厂间销售单
- **【2号位】** 在内存中为供给池里的预期单据赋予极低的基础备货分数（如 10 分）

**数据来源**：APS 数据库（Order、APS_BOM_RAW、RoutingOperation、Resource、Inventory等）  
**数据去处**：ScheduleContext（内存，包含独立需求池和供给池的逻辑分类）

**数据流向**：
```
APS数据库（多表联合查询） → 2号位.高性能SQL → ScheduleContext（内存） → 2号位.逻辑分类 → 独立需求池 + 供给池
```

---

#### **步骤1.3.5：MES实绩防呆与质检（4道防线）**

**负责人**：5号位 + 2号位

**动作**：在拉取历史Task数据时，必须执行4道防线，防止脏数据污染排程沙盘。

**⚠️ 防线1：容差结案（短交结案）**
- **触发条件**：Task状态为"进行中"，但实际完工数量在计划数量的95%-105%区间内
- **【5号位】** 实现 `IToleranceClosureRule` 接口，判定是否在容差范围内
- **【5号位】** 返回 `ToleranceClosureVoucher` 凭证，标注应结案的Task ID
- **【2号位】** 根据凭证，将Task状态修改为"已完成"，释放机床资源
- **超出容差范围**：打上 `QUANTITY_MISMATCH` 标签，推送PMC看板人工裁决

**⚠️ 防线2：漏报工反冲（倒推结案）**
- **触发条件**：后工序已开工，但前工序未报工完成（工序链断裂）
- **【5号位】** 实现 `IBackflushRule` 接口，识别断链的前工序Task
- **【5号位】** 返回 `BackflushVoucher` 凭证，标注应倒推结案的Task ID
- **【2号位】** 根据凭证，自动将前工序标记为"已完成"，防止工序链断裂
- **架构原则**：只做状态修正，不修改数量（坚守1:1刚性流转）

**⚠️ 防线3：超时熔断（僵尸清理）**
- **触发条件**：Task开工时间超过3倍标准工时，仍未报工完成
- **【5号位】** 实现 `ITimeoutMeltdownRule` 接口，识别超时僵尸Task
- **【5号位】** 返回 `TimeoutMeltdownVoucher` 凭证，标注应熔断的Task ID
- **【2号位】** 根据凭证，强制释放机床资源，将Task打上 `TIMEOUT_ZOMBIE` 标签
- **后续处理**：推送PMC看板，由PMC确认是继续等待还是触发重排

**⚠️ 防线4：人工裁决（PMC看板）**
- **触发条件**：所有异常Task（QUANTITY_MISMATCH、TIMEOUT_ZOMBIE等）
- **【3号位】** 每日早班（07:00）将异常Task推送至PMC看板
- **【PMC】** 手动确认结案、手动修正数量、或触发局部重排
- **架构原则**：系统只负责识别和标记，最终决策权归PMC

**凭证交互模式**：
```
5号位.防呆规则 → 返回Voucher凭证 → 2号位.统一执行状态变更 → 沙盘更新
```

**数据来源**：Task表（历史实绩）  
**数据去处**：ScheduleContext（清洗后的干净数据）

**⚠️ 架构红线**：
- 5号位绝对禁止直接修改Task状态，必须返回凭证
- 2号位统一执行所有状态变更，确保事务一致性
- V1.0绝对不考虑报废折算，数量1:1刚性流转

---

#### **步骤1.4：排产策略配置装载**

**动作**：
- **【2号位】** 从配置表中读取 **【0号位】** 在界面上配置的"排产策略"
  - 各产品族的 Frozen/Firm 区天数
  - 是否考虑在途库存
  - 插单优先级规则
  - 最小批量（MinBatchSize）
  - 换型时间（SetupTime）
- **【2号位】** 将策略配置作为"规则字典"放入内存沙盘

**数据来源**：APS.SchedulingStrategy 表  
**数据去处**：ScheduleContext（内存）

**数据流向**：
```
APS.SchedulingStrategy表 → 2号位.配置加载 → ScheduleContext.策略字典（内存）
```

**业务意义**：将所有的 I/O（读写磁盘）操作前置，确保后续的核心计算在纯内存中进行，不受数据库性能拖累。

---

### ⚙️ 阶段2：供需匹配与造单（Pegging，生成作战单元）

**负责人**：2号位（引擎框架） + 5号位（业务规则插件）

#### **步骤2.0：阵前阅兵与订单综合打分（Order Prioritization）**

**动作**：
- **【2号位】** 在执行 BOM 树遍历之前，将"独立需求池"中的所有顶层订单（仅含 MTO 销售单、MTS 成品备货单）传递给 **【5号位】**
- **【5号位】** 调用 `IOrderPrioritizationRule` 插件，在纯内存中执行 O(n) 的极速算分：
  - **MTO（销售单）**：根据 VIP 等级、交期紧急度等表层属性算分
  - **MTS（成品补库/独立需求）**：极速 O(1) 寻址该成品的当前可用库存，结合安全库存（SS）计算缺口紧急度算分
  - **（⚠️架构红线：严禁在此阶段遍历 BOM 树或向下探测子件库存，确保极速运算）**
- **【5号位】** 算完后，返回包含各订单绝对总分的 `OrderScoreResult` 凭证
- **【2号位】** 根据分数执行降序排列（OrderByDescending），确立最终的抢料出场顺序，随后进入步骤 2.1 逐一进行 BOM 拆解

**数据来源**：独立需求池（内存，只读传入5号位）  
**数据去处**：OrderScoreResult凭证 → 排序后的订单队列（内存）

**数据流向**：
```
独立需求池 → 5号位.纯计算(返回OrderScoreResult凭证) → 2号位.降序排列 → 确立抢料顺序
```

**业务意义**：确保高优先级订单（如VIP客户、交期紧急、库存缺口大的MTS）优先抢占库存，避免低分订单"插队"导致缺料。

---

#### **步骤2.1：BOM树遍历与库存扣减（凭证交互模式 + 优先级继承）**

**⚠️ 架构红线：只记账不造单**

在此阶段，5号位绝对禁止边扣减边生成 Task。5号位只负责当会计，在 ERP 现成的 MTS 单据上维护一个**《血缘分配账本（Pegging Ledger）》**。例如：某个 MTS-001（1000件）被分配给了两个需求：份额A=200件@VIP客户900分，份额B=800件@备货10分。在此阶段，内存中没有任何 Task 被物理生成。

**【2号位与5号位交互契约】**：
- **【2号位】** 拿着"独立需求池"中排好序的顶层订单（如 900 分的 MTO），对 BOM 树进行深度优先遍历（DFS）
- **【2号位】** 遍历到每个零件需要扣减库存时，向 **【5号位】** 传入净需求量与只读的"供给池"列表
- **【5号位】** 作为"无副作用的纯计算器"，在供给池中寻找物理库存或 MTS/厂间订单进行匹配：
  - 标准 FIFO（先进先出）
  - 在途库存折算（调用 `IInTransitRule` 判断在途物料何时可用）
  - 替代料规则（如果主料缺货，是否使用替代料）
  - **（新增逻辑）** 若匹配到供给池中的单据（如 MTS 加工单），计算该单据应继承的顶层高优先级分数
- **【5号位】** 算完后，严禁直接修改内存库存余额或优先级，必须向 **【2号位】** 返回一张包含**【净需求数量】**、**【库存分配明细】**与**【目标优先级（TargetPriority）】**的扣减凭证（`PeggingResult`）
- **【2号位】** 拿到该凭证后，在主干流程中统一掌控绝对的数据修改权：
  - 执行库存余额的安全扣减
  - 统一在沙盘中写入 `PeggingLinks` 供需血缘追溯连线
  - **（新增逻辑）** 统一更新对应单据的优先级分数（如从 10 分飙升至 900 分）
  - **在该单据的《血缘分配账本》中记录份额**：例如 MTS-001 的账本中新增一条：份额A=200件@900分（来自VIP客户）

**数据来源**：ScheduleContext.InventoryRecords + 供给池（内存，只读传入5号位）  
**数据去处**：ScheduleContext.PeggingGraph + 单据优先级（内存，由2号位写入）

**数据流向**：
```
独立需求池 → 2号位.DFS遍历 → 5号位.纯计算(返回PeggingResult凭证含TargetPriority) → 2号位.统一执行扣减、写PeggingLinks、更新优先级
```

---

#### **步骤2.2：物料短缺识别与采购建议（凭证模式）**

**【2号位与5号位交互契约】**：
- **【2号位】** 在扣减过程中发现库存不足时，调用 **【5号位】** 的 `IMaterialShortageRule` 插件
- **【5号位】** 作为"无副作用的纯计算器"，识别缺料并计算采购建议
- **【5号位】** 算完后，严禁直接修改内存沙盘，必须向 **【2号位】** 返回一张包含**【PR对象】**与**【缺料订单标记】**的凭证（`MaterialShortageResult`）
- **【2号位】** 拿到该凭证后，在主干流程中统一掌控绝对的数据修改权：
  - 将 PR 对象存入 `ScheduleContext.PurchaseRequisitions` 内存集合
  - 标记缺料订单为 `MATERIAL_SHORTAGE` 状态

**⚠️ 架构红线**：PR 作为排程的"副产品"，必须在内存中累积，禁止在此步骤写入数据库。所有 PR 将在阶段5与 Task 一起批量落盘。

**数据来源**：ScheduleContext.Inventory（内存，只读传入5号位）  
**数据去处**：ScheduleContext.PurchaseRequisitions（内存集合，由2号位写入）

**数据流向**：
```
ScheduleContext.Inventory → 5号位.纯计算(返回MaterialShortageResult凭证) → 2号位.统一执行PR存储与订单标记
```

---

#### **步骤2.3：换型属性标注（凭证模式）**

**【2号位与5号位交互契约】**：
- **【2号位】** 在生成 Task 时，调用 **【5号位】** 的 `ISetupAttributeRule` 插件
- **【5号位】** 作为"无副作用的纯计算器"，从工艺路线或物料属性中提取换型关键属性：
  - 模具编号（注塑工艺）
  - 颜色代码（表面处理工艺）
  - 材质规格（型材挤压工艺）
- **【5号位】** 算完后，严禁直接修改 Task 对象，必须向 **【2号位】** 返回一张包含**【换型属性】**的凭证（`SetupAttributeResult`）
- **【2号位】** 拿到该凭证后，在主干流程中统一掌控绝对的数据修改权：
  - 为 Task 打上 `SetupAttribute` 标签

**数据来源**：ScheduleContext.Routing/Material（内存，只读传入5号位）  
**数据去处**：ScheduleContext.Tasks（内存，由2号位写入属性）

**数据流向**：
```
ScheduleContext.Routing/Material → 5号位.纯计算(返回SetupAttributeResult凭证) → 2号位.统一执行Task属性标注
```

---

#### **步骤2.4：同域跨厂厂间订单转真实发货Task（凭证模式）**

**⚠️ 架构红线说明**：
- **此步骤只处理同域跨厂场景**：订单属于同一产品族，但半成品和成品在不同物理工厂生产
- **绝对禁止在此步骤处理跨产品族依赖**：跨产品族依赖已在"阶段0.5"通过静态SQL扫描固化到`DomainDependency`表，排程时通过"虚拟库存硬约束"传递（见第三部分场景3）

**【2号位与5号位交互契约】**：
- **【2号位】** BOM 拆解遇到跨厂时，呼叫 **【5号位】**
- **【5号位】** 在供给池中找到对应的"厂间销售订单"（ERP 提前下达的真实单据），将其作为供给进行匹配
- **【5号位】** 计算物流时间（运输时间 + 检验时间），并向 **【2号位】** 返回包含**【真实物流发货Task生成指令】**的凭证（需携带该厂间订单的 ERP 原始单号）
- **【2号位】** 接收凭证，在内存中统一生成带有 ERP 单号的 `ShippingTask`，并串联入时间链条，供阶段3物理排程

**示例**：
```
订单SO001：产品族X（整机）
- A厂生产半成品（产品族X）
- ERP提前下达厂间销售订单：SO-Inter-001（A厂→B厂）
- B厂继续生产成品（产品族X）

→ 2号位BOM拆解遇到跨厂，呼叫5号位
→ 5号位在供给池中找到厂间订单 SO-Inter-001，计算物流时间（2.5天）
→ 5号位返回凭证：ShippingTask生成指令（携带ERP单号 SO-Inter-001）
→ 2号位接收凭证，生成真实发货Task（ERP单号：SO-Inter-001，物流耗时：2.5天）
```

**数据来源**：供给池中的厂间销售订单（内存，只读传入5号位）  
**数据去处**：ScheduleContext.Tasks（内存，由2号位写入真实ShippingTask）

**数据流向**：
```
供给池.厂间订单 → 5号位.纯计算(返回ShippingTask生成凭证含ERP单号) → 2号位.统一生成真实发货Task
```

**业务意义**：废除纯虚拟的转移任务，排程结果落盘后可直接生成携带 ERP 真实单号的厂间发货指令，指导车间/物流按时发车。

---

#### **步骤2.5：孤儿单据级联扫尾与拓扑拆解（Cascading Sweep）**

**动作**：顶层订单第一波拆解完毕后，**【2号位】** 必须按照严格的业务位阶，对"供给池"分两步执行级联扫尾：

**第一阶梯（外部契约扫尾）**：
- **【2号位】** 优先扫描供给池，将未被绑定的**"厂间销售订单（跨厂调拨单）"**强行转入"独立需求池"，保留其底薪分数（如 10 分）
- **【2号位】** 以它们为根节点启动 DFS 拆解。**（业务意义：它们在向下拆解时会自然扣减内部的加工 MTS 单据，防止跨厂发货与内部备货发生负荷重复计算）**

**第二阶梯（内部多级备货余量的"瀑布式"扫尾）**：
- 厂间订单全部扫尾拆解完成后，**【2号位】** 扫描供给池中依然未被绑定的各层级孤儿单据（如为了凑 OP 点下达的 加工 MTS、锻造 MTS、型材 MTS 等）
- **（⚠️架构红线：基于低阶码的拓扑降维）**：**【2号位】** 必须将这些孤儿单据按 BOM 结构从顶层向底层（低阶码 LLC 从高到低）进行排序
- 排序后，**【2号位】** 开启循环，将它们逐级转入"独立需求池（10 分）"并立即启动 DFS 向下拆解

**业务效果演示**：
```
示例：三级工艺链的瀑布式扫尾
- 供给池中有3个孤儿MTS：加工MTS(100件，LLC=0)、锻造MTS(150件，LLC=1)、型材MTS(200件，LLC=2)
- BOM关系：1件加工成品需要1件锻造半成品，1件锻造半成品需要1件型材原料

按LLC排序后的扫尾循环：

第1轮：加工MTS(100件) 转入独立需求池 → DFS拆解到底
  → 消耗锻造MTS 100件（供给池剩余：锻造MTS 50件）
  → 继续向下拆解，消耗型材MTS 100件（供给池剩余：型材MTS 100件）
  → 生成加工Task、锻造Task、型材Task

第2轮：锻造MTS(50件) 转入独立需求池 → DFS拆解到底
  → 消耗型材MTS 50件（供给池剩余：型材MTS 50件）
  → 生成锻造Task、型材Task

第3轮：型材MTS(50件) 转入独立需求池 → DFS拆解到底
  → 生成型材Task

业务效果：由于严格按层级循环，排在前面的高层级余量（如 加工 MTS）变为需求后，在向下拆解时，会自然去供给池"吃掉"一部分低层级的余量（如 锻造 MTS、型材 MTS）。等循环真正走到下一级（锻造 MTS）时，它可能已经被消耗光了。此机制完美避免了 N 层复杂工艺链下的"负荷虚假翻倍"，实现一波收敛。
```

**数据来源**：供给池中未被绑定的剩余单据（内存）  
**数据去处**：ScheduleContext.Tasks（内存，低优先级Task）

**数据流向**：
```
第一阶梯：供给池.厂间订单 → 2号位.转入独立需求池 → 2号位.DFS拆解到底 → 消耗内部MTS
第二阶梯：供给池.多级MTS → 2号位.按LLC排序 → 2号位.逐级转入+DFS拆解到底 → 瀑布式消耗 → 低优先级Task
```

**业务意义**：
- **外部契约优先**：厂间订单优先扫尾，确保跨厂协同不受内部备货干扰
- **避免负荷重复**：通过拓扑排序和瀑布式扫尾，避免多级工艺链下的机床负荷虚假翻倍
- **一波收敛**：高层级余量自然消耗低层级余量，确保所有 ERP 单据都被转化为底层 Task
- **见缝插针**：低分单据将在阶段3被1号位塞入机床的"产能白地"中

---

#### **步骤2.6：基于血缘账本的统一拆批与Task实例化（两阶段生成法）**

**动作**：在所有需求连线和孤儿扫尾全部结束后，**【2号位】** 开始集中为所有 MTS/MTO 单据生成底层的执行 Task。

**第一阶段：按血缘分堆**
- **【2号位】** 读取每个单据的《血缘分配账本（Pegging Ledger）》
- 严格按照不同份额继承的优先级分数，将单据划分为不同的逻辑块

**示例**：
```
MTS-001（1000件）的血缘账本：
- 份额A：200件 @ 900分（来自VIP客户）
- 份额B：800件 @ 10分（基础备货）

→ 2号位将 MTS-001 划分为 2 个逻辑块：
  - 逻辑块1：200件，优先级900分
  - 逻辑块2：800件，优先级10分
```

**第二阶段：5号位动态拆批裁决**
- **【2号位】** 针对每一个逻辑块，呼叫 **【5号位】** 的 `ITaskSplitRule`（任务拆批规则）插件
- **【5号位】** 读取 PMC 配置的拆批规则（MOQ/EOQ、瓶颈设备策略等）
- **【5号位】** 根据当前设备负荷、下游交货压力等实时因素，动态下发《拆批凭证（TaskSplitVoucher）》
- 凭证内容：决定是保持原样，还是切碎成多个符合经济批量的碎片

**示例**：
```
逻辑块2：800件，优先级10分

5号位读取规则：
- MOQ = 200件
- EOQ = 500件
- 当前设备负荷 = 85%（较高）
- 下游紧急订单 = 2个

5号位裁决：
- 拆分成 2 个批次：400件 + 400件
- 理由：设备负荷高，拆分后可插空排程

返回凭证：
- 批次1：400件，优先级10分
- 批次2：400件，优先级10分
```

**第三阶段：2号位当苦力造沙子**（2026-04-18 补充StageDetail说明）
- **【2号位】** 根据凭证，执行单纯的 for 循环
- **【2号位】** 读取 `APS_BOM_STAGE_PATH_RAW`（含 `StageScopeType`=EDGE/ROOT 双层阶段路径），获取该物料在BOM树中的阶段顺序（如：注塑→表面处理→装配）
- 将逻辑块实例化为一个个带有 OP10, OP20 工序的物理 Task，工序串接顺序依据 `RoutingOperation` + `RoutingDependency` 的DAG关系
- 赋予对应的优先级分数
- 装入沙盘交接给 **【1号位】**
- **⚠️ v5.0.8说明**：EDGE路径表示相邻工序间的直接连接，ROOT路径表示从根产品到各阶段的完整派生路径。1号位在排程时可利用ROOT路径快速定位物料所处的工艺阶段

**数据来源**：血缘分配账本（内存）  
**数据去处**：ScheduleContext.Tasks（内存，物理Task对象）

**数据流向**：
```
血缘账本 → 2号位.按份额分堆 → 5号位.动态拆批裁决(返回TaskSplitVoucher) → 2号位.for循环生成物理Task → 装入沙盘
```

**⚠️ 架构红线**：
- **只有中间库 MTS 允许拆批**：MTO 订单绝对禁止拆分（客户订单必须整单交付）
- **5号位必须读取 PMC 配置的规则**：禁止硬编码拆批逻辑
- **拆批发生在 Task 生成阶段**：不是在 Pegging 阶段，确保血缘关系清晰

**业务意义**：
- **两阶段生成法**：先建立血缘关系（Pegging），再统一生成 Task，逻辑清晰
- **动态拆批优化**：根据实时设备负荷和交货压力，优化批次大小
- **完美继承 ERP 体系**：尊重 ERP 的固定批量单据，通过拆批优雅切分为车间可执行的 Task

---

**阶段2产出**：
- ✅ 在内存中生成了 10 万个确定了"加工数量"和"加工路线"的生产任务（Task）
- ✅ 所有任务的"开工时间"和"完工时间"都是空的
- ✅ **架构底线**：完美继承 ERP 的固定批量单据体系，通过 5号位的动态拆批裁决，将庞大的 ERP 单据优雅切分为适合车间执行的物理 Task，确保既不错过紧急订单，又最大化保证车间的连续生产意愿

**⚠️架构名词对齐**：本系统中的 Task 指代单道工序任务。2号位必须根据物料的 Routing，将 1 个 MTS/MTO 单据拆解为 N 个前后相连的工序级 Task。

**业务意义**：
- 将稳定不变的"拆单流程"与天天变化的"车间扣减规则"彻底解耦
- 未来车间有新规矩，只需新增一个插件，不影响主流程代码
- 两阶段生成法（先 Pegging 后 Task 实例化）确保血缘关系清晰，拆批逻辑可控

---

#### **步骤2.7：Pegging 补强 — 算法Pegging与物理Pegging表分离（v3.14 2026-06-23）**

**物理 Pegging 表**（数据库表）是 Task-to-Task 供需血缘表，记录 `UpstreamTaskId/DownstreamTaskId`。它在 Task 和 ShippingTask 生成之后由 2号位写入。

**算法层 Pegging** 发生在 Task 生成之前，是内存中的供需匹配、供给扣减、Pegging Ledger。库存、ZP/BP 出口库、Received 汇总、管道在途等**非 Task 供给**不写入物理 Pegging 表，而是写入 `PeggingSupplyAllocation`。

```text
算法层 Pegging / Voucher
    ↓
PeggingSupplyAllocation（库存/在途/Received等非Task供给分配细账）
    ↓
Task / ShippingTask 实例化
    ↓
物理 Pegging 表（Task-to-Task 血缘）
```

#### **步骤2.8：跨厂供给模式判定（v3.14）**

两类跨厂供给模式：

| 模式 | 中文 | 判定 | 供给寻找 |
|------|------|------|---------|
| `STAGE_HANDOFF` | 大工艺接续型 | ChildMaterialCode+ToFactoryCode 存在M库承接点 | 接收工厂XC→M库→在途→上游M库→新增生产 |
| `INTER_FACTORY_ORDER` | 厂间出荷指示型 | 无M库承接点，或5号位裁决 | **先查在途**→再查ZP/BP Received→再进入生产工厂排产 |

**M库判定**：2号位通过 `MaterialSupplyContext + MES_ProcessCode_View.ERPProperty` 生成内存索引 `MaterialCode+FactoryCode→HasMStock`。ERPProperty 来自 ERP 真实属性值，5号位同步透出维护。

**INTER_FACTORY_ORDER 供给寻找顺序**（P0）：
1. 从 Order 表锁定当前厂间出荷指示号（须未完成状态）
2. 先查 `SupplyFact_Pipeline` 中 `SourceDocumentNo=当前出荷指示号` 的在途
3. 不足部分查 `ERP_Received_ByDocument_View`（`DocumentType=SHIPPING_INSTRUCTION AND DocumentNo=当前出荷指示号 AND WarehouseCode对应ERPProperty IN ('ZP','BP')`）
4. 仍不足则进入生产工厂内部排产，补足出荷指示号缺口

**红线**：先查在途、再查 Received 出口库。已在途的数量通常已从出口库发出，先查出口库可能重复计算。

**ZP/BP 出口库规则**：ZP/BP 库存默认不是通用可用库存。仅当 `DocumentNo=当前出荷指示号 AND DocumentType=SHIPPING_INSTRUCTION AND 出荷指示号未完成` 时，对应 ReceivedQty 才进入供给分配。

**示例1：INTER_FACTORY_ORDER**（CN需D-001 100，来源TJ，出荷号SHIP-TJ-CN-001）
- SupplyFact_Pipeline: SHIP-TJ-CN-001 在途40 → 剩余60
- ERP_Received: TJ_ZP|ZP|SHIPPING_INSTRUCTION|SHIP-TJ-CN-001|35 → 剩余25
- 进入TJ内部排产：新增生产需求25

**示例2：STAGE_HANDOFF**（CN需D-001 120，边TJ_MACH→CN_SURF，CN有M库）
- CN M库=20 | CN XC=30 | TJ→CN在途=40 | TJ M库=25 | 新增生产=5
- 生成：CN_SURF Task 100、TJ_MACH Task 5、TJ→CN ShippingTask 30

---

### ⚔️ 阶段3：纯粹的时空时序推演（排俄罗斯方块）

**负责人**：1号位（排程算法核心）

#### **步骤3.1：任务优先级排序**

**动作**：
- **【1号位】** 接收 **【2号位】** 传递的 10 万个 Task（内存对象）
- **【1号位】** 解析策略配置中的优先级规则
- **【1号位】** 对任务进行优先级降序排列（Priority DESC）

**数据来源**：ScheduleContext.Tasks（内存）  
**数据去处**：1号位内部优先级队列（内存）

**数据流向**：
```
ScheduleContext.Tasks → 1号位.优先级队列排序 → 内部优先级队列
```

---

#### **步骤3.2：时间槽寻址与排程**

**动作**：
- **【1号位】** 对每个 Task，在设备日历的空闲时间槽中执行"倒排寻址"或"撞墙翻转正排"
- **【1号位】** 使用 `IntervalTree`（时间线段树）进行极速检索
- **【1号位】** 考虑前置约束（上游Task必须完成）
- **【1号位】** 考虑设备日历（班次、节假日、维修时间）

**数据来源**：1号位内部优先级队列 + ScheduleContext.ResourceCalendar（内存）  
**数据去处**：Task.StartTime / Task.EndTime（内存）

**数据流向**：
```
优先级队列 + ResourceCalendar → 1号位.时间槽寻址算法 → Task.StartTime/EndTime
```

---

#### **步骤3.3：换型优化启发式**

**动作**：
- **【1号位】** 当算法在某台设备的时间槽中寻找下一个可排产的 Task 时
- **【1号位】** 如果存在多个候选任务，优先选择与上一任务 `SetupAttribute` 相同的任务
- **【1号位】** 这种局部微调不会破坏优先级大框架，但能显著提升瓶颈设备产能（5-15%）

**示例**：
```
注塑机刚完成模具A的任务
队列中有：模具A的任务、模具B的任务
→ 算法优先排模具A，避免换模时间损失
```

**数据来源**：Task.SetupAttribute（内存）  
**数据去处**：Task排序调整（内存）

**数据流向**：
```
Task.SetupAttribute → 1号位.换型优化算法 → Task排序微调
```

---

**阶段3产出**：
- ✅ 将这 10 万个 Task 严丝合缝地填入时间轴
- ✅ 所有 Task 获得了精确的开工与完工时间
- ✅ 通过换型优化，瓶颈设备（注塑、表面处理、型材挤压）的换型次数显著减少，产能利用率提升

**业务意义**：
- 让算法引擎保持绝对的纯粹性，专心压榨 CPU 算力
- 这是实现秒级/分钟级排程的核心底座
- 换型优化在不增加算法复杂度的前提下，通过简单的启发式规则即可获得显著的产能提升

---

### 🧠 阶段4：业务回填与战报生成（后置翻译）

**负责人**：5号位（业务规则） + 3号位（战报生成）

#### **步骤4.1：交期违约检查**

**动作**：
- **【5号位】** 调用 `IValidationRule` 插件，对排程结果进行业务校验
- **【5号位】** 检查订单交期违约：比较 `Task.EndTime` 与 `Order.CustomerDueDate`
- **【5号位】** 标记延期订单（`DELAYED` 状态，红色警告）
- **【5号位】** 计算延期天数，生成客户通知清单

**⚠️ 架构说明**：1号位的有限产能寻址算法（Finite Capacity Scheduling）已保证设备负荷率≤100%。如果出现>100%，那是算法Bug，不是业务验证的范畴。业务验证关注的是"订单是否延期"，而非"设备是否超负荷"。

**数据来源**：ScheduleContext.Tasks + ScheduleContext.Orders（内存）  
**数据去处**：ScheduleContext.ValidationResult（内存）

**数据流向**：
```
ScheduleContext.Tasks + Orders → 5号位.交期违约检查 → ValidationResult（延期订单清单）
```

---

#### **步骤4.2：最晚需料时间推算**

**动作**：
- **【5号位】** 根据 Task 的开工时间减去采购提前期
- **【5号位】** 推算出 `latest_need_time`（最晚需料时间）
- **【5号位】** 更新采购建议的交期要求

**数据来源**：Task.StartTime（内存） + MaterialSupplyContext.LeadTimeDays（内存，v5.0仓库级上下文）  
**数据去处**：PurchaseRequisition.RequiredDate（缓冲表）

**数据流向**：
```
Task.StartTime + MaterialSupplyContext.LeadTimeDays → 5号位.时间推算 → PR.RequiredDate
```

---

#### **步骤4.3：轻量级枚举标签打标（不生成文本战报）**

**⚠️ 架构红线说明**：
- **问题**：在阶段4让3号位（接口域）直接在 `ScheduleContext`（内存沙盘）里生成文本战报，违反了模块边界。`ScheduleContext` 是1/2/5号位的私有领地，3号位不应触碰推演内存。
- **解决**：5号位只在Task对象上打**轻量级枚举标签**（ReasonCode + 关联ID），不组装文本。战报生成移到"阶段5落盘后"由3号位异步完成。

**动作**：
- **【5号位】** 在Task对象上打枚举标签（不是文本）：
  - `Task.ReasonCode`：枚举值（如：1=物料短缺，2=产能不足，3=交期紧张）
  - `Task.ReasonMaterialId`：关联的物料ID（如果是缺料）
  - `Task.ReasonResourceId`：关联的设备ID（如果是产能不足）
- **【5号位】** 示例代码契约（C#）：
  ```csharp
  task.ReasonCode = ReasonCode.MaterialShortage;  // 枚举：1
  task.ReasonMaterialId = 10086;                  // 关联物料ID：轴承
  task.ReasonResourceId = null;                   // 不相关
  ```

**数据来源**：ScheduleContext.Tasks（内存）  
**数据去处**：Task对象的枚举字段（内存）

**数据流向**：
```
ScheduleContext.Tasks → 5号位.打枚举标签 → Task.ReasonCode/ReasonMaterialId/ReasonResourceId（内存）
```

**⚠️ 架构契约**：
- 5号位**只打标签**，不翻译成文本
- 3号位**不触碰**推演期的内存沙盘
- 战报文本生成移到"阶段5.5"（落盘后由3号位异步完成）

**业务意义**：保证内存沙盘的纯洁性，避免模块耦合和OOM责任扯皮。

---

### 💾 阶段5：极速落盘与零停机发布（完美收尾）

**负责人**：2号位（数据落盘） + 3号位（版本切换） + 5号位（冻结判定）

---

#### **步骤5.0：落库前的滑动窗口冻结判定（下发MES的唯一阀门）**

**负责人**：5号位 + 2号位

**动作**：
- **【2号位】** 在执行 SqlBulkCopy 写入数据库的前一秒，呼叫 5号位执行冻结判定
- **【5号位】** 实现 `IFreezeZoneRule` 接口，执行时间审判：
  - 读取当前域的冻结区天数配置（如：未来3天）
  - 遍历沙盘中所有今天刚被 1号位排好时间的新 Task
  - 如果 `Task.PlannedStartTime` 落入未来3天内 → 打上 `IsFrozen = true` 标签
  - 如果 `Task.PlannedStartTime` 在3天外 → 打上 `IsFrozen = false` 标签
- **【5号位】** 返回 `FreezeZoneVoucher` 凭证，标注所有应冻结的 Task ID
- **【2号位】** 根据凭证，统一执行 `IsFrozen` 标签的批量更新

**⚠️ 架构红线**：
- **冻结区是滑动窗口**：每天 02:00 自动向前推进，无需 PMC 审批
- **IsFrozen = true 是下发 MES 的唯一阀门**：只有带此标签的 Task 才会被 3号位推送到车间
- **3天外的计划默默落库**：车间不可见，等待明晚 02:00 被销毁重排

**凭证交互模式**：
```
5号位.时间审判 → 返回FreezeZoneVoucher → 2号位.批量打标 → 沙盘更新
```

**数据来源**：ScheduleContext.Tasks（内存）  
**数据去处**：Task.IsFrozen 字段

**数据流向**：
```
1号位排好的Task → 5号位.冻结判定 → FreezeZoneVoucher → 2号位.打标 → Task.IsFrozen字段
```

---

#### **步骤5.1：SqlBulkCopy 批量落盘**

**动作**：
- **【2号位】** 使用 `SqlBulkCopy` 技术，在 30 秒内将所有内存对象批量写入数据库：
  - 10 万条 Task 结果
  - 数千条 PurchaseRequisition（采购建议）
  - ValidationResult（延期订单清单）
- **【2号位】** 写入到"草稿版本"分区（`PlanVersionId = 20260227_020000`）
- **【2号位】** 采用中转堆表（Staging Table）隔离写入，避免锁冲突
- **【2号位】** v3.8 新增：将 1号位推演期间在内存中产出的 `ExplanationFactDraft` 列表，与 Task/Pegging 同批次批量落库到 `ScheduleExplanationFact` 表（1号位禁止直接写 DB，统一由 2号位批量处理）
- **【2号位】** v3.13 更新：批量落库完成后，回填 `ScheduleRun.Status=COMPLETED` + `ScheduleRun.CompletedAt`；同时更新 `PlanVersion.Status=ACTIVE` + `PlanVersion.ActivatedAt` + `PlanVersion.ActivatedBy='SYSTEM'`

**⚠️ 架构红线**：这是整个排程流程中**唯一允许的 DB I/O 操作**。阶段1-4的所有中间产物（Task、PR、战报、ExplanationFact）都必须在内存中累积，最后一次性批量落盘。

**数据来源**：ScheduleContext（内存中的所有对象）  
**数据去处**：APS 数据库（草稿版本分区）

**数据流向**：
```
ScheduleContext.Tasks → 2号位.SqlBulkCopy → APS.Task表
ScheduleContext.PurchaseRequisitions → 2号位.SqlBulkCopy → APS.PurchaseRequisition表
ScheduleContext.ValidationResult → 2号位.SqlBulkCopy → APS.DelayedOrder表
ScheduleContext.ExplanationFactDrafts → 2号位.SqlBulkCopy → APS.ScheduleExplanationFact表（v3.8新增）
落库完成 → 2号位回填 ScheduleRun.Status=COMPLETED + PlanVersion.Status=ACTIVE（v3.13 四表收敛）
```

> **v3.8 架构说明**：`ScheduleExplanationFact` 的后处理读模型（`OrderScheduleSummary` / `ResourceLoadSummary` / `PlanKpiSummary`）在 Task 落库完成后**异步**生成（非阻塞当前批次），由 2号位 BackgroundService 处理；阶段一 DDL 骨架就绪，Batch 3 补齐完整索引与约束。

---

#### **步骤5.2：版本激活（v3.13 四表收敛）**

**动作**（`FULL_SCHEDULE` 凌晨主链）：
- 落库完成后，在步骤5.1中已同步完成版本激活：`PlanVersion.Status=ACTIVE` + `ActivatedAt` + `ActivatedBy='SYSTEM'`
- 正式采用直接通过 `PlanVersion.Status = ACTIVE` 表示，不再使用独立的 `System_Active_Version` 表

> **v3.13 四表收敛（2026-06-23）**：
> - `FULL_SCHEDULE` 完成后，2号位在步骤5.1 落库时同步更新 `PlanVersion.Status=ACTIVE`
> - `MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` / `SIMULATION` / `INSERT_ORDER_WHATIF` 类产出的 `PlanVersion` 默认状态为 **CANDIDATE**
> - 正式激活须 3号位通过版本激活 API 将 `PlanVersion.Status` 改为 `ACTIVE`
> - **落盘（步骤5.1）** 与 **激活（步骤5.2）** 是两个解耦的独立动作——任何 RunType 完成落盘后版本都存在，但只有 `FULL_SCHEDULE` 默认走步骤5.2；其余 RunType 停在步骤5.1，等待用户或系统显式激活
> - **⚠️ 禁止**：仿真版本 / WHATIF 版本 / 人工重排版本 自动覆盖当前正式版本

---

#### **步骤5.3：前端刷新广播**

**动作**：
- **【3号位】** 向全厂发送更新广播（通过 SignalR）
- **【4号位】** 前端甘特图瞬间刷新，显示新版本计划

**数据流向**：
```
3号位.SignalR广播 → 4号位.前端自动刷新
```

**业务意义**：
- 用户在白天看计划、拖拽排产时，即使后台正在进行 10 万级的数据重排
- 前端看板也绝对不会卡顿或锁死
- 实现真正的零停机（Zero-Downtime）体验

---

### 📝 阶段5.5：可解释性战报异步生成（后置翻译）

**时间**：阶段5落盘完成后，异步执行  
**负责人**：3号位（接口域）

**⚠️ 架构说明**：
- 1/2/5号位在阶段5落盘后工作结束，内存释放
- 3号位从数据库读取Task的枚举标签，翻译成人类可读的文本战报
- 彻底保证推演期内存沙盘的纯洁性

#### **步骤5.5.1：3号位异步触发**

**动作**：
- **【3号位】** 在阶段5完成后，启动异步后台任务（Hangfire或BackgroundService）
- **【3号位】** 从数据库读取刚落盘的Task表（带枚举标签）

**数据来源**：APS.Task表（已落盘）  
**数据去处**：内存缓存（临时）

---

#### **步骤5.5.2：枚举标签翻译成文本**

**动作**：
- **【3号位】** 读取Task的枚举标签：
  - `ReasonCode = 1`（物料短缺）
  - `ReasonMaterialId = 10086`（轴承）
- **【3号位】** 查询缓存字典，获取物料名称：
  ```csharp
  var materialName = _cache.GetMaterialName(10086);  // 返回"轴承"
  ```
- **【3号位】** 组装文本战报：
  ```csharp
  var explainText = $"订单因为[{materialName}({task.ReasonMaterialId})]缺料导致延期";
  ```

**数据来源**：Task.ReasonCode + Task.ReasonMaterialId + 缓存字典  
**数据去处**：ExplainTrace文本

---

#### **步骤5.5.3：战报写入数据库**

**动作**：
- **【3号位】** 将翻译好的文本战报写入 `ExplainTrace` 表
- **【3号位】** 用户在前端点击"查看战报"时，直接读取这张表

**数据来源**：ExplainTrace文本（内存）  
**数据去处**：APS.ExplainTrace表

**数据流向**：
```
APS.Task表（枚举标签） → 3号位.读取 → 缓存字典翻译 → ExplainTrace表
```

**业务意义**：
- 不仅给出排产结果，还给出系统的"思考过程"
- 为采购和车间调度提供可追溯的决策依据
- 完全不影响核心排程的内存纯洁性和性能

---

#### **步骤5.5.4（v3.8 新增）：读模型异步后处理**

**时间**：Task/ExplanationFact 落库完成后异步执行（非阻塞）  
**负责人**：2号位（BackgroundService）

**动作**：
- **【2号位】** 异步扫描当前 `PlanVersionId` 下已落库的 Task / ExplanationFact
- **【2号位】** 生成 `OrderScheduleSummary`（订单级计划完工 / 延期 / 风险 / 主因代码）
- **【2号位】** 生成 `ResourceLoadSummary`（资源×日期：负荷小时 / 负荷率 / 是否瓶颈）
- **【2号位】** 生成 `PlanKpiSummary`（版本级：准交率 / 延期订单数 / VIP延期 / 平均负荷率 / 瓶颈数）

**⚠️ 架构红线**：这三张读模型表**不参与排程内核**；生成失败不影响版本激活；阶段一 DDL 骨架即用，Batch 3 补完整索引。

---

#### **步骤5.5.5（v3.8 新增骨架注解）：仿真/人工重排入口预留**

**阶段**：阶段一为骨架注解（不实装）；阶段二实装  
**位置**：凌晨主链之外的独立触发路径

> **阶段二仿真入口**（预留说明）：
> - `SIMULATION` / `INSERT_ORDER_WHATIF` 类 `ScheduleRun` 由 3号位 API 触发（非 Hangfire 凌晨路径）
> - 可选先建 `Scenario`（记录 what-if 假设条件和优化目标），再触发 `ScheduleRun` 关联该 Scenario
> - 排程内核**复用当前同一套**（1号位），只是输入 ScheduleContext 加载不同数据快照（基于 `BaseVersionId` + 假设条件）
> - 产出 `PlanVersion`（CANDIDATE 状态，不自动激活）+ `ScheduleExplanationFact` + 读模型三张表
> - 通过 `ScenarioObjectiveScore` 对多个 `PlanVersion` 的 `PlanKpiSummary` 进行多目标比较（**不重新扫 Task 明细**）
> - `MANUAL_RESCHEDULE`（人工重排）不要求建 Scenario，直接触发 `ScheduleRun`，产出 CANDIDATE 版本

---

## 第二部分：跨厂协同全流程（5个场景）

跨厂协同是指订单需要在多个工厂或产品族之间流转时的协调机制。以下是5个典型场景：

---

### 🏭 场景1：同域跨厂物流（厂间订单发货Task）vs 异域跨族依赖（虚拟库存硬约束）

**⚠️ 架构红线说明**：
- **同域跨厂**（内政）：A厂和B厂都生产同一产品族X，半成品在厂间流转
  - 机制：5号位在排程时，匹配真实的厂间销售订单，并动态生成真实的物流发货Task（ShippingTask）
  - 特点：不影响域调度顺序，且排程结果带有真实 ERP 调拨单号
  - 示例：A厂生产产品族X的半成品 → 运输到B厂 → B厂继续生产产品族X的成品
- **异域跨族**（外交）：产品族A消耗产品族B的半成品
  - 机制：01:50静态扫描生成 `DomainDependency` 表 + 02:00上游落盘后下游读取为**虚拟库存硬约束**
  - 特点：必须提前确定域调度顺序（上游先排，下游后排）
  - 示例：产品族B（电机）先排 → 落盘 → 产品族A（整机）读取为虚拟库存 → 后排

**流程步骤**：

#### **步骤1.1：同域跨厂场景（基于真实单据的发货Task）**

**负责人**：5号位（业务规则）

**触发条件**：订单的BOM树中存在同产品族、跨工厂的物料需求，且供给池中有真实的厂间销售订单

**动作**：
- **【5号位】** 在排程时检测到跨厂需求
- **【5号位】** 在供给池中寻找对应的"厂间销售订单"，计算物流耗时（Duration），并向 **【2号位】** 返回包含**【发货Task生成指令与ERP单号】**的跨厂协同凭证
- **【2号位】** 接收凭证，在内存沙盘中统一建立 Pegging 连线，并统一生成真实的物流发货Task（ShippingTask）

**示例**：
```
订单SO001：产品族X（整机）
- A厂生产半成品（产品族X）
- ERP提前下达厂间销售订单：SO-Inter-001（A厂→B厂）
- B厂继续生产成品（产品族X）

→ 5号位在供给池中找到厂间订单 SO-Inter-001
→ 5号位动态生成真实发货Task：
  - 前置Task：A厂产品族X半成品生产
  - ShippingTask (ERP单号: SO-Inter-001)：运输（2.5天）
  - 后置Task：B厂产品族X成品生产
```

**数据来源**：供给池中的厂间销售订单（内存）  
**数据去处**：ScheduleContext.Tasks（内存，包含真实ShippingTask）

**⚠️ 架构契约**：
- 真实发货Task**只用于同域跨厂**场景
- 不影响域调度顺序（因为都是同一个产品族域）
- 在单个域的排程算法内部处理
- 排程结果带有真实 ERP 调拨单号，可直接指导车间/物流发货

---

#### **步骤1.2：异域跨族场景（虚拟库存硬约束）**

**负责人**：2号位（数据基础设施）

**触发条件**：订单的BOM树中存在跨产品族的物料需求

**动作**：
- **【2号位】** 在01:50静态扫描时，已通过SQL将跨产品族依赖固化到 `DomainDependency` 表（见阶段0.5）
- **【2号位】** 在02:00排程时，按拓扑顺序执行：
  - 上游域（产品族B）先排，立即落盘
  - 下游域（产品族A）启动前，读取上游落盘结果，构建**虚拟库存**（带AvailableTime）
  - 下游域排程时，虚拟库存的AvailableTime作为**硬约束**，算法自动"撞墙"推迟

**示例**：
```
订单SO002：产品族A（整机）需要产品族B（电机）的半成品

01:50静态扫描：
- SQL扫描生成：DomainDependency（产品族B → 产品族A）

02:00排程：
- 拓扑排序：产品族B先排，产品族A后排
- 产品族B排完，立即落盘（电机完工时间：3月3日14:00）
- 产品族A启动前，读取产品族B结果，构建虚拟库存：
  - MaterialId = 电机
  - AvailableTime = 3月3日14:00 + 物流2天 = 3月5日14:00
- 产品族A排程时，装配Task尝试排在3月1日→撞墙→自动推迟到3月5日14:00
```

**数据来源**：APS.Task表（上游域已落盘） + DomainDependency表  
**数据去处**：下游域.ScheduleContext.VirtualInventory（内存）

**⚠️ 架构契约**：
- 虚拟库存硬约束**只用于异域跨族**场景
- 必须严格按拓扑顺序执行（上游先排，下游后排）
- 上游的时间自动变成下游的物理时间墙

---

### ⚡ 场景2：跨域优先级继承（01:50预处理刷库）

**时间**：凌晨 01:50（与跨域依赖静态扫描同步执行）  
**负责人**：2号位（数据基础设施）

**⚠️ 架构红线说明**：
- **问题**：如果在02:00内存排程期间动态传递优先级（下游紧急订单提升上游优先级），会导致"时间倒流悖论"——上游域已经排完了，无法再调整优先级。
- **解决**：优先级继承必须在01:50的SQL预处理阶段通过刷库（UPDATE）完成，确保02:00排程启动时，所有订单的优先级已经是最终状态。

**流程步骤**：

#### **步骤2.1：识别跨域紧急订单**

**动作**：
- **【2号位】** 在01:50执行SQL查询，识别所有紧急订单（`Priority = 'URGENT'` 或 `Priority = 'VIP'`）
- **【2号位】** 通过BOM树追溯，找出这些紧急订单依赖的上游半成品物料
- **【2号位】** 定位上游域的相关订单

**SQL逻辑**：
```sql
-- 识别需要优先级继承的上游订单
SELECT DISTINCT
    上游订单.OrderId AS UpstreamOrderId,
    下游订单.Priority AS InheritedPriority
FROM 
    APS.Order AS 下游订单
    INNER JOIN BOM ON BOM.ParentMaterialId = 下游订单.MaterialId
    INNER JOIN Material AS 半成品 ON BOM.ComponentMaterialId = 半成品.MaterialId
    INNER JOIN APS.Order AS 上游订单 ON 上游订单.MaterialId = 半成品.MaterialId
WHERE 
    下游订单.Priority IN ('URGENT', 'VIP')
    AND 半成品.Type = 'SemiFinished'
    AND 上游订单.Priority > 下游订单.Priority;  -- 只提升优先级，不降低
```

---

#### **步骤2.2：批量刷库（UPDATE优先级）**

**动作**：
- **【2号位】** 执行批量UPDATE，将上游订单的优先级提升到下游紧急订单的优先级
- **【2号位】** 记录优先级继承日志（用于审计和解释）

**SQL逻辑**：
```sql
-- 批量更新上游订单优先级
UPDATE APS.Order
SET 
    Priority = 继承表.InheritedPriority,
    PrioritySource = 'INHERITED_FROM_DOWNSTREAM',
    UpdatedAt = GETDATE()
FROM APS.Order
INNER JOIN (
    -- 上面的查询结果
    SELECT UpstreamOrderId, MIN(InheritedPriority) AS InheritedPriority
    FROM ... 
    GROUP BY UpstreamOrderId
) AS 继承表 ON APS.Order.OrderId = 继承表.UpstreamOrderId;

-- 记录继承日志
INSERT INTO APS.PriorityInheritanceLog (UpstreamOrderId, DownstreamOrderId, InheritedPriority, Timestamp)
SELECT ...;
```

---

#### **步骤2.3：02:00排程直接读取最终优先级**

**动作**：
- **【2号位】** 在02:00的"阶段1：数据备料"中，直接读取已刷新的订单优先级
- **【1号位】** 排程算法按照最终优先级进行排序和资源分配
- **绝对禁止**在内存排程期间动态调整优先级

**数据流向**：
```
01:50 SQL刷库 → APS.Order表（优先级已更新） → 02:00阶段1读取 → 1号位排程算法
```

**示例**：
```
01:50执行：
- 下游订单SO999（整机，优先级=1 URGENT）
- 依赖上游订单SO888（电机，优先级=5 NORMAL）
- SQL刷库：UPDATE SO888 SET Priority = 1

02:00排程：
- 2号位读取：SO888优先级=1（已继承）
- 1号位排程：SO888按优先级1排在前面
- 无需动态传递，避免时间倒流
```

**架构收益**：
- 彻底消灭"时间倒流悖论"
- 优先级在排程启动前已固化，算法逻辑简单
- 所有优先级调整都有审计日志

---

### 📦 场景3：同域跨厂半成品在途管理（厂间物流发货Task场景）

**触发条件**：同产品族、跨工厂的半成品正在运输中（如：A厂产品族X半成品运往B厂）

**⚠️ 架构说明**：
- 此场景**只适用于同域跨厂**（基于真实厂间订单的发货Task）
- **异域跨族**场景不存在"在途管理"，因为上游落盘后下游直接读取虚拟库存，无需跟踪运输状态

**流程步骤**：

#### **步骤3.1：在途状态更新**

**负责人**：3号位（MES集成）

**动作**：
- **【3号位】** 接收MES发送的"半成品发货"事件（同产品族、跨工厂）
- **【3号位】** 更新物流发货Task（ShippingTask）的状态为 `IN_TRANSIT`
- **【3号位】** 记录预计到货时间（ETA - Estimated Time of Arrival）

**示例**：
```
A厂产品族X半成品完工 → 发货到B厂
→ MES发送事件：ShippingTask_12345 (ERP单号: SO-Inter-001) 发货
→ 3号位更新：Task.Status = IN_TRANSIT, ETA = 3月3日10:00
```

**数据来源**：MES发货事件（通过MQ）  
**数据去处**：APS.Task表（ShippingTask状态更新）

**数据流向**：
```
MES发货事件 → 3号位.MES网关 → Task.Status = IN_TRANSIT
```

---

#### **步骤3.2：延迟处理（打标签+前端标红+PMC决策）**

**负责人**：5号位 + 4号位（前端）

**⚠️ 架构红线说明**：
- **物理常识**：APS的计划（Plan）是固化在数据库里的静态时间戳。如果在白天的动态执行中，卡车晚点了，下游机床（后续Task）原本计划在10:00开工的数据，**绝对不可能"自动推迟"**，除非启动1号位的排程算法（重排程）。
- **V1.0保守原则**：系统原则上不自动触发重排，避免白天车间计划频繁震荡。决策权交还给PMC。

**动作**：
- **【5号位】** 调用 `IInTransitRule` 插件，检测运输延迟
- **【5号位】** 如果实际到货时间 > 预计到货时间，在受影响的后续Task上打 `LOGISTICS_DELAY` 标签
- **【5号位】** 生成延迟通知，发送给PMC
- **【5号位】** **该后续Task的计划开工时间在数据库中保持不变**
- **【4号位】** 前端甘特图将该Task标红显示，提示PMC存在物流延迟风险
- **【0号位】** PMC评估后决策：手动调整计划，或主动点击"触发局部重排"

**示例**：
```
预计到货：3月3日10:00
实际到货：3月5日14:00（延迟2.17天）

→ 5号位打标签：后续Task.ReasonCode = LOGISTICS_DELAY, DelayDays = 2.17
→ 后续Task的计划开工时间在数据库中保持不变（仍为3月3日12:00）
→ 前端甘特图将该Task标红，显示"物流延迟2.17天"
→ PMC看到告警后决策：
  - 选项1：手动调整该Task的开工时间
  - 选项2：点击"触发局部重排"，由1号位重新计算
  - 选项3：接受风险，等明天凌晨02:00全量排程自动修正
```

**数据来源**：Task.ETA vs Task.ActualArrivalTime  
**数据去处**：后续Task.ReasonCode + 延迟通知 + 前端标红

**数据流向**：
```
Task.ETA vs ActualArrivalTime → 5号位.延迟检测 → 打标签（数据库时间不变） → 前端标红 → PMC决策
```

**⚠️ 架构契约**：
- **绝对禁止**"不重排但时间自动推迟"的魔法逻辑
- 数据库中的Task开工时间保持不变，只打标签
- 前端甘特图通过标红提示PMC
- 决策权交给PMC，由PMC选择是否触发重排

---

### 🤝 场景4：异域跨族上游延期自动顺延（虚拟库存硬约束场景）

**触发条件**：异域跨族场景中，上游域排程完成，半成品完工时间晚于预期

**⚠️ 架构红线说明**：
- **问题**：如果上游延期后"通知下游、触发下游重排"，会导致多余的重排机制，增加系统复杂度和震荡风险。
- **解决**：在DAG批处理架构中，上游延期会自动转化为下游虚拟库存时间的推迟，下游算法会自动顺延，无需任何重排。

**流程步骤**：

#### **步骤4.1：上游域排程完成，立即落盘**

**负责人**：2号位

**动作**：
- **【2号位】** 上游域（如：产品族B-电机）排程完成
- **【2号位】** 使用 `SqlBulkCopy` 将上游域的Task结果立即落盘到数据库
- **【2号位】** 半成品完工时间已固化（如：原计划3月1日10:00，实际排到3月3日14:00，延期2.17天）

**数据来源**：域B.ScheduleContext（内存）  
**数据去处**：APS.Task表（域B已落盘）

---

#### **步骤4.2：下游域读取上游结果，虚拟库存时间自动推迟**

**负责人**：2号位

**动作**：
- **【2号位】** 下游域（如：产品族A-整机）启动前，从数据库读取上游域刚落盘的Task结果
- **【2号位】** 构建虚拟库存时，`AvailableTime` 自动使用上游的实际完工时间（3月3日14:00 + 物流2天 = 3月5日14:00）
- **【2号位】** 虚拟库存的时间已经包含了上游延期，无需额外通知

**示例SQL**：
```sql
SELECT 
    t.MaterialId,
    MAX(t.EndTime) + INTERVAL '2 DAY' AS AvailableTime,  -- 实际完工时间（已延期）+ 物流
    SUM(t.Quantity) AS Quantity
FROM APS.Task t
WHERE t.DomainId = '产品族B'
  AND t.MaterialId IN (SELECT ComponentMaterialId FROM BOM WHERE ParentDomain = '产品族A')
GROUP BY t.MaterialId;
```

**数据来源**：APS.Task表（域B已落盘，包含延期）  
**数据去处**：域A.ScheduleContext.VirtualInventory（内存）

---

#### **步骤4.3：下游域排程时自动"撞墙"顺延**

**负责人**：1号位

**动作**：
- **【1号位】** 下游域排程时，检查虚拟库存的 `AvailableTime`（3月5日14:00）
- **【1号位】** 算法自动"撞墙"，将下游Task的开工时间推迟到3月5日14:00之后
- **【1号位】** 无需任何"通知"或"重排"，延期自动传递

**示例**：
```
上游延期：
- 原计划：电机3月1日10:00完工
- 实际排程：电机3月3日14:00完工（延期2.17天）

下游自动顺延：
- 虚拟库存.AvailableTime = 3月5日14:00（自动包含延期）
- 下游装配Task尝试排在3月1日→撞墙→自动推迟到3月5日14:00
- 无需重排，数学上100%自动收敛
```

---

#### **步骤4.4：5号位打标签（供PMC线下决策）**

**负责人**：5号位

**动作**：
- **【5号位】** 在下游Task上打枚举标签：`ReasonCode = UPSTREAM_DELAY`
- **【5号位】** 记录关联信息：`UpstreamDomain = '产品族B'`，`DelayDays = 2.17`
- **【5号位】** 不触发任何自动重排或通知
- **【5号位】** PMC在前端查看"延期原因分析"时，看到"因上游产品族B延期2.17天导致顺延"

**数据来源**：域A.Task（内存）  
**数据去处**：Task.ReasonCode + Task.UpstreamDomain + Task.DelayDays（内存，后续落盘）

**数据流向**：
```
上游延期 → 虚拟库存时间推迟 → 下游算法自动顺延 → 5号位打标签 → PMC线下决策
```

**⚠️ 架构契约**：
- **绝对禁止**"通知下游、触发下游重排"的冗余机制
- 上游延期自动转化为虚拟库存时间推迟，下游算法自动顺延
- 只在输出结果中打标签，供PMC人工决策（如：是否需要增加班次、外协加工）

**架构收益**：
- 完全不需要写"下游重排"逻辑
- 延期自动传递，数学上100%收敛
- PMC有完整的延期归因分析，可线下决策

---

## 第三部分：分域计算全流程（3个场景）

分域计算是指将7个产品族的排程任务并发执行，以提升整体排程速度。以下是3个关键场景：

---

### 🚀 场景1：分域任务分配与并发调度

**触发条件**：凌晨全量排程或局部重排

**流程步骤**：

#### **步骤1.1：任务分配**

**负责人**：3号位

**动作**：
- **【3号位】** 根据 **【2号位】** 提供的依赖图，将7个产品族分配到Hangfire任务队列
- **【3号位】** 为每个域创建独立的排程任务（ScheduleDomainJob）
- **【3号位】** 设置任务优先级（有依赖的域优先级更高）

**数据来源**：ScheduleContext.DomainDependencyGraph（内存）  
**数据去处**：Hangfire任务队列

**数据流向**：
```
DomainDependencyGraph → 3号位.任务分配 → Hangfire.7个并发Job
```

---

#### **步骤1.2：并发执行**

**负责人**：3号位（调度） + 1号位（执行）

**动作**：
- **【3号位】** Hangfire同时启动多个域的排程任务（最多7个并发）
- **【1号位】** 每个域独立执行排程算法
- **【3号位】** 监控每个域的执行状态（RUNNING、COMPLETED、FAILED）

**示例**：
```
时间轴：
00:00 - 域A、域B、域C同时开始排程（无依赖）
05:00 - 域A完成，触发域D开始（域D依赖域A）
08:00 - 域B、域C完成，触发域E、域F开始
12:00 - 所有域完成
```

**数据来源**：Hangfire任务队列  
**数据去处**：各域的排程结果（内存）

**数据流向**：
```
Hangfire调度 → 1号位.并发排程 → 各域ScheduleContext
```

---

### 🧩 补充：V1 共享资源的“配额/预留窗口”粗隔离策略（不抽共享资源域）

当不同产品域之间存在少量共享资源（同一资源池被多域使用），但该资源**利用率低或冲突少**时，V1.0阶段不引入“共享资源域统一排程”的复杂协同，而采用“配额/预留窗口”进行粗隔离，降低并发计算的冲突概率。

**适用条件**（满足其一即可纳入粗隔离，而不是抽成共享资源域）：
- **共享但非瓶颈**：资源池利用率长期低于阈值（如 <70%）
- **共享但低冲突**：跨域抢占冲突次数低于阈值（如每日<5次，或冲突占比<1%）

**识别口径**：以“资源池（WorkCenter/ResourceGroup）”为粒度，而不是物料SKU。
- 统计 `Domains(R)` = 该资源池R在计划周期内服务过的产品域集合
- 当 `|Domains(R)| >= 2` 且不满足“瓶颈共享”判定时，进入粗隔离清单

**配置参数**（由0号位/PMC配置，2号位落地配置存储，5号位规则读取）：
- `QuotaMode`：
  - `Percent`：按百分比分配（如A:60%，B:40%）
  - `TimeWindow`：按时间窗预留（如每天08:00-16:00给A，16:00-24:00给B）
- `QuotaHorizonDays`：配额生效范围（如未来7天）
- `BorrowPolicy`：借用策略（`Forbidden` / `AllowedWithPenalty`）
- `BorrowPenalty`：借用惩罚系数（用于排序时降低借用域任务优先级）

**生效方式**（核心思想：把“共享资源池”在内存日历中切成多个“虚拟子日历”）：
- **【2号位】** 在构建 `ResourceCalendar` 时，对粗隔离资源池生成“虚拟产能日历切片”（Virtual Calendar Slice）
- **【1号位】** 进行时间槽寻址时：
  - 域A只在A的切片内寻址
  - 域B只在B的切片内寻址
- **【5号位】** 若启用借用策略，则允许在对方切片里寻址，但会触发惩罚或需要人工审批

**冲突与降级**（粗隔离并不保证最优，仅保证可控）：
- 若出现“配额不足导致大量延期”（如延期订单数超过阈值），则升级处理：
  - **升级策略A**：将该资源池标记为“瓶颈共享”，在下一轮排程中抽成“共享资源域统一排程”
  - **升级策略B（V1保守）**：保持粗隔离，但向PMC输出“配额调整建议”（增加A配额/减少B配额）

**数据流向**：
```
资源池R → 统计Domains(R)与冲突指标 → 0号位/PMC配置Quota → 2号位.构建虚拟日历切片 → 1号位.按切片寻址
```

---

### ⚠️ 场景2：分域失败重试与降级

**触发条件**：某个域的排程任务失败或超时

**流程步骤**：

#### **步骤2.1：失败检测**

**负责人**：3号位

**动作**：
- **【3号位】** 监控Hangfire任务状态
- **【3号位】** 检测失败原因：
  - 算法异常（如：内存溢出、死循环）
  - 超时（单域排程超过30分钟）
  - 数据异常（如：BOM环路）

**数据来源**：Hangfire任务状态  
**数据去处**：失败日志

**数据流向**：
```
Hangfire任务监控 → 3号位.失败检测 → 失败日志
```

---

#### **步骤2.2：自动重试**

**负责人**：3号位

**动作**：
- **【3号位】** 配置Hangfire自动重试策略（最多3次）
- **【3号位】** 第1次失败：立即重试
- **【3号位】** 第2次失败：等待5分钟后重试
- **【3号位】** 第3次失败：触发降级策略

**数据流向**：
```
失败检测 → 3号位.重试策略 → Hangfire重新调度
```

---

#### **步骤2.3：降级为粗排**

**负责人**：3号位 + 0号位（决策）

**动作**：
- **【3号位】** 3次重试仍失败后，通知 **【0号位】**
- **【0号位】** 决策：是否降级为粗排（Rough Scheduling）
- **【3号位】** 执行降级：
  - 关闭换型优化
  - 简化约束条件
  - 使用更大的时间粒度（小时级而非分钟级）

**数据流向**：
```
3次失败 → 0号位.降级决策 → 3号位.粗排执行
```

**业务意义**：
- 确保即使细排失败，也能提供粗略的排程结果
- 避免因单个域失败导致整体排程瘫痪

---

### 🔄 场景3：单向硬约束传递（废除事后修复，避免排程震荡）

**触发条件**：按拓扑顺序执行域排程

**⚠️ 架构红线说明**：
- **问题**：如果采用"事后校验+修复"（域A重排影响域C，域C重排又影响域B），会导致"排程震荡（Scheduling Oscillation）"，系统可能陷入死循环，永远无法输出最终版本。
- **解决**：采用Asprova等专业引擎的"单向硬约束传递"法则：上游域先排，输出的完工时间作为**绝对硬性时间屏障（Hard Constraint）**传递给下游域，下游域只能在这条时间线之后排产。

**流程步骤**：

#### **步骤3.1：上游域先排，立即落盘**

**负责人**：1号位 + 2号位 + 3号位

**动作**：
- **【3号位】** 根据拓扑排序结果，先启动上游域（如：产品族B-电机）
- **【1号位】** 上游域排程完成，生成Task结果
- **【2号位】** 立即使用 `SqlBulkCopy` 将上游域的Task落盘到数据库

**示例**：
```
拓扑排序结果：
Layer 0: 产品族B（电机，无依赖）
Layer 1: 产品族A（整机，依赖B）

→ 产品族B先排程，立即落盘
```

**数据来源**：域B.ScheduleContext（内存）  
**数据去处**：APS.Task表（域B的结果）

**数据流向**：
```
域B.ScheduleContext → 2号位.SqlBulkCopy → APS.Task表（域B落盘）
```

---

#### **步骤3.2：下游域读取上游结果，构建虚拟库存**

**负责人**：2号位

**动作**：
- **【2号位】** 下游域（产品族A）启动前，从数据库读取上游域（产品族B）刚落盘的Task结果
- **【2号位】** 将上游域生产出来的半成品，作为**带时间戳的虚拟库存（Virtual Inventory）**放入下游域的 `ScheduleContext`
- **【2号位】** 虚拟库存包含关键字段：
  - `MaterialId`：半成品物料ID（如：电机）
  - `AvailableTime`：最早可用时间（上游完工时间 + 物流时间）
  - `Quantity`：可用数量

**示例**：
```sql
-- 2号位执行的SQL查询
SELECT 
    t.MaterialId,
    MAX(t.EndTime) + INTERVAL '2 DAY' AS AvailableTime,  -- 完工时间+物流2天
    SUM(t.Quantity) AS Quantity
FROM APS.Task t
WHERE t.DomainId = '产品族B'
  AND t.MaterialId IN (SELECT ComponentMaterialId FROM BOM WHERE ParentDomain = '产品族A')
GROUP BY t.MaterialId;
```

**数据来源**：APS.Task表（域B已落盘）  
**数据去处**：域A.ScheduleContext.VirtualInventory（内存）

**数据流向**：
```
APS.Task表（域B） → 2号位.SQL查询 → 域A.ScheduleContext.VirtualInventory（虚拟库存）
```

---

#### **步骤3.3：下游域排程时自动"撞墙"推迟**

**负责人**：1号位

**动作**：
- **【1号位】** 下游域（产品族A）执行排程算法
- **【1号位】** 在排装配Task时，检查虚拟库存的 `AvailableTime`
- **【1号位】** 如果尝试的开工时间 < `AvailableTime`，算法自动"撞墙"，强制推迟到 `AvailableTime` 之后

**示例**：
```
域B电机完工时间：2026-03-01 10:00
物流时间：2天
→ 虚拟库存.AvailableTime = 2026-03-03 10:00

域A装配Task尝试排在：2026-03-01 14:00
→ 1号位检查：14:00 < AvailableTime (03-03 10:00)
→ 算法撞墙！自动推迟到 2026-03-03 10:00 之后
→ 最终排在：2026-03-03 10:00
```

**数据来源**：域A.ScheduleContext.VirtualInventory（内存）  
**数据去处**：域A.Task.StartTime（自动推迟）

**数据流向**：
```
域A.Task尝试开工 → 1号位.检查VirtualInventory.AvailableTime → 撞墙推迟 → Task.StartTime自动调整
```

---

#### **步骤3.4：下游域落盘，天然100%一致**

**负责人**：2号位

**动作**：
- **【2号位】** 下游域（产品族A）排程完成后，立即落盘
- **【2号位】** 由于上游的时间已经作为硬约束传递，下游结果天然满足时间一致性

**⚠️ 架构契约**：
- **绝对禁止**"事后校验+修复"的循环
- 只要严格按拓扑顺序 + 硬约束传递，结果数学上100%绝对收敛
- **V1保留一次性告警（不修复）**：如果下游发现"上游给的时间太晚，导致客户交期违约"，不触发重排，只在**交期违约清单**里体现（由PMC决策是否手动调整）

**数据流向**：
```
域A.ScheduleContext → 2号位.SqlBulkCopy → APS.Task表（域A落盘，天然一致）
```

**架构收益**：
- 完全不需要写任何"校验和修复"逻辑
- 上游的时间自动变成下游的物理时间墙
- 数据在数学上100%绝对收敛，永远不可能出现排程震荡
- 如果出现交期违约，在ValidationResult里体现，由PMC决策

---

## 第四部分：动态实绩与异常重排（6个场景）

车间实际执行过程中会出现各种异常，需要动态调整排程计划。以下是6个关键场景：

---

### 📡 场景1：MES实绩接收与去重

**触发条件**：MES系统上报车间实绩事件

**⚠️ 重排触发阈值**：
- **累计延迟 > 2小时**：触发 NORMAL 优先级重排
- **累计延迟 > 4小时**：触发 HIGH 优先级重排
- **影响VIP订单**：触发 URGENT 优先级重排
- **延迟Task数量 > 10个**：触发域级重排

**流程步骤**：

#### **步骤1.1：实绩事件接收**

**负责人**：3号位

**动作**：
- **【3号位】** 通过MQ接收MES实绩事件（START、COMPLETE、PAUSE、RESUME、SCRAP）
- **【3号位】** 将事件存入 `EventSourcingBuffer` 表（事件溯源缓冲区）
- **【3号位】** 返回ACK确认，避免MES重复发送

**数据来源**：MES系统（通过MQ）  
**数据去处**：APS.EventSourcingBuffer 表

**数据流向**：
```
MES.实绩事件 → MQ → 3号位.MES网关 → EventSourcingBuffer表
```

---

#### **步骤1.2：消息去重与幂等性处理**

**负责人**：3号位

**动作**：
- **【3号位】** 检查事件的 `EventId` 是否已存在
- **【3号位】** 如果已存在，丢弃重复消息
- **【3号位】** 如果是新消息，继续处理

**数据来源**：EventSourcingBuffer.EventId  
**数据去处**：去重后的有效事件

**数据流向**：
```
EventSourcingBuffer → 3号位.EventId去重 → 有效事件队列
```

---

### 🔧 场景2：设备故障与日历阻挡块生成

**触发条件**：MES上报设备故障事件（RESOURCE_BREAKDOWN）

**⚠️ 重排触发阈值**：
- **瓶颈设备故障**：立即触发 HIGH 优先级重排
- **普通设备故障**：触发 NORMAL 优先级重排
- **预计修复时间 > 8小时**：触发域级重排
- **影响冻结区任务**：触发 URGENT 优先级重排

**流程步骤**：

#### **步骤2.1：故障事件转化**

**负责人**：5号位

**动作**：
- **【5号位】** 调用 `IRescheduleRule` 插件，处理设备故障事件
- **【5号位】** 提取关键信息：
  - 故障设备ID
  - 故障开始时间
  - 预计修复时间（EstimatedRepairTime）
- **【5号位】** 生成设备日历阻挡块（CalendarBlock）

**数据来源**：MES故障事件  
**数据去处**：ResourceCalendar.Blocks（内存）

**数据流向**：
```
MES故障事件 → 5号位.故障转化规则 → ResourceCalendar阻挡块
```

---

#### **步骤2.2：重排评估与触发**

**负责人**：5号位 + 3号位

**动作**：
- **【5号位】** 评估是否需要触发重排：
  - 检查该设备上是否有未完成的Task
  - 计算影响的订单数量
  - 判断是否超过重排阈值（如：影响>10个订单）
- **【5号位】** 如果需要重排，通知 **【3号位】**
- **【3号位】** 调用 `TriggerReschedule(scope, reason)` 接口，触发局部重排

**数据来源**：ResourceCalendar.Blocks + Task.ResourceId  
**数据去处**：重排任务触发

**数据流向**：
```
阻挡块 + 受影响Task → 5号位.重排评估 → 3号位.触发重排
```

**⚠️ V1.0范围说明**：设备修复后，直接调用全局或单域重排，不做复杂的"局部加急微调规则"。

---

### ⏸️ 场景3：任务暂停与恢复

**触发条件**：MES上报任务暂停事件（PAUSE）或恢复事件（RESUME）

**⚠️ 重排触发阈值**：
- **暂停时间 > 4小时**：触发 NORMAL 优先级重排
- **暂停Task数量 > 5个**：触发域级重排
- **影响关键路径**：触发 HIGH 优先级重排

**流程步骤**：

#### **步骤3.1：暂停处理**

**负责人**：5号位

**动作**：
- **【5号位】** 调用 `IRescheduleRule` 插件，处理暂停事件
- **【5号位】** 更新Task状态：`RUNNING` → `PAUSED`
- **【5号位】** 记录暂停时间和原因
- **【5号位】** 计算剩余工时

**数据来源**：MES暂停事件  
**数据去处**：Task.Status + Task.PausedTime

**数据流向**：
```
MES暂停事件 → 5号位.暂停处理 → Task状态更新
```

---

#### **步骤3.2：恢复处理**

**负责人**：5号位

**动作**：
- **【5号位】** 处理恢复事件
- **【5号位】** 更新Task状态：`PAUSED` → `RUNNING`
- **【5号位】** 重新计算预计完工时间（基于剩余工时）
- **【5号位】** 如果延期严重，触发局部重排

**数据来源**：MES恢复事件  
**数据去处**：Task.Status + Task.EstimatedEndTime

**数据流向**：
```
MES恢复事件 → 5号位.恢复处理 → Task状态更新 → 可能触发重排
```

---

### 📉 场景4：报废处理

**触发条件**：MES上报报废事件（SCRAP）

**⚠️ 重排触发阈值**：
- **报废率 > 10%**：推送PMC看板告警
- **报废影响VIP订单**：触发 HIGH 优先级重排

**⚠️ V1.0架构红线处理方式**：

**负责人**：3号位 / 5号位

**动作**：
- **【3号位】** 接收到 MES 报废事件后，仅在原 Task 上记录报废数量日志（`ScrapQuantity` 字段），供追溯和统计分析
- **【5号位】** V1.0 坚守 **1:1 刚性流转规则**，绝对禁止以下行为：
  - ❌ 禁止自动扣减后续工序的待排数量
  - ❌ 禁止自动生成补料 Task
  - ❌ 禁止在内存中计算"剩余合格品数量"来影响排程
- **【PMC】** 现场因报废产生的实际物理缺口，由 PMC 线下确认后，在 **ERP 系统**中录入全新的补料 MTS 单据
- **【2号位】** APS 引擎在下一次 02:00 全量拉取时，自动将 ERP 新下达的补料单据纳入排程

**数据流向**：
```
MES报废事件 → 3号位.记录日志 → Task.ScrapQuantity（仅供追溯）
PMC线下确认 → ERP录入补料单 → 次日02:00 APS自动拉取
```

---

### 📝 场景5：订单取消与变更

**触发条件**：ERP系统通知订单取消或变更

**⚠️ 重排触发阈值**：
- **取消订单已进入冻结区**：触发 URGENT 优先级重排
- **取消订单释放瓶颈产能**：触发 HIGH 优先级重排
- **订单数量变更 > 20%**：触发 NORMAL 优先级重排

**流程步骤**：

#### **步骤5.1：订单取消处理**

**负责人**：5号位

**动作**：
- **【5号位】** 调用 `IOrderChangeRule` 插件
- **【5号位】** 标记订单状态为 `CANCELLED`
- **【5号位】** 释放已分配的产能（删除相关Task）
- **【5号位】** 触发局部重排（重新分配释放的产能）

**数据来源**：ERP订单取消事件  
**数据去处**：Order.Status + Task删除 + 重排触发

**数据流向**：
```
ERP取消事件 → 5号位.取消处理 → Order标记 + Task删除 → 触发重排
```

---

#### **步骤5.2：订单数量/交期变更**

**负责人**：5号位

**动作**：
- **【5号位】** 处理订单变更
- **【5号位】** 更新Order的 `Quantity` 或 `CustomerDueDate`
- **【5号位】** 判断变更影响：
  - 数量增加：可能需要增加Task
  - 数量减少：可能需要删除Task
  - 交期提前：可能需要提升优先级
- **【5号位】** 触发局部重排

**数据流向**：
```
ERP变更事件 → 5号位.变更处理 → Order更新 → 触发重排
```

---

### 🔄 场景6：局部重排执行

**触发条件**：上述任何异常触发重排

**流程步骤**：

#### **步骤6.1：重排范围确定**

**负责人**：3号位 + 5号位

**动作**：
- **【5号位】** 确定重排范围（scope）：
  - 单个设备（设备故障）
  - 单个产品族（域内异常）
  - 全局（跨域影响）
- **【3号位】** 根据范围创建Hangfire重排任务

**数据来源**：异常事件 + 影响分析  
**数据去处**：Hangfire重排任务

---

#### **步骤6.2：快照融合**

**负责人**：5号位 + 2号位

**动作**：
- **进行中的Task（在制品）**：**【5号位】**折算出剩余工时，并在沙盘中强制锁定其当前机床与开工时间（作为不可移动的物理锚点）。

**数据来源**：Task.Status + MES实绩  
**数据去处**：重排ScheduleContext（内存）

**数据流向**：
```
Task实绩 + 设备阻挡块 → 5号位.折算剩余工时 → 2号位.快照融合 → 重排ScheduleContext
```

---

#### **步骤6.3：重排执行**

**负责人**：1号位

**动作**：
- **【1号位】** 执行排程算法。遇到锁定的在制Task时，算法直接以此为锚点，结合机床日历向后划拨"剩余工时"，精确推算出全新的完工时间，并以此为基准顺延所有下游工序。

**数据流向**：
```
重排ScheduleContext → 1号位.排程算法（推雪机避让） → 新的排程结果
```

---

#### **步骤6.4：结果发布**

**负责人**：2号位 + 3号位

**动作**：
- **【2号位】** 使用SqlBulkCopy批量落盘
- **【3号位】** 版本指针切换
- **【3号位】** 前端广播刷新

**数据流向**：
```
新排程结果 → 2号位.批量落盘 → 3号位.版本切换 → 前端刷新
```

**业务意义**：
- 实现车间异常的快速响应（15-30分钟内完成重排）
- 保持计划与实际的一致性
- 最小化异常对整体计划的影响

---

## 第五部分：人工干预与调整流程（3个场景）

PMC（生产计划员）需要对系统生成的计划进行人工调整和审批。以下是3个关键场景：

---

### 🖱️ 场景1：PMC手动拖拽调整

**触发条件**：PMC在甘特图上拖拽任务，调整开工时间

**流程步骤**：

#### **步骤1.1：前端拖拽事件**

**负责人**：4号位

**动作**：
- **【4号位】** 监听甘特图的拖拽事件
- **【4号位】** 执行合规性检查：
  - 禁止拖拽Frozen区（冻结区）的任务
  - 禁止拖拽已开工的任务（Status = RUNNING）
  - 禁止拖拽到设备维修时间段
- **【4号位】** 如果违规，弹出警告并阻止拖拽
- **【4号位】** 如果合规，显示影响范围提示

**数据来源**：甘特图UI事件  
**数据去处**：前端状态

**数据流向**：
```
用户拖拽 → 4号位.合规性检查 → 通过/拒绝
```

---

#### **步骤1.2：影响分析**

**负责人**：5号位

**动作**：
- **【4号位】** 将拖拽请求发送到后端
- **【5号位】** 分析影响范围：
  - 该任务的后续任务（依赖链）
  - 同一设备上的其他任务（时间冲突）
  - 关联订单的交期影响
- **【5号位】** 返回影响订单列表

**数据来源**：Task.Id + 新的StartTime  
**数据去处**：影响分析结果

**数据流向**：
```
拖拽请求 → 5号位.影响分析 → 影响订单列表
```

---

#### **步骤1.3：确认与局部重排**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** 显示确认对话框：
  - "此调整将影响X个订单，是否继续？"
  - 显示受影响订单列表
- **【用户】** 点击"确认"
- **【4号位】** 发送调整请求到后端
- **【3号位】** 触发局部重排（只重排受影响的任务）

**数据流向**：
```
用户确认 → 4号位.调整请求 → 3号位.局部重排
```

---

### 🔒 场景2：任务手动锁定与优先级调整

**触发条件**：PMC需要锁定某些任务，或调整优先级

**流程步骤**：

#### **步骤2.1：任务锁定**

**负责人**：4号位 + 5号位

**动作**：
- **【4号位】** PMC在甘特图上选中任务，点击"锁定"按钮
- **【4号位】** 发送锁定请求到后端
- **【5号位】** 更新Task状态：`IsLocked = true`
- **【5号位】** 锁定的任务在后续重排中不会被移动

**数据来源**：Task.Id  
**数据去处**：Task.IsLocked = true

**数据流向**：
```
用户锁定操作 → 4号位.锁定请求 → 5号位.Task状态更新
```

---

#### **步骤2.2：优先级调整**

**负责人**：4号位 + 5号位

**动作**：
- **【4号位】** PMC拖动优先级滑块，调整任务优先级
- **【4号位】** 发送优先级调整请求
- **【5号位】** 更新Task.Priority字段
- **【5号位】** 如果优先级提升，可能触发重排

**示例**：
```
任务T001原优先级=5
PMC调整为优先级=1（最高）
→ 系统触发局部重排，优先安排T001
```

**数据流向**：
```
优先级调整 → 4号位.调整请求 → 5号位.Priority更新 → 可能触发重排
```

---

### ✅ 场景3：计划审批与冻结

**触发条件**：排程计划生成后，需要PMC审批才能下发MES

**流程步骤**：

#### **步骤3.1：计划审批**

**负责人**：4号位 + 0号位

**动作**：
- **【4号位】** 显示计划审批界面：
  - 排程结果摘要（准时率、产能利用率）
  - 延期订单列表
  - 关键瓶颈设备
- **【0号位】** 或授权的PMC审批计划
- **【4号位】** 填写审批意见
- **【4号位】** 点击"批准"或"驳回"

**数据来源**：PlanVersion + ValidationResult  
**数据去处**：PlanVersion.ApprovalStatus

**数据流向**：
```
审批界面 → 0号位.审批决策 → PlanVersion.ApprovalStatus更新
```

---

#### **步骤3.2：人工强制冻结/解冻（特殊干预）**

**负责人**：5号位 + PMC

**⚠️ 重要说明**：
- **系统的冻结主要由每日 02:00 的滑动窗口自动完成**（见阶段5步骤5.0）
- **PMC 的界面只用于对特殊单据进行"人工强制干预"**

**动作**：
- **【PMC】** 在特殊情况下（如紧急插单、设备临时维护），需要强制冻结或解冻某些 Task
- **【4号位】** 提供人工冻结/解冻界面：
  - 显示当前冻结区边界（如：未来3天）
  - 允许 PMC 手动勾选需要强制冻结的 Task（即使在3天外）
  - 允许 PMC 手动解冻冻结区内的 Task（需要审批权限）
- **【5号位】** 调用 `IManualFreezeRule` 插件，执行人工冻结/解冻
- **【5号位】** 更新 Task.IsFrozen 字段和 Task.ManualFrozenBy 字段（记录操作人）

**示例场景**：
```
场景1：紧急插单强制冻结
→ VIP客户紧急插单，PMC手动将该订单的所有Task强制冻结
→ 即使排在5天后，也立即下发MES优先生产

场景2：设备维护临时解冻
→ 某设备临时维护，PMC手动解冻该设备上的冻结区Task
→ 触发局部重排，将Task转移到其他设备
```

**数据流向**：
```
PMC人工干预 → 5号位.ManualFreezeRule → Task.IsFrozen + Task.ManualFrozenBy更新
```

---

#### **步骤3.3：解冻申请**

**负责人**：4号位 + 0号位

**动作**：
- **【4号位】** 如果PMC需要调整冻结区任务，提交解冻申请
- **【4号位】** 填写解冻理由（如：客户紧急变更）
- **【0号位】** 审批解冻申请
- **【5号位】** 如果批准，更新Task.IsFrozen = false

**数据流向**：
```
解冻申请 → 4号位.申请界面 → 0号位.审批 → Task.IsFrozen更新
```

**业务意义**：
- 给予PMC灵活调整计划的能力
- 通过审批和冻结机制，保证计划的稳定性
- 避免频繁变更导致车间执行混乱

---

## 第六部分：数据同步与一致性流程（3个场景）

确保APS与ERP、MES等外部系统的数据一致性。以下是3个关键场景：

---

### 🔄 场景1：ERP增量订单同步（2026-04-03 订单链路审计修正）

**触发条件**：ERP系统有新订单或订单变更

**完整链路**：`v_APS_SalesOrder` → `ERP_Order_Staging`（PENDING）→ `sp_ValidateAndPromoteOrders` → `Order_Canonical` → 夜间 `sp_SyncOrdersToPartitionTable` → `Order`

**流程步骤**：

#### **步骤1.1：增量拉取与写入Staging**

**负责人**：2号位

**动作**：
- **【2号位】** 每小时通过Hangfire定时任务执行`ERPOrderSyncService.IncrementalSyncAsync()`
- **【2号位】** 从ERP契约视图`v_APS_SalesOrder`拉取增量数据（含SO/MTO/MTS/SS，UNION ALL两类中间表）
- **【2号位】** 同时通过CDC监控ERP订单表变化，记录到`CDC_ChangeLog`
- **【2号位】** 将拉取的订单写入`ERP_Order_Staging`表（`SyncStatus = 'PENDING'`）

**数据来源**：ERP.`v_APS_SalesOrder`（契约视图，含BOMNO/SourceSystem/SourceMasterID）  
**数据去处**：APS.`ERP_Order_Staging`表

**数据流向**：
```
ERP.v_APS_SalesOrder → 2号位.ERPOrderSyncService → APS.ERP_Order_Staging(PENDING)
```

---

#### **步骤1.2：数据质量校验与提升到Canonical**

**负责人**：2号位

**动作**：
- **【2号位】** 调用`sp_ValidateAndPromoteOrders`（v5.0.27全量重写）执行校验与提升：
  - **PHASE 0**：`#TargetStagingIds` 锁定本批次所有PENDING行ID（UPDLOCK+ROWLOCK防并发）
  - **STEP 0：MaterialCode 三级解析链**：0a SourceMasterID→MaterialMapping（最高优先级）→ 0b Model→MaterialMapping.SourceModel（一对多→立即FAILED+MATERIAL_MAPPING_AMBIGUOUS）→ 0c EmergencyOverride兜底
  - **STEP 1a：硬失败校验**：必填字段+OrderType未知（⚠️ 禁止将ERP原始值写入Canonical，必须FAILED+ORDER_TYPE_UNKNOWN）
  - **STEP 1b**：MaterialCode不在Material主数据中→MATERIAL_NOT_FOUND
  - **STEP 1c：非阻断诊断**：BOMNO=NULL→FailureCode=`BOMNO_MISSING`+NextActionCode=`WAIT_BOM_WORKSET`（SyncStatus不置FAILED，订单仍可PROCESSED）
  - **STEP 2a**：OrderType标准化（SO/MTO/1→SALES_ORDER；MTS/SS/SS_U/2→PRODUCTION_INSTRUCTION）
  - **STEP 2b**：CustomerSegment通过CustomerCodeMap派生；**⚠️ 无匹配→`UNKNOWN`（不再默认OVERSEAS）**；CustomerCode为NULL→NULL
  - **STEP 3：MERGE→Order_Canonical**：Upsert键=`SourceSystem+SourceOrderId`；NonStockShipmentType/OriginalOrderSource inline标准化；SourceModel透传
- **【2号位】** 提升成功 → `SyncStatus`=`PROCESSED`（含BOMNO_MISSING诊断的行也为PROCESSED）
- **【2号位】** 校验失败 → `SyncStatus`=`FAILED`，`FailureCode`单值最高优先级，`ErrorMessage`记录人类可读详情
- **【2号位】** ETL日志写入`APS_ETL_Log`表（含ValidatedCount/PromotedCount/FailedCount/BOMNOMissingCount四计数）

**数据流向**：
```
APS.ERP_Order_Staging(PENDING) → sp_ValidateAndPromoteOrders
  → 提升成功: Order_Canonical(Upsert) + Staging(PROCESSED) [可含 BOMNO_MISSING 诊断]
  → 硬失败: Staging(FAILED) + FailureCode单值 + NextActionCode + ErrorMessage留痕
```
> **⚠️ v5.0.27 设计红线**：
> - `FailureCode` 单值，只记最高优先级；`BOMNO_MISSING`=非阻断诊断（订单仍 PROCESSED）
> - 阻断以 `SyncStatus='FAILED'` 为准；`BOMNO_MISSING + WAIT_BOM_WORKSET` = 明确非阻断组合
> - `CustomerSegment='UNKNOWN'` ≠ `'OVERSEAS'`，消费方须识别 UNKNOWN 走保守路径
> - OrderType 未知尤禁写入 Canonical

---

### 📊 场景2：主数据变更与版本管理

**触发条件**：BOM、工艺路线、物料主数据发生变更

**流程步骤**：

#### **步骤2.1：变更检测**

**负责人**：2号位

**动作**：
- **【2号位】** 通过CDC监控主数据表变化
- **【2号位】** 检测到变更后，生成新版本号
- **【2号位】** 记录变更历史（ChangeLog）

**数据来源**：ERP主数据表（CDC监控）  
**数据去处**：APS主数据表 + ChangeLog

---

#### **步骤2.2：重排触发判断**

**负责人**：5号位

**动作**：
- **【5号位】** 调用 `IMasterDataChangeRule` 插件
- **【5号位】** 判断变更是否需要触发重排：
  - BOM结构变更：需要重排
  - 工艺路线变更：需要重排
  - 物料名称变更：不需要重排
- **【5号位】** 如果需要，通知 **【3号位】** 触发重排

**数据流向**：
```
主数据变更 → 5号位.重排判断 → 3号位.触发重排（如需要）
```

---

### 🔁 场景3：MES数据幂等性处理

**触发条件**：MES可能重复发送实绩事件

**流程步骤**：

#### **步骤3.1：EventId去重**

**负责人**：3号位

**动作**：
- **【3号位】** 接收MES事件时，检查 `EventId` 是否已处理
- **【3号位】** 维护已处理事件ID的缓存（Redis或内存）
- **【3号位】** 如果EventId已存在，直接返回成功（幂等）
- **【3号位】** 如果是新事件，继续处理

**数据流向**：
```
MES事件 → 3号位.EventId检查 → 去重/继续处理
```

---

#### **步骤3.2：状态机校验**

**负责人**：5号位

**动作**：
- **【5号位】** 校验Task状态转换的合法性：
  - PENDING → START：合法
  - START → COMPLETE：合法
  - COMPLETE → START：非法（已完成不能再开工）
- **【5号位】** 如果状态转换非法，记录错误日志但不抛异常
- **【5号位】** 保证幂等性：重复的COMPLETE事件不会导致错误

**数据流向**：
```
MES事件 → 5号位.状态机校验 → 合法则更新/非法则记录日志
```

**业务意义**：
- 确保APS与外部系统数据一致
- 通过CDC实现高效的增量同步
- 通过幂等性处理避免重复消费导致的数据错误

---

## 第七部分：监控反馈与容错流程（7个场景）

系统运行过程中需要实时监控和容错处理。以下是7个关键场景：

---

### 📊 场景1：排程KPI统计与分析

**触发条件**：排程完成后，自动计算KPI指标

**负责人**：3号位

**动作**：
- **【3号位】** 自动计算关键KPI：
  - 准时率：按时完成的订单数 / 总订单数
  - 产能利用率：实际加工时间 / 可用时间
  - 换型次数：总换型次数（越少越好）
  - 平均延期天数：延期订单的平均延期天数
- **【3号位】** 生成KPI报表
- **【3号位】** 与历史KPI对比，识别趋势

**数据来源**：Task + Order + ResourceCalendar  
**数据去处**：KPI报表

**数据流向**：
```
排程结果 → 3号位.KPI计算 → KPI报表 → 4号位.前端展示
```

---

### 🎯 场景2：交期达成率跟踪

**触发条件**：实时跟踪订单交期达成情况

**负责人**：3号位 + 5号位

**动作**：
- **【3号位】** 每日统计：
  - 应交订单数
  - 实际交付订单数
  - 延期订单数
- **【5号位】** 分析延期原因：
  - 设备故障导致
  - 物料短缺导致
  - 订单插单导致
- **【3号位】** 生成交期达成率报告

**数据流向**：
```
Order.CustomerDueDate vs Task.ActualEndTime → 3号位.统计 → 交期达成率报告
```

---

### 🔍 场景3：执行监控与偏差预警

**触发条件**：实时监控计划执行情况

**负责人**：3号位 + 4号位

**动作**：
- **【3号位】** 实时对比：
  - 计划开工时间 vs 实际开工时间
  - 计划完工时间 vs 实际完工时间
- **【3号位】** 计算偏差：
  - 如果偏差 > 阈值（如：延迟2小时），触发预警
- **【4号位】** 前端弹窗预警：
  - "任务T001预计延期4小时，影响订单SO123"
- **【3号位】** 发送通知给PMC

**数据流向**：
```
Task.PlannedTime vs Task.ActualTime → 3号位.偏差计算 → 预警通知
```

---

### ⏱️ 场景4：排程超时中断与恢复

**触发条件**：单域排程超过30分钟

**负责人**：3号位

**动作**：
- **【3号位】** Hangfire监控任务执行时间
- **【3号位】** 如果超过30分钟，中断任务
- **【3号位】** 保存部分结果（已排程的Task）
- **【3号位】** 记录中断原因和进度
- **【3号位】** 通知 **【0号位】** 决策：
  - 重试（调整参数）
  - 降级为粗排
  - 人工介入

**数据流向**：
```
Hangfire任务监控 → 超时检测 → 中断 → 部分结果保存 → 0号位决策
```

---

### 💾 场景5：内存溢出监控与告警

**触发条件**：排程过程中内存使用超过阈值

**负责人**：2号位

**动作**：
- **【2号位】** 在排程过程中监控内存使用：
  - 每5分钟检查一次GC堆内存
  - 阈值：2GB（可配置）
- **【2号位】** 如果超过阈值：
  - 触发GC.Collect()强制回收
  - 记录内存快照
  - 发送告警通知
- **【2号位】** 如果仍然超过，中断排程

**数据流向**：
```
GC.GetTotalMemory → 2号位.内存监控 → 超阈值告警 → 可能中断
```

---

### 🔌 场景6：数据库连接池管理

**触发条件**：数据库连接池耗尽

**负责人**：2号位

**动作**：
- **【2号位】** 配置连接池参数：
  - 最小连接数：10
  - 最大连接数：100
  - 连接超时：30秒
- **【2号位】** 监控连接池状态：
  - 当前活跃连接数
  - 等待连接的请求数
- **【2号位】** 如果连接池耗尽：
  - 记录错误日志
  - 拒绝新请求（返回503）
  - 发送告警通知
- **【2号位】** 定期检测死锁并自动恢复

**数据流向**：
```
连接池监控 → 2号位.状态检查 → 耗尽告警 + 拒绝请求
```

---

### ⚠️ 场景7：异常任务高亮显示

**触发条件**：任务出现异常状态

**负责人**：4号位

**动作**：
- **【4号位】** 在甘特图上高亮显示异常任务：
  - 红色：延期任务（DELAYED）
  - 橙色：暂停任务（PAUSED）
  - 灰色：取消任务（CANCELLED）
  - 黄色：物料短缺（MATERIAL_SHORTAGE）
- **【4号位】** 提供筛选功能：
  - 只显示异常任务
  - 按异常类型筛选
- **【4号位】** 点击异常任务，显示详细信息

**数据流向**：
```
Task.Status → 4号位.状态着色 → 甘特图高亮显示
```

**业务意义**：
- 实时掌握系统运行状态
- 及时发现并处理异常
- 通过KPI分析持续改进排程质量

---

## 第八部分：计划发布与版本管理（3个场景）

排程计划需要经过审批、下发、版本管理等流程。以下是3个关键场景：

---

### 📤 场景1：计划下发MES

**触发条件**：计划审批通过后，下发到MES执行

**流程步骤**：

#### **步骤1.1：下发准备**

**负责人**：3号位

**动作**：
- **【3号位】** 检查计划状态：
  - ApprovalStatus = APPROVED
  - IsFrozen = true（已冻结）
- **【3号位】** 筛选需要下发的Task：
  - 只下发Frozen区的任务（近3天）
  - 排除已下发的任务
- **【3号位】** 生成MES工单数据

**数据来源**：PlanVersion + Task  
**数据去处**：MES工单数据

---

#### **步骤1.2：批量下发**

**负责人**：3号位

**动作**：
- **【3号位】** 调用MES接口，批量下发工单
- **【3号位】** 记录下发状态：
  - Task.IsDispatched = true
  - Task.DispatchedTime = 当前时间
- **【3号位】** 等待MES确认回执

**数据流向**：
```
Task（Frozen区） → 3号位.MES接口 → MES工单 → 等待回执
```

---

#### **步骤1.3：下发确认与重试**

**负责人**：3号位

**动作**：
- **【3号位】** 接收MES确认回执
- **【3号位】** 如果下发成功，更新Task.DispatchStatus = SUCCESS
- **【3号位】** 如果下发失败：
  - 记录失败原因
  - 自动重试（最多3次）
  - 如果仍失败，通知PMC人工处理

**数据流向**：
```
MES回执 → 3号位.状态更新 → Task.DispatchStatus
```

---

### 🗂️ 场景2：版本切换与回滚

**触发条件**：需要切换到新版本或回滚到旧版本

**流程步骤**：

#### **步骤2.1：版本切换**

**负责人**：3号位

**动作**：
- **【3号位】** 执行版本指针切换：
  - 更新 `PlanVersion.Status=ACTIVE`
  - ActiveVersionId = 新版本ID
- **【3号位】** 版本切换是原子操作（微秒级）
- **【3号位】** 前端自动刷新，显示新版本计划

**数据流向**：
```
新版本ID → 3号位.版本切换 → PlanVersion.Status=ACTIVE → 前端刷新
```

---

#### **步骤2.2：版本回滚**

**负责人**：3号位 + 0号位

**动作**：
- **【0号位】** 决策：回滚到哪个历史版本
- **【3号位】** 执行回滚：
  - ActiveVersionId = 历史版本ID
- **【3号位】** 记录回滚原因
- **【3号位】** 通知相关人员

**示例**：
```
当前版本：V20260301_020000（有问题）
回滚到：V20260228_020000（上一个稳定版本）
```

**数据流向**：
```
0号位.回滚决策 → 3号位.版本切换 → 历史版本激活
```

---

### 📋 场景3：版本历史查询与对比

**触发条件**：PMC需要查看历史版本或对比差异

**流程步骤**：

#### **步骤3.1：版本列表查询**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** 显示版本历史列表：
  - 版本号
  - 生成时间
  - 审批状态
  - KPI指标（准时率、产能利用率）
- **【3号位】** 提供版本查询API

**数据流向**：
```
PlanVersion表 → 3号位.查询API → 4号位.版本列表展示
```

---

#### **步骤3.2：版本对比**

**负责人**：4号位 + 3号位

**动作**：
- **【4号位】** PMC选择两个版本进行对比
- **【3号位】** 计算差异：
  - 哪些Task的时间变了
  - 哪些订单的交期变了
  - KPI指标对比
- **【4号位】** 高亮显示差异

**数据流向**：
```
版本A + 版本B → 3号位.差异计算 → 4号位.差异高亮显示
```

**业务意义**：
- 确保计划正确下发到车间执行
- 支持版本回滚，应对紧急情况
- 通过版本对比，分析计划变化原因

---

## 总结：30个完整流程覆盖

本文档完整覆盖了APS系统的30个核心流程：

**第一部分：凌晨全量排程主流程**（6个阶段）
- 阶段0：触发起点
- 阶段1：数据备料（4个步骤）
- 阶段2：供需匹配（5个步骤）
- 阶段3：时空推演（3个步骤）
- 阶段4：业务回填（3个步骤）
- 阶段5：极速落盘（3个步骤）

**第二部分：跨厂协同全流程**（4个场景）
1. 同域跨厂物流（厂间订单发货Task）vs 异域跨族依赖（虚拟库存硬约束）
2. 跨域优先级继承（01:50预处理刷库）
3. 同域跨厂半成品在途管理（厂间订单发货Task场景）
4. 异域跨族上游延期自动顺延（虚拟库存硬约束场景）

**第三部分：分域计算全流程**（3个场景 + 1个补充）
1. 分域任务分配与并发调度（含V1共享资源配额/预留窗口粗隔离策略补充）
2. 分域失败重试与降级
3. 单向硬约束传递（废除事后修复，避免排程震荡）

**第四部分：动态实绩与异常重排**（6个场景）
1. MES实绩接收与去重
2. 设备故障与日历阻挡块生成
3. 任务暂停与恢复
4. 报废与补料
5. 订单取消与变更
6. 局部重排执行

**第五部分：人工干预与调整流程**（3个场景）
1. PMC手动拖拽调整
2. 任务手动锁定与优先级调整
3. 计划审批与冻结

**第六部分：数据同步与一致性流程**（3个场景）
1. ERP增量订单同步
2. 主数据变更与版本管理
3. MES数据幂等性处理

**第七部分：监控反馈与容错流程**（7个场景）
1. 排程KPI统计与分析
2. 交期达成率跟踪
3. 执行监控与偏差预警
4. 排程超时中断与恢复
5. 内存溢出监控与告警
6. 数据库连接池管理
7. 异常任务高亮显示

**第八部分：计划发布与版本管理**（3个场景）
1. 计划下发MES
2. 版本切换与回滚
3. 版本历史查询与对比

**总计**：30个完整流程，每个流程都明确标注了负责岗位（0-5号位）和数据流向。

---

## 附录A：CDC后台常驻守护进程（24小时流式同步）

**负责人**：2号位（数据基础设施）

**⚠️ 架构定位**：
- CDC增量同步是**24小时后台常驻守护进程（Daemon）**，与02:00排程主流程在**物理线程上彻底撕裂**
- 排程引擎在02:00只做**瞬间快照**，绝不等待CDC同步完成
- 这是"流批分离"的核心体现：流式同步（CDC）与批处理排程（02:00）互不阻塞

---

### 📡 CDC守护进程实现方式

**技术选型**：ASP.NET Core `BackgroundService`（IHostedService）

**为什么不用Hangfire每分钟跑一次？**
- Hangfire调度本身有开销，且容易堆积
- CDC需要的是**永远不会停止的死循环（Daemon）**，而不是定时任务

**代码契约（C#）**（2026-04-18 更新，对齐三层订单路径）：
```csharp
public class CdcSyncDaemon : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. 检测ERP订单表的CDC变更
                var changes = await _cdcEngine.DetectChanges("ERP.Order");
                
                // 2. 批量拉取增量数据（每次最多1000条）
                if (changes.Count > 0)
                {
                    var orders = await _erpDb.PullIncrementalOrders(changes);
                    
                    // 3. 写入Staging表（不是直接合并到Order表！）
                    //    完整路径：ERP_Order_Staging(PENDING)
                    //      → sp_ValidateAndPromoteOrders → Order_Canonical
                    //      → 夜间 sp_SyncOrdersToPartitionTable → Order
                    await _apsDb.InsertToOrderStaging(orders);
                    
                    _logger.LogInformation($"CDC同步完成：{orders.Count}条订单写入Staging");
                }
                
                // 4. 等待5秒后继续下一轮
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CDC同步异常，5秒后重试");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
```

---

### ⏱️ CDC触发频率设计

**推荐频率**：每5秒一次（可配置）

**频率权衡**：
- **太快（如1秒）**：数据库压力大，CDC本身开销高
- **太慢（如1分钟）**：白天插单响应慢，用户体验差
- **5秒**：平衡点，既保证实时性，又不给数据库造成过大压力

**批量拉取策略**：
- 每次最多拉取1000条变更记录
- 如果积压超过1000条，分多次拉取（避免单次事务过大）

---

### 🔒 02:00排程的"快照一刀切"机制

**隔离契约**：
- 排程引擎在02:00:00这一毫秒，直接读取APS数据库当前最新状态
- **绝对不检查**CDC同步到了哪里
- 如果ERP在02:00:01过来一个急单，只能等白天的"动态异常重排"或明天的全量排程

**数据库锁隔离**：
为了防止1号位拉取数据时被CDC的写入锁阻塞，2号位在拉取 `ScheduleContext` 的SQL中，必须强制使用：

**方式1：SQL Server事务隔离级别**
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED SNAPSHOT;
-- 或者在数据库级别开启RCSI
ALTER DATABASE APS SET READ_COMMITTED_SNAPSHOT ON;
```

**方式2：EF Core无跟踪查询**
```csharp
var orders = await _context.Orders
    .AsNoTracking()  // 不跟踪实体，避免锁
    .Where(o => o.Status == "ACTIVE")
    .ToListAsync();
```

---

### 🛡️ CDC守护进程的容错机制

**异常重试**：
- 如果CDC同步失败（如网络抖动、数据库死锁），记录错误日志，5秒后自动重试
- 不影响排程主流程

**监控告警**：
- 如果CDC连续失败超过10次，发送告警通知给运维
- 如果CDC积压超过10000条未同步，发送告警通知

**降级策略**：
- 如果CDC守护进程崩溃，排程主流程仍然可以正常运行（只是数据可能不是最新的）
- 运维修复CDC后，会自动追赶积压的变更

---

### 📊 CDC与排程主流程的时间线对比

```
时间轴：
01:50 - 2号位执行跨域依赖静态扫描（阶段0.5）
02:00 - 3号位触发排程主流程（阶段0）
02:00:00 - 2号位瞬间快照读取APS数据库（阶段1，不等CDC）
02:00:01 - 1号位开始纯内存排程（阶段2-4）
02:15 - 2号位批量落盘（阶段5）
02:15:01 - 3号位版本切换（阶段5）
02:15:02 - 排程主流程结束

同时：
00:00 - CDC守护进程持续运行（每5秒一次）
01:59:55 - CDC正在同步ERP数据
02:00:00 - 排程引擎快照读取（CDC可能还在同步，但排程不等）
02:00:05 - CDC继续同步（与排程并行）
...
23:59:55 - CDC仍在运行（24小时不停）
```

**架构收益**：
- 流式同步（CDC）与批处理排程（02:00）完全解耦
- 排程引擎不会被CDC的I/O阻塞，15分钟高性能目标可达成
- 白天插单通过CDC实时同步，触发动态重排
- 凌晨全量排程用快照数据，保证稳定性

---

## 附录B：架构合理性说明

（保持原有内容不变）

