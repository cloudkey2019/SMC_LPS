# APS V1 Domain定义与分域计算本轮文档修改清单

**版本**：v1.0  
**日期**：2026-09-01  
**性质**：下一轮冻结文档修改任务清单  
**使用方式**：当前一轮文档修改完成并重新冻结后，再按本文启动下一轮Domain相关文档调整  
**来源裁决**：《APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901》

---

# 一、本轮处理原则

当前正在执行的是前一轮冻结文档修改，因此本次Domain相关调整**不与前一轮并行混改**。

建议顺序：

1. 先完成当前正在进行的上一轮文档修改；
2. 对上一轮修改后的文档做一次完整交叉审核；
3. 确认上一轮业务/技术基线重新冻结；
4. 再以本文件为任务清单，启动Domain定义与分域计算专项修改；
5. 修改完成后再次更新冻结索引、变更记录和最终文档目录。

本文件只记录下一轮要改什么，不直接替代现有冻结基线。

---

# 二、本次Domain裁决的核心变化

本次真正新增/补齐的业务口径包括：

1. Domain正式定义为：
   - APS独立排程边界；
   - 独立PlanVersion边界；
   - Candidate边界；
   - 失败传播与发布边界。

2. Domain不再默认等同于ProductFamily。

3. V1 Domain范围最多允许两种：
   - `FAMILY`
   - `FACTORY_FAMILY`

4. Domain由系统管理员维护。

5. DomainDefinition属于APS基础配置/主数据级配置：
   - 不属于RuleSet；
   - 不属于ParameterSet；
   - 不属于ODS源事实。

6. 3号位负责DomainDefinition治理与Run引用冻结。

7. 2号位负责：
   - Demand/Order实际归域；
   - 真实DomainKey；
   - 逐Domain执行；
   - 跨Domain Quantity-Time；
   - FULL共享资源互斥；
   - 失败传播。

8. 1号位只消费DomainKey和单Domain求解上下文，不重新定义Domain。

9. 4号位提供最小Domain配置维护页面。

10. 夜间FULL：
    - 一个ScheduleRun；
    - 多个Domain；
    - 每个Domain独立PlanVersion；
    - 当前V1采用串行Domain执行。

11. 不恢复旧的多Hangfire Domain Job并发架构。

12. 当前实现应把单Domain执行封装为稳定边界，为未来并行保留能力。

13. 未来仅对：
    - 无依赖；
    - 无共享Resource；

    的Ready Domain做受控并行。

14. `Domain_Dependency`必须以真实`DomainKey`为节点，不再默认使用`ProductFamily.Code`。

15. 15分钟目标通过真实分域压测验证，不因理论担忧提前引入并行复杂度。

---

# 三、必须修改的最高业务基线

## 3.1 《APS_V1_最终全部流程与业务基线》

### 修改级别
**必须修改，且必须升版。**

建议：

`v1.1 → v1.2`

### 需要新增/修订的内容

#### 1. Domain正式定义

补充：

> Domain是APS独立排程、PlanVersion、Candidate、失败传播与发布边界。

明确：

- Domain ≠ ProductFamily；
- Domain ≠ Factory；
- Domain ≠ ProductionDepartment；
- Domain ≠ Stage。

#### 2. Domain Scope

冻结V1仅支持：

- `FAMILY`
- `FACTORY_FAMILY`

不得支持任意维度动态拼接。

#### 3. Domain维护

补充：

- 系统管理员维护；
- DomainKey稳定唯一；
- Domain定义在Run开始前必须稳定。

#### 4. 号位职责

补充：

- 3号位：治理DomainDefinition及运行引用；
- 2号位：执行Order/Demand归域和分域计算；
- 1号位：只消费DomainKey；
- 4号位：提供最小维护页面。

#### 5. 夜间FULL

修订成：

```text
1 ScheduleRun
→ 多Domain
→ 每Domain独立PlanVersion
→ 按Domain_Dependency拓扑
→ 当前串行逐Domain执行
```

#### 6. 未来并行

补充：

仅在真实压测需要时，对：

```text
无依赖
AND
无共享Resource
```

的Domain并行。

#### 7. Domain_Dependency

明确：

上下游节点必须使用真实`DomainKey`。

---

# 四、必须修改的权威技术走查

