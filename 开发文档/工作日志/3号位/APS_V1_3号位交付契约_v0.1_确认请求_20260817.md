# APS V1 3号位交付契约 v0.1 —— 2号位 / 5号位 确认请求单

**版本**：v0.1（确认稿）
**日期**：2026-08-17
**收件方**：2号位、5号位
**发起方**：3号位
**性质**：《APS_V1_3号位交付契约_v0.1_20260817.md》的配套确认单。请 2号位 / 5号位对契约中需跨号位确认的内容**逐项回复"确认 / 需修改"**，确认后契约定稿（v0.2），作为阶段 B（FrozenStrategySnapshot）与阶段 D（Supply/Procurement 参数）的实现依据。
**上位依据**：
- 0号位裁决 A（FrozenStrategySnapshot 归属：3号位构建、2号位 Run 启动一次装载）、裁决 C（FrozenFactParameters = Snapshot 参数子集投影，非独立版本体系）、裁决 D（ETA 为冻结 Invariant）
- 《APS_V1_关键接口冻结_1-2_2-5_2-3_v1.0_20260814.md》§4.2（Snapshot 最小内容）、§3.2（5号位 PI Position 接口签名）、§3.3（Procurement/Timed Supply 边界）
- 完整契约：《APS_V1_3号位交付契约_v0.1_20260817.md》

> 确认红线（契约既定，不改）：**不新增表、不新增独立版本号**（裁决 C）；一次 Run 一份规则真相；Cache Key 必须含 VersionId（清单 51）。

---

# 一、请 2号位确认（6 项）

| # | 确认项 | 契约/依据 | 2号位确认 |
|---|---|---|---|
| **C2-1** | **Run 启动一次装载语义**：2号位只读 PUBLISHED 版本，在 ScheduleRun 启动时调用 3号位 Provider `GetFrozenStrategySnapshotAsync(strategyProfileVersionId, ct)` **一次**，结果本 Run 内存使用；**不逐笔 RPC 调 3号位**（禁止每分配一笔 Demand 就问"该怎么排"） | 裁决 A / 冻结 4.1 / 清单 53 | ☐ 确认 / ☐ 需修改 |
| **C2-2** | **冻结锚点一致性**：一个 `FrozenStrategySnapshot` 内 `StrategyProfileVersionId`、`RuleSetVersionId`、`ParameterSetVersionId` 三者来自**同一 StrategyProfileVersion 包**，一次 Run 各 Domain 版本一致（禁止 A 域上午规则、B 域中午新规则） | 清单 12 / B4 / 契约 §二 | ☐ 确认 / ☐ 需修改 |
| **C2-3** | **IsDefault 绑定逻辑不变**：2号位现有 `ScheduleRunService.CreateScheduleRunAsync` 的默认版本选择 SQL（`Status='PUBLISHED' AND IsDefault=1 AND RunType='FULL_SCHEDULE' ORDER BY PublishedAt DESC`）**保持不变**；3号位负责维护"同一包仅一个 IsDefault=1 且 PUBLISHED"不变量，不反向改动 2号位 SQL | 盘点结论 §三.3 / 契约 §六-3 | ☐ 确认 / ☐ 需修改 |
| **C2-4** | **Cache Key 约定**：Snapshot 缓存键为 `frozen-strategy-snapshot:{strategyProfileVersionId}`，**必须含 VersionId**（不同版本不互相污染）；Run 启动装载后 **Run 内不刷新**（中途发布新版本不影响本次 Run） | 清单 51 / 裁决 A / 契约 §五 | ☐ 确认 / ☐ 需修改 |
| **C2-5** | **六块结构 ↔ 冻结 4.2 三块覆盖**：`FrozenStrategySnapshot` 六块（DemandPriority / Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail）是冻结文档 4.2 三块（Demand排序 / Supply-Lock / Solver策略）的细拆，双向无遗漏、无多余 | 冻结 4.2 / 契约 §二 | ☐ 确认 / ☐ 需修改 |
| **C2-6** | **FrozenFactParameters 转交方**（开放点，请 2号位拍板）：5号位 PI Position 接口（冻结 3.2）需要 `FrozenFactParameters`。**投影抽取由谁做？** 建议：**3号位在 Snapshot 内提供投影方法**（如 `Snapshot.ToFrozenFactParameters()`），2号位拿到 Snapshot 后转交即可——2号位无需知道投影规则；备选：2号位自行从 Snapshot 抽取（需 2号位维护投影字段清单）。**请确认抽取方** | 裁决 C / 冻结 3.2 / 契约 §四 | ☐ 3号位抽（建议） / ☐ 2号位抽 / ☐ 需修改 |

**2号位补充意见**：____________________________________________

---

# 二、请 5号位确认（3 项）

