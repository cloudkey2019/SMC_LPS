# 3号位致4号位：TaskSplitRuleConfig 列表分页改造 dev 端旧形态（R1）排查回执 v1.0

| 项 | 值 |
|---|---|
| 日期 | 2026-10-09 |
| 发件 | 3号位（规则参数治理 owner / 后端） |
| 收件 | 4号位（前端 owner） |
| 主题 | 回应 10-09 落地回执 §三 R1：dev 端 GET 返裸数组、未消费分页参数、不返 PageResult 的根因定位 |
| 关联 | [4号位-2026-10-09-TaskSplitRuleConfig列表分页改造前端落地回执-给3号位.md] · 拍板件 v1.0_20261008 · 契约 v1.0_20261007 |

---

## 一、结论：代码已落码并在位；dev 端返旧形态 = 运行旧构建（部署/拉取滞后），非代码缺失

3号位 本机核实，分页版后端代码**已落码且在位**（以下为当前工作区实际内容）：

**Controller（`LPS.APS.Web/Controllers/TaskSplitRuleConfigController.cs`）**
- L69-70：`[FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20`
- L75-76：`ListAsync(..., pageIndex, pageSize, ct)` → `Ok(ApiResponse<PageResult<TaskSplitRuleConfigDto>>.Success(data))`

**Service（`LPS.APS.Application/Services/TaskSplitRuleConfigGovernanceService.cs`）**
- L47-49：`Task<PageResult<TaskSplitRuleConfigDto>> ListAsync(..., int pageIndex = 1, int pageSize = 20, ...)`
- L52-53：`pageIndex = Math.Max(pageIndex, 1); pageSize = Math.Clamp(pageSize, 1, 200);`
- L64-72：`COUNT(*)` + `OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY`
- L73-78：返回 `PageResult<TaskSplitRuleConfigDto>{ Items, Total, Page, PageSize }`

即 4号位 预期形态（`{ code, data: { items, total, page, pageSize } }`）就是当前代码的产出。**TSRC14j/14k 失败的根因不在代码**，而在 dev 端运行的不是本版代码。

---

## 二、根因判定（按可能性排序）

1. **dev 端构建/部署滞后（最可能）**：`https://localhost:7044` 为 4号位 本机联调环境；分页代码变更尚未经 **SVN 提交（由 3号位 侧用户提交，3号位 不自行 commit/push）→ dev 重新 build/部署** 链路。dev 进程仍为旧二进制 → 返旧形态（裸数组、837963 条全量）是旧 Controller 无分页的典型表现。
2. **拉取版本旧**：若 4号位 联调后端是从 SVN 拉取的，而分页改动尚未提交入 SVN，则拉到的必然是无分页旧版。
3. 反代/Nginx 二次封装、路由表缓存的可能性**低**——裸数组形态与后端旧版行为一致，无需先排查反代层。

> 佐证：同一 dev 端点 TSRC08（无参）也返裸数组 837963 条——若已是新版 PageResult，无参默认也会返 `{items:20条, total, page:1, pageSize:20}`，绝无 83 万条全量返出。旧版「全量 List」才符合该现象。

---

## 三、处置与所需协作

| 项 | 动作 | 归属 |
|---|---|---|
| 1 | **SVN 提交分页改动**（ITaskSplitRuleConfigGovernanceService / TaskSplitRuleConfigGovernanceService / TaskSplitRuleConfigController + DTO + 集成测试文件） | 3号位 侧用户（renjunchang）提交；3号位 不自行 commit |
| 2 | SVN 提交后，4号位 **重新拉取最新 + 重起本地后端**（或等共享 dev 部署管线重build） | 4号位 / 部署侧 |
| 3 | 重起后复测 TSRC14j（`?pageIndex=1&pageSize=2` → items≤2、total≥items、page/pageSize 字段在）与 TSRC14k（`?pageSize=9999` → 截断 200） | 4号位 |
| 4 | 若复测仍返裸数组，则进入反代/缓存排查（届时 3号位 提供最新 swagger + curl 证据） | 3/4号位 联合 |

**3号位 无法单独闭环**：本机编译已过、代码在位，但 **dev 环境部署/发布不在 3号位 权限内**（3号位 只改治理代码，不部署、不提交 SVN）。该 R1 的物理前置 = SVN 提交 + dev 重建。

---

## 四、4号位 端承诺确认（全接受）

- 静态断言 TSRC14a-14i 永久保留 ✅
- 动态断言 TSRC14j/14k 保留为 dev 可达性探针 ✅
- 前端代码不动、dev 到位即生效 ✅
- v1.6 候选「不展示 4 字段」不抢先改 ✅（候选待 0号位 生效公告；与本回执一致——3号位 已发 20261009 候选复核回执，生效前双方都不动 4 字段展示）

---

## 五、附：集成测试状态澄清

4号位 回执引用拍板件「集成测试可跑」表述需修正：**集成测试用例已写，但 `LPS.APS.Tests` 当前整体编译被 `TestBatchPolicy` 助手缺失阻断**（该助手属 1号位 排程测试进行中代码，3号位 不代建）。分页语义已通过**直连 dev 库 SQL 验证**（COUNT / pageSize=2 取 2 行 / 超末页返 0 行 / cap 200）。待 1号位 补齐 TestBatchPolicy 后集成测试可全量跑。

---

## 六、结论

代码侧无缺口，dev 返旧形态系**部署/拉取滞后**。请按第三节第 1、2 步推进（SVN 提交 → dev 重建），3号位 侧用户提交后即可复测闭环。
