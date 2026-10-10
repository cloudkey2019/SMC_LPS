# APS 三份冻结业务 → 六份权威技术文档 差异与修改矩阵 v1.0

**日期**：2026-08-12  
**性质**：冻结业务向技术文档收口的实施矩阵；**不是业务重新评审文档**。  
**最高依据**：
1. 《APS V1最终全部流程与业务基线_v1.0_20260812》
2. 《APS Pegging供需承接与分层计算业务说明_v1.1_冻结对齐版》
3. 《APS有限产能排产与滚动90天计划业务说明_v1.1_冻结对齐版》

**本轮目标文档**：
1. 《APS 核心排产全流程走查（完整版）》V3.16
2. 《APS 各类基础数据分层承接与演变总表》v3.31
3. 《APS 数据架构与防腐层设计方案》v1.35
4. 《APS 集成接口设计规范》v1.25
5. 《APS 数据库表结构字段说明文档》v5.1.1
6. 《APS 数据库表结构设计 DDL》v5.1.1

---

## 0. 防漂移规则

本矩阵只允许把六份技术文档向已冻结业务收敛，不允许借修文档重新设计业务。

处理规则：

- **K 保留**：与冻结业务一致，不改业务语义；
- **M 修改**：当前技术文档与冻结业务冲突，改成冻结口径；
- **A 新增**：冻结业务已有、当前文档缺失，补入；
- **D 删除/退出V1正文**：旧设计不再属于V1正式主链；如需追溯，只留“历史已废止”说明；
- **H 历史保留**：只保留版本追溯，不得作为当前实现依据；
- **P 保护区**：当前冻结业务没有要求改变，不得顺手重构。

任何人如果认为必须改变已冻结业务，必须单独提出“**重新打开冻结决策 F-xxx**”，不得在技术文档修订中静默改变。

---

## 1. 总体结论

当前六份文档的基础架构仍可复用，**不需要推倒重写**。主要问题集中在2026-07-20之后继续收敛出的新冻结口径尚未回写：

- Pegging仍残留全局PriorityScore、5号位运行时插件/Voucher、2号位提前Task化；
- 采购/VMI/Received/Pipeline仍被写成V1空链路；
- 跨厂仍残留ShippingTask有限物流和错误供给顺序；
- 90天仍残留“远期LeadTime粗排/ROUGH_CUT”语义；
- Candidate仍残留“Base ACTIVE全部Allocation不可重用”“Scope不得扩大”“MaxImpactedOrders硬截断”；
- Candidate采用仍被完整OA审批体系绑死；
- Demand Protection、PI Position、计划良率、AllocationTaskShare等冻结能力尚未进入最小数据模型；
- 夜间跨Domain失败仍缺“阻断下游本次发布 + 新ScheduleRun人工恢复”完整闭环；
- Explanation、Firm/Frozen跨版本、白天跨Domain共享资源仍需按冻结口径重写。

这些属于**技术文档同步问题**，不是新的业务未决项。

---

## 2. 横向差异矩阵

符号顺序：**走查 / 演变总表 / 防腐层 / 接口 / 字段 / DDL**。

