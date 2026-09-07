# APS V1 白天 Candidate 排程运行创建（B-1）契约草案 v1.0

> 文档编号：LPS-3-20260902-B1
> 作者：3号位（应用编排 / 运行生命周期治理）
> 日期：2026-09-02
> 状态：**草案（创建侧已实现；触发契约与 P1/P2/P3 契约点待 2号位 / 0号位 回执裁定）**

---

## 〇、一句话交付说明

3号位 为 4号位「创建/发起白天候选运行」提供入口：**创建一个白天候选 ScheduleRun（RUNNING，冻结运行类型 × 用途 × 策略包版本 × 预期 Domain）+ 一个 Candidate PlanVersion 壳（BUILDING）**，随创建写入 `CreateCandidateRun` 审计；随后按 2↔3 触发契约（见 §四）交 2号位 主流程执行并收口 Run。本轮实现仅创建侧，触发处留契约接缝不接线。

---

## 一、冻结依据（来源文档）

| 来源 | 要点 |
|---|---|
| 0号位 2026-08-29 裁决3（《0号位裁决_跨号位接口归属答复》） | MANUAL_RESCHEDULE 的白天候选 ScheduleRun **创建归 3号位 运行治理侧**；创建时冻结 运行类型/用途/策略版本，交 2号位 主流程执行收口 |
| 《APS_集成接口设计 v1.28》§5.3 / §6.5 | 3号位 负责 白天 Candidate Run 创建、RunType·Purpose 合法性；白天运行发起 RunType 组合 |
| 《APS_核心排产全流程走查 V3.18》§41（白天 Candidate 原则） | Base ACTIVE 只读；新建 ScheduleRun；新建 Candidate PlanVersion；Candidate 严格单 Domain；不修改 Base |
| 《3号位 规则参数认证权限与运行生命周期开发实施包 v1.2》§十九 | RunType×Purpose 冻结合法组合（见 §三.3） |
| 《APS_数据库表结构设计 v5.1.4》§3.1/§3.2 | ScheduleRun（BasePlanVersionId / ExpectedDomainKeysJson 冻结 / ScopeJson）；PlanVersion（DomainKey V1 必填 / SourceScheduleRunId / UQ_PlanVersion_OneActivePerDomain / UQ_PlanVersion_ScheduleRun_Domain） |

---

## 二、端点契约（创建侧）

### 2.1 端点

```
POST api/Governance/run/candidate
```

### 2.2 请求体 `CreateCandidateRunRequest`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `RunType` | string | ✅ | 白天候选类：`MANUAL_RESCHEDULE` / `LOCAL_RESCHEDULE` / `INSERT_ORDER_WHATIF` |
| `Purpose` | string | ✅ | 用途，须 ∈ RunType 冻结合法组合（§三.3） |
| `DomainKey` | string | ✅ | 目标 Domain（白天候选严格单 Domain） |
| `BasePlanVersionId` | int? | ❌ | 所基于的当前 ACTIVE 计划版本；缺省由服务按 DomainKey 解析当前 ACTIVE |
| `DataCutoffTime` | datetime? | ❌ | 本次运行统一数据切片边界；缺省 now |
| `Actor` | string | ✅ | 操作人（写 TriggeredBy / 壳 CreatedBy / 审计 OperatedBy） |
| `Remark` | string? | ❌ | 备注（契约位保留，B-1 本轮不参与业务逻辑） |

### 2.3 响应

成功 `200`：

```json
{
  "success": true,
  "data": {
    "NewScheduleRunId": 101,
    "NewPlanVersionId": 202
  },
  "message": "白天候选运行创建成功：RunId=101, CandidatePlanVersionId=202"
}
```

失败 `400/404`：`{ "success": false, "error": "<中文原因>" }`（沿用 GovernanceController 现有 `InvalidOperationException` 捕获模式，不静默降级）。

### 2.4 创建产物（单事务原子写，任一失败整体回滚）

**ScheduleRun（新，RUNNING）**：`RunType` / `Status='RUNNING'` / `TriggeredBy=Actor` / `DataCutoffTime` / `BasePlanVersionId` / `StrategyProfileVersionId`（冻结的默认策略版本）/ `ExpectedDomainKeysJson=['DomainKey']`（冻结）/ `StartedAt` / `CreatedAt`。

