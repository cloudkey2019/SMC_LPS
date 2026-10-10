# Scope校验需求 — 5号位→3号位

**日期**：2026-09-12  
**来源**：4号位执行清单（2026-08-18\审核\25-4）任务⑤  
**目的**：请求3号位提供Scope校验接口，供5号位后端调用

---

## 一、背景

0号位裁决要求5号位后端必须执行业务范围权限校验：

```
3号位身份上下文
↓
功能Permission
↓
Business Scope
↓
合法Scope下推
↓
5→2 Query/Application Service
```

当前5号位所有Controller的查询参数（如factoryCode、domainKey）都由前端传入，后端未做Scope校验。

---

## 二、5号位需要3号位提供的接口

### 接口：IDataScopeService

**建议接口定义**：

```csharp
/// <summary>
/// 业务范围校验服务
/// </summary>
public interface IDataScopeService
{
    /// <summary>
    /// 校验当前用户是否在指定业务范围内
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="scopeType">范围类型</param>
    /// <param name="scopeValue">范围值</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否在范围内</returns>
    Task<bool> IsInScopeAsync(
        int userId,
        ScopeType scopeType,
        string scopeValue,
        CancellationToken ct = default);

    /// <summary>
    /// 断言当前用户在指定业务范围内，不在则抛出异常
    /// </summary>
    Task EnsureInScopeAsync(
        int userId,
        ScopeType scopeType,
        string scopeValue,
        CancellationToken ct = default);

    /// <summary>
    /// 获取用户的业务范围
    /// </summary>
    Task<UserDataScope> GetUserScopeAsync(
        int userId,
        CancellationToken ct = default);
}

/// <summary>
/// 范围类型
/// </summary>
public enum ScopeType
{
    Factory,
    ProductFamily,
    Department,
    Domain
}

/// <summary>
/// 用户业务范围
/// </summary>
public class UserDataScope
{
    public List<string> FactoryCodes { get; init; } = new();
    public List<string> ProductFamilyCodes { get; init; } = new();
    public List<string> DepartmentCodes { get; init; } = new();
    public List<string> DomainKeys { get; init; } = new();
}
```

---

## 三、5号位的调用方式

在每个Controller方法体开头调用：

```csharp
// 示例：查询订单时校验Factory和Domain
[HttpGet]
[Authorize(Policy = PermissionCodes.PlanView)]
public async Task<ApiResponse<List<OrderListItemDto>>> QueryOrders(
    [FromQuery] string? factoryCode = null,
    [FromQuery] string? domainKey = null,
    CancellationToken ct = default)
{
    // Scope校验
    if (!string.IsNullOrEmpty(factoryCode))
        await _dataScopeService.EnsureInScopeAsync(userId, ScopeType.Factory, factoryCode, ct);
    if (!string.IsNullOrEmpty(domainKey))
        await _dataScopeService.EnsureInScopeAsync(userId, ScopeType.Domain, domainKey, ct);

    // 业务逻辑...
}
```

---

## 四、需要校验的Controller清单

| Controller | 校验维度 |
|-----------|---------|
| OrderQueryController | Factory, Domain |
| OverviewController | Domain |
| ExplanationController | Domain |
| PeggingTraceController | Domain |
| SupplyFactTraceController | Factory |
| GovernanceQueryController | - |
| DomainStatusController | Domain |
| DemandProtectionController | Factory, Domain |
| BusinessFactIssueController | Factory |
| ProcurementManualEtaController | Factory, Department |
| PiPositionController | Factory, Domain |

---

## 五、当前阻塞

5号位所有Controller已就绪，等待3号位提供`IDataScopeService`实现。

**阻塞项**：
- 3号位未提供Scope校验接口
- 5号位无法执行后端Scope校验
- 4号位前端可绕过后端直接访问数据（安全风险）

---

## 六、期望回复

请3号位确认：
1. 是否提供`IDataScopeService`接口？
2. 接口方法签名和参数是否同意上述建议？
3. 预计什么时候可以提供实现？
4. 用户Scope数据存储在哪个表？（APS_Auth库？）

---

## 七、参考文档

- 0号位裁决：《APS_V1_20260901_职责前端接口与权限归属调整正式裁决_v1.0》
- 权限设计：《APS_权限与审批设计方案_v1.1_20260901冻结对齐版》§21.2
- 5号位实施包：《APS_V1_5号位复杂业务事实ODS与业务接口接入开发实施包_v1.3》§二十一
- 4号位执行清单：《5号位-执行清单-2026-09-12》任务⑤
