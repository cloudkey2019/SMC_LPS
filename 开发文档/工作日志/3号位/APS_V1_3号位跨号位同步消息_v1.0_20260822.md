# APS V1 3号位 跨号位同步消息（致 0号位 DDL 裁决 / 致 2号位 联调回执）

> 版本：v1.0
> 日期：2026-08-22
> 发送方：3号位
> 接收方：0号位（消息 1）、2号位（消息 2）
> 用途：跨号位同步留痕。消息 1 催促 DDL 方案 A/B/C 裁决（检查项 11~13）；消息 2 同步交付物 11 单侧就绪声明并请求六项检查点回执。
> 关联：《APS_V1_3号位六块Snapshot持久化来源映射表_v1.0_20260819.md》、《APS_V1_3号位交付契约_v0.2_20260817.md》、《APS_V1_3号位与2号位Snapshot联调记录_3号位单侧就绪声明_v1.0_20260822.md》

---

## 消息 1：致 0号位 — DDL 方案 A/B/C 裁决提醒（检查项 11~13 仍待答复）

> **主题：3号位 P0-01 待裁决项仍未关闭，请求裁决 DDL 方案 A/B/C**

0号位您好，3号位 就第三轮复审检查项 11~13 的 DDL 裁决事项再次同步（截至 2026-08-22 仍未收到答复）：

### 一、阻塞问题是什么

第三轮复审冻结检查清单（二轮复审报告 v1.2 §9）中 **11/12/13 三项未关闭**，根因收敛为一个技术决策：

- **11**：冻结 DDL（v5.1.2）的 RuleSetVersion/ParameterSetVersion 无"发布内容快照"字段，与 3号位 六块 Snapshot 持久化来源不一致；
- **12**：FrozenStrategySnapshot 六块中 SolverStrategy/CandidateGuardrail **仍为空对象**（无真实版本来源），无法按 VersionId 重放具体值；
- **13**：这两块未纳入"缺失/损坏一律失败"语义。

### 二、为什么必须 0号位 裁决

红线 #6：数据库结构变更由 2号位 专属执行，3号位 不得自行 ALTER 冻结 DDL。3号位 已遵守承诺：未 ALTER、未扩写依赖非冻结字段的 Repository SQL。

### 三、已提交待裁决的方案

《六块Snapshot持久化来源映射表_v1.0_20260819.md》§五 提供三方案：

| 方案 | 说明 | 特点 |
|---|---|---|
| **A（推荐）** | RuleSetVersion/ParameterSetVersion 各增 1 个内容 Snapshot 列 | 最简，满足全部约束，不新增表/版本体系 |
| B | 主题规则表 + 版本外键 | 字段级可维护，但新增多表、DDL 改动大 |
| C | 保持现状 JSON 列补进 DDL | 代码改动最小，但与 0号位"不增加主题专用列"方向冲突 |

三方案均按 0号位 6 条约束设计：

1. 发布后不可变；
2. 能按 StrategyProfileVersionId 恢复当次 Run 完整 Snapshot；
3. 不新增独立 Snapshot 表；
4. 不新增新的版本号体系；
5. 不把主题规则表废掉；
6. 不建设 DSL/RuleCondition/RuleAction 平台。

### 四、待确认项（映射表 §六）

1. 采用方案 A / B / C 中哪一种；
2. 若 A：Snapshot 内容字段命名与格式（单一 JSON 聚合 vs 分块）；
3. 若 B：主题表清单与版本绑定字段；
4. 是否需在版本表补充 ChangeReason / 审计落点。

### 五、阻塞影响清单（裁决前无法推进）

- 检查项 11~13 未关闭（第三轮复审未全绿）；
- 阶段 E：E-1（Mode 映射）/E-2（Solver 参数）/E-3（Guardrail）真实来源重放 + E-T 门 R14~R17；
- 交付物 7（Solver Strategy 参数）、12（与 1号位 联调）；交付物 10 的 R01~R22 仍为 **18/22**。

### 六、等待期间 3号位 已完成（未空等）

- E-4 Solver 发布前校验：新增 2 个校验器 + 15 测试全绿（不依赖 DDL，裁决后仅需接线）；
- 交付物 11 单侧就绪声明（致 2号位 的联调记录前置部分）；
- 《P0-02执行蓝图》备妥：三方案 → 精确代码落点，**裁决即执行，无需重新调研**。

### 七、请求

