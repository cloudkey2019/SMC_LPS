# APS V1 白天业务场景与 Candidate 人工调整冻结文档修改指导

**版本**：v1.1  
**日期**：2026-09-14  
**适用范围**：APS V1 当前冻结文档修订、0～5号位实施包补充、后续代码审核与联调  
**文档性质**：冻结文档修改指导，不是新的长期平行业务基线  
**替代版本**：v1.0  

---

# 0. 本次修改目的

本轮不新增第二套 Candidate、不新增第二套有限产能求解器（Solver）、不新增新的运行类型（RunType）/用途（Purpose），而是把现有白天能力按业务场景重新收口。

本轮新增/修正的核心口径：

1. 一次局部重排（LOCAL_RESCHEDULE）只允许一种业务发起场景；
2. 同一场景下允许一次选择多个同类型对象；
3. 甘特拖拽固定使用正常优先模式（NORMAL）；
4. 已有订单提前允许多个订单分别输入不同目标日期；
5. 设备故障允许一次选择多台已正式不可用设备；
6. 资源日历调整允许一次选择多台已正式修改日历（Calendar）的设备；
7. 删除“其它明确对象局部调整”这一泛化入口；
8. 每次Candidate必须以当时唯一正式生效计划（ACTIVE PlanVersion）为基准，禁止Candidate叠Candidate；
9. 变化起点只决定影响传播从哪里开始，不屏蔽其它已经生效的真实事实；
10. 当前固定11字段的 `ScopeJson` 需要正式重新评估Candidate运行输入承载方式。

---

# 一、最高业务原则

## 1.1 一次LOCAL只允许一种业务场景

正式冻结：

> **一次LOCAL_RESCHEDULE只允许一个业务发起场景类型，但该场景下允许多个同类型变化对象。**

允许：

```text
已有订单提前
→ 一次选择多个订单
→ 每个订单各自目标日期
→ 一个ScheduleRun
→ 一个Candidate PlanVersion
```

允许：

```text
设备故障后重排
→ 一次选择多台故障设备
→ 一个ScheduleRun
→ 一个Candidate PlanVersion
```

不允许一次LOCAL同时混装：

```text
订单提前
+ 甘特拖拽
+ 设备故障
+ Calendar调整
```

V1不建设万能场景模拟平台。

## 1.2 Candidate必须基于ACTIVE

每一次白天Candidate必须基于当时唯一正式生效计划（ACTIVE PlanVersion）和当前最新真实事实。

正确：

```text
ACTIVE A
↓
场景1
↓
Candidate B
↓
确认采用
↓
B成为新的ACTIVE
↓
场景2
↓
以新的ACTIVE B为基准
↓
Candidate C
```

如果Candidate B被放弃：

```text
ACTIVE仍为A
↓
场景2仍以A为基准
```

禁止：

```text
ACTIVE A
↓
未确认Candidate B
↓
以B为Base
↓
再生成Candidate C
```

即：

> **V1禁止Candidate叠Candidate。**

## 1.3 变化起点不会屏蔽其它真实事实

本次选择的对象只表示：

> **为什么发起本次局部重排、系统从哪里开始识别影响。**

它不限制1号位只能看到这些对象。

例如：

```text
MC01 / MC02已故障
MC05 Calendar此前已正式修改
```

本次从“设备故障后重排”入口选择MC01、MC02时：

- MC01/MC02是本次变化起点；
- MC05新的Calendar仍属于当前真实事实；
- Solver仍必须看到当前完整资源日历、资源资格、资源占用、物料可用时间等事实。

正式冻结：

> **变化起点决定影响传播起点，不限制当前完整真实上下文进入求解。**

---

# 二、白天业务场景矩阵

