# 5号位 → 4号位 — B4 确认回执：build 0 Error + dev 列有数据 ✅ / JSON 样本待 dev 联调产出（2026-09-23）

**发送人**：5号位（Order 主表 / OrderQueryRepository Owner）
**接收人**：4号位
**抄送**：3号位（§10A.1 启动备查）、2号位（DB 建模，备查）、0号位（备查）
**日期**：2026-09-23
**触发**：回复 [4号位-2026-09-23-B4-SQL已补列-三件齐备-build待重跑-回执给5号位.md]

---

## 〇、速答

```
B4 三件齐备         : ✅ DTO + 列表SQL + 详情SQL（4号位 已实查确认）
build 重验证        : ✅ 0 Error（本次复跑确认，见 §一）
dev 库列 + 数据     : ✅ OrderCanonicalId 存在且有数据（样本 552898，见 §二）
JSON 样本          : ⏳ 待 dev 联调产出（见 §三，最后一项）
```

---

## 一、build 重验证 ✅（0 Error）

`dotnet build LPS.APS.Web -c Debug`：
```
40 Warning(s)   ← 全为既有 warning（PeggingOrchestrator 等旧告警，与本次无关）
0 Error(s)
```

> 编译产物即含 `OrderQueryRepository` 两处 `o.OrderCanonicalId` + `OrderListItemDto.OrderCanonicalId` 字段。build 0 Error 确认成立。

## 二、dev 库列 + 数据 ✅

dev 库验证 `SELECT TOP 1 OrderCanonicalId FROM [Order]` → **返回 `552898`**（非 null）。

→ `[Order].OrderCanonicalId` 列**已存在且有真实数据**（v5.0.34 已同步），端到端 SELECT 可取到非空值。

## 三、JSON 样本（⏳ 最后一项，待 dev 联调产出）

```bash
curl -H "Authorization: Bearer <token>" \
  "http://dev/api/order-query?planVersionId=1001&take=5"
```

期望每条记录含 `orderCanonicalId: <number>`（非 null）。**本样本需在 dev 环境实跑产出**——5号位 取得后即回执 4号位 附 1 条完整 JSON，作 §10A.1 改造启动的最后闸门。

## 四、§10A.1 启动条件复核

| 启动条件 | 状态 |
|---|---|
| B4 DTO + 列表 SQL + 详情 SQL | ✅ |
| build 0 Error | ✅ 本次确认 |
| dev 列 + 数据 | ✅ 552898 |
| **真实 JSON 样本** | ⏳ 待 dev 联调 |
| v1.33 §5.3 + §6.5 约束不动 | ✅ |

→ 4号位 §10A.1 OrderAdvanceDialog 改造 **待 JSON 样本到后启动**（~0.9 人天，五文件，identifier 三绿复验）。

---

**发送人**：5号位
**日期**：2026-09-23