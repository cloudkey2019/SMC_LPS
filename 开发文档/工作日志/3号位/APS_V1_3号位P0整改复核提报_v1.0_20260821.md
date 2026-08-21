# APS V1 3号位 P0 整改复核提报

> 版本：v1.0
> 日期：2026-08-21
> 提报人：3号位
> 复核对象：0号位
> 依据：《APS_V1_3号位代码综合符合性审核报告_v1.1_20260819》（冻结整改基线）
> 对应提交：`a9de010`（整改主体代码）、`de79f27`（P0-05~P0-08 集成测试补齐）

---

## 一、提报背景

按 0 号位《代码综合符合性审核报告_v1.1》冻结整改基线，3 号位已完成 P0-01~P0-08 八批整改，并在给 0 号位复核前补齐了修改项的单元测试与集成测试。现提交复核。

## 二、整改总览

| 批 | 编号 | 项目 | 状态 | 验证 |
|---|---|---|---|---|
| 一 | P0-01 | 六块持久化来源映射 + 最小 DDL 差异 | ✅ 完成 | 产出《六块Snapshot持久化来源映射表_v1.0》 |
| 一 | P0-02 | 删除 RuleSet/ParameterSet 子默认版本真相 | ✅ 完成 | 先核对冻结 DDL 再删，0 错误 |
| 一 | P0-03 | 补 PlanningYield / SolverStrategy / CandidateGuardrail 真实来源 | 🟡 部分 | 结构层已满足，装载层待 P0-01 裁决（见五） |
| 一 | P0-04 | Snapshot 坏配置明确失败 | ✅ 完成 | 四块 JSON 缺失/损坏一律装载失败 |
| 二 | P0-05 | 正式 Publish 强制发布前校验 | ✅ 完成 | 无绕过路径；DemandPriorityValidator 正式接入 |
| 二 | P0-06 | StrategyProfileVersion 治理完整闭环 | ✅ 完成 | 发布/校验/默认解析/引用合法性/Run 追溯 |
| 三 | P0-07 | DemandPriority 补 DueDate/IssueDate | ✅ 完成 | 字段 + 匹配映射 + 校验白名单 |
| 三 | P0-08 | 运行生命周期与 Candidate 最小确认边界 | ✅ 完成 | 契约 6.11 + 5 方法 + 23 单元测试 + 3 集成测试 |

## 三、逐批整改要点

### P0-01：六块持久化来源映射 + 最小 DDL 差异（✅ 完成）

- 产出《六块Snapshot持久化来源映射表_v1.0》，逐块核对来源（DemandPriority / Lock / Supply / Procurement 来自版本表 JSON；SolverStrategy / CandidateGuardrail 无来源字段）
- 需 0 号位确认 **DDL 方案 A/B/C**（见"五、待 0 号位裁决"），确认后收口 P0-03

### P0-02：删除 RuleSet/ParameterSet 子默认版本真相（✅ 完成）

- 先核对冻结 DDL v5.1.2（§3.10.2 / §3.10.4 / §3.10.6）事实：RuleSetVersion / ParameterSetVersion **无** `IsDefault` 列，与 0 号位答复一致后执行
- 删除：两个实体 `IsDefault` 属性、两个仓储 `ClearDefaultFlagAsync` / `GetDefaultByXxxIdAsync`、Application 层发布时清默认逻辑、Web 层两个 GET 端点
- **保留**：`StrategyProfileVersion.IsDefault`（默认真相唯一落点，UQ 兜底）、`StageLeadTimeParam.IsDefault`（阶段级兜底，不同语义）、四块 JSON 字段（与 P0-01 裁决耦合，确认前不删）
- 验证：build 0 错误；治理发布 + DemandPriority 相关测试 14/14 通过

### P0-03：补 PlanningYield / SolverStrategy / CandidateGuardrail 真实来源（🟡 部分）

逐条核对 0 号位三条要求：

| 要求 | 现状 | 结论 |
|---|---|---|
| ① Snapshot 必须显式包含 PlanningYield | `ProcurementBlock.PlanningYields` 已存在（FrozenStrategySnapshot），Provider 从 ProcurementJson 装载 | ✅ 结构层满足 |
| ② 不得让 Solver/Candidate 依赖代码默认值冒充冻结配置 | Provider 仍 `new SolverStrategyBlock()` / `new CandidateGuardrailBlock()` 空对象 | ❌ 真实缺口，装载层依赖 P0-01 DDL 裁决 |
| ③ 不得让 2 号位再查另一套 PlanningYield 表 | 2 号位从同一 Snapshot 读取 | ✅ 满足 |

