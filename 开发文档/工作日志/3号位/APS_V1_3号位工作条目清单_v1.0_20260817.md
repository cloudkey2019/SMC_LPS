# APS V1 3号位工作条目清单（实施包整理）

**版本**：v1.0
**日期**：2026-08-17
**性质**：由《APS_V1_3号位规则参数与运行生命周期开发实施包_v1.0_20260814.md》整理的工作条目清单
**上位依据**：APS V1三份业务冻结文档 + 六份技术冻结文档 +《APS V1关键接口冻结：1↔2、2↔5、2↔3》
**修订记录**：2026-08-17 第 0 步现状盘点后，按冻结 DDL `v5.1.2_冻结对齐版` 修正——版本状态 3 态→**6 态**（#11/A2/A3）、RunType 组合→**单值枚举**（#44/F8/附二），并固化 IsDefault 语义。

---

# 第一部分：按实施包顺序的工作条目（55 条）

## 一、定位与职责

1. **治理规则、参数、策略版本**，并在一次 ScheduleRun 开始时冻结本次运行使用的规则与参数
2. 维护 ScheduleRun / PlanVersion 生命周期元数据边界
3. ExpectedDomainKeysJson 冻结
4. StrategyProfileVersion 绑定
5. Candidate 最小人工确认/激活边界
6. 规则、参数、策略的维护与发布
7. **明确不负责**：Demand 逐笔排序、Supply 选择、Pegging Allocation、余额/PI Position、采购/VMI/跨厂事实、FinalTask、有限产能 Solver、Task 持久化、MES 下发

## 二、规则引擎原则

8. 采用"全局默认 + 少量例外覆盖"，**不建** DSL/脚本平台/表达式引擎/插件市场/命令链编排器
9. 三类配置对象治理：**RuleSet**（用哪条规则）、**ParameterSet**（数值多少）、**StrategyProfile**（哪组 RuleSet+ParameterSet + 排程策略）
10. 复用现有**六张治理表**：RuleSet / RuleSetVersion / ParameterSet / ParameterSetVersion / StrategyProfile / StrategyProfileVersion

## 三、版本生命周期

11. 版本生命周期：**按冻结 DDL 6 态** `DRAFT → SUBMITTED → APPROVED → PUBLISHED → DISABLED / ARCHIVED`（v5.1.2 CK 约束）；PUBLISHED 不可直接改业务内容，修改必须产生新 Version；**"不可再被新 Run 选中"由 PUBLISHED→DISABLED/ARCHIVED 承载（替代实施包 RETIRED）**；历史 PlanVersion 可追溯当时使用的版本
12. **Run 启动时冻结**：StrategyProfileVersionId + RuleSetVersion + ParameterSetVersion + ExpectedDomainKeysJson + DataCutoffTime；Run 中途发布新规则不影响本次 Run；**同一 Run 各 Domain 必须版本一致**

## 四、2↔3 接口

13. 实现 `FrozenStrategySnapshot` DTO（一次性 Snapshot 而非逐笔决策）：含 DemandPriority / Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail 六块配置

## 五、Demand 排序规则

14. 实现 **CalculationLayer → Priority Segment（有序）→ 第一命中 → Segment 内部 Sort → Stable Tie-break** 模型，**禁止全局 PriorityScore**
15. Priority Segment 配置结构：SegmentOrder / MatchConditions / SortFields / SortDirection / StableTieBreakFields / IsEnabled
16. 第一命中后不再匹配后续 Segment（避免多规则叠加不可解释）
17. **不建**：布尔表达式 DSL / 动态 C# 脚本 / SQL 片段规则 / 全局 Weighted Score

## 六、Demand Protection 规则

18. 只治理**触发条件**（RemainingTime<NormalLT、DelayStatus=DELAYED、CustomerTier=VIP、特定 OrderType、PMC 人工强保护）与保护份额/时点
19. Sticky 规则：保护持续到 Demand 完成/取消/Supply 失效/PMC 显式释放；手工释放记录 Actor / Time / Reason（3号位配置入口，2号位执行）

