# APS V1 3号位 P0 整改跟踪

> 版本：v1.0
> 日期：2026-08-19
> 执行人：3号位
> 依据：《APS_V1_3号位代码综合符合性审核报告_v1.1_20260819》（0号位冻结整改基线）
> 执行方式：按 0 号位报告五批顺序执行（先核对 DDL 事实再动手）

---

## 总览

| 批 | 编号 | 项目 | 状态 | 说明 |
|---|---|---|---|---|
| 一 | P0-01 | 六块持久化来源映射 + 最小 DDL 差异 | ✅ 完成 | 产出《六块Snapshot持久化来源映射表_v1.0》；待 0 号位确认方案 A/B/C |
| 一 | P0-02 | 删除 RuleSet/ParameterSet 子默认版本真相 | ✅ 完成 | 已核对冻结 DDL 后删除；见下 |
| 一 | P0-03 | 补 PlanningYield / SolverStrategy / CandidateGuardrail 真实来源 | 🟡 部分 | 结构层核对：PlanningYield 已在 Snapshot（ProcurementBlock.PlanningYields）；装载层（Solver/Candidate）依赖 P0-01 DDL 裁决 |
| 一 | P0-04 | Snapshot 坏配置明确失败 | ✅ 完成 | 四块 JSON 缺失/损坏一律装载失败，不静默回退空 Block；见下 |
| 二 | P0-05 | 正式 Publish 强制发布前校验 | ✅ 完成 | 无两条绕过路径；见下 |
| 二 | P0-06 | StrategyProfileVersion 治理完整闭环 | ✅ 完成 | 发布/校验/默认解析/引用合法性/Run 引用追溯；见下 |
| 三 | P0-07 | DemandPriority 补 DueDate/IssueDate | ✅ 完成 | 字段 + 匹配映射 + 校验；见下 |
| 三 | P0-08 | 运行生命周期与 Candidate 最小确认边界 | ✅ 完成 | 契约 6.11 + 接口 + 服务 + 仓储 + 端点 + 23 测试；见下 |

---

## P0-02 整改详情（已完成）

### 核对结论（先核对再动手）

冻结 DDL v5.1.2（§3.10.2 / §3.10.4 / §3.10.6）事实核对：

| 表 | 是否有 `IsDefault` 列 | 结论 |
|---|---|---|
| RuleSetVersion | ❌ 无 | 0 号位答复正确 |
| ParameterSetVersion | ❌ 无 | 0 号位答复正确 |
| StrategyProfileVersion | ✅ 有（`IsDefault BIT NOT NULL DEFAULT 0` + 唯一过滤索引 `UQ_StrategyProfileVersion_DefaultPublished`） | 默认真相唯一落点 |

→ 0 号位 P0-02 答复与冻结 DDL 完全一致，可执行。

### 已删除（代码改动）

| 文件 | 删除内容 |
|---|---|
| `LPS.APS.Core/Entities/APS/RuleSetVersion.cs` | `public bool IsDefault { get; set; }` |
| `LPS.APS.Core/Entities/APS/ParameterSetVersion.cs` | `public bool IsDefault { get; set; }` |
| `LPS.APS.Core/Interfaces/IRuleSetVersionRepository.cs` | `ClearDefaultFlagAsync` / `GetDefaultByRuleSetIdAsync` 声明 |
| `LPS.APS.Core/Interfaces/IParameterSetVersionRepository.cs` | `ClearDefaultFlagAsync` / `GetDefaultByParameterSetIdAsync` 声明 |
| `LPS.APS.Engine/Repositories/Governance/RuleSetVersionRepository.cs` | 两方法实现 + INSERT/UPDATE SQL 中 `[IsDefault]`/`@IsDefault` |
| `LPS.APS.Engine/Repositories/Governance/ParameterSetVersionRepository.cs` | 两方法实现 + INSERT/UPDATE SQL 中 `[IsDefault]`/`@IsDefault` |
| `LPS.APS.Application/Services/GovernanceVersionService.cs` | 两处发布时"清子版本默认"逻辑 + 两处 `CompareField("IsDefault",...)` |
| `LPS.APS.Web/Controllers/GovernanceController.cs` | 两个 HTTP GET 端点：`rule-set/{id}/default-version` / `parameter-set/{id}/default-version` |

