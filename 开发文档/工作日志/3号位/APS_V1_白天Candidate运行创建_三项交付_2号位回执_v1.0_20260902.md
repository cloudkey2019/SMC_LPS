# 白天候选排程运行 —— 三项交付 · 2号位回执

> **文档性质**：2号位 → 3号位 正式回执（对《APS_V1_白天Candidate运行创建_三项交付_提交2号位_v1.0_20260902.md》的逐项确认）
> **版本**：v1.0
> **日期**：2026-09-02
> **状态**：已回执，含一项**需 3号位 修正的 Status 词表冲突**（见 §三）

---

## 一、回执表（对交付文档 §五）

| 交付 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 一 方案 A 方法签名 | `RunSchedulingAndFinalizeAsync` + 建议实现 | ✅ **采纳并实现**（签名照单；已更新《APS_集成接口设计》§5.3.1 红线 #5） |
| 二 GovernanceAuditLog 表结构 | 9 列 + 2 索引参考 DDL | ✅ **已按红线 #6 出独立建表脚本**（见 §二） |
| 三 事项四基线快照 | 以 `BasePlanVersionId` 为只读数据源，防 ACTIVE 漂移 | ✅ **已补快照保护**（仅钉 Order，见 §四） |
| 四 状态词表配合 | 壳初始 `Created`、候选靠 `VersionCategory`、采用 `Status='ACTIVE'` | ✅ 确认，但 **3号位 代码仍是旧 BUILDING/CANDIDATE 口径，需改**（见 §三） |

---

## 二、事项五：GovernanceAuditLog 建表脚本（已出）

- 独立脚本：[GovernanceAuditLog_建表_Auth库_20260902.sql](./GovernanceAuditLog_建表_Auth库_20260902.sql)
- 目标库：**APS_Auth**（非 APS_Production），须在 Auth 库连接下执行
- 与 3号位 实体 + EF 配置逐列一致（`Id` BIGINT IDENTITY / `OperationType` `EntityType` NVARCHAR(50) / `VersionCode` NVARCHAR(100) / 其余 NVARCHAR(MAX) / `OperatedAt` DATETIME2 NOT NULL），索引 `IX_GovernanceAuditLog_Entity` + `IX_GovernanceAuditLog_OperatedAt`
- 幂等（`IF OBJECT_ID` + `sys.indexes` 保护），可重复执行

---

## 三、⚠️ 需 3号位 修正：Status 词表与 D7 裁决冲突

交付文档 §四白纸黑字「壳初始 `Status='Created'`（采纳）」，但 3号位 提交的代码仍是**旧 BUILDING/CANDIDATE 口径**，与 D7 裁决（`PlanVersion.Status` 走 2号位四值，候选身份靠 `VersionCategory='CANDIDATE'`）冲突。具体三处：

1. **[ScheduleRunRepository.cs:158](../../LPS.APS.Engine/Repositories/Governance/ScheduleRunRepository.cs#L158)** —— 建壳仍写 `Status='BUILDING'`，应按 D7 改为 `'Created'`。

2. **[RunLifecycleService.cs:275](../../LPS.APS.Application/Services/RunLifecycleService.cs#L275)** —— `EnsureCandidateConfirmable` 用 `version.Status != "CANDIDATE"` 做确认/激活门禁。D7 下 2号位 只会写 `Computing→Computed`（绝不写 `CANDIDATE`），此门禁会导致任何候选在确认/激活时抛「仅 CANDIDATE 可确认/激活」被拒。**应改为 `VersionCategory == "CANDIDATE"`（+ 状态 `Computed`）门禁。**

3. **[RunLifecycleService.cs:559](../../LPS.APS.Application/Services/RunLifecycleService.cs#L559)** —— `GetRunDomainStatusAsync` 用 `Status == "CANDIDATE"` 映射「域级 CANDIDATE 待确认」；D7 下候选身份在 `VersionCategory`，此映射应改按 `VersionCategory` 判断。

连带：`ConfirmCandidateAsync` / `ActivateCandidateAsync` 的审计 `BeforeStatus = "CANDIDATE"`（[RunLifecycleService.cs:181](../../LPS.APS.Application/Services/RunLifecycleService.cs#L181)、[:231](../../LPS.APS.Application/Services/RunLifecycleService.cs#L231)）也应按 D7 调整为真实 Status（`Computed`）或改记 `VersionCategory`。

> 2号位 按边界不越位改 3号位 代码；此三项请 3号位 修正后，C 线联调（交付文档 §六.5）才能走通「2号位 写 Computed → 3号位 确认/激活」。

---

## 四、事项四：基线快照落地口径（已实现）

- 主修复：候选壳无订单（3号位 建壳不复制订单），2号位 已把**订单数据源钉 `BasePlanVersionId`**——`RunSchedulingAndFinalizeAsync` 反查 `ScheduleRun.BasePlanVersionId`，`ExecuteDomainAsync` 的需求侧 `Order` 查询改从基线读；结果仍写回候选壳（`PlanVersionId` 不变）。
- **本轮只钉 Order**（经确认）：资源 / 库存 / pipeline 是全局表、不分 PlanVersion，不存在「随 ACTIVE 漂移」，FULL 与候选读同一份 live 数据，无需切。
- **已知次级缺口（记台账，不阻塞链路）**：
  - `OrderBomRequestLink`（[PeggingOrchestrator.cs:405](../../LPS.APS.Application/Services/PeggingOrchestrator.cs#L405)/471，`WHERE PlanVersionId`）候选下为空 → 候选 `RootOrderId` 落 0；
  - `StageProgressSnapshot`（`WHERE ScheduleRunId`）候选是全新 Run → MES 进度空。
  - 二者等真实候选数据 + 联调再定是否切基线 Run / 基线版本。

---

## 五、其余事项确认

| # | 事项 | 2号位 回执 |
|---|---|---|
| 4 | 事项三 Purpose 落库口径 | 维持「仅校验+审计」不落库；`ScopeJson` 不承载（冲突已认可）；如需落库走新增列（红线 #6）——**待 0号位/2号位 对齐** |
| 5 | C 线联调确认 | 待 3号位 完成 §三 Status 修正后启动；2号位 侧已保证 `ExecuteDomainAsync` 对候选壳只写 `Status=Computing/Computed/ComputeFailed`、不碰 `VersionCategory`，收口 `CompleteAsync/FailAsync` 就绪 |
| 6 | 状态词表配合 | ✅ 已回执（见 §一） |

---

## 六、2号位 本轮交付清单

1. 接口：`ISchedulingOrchestrator.RunSchedulingAndFinalizeAsync`（[ISchedulingOrchestrator.cs](../../LPS.APS.Core/Interfaces/ISchedulingOrchestrator.cs)）+ 实现（[SchedulingOrchestrator.cs](../../LPS.APS.Application/Services/SchedulingOrchestrator.cs)）
2. 基线快照：`ExecuteDomainAsync` 增 `sourcePlanVersionId`，订单钉 `BasePlanVersionId`
3. 建表：[GovernanceAuditLog_建表_Auth库_20260902.sql](./GovernanceAuditLog_建表_Auth库_20260902.sql)
4. 接口文档：`APS_集成接口设计` §0/§5.3.1 已更新（红线 #5）
