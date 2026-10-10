# APS V1 ScopeJson SchemaVersion=2 文档同步登记（致 2号位 / 1号位）

**版本**：v1.0（同步登记，待 0号位 批准 + 各号位回执）
**日期**：2026-09-22
**发送人**：3号位（规则/参数治理、运行生命周期治理 Owner）
**接收人**：2号位（投影 M2~M5 + DDL 确认）、1号位（M5 消费壳）
**抄送**：0号位（批准）、5号位（查询侧，备查）
**性质**：**契约文档同步登记**——按 0号位 裁决执行 SchemaVersion=2 的「接口设计 / 字段说明 / DDL」文档同步
**依据**：
- 0号位《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》**四**（ScopeJson 采用 SchemaVersion 演进，需同步接口设计、字段说明、DDL）

---

## 〇、速览

```
0号位 四：不能因早期字段说明固定 11 字段，限制 Candidate 扩展。
          SchemaVersion=2 可承载 BusinessTriggerType / PriorityMode / OrderTargets / TaskTargets / ChangedResourceIds。
          需要同步：接口设计、字段说明、DDL。
现状：冻结字段说明 v5.1.8 行3600 ScopeJson=固定11字段；§七(行5534)仅"支持SchemaVersion"无字段集
3号位 已落地：RunScope 强类型（DomainSolveRequest.RunScope）承载 SchemaVersion=2 字段；序列化口径=A 字符串码（已授权落库）
待同步：字段说明文档 / 集成接口设计 / 契约文档三处；DDL 无变更
```

---

## 一、0号位 裁决（四）

> 采用 SchemaVersion 演进。不能因为早期字段说明固定 11 字段，就限制 Candidate 扩展。SchemaVersion=2 可承载：BusinessTriggerType、PriorityMode、OrderTargets、TaskTargets、ChangedResourceIds。需要同步：接口设计、字段说明、DDL。

---

## 二、现状核对

| 项 | 现状 | 位置 |
|---|---|---|
| 冻结字段说明 v5.1.8 | ScopeJson = **固定 11 字段**（Purpose/OrderCanonicalIds/FactoryIds/ProductFamilyIds/ResourceGroupIds/PlanHorizonStart/PlanHorizonEnd/LockedTaskIds/AllowTouchFrozenZone/AllowDelaySalesOrder/MaxImpactedOrders），⚠️ 不含 ExpectedDomainKeys | 行 3600 |
| 冻结字段说明 v5.1.8 §七 | "ScheduleRun.ScopeJson 继续作为 Candidate 输入承载。支持 SchemaVersion。不新增 CandidateInput/ScenarioInput/PriorityMode 字段"（指 DB 列）| 行 5534-5545 |
| 3号位 M1（已落地）| `DomainSolveRequest.RunScope` 强类型承载 SchemaVersion=2：`Trigger`(BusinessTriggerType) / `PriorityMode` / `InScopeLogicalDemandKeys` / `DueDateOverrides` / `TaskTargetOverrides` / `ChangedResourceIds` | DomainSolveRequest.cs RunScope |
| 序列化口径 | **A 字符串码**定案（BusinessTriggerType/PriorityMode 以字符串码序列化，授权 3号位 落库）| ScopeJsonV2 系列回执 |
| 投影四段 | 2号位 M2~M5 **未落地**；1号位 ScopeConstraint.cs 遗留死代码，M5 消费壳待 1↔2 另定契约 | — |

---

## 三、文档同步清单（0号位 四「需要同步」）

| # | 目标文档 | 位置 | 同步内容 | 责任 |
|---|---|---|---|---|
| 1 | 《APS_数据库字段说明文档_v5.1.8》 | 行 3600 ScopeJson 行 | "固定 11 字段" → 补：**SchemaVersion 演进，SchemaVersion=2 可承载 BusinessTriggerType/PriorityMode/OrderTargets/TaskTargets/ChangedResourceIds（与 11 字段并存、向后兼容）** | 3号位 登记 / 0号位 批准 |
| 2 | 《APS_数据库字段说明文档_v5.1.8》 | 行 5534 §七 | 补 SchemaVersion=2 **字段集明细 + 序列化口径（A 字符串码）** | 同上 |
| 3 | 《APS_集成接口设计_v1.33》 | ScheduleRun / ScopeJson 相关节 | 同步 SchemaVersion=2 载荷说明（Trigger 码表 / PriorityMode 码表 / 各 Override 结构）| 同上 |
| 4 | 《01_核心数据结构契约》 | RunScope 条目 | 登记 SchemaVersion=2 字段 ↔ `DomainSolveRequest.RunScope` 强类型一一对应 | 3号位 |
| 5 | DDL（ScheduleRun.ScopeJson）| 表结构 | **无变更**：NVARCHAR(MAX) 已容纳 SchemaVersion=2 载荷 | 2号位 确认 |

---

## 四、责任清单

| # | 动作 | 责任 | 说明 |
|---|---|---|---|
| 1 | 三处冻结/契约文档同步（§三 1-3）| 3号位 出登记 + 0号位 批准 | 冻结文档修改走既有指导流程 |
| 2 | 契约文档 RunScope 登记（§三 4）| 3号位 | 随本登记同步 |
| 3 | DDL 无变更确认（§三 5）| 2号位 | 回执确认 |
| 4 | 投影四段 M2~M5（ScopeJson → RunScope 各块）| 2号位 | 未落地，随 Run 链路推进 |
| 5 | M5 消费壳 + ScopeConstraint.cs 死代码处置 | 1号位（与 2号位 另定契约）| 遗留项登记 |

---

## 五、回执表

| 问 | 2号位 | 1号位 | 0号位 |
|---|---|---|---|
| §三 文档同步清单是否认可 | （待回执）| — | （待批准）|
| §三-5 DDL 无变更确认 | （待回执）| — | — |
| §四-5 M5 消费壳/死代码处置排期 | — | （待回执）| — |

---

*批准/回执后：按清单执行三处文档同步 → 契约文档 RunScope 登记 → 2号位 投影 M2~M5 推进。*
