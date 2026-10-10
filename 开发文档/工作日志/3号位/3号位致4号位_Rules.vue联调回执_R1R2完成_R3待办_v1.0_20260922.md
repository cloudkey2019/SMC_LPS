# 3号位 致 4号位 — Rules.vue 联调回执（R1/R2 完成，R3 待办）

> **发送人**：3号位（规则参数治理 / 认证 / RBAC / Domain 治理）
> **接收人**：4号位（前端）
> **抄送**：0号位（备查）、2号位（DDL / 数据引擎，备查）
> **日期**：2026-09-22
> **触发**：《3号位催办单-2026-09-22-Rules.vue联调阻塞-3项.md》（🔴P0，R1/R2/R3）
> **性质**：**催办回执** —— R1 ✅ / R2 ✅（含 1 处契约澄清）/ R3 ⏳；附 V1-V5 验收指引
> **前置**：0号位 2026-09-22《1号位、3号位问题统一回复意见》已出（SetupSource 枚举统一 + ScopeJson SchemaVersion 演进，见 §四 预告）

---

## 〇、速览

```
R1 造数       ✅ 已落库（RuleSet#1287 + ParameterSet#881，V1 PUBLISHED）→ V1-V3 可立即验收
R2 列表 DTO   ✅ 已交付 6 个真源字段 → V4 版本号/状态/6态KPI 真实
              ⚠️ DomainKey 无真源，已提报 0号位 裁决（前端 Domain tag 维持现状兜底）
              ⚠️ CurrentVersion(int) 契约修正 → VersionCode(string)，见 §二-3 需确认
R3 fork 端点  ⏳ 未实测（F1-F4，P1，预计 09-25 前）→ V5 待 R3
验收指引      V1-V3 现在即可跑；V4 部分可跑；V5 待 R3
```

---

## 一、R1（P0 造数）✅ 已落库

**落库内容**（dev 库 `APS_Production`，2026-09-22）：

| 主表 | 版本 | 状态 | 快照来源 |
|---|---|---|---|
| RuleSet#1287（TEST-RS-…，列表第一张卡片）| `V1` | PUBLISHED | 复用 RS-DEMO-V2（RuleSetVersion Id=478）ContentSnapshotJson 原样 |
| ParameterSet#881（TEST-PS-…，同名后缀）| `V1` | PUBLISHED | 复用 PS-DEMO-V3（ParameterSetVersion Id=807）ContentSnapshotJson 原样 |

- `EffectiveFrom/EffectiveTo = NULL`（无窗口约束，恒在当前生效窗口内——修正了首版 GETDATE 时区导致的"未来生效"问题）；
- 幂等：按 `(RuleSetId, VersionCode)` / `(ParameterSetId, VersionCode)` 守卫，可重复执行；
- 复现脚本：`LPS.APS.Web/Sql/APS_RulesVue_dev_seed_20260922.sql`（UTF-8，`sqlcmd -f 65001` 执行）；
- **合规说明**：仅数据 INSERT，**无 DDL**（红线 #6 合规）；快照按方案A（ContentSnapshotJson 单列真相源）复用已裁决数据。

> ⚠️ **验收范围提示**：484 条规则集仅 17 条有版本，本次只造了 **1287/881 这一条完整链**。**V1 验收请用卡片 1287**（列表第一张）；其余卡片详情仍 404，属数据覆盖问题，非端点问题（后续如需可扩造）。

## 二、R2（P1 列表 DTO 扩展）✅ 已交付

`GET /api/governance/rule-sets` 现返回 `RuleSetListItemDto`，主表字段 + **6 个真源版本摘要字段**：

| 字段 | 真源 | 说明 |
|---|---|---|
| `currentVersionCode` | RuleSetVersion.VersionCode（最新 PUBLISHED）| 无则 null |
| `draftVersionCode` | RuleSetVersion.VersionCode（最新 DRAFT）| 无则 null |
| `status` | 最新版本 Status | 六态：DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED；无版本则 null |
| `lastChangeReason` | RuleSetVersion.Remarks | — |
| `publishedAt` | RuleSetVersion.PublishedAt | 最新 PUBLISHED 发布时间 |
| `retiredAt` | RuleSetVersion.EffectiveTo | 最新版本生效窗口关闭时间；无则 null |

→ **V4 可删**：版本号 / 状态 / 6 态 KPI 的 mapping 兜底（`isActive→PUBLISHED`）可删，数据真实。

### 2.1 DomainKey ⚠️ 未纳入 —— 无真源，已提报 0号位

催办单示例 `DomainKey`（来源：`RuleSetVersion.DemandDomainKey` 或 Domain 关联）**两处均不存在**：