> 装载层整改并入 P0-04 失败机制思路，待 0 号位确认 DDL 方案后为 Solver/Candidate 建立真实版本来源并收口。

### P0-04：Snapshot 坏配置明确失败（✅ 完成）

- 四块有来源 JSON（DemandPriority/Lock/Supply/Procurement）缺失或损坏 → 一律抛 `InvalidOperationException`，Snapshot 装载失败，不静默回退空 Block
- 显式空块 `"{}"` 为合法表达（与"缺失"区分）
- 失败信息含版本号，可追溯（如"规则集版本 200 的 DemandPriorityJson 为空/缺失"）
- SolverStrategy/CandidateGuardrail 在 P0-01 裁决前保持空对象（避免所有现有 Run 失败）
- 验证：build 0 错误；两测试改为断言抛异常

### P0-05：正式 Publish 强制发布前校验（✅ 完成）

- 两条 Publish 路径（RuleSet/ParameterSet）均**强制调用** `ValidateXxxForPublishAsync`，`IsValid=false` 抛 `InvalidOperationException`（含错误摘要 `GetErrorMessage()`），无绕过路径
- **DemandPriorityValidator 正式接入**：此前从未被调用（Scrutor 只按接口注册，sealed 无接口类不进 DI）；现经 Validate 强制校验（Segments 序号、MatchConditions、IN 列表、禁用字段等）
- 参数集三块 JSON 新增业务校验（Lock/Supply/Procurement 边界约束）
- 缺失/损坏语义：JSON 空/缺失 → `EMPTY_PARAMETER` / `EMPTY_DEMAND_PRIORITY`；反序列化失败 → `INVALID_JSON`；一律进 `result.Errors`，不抛未捕获异常
- 验证：build 0 错误；全量测试 23/24（唯一失败为既有 2 号位 DI 问题）
- 语义澄清：`Publish_AlreadyPublishedVersion` 测试原为空 JSON，会被新校验抢先拦截——已补合法 JSON 穿透校验，确保拦截点确为"已发布"

### P0-06：StrategyProfileVersion 治理完整闭环（✅ 完成）

0 号位 8 项要求全部落地（契约 6.10）：

| 要求 | 实现 |
|---|---|
| ① Validate | 状态可发布/编码非空/引用合法/生效窗口/默认歧义（DEFAULT_CONFLICT） |
| ② Publish | 发布前强制校验无绕过；IsDefault=1 先 ClearDefaultFlag 再置位（防 UQ 冲突） |
| ③ 当前有效默认 PUBLISHED 解析 | `ResolveDefaultStrategyProfileVersionAsync(runType, asOf)`：0/null、1/返回、>1/抛歧义 |
| ④ 引用合法性 | REF_NOT_FOUND / REF_NOT_PUBLISHED |
| ⑤ RunType 匹配 | 经父表 `StrategyProfile.RunType` JOIN（新 `IStrategyProfileRepository`） |
| ⑥ EffectiveFrom/To | 校验窗口 + 解析默认时过滤 |
| ⑦ 默认唯一与歧义 | DB UQ 兜底 + Application 层跨 Profile 歧义检测（多候选抛异常，不随机取） |
| ⑧ Run 引用追溯 | `GetRunStrategyProfileTraceAsync` → RunStrategyProfileTrace DTO |

- 跨号位边界：未修改 2 号位 `ScheduleRunService`（FULL_SCHEDULE 默认解析 SQL 保留）；未 ALTER 冻结 DDL（RunType 经父表 JOIN）
- 红线 #5：新增接口前已先追加契约文档 6.10
- 验证：build 0 错误；全量 35/36（12 个新增 P0-06 测试全绿）
- 设计要点：换默认版本流程 = 先旧默认置 0（Update）→ 再发布新默认（IsDefault=1）；直接发布会触发 DEFAULT_CONFLICT 拒绝，保证任意时点"当前有效默认"唯一

### P0-07：DemandPriority 补 DueDate/IssueDate（✅ 完成）

