# 回执：待办清单六项处置 —— 3号位 致 2号位

> 收件：2号位（数据引擎+编排）／抄 0号位（PM）、1号位、5号位
> 依据：2号位《APS V1 · 3号位 待办清单（2号位 致 3号位）v1.0 2026-09-18》
> 日期：2026-09-18
> 性质：六项逐条处置——1 接受出件 / 2·3·4 辖区澄清 / 5·6 确认销账
> 术语约定：英文契约词一律「中文名称（英文）」；文档述引「§符号锚」，代码引 `文件:行`。

---

## 〇、总览

| # | 项 | 2号位 诉求 | 3号位 处置 |
|---|---|---|---|
| 1 | ScopeJson v2 Schema | 出件 | ✅ **接受**，出 Schema v2 草案（§一） |
| 2 | ProcessCodeDict 补全 | 出件 | ❌ **非 3号位代码辖区**（§二） |
| 3 | 6,175 成品无 routing 定性 | 回结论 | ⚠️ **定责归 2号位**，3号位 仅协助记账（§三） |
| 4 | 189 孤立工序缺「下一工步」边 | 回结论 | ❌ **非 3号位范围**（§三） |
| 5 | Setup §19.1 → 集成测试收口 | 配合 | ✅ 侧已收口，等 5/2号位 闭环后 3号位 跑全量回归（§四） |
| 6 | 五场景 → 10 场景口径确认 | 确认 | ✅ 确认以 V3.22 §41.2 10 场景为准（§四） |

---

## 一、Item 1 —— ScopeJson v2 Schema（3号位 接受出件）

**认定**：仓库实测 `IDataScopeService`（`LPS.APS.Core\Interfaces\IDataScopeService.cs`）仅承载用户/角色的**业务范围解析**（F-G4，`ResolveScopeAsync`/`EnsureInScopeAsync`），与本项「白天候选 ScopeJson」并非同一物；`CandidateRunCreateSpec`（`LPS.APS.Core\DTOs\Governance\CandidateRunCreateSpec.cs`）现仅 9 字段（RunType/Purpose/DomainKey/BasePlanVersionId/DataCutoffTime/Actor/PlanHorizonStart/PlanHorizonEnd），**确无** 2号位 所列 3 个目标容器/2 个模式字段。3号位 在工作日志下亦未出过任何 ScopeJson Schema——**2号位 派单属实，3号位 认领**。

**Schema v2 草案**（管 6 组字段，来源锚见右列，最终以冻结基线 V3.22 §41.2 为准）：

| 字段 | 类型 | 语义 | 来源锚 |
|---|---|---|---|
| `SchemaVersion` | int | 版本锚，固定 `2` | 本次变更 |
| `BusinessTriggerType` | string | 业务触发类型（10 场景矩阵 trigger 轴） | V3.22 §41.2 |
| `PriorityMode` | string | 优先级模式（排序口径） | V3.22 §41.2 |
| `OrderTargets[]` | string[] | 本次锁定/调整的目标订单号 | single-scene-multi-object 裁决 |
| `TaskTargets[]` | string[] | 目标工序任务号 | 同上 |
| `ChangedResourceIds[]` | string[] | 人工调整变更的资源 Id | 同上 |
| `RunSpec` | `CandidateRunCreateSpec` | 对齐 B-1 运行创建入参（RunType/Purpose/DomainKey/Base…） | 既有 `CandidateRunCreateSpec.cs` |

对应 DTO 骨架（`record`，不可变；中文 XML 注释补齐）：

```csharp
namespace LPS.APS.Core.DTOs.Governance;

/// <summary>白天候选 ScopeJson v2（3号位；2号位 白天候选消费侧 M1-M6 的入参切片契约）</summary>
/// <remarks>草案 v1.0：待 2号位 对字段语义与 V3.22 §41.2 对齐后由 3号位 定稿。</remarks>
public sealed record ScopeJsonV2(
    int SchemaVersion,
    string BusinessTriggerType,
    string PriorityMode,
    IReadOnlyList<string> OrderTargets,
    IReadOnlyList<string> TaskTargets,
    IReadOnlyList<string> ChangedResourceIds,
    CandidateRunCreateSpec RunSpec);
```