**PlanVersion（新 Candidate 壳，BUILDING）**：`VersionCode=CANDIDATE_{DomainKey}_{yyyyMMddHHmmss}` / `VersionCategory='CANDIDATE'` / `DomainKey` / `PlanHorizonStart~End`（复制 Base，缺省今天~+90 天）/ `ComputeMode='FULL'`（V1 统一 Solver）/ `Status='BUILDING'` / `SourceScheduleRunId=新 Run Id` / `CreatedBy=Actor`。

**审计（GovernanceAuditLog）**：`OperationType='CreateCandidateRun'` / `EntityType='ScheduleRun'` / `EntityId=新 Run Id` / `Before='-'` / `After='RUNNING'` / `OperatedBy=Actor` / `Remarks` 含 RunType·Purpose·Domain·BasePlanVersionId·StrategyProfileVersionId·NewPlanVersionId。

> **P1 注**：`Purpose` 本轮**仅校验 + 审计**，不落 ScheduleRun 表（DDL 无 Purpose 列，见 §五）。

---

## 三、校验规则（创建侧已实现）

按顺序校验，任一失败抛 `InvalidOperationException`（不静默降级）：

1. **Actor 非空**；
2. **RunType ∈ 白天候选类** `{MANUAL_RESCHEDULE, LOCAL_RESCHEDULE, INSERT_ORDER_WHATIF}`（`FULL_SCHEDULE`/`SIMULATION` 拒绝）；
3. **Purpose × RunType 冻结合法组合**（实施包 §十九）：

   | RunType | 合法 Purpose |
   |---|---|
   | `INSERT_ORDER_WHATIF` | `CTP`、`INSERT_IMPACT_ANALYSIS`（二者永远不得激活） |
   | `LOCAL_RESCHEDULE` | `INSERT_RESCHEDULE`、`MANUAL_ADJUSTMENT` |
   | `MANUAL_RESCHEDULE` | `MANUAL_ADJUSTMENT` |

4. **DomainKey 非空**（单 Domain 语义）；
5. **Base ACTIVE 校验**：传 `BasePlanVersionId` → 校验存在 + Status=`ACTIVE` + DomainKey 一致；不传 → 按 DomainKey 解析当前 ACTIVE，无 ACTIVE 则拒绝（白天 Candidate 必须基于 Base ACTIVE 只读锚定）；
6. **默认策略版本解析**：`IStrategyProfileVersionRepository.GetDefaultByRunTypeAsync(RunType)`（IsDefault=1 + PUBLISHED + 父 Profile RunType/IsActive=1）→ Application 层按 EffectiveFrom/To 生效窗口过滤 → **0 缺失拒绝 / 多歧义拒绝**（红线 #4：不盲目取第一个）；
7. **DataCutoffTime 缺省 now**；
8. **单事务原子写** Run + 壳（§2.4）→ 写审计。

---

## 四、2↔3 触发契约（待 2号位 / 0号位 裁定）

创建侧只负责「建 Run + 壳」，**不调用 2号位 主流程**。触发方案 A/B/C 三选一：

### 方案 A（推荐）：扩展 ISchedulingOrchestrator 显式触发

- 2号位 在 `ISchedulingOrchestrator` 暴露 `RunSchedulingAsync(planVersionId, scheduleRunId, strategyProfileVersionId)`：按传入 Run Id 同步执行计算并**收口该 Run**（回填 PlanVersion 结果 + ScheduleRun 终态 + CompletedAt）。
- 3号位 创建成功后调用该契约执行。
- **红线 #5 前置**：2号位 须先更新《APS_集成接口设计》接口契约文档，再改接口签名。

### 方案 B：on-demand 触发 job（Hangfire Enqueue）

- 2号位 提供「按 Run Id 触发」的 on-demand job（如 `DomainSchedulingJob.Enqueue(runId)`），3号位 创建成功后投递。

### 方案 C：复用 RunSchedulingAutoAsync 领取

- 2号位 `RunSchedulingAutoAsync` 当前领取条件为 `Status='Created' AND SourceScheduleRunId>0` 的壳，与 B-1 壳初始 Status=`BUILDING` **冲突**（见 P2）；若走 C 需裁定壳状态改为 `Created`，或扩展领取条件。