请按映射表 §六 四项逐项确认。裁决后 3号位 立即：按裁决同步实体/Repository/契约文档 6.11 → Provider 装载两块真实 JSON（缺失/损坏一律失败）→ 补具体值重放断言 → 第三轮复审 11~13 闭环。

---

## 消息 2：致 2号位 — Snapshot 联调就绪声明（交付物 11 单侧部分）

> **主题：3号位 与 2号位 Snapshot 联调——3号位 单侧就绪，请回执六项检查点**

2号位您好，交付物 11（与 2号位 Snapshot 联调记录）的 3号位 单侧部分已就绪：`开发文档/工作日志/3号位/APS_V1_3号位与2号位Snapshot联调记录_3号位单侧就绪声明_v1.0_20260822.md`。请知悉并按契约回执。

### 一、契约依据（重要）

联调检查点定义于《交付契约 v0.2》§六，该版本已由 **0号位 统一确认定稿**（代 2号位/5号位 收敛）。按契约：**2号位 回来后仅做代码联调，不再获得重新决定契约的权力**。

### 二、3号位 侧已就绪能力

- `IFrozenStrategySnapshotProvider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, ct)`：按已冻结 VersionId 一次装配六块；
- 六块 DTO 头部显式携带三 VersionId（同一包内一致）；
- 四块（DemandPriority/Lock/Supply/Procurement）真实 JSON 重放 + 具体值断言；缺失/损坏一律抛异常；
- B-5 Snapshot 缓存（键含 VersionId、不污染、Run 内不刷新）；
- C-4 Demand 排序 Fixture（供 2号位 联调）。

### 三、六项检查点（契约 §六）3号位 侧状态

| # | 检查点 | 3号位 侧 | 待 2号位 |
|---|---|---|---|
| 1 | 按冻结 VersionId 一次装载 + 内存执行、不漂移 | ✅ Provider 入参即 VersionId，无逐笔 | 端到端调用验证 |
| 2 | 三 VersionId 同源、一 Run 一致 | ✅ DTO 显式 + 集成测试断言 | 对齐 |
| 3 | 默认版本语义（未指定时取唯一 PUBLISHED） | ✅ 默认治理就绪（A-6 不变量）+ `ResolveDefaultStrategyProfileVersionAsync`（3号位 已实现） | **2号位 Run 启动调用 3号位 默认解析一次并缓存，不建第二套默认 SQL**（0号位 P1-02 修正，见附录） |
| 4 | Cache Key 含 VersionId + 不污染 + 不刷新 | ✅ B-5 已实现 | 缓存键字符串对齐（不冻结） |
| 5 | 六块 + PlanningYield 覆盖、三 VersionId 显式 | ✅ 结构齐全 | 核对 |
| 6 | 2号位 抽 FrozenFactParameters 转交 5号位 | ✅ 契约已禁 `Snapshot.ToFrozenFactParameters()`（避免 3→5 隐形依赖） | **抽取 Mapping 由 2号位 实现** |

### 四、需 2号位 配合/回执的 5 点

1. Run 启动调用 Provider 一次装载验证（检查点 1 端到端）；
2. Run 启动调用 3号位 默认解析（`ResolveDefaultStrategyProfileVersionAsync`）一次并缓存（检查点 3，0号位 P1-02 修正：2号位 不自行实现默认版本 SQL）；
3. 缓存键字符串对齐（检查点 4）；
4. `FrozenFactParameters` 抽取 Mapping（检查点 6）；
5. ⚠️ Solver/Candidate 两块真实来源**待 0号位 DDL 方案 A/B/C 裁决**——与四块**解耦**，不阻塞四块联调。

### 五、请求

请按上表 6 项检查点逐一回执（✅/❌ + 备注）。回执后本记录升级为完整联调记录（交付物 11 闭环）。

---

## 与 0号位验收包 §六 对照（汇报附件）

> 依据：《APS_V1_0号位总体项目验收包_v1.0_20260814.md》§六（3号位验收）。3号位 全部提报/证据按此对照，0号位 复核时以此为准。

### §6.1 必须交付（11 项）

