# 回执：Position5 后端 Scope 校验（IDataScopeService）—— 已提供，请按本文口径直接消费

> 收件：5号位（业务事实/控制器）、PM（存档）
> 发件：3号位（认证 + 角色 + 功能权限 + 业务范围权限）
> 依据：派单 `Position5_Scope校验需求_20260912(1).md`（4号位执行清单 2026-08-18\审核\25-4 任务⑤）
> 日期：2026-09-14

---

## 一、结论一句话

`IDataScopeService` **3号位 已于 2026-09-04（F-G4）收口**，接口 / 实现 / 实体 / 授权 DDL 全链路均已落地，派单 §五「3号位未提供 Scope 校验接口」的阻塞前提**已过时**。无需新建接口、无需等待新交付；5号位 按本文 §三 或 §五 直接注入并消费即可，当天可接入。

---

## 二、回执表（对应派单 §六 四问）

| 问 | 派单问题 | 回执 |
|---|---|---|
| 1 | 是否提供 `IDataScopeService` 接口？ | ☑ **已提供**（F-G4，2026-09-04 收口），定义见 `LPS.APS.Core/Interfaces/IDataScopeService.cs`，非本次新建 |
| 2 | 签名与参数是否同意提案？ | ⚠ **方向一致、形态不同**：已有一入口 `ResolveScopeAsync(userId) → DataScopeContext`，语义覆盖提案三个方法；枚举 / 扁平 DTO 不建议采纳，详见 §三 |
| 3 | 预计何时可提供实现？ | ☑ **无等待期**：实现已在 `LPS.APS.Engine/Services/Auth/DataScopeService.cs` 并已 DI 注册（Scoped），5号位 注入即可用 |
| 4 | 用户 Scope 数据存哪张表？ | APS_Auth 库三表：`DataScopePolicy`（策略行）+ `UserDataScope`（用户↔策略）+ `RoleDataScope`（角色↔策略，经 `UserRole` 关联），详见 §四 |

---

## 三、与提案的四处差异裁决（红线：接口即契约，先对齐再动代码）

| # | 提案 | 3号位 现状（真源） | 裁决 |
|---|---|---|---|
| 1 | `ScopeType` 枚举（Factory/ProductFamily/Department/Domain） | `DataScopeTypes` **字符串常量**（含 `Global` / `ResourceOrgGroup(遗留)`），见 `LPS.APS.Core/Authorization/DataScopeTypes.cs` | ❌ 弃枚举，**沿用字符串常量**。DDL 检查约束 `CK_DataScope_Type` 为字符串，且存在 `Global`（全放行）与 `ResourceOrgGroup`（遗留兼容）两维，枚举会漏维度 |
| 2 | `class UserDataScope`（4 个 List 扁平 DTO） | 实体 `UserDataScope` 已占据该名（`UserDataScope` = UserId↔ScopePolicyId 关联表），且扁平 List 表达不了「同维并集 ∩ 跨维交集 + Global」语义 | ❌ 弃扁平 DTO，**复用 `DataScopeContext`**（`IsGlobal` / `Allows(type,value)` / `GetValues(type)`），见 `LPS.APS.Core/Authorization/DataScopeContext.cs` |
| 3 | `IsInScopeAsync(userId,type,value)` / `EnsureInScopeAsync` / `GetUserScopeAsync` | `ResolveScopeAsync(userId)` + `context.Allows` / `context.GetValues` / `context.IsGlobal` | ✅ **语义已等价，不新增接口方法**。5号位 直接用上下文方法判断方可，避免工厂方法膨胀 |
| 4 | `Department` 维度 | 3号位 `DataScopeTypes.Department` = **生产部门（ProductionDepartment）**，非任意部门 | ⚠ **待对齐**：`ProcurementManualEtaController` 的 Department 维度是否 = 生产部门，请 5号位 确认口径后再按对应维度校验 |

---

## 四、Scope 数据存储（回答第 4 问，APS_Auth 库）