## 4.1 《APS 核心排产全流程走查》

### 修改级别
**必须修改，建议升版。**

建议：

`V3.17 → V3.18`

### 需要修改的章节

#### 1. 阶段0：运行身份准备

增加：

- 加载当前有效DomainDefinition；
- 冻结本次ExpectedDomainKeysJson；
- 建立真实DomainKey。

#### 2. Domain归属

新增：

```text
Order / Demand
→ 根据DomainDefinition匹配
→ DomainKey
```

明确：

- FAMILY按ProductFamily归域；
- FACTORY_FAMILY按Factory + ProductFamily归域。

#### 3. Domain执行

把“当前Domain”概念落成真实执行链：

```text
for each Domain
    PlanVersion
    Context
    Pegging
    LogicalProductionDemand
    Solve
    Persist
    Publish
```

#### 4. 拓扑执行

明确：

- 先按Domain_Dependency拓扑；
- 同一拓扑层V1按稳定顺序串行。

#### 5. FULL共享资源

增加：

前一个已成功Domain的共享Resource占用，必须传给后续Domain作为不可占用时间块。

#### 6. 跨Domain Quantity-Time

继续强化：

- Material
- Quantity
- AvailableTime
- 保留分段

不得恢复VirtualInventoryBalance持久化。

#### 7. 失败传播

补齐：

- 上游FAILED；
- 下游BLOCKED/不发布；
- 无关Domain继续；
- ScheduleRun汇总PARTIAL_SUCCESS。

#### 8. 性能埋点

增加每Domain：

- ContextBuildMs
- PeggingMs
- SolverMs
- PersistMs
- TotalMs
- DemandCount
- FinalTaskCount

以及ScheduleRunTotalMs。

---

# 五、必须修改的2号位开发实施/整改文档

## 5.1 2号位增量整改包

### 修改级别
**必须修改，是本次代码影响最大的文档之一。**

### 需要增加的整改项

#### 1. DomainDefinition消费

2号位必须读取当前有效DomainDefinition。

#### 2. Order/Demand归域

形成真实：

```text
DomainKey
```

禁止：

- 用PlanVersionId代替DomainKey；
- 默认Domain=ProductFamily；
- 代码内另写一套Domain规则。

#### 3. 逐Domain执行

当前整版一次Solve必须整改为：

```text
每Domain独立
Pegging
→ LogicalProductionDemand
→ DomainSolveRequest
→ SolveAsync
→ Persist
```

#### 4. 每Domain独立PlanVersion

一个FULL ScheduleRun对应多个PlanVersion。

#### 5. Quantity-Time

补齐跨Domain真实Quantity-Time Slice。

#### 6. FULL共享资源

补齐前序Domain共享Resource Occupancy向后续Domain传递。

#### 7. Candidate外Domain Block

补齐：

`CandidateContext.ExternalDomainResourceBlocks`

#### 8. 失败传播

补齐：

- FAILED上游；
- BLOCKED下游；
- 无关Domain继续；
- PARTIAL_SUCCESS。

#### 9. 单Domain执行封装

建议形成稳定内部边界，例如：

`ExecuteDomainAsync(...)`

当前串行，未来可并行。

#### 10. 性能统计

按Domain记录完整耗时。

---

# 六、必须修改的3号位实施包

## 6.1 《APS V1 3号位规则、参数与运行生命周期开发实施包》

### 修改级别
**必须修改。**

建议：

`v1.0 → v1.1`

### 修改内容

#### 1. DomainDefinition定位

明确：

DomainDefinition不是：

- RuleSet
- ParameterSet
- StrategyProfile

而是APS基础配置。

#### 2. Domain治理

3号位负责：

- Domain定义维护后端；
- 校验；
- 启用/停用；
- 当前有效Domain集合查询；
- 变更审计。

#### 3. Run引用

ScheduleRun启动时：

- 冻结ExpectedDomainKeysJson；
- 只能引用当前有效Domain。

#### 4. Candidate

继续严格：

- 一个Candidate只能一个Domain。

#### 5. StrategyProfile关系

补充：

```text
DomainDefinition
= Domain是什么

StrategyProfile
= Domain使用什么策略

ExpectedDomainKeysJson
= 本次Run算哪些Domain
```

---

