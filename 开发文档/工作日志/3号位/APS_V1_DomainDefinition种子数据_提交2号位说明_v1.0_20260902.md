# DomainDefinition 种子数据 —— 提交 2号位的说明

> **文档性质**：3号位 → 2号位 种子数据交付说明（跨号位协作）
> **版本**：v1.1（修订：种子数据从现有数据库真实主数据读取，绑定真实 CYL/FRL/BJ/TJ，去除合成演示主数据）
> **日期**：2026-09-02
> **状态**：交付（待 2号位 确认数据口径与执行时机）
> **关联文档**：
> - `开发文档/2026-09-02文档更新/APS_联调种子数据_DomainDefinition_20260902.sql`（本次交付的种子脚本）
> - `开发文档/工作日志/3号位/APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901.md`（0号位 裁决，§2.2 示例域）
> - `开发文档/2026-09-02文档更新/APS_数据库表结构设计_v5.1.4_20260901_Domain专项冻结对齐_治理修正版.sql`（DomainDefinition DDL）

---

## 一、交付内容

按任务④d（**DomainDefinition 种子数据，3号位/PM 提供**）且按用户指示**种子数据从现有数据库真实数据读取**，3号位 已准备种子脚本：

- **脚本文件**：`开发文档/2026-09-02文档更新/APS_联调种子数据_DomainDefinition_20260902.sql`
- **内容**：3 条稳定排程域（DomainDefinition），绑定**现有库真实主数据**（不 INSERT 主数据）
- **数据来源**：`ProductFamily` / `Factory` 按 Code 只读查询取 Id（实测：CYL=1、FRL=2、BJ=3、TJ=6；脚本不硬编码 Id，按 Code 解析）
- **目标库**：`APS_Production`
- **幂等**：按 `DomainDefinition.DomainKey` `IF NOT EXISTS` 守卫，可重复执行
- **合规**：仅数据 INSERT，无 DDL 变更（红线 #6）

**实测确认（2026-09-02）**：`DomainDefinition` 表已由 2号位 按 v5.1.4 迁移创建、当前为空——脚本前置检查可直接通过；真实主数据 CYL/FRL/BJ/TJ 均在库。

## 二、3 个域（0号位 裁决 §2.2 字面键，绑定真实主数据，用户已确认）

| DomainKey | DomainName（3号位 拟定口径） | ScopeType | 产品族（真实） | 工厂（真实） | 含义 |
|---|---|---|---|---|---|
| `FAMILY_X` | 气缸全厂域（联调） | `FAMILY` | CYL 气缸 | （NULL） | 全厂级、单产品族 |
| `BJ_FAMILY_Y` | 北京厂三联件域（联调） | `FACTORY_FAMILY` | FRL 三联件 | BJ 北京工厂 | 北京工厂 × 三联件 |
| `TJ_FAMILY_Y` | 天津厂三联件域（联调） | `FACTORY_FAMILY` | FRL 三联件 | TJ 天津工厂 | 天津工厂 × 三联件 |

**选择理由（绑定真实数据）**：

- `FRL`（三联件）说明「包括BJ和TJ全部」——与 0号位 裁决 §2.2 的"同一产品族 Y 跨 BJ/TJ 两个 FACTORY_FAMILY"模式天然吻合，故 `BJ_FAMILY_Y` / `TJ_FAMILY_Y` 绑定 FRL+BJ / FRL+TJ；
- `CYL`（气缸）说明「包括三个量产工厂全部」——适合全厂级 `FAMILY` 域，故 `FAMILY_X` 绑定 CYL。

字段口径对齐 v5.1.4 DDL：

- `ScopeType` 仅 `FAMILY` / `FACTORY_FAMILY`（V1 冻结值域，`CK_DomainDefinition_ScopeType` 强制）；
- `FAMILY` 必须 `FactoryId = NULL`，`FACTORY_FAMILY` 必须 `FactoryId` 非 NULL（`CK_DomainDefinition_FactoryScope` 强制）；
- `DomainKey` 是**稳定 / 唯一 / 可追溯**的域标识，**不是 ProductFamily.Code**（0号位 裁决 §5）。本批保留裁决字面示例键 `FAMILY_X` / `BJ_FAMILY_Y` / `TJ_FAMILY_Y`（用户确认），键名本身不可直接追溯到产品族，**绑定关系固化在 DomainName 与本文档/脚本注释中，实现配置级可追溯**。

## 三、前置条件（已实测满足）

1. **物理建表**：`DomainDefinition` 表已存在（v5.1.4 已迁移，2026-09-02 实测为空）✅；
2. **真实主数据**：CYL / FRL（ProductFamily）、BJ / TJ（Factory）均在库 ✅；
3. **命名确认**：DomainName 为 3号位 拟定业务口径。**正式命名请 PM 确认后，3号位 更新脚本重跑**（脚本幂等，重跑仅补新增）。

## 四、2号位 回执

| 事项 | 3号位 说明 | 2号位 回执 |
|---|---|---|
| 数据口径 | 3 域（字面键绑定真实 CYL/FRL/BJ/TJ）是否可用 | □ 可用　□ 需调整（请说明） |
| 执行时机 | 确认后由 2号位 在 APS_Production 执行脚本（表已存在，可直接执行） | □ 已执行　□ 待执行 |

## 五、3号位 收到回执后的动作

1. 若命名需调整 → 按 PM 口径更新脚本重跑；
2. 与 B-1 白天候选运行的 DomainKey 取值联动（B-1 创建时 DomainKey 取自本治理配置，见《白天Candidate运行创建_提交2号位需求说明》）。
