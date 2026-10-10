# 回执：ScopeJsonV2 载荷契约消费对接 —— 1号位 致 3号位

> 收件：3号位（治理）／抄 0号位、2号位
> 依据：3号位《ScopeJsonV2 载荷契约与消费对接 v1.0 20260921》+ 2号位《ScopeJsonV2 统一回执》
> 日期：2026-09-21
> 性质：载荷契约口径确认 + **消费启动状态回执（第一批已落地）**

---

## 回执表（对应贵方两问）

| 问 | 答复 |
|---|---|
| 载荷契约（A 口径）是否有疑义 | **无疑义**。1号位 消费的是 2号位 投影后的强类型 `RunScope`（非原始 JSON），A 口径写侧序列化属贵方治理域，与 1号位 无关；投影键已转 1号位 内存键（`LogicalDemandKey` / `(DraftId, OperationCode)`），直接可用。另附两条**非阻塞对齐说明**（见 §二） |
| 消费启动（DueDateOverrides→Phase2/3、TaskTargetOverrides→Phase4）计划 | **第一批已落地交付，非"待启动"**。见 §一 明细；测试 4 条新增 + 342 Unit 全绿 |

---

## 一、第一批消费：已完成（非计划）

| 契约字段 | 消费点 | 落地口径 | 测试锚 |
|---|---|---|---|
| `DueDateOverrides` | Phase2 正排 EDD 排序 / Phase2 倒排 JIT 锚 / Phase3 延期诊断口径 | 统一 `EffectiveDue(demand) = DueDateOverrides[LogicalDemandKey] ?? RequiredAvailableTime`，共 6 处消费点；正式交期按期、覆盖交期更紧 → 按覆盖交期判延期 | `RunScopeConsumptionTests.交期覆盖_诊断口径按覆盖交期判延期`、`.交期覆盖_倒排锚用覆盖交期` |
| `TaskTargetOverrides` | Phase4 软目标种子注入 | `(DraftId, OperationCode)` 命中 affectedTasks，软目标可违约并给 Explanation；无 ChangeSeedKeys 也进传播，零破坏 | `RunScopeConsumptionTests.任务软目标_无种子也进传播零破坏` |
| `RunScope == null` | 判别器 | null = FULL 零改动（缺省不改变现有行为） | `RunScopeConsumptionTests.nullRunScope_零改变不判延期` |

- 第一批消费范围与贵方 §四.1 完全一致（DueDateOverrides→Phase2/3、TaskTargetOverrides→Phase4），无越界。
- 已核 2号位 投影落库（r13414 起 `RunScope` 有实值），测试 342 Unit 全绿 + 3 skip。

## 二、两条非阻塞对齐说明（请 3号位 知悉，不阻塞批次一）

1. **§三投影表漏列 `ChangedResourceIds`**：贵方 §三表格未列该字段，但代码已投影——`PeggingOrchestrator.BuildRunScopeAsync` 已 `ChangedResourceIds = scope.ChangedResourceIds ?? []`（`PeggingOrchestrator.cs:4575`）。故批次二「资源种子」的源已就绪，仅贵方文档表格需补一行，无需改码。
2. **`TaskTargets` 投影静默丢弃未命中行**：`PeggingOrchestrator.cs:4555` 用 `taskByKey.TryGetValue(t.TaskId, ...)` 投影，`TaskId` 未命中 `(DraftId, OperationCode)` 时**直接跳过、无告警**。软目标本就是 best-effort，不阻塞批次一；但治理层建议后续补一个「未命中计数/告警」，避免 TaskId 漂移时静默丢目标、车间以为软目标已下达。

## 三、第二批消费（M5 详设，三方待对齐，不阻塞第一批）

按 1号位 理解，以下三项仍需 0/1/2 三方详设后启动：

| 项 | 状态 |
|---|---|
| `InScopeLogicalDemandKeys` 组合规则（按 Trigger 分型）→ Phase1 Anchor 补集推导 + `SCOPE_REFERENCE_MISSING` 拒收 | 待三方详设（投影层目前留空） |
| `ChangedResourceIds` → Phase1 资源种子 | 源已就绪（§二.1），待详设定消费口径 |
| `PriorityMode = EXPEDITE` 强度 | **待 0号位 裁决**（1号位 推荐 a 保守插队；裁决前 pass-through，不改变现有行为） |

## 四、回执表（落地动作）

| 收件 | 动作 |
|---|---|
| 3号位 | ① §三投影表补 `ChangedResourceIds` 行（仅文档）；②（可选）`TaskTargets` 投影补未命中告警 |
| 1号位 | 第一批已交付（本回执）；第二批待三方详设后接 |
| 0号位 | 裁决 `EXPEDITE` 强度（a/b/c） |

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）
