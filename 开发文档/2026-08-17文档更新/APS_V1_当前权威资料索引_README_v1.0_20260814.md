# APS V1 当前权威资料索引 README（项目入口）

**版本**：v1.0  
**日期**：2026-08-14  
**用途**：作为 APS Project Sources 的统一入口文件。新对话、新AI、新开发成员开始工作时，应先阅读本文件，再按任务读取对应冻结文档。  
**状态**：当前有效

---

# 一、最高原则

APS V1 已完成业务冻结和技术冻结。

后续任何开发、代码审查、接口调整、字段修正、测试和上线验收，都必须遵守：

> **已冻结业务优先于技术文档；技术冻结优先于实施包；实施包优先于当前代码。**

严禁因为：

- 当前代码已经这样写；
- 旧DDL/旧字段仍存在；
- 另一个AI提出“更优方案”；
- 想让架构更标准、更优雅；
- 为V2提前扩展；
- 实现起来更方便；

而静默修改已经冻结的APS V1业务。

只有发现真实新业务事实或已冻结规则无法共同成立时，才允许显式提出：

> **请求重新打开冻结决策 F-xxx**

并由0号位重新裁决。

---

# 二、权威层级

## 一级：最终业务冻结基线

最高业务权威。

1. `APS_V1_最终全部流程与业务基线_v1.0_20260812.md`

作用：

- 定义APS V1全业务流程；
- 定义数量、时间、版本、异常、职责边界；
- 定义V1做什么、不做什么；
- 所有技术实现必须向本文件对齐。

---

## 二级：两份专项业务冻结说明

2. `APS_Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版.md`
3. `APS_有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版.md`

作用：

- 对一级业务冻结进行专项解释；
- 提供Pegging、PI Position、Supply、Allocation、有限产能、Candidate等详细业务说明；
- 不得独立推翻一级业务冻结。

---

## 三级：六份权威技术冻结文档

4. `APS_核心排产全流程走查_V3.17_冻结对齐版.md`
5. `APS_各类基础数据分层承接与演变总表_v3.32_冻结对齐版.md`
6. `APS_数据架构与防腐层设计方案_v1.36_冻结对齐版.md`
7. `APS_集成接口设计_v1.26_冻结对齐版.md`
8. `APS_数据库字段说明文档_v5.1.2_冻结对齐版.md`
9. `APS_数据库表结构设计_v5.1.2_冻结对齐版.sql`

作用：

- 把冻结业务落实为流程、数据职责、接口、字段和DDL；
- 六份文档之间已经完成总交叉审查；
- 不允许单份文档重新定义业务。

---

## 四级：关键接口冻结

10. `APS_V1_关键接口冻结_1-2_2-5_2-3_v1.0_20260814.md`

作用：

冻结三个最关键跨号位边界：

- 1↔2：有限产能Solver；
- 2↔5：PI Position / Complex Facts；
- 2↔3：规则参数 / Strategy Snapshot / 生命周期。

接口调整应优先做DTO最小增量，不优先新增表或改变职责。

---

## 五级：0～5号位实施包

11. `APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md`
12. `APS_V1_2号位增量开发实施包_v1.1_执行对齐版_20260814.md`
13. `APS_V1_3号位规则参数与运行生命周期开发实施包_v1.0_20260814.md`
14. `APS_V1_4号位页面与业务操作开发实施包_v1.0_20260814.md`
15. `APS_V1_5号位复杂业务事实与ODS开发实施包_v1.0_20260814.md`
16. `APS_V1_0号位总体项目验收包_v1.0_20260814.md`

说明：

- 2号位已有代码，因此采用“增量整改包”，不是新项目重写方案；
- 1/3/4/5号位当前按冻结职责从零组织；
- 0号位文件用于总体验收，不是开发设计说明。

---

## 六级：冻结证明与审查记录

建议保留：

17. `APS_V1_业务与技术冻结清单_SHA256_20260812.md`
18. `APS_六份权威技术文档_总交叉审查与技术冻结报告_20260812.md`

作用：

- 证明冻结文件版本；
- 防止文件被静默修改；
- 记录六文档总交叉审查结论。

---

# 三、当前0～5号位职责

## 0号位

负责：

- 业务裁决；
- 产品范围；
- PMC/业务验收；
- 是否重新打开冻结决策；
- 最终上线判断。

不负责：

- 开发实现细节。

---

## 1号位