## 七、Supply 相关规则

20. Inventory Availability：治理 Warehouse 是否可用、Priority、必要 Factory/ProductFamily 上下文（2号位执行）
21. PI 简单排序：默认 Issue/Create Time ASC，必要时 CreatedAt/IssueDate/Stable PI No；**不建**复杂 PI 评分引擎
22. Procurement 排序治理：Warehouse Priority / 默认 LT / Margin / Arrival Offset（不重排 Eligibility→Warehouse Priority→AvailableTime→PO Release Time→PO+Line 的冻结排序链）

## 八、采购与良率参数

23. Default Purchase LT（按 Receiving Warehouse + 必要 Material 维度）
24. ETA 优先级冻结为：Manual ETA → ERP ETA → Default LT（不可做成任意 Rule Chain）
25. 逾期 Margin（MarginPercent / MinimumExtraDays，保守修正）
26. Arrival-to-Usable Offset（按 Receiving Warehouse，ArrivalTime→AvailableTime）
27. Planning Yield 参数维护（Material/Stage 维度，2号位算 NetOutputQty→PlannedProcessQty，1号位只消费）；**红线：已有 PI Supply 不能按 Yield 再次放大**

## 九、Solver 策略参数

28. StrategyMode：FORWARD / BACKWARD / MIXED
29. Dynamic Bottleneck：AUTO / PREFER_ANCHOR / FORCE_ANCHOR / NOT_ANCHOR（不建瓶颈知识图谱）
30. On-time Target（业务优化层目标，不被 Setup/WIP/Utilization 反向压过）
31. Split 参数：MaxOptimizationSplitCount（如 3）/ Mandatory Split 限制 / MinBatchQty（不限无限拆分）
32. Setup 参数：Mold / Tool / Material / Color 等冻结维度（不做全局 TSP 权重矩阵平台）
33. Stage Overlap：是否允许 / Transfer Batch Qty / Threshold Qty/Percent

## 十、Candidate Guardrail

34. 治理技术参数：Normal≈60s / Soft≈90s / Local Hard≈180s / Impacted Task 警戒≈30% / Max Repair Attempts≈5 / Max Propagation Rounds≈10 / Resource TopN≈5 / Split Alternative≈3
35. **红线**：Guardrail 不能截断正确性——MaxImpactedOrders 超阈值只能 Warning + 人工确认，不能停止传播返回伪可行结果

## 十一、ScopeJson 与 ExpectedDomainKeysJson

36. ScopeJson 保持冻结 11 字段，**禁止**往里加 ExpectedDomainKeys / Solver Trace / CrossDomain / 任意规则快照
37. ExpectedDomainKeysJson 是 ScheduleRun 独立字段：FULL 一个或多个 Domain / Candidate 严格一个 / Run 开始后不可变 / 不能按已创建 PlanVersion 反推

## 十二、ScheduleRun 生命周期

38. FULL 状态：RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED，终态写 CompletedAt
39. Domain PlanVersion 状态：BUILDING / ACTIVE / FAILED / CANDIDATE / ARCHIVED（以冻结 DDL 为准）
40. **不强制分钟点**（不必 00:38 建 Run / 02:00 建 Version），只要求 Run 可追溯、DataCutoffTime 一致、StrategyProfileVersion 一致、ExpectedDomainKeysJson 冻结、每 Domain PlanVersion 可追溯、终态闭合
41. **FULL 失败链**：上游失败→上游 PlanVersion FAILED、原 ACTIVE 保持、直接/间接依赖下游本次不发布新 ACTIVE、无关 Domain 继续；支持 Dependency 查询 / Run 状态汇总 / 被阻断 Domain 状态原因展示；**不建**全域 ALL_OR_NOTHING / 原子多 Domain 激活组 / 自动跨域回滚
42. **人工恢复**：失败后新建 ScheduleRun，**禁止**把 FAILED Run 改回 RUNNING；恢复范围=失败根域+被阻断下游+必要时补上游（不建复杂 Retry 平台）

