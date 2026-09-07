# 白天/夜间排程阻塞 —— RoutingOperation 缺 TransferBatchSize 列（提交 2号位）

> **文档性质**：3号位 → 2号位 正式交付（跨号位协作，端到端排程阻塞）
> **版本**：v1.0
> **日期**：2026-09-02
> **状态**：待 2号位 处置（二选一，见 §三）
> **红线**：#6 DB 表结构变更由 2号位 专属执行

---

## 一、背景与定位过程

`RealSchedulingIntegrationTest`（v5.1.2 完整排程流程集成测试）持续失败。本次定位链：

1. **测试传 `strategyProfileVersionId=1L`（不存在）** → `LoadStrategyConfigAsync`（SchedulingOrchestrator.cs:683 `if (rows.Count == 0) return;`）静默返回 → 空策略上下文 → Pegging 全失败。
   → 已由 3号位 改为真实策略包 `SP-DEMO-V2.0`（Id=251，B 项种子已就位）。
2. **`SchedulingRunResult.ErrorMessage` 在 Pegging 失败路径从不填充** → 失败原因不可见。
   → 已由 3号位 修复：聚合各单失败原因到 `ErrorMessage`（SchedulingOrchestrator.cs result 构造）。
3. **聚合后暴露真实根因**：`Pegging 失败 OrderId=109720: 列名 'TransferBatchSize' 无效。`（SQL Server 207）

---

## 二、根因：代码按契约引用列，`RoutingOperation` 表缺该列

| 项 | 证据 |
|---|---|
| 报错 SQL | `LPS.APS.Application/Services/PeggingOrchestrator.cs:1761`：`SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, OperationName, ProcessType, StageCode, StandardDuration, SetupTime, TransferBatchSize, IsActive FROM RoutingOperation WHERE IsActive = 1 AND MaterialId IN @MaterialIds` |
| 库实测 | `RoutingOperation` 表 Batch 相关列：**无**（`INFORMATION_SCHEMA.COLUMNS` 查询为空） |
| 数据所在 | `RoutingPlanningParam` 表存在 `MinBatchSize / MaxBatchSize / TransferBatchSize` |
| 实体契约 | `LPS.APS.Core/Entities/Aps/RoutingOperation.cs:56` 注释：**"来源：RoutingPlanningParam.TransferBatchSize，由2号位加载"** |

**结论**：3号位 SQL 按实体契约引用 `TransferBatchSize`（契约注释明确由 2号位 加载），非代码笔误；缺口在 `RoutingOperation` 表结构与装载——`TransferBatchSize` 数据在 `RoutingPlanningParam`，但 2号位 未装载到 `RoutingOperation`（建列/映射未落地）。

---

## 三、请 2号位 二选一处置（红线 #6：2号位 专属）

1. **方案一（推荐，最小契约一致）**：`RoutingOperation` 表补 `TransferBatchSize DECIMAL/NUMERIC NULL` 列（DDL v5.1.5，幂等 `IF COL_LENGTH` 保护），并由装载逻辑从 `RoutingPlanningParam.TransferBatchSize` 按对应工序填充。3号位 SQL 零改动。
2. **方案二**：确认查询应改为 `RoutingOperation` JOIN `RoutingPlanningParam` 取该列——3号位 依 2号位 回执改 `PeggingOrchestrator.cs:1761` SQL（去掉直接列引用改 JOIN）。

> 两者择一即可，3号位 侧按回执收口。

---

## 四、影响范围

- **端到端阻塞**：`RoutingOperation` 表有数据时，凡经 Pegging 装载 Routing 三件套的排程（白天候选、夜间 Run）都会在此失败（SQL 207）。
- 关联消费方：`PhaseOneConstraintBuilder.cs:172`、`PhaseTwoInitialScheduler.cs:560/577/585/645-649`（读 `RoutingOperation.TransferBatchSize` 实体属性做 Stage overlap 阈值启动，P7 修复依赖此列）。
- 阻塞项：RealSchedulingIntegrationTest 转绿、C 线联调、夜间 Run 端到端。

---

## 五、2号位 回执表

| 项 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 方案一 | RoutingOperation 补 TransferBatchSize 列 + 从 RoutingPlanningParam 装载 | □ 采纳　□ 需调整 |
| 方案二 | 3号位 改 SQL 为 JOIN RoutingPlanningParam | □ 采纳　□ 需调整 |
| 预计落地 | 列/装载就绪后 3号位 重跑 RealScheduling 验证 | □ 已落地（版本号/时间） |
