# APS V1 · 白天候选（插单）· 各号位配合事项 v1.0

> 日期：2026-09-24 ｜ 2号位发起
> 收件：1号位、3号位、5号位 ｜ 抄：PM（0号位）
> 本文自带全部上下文，不需翻旧件。

---

## 一、业务全景

### 什么是白天候选

白天 Candidate = 以当前已发布的生产计划（Base ACTIVE）为基线，人为选若干订单、做一次局部重排，输出「Base vs Candidate」差异供业务判断是否激活。PM 定 10 场景（CTP 只评估不激活、插单经确认激活）。

### PM 已裁决的关键口径

以下全来自 PM 0923《Candidate业务范围·统一裁决回复》8 节，各方已认收：

| PM 裁决 | 原文口诀 |
|---|---|
| **新订单走统一载体** | 不新增 `NEW_ORDER` 字段，新订单统一走 `OrderTargets[].OrderCanonicalId` |
| **ChangeSeed = 变化起点** | 2号位 供变化需求集合 → 1号位 算受影响 Task+资源+可移动范围 |
| **变化对象 = Seed，非事实过滤器** | 影响范围由 1号位 从变化 Seed 动态传播推导，不按产品族/固定天数/订单数硬截断 |
| **Business Scope** | 业务操作权限边界：订单类操作→产品族 PMC 范围、Task 局部→计划员范围。分工：**3号位 生成、2号位 透传不判权限、1号位 遵守 + 范围不足拒绝** |
| **白天 BOM** | 白天候选新订单 BOM 按需获取：**5号位 供接口、2号位 接线加载** |
| **AllocationSequence** | V1 不依赖，ChangeSeedKeys 已用 `DemandKey`（`ORDER_{OrderId}_{MaterialCode}_{FactoryId}`，版本无关稳定） |
| **EXPEDITE** | 作用对象 = Demand（非 Task）；链路 = 优先级调整 → Demand 重排 → 重 Pegging → 重 Task → 排程；不突破硬约束；不建损失模型（只 NORMAL/EXPEDITE 二值）；只输出影响范围 |

---

## 二、2号位 已完成（各号位可直接消费/接线）

### 2.1 ScopeJson v2（3号位 Schema → 2号位 消费）

`ScopeJsonV2` 已强类型反序列化并校验。当前结构（3号位 已交付、2号位 已接线）：

| 字段 | 说明 |
|---|---|
| `Trigger` | 十码枚举（`LOCAL_INSERT`/`LOCAL_RESCHEDULE`/`LOCAL_ADJUST` 等） |
| `PriorityMode?` | 独立轴（`NORMAL`/`EXPEDITE`），有轴约束（GANTT/EQUIPMENT/RESOURCE_CALENDAR/NEW_ORDER_CTP 禁 EXPEDITE） |
| `OrderTargets[]` | `{ OrderCanonicalId, ManualTargetDueDate? }` —— 目标订单集合，去重唯一 |
| `TaskTargets[]` | `{ TaskId, TargetTime }` —— 目标 Task 集合 |
| `ChangedResourceIds[]` | `int[]` —— 故障/调整资源集合 |

### 2.2 Candidate 上下文（2号位 装配 → 1号位 消费）

2号位 在 Pegging 阶段组装以下对象，经 `DomainSolveRequest.RunScope` 传入 1号位：

| 字段 | 语义 | 当前值 |
|---|---|---|
| `ChangeSeedKeys` | 变化需求键集合（相对 Base ACTIVE 的新增/减少/数量变化 DemandKey） | 已填（`ComputeChangeSeedKeysAsync`，从 AllocationTaskShare 反查 Base） |
| `DueDateOverrides` | 需求 → 软目标交期（`LogicalDemandKey` → `DateTime`） | 已填（订单级 ManualTargetDueDate 投影） |
| `TaskTargetOverrides` | Task → 软目标时间（`(DraftId, OperationCode)` → `TargetTime`） | 已填 |
| `ChangedResourceIds` | 故障/调整资源集合（`int[]`） | 已填 |
| `InScopeLogicalDemandKeys` | 范围内可移动需求集合 | **留空（设计如此）**：冻结文档 v1.7 §67/§69 明确「变化对象=Seed、非事实过滤器；影响范围由 1号位 从 Seed 动态推导」，2号位 不预填 |
| `Trigger` / `PriorityMode` | 触发码 + 优先级模式（Core 枚举） | 已填 |

### 2.3 其余已落地项

