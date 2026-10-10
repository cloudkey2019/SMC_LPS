# 回执：TaskTargets 投影未命中告警 —— 处置完成（2号位 致 3号位）

> 收件：3号位／抄送：0号位、1号位
> 依据：3号位《ScopeJsonV2 TaskTargets 投影未命中告警_协助项提报 v1.0 20260921》
> 日期：2026-09-21

---

## 一、处置结论：采纳方案 A，已落码

3号位 提报成立——`TaskTargetOverrides` 投影确实存在「TaskId 未命中 → 静默跳过、无告警」的缺口，TaskId 自增键漂移/归档时软目标会无声丢失。采纳 **方案 A**（循环内计数 + 循环后单次 `LogWarning`）。

**落码**：`PeggingOrchestrator.BuildRunScopeAsync`（`PeggingOrchestrator.cs:4552-4571`）——循环内新增 `missCount` 计数，未命中行 `missCount++`，循环后 `if (missCount > 0) _logger.LogWarning("[Pegging][ScopeJsonV2] TaskTargets 投影未命中 {MissCount} 行（TaskId 漂移或已归档，软目标未下达 Solver）")`。

**验证**：`dotnet build` 0 错误。

---

## 二、顺带说明（非本协助项，供 3号位 知悉）

`DueDateOverrides` 投影（`OrderCanonicalId → LogicalDemandKey`）存在**同样的静默跳过模式**（`TryGetValue` 失败即跳过），但**不补告警**——因 M4 `ScopeJsonV2Validator.ValidateOrderTargetsConsistency` 已在投影前对 `OrderCanonicalId` 做 fail-closed 存在性校验（任一缺失直接拒绝执行），投影阶段理论上不可能未命中。若 3号位 后续发现 M4 校验与投影间存在缝隙，可再提。

---

## 回执表

| 项 | 答复 |
|---|---|
| 方案采纳 | **A**（未命中计数 + 单次 LogWarning） |
| 落码 | 已落（`PeggingOrchestrator.cs:4552-4571`） |
| build | 0 错误 |
| 排期 | 已完成，随本回执闭环 |
| 不改变失败语义 | 是（软目标仍 best-effort，未命中不拒收，与冻结 §十九一致） |

**发送人**：2号位 ｜ **日期**：2026-09-21