# APS V1 白天业务场景 / Candidate人工调整——冻结文档修改影响矩阵及精确Delta

**版本**：v1.0  
**日期**：2026-09-15（北京时间）  
**上位依据**：《APS V1 白天业务场景与 Candidate 人工调整冻结文档修改指导 v1.1（20260914 单场景多对象 最终论证版）》  
**当前冻结入口**：《APS V1 20260914冻结基线索引 v1.2（SH旧模型残留清理版）》  
**文档性质**：正式修订前置影响分析 + 精确Delta，不替代后续正式母文档

---

# 一、当前权威基线锁定

本轮必须从2026-09-14当前有效冻结基线继续自然演进，不回退到2026-09-13已被SH P0纠错替换的版本。

## 1.1 当前正式母文档

| 类别 | 当前有效版本 |
|---|---|
| 最终业务基线 | v1.5（20260914 SH旧模型残留清理版） |
| Pegging | v1.4（20260914 SH旧模型残留清理版） |
| 有限产能 | v1.4（20260913最终治理校正版，SH专项检查后继续有效） |
| 核心走查 | V3.21（20260914 SH旧模型残留清理版） |
| 集成接口 | v1.31（20260914 SH旧模型残留清理版） |
| 数据架构 | v1.42（20260914 SH旧模型残留清理版） |
| 数据库字段 | v5.1.6（20260914 SH旧模型残留清理版完整版） |
| DDL | v5.1.6（20260914 SH旧模型残留清理版） |
| 0号位验收 | v1.3（20260914 SH旧模型残留清理版） |
| 1号位实施包 | v1.3（20260913最终治理校正版） |
| 2号位实施包 | v1.6（20260914 SH旧模型残留清理版） |
| 3号位实施包 | v1.4（20260913最终治理校正版） |
| 4号位实施包 | v1.3（20260903） |
| 5号位实施包 | v1.4（20260914 SH旧模型残留清理版） |
| 代码审核基线 | v1.1（20260914 SH旧模型残留清理版） |

## 1.2 本轮不得回退的既有冻结

以下保持不变：

- Candidate严格单Domain；
- 一个ScheduleRun通过`ExpectedDomainKeysJson`冻结预期Domain，白天Candidate恰好一个Domain；
- `BasePlanVersionId`为Candidate基准版本现有物理字段；
- 仍只有一套90天有限产能Solver；
- 仍只使用既有5种合法RunType/Purpose组合；
- WHATIF Candidate不得直接激活；
- 其它Domain当前ACTIVE共享资源占用仍作为不可移动阻挡块；
- Firm / Frozen / 已执行 / Demand Protection / Strict Binding等硬约束保持；
- Candidate普通未锁Allocation仍可释放重新竞争；
- Scenario不是Candidate/CTP V1主链硬依赖；
- 不建设MultiDomain Candidate；
- 不恢复全局PriorityScore/加权总分；
- 20260914 SH单一Supply及跨厂在途边界保持不变。

---

# 二、本轮变化边界

## 2.1 本轮真正新增/修改的业务

1. 一次`LOCAL_RESCHEDULE`只允许一个业务发起场景类型；
2. 一个场景允许多个同类型对象；
3. Candidate必须基于发起时当前唯一`ACTIVE PlanVersion`；
4. 禁止Candidate叠Candidate；
5. 甘特拖拽固定`NORMAL`；
6. 已有订单提前支持“多个订单 + 每单独立目标日期”；
7. 设备故障支持多台正式不可用设备；
8. Calendar调整支持多台已正式修改Calendar的资源；
9. 删除“其它明确对象局部调整”万能入口；
10. 变化对象只定义影响传播起点，不过滤当前真实Solver上下文；
11. CTP统一为正常业务优先规则下的真实有限物料+有限产能评估；
12. 急插影响分析只对本次明确选择订单及其派生需求链使用`EXPEDITE`；
13. WHATIF结果正式采用时，必须重新基于**当时最新ACTIVE + 最新真实事实**创建可激活LOCAL Candidate；
14. `LOCAL_RESCHEDULE`即使影响扩大到全Domain，RunType仍保持LOCAL；
15. `MANUAL_RESCHEDULE`只表示用户从一开始主动选择“整个当前Domain全部可移动计划重排”；
16. Department Business Scope继续约束允许移动集合。

