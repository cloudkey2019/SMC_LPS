# APS 数据库表结构字段说明文档（业务确认版）

**版本**：v2.0  
**日期**：2026-03-11  
**目的**：供0号位（业务负责人）确认数据库设计是否满足业务需求  
**基于**：《APS_数据库表结构设计_v2.0.sql》（v2.4架构终极修复完全体）  
**确认人**：0号位（精益架构师/业务负责人）

**⚠️ 重要更新（v2.0）**：
- 已同步v2.4架构终极修复（7个P0 + 5个P1修复）
- 库存表结构重构：废弃旧Inventory表，改用InventoryFact_ERP、InventoryFact_MES、InventoryBalance三层架构
- 新增MaterialMapping表（物料映射，SCD Type 2拉链表）
- 新增InventorySourcePriority表（库存优先级配置）

---

## 📋 文档说明

本文档将数据库表结构转换为业务易读的字段说明格式，每个表包含：
- **表名**：数据库表名称
- **业务用途**：该表在业务中的作用
- **字段清单**：包含英文字段名、中文含义、数据类型、业务说明、示例值

**请0号位重点确认**：
1. ✅ 字段是否覆盖了所有业务场景
2. ✅ 字段含义是否与业务理解一致
3. ✅ 是否有遗漏的关键业务字段
4. ✅ 枚举值（如订单类型、状态）是否完整

---

## 一、主数据表（Master Data）

### 1.1 ProductFamily（产品族配置表）

**业务用途**：定义7个产品族的基础信息，用于分域计算

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 产品族ID | INT | 主键，自增 | 1 |
| Code | 产品族代码 | NVARCHAR(50) | 唯一标识，如"X1_整机" | X1_MACHINE |
| Name | 产品族名称 | NVARCHAR(200) | 显示名称 | 整机产品族 |
| Description | 描述 | NVARCHAR(500) | 详细说明 | 包含最终装配的整机产品 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ 7个产品族的Code命名规则是否符合企业标准？
- ✅ 是否需要增加"负责人"、"优先级"等字段？

---

### 1.2 Factory（工厂表）

**业务用途**：定义多工厂信息，支持跨厂协同排程

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 工厂ID | INT | 主键，自增 | 1 |
| Code | 工厂代码 | NVARCHAR(50) | 唯一标识 | F001 |
| Name | 工厂名称 | NVARCHAR(200) | 显示名称 | 深圳工厂 |
| Location | 地理位置 | NVARCHAR(200) | 详细地址 | 广东省深圳市宝安区 |
| TimeZone | 时区 | NVARCHAR(50) | 用于跨时区排程 | China Standard Time |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ 工厂代码是否与ERP系统一致？
- ✅ 是否需要增加"工厂类型"（自有/外协）字段？

---

### 1.3 Resource（资源表 - 设备/机台/产线）

**业务用途**：定义生产资源（设备、机台、产线），支持排程分配

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 资源ID | INT | 主键，自增 | 1 |
| Code | 资源代码 | NVARCHAR(50) | 设备编号 | MC-001 |
| Name | 资源名称 | NVARCHAR(200) | 设备名称 | 注塑机001号 |
| FactoryId | 所属工厂ID | INT | 外键关联Factory表 | 1 |
| ResourceGroupId | 资源组ID | INT | 外键关联ResourceGroup表 | 10 |
| ResourceType | 资源类型 | NVARCHAR(50) | MACHINE（机器）、LINE（产线）、WORKSTATION（工位）、LOGISTICS（物流） | MACHINE |
| Capacity | 产能系数 | DECIMAL(18,4) | 产能倍率，1.0=标准产能 | 1.2 |
| Status | 设备状态 | NVARCHAR(20) | AVAILABLE（可用）、DOWN（故障）、MAINTENANCE（维护）、OFFLINE（离线） | AVAILABLE |
| BreakdownStartTime | 故障开始时间 | DATETIME2 | MES实绩：设备故障时间 | 2026-03-05 14:30:00 |
| EstimatedRepairTime | 预计修复时长 | INT | 单位：分钟 | 120 |
| BreakdownReason | 故障原因 | NVARCHAR(500) | 故障描述 | 主轴轴承损坏 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 15:00:00 |

**业务确认点**：
- ✅ ResourceType枚举值是否覆盖所有设备类型？
- ✅ 是否需要增加"设备负责人"、"维护周期"字段？
- ✅ 故障信息字段是否满足MES实绩处理需求？

---

### 1.4 ResourceGroup（资源组表）

**业务用途**：定义次资源（人员、工装、夹具）的容量管理

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 资源组ID | INT | 主键，自增 | 1 |
| Code | 资源组代码 | NVARCHAR(50) | 唯一标识 | OPER-MC |
| Name | 资源组名称 | NVARCHAR(200) | 显示名称 | 注塑操作工 |
| FactoryId | 所属工厂ID | INT | 外键关联Factory表 | 1 |
| GroupType | 资源组类型 | NVARCHAR(50) | PERSONNEL（人员）、TOOLING（工装）、FIXTURE（夹具） | PERSONNEL |
| TotalCapacity | 总容量 | INT | 如：操作工总人数 | 20 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ GroupType枚举值是否覆盖所有次资源类型？
- ✅ V1.0是否需要支持次资源约束？（如果不需要，可以V2.0再实现）

