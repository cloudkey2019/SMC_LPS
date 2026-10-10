# APS V1 当前有效冻结规则清单 v1.6（20261009 Batch Policy与MTS PI数量边界控制层收口候选版）

**版本**：v1.6  
**日期**：2026-10-09（北京时间）  
**定位**：APS V1当前有效业务与技术规则控制清单  
**状态**：2026-10-09控制层收口候选提交记录。正式切换前，已生效规则清单为v1.5、唯一索引为v1.11；本规则清单v1.6仅在指导位控制层一致性审核通过且0号位正式宣布本轮生效后转为当前有效，届时v1.5转历史追溯。提交日期状态不构成未来永久状态判断。

> 本清单是在既有控制清单上做收口更新，不删除本轮未受影响的既有有效主题。  
> 本轮新增/覆盖规则的正式来源只允许指向已通过正文审核的本轮正式正文候选；控制层生效前其正式身份仍由v1.11决定。讨论稿、修改指导、审核报告与独立技术核实任务不作为业务规则正式来源。

## 一、权威、治理与总体原则

|规则ID|当前有效规则|Owner|正式来源/状态|
|---|---|---|---|
|G-001|权威优先级：0号位最新明确裁决 > 当前最终业务基线 > 当前正式专项/核心流程 > 当前技术契约 > 当前实施包 > 历史文档/代码注释|0|业务基线v1.10|
|G-002|冻结变更必须先完成业务/技术正文修改与一致性审核，再更新控制层；代码整改最后进行，代码不得反向修改冻结业务|0|冻结治理规则；本轮正文Step 9已通过|
|G-003|保护2号位既有Demand/Supply/Pegging/Allocation/PlanVersion/ScheduleRun主链，以增量闭环为原则，不重建第二套运行平台|0/2|业务基线v1.10、2号位v2.1|
|G-004|V1采用订单驱动拉取、契约视图、本地快照；不全量搬运历史业务模型|0/2/5|业务基线v1.10、数据架构v1.45|
|G-005|不得新增TaskHeader、OperationTask第二平台、Continuation永久表、ExecutionBatch永久状态机、FinalMaterial、MaterialVariant、MaterialStateMaster、RoutingChoicePlatform（第二Routing选择平台）、第二Routing平台、第二Pegging、第二Solver、MultiDomain Candidate|0|业务基线v1.10、数据架构v1.45、DDL v5.1.8.4|

## 二、0～5号位职责与数据库边界

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|R-001|0号位负责项目总体业务裁决、范围控制和最终冻结确认|0|业务基线v1.10、0号位v1.7|
|R-002|1号位负责有限产能求解与FinalTask生成：Resource、Start/End、Direction、C桶Batch、Routing选择、Setup、TaskDependency、Explanation；零APS DB I/O、零MES直读|1|业务基线v1.10、有限产能v1.8、1号位v1.7|
|R-003|2号位负责运行主流程、Demand/Supply/Pegging/Allocation/Q、A/B/C、ContinuationKey、Solver调用、FinalTask结果校验/持久化、TaskNo/MES身份闭环|2|业务基线v1.10、2号位v2.1、接口v1.35|
|R-004|3号位负责Auth/RBAC/Scope、Rule/Parameter/Strategy、Batch Policy、Direction治理、StageLeadTimeParam与Run/Candidate治理|3|3号位v1.9、字段v5.1.10|
|R-005|4号位负责页面、用户操作入口和展示；不计算Pegging/Solver/权限真相，不直接写核心运行DB|4|4号位v1.6|
|R-006|5号位负责ODS、PI Position、MES执行事实、多Slice、Routing回接、LastReportResourceCode与查询接口；不负责Pegging/Q/A-B-C/TaskNo/Solver/StageLeadTimeParam治理|5|5号位v1.7、字段v5.1.10|

## 三、ScheduleRun / PlanVersion / Domain / Candidate

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|RUN-001|夜间FULL按当前有效Domain集合串行执行，每个Domain独立PlanVersion|2/3|业务基线v1.10；既有Domain正式裁决继续有效|
|RUN-002|白天Candidate严格单Domain，不建设MultiDomain Candidate|2/3|业务基线v1.10；既有Domain正式裁决继续有效|
|RUN-003|DomainKey是真实业务域标识，不等同ProductFamily或技术ID|2/3|业务基线v1.10；Domain正式裁决v1.0|
|RUN-004|共享资源场景下，其它Domain ACTIVE占用对当前Candidate作为不可移动阻挡块|1/2|有限产能v1.8|

