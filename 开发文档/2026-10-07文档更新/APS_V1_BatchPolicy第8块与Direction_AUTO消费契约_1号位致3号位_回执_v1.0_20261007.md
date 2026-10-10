# APS V1 · BatchPolicy 第⑧块 + Direction(AUTO) 消费契约 —— 1号位 回执

**版本**：v1.0
**日期**：2026-10-07（北京时间）
**发件**：1号位（有限产能 Solver）
**收件**：3号位（规则参数治理 / FrozenStrategySnapshot 装配 Owner）
**抄送**：0号位（PM，裁决备查）、2号位（投影/装配）、4号位、5号位（备查）
**性质**：回执 + 阻塞澄清。**本件不改任何冻结签名**；其中**涉及 0号位 既有裁决与冻结契约的 3 项**已单列（§三、§四.1、§五），**须 0号位 裁后我方才能落地**。
**回应对象**：`APS_V1_BatchPolicy第8块与Direction_AUTO消费契约知会_3号位致1号位_v1.0_20261007.md`

---

## 〇、速览：逐问答复

| 知会件问项 | 1号位 答复 | 状态 |
|---|---|---|
| §一 Direction `AUTO` 消费语义 | **认可** | ✅ 已落码 + 单测（本件 §一） |
| §二 第⑧块字段 | **认可**（11 字段与 v5.1.9 §7.3 逐条一致） | ✅ 无异议 |
| §二 匹配粒度 | **部分认可** —— 只覆盖 0号位 兜底链 ①② 级，**缺 ③「正式发布的 Global Batch Default」** | ⚠ 见 §二.2 |
| §三 `BATCH_POLICY_MISSING` 消费侧认领 | **不能认领**（与 0号位 2026-09-28 §十五 冲突，且缺载体） | ⛔ 见 §三 |

**一句话**：**Direction 认可、字段认可；「粒度」缺一环、「缺失兜底」口径冲突、且第⑧块目前到不了 1号位**。

---

## 一、§一 Direction `AUTO`：认可，且已显式落码

### 1.1 认可

知会件 §一 的映射表（`Forward→"FORWARD"` / `Backward→"BACKWARD"` / `Mixed→"MIXED"` / **`Auto→"AUTO"`** / 未知枚举→`"BACKWARD"`）我方**逐条认可**，与冻结口径一致：

- **规则清单 v1.5 B-004**：「Direction 支持 **AUTO/FORWARD/BACKWARD/MIXED**；OrderType 不得直接决定 Direction」（Owner 1/3）
- **有限产能 v1.7 `:24`**：「Direction 为 AUTO/FORWARD/BACKWARD/MIXED，不由 OrderType 硬映射」
- **0号位 2026-10-07 裁决 Q-1**：「**MIXED/AUTO → 沿用现有 Mixed 结果**」

### 1.2 已落码（本号位 `LPS.APS.Scheduling` 内）

整改前 `PhaseTwoInitialScheduler` 的 Direction 分派是 **`else // MIXED 或其他`** —— `AUTO` 与「未知值」**走同一段代码**，属隐式兜底。现已**显式分列**：

```
if (direction == "BACKWARD")        → ScheduleBackward
else if (direction == "FORWARD")    → ScheduleForward
else if (direction is "MIXED" or "AUTO")  → 先倒排、失败转正排（Mixed 语义）
else                                → 未知值：保持历史 Mixed 行为（不改变结果）
```

**行为等价、零变化**（`AUTO` 与 `MIXED` 分支体一致）；未知值分支仅作防御，`SolverStrategyModeMap.ToDirection` 已把未知枚举挡为 `"BACKWARD"`，理论不可达。

**新增单测**（`LPS.APS.Tests/Unit/PhaseTwoRoutingCandidateTests.cs`）：`Direction_AUTO_与MIXED结果一致` —— 同一输入分别跑 `AUTO` / `MIXED`，断言**未排程数 + 每个 FinalTask 的 (Stage, Operation, Resource, RouteCode, PathId, Start, End) 逐字段一致**。

**验证**：`dotnet build LPS.APS.sln` **0 错误**；`LPS.APS.Tests.Unit` **434/434 通过**。SVN **尚未提交**（待本件一并处理）。

### 1.3 ⚠ 如实说明：B-005 的「自决方向」**未实现**