---

### 1.5 ResourceCalendar（资源日历表）

**业务用途**：定义设备的班次、假日、维护计划

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 日历ID | BIGINT | 主键，自增 | 1 |
| ResourceId | 资源ID | INT | 外键关联Resource表 | 1 |
| CalendarDate | 日历日期 | DATE | 日期 | 2026-03-05 |
| ShiftType | 班次类型 | NVARCHAR(50) | DAY_SHIFT（白班）、NIGHT_SHIFT（夜班）、OVERTIME（加班） | DAY_SHIFT |
| StartTime | 开始时间 | TIME | 班次开始时间 | 08:00:00 |
| EndTime | 结束时间 | TIME | 班次结束时间 | 20:00:00 |
| AvailableHours | 可用工时 | DECIMAL(5,2) | 扣除休息时间后的净工时 | 11.5 |
| IsAvailable | 是否可用 | BIT | 1=可用，0=不可用（假日/维护） | 1 |
| Reason | 不可用原因 | NVARCHAR(200) | 如：国庆假期、设备维护 | 设备年度保养 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |

**业务确认点**：
- ✅ ShiftType枚举值是否覆盖所有班次类型？
- ✅ 是否需要支持"弹性班次"（如：早班、中班、晚班）？

---

### 1.6 Material（物料主数据表）

**业务用途**：定义所有物料（成品、半成品、原材料）的基础信息

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 物料ID | INT | 主键，自增 | 1 |
| Code | 物料编码 | NVARCHAR(100) | 唯一标识，与ERP一致 | MAT-10001 |
| Name | 物料名称 | NVARCHAR(200) | 显示名称 | 整机外壳（黑色） |
| ProductFamilyId | 产品族ID | INT | 外键关联ProductFamily表 | 1 |
| MaterialType | 物料类型 | NVARCHAR(50) | FINISHED_GOOD（成品）、SEMI_FINISHED（半成品）、RAW_MATERIAL（原材料） | SEMI_FINISHED |
| UOM | 计量单位 | NVARCHAR(20) | PCS（件）、KG（千克）、M（米）等 | PCS |
| LeadTimeDays | 提前期天数 | INT | 采购或生产提前期 | 7 |
| SafetyStock | 安全库存 | DECIMAL(18,4) | 最低库存量 | 500 |
| LowLevelCode | 低阶码（LLC） | INT | 用于BOM拓扑排序，0=顶层 | 2 |
| IsPurchased | 是否采购件 | BIT | 1=采购件，0=自制件 | 0 |
| IsSimpleItem | 是否简单物料 | BIT | 1=简单物料（可自动生成默认工艺） | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ MaterialType枚举值是否覆盖所有物料类型？
- ✅ LowLevelCode（低阶码）是否需要手动维护，还是系统自动计算？
- ✅ 是否需要增加"替代料"、"物料属性"（颜色、规格）字段？

---

### 1.7 BOM（物料清单表）

**业务用途**：定义物料之间的父子关系（成品由哪些半成品/原材料组成）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | BOM ID | BIGINT | 主键，自增 | 1 |
| ParentMaterialId | 父物料ID | INT | 外键关联Material表（成品） | 100 |
| ChildMaterialId | 子物料ID | INT | 外键关联Material表（半成品/原材料） | 200 |
| Quantity | 用量 | DECIMAL(18,6) | 1个父物料需要多少个子物料 | 2.5 |
| ScrapRate | 损耗率 | DECIMAL(5,4) | 0.05表示5%损耗 | 0.05 |
| LeadTimeOffset | 提前期偏移 | INT | 单位：天，负数表示提前 | -2 |
| BOMLevel | BOM层级 | INT | 1-10，1表示直接子件 | 1 |
| EffectiveFrom | 生效日期 | DATE | BOM生效开始日期 | 2026-01-01 |
| EffectiveTo | 失效日期 | DATE | BOM失效日期，NULL表示永久有效 | NULL |
| IsVirtual | 是否虚拟BOM | BIT | 1=系统自动生成，0=手动维护 | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedBy | 创建人 | NVARCHAR(100) | MANUAL（手动）或系统用户 | MANUAL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ ScrapRate（损耗率）是否需要按工序维护，还是按BOM维护？
- ✅ 是否需要支持"替代BOM"（多个BOM版本）？
- ✅ BOM环路检测是否需要在数据库层面约束？

---

### 1.8 Routing（工艺路线表）

