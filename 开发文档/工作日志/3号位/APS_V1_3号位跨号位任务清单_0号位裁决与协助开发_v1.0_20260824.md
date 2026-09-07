# APS V1 跨号位任务清单：0号位裁决 + 各号位协助开发（正式生产任务级）

**版本**：v1.0
**日期**：2026-08-24
**编制**：3号位
**适用对象**：0号位（裁决）、2号位 / 1号位 / 4号位（协助开发）
**性质**：第四轮正式复审（`7455e28` Commit 冻结版）收口后的**跨号位任务清单**。方案 A（ContentSnapshotJson 为唯一持久化载体）已由 0号位 裁决收口；本清单为剩余**待 0号位 裁决项（2 项）**与**待其他号位协助开发项（4 项）**的任务级拆解，每项标注优先级、依赖、验收判据与预估。
**上位依据**：
- 《APS_V1_3号位代码第四轮正式复审报告_7455e28_Commit冻结版(1).md》（收口基线）
- 《APS_V1_3号位DDL变更申请_ContentSnapshotJson_v1.0_20260822.md》（C1 变更申请，红线 #6 已走流程）
- 《APS_V1_3号位与2号位Snapshot联调记录_3号位单侧就绪声明_v1.0_20260822.md》（联调前置）
- 契约文档《05_3号位和1号位对外契约.md》§6.3（SchedulingOrchestrator 依赖）/ §6.10.5（ContentSnapshotJson 子块）/ §7.0（冻结契约 IFiniteCapacityScheduler）
- 项目红线：红线 #5（接口即契约）、红线 #6（DB 结构变更 2号位 专属执行）

**当前基线（2026-08-24 首版）**：单测 **114/114 全绿**；集成测试 **115 pass / 13 skip / 1 fail**——唯一失败为 `v5.1.2: 完整排程流程集成测试`（RealScheduling DI：`Unable to resolve service for type 'LPS.APS.Scheduling.Solvers.FiniteCapacitySolver'`）；13 个 skip 为方案 A 集成测试（测试库未迁移，按动态 Skip 约定待 2号位 迁移后自动转绿）。

> **2026-08-27 基线更新**（详见 §七）：集成测试 **124 pass / 5 skip / 0 fail（总计 129）**——C1（ContentSnapshotJson 迁移）已完成、13 方案A skip 转绿；`v5.1.2: 完整排程流程集成测试` 已转绿（工作树 SchedulingOrchestrator 重构依赖接口 + 3号位 修复 PeggingOrchestrator SchedulingContext 透传，见《APS_V1_3号位越线自查与处置提报_20260827.md》§三）；剩余 5 skip = 2号位 待建 `GovernanceAuditLog` 表（3）+ `ScheduleRun.ExpectedDomainKeysJson` 列（2）。

---

# 一、任务清单总览（汇总表）

| 分类 | 编号 | 任务 | 负责号位 | 优先级 | 前置依赖 | 验收判据 | 状态 |
|---|---|---|---|---|---|---|---|
| 0号位 裁决 | **R1** | SchedulingOrchestrator DI 缺口修复方向裁决 | 0号位 | **P0** | — | 0号位 书面裁决；C2 按其执行后 `v5.1.2` 转绿 | ✅ 已裁决（方案2） |
| 0号位 裁决 | **R2** | 集成测试验收口径裁决（迁移前置下 Skip 是否可验收） | 0号位 | **P1** | — | 0号位 明确验收口径 | ✅ 已裁决（口径A调整版） |
| 2号位 协助 | **C1** | 测试库 v5.1.2 迁移（ContentSnapshotJson 列） | 2号位 | **P0** | 方案 A（已裁决） | 13 个 Skip 集成测试自动转绿 | ✅ 已完成（2026-08-27 基线 13 skip→5） |
| 2号位 协助 | **C2** | SchedulingOrchestrator DI 修复（按 R1 方案2：依赖接口） | 2号位 | **P0** | R1（已裁决） | `v5.1.2: 完整排程流程集成测试` 转绿 | ✅ 已转绿（工作树重构 + 3号位 修复，见 §七） |
| 2号位 协助 | **C5** | 建 `GovernanceAuditLog` 表 + `ScheduleRun.ExpectedDomainKeysJson` 列（2号位 DDL） | 2号位 | **P0** | 3号位 实体/仓储/DbSet 已交付 | 5 个 Skip 自动转绿 → 129/0/0 | ⏳ 待 2号位 执行 |
| 2号位 协助 | **C6** | 分域执行链收编/废弃裁决（4 文件已注释退出） | 2号位 | **P1** | 越线处置（已注释退出） | 书面裁决 + 处置落地 | ⏳ 待 2号位 裁决 |
| 1号位 协助 | **C3** | FiniteCapacitySolver 完整实现 + 排程数据准备脚本可用性核对 | 1号位 | **P1** | C2 | RealScheduling 全链路跑通 | 待执行 |
| 4号位 协助 | **C4** | 前端联调按 GET 投影契约（六块 JSON 为内存字段） | 4号位 | **P2** | 方案 A（已裁决） | 前端可读取/展示六块快照 | 待启动 |

