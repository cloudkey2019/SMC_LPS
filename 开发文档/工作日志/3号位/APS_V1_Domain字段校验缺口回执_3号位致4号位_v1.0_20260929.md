# Domain 字段校验缺口回执 —— 3号位 → 4号位

> **文档性质**：4 项字段校验缺口（F1-F4）+ 1 建议（R5）落地回执（回应 `4号位-2026-09-29-Domain后端字段校验缺口-给3号位.md`）
> **发送人**：3号位（Governance Controller / DomainDefinition 实体 Owner）
> **接收人**：4号位（前端）
> **日期**：2026-09-29 ｜ **版本**：v1.0
> **状态**：后端 4 项 + R5 已全部落码并验证；前端 1 处对齐待 4号位 处理

---

## 〇、速答清单

```
R1 sortOrder 边界（0-9999）                     : 接受（已落码）
R2 DomainKey 字符集 + 长度下限 (≥4)              : 接受（已落码，^[A-Z][A-Z0-9_-]{3,49}$）
R3 scopeType 枚举大小写（仅 FAMILY/FACTORY_FAMILY）: 接受（已落码，严格大小写）
R4 DomainName 长度上限（≤ 64）                    : 接受（已落码，2-64）
R5 4 项是否统一到一处校验                        : 接受（已落码，本就集中在服务层 ValidateCoreAsync）
```

---

## 一、落码明细（工作完成情况）

后端 4 项校验 + R5 全部落在 3号位 服务层 `LPS.APS.Application/Services/DomainDefinitionGovernanceService.cs` 的 `ValidateCoreAsync`（`CreateAsync` / `UpdateAsync` 入口均先经此校验），Controller 侧零改动（已统一把 `InvalidOperationException` 映射为 `400 ApiResponse.Fail`）。

| # | 字段 | 落地规则 | 实现要点 |
|---|---|---|---|
| F1 | `sortOrder` | `0 ≤ x ≤ 9999`，越界抛 400 | 新增常量 `MinSortOrder=0 / MaxSortOrder=9999` |
| F2 | `domainKey` | `^[A-Z][A-Z0-9_-]{3,49}$`（首字符大写字母，4-50 字符，仅 A-Z0-9_-） | 新增静态 `DomainKeyPattern` 正则，替换原「仅长度 ≤50」校验 |
| F3 | `scopeType` | 全值精确匹配 `FAMILY` / `FACTORY_FAMILY`，严格大小写 | 移除原 `ToUpperInvariant()` 宽容归一化 |
| F4 | `domainName` | `2 ≤ len ≤ 64` | 原 `MaxDomainNameLength=200` 收紧为 64，补下限 `MinDomainNameLength=2` |
| R5 | 集中校验 | 校验集中在服务层 `ValidateCoreAsync` | 本就集中，本次同处增强，与 `RbacValidator` 集中校验模式一致 |

> 命中即抛 `InvalidOperationException` → Controller 返回 `400` + 可读 message，符合 §六 验收（含中文 domainKey 字符集拒绝）。

---

## 二、验证结果

| 项 | 结果 |
|---|---|
| `dotnet build` | 0 错误 |
| `dotnet test --filter DomainDefinitionGovernance` | **13/13 通过**（0 失败 0 跳过，含集成测试实跑） |

---

## 三、测试固件同步（为匹配新契约）

3号位 侧测试固件已同步新契约（域键原用小写 / 2 字符，会撞新字符集结算），改动如下、断言行为不变：

- `LPS.APS.Tests/Unit/DomainDefinitionGovernanceServiceTests.cs`：固件域键 `"D1"/"D2"` → `"DOM1"/"DOM2"`（满足 4 字符下限）。
- `LPS.APS.Tests/Integration/DomainDefinitionGovernanceIntegrationTests.cs`：`_uniqueSuffix` 由小写 GUID hex 转大写（`.ToUpperInvariant()`），`"badfamily"/"changed"` 后缀改大写，满足 `[A-Z0-9_-]` 字符集。

---

## 四、请 4号位 对齐（1 处需改 + 2 处知会 + 验收交接）

1. **【需改】前端 domainKey 上限 64→50**：`src/views/Aps/Domain.vue` 现正则 `/^[A-Z][A-Z0-9_-]{3,63}$/`（上限 **64**），与后端契约 `{3,49}`（上限 **50**，源自 DDL 列宽）不一致。请收紧为 **`/^[A-Z][A-Z0-9_-]{3,49}$/`**，否则前端放行 51-64 字符会被后端 400 拦下。
2. **【知会】sortOrder=0 语义**：`0` 仍走既有「未指定 → 回落默认 100」逻辑（非存 0）。§六 V1 仅断言 HTTP 码，不受影响；若需「0 作为真实排序值落库」请告知，3号位 再改回落逻辑。
3. **【知会】** 4号位 侧 §六 V1-V8 实测 + §七 D14-D17 动态断言，由你方按既定验收清单跑；后端已就绪。

---

## 五、交付状态汇总

| 项 | 状态 | 归属 |
|---|---|---|
| F1-F4 后端校验 + R5 集中校验 | ✅ 已落码 | 3号位 |
| 后端构建 + 测试验证 | ✅ 0 错 / 13 绿 | 3号位 |
| 前端 domainKey `{3,63}`→`{3,49}` 收紧 | ⏳ 待改 | 4号位 |
| §六 V1-V8 + §七 D14-D17 验收 | ⏳ 待跑 | 4号位 |

---

**发送人**：3号位 ｜ **接收人**：4号位 ｜ **日期**：2026-09-29 ｜ **版本**：v1.0 ｜ **状态**：后端已收口，待前端对齐 + 验收