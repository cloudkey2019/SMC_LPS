# 3号位致2号位 — 业务范围二次校验收口协作报告

**发送**：3号位
**接收**：2号位（数据引擎/框架）
**日期**：2026-09-15
**主题**：契约① scope 二次校验落地后的接口就位声明 + 需求保护释放 / 规则集发布的裁决请求

---

## 一、3号位已交付（供 2号位 知悉，无需动作）

新增接口 `IDataScopeService.EnsureInScopeAsync`，fail-closed 语义，供各业务 Controller 做业务范围（scope）二次校验：

```csharp
/// 断言指定用户对 (scopeType, value) 拥有业务范围授权（F-G4，fail-closed）。
/// Global 放行；该维度未授权或不含目标值一律抛 ScopeViolationException。
Task EnsureInScopeAsync(int userId, string scopeType, string value, CancellationToken cancellationToken = default);
```

- **实现**：`LPS.APS.Engine\Services\Auth\DataScopeService.cs`
- **异常**：`LPS.APS.Core\Authorization\ScopeViolationException`（Web 层统一映射 HTTP 403 + `ApiResponse.Fail(403,…)`）
- **契约影响**：仅新增方法，**未改动任何既有接口签名**（红线 #5 合规）；本报告即同步契约文档。
- **验证**：build 0 错误，RunLifecycleServiceTests 43/43 通过。

---

## 二、需求保护释放的 Factories 维度校验归属澄清

契约 §三① 要求 `POST /api/demand-protection/release`（`DemandProtectionRelease`）补 **Factories 维度** scope 校验。

核查结论：
- `DemandProtectionController` 归 **5号位**（Controller 头注释「5号位提供给4号位」），其 `Release` 动作**中转调用** 2号位的 `IDemandProtectionReleaseService.ReleaseLocksAsync`。
- 按号位红线，3号位**不越位改** 5号位 Controller。

**裁决请求**：Factories 维度的 scope 校验应落在 5号位中转层（5号位职责），由 5号位在调用 `ReleaseLocksAsync` 前调 `EnsureInScopeAsync(actorUserId, DataScopeTypes.Factory, factoryCode)`。2号位只需保持 `ReleaseLocksAsync` 接口现状不变即可。请 2号位知悉此接口已就位，便于 5号位联调时对齐。

---

## 三、规则集发布/停用的 scope 维度裁决（需 2号位/PM 定夺）

契约 §三① 要求 `rule-set-version/{id}/publish`、`retire` 补 `Domains ∋ ruleSet.DomainKey`（`ALL` 放行）。

**阻塞点**：规则集（RuleSet）实体**无 DomainKey 字段**，无法执行「Domains ∋ DomainKey」校验。

**请求裁决**（二选一）：
1. **方案 A（推荐）**：RuleSet 按「无域维度约束」处理 —— publish/retire 仅保留现有 `[Authorize(Policy = RulePublish)]`，不补 scope 校验。
2. **方案 B**：RuleSet 补充 DomainKey 字段（涉及 **2号位 DDL 变更**，需提交 2号位）。

3号位倾向方案 A（最小改动，RulePublish 权限码已足够收敛发布权；v1.2 已合并 RULE_ADMIN/RULE_PUBLISHER 为 `aps.admin.aps`）。

---

## 四、不在本次收口范围（仅备案）

- **CTP 评估端点（`CtpEvaluate`）**：权限码与端点当前均**未落地**，非本次收口项。
- **MES 下发端点（`MesDispatch`）**：同上，权限码与端点均未落地。

以上两行待相关号位落地端点后，再由对应归属号位补 scope 校验。