| 序号 | 业务场景 | 一次允许选择的对象 | 技术运行组合 | 优先级模式 | 是否可直接激活 |
|---|---|---|---|---|---|
| 1 | 新订单正常承诺评估 | 多个已进入标准订单事实的新订单 | INSERT_ORDER_WHATIF + CTP | NORMAL | 否 |
| 2 | 新订单急插影响分析 | 多个明确急单订单 | INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS | EXPEDITE | 否 |
| 3 | 按CTP结果正式插入 | 本次明确选择的新订单 | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | NORMAL | Candidate确认后可激活 |
| 4 | 按急插分析结果正式插入 | 本次明确选择的新订单 | LOCAL_RESCHEDULE + INSERT_RESCHEDULE | EXPEDITE | Candidate确认后可激活 |
| 5 | 已有订单提前——正常模式 | 多个订单 + 每单独立目标日期 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | NORMAL | Candidate确认后可激活 |
| 6 | 已有订单提前——急单模式 | 多个订单 + 每单独立目标日期 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | EXPEDITE | Candidate确认后可激活 |
| 7 | 甘特图人工拖拽调整 | 一个或多个Task + 各自目标时间 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | **固定NORMAL** | Candidate确认后可激活 |
| 8 | 设备故障后的局部重排 | 一台或多台已正式不可用设备 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | **固定NORMAL** | Candidate确认后可激活 |
| 9 | 资源日历正式调整后的局部重排 | 一台或多台已正式修改Calendar的设备 | LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT | **固定NORMAL** | Candidate确认后可激活 |
| 10 | 整个当前Domain人工重排 | 不选局部对象，直接当前Domain | MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT | 按现有冻结优先规则 | Candidate确认后可激活 |

本轮删除：

> **“其它明确对象局部调整”**

V1只开放边界清楚、能够审计的业务入口。

---

# 三、CTP统一业务定义

承诺交期评估（CTP）定义为：

> **针对已经进入APS标准订单事实的新订单，在不人为赋予急单特权、按照正常业务优先规则的条件下，计算真实有限物料与有限产能下的最早可承诺完成时间，并说明对当前计划的影响。**

CTP不是：

- 只查空档；
- 完全不允许影响当前Domain可移动计划；
- 自动提升为最高优先级；
- 直接生成正式计划。

CTP必须继续使用真实供需承接（Pegging）、物料可用时间（AvailableTime）、资源资格（Resource Eligibility）、Calendar、有限产能和Candidate影响传播。

CTP结果至少包括：

- 是否满足目标交期；
- 最早可承诺完成时间；
- 是否依赖估算采购；
- 受影响订单；
- 哪些订单仍按期；
- 哪些订单延期；
- 最大延期；
- 是否触碰需求保护（Demand Protection）；
- 主要瓶颈；
- 主要原因。

## 3.1 CTP Candidate不得直接激活

正式采用CTP结果时：

```text
当前ACTIVE + 当前最新事实
↓
重新创建
LOCAL_RESCHEDULE + INSERT_RESCHEDULE
PriorityMode = NORMAL
↓
重新生成一个可激活Candidate
↓
人工确认
↓
ACTIVE
```

不得把原WHATIF Candidate直接转换为ACTIVE。

## 3.2 跨Domain CTP

Candidate仍严格单Domain。

如果一个订单依赖多个Domain：

> 按现有依赖拓扑串行执行多个单Domain WHATIF，并最终汇总成一次客户CTP答案。

---

# 四、急单插入影响分析

急单插入影响分析定义为：

> **用户明确把选中的新订单作为急单，在所有仍可移动、仍可重新竞争的普通需求中提高优先层级，评估最快完成时间和对已有计划的影响。**

急单优先（EXPEDITE）只作用于：

> **本次明确选择的订单及其派生需求链。**

不得突破：

- 已执行不可逆事实；
- 冻结区/固定区（Frozen / Firm）；
- 严格绑定；
- 需求保护；
- 物料真实AvailableTime；
- Routing；
- Resource Eligibility；
- Business Scope；
- 其它Domain ACTIVE不可移动占用。

NORMAL / EXPEDITE不得恢复全局PriorityScore、加权总分或任意表达式评分器，仍服从现有优先规则段模型。

---

# 五、NORMAL / EXPEDITE场景限制

| 场景 | 优先级 |
|---|---|
| 新订单CTP | NORMAL |
| 新订单急插影响分析 | EXPEDITE |
| 按CTP结果正式插入 | NORMAL |
| 按急插结果正式插入 | EXPEDITE |
| 已有订单提前 | NORMAL / EXPEDITE由用户明确选择 |
| 甘特图拖拽 | **固定NORMAL** |
| 设备故障后重排 | **固定NORMAL** |
| 资源日历调整后重排 | **固定NORMAL** |
| 整Domain人工重排 | 按正式冻结优先规则 |

