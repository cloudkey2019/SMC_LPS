# 待 2号位 协助任务清单（最新）—— 3号位 致 2号位

> 收件：2号位（数据引擎+编排）／抄 0号位（PM）、1号位
> 依据：3号位《Setup换型治理进展与依赖移交回执 v1.0 2026-09-19》、1号位《口径确认与骨架进度回执 v1.0 2026-09-18》、《预算参数提值回执 v1.0 2026-09-20》、2号位《待办清单处置回执答复 v1.1 2026-09-18》
> 日期：2026-09-20
> 性质：汇总当前所有归 2号位 的协助/阻塞项，按「主链阻塞 → 取证 → 契约验收 → 边界移交 → 测试基建」排序
> 术语约定：英文契约词一律「中文名称（英文）」；文档述引「§符号锚 + 值」，代码引 `文件:行`。

---

## 〇、一句话

以下 7 项均为 2号位 辖区待办，3号位 已把 3号位 侧能做的（契约冻结 / DTO 骨架 / 静态判定）全部做完，卡点全在 2号位 侧落子。

---

## A. 主链运行时硬阻塞（Setup 换型 P1-02）

### A1. `ISetupTransitionRuleRepository` Dapper 实现（头号阻塞）

- **现状**：3号位 治理侧已闭环——`SetupTransitionRuleService` + `SetupTransitionRuleController`（五端点 + RuleView/RuleMaintain 权限码）+ 10 单测全绿，`dotnet build` 0 错误。但服务注入 `ISetupTransitionRuleRepository` 在运行时**无实现可 resolve**，端点启动即失败。
- **契约已冻结**：`LPS.APS.Core\Interfaces\ISetupTransitionRuleRepository.cs`（5 方法：`GetByRuleSetVersionAsync` / `GetByIdAsync` / `AddAsync` / `UpdateAsync` / `DeleteAsync`）。
- **表已落地**：`SetupTransitionRule` 表（实体 + DDL）2026-09-17 经批准由 3号位 直接建（DB 红线 #6 豁免），**2号位 只需照接口写 Dapper 仓储，无需建表**。
- **动作**：对 `SetupTransitionRule` 表落 Dapper 实现，注册进 DI（Engine 层 Scrutor 自动扫描 `LPS.APS.Engine.Repositories.APS` 命名空间）。

### A2. 第⑦块 `SetupTransitionRules` 按 Domain 裁剪投影进 `DomainSolveRequest`

- **现状**：1号位 接线头号依赖；`SetupTransitionRuleSnapshot` 形态已定（List 容器 / `int?` MaterialId / EXACT+DEFAULT 按 RuleType 区分 / Dept+Stage 进 DTO），只差通道。
- **依据**：1号位《口径确认与骨架进度回执》§五。
- **动作**：按 `FrozenStrategySnapshot.SetupTransitionRules`（⑦块，随 `RuleSetVersionId` 锚冻结）裁剪投影，按 Domain（ProductionDepartmentId + StageCode）建内存索引，传给 1号位 `DomainSolveRequest`。

---

## B. 数据缺口取证（定责三分法，2号位 供证据 / 3号位 判责）

### B1. 「视图丢 vs 源缺」判定取证

- **现状**：3号位 静态判定已出——仓库内**无** 3号位 ODS 清洗对象（`MES_APS_Routing_Dependency_View` / `MES_APS_Routing_Operation_View` / 中间表 `MiddleRoutingData` / `MiddleRoutingDataJ`）的真实 DDL，`*.sql` 里只有 2号位 的 `ext_` 同义词包装。决定性证据在 MES_Integration 库。
- **动作（2号位 供两样）**：
  1. `MES_APS_Routing_Dependency_View` / `MES_APS_Routing_Operation_View` 及 `MiddleRoutingData` / `MiddleRoutingDataJ` 的真实 DDL（或授 3号位 读库自取）；
  2. 配合代跑取证 SQL（2号位 已备）：`SELECT * FROM dbo.MiddleRoutingData WHERE MES_ID=N'3231940'` 及 `MiddleRoutingDataJ` 同条。
- **落责**：源生表有「下工步」行而中间表无 → 视图丢（3号位 修）；源本来就缺 → 源缺（3号位 开清单给 PM，台账 K「阻塞 B」记账）。

---

## C. 契约验收

### C1. ScopeJsonV2 十码验收

- **现状**：3号位 已于 2026-09-18 v2.0 附 DTO 骨架 + `BusinessTriggerType` 十码枚举（§41.2「10 场景矩阵」，`CTP_NORMAL` ~ `DOMAIN_MANUAL_RESCHEDULE`），送验中。
- **动作**：2号位 验收十码；验码后 3号位 即落 `ScopeJsonV2` 实体 + `RunLifecycleService` 解析/校验（M1 消费侧开工）。

### C2. ⑨ ExplainTrace 载体契约

- **现状**：`DomainSolveResult` 现只有 `ExplanationFacts`，无 ExplainTrace 载体；Q4 裁决「初始设备状态 Setup=0、不新增 ReasonCode、走 ExplainTrace/等价轻量追踪（符号锚 `INITIAL_SETUP_STATE`）」。
- **动作**：2号位 定 `DomainSolveResult` 中 ExplainTrace 载体契约（或 1号位 先用 `EvidenceJson` 降级）；定稿后三方对齐 explanationType 符号。

---

## D. 边界移交（Core 层 2号位 辖区）

### D1. `SchedulingContext.SetupAttribute` 删除/废弃

- **现状**：1号位 回执 §四已确认——该字段位于 `LPS.APS.Core\Models\Scheduling\SchedulingContext.cs`（Core 层，2号位 辖区），1号位 重写 `SetupOptimizer` 后已零引用、无权删除。
- **动作**：2号位 按 §21「物理字段兼容废弃」口径处理（标记废弃或删除）。

---

## E. 测试基建（附带提醒）

### E1. `RealSchedulingIntegrationTest`（Task 空 / Pegging 未落库）

- **现状**：报「Task 表没有数据！PeggingOrchestrator 可能未正确 INSERT」；3号位 已提报 2号位（D 类），r13371 改过 `PeggingOrchestrator`。
- **动作**：2号位 确认 `PeggingOrchestrator` INSERT 是否被 r13371 影响。

---

## 汇总表

| # | 项 | 类别 | 关键依赖 | 状态 |
|---|---|---|---|---|
| A1 | `ISetupTransitionRuleRepository` Dapper 实现 | 主链硬阻塞 | 契约已冻结、表已落地 | ⏳ 头号阻塞 |
| A2 | 第⑦块 `SetupTransitionRules` 投影进 `DomainSolveRequest` | 主链阻塞 | 形态已定、1号位 等接线 | ⏳ |
| B1 | 「视图丢 vs 源缺」取证（供 DDL + 代跑 SQL） | 取证 | 3号位 已出静态判定 | ⏳ |
| C1 | ScopeJsonV2 十码验收 | 契约验收 | 3号位 已附骨架+十码 | ⏳ 送验 |
| C2 | ⑨ ExplainTrace 载体契约 | 契约载体 | Q4 已裁、符号锚已定 | ⏳ |
| D1 | `SchedulingContext.SetupAttribute` 删除/废弃 | 边界移交 | 1号位 已零引用 | ⏳ |
| E1 | `RealSchedulingIntegrationTest` Pegging 未落库 | 测试基建 | 3号位 已提报 D 类 | ⏳ |

> 3号位 不越位改 2号位 代码；上表契约/骨架均已由 3号位 备好，2号位 侧落子即解锁下游 1号位 接线与 #8 全量回归。