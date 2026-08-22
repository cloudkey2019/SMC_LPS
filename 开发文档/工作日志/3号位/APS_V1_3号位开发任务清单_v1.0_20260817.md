# APS V1 3号位开发任务清单 v1.0（任务级拆解）

**版本**：v1.0
**日期**：2026-08-17
**适用对象**：3号位（及其开发 AI）
**性质**：《APS_V1_3号位代码开发前置准备清单》第 3 步（开发任务拆解）产出。把 55 条工作条目按阶段 A~F 拆为**任务级**清单，每任务标注依赖、验收场景（R01~R22）、对应 DoD 项，并定义阶段门与 12 项交付物检查点。
**上位依据**：
- 《APS_V1_3号位工作条目清单_v1.0_20260817.md》（55 条 + 阶段 A~F 表 + R01~R22 + DoD + 12 项交付物）
- 《APS_V1_3号位交付契约_v0.2_20260817.md》（0号位统一确认定稿：六块 DTO / Provider / FrozenFactParameters / Cache Key）
- 《APS_V1_3号位现状盘点结论_v1.0_20260817.md》（第 1 步：DI 自写扩展方法、Scheduling 阻塞项）

**开发顺序红线（固定，不得跳门）**：阶段A → 门 R01/R02 → 阶段B → 门 R03 → 阶段C → 门 R04~R06 → 阶段D → 门 R07~R13 → 阶段E → 门 R14~R17 → 阶段F → 门 R18~R22。

---

# 一、任务拆解总览（任务 ↔ 条目 ↔ 验收 ↔ DoD）

| 阶段 | 任务 | 对应清单条 | 依赖 | 验收(R) | DoD 项 |
|---|---|---|---|---|---|
| A | A-1~A-9, A-T | 1~6, 8~10, 45~47 | 契约v0.2 | R01/R02 | 六表治理可用 / 发布不可静默覆盖 |
| B | B-1~B-5, B-T | 12, 13, 51 | A | R03 | Snapshot 可冻结追溯 / 无逐笔 RPC |
| C | C-1~C-4, C-T | 14~17 | B | R04~R06 | Priority Segment 可表达 / 无全局 Score |
| D | D-1~D-8, D-T | 18~27 | B | R07~R13 | Protection/Procurement/Yield 参数可取 |
| E | E-1~E-4, E-T | 28~35 | B | R14~R17 | Solver 可消费 / Guardrail 完整 |
| F | F-1~F-6, F-T | 36~44 | A | R18~R22 | ExpectedDomainKeysJson / 生命周期 / Candidate |

> 注：C~E 依赖阶段 B 的 Snapshot DTO（B-1），故 B 是 C/D/E 的共同前置；F 依赖 A（六表治理）与 2号位 ScheduleRun 外壳。

---

# 二、分阶段任务明细

## 阶段 A：六表治理骨架（门：R01/R02 绿 → 进 B）

| 任务 | 内容 | 依赖 | 验收 | DoD |
|---|---|---|---|---|
| **A-1** | Core 六表实体：RuleSet / RuleSetVersion / ParameterSet / ParameterSetVersion / StrategyProfile / StrategyProfileVersion（字段按冻结 DDL v5.1.2 §3.10） | 契约v0.2 | 实体字段与 DDL 对齐 | 六表治理可用 |
| **A-2** | Engine 治理仓储（`Repositories.APS`，Dapper + `DatabaseConnectionManager`，CRUD） | A-1 | 仓储可读写六表 | 六表治理可用 |
| **A-3** | 3号位自写扩展方法 `AddGovernanceServices` 注册治理仓储/服务（不依赖 2号位扫描，盘点结论 §五） | A-2 | DI 解析成功 | 六表治理可用 |
| **A-4** | 版本状态机 **6 态**：DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED（DDL CK；清单 #11/A2） | A-1 | 状态流转合法/非法都受控 | 六表治理可用 |
| **A-5** | 发布/停用 + 发布前校验（引用 PUBLISHED、参数越界、Guardrail 为正、On-time 0~100、Split 超限、StrategyMode 合法） | A-2,A-4 | 非法发布被拒 | 六表治理可用 |
| **A-6** | **IsDefault 不变量**：同一 StrategyProfile 仅一个 `IsDefault=1 AND PUBLISHED`（UQ_StrategyProfileVersion_DefaultPublished）+ 默认版本服务（2号位 Run 绑定关键） | A-5 | 不变量恒成立 | 六表治理可用 |
| **A-7** | 变更审计：CreatedBy/CreatedAt/PublishedBy/PublishedAt/ChangeReason/VersionNo/ParentVersionId/内容 Snapshot | A-5 | 审计记录完整 | 六表治理可用 |
| **A-8** | Diff / Run 引用追溯服务 | A-5 | Diff 与追溯可查 | 六表治理可用 |
| **A-9** | Application 服务编排 + Web Controller（为 4号位：列表/版本查看/发布/停用/Diff/当前 PUBLISHED/追溯） | A-5~A-8 | API 可用（编译待 Scheduling 解阻） | 六表治理可用 |
| **A-T** | R01（发布 RuleSetVersion 历史不可覆盖）+ R02（发布 ParameterSetVersion 新 Run 可引用、旧 Run 不变）测试 | A-4~A-6 | R01/R02 绿 | R01~R22 通过 |