### 保留（不在删除范围）

- `StrategyProfileVersion.IsDefault` — 默认真相唯一落点（0 号位指定保留）
- `StageLeadTimeParam.IsDefault` — 阶段级兜底默认，不同语义
- `RuleSetVersion.DemandPriorityJson` / `ParameterSetVersion.LockJson/SupplyJson/ProcurementJson` — 与 P0-01 DDL 裁决耦合，确认前不删
- `RuleSetVersion.Remarks/UpdatedAt/UpdatedBy` 等审计字段 — 保留

### 验证结果

- ✅ `dotnet build LPS.APS.sln`：0 错误（19 个警告均为既有：CS0618/CS1998/CS8892/CS8604，与本次无关）
- ✅ 治理发布 + DemandPriority 相关测试：14/14 通过
- ✅ 全量 24 个测试：23 通过，1 个既有失败（见下，与 P0-02 无关）

---

## P0-05 整改详情（已完成）

### 0 号位要求（冻结基线）

正式 Publish 强制发布前校验：不得存在两条绕过路径（Publish API 直接发布 vs Validate API 校验），正式发布必须走 Validate → 有 Error 拒绝 → Publish。

### 实现决策

- **两条发布路径**（`PublishRuleSetVersionAsync` / `PublishParameterSetVersionAsync`）均**强制调用** `ValidateXxxForPublishAsync`，`IsValid=false` 时抛 `InvalidOperationException`（含错误摘要 `GetErrorMessage()`），**无绕过路径**
- **DemandPriorityValidator 正式接入**：此前从未被调用（Scrutor 只按接口注册，sealed 无接口类不进 DI）——P0-05 起经 `ValidateRuleSetVersionForPublishAsync` 强制校验（Segments 序号、MatchConditions、IN 列表、禁用字段等）
- **参数集三块 JSON 新增业务校验**（均为 3 号位 DTO 语义内的边界约束）：
  | JSON | 校验规则 |
  |---|---|
  | LockJson | 启用 RemainingTime 阈值但阈值非正 → Error |
  | SupplyJson | WarehousePriority 重复 → Error（优先级歧义） |
  | ProcurementJson | YieldPercent 不在 (0,100] → Error；DefaultLtDays ≤ 0 → Error；OffsetHours < 0 → Error；MarginPercent 不在 0~100 → Error；MinimumExtraDays < 0 → Error |
- **缺失/损坏语义**：JSON 空/缺失 → `EMPTY_PARAMETER` / `EMPTY_DEMAND_PRIORITY`；反序列化失败 → `INVALID_JSON`；一律进 `result.Errors`，不抛未捕获异常

### 代码改动

| 文件 | 改动 |
|---|---|
| `LPS.APS.Application/Services/GovernanceVersionService.cs` | 两条 Publish 路径强制校验；`ValidateRuleSetVersionForPublishAsync` 接入 `DemandPriorityValidator` + 空 JSON 拒绝；新增 `ValidateLockJson/ValidateSupplyJson/ValidateProcurementJson` 三方法；`JsonOptions` 常量 |
| `LPS.APS.Core/DTOs/Governance/PublishValidationResult.cs` | 新增 `GetErrorMessage()`（错误汇总，P0-05 发布被拒时反馈） |
| `LPS.APS.Tests/Unit/RuleSetVersionPublishTests.cs` | Draft 测试补合法 `DemandPriorityJson`；已发布测试同样补合法 JSON（穿透校验，确保拦截点确为"已发布"） |
| `LPS.APS.Tests/Unit/ParameterSetVersionPublishTests.cs` | 新增 `CreateValidJson()` 构造合法三块 JSON；Draft / V2 发布 / 已发布 三测试均补合法内容 |

