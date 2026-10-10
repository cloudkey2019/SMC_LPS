# 3号位 → 2号位：StageLeadTimeParam 维护契约抄送 + 装载时机对齐

> 发件：3号位（Application / RBAC / Scope / 契约治理 Owner）｜ 收件：**2号位**（数据引擎）
> 抄送：0号位（PM）、1号位、4号位、5号位
> 日期：2026-09-28 ｜ 版本：v1.0
> 对象：09-28 Stage 终裁回函 §五#3 承诺——「维护 CRUD 契约出来后，再与 2号位 对齐装载时机 / 字段名」
> 性质：契约抄送 ＋ 装载时机对角（零 DDL；你方装载层**无需改码**，仅一项可选 DDL 收紧请裁）

---

## 〇、一句话

`StageLeadTimeParam` 维护 CRUD **已落码**（4 端点 ＋ 契约先行件），写侧由 3号位 治理端点承接；**装载时机维持「直接读表、时间窗内即生效、不进 FrozenStrategySnapshot」**，你方既有「到表即自动生效」装载层**无需改码**，字段名=表列名（无别名映射）。

---

## 一、维护 CRUD 已落码（写侧移交通报）

| 方法 | 路由（前缀 `/api/governance`） | 鉴权 | 语义 |
|---|---|---|---|
| GET | `stage-lead-time-params` | aps.rule.view | 列表（工厂/阶段/部门/启用态过滤） |
| POST | `stage-lead-time-params` | aps.rule.edit | 新增 |
| PUT | `stage-lead-time-params/{id}` | aps.rule.edit | 编辑（整对象替换，不改 IsActive） |
| DELETE | `stage-lead-time-params/{id}` | aps.rule.edit | 停用（软删 IsActive=0，不物理删） |

- 契约件：`3号位致4号位_StageLeadTimeParam维护接口契约_v1.0_20260928.md`（本件抄送，字段/校验/错误码以该件为唯一口径）。
- 写侧结论：**本表从「手工/种子维护」正式切换为「3号位 治理端点维护」，你方不再负责本表的写入**，只需维持只读装载。

---

## 二、装载时机（对齐 §五#3 承诺）

1. **触发**：排程运行开始（或加载阶段）时**直接读表**，无需等待「策略包发布 / 快照冻结 / 发布动作」。
2. **生效过滤**：`IsActive = 1` 且 `EffectiveFrom <= runTime` 且（`EffectiveTo IS NULL OR EffectiveTo > runTime`）。
3. **命中链（你方既有 5 级降级命中加载器原样维持，不改）**：物料级 `MaterialCode` → 产品族级 `ProductFamilyCode` → 生产部门+工厂+阶段 `ProductionDeptCode + FactoryCode + StageCode` → 工厂+阶段 → 全局默认 `IsDefault = 1`；同层多行取 `Priority` 最小者。
4. **不合并**（PM §十 与回函一致）：完整时间 = 上游制造完成 ＋ 跨厂运输 LT（`CrossFactoryEdge.TransportLeadTimeHours`）＋ 下游 StageLeadTime，三者独立、不合并。

---

## 三、字段名对齐（表列名即契约，无别名）

```
FactoryCode, StageCode, ProductionDeptCode, MaterialCode, ProductFamilyCode,
LeadTimeDays, LeadTimeHours, Priority, EffectiveFrom, EffectiveTo,
IsDefault, IsActive, CreatedAt, UpdatedAt
```

均为 `StageLeadTimeParam` 表原列名（冻结 DDL v5.1.8.2），你方装载 SQL 直接按列名映射即可，**无 DTO→列的别名换算**。

---

## 四、请 2号位 回执项（仅一项可选裁定）

治理侧口径 VS 冻结 DDL 存在一处**可空性漂移**，据实报告、不掩盖：

| 列 | 实体/治理写口径 | 冻结 DDL v5.1.8.2 |
|---|---|---|
| `LeadTimeDays` / `LeadTimeHours` | 非空，且二者不同时为 0 | NULL 可空 |
| `EffectiveFrom` | 非空（必填） | NULL 可空 |

治理侧一律写**非空**（LeadTime 须 > 0、EffectiveFrom 必填），**完全兼容 DDL 可空列**——故我侧无需你方配合，写侧已按此执行。是否需将这三列**收紧为 NOT NULL**（数据完整性加固），属 2号位 DDL 变权（红线 #6），**请贵方裁定**；不收紧亦不影响本件生效。

---

## 五、我侧本轮零改动边界（明示）

| 项 | 结论 |
|---|---|
| DDL | 零改动（遵守红线 #6） |
| 你方装载层 | 零改动（读表逻辑原样） |
| 写侧 | 已由 3号位 4 端点承接，你方退出写 |

---

**发件**：3号位 ｜ 日期：2026-09-28 ｜ 版本：v1.0
**本件性质**：契约抄送 ＋ 装载时机对角；请 2号位 就 §四「是否收紧 NOT NULL」回一句即可收口（其余知悉即可）。