# 回执：P-1 零改动确认（附两处澄清）—— 1号位 致 3号位

> 收件：3号位（规则治理）／抄 0号位
> 依据：3号位《Setup规则承载重构_1号位_P-1零改动确认件 v1.0 20260923》
> 日期：2026-09-23
> 性质：**P-1 确认（零改动）＋ 两处表述澄清**

---

## 一、P-1 确认：快照第⑦块消费语义不变 ✓

**1号位 零代码改动确认** —— 证据链与 3号位 所列一致（逐项核实）：

| 消费点 | 1号位 核实 |
|---|---|
| `PhaseOneConstraintBuilder` :703-705 `SetupOptimizer.BuildRuleLookups(request.StrategySnapshot?.SetupTransitionRules)` | ✓ **只读第⑦块快照**，不读物理表 |
| `SetupOptimizer` 头注释「不自建规则读取、不读 3号位 数据库」 | ✓ 属实 |
| 命中语义（EXACT → DEFAULT → 无规则 0 分钟） | ✓ 不变 |
| 快照装载源改道（`FrozenStrategySnapshotProvider` 第⑦块 = 子块反序列化）| ✓ **1号位 无感**（装载源在 2号位 侧）|
| SetupSource 填充链路 | ✓ 不变 |

**「零代码改动无工作项」= 确认** ✓

## 二、澄清 1：`SetupOutcome` 枚举 5 值 ≠ 落库 `SetupSource` 4 态

贵方 §三.4 写：「`SetupOutcome`（ExactHit/DefaultHit/**SameProductZero**/InitialState/RuleMissing）→ `SetupOutcomeToSource` 填充链路不动」——**表述易被误读**，澄清如下：

| 层 | 值域 | 说明 |
|---|---|---|
| **内部 `SetupOutcome` 枚举** | **5 值**（`ExactHit`/`DefaultHit`/`SameProductZero`/`InitialState`/`RuleMissing`）| Scheduling **内部诊断**用，**保留 5 值**（区分路径）|
| **落库 `Task.SetupSource`** | **4 态**（`INITIAL_SETUP_STATE`/`EXACT`/`DEFAULT`/`SETUP_RULE_MISSING_ZERO_FALLBACK`）| **0号位 20260922 裁决**；`SameProductZero → DEFAULT`（同产品无显式规则=业务默认不换型）|

**即**：`SetupOutcomeToSource` 做 **5→4 归并**，`SameProductZero` **不再独立落库**（归 `DEFAULT`）。**链路"不动"指的是填充链路结构不变，但映射值域已是 4 态**（贵方 §三.4 若登记契约需按 4 态）。

## 三、澄清 2：fail-open 的落值 ≠ 统一 `DEFAULT`

贵方 §三.3 写：「子块缺失/为空/损坏 → 空规则列表 → **DEFAULT 兜底**（无规则 = 0 分钟）」——**意图正确（fail-open：不报错、按 0 分钟），但落值不统一为 `DEFAULT`**：

**空规则列表时，按上下文落 4 态之一**（1号位 `ResolveSetupCore` 实际行为）：

| 上下文 | 落 `SetupSource` |
|---|---|
| 无上一产品（`fromMaterialId is null`）| `INITIAL_SETUP_STATE` |
| 同产品连续（`from == to`）| `DEFAULT` |
| 有前后产品 且 EXACT/DEFAULT 均缺失 | **`SETUP_RULE_MISSING_ZERO_FALLBACK`**（WARNING，进 4号位 数据质量查询）|

**关键**：**空规则最常见落 `SETUP_RULE_MISSING_ZERO_FALLBACK`**（非 `DEFAULT`），且带 **WARNING 追踪**（0号位 Q4 裁决：规则缺失必须记解释）——**"fail-open"≠"静默 DEFAULT"**。

**fail-open 与六块 fail-closed 的区分**：1号位 认同（Setup 为可选块，缺失不阻断求解；但**必须记缺失解释**，非静默）✓

## 四、回执栏（P-1，1号位 填写）

| 项 | 确认 |
|---|---|
| P-1 快照第⑦块消费语义不变（命中三层/结构/fail-open） | **☑ 确认**（结构/命中三层不变；fail-open 见 §三 澄清——落值按上下文，非统一 DEFAULT）|
| 零代码改动无工作项 | **☑ 确认** |
| 备注 | ① 枚举 5 值 vs 落库 4 态（§二）；② fail-open 落值非统一 DEFAULT（§三）。**若贵方契约登记件含"5 值/DEFAULT 兜底"表述，建议同步为 4 态/按上下文** |

## 五、待办衔接

- 1号位 P-1 确认 → 2号位 D-1 DROP 闭环 ✓（1号位 不阻塞）
- **提请 3号位**：若《Setup规则承载契约登记件》中 `SetupOutcome` 值域或 fail-open 落值表述与 §二/§三 不一致，请按 **4 态**同步（0号位 20260922 裁决）

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）