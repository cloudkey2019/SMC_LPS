# APS V1 CTP 主数据迁移——§三 业务定义清单终稿（3号位 致 4号位）v1.0

- **发件人**：3号位（DomainDefinition 治理 / CTP 全栈 Owner）
- **收件人**：4号位（CTP 评估前端 Owner / 验收提请方）
- **抄送**：0号位（裁决备查）、1号位/2号位（核心试算接入备查）
- **日期**：2026-10-09
- **版本**：v1.0（**按 0号位 2026-10-09 裁决 + dev 真实主数据实查定稿**，取代 4号位 函 §三 示例假数据表）
- **关联**：
  - `APS_V1_CTP迁移域键唯一性核对_升级报告_3号位致0号位_v1.2_20261009.md`（§四 0号位 裁决：一产品族一 Domain）
  - `4号位-2026-09-30-CTP评估主数据迁移-给3号位(1).md`（§三 示例 / §五 验收 / §七 自测 SQL）
  - 迁移脚本 `LPS.APS.Web/Sql/APS_CTP_domain_seed_202610xx.sql`（v3，本清单数据源）

---

## 一、结论（迁移形态收窄）

dev 实查确认：**`Material.ProductFamilyId` 真实映射已存量 ~27 万条**（CYL 24.1万 / FRL 1.1万 / TZP 4.5千 / MJ 4.3千 / VALVE 4千 / XC 2千 / QT 1.9千 / JS 1.3千 / WKQ 50 / LGJ 48）——**迁移无需再 UPDATE Material**。CTP 主数据缺口收敛为单一动作：**为 8 个「无域」真实产品族各 INSERT 1 条 DomainDefinition（ScopeType=FAMILY）**。

## 二、真实主数据基线（2026-10-09 dev 只读实查）

| 对象 | dev 实况 |
|---|---|
| 真实产品族（IsActive=1） | `CYL(1) / FRL(2) / VALVE(3) / LGJ(4) / XC(5) / JS(6) / WKQ(7) / TZP(8) / MJ(9) / QT(10)`（+ 测试 seed TF_*/PF_Sx_*，不入） |
| 真实工厂（IsActive=1） | `CN(2) / BJ(3) / CN6注(4) / SH(5) / TJ(6)`（+ 测试 seed TF_*，不入） |
| DomainDefinition 现有 3 键（**保持不动**） | `FAMILY_X`（FAMILY, CYL）、`BJ_FAMILY_Y`（FACTORY_FAMILY, FRL+BJ）、`TJ_FAMILY_Y`（FACTORY_FAMILY, FRL+TJ） |
| Material 映射存量 | ~27 万条（已归族，无需回填） |

## 三、§三 业务定义清单终稿（8 条，一产品族一 Domain）

| # | 产品族 Code | DomainKey（新） | DomainName | ScopeType | FactoryId | SortOrder |
|---|---|---|---|---|---|---|
| 1 | VALVE | `FAMILY_VALVE` | 取自 ProductFamily.Name 子查询 | FAMILY | NULL | 100 |
| 2 | LGJ | `FAMILY_LGJ` | 同上 | FAMILY | NULL | 100 |
| 3 | XC | `FAMILY_XC` | 同上 | FAMILY | NULL | 100 |
| 4 | JS | `FAMILY_JS` | 同上 | FAMILY | NULL | 100 |
| 5 | WKQ | `FAMILY_WKQ` | 同上 | FAMILY | NULL | 100 |
| 6 | TZP | `FAMILY_TZP` | 同上 | FAMILY | NULL | 100 |
| 7 | MJ | `FAMILY_MJ` | 同上 | FAMILY | NULL | 100 |
| 8 | QT | `FAMILY_QT` | 同上 | FAMILY | NULL | 100 |

**命名口径**（符合 0号位 裁决「DomainKey 非 ProductFamily.Code 别名」）：前缀 `FAMILY_` 为域类型标记（对齐既有 `FAMILY_X` 前缀风格），产品族段取可读英文——域键是 **APS 内部稳定业务计算域标识**，非 Code 直用。域键为治理数据而非接口契约，如需 0号位/业务 调整命名，仅改 seed 数据值即可（零成本）。

