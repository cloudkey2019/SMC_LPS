# APS V1 · BatchPolicy 第⑧块 + Direction(AUTO) 消费契约知会（3号位）

**版本**：v1.0
**日期**：2026-10-07
**发送人**：3号位（规则参数治理、FrozenStrategySnapshot 装配 Owner）
**接收人**：1号位（Solver 消费落地）
**抄送**：0号位（PM，裁决备查）、2号位（投影/装配）、4号位（前端备查）、5号位（查询侧备查）
**性质**：**契约知会件**——登记 0号位 2026-10-07《批量正倒排（Batch Policy + Direction）裁决》落码后的 1号位 消费契约；**不改任何冻结签名（红线 #5）**
**依据**：
- 0号位《APS V1 本轮七治理项核实 BatchDirectionRouting 待拍板回执 0号位致3号位》v1.0（2026-10-07）
- 05 契约 §7.0.2（SolverStrategyMode Auto 登记）+ §6.10.5（BatchPolicy 子块登记，3号位 2026-10-07 补）
- 代码已落：`SolverStrategyMode.Auto` + `SolverStrategyModeMap.ToDirection`；`FrozenStrategySnapshot.BatchPolicy`（第⑧块）

---

## 〇、速览

```
① Direction    SolverStrategyMode += Auto → SchedulingDirection "AUTO"（1号位 按求解上下文自决方向，不按 OrderType 硬编码）
② 第⑧块        FrozenStrategySnapshot.BatchPolicy = List<BatchPolicyRuleSnapshot>（发布时从主题表 TaskSplitRuleConfig 投影）
③ 装载语义      装载 fail-open（缺子块/空/损坏 → 空列表）；消费缺策略 fail-closed（BATCH_POLICY_MISSING）归 1号位
④ 匹配粒度      MaterialId + ProductionDepartmentId（NULL 部门 = Material 级默认）
```

---

## 一、Direction：SolverStrategyMode.Auto

`SolverStrategyMode { Forward, Backward, Mixed, Auto }`；`SolverStrategyModeMap.ToDirection` 映射：

| Mode | SchedulingDirection | 语义 |
|---|---|---|
| `Forward` | `"FORWARD"` | 正排 |
| `Backward` | `"BACKWARD"` | 倒排 |
| `Mixed` | `"MIXED"` | 混合正/倒排 |
| `Auto`（**本轮新增**） | `"AUTO"` | 1号位 按当前求解上下文决定最终方向（不按 OrderType 硬编码） |
| 未知枚举（防御） | `"BACKWARD"` | 等效 Backward（与历史行为一致） |

**消费约定**：1号位 `PhaseTwoInitialScheduler` 读到 `"AUTO"` 时，依据瓶颈/交期/物料可用等求解上下文自决最终方向，非全正排/全倒排二选一，与既有「正排/倒排/混合同一 Solver 内决策」口径兼容（冻结《有限产能排产与滚动 90 天计划业务说明》§11、《核心排产全流程走查》§27）。

---

## 二、第⑧块：FrozenStrategySnapshot.BatchPolicy（消费契约）

**来源**：`ParameterSetVersion.ContentSnapshotJson.BatchPolicy` 子块（发布时把主题表 `TaskSplitRuleConfig` 中 IsActive=1 且处于生效区间内的规则投影为快照，随版本冻结）。

**类型**：`List<BatchPolicyRuleSnapshot>`，字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| MaterialId | int | 物料 |
| ProductionDepartmentId | int? | 生产部门（NULL = Material 级默认） |
| MinExecutionBatchQty | decimal? | 硬最小执行批量 |
| MaxExecutionBatchQty | decimal? | 硬最大执行批量（NULL = 无硬上限） |
| PreferredBatchQty | decimal? | 软偏好切点批量 |
| AllowSplit | bool | 是否允许拆分 |
| AllowMerge | bool | 是否允许合并 |
| MaxOptimizationSplitCount | int? | 仅限优化性拆分搜索的最大拆分次数 |
| MaxBatchCandidates | int? | 单问题最多评估的候选数 |
| BottleneckSplitStrategy | string? | `PREFER_SPLIT` / `PREFER_MERGE` |
| NonBottleneckStrategy | string? | `PREFER_LARGE_BATCH` / `PREFER_SMALL_BATCH` |

**匹配粒度**：MaterialId + ProductionDepartmentId；NULL 部门规则 = Material 级默认（兜底），优先命中精确部门规则。

---

## 三、fail-closed 约定（BATCH_POLICY_MISSING，归 1号位 消费侧）

- **装载 fail-open**：第⑧块缺失/为空/JSON 损坏 → `BatchPolicy = 空列表`，不阻断 Run 装载（与第⑦块 Setup 规则同轨；区别于 ①②③④⑤⑥ 六块 fail-closed）。
- **消费 fail-closed（归 1号位）**：当 1号位 对某 Material(+Dept) 需做批量拆分/合并决策、但 `BatchPolicy` 中无匹配规则时 → 报 **`BATCH_POLICY_MISSING`**（fail-closed，**拒绝静默用默认拆批**），由上游/用户显式补配 TaskSplitRuleConfig 规则后重跑。

> 契约边界：`BATCH_POLICY_MISSING` 是 1号位 消费侧信号，第⑧块子块本身**不承载**该信号位（3号位 装配侧只如实投影 active+生效区间规则）。

---

## 四、与既有落码对齐（3号位 已落码索引）

- `Core/Dto/FrozenStrategySnapshot.cs`：`SolverStrategyMode.Auto`、`FrozenStrategySnapshot.BatchPolicy`、`BatchPolicyRuleSnapshot`
- `Core/Dto/DomainSolveRequest.cs`：`SolverStrategyModeMap.ToDirection(Auto)="AUTO"`
- `Application`：`SolverStrategyValidator` 放行 Auto；`GovernanceVersionService` 发布时投影 BatchPolicy 进 ContentSnapshotJson；`FrozenStrategySnapshotProvider` 反序列化第⑧块
- 主题表 `TaskSplitRuleConfig` 治理 CRUD（3号位，4 端点）已落码；`dotnet build` 0 错、投影/快照单测绿

---

## 五、回执表

| 问 | 1号位 |
|---|---|
| §一 Direction AUTO 消费语义认可 | （待回执） |
| §二 第⑧块字段/匹配粒度认可 | （待回执） |
| §三 BATCH_POLICY_MISSING fail-closed 认领（消费侧） | （待回执） |

---

**发送人**：3号位 ｜ **日期**：2026-10-07