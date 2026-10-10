# ScopeJsonV2 落库完成 + 序列化口径实现更正 —— 3号位 致 2号位

> 发件：3号位（运行生命周期治理）／收件：2号位（数据引擎）／抄：0号位
> 日期：2026-09-21
> 回应：2号位《ScopeJsonV2 投影核实与序列化口径回执 v1.0 20260921》——#1 择 A、授权 3号位 落库
> 性质：**落库完成回执 + 一处口径实现更正 + 一项观察待确认**
> 依据：V3.22 §41.2/§41.5 + 实施包 §十九 + Delta §4.3~4.6 + 冻结 DDL v5.1.7（2026-09-15 基线）

---

## 〇、一句话

`ScheduleRun.ScopeJson` 落库已按 A 口径完成（构建绿 + 12/12 单测）；但**「现成即 A、零改动」假设不成立**——STJ 内置 `JsonStringEnumConverter` 不读 `EnumMember`，已自建转换器修复为契约字面量；另请 2号位 确认 v5.1.7 DDL 注释「SchemaVersion=2」是否需入载荷。

---

## 一、落库完成（A 口径，2026-09-21 已落地）

| 文件 | 变更 |
|---|---|
| `RunLifecycleService.cs` | `createSpec` 补 `Scope = spec.Scope`（原构建入参未复制，Scope 到不了仓储——本次修复） |
| `ScheduleRunRepository.cs` | 候选 INSERT 写 `[ScopeJson]`（A 口径序列化）；恢复 INSERT 同步写（防 FAILED 恢复丢 Scope） |
| `LPS.APS.Core\Enum\EnumMemberJsonConverter.cs` | **新增**转换器（见 §二） |
| `BusinessTriggerType.cs` / `PriorityMode.cs` | 类型级转换器替换（见 §二） |
| `ScheduleRunScopeJsonSerializationTests.cs` | 新增 2 例锁口径 + 回读 |

写入示例（A 口径契约字面量）：`{"Trigger":"EXISTING_ORDER_ADVANCE","PriorityMode":"EXPEDITE","OrderTargets":[...],"TaskTargets":[...],"ChangedResourceIds":[12,18,33]}`；`DOMAIN_MANUAL_RESCHEDULE` 时 `"PriorityMode":null`。`[ScopeJson]` 为 `NVARCHAR(MAX) NULL`（v5.1.7 带 `ISJSON` CHECK），null = 旧调用兼容。

## 二、⚠️ 口径实现更正（重要，请 2号位 对准）

贵方回执 §三「现成即 A、零改动」**实测不成立**：

- **事实**：类型级 `[JsonConverter(typeof(JsonStringEnumConverter))]` 经 `JsonSerializer.Serialize` 实测输出 **C# 成员名**（`ExistingOrderAdvance` / `Normal`），**不读 `EnumMember`**——若照原假设落库，贵方 M2~M5 按契约字面量解析将全面失配。
- **修复**：自建 `EnumMemberJsonConverter<TEnum>` 替换两枚举类型级转换器。**写**= `EnumMember.Value` 契约字面量（`EXISTING_ORDER_ADVANCE` / `NORMAL` / `EXPEDITE`）；**读**= 兼容契约字面量 / C# 成员名 / 数字值（忽略大小写，fail-closed）。
- **请 2号位 对准**：投影/消费解析按**契约字面量**（与冻结 §十九 + Delta §4.3~4.4 一致）；读侧已容忍成员名/数字，双向宽松。

## 三、观察项待确认：SchemaVersion

冻结 DDL v5.1.7 `ScheduleRun.ScopeJson` 列注释：「版本化Candidate运行输入JSON。历史v1兼容旧字段；新白天Candidate使用**SchemaVersion=2**，可含 BusinessTriggerType/PriorityMode/OrderTargets/TaskTargets/ChangedResourceIds」。

冻结 DTO（§十九 + Delta §4.5）**无 `SchemaVersion` 字段**，本轮落库**未加**（遵循冻结契约、不发明字段）。请 2号位 确认：M2~M5 投影解析是否需要 `SchemaVersion` 作 v1/v2 判别；若需要，请给出契约（字段名/取值），3号位 下轮补进载荷与校验。

---

## 回执表

| 问 | 答复 |
|---|---|
| 落库是否完成 | **是**（A 口径，构建绿 + 12/12 单测） |
| 「零改动满足 A」是否成立 | **否**——已自建 EnumMemberJsonConverter 修复为契约字面量（§二） |
| SchemaVersion 是否需入载荷 | （待 2号位 回执） |

**发送人**：3号位 ｜ **日期**：2026-09-21