## 2.2 本轮覆盖的旧口径

### 覆盖1：甘特调整RunType

旧：

```text
甘特需要调整
→ MANUAL_RESCHEDULE
```

新：

```text
甘特多Task调整
→ LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
→ PriorityMode=NORMAL
```

### 覆盖2：ScopeJson固定11字段绝对不变

旧：

```text
ScopeJson固定11字段Schema
```

新：

> `ScheduleRun.ScopeJson`物理列保持`NVARCHAR(MAX)`不变，但Candidate运行输入Schema正式升级为**版本化Schema v2**。

### 覆盖3：LOCAL泛化入口

旧文档/页面若存在：

> 其它明确对象局部调整

本轮正式删除，不再提供万能入口。

### 覆盖4：CTP“尽量不影响既有计划”的模糊理解

新正式定义：

> CTP在`NORMAL`下允许当前Domain中仍可移动、仍可重新竞争的普通需求发生真实重新竞争，但不突破硬约束，并完整返回影响。

---

# 三、本轮明确不新增

- 新RunType；
- 新Purpose；
- 新Candidate类型；
- 新Solver；
- Candidate专属Calendar；
- EXISTING_ORDER_WHATIF；
- GANTT_DRAG_RUN；
- EQUIPMENT_FAILURE_RUN；
- CALENDAR_RESCHEDULE_RUN；
- EXPEDITE_RUN；
- Scenario/Simulation平台；
- 人工长期DueDate Override平台；
- 每个业务场景独立表；
- MultiDomain Candidate；
- 全局PriorityScore；
- 浏览器实时排程器。

---

# 四、关键技术裁决：ScopeJson正式升级为Candidate运行输入Schema v2

## 4.1 物理实现裁决

正式采用：

```text
ScheduleRun.ScopeJson
NVARCHAR(MAX)
物理列不变
```

不新增：

- CandidateInput主表；
- ScenarioInput主表；
- PriorityMode物理列；
- BusinessTriggerType物理列；
- OrderTarget物理明细表；
- TaskTarget物理明细表。

原因：

1. 本轮对象均属于一次ScheduleRun的运行输入快照；
2. `ScopeJson`已经承担运行范围/输入快照职责；
3. 需要可追溯、可重放，但不存在长期独立主数据生命周期；
4. 新建平台/专表属于V1过度设计。

## 4.2 SchemaVersion

新Run必须写：

```json
{
  "SchemaVersion": 2
}
```

历史`SchemaVersion`缺失的旧记录按v1固定11字段解释，只用于历史兼容。

## 4.3 Candidate业务发起类型

新增运行输入语义：

```text
BusinessTriggerType
```

受控值：

```text
NEW_ORDER_CTP
NEW_ORDER_IMPACT
NEW_ORDER_INSERT
EXISTING_ORDER_ADVANCE
GANTT_ADJUSTMENT
EQUIPMENT_FAILURE
RESOURCE_CALENDAR_CHANGE
DOMAIN_MANUAL_RESCHEDULE
```

它只是：

> **本次ScheduleRun为什么发起。**

它不是新`RunType`，也不是ScenarioType。

## 4.4 PriorityMode

新增：

```text
PriorityMode = NORMAL / EXPEDITE
```

合法性矩阵：

