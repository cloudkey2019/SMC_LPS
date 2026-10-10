# 3号位 → 4号位：白天候选统一入口 E 端点交付（summary / comparison / expedite-impact 骨架）

> **发件**：3号位（应用编排、规则/参数治理、权限与范围治理 Owner）
> **致**：4号位（前端）
> **抄**：0号位（备查）、2号位（备查）
> **日期**：2026-09-24 ｜ **版本**：v1.0
> **依据**：《APS_V1_D_E_Q2回执_2号位致3号位_v1.0_20260924.md》E-1/E-2/E-3 最小集
> **性质**：**端点交付通知** —— 白天候选（插单）统一只读入口 3 端点已落码并提交，供 4号位 前端接线联调。

---

## 〇、一句话

白天候选统一入口 **3 个只读端点已实现并提交**：候选运行摘要（`summary`）、候选 vs 基础差异（`comparison`）、加速影响范围（`expedite-impact`，骨架占位）。均挂 `aps.candidate.view` 权限码，返回统一 `ApiResponse<T>` 信封。4号位 可接线联调，`expedite-impact` 本期为骨架（`ready=false`），真实内容待 P1-2 数据链路就绪后填充。

---

## 一、端点清单

| 端点 | 方法 | 授权码 | 状态 |
|---|---|---|---|
| `GET /api/candidate/{candidateId}/summary` | GET | `aps.candidate.view` | ✅ 已实现 |
| `GET /api/candidate/{candidateId}/comparison` | GET | `aps.candidate.view` | ✅ 已实现 |
| `GET /api/candidate/{candidateId}/expedite-impact` | GET | `aps.candidate.view` | ✅ 骨架（`ready=false`） |

> **`candidateId` 语义 = `ScheduleRun.Id`（候选 Run Id），非 `PlanVersion.Id`** —— 请前端传候选 Run 的 Id，勿传计划版本 Id。

---

## 二、统一响应信封（`ApiResponse<T>`）

所有端点返回同一结构，Success/Fail 两种：

```jsonc
// 成功
{ "code": 200, "message": "success", "data": { … }, "traceId": null, "timestamp": "2026-09-24T…" }

// 失败（code 即 HTTP 业务码）
{ "code": 404, "message": "候选运行不存在：…", "data": null, "traceId": null, "timestamp": "…" }
```

| code | 语义 |
|---|---|
| 200 | 成功，`data` 为对应 DTO |
| 404 | 候选 Run 不存在，或超出当前用户业务范围（越权兜底） |
| 400 | 仅 `comparison`：该 Run 无 BasePlanVersionId（无法做差异） |

---

## 三、DTO 形状（JSON 均为 camelCase）

### 3.1 `summary` → `CandidateSummaryDto`

```jsonc
{
  "candidateId": 123,
  "runStatus": "COMPLETED",          // RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED
  "orderCount": 45,
  "taskCount": 812,
  "createdBy": "zhang.san",
  "createdAt": "2026-09-24T10:30:00"
}
```

### 3.2 `comparison` → `CandidateDiffDto`（最小集，后续可按需扩）

```jsonc
{
  "basePlanVersionId": 100,          // = ScheduleRun.BasePlanVersionId
  "candidatePlanVersionId": 101,     // 候选 PlanVersion 壳
  "addedOrders": [ 1021, 1022 ],     // 候选新增订单（OrderId）
  "removedOrders": [],               // 候选移除订单（OrderId）
  "changedTasks": [
    {
      "orderId": 1023,
      "materialId": 5001,
      "operationSeq": 10,
      "operationCode": "OP-10",
      "routeCode": "R1",
      "pathId": 1,
      "resourceId": 208,
      "plannedStartTime": "2026-09-24T12:00:00",
      "plannedEndTime": "2026-09-24T12:30:00"
    }
  ]
}
```

> 差异口径：订单级差异由 Task 主键（OrderId+MaterialId+OperationSeq+OperationCode+RouteCode+PathId）差集去重 OrderId 派生；`changedTasks` = 同主键但时间(/资源)变化的候选侧 Task。

### 3.3 `expedite-impact`（骨架占位）

```jsonc
{
  "candidateId": 123,
  "ready": false,
  "message": "expedite-impact 待 P1-2 数据就绪后填充"
}
```

> 本期仅做宿主骨架（路由存在即对接成功）；真实影响范围（expeditedOrder/affectedOrders/protectedOrders/changes）待 P1-2 数据链路就绪后由 3号位 填充。前端当前勿对 `ready=false` 做真实业务渲染，可先放占位。

---

## 四、与既有 Candidate 动作的关系

本通知为**新增只读 GET 统一入口**；既有 `GovernanceController`（`api/governance`）下的候选动作不变、并存：

| 既有端点 | 动作 |
|---|---|
| `POST /api/governance/run/candidate` | 白天候选 Run 创建（B-1 入口） |
| `POST /api/governance/plan-version/{id}/confirm-candidate` | 候选确认（待激活） |
| `POST /api/governance/plan-version/{id}/activate-candidate` | 候选激活（→ACTIVE） |
| `GET /api/governance/plan-version/{a}/compare-with/{b}` | 候选 vs 基础富对比（H1，task 级） |

`api/candidate` 是 2号位 09-24 定的「Candidate 统一入口」查询宿主，与 `api/governance` 的富对比（H1）互补、非替代。

---

## 五、联调提示

- API 已提交（SVN），run 起应用即可命中 `api/candidate` 路由；
- `candidateId` 取自「白天候选 Run 列表/创建」返回的 `ScheduleRun.Id`；
- 骨架 `expedite-impact` 返回 `ready=false`，前端按占位处理勿报错；
- 若需「富对比」（Task 级计数/按工序下钻），仍走 `api/governance/plan-version/{a}/compare-with/{b}`。

---

## 六、红线声明

- 3号位 仅做 Web 薄层 + 委托 `IScheduleQueryService`（只读查询），未触碰排程编排（编排归 2号位 SchedulingOrchestrator，E-4）；
- `IScheduleQueryService`/`ScheduleQueryService` 仅**新增** 2 方法（红线 #5），零 DDL（红线 #6）；
- 未触碰 1号位/5号位 辖区。

---

**发件**：3号位 ｜ **日期**：2026-09-24 ｜ **版本**：v1.0
**本件性质**：端点交付通知（E 3 端点已落码提交，供 4号位 接线联调）。