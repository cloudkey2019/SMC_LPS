# APS V1 3号位 ProcurementSortParams 孤儿落位裁决请求

**版本**：v1.0
**日期**：2026-08-28
**编制**：3号位
**性质**：3号位 阶段 B/D 交付已基本完成，仅剩 **1 处跨号位契约形状待 0号位 裁决**——`FrozenStrategySnapshot` 中 `ProcurementSortParams`（采购排序治理参数）为**孤儿类型**（未被六块任一引用），请求 0号位 三选一定其最终落位。
**上位依据**：
- 《APS_V1_0号位对3号位规则齐全度核查汇报答复_20260828.md》（三类 Supply 排序分别执行 / 避免过度设计 / VMI 口径 A）
- 《APS_V1_3号位规则齐全度核查汇报_20260827.md》（第 3 行：`ProcurementSortParams` 归 "Supply 排序 · Procurement-VMI 类别"）
- 《APS_V1_3号位交付契约_v0.2_20260817.md》（六块结构 / C5-1：ProcurementSort 不进入 FrozenFactParameters）
- 《APS_V1_3号位开发任务清单_v1.0_20260817.md》B-1（六块 DTO）

**交付基线（2026-08-28）**：集成测试 124 pass / 5 skip / 0 fail（129 总）；`FrozenStrategySnapshotProvider` 已 DI 注册；六块 DTO 冻结于 `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs`。

---

# 一、裁决请求（先给结论）

> **`ProcurementSortParams`（`FrozenStrategySnapshot.cs:142-148`）现为孤儿类型（未被六块任一引用、Provider 不装配、零消费方）。请 0号位 三选一裁决其落位：**
>
> - **A（推荐）**：挂 `SupplyBlock`（③）作第三个 Supply 排序类别（与 `Inventory` / `PiSort` 平级）；
> - **B**：挂 `ProcurementBlock`（④），与 LT/Margin/Offset/Yield 同块；
> - **C**：判定四个治理开关不必要 → **删除孤儿类型**（链留注释），2号位 SessionStart 直接读 `ProcurementBlock.DefaultPurchaseLt / ArrivalToUsableOffsets`。
>
> 其余口径（VMI 口径 A、不新增逐 Supply 事实字段、FrozenFactParameters 范围）**均不变**，仅背书。

---

# 二、交付状态核验（3号位 基本已交付 ✅）

| 交付项 | 现状 | 硬证据 |
|---|---|---|
| `IFrozenStrategySnapshotProvider` 接口 | ✅ 已交付 | `LPS.APS.Core/Interfaces/IFrozenStrategySnapshotProvider.cs` |
| `FrozenStrategySnapshotProvider` 实现 | ✅ 已交付 | `LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs`（B-5 按 VersionId 缓存，一次 Run 装载一次） |
| **DI 注册** | ✅ 已注册（2号位 直接注入消费） | `ApplicationServiceExtensions.cs:19-28`：Scrutor 自动扫描 `LPS.APS.Application.Services` → Scoped |
| 六块 DTO | ✅ 已冻结 | `FrozenStrategySnapshot.cs:11-24` 六块：DemandPriority① / Lock② / Supply③ / Procurement④ / SolverStrategy⑤ / CandidateGuardrail⑥ |
| ETA Invariant | ✅ 已固化、不可配置化 | `LPS.APS.Core/Rules/EtaInvariant.cs` + `EtaInvariantTests`（R10）；六块清单断言 `EtaInvariantTests.cs:141-146` |
| 测试 | ✅ 单元+集成齐备 | `FrozenStrategySnapshotProviderTests` / `...IntegrationTests` / `StageDTests`（R07~R13） |

**仅剩 1 处遗留**：`ProcurementSortParams` 落位（见 §三）。

---

# 三、待裁决项实况：`ProcurementSortParams` 确为孤儿类型

**定义**：`FrozenStrategySnapshot.cs:142-148`，四个治理开关：

```csharp
public sealed class ProcurementSortParams
{
    public bool WarehousePriorityEnabled { get; set; }
    public bool DefaultLtAsTieBreak { get; set; }   // 是否以 DefaultLT 参与排序
    public bool MarginApplied { get; set; }
    public bool ArrivalOffsetApplied { get; set; }
}
```

**孤儿证据（四条，均核实）**：

1. **未被六块任一引用**：`DemandPriorityBlock / LockBlock / SupplyBlock / ProcurementBlock / SolverStrategyBlock / CandidateGuardrailBlock` 均无该类型属性（`FrozenStrategySnapshot.cs` 全文核对）；
2. **Provider 不装配**：`FrozenStrategySnapshotProvider.cs:94-114` 仅装配六块，`ProcurementSortParams` 永不落入 Snapshot JSON；
3. **零消费方**：全代码库 grep `ProcurementSortParams` 仅命中本 DTO 定义 + 文档，无任何 2号位/测试消费；
4. **无测试覆盖**：`EtaInvariantTests.cs:141-146` 六块清单、`StageDTests` R 门均不含该类型。

> 即：核查汇报第 3 行标记为"✅ 齐全"的采购排序冻结链，其**治理参数当前无任何交付通道**——若不管，2号位 无法读取这四个开关，该行 ✅ 名不副实。

---

# 四、候选方案三选一（含 3号位 推荐）

