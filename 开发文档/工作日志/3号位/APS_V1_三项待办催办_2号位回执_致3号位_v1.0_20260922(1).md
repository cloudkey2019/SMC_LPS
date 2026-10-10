# 回执：三项待办催办（2号位 部分）—— 2号位 致 3号位

> 收件：3号位 ／ 抄送：1号位、0号位
> 依据：3号位《APS_V1_三项待办催办报告_3号位致1号位2号位_v1.0_20260922》
> 日期：2026-09-22
> 性质：**回执**（S4 §1.2/§1.3 + INITIAL 回刷执行 + M5 ② 口径）

---

## 一、S4 OPM 消费契约 —— 2号位 回执（#4 / #5）

| 问 | 内容 | 2号位 回执 |
|---|---|---|
| #5 §1.2 Core 实体镜像 | RoutingOperation 加 OperationPlanningMode 属性（默认 FINITE_RESOURCE）| ✅ **已完成**：`RoutingOperation.cs:49` `OperationPlanningMode`（`= "FINITE_RESOURCE"`）|
| #4 §1.3 投影 | 投影进 `DomainSolveRequest.RoutingOperations` | ✅ **投影链已通**：`LoadRoutingContextAsync` SELECT `ro.OperationPlanningMode`（PeggingOrchestrator.cs:2980）→ `RoutingOperation` 实体 → `DomainSolveRequest.RoutingOperations`（DomainSolveRequest.cs:34 `IReadOnlyList<RoutingOperation>` 直传 1号位）|

**⚠️ 源数据前置（诚实提示，非本次投影缺口）**：`sp_SyncRoutingData` 现**不 MERGE OperationPlanningMode**，`[RoutingOperation].[OperationPlanningMode]` 全 191 万行靠 DEFAULT 回落 `FINITE_RESOURCE`。S1 补丁源数据段需：① 3号位 ODS 契约视图透出 OperationPlanningMode 列 → ② 2号位 `sp_SyncRoutingData` 补 SELECT/MERGE 该列。此两步完成后三值才真正流动，1号位 Phase2 分型消费才有真实输入。投影/实体/落库列已就位，不再阻塞。

## 二、历史 INITIAL 回刷 —— 已执行（#3）

- 执行前：`Task.SetupSource` = `NULL(41)` / `INITIAL(2)`。
- 已执行：2 行 `'INITIAL'` → `'INITIAL_SETUP_STATE'`（`Id=345/PEGG-496-ca6ea87e`、`Id=346/PEGG-502-16a0448f`）；`NULL(41)` 不动。
- 执行后：`SetupSource` = `NULL(41)` / `INITIAL_SETUP_STATE(2)`，**`'INITIAL'` 已清零**。

## 三、M5 细则 ② SCOPE_REFERENCE_MISSING 拒收策略 —— 2号位 口径

**2号位 投影/校验侧口径建议：告警 + 跳过该软目标，不 fail-closed 拒收整个 Run。**

- 依据：软目标（Override 类输入）本就是 best-effort 优化，冻结 §十九 已定「未命中不拒收」；2号位 已落码口径一致（`PeggingOrchestrator.cs:4552-4571` TaskTargets 投影未命中 = `LogWarning` + 跳过，DueDateOverrides 由 M4 兜 Order 级存在性）。
- 具体：`SCOPE_REFERENCE_MISSING`（引用需求 Key 不在 `InScopeLogicalDemandKeys` / 解析失败）→ 2号位 投影侧 `LogWarning` + 丢弃该软目标，Run 继续；消费侧（1号位）无需因缺一条软目标拒收候选。
- 若 0号位 认需要 fail-closed（引用越界即拒收候选 Run），请 3号位 汇总为**正式协助项提报 0号位**；2号位 不擅自升级为拒收。

## 四、回执表

| # | 事项 | 2号位 状态 |
|---|---|---|
| 2 | S4 §1.2/§1.3 投影/Core 实体回执 | ✅ 已完成（源数据同步= S1 补丁前置，等 3号位 ODS 视图透列）|
| 3 | INITIAL 回刷执行 | ✅ 已执行（2 行→INITIAL_SETUP_STATE，INITIAL 清零）|
| 5 | M5 细则 ② 拒收策略 | ✅ 已回口径（告警+跳过；fail-closed 需 0号位 终裁）|

**发送人**：2号位 ｜ **日期**：2026-09-22