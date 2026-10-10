# APS V1 Setup 规则承载重构——4号位 F-1 第三轮回执答复（3号位）

**文档性质**：跨号位正式回执（3号位 → 4号位）——针对 4号位 `F-1第三轮回执-uncovered-stats真实情况-给3号位(1).md` 的正式答复，收口两项待办：#1 uncovered-stats 处置、#2 F-1 撤销确认。
**发送方**：3号位 ｜ **接收方**：4号位（前端，主承接方）｜ **日期**：2026-09-23
**抄送**：0号位（备查）
**关联**：4号位 第三轮回执｜本会话 `DTO契约对齐函`｜`端点实现完成通知`｜F-1 确认件｜契约登记件 S-10

---

## 〇、速答

```
F-1 维持成立              : ✅ 按 13 端点零改动开工（真源 SetupRuleController.cs 未动）
#2 撤销状态（Controller）   : ✅ 已彻底删除（4 文件退役，build 0 错误）——4号位轮执观察已过时
#1 #10 uncovered-stats     : ✅ 已按选项 (d) 补齐实现（S-10 登记），前端可切真实数据
ISetupRuleService 改动      : ✅ 仅新增 GetUncoveredStatsAsync，既有 13 端点签名零改动
本轮新增 lps 改动           : ✅ 无（承载已定，端点为独立新增）
```

---

## 一、#2 F-1 撤销确认 ✅（补证 4号位 轮执 §二）

4号位 第三轮回执载明 `SetupTransitionRuleController.cs`（mtime Sep 23 09:26）**仍存在**——该观察**已过时**。3号位 本会话已删除该 Controller，并连同退役 `SetupTransitionRuleService.cs` / `ISetupTransitionRuleService.cs` / `SetupTransitionRuleServiceTests.cs`，**共 4 文件**已删，`dotnet build LPS.APS.sln` **0 错误**。

| 文件 | 状态 |
|---|---|
| `SetupTransitionRuleController.cs` | ✅ 已删 |
| `SetupTransitionRuleService.cs` | ✅ 已删（无运行时消费者，按 #4 退役）|
| `ISetupTransitionRuleService.cs` | ✅ 已删 |
| `SetupTransitionRuleServiceTests.cs` | ✅ 已删 |
| `SetupRuleController.cs`（真源，13 端点）| ✅ 未动 |

→ 双套 HTTP 面问题**彻底闭环**；前端统一走 `SetupRule`（13 端点 + 新增 #14）通道。

## 二、#1 #10 uncovered-stats 处置 ✅（已采 3号位 倾向选项 d）

4号位 前两封假设"#10 是 F-1 §二.1 漏列"经重核修正——后端从未实现、前端 mock 兜底。3号位 采**选项 (d) 补齐端点**（非补 F-1 列表，属独立新增 #14）：

| 项 | 值 |
|---|---|
| 路由 | `GET /api/governance/setup-rules/uncovered-stats`（与前端 setup.ts:424 一致）|
| 权限 | `aps.setup.view` |
| 数据源 | `[Task].SetupSource='SETUP_RULE_MISSING_ZERO_FALLBACK'` 命中频次聚合（runId 经 PlanVersion.SourceScheduleRunId JOIN）|
| 四级下钻 | `runId → departmentId → operationCode → resourceId → materialId`（全可选，未传不筛）|
| 落地 | `SetupRuleController#14` + `ISetupRuleService.GetUncoveredStatsAsync` + `ISetupUncoveredStatRepository/SetupUncoveredStatRepository` + DTO `SetupUncoveredStatDto`；契约登记件 **S-10 ✅** |
| 验证 | `dotnet build` 0 错误；Setup 相关 56 测试全绿（53 既有 + 3 新增）|

**架构红线**：只读查询不建 DDL（#6 ✅）、只增接口方法不改既有签名（#5 ✅）、仓储不引 Scheduling（#1 ✅ 用 SetupSource 字面量）、返回列表（#4 ✅）。

**前端切换**：`APS_USE_MOCK=false` 即调真实端点；`MOCK_UNCOVERED_STATS` 降级兜底保留；SetupUncovered 页**取消「Demo 数据」提示**。

## 三、ISetupRuleService 接口细节确认（无冲突）

| 项 | 现状 | F-1 13 端点契约 | 冲突 |
|---|---|---|---|
| `ListAsync(long, string)` 合并 | Service 层 1 方法按 ruleType 过滤 | HTTP 层仍拆 `setup-rules/exact` + `/default` 2 端点 | ✅ 无（接口合并，HTTP 保持拆分）|
| `DeleteAsync(long)` 合并 | 单方法 | HTTP 层仍拆 2 DELETE 端点 | ✅ 无 |
| 新增 `GetUncoveredStatsAsync` | #14 独立新增 | 不触碰既有 13 | ✅ 无 |

## 四、待 4号位 回执

| # | 项 | 确认 |
|---|---|---|
| 1 | `SetupUncoveredStatDto` 前端字段 vs 实现（§二 字段表）是否一致，不一致列差异 | ☐ |
| 2 | 四级下钻是否符合 SetupUncovered 页交互 | ☐ |
| 3 | 真实数据联调结果（`APS_USE_MOCK=false` + runId）| ☐ |
| 4 | F-1 撤销反向回执（13 端点零改动复验）| ☐ |
| 备注 | | |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**对应通知**：本轮已发 `DTO契约对齐函` + `端点实现完成通知`（本函为其正式收口汇总）
**待办**：4号位 回执 §四 四栏 → 3号位 视字段差异微调 + S-10 收口