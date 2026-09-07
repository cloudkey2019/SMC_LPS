# APS V1 3号位开发任务清单 v3.0（从全流程走查 V3.18 拆分 · 含细节文档索引）

**版本**：v3.0
**日期**：2026-09-02
**适用对象**：3号位（及其开发 AI）——主；0号位 / 2号位 / 1号位 / 5号位 / 4号位——协调
**性质**：以《核心排产全流程走查 V3.18（Domain专项冻结对齐版）》为主文档，按 3号位 职责拆分任务组，并逐任务索引最新冻结细节文档（2026-09-02 文件夹）。v3.0 在 v2.0（2026-08-31 收口版）基础上，吸收 2026-09-01 Domain 专项冻结与职责裁决，重组为"主文档驱动 + 细节文档索引"的任务视图。
**上位依据（按权威层级）**：
1. 《APS_核心排产全流程走查_V3.18_20260901_Domain专项冻结对齐_治理修正版_v2.md》（主文档，§5 3号位职责 / §7.1 / §8.2 / §34 / §35 / §41 / §52~§54 / Domain专项增量）
2. 《APS_V1_20260901_职责前端接口与权限归属调整正式裁决_v1.0.md》（Auth/RBAC/Scope 接管、G4/G7/G8 迁移、数据库 Owner 原则）
3. 《APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901.md》（3号位 Domain 治理边界 §4.3）
4. 《APS_V1_3号位规则参数认证权限与运行生命周期开发实施包_v1.2_20260901_Domain专项完整合并_治理修正版.md》（细节文档主实施包）
5. 《APS_集成接口设计_v1.28_20260901职责与前端接口冻结对齐版.md》
6. 《APS_数据库表结构设计_v5.1.4_20260901_Domain专项冻结对齐_治理修正版.sql》《APS_Auth数据库DDL_v1.1》
7. 《0号位裁决_跨号位接口归属答复.md》（2026-08-29，7 条裁决）

**引用规则**：正文第一次出现优先使用中文术语（排程域/排程运行/计划版本/候选计划/数量—时间切片/外部排程域资源阻挡块）。所有细节文档一律引用 2026-09-02 文件夹最新冻结版。

**当前基线（2026-08-31 实测 `dotnet test LPS.APS.Tests`）**：集成测试 **135 pass / 6 skip / 0 fail（141 总）**。6 个 skip 全部为 2号位 DDL 未就位（治理审计表 + 排程运行预期域键列）。

---

# 一、从主文档拆分出的九大任务组（总览）

> 拆分方法：以走查 V3.18 §5「3号位：规则、参数与运行生命周期」五大职责主线为骨架，叠加 §7.1 运行边界、Domain专项增量、职责裁决 v1.0 新增接管项，归纳为九组。

| 组 | 任务组 | 主文档依据（走查 V3.18） | 细节文档索引 | 现状 |
|---|---|---|---|---|
| **A** | 规则/参数/策略版本治理 | §5、§7.1 | 实施包 §一-A、§三、§四、§五、§八~§十四、§三十二、§三十四-A、§三十五 | ✅ 已收口 |
| **B** | 运行生命周期治理 | §5、§8.2、§34、§35、§41、Domain专项 §2/§6 | 实施包 §六、§十六、§十七、§十九、§二十二；Domain专项 §6、§17 | 🟠 主体收口，3 项待推进（见三） |
| **C** | Candidate 版本壳与激活边界 | §5、§41、§52、§53、§54 | 实施包 §十八、§十九、§四十二；Domain专项 §11、§12 | ✅ 已实现 |
| **D** | 规则参数快照冻结装载 | §5、§7.1 | 实施包 §七、§三 | ✅ 已收口 |
| **E** | DomainDefinition 治理 | Domain专项 §2、§8.2 | 实施包 Domain专项 §0~§27；DDL v5.1.4 头部注释；Domain 裁决 §4.3 | ⚠️ 未实现（无代码） |
| **F** | 认证/RBAC/功能权限/业务范围权限 | （第二层裁决） | 职责裁决 v1.0 §六~§九；实施包 §二十二~§三十；APS_Auth DDL v1.1 | ⚠️ 部分待核实 |
| **G** | 3↔4 接口与联调 | §5 | 职责裁决 v1.0 §3.2-A；实施包 §二十、§三十一；集成接口 v1.28；3-4联调规格 v1.2 | ✅ G1~G10 落地 |
| **H** | G4/G7/G8 查询迁移 5号位 | §5、§34 | 职责裁决 v1.0 §十；集成接口 v1.28 §10；实施包 §二十一 | 🔄 迁移中 |
| **I** | 发布前校验 + 无效仓库引用校验 | （0号位 08-29 裁决5） | 实施包 §三十二；任务清单 v2.0 二-9 | ✅ 已实现 |

