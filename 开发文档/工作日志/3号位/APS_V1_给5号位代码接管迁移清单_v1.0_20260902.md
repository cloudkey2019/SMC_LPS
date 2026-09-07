# APS V1 给5号位的代码接管迁移清单（G4 / G7 / G8 / Schedule 普通查询）

**版本**：v1.0
**日期**：2026-09-02
**交接方**：3号位（本清单由 3号位 生成）
**接管方**：5号位（业务接口接入层）
**性质**：3号位 现行代码逐项交接清单 + 5号位 接管侧新建清单 + 迁移步骤 + 验收标准。**本清单只列交接对象与契约，不代 5号位 设计实现**（实现归 5号位，跨号位接口细节按下方"开放问题"三方确认）。

**上位依据（按权威层级）**：
1. 《APS_V1_20260901_职责前端接口与权限归属调整正式裁决_v1.0.md》§十（G4/G7/G8/Schedule 逐步移交 5号位 业务接口接入层；保持 URL/DTO 兼容；改契约先改集成接口设计；3号位 不再新增同类查询；5号位 读 2号位 事实不自行重算；不新增中转表）
2. 《APS_集成接口设计_v1.28_20260901职责与前端接口冻结对齐版.md》§10（原状态 / v1.28 正式归属 / 迁移原则）
3. 《APS_V1_5号位复杂业务事实ODS与业务接口接入开发实施包_v1.1_20260901_治理修正版.md》§十九（G4/G7/G8/Schedule 正式迁移 5号位）与验收 F39~F42
4. 《APS_V1_3号位规则参数认证权限与运行生命周期开发实施包_v1.2_20260901_Domain专项完整合并_治理修正版.md》§二十一（3号位 处理原则）与验收 A15
5. 《APS_核心排产全流程走查_V3.18_20260901_Domain专项冻结对齐_治理修正版_v2.md》（排程运行/排程域运行结果真值语义）

**引用规则**：正文首次出现优先中文术语（排程运行 ScheduleRun / 计划版本 PlanVersion / 候选计划 Candidate / 排程域 Domain / 排程域定义 DomainDefinition）。契约变更必须先改《APS_集成接口设计》，本清单不构成契约变更。

---

# 一、迁移总则

> 目的：消除"部分业务结果找 3号位、其它业务结果找 5号位"的例外。G4（排程运行列表）、G7（排程域依赖）、G8（排程域状态）及 Schedule（计划版本/甘特图/概要）普通结果查询，从 3号位 逐步移交 **5号位 业务接口接入层**；3号位 保留真正的治理职责，停止新增普通计划结果查询。

**六条迁移原则（冻结裁决原文）：**
1. 优先保持已有 URL 和 DTO 兼容；
2. 如需改契约，必须先修改《APS_集成接口设计》；
3. 3号位 不再新增同类查询接口；
4. 5号位 读取 2号位 事实或调用 2号位 Query Service，**不自行重算**；
5. 不新增中转表；
6. 不允许 3号位 与 5号位 长期维持两套不同业务语义。

**普通查询与治理动作分开（5号位实施包 §19.2）：**
- 普通查询：`4 → 5`
- 治理动作：`4 → 3`（排程运行创建 / 排程运行类型与目的治理 / 策略绑定 / 候选计划确认·激活 / 失败恢复治理 / 规则·参数·权限治理 / 审计）

---

# 二、代码现状盘点（3号位 侧交接对象）

> 以下为 3号位 当前已实现、按裁决应移交 5号位 的普通结果查询代码，逐项列出**端点 → 服务 → 仓储 → 返回类型 → 数据源**。URL/DTO 由 5号位 承接并保持兼容。

## 2.1 G4 — 排程运行列表

