# 回执：P1-02 Solver 策略冻结参数 —— 冻结已覆盖 7/9，缺口在投影面，非「3号位 未冻结」

> 收件：0号位（PM）／2号位（数据引擎+编排）／1号位（排程算法）
> 发件：3号位（策略参数治理：冻结 Snapshot 定义 + 发布前校验）
> 依据：P1-02 派单（「FiniteCapacityParameters 仅 4 项，其余 9 类策略硬编码在 1号位」）
> 日期：2026-09-14

---

## 一、结论一句话

P1-02 的「3号位 未冻结」前提**不成立**：3号位 的 `FrozenStrategySnapshot`（2↔3 契约）早已带齐 9 类里 7 类的类型化参数、默认值与发布前校验（P0-02 已收口真实来源）。**1号位 硬编码的真正根因是 2号位↔1号位 的 `FiniteCapacityParameters` 只有 4 字段、投影时把其余参数全丢了**——1号位 拿不到冻结值，只能照旧写死。

- 3号位 侧真缺：仅 2 类（早做惩罚、稳定性偏好）+ `AllowSplit` 布尔真源 + 待 0号位 裁决的 `Bottleneck Override` 语义。
- 2号位 侧缺口：`FiniteCapacityParameters` 扩展 + ⑤⑥ 全字段投影。
- 1号位 侧缺口：用收到的参数替换硬编码。
- **冻结默认值 = 1号位 当前硬编码值，故必须先由 1号位 交「硬编码值清单」**（见 §五）。

---

## 二、证据链（3 处一锤定音）

| # | 文件 | 归属 | 现状 |
|---|---|---|---|
| 1 | `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs:22-23` | 3号位（2↔3 契约） | **六块完整**：⑤`SolverStrategyBlock`（Mode/AllowMerge/MaxIterations/BottleneckMode/OnTimeTarget/Split/Setup/StageOverlap）＋⑥`CandidateGuardrailBlock`（NormalMs/SoftMs/LocalHardMs/…/MaxPropagationRounds=10/SplitAlternatives=3）全在 |
| 2 | `LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs:110,113` | 3号位（装配） | P0-02 已从 `ParameterSetVersion.ContentSnapshotJson` **真实装载** ⑤⑥（非空对象、缺块即装载失败） |
| 3 | `LPS.APS.Core/Dto/DomainSolveRequest.cs:206-212` ＋ `PeggingOrchestrator.cs:191-197` | 2号位（1↔2 契约+编排） | `FiniteCapacityParameters` 仅 `AllowSplit/AllowMerge/MaxIterations/SchedulingDirection` 4 字段；投影只摘这 4 个，**BottleneckMode/OnTimeTarget/Setup/StageOverlap 与整块 CandidateGuardrail 全部丢弃** |

---

## 三、九策略 × 三方对照

| # | P1-02 策略 | 3号位 冻结(⑤/⑥) | FiniteCapacityParameters | 1号位 硬编码 | 缺口归属 |
|---|---|---|---|---|---|
| 1 | Dynamic Bottleneck | ✅ `BottleneckMode`(DynamicBottleneckMode) | ❌ | `PhaseThreeDiagnostics.cs:92` `>0.85m`、`:141` `>0.90m` | 2号位投影 |
| 2 | Bottleneck Override | ⚠ 疑=`BottleneckMode` ForceAnchor/PreferAnchor | ❌ | 未见明确魔数 | **0号位裁决语义** |
| 3 | On-time Target | ✅ `OnTimeTarget`(TargetPercent+IsPrimaryObjective) | ❌ | 未见 TargetPercent 魔数 | 2号位投影 |
| 4 | Setup 策略 | ✅ `Setup`(Dimensions+DefaultSetupMinutes=30+LookAhead=5) | ❌ | `SetupOptimizer.cs:59` default=30min | 2号位投影 |
| 5 | Split 上限 | ✅ `Split`(MaxOptimizationSplitCount=3+…) | ⚠ 仅 `AllowSplit=Max>1` | `PhaseFourLocalRepair.cs:1068` Split候选≤3 | 2号位投影 |
| 6 | Stage overlap | ✅ `StageOverlap`(AllowOverlap+TransferBach+Threshold Qty/Percent) | ❌ | 未见量化魔数 | 2号位投影 |
| 7 | Candidate Guardrail | ✅ ⑥整块(10轮/3/30%/5次/5TopN…) | ❌ 整块丢弃 | `PhaseFourLocalRepair.cs:106` `0.3m` 警戒线、`:1068` ≤3 | 2号位投影 |
| 8 | 早做惩罚 | ❌ **无此参数** | ❌ | 未定位到（疑隐含于算法结构） | **3号位补冻结** |
| 9 | 稳定性偏好 | ❌ **无此参数** | ❌ | `PhaseFiveCompression.cs:16,44`「尽量保持计划稳定」（无量化魔数） | **3号位补冻结** |

附带漂移（需一并收口）：

- `FiniteCapacityParameters.AllowSplit`（`PhaseFourLocalRepair.cs:802` 消费）在⑤里**无对应布尔**；`PeggingOrchestrator.cs:193` 以 `Split.MaxOptimizationSplitCount > 1` 反推，属歪打正着、非干净契约。
- ⑤`SolverStrategyBlock.Mode` 是枚举 `SolverStrategyMode`，1↔2 `FiniteCapacityParameters.SchedulingDirection` 是字符串（`PhaseTwoInitialScheduler.cs:27` 消费）；映射在 `PeggingOrchestrator` 内临时变量，需在 2号位 投影处正式化（Mode↔"FORWARD/BACKWARD/MIXED"）。
- `MaxIterations` 当前 1号位 无消费点（grep 未命中），但作为求解迭代上限仍应保留冻结、待 1号位 确认是否纳入求解循环。

