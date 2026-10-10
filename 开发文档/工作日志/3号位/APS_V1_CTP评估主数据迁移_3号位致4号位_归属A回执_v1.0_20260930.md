# 致 4号位：CTP 评估 dev 主数据迁移——3号位 回执（§六 归属 A · 全栈 Owner）

- **发件人**：3号位（DomainDefinition 治理 Owner / 契约与方案治理）
- **收件人**：4号位（抄送 5号位 知悉、0号位 备查）
- **日期**：2026-09-30
- **关联**：
  - `4号位-2026-09-30-CTP评估主数据迁移-给3号位(1).md`（本件所回之函，§六 归属确认）
  - `APS_V1_CTP评估主数据缺失回执_3号位致4号位_v1.0_20260930.md`（上一回执，B1 分工）
  - `APS_V1_CTP评估接口契约登记_v1.0_20260930.md`（已补记第 4 条前置）
- **结论**：**§六 归属确认 = 选项 A**——Material→ProductFamily（ProductFamilyId）归 3号位，3号位 一并出 §三 业务定义清单 + 迁移 + 测试 + 冒烟。DomainDefinition 治理归 3号位 成立（v1.44 L34 已核实）。

---

## 一、§六 归属确认（选项 A）

| 项 | 裁决 |
|---|---|
| Material.ProductFamilyId 归属 | ✅ **归 3号位**（选项 A） |
| 依据 | v1.44 L34/L1373 已核实「3号位 负责 DomainDefinition、当前有效 Domain 集合、ExpectedDomainKeysJson 冻结及依赖治理」；ProductFamilyId 为归域链路入口，随 Domain 治理归 3号位 一并持有 |
| 后续 | 3号位 出 §三 表（含 Material→ProductFamily + ProductFamily→DomainKey）+ SQL 迁移 + dev 落库 + 9 单测 + 14 路径冒烟，4号位 闭环 |

---

## 二、与上一回执（B1 分工）的变更说明

| 项 | B1 口径（上一函） | 本回执（A 版，以本件为准） |
|---|---|---|
| 业务定义归属 | 5号位 出表，3号位 翻译 | **3号位 自出**（5号位 已驳回；v1.44 明文 DomainDefinition 归 3号位） |
| Material.ProductFamilyId | 待定 | **归 3号位**（选项 A） |
| 时间线 | 5号位 10-08 交付 → 3号位 +3 工作日 | 3号位 自主：§三 表 **10-08**，迁移+冒烟 **10-15** |

> 原「5号位 出表」假设作废，以本回执 + 4号位 函 `(1)` 为准。

---

## 三、§三 业务定义清单（3号位 自出 · 示例 + 待核对项）

按 4号位 函 §三 示例 7 物料 + 真实 DDL 约束出表（占位，落库前核对现有域键，见 §四）：

| MaterialCode | 产品族 | DomainKey | ScopeType | FactoryCode | SortOrder |
|---|---|---|---|---|---|
| M-001 电机壳体-A | 电机壳体 | FAMILY_INJECTION | FAMILY | — | 1 |
| M-002 机柜上盖-B | 机柜 | BJ_FAMILY_INJECTION | FAMILY | — | 1 |
| M-003 控制箱-C | 控制箱 | ⚠️ 见 §四 | FACTORY_FAMILY | F-CDG-02 | 1 |
| M-004 标准泵-D | 标准泵 | FAMILY_INJECTION | FAMILY | — | 1 |
| M-005 包装套件-E | 包装套件 | ⚠️ 见 §四 | FACTORY_FAMILY | F-SUZ-01 | 1 |
| M-006 机架焊接件-F | 机架焊接件 | FAMILY_INJECTION | FAMILY | — | 1 |
| M-007 电路板组件-G | 电路板组件 | ⚠️ 见 §四 | FACTORY_FAMILY | F-CDG-02 | 1 |

> 产品族（ProductFamily）需 3号位 补 seed（本仓无 Material/ProductFamily 主数据 seed，需确认 dev 是否已有；无则随迁移一并 INSERT，红线 #6 合规——DML 非 DDL）。