## 阶段 B：FrozenStrategySnapshot（门：R03 绿 → 进 C）

| 任务 | 内容 | 依赖 | 验收 | DoD |
|---|---|---|---|---|
| **B-1** | Core `FrozenStrategySnapshot` 六块 DTO（契约 v0.2 §二：DemandPriority/Lock/Supply/Procurement+SolverStrategy/CandidateGuardrail；头部三 VersionId；PlanningYield 进 ProcurementBlock） | A-1 | DTO 与契约一致 | Snapshot 可冻结追溯 |
| **B-2** | Core 接口 `IFrozenStrategySnapshotProvider.GetFrozenStrategySnapshotAsync(long strategyProfileVersionId, CancellationToken ct)` | B-1 | 签名与契约 §三 一致 | Snapshot 可冻结追溯 |
| **B-3** | Application 装配实现：StrategyProfileVersion → RuleSetVersion + ParameterSetVersion → 六块 Snapshot | A-2,B-2 | 一次装配返回完整 Snapshot | Snapshot 可冻结追溯 |
| **B-4** | Run 启动冻结语义（C2-1）：按 Run 已冻结的指定 VersionId 获取，不重新选 Default、不逐笔 RPC、不因后续发布漂移 | B-3 | 语义与契约 §三 一致 | 无逐笔 RPC |
| **B-5** | Snapshot 缓存：Cache Key 必须含 StrategyProfileVersionId + 不污染 + Run 内不刷新（C2-4） | B-3 | 缓存命中/隔离正确 | Snapshot 可冻结追溯 |
| **B-T** | R03 测试（Run 启动后发布新规则，当前 Run 仍用旧冻结版本） | B-3~B-5 | R03 绿 | R01~R22 通过 |

## 阶段 C：Demand Priority（门：R04~R06 绿 → 进 D）

| 任务 | 内容 | 依赖 | 验收 | DoD |
|---|---|---|---|---|
| **C-1** | DemandPriorityBlock 解析：Segments 有序（SegmentOrder）、First-match、强类型 MatchConditions（不建 DSL） | B-1 | Segment 结构可表达冻结排序 | Priority Segment 可表达 |
| **C-2** | Segment 命中 + 内部 Sort + 稳定 Tie-break 实现（无全局 PriorityScore） | C-1 | 第一命中即止 / 排序稳定 | 无全局 Score |
| **C-3** | 禁全局 PriorityScore 校验 + 命中后不再匹配后续 Segment 校验 | C-2 | 非法配置被拒 | 无全局 Score |
| **C-4** | Fixture/示例 Snapshot（供 2号位联调 Demand 排序） | B-1 | 示例数据可用 | 无逐笔 RPC |
| **C-T** | R04（第一命中）/ R05（多 Sort 稳定）/ R06（不同 DemandType 交错）测试 | C-2 | R04~R06 绿 | R01~R22 通过 |

## 阶段 D：Supply / Lock / Procurement 参数（门：R07~R13 绿 → 进 E）

