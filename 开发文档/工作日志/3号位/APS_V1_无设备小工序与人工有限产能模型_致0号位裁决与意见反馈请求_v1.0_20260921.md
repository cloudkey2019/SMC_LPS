# APS V1 无设备小工序与人工有限产能模型 —— 致 0号位 裁决与意见反馈请求

**版本**：v1.0
**日期**：2026-09-21
**致**：0号位
**来自**：3号位
**依据**：
- 《第六APS_V1_无设备小工序与人工有限产能模型方案论证及裁决.md》
- 0号位《未命名的Markdown文件 (49)(2).md》（补充裁决）
- 《APS_V1_无设备小工序与人工有限产能模型补充裁决_3号位回执_v1.0_20260921.md》

**请求性质**：0号位 补充裁决已收悉（R1/R2/R3/R5 已闭环）；本请求聚焦**补充裁决未覆盖 / 需进一步明确的 5 项补裁点 + 1 项意见反馈**，请 0号位 拍板后 3号位 进入实施。

---

## 一、请 0号位 裁决的补裁点（R4'~R9）

### R4'｜规则默认 vs MES LagTime 事实优先级

**背景**：`RoutingDependency.LagTime`（V1 仅 ES，默认 0）已被 1号位 Solver 消费；MES 侧《MES_APS_Routing_View.sql》硬编码 `LagTime=0`，即 MES 目前不提供等待事实。补充裁决确认 OperationTransitionRule 由 3号位 提供默认等待，但**未明确"规则默认"与"MES 事实"的合并优先级**。

**候选**：
- **A（建议）**：`MES LagTime 事实 > OperationTransitionRule 规则默认`，作为固定 Invariant（对齐 `ManualETA > ERPETA > DefaultLT` 口径）——MES 后续补齐事实不返工。
- B：规则默认完全接管，MES LagTime 不再消费——1号位 需改消费逻辑，MES 补事实后无路径。

### R6｜OperationPlanningMode 表达载体

**背景**：补充裁决新增 OperationPlanningMode（FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY），属 APS 规划属性、3号位 治理。落地载体未明确。

**候选**：
- **A（建议）**：独立治理表（参照 SetupTransitionRule，RuleSetVersionId 冻结 + 冲突校验 + 审计），经 DomainSolveRequest 传入 1号位——治理可追溯，与 Transition/Transfer 同通道。
- B：复用 `RoutingPlanningParam`（APS_LOCAL 规划参数表）追加列——少一张表，但混合规划参数与能力分类语义。

### R7｜未配置 OperationPlanningMode 的兜底行为

**背景**：1号位 提出"无 Eligible Resource 目前 = 失败/数据异常"。补充裁决要求非瓶颈/等待工序进入排程链，但**未配置模式的工序（尤其无资源）如何处理未明确**。

**候选**：
- **A（建议）**：有 Eligible Resource → 按 FINITE_RESOURCE；无 Resource 且未配置 → 数据异常登记（不静默跳过、不改变 Routing 链），由 3号位 配置补齐后再排。
- B：无资源未配置一律按 UNCONSTRAINED 兜底——有静默放大产能风险。

### R8｜配品（M101）分类的业务确认流程

**背景**：补充裁决裁"配品分类由业务确认，不自动推断"（情况A 逐件操作→FINITE_RESOURCE；情况B 齐套确认→UNCONSTRAINED）。3号位 提供治理 API + 4号位 页面，但**确认责任人与流程未指定**。

**候选**：
- **A（建议）**：由 PMC/工艺归口提供《配品工序分类清单》（工序→分类），3号位 据此落 OperationPlanningMode seed，业务后续可改。
- B：仅建空治理入口，业务自行维护——V1 排程配品会缺分类阻塞。

### R9｜Resource Calendar 正式生产数据源

**背景**：补充裁决裁 Calendar 装载 V1 必须闭环；人工资源槽正式生产方式为 B（资源主数据侧创建，2号位 仅加载，测试阶段允许 2号位 临时 Seed）。**正式生产下 MES/业务侧是否提供契约视图（MANUAL_STATION 资源 + 班次 Calendar）未确认**。

**候选**：
- **A（建议）**：3号位 与 2号位 联合核查 MES 契约视图供给情况，若缺则向 0号位 提报数据源方案（业务主数据平台 / 手工维护）。
- B：默认 MES 提供，不核查——V1 联调期可能因视图缺失返工。

---

## 二、请 0号位 意见反馈的 1 项（F1）

### F1｜等待/转运规则与现有 StageOverlapParams 的关系确认

**背景**：`FrozenStrategySnapshot.SolverStrategyBlock.StageOverlapParams`（AllowOverlap / TransferBatchQty / ThresholdQty / ThresholdPercent）为"跨 Stage 重叠 + 转移批量"；新增 StageTransferTime 为"跨大工艺固定转运时长"，语义不同。

**请 0号位 确认**：
1. 两者**并存不冲突**（一个管重叠策略、一个管转运时长），无需合并？
2. 若并存，1号位 消费时**优先级/叠加规则**由 3号位 在契约文档定义，0号位 是否有既定口径？

> 此项不阻塞实施，仅需 0号位 书面确认口径，避免 1号位 混淆消费。

---

## 三、裁决结果接收方式

- 0号位 可**逐项回复**（R4'~R9 + F1），或按现有惯例统一回复至 3号位；
- 3号位 收到裁决后：更新回执文档 → 进入 S1（DDL 变更申请 v5.1.9 候选）→ S2~S7 实施；
- 涉及 2号位/1号位 的裁决（R9、F1）将同步转达对应号位。

---

## 四、汇总

| # | 事项 | 类型 | 3号位 建议 |
|---|---|---|---|
| R4' | LagTime 事实 vs 规则默认优先级 | 裁决 | A（MES 事实优先） |
| R6 | OperationPlanningMode 载体 | 裁决 | A（独立治理表 + DomainSolveRequest） |
| R7 | 未配置模式兜底 | 裁决 | A（数据异常登记，不静默跳过） |
| R8 | 配品分类确认流程 | 裁决 | A（PMC/工艺归口清单） |
| R9 | Calendar 正式数据源 | 裁决 | A（联合 2号位 核查） |
| F1 | StageOverlap vs StageTransfer 关系 | 意见反馈 | 书面确认并存口径 |

---

*请 0号位 裁决/反馈后，3号位 进入实施。*
