# 3号位 致 4号位 — EXPEDITE 影响范围端点契约（P1-2，**v1.0 定版**）

> **文件名注**：本文本已由 v0.9 草案升级为 **v1.0 定版**；文件名沿用 v0.9 以维持既有引用链，内容以 `契约版本：v1.0（定版）` + 本行注为准。v0.9 草案文本废弃。
>
> **发送人**：3号位（Candidate / RunLifecycle 查询 Owner，契约起草方）
> **接收人**：4号位（前端，消费方）
> **抄送**：2号位（Data / Base 比对派生 + 投影层保护兜底）、0号位（业务保护识别字段补裁）、1号位（新方案+原因供给）
> **日期**：2026-09-23
> **性质**：**端点契约定版** —— 支撑《3号位致4号位_EXPEDITE影响回执拍板_v1.0》P0-1(a) 前端「EXPEDITE 影响范围」UI 模块
> **依赖**：0号位 Q4-B §六 影响范围输出示例；红线#5「接口即契约」新端点先落契约文档
> **定版依据（D1/D2/① 数据可给性已确认）**：2号位《Q4-B EXPEDITE 收口及缺口①②④ 回执 v1.0》——D2 归因区分可行、数据齐备；① 保护边界选 (a) 投影层排除、红线投影层兜底书面确认；D1 数据分工（1号位 §四 回执）已认证。遗留 0号位 业务保护识别字段 + RunScope.BusinessScope 载体补裁，均为跨号位裁决，不阻塞本端点核心契约。

---

## 〇、速览

```
端点          GET /api/candidate/{candidateId}/expedite-impact
契约版本      v1.0（定版）
权限          aps.plan.view
HTTP         200 数据返回 / 400 无效 candidateId / 404 candidate 不存在/MODE 非 EXPEDITE / 403 无权限
返回时机     实时计算（Candidate 排程结果已产出，派生影响范围，非选购决策点，不预计算）
数据分工     newDate=1号位 ExplanationFacts；originalDate=2号位 与 Base PlanVersion 比对；归因=2号位 资源槽抢占判定
```

---

## 一、接口定义

```
GET /api/candidate/{candidateId}/expedite-impact
```

**语义**：返回一次 Candidate 重排中，因 EXPEDITE 需求订单优先竞争资源而产生的**影响范围**——即被 EXPEDITE 需求「推迟 / 挤占」的其他可调整需求订单，及各单原→新计划日期变化。

**入参**：

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `candidateId` | long | ✅ | 本次 Candidate（ScheduleRun / 重排运行）Id |

**响应**（200）：

```jsonc
{
  "expeditedOrder": {
    "orderCanonicalId": "10001",        // EXPEDITE 被提前的需求订单
    "originalDate": "2026-09-30T00:00:00",
    "newDate": "2026-09-25T00:00:00"    // EXPEDITE 提前至的新计划日期
  },
  "affectedOrders": [                    // 被推迟（挤占）的其他可调整订单，受影响由大到小
    {
      "orderCanonicalId": "10002",
      "originalDate": "2026-09-25T00:00:00",
      "newDate": "2026-09-28T00:00:00",   // 因 EXPEDITE 资源竞争被推迟
      "reason": "EXPEDITE_RESOURCE_CONTENTION"
    }
  ],
  "protectedOrders": [                     // 参与计算但未被推移（保护边界未被突破）
    {
      "orderCanonicalId": "10004",
      "originalDate": "2026-09-24T00:00:00",
      "newDate": "2026-09-24T00:00:00",    // 不变
      "reason": "PROTECTED_BOUNDARY"
    }
  ],
  "summary": {
    "affectedCount": 1,
    "protectedCount": 1,
    "totalDelayDays": 4        // Σ(newDate-originalDate) of affectedOrders；供业务快速评估
  }
}
```

> **`reason` 归因判定（D2，2号位 已确认可行）**：同一 `ResourceId` 上，Base 版本 `[Start,End]` 槽原占单 X（`OrderId`）、New 版本同槽改占 EXPEDITE 单 Y → 归因 `EXPEDITE_RESOURCE_CONTENTION`；`ReasonCode`（求解根因）仅佐证，不承担归因。