## 四、Demand / Supply / Pegging / DemandGoal

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|P-001|SO/MTO→SALES_ORDER；MTS/SS/SS_U→PRODUCTION_INSTRUCTION|2|业务基线v1.10|
|P-002|Pegging由2号位负责，只回答供需承接/数量/缺口，不决定最终Resource/Task/时间|2|业务基线v1.10、Pegging v1.6|
|P-003|Supply正式顺序保持：库存→已到厂未入库→采购在途→未结采购剩余→不足建议/Planning-only Placeholder|2|业务基线v1.10、Pegging v1.6|
|P-004|PI Position是已有Supply内部执行位置事实，不是独立Supply|5/2|业务基线v1.10、Supply/PI Position正式专项v1.0|
|P-005|对象类型、Demand/Supply角色、DemandGoal三者分离|2|Pegging v1.6|
|P-006|V1 DemandGoal包含CUSTOMER_COMMITMENT与INVENTORY_REPLENISHMENT|2|Pegging v1.6|
|P-007|Pegging先形成逻辑生产需求Q，再结合MES执行事实识别A/B/C；执行事实不得反向扩大Q|2|业务基线v1.10、Pegging v1.6|
|P-008|Pegging传播Quantity、RequiredAvailableTime/NeedBy、DemandGoal、Demand Protection、DemandSequence/血缘；不传播最终Direction、Batch、Resource、TaskNo、Routing选择|2|Pegging v1.6|
|P-009|Planning-only Purchase Placeholder仅运行内存存在，不建表、不下采购单、不构成正式供应承诺|2|业务基线v1.10、Pegging v1.6|

## 五、MES事实 / PI Position / 执行回接

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|E-001|5号位负责MES原始事实标准化、PI Position、NextOperation/执行位置回接；2号位消费标准化事实|5/2|业务基线v1.10、5号位v1.7|
|E-002|LastReportResourceCode属于Operation级事实，不提升为MES工单级唯一设备真相|5|5号位v1.7、字段v5.1.10|
|E-003|ExistingExecution按MESWorkOrder执行上下文表达，一个WO可有1..N Slices|5/2|接口v1.35、5号位v1.7|
|E-004|人工MES工单只有在当前Routing唯一匹配时才回接APS OperationCode；不唯一则Issue + Stage/Dept + StageLeadTime降级，不猜DEFAULT、不伪造Operation|5|5号位v1.7、核心走查V3.27|

## 六、A/B/C与跨版本连续性

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|C-001|A=已有APS下发/在制且有可继承TaskNo；B=外部MES执行份额、无旧APS TaskNo；C=未下发未开工自由份额|2|业务基线v1.10、2号位v2.1|
|C-002|一个ScheduleRun内一个MESWorkOrderNo一个且仅一个ContinuationKey；同WO的1..N Slices共享该Key|2|接口v1.35、2号位v2.1|
|C-003|ContinuationKey不得按LogicalDemandKey裂分，不新增Continuation永久表|2|数据架构v1.45、2号位v2.1|
|C-004|A/B已有真实Execution Batch，不参加普通自由拆/合批；C才做动态Batch Decision|1/2|有限产能v1.8|
|C-005|E>Q时ContinuationQty=Q、FreeQty=0、Overcommit=E-Q，不扩大Q|2|核心走查V3.27|

## 七、Task / TaskNo / MES工单 / Execution Batch

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|T-001|Task.Id是一条Operation级PlanVersion排程结果记录|1/2|业务基线v1.10|
|T-002|TaskNo是Stage内MES执行批的APS跨版本业务身份；一个TaskNo可对应N条Operation Task|2|业务基线v1.10、2号位v2.1|
|T-003|一个TaskNo绑定一个MESWorkOrderNo；MES工单不跨Stage|2|接口v1.35、字段v5.1.10|
|T-004|A继续旧TaskNo/原WO；B Solver前TaskNo=NULL，FinalTask后2号位生成新TaskNo绑定原WO；只有C新建MES工单|2|业务基线v1.10、2号位v2.1、接口v1.35|
|T-005|ExecutionBatchDraftKey是Solver归组键，不是TaskNo，不建设永久ExecutionBatch平台|1/2|接口v1.35、数据架构v1.45|
|T-006|FinalTask由1号位生成；2号位只负责结果校验、持久化与TaskNo/MES身份闭环|1/2|0号位v1.7、1号位v1.7、2号位v2.1、接口v1.35|

