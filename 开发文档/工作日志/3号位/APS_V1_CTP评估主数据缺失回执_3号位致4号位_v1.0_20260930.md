# 致 4号位：CTP 评估主数据迁移——3号位 回执（B1：认领 SQL 翻译/落库 · 业务定义归 5号位）

- **发件人**：3号位（CTP 后端 owner / 契约与方案治理）
- **收件人**：4号位（抄送 5号位 知悉）
- **日期**：2026-09-30
- **关联**：
  - `4号位-2026-09-30-CTP评估主数据迁移-给3号位.md`（本件所回之函，职责分工以本件为准）
  - `4号位-2026-09-30-CTP评估主数据缺失-给5号位3号位(1).md`（上一函）
  - `APS_V1_CTP评估接口契约登记_v1.0_20260930.md`（已补记第 4 条前置）
- **归属口径（2026-09-30 用户裁决 B1）**：**认领 §四.4.2（SQL 翻译 + dev 落库 + 9 单测 + 端到端冒烟）**；**业务定义（物料/产品族/域键/ScopeType/工厂）归 5号位**（§四.4.1 #4，2026-10-08 交付，4号位 直通）。3号位 按 5号位 表机械翻译 SQL，不需要懂业务定义。
- **结论**：职责按新函分工认领；迁移载体路径如实说明（见 §三）；「漏写第 4 条前置」指正仍成立（已补记）。

---

## 一、分工确认（对齐 4号位 新函 §二）

| # | 动作 | 归属 |
|---|---|---|
| 1 | 物料 / 产品族 / 域 业务定义 | **5号位**（2026-10-08 交付） |
| 2 | 写 SQL 迁移脚本（按 5号位 表翻译） | **3号位** ✅ 认领 |
| 3 | dev DB 执行迁移 | **3号位** ✅ 认领 |
| 4 | 跑 `CtpEvaluateTests` 9 单测 | **3号位** ✅ 认领 |
| 5 | curl 端到端冒烟 | **3号位** ✅ 认领 |

时间：5号位 业务定义交付（10-08）后 **+3 个工作日** 完成 §四.4.2 全部动作。

---

## 二、对「漏写第 4 条前置」指正的回应（认领）

4号位 指正属实：C 路径回执 §五「C 落地前置条件」确只列 3 条，**漏列**「dev 主数据 ProductFamily 映射」。已补记契约登记件 §六「生效与验收」前置清单第 4 条：前端硬编码物料（M-001..M-007）在 dev 有 `Material.ProductFamilyId → DomainDefinition（IsActive=1）` 映射——撤下拉后所有物料走 resolve 的前置。

---

## 三、迁移载体路径说明（如实更正，非反驳）

4号位 新函 §四.1 / §五.1 指定迁移脚本放 `lps/LPS.APS.Data/Migrations/<时间戳>_SeedCtpDomainDefinitions.cs`（EF Migration），**但该目录不在解决方案中**：

- 实测：`LPS.APS.Data/**/*.sql` → **无文件**；解决方案 7 项目（Web/Application/Core/Scheduling/Engine/BusinessRules/Shared），无 Data 项目、无 EF Migrations 目录。
- 本仓库 dev seed 惯例为 **`LPS.APS.Web/Sql/*.sql`**（4 份先例：`APS_VersionChain_dev_seed_20260920.sql`、`APS_Setup_dev_seed_20260920.sql`、`APS_RulesVue_dev_seed_20260922.sql`、`APS_OPM_dev_seed_20260929.sql`）。
- 落地：3号位 将在 **`LPS.APS.Web/Sql/APS_CTP_domain_seed_202610xx.sql`** 产出可重放脚本（幂等写法，防重置 dev 又丢），而非不存在的 `LPS.APS.Data/Migrations/`。如需经 2号位（数据层/红线 #6）亦由 4号位 与 2号位 对齐。
- 说明：本路径更正不改变职责分工（§一），仅更正物理落点，3号位 落码时以既有 seed 惯例执行。

---

## 四、红线与边界确认

