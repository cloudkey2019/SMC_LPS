# APS V1 无设备小工序 —— OperationPlanningMode 消费契约草案（S4）

**版本**：v1.0（草案，待 1号位 / 2号位 回执）
**日期**：2026-09-22
**发送人**：3号位（规则/参数治理、工艺属性治理 Owner）
**接收人**：1号位（消费）、2号位（投影/落库）
**抄送**：0号位（PM，备查）
**性质**：**消费契约草案**——红线 #5 前置：接口/契约变更先出文档；本单固化 OperationPlanningMode（OPM）三态从「数据承载 → 接口 → 代码」的完整消费口径
**依据**：
- 0号位《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》**五.1 / 五.2 / 七**（最终口径）
- S1《DDL变更申请_v5.1.9终版_v2.0_20260922.md》（RoutingOperation 加列，**不新增任何表**）
- 《APS_V1_无设备工序_1号位致3号位_确认单_v1.0_20260921.md》（1号位 消费口径）
- 《未命名的Markdown文件 (52)(2).md》（0号位 BOM→Stage→Routing 裁决，2026-09-21）
- 《APS_V1_1号位答疑_Task粒度与Operation_MES执行边界说明.md》（1号位 Task 粒度 / OPM / MES 边界口径）
- 冻结 DDL《APS_数据库表结构设计_DDL_v5.1.8.2_…冻结版.sql》

---

## 〇、速览（5 分钟读完）

```
数据承载：RoutingOperation.OperationPlanningMode 列（冻结 DDL v5.1.8.2 已定义）
          → Core.RoutingOperation 实体镜像属性（默认 FINITE_RESOURCE）
          → 2号位 Dapper 投影进 DomainSolveRequest.RoutingOperations（整块已携带，结构零改动）
消费行为：FINITE_RESOURCE  正常资源找槽（现状）；无合格资源 → 业务 Unscheduled
          UNCONSTRAINED    跳过资源，叠加 StandardDuration×数量 到工艺链最早位置，不判失败
          WAIT_ONLY        跳过资源，生成占位 Task（时间节点），等待默认 0
          （OPM 不决定是否生成 Task——排程时间节点模型；MES 资格不由 OPM/ResourceId 单独决定）
fail-closed：无设备工序必须显式配 UNCONSTRAINED / WAIT_ONLY；FINITE 不得因无资源自动无限产能
等待/转运：本轮不新增表；消费锚点 PhaseTwoInitialScheduler.cs:867 edge.LagTime 默认 0（不回落 MES）
制造路径：Routing 查询必须带 Stage（MaterialId + StageCode）——(52)(2) 裁决登记
```

---

## 一、数据承载契约（RoutingOperation 实体扩展）

### 1.1 物理列（冻结 DDL v5.1.8.2，S1 终版申请落库）

`RoutingOperation` 增加 `OperationPlanningMode` 列：

```
NVARCHAR(32) NOT NULL DEFAULT ('FINITE_RESOURCE')
CHECK (OperationPlanningMode IN ('FINITE_RESOURCE', 'UNCONSTRAINED', 'WAIT_ONLY'))
```

- 默认 `FINITE_RESOURCE`：既有 RoutingOperation 行自动落默认值，**无数据迁移**；
- 归属 `RoutingOperation`（工艺数据），**不新增独立参数表**（0号位 五.1）。

### 1.2 代码侧镜像（Core 实体）

`LPS.APS.Core/Entities/Aps/RoutingOperation.cs` 新增属性（随 S1 落库后实施）：

```csharp
/// <summary>
/// 排程规划模式（APS 工艺规划属性，0号位 09-22 统一回复意见 五.1）。
/// FINITE_RESOURCE（默认，需资源有限产能）/ UNCONSTRAINED（无约束，无设备工序）/ WAIT_ONLY（仅等待占位）。
/// 对齐冻结 DDL v5.1.8.2 RoutingOperation.OperationPlanningMode 列。
/// </summary>
public string OperationPlanningMode { get; set; } = "FINITE_RESOURCE";
```

> 实施归属：实体为 Core 契约（3号位 契约登记）；Dapper 投影与实体落地由 2号位 随 S1 补丁执行（红线 #6，落库归属待 0号位 确认①）。