**业务用途**：定义物料的加工工序（加工路线、工时、设备）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 工艺ID | BIGINT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| OperationSeq | 工序序号 | INT | 1-20，表示加工顺序 | 10 |
| OperationCode | 工序代码 | NVARCHAR(50) | 工序唯一标识 | OP-010 |
| OperationName | 工序名称 | NVARCHAR(200) | 显示名称 | 注塑成型 |
| ProcessType | 工艺类型 | NVARCHAR(50) | ASSEMBLY（装配）、MACHINING（机加工）、INSPECTION（检验）、PROCUREMENT（采购） | MACHINING |
| ResourceGroupId | 资源组ID | INT | 外键关联ResourceGroup表 | 10 |
| StandardDuration | 标准工时 | DECIMAL(18,4) | 单位：小时 | 2.5 |
| SetupTime | 准备时间 | DECIMAL(18,4) | 换型时间，单位：小时 | 0.5 |
| MinBatchSize | 最小批量 | DECIMAL(18,4) | 最小加工批量 | 10 |
| MaxBatchSize | 最大批量 | DECIMAL(18,4) | 最大加工批量 | 1000 |
| EffectiveFrom | 生效日期 | DATE | 工艺生效开始日期 | 2026-01-01 |
| EffectiveTo | 失效日期 | DATE | 工艺失效日期，NULL表示永久有效 | NULL |
| IsDefault | 是否默认工艺 | BIT | 1=系统自动生成，0=手动维护 | 0 |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedBy | 创建人 | NVARCHAR(100) | MANUAL（手动）或系统用户 | MANUAL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ ProcessType枚举值是否覆盖所有工艺类型？
- ✅ SetupTime（换型时间）是否需要按"从A物料换到B物料"维护换型矩阵？
- ✅ 是否需要支持"并行工序"（多个工序同时进行）？

---

### 1.9 MaterialMapping（物料映射表 - SCD Type 2拉链表）⭐ v2.0新增

**业务用途**：记录物料在ERP和MES系统中的映射关系，支持一物多仓、历史追溯

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 映射ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 核心业务键 | RAW-STEEL-001 |
| ERP_MasterID | ERP主数据ID | INT | ERP系统中的物料ID | 100001 |
| ERP_Warehouse | ERP仓库编码 | NVARCHAR(50) | 支持一物多仓 | WH-01 |
| MES_ID | MES自建物料ID | INT | MES系统中的物料ID | 5001 |
| Source | 来源系统 | NVARCHAR(20) | ERP（ERP主数据）、MES_CUSTOM（MES自建物料） | ERP |
| ValidFrom | 生效开始时间 | DATETIME2 | SCD Type 2：记录生效时间 | 2026-01-01 00:00:00 |
| ValidTo | 生效结束时间 | DATETIME2 | SCD Type 2：记录失效时间，NULL表示当前有效 | NULL |
| IsCurrent | 是否当前版本 | BIT | 1=当前有效，0=历史版本 | 1 |
| ERP_Warehouse_Norm | 仓库标准化值 | NVARCHAR(50) | 持久化计算列，用于唯一索引 | WH-01 |
| MES_ID_Norm | MES_ID标准化值 | INT | 持久化计算列，用于唯一索引 | 5001 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 10:00:00 |

**业务确认点**：
- ✅ 一物多仓场景：同一个物料在不同仓库有不同的ERP_MasterID，是否符合业务实际？
- ✅ 历史追溯：当物料映射关系变更时，是否需要保留历史版本？
- ✅ MES自建物料：是否存在MES系统自建的物料（不在ERP中）？

---

### 1.10 InventoryFact_ERP（ERP库存事实表）⭐ v2.0新增

**业务用途**：记录从ERP系统同步的库存数据（原始数据，不做修改）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务键 | RAW-STEEL-001 |
| FactoryId | 工厂ID | INT | 外键关联Factory表 | 1 |
| Warehouse | 仓库编码 | NVARCHAR(50) | ERP仓库 | WH-01 |
| OnHandQty | 现有量 | DECIMAL(18,4) | ERP系统的实际库存 | 1000 |
| SyncedAt | 同步时间 | DATETIME2 | 从ERP同步的时间戳 | 2026-03-05 16:00:00 |
| SourceSystem | 来源系统 | NVARCHAR(50) | 固定值：ERP | ERP |

**业务确认点**：
- ✅ ERP库存是否包含在途库存？
- ✅ 同步频率是多久？（建议：每小时同步一次）

---

### 1.11 InventoryFact_MES（MES库存事实表）⭐ v2.0新增

**业务用途**：记录从MES系统同步的库存数据（原始数据，不做修改）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务键 | RAW-STEEL-001 |
| FactoryId | 工厂ID | INT | 外键关联Factory表 | 1 |
| Location | 库位 | NVARCHAR(100) | MES系统的库位 | 车间A-货架01 |
| OnHandQty | 现有量 | DECIMAL(18,4) | MES系统的实际库存 | 500 |
| SyncedAt | 同步时间 | DATETIME2 | 从MES同步的时间戳 | 2026-03-05 16:05:00 |
| SourceSystem | 来源系统 | NVARCHAR(50) | 固定值：MES | MES |