负责：

- 唯一有限产能Solver；
- FinalTask；
- Resource选择；
- PlannedStart/End；
- Forward / Backward / Mixed；
- Split / Merge；
- Setup；
- Stage overlap；
- Candidate局部传播；
- Task物理依赖；
- ScheduleExplanationFacts。

不负责：

- Pegging；
- Supply分配；
- 数据库写入。

---

## 2号位

负责：

- APS主流程；
- Demand/Supply；
- Pegging；
- DemandBalance / SupplyBalance；
- Allocation；
- Lock执行；
- PI选择与Position消费；
- LogicalProductionDemand；
- DomainSolveRequest组装；
- Solver调用；
- FinalTask等结果统一持久化；
- Candidate；
- ScheduleRun/PlanVersion运行协作。

最高实现原则：

> **保留现有代码外壳，最小整改。**

---

## 3号位

负责：

- RuleSet / ParameterSet / StrategyProfile；
- 版本治理；
- FrozenStrategySnapshot；
- Demand Priority Segment配置；
- Solver策略参数；
- ExpectedDomainKeysJson；
- 运行生命周期元数据；
- Candidate最小确认边界。

不负责：

- 逐笔Pegging；
- Task；
- Supply选择。

---

## 4号位

负责：

- APS页面；
- CTP；
- Candidate对比；
- 甘特图；
- Explanation；
- PI Position展示；
- 规则参数维护UI；
- Run/Version状态；
- MES下发资格展示。

禁止：

> 页面直接修改运行库。

---

## 5号位

负责：

- BOM / Workset；
- PI Position；
- MES复杂进度事实；
- 两类跨厂事实；
- PO/VMI/Arrived/Transit等Timed Supply；
- ERPProperty；
- ODS防腐；
- Business Fact Issue。

不负责：

- 最终Pegging；
- Task；
- Solver。

---

# 四、当前主流程必须保持

正式APS V1主链：

```text
源系统/ODS事实
→ Demand / Supply
→ Pegging
→ Allocation
→ LogicalProductionDemand
→ DomainSolveRequest
→ 1号位有限产能Solver
→ FinalTask
→ 2号位统一事务持久化
→ PlanVersion发布
→ UI / MES
```

严禁恢复旧主链：

```text
Pegging
→ 2号位提前INSERT Task
→ Solver只补时间和Resource
```

关键区别：

> **FinalTask由1号位算法生成；数据库INSERT由2号位执行。**

---

# 五、关键业务冻结速查

## 5.1 SALES_ORDER

ERP进入APS前已经完成成品库存扣减。

APS：

> 不再二次扣成品库存。

---

## 5.2 PI RemainingQty

ERP定义：

> 尚未最终进入目标M库的全部剩余数量。

MES WIP、XC、Transit只定位位置，不增加总量。

---

## 5.3 PI Position

- 先选PI，再消费该PI Position；
- Position互斥；
- 合计必须等于ERP RemainingQty；
- 无法定位进入UNLOCATED；
- PI不能消费自己。

---

## 5.4 Allocation

一次成功Allocation必须：

- DemandBalance扣减；
- SupplyBalance扣减；
- Eligibility；
- Strict Binding；
- Demand Protection；
- Execution不可逆事实；
- AllocationSequence；
- AllocationRecord。

---

## 5.5 FinalTask

- 2号位不提前生成；
- 1号位生成；
- 允许多Demand合并Task；
- 必须保留AllocationTaskShare多对多。

---

## 5.6 90天有限产能

V1：

> 一套90天有限产能Solver。

不建设第二套远期ROUGH_CUT Solver。

---

## 5.7 Candidate

- 严格单Domain；
- 普通未锁Allocation可重新竞争；
- 其它Domain ACTIVE共享资源占用是不可移动阻挡块；
- MaxImpactedOrders只告警，不截断正确性；
- WHATIF不自动激活。

---

## 5.8 跨Domain

CTP采用：

> 多个单Domain WHATIF串行。

Quantity-Time必须保留多段，例如：

- 40件15日；
- 60件17日。

不能压平为100件17日。

---

## 5.9 跨厂

两类：

- STAGE_HANDOFF；
- INTER_FACTORY_ORDER。

永不建设有限物流Task。

物流只形成：

> 上游完成时间 + Transport/Inspection/Transfer LT = 下游AvailableTime。

---

## 5.10 采购

真实V1 Supply包括：