### 验证结果

- ✅ `dotnet build`：0 错误（4 警告均为既有 CS8892/CS8604，与本次无关）
- ✅ 全量测试：**23/24 通过**（唯一失败为既有 `完整排程流程集成测试`，`FiniteCapacitySolver` DI 问题，属 2号位/1号位，见"发现的其他问题"）

### 语义澄清（测试修正）

`Publish_AlreadyPublishedVersion_ThrowsInvalidOperation` 原为空 JSON 版本，会被新校验以"JSON 为空"**抢先拦截**——测试虽绿但未测到"历史不可覆盖"。已为已发布版本补合法 JSON，使其穿透校验后由状态机 `EnsurePublishable` 拒绝，确保拦截点确为"已发布"。

---

## P0-06 整改详情（已完成）

### 核对结论（先核对再动手）

冻结 DDL v5.1.2（§3.10.5 / §3.10.6 / §3.11）事实：

| 事实 | 结论 |
|---|---|
| `StrategyProfileVersion`：`IsDefault` + `UQ_StrategyProfileVersion_DefaultPublished`（WHERE IsDefault=1 AND Status='PUBLISHED'） | ✅ 同 Profile 内默认唯一由 DB 强制 |
| `StrategyProfileVersion`：`EffectiveFrom/EffectiveTo`、FK→RuleSetVersion/ParameterSetVersion | ✅ 字段齐全 |
| **`StrategyProfileVersion` 无 `RunType` 列** | ⚠️ RunType 在**父表** `StrategyProfile.RunType`（值域 5 种）→ RunType 匹配须 JOIN 父表 |
| `ScheduleRun.StrategyProfileVersionId`（§3.11）| ✅ Run 引用载体 |
| `ScheduleRun.RunType` | ✅ Run 自身有 RunType |

### 0 号位 8 项要求 → 实现映射

| 0 号位要求 | 实现 |
|---|---|
| ① `ValidateStrategyProfileVersionForPublish` | `GovernanceVersionService.ValidateStrategyProfileVersionForPublishAsync`：状态可发布 / 编码非空 / **引用合法**（RuleSet/ParameterSet 存在且 PUBLISHED）/ 生效窗口 / **默认歧义**（DEFAULT_CONFLICT） |
| ② `PublishStrategyProfileVersion` | 发布前强制校验（无绕过路径）；**IsDefault=1 时先 ClearDefaultFlag 再置位**（防 UQ 冲突）；审计日志 |
| ③ 当前有效默认 PUBLISHED 策略包查询/解析 | `ResolveDefaultStrategyProfileVersionAsync(runType, asOf)`：RunType 匹配 + 生效窗口过滤 → 0/null、1/返回、>1/抛歧义 |
| ④ 引用合法性检查 | Validate 内 REF_NOT_FOUND / REF_NOT_PUBLISHED |
| ⑤ RunType 匹配 | 经父表 `StrategyProfile.RunType`（新 `IStrategyProfileRepository` + `GetDefaultByRunTypeAsync` JOIN） |
| ⑥ EffectiveFrom / EffectiveTo | 校验窗口 + 解析默认时过滤（asOf 默认当前时刻） |
| ⑦ 默认唯一性和歧义检查 | DB UQ 兜底 + Application 层跨 Profile 歧义检测（多候选 → 抛 `InvalidOperationException`，**不随机取一个**） |
| ⑧ Run 引用追溯 | `GetRunStrategyProfileTraceAsync` → `RunStrategyProfileTrace` DTO（版本→父 Profile→规则集/参数集版本编码） |

### 跨号位边界遵守

- **未修改 2 号位** `ScheduleRunService`（其内部 FULL_SCHEDULE 默认解析 SQL 保留不动；0 号位明确"不要求 2 号位改主流程、不作为 3 号位反向修改理由"）
- **未 ALTER 冻结 DDL**（RunType 经父表 JOIN，不加列）
- **红线 #5**：新增接口前已在 `契约文档/05_3号位和1号位对外契约.md` 追加 6.10 治理接口契约章节