| 方案 | 落位 | 优点 | 缺点/风险 |
|---|---|---|---|
| **A（推荐）** | **挂 `SupplyBlock`（③）作第三排序类别**，即 `SupplyBlock.Procurement` | ① 与 Inventory/PI 排序规则平级，2号位在排序执行点**一处读完三类规则**；② 契合核查汇报归类（"Supply 排序 · Procurement-VMI 类别"）；③ 契合契约 C5-1——`InventoryAvailability` 与 `ProcurementSort` 本就同列"3号位治理→2号位执行的 Pegging 规则"，而 `InventoryAvailability` 即挂 SupplyBlock | 变更 2↔3 消费读取位置（需 0号位 背书，即本次裁决） |
| B | 挂 `ProcurementBlock`（④） | 采购相关集中一块 | ⚠️ **不建议**：ProcurementBlock 是 `FrozenFactParameters`（5号位 投影）的数据源（DefaultPurchaseLt / OverdueMargin / ArrivalToUsableOffsets，契约 C5-1）；C5-1 明确排除 ProcurementSort 进投影，挂这里形成"2号位 抽取投影时须记得剔除"的误伤耦合 |
| C | **删除孤儿类型**，链留注释；2号位 SessionStart 直读 `ProcurementBlock.DefaultPurchaseLt / ArrivalToUsableOffsets` | 最小化：不新增任何 Snapshot 字段，契合 0号位 08-28"避免过度设计"态度 | ① 四个治理开关（WarehousePriorityEnabled / DefaultLtAsTieBreak / MarginApplied / ArrivalOffsetApplied）丧失"3号位 治理其中参数"能力，核查汇报第 3 行须改判为"链固定、无治理开关"；② 若走此路，孤儿类型必须**删除**，不得保留死代码 |

**3号位 推荐：A**。理由：`ProcurementSortParams` 语义是 **Supply 排序规则**（非采购事实参数），与 Inventory / PI 同族（核查汇报归类 + C5-1 同列 + 0号位"Inventory、PI、Procurement 三类规则分别执行"口径）；挂 `ProcurementBlock`（④）会造成与 5号位 投影抽取的耦合误伤；保留孤儿则契约不可消费、核查 ✅ 不成立。

---

# 五、连带确认（非新问，仅背书）

以下三条均为已裁决口径，落位变更**不触碰**，仅请 0号位 在裁决时一并背书"其余口径不变"：

| # | 口径 | 来源 |
|---|---|---|
| 1 | VMI 保持独立 SupplyType，V1 排序共用现有 Procurement/Timed 链，**不新增 VMI 独立规则类别** | 0号位 2026-08-28 口径 A |
| 2 | **不得**在 FrozenStrategySnapshot 增加 PhysicalSourceKey / SourceDocumentNo 类逐 Supply 事实字段 | 0号位 2026-08-28 |
| 3 | `FrozenFactParameters` 范围不变（= DefaultPurchaseLt + OverdueMargin + ArrivalToUsableOffsets，**不含 ProcurementSort**） | 契约 C5-1 |

---

# 六、3号位 自属部分（无需 0号位 确认）

- Provider 装配路径：按落位在 `Supply` / `Procurement` 子块 JSON 增补第三属性（方案 A/B 时）；
- `StageDTests` 补 JSON 往返验收（R 门）；
- 交付契约 v0.2 §二 结构同步更新（红线 #5：先过契约文档再改 Snapshot）；
- DI 注册无需变更（Scrutor 自动扫描已完成）。

---

# 七、裁决后 3号位 落地清单

| 落位裁决 | 3号位 动作 |
|---|---|
| A | `SupplyBlock` 增 `Procurement` 属性 + Provider 装配 + 测试补 R 验收 + 契约 §③ 增补 |
| B | `ProcurementBlock` 增属性 + Provider 装配 + 测试 + 契约 §④ 增补（附 C5-1 投影剔除说明） |
| C | 删除 `ProcurementSortParams` 类型 + 核查汇报第 3 行改判（链固定、无治理开关）+ 注释保留冻结链 |

---

---

# 八、0号位 裁决回执（2026-08-28 最终业务裁决）

**0号位 已裁决**（V1 供给排序最终业务裁决，不扩新排序体系）：

- **采购/Timed Supply 排序链固定**：Eligibility → Warehouse Priority → AvailableTime → PO Release Time → PO+Line；**不可重排、无 Enable/Disable 开关**。
- **本请求落位结论 = 原方案 C 细化**：`ProcurementSortParams`（四开关类型）**删除**；采购参数回归 **④ `ProcurementBlock`** 四参数值——`WarehousePriority`（原缺值，已补齐）/ `DefaultPurchaseLt` / `OverdueMargin` / `ArrivalToUsableOffsets`；2号位 在 Run/Domain 事实准备阶段直读并形成 AvailableTime，按固定链执行。
- **方案 A/B 撤销**：`SupplyBlock` 不再增第三排序类别属性（三类排序中 Inventory / PI 仍在 SupplyBlock，采购参数在 ProcurementBlock）。

**落地记录（3号位 2026-08-28）**：

| 文件 | 动作 |
|---|---|
| `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs` | 删除 `ProcurementSortParams`；`SupplyBlock` 恢复原状；`ProcurementBlock` 增 `WarehousePriority` |
| `LPS.APS.Tests/Unit/StageDTests.cs` | 移除四开关测试；新增 `R09_Procurement仓库优先级_可读取` |
| 交付契约 v0.2 | §③ 恢复原状；§④ 增 `WarehousePriority` + 裁决说明 |
| 规则齐全度核查汇报 | 第 3 行证据更新为四参数值模型 |

**其余口径确认**：VMI 保持独立 SupplyType 共用采购链（口径 A）、不新增逐 Supply 事实字段、`FrozenFactParameters` 范围不变——均不受本落位影响。

---

*本请求由 3号位 2026-08-28 交付核验整理；证据链完整（文件:行号可追溯）。红线 #5（接口即契约）：落位变更先过契约文档再改 DTO（见 §六），3号位 不自定跨号位契约形状。*