---

# 二、任务组详情（含细节文档索引）

## A. 规则/参数/策略版本治理 ✅

**主文档依据**：走查 §5「3号位负责：规则/参数编辑、校验、发布；RuleSet / ParameterSet / StrategyProfile版本治理」；§7.1「V1运行时只允许两种基础配置模式：有序规则第一命中 / 全局默认+范围例外覆盖；六张治理表属于保护区」。

**细节文档索引**：
- 实施包 v1.2 §一-A（规则/参数/策略/运行治理定位）、§三（V1规则引擎总体原则：全局默认+少量例外覆盖）、§四（现有六张治理表继续使用）、§五（版本生命周期）、§八（Demand排序规则）、§九（Demand Protection规则）、§十（Supply相关规则）、§十一（采购参数）、§十二（计划良率参数）、§十三（Solver策略参数）、§十四（Candidate Guardrail）、§三十二（发布前校验）、§三十四-A（阶段A：保留原规则治理主线）、§三十五（最低验收场景）
- DDL v5.1.4：RuleSet / ParameterSet / StrategyProfile / 各版本表 / FrozenStrategySnapshot
- 集成接口 v1.28：治理类接口（Rule/Parameter/Strategy 版本 CRUD/发布/停用/Diff/校验/追溯）

**现状**：✅ 已收口（v2.0 阶段 A 收口确认；治理控制器全套路由在库；4号位 审计确认后端完备）。

## B. 运行生命周期治理 🟠

**主文档依据**：走查 §5（ScheduleRun生命周期）；§8.2「ScheduleRun.ExpectedDomainKeysJson 在运行启动时冻结本次预期 Domain 集合，运行过程中不得静默增删；白天 Candidate 的 ExpectedDomainKeysJson 严格只有一个 Domain」；§34（夜间 Domain 发布 / 终态判定 COMPLETED / PARTIAL_SUCCESS / FAILED）；§35（人工失败恢复：新建 ScheduleRun、从失败 Domain 出发读 Domain_Dependency、只重算必要依赖链）；§41（Candidate 入口：新建 ScheduleRun + 新建 Candidate PlanVersion、Base ACTIVE 只读、Candidate 严格单 Domain）；Domain专项 §2（ScheduleRun 开始时冻结有效 Domain 集合）与 §6（一个 Run 多个 Domain PlanVersion）。

**细节文档索引**：
- 实施包 v1.2 §六（一次 ScheduleRun 中的冻结规则）、§十六（ScheduleRun 生命周期：3号位 负责 Run 元数据创建/治理、StrategyProfileVersion 绑定、ExpectedDomainKeysJson、RunType/Purpose、白天 Candidate Run 创建、Candidate 确认/激活、FAILED 恢复边界）、§十七（FULL 失败链与人工恢复）、§十九（RunType / Purpose 冻结合法组合）、§二十二（完整接管认证）
- Domain专项 §6（ScheduleRun 启动治理）、§17（ExpectedDomainKeysJson Owner 边界）
- DDL v5.1.4 §3.1（ScheduleRun：ExpectedDomainKeysJson + ISJSON CHECK）、§3.2（PlanVersion：DomainKey / SourceScheduleRunId / 状态值域）
- 集成接口 v1.28 §5（运行治理：3号位 创建/治理 Run 后调用 2号位 内部排程服务；4号位 不直接调用 2号位）

