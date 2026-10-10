# 回执：Setup 换型治理侧闭环通报 + 三项上游依赖移交 —— 3号位 致 2号位、1号位

> 收件：2号位（数据引擎+编排）、1号位（排程算法）／抄 0号位（PM）
> 依据：2号位《待办清单处置回执答复 v1.1 2026-09-18》＋ 1号位《口径确认与骨架进度回执 v1.0 2026-09-18》＋ 2号位《MES 路由依赖视图缺边取证 2026-09-15》＋ r13376
> 日期：2026-09-19
> 性质：SetupTransitionRule 规则维护治理侧闭环通报 ＋ 三项上游依赖移交（仓储实现 / ODS 视图取证 / 三预算提值）
> 术语约定：英文契约词一律「中文名称（英文）」；文档述引「§符号锚 + 值」，代码引 `文件:行`。

---

## 〇、一句话

SetupTransitionRule 规则维护（CRUD + 审计 + 端点 + 权限码 + 单测）3号位 已闭环；现把三件事移交上游——① 仓储 Dapper 实现（2号位，运行时头号阻塞）② ODS 路由视图取证（2号位，定「视图丢 vs 源缺」）③ 三预算参数提值（1号位，启动 ParameterSetVersion 治理）。

---

## 一、SetupTransitionRule 规则维护治理侧已闭环（通报）

本轮 3号位 已完成并本地验证：

| 交付物 | 内容 | 状态 |
|---|---|---|
| `SetupTransitionRuleService` | CRUD 编排 + §十 唯一键冲突校验（EXACT 键 / DEFAULT 键，仅 IsActive=true）+ 统一审计落库（`AuditLog`，EntityType=SetupTransitionRule，fail-closed） | ✅ |
| `ISetupTransitionRuleService` / `ISetupTransitionRuleRepository` | 服务契约 + 数据访问契约（后者由 2号位 实现） | ✅ 契约冻结 |
| `SetupTransitionRuleController` | List / Get / Create / Update / Delete 五端点，`ApiResponse<T>` 信封 | ✅ |
| 权限码接线 | 读端挂 `RuleView`（aps.rule.view），写端挂 `RuleMaintain`（aps.rule.edit）；审计身份后端取 Claim，拒信请求体 | ✅ |
| 单元测试 `SetupTransitionRuleServiceTests` | 10 个全绿（无冲突 / EXACT 冲突 / DEFAULT 冲突 / 审计不可写 fail-closed / 快照 / 目标不存在等） | ✅ |

Web 构建 **0 错误 0 警告**（其余为既有 CS0618/8618 告警，非本轮引入）。

---

## 二、移交 2号位：`ISetupTransitionRuleRepository` Dapper 实现（运行时头号阻塞）

上表服务/端点已就绪，但 **`ISetupTransitionRuleRepository` 目前无 2号位 Dapper 实现**，运行时无法 resolve，属主链阻塞。契约已冻结，请 2号位 照接口落 Dapper 版本：

- `GetByRuleSetVersionAsync(long ruleSetVersionId)` —— 按规则集版本整版本读取（含 Inactive，冲突校验在内存层自行筛选）；
- `GetByIdAsync(long id)` / `AddAsync`（返回新主键）/ `UpdateAsync` / `DeleteAsync`。

表 `SetupTransitionRule` 实体与 DDL 已落地（2026-09-17 经批准由 3号位 直接建表，DB 红线 #6 豁免），2号位 只需对着表写仓储，无需建表。

---

## 三、移交 2号位：「视图丢 vs 源缺」判定取证（#5 结论 + 取证需求）

承接 2号位《待办清单处置回执答复 v1.1》Item 3/4 的「视图丢 vs 源缺」判定，3号位 已做完静态部分，结论如下：

**静态结论**：仓库内**没有** 3号位 ODS 清洗对象的真实 DDL——`MES_APS_Routing_Dependency_View` / `MES_APS_Routing_Operation_View` 及中间表 `MiddleRoutingData` / `MiddleRoutingDataJ`。`*.sql` 里只有 2号位 的 `ext_` 同义词包装（`SELECT * FROM [MES_Integration].[dbo].[…]`，零处理）。**3号位 ODS 清洗层 DDL 未入库**，故「视图丢 vs 源缺」无法靠仓库静态文件定性。

**决定性证据在 MES_Integration 库**，二分落责：

| 取证结果 | 判定 |
|---|---|
| 源生表有「当前工步→下一工步」行，但 `MiddleRoutingData`/`MiddleRoutingDataJ` 没有 | **视图丢 → 3号位**（中间表/视图生成逻辑丢行，本号位修） |
| 源生表本就缺这道关系 | **源缺 → 开清单给 PM/MES 职能补** |

**取证需求（两样，返回前可开工）**：
1. `MES_APS_Routing_Dependency_View` / `MES_APS_Routing_Operation_View` 及 `MiddleRoutingData` / `MiddleRoutingDataJ` 的真实 DDL（3号位 可自取 MES_Integration 库；或 2号位 顺手贴一下）；
2. 取证 SQL 已由 2号位 备好（`SELECT * FROM dbo.MiddleRoutingData WHERE MES_ID=N'3231940'` 等），请 2号位 配合代跑或授 3号位 读库取数。

属「源缺」的，3号位 出清单给 PM、按台账 K「阻塞 B」记账，不阻塞主链。

---

## 四、移交 1号位：三预算参数提值（启动 ParameterSetVersion 治理）

据 0号位 裁决（《正式回复 v1.0》§二 + §3.4），三预算参数**不由 0号位 冻结具体数值**：有界搜索预算 / 最大邻域尝试次数由 **1号位 性能标定提值、3号位 ParameterSetVersion 治理**；单生产日保护阈值当前删除。500/50 仅作 1号位 测试初值。

请 1号位 完成性能标定后把**有界搜索预算 / 最大邻域尝试次数**的两个初始值回传 3号位，3号位 即接手 ParameterSetVersion 治理槽位（承载 ParameterSetVersion → FrozenStrategySnapshot → 1号位 消费）。数值未到位前，3号位 无法落治理参数，状态记「等 1号位 提值」。

---

## 五、汇总（上下游下一步）

| 项 | 责任 | 3号位 已做 / 待做 | 状态 |
|---|---|---|---|
| SetupTransitionRule 治理（服务+审计+端点+权限码+单测） | 3号位 | 已闭环 | ✅ |
| `ISetupTransitionRuleRepository` Dapper 实现 | 2号位 | 契约已冻结，等实现 | ⏳ 头号阻塞 |
| 「视图丢 vs 源缺」静态判定 | 3号位 | 已出（视图 DDL 未入库、需库取证） | ✅ 待 2号位 供 DDL/取证 |
| 源缺清单给 PM | 3号位 | 待库取证定性后出 | ⏳ 取证中 |
| 三预算参数提值 | 1号位 | 等初始值，即接手治理 | ⏳ 等提值 |
| ScopeJsonV2 十码验收 | 2号位 | 已附 DTO 骨架 + 十码枚举（2026-09-18） | ⏳ 送验中 |

> 3号位 不越位改 1号位 / 2号位 代码；上述移交项均属对方辖区，3号位 仅提契约与取证需求。