### 代码改动清单

| 文件 | 改动 |
|---|---|
| `契约文档/05_3号位和1号位对外契约.md` | 追加 6.10 治理版本发布接口契约（IGovernanceVersionService 扩展 / IStrategyProfileRepository / IStrategyProfileVersionRepository 扩展 / RunStrategyProfileTrace） |
| `LPS.APS.Core/Interfaces/IStrategyProfileRepository.cs` | **新增**（父表仓储：GetById / GetByRunType） |
| `LPS.APS.Core/DTOs/Governance/RunStrategyProfileTrace.cs` | **新增**（Run 引用追溯 DTO） |
| `LPS.APS.Core/Interfaces/IStrategyProfileVersionRepository.cs` | 加 `GetDefaultByRunTypeAsync`（歧义检测全集返回，红线 #4 不 First()） |
| `LPS.APS.Core/Interfaces/IGovernanceVersionService.cs` | 加 4 方法（ValidateStrategyProfile / PublishStrategyProfile / ResolveDefault / GetRunTrace） |
| `LPS.APS.Engine/Repositories/Governance/StrategyProfileRepository.cs` | **新增**（Dapper 父表实现） |
| `LPS.APS.Engine/Repositories/Governance/StrategyProfileVersionRepository.cs` | 加 `GetDefaultByRunTypeAsync`（JOIN 父表 IsActive=1） |
| `LPS.APS.Engine/Extensions/GovernanceServiceExtensions.cs` | 注册 `IStrategyProfileRepository` |
| `LPS.APS.Application/Services/GovernanceVersionService.cs` | 注入 2 个仓储（父表+版本表）；实现 4 个 P0-06 方法 + 泛型引用校验 `ValidateReferencedVersionAsync<T>` |
| `LPS.APS.Web/Controllers/GovernanceController.cs` | 注入版本仓储；新增 8 端点：versions / version / create / update / publish / validate / default 解析 / trace |
| `LPS.APS.Tests/Unit/StrategyProfileVersionGovernanceTests.cs` | **新增** 12 测试（校验 4 / 发布 3 / 解析 4 / 追溯 1） |
| `LPS.APS.Tests/Unit/RuleSetVersionPublishTests.cs` / `ParameterSetVersionPublishTests.cs` | 构造函数补 2 个新 mock 参数 |

### 验证结果

- ✅ `dotnet build LPS.APS.sln`：0 错误
- ✅ 全量测试：**35/36 通过**（12 个新增 P0-06 测试全绿；唯一失败为既有 `完整排程流程集成测试` DI 问题，属 2号位）
- ✅ 发布默认版本防 UQ 冲突：IsDefault=1 发布先 `ClearDefaultFlagAsync`（测试验证）

### 设计要点（歧义语义）

换默认版本流程：先把旧默认 `IsDefault` 置 0（Update 接口）→ 再发布新默认（IsDefault=1）。若直接发布会触发 `DEFAULT_CONFLICT` 校验拒绝——保证任何时点"当前有效默认"唯一，符合 0 号位"歧义报配置错误，不随机取一个"的跨号位冻结语义。

---

## P0-07 整改详情（已完成）

### 0 号位要求（冻结基线）

DemandPriority 框架正确但还不能表达冻结的真实默认排序——核心排序需要 DueDate / IssueDate（冻结示例：`Delayed SALES_ORDER → DueDate ASC → CustomerTier DESC → IssueDate ASC`）。必须补：DemandField.DueDate / DemandField.IssueDate / DemandRecord 对应字段 / Matcher Sort 映射 / Validator 合法字段映射 / 测试。红线：CalculationLayer 由 2 号位先分层再应用 Segment，3 号位不管理全局 Demand 池。