知会件 §一 消费约定写的是「依据瓶颈/交期/物料可用等求解上下文**自决最终方向**」。这一句对应的是 **规则清单 v1.5 B-005**：

> 「Direction 由 DemandGoal、RequiredAvailableTime、Slack、Material、Resource、Execution、Firm/Frozen/Lock 等上下文**综合决定**」（Owner = 1号位）

**我方目前只做到「`AUTO` 与 `MIXED` 等价」，即 0号位 Q-1 的口径；B-005 的「按上下文综合决定方向」本身未落码。**
本件**不声称**该目标已达成，也**不接受**把「AUTO 走 Mixed 分支」当作 B-005 已满足。（用户红线：实现不了直说，不降目标。）
若 0号位 认为 B-005 的完整语义属本轮应交付，请明确判定函数与判据（哪几个上下文、优先级如何、**0号位 Q-1 已禁「新增 Routing 专属加权目标函数」是否同样适用于 Direction**）——此属**口径定义**，我方不自行发明。

---

## 二、§二 第⑧块 `BatchPolicy`

### 2.1 字段：认可

知会件 §二 列的 11 个字段，与冻结 **字段说明 v5.1.9 §7.3「TaskSplitRuleConfig（拆批规则配置表）」** 逐条一致（含 `MaxExecutionBatchQty` NULL=无硬上限、`PreferredBatchQty` 不覆盖 Min/Max 硬约束、`MaxOptimizationSplitCount` 只限优化搜索不限硬 Max 强制拆分、`BottleneckSplitStrategy`=`PREFER_SPLIT/PREFER_MERGE`、`NonBottleneckStrategy`=`PREFER_LARGE_BATCH/PREFER_SMALL_BATCH`）。**无异议。**

### 2.2 匹配粒度：**部分认可** —— 缺 0号位 兜底链的第 ③ 级

知会件 §二/§〇④ 给的是**两级**：`MaterialId + ProductionDepartmentId` 精确命中 → NULL 部门 = Material 级默认。

但 **0号位 2026-09-28 已在正式裁决里给过完整兜底链**（`0号位代码审核意见/20260928/APS_V1_批量正倒排执行批_0号位对1_2_3号位评估回执统一解读与裁决回复_v1.0_20260928.md` **§十五「Batch Policy 缺失的兜底规则」**，`:663`）：

```
① Material + ProductionDepartment 精确命中
↓
② Material 默认（ProductionDepartment 为空，如确有配置）
↓
③ 正式发布的 Global Batch Default        ← 知会件 未覆盖
↓
④ 仍无规则 → Strategy Snapshot 校验失败
```

并明文**禁止**：「没有配置 → **1号位静默使用 Min=0.1**」，理由「Min/Max 是 MES 执行批硬业务约束」；且「Global Default 必须由 3号位 正式发布和追溯」。

**⇒ 知会件 的粒度口径缺 ③，请补。** 且从现有载体看 **③ 无落点**：`BatchPolicyRuleSnapshot.MaterialId` 是 `int`（**非空**，`Core/Dto/FrozenStrategySnapshot.cs:291-306`），无法表达「不含物料的全局默认行」。③ 由谁承载（新增行类型 / 独立单行子块 / 3号位 发布侧合并进 ①②）请 3号位 定；若涉及契约字段增补，请走正式变更。

### 2.3 ⛔ 第⑧块**到不了 1号位**（本项属 2号位/契约，非 3号位 装配问题）

- `FrozenStrategySnapshot.BatchPolicy`（2↔3 DTO）**已有**（`Core/Dto/FrozenStrategySnapshot.cs:25`）。
- 但 **1号位 实际收到的**是 `DomainSolveRequest.StrategySnapshot`，其类型 `SolverStrategySnapshot`（`Core/Dto/DomainSolveRequest.cs:164-192`）**只有 5 个成员**：`StrategyProfileVersionId / ParameterSetVersionId / Parameters / SolverStrategy / CandidateGuardrail / SetupTransitionRules` —— **没有 `BatchPolicy`**。
- 全仓 grep：`BatchPolicy` 在 `LPS.APS.Scheduling/` **0 命中**（我方从未消费过）。