### 1.3 投影通道（2号位）

- `DomainSolveRequest.RoutingOperations`（`IReadOnlyList<RoutingOperation>`）**整块已携带**——2号位 仅需在 Dapper 投影中读取新列，**契约结构零改动**；
- 数据来源：ODS 契约视图 `ext_MES_APS_Routing_Operation_View` 补充该列（工艺属性随工序主数据走，与 1号位确认单 §二「独立进 DomainSolveRequest、不进 Frozen 六块」一致）。

---

## 二、消费行为契约（1号位）

### 2.1 三态消费映射

| 值 | 语义 | 1号位 消费行为 | 产能资源占用 |
|---|---|---|---|
| `FINITE_RESOURCE` | 需资源（人工有限产能）| 现有资源找槽：OperationResourceEligibility 合格资源 + ResourceCalendar 产能窗口（现状逻辑）| 占产能资源 |
| `UNCONSTRAINED` | 无约束（无设备工序）| 跳过资源找槽，叠加 `StandardDuration × PlannedProcessQty` 到工艺链最早位置；**不判资源失败** | 不占产能资源（占时间轴）|
| `WAIT_ONLY` | 仅等待/转运占位 | 跳过资源，生成**占位 Task（时间节点）**，叠加等待时间（本轮**默认 0**）；影响后续 Operation 最早开始 | 不占产能资源（占时间轴）|

> **Task 生成语义澄清**（1号位答疑）：OperationPlanningMode **不决定"是否生成 Task"**——APS 排程链路 `Routing Operation → Resource Eligibility → 有限产能 Solver → Task`，任何 Operation 求解后均可产生 Task（APS 排程时间节点）；OPM 只指导该 Operation **如何参与时间轴计算**。拆批 / 多资源并行可令同一 Operation 产生多个 Task；非资源工序（冷却/固化/工艺等待）同样生成 Resource 为空的占位 Task（时间约束 / Dependency 计算 / 甘特展示 / 排程解释载体）。Task 与 MES 工单为**映射关系，非一对一**。

改动范围：Phase2 正排/倒排分型（`ScheduleForward` / 倒排对应路径），限 Scheduling 层（对齐 1号位确认单 §二「改动限 Phase2 分型，Scheduling 层」）。

### 2.2 fail-closed（0号位 R7 + 1号位确认单 §一）

1. **无设备工序必须显式配 `UNCONSTRAINED` / `WAIT_ONLY`**——否则视为工艺数据缺陷；
2. **`FINITE_RESOURCE` 不得因无资源自动降级为无限产能**（禁止）；无合格资源保持现状业务 `Unscheduled`（`PhaseTwoInitialScheduler.cs:880-883` `eligibleResources.Count == 0 → 返回空，业务 Unscheduled`）；
3. 工艺数据强校验（S2 治理侧加载期）：`FINITE_RESOURCE` 工序必须在 `OperationResourceEligibility` 存在合格资源，否则报工艺数据缺陷（3号位 治理侧登记，2号位 加载侧执行）。

### 2.3 MES 执行边界与职责边界（1号位 答疑 §7/§8 登记）

1. **ResourceId 为空 ≠ 不能 MES 执行**：是否创建 MES 工单与是否存在资源约束**不是同一维度**——MES 工单创建资格由**生产执行模型 / 工艺执行属性 / MES 接口契约**决定，**不由 OperationPlanningMode 或 ResourceId 单独决定**（自然冷却可能是 Routing 等待，也可能是需人工确认/状态闭环的独立生产指示）；MES 是否下发属 2号位 MES 接口域，本契约不做排程侧判定。
2. **职责边界（登记固化）**：
   - 1号位：Operation 级有限产能求解、Resource 选择、Start/End 计算、Task 生成；
   - 2号位：Task 结果落库、Task 生命周期、MES 接口和下发；
   - 3号位：Routing 工艺事实、OperationPlanningMode、Resource Eligibility 等工艺/资源 ODS 数据（本契约治理侧职责确认）。

---

## 三、等待/转运（本轮不新增承载）

### 3.1 定案口径

0号位 五.2：`OperationTransitionRule` / `StageTransferTime` **本轮不新增表**；Setup 非工序转换模型；如需补充作**独立能力单独立项**（不在本轮）。

