# APS V1 CTP 主数据迁移——真实主数据落地回执（3号位 致 4号位）v1.0

- **发件人**：3号位（DomainDefinition 治理 / CTP 全栈 Owner）
- **收件人**：4号位（CTP 评估前端 Owner / 验收提请方）
- **抄送**：0号位（裁决备查）、1号位/2号位（核心试算接入备查）
- **日期**：2026-10-09
- **版本**：v1.0（§三 清单终稿 → dev 真实落地完成回执）
- **关联**：
  - `APS_V1_CTP_domain_seed_清单终稿_3号位致4号位_v1.0_20261009.md`（§三 8 键清单终稿）
  - `APS_V1_CTP迁移域键唯一性核对_升级报告_3号位致0号位_v1.3_20261009.md`（Q1 已裁 / Q3 已确认）
  - `4号位-2026-09-30-CTP评估主数据迁移-给3号位(1).md`（§五 验收 / §七 自测 SQL / §十 已闭环动作）
  - 迁移脚本 `LPS.APS.Web/Sql/APS_CTP_domain_seed_202610xx.sql`（v3）

---

## 一、结论

§三 清单终稿（8 条，一产品族一 Domain）经 **0号位 裁决确认**（Q3：业务边界未擅自改变，脚本方向可继续）后，已于 2026-10-09 在 dev 库 **APS_Production** 落地完成，并通过 9 单测 + 32 真实路径冒烟。

## 二、0号位 裁决要点（本回执执行依据）

- 0号位 **不提供真实主数据**——真实 ProductFamily/Factory/Material 以 dev 正式数据为准，由 3号位 查询核实执行；
- 认可：保留既有 3 键不动、新增 8 键、全 `FAMILY`、`FactoryId=NULL`、不 UPDATE Material、弃用演示假数据、dev-only、执行后唯一性+歧义检查；
- 8 个 DomainKey 命名（`FAMILY_VALVE` 等）属 3号位 治理职责，无需逐条批准；
- 真实冒烟物料由 3号位 自选（ProductFamilyId 有值 + 产品族有有效域 + 真实有效）；
- ⚠️ `SortOrder=100` 仅稳定排序辅助，非 Domain/求解/依赖优先级。

## 三、落地结果

| 验收项（4号位 函 §五） | 结果 |
|---|---|
| DomainDefinition 行数 | ✅ **11 行**（既有 3：FAMILY_X/BJ_FAMILY_Y/TJ_FAMILY_Y + 新增 8：FAMILY_VALVE/LGJ/XC/JS/WKQ/TZP/MJ/QT） |
| 域键唯一性自检 | ✅ 空集（UQ_DomainDefinition_DomainKey 无冲突） |
| 命中歧义自检 | ✅ 空集（8 新域族真实物料 × CN/BJ/SH/TJ 每组合 HitCount=1） |
| 不 UPDATE Material | ✅ 未写 Material（真实映射存量 ~27 万条保持不变） |
| CtpEvaluateTests 9 单测 | ✅ **9/9 绿**（114ms） |
| 端到端真实冒烟 | ✅ **32/32 全 501**（8 真实物料 × CN/BJ/SH/TJ），**零 400/422** |

> 迁移脚本 v3 为幂等（IF NOT EXISTS）+ fail-closed（产品族缺失防 515），dev-only，可安全重跑。

## 四、真实冒烟物料清单（供 4号位 D01-D02 动态断言取用）

| 产品族 | 真实物料 MaterialCode |
|---|---|
| VALVE | `FINAL_ASSY-51-SYE100-3-2A-ZZ` |
| LGJ | `FINAL_ASSY-IDF-K2A0716M` |
| XC | `FINAL_RAW-A6061-355-1200-GC` |
| JS | `FINAL_ASSY-145111` |
| WKQ | `FINAL_ASSY-11E-XQB` |
| TZP | `FINAL_ASSY-10-VK334V-DND7362` |
| MJ | `FINAL_WIP-1_2A-37273-40` |
| QT | `FINAL_ASSY-GJ22408-1` |

> 均为 dev 真实有效物料（ProductFamilyId 有值 + 产品族有 IsActive=1 DomainDefinition）。工厂取 `CN/BJ/SH/TJ` 任一真实工厂。

## 五、4号位 需做

1. **verify-acceptance 动态断言 D01-D02**：基于 §四 真实物料 + 真实工厂 POST `/api/ctp/evaluate`，断言 **501**（核心试算未接入契约行为）而非 400/422；A01-A05/C01-C02 静态断言不受影响。
2. **前端撤下拉后的 C 路径回归**：真实物料评估可走通至 501，无 400（域键 resolve 已命中）。

## 六、不在本回执范围

- **核心试算接入（替换 501）**：归 1号位/2号位（跨号位协助项），本回执只解决 400/422，不解决 501；
- **FRL 族**：既有 BJ/TJ 两厂 FACTORY_FAMILY 历史边界保持不动；其 CN/SH 命中 0 属既有行为，如确有真实 Order/Demand 需进 APS，由 3号位/2号位 举真实运行事实后交 0号位 判断是否新增域；
- **演示数据**（M-001..M-007 / F-SUZ-01 / F-CDG-02）：确认系前端硬编码演示假数据，已彻底弃用、不入任何脚本。

## 七、时间线

- §三 清单终稿 + 迁移脚本 v3：**2026-10-09**（已完成）
- dev 落库 + 9 单测 + 32 冒烟：**2026-10-09**（已完成）
- 4号位 D01-D02 + 前端回归：待 4号位 排期（seed 已就绪，可立即动）
- 核心试算接入：1号位/2号位 引擎就绪后

— 3号位