| ID | 冻结业务主题 | 当前主要冲突/缺失 | 六文档动作 | 优先级 | 2号位代码影响 |
|---|---|---|---|---|---|
| F-001 | 三份冻结业务为上位标准 | 六份文档仍互相引用2026-07-20旧版本为最高依据 | A/A/A/A/A/A：每份首页增加“冻结业务上位声明” | P0 | 无代码影响 |
| F-002 | 客户SALES_ORDER进入APS已是ERP成品库存处理后的需生产量 | 旧流程容易让人继续在APS顶层搜索成品库存 | M/M/M/M/M/—：顶层SALES_ORDER明确禁止二次扣成品库存 | P0 | Pegging入口校验 |
| F-003 | PI剩余总量以ERP `Quantity-ReceivedQty`边界为准 | 旧进度链容易把MES/XC/在途再叠加成PI总量 | M/M/M/M/M/必要字段注释：MES/XC/在途只解释位置，不重算PI总量 | P0 | PI总量闭合 |
| F-004 | Demand排序=计算层→有序规则段→第一命中→段内排序 | Order仍有PriorityScore，走查还有InheritedPriority/PriorityScore | M/M/M/M/M/M：PriorityScore退出当前V1业务权威；可物理兼容但不得驱动主链 | P0 | 2号位排序执行改造 |
| F-005 | 3号位治理规则，2号位执行排序/Allocation，5号位只做复杂事实 | 当前大量“5号位插件实时裁决Pegging/拆批/延期” | M/M/M/M/M/注释：重写0～5号位职责 | P0 | 保护2号位Orchestrator，移除5号位逐笔回调 |
| F-006 | 2号位只形成逻辑生产需求；FinalTask由1号位生成 | 走查/总表/防腐/接口均写2号位Task/ShippingTask实例化 | M/M/M/M/M/M：正式主链改为“逻辑生产需求→1号位FinalTask” | P0 | 退出提前Task路径 |
| F-007 | Task与Demand多对多，允许多需求合并为一个FinalTask | Task.OrderId NOT NULL；当前无AllocationTaskShare物理表 | M/M/M/M/A/A：Task.OrderId降为非权威/可空兼容；新增最小AllocationTaskShare | P0 | 持久化从一对一改多对多 |
| F-008 | `Task.Quantity`=净合格产出，`PlannedProcessQty`=产能加工量 | 当前只有Task.Quantity | A/A/A/A/A/A：加入PlannedProcessQty及闭合说明 | P0 | Solver输入输出与持久化 |
| F-009 | V1正式支持计划良率 | 六份旧文档主链未形成NetRequiredQty/PlannedProcessQty反算 | A/A/A/A/A/A：2号位反算、1号位占能力；PI Supply不膨胀 | P0 | 新增数量反算 |
| F-010 | 当前不假设BOM存在ScrapRate参与V1反算 | 易与未来BOM材料损耗混用 | A/A/A/A/M/M注释：计划良率≠BOM材料损耗 | P1 | 防重复放大量 |
| F-011 | 90天只有一套正式有限产能Solver | 防腐层仍有8～90天LeadTime占位粗排；字段ComputeMode含ROUGH_CUT | M/M/M/M/M/M：V1不按近远期切两套语义；ROUGH_CUT不得成为V1主链模式 | P0 | 1号位单Solver |
| F-012 | 近远期只允许优化深度不同 | 旧“近7天严格、8～90天粗排”与冻结冲突 | M/M/M/M/M/注释：远期同样占真实资源；只有明确简化Stage使用StageLeadTime | P0 | Solver搜索深度参数化 |
| F-013 | 路由缺失不是按日期自动变粗排 | 防腐层按due date区分“近7天报错/远期LeadTime占位” | M/M/M/M/M/—：改为统一降级规则，不以日期自动切Solver语义 | P1 | 1号位fallback清理 |
| F-014 | 动态瓶颈+混合正倒排+五阶段Solver | 旧文档对真实Solver能力描述不完整 | A/A/A/A/A/必要参数：补正式算法边界，不建设命令链 | P1 | 1号位实现主线 |
| F-015 | FinalTask拆/合批属于1号位 | 当前TaskSplitRule/TaskSplitVoucher由5号位+2号位提前拆批 | M/M/M/M/M/M：主题参数可保留，执行权移1号位；Voucher退出正式主链 | P0 | DefaultBatchSplitter退出 |
| F-016 | Pegging要有Demand/Supply双边余额与原子Allocation | 当前更多围绕Task Pegging，缺DemandBalance/逻辑Ledger | A/A/A/A/A/最小化A：补运行时余额、Allocation与闭合；不强制新大Ledger平台 | P0 | 2号位核心补齐 |
| F-017 | PSA仅内部非Task供给Allocation镜像，不是第二真相源 | 当前文档定位部分正确，但DDL实际缺少完整最小对象/代码有重复写风险 | M/M/M/M/M/A：保留极简PSA，统一事务，一次写入；无独立生命周期 | P1 | 删除事务外重复写 |
| F-018 | PI必须先选PI，再消费PI内部Position | 当前跨厂/在制逻辑没有完整Position主链 | A/A/A/A/A/A：补PI选择→Position消费 | P0 | 5号位Position服务+2号位Allocation |
| F-019 | PI Position一张最小快照表，多行互斥闭合 | 当前DDL无正式Position表；旧方案曾有Header/Slice倾向 | A/A/A/A/A/A：只建一张最小PI位置快照，不建Header+Slice平台 | P0 | 5号位输出、2号位保存 |
| F-020 | PI Position异常：强事实修正/UNLOCATED/保守排程 | 当前缺互斥闭合及UNLOCATED规则 | A/A/A/A/A/A：补正常、缺Stage、上下游矛盾、强事实超量规则 | P0 | PI calculator |
| F-021 | PI不能消费自己 | 当前六文档缺明确自循环红线 | A/A/A/A/A/校验：同PI派生Demand不得再由同一PI Supply承接 | P0 | Allocation安全校验 |
| F-022 | StageProgress由5号位用于PI位置，2号位消费Position，1号位只收已解析起点 | 字段/总表/防腐仍写“1号位按StageProgress扣减Task” | M/M/M/M/M/—：消除两套PI位置真相 | P0 | 调整消费链，不废除快照 |
| F-023 | 采购、VMI、已到厂未入库、厂间在途正式进入V1 | 六文档大量写PipelineSupplies V1空、WHERE 1=0、V1.1/V2 | M/M/M/M/M/M：全部升级为V1正式供给事实 | P0 | 5号位真实ODS + 2号位装载 |
| F-024 | PO/VMI ETA：人工ETA→ERP ETA→DefaultLT | 当前仅有部分ETA/AvailableTime骨架 | A/A/A/A/A/A/参数：补统一优先级和估算标记 | P0 | 2/3/5接口 |
| F-025 | 无正式PO/VMI时可用Planning-only Purchase Placeholder | 当前只有PurchaseSuggestionDraft，没有“让90天计划不断链”的供给占位 | A/A/A/A/A/—：仅内存，不建表/不下ERP/不生成Task，标ESTIMATED/NOT_COMMITTED | P0 | ScheduleContext新增运行时供给类型 |
| F-025A | 采购/供应商参考需用日期应来自实际消费Task计划时间 | 旧流程存在按Task起点/固定LT简单倒推但未与最终90天统一Solver闭合 | A/A/A/A/A/—：排程完成后由真正消费节点时间回推采购参考日期；仍不自动生成正式采购单 | P1 | 2号位汇总1号位结果形成PurchaseSuggestionDraft |
| F-026 | STAGE_HANDOFF顺序=目标M→PI→PI Position→新增生产 | 走查仍是“XC→M→在途→上游M→新增生产” | M/M/M/M/M/—：重写供给顺序 | P0 | 2号位Pegging逻辑 |
| F-027 | INTER_FACTORY_ORDER顺序=目标BS/KS→SH；SH内在途→同SH Received→未生产 | 当前V1将Received/Pipeline为空且目标顺序不完整 | M/M/M/M/M/M：正式纳入V1并按SH号防重复 | P0 | 2号位分层Pegging |
| F-028 | 跨厂物流永久不进入当前V1有限物流Task体系 | 走查大量ShippingTask、物流状态、MES发货Task；DDL/字段有ShippingTask生成器注释 | D/D/D/D/D/D或H：ShippingTask从V1排程主链退出；运输只形成LeadTime→AvailableTime | P0 | 删除/旁路ShippingTask主链 |
| F-029 | `LOGISTICS_DELAY`可作为Explanation原因，但不代表有限物流Task | 当前原因事实绑定ShippingTask较多 | M/M/M/M/M/—：绑定业务AvailableTime/ETA/运输事实，不要求Task | P1 | Explanation证据对象调整 |
| F-030 | 跨Domain供给必须保留Quantity-Time分段 | 当前常用汇总Virtual Inventory + 一个AvailableTime | A/A/A/A/A/—：40@15日、60@17日必须保持两段 | P0 | Context结构支持多切片 |
| F-031 | 跨Domain运输LT使用真实工厂/Stage LT，不统一硬编码2天 | Domain_Dependency.DefaultLeadTimeDays默认2天 | M/M/M/M/M/M：不再以“2天”作业务权威；若字段为兼容缓存，值须源自真实LT | P0 | 兼容字段可保留，语义改变 |
| F-032 | 夜间Domain按拓扑；上游失败则依赖下游本次不发布新ACTIVE | 当前只写Mismatch Risk+人工建议，下游仍可能独立发布 | M/M/M/M/M/必要校验：增加依赖链发布门禁 | P0 | 2/3号位编排 |
| F-033 | 无关Domain仍独立发布，ScheduleRun可PARTIAL_SUCCESS | 当前这部分已基本正确 | K/K/K/K/K/K | P0 | 保护，不回退ALL_OR_NOTHING |
| F-034 | FAILED后人工重算必须新建ScheduleRun，原FAILED历史不改 | 当前缺完整手工恢复闭环 | A/A/A/A/A/必要字段审计：按依赖链自动补上下游，拓扑重算 | P0 | 复用FULL主链，不建Retry平台 |
| F-035 | Candidate Base ACTIVE不修改，新建单Domain PlanVersion | 当前基本正确 | K/K/K/K/K/K | P0 | 保护现有版本壳 |
| F-035A | 夜间FULL每次按最新优先规则重新Pegging，普通旧Allocation无跨版本稳定性偏好 | 旧文档存在优先级继承/既有关系延续倾向，容易把昨天普通关系当保护 | M/M/M/M/M/—：只有严格绑定、Demand Protection、不可逆执行份额保留；普通关系重新竞争 | P0 | 夜间Pegging不得沿用普通旧关系 |
| F-036 | Candidate普通未锁Allocation可释放重新竞争 | 当前明确“Base供给-Base全部已分配；ACTIVE占用不得重用” | M/M/M/M/M/—：改为当前物理供给-已消耗-严格绑定-保护锁-不可逆-失效 | P0 | `BuildRemainingSupplyContextAsync`语义改造 |
| F-037 | Candidate影响范围由1号位真实依赖动态传播 | 当前ScopeJson被写成唯一运行范围，不得扩大 | M/M/M/M/M/—：Scope只表示初始请求/业务边界，不能截断物理传播 | P0 | 1号位Dirty Queue |
| F-038 | `MaxImpactedOrders`只可作警戒/提示阈值 | 当前达到上限必须停止扩大Scope | M/M/M/M/M/—：删除硬停止语义 | P0 | 移除硬截断 |
| F-039 | PlanHorizonStart/End是初始调整范围，不是影响硬边界 | 当前Scope唯一权威易形成硬截断 | M/M/M/M/M/—：实际受影响范围由Candidate/Base差异得出 | P1 | 局部传播可越界 |
| F-040 | 局部传播过大时，同一Solver升级为单Domain完整可移动重排 | 当前缺明确fallback或可能依赖MANUAL_RESCHEDULE另一路径 | A/A/A/A/A/—：不建第二LocalSolver | P1 | 1号位fallback |
| F-041 | 白天单Domain Candidate遇跨Domain共享设备：其它Domain ACTIVE为不可移动阻挡块 | 当前六文档未完整冻结 | A/A/A/A/A/—：不建共享资源配额/借用/跨域传播 | P0 | ScheduleContext载入外域占用只读块 |
| F-041A | 夜间FULL即使Domain独立版本，也必须遵守跨Domain共享资源真实互斥，不能双占同一设备时间 | 当前“分Domain独立计算/发布”若被简单并行实现，会漏共享资源现实 | A/A/A/A/A/—：夜间同一Run必须把共享资源占用作为真实约束传递；具体实现不得扩成资源配额/借用平台 | P0 | 1/2/3号位编排与资源时间轴需保证互斥 |
| F-042 | 白天跨Domain插单=多个单Domain WHATIF按依赖拓扑串行 | 当前仅“选多Domain拆多个重排”，没有完整CTP链 | A/A/A/A/A/—：C→B→A逐层传Quantity-Time，前端汇总一个CTP | P0 | 2/3号位编排 |
| F-043 | CTP与INSERT_IMPACT_ANALYSIS可保留Purpose，但同一次WHATIF应同时产交期+影响 | 当前定义为两个独立Purpose，易被实现成两次Solver | M/M/M/M/M/—：明确不得重复跑两个独立Solver | P1 | 复用同一Candidate结果 |
| F-044 | Scenario对普通CTP/插单WHATIF不是必选依赖 | 当前演变总表称“正式业务容器”，接口通常创建 | M/M/M/M/M/M注释：降为可选容器/未来多场景用途，不删表不强制 | P2 | 最小影响 |
| F-045 | WHATIF永不自动正式；正式采用只需最小人工授权确认 | 当前明确CANDIDATE_ACTIVATION ApprovalRecord=APPROVED才可激活 | M/M/M/M/M/M注释：不把完整OA审批作为V1主链前置；记录谁/何时/采用哪个Candidate即可 | P0 | 以2号位实际代码为准，避免新增重审批 |
| F-046 | 若现有审批重，会阻碍V1则旁路/简化 | 旧审批表/接口体系过重 | M/M/M/M/H或P/P：审批表可保留，不得反向要求主链实现 | P2 | 保护已有表，解除硬依赖 |
| F-047 | Demand Protection与Strict Binding、Execution Constraint三类分开 | 当前六文档缺Demand Protection正式主链 | A/A/A/A/A/最小A：需求—供给锁用LockType区分STRICT_BINDING/DEMAND_PROTECTION；Execution事实独立 | P0 | Lock进入Pegging循环 |
| F-048 | Demand Protection锁数量份额，不锁整个PI/PO | 当前无份额级保护语义 | A/A/A/A/A/A/或统一Allocation字段 | P0 | 2号位锁传播 |
| F-049 | 下阶通用PI开工不自动绑定原Demand | 旧“执行锁=关系锁”风险 | A/A/A/A/A/—：生产事实固定，Ownership仅在严格/保护/不可逆时锁 | P0 | 重排释放逻辑 |
| F-050 | Firm/Frozen是排程不可移动，不等于Pegging Lock | 当前FreezeZoneVoucher/审批容易混用 | M/M/M/M/M/—：职责分离 | P0 | 2→1约束输入 |
| F-051 | 不建设FrozenZoneSnapshot正式平台；跨版本从上一ACTIVE读取仍有效锚点 | 当前FreezeZoneVoucher/快照思路较重 | M/M/M/M/M/D/H：停止新链；保留Firm/Frozen语义 | P1 | 不再维护Snapshot |
| F-052 | VirtualInventoryBalance不持久化 | 旧跨Domain“虚拟库存”概念可能被物理化 | M/M/M/M/M/D/H：只在内存传Quantity-Time | P1 | 不新增持久状态 |
| F-053 | Explanation根因由1号位求解时原生产出；2号位聚合持久化 | 走查还让5号位生成延期/物流/跨域Reason | M/M/M/M/M/—：5号位只提供数据Issue/事实，不做调度根因 | P0 | `ExplanationFactDraft`统一从Solver返回 |
| F-054 | `DUE_DATE_RISK`是结果，不是根因；延迟不能机械累计 | 当前ReasonCode字典可保留，但用法需改 | M/M/M/M/M/—：保留15项字典，重写生成规则 | P1 | Explanation算法 |
| F-055 | Candidate、无PI占位Task、UNLOCATED保守Task不得下发MES | 当前MES下发资格主要靠FreezeZoneVoucher | M/M/M/M/M/—：正式发布资格独立明确 | P0 | MES出站过滤 |
| F-056 | 无PI号虚拟Task允许规划、不能下MES，次日有正式PI自动重算替代 | 当前缺完整冻结口径 | A/A/A/A/A/必要标记 | P1 | 2/1结果标记 |
| F-057 | MES五态保持0～4，不新增PAUSE/RESUME闭环 | 当前已基本正确 | K/K/K/K/K/K | P0 | 保护 |
| F-058 | 设备故障只形成资源事实+Explanation+Recommendation，PMC决定是否重排 | 当前主文已基本收口 | K/K/K/K/K/K | P1 | 保护，不恢复自动重排 |
| F-059 | 规则引擎只两种运行模式：有序第一命中；默认+例外覆盖 | 当前主题表+Voucher/插件混合，规则执行职责散 | M/M/M/M/M/M注释：运行时一次加载冻结快照，不逐Demand调用3/5号位 | P1 | 3号位治理、1/2内存消费 |
| F-060 | 规则治理6表不因“减法”贸然删除，先保护3号位已有实现 | 当前6表已存在 | P/P/P/P/P/P：不在本轮差异矩阵直接删表；只简化主链语义 | P2 | 防止过度重构 |
| F-061 | Rule/Parameter版本状态不应因表存在就强制完整审批流 | 当前DRAFT/SUBMITTED/APPROVED/PUBLISHED等可能偏重 | M/M/M/M/M/M注释：实际发布流程以3号位已实现最小闭环为准，不新增审批依赖 | P2 | 后续查3号位代码后再决定DDL删不删枚举 |
| F-062 | PSA/逻辑Allocation/TaskShare需在同一Domain事务内闭合 | 当前2号位代码曾出现事务内+事务外PSA、先DELETE再事务INSERT | M/M/M/M/M/M：文档写死原子持久化边界 | P0 | 统一事务 |
| F-063 | `InventoryBalance.AllocatedQty`不作为跨PlanVersion运行期永久扣减真相 | 当前代码历史路径有污染风险，旧文档语义不够清 | A/A/A/A/M/M注释：源Supply事实+本版本Allocation内存余额为准 | P0 | 删除运行期永久UPDATE |
| F-064 | Pegging闭合错误必须Domain失败；无法按期不是Solver失败 | 旧流程有Warning继续排程风险 | M/M/M/M/M/—：区分数据降级与数量/事务硬失败 | P0 | 失败即停止发布 |
| F-065 | 客户订单完成终点=最终生产/装配入ZP/BP | 旧ShippingTask把客户/厂间物流扩进终点 | M/M/M/M/M/—：物流时间不进入生产有限产能终点 | P0 | CTP终点修正 |
| F-066 | 周/月/季度只是同一90天计划聚合视图；V1无年度详细排程 | 当前可能存在ComputeMode/阶段二评分扩展 | M/M/M/M/M/—：不新增周/月/季Solver | P1 | 读模型可保留 |
| F-067 | 10万Task、夜间约15分钟为统一Solver压测目标 | 当前旧文字还有单域30分钟等历史目标 | M/M/M/M/M/—：统一性能验收口径 | P1 | 压测基线 |

