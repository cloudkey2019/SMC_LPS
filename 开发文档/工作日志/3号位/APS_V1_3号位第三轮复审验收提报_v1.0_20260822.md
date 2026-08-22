# APS V1 3号位 第三轮复审验收提报

> 版本：v1.0
> 日期：2026-08-22
> 提报人：3号位
> 复核对象：0号位（第三轮正式复审入口）
> 依据：《APS_V1_3号位代码第二轮复审核报告_Commit锁定版_v1.2_20260821》§9 第三轮复审冻结检查清单
> 对应提交：`d5edaf5`（D-8 ETA 不变量 + D-T 阶段 D 验收测试 R07~R13）、`3d04d7c`（B-5 快照缓存）、`de79f27`（P0-05~P0-08 集成测试补齐）等

---

## 一、提报背景

二轮复审报告 v1.2（2026-08-21）§8 给出三批整改顺序、§9 冻结 9 项第三轮复审检查清单（编号 11~19）。3号位已按顺序执行第二批（Candidate 生命周期红线）、第三批（验证与收口）并全部落地。本提报按 §9 清单逐项对照，**已满足项附实证，未关闭项标注阻塞原因与依赖**，作为第三轮正式复审的交付物。

> 说明：本轮不重复二轮报告已确认冻结保留的部分（PASS-01~06），仅对照第三轮冻结检查清单。

## 二、§9 冻结检查清单逐项对照（2026-08-22 实测）

| 清单项 | 内容 | 状态 | 证据 |
|---|---|---|---|
| **11** | 正式 DDL/字段说明与 RuleSetVersion/ParameterSetVersion 内容快照落点一致 | ❌ **阻塞（外部）** | 已提交《六块Snapshot持久化来源映射表_v1.0_20260819》§五 DDL 方案 A/B/C 供 0/2/3 号位一次性确认；**0号位尚未答复**（见《P0整改复核提报》§五.1） |
| **12** | FrozenStrategySnapshot 六块均可由指定 StrategyProfileVersion 重放具体值 | ❌ **阻塞（外部）** | 四块（DemandPriority/Lock/Supply/Procurement）已按 VersionId 重放 + 具体值断言；SolverStrategy/CandidateGuardrail 仍空对象，依赖 11 裁决后收口（P0-02） |
| **13** | 四类必填配置损坏/缺失均失败，Solver/Candidate 不再用空对象 | 🟡 部分 | 四块缺失/损坏一律抛异常（P0-04 ✅，`FrozenStrategySnapshotProviderTests` 全绿）；Solver/Candidate 空对象待 12 收口 |
| **14** | CTP / INSERT_IMPACT_ANALYSIS 无法通过任何路径激活 | ✅ **满足** | `Activate_来源Run为INSERT_ORDER_WHATIF_抛异常` / `Activate_SourceScheduleRunId为空_抛异常`；RunLifecycle 单测 27/27 全绿 |
| **15** | 未确认 Candidate 无法激活；确认不污染 Activated 字段 | ✅ **满足** | `Activate_未确认_抛异常` / `Confirm_合法_仅记审计不写Activated`（断言 ActivatedAt.BeNull、UpdateAsync Never） |
| **16** | Base ACTIVE 存在时仍可完成正常 Candidate 确认和原子采用 | ✅ **满足** | `Confirm_同域已有ACTIVE_仍可正常确认` / `Activate_同域已有ACTIVE_原子替换`（`IPlanVersionRepository.ReplaceActiveAsync` 单事务归档+置位） |
| **17** | ExpectedDomainKeys FULL 重复值被拒绝 | ✅ **满足** | `Validate_FullSchedule_重复DomainKey_抛异常`（P1-01） |
| **18** | R01~R22 测试及实际执行结果可提供 | ✅ **满足** | 《R01-R22验收证据映射表_v1.0》：18/22 全绿（R14~R17 阻塞于 12）；`dotnet test` 实测 **97 总 / 90 通过 / 6 跳过 / 1 失败** |
| **19** | 保持无 PriorityScore、无逐笔 RPC、无 3→5 决策接口、无 DSL/插件平台 | ✅ **满足** | 本轮未引入任何此类构造；接口仅按契约 6.11 演进（红线 #5） |

**汇总：9 项中 6 项 ✅ 满足、1 项 🟡 部分（13）、2 项 ❌ 阻塞（11/12）。已满足项不依赖任何 DDL 裁决，可先行复核。**

## 三、已满足项的实现落点（供 0号位 复核勾选）

