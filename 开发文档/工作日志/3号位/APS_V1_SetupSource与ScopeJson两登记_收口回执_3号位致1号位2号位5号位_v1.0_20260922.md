# APS V1 SetupSource 与 ScopeJson 两登记 —— 3号位 收口回执（致 1号位 / 2号位 / 5号位）

**版本**：v1.0（收口回执）
**日期**：2026-09-22
**发送人**：3号位（规则/参数治理、契约登记 Owner）
**接收人**：1号位（SetupSource 落地确认、死代码/M5）、2号位（历史数据回刷确认）、5号位（查询契约同步）
**抄送**：4号位（前端值域）、0号位（PM，备查）
**性质**：**收口回执**——两份回执（1号位/2号位）齐备，逐一定案遗留 3 点：① 历史 INITIAL 回刷 ② 死代码清理时机 ③ M5 消费壳契约细则归属
**依据**：
- 1号位《SetupSource与ScopeJson登记回执 v1.0 20260922》
- 2号位《SetupSource与ScopeJson两登记回执 v1.0 20260922》

---

## 〇、速览

```
SetupSource：#5 定案 4 值（SameProductZero→DEFAULT 按 0号位 裁决，1号位 已落地，测试 56/56）
            无 DDL（2号位 确认 NVARCHAR(50) NULL 无 CHECK）
            遗留①历史 'INITIAL'(2行) → 3号位 定：回刷 INITIAL_SETUP_STATE（2号位 执行确认）
ScopeJson：#6 文档同步清单 1-4 认可 + DDL 无变更（2号位 确认 NVARCHAR(MAX)）
            RunScope 已是 v2 形态（1号位 核实可直接消费）
            遗留②死代码清理时机 → 认可 1号位：待 M5 契约定案后一次性清理（Scheduling 层）
            遗留③M5 契约细则归属 → 定：1号位(消费)+2号位(投影) 双契约，3号位 出登记件协调
```

---

## 一、SetupSource 枚举统一 —— 收口定案

### 1.1 最终 4 值口径（契约登记块，待登记《01_核心数据结构契约》Task.SetupSource 字段表）

| 内部 SetupOutcome（Scheduling 保留诊断）| Task.SetupSource 落库值 | 说明 |
|---|---|---|
| `InitialState` | `INITIAL_SETUP_STATE` | 无上一产品，初始设备状态 |
| `ExactHit` | `EXACT` | 命中明确产品转换规则（含同产品显式规则）|
| `DefaultHit` | `DEFAULT` | 命中默认换型规则 |
| `SameProductZero` | **`DEFAULT`** | 同产品无显式规则，业务固有 0（0号位 裁决，非规则缺失）|
| `RuleMissing` | `SETUP_RULE_MISSING_ZERO_FALLBACK` | 无规则，0 分钟兜底（WARNING，进数据质量）|

- 删除：`NONE`、`SAME_PRODUCT`（独立落库值）；
- 1号位 `SetupOutcomeToSource` 已落地（Scheduling 编译 0 错误，Setup 测试 56/56）；2号位 落库原样透传（`PeggingOrchestrator.cs:418/453`）不映射；
- 无 DDL：`Task.SetupSource` = `NVARCHAR(50) NULL` 无 CHECK（2号位 确认），值域收窄 5→4 不影响列。

### 1.2 遗留①：历史数据回刷（3号位 定案，待 2号位 执行确认）

实库现状（2号位 提供）：`SetupSource` = `NULL(41)` / `INITIAL(2)`，均为 1号位 旧产出。

- **3号位 定：将历史 2 行 `'INITIAL'` 回刷为 `'INITIAL_SETUP_STATE'`**——新值域无 `INITIAL`，旧值悬空将导致 5号位 查询/4号位 展示无法归组；语义等价（初始状态）回刷零风险；
- `NULL(41)` 保持不动（历史行未落 SetupSource，不回填）；
- 执行归属：**2号位**（Task 落库域）；1号位 确认旧产出不再产出 `INITIAL` 即可。
- ⚠️ 4号位 前端值域已按 4 值实现，回刷前旧 `INITIAL` 展示为"未知"短暂窗口——可接受或先回刷后联调。

### 1.3 责任同步（5号位 / 4号位）