| 项 | 内容 |
|---|---|
| 端点 | `GET /api/governance/runs?take=&status=&runType=`（GovernanceController.GetRuns） |
| 服务/仓储 | `IScheduleRunRepository.GetListAsync` → Engine `ScheduleRunRepository`（只读冻结列，按 Id 倒序，take 缺省 100 上限 200） |
| 返回类型 | `IReadOnlyList<ScheduleRunGov>`（Core/DTOs/Governance） |
| 数据源 | `APS_Production.dbo.ScheduleRun` 只读冻结列；**状态回填由 2号位 运行收口负责，5号位 不得修改运行结果语义** |
| 备注 | 边界在仓储接口注释中已固化：ScheduleRun 仅"失败恢复新建"一条写入路径归 3号位 治理，5号位 只读 |

## 2.2 G7 — 排程域依赖

| 项 | 内容 |
|---|---|
| 端点 | `GET /api/governance/domain-dependencies?domainCode=&direction=downstream\|upstream`（GovernanceController.GetDomainDependencies） |
| 服务/仓储 | `IDomainDependencyRepository.GetByDomainAsync` → Engine `DomainDependencyRepository` |
| 返回类型 | `IReadOnlyList<DomainDependency>`（Core/Entities/APS） |
| 数据源 | 2号位 `sp_ScanDomainDependency` 每日扫描结果（本接口只读，不落库） |
| 语义 | downstream=本域被谁依赖；upstream=本域依赖谁（direction 语义在仓储注释已固化） |

## 2.3 G8 — 排程域状态汇总

| 项 | 内容 |
|---|---|
| 端点 | `GET /api/governance/run/{scheduleRunId}/domain-status`（GovernanceController.GetRunDomainStatus） |
| 服务 | `RunLifecycleService.GetRunDomainStatusAsync`（3号位 治理逻辑：成功/失败/被阻断 区分，走查 §34 终态语义） |
| 返回类型 | 运行域级状态聚合（3号位 文档 §十六） |
| 数据源 | 2号位 运行结果事实 + 3号位 治理聚合 |
| **⚠️ 关键** | 该聚合的真源判定逻辑当前在 3号位 服务内。5号位 接管查询端点时，"直接复用 3号位 内部服务" 与 "按 2号位 事实重建聚合" 二选一，**必须三方裁定**（见开放问题 Q1） |

## 2.4 Schedule 普通计划结果查询

| 端点 | 服务方法 | 返回类型 | 数据源 |
|---|---|---|---|
| `GET /api/schedule/versions?take=` | `ScheduleQueryService.GetVersionsAsync` | `IReadOnlyList<PlanVersionSummaryDto>` | 已落盘 APS_Production（计划版本） |
| `GET /api/schedule/gantt/{planVersionId}` | `ScheduleQueryService.GetGanttAsync` | `GanttDataDto`（含 GanttResourceDto / GanttTaskDto / GanttUnavailableWindowDto / GanttTaskShareDto / GanttTaskSegmentDto / GanttDelayReasonDto） | 计划版本 + 任务 + 资源 + 订单 + 物料 |
| `GET /api/schedule/summary/{planVersionId}` | `ScheduleQueryService.GetSummaryAsync` | `ScheduleSummaryDto` | 已落盘 APS_Production |
| 服务接口 | `IScheduleQueryService`（Application/Services/Query） | — | 数据源为已落盘事实，不触碰推演期内存沙盘 |

## 2.5 交接 DTO 清单（5号位 承接，保持兼容）

- `PlanVersionSummaryDto`、`GanttDataDto`（+ 6 个子 DTO）、`ScheduleSummaryDto`、`CandidateComparisonDto`（+ TaskChangeSummaryDto / ImpactSummaryDto / PlanVersionBriefDto）
- `ScheduleRunGov`、`DomainDependency`
- 注：`CandidateComparisonDto` 相关接口（`plan-version/{id}/compare-with/{baseId}`）当前为候选计划对比支持接口——候选计划治理留 3号位，**是否随迁待确认**（开放问题 Q6）

---

# 三、3号位 保留清单（禁止误迁）

