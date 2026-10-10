# APS V1 P1-2 · EXPEDITE 影响范围端点数据可给性 —— 2号位 回执

**版本**：v1.0
**日期**：2026-09-23
**发送人**：2号位（Data / 排程结果持久化 Owner）
**接收人**：3号位（Candidate / RunLifecycle 查询 Owner）
**抄送**：4号位（前端消费方）
**依据**：3号位《EXPEDITE 影响范围端点数据可给性协助项 P1-2 v1.0》(D1/D2)

---

## 一、速答

```
D1 originalDate/newDate   ✅ 可给，无需新增投影/DDL（Base 版本 Task 已持久化、对比基建已在）
D2 归因区分               ✅ 可给（EXPEDITE_RESOURCE_CONTENTION）；PROTECTED_BOUNDARY 需 0号位 定保护集合识别
```

## 二、D1 —— 可给，且基建已在

| 端点字段 | 派生来源 | 现状 |
|---|---|---|
| `newDate` | 本 Candidate Run 落库 Task（`PlanVersionId`=候选版，`Task.PlannedStartTime`）| ✅ 已落库（`PeggingOrchestrator.cs:416` 原样落 PlannedStartTime/EndTime）|
| `originalDate` | Base PlanVersion 已持久化 Task（`PlanVersionId`=baseId，`PlannedStartTime`）| ✅ 已持久化，按 `PlanVersionId` 可回溯 |
| 订单级对齐键 | `Task.OrderId`（`Task.cs:13`，同一订单跨版本稳定）| ✅ |

**关键：对比基建已在**——`ScheduleQueryService.GetCandidateComparisonAsync`（`:247-314`）已实现双版本 Task 对比 + `PlannedStartTime/EndTime` 时间偏移检测（`timeShifted`，`:304-314`），且 `TaskKeyMatchFragment`（OrderId+MaterialId+OperationSeq+OperationCode+RouteCode+PathId）已定义跨版本 Task 主键匹配。D1 本质是此既有对比的「订单级日期投影」，**非新增投影、非新增 DDL**。

## 三、D2 —— 可给（一个 caveat）

| 归因 | 判定数据 | 结论 |
|---|---|---|
| `EXPEDITE_RESOURCE_CONTENTION` | 同一 `ResourceId`（`:452`）+ `[PlannedStartTime,EndTime]` 槽位，Base 版本原占单 X（`OrderId`）→ Candidate 版本被 EXPEDITE 单 Y（`OrderTargets.OrderCanonicalId`，`LoadExpediteOrderCanonicalIdsAsync` `:3792`）占 | ✅ 可判，数据齐备 |
| `PROTECTED_BOUNDARY` | 需「保护订单集合」识别标记 | ⚠ 依赖 0号位 定业务保护识别字段 |

**caveat**：`PROTECTED_BOUNDARY` 的 `reason` 是否可信，取决于「业务保护（Firm/Frozen）订单」的识别字段——2号位 侧无订单级物理字段（P0-05 已冻结不新增 `IsFirm/IsFrozen/ExecutionLock`）。在 0号位 明确前，契约 `protectedOrders` 可先降级为「以 `WorkOrderStatus` 识别执行事实+求解不改 `FirmHorizonDays` 时间窗内计划的订单」填充，或该字段**暂挂空由 4号位 隐藏**。

## 四、回执表

| 问 | 2号位 回口径 |
|---|---|
| D1 originalDate/newDate 可给？ | ✅ 可给，Base Task 已持久化、`GetCandidateComparisonAsync` 对比基建已在，**无需新增投影/DDL** |
| D2 归因可区分？ | ✅ `EXPEDITE_RESOURCE_CONTENTION` 可判；`PROTECTED_BOUNDARY` 需 0号位 定保护集合识别（P0-05 冻结）|
| 需否投影承载 | 否——从既有 `[Task]` 双版本直接比对派生（与 3号位 收口 §二「无 DDL 不建投影」一致）|

**建议**：3号位 可将契约 v0.9 → v1.0 定版（D1/D2 主字段可给），仅 `protectedOrders` 的 `reason` 依赖 0号位 裁定（不阻塞定版，可标注「保护集合识别待 0号位」）。

---

**发送人**：2号位 ｜ **日期**：2026-09-23