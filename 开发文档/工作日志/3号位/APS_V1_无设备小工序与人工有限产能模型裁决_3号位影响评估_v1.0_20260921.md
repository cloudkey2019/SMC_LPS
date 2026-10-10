# APS V1 无设备小工序与人工有限产能模型裁决 —— 3号位影响评估

**版本**：v1.0
**日期**：2026-09-21
**评估对象**：《第六APS_V1_无设备小工序与人工有限产能模型方案论证及裁决.md》（下称《第六》）
**评估角色**：3号位（Application 编排 + 规则参数治理）
**性质**：影响评估报告（含待 0号位 裁决清单 + 跨号位依赖 + 3号位 落地动作清单）

---

## 一、裁决要点摘要

《第六》八条最终裁决中，**直接涉及 3号位 职责**的是 §七 规则参数链路：

| # | 裁决内容 | 3号位 关联度 |
|---|---|---|
| 1 | 无设备 ≠ 无限产能，按有限能力约束分三类：有限资源工序 / 非瓶颈工序 / 等待型工序 | 间接（数据表达与分类） |
| 2 | 人工复用现有 Resource / ResourceCalendar / OperationResourceEligibility 模型，**不建独立模型** | 间接（数据源与契约视图） |
| 3 | 人工通过**产能槽**表达（10人 = 10 个并行 Resource Slot），不维护真实员工 | 间接（资源主数据供给） |
| 4 | 白班/夜班通过 **Resource Calendar** 表达 | 间接（日历装载落地） |
| 5 | **不修改 MES Routing** | 约束（数据事实层不动） |
| 6 | Operation 与 Resource 通过 **OperationResourceEligibility** 关联 | 间接（已存在，需确认覆盖） |
| 7 | 等待和转运采用 **3号位规则参数**简化表达：Operation Transition Rule（按部门/Stage 默认工序间等待、转运）+ Stage Transfer Time（大工艺间转运） | **直接主责** |
| 8 | Operation/ProductionDepartment/Resource/Calendar/Eligibility 职责模型 | 间接 |

> 仓库内未检索到「第一~第五」篇方案论证及裁决，如本系列需串联上下文，建议 0号位 提供系列目录。

---

## 二、影响面总览

| 裁决项 | 3号位 影响 | 等级 | 现状 |
|---|---|---|---|
| Operation Transition Rule（工序间等待/转运默认规则） | **新增规则参数治理**（模型/接口/种子/审计） | 🔴 直接主责 | 无此规则，无表 |
| Stage Transfer Time（大工艺间转运时间） | **新增规则参数治理** | 🔴 直接主责 | 无此规则，无表；现有 StageOverlapParams 语义不同 |
| 工序三分类（有限/非瓶颈/等待） | 需定义**分类表达与判定来源** | 🟠 待裁决 | RoutingOperation.ProcessType 是工序类型标签，非产能模型分类 |
| 等待型工序 Wait Duration | 需定义**事实 vs 规则默认**优先级 | 🟠 待裁决 | RoutingDependency.LagTime 已存在但 MES 硬编码 0 |
| 人工产能槽（MANUAL_STATION 资源） | 资源主数据供给 + 契约视图缺口 | 🟡 协调2号位 | Resource.ResourceType 支持 MANUAL_STATION；视图是否供给待确认 |
| Resource Calendar 白/夜班 | 日历装载服务落地 | 🟡 协调2号位 | SchedulingContext 已建模，V1 7x24 占位，装载服务未落地 |
| 六块 FrozenStrategySnapshot 契约 | 等待/转运规则是否入 Snapshot | 🔴 契约变更风险 | 六块冻结，不含等待/转运 |
| 权限码 / 4号位前端 / 种子 | 规则维护交付面 | 🟠 直接主责 | 参照 SetupTransitionRule 范式（aps.rule.* / aps.setup.*） |

---

## 三、分项影响评估

### A. 等待/转运规则参数（3号位 直接主责）

《第六》§七 明确：**Operation Transition Rule**（按部门/Stage 维护默认工序间等待、转运规则，如"精加工→精修默认等待 30 分钟"）+ **Stage Transfer Time**（大工艺间转运，如"机加工→表面处理 4 小时"）由 **3号位 规则参数维护 → 2号位 加载 → 1号位 消费**。

