# APS V1 Setup 规则承载契约登记件（重构 S-7）

**文档性质**：Setup 换型规则承载的正式契约登记件。依据 0号位 口径「Setup 规则复用 RuleSetVersion/快照承载，不建独立表」，**撤销** `SetupTransitionRule` 独立物理表条目，**登记** 新承载 `RuleSetVersion.ContentSnapshotJson.SetupTransitionRules` 子块。
**起草**：3号位 ｜ **日期**：2026-09-23 ｜ **前置**：重构方案 `APS_V1_SetupTransitionRule重构方案_复用RuleSetVersion快照承载_3号位_v1.0_20260922.md`（0号位 已批准）
**状态**：✅ 登记生效（S 系列代码全绿落地；D-1 DROP 表已由 2号位 执行闭环 + D-2 容量回执，2026-09-23）

---

## 一、登记对象

Setup 换型规则（EXACT/DEFAULT）——原以独立物理表 `SetupTransitionRule` 承载（越位建表，未入冻结 DDL），现正式登记为：

> **承载 = `RuleSetVersion.ContentSnapshotJson` 的 `SetupTransitionRules` 子块（JSON 数组，NVARCHAR(MAX) 单列存储，零 DDL）**

与 `DemandPriority` 子块同轨（契约 §6.10.5 内容快照扩展），复用既有 RuleSetVersion 状态机（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED），不建独立版本体系。

## 二、新承载登记条款

### 2.1 块键与结构

| 项 | 值 |
|---|---|
| 块键名 | `SetupTransitionRules`（`SetupTransitionRuleProjector.SetupTransitionRulesBlockName` 单一真源） |
| 存储载体 | `RuleSetVersion.ContentSnapshotJson`（JSON 单列） |
| 块内容 | `SetupTransitionRule[]`（治理实体 JSON 数组，含审计字段） |

### 2.2 治理实体字段（块内，前端 DTO 传 Id）

```text
Id                    long    治理 Id（合成，见 2.3）
RuleSetVersionId      long    所属规则集版本
ProductionDepartmentId int    生产部门（INT Id）
StageCode             string  工序阶段（NVARCHAR Code）
OperationCode         string  工序（NVARCHAR Code）
ResourceId            int     资源（INT Id）
FromMaterialId        int     转换前物料（INT Id）
ToMaterialId          int     转换后物料（INT Id）
RuleType              string  EXACT（七元组）/ DEFAULT（五元组）
SetupMinutes          decimal 换型时间（分钟，>0）
IsActive              bool    软删标记（消费端只投影 true）
CreatedAt / CreatedBy / UpdatedAt / UpdatedBy   审计字段
```

### 2.3 治理 Id 合成规则（跨版本隔离，全局唯一）

```text
BuildRuleId(ruleSetVersionId, seq) = ruleSetVersionId * 10_000_000L + seq
ParseRuleId(id) = (id / 10_000_000L, id % 10_000_000L)
```

### 2.4 两级快照（治理 vs 消费）

| 层 | 结构 | 说明 |
|---|---|---|
| 治理端（块内） | `SetupTransitionRule`（含 Id/审计字段） | SetupRuleService / SetupTransitionRuleService 读写；冲突校验器（纯计算）保留 |
| 消费端（第⑦块） | `SetupTransitionRuleSnapshot`（仅业务键 + SetupMinutes + RuleType，IsActive=true 过滤，无审计字段） | `FrozenStrategySnapshotProvider` 装配 → 1号位 `SetupOptimizer` 消费 |

### 2.5 语义：fail-open（区别于六块 fail-closed）

| 场景 | 消费端行为 |
|---|---|
| 子块缺失 / 为空 / 损坏 | 空规则列表 → **不阻断求解**；`SetupSource` 落库按上下文落 **4 态之一**（1号位 2026-09-23 P-1 回执澄清，0号位 20260922 裁决）：无上一产品→`INITIAL_SETUP_STATE`；同产品连续→`DEFAULT`；有前后产品且 EXACT/DEFAULT 均缺失→`SETUP_RULE_MISSING_ZERO_FALLBACK`（WARNING，进 4号位 数据质量查询） |
| 子块存在且合法 | 投影为快照供命中（EXACT 优先，DEFAULT 兜底，判定归 1号位） |

> **fail-open ≠ 静默 DEFAULT**：空规则最常见落 `SETUP_RULE_MISSING_ZERO_FALLBACK`（非统一 `DEFAULT`），且规则缺失**必须记解释**（0号位 Q4 裁决）。
> 与 DemandPriority/Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail 六块 **fail-closed**（P0-02 六块统一失败）语义不同：Setup 为可选项，空 = 合法兜底。
> `SetupOutcome` 内部枚举 **5 值**（ExactHit/DefaultHit/SameProductZero/InitialState/RuleMissing）→ 落库 `SetupSource` **4 态**（`SetupOutcomeToSource` 归并：SameProductZero→DEFAULT），2号位 只承载 string 原样落库、不做映射。

## 三、撤销旧条目（独立物理表）

原 `APS_V1_3号位规则参数认证权限与运行生命周期开发实施包_v1.6_20260920_Setup换型规则专项冻结对齐版.md` 登记条目正式撤销：