| BusinessTriggerType | PriorityMode |
|---|---|
| NEW_ORDER_CTP | 固定NORMAL |
| NEW_ORDER_IMPACT | 固定EXPEDITE |
| NEW_ORDER_INSERT | NORMAL或EXPEDITE，由采用来源决定 |
| EXISTING_ORDER_ADVANCE | NORMAL / EXPEDITE，由用户明确选择 |
| GANTT_ADJUSTMENT | 固定NORMAL |
| EQUIPMENT_FAILURE | 固定NORMAL |
| RESOURCE_CALENDAR_CHANGE | 固定NORMAL |
| DOMAIN_MANUAL_RESCHEDULE | 按既有正式优先规则；不得借此创建全局PriorityScore |

## 4.5 结构化集合

### 已有订单提前

```json
"OrderTargets": [
  {
    "OrderCanonicalId": 1001,
    "ManualTargetDueDate": "2026-09-20T00:00:00"
  },
  {
    "OrderCanonicalId": 1002,
    "ManualTargetDueDate": "2026-09-22T00:00:00"
  }
]
```

`ManualTargetDueDate`：

- 只属于本次Run；
- 不修改ERP DueDate；
- 不修改Order_Canonical正式DueDate；
- NORMAL下不得偷换为Demand排序字段。

### 甘特拖拽

```json
"TaskTargets": [
  {
    "TaskId": 501,
    "TargetTime": "2026-09-15T08:00:00"
  },
  {
    "TaskId": 608,
    "TargetTime": "2026-09-15T10:30:00"
  }
]
```

TargetTime是软目标：

> Solver允许返回其它可行时间，并给Explanation。

### 故障/Calendar资源

```json
"ChangedResourceIds": [12, 18, 33]
```

资源是否真实故障、Calendar是否已正式修改，不以ScopeJson为事实真相；ScopeJson只记录本次变化起点。

## 4.6 旧11字段兼容关系

原字段继续保留其适用语义：

```text
Purpose
OrderCanonicalIds
FactoryIds
ProductFamilyIds
ResourceGroupIds
PlanHorizonStart
PlanHorizonEnd
LockedTaskIds
AllowTouchFrozenZone
AllowDelaySalesOrder
MaxImpactedOrders
```

新增Schema v2字段：

```text
SchemaVersion
BusinessTriggerType
PriorityMode
OrderTargets[]
TaskTargets[]
ChangedResourceIds[]
```

### 单一真相约束

如果同时存在：

```text
OrderCanonicalIds
OrderTargets[]
```

则：

```text
OrderCanonicalIds
=
OrderTargets[].OrderCanonicalId去重投影
```

不得出现不一致。

`BasePlanVersionId`继续使用ScheduleRun独立物理列，不重复写入ScopeJson。

`ExpectedDomainKeysJson`继续是预期Domain唯一权威字段，不写回ScopeJson。

---

# 五、Candidate Base ACTIVE与并发Candidate裁决

## 5.1 创建时

所有白天Candidate Run必须满足：

```text
BasePlanVersionId
=
当前目标Domain唯一ACTIVE PlanVersion.Id
```

禁止：

```text
BasePlanVersionId指向CANDIDATE
```

## 5.2 多Candidate并存

允许：

```text
ACTIVE A
→ Candidate B
→ Candidate C
→ Candidate D
```

B/C/D均以A为Base，可同时用于人工比较。

## 5.3 激活时防止旧Base覆盖新ACTIVE

如果：

```text
B先被确认
A → ARCHIVED
B → ACTIVE
```

则仍以A为Base的C/D：

> **不得再直接激活。**

必须重新基于当前ACTIVE B和当前最新真实事实创建新的Candidate。

这样同时满足：

- Candidate可以多套并存；
- 禁止Candidate叠Candidate；
- 激活结果永远基于当前正式计划；
- 不需要新增PlanVersion状态。

---

# 六、单Domain与多对象边界

同一Run虽然允许多个同类型对象，但所有对象必须能够归入同一个目标Domain。

例如：

```text
已有订单A/B/C提前
```

