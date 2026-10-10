# 3号位 → 4号位 — 回执：TaskSplitRuleConfig 列表分页改造（方案 A 已拍板并落码）

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 来件 | `4号位-2026-10-08-TaskSplitRuleConfig-列表分页改造-给3号位(1).md` |
| 发件 | 3号位（规则参数治理 owner / 后端） |
| 收件 | 4号位（前端 owner） |
| 抄送 | 2号位（仅信息同步） |
| 主题 | **拍板方案 A**；后端已落码（接口/实现/Controller/测试用例）；4号位 可启动前端改造 |
| 优先级 | P2 |

---

## 一、方案裁定：**A**（与 R2 既有 `pageIndex`/`pageSize` + `PageResult<T>` 完全对齐，零新概念）

- B/C 不采纳：B（skip/take 双语义）长期不推荐；C（前端客户端分页）生产上千条首屏拉全量不可行。

## 二、后端落码清单（已编译 0 错误：Application / Web）

| 文件 | 改动 |
|---|---|
| `LPS.APS.Application/Services/ITaskSplitRuleConfigGovernanceService.cs` | `ListAsync` 签名 → `Task<PageResult<TaskSplitRuleConfigDto>>`，增 `int pageIndex = 1, int pageSize = 20`（XML 注释注明 R2 契约） |
| `LPS.APS.Application/Services/TaskSplitRuleConfigGovernanceService.cs` | 实现：`Math.Max(pageIndex,1)` + `Math.Clamp(pageSize,1,200)` + `COUNT` + `OFFSET/FETCH NEXT`，返回 `PageResult`；过滤参数不变 |
| `LPS.APS.Web/Controllers/TaskSplitRuleConfigController.cs` | `List` 增 `[FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20`，透传 service，响应泛型改 `PageResult<TaskSplitRuleConfigDto>`（补 `using LPS.APS.Core.Dto;`） |
| `LPS.APS.Tests/Integration/TaskSplitRuleConfigGovernanceServiceIntegrationTests.cs` | 新增分页用例（pageSize=2 / 9999→200 截断 / 超末页空 items / pageIndex<1 归 1）+ 清理列表字段 |

## 三、验证结果

**契约对齐（4号位 函件 §2.1/2.2/2.3）：**

| 场景 | 预期 | 结果 |
|---|---|---|
| `pageIndex` 非整数 | 400 | ASP.NET 模型绑定原生 400 ✅ |
| `pageSize` > 200 | 200（静默截断 200） | `Math.Clamp` 截断 ✅ |
| `pageIndex` 超末页 | 200（items 空数组，total 真实） | ✅ |
| `pageIndex` < 1 | 归 1 | `Math.Max` ✅ |

**SQL 语义直连 dev 库验证**（真实 SQL Server，只读）：`COUNT=8`；`OFFSET 0 FETCH 2 → 2 行`；`OFFSET 19998 FETCH 2（超末页）→ 0 行`；`FETCH 200 → 8 行`。

**集成测试用例**：已写好（pageSize=2 → `items.length=2/page=1/pageSize=2` 等），**但暂无法经 `dotnet test` 跑通**——见 §四。

## 四、⚠️ 需 4号位 知悉：测试项目被其他号位未提交代码阻断

`LPS.APS.Tests/Unit/` 下 18+ 个**未跟踪**测试文件（`BottleneckModeTests / ExecutionBatchDraftTests / SetupWiringTests` 等，疑为 1号位 排程测试）引用 `TestBatchPolicy.Permissive(...)`，但该助手类**全仓无定义** → 测试项目整体编译失败。分页集成测试用例已写入该文件，待相关号位补上助手后即可跑通；本回执以直连 SQL 验证替代，语义等价。

## 五、4号位 前端配套（函件 §四 可启动）

- `taskSplitRuleConfig.ts`：`TaskSplitRuleConfigListQuery` 增 `pageIndex?/pageSize?`；新增 `PagedTaskSplitRuleConfig` 类型；`list()` 返回 `Promise<PagedTaskSplitRuleConfig>`
- `store`：存 `items/total/page/pageSize`；`load()` 透传 query
- `TaskSplitRuleConfig.vue`：ElPagination；筛选/刷新回 pageIndex=1；KPI 总数用后端 `total`
- 兜底（函件 §末尾）可保留：旧形态 → 单页降级（后端已不再返回旧形态，此兜底为兼容过渡）

> 验证建议（函件 §五 TSRC14）：`pageIndex=1&pageSize=2 → items.length=2 且 total≥items.length`；`pageSize=9999 → pageSize=200`。

---

> 后端已完成，4号位 可开工；验收结果请回执。其它治理端点（StrategyProfile/ParameterSet/RuleSet）不改动。
