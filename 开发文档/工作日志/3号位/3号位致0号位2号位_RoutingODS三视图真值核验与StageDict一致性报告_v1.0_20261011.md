# 3号位 → 0号位 / 2号位：Routing ODS 三视图真值核验与 StageDict 一致性报告（v1.0）

> 发件：3号位（Routing ODS 事实 Owner 特殊例外，2026-10-11 0号位 职责调整）
> 致：0号位（即 PM）/ 2号位（Routing 运行装载 Owner）
> 日期：2026-10-11 ｜ 版本：v1.0
> 性质：**只读核验报告**（非冻结文档变更），作为组织「真实单 Domain 完整出计划」的提报附件
> 承接：0号位 2026-10-11 裁决「Routing ODS 事实 Owner=3号位」→ 3号位 首次实测 ODS 库 Routing 数据质量

---

## 一、核验范围与方法

| 项 | 说明 |
|---|---|
| 服务器/库 | ODS `10.116.2.73.MES_Integration`（Routing 三视图 + StageDict）；APS `10.116.2.75.APS_Production`（装载表现状） |
| 核验对象 | `MES_APS_Routing_Operation_View` / `MES_APS_Routing_Dependency_View` / `MES_APS_Routing_Stage_View` + `StageDict` |
| 方式 | 只读 SQL（行数 / NULL 计数 / 分布 / 抽样 / JOIN 一致性），零写入 |
| 契约基准 | 07 字段说明 v5.1.10 §4.4b/4.4c（MES_APS_Routing_Operation/Dependency_View 契约）+ §1.9 StageDict |

---

## 二、核验结果

### 2.1 三视图真值存在（非空）✅

| 视图 | 行数 | 结论 |
|---|---|---|
| MES_APS_Routing_Operation_View | 1,923,035 | ✅ 有真实数据 |
| MES_APS_Routing_Dependency_View | 1,928,053 | ✅ 有真实数据 |
| MES_APS_Routing_Stage_View | 614,797 | ✅ 有真实数据 |

### 2.2 完整性核验 ✅（主体良好）

| 检查项 | 结果 |
|---|---|
| RouteCode 空值 | 三视图 **0** 空 ✅ |
| PathId 空值 | 三视图 **0** 空 ✅ |
| StageCode 空值 | 三视图 **0** 空 ✅ |
| ProcessCode 空值（Operation） | 0 空 ✅ |
| **StageCode ↔ StageDict 一致性** | **0 缺失**（Operation 全部 StageCode 命中 StageDict；StageDict 共 29 行）✅ |
| 启停事实 IsActive | 全为 1（192万启用、0 停用）——当前无停用工序 |
| 微小缺口 | Operation 部门码空 38 行；StandardTime 空 10 行；SetupTime 空 0 |

### 2.3 契约漂移发现 ⚠️（需 0号位 确认口径 / 2号位 装载侧对齐）

| # | 发现 | 冻结契约（07） | 实测（ODS 视图） | 影响 |
|---|---|---|---|---|
| D-01 | 物料标识列名 | 显式输出**标准化 MaterialCode** + MES_ID + Model | 无 MaterialCode 列，实为 **ProcessCode** | 契约列名与实物不符；`ProcessCode` 是否为标准化 Material 身份、与 MaterialMapping 绑定链需确认 |
| D-02 | 来源系统列名/取值 | `SourceSystem`，'MES'（当前唯一来源） | 列名 `Source`，取值 **ERP 1,917,842 行 + MES 5,193 行** | 列名不符且来源以 ERP 为主，与契约「MES 唯一来源」矛盾 |
| D-03 | 有效期事实字段 | 0号位 裁决 3号位 负责「有效期与启停事实字段正确传递」 | 无 `EffectiveFrom/EffectiveTo` 列，仅有 IsActive | ODS 视图不含有效期版本化字段；Routing 版本化（EffectiveFrom/To，08 DDL）由装载侧 2号位 承担还是 ODS 补列，需定案 |
| D-04 | FINAL 身份 | MaterialCode 已区分普通/FINAL 身份（FINAL_ 前缀） | `ProcessCode LIKE 'FINAL_%'` 计数 = **0** | ODS 视图无 FINAL_ 前缀行；FINAL 身份是否由 MaterialMapping 承接需确认 |

### 2.4 数据质量异常 ⚠️（需 2号位 装载侧确认）

| # | 发现 | 数据 | 影响 |
|---|---|---|---|
| Q-01 | **ToOperationCode 空 614,810 行**（占 Dependency 全量 ~32%），全部 `DependencyType='ES'` | 样例：RouteCode=「组装,装配,...」PathId=1 StageCode=BJ_ASSY From=M104 To=<NULL> | 61.5 万依赖记录无目标工序——装载侧若校验 To 非空将阻断；若已过滤则 APS 装载量应差 ~61.5 万（见 2.5）；ES 类型语义（结束/末工序？）需 2号位 确认 |

### 2.5 APS 库装载现状（2号位 侧，仅核验非空）

| 表 | 行数 | 结论 |
|---|---|---|
| RoutingOperation | 1,701,834 | ✅ 已装载真实数据（ODS 192万→APS 170万） |
| RoutingDependency | 1,168,138 | ✅ 已装载（ODS 193万→APS 117万，差值≈Q-01 空 To 61.5万 + 其他过滤，提示装载侧已过滤空 To 行） |
| RoutingStage | 539,419 | ✅ 已装载 |
| RoutingPlanningParam | **0** | ⚠️ 规划参数表为空（是否属本域 seed/装载范围待确认） |
| MaterialMapping | 1,801,031 | ✅ 有真值（Material 绑定基础具备） |

---

## 三、结论与建议

1. **Routing ODS 基础数据真实存在且大体完整**（192万工序 / 193万依赖 / 61万 Stage / StageDict 一致性成立），**真实单 Domain 跑通的 Routing 数据前提基本具备**。
2. **三类需定案/确认项**（不阻塞基础跑通，但影响契约一致性验收）：
   - **契约漂移 D-01~D-04**：视图列名与取值偏离 07 §4.4b/4.4c 契约（MaterialCode→ProcessCode、SourceSystem→Source、无 EffectiveFrom/To、无 FINAL_ 前缀）→ **3号位 建议**：以实物视图为准补录契约（勘误候选）或由 2号位 装载契约列透传时校正，**口径请 0号位 裁决**（3号位 不擅自改冻结文档）。
   - **Q-01 空 To 依赖**：614,810 行 ES 依赖无目标工序 → 需 2号位 确认装载侧处理（已过滤 or 保留）；若已过滤，需确认过滤后依赖图仍闭合。
   - **RoutingPlanningParam 空**：确认是否属本域装载/seed 范围。
3. **建议执行顺序**（供 0号位 组织跑通）：
   - 2号位：确认 Q-01 空 To 行装载语义 + RoutingPlanningParam 归属；
   - 0号位：裁决 D-01~D-04 契约口径（实物 vs 契约）；
   - 3号位：按裁决跟进（视图契约勘误候选材料 / StageDict 侧一致性维持），并提供单域所需 Domain/SPV/RSV/PSV/BatchPolicy seed 清单。

---

**发件**：3号位 ｜ 日期：2026-10-11 ｜ 版本：v1.0 ｜ SVN 提交归用户（renjunchang）