## 十三、Candidate 最小人工确认

43. 最小边界：Candidate 生成 / 展示 / Actor 确认 / ConfirmedAt / CandidatePlanVersionId / Activate 动作 / 审计记录；OA 仅作可选 Adapter，**OA 不可用不阻断 Candidate 功能**
44. **StrategyProfile.RunType 按冻结 DDL 单值枚举**：FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF（v5.1.2 CK 约束）；实施包 RunType/Purpose 组合语义（CTP、Impact Analysis 不得激活等）落 Run 创建校验，**不新增 Purpose 列**

## 十四、页面、审计与验证

45. 为 4号位提供后端能力：RuleSet/ParameterSet/StrategyProfile 列表、Version 查看、发布/停用、Diff、当前 PUBLISHED 版本、Run 引用追溯（不开发前端页面）
46. 变更审计：CreatedBy / CreatedAt / PublishedBy / PublishedAt / ChangeReason / VersionNo / ParentVersionId / 内容 Snapshot
47. **发布前校验**：同一 StrategyProfile 引用的 RuleSet/ParameterSet 是否 PUBLISHED、循环/重复 Priority Segment、Parameter 越界、Guardrail 为正、On-time Target 0~100%、Split Count 超限、Warehouse 引用有效、StrategyMode 合法

## 十五、禁止项与开发顺序

48. **不建规则平台**：用户表达式/脚本上传/动态编译/任意 SQL/流程编排/Plugin/Marketplace/CEP/决策表平台，只实现冻结业务需要的规则与参数
49. 开发顺序：**阶段A** 六表治理骨架 → **阶段B** FrozenStrategySnapshot → **阶段C** Demand Priority → **阶段D** Supply/Lock/Procurement 参数 → **阶段E** Solver Strategy → **阶段F** ScheduleRun/Candidate 生命周期

## 十六、验收与交付

50. **R01~R22 共 22 个最低验收场景**（详见第三部分）
51. **性能要求**：一次 Run 只加载一次 FrozenStrategySnapshot；不在 Pegging 循环逐笔查库 / Solver 逐 Task 读配置表；Snapshot 可缓存，Cache Key 必须含 VersionId
52. **12 项交付物**：六表后端、版本发布机制、Snapshot DTO、Demand Priority、Demand Protection、Supply/Procurement 参数、Solver Strategy 参数、ScheduleRun/ExpectedDomainKeysJson 生命周期、Candidate 确认/激活后端、R01~R22 测试结果、与2号位 Snapshot 联调记录、与1号位 Solver Strategy 联调记录
53. **联调红线**：2号位只能读 PUBLISHED + Run 时冻结 + 内存执行（不能改 RuleSet/写回 Parameter/逐笔调 3号位）；3号位不能返回逐笔"订单A优先"结果/替 2号位执行 Allocation/改 DemandBalance、SupplyBalance；1号位只消费 Solver Strategy Snapshot（不能自读治理表/忽略 StrategyProfileVersion/中途刷新参数）
54. **DoD**：17 项完成定义全满足 + R01~R22 全通过
55. **一句话交付要求**：不做"万能规则平台"，把已冻结规则参数策略做成**可版本化、可发布、可冻结、可追溯**的配置体系，让 2号位和 1号位在一次 Run 中使用同一份不再变化的规则快照

---

# 第二部分：按开发阶段 A~F 编排

## 阶段A：六表治理骨架

> 对应实施包第二十四章 + 第三、四章