**业务确认点**：
- ✅ MES库存是否包含在制品（WIP）？
- ✅ MES库存与ERP库存是否存在差异？如何处理？

---

### 1.12 InventoryBalance（库存余额表 - 汇聚表）⭐ v2.0新增

**业务用途**：汇聚ERP和MES的库存数据，提供统一的库存查询视图

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 记录ID | BIGINT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | 业务键 | RAW-STEEL-001 |
| FactoryId | 工厂ID | INT | 外键关联Factory表 | 1 |
| OnHandQty | 现有量 | DECIMAL(18,4) | 汇聚后的总库存 | 1500 |
| AllocatedQty | 已分配量 | DECIMAL(18,4) | 已被订单占用的数量 | 200 |
| AvailableQty | 可用量 | DECIMAL(18,4) | 计算列：现有量-已分配量 | 1300 |
| Source | 主要来源 | NVARCHAR(20) | ERP、MES、BOTH（双源汇聚） | BOTH |
| LastUpdatedAt | 最后更新时间 | DATETIME2 | 库存最后更新时间 | 2026-03-05 16:10:00 |

**业务确认点**：
- ✅ 当ERP和MES库存都存在时，如何汇聚？（求和？优先级？）
- ✅ 库存扣减时，优先扣减哪个来源的库存？
- ✅ 是否需要支持"安全库存预警"？

---

### 1.13 InventorySourcePriority（库存来源优先级配置表）⭐ v2.0新增

**业务用途**：配置不同物料的库存来源优先级（ERP优先 or MES优先）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 配置ID | INT | 主键，自增 | 1 |
| MaterialCode | 物料编码 | NVARCHAR(50) | NULL表示全局默认 | RAW-STEEL-001 |
| FactoryId | 工厂ID | INT | NULL表示全局默认 | 1 |
| PrimarySource | 主要来源 | NVARCHAR(20) | ERP、MES | ERP |
| FallbackSource | 备用来源 | NVARCHAR(20) | ERP、MES | MES |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 10:00:00 |

**业务确认点**：
- ✅ 默认优先级是ERP还是MES？
- ✅ 是否存在某些物料只在MES中有库存（例如半成品）？
- ✅ 当主要来源库存不足时，是否自动使用备用来源？

---

### 1.14 FenceConfig（冻结区/锁定区配置表）

**业务用途**：定义不同工艺类型的冻结区和锁定区天数

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 配置ID | INT | 主键，自增 | 1 |
| ProductFamilyId | 产品族ID | INT | 外键关联ProductFamily表 | 1 |
| FactoryId | 工厂ID | INT | 外键关联Factory表，NULL表示全局 | NULL |
| ProcessType | 工艺类型 | NVARCHAR(50) | ASSEMBLY（装配）、MACHINING（机加工）、PROCUREMENT_DOMESTIC（国内采购）、PROCUREMENT_IMPORT（进口采购） | ASSEMBLY |
| FrozenDays | 冻结区天数 | INT | 冻结区：不允许修改 | 3 |
| FirmDays | 锁定区天数 | INT | 锁定区：需要审批才能修改 | 2 |
| EffectiveFrom | 生效日期 | DATE | 配置生效开始日期 | 2026-01-01 |
| EffectiveTo | 失效日期 | DATE | 配置失效日期，NULL表示永久有效 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ FrozenDays和FirmDays的天数是否符合实际业务需求？
- ✅ 是否需要按"客户类型"（VIP/普通）设置不同的冻结区？

---

## 二、计划版本与订单表

### 2.1 PlanVersion（计划版本表）

**业务用途**：记录每次排程的版本信息（支持多版本对比和回滚）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 版本ID | INT | 主键，自增 | 1 |
| VersionCode | 版本编号 | NVARCHAR(50) | 唯一标识，如：20260305_020000 | 20260305_020000 |
| VersionType | 版本类型 | NVARCHAR(50) | DAILY（每日排程）、AUDIT（审计版本）、MILESTONE（里程碑）、MANUAL（手动排程） | DAILY |
| DomainKey | 分域标识 | NVARCHAR(100) | 产品族_工厂，如：X1_F001 | X1_F001 |
| PlanHorizonStart | 计划开始日期 | DATE | 排程时间范围起始 | 2026-03-05 |
| PlanHorizonEnd | 计划结束日期 | DATE | 排程时间范围结束 | 2026-04-05 |
| ComputeMode | 计算模式 | NVARCHAR(50) | FULL_DETAIL（全量精排）、CRITICAL_PATH（关键路径）、ROUGH_CUT（粗排）、DOMAIN_SPLIT（分域并发） | DOMAIN_SPLIT |
| Status | 状态 | NVARCHAR(50) | RUNNING（运行中）、COMPLETED（完成）、FAILED（失败）、ARCHIVED（已归档） | COMPLETED |
| StartedAt | 开始时间 | DATETIME2 | 排程开始时间 | 2026-03-05 02:00:00 |
| CompletedAt | 完成时间 | DATETIME2 | 排程完成时间 | 2026-03-05 02:12:30 |
| DurationSeconds | 耗时（秒） | INT | 排程总耗时 | 750 |
| TotalOrders | 订单总数 | INT | 本次排程处理的订单数 | 5000 |
| TotalTasks | 任务总数 | INT | 本次排程生成的任务数 | 50000 |
| ErrorMessage | 错误信息 | NVARCHAR(MAX) | 排程失败时的错误描述 | NULL |
| CreatedBy | 创建人 | NVARCHAR(100) | 触发排程的用户或系统 | SYSTEM |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:00:00 |
| ArchivedAt | 归档时间 | DATETIME2 | 版本归档时间 | NULL |

