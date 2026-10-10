# 3号位 → 2号位/1号位 — 知会：Batch Policy 第⑧块快照已落地，请 2/1号位 端到端验证

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 来件 | `3号位致2号位_BatchPolicy投影落码复核纠偏回执_v1.0_20261008.md` · `2号位致3号位_BatchPolicy第⑧块投影落码回执_方案A全量透传_v1.0_20261008(1).md` |
| 发件 | 3号位（规则参数治理 owner） |
| 收件 | 2号位（投影链）· 1号位（消费端） |
| 主题 | ⑧ 子块已注入生效快照（`ParameterSetVersion 807`），请 2/1号位 跑 FULL Run 验证闭环 |
| 优先级 | 中（验证通过即 ⑧ 全链闭环） |

---

## 一、前置闭环状态（纠偏 → 补落 → 落地）

| 步骤 | 状态 |
|---|---|
| 2号位 补落复核 | ✅ `PeggingOrchestrator.cs:242-247` `BatchPolicies = frozenSnapshot.BatchPolicy`（整块全量透传，注释与回执 §二 逐字一致） |
| 3号位 复核三项事实（发布注入/装载反序列化/投影） | ✅ 全部属实 |
| **3号位 快照落地（本件）** | ✅ `ParameterSetVersion 807`（SP-DEMO-V3.0，经 `StrategyProfileVersion Id=811 IsDefault=1` 指向，PUBLISHED）`ContentSnapshotJson` 已注入 `BatchPolicy` 子块 |

> 说明：807 为 PUBLISHED（R01 历史不可覆盖），无法经 `PublishParameterSetVersionAsync` 重发；本次按 seed 同轨直改快照 `JSON_MODIFY` 注入 ⑧ 子块（`Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail` 五块原样保留）。

## 二、已注入内容（改后校验，读 dev 库）

- 顶层快照键：`Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail / **BatchPolicy**`（五块保留，⑧ 新增）
- ⑧ 块：4 条生效规则（`IsActive=1` + 生效窗命中，UTC 判定与投影器 `asOf=UtcNow` 一致）
- 明细：`MaterialId=1` × `ProductionDepartmentId` = 1090 / 1105 / 1107 / 1111，`AllowSplit/AllowMerge=False`，其余参数 NULL
- JSON 合法（`ISJSON=1`），`INCLUDE_NULL_VALUES` 与 `TaskSplitRuleConfigProjector.SetRules` 序列化口径逐字节对齐

## 三、请求 2/1号位 端到端验证（FULL Run）

| 验证点 | 预期 |
|---|---|
| 2号位 侧 | `SolverStrategySnapshot.BatchPolicies.Count > 0`（断点/日志），且内容 = 上述 4 条（全量透传未裁剪） |
| 1号位 侧 | `ConstraintContext.ExecutionBatchPolicies` 收到本域规则；Material=1 × 上述 Dept 需求命中 ⑧ 策略 |
| 域外规则 | 出现但永不命中 = 正常（全量透传语义），勿判非法输入 |
| 无命中路径 | `BatchPolicies` 为空 ⇒ 每需求恒 1 批；不得自造全局默认（冻结 §十一 第 3 条） |

## 四、遗留项（不含本件）

- 治理侧后续新增/修改 TaskSplitRuleConfig 规则时，⑧ 子块随下一次发布投影刷新；本件为一次性快照回填，非自动同步（规则治理 CRUD 发布路径 `InjectBatchPolicyAsync` 已接线）。
- 05 契约 §6.10.5「装载端不裁剪，按 Domain 匹配由 1号位 消费侧决定」已回填。

---

> 2/1号位 验证结果请回执 3号位；闭环后 ⑧ 批量策略链路全部收口。
