# 回执：Business Scope 消费端认收 —— 1号位 致 3号位

> 收件：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）／抄 0号位、2号位
> 依据：3号位《APS V1 · Business Scope 全链路就绪，致 1号位 认收消费端 v1.0 20260923》§三 / §五
> 日期：2026-09-23
> 性质：**认收回执** —— **载体认收** ✓；**但落码条件当前不满足**：判定键在 1号位 契约内缺失，须先补投影（详见 §二）
> 取证方式：读工作实际代码，逐条附 `文件:行`

---

## 一、载体认收 ✓（形状无异议）

| 环节 | 1号位 核实 |
|---|---|
| 载体存在 | ✅ `DomainSolveRequest.cs:301` `public BusinessScopeDto? BusinessScope { get; init; }`（`RunScope` 内）|
| 结构 | ✅ `BusinessScopeDto`（`LPS.APS.Core/DTOs/Scope/BusinessScopeDto.cs:11`）三字段：`IsGlobal`(:14) / `ScopeType`(:17) / `ScopeValues`(:20) |
| 透传 | ✅ 贵函 §二 所陈 `PeggingOrchestrator.BuildRunScopeAsync` 原样透传、null→null、无判权限 —— 1号位 无异议 |
| 与 `InScope` 的关系 | ✅ 认同「BusinessScope=权限硬框（谁可动）、InScope=计划细粒度；BusinessScope 优先为硬边界」的层级关系 |

**载体本身 1号位 认收无异议。**

---

## 二、⚠ 落码阻断：判定所需的键，在 1号位 契约内**全部缺失**

贵函 §三 判定语义要求：

> 对象对应 **产品族（ProductFamily）/ 生产组织（ResourceOrgGroup）** 的业务键 ∈ `BusinessScope.ScopeValues` 或 `IsGlobal=true` → 可移动；否则不可移动。

**1号位 侧实证（grep 全层）**：

| 检查 | 结果 |
|---|---|
| `LogicalProductionDemand` 有无 `ProductFamily` / `ResourceOrgGroup` | ❌ **无**（`LPS.APS.Core/Dto/LogicalProductionDemand.cs` grep 空）|
| `DomainSolveRequest` 资源侧有无 `ResourceOrgGroup` | ❌ **无** |
| 1号位 侧是否已消费这两键 | ❌ **零命中**（`LPS.APS.Scheduling` 全层 grep 空）|
| `ScopeValues` 的口径 | ⚠ 贵函定义为「业务键字符串，与 `IDataScopeService.EnsureInScopeAsync` 的 value 同口径」—— **该口径 1号位 完全不具备**（不查库、不持 DataScope）|

**结论**：1号位 **无法判定"某需求/资源是否在 BusinessScope 范围内"** —— 缺键、缺口径。**直接落码不可行**。

### 2.1 分维度可行性（精确到维度）

`BusinessScopeDto.ScopeType` 是业务范围**维度**，据此逐维评估：

| `ScopeType` | 1号位 可否落码 | 依据 |
|---|---|---|
| **`Department`** | ✅ **可** | 1号位 契约**有**该维度键：`OperationResourceEligibility.ProductionDepartmentId`、`MaterialStageDepartmentContextDto.ProductionDepartmentId`（部门锁定链路已在用，`PhaseOneConstraintBuilder`）|
| **`ProductFamily`** | ❌ **缺键** | 契约内无该字段 |
| **`ResourceOrgGroup`** | ❌ **缺键** | 契约内无该字段 |

### 2.2 建议（照既有范式，成本最低）

**请投影侧（2号位/3号位）把「越界判定」前置，直接投影结果**——照 `DueDateOverrides` 的既有范式（其头注即「`LogicalDemandKey` → `ManualTargetDueDate`（**OrderCanonicalId 已按需求展开**）」，`DomainSolveRequest.cs:290`）：

```
建议新增（RunScope 内）：
  OutOfScopeDemandKeys : IReadOnlyList<string>   // 越界 demand 的 LogicalDemandKey（1号位 内存键）
  OutOfScopeResourceIds: IReadOnlyList<int>      // 越界资源（与 ResourceDefinition.ResourceId 同域）
```

**理由**：`ScopeValues` 是 DataScope 业务键口径，1号位 永远不具备；而越界结论在 3号位（`IDataScopeService`）侧**本来就能算**。投影结果而非投影判据，1号位 即可零歧义落码。

---

## 三、⚠ 语义澄清：「必须移动才能成可行方案」中的「移动」，1号位 无此概念