---

## 3. 按文档逐份修改范围

### 3.1 《核心排产全流程走查》——第一优先修改

这是六份文档里冲突最多的一份，应先改，因为后五份都要跟它对齐。

**必须重写的主段**：

1. 前言“稳定框架+5号位灵活插件+凭证交互模式”——改成3号位治理、2号位Pegging执行、5号位复杂事实、1号位Solver；Voucher不再是通用主链。
2. 阶段0.5跨Domain：保留依赖拓扑；删除“DefaultLeadTimeDays固定2天”为权威口径；ShippingTask退出。
3. 数据准备00:55：Pipeline不再V1空跑；采购/VMI/Received/跨厂在途升级为正式V1事实。
4. Pegging阶段：补分层Demand、第一命中排序、Demand/Supply双边余额、PI选择、PI Position、自消费保护、Demand Protection。
5. 跨厂流程：重写STAGE_HANDOFF和INTER_FACTORY_ORDER；删除有限物流Task。
6. Task阶段：删除2号位+5号位TaskSplitVoucher提前生成Task，改为2号位逻辑生产需求→1号位FinalTask。
7. 有限产能阶段：写入计划良率、90天一套Solver、动态瓶颈、混合正倒排、五阶段、Explanation原生。
8. 夜间发布：上游失败阻断依赖下游本次新ACTIVE，无关域照常发布；增加人工新ScheduleRun恢复链。
9. 白天Candidate：改RemainingSupply、动态影响传播、MaxImpactedOrders警戒阈值、共享资源外域ACTIVE阻挡、单域全排fallback。
10. 白天跨Domain插单：增加C→B→A多个单Domain WHATIF串行CTP。
11. Candidate采用：删“必须ApprovalRecord APPROVED”的主链前提；保留最小人工确认审计。
12. MES：Candidate/虚拟/UNLOCATED不得下发；Firm/Frozen跨版本锚点；不依赖FreezeZoneSnapshot/Voucher。
13. Explanation：1号位出根因，2号位汇总，5号位不再产生排程原因。

