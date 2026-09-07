# TransferBatchSize 列缺口 —— 2号位回执

> **文档性质**：2号位 → 3号位 正式回执（对《APS_V1_TransferBatchSize列缺口_提交2号位_v1.0_20260902(1).md》）
> **版本**：v1.0
> **日期**：2026-09-02
> **处置**：**采纳方案二**，已在 2号位 代码内落地，3号位 零改动。

---

## 一、处置结论：方案二（JOIN），非方案一

**理由**：冻结 DDL v5.1.4 明确把批量/规划参数**从 Routing 拆出**到 `RoutingPlanningParam`，注释「从原 Routing 表拆出 MinBatchSize/MaxBatchSize（当前无 ODS 来源，**不应污染工艺事实层**）」。`TransferBatchSize` 同属该表（v5.1.4 §2.6.7e，`DECIMAL(18,4) NULL`）。

方案一（补 `RoutingOperation.TransferBatchSize` 列 + 装载）等于把规划参数**反向塞回工艺事实表**，违背冻结设计意图，还会引入一份需要持续同步的冗余数据。方案二只读 `RoutingPlanningParam`、`RoutingOperation` 表结构不动，单一事实源，无需红线 #6 DDL 变更。

## 二、Ownership 澄清

报错 SQL（`SELECT … TransferBatchSize … FROM RoutingOperation`）在 **2号位 的 `PeggingOrchestrator.LoadRoutingContextAsync`**（[PeggingOrchestrator.cs](../../LPS.APS.Application/Services/PeggingOrchestrator.cs)），**不是 3号位 代码**。故方案二落地 = 2号位 改自己 SQL，**3号位 两种方案下均零改动**（贵文档 §三「3号位 改 SQL」的前提不成立）。

## 三、已落地内容

1. **TransferBatchSize 读取改 JOIN**（[PeggingOrchestrator.cs:1898](../../LPS.APS.Application/Services/PeggingOrchestrator.cs#L1898)）：
   ```sql
   SELECT ro.MaterialId, …, ro.SetupTime, rpp.TransferBatchSize, ro.IsActive
   FROM RoutingOperation ro
   LEFT JOIN RoutingPlanningParam rpp
     ON rpp.MaterialId = ro.MaterialId AND rpp.RouteCode = ro.RouteCode
    AND rpp.PathId = ro.PathId AND rpp.OperationCode = ro.OperationCode
   WHERE ro.IsActive = 1 AND ro.MaterialId IN @MaterialIds
   ```
   - 键 = `(MaterialId, RouteCode, PathId, OperationCode)`；`RoutingPlanningParam` 无部门维度，同一工序跨部门共享同一 `TransferBatchSize`（与冻结 DDL 一致）。
   - 无参数行 → `TransferBatchSize=NULL` → 1号位 按「无阈值」跳过（`PhaseTwoInitialScheduler` 已有 `HasValue` 守卫）。
   - **SQL 207 消除**，端到端能走到更深一层；`TransferBatchSize` 是否真有值属**数据覆盖率**（`RoutingPlanningParam` 尚无 MES 来源），数据来了 JOIN 自动生效，代码无需再改。

2. **ErrorMessage 聚合补上**（[SchedulingOrchestrator.cs:392](../../LPS.APS.Application/Services/SchedulingOrchestrator.cs#L392)）：贵文档 §一.2 记「已由 3号位 修复」，但 2号位 工作区未见该改动，已在 `ExecuteDomainAsync` 结果构造补上——Pegging 部分失败时聚合各单失败原因到 `ErrorMessage`（截取前 10 单，超出标注总数）。

## 四、回执表

| 项 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 方案一 | RoutingOperation 补列 + 从 RoutingPlanningParam 装载 | ❌ 不采纳（违反冻结「不污染工艺事实层」设计） |
| 方案二 | 改 SQL 为 JOIN RoutingPlanningParam | ✅ **采纳并落地**（2号位 自己改，3号位 零改动） |
| 预计落地 | 列/装载就绪后重跑 RealScheduling 验证 | ✅ 已落地（2026-09-02），请 3号位 重跑 `RealSchedulingIntegrationTest` 验证（预期不再报 SQL 207） |