- 线上 `RuleSetVersion` 无 `DemandDomainKey` 列（DDL v5.1.8.2 实测）；
- 全库 `DomainKey` 仅存在于 DomainDefinition / Order / PlanVersion，**规则集无任何域关联**；
- 规则集是全局治理对象，DomainKey 是 PlanVersion/Order 级概念 → **模型层缺"域"维度，非字段缺失**。

已发《3号位致0号位_DomainKey规则集域维度裁决提请_v1.0_20260922.md》提报 0号位（3 项：是否需要域维度 / 关联机制 / 过渡口径）。**裁决前前端卡片 Domain tag 维持现有 mapping 兜底（不新增恶化）**；裁决后 3号位 当日转达。

### 2.2 契约失真修正 ⚠️ CurrentVersion(int) → VersionCode(string)

催办单示例 `CurrentVersion`（int，`MAX(Version) WHERE Status=PUBLISHED`）——**线上无数值 `Version` 列**，真源为 `VersionCode`（字符串，如 `V1` / `RS-DEMO-V2`），故交付为字符串 `currentVersionCode`。

> **请 4号位 确认**：前端接受**字符串版本号**即可（推荐，贴近真实数据），或确需数值版本号请回执，3号位 另行提供映射口径。

## 三、R3（P1 fork 端点）⏳ 待办

F1-F4 四端点（`rule-set/version/{id}` GET、`parameter-set/version/{id}` GET、`rule-set/version` POST、`parameter-set/version` POST）**尚未实测落库返新 versionId**。3号位 排期 **09-25 前**完成 F1-F4 联调测试 + 必要修补，完成后单独回执（触发 V5 验收）。

## 四、重要预告（0号位 2026-09-22 裁决，涉及前端未来变更）

1. **SetupSource 枚举统一**：甘特换型标签将由现 5 值（EXACT/DEFAULT/SAME_PRODUCT/NONE/INITIAL）统一为 **4 值**：

   | 值 | 含义 |
   |---|---|
   | INITIAL_SETUP_STATE | 无上一产品，初始设备状态 |
   | EXACT | 命中明确产品转换规则 |
   | DEFAULT | 命中默认换型规则 |
   | SETUP_RULE_MISSING_ZERO_FALLBACK | 无规则，按 0 分钟兜底 |

   删除 NONE / SAME_PRODUCT（SAME_PRODUCT 是业务条件不是规则来源）。**前端勿固化当前 5 值标签**，3号位 落地后知会。

2. **ScopeJson SchemaVersion 演进**：Candidate 载荷采用 SchemaVersion 版本化（v2 承载 BusinessTriggerType/PriorityMode/OrderTargets/TaskTargets/ChangedResourceIds），不因早期"固定 11 字段"说明限制扩展。前端相关类型可能新增 `schemaVersion` 字段，3号位 落地后随契约同步知会。

> 以上两项**不阻塞** Rules.vue 当前验收（页面10 规则/参数/策略维护），仅预告，避免前端提前固化。

## 五、验收指引（V1-V5）

| # | 验证 | 状态 | 指引 |
|---|---|---|---|
| V1 | 刷新 `/rules` 任一卡片详情可加载（不再 404）| ✅ 可立即跑 | **用卡片 1287**（第一张）；其余卡片因未造数仍 404，非端点问题 |
| V2 | [+ 新建草稿] 按钮出现 | ✅ 可立即跑 | V1 通过后 |
| V3 | 新建草稿 → 跳参数 Tab，DRAFT v2 已建 | ✅ 可立即跑 | 依赖 F3/F4 正常（若遇问题即为 R3 待办项，请记录报回）|
| V4 | R2 落地后删 mapping：版本号 / 状态 / 6 态 KPI 真实 | ✅ 版本号/状态/KPI 可删；⚠️ **Domain tag 保留兜底** | 待 0号位 DomainKey 裁决 |
| V5 | R3 4 端点 F1-F4 curl 200 + 预期字段 | ⏳ 待 R3（09-25 前）| 完成后再验收 |

## 六、附档

- 催办单：《3号位催办单-2026-09-22-Rules.vue联调阻塞-3项.md》
- 裁决函：《3号位致0号位_DomainKey规则集域维度裁决提请_v1.0_20260922.md》
- 造数脚本：`LPS.APS.Web/Sql/APS_RulesVue_dev_seed_20260922.sql`
- 0号位 裁决：《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》（SetupSource/ScopeJson 预告出处）

---

**发送人**：3号位 ｜ **日期**：2026-09-22
**待办**：① 4号位 跑 V1-V4（V4 版本号/状态/KPI，Domain tag 除外）② 4号位 确认 CurrentVersion(string) 口径 ③ 3号位 R3 F1-F4（09-25 前）④ 0号位 DomainKey 裁决后转达