**影响分析**：
1. **需新增两类规则治理模型**，参照 `SetupTransitionRule` 既有范式（RuleSetVersionId 冻结 + 发布前冲突校验 + AuditLog 审计 + 权限码 + 种子落库）：
   - `OperationTransitionRule`：维度至少含（部门, 前工序, 后工序 或 按 Stage 默认）→ 等待分钟/转运分钟；支持 EXACT（明确工序对）与 DEFAULT（部门/Stage 级默认）双类型（对齐 SetupTransitionRuleType 口径）。
   - `StageTransferTime`：维度（起 Stage, 止 Stage）→ 转运分钟；按 Stage 字典（RoutingStage，3号位 契约 → 2号位 装载）维护。
2. **契约变更风险（红线5：接口即契约）**：当前六块 Snapshot（FrozenStrategySnapshot v0.2 冻结）**不含等待/转运**。等待/转运规则进入 Solver 有两条路径，均需 0号位 裁决：
   - **路径一**：扩展六块（新增 Transition/Transfer 块）→ 触碰已冻结 Snapshot 契约；
   - **路径二**：参照 `SetupTransitionRule` 走 `DomainSolveRequest` 单独传入（当前 Setup 换型即此模式）→ 不触碰六块，但需改 `DomainSolveRequest` DTO（1号位 消费接口）。
3. **与现有 StageOverlapParams 的关系**：`SolverStrategyBlock.StageOverlapParams`（AllowOverlap / TransferBatchQty / ThresholdQty / ThresholdPercent）是"跨 Stage 是否允许重叠 + 转移批量"，与 **Stage Transfer Time**（跨大工艺固定转运时长）语义**不同维度**，需在契约文档中显式区分，避免 1号位 混淆消费。

### B. 工序三分类的表达与判定（待裁决）

《第六》§六 分类：
- **有限资源工序**（精修）→ 进 Resource 有限产能排程；
- **非瓶颈工序**（清洗）→ 仅 Operation Duration（有处理时间，不竞争）；
- **等待型工序**（自然冷却）→ 仅 Wait Duration。

**影响分析**：
1. **现状**：`RoutingOperation.ProcessType`（MACHINING/ASSEMBLY/INSPECTION）是工序类型标签（v5.0.12 明确"仅用于报表/粗分组，不参与 1号位 排程主键"），**与产能模型三分类不是同一维度**，不能直接复用。
2. **三分类是产能模型属性，不是 MES 工艺事实**（§五：不修改 MES Routing）→ 需要在 APS 侧表达，候选：
   - 由 3号位 规则参数维护"工序→产能模型分类"映射（如清洗=UNCONSTRAINED、自然冷却=WAIT）—— **3号位 主责**；
   - 或由 2号位 投影时依据 MES 数据（IfhasEquips / EquipType / 无资源关联）自动推断—— **2号位 主责**。
   - **该归属未在《第六》中裁决，需 0号位 明确**。
3. **表达载体缺口**：`RoutingPlanningParam`（Min/MaxBatchSize/TransferBatchSize，SourceSystem=APS_LOCAL）是现成 APS 本地规划参数表，可承载分类标签；但需 DDL 评估/0号位 批准。

### C. 人工产能槽与日历（协调 2号位）

《第六》§三/§四：精修 10 人 = `FINISH_DAY_01~10` 十个并行 Resource Slot，白班 Calendar 08:00-17:00、夜班 `FINISH_NIGHT_01~05` Calendar 22:00-06:00；**否决**"一个虚拟 Resource + CapacityFactor=10"。

