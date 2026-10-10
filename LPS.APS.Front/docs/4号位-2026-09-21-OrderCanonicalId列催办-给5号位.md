# 4 号位 → 5 号位：§10A.1「已有订单提前」依赖的 `OrderCanonicalId` 列催办（P1）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（业务查询端点 Owner）
> **抄送**：0 号位（治理留档）、3 号位（同链路 §10A 备查）
> **日期**：2026-09-21
> **性质**：**单点催办**——仅 1 项；§10A.1 唯一剩余上游依赖
> **关联**：《4号位-2026-09-21-合并催办-请求事项清单-给5号位.md》§〇 R1-R4 之外的 §10A.1 落地项（09-21 函未单列，因前端自报「可手填兜底」；今 §10A 上游 B1/B2/B3 已随 3号位 r13416 全补，前端三绿 + J段 13/13 通过——剩余 B4 为唯一项，故发本函）

---

## 〇、速答清单（请照此格式回执）

```
R1 OrderCanonicalId 列就绪时间 = ____（择期 OR 跟 R1 用户输出 DTO 契约 同步）
```

---

## 一、问题陈述（5 分钟读完）

§10A.1「已有订单提前」要求前端为每条目标订单传 `orderTargets[].orderCanonicalId`（**规范化订单 Id**，与 `orderId`=[Order].Id 不可混用——后者为当前 PlanVersion 内技术主键，前者跨版本稳定）。当前 5 号位：

- **`OrderListItemDto`**（`lps/LPS.APS.Core/Dto/OrderQueryDtos.cs:6-30`）：**未含 `OrderCanonicalId` 字段**
- **`OrderQueryRepository.QueryOrdersAsync`**（`lps/LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs:36-73`）：**SQL 未 select `o.OrderCanonicalId`**（仅 `o.Id`、`o.OrderNo` 等 20 列）

**对前端的影响**：订单列表不返回 `OrderCanonicalId` → §10A.1 行内只能手填（前端**已按计划落手填兜底**：ElInput 数字校验 + 必填 + 预填 `orderNo` 作 placeholder 提示 + ElAlert 说明「OrderCanonicalId 为规范化订单 ID，请填真实值」）。手填不友好、不可批量、错误率高，影响 §10A.1 实际使用。

**业务侧影响**：规范化订单 Id 是冻结文档 §十A 10A.1 入口的契约字段（**单一真相约束 §4.6**：orderTargets 按 OrderCanonicalId 去重），缺失即 10A.1 范围不可用；后端 `ScopeJsonV2Validator.ValidateOrderTargetsUnique` 已按 OrderCanonicalId 去重，**前端的「真实数据源」缺口 B4 是 10A.1 端到端验收的最后一项**。

---

## 二、改动请求（前端建议；具体由 5 号位 决定）

### 2.1 DTO（`OrderListItemDto.cs:6-30`）

```csharp
public sealed class OrderListItemDto
{
    public long Id { get; init; }
    // ... 现有 20 字段 ...
    public string? OrderCanonicalId { get; init; }  // 新增（可空，旧数据兼容）
}
```

> 类型用 `long`（与 `[Order_Canonical].Id` 一致；非字符串 OrderNo）；可空以兼容 Order_Canonical 表尚未建立的过渡期。

### 2.2 SQL（`OrderQueryRepository.cs:36-73`，单方法单点）

```sql
SELECT
    o.Id,
    o.PlanVersionId,
    o.OrderNo,
    -- ... 现有 18 字段 ...
    oc.Id AS OrderCanonicalId,  -- 新增（LEFT JOIN Order_Canonical oc ON oc.OrderId = o.Id）
    -- ... 余下 1 字段 ...
FROM [Order] o
LEFT JOIN Factory f ON f.Id = o.FactoryId
LEFT JOIN ProductFamily pf ON pf.Id = o.ProductFamilyId
LEFT JOIN Order_Canonical oc ON oc.OrderId = o.Id  -- 新增
WHERE ...
```

> 注：`Order_Canonical` 表是否已存在 / `OrderId` 关联键如何定义由 5 号位 核实；前端**不**预设实体结构，请按 5 号位 实际表设计为准。

### 2.3 同 SQL 的其他查询方法

`OrderQueryRepository.cs:104-` / `127-` / `141-` / `167-` / `208-` 共 4 处 SELECT 是否同步改，由 5 号位 决定（前端仅用列表 `QueryOrdersAsync`）。

---

## 三、前端准备状态（已就绪，列端即接）

- `frontNew/src/api/aps-v1/types/order.ts:44-66 OrderBasicInfo` 已预留 `orderCanonicalId?: number | null` 字段（前端自加，对应后端字段名 `orderCanonicalId` camelCase JSON）
- `Order.vue` 表头「已有订单提前（N）」按钮 + 行内选择 + `OrderAdvanceDialog` 接线已全部就绪
- 字段就绪后**前端当日**接：`OrderAdvanceDialog` 行内 `orderCanonicalId` 输入框改为只读展示 + 提交时自动带入；手填兜底保留为兜底（若 OrderCanonicalId 为 null 即退化手填路径）
- 验证：`scripts/verify-acceptance.mjs` 后续可加 J 段静态断言（`OrderBasicInfo` 含 `orderCanonicalId` 字段）——本函**不**包含具体验收位，由 5 号位 端点就绪后 4 号位 接续

---

## 四、上游同链路项（备查）

- 3 号位 §10A 三断点 B1+B2+B3 **已随 r13416 全补**（09-21）：CreateCandidateRunRequest.Scope + RunLifecycleService.createSpec.Scope + ScheduleRunRepository.CreateCandidateRunAsync INSERT [ScopeJson] 三处落地；J段 13/13 通过 + 三绿复验
- 4 号位 侧 §10A 五业务入口全部就绪：**5 入口 UI + Dialog + ScopeJsonV2 校验 + triggerBusinessEntry 统一出口 + Candidate 页 source 标签**（`gantt-adjust`/`equipment-failure`/`resource-calendar`/`domain-manual`/`order-advance`）

---

## 五、4 号位 能力边界附注（不影响 R1，仅同步）

DhxGantt 组件**不支持多 Task 拖拽**（`DhxGanttDragPayload.taskId` 单值）→ §10A.2「一次拖拽/选择多个 Task」中「多选」由 Dialog 表格承担；拖拽仅作单条预填。3 号位 已抄收，无异议。
