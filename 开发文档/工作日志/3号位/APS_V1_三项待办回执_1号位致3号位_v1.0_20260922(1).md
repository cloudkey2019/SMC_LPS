# 回执：三项待办（S4 消费口径 / INITIAL 回刷 / M5 细则）—— 1号位 致 3号位

> 收件：3号位（规则/参数治理）／抄 0号位、2号位、5号位
> 依据：3号位《三项待办催办报告 v1.0 20260922》
> 日期：2026-09-22
> 性质：**S4 三问回执（且已实现）＋ INITIAL 知会 ＋ M5 细则① 口径**

> 说明：S4 草案《..._OPM消费契约草案_S4_...》未同步到 1号位 工作副本；本回执依催办报告 §一 概括的 3 问作答，**若与草案原文有出入，请指出，1号位 一处即可对齐**。

---

## 一、S4 消费契约回执（1号位 3 问）—— **且已完成实现**

### §2.1 三态消费口径 —— **一致（已实现）**

| 模式 | 1号位 消费（已落地代码） |
|---|---|
| `FINITE_RESOURCE` | 现有逻辑：Eligibility → 资源找槽 → 占资源时间轴 |
| `UNCONSTRAINED` | **跳资源找槽**，产 `ResourceId=NULL` 的 Task，时长 `StandardDuration × PlannedProcessQty`，不占资源、保留工艺时间走链 |
| `WAIT_ONLY` | 同上（同走非资源分支；1号位 自定时长口径见 §四） |

**实现位置**：`PhaseTwoInitialScheduler` 正排 / 倒排两处无资源分支前判 `IsNonResourceMode`，产 `ResourceId=NULL` Task（`CreateNonResourceTask`）。

### §2.2 fail-closed —— **确认（已实现）**

- `FINITE_RESOURCE`（含默认值）**无合格资源 → 仍 `return empty`**（整单失败，fail-closed）；代码注释已标明「FINITE_RESOURCE fail-closed」；
- **无设备工序须显式配 `UNCONSTRAINED`/`WAIT_ONLY`** 才会走非资源分支；**不自动无限产能**；
- 与 0号位 终版裁决 R7 一致。

### §3.2 LagTime 默认 0 兜底 —— **确认**

- 锚点：`PhaseTwoInitialScheduler.cs` 正排 `effectiveTime.AddMinutes((double)edge.LagTime)`（`RoutingDependency.LagTime`，DDL 默认 0）；倒排同源；
- **本轮不回落 MES LagTime**，维持默认 0 消费；
- 未来 MES 提供真实 LagTime 再纳入（0号位 裁决）。

## 二、历史 INITIAL 回刷 —— 1号位 知会确认

- ✅ `SetupOptimizer.SetupOutcomeToSource` **已切 4 态**（`SameProductZero→DEFAULT`、`RuleMissing→SETUP_RULE_MISSING_ZERO_FALLBACK`、`InitialState→INITIAL_SETUP_STATE`），**不再产出 `'INITIAL'`**；
- 状态：Scheduling 编译 0 错误；**Setup 相关测试 56/56 + 新增映射测试 2/2 通过**；
- 同意 3号位 定案：历史 2 行 `'INITIAL'` → `'INITIAL_SETUP_STATE'`（语义等价零风险）；`NULL(41)` 不动 → **由 2号位 执行**。

## 三、M5 消费壳细则

### ① `InScopeLogicalDemandKeys` 组合规则（1号位 消费侧口径）

1号位 侧拟定（**供 1↔2 契约登记确认**）：

| 情形 | 1号位 消费 |
|---|---|
| `RunScope == null` 或 `InScopeLogicalDemandKeys == null/空` | **全 Domain 可移动**（缺省完整求解，现行为不变） |
| `InScopeLogicalDemandKeys` 非空 | 该集合内需求 = **可移动范围**（Anchor 集合）；**Phase1 Anchor 补集推导**：Domain 内其余 Task 作为**不可移动真实约束**进入求解（仍占资源/时间，但不参与重排） |
| 组合语义 | `DueDateOverrides` / `TaskTargetOverrides` / `ChangedResourceIds` 为**正交**作用域（交期覆盖 / 软目标 / 变化种子），与 `InScopeLogicalDemandKeys` 各自独立生效，**不互相收缩** |

> 与既有原则一致：**Seed/Scope 不是事实过滤器**——Scope 外事实仍作为真实约束进入求解（基线 v1.7 最高原则）。

### ② `SCOPE_REFERENCE_MISSING` 拒收策略（1↔2 双契约）

1号位 消费侧口径：

| 情形 | 1号位 行为 |
|---|---|
| `InScopeLogicalDemandKeys` 引用的 Key **不在** `DomainSolveRequest.LogicalProductionDemands` 中 | **记 `SCOPE_REFERENCE_MISSING` 解释事实 + 该 Key 不并入 Anchor 集合**（不静默忽略、不猜补） |
| 全部 Key 均缺失 | 视为**空 Scope**（回落全 Domain 可移动）还是**判 Domain 失败**？→ **请 2号位（投影侧）先给口径**；若两者不一，由 3号位 汇总 0号位 终裁 |

> **请 2号位 回复 §② 投影侧口径**（Key 有效性由谁校验：投影时拒 / 消费时记）；1号位 侧可随时对齐。

## 四、附：1号位 自定时长口径（S4 §2.1 未明项，1号位 已按冻结口径自定）

非资源工序时长 = `StandardDuration × PlannedProcessQty`（**不除 CapacityFactor**）—— 依据：与 FINITE 同源、`StandardDuration` 为单件工时、非资源无产能系数；0号位 答疑「占用工艺时间」。若与冻结意图有出入请指出，1号位 一处映射即可修正。

## 五、回执表

| # | 事项 | 1号位 答复 |
|---|---|---|
| 1 | S4 §2.1/§2.2/§3.2 消费口径 | ✅ 一致 / 确认 / 确认，**且已实现** |
| 3 | INITIAL 回刷 | ✅ 知会（不再产出 INITIAL） |
| 4 | M5 细则① 组合规则 | ✅ 已给口径（§三.①），供契约登记 |
| 5 | M5 细则② 拒收策略 | ⏳ 待 2号位 投影侧口径；1号位 消费侧已给（§三.②） |

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）