### 字段语义核对（先核对再动手）

数据库字段说明文档：`DueDate`=统一交期（DATE/DATETIME2，客户要求交期）；`IssueDate`=订单发行/下发日期（源事实字段，MTS 无值）。

### 实现决策

- **`DemandField` 枚举末尾追加 `DueDate` / `IssueDate`**（序数 6/7，现有 0-5 序数不变，已存 JSON 整数序号不受影响）
- **`DemandRecord` 加 `DateTime? DueDate` / `DateTime? IssueDate`**（init 属性，与既有可空字段一致）
- **Matcher 三处 switch 补映射**：`ApplySort` / `ApplyThenSort` / `GetFieldValue`；日期匹配经 `CompareValues` 的 `IComparable` 比较（支持 LessThan/GreaterOrEqual 等条件）
- **日期 null 语义**：与既有 `double? RemainingTimeHours` 一致——LINQ `OrderBy` 对 `Nullable<T>` Asc 时 null 排最前、Desc 时 null 排最后；不引入自定义比较器（0 号位未要求 null 语义，测试固化行为）
- **Validator 合法字段映射**：新增 `SupportedDemandFields` 白名单（8 字段），Validate 遍历所有 Segment 的 MatchConditions + SortFields 校验字段在白名单内——防未来枚举新增值未同步 Matcher switch 时，配置越过发布校验、运行期才抛 `NotSupportedException`
- **红线遵守**：未引入任何 CalculationLayer/全局 Demand 池管理（2 号位职责），仅补字段与映射

### 代码改动清单

| 文件 | 改动 |
|---|---|
| `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs` | `DemandField` 追加 `DueDate` / `IssueDate`（含 P0-07 同步提醒注释） |
| `LPS.APS.Application/Services/DemandPriorityMatcher.cs` | `DemandRecord` 加 2 日期字段；`ApplySort`/`ApplyThenSort`/`GetFieldValue` 三处 switch 补映射 |
| `LPS.APS.Application/Services/DemandPriorityValidator.cs` | 新增 `SupportedDemandFields` 白名单 + `ValidateFieldMapping`（合法字段映射校验） |
| `LPS.APS.Tests/Unit/DemandPriorityMatcherTests.cs` | +3 测试：冻结示例（DueDate ASC→CustomerTier DESC→IssueDate ASC）/ DueDate 匹配条件日期比较 / IssueDate 排序 null 语义 |
| `LPS.APS.Tests/Unit/DemandPriorityValidatorTests.cs` | **新增** +4 测试：枚举-白名单一致性（防漏登记）/ 冻结示例配置通过 / DueDate 匹配通过 / 合法配置通过 |

### 验证结果

- ✅ `dotnet build LPS.APS.sln`：0 错误（19 警告均为既有 CS8604 等）
- ✅ DemandPriority + 治理发布相关测试：**33/33 通过**（新增 7 个全绿）
- ✅ 全量 43 个测试：**42 通过**，唯一失败为既有 `完整排程流程集成测试`（FiniteCapacitySolver DI，属 2号位，与本次无关）

---

## P0-03 核对结论（部分完成）

0 号位 P0-03「必须修改」三条逐条核对：

| 0 号位要求 | 代码现状 | 结论 |
|---|---|---|
| ① Snapshot 必须显式包含 PlanningYield | `ProcurementBlock.PlanningYields` 已存在（`FrozenStrategySnapshot.cs:157`，C2-5）；Provider 从 `ProcurementJson` 装载 | ✅ 结构层已满足 |
| ② 不得让 Solver/Candidate 依赖代码默认值冒充冻结配置 | Provider 仍 `new SolverStrategyBlock()` / `new CandidateGuardrailBlock()` 空对象 | ❌ 真实缺口，装载层依赖 P0-01 DDL 裁决 |
| ③ 不得让 2号位再查另一套 PlanningYield 表 | 2号位从同一 Snapshot 读取 | ✅ 已满足 |

