# APS V1 Domain专项总交叉复审与修正收口报告 v1.0

**日期**：2026-09-01（北京时间）  
**性质**：Domain专项文档总交叉复审、问题修正与冻结前收口  
**结论级别**：业务口径通过；文档装配问题已修正；数据库正式结构已补齐；3号位实施包保留“母版 + Domain增量补充”两件套使用方式

---

# 1. 本次复审对象

本次交叉复审覆盖：

1. 《APS V1 最终全部流程与业务基线 v1.2》
2. 《APS Pegging供需承接与分层计算业务说明 v1.2》
3. 《APS 有限产能排产与滚动90天计划业务说明 v1.2》
4. 《APS 核心排产全流程走查 V3.18》
5. 《APS 数据架构与防腐层设计方案 v1.39》
6. 《APS 数据库表结构设计 v5.1.3 / v5.1.4》
7. 《APS 数据库字段说明文档 v5.1.3 / v5.1.4》
8. 《APS V1 2号位增量开发实施包 v1.3》
9. 《APS V1 3号位规则、参数、认证权限与运行生命周期开发实施包 v1.1 + Domain专项v1.2增量补充》
10. 《APS V1 4号位页面与业务操作开发实施包 v1.2》
11. 《APS V1 0号位总体项目验收包 v1.1》
12. 《APS V1 Domain定义、分域计算与运行边界业务裁决 v1.0》

---

# 2. 总结论

Domain专项的**业务方向没有发生漂移**。

最终仍然是：

> 排程域（Domain）只负责形成稳定排程、计划版本（PlanVersion）、候选计划（Candidate）、失败传播和发布边界，不重新设计Pegging、不重新设计有限产能求解器（Solver）、不建立第二套库存或运行平台。

经过本轮交叉复审，确认以下业务主线一致：

```text
排程域定义（DomainDefinition）
        ↓
Run启动冻结ExpectedDomainKeysJson
        ↓
2号位真实归域
        ↓
真实DomainKey
        ↓
按Domain依赖顺序串行逐Domain
        ↓
Domain内既有Pegging
        ↓
既有90天有限产能Solver
        ↓
每Domain独立PlanVersion
```

跨Domain继续通过：

```text
Material + Quantity + AvailableTime
```

传递数量—时间切片（Quantity-Time Slice）。

共享资源继续通过真实占用时间块向后续Domain传递。

白天Candidate继续严格单Domain。

---

# 3. 本轮发现并已经修正的问题

## P0-01：部分升版文档被错误写成“专项摘要版”

发现：

此前生成的以下Domain升版文件篇幅明显短于母版：

- 最高业务基线；
- Pegging专项；
- 有限产能专项；
- 全流程走查；
- 数据架构；
- 2号位实施包；
- 4号位实施包。

这种文件虽然Domain新增口径本身正确，但如果直接替换母版，会造成大量原冻结内容丢失。

### 修正

已改成：

> **母版全文完整保留 + Domain专项增量追加**

并生成“完整继承修正版”。

### 冻结要求

之前生成的短版Domain文档：

> **不得作为完整替代文档进入最终冻结目录。**

只能视为Domain专项增量说明/过程稿。

---

## P0-02：数据库正式DDL缺少DomainDefinition物理承载

Domain裁决已经明确：

> 系统必须存在持久化、可维护、可查询的DomainDefinition配置能力。

复审v5.1.3 DDL确认：

- 已有 `ScheduleRun.ExpectedDomainKeysJson`；
- 已有 `PlanVersion.DomainKey`；
- 已有 `Order.DomainKey`；
- 已有 `Domain_Dependency`；
- 但没有职责等价的正式DomainDefinition配置对象。

因此不能再保持“是否新增待确认”。

### 修正

正式增加最小：

`DomainDefinition`

字段：

- DomainKey；
- DomainName；
- ScopeType；
- ProductFamilyId；
- FactoryId；
- IsActive；
- SortOrder；
- 创建/修改审计字段。

V1 ScopeType只允许：

- FAMILY；
- FACTORY_FAMILY。

没有增加：

- DSL；
- SQL表达式；
- Domain运行表；
- Domain资源配额；
- VirtualInventoryBalance。

---

## P0-03：Domain_Dependency仍残留ProductFamily.Code旧语义

