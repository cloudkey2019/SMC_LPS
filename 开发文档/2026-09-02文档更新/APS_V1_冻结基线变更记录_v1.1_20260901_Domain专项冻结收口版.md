# APS V1 冻结基线变更记录 v1.1

**版本**：v1.1  
**日期**：2026-09-01（北京时间）  
**用途**：记录APS V1从业务冻结到职责、接口、权限和实施包调整过程中的正式变更，确保所有角色理解“新增、替代、保留、不变”的边界。

---

# 1. 变更原则

本记录只登记已经正式确认的冻结变化。

原则：

- 不记录讨论中的方案；
- 不记录未批准设计；
- 不因为代码实现变化自动形成业务变更；
- 不因为职责调整重新打开业务冻结。

所有变更必须说明：

1. 变更原因；
2. 影响范围；
3. 是否影响业务；
4. 是否影响接口；
5. 是否影响数据库；
6. 是否影响代码。

---

# 2. 当前基线起点

APS V1原冻结体系：

- 业务冻结：APS V1最终全部流程与业务基线；
- 专项冻结：Pegging、有限产能；
- 技术冻结：数据库、字段、数据架构、接口；
- 各号位实施包；
- 代码审核基线。

上述业务冻结继续有效。

---

# 3. 2026-09-01职责与接口调整

## 3.1 变更内容

新增：

- 3号位负责认证权限体系；
- 3号位负责Business Scope；
- 3号位负责Rule/Parameter/Strategy治理；
- 5号位承担非治理类APS业务接口接入；
- 4号位直接访问方向调整。

---

## 3.2 不变内容

以下业务Owner不变：

| 内容 | Owner |
|---|---|
| Pegging | 2号位 |
| Allocation | 2号位 |
| Demand Protection业务规则 | 2号位 |
| Effective ETA | 2号位 |
| AvailableTime | 2号位 |
| ScheduleRun | 2号位 |
| PlanVersion | 2号位 |
| FinalTask | 2号位 |
| 有限产能Solver | 1号位 |

---

# 4. 接口方向变更

## 4.1 原方向

历史上存在：

```
4号位 → 2号位
4号位 → 3号位
4号位 → 5号位
```

---

## 4.2 当前正式方向

治理类：

```
4号位
 ↓
3号位
 ↓
2号位运行服务
 ↓
1号位Solver（需要时）
```

非治理类：

```
4号位
 ↓
5号位
 ↓
2号位Query/Application Service
```

禁止：

```
4号位 → 1号位
4号位 → 2号位HTTP/API
```

---

# 5. Auth与权限体系变更

## 5.1 原状态

历史实现中，部分Auth相关能力位于2号位。

---

## 5.2 当前状态

完整移交3号位：

包括：

- Authentication；
- Login；
- Refresh；
- User；
- Role；
- Permission；
- Business Scope；
- Auth Audit；
- APS_Auth DDL。

---

## 5.3 保留原则

迁移：

不是重写。

允许：

- 代码复用；
- Namespace调整；
- 配置调整。

禁止：

- 形成两套Auth；
- 形成两套RBAC；
- 形成两套Business Scope。

---

# 6. 数据库Owner变更

## 6.1 新原则

废止：

> 所有APS数据库结构统一由2号位维护。

采用：

> 谁拥有业务/技术事实，谁拥有对应数据库结构。

---

## 6.2 Owner调整

| 数据 | Owner |
|---|---|
| APS_Auth | 3号位 |
| Rule/Parameter/Strategy治理表 | 3号位 |
| ODS复杂事实 | 5号位 |
| PI Position事实 | 5号位 |
| Manual ETA事实 | 5号位 |
| Pegging/Allocation | 2号位 |
| PlanVersion/ScheduleRun | 2号位 |
| FinalTask | 2号位 |

---

# 7. Manual ETA责任调整记录

## 保留：

Manual ETA属于APS内部事实。

## 不变：

5号位：

- 维护Manual ETA事实；
- 提供ERP ETA、ReleaseDate等事实。