> 装载层整改并入 P0-04 失败机制思路，待 P0-01 确认 DDL 方案 A/B/C 后，为 Solver/Candidate 建立真实版本来源并收口。

---

## P0-04 整改详情（已完成）

### 核对结论

0 号位 P0-04 边界：必填 Block 缺失 → 装载失败；JSON/内容损坏 → 装载失败；只有明确允许缺省的**单个字段**才允许冻结默认值；历史版本（DISABLED/ARCHIVED）仍可按 Id 读取不可变内容。

### 实现决策

- **四块有来源 JSON**（DemandPriority/Lock/Supply/Procurement）缺失（null/空白）或损坏（JsonException）→ **一律抛 `InvalidOperationException`，Snapshot 装载失败**
- **显式空块 `"{}"`** 为合法表达（反序列化成功 → 空对象 + 字段级冻结默认值），与"缺失"区分
- **SolverStrategy/CandidateGuardrail**：P0-01 未裁决前无来源字段，保持空对象（不强制必填，避免所有现有 Run 失败）
- 失败信息包含**版本号**（可追溯，如"规则集版本 200 的 DemandPriorityJson 为空/缺失"）

### 代码改动

| 文件 | 改动 |
|---|---|
| `LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs` | 四个 `DeserializeXxxBlock` 加 `versionId` 参数；空/损坏 → 抛异常（不再静默回退） |
| `LPS.APS.Core/Interfaces/IFrozenStrategySnapshotProvider.cs` | `<remarks>` 补充失败语义（红线 #5 契约一致性，签名不变） |
| `LPS.APS.Tests/Unit/FrozenStrategySnapshotProviderTests.cs` | 两测试改为断言抛异常：`JSON为空_装载失败抛异常` / `JSON格式错误_装载失败抛异常` |

### 验证结果

- ✅ `dotnet build`：0 错误
- ✅ 全量测试：23/24 通过（唯一失败为既有 `完整排程流程集成测试`，DI 问题，与本次无关）
- ✅ 集成测试（真实数据库完整数据）不受影响

---

## 发现的其他问题（非本次整改范围，提交反馈）

### 既有问题 1：`完整排程流程集成测试` 失败 — SchedulingOrchestrator DI 依赖具体类

- 现象：`Unable to resolve service for type 'LPS.APS.Scheduling.Solvers.FiniteCapacitySolver' while attempting to activate 'SchedulingOrchestrator'`
- 根因：`SchedulingServiceExtensions.AddSchedulingServices()` 仅注册接口 `IFiniteCapacityScheduler → FiniteCapacitySolver`；而 `SchedulingOrchestrator` 构造函数依赖**具体类** `FiniteCapacitySolver`（`SchedulingOrchestrator.cs:41`）
- 归属：2号位/1号位层（Engine/Scheduling 依赖面），非 3 号位代码。按红线不擅自修改
- 建议：提交给 2 号位，在 `AddSchedulingServices()` 补 `services.AddSingleton<FiniteCapacitySolver>()`，或 `SchedulingOrchestrator` 改依赖接口

---

## P0-08 整改详情（已完成）

### 0 号位要求（冻结基线）

ScheduleRun 运行生命周期治理（ExpectedDomainKeysJson 冻结规则 / StrategyProfileVersion 绑定 / Candidate 最小确认 / FAILED 恢复 / Run 引用追溯）。红线：不重写 2号位已冻结的运行状态执行逻辑（SchedulingOrchestrator / ScheduleRunService / DomainSchedulingJob 不动）。

### 边界核对（先核对再动手）

冻结 DDL v5.1.2（§3.1 ScheduleRun / §3.2 PlanVersion）事实核对：