```
User ──< UserRole >── Role
  │                    │
  │ (用户直接范围)       │ (角色范围)
  ▼                    ▼
UserDataScope      RoleDataScope
  └────────┬───────────┘
           ▼
    DataScopePolicy (ScopeType + ScopeValue + IsEnabled)
```

- 有效范围 = `UserDataScope` ∪ `RoleDataScope`（经 `UserRole`） 命中的 `DataScopePolicy`，含 `Global` 时全放行，无任何策略时拒绝全部（安全默认）。
- 实现：`LPS.APS.Engine/Services/Auth/DataScopeService.cs`（EF Core + `AuthDbContext`），自动注册 Scoped（`Engine/Extensions/DatabaseServiceExtensions.cs`）。

---

## 五、两个安全口径（派单未写，接入前必须收口）

1. **`userId` 必须后端从声明取，禁止信任前端/请求体**。派单示例里 `EnsureInScopeAsync(userId, …)` 未说明 `userId` 来源，若取自助查询参数即横向越权。3号位 既有惯例：`User.FindFirst(ClaimTypes.NameIdentifier)`（`LPS.APS.Web/Controllers/GovernanceController.cs:72`），5号位 必须照搬。
2. **光「校验传入参数」不够，必须「按 Scope 过滤结果集」**。当 `?factoryCode=` 省略时，仅校验非空参数会让空参查询绕过校验、返回全域数据。正确做法参照 3号位 已落地范例：`ScheduleQueryService` 用 `ResolveScopeAsync` + `GetValues` 过滤（`LPS.APS.Application/Services/Query/ScheduleQueryService.cs:421`）。

---

## 六、请 5号位 下一步（改正派单示例的调用样板）

```csharp
/// <summary>后端解析当前用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
private int GetCurrentUserId()
    => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

[HttpGet]
[Authorize(Policy = PermissionCodes.PlanView)]
public async Task<ApiResponse<List<OrderListItemDto>>> QueryOrders(
    [FromQuery] string? factoryCode, [FromQuery] string? domainKey, CancellationToken ct)
{
    var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), ct);

    // 校验：非空入参必须落在授权范围（Global 全放行，无范围全拒绝）
    if (!string.IsNullOrEmpty(factoryCode) && !scope.Allows(DataScopeTypes.Factory, factoryCode))
        return Forbid();
    if (!string.IsNullOrEmpty(domainKey) && !scope.Allows(DataScopeTypes.Domain, domainKey))
        return Forbid();

    // 过滤：空参按授权范围收窄结果集，避免越权返回全域数据
    var allowedFactories = scope.GetValues(DataScopeTypes.Factory);
    var allowedDomains   = scope.GetValues(DataScopeTypes.Domain);
    // 查询时以 allowedFactories / allowedDomains 作为过滤条件（IsGlobal 时不追加过滤）

    // 业务逻辑...
}
```

接入清单：

- [ ] 在 5号位 服务/控制器注入 `IDataScopeService`（`LPS.APS.Core.Interfaces`，实现由 Web 宿主指向 Engine，依赖已就绪）。
- [ ] 每个受控 Controller 加 `GetCurrentUserId()`（取自 `ClaimTypes.NameIdentifier`）。
- [ ] 校验维度统一引用 `DataScopeTypes` 字符串常量，弃枚举。
- [ ] 空参查询走 `GetValues` 过滤，不只 assert 单个入参。
- [ ] `ProcurementManualEtaController` 的 Department 口径先与 3号位 对齐（§三 #4）。

---

## 七、阻塞状态解除

派单 §五 三项阻塞，本回执后全部解除：

| 阻塞项 | 状态 |
|---|---|
| 3号位未提供 Scope 校验接口 | ✅ 已提供，回执即文档 |
| 5号位无法执行后端 Scope 校验 | ✅ 按 §六 接入即解除 |
| 4号位前端可绕过后端直接访问（安全风险） | ⚠ 后端校验接上后收敛，但仍建议 4号位 在前端同步按权限隐藏/禁用越权入口（纵深防御，非 3号位 交付范围） |