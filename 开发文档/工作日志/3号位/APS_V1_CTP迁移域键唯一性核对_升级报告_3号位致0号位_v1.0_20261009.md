# APS V1 CTP 迁移域键唯一性核对——升级报告（3号位 致 0号位）v1.2

- **发件人**：3号位（DomainDefinition 治理 Owner / CTP 全栈 Owner）
- **收件人**：0号位（PM / 最终裁决）
- **抄送**：4号位（知悉，主数据提请方）
- **日期**：2026-10-09（v1.3：**Q1 已裁（方案 a）；Q2 时间线由 3号位 自决不再提请；新增「清单终稿 + 迁移脚本」提报 0号位 确认**）
- **性质**：**升级报告 + 裁决落实 + 清单提报**——CTP 迁移前置：域键分配违反真实 DDL 唯一约束，0号位 已裁 Q1；清单终稿 + 脚本待 0号位 确认后执行
- **关联**：
  - `APS_V1_CTP评估接口契约登记_v1.0_20260930.md`（§六.4 C 路径前置：dev 主数据 ProductFamily 映射）
  - `4号位-2026-09-30-CTP评估主数据迁移-给3号位(1).md`（§三 示例 / §五 验收 / §八 时间线）
  - `APS_V1_CTP评估主数据迁移_3号位致4号位_归属A回执_v1.0_20260930.md`（§四 域键唯一性核对预判 / §六 时间线）
  - `APS_V1_CTP_domain_seed_清单终稿_3号位致4号位_v1.0_20261009.md`（§三 8 键清单终稿，提报附件）
  - `LPS.APS.Web/Sql/APS_CTP_domain_seed_202610xx.sql`（迁移脚本 v3，提报附件）

---

## 一、背景（一句话）

CTP 评估接口契约 v1.0 已登记，裁决 A：**3号位 全栈 Owner**（业务定义 + `Material.ProductFamilyId` + 迁移 + 测试 + 冒烟）。时间线：§三 清单终稿 **10-08**、迁移+9 单测+14 路径冒烟 **10-15**。C 路径生效前置④：dev 主数据须有 `Material.ProductFamilyId → DomainDefinition(IsActive=1)` 映射。

## 二、域键唯一性核对结果（2026-10-09 dev 实况，只读核实）

| 对象 | dev 实际状态 |
|---|---|
| `DomainDefinition` 现有域键 | **仅 3 条**：`FAMILY_X`（FAMILY, PF1=CYL）、`BJ_FAMILY_Y`（FACTORY_FAMILY, PF2=FRL, F=BJ）、`TJ_FAMILY_Y`（FACTORY_FAMILY, PF2=FRL, F=TJ） |
| 真实产品族 | `CYL/FRL/VALVE/LGJ/XC/JS/WKQ/TZP/MJ/QT`（前 10）+ 测试 seed（`TF_*`/`PF_Sx_*`） |
| 真实工厂 | `CN / BJ / SH / TJ` 等 |

> **说明（v1.1 撤回）**：4号位 函 §三 示例的 `M-001..M-007 / F-SUZ-01 / F-CDG-02 / 电机壳体等 7 产品族` 经确认系**前端硬编码演示假数据**（CTP 契约登记 v1.0 §六.4「前端硬编码物料」），非真实生产主数据——不构成真实主数据迁移阻塞。真实 CTP 冒烟/验收将基于**真实 dev 主数据**目标执行。

## 三、冲突点（原待裁问题，已裁决）

**示例域键跨产品族复用违反 `UQ_DomainDefinition_DomainKey UNIQUE(DomainKey)`**：`FAMILY_INJECTION` 跨 4 产品族、`BJ_FAMILY_INJECTION` 跨 3 产品族，照抄必违反唯一约束；且 dev 既有 3 键与示例键无交集。该冲突与示例数据真假无关（凡真实 CTP 主数据新增域键均受 UQ 约束）→ **已由 0号位 裁决**，见 §四。

## 四、0号位 裁决（Q1 域键分配——已裁）

### 裁决原文（2026-10-09）

> DomainKey 采用**「一产品族一 Domain」**的原则，即选择方案 (a)。**不同 ProductFamily 不得共用同一 DomainKey**。DomainKey 不是 ProductFamily.Code 别名，而是 **APS 内部稳定业务计算域标识**，由 3号位 通过 DomainDefinition 治理。**Factory 维度不自动拆域**，仅在业务计算边界、计划发布边界确实独立时采用 Factory+ProductFamily 形成独立 Domain。

