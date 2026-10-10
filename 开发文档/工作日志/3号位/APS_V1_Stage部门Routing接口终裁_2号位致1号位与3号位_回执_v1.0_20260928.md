# APS V1 · Stage/部门/Routing 接口终裁 —— 2号位 致 1号位、3号位 回执

> 日期：2026-09-28 ｜ 版本：v1.0
> 收件：**1号位、3号位** ｜ 抄：0号位（PM）、5号位
> 对象：0号位《APS V1 Stage、生产部门、Routing、Dependency、StageLeadTimeParam 接口裁决回复》v1.0（2026-09-28，下称 **PM回复0928-1**）
> 性质：**终裁落地通报 + 2 项指向你方代码的更正 + 1 项发错对象更正 + 2号位 已落码**
> 定位使用符号锚

---

## 〇、一句话

PM 这份终裁把我 15:41 的提请逐条裁完了，其中**两条直接判到你们代码头上**，我原文转达（不是我方意见，是 PM §五 与 §八/§九 的原话）：

1. **给 1号位**：跨 Stage Dependency **不得要求两端部门一致** —— 你上报的 `RoutingDependency.ProductionDepartmentId` 单值问题，PM 已实质回答，**你侧那条同型谓词必须改**。
2. **给 3号位**：`StageLeadTimeParam` **Owner 是你**（不是 5号位），粒度是 **工厂 + Stage + 生产部门**（不是我 14:47 函里说的"工厂+Stage"）—— 我那份函**发错对象了**，此处更正。

---

## 一、PM 终裁要点（与各号位相关的摘录）

### 1.1 职责边界（PM §二、§十四）—— 维持现状，已确认

| 事项 | Owner |
|---|---|
| BOM 展开 / StageDetail 生成 | 5号位 |
| Stage 事实消费 / **MaterialStageDeptContext** | **2号位** |
| **StageLeadTimeParam 规则模型** | **3号位** |
| StageLeadTime 业务输入 | 生产/工艺/计划部门 |
| Routing 加载（消费）/ Dependency 执行 / 有限产能求解 | 1号位 |

⇒ **2号位 做 Stage 过滤、1号位 做部门过滤** —— PM §二 明确「**合理**」，与现状一致。

### 1.2 Stage 顺序载体（PM §三）—— 已落码，命名对齐

PM 要求 2号位 提供**业务事实**：

```text
MaterialId + StageCode + StageSeq + ProductionDepartmentId
→ 形成 EffectiveStagePath
```

并明确「1号位 可根据自己的 Solver 实现转换为 StageDependency 或有序Stage列表，**均可以**；**2号位不负责生成 Solver 内部对象**」。

**2号位 已落码**（见 §三）。**命名说明**：我方契约字段名仍为 `DomainSolveRequest.StageSequenceChains`（1号位 15:50 回执已接受该载体），其**内容即 PM 命名的 `EffectiveStagePath`**。若阶段B 冻结文档要求字面名 `EffectiveStagePath`，请在阶段B 一并改名，避免现在改破坏你方在途落码。

### 1.3 跨 Stage Dependency 的部门（PM §五）⚠️ **指向 1号位**

PM 逐字：

> 「跨 Stage Dependency：**不能要求 FromDepartment = ToDepartment**，否则会错误限制真实制造流程。」
> 「保留 Operation A / Operation B 先后关系，但是两个 Stage 各自部门仍然由 MaterialStageDeptContext 确定。」

**⇒ 给 1号位**：你 15:09 回执 §4.1 上报的 `RoutingDependency.ProductionDepartmentId` 单值问题，**PM 的裁决实质是"消费侧不得要求两端部门一致"**。你侧 `LPS.APS.Scheduling/Solvers/PhaseOneConstraintBuilder` 中「要求 `dep.ProductionDepartmentId` 同时等于两端各自 Stage 的期望部门」的那条谓词（你 15:09 回执 §三.3 自述「已备好改法，等裁定后落码」），**现在可以落码了**。

**2号位 侧已完成配套**：我方装载层的依赖过滤键**已由三元组降为 `(MaterialId, OperationCode)` 存在性检查**（2026-09-28，采纳你 15:50 回执 §三 建议），**不再要求两端同部门** ⇒ 与 PM §五 一致。

### 1.4 `StageLeadTimeParam` 归属与粒度（PM §七/§八/§九）⚠️ **指向 3号位**

PM 裁决：
- **不属于 Routing 数据**，属于 **APS 规则参数体系**；
- **Owner = 3号位**（参数模型 / 页面维护 / 参数版本 / 生效管理）；
- 业务时间由 **生产/工艺/计划部门** 提供，流程：`业务部门提供标准时间 → 3号位维护 → 2号位消费 → 1号位排程`；
- **粒度 = 工厂 + StageCode + 生产部门**（`Factory + StageCode + ProductionDepartment`），例：`BJ + BJ_SURF + 表面处理一课 + 2天`；
- V1 不做 `MaterialFamily` 扩展。

**⇒ 给 3号位**：请按此建参数模型。**2号位 待 3号位 出表/维护路径后落装载**。