| # | 确认项 | 契约/依据 | 5号位确认 |
|---|---|---|---|
| **C5-1** | **FrozenFactParameters 字段覆盖**：投影包含 `DefaultPurchaseLt`（按 Warehouse/Material）、`OverdueMargin`（MarginPercent/MinimumExtraDays）、`ArrivalToUsableOffsets`（按 Warehouse）、`ProtectionTrigger`、`InventoryAvailability`、`ProcurementSort`。**是否覆盖 PI Position 与 Timed Supply 计算所需的全部冻结参数？** 如缺，请列出缺的字段 | 冻结 3.2/3.3 / 裁决 C / 契约 §四 | ☐ 覆盖 / ☐ 缺字段：________ |
| **C5-2** | **接口签名参数一致**：5号位 `CalculateProductionInstructionPositionsAsync(inputs, FrozenFactParameters parameters, ct)` 的 `parameters` 即为本投影类型（以 `StrategyProfileVersionId` 为唯一锚点，**无独立版本号**），5号位按 3.2 消费，无类型/语义歧义 | 冻结 3.2 / 裁决 C | ☐ 确认 / ☐ 需修改 |
| **C5-3** | **投影边界（不含项）确认**：投影**不含** Demand 排序、Solver 策略、Candidate Guardrail（5号位不需要）；5号位**不直接 RPC 调 3号位**，只消费经 2号位一次转交的投影。确认 5号位无需额外块 | 裁决 C / 冻结 3.2 红线 | ☐ 确认 / ☐ 需修改 |

**5号位补充意见**：____________________________________________

---

# 三、确认后动作

1. 2号位/5号位逐项回复后，契约 v0.1 → **v0.2 定稿**（含 C2-6 抽取方结论）
2. 字段名 / 落位（建议 `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs`）定稿
3. 3号位进入阶段 B：实现 `GetFrozenStrategySnapshotAsync` → 与 2号位联调（交付物 11）
4. 阶段 D 按定稿的 ④Procurement 实现 Supply/Procurement 参数

---

# 四、确认反馈格式

请按以下格式回复（可逐项勾选）：

```text
2号位：C2-1 [确认/需修改]  C2-2 [...]  ...  C2-6 [3号位抽/2号位抽/需修改]
5号位：C5-1 [覆盖/缺字段:...]  C5-2 [...]  C5-3 [...]
补充意见：...
```

---

*本确认单由 3号位依据 0号位裁决 A/C/D 与《关键接口冻结》生成；确认结果将回填至《APS_V1_3号位交付契约_v0.1_20260817.md》并定稿为 v0.2。*

---

# 五、0号位统一确认结果（2026-08-17，本单已关闭）

0号位依据已冻结业务基线、接口冻结、实施包及已冻结的 2号位代码审核结论，**统一代 2号位/5号位确认，不再等待人员到岗二次裁决**。逐项结论（详见《0号位对交付契约做出的答复.md》与《APS_V1_3号位交付契约_v0.2_20260817.md》§〇）：

| # | 结论 | 要点 |
|---|---|---|
| C2-1 | 需修改 | 按 Run 冻结的指定 VersionId 一次获取 Snapshot；不重新选 Default、不因后续发布漂移 |
| C2-2 | 确认 | 三 VersionId 同一包；一 Run 所有 Domain 一致 |
| C2-3 | 需修改文字 | 不冻结 SQL；语义=未显式指定时按 RunType 取唯一无歧义默认 PUBLISHED |
| C2-4 | 需修改文字 | Cache Key 必须含 VersionId + 不污染 + Run 内不刷新；字符串不冻结 |
| C2-5 | 需补充后确认 | 六块保留；PlanningYield 必须进入 Snapshot（归属 ④ProcurementBlock） |
| C2-6 | **2号位抽** | 3号位只提供完整 Snapshot；2号位集成层 Mapping 抽取投影转交 |
| C5-1 | 需修改 | 投影收缩为 `StrategyProfileVersionId + DefaultPurchaseLt + OverdueMargin + ArrivalToUsableOffsets`；删除 Protection/Inventory/ProcurementSort；不新增 UNLOCATED 容忍率 |
| C5-2 | 确认 | 无独立版本体系；父锚点追溯；不新增表/版本号 |
| C5-3 | 确认（按 C5-1） | 不含 Demand/Solver/Candidate 及 2号位执行规则；5号位不直接 RPC 3号位 |

**确认性质**：均为契约文字/字段边界收敛，**无新增 2号位整改项，未改变已冻结的 2号位审核意见**。2号位回来后仅做代码联调。

**本单状态**：☑ 已由 0号位统一确认关闭。契约已定稿为 **v0.2**（`APS_V1_3号位交付契约_v0.2_20260817.md`），作为阶段 B 实现依据与 2号位/5号位联调输入。无需再向 2号位/5号位发送本单。
