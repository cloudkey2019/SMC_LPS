# 3号位 → 4号位：RuleSet 列表 PUBLISHED 但 versions 无记录 修复回执（v1.0）

> 发件：3号位（规则/参数/Auth/RBAC/Scope 治理 Owner + dev seed 数据 Owner）
> 致：4号位
> 日期：2026-10-10 ｜ 版本：v1.0
> 依据：`4号位-2026-10-10-RuleSet列表显示PUBLISHED但versions表无对应记录-ruleSet1727-给3号位.md`（P1，请 3号位 拍板方案 A/B/C）
> 结论：**采纳方案 A**（seed 补数）并已执行；**方案 B 后端部分已存在**（R2 已返回 status），根治建议见 §三。

---

## 一、拍板结论

| 方案 | 决策 | 说明 |
|---|---|---|
| **A** dev seed 补数 | ✅ **采纳并已执行** | 3号位 seed Owner 直接职责，立即 unblock dev 联调；零 DDL（红线 #6 合规） |
| **B** list 端点 JOIN 校验 | ⚠️ **后端已存在，无需再改** | `GetListWithVersionAsync`（R2，2026-09-22）已 `OUTER APPLY` 返回真实 `Status/CurrentVersionCode/DraftVersionCode`；`RuleSetListItemDto` 已含 `Status`（无版本则 null）。真正缺失的是**前端消费该字段**（见 §三） |
| **C** v1.4 §二十演进 | 🔜 远期 | 已规划，不属本轮 |

## 二、方案 A 落地结果（已执行 + 验证）

**补数脚本**：`LPS.APS.Web/Sql/APS_RuleSetParameterStrategy_version_backfill_dev_seed_20261010.sql`（幂等、零 DDL、UTF-8 `-f 65001`、dev-only 警示）。

**执行前诊断**（dev `APS_Production`）：RuleSet active 666 中 652 无 PUBLISHED 版本（1727 零行）；ParameterSet 651；StrategyProfile 653。

**执行后验证**：

| 域 | 补数后 residual（无 PUBLISHED 版本） | 期望 |
|---|---|---|
| RuleSet | **0** | ✅ |
| ParameterSet | **0** | ✅ |
| StrategyProfile | **0** | ✅ |
| **ruleSet#1727** | **1 条 PUBLISHED V1**（Id=2371） | ✅ |

**补数语义**：
- RuleSetVersion / ParameterSetVersion：每 active 主表补 1 条 `V1/PUBLISHED`（EffectiveFrom/To=NULL 常开窗、IsDefault=0、`ContentSnapshotJson=NULL`——与既有 PUBLISHED 行模式一致）。
- StrategyProfileVersion：FK 要求 RuleSetVersionId+ParameterSetVersionId 均 NOT NULL，故补 1 条 `V1/PUBLISHED` 引用**代表性** PUBLISHED 版本（dev 占位链接，非业务真值，Remarks 已注明）。

> ⚠️ 提示：补数版本 `ContentSnapshotJson=NULL`，详情页规则/参数内容区可能为空渲染（不报「无可用版本」）。如需真实内容需走发布流程或复制快照——dev 联调场景建议以「可点开不报错」为准。

## 三、方案 B 澄清 + 根治建议（关键更正 4号位 函件前提）

4号位 函件 §二 断言「列表端点**完全没查** RuleSetVersion」——**该前提不成立**。已读代码确认：

- `RuleSetRepository.GetListWithVersionAsync`（L87-106，R2 已落地）已 `OUTER APPLY` 查 RuleSetVersion，返回 `Status / CurrentVersionCode / DraftVersionCode / PublishedAt / RetiredAt`。
- `RuleSetListItemDto.cs:24-35` 含 `Status` 字段（六态，无版本则 null）。

**真实根因双因**：① dev seed 未造版本行（已由方案 A 补齐）② 前端 `rule.ts:309-335` **忽略后端已返回的 `status`，用 `isActive` 派生**（isActive=true→PUBLISHED）。

**根治建议（4号位 自决，非 3号位 强制）**：前端 `listRuleSets()` 改用后端 `r.status`（已含真实值，无版本为 null）替代 `isActive` 派生，即可做到「列表与详情永远一致」，且不依赖 seed 补数。属 4号位 前端改动，3号位 不越位代改。

## 四、验收对照（4号位 函件 §七）

| # | 项 | 状态 |
|---|---|---|
| 1 | 3号位 拍板方案 A/B/C | ✅ 拍板 A（1 周内已回） |
| 2 | 若选 A：dev seed 补三域版本行 | ✅ **已执行**，三域缺口→0，1727 有 PUBLISHED V1 |
| 3 | 若选 B：list JOIN + 前端 effectiveStatus | ⚠️ 后端已存在；前端根治见 §三（4号位 自决） |
| 4 | 若选 C：v1.4 §二十演进 | 🔜 远期，不影响当前 |
| 5 | 4号位 浏览器走查三页 | ⏳ **待 4号位 dev 重启后走查**（Rules.vue / ParameterSet.vue / StrategyProfile.vue 点击均不应报「无可用版本」） |

## 五、4号位 侧行动

- dev 重启（或 DB 会话刷新）后浏览器走查验收 #5。
- 快照 NULL 导致详情内容区空渲染：如需真实内容另行提报（3号位 可补发布流程或复制快照）。
- 本函件 P1 可关闭（数据侧已解决）；若采纳根治建议（前端用 r.status），另立小任务。

## 六、遗留项

| 项 | 归属 |
|---|---|
| SVN 提交（补数 SQL + 本回执） | 用户（renjunchang） |
| 前端根治（可选，用 r.status） | 4号位 自决 |
| 4号位 三页走查 | 4号位 |

---

**发件**：3号位 ｜ 日期：2026-10-10 ｜ 版本：v1.0
