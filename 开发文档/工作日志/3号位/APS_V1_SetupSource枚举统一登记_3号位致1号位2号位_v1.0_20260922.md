# APS V1 SetupSource 枚举统一登记（致 1号位 / 2号位 / 5号位）

**版本**：v1.0（登记 + 映射建议，待 1号位 确认）
**日期**：2026-09-22
**发送人**：3号位（规则/参数治理、Setup 治理 Owner）
**接收人**：1号位（SetupOptimizer 产出/映射执行）、2号位（Task 落库确认）、5号位（查询契约同步）
**抄送**：4号位（前端展示，备查）、0号位（PM，备查）
**性质**：**契约登记**——按 0号位 裁决统一 SetupSource 值域为 4 值；登记现状核对、映射建议与责任清单
**依据**：
- 0号位《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》**二.1 / 二.2**（SetupSource 保留补入 Task 事实模型 + 枚举 4 值统一）
- 0号位 2026-09-17 Q4 裁决（初始状态与规则缺失严格分开）

---

## 〇、速览

```
0号位 裁决 4 值：INITIAL_SETUP_STATE / EXACT / DEFAULT / SETUP_RULE_MISSING_ZERO_FALLBACK（删 NONE / SAME_PRODUCT）
现状核对：1号位 SetupOptimizer.SetupOutcome 5 值（ExactHit/DefaultHit/SameProductZero/InitialState/RuleMissing）
          → DomainSolveResult 映射 5 值字符串（EXACT/DEFAULT/SAME_PRODUCT/NONE/INITIAL）→ Task.SetupSource 落库
差异：值域命名 4↔5 不一致；SameProductZero 需归并；NONE 需改名 SETUP_RULE_MISSING_ZERO_FALLBACK
Task.SetupSource 物理列已存在（Task.cs:59）→ 无 DDL
```

---

## 一、0号位 裁决口径（二.1 / 二.2）

### 1.1 SetupSource 保留并补入 Task 事实模型（二.1）

Setup 已冻结为需要解释的业务事实，需记录：命中的换型规则类型、为什么得到该 Setup 时间、是否使用默认规则、是否规则缺失兜底。

> **已满足**：`Task.SetupSource` 物理列已落地（`LPS.APS.Core/Entities/Aps/Task.cs:59`），1号位 SetupOutcome 原样落库（PeggingOrchestrator.cs:417-418/450）——本项**无新增 DDL**，仅值域统一。

### 1.2 枚举统一为 4 值（二.2）

| 值 | 含义 |
|---|---|
| `INITIAL_SETUP_STATE` | 无上一产品，初始设备状态 |
| `EXACT` | 命中明确产品转换规则 |
| `DEFAULT` | 命中默认换型规则 |
| `SETUP_RULE_MISSING_ZERO_FALLBACK` | 无规则，按 0 分钟兜底 |

不保留：`NONE`、`SAME_PRODUCT`（SAME_PRODUCT 只是业务条件，不是规则来源）。

---

## 二、现状核对（代码锚点）

### 2.1 1号位 产出（Scheduling 层）

`SetupOptimizer.cs:46-58` `SetupOutcome` 枚举 5 值 + 解析优先级（`SetupOptimizer.cs:101-146`）：

| 优先级 | 触发条件 | 当前 Outcome | 当前解释类型 |
|---|---|---|---|
| 1 | 无上一产品（fromMaterialId is null）| `InitialState` | `INITIAL_SETUP_STATE`（INFO）|
| 2 | EXACT 规则命中（含同产品 A→A 显式规则）| `ExactHit` | — |
| 3 | 同产品连续无显式规则 | `SameProductZero` | — |
| 4 | DEFAULT 规则命中 | `DefaultHit` | `DEFAULT_SETUP_FALLBACK`（INFO）|
| 5 | EXACT/DEFAULT 均缺失 | `RuleMissing` | `SETUP_RULE_MISSING_ZERO_FALLBACK`（WARNING）|

### 2.2 落库映射（现状 5 值）

`DomainSolveResult.cs:66-69` 值契约 + `GanttDataDto.cs:176`：

```
ExactHit→EXACT、DefaultHit→DEFAULT、SameProductZero→SAME_PRODUCT、RuleMissing→NONE、InitialState→INITIAL
```

