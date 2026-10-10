# APS V1 Setup 规则承载重构方案（复用 RuleSetVersion / 快照承载，不建独立表）

**版本**：v1.0（重构方案登记件 + 任务清单）
**日期**：2026-09-22
**起草人**：3号位（规则/参数治理）
**批准**：3号位 自定案（用户确认：不合规开发无需 0号位 裁决，直接推倒重构）
**接收人**：2号位（红线 #6 DROP 执行）、1号位（消费确认）、4号位（前端契约）、5号位（查询通道）
**抄送**：0号位（PM，备查）
**性质**：**重构方案登记件**——Setup 规则承载从独立物理表迁移到 RuleSetVersion.ContentSnapshotJson 快照子块，满足 0号位 口径「Setup 规则复用 RuleSetVersion / 快照承载，不建独立表」。
**依据**：
- 0号位《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》五.2（不能因提出表名就认为冻结已规定）
- 0号位《APS_V1_Setup换型v1.2_悬置项_0号位正式回复_v1.0_20260917》§三（不强制独立 Setup 版本体系；Setup 规则属于 RuleSetVersion；快照内 SetupTransitionRules 或等价结构）
- 《APS_V1_SetupTransitionRule越位建表如实上报_3号位致0号位_v1.0_20260922.md》（合规定性：越位建表，不请求裁决，改为重构整改）
- 冻结 DDL v5.1.8.2（RuleSetVersion 已有 ContentSnapshotJson 列，方案 A 落点）

---

## 〇、速览

```
问题定性：SetupTransitionRule 独立物理表 = 3号位 越位建表（0号位 未冻结、未批准）
重构目标：Setup 规则承载 → RuleSetVersion.ContentSnapshotJson.SetupTransitionRules 快照子块（零 DDL）
消费端   ：SetupOptimizer 已走快照（合规，零改动）
装载端   ：Provider 反序列化第⑦块（替代 PeggingOrchestrator 物理表读取）
治理端   ：SetupRuleService 13 端点底层改道子块（接口不变，红线 #5）
撤销     ：DROP SetupTransitionRule 物理表（2号位 红线 #6）+ 仓储/实体并入快照结构
任务清单 ：3号位 S-1~S-9（主责）→ 2号位 D-1~D-2 → 1号位 P-1 / 4号位 F-1 / 5号位 R-1
```

---

## 一、问题定性（推倒重构，不请求裁决）

| # | 事实 | 定性 |
|---|---|---|
| 1 | SetupTransitionRule 由 3号位 依据《冻结文档修改指导 v1.2》§九/§十建表（2026-09-17） | 依据「建议」文档，非 0号位 批准 |
| 2 | 冻结 DDL v5.1.8.2 未收录 SetupTransitionRule（全文件检索零命中） | 冻结缺表漂移 |
| 3 | 0号位 统一回复五.2：不能因提出表名就认为冻结已规定；悬置项回复 §三：Setup 规则属于 RuleSetVersion，不强制独立表 | 独立表与 0号位 口径相悖 |
| 4 | 消费端已走 SetupTransitionRuleSnapshot 快照（r13376，合规）；不合规聚焦治理端独立物理表 | 重构仅动治理/装载端，消费端零改动 |

**结论**：推倒重构——撤销 SetupTransitionRule 独立表，Setup 规则承载迁移到 RuleSetVersion.ContentSnapshotJson 快照子块。无需 0号位 逐项裁决。

---

## 二、现状盘点（已实现业务逻辑 + 跨号位依赖）

### 2.1 消费端（1号位）——已合规，零改动

`SetupOptimizer.cs`：`BuildRuleLookups(IEnumerable<SetupTransitionRuleSnapshot>)`（:173）→ EXACT/DEFAULT 字典 → `ResolveSetup`（:109）。不读物理表，头注释明示「不读 3号位 数据库」。

### 2.2 快照载体与装载（2号位 / PeggingOrchestrator）

