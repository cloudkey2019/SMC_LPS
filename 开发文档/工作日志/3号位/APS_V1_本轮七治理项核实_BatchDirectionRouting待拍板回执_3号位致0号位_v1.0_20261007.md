# APS V1_本轮七治理项核实_Batch/Direction/Routing 待拍板回执

**致**：0号位
**自**：3号位
**版本**：v1.0
**日期**：2026-10-07（北京时间）
**上位文档**：《2026-10-07 文档更新》——0号位总体项目验收包 v1.6 + 最终全部流程与业务基线 v1.8 + 3号位规则参数实施包 v1.7

---

## 一、回执目的

通读本轮三份上位文档后，3号位 将 3号位实施包 v1.7「本轮新增治理项」逐项核实。结论：**7 项中 4 项已收口无遗留（#3/#5/#6/#7），3 项（#1 Batch Policy / #2 Direction / #4 Routing 治理）须 0号位 拍板、且 #4 另涉 2号位 DDL 协助**，特此提报。

## 二、已收口项（备忘，免 0号位 重复关注）

| # | 治理项 | 结论 |
|---|---|---|
| #3 | 阶段提前期参数（StageLeadTimeParam）三级匹配层级 | 消费侧 1号位 已只认 DEPT_EXACT→FACTORY_STAGE_DEFAULT→GLOBAL_STAGE_DEFAULT；3号位 侧 4 处注释已随 v1.7 回写对齐，无逻辑改动 |
| #5 | FINAL Material 负面清单 | 全仓 `.cs` 零命中 `IsFinal`/`FinalFlag` 第二真相字段与任何 FINAL 判断引擎类名，3号位 未越界 |
| #6 | 候选输入（Candidate Input）v2 字段 | v1.7 §6.2 七字段（SchemaVersion/BusinessTriggerType/PriorityMode/OrderTargets/TaskTargets/ChangedResourceIds/BasePlanVersionId）已全落码，零缺口 |
| #7 | 数据库写边界 | 3号位 层无 SqlBulkCopy、无直写 Task/FinalTask/Pegging，只写治理/Auth 库；无越界写 |

## 三、#1 Batch Policy + #2 Direction —— 待 0号位 拍板「是否本轮落码」

**治理对象与粒度**（v1.7 第 27～28 行）：

- **批量策略（Batch Policy）**：以「物料（Material）+ 生产部门（ProductionDepartment）」为主要业务粒度，治理最小批量（MinBatch）/ 最大批量（MaxBatch）/ 偏好批量（Preferred）/ 允许拆分（AllowSplit）/ 允许合并（AllowMerge）/ 优化拆分预算等参数版本。
- **方向策略（Direction）**：治理正向排程（FORWARD）/ 逆向排程（BACKWARD）/ 混合（MIXED）/ 自动（AUTO）选择策略，**不按订单类型（OrderType）硬编码**。

**现状**：2026-09-28 裁决已定承接方案——`TaskSplitRuleConfig` 主题表 + `FrozenStrategySnapshot.BatchPolicy` 快照两级承接、`Mode += AUTO`、缺策略 fail-closed，**但当时裁「本轮不落码」**。

**矛盾点**：v1.7 实施包（2026-10-05）已将二者列为「本轮新增治理项」，与 09-28「不落码」口径存在时间差。

**待 0号位 拍板**：⚠️ 排程策略参数（Batch Policy / Direction）**是否本轮落码**？

**若落码，3号位 侧范围**：维护上述参数的 CRUD + 版本化 + 冻结快照投影（`BatchPolicy` 快照两级承接），供 2号位 Run 装载、1号位 Solver 消费。

**3号位 边界（不越位）**：不执行批量计算、不执行方向求解、不参与 Solver——此二项仅**策略参数治理**。

## 四、#4 Routing 静态治理 —— 待 0号位 拍板范围 + 待 2号位 定 DDL 载体

**v1.7 第 30 行治理项原文**：支持真实 RouteCode+PathId 的启用/停用、优先级/偏好等静态治理；3号位 不执行动态路径求解。

**现状核实（3号位 侧）**：

1. **治理对象错位**：现有 `RoutingOperationGovernanceService` 实为「工序工艺规划属性（OperationPlanningMode，OPM）治理」（T1·S2/S3，已落码），其接口仅 `ListOperationsAsync` + `UpdateOperationPlanningModeAsync`，**不是**「Routing 启用/停用、优先级/偏好」静态治理。
2. **端点/字段缺失**：接口、实体、DTO 均无「启用/停用（IsEnabled）、优先级（Priority）、偏好（Preference）」字段与方法。
3. **真实 RouteCode/PathId 口径冲突**：`RoutingOperationDto.RouteCode` 默认 `"DEFAULT"`、`PathId` 默认 `1`；`RoutingPlanningParam.cs` 注释仍写「RouteCode V1固定'DEFAULT'、PathId V1固定1，V2 才扩展多路径」。**与红线 Q1「DEFAULT/1 不得作为归一化业务真值」冲突**，真实多 Routing 未贯彻到治理投影层。

**待 0号位 拍板**：Routing 静态治理**是否纳入本轮落码范围**？

**待 2号位 协助（触红线 #6）**：若落码，须 2号位 先定「启用/停用、优先级/偏好」字段的 **DDL 载体**（落在 `RoutingOperation` 还是 `RoutingPlanningParam`），3号位 再按原定载体落治理端点（红线 #5 新增契约，不改冻结签名）。

## 五、统一前置提醒

本轮回执所依三份文档（v1.6 / v1.8 / v1.7）均标「**正式变更态，待指导位 Step 9 复审，尚未冻结生效**」。上述 #1/#2/#4 三项若裁决本轮落码，建议在 **Step 9 复审通过后**再动代码，避免「代码先于冻结」反推（对照顺序：业务→数据模型→接口→代码）。

## 六、请 0号位 答复（三问）

1. **#1/#2**：排程策略参数（批量策略 Batch Policy、方向策略 Direction）是否本轮落码？（是 / 否 / 维持 09-28「不落码」口径）
2. **#4**：Routing 静态治理是否纳入本轮范围？（是 / 否 / 顺延 V2 多 Routing 一并做）
3. **#4（若为「是」）**：是否同步知会 2号位 启动「启用/停用、优先级/偏好」字段的 DDL 载体定稿？

3号位 将按 0号位 答复执行，不自作主张、不越位。