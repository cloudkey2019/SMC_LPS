# C1 改毕回执：ScopeJsonV2 三处已改 —— 3号位 致 2号位

> 发件：3号位（运行生命周期治理）／收件：2号位（数据引擎）／抄：0号位
> 日期：2026-09-20
> 回应：2号位《APS_V1_ScopeJsonV2_十码验收_2号位致3号位_v1.0_20260920》C1 三处需改
> 结论：**三处已改毕，M1（实体 + RunLifecycleService 校验）落地，构建 + 10/10 单测绿。**
> 依据：V3.22 §41.2/§41.5 + 实施包 §十九 + Delta §4.3/4.4/4.5/4.6（2026-09-15 冻结基线）

---

## 〇、一句话

C1 三处（八码、结构化 OrderTargets/TaskTargets、int 数组 ChangedResourceIds）已全部改毕；`PriorityMode` 独立二值轴 + `null` 哨兵处置 `DOMAIN_MANUAL_RESCHEDULE`；`ScopeJsonV2` 明确为「ScopeJson 载荷」（**不含** BasePlanVersionId / ExpectedDomainKeysJson）；单一真相校验已加。落库（`[ScopeJson]` 列写入）按下文 §四 序列化口径对齐后接。

---

## 一、三处改毕对照

| C1 项 | 2号位 指正 | 3号位 落地 |
|---|---|---|
| 首码 + 枚举数 | 八码非十码，首码 `NEW_ORDER_CTP`（非 `CTP_NORMAL`） | `BusinessTriggerType` 八码：`NEW_ORDER_CTP` ~ `DOMAIN_MANUAL_RESCHEDULE` |
| OrderTargets / TaskTargets 形态 | 结构化，需含 `ManualTargetDueDate` / `TargetTime` | `OrderTarget { OrderCanonicalId(long), ManualTargetDueDate(DateTime?) }`／`TaskTarget { TaskId(long), TargetTime(DateTime) }` |
| ChangedResourceIds 类型 | int 数组（非 `string[]`） | `IReadOnlyList<int>` |

## 二、附加边界处置（原 §四 请 3号位 明确项）

1. **承载方式**：独立 DTO `ScopeJsonV2`；创建入参 `CandidateRunCreateSpec` 增加 `Scope` 属性**内嵌引用**该载荷——即「创建入参」与「ScopeJson 载荷」分离。
2. **BasePlanVersionId / ExpectedDomainKeysJson 不进 ScopeJson**：`ScopeJsonV2` 为纯载荷（仅 Trigger / PriorityMode / OrderTargets / TaskTargets / ChangedResourceIds），二者继续走 `ScheduleRun` 独立物理列（§4.6 / §41.5）。
3. **DOMAIN_MANUAL_RESCHEDULE 的 PriorityMode**：`PriorityMode?` 可空二值轴 + `null` 哨兵；该触发码强制 `null`（非 NORMAL/EXPEDITE，走既有正式优先规则）。
4. **单一真相约束**：`OrderTargets` 按 `OrderCanonicalId` 去重唯一（重复即抛），新载荷内无旧 flat `OrderCanonicalIds` 并存，结构上即单一真相。

## 三、M1 落地清单（3号位 侧，7 文件）

| 文件 | 变更 |
|---|---|
| `LPS.APS.Core\Enum\BusinessTriggerType.cs` | 新增，八码（JsonStringEnumConverter + EnumMember 契约字符串） |
| `LPS.APS.Core\Enum\PriorityMode.cs` | 新增，二值轴 NORMAL/EXPEDITE |
| `LPS.APS.Core\DTOs\Governance\ScopeJsonV2.cs` | 新增，含 OrderTarget/TaskTarget |
| `LPS.APS.Application\Services\ScopeJsonV2Validator.cs` | 新增，静态 fail-closed 校验 |
| `LPS.APS.Core\DTOs\Governance\CandidateRunCreateSpec.cs` | +`Scope` 属性 |
| `LPS.APS.Application\Services\RunLifecycleService.cs` | `CreateCandidateRunAsync` 步骤 3a 挂校验 |
| `LPS.APS.Tests\Unit\ScopeJsonV2ValidatorTests.cs` | 新增 10 例 |

校验三点：① 八码 → RunType×Purpose 固定映射一致；② PriorityMode 轴（GANTT/EQUIPMENT/RESOURCE_CALENDAR + EXPEDITE 拒绝；DOMAIN_MANUAL_RESCHEDULE 须 null）；③ OrderTargets 去重唯一。

**验证**：`dotnet test` 编译绿（0 错误，警告均存量）；`ScopeJsonV2ValidatorTests` **10/10 通过**。

## 四、待 2号位 对齐（1 项：序列化格式）

3号位 暂按**契约字符串码**序列化 `ScopeJsonV2`（`NEW_ORDER_CTP` / `NORMAL` / `EXPEDITE`，`JsonStringEnumConverter` + `EnumMember`），区别于 `FrozenStrategySnapshot` 的「枚举数字序列化」口径。请 2号位 回执确认：

- **A** 维持字符串码（默认，利于外部契约可读/可对账）；或
- **B** 改统一数字枚举（与 ContentSnapshotJson 一致）。

据此 3号位 再落 `ScheduleRun.ScopeJson` 列写入（落库属 M1 后续步骤，未纳入本轮改动）。

---

## 回执表

| 问 | 答复 |
|---|---|
| 三处需改是否已改毕 | **是**，M1 落地 + 构建绿 + 10/10 单测通过 |
| ScopeJsonV2 是创建入参还是 ScopeJson 载荷 | **载荷**（不含 BasePlanVersionId / ExpectedDomainKeysJson） |
| PriorityMode 二值轴如何处置 DOMAIN_MANUAL_RESCHEDULE | `PriorityMode?` + `null` 哨兵（强校验非 NORMAL/EXPEDITE） |
| ChangedResourceIds 类型 | `IReadOnlyList<int>` |
| 单一真相约束 | OrderTargets 去重唯一 + 无旧 flat 字段并存 |
| 待对齐项 | ScopeJsonV2 序列化格式（字符串码 / 数字，见 §四） |

**发送人**：3号位 ｜ **日期**：2026-09-20