| # | 条目 |
|---|---|
| A1 | 实现六张治理表（RuleSet / RuleSetVersion / ParameterSet / ParameterSetVersion / StrategyProfile / StrategyProfileVersion）的 CRUD |
| A2 | 版本生命周期：**按冻结 DDL 6 态** DRAFT→SUBMITTED→APPROVED→PUBLISHED→DISABLED/ARCHIVED；PUBLISHED 不可直接改业务内容，修改必须产生新 Version |
| A3 | **DISABLED/ARCHIVED 不再被新 ScheduleRun 选中（替代实施包 RETIRED 语义）**；历史 PlanVersion 可追溯当时使用的版本 |
| A4 | 版本发布机制：发布/停用、Diff、当前 PUBLISHED 版本、Run 引用追溯 |
| A5 | 变更审计：CreatedBy / CreatedAt / PublishedBy / PublishedAt / ChangeReason / VersionNo / ParentVersionId / 内容 Snapshot |
| A6 | 发布前校验：引用版本是否 PUBLISHED、循环/重复 Segment、参数越界、Guardrail 为正、On-time Target 0~100%、Split 超限、Warehouse 引用有效、StrategyMode 合法 |
| A7 | 为 4号位提供列表/版本查看/发布/停用/Diff/追溯等后端接口（不开发前端） |

## 阶段B：FrozenStrategySnapshot

> 第二十四章 + 第六、五章

| # | 条目 |
|---|---|
| B1 | 构建 `FrozenStrategySnapshot` DTO：StrategyProfileVersionId + DemandPriority / Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail 六块配置 |
| B2 | 装配链路：StrategyProfileVersion → RuleSetVersion + ParameterSetVersion → Snapshot DTO，支持 2号位一次装载 |
| B3 | **Run 启动时冻结**：StrategyProfileVersionId + RuleSetVersion + ParameterSetVersion + ExpectedDomainKeysJson + DataCutoffTime；Run 中途发布新规则不影响本次 Run |
| B4 | 同一 Run 各 Domain 必须版本一致（禁止 A 域上午规则、B 域中午新规则） |
| B5 | 性能：一次 Run 只加载一次 Snapshot；可内存缓存，**Cache Key 必须含 VersionId** |

## 阶段C：Demand Priority

> 第二十四章 + 第七章

| # | 条目 |
|---|---|
| C1 | 实现 CalculationLayer → Priority Segment（有序）→ 第一命中 → Segment 内 Sort → Stable Tie-break 模型 |
| C2 | **禁止全局 PriorityScore**（不同 DemandType 同层竞争按 Segment 规则交错） |
| C3 | Segment 配置结构：SegmentOrder / MatchConditions / SortFields / SortDirection / StableTieBreakFields / IsEnabled |
| C4 | 第一命中后不再匹配后续 Segment（杜绝多规则叠加不可解释） |
| C5 | 先提供 Fixture 与真实 Snapshot（供 2号位联调） |

## 阶段D：Supply / Lock / Procurement 参数

> 第二十四章 + 第八、九、十、十一章

| # | 条目 |
|---|---|
| D1 | Demand Protection 触发条件治理：RemainingTime<NormalLT / DELAYED / VIP / 特定 OrderType / PMC 人工强保护，输出保护份额与时点 |
| D2 | Sticky 规则：保护持续到完成/取消/Supply 失效/PMC 显式释放；手工释放记录 Actor/Time/Reason |
| D3 | Inventory Availability：Warehouse 是否可用 / Priority / Factory/ProductFamily 上下文 |
| D4 | PI 简单排序：默认 Issue/Create Time ASC（不建复杂评分引擎） |
| D5 | Default Purchase LT（Receiving Warehouse + 必要 Material 维度） |
| D6 | ETA 优先级冻结：Manual ETA → ERP ETA → Default LT（不做可重排 Rule Chain） |
| D7 | 逾期 Margin：MarginPercent / MinimumExtraDays 保守修正 |
| D8 | Arrival-to-Usable Offset（Receiving Warehouse 级，ArrivalTime→AvailableTime） |
| D9 | Planning Yield 参数维护（Material/Stage 维度）；**红线：已有 PI Supply 不得按 Yield 再次放大** |