**需要保留的正确骨架**：ScheduleRun/PlanVersion多Domain、ExpectedDomainKeysJson、PARTIAL_SUCCESS、MES五态、BOM实时RequestDetail链、禁止自动重排、ReasonCode字典、OrderCanonical状态准入。

### 3.2 《各类基础数据分层承接与演变总表》

重点不是重写所有历史演变，而是把**当前态横向总表**改对：

- Order行删除PriorityScore作为主链；
- PipelineSupply行从V1空跑升级为V1正式；
- CrossFactory行删除“5号位插件+1号位消费Task/ShippingTask”；
- 排程运行编排行将Task实例化移给1号位；
- StageProgress行改成5号位PI位置事实→2号位Position→1号位解析起点；
- Scenario降为可选容器；
- Candidate RemainingSupply改为未锁普通Allocation可重竞争；
- 新增Demand Protection、PI Position、计划良率、Planning-only Purchase Placeholder、跨Domain Quantity-Time切片和失败人工恢复的横向承接关系。

历史版本说明可以保留，但必须标“历史已废止”，不能让历史段落看起来仍是当前口径。

### 3.3 《数据架构与防腐层设计方案》

重点修正数据与职责边界：

- 删除“02:00 2号位生成Task/ShippingTask候选”；
- 删除远期8～90天按DefaultLeadTime占位粗排作为正式策略；
- Pipeline正式接入V1；
- Received视图不得再是V1空骨架；
- PI Position采用一张最小快照，不建Header+Slice；
- StageProgress不直接交1号位解释PI位置；
- Candidate RemainingSupply按真实可重竞争Supply计算；
- 其它Domain ACTIVE占用作为白天共享资源阻挡块；
- 不建VirtualInventoryBalance/FrozenZoneSnapshot/有限物流状态平台；
- 规则参数运行时只读冻结快照，不逐笔回调3/5号位。