## 八、Batch与Direction

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|B-001|正式有效Batch Policy按MaterialId + 明确ProductionDepartmentId精确匹配；不把历史NULL部门记录泛化为各部门规则；有效期重叠须治理排歧|3/2|业务基线v1.10、字段v5.1.10、3号位v1.9|
|B-002|Min/Max及AllowSplit/AllowMerge按其既有合法性语义约束C桶批量；PreferredBatchQty为履约优先下的经济批量软偏好，不是硬固定数量；硬合法性不受优化预算耗尽影响|1/2/3|有限产能v1.8、字段v5.1.10、1号位v1.7|
|B-003|A/B既有MES执行批不参与普通拆合；C桶由1号位在Stage Execution Batch层级作Batch Decision，同批多Operation级Task共享批身份/TaskNo，不按Operation重复生成工单|1/2|业务基线v1.10、有限产能v1.8、2号位v2.1|
|B-004|Direction支持AUTO/FORWARD/BACKWARD/MIXED；OrderType不得直接决定Direction|1/3|业务基线v1.10、有限产能v1.8|
|B-005|Direction由DemandGoal、RequiredAvailableTime、Slack、Material、Resource、Execution、Firm/Frozen/Lock等上下文综合决定|1|有限产能v1.8|
|B-006|拟新增、启用、正式发布或进入有效期的业务Batch Policy规则：MaterialId、明确ProductionDepartmentId、PreferredBatchQty业务必填，Preferred>0且落在已配置有效硬Min/Max之内；由3号位后端校验并由授权车间业务人员维护；原DDL字段物理Nullable仅为历史兼容|3/4/2|字段v5.1.10、3号位v1.9、4号位v1.6、DDL v5.1.8.4|
|B-007|MaxOptimizationSplitCount、MaxBatchCandidates是1号位Solver有界搜索技术预算，不再按Material+部门由车间维护；有效值与运行版本应单源可追溯，旧TaskSplitRuleConfig历史预算列不作为当前主链预算输入；经验值3/8不是统一业务上限|1/2/3|业务基线v1.10、有限产能v1.8、字段v5.1.10|
|B-008|BottleneckSplitStrategy与NonBottleneckStrategy两列物理保留用于历史兼容；即使历史值非NULL亦不作为V1当前拆合批偏好/硬约束或Solver排序输入，不覆盖既有动态瓶颈与锚点策略|1/2/3|字段v5.1.10、有限产能v1.8、1号位v1.7|
|B-009|明确且唯一归属MTS PI的C桶新Stage执行批，运行上限为适用的配置Max、PI原始数量、ERP当前PI剩余总量，以及依据PI Position/执行位置确认的当前Stage未占用合法自由量四者中的较严值；同PI同Stage多新增批累计不得超可用量；不同Operation不得重复扣减；不得改变Q/PI Position或回写物料级Max|1/2/5|业务基线v1.10、有限产能v1.8、接口v1.35、核心走查V3.27|
|B-010|仅在明确MTS PI且合法C桶份额完全无匹配有效Batch Policy时，按本轮Q_C保守组织一个Stage执行批候选，并以当前PI/Stage合法自由余额约束；不伪造物料级Min/Max/Preferred，不额外优化拆合，超出合法可用量须显式记录未满足，不扩大/静默截断Q；缺配须可追溯；无明确PI来源不适用该兜底；已有配置无效不得冒充无匹配|1/2/3/4|业务基线v1.10、接口v1.35、核心走查V3.27、0号位v1.7|