## 阶段E：Solver Strategy

> 第二十四章 + 第十二、十三章

| # | 条目 |
|---|---|
| E1 | StrategyMode：FORWARD / BACKWARD / MIXED |
| E2 | Dynamic Bottleneck：AUTO / PREFER_ANCHOR / FORCE_ANCHOR / NOT_ANCHOR（不建瓶颈知识图谱） |
| E3 | On-time Target（业务优化目标，不被 Setup/WIP/Utilization 反向压过） |
| E4 | Split 参数：MaxOptimizationSplitCount（如3）/ Mandatory Split 限制 / MinBatchQty |
| E5 | Setup 参数：Mold / Tool / Material / Color 等冻结维度（不做全局 TSP 权重矩阵平台） |
| E6 | Stage Overlap：是否允许 / Transfer Batch Qty / Threshold Qty/Percent |
| E7 | Candidate Guardrail 参数：60s / 90s / 180s / Impacted 警戒 30% / Max Repair≈5 / Max Propagation≈10 / Resource TopN≈5 / Split Alt≈3 |
| E8 | **红线**：Guardrail 不截断正确性——MaxImpactedOrders 超阈值只能 Warning + 人工确认，不得返回伪可行结果 |

## 阶段F：ScheduleRun / Candidate 生命周期

> 第二十四章 + 第十四~十九章

| # | 条目 |
|---|---|
| F1 | ExpectedDomainKeysJson 冻结：FULL 一或多个 Domain / Candidate 严格一个 / Run 开始后不可变 / 不得按已建 PlanVersion 反推 |
| F2 | ScheduleRun 生命周期：RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED，终态写 CompletedAt；PlanVersion：BUILDING / ACTIVE / FAILED / CANDIDATE / ARCHIVED |
| F3 | 不强制分钟点（00:38/02:00），只要求可追溯、DataCutoffTime/StrategyProfileVersion 一致、终态闭合 |
| F4 | FULL 失败链：上游失败→上游 FAILED、原 ACTIVE 保持、依赖下游本次不发布新 ACTIVE、无关域继续；不建全域 ALL_OR_NOTHING / 原子激活组 / 自动跨域回滚 |
| F5 | 人工恢复：失败后新建 ScheduleRun，**禁止** FAILED 改回 RUNNING；恢复失败根域+被阻断下游+必要时补上游 |
| F6 | Candidate 最小人工确认：生成/展示/Actor 确认/ConfirmedAt/CandidatePlanVersionId/Activate/审计 |
| F7 | OA 仅作可选 Adapter，**OA 不可用不阻断 Candidate 功能** |
| F8 | **StrategyProfile.RunType 按 DDL 单值枚举校验（FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF）**；CTP / Impact Analysis 不得激活 |

---

# 第三部分：按验收清单 R01~R22 编排

| 编号 | 验收场景 | 必须结果 | 所属阶段 |
|---|---|---|---|
| R01 | 发布 RuleSetVersion | 历史版本不可被覆盖 | A |
| R02 | 发布 ParameterSetVersion | 新 Run 可引用，旧 Run 不变 | A |
| R03 | Run 启动后发布新规则 | 当前 Run 仍用旧冻结版本 | B |
| R04 | Priority Segment 第一命中 | Demand 只进入一个 Segment | C |
| R05 | 同 Segment 多 Sort 字段 | 顺序稳定 | C |
| R06 | 不同 DemandType 同层竞争 | 按 Segment 规则交错，不用全局 Score | C |
| R07 | Demand Protection 触发 | 2号位能读取触发配置 | D |
| R08 | PI 默认排序 | Issue/Create Time 规则正确 | D |
| R09 | Warehouse Availability | 规则可正确解析 | D |
| R10 | Manual ETA > ERP ETA > DefaultLT | 参数链可表达 | D |
| R11 | DefaultLT 逾期 | Margin 可配置 | D |
| R12 | Arrival Offset | Warehouse 级生效 | D |
| R13 | Planning Yield | 2号位可取正确版本值 | D |
| R14 | MIXED Solver Strategy | 1号位能读取 | E |
| R15 | On-time Target | 取值正确 | E |
| R16 | Candidate 60/90/180 | Snapshot 中正确 | E |
| R17 | MaxImpactedOrders | 仅 Warning 语义 | E |
| R18 | FULL ExpectedDomainKeysJson 多 Domain | 合法 | F |
| R19 | Candidate ExpectedDomainKeysJson 多于 1 个 | 拒绝 | F |
| R20 | FAILED Run 人工恢复 | 新建 Run | F |
| R21 | Candidate 确认 | Actor/Time/Version 可追溯 | F |
| R22 | OA 不可用 | 不阻断最小人工确认能力 | F |