### 3.4 《集成接口设计》

重点改服务契约语义，不需要重新设计整个Adapter体系：

- `BuildRemainingSupplyContextAsync`改变语义；
- `BuildScheduleContextAsync`输出逻辑生产需求、Allocation、Quantity-Time、Routing/Eligibility/Calendar/不可移动事实，不是已实例化Task；
- `IFiniteCapacityScheduler`返回FinalTask+AllocationTaskShare+Task-to-Task+Explanation；
- PipelineSupplyItem从“V1空”改为正式V1；
- 增加PI Position Calculator契约；
- 增加Planning-only Purchase Placeholder运行时契约；
- MaxImpactedOrders不再硬截断；
- Candidate激活从完整ApprovalRecord硬依赖改为最小人工确认边界；
- CTP/Impact共用同一WHATIF结果；
- 跨Domain WHATIF编排使用多个单Domain运行，不新增MultiDomain API。

### 3.5 《数据库字段说明》

重点是让字段语义不再把旧业务重新带回来：

- PriorityScore标历史兼容/非V1主链权威；
- Task.OrderId改为非权威单值归属，真正Demand血缘走AllocationTaskShare；
- Task增加PlannedProcessQty；
- 新增AllocationTaskShare字段说明；
- 新增最小PI Position快照字段说明；
- 新增Demand Protection最小锁语义；
- StageProgressSnapshot删“1号位优先消费”表述；
- SupplyFact_Pipeline/Received从V1空改V1正式；
- ComputeMode中的ROUGH_CUT不得被描述为V1远期主链；
- Scenario、ApprovalFlow等标可选/非V1主链；
- Frozen/Firm说明与Demand Lock分开；
- ShippingTask相关说明退出V1生产排程主链。

