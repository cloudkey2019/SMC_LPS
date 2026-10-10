# APS V1 — 3号位代码专项审核裁决

- 审核日期：2026-10-09
- 审核仓库：`killvabj/LPS`
- 审核Commit：`4caebef09a917caccc2e21aba16cb5a038efdfa2`
- 上一提交：`a78ad349b4574291ec2db80607e9daa9c566d07b`（本次只前进一个提交）
- 审核范围：3号位认证授权、Business Scope、Batch Policy、规则/参数快照、StageLeadTimeParam、Run/Candidate治理的抽查与重点反证；只对3号位负责的代码下整改结论。
- 正式基线：截至可核实文件，唯一生效索引v1.11、3号位实施包v1.8、Auth设计/DDL v1.1及历史已生效治理规则。2026-10-08正文v1.9及2026-10-09控制清单v1.6均明确标为候选，是否切换仅由0号位正式公告决定。本报告对候选新增规则单列「切换前必验」，不提前宣告生效。
- 审核方法：GitHub真实源码静态核查、相邻前一提交差异、冻结文档逐项映射。没有执行数据库联调或单元测试，也没有可查到该SHA的CI工作流；因此不能宣布独立动态验收通过。

## 一、最终结论

**BLOCKED — 3号位不予整体验收通过。**

主体治理模块已有实质代码，不是空壳。Batch Policy维护、硬Min/Max与Preferred校验、部门写入Scope、有效期检查、版本发布快照及读取链已经可见。Run/Candidate、Auth/RBAC/Scope也已有大量实现。但至少存在明确的后端Business Scope缺口和Batch Policy有效期逻辑冲突，必须整改。其他号位的提交不计入3号位缺陷。

## 二、已确认实现的能力

1. `TaskSplitRuleConfigGovernanceService`：Create/Update/Deactivate/List（含分页）、规则合法性验证、部门编码到`EnsureInScopeAsync`的写入校验、审计调用。
2. `TaskSplitRuleConfigProjector`：按active和有效时段投影业务字段，序列化到`ContentSnapshotJson.BatchPolicy`；`GovernanceVersionService.PublishParameterSetVersionAsync`在发布时注入；`FrozenStrategySnapshotProvider`能解析。
3. 无第二BatchPolicy实体平台；已保留历史物理列、按候选规则停止向业务快照投影预算及两个瓶颈旧枚举。
4. Auth、Run/Candidate生命周期有具体实现，并非此次Commit全部新增。对这些旧实现只能称为「代码已存在」，不能称为本轮已通过真实全链测试。

## 三、P0问题（可据源码确认）

### P0-01：Batch Policy列表查询未实施登录用户有效Business Scope过滤

- 源码：`LPS.APS.Application/Services/TaskSplitRuleConfigGovernanceService.cs:58-91`，`LPS.APS.Web/Controllers/TaskSplitRuleConfigController.cs:64-82`。
- `ListAsync`接收可任意省略的`productionDepartmentId`，没有`actorUserId`，也没有解析有效部门/工厂Scope；SQL只按调用方自选过滤，省略部门即查询全表，COUNT亦全表。
- 已生效《APS权限与审批设计方案v1.1》§9.3明示「查询接口必须按照有效Scope返回数据」，禁止先全量返回再前端隐藏。RBAC的`RuleView`不能替代Scope。
- 整改：后端使用受信主体的`ResolveScopeAsync`约束查询和Count；传入超范围部门时403或空结果需与现有Scope契约一致；Global权限按真实DataScopePolicy处理。新增只授权部门A但尝试查看部门B（无参数、显式B、跨页/Total）的反证。

### P0-02：Batch Policy编辑只检查新部门，存在跨部门篡改入口

- 源码：`TaskSplitRuleConfigGovernanceService.cs:160-202`。
- `UpdateAsync`先按Id取得旧记录，**不对旧记录的`ProductionDepartmentId`鉴权**；只执行`EnsureDepartmentScopeAsync(input.ProductionDepartmentId)`。用户若仅授权A部门，知道B部门规则Id，就可能提交把B记录改成A部门并覆盖其规则。
- 已生效Auth冻结§9.4要求「目标对象在业务Scope内」，不能把目标解释为请求体伪造的新值。
- 整改：对旧归属和拟变更的新归属分别校验、更新时在SQL侧强化归属/并发条件；禁止IDOR式跨部门记录迁移。追加A用户修改B记录并将部门改A的反证（应403、数据库无修改、审计Denied按既有审计契约）。

