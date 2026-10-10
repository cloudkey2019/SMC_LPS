# APS V1 无设备小工序与人工有限产能模型 —— 最终裁决 3号位 接收回执

**版本**：v1.0
**日期**：2026-09-21
**回执对象**：0号位《未命名的Markdown文件 (51)(1).md》（下称《最终裁决》）
**回执角色**：3号位
**前置**：
- 《APS_V1_无设备小工序与人工有限产能模型裁决_3号位影响评估_v1.0_20260921.md》（R1~R5）
- 《APS_V1_无设备小工序与人工有限产能模型_致0号位裁决与意见反馈请求_v1.0_20260921.md》（R4'~R9 + F1）
- 《APS_V1_无设备小工序与人工有限产能模型补充裁决_3号位回执_v1.0_20260921.md》
- 1号位 最新回执（《最终裁决》引述：不剥离 UNCONSTRAINED 工序）

---

## 一、收悉确认（总述）

《最终裁决》已通读，**全部接受，无残余补裁点**。核心结论四项：

1. **R6**：OperationPlanningMode = **APS 工艺规划属性（Operation Planning Attribute）**，**非规则引擎、非独立治理表**（否 3号位 独立治理表建议，定为过度设计）；载体定 **`OperationPlanningParam` / `APS Operation Extension`**（OperationCode + PlanningMode + EffectiveVersion）；责任：3号位 维护、2号位 加载、1号位 消费。
2. **R4'**：V1 **不建设 MES LagTime 事实治理**；当前 `LagTime=0` 非有效事实、**不覆盖 APS 规则**；链 = 有效 Routing LagTime → OperationTransitionRule → 默认 0。
3. **R5/R9（重点修正）**：**Resource 来源 ≠ Calendar 来源**。设备 Resource 可来自 MES/EAM ETL；**Resource Calendar 由 APS 资源能力维护**（工作时间/班次/停机/可用窗口），不来自 MES/Resource ETL；**人工 Resource Slot 由 APS 资源体系维护**（FINISH_SLOT_01~N + Calendar 班次）。
4. **R7**：3号位 **A 方案 批准**——有 ResourceEligibility → 默认 `FINITE_RESOURCE`；无 Resource → 必须配置 `UNCONSTRAINED` 或 `WAIT_ONLY`，否则**异常**；**禁止「无资源自动无限产能」**（会掩盖主数据错误）。

---

## 二、裁决对照回执（R4'~R9 + F1）

| # | 事项 | 0号位 最终裁决 | 3号位 回执 | 状态 |
|---|---|---|---|---|
| R4' | MES LagTime vs 规则默认 | V1 不建 MES 等待事实治理；LagTime=0 非有效事实不覆盖 APS 规则；未来 MES 提供真实 LagTime 后可消费 | 确认。**撤回**原建议 A（MES 事实优先建模）；按「有效 Routing LagTime → OperationTransitionRule → 默认 0」处理，MES 侧 LagTime=0 视同无事实 | ✅ 已裁决 |
| R6 | OperationPlanningMode 载体 | APS 工艺规划属性，非规则引擎、非独立治理表；建议 `OperationPlanningParam` / `APS Operation Extension`（OperationCode + PlanningMode + EffectiveVersion） | 确认。**放弃独立治理表**；载体定 `OperationPlanningParam`；3号位 维护 / 2号位 加载 / 1号位 消费 | ✅ 已裁决 / ⏳ 载体 DDL 待批 |
| R7 | 未配置模式兜底 | **A 方案 批准**：有 eligibility→FINITE_RESOURCE；无资源→必须 UNCONSTRAINED 或 WAIT_ONLY 否则异常；禁止无资源自动无限产能 | 确认采纳；无资源异常登记不静默跳过、不改变 Routing 链 | ✅ 已裁决 |
| R8 | 配品（M101）分类 | **不再单独展开专项**；统一原则所有 Operation 按实际制造能力确定 PlanningMode | 确认。**撤回**原 R8 专项（PMC 清单流程）设计；配品分类 = OperationPlanningMode 数据来源之一，3号位 维护入口、业务拍板、不单列流程 | ✅ 已裁决 |
| R9 | Calendar 正式数据源 | **重点修正**：Resource 来源 ≠ Calendar 来源；设备 Resource 可 MES/EAM ETL；Calendar = APS 资源能力维护；人工 Resource Slot = APS 资源体系维护 | 确认。**撤回**原 R9 建议（MES 提供 MANUAL_STATION+Calendar）；改「MES 提供可用 Resource 事实；APS 负责 Calendar + 人工 Slot」；2号位 只加载 | ✅ 已裁决 / ⏳ Calendar 装载闭环（2号位） |
| R5 | 人工产能槽 | 确认 B 为正式生产方式（APS 资源主数据侧创建 FINISH_SLOT_01~N + Calendar 班次；测试 Seed 临时不转正式） | 确认不变；3号位 不生成资源不维护员工；2号位 只加载、1号位 只消费 | ✅ 已裁决 |
| R1/R2 | Transition/Transfer 通道 | 3号位 维护 Operation Transition Rule + Stage Transfer Time；经 **DomainSolveRequest**；**不进 Frozen 六块**；**不做复杂规则引擎**（简化） | 确认；对齐 SetupTransitionRule 治理先例（RuleSetVersionId 冻结 + 冲突校验 + 审计 + 权限码）；DDL 变更申请 v5.1.9 候选 | ✅ 已裁决 / ⏳ DDL 待批 |
| F1 | StageOverlapParams 并存 | 0号位 未逐项点名；§八 隐含并存（一个重叠+转移批量、一个固定转运时长） | 按**并存**处理；优先级/叠加规则由 3号位 在契约文档定义并呈 0号位 备案，防 1号位 混淆 | ✅ 口径自定（登记） |

