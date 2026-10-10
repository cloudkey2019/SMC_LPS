# APS V1 无设备工序 —— 3号位 回执（致 1号位）

**版本**：v1.0
**日期**：2026-09-21
**回执对象**：《APS_V1_无设备工序_1号位致3号位_确认单_v1.0_20260921.md》
**回执角色**：3号位（规则/参数治理）
**依据**：0号位 终版裁决（《未命名的Markdown文件 (51)(1).md》）§二 R4'/R6 / §八 / §九；《APS_V1_无设备小工序与人工有限产能模型_DDL变更申请_v5.1.9候选_v1.0_20260921.md》（S1，已提交 0号位）

---

## 一、总述

三参数治理口径与 0号位 裁决、与 1号位 确认单**完全一致**，3号位 已按 S1 设计好载体（三表 DDL 申请已提交 0号位，待批准落库）。本回执先交付 **字段/示例/默认值/兜底行为**，供 1号位 前置开消费代码；实际数据通道（DomainSolveRequest 扩展）落地时序见 §六。

---

## 二、Q1｜OperationPlanningParam 载体字段/类型/示例

表 `OperationPlanningParam`（APS 工艺规划属性，**无 RuleSetVersionId 冻结**，非规则引擎）：

| 字段 | 类型 | 说明 |
|---|---|---|
| `Id` | BIGINT IDENTITY PK | 主键 |
| `OperationCode` | NVARCHAR(50) NOT NULL | 工序标识（键） |
| `PlanningMode` | NVARCHAR(30) NOT NULL | 三分类：`FINITE_RESOURCE` / `UNCONSTRAINED` / `WAIT_ONLY`（CHECK 约束） |
| `EffectiveVersion` | NVARCHAR(30) NOT NULL | 生效版本（如 `V1.0`） |
| `IsActive` | BIT NOT NULL DEFAULT 1 | 启用标记 |
| `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy` | 审计四列 | 同 SetupTransitionRule 先例 |

**示例**（0号位 裁决 §二 原文示例）：

| OperationCode | PlanningMode | EffectiveVersion |
|---|---|---|
| 精修 | FINITE_RESOURCE | V1.0 |
| 清洗 | UNCONSTRAINED | V1.0 |
| 冷却 | WAIT_ONLY | V1.0 |

**版本选取**：同 OperationCode 多版本取 **IsActive=1 最新版本**（S4 契约文档定案并备案）。

---

## 三、Q2｜Transition / Transfer 规则参数定义 + 默认值

两表均随 **RuleSetVersionId 冻结**（参照 SetupTransitionRule 通道）。

### OperationTransitionRule（工序转运规则）

| 字段 | 类型 | 说明 |
|---|---|---|
| `Id` | BIGINT IDENTITY PK | 主键 |
| `RuleSetVersionId` | BIGINT NOT NULL FK→RuleSetVersion | 冻结版本 |
| `ProductionDepartmentId` | INT NULL | 部门范围（NULL=所有） |
| `StageCode` | NVARCHAR(50) NULL | 大工艺范围（NULL=跨 Stage 兜底） |
| `FromOperationCode` | NVARCHAR(50) NULL | 起始工序（NULL=范围内任意） |
| `ToOperationCode` | NVARCHAR(50) NULL | 目标工序（NULL=范围内任意） |
| `TransferMinutes` | DECIMAL(18,4) NOT NULL | 转运时长（分钟），CHECK ≥ 0 |
| `IsActive` + 审计四列 | — | 同先例 |

**默认值（seed 样例，业务可改）**：裁决 §八 示例「机加工内部默认转运 30 分钟」→ seed 提供 `TransferMinutes = 30`；**未命中任何规则 → 兜底 0（不叠加）**。

### StageTransferTime（跨大工艺转运时长）

