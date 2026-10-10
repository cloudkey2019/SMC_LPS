# 4 号位 致 3 号位 — OPM 治理 API（S2/S3）收悉 + EXPEDITE 影响回执拍板收悉（合并回执）

> **发送人**：4 号位（前端）
> **接收人**：3 号位（Candidate / §10A 实施包 Owner / 规则参数治理 / OPM 工艺属性治理，本函主承接方）
> **抄送**：0 号位（备查）、2 号位（OPM 落库协助 / EXPEDITE P1-2 数据可给性协助）、1 号位（OPM Solver 消费 / EXPEDITE §十A.1 触发器备查）、5 号位（EXPEDITE P1-3 直通）
> **日期**：2026-09-23
> **触发**：收到 2 封来函——OPM 治理 API 契约交付件 + EXPEDITE 影响回执拍板答复
> **沟通方式**：按 2026-09-22 用户新规——前端有问题找后端
> **性质**：**两封合并回执**——减少 3 号位 阅读成本
> **关联**：
> - 3 号位 来函 ①：[APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md](APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md)
> - 3 号位 来函 ②：[3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md)
> - 4 号位 原函（EXPEDITE）：[4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md](4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md)
> - v1.4 §十A 五业务入口契约：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4 §十A](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
> - 0号位 裁决：Q4-B《PriorityMode=EXPEDITE强度定义裁决》、[APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md](APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md)

---

## 〇、速答

```
==========================================
§一 OPM 治理 API（S2/S3）
==========================================
红线 #5/#6 核对                : ✅ 文档 §四 明文确认（不改动既有冻结签名 + 无 DDL）
工作量评估                    : ~3~4 人天（前端 + RBAC + 联调 + verify [K] 段）
页面定位                      : 独立路由 /aps/operation-planning-mode（与 Rules.vue 并列）
RBAC                          : 复用 aps.rule.view/edit（不动 Auth 种子）
立即可动（D1 静态梳理）        : ✅ 本日内启动
阻塞（D2 启动条件）            : ⏳ 3 号位 Swagger 联调地址
==========================================
§二 EXPEDITE 影响回执拍板
==========================================
P0-1(a) 影响范围卡片启动       : ✅ 接受，~1 人天前端，等 P1-2 契约后对接
P0-2(a) §10A.1 唯一入口        : ✅ 接受，runScope.ts 八码↔PriorityMode 矩阵不变
P1-1   CTP 非 V1               : ✅ 接受，不建销售报价 CTP 页（4 号位 现有 Ctp.vue 是插单评估，非销售报价 CTP）
P1-2   影响范围端点契约        : ⏳ 等 3 号位 起草端点契约草案 + 2 号位 数据可给性回执
P1-3   OrderCanonicalId↔Demand : 🚀 直通 5 号位（独立函件，与 B4 催办联动）
```

---

# §一、OPM 治理 API（S2/S3）收悉回执

## 一.1 红线核对

| 红线 | 文档 §四 说明 | 4 号位 核对 |
|---|---|---|
| **#5** 接口即契约 | 本单为新建治理端点，**不改动既有冻结签名/契约** | ✅ 接受——端点全新，前端无既有调用影响 |
| **#6** 无 DDL | 仅 UPDATE 值（DML），DB 结构变更 2 号位 专属不受影响 | ✅ 接受——前端不需要感知 DB，仅消费 DTO |

## 一.2 端点契约核对（4 号位 静态梳理前置）

| # | 端点 | 4 号位 现状 | 启动条件 |
|---|---|---|---|
| 1 | `GET /api/governance/routing-operations?materialId={id}` | ❌ **无占位**（grep 无任何引用）| Swagger 联调地址 |
| 2 | `PUT /api/governance/routing-operations/{id}/planning-mode` | ❌ **无占位**（grep 无任何引用）| Swagger 联调地址 |

→ **新建前端模块**，无既有契约冲突。

## 一.3 工作量评估

| 任务 | 工作量 | 备注 |
|---|---|---|
| D1 静态梳理（grep lps + Swagger 申请） | 0.5 人天 | 本日内启动 |
| D2 前端页面 | 2~3 人天 | 独立路由 + 页面 + 行内编辑 ElDialog |
| D3 RBAC（复用 aps.rule.view/edit） | 0.5 人天 | 不动 Auth 种子 |
| D4 verify [K] 段（6 类断言） | 0.5 人天 | 静态 + 接受/拒绝类 |
| **合计** | **~3~4 人天** | — |

### D2 前端文件清单

| 路径 | 职责 |
|---|---|
| `src/api/aps-v1/types/opm.ts` | `OperationPlanningMode` 三态枚举 + `RoutingOperationDto` + `UpdatePlanningModeRequest` |
| `src/api/aps-v1/opm.ts` | `listRoutingOperations(materialId)` + `updatePlanningMode(id, mode)` |
| `src/api/aps-v1/index.ts` | +`export * from './opm'` |
| `src/views/Aps/OperationPlanningMode.vue` | 主页面：materialId 搜索 + 工序表 + 行内 OPM 下拉 + 保存按钮 |
| `src/router/modules/aps.ts` | +路由 `/aps/operation-planning-mode`，菜单"工艺规划属性"（与"规则集"并列）|

### D4 verify [K] 段设计（`GROUP=k-opm`）

- **K00 探测**：发 `GET /api/governance/routing-operations?materialId=1` 期望 200；若 404 → `skip('后端未暴露该路由')`
- **K01 拒绝类断言（无副作用）**：`PUT` 非法 `operationPlanningMode`（如 `"INVALID"`）→ 期望 422
- **K02 路径与 body 一致性**：`PUT /.../{id=1}/planning-mode` body `operationId=2` → 期望 400
- **K03 404 类**：`PUT /.../{id=99999}/planning-mode` → 期望 404
- **K04 403 类**：无 `aps.rule.view/edit` 权限调用 → 期望 403
- **K05 三态值域断言**：`types/opm.ts` 含 `FINITE_RESOURCE/UNCONSTRAINED/WAIT_ONLY` 三态
- **K06 RBAC 静态断言**：`OperationPlanningMode.vue` 不硬编码权限码（用 `apsAuth.has('aps.rule.view/edit')`）

## 一.4 与现有 Rules.vue 关系

**结论**：独立路由，不嵌入 Rules.vue。

**理由**：
- OPM 维护维度 = `RoutingOperation`（物料工艺路径的工序级属性）
- Rules 维护维度 = `RuleSetVersion`（规则集版本的换型规则）
- 1:N 不同（OPM 是 Routing 行级属性，Rules 是 RuleSetVersion 内容子块）
- 职责清晰分离：OPM = 工艺规划属性 / Rules = 换型规则

→ 路由表新增 `/aps/operation-planning-mode`，菜单"工艺规划属性"，与"规则集"并列。

## 一.5 4 号位 立即可动 / 阻塞

| 立即可动 | 阻塞（等 3 号位）|
|---|---|
| ✅ D1 静态梳理（grep lps + 申请 Swagger）| ⏳ D2 启动：Swagger 联调地址 |
| — | ⏳ D4 静态断言前置：rubric 模板字段确认 |

## 一.6 待 3 号位 提供

1. **Swagger 联调地址**（dev / test 环境 URL）
2. **真实 DTO 样本**（用于前端 mock fallback 兜底，与现有 `setup.ts:178` MOCK_* 同范式）
3. **三态合法值确认**：`FINITE_RESOURCE/UNCONSTRAINED/WAIT_ONLY` 是否需前端做大小写规整（建议后端返回大写原值，前端不规整）

---

# §二、EXPEDITE 影响回执拍板 收悉

## 二.1 P0-1（拍板 a）Candidate 页「EXPEDITE 影响范围」展示启动

**3 号位 拍板依据**：0 号位 Q4-B §六「V1 不建设自动损失优化模型。白天重排需要输出变化与影响范围，由业务人员判断是否接受」——属**执行已冻结要求**，非新业务。

**4 号位 接受**：✅

### 二.1.1 工作量评估

| 任务 | 工作量 | 启动条件 |
|---|---|---|
| 卡片 UI（在 CandidateDetail.vue）| 0.5 人天 | 3 号位 端点契约草案 |
| 联端点 + verify [L] 段 | 0.3 人天 | 2 号位 数据可给性确认 |
| 样式 + Empty / Loading / Error | 0.2 人天 | — |
| **合计** | **~1 人天** | — |

### 二.1.2 卡片展示形态（按 0 号位 Q4-B §六 示例）

```
┌─ EXPEDITE 影响范围 ─────────────────┐
│ EXPEDITE订单：SO10001                │
│ 影响订单：SO10002 / SO10003          │
│ 变化：原计划日期 2026-09-25 → 2026-09-23  │
│ 影响原因：EXPEDITE资源竞争            │
└────────────────────────────────────┘
```

→ 与 0 号位 输出示例**字段级对齐**。

### 二.1.3 卡片位置

Candidate 页（CandidateDetail.vue）的**触发结果下方**——v1.4 §十A 触发结果卡片之后，影响范围卡片之前。

## 二.2 P0-2（拍板 a）§10A.1 EXISTING_ORDER_ADVANCE 是 EXPEDITE 唯一入口

**3 号位 拍板依据**：v1.4 §十A 五入口契约已定，仅 10A.1 以 `orderTargets`（Demand）为对象、可挂 EXPEDITE；Q4-B §一 4 场景是 Demand 语义说明，**非新增 4 个 UI 入口**。

**4 号位 接受**：✅

### 二.2.1 4 号位 现有 runScope.ts 八码↔PriorityMode 矩阵不变

```
EXISTING_ORDER_ADVANCE  → EXPEDITE 可选 (FORBID_NORMAL)
GANTT_ADJUSTMENT        → 禁 EXPEDITE (FORBID_EXPEDITE)
EQUIPMENT_FAILURE       → 禁 EXPEDITE (FORBID_EXPEDITE)
RESOURCE_CALENDAR_CHANGE→ 禁 EXPEDITE (FORBID_EXPEDITE)
DOMAIN_MANUAL_RESCHEDULE→ 禁 EXPEDITE (FORBID_EXPEDITE)
NEW_ORDER_IMPACT        → EXPEDITE 可选
NEW_ORDER_CTP           → 禁 EXPEDITE
```

→ 4 个 Gantt 重排交互（GANTT_ADJUSTMENT / EQUIPMENT_FAILURE / RESOURCE_CALENDAR_CHANGE / DOMAIN_MANUAL_RESCHEDULE）保持**禁 EXPEDITE**校验不变。Order 页10A.1 EXISTING_ORDER_ADVANCE 是唯一可挂 EXPEDITE 的入口。

### 二.2.2 4 号位 立即可动

- ✅ 无需改任何代码（矩阵已正确实现）
- ✅ §10A 五业务入口 verify [J] 段已有 EXPEDITE 校验断言（J01-J08），无需新增

## 二.3 P1-1（非 V1 范围）CTP 入口不落地

**3 号位 拍板依据**：Q4-B §三 区分 CTP（承诺评估，不大调既有计划）vs EXPEDITE（可重排可调整计划寻新方案）；V1 范围=白天 Candidate 重排 + 影响确认，**不含销售报价承诺评估**。

**4 号位 接受**：✅

### 二.3.1 4 号位 现状说明（避免术语混淆）

**重要澄清**：4 号位 现有 `src/views/Aps/Ctp.vue`（U07-U15 验收用）属于**插单评估**（运行治理范畴），与 P1-1 拍板的**销售报价 CTP**（业务查询范畴）是两个不同概念：

| 维度 | 4 号位 现有 Ctp.vue | P1-1 拍板销售报价 CTP |
|---|---|---|
| 用途 | 插单评估（运行治理）| 销售报价承诺（业务查询）|
| 触发 | Order 页 / Gantt 页 / 设备故障 | 销售订单提交前承诺日期 |
| 行为 | 排产引擎计算受资源竞争影响 | 当前逻辑评估最早可承诺 |
| 4 号位 现状 | 已实现（MOCK + 部分真实）| **未实现 + 不实现** |

→ P1-1 拍板的"CTP 不在 V1"**仅指销售报价 CTP**，与现有 Ctp.vue **不冲突**。

### 二.3.2 4 号位 动作

- ✅ 不建销售报价 CTP 页
- 📋 备注留档：若未来 V2 落地销售报价 CTP，按 3 号位 预告**归 5 号位**（业务查询 Query 类，非 4 号位 维护页）

## 二.4 P1-2（契约 3 号位 起草 + 2 号位 协助项）影响范围端点

**3 号位 拍板依据**：
- 契约由 3 号位（Candidate / RunLifecycle 查询 Owner）起草
- 数据可给性（受影响单是否可从排程结果派生）= 2 号位 能力，非裁决项
- 草案端点：`GET /api/candidate/{id}/expedite-impact`，DTO 含 `expeditedOrder/affectedOrders/protectedOrders/changes`

**4 号位 状态**：⏳ 等 3 号位 交付契约草案 + 2 号位 数据可给性回执后启动 D2

### 二.4.1 4 号位 接收契约定型

```
GET /api/candidate/{id}/expedite-impact
Authorization: Bearer <token>          # 需 aps.plan.view
返回 200：
{
  expeditedOrder: { orderId, orderCanonicalId, originalDate, newDate },
  affectedOrders: [{ orderId, orderCanonicalId, originalDate, newDate }],
  protectedOrders: [{ orderId, orderCanonicalId }],
  changes: [{ orderId, before, after, reason: 'EXPEDITE_RESOURCE_CONTENTION' }]
}
```

→ 4 号位 字段级接收，待 3 号位 交付正式契约后按实际命名做 type + api 适配。

## 二.5 P1-3（4 号位 直通 5 号位）OrderCanonicalId ↔ Demand 实体关系

**3 号位 拍板依据**：涉 5 号位 主数据实体语义，3 号位 不判断不协调，**4 号位 直通 5 号位**。

**4 号位 状态**：🚀 已起草独立函件给 5 号位（见关联）

### 二.5.1 直通函主题（独立）

→ [4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md](4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md)（本函并行发出）

### 二.5.2 直通 3 项问题

1. **OrderCanonicalId 与 EXPEDITE 作用的"需求订单（Demand）"是否同实体？**
2. **若同实体，B4 催办 OrderCanonicalId 列补齐后 EXPEDITE 影响范围卡片是否自动带 OrderCanonicalId？**
3. **§10A.1 OrderAdvanceDialog 手填兜底是否可改为自动带入？**

### 二.5.3 5 号位 B4 联动

5 号位 B4 催办（OrderQueryRepository 补 `o.OrderCanonicalId` 列）已在 09-22 发出，本直通函**与 B4 联动**，避免重复催办。

---

# §三、4 号位 立即可动 / 阻塞清单

## 三.1 立即可动（无需等 3 号位）

| 事项 | 启动条件 |
|---|---|
| OPM 治理 API D1 静态梳理 | 本日内（grep lps routing-operations + 申请 Swagger）|
| EXPEDITE P1-3 直通 5 号位 | 本日内（独立函件已起草）|

## 三.2 阻塞（等 3 号位）

| 事项 | 启动条件 | 期望时间 |
|---|---|---|
| OPM 治理 API D2 前端页面 | 3 号位 Swagger 联调地址 | 09-25 前 |
| OPM 治理 API D4 verify [K] 段 | 3 号位 rubric 模板字段确认 | 09-25 前 |
| EXPEDITE P0-1(a) 影响范围卡片 D2 | 3 号位 端点契约草案 + 2 号位 数据可给性 | 09-26 前 |

---

# §四、配合时间

| 期望 | 时间 | 接收方 |
|---|---|---|
| **3 号位 提供 OPM Swagger 联调地址** | 09-25 前 | 3 号位 |
| **3 号位 提供 OPM 真实 DTO 样本** | 09-25 前 | 3 号位 |
| **3 号位 提供 EXPEDITE P1-2 端点契约草案** | 09-25 前 | 3 号位 |
| **3 号位 / 2 号位 P1-2 数据可给性回执** | 09-25 前 | 3 号位 + 2 号位 |
| **4 号位 OPM D1 静态梳理** | 本日内 | 4 号位 |
| **4 号位 OPM D2 前端页面** | 09-25 后启动，09-27 前完成 | 4 号位 |
| **4 号位 OPM D4 verify [K] 段** | 09-27 上线 | 4 号位 |
| **4 号位 EXPEDITE P0-1(a) D2** | 09-26 后启动，09-28 前完成 | 4 号位 |
| **4 号位 EXPEDITE P1-3 直通 5 号位** | 本日内 | 4 号位 → 5 号位 |

---

# §五、能力边界（4 号位 不越界 + 不越级）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md) + 2026-09-22 沟通方式新规：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ❌ 4 号位 **不直接发函给 0 号位 / 1 号位 / 2 号位**（EXPEDITE P1-3 涉 5 号位主数据实体语义，按新规由 4 号位 直通 5 号位）
- ✅ 4 号位 仅前端代码改动
- ✅ 4 号位 收到 3 号位 Swagger + P1-2 契约 + 2 号位 数据可给性后，三绿复验

---

# §六、附档

- 3 号位 来函 ① OPM API：[APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md](APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md)
- 3 号位 来函 ② EXPEDITE 拍板：[3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md)
- 4 号位 原函（EXPEDITE）：[4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md](4号位-2026-09-22-EXPEDITE插单语义补充v1.0影响回执-P0×2+P1×3-给3号位.md)
- 4 号位 EXPEDITE P1-3 直通 5 号位（并行）：[4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md](4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md)
- 4 号位 EXPEDITE 影响范围卡片前端占位：暂无（待 P1-2 契约草案后建）
- 4 号位 OPM 治理页面占位：暂无（待 Swagger 联调地址后建）
- v1.4 §十A 五业务入口契约：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4 §十A](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
- 0号位 Q4-B 裁决：《PriorityMode=EXPEDITE强度定义裁决》、[APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md](APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md)
- 5 号位 B4 催办联动：OrderQueryRepository 补 `o.OrderCanonicalId` 列（09-22 已发）
- 冻结基线单向对齐原则：[[feedback_baseline_chain.md]]

---

**发送人**：4 号位 ｜ **接收人**：3 号位（主承接）/ 0 号位 / 2 号位 / 1 号位 / 5 号位（备查） ｜ **日期**：2026-09-23