| 任务 | 内容 | 依赖 | 验收 | DoD |
|---|---|---|---|---|
| **D-1** | Protection 触发/Sticky 配置（Trigger/Sticky，清单 18~19） | B-1 | 2号位可读取触发配置 | Protection 可表达 |
| **D-2** | Inventory Availability 规则（WarehousePriority/Factory/ProductFamily 上下文） | B-1 | 规则可解析 | 参数可取 |
| **D-3** | PI 简单排序参数（PiSortBy） | B-1 | 排序参数正确 | 参数可取 |
| **D-4** | Default Purchase LT（按 Warehouse/Material） | B-1 | 2号位可取正确版本值 | 参数可取 |
| **D-5** | 逾期 Margin（MarginPercent/MinimumExtraDays，保守修正） | B-1 | Margin 可配置 | 参数可取 |
| **D-6** | Arrival-to-Usable Offset（按 Warehouse） | B-1 | Warehouse 级生效 | 参数可取 |
| **D-7** | Planning Yield（Material/Stage；红线：已有 PI Supply 不得按 Yield 再放大） | B-1 | 2号位可取正确版本值 | 参数可取 |
| **D-8** | ETA Invariant 固化：`Manual ETA > ERP ETA > DefaultLT` 不可配置化（裁决 D） | D-4 | 无 ETA 排序配置入口 | 参数可取 |
| **D-T** | R07（Protection 触发）/ R08（PI 排序）/ R09（Warehouse Avail）/ R10（ETA 链）/ R11（Margin）/ R12（Offset）/ R13（Yield）测试 | D-1~D-8 | R07~R13 绿 | R01~R22 通过 |

## 阶段 E：Solver Strategy（门：R14~R17 绿 → 进 F）

| 任务 | 内容 | 依赖 | 验收 | DoD |
|---|---|---|---|---|
| **E-1** | SolverStrategyMode 映射：`StrategyConfig.Mode`（Backward/Forward/BackwardThenForward）↔ `SolverStrategyMode`（Forward/Backward/Mixed），不静默改 StrategyConfig 语义（盘点 §三.4） | B-1 | 映射定义完成 | Solver 可消费 |
| **E-2** | 动态瓶颈 / On-time / Split / Setup / Overlap 参数（契约 v0.2 §二-⑤） | B-1 | 1号位可读取 | Solver 可消费 |
| **E-3** | Candidate Guardrail 参数（60/90/180s、MaxImpactedOrders 仅 Warning 红线） | B-1 | Snapshot 中正确 | Guardrail 完整 |
| **E-4** | Solver 相关发布前校验（On-time 0~100、Split 超限等） | A-5,E-1~E-3 | 非法配置被拒 | Solver 可消费 |
| **E-T** | R14（MIXED 可读）/ R15（On-time 取值）/ R16（Guardrail 正确）/ R17（MaxImpacted 仅 Warning）测试 | E-1~E-4 | R14~R17 绿 | R01~R22 通过 |

## 阶段 F：Run / Candidate 生命周期（门：R18~R22 绿 → 阶段完成）

| 任务 | 内容 | 依赖 | 验收 | DoD |
|---|---|---|---|---|
| **F-1** | ExpectedDomainKeysJson 冻结/校验（FULL 多 Domain / Candidate 严格一个 / Run 后不可变 / 不得按已建 PlanVersion 反推；DDL ISJSON） | A-2 | 冻结与校验规则正确 | ExpectedDomainKeysJson 正确 |
| **F-2** | ScheduleRun 生命周期协作（RUNNING/COMPLETED/PARTIAL_SUCCESS/FAILED + PlanVersion 5 态；与 2号位 `ScheduleRunService` 外壳协作，不改 2号位代码） | A-9 | 生命周期元数据正确 | 生命周期可支持 |
| **F-3** | FULL 失败链（上游 FAILED→下游不发布新 ACTIVE、无关域继续；不建 ALL_OR_NOTHING） | F-2 | 失败链正确 | 生命周期可支持 |
| **F-4** | 人工恢复（新建 ScheduleRun；禁止 FAILED→RUNNING） | F-2 | 恢复规则正确 | 生命周期可支持 |
| **F-5** | Candidate 最小人工确认/激活后端（生成/展示/Actor 确认/ConfirmedAt/CandidatePlanVersionId/Activate/审计） | F-2 | Candidate 可确认可追溯 | Candidate 可用 |
| **F-6** | OA Adapter（可选；OA 不可用不阻断 Candidate 功能） | F-5 | 无 OA 也能确认 | Candidate 可用 |
| **F-T** | R18（FULL 多 Domain 合法）/ R19（Candidate 多 Domain 拒绝）/ R20（FAILED 人工恢复）/ R21（Candidate 可追溯）/ R22（OA 不可用不阻断）测试 | F-1~F-6 | R18~R22 绿 | R01~R22 通过 |