**已实现**：ExpectedDomainKeys 冻结校验端点、Candidate 确认/激活、FAILED 恢复、Run 引用追溯、G8 域级状态汇总（RunLifecycleService + GovernanceController 全套）。
**待推进**：见第三章 B-1 / B-2 / B-3。

## C. Candidate 版本壳与激活边界 ✅

**主文档依据**：走查 §5（Candidate版本壳与激活边界）；§41（一个 Candidate = 1 Domain + 1 Base ACTIVE PlanVersion；其它 Domain ACTIVE 共享资源作为 ExternalDomainResourceBlocks 传递）；§52（WHATIF 不能自动正式，CTP / INSERT_IMPACT_ANALYSIS 不得激活）；§53（手工拖拽/调整）；§54（任务手动锁定与 Demand Protection 不是一回事）。

**细节文档索引**：
- 实施包 v1.2 §十八（Candidate 最小人工确认：生成/展示/Actor 确认/ConfirmedAt/CandidatePlanVersionId/Activate/审计，不等同通用 ApprovalFlow）、§十九（RunType/Purpose 合法组合，CTP / INSERT_IMPACT_ANALYSIS 不得激活）、§四十二（DoD）
- Domain专项 §11（Candidate 治理保持单 Domain）、§12（Candidate 与业务范围权限）
- DDL v5.1.4 §3.2（PlanVersion 状态值域：BUILDING=版本壳已创建结果未完整落库 / CANDIDATE / ACTIVE / ARCHIVED / FAILED；UQ_PlanVersion_OneActivePerDomain）

**现状**：✅ 确认/激活已实现（RunLifecycleService：ConfirmCandidateAsync 只记审计、ActivateCandidateAsync 过 5 道硬校验 + 原子替换同域 ACTIVE）。

## D. 规则参数快照冻结装载 ✅

**主文档依据**：走查 §5「运行开始前冻结并装载规则参数快照」；§7.1「运行开始前加载不可变版本快照，1/2号位在内存消费；禁止逐 Demand 调用 3号位、逐 Task 调用 5号位」。

**细节文档索引**：实施包 v1.2 §七（2↔3 FrozenStrategySnapshot 接口）、§三；集成接口 v1.28 §"3号位 Strategy Snapshot 一次冻结、批量装载"。

**现状**：✅ 已收口（v2.0 阶段 B：六块 DTO 冻结、Provider + 缓存、13 方案A 测试转绿；B-5 快照缓存已实现）。

## E. DomainDefinition 治理 ⚠️

**主文档依据**：走查 Domain专项 §2（Domain 冻结：ScheduleRun 开始时读取当前有效 DomainDefinition → 形成当前 Run 计算 Domain 集合 → 保存 ExpectedDomainKeysJson → 后续执行只使用冻结集合）；§8.2；Domain 裁决 §4.3（3号位：DomainDefinition 后端维护能力、合法性校验、启用/停用、运行开始时提供/冻结本次有效 Domain 集合、ExpectedDomainKeysJson 冻结、PlanVersion 与 DomainKey 生命周期一致性、Candidate 严格单 Domain 校验）。

