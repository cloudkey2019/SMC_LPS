APS V1 3号位代码第二轮复审核报告
Commit锁定版 v1.2
审核日期：2026-08-21
仓库：https://github.com/cloudkey2019/SMC_LPS
分支：main
锁定Commit：f62e141c55ae4d262c724893c439c6ae7c9673b8
父提交：d465290

报告性质：基于冻结业务/技术基线与上一轮正式审核报告的增量复审；不重新设计APS V1业务，不反向修改2号位冻结主链。
 
0. 审核范围、依据与方法
本轮以3号位上一版《APS V1 3号位代码综合符合性审核报告 v1.1（2026-08-19）》作为整改基线，对锁定Commit f62e141进行增量复核。审核优先级为：冻结业务基线 > 冻结技术文档 > 关键接口冻结 > 3号位实施包 > 上一轮审核基线 > 当前代码。
项目	内容
代码仓库	cloudkey2019/SMC_LPS
分支	main
Commit	f62e141c55ae4d262c724893c439c6ae7c9673b8
Parent	d465290
Commit事实	Public仓库；GitHub显示674 files changed，+44,240 / -0
主要审核范围	LPS.APS.Application/Services、LPS.APS.Core治理实体/DTO、LPS.APS.Tests/Unit，以及3号位边界相关代码
测试说明	本轮审查了测试源码，但当前运行环境无法直接clone并执行dotnet test，因此不把“测试文件存在”表述为“测试已实际通过”

本轮重点保护以下既定边界：
•	2号位仍是SchedulingOrchestrator和Pegging主流程Owner；3号位不得另建主排程主链。
•	3号位负责规则/参数/策略版本治理、FrozenStrategySnapshot及运行生命周期边界；2号位一次Run装载一次并在内存执行。
•	3号位不得成为逐Demand、逐Allocation的在线规则决策服务，不得建立3→5逐笔Priority/Pegging裁决接口。
•	V1不建设DSL、动态脚本、插件市场、MultiDomain Candidate等过度设计。
1. 总体结论
本轮结论：未通过3号位V1最终验收，但相比上一轮已有明显实质整改；继续增量整改，不推倒重写。
当前Commit已经把上一轮多个核心P0真正修进代码：子版本IsDefault真相已退出、发布路径强制校验、StrategyProfileVersion治理闭环明显完善、DueDate/IssueDate进入Priority Matcher、Snapshot已有来源块不再静默回退、FAILED恢复和Run追溯已经形成。
但仍存在三个正式上线阻塞面：①规则/参数版本内容的物理落点仍与冻结DDL不一致且未看到已批准的最小DDL对齐证据；②FrozenStrategySnapshot中的SolverStrategy和CandidateGuardrail仍是空对象，尚不能形成完整、可重放的Run策略真相；③Candidate确认/激活边界存在多处实质错误，包括WHATIF可被激活、未强制先确认、确认时误写Activated字段，以及正常Base ACTIVE场景会被现有同域ACTIVE检查阻断。
2. 上一轮8个P0逐项复核
编号	上一轮问题	当前状态	本轮判定
P0-01	治理版本内容落点/DDL不一致	未关闭	代码仍依赖DemandPriorityJson、LockJson、SupplyJson、ProcurementJson及额外审计字段；未看到0/2/3批准并落地的最小DDL差异证据。
P0-02	RuleSet/Parameter子版本IsDefault错误	关闭	当前RuleSetVersion/ParameterSetVersion已无IsDefault；默认真相集中在StrategyProfileVersion。
P0-03	FrozenSnapshot不完整	部分关闭	PlanningYield已进入Procurement块；SolverStrategy和CandidateGuardrail仍明确TODO并返回空对象。
P0-04	Snapshot坏配置静默回退	关闭	DemandPriority/Lock/Supply/Procurement缺失或JSON损坏均直接抛异常，不再回退空Block。
P0-05	Publish可绕过校验	关闭	RuleSet、ParameterSet、StrategyProfile正式Publish均强制先Validate。
P0-06	StrategyProfileVersion治理不完整	基本关闭	已补引用PUBLISHED校验、生效窗口、默认策略解析、歧义拒绝、Run追溯。
P0-07	Priority缺DueDate/IssueDate	关闭	Validator白名单、Matcher排序/匹配、DemandRecord字段均已补齐。
P0-08	生命周期/Candidate边界缺失	部分完成但仍P0	ExpectedDomain基础校验、FAILED新Run、Run追溯已完成；Candidate确认/激活仍存在冻结红线违背。