特别禁止：

> 通过拖拽某个Task把对应订单“偷偷变成急单”。

如果用户希望已有订单按急单处理，应进入“已有订单提前——急单模式”。

---

# 六、已有订单提前

## 6.1 一次可以多个订单、每单不同目标日期

例如：

```text
订单A → 9月20日
订单B → 9月22日
订单C → 9月25日
```

运行输入必须表达为：

> **订单ID与目标日期成对的集合。**

禁止用：

```text
多个OrderCanonicalId
+
一个ManualTargetDueDate
```

表达多个不同目标日期。

## 6.2 人工目标日期只属于本次Candidate

人工计划目标日期不修改：

- ERP DueDate；
- Order_Canonical正式客户交期；
- 长期主数据。

V1不建设持续有效人工计划交期Override平台。

## 6.3 NORMAL模式不能偷换正式DueDate

人工计划目标日期与订单优先权必须分开。

NORMAL模式下：

- Demand排序继续使用现有正式业务事实和冻结优先规则；
- 人工目标日期作为本次有限产能求解的目标完成时间/优化目标；
- 不允许把人工目标日期写成新的正式DueDate来改变排序。

EXPEDITE模式需要提高优先级时，由PriorityMode明确表达。

---

# 七、甘特图人工拖拽

## 7.1 固定NORMAL

正式冻结：

> **甘特拖拽固定使用NORMAL，不提供EXPEDITE选择。**

## 7.2 一次可以多个Task

允许：

```text
Task A → 希望移动到08:00附近
Task B → 希望移动到10:30附近
```

一次提交，一个LOCAL，一个Candidate。

## 7.3 拖拽目标为软目标

拖到08:00表示：

> 希望系统尽量向08:00调整。

如果08:00不可行：

```text
Solver返回08:30
+
Explanation说明原因
```

V1不自动增加“必须08:00，否则失败”的硬锁。

## 7.4 不直接修改正式Task

流程：

```text
当前ACTIVE甘特图
↓
用户拖拽一个或多个Task
↓
点击评估
↓
LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
↓
1号位真实求解
↓
生成Candidate PlanVersion
↓
4号位展示Candidate甘特图与变化
↓
用户采用/放弃
```

Candidate甘特图必须来自后台真实Candidate，不是前端假的计划结果。

---

# 八、设备故障后的局部重排

## 8.1 一次允许多台设备

例如：

```text
设备A故障
设备B故障
设备C故障
```

用户可以一次选择A/B/C：

```text
A/B/C已正式不可用
↓
作为本次变化起点
↓
一次LOCAL_RESCHEDULE
↓
一个Candidate
```

## 8.2 故障必须先成为真实资源事实

设备故障不是Candidate假设。

必须先正式更新资源不可用事实，再发起重排。

## 8.3 不直接指定A换B

标准场景：

- A退出合法候选；
- B/C等仍满足资源资格的设备继续作为候选；
- 1号位结合Calendar、Capacity、Setup、已有占用决定最终资源；
- 无合法替代资源时返回延期或不可行原因。

## 8.4 Firm / Frozen

如果故障设备上的未来Task位于Firm/Frozen：

- 已执行部分不可逆；
- 尚未执行部分不能因为设备坏了就自动突破Fence；
- 必须复用现有人工重排授权/冻结区触碰权限；
- 无权时返回“需要授权重排”；
- 有权时才允许调整未来部分。

---

# 九、资源日历调整后的局部重排

## 9.1 一次允许多台设备

例如：

```text
MC01加班
MC03班次延长
MC08次日停班
```

Calendar正式修改完成后，可一次选择这三台资源，运行一次LOCAL。

## 9.2 V1不做假设Calendar

V1不支持：

> “Calendar不改，先假设今晚加班2小时看看结果”。

正确流程：

```text
正式修改Resource Calendar
↓
成为当前有效事实
↓
选择本次发生Calendar变化的资源集合
↓
LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
↓
一个Candidate
```

---

