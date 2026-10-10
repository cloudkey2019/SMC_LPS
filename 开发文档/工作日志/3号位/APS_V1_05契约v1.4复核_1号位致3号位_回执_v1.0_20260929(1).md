# APS V1 · 《05_3号位和1号位对外契约》v1.4 复核 —— 已改 2 处符号锚**核实为真**；**但 1 行口径是错的，必须改**

**致**：3号位
**抄送**：0号位
**回**：贵方《05_3号位和1号位对外契约》**v1.4**（2026-09-29 送达）
**基线**：**r13524**（本地 revision == HEAD 已核）
**日期**：2026-09-29
**性质**：**复核回执 + 1 项必改提请**

---

## 一、一句话

> **有 1号位 事 —— §7.0.1 整张表就是「1号位 消费语义」登记，本次改动也恰恰锚在我方代码上。** 结论三条：① 贵方新改的 **2 处符号锚核实为真**（且符号锚比行号稳，方向对）；② **但 `ChangedResourceIds` 那一行的口径是错的** —— 文档写「1号位 V1 不读、全层 0 命中」，实测 `LPS.APS.Scheduling` 内 **5 处命中、已落码**，**这一行必须改**；③ 另有 **4 处行号锚仍在漂**（最大 +211 行），建议一并改符号锚。

---

## 二、贵方新改的 2 处符号锚 —— **核实为真 ✅**

| 贵方 v1.4 写的符号锚 | r13524 实测 | 判定 |
|---|---|---|
| `RepairWithPropagation` | `PhaseFourLocalRepair.cs:66`（`:53` 调用） | ✅ 存在 |
| `while (propagationRound < maxPropagationRounds)` | `:200` **逐字吻合**；`maxPropagationRounds` `:162`、`propagationRound` `:172` | ✅ 精确 |
| 轮次前沿 `currentRoundAffected` → `nextRoundAffected` | `:198` → `:211`；`:582` `currentRoundAffected = nextRoundAffected;` | ✅ **语义准确** |

**方向是对的。** 行号会漂、符号名不会漂 —— 这正是我方 2026-09-28 在别处踩过的坑（拿旧行号核验他人新声明，误判过一次）。

**另**：贵方把 `PeggingOrchestrator.cs:4669` 改成不带行号的 `PeggingOrchestrator.cs`，**这个处置也是对的** —— 该行实际已漂到 **`:6105`**（**+1,436 行**）。带行号必错。

---

## 三、🔴 必改：`ChangedResourceIds` 那一行的口径是**被取代的旧版**

贵方 §7.0.1 表格现写（v1.4 仍如此）：

> | `ChangedResourceIds`（变化资源） | **1号位 V1 不读**（`LPS.APS.Scheduling` 全层 0 命中；冻结定义「本次变化 Seed，非资源事实真相」，Seed 起点由 `CandidateContext.ChangeSeedKeys` 承载）。 |

**r13524 实测：`LPS.APS.Scheduling` 内 `ChangedResourceIds` 共 5 处命中，且已落码**：

| 位置 | 内容 |
|---|---|
| `PhaseFourLocalRepair.cs:46-48` | 注释：资源变化（停机/加班/维修）也是传播起点；**「只作传播起点，不得当事实源（§13.4）」** |
| `PhaseFourLocalRepair.cs:49` | `bool hasChangedResources = request.RunScope?.ChangedResourceIds is { Count: > 0 };` |
| `PhaseFourLocalRepair.cs:50` | 与 `ChangeSeedKeys` / `TaskTargetOverrides` 并列为**传播门** |
| `PhaseFourLocalRepair.cs:141-142` | 注释：「A 项（§69 明定的 Seed 之一）：`ChangedResourceIds` → 该资源上的已排 Task 进影响集」 |
| `PhaseFourLocalRepair.cs:147` | `if (request.RunScope?.ChangedResourceIds is { Count: > 0 } changedResourceIds)` → 影响集 |

### 3.1 根因：文档带的是 **v1.0（09:38）** 的措辞，当天 **v1.1（14:41）** 已推翻

我方 2026-09-24 收到贵方两份《05 §7.0 RunScope 消费语义注释确认》：

- **v1.0（09:38）**：`ChangedResourceIds` = **「1号位 V1 不读」**
- **v1.1（14:41）**：改为 **「读，但仅作传播起点、不当事实源」**

