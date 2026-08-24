# APS V1 3号位 裁决申请：R1 / R2（0号位）

> 版本：v1.0
> 日期：2026-08-24
> 申请方：3号位
> 受裁决方：0号位
> 关联清单：《APS_V1_3号位跨号位任务清单_0号位裁决与协助开发_v1.0_20260824.md》（R1/R2 立项依据）
> 上位依据：《APS_V1_3号位代码第四轮正式复审报告_7455e28_Commit冻结版(1).md》；契约文档《05_3号位和1号位对外契约.md》§6.3 / §6.10.5 / §7.0；红线 #5 / #6
> 当前基线：单测 114/114 全绿；集成测试 115 pass / 13 skip / 1 fail

---

## 一、R1：跨号位 DI 缺口修复方向裁决（P0）

### 1.1 请求事项

请 0号位 就 **SchedulingOrchestrator DI 缺口**的修复方向给出书面裁决（二选一），并明确**责任归属**（由哪一号位执行）。

### 1.2 现状证据（已核实）

- `LPS.APS.Application/Services/SchedulingOrchestrator.cs` **L41** 构造函数依赖**具体类** `FiniteCapacitySolver`；
- `LPS.APS.Scheduling/Extensions/SchedulingServiceExtensions.cs` 的 `AddSchedulingServices()` 仅注册**接口** `IFiniteCapacityScheduler → FiniteCapacitySolver`（单例），**未注册具体类**；
- 后果：DI 无法解析 `SchedulingOrchestrator`，集成测试 `v5.1.2: 完整排程流程集成测试`（RealSchedulingIntegrationTest）构造函数抛
  `Unable to resolve service for type 'LPS.APS.Scheduling.Solvers.FiniteCapacitySolver'` → **全红**。
- **定性**：非本次回归，属**既有跨号位缺口**——`SchedulingOrchestrator` 归属 2号位，`AddSchedulingServices()` 归属 1号位（Scheduling 层），职责交叉导致无单方可独立闭环。

### 1.3 候选方案（二选一）

| 方案 | 内容 | 执行方 | 优点 | 代价 / 风险 |
|---|---|---|---|---|
| **方案 1** | 在 `AddSchedulingServices()` 补注册具体类 `FiniteCapacitySolver` | 1号位 | 改动最小（~0.5 人时） | 具体类/接口双注册存在实例歧义隐患；业务代码继续依赖具体类型，违反依赖倒置 |
| **方案 2（推荐）** | `SchedulingOrchestrator` 构造改为依赖接口 `IFiniteCapacityScheduler` | 2号位 | 符合依赖倒置；与 Scheduling 层"仅注册接口"的设计一致；长期可维护 | 需改 2号位 代码（~1 人时）；须按红线 #5 **先同步契约 §6.3** 再改签名 |

### 1.4 建议

**推荐方案 2**：让 `SchedulingOrchestrator` 依赖冻结接口 `IFiniteCapacityScheduler`（契约 §7.0 已冻结），一次修正根本问题；方案 1 仅掩盖症状且引入歧义隐患。

### 1.5 验收判据

1. 0号位 书面裁决（含责任归属 + 方案选择）；
2. 对应号位按裁决执行后，`v5.1.2: 完整排程流程集成测试` **转绿**（C2/C3 联动，见关联清单）。

### 1.6 期望时限

裁决 1~2 个工作日；执行当轮闭环。

---

## 二、R2：集成测试验收口径裁决（P1）

### 2.1 请求事项

请 0号位 明确：在 **2号位 测试库 v5.1.2 迁移（C1）尚未执行**的现状下，方案 A 集成测试按动态 Skip 约定转绿，**是否计入方案 A 当前验收状态**？

### 2.2 现状（已核实）

- 2号位 尚未执行测试库迁移（`RuleSetVersion` / `ParameterSetVersion` 缺 `ContentSnapshotJson` 列）；
- 13 个方案 A 集成测试（含新全链路 `方案A_端到端链_CreateReloadUpdateValidatePublishReloadSnapshot_历史重放`）按既有动态 Skip 约定**自动转绿**（探针 `TestEnvironment.HasContentSnapshotJsonColumn()` 检测列不存在即 Skip）；
- 3号位 侧代码已交付（服务 CRUD 归一化 + Provider 六块真实来源 + 集成测试重写），只差列就位。

### 2.3 候选口径（二选一）

| 口径 | 含义 | 影响 |
|---|---|---|
| **口径 A（推荐）** | 接受"待 2号位 迁移后测试自动转绿"为方案 A 当前验收状态；C1 列为**待办跟踪项**而非验收阻塞 | 收口可按期闭环；C1 完成即自动补证 |
| **口径 B** | 要求**先迁移、后验收**（C1 未闭环前方案 A 集成验收暂缓） | 更严格，但收口延期直至 2号位 迁移 |

### 2.4 建议

**推荐口径 A**：代码侧已就绪、仅差 DB 列（属 2号位 专属执行、3号位 无权 ALTER，红线 #6）；以"迁移后自动转绿"作为验收闭环条件，兼顾严格性与流程现实。

### 2.5 验收判据

1. 0号位 明确口径 A / B；
2. 按口径更新《APS_V1_3号位跨号位任务清单》§五 验收口径表 与《APS_V1_3号位P0整改复核提报》验收结论。

### 2.6 期望时限

裁决 1~2 个工作日。

---

## 三、请求汇总（0号位 批注栏）

| 编号 | 请求 | 0号位 裁决意见 | 责任归属 | 期限 |
|---|---|---|---|---|
| R1 | DI 修复方向：方案 1 / 方案 2（推荐 2） | ☐ 方案1 ☐ 方案2 | ☐ 1号位 ☐ 2号位 | 1~2 工作日 |
| R2 | 验收口径：A（推荐）/ B | ☐ 口径A ☐ 口径B | — | 1~2 工作日 |

---

## 四、裁决回执后 3号位 动作（预承诺）

1. **R1→方案 2**：3号位 配合 2号位 完成契约 §6.3 变更说明（红线 #5 前置文档），2号位 改 `SchedulingOrchestrator` 构造依赖接口；
2. **R2→口径 A**：更新关联清单 §五/§六 与验收提报，将 C1 列为待办跟踪项；
3. 裁决落地后同步《APS_V1_3号位跨号位任务清单》状态跟踪，向 0号位 回报。

> 本申请为 3号位 提交的正式 0号位 裁决申请；R1/R2 均不涉及 3号位 越界改码（红线 #5/#6 约束下仅呈报与配合）。
