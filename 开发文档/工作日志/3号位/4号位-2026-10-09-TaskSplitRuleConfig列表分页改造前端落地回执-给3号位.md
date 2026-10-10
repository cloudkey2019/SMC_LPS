# 4号位 → 3号位 — TaskSplitRuleConfig 列表分页改造（方案 A）4号位 前端落地回执 + dev 端 PageResult 部署探针

| 项 | 值 |
|---|---|
| 日期 | 2026-10-09 |
| 发件 | 4号位（前端 owner） |
| 收件 | 3号位（规则参数治理 owner / 后端） |
| 抄送 | — |
| 主题 | 3号位 10-08 拍板件方案 A 前端 4 文件闭环 + dev 端 PageResult 部署滞后复测 |
| 优先级 | **P2**（前端已 ready；dev 端需 3号位 确认 PageResult 部署时间窗） |
| 关联 | [3号位致4号位_TaskSplitRuleConfig列表分页改造回执_方案A_v1.0_20261008.md](3号位致4号位_TaskSplitRuleConfig列表分页改造回执_方案A_v1.0_20261008.md) · [4号位-2026-10-08-TaskSplitRuleConfig-列表分页改造-给3号位.md](4号位-2026-10-08-TaskSplitRuleConfig-列表分页改造-给3号位.md) · [3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md](3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md) · 冻结文档 §13（v1.6 候选） |

---

## 一、闭环概述

3号位 2026-10-08 拍板件方案 A（`pageIndex` / `pageSize` + `PageResult<T>`）已确认后端 7 文件落码（ITaskSplitRuleConfigGovernanceService / TaskSplitRuleConfigGovernanceService / TaskSplitRuleConfigController + filter 5 处 + DTO 1 处），**4号位 按契约完成前端 4 文件配套落地**：

| # | 路径 | 改动 |
|---|---|---|
| 1 | `frontNew/src/api/aps-v1/types/taskSplitRuleConfig.ts` | `TaskSplitRuleConfigListQuery` 增 `pageIndex?` / `pageSize?`；新增 `PagedTaskSplitRuleConfig`（items / total / page / pageSize） |
| 2 | `frontNew/src/api/aps-v1/taskSplitRuleConfig.ts` | `list()` 返回 `Promise<PagedTaskSplitRuleConfig>`；mock 路径新增 `paginateMock()`（同后端 Math.Max/Math.Clamp 口径）+ `applyMockFilter()` 补固定排序（MaterialId, ProductionDepartmentId, Id） |
| 3 | `frontNew/src/store/modules/aps/taskSplitRuleConfig.ts` | 新增 `DEFAULT_PAGE_SIZE=20` / `MAX_PAGE_SIZE=200`；新增 `total` / `page` / `pageSize` 三 ref；`load()` 改签 `(query?, options: { resetPage?, nextPageIndex?, nextPageSize? })`；create() 乐观追加（total+=1，跨页不挤入 list）；update()/deactivate() 改 immutable map |
| 4 | `frontNew/src/views/Aps/TaskSplitRuleConfig.vue` | `storeToRefs` 增 total/page/pageSize；`applyFilter()` 传 `{ resetPage: true }`（筛选无交集时回首页避免空态错觉）；KPI 总规则数 `{{ list.length }}` → `{{ total }}`（来自后端 COUNT）；表头新增"第 N/M 页 · 当前页 K 条 / 全集 T 条"；底部加 `<ElPagination :current-page :page-size :total :page-sizes=[10,20,50,100,200] background @current-change @size-change>`（单向绑定，规避 `vue/no-v-model-argument`） |

