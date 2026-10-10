# 3号位 → 4号位：TaskSplitRuleConfig dev 库 schema 已补齐 · 请跑真实联调（v1.0）

> 发件：3号位（Batch Policy 治理 Owner）｜ 致：4号位（前端）｜ 抄：0号位 ｜ 日期：2026-10-07
> 关联：4号位《TaskSplitRuleConfig-dev库schema阻塞-给3号位》(2026-10-07)；0号位 正式裁决（`未命名的Markdown文件 (4).md`）

---

## 一、阻塞已解

dev 库 `TaskSplitRuleConfig` 已补齐正式 DDL v5.1.8.3 的 8 列并核对重建索引：

- 列：`ProductionDepartmentId` / `MinExecutionBatchQty` / `MaxExecutionBatchQty` / `PreferredBatchQty` / `AllowSplit` / `AllowMerge` / `MaxOptimizationSplitCount` / `MaxBatchCandidates`
- 索引：`IX_TaskSplitRuleConfig_Query(MaterialId, ProductionDepartmentId, IsActive)`（与正式 DDL L4484 一致）

归属口径：DDL 按业务 Owner（0号位 裁决确认），`TaskSplitRuleConfig` = Batch Policy 治理结构，**3号位 直接落地**，不再转 2号位。
补丁件：`开发文档/2026-10-07文档更新/APS_数据库_v5.1.8.3_TaskSplitRuleConfig补列_升级补丁.sql`

## 二、请 4号位 继续

1. 跑 `verify-acceptance.mjs` **t-tsrc 段真实联调**：`TSRC08` 由静态跳过 → 改真实断言（`GET /api/governance/task-split-rule-configs` 返 200 + 数组）。
2. 按你们 §五 后续：store 桩代码 + 路由占位 + TaskSplitRuleConfig 页面（仿 `ManualEta.vue` 简化版）。
3. 完成后发对接完成函给 3号位。

## 三、环境口径（知悉）

- **test**：随本轮测试部署同步同一补丁（勿让 dev/test 长期不同 Schema）。
- **prod**：按正式发布流程，本次 dev 联调不构成提前动生产。

---

**发件**：3号位 ｜ **日期**：2026-10-07 ｜ **版本**：v1.0