**细节文档索引**：
- 实施包 Domain专项 §0（本版定位）、§1（DomainDefinition 正式定位：APS 内部基础配置，不属于 RuleSet/ParameterSet/ODS）、§2（V1 允许的 Domain 范围类型：FAMILY / FACTORY_FAMILY）、§3（3号位 DomainDefinition 治理职责：新建/编辑/启用/停用/唯一性检查/ScopeType 合法性/Factory·ProductFamily 引用合法性/审计/查询当前有效集合；最小字段 DomainKey/DomainName/ScopeType/ProductFamily/Factory/IsActive/SortOrder/审计）、§4（DomainKey 治理规则：稳定/唯一/可追溯，不等于 ProductFamily.Code / PlanVersionId / ScheduleRunId）、§5（DomainDefinition 变更生效边界：Run 启动时冻结，中途配置修改不影响当前 Run）、§6（ScheduleRun 启动治理）、§8（Domain_Dependency 治理：上下游为真实 DomainKey）、§11（Candidate 治理保持单 Domain）、§16（DomainDefinition 数据库 Owner）、§17（ExpectedDomainKeysJson Owner 边界）、§25（Domain 专项验收场景）、§26（完成定义）
- DDL v5.1.4 头部注释（14~16 行）：声明新增 DomainDefinition 最小配置表（DomainKey/DomainName/ScopeType/ProductFamilyId/FactoryId/IsActive/SortOrder/审计字段；V1 ScopeType 仅 FAMILY/FACTORY_FAMILY；属 3号位 治理；不纳入 RuleSet/ParameterSet/StrategyProfile/ODS）。**注：v5.1.4 中该表仅有声明，无 CREATE TABLE 实体**——物理表是否已落库需与 2号位 核实。
- Domain 裁决 v1.0 §3.2（物理实现优先顺序：先查等价结构，无则增最小表）、§17.6（数据库/字段说明检查）

**现状**：⚠️ **未实现**（代码库 `*.cs` 中无 DomainDefinition 实体/仓储/服务；DDL 仅注释声明）。**本组是 3号位 当前最大的净新增交付面。**

## F. 认证/RBAC/功能权限/业务范围权限 ⚠️

**主文档依据**：职责裁决 v1.0 §六（3号位 完整接管认证与权限：原 2号位 认证代码整体移交）、§七（V1 功能权限：查看计划/CTP/插单影响/局部重排/人工重排/Candidate确认/规则维护/规则发布/Manual ETA 维护/Demand Protection 释放/MES 受控操作/用户角色权限管理/审计查看；V1 业务范围权限：Factory / ProductFamily / Department / ProductionDepartment / Domain，允许组合）、§八（权限执行边界：3号位 认证+角色+功能+业务范围 → 4号位 展示 → 3号位或5号位 API 后端再次鉴权）、§九（APS_Auth 库 Owner 归 3号位）。

**细节文档索引**：
- 实施包 v1.2 §二十二（完整接管认证）、§二十三（APS_Auth 数据库 Owner）、§二十四（V1 功能权限）、§二十五（V1 业务范围权限）、§二十六（3号位权限服务输出）、§二十七（后端权限硬校验）、§二十八（业务范围与 2号位 接口边界：2号位 不维护谁拥有什么权限，只消费已校验并限定的业务 Scope）、§二十九（Audit）、§三十（Approval 兼容边界）、§三十一（规则页面与权限页面）、§三十四-B/C/D（阶段 B Auth 接管 / C RBAC / D Business Scope）
- APS_Auth 数据库 DDL v1.1、字段说明 v1.1
- 集成接口 v1.28 §7（认证/授权接口：登录/刷新/登出/当前用户；用户/角色/权限；功能权限；业务范围权限）

**现状**：⚠️ Auth 壳（Login/Refresh/Logout/CurrentUser）已存在；**RBAC / 功能权限 / 业务范围权限的后端硬校验与移交完成度需盘点核实**（见第三章 F）。

## G. 3↔4 接口与联调 ✅