**影响分析**：
1. **资源侧已具备基础**：`Resource.ResourceType` 已支持 `MANUAL_STATION`（DDL v5.1.7）；`OperationResourceEligibility` 已支持"某工序 → 哪些资源（含优先级/产能系数）"。裁决模型与现有结构**方向一致**。
2. **产能槽语义确认**：1号位 Solver 按 Resource 时间轴互斥占用，10 个并行槽 = 10 条独立时间轴，**天然满足"10 个任务并行"**，无需 CapacityFactor 折叠。现有 `OperationResourceEligibility.CapacityFactor` / `Resource.CapacityFactor` 是"产能系数（速度因子）"语义，与裁决"不得用 CapacityFactor 表达并行数"**不冲突**，但需在契约中固化口径防误用。
3. **数据供给缺口（跨 2号位）**：MANUAL_STATION 资源（精修人工 01~15）是否由 MES 契约视图 `ext_MES_APS_Resource_View` 供给？人工产能槽的 Resource 主数据 + 白/夜班 Calendar 需要 2号位 装载落地。当前 `SchedulingOrchestrator.cs:592` 注释明确 "V1 日历策略 7x24，待专门的 ResourceCalendar 装载服务落地后替换" —— **ResourceCalendar 装载服务未落地**，属 2号位 职责，3号位 需提报契约视图缺口清单。
4. **Eligibility 覆盖确认**：人工工序（精修）→ 人工资源 01~15 的关联，需确认 `ext_APS_OperationResourceEligibility_View`（2号位 装载）是否覆盖 MANUAL_STATION 资源场景。

### D. 数据事实 vs 规则默认（待裁决）

**现状**：`RoutingDependency.LagTime`（V1 仅 ES，默认 0）已存在，且已被 1号位 Solver 消费（`PhaseTwoInitialScheduler` / `PhaseFourLocalRepair` 的 `GetLagTime`）。但《MES_APS_Routing_View.sql》**硬编码 `LagTime = 0`**（组装段 L247/L276），即 MES 侧不提供等待事实。

**影响分析**：等待型工序（自然冷却）的 Wait Duration 无真实来源。按《第六》§七，由 3号位 规则参数（OperationTransitionRule 默认等待）提供 → 需明确：
1. **优先级 Invariant**：规则默认 vs MES LagTime 事实的合并口径（对齐既有 `ManualETA > ERPETA > DefaultLT` 的固定 Invariant 思路，建议"MES LagTime 事实 > 规则默认"，或参考 SetupTransitionRule 的 EXACT/DEFAULT 覆盖语义）；
2. 该口径是否写入契约文档（红线5）。

### E. 契约与红线影响

1. **红线"不新增表"（交付契约 v0.2）**：OperationTransitionRule / StageTransferTime 若落地为治理表，属 DDL 变更（同 SetupTransitionRule 曾走 0号位 批准 + DDL v5.1.8 补丁的路径），**3号位 不自决，提交 DDL 变更申请**。
2. **红线5（接口即契约）**：若走路径一（扩展六块）或路径二（改 DomainSolveRequest），均需先更新《3号位交付契约》/《关键接口冻结》相关章节，再动代码。
3. **DomainSolveRequest 现状**：已有 `SetupTransitionRules` 传入先例（Setup 换型闭环），等待/转运规则可复用同一通道（需 1号位 确认消费契约）。

---

## 四、现有实现对照表

| 裁决需求 | 现有支持 | 缺口 | 责任 |
|---|---|---|---|
| 人工资源（MANUAL_STATION） | ✅ Resource.ResourceType | 视图是否供给 | 2号位 |
| 工序→资源关联 | ✅ OperationResourceEligibility | 人工场景覆盖确认 | 2号位 |
| 资源日历（白/夜班） | ⚠️ SchedulingContext 已建模 | 装载服务未落地（7x24 占位） | 2号位 |
| 等待型工序 Wait Duration | ⚠️ RoutingDependency.LagTime（ES，默认0） | MES 硬编码 0，无事实来源 | 3号位规则默认 / 待裁决 |
| 工序间等待/转运规则 | ❌ 无 | 新增治理模型 | **3号位** |
| 大工艺间转运时间 | ❌ 无 | 新增治理模型（与 StageOverlap 区分） | **3号位** |
| 工序三分类标签 | ❌ ProcessType 语义不同 | 分类表达载体 | 待裁决 |
| 六块 Snapshot | ✅ 已冻结 | 不含等待/转运 | 契约变更（0号位） |

---

## 五、跨号位依赖

