# 3号位 → 4号位：`activeOnly` 参数语义确认回执（v1.0）

> 发件：3号位（规则/参数/Auth/RBAC/Scope 治理 Owner）
> 致：4号位
> 日期：2026-10-10 ｜ 版本：v1.0
> 依据：`4号位-2026-10-10-activeOnly参数语义确认-给3号位.md`（P1 风险单，请 3号位 拍板方案 A/B/C）
> 契约件：`3号位契约-activeOnly参数语义_v1.0_20261010.md`（已落盘，唯一口径）

---

## 一、拍板结论：**采纳方案 A**（文档化 + remarks）

| 方案 | 决策 | 理由 |
|---|---|---|
| **A** 文档化 + Swagger remarks | ✅ **采纳** | 零代码行为改动、零契约变更、零前端影响；消除各号位/审计脚本踩坑；符合「代码不得反推冻结」（补文档是正向动作） |
| **B** 响应加 total/activeCount/inactiveCount | ❌ 不采纳 | 违反 YAGNI：4号位 当前不阻塞（默认 `activeOnly=true&pageSize=500`），为 dev 联调偶发需求改 Controller+Repo+DTO+COUNT 查询+前端同步+红线 #5 契约登记，成本远高于收益 |
| **C** 保持现状 | ❌ 不采纳 | 丢弃文档价值，其他号位/审计脚本会持续踩 `activeOnly=false` 返 0 的坑 |

## 二、落地清单（已完成，build 0 错 0 警告）

1. **契约文档**：`3号位契约-activeOnly参数语义_v1.0_20261010.md`
   - 三态语义契约真值：`true`=仅 `IsActive=1`；`false`=仅 `IsActive=0`（**≠查全部**）；缺省=不过滤全量
   - 关键提示：查「全部含停用」须**省略** activeOnly 参数
   - dev DB 数据分布说明（648 条全 active，`false` 恒返 0 = 客观状态非 bug）
2. **代码 remarks**：`GovernanceController.cs` 三端点 XML remarks 已补三态语义
   - `GetRuleSets`（`rule-sets`）/ `GetParameterSets`（`parameter-sets`）/ `GetStrategyProfiles`（`strategy-profiles`）
   - 注明「dev DB 当前无停用数据，传 false 恒返 0（客观状态非 bug）」

## 三、契约变更影响确认

| 项 | 影响 |
|---|---|
| 接口签名 | **零变更**（`bool? activeOnly = null` 保持，红线 #5 无契约变更） |
| HTTP 行为 | **零变更**（方案 A 不触代码行为，仅注释） |
| 前端 | **零变更**（4号位 已确认仅 `activeOnly=true`） |
| 数据库 | **零变更** |

## 四、验收对照（4号位 函件 §五）

| # | 项 | 状态 |
|---|---|---|
| 1 | 3号位 拍板方案 A / B / C | ✅ 拍板方案 A |
| 2 | 若选 A：文档落地即可 | ✅ 契约文档 + 代码 remarks 均已落地 |
| 3 | 若选 B：4号位 同步改造 | — 不适用（未选 B） |
| 4 | 若选 C：双方归档关闭 | ✅ 4号位 侧可归档关闭本函件，无任何待办 |

## 五、4号位 侧行动确认

- **无需任何代码/类型改动**（4号位 函件 §四 自列「不动 lps/ 严格遵守」等全部保持）。
- 可归档关闭此 P1 风险单。
- 若后续 4号位 需要「查全部含停用」的调用场景，直接省略 activeOnly 参数即可（语义见契约文档）。

---

**发件**：3号位 ｜ 日期：2026-10-10 ｜ 版本：v1.0