只有在本次Candidate目标Domain一致时，才允许一个Run提交。

如果对象跨多个目标Domain：

> 拆成多个单Domain Run；不能因为“同一业务场景”建设MultiDomain Candidate。

跨Domain CTP仍按现有Domain依赖拓扑串行多个单Domain WHATIF后汇总客户答案。

---

# 七、业务变化对象与Solver真实上下文必须区分

正式定义两个概念。

## 7.1 本次变化对象（Seed）

只包括用户本次明确选择的：

- 新订单；
- 已有订单；
- Task；
- 故障Resource；
- Calendar变化Resource。

它决定：

> 从哪里开始识别变化和传播影响。

## 7.2 当前真实求解上下文

Solver仍必须看到：

- 当前有效Resource Calendar；
- 当前设备可用/不可用；
- Resource Eligibility；
- Routing；
- Setup；
- 当前物料AvailableTime；
- 当前资源占用；
- Firm/Frozen；
- 已执行不可逆事实；
- Demand Protection；
- 其它Domain ACTIVE共享资源阻挡。

因此：

> **Seed不是Solver事实过滤器。**

同时也要防止反向扩大：

> 未被本次选择的其它新订单，不因为“当前事实完整”就自动进入本次业务Scenario对象集合。

---

# 八、A/B/C文档影响矩阵

| 文档 | 分类 | 当前版本 | 建议新版本 | 是否必须修改 | 核心原因 |
|---|---|---:|---:|---|---|
| 最终全部流程与业务基线 | A | v1.5 | v1.6 | 是 | 白天业务场景主定义变化 |
| Pegging业务说明 | A | v1.4 | v1.5 | 是 | NORMAL/EXPEDITE、人工目标日期排序边界 |
| 有限产能业务说明 | A | v1.4 | v1.5 | 是 | 多目标、故障、Calendar、完整真实上下文 |
| 核心排产全流程走查 | A | V3.21 | V3.22 | 是 | 白天10场景全流程需要完整走查 |
| 集成接口 | A | v1.31 | v1.32 | 是 | ScopeJson v2、结构化输入、Candidate查询/确认 |
| 数据架构与防腐层 | A | v1.42 | v1.43 | 是（小改） | 运行输入Owner与防第二真相 |
| 数据库字段说明 | A | v5.1.6 | v5.1.7 | 是 | ScopeJson从固定11字段升级为v2 |
| DDL | A | v5.1.6 | v5.1.7 | 是（轻量） | 不增列/表，仅更新ScopeJson契约注释 |
| 1号位实施包 | A | v1.3 | v1.4 | 是 | 多软目标/多故障资源/LOCAL兜底 |
| 2号位实施包 | A | v1.6 | v1.7 | 是 | 单场景多对象解析与Candidate执行 |
| 3号位实施包 | A | v1.4 | v1.5 | 是 | Run输入治理、Base ACTIVE、激活门禁 |
| 4号位实施包 | A | v1.3 | v1.4 | 是，且改动较大 | 中文场景入口、甘特LOCAL、多对象输入 |
| 5号位实施包 | A | v1.4 | v1.5 | 是（小改） | Candidate PlanVersion Gantt/DTO/Scope过滤 |
| 0号位总体项目验收包 | A | v1.3 | v1.4 | 是 | 新增完整白天场景验收 |
| 代码审核检查基线 | A | v1.1 | v1.2 | 是 | 新增Candidate场景/ScopeJson审核红线 |
| Domain裁决 | B | v1.0 | 不升版 | 检查后不改 | Candidate仍严格单Domain |
| 职责/前端/接口/权限裁决 | B | v1.0 | 原则不升版 | 需复核 | 既有Owner不变，仅4号位页面能力扩展 |
| 权限与审批方案/Auth字段/Auth DDL | B | v1.1 | 原则不升版 | 需复核 | 复用已有Business Scope与Candidate权限 |
| SH专项P0修复说明 | C | v1.0 | 不改 | 否 | 与本轮无关 |
| PI Position专项治理 | C | 现行 | 不改 | 否 | 与本轮无关 |