## 九、BOM / Stage / Routing / StageLeadTime

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|S-001|BOM只拉当前计划相关Workset，不全量搬运历史BOM|5/2|业务基线v1.10、数据架构v1.45|
|S-002|Operation完整身份至少包含Material + ProductionDepartment + RouteCode + PathId + StageCode + OperationCode；OperationCode不是全局唯一键|1/2|字段v5.1.10、接口v1.35|
|S-003|RoutingDependency、Eligibility、CapacityFactor必须保持PathId隔离，不得跨Path串接|1/2|字段v5.1.10、核心走查V3.27|
|S-004|StageLeadTimeParam治理/维护Owner=3号位；2号位装载/匹配；1号位消费；5号位只提供BOM/Stage/ODS事实|1/2/3/5|字段v5.1.10、3号位v1.9|
|S-005|StageLeadTime正常匹配链=DEPT_EXACT → FACTORY_STAGE_DEFAULT → GLOBAL_STAGE_DEFAULT；Material/ProductFamily只历史兼容|2/3|字段v5.1.10|
|S-006|无Routing Stage形成StageTimingNode，只传播时间；不生成Task/TaskNo/MES工单；缺参报STAGE_LEADTIME_MISSING|1/2|核心走查V3.27、字段v5.1.10|

## 十、V1多Routing

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|RT-001|V1正式支持多个业务合法RouteCode + PathId；DEFAULT/Path1不得作为业务真值默认|1/2/3/5|业务基线v1.10、有限产能v1.8|
|RT-002|A/B固定真实RouteCode + PathId，局部修复不得换路径|1/2|1号位v1.7、接口v1.35|
|RT-003|C桶由2号位提供1..N合法RoutingCandidates，1号位按Execution Batch选择一条完整RouteCode + PathId|1/2|1号位v1.7、2号位v2.1、接口v1.35|
|RT-004|单个ExecutionBatchDraftKey只能使用一条完整Path，不得跨Route/Path拼Operation|1|核心走查V3.27|
|RT-005|APS创建MES工单必须保留实际RouteCode/PathId/Operation身份，供后续5号位执行回接|2/5|接口v1.35、5号位v1.7|

## 十一、PreferredResource

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|PR-001|5号位保留Operation级LastReportResourceCode|5|5号位v1.7|
|PR-002|2号位关联当前Continuation Slice并按当前Operation Eligibility校验；合法可靠才形成PreferredResourceCode，否则NULL|2|2号位v2.1、接口v1.35|
|PR-003|1号位把PreferredResourceCode作为软偏好，不是Hard Lock，不得突破Eligibility/Calendar/Firm/Frozen/Material/Routing硬约束|1|1号位v1.7、有限产能v1.8|

## 十二、FINAL Material标准化

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|F-001|普通物料保持FG_/WIP_/RAW_；明确出口库/最终可销售层级标准化为FINAL_FG_/FINAL_WIP_/FINAL_RAW_并形成独立APS MaterialId|2/5|业务基线v1.10、数据架构v1.45、字段v5.1.10|
|F-002|FINAL_不是新MaterialType；先Strip FINAL_，再按FG/WIP/RAW解析原MaterialType|2/5|字段v5.1.10|
|F-003|一般Warehouse差异不自动拆Material；FINAL只用于既有裁决明确的出口/最终销售层级|2/5|数据架构v1.45|
|F-004|ERP_Master_View、MES_Material_View及Routing Operation/Dependency/Eligibility/Stage向APS使用标准化MaterialCode；MES_ID/Model只作追溯/校验|2/5|字段v5.1.10、接口v1.35|
|F-005|2号位按标准化MaterialCode → MaterialMapping → MaterialId绑定Routing，不得从Routing/Operation/Stage反向猜FINAL|2|2号位v2.1、字段v5.1.10|
|F-006|5号位不得根据包装工序、Routing长短或Operation名称推断FINAL|5|5号位v1.7|
|F-007|FINAL不新增BOM层，不自动生成FINAL_xxx→xxx虚构BOM；真实源BOM边正常同步|2/5|业务基线v1.10、核心走查V3.27|