### 3.6 《数据库表结构设计 DDL》——最后改

必须等前五份语义稳定后再改DDL，避免“先改表后重新解释业务”。

**预计必须动的物理结构**：

1. `Order.PriorityScore`：建议先保留兼容列，但注释改“历史/展示兼容，不作为V1主排序权威”；不为简化而强删，保护现有代码。
2. `Task.OrderId`：允许NULL或降级为兼容主归属字段；真正多Demand关系由`AllocationTaskShare`承接。
3. `Task`：增加`PlannedProcessQty`。
4. 新增最小`AllocationTaskShare`。
5. 补最小PI Position快照表；禁止Header+Slice两套生命周期。
6. 增加/统一需求—供给锁最小对象，`LockType=STRICT_BINDING/DEMAND_PROTECTION`；Execution现实事实不与其混成一套。
7. Pipeline/PO/VMI/Arrived/Received契约移除V1 `WHERE 1=0`正式口径；具体源SQL由5号位按真实系统实现，不允许2号位猜源表。
8. `Domain_Dependency.DefaultLeadTimeDays`不再硬编码2；优先保留兼容字段但改为真实转运LT派生缓存/非独立业务配置，避免为删除字段大改2号位现有代码。
9. `Scenario`/Simulation骨架不必删除，只取消普通WHATIF主链强依赖。
10. Approval表不因存在就要求V1实现完整OA审批；是否物理删除不在本轮强制，避免无意义数据库重构。
11. `TaskSplitRuleConfig`可作为拆批参数来源保留，但不再意味着5号位生成TaskSplitVoucher/2号位提前拆Task。
12. FrozenZoneSnapshot/VirtualInventoryBalance如果实际DDL没有则不新增；若历史环境已有，标deprecated，不建设生成链。
13. PSA如代码已使用，应补成极简正式对象并统一事务；不得变成第二真相源。