| 字段 | 类型 | 说明 |
|---|---|---|
| `Id` | BIGINT IDENTITY PK | 主键 |
| `RuleSetVersionId` | BIGINT NOT NULL FK→RuleSetVersion | 冻结版本 |
| `FromStageCode` | NVARCHAR(50) NOT NULL | 起始大工艺 |
| `ToStageCode` | NVARCHAR(50) NOT NULL | 目标大工艺（单向，逆向另配） |
| `TransferMinutes` | DECIMAL(18,4) NOT NULL | 转运时长（分钟），CHECK ≥ 0 |
| `IsActive` + 审计四列 | — | 同先例 |

**默认值（seed 样例，业务可改）**：裁决 §八 示例「机加工→表面处理 4 小时」→ seed 提供 `TransferMinutes = 240`；**未命中 → 兜底 0（不叠加）**。

---

## 四、Q3｜规则缺失时的兜底行为（关键口径）

**结论：规则优先；规则缺失 → 默认 0（不叠加）；当前 MES `LagTime=0` 不作为有效事实、不回落。**

消费链（对齐 0号位 终裁 R4' 链路）：

```
1. 命中 OperationTransitionRule / StageTransferTime 具体规则  → 用规则值（TransferMinutes）
2. 未命中规则                                              → 0（不叠加等待/转运）
3. （未来）MES 提供真实 LagTime 事实                          → 届时纳入优先级体系（规则之上）
```

**不回落 MES Routing LagTime**：当前 `MES_APS_Routing_View` 硬编码 `LagTime=0`，按 0号位 终裁 **非有效事实**，禁止其覆盖 3号位 规则（对齐 ManualETA>ERPETA>DefaultLT 之外的另一面——无效事实不参与）。

**消费锚点确认**：叠加接入点采纳 1号位 `PhaseTwoInitialScheduler.cs:867`（`edge.LagTime` 工序间隙）+ 跨 Stage 间隙，改动限 Scheduling 层，Core DTO 不动——3号位 无异议。

---

## 五、消费优先级总述（给 1号位 的清晰链）

| 参数 | 命中 | 未命中（兜底） |
|---|---|---|
| OperationPlanningMode | 三态消费（FINITE 找槽 / UNCONSTRAINED 占时不占资源 / WAIT_ONLY 只加等待） | **无资源工序未配置 → 判异常（fail-closed）**；有资源未配置 → 按 FINITE_RESOURCE（0号位 R7 A方案） |
| OperationTransitionRule | 用规则 TransferMinutes | 0（不叠加） |
| StageTransferTime | 用规则 TransferMinutes | 0（不叠加） |

---

## 六、落地时序（依赖 DDL 批准）

| 步骤 | 动作 | 归属 |
|---|---|---|
| 1 | 0号位 批准 S1 申请单（3 表 + 落库执行归属 ① / FK 口径 ② / 版本规则 ③） | 0号位 |
| 2 | v5.1.8→v5.1.9 升级补丁落库 dev | 2号位（或按授权 3号位） |
| 3 | S2 治理模型（三表 Entity+Repo+Service+Validator+审计+权限码）+ seed（30/240 默认值） | 3号位 |
| 4 | S4 契约草案：三参数独立进 `DomainSolveRequest`（仿 SetupTransitionRules，不进 Frozen 六块）+ 架构原则登记 | 3号位（改 DTO 前先更新契约文档，红线 #5） |
| 5 | **通道数据到位 → 1号位 当天开消费代码**（Phase2 分型 + 测试） | 1号位 |

---

## 七、回执表

| 1号位 问 | 3号位 答 |
|---|---|
| ① OperationPlanningParam 字段/类型/示例 | §二（字段表 + 精修/清洗/冷却示例 + 版本规则） |
| ② Transition / Transfer 参数 + 默认值 | §三（两表字段 + seed 默认 30/240；未命中兜底 0） |
| ③ 规则缺失回落 LagTime 还是规则优先 | §四（**规则优先；缺失→0；不回落 MES LagTime=0**） |

---

*本回执基于 2026-09-21 工作树现状。DDL 批准 + 落库后 3号位 按 §六 步进。*