### 14 ~ 16（Candidate 生命周期红线，第二批整改）

- **14 CTP 永不激活**：`RunLifecycleService.ActivateCandidateAsync` 链式前置：`EnsureCandidateConfirmable → EnsureConfirmedAsync（GovernanceAuditLog 存在 ConfirmCandidate 审计）→ EnsureSourceRunActivatableAsync（SourceScheduleRunId 非空 + RunType != "INSERT_ORDER_WHATIF"）`。无任何路径可绕过（无 ScopeJson/Purpose 分支遗漏）。
- **15 确认硬前置 + 不污染 Activated**：`ConfirmCandidateAsync` 仅写审计（OperationType=ConfirmCandidate），不再写 ActivatedAt/ActivatedBy、不再 UpdateAsync；`ActivateCandidateAsync` 强制校验确认审计存在，无确认事实直接抛异常。
- **16 原子替换**：`IPlanVersionRepository.ReplaceActiveAsync(candidate, actor, activatedAt)` 单事务内：同 DomainKey 既有 ACTIVE → ARCHIVED（+ArchivedAt），candidate → ACTIVE（+ActivatedAt/ActivatedBy）。UQ_PlanVersion_OneActivePerDomain 由事务原子性兜底，非"先删后插"窗口。

### 17（FULL 重复 Domain）

- `ValidateDomainKeys` FULL 分支 `domains.Distinct().Count() != domains.Count → 抛异常`。

### 18（R01~R22 证据）

- 映射表 §二 逐项映射；测试于 2026-08-22 仓库内实际执行（非仅源码审查）。
- 6 跳过 = 环境缺口（APS_Auth 库 / ExpectedDomainKeysJson 列，2号位协同后自动转绿）；1 失败 = 2号位 `FiniteCapacitySolver` DI（`SchedulingOrchestrator.cs:41` 依赖具体类），已转 2号位（《P0整改复核提报》§六.3）。

### 19（无过度设计）

- 全程无 PriorityScore（Validator 白名单禁止）、无逐笔 RPC、无 3→5 决策接口、无 DSL/脚本/插件平台。

## 四、未关闭项（11~13）阻塞原因与依赖

| 项 | 阻塞原因 | 依赖 | 裁决后 3号位 立即动作 |
|---|---|---|---|
| 11 | 冻结 DDL 无版本内容快照字段，3号位红线 #6 不得自行 ALTER | 0号位 裁决 DDL 方案 A/B/C（映射表 §五/§六） | 按裁决同步修订实体/Repository/契约文档 6.11，保证按 StrategyProfileVersionId 完整恢复 Run 内容 |
| 12 | SolverStrategy/CandidateGuardrail 无真实版本来源（P0-02） | 11 裁决后建立来源 | Provider 装载两块真实 JSON（缺失/损坏一律失败），删除空对象正式路径；补具体值重放断言（非 NotNull） |
| 13 | 两块未纳入失败语义（同 12 根因） | 12 | 随 12 一并落地 |

> 优先级建议：0号位 优先答复映射表 §六.1（方案 A/B/C），3号位 裁决后立即收口 11~13，第三轮复审即全部闭环。

## 五、测试验证汇总（2026-08-22 实测）

```
dotnet build LPS.APS.sln   → 0 错误
dotnet test LPS.APS.Tests  → 总计 97 / 通过 90 / 跳过 6 / 失败 1
```

- **6 跳过** = 治理链路集成测试 `[SkippableFact]`（APS_Auth 库 + ExpectedDomainKeysJson 列环境缺口，2号位补齐后自动转绿，无需改测试）
- **1 失败** = 2号位 `FiniteCapacitySolver` DI（既有，非本轮范围）
- 本轮（#3~#10）整改未引入任何新失败/跳过；RunLifecycle 单测 27/27 全绿（含 14~16 的 8 项红线断言）

### 阶段 E 推进记录（E-4 已完成，2026-08-22）

> 阶段 E（Solver Strategy）中不依赖 0号位 DDL 裁决的任务 E-4（Solver 发布前校验）已独立闭环：

- **新增** `SolverStrategyValidator.cs` + `CandidateGuardrailValidator.cs`（Application/Services，纯内容校验）：
  - SolverStrategyBlock：OnTimeTarget.TargetPercent 0~100、Split.MaxOptimizationSplitCount/MinBatchQty 非负、Setup.DefaultSetupMinutes 为正/LookAhead 非负、StageOverlap.Transfer/Threshold 非负且 ThresholdPercent 0~100、SolverStrategyMode 枚举合法（防御数字越界反序列化）
  - CandidateGuardrailBlock：NormalMs/SoftMs/LocalHardMs 为正 + **时序 NormalMs≤SoftMs≤LocalHardMs（60/90/180）**、ImpactedTaskWarningPercent 0~100、Repair/Propagation/ResourceTopN/SplitAlternatives 非负
