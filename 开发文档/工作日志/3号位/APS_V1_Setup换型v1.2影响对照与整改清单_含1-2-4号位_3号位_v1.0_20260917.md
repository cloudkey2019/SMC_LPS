# 3号位 Setup换型 v1.2 影响对照与整改清单（含 1/2/4 号位）

> 发件：3号位（规则/参数/认证权限/运行生命周期治理）
> 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》
> 日期：2026-09-17
> 性质：对 v1.2 全量影响分析，逐号位列「v1.2 冻结口径 ↔ 现状 ↔ 待改动作」，供回写正式基线 + 派单

---

## 一、总况

v1.2 推翻旧的「属性式换型」口径：废止 `SetupAttribute`（Mold/Tool/Material/Color）与 MES / `RoutingOperation.SetupTime` 运行真相，改为**「当前工序 + 当前设备 + 前产品 + 后产品 → SetupMinutes」的明确规则模型**。Setup 是真实资源占用（非排序分数），从初始资源/时间槽评价阶段就参与求解。

本职边界（§19.1）：3号位 负责 Setup 规则治理、RuleSetVersion 内容、冲突校验、权限、审计、算法参数治理、本 Run 规则版本冻结、Setup 维护 API；不负责 Pegging / Solver 序列计算。

**§0 红线**：本轮为「冻结文档修改指导」，正式文档更新前各号位可提实现疑问，但**不得绕过冻结文档直接自由编码**——先做影响分析与回写，再动代码。

---

## 二、对 3号位 的影响（重点）

### 2.1 冻结块字段删改（按 0号位 五段式）

| 字段 | 中文业务含义 | 谁在用 | 替代 | 是否改变业务 |
|---|---|---|---|---|
| `SolverStrategyBlock.Setup.Dimensions`（Mold/Tool/Material/Color）| 换型属性维度（按模具/刀具/颜色/材质判「同不同属性」）| 1号位 `SetupOptimizer` 读 `SchedulingContext.SetupAttribute` 的契约依据；2号位 整块透传 | 无——属性式整体废止，由 `SetupTransitionRule`（EXACT/DEFAULT 规则查表）取代 | **是**（属性匹配判型 → 规则查表） |
| `Setup.DefaultSetupMinutes = 30` | 全局统一默认换型 30 分钟 | Solver 消费默认换型；`SolverStrategyValidator` 有 `E4_SetupDefaultSetupMinutes_非正_拒绝` | DEFAULT 规则（部门+Stage+工序+设备 → DefaultSetupMinutes）进 RuleSetVersion；无规则 → 0 分钟 | **是**（全局 30 分钟禁用） |
| `Setup.SetupLookAheadSize = 5` | 局部相邻 Task 换型启发式的 look-ahead 窗口 | Solver 局部相邻修复的邻域窗口 | 有界搜索预算 / 最大邻域尝试次数 / 单生产日保护阈值，进 ParameterSetVersion | **是**（固定窗口 → 有预算有界邻域搜索） |

> 趋势：`SolverStrategyBlock.Setup`（`SetupParams`）三字段全部失去原语义——Setup 规则迁入 RuleSetVersion、算法数值迁入 ParameterSetVersion（§8.2）。该嵌块的去留需与 2号位 对齐 2↔3 契约后再定，不先自由删。

### 2.2 3号位 新增治理

- **新实体 `SetupTransitionRule`**（§九）：`RuleSetVersionId + ProductionDepartmentId + StageCode + OperationCode + ResourceId + FromMaterialId? + ToMaterialId? + RuleType(EXACT/DEFAULT) + SetupMinutes + IsActive + 审计字段`
- **唯一性/冲突**（§十）：EXACT 唯一键 `RuleSetVersion+Dept+Stage+Operation+Resource+From+To`；DEFAULT 唯一键 `RuleSetVersion+Dept+Stage+Operation+Resource`；**发布前冲突阻止**，不得让 Solver 运行时随机选规则
- **命中优先级**（§五）：EXACT → DEFAULT（工序+设备）→ 无规则 0 分钟（登记解释）；同产品 A→A 默认 0 分钟（§六）
- **校验迁移**：`SolverStrategyValidator` 删除 `DefaultSetupMinutes` 相关校验；新增「规则冲突校验 + 发布前阻止」
- **缺失规则治理**（§十八）：按 0 分钟兜底次数统计，六维 `Dept/Stage/Operation/Resource/From/To`，供 4号位 查询补维护
- **Setup 维护 API**（§19.1）：承接 4号位 明确/默认规则保存到 DRAFT RuleSetVersion

### 2.3 两件既有文书作废

- 《P1-02 致 0号位 Setup换型属性数据源裁决请求》：v1.2 已直接答复（废止属性式、无需补数据源）→ **作废，结项**
- 《P1-02 致 2号位 Setup换型数据链确认与催办》：催办的「MES SetupTime/属性字段」不再需要 → **结项**

---

## 三、对 2号位 的影响

