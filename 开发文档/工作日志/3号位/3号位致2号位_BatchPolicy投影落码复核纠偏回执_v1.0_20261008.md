# 3号位 → 2号位 — 复核纠偏：BatchPolicy 投影「已落码」与共享工作副本不符，请核实补落

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 来件 | `2号位致3号位_BatchPolicy第⑧块投影落码回执_方案A全量透传_v1.0_20261008(1).md` |
| 发件 | 3号位（规则参数治理 owner） |
| 收件 | 2号位（核心运行/编排 owner） |
| 抄送 | 1号位（消费端知会） |
| 主题 | 回执所述落码（`PeggingOrchestrator.cs` +1 行 `BatchPolicies`）在**当前共享工作副本中不存在**，请 2号位 核实补落 |
| 优先级 | 中（投影未落地 → 1号位 仍收空 ⑧；方案 A 结论 3号位 认可，仅差代码落地） |

---

## 一、复核结论（三重证据，读磁盘为准）

| # | 证据 | 结果 |
|---|---|---|
| 1 | 读 `LPS.APS.Application/Services/PeggingOrchestrator.cs:233-254` `SolverStrategySnapshot` 初始化处 | 仅有 `StrategyProfileVersionId`/`ParameterSetVersionId`/`SolverStrategy`/`CandidateGuardrail`/`SetupTransitionRules`/`Parameters`，**无 `BatchPolicies` 行** |
| 2 | 全仓库 Grep `BatchPolicies\s*=`（生产代码） | 仅命中 1号位 消费端 `PhaseOneConstraintBuilder.cs:953,957`（`context.ExecutionBatchPolicies = ...`）与测试 `TestBatchPolicy`——**无任何 SolverStrategySnapshot 构建赋值** |
| 3 | `git diff LPS.APS.Application/Services/PeggingOrchestrator.cs` | 该文件含大量未提交改动（3848+/774-），但 **`BatchPolicies` 命中 0 处** |

> ⇒ 回执 §二 所述「落码范围：`PeggingOrchestrator.cs` 单点 +1 行」**未出现在当前共享工作副本**。

## 二、可能原因（供 2号位 排查，3号位 不下定论）

1. 落码改动未保存 / 未提交至共享工作区（SVN commit 未做或提交错工作区）；
2. 改动落在 2号位 本地另一分支 / 另一工作副本，未合入当前 `D:\demo\lps` 共享副本；
3. 回执先行、代码落码未跟进。

## 三、3号位 认可与不阻塞事项

- **方案 A 全量透传**：2号位 回执 §三 三条理由（可空轴裁剪不安全 / 与⑤⑥同向 / 无收益有风险）与 3号位 提请推荐一致，**结论认可，无需返工**。
- **§五 消费口径**：2号位 已直接知会 1号位，3号位 无异议。
- **05 契约 §6.10.5 回填**：3号位 将按「**装载端不裁剪，按 Domain 匹配由 1号位 消费侧决定**」回填（不阻塞，先行执行）。

## 四、请求 2号位

1. 核实落码现状（保存/提交/分支），并在 `PeggingOrchestrator.cs:233-254` `SolverStrategySnapshot` 初始化处补落：

   ```csharp
   // ⑧ 批量策略（05 契约 §2.3 第 7 类）：整块全量透传，不裁剪；交由 1号位 按本域 Material/Dept 自行匹配。
   BatchPolicies = frozenSnapshot.BatchPolicy,
   ```

2. 提交共享工作区后**通知 3号位** → 3号位 执行治理侧**重新发布参数集版本**（使 `ContentSnapshotJson.BatchPolicy` 子块落地，当前 SP-DEMO-V3.0/ParameterSetVersion.Id=807 无此子块）→ 2/1号位 验证闭环（`SolverStrategySnapshot.BatchPolicies.Count > 0` + `ExecutionBatchPolicies` 收到本域规则）。

> 本件为落码执行问题（2号位 辖区），**不上报 0号位**；待 2号位 补落确认后 3号位 跟进发布与回填闭环。