**阶段↔验收映射**：A=6 项（R01~R02）、B=5 项（R03）、C=5 项（R04~R06）、D=9 项（R07~R13）、E=8 项（R14~R17）、F=8 项（R18~R22）。

---

# 附：完成定义（DoD）与联调红线

## 完成定义（DoD，须同时满足）

- 六表治理可用；发布版本不可被静默覆盖；Run 冻结版本可追溯
- Priority Segment 可表达冻结排序；无全局 PriorityScore
- Demand Protection 触发配置可表达；Procurement 参数完整；Planning Yield 参数可取
- Solver Strategy 可被 1号位消费；Candidate Guardrail 完整
- ExpectedDomainKeysJson 规则正确；FULL/PARTIAL/FAILED 生命周期可支持
- Candidate 最小人工确认可用；不依赖完整 OA
- 无逐笔 RPC 规则执行；无 DSL/插件平台过度设计
- R01~R22 通过

## 与 2号位联调红线

- 2号位只能：读取 PUBLISHED 版本、Run 开始时冻结、在内存执行
- 2号位不能：修改 RuleSet、临时写回 Parameter、每笔 Allocation 调用 3号位在线判断
- 3号位不能：返回"订单A优先于订单B"的逐笔结果、替 2号位执行 Allocation、直接修改 DemandBalance/SupplyBalance

## 与 1号位联调红线

- 1号位只消费 Solver Strategy Snapshot
- 3号位不能直接控制 Task / Resource / Start / End
- 1号位不能：自己读治理表、忽略 StrategyProfileVersion、在 Run 中途刷新参数

---

*本清单由《APS_V1_3号位规则参数与运行生命周期开发实施包_v1.0_20260814.md》整理，如有出入以上位文档为准。*

---

# 附二：覆盖度验证结论与约束备注

## 一、覆盖度验证结论（清单 × 实施包 31 章双向核对）

**验证方法**：正向（实施包 31 章每个需求/指令点 → 是否落到 55 条清单）+ 反向（55 条每条 → 是否有原文依据）。

- **正确性：通过** —— 55 条均有实施包原文依据，无凭空添加、无误读；仅 3 处"约束性细节"在清单中隐含未显式（见下表）。
- **完整性：通过** —— 31 章需求点全部落点，无章节级遗漏。

**重点确认项（易漏项均已覆盖）**：
- ✅ 22 个验收场景 R01~R22 全列（#50）
- ✅ 12 项交付物全列（#52）
- ✅ 2↔3 联调红线（3不能：返回逐笔结果 / 替 2号位执行 Allocation / 改 DemandBalance、SupplyBalance）、1↔3 联调红线（3不能直接控制 Task / Resource / Start / End）全列（#53）
- ✅ DoD 17 项全列（#54）
- ✅ 性能三红线：Snapshot 一次加载、不在 Pegging 循环逐笔查库 / Solver 逐 Task 读配置表、Cache Key 含 VersionId（#51）

## 二、约束细节备注（实现时须遵循）