> **当前状态**：未接线。期间新建 RUNNING Run 为临时态（已冻结基线，未被计算执行）；裁定后由 3号位 补接线。

---

## 五、契约点 P1：Purpose 落库

- **现状**：DDL v5.1.4 ScheduleRun **无 Purpose 列**；Core 全仓无 Purpose 概念（本轮在 Core 新增 `StrategyProfilePurpose` 常量 + 校验映射 + 审计携带）。
- **选项**：
  - A）新增 `ScheduleRun.Purpose` 列（DB 变更 2号位 专属，红线 #6）——最直接，追溯/展示友好；
  - B）复用 `ScopeJson` 承载 `{"purpose": "..."}`——无 DDL 变更，但语义耦合；
  - C）仅校验 + 审计（本轮采用）——不落库，靠审计追溯。
- **建议**：选项 A（待 2号位 / 0号位 裁定；裁定后 2号位 落 DDL，3号位 补写入）。

---

## 六、契约点 P2：Candidate 壳初始 Status

- **B-1 取值**：`BUILDING`（DDL 值域 BUILDING/CANDIDATE/ACTIVE/ARCHIVED/FAILED 的一部分；G8 已映射 `BUILDING → RUNNING` 域级状态，语义自洽）。
- **2号位 领取冲突**：`RunSchedulingAutoAsync` 按 `Status='Created'` 领取（夜间批次壳初始 `Created`）；B-1 白天壳若走方案 C 需统一。
- **建议**：走方案 A/B（3号位 显式触发），2号位 主流程不依赖壳初始状态领取，`BUILDING` 语义保持。

---

## 七、契约点 P3：BasePlanVersionId 语义

- DDL 注释（§3.1）：*"白天 Candidate 所基于的当前 ACTIVE 版本；由创建服务校验版本存在、状态和 Domain 一致性"*。
- B-1 创建侧已实现该校验（§三.5）。
- **待 2号位 确认**：执行期是否需要对 `BasePlanVersionId` 指向的 ACTIVE 版本做快照/引用保护（防执行期间被替换），若需要，冻结机制与实现归属（2号位）另立子项。

---

## 八、当前实现范围与越位红线

| 项目 | 状态 |
|---|---|
| 契约草案文档 | ✅ 本文件 |
| Core：`StrategyProfilePurpose` 常量 | ✅ |
| Core：`CandidateRunCreateSpec` / `CandidateRunCreatedResult` DTO | ✅ |
| Core：`ScheduleRunGov` 补 `BasePlanVersionId` / `ScopeJson` | ✅ |
| Core：`IScheduleRunRepository.CreateCandidateRunAsync` / `IPlanVersionRepository.GetActiveByDomainKeyAsync` / `IRunLifecycleService.CreateCandidateRunAsync` | ✅ |
| Engine：`ScheduleRunRepository` 单事务创建 / `PlanVersionRepository.GetActiveByDomainKeyAsync` | ✅ |
| Application：`RunLifecycleService.CreateCandidateRunAsync`（校验+审计+接缝注释） | ✅ |
| Web：`POST api/Governance/run/candidate` | ✅ |
| 单元测试（合法/非法矩阵 + Base/策略版本/原子性） | ✅ |
| 集成测试（真实库，GovernanceAuditLog 表未迁移则 Skip） | ✅ |
| **2↔3 触发接线（A/B/C）** | ⏳ **契约待裁定，未接线** |
| Purpose 落库（P1） | ⏳ **待裁定** |
| BasePlanVersionId 执行期快照保护（P3） | ⏳ **待确认** |

**越位红线**：不调 2号位 主流程；不建 DDL 列；不重写 2号位 运行状态执行；接口变更以本契约草案为前置（红线 #5）。

---

## 九、回执要求

请 **2号位** 回复：
1. 触发契约选 **A / B / C**（建议 A）；
2. `BasePlanVersionId` 执行期是否需要快照保护（P3）。

请 **0号位** 裁定：
1. Purpose 落库选项（P1，建议 A）；
2. 壳初始 Status 取值（P2，建议 BUILDING + 方案 A/B，不依赖 `Created` 领取）。
