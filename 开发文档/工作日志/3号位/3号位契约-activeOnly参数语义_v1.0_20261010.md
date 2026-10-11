# 3号位 契约：`activeOnly` 参数语义登记（v1.0）

> 发件：3号位（规则/参数/Auth/RBAC/Scope 治理 Owner）
> 收件：4号位（抄送其他号位、审计脚本维护方）
> 日期：2026-10-10 ｜ 版本：v1.0
> 依据：`4号位-2026-10-10-activeOnly参数语义确认-给3号位.md`（P1 风险单）——3号位 拍板 **方案 A**（文档化 + remarks，零代码行为改动）
> 结论：**非后端 bug**——`?activeOnly=false` 返 0 = dev DB 客观状态（无 `IsActive=0` 数据），参数解析/Controller 透传/Repository SQL 拼接均正确。

---

## 一、三态语义（契约真值）

`GET /api/governance/rule-sets`、`/parameter-sets`、`/strategy-profiles` 三端点共享同一 `activeOnly` 三态语义：

| `activeOnly` 值 | 后端行为 | SQL 效果 | dev DB 预期 |
|---|---|---|---|
| `?activeOnly=true` | 仅查启用 | `AND [IsActive] = 1` | 648 条（当前全量） |
| `?activeOnly=false` | 仅查停用/归档 | `AND [IsActive] = 0` | **0 条（当前无停用数据 = 客观状态）** |
| 缺省（不传） | 不过滤，全量 | 不加 IsActive 条件 | 648 条 |

**关键点**：`false` ≠ 「查全部含停用」。要查「全部含停用」请**省略该参数**（等同 `IS NULL`）。`false` 是「只查停用」。

## 二、开发环境数据分布（非契约，供踩坑预判）

- dev DB `APS_Production.dbo.RuleSet` 全表 648 条，`IsActive` 全部 = 1（seed 仅造 active 数据，与早期 R1 催办单一致）。
- 因此 `?activeOnly=false` 在 dev 恒返 0——**不是参数 bug，是真实状态**。
- 与 4号位 10-10 观察到的「1682 占位数据无版本」同源：dev 数据分布问题，非后端行为缺陷。

## 三、代码备注（已落地）

`GovernanceController.cs` 三端点 XML remarks 已补三态语义说明（`GetRuleSets`/`GetParameterSets`/`GetStrategyProfiles`），注明「缺省不过滤；false=仅停用」防误读。环境数据分布不入代码注释（易腐化），仅存本契约文档。

## 四、契约变更影响

| 项 | 影响 |
|---|---|
| 接口签名 | 零变更（`bool? activeOnly = null` 保持） |
| HTTP 行为 | 零变更（方案 A 不触代码行为） |
| 前端 | 零变更（4号位 确认仅 `activeOnly=true`） |
| 数据库 | 零变更 |

## 五、回执

- **3号位 拍板**：方案 A 采纳；B（响应加 total/activeCount）与 C（保持现状）不采纳——B 违反 YAGNI（4号位 当前不阻塞），C 丢弃文档价值。
- **4号位 侧**：无需行动，此函件可归档关闭。
- 其他号位/审计脚本：查停用数据请先确认存在 `IsActive=0` 数据；查全量请省略 activeOnly。

---

**发件**：3号位 ｜ 日期：2026-10-10 ｜ 版本：v1.0