| 项 | v1.2 口径 | 现状/待办 |
|---|---|---|
| 规则装载 | 按本 Run Frozen Strategy Snapshot 装载 Setup 规则，只加载本 Domain 涉及规则，形成内存快速查找并传 1号位 | 待：Setup 规则如何进 FrozenStrategySnapshot（2↔3 契约变更，见 §六） |
| 快速查找结构 | `Resource+Operation+From+To → SetupMinutes`；`Resource+Operation → DefaultSetupMinutes` | 待建设 |
| 业务范围裁剪 | ProductionDepartment / Stage 作为规则裁剪、唯一性、业务范围上下文保留 | 沿现有 RuleSet 裁剪逻辑扩展 |
| DDL | 新表 `SetupTransitionRule`（含唯一键约束）| 2号位 专属执行；3号位 只提变更申请（红线：DB 结构归 2号位） |
| MES SetupTime | `RoutingOperation.SetupTime` 历史兼容列退役：新代码不读、不 fallback、不与新规则叠加；是否 DROP 由兼容评估定 | 2号位 治理存量列 |

## 四、对 1号位 的影响

| 项 | v1.2 口径 | 现状/待办 |
|---|---|---|
| Setup 语义 | 真实资源占用（非排序分数），计入 Resource Timeline / Start/End / 冲突 / 交期 / 稼动 | 待改 |
| 求解阶段 | 初始 Resource/时间槽评价即消费 Setup，随后序列优化再消费（不能排完再补）| 待改 |
| 换型键 | 一律用「被排入的当前 Task 自身 `OperationCode` + Resource + 前产品 + 当前产品」，不读上一 Task 工序 | 待改（废止 FromOperation/ToOperation 模型） |
| 夜间 FULL | 单 Resource × 生产日 × 固定锚点间可移动段，全候选 + 有界邻域搜索，不枚举排列、不承诺全局最优 | 待改 |
| 白天 Candidate | 局部优先，重算受影响邻接 Setup，不洗牌全天 | 沿现有 Candidate 增强 |
| 边界 | 生产日只是搜索边界，不重置设备上一产品状态；无上一产品 → 初始设备状态 Setup=0 + Explanation | 待覆盖 |
| **废止代码** | `SchedulingContext.SetupAttribute`（属性式）→ `SetupOptimizer` 重写为查规则表 | **重点整改** |
| 禁令 | 禁读 3号位 库、禁自维护规则、禁 `RoutingOperation.SetupTime` fallback、禁全厂/90 天全排列、禁建第二套 Solver | 校验项 |

## 五、对 4号位 的影响

| 项 | v1.2 口径 | 待办 |
|---|---|---|
| 明确产品转换维护 | 选 部门/大工艺/工序/前产品/后产品 → 按 `OperationResourceEligibility` 显共同合法设备 → 选设备录分钟 → 存 DRAFT RuleSetVersion | 待建页面 |
| 默认规则维护 | 选 部门/大工艺/工序/设备/默认分钟 | 待建页面 |
| 动态建议设备集合 | `B 当前 Operation 合法 Resource ∩ A 同部门/Stage 任一 Operation 可用 Resource`；仅维护辅助，保存后落具体 Resource，不落永久组 | 待建 |
| 治理辅助 | 冲突提示、缺失规则查询、0 分钟兜底查询、规则版本/Diff/发布状态 | 待建 |
| 缺失规则补维护 | 按 0 分钟兜底六维统计补高频/长换型/瓶颈设备/稼动明显工序 | 待建查询 |

> 5号位：§19.5 明确无 Setup 核心新增职责（不建事实/不推属性/不维护规则/不定时间），本轮不派。

---

## 六、跨号位契约迁移点（需对齐后才动代码）

1. **FrozenStrategySnapshot 中 Setup 的承载方式**：现行 `SolverStrategyBlock.Setup`（三参数）按 §8.2 应拆分——规则内容走 RuleSetVersion、算法预算走 ParameterSetVersion。这改的是 2↔3 契约，**须 0号位 定 + 2号位 对齐再改**。
2. **1↔2 数据通道**：2号位 把 Setup 规则查找结构装入 `DomainSolveRequest`，1号位 换读（废止 `SetupAttribute`）。
3. **DDL 归属**：`SetupTransitionRule` 建表走 2号位；3号位 只提交变更申请并治理内容。

---

## 七、旧口径覆盖清单（§20 → 各号位）

| 旧口径 | v1.2 | 主要落点 |
|---|---|---|
| MES 已有 SetupTime | 废止 | 2号位 退役列 / 1号位 禁读 |
| Setup 依赖 Mold/Tool/Color/Material 属性 | 废止 | 3号位 删 `Dimensions`；1号位 删 `SetupAttribute` |
| `RoutingOperation.SetupTime` 进 Solver | 废止 | 通链移除 |
| 前小工序+后小工序模型 | 废止 | 1/2号位 改单 `OperationCode` |
| Setup 只局部相邻修复 | 部分覆盖（初始即参与 + 夜间有界序列优化 + 插入重算）| 1号位 |

---

## 八、结论 + 派单建议

v1.2 是 Setup 的**口径级翻转**：3号位 冻结块 `SetupParams` 三字段全部失义、需新增 `SetupTransitionRule` 治理；2号位 需改契约装载 + 建表 + 退役 MES SetupTime；1号位 需重写 `SetupOptimizer` 为规则查表 + 夜间有界序列优化；4号位 需建维护页面与缺失治理查询。

**按 §0 红线，本轮先做影响分析（本清单即产出），不动代码**；下一动作建议：

- **3号位**：据此回写正式基线中 Setup 相关章节 + 准备 `SetupTransitionRule` 契约草案（五段式删字段拟案一并附上），报 2号位 对齐 §六.1 契约承载方式。
- **0号位**：确认 §六.1（Setup 规则在 FrozenStrategySnapshot 的承载）为悬置裁决项。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）