2号位：

- 计算Effective ETA；
- 计算AvailableTime；
- 用于Pegging。

链路：

```
Manual ETA
>
ERP ETA
>
ReleaseDate + DefaultPurchaseLT
```

---

# 8. 历史文档替代关系

| 原版本 | 当前版本 |
|---|---|
| 数据架构v1.37 | 数据架构v1.38 |
| 集成接口v1.27 | 集成接口v1.28 |
| 2号位实施包v1.1 | 2号位实施包v1.2 |
| 权限设计v1.0 | 权限设计v1.1 |
| 权限实施v1.0 | 权限实施v1.1 |

旧版本：

- 保留；
- 用于追溯；
- 不作为开发依据。

---

# 9. 明确未发生的变更

2026-09-01没有改变：

- Pegging算法；
- Demand/Supply数量闭环；
- PI Position业务规则；
- 有限产能算法；
- Candidate业务语义；
- ScheduleRun状态语义；
- PlanVersion语义；
- Planning-only Purchase Placeholder规则；
- MES五态规则。

---

# 10. 后续变更要求

任何未来修改必须：

1. 更新本变更记录；
2. 更新冻结基线索引；
3. 明确影响范围；
4. 更新相关实施包；
5. 更新测试验收。

---

# 11. 当前最终解释

> 2026-09-01是APS V1架构治理收口节点。此次变化解决的是职责、接口、权限和数据库Owner问题，而不是重新设计APS业务。2号位继续保持核心排程运行Owner；3号位负责治理、安全和权限；5号位负责复杂事实以及非治理业务接口接入；4号位按照新的访问边界调用；1号位Solver边界保持不变。


---

# 10. 2026-09-01 Domain定义与分域计算专项冻结

## 10.1 变更原因

历史技术设计中，Domain存在以下隐含旧假设：

- Domain近似等于ProductFamily；
- Domain_Dependency节点使用ProductFamily.Code；
- 存在多Domain Job并发历史设计；
- Domain缺少统一稳定定义和正式治理承载。

这些假设不足以支撑已冻结的：

- 每Domain独立PlanVersion；
- 白天Candidate单Domain；
- 跨Domain失败传播；
- 共享Resource现实占用；
- 工厂+产品族分域。

因此本轮明确重新打开并重新冻结的对象仅为：

> **Domain定义、分域计算与运行边界。**

---

## 10.2 业务变更

新增正式定义：

> 排程域（Domain）是APS用于隔离排程计算、计划版本（PlanVersion）、候选计划（Candidate）、失败传播和发布边界的稳定业务范围。

V1只支持：

- 产品族域（FAMILY）；
- 工厂+产品族域（FACTORY_FAMILY）。

明确：

`Domain ≠ ProductFamily`

但FAMILY可以用单一ProductFamily定义一个Domain范围。

---

## 10.3 运行方式变更

当前V1正式FULL方式明确为：

```text
一个ScheduleRun
↓
多个Domain
↓
按真实Domain依赖排序
↓
串行逐Domain执行
↓
每Domain独立PlanVersion
```

历史多Hangfire Domain Job并发方案退出正式V1主链。

未来并行不是本轮V1开发要求。

---

## 10.4 跨Domain供给与时间

继续使用：

```text
Material + Quantity + AvailableTime
```

并保留数量—时间切片。

不建设：

- VirtualInventoryBalance；
- 第二套库存事实；
- 跨Domain供给平台。

---

## 10.5 共享资源

新增明确运行闭环：

前序Domain成功后，其真实共享Resource占用必须作为后续Domain不可用时间块。

白天Candidate中：

其它Domain ACTIVE占用作为外部排程域资源阻挡块（ExternalDomainResourceBlocks），不可移动。

不建设：

- Domain资源配额；
- 资源借用；
- 跨Domain资源交易/配额平台。

---

## 10.6 失败传播

上游Domain失败：

- 依赖下游不发布本次新ACTIVE；
- 保留上一ACTIVE；
- 无关Domain继续；
- ScheduleRun允许PARTIAL_SUCCESS。

