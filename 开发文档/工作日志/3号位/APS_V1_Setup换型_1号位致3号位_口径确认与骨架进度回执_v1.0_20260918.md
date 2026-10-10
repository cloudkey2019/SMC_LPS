# 回执：Setup换型 v1.2 口径确认 + 骨架已开工 —— 1号位 致 3号位

> 收件：3号位（规则/参数/运行生命周期治理）／抄 0号位（PM）、2号位（数据引擎+编排）
> 依据：3号位《Setup换型v1.2_致1号位_换型口径与待裁决项标注_v1.0_20260917》+ 0号位《未命名的Markdown文件19.md》（Q1/Q4 裁决）+ r13376（SetupTransitionRuleSnapshot 落地）
> 日期：2026-09-18
> 性质：口径确认 + 进度同步 + 信息差补齐（裁决项 3/4 已裁）+ 一项边界移交

---

## 一、§一 八条口径：全部确认，无异议

属性式废止 / EXACT→DEFAULT→0 三层查表 / 真实占用 / 换型键只用当前 Task 自身 OperationCode / 初始评价阶段即消费 / 生产日不重置设备状态 / 初始设备状态 Setup=0+Explanation / 五禁令——1号位 全部照办。

## 二、信息差补齐：裁决项 3/4 已由 0号位 裁决（20260917）

3号位 表中「裁决项 3（Q1 生产日）」「裁决项 4（Q4 Explanation 码）」**已裁**，见 0号位《未命名的Markdown文件19.md》：

| 项 | 0号位 裁决 |
|---|---|
| Q1 生产日 | = Resource Calendar **最大连续可用生产窗口**；不按自然日切、跨零点不断开、**不新增 ProductionDay 字段**；固定锚点只在窗口内部切割可移动段 |
| Q4 初始设备状态 | Setup=0，**不新增 ScheduleExplanationFact ReasonCode**；写 ExplainTrace/等价轻量追踪，内部类型可用 `INITIAL_SETUP_STATE` 符号锚；与「规则缺失降级」**严格分支分开** |

**裁决项 2 也已被 3号位 自己在 r13376 落地**：`FrozenStrategySnapshot.SetupTransitionRules`（第⑦块，`SetupTransitionRuleSnapshot`），随 RuleSetVersionId 锚冻结。我此前给 2号位 的 Q2（DTO 形态四问）随之全部有答案：List 容器 / `int?` MaterialId / EXACT+DEFAULT 混排按 RuleType 区分 / Dept+Stage 进 DTO。**Q2 关闭**。

剩余待裁：**仅裁决项 1（三预算参数数值）**，等 0号位；1号位 骨架已留参数位、未自定值。

## 三、1号位 已开工（§五 节奏第 1 条），骨架完成 + 17 测试全绿

`SetupOptimizer` 已按 v1.2 重写（旧属性式方法删除）：

1. `ResolveSetup(operationCode, resourceId, fromMaterialId?, toMaterialId, exactRules, defaultRules)`——三层确定性命中，方向性键；`SetupOutcome` 枚举区分 ExactHit / DefaultHit / SameProductZero / **InitialState / RuleMissing**（后两者按 0号位 裁决严格分支，不合并）。
2. `BuildRuleLookups(SetupTransitionRuleSnapshot?)`——第⑦块快照 → 运行时 EXACT/DEFAULT 字典适配；无效行防御跳过、同键取第一条不随机（§十）。
3. `BuildProductionWindows(slots)`——Q1 裁决的生产日窗口合并（相邻无间断合并、跨零点不断、班次断点切窗）。
4. 新增 `LPS.APS.Tests/Unit/SetupTransitionRuleTests.cs` 17 测试全绿，覆盖验收场景 S01/S03/S04/S05/S06/S08/S18 + Q4 分支区分 + Q1 窗口切分 + 快照适配。

**旧锚警告已遵守**：`DefaultSetupMinutes=30`/`SetupLookAheadSize=5` 在新代码零消费，等 0号位 终裁回填。

## 四、边界移交：`SchedulingContext.SetupAttribute` 删除不在 1号位 权限内

3号位 §四要求「删除 `SchedulingContext.SetupAttribute`」——该字段位于 **LPS.APS.Core/Models/Scheduling/SchedulingContext.cs**，属 Core 层（2号位 辖区）。1号位 边界仅 Scheduling+Tests，**不能删**。现状：Solver 五阶段流程从未消费该字段（仅旧 SetupOptimizer 死代码引用，本次重写后已零引用）。请 2号位 按 §21「物理字段兼容废弃」口径处理（标记废弃或删除），1号位 侧已无任何读取。

## 五、剩余依赖与接线计划

| 依赖 | 责任 | 状态 |
|---|---|---|
| 第⑦块 `SetupTransitionRules` 按 Domain 裁剪投影进 `DomainSolveRequest` | 2号位 | ⏳ 头号阻塞（形态已定，只差通道） |
| 三预算参数数值（有界搜索/最大邻域尝试/单生产日保护阈值）+ ParameterSetVersion 承载 | 0号位 定值、3号位 治理 | ⏳ 骨架留位 |
| ExplainTrace 载体（`DomainSolveResult` 现只有 ExplanationFacts） | 2号位 定契约（或 1号位 先用 EvidenceJson 降级） | ⏳ |

2号位 通道落地后，1号位 接线顺序：Phase1 `BuildRuleLookups` 建约束上下文 → Phase2 初始 Resource/时间槽评价消费 Setup（含插入对后继 Setup 影响，§12）→ Phase4 邻接重算改口径 → Phase5 后接夜间 FULL 有界序列优化（窗口切分已就绪）。

## 六、附带提醒（抄 2号位）

最新代码下 4 个 Integration 失败均与 1号位 无关，请对应责任人看一眼：
- `RealSchedulingIntegrationTest`：「Task表没有数据！PeggingOrchestrator可能未正确INSERT」——r13371 改过 PeggingOrchestrator，请 2号位 确认；
- `GovernanceVersionServiceIntegrationTests` ×2：APS_Auth.dbo.User 缺测试 UserId 的 FK 冲突（老问题）；
- `ContinuityRedLineIntegrationTest`：单跑通过、全量跑失败——顺序依赖/DB 残留，flaky。

1号位 侧单测全绿（本轮新增 17 个），Scheduling 编译干净。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）
