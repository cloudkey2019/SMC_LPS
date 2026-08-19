# APS V1 3号位代码开发前置准备清单

**版本**：v1.0
**日期**：2026-08-17
**适用对象**：3号位（及其开发 AI）
**性质**：工作清单已完善齐全后的"开发前置执行步骤"，按顺序执行；完成后进入阶段 A~F 开发
**上位依据**：《APS_V1_3号位工作条目清单_v1.0_20260817.md》（55 条 + 附一 DoD + 附二约束 + 附三裁决）、《APS_V1_3号位视角_23和35接口缺口清单_v1.2_20260817.md》（最终裁决）

---

# 一、背景与结论

3号位工作清单经三级验证已完善齐全：

1. **附二**：55 条 × 实施包 31 章双向核对（正确性通过、完整性通过）
2. **8.17 权威复核**：55 条 × README / 关键接口冻结 / 2号位 v1.1 / 5号位实施包（3↔2、3↔5 一致）
3. **0号位裁决 v1.2**：开放点闭合、补充项修正/删除，无剩余业务缺口

因此需求/业务理解层面不再需要分析，**进入实现与联调**。但写代码前须按本文件执行前置准备。

---

# 二、前置准备步骤

## 第 0 步：现状盘点（必做第一步，约 0.5 天）

**目的**：确定六张治理表代码侧现状，避免重复造轮子；对齐 README"1/3/4/5 号位从零组织"，但以实际代码为准。

**输入**：
- 冻结 DDL：`APS_数据库表结构设计_v5.1.2_冻结对齐版.sql`（六表 §3.10，8731-8866 行；ScheduleRun.StrategyProfileVersionId / ExpectedDomainKeysJson 字段；v5.0 已过时）
- 现有代码：LPS.APS.Engine / Core / Application / Web

**动作**：
- 扫代码库：六表（RuleSet / RuleSetVersion / ParameterSet / ParameterSetVersion / StrategyProfile / StrategyProfileVersion）是否已有实体/仓储/服务/控制器
- 确认 DB：APS_Production 六表是否已建；ScheduleRun 是否含 StrategyProfileVersionId、ExpectedDomainKeysJson 字段
- 确认现有枚举：RuleSetVersion / ParameterSetVersion 状态枚举（若已有 DRAFT/PUBLISHED/RETIRED 之外的不同枚举，以冻结 DDL 为准，不为命名重建表）

**产出**：《3号位复用/从零盘点结论》——每张表/每类代码标注"复用外壳 or 从零新建"

**完成判据**：盘点表完成，无遗留未知；与 2号位现状不冲突

## 第 1 步：工程基线（约 0.5 天）

**目的**：搭好 3号位开发与测试所需的工程骨架。

**动作**：
- 复用 xUnit 测试工程（实际已存在 `LPS.APS.Tests`，CLAUDE.md"暂无测试项目"描述过时，不重复搭建）
- 确认 3号位服务落位与 DI 扫描命名空间（Application.Services → Scoped 自动扫描；治理仓储 → 3号位自写扩展方法注册）
- 确认 `appsettings.json` 四库连接串（APS_Production / MES_Integration / APS_Auth / APS_Hangfire）
- 确认访问方式：六张治理表在 APS_Production → **Dapper**（仅 Auth 库用 EF Core）

**产出**：xUnit 工程可跑通空测试；DI 注册骨架就位

**完成判据**：`dotnet build` 通过 + 空测试 green

**执行结果（2026-08-17）**：
- ✅ 测试工程复用：`LPS.APS.Tests` 已存在且基础设施完整（xUnit 2.6.2 + Moq 4.20.70 + FluentAssertions 6.12.0 + coverlet + Microsoft.NET.Test.Sdk），已加入 sln；含 v5.1.2 架构集成测试（RealSchedulingIntegrationTest），**无需新建**
- ✅ 连接串确认：`appsettings.json` 四库齐全（APS=10.116.2.75/APS_Production、ODS=10.116.2.73/MES_Integration、Auth=10.116.2.75/APS_Auth、Hangfire=10.116.2.75/APS_Hangfire）
- ✅ Dapper 访问确认：六表治理走 APS 库 Dapper（`DatabaseConnectionManager` + `DatabaseId.APS`）；仅 Auth 库 EF Core（`AuthDbContext`）
- ✅ DI 落位决策：CLAUDE.md 宣称 `Engine.Repositories.APS` 自动扫描，但实际 `DatabaseServiceExtensions.cs` 仅扫 Auth+Pegging —— 经决策，3号位治理仓储采用**自写扩展方法注册**（不修改 2号位框架代码）
- ⚠️ 阻塞项（1号位，非 3号位）：Scheduling 项目 16:19 写入的 `FiniteCapacitySolver.cs` 引用不存在的 `LPS.APS.Scheduling.Models` 命名空间与 `ScopeConstraint` 类型（全仓无定义），阻塞 Application/Web/Tests 编译。分项验证 Core/Engine/Shared/BusinessRules **编译零错误**；全量 `dotnet build` + 空测试 green 待 1号位补齐 Models 后重试

