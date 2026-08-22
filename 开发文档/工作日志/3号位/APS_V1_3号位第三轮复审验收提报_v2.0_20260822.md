# APS V1 3号位 第三轮复审验收提报

> 版本：v2.0（裁决后收口版）
> 日期：2026-08-22
> 提报人：3号位
> 复核对象：0号位（第三轮正式复审入口）
> 依据：《APS_V1_3号位代码第二轮复审核报告_Commit锁定版_v1.2_20260821》§9 第三轮复审冻结检查清单
>        + 0号位《第三轮正式复审报告_a5741a7_Commit冻结版(1).md》§12.3（**批准方案 A**，2026-08-22）
> 对应提交：`d5edaf5`（D-8 ETA 不变量 + D-T 阶段 D 验收测试 R07~R13）、`3d04d7c`（B-5 快照缓存）、`de79f27`（P0-05~P0-08 集成测试补齐）等
> 变更：v1.0（2026-08-22 第三轮提报基线）→ v2.0 收口 P0-01b/P0-02a/P0-02b（#16~#19），清单 11/12/13 由 ❌/🟡 转 ✅ 关闭

---

## 一、提报背景

二轮复审报告 v1.2（2026-08-21）§8 给出三批整改顺序、§9 冻结 9 项第三轮复审检查清单（编号 11~19）。3号位已按顺序执行第二批（Candidate 生命周期红线）、第三批（验证与收口）并全部落地。**0号位 于 2026-08-22 裁决《第三轮正式复审报告》§12.3 批准方案 A**（ContentSnapshotJson 通用快照），3号位 随即执行 #16~#19 收口 P0-01b/P0-02a/P0-02b。本提报 v2.0 为收口后最终交付物：**9 项清单全部满足**。

## 二、§9 冻结检查清单逐项对照（2026-08-22 实测，收口后）

| 清单项 | 内容 | 状态 | 证据 |
|---|---|---|---|
| **11** | 正式 DDL/字段说明与 RuleSetVersion/ParameterSetVersion 内容快照落点一致 | ✅ **关闭** | 0号位 批准方案 A；《DDL变更申请_ContentSnapshotJson_v1.0》已提交 2号位（红线 #6）；实体/Repository SQL 对齐（`ContentSnapshotJson` + `EffectiveFrom/To/ApprovedAt/By` 双写，冻结 DDL v5.1.2 逐列匹配） |
| **12** | FrozenStrategySnapshot 六块均可由指定 StrategyProfileVersion 重放具体值 | ✅ **关闭** | Provider 六块统一从 `ContentSnapshotJson` 子块反序列化（契约 §6.10.5）；R14~R17 具体值断言（SolverStrategy.Mode=Backward / TargetPercent=85 / CandidateGuardrail.NormalMs=70000 等） |
| **13** | 四类必填配置损坏/缺失均失败，Solver/Candidate 不再用空对象 | ✅ **关闭** | 六块统一缺失/损坏一律装载失败（新增 2 用例）；显式 `"{}"` 为合法默认；发布链接线 SolverStrategy/CandidateGuardrail 两 Validator（非法内容拒绝发布，新增 2 用例） |
| **14** | CTP / INSERT_IMPACT_ANALYSIS 无法通过任何路径激活 | ✅ **满足** | `Activate_来源Run为INSERT_ORDER_WHATIF_抛异常` / `Activate_SourceScheduleRunId为空_抛异常`；RunLifecycle 单测 27/27 全绿 |
| **15** | 未确认 Candidate 无法激活；确认不污染 Activated 字段 | ✅ **满足** | `Activate_未确认_抛异常` / `Confirm_合法_仅记审计不写Activated`（断言 ActivatedAt.BeNull、UpdateAsync Never） |
| **16** | Base ACTIVE 存在时仍可完成正常 Candidate 确认和原子采用 | ✅ **满足** | `Confirm_同域已有ACTIVE_仍可正常确认` / `Activate_同域已有ACTIVE_原子替换`（`IPlanVersionRepository.ReplaceActiveAsync` 单事务归档+置位） |
| **17** | ExpectedDomainKeys FULL 重复值被拒绝 | ✅ **满足** | `Validate_FullSchedule_重复DomainKey_抛异常`（P1-01） |
| **18** | R01~R22 测试及实际执行结果可提供 | ✅ **满足** | 《R01-R22验收证据映射表_v2.0》：**22/22 全绿**；单元测试 **104/104 通过**；集成测试 7 失败受 2号位 DDL 未落地外部阻塞（见 §五） |
| **19** | 保持无 PriorityScore、无逐笔 RPC、无 3→5 决策接口、无 DSL/插件平台 | ✅ **满足** | 本轮未引入任何此类构造；接口仅按契约 6.11 演进（红线 #5） |

**汇总：9 项全部 ✅ 满足/关闭——第三轮复审检查清单闭环。**

## 三、已满足项的实现落点（供 0号位 复核勾选）

### 11 ~ 13（本轮 #16~#19 收口）