# 十、LOCAL_RESCHEDULE与MANUAL_RESCHEDULE边界

## 10.1 LOCAL_RESCHEDULE

统一定义：

> **由一个明确业务场景和一个或多个同类型变化对象发起，系统从这些对象开始真实影响传播，并尽量少扰动其它计划。**

V1允许的LOCAL业务入口：

1. 新订单正式插入；
2. 已有订单提前；
3. 甘特图调整；
4. 设备故障后重排；
5. 资源日历调整后重排。

本轮不保留“其它明确对象局部调整”。

## 10.2 LOCAL传播扩大到全Domain

如果最初从局部对象发起，但Solver最终需要扩大到当前Domain全部可移动Task重排：

> **RunType仍为LOCAL_RESCHEDULE。**

不能因为影响变大自动切成MANUAL。

## 10.3 MANUAL_RESCHEDULE

只有：

> **用户从一开始就明确选择“整个当前Domain全部可移动计划重新排”**

才使用：

```text
MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT
```

MANUAL不是“影响很大”，而是业务从一开始主动选择整域重排。

---

# 十一、Department业务范围

局部重排不得因为影响传播突破Business Scope。

例如用户只有Department A范围：

- A范围Task可以移动；
- B范围Task不能被当前用户发起的Candidate移动；
- B范围Task可作为真实资源/时间阻挡进入计算。

职责：

```text
3号位
→ 认证Business Scope

2号位
→ 形成允许移动集合
→ 范围外Task作为不可移动锚点/阻挡

1号位
→ 只负责求解

2号位
→ 返回后再次校验Candidate没有越权
```

如果真实可行方案必须移动无权Task：

> 返回“当前Business Scope不足，需要更高范围用户重新发起”。

---

# 十二、现有技术组合保持不变

继续只使用：

```text
INSERT_ORDER_WHATIF + CTP
INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS
LOCAL_RESCHEDULE + INSERT_RESCHEDULE
LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT
MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT
```

不增加：

- EXISTING_ORDER_WHATIF；
- GANTT_DRAG_RUN；
- EQUIPMENT_FAILURE_RUN；
- CALENDAR_RESCHEDULE_RUN；
- EXPEDITE_RUN；
- SCENARIO_RUN。

---

# 十三、Candidate运行输入治理

## 13.1 当前固定11字段ScopeJson必须重新评估

现有冻结文档仍把 `ScheduleRun.ScopeJson` 固定为11字段。

但本轮必须表达：

- PriorityMode；
- 多订单各自目标日期；
- 多Task拖拽目标；
- 多故障设备；
- 多Calendar变化资源；
- 本次唯一业务场景类型。

因此必须正式裁决：

> **允许对Candidate运行输入Schema做正式升级；不得一边保留“11字段绝对不变”，一边在代码里偷偷增加第12、13个字段。**

## 13.2 必须支持结构化集合

已有订单提前：

```text
[
  订单A + 目标日期A,
  订单B + 目标日期B
]
```

甘特调整：

```text
[
  TaskA + 目标时间A,
  TaskB + 目标时间B
]
```

设备故障：

```text
[ResourceA, ResourceB, ResourceC]
```

Calendar变化：

```text
[ResourceA, ResourceB, ResourceC]
```

## 13.3 不先拍死物理实现

可选择：

- 正式升级ScopeJson Schema；
- 或使用与ScheduleRun明确关联的最小运行输入快照/DTO。

必须满足：

- 可追溯；
- 可重放；
- 一个Run只能有一个场景类型；
- 支持同场景多对象；
- 与ScheduleRun关联明确；
- 不建设Scenario平台。

如果只是扩展NVARCHAR(MAX)内部Schema，原则上无需新增物理列，但字段说明、接口Schema、DDL注释和2/3号位实施包必须同步。

---

# 十四、Candidate结果展示与职责

## 14.1 4号位

提供独立中文入口：

- 新订单正常承诺评估；
- 新订单急单影响分析；
- 已有订单提前；
- 甘特图调整；
- 设备故障后重排；
- 资源日历调整后重排；
- 整个当前计划域重新排程。

负责多对象选择、允许场景下的NORMAL/EXPEDITE、每订单目标日期、甘特目标、Candidate甘特图、影响摘要、确认/放弃。