v5.1.3中仍明确写有：

```text
UpstreamDomainCode = ProductFamily.Code
DownstreamDomainCode = ProductFamily.Code
```

这与最新Domain冻结直接冲突。

### 修正

v5.1.4统一为：

```text
UpstreamDomainCode   = 上游真实DomainKey
DownstreamDomainCode = 下游真实DomainKey
```

字段名暂时保留历史兼容，不为“命名更漂亮”强制重构数据库。

---

# 4. DomainDefinition最终职责检查

## 3号位

负责：

- DomainDefinition治理；
- 新增/修改；
- 启用/停用；
- ScopeType校验；
- ProductFamily / Factory合法性；
- 变更审计；
- 当前有效Domain集合；
- Run启动冻结ExpectedDomainKeysJson；
- Domain业务范围权限。

## 2号位

负责：

- Order / Demand / Supply / Material上下文真实归域；
- 形成真实DomainKey；
- Domain内Pegging；
- LogicalProductionDemand；
- 逐Domain调用Solver；
- 计划结果持久化；
- 跨Domain Quantity-Time；
- 共享Resource占用传递；
- 失败传播；
- 实际Domain执行顺序。

## 1号位

只负责：

- 单Domain Solver；
- 消费DomainKey；
- 消费ExternalDomainResourceBlocks。

不解释：

- FAMILY；
- FACTORY_FAMILY；
- DomainDefinition。

## 4号位

只负责最小维护页面。

Domain治理走：

```text
4 → 3
```

普通计划结果继续：

```text
4 → 5
```

## 5号位

保持复杂业务事实、ODS、防腐层和非治理业务接口接入。

不增加Domain调度职责。

---

# 5. Domain与ProductFamily关系复审

最终统一：

```text
Domain ≠ ProductFamily
```

V1允许：

## FAMILY

```text
Domain
= 一个ProductFamily整体
```

但这是Scope定义方式，不代表：

```text
DomainKey = ProductFamily.Code
```

## FACTORY_FAMILY

```text
Domain
= Factory + ProductFamily
```

因此：

- 同ProductFamily可以属于同一个FAMILY Domain；
- 同ProductFamily也可以因Factory不同属于多个FACTORY_FAMILY Domain；
- 跨Factory不自动等于跨Domain；
- 同ProductFamily不自动等于同Domain。

---

# 6. ScheduleRun / PlanVersion检查

结论：通过。

继续保持：

```text
一个ScheduleRun
        ↓
多个Domain
        ↓
每个Domain独立PlanVersion
```

Run开始：

冻结：

`ExpectedDomainKeysJson`

当前Run内Domain配置不漂移。

不得：

- 每Domain创建独立ScheduleRun；
- 一个PlanVersion承载全部Domain；
- 运行中修改ExpectedDomainKeysJson。

---

# 7. FULL执行方式检查

当前V1最终口径：

```text
Domain依赖
↓
拓扑准备
↓
稳定排序
↓
串行逐Domain执行
```

当前不做并行。

未来只有真实压测证明必要，且：

```text
无未完成依赖
AND
无共享Resource
```

才允许重新打开并行实施。

不恢复旧：

- DomainSchedulingJob并发；
- DomainLayerCoordinatorJob并发；
- 多Hangfire Domain Job架构。

---

# 8. 跨Domain Quantity-Time检查

结论：通过。

保持切片：

```text
40 @ 15日
60 @ 17日
```

不能变成：

```text
100 @ 单一时间
```

该对象属于：

> 当前ScheduleRun内存运行上下文。

不建设持久化VirtualInventoryBalance作为第二供给真相。

---

# 9. 共享资源检查

结论：通过。

FULL：

```text
Domain A成功
↓
真实共享Resource占用
↓
当前Run共享资源上下文
↓
Domain B不可用时间块
```

Candidate：

其它Domain ACTIVE共享资源占用：

`ExternalDomainResourceBlocks`

且不可移动。

不建设：

- 跨Domain配额；
- 资源借用；
- Domain Resource Pool；
- 虚拟子日历平台。

---

# 10. 失败传播检查

结论：通过。

上游失败：

- 当前失败Domain新PlanVersion失败；
- 依赖下游本次不发布新ACTIVE；
- 下游保持旧ACTIVE；
- 无关Domain继续；
- Run最终允许PARTIAL_SUCCESS。