---

## 二、DTO 契约（C#）

```csharp
namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>EXPEDITE 影响范围查询结果（P1-2，对应 0号位 Q4-B §六 输出示例）</summary>
public sealed record ExpediteImpactResponse
{
    public OrderImpactDto ExpeditedOrder { get; init; } = default!;      // 本次被提前的需求订单
    public IReadOnlyList<OrderImpactDto> AffectedOrders { get; init; } = [];  // 被推迟的可调整单
    public IReadOnlyList<OrderImpactDto> ProtectedOrders { get; init; } = []; // 参与但未推移的受保护单
    public ImpactSummaryDto Summary { get; init; } = default!;
}

/// <summary>单个需求订单的影响明细；OrderCanonicalId 为鉴另键（= ScopeJsonV2.OrderTargets 身份）</summary>
public sealed record OrderImpactDto
{
    public string OrderCanonicalId { get; init; } = default!;  // Demand 业务键（OrderCanonicalId，string 展示）
    public DateTime OriginalDate { get; init; }                // 重排前计划日期（2号位 Base 比对）
    public DateTime NewDate { get; init; }                     // 重排后计划日期（1号位 新方案）
    public string Reason { get; init; } = default!;            // EXPEDITE_RESOURCE_CONTENTION / PROTECTED_BOUNDARY
}

/// <summary>影响范围汇总</summary>
public sealed record ImpactSummaryDto
{
    public int AffectedCount { get; init; }
    public int ProtectedCount { get; init; }
    public int TotalDelayDays { get; init; }
}
```

**字段真源（数据分工已确认，2号位 回执 §三）**：
- `OrderCanonicalId`：Demand 业务键，真源 `DemandBalance.DemandKey`（= OrderCanonicalId）；EXPEDITE 身份经 2号位 `LoadExpediteOrderCanonicalIdsAsync`（`PeggingOrchestrator.cs:3792`）读 `OrderTargets`。
- `originalDate`：**2号位 与 Base PlanVersion 比对派生**（1号位 纯内存不知旧计划，不能提供）。
- `newDate`：**1号位 新方案**（`ExplanationFacts`/`FinalDraftId`；1号位 回执 §四 确认能提供）。
- `Reason`：**2号位 资源槽占位竞争判定**（Base 槽原占单 → 被 EXPEDITE 单抢占），`ReasonCode`/`EvidenceJson` 佐证。

---

## 三、契约形态约定

1. **读端点归 3号位 契约，数据派生归 2号位**：端点接口由 3号位（Candidate 查询 Owner）；`originalDate` 比对 + `Reason` 归因判定由 2号位 在 `expedite-impact` 装配前实现（与 D1 日期 delta 同源扩展，2号位 已认领）。
2. **不新增 DDL、不建投影**：影响范围为派生结果（读），字段全部取自现有排程结果契约（`Task.PlanVersionId/ResourceId/PlannedStart/EndTime/OrderId` + Base PlanVersion + `Order[].OrderCanonicalId`）。
3. **前端 4号位**：消费本契约渲染 P0-1(a) 影响范围卡片；未依赖后端 DDL。
4. **返回时机**：实时计算。Candidate 排程结果已产出、影响范围是派生查询，非选购决策点，无需预计算/缓存。

---

## 四、保护边界（① 已定案 —— 投影层排除）

**V1 采用 2号位 回执 (a)：保护边界由 2号位 投影层排除兜底，不接 Solver `ExecutionConstraints`（b 供给源缺失 + P0-05 冻结不新增保护字段）**。书面确认红线（执行事实 / 业务保护 / 技术硬约束）由 2号位 投影层兜底。

**识别口径 + 一处待 0号位 明确**：

