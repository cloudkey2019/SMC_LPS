# APS V1 4号位页面与业务操作开发实施包（冻结版）

**版本**：v1.0  
**日期**：2026-08-14  
**适用对象**：4号位及其开发AI  
**文档性质**：从零开发实施说明  
**上位依据**：APS V1三份业务冻结文档 + 六份技术冻结文档 +《APS V1关键接口冻结：1↔2、2↔5、2↔3》

---

# 一、4号位在APS V1中的定位

4号位不是排程引擎，也不是数据库维护工具。

4号位在V1中的核心职责是：

> **把已经由2号位、1号位、3号位、5号位形成的计划、影响、风险、规则和操作边界，以PMC/业务人员能理解和能安全操作的方式呈现出来。**

4号位主要负责：

1. 计划结果查询与展示；
2. 订单纳期/CTP查询；
3. 白天插单影响分析与Candidate对比；
4. Candidate最小人工确认与采用入口；
5. 异常、瓶颈、延期原因展示；
6. 计划版本/运行状态展示；
7. 规则与参数维护页面；
8. PI Position/UNLOCATED/数据问题展示；
9. MES下发资格/状态展示；
10. 业务操作审计信息展示。

4号位不负责Demand排序算法、Pegging、Supply扣减、PI Position计算、有限产能求解、FinalTask生成、ScheduleRun/PlanVersion核心状态机实现、直接UPDATE APS运行结果表、直接修改Task时间/资源、直接修改Allocation，也不能绕过2/3号位服务写数据库。

一句话：

> **4号位负责“看清楚、比较清楚、操作安全”，不负责“自己算”和“自己改结果”。**

---

# 二、页面总原则

## 2.1 页面只调用后端服务，不直接改运行库

任何页面操作必须走API/Application Service/已冻结业务服务。

禁止：

- 页面直接UPDATE Task；
- 页面直接UPDATE PlanVersion；
- 页面直接UPDATE PeggingSupplyAllocation；
- 页面直接改DemandSupplyHardLock；
- 页面直接改InventoryBalance；
- 页面直接执行排程SQL。

## 2.2 页面展示必须区分“事实、结果、估算、建议”

V1至少区分四类：

- FACT：设备故障、PI当前Position、PO ETA、MES报工等真实事实；
- RESULT：FinalTask、PlannedStart/End、EarliestCompletion、Delay等排程结果；
- ESTIMATED：Planning-only Purchase Placeholder等估算；
- RECOMMENDATION：建议发起重排、建议确认Candidate等。

页面不得把ESTIMATED显示成确定承诺。

---

# 三、V1页面范围

建议V1只做以下九类页面：

1. 排产总览
2. 订单/需求计划查询
3. 甘特图/资源计划
4. CTP/插单评估
5. Candidate对比与确认
6. 异常与原因解释
7. PI位置与供给追溯
8. 规则与参数维护
9. 运行/版本与MES下发状态

---

# 四、页面1：排产总览

目标是让PMC快速回答：今天/本周计划是否正常、哪些订单延期、哪些资源过载、哪些Domain失败、哪些计划依赖估算采购、哪些Candidate待确认。

主要区域包括：

- 当前ACTIVE版本：DomainKey、PlanVersionId、ActivatedAt、SourceScheduleRunId、PlanHorizon；
- 订单结果摘要：On-time、Delayed、Risk、Unscheduled、Estimated-only；
- 资源摘要：关键Resource负荷、Bottleneck、无可用产能时段；
- 数据/运行异常：FAILED Domain、被上游失败阻断的Domain、PI Position严重Issue、ODS契约失败等。

---

# 五、页面2：订单/需求计划查询

支持按OrderNo、ProductionInstructionNo、Material、Customer、Factory、ProductFamily、DueDate、DelayStatus、PlanVersion、Domain查询。

订单详情建议分四块：