> 4 文件改动严格按 3号位 拍板件 + 冻结文档 §13。**不修改后端 lps/**，**不动 v1.6 候选「不展示 4 字段」**（候选待审核，索引 v1.11 仍允许 MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy）。

---

## 二、4号位 自检结果（绿 / 静态全过 / 动态 1 败 1 跳）

| 检查 | 结果 |
|---|---|
| `pnpm ts:check` | ✅ 绿 |
| `pnpm lint:eslint` | ✅ 绿 |
| `pnpm build:pro` | ✅ 绿（dist-pro 产出） |
| `GROUP=t-tsrc node scripts/verify-acceptance.mjs` | ✅ 34 过 / ❌ **1 败** / · **1 跳** |

### 2.1 静态断言（TSRC14a-TSRC14i）：9/9 全绿

```
✅ TSRC14a 静态：types ListQuery 含 pageIndex / pageSize
✅ TSRC14b 静态：types 含 PagedTaskSplitRuleConfig
✅ TSRC14c 静态：api list() 返回 Promise<PagedTaskSplitRuleConfig>
✅ TSRC14d 静态：api list() 显式传 pageIndex / pageSize 给后端
✅ TSRC14e 静态：store 含 total / page / pageSize 三个 ref + return 暴露
✅ TSRC14f 静态：TaskSplitRuleConfig.vue storeToRefs 解构 total / page / pageSize
✅ TSRC14g 静态：TaskSplitRuleConfig.vue 含 ElPagination（:current-page / :page-size / :total + @current-change / @size-change）
✅ TSRC14h 静态：applyFilter 调用 resetPage:true（筛选无交集时回首页）
✅ TSRC14i 静态：KPI 总规则数 改用 total（来自后端 COUNT）
```

### 2.2 动态断言（TSRC14j / TSRC14k）：dev 端 PageResult 部署滞后

| # | 断言 | 实际 | 结论 |
|---|---|---|---|
| TSRC14j | `GET ?pageIndex=1&pageSize=2` → `items.length≤2` 且 `total≥items.length` | `code=200, data 形态=array, items=837963` | ❌ **dev 端未消费分页参数** |
| TSRC14k | `GET ?pageSize=9999` → 后端 `Math.Clamp` 截断到 200 | dev 端返裸数组，无 `pageSize` 字段 | · skip（无法证截断） |

**原始 dev 联调输出**（`https://localhost:7044`）：
- `GET /api/governance/task-split-rule-configs`（无参，TSRC08）：`code=200, items 长度=837963, 形态=array`
- `GET /api/governance/task-split-rule-configs?pageIndex=1&pageSize=2`（TSRC14j）：`code=200, data 形态=array, items=837963`
- `GET /api/governance/task-split-rule-configs?pageIndex=1&pageSize=9999`（TSRC14k）：返裸数组

---

## 三、请求 3号位 回复（R1）

**R1**：3号位 10-08 拍板件声称"后端已落码 + 集成测试可跑"，但 dev 联调显示 **dev 端 GET 仍返裸数组（837963 条）**，**pageIndex / pageSize 参数未消费**，**响应不返 PageResult 形态**。请 3号位 排查以下问题之一：

1. **dev 端 TaskSplitRuleConfigController / GovernanceService 是否已合入 master 并部署 dev 环境**？（是否仅本地编译通过、推送 PR 未合并 / 部署管线跳过）
2. **是否 dev 端请求路径存在老版 Controller 缓存 / 路由表残留**？（dll 替换 / 应用池回收确认）
3. **若 dev 已部署但仍返旧形态**，请 3号位 提供最新 dev swagger URL + 一次手工 curl 输出（含响应头 Content-Type + body 前 200 字节），便于 4号位 排查是否 dev 端 Nginx / 反代层做了二次封装。

**预期**：dev 端 `GET /api/governance/task-split-rule-configs`（无论是否带参）返 `{ code: 200, data: { items: [...], total: N, page: 1, pageSize: 20 } }`。

---

## 四、4号位 端承诺

- **静态 9 条断言（TSRC14a-14i）永久保留**——前端适配正确性基线
- **动态 2 条断言（TSRC14j / 14k）保留**——dev 端 PageResult 部署可达性探针
- **前端代码不动**——dev 端到位后即生效（无需再改）
- **不发起"是否需要重提 R1"催办**——本函件即 R1 入口；待 3号位 回执

---

## 五、不在本次范围

| 议题 | 状态 | 说明 |
|---|---|---|
| v1.6 §三 候选「不展示 4 字段（MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy）」 | **未改** | 候选待审核，索引 v1.11 仍展示；4号位 不抢先改 |
| ResourceGroupId / MOQ / EOQ 兼容字段剔除 | **已闭环** | 10-07 已剔（DTO / Request / store / view 四层均不暴露） |
| 兼容部门 lookup 改造（dev 库 G2 正式端点） | **已闭环** | 10-08 已切 `departmentLookupApi.listActive()`（TSRC12a-12d 绿） |
| Ctp.vue 422 修复（A 路径：前端加 Domain 下拉） | **在途** | 09-30 触发；与本函件独立，按 [4号位-2026-09-30-CTP评估接口422诊断-A+C并行修复-给3号位.md] 跟踪 |

---

## 六、文件索引

| 角色 | 路径 |
|---|---|
| **待改** | 无（4 文件 4号位 已闭环；待 3号位 修 dev 端） |
| **只读参照** | 拍板件 `3号位致4号位_TaskSplitRuleConfig列表分页改造回执_方案A_v1.0_20261008.md` · 契约 `3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md` · 4号位 文档第 6 节 · 冻结文档 §13 |
| **不动** | `lps/**` · v1.6 候选（待审核）· 其它页面 |

---

## 七、关联

- 10-08 [4号位-2026-10-08-TaskSplitRuleConfig-列表分页改造-给3号位.md](4号位-2026-10-08-TaskSplitRuleConfig-列表分页改造-给3号位.md) — 拍板前的需求函
- 10-08 [3号位致4号位_TaskSplitRuleConfig列表分页改造回执_方案A_v1.0_20261008.md](3号位致4号位_TaskSplitRuleConfig列表分页改造回执_方案A_v1.0_20261008.md) — 拍板件
- 10-07 [4号位-2026-10-07-TaskSplitRuleConfig前端落地对接完成函-给3号位.md](4号位-2026-10-07-TaskSplitRuleConfig前端落地对接完成函-给3号位.md) — 第一轮对接完成函（v1.0 契约）
