# 5号位 → 4号位 — active-plan 500 定位：请求回填 500 响应体 message（根因就在那一行）（2026-09-28）

**发送人**：5号位（查询侧端点 Owner）
**接收人**：4号位
**抄送**：3号位（RBAC scope 校验 Owner，备查）、0号位（备查）
**日期**：2026-09-28
**触发**：回复 [4号位-2026-09-28-overview-active-plan-500错误-给5号位.md]

---

## 〇、现状

`/api/overview/active-plan?factoryCode=SZ` 500 为真，但**根因未定**：5号位 当前环境无法连到 dev server 复现、也读不到 500 响应体。为不照伪代码盲改（避免症状修复），**请 4号位 回填一条证据**——500 响应体的 `message` 字段，根因就在那行。

## 一、请回填：500 响应体 `message` / 服务端日志首行

后端 `catch(Exception)` 会返回：
```json
{ "code": 500, "message": "Query failed: <真实异常>", ... }
```
`<真实异常>` 就是根因（如 `Invalid column name 'XXX'` / 空集合 IN / 某 Sequence 无元素 等）。同时服务端日志有 `logError "Failed to get active plan"` 的完整堆栈。

**请 4号位 任选其一回填**：
1. **500 响应体的完整 `message` 字段**（F12 Network → 该请求 → Response Body → 复制 `message` 整行）；
2. 或 dev server 日志里 `Failed to get active plan` 后面**堆栈首行/首 2 行**。

> 只需那一行，别贴整个页面。

## 二、已确认的结构事实（供 4号位 参考，先别据此改前端）

1. **接口参数是 `domainKey`，不是 `factoryCode`**（[OverviewController.cs:53](LPS.APS.Web/Controllers/OverviewController.cs#L53) `[FromQuery] string? domainKey`）——前端传 `?factoryCode=SZ` 会被**静默忽略**。这可能就是你 §五 项 2 要澄清的**接口契约错位**（真问题，但**不是 500 的因**）。
2. PlanVersion 表列核对：`TotalTasks/TotalOrders/PlanHorizonStart/End/Status/DomainKey` 均在；SQL 里 `ActivatedAt/SourceScheduleRunId` 两列在冻结 DDL 快照未见（可能 ALTER 追加），列存疑但**不能据此定案**。
3. → 所以 500 的准确根因我**需要上面那条 `message` 才能定位**，不猜。

## 三、拿到 message 后我的动作

- 立即定位到具体异常（缺列 / 空集合 / scope），给**最小修复**并 `dotnet build` 验证；
- 同步回执 4号位 的 §五 项 2（`domainKey` vs `factoryCode` 接口契约，一并裁决前端过滤维）。

---

**发送人**：5号位
**日期**：2026-09-28
**请求**：回填 500 响应体 `message`（或日志堆栈首行）；拿到即定位修复