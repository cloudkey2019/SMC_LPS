# APS V1 3号位 与 2号位 Snapshot 联调记录（3号位单侧就绪声明）

> 版本：v1.0
> 日期：2026-08-22
> 归属：3号位（交付物 11 — 与 2号位 Snapshot 联调记录）
> 依据：《APS_V1_3号位开发任务清单_v1.0_20260817.md》§四 #11（对应任务 B-3~B-5 + C-4）、《APS_V1_3号位交付契约_v0.2_20260817.md》§六（六项联调检查点）
> 性质：**3号位 单侧就绪声明**——3号位 侧实现落点 + 六项检查点单侧证据 + 待 2号位 配合/回执点。2号位 回执后，本记录升级为完整联调记录。

---

## 一、背景与定位

交付物 11 = 与 2号位 Snapshot 联调记录，检查点定义于契约 v0.2 §六（0号位统一确认后的定稿契约，**不再等待 2号位 二次裁决契约**；2号位 回来后仅做代码联调）。本文件为 3号位 侧就绪声明：

- 3号位 已交付能力与证据（可单侧验证的部分）；
- 六项检查点中**属 3号位 侧可自证的项** → 直接给出证据；
- 属 2号位 实现/配合的项 → 明确列出 3号位 提供的输入与待回执点；
- SolverStrategy/CandidateGuardrail 两块依赖 0号位 DDL 方案 A/B/C 裁决（检查项 12），本记录标注为**部分待裁决**，不阻塞四块联调。

## 二、3号位 侧交付落点

| 能力 | 落点 | 状态 |
|---|---|---|
| 六块 DTO（含头部三 VersionId） | `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs` | ✅ 完成（B-1，契约 v0.2 §二） |
| Provider 接口 | `IFrozenStrategySnapshotProvider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, ct)`（Core 接口） | ✅ 完成（B-2） |
| Provider 装配实现 | `LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs` | ✅ 完成（B-3） |
| 冻结语义（按指定 VersionId，不重选 Default、不逐笔 RPC、不漂移） | Provider 装载路径（B-4） | ✅ 完成 |
| Snapshot 缓存（Cache Key 含 VersionId、不污染、Run 内不刷新） | B-5（提交 `3d04d7c`） | ✅ 完成 |
| Demand 排序 Fixture/示例（供 2号位 联调） | 阶段 C（C-4） | ✅ 完成 |
| SolverStrategy / CandidateGuardrail 真实来源 | Provider 内两块现为**空对象**（L101/L104） | 🟡 待 0号位 DDL 裁决（P0-02） |

## 三、契约 v0.2 §六 六项检查点逐项自评

| # | 检查点 | 3号位 单侧证据 | 状态 |
|---|---|---|---|
| 1 | 按 Run 已冻结的指定 VersionId 一次装载 + 内存执行；不重选 Default、不逐笔 RPC、不因后续发布漂移 | `GetFrozenStrategySnapshotAsync` 仅以 `strategyProfileVersionId` 为入参一次装配六块；Provider 无任何逐笔/按 Domain 再查逻辑；B-5 缓存含 VersionId 保证同 Run 不漂移 | ✅ 3号位 侧满足（**最终行为由 2号位 Run 启动调用验证**，待 2号位 回执） |
| 2 | 三 VersionId 来自同一 StrategyProfileVersion；一 Run 所有 Domain 一致 | DTO 头部显式 `StrategyProfileVersionId / RuleSetVersionId / ParameterSetVersionId`（同一包内装配，契约 C2-2）；集成测试断言三者一致 | ✅ 3号位 侧满足 |
| 3 | 默认版本语义：未显式指定时按 RunType 取唯一无歧义默认 PUBLISHED；**具体 SQL 归 2号位实现，不冻结** | 3号位 侧默认治理就绪：StrategyProfile `IsDefault=1 AND PUBLISHED` 唯一不变量（A-6，UQ 兜底） | ✅ 3号位 侧提供默认治理；**取默认 SQL 由 2号位 实现**，待 2号位 回执 |
| 4 | Cache Key 含 StrategyProfileVersionId + 不污染 + Run 内不刷新；字符串不冻结 | B-5 实现：缓存键含 VersionId，版本间不污染，Run 内不刷新（提交 `3d04d7c`）；具体字符串由 2号位 决定（契约明确不冻结） | ✅ 3号位 侧满足语义；待 2号位 对齐缓存键 |
| 5 | 六块 + PlanningYield ↔ 冻结 4.2 三块覆盖无遗漏；三 VersionId 元数据显式 | DTO 六块齐全；PlanningYield 归属 ④ ProcurementBlock.PlanningYields（C2-5）；三 VersionId 头部显式 | ✅ 3号位 侧满足（Solver/Candidate 内容待裁决，结构已齐） |
| 6 | 2号位 抽 FrozenFactParameters 转交 5号位；3号位 DTO 不依赖 5号位类型；不新增表/版本号 | 契约明确"**不提供** `Snapshot.ToFrozenFactParameters()`"（避免 3号位→5号位 隐形依赖）；抽取 Mapping 属 2号位 主流程职责 | ✅ 3号位 侧已按契约约束实现（**2号位 执行抽取**，待 2号位 回执） |

## 四、3号位 单侧测试证据

```
dotnet test LPS.APS.Tests  --filter "FullyQualifiedName~FrozenStrategySnapshotProvider"
```

- `FrozenStrategySnapshotProviderTests.cs`（Unit）：四块（DemandPriority/Lock/Supply/Procurement）按 VersionId 重放 + **具体值断言**（非 NotNull）；三 VersionId 一致。
- `FrozenStrategySnapshotProviderIntegrationTests.cs`（Integration）：四块**缺失/损坏一律抛异常**（P0-04），与契约"Run 启动失败语义"一致。
- B-5 快照缓存（提交 `3d04d7c`）：缓存命中/版本隔离/Run 内不刷新。
- C-4 Demand 排序 Fixture：供 2号位 联调 Demand 排序的示例 Snapshot（阶段 C 验收 R04~R06 覆盖）。

> 注：Solver/Candidate 两块的具体值重放断言（非 NotNull）依赖 0号位 DDL 方案 A/B/C 裁决（检查项 12），裁决后按《P0-02执行蓝图》补齐，本记录随之更新。

## 五、待 2号位 配合/回执点

| # | 事项 | 说明 |
|---|---|---|
| 1 | Run 启动调用 Provider 一次装载验证 | 检查点 1 的端到端行为（2号位 `ScheduleRunService` 侧） |
| 2 | 默认版本取数 SQL 实现 | 检查点 3（契约明确归 2号位，不冻结 SQL） |
| 3 | 缓存键字符串对齐 | 检查点 4（字符串不冻结，语义已满足） |
| 4 | `FrozenFactParameters` 抽取 Mapping | 检查点 6（2号位 主流程编排职责，C2-6） |
| 5 | Solver/Candidate 真实来源裁决 | 0号位 DDL 方案 A/B/C（与本记录四块部分解耦，不阻塞四块联调） |

## 六、状态

- [x] 3号位 单侧就绪声明（本文件，v1.0）
- [ ] 2号位 六项检查点回执 → 升级为完整联调记录（交付物 11 闭环）

---

*本声明为交付物 11 的 3号位 单侧部分；与《第三轮复审验收提报》§六（跨号位边界遵守）一致：未修改 2号位 `ScheduleRunService`/`SchedulingOrchestrator`/`DomainSchedulingJob`/`DomainLayerCoordinatorJob`，未 ALTER 冻结 DDL。*
