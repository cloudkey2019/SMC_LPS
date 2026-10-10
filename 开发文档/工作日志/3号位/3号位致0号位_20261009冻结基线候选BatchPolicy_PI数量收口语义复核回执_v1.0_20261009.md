# 3号位致0号位：20261009 冻结基线候选（Batch Policy／Solver 预算／MTS PI 数量收口）语义复核回执 v1.0

**编制**：3号位
**日期**：2026-10-09
**复核对象**：`2026-10-09文档更新/` 候选文档集（冻结基线索引 v1.12 候选 + 11 份正文候选 + 3 份不变正文；规则清单 v1.6 候选 / 废止 v1.4 候选 / 依赖矩阵 v1.4 候选）
**复核口径**：候选未正式生效前，3号位 **不宣布对齐切换、不改动已落码代码**；本回执仅记录语义确认、现状代码差距与候选生效后的对齐落码项（与「正文 PASS ≠ 代码验收」对偶）。

---

## 一、候选语义确认（3号位 立场，PASS）

1. **Batch Policy 正式业务定位粒度 = `MaterialId + ProductionDepartmentId`（明确部门命中）**——确认。
2. **正式业务字段 = 硬 Min/Max、AllowSplit/AllowMerge、经济性软偏好 `PreferredBatchQty`**；物理列保持 `NULL` 历史兼容，不以直接 SQL `NOT NULL` 替代 3号位 后端发布校验——确认。
3. **`PreferredBatchQty` 业务生效必填且 `>0`、处于已配置硬 Min/Max 合法范围内**——确认，由 3号位 后端发布校验承担。
4. **`MaxOptimizationSplitCount` / `MaxBatchCandidates`**：不再作为 `Material+Department` 普通 Batch Policy 业务配置或旧列预算真相；优化拆分尝试上限 / 单问题候选评估预算**由 1号位 确定精确计数范围、边界与压测默认**，无现成动态载体时用 1号位 **有界、可追溯的版本化安全默认**；不得把历史 `3/8` 写死为通用业务上限——确认。3号位 **不新增参数值平台**。
5. **`BottleneckSplitStrategy` / `NonBottleneckStrategy`**：历史兼容列，V1 主链**无论 NULL 或非 NULL 均不得消费**其拆/合批倾向；`PREFER_SPLIT/PREFER_MERGE`、`PREFER_LARGE_BATCH/PREFER_SMALL_BATCH` 仅追溯；既有动态瓶颈与 Anchor 策略不被覆盖——确认。
6. **无匹配 ≠ 存在但生效校验失败**（后者不得走缺配兜底）；**NULL 部门历史记录不默认为所有部门的生效规则**——确认。
7. 本轮**无 DDL 可执行 SQL 变更**（候选 DDL v5.1.8.4 仅修改注释）——确认，物理列不动，对齐纯代码层。

---

## 二、与现状代码差距（候选生效后须对齐项）

现状：按 0号位 2026-10-07 裁决「本轮落码」，3号位 已落地 ⑧ Batch Policy 投影（`TaskSplitRuleConfigProjector`）+ 治理 CRUD（`ITaskSplitRuleConfigGovernanceService` / `TaskSplitRuleConfigController`），当前将 4 个技术/策略字段**全量透传**快照。

| # | 现状（已落码） | 候选冻结要求 | 对齐动作（候选生效后） |
|---|---|---|---|
| ① | `Project` 将 `MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy` 全量写入 `BatchPolicyRuleSnapshot`（⑧ 块） | 4 字段不作为有效预算 / 拆合批倾向来源被主链消费 | ⑧ 投影停止透传 4 字段，快照仅保留正式业务字段（MaterialId / ProductionDepartmentId / Min/Max / PreferredBatchQty / AllowSplit / AllowMerge）；物理列保留历史 |
| ② | `SaveTaskSplitRuleConfigRequest` / `TaskSplitRuleConfigDto` 将 4 字段作为可维护业务字段 | 不再作为普通 Batch Policy 业务配置；两枚举 V1 不得经治理 API 提供有效偏好 | 治理 DTO 移除或降级为历史只读；治理 API 不再接受两枚举为有效策略；Solver 技术预算归 1号位 版本化安全默认 |
| ③ | `Validate` 仅「批量 ≥0、Max≥Min」 | `PreferredBatchQty` 业务生效必填且 `>0`、在硬 Min/Max 内 | 发布/启用校验补强：Preferred>0 且 Min≤Preferred≤Max |
| ④ | `ProductionDepartmentId=NULL` 按「Material 级默认」语义投影快照（DTO 注释如此） | NULL 部门历史记录不默认为所有部门的生效规则 | 生效规则须按明确 `MaterialId+ProductionDepartmentId` 命中；NULL 部门记录不投为全部门生效；DTO/契约注释同步更正 |

---

## 三、生效前立场与缺配兜底口径

- **生效前不改代码**：上述 ①②③④ 均为**候选正式生效后**的落码项；生效前 3号位 保留已落码实现，避免未生效语义先行改写工程事实。
- **缺配兜底**：候选要求「无匹配只生成现有机制中的缺配待维护提示，不由 3号位 计算 Stage 运行时上限、Batch Decision 或新工艺参数」——确认。3号位 现有「⑧ 装载 fail-open + 匹配终端 ④ fail-closed」分工**不变**，缺配提示由现有机制承担，3号位 不新增计算。
- **MTS PI 数量边界**：PI 原始/剩余/当前 Stage 可新成批余额属**运行时数量约束**，不属于 3号位 维护字段——确认，3号位 无相关字段需处理。

---

## 四、待 0号位 / 相关号位 确认

1. **Solver 技术预算治理归属**：候选既要求「3号位 仅在现成、确有真实值载体的参数版本体系中治理 `MaxOptimizationSplitCount/MaxBatchCandidates`」，又「不能假设 `ParameterSetVersion` 自带参数明细」「不新增参数值平台」。3号位 理解 = **本轮 3号位 对两个 Solver 技术预算字段不承担治理动作**，由 1号位 有界、可追溯、版本化安全默认承载。请 0号位 确认该理解。
2. **⑧ 快照收窄影响 1号位 消费侧**：4 字段停投后 `BatchPolicyRuleSnapshot` 形状收窄，请 1/2号位 复核消费侧（如 `PhaseOneConstraintBuilder` 等）无对 4 字段的有效引用。
3. **验收包 v1.7 新增验收点**（有效规则按 MaterialId+明确部门命中；Preferred>0 在硬 Min/Max 内由 3号位 后端及 4号位 页面校验）——3号位 在候选生效后落码时同步覆盖。

---

## 五、结论

候选语义与 3号位 既有职责边界一致，**PASS**；差异集中在 ⑧ 快照透传范围与发布校验强度，属**候选生效后的代码对齐项**，生效前不先行改写。