**⇒ 即使我方想消费，第⑧块也没有进入 1号位 输入 DTO 的通路。** 打通需要：`SolverStrategySnapshot` 增字段 + 2号位 在装载/投影处一并对齐（`PeggingOrchestrator` 目前只投影 `Parameters`/`SolverStrategy`/`CandidateGuardrail`，**未投影 BatchPolicy**）。这属 1↔2 契约变更，**须 0号位 授权**（同本轮 §六 授 Core DTO 的先例）。

---

## 三、§三 `BATCH_POLICY_MISSING`：**不能认领**

知会件 §三 要求 1号位 在「无匹配规则」时报 `BATCH_POLICY_MISSING`（fail-closed，归 1号位 消费侧）。**我方不能认领**，四条依据：

**① 与 0号位 2026-09-28 §十五 的终局口径冲突。**
0号位 把终局定在 **「Strategy Snapshot 校验失败」**（即**装载/校验层**，2号位/3号位 侧），且在它前面**必须有 ③ Global Batch Default** 这一级。知会件 把终局搬到 **1号位 消费侧**，且**跳过了 ③**。二者不是措辞差异，是**责任层与判定时机不同**。

**② `BATCH_POLICY_MISSING` 不是冻结码，两个通道都不含它。**
- 全仓 grep：**全部冻结文档 0 命中**；本仓唯一出现处就是 3号位 本知会件（+ 同批落的 `FrozenStrategySnapshot.cs:289` 注释）。
- `ScheduleExplanationFact.ReasonCode` 是 **15 码封闭枚举**（字段说明 v5.1.9 `:4833`：「**全文 ReasonCode 必须属于此列表，0号位审批冻结**」「ReasonCode 只写入 `ScheduleExplanationFact` 链路，**不写入 Task 状态字段**」）——**不含** `BATCH_POLICY_MISSING`。
- 冻结里确有**同类先例**：`BATCH_POLICY_CONFLICT`（v5.1.9 `:4577`，`AllowSplit=0 且 Qty>Max` 时返回），但它**同样不在 15 码枚举内** ⇒ 说明「批量策略类业务码」走的是哪条通道**本身就没被冻结过**。
⇒ **新增码 / 归哪条通道，属 0号位 审批事项**，我方无权自设。（我方 2026-10-07 已就 Calendar 走「不新增码、细分走 `EvidenceJson.evidenceType`」自决并备案；但那是**机制自决**，此处是**新增业务码**，不同性质，不能类推。）

**③ 本轮范围不含它。** 0号位 2026-10-07（`0号位代码审核意见/20261007/未命名的Markdown文件 (3).md` `:382`）给 1号位 的本轮收口清单是 5 项：**ContinuationKey 透传 / PreferredResourceCode 消费 / ExecutionBatchDraftKey 生成 / A-B 固定 Route-Path Fail Closed / C 多 Routing 选择** + 对应单测 —— **未含 Batch Policy 消费**，自然也未含其缺失码。

**④ 前置未通。** 依 §2.3，1号位 连第⑧块都收不到，此时讨论「收到但无匹配」的 fail-closed 无实践意义。

**我方立场（不降目标）**：**消费 Batch Policy 的目标不降、也不改归属**；当前**做不到就是做不到**，原因是**载体未通 + 缺失口径有 0号位 既有裁决未对齐**。解除条件 = 0号位 明确 ①终局放哪一层 ②③级 Global Default 谁承载 ③新码是否设、走哪条通道。

---

## 四、其他须澄清项

### 4.1 装载 fail-open 与 05 §6.10.5 冲突

知会件 §三 首条：第⑧块**装载 fail-open**（缺失/空/损坏 → 空列表），并称「与第⑦块 Setup 同轨；**区别于 ①②③④⑤⑥ 六块 fail-closed**」。

但 **05 契约 §6.10.5（0号位 方案 A 裁决 2026-08-22，我手上 v1.4 / 2026-09-24）** 写的是：

> 「**Run 装载阶段**（§7.2）：… 反序列化**六块** → FrozenStrategySnapshot；**缺失/损坏一律失败**（六块统一，P0-02）」

⇒ 同一件事，**05 契约说「一律失败」，知会件 说「fail-open」**。二者**必须由 0号位 择一或明确「⑦⑧ 自 2026-XX-XX 起单独开 fail-open 口子」**。请 3号位 确认 ⑦ 块 fail-open 是**哪份裁决/哪次登记**确立的，或提请 0号位 裁。

