# 派单：MaterialStageDeptContext 部门锁凭据缺失的根因 —— 公司部门码 ↔ 生产单元(holon) 缺映射桥 —— 2号位 → PM

> 收件：PM（拍板映射权威来源与归属）
> 发件：2号位（Pegging + Scheduling）
> 抄送：5号位（ODS / ERP 同步侧事实）
> 依据：3号位《被 2号位 阻塞任务清单》P0「MaterialStageDeptContext 全表空」+ 2号位 09-07 全库实测
> 日期：2026-09-07

---

## 结论一句话

`MaterialStageDeptContext` 重建 0 行，**不是 SP bug、不是 "没跑"、也不是 5号位 数据脏**；根因是——MSC 里只有 **公司级部门代码（3 位）**，而 APS 排程字典用的是 **生产单元（holon，另一套 3 位码）**，两套编码不同源、库中无映射桥，SP 按「不得凭空建映射」裁定正确拒了。**谁建/谁维护这座桥，需 PM 拍板。**

---

## 一、背景：这表是干嘛的（一句话）

`MaterialStageDeptContext` =「物料 × 阶段 → 默认生产部门」映射，是 **1号位 部门锁定 `ApplyDepartmentLock` 的唯一消费入口**（按 `(MaterialId, StageCode)` 取 `ProductionDepartmentId`）。查不到 → 该料路由被剔除 → 判 `Unscheduled` → 端到端 `FinalTasks=0`。所以它空 = 白天/整条排程全红。

---

## 二、四套编码实测对照（2026-09-07 全库查证）

| 编码 | 在哪 | 实际值样本 | 量 |
|---|---|---|---|
| **公司级部门代码** | `MaterialSupplyContext.DefaultProductionDeptCode`（ERP `sp_SyncMasterData` 同步，动不了） | `725` `619` `621` `-28` `-51` | 96 万行非空 |
| **生产单元 holon 码（3 位）** | `Resource.SourceProductionDeptCode` 内嵌（格式 `车间名+3位码+HL`，如 `CN_MACH_机加工2-1车间731HL`） | `731` `767` `768` `704` `910` … | 全在，且已填 `ProductionDepartmentId` |
| **车间字典主键** | `ProductionDepartment`（DeptCode 中文 / SourceDeptCode 6 位 / Id 838+） | `CN_MACH_机加工2-1车间` / `116210` / `838` | 286 行 |
| MSC 里的生产部门 Id | `MaterialSupplyContext.DefaultProductionDepartmentId` | 全 NULL | 0 行 |

**已通与断掉的链：**

```
holon(生产单元 Resource) ──ProductionDepartmentId──▶ ProductionDepartment(车间)   ✅ 已通（Resource 已挂车间主键）
公司部门码(725, MSC)     ──？？？？────────────────▶ 生产单元 / 车间               ❌ 断，全库无映射表
```

**全库已扫 `sys.tables` / `sys.columns`**：无任何 holon/生产单元/工作中心映射表。可当桥的候选列只有 `Resource.SourceProductionDeptCode`、`MaterialSupplyContext.ProcurementDeptCode`（空）、`OutsourceDeptCode`（空）、`MaterialStageDeptOverride.ProductionDeptCode`——没有一张是「公司部门码 → holon」的对照表。

**SP 行为复核（非 bug）**：`sp_RebuildMaterialStageDeptContext` 两条解析路径（标准 Id 优先 → DeptCode 回退）在数据里全走不通，遂将 86.1 万行记入 `DEPT_NOT_IN_DICT`、48.7 万行记入 `MISSING_DEPT_IN_MSC`，产出 0 行。这是严格遵循「不得凭空建映射」裁定的正确表现。

---

## 三、请 PM 拍板三件事

### Q1（最重）：「公司部门码 → 生产单元/车间」的权威映射从哪来？

- **A.** ERP 主数据里本就有「公司部门 ↔ 生产单元」对照，只是 `sp_SyncMasterData` 没同步这层 → 补同步一张映射表（归属 ERP/5号位）；
- **B.** ERP 里公司部门直接对应车间，用另一张 ERP 部门主档补 `SourceDeptCode`/外键对齐；
- **C.** ERP 没有这层关系，需在 APS 建一张「公司部门码 ↔ ProductionDepartment」人工维护表；
- **D.** 其他（请说明）。

### Q2：映射表建成后归谁维护？

- 5号位 ODS 同步维护 / 2号位 APS 侧维护 / ERP 主数据维护。**补一句**：若要 2号位 建 `公司部门码↔车间` 表，请一并授予初始灌值源（否则 2号位 也无从对齐，仍是 0 行）。

### Q3：映射落地前，这个 P0 怎么应急（要不要先松口）？

- **a.** 允许先用 `MaterialStageDeptOverride`（已存在，含 `ProductionDeptCode`）把**测试物料直配车间**，先让端到端转绿，映射桥后补；
- **b.** 允许缺失语境的料走「默认车间兜底」而非 `Unscheduled`（需裁定默认车间由谁定）；
- **c.** 不应急，等映射桥落地一次性关（请给排期）。

---

## 回执表

| # | 请 PM 答复 | 回执 |
|---|---|---|
| Q1 | 权威映射来源（A/B/C/D） | □ A □ B □ C □ D：____ |
| Q2 | 映射表维护归属 | □ 5号位 □ 2号位 □ ERP：____ |
| Q3 | 落地前应急 | □ a（测试物料 Override 直配）□ b（默认车间兜底）□ c（等排期）：____ |