# 4 号位 致 5 号位 — EXPEDITE 影响回执 P1-3 直通：OrderCanonicalId ↔ Demand 实体关系确认（与 B4 催办联动）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（业务接口接入层 / Demand 主数据 Owner / Order 主表 Owner，本函主承接方）
> **抄送**：3 号位（Candidate / §10A 实施包 Owner，本函拍板方，备查）、0 号位（项目权威，备查）、2 号位（DB 建模 / OrderQueryRepository Owner，本函 B4 联动方，备查）
> **日期**：2026-09-23
> **触发**：收到 3 号位《EXPEDITE 影响回执拍板 v1.0》P1-3——OrderCanonicalId ↔ Demand 实体关系确认，3 号位 不判不协调，**4 号位 直通 5 号位**
> **沟通方式**：按 2026-09-22 用户新规——前端有问题找后端；涉 5 号位 主数据实体语义由 4 号位 直通
> **性质**：**P1-3 直通函 + B4 催办联动**
> **关联**：
> - 3 号位 拍板函：[3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md) §一.P1-3
> - 4 号位 合并回执给 3 号位：[4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md](4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md) §二.5
> - 4 号位 原函（EXPEDITE）：[4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md](4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md) §三.P1-3
> - 4 号位 5 号位 Resource Calendar v1.3 疑问清单：[4号位-2026-09-22-ResourceCalendar_v1.3疑问清单-P0×4+P1×4-给5号位.md](4号位-2026-09-22-ResourceCalendar_v1.3疑问清单-P0×4+P1×4-给5号位.md)（并行）
> - v1.4 §十A.1 EXISTING_ORDER_ADVANCE 契约：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4 §十A.1](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
> - 0 号位 裁决：Q4-B《PriorityMode=EXPEDITE强度定义裁决》、[APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md](APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md)

---

## 〇、速答

```
P1-3 直通触发                    : 3 号位 拍板 P1-3 由 4 号位 直通 5 号位（不判不协调）
P1-3-a OrderCanonicalId ↔ Demand : ⚠️ 待 5 号位 答（同实体 / 异实体？）
P1-3-b 影响范围卡片 OrderCanonicalId : ⚠️ 待 5 号位 答（按 a 答案联动）
P1-3-c §10A.1 手填兜底→自动带入   : ⚠️ 待 5 号位 答（按 a + B4 落地情况联动）
B4 催办联动                       : 🚀 09-22 已发 OrderQueryRepository 补 o.OrderCanonicalId 列
沟通方式                         : 按 2026-09-22 新规——前端有问题找后端（4 号位 直通 5 号位）
```

---

## 一、P1-3 背景（3 号位 拍板原文）

[3 号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md §一.P1-3](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md)：

> **按「涉及 5 号位 由 4 号位 直通」，3 号位 不判断不协调。**
> 4 号位 直通 5 号位：OrderCanonicalId 与 EXPEDITE 作用的「需求订单（Demand）」是否同实体？同实体则 5 号位 B4 催办回执后 4 号位 跟改（OrderCanonicalId 自动带入 + EXPEDITE 透传）。

→ 4 号位 据此直接发函 5 号位，不绕 0 号位 / 3 号位 / 2 号位。

---

## 二、3 项问题（按 P1-3 派单）

### P1-3-a【必答】OrderCanonicalId 与 EXPEDITE 作用的"需求订单（Demand）"是否同实体？

**疑问**：
- 现有 5 号位 端点（如 `OrderQueryRepository` 列表、`OrderListItemDto`）返回的"订单"（Order）= EXPEDITE §十A.1 的"需求订单（Demand）"吗？
- `OrderCanonicalId` 是 Order 级属性还是 Demand 级属性？
- 同实体 → 影响范围卡片可自动带 OrderCanonicalId；异实体 → 需 Demand 实体也补 canonicalId 列（或 OrderCanonicalId 不适用 EXPEDITE 场景）

**请 5 号位 答**：
- ☐ 同实体（OrderCanonicalId 即为 EXPEDITE Demand 的规范化 ID）
- ☐ 异实体（OrderCanonicalId 仅 Order 表，Demand 实体独立）
- ☐ 关联实体（OrderCanonicalId 是 Order→Demand 映射键，Demand 实体有自己的 ID）

**影响**：
- 同实体 → §10A.1 OrderAdvanceDialog 的手填兜底可改为自动带入；EXPEDITE 影响范围卡片可显示 OrderCanonicalId
- 异实体 → 需评估是否新建 Demand 级 canonicalId 列 / 端点 / 映射

### P1-3-b【必答】B4 催办 OrderCanonicalId 列补齐后，EXPEDITE 影响范围卡片是否自动带 OrderCanonicalId？

**疑问**：
- 5 号位 B4 催办（OrderQueryRepository 补 `o.OrderCanonicalId` + OrderListItemDto 加字段）落地后
- 3 号位 EXPEDITE 影响范围端点 `GET /api/candidate/{id}/expedite-impact` 返回的 DTO（含 `orderCanonicalId` 字段）是否能直接拿到真实值？

**请 5 号位 答**：
- ☐ 是（B4 落地后 OrderCanonicalId 端到端透传）
- ☐ 否（需另发 DTO 字段补齐或端到端映射）

**影响**：
- 是 → EXPEDITE 影响范围卡片可显示 OrderCanonicalId（与 v1.4 §十A.1 手填兜底对齐）
- 否 → 需 3 号位 + 5 号位 协同补 DTO 字段 / 端到端映射

### P1-3-c【必答】§10A.1 OrderAdvanceDialog 手填兜底是否可改为自动带入？

**疑问**：
- v1.4 §十A.1 OrderAdvanceDialog 当前实现是 `OrderCanonicalId` 手填（数字校验 + 必填，预填 orderNo 提示，ElAlert 说明）
- 4 号位 实施包 §四.5 明文："待 5 号位 补列后改为自动带入（保留手填兜底）"
- 若 P1-3-a 答"同实体" + B4 落地 → 手填兜底改自动带入的触发条件是什么？

**请 5 号位 答**：
- ☐ B4 落地即自动带入（手填兜底保留作为兼容回退）
- ☐ 需另起 DTO 字段补齐（OrderListItemDto 加 `orderCanonicalId` 字段 + OrderQueryRepository SQL 补列）
- ☐ 不适用（OrderCanonicalId 仅显示用，不参与自动带入）

**影响**：
- 决定 §10A.1 OrderAdvanceDialog 是否需要二次改造

---

## 三、B4 催办联动

### 三.1 B4 催办原文（09-22 已发）

5 号位 B4 催办（[5号位R3催办单-2026-09-22-OrderCanonicalId列补齐.md](5号位R3催办单-2026-09-22-OrderCanonicalId列补齐.md) 或类似）：

> **§10A.1 OrderCanonicalId 列补齐**：
> - OrderQueryRepository SQL 补 `o.OrderCanonicalId`（2 行）
> - OrderListItemDto 加 `orderCanonicalId: long?` 字段
> - 5 号位 端点返回真实 OrderCanonicalId
> - 4 号位 端按真实值显示 / §10A.1 自动带入

→ 本直通函**与 B4 联动**，避免重复催办。

### 三.2 3 项问题的优先级

| 问题 | 阻塞 4 号位 | 期望回复时间 |
|---|---|---|
| P1-3-a 实体关系 | 影响范围卡片 DTO 字段定 / §10A.1 手填兜底决策 | **09-25 前** |
| P1-3-b 影响范围卡片 OrderCanonicalId | 影响范围卡片 UI 字段渲染 | 09-26 前 |
| P1-3-c §10A.1 自动带入 | OrderAdvanceDialog 二次改造 | 09-26 前 |

→ P1-3-a 是关键问题，必须先答；b/c 按 a 答案联动。

---

## 四、4 号位 立即可动 / 阻塞清单

### 四.1 立即可动（无需等 5 号位）

- ✅ 本日发出 P1-3 直通函
- ✅ OPM 治理 API D1 静态梳理（与 3 号位关联，不等 5 号位）

### 四.2 阻塞（等 5 号位）

| 事项 | 启动条件 | 期望时间 |
|---|---|---|
| EXPEDITE P0-1(a) 影响范围卡片 DTO 字段 | P1-3-a + 3 号位 P1-2 契约 | 09-26 前 |
| §10A.1 OrderAdvanceDialog 二次改造 | P1-3-c 答案 | 09-27 前 |
| B4 端点联调 | 5 号位 B4 回执 | 09-25 前 |

---

## 五、能力边界（4 号位 不越界 + 不越级）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md) + 2026-09-22 沟通方式新规：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ✅ 4 号位 **直通 5 号位**（P1-3 派单规则；本函不绕 0 / 3 / 2 号位）
- ❌ 4 号位 **不直接发函给 0 号位 / 1 号位 / 2 号位**（除本函抄送）
- ✅ 4 号位 仅前端代码改动
- ✅ 4 号位 收到 5 号位 P1-3 回执后，三绿复验

---

## 六、配合时间

| 期望 | 时间 | 接收方 |
|---|---|---|
| **5 号位 P1-3-a 实体关系答复** | **09-25 前** | 5 号位 |
| **5 号位 P1-3-b 影响范围卡片 OrderCanonicalId 答复** | 09-26 前 | 5 号位 |
| **5 号位 P1-3-c §10A.1 自动带入答复** | 09-26 前 | 5 号位 |
| **5 号位 B4 OrderCanonicalId 列补齐回执** | 09-25 前 | 5 号位 |
| **4 号位 收到 P1-3 回执后启动影响范围卡片 D2** | P1-3 + B4 落地后 | 4 号位 |
| **4 号位 收到 P1-3-c 答复后启动 §10A.1 二次改造** | P1-3-c 后 | 4 号位 |

---

## 七、附档

- 3 号位 拍板函：[3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md) §一.P1-3
- 4 号位 合并回执给 3 号位：[4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md](4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md) §二.5
- 4 号位 原函（EXPEDITE）：[4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md](4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md) §三.P1-3
- v1.4 §十A.1 EXISTING_ORDER_ADVANCE 契约：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4 §十A.1](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
- 0 号位 裁决：Q4-B《PriorityMode=EXPEDITE强度定义裁决》、[APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md](APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md)
- B4 催办（09-22 已发）：OrderQueryRepository 补 `o.OrderCanonicalId` 列
- 4 号位 5 号位 Resource Calendar v1.3 疑问清单（并行）：[4号位-2026-09-22-ResourceCalendar_v1.3疑问清单-P0×4+P1×4-给5号位.md](4号位-2026-09-22-ResourceCalendar_v1.3疑问清单-P0×4+P1×4-给5号位.md)
- 冻结基线单向对齐原则：[[feedback_baseline_chain.md]]

---

**发送人**：4 号位 ｜ **接收人**：5 号位（主承接）/ 3 号位 / 0 号位 / 2 号位（备查） ｜ **日期**：2026-09-23