### 3.2 消费锚点

`PhaseTwoInitialScheduler.cs:867`：

```csharp
var candidateStart = effectiveTime.AddMinutes((double)edge.LagTime);
```

- 等待/转运原接入点即此 `edge.LagTime`（1号位确认单 §二）——本轮**维持 `RoutingDependency.LagTime` 既有来源（默认 0）**，不做任何叠加；
- **兜底口径**（0号位 已裁：当前 LagTime=0 不作为有效事实、不覆盖规则）：本轮无规则表可覆盖，一律**默认 0 消费，不回落 MES LagTime**（MES LagTime=0 不引入）。

---

## 四、制造路径原则登记（(52)(2) 裁决固化）

本登记为跨层约束，2号位 投影与 1号位 消费均须遵守：

| # | 原则 | 内容 |
|---|---|---|
| 1 | **Routing 查询必须带 Stage** | `MaterialId + StageCode` 定位工序；禁止仅按 MaterialId 取 Routing |
| 2 | 一需求不拆分 | 同一物料跨多 Stage = 一个生产需求，**不拆分** |
| 3 | 上下文 Key 不调整 | `MaterialStageDeptContext` Key = `MaterialId + StageCode`（不调整）|
| 4 | Routing Context 形成 | 2号位 加载完整路径（MaterialId+StagePath）、按（MaterialId+StageCode）匹配；1号位 不推 Stage，锁 ProductionDepartmentId 后过滤 Routing 三件套 |
| 5 | 本项目各层遵守 | 2号位 投影、1号位 消费、5号位 查询侧均登记本原则 |

---

## 五、接口同步清单（0号位 七「必要接口同步」）

| # | 同步项 | 责任 | 状态 |
|---|---|---|---|
| 1 | Core `RoutingOperation` 实体新增 `OperationPlanningMode`（§1.2）| 3号位 契约登记 / 2号位 实体落地+投影 | 待 S1 落库后实施 |
| 2 | 契约文档《01_核心数据结构契约》RoutingOperation 字段表补 OperationPlanningMode | 3号位 | 随本单同步登记 |
| 3 | `DomainSolveRequest` 结构**零改动**确认（RoutingOperations 整块已携带）| 3号位 复核 / **1号位 确认** | 待 1号位 回执 |
| 4 | 1号位 Phase2 正/倒排分型消费 + 测试（限 Scheduling 层）| 1号位 | 通道数据到位当天开工（1号位确认单承诺）|
| 5 | S2/S3 治理侧 OPM 维护 API 交付 4号位 | 3号位 | 另件（不属本单）|

---

## 六、与其他补齐项边界（避免范围蔓延）

| 项 | 归属 | 处理 |
|---|---|---|
| Task.SetupSource 字段 + 枚举统一（INITIAL_SETUP_STATE/EXACT/DEFAULT/SETUP_RULE_MISSING_ZERO_FALLBACK）| Setup 链 | 单独契约件，本单**不含** |
| ScopeJson SchemaVersion=2 | 已 M1 落地 | 本单**不含** |
| ResourceCalendar 物理承载 | 2号位（0号位 三）| 本单仅 FINITE 消费引用既有 `CalendarSlots`，**不含**承载 |
| 配品 M101 | 已闭环（0号位 六）| 仅作为无设备小工序示例，**不单列** |

---

## 七、回执表

| 问 | 1号位 | 2号位 |
|---|---|---|
| §2.1 三态消费口径与 §2.2 fail-closed 是否一致 | （待回执）| — |
| §3.2 LagTime 默认 0 兜底（不回落 MES）口径是否一致 | （待回执）| — |
| §1.3 Dapper 投影 OperationPlanningMode（随 S1 落库后）是否确认 | — | （待回执）|
| §1.2 Core 实体镜像属性是否认可（实施随 S1 补丁）| — | （待回执）|

---

*批准/回执后：S4 登记进契约文档《01_核心数据结构契约》→ 随 S1 落库实施 §1.2/§1.3 → 1号位 开消费代码（Phase2 分型）→ S7 集成测试（清洗/冷却/人工产能槽并行 + 无资源 fail-closed）。*
