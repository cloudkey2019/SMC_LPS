# APS V1 Setup 规则承载重构——5号位 R-1 查询通道通知

**文档性质**：跨号位通知（5号位 收）——Setup 规则承载重构（独立物理表 → `RuleSetVersion.ContentSnapshotJson.SetupTransitionRules` 子块）对 5号位 查询侧的通道变更与 SetupSource 值契约同步。
**发送方**：3号位 ｜ **接收方**：5号位（业务规则/查询）｜ **日期**：2026-09-23
**依据**：契约登记件 `APS_V1_Setup规则承载契约登记件_3号位_v1.0_20260923.md`（§五 R-1）；1号位 P-1 回执澄清（SetupOutcome 5 值 ≠ SetupSource 4 态）；0号位 20260922 裁决
**抄送**：0号位（备查）

---

## 一、结论

**5号位 若现有查询依赖 `SetupTransitionRule` 物理表 → 该表已 DROP（2号位 D-1，2026-09-23），须改走治理查询通道；`GanttDataDto.SetupSource` 值契约已由旧 5 值收窄为 4 态，`GanttDataDto.cs:176` 注释待同步。** 需 5号位 **R-1 回执确认**。

---

## 二、SetupSource 值契约（4 态，0号位 20260922 裁决）

| 层 | 值域 | 说明 |
|---|---|---|
| **落库 `Task.SetupSource`** | **4 态**：`INITIAL_SETUP_STATE` / `EXACT` / `DEFAULT` / `SETUP_RULE_MISSING_ZERO_FALLBACK` | 0号位 20260922 裁决；旧 5 值 `SAME_PRODUCT` / `NONE` **不再是正式值** |
| **内部 `SetupOutcome` 枚举** | **5 值**（`ExactHit`/`DefaultHit`/`SameProductZero`/`InitialState`/`RuleMissing`） | Scheduling 内部诊断用，保留 5 值（区分路径），**不进查询通道** |

**归并规则**（`SetupOptimizer.SetupOutcomeToSource`，`SetupOptimizer.cs:68-76`，测试锚 `SetupTransitionRuleTests.cs:359-382`）：

| SetupOutcome | → SetupSource |
|---|---|
| `ExactHit` | `EXACT` |
| `DefaultHit` | `DEFAULT` |
| `SameProductZero`（同产品连续无显式规则 = 业务默认不换型） | `DEFAULT` |
| `RuleMissing`（有前后产品但 EXACT/DEFAULT 均缺失） | `SETUP_RULE_MISSING_ZERO_FALLBACK` |
| `InitialState`（无上一产品/班头首单） | `INITIAL_SETUP_STATE` |

**落库链路**（不变）：1号位 Solver 产出（`SetupOutcomeToSource`）→ 2号位 原样落库 `[Task].[SetupSource]`（`PeggingOrchestrator.cs:412-413`，2号位 不反推）→ 查询透传 `ScheduleQueryService.cs:149` → `GanttDataDto.SetupSource`。

---

## 三、Setup 规则查询通道变更（若 5号位 依赖旧表）

- **旧路径**：查询 `[dbo].[SetupTransitionRule]` 物理表（EXACT/DEFAULT 规则明细）。
- **现状**：物理表已由 2号位 D-1 **DROP**（开发库 `APS_Production`，含唯一索引/约束/seed 清理，2026-09-23）；全仓物理表 SQL 清零（仅剩注释）。
- **新承载**：规则存 `RuleSetVersion.ContentSnapshotJson.SetupTransitionRules` 子块（NVARCHAR(MAX) 单列，零 DDL），与 `DemandPriority` 子块同轨。
- **治理查询通道**：5号位 如需规则明细查询，走 `SetupRuleController`（`api/governance/setup-rules/*`，权限 `aps.setup.view`）——规则列表（EXACT/DEFAULT 按版本）、规则集版本列表、版本差异 `diff`、发布态；主数据下拉走 `lookups/*`（departments/resources/materials，**红线 #4：返回列表非单对象**）。
- **语义提示**：Setup 为**可选块**（fail-open）——子块缺失/为空/损坏 → 空规则列表 → **不阻断求解**，落 `SetupSource` 4 态之一；与 DemandPriority 等六块 fail-closed 不同。空规则最常见落 `SETUP_RULE_MISSING_ZERO_FALLBACK`（WARNING，进 4号位 数据质量查询），**≠ 静默 DEFAULT**。

---

## 四、5号位 待办（R-1 回执确认项）

| # | 项 | 说明 | 归属 |
|---|---|---|---|
| 1 | **GanttDataDto.cs:176 注释同步** | 现注释仍写旧 5 值（EXACT/DEFAULT/SAME_PRODUCT/NONE/INITIAL），与实际 4 态不符；建议改为「值契约：4 态 `INITIAL_SETUP_STATE`/`EXACT`/`DEFAULT`/`SETUP_RULE_MISSING_ZERO_FALLBACK`（0号位 20260922；内部 SetupOutcome 5 值经 SetupOutcomeToSource 归并）」 | 查询侧归 5号位 |
| 2 | **旧表查询依赖排查** | 若 5号位 代码/存储过程仍引用 `[dbo].[SetupTransitionRule]` → 改走 §三 治理查询通道 | 5号位 |
| 3 | **SetupSource 4 态消费** | 查询/展示侧按 4 态值域消费（甘特换型标签：EXACT/DEFAULT 正常；SETUP_RULE_MISSING_ZERO_FALLBACK 红 Tag 规则缺失；INITIAL_SETUP_STATE 班头首单普通展示）；不再出现 SAME_PRODUCT/NONE | 5号位 |

---

## 五、回执栏（5号位 填写）

| 项 | 确认 |
|---|---|
| SetupSource 4 态值契约收窄（删 SAME_PRODUCT/NONE） | ☐ 确认 |
| GanttDataDto.cs:176 注释待同步 4 态 | ☐ 确认（由 5号位 同步 / 或回执请 3号位 代改） |
| 无旧表 `[dbo].[SetupTransitionRule]` 查询依赖 | ☐ 确认（若有 → 列改道清单） |
| 备注 |  |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**待办**：5号位 R-1 回执 → 若涉及 SetupSource 查询/展示改动归 5号位 处置（涉及 5号位 事务亦可由 4号位 直通沟通）