---

# 九、逐文档精确Delta

## 9.1 《最终全部流程与业务基线》v1.6

### 必须修改原章节

1. 白天插单与Candidate章节：
   - 从“插单Candidate”扩大为“白天业务场景与Candidate”；
   - 增加10场景矩阵；
   - 一次LOCAL一个BusinessTriggerType；
   - 同场景多对象；
   - 删除其它明确对象局部调整。

2. Candidate版本章节：
   - Base必须当前ACTIVE；
   - 禁止Base=CANDIDATE；
   - 多Candidate可并存；
   - 激活时Base已过期则拒绝采用并重新运行。

3. CTP：
   - NORMAL；
   - 正常业务优先规则下真实重新竞争；
   - WHATIF不可直接激活；
   - 正式采用重建LOCAL + INSERT_RESCHEDULE + NORMAL。

4. 急单：
   - EXPEDITE仅选中订单及派生链；
   - 不突破Firm/Frozen/Eligibility/Demand Protection等硬约束。

5. 已有订单提前：
   - 多订单；
   - 每单不同ManualTargetDueDate；
   - NORMAL不改正式DueDate。

6. 甘特：
   - LOCAL + MANUAL_ADJUSTMENT；
   - 多Task；
   - 固定NORMAL；
   - TargetTime软目标。

7. 故障/Calendar：
   - 多Resource；
   - 事实先正式生效，再作为Seed；
   - 不建设假设Calendar。

8. LOCAL/MANUAL：
   - LOCAL传播扩大到全Domain仍为LOCAL；
   - MANUAL仅用户一开始整Domain重排。

9. Business Scope：
   - Scope外Task不可移动，但可作为阻挡。

### 明确不动

- Candidate单Domain；
- 外Domain ACTIVE Resource Block；
- 同一Solver；
- WHATIF不可直接激活；
- SH/PI/跨版本连续性口径。

---

## 9.2 《Pegging供需承接与分层计算业务说明》v1.5

只增加与Demand排序/Pegging直接有关的内容，不把页面/Run治理搬进Pegging文档。

### Delta

- `PriorityMode=NORMAL`：
  - 继续现有正式业务优先规则；
  - ManualTargetDueDate不能代替正式DueDate改变Demand顺序。

- `PriorityMode=EXPEDITE`：
  - 只提升本次明确选择订单及其派生需求链；
  - 其它普通Demand保持原规则；
  - Demand Protection/Strict Binding/不可逆事实优先。

- 删除任何暗示：
  - 修改DueDate实现急单；
  - 恢复PriorityScore；
  - 所有Candidate对象统一急单化。

---

## 9.3 《有限产能排产与滚动90天计划业务说明》v1.5

### Delta

- Candidate Seed支持多同类对象；
- `TaskTargets[]`多个软目标；
- `OrderTargets[]`多个目标日期；
- 故障Resource集合正式排除；
- Calendar变化Resource集合只作为传播Seed；
- 当前完整真实Calendar/Eligibility/占用仍全量进入Solver；
- Gantt固定NORMAL；
- EXPEDITE不突破任何硬约束；
- LOCAL影响扩大到全Domain仍保持LOCAL；
- Firm/Frozen故障场景按已有授权机制；
- 多个对象必须同一目标Domain；
- 其它Domain ACTIVE仍为Resource Block。

---

## 9.4 《核心排产全流程走查》V3.22

必须新增完整端到端场景流程，而不是只列规则。

### 需要完整走查的10类场景

1. 多新订单NORMAL CTP；
2. 多新订单EXPEDITE影响分析；
3. CTP结果正式插入；
4. 急插结果正式插入；
5. 多已有订单提前NORMAL；
6. 多已有订单提前EXPEDITE；
7. 多Task甘特拖拽；
8. 多设备故障；
9. 多Calendar变化资源；
10. 整Domain MANUAL重排。