---

## 三、对 3号位 原建议的修正记录（3 处否/修正 + 1 处撤销专项）

| 原建议 | 裁决结果 | 修正后口径 |
|---|---|---|
| R6 独立治理表（参照 SetupTransitionRule） | **否**（过度设计） | `OperationPlanningParam` / `APS Operation Extension` 载体 |
| R4' MES 事实 > 规则默认（固定 Invariant） | **否**（V1 不建设事实治理） | 当前 LagTime=0 不覆盖；未来真实事实可接入 |
| R9 MES 提供 MANUAL_STATION 资源 + Calendar | **修正**（Resource/Calendar 来源分离） | MES 提供 Resource 事实；Calendar + 人工 Slot 由 APS 维护 |
| R8 配品分类专项（PMC/工艺归口清单流程） | **撤销专项**（统一原则） | 配品 = OperationPlanningMode 数据来源之一，不单列 |

---

## 四、架构原则（0号位 建议写入冻结 —— 登记）

> MES Routing 描述「生产要做什么」；APS Planning 属性描述「APS 如何计划」；Resource 描述「由什么能力执行」；Calendar 描述「什么时候可以执行」。四者不混。

四层映射：

```
MES Routing          → Operation
APS Planning Attribute → OperationPlanningMode
APS Resource Model   → Resource + Eligibility
APS Calendar Model   → Resource Availability
```

→ 3号位 随 S4 契约文档修订一并**登记进冻结文档修改指导**（不扩冻结六块 Snapshot，仅新增 DomainSolveRequest 通道）。

---

## 五、3号位 落地动作清单（按最终裁决修订）

> 排序依赖：DDL 批准 + 2号位 契约视图缺口核查。

| 阶段 | 动作 | 依赖 | 交付物 |
|---|---|---|---|
| S0 | 提交本接收回执 | — | 本文档 |
| S1 | **DDL 变更申请**（OperationPlanningParam 载体 + OperationTransitionRule + StageTransferTime） | 0号位 批准 | DDL 变更申请单（v5.1.9 候选） |
| S2 | 规则治理模型：OperationPlanningParam / OperationTransitionRule / StageTransferTime（Entity+Repo+Service+Validator+审计+权限码） | S1 | 参照 SetupTransitionRule 全套 |
| S3 | 治理 API 规格（CRUD + 种子）交付 4号位 | S2 | 3-4联调接口规格 |
| S4 | 消费契约草案（DomainSolveRequest 扩展 + 契约文档修订 + 架构原则登记 + StageOverlap 显式区分） | R2/R4' / 1号位 | 契约草案（改 DomainSolveRequest 前先更新契约文档，红线 #5） |
| S5 | 2号位 契约视图缺口清单（Resource 事实 / Calendar 装载闭环 / Eligibility / MANUAL_STATION） | R5/R9 | 提报 2号位 清单 |
| S6 | 种子数据落库 dev（等待/转运默认规则 + 配品分类样例） | S2 / R8 | seed 脚本（`-f 65001`） |
| S7 | 集成测试：清洗（UNCONSTRAINED）、冷却（WAIT_ONLY）、配品（业务分类）、人工产能槽并行 | S4/S5/S6 | 测试用例 |

---

## 六、跨号位通知要点（修订）

- **2号位**：仅加载传递（OperationPlanningParam / Transition / Transfer / MANUAL_STATION Resource / Calendar），不自动推断分类、不生成资源；**Calendar 装载服务 = 2号位 V1 闭环项**；测试 Seed 仅限测试阶段。
- **1号位**：消费三态（FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY）+ Transition/Transfer（经 DomainSolveRequest）；**不剥离 UNCONSTRAINED 工序**（保留工序链、只占时间不占资源）；WAIT_ONLY 只增等待时间；无资源工序按模式消费、不再一律判失败。
- **4号位**：规则维护页面（OperationPlanningMode + OperationTransitionRule + StageTransferTime）。
- **5号位**：无直接依赖（Stage 顺序权威仍在 StageDetail）。

---

## 七、回执确认表

| 问 | 答 |
|---|---|
| 裁决是否全部接受 | 是，无残余补裁点 |
| 需 0号位 再裁事项 | 无（F1 按并存口径，3号位 契约文档定义并备案） |
| 下一步 | S1 DDL 变更申请（v5.1.9 候选）→ S2~S7 按清单实施 |

---

*本回执基于 2026-09-21 工作树现状。DDL 批准 + 2号位 缺口核查回执后进入实施。*