> 优先级说明：**P0** = 阻塞测试转绿或职责边界裁定，未闭环前当前收口不完整；**P1** = 阻塞全链路跑通；**P2** = 低优先（前端尚未启动）。

---

# 二、需 0号位 裁决任务（2 项）

## R1 🔴 跨号位 DI 缺口：SchedulingOrchestrator 依赖具体类（P0）

- **类型**：职责归属 + 技术方向裁决
- **任务背景/现状证据**：
  - `LPS.APS.Application/Services/SchedulingOrchestrator.cs` L41 构造函数依赖**具体类** `FiniteCapacitySolver`；
  - `LPS.APS.Scheduling/Extensions/SchedulingServiceExtensions.cs` 的 `AddSchedulingServices()` 仅注册**接口** `IFiniteCapacityScheduler → FiniteCapacitySolver`（单例），未注册具体类；
  - 后果：DI 无法解析 `SchedulingOrchestrator`，集成测试 `v5.1.2: 完整排程流程集成测试` 构造函数抛 `Unable to resolve service for type 'LPS.APS.Scheduling.Solvers.FiniteCapacitySolver'` → **全红（非本次回归，属既有缺口）**。
- **待裁决点（二选一）**：
  - **方案 1（最小改动）**：1号位 在 `AddSchedulingServices()` 补注册具体类 `FiniteCapacitySolver`。代价：具体类 / 接口双注册存在实例歧义隐患，且业务代码继续依赖具体类型（违反依赖倒置）。
  - **方案 2（推荐）**：2号位 将 `SchedulingOrchestrator` 构造改为依赖接口 `IFiniteCapacityScheduler`。代价：需改 2号位 代码，并同步契约文档 §6.3（红线 #5：先更新契约再改签名）。
- **验收判据**：0号位 就修复方向给出书面裁决；对应号位按裁决执行后 `v5.1.2: 完整排程流程集成测试` 转绿。
- **预估**：裁决本身 0.5 人时；方案 1 约 0.5 人时 / 方案 2 约 1 人时（含契约同步）。
- **备注**：跨 2号位（Orchestrator 归属）与 1号位（Scheduling 注册归属），故需 0号位 定界。

## R2 🟡 集成测试验收口径：方案 A 迁移前置下 Skip 是否可验收（P1）

- **类型**：验收口径裁决
- **任务背景/现状**：2号位 尚未执行测试库 v5.1.2 迁移（`RuleSetVersion`/`ParameterSetVersion` 缺 `ContentSnapshotJson` 列），13 个方案 A 集成测试（含新全链路 `方案A_端到端链_CreateReloadUpdateValidatePublishReloadSnapshot_历史重放`）按既有动态 Skip 约定自动转绿（`TestEnvironment.HasContentSnapshotJsonColumn()` 探针）。
- **待裁决点**：验收时是否接受"**2号位 迁移完成后测试自动转绿**"作为方案 A 当前验收口径？还是要求**先迁移、后验收**（即 C1 未闭环前，方案 A 集成验收暂缓）？
- **验收判据**：0号位 明确口径；按口径更新本清单 §五 与验收提报。
- **预估**：裁决本身 0.5 人时。
- **备注**：涉及 0号位 对"待迁移即转绿"约定的认可，避免验收口径歧义。

---

# 三、需其他号位协助开发任务（4 项）

## C1 🔴 2号位：测试库 v5.1.2 迁移（ContentSnapshotJson 列）（P0）

- **任务描述**：在 APS_Production **测试库**执行 DDL v5.1.2 迁移——`RuleSetVersion` / `ParameterSetVersion` 增加 `ContentSnapshotJson NVARCHAR(MAX)` 列（方案 A 唯一内容载体），并按契约 §6.10.5 保持六主题 JSON 列兼容（退出持久化，保留为内存/API 字段）。
- **红线约束**：红线 #6 —— DB 结构变更由 2号位 **专属执行**；3号位 已提交变更申请《APS_V1_3号位DDL变更申请_ContentSnapshotJson_v1.0_20260822.md》为执行依据。
- **验收判据**：`TestEnvironment.HasContentSnapshotJsonColumn()` 探针检测到列存在 → 13 个 Skip 集成测试（FrozenStrategySnapshotProvider 2 个 + DemandPriority 3 个 + GovernanceVersionService 含全链路）**自动转绿**。
- **前置依赖**：方案 A（0号位 已裁决，无需再裁）。
- **预估**：0.5 ~ 1 人日（含脚本回归）。