不得自己排程或直接UPDATE正式Task。

## 14.2 5号位

普通Gantt / Schedule查询继续：

```text
4 → 5 → 2
```

5号位提供稳定查询接口/DTO/Scope过滤，不计算Candidate。

## 14.3 3号位

继续负责：

- Run创建与治理；
- RunType/Purpose合法性；
- Base ACTIVE PlanVersion；
- 单Domain；
- Business Scope；
- PriorityMode；
- 一个Run一个场景类型；
- Candidate运行输入追溯；
- Candidate确认/激活；
- Audit。

并禁止使用未激活Candidate作为下一Run的Base。

## 14.4 2号位

继续负责：

- Candidate真实业务执行；
- 重新Pegging；
- 解析本次唯一场景输入；
- 形成变化起点/人工目标；
- LogicalProductionDemand；
- DomainSolveRequest；
- 调用1号位；
- Candidate结果持久化/查询；
- Scope可移动集合与结果校验。

## 14.5 1号位

继续负责：

- 有限产能；
- 最终Resource；
- Start/End；
- 多个同类目标的真实影响传播；
- 故障资源排除；
- 合法替代资源；
- Explanation；
- Unscheduled。

不读取页面，不直接写数据库，不判断用户权限。

---

# 十五、本轮明确禁止新增

1. 新RunType/Purpose；
2. “其它明确对象局部调整”万能入口；
3. 一次LOCAL混合多种业务场景；
4. Candidate叠Candidate；
5. 纯假设订单录入平台；
6. APS长期人工计划交期Override平台；
7. Candidate专属假设Calendar；
8. 浏览器实时排程器；
9. 甘特拖拽直接UPDATE正式Task；
10. 人工A设备→B设备硬替换主流程；
11. 第二套Solver；
12. MultiDomain Candidate；
13. Scenario/Simulation平台；
14. 全局PriorityScore；
15. 为每个场景创建独立业务表。

---

# 十六、冻结文档影响范围

## 16.1 必须修改

### A. 《APS V1 最终全部流程与业务基线》

补充：

- 新业务场景矩阵；
- 一次LOCAL一个场景类型、同场景多对象；
- Candidate必须基于ACTIVE；
- 禁止Candidate叠Candidate；
- CTP NORMAL与急单EXPEDITE；
- CTP/Impact正式采用重新创建LOCAL；
- 多订单不同目标日期；
- 甘特固定NORMAL；
- 多设备故障；
- 多资源Calendar变化；
- LOCAL/MANUAL边界；
- Department Scope。

### B. 《APS 有限产能排产与滚动90天计划业务说明》

补充：

- 多个同类人工目标；
- 甘特软目标、固定NORMAL；
- 多故障资源排除；
- 当前完整真实上下文；
- LOCAL Domain级兜底；
- Firm/Frozen故障场景；
- EXPEDITE不突破硬约束。

### C. 《APS Pegging供需承接与分层计算业务说明》

仅补：

- NORMAL/EXPEDITE对白天Demand排序语义；
- EXPEDITE只作用选中订单及其需求链；
- 人工目标日期不能偷换正式DueDate；
- 不恢复PriorityScore。

### D. 《APS 核心排产全流程走查》

补齐：

- 新订单选择；
- CTP/急单影响；
- 正式INSERT_RESCHEDULE；
- 多订单提前；
- 多Task拖拽；
- 多设备故障；
- 多资源Calendar变化；
- 整域重排；
- Candidate以ACTIVE为Base；
- 确认/激活后下一场景才能以新ACTIVE为Base。

### E. 《APS 集成接口设计》

需要表达：

- OrderCanonicalIds；
- PriorityMode；
- 多订单目标日期集合；
- 多Task目标集合；
- 多故障资源集合；
- 多Calendar变化资源集合；
- 本次唯一场景类型；
- Candidate PlanVersion Gantt查询；
- Candidate确认/激活。

### F. 1号位实施包

补：

- 多个同类人工目标；
- 甘特软目标、固定NORMAL；
- 多故障资源；
- 当前完整真实上下文；
- LOCAL Domain级兜底。

### G. 2号位实施包

补：

