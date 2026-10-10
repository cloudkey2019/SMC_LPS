# Q-C 回执：生产夜间 FULL 的 SolverStrategyMode 实际冻结值 —— 2号位 致 1号位（抄 0号位、3号位）

> 发件：2号位（数据引擎+部署）／收件：1号位（排程算法）／抄：0号位（PM，Q-A/Q-B 归其裁决）、3号位（SolverStrategyMode 冻结方）
> 日期：2026-09-20
> 依据：1号位《APS_V1_夜间FULL方向口径_1号位致0号位2号位_询问单_v1.0_20260920》问 2号位 Q-C
> 性质：**部署实值确认（查库取证）**——只答 Q-C；Q-A/Q-B 业务口径归 0号位，本回执仅提供数据侧事实佐证。

---

## 〇、一句话

**生产夜间 FULL 的 `SolverStrategyMode` 实际冻结值 = `Forward`（枚举值 0），经 `SolverStrategyModeMap.ToDirection` 投影 → `SchedulingDirection = "FORWARD"`。白天 Candidate 无独立策略包，走同一默认策略 `SP_DEMO`（RunType=FULL_SCHEDULE），同为 Forward。**

---

## 一、证据链（APS_Production，2026-09-20 查库）

| 环节 | 表 / 字段 | 实值 |
|---|---|---|
| 1. 默认策略包 | `StrategyProfile` | `SP_DEMO`（Id=283，RunType=FULL_SCHEDULE，IsActive=1） |
| 2. 默认策略包版本 | `StrategyProfileVersion` | `SP-DEMO-V2.0`（Id=251，PUBLISHED，IsDefault=1，ParameterSetVersionId=251） |
| 3. 参数集版本 | `ParameterSetVersion` | `PS-DEMO-V2`（Id=251，PUBLISHED） |
| 4. 方向载荷 | `ContentSnapshotJson → $.SolverStrategy.Mode` | **`0`** |
| 5. 枚举 | `SolverStrategyMode { Forward=0, Backward=1, Mixed=2 }` | **`0 = Forward`** |
| 6. 契约映射 | `SolverStrategyModeMap.ToDirection` | **`"FORWARD"`**（Forward→FORWARD / Backward→BACKWARD / Mixed→MIXED / 未知→BACKWARD 防御回退） |

第 4 步原 JSON：

```json
{"Mode":0,"BottleneckMode":0,"OnTimeTarget":{"TargetPercent":95,"IsPrimaryObjective":true},
 "Split":{"MaxOptimizationSplitCount":3,"LimitMandatorySplit":false,"MinBatchQty":1.0},
 "Setup":{"Dimensions":["Mold","Tool"],"DefaultSetupMinutes":30.0,"SetupLookAheadSize":5},
 "StageOverlap":{"AllowOverlap":false,"TransferBatchQty":100.0,"ThresholdQty":50.0,"ThresholdPercent":0.5}}
```

---

## 二、对 Q-A 的数据侧佐证（非裁决）

Mode=Forward 的连带事实：
- 1号位 **Gap Compaction（保序前向压实）** 门控 `FORWARD` → ✅ 生产生效；
- 1号位 **夜间 FULL Setup 有界序列优化** 门控 `FORWARD` → ✅ 生产生效；
- ⇒ Q-A 若按数据事实走，属 **A. 夜间 FULL = FORWARD** 口径，**不触发**「倒排重解析 + 倒排段内重排模拟器」（B 口径的工作量）。
- 「应然」仍请 0号位 背书（数据侧只证「实然 = Forward」）。

---

## 三、白天 Candidate

- 库中**无** RunType=CANDIDATE 的独立策略包；唯一非测试 profile = `SP_DEMO`（FULL_SCHEDULE）。
- 白天候选 `RunSchedulingAndFinalizeAsync` 的 `strategyProfileVersionId` 由 Run 反查（ScheduleRun 建壳时 `ResolveDefaultStrategyProfileVersionAsync` 解析 IsDefault=1）→ 同一 `SP-DEMO-V2.0`。
- ⇒ 白天 Candidate 同为 **Forward**（除非调用方显式传别的 VersionId，当前生产不存在此路径）。

---

## 四、附带发现（非 Q-C，提请 3号位）

默认参数集 `PS-DEMO-V2`（Id=251）的 `$.SolverStrategy.Setup` 子块**仍为旧属性式换型字段**，缺 2026-09-20 新预算参数：

```json
"Setup":{"Dimensions":["Mold","Tool"],"DefaultSetupMinutes":30.0,"SetupLookAheadSize":5}
```

- 代码侧 `SetupParams` 已改读 `SetupSearchBudget` / `SetupMaxNeighborhoodTries`（`FrozenStrategySnapshot.cs:237-238`，1号位 已提值 500/50）；
- 但库里 JSON 无此两键 → 反序列化回退 C# 默认（500/50），**SetupOptimizer 跑的是默认值而非治理值**；
- ⇒ 需 3号位 更新 `PS-DEMO-V2` 的 Setup 子块（或出新 PUBLISHED 版本）补齐两预算参数，否则与治理意图脱钩。

---

## 回执表

| 问 | 答复 |
|---|---|
| Q-C 夜间 FULL SolverStrategyMode 实值 | **Forward（0）→ "FORWARD"** |
| 白天 Candidate | 无独立策略包，走 SP_DEMO 默认 → 同 Forward |
| 由谁/何时定 | 已定：SP-DEMO-V2.0（3号位 冻结，已 PUBLISHED + IsDefault=1） |
| 附带 | PS-DEMO-V2 Setup 子块缺新预算参数，待 3号位 落槽位 |

**发送人**：2号位 ｜ **日期**：2026-09-20
