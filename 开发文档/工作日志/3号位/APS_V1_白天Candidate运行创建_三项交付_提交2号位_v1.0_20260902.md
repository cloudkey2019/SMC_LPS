# 白天候选排程运行 —— 三项交付（提交 2号位）

> **文档性质**：3号位 → 2号位 正式交付（跨号位协作，2号位 动 C 线代码的前置输入）
> **版本**：v1.0
> **日期**：2026-09-02
> **状态**：已交付，待 2号位 回执确认
> **关联文档**：
> - `APS_V1_白天Candidate运行创建_提交2号位需求说明_v1.0_20260902.md`（v1.2，本交付为其 §7 的独立整理）
> - `LPS.APS.Core/Interfaces/ISchedulingOrchestrator.cs`、`LPS.APS.Application/Services/SchedulingOrchestrator.cs`、`LPS.APS.Engine/Services/Sync/ScheduleRunService.cs`
> - `LPS.APS.Core/Entities/Auth/GovernanceAuditLog.cs`、`LPS.APS.Engine/Data/AuthDbContext.cs`

---

## 背景

2号位 2026-09-02 回复确认：收到**方案 A 方法签名、事项五 GovernanceAuditLog 表结构、事项四基线快照**后动 C 线代码。本文件为三项交付。

> 另：2号位 提到的"最要紧"项（标一个 `IsDefault=1` 的 FULL_SCHEDULE 策略包版本）**已由 3号位 于 2026-09-02 落地**——执行 20260901 策略包种子，`SP_DEMO / SP-DEMO-V2.0 / PUBLISHED / IsDefault=1` 已唯一生效（实测确认），夜间排程不再静默无策略。

---

## 一、方案 A 方法签名（红线 #5：2号位 先更新《APS_集成接口设计》再实现）

现有 `ISchedulingOrchestrator`（`LPS.APS.Core/Interfaces/ISchedulingOrchestrator.cs`）：

- `RunSchedulingAsync(int planVersionId, ct)`：调 `ExecuteDomainAsync(…, scheduleRunId:0, …)`，**不收口 Run**（SchedulingOrchestrator.cs:198-199）；
- `RunSchedulingAutoAsync(ct)`：自动领取 + 统一收口（夜间路径，不动）。

**方案 A 提案**——`ISchedulingOrchestrator` 新增方法（白天候选专用，独立于夜间自动路径）：

```csharp
/// 单域执行并收口 Run（白天候选：建 Run+壳后由 3号位 调用）
/// 语义：RUNNING → COMPLETED（IsSuccess=true）/ FAILED（IsSuccess=false，ErrorMessage 落 Run）
/// 红线 #5：本签名先同步更新《APS_集成接口设计》，2号位 确认后实现
Task<SchedulingRunResult> RunSchedulingAndFinalizeAsync(
    int planVersionId,
    int scheduleRunId,
    long strategyProfileVersionId,
    CancellationToken cancellationToken = default);
```

**建议实现**：`ExecuteDomainAsync(planVersionId, scheduleRunId, dataCutoffTime: null, strategyProfileVersionId)` → 按返回 `IsSuccess` 调 `ScheduleRunService.CompleteAsync / FailAsync`（Engine 已有，`LPS.APS.Engine/Services/Sync/ScheduleRunService.cs:114-158`）。

**3号位 侧**：创建动作末尾注释标记的调用点在本签名落定后接线（不越位调用 2号位 内部方法，只调接口）。

---

## 二、事项五：GovernanceAuditLog 表结构（已由 3号位 建表，用户授权）

表结构源自 3号位 实体 `LPS.APS.Core/Entities/Auth/GovernanceAuditLog.cs` + EF 配置 `AuthDbContext.cs:131-141`（**Auth 库**，EF Core 使用）。

> **已建表（2026-09-02，3号位）**：用户授权 3号位 自行创建该表。`APS_Auth.dbo.GovernanceAuditLog` 已创建，实测 9 列 + 2 索引与下方 DDL 一致，与 EF 实体映射完全对齐。**原"待 2号位 红线 #6 建表"项已关闭**（用户授权例外）。

### 已建表 DDL（与 EF 实体映射一致）

```sql
-- APS_Auth.dbo.GovernanceAuditLog（3号位 A-7 治理审计；红线 #6 由 2号位 独立建表）
CREATE TABLE dbo.GovernanceAuditLog (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    OperationType NVARCHAR(50)   NOT NULL,   -- Publish / Disable / Archive / CreateCandidateRun / …
    EntityType    NVARCHAR(50)   NOT NULL,   -- RuleSetVersion / ParameterSetVersion / StrategyProfileVersion / ScheduleRun / …
    EntityId      BIGINT         NOT NULL,   -- 实体 Id
    VersionCode   NVARCHAR(100)  NULL,       -- 实体版本号
    BeforeStatus  NVARCHAR(MAX)  NULL,       -- 操作前状态
    AfterStatus   NVARCHAR(MAX)  NULL,       -- 操作后状态
    OperatedBy    NVARCHAR(MAX)  NULL,       -- 操作人
    OperatedAt    DATETIME2      NOT NULL,   -- 操作时间
    Remarks       NVARCHAR(MAX)  NULL        -- 备注（原因、审批单号等）
);
CREATE INDEX IX_GovernanceAuditLog_Entity      ON dbo.GovernanceAuditLog(EntityType, EntityId);
CREATE INDEX IX_GovernanceAuditLog_OperatedAt  ON dbo.GovernanceAuditLog(OperatedAt);
```