## C2 🔴 2号位：SchedulingOrchestrator DI 修复（P0）

- **任务描述**：按 **R1 裁决方向**执行修复：
  - 若裁决**方案 2**：将 `SchedulingOrchestrator` 构造函数依赖 `FiniteCapacitySolver`（具体类）改为 `IFiniteCapacityScheduler`（接口），并先同步契约文档 §6.3（红线 #5）；
  - 若裁决**方案 1**：由 1号位 在 `AddSchedulingServices()` 补注册具体类（见 C3 备注）。
- **验收判据**：`v5.1.2: 完整排程流程集成测试`（RealSchedulingIntegrationTest）构造函数不再抛 DI 解析异常并**转绿**。
- **前置依赖**：R1（裁决方向）。
- **预估**：方案 2 约 1 人时；方案 1 由 1号位 执行约 0.5 人时。
- **备注**：此缺口阻塞排程链路集成验证，为当前唯一全红测试，建议 R1 裁决后当轮闭环。

## C3 🟡 1号位：FiniteCapacitySolver 完整实现 + 排程数据准备脚本可用性（P1）

- **任务描述**：
  1. 核对/补齐 `FiniteCapacitySolver` 完整实现（当前注册于接口 `IFiniteCapacityScheduler` 下，为求解器主体）；
  2. 核对 RealScheduling 集成测试所需**真实排程数据准备脚本**可用性（资源/工艺/订单/库存等预置数据），保证 `v5.1.2: 完整排程流程集成测试` 全链路（装配→求解→回写）可跑通。
- **验收判据**：C2 修复后，RealScheduling 全链路测试**转绿**且结果符合预期（非空排程结果、资源负载、订单排程状态）。
- **前置依赖**：C2（DI 修复）。
- **预估**：1 ~ 2 人日（视求解器完备度与数据脚本现状）。
- **备注**：契约已冻结 `IFiniteCapacityScheduler.SolveAsync(DomainSolveRequest, CancellationToken)`（契约 §7.0），1号位 实现须对齐冻结签名（红线 #5）。

## C4 ⚪ 4号位：前端联调按 GET 投影契约（六块 JSON 为内存字段）（P2）

- **任务描述**：前端（Vue 3 + TypeScript，尚未启动）联调时按 **GET 投影契约**消费方案 A 数据——`DemandPriorityJson` / `LockJson` / `SupplyJson` / `ProcurementJson` / `SolverStrategyJson` / `CandidateGuardrailJson` 六主题为**内存/API 字段**（API 兼容未变），`ContentSnapshotJson` 为持久化载体（前端不直接读取）。
- **验收判据**：前端联调文档/页面可读取并展示六块快照；发布/停用/Diff 界面走通。
- **前置依赖**：方案 A（已裁决）。
- **预估**：待前端启动后评估。
- **备注**：低优先，仅预留契约约定，避免前端联调期二次改造。

---

# 四、依赖关系与关键路径

```text
（独立）方案 A（已裁决）
   ├── C1（2号位迁移） ──→ 13 个 Skip 集成测试自动转绿（含方案 A 全链路）
   └── R2（验收口径裁决） ──→ 收口验收口径

（链式关键路径）R1（裁决方向）
   └── C2（2号位 按裁决修 DI） ──→ v5.1.2 完整排程测试转绿
        └── C3（1号位 求解器+数据） ──→ RealScheduling 全链路跑通
```

- **关键路径**：`R1 → C2 → C3`（排程链路集成验证全绿）。
- **并行支线**：`C1`（迁移）与 `R2`（口径）可并行推进，不阻塞关键路径。
- **全局转绿判据**：C1 + C2 到位后，集成套件由 `115 pass / 13 skip / 1 fail` 变为 **129 pass / 0 skip / 0 fail**（13 skip 转绿 + 1 fail 转绿；`方案A_端到端链` 属新增计入）。

---

# 五、验收口径（判据统一约定）