3. 本轮确认通过并应冻结保留的部分
PASS-01 FrozenStrategySnapshot入口方向正确
仍以StrategyProfileVersionId为唯一入口，向下解析RuleSetVersion和ParameterSetVersion；没有退回逐笔在线问规则。
PASS-02 子版本默认真相已收敛
RuleSetVersion与ParameterSetVersion当前实体不再包含IsDefault；StrategyProfileVersion才承担默认策略包选择。
PASS-03 发布门槛已强制化
RuleSet/ParameterSet/StrategyProfile的Publish方法均在改变状态前执行Validate，不再存在“可校验但可绕过”的双路径。
PASS-04 StrategyProfileVersion治理显著闭合
已经校验引用版本PUBLISHED、生效窗口、默认策略唯一性，并能按RunType解析当前默认策略包；歧义时明确报错。
PASS-05 Priority Segment主模型正确
First-match、Segment内排序、DueDate/IssueDate、稳定Tie-break方向均已进入Matcher；PriorityScore继续被Validator禁止。
PASS-06 FAILED恢复与Run追溯方向正确
RecoverFailedRunAsync只接受FAILED并新建RUNNING，不修改历史FAILED；RunReferenceTrace可追踪Strategy/Rule/Parameter版本。
4. P0：必须整改，未关闭前不得判3号位完成
P0-01 版本内容物理落点与冻结DDL仍未闭合
当前RuleSetVersion实体仍包含Remarks、UpdatedAt、UpdatedBy、DemandPriorityJson；ParameterSetVersion仍包含Remarks、UpdatedAt、UpdatedBy、LockJson、SupplyJson、ProcurementJson。FrozenStrategySnapshotProvider又直接依赖这些JSON字段恢复Run快照。上一轮已经明确：冻结v5.1.2字段说明/DDL并未给出这套字段，因此这不是可以静默由3号位自行扩表的问题。
风险：如果直接连接仍按冻结DDL建设的正式库，Repository/实体映射将失配；同时如果不落这些字段，又无法按VersionId重放已发布内容。
整改要求：
•	保留上一轮裁决：3号位先提交《Snapshot持久化来源映射表 + 最小DDL差异》，由0/2/3一次性做技术确认。
•	不得新建多套版本表、Snapshot平台或DSL；优先最小字段增量/发布内容快照。
•	确认后同步修订字段说明/DDL和代码，保证按StrategyProfileVersionId可以完整恢复当次Run的不可变内容。
本轮状态：P0，未关闭。
P0-02 FrozenStrategySnapshot仍不是完整策略真相
PlanningYield已确认进入Procurement块，这是上一轮P0-03的实质修复；但Provider源码仍明确写着：SolverStrategy暂无真实版本来源，返回new SolverStrategyBlock()；CandidateGuardrail同样返回空对象。
这意味着数据库虽然记录某个StrategyProfileVersionId，本Run实际使用的正/倒/混合策略、瓶颈、On-time目标、Split/Setup/Overlap及Candidate Guardrail并没有从该版本重放，仍可能依赖代码默认值或上游另取当前值，从而破坏“一次Run一份冻结规则真相”。
整改要求：
•	在P0-01最小物理落点确认后，把SolverStrategy和CandidateGuardrail纳入同一Parameter/Strategy版本内容。
•	正式Snapshot装载时这两块也必须执行缺失/损坏失败，不允许用空对象冒充已冻结配置。
•	测试不能只断言NotNull，必须断言具体配置值由指定VersionId重放。
本轮状态：P0，部分整改但未关闭。
P0-03 Candidate确认与激活没有阻断CTP / INSERT_IMPACT_ANALYSIS
冻结红线是INSERT_ORDER_WHATIF中的CTP和INSERT_IMPACT_ANALYSIS永远不得激活。当前RunLifecycleService的ActivateCandidateAsync只校验CANDIDATE状态、DomainKey和同域ACTIVE，源码中没有读取SourceScheduleRun对应的RunType、ScopeJson或Purpose，也没有CTP/INSERT_IMPACT_ANALYSIS拒绝逻辑。
直接后果：只要PlanVersion处于CANDIDATE且通过当前几项校验，WHATIF版本存在被转成ACTIVE的路径。
最小整改：ActivateCandidateAsync根据PlanVersion.SourceScheduleRunId读取ScheduleRun，校验RunType + ScopeJson.Purpose；CTP/INSERT_IMPACT_ANALYSIS无条件拒绝激活。
本轮状态：P0。
P0-04 激活没有强制要求“已完成最小人工确认”
代码注释写“确认后正式采用”，但ActivateCandidateAsync没有读取任何确认标志/确认审计；测试用例也直接把一个CANDIDATE调用Activate后断言变为ACTIVE，未先执行ConfirmCandidateAsync。
这与冻结的最小人工确认边界冲突。确认不要求OA，但必须成为正式采用的硬前置，而不是一个可选API。
最小整改：优先复用现有GovernanceAuditLog作为确认事实，Activate前校验存在针对该Candidate的有效ConfirmCandidate审计；无需新增审批平台。
本轮状态：P0。
P0-05 ConfirmCandidate错误写入ActivatedAt / ActivatedBy
ConfirmCandidateAsync在保持PlanVersion.Status=CANDIDATE的同时写入ActivatedAt和ActivatedBy。随后真正ActivateCandidateAsync又再次写这两个字段。
这会造成“尚未正式采用的Candidate已经有激活时间/激活人”的事实污染，UI、审计和后续接口均可能把确认误当激活。
最小整改：确认阶段只记录Actor、ConfirmedAt、CandidatePlanVersionId、Remark等确认事实；不要写ActivatedAt/ActivatedBy。若不新增字段，可完全使用现有GovernanceAuditLog承载确认事实。
本轮状态：P0。
P0-06 Candidate正常采用会被“同域已有ACTIVE”检查阻断
当前ConfirmCandidateAsync和ActivateCandidateAsync都调用EnsureNoActiveInSameDomainAsync；只要同Domain已有ACTIVE就拒绝。
但冻结业务下Candidate通常就是基于当前Base ACTIVE生成，正式采用前旧ACTIVE本来就应继续存在。因此这段检查会让标准的“Base ACTIVE → Candidate比较/确认 → 新Candidate采用”流程无法完成，除非外部先手工把旧ACTIVE处理掉。
整改要求：不要删除“每Domain只能一个ACTIVE”的数据库红线，而是把采用动作做成正确的原子替换边界：在一次受控事务/既有激活服务中将旧ACTIVE归档，再将已确认Candidate置ACTIVE。如果现有2号位已有可复用激活原子能力，3号位调用即可；不得要求2号位重写SchedulingOrchestrator。
本轮状态：P0。
5. P1：上线前应闭合，但不应借此扩架构
P1-01 ExpectedDomainKeysJson缺少重复Domain校验
当前已校验非空、合法JSON、空DomainKey、FULL至少1个、非FULL恰1个；但未看到FULL场景对重复DomainKey的Distinct校验。
整改：增加重复Domain拒绝即可，不新增表。
P1-02 R01～R22尚未形成完整验收证据
当前Unit目录已有DemandPriorityMatcher/Validator、FrozenStrategySnapshotProvider、RuleSet/ParameterSet Publish、RunLifecycle、StrategyProfile治理等7类测试文件，说明测试建设明显进步。但仍不能等同于实施包要求的R01～R22完整验收；尤其现有RunLifecycle测试反而把“未确认直接激活”和“存在Base ACTIVE则激活失败”固化为预期行为，需要随P0修复同步改写。
下一轮至少必须补/修：完整Snapshot具体值、Run中规则不漂移、PlanningYield、SolverStrategy、CandidateGuardrail、CTP/Impact永不激活、确认后才激活、Base ACTIVE原子替换、FULL重复Domain拒绝、FAILED新Run。
P1-03 README仍保留旧职责说明
仓库README仍存在把BusinessRules描述为5号位Pegging/LotSizing/Priority等历史口径，容易继续误导后续AI/开发。
整改：只做最小职责修正/顶部Deprecated说明，不重写README体系。
P1-04 测试只审源码，未在本轮环境实际执行
由于当前运行环境无法直接clone GitHub仓库，本轮无法实际执行dotnet test。本报告对测试的判断是“测试源码覆盖/断言是否正确”，不是“测试运行已通过”。
建议3号位下一次提交同时附dotnet test结果或CI流水线结果，作为R01～R22正式验收证据。
6. 本轮明确不新增、也不反向打开的问题
•	不新增任何2号位P0；2号位SchedulingOrchestrator、Pegging、Allocation主链继续以最近审核报告为保护基线。
•	Demand排序仍是3号位配置、2号位执行；DemandPriorityMatcher若作为纯函数共享实现可保留，但不得把3号位变成逐Demand RPC服务。
•	PlanningYield放在Procurement参数块的具体DTO组织形式可以接受，只要语义显式、版本可追溯并由2号位反算PlannedProcessQty。
•	不要求增加3→5运行时决策接口；5号位只接收2号位投影的FrozenFactParameters。
•	不建设DSL、脚本、RuleCondition/RuleAction通用平台、插件市场、MultiDomain Candidate。
•	ScheduleRun/PlanVersion具体创建分钟点不重新讨论，只要求冻结版本、ExpectedDomain、DataCutoffTime和状态终态闭合。
7. 3号位当前成熟度判断
能力域	状态	说明
Rule/Parameter版本发布	✅ 基本通过	正式Publish已强制Validate；版本审计方向正确
StrategyProfile治理	✅ 基本通过	引用PUBLISHED、生效窗口、默认策略、歧义与追溯已具备
Demand Priority	✅ 通过	First-match + Segment Sort + DueDate/IssueDate，禁PriorityScore
FrozenStrategySnapshot	⚠️ 部分通过	四块+PlanningYield真实；SolverStrategy/CandidateGuardrail仍为空
版本内容持久化	❌ 未通过	与冻结DDL最小落点尚未正式闭合
ExpectedDomain/FAILED恢复	✅/⚠️	主体已实现；FULL重复Domain校验待补
Candidate确认/激活	❌ 未通过	存在4个P0级生命周期错误
R01～R22测试	⚠️ 未完成	已有7类测试文件，但覆盖/断言尚不足且未实际运行