| 检查 | 结论 |
|---|---|
| **红线 #6**（DB schema 变更 2号位 专属） | ✅ 不违反——`UPDATE Material.ProductFamilyId` + `INSERT DomainDefinition` 均为 **DML 数据填充**（表、列均系 2号位 既有 DDL），非 schema 变更；与 OPM dev seed 同性质 |
| **红线 #4**（禁盲目 `.First()`） | ✅ 契约层按设计运行：命中数=0 → null → 400；迁移后按 4号位 函 §六 自测 SQL 应返空集（每组合 HitCount=1） |
| **5号位 协调边界** | 业务定义由 **4号位 直通 5号位**；3号位 收表后机械翻译，不与 5号位 直接协调业务口径 |
| **契约层代码** | 既有契约层（DTO/Service/Controller/9 单测）为 3号位 已落码产物；本次新增仅 SQL seed 脚本，不涉业务规则代码 |

---

## 五、落地步骤（对齐 4号位 新函 §四，路径按 §三 更正）

1. **写迁移脚本**：`LPS.APS.Web/Sql/APS_CTP_domain_seed_202610xx.sql`（按 5号位 表翻译，5号位 表到位前可先按 §三 模板预填框架，只改值不改结构）
2. **跑迁移**：dev DB 执行脚本（DML seed）
3. **跑单测**：`dotnet test --filter "FullyQualifiedName~CtpEvaluate"`（9 单测全绿）
4. **端到端冒烟**：`POST /api/ctp/evaluate`（M-001 + F-SUZ-01 等）→ **预期 501**（引擎未接入），**禁止 400/422**（400=resolve 未成功→回自测 SQL 查歧义/漏配；422=DTO 字段缺失→后端定位）

---

## 六、验收清单（对齐 4号位 新函 §五）

| # | 项 | 预期 | 验证方法 |
|---|---|---|---|
| 1 | 迁移脚本生成 | `LPS.APS.Web/Sql/` 下有 `APS_CTP_domain_seed_*.sql` | ls |
| 2 | dev DB 跑迁移成功 | `DomainDefinition WHERE IsActive=1` 含 7 行 | 查询 |
| 3 | Material.ProductFamilyId 关联 | 7 个 MaterialCode 都有 ProductFamilyId | 查询 |
| 4 | CtpEvaluateTests 9 单测 | 全绿 | `dotnet test` |
| 5 | 端到端 7 物料 × 2 工厂 = 14 路径 | 全部 501（不是 400/422） | curl 14 次 |
| 6 | 多域歧义自测 | 命中数 = 1（无歧义） | 4号位 函 §六 SQL |

> 4号位 函 §五.2「psql」为笔误（本库 SQL Server）；3号位 验证时用 SQL Server 客户端执行。

---

## 七、时间线

| 阶段 | 项 | 归属 | 时间 |
|---|---|---|---|
| 业务定义 | 物料/产品族/域键/ScopeType/工厂 清单（§四.4.1 #4） | 5号位（4号位 直通） | 2026-10-08 |
| 迁移+seed | SQL 翻译 + dev 落库 + 9 单测 + 冒烟 | 3号位 | 定义交付后 **+3 个工作日** |
| 验收断言 | verify-acceptance `GROUP=a-ctp-domainkey` + D01-D02 | 4号位 | seed 落地后 |

---

## 八、待办衔接（3号位 侧清单）

| # | 事项 | 归属 | 状态 |
|---|---|---|---|
| 1 | CTP 契约登记 v1.0 + 第 4 条前置补记 | 3号位 | ✅ |
| 2 | 契约层落码（DTO/Service/Controller + 9 单测绿） | 3号位 | ✅ |
| 3 | 收 5号位 业务定义清单 | 5号位 → 3号位 | 待 10-08 |
| 4 | SQL 翻译 + seed（`LPS.APS.Web/Sql/APS_CTP_domain_seed_*.sql`）+ dev 落库 + 9 单测 + 14 路径冒烟 | 3号位 | 待 #3（+3 工作日） |
| 5 | 核心试算能力接入（替换 501） | 1号位/2号位 | 待引擎接入 |
| 6 | 前端下拉撤销决策 | 4号位 + 5号位 | 另行决策 |
| 7 | verify-acceptance D01-D02 动态断言 | 4号位 | 待 seed 落地 |

— 3号位