### 每个流程都要明确

```text
4号位发起
→ 3号位校验RunType/Purpose/BusinessTriggerType/PriorityMode/Business Scope/Base ACTIVE
→ 2号位解析结构化输入、形成Seed和可移动集合
→ 1号位有限产能
→ Candidate PlanVersion
→ 5号位查询（普通Gantt/结果页面）
→ 4号位展示
→ 确认/放弃
→ 3号位激活门禁
```

---

## 9.5 《集成接口设计》v1.32

### ScheduleRun创建输入

正式增加CandidateInput v2 DTO语义：

```text
SchemaVersion
BusinessTriggerType
PriorityMode
OrderCanonicalIds
OrderTargets[]
TaskTargets[]
ChangedResourceIds[]
原11字段其余适用字段
```

### 3↔2接口

增加：

- 单场景合法性校验结果；
- 多对象结构化输入；
- Candidate Base ACTIVE校验；
- Business Scope允许移动集合；
- Candidate结果查询标识。

### 4↔3

新增/收敛中文业务入口对应请求：

- NewOrderCtpRequest
- NewOrderImpactRequest
- InsertRescheduleRequest
- ExistingOrderAdvanceRequest
- GanttAdjustmentRequest
- EquipmentFailureRescheduleRequest
- ResourceCalendarRescheduleRequest
- DomainManualRescheduleRequest

接口名可以复用现有统一Run创建API，不要求每个场景建设独立HTTP Endpoint；文档需明确这些是业务请求DTO/入口语义。

### 4↔5

Candidate Gantt查询必须带：

```text
PlanVersionId = Candidate PlanVersionId
DomainKey
Scope过滤上下文
```

5号位不得计算Candidate。

---

## 9.6 《数据架构与防腐层》v1.43

只做小幅治理：

```text
ScheduleRun.ScopeJson
= 运行输入快照
≠ 业务主数据
≠ 第二Scenario真相源
```

明确Owner：

- 3号位：创建/校验/冻结Candidate输入；
- 2号位：消费运行输入执行业务；
- 4号位：采集用户输入；
- 5号位：不拥有Candidate输入；
- 1号位：消费2号位组装后的求解上下文，不解析页面Schema。

明确：

> 不新增CandidateInput持久化平台。

---

## 9.7 《数据库字段说明》v5.1.7

### ScheduleRun.ScopeJson

旧：

> 固定11字段。

改为：

> **版本化运行输入JSON，历史v1=11字段，新建白天Candidate必须SchemaVersion=2。**

字段说明需写完整v2字段表、类型、必填条件、场景合法性。

### BasePlanVersionId

补充：

- Candidate创建时必须指向当前Domain ACTIVE；
- 激活时再次校验仍是当前正式Base关系；
- 不允许指向CANDIDATE。

### ExpectedDomainKeysJson

保持不变：

- 白天恰好一个Domain；
- 不回流ScopeJson。

---

## 9.8 《DDL》v5.1.7

### 不新增物理列/表

`ScheduleRun.ScopeJson NVARCHAR(MAX)`保持。

### 修改内容

- 顶部版本变化注释；
- `ScopeJson`列注释改为“Candidate运行输入版本化JSON；Schema v2见字段说明/接口”；
- 删除/废止“固定11字段绝对不变”的DDL注释；
- `BasePlanVersionId`注释补“Candidate必须指向创建时当前ACTIVE；不得指向CANDIDATE”。

本轮不增加SQL层复杂JSON字段级CHECK，避免把业务Schema硬编码进DDL；结构合法性由3号位Run治理服务校验。

---

## 9.9 《1号位实施包》v1.4

新增：

