# 3号位 → 4号位：R3 fork 端点（F1–F4）联调完成回执（含 1 处授权修补）

> **发件**：3号位（规则/参数治理、权限治理 Owner）
> **致**：4号位（前端）
> **抄**：0号位（备查）、2号位（备查）
> **日期**：2026-09-24 ｜ **版本**：v1.0
> **依据**：《3号位致4号位_Rules.vue联调回执_R1R2完成_R3待办_v1.0_20260922.md》§三（R3 ⏳ 排期 09-25 前）
> **性质**：**R3 fork 端点联调完成回执** —— F1–F4 fork 双发链路联调已绿 + 1 处授权 bug 已修，**触发 V5 验收**。

---

## 〇、一句话

R3 的 F1–F4 四端点 fork 双发链路（GET 取版本 → 复制改 VersionCode → POST 建 DRAFT）**联调已闭环**：真实 dev 库落库返回新 versionId、回读字段一致、强制 DRAFT，已由集成测试实证通过；并顺手修复 1 处参数端点授权挂码错误（`RuleMaintain` → `ParameterEdit`）。**V5 可启动验收。**

---

## 一、F1–F4 联调结果

| 端点 | 动作 | 断言 | 结果 |
|---|---|---|---|
| F3 `POST rule-set/version` | 建规则集草稿 | 返新 `ruleSetVersionId > 0` + 强制 DRAFT | ✅ |
| F1 `GET rule-set/version/{id}` | 回读 | `VersionCode`/`RuleSetId`/`DemandPriorityJson` 一致 | ✅ |
| F4 `POST parameter-set/version` | 建参数集草稿 | 返新 `parameterSetVersionId > 0` + 强制 DRAFT | ✅ |
| F2 `GET parameter-set/version/{id}` | 回读 | `VersionCode`/`ParameterSetId`/`SolverStrategyJson` 一致 | ✅ |
| — | 级联 | 双 DRAFT 可被 StrategyProfile fork 引用 | ✅ |

**实证**：集成测试 `GovernanceVersionServiceIntegrationTests.R3_F1_F4_fork双发链路_新建双DRAFT并回读_策略包可引用` 真实 dev 库跑通（失败 0 · 通过 1 · 跳过 0，573ms）。

> **验证深度说明**：本次为**服务层集成测试**（真实 `APS_Production` 库 + 真实仓储 + 真实编排，即 HTTP 端点所委托的同一实现链）。HTTP 端点本身是 `[Authorize]` + 服务方法调用的薄封装，路由 `api/governance/rule-set/version/{id}` 等未做真实 curl（需 run 起应用 + 持 token），该层风险低、如需可补一轮真实 HTTP 冒烟。

---

## 二、🔧 必要修补（本轮发现并已修）

联调复核授权挂码时发现：参数集**写**端点错误挂了规则集权限码。

| 端点 | 修复前 | 修复后 |
|---|---|---|
| F4 `POST parameter-set/version` | `RuleMaintain`（`aps.rule.edit`） | **`ParameterEdit`（`aps.parameter.edit`）** |
| `PUT parameter-set/version/{id}` | `RuleMaintain` | **`ParameterEdit`** |

**后果**（修复前）：`aps.parameter.edit` 虽在权限码清单里、却 0 处被 Endpoint 引用；只授 `aps.parameter.edit`（不授 `aps.rule.edit`）的用户在 F4 会 403。读侧（F2 `ParameterView`）本就正确，写侧（F4）是复制规则端点时漏改。**现已对齐，全量 `dotnet build` 0 错误。**

---

## 三、附：非 F1–F4 范围、建议后续确认（不改本次）

参数集版本 **publish / disable** 端点目前仍挂 rule 系权限码（`RulePublish` / `RuleMaintain`），因无 `ParameterPublish` / `ParameterDisable` 专用码。属既有设计、非 R3 范围；是否需要为参数集发布/失效补专用码，建议 0号位/3号位 另行裁定，本次不动。

---

## 四、后续分工（触发 V5）

| 动作 | 归属 |
|---|---|
| F1–F4 fork 链联调 | 3号位 ✅ 已完成 |
| F4/PUT 授权挂码修补 | 3号位 ✅ 已完成 |
| **V5 验收**（F1–F4 curl 200 + 预期字段，或前端 forkDraft 实跑） | 4号位 |
| （可选）真实 HTTP 冒烟（run 应用 + token） | 3号位/4号位 协商 |

---

## 五、红线声明

- 服务层集成测试不改库结构（红线 #6 合规）；
- 修补仅改 `GovernanceController` 2 处 `[Authorize]` 策略常量（3号位 辖区 Web 层），无签名变更；
- 未触碰 4号位 前端、1号位/2号位 辖区。

---

**发件**：3号位 ｜ **日期**：2026-09-24 ｜ **版本**：v1.0
**本件性质**：R3 fork 端点联调完成回执（F1–F4 已绿 + 1 处授权修补）。4号位 可启动 V5 验收。