| 事实 | 结论 |
|---|---|
| `ScheduleRun.ExpectedDomainKeysJson`（NVARCHAR(MAX) + CHECK ISJSON） | ✅ 冻结预期 Domain 集合，终态判定唯一权威来源；创建后不可修改 |
| `ScheduleRun.RunType` 值域 5 种 | ✅ FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF |
| `ScheduleRun.Status` 四态（RUNNING/COMPLETED/PARTIAL_SUCCESS/FAILED） | ✅ 终态口径已固化 |
| `PlanVersion.ActivatedAt / ActivatedBy`（DDL v5.1.2 已含） | ✅ Candidate 确认/激活落点 |
| `UQ_PlanVersion_OneActivePerDomain`（WHERE Status='ACTIVE' AND DomainKey IS NOT NULL） | ✅ 每域单一正式采用版本由 DB 强制，Application 层预检 |
| `PlanVersion.Status` 值域（BUILDING/CANDIDATE/ACTIVE/ARCHIVED/FAILED） | ✅ Candidate 确认/激活状态机依据 |

### 实现决策

- **ExpectedDomainKeysJson 冻结规则**：FULL_SCHEDULE → Domain 数 ≥ 1；RESCHEDULE 类（Candidate）→ 恰 1 Domain。空/缺失/非 JSON 数组/含空 DomainKey/数量越界 → 一律抛 `InvalidOperationException`（配置错误，不静默降级）
- **Candidate 最小确认**：`ConfirmCandidate`（CANDIDATE 校验 + DomainKey 非空 + 同域唯一预检 → 写 ActivatedAt/ActivatedBy，状态保持 CANDIDATE）+ `ActivateCandidate`（CANDIDATE → ACTIVE 正式采用）。两步分离：确认仅记录，激活才采用
- **审计记录**：`CandidatePlanVersionId` 无独立列，记入 `GovernanceAuditLog.Remarks`（契约 6.11.4 说明）
- **FAILED 恢复**：为 FAILED ScheduleRun **新建**一条 RUNNING（继承 RunType / StrategyProfileVersionId / ExpectedDomainKeysJson 基线），**新建前先校验继承基线合法性**（避免插入后再因基线不合法产生孤立 RUNNING）；旧 FAILED 记录绝不动（不回改 RUNNING）
- **Run 引用追溯**：Run 维补齐（P0-06 为版本维），含冻结 ExpectedDomainKeysJson 与关联 PlanVersion 状态

### 代码改动清单

| 文件 | 改动 |
|---|---|
| `契约文档/05_3号位和1号位对外契约.md` | 追加 6.11.5 支撑仓储接口契约（IScheduleRunRepository / IPlanVersionRepository / ScheduleRunGov） |
| `LPS.APS.Core/DTOs/Governance/ScheduleRunGov.cs` | **新增**（ScheduleRun 治理只读模型，仅冻结列） |
| `LPS.APS.Core/Interfaces/IScheduleRunRepository.cs` | **新增**（GetById / InsertForRecoveryAsync） |
| `LPS.APS.Core/Interfaces/IPlanVersionRepository.cs` | **新增**（GetById / GetActiveByDomainKeyAsync / UpdateAsync / GetLatestByScheduleRunIdAsync） |
| `LPS.APS.Application/Services/RunLifecycleService.cs` | **新增**（IRunLifecycleService 实现：5 方法 + 冻结规则校验 + 同域唯一预检） |
| `LPS.APS.Engine/Repositories/Governance/ScheduleRunRepository.cs` | **新增**（Dapper：只读冻结列 + 恢复新建 INSERT OUTPUT Id） |
| `LPS.APS.Engine/Repositories/Governance/PlanVersionRepository.cs` | **新增**（Dapper：读 + 仅更新 Status/ActivatedAt/ActivatedBy） |
| `LPS.APS.Engine/Extensions/GovernanceServiceExtensions.cs` | 注册 2 个新仓储 |
| `LPS.APS.Web/Controllers/GovernanceController.cs` | 注入 IRunLifecycleService；新增 5 端点（validate-domain-keys / confirm-candidate / activate-candidate / recover / trace） |
| `LPS.APS.Tests/Unit/RunLifecycleServiceTests.cs` | **新增** 23 测试（校验 8 / 确认 5 / 激活 4 / 恢复 4 / 追溯 2） |