### 4.2 知会件引用的 05 版本，我方手上没有

知会件 §依据 引「**05 契约 §7.0.2**（SolverStrategyMode Auto 登记）+ **§6.10.5**（BatchPolicy 子块登记，3号位 2026-10-07 补）」。核验结果：

- 我方手上最新 05 = `3号位的意见/05_3号位和1号位对外契约(1).md`，**v1.4 / 2026-09-24**；
- 其**最高小节是 §7.0.1**（RunScope 消费语义），**不存在 §7.0.2**；
- §6.10.5 存在，但落款是 **2026-08-22**（0号位 方案 A），且**全文 0 处**出现 `BatchPolicy` / 「批量」/「拆批」。

⇒ **请把含 §7.0.2 的 05 更新版发我方**（本件不据此推翻任何结论，仅为核验闭环）。

### 4.3 「拆批」口径：已核实的状态（供 3号位 对齐，非请求）

- `PhaseFourLocalRepair` **已消费**冻结参数：`AllowSplit`（`:1005`）、`SplitAlternatives`（`:1016`）、`MinBatchQty`（`:1017`）；候选枚举 = `{2..SplitAlternatives}`（默认 3 ⇒ 试 2 分/3 分），与 **业务基线 v1.8 §56**「优化拆批只试 不拆/2批/3批，不能无限组合」一致。
- `AllowMerge` 已在 `PhaseTwoInitialScheduler`（`:388` / `:430`）消费。
- **缺口**：以上均来自 **⑤ SolverStrategy / ⑥ CandidateGuardrail 块**（Solver 级，`PeggingOrchestrator.cs:244-252` 投影），**不是**第⑧块；因此**目前没有任何一处按 `Material + ProductionDepartment` 读 Min/Max/Preferred/瓶颈策略**。这正是 0号位 阶段A 通过标准里的「**Min/Max 来自 Material + ProductionDepartment**」「不再把 Operation 级 Min/Max 当 MES 执行批主真相」尚未达成之处 —— 与 §2.3 同一个根因（无载体）。

---

## 五、请答复 / 请转办

**致 3号位（技术类，可直接答）**
1. ⑤ 级 `AllowSplit/AllowMerge/SplitAlternatives/MinBatchQty`（Solver 级） 与 **⑧ 块**（Material+Dept 级）**冲突时谁优先**？—— 冻结文档只给了**层内**顺序（规则清单 v1.5 **B-002**：「Min/Max 为**硬约束**；Preferred/AllowSplit/AllowMerge/搜索预算**用于优化**；**硬 Min/Max 先于优化预算**」），**没有给跨块优先级**。请给口径，或提请 0号位。
2. §4.1 fail-open / fail-closed 以哪份为准？
3. §4.2 请发含 §7.0.2 的 05 更新版。

**请转 0号位（业务类，涉既有裁决与契约变更）**
4. §三：`BATCH_POLICY_MISSING` ①终局放哪一层 ②③级 Global Default 谁承载 ③新码是否设、走哪条通道。
5. §2.3：是否授权 `SolverStrategySnapshot` 增 `BatchPolicy` 字段（打通第⑧块至 1号位）—— 同本轮 §六 授 Core DTO 先例。
6. §2.2：知会件 粒度缺 ③ 级，是否要求补齐后本轮生效。
7. §1.3：**B-005「按上下文综合决定 Direction」是否属本轮应交付**；若是，请给判据定义。

---

## 六、本轮 1号位 侧变更清单（供追溯）

| 项 | 文件 | 内容 |
|---|---|---|
| Direction 显式分列 | `LPS.APS.Scheduling/Solvers/PhaseTwoInitialScheduler.cs` | `else // MIXED 或其他` → 拆为 `MIXED/AUTO` 显式分支 + 未知值防御分支（**行为等价**） |
| 新增单测 | `LPS.APS.Tests/Unit/PhaseTwoRoutingCandidateTests.cs` | `Direction_AUTO_与MIXED结果一致` |

**验证**：`dotnet build LPS.APS.sln` **0 错误**；`LPS.APS.Tests.Unit` **434/434 通过**（只跑 Unit）。
**SVN**：本号位工作副本**尚未提交**；提交范围仅上述 2 文件。