- `DemandField` 枚举末尾追加 `DueDate`/`IssueDate`（序数 6/7，既有 0-5 不变，已存 JSON 整数序号不受影响）
- `DemandRecord` 加 `DateTime? DueDate`/`IssueDate`（init 属性）
- Matcher 三处 switch 补映射（ApplySort/ApplyThenSort/GetFieldValue）；日期经 `CompareValues` 的 `IComparable` 比较
- 日期 null 语义：与既有 `double? RemainingTimeHours` 一致，LINQ OrderBy 对 Nullable<T> Asc null 排最前 / Desc null 排最后，不引入自定义比较器（测试固化行为）
- Validator 新增 `SupportedDemandFields` 白名单（8 字段）——防枚举新增值未同步 Matcher switch 时，配置越过发布校验、运行期才抛 `NotSupportedException`
- 红线遵守：未引入 CalculationLayer/全局 Demand 池管理（2 号位职责）
- 验证：build 0 错误；DemandPriority + 治理发布相关测试 33/33；全量 42/43

### P0-08：运行生命周期与 Candidate 最小确认边界（✅ 完成）

0 号位要求全部落地（契约 6.11）：

- **ExpectedDomainKeysJson 冻结规则**：FULL_SCHEDULE → Domain 数 ≥ 1；RESCHEDULE 类（Candidate）→ 恰 1 Domain。空/缺失/非 JSON 数组/含空 DomainKey/数量越界 → 一律抛 `InvalidOperationException`，不静默降级
- **Candidate 最小确认**：`ConfirmCandidate`（CANDIDATE 校验 + DomainKey 非空 + 同域唯一预检 → 写 ActivatedAt/ActivatedBy，状态保持 CANDIDATE）+ `ActivateCandidate`（CANDIDATE → ACTIVE）。两步分离：确认仅记录，激活才采用
- **审计记录**：`CandidatePlanVersionId` 无独立列，记入 `GovernanceAuditLog.Remarks`（契约 6.11.4）
- **FAILED 恢复**：为 FAILED ScheduleRun **新建**一条 RUNNING（继承 RunType/StrategyProfileVersionId/ExpectedDomainKeysJson 基线），**新建前先校验继承基线合法性**（避免插入后再因基线不合法产生孤立 RUNNING）；旧 FAILED 记录绝不动
- **Run 引用追溯**：Run 维补齐（P0-06 为版本维），含冻结 ExpectedDomainKeysJson 与关联 PlanVersion 状态
- 跨号位边界：未修改 2 号位 `ScheduleRunService` / `SchedulingOrchestrator` / `DomainSchedulingJob` / `DomainLayerCoordinatorJob`；未 ALTER 冻结 DDL
- 验证：build 0 错误 0 警告；全量 65/66（23 个新增 P0-08 测试全绿）

## 四、测试验证汇总（提交时点）

### 单元测试

| 批 | 测试文件 | 数量 |
|---|---|---|
| P0-06 | `StrategyProfileVersionGovernanceTests.cs`（新增） | 12 |
| P0-08 | `RunLifecycleServiceTests.cs`（新增） | 23 |
| P0-07 | `DemandPriorityMatcherTests.cs`（+3）/ `DemandPriorityValidatorTests.cs`（新增 +4） | 7 |
| P0-04 | `FrozenStrategySnapshotProviderTests.cs`（改断言） | 2 |

### 集成测试（P0-05~P0-08，真实连库 + 真实仓储 + 真实服务编排）

| 文件 | 覆盖 | 数量 |
|---|---|---|
| `TestEnvironment.cs`（新增） | 环境探测辅助（Auth 库可达性 / ExpectedDomainKeysJson 列存在性；只读探测不碰库结构，红线 #6） | — |
| `GovernanceVersionServiceIntegrationTests.cs`（新增） | 发布闭环全链路（校验→发布→默认解析→Run 追溯）、P0-05 坏配置被拒无绕过、P0-06 引用未发布被拒、P0-07 校验器集成 | 4 |
| `RunLifecycleServiceIntegrationTests.cs`（新增） | P0-08 恢复新建 RUNNING 继承基线、Run 引用追溯完整链、Candidate 确认与激活落库 | 3 |

### 最终数据

```
dotnet build LPS.APS.sln   → 0 错误
全量测试                    → 73 总数 / 66 通过 / 6 跳过 / 1 失败
```

- **6 跳过** = 治理链路集成测试统一 `[SkippableFact]`，依赖 APS_Auth 库与 ExpectedDomainKeysJson 列；环境补齐后自动转绿无需改测试代码（详见六）
- **1 失败** = 既有 2 号位 `FiniteCapacitySolver` DI 问题（`完整排程流程集成测试`），非本次整改范围（详见六）
- 集成测试数据清理验证：测试后 ScheduleRun/PlanVersion 残留为 0