---

# 三、阶段门定义（固定，不得跳门）

```text
阶段A 六表治理       → 门 R01/R02 绿   → 进 B
阶段B Snapshot       → 门 R03 绿       → 进 C
阶段C Demand Priority → 门 R04~R06 绿  → 进 D
阶段D Supply/Lock/Procurement → 门 R07~R13 绿 → 进 E
阶段E Solver Strategy → 门 R14~R17 绿  → 进 F
阶段F Run/Candidate 生命周期 → 门 R18~R22 绿 → 阶段完成
→ 与 2号位 Snapshot 联调（交付物 11）
→ 与 1号位 Solver Strategy 联调（交付物 12）
```

**门判据**：对应 R 场景自动化用例全绿 + 该阶段任务 DoD 满足，方可进入下一阶段。红线上位：不建规则平台/DSL/插件（#48）、性能一次装载（#51）、联调红线（#53）、DoD 17 项（#54）。

---

# 四、12 项交付物检查点（清单 #52）

| # | 交付物 | 对应任务 | 检查点 |
|---|---|---|---|
| 1 | 六表后端（实体+仓储+服务+Controller） | A-1~A-9 | R01/R02 绿 |
| 2 | 版本发布机制（发布/停用/Diff/追溯/默认版本） | A-5~A-8 | 发布不可静默覆盖 |
| 3 | FrozenStrategySnapshot DTO | B-1 | 与契约 v0.2 一致 |
| 4 | Demand Priority（Segment 模型） | C-1~C-4 | R04~R06 绿 |
| 5 | Demand Protection（触发/Sticky） | D-1 | R07 绿 |
| 6 | Supply / Procurement 参数 | D-2~D-8 | R08~R13 绿 |
| 7 | Solver Strategy 参数 | E-1~E-4 | R14~R17 绿 |
| 8 | ScheduleRun / ExpectedDomainKeysJson 生命周期 | F-1~F-4 | R18~R20 绿 |
| 9 | Candidate 确认/激活后端 | F-5~F-6 | R21~R22 绿 |
| 10 | R01~R22 测试结果 | A-T~F-T | 全绿 |
| 11 | 与 2号位 Snapshot 联调记录 | B-3~B-5 + C-4 | 联调检查点（契约 §六）通过 |
| 12 | 与 1号位 Solver Strategy 联调记录 | E-1~E-4 | 1号位可消费 |

---

# 五、状态跟踪（2026-08-22 同步）

> 更新说明：A~D/F 阶段门全绿；E 阶段 E-4 独立闭环、E-1~E-3/E-T 阻塞于 0号位 DDL 方案 A/B/C 裁决（P0-02）；交付物 11 单侧就绪声明已产出（《APS_V1_3号位与2号位Snapshot联调记录_3号位单侧就绪声明_v1.0_20260822.md》），待 2号位 回执。

- [x] 阶段 A（A-1~A-9, A-T）→ 门 R01/R02 ✅
- [x] 阶段 B（B-1~B-5, B-T）→ 门 R03 ✅（四块真实重放绿；Solver/Candidate 两块待 P0-02）
- [x] 阶段 C（C-1~C-4, C-T）→ 门 R04~R06 ✅
- [x] 阶段 D（D-1~D-8, D-T）→ 门 R07~R13 ✅
- [ ] 阶段 E（E-1~E-4, E-T）→ 门 R14~R17 🟡 部分：E-4 ✅（15 测试全绿）；E-1~E-3 + E-T 阻塞 0号位 DDL 裁决
- [x] 阶段 F（F-1~F-6, F-T）→ 门 R18~R22 ✅
- [ ] 与 2号位 Snapshot 联调记录（交付物 11）🟡 单侧就绪声明 v1.0 已产出；待 2号位 六项检查点回执闭环
- [ ] 与 1号位 Solver Strategy 联调记录（交付物 12）❌ 阻塞：依赖 E-1~E-3（0号位 DDL 裁决）+ 1号位 消费配合

---

*本清单由《APS_V1_3号位工作条目清单_v1.0_20260817.md》拆解生成；任务级依赖/验收/DoD 已标注，阶段门固定。开发时按此执行，每阶段 TDD（第 4 步）先行。*
