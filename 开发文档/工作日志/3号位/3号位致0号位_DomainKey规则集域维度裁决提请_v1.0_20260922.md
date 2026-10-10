# 3号位 致 0号位 — 裁决提请（规则集列表 DTO 新增 DomainKey 字段的真源缺口）

> **发送人**：3号位（规则参数治理 / 认证 / RBAC / Domain 治理 Owner）
> **接收人**：0号位（PM / 最终裁决）
> **抄送**：4号位（前端，经 3号位 中转）、2号位（数据引擎 / DDL）、5号位（查询侧，备查）
> **日期**：2026-09-22
> **触发**：《3号位催办单-2026-09-22-Rules.vue联调阻塞-3项.md》R2（列表 DTO 扩展，P1）——4号位 要求 `GET /api/governance/rule-sets` 列表 DTO 新增 **DomainKey** 字段，用于前端卡片 **Domain tag** 展示
> **性质**：**裁决提请函**——规则集是否引入"域"维度；裁决后 3号位 落地 DTO / 知会 4号位
> **前置**：R1（造数）已落地、R2 其余 6 个有真源字段已落地（见 §四 现状）

---

## 〇、速览

```
提请 1（DomainKey 真源）: 规则集是否需要"所属域"维度？
                          a) 不需要 → 前端移除 Domain tag
                          b) 需要   → 引入域关联机制（见提请 2 选项）
提请 2（若需要）关联机制   : a) RuleSetVersion 加列 / b) 新建关联表 / c) ContentSnapshotJson 内嵌
提请 3（过渡口径）裁决前    : 前端隐藏 tag / 显示"全局"占位（4号位 待命）
```

---

## 一、问题陈述（5 分钟读完）

4号位 `/rules` 页面（页面10 规则、参数与策略维护）要求规则集列表卡片展示**所属域标签（Domain tag）**，据此请求 `GET /api/governance/rule-sets` 列表 DTO 新增 **DomainKey** 字段（催办单 R2，P1）。

3号位 在 R2 实施前逐项核查线上库与代码，发现 **DomainKey 在规则集数据模型上无任何真源**——该字段无法从既有数据中取出，需 0号位 裁决规则集是否引入"域"维度及关联机制后方可落地。

## 二、核查证据（R2 实施前逐项验证）

### 2.1 线上库 `APS_Production`（10.116.2.75，v5.1.8.2 schema）

**`RuleSetVersion` 全列**（`INFORMATION_SCHEMA.COLUMNS` 实测）：

```
Id, RuleSetId, VersionCode, Status, EffectiveFrom, EffectiveTo,
PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
CreatedAt, CreatedBy, UpdatedAt, UpdatedBy,
IsDefault, Remarks, ContentSnapshotJson
```

→ **无 `DemandDomainKey` 列**（催办单假设的来源列在线上不存在）。

**`RuleSet` 主表全列**：

```
Id, RuleSetCode, RuleSetName, Description, IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
```

→ 无任何域相关列。

**全库扫描 `DomainKey` / `DemandDomainKey` / `DomainId` 列**，仅命中 3 张表：

```
DomainDefinition.DomainKey   ← 域定义字典（IsActive=1 有效域集合）
Order.DomainKey              ← 订单
PlanVersion.DomainKey        ← 计划版本
```

→ **规则集三张治理表（RuleSet / RuleSetVersion + 参数集族）全部无域字段**。

**扫描规则集↔域关联表**（`LIKE '%Rule%Domain%'` / `'%Domain%Rule%'`）：

```
（空）→ 无任何中间/关联表
```

### 2.2 代码层核查

| 位置 | 结论 |
|---|---|
| `RuleSet` / `RuleSetVersion` 实体（Core/Entities/Aps）| 无 `DomainKey` 属性 |
| `DemandPriorityBlock`（规则集内容快照的排序块）| 仅 `Segments`，无 DomainKey |
| 全代码搜 `RuleSet.*Domain` / `Domain.*RuleSet`（过滤 Scheduling/PlanVersion/DomainDefinition 无关项）| 零命中，无任何规则集↔域关联机制 |
| 冻结文档 v1.4 §十八（页面10 契约源头）| DomainKey 全部出现在 **PlanVersion / Order** 上下文（页面11 运行/版本页），**页面10 不提域** |