## 五、待 0 号位裁决 / 确认事项

### 1. P0-01 DDL 方案 A/B/C（阻塞 P0-03 收口）

- P0-03 装载层（SolverStrategy/CandidateGuardrail 真实版本来源）依赖 P0-01 裁决
- 确认后 3 号位立即收口：为 Solver/Candidate 建立真实版本来源，替换 `new SolverStrategyBlock()` 空对象
- 建议：优先给出方案选择，以便闭环 P0 全部八批

### 2. P0-01 方案确认后需同步答复

- 四块 JSON 字段（DemandPriorityJson / LockJson / SupplyJson / ProcurementJson）删除与否，与 DDL 裁决耦合，确认前 3 号位暂缓处理

## 六、提交 2 号位协同事项（不阻塞 3 号位复核，但影响集成测试全绿）

### 1. APS_Auth 库未部署（测试服务器 10.116.2.75 仅有 APS_Production / APS_Hangfire）

- 影响：治理发布/确认/激活/恢复强制写 `GovernanceAuditLog`（EF Core）无法落地 → 6 个治理链路集成测试 Skip
- 请求：2 号位部署 APS_Auth 库后，6 个 `[SkippableFact]` 自动转绿

### 2. 测试库 ScheduleRun 缺 `ExpectedDomainKeysJson` 列（冻结 DDL v5.1.2 §3.1 未迁移）

- 影响：P0-08 恢复/追溯链路无法读该列
- 请求：2 号位按冻结 DDL 迁移该列后，对应集成测试自动转绿

### 3. 既有 1 个失败：`完整排程流程集成测试` — SchedulingOrchestrator DI 依赖具体类

- 现象：`Unable to resolve service for type 'LPS.APS.Scheduling.Solvers.FiniteCapacitySolver'`
- 根因：`AddSchedulingServices()` 仅注册接口 `IFiniteCapacityScheduler → FiniteCapacitySolver`；`SchedulingOrchestrator` 构造函数依赖**具体类** `FiniteCapacitySolver`（`SchedulingOrchestrator.cs:41`）
- 归属：2 号位/1 号位层，3 号位按红线不擅自修改
- 建议：在 `AddSchedulingServices()` 补 `services.AddSingleton<FiniteCapacitySolver>()`，或 `SchedulingOrchestrator` 改依赖接口

## 七、跨号位边界遵守声明

- ✅ 未修改 2 号位运行状态执行逻辑：`ScheduleRunService` / `SchedulingOrchestrator` / `DomainSchedulingJob` / `DomainLayerCoordinatorJob`
- ✅ 未 ALTER 冻结 DDL（v5.1.2）：RunType 经父表 JOIN、Candidate 落点复用既有 ActivatedAt/ActivatedBy、每域唯一由既有 UQ 兜底
- ✅ 红线 #5（接口即契约）：新增接口前均已先追加契约文档 6.10 / 6.11 / 6.11.5
- ✅ 红线 #4（不盲目 First）：同域 ACTIVE 查询"无则 null"语义明确；默认解析按全集判定数量规则
- ✅ 红线 #6：集成测试环境探测为只读，不碰库结构

## 八、复核清单（供 0 号位逐项勾选）

- [ ] P0-01：六块来源映射表确认 + DDL 方案 A/B/C 裁决
- [ ] P0-02：子默认版本删除范围与保留项（StrategyProfileVersion.IsDefault）认可
- [ ] P0-03：结构层满足确认；装载层收口条件（P0-01 裁决后）认可
- [ ] P0-04：坏配置明确失败语义（缺失 vs 显式空块 `{}`）认可
- [ ] P0-05：两条发布路径强制校验、无绕过路径确认
- [ ] P0-06：8 项治理闭环实现逐项核对
- [ ] P0-07：DueDate/IssueDate 字段语义与 null 行为（测试固化）确认
- [ ] P0-08：运行生命周期 5 方法 + Candidate 两步确认边界确认
- [ ] 测试数据：73 总数 / 66 通过 / 6 跳过 / 1 失败 认可；环境缺口转交 2 号位
- [ ] 契约文档 6.10 / 6.11 与实现一致性确认

---

## 附：本次提交清单

| 提交 | 内容 |
|---|---|
| `a9de010` | P0 整改主体代码（41 文件 +4581/-210 行）：治理版本发布闭环与运行生命周期治理 |
| `de79f27` | P0-05~P0-08 集成测试补齐（5 文件 +754 行） |

工作树干净，无未提交改动。
