# APS V1 ResourceCalendar 人工能力槽 + EXPEDITE 落码完成回执（S1~S5）

**版本**：v1.0
**日期**：2026-09-23
**发送人**：2号位（数据引擎 · 主执行窗口）
**接收人**：0号位（PM 收口）、1号位（消费确认）、5号位（数据维护知会）、3号位（备查）
**抄送**：4号位（备查）
**依据**：
- 0号位《PM 回复 0923》（EXPEDITE + ResourceCalendar 统一裁决）
- 0号位《APS V1 Resource Calendar 资源日历能力补充冻结方案 v1.3_人工能力槽模型与职责边界修订版》
- 0号位《APS V1 ResourceCalendar 人工能力槽及有限产能资源模型统一裁决回复》
- 1号位《主线待裁决 B1/B2/B3 回执 v1.0》
- 3号位《主线待裁决清单汇总 C1/C2 回执 v1.0》

---

## 一、总体结论

**2号位 落码五步（S1~S5）全部完成，`dotnet build` 0 错误、新增代码 0 警告。**

冻结裁决的三块，已按「运行时 Projection + 不落负号 + Demand 层承载」终版口径落地：

| PM 裁决块 | 落地状态 |
|---|---|
| ① 人工槽消费键 = 运行时 Projection（非业务化负 Id） | ✅ S3 落地 |
| ② 铺天数非 2号位责任 + 取消 730 天 7×24 合成 | ✅ S4 落地 |
| ③ EXPEDITE = Demand 层承载 · 可调整排序前置竞争层 | ✅ S5 落地 |
| ④ 资源模型统一裁决（1槽=1并行道 / 主关联 OperationCode / 源分离） | ✅ S1/S2 落地 |

---

## 二、逐项落地

### S1 · DDL 主关联修正（OperationName → OperationCode）
- `ManualCapacitySlot` 唯一键 `UQ_ManualCapacitySlot_Dept_OpName_Slot` → `UQ_ManualCapacitySlot_Dept_OpCode_Slot`（`(ProductionDepartmentId, OperationCode, SlotCode)`），`OperationName` 降为仅展示列（PM 资源模型裁决 §四/§五，翻案对齐）。
- 脚本：`03_SQL脚本/ResourceCalendar_设备人工三表_DDL_20260923.sql`

### S2 · 三表 + Task 列（直接上表）
- 开发库 `APS_Production` 三表已建：`ResourceCalendarSlot`（挂 `ResourceId`）/ `ManualCapacitySlot`（主档）/ `ManualCapacitySlotCalendar`（挂 `ManualSlotId`）。
- `[Task].[ManualSlotId] INT NULL` 已加（`COL_LENGTH=4` 实测在库）；`[Task].[ResourceId]` 已存在（设备 FK）。
- 脚本：`03_SQL脚本/Task_加ManualSlotId列_20260923.sql`

### S3 · 人工槽装载投影 + eligibility 展开 + 落库拆键
新增 `LPS.APS.Application/Services/SolverResourceProjection.cs` 承载合成键规则：

```csharp
ManualSlotResourceOffset = 1_000_000_000   // 正数偏移，避开设备 Resource.Id 空间（实测 MAX=14508）
IsManualSlotId(id) => id >= offset
ToManualSlotId(id)  => id - offset
```

- **S3a 装载投影**（`SchedulingOrchestrator.LoadResourcesAndCalendarAsync`）：读 `ManualCapacitySlot`(IsActive=1) + `ManualCapacitySlotCalendar`(AvailableFlag=1)，投影合成 `SchedulingResource`（`ResourceId = offset + ManualSlotId`、`CapacityFactor = 1.0`〔§十二 1槽=1并行道〕、`DispatchPriority = 100`）+ 日历窗口（**无日历 = 不可用**，与设备同口径 §九）。
- **S3b eligibility 展开**（`PeggingOrchestrator.LoadRoutingContextAsync`）：每个启用人工槽按 `(ProductionDepartmentId, OperationCode)` 匹配 RoutingOperation 工序，追加合成 `OperationResourceEligibility`（`ResourceId = 合成键`、`Priority = 100` 让真实设备优先、`CapacityFactor = 1.0`），**不触碰正式表**。
- **S3c 落库拆键**（`PeggingOrchestrator` Task INSERT + `ApsTask` 内存构造）：`FinalTaskDraft.ResourceId ≥ offset` → 拆回 `Task.ResourceId = NULL` + `Task.ManualSlotId = 原槽号`；设备 `ResourceId` 原样落库（P0-06 不丢时间资源真相）。实体 `LPS.APS.Core/Entities/Aps/Task` 加 `ManualSlotId int?`。

### S4 · 真实日历加载（取消 730 天 7×24 合成）
- `SchedulingOrchestrator.LoadResourcesAndCalendarAsync` 由「合成 730 天 7×24」改为只读 `ResourceCalendarSlot`(AvailableFlag=1) 真实可用窗口；**无日历行 = 该资源不可用**（§九 终版，设备+人工统一）。

