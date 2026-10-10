# APS V1 · Business Scope 生成侧装配落码完成 —— 3号位 致 2号位

**版本**：v1.0
**日期**：2026-09-23
**发送人**：3号位（BusinessScope 生成 Owner / ScopeJson Schema Owner）
**接收人**：2号位（透传方）
**抄送**：1号位（遵守方）、0号位（裁决备查）、4号位（前端备查）
**依据**：3号位《Candidate统一裁决_BusinessScope契约定案及收口 v1.1》§三/§四（落码归属表 #1/#3）｜0号位《统一裁决回复》§五/§七｜2号位《BusinessScope载体传递路径澄清 v1.0》（采纳路径 A）

---

## 一、知会要点

3号位 已按契约定案 §四 落码归属表 **#1 + #3（生成侧）** 落码完毕，整解决方案编译 **0 错误**。2号位 可据此按下文 §三 接 **透传端**（归属表 #6）与 **RunScope 属性自领**（#2），不再有生成侧缺口。

## 二、3号位 已完成落码（生成侧，真空④ 3号位 职责）

| # | 文件 | 动作 | 状态 |
|---|---|---|---|
| **#1a** | `LPS.APS.Core/DTOs/Scope/BusinessScopeDto.cs` | 新增 DTO（`IsGlobal` / `ScopeType` / `ScopeValues`），真源对齐 `DataScopeTypes` 常量 | ✅ 已落码 |
| **#1b** | `LPS.APS.Core/DTOs/Governance/ScopeJsonV2.cs` | 新增 `BusinessScope?` 属性（SchemaVersion 版本化扩展，历史载荷缺省 null=fail-safe） | ✅ 已落码 |
| **#3** | `LPS.APS.Application/Services/RunLifecycleService.cs` | `CreateCandidateRunAsync` 新增 3a' 装配段 + 私有 `BuildBusinessScopeAsync` | ✅ **本次落码** |

### 生成侧装配语义（2号位 透传时须知）

1. **服务器端权威生成**：`CreateCandidateRunAsync` 在 `ScopeJsonV2Validator.Validate` 之后、Domain 校验之前，以 `actorUserId` 经 `IDataScopeService.ResolveScopeAsync(userId, ct)` 解析 → 装配 `spec.Scope.BusinessScope`，并**覆盖**前端/调用方可能自传的该值（业务范围=授权边界，不得由调用方自定）。
2. **Trigger→维度映射**（契约定案 §1.3，经 `BusinessTriggerType` 判定）：

   | BusinessTriggerType | 维度（DataScopeTypes） | 数据来源 |
   |---|---|---|
   | NEW_ORDER_CTP / NEW_ORDER_IMPACT / NEW_ORDER_INSERT / EXISTING_ORDER_ADVANCE | `ProductFamily` | ResolveScopeAsync 解析的产品族码集 |
   | GANTT_ADJUSTMENT / DOMAIN_MANUAL_RESCHEDULE | `ResourceOrgGroup` | 生产组织码集 |
   | EQUIPMENT_FAILURE / RESOURCE_CALENDAR_CHANGE | `Global` | IsGlobal=true（中断级不涉用户业务范围） |

3. **兜底**：`DataScopeContext.Global` → `IsGlobal=true`；维度未授权（`GetValues` 返回 null / 空集）→ `IsGlobal=false` + `ScopeValues` 空集（fail-safe，交 1号位 范围不足返回 `BUSINESS_SCOPE_INSUFFICIENT`）。`ScopeValues` 键形态=业务键字符串（产品族码 / ResourceOrgGroup 码），对齐 `EnsureInScopeAsync` 的 `value` 同口径。

## 三、待 2号位 接手（透传端 + 属性自领）

契约定案 §四 归属表 **#2 / #6** 为 2号位 辖区，3号位 不越位，清单如下：

| # | 落点 | 动作 |
|---|---|---|
| **#6 透传** | `PeggingOrchestrator.BuildRunScopeAsync`（2号位） | 反序列化 `ScopeJsonV2` → 原样映射进 `DomainSolveRequest.RunScope.BusinessScope`；**不判权限、不新建列、不新增 DDL** |
| **#2 属性** | `LPS.APS.Core/Dto/DomainSolveRequest.cs` 的 `RunScope`（2号位 自领） | 新增 `BusinessScopeDto? BusinessScope` 属性（新增属性，不改既有 6 字段签名；1号位 认收 + 消费 `BUSINESS_SCOPE_INSUFFICIENT`） |

透传端实现提示（2号位 现值写入，无需回执，出现契约歧义再提）：
- `ScopeJsonV2.BusinessScope` 为 nullable 可选块——历史载荷缺省 null；透传时 `BusinessScope` 为 null → 直接映射进 `RunScope.BusinessScope=null`（1号位 侧按「无授权 fail-safe」处理，不设默认非空）。
- 装配端 `BusinessScopeDto` 已由 3号位 出到 `Core/DTOs/Scope/`，2号位 于 `BuildRunScopeAsync` 直接 `dotnet build` 收口即可。

## 四、红线核验（3号位 自查）

- **红线#5（接口即契约）**：`IRunLifecycleService` / `DomainSolveRequest` 既有签名**零改动**；BusinessScope 为**新增属性/新增分支**，未改既有 6 字段 / 既有 4 块签名。
- **红线#6（DB schema 2号位 专属）**：3号位 **零 DDL**——BusinessScope 随 `ScopeJsonV2` 并入既有 `ScheduleRun.ScopeJson` 列持久化，不建列不改表。
- **红线#3（引擎无业务规则）**：装配逻辑在 Application（3号位 RunLifecycleService），未触 Engine / Scheduling。
- **红线#4（master 返回列表）**：`DataScopeContext.GetValues` 返回集合，BusinessScope 消费取值非单对象 `.First()` 盲取。

## 五、与 P1-2 / 业务保护识别字段的关系（正交，仍待 0号位）

- 本装配 = **用户业务范围授权框**（权限治理），非 **Firm/Frozen 业务保护**（订单业务属性，已确认不可调整）——两者正交叠加（契约定案 §六）。
- 业务保护识别字段 **(i) IsInFrozenZone / (ii) ScopeJson 保护集合** 仍待 0号位 正交评审闭合；P1-2 过渡期仍以 2号位 `FirmHorizonDays` 兜底。本函不越位代判。

## 六、待办（归属明确，3号位 不代办）

| # | 待办 | 归属 | 依赖 |
|---|---|---|---|
| 1 | `BuildRunScopeAsync` 透传映射（§三 #6） | 2号位 | 生成侧已就绪（本函） |
| 2 | `RunScope.BusinessScope` 属性 + 1号位 认收 `BUSINESS_SCOPE_INSUFFICIENT`（§三 #2） | 2号位/1号位 | 属性 2号位 自领先行 |
| 3 | 业务保护识别字段 (i)/(ii) | 0号位 | 正交评审 |
| 4 | P1-2 expedite-impact 落地（宿主 + Base 比对派生） | 2号位/3号位 | 另行排期 |

---

**发送人**：3号位 ｜ **日期**：2026-09-23 ｜ **版本**：v1.0
**对应**：契约定案 v1.1 §三/§四｜2号位《载体传递路径澄清 v1.0》｜0号位《统一裁决回复》§五/§七
**本函性质**：生成侧落码完成知会 + 透传端接手清单——无契约口径变更，供 2号位 接 #2/#6 时参照。