**集成测试影响**：表已建，`RunLifecycleServiceIntegrationTests` / `GovernanceVersionServiceIntegrationTests` 的 GovernanceAuditLog skip 项应转绿（待跑测试确认），无需改测试代码。

---

## 三、事项四：执行侧基线快照保护（3号位 建议）

- **语义**：白天候选以**创建时冻结的 `ScheduleRun.BasePlanVersionId`** 为只读基线；执行时**不许随执行时刻"当前 ACTIVE"漂移**。
- **现状**：`ExecuteDomainAsync` 只读自身 PlanVersion（SchedulingOrchestrator.cs:227-230），无基线概念；沙盘装载 `LoadSchedulingContextAsync` 读本版本数据。
- **建议**：方案 A 入口内按 `scheduleRunId` 反查 `BasePlanVersionId`；候选场景下沙盘数据源（Order/资源/库存）切到基线版本（**只读，不改写基线**），候选壳仅承载结果。基线版本本身不得被修改（只读红线）。
- 集成测试在 2号位 落地后由 3号位 补基线快照断言用例。

---

## 四、状态词表配合（2号位 已写进回执，3号位 确认采纳）

- `PlanVersion.Status` 候选走执行词表 `Created/Computing/Computed/ComputeFailed`，**不写** BUILDING/CANDIDATE/FAILED；
- 候选靠 `VersionCategory='CANDIDATE'` 标识（一致 ✅）；
- 壳初始 `Status='Created'`（采纳，台账更干净）；
- 采用生效 `Status='ACTIVE'`（v5.1.4 已有 `UQ_PlanVersion_OneActivePerDomain` 筛选唯一索引，L3861；治理动作，3号位 治理侧负责，2号位 主流程不管）。

---

## 五、2号位 回执表

| 交付 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 一 方案 A 方法签名 | `RunSchedulingAndFinalizeAsync` 签名 + 建议实现（红线 #5 先更集成接口文档） | □ 采纳并实现　□ 需调整 |
| 二 GovernanceAuditLog 表结构 | 9 列 + 2 索引参考 DDL（Auth 库，EF 映射一致） | **已由 3号位 建表（用户授权，2026-09-02）** |
| 三 事项四基线快照 | 以创建时冻结 `BasePlanVersionId` 为只读数据源，防 ACTIVE 漂移 | □ 已补快照保护　□ 需调整 |
| 四 状态词表配合 | 壳初始 `Created`、候选靠 `VersionCategory='CANDIDATE'`、采用 `Status='ACTIVE'` | □ 确认　□ 需调整 |

---

## 六、需要 2号位 确认与协助的完整事项清单

将散落在《提交2号位需求说明》v1.2 各处、依赖 2号位 的事项收拢于此，2号位 按本清单逐项确认即可：

| # | 事项 | 类别 | 说明 | 当前状态 |
|---|---|---|---|---|
| 1 | **方案 A 方法签名** `RunSchedulingAndFinalizeAsync` | 确认 + 实现 | 见 §一；红线 #5 先更新《APS_集成接口设计》；3号位 随后在创建动作接线 | 待 2号位 |
| 2 | **GovernanceAuditLog 建表** | 已完成 | 见 §二 DDL；**已由 3号位 2026-09-02 建表（用户授权）**，9 列 + 2 索引实测就位，EF 映射一致 | ✅ 已建表 |
| 3 | **事项四基线快照保护** | 协助 | 见 §三；候选沙盘数据源切 `BasePlanVersionId`（只读，防 ACTIVE 漂移） | 待 2号位 |
| 4 | **事项三 Purpose 落库口径** | 确认（协同 0号位） | 维持"仅校验+审计"不落库；`ScopeJson` 冻结不承载（2号位 已认可冲突）；如需落库走新增列（红线 #6） | 待 0号位/2号位 对齐 |
| 5 | **C 线联调确认** | 确认 | 3号位 创建壳（`Status='Created'`、`VersionCategory='CANDIDATE'`）并接线调用后，2号位 确认 `ExecuteDomainAsync` 对 CANDIDATE 壳写 `Computed` 不破坏 `VersionCategory`、Run 收口（COMPLETED/FAILED）正常 | 待联调 |
| 6 | **状态词表配合** | 已确认 | 见 §四；壳初始 `Created`、候选靠 `VersionCategory` 标识、采用 `Status='ACTIVE'` | ✅ 2号位 已回执 |

> **注（供知悉，无需 2号位 动作）**：夜间 auto 领取已含 `AND sr.RunType='FULL_SCHEDULE'` 防御（SchedulingOrchestrator.cs:63，注释 L58"白天候选不得被发令枪误领"）——出入① 的防御已落地，白天候选 Run 不会被夜间发令枪抢走。
