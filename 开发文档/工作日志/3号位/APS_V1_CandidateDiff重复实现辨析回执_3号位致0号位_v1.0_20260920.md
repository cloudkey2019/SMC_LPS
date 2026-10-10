# 回执：Candidate Diff 重复实现辨析（3号位 / 5号位 是否有冲突）—— 3号位 致 0号位

> 收件：0号位（PM）／抄 1号位、2号位、5号位、4号位
> 依据：0号位 口头问询「Candidate Diff 是否已重复实现，3号位 / 5号位 代码是否有冲突」
> 日期：2026-09-20
> 性质：定责辨析（无冲突结论 + 重复定位 + 溯源 Q6 + 收口建议）
> 术语约定：英文契约词一律「中文名称（英文）」；文档述引「§符号锚 + 值」，代码引 `文件:行`。

---

## 〇、结论

**没有冲突，但确实存在「同一套差集算法被维护两份」的重复实现。**

- 编译、运行时、数据层：无冲突（两处均只读 `[Task]`/`[Order]` 落库事实，不互写、不互调）。
- 功能层：无冲突（各接一个页面，入参/出参/用途不同，互不取代）。
- 唯一问题：底层 Task 主键差集 + 时间平移/新增延期判定 SQL 是同一套拷贝，存在**口径漂移**隐患。

---

## 一、两处分别是干什么的

| 维度 | 3号位 `ScheduleQueryService.GetCandidateComparisonAsync` | 5号位 `OverviewQueryRepository.GetCandidateSummaryAsync` |
|---|---|---|
| 页面 | 候选「详情对比」页（H1） | 候选「列表」页（Overview 面板） |
| 输入 | 一对版本：candidate + base 两个 Id | 全域所有 CANDIDATE 版本，逐个遍历 |
| 输出 | 单份完整报告 `CandidateComparisonDto`（变更汇总 + 影响汇总 + 双方版本概要） | 列表 N 行，每行 `CandidateBriefDto` 摘要（AvgDeltaHours + 各计数） |
| 端点 | `GovernanceController` `compare-with/{baseId}` | `OverviewController` |
| 附加 | 带 F-G4 业务范围校验、404/400 身份校验 | 无范围校验、纯聚合 |

代码定位：
- 3号位：`LPS.APS.Application/Services/Query/ScheduleQueryService.cs:246`
- 5号位：`LPS.APS.BusinessRules/Repositories/OverviewQueryRepository.cs:178`（`ComputeCandidateComparisonAsync`）

---

## 二、重复在哪（为什么说"重复实现"）

5号位 `OverviewQueryRepository.cs:177` 注释**自陈**：「候选 vs 基础 差集比较聚合（**从 ScheduleQueryService 差集逻辑移植**，5号位只读 [Task]/[Order] 落库事实，不重算业务）」。

即 5号位 落地列表摘要时，把 3号位 详情对比里那段差集 SQL 逐份拷贝了一份。两处：

- 差集主键相同：`OrderId + MaterialId + OperationSeq + OperationCode + RouteCode + PathId`；
- `added / removed / timeShifted / resourceChanged / impactedOrders / newDelays / 平均完成时间` 判定口径一致。

另有一个**契约漂移信号**：同一概念两处字段名不同——3号位 `CrossDomainBlocked`(=0, int)、5号位 `CrossDomainBlockedCount`(null)。

---

## 三、溯源（为什么会这样）

`LPS.APS.BusinessRules` 随卡点 `给5号位代码接管迁移清单_v1.0_20260902.md:83-85` 迁移时，`CandidateComparisonDto` 被列入随迁清单，但批注「候选计划治理留 3号位，是否随迁**待确认**（开放问题 Q6）」。结果形成中间态：列表查询随迁到 5号位（自带一份移植差集 SQL），详情对比仍留 3号位——两边各持一份同一口径。

---

## 四、风险与收口建议

**风险**：非即时冲突，而是将来改 Task 主键或差集语义时只改一处，详情页与列表页的「新增/移除/新增延期」数字会对不上。

**收口二选一（请在 0号位 裁决）**：

1. **详情对比也随迁 5号位**，3号位 删 `GetCandidateComparisonAsync`（及 `CandidateComparisonDto` 详情版），列表/详情统一由 5号位 出；
2. **列表改走 3号位 详情接口**，5号位 删移植副本，单一实现。

任选其一后，`CrossDomainBlocked` / `CrossDomainBlockedCount` 字段名一并收敛。

---

## 五、汇总

| 问 | 答 |
|---|---|
| 是否重复实现 | 是（底层差集算法两份拷贝） |
| 编译/数据冲突 | 无 |
| 功能冲突 | 无（详情页 vs 列表页，各司其职） |
| 根因 | 迁移清单 Q6 悬置 |
| 待裁 | 收口方向（随迁 or 收敛单实现） |

> 3号位 不越位改 5号位 代码；本回执仅辨析 + 提收口建议，裁决由 0号位 出。