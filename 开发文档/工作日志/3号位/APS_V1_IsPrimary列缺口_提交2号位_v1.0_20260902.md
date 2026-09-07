# 白天/夜间排程阻塞 —— OperationResourceEligibility 缺 IsPrimary 列（提交 2号位）

> **文档性质**：3号位 → 2号位 正式交付（跨号位协作，端到端排程阻塞）
> **版本**：v1.0
> **日期**：2026-09-02
> **状态**：待 2号位 处置（建议一，见 §三）
> **红线**：#6 DB 表结构变更由 2号位 专属执行

---

## 一、背景与定位过程

`RealSchedulingIntegrationTest`（v5.1.2 完整排程流程集成测试）验证链延续（承接《APS_V1_TransferBatchSize列缺口_2号位回执》方案二落地后）：

1. **TransferBatchSize（已修复）**：2号位 将 `LoadRoutingContextAsync` 改为 JOIN `RoutingPlanningParam`，SQL 207 消除。
2. **本项暴露**：重跑后报 `Pegging 失败 1/1 单：OrderId=109721: 列名 'IsPrimary' 无效。`（SQL Server 207）——同一 Pegging 装载路径的下一列缺口。

## 二、根因：库表结构与冻结 DDL v5.1.4 不一致（非代码/非契约错误）

| 项 | 证据 |
|---|---|
| 报错 SQL | `LPS.APS.Application/Services/PeggingOrchestrator.cs:1924-1930`：`SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, ResourceId, Priority, CapacityFactor, IsPrimary, IsActive FROM OperationResourceEligibility WHERE IsActive = 1 AND MaterialId IN @MaterialIds` |
| 冻结 DDL | `开发文档/2026-09-02文档更新/APS_数据库表结构设计_v5.1.4_20260901_Domain专项冻结对齐_治理修正版.sql:2688`：**`IsPrimary BIT NOT NULL DEFAULT 0`** —— 契约**预期有该列** |
| 实体契约 | `LPS.APS.Core/Entities/Aps/OperationResourceEligibility.cs:47`：`public bool IsPrimary { get; set; }` —— 与 DDL 一致 |
| 库实测 | `APS_Production.OperationResourceEligibility` 仅 14 列（Id/MaterialId/ProductionDepartmentId/RouteCode/PathId/OperationCode/ResourceId/Priority/CapacityFactor/IsActive/EffectiveFrom/EffectiveTo/CreatedAt/UpdatedAt）——**无 `IsPrimary`** |

**结论**：DDL、实体、代码三方契约一致，唯独 **APS_Production 实际库表结构落后于冻结 DDL v5.1.4**——`IsPrimary` 列未落地（建表时按旧版执行，或迁移未同步）。Pegging 装载按契约 SELECT 报 207，属**库结构同步缺口**，非代码笔误。

> 与 TransferBatchSize 的差异：那项是「代码引用位置错，DDL 明确放 RoutingPlanningParam」；本项是「代码位置对，**库缺列**」。

## 三、请 2号位 处置（红线 #6：2号位 专属）

1. **建议（最小，代码零改动）**：在 APS_Production 对 `OperationResourceEligibility` **幂等补列**（对齐冻结 DDL v5.1.4:2688）：
   ```sql
   IF COL_LENGTH('dbo.OperationResourceEligibility', 'IsPrimary') IS NULL
       ALTER TABLE OperationResourceEligibility ADD IsPrimary BIT NOT NULL DEFAULT 0;
   ```
   DDL v5.1.4 已冻结含该列，**无需改契约文档**；3号位 侧代码（PeggingOrchestrator SELECT）本就正确，零改动。
2. 若 2号位 判断 ODS 契约视图 `ext_APS_OperationResourceEligibility_View` 尚未输出该列、短期不接入 → 需裁定「能力首选语义」缺失期的处置（但 DDL 已冻结，不推荐绕道）。

## 四、影响范围

- **端到端阻塞**：凡经 Pegging 装载资源能力关系的排程（白天候选、夜间 Run）在 `OperationResourceEligibility` 有数据时在此失败（SQL 207）。
- 关联消费方：`PeggingOrchestrator.cs:1924-1930`（资源能力装载，`IsPrimary` 用于首选资源判定）、1号位 排程资源选择。
- 阻塞项：RealSchedulingIntegrationTest 转绿、C 线联调、夜间 Run 端到端。

## 五、2号位 回执表

| 项 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 建议一 | APS_Production 幂等补 `IsPrimary BIT NOT NULL DEFAULT 0`（对齐冻结 DDL v5.1.4:2688），代码零改动 | □ 采纳　□ 需调整 |
| 建议二 | ODS 视图未输出 IsPrimary 时的缺失期裁定 | □ 采纳　□ 需调整 |
| 预计落地 | 补列后 3号位 重跑 RealScheduling 验证 | □ 已落地（版本号/时间） |