→ `Task.SetupSource`（string，原样落库，2号位 不反推）。

---

## 三、4 值统一映射建议

| 现状 5 值 | 统一后 4 值 | 说明 |
|---|---|---|
| `InitialState` | `INITIAL_SETUP_STATE` | 枚举改名，与既有 ExplanationType 一致 |
| `ExactHit` | `EXACT` | 改名，语义不变 |
| `DefaultHit` | `DEFAULT` | 改名，语义不变 |
| `RuleMissing` | `SETUP_RULE_MISSING_ZERO_FALLBACK` | 改名，与既有 ExplanationType 一致；删除 NONE |
| `SameProductZero` | **归并**（见 §3.1）| 删除 SAME_PRODUCT 独立值 |

### 3.1 SameProductZero 归并建议（待 1号位 确认）

0号位 已裁：SAME_PRODUCT 只是业务条件，**不是规则来源**，不保留独立值。

- 同产品 A→A **有显式规则** → 已由 `ExactHit` 分支覆盖（§6：同产品显式规则优先），落 **`EXACT`**；
- 同产品 A→A **无显式规则**（`SetupOptimizer.cs:127-130` 分支）→ Setup=0 是**业务固有 0**（§6 同产品默认 0），**不是规则缺失**：
  - **建议落 `SETUP_RULE_MISSING_ZERO_FALLBACK`**——0 分钟兜底语义一致（值域 4 值唯一可表达"无规则命中且 0 分钟"的值）；
  - ⚠️ 但需 1号位 确认**解释级别**：同产品场景不应升级为 WARNING（避免误报"规则缺失"进 4号位 数据质量查询）——内部 Outcome `SameProductZero` 可保留（Scheduling 内部诊断），仅**落 Task.SetupSource 的字符串值**归并；若 1号位 判定需区分，提请 0号位 追加裁决。
- **备选**：1号位 在求解内将同产品无规则场景解释为 DEFAULT 语义（命中"同产品固有默认 0"）落 `DEFAULT`——由 1号位 业务判断后定。

> **登记结论**：SetupSource 值域统一后为 4 值；SameProductZero 的具体归并（SETUP_RULE_MISSING_ZERO_FALLBACK 或 DEFAULT）由 **1号位 在回执表确认**。

---

## 四、责任清单

| # | 动作 | 责任 | 说明 |
|---|---|---|---|
| 1 | `SetupOptimizer.SetupOutcome` 值域/命名对齐 4 值 + SameProductZero 归并实现 | 1号位 | Scheduling 层；新增集成测试（SetupTransitionRuleTests.cs 同步）|
| 2 | `DomainSolveResult.cs:66-69` 值契约更新为 4 值映射 | 1号位（映射）+ 3号位（契约登记）| 删除 NONE/SAME_PRODUCT 两行 |
| 3 | `GanttDataDto.cs:176` 值契约更新为 4 值 | 3号位 契约 / 5号位 查询侧同步 | 前端展示值域 |
| 4 | `Task.SetupSource` 列：无 DDL 变更（列已存在）| 2号位 确认 | 值域收窄不影响列类型 |
| 5 | SetupTransitionRule 治理侧：**不产出** SAME_PRODUCT/NONE 来源（3号位 发布前校验不含该值）| 3号位 | 规则表 RuleType 本就仅 EXACT/DEFAULT，无需变更 |
| 6 | 4号位 前端 SetupSource 展示值域同步 4 值 | 4号位（经 3号位 中转）| 现 NONE/SAME_PRODUCT 展示处理移除 |

---

## 五、回执表

| 问 | 1号位 | 2号位 | 5号位 |
|---|---|---|---|
| §3 映射建议（含 SameProductZero 归并）是否认可 | （待回执）| — | — |
| §四-4 Task.SetupSource 无 DDL 确认 | — | （待回执）| — |
| §四-3 GanttDataDto 4 值契约同步确认 | — | — | （待回执）|

---

*回执后：登记进契约文档《01_核心数据结构契约》Task.SetupSource 字段表（4 值）+ 1号位 实施枚举统一 + 5号位 查询契约同步 + 4号位 前端值域。*
