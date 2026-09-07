# APS V1 3号位 待0号位裁决：跨号位接口归属

**版本**：v1.0
**日期**：2026-08-29
**提请人**：3号位
**裁决对象**：3-4 联调中涉及多号位边界、当前存在归属分歧/未决事项的接口
**背景**：3-4 联调规格 v1.1 已完成（G1/G2/G3/G5/G6/G7/G8/G9/G10 已实现），剩余事项均涉及 2号位 运行结果域或写操作归属，按"不要越位"红线 3号位 不自行处置，提请 0号位 裁决。

---

## 裁决项 1：G4 Run 列表查询（页面9"运行状态"）

**背景**
- 4号位 页面9 需要 Run 列表（RunId/RunType/Status/DataCutoffTime/StrategyProfileVersion/ExpectedDomainKeys/CompletedAt/ErrorMessage）
- 现有可替代：F5 单条 trace + G1 版本列表（v1.1 §二 G4 可替代方案，但无法展示"运行历史列表"）
- v1.1 §8.3 原则："查询端点本身属 3↔4 范围（同 G7/G8），数据源归 2号位"

**分歧点**
- ScheduleRun 表属 2号位 运行结果域；v1.1 标注"是否纳入本轮由 3号位×4号位 确认（涉及 2号位 数据源）"
- 若纳入：3号位 建 `GET /api/Governance/runs`，需 2号位 授权读取 ScheduleRun 表通道（未正式确认）

**候选方案**
- A. 纳入本轮：3号位 建查询端点，2号位 出具读取通道授权
- B. 本轮不做：页面9 以 F5 + G1 覆盖（降级）
- C. 2号位 自建查询端点直供 4号位（打破"2号位 无 HTTP 端点"惯例）

**3号位 建议**：A（页面9 需要多 Run 列表对比，F5 单条 + G1 是 PlanVersion 列表，覆盖不完整）；若 0号位 判断本轮资源有限，可 B 降级，G4 排期后置。

---

## 裁决项 2：手工释放 Demand Protection（4号位 操作入口）

**背景**
- v1.1 §8.3 明确"手工释放 Demand Protection 动作 → 2号位"（写操作，写 DemandProtection 释放记录）
- 但 2号位 **无对外 HTTP 端点**，4号位 无触达通道
- LockJson 配置（Demand Protection 规则）在 3号位 治理侧；释放动作落 2号位 运行域

**分歧点**
- 3号位 是否封装端点"中转"调 2号位 服务？还是 2号位 自建端点直供？还是本轮不做？
- 3号位 顾虑：封装"中转"意味着 3号位 编排 2号位 域写逻辑，有越位风险

**候选方案**
- A. 3号位 封装端点 `POST /api/Governance/.../release-protection`，内部调 2号位 领域服务（2号位 提供）
- B. 2号位 自建 HTTP 端点直供 4号位（存在 5号位 ProcurementManualEtaController 直连先例）
- C. 本轮不做（4号位 该页面操作后置）

**3号位 建议**：B（写操作归 2号位 域，自建端点不增加 3号位 越位风险，且有 5号位 直连先例）；若 2号位 无 HTTP 端点纳入规划，则 A 亦可（2号位 只出领域服务，3号位 薄转发）。

---

## 裁决项 3：发起 Manual Reschedule（ScheduleRun 创建/触发）

**背景**
- v1.1 §8.3 明确"ScheduleRun 创建/触发（MANUAL_RESCHEDULE 发起）→ 2号位"
- 2号位 无对外 HTTP 端点，4号位 无发起通道
- **边界冲突**：F4 FAILED 恢复（新建 ScheduleRun，R20）端点已由 3号位 实现并验收——同属 ScheduleRun 创建域，与 MANUAL_RESCHEDULE 发起边界需明确

**分歧点**
- 发起通道：中转 vs 2号位 直连 vs 本轮不做（同裁决项 2）
- F4 既有实现归属：维持现状（3号位 保留 F4，2号位 做 MANUAL 发起）还是统一归 2号位（F4 回退）？

**候选方案**
- A. 3号位 封装端点发起，内部调 2号位 服务；F4 维持现状
- B. 2号位 自建发起端点（同裁决项 2-B）；F4 维持现状
- C. 2号位 统一接管 ScheduleRun 创建（含 F4 回退给 2号位）
- D. 本轮不做发起（仅保留 F4）

**3号位 建议**：A 或 B（F4 已实现且 R20 已验收，不建议回退徒增回归；MANUAL 发起按裁决项 2 的归属原则一并裁定）。若 0号位 认为 ScheduleRun 创建应完全归 2号位 统一，则选 C，3号位 配合回退 F4。

---

## 裁决项 4：G7/G8 数据源读取通道授权（已完成接口的遗留确认）

**背景**
- G7 域依赖查询端点已实现（3号位）：数据源 `APS_Production.Domain_Dependency` 表由 **2号位** `sp_ScanDomainDependency`（每日 01:50）TRUNCATE+INSERT 全量刷新
- G8 Run 域级状态汇总端点已实现（3号位）：数据源含 2号位 运行结果（ScheduleRun 状态）与 3号位 PlanVersion
- v1.1 §8.3 原文："仅需向 2号位 确认读取通道"——**该授权确认未正式落实**

**分歧点**
- 3号位 跨号位读取 2号位 落库表（Domain_Dependency、ScheduleRun）的通道授权，无 0号位/2号位 正式背书

**请求**
- 0号位 背书确认：3号位 对 Domain_Dependency、ScheduleRun 表只读访问通道正式授权；或责令 2号位 出具读取通道确认回执

---

## 裁决项 5：INVALID_WAREHOUSE_REF 校验缺口排期（§8.2）

**背景**
- 参数集发布前校验缺"Warehouse 参数是否引用有效 Warehouse"（现仅查重复）
- 原因：Warehouse 有效性需 **2号位 主数据**，v1.1 §8.2 明确"本轮不纳入，待 2号位 主数据查询就绪后再补"

**分歧点**
- 该缺口由哪个号位排期：2号位 主数据就绪后 3号位 补校验（读 2号位 主数据），还是 2号位 直接提供 Warehouse 有效性校验服务？

**3号位 建议**：先 2号位 提供 Warehouse 主数据查询能力（或有效性校验服务），3号位 在 E2 发布前校验接入 `INVALID_WAREHOUSE_REF`；请 0号位 明确归属与排期责任。

---

## 附注：全仓测试基线风险知悉（与上述裁决无关）

- 3号位 全量测试（LPS.APS.Tests）：**135 通过 / 5 跳过 / 0 失败**，基线绿
- **LPS.APS.BusinessRules.Tests（5号位）：18 失败**（如 `ProductionInstructionPositionCalculatorTests` Quantity 15 vs 40 等，Transit/Stage/Supply 计算逻辑）——与 3号位 改动无关，但影响全仓联调基线判断
- 请求：0号位 知悉并转 5号位 收口测试基线

---

*本裁决请求由 3号位 整理；裁决结果将回写 v1.1 规格与任务清单。*