贵函 §三：

> 若某需求/资源**命中范围外且不移动则不可行** → 停止并返回 `BUSINESS_SCOPE_INSUFFICIENT`。

**1号位 的模型是「输入 demand 集合 → 输出全新 Task 集合」**（纯内存、不读库、**不持有 Base 旧计划**）——**没有"移动已有 Task"这个动作**，故「不移动则不可行」无法直接评估。

**建议转换为「资源可行性」判定（1号位 可做）**：

> 某 demand 的**全部合格资源**（`OperationResourceEligibility` 命中的资源集）**均在范围外** → 该 demand **无法在不越界前提下排上** → **拒收 `BUSINESS_SCOPE_INSUFFICIENT`**（fail-closed）。

即：**"越界即不可用"** —— 等价于贵函「范围外对象不可移动」的排程侧表述，且 1号位 只需现有 `OperationResourceEligibility` + 范围外资源集（§二 建议的 `OutOfScopeResourceIds`）即可判定，**无需旧计划**。

**请确认该转换与贵方本意等价**；若不等价（例如"移动"另有所指），请给出 1号位 可观测的判据。

---

## 四、§五 落码清单评估

| # | 贵方 落点 | 1号位 评估 |
|---|---|---|
| 1 | 常量 `BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT"` | ✅ **可落**（常量与拒绝信号机制，1号位 侧无阻碍；与既有 `SCOPE_REFERENCE_MISSING` / `SCOPE_VIOLATION` 同族）|
| 2 | 消费点：`RunScope.BusinessScope` 非 null 时遵守判定 | ⚠ **依赖 §二（键缺口）与 §三（语义转换）先闭环** —— 二者不决，落码即返工 |
| 3 | 单测（IsGlobal 放行 / 命中 ScopeValues 可移动 / 范围外必要移动拒收） | ✅ **可写**，同样依赖 §二/§三 前置 |

**建议**：**1 可先落**（常量定义，无依赖）；**2、3 待 §二/§三 澄清后同批落**。

---

## 五、⚠ 归纳：这是今日第 4 例「上游用了 1号位 不存在的键」

| # | 来源 | 缺失的键 | 后果 |
|---|---|---|---|
| 1 | 2号位《订单保护事实载体》| `OrderCanonicalId`（long，且违红线）| `Frozen` 锚点无法关联 Task |
| 2 | 2号位《订单保护事实载体》| （同源：1号位 无「旧计划」）| `Firm` 的"标记变化"无法判定 |
| 3 | 2号位《插单 Candidate 缺口清单》④ | BusinessScope 载体（当时缺失）| 业务性拒收无处判定 |
| 4 | **本函** | `ProductFamily` / `ResourceOrgGroup` | BusinessScope 范围判定无法落码 |

**根因同一**：上游按「自己的业务键域」设计投影，而 1号位 是**纯内存、键域固定**的消费方（《ScopeJsonV2 消费契约》§2.3 红线：「官方 Id / 业务键不进 Solver 契约，全部转 1号位 内存键」）。

**建议确立一条通则**（提请 0号位 登记，供全号位后续设计复用）：

> **凡要求 1号位 按某键做判定者，该键必须是 1号位 契约既有字段；否则由投影侧把「判定结论」投影为 1号位 内存键，不投判据。**

这条若能确立，可一次性消除上述 4 例及后续同类问题（今日已第 4 次，非偶发）。

---

## 六、回执表

| # | 项 | 1号位 回口径 |
|---|---|---|
| 1 | `RunScope.BusinessScope` 载体 | ✅ **认收**（形状/层级关系无异议）|
| 2 | §三 判定语义（ProductFamily/ResourceOrgGroup ∈ ScopeValues）| ⚠ **当前不可落码** —— 两键在 1号位 契约内缺失（§二）；`Department` 维度可落码 |
| 3 | §三 「必须移动才能成可行方案」| ⚠ **须转为"资源可行性"判定**（全部合格资源越界→拒收），1号位 无"移动"概念（§三）|
| 4 | §五 落码清单 | **1 可先落**（常量）；**2、3 待 §二/§三 澄清后同批** |
| 5 | 建议 | 投影侧前置越界结论（`OutOfScopeDemandKeys` / `OutOfScopeResourceIds`），照 `DueDateOverrides` 范式（§2.2）|
| 6 | 通则提请 | 提请 0号位 登记「1号位 判定键必须为契约既有键，否则投结论不投判据」（§五）|

**发送人**：1号位 ｜ **日期**：2026-09-23

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）