**业务确认点**：
- ✅ VersionType枚举值是否覆盖所有排程场景？
- ✅ 是否需要增加"排程触发原因"字段？

---

### 2.2 Order（订单表）

**业务用途**：记录所有订单（销售订单、MTS备货单、安全库存单）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 订单ID | BIGINT | 主键，自增 | 1 |
| OrderNo | 订单号 | NVARCHAR(50) | 唯一标识，与ERP一致 | SO-20260305-001 |
| OrderType | 订单类型 | NVARCHAR(20) | SO（销售订单）、MTS（备货单）、SS（安全库存）、SS_U（紧急安全库存） | SO |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| ProductFamilyId | 产品族ID | INT | 外键关联ProductFamily表 | 1 |
| FactoryId | 工厂ID | INT | 外键关联Factory表 | 1 |
| Quantity | 数量 | DECIMAL(18,4) | 订单数量 | 1000 |
| UOM | 计量单位 | NVARCHAR(20) | PCS、KG、M等 | PCS |
| CustomerDueDate | 客户要求交期 | DATE | 客户要求的交货日期 | 2026-03-20 |
| PromisedDate | 承诺交期 | DATE | APS承诺的交货日期（ATP/CTP结果） | 2026-03-18 |
| Priority | 基础优先级 | INT | 1-100，手动设置的优先级 | 80 |
| PriorityScore | 综合优先级分数 | DECIMAL(10,2) | 步骤2.0计算的综合分数（含VIP加分、交期紧急度等） | 950.5 |
| ScoreCalculatedAt | 打分时间戳 | DATETIME2 | 用于审计 | 2026-03-05 02:01:00 |
| BaseScore | 基础备货分数 | DECIMAL(10,2) | 供给池单据的默认分数（如10分） | 10 |
| Status | 订单状态 | NVARCHAR(50) | NEW（新建）、PLANNED（已排程）、CONFIRMED（已确认）、IN_PROGRESS（进行中）、COMPLETED（已完成）、CANCELLED（已取消）、DATA_INCOMPLETE（数据不完整） | PLANNED |
| DomainKey | 分域标识 | NVARCHAR(100) | 产品族_工厂 | X1_F001 |
| IsCrossDomain | 是否跨域订单 | BIT | 1=跨域，0=单域 | 0 |
| SourceSystem | 来源系统 | NVARCHAR(50) | ERP、MES、MANUAL等 | ERP |
| SourceOrderId | 源系统订单ID | NVARCHAR(100) | ERP中的订单主键 | 123456 |
| ValidationError | 数据验证错误 | NVARCHAR(500) | 数据质量问题描述 | NULL |
| OverrideBOMId | 手动指定BOM | BIGINT | 外键关联BOM表 | NULL |
| OverrideRoutingId | 手动指定工艺 | BIGINT | 外键关联Routing表 | NULL |
| IsPurchasedItem | 是否采购件 | BIT | 1=采购件，0=自制件 | 0 |
| IsUrgent | 是否紧急订单 | BIT | 1=紧急，0=普通 | 0 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 01:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-05 02:00:00 |

**业务确认点**：
- ✅ OrderType枚举值是否覆盖所有订单类型？
- ✅ PriorityScore（综合优先级分数）的计算规则是否符合业务需求？
- ✅ 是否需要增加"客户编号"、"客户名称"字段？

---

### 2.3 Task（任务表）