## 十三、Resource Calendar与人工有限产能（本轮未重新打开）

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|RC-001|Resource Calendar业务事实维护边界保持既有冻结；设备与人工有限产能均由2号位建模装载给1号位消费|5/2/1|业务基线v1.10；`APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版(1).md`|
|RC-002|人工槽按生产部门+小工序+槽位维护，不使用真实人名；V1不做跨工序人工Eligibility|5/4|业务基线v1.10；`APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版(1).md`|
|RC-003|日历修改不自动触发重排，需人工发起Candidate|4/3/2|业务基线v1.10|
|RC-004|无设备且无限产能工序不建资源；有限人工工序才建能力槽|1/2/5|业务基线v1.10、有限产能v1.8|

## 十四、Firm / Frozen / Lock / Candidate

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|L-001|Firm/Frozen保护Task移动边界；Demand Protection/Strict Binding保护供需份额，语义不得混同|2/3|业务基线v1.10、有限产能v1.8|
|L-002|业务Lock由2号位/上游形成并传给1号位；1号位执行硬约束但不创造业务锁|2/1|有限产能v1.8|
|L-003|EXPEDITE可提高优先，但不得突破Firm/Frozen/Strict Binding/Material AvailableTime等硬约束|1/2/3|业务基线v1.10、有限产能v1.8|

## 十五、Setup（本轮未重新打开）

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|ST-001|Setup是真实Resource时间占用，不是简单排序分数；由1号位纳入有限产能求解|1/3|业务基线v1.10、有限产能v1.8|
|ST-002|Setup规则/参数由3号位治理，2号位装载，1号位求解；不生成Setup Task第二平台|1/2/3|业务基线v1.10、有限产能v1.8|

## 十六、跨厂 / SH / PI Position（本轮未重新打开）

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|X-001|跨厂继续区分大工艺接续型与厂间订单型|2/5|业务基线v1.10、Pegging v1.6|
|X-002|SH保持一张SH一个Supply与整单状态判断；ZP/BP/Received只作物流/数据质量追溯，不切Supply|2/5|业务基线v1.10、Pegging v1.6|
|X-003|INTERPLANT_IN_TRANSIT不进入SupplyFact_Pipeline；PI跨厂在途只属于PI Position|2/5|业务基线v1.10、Pegging v1.6|

## 十七、用户输出与Explanation（本轮未重新打开）

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|U-001|Explanation事实链：1号位产生 → 2号位保存 → 5号位查询 → 4号位展示；查询层不重算原因|1/2/5/4|业务基线v1.10、0号位v1.7、4号位v1.6、5号位v1.7|
|U-002|V1核心输出继续包括订单交付/延期原因、版本追溯、甘特/任务清单、设备负荷、换型相关指标及Candidate影响|1/2/4/5|业务基线v1.10、0号位v1.7|

## 十八、Auth / RBAC / Scope（本轮未重新打开）

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|A-001|APS_Auth完整归3号位；V1支持Authentication、RBAC、Business Scope、Audit|3|权限与审批设计方案v1.1、Auth字段/DDL v1.1|
|A-002|4号位不复制权限真相，页面操作必须经过正式权限/业务校验链|3/4|权限与审批设计方案v1.1、4号位v1.6|

## 十九、接口、字段、DDL与版本维护

|规则ID|当前有效规则|Owner|正式来源|
|---|---|---|---|
|DB-001|业务、接口、字段、DDL必须一致；数据库结构不得反向定义业务|0/2/3/5|业务基线v1.10、数据架构v1.45、接口v1.35、字段v5.1.10、DDL v5.1.8.4|
|DB-002|Task持久化真实ProductionDepartment/Stage/RouteCode/PathId；TaskNo/MES绑定只承载身份，不复制MES状态机|2|字段v5.1.10、DDL v5.1.8.4|
|DB-003|TaskSplitRuleConfig原表增量承载Batch Policy，不新建第二BatchPolicy平台|3|字段v5.1.10、DDL v5.1.8.4|

## 二十、维护规则

1. 只有已进入正式正文并通过一致性审核的规则，才能进入本清单。  
2. 旧规则被覆盖时，必须同步登记《废止/覆盖规则清单》。  
3. 本轮修改指导、审核报告、过程差异材料和独立技术核实闭环任务不作为正式冻结规则来源。  
4. 本轮当前待0号位最小技术裁决项：**无**。  
5. 本次勘误仅同步3号位正式来源版本引用；待指导位定向审核与0号位确认后，随新的唯一冻结基线索引一并切换。
