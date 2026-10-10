# 4号位 → 3号位 — TaskSplitRuleConfig 列表分页改造（GET 加 pageIndex / pageSize）

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 发件 | 4号位（前端 owner） |
| 收件 | 3号位（规则参数治理 owner / 后端） |
| 抄送 | 2号位（DDL 红线 #6 owner，仅信息同步） |
| 主题 | TaskSplitRuleConfig 列表加标准分页（pageIndex / pageSize），响应改用 `PageResult<T>` 容器 |
| 优先级 | **P2**（dev 库已 8 条，生产 1000+；前端 ElTable 单页全量会导致首屏 / 切换卡顿） |
| 关联 | [4号位-2026-10-07-TaskSplitRuleConfig-Dapper-Dept-DBNull-500-给3号位.md](4号位-2026-10-07-TaskSplitRuleConfig-Dapper-Dept-DBNull-500-给3号位.md) · [3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md](3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md) · [RbacController.cs:34-44](lps/LPS.APS.Web/Controllers/RbacController.cs#L34-L44) · [PageResult.cs:1-30](lps/LPS.APS.Core/Dto/PageResult.cs#L1-L30) |

---

## 一、需求

`TaskSplitRuleConfig` 列表当前 `GET /api/governance/task-split-rule-configs` 全量返回。当前 dev 库 8 条、预生产几千条，**前端 ElTable 单页全量 + 部门 lookup 200 条反查**在生产规模下会出现：

- 首屏渲染卡顿（8 列 × 数千行 DOM 节点）
- 翻页 / 筛选响应迟滞
- 服务端序列化 + Dapper 反射开销放大

**结论**：参考 R2 系列治理接口的 `pageIndex` / `pageSize` + `PageResult<T>` 形态，**把列表接入标准分页**。

---

## 二、契约（与 Rbac / Audit / ManualEta 对齐）

### 2.1 请求

```
GET /api/governance/task-split-rule-configs
  ?materialId={int?}
  &productionDepartmentId={int?}
  &isActive={bool?}
  &pageIndex={int=1}        ← 新增
  &pageSize={int=20}        ← 新增
```

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| materialId | int? | — | 物料过滤（与现有契约一致） |
| productionDepartmentId | int? | — | 部门过滤（与现有契约一致） |
| isActive | bool? | — | 启用状态过滤（与现有契约一致） |
| **pageIndex** | int | **1** | 1 基页码，`< 1` 强制归 1 |
| **pageSize** | int | **20** | 每页条数，硬上限 **200**（与 RbacController 保持一致），超限截断到 200 |

> 与 [RbacController.cs:34-44](lps/LPS.APS.Web/Controllers/RbacController.cs#L34-L44) 同模式，零新概念。

### 2.2 响应（改 `PageResult<TaskSplitRuleConfigDto>`）

```json
{
  "code": 200,
  "message": "success",
  "data": {
    "items": [
      {
        "id": 1,
        "materialId": 5346847,
        "productionDepartmentId": 1114,
        "minExecutionBatchQty": 1,
        "maxExecutionBatchQty": 2000,
        "preferredBatchQty": 3000,
        "allowSplit": true,
        "allowMerge": true,
        "maxOptimizationSplitCount": 3,
        "maxBatchCandidates": 5,
        "bottleneckSplitStrategy": "PREFER_SPLIT",
        "nonBottleneckStrategy": "PREFER_LARGE_BATCH",
        "isActive": true,
        "effectiveFrom": "2026-10-08T00:00:00Z",
        "effectiveTo": null,
        "createdAt": "2026-10-08T10:00:00Z",
        "updatedAt": "2026-10-08T10:00:00Z"
      }
    ],
    "total": 1024,
    "page": 1,
    "pageSize": 20
  },
  "traceId": "...",
  "timestamp": "..."
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `data.items` | `TaskSplitRuleConfigDto[]` | 当前页数据（**改名**：`ListAsync` 返回的 `IReadOnlyList<...>` 改为 `PageResult<...>` 的 `Items`） |
| `data.total` | `int` | 满足筛选条件的总条数（COUNT） |
| `data.page` | `int` | 实际生效的页码（与请求 pageIndex 一致，`< 1` 已归 1） |
| `data.pageSize` | `int` | 实际生效的每页条数（`> 200` 已截断到 200） |

### 2.3 错误码

| 场景 | 状态 |
|---|---|
| pageIndex / pageSize 非整数 | 400 |
| pageSize > 200 | 200（静默截断到 200；与 Rbac / Audit 一致，**不报错**） |
| pageIndex 超过最后一页 | 200（`items` 空数组，`total` 仍为真实总数） |

---

## 三、后端改造范围

| 文件 | 改动 |
|---|---|
| `ITaskSplitRuleConfigGovernanceService.ListAsync` 签名 | 增 `int pageIndex = 1, int pageSize = 20`，返回类型 `Task<PageResult<TaskSplitRuleConfigDto>>` |
| `TaskSplitRuleConfigGovernanceService.ListAsync` 实现 | 复用 `RbacManagementService.QueryPagedAsync` (RbacManagementService.cs:757-789) 同款 `COUNT + OFFSET/FETCH NEXT` 模板；参数化 `MaterialId/ProductionDepartmentId/IsActive` 不变；新增 `PageIndex`/`PageSize` 入参；`Math.Max(pageIndex, 1)` + `Math.Clamp(pageSize, 1, 200)` |
| `TaskSplitRuleConfigController.List` | 增 `[FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20`，透传给 service；如已有 `ResolvePaging` helper 直接复用 |
| `TaskSplitRuleConfigListQuery`（DTO） | 增 `pageIndex?: number; pageSize?: number` |
| `TaskSplitRuleConfigIntegrationTests`（如已有 list 用例） | 补 1 个用例：pageSize=2 → 验证 `total/items.length=2/page=1/pageSize=2` |

> 复用 R2 既有的 `QueryPagedAsync<T>(...)`（RbacManagementService.cs:757-789）能避免重复实现，**最省事**；如要独立拆分也可以，但函数体请直接拷贝现有 OFFSET/FETCH 模板。

---

## 四、4号位 前端配套改造（函件后立即开工）

| 文件 | 改动 |
|---|---|
| `frontNew/src/api/aps-v1/types/taskSplitRuleConfig.ts` | `TaskSplitRuleConfigListQuery` 增 `pageIndex?: number; pageSize?: number`；新增 `PagedTaskSplitRuleConfig = { items: TaskSplitRuleConfigDto[]; total: number; page: number; pageSize: number }` |
| `frontNew/src/api/aps-v1/taskSplitRuleConfig.ts` | `list()` 返回类型改为 `Promise<PagedTaskSplitRuleConfig>`；请求 URL 追加 `pageIndex`、`pageSize`；保留现有 `materialId/productionDepartmentId/isActive` 透传 |
| `frontNew/src/store/modules/aps/taskSplitRuleConfig.ts` | `list` 改存 `items` + `total` + `page` + `pageSize`；新增 `pageIndex` / `pageSize` 状态（默认 1 / 20）；`load()` 接受 `query` 透传 |
| `frontNew/src/views/Aps/TaskSplitRuleConfig.vue` | 表格上方加 ElPagination；筛选 / 刷新 → 回到 pageIndex=1；KPI 总数改用后端 `total`（去掉前端 `list.length`） |

> 前端不会抢跑，必须等本函件回执 + 后端真的改完才上线。回执里如后端保持老返回（只 `data: TaskSplitRuleConfigDto[]`），4号位 端会**做兜底**（旧形态 → 走 `paged` 判断 + 降级渲染为单页），不出现 500。

---

## 五、验证（4号位 复跑验收）

| 场景 | 预期 |
|---|---|
| `pnpm ts:check` | 绿 |
| `pnpm lint:eslint` | 绿 |
| `GROUP=t-tsrc node scripts/verify-acceptance.mjs` | TSRC01-13b 全绿；新增 TSRC14 动态：列表 GET + pageIndex=1&pageSize=2 → `items.length=2` 且 `total ≥ items.length`；`pageSize=9999` → `pageSize=200`（截断） |
| 浏览器走查 | 1) 翻页、2) 切每页大小、3) 切回 1 页、4) 筛选后回首页、5) 大量行（>500）滚动顺畅 |

---

## 六、不在请求范围

- 后端代码（4号位 硬约束：lps/ 只读）
- 冻结文档（待 3号位 落码后由 2号位 对齐 v5.1.x 文档）
- 其它治理端点改造（本函件**仅 TaskSplitRuleConfig**；`StrategyProfile` / `ParameterSet` / `RuleSet` 仍走 R2 既有分页）

---

## 七、替代方案

- **方案 A（推荐）**：与 R2 既有 `pageIndex` / `pageSize` + `PageResult<T>` 完全对齐，零新概念
- **方案 B**：使用 `skip` / `take`（与 OrderQueryController 一致），但前端要兼容两套语义，长期不推荐
- **方案 C**：仅前端做客户端分页（dev 库 8 条可行，生产上千条会导致首屏拉全量，仍需后端分页）

请 3号位 拍板方案 A；4号位 收到回执后启动前端改造。

---

> 按 4号位 → 后端中转沟通方式，本函件仅发 3号位 + 抄送 2号位（仅信息同步，不请 2号位 决策）。
