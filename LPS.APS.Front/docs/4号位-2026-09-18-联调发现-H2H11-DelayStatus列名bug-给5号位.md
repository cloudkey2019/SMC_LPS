# 4号位 → 5号位 — 联调验证发现 DelayStatus 列名 bug（2026-09-18）

**发件方**：4号位  
**收件方**：5号位（OrderQueryController / OrderQueryRepository 维护人）  
**触发**：verify-integration.mjs 全量跑通 85/3，其中 **H2 / H11 失败** = 后端 SQL 列名引用错误。  
**关联**：[5号位-2026-09-17-URL-DTO对接缺口回复-给4号位.md](5号位-2026-09-17-URL-DTO对接缺口回复-给4号位.md) §一 2.1（Order Summary 端点 9/17 已新增并 dotnet build 0 Error，本次是 9/18 联调发现的 SQL bug）。

> 📂 **本文件只包含联调实测发现的后端 bug**，前端代码无问题。

---

## 一、H2 / H11 失败详情

**verify-integration.mjs 输出**：

```
[H] 第一波真实页面接入
  ❌ H2 GET /api/order-query?planVersionId=1&pageIndex=1&pageSize=10
     code=500, data=false
  ❌ H11 GET /api/order-query/summary?planVersionId=1
     code=500, keys=-
```

**错误响应体**：

```
H2:
{
  "code": 500,
  "message": "Query failed: 列名 'DelayStatus' 无效。\r\n列名 'DelayStatus' 无效。",
  "timestamp": "2026-09-18T08:45:26.5845716+08:00"
}

H11:
{
  "code": 500,
  "message": "Query failed: 列名 'DelayStatus' 无效。\r\n列名 'DelayStatus' 无效。\r\n列名 'DelayStatus' 无效。\r\n列名 'DelayStatus' 无效。",
  "timestamp": "2026-09-18T08:45:26.6904698+08:00"
}
```

**错误特征**：
- 端点 `GET /api/order-query`（H2）和 `GET /api/order-query/summary`（H11）都引用了不存在的列 `DelayStatus`
- H2 报 2 次（说明 2 处子查询引用）
- H11 报 4 次（说明 4 处子查询引用——分别对应 onTimeCount / delayedCount / riskCount / unscheduledCount 四个聚合）

---

## 三、根因

`OrderQueryRepository`（或同类 SQL）用 `DelayStatus` 列名做 WHERE 过滤，但当前 dev 数据库 `dbo.OrderQuery`（或对应表）实际列名不是 `DelayStatus`。

**可能原因**（待 5号位 核实）：

1. **列名拼写差异**：
   - 实际可能是 `Delay_Status`（下划线分隔）
   - 或 `delay_status`（全小写）
   - 或 `delayStatus`（camelCase，EF Core 习惯）
   - 或 `Status`（更宽泛）

2. **表结构不一致**：
   - 可能 OrderQuery 表没有「按时 / 延误 / 风险」状态字段
   - 需 5号位 提供真实 OrderQuery 表 schema

3. **回执 DTO 与 SQL 不匹配**：
   - 5号位 9/17 回执 §一 2.1 给前端 DTO 时承诺 `{ onTimeCount, delayedCount, riskCount, unscheduledCount }`
   - 但后端 SQL 实际未按这个 DTO 实现聚合（直接 SELECT 不分状态）

---

## 四、5号位 决策建议（4号位 推荐 A）

### 选项 A（推荐）：核对 OrderQuery 表真实列名 + 修 SQL

**理由**：
- 5号位 9/17 回执已承诺 4 字段聚合（onTimeCount/delayedCount/riskCount/unscheduledCount），联调报错说明 SQL 引用了未实现该 DTO 的字段
- 4号位 文档 v1.4 §八.2 明确 OrderSummary 是核心 KPI（顶栏展示），阻塞联调全绿

**改动范围**（5号位）：
- `OrderQueryRepository.cs`：核对 OrderQuery 表真实列名（推测是 `delay_status` 或 `status`）
- 修 4 处 SQL 子查询：分别映射 `DelayStatus IS NULL OR 'ON_TIME'` / `= 'DELAYED'` / `= 'RISK'`（或对应真实列值）
- `/api/order-query` 主列表 SQL 同步修
- dev seed 数据若缺 `DelayStatus` 字段值，需补默认 `ON_TIME` 或 `NULL`

### 选项 B：临时把 DelayStatus 过滤去掉，返回 totalCount

**缺点**：
- DTO 退化为 `totalCount` 单字段，与 9/17 回执承诺不符
- 阻塞 Order 页顶栏 KPI 渲染（U04 验收）

### 选项 C：5号位 先回执真实列名，4号位 暂不改前端

**理由**：4号位 已按 9/17 DTO 写好前端 mapping，前端无需改；只等 5号位 修后端 SQL

**4号位 默认**：选项 A（如能在 9/19 前修好不阻塞联调全绿）。

---

## 五、4号位 已完成的配合

- verify-integration.mjs H2/H11 断言保留（标红作回归基线）
- 前端 `order.ts` getSummary() mapping `onTimeCount → ON_TIME` / `delayedCount → DELAYED` / `riskCount → AT_RISK` / `unscheduledCount → UNSCHEDULED` 已就位（@see `api/aps-v1/order.ts` L88-94），后端修好即生效
- 当前 H2/H11 失败**不影响**其他 5号位 端点的验证（H4/H5/H6/H7/H10/H12 全绿）

---

## 六、时序更新

- **2026-09-17 下午**：5号位 回执 4 项（Order Summary / PI 详情 / ManualETA 复核全部确认）
- **2026-09-18 上午**：
  - verify-integration.mjs 全量 85/3
  - **H2/H11 DelayStatus 列名 bug 发现** → 等待 5号位 选项 A/B/C 决策
  - **G4 FK 约束 bug 发现** → 等待 3号位 修（详见致 3号位 文件）
  - **Candidate 列表 17 字段** 仍待 5号位 选项 A/B/C 决策（@see [3号位回执落地-给5号位.md](4号位-2026-09-17-3号位回执落地-给5号位.md) §一）
- **2026-09-19**（待 5号位 回复）：
  - H2/H11 选项 A/B/C 决策
  - Candidate 列表 17 字段选项 A/B/C 决策
- **2026-09-20~24**：4号位 步骤 7-10（RBAC 4 页 + Audit + Domain + Run/Candidate 收尾）
- **2026-09-25 起**：联调全绿（v1.4 §二十九 U23-U50）

---

## 七、回执格式建议

```
四#选项: A（修 SQL + 补 seed）✓ | B（退化 totalCount）✓ | C（暂不改前端）✓
四#真实列名: <5号位 确认 OrderQuery 表 delay_status / status / 其他>
四#预计: 9/19 前 / 9/22 前 / 9/24 前
```

---

**附**：本次验证 commit 范围（前端）
- `frontNew/scripts/verify-integration.mjs` ← H 段加 planVersionId（H2/H5/H11 需 planVersionId）

verify-integration 全量：**85 通过 / 3 失败**（3 红 = H2 + H11 + G4，均后端 bug）

— 4号位