| 环节 | 现状 |
|---|---|
| `FrozenStrategySnapshot` 第⑦块 | `SetupTransitionRules`（List\<SetupTransitionRuleSnapshot\>，FrozenStrategySnapshot.cs:271）已存在 |
| 装载 | **`PeggingOrchestrator.LoadSetupTransitionRulesAsync`（:3019）从 `[dbo].[SetupTransitionRule]` 物理表读取** → `SetupTransitionRuleProjector.Project` → 填第⑦块 → `CropToDomain`（:220）|
| 反序列化通道（可复用） | `FrozenStrategySnapshotProvider`（:91）已从 `RuleSetVersion.ContentSnapshotJson` 反序列化 DemandPriority（①）等六块——**第⑦块未入此通道** |

### 2.3 治理端（3号位）——不合规点所在

| 组件 | 现状 |
|---|---|
| 实体 | `SetupTransitionRule`（Core/Entities/Aps/SetupTransitionRule.cs）——独立物理表实体 |
| 仓储 | `SetupTransitionRuleRepository`（Dapper，直读 `[dbo].[SetupTransitionRule]`）|
| 服务 | `SetupTransitionRuleService`（CRUD + 冲突校验 + AuditLog）+ `SetupRuleService`（13 端点编排）+ `SetupRuleController`（13 端点 + aps.setup.view/edit/publish）|
| 版本壳（可复用） | `RuleSetVersion`：已有 `DemandPriorityJson`（主题 JSON）+ **`ContentSnapshotJson`（唯一真相，NVARCHAR(MAX)，零 DDL 可容纳新子块）** |
| 归一化机制 | `EnsureRuleSetNormalized`（GovernanceVersionService.cs:974）：主题 JSON → ContentSnapshotJson 子块 → 发布聚合 → Provider 反序列化 |

### 2.4 跨号位任务协助（已登记在链）

- **2号位**：物理表落库（SetupTransitionRuleRepository）、seed A/B/C、历史回刷、快照投影链——重构后需改装载源
- **4号位**：13 端点 + 3 权限码（aps.setup.*）前端契约（DTO 传 Id）
- **1号位**：消费端（SetupOptimizer）+ Setup 预算参数（ParameterSet 通道，500/50）
- **5号位**：查询契约 SetupSource 4 值同步（GanttDataDto.cs:176）

---

## 三、重构方案（满足 0号位 口径：复用 RuleSetVersion / 快照承载，不建独立表）

### 3.1 方案总纲

**Setup 规则承载从 `SetupTransitionRule` 独立物理表 → `RuleSetVersion.ContentSnapshotJson` 的 `SetupTransitionRules` JSON 子块**（与 DemandPriority 完全同轨）。**零 DDL**（ContentSnapshotJson 列已存在 NVARCHAR(MAX)）。

```
治理端(3号位)                             快照装载(2号位/Provider)             消费端(1号位)
SetupRuleController 13端点 ──┐
  │ 读写                          │
  ▼                          ContentSnapshotJson                       ▼
RuleSetVersion ──────────────────► FrozenStrategySnapshot               SetupOptimizer
  .ContentSnapshotJson               .SetupTransitionRules(⑦块)          .BuildRuleLookups
  .SetupTransitionRules 子块 ──────► (Provider 反序列化)                    .ResolveSetup
  (JSON 数组, SetupTransitionRuleSnapshot 结构)                              (已合规, 零改动)
```

### 3.2 核心动作