### P0-03：StageLeadTimeParam治理列表与写入缺Business Scope后端校验

- 源码：`LPS.APS.Application/Services/StageLeadTimeParamGovernanceService.cs:46-60,64-95,129-155`；`LPS.APS.Web/Controllers/StageLeadTimeParamController.cs:60-145`。
- 服务中没有`IDataScopeService`或等效可信Scope限制；列表的Factory/Department参数是调用方可控筛选，创建与编辑仅作业务校验，不校验登录用户Factory/Department范围。
- `StageLeadTimeParam`归3号位治理；适用冻结Auth §9.3/9.4。该风险属于仓库累计代码，不一定为`4caebef`新引入。
- 整改：在3号位后端落实列表Scope与老/新目标Scope校验；对Factory+Department组合约束作反证。随后对Setup、Domain、其它治理端点做同类Scope专项扫描，不能因为本报告只抽查两类API而假设其它API全合格。

### P0-04：Batch Policy有效期“相切不重叠”与有效判断同时成立规则矛盾

- 源码：`TaskSplitRuleConfigGovernanceService.cs:390-418`的窗口排重使用严格`<`，故允许第一条 `[10:00,11:00]`、第二条 `[11:00,12:00]`；`TaskSplitRuleConfigProjector.cs:35-39`判断`EffectiveFrom <= now && EffectiveTo >= now`，故`11:00`两条同时有效。
- 这直接与「同一Material+Dept同一时刻至多一条规则」冲突，并可能向发布快照投影重复键。代码注释称「相切不重叠」，但投影实现是包含截止点。
- 整改：统一明示半开`[From,To)`或闭区间语义（仅技术口径保持一致，不借机重新裁决批量业务），并在SQL检重、Project、数据库数据校验、边界单测保持相同；测试同秒相切、NULL开端/末端、历史数据冲突。当前SQL没有数据库级唯一时间窗防护。

## 四、候选新版正式切换前必须关闭（不得提前计入旧冻结P0）

### CANDIDATE-P0-01：旧记录非法内容能够静默进入发布快照

- 候选v1.9 §「本轮新增治理项」与字段v5.1.10明确「已有不合法记录不能静默转有效配置」。
- `TaskSplitRuleConfigProjector.Project()`只筛`IsActive`、部门非空与有效期，不校验`PreferredBatchQty`是否非空/正值/处于Min/Max之内，也不校验Min/Max数值合法性；`GetActiveRulesForSnapshotAsync`直接取表并调用Project；`GovernanceVersionService.PublishParameterSetVersionAsync`直接注入并发布。
- 因此过去绕过新API写入的存量active行，例如`PreferredBatchQty=NULL`或`Min>Max`，可以进入新发布参数集快照；新Create/Update校验不能补救存量。
- 整改：发布前对全部拟生效行做统一验证；非法行明确阻断发布（或按正式裁决单独隔离和标记，严禁默默当有效）；补存量非法值、部门NULL和时间窗冲突的快照发布反证。正式切换前必过。

### CANDIDATE-RELEASE-02：候选规则已提前写成当前主链行为

- `TaskSplitRuleConfigProjector`已排除NULL部门，并停止投影历史搜索预算；API也已拒绝NULL部门、强制Preferred。该实现与10月8—9日候选方向一致。
- 但控制层清单v1.6明确「正式切换前沿用v1.5/唯一索引v1.11」。因此需要0号位协调版本切换、数据迁移和1/2/3号位接口同步；不能靠代码注释「2026-10-09生效」代替正式生效公告。

## 五、P1问题与工程风险

### P1-01：NULL部门历史记录无法正常停用

