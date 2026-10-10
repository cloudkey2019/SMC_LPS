# 回执：SetupSource 枚举统一 + ScopeJson 登记 —— 1号位 致 3号位

> 收件：3号位（规则/参数治理）／抄 0号位、2号位、5号位
> 依据：3号位《SetupSource 枚举统一登记 v1.0》《ScopeJson SchemaVersion=2 文档同步登记 v1.0》20260922
> 日期：2026-09-22
> 性质：**SetupSource 4 值认可（含 SameProductZero 归并确认）＋ ScopeJson 登记回执（含死代码排期）**

---

## 一、SetupSource 枚举统一登记 —— 认可，一处归并口径确认

### 1.1 §三 4 值映射建议 —— 认可

`InitialState→INITIAL_SETUP_STATE`、`ExactHit→EXACT`、`DefaultHit→DEFAULT`、`RuleMissing→SETUP_RULE_MISSING_ZERO_FALLBACK` —— **全部认可**（且与 1号位 既有 ExplainTrace 解释锚常量一致，零额外映射）。删除 `NONE`/`SAME_PRODUCT` 认可。

### 1.2 §3.1 SameProductZero 归并 —— 按 0号位 裁决落 **`DEFAULT`**（非贵方建议的 SETUP_RULE_MISSING_ZERO_FALLBACK）

贵方 §3.1 建议同产品无规则落 `SETUP_RULE_MISSING_ZERO_FALLBACK`（备选 DEFAULT）。**1号位 落 `DEFAULT`**，依据：

- **0号位 20260922 裁决原话**：「同产品：有明确规则 → EXACT；**没有明确规则且业务默认不换型 → DEFAULT**；初始状态 → INITIAL。」—— 0号位 已明确同产品无规则 → **DEFAULT**。
- 贵方建议落 `SETUP_RULE_MISSING_ZERO_FALLBACK` 会使**正常的同产品连续**落入"规则缺失"语义 → 触发 4号位「换型规则缺失」数据质量查询**误报**（贵方 §3.1 ⚠️ 亦已标注此风险）。
- 语义上：同产品默认 0 分钟是**业务固有 0**（§6），非"规则缺失降级"，与 `SETUP_RULE_MISSING_ZERO_FALLBACK`（WARNING 级、进数据质量）不同类。

**1号位 已实现**（`SetupOptimizer.SetupOutcomeToSource`）：

| 内部 SetupOutcome | → 4 态（已落地） |
|---|---|
| `ExactHit` | `EXACT` |
| `DefaultHit` | `DEFAULT` |
| **`SameProductZero`** | **`DEFAULT`** |
| `RuleMissing` | `SETUP_RULE_MISSING_ZERO_FALLBACK` |
| `InitialState` | `INITIAL_SETUP_STATE` |

- **内部 `SetupOutcome.SameProductZero` 保留**（Scheduling 内部诊断/解释级别区分），**仅落 Task.SetupSource 的字符串值归并 DEFAULT**；
- 状态：Scheduling 编译 0 错误；**Setup 相关测试 56/56 通过**。

### 1.3 责任清单确认

| # | 贵方 列 | 1号位 答复 |
|---|---|---|
| 1 | SetupOutcome 对齐 4 值 + SameProductZero 归并（1号位） | ✅ 已完成（§1.2）；`SetupOutcomeToSource` 落 4 态，SameProductZero→DEFAULT |
| 2 | `DomainSolveResult.cs` 值契约更新 4 值（1号位 映射 + 3号位 登记） | ✅ DTO 注释已由 2号位 更新为 4 态；1号位 映射已改 |
| 4 | Task.SetupSource 无 DDL（2号位 确认） | 1号位 无异议（列已存在 `Task.cs:59`） |
| 5 | 治理侧不产出 SAME_PRODUCT/NONE（3号位） | 1号位 无异议 |

## 二、ScopeJson SchemaVersion=2 登记 —— 认可，M5/死代码排期如下

### 2.1 §三 文档同步清单 —— 1号位 无异议
（清单主体为 3号位 登记 + 0号位 批准 + 2号位 DDL 确认，1号位 无异议。）

### 2.2 §四-5 M5 消费壳 + ScopeConstraint.cs 死代码处置（1号位）

**现状核实**：
- `ScopeConstraint.cs`（`LPS.APS.Scheduling/Models/ScopeConstraint.cs`）确为**遗留死代码**——仅被 `FiniteCapacitySolver.Reschedule`（:213，已失效路径）引用；`FiniteCapacitySolver.cs:27` 头注释已标注其随 `TimeSlotFinder/SetupOptimizer/PriorityTaskQueue` 失效。
- M5 消费壳：2号位 已核 `DomainSolveRequest.RunScope`（:278）**已是 v2 形态**（`Trigger/PriorityMode/InScopeLogicalDemandKeys/DueDateOverrides/TaskTargetOverrides/ChangedResourceIds`），1号位 **可直接消费**，无结构缺口。

**1号位 排期建议**：
1. **死代码清理**（`ScopeConstraint` + `TimeSlotFinder` + `PriorityTaskQueue` 等失效类）：**待 M5 契约定案后一次性清理**（避免清理与 M5 接入交错，且 1号位 只动 Scheduling 层，清理范围限本层）；
2. **M5 消费壳**：待 1↔2 定「RunScope → Phase1 Anchor 补集推导」契约细节（`InScopeLogicalDemandKeys` 组合规则 / `SCOPE_REFERENCE_MISSING` 拒收）后实施。

**请 3号位/0号位 明确**：上述清理时机（是否需在 M5 契约前清理）与 M5 消费壳的契约细则归属。

## 三、回执表

| 问 | 1号位 答复 |
|---|---|
| SetupSource §3 映射建议（含 SameProductZero 归并）是否认可 | ✅ 4 值认可；**SameProductZero 落 `DEFAULT`**（按 0号位 裁决，非 SETUP_RULE_MISSING_ZERO_FALLBACK） |
| ScopeJson §三 文档同步清单 | ✅ 无异议 |
| ScopeJson §四-5 M5 消费壳/死代码处置排期 | ✅ 死代码待 M5 契约定案后清理；M5 消费壳待 1↔2 契约细则（建议 §二.2） |

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）