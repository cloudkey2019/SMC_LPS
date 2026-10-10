# APS V1 无设备小工序与人工有限产能模型补充裁决 —— 3号位回执

**版本**：v1.0
**日期**：2026-09-21
**回执对象**：0号位《未命名的Markdown文件 (49)(2).md》（下称《补充裁决》）
**回执角色**：3号位
**前置**：《第六APS_V1_无设备小工序与人工有限产能模型方案论证及裁决.md》 + 《APS_V1_无设备小工序与人工有限产能模型裁决_3号位影响评估_v1.0_20260921.md》（含 R1~R5）

---

## 一、收悉确认

《补充裁决》已通读。总体判断：0号位 未推翻原方案，补充裁决把「概念正确」推进到「可落地」，核心补充为三项：

1. **Operation 能力分类**（OperationPlanningMode：FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY）→ **3号位 规则治理**；
2. **等待/转运规则治理**（Operation Transition Rule / Stage Transfer Time）→ **3号位 维护**，走**独立 DomainSolveRequest**，**不扩已冻结六块 Snapshot**；
3. **Calendar 资源装载** → **V1 必须闭环**（跨 2号位）；人工资源槽由**资源主数据侧**正式创建，2号位 只加载。

3号位 对以上无异议，逐项回执如下。

---

## 二、R1~R5 裁决对照回执

| # | 原影响评估裁决点 | 0号位 裁决 | 3号位 回执 | 状态 |
|---|---|---|---|---|
| R1 | OperationTransitionRule / StageTransferTime 是否新增治理表 | 保留原方案：3号位 维护；**单独进入 DomainSolveRequest**，参照 SetupTransitionRule 方式 | 确认。参照 SetupTransitionRule 建治理模型（Entity+Repository+Service+Validator+审计）；建表属 DDL 变更，仍按红线提 DDL 变更申请（详见 §三-3） | ✅ 方向确认 / ⏳ DDL 待批 |
| R2 | 等待/转运规则如何进入 1号位 | **独立 DomainSolveRequest，不扩 Frozen 六块** | 确认。与 R2 建议一致（对齐 SetupTransitionRules 先例），不动已冻结六块；改 DomainSolveRequest 前先更新契约文档（红线5） | ✅ 已裁决 |
| R3 | 工序三分类（有限/非瓶颈/等待）判定归属 | **增加 OperationPlanningMode**（FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY）；分类=APS 规则，**3号位 规则治理**；2号位 不自动推断；配品分类由业务确认 | 确认。OperationPlanningMode 属 APS 规划属性，3号位 治理；2号位 仅加载传递；1号位 消费。配品（M101）需业务确认，3号位 提供治理入口 | ✅ 已裁决 / ⏳ 载体 DDL + 业务确认流程待定 |
| R4 | 规则默认 vs MES LagTime 事实优先级 | 《补充裁决》**未明确覆盖**此优先级口径 | 需 0号位 补裁：建议「MES LagTime 事实 > OperationTransitionRule 规则默认」（对齐 ManualETA>ERPETA>DefaultLT 固定 Invariant 思路），避免 MES 后续补事实后返工（详见 §四-1） | ⚠️ 残余待补裁 |
| R5 | 人工产能槽 + 白/夜班 Calendar 数据源 | **B 为正式生产方式**：资源主数据侧创建（业务/资源管理提供人数 → 生成 FINISH_DAY_01~10 / FINISH_NIGHT_01~05 → Calendar 维护班次）；2号位 仅加载；测试阶段允许 2号位 临时 Seed，不作正式 V1 设计；Resource Calendar 装载 V1 必须闭环 | 确认。3号位 不生成资源、不维护员工；Calendar 装载服务落地属 2号位 V1 闭环项，3号位 以契约视图缺口清单驱动；测试 Seed 边界遵守"临时不转正式" | ✅ 已裁决 |

---

## 三、3号位 对裁决的确认与补充理解

### 1. OperationPlanningMode —— 3号位 规则治理，载体待定

- 0号位 裁决：分类=APS 规则，3号位 治理，2号位 加载，1号位 消费；**不是 MES Routing 事实**，不以 EquipNo 是否为空自动推断。
- 3号位 理解：`RoutingOperation.ProcessType`（MACHINING/ASSEMBLY/INSPECTION）保持为工序类型标签（v5.0.12 口径不变），**另立 OperationPlanningMode** 为产能模型分类，两者不同维度。
- **载体建议**：复用 `RoutingPlanningParam`（APS_LOCAL 规划参数表，已承载 Min/MaxBatchSize/TransferBatchSize）追加 `OperationPlanningMode` 列，或参照 SetupTransitionRule 独立治理表 + 走 DomainSolveRequest 传入。**建列/建表均需 0号位 批准（DDL）**。
- **默认行为需 0号位 确认**：未配置 OperationPlanningMode 的工序，1号位 兜底处理？建议「有 Eligible Resource → 按 FINITE_RESOURCE；无 Resource 且未配置 → 数据异常登记，不静默跳过」（防 1号位 无资源误判为失败，也不改变 Routing 链）。

### 2. 配品（M101）分类 —— 业务确认入口，3号位 提供治理面

