# APS V1 无设备小工序与人工有限产能模型 —— 等待/转运规则建表通道澄清（致 0号位）

**版本**：v1.0
**日期**：2026-09-21
**发送人**：3号位（规则参数治理 / S1 DDL 申请 Owner）
**接收人**：0号位（PM / 最终裁决）
**抄送**：1号位（消费）、2号位（落库）、4号位（前端，备查）
**性质**：**裁决澄清函**——描述问题成因，请 0号位 裁决建表通道
**触发**：2026-09-21 文档更新通读发现「补充裁决语义已批准 vs 冻结 DDL 物理未收录」落差
**关联**：《APS_V1_无设备小工序与人工有限产能模型_DDL变更申请_v5.1.9候选_v1.0_20260921.md》（S1）、《…_DDL批准说明与确认请求_3号位致0号位_v1.0_20260921.md》、《…_最终裁决_3号位接收回执_v1.0_20260921.md》、《…_0号位对3号位修改与裁决_通读报告_v1.0_20260921.md》

---

## 〇、一句话

**0号位 补充裁决 R1/R2 已批准「等待/转运规则要建、3号位 维护、独立进 DomainSolveRequest」，但同日冻结的 DDL v5.1.8.2 只落了 `RoutingOperation.OperationPlanningMode` 列、未收录 OperationTransitionRule / StageTransferTime 两表的物理承载——「语义已批、物理缺失」落差，导致 S1 申请单形态与 S2~S7 落地链起点无法确定。请 0号位 裁决建表通道（新表 / 并入 RuleSet / V1 延后）。**

---

## 一、问题现象

冻结 DDL v5.1.8.2（2026-09-21 文档更新发布）对无设备小工序/人工有限产能的物理落地为：

| 承载项 | 冻结 DDL 是否收录 | 备注 |
|---|---|---|
| `RoutingOperation.OperationPlanningMode` 列 | ✅ 收录 | NVARCHAR(32) NOT NULL DEFAULT 'FINITE_RESOURCE' + 三态 CHECK |
| `ResourceType = HUMAN` | ✅ 收录 | 复用 Resource + ResourceCalendar + OperationResourceEligibility |
| Setup 规则 | ✅ 收录 | 作为 Resource Timeline 约束，走现有 Rule/Parameter 体系 |
| **OperationTransitionRule（工序间等待/转运）** | ❌ **未收录** | — |
| **StageTransferTime（大工艺间转运）** | ❌ **未收录** | — |

同时冻结 DDL 的「不新增表」清单（OperationSupply / OperationPegging / CandidateInput / ScenarioInput / SetupRuntime）**也未包含**这两张表——即它们既不在「新增」清单、也不在「不新增」清单，处于**真空地带**，3号位 无法从冻结文档判定承载通道。

## 二、问题成因（根因链）

### 成因 1：语义层已批准（补充裁决 R1/R2）

0号位《未命名(49)(2).md》补充裁决明确：

| 裁决点 | 0号位 结论 | 3号位 回执状态 |
|---|---|---|
| R1 是否新增治理表 | **保留原方案**：3号位 维护；单独进入 DomainSolveRequest，**参照 SetupTransitionRule 方式** | ✅ 方向确认 / ⏳ DDL 待批 |
| R2 如何进入 1号位 | **独立 DomainSolveRequest，不扩 Frozen 六块** | ✅ 已裁决 |

→ 语义层：**这两类规则「存在、3号位 治理、独立通道」已被 0号位 批准**，且明确要求参照 SetupTransitionRule（该规则=独立治理表 + RuleSetVersionId 冻结 + 发布前冲突校验 + AuditLog + 权限码）。

### 成因 2：物理层未收录（冻结 DDL v5.1.8.2）

同一轮冻结的物理 DDL 只落地了 OperationPlanningMode 列（覆盖 R6 载体），**未给等待/转运规则落地承载表**，也未在「不新增表」清单中显式豁免它们。Setup 规则的承载被明确归入「现有 Rule/Parameter 体系」，但**文档未声明等待/转运规则是否同走该体系**。

### 成因 3：落差导致 S1 无法定稿

- S1 申请单（v5.1.9 候选）按「**新表**」提交（OperationTransitionRule / StageTransferTime 两张 RuleSetVersionId 冻结表）；
- 若 0号位 本意是「**并入现有 RuleSet 通道**」，则 S1 的过渡/转运建表部分需撤回，只保留与冻结 DDL 对齐的列治理口径；
- 3号位 **不能自行判定**「新表 or 并入」——红线「DB schema 变更 2号位 专属 + 0号位 授权」约束下，建表/不建表均须 0号位 拍板。

### 影响链

通道未定 → S1 申请单形态未定 → 2号位 无法落库 → S2（治理模型 Entity+Repo+Service）无法起 → S4（1号位 消费契约，改 DomainSolveRequest 前先更新契约文档·红线5）后延 → **无设备小工序整条落地链在起点阻塞**。

---

## 三、请 0号位 裁决（建表通道）

| 选项 | 方案 | 3号位 评估 |
|---|---|---|
| **A. 新增治理表**（参照 SetupTransitionRule 先例） | OperationTransitionRule / StageTransferTime 独立治理表，RuleSetVersionId 冻结 + 发布前冲突校验 + AuditLog + 权限码 | **3号位 建议**——治理可追溯、对齐既有范式、与补充裁决 R1「参照 SetupTransitionRule」原文一致；S1 申请单（v5.1.9 候选）即终版，可直接进入 DDL 批准流程 |
| B. 并入现有 RuleSet/ParameterSet 冻结通道 | 不新增表，等待/转运规则作为规则行承载于现有规则版本体系 | 零新表，但 R1 原文「参照 SetupTransitionRule 方式」指向独立治理表；需 0号位 确认两种规则的维度（部门/前工序/后工序、起止 Stage）能在现有通道无损表达 |
| C. V1 延后 | 本轮仅 OperationPlanningMode 列兜底；等待/转运默认 0 由 1号位 消费（对齐 R4' 兜底口径） | 最小改动，但缺失等待型工序（自然冷却）/转运真实时长，业务语义打折，1号位 消费降级 |

**裁决建议**：选 **A**。批准后 3号位 按 S1 申请单推进 DDL 批准 → 2号位 落库 → S2~S7 链启动。

## 四、附带请裁（同线程，1 项）

**配品（M101）分类确认的责任人与流程**（最终裁决 R8「配品不单独专项、按实际制造能力定 PlanningMode」）：业务如何把「情况A 逐件操作→FINITE_RESOURCE / 情况B 齐套确认→UNCONSTRAINED」落到 APS 配置——3号位 提供治理 API + 4号位 页面，但**责任人/流程由 0号位 指定**（3号位 不替业务定值）。可与 §三 一并裁决。

---

## 请求

1. **§三 建表通道裁决**（建议 A）——优先项，直接解锁 S1~S7；
2. §四 配品流程指定——随附裁决即可；
3. 裁决后 3号位 当日修订 S1 申请单至终版并提报 DDL 批准。

**发送人**：3号位 ｜ **日期**：2026-09-21
**待办**：① 0号位 裁决建表通道 ② 3号位 S1 申请单定稿 ③ 2号位 落库 → S2~S7