### 裁决落地口径（3号位 执行）

| 原则 | 落实方式 |
|---|---|
| 一产品族一 Domain | 每个真实 ProductFamily 一个独立 `DomainKey`；`DomainDefinition` 每行 DomainKey 全局唯一（UQ） |
| DomainKey ≠ ProductFamily.Code 别名 | DomainKey 命名取 APS 内部业务计算域语义（如 `FAMILY_INJECTION_<产品族>` 族系），不复用 `ProductFamily.Code`；3号位 治理命名 |
| Factory 不自动拆域 | 默认 `ScopeType='FAMILY'`（全工厂共享域，FactoryId=NULL）；**仅当业务计算边界/计划发布边界确实独立**才用 `FACTORY_FAMILY`（Factory+ProductFamily），须 3号位 举证边界独立性 |
| 既有 3 键 | `FAMILY_X/BJ_FAMILY_Y/TJ_FAMILY_Y` 保持不动（历史运行兼容），不与新键合并、不删改 |

## 五、Q2 自决 + 待 0号位 确认（1 项）

### Q2（时间线顺延）——已由 3号位 自决，不再提请 0号位
- 时间线顺延由 3号位 侧直接拍板（用户 2026-10-09 指示），不再提请 0号位 裁决；§三 清单终稿 + 迁移脚本已按真实主数据**先行定稿**，执行不受 Q2 影响。

### Q3（清单终稿 + 迁移脚本确认）——提请 0号位 把关
- **内容**：§三 清单终稿 v1.0（8 个新域键 `FAMILY_VALVE/LGJ/XC/JS/WKQ/TZP/MJ/QT`，全 `ScopeType=FAMILY`，FactoryId NULL）+ 迁移脚本 v3 `APS_CTP_domain_seed_202610xx.sql`（仅 INSERT 8 键，fail-closed + 幂等，dev-only）。
- **确认点**：① 域键命名（`FAMILY_<族>`，非 Code 别名，对齐既有 `FAMILY_X` 前缀风格）；② 全 FAMILY 不拆域口径（`FRL` 既有 `BJ/TJ` 两厂 FACTORY_FAMILY 保持不动，其余 8 族默认全厂共享域）；③ 不 UPDATE Material（真实映射已存 ~27 万条）。
- **确认后**：3号位 方执行 dev 落库 + CtpEvaluateTests 9 单测 + 真实路径冒烟。

## 六、迁移执行口径（裁决后，3号位 按真实主数据落地）

1. **真实目标**：迁移脚本基于真实 dev 主数据（真实产品族/物料/工厂）编写，不造演示假数据。
2. **域键命名**：按「一产品族一 Domain」逐真实产品族生成独立 `DomainKey`；默认 `FAMILY`；确有独立边界的才 `FACTORY_FAMILY`（附边界举证）。
3. **冒烟路径**：真实 CTP 冒烟选真实物料（存在 `ProductFamilyId` 且产品族有 `IsActive=1` DomainDefinition 映射）+ 真实工厂，路径数按真实数据可达数量定。
4. **演示数据**：如需支持前端 demo（M-001..M-007 撤下拉后走 resolve），属演示 seed 范畴，另行确认，不入本次裁决。

## 七、待办衔接

| # | 事项 | 归属 | 状态 |
|---|---|---|---|
| 1 | 域键唯一性核对（dev 实况） | 3号位 | ✅ |
| 2 | 0号位 裁决 Q1 域键分配（方案 a + 两条原则） | 0号位 | ✅ 已裁（v1.2） |
| 3 | Q2 时间线顺延自决 | 3号位 | ✅ 自决（10-09 指示，不再提 0号位） |
| 4 | §三 清单终稿 + 迁移脚本 v3 定稿（真实主数据 + 一产品族一 Domain） | 3号位 | ✅ 已定稿（v1.0 / v3） |
| 4b | 清单终稿 + 脚本 提报 0号位 确认 | 0号位 | ⏳ **待确认**（确认后才落库） |
| 5 | dev 落库 + CtpEvaluateTests 9 单测 + 真实路径冒烟 | 3号位 | 待 #4b |
| 6 | 核心试算接入（替换 501） | 1号位/2号位 | 待引擎 |
| 7 | verify-acceptance D01-D02 动态断言 | 4号位 | 待 seed 落地 |

— 3号位
