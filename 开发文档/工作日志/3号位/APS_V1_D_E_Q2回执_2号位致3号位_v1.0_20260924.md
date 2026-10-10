# D/E/Q2 回执 —— 2号位 致 3号位

> 收件：3号位 ｜ 日期：2026-09-24 ｜ 版本：v1.0
> 对象：贵方《白天候选配合事项清单回执 v1.0》

---

## 一、D BusinessScopeDto —— 收讫，即落

贵方确认 DTO 已定型（`BusinessScopeDto` 三字段 `IsGlobal`/`ScopeType`/`ScopeValues`），`RunScope.BusinessScope` 字段已存在。2号位 收讫，将在 `BuildRunScopeAsync` 补一行透传。

---

## 二、E api/candidate —— 4 问逐项回

| # | 贵方问 | 2号位 答 |
|---|---|---|
| **E-1** | 端点集 | 最小一版：**`GET /api/candidate/{candidateId}/summary`**（返回单候选 Run 摘要，含 Status / 创建时间 / 订单数 / Task 数）+ **`GET /api/candidate/{candidateId}/comparison`**（返回 Base-vs-Candidate 差异）。无批量列表、无分页。 |
| **E-2** | expedite 挂点 | 挂 **`GET /api/candidate/{id}/expedite-impact`**，宿主先行（骨架端点）、expedite 内容后补（P1-2 数据准备就绪后填充） |
| **E-3** | 返回 DTO | `summary` 返回 `{ candidateId, runStatus, orderCount, taskCount, createdBy, createdAt }`；`comparison` 返回 `{ basePlanVersionId, candidatePlanVersionId, addedOrders[], removedOrders[], changedTasks[] }`。字段清单为最小集，后续可按需扩。 |
| **E-4** | 编排归属 | **确认**：宿主仅做 Web 薄层 → 委托 `RunLifecycleService` → `SchedulingOrchestrator`（2号位），3号位 不碰编排。E-4 红线成立。 |

---

## 三、Q2 容器口径 —— RuleSet

`FirmHorizonDays` / `FrozenHorizonDays` 走 **RuleSet（规则集）**，与既有策略通道一致。不另开 ParameterSet 或新容器。若需新列，走红线 #6（3号位 提 DDL 申请、2号位 落）。

---

**发送人**：2号位 ｜ **日期**：2026-09-24