**业务用途**：记录排程生成的所有加工任务（分区表，按PlanVersionId分区）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 任务ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联PlanVersion表 | 1 |
| TaskNo | 任务编号 | NVARCHAR(50) | 唯一标识 | TASK-20260305-000001 |
| OrderId | 订单ID | BIGINT | 外键关联Order表 | 100 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 200 |
| OperationSeq | 工序序号 | INT | 1-20 | 10 |
| OperationCode | 工序代码 | NVARCHAR(50) | 工序标识 | OP-010 |
| ResourceId | 资源ID | INT | 外键关联Resource表 | 5 |
| ResourceGroupId | 资源组ID | INT | 外键关联ResourceGroup表 | 10 |
| Quantity | 数量 | DECIMAL(18,4) | 任务数量 | 500 |
| UOM | 计量单位 | NVARCHAR(20) | PCS、KG、M等 | PCS |
| PlannedStartTime | 计划开始时间 | DATETIME2 | APS排程的开始时间 | 2026-03-06 08:00:00 |
| PlannedEndTime | 计划结束时间 | DATETIME2 | APS排程的结束时间 | 2026-03-06 10:30:00 |
| Duration | 工时 | DECIMAL(18,4) | 单位：小时 | 2.5 |
| Status | 任务状态 | NVARCHAR(50) | PLANNED（已排程）、RELEASED（已下发）、IN_PROGRESS（进行中）、COMPLETED（已完成）、CANCELLED（已取消）、SUSPENDED（已暂停） | PLANNED |
| IsLocked | 是否在冻结区 | BIT | 1=冻结区（不可修改），0=非冻结区 | 1 |
| IsCriticalPath | 是否关键路径 | BIT | 1=关键路径，0=非关键路径 | 1 |
| TaskType | 任务类型 | NVARCHAR(50) | PRODUCTION（生产）、TRANSFER（转移）、PROCUREMENT（采购） | PRODUCTION |
| SourceFactory | 源工厂 | INT | 外键关联Factory表（跨厂时） | 1 |
| TargetFactory | 目标工厂 | INT | 外键关联Factory表（跨厂时） | 2 |
| ActualStartTime | 实际开始时间 | DATETIME2 | MES实绩：实际开工时间 | 2026-03-06 08:05:00 |
| ActualEndTime | 实际结束时间 | DATETIME2 | MES实绩：实际完工时间 | 2026-03-06 10:40:00 |
| ActualQuantity | 实际完工数量 | DECIMAL(18,4) | MES实绩：实际产量 | 495 |
| ScrapQuantity | 报废数量 | DECIMAL(18,4) | MES实绩：报废数量 | 5 |
| DelayMinutes | 延迟时长 | INT | 单位：分钟 | 15 |
| SuspendedAt | 暂停时间 | DATETIME2 | 任务暂停时间 | NULL |
| SuspendReason | 暂停原因 | NVARCHAR(200) | 暂停原因描述 | NULL |
| ResumedAt | 恢复时间 | DATETIME2 | 任务恢复时间 | NULL |
| SetupAttribute | 换型属性 | NVARCHAR(100) | 模具编号/颜色代码/材质规格等 | MOLD-001 |
| BatchSeq | 批次序号 | INT | 拆批后的序号（1,2,3...） | 1 |
| IsBatchSplit | 是否拆批生成 | BIT | 1=拆批生成，0=正常生成 | 1 |
| SplitReason | 拆批理由 | NVARCHAR(200) | 如："设备负荷85%，下游有紧急订单" | 设备负荷85%，下游有紧急订单 |
| OriginalOrderNo | 原始订单号 | NVARCHAR(50) | 拆批前的订单号 | SO-20260305-001 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:05:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-03-06 10:40:00 |

**业务确认点**：
- ✅ Status枚举值是否覆盖所有任务状态？
- ✅ MES实绩字段（ActualStartTime、ActualQuantity等）是否满足车间反馈需求？
- ✅ 拆批追溯字段（BatchSeq、SplitReason等）是否满足PMC追溯需求？
- ✅ SetupAttribute（换型属性）的内容格式是否需要标准化？

---

## 三、Pegging与追溯表

### 3.1 Pegging（供需关系表）

**业务用途**：记录任务之间的供需血缘关系（分区表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | Pegging ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联PlanVersion表 | 1 |
| UpstreamTaskId | 上游任务ID | BIGINT | 供给方Task | 100 |
| DownstreamTaskId | 下游任务ID | BIGINT | 需求方Task | 200 |
| UpstreamMaterialId | 上游物料ID | INT | 供给物料 | 50 |
| DownstreamMaterialId | 下游物料ID | INT | 需求物料 | 100 |
| Quantity | 数量 | DECIMAL(18,4) | Pegging数量 | 500 |
| UOM | 计量单位 | NVARCHAR(20) | PCS、KG、M等 | PCS |
| PeggingType | Pegging类型 | NVARCHAR(50) | BOM（BOM关系）、ROUTING（工序关系）、TRANSFER（跨厂转移） | BOM |
| LeadTimeDays | 提前期天数 | INT | 物流提前期 | 2 |
| IsCrossDomain | 是否跨域 | BIT | 1=跨域，0=单域 | 0 |
| AllocatedQuantity | 分配数量 | DECIMAL(18,4) | 该Pegging分配的具体数量 | 500 |
| InheritedPriority | 继承的优先级 | INT | 如：900分来自VIP客户 | 900 |
| AllocationReason | 分配理由 | NVARCHAR(200) | 如："来自VIP客户"、"基础备货" | 来自VIP客户 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:05:00 |

