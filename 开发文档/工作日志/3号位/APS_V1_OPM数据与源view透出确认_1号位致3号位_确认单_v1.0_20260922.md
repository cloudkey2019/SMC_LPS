# 确认单：OPM 三值数据 / 源 view 透出 / M5 契约登记 —— 1号位 致 3号位

> 收件：3号位（规则/参数治理）／抄 0号位、2号位
> 日期：2026-09-22
> 性质：**1号位 侧三项待 3号位 落实（阻塞分型端到端验证与 M5 契约）**

---

## 一、`OperationPlanningMode` 三值数据维护（分型生效的**数据前提**）

**背景**：0号位 20260922 裁决「`OperationPlanningMode` 属工艺数据，归 `RoutingOperation`，3号位 维护」。

**现状**：1号位 已完成**代码侧全部**——
- `RoutingOperation.OperationPlanningMode` 实体 + 装载 SELECT（2号位 r13432/r13433）；
- Phase2 正排/倒排**分型实现** + `ResourceId=NULL` Task；
- **单元测试 4/4 通过**（UNCONSTRAINED/WAIT_ONLY 无资源产 Task；FINITE 无资源 fail-closed）。

**缺口**：库上**三值数据未填** → 无设备工序（如配品 M101）仍标默认 `FINITE_RESOURCE` → **依然 fail-closed**、分型效果无法在真实数据上体现。

**请 3号位**：按工艺维护 `RoutingOperation.OperationPlanningMode` 三值（`FINITE_RESOURCE` / `UNCONSTRAINED` / `WAIT_ONLY`），至少覆盖已知无设备工序（配品 M101 类）。**数据就位后知会 1号位 跑端到端验证。**

## 二、routing 源 view 透出 `OperationPlanningMode` 列（数据来源）

**事实**：2号位 前单已提「待 3号位 routing **源 view 需透出 `OperationPlanningMode` 列**（DDL 已定，但源 view/数据由 3号位 维护）」——**该列未透出则装载取不到真实值**（回落默认）。

**请 3号位**：确认 routing 源 view（`ext_MES_APS_Routing_*` 或对应 ODS 视图）**是否需/已透出该列**；若源侧无此列，需明确**值从何处维护**（3号位 治理表 / 4号位 配置页面 / MES Routing）。

> **1号位 侧仅消费，不定义、不推断、不按「有无设备」自行判定**（0号位 裁决）。

## 三、M5 消费壳契约登记（待 1↔2 细则齐后 3号位 登记）

**背景**：3号位《三项待办催办》§三定「M5 消费壳 = 1号位（消费）+ 2号位（投影）双契约，3号位 出登记件」。

**1号位 侧已给**：
- ① `InScopeLogicalDemandKeys` 组合规则口径（《三项待办回执》§三.①）；
- ② 消费侧拒收口径（待 2号位 投影侧对齐）。

**请 3号位**：待 2号位 回 ② 后，**汇总登记** M5 消费壳契约（1号位 消费端 + 2号位 投影端）；若 ② 涉及 0号位 终裁（拒收策略），由 3号位 汇总正式协助项提报。

> **另**：M5 **详设本身**（`InScopeLogicalDemandKeys` 终裁 / `PriorityMode=EXPEDITE` 强度）1号位 已另发《M5详设与日历表维度确认 致0号位》——**详设明确前 1号位 不实现第二批**（DTO 注释明写「M5 详设前留空」）。

## 四、回执表

| # | 事项 | 请 3号位 |
|---|---|---|
| 1 | `OperationPlanningMode` 三值数据维护（覆盖无设备工序） | 落实 + 知会 1号位 |
| 2 | routing 源 view 透出该列 / 值维护归属 | 确认 |
| 3 | M5 消费壳契约登记（待 2号位 ② 后） | 登记 |

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）