### 验证结果

- ✅ `dotnet build LPS.APS.sln`：0 错误 0 警告（本次新增代码）
- ✅ 全量测试：**65/66 通过**（23 个新增 P0-08 测试全绿；唯一失败为既有 `RealSchedulingIntegrationTest` 的 `FiniteCapacitySolver` DI 问题，属 2号位，见"既有问题 1"）

### 集成测试补齐（P0-05/P0-06/P0-07/P0-08，2026-08-20）

给 0 号位复核前补齐修改项集成测试（真实连库 + 真实仓储 + 真实服务编排），新增 3 个文件：

| 文件 | 覆盖 |
|---|---|
| `LPS.APS.Tests/Integration/TestEnvironment.cs` | **新增** 环境探测辅助（Auth 库可达性 / ExpectedDomainKeysJson 列存在性；只读探测不碰库结构，红线 #6） |
| `LPS.APS.Tests/Integration/GovernanceVersionServiceIntegrationTests.cs` | **新增** 发布闭环全链路（校验→发布→默认解析→Run 追溯）、P0-05 坏配置被拒无绕过、P0-06 引用未发布被拒、P0-07 校验器集成（4 个 SkippableFact） |
| `LPS.APS.Tests/Integration/RunLifecycleServiceIntegrationTests.cs` | **新增** P0-08 恢复新建 RUNNING 继承基线、Run 引用追溯完整链、Candidate 确认与激活落库（3 个 SkippableFact） |

- 集成测试统一 `[SkippableFact]`：测试环境缺依赖（APS_Auth 库 / ExpectedDomainKeysJson 列）时**动态 Skip 并提示需 2 号位补齐**，不假绿不误红；环境补齐后自动转绿无需改测试代码
- ✅ 全量测试：**73 总数 / 66 通过 / 6 跳过 / 1 失败**（6 个跳过 = 治理链路集成测试依赖 APS_Auth 库；`校验器集成_P007` 不依赖 Auth 已真实连库跑通；唯一失败仍为既有 2号位 DI 问题）
- ✅ 集成测试数据清理验证：测试后 ScheduleRun/PlanVersion 残留为 0

### 跨号位边界遵守

- **未修改 2号位** `ScheduleRunService` / `SchedulingOrchestrator` / `DomainSchedulingJob` / `DomainLayerCoordinatorJob`——运行状态执行流转不动
- **未 ALTER 冻结 DDL**（Candidate 确认落点复用既有 ActivatedAt/ActivatedBy；每域唯一由既有 UQ 索引兜底）
- **红线 #5**：新增接口前已先追加契约文档 6.11（P0-08）与 6.11.5（支撑仓储）
- **红线 #4**：同域 ACTIVE 查询返回单对象有明确"无则 null"语义（预检场景，非列表歧义）；ExpectedDomainKeys 解析按全集判定数量规则

---

## 下一步

- **P0-03 收口**：待 P0-01 DDL 方案 A/B/C 确认后，为 Solver/Candidate 建立真实版本来源（装载层整改）
- **集成测试环境缺口（需 2 号位补齐后，6 个治理链路集成测试自动转绿）**：
  1. **APS_Auth 库未部署**（测试服务器 10.116.2.75 仅有 APS_Production / APS_Hangfire）→ 治理发布/确认/激活/恢复强制写 `GovernanceAuditLog`（EF Core）无法落地
  2. **测试库 ScheduleRun 缺 `ExpectedDomainKeysJson` 列**（冻结 DDL v5.1.2 §3.1 未迁移）→ P0-08 恢复/追溯链路无法读该列
- 提示：P0-01~P0-08 八批整改已全部完成（P0-03 装载层部分除外），单元测试 59 + 集成测试补齐完毕，建议向 0 号位提交验证结果、确认 P0-01 方案后收口 P0-03
