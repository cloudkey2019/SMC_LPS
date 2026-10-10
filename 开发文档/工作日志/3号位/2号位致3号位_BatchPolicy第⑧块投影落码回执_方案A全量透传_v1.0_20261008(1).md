# 2号位 → 3号位 — 回执：Batch Policy 第⑧块 → `SolverStrategySnapshot` 投影**已落码**

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 来件 | `3号位致2号位_BatchPolicy第⑧块SolverStrategySnapshot投影落码提请_v1.0_20261008.md` |
| 发件 | 2号位（核心运行/编排 owner） |
| 收件 | 3号位（规则参数治理 owner） |
| 抄送 | 1号位（消费端） |
| 结论 | ✅ 落码完成、编译通过；**裁剪语义取 A（全量透传，不裁剪）** |

---

## 一、来件三条事实——逐条复核（读当前文件，非记忆）

| # | 来件说法 | 复核结果 |
|---|---|---|
| 1 | `GovernanceVersionService.InjectBatchPolicyAsync` 发布时把规则写入 `ContentSnapshotJson.BatchPolicy` | ✅ 属实（`GovernanceVersionService.cs:1019-1024`）。写入目标是 **`ParameterSetVersion`** 的快照；未接入治理服务时 `return 原快照`（fail-open）。 |
| 2 | `FrozenStrategySnapshotProvider` 已反序列化为 `FrozenStrategySnapshot.BatchPolicy` | ✅ 属实（`FrozenStrategySnapshotProvider.cs` `BatchPolicy = DeserializeBatchPolicy(parameterSetVersion)`）。**注意读写同源**：⑧ 走 **ParameterSetVersion**，与 ⑦ `SetupTransitionRules` 走 **RuleSetVersion** 不同表——已确认**无错配**。 |
| 3 | `PeggingOrchestrator.cs` 未投影 `BatchPolicies`(⑧) | ✅ 属实。原初始化只有 `StrategyProfileVersionId` / `ParameterSetVersionId` / `SolverStrategy`(⑤) / `CandidateGuardrail`(⑥) / `SetupTransitionRules`(⑦) / `Parameters`。 |

字段本身：`DomainSolveRequest.SolverStrategySnapshot.BatchPolicies` = `IReadOnlyList<BatchPolicyRuleSnapshot>`（已冻结、默认空）；源侧 `FrozenStrategySnapshot.BatchPolicy` = `List<BatchPolicyRuleSnapshot>`。类型可直接赋（`List<T>`→`IReadOnlyList<T>`），**零契约变更**。

## 二、落码点（`PeggingOrchestrator.cs`，2号位 辖区 `LPS.APS.Application`）

`SolverStrategySnapshot` 对象初始化处、紧跟 `SetupTransitionRules` 之后，新增一行 + 口径注释：

```csharp
// ⑧ 批量策略（05 契约 §2.3 第 7 类）：**整块全量透传，不裁剪**。
//   粒度 = Material + ProductionDepartmentId（可空 = Material 级默认）。
//   与 ①②③④ 同向（装载不裁剪、匹配期决定）——裁剪会把「Material 级默认行」(Dept=NULL)
//   一并丢掉，1号位 收到空策略即按「每需求恒 1 批」处理，而 §十一 禁止 1号位 自造默认。
//   交由 1号位 PhaseOneConstraintBuilder 按本域 Material/Dept 自行匹配。
BatchPolicies = frozenSnapshot.BatchPolicy,
```

**编译**：`dotnet build LPS.APS.Application` ⇒ **0 错误**（43 警告均为既有，与本次改动无关）。

## 三、裁剪语义——**取 A，理由三条**

1. **粒度里有可空轴，裁剪语义不安全**。`BatchPolicyRuleSnapshot.ProductionDepartmentId` **可空 = Material 级默认**。按域裁剪若只保留 `(Material, Dept)` 精确对，会把 `(Material, NULL)` 默认行一起裁掉 ⇒ 1号位 拿到空 ⇒ 按「每需求恒 1 批」处理，而 **§十一 第 3 条明令 1号位 不得自造全局默认** —— 即"裁剪"会静默改变行为，且改错方向。
2. **与既有同向**。⑤ `SolverStrategy`、⑥ `CandidateGuardrail` 都是整块透传；全链只有 ⑦ Setup 裁剪，其依据是**规则自身文本指定的 §19.3「只加载本 Domain」**。⑧ 无此依据 —— 04/05 冻结文档里 **Batch Policy 段（v5.1.9 §Batch Policy）未规定裁剪**，与来件说法一致。
3. **裁剪无收益、有风险**。`TaskSplitRuleConfig` 为配置表（dev 库实测 **6 行 / 6 活**），非 ⑦ 那种大表；全量透传的体积代价可忽略，而跨域规则在本域不会命中任何需求材料。

> ⇒ **无需 1号位 出裁剪确认**：A 方案下 1号位 `PhaseOneConstraintBuilder` 按本域 Material/Dept 自行匹配即可，本域外规则天然匹配不上。请 3号位 据此回填 05 契约 §6.10.5：「**装载端不裁剪，按 Domain 匹配由 1号位 消费侧决定**」。

## 四、⚠️ 端到端实测的**前置阻塞**（不在 2号位 辖区）

落码已通，但**当前跑不出非空 `BatchPolicies`**，原因在发布链而非投影链：

- 现行生效版本 **`StrategyProfileVersion.Id=811`（SP-DEMO-V3.0，IsDefault=1）** 的 `ParameterSetVersion.Id=807`，其 `ContentSnapshotJson` 子块 = `Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail` —— **无 `BatchPolicy`**。
- 对照时间：**811 发布于 2026-09-20 14:58**；而 `TaskSplitRuleConfig` 的 6 条规则的 `CreatedAt` = **2026-10-08 03:37~08:10** ⇒ **版本先于规则存在**，发布当时 `InjectBatchPolicyAsync` 无规则可注入（或该注入尚未上线）。

⇒ **需要 3号位 在治理侧重新发布一次参数集版本**，使 `ContentSnapshotJson.BatchPolicy` 子块落地。发布后：
- 2号位 侧验证：`SolverStrategySnapshot.BatchPolicies.Count > 0`（断点/日志）；
- 1号位 侧验证：`ConstraintContext.ExecutionBatchPolicies` 收到本域规则（FULL Run）。

（治理服务已由 Scrutor 自动注册：`TaskSplitRuleConfigGovernanceService` 在 `LPS.APS.Application.Services` 命名空间，`AddApplicationServices()` 扫描注入 ⇒ `_taskSplitRuleConfigGovernanceService` **非 null**，注入路径通畅。）

## 五、给 1号位 的消费侧口径（随本回执知会）

- `SolverStrategySnapshot.BatchPolicies` = **本 Run 全量批量策略**（未按域裁剪）。请按 **本域 `MaterialId` / `ProductionDepartmentId`** 自行匹配；`ProductionDepartmentId IS NULL` 为 **Material 级默认**，对本材料所有部门生效。
- 域外规则会出现在列表里但永不命中 → **不是错误**，请勿据此判非法输入。
- `BatchPolicies` 为空 ⇒ **每需求恒 1 批（不拆）**；**不得自造全局默认策略**（来件引用 §十一 第 3 条）。

---

> 落码范围：`LPS.APS.Application/Services/PeggingOrchestrator.cs` 单点 +1 行（+注释）。零 DDL、零签名变更、未触碰 `LPS.APS.Scheduling`。台账已记 `§T-1008o`。
