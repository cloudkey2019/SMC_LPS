# 白天/夜间排程阻塞 —— MaterialStageDeptContext 全表为空（提交 2号位）

> **文档性质**：3号位 → 2号位 正式交付（跨号位协作，端到端排程阻塞）
> **版本**：v1.0
> **日期**：2026-09-03
> **状态**：待 2号位 处置（见 §三）
> **红线**：不越位——本项为 2号位 数据引擎/同步职责（`LPS.APS.Engine.Services.Sync`），3号位 仅诊断提报

---

## 一、背景与定位过程

`RealSchedulingIntegrationTest`（v5.1.2 完整排程流程集成测试）验证链延续，承接《APS_V1_IsPrimary列缺口_提交2号位_v1.0_20260902.md》补列回执后：

1. **TransferBatchSize（已修复）** → **IsPrimary（已修复，2026-09-03 确认 SQL 207 消失）** → **BOM 数据缺口（已处置：方案 A 改用真实物料 `TEST_A_20260713_390`，BOM 边 2 行已齐）**。
2. **本项暴露**：真实物料 BOM/Routing/Eligibility/Inventory 均齐备后，重跑仍 `IFiniteCapacityScheduler.SolveAsync: FinalTasks=0, Success=True`，Task/Pegging 均 0 行，排程整体「成功」但零产出。

## 二、根因：部门锁定（最小 B）数据缺口 —— MaterialStageDeptContext 全表为空

排程链路：`PeggingOrchestrator` 组装 `DomainSolveRequest`（含 LogicalProductionDemands + Routing 三件套 + MaterialStageDepartmentContexts）→ 1号位 `FiniteCapacitySolver` 五阶段排程 → `PhaseOneConstraintBuilder.ApplyDepartmentLock`（部门锁定，PM 裁定《ProductionDepartment回复.md》最小 B）。

**1号位部门锁定规则**（`LPS.APS.Scheduling/Solvers/PhaseOneConstraintBuilder.cs:77-133`）：
- 按 `(MaterialId, StageCode)` 查 `request.MaterialStageDepartmentContexts` → `ProductionDepartmentId`；
- 任一工序 Stage 查不到 Context → 该 Material 整条 Routing 剔除；
- Routing 全剔 → Phase2「无工艺路线」→ 需求判 `Unscheduled` → `FinalTasks=0`。

**库实测证据（2026-09-03）**：

| 表 | 实测 | 结论 |
|---|---|---|
| `MaterialStageDeptContext`（部门锁定输入） | **0 行**（全表空） | 1号位 拿不到任何 (MaterialId,StageCode)→部门 映射 |
| `MaterialSupplyContext`（重建 SP 的上游源） | 1,798,466 行，`IsCurrent=1` 1,798,464 行，其中 `DefaultProductionDeptCode` 非空 962,076 行 | 上游源充足，非空映射 96 万行 |
| `sp_RebuildMaterialStageDeptContext` | 存在（`sys.procedures` 命中） | SP 已建 |
| 测试物料 `TEST_A_20260713_390` 在 `MaterialSupplyContext` | **0 行**（家族 A/B/C 三物料均无） | 测试物料上游同步缺失 |

**结论**：重建链路「源有 96 万可重建行 + SP 已建」但输出 `MaterialStageDeptContext` 为 0 行，说明 **每日 01:55 重建作业（`MaterialStageDeptContextRebuildService` → `sp_RebuildMaterialStageDeptContext`）未产出数据**（未运行 / 运行失败 / SP 内过滤逻辑异常）。这是 2号位 数据同步职责缺口，非 3号位 代码问题。

> 测试物料 `TEST_A_20260713_390` 还存在二级缺口：其 `MaterialSupplyContext` 无上游行（家族三物料均无），即便重建作业恢复，该物料仍需上游 ODS 同步补一行（含 `DefaultProductionDeptCode` 非空）才能被 1号位 锁定部门。

## 三、请 2号位 处置（3号位 零改动）

1. **主项（最小，代码零改动）**：恢复/执行 MaterialStageDeptContext 重建（每日 01:55 作业），使 `MaterialStageDeptContext` 产出非空行：
   ```sql
   -- 手工触发一次重建（2号位 权限执行，替代等待 01:55）
   EXEC sp_RebuildMaterialStageDeptContext @TriggerMode='FULL', @BatchNo='MANUAL_20260903';
   SELECT COUNT(*) FROM MaterialStageDeptContext;  -- 期望 > 0
   ```
   若重建后仍 0 行 → 请排查 SP 内过滤/Join 逻辑（源已有 96 万可重建行）。
2. **测试物料上游**：确认 `TEST_A_20260713_390` 家族是否需在 `MaterialSupplyContext` 补上游 ODS 同步行（若该物料为临时测试物料、不属正式主数据，请 2号位 裁定是否提供替代「真实 + 部门上下文齐备」的验证物料）。

## 四、影响范围

- **端到端阻塞**：凡 1号位 排程的域，其需求物料在 `MaterialStageDeptContext` 无 `(MaterialId,StageCode)` 映射时，全部判 `Unscheduled` → `FinalTasks=0` → Task/Pegging 零行。
- 关联消费方：`FiniteCapacitySolver.ApplyDepartmentLock`（1号位）、`PeggingOrchestrator.LoadMaterialStageDeptContextAsync`（2号位，仅读表，非根因）。
- 阻塞项：RealSchedulingIntegrationTest 转绿、C 线联调、夜间 Run 端到端。

## 五、2号位 回执表

| 项 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 主项 | 恢复 MaterialStageDeptContext 重建，产出非空映射 | □ 采纳　□ 需调整 |
| 排查 | 若重建仍 0 行，排查 SP 过滤/Join（源 96 万可重建行） | □ 采纳　□ 需调整 |
| 测试物料 | 裁定 `TEST_A_20260713_390` 上游同步或替代验证物料 | □ 采纳　□ 需调整 |
| 预计落地 | 重建后 3号位 重跑 RealScheduling 验证 | □ 已落地（版本号/时间） |