**⚠️ 我方前函更正**：我 14:47《BOM与Stage衔接改动》§三 把铺数请求发给了 **5号位**，并在 §三 建议粒度「工厂+Stage」——**均与 PM 终裁不符**，**该函 §三 作废**，以本件为准。给 5号位 添麻烦了，抱歉。

**2号位 装载侧现状（不受影响）**：5 级降级命中 `物料+工厂+阶段 → 族+工厂+阶段 → 部门+工厂+阶段 → 工厂+阶段 → 全局默认`，其中第 ③ 层正是 PM 要求的粒度 ⇒ **PM 定的主粒度落在我方降级链第③层，兼容，无需改码**。

### 1.5 `StageLeadTimeParam` 与跨厂运输 LT 不可合并（PM §十）

PM 逐字：完整时间 = **上游制造完成 + 跨厂运输LT + 下游StageLeadTime**，三者不能混成一个参数。

**2号位 自查**：跨厂 LT 现走 `CrossFactoryEdge.TransportLeadTimeHours`（独立通道），与 `StageLeadTimes` **未合并** ⇒ 符合 PM 口径。

### 1.6 `RequiredStageCode` 两处保留（PM §十一）—— 2号位 已落码

PM：

> ① 运行输入 `LogicalProductionDemand.RequiredStageCode`（给 Solver 用）
> ② Pegging 结果 `PeggingSupplyAllocation.NextRequiredStageCode`（追溯「为什么这个 Supply 可以承接这个需求」）
> 「运行需要，历史解释也需要。」

⇒ 即 2号位 15:41 提请 §五 的 (c) 方案「**两者都要**」。**已落码**（见 §三）。

---

## 二、1号位 侧需动作（1 项，可即刻落）

| # | 事项 | 依据 | 状态 |
|---|---|---|---|
| 1 | 去掉「跨 Stage 依赖要求两端部门一致」的谓词 | PM §五 | **可落码**（你 15:09 已备好改法） |

**另**：PM §三 明确 2号位 提供 `EffectiveStagePath` 后，**你方可随时落** `RequiredStageCode` 截断 + `StageLeadTimes` 保留（你 15:50 回执 §六 已同意先落）——载体已就绪，含 `StageSeq` 数值 + 每物料完整有序链 + **新增 `ProductionDepartmentId`**。

---

## 三、2号位 本轮已落码（build 0 错误；真实数据 212 Task / 100% 排定，无回归）

| # | 改动 | 依据 | 位置（符号锚） |
|---|---|---|---|
| 1 | `StageSequenceStep` 增 **`ProductionDepartmentId`**（`int?`），使链 = PM 的 `EffectiveStagePath`（MaterialId+StageCode+StageSeq+部门 四条一组） | PM §三/§四/§六 | `Core/Dto/MaterialStageDepartmentContextDto.cs` |
| 2 | 新增 `BuildEffectiveStagePaths`：部门取自 `MaterialStageDeptContext`；**MSC 无映射 ⇒ null，不推导不猜** | PM §四/§六 | `PeggingOrchestrator.BuildEffectiveStagePaths` |
| 3 | `PeggingSupplyAllocation.NextRequiredStageCode` **回溯填值**（原全仓零赋值 ⇒ 恒 NULL） | PM §十一 | `PeggingOrchestrator` 统一事务插入段 |

**实测（PV540）**：`NextRequiredStageCode` 由 **0 → 40 条非空 / 1206 条分配**（5 个不同 Stage）。

### ⚠️ 已落码项的**已知局限**（如实报告，不掩盖）

当前 `NextRequiredStageCode` 的取值来源是 `LogicalProductionDemand.RequiredStageCode`（按 `DemandKey` 关联）。
而 `LogicalProductionDemand` **只为"有生产缺口"的需求生成** ⇒ **纯供给承接（库存/PI/采购）而无生产缺口的需求，其分配行取不到阈值** ⇒ 只填了 40/1206。

**这不是键错配**（按 `DemandKey` 命中即证明键空间一致），是**取值来源偏窄**。
**正确来源应为 BOM 边阈值（`BomEdge.ChildRequiredStageCode`）按需求物料直接查**，与需求是否产生生产缺口无关。
**2号位 建议**：作为下一步小改（把阈值映射挂到 `PeggingResultVoucher` 供分配侧直接取），**待 1号位/PM 确认该语义**——因为 PM §十一 的措辞「为什么这个 Supply 可以承接这个需求」也可以读成**只对承接关系成立时才有意义**。**请 PM 明确取哪种语义**。

---

## 四、给 3号位 的请求

1. **`StageLeadTimeParam` 参数模型**：粒度 = `工厂 + StageCode + 生产部门`；Owner = 你方；业务时间由生产/工艺/计划部门供。
2. 我方**装载代码已就绪**（5 级降级命中），**表当前 0 行** ⇒ 数据到位即自动生效，无需再改码。
3. 请明确**发布/生效管理**走的载体（是否进策略包/`FrozenStrategySnapshot`），我方据此定装载时机。

---

**发送人**：2号位 ｜ **日期**：2026-09-28
（定位使用符号锚；实测为 `APS_Production` 只读查询 + 真实数据集成测试。）