- **执行事实**（已开工/已完工）：2号位 用 `WorkOrderStatus` 四态（`RELEASED/IN_PROGRESS/CLOSED/DELETED`，`ExistingExecutionContextDto`）识别排除 —— ✅ 已可兜底。
- **业务保护（Firm/Frozen）**：2号位 侧**无订单级物理字段**（P0-05 冻结不新增 `IsFirm/IsFrozen/ExecutionLock`）——**待 0号位 明确**：(i) 复用 `IsInFrozenZone` 时间窗口径，还是 (ii) 由 3号位 在 ScopeJson 增传「保护订单集合」。0号位 明确前，2号位 投影层只排除执行事实，「业务保护」以求解不改 `FirmHorizonDays` 时间窗内计划为过渡兜底。
- **是否列入 `protectedOrders`**：若后续 0号位 明确业务保护识别径，被保护未推移单入 `protectedOrders`（`reason=PROTECTED_BOUNDARY`）；过渡期以 FirmHorizonDays 窗内计划为兜底，不强行列示。

## 五、落地归属与依赖

```
1号位 新方案 Task + 根因（ExplanationFacts）      —— 1号位 已具备（零改契约）
  → 2号位 Base 版本比对：originalDate delta + ResourceId 槽抢占归因（D1/D2 同源派生）—— 2号位 认领
  → 3号位 候选宿主 + 装配 ExpediteImpactResponse —— 宿主 api/candidate/{id} 待建
  → 4号位 P0-1(a) 影响范围卡片联调
```

| 待办 | 归属 | 状态 |
|---|---|---|
| 建 `api/candidate/{candidateId}` 宿主 + 挂 `expedite-impact` | 2号位 宿主 / 3号位 装配 | ⏳ 待落地排期 |
| 业务保护识别字段 (i)/(ii) | **0号位** | ⏳ 补裁（过渡兜底 FirmHorizonDays 不阻塞）|
| `RunScope.BusinessScope` 载体（真空④）| **0号位** → 3号位 补契约 → 2号位 投影 | ⏳ 待 0号位 |

## 六、与 v1.4 §10A 一致性

- `OrderCanonicalId` 作订单唯一身份，与 §10A.1 EXISTING_ORDER_ADVANCE 的 `orderTargets[].OrderCanonicalId` **同源**（ScopeJsonV2.OrderTargets），前端无须新增身份字段。
- 保护订单（`protectedOrders`）不与「可调整」重叠：EXPEDITE 不能突破执行事实 / 业务保护 / 技术硬约束（Q4-B §四、§五），受保护单单独列示、不列入 `affectedOrders`。

---

## 七、附档

- 定版依据回执：[APS_V1_Q4B_EXPEDITE收口及缺口①②④_2号位回执_v1.0_20260923(1).md](APS_V1_Q4B_EXPEDITE收口及缺口①②④_2号位回执_v1.0_20260923(1).md)
- 契约收口函：[APS_V1_Q4B_EXPEDITE_1号位2号位回执收口_3号位_v1.0_20260923.md](APS_V1_Q4B_EXPEDITE_1号位2号位回执收口_3号位_v1.0_20260923.md)
- 回执拍板：[3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md)
- 0号位 EXPEDITE 裁决：[Q4-B：PriorityMode=EXPEDITE强度定义裁决.md](Q4-B：PriorityMode=EXPEDITE强度定义裁决.md)
- ScopeJsonV2：`LPS.APS.Core/DTOs/Governance/ScopeJsonV2.cs`（OrderTargets 身份键）
- DemandBalance：`LPS.APS.Core/Dto/DemandBalance.cs`（DemandKey 真源）

---

**发送方**：3号位 ｜ **接收方**：4号位（主）+ 2号位（数据派生 / 保护兜底）+ 0号位（补裁项）｜ **日期**：2026-09-23
**定版（v1.0）**：自 v0.9 草案升级——D1/D2 数据分工与归因判定、① 投影层排除均以 2号位 回执确认落地；遗留 0号位 业务保护识别字段 + RunScope.BusinessScope 补裁（跨号位，不阻塞本端点核心）。
**待办**：① 0号位 裁业务保护识别字段 (i)/(ii) + RunScope.BusinessScope ② 2号位 宿主 + Base 比对派生落地 ③ 4号位 对接渲染