该变更没有恢复全域ALL_OR_NOTHING。

---

# 11. Domain职责变更记录

| 内容 | Owner |
|---|---|
| DomainDefinition治理 | 3号位 |
| 当前有效Domain集合 | 3号位 |
| ExpectedDomainKeysJson冻结 | 3号位治理、2号位消费 |
| Order/Demand/Supply真实归域 | 2号位 |
| Domain内Pegging | 2号位 |
| Domain单元Solver | 1号位消费上下文 |
| Domain_Dependency治理 | 3号位 |
| Domain_Dependency运行消费 | 2号位 |
| FULL共享Resource占用传递 | 2号位，1号位消费Block |
| Candidate单Domain治理 | 3号位 |
| Candidate实际业务执行 | 2号位 |
| Domain维护页面 | 4号位 |

5号位不因本轮Domain专项新增调度Owner职责。

---

# 12. 数据库变更记录

## 12.1 DDL升版

`v5.1.3 → v5.1.4`

正式增加：

- 最小排程域定义（DomainDefinition）持久化承载。

正式修正：

- DomainKey不再解释为ProductFamily.Code；
- Domain_Dependency上下游节点改为真实DomainKey。

保持复用：

- ScheduleRun.ExpectedDomainKeysJson；
- PlanVersion.DomainKey；
- Order.DomainKey。

---

## 12.2 明确不增加

不增加：

- DomainRuntime；
- DomainExecutionState；
- DomainContext持久化表；
- MultiDomain Candidate表；
- VirtualInventoryBalance；
- Domain资源配额表。

---

# 13. 文档装配修正记录

本轮初次生成Domain升版文档时，部分文件只包含Domain专项摘要，没有完整继承母版正文。

经总交叉复审确认：

> 这属于文档装配缺陷，不属于业务裁决变化。

已生成“完整继承修正版”。

规则：

- 完整继承修正版进入正式冻结目录；
- 同版本摘要短版降级为过程稿；
- 不允许摘要版替换完整母版。

---

# 14. 3号位实施包装配记录

3号位现阶段使用：

> v1.1完整母版 + v1.2 Domain专项增量补充

共同生效。

原因：

v1.1已经完整包含2026-09-01认证权限、业务范围和接口职责调整；

v1.2只负责追加DomainDefinition及Domain治理专项。

不得用v1.2增量稿单独覆盖v1.1完整母版。

---

# 15. 本轮业务未改变清单

Domain专项没有改变：

- Demand/Supply定义；
- Pegging排序和Allocation；
- PI Position；
- Demand Protection；
- Manual ETA；
- Effective ETA；
- AvailableTime；
- Planning-only Purchase Placeholder；
- 90天单一正式有限产能Solver；
- PlannedProcessQty；
- FinalTask；
- MES五态；
- 4→3 / 4→5前端接口冻结；
- 5号位复杂事实职责。

---

# 16. Domain专项当前冻结结果

当前正式版本：

- 最高业务基线：v1.2；
- Pegging专项：v1.2；
- 有限产能专项：v1.2；
- 核心全流程走查：V3.18；
- 数据架构：v1.39；
- 数据库DDL：v5.1.4；
- 字段说明：v5.1.4；
- 2号位实施包：v1.3；
- 3号位：v1.1母版 + v1.2 Domain增量；
- 4号位实施包：v1.2；
- 0号位验收包：v1.1；
- 冻结索引：v2.2；
- 最终文档目录与引用规则：v1.2；
- 本变更记录：v1.1。

---

# 17. 本轮最终裁决

> Domain专项已经完成重新定义、技术对齐、数据库承载、实施包对齐和验收收口。后续不得再以历史代码、ProductFamily旧注释、多Domain Job旧架构或摘要过程稿作为依据重新打开Domain业务；若未来确需Domain并行、MultiDomain Candidate、资源配额或更复杂Scope表达式，应作为V2/V3或单独业务裁决重新讨论。
