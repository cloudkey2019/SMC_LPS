# 3号位 → 4号位：TaskSplitRuleConfig 与 StageLeadTimeParam 接口签名 Scope 变更登记（v1.1）

> 发件：3号位（规则参数治理 Owner）｜ 致：4号位（前端）｜ 抄：0号位、1号位、2号位、5号位
> 日期：2026-10-09 ｜ 版本：v1.1（增量变更登记，不改动 v1.0 契约件）
> 依据：0号位 对 3号位 代码专项审核（BLOCKED，commit `4caebef0`）P0-01 / P0-03 整改
> 性质：红线 #5 接口即契约——`ListAsync` 签名变更登记；本件为 v1.0 契约的增量修订，**唯一口径仍以 v1.0 契约 + 本件为准**

---

## 〇、变更总览

| # | 契约件（v1.0） | 变更点 | 起因 |
|---|---|---|---|
| 1 | `3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md` | `ListAsync` 增 `actorUserId` + List 按 Department Scope 过滤 | 0号位 审核 P0-01 |
| 2 | `3号位致4号位_StageLeadTimeParam维护接口契约_v1.0_20260928.md` | `ListAsync` 增 `actorUserId` + List 按 Factory/Dept Scope 过滤 + 写路径目标归属校验 | 0号位 审核 P0-03 |

> **前端共性影响：HTTP 请求参数与请求体零变化**——`actorUserId` 为服务层受信主体参数，由 Controller 从登录身份取（`GetCurrentUserId()`），前端无感。仅新增 403 失败语义与既有 Scope 契约一致。

---

## 一、P0-01 — TaskSplitRuleConfig List 业务范围过滤

**接口签名**（服务层，HTTP 层无感）：

```
Task<PageResult<TaskSplitRuleConfigDto>> ListAsync(
    int? materialId, int? productionDepartmentId, bool? isActive,
    int actorUserId, int pageIndex = 1, int pageSize = 20, CancellationToken ct = default)
```

**后端语义**（`TaskSplitRuleConfigGovernanceService.ListAsync` + `ResolveAllowedDepartmentIdsAsync`）：

| 场景 | 行为 |
|---|---|
| Global 用户 | 不过滤（null），全放行 |
| 非 Global + 有 Department 授权 | 授权部门（DeptCode → `ProductionDepartment.Id`）IN 约束 **COUNT + 列表** |
| 非 Global + 无任何 Department 授权 | fail-closed 空页（`items=[] / total=0`）——禁止先全量返回再前端隐藏 |
| 显式传入未授权部门 | **403** `ScopeViolationException`（防 IDOR 探测，与写路径 Scope 契约一致） |

**响应**：`PageResult<TaskSplitRuleConfigDto>` 结构不变；新增 403 失败信封 `{ code:403, message:"业务范围越界（Department）：…" }`。

**前端影响**：请求参数零变化；收到 403 按既有 Scope 契约提示（无该部门可见性即无操作权）。

---

## 二、P0-03 — StageLeadTimeParam 列表与写路径业务范围

**接口签名**（服务层，HTTP 层无感）：

```
Task<IReadOnlyList<StageLeadTimeParamDto>> ListAsync(
    string? factoryCode, string? stageCode, string? productionDeptCode, bool? isActive,
    int actorUserId, CancellationToken ct = default)
```

**GET 列表语义**（`StageLeadTimeParamGovernanceService.ListAsync`）：

| 场景 | 行为 |
|---|---|
| Global 用户 | 不过滤 |
| 非 Global + Factory 授权 | `AND FactoryCode IN @Factories` 约束 |
| 非 Global + 无 Factory 授权 | fail-closed 空列表（每行必有 FactoryCode，AND 交集语义下不可见） |
| 非 Global + Department 授权 | `AND (ProductionDeptCode IS NULL OR ProductionDeptCode IN @Depts)`（NULL 部门行放行——无部门归属不越权） |
| 维度未授权 | 该维度不施加约束 |
| 无任何授权 | fail-closed 空列表 |

**写路径**（Create/Update/Deactivate 经 `EnsureTargetInScopeAsync`）：非 Global 用户 **FactoryCode 必在 Factory 授权内**、**ProductionDeptCode 非空时必在 Department 授权内**；越界 → **403** `ScopeViolationException`。Update 对**老归属 + 新归属分别校验**（防 IDOR 式跨工厂/跨部门记录迁移）。

**错误表增补**（v1.0 §三 追加行）：

| 场景 | 状态码 |
|---|---|
| 业务范围越界（Factory/Department 不在授权） | 403 |

---

## 三、反证测试（可复现日志，0号位 审核 §八「提交可复现测试日志」）

| 项 | 用例 | 结果 |
|---|---|---|
| P0-01 显式越权 | 集成 `ListAsync_非Global显式越权部门_抛ScopeViolationException` | 403 ✅ |
| P0-01 无授权空页 | 集成 `ListAsync_非Global无部门授权_failClosed空页` | items=[]/total=0 ✅ |
| P0-02 旧记录越权 | 集成 `UpdateAsync_旧记录部门越权_抛ScopeViolationException` | 403 ✅ |
| P0-03 List IN 拼接 | 单测 `ListAsync_非Global工厂与部门授权_拼入IN条件` | SQL 含 IN 约束 ✅ |
| P0-03 无授权空列表 | 单测 `ListAsync_无任何授权_返回空列表且不查库` | 空列表 + 零查询 ✅ |
| P0-03 工厂越权写 | 单测 `CreateAsync_工厂越权_抛ScopeViolationException` | 403 ✅ |

（单测 27 绿 / 集成 14 绿 / 编译 0 错；既有分页用例在 `_dataScope` 默认 Global 下不受影响。）

---

## 四、说明

- **不改动** v1.0 契约件与 4号位 前端落地回执所引用之拍板件（保持只读参照稳定）；本件为 v1.1 增量登记，两契约唯一口径 = v1.0 件 + 本件。
- 0号位 审核后续项（P1-02 并发互斥 / P1-03 审计原子性 / P1-04 Scope 扩散）与本件无接口签名冲突，另行交付。

**发件**：3号位 ｜ 日期：2026-10-09 ｜ 版本：v1.1（增量变更登记）