FAILED恢复：

> 新建ScheduleRun。

不允许FAILED → RUNNING。

---

# 11. Candidate检查

结论：通过。

继续：

```text
一个Candidate
=
一个Domain
+
一个Base ACTIVE PlanVersion
```

V1没有MultiDomain Candidate。

跨Domain CTP如果发生：

> 多个串行单Domain WHATIF / Candidate链。

不是一个联合多Domain Candidate。

---

# 12. 数据库最终修订结论

正式升版：

- DDL：v5.1.3 → v5.1.4
- 字段说明：v5.1.3 → v5.1.4

v5.1.4只新增：

1. 最小DomainDefinition；
2. DomainKey统一语义；
3. Domain_Dependency真实DomainKey语义。

复用：

- ScheduleRun.ExpectedDomainKeysJson；
- PlanVersion.DomainKey；
- Order.DomainKey。

不新增：

- DomainRuntime；
- DomainContext表；
- VirtualInventoryBalance；
- MultiDomain Candidate表；
- Domain资源配置平台。

---

# 13. 3号位实施包装配说明

3号位上一轮v1.1完整母版已经包含：

- Rule / Parameter / Strategy；
- ScheduleRun / Candidate治理；
- 完整Authentication / RBAC；
- Factory / ProductFamily / Department / Domain Business Scope；
- APS_Auth Owner；
- 普通计划结果查询迁出3号位。

Domain专项v1.2只增加：

- DomainDefinition治理；
- Domain集合冻结；
- Domain_Dependency治理；
- Candidate单Domain校验；
- Domain维护权限。

因此当前有效方式为：

> **3号位v1.1完整冻结母版 + v1.2 Domain专项增量补充共同生效。**

在形成单一合并文件之前，不得用v1.2增量文件单独替掉v1.1完整母版。

这是文档装配问题，不是业务未决问题。

---

# 14. 需要废止/退出的过程文件

以下类型文件不得进入最终冻结主目录：

1. 只有Domain摘要、未继承母版全文的“短版升版文档”；
2. 《数据库表结构与字段说明 Domain专项修订说明》；
3. 《数据库表结构与字段说明 Domain专项正式修订结论》；
4. 其它仅用于本轮判断和过程沟通的中间稿。

最终目录只应保留：

- 完整继承修正版；
- 正式v5.1.4数据库文档；
- 必要专项业务裁决；
- 实施包正式有效版本；
- 总体验收包。

---

# 15. 当前总状态

| 领域 | 状态 |
|---|---|
| Domain业务定义 | 通过 |
| Domain Scope | 通过 |
| Domain职责边界 | 通过 |
| ScheduleRun/PlanVersion | 通过 |
| FULL串行逐Domain | 通过 |
| Candidate单Domain | 通过 |
| Quantity-Time | 通过 |
| 共享Resource | 通过 |
| 失败传播 | 通过 |
| 权限边界 | 通过 |
| 前端4→3/4→5 | 通过 |
| 过度设计检查 | 通过 |
| 数据库DomainDefinition | 已补齐 |
| Domain_Dependency旧ProductFamily语义 | 已修正 |
| 完整母版继承 | 已修正 |
| 3号位单文件合并 | 装配项；当前母版+增量共同有效 |

---

# 16. 最终裁决

> **本轮Domain专项没有重新打开APS V1原有Pegging、有限产能、供给、Demand Protection、Manual ETA、PI Position、MES或权限边界。真正需要修正的是Domain定义和分域运行边界，以及与之直接相关的数据库最小配置承载。当前业务闭环已经一致；后续不得因为代码实现便利重新把Domain退化为ProductFamily，也不得因为性能担忧提前恢复多Domain并发平台。**

---

# 17. 下一步冻结动作

完成本报告后，下一步只做文档治理收尾：

1. 将“完整继承修正版”列为正式候选冻结版本；
2. 将DDL/字段说明v5.1.4列为正式候选冻结版本；
3. 更新《当前有效冻结基线索引》；
4. 更新《最终文档目录与引用规则》；
5. 更新《冻结基线变更记录》；
6. 明确废止本轮摘要短版和数据库过程说明稿。

上述属于文档索引治理，不再重新讨论Domain业务。