`TaskSplitRuleConfigGovernanceService.DeactivateAsync():250`对`existing.ProductionDepartmentId!.Value`取值。候选规则明确数据库允许NULL部门历史兼容；若存量NULL记录仍`IsActive=1`，管理人员即使有权限也无法通过当前停用路径完成治理，可能Null异常（500）。需要设计既有Global或管理员处置分支、保持审计，并添加集成反证。

### P1-02：有效期排重存在竞态窗口

`EnsureUniqueEffectiveWindowAsync()`的`SELECT COUNT`与后续`INSERT/UPDATE`为分离数据库操作，DDL只提供非唯一查询索引`IX_TaskSplitRuleConfig_Query`。并发请求可能均通过检查而写入同业务键重叠窗口。需要单事务加正确隔离/应用锁等数据库端互斥机制，并提交两并发请求反证；不应把单线程用例通过当作并发唯一性已保证。

### P1-03：治理DML与AuditLog落地原子性未证明

Batch Policy的Create/Update/Deactivate都是先对APS业务库DML，再调用审计仓储；未见跨两者统一可回滚事务。若审计写失败可能已有规则生效但操作没有可追溯审计。请3号位说明已冻结审计一致性契约和既有失败补偿策略，并作审计失败注入测试；未证实前不直接认定线上审计必丢，但不能宣称治理+审计已端到端验收。

### P1-04：Scope问题存在向其它治理服务扩散的迹象

`SetupRuleService`未发现`EnsureInScope/ResolveScopeAsync`使用点，`StageLeadTimeParamGovernanceService`也没有。需要3号位对所有可编辑的规则/参数/Domain/Governance服务提交功能权限×Scope二次校验清单；按确有业务范围的实体落具体反证，而非盲目上通用记录级ACL平台。

## 六、跨号位边界与不归责事项

本次对比包含`PeggingOrchestrator.cs`约千行改动、`LogicalProductionDemand.cs`与`PiRemainingFact.cs`新契约、位置探针。它们需要2号位按冻结数量/运行链另审，但不计入3号位工作完成率或P0。1号位真实Solver数量正确性、5号位ODS事实、4号位页面展示也不在本轮归责范围。

`RoutingOperationGovernanceService`仍可见：当前它维护`OperationPlanningMode`，不是直接治理RouteCode/PathId合法候选或动态选路；不能仅凭类名判越权。应在0号位切换新版时核对其唯一工艺属性维护授权，禁止扩展为第二套Routing事实/选择平台。

## 七、必须提供的最小反证清单

1. 用户A有RuleView、仅授权部门A；GET规则列表不传部门、显式传B、检查Total与跨页，均不能泄露B。
2. 用户A有RuleMaintain、仅授权A；用B记录Id改成A部门，必须403且B原记录不变；改A记录到B同样拒绝。
3. StageLeadTimeParam按Factory+Department授权：越权读/新建/修改/停用均拒绝；拥有合法Scope可成功。
4. 同Material+Dept的有效期窗口`[10,11]`与`[11,12]`边界，同一瞬间不可双有效；NULL无界窗口不能重叠。
5. 两个并发Create同业务键、重叠窗口，最多一个成功；数据库最终有效记录数保持唯一。
6. 存量active Preferred=null、Preferred<=0、Min>Max、NULL部门规则：发布时不得非法记录静默成为有效快照；旧NULL部门记录可由授权管理员有审计地停用。
7. 参数集发布→冻结`ContentSnapshotJson.BatchPolicy`→策略快照读取→2号位运行装载，业务字段、部门身份、有效期与版本追溯一致。
8. Scope/审计失败注入及Candidate生命周期核心用例，与公开Commit SHA对应，提交可复现测试日志。

## 八、交付给0号位的裁决

- **3号位整体：BLOCKED**。
- 已完成的治理骨架和字段收口保留，不要求推倒重做。
- 先关闭P0-Scope与有效期冲突，再做候选新版发布前存量校验与并发防护。
- 禁止把尚未生效的10月8—9日候选直接当作现行冻结违例，同时禁止通过已经写好的代码反向宣布候选生效。
- 没有该SHA的可验证CI/数据库集成测试记录，不将开发者单测口头报告等同于独立验收。
