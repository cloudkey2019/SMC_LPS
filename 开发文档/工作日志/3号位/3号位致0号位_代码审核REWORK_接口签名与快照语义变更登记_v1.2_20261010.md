# 3号位 → 0号位/4号位：代码审核 REWORK 整改接口签名与快照语义变更登记（v1.2）

> 发件：3号位（规则参数治理 Owner）｜ 致：0号位（PM/仲裁）、4号位（前端）｜ 抄：1号位、2号位、5号位
> 日期：2026-10-10 ｜ 版本：v1.2（增量变更登记，不改动 v1.0/v1.1 契约件）
> 依据：0号位 对 Commit `ccb2b15` 再次专项审核（`3号位致0号位_代码审核修复完成报告_0号位再次代码审核结果_B1_20261010.md`，REWORK/BLOCKED）
> 性质：红线 #5 接口即契约——查询接口签名变更登记 + 快照装载语义变更登记；**唯一口径 = v1.0/v1.1 契约 + 本件**

---

## 〇、变更总览

| # | 契约件/区域 | 变更点 | 起因 |
|---|---|---|---|
| 1 | `SetupRule` #1/#5 列表 + 详情 | `ListAsync`/`GetByIdAsync` 增 `actorUserId`；列表/详情按 Department Scope 过滤 | 3-NEW-P0-01 |
| 2 | `RoutingOperation` 列工序 | `ListOperationsAsync` 增 `actorUserId`；列表按 Department Scope 过滤 | 3-NEW-P0-02 |
| 3 | Setup/OPM 写路径越界 | Controller `HandleError` 补 `ScopeViolationException`→**403**（原 500） | 3-NEW-P1-02 |
| 4 | TaskSplitRuleConfig 列表 | 非 Global + 显式部门参数 → 结果/Total 只含该部门（等值过滤） | 3-NEW-P1-01 |
| 5 | BatchPolicy 发布资格 | `IsSnapshotEligible` 补 Min/Max **非负**校验；发布前增同键生效窗口**重叠**校验 | 3-NEW-P0-03 / R02 |
| 6 | BatchPolicy 快照装载 | 子块 **JSON 损坏 → 显式失败**（不再静默空列表），区分「未配置/空配置/损坏」三态 | 3-NEW-P0-04 |

> **前端共性影响：HTTP 请求参数与请求体零变化**——`actorUserId` 为服务层受信主体参数，由 Controller 从登录身份取（`GetCurrentUserId()`），前端无感。仅新增/统一 **403** 失败语义。

---

## 一、SetupRule 列表/详情业务范围（3-NEW-P0-01）

**接口签名**（服务层，HTTP 层无感）：
```
Task<IReadOnlyList<SetupRuleDto>> ListAsync(long ruleSetVersionId, string ruleType, int actorUserId, CancellationToken ct = default);
Task<SetupRuleDto?> GetByIdAsync(long id, int actorUserId, CancellationToken ct = default);
```

**列表语义**（`SetupRuleService.ListAsync`，按规则 `ProductionDepartmentId` 回带 DeptCode 过滤）：

| 场景 | 行为 |
|---|---|
| Global 用户 | 不过滤，返回该版本+类型全部规则 |
| 非 Global + 有 Department 授权 | 仅返回 `ProductionDepartmentId` 属授权部门的规则（越权部门规则隐藏，防通过版本 Id 窥探） |
| 非 Global + 无任何 Department 授权 | fail-closed 空列表（禁止先全量再前端隐藏） |

**详情语义**（`GetByIdAsync`）：非 Global 用户目标规则部门不在授权 → **403** `ScopeViolationException`（防通过 Id 窥探 B 部门规则）。

## 二、RoutingOperation 列工序业务范围（3-NEW-P0-02）

**接口签名**（服务层）：
```
Task<IReadOnlyList<RoutingOperationDto>> ListOperationsAsync(int materialId, int actorUserId, CancellationToken ct = default);
```

**列表语义**（`RoutingOperationGovernanceService.ListOperationsAsync`，SQL 层部门 IN 过滤）：

| 场景 | 行为 |
|---|---|
| Global 用户 | 不过滤，按 MaterialId 返回全部工序 |
| 非 Global + 有 Department 授权 | `AND ProductionDepartmentId IN @授权部门`（同物料 A/B 部门工序隔离） |
| 非 Global + 无任何 Department 授权 | fail-closed 空列表（不查库） |