> 以下代码**不迁移**，仍为 3号位 治理职责（集成接口 v1.28 §10.2 + 5号位实施包 §19.2 明确）：

| 保留项 | 端点/服务 | 依据 |
|---|---|---|
| 排程运行创建 | 规划中：白天候选计划排程运行创建（待 B-1 契约） | §10.2"Run创建" |
| 排程运行类型/目的治理 | RunType/Purpose 校验 | §10.2 |
| 策略绑定 | StrategyProfileVersion 绑定 | §10.2 |
| 候选计划确认/激活 | `POST plan-version/{id}/confirm-candidate`、`POST plan-version/{id}/activate-candidate` | §10.2"Candidate确认/激活" |
| 失败恢复 | `POST run/{failedScheduleRunId}/recover` | §10.2"FAILED恢复" |
| 排程运行引用追溯 | `GET run/{scheduleRunId}/trace` | 3号位 治理 |
| 排程域键冻结校验 | `POST run/{id}/validate-domain-keys` | 3号位 治理 |
| 审计 | `GET /api/governance/audit-logs`（G6） | §10.2"规则/参数/权限治理" |
| 规则/参数/策略治理全套 | rule-set / parameter-set / strategy-profile 的 CRUD / publish / disable / diff / validate | §10.2 |
| 认证/RBAC/业务范围权限 | AuthController 等 | 职责裁决 §六 |

---

# 四、5号位 接管侧清单（要新建/承接）

> 5号位 承接上述 §二 的查询能力，按 5号位实施包 §十九 与既有 5号位 API 模式（如 ProcurementManualEtaController 同款"控制器 + 业务服务"分层）落地。**本清单不代设计具体文件结构**。

## 4.1 新建/承接内容
1. **业务接口接入层查询端点**：承接 G4 / G7 / G8 / Schedule 四个方向的 4→5 入口（URL/DTO 与 §二 保持兼容）；
2. **查询服务**：承接/重建 IScheduleQueryService 等价查询能力（或按开放问题 Q2/Q4 接入 2号位 Query Service）；
3. **权限消费**：消费 3号位 提供的身份、功能权限和业务范围上下文，后端校验 Factory / ProductFamily / Department / Domain 业务范围（未过 → 403）；
4. **数据来源约束**：读取 2号位 事实或调用 2号位 Query Service，**不自行重算**；不在 5号位 新建运行结果镜像表（禁止 `FrontendScheduleRun` / `GatewayAllocation` 之类前端镜像结构）。

## 4.2 边界红线（接管方必须遵守）
- G4：状态回填属 2号位 运行收口，5号位 只读不写；
- G7：只读 `sp_ScanDomainDependency` 扫描结果，不落库；
- G8：不产生第二套计划真相（验收 F42）；
- 失败恢复、候选计划确认/激活 等治理动作**仍走 3号位**（验收 F43/F44），5号位 不得越权执行；
- 普通查询与治理动作分流：`4→5` 查，`4→3` 治。

---

# 五、迁移步骤（阶段化，5号位 主导执行、3号位 配合）

| 阶段 | 动作 | 责任 |
|---|---|---|
| **S0 契约快照** | 以本清单 §二 为契约基线，3↔5 双方冻结 URL/DTO 快照；如需改动，先改《APS_集成接口设计》 | 3号位 出快照，5号位 确认 |
| **S1 平行实现** | 5号位 实现 G4/G7/G8/Schedule 查询端点（4→5），3号位 旧端点暂保留兼容 | 5号位 |
| **S2 语义比对** | 双跑比对：5号位 新端点 vs 3号位 旧端点逐项比对业务结果语义（验收 A15：不改变原业务结果语义） | 5号位 + 3号位 复核 |
| **S3 切换指向** | 4号位 前端切到 5号位 端点（不要求 4号位 为 Owner 迁移重写页面） | 4号位 + 5号位 |
| **S4 收敛** | 迁移完成后，3号位 不再作为 4号位 普通计划结果查询入口（停用或按裁决移除旧端点；3号位 停止新增同类查询） | 3号位 执行，0号位 确认 |