**ScopeType 说明**：全部 `FAMILY`（FactoryId NULL，全工厂共享域）——按裁决「Factory 维度不自动拆域」；8 族均未举证独立业务计算/计划发布边界 → 不拆。`FRL` 既有 `BJ/TJ` 两厂 FACTORY_FAMILY 系历史已存业务边界，**保持不动**（不新增/删改/合并）；`CYL` 已有 `FAMILY_X` 全厂域，不新增。

## 四、与 4号位 函 §三 示例的差异

| 项 | 4号位 函 §三 示例（已废弃） | 本清单终稿 v1.0 |
|---|---|---|
| 物料 | M-001..M-007（前端硬编码演示假数据，dev 不存在） | 不写 Material（真实映射已存 ~27 万条） |
| 产品族 | 电机壳体/机柜/控制箱/…（dev 无此产品族） | 真实 8 族（VALVE/LGJ/XC/JS/WKQ/TZP/MJ/QT） |
| 域键 | FAMILY_INJECTION / BJ_FAMILY_INJECTION（跨族复用，违反 UQ） | 8 个独立键 FAMILY_*（一产品族一 Domain，UQ 合规） |
| ScopeType | FAMILY / FACTORY_FAMILY 混用 | 全 FAMILY（默认不拆域） |
| 迁移动作 | UPDATE Material + INSERT DomainDefinition | 仅 INSERT DomainDefinition |

## 五、0号位 裁决落实对照

| 裁决原则 | 落实 |
|---|---|
| 一产品族一 Domain（方案 a） | 8 族各 1 独立 DomainKey；既有 CYL/FRL 键不动 |
| 不同 ProductFamily 不得共用同一 DomainKey | 8 键全局唯一，无跨族复用 |
| DomainKey 非 Code 别名、3号位 治理 | FAMILY_<族> 命名，治理口径见 §三 |
| Factory 不自动拆域，仅边界独立才拆 | 全 FAMILY；FRL 既有 FACTORY_FAMILY 系历史边界保持不动 |

## 六、真实冒烟路径（迁移后）

- **物料来源**：从真实映射存量中取 8 新域族各 1 条真实 `MaterialCode`（`SELECT MIN(Id) ... GROUP BY ProductFamilyId`，脚本 §3 已内置歧义自检）。
- **工厂**：`CN / BJ / SH / TJ` 4 真实工厂。
- **预期**：`POST /api/ctp/evaluate` 返回 **501**（核心试算未接入，契约行为）而非 400/422。
- **注意**：`FRL` 族不纳入冒烟（其 CN/SH 命中 0 系既有 FACTORY_FAMILY 边界行为，非本次引入）。

## 七、验收与时间线

| # | 项 | 归属 | 状态/日期 |
|---|---|---|---|
| 1 | §三 清单终稿（本件） | 3号位 | ✅ 10-09 |
| 2 | 迁移脚本 v3（仅 INSERT 8 键，fail-closed + 幂等） | 3号位 | ✅ 10-09 |
| 3 | Q2 时间线顺延裁决（+3 工作日） | 0号位 | ⏳ 待裁 |
| 4 | dev 落库（sqlcmd -f 65001 执行脚本） | 3号位 | 待 #3 后 |
| 5 | CtpEvaluateTests 9 单测 + 真实路径冒烟 | 3号位 | 待 #4 |
| 6 | 核心试算接入（替换 501） | 1号位/2号位 | 待引擎 |
| 7 | verify-acceptance D01-D02 动态断言 | 4号位 | 待 seed 落地 |

**4号位 需知**：seed 落地后（#4），D01-D02 动态断言可基于**真实物料**（6 条路径：8 新域族各 1 × CN/BJ/SH/TJ 组合抽样）补加；A01-A05/C01-C02 静态断言不受影响。

— 3号位