| # | 验收项 | 3号位 状态 | 证据落点 |
|---|---|---|---|
| 1 | 六张规则/参数治理表后端 | ✅ | 阶段 A（A-1~A-9，交付物 1） |
| 2 | 版本发布 | ✅ | A-5~A-8（交付物 2） |
| 3 | FrozenStrategySnapshot | ✅ | B-1~B-5（交付物 3） |
| 4 | Priority Segment | ✅ | C-1~C-4（交付物 4，R04~R06 绿） |
| 5 | Demand Protection 参数 | ✅ | D-1（交付物 5，R07 绿） |
| 6 | Procurement 参数 | ✅ | D-2~D-8（交付物 6，R08~R13 绿） |
| 7 | Solver Strategy | 🟡 E-1~E-3 阻塞 0号位 DDL 裁决；E-4 校验器已闭环 | 蓝图备妥，裁决即执行 |
| 8 | Candidate Guardrail | 🟡 E-3 阻塞同上；E-4 校验器已闭环 | 同上 |
| 9 | ScheduleRun 生命周期 | ✅ | F-1~F-4（交付物 8，R18~R20 绿） |
| 10 | Candidate 最小确认 | ✅ | F-5~F-6（交付物 9，R21~R22 绿） |
| 11 | R01~R22 测试结果 | 🟡 18/22 绿（R14~R17 阻塞） | 《R01-R22验收证据映射表》 |

### §6.2 必须确认（A~D）

| 确认项 | 3号位 状态 | 证据落点 |
|---|---|---|
| A. 一次 Run 只用一份冻结版本 | ✅ 冻结语义已实现（按指定 VersionId 一次装载，不漂移）；端到端行为待 2号位 验证 | B-4 + 联调检查点 1（契约 §六 #1） |
| B. 不恢复 PriorityScore | ✅ 排序链路 CalculationLayer → Priority Segment → First Match → Segment Sort，无全局 Score | C-2/C-3 + 验收提报 §三 19 |
| C. 不做逐笔在线规则调用 | ✅ 一次装载进内存，无逐笔 RPC | B-4 + 联调检查点 1 |
| D. 不建设万能规则平台 | ✅ 无 DSL/脚本/任意 SQL/Plugin/通用编排 | 验收提报 §三 19 + 红线 #5 |

> ⚠️ 7/8/11 三项（Solver Strategy / Candidate Guardrail / R14~R17）收敛到同一个外部依赖：**0号位 DDL 方案 A/B/C 裁决**（检查项 11~13）。裁决后按《P0-02执行蓝图》收口，§6.1 即 11/11 全 ✅。

---

## 附录：0号位 答复与裁决应用（2026-08-22）

依据《APS_V1_3号位代码第三轮正式复审报告_a5741a7_Commit冻结版(1).md》：

1. **DDL 方案 A/B/C 裁决**：批准**方案 A**——RuleSetVersion / ParameterSetVersion 各增 1 个通用发布内容快照字段 `ContentSnapshotJson NVARCHAR(MAX) NULL`；不新增表/版本体系/主题专用列；不新增 ChangeReason（用 GovernanceAuditLog）；由数据库责任方（2号位）执行正式 DDL 同步，3号位 提交变更申请。
2. **P0-01 未关闭**：Repository 与冻结 DDL 双向不一致（依赖非冻结主题 JSON/Updated/Remarks + 遗漏 EffectiveFrom/EffectiveTo/ApprovedAt/ApprovedBy 落库）；3号位 自述"未扩写依赖非冻结字段的 Repository SQL"与源码不符，已更正。
3. **P0-02 未关闭**：Solver/Candidate 空对象 + 两 Validator 未接入发布链 + R14~R17 无真实值重放；按 §7 一次收口（发布阶段聚合 ContentSnapshotJson / Run 装载阶段六块还原）。
4. **P1-01**：Provider 需加三版本 PUBLISHED + 有效期硬校验（一处权威防御）。
5. **P1-02（本消息 2 修正）**：**不采纳**"默认版本取数 SQL 由 2号位 实现"——3号位 已实现 `ResolveDefaultStrategyProfileVersionAsync`，正确边界为 2号位 Run 启动调用一次并缓存；本消息 §三 检查点 3 / §四 待回执点 2 已按此修正。
6. **联调结论**：联调方向保留；2号位 不需重新确认业务契约，仅代码联调；本轮无新增 2号位 业务 P0。

## 留痕状态

- [x] 消息 1（致 0号位）已起草落盘，2026-08-22
- [x] 消息 2（致 2号位）已起草落盘，2026-08-22
- [x] 0号位 裁决回执（2026-08-22）：**批准方案 A**；P1-02 修正；P0-01/P0-02 收口清单 15 项（详见附录）
- [ ] 2号位 六项检查点回执（交付物 11 闭环，按 P1-02 修正后口径）