---

# 六、开放问题（需 3↔5↔2 三方/0号位 确认后再动手）

| # | 问题 | 影响 | 建议 |
|---|---|---|---|
| Q1 | **G8 真源判定归属**：5号位 的域级状态查询是"直接复用 3号位 `RunLifecycleService` 内部服务"还是"按 2号位 事实重建聚合"？裁决要求 5号位"读 2号位 事实不自行重算"，但真源聚合逻辑在 3号位 | G8 实现方式 | 建议：5号位 端点内调 3号位 治理服务（治理真源唯一），不重建——需 0号位 确认 |
| Q2 | **G4/G7/Schedule 数据源接入方式**：5号位 是直接读 APS_Production 事实表，还是调 2号位 对外 Query Service？裁决说"或调用2号位Query Service"，需要 2号位 确认是否已有/新增 | 5号位 数据访问契约 | 与 2号位 对齐 Query Service 清单 |
| Q3 | **G4 状态回填**：确认状态回填完全由 2号位 运行收口负责，5号位 只读 | 无实现风险 | 2号位 一句话确认 |
| Q4 | **G7 扫描结果读法**：`sp_ScanDomainDependency` 结果是否经 2号位 查询服务暴露，还是 5号位 直读 Domain_Dependency 表 | 5号位 数据访问契约 | 2号位 确认 |
| Q5 | **旧端点停用时机/方式**：S4 阶段 3号位 旧端点"软停用"（返回迁移提示）还是"移除" | 回归风险 | 0号位 定，优先软停用过渡 |
| Q6 | **候选计划对比接口**（compare-with）：候选计划治理留 3号位，该对比查询是否随迁 5号位？ | 4号位 使用方指向 | 与 4号位 确认使用方；倾向保留 3号位（随候选治理） |

---

# 七、跨号位协助项提报（G4 / G7 / G8 / Schedule 迁移前置）

**提报人**：**5号位**（迁移接管方主导提报与跟进；3号位 提供契约背景并核对，不再代为提交/跟进）
**日期**：2026-09-02
**提交对象**：0号位（裁决）+ 2号位（数据源/运行事实确认）
**性质**：迁移清单 §六 开放问题 Q1~Q6 的正式协助项提报。按"不要越位"红线，5号位 不代 2号位 定义数据访问契约、不代 0号位 裁定归属。**5号位 在协助项回执前仅可做不依赖项（§七.7），回执后正式动工（S1）**。

## 7.1 协助项 1（原 Q1）— G8 排程域状态真源判定归属 → 提交 0号位 裁决

**背景**：G8 域级状态聚合真源逻辑当前在 3号位 `RunLifecycleService.GetRunDomainStatusAsync`（成功/失败/被阻断，走查 §34 终态语义）。裁决要求 5号位"读 2号位 事实不自行重算"。

**分歧点**：5号位 端点复用 3号位 治理服务（真源唯一），还是按 2号位 事实在 5号位 重建聚合（第二套逻辑风险）。

**候选方案**：
- A. 5号位 端点内调 3号位 治理服务，真源唯一（**3号位 建议**）；
- B. 5号位 按 2号位 事实重建聚合。

**期望回执**：0号位 裁定 A/B；若选 A，确认 5号位 调用 3号位 内部服务的接口形态（内进程调用 / 暴露内部接口）。

## 7.2 协助项 2（原 Q2）— G4/G7/Schedule 数据源接入方式 → 提交 2号位 确认

**背景**：裁决"5号位 读取 2号位 事实或调用 2号位 Query Service"。

**分歧点**：5号位 直读 APS_Production 事实表，还是调 2号位 对外 Query Service（当前是否已有/需新增）？