---

## 4. 保护区：本轮不得顺手修改

以下内容没有被最终冻结业务推翻，本轮差异修订不得因为“看起来复杂”而擅自删除：

1. `ScheduleRun + PlanVersion`版本壳和多Domain关系；
2. `ExpectedDomainKeysJson`独立字段方案；
3. `PARTIAL_SUCCESS`；
4. 白天Candidate严格单Domain；
5. OrderCanonical→Order版本快照；
6. BOM Workset / RequestDetail / Realtime三表 / RAW搬运链；
7. `OrderBomRequestLink`稳定锚点；
8. RoutingOperation / RoutingDependency / OperationResourceEligibility三件套；
9. `MaterialStageDeptContext`现有主链——除非后续发现与实际代码不可实现的硬冲突，否则本轮不因“简化”重构；
10. `ProcessCodeDict.ERPProperty`必须来自ERP真实业务属性并由5号位同步透出；
11. MES五态0～4；
12. Resource故障不自动重排；
13. RuleSet/ParameterSet/StrategyProfile六张治理表物理结构先保护，避免在不知道3号位代码的情况下过度重构；
14. 2号位现有`SchedulingOrchestrator`、ScheduleContext装载、SupplyPool余额原型、`IFiniteCapacityScheduler`接口、事务框架。

