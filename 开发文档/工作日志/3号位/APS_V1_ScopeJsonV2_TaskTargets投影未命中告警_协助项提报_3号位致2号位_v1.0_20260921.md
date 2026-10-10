# ScopeJsonV2 TaskTargets 投影未命中告警 —— 协助项提报（3号位 致 2号位）

> **收件**：2号位（数据引擎 Owner，`PeggingOrchestrator` 投影域）
> **抄送**：0号位（PM）、1号位（Solver 消费方）
> **日期**：2026-09-21
> **依据**：1号位《ScopeJsonV2 载荷契约消费回执 v1.0 20260921》§二.2 治理层建议；2号位《ScopeJsonV2 统一回执 v1.0 20260921》M2~M5 投影
> **性质**：**正式协助项提报**——属 2号位 代码域（`PeggingOrchestrator.cs`），3号位 不越位直接改，转协助项请 2号位 处置
> **术语约定**：英文契约词一律「中文名称（英文）」；代码引 `文件:行`

---

## 〇、一句话

`TaskTargetOverrides` 投影在 `PeggingOrchestrator.BuildRunScopeAsync` 中按 `TaskId` 查复合键 `(DraftId, OperationCode)`，**未命中行直接跳过、无任何告警**（`PeggingOrchestrator.cs:4555-4564`，实测无 else 分支）。软目标本就 best-effort、不阻塞批次一，但 TaskId 漂移时会在车间无感的情况下静默丢软目标——请 2号位 补「未命中计数/告警」。

## 一、背景（为什么需要）

| 项 | 内容 |
|---|---|
| 投影代码 | `PeggingOrchestrator.cs:4552-4566`：`scope.TaskTargets` 逐行 `if (taskByKey.TryGetValue(t.TaskId, out var row))` 才组装 `TaskTargetOverride`；`TaskId` 未命中 `(DraftId, OperationCode)` 时**跳过、无 else、无日志** |
| 风险 | TaskId 是数据库自增键，历史数据清理/重建（DBCC CHECKIDENT、分库迁移、软删归档）会致漂移；届时传入的软目标静默消失，**车间以为软目标已下达，实际 Solver 从未收到**——排程结果偏离工单预期且不可追溯 |
| 现状态 | 1号位 第一批消费已交付（Phase4 软目标），测试 342 Unit 全绿；本协助项**不阻塞批次一**，属治理加固 |
| 与批次二关系 | 批次二 `ChangedResourceIds`/`InScopeLogicalDemandKeys` 详设仍走三方对齐，本协助项独立排期 |

## 二、建议口径（3号位 提议，2号位 认可或修订）

按 1号位 §二.2 原话「补一个未命中计数/告警」展开，三选一即可：

| 方案 | 落地 | 效果 |
|---|---|---|
| **A（推荐）** | 循环内统计 `missCount`，循环后 `if (missCount > 0) logger.LogWarning("TaskTargets 投影未命中 {Count} 行，TaskId 漂移或已归档", missCount)` | 一次告警、性能无损，日志可查 |
| B | 未命中行 Id 明细写入日志（`LogWarning` 附 `TaskId` 列表） | 可定位到具体 TaskId，日志量大 |
| C | 未命中 >0 时在 `RunScope` 附诊断字段（如 `ProjectionWarnings`），供前端/审计可读 | 面向业务可观测，需扩契约（红线 #5 契约先行） |

- **推荐 A**：改动最小、不扩契约、不阻塞批次一；如需后续面向业务可观测再升级 C。
- 不做任何失败语义变更（软目标仍 best-effort，未命中不拒收——与冻结 §十九「可违约并给 Explanation」一致）。

## 三、请 2号位 处置

1. 采纳/修订 §二 建议口径；
2. 落码 + `dotnet build` 回归（若改投影行为，请同步补/改测试锚）；
3. 回执 3号位：处置结果 + 排期。

---

**发送人**：3号位 ｜ **日期**：2026-09-21
**待办**：① 2号位 处置（§二 方案 A 建议）② 回执 3号位 ③ 3号位 归档《载荷契约消费回执》§四.1 ②