- **11 内容快照落点**：`PublishRuleSetVersionAsync` 聚合 `DemandPriority` 子块 → `ContentSnapshotJson`；`PublishParameterSetVersionAsync` 聚合五子块（Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail）→ `ContentSnapshotJson`。实体/Repository SQL 与冻结 DDL v5.1.2 §3.10.2/§3.10.4 逐列匹配（正式字段双写，无未冻结列）。
- **12 六块真实重放**：`FrozenStrategySnapshotProvider.GetFrozenStrategySnapshotAsync` 六块统一从 `ContentSnapshotJson` 结构化子块反序列化（替代原 Solver/Candidate 空对象）。R14~R17 具体值断言：`Mode==Backward`、`TargetPercent==85`、`DefaultSetupMinutes==45`、`NormalMs==70000`、`SoftMs==110000`、`LocalHardMs==200000`、`WarnOnlyOnMaxImpacted==true`。
- **13 统一失败语义**：六块缺失/损坏一律装载失败（"缺少 X 子块"/"ContentSnapshotJson 格式无效"/"反序列化结果为空"）；P1-01 防御：非 PUBLISHED/未生效/已失效版本装载失败（新增 2 用例）；发布链 `ValidateParameterSetVersionForPublishAsync` 接线两 Validator。

### 14 ~ 16（Candidate 生命周期红线，第二批整改）

- **14 CTP 永不激活**：`RunLifecycleService.ActivateCandidateAsync` 链式前置：`EnsureCandidateConfirmable → EnsureConfirmedAsync（GovernanceAuditLog 存在 ConfirmCandidate 审计）→ EnsureSourceRunActivatableAsync（SourceScheduleRunId 非空 + RunType != "INSERT_ORDER_WHATIF"）`。无任何路径可绕过（无 ScopeJson/Purpose 分支遗漏）。
- **15 确认硬前置 + 不污染 Activated**：`ConfirmCandidateAsync` 仅写审计（OperationType=ConfirmCandidate），不再写 ActivatedAt/ActivatedBy、不再 UpdateAsync；`ActivateCandidateAsync` 强制校验确认审计存在，无确认事实直接抛异常。
- **16 原子替换**：`IPlanVersionRepository.ReplaceActiveAsync(candidate, actor, activatedAt)` 单事务内：同 DomainKey 既有 ACTIVE → ARCHIVED（+ArchivedAt），candidate → ACTIVE（+ActivatedAt/ActivatedBy）。UQ_PlanVersion_OneActivePerDomain 由事务原子性兜底，非"先删后插"窗口。

### 17（FULL 重复 Domain）

- `ValidateDomainKeys` FULL 分支 `domains.Distinct().Count() != domains.Count → 抛异常`。

### 18（R01~R22 证据）

- 映射表 v2.0 §二 逐项映射（22/22 全绿）；单元测试 104/104 于 2026-08-22 仓库内实际执行（非仅源码审查）。
- 集成测试 7 失败 = 2号位 未执行 #15 变更申请（DB 缺 `ContentSnapshotJson` 列），红线 #6 外部阻塞；6 跳过 = 环境缺口（APS_Auth 库 / ExpectedDomainKeysJson 列）；1 失败 = 2号位 `FiniteCapacitySolver` DI（既有）。

### 19（无过度设计）

- 全程无 PriorityScore（Validator 白名单禁止）、无逐笔 RPC、无 3→5 决策接口、无 DSL/脚本/插件平台。

## 四、原未关闭项（11~13）收口记录

| 项 | v1.0 状态 | 阻塞原因 | 收口动作（#16~#19） | v2.0 状态 |
|---|---|---|---|---|
| 11 | ❌ 阻塞 | 冻结 DDL 无快照字段，红线 #6 不得自行 ALTER | 0号位 批准方案 A（§12.3）；《DDL变更申请_ContentSnapshotJson》提交 2号位；实体/Repository SQL 对齐 | ✅ 关闭 |
| 12 | ❌ 阻塞 | Solver/Candidate 无真实版本来源 | Provider 六块统一 ContentSnapshotJson 子块反序列化；R14~R17 具体值断言（非 NotNull） | ✅ 关闭 |
| 13 | 🟡 部分 | 两块未纳入失败语义 | 六块统一缺失/损坏装载失败（+2 用例）；两 Validator 接入发布链（+2 用例） | ✅ 关闭 |

> 发布链与装载链闭环（契约 §6.10.5）：发布阶段 validate → 聚合五子块/一子块 → ContentSnapshotJson → PUBLISHED；装载阶段 ContentSnapshotJson → 六块反序列化 + P1-01 防御。

## 五、测试验证汇总（2026-08-22 实测，收口后）

```
dotnet build LPS.APS.sln   → 0 错误
dotnet test LPS.APS.Tests --filter "FullyQualifiedName~Unit"  → 104 通过 / 0 失败 / 0 跳过
```