---

## 5. 2号位代码不可调和性检查

本矩阵没有发现需要推翻2号位现有外壳的修改。真正冲突点都可以通过**退出旧路径 + 补核心**解决：

### 必须保护

- `SchedulingOrchestrator`；
- ScheduleRun/PlanVersion；
- ScheduleContext装载；
- SupplyPool.RemainingQty；
- Stage进度装载；
- `IFiniteCapacityScheduler.SolveAsync`接口方向；
- DTO/事务模板。

### 必须退出或改语义

- 2号位提前FinalTask；
- 外层第二次旧FiniteCapacitySolver；
- PassThrough作为正式Solver；
- Base ACTIVE全部Allocation扣死的RemainingSupply；
- PSA事务外重复写；
- `InventoryBalance.AllocatedQty`运行期永久污染；
- Pegging失败Warning后继续；
- TaskShare一对一压缩；
- 5号位逐笔插件裁决；
- FrozenZoneSnapshot生成链。

### 必须补齐

- DemandBalance；
- Supply原子扣减；
- Allocation/逻辑Ledger；
- Demand Protection/Strict Binding执行；
- PI Position服务；
- AllocationTaskShare多对多；
- 计划良率；
- 单一真实1号位Solver；
- Candidate动态影响传播；
- 失败Domain人工新ScheduleRun恢复。

**判断**：这是主链核心整改，但不是不可调和冲突，更不是推倒重写。

---

## 6. 下一步修改顺序

矩阵冻结后，按以下顺序修改六份技术文档：

1. **《APS 核心排产全流程走查》**——先把“怎么跑”改对；
2. **《APS 各类基础数据分层承接与演变总表》**——把各数据对象横向承接关系改对；
3. **《APS 数据架构与防腐层设计方案》**——把Socket/Plug/Loader/运行上下文职责改对；
4. **《APS 集成接口设计》**——把0～5号位接口与输入输出改对；
5. **《APS 数据库字段说明》**——把字段语义改对；
6. **《APS 数据库表结构设计DDL》**——最后做物理结构最小修改。

每修改完一份，只做“冻结符合性审查”，不重新讨论业务。

六份全部修改完后，再统一做：

> **业务闭环 + 数量闭环 + 时间闭环 + 版本闭环 + 异常恢复闭环 + 0～5号位职责闭环 + 过度设计回流检查 + 2号位代码兼容检查。**

通过后才进入0～5号位实施包与代码整改。

---

## 7. 本矩阵最终判断

1. **三份业务冻结目前无需重新打开。**
2. 六份技术文档确实存在较多旧口径，但主要是2026-07-20以后冻结内容未同步，**不是新业务矛盾**。
3. 当前最重的修改集中在《全流程走查》；如果先把它改正确，后五份可以按其语义逐层落地。
4. 目前没有发现必须为了技术实现而改变冻结业务的点。
5. 目前没有发现与2号位代码不可调和的矛盾。
6. 本轮最大的防过度设计原则仍然是：**不建有限物流Task、不建MultiDomain Candidate、不建VirtualInventoryBalance、不建FrozenZoneSnapshot平台、不建第二套90天远期Solver、不因Scenario/Approval/规则表已经存在就强迫主链使用。**

**状态**：差异矩阵v1.0，可作为六份技术文档正式修订的唯一修改清单；任何新增修改项若涉及业务含义变化，必须先回到冻结层显式裁决。