**主文档依据**：职责裁决 v1.0 §3.2-A（治理、安全、运行治理类：4号位 → 3号位，含登录/用户角色权限/功能权限/业务范围权限/RuleSet·ParameterSet·StrategyProfile/Demand Priority 配置/Diff·发布·退役·校验/白天 Candidate Run 创建/CTP·插单试算发起/LOCAL_RESCHEDULE·MANUAL_RESCHEDULE 发起/Candidate 确认·激活/FAILED 恢复治理）。

**细节文档索引**：实施包 v1.2 §二十（3↔4 正式接口范围）、§三十一（规则治理与 Auth/RBAC 页面）；集成接口 v1.28；《APS_V1_3-4联调接口规格 v1.2》（提供给 4号位）与《3-4联调接口规格 4号位审计结果 v1.0》。

**现状**：✅ 联调接口 G1~G10（主表列表、停用、发布版本直达、运行列表、域依赖、审计日志、运行域级状态、发布变更原因等）已全部落地；4号位 审计 6 缺口已在 v1.2 规格逐项处理。

## H. G4/G7/G8 查询迁移 5号位 🔄

**主文档依据**：职责裁决 v1.0 §十（G4 Run 列表 / G7 Domain 依赖 / G8 Domain 状态及 Schedule 查询，为避免"部分业务结果找 3号位、其它业务结果找 5号位"的例外，逐步移交 5号位 业务接口接入层；尽可能保持 URL/DTO 兼容；契约变更先改《集成接口设计》；3号位 不再新增同类查询接口）。

**细节文档索引**：集成接口 v1.28 §10（G4/G7/G8/Schedule 查询迁移）；实施包 v1.2 §二十一（G4/G7/G8/Schedule 查询迁移：3号位 保留真正治理侧职责，停止新增普通计划结果查询）。

**现状**：🔄 迁移进行中。3号位 保留 Run/Candidate 治理真职责（G8 域级状态真源判定），普通计划结果查询逐步移交 5号位；不新增中转表。

## I. 发布前校验 + 无效仓库引用校验 ✅

**主文档依据**：0号位 2026-08-29 裁决5（仓库编码校验归 3号位）；走查无直接章节（属治理侧）。

**细节文档索引**：实施包 v1.2 §三十二（发布前校验：后端发布强制完整校验，IsValid=false 拒发）；任务清单 v2.0 二-9（无效仓库引用校验：0号位 裁决5，INVALID_WAREHOUSE_REF）。

**现状**：✅ 已实现（`GovernanceVersionService.ValidateWarehouseReferencesAsync` 采集 Supply/Procurement 四处仓库编码，只读校验权威事实源 MaterialSupplyContext ∪ InventoryFact_ERP；空源守卫；可选 ctor 参数向后兼容；135 测试基线无回归）。**待办**：权威源一句话确认（2号位/5号位）。

---

# 三、待推进任务清单（按优先级）

> 从主文档拆分出的任务中，尚未收口的净增量。**优先级 P0=必须补齐，P1=主体收口后待转绿/待核实。**