- 单场景多对象；
- 多订单不同目标日期；
- PriorityMode；
- 变化起点/人工目标组装；
- Candidate必须基于ACTIVE；
- 禁止Candidate叠Candidate；
- Scope移动集合；
- Candidate结果持久化/查询。

### H. 3号位实施包

补：

- 场景到5种技术组合映射；
- 一个Run一个场景类型；
- PriorityMode合法性；
- Candidate输入治理；
- Base必须ACTIVE；
- MANUAL仅整Domain；
- Scope/Audit。

### I. 4号位实施包

主要页面增量：

- 新订单选择；
- CTP/急单中文入口；
- 已有订单提前；
- 允许场景才显示NORMAL/EXPEDITE；
- 甘特固定NORMAL；
- 多Task拖拽；
- Candidate甘特；
- 多设备故障；
- 多资源Calendar调整后重排；
- 整域重排；
- 删除“其它明确对象局部调整”。

### J. 5号位实施包

小改：

- Candidate PlanVersion Gantt查询；
- DTO；
- Scope过滤；
- 不计算Candidate。

## 16.2 条件修改

### 《APS 数据库字段说明文档》

必须正式处理Candidate运行输入Schema。

如果继续ScopeJson：

> 正式升级Schema，不再写“11字段绝对不变”。

### 《APS 数据库表结构设计 DDL》

若只是ScopeJson内部Schema升级：

> 不增加物理列，但DDL注释同步。

### 《APS 数据架构与防腐层设计方案》

仅在新增物理运行输入结构时补Owner、数据流和防第二真相边界。

### 《APS V1 0号位总体项目验收包》

正式回写后补验收场景。

---

# 十七、建议新增验收场景

1. 白天3张新订单只选择1张CTP，另外2张不得自动进入本次Candidate；
2. CTP NORMAL不得人为急单化，Candidate不可直接激活；
3. 接受CTP后必须重新基于当前ACTIVE创建LOCAL + INSERT_RESCHEDULE + NORMAL；
4. 急单影响分析仅提升选中订单及其需求链；
5. 多订单提前一次支持多个不同目标日期；
6. NORMAL下人工计划目标日期不得改写正式DueDate；
7. 甘特多Task拖拽固定NORMAL；
8. 多设备故障一次LOCAL；
9. 故障无替代资源时不得突破Eligibility；
10. 故障Task位于Firm/Frozen时按既有授权机制处理；
11. 多资源Calendar正式修改后一次LOCAL；
12. 本次变化起点之外的其它最新真实Calendar/故障事实仍进入Solver；
13. LOCAL影响扩大到全Domain后RunType仍为LOCAL；
14. 用户从一开始整Domain重排才是MANUAL；
15. Department Scope外Task不得被当前用户移动；
16. Candidate Gantt显示真实Candidate结果；
17. Candidate B未确认时不得作为下一Run Base；
18. Candidate B确认成为ACTIVE后，下一场景以新的ACTIVE B为Base。

---

# 十八、最终冻结口径

> **APS V1白天局部人工重排按业务场景分入口。一次LOCAL_RESCHEDULE只允许一种业务发起场景，但同一场景允许一次选择多个同类型对象：已有订单提前可一次选择多个订单且每个订单有独立目标日期；甘特图调整可一次提交多个Task目标且固定NORMAL；设备故障后重排可一次选择多台已正式不可用设备且固定NORMAL；资源日历调整后重排可一次选择多台已正式修改Calendar的设备且固定NORMAL。一次操作生成一个ScheduleRun和一个Candidate PlanVersion。所有Candidate必须以当时唯一ACTIVE PlanVersion为Base，禁止Candidate叠Candidate；上一Candidate只有在人工确认成为新的ACTIVE后，后续其它业务场景才以新的正式计划为基础继续生成Candidate。变化起点只决定本次影响传播从哪里开始，不屏蔽其它已经生效的真实资源、Calendar、物料和Task占用事实。只有用户从一开始明确要求整个当前Domain全部可移动计划重新排时才使用MANUAL_RESCHEDULE。V1不保留“其它明确对象局部调整”万能入口，不新增RunType/Purpose，不建设Scenario平台。**

---

**结束**