- 多Seed输入；
- 多Task软TargetTime；
- 多订单人工计划目标的求解目标；
- 多故障资源不可用；
- 多Calendar变化后的当前真实Calendar；
- Seed不等于事实过滤；
- Gantt固定NORMAL；
- EXPEDITE只改变上游已确定业务优先层级，不突破Solver硬约束；
- LOCAL完整Domain兜底仍返回同一Run结果。

不新增：

- 页面Schema解析；
- 权限判断；
- BusinessTriggerType业务治理。

---

## 9.10 《2号位实施包》v1.7

新增主责：

1. 解析CandidateInput v2；
2. 验证/消费单场景多对象；
3. 形成变化Seed；
4. 组装`OrderTargets[] / TaskTargets[] / ChangedResourceIds[]`；
5. NORMAL/EXPEDITE进入Demand/Pegging业务上下文；
6. ManualTargetDueDate不修改正式DueDate；
7. 当前Scope内允许移动集合；
8. Scope外Task形成不可移动Anchor/Block；
9. Candidate结果落盘；
10. Candidate查询数据准备；
11. 多Candidate Base关系与结果一致性配合3号位。

明确：

> 2号位不新增Candidate Scenario平台。

---

## 9.11 《3号位实施包》v1.5

本轮是核心治理Owner。

### 必须新增

- CandidateInput Schema v2校验；
- BusinessTriggerType合法值；
- BusinessTriggerType ↔ RunType/Purpose合法组合；
- BusinessTriggerType ↔ PriorityMode合法矩阵；
- 一Run一个业务场景；
- 同场景允许多对象；
- BasePlanVersionId必须当前ACTIVE；
- 禁止Base=CANDIDATE；
- 激活时Base过期保护；
- Business Scope校验；
- Candidate确认/放弃/激活审计；
- MANUAL只允许整Domain入口。

### 原11字段声明

必须废止“固定11字段绝对不变”。

---

## 9.12 《4号位实施包》v1.4

本轮改动最大。

### 新增独立中文入口

- 新订单正常承诺评估；
- 新订单急单影响分析；
- 已有订单提前；
- 甘特图调整；
- 设备故障后重排；
- 资源日历调整后重排；
- 整个当前计划域重新排程。

### 已有订单提前

- 多订单选择；
- 每单独立目标日期；
- NORMAL / EXPEDITE显式选择。

### 甘特

旧：

```text
拖拽 → MANUAL_RESCHEDULE
```

新：

```text
多Task拖拽
→ 评估
→ LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
→ NORMAL
→ Candidate Gantt
```

前端不得直接写正式Task。

### 故障/Calendar

- 多资源选择；
- 只允许选择已经正式生效的故障/Calendar变化事实；
- 不提供假设Calendar。

### 删除

- “其它明确对象局部调整”。

---

## 9.13 《5号位实施包》v1.5

小改：

- 支持指定Candidate PlanVersion的Gantt/Schedule查询；
- DTO增加Candidate标识/PlanVersion；
- 继续做Business Scope结果过滤；
- 不解析ScopeJson业务场景；
- 不计算Candidate；
- 不治理Candidate激活。

---

## 9.14 《0号位总体项目验收包》v1.4

至少新增原指导18个验收场景，并补充以下治理回归：

19. 同时生成B/C两个Candidate，均基于ACTIVE A，允许并存；
20. B先激活后，仍基于A的C不得直接激活；
21. 多对象跨两个Domain的一次LOCAL请求必须拒绝/拆分；
22. ScopeJson v2 `OrderTargets[]`与`OrderCanonicalIds`不一致必须拒绝；
23. `GANTT_ADJUSTMENT + EXPEDITE`必须拒绝；
24. `EQUIPMENT_FAILURE + EXPEDITE`必须拒绝；
25. `RESOURCE_CALENDAR_CHANGE + EXPEDITE`必须拒绝；
26. Candidate Scope外Task不得移动，但可作为资源阻挡；
27. 本次Seed外已正式生效的Calendar/故障仍进入Solver；
28. 未选择的新订单不得自动进入本次Candidate业务对象集合。

