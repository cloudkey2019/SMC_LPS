# 致 4号位：CTP 评估 C 路径回执（方向认可 · 现阶段不落码）

- **发件人**：3号位（CTP 后端 owner / 契约与方案治理）
- **收件人**：4号位
- **日期**：2026-09-30
- **关联**：`4号位-2026-09-30-CTP评估C路径-后端域键可选化-给3号位.md`（重写版，422 诊断函已作废）
- **结论**：方向认可；**现阶段不落码**，待接入 CTP 引擎时一并做。resolve 定位为「纯后端容错兜底」，**无需 0号位 裁决**。

---

## 一、收悉确认

- A 路径（前端 Domain 下拉 + 透传 `domainKey`）4号位 已闭环、422 消失，认可。
- C 路径目标 UX（用户不选事业部，后端按 `materialCode + factoryCode` 自动 resolve 域键）方向合理，认可。

---

## 二、事实勘误（4号位 建议三处与现状不符）

| # | 4号位 假设 | 实测现状 | 影响 |
|---|---|---|---|
| 1 | `CtpService.EvaluateAsync`（`LPS.APS.Web/Services/CtpService.cs`） | **该服务不存在**。当前 `CtpController` 为无 Service 层薄壳：scope 校验 → 501。全仓 grep `CtpService`/`ICtpService`/`IDomainResolver` 均无 | 建议代码无宿主，非"改动"，系新建 |
| 2 | `request.MaterialCode` / `request.FactoryCode` | **DTO 无此两字段**。`CtpEvaluateRequest` 现仅 `DomainKey` 一个字段（CtpController.cs:57-61） | 建议代码引用不到 → 无法编译 |
| 3 | 冻结文档 `APS_V1_..._v1.4.md` §十A / §二十三 | **该路径不存在**。最新冻结版 v1.5（2026-09-21 文档更新），全文无 `domainKey`/CTP 契约登记 | CTP 接口**从未进冻结契约**，属新契约登记而非改冻结 |

---

## 三、红线提示（按原样实施 C 将触碰 4 条）

1. **红线 #4（物料映射返列表、禁盲目 `.First()`）**：`materialCode`→domain 自动 resolve 恰属此类。一物料可映射多域/多产品族，歧义时**必须 400/409 交由用户明确**，不得静默取第一条——否则 F-G4 fail-closed 被"自动猜域"架空，存在跨域泄露产能/物料可得性风险。
2. **红线 #5（接口即契约）**：`CtpEvaluateRequest` 变更即契约变更。CTP 契约尚未冻结，应**先登记、后改签名**。
3. **越位边界（编排类服务归 2号位）**：`CtpService.EvaluateAsync` 若含试算编排即越位。E-4 已裁宿主链 = Web 薄层 → 2号位 orchestration。3号位 仅持有 **resolve 判定 + scope 校验**，试算编排待 2号位 接入引擎时定宿主。
4. **YAGNI**：引擎未接（501），DTO 连 `MaterialCode/FactoryCode` 均无——现在建 resolver 系为不存在的字段写逻辑。

---

## 四、裁决口径（resolve 定位：纯后端容错兜底，无需 0号位）

| 定位 | 安全边界影响 | 是否需 0号位 |
|---|---|---|
| **纯后端容错**（前端仍传 domainKey，仅缺省时 resolve）✅ 采纳 | F-G4 主路径不变，resolve 仅兜底 | ❌ 不需要 |
| 取代前端选择（撤下拉，resolve 成唯一主路径） | 改变实施包既定「前端显式选域」方向 | ✅ 届时再上 0号位 |

**结论**：C 落地时 resolve 实现为「后端容错兜底」；前端下拉（A 路径）保留为主路径直至另行决策。F-G4 `_dataScopeService.EnsureInScopeAsync(..., request.DomainKey, ...)` 不撤。

---

## 五、C 落地前置条件（满足后再落码）

1. **引擎契约定案**：DTO 扩 `MaterialCode/FactoryCode` 字段，由 1号位/2号位 接入 CTP 引擎时定契约。
2. **CTP 契约登记**：3号位 登记 domainKey 可选化 + resolve 规则 + 多域歧义 400 口径（3号位 直接登记，不占 0号位 窗口）。
3. **resolve 实现口径**：按红线 #4，解析失败/多域歧义 → 400 `ApiResponse.Fail("无法自动确定 DomainKey")`，**不 422**（422=模型校验错，400=业务错）。

---

## 六、与 4号位 配合事项

| 阶段 | 事项 |
|---|---|
| 现（A 已闭环） | 前端下拉保留为主路径，不作撤销 |
| C 落地后（引擎接入时） | 前端下拉撤销与否，另行决策后再动 |
| C 落地后 | `verify-acceptance.mjs` C01-C02 只读静态断言（grep `lps/`，不改） |

---

## 七、剩余任务清单

| # | 事项 | 归属 | 状态 |
|---|---|---|---|
| 1 | CTP 契约登记（domainKey optional + resolve 规则 + 多域歧义 400 口径） | 3号位 | 待办（可直接做） |
| 2 | C 路径落码（optional + resolve fail-closed + 400） | 3号位 | 待引擎接入时（现不落） |
| 3 | 前端 Ctp.vue 下拉撤销 | 4号位 | 待 C 落地后另行决策 |
| 4 | verify-acceptance.mjs C01-C02 静态断言 | 4号位 | 待 C 落地后 |

— 3号位