| 出处 | 约束细节 | 说明 |
|---|---|---|
| 第四章（版本生命周期） | "如果现有 DDL 枚举略有不同，以现有兼容结构为准，**不为命名重新建表**" | **已确认（第 0 步盘点）：冻结 DDL v5.1.2 为 6 态** DRAFT / SUBMITTED / APPROVED / PUBLISHED / DISABLED / ARCHIVED；实施包 RETIRED 语义由 DISABLED / ARCHIVED 承载 |
| 十九章（RunType） | **StrategyProfile.RunType 为 DDL 单值枚举**：FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF，非 RunType/Purpose 组合 | 组合语义（CTP、Impact 不得激活等）落 Run 创建校验，不新增 Purpose 列 |
| 10.1（Default Purchase LT） | "**不增加 ProductFamily 维度**；WarehouseCode 全局唯一则不再重复加 Factory 作为配置维度" | 配置维度最小化 |
| 10.3（逾期 Margin） | "默认值由业务配置，3号位只实现治理，**不替 0号位创造新口径**" | 参数默认值不属于 3号位决策范围 |
| 版本绑定（第 0 步盘点） | **IsDefault=1 且 PUBLISHED 是 2号位 Run 绑定的关键语义**（`ScheduleRunService.CreateScheduleRunAsync` 按此取默认版本）；DDL 唯一索引强制"同一包仅一个 IsDefault=1 且 PUBLISHED" | 3号位发布/默认版本接口必须维护该不变量，与 2号位联调红线 |

> 上述均属实现约束而非独立工作条目，不影响正确性判定，实现阶段须遵循。

---

# 附三：3↔5 交集补充与实施裁决（0号位裁决后，2026-08-17）

**背景**：依据 8.17 权威文档核对 3↔5 交集完整性，并按 0号位实施裁决固化（详见《APS_V1_3号位视角_23和35接口缺口清单_v1.2_20260817.md》最终版第四章）。

1. **FrozenFactParameters 与 FrozenStrategySnapshot 关系（裁决 C：已明确，不造第二套 Snapshot）**
   - `FrozenFactParameters` 只是 FrozenStrategySnapshot 中供 5号位复杂事实计算所需参数的最小投影，**非独立版本体系**；
   - V1 优先链路：3号位生成 Snapshot → 2号位一次装载 → 2号位抽取必要 FrozenFactParameters → 批量传给 5号位（PI Position / Timed Supply）；
   - **不新增** FrozenFactParametersVersion / 表 / 独立版本号；不让 5号位逐笔 RPC 调 3号位；一个 Run 只有一份规则真相。

2. **ETA 职责（裁决 D：Invariant 修正）**
   - `Manual ETA > ERP ETA > DefaultLT` 为**冻结业务 Invariant**，不做可配置排序；
   - 3号位治理：Default Purchase LT、逾期 Margin、Arrival-to-Usable Offset 等参数；
   - 5号位/ODS：按固定优先级算 Effective ETA → 应用冻结参数 → AvailableTime；
   - 2号位：消费 TimedSupplyFact 做 Supply 选择 / Balance / Allocation；
   - 清单表述：第 24 条"冻结为 Manual→ERP→DefaultLT（不可做成 Rule Chain）"即 Invariant 语义，实现时不得做成可配置排序。

3. **（已删除）PI Position "UNLOCATED 比例/容忍度" —— 裁决 B：不新增**
   - 冻结规则：无法定位份额进入 UNLOCATED，但 Σ PositionQty 仍必须等于 ERP RemainingQty；无法闭合且无法保守定位 → 严重 Issue / Domain 失败；
   - 5号位实施包"Position 容错阈值"仅指事实计算技术容差可能（浮点/单位换算级），**非业务容忍率**；
   - **不在 D 阶段新增该参数**，不从本清单新增治理项。

**对原 55 条的影响**：正确性不变；补充点 1 撤销（裁决 B），补充点 2/3 按裁决 C/D 固化；FrozenStrategySnapshot 归属按裁决 A（3号位构建、2号位一次装载），执行链固定，不再作为开放问题。