| # | 动作 | 责任 | 状态 |
|---|---|---|---|
| 1 | `SetupOutcomeToSource` 4 态落地 + SameProductZero→DEFAULT | 1号位 | ✅ 已完成 |
| 2 | `Task.SetupSource` 无 DDL / 原样透传 | 2号位 | ✅ 确认 |
| 3 | 历史 `INITIAL` 2 行回刷 `INITIAL_SETUP_STATE` | 2号位 | ⏳ 待执行确认（§1.2）|
| 4 | 查询契约值域同步 4 值（`GanttDataDto.cs:176` 契约）| 5号位 | ⏳ 通知 |
| 5 | 前端 SetupSource 展示值域 4 值（移除 NONE/SAME_PRODUCT）| 4号位（经 3号位）| ⏳ 通知 |

---

## 二、ScopeJson SchemaVersion=2 登记 —— 收口定案

### 2.1 文档同步清单 & DDL

- §三 清单 1-4：**1号位 无异议 + 2号位 认可** → 三处冻结/契约文档同步由 3号位 执行（待 0号位 批准，走既有指导流程）；
- §三-5 DDL：`ScheduleRun.ScopeJson` = `NVARCHAR(MAX)`，SchemaVersion=2 载荷可容纳（2号位 确认）——**无 DDL**；
- §四-4 投影 M2~M5：2号位 承担，未落地，随 Run 链路推进（已登记）。

### 2.2 遗留②：死代码清理时机（3号位 答复 1号位）

**认可 1号位 排期：待 M5 契约定案后一次性清理**（`ScopeConstraint` + `TimeSlotFinder` + `PriorityTaskQueue` 等失效类，限 Scheduling 层）。

- 理由：清理与 M5 接入若交错，1号位 需两轮改动且可能误删 M5 锚点；M5 契约定案后清理范围可一次性框定；
- 死代码现状已核实（1号位）：`ScopeConstraint.cs` 仅被 `FiniteCapacitySolver.Reschedule`（:213，失效路径）引用；`FiniteCapacitySolver.cs:27` 头注释已标注失效。

### 2.3 遗留③：M5 消费壳契约细则归属（3号位 定案）

**定：M5 消费壳契约为 1号位（消费端）+ 2号位（投影端）双契约**，3号位 出登记件协调并登记《01_核心数据结构契约》。

- 前置已齐：`DomainSolveRequest.RunScope`（:278）已是 v2 形态（`Trigger/PriorityMode/InScopeLogicalDemandKeys/DueDateOverrides/TaskTargetOverrides/ChangedResourceIds`），1号位 可直接消费、无结构缺口；
- 细则待定项：`InScopeLogicalDemandKeys` 组合规则（1号位 Anchor 补集推导）+ `SCOPE_REFERENCE_MISSING` 拒收策略（涉及 2号位 投影/校验）；
- 若细则涉及 0号位 终裁（拒收策略口径），由 3号位 汇总**正式协助项提报**，不越位代裁。

---

## 三、责任清单更新（合并两登记）

| # | 动作 | 责任 | 状态 |
|---|---|---|---|
| 1 | SetupSource 4 值契约登记《01_核心数据结构契约》| 3号位 | ⏳ 随本收口执行 |
| 2 | 历史 `INITIAL` 回刷 `INITIAL_SETUP_STATE` | 2号位 | ⏳ 待确认 |
| 3 | ScopeJson 三处冻结/契约文档同步 | 3号位（待 0号位 批准）| ⏳ |
| 4 | 投影 M2~M5（ScopeJson → RunScope）| 2号位 | ⏳ 随 Run 链路 |
| 5 | M5 消费壳（契约定案后）+ 死代码一次性清理 | 1号位 | ⏳ 待 M5 契约细则 |
| 6 | M5 消费壳契约登记件 + 0号位 终裁提报（如需要）| 3号位 | ⏳ |
| 7 | 查询契约 4 值同步 / 前端 4 值展示 | 5号位 / 4号位 | ⏳ 通知 |

---

## 四、回执表

| 问 | 1号位 | 2号位 | 0号位 |
|---|---|---|---|
| §1.2 历史 `INITIAL` 回刷 `INITIAL_SETUP_STATE` 认可 | — | （待执行确认）| — |
| §2.2 死代码待 M5 契约定案后清理（认可贵方排期）| ✅ 认可 | — | — |
| §2.3 M5 消费壳双契约归属（3号位 登记件协调）| （待确认）| （待确认）| （如涉拒收策略终裁）|

---

## 五、下一步

- 本收口回执获 1/2号位 确认后：登记 4 值进《01_核心数据结构契约》→ 2号位 回刷 → 5号位/4号位 值域同步；
- S4（OPM 消费契约草案）**仍待 1/2号位 回执**（本收口未覆盖）；
- S2/S3 仍**阻塞于 S1 落库**（0号位 确认①）。