---

## 9.15 《代码审核检查基线》v1.2

新增P0/P1审核红线：

- Candidate Base指向CANDIDATE；
- 激活旧Base Candidate覆盖新ACTIVE；
- 一次LOCAL混合多个BusinessTriggerType；
- 甘特使用EXPEDITE；
- 甘特仍调用MANUAL_RESCHEDULE；
- 故障/Calendar做假设事实而非先正式生效；
- 多订单不同目标日期被压成一个ManualTargetDueDate；
- ManualTargetDueDate改写正式DueDate；
- PriorityMode通过DueDate篡改实现；
- ScopeJson仍按固定11字段拒绝合法v2输入；
- 新增CandidateInput平台/第二Scenario真相源；
- Seed被误实现为Solver事实过滤；
- Scope外Task被移动；
- LOCAL影响扩大后错误改写成MANUAL；
- “其它明确对象局部调整”万能入口继续存在；
- Candidate WHATIF直接激活；
- CTP采用不重新基于最新ACTIVE运行。

---

# 十、B类文档检查要求

## 10.1 Domain裁决

检查：

- Candidate单Domain；
- ExpectedDomainKeysJson；
- 跨Domain CTP串行。

若无冲突，不升版。

## 10.2 职责/前端/接口/权限归属裁决

检查4号位入口扩展是否超出既有“页面/交互Owner”边界。

预计：

> Owner不变化，因此不升版；但如果文档列举的前端入口是封闭清单，需要同步升版。

## 10.3 权限/Auth

复用已有：

- Factory；
- ProductFamily；
- Department；
- Domain；
- Candidate Create/Confirm/Activate；
- Frozen/Firm触碰权限。

若现有权限码能够表达，不新增场景专属Permission。

---

# 十一、版本建议

本轮建议升版：

```text
业务基线                  v1.6
Pegging                   v1.5
有限产能                  v1.5
核心走查                  V3.22
集成接口                  v1.32
数据架构                  v1.43
数据库字段                v5.1.7
DDL                       v5.1.7
0号位                     v1.4
1号位                     v1.4
2号位                     v1.7
3号位                     v1.5
4号位                     v1.4
5号位                     v1.5
代码审核检查基线          v1.2
```

日期统一使用本轮正式修订日期：

```text
2026-09-15
```

---

# 十二、正式修改顺序

按冻结文档工作流，后续实际修订顺序：

```text
1. 最终业务基线
2. Pegging
3. 有限产能
4. 核心走查
5. 集成接口
6. 数据架构
7. 数据库字段
8. DDL
9. 1号位
10. 2号位
11. 3号位
12. 4号位
13. 5号位
14. 0号位验收
15. 代码审核检查基线
16. B类文档复核
17. 旧口径语义反查
18. 场景回归
19. 六维一致性审核
20. 冻结索引/替换清单/发布说明
```

---

# 十三、本轮终验必须专项搜索的旧口径

正式修改完成后，至少反查：

```text
固定11字段
11字段Schema
MANUAL_RESCHEDULE形成Candidate
其它明确对象局部调整
Candidate叠Candidate
Base Candidate
PriorityScore
修改正式DueDate
ManualTargetDueDate
EXPEDITE
甘特
设备故障
Calendar
Scenario平台
MultiDomain Candidate
```

同时必须做语义回归，不能只依赖关键词。

---

# 十四、当前结论

本轮修改边界已经足够明确，可以进入正式母文档修订。

本轮最关键的技术收口为：

> **保留现有ScheduleRun、PlanVersion、RunType/Purpose和唯一Solver；把白天业务差异收敛为`ScopeJson Candidate Input Schema v2`中的业务发起类型、优先模式和同场景多对象结构化输入。**

这样既满足当前业务，也不建设第二套Candidate/Scenario平台。
