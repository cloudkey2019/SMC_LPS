# APS V1 无设备小工序与人工有限产能模型 —— 等待/转运规则接收回执（致 0号位）

**版本**：v2.0（修正版，**supersedes v1.0**《…建表通道澄清_3号位致0号位_v1.0_20260921.md》）
**日期**：2026-09-22
**发送人**：3号位（规则参数治理 / S1 DDL 申请 Owner）
**接收人**：0号位（PM / 最终裁决）
**抄送**：1号位（消费）、2号位（落库）、4号位（前端，备查）
**性质**：**接收回执**——接受《1号位、3号位问题统一回复意见(2026-09-22)》口径：等待/转运表**本轮不新增**，撤销 S1 建表诉求
**触发**：2026-09-22 《统一回复意见》五.2 / 六 / 七 对 3号位 澄清函的修正
**关联**：v1.0《…建表通道澄清》(2026-09-21)、《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》、S1《DDL变更申请_v5.1.9候选》、冻结 DDL v5.1.8.2

---

## 〇、修订说明

1. **前提修正**（《统一回复意见》**五.2**）：Setup 非工序转换模型——冻结 Setup 模型为「当前 Operation + Resource + FromMaterial + ToMaterial」决定 Setup 时间，非 FromOperation → ToOperation；**不能因提出表名就认为冻结已规定 OperationTransitionRule / StageTransferTime 表**。
2. **建表诉求撤销**：本轮**不新增** OperationTransitionRule / StageTransferTime 表；「如需补充应作为独立能力确认物理承载」为**未来单独立项**通道，不纳入本轮 S1。
3. **配品请裁撤回**（《统一回复意见》**六**）：配品 M101 不再单独讨论，不形成独立业务模型/裁决项。
4. 本修正版为 v2.0 未发出前的就地修正——原 v2.0 问题②「建议新增治理表」与统一回复「本轮不新增」相左，已删除。

---

## 一、现状澄清（Setup 与等待/转运的边界）

### 1. Setup：无争议，不在本函范围

- 冻结 Setup 模型 = 当前 Operation + Resource + FromMaterial + ToMaterial 决定 Setup 时间；
- Setup 物理承载 = **SetupTransitionRule 已落地表**（RuleSetVersionId 冻结 + 发布前冲突校验 + AuditLog + 权限码），V1 冻结 DDL v5.1.8.2 作为 Resource Timeline 约束收录；
- 3号位 Setup 治理正常推进，**不引入 OperationTransitionRule 承载 Setup**。

### 2. 等待/转运：另外的业务事实，本轮不新增承载

- 工序间等待 / 自然冷却 / 阶段转运 属于**另外的业务事实**，不属于 Setup 模型；
- 冻结 DDL v5.1.8.2 未收录两表，且《统一回复意见》「本轮需要补齐」清单（Task.SetupSource / ResourceCalendar 物理承载 / ScopeJson 版本化 / 必要接口同步）**不含**两表；
- 依《统一回复意见》七「不得因示例场景扩大业务范围」：无设备小工序为示例，**本轮不新增**等待/转运表。

---

## 二、结论（3号位 接收口径）

1. **OperationTransitionRule / StageTransferTime：本轮不新增、不进入 S1 DDL 申请**；
2. 等待/转运业务事实如需表达，由 **OperationPlanningMode 三态（FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY）** 兜底 + 1号位 默认 0 消费（对齐 R4' 兜底口径）；
3. 未来如业务确需，作为**独立能力单独立项**确认物理承载（不在本轮范围）。

---

## 三、S1 申请单定稿口径

按冻结 DDL v5.1.8.2 对齐：

- `RoutingOperation` 加 `OperationPlanningMode` 列（NVARCHAR(32) NOT NULL DEFAULT 'FINITE_RESOURCE' + 三态 CHECK）；
- **撤销** OperationTransitionRule / StageTransferTime 建表诉求；
- 3号位 据此将 S1 申请单（v5.1.9 候选）修订至终版并提报 DDL 批准流程（2号位 落库）。

---

## 四、请求

1. 0号位 确认本接收口径（本轮不新增两表、三态兜底）；
2. 确认后 3号位 当日修订 S1 至终版并提报 DDL 批准。

**发送人**：3号位 ｜ **日期**：2026-09-22
**待办**：① 0号位 确认接收口径 ② 3号位 S1 终版 ③ 2号位 落库 → S2~S7