---

## 四、⚠️ 域键唯一性核对（落库前必做，属 3号位 治理职责）

真实 DDL 约束 `UQ_DomainDefinition_DomainKey UNIQUE(DomainKey)`——**同一 DomainKey 全表只能一条**。4号位 函 §三 示例中：
- `FAMILY_INJECTION` 出现 4 次（M-001/004/005/006）
- `BJ_FAMILY_INJECTION` 出现 3 次（M-002/003/007）

**直接照抄 §三 示例会违反唯一约束**（4号位 函 §九「可以照抄」与 DDL 冲突）。3号位 处理方案：

| # | 处理 | 归属 |
|---|---|---|
| 1 | 核对 dev 现有 `DomainDefinition` 域键集合（是否已有 FAMILY_INJECTION/BJ_FAMILY_INJECTION，绑定哪个 ProductFamilyId） | 3号位（治理数据） |
| 2 | 同产品族可共享域键；**不同产品族不可共用同一 DomainKey** → 需独立域键（如 FAMILY_INJECTION / BJ_FAMILY_INJECTION / 新键） | 3号位 |
| 3 | **如需新域键**（超出 FAMILY_INJECTION/BJ_FAMILY_INJECTION 既有集合）→ 按 4号位 函 §三 注「走 0号位 裁决」 | 0号位 裁决 |
| 4 | 多域歧义禁止：同 (MaterialCode, FactoryCode) 组合不得两条 DomainDefinition 同时命中 | 3号位（红线 #4 校验） |

> 本节为治理正确性提示，非对 4号位 函 §三 的业务否定——3号位 落库前按现有域键集合定最终 DomainKey 分配。

---

## 五、路径更正（沿用上一回执，非反驳）

4号位 函 §四.1/§五.1 指定 `lps/LPS.APS.Data/Migrations/<时间戳>_SeedCtpDomainDefinitions.cs`（EF Migration），**但该目录不在解决方案中**（7 项目无 Data 项目、无 EF Migrations）。3号位 按本仓 seed 惯例在 **`LPS.APS.Web/Sql/APS_CTP_domain_seed_202610xx.sql`** 产出（v2 · fail-closed 框架已就绪，上一回执交付），改为按 A 版：自出产品族 seed + Material.ProductFamilyId 回填 + DomainDefinition seed。

---

## 六、时间线（对齐 4号位 函 §八）

| # | 项 | 归属 | 日期 |
|---|---|---|---|
| 1 | §六 归属回执（A） | 3号位 | ✅ 本件 |
| 2 | §三 DomainDefinition + Material 业务定义清单终稿（含域键核对） | 3号位 | **2026-10-08** |
| 3 | 产品族 seed（如 dev 缺失）+ 迁移脚本定稿 | 3号位 | 随 #2 |
| 4 | dev 落库 + 9 单测 + 14 路径冒烟 | 3号位 | **2026-10-15** |
| 5 | verify-acceptance D01-D02 动态断言 | 4号位 | seed 落地后 |

---

## 七、待办衔接（3号位 侧清单）

| # | 事项 | 归属 | 状态 |
|---|---|---|---|
| 1 | CTP 契约登记 v1.0 + 第 4 条前置补记 | 3号位 | ✅ |
| 2 | 契约层落码（9 单测绿） | 3号位 | ✅ |
| 3 | §六 归属 A 回执 | 3号位 | ✅ 本件 |
| 4 | 核对 dev 现有域键集合 + §三 清单终稿（10-08） | 3号位 | 待办 |
| 5 | 产品族 seed + 迁移脚本定稿 + dev 落库 + 9 单测 + 14 路径冒烟（10-15） | 3号位 | 待 #4 |
| 6 | 核心试算接入（替换 501） | 1号位/2号位 | 待引擎 |
| 7 | verify-acceptance D01-D02 | 4号位 | 待 seed 落地 |

— 3号位