### S5 · EXPEDITE 可调整 Demand 排序前置竞争层
- `UpstreamDemand` 加 `OrderCanonicalId(long?)`；`IDemandPriorityExecutor.ExecutePrioritySort` 加可选参数 `IReadOnlySet<long>? expediteOrderCanonicalIds = null`（向后兼容）。
- `DemandPriorityExecutor.ExecutePrioritySort` 最外层加「EXPEDITE 前置竞争层」：命中 expedite 集合的 Demand 作独立桶，与 normal 桶**各自按 3号位冻结 DemandPriorityConfig 排序**，expedite 桶整体前置后统一打 `DemandSequence`（=「可调整前置竞争层」，**非恒先、非新增 Task 优先级、不突破任何字段/规则**）。
- `PeggingOrchestrator`：`OrderPeggingRow` + SELECT 加 `o.OrderCanonicalId`；`BuildDemandSequenceMapAsync` 新增 `LoadExpediteOrderCanonicalIdsAsync`——读 `ScheduleRun.ScopeJson`，**仅 `PriorityMode == EXPEDITE` 且 `OrderTargets` 非空**时输出 `OrderCanonicalId` 去重集合，否则空集（空集 = 零行为变化，FULL 排程不受影响）。

---

## 三、关键口径决策

1. **合成键 = 正数偏移，不落负号**：PM 0923 推翻「业务化负 Id」改「运行时 Projection」后，2号位 把合成键定为 `1_000_000_000 + ManualSlotId`——始终为正、天然避开设备 `Resource.Id` 数值空间（实测 MAX=14508），满足「不暴露负号规则、不改 Resource 表、Task 不永久存负 ResourceId」。合成键只存在于运行时三通道（`Resources`/`CalendarSlots`/`OperationResourceEligibility`），落库即拆回。
2. **人工槽不污染正式资源主数据**：`ManualCapacitySlot` 独立主档，投影时才合成 `SchedulingResource`，`FactoryId = 空`、`ResourceName = "MAN:{OperationCode}:{SlotCode}"`（仅运行期展示/区分，非业务字段）。
3. **EXPEDITE 只重排不重规则**：身份 = `OrderTargets.OrderCanonicalId`，作用层 = `DemandPriorityExecutor` 最外层前置桶，`PriorityMode == EXPEDITE` 是唯一触发开关；同层内仍走 3号位冻结策略竞争，不新增 Task 优先级、不突破硬约束（Firm/Frozen/产能/交期可用性）。

---

## 四、2号位 自决点（请 PM / 1号位 复核，均为实现细节非契约点）

| # | 自决点 | 现值 | 备注 |
|---|---|---|---|
| 1 | 合成键数值空间 | `1_000_000_000 + ManualSlotId`（正数偏移） | 符合 PM「不落负号」；偏移量据设备 Id 空间实测设定，请 1号位 确认消费端无上限假设 |
| 2 | 人工槽 eligibility `Priority` | `100`（真实设备优先，人工槽作有限产能兜底） | PM 未明确人工槽 vs 设备的排序优先级，2号位 暂取中性值 |
| 3 | 合成 `ResourceName` 格式 | `MAN:{OperationCode}:{SlotCode}` | 仅运行期展示/日志定位，非落库键 |

---

## 五、编译与验证

- `dotnet build LPS.APS.Application` → **0 错误**。
- 警告：全量 40 条均为既有（`CS0618 PeggingResultVoucher` 已过时 / `CS8618` nullable 等），**新增 S3/S5 代码 0 警告**。
- 未跑运行时端到端：`ManualCapacitySlot`/`ManualCapacitySlotCalendar` 当前依赖 5号位 灌入测试日历数据（PM §九 测试方案：设备 365 天 08:00-17:00、人工槽 365 天白/夜班）后方可实测人工槽排程。

---

## 六、跨号位依赖与剩余（非本段待办）

| 号位 | 事项 | 状态 |
|---|---|---|
| **5号位** | `ManualCapacitySlot` / `ManualCapacitySlotCalendar` 持久化 + 测试日历灌数（持久化 Owner，2号位 只加载） | 待灌数后实测 |
| **1号位** | 确认合成 ResourceId 走既有三通道原样消费（已核实无稀疏数组下标，理论零改动） | 请复核自决点 #1 |
| **3号位** | `PriorityMode=EXPEDITE` 契约语义登记（C1：纯文档，无运行时治理位） | 已确认无动作 |
| **4号位** | `NEW_ORDER_*` 新订单 ScopeJson 承载（2↔3 技术对齐）；BOM 白天运行机制（5号位） | M3 段已登记，跨号位 |
| **4号位** | 旧 `SetupTransitionRuleController` 退役前端切换确认（C2） | 后端已自满足，待前端一句确认 |

---

## 七、收口登记表

| 编号 | 事项 | 结论 |
|---|---|---|
| S1 | DDL 主关联 OperationCode | ✅ 落地 |
| S2 | 三表 + Task.ManualSlotId 列 | ✅ 落地（COL_LENGTH 已核） |
| S3 | 人工槽投影/展开/拆键 | ✅ 落地 |
| S4 | 真实日历、取消 7×24 合成 | ✅ 落地 |
| S5 | EXPEDITE 前置竞争层 | ✅ 落地 |
| A1 | 主关联 OperationCode（§四/§五） | ✅ 并入 S1 |
| B1 | 1槽=1并行道、CapacityFactor=1（§十二） | ✅ 并入 S3 |
| B2 | 无日历=不可用、不保留兜底（§九） | ✅ 并入 S4 |
| B3 | EXPEDITE V1 全量重排 | ✅ 并入 S5（全量语义） |
| A2 | EXPEDITE 可调整边界 | ✅ OrderTargets 承载（OrderCanonicalId 可批量调整） |
| C1 | PriorityMode 治理落点 | ✅ 3号位 确认（无运行时位） |
| C2 | 旧 Setup API 退役 | ⏳ 后端已自满足，前端切换待 4号位 |

---

**发送人**：2号位 ｜ **日期**：2026-09-23