| 优先级 | 任务 | 主文档依据 | 细节文档索引 | 卡点 / 依赖 | 3号位 侧可推进度 |
|---|---|---|---|---|---|
| **P0** | **E-1 DomainDefinition 后端治理**（实体/仓储/服务/接口：新建/编辑/启用/停用/唯一性/ScopeType 与引用合法性/审计/当前有效集合查询） | 走查 Domain专项 §2 | 实施包 Domain专项 §3、§4、§5、§6、§16、§25、§26 | 代码零实现；物理表是否已落库需核实（v5.1.4 仅注释声明） | 自主可推进（表结构由 3号位 治理定义，DDL 按新 Owner 原则归 3号位 或与 2号位 协调） |
| **P0** | **E-2 ExpectedDomainKeysJson 冻结接入 Run 创建**（Run 启动时写入冻结集合） | 走查 §8.2、Domain专项 §2 | 实施包 §六、Domain专项 §6、§17 | 校验端点已有（validate-domain-keys）；写集合落点待接通 | 需 2号位 建 ExpectedDomainKeysJson 列后接通（见 B-2） |
| **P1** | **B-1 白天 Candidate Run 创建端点**（MANUAL_RESCHEDULE 发起：3号位 建 Run → 内部调 2号位 运行服务 → 建壳 → 排程） | 走查 §35、§41 | 实施包 §十六；集成接口 v1.28 §5；0号位 08-29 裁决 | GovernanceController 无创建 Run 路由；ScheduleController 明确"V1 不包含触发重排" | 需先定 2号位 内部运行服务调用契约（治理入口由 3号位 调 2号位 内部运行服务） |
| **P1** | **B-2 运行生命周期 6 集成测试转绿** | 走查 §34、§35 | 实施包 §十六~§十八 | 2号位 建 GovernanceAuditLog 表 + ScheduleRun.ExpectedDomainKeysJson 列（红线 #6；新 Owner 原则下 3号位 可协调） | 阻塞（DDL 未落库） |
| **P1** | **F 认证/RBAC/业务范围权限完成度盘点与补齐** | 职责裁决 v1.0 §六~§八 | 实施包 §二十二~§二十八；APS_Auth DDL v1.1 | Auth 壳已有；User/Role/Permission/Scope 后端硬校验待盘点 | 先盘点，后按缺口补齐 |
| **P2** | **I 权威仓库事实源确认** | 0号位 08-29 裁决5 | 实施包 §三十二 | 等 2号位/5号位 一句话确认权威源位置 | 等待确认（如出入仅改一处查询 SQL） |
| **P2** | **H G4/G7/G8 迁移 5号位 配合** | 职责裁决 v1.0 §十 | 集成接口 v1.28 §10 | 5号位 业务接口接入层逐步接手 | 3号位 停增同类查询、配合契约兼容 |

---

# 四、3号位 明确不负责（从主文档 §5 / 职责裁决二次确认）

- 逐 Demand 排序、逐 Supply 分配（2号位）；
- Task / Pegging 运行结果写入（2号位）；
- 在排程过程中被逐笔同步回调（禁止逐 Demand RPC 调用 3号位）；
- ODS / BOM 展开 / 复杂事实计算 / 数据质量 Issue（5号位）；
- FinalTask / 最终延期 ReasonCode（1号位）；
- 给每条 Order 逐笔执行 Domain 匹配、Domain 内 Pegging、Domain 内 Solver（2号位 执行层）。

---

# 五、完成定义（DoD）

1. E-1 DomainDefinition 治理全套（实体/仓储/服务/接口/审计）落地，当前有效 Domain 集合可查询，合法性校验齐全；
2. E-2 白天与夜间 Run 启动均冻结 ExpectedDomainKeysJson（白天严格 1 Domain，夜间 ≥1 去重）；
3. B-1 白天 Candidate Run 创建端点可用（内部调 2号位 运行服务），4号位 可从页面发起 MANUAL_RESCHEDULE；
4. B-2 6 个运行生命周期集成测试转绿（141 全绿 / 0 skip）；
5. F 认证/RBAC/功能权限/业务范围权限后端硬校验落地，APS_Auth DDL v1.1 对齐；
6. H G4/G7/G8 迁移交接完成，3号位 不再新增普通查询接口；
7. 红线：#5 接口即契约（先改契约文档再改签名）、#6 DB 结构变更 2号位 专属（新 Owner 原则下涉其它 Owner 核心表走跨号位评审）。

---

# 六、一句话交付要求

> 3号位 以走查 V3.18 为业务/技术主依据、以实施包 v1.2 为细节实现依据：守住已收口的规则/参数/策略治理、运行生命周期治理、Candidate 确认激活、快照冻结、3↔4 联调；补齐 DomainDefinition 治理（P0）与白天 Candidate Run 创建（P1）；配合 2号位 DDL 转绿、5号位 查询迁移、0号位 裁决落地，全程不越位。