| 号位 | 依赖事项 | 3号位 动作 |
|---|---|---|
| 0号位 | ①等待/转运规则是否新增治理表（DDL）②是否入六块/改 DomainSolveRequest ③工序三分类归属 ④规则默认 vs MES 事实优先级 ⑤人工产能槽/日历装载 | 提报裁决清单（见 §六） |
| 2号位 | MANUAL_STATION 资源视图、ResourceCalendar 装载、Eligibility 人工场景、LagTime 事实来源 | 提供契约视图缺口清单（提报 2号位） |
| 1号位 | 消费等待/转运规则（DomainSolveRequest 通道或六块）、产能槽并行时间轴语义确认 | 提供消费契约草案（对齐 SetupTransitionRules 模式） |
| 4号位 | 等待/转运规则维护页面 | 规则治理 API 规格交付（CRUD + 权限码） |
| 5号位 | 无直接依赖（Stage 顺序权威在 StageDetail，5号位产出） | 无需动作 |

---

## 六、待 0号位 裁决清单

| # | 裁决点 | 选项 | 3号位 建议 |
|---|---|---|---|
| R1 | OperationTransitionRule / StageTransferTime 是否新增治理表 | A.新增（参照 SetupTransitionRule） B.并入六块 DTO 不建表 C.延后 V1 | **A**（治理可追溯，符合既有范式） |
| R2 | 等待/转运规则如何进入 1号位 | A.扩展六块 B.走 DomainSolveRequest（同 SetupTransitionRules） | **B**（不动已冻结 Snapshot 六块，风险最小） |
| R3 | 工序三分类（有限/非瓶颈/等待）判定归属 | A.3号位 规则参数 B.2号位 投影推断 | **A**（与规则参数治理同域；B 需 MES 数据支撑，MES 无该属性） |
| R4 | 规则默认 vs MES LagTime 事实优先级 | A.MES 事实 > 规则默认 B.规则默认完全接管 | **A**（对齐 ManualETA 固定 Invariant 思路，MES 后续补事实不返工） |
| R5 | 人工产能槽 + 白/夜班 Calendar 数据源 | A.由 2号位 契约视图供给 B.3号位 维护资源/日历 | **A**（Resource 主数据走外部镜像，符合 v5.0 设计） |

---

## 七、3号位 落地动作清单

> 所有动作须在 R1~R5 裁决后按序执行，不自决越线。

| 阶段 | 动作 | 依赖 | 交付物 |
|---|---|---|---|
| S0 | 提交本影响评估 + R1~R5 裁决请求 | — | 本文档 + §六清单 |
| S1 | 若 R1=A：DDL 变更申请（OperationTransitionRule / StageTransferTime 表） | R1 | DDL 变更申请单 |
| S2 | 规则治理模型：Entity + Repository + Service（CRUD+冲突校验+审计）+ Validator | R1 / 2号位 DDL 落地 | 参照 SetupTransitionRule 全套 |
| S3 | 权限码 + 治理 API 规格（CRUD 端点 + 种子）交付 4号位 | S2 | 3-4联调接口规格 |
| S4 | 消费契约草案：1号位 等待/转运规则输入（DomainSolveRequest 或六块） | R2 / 1号位 | 契约草案 |
| S5 | 2号位 契约视图缺口清单（MANUAL_STATION / Calendar / Eligibility 人工场景 / LagTime） | R5 | 提报 2号位 清单 |
| S6 | 种子数据落库 dev（等待/转运默认规则段） | S2 | seed 脚本 |
| S7 | 集成测试：等待型工序（自然冷却）、非瓶颈工序（清洗）、人工产能槽并行排程 | S4/S5/S6 | 测试用例 |

---

## 八、结论

《第六》裁决对 3号位 的**直接主责**集中在**等待/转运规则参数治理**（OperationTransitionRule + StageTransferTime），其余各项（人工产能槽、Resource Calendar、工序三分类、Eligibility）多为**数据供给与装载**，属 2号位 职责但需 3号位 以契约视图缺口清单形式驱动。

最大契约风险：等待/转运规则进入 Solver 的通道（六块 vs DomainSolveRequest）触碰已冻结契约，**建议优先 R1/R2 裁决**，R3~R5 可在 S1 并行推进。3号位 不自决新增表、不改已冻结接口签名，全部走 0号位 裁决 + 契约文档先行。

---

*本评估基于 2026-09-21 工作树现状（DDL v5.1.8 / SetupTransitionRule 已落地）。后续裁决或 DDL 变化将修订本报告。*