> 正常 Routing 事实仍来自正式源（2号位），本治理查询仅按部门 Scope 过滤，不新建第二套工艺平台。

## 三、Setup/OPM 越权 HTTP 403 统一（3-NEW-P1-02）

- `SetupRuleController.HandleError` 与 `RoutingOperationController.HandleError` 补 `ScopeViolationException → StatusCode(403, {code:403, message})`（与 TaskSplitRuleConfigController / StageLeadTimeParamController 既有 403 行为一致）。
- 服务层越权已抛 `ScopeViolationException`（DML 零变更）；本次仅修正 HTTP 状态码 500→403。

## 四、TaskSplitRuleConfig 列表显式过滤语义修正（3-NEW-P1-01）

v1.1 登记的 List 语义表更新（非 Global 分支）：

| 场景 | v1.1（旧） | v1.2（新） |
|---|---|---|
| 非 Global + 授权 A+B + 显式传部门 A | `IN (A,B)`（返回 A+B，Total 含 B） | `IN (A,B) AND =A`（仅返回 A，Total 只含 A）——尊重显式过滤条件 |
| 非 Global + 显式传未授权部门 | 403 | 403（不变） |

> 语义修正：非 Global 查询**同时尊重「授权集合」与「用户显式条件」**，COUNT 与页结果一致。

## 五、BatchPolicy 发布资格补强（3-NEW-P0-03 / R02）

**`IsSnapshotEligible` 补 Min/Max 非负**（与治理写路径 `Validate` 同口径，3-NEW-P0-03）：
- `MinExecutionBatchQty < 0` 或 `MaxExecutionBatchQty < 0` → 发布资格判定失败 → 存量负值行阻断参数集发布（严禁默默当有效）。

**发布前生效窗口重叠校验（R02 反证）**：`GetActiveRulesForSnapshotAsync` 在单行资格校验后，按 `(MaterialId, ProductionDepartmentId)` 分组检查**生效时间窗重叠**（半开区间 `[EffectiveFrom, EffectiveTo)`，NULL 无界，与治理写路径唯一有效期同口径）——存量重叠行阻断发布并列 Id，杜绝重复键快照。

## 六、BatchPolicy 损坏装载显式失败（3-NEW-P0-04，B-010 三态）

`TaskSplitRuleConfigProjector.ExtractRules` 语义变更：

| 子块状态 | 旧行为 | 新行为 |
|---|---|---|
| 子块缺失 / `null` | 空列表 | 空列表（合法未配置，不变） |
| 显式空配置 `"BatchPolicy":[]` | 空列表 | 空列表（明确空配置，不变） |
| **JSON 结构损坏** | 空列表（fail-open） | **抛 `SetupRuleDataRedLineException`（显式失败）** |

> 依据 B-010：**「完全无匹配」≠「已有配置但损坏」**。损坏子块不再被误当「缺配兜底」消费；治理读/发布/装载（`FrozenStrategySnapshotProvider`）均显式失败（422），版本追溯不失真。装载端不再承载匹配终端 ④ 的 fail-closed 信号位（仍由 1号位 消费侧执行），但损坏本身不再静默吞掉。

## 七、反证测试（可复现，单测/集成）

| 项 | 用例 |
|---|---|
| P0-01 | Setup List：授权 A 仅返回 A、无授权空列表、Global 全量；GetById 越权 403 |
| P0-02 | OPM List：授权 A 仅返回 A 部门工序、无授权空列表（不查库）、Global 全量 |
| P1-02 | Setup/OPM 越权服务层抛 `ScopeViolationException`（Controller 403 映射已接线） |
| P1-01 | TaskSplit List：授权 A+B 传 A → Items/Total 只含 A |
| P0-03 | 存量 `Min=-5` 行 → 发布阻断并列 Id |
| P0-04 | 缺子块→空、空配置→空、损坏→抛（三态反证） |
| R02 | 同键重叠窗口存量 → 发布阻断 |

## 八、前端影响汇总

- HTTP 请求参数/请求体 **零变化**（actorUserId 服务层注入）。
- 新增 403 失败语义（Setup 列表/详情、OPM 列表越界）——按既有 Scope 契约提示。
- BatchPolicy 发布：损坏 JSON 现在发布报错（422 数据红线），不再静默成「无规则」——前端按 422 展示「批量策略配置损坏」。

**发件**：3号位 ｜ 日期：2026-10-10 ｜ 版本：v1.2
