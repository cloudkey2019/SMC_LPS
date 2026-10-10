# 3号位 → 2号位 — 提请落码：Batch Policy 第⑧块 → SolverStrategySnapshot 投影（§2.3 打通）

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 发件 | 3号位（规则参数治理 owner） |
| 收件 | 2号位（核心运行/编排 owner） |
| 抄送 | 1号位（消费端知会） |
| 主题 | **§2.3 打通**：第⑧块 `BatchPolicy` 已到 `FrozenStrategySnapshot`，但未投影进 `SolverStrategySnapshot` → 1号位 消费端收不到，请 2号位 落码接线 |
| 优先级 | 中（⑧ 已发布可落库、快照已装配，仅差编排层接线；1号位 侧 Batch Decision 依赖此块） |

---

## 一、现状（三事实，代码为准）

| # | 环节 | 现状 | 证据 |
|---|---|---|---|
| 1 | 治理落库 | `TaskSplitRuleConfig` 表 CRUD 已可用；发布时 `GovernanceVersionService.InjectBatchPolicyAsync` 把 active+生效区间规则投影为 `BatchPolicyRuleSnapshot` 写入 `ContentSnapshotJson.BatchPolicy` 子块 | `GovernanceVersionService.cs:1016-1076` |
| 2 | 快照装配 | `FrozenStrategySnapshotProvider.GetFrozenStrategySnapshotAsync` 已把 `ContentSnapshotJson.BatchPolicy` 反序列化为 `FrozenStrategySnapshot.BatchPolicy`（⑧ 块，fail-open） | `FrozenStrategySnapshotProvider.cs:121-123`、`FrozenStrategySnapshot.cs:25` |
| 3 | **编排投影（缺口）** | `PeggingOrchestrator.cs:233-254` 构建 `SolverStrategySnapshot` 时仅投影 `SolverStrategy`/`CandidateGuardrail`/`SetupTransitionRules`(⑦)/`Parameters`，**未投影 `BatchPolicies`(⑧)** → 1号位 经 `DomainSolveRequest.StrategySnapshot` 收到恒为 `Array.Empty` | `PeggingOrchestrator.cs:233-254`、`DomainSolveRequest.cs:194` |

> `SolverStrategySnapshot.BatchPolicies` 字段**已存在且已冻结**（`DomainSolveRequest.cs:189-195`，05 契约 §2.3 第 7 类；注释已写明「2号位 按本 Run 从 TaskSplitRuleConfig 装载并投影」）——**无契约变更、无接口签名变更**，纯接线落码。

## 二、提请落码点

`PeggingOrchestrator.cs:233-254` `SolverStrategySnapshot` 对象初始化处，补：

```csharp
BatchPolicies = frozenSnapshot.BatchPolicy,   // ⑧ 批量策略（Material+ProductionDepartment 粒度；空=每需求恒 1 批）
```

与 `SetupTransitionRules = SetupTransitionRuleProjector.CropToDomain(...)` 并列。

## 三、需 2号位 与 1号位 对齐的一点（裁剪语义）

- ⑦ Setup 走 `CropToDomain`（按本 Domain 的 ProductionDepartmentId + StageCode 裁剪）。
- ⑧ `BatchPolicy` 粒度 = `Material + ProductionDepartmentId`（可空 = Material 级默认），05 契约 §6.10.5 **未规定裁剪**。请 2号位 与 1号位 确认二选一：
  - **A（推荐）**：全量透传 `BatchPolicies`，1号位 `PhaseOneConstraintBuilder` 按本域 Material/Dept 自行匹配（与 ① 兜底链 ①②③④ 同向：装载不裁剪，匹配期决定）；
  - **B**：按本 Domain 裁剪（同 ⑦ 语义），需 1号位 确认消费端不再自行过滤。
- 请 2号位 在落码回执中明确所取方案，便于 3号位 回填 05 契约 §6.10.5。

## 四、红线与范围

- 红线 #1（Scheduling 零 DB）：本提请不改 1号位 侧代码，1号位 仅消费字段。
- 红线 #5（接口即契约）：`SolverStrategySnapshot` 字段已冻结，**不改签名/不增删字段**。
- 红线 #6（DDL）：零 DDL——表/子块/快照字段均已落地，纯编排接线。
- 范围：`PeggingOrchestrator.cs` 单点投影（预估 ≤5 行）+ 1号位 侧消费确认。

## 五、验证建议

1. **1号位 侧**：单测/联调确认 `ConstraintContext.ExecutionBatchPolicies` 收到本域规则（发布含 BatchPolicy 的版本后跑一次 FULL Run）。
2. **2号位 侧**：断点/日志确认 `SolverStrategySnapshot.BatchPolicies.Count` > 0（dev 库 TaskSplitRuleConfig 已可造数）。
3. 3号位 侧：FrozenStrategySnapshot 装配已有单测（24/24 绿），不需随动。

---

> 按「涉及 1号位 契约语义由 3号位 知会」方式，本提请发 2号位（落码义务）+ 抄送 1号位（消费端确认裁剪语义）。3号位 收到回执后回填 05 契约 v1.6。