### 2.3 结论

> **规则集在数据模型上不存在"域"这一维度。** DomainKey 是 **PlanVersion（计划版本）/ Order（订单）级**概念——标识"某次排程 / 某订单属于哪个业务域"；而规则集是**全局治理对象**（一套规则配置可被任意域引用），既有模型中二者无关联。4号位 卡片 Domain tag 需求背后是**模型层缺失**，非字段缺失。

## 三、为什么不能自行补字段（3号位 不越权的理由）

**方案A 已裁决的真相源原则**（P0-01/P0-02）：规则集内容以 `ContentSnapshotJson` 单列为唯一真相源，不能随意造字段。若给 DTO 硬加 DomainKey：

| 做法 | 后果 |
|---|---|
| 恒返回 null | 前端 Domain tag 永远空白，**契约失真**（前端以为有值）|
| 从 `DemandDomainKey` 取 | **列不存在**，SQL 直接报错 |
| 从 `DomainDefinition` 猜 | **猜测无依据**，违反"物料映射类禁止盲目 First()"红线 #4 精神，业务上错误（规则集本就不属于某域）|

→ 是否给规则集引入"域维度"是**架构/业务决策**，按号位职责属 0号位 裁决范围，3号位 不自作主张。

## 四、提请事项

### 提请 1：规则集是否需要"所属域"维度？

- **a. 不需要**——规则集全局共享，**前端移除卡片 Domain tag**（页面10 契约源本不提域，移除最贴近冻结文档）；
- **b. 需要**——规则集按域隔离/打标，引入关联机制（见提请 2）。

### 提请 2（若提请 1 裁 b）域关联机制选项

| 选项 | 机制 | 实施方 | 影响 |
|---|---|---|---|
| a | `RuleSetVersion` 加 `DomainKey` 列（版本级域，随版本发布固化）| 2号位 DDL + 3号位 治理 | 版本级隔离，需 DDL 变更申请 |
| b | 新建域↔规则集关联表（多对多）| 2号位 DDL + 3号位 治理 | 灵活，可多域共享；需 DDL |
| c | `ContentSnapshotJson` 内嵌域字段 | 3号位 治理（无 DDL）| 零 DDL，但域语义混入内容快照，与"快照=可重放内容"定位冲突 |

### 提请 3：裁决前的过渡口径（4号位 前端待命）

- **a. 前端先隐藏 Domain tag**（推荐，无失真、不阻塞页面）；
- **b. 前端显示"全局"占位**（语义上规则集全局共享）。

## 五、现状与影响（不影响 R1/R2 主链路）

| 项 | 状态 |
|---|---|
| R1 造数（1287 规则集 + 881 参数集 PUBLISHED 版本）| ✅ 已落地，详情端点不再 404（V1 验收）|
| R2 列表 DTO 其余 6 字段（CurrentVersionCode/DraftVersionCode/Status/LastChangeReason/PublishedAt/RetiredAt）| ✅ 已落地（4 文件改动，编译通过、SQL 实测验证），**DomainKey 未纳入** |
| 前端核心验收 V1-V4（详情加载 / 新建草稿 / 版本号 / 6 态 KPI）| **不受 DomainKey 影响**，均可跑通 |
| 仅受影响点 | 卡片 Domain tag 一处展示；前端当前为 mapping 兜底（已失真），裁决前维持现状不新增恶化 |

> 说明：R2 版本摘要真源同时修正一处契约失真——催办单示例 `CurrentVersion`（int）实为 `VersionCode`（字符串），已按真实 schema 以 `CurrentVersionCode` 字符串落地，随本函知会 4号位。

---

## 请求

以上 3 项可逐项独立裁决；**请 0号位 书面答复**（任一形式均可），3号位 收讫后当日转达 4号位 / 2号位（若涉 DDL）。4号位 侧对 Domain tag 维持「前端待命」，R1/R2 主链路验收不受阻塞。

**发送人**：3号位 ｜ **日期**：2026-09-22
**待办**：① 0号位 裁决 3 项 ② 收讫后转达 4号位（+2号位 若涉 DDL）