| # | 动作 | 承载/通道 | 关键点 |
|---|---|---|---|
| 1 | **Setup 规则入快照子块**：`RuleSetVersion.ContentSnapshotJson` 增加 `SetupTransitionRules` 子块（JSON 数组，元素 = `SetupTransitionRuleSnapshot` 结构：Dept/Stage/Operation/Resource/From/To/RuleType/SetupMinutes）| RuleSetVersion 通道 | 与 DemandPriority 同轨；**零 DDL** |
| 2 | **治理 API 底层改道**：`SetupRuleService` 13 端点从 `ISetupTransitionRuleRepository`（物理表）改为读写 `ContentSnapshotJson` 的 Setup 子块；`GetVersionForWriteAsync` DRAFT 限制保留 | 3号位 | **接口不变（红线 #5）**，前端零改动 |
| 3 | **快照装载改道**：`FrozenStrategySnapshotProvider` 反序列化第⑦块（从 `RuleSetVersion.ContentSnapshotJson`）→ `PeggingOrchestrator.LoadSetupTransitionRulesAsync` 移除物理表读取 | Provider | 1号位 已消费快照，链路无缝 |
| 4 | **撤销独立表**：DROP `SetupTransitionRule` 物理表 + 撤销 `SetupTransitionRuleRepository` / `ISetupTransitionRuleRepository` + 实体并入快照结构 | 2号位（红线 #6 执行）| `SetupTransitionRuleService` 改纯 JSON 子块 CRUD（冲突校验器保留，纯计算）|
| 5 | **治理校验扩展**：`EnsureRuleSetNormalized` / `ProjectRuleSetContent` / `CompareRuleSetVersions` / 发布前校验补 Setup 子块 | GovernanceVersionService | 归一化合并 DemandPriority + SetupTransitionRules 双子块 |
| 6 | **契约登记**：Setup 规则承载条目从「独立表」改为「RuleSetVersion.ContentSnapshotJson.SetupTransitionRules」 | 3号位 | 撤销物理表条目，登记快照子块条目 |

### 3.3 与 0号位 口径逐条核对

| 0号位 口径 | 方案符合性 |
|---|---|
| 「Setup 规则仍然属于 RuleSetVersion」| ✅ 子块挂 RuleSetVersion.ContentSnapshotJson |
| 「不强制新建独立 Setup 版本体系」| ✅ 复用既有 RuleSetVersion 状态机 |
| 「FrozenStrategySnapshot 中 SetupTransitionRules 或等价结构」| ✅ 第⑦块 `SetupTransitionRules` 原样保留 |
| 「不建独立表」| ✅ DROP SetupTransitionRule，零新表 |

### 3.4 红线核对

| 红线 | 符合性 |
|---|---|
| #3 SqlBulkCopy 先去重 | ✅ 无新表、无 BulkCopy 路径；冲突校验器（纯计算）保留为主防线 |
| #5 接口即契约 | ✅ 13 端点签名/DTO 不变，仅底层承载改道；快照结构契约（SetupTransitionRuleSnapshot）原样复用 |
| #6 DB schema 变更 2号位 专属 | ✅ DROP 由 2号位 执行（D-1），3号位 仅提变更登记 |
| 冻结顺序（业务→数据→接口→代码）| ✅ 本方案为数据承载整改，对齐已冻结业务语义，代码随承载改道 |

---

## 四、任务清单（依重构方案陈列）

### 4.1 3号位（治理端整改，主责）

| # | 任务 | 产出 | 状态 |
|---|---|---|---|
| S-1 | `SetupRuleService` 13 端点底层：物理表 → `ContentSnapshotJson.SetupTransitionRules` 子块（List/Create/Update/Delete/Diff/Publish）| 代码改造 | ✅ |
| S-2 | `SetupTransitionRuleService` 改造为纯 JSON 子块 CRUD（冲突校验器保留）| 代码改造 | ✅ |
| S-3 | 撤销 `SetupTransitionRuleRepository` / `ISetupTransitionRuleRepository`（物理表访问清零）| 代码删除 | ✅ |
| S-4 | `FrozenStrategySnapshotProvider` 反序列化第⑦块（从 RuleSetVersion.ContentSnapshotJson）| 代码改造 | ✅ |
| S-5 | `PeggingOrchestrator.LoadSetupTransitionRulesAsync` 移除物理表读取（改从 Provider 快照）| 代码改造 | ✅ |
| S-6 | `GovernanceVersionService`：`EnsureRuleSetNormalized` / `ProjectRuleSetContent` / `CompareRuleSetVersions` / 发布前校验补 Setup 子块 | 代码改造 | ✅ |
| S-7 | 契约登记件：Setup 规则承载 = RuleSetVersion.ContentSnapshotJson.SetupTransitionRules（撤销独立表条目）| 文档 | ✅ |
| S-8 | `SetupTransitionRuleProjector.Project` 输入源从物理表实体 → 快照子块（或并入反序列化，CropToDomain 保留）| 代码改造 | ✅ |
| S-9 | 单测更新：Setup 治理测试（ProjectionTests / ServiceTests / ConflictValidatorTests）改子块输入 | 测试 | ✅ |