| 项 | 说明 |
|---|---|
| M3 EXPEDITE 入链 | `DemandPriorityExecutor` 前置竞争层已接 PriorityMode + OrderCanonicalId，EXPEDITE 订单硬前缀到排序最前 |
| M4 一致性校验 | `ScopeJsonV2` 的 `OrderTargets[]` 与本次装载订单集 fail-closed 校验（任一目标订单不在→拒绝），单测 13/13 绿 |
| Candidate 骨架 | `RunSchedulingAndFinalizeAsync`（反查 Run+BasePlanVersionId → `ExecuteDomainAsync` 带 Scope）完整可跑 |

---

## 三、各号位待办

### 对 1号位

**前置上下文**：PM 已定「ChangeSeed=变化起点，由 1号位 从 Seed 动态推导可移动范围」。2号位 已把 `ChangeSeedKeys`/`TaskTargetOverrides`/`ChangedResourceIds` 填入 `RunScope`（见 §2.2）。

| # | 你要做的 | 为什么 | 2号位 侧配合 |
|---|---|---|---|
| **A** | 消费 `ChangedResourceIds`（故障/调整资源集合） | 当前零消费（M5 二阶段需） | 字段已有值，直接读 |
| **B** | 从 `ChangeSeedKeys` 推导可移动集合 + Scope外 Anchor/Block 补集 + 「范围不足」拒收（`SCOPE_REFERENCE_MISSING`） | v1.7 §67：Seed 非过滤器，影响范围由你动态推导 | `InScopeLogicalDemandKeys` 留空（设计如此），ChangeSeed 已填 |
| **C** | 出「范围键」消费契约：哪些字段进 `DomainSolveRequest`、在哪层消费、与现有 `LockedTaskConstraint` 如何不冲突 | M3/M5 适配需要此契约 | 契约一出 2号位 即适配 |

### 对 3号位

| # | 你要做的 | 为什么 | 2号位 侧配合 |
|---|---|---|---|
| **D** | 出 `BusinessScopeDto`（DTO + 字段结构） | PM 裁决：3号位 生成 → 2号位 透传 → 1号位 遵守。当前 `IDataScopeService` 你已供、2号位 已注入（3 处），只差 `RunScope.BusinessScope` 的 DTO 字段定型 | DTO 一到，2号位 `BuildRunScopeAsync` 一行透传 |
| **E** | `api/candidate` 端点 | PM 0923 已裁：Candidate 统一入口 | 端点一到 2号位 接线 |

### 对 5号位

| # | 你要做的 | 为什么 | 2号位 侧配合 |
|---|---|---|---|
| **F** | 供白天 BOM 按需展开接口 | PM 裁决：白天候选新订单 BOM 按需获取（非夜间 FULL 的 `PullBOMResultFromODSAsync` 全量拉） | 接口一到 2号位 接线 |

---

## 四、PM 待裁决（各号位同步知情，等回后 2号位 统一落码）

以下三条为 2号位 2026-09-23 发 PM 的方案 §四（Q1/Q2/Q3），当前待回。各号位暂不需动作，只同步知情。

| Q | 问题 | 2号位 推荐 | 影响 |
|---|---|---|---|
| Q1 | Firm 识别边界：X（计划日期≤7天为 Firm）vs Y（已排上 Base ACTIVE 即 Firm，不限天数） | **Y** | 订单保护识别逻辑在 Q1 定案后才能落 |
| Q2 | `FirmHorizonDays`/`FrozenHorizonDays` 窗口参数归属与装载通道 | 随策略包（RuleSet）由 **3号位 维护默认值**，2号位 装载透传（复用既有策略通道） | 参数目前零赋值零消费 |
| Q3 | Frozen 保护：2号位 供「执行事实位置锚点」是否被 PM §八「不供禁移 Task 列表」涵盖 | 分两层：执行层（已下发 MES=锚定位置）vs 窗口层（仅 FrozenHorizonDays=排出后不移动） | 若 Q3 否决执行层锚点，已下发 Task 在 1号位 不持有 Base 旧计划时无保护机制 |

---

## 五、阻塞解除后 2号位 工作量

全部是 1~数行改码：

- Q1 回 → `OrderProtection` 识别 + 投影（2号位 落）
- 3号位 DTO 到 → `RunScope.BusinessScope` 透传（一行）
- 5号位 接口到 → BOM 白天接线
- 1号位 契约到 → M3 范围键适配

---

**发送人**：2号位 ｜ **日期**：2026-09-24