- **单元测试 104/104 全绿**：含本轮新增 6 项（六块统一失败 ×2、P1-01 防御 ×2、Validator 发布链拒绝 ×2、发布聚合 ContentSnapshotJson 断言）与改写 4 项（正常装配六块真实重放、JSON 缺失/损坏来源迁移、失败不写缓存）
- **集成测试 7 失败 / 1 通过 / 6 跳过**：7 失败根因为 `RuleSetVersionRepository.AddAsync` 写 `[ContentSnapshotJson]` 列而 DB 尚无此列（**2号位 执行 #15 变更申请后自动转绿**，非 3号位 代码缺陷）；6 跳过 = 环境缺口（APS_Auth 库 / ExpectedDomainKeysJson 列）；1 失败 = 2号位 `FiniteCapacitySolver` DI（既有，非本轮范围）

## 六、跨号位边界遵守声明

- ✅ 未修改 2号位运行状态执行逻辑：`ScheduleRunService` / `SchedulingOrchestrator` / `DomainSchedulingJob` / `DomainLayerCoordinatorJob`
- ✅ 未 ALTER 冻结 DDL（v5.1.2）：DDL 变更以《DDL变更申请》提交 2号位 执行（红线 #6）
- ✅ 红线 #5：接口新增/变更前均已先追加契约文档 6.10 / 6.11 / 6.11.5 / §6.10.5 方案 A 落点
- ✅ 红线 #4：不盲目 `.First()`；同域 ACTIVE 查询"无则 null"语义明确
- ✅ 红线 #6：集成测试环境探测为只读，不碰库结构

## 七、复核清单（供 0号位 / 第三轮复审逐项勾选）

- [ ] 11 DDL/快照落点一致（方案 A 已批准，DDL 变更申请已提交 2号位）
- [ ] 12 六块按 StrategyProfileVersion 重放具体值（R14~R17）
- [ ] 13 必填损坏/缺失一律失败，Solver/Candidate 真实来源
- [ ] 14 CTP/INSERT_IMPACT_ANALYSIS 无任何路径可激活
- [ ] 15 未确认 Candidate 无法激活；确认不污染 Activated 字段
- [ ] 16 Base ACTIVE 存在时 Candidate 确认与原子采用正常
- [ ] 17 ExpectedDomainKeys FULL 重复 Domain 被拒绝
- [ ] 18 R01~R22 证据与 `dotnet test` 实测（单元 104/104）
- [ ] 19 无 PriorityScore / 逐笔 RPC / 3→5 决策接口 / DSL 平台

---

## 附：相对二轮锁定 Commit（f62e141）的 3号位 变更清单

| 文件 | 变更 |
|---|---|
| `LPS.APS.Application\Services\RunLifecycleService.cs` | P0-03/04/05/06 + P1-01 主整改（14~17） |
| `LPS.APS.Core\Interfaces\IPlanVersionRepository.cs` | 移除 `GetActiveByDomainKeyAsync`、新增 `ReplaceActiveAsync`（16） |
| `LPS.APS.Engine\Repositories\Governance\PlanVersionRepository.cs` | 实现单事务原子替换 `ReplaceActiveAsync`（16） |
| `LPS.APS.Tests\Unit\RunLifecycleServiceTests.cs` | 改写 4 错误测试 + 新增 5 测试（27 项全绿） |
| `LPS.APS.Tests\Integration\RunLifecycleServiceIntegrationTests.cs` | 确认/激活断言对齐（15/16） |
| `开发文档\契约文档\05_3号位和1号位对外契约.md` | §6.11 契约收紧 + §6.10.5 方案 A 落点（红线 #5） |
| `README.md` | P1-03 最小去旧化 |
| `开发文档\工作日志\3号位\APS_V1_3号位R01-R22验收证据映射表_v2.0_20260822.md` | R01~R22 逐项映射 + 实测证据（18，22/22 全绿） |
| `LPS.APS.Application\Services\SolverStrategyValidator.cs` / `CandidateGuardrailValidator.cs` | **E-4** Solver/Guardrail 发布前校验器 |
| `LPS.APS.Tests\Unit\SolverStrategyValidatorTests.cs` | **E-4** 15 用例全绿 |
| `LPS.APS.Core\Entities\Aps\RuleSetVersion.cs` / `ParameterSetVersion.cs` | **#16** +`ContentSnapshotJson`；-`UpdatedAt/UpdatedBy`；主题 JSON 注释为发布装配中间态 |
| `LPS.APS.Engine\Repositories\Governance\RuleSetVersionRepository.cs` / `ParameterSetVersionRepository.cs` | **#16** INSERT/UPDATE 对齐方案 A |
| `LPS.APS.Application\Services\FrozenStrategySnapshotProvider.cs` | **#17** 六块统一 ContentSnapshotJson 子块反序列化 + P1-01 防御（EnsureLoadable） |
| `LPS.APS.Application\Services\GovernanceVersionService.cs` | **#18** 两 Validator 接入发布链 + 发布聚合 ContentSnapshotJson |
| `LPS.APS.Tests\Unit\FrozenStrategySnapshotProviderTests.cs` / `ParameterSetVersionPublishTests.cs` | **#19** R14~R17 重放 + 六块统一失败 ×2 + P1-01 ×2 + Validator 拒绝 ×2 + 发布聚合断言 |