> **S 系列执行记录（2026-09-23 回填）**：S-1~S-9 全部落地并验证——构建 0 错误；**350/350 单测全绿**（Setup 治理相关 48/48，含 S-9 新增 `ProjectFromSnapshot` 3 用例 + 子块承载→校验整链 2 用例）；红线 #5 接口签名零改动；Setup 子块 fail-open 语义经 S-6 发布校验（缺失跳过/损坏阻断）与 S-8 便捷入口固化。契约登记件：`APS_V1_Setup规则承载契约登记件_3号位_v1.0_20260923.md`。

### 4.2 2号位（数据引擎，红线 #6 执行）

| # | 任务 | 产出 | 状态 |
|---|---|---|---|
| D-1 | DROP `SetupTransitionRule` 物理表（含唯一索引/seed 段 A/B/C 清理）| 补丁 + 落库 | ⏳ 待 S 系列就绪 |
| D-2 | 确认 `RuleSetVersion.ContentSnapshotJson`（NVARCHAR(MAX)）承载 Setup 子块容量可行 | 回执 | ⏳ |

### 4.3 1号位（消费端）

| # | 任务 | 产出 | 状态 |
|---|---|---|---|
| P-1 | 确认 `SetupOptimizer.BuildRuleLookups/ResolveSetup` 消费不变（快照第⑦块语义不变）| 回执 | ⏳ 零改动确认 |

### 4.4 4号位（前端）

| # | 任务 | 产出 | 状态 |
|---|---|---|---|
| F-1 | 确认 13 端点契约不变（红线 #5：接口签名/DTO 不动，前端零改动）| 回执 | ⏳ 通知 |

### 4.5 5号位（业务规则）

| # | 任务 | 产出 | 状态 |
|---|---|---|---|
| R-1 | 若 5号位 查询依赖 SetupTransitionRule 表 → 改走治理查询通道（GanttDataDto.cs:176 SetupSource 4 值同步）| 通知 | ⏳ |

### 4.6 实施顺序

```
S-1~S-9（3号位 治理/装载改道）→ D-1~D-2（2号位 DROP 表 + 容量确认）→ P-1/F-1/R-1（各号位确认）
```

---

## 五、风险与回退

| 风险 | 影响 | 缓解 |
|---|---|---|
| 发布前校验未覆盖 Setup 子块 → 坏快照进入 PUBLISHED | 排程误用规则 | S-6 校验扩展先行；P0-05 发布前强制校验兜底 |
| 归一化合并双子块（DemandPriority + SetupTransitionRules）遗漏 | 发布丢失部分内容 | S-6 单测覆盖合并路径 |
| DROP 表早于 S 系列就绪 | 治理/装载断链 | 顺序约束：D-1 待 S-1~S-5 验收后执行 |
| 4号位 前端依赖物理表语义 | 前端联调中断 | F-1 契约确认前置，接口不变即无感 |

---

## 六、责任清单

| # | 动作 | 责任 | 状态 |
|---|---|---|---|
| 1 | 重构方案登记件（本单）| 3号位 | ✅ 本单 |
| 2 | S-1~S-9 治理/装载改道 | 3号位 | ✅（2026-09-23 全绿回填） |
| 3 | D-1~D-2 DROP 表 + 容量确认 | 2号位 | ⏳ |
| 4 | P-1 / F-1 / R-1 各号位确认 | 1号位 / 4号位 / 5号位 | ⏳ |
| 5 | 0号位 备查（不请求裁决）| 0号位 | 抄送 |

---

**起草人**：3号位 ｜ **日期**：2026-09-22
**待办**：① 本方案登记件确认后启动 S-1~S-9 ② 2号位 待 S 系列就绪后执行 D-1~D-2