## 第 2 步：接口/DTO 定稿（与 2号位对齐，约 1 天）

**目的**：把裁决 A / C 的接口语义落为可联调的精确契约。

**动作**：
- `FrozenStrategySnapshot` 六块配置（DemandPriority / Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail）精确字段名定稿（按实施包语义 + 现有工程风格；类名不强制）
- `GetFrozenStrategySnapshotAsync(long strategyProfileVersionId, CancellationToken ct)` Provider 落地位置（**3号位提供，2号位 Run 启动时调用一次**）
- `FrozenFactParameters` 投影定义：从 Snapshot 抽取供 5号位的最小参数子集（DefaultLT / Margin / Offset / 必要采购参数）
- Cache Key 约定：必须含 VersionId；Run 中不刷新

**产出**：《3号位交付契约 v0.1》（DTO 定义 + Provider 签名 + 投影映射），提交 2号位联调前确认

**完成判据**：契约 v0.1 双方确认无异议（不新增表、不新增独立版本号）

## 第 3 步：开发任务拆解（约 0.5 天）

**目的**：把 55 条拆成可执行、可验收的开发任务，设阶段门。

**动作**：
- 55 条按阶段 A~F 拆为任务（A1~A7 → B1~B5 → C1~C5 → D1~D9 → E1~E8 → F1~F8），每任务标注：依赖、验收场景（R01~R22 映射）、对应 DoD 项
- 阶段门：A 以 R01/R02 为门进 B；B 以 R03 为门进 C；C 以 R04~R06 为门进 D；D 以 R07~R13 为门进 E；E 以 R14~R17 为门进 F；F 以 R18~R22 为门
- 对照 12 项交付物建检查点

**产出**：开发任务清单（任务级，含依赖与验收）

**完成判据**：每个任务有明确验收；阶段门定义清楚

## 第 4 步：TDD 启动（与阶段 A 开发并行）

**目的**：以测试先行进入开发，保障 80%+ 覆盖。

**动作**：
- 阶段 A 先写：R01（发布 RuleSetVersion 历史不可覆盖）、R02（发布 ParameterSetVersion 新 Run 可引用、旧 Run 不变）
- 测试结构：AAA；命名描述行为
- 每阶段推进时，对应验收场景转为自动化用例

**产出**：阶段 A 测试用例集（RED）

**完成判据**：R01 / R02 测试先红后绿

---

# 三、阶段门与开发顺序（固定）

```text
阶段A 六表治理       → 门 R01/R02
阶段B Snapshot       → 门 R03
阶段C Demand Priority → 门 R04~R06
阶段D Supply/Lock/Procurement → 门 R07~R13
阶段E Solver Strategy → 门 R14~R17
阶段F Run/Candidate 生命周期 → 门 R18~R22
→ 与 2号位 Snapshot 联调（交付物 11）
→ 与 1号位 Solver Strategy 联调（交付物 12）
```

**红线贯穿全程**：不建规则平台/DSL/插件（#48）；性能一次装载（#51）；联调红线（#53）；DoD 17 项（#54）。

---

# 四、与既有文档关系

| 文档 | 关系 |
|---|---|
| 《APS_V1_3号位工作条目清单_v1.0_20260817.md》 | 本文件的操作对象：55 条按阶段拆解执行 |
| 《APS_V1_3号位视角_23和35接口缺口清单_v1.2_20260817.md》 | 本文件的接口依据：裁决 A/C 在此落地为契约 v0.1 |
| 《APS_V1_3号位规则参数与运行生命周期开发实施包_v1.0_20260814.md》 | 上位依据：字段/语义以实施包为准 |

---

# 五、状态跟踪

- [x] 第 0 步 现状盘点 → 《APS_V1_3号位现状盘点结论_v1.0_20260817.md》（完成；发现文档口径问题 6 处已按冻结 DDL v5.1.2 修复）
- [~] 第 1 步 工程基线 → xUnit 工程 + DI 骨架（部分完成：复用/连接串/DI 决策确认；全量 build + 空测试 green 待 1号位补齐 Scheduling Models）
- [x] 第 2 步 接口/DTO 定稿 → 《APS_V1_3号位交付契约_v0.2_20260817.md》（0号位统一代 2号位/5号位确认，定稿）
- [x] 第 3 步 任务拆解 → 《APS_V1_3号位开发任务清单_v1.0_20260817.md》（完成，任务级拆解 + 阶段门 + 12 项交付物检查点）
- [x] 第 4 步 TDD 启动 → R01/R02 RED→GREEN（RuleSetVersionPublishTests 3 用例 + ParameterSetVersionPublishTests 3 用例，共 6 用例全绿：失败 0 / 通过 6）
- [ ] 阶段 A 开发与验收（R01/R02 绿）→ 门
- [ ] 阶段 B~F 依次推进（对应 R03~R22 绿）→ 门
- [ ] 与 2号位 / 1号位联调记录

---

*本文件由 0号位裁决（2026-08-17）后生成，作为 3号位从文档阶段进入开发阶段的操作入口。*
