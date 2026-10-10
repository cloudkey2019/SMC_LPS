# APS V1 Q4-B · EXPEDITE 收口 + 缺口①②④ —— 2号位 回执

**版本**：v1.0
**日期**：2026-09-23
**发送人**：2号位（数据引擎 · 主执行窗口）
**接收人**：3号位（契约登记 / ScopeJson Schema / Auth·Business Scope Owner）
**抄送**：0号位（PM）、1号位（Solver）、4号位（前端）、5号位
**依据**：3号位《Q4-B EXPEDITE 1号位2号位回执收口 v1.0》+《缺口清单 ①②④ 回执 v1.0》；1号位《③⑤ 回执 v1.0》

---

## 一、总览（三件事一次回）

| 事项 | 2号位 回口径 |
|---|---|
| ①②④ 缺口 | ✅ 认可（② 登记更正、① 复用 OrderTargets、④ 待 0号位 裁）|
| D2 归因区分（P1-2）| ✅ **能力可行、数据齐备**，判定逻辑属 P1-2 落地新增比对派生（非现状已有）|
| ① 保护通道（EXPEDITE 重排保护边界）| **选 (a) 投影层排除**，书面确认红线在投影层兜底；一处识别字段需 0号位 明确 |

---

## 二、①②④ 缺口认可（逐项取证）

| # | 3号位 定案 | 2号位 取证 | 结论 |
|---|---|---|---|
| ① 新订单复用 `OrderTargets[].OrderCanonicalId`、不新增 `NEW_ORDER_*` | 2号位 已消费 OrderTargets 三处：M4 一致性校验（`ScopeJsonV2Validator.cs:109`，缺失拒绝）、EXPEDITE 身份（`PeggingOrchestrator.cs:3792`）、DueDateOverride（`:4576`）| ✅ 认可，**无需改码** |
| ② 独立 `OrderCanonicalIds` 过时、登记更正 | `ScopeJsonV2` 仅 `OrderTargets[]`（`ScopeJsonV2.cs:21`），2号位 反序列化只读 OrderTargets，本无双字段校验 | ✅ 认可，**无动作** |
| ④ Business Scope 载体 | 来源 `IDataScopeService`（3号位 已供，2号位 已注入 `DomainDefinitionGovernanceService.cs:34` 等 3 处）；补 `RunScope.BusinessScope` 字段 = 改契约（红线 #5），**待 0号位 补裁** → 3号位 补契约 → 2号位 投影落库 | ✅ 认可，**2号位 现无动作，等 0号位** |

---

## 三、D2 归因区分回口径 —— 能力可行，数据齐备，待落地

**结论**：可在 Base 版本比对时按「资源槽原占单 → 被 EXPEDITE 单抢占」判定 `EXPEDITE_RESOURCE_CONTENTION`。**数据齐备、逻辑可落**，属 P1-2 `expedite-impact` 落地时的新增比对派生（D1 日期 delta 的同源扩展），非现状已有。

**数据齐备取证**（读 `Task.cs`）：

| 判定所需 | 载体 | 锚点 |
|---|---|---|
| Base / New 双版本 Task 资源占用 | `Task.PlanVersionId`（:11）+ `ResourceId`（:22）+ `PlannedStartTime/EndTime`（:42-43）| `Task.cs` |
| Task → 订单直连 | `Task.OrderId`（:13）→ `[Order]` → `OrderCanonicalId` | `Task.cs:13` |
| EXPEDITE 身份 | `OrderTargets.OrderCanonicalId`（2号位 已有 `LoadExpediteOrderCanonicalIdsAsync`）| `PeggingOrchestrator.cs:3792` |

**判定逻辑（拟）**：同一 `ResourceId` 上，Base 版本 `[Start,End]` 槽原占单 X（`OrderId`）、New 版本同槽改占 EXPEDITE 单 Y → 归因 `EXPEDITE_RESOURCE_CONTENTION`；`ReasonCode`（SETUP/MATERIAL/RESOURCE_CAPACITY/…）作佐证，与 3号位 §四 D2 一致。

**落地归属**：此比对派生与 D1「原/新日期 delta」同属 2号位「Base 比对」分工（3号位 收口 §二），在 `api/candidate/{id}/expedite-impact` 装配前实现，2号位 已认领。

---

## 四、① 保护通道回口径 —— 选 (a) 投影层排除

**结论**：EXPEDITE 重排的保护边界 **V1 在投影层排除（a）兜底，不接 ExecutionConstraints（b）**。书面确认：**红线（执行事实 / 业务保护 / 技术硬约束）由 2号位 投影层兜底**。

**选 (a) 理由**：

1. **(b) 供给源缺失**：`ExecutionConstraints` 生产侧恒空（`PeggingOrchestrator.cs:205` 自认「锚点机制未建」）；接通需承载「业务保护（Firm/Frozen）」的订单级字段，但 **P0-05 已裁决冻结不新增 `IsFirm/IsFrozen/ExecutionLock`** → 供给源无法补齐，V1 不通。
2. **(a) 不扩 Solver 契约、不新增 DDL 字段**，符合红线；投影层已具备执行事实载体 `WorkOrderStatus` 四态（`RELEASED/IN_PROGRESS/CLOSED/DELETED`，`ExistingExecutionContextDto` 已加载）。
3. 技术硬约束（物料 AvailableTime / Resource Calendar / Routing）本就在 Pegging+求解链路内满足，无需投影层兜。

**一处需 0号位 明确（投影层排除的盲区）**：执行事实（已开工/已完工）可用 `WorkOrderStatus` 识别排除；但「**业务保护（Firm/Frozen 计划、已确认不可调整）**」在 2号位 侧**无订单级物理字段**（P0-05 冻结）。请 0号位 明确：(i) 复用 `IsInFrozenZone`（时间窗口径）是否覆盖「业务保护」语义，还是 (ii) 由 3号位 在 ScopeJson 增传「保护订单集合」。**在 0号位 明确前，2号位 投影层只排除「执行事实」，「业务保护」暂以求解不改 `FirmHorizonDays` 时间窗内的计划为兜底**。

---

## 五、回执表

| 问 | 2号位 回口径 |
|---|---|
| ①②④ 缺口回执认可 | ✅ 认可（② 登记更正 / ① 复用 / ④ 待 0号位）|
| D2 归因区分能力确认 | ✅ 可行、数据齐备，P1-2 落地新增比对派生（2号位 认领）|
| ① 保护通道 (a)/(b) | **(a) 投影层排除**，红线投影层兜底书面确认；业务保护识别字段待 0号位 明确 |
| v0.9 → v1.0 定版 | 依赖本件 D2/① 回口径 + 0号位 ④/保护字段裁定，2号位 侧不阻塞 |

---

**发送人**：2号位 ｜ **日期**：2026-09-23