1. 订单基本信息；
2. Pegging承接：SupplyType、SupplyKey、AllocatedQty、AvailableTime、Commitment/Confidence、Lock；
3. 生产计划：FinalTask、Stage/Operation、Resource、Start/End、Quantity、PlannedProcessQty、Status；
4. 原因解释：延期、物料等待、资源不足、前序延迟、Firm/Frozen、外Domain资源阻挡、采购估算风险。

---

# 六、页面3：甘特图 / 资源计划

甘特图只展示FinalTask，不展示LogicalProductionDemand为Task。

支持Resource、Stage、ProductionDepartment、Factory、Domain维度。

Task正式状态只展示：

- PLANNED
- RELEASED
- IN_PROGRESS
- COMPLETED
- CANCELLED

不增加SCHEDULED、PAUSED、SUSPENDED、WAITING、RUNNING。

不可移动标识可显示EXECUTION/FIRM/FROZEN，但用户不能拖动后直接写库。

V1不做“拖一下就直接改正式计划”。如需人工调整，应发起MANUAL_RESCHEDULE形成Candidate，再比较、确认。

---

# 七、页面4：CTP / 插单评估

输入至少包括：新订单/OrderCanonicalId、Material、Quantity、Factory、Requested DueDate、必要客户信息、Purpose。

输出必须回答：

1. 能否满足Requested DueDate；
2. 最早完成日期；
3. 是否依赖ESTIMATED供给；
4. 受影响订单数；
5. 哪些订单新增延期；
6. 哪些Protection冲突；
7. 主要瓶颈；
8. 主要原因；
9. Candidate是否单Domain；
10. 跨Domain时是否完成链式WHATIF。

如果依赖Planning-only Purchase Placeholder，页面必须明显显示：

> **估算日期，采购尚无正式承诺**

不能只显示绿色“可交”。

---

# 八、跨Domain CTP页面

V1没有MultiDomain Candidate。

后台应是：

Domain C WHATIF → Quantity-Time → Domain B WHATIF → Quantity-Time → Domain A WHATIF → 汇总。

页面可以只展示一个综合结果，但详情应能看到每个Domain的Candidate结果、Quantity-Time传递、关键瓶颈和失败Domain。

---

# 九、页面5：Candidate对比与确认

至少展示：

- 新订单Requested DueDate、Candidate Completion、是否按期、是否Estimated；
- 既有订单原完成时间、Candidate完成时间、差值、是否从On-time变Delayed、Protection冲突；
- Task变化摘要：新增、删除、时间移动、Resource变更数量。

不要求V1做复杂Diff Graph。

Candidate确认采用最小人工确认：

- Candidate PlanVersionId；
- Base PlanVersionId；
- 影响摘要；
- Confirm按钮；
- Actor；
- ConfirmedAt；
- Remark（可选）。

CTP和INSERT_IMPACT_ANALYSIS永远不得激活，页面必须隐藏/禁用“采用”。

---

# 十、OA审批边界

V1不把完整OA审批作为Candidate必经主链。

如果企业需要OA，只作为可选Adapter。

OA不可用时，APS最小人工确认仍应工作；不为OA增加复杂Task/PlanVersion状态。

---

# 十一、页面6：异常与原因解释

主要消费：

- ScheduleExplanationFact；
- BusinessFactIssue；
- Run/Domain失败信息；
- RescheduleRecommendation。

页面必须优先显示根因，比如采购料9月3日才可用、MC01无产能、上游Stage完成晚、Firm任务占关键窗口。

不要只显示DUE_DATE_RISK。

数据问题与排程问题要分开显示。

---

# 十二、设备故障页面

V1设备故障流程：

故障事实 → ImpactAssessment → ScheduleExplanationFact → RescheduleRecommendation → 看板提示 → PMC决定是否重排。

页面可以显示Resource Down、受影响订单、建议重排。

不能自动PAUSE Task、自动RESUME Task、自动创建ScheduleRun、自动激活Candidate。

---

# 十三、页面7：PI Position / 供给追溯

PI Position按PI展示：

- ERP RemainingQty；
- PositionType；
- Stage；
- XC；
- Transit；
- Waiting；
- UNLOCATED；
- PositionQty；
- Issue。