**候选方案**：
- A. 2号位 提供/确认 Query Service 清单，5号位 调用；
- B. 5号位 直读事实表（需 2号位 授权读通道）。

**期望回执**：2号位 提供 Query Service 清单或直读授权确认。

## 7.3 协助项 3（原 Q3）— G4 排程运行状态回填归属 → 提交 2号位 确认

**背景**：G4 列表的状态字段回填由 2号位 运行收口负责。

**期望回执**：2号位 一句话确认"状态回填归 2号位 运行收口，5号位 只读不写"。

## 7.4 协助项 4（原 Q4）— G7 排程域依赖扫描结果读法 → 提交 2号位 确认

**背景**：G7 数据源为 2号位 `sp_ScanDomainDependency` 每日扫描结果（Domain_Dependency 表）。

**分歧点**：经 2号位 查询服务暴露，还是 5号位 直读 Domain_Dependency 表？

**期望回执**：2号位 确认读法；若直读，出具读通道授权。

## 7.5 协助项 5（原 Q5）— 3号位 旧端点收敛方式 → 提交 0号位 裁决

**背景**：S4 阶段 3号位 旧端点（runs / domain-dependencies / domain-status / schedule/*）在迁移完成后收敛。

**候选方案**：
- A. 软停用：返回"迁移至 5号位"提示，过渡期保留（**3号位 建议**）；
- B. 直接移除。

**期望回执**：0号位 定 A/B 及软停用过渡期长度。

## 7.6 协助项 6（原 Q6）— 候选计划对比接口归属 → 提交 4号位 使用方确认 + 0号位

**背景**：compare-with（CandidateComparison）是候选计划对比支持接口，候选计划治理留 3号位。

**分歧点**：随迁 5号位，还是保留 3号位（随候选计划治理）？

**期望回执**：4号位 确认页面使用方；0号位 确认归属（**3号位 建议：保留 3号位**）。

## 7.7 5号位 可先行事项（不依赖以上回执）

- 消费 3号位 权限上下文（身份/功能权限/业务范围）的接入准备；
- DTO 承接（§2.5）与 URL 快照对齐（S0 可先行）；
- 后端业务范围校验骨架（Factory / ProductFamily / Department / Domain → 403）。

## 7.8 提报主体与回执回写

- **提报主体**：协助项 7.1~7.6 由 **5号位** 主导向 0号位/2号位 提交并跟进（3号位 提供契约背景核对，不代提交）；
- **回执回写**：回执由 5号位 回写本迁移清单 §六 开放问题状态，并同步任务清单 v3.0 任务 H 状态；3号位 复核契约语义一致性。

---

# 八、验收标准（对齐冻结文档验收项）

| 编号 | 验收项 | 来源 |
|---|---|---|
| F39 | G4 迁移：排程运行普通查询由 5号位 提供 | 5号位实施包 §十九 验收 |
| F40 | G7 迁移：排程域依赖查询由 5号位 提供 | 同上 |
| F41 | G8 迁移：排程域状态查询由 5号位 提供 | 同上 |
| F42 | Schedule 查询迁移：不产生第二套计划真相 | 同上 |
| A15 | G4/G7/G8 迁移：不改变原业务结果语义 | 3号位实施包 §二十一 验收 |
| — | 3号位 不再新增同类查询；两套入口不长期并存 | 裁决 §十 / 集成接口 §10.3 |
| — | 5号位 消费 3号位 权限上下文；无权限访问返回 403 | 职责裁决 §八 / 5号位实施包 F55/F45 |

---

# 九、一句话交付要求

> 3号位 以本清单完成 G4/G7/G8/Schedule 普通查询的代码交接（保持 URL/DTO 兼容、不自行重算、不建镜像表）；**协助项提报（7.1~7.6）由 5号位 主导提交 0号位/2号位 并跟进回执**；5号位 承接业务接口接入层后，4→5 查、4→3 治分流落地；回执经 0号位/2号位 确认后 5号位 正式动工（S1），全程不越位。
