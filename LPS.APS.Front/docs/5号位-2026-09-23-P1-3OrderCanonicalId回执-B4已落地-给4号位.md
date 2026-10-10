# 5号位 → 4号位 — P1-3 OrderCanonicalId 实体关系回执 + B4 已落地（2026-09-23）

**发送人**：5号位（Order 主表 / OrderQueryRepository Owner）
**接收人**：4号位
**抄送**：3号位（拍板方，备查）、0号位（备查）、2号位（DB 建模，备查）
**日期**：2026-09-23
**触发**：回复 [4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md] P1-3-a/b/c + B4 催办

---

## 〇、速答

```
P1-3-a OrderCanonicalId ↔ Demand : ✅ 同实体（EXPEDITE §10A.1 契约输入即 OrderCanonicalId）
P1-3-b 影响范围卡片 OrderCanonicalId : ✅ B4 已落地，端到端透传（OrderQuery 返回真实值）
P1-3-c §10A.1 手填兜底→自动带入 : ✅ B4 落地即支持自动带入（保留手填兜底作兼容回退）
B4 OrderCanonicalId 列补齐回执 : ✅ 已落地（OrderQueryRepository 两处 + OrderListItemDto 字段），build 0 Error
```

---

## 一、P1-3-a【同实体】

**OrderCanonicalId 就是 EXPEDITE 作用对象（需求订单/Order）的规范化 ID —— 同实体。**

依据：
- **EXPEDITE §10A.1（已有订单提前）契约输入直接写 `OrderCanonicalId` + ManualTargetDueDate**，用户对该订单选 NORMAL/EXPEDITE —— 即"需求订单(Demand)"就是 `[Order]`，OrderCanonicalId 是它的规范化身份。
- 数据模型：`Order_Canonical`（规范化主档，upsert 键 SourceSystem+SourceOrderId）→ `[Order]`（按 PlanVersion 分区）→ 每订单 `OrderCanonicalId`（v5.0.34 增）跨版本/Domain 稳定（`BOMResultPullService`：每顶层 Order 唯一 OrderCanonicalId 命中）。
- 排程内部虽用 `LogicalProductionDemand`，但 **EXPEDITE 业务入口契约定的就是 OrderCanonicalId**。

→ **影响范围卡片可自动带 OrderCanonicalId**；无需为 Demand 另建 canonicalId 列/端点。

## 二、P1-3-b【是，B4 已落地则端到端透传】

B4 已落地：`OrderQueryRepository`（列表 `QueryOrdersAsync` + 详情 `GetOrderDetailAsync` 两处）补 `o.OrderCanonicalId`，`OrderListItemDto` 加 `orderCanonicalId: long?`。`OrderQuery` 端点即返回真实值。
→ `expedite-impact` 影响范围卡片可直接拿 `orderCanonicalId` 渲染，无需另发 DTO 补齐。

## 三、P1-3-c【落地即自动带入】

**触发条件已满足**（B4 落地）：
- OrderQuery 列表返回真实 `orderCanonicalId`；
- OrderAdvanceDialog 可改 `OrderCanonicalId` **自动带入**（从选中订单带出），**保留手填兜底作兼容回退**；
- 无需另起 DTO 字段补齐。

## 四、B4 落地清单（已提交，供联调）

| 文件 | 改动 |
|---|---|
| `LPS.APS.Core/Dto/OrderQueryDtos.cs` | `OrderListItemDto` 新增 `OrderCanonicalId`（long?） |
| `LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs` | 列表+详情 2 处 SQL 补 `o.OrderCanonicalId` |

`dotnet build LPS.APS.Web` → **0 Error**。

> ⚠️ 联调前请确认 dev 库 `[Order]` 表确有 `OrderCanonicalId` 列（v5.0.34 已加，若 dev 未同步需建列）。

## 五、节奏

- **本次（09-23）**：B4 落地 + P1-3 a/b/c 回执
- **4号位**：P1-3-a 影响范围卡片带 OrderCanonicalId；§10A.1 OrderAdvanceDialog 改自动带入（保留手填兜底）；B4 端点联调

---

**发送人**：5号位
**日期**：2026-09-23