# 七、必须修改的数据库DDL

## 7.1 《APS_数据库表结构设计》

### 修改级别
**必须检查；如无现成等价结构则必须修改并升版。**

建议：

`v5.1.3 → v5.1.4`

### 需要处理

#### 1. DomainDefinition

先检查当前DDL是否已有职责等价对象。

如果没有，应增加最小配置结构。

建议业务字段：

- DomainKey
- DomainName
- ScopeType
- ProductFamilyId
- FactoryId
- IsActive
- SortOrder
- CreatedBy
- CreatedAt
- UpdatedBy
- UpdatedAt

注意：

本轮冻结的是业务能力，最终表名可按现有命名规范调整。

#### 2. Scope合法性

`ScopeType`仅允许：

- FAMILY
- FACTORY_FAMILY

#### 3. Domain_Dependency

修订：

`UpstreamDomainCode / DownstreamDomainCode`

语义必须指向真实DomainKey。

不得继续说明为：

`ProductFamily.Code`

#### 4. DomainKey相关字段

全面检查：

- Order.DomainKey
- PlanVersion.DomainKey
- ScheduleRun.ExpectedDomainKeysJson
- Candidate DomainKey
- Domain_Dependency

是否语义一致。

---

# 八、必须修改的数据库字段说明

## 8.1 《APS_数据库字段说明文档》

### 修改级别
**必须与DDL同步升版。**

建议：

`v5.1.3 → v5.1.4`

### 补充/修订

#### DomainDefinition字段

解释：

- DomainKey；
- DomainName；
- ScopeType；
- ProductFamilyId；
- FactoryId；
- IsActive。

#### DomainKey

统一解释为：

> APS稳定排程域标识。

不得再解释为ProductFamily同义词。

#### Domain_Dependency

明确上下游均为DomainKey。

---

# 九、必须修改的数据架构与防腐层文档

## 9.1 《APS_数据架构与防腐层设计方案》

### 修改级别
**必须修改。**

建议：

`v1.37 → v1.38`

### 需要增加

#### 1. DomainDefinition归属

明确：

- 属于APS内部基础配置；
- 不属于ODS；
- 不属于ERP/MES事实；
- 不进入RuleSet。

#### 2. 数据流

```text
DomainDefinition
↓
2号位归域
↓
Order/Demand DomainKey
↓
ScheduleRun ExpectedDomainKeys
↓
Domain PlanVersion
```

#### 3. 跨Domain运行数据

明确：

- Quantity-Time为当前Run内存传递；
- 不建立VirtualInventoryBalance持久化平台。

---

# 十、建议修改的4号位实施包

## 10.1 《APS V1 4号位页面与业务操作开发实施包》

### 修改级别
**建议修改。**

建议：

`v1.0 → v1.1`

### 增加最小Domain配置页面

仅系统管理员可维护：

- DomainKey
- DomainName
- ScopeType
- ProductFamily
- Factory
- IsActive

明确不做：

- Domain设计器；
- 任意条件表达式；
- DSL；
- 动态规则组合。

---

# 十一、建议修改的0号位验收包

## 11.1 《APS V1 0号位总体项目验收包》

### 修改级别
**建议修改。**

建议：

`v1.0 → v1.1`

### 增加验收项

1. Domain来自正式DomainDefinition；
2. DomainKey不等于技术Id；
3. FAMILY归域正确；
4. FACTORY_FAMILY归域正确；
5. 一个FULL Run多Domain独立PlanVersion；
6. 逐Domain执行真实存在；
7. Domain_Dependency使用DomainKey；
8. 上游失败下游不发布；
9. FULL共享Resource不双占；
10. Candidate严格单Domain；
11. Candidate外Domain共享资源Block生效；
12. 分域后10万Task性能有真实测量。

---

# 十二、需要小幅同步的专项业务文档

## 12.1 《APS Pegging供需承接与分层计算业务说明》

### 不需要重写Pegging算法。

只需补充：

- 当前Domain由有效DomainDefinition确定；
- Domain不等于ProductFamily；
- 跨Domain节点使用DomainKey。

如文档中存在“Product Domain = ProductFamily”的表述，应修订。

---

## 12.2 《APS 有限产能排产与滚动90天计划业务说明》

### 不改变1号位职责。