最好显示Position合计 / ERP RemainingQty。

Supply追溯可展示INVENTORY、PI、PO、VMI、ARRIVED_NOT_INBOUND、INTERPLANT_TRANSIT、RECEIVED、PLANNED_PRODUCTION、PLANNING_PURCHASE_PLACEHOLDER。

Placeholder必须明显标记ESTIMATED / NOT_COMMITTED。

---

# 十四、页面8：规则与参数维护

4号位负责页面，3号位负责后端治理。

支持：

- RuleSet列表、当前版本、DRAFT编辑、Diff、发布、Retired、ChangeReason；
- Demand Priority按Segment展示，不展示PriorityScore；
- Parameter维护Default Purchase LT、Arrival Offset、Demand Protection阈值、Planning Yield、Solver Strategy、On-time Target、Candidate Guardrail、Split/Setup/Overlap等；
- 发布前调用3号位校验API，页面不能直接改PUBLISHED字段。

---

# 十五、页面9：运行 / 版本 / MES下发状态

ScheduleRun展示：

- RunId
- RunType
- Status
- DataCutoffTime
- StrategyProfileVersion
- ExpectedDomainKeys
- CompletedAt
- ErrorMessage

PlanVersion展示：

- DomainKey
- BUILDING/ACTIVE/FAILED/CANDIDATE/ARCHIVED
- BasePlanVersion
- ActivatedAt
- ErrorMessage

PARTIAL_SUCCESS必须区分成功、失败、因上游失败被阻断的Domain。

FAILED人工恢复按钮必须调用“新建ScheduleRun”，不能把FAILED直接改回RUNNING。

---

# 十六、MES下发资格展示

Task详情可显示EligibleForMES和原因。

不得下发的典型情况：

- Candidate；
- UNLOCATED；
- 无正式PI的规划占位Task；
- 仍依赖Planning-only Purchase Placeholder；
- 不在合法下发窗口；
- 已取消/失效。

页面只能调用下发服务，不直接写MES接口表。

---

# 十七、人工操作审计

会改变计划状态的业务操作至少记录：

- Actor
- Time
- ObjectType
- ObjectId
- Action
- Remark/Reason（必要时）

包括Candidate确认、手工释放Demand Protection、Rule/Parameter发布、发起Manual Reschedule、MES下发。

---

# 十八、权限最小化

V1建议至少：

- VIEWER：只读
- PMC：发起评估/确认Candidate/重排
- RULE_ADMIN：维护DRAFT
- RULE_PUBLISHER：发布规则
- SYSTEM_ADMIN：技术维护

如公司已有权限框架就复用，不再建设新的APS Auth平台。

---

# 十九、前端API原则

4号位不依赖数据库表字段作为长期API。

后端提供业务DTO，例如：

- PlanOverviewDto
- OrderScheduleDetailDto
- CandidateComparisonDto
- CtpResultDto
- PiPositionViewDto
- ExplanationViewDto
- RunStatusDto
- RuleVersionDto

---

# 二十、前端状态与后端状态不要混用

UI可以有loading、disabled、warning、expanded，但这些不是业务状态。

不要把“处理中”随手落成PlanVersion/Task新状态。

---

# 二十一、建议开发顺序

阶段A：只读结果页  
- 排产总览
- 订单详情
- 甘特图
- Explanation
- Run/Version

阶段B：CTP/Candidate  
- CTP输入
- Candidate结果
- Base vs Candidate
- Estimated标识
- 最小人工确认

阶段C：规则参数页面  
- RuleSet
- ParameterSet
- StrategyProfile
- Priority Segment
- Diff/Publish

阶段D：异常/PI Position/MES  
- PI Position
- BusinessFactIssue
- 设备故障影响
- MES下发资格
- 人工恢复入口

---

# 二十二、最低验收场景

