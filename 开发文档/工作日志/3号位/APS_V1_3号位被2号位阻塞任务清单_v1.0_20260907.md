# APS V1 3号位 被 2号位 阻塞任务清单 v1.0

> **收件**：2号位（Pegging + Scheduling，回执）
> **抄送**：0号位（PM，域权裁决项）、5号位（仓库事实源归属）
> **发件**：3号位（应用编排 / 运行生命周期治理）
> **日期**：2026-09-07
> **性质**：3号位 侧整理的「当前仍被 2号位 阻塞」任务汇总，每项给清需求、原因与推荐方案，供 2号位 回执与催办。
> **红线**：不越位——下各项均属 2号位 职责（数据引擎/同步、主流程执行、契约提供），3号位 侧已做到「可自主推进的上限」，仅留「约定接缝 / 数据缺口 / 一句话确认」给 2号位。

---

## 〇、一句话结论

3号位 当前被 2号位 阻塞的实质项共 **2 项**（P0×1、P1×1），另 **1 项连带待办**受 0号位 拍板前置（拍板后转 2号位 执行）。其余历史阻塞（SolverStrategy 投影、Pegging 双重插入、权威仓库事实源）经代码核查**已释放/已确认**，无需 2号位 再处理。

---

## 一、阻塞清单总览

| # | 优先级 | 阻塞项 | 需求一句话 | 3号位侧状态 | 2号位侧待办 |
|---|---|---|---|---|---|
| 1 | **P0** | MaterialStageDeptContext 全表为空 | RealScheduling 端到端排程转绿（Task/Pegging 产出非零） | 已提报（09-03），代码零改动 | 恢复重建作业 + 补测试物料上游 |
| 2 | **P1** | 白天候选触发接缝（方案 A/B/C） | 白天候选创建后触发 2号位 主流程排程并收口 Run | 创建侧已实现，触发未接线 | 裁定 A/B/C + 提供内部运行服务契约 |
| 3 | **P1*** | INSERT_ORDER_WHATIF 插单试排新订单装载 | 插单试排新订单入口（需求源头缺口） | 待 0号位 拍板口径 | 拍板后：按候选 VersionId 装新单 + 答复 Q1 BOM 白天展开时机 |

> `*` 项当前主阻塞为 **0号位（PM）** 拍板（要不要吃白天新单 + 口径 A/B + Q1/Q2），非纯 2号位；仅因拍板后落地动作落在 2号位，故列入连带项供 2号位 提前知悉。

---

## 二、逐项详述

### 1.【P0】MaterialStageDeptContext 全表为空 —— 端到端排程阻塞

**需求描述**
`RealSchedulingIntegrationTest`（v5.1.2 完整排程流程集成测试）是 3号位 侧唯一仍在失败的集成测试（09-04 实测 198/197/1，唯一失败即此项）。目标是排程产出非零 Task/Pegging 行，验证「需求 → Pegging → 1号位 Solver → Task 落盘」全链打通。

**阻塞原因（根因 2号位 数据同步职责）**
1号位 部门锁定规则（`PhaseOneConstraintBuilder.ApplyDepartmentLock`，最小 B）按 `(MaterialId, StageCode)` 查 `request.MaterialStageDepartmentContexts` 取 `ProductionDepartmentId`；查不到 → 该 Material 整条 Routing 剔除 → 需求判 `Unscheduled` → `FinalTasks=0`。

库实测（09-03）：
- `MaterialStageDeptContext`（部门锁定输入凭据）＝ **0 行**；
- 上游 `MaterialSupplyContext`＝179.8 万行（`IsCurrent=1`，`DefaultProductionDeptCode` 非空 96.2 万行），`sp_RebuildMaterialStageDeptContext` 已建——**源充足但重建未产出**；
- 测试物料 `TEST_A_20260713_390` 在 `MaterialSupplyContext` 亦 0 行（家族三物料均无，上游 ODS 同步缺失）。

结论：每日 01:55 重建作业（`MaterialStageDeptContextRebuildService` → `sp_RebuildMaterialStageDeptContext`）未产出数据（未运行 / 失败 / SP 过滤异常）。

**推荐方案（2号位，代码零改动）**
1. **主项**：恢复/执行重建，使 `MaterialStageDeptContext` 产出非空行：
   ```sql
   EXEC sp_RebuildMaterialStageDeptContext @TriggerMode='FULL', @BatchNo='MANUAL_20260903';
   SELECT COUNT(*) FROM MaterialStageDeptContext;   -- 期望 > 0
   ```
   若重建后仍 0 行 → 排查 SP 内过滤/Join（源已有 96 万可重建行）。
2. **测试物料上游**：确认 `TEST_A_20260713_390` 家族是否需在 `MaterialSupplyContext` 补上游 ODS 同步行（含 `DefaultProductionDeptCode` 非空）；若该物料属临时测试物料，请 2号位 裁定提供「真实 + 部门上下文齐备」的替代验证物料。

**关联文档**：`APS_V1_MaterialStageDeptContext表空_提交2号位_v1.0_20260903.md`

---

### 2.【P1】白天候选触发接缝（方案 A/B/C）—— 白天候选功能未闭环

**需求描述**
4号位 从页面发起白天候选（`MANUAL_RESCHEDULE` → `POST api/Governance/run/candidate`），3号位 建 Run + Candidate 壳后，应触发 2号位 主流程执行排程，并**收口该 Run**（回填 PlanVersion 结果 + ScheduleRun 终态 + CompletedAt），产出候选结果供 4号位 查看/确认/激活。