8. 下一轮整改顺序
第一批：先解决可运行真相
1.	完成P0-01最小DDL/版本内容物理落点确认并同步代码/文档。
2.	让SolverStrategy与CandidateGuardrail真正从指定StrategyProfileVersion重放，删除空对象正式路径。
第二批：修Candidate生命周期红线
3.	Activation增加RunType/Purpose校验，CTP和INSERT_IMPACT_ANALYSIS永不激活。
4.	Activate必须验证已完成最小人工确认。
5.	Confirm不再写ActivatedAt/ActivatedBy。
6.	采用动作支持Base ACTIVE到新Candidate ACTIVE的原子替换，不再因存在Base ACTIVE直接拒绝。
第三批：验证与收口
7.	ExpectedDomainKeysJson增加重复Domain拒绝。
8.	补齐R01～R22并修正现有错误生命周期测试。
9.	提供实际dotnet test/CI结果。
10.	README做最小职责去旧化。
9. 第三轮复审冻结检查清单
下一轮只重点复核以下事项；已关闭方向不重新打开：
11.	正式DDL/字段说明与RuleSetVersion/ParameterSetVersion内容快照落点一致。
12.	FrozenStrategySnapshot六块均可由指定StrategyProfileVersion重放具体值。
13.	四类必填配置损坏/缺失均失败，Solver/Candidate不再用空对象。
14.	CTP/INSERT_IMPACT_ANALYSIS无法通过任何路径激活。
15.	未确认Candidate无法激活；确认不污染Activated字段。
16.	Base ACTIVE存在时仍可完成正常Candidate确认和原子采用。
17.	ExpectedDomainKeys FULL重复值被拒绝。
18.	R01～R22测试及实际执行结果可提供。
19.	继续保持无PriorityScore、无逐笔RPC、无3→5决策接口、无DSL/插件平台。
10. 最终裁决
最终结论：当前Commit f62e141不通过3号位V1最终验收。
但本轮不是方向失败。规则/参数治理、StrategyProfile、Priority Segment、Snapshot错误处理、FAILED恢复等已经进入可用收口阶段。当前真正阻塞集中在“完整策略快照的版本可重放”与“Candidate最小确认/激活边界”。
因此继续采用增量整改：不推倒重写、不新增平台、不改变2号位主链。完成本报告P0后，再进入第三轮正式复审。
附录A：本轮关键源码证据索引
证据对象	位置	结论
Commit	GitHub commit f62e141	Public；1 parent d465290；674 files changed；+44,240/-0
FrozenStrategySnapshotProvider.cs	L590-L628	PlanningYield已进入Procurement；SolverStrategy/CandidateGuardrail明确TODO且返回空对象
FrozenStrategySnapshotProvider.cs	L635以后	DemandPriority/Lock/Supply/Procurement缺失或JSON损坏直接失败
RuleSetVersion.cs	实体字段	仍含Remarks/UpdatedAt/UpdatedBy/DemandPriorityJson
ParameterSetVersion.cs	实体字段	仍含Remarks/UpdatedAt/UpdatedBy/LockJson/SupplyJson/ProcurementJson
GovernanceVersionService.cs	Publish方法	RuleSet/Parameter/Strategy正式Publish均强制Validate
GovernanceVersionService.cs	P0-06区段	Strategy引用PUBLISHED、生效窗口、默认策略、歧义拒绝、Run追溯
DemandPriorityMatcher.cs	First-match/Sort区段	DueDate/IssueDate已进入排序和DemandRecord；First-match模型正确
RunLifecycleService.cs	L960-L1107	Confirm误写Activated字段；Activate无确认前置；同域ACTIVE直接拒绝
RunLifecycleService.cs	全文搜索	无ScopeJson/Purpose/INSERT_IMPACT_ANALYSIS激活拦截
RunLifecycleService.cs	L1111以后	FAILED恢复新建RUNNING，不修改旧FAILED；Run追溯已实现
RunLifecycleServiceTests.cs	Activate测试	测试允许未确认直接激活，并把同域已有ACTIVE定义为失败
LPS.APS.Tests/Unit	目录	现有7类测试文件，尚未形成R01～R22完整执行证据

