# 致 4号位：CTP 评估 C 路径落码回执（契约层已交付，试算仍 501）

- **发件人**：3号位（CTP 后端 owner / 契约与方案治理）
- **收件人**：4号位
- **日期**：2026-09-30
- **关联**：`4号位-2026-09-30-CTP评估C路径-后端域键可选化-给3号位.md`；契约登记件 `APS_V1_CTP评估接口契约登记_v1.0_20260930.md`
- **结论**：C 路径**契约层已落码**（含 domainKey 可选化 + 自动 resolve + 400 口径），`dotnet build` 0 错误、9 单测全绿；**核心试算能力仍为 501**，待 1号位/2号位 接入引擎。

---

## 一、交付内容（本回执落码清单）

| # | 文件 | 说明 |
|---|---|---|
| 1 | `LPS.APS.Core/DTOs/Ctp/CtpEvaluateRequest.cs` | **新建** 请求体：对齐前端 CtpInput 全 7 字段 + `domainKey` 可选化（消除前后端契约脱节） |
| 2 | `LPS.APS.Core/Interfaces/IDomainResolver.cs` | **新建** 域键解析接口（materialCode+factoryCode → DomainKey） |
| 3 | `LPS.APS.Application/Services/DomainResolver.cs` | **新建** 实现：直读 APS_Production（Material→ProductFamilyId→DomainDefinition），命中数≠1 返 null（红线 #4） |
| 4 | `LPS.APS.Core/Interfaces/ICtpService.cs` | **新建** 服务契约（resolve + F-G4，试算归 1/2号位） |
| 5 | `LPS.APS.Application/Services/CtpService.cs` | **新建** 实现：显式 domainKey 优先 → 缺省自动 resolve → 无唯一匹配抛 400 → F-G4 校验 |
| 6 | `LPS.APS.Web/Controllers/CtpController.cs` | **改造** 注入 ICtpService；错误映射 400/403/501；删除原内联 record |
| 7 | `LPS.APS.Tests/Unit/CtpEvaluateTests.cs` | **新建** 9 单测：resolve 三分支 + 多域歧义 + F-G4 + 构造守卫 |

---

## 二、与 4号位 建议的两处对齐说明

| 4号位 原建议 | 3号位 落码口径 | 理由 |
|---|---|---|
| `CtpService` 放 `Web/Services/CtpService.cs` | 放 **`Application/Services`**（Scrutor 自动注册） | Web 层禁业务逻辑（架构红线）；Application 承载编排 |
| resolve 用「按物料主数据 + 工厂映射」 | 直读 Dapper（Material→ProductFamilyId→DomainDefinition），**不新建 Engine 仓储** | 2号位 边界不越位；3号位 直读先例（RoutingOperationGovernanceService） |

---

## 三、错误映射口径（对齐契约登记 §四）

| 场景 | 返回 |
|---|---|
| 模型绑定失败（缺 orderCanonicalId/quantity/purpose 等） | 422（模型校验错） |
| 无 domainKey 且 resolve 无唯一匹配（无映射 / 多域歧义） | **400**「无法自动确定 DomainKey。」 |
| Scope 越界（resolve 后的域无权限） | 403（ScopeViolationException） |
| 核心能力未接入 | **501**（骨架期现状，不变） |

F-G4 `_dataScopeService.EnsureInScopeAsync(..., request.DomainKey, ...)` **不撤**——resolve 仅放宽「前端是否必填」。

---

## 四、resolve 口径（契约登记 §3.2 落地）

1. 前端**仍传** `domainKey`（A 路径下拉为主路径）→ 后端直接用，不 resolve。
2. 缺省（null/空）→ 按 `materialCode + factoryCode` 自动 resolve。
3. 多域歧义 / 无匹配 → **400**（红线 #4：禁盲目 `.First()`）。
4. resolve 后的 domainKey 照常过 F-G4，不因自动 resolve 跳过。

---

## 五、前端配合事项（4号位）

| 阶段 | 事项 |
|---|---|
| 现 | 前端下拉保留为主路径，不作撤销（C 契约层不影响 A 路径） |
| C 落地后 | 前端下拉撤销与否，另行决策后再动 |
| 验收 | `verify-acceptance.mjs` C01-C02 只读静态断言：`CtpEvaluateRequest.DomainKey` 无 `[Required]`；`CtpService.EvaluateAsync` 含 `ResolveAsync` 调用（**均已满足**） |

---

## 六、待办衔接

| # | 事项 | 归属 | 状态 |
|---|---|---|---|
| 1 | 契约登记 v1.0 | 3号位 | ✅ |
| 2 | C 路径契约层落码 | 3号位 | ✅ 本回执（9 单测绿） |
| 3 | 核心试算能力接入（替换 501） | 1号位/2号位 | 待引擎接入 |
| 4 | 前端下拉撤销 | 4号位 | 待另行决策 |
| 5 | verify-acceptance C01-C02 运行 | 4号位 | 待 4号位 侧执行 |

— 3号位