| 编号 | 场景 | 必须结果 |
|---|---|---|
| U01 | ACTIVE多Domain | 各Domain版本正确展示 |
| U02 | PARTIAL_SUCCESS | 成功/失败/阻断Domain区分 |
| U03 | 订单Pegging详情 | Supply来源和数量可追溯 |
| U04 | 一个Task承接多Order | TaskShare正确展示 |
| U05 | 计划良率 | NetQty与PlannedProcessQty不混淆 |
| U06 | 40+60 Quantity-Time | 页面不压平成100最晚 |
| U07 | Estimated采购 | 明显显示NOT_COMMITTED |
| U08 | CTP可按期 | 最早日期和原因正确 |
| U09 | CTP无法按期 | 显示根因，不只显示DUE_DATE_RISK |
| U10 | Candidate影响既有订单 | Base/Candidate差异正确 |
| U11 | CTP Purpose | 无激活按钮 |
| U12 | INSERT_IMPACT_ANALYSIS | 无激活按钮 |
| U13 | Candidate采用 | Actor/Time可追溯 |
| U14 | 外Domain共享设备阻挡 | 页面显示阻挡原因 |
| U15 | MaxImpactedOrders超阈值 | Warning，不表示截断 |
| U16 | PI UNLOCATED | 显示数量并提示不可MES |
| U17 | 无PI规划Task | 显示不可MES |
| U18 | Planning Placeholder依赖Task | 不允许下MES |
| U19 | FAILED Run恢复 | 新Run产生，旧Run仍FAILED |
| U20 | 设备故障 | 只提示影响/建议，不自动Pause |
| U21 | Priority Segment | 页面不出现全局PriorityScore |
| U22 | Rule发布 | 新Version生成，历史不覆盖 |

---

# 二十三、性能与交互要求

- 甘特图按时间窗和资源分页/虚拟滚动；
- 大列表服务端分页；
- Explanation按需加载；
- Candidate Diff先摘要后详情；
- 不一次加载90天所有Task到浏览器；
- 不前端计算Pegging/负荷；
- 不前端做跨Domain链式WHATIF。

---

# 二十四、明确不做

V1禁止建设：

- 可视化流程编排器；
- 任意拖拽直接改正式Task；
- MultiDomain Candidate编辑器；
- 物流有限排程界面；
- 通用审批平台；
- 通用BI平台；
- 通用规则DSL编辑器；
- Solver Trace逐步回放；
- Impact Graph图数据库可视化；
- Scenario多目标仿真平台；
- 自动设备Pause/Resume控制台。

---

# 二十五、4号位交付物

1. 页面信息架构；
2. 排产总览；
3. 订单详情；
4. 资源甘特图；
5. CTP/插单评估；
6. Candidate对比与确认；
7. Explanation/异常页；
8. PI Position/供给追溯；
9. Rule/Parameter/Strategy维护；
10. Run/Version/MES状态；
11. U01～U22测试结果；
12. 与2号位、3号位API联调记录。

---

# 二十六、完成定义

4号位完成必须同时满足：

- 不直接写APS运行库；
- 只展示FinalTask，不把LogicalProductionDemand当Task；
- Task状态值域正确；
- CTP能区分确定与Estimated；
- Candidate严格单Domain语义不被UI打破；
- 跨Domain CTP只做汇总展示；
- Candidate Base对比清楚；
- CTP/Impact Analysis不能激活；
- OA不是硬依赖；
- Explanation展示根因；
- PI Position可追溯；
- UNLOCATED/Placeholder明确不可MES；
- FAILED恢复创建新Run；
- Priority Segment按真实规则展示；
- 规则发布不覆盖历史；
- 无直接拖动正式Task写库；
- 无新增V1过度设计；
- U01～U22全部通过。

---

# 二十七、一句话交付要求

> **4号位的V1任务不是做一个“看起来很强大的APS大屏”，而是把当前ACTIVE计划、CTP/插单影响、Candidate差异、真实延期原因、PI位置、规则参数和运行状态准确呈现，并让PMC只能通过受控业务动作改变计划，绝不绕过2号位/3号位服务直接修改运行结果。**