**业务确认点**：
- ✅ PeggingType枚举值是否覆盖所有供需关系类型？
- ✅ AllocatedQuantity和InheritedPriority字段是否满足血缘分配账本需求？

---

### 3.2 ExplainTrace（可解释性追踪表）

**业务用途**：记录排程决策的可解释性信息（分区表）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 追踪ID | BIGINT | 主键，自增 | 1 |
| PlanVersionId | 计划版本ID | INT | 外键关联PlanVersion表 | 1 |
| TaskId | 任务ID | BIGINT | 外键关联Task表 | 100 |
| TraceType | 追踪类型 | NVARCHAR(50) | RESOURCE_SELECTION（资源选择）、TIME_CALCULATION（时间计算）、CONSTRAINT_VIOLATION（约束冲突）、PEGGING_BREAK（Pegging断裂） | RESOURCE_SELECTION |
| TraceLevel | 追踪级别 | NVARCHAR(20) | INFO（信息）、WARNING（警告）、ERROR（错误） | INFO |
| Message | 追踪消息 | NVARCHAR(MAX) | 可读的决策说明 | 选择资源MC-001，因为负荷率最低（65%） |
| ContextData | 上下文数据 | NVARCHAR(MAX) | JSON格式的详细数据 | {"resourceId":5,"loadRate":0.65} |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-03-05 02:05:00 |

**业务确认点**：
- ✅ TraceType枚举值是否覆盖所有需要追踪的决策类型？
- ✅ 可解释性信息是否需要在前端展示给PMC？

---

## 四、拆批规则配置表

### 4.1 TaskSplitRuleConfig（拆批规则配置表）

**业务用途**：PMC配置拆批规则（MOQ、EOQ、瓶颈设备策略）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 规则ID | INT | 主键，自增 | 1 |
| MaterialId | 物料ID | INT | 外键关联Material表 | 100 |
| ResourceGroupId | 资源组ID | INT | 外键关联ResourceGroup表 | 10 |
| MinimumOrderQuantity | 最小经济批量（MOQ） | DECIMAL(18,4) | 最小加工批量 | 100 |
| EconomicOrderQuantity | 最大经济批量（EOQ） | DECIMAL(18,4) | 最大加工批量 | 1000 |
| BottleneckSplitStrategy | 瓶颈设备拆批策略 | NVARCHAR(50) | PREFER_SPLIT（倾向拆批）、PREFER_MERGE（倾向合批） | PREFER_SPLIT |
| NonBottleneckStrategy | 非瓶颈设备策略 | NVARCHAR(50) | PREFER_LARGE_BATCH（倾向大批量）、PREFER_SMALL_BATCH（倾向小批量） | PREFER_LARGE_BATCH |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| EffectiveFrom | 生效时间 | DATETIME2 | 规则生效开始时间 | 2026-01-01 00:00:00 |
| EffectiveTo | 失效时间 | DATETIME2 | 规则失效时间，NULL表示永久有效 | NULL |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |
| CreatedBy | 创建人 | NVARCHAR(50) | 创建该规则的用户 | PMC_ZHANG |
| UpdatedBy | 更新人 | NVARCHAR(50) | 最后更新该规则的用户 | PMC_ZHANG |

**业务确认点**：
- ✅ MOQ和EOQ的数值范围是否符合实际生产需求？
- ✅ BottleneckSplitStrategy和NonBottleneckStrategy的策略是否覆盖所有场景？
- ✅ 是否需要增加"拆批优先级"字段（多个规则冲突时的优先级）？

---

## 五、审批流表（V2.0功能，V1.0可选）

### 5.1 ApprovalFlow（审批流定义表）

**业务用途**：定义审批流程（冻结区修改、牺牲订单、暂停已开工任务）

| 英文字段名 | 中文含义 | 数据类型 | 业务说明 | 示例值 |
|-----------|---------|---------|---------|--------|
| Id | 审批流ID | INT | 主键，自增 | 1 |
| FlowCode | 审批流代码 | NVARCHAR(50) | 唯一标识 | FROZEN_CHANGE |
| FlowName | 审批流名称 | NVARCHAR(200) | 显示名称 | 冻结区修改审批 |
| FlowType | 审批流类型 | NVARCHAR(50) | FROZEN_CHANGE（冻结区修改）、SACRIFICE_SO（牺牲订单）、PAUSE_STARTED（暂停已开工） | FROZEN_CHANGE |
| ApprovalLevels | 审批级数 | INT | 1-3级审批 | 2 |
| TimeoutHours | 超时时长 | INT | 单位：小时 | 24 |
| TimeoutAction | 超时动作 | NVARCHAR(50) | AUTO_APPROVE（自动通过）、AUTO_REJECT（自动拒绝）、ESCALATE（升级） | AUTO_REJECT |
| IsActive | 是否启用 | BIT | 1=启用，0=停用 | 1 |
| CreatedAt | 创建时间 | DATETIME2 | 记录创建时间 | 2026-01-01 08:00:00 |
| UpdatedAt | 更新时间 | DATETIME2 | 最后更新时间 | 2026-01-15 10:30:00 |