---

## 四、三方工作拆分与红线

| 号位 | 干什么 | 产出 | 红线 |
|---|---|---|---|
| **3号位** | 补 `SolverStrategyBlock` 缺失项（早做惩罚/稳定性偏好 + `AllowSplit` 布尔真源 + BottleneckOverridden 语义经 0号位 裁决后定点），定 frozen 默认值（=1号位 现值）、扩 `SolverStrategyValidator` | 冻结参数 spec + 参数集版本变更申请 | 只动 2↔3 契约与其校验，**不动 2号位 编排/1号位 算法** |
| **2号位** | 扩 `FiniteCapacityParameters`（1↔2 契约），把 ⑤+⑥ 全字段投影进 `PeggingOrchestrator` | DTO 变更 + 投影实现 | 1↔2 契约归 2号位，3号位 只提交字段清单不改其代码 |
| **1号位** | 用收到的参数替换硬编码（0.85/0.90/0.3/≤3/30min/稳定性…） | 去硬编码 | 消费 `StrategySnapshot.Parameters`，不自行再设默认 |
| **0号位（PM）** | 裁决 §七 三项语义 | 裁决 | — |

---

## 五、请 1号位 交「当前硬编码值清单」（冻结默认值的唯一真源）

3号位 冻结的默认值**必须等于 1号位 当前硬编码值**，否则「消费冻结策略」会静默改行为。3号位 已 grep 定位到的现值：

| 策略 | 现值 | 出处 |
|---|---|---|
| 瓶颈识别阈值 | `0.85m`（Load/AvailableCapacity）、`0.90m`（ResourceUtilization） | `PhaseThreeDiagnostics.cs:92,141` |
| 传播警戒线 | `0.3m`（受影响 Task 30%） | `PhaseFourLocalRepair.cs:106` |
| Split 候选上限 | `3`（2 分/3 分） | `PhaseFourLocalRepair.cs:1068` |
| Setup 默认时长 | `30min`、LookAhead `5` | `SetupOptimizer.cs:59`、FrozenStrategySnapshot 默认 |
| Candidate 传播轮数 | `10`、Repair 尝试 `5`、TopN `5` | `CandidateGuardrailBlock` 默认 |

**仍需 1号位 枚举现值（3号位 未定位到）**：

1. **早做惩罚**（是否量化惩罚系数/比例？还是算法结构既定、不可配？）
2. **稳定性偏好**（PhaseFive「保持计划稳定」的稳定权重/允许偏离幅度？当前是隐含还是仅有硬编码阈值？）
3. **On-time Target**（交期达成率目标值 + 是否真作为主优化目标压制 Setup/WIP/Utilization）
4. **Stage overlap**（AllowOverlap 默认、TransferBatchQty/ThresholdQty/ThresholdPercent 现值）
5. **Bottleneck Override**（覆盖锚资源的现值/触发条件；与 DynamicBottleneckMode 的关系）

> 请 1号位 按上表以「当前代码实际值」回填；3号位 拿到清单后当日冻结、2号位 接投影、1号位 换硬编码为参数读取，形成闭环。

---

## 六、请 2号位 回执（FiniteCapacityParameters 扩展 + 投影）

| 问 | 2号位 请确认 |
|---|---|
| 1 | 是否同意扩展 `FiniteCapacityParameters`（1↔2 契约）承载 ⑤⑥ 全字段（BottleneckMode/OnTimeTarget/Split/Setup/StageOverlap + CandidateGuardrailBlock）？ |
| 2 | 投影点 `PeggingOrchestrator.cs:191-197` 是否由 2号位 补齐（含 Mode↔SchedulingDirection 字符串正式映射 + AllowSplit 布尔真源）？ |
| 3 | 预计何时可接投影（依赖 3号位 冻结 spec 与 1号位 消费约定）？ |

---

## 七、请 0号位（PM）裁决（3 项语义）

| # | 问题 | 选项 | 影响 |
|---|---|---|---|
| 1 | Bottleneck Override 是否独立于 Dynamic Bottleneck？ | □ =`DynamicBottleneckMode` ForceAnchor/PreferAnchor 已覆盖 | 3号位 不再新增字段，仅冻结枚举值 |
| | | □ 独立维度，需新增 `BottleneckOverride` 参数 | 3号位 补字段 + 校验 |
| 2 | 早做惩罚是否需量化参数？ | □ 可配置（补 `EarlyStartPenalty` 字段） | 3号位 冻结 + 1号位 消费 |
| | | □ 算法既定行为、不配 | 3号位 不冻结（从 P1-02 九清单移除） |
| 3 | 稳定性偏好是否需量化参数？ | □ 可配置（补 `Stability` 字段，如稳定权重/允许偏离） | 3号位 冻结 + 1号位 消费 |
| | | □ 算法既定行为、不配 | 3号位 不冻结 |

---

## 八、回执表

| P1-02 问 | 3号位 回执 |
|---|---|
| 3号位 是否冻结完整参数？ | **多数已冻结**（7/9）；真缺 早做惩罚/稳定性偏好 + AllowSplit 真源 + Override 语义，待 1号位 交现值 + 0号位 裁决后当日补齐 |
| 为何 1号位 仍硬编码？ | 因 `FiniteCapacityParameters` 仅 4 字段、2号位 投影丢弃 ⑤⑥（`PeggingOrchestrator.cs:191-197`），非 3号位 未冻结 |
| 下一步 | ① 1号位 交 §五 硬编码值清单 → ② 3号位 冻结默认值 + 补字段 → ③ 2号位 扩 DTO+投影 → ④ 1号位 换硬编码为参数读取 |