补充：

- 1号位一次只Solve一个Domain；
- Domain由上游定义；
- 1号位不解释Domain Scope；
- Domain可以是FAMILY或FACTORY_FAMILY；
- 当前FULL由2号位串行编排Domain。

---

# 十三、原则上不需要改的文档

## 13.1 《APS V1关键接口冻结：1↔2、2↔5、2↔3》

当前接口方向无需重开。

原因：

- 2↔3已有ExpectedDomainKeysJson；
- 1号位本来就是单Domain Solve；
- Candidate本来就严格单Domain。

只有实际DTO缺字段时，按现有原则追加最小字段即可。

---

## 13.2 《APS_集成接口设计》

原则上不需要因DomainDefinition修改ERP/MES外部接口。

Domain属于APS内部配置。

如果后续要登记4号位内部Domain管理API，可作为内部接口补充，但不是本次业务主链必须项。

---

# 十四、必须全局搜索并清理的旧表述

下一轮修改时必须全文搜索：

```text
Domain = ProductFamily
DomainCode = ProductFamily.Code
Product Domain = ProductFamily
跨产品族 = 跨Domain
同ProductFamily = 同Domain
```

这些表达以后均不能作为无条件规则。

---

# 十五、建议版本升级清单

| 文档 | 当前版本 | 建议版本 |
|---|---:|---:|
| 最终全部流程与业务基线 | v1.1 | v1.2 |
| 核心排产全流程走查 | V3.17 | V3.18 |
| 数据架构与防腐层 | v1.37 | v1.38 |
| 数据库DDL | v5.1.3 | v5.1.4 |
| 数据库字段说明 | v5.1.3 | v5.1.4 |
| 3号位实施包 | v1.0 | v1.1 |
| 4号位实施包 | v1.0 | v1.1 |
| 0号位验收包 | v1.0 | v1.1 |
| 集成接口设计 | v1.27 | 原则上不升版 |
| 关键接口冻结 | v1.0 | 原则上不升版 |

2号位采用当前有效增量整改包追加Domain专项整改章节或生成下一版整改包。

---

# 十六、最后必须同步的文档治理文件

Domain相关文档完成后，还必须更新：

1. 《APS_V1_当前有效冻结基线索引》
2. 《APS_V1_最终文档目录与引用规则》
3. 《APS_V1_冻结基线变更记录》

必须记录：

- 本次业务变化原因；
- 新旧版本；
- 哪些旧表述失效；
- 哪些文档保持不变；
- 当前唯一有效版本。

---

# 十七、执行顺序建议

下一轮正式修改建议按以下顺序进行：

```text
1. 最终业务基线
↓
2. Pegging / 有限产能专项（只做一致性修订）
↓
3. 核心全流程走查
↓
4. 数据架构
↓
5. DDL + 字段说明
↓
6. 2号位整改包
↓
7. 3号位实施包
↓
8. 4号位实施包
↓
9. 0号位验收包
↓
10. 全文交叉审核
↓
11. 冻结索引 / 目录 / 变更记录
```

不要先改DDL再反推业务基线。

---

# 十八、本轮修改完成判定

Domain专项文档修改完成后，必须能够从全部当前有效文档得到完全一致的答案：

1. Domain是什么？
2. Domain在哪里定义？
3. 谁能维护？
4. V1允许哪些Scope？
5. Order/Demand由谁归域？
6. DomainKey由谁生成和消费？
7. 一次FULL有几个PlanVersion？
8. Domain如何排序执行？
9. 当前是否并行？
10. 跨Domain如何传Quantity-Time？
11. 共享资源如何避免双占？
12. 上游失败下游怎么处理？
13. Candidate是否允许多Domain？
14. Domain_Dependency引用什么Key？
15. 未来并行需要满足什么条件？
16. 15分钟性能如何验证？

只要任一文档仍给出不同答案，本轮不能重新冻结。

---

# 十九、一句话任务说明

> **当前先完成上一轮冻结文档修改；下一轮再以本清单为唯一任务入口，把Domain从“文档中隐含的ProductFamily概念”正式收敛为系统管理员维护的APS排程域，并同步闭合DomainDefinition、逐Domain执行、跨域Quantity-Time、共享资源、失败传播、Candidate与性能验收。**
