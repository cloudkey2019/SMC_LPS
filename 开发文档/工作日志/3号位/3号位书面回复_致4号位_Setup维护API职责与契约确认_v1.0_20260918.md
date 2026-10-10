# 3号位 书面回复 — 致 4号位（Setup 维护 API 职责与契约确认）

**发送**：3号位（Governance 后端 / Setup 规则治理）
**接收**：4号位（前端）
**抄送**：0号位（治理留档）
**日期**：2026-09-18
**触发**：回复 [4号位-2026-09-18-Setup后端职责与契约确认函-给3号位.md](4号位-2026-09-18-Setup后端职责与契约确认函-给3号位.md)

---

## 一、总纲：认责，且「非 0 落地」

先纠正 4号位 2.2 的一个误判：**Setup 换型后端并非「0 落地」**，是「表/实体已落地、端点/权限码未落地」的中间态。

| 已落地（3号位） | 未落地 |
|---|---|
| `SetupTransitionRule` 实体（`LPS.APS.Core\Entities\Aps\SetupTransitionRule.cs`） | `GovernanceController` Setup 端点 |
| DDL 补丁 v5.1.7→v5.1.8（建表 + 唯一索引 + EXACT/DEFAULT CHECK） | `aps.setup.view/edit/publish` 三码 |
| 冲突校验器 + 单测 | dev seed |

> 4号位 2.2 第 2 条「grep `SetupRule` 0 命中」系**关键词误判**：实体名为 `SetupTransitionRule`（无连续 `SetupRule` 子串）。第 3 条（`aps.setup` 0 命中）属实。

---

## 二、逐条回复 Q1–Q7

| # | 答复 | 依据 / 说明 |
|---|---|---|
| **Q1**（是否认属） | **是** | 0号位 2026-09-17 已裁决「**Setup 规则治理归 3号位**，DDL 按业务 Owner」，`SetupTransitionRule` 建表走红线 #6 豁免、由 3号位 直接落地。维护 API + 权限码 + seed 为该治理的自然延续 |
| **Q2**（排期） | 根因确认 + 排期见下 | 根因 = 3号位 实施包 **09-15 冻结**早于 Setup 专项 **09-16 进冻结索引**，范围文档需 **v1.5→v1.6 升版补入**；4号位「范围文档早切一天」假设成立。**排期：拟 09-25 合入 dev（待 3号位 负责人最终确认报备 0号位）** |
| **Q3**（若不认属） | 不适用 | 已认责，无异议 |
| **Q4**（契约路径对齐） | 分两类，**倾向方案 b** | 见下 §三 |
| **Q5**（权限码） | 归 3号位，随端点排 | 修改点：`PermissionCodes.cs` 常量 + `PermissionSeedService.cs` seed + 角色绑定（viewer/planner→view；admin.aps/admin.system→三码全），随端点同批落地 |
| **Q6**（dev seed） | **是** | ≥1 DRAFT `RuleSetVersion` + ≥1 ACTIVE Run，随端点一起提供（沿用 G4 最小列集 seed 模式） |
| **Q7**（diff 补 `setupRuleChanges`） | **是** | `diff` 响应补 `setupRuleChanges: { added, modified, removed }`，随 Q4 的 diff 端点设计一并落地 |

---

## 三、Q4 契约路径对齐：分两类

**类 A —— SetupTransitionRule 独立端点**（`setup-rules/exact`、`uncovered-stats`、`operation-resource-eligibility` 等）：
→ 全新端点，归 3号位，无路径冲突，按 §11.1 新增。

**类 B —— §2.3 的 #11/#12/#13**（`rule-set-versions` / `-diff` / `-publish`）：
→ 与现有 `rule-set/{ruleSetId}/versions`、`rule-set/version/diff`、`rule-set/version/{versionId}/publish` **路径形态重叠**。

**初步倾向方案 b**（修订契约为现有路径，前端 `setup.ts` 单文件适配，约 0.5 人天）——理由：避免双路径别名维护负担 + 契约不一致风险。
**最终定案**：待 4号位 提供 §11.1 #11/#12/#13 与现有端点的**字段级对照**后，与 0号位 会签冻结（尤其 #12 diff 需确认是否复用现有 `rule-set/version/diff` 并扩展 `setupRuleChanges` 维度）。

---

## 四、已记录的阻塞点（提请 2号位 确认）

`SetupTransitionRule.ResourceId / FromMaterialId / ToMaterialId` 的 **FK 到 Resource/Material 主键口径仍待 2号位 确认**，会连带影响 POST 校验与 seed；排期内需预留。

---

## 五、给 4号位 的结论

- **结论 A（职责确认 + 契约不变）**：Q1 认责成立；端点归 3号位，v1.5→v1.6 升版后按序落地。
- **Q4 类 B 路径**：建议走**结论 B 的契约修订**（方案 b），统一到现有 `rule-set/...` 路径，避免别名双轨。
- 书面留档已完成（本函 + 后续 v1.6 升版索引）。

---

**发送人**：3号位
**日期**：2026-09-18
**待办**：① 排期日期最终确认（D1）② Q4 类 B 逐端点字段对照 ③ FK 口径等 2号位 回执