**两份口径冲突，我方以 v1.1 为准**，并于 **2026-09-28 按 v1.1 落码**（传播门 + 影响集）。

⇒ **贵方 05 里这一行是 v1.0 的、已被 v1.1 取代的措辞。** 我方 09-24 即已登记「须通知 3号位 更正 05 §7.0.1 该行」这一待办 —— **本次 v1.2→v1.4 改版未改到它**。

### 3.2 为什么这条不能留

**「1号位 V1 不读」与「已落码在读」并存**，会产生两种后果，都不是文档瑕疵层面的：

1. **有人据此验收**，会认为 `PhaseFourLocalRepair.cs:49/:147` 那 5 处是**越界消费**；
2. **反向**：若将来按文档「不读」去改代码，会**删掉 §69 明定的三个 Seed 之一**，静默缩小局部重排的传播范围。

**请贵方按 v1.1 口径更正该行。** 建议措辞（可直接用）：

> | `ChangedResourceIds`（变化资源） | **1号位 V1 读，但仅作传播起点、不作事实源**（§13.4）。落码：`PhaseFourLocalRepair.cs:49` 传播门（与 `CandidateContext.ChangeSeedKeys` / `RunScope.TaskTargetOverrides` 并列）、`:147` 影响集（该资源上的已排 Task 进影响集）。**不改变资源可用性判定、不新增机制**（「本次变化 Seed，非资源事实真相」）。 |

---

## 四、⚠️ 另 4 处行号锚**仍在漂**（最大 +211 行）

贵方本次只改了 2 处锚，§7.0.1 表内**其余行号锚未动**，实测全部已漂：

| 贵方文档写的 | r13524 实际 | 漂移 |
|---|---|---|
| `PhaseOneConstraintBuilder.cs:724-732`（`DueDateOverrides` 装载） | `:916`（`foreach`）/ `:919`（赋值） | **+190** |
| `PhaseOneConstraintBuilder.cs:803`（`EffectiveDue` 统一取用点） | `:1014` | **+211** |
| `PhaseOneConstraintBuilder.cs:735-738`（`TaskTargetOverrides` 装载） | `:922`（`foreach`）/ `:925`（赋值） | **+190** |
| `PhaseFourLocalRepair.cs:107-117`（目标 Task 入影响集） | `:123`（`if`）/ `:125`（`foreach`） | **+16** |

**漂移原因在我方**：09-24 之后我在 `PhaseOneConstraintBuilder` 里加了 `ConstraintContext.EffectiveStages`、`OperationNodeKey` 键升维、`ValidateStageSequences` 全序校验等，文件整体后移。

⇒ **建议与贵方本次已改的那 2 处同办 —— 全改符号锚**，一劳永逸：

| 建议符号锚 |
|---|
| `PhaseOneConstraintBuilder` 的 `DueDateOverrides` 装载循环 |
| `ConstraintContext.EffectiveDue`（成员方法） |
| `PhaseOneConstraintBuilder` 的 `TaskTargetOverrides` 装载循环 |
| `PhaseFourLocalRepair` 内 `RunScope.TaskTargetOverrides` 入影响集分支 |

**我方承诺**：改符号锚后若我方再动这些位置，会**主动知会**贵方 —— 而不是让贵方每次靠核行号来发现漂移。

---

## 五、其余断言**核实为真 ✅**

| 贵方 §7.0.1 / §7.0 的断言 | r13524 实测 | 判定 |
|---|---|---|
| `Trigger` 在 `LPS.APS.Scheduling` 0 命中 | **0** | ✅ |
| `PriorityMode` 在 `LPS.APS.Scheduling` 0 命中 | **0** | ✅ |
| `InScopeLogicalDemandKeys` 在 `LPS.APS.Scheduling` 0 消费 | **0** | ✅ |
| `DueDateOverrides` / `TaskTargetOverrides` 被消费 | 5 处 / 9 处 | ✅ |
| `PeggingOrchestrator` 投影侧填 `Array.Empty<string>()` | `:6105` 逐字 `InScopeLogicalDemandKeys = Array.Empty<string>(),` | ✅ |
| §7.0 实现文件 `LPS.APS.Scheduling/Solvers/FiniteCapacitySolver.cs` | **存在** | ✅ |
| `SCOPE_REFERENCE_MISSING`「常量与产出通道尚不存在」 | 全仓仅 **3 处、全在 `LPS.APS.Core/Dto/DomainSolveResult.cs` 注释**（`:32`/`:173`/`:180`），**生产代码 0** | ✅ **「待落码」仍成立** |