- 0号位 裁决：配品不默认归入有限资源，由业务确认（情况A 逐件操作→FINITE_RESOURCE 建配品人工01~N；情况B 齐套确认→UNCONSTRAINED）。
- 3号位 理解：业务确认是 OperationPlanningMode 的**数据来源之一**，3号位 提供维护入口（治理 API + 4号位 页面），具体值由业务拍板，**3号位 不替业务定**。
- 待 0号位 明确：配品分类确认的**责任人与流程**（业务如何把"情况A/B"落到 APS 配置）。

### 3. Transition / Transfer 治理模型 —— 参照 SetupTransitionRule，但建表走 DDL 变更申请

- 0号位 裁决：保留原方案，3号位 维护 Operation Transition Rule + Stage Transfer Time，单独进 DomainSolveRequest，不扩六块。
- 3号位 理解：完全对齐 SetupTransitionRule 先例（RuleSetVersionId 冻结 + 发布前冲突校验 + AuditLog + 权限码 + 种子）。SetupTransitionRule 当时经 0号位 批准 + DDL v5.1.8 补丁落地；本次同类操作 **3号位 提交 DDL 变更申请（v5.1.9 候选）**，不自决建表。
- 与现有 `StageOverlapParams`（跨 Stage 重叠+转移批量）语义不同，将在契约文档显式区分，防 1号位 混淆。

### 4. Calendar 装载 —— V1 闭环项，3号位 驱动 2号位

- 0号位 裁决：Resource Calendar 装载 V1 必须闭环（无真实 Calendar 则白班 10/夜班 5 无法表达）。
- 3号位 理解：`SchedulingContext.ResourceCalendars` 已建模，现 7x24 占位（SchedulingOrchestrator.cs:592）；装载服务落地属 2号位，3号位 提报契约视图缺口清单（MANUAL_STATION 资源、Calendar 班次、Eligibility 人工场景）。

---

## 四、残余待 0号位 补裁点

| # | 补裁点 | 背景 | 3号位 建议 |
|---|---|---|---|
| R4' | 规则默认 vs MES LagTime 事实优先级 | 0号位 未覆盖；MES 现硬编码 LagTime=0，1号位 已消费 LagTime | **MES LagTime 事实 > OperationTransitionRule 规则默认**（固定 Invariant，对齐 ManualETA 口径） |
| R6 | OperationPlanningMode 表达载体 | 复用 RoutingPlanningParam 加列 vs 独立治理表 | 独立治理表 + 走 DomainSolveRequest（与 Transition/Transfer 同通道，治理可追溯） |
| R7 | 未配置 OperationPlanningMode 的兜底 | 防 1号位 无资源误判失败、不改变 Routing 链 | 有资源→FINITE；无资源且未配置→数据异常登记，不静默跳过 |
| R8 | 配品分类确认流程 | 业务确认入口与责任人 | 3号位 提供治理 API + 4号位 页面；业务拍板；流程由 0号位 指定 |
| R9 | Calendar 装载正式生产数据源 | 0号位 裁 B（资源主数据侧创建） | 确认 MES/业务侧是否提供契约视图；无则 3号位 与 2号位 联合提报数据源方案 |

---

## 五、3号位 落地动作清单（按裁决修订）

> 排序依赖：R4'~R9 补裁结果 + DDL 批准。

| 阶段 | 动作 | 依赖 | 交付物 |
|---|---|---|---|
| S0 | 提交本回执 + R4'~R9 补裁请求 | — | 本文档 |
| S1 | DDL 变更申请（OperationPlanningMode + OperationTransitionRule + StageTransferTime） | R6 / 0号位 批准 | DDL 变更申请单（v5.1.9 候选） |
| S2 | 规则治理模型：OperationPlanningMode / OperationTransitionRule / StageTransferTime 的 Entity+Repo+Service+Validator | R1/R3/R6 | 参照 SetupTransitionRule 全套 |
| S3 | 权限码 + 治理 API 规格（CRUD + 种子）交付 4号位 | S2 | 3-4联调接口规格 |
| S4 | 消费契约草案：1号位 输入（OperationPlanningMode + TransitionRule + StageTransferTime 经 DomainSolveRequest） | R2/R4' / 1号位 | 契约草案（改 DomainSolveRequest，先更新契约文档） |
| S5 | 2号位 契约视图缺口清单（MANUAL_STATION / Calendar / Eligibility / 配品分类） | R5/R9 | 提报 2号位 清单 |
| S6 | 种子数据落库 dev（等待/转运默认规则 + 配品分类样例） | S2 / R8 | seed 脚本 |
| S7 | 集成测试：清洗（UNCONSTRAINED）、冷却（WAIT_ONLY）、配品（业务分类）、人工产能槽并行 | S4/S5/S6 | 测试用例 |

---

## 六、跨号位通知要点

- **2号位**：仅加载传递（OperationPlanningMode / TransitionRule / StageTransferTime / MANUAL_STATION 资源 / Calendar），不自动推断分类、不由 2号位 代码生成资源；测试 Seed 仅限测试阶段。
- **1号位**：消费 OperationPlanningMode 三态 + Transition/Transfer 规则（经 DomainSolveRequest）；无资源工序不再一律判失败，按模式消费；人工/设备无感知（同一 Resource 通道）。
- **4号位**：规则维护页面（OperationPlanningMode + Transition + Transfer）。
- **5号位**：无直接依赖（Stage 顺序权威仍在 StageDetail）。

---

*本回执基于 2026-09-21 工作树现状。R4'~R9 补裁 + DDL 批准后进入实施。*