- **新增测试** `SolverStrategyValidatorTests.cs`：15 用例全绿（默认通过/合法配置/各越界拒绝/时序倒挂拒绝/Mode 越界拒绝）
- **验证**：`dotnet build` 0 错误；`dotnet test`（新增过滤）15/15 通过
- **不依赖 DDL**：校验对象为内存 DTO，测试不碰库；**裁决后仅需接线**——`GovernanceVersionService.ValidateParameterSetVersionForPublishAsync` 反序列化 `SolverStrategyJson`/`CandidateGuardrailJson` → 调校验器 → 错误进 `result.Errors`（详见《P0-02执行蓝图》§四.5）
- 阶段 E 剩余（E-1~E-3 真实版本来源重放 + E-T 门 R14~R17）仍阻塞于 0号位 DDL 方案 A/B/C 裁决

## 六、跨号位边界遵守声明

- ✅ 未修改 2号位运行状态执行逻辑：`ScheduleRunService` / `SchedulingOrchestrator` / `DomainSchedulingJob` / `DomainLayerCoordinatorJob`
- ✅ 未 ALTER 冻结 DDL（v5.1.2）：候选落点复用既有 ActivatedAt/ActivatedBy/ArchivedAt；每域唯一由既有 UQ 兜底
- ✅ 红线 #5：接口新增/变更前均已先追加契约文档 6.10 / 6.11 / 6.11.5
- ✅ 红线 #4：不盲目 `.First()`；同域 ACTIVE 查询"无则 null"语义明确
- ✅ 红线 #6：集成测试环境探测为只读，不碰库结构

## 七、复核清单（供 0号位 / 第三轮复审逐项勾选）

- [ ] 14 CTP/INSERT_IMPACT_ANALYSIS 无任何路径可激活
- [ ] 15 未确认 Candidate 无法激活；确认不污染 Activated 字段
- [ ] 16 Base ACTIVE 存在时 Candidate 确认与原子采用正常
- [ ] 17 ExpectedDomainKeys FULL 重复 Domain 被拒绝
- [ ] 18 R01~R22 证据与 `dotnet test` 实测结果（97/90/6/1）
- [ ] 19 无 PriorityScore / 逐笔 RPC / 3→5 决策接口 / DSL 平台
- [ ] 11 DDL 方案 A/B/C 裁决（映射表 §六.1）——**阻塞项，待 0号位**
- [ ] 12/13 Solver/Candidate 真实来源重放——**裁决后立即收口**

---

## 附：相对二轮锁定 Commit（f62e141）的 3号位 变更清单

| 文件 | 变更 |
|---|---|
| `LPS.APS.Application\Services\RunLifecycleService.cs` | P0-03/04/05/06 + P1-01 主整改（14~17） |
| `LPS.APS.Core\Interfaces\IPlanVersionRepository.cs` | 移除 `GetActiveByDomainKeyAsync`、新增 `ReplaceActiveAsync`（16） |
| `LPS.APS.Engine\Repositories\Governance\PlanVersionRepository.cs` | 实现单事务原子替换 `ReplaceActiveAsync`（16） |
| `LPS.APS.Tests\Unit\RunLifecycleServiceTests.cs` | 改写 4 错误测试 + 新增 5 测试（27 项全绿） |
| `LPS.APS.Tests\Integration\RunLifecycleServiceIntegrationTests.cs` | 确认/激活断言对齐（15/16） |
| `开发文档\契约文档\05_3号位和1号位对外契约.md` | §6.11 契约收紧（红线 #5，14~17） |
| `README.md` | P1-03 最小去旧化 |
| `开发文档\工作日志\3号位\APS_V1_3号位R01-R22验收证据映射表_v1.0_20260822.md` | R01~R22 逐项映射 + 实测证据（18） |
| `LPS.APS.Application\Services\SolverStrategyValidator.cs` / `CandidateGuardrailValidator.cs` | **E-4** Solver/Guardrail 发布前校验器（纯内容校验，不依赖 DDL） |
| `LPS.APS.Tests\Unit\SolverStrategyValidatorTests.cs` | **E-4** 15 用例全绿 |
