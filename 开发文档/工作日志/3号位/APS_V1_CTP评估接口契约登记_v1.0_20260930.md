# APS V1 CTP 评估接口契约登记（v1.0）— 3号位

- **发件人**：3号位（CTP 后端 owner / 契约与方案治理）
- **收件人**：4号位（前端）、1号位/2号位（引擎接入时消费）
- **日期**：2026-09-30
- **性质**：**新契约登记**（CTP 接口从未进入任何冻结契约；v1.5 实施包无 CTP 段），非改冻结
- **关联**：`4号位-2026-09-30-CTP评估C路径-后端域键可选化-给3号位.md`、`APS_V1_CTP评估C路径回执_3号位致4号位_v1.0_20260930.md`
- **落码状态（2026-09-30 更新）**：契约层已落码——DTO 7 字段 + domainKey 可选化、IDomainResolver/DomainResolver（红线 #4 歧义 400）、ICtpService/CtpService（resolve+F-G4，试算仍 501）、CtpController 错误映射（400/403/501）已实现且 9 单测绿；**核心试算能力仍待 1号位/2号位 接入（501）**

---

## 一、接口定义

| 项 | 值 |
|---|---|
| 方法/路径 | `POST /api/ctp/evaluate` |
| 宿主 | `LPS.APS.Web/Controllers/CtpController.cs`（3号位 薄层） |
| 权限码 | `aps.ctp.view`（查看）/ `aps.ctp.evaluate`（发起） |
| 校验链 | F-G4 `_dataScopeService.EnsureInScopeAsync(userId, Domain, domainKey, ct)` **不撤** |
| 现状返回 | 501「核心能力由 1号位/2号位 接入」（骨架期） |
| 未来返回 | `CtpResultDto` 由 1号位/2号位 接入引擎时定（含受影响订单、最早交期、瓶颈、保护冲突等，对齐前端 CtpImpactedOrder/CtpDomainResult） |

---

## 二、请求体契约（前后端对齐）

### 2.1 字段表（前端 `CtpInput` ↔ 后端 `CtpEvaluateRequest`）

| 字段 | 类型 | 必填 | 前端现状 | 后端现状 | 说明 |
|---|---|---|---|---|---|
| `orderCanonicalId` | string | ✅ | 有 | **缺** | 新订单 ERP/内部统一编号 |
| `materialCode` | string | ✅ | 有 | **缺** | 用于 resolve 域键 |
| `quantity` | number | ✅ | 有 | **缺** | 数量 |
| `factoryCode` | string | ✅ | 有 | **缺** | 工厂；与 materialCode 联合 resolve 域键 |
| `requestedDueDate` | IsoDate | ✅ | 有 | **缺** | 客户请求交期 |
| `customerCode` | string? | ❌ | 有 | **缺** | 可选 |
| `purpose` | `'CTP' \| 'INSERT_IMPACT_ANALYSIS'` | ✅ | 有 | **缺** | **CTP Purpose 永远不允许激活**（U11，仅展示） |
| `domainKey` | string? | ❌ | 有（A 路径已加） | 有（现 `[Required]`） | **v1.0 登记为可选**，见 §三 |

> 后端 `CtpEvaluateRequest` 现仅 `DomainKey` 一字段（CtpController.cs:57-61），其余 7 字段随引擎接入一并补齐。

---

## 三、domainKey 契约（核心登记项）

### 3.1 字段口径（v1.0 终定）

```csharp
// 现：[Required(ErrorMessage = "DomainKey 必填")] public required string DomainKey { get; init; }
// 改：public string? DomainKey { get; init; }   // 移除 [Required] + 去掉 required
```

### 3.2 resolve 规则（纯后端容错兜底，非取代前端选择）

1. 前端**仍传** `domainKey`（A 路径下拉为主路径）→ 后端直接用，不 resolve。
2. 缺省（`null`/空）时 → 按 `materialCode + factoryCode` 自动 resolve 域键。
3. **多域歧义**（一物料映射多域/多产品族）→ 按红线 #4 **不得盲目 `.First()`**，返回 **400** `ApiResponse.Fail("无法自动确定 DomainKey")`。
4. 解析失败（物料/工厂无域映射）→ 同样 **400**（400=业务错；**不 422**，422 仅限模型校验错）。
5. F-G4 校验对 resolve 后的 `domainKey` 照常执行，不因自动 resolve 而跳过。

### 3.3 resolve 实现

- 载体：`IDomainResolver`（3号位 新建，视既有仓储实现，**无需新增表**，按物料主数据 + 工厂映射）。
- 越位边界：3号位 仅持有 resolve 判定 + scope 校验；**试算编排归 2号位**（E-4 宿主链 = Web 薄层 → 2号位 orchestration）。

---

## 四、校验口径汇总

| 场景 | 返回 |
|---|---|
| 模型绑定失败（如缺 `orderCanonicalId`/`quantity`/`purpose`） | 422（模型校验错） |
| 无 `domainKey` 且 resolve 失败/歧义 | 400「无法自动确定 DomainKey」 |
| Scope 越界（resolve 后的域无权限） | 403（ScopeViolationException → ApiResponse.Fail） |
| 核心能力未接入 | 501（骨架期现状） |
| CTP Purpose 尝试激活 | 禁止（U11，仅展示） |

---

## 五、1号位/2号位 协作点

- 引擎接入时仅需消费 `request.DomainKey`（前端传 或 service resolve 的同一字段），**无需为本路径做改动**。
- `CtpResultDto` 契约由接入时 1号位/2号位 定义，本件不预设字段。

---

## 六、生效与验收

- 生效：随 C 路径落码（引擎接入时）一并生效；登记即生效（契约冻结，后续改动须走变更登记）。
- 4号位 验收：`verify-acceptance.mjs` C01-C02 只读静态断言（`CtpEvaluateRequest.DomainKey` 无 `[Required]`；`CtpService.EvaluateAsync` 含 `ResolveAsync` 调用）；协助核对 resolve 失败返 400 而非 422。
- 前端兼容：`CtpInput.domainKey` 保留（A 路径下拉主路径），C 落地后是否撤销另行决策。
- **C 路径生效前置（2026-09-30 补记第 4 条）**：① 引擎契约定案；② 本契约登记；③ resolve 实现口径；④ **dev 主数据 ProductFamily 映射**——前端硬编码物料（M-001..M-007）在 dev 有 `Material.ProductFamilyId → DomainDefinition（IsActive=1）` 映射（撤下拉后所有物料走 resolve 的前置）。④ 未满足前撤下拉将使无映射物料全 400（fail-closed，属预期）。

---

## 七、待办衔接

| # | 事项 | 归属 | 状态 |
|---|---|---|---|
| 1 | 本契约登记件 | 3号位 | ✅ 本件 |
| 2 | C 路径落码（optional + resolve fail-closed + 400 + DTO 补 7 字段） | 3号位 | ✅ 已落码（9 单测绿）；试算仍 501 待引擎接入 |
| 3 | 前端下拉撤销 | 4号位 | 待 C 落地后另行决策 |
| 4 | verify-acceptance C01-C02 | 4号位 | 待 C 落地后（4号位 侧运行） |

— 3号位