**阻塞原因（接缝契约未定，越位红线阻却）**
- 创建侧 3号位 已全部实现：建 RUNNING Run + BUILDING 候选壳（单事务原子写）+ 8 道校验 + 审计，`RunLifecycleService.CreateCandidateRunAsync` 完成；
- **触发处留接缝未接线**（`RunLifecycleService.cs:451-452`）：3号位 不得越位调用 2号位 内部方法，需 2号位 提供规范触发契约；
- 触发方案 A/B/C 三选一未裁定（见 B-1 契约草案 §四），其中方案 C 因基线壳 `Status='Created'` 领取与 B-1 壳 `BUILDING` 冲突，天然不适配。

**推荐方案（建议方案 A）**
- **方案 A（推荐）**：2号位 在 `ISchedulingOrchestrator` 暴露 `RunSchedulingAsync(planVersionId, scheduleRunId, strategyProfileVersionId)`（按传入 Run Id 同步执行计算并收口该 Run）；3号位 创建成功后调用。
  - **红线 #5 前置**：2号位 先更新《APS_集成接口设计》接口契约文档，再改接口签名。
- 方案 B：2号位 提供 on-demand job（如 `DomainSchedulingJob.Enqueue(runId)`），3号位 创建成功后投递。
- 方案 C（不推荐）：需先将壳初始 Status 从 `BUILDING` 改为 `Created` 或扩展领取条件，侵入夜间主流程。

**连带契约点（请 2号位 一并回）**
- **P1 Purpose 落库**：DDL 无 `ScheduleRun.Purpose` 列，3号位 建议新增列（DB 变更 2号位 专属，红线 #6）；裁定后 2号位 落 DDL、3号位 补写入。
- **P3 BasePlanVersionId 执行期快照保护**：执行期是否对 Base ACTIVE 版本做快照/引用保护（防执行期间被替换）；若需要，冻结机制与实现归属 2号位。

**关联文档**：`APS_V1_白天Candidate运行创建_B1契约草案_v1.0_20260902.md`

---

### 3.【P1*】INSERT_ORDER_WHATIF 插单试排新订单装载（连带，主 0号位）

**需求描述**
白天候选 `INSERT_ORDER_WHATIF`（插单试排 / CTP / 插入影响分析）需「基线订单 ∪ 一张新插订单」作为需求集；当前整条链无「新订单从哪来、怎么进候选」的字段/机制。

**阻塞原因（源头契约缺口，非实现）**
3号位 需求只覆盖「以当前 ACTIVE 为只读基线、重排基线那套订单」；候选壳借基线订单填需求集对 `MANUAL_RESCHEDULE` 成立，对 `INSERT_ORDER_WHATIF` 不成立。此裂缝已在 `APS_V1_插单试排新订单装载缺口_2号位致PM_v1.0_20260904.md` 提报 0号位 拍板。

**推荐方案（0号位 拍板后转 2号位）**
1. **口径 A（推荐）**：3号位 创建候选时传 `OrderNos` 列表（显式、可控、可审计）；
2. 2号位 复用 `OrderLoadingService`（`sp_SyncOrdersToPartitionTable`，`Order_Canonical → Order 分区表`）按**候选 PlanVersionId** 装新单，需求集＝基线 ∪ 新单；
3. **Q1**：新订单 BOM 白天展开时机谁保证（现 `PullBOMResultFromODSAsync` 仅夜间批跑），请 2号位/5号位 答复。

---

## 三、已解除阻塞（仅供 2号位 知悉，无需再处理）

以下历史阻塞经代码核查已释放，避免重复派单：

| 历史项 | 现状 | 证据 |
|---|---|---|
| SolverStrategy `AllowMerge`/`MaxIterations` 投影 | ✅ 已接 | `PeggingOrchestrator.cs:189-190` 已 `AllowMerge = solverStrategy.AllowMerge` / `MaxIterations = solverStrategy.MaxIterations` |
| PeggingOrchestrator 双重插入 `PeggingSupplyAllocation` | ✅ 已消 | `PersistSupplyAllocationAsync` 全文件无匹配，仅剩统一事务 `PersistDomainAndPeggingInTransactionAsync` |
| 白天候选创建侧（Run + 壳） | ✅ 已实现 | `POST api/Governance/run/candidate` + 8 道校验 + 审计（仅触发接缝未接线，见 #2） |
| 权威仓库事实源确认（INVALID_WAREHOUSE_REF） | ✅ 已确认并落地 | `GovernanceVersionService.cs:663` 已 `SELECT DISTINCT ProcessCode FROM [ext_MES_ProcessCode_View]`（APS 库，仓库字典） |

---

## 四、回执表（请 2号位 逐项勾选）

| # | 请 2号位 答复 | 回执 |
|---|---|---|
| 1 | 恢复 MaterialStageDeptContext 重建 + 测试物料上游裁定 | □ 已落地（时间/版本）□ 需调整 |
| 2 | 触发接缝选定 A/B/C（建议 A） | □ A □ B □ C |
| 2-P1 | Purpose 落库是否新增 `ScheduleRun.Purpose` 列 | □ 新增 □ 其他 |
| 2-P3 | BasePlanVersionId 执行期是否需快照保护 | □ 需要（归属 2号位）□ 不需要 |
| 3 | 插单试排口径待 0号位 拍板后，2号位 装新单 + 答复 Q1 | □ 已知悉（待 0号位） |