**业务确认点**：
- ✅ FlowType枚举值是否覆盖所有需要审批的场景？
- ✅ TimeoutAction的默认行为是否符合业务需求？
- ✅ V1.0是否需要实现审批流？（如果不需要，可以V2.0再实现）

---

## 六、数据质量与监控表（部分省略）

由于篇幅限制，以下表的详细字段说明省略，仅列出表名和业务用途：

- **DataValidationLog**：数据质量检查日志表
- **ReschedulingJob**：重排程任务表
- **AlertNotification**：预警通知表
- **PurchaseRequisition**：采购申请表

---

## 七、0号位确认清单

请0号位（业务负责人）重点确认以下事项：

### ✅ 核心业务字段确认

| 序号 | 确认项 | 状态 | 备注 |
|------|-------|------|------|
| 1 | 产品族（ProductFamily）的Code命名规则是否符合企业标准？ | ⬜ 待确认 |  |
| 2 | 订单类型（OrderType）枚举值是否完整？（SO、MTS、SS、SS_U） | ⬜ 待确认 |  |
| 3 | 物料类型（MaterialType）枚举值是否完整？ | ⬜ 待确认 |  |
| 4 | 资源类型（ResourceType）枚举值是否覆盖所有设备类型？ | ⬜ 待确认 |  |
| 5 | 工艺类型（ProcessType）枚举值是否覆盖所有工艺？ | ⬜ 待确认 |  |
| 6 | 任务状态（Task.Status）枚举值是否完整？ | ⬜ 待确认 |  |
| 7 | 冻结区/锁定区天数配置是否符合实际业务需求？ | ⬜ 待确认 |  |
| 8 | 拆批规则（MOQ、EOQ）的数值范围是否合理？ | ⬜ 待确认 |  |

### ✅ 遗漏字段确认

| 序号 | 确认项 | 状态 | 备注 |
|------|-------|------|------|
| 1 | 是否需要增加"客户编号"、"客户名称"字段到Order表？ | ⬜ 待确认 |  |
| 2 | 是否需要增加"物料属性"（颜色、规格）字段到Material表？ | ⬜ 待确认 |  |
| 3 | 是否需要增加"替代料"字段到Material表？ | ⬜ 待确认 |  |
| 4 | 是否需要增加"换型矩阵"（从A物料换到B物料的时间）？ | ⬜ 待确认 |  |
| 5 | 是否需要支持"批次管理"（同一物料的不同批次）？ | ⬜ 待确认 |  |
| 6 | 是否需要增加"设备负责人"、"维护周期"字段到Resource表？ | ⬜ 待确认 |  |

### ✅ V1.0范围确认

| 序号 | 功能 | V1.0是否实现 | 备注 |
|------|------|-------------|------|
| 1 | 审批流（ApprovalFlow） | ⬜ V1.0实现 / ⬜ V2.0实现 |  |
| 2 | 次资源约束（ResourceGroup） | ⬜ V1.0实现 / ⬜ V2.0实现 |  |
| 3 | 批次管理 | ⬜ V1.0实现 / ⬜ V2.0实现 |  |
| 4 | 替代料 | ⬜ V1.0实现 / ⬜ V2.0实现 |  |
| 5 | 并行工序 | ⬜ V1.0实现 / ⬜ V2.0实现 |  |

---

## 八、附录：枚举值汇总

### 订单类型（OrderType）
- `SO`：销售订单（Sales Order）
- `MTS`：备货单（Make-to-Stock）
- `SS`：安全库存单（Safety Stock）
- `SS_U`：紧急安全库存单（Urgent Safety Stock）

### 物料类型（MaterialType）
- `FINISHED_GOOD`：成品
- `SEMI_FINISHED`：半成品
- `RAW_MATERIAL`：原材料

### 资源类型（ResourceType）
- `MACHINE`：机器设备
- `LINE`：生产线
- `WORKSTATION`：工作站
- `LOGISTICS`：物流设备

### 工艺类型（ProcessType）
- `ASSEMBLY`：装配
- `MACHINING`：机加工
- `INSPECTION`：检验
- `PROCUREMENT`：采购

### 任务状态（Task.Status）
- `PLANNED`：已排程
- `RELEASED`：已下发
- `IN_PROGRESS`：进行中
- `COMPLETED`：已完成
- `CANCELLED`：已取消
- `SUSPENDED`：已暂停

### 设备状态（Resource.Status）
- `AVAILABLE`：可用
- `DOWN`：故障
- `MAINTENANCE`：维护中
- `OFFLINE`：离线

---

**文档结束**

**确认签字**：

- **0号位（业务负责人）**：________________  日期：________
- **2号位（技术负责人）**：________________  日期：________

**确认结果**：
- ⬜ 通过，可以进入开发阶段
- ⬜ 需要修改，修改内容见附件
