# APS V1 Setup —— #10 uncovered-stats 端点已实现——实现完成通知

**文档性质**：跨号位实现完成通知（3号位 → 4号位）——#10 uncovered-stats 后端端点已落地，前端可切真实数据；附前端 DTO 字段最终确认请求 + Controller 残留澄清。
**发送方**：3号位 ｜ **接收方**：4号位（前端）｜ **日期**：2026-09-23
**依据**：4号位 F-1 第三轮回执选项 (d)、3号位 裁决「3号位补齐端点 + Setup治理域 + 对齐前端DTO + 四级下钻全部实现」；契约登记件 S-10 登记
**抄送**：0号位（备查）

---

## 一、端点已实现 ✅

| 项 | 值 |
|---|---|
| 方法 | `GET` |
| 路由 | `/api/governance/setup-rules/uncovered-stats`（与前端 setup.ts:424 一致）|
| 权限 | `aps.setup.view` |
| 数据源 | `[Task].SetupSource = 'SETUP_RULE_MISSING_ZERO_FALLBACK'` 命中频次聚合 |
| 响应 | `ApiResponse<IReadOnlyList<SetupUncoveredStatDto>>`（空结果返回空列表）|

**四级下钻过滤参数**（全部可选，未传不筛）：
`runId` → `departmentId` → `operationCode` → `resourceId` → `materialId`

## 二、响应字段（后端实现采用初拟口径）

| 字段 | 类型 | 说明 |
|---|---|---|
| `scheduleRunId` | number\|null | 排程 Run Id（经 PlanVersion.SourceScheduleRunId）|
| `departmentId` / `departmentCode` | number / string | 生产部门主键 / DeptCode 回带 |
| `operationCode` | string | 工序编码 |
| `resourceId` / `resourceCode` | number / string | 资源主键 / ResourceCode 回带 |
| `materialId` / `materialCode` | number / string | 物料主键 / MaterialCode 回带 |
| `hitCount` | number | 命中频次（Task 计数）|
| `sampleTaskId` | number\|null | 首条命中 Task Id（下钻定位）|

> **请 4号位 确认**：前端 `types/setup.ts` 的 `SetupUncoveredStatDto` 若与本表字段**不一致**，请列差异 → 3号位 在 Controller/DTO 装配处微调（改动成本低，不影响既有 13 端点）。

## 三、mock 可切换真实数据

前端将 `APS_USE_MOCK` 置 `false` 即调真实端点；`MOCK_UNCOVERED_STATS` 兜底保留（仅异常/降级时触发）。SetupUncovered 页**不再需「Demo 数据」提示**（若前端此前已加）。

## 四、关于 Controller 残留（澄清 4号位 第三轮回执 §二）

4号位 第三轮回执载明 `SetupTransitionRuleController.cs`（mtime Sep 23 09:26）**仍存在**——该观察**已过时**。3号位 本会话已删除该 Controller（连同 `SetupTransitionRuleService.cs` / `ISetupTransitionRuleService.cs` / `SetupTransitionRuleServiceTests.cs` 共 4 文件），`dotnet build` 0 错误；旧 API 全部退役，前端统一走 `SetupRule`（13 端点 + 新增 #14）通道。双套 HTTP 面问题**已彻底闭环**。

## 五、待 4号位 回执

| 项 | 确认 |
|---|---|
| `SetupUncoveredStatDto` 前端字段与本表 §二 是否一致（不一致请列差异） | ☐ |
| 四级下钻（§一）是否符合 SetupUncovered 页交互 | ☐ |
| 真实数据联调结果（`APS_USE_MOCK=false` 下 runId 过滤） | ☐ |
| 备注 | |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**对应**：`APS_V1_Setup_uncovered-stats_DTO契约对齐函_3号位致4号位_v1.0_20260923.md`
**待办**：4号位 回执字段确认/联调结果 → 3号位 视差异微调 + 收口 S-10