| 位置 | 原条目 | 撤销结论 |
|---|---|---|
| :35 变更表 row 1 数据库影响 | `RuleSet/RuleSetVersion 复用 + SetupTransitionRule 表` | **撤销** → 改为 `RuleSetVersion.ContentSnapshotJson.SetupTransitionRules 子块（零 DDL）` |
| §十 A1（:1953） | `2号位 SetupTransitionRuleRepository CRUD + DI` | **撤销**（S-3 已删仓储与接口，物理表访问清零） |
| §十 A2（:1954） | `2号位 第⑦块 SolverStrategySnapshot.SetupTransitionRules 投影` | **改道**（S-4 Provider 从 ContentSnapshotJson 反序列化装配第⑦块） |

## 四、代码落地印证（S 系列，证据链）

| 任务 | 落地位置 | 状态 |
|---|---|---|
| S-1 治理 API 改道 | `SetupRuleService`（13 端点读写子块）+ `SetupTransitionRuleProjector`（ExtractRules/SetRules/BuildRuleId/ParseRuleId） | ✅ |
| S-2 纯 JSON 子块 CRUD | `SetupTransitionRuleService`（5 方法，冲突校验器保留）；**2026-09-23 退役**——无运行时消费者（Controller 已删、SetupRuleService 只引常量），按 #4 退役删除 | ✅（已退役） |
| S-3 撤销仓储 | 删除 `ISetupTransitionRuleRepository` / `SetupTransitionRuleRepository` + DI 注册；5 个集成测试 DI 同步清理 | ✅ |
| S-4 快照装配 | `FrozenStrategySnapshotProvider` 第⑦块 = `Project(ExtractRules(ContentSnapshotJson))` | ✅ |
| S-5 移除物理表读取 | `PeggingOrchestrator` 删 `LoadSetupTransitionRulesAsync` + 构造依赖 | ✅ |
| S-6 归一化/比较/发布校验 | `GovernanceVersionService`：EnsureRuleSetNormalized 保留 Setup 子块；Compare 增行；发布前校验（缺失跳过/损坏阻断） | ✅ |
| S-8 投影输入源 | Projector.Project 输入 = 子块反序列化（CropToDomain 保留） | ✅ |
| S-9 单测更新 | ProjectionTests / ServiceTests / ConflictValidatorTests 改子块输入 | ✅ |
| S-10 uncovered-stats 端点 | #14 `setup-rules/uncovered-stats`（`SetupRuleController`）+ `ISetupRuleService.GetUncoveredStatsAsync` + `ISetupUncoveredStatRepository/SetupUncoveredStatRepository`（Dapper 聚合 [Task].SetupSource='SETUP_RULE_MISSING_ZERO_FALLBACK'）+ DTO `SetupUncoveredStatDto`；四级下钻 runId→部门→工序→设备→产品，Code 回带 | ✅ |

## 五、各号位影响

| 号位 | 影响 | 处置 |
|---|---|---|
| 1号位 | `SetupOptimizer` 消费快照第⑦块语义不变 | P-1 零改动确认 |
| 2号位 | DROP `SetupTransitionRule` 物理表（含唯一索引/seed 段 A/B/C 清理） | D-1（红线 #6 专属执行，待 S 系列就绪） |
| 2号位 | 确认 `ContentSnapshotJson`（NVARCHAR(MAX)）容量可行 | D-2 回执 |
| 4号位 | 13 端点契约不变（红线 #5：接口签名/DTO 不动） | F-1 零改动确认 |
| 5号位 | 若查询依赖 SetupTransitionRule 表 → 改走治理查询通道（GanttDataDto SetupSource 4 值同步） | R-1 **已闭环**（2026-09-23：通知 → 5号位 回执 3 项✅ → 3号位 复核 `_5号位R-1回执复核_`；GanttDataDto.cs:176 四态注释 3号位 代落盘，SetupOutcomeToSource 归并一致，BusinessRules 零旧表依赖） |

## 六、生效条件与遗留

- **登记生效**：S 系列（S-1~S-9）全部落地即生效；当前代码已随承载改道（S-1~S-9 ✅，构建 0 错误、350/350 单测全绿）。
- **物理表清理闭环（2026-09-23）**：`DROP SetupTransitionRule` 已由 2号位 D-1 执行（红线 #6，开发库 `APS_Production` 复检归零，旧仓储/接口 svn delete r13444）+ D-2 `ContentSnapshotJson` 容量回执（NVARCHAR(MAX) ≤2GB 可行）——**本登记件状态转 ✅**。
- **遗留台账（2026-09-23 处置）**：`SetupTransitionRuleController.cs`（3号位 本会话撤销）+ `SetupTransitionRuleService.cs` / `ISetupTransitionRuleService.cs` / `SetupTransitionRuleServiceTests.cs`（无运行时消费者，3号位 按 #4 退役删除）——旧 API 全部退役，前端统一走 `SetupRule`（13 端点）通道，闭环完成。
- **不修改冻结文档正文**：v1.6 冻结对齐版保持不变，本登记件为正式承载变更依据；如后续版本升级（v1.7）将吸收本条登记。

---

**起草人**：3号位 ｜ **日期**：2026-09-23
**抄送**：0号位（备查，不请求裁决）｜ 1号位 / 2号位 / 4号位 / 5号位