- Inventory；
- Arrived-not-inbound；
- PO；
- VMI；
- Transit等。

没有正式PO/VMI承诺时：

> 可使用Planning-only Purchase Placeholder。

但必须：

- 仅内存；
- ESTIMATED；
- NOT_COMMITTED；
- 不生成PO；
- 不生成Task；
- 不下ERP；
- CTP不得当作确定承诺。

---

## 5.11 MES

Task正式状态：

- PLANNED
- RELEASED
- IN_PROGRESS
- COMPLETED
- CANCELLED

不增加：

- PAUSED
- SUSPENDED
- SCHEDULED
- WAITING
- RUNNING

Candidate、UNLOCATED、无正式PI规划Task、仍依赖未承诺采购占位的Task不得下MES。

---

# 六、明确不做的V1内容

任何AI或开发人员再次建议以下内容，应默认判定为“超出V1冻结范围”，除非0号位显式重新打开：

- MultiDomain Candidate；
- 跨Domain共享资源配额/借用平台；
- 有限物流Task；
- 第二套远期ROUGH_CUT Solver；
- 通用DSL规则平台；
- 动态插件市场；
- Persisted Impact Graph；
- Solver完整Trace数据库；
- 通用因果图平台；
- 全局数学最优；
- 无限拆批组合；
- VirtualInventoryBalance持久平台；
- FrozenZoneSnapshot平台；
- 普通插单强制Scenario；
- 重型多级审批；
- 为架构整洁增加大量表/FK。

---

# 七、新对话 / 新AI启动顺序

进入APS Project的新对话后，建议按以下顺序工作：

## 第一步

先读本README。

## 第二步

根据任务读取对应上位冻结文件。

例如：

### 审2号位代码

优先读取：

1. 最终业务冻结基线；
2. Pegging专项；
3. 核心排产全流程走查；
4. 关键接口冻结；
5. 2号位增量实施包。

不要先读旧代码README决定业务。

### 审1号位Solver

优先读取：

1. 有限产能专项；
2. 核心排产全流程走查；
3. 关键接口冻结；
4. 1号位实施包。

### 审5号位

优先读取：

1. Pegging专项；
2. 数据架构/防腐层；
3. 关键接口冻结；
4. 5号位实施包。

---

# 八、旧资料处理规则

以下文件即使保留在本地历史目录，也不应作为Project Sources当前权威资料：

- 六份技术文档旧版本；
- 2号位实施包v1.0；
- 中间差异矩阵；
- 单份技术文档逐次审查报告；
- 2号位AI临时审核报告；
- 临时Bug分析；
- 已被后续冻结结论替代的说明稿。

如必须保留，应明确放入：

> `Archive / Historical`

并在文件名前加：

> `HISTORICAL_` 或 `DEPRECATED_`

避免新AI误读。

---

# 九、文件冲突处理

如果发现文件之间有不同表述：

1. 先检查是否读到了旧版本；
2. 按本文第二部分权威层级判断；
3. 下级文件与上级冲突时，修改下级理解；
4. 不允许静默把上级冻结业务改掉；
5. 若确实发现上级冻结规则无法共同成立，才提出重新打开冻结决策。

---

# 十、代码审查标准

后续代码审查统一只看：

1. 是否符合业务冻结；
2. 是否符合技术冻结；
3. 是否跨号位职责；
4. 数量是否闭合；
5. 时间是否有限产能真实；
6. 版本/事务是否闭合；
7. 是否恢复旧主链；
8. 是否过度设计；
9. 是否保护2号位现有代码外壳；
10. 是否通过对应实施包验收用例。

---

# 十一、当前项目阶段

截至2026-08-14：

- 三份业务文档：已冻结；
- 六份技术文档：已冻结；
- 六文档总交叉审查：已通过；
- 1↔2、2↔5、2↔3：接口冻结；
- 0～5号位实施包：已完成；
- 2号位：已进入代码增量整改；
- 后续阶段：各号位开发提交 → 按冻结基线审代码 → 0号位总体验收。

因此：

> **当前不应继续扩大架构设计。**

后续重点是：

> **实现、联调、验收。**

---

# 十二、一句话入口规则

> **任何新对话、新AI、新开发成员进入APS V1时，先读本README，再按权威层级读取当前冻结文件；不要用旧代码、旧DDL、旧AI报告反向定义业务，也不要重新打开已经冻结的设计。**