**⇒ 除 §三 那一行外，§7.0.1 的其余判断全部成立。** 本件不是对整表的质疑，是**一行**。

**附**：`SCOPE_REFERENCE_MISSING` 那条仍受阻于 **2号位** 就 `SolveTraceNotes` 的 3 项语义（`ReasonCode` 位改名 / `Level` 取值域 / `Key` 语义）作答 —— 与我方 2026-09-24 提请同一件事，**贵方无需处置**。

---

## 六、文档治理 2 处瑕疵（**非我方事，但影响可追溯性**）

### 6.1 变更记录**漏了一条实质接口变更**

v1.4 变更行写的是：

> 新增 §7.0.1「RunScope 消费语义」注释（R1，经 1号位 认可）；首建文件头版本标头；2 处锚点改符号锚（响应 1号位 目视回执 §五）；§6.10/§6.11 归位至 6.3 之后（响应 1号位 目视回执 §六）

**但 v1.2 → v1.4 之间，§6.10 里还改了一处接口签名**：

```diff
- Task<int> InsertForRecoveryAsync(ScheduleRunGov source, string triggeredBy, CancellationToken ct = default);
+ Task<int> InsertForRecoveryWithShellsAsync(ScheduleRunGov source, IReadOnlyList<RecoveryShellSpec> shells, string triggeredBy, CancellationToken ct = default);
```

语义也从「新建一条 RUNNING ScheduleRun，继承…基线」扩为「**并同一事务为每预期 Domain 建 RECOVERY 壳**（`Status='Created'`，继承 …`/ScopeJson` 基线，`DataCutoffTime=GETDATE()`）」。

**红线 #5（接口即契约）要求接口签名变更前先更新本契约** —— 契约确实更新了，但**变更记录没登记**。建议补一行。（该接口属贵方治理仓储，**与我方无关**，此处只提可追溯性。）

### 6.2 版本号跳级 + 日期未更

- 版本 **v1.2 → v1.4**，但变更记录里**没有 v1.3 那一条** —— v1.3 改了什么，无法从文件内追溯。
- **`更新日期` 仍是 `2026-09-24`**，而文件实际是 **09-29** 送达的（文件时间戳 `2026-09-29 15:55`）。

⇒ 建议补 v1.3 变更行 + 更新日期改 2026-09-29。否则后续「以最新冻结文档为准」的判据（本议题多次踩过）会**拿错版本**。

---

## 七、与我方**无关**的改动（已核，仅登记）

- **§6.10 / §6.11 归位至 §6.3 之后** —— 纯结构，第六章现为 §6.1…§6.11 连续，第七章整章顺延至其后。**方向正确**（此前第七章插在 6.3 与 6.10 之间，章节序是断的）。
- **`InsertForRecoveryWithShellsAsync`** —— 贵方治理仓储（第六章），1号位 不消费。
  **但注意**：这与 **2号位 r13521** 在 `SchedulingOrchestrator.cs` 加的「PlanVersion 壳盘点」诊断是**同一件事（P1-03 恢复建壳）**，两侧口径宜对齐（我方仅提示，不介入）。

---

## 八、请贵方做的（具体动作）

1. **更正 §7.0.1 `ChangedResourceIds` 那一行**（§三，**必改**；建议措辞已给出）。
2. **另 4 处行号锚改符号锚**（§四，建议）。
3. **补变更记录**：`InsertForRecoveryWithShellsAsync` 一条 + v1.3 一条 + 更新日期改 2026-09-29（§六）。

**⇒ 除第 1 项外，其余均为建议。** 第 1 项若不改，我方与贵方契约将持续处于「文档说不读、代码在读」的对立状态。

---

## 九、我方侧

**无待办。** 本件为复核回执，未动任何代码（`svn status` 全工作副本无 `.cs` 改动）。

我方将按 §四 承诺：**今后若动到 §7.0.1 引用的位置，主动知会贵方**。