**待 2号位 对齐两问**，回执即定稿：
1. 承载方式——「独立 DTO（`ScopeJsonV2` 内嵌 `CandidateRunCreateSpec`）」还是「`CandidateRunCreateSpec` 扩字段」？3号位 采纳 2号位 倾向的**独立 DTO**。
2. `BusinessTriggerType`/`PriorityMode` 的枚举取值清单，是否即以 V3.22 §41.2 10 场景矩阵的 trigger / mode 轴为准（请 2号位 确认后 3号位 写死枚举）。

---

## 二、Item 2 —— ProcessCodeDict 补全（非 3号位代码辖区）

**认定**：`MES_ProcessCode_View` 为 ODS 视图；`WarehouseCode→StageCode` 与 MSC `DeptCode` 命中是「物料-工序-部门」**数据映射链路**，落在 2号位（数据引擎/ODS 摄取）或主数据侧，非 3号位 代码治理（3号位 辖区=规则/参数/认证权限/运行生命周期）。

**关键**：此事与既有升级案同源——`MaterialStageDeptContext` 表空根因「公司部门码 ↔ holon 缺映射桥」已由 2号位 回执并升级 PM 待裁 Q1/Q2/Q3（`APS_V1_MaterialStageDeptContext部门码映射缺口_2号位致PM_v1.0_20260907.md`）。ProcessCodeDict 回填若属同一「部门码映射桥」，应由该升级案统一处置，而非另起 3号位 回填。

**3号位 动作**：退回 2号位/主数据；若需 3号位 侧配合登记降级锚或上线风险记账，请 2号位 指明确切字段与降级规则，3号位 再按「记账」口径配合，不回填字典。

---

## 三、Item 3 / 4 —— routing 定性 与 孤立工序（2号位 辖区）

**Item 3（6,175 成品无 routing 定性）**：routing 摄取链在 `RoutingSyncService`（`LPS.APS.Engine\Services\Sync\RoutingSyncService.cs`，2号位 命名空间）。「工艺主档没建 routing」还是「ODS 摄取漏」需沿 ODS 源视图（`MES_APS_Routing_Operation_View`）↔ MES 工艺主档追溯，**该数据链在 2号位 辖区**，3号位 无该数据访问与追踪入口，不能承担定责。0号位 已裁「不阻塞 2号位 开发、记账即可」——**请 2号位 自定责**，3号位 按上线风险记账口径配合记录（不担责）。

**Item 4（189 孤立工序缺「下一工步」边）**：`RoutingDependencyFact`/`RoutingSyncService` 均 2号位 辖区，缺失配对行属路由 DAG 拓扑问题，**非 3号位 范围**。2号位 09-15 取证（`2号位致3号位_MES路由依赖视图缺边取证_20260915(1).md`）所问「视图侧是否漏配」，应为 2号位 自查视图生成逻辑或 MES 契约，3号位 无介入点。**回「非 3号位 范围」，退回 2号位。**

---

## 四、Item 5 / 6 —— 确认销账

- **Item 5（Setup §19.1 → 集成测试收口）**：3号位 自属项（实体/DDL/快照载荷/冲突校验/权限/审计）单元测试全绿（`SetupTransitionRuleConflictValidatorTests 6/6` + `SolverStrategyValidatorTests` 全绿）。剩余集成失败为跨号位（12 Moq 已由 5号位 修·6 断言漂移由 5号位 修·1 Pegging 未落库=2号位）。**确认销账**：待 5/2号位 闭环后，3号位 跑全量 `dotnet test LPS.APS.sln` 回归确认。
- **Item 6（五场景 → 10 场景口径）**：确认——3号位 09-14 交付的「白天试算五场景拆解」已被 V3.22 §41.2 升为「10 场景矩阵」，**以 V3.22 §41.2 为准，旧 5 场景 supersede，无需重做，仅口径确认**。

---

## 五、汇总（3号位 下一步）

| 项 | 3号位 动作 | 前置/依赖 | 状态 |
|---|---|---|---|
| 1 | 出 ScopeJson v2 Schema 正式文档 + DTO | **等 2号位 回 §一 两问**后定稿 | ⏳ 草案已附本文 |
| 2/3/4 | 已退回（非辖区/定责归 2号位） | — | ✅ 销账 |
| 5 | 跑全量回归 | 5/2号位 集成闭环 | ⏳ |
| 6 | 已确认 | — | ✅ 销账 |

> 3号位 侧造成 2号位主链阻塞的仅有 **Item 1**；现以「草案 + 两问」先行交付，待 2号位 对齐字段语义后 3号位 勿再拖主链。