| # | 口径项 | 约定 |
|---|---|---|
| 1 | 方案 A 集成测试（C1 迁移前） | **R2 已裁决（口径A 调整版）**：整改完成但**不提前宣布最终验收**；终验 = C1 迁移 + 真实集成测试 + 全链路（Create→Reload→Update→Validate→Publish→Reload→Snapshot→历史重放） |
| 2 | `v5.1.2: 完整排程流程集成测试` | **R1 已裁决（方案2）**：`SchedulingOrchestrator` 依赖接口 `IFiniteCapacityScheduler`，2号位 整改（契约 §6.3 前置已更新）；C2/C3 执行为闭环条件 |
| 3 | 接口签名变更 | 红线 #5：先更新契约文档（§6.3 / §7.0）再改代码 |
| 4 | DB 结构变更 | 红线 #6：仅 2号位 执行；3号位 侧仅走变更申请 |

---

# 六、状态跟踪（2026-08-24 首版）

- [x] R1 0号位 裁决 DI 修复方向（P0）—— ✅ 已裁决：**方案2**（`SchedulingOrchestrator` 依赖接口，2号位 修改）—— 依据 0号位《8802d56 正式复审报告》§三
- [x] R2 0号位 裁决集成测试验收口径（P1）—— ✅ 已裁决：**口径A（调整版）**——整改完成但**不提前宣布最终验收**，依据 §四
- [ ] C1 2号位 测试库 v5.1.2 迁移（P0）—— ⏳ 待 2号位 执行（3号位 变更申请已提交）
- [ ] C2 2号位 SchedulingOrchestrator DI 修复（P0）—— ⏳ 待 2号位 执行（按 R1 方案2；3号位 契约 §6.3 前置已更新）
- [ ] C3 真实集成测试执行（P1）—— 3号位 配合；待 C1+C2 就位（终验链路：Create→Reload→Update→Validate→Publish→Reload→Snapshot→历史重放）
- [x] P1 .gitignore 二进制污染 —— ✅ 已关闭（#27 解除 669 文件跟踪 + 本轮补 `*.pdb`）
- [ ] C4 4号位 前端联调投影契约（P2）—— 待前端启动

**下一步建议**：R1/R2 已获 0号位 裁决（《8802d56 正式复审报告》）；3号位 侧待办收敛为"契约 §6.3 前置（已更新）+ 真实集成测试配合"，待 C1/C2（2号位 迁移 + DI 整改）就位后执行终验链路。

---

# 七、状态跟踪更新（2026-08-27 越线处置 + 收口复核后）

**关联提报**：《APS_V1_3号位越线自查与处置提报_20260827.md》（越位代码处置 / 收口复核修复全记录）。

- [x] **C1** 2号位 测试库 v5.1.2 迁移（ContentSnapshotJson 列）—— ✅ 已完成：13 方案A skip 已转绿（08-27 基线确认）
- [x] **C2** SchedulingOrchestrator DI 修复—— ✅ 已转绿：工作树 SchedulingOrchestrator 重构已依赖接口 `IFiniteCapacityScheduler`；剩余 `v5.1.2` 测试失败根因为 3号位 PeggingOrchestrator 批量重构丢失 `SchedulingContext`（已修复，见提报 §三.2）
- [ ] **C5** 2号位 建 `GovernanceAuditLog` 表 + `ScheduleRun.ExpectedDomainKeysJson` 列（P0）—— ⏳ 待 2号位 执行；闭环后 5 skip→0、集成套件 129/0/0
- [ ] **C6** 2号位 分域执行链收编/废弃裁决（P1）—— ⏳ 待 2号位 裁决；4 文件已注释退出、原文保留（越线处置 §四.1）
- [ ] **C3** 1号位 求解器 + 数据脚本（P1）—— 待执行（不阻塞当前 124/5/0 绿基线）
- [x] **3号位 越线处置** —— ✅ 4 文件注释退出（DomainSchedulingJob / DomainLayerCoordinatorJob / 两接口），构建 0 错误、Web 启动无 DI 崩溃
- [x] **3号位 收口复核修复** —— ✅ 测试配置加载（5 fixture 补 Local.json + csproj 复制）+ PeggingOrchestrator SchedulingContext 透传，全量 124/5/0 恢复基线

**下一步**：全局转绿判据由 `C1 + C2` 收敛为 **C5（2号位 2 项 DDL）**；C5 到位后集成套件 **129 pass / 0 skip / 0 fail**。终验链路（C3 真实全链路：Create→Reload→Update→Validate→Publish→Reload→Snapshot→历史重放）待 C3/C5 就位后执行。

---

*本清单由第四轮正式复审（`7455e28`）收口结论整理；任务级依赖/验收/预估已标注，红线 #5/#6 全程约束。*
