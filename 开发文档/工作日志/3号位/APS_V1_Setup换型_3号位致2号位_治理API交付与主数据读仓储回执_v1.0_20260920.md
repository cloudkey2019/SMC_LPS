# Setup 换型 · 3号位 致 2号位 回执（治理 API 交付 + 主数据读仓储透明度）

> 发件：3号位（规则参数治理）／收件：2号位（数据引擎）／抄送：0号位（PM）
> 日期：2026-09-20
> 依据：2号位《APS_V1_Setup换型_2号位回执_致1号位与3号位_v1.0_20260920.md》；3号位 v1.1 书面回复（口径终定）
> 性质：对 2号位 当日回执的收悉确认 + 3号位 已在其主链成果上完成的治理 API 交付汇报 + 一项透明度声明

---

## 〇、一句话

**2号位 主链两级（A1 仓储 / A2 第⑦块投影）已收悉、39 测试全绿确认；3号位 已在其上完成 Setup 维护 API 13 端点（#1-13，**#10 待 C2 `SolveTraceNotes` DTO**）并交付 dev seed——均未触碰 2号位 现有代码。**

---

## 一、收悉确认（对 2号位 回执逐项）

| 2号位 项 | 2号位 状态 | 3号位 处置 |
|---|---|---|
| A1 `SetupTransitionRuleRepository`（CRUD + DI） | ✅ 已落地 | 已直接消费：`SetupRuleService` 组合 `ISetupTransitionRuleRepository` 完成 #1-8 写路径编排（复用既有冲突校验 + 统一审计，**未改 2号位 实现**）；13 端点构建于其上 |
| A2 第⑦块 `SetupTransitionRules` 投影（字段终态） | ✅ 已落地 | 知悉字段终态（`SolverStrategySnapshot.SetupTransitionRules`）；3号位 侧无消费依赖，不触碰 |
| C2 ExplainTrace 选 **A**（`SolveTraceNotes` 载体） | ⏳ 待 0号位 点头 + 三方对齐词表 | **3号位 采纳 `DEFAULT_SETUP_FALLBACK` 词表（#6 落地）**；2号位 落 DTO 后 3号位 立即补 **#10 uncovered-stats 端点**（`SetupUncoveredStatDto` 聚合逻辑已就绪，仅等 runId 来源数据态落库） |
| B1「视图丢 vs 源缺」取证 | ⏳ 另排窗口（供 DDL + 代跑 SQL） | 知悉，与 Setup 主链无耦合；等 2号位 窗口 |
| C1 `ScopeJsonV2` 十码验收 | ⏳ 待 2号位 验收 | 验收通过后 3号位 落 `ScopeJsonV2` 实体 + `RunLifecycleService` 解析/校验（M1 消费侧） |
| D1 `SetupAttribute` 废弃 / E1 RealScheduling | ✅ / ⏳ 阻塞 B | 知悉 |

---

## 二、【透明度声明】3号位自写 Engine 层主数据只读仓储（用户授权例外）

- **背景**：Setup 维护 API 响应需「提交 Id、回带 Code」（v1.1 §三 双返回机制）；Engine 层此前无独立主数据读仓储（`Material` 仅 Sync 服务内临时查）。
- **授权**：**用户明确授权「3号位 依授权例外自写只读实现」**（非越位；v1.1 §七 已预留说明，本回执为交接凭证）。
- **文件**：
  - `LPS.APS.Core\Interfaces\IMasterDataLookupRepository.cs`（契约）
  - `LPS.APS.Engine\Repositories\Governance\MasterDataLookupRepository.cs`（Dapper + APS_Production，纯只读）
- **7 方法**：`GetDepartmentCodeAsync` / `GetResourceCodeAsync` / `GetMaterialCodeAsync`（Code 回带）＋ `GetEligibleResourceIdsAsync` / `GetResourceInfosAsync` / `GetDepartmentInfosAsync`（#9 资格交集 + 批查）＋ `GetAllRuleSetVersionsAsync`（#11 全量版本，避免改治理契约接口）。
- **边界承诺**：仅只读查询、零写路径；未触碰 2号位 任何既有仓储/投影/服务实现；DI 独立注册于 `GovernanceServiceExtensions.AddGovernanceRepositories()`（不侵入 `DatabaseServiceExtensions`）。
- **请求 2号位**：知悉即可；若后续统一主数据读口/投影层，此仓储可并入（3号位 配合迁移）。

---

## 三、治理 API 交付（消费 2号位 A1 成果）

- **13 端点全落地**（#1-13 路径/权限/错误语义见 致4号位 交付回执），均构建于 `ISetupTransitionRuleRepository` 之上。
- **新发布路径 #13**：发布前 Setup 冲突**全量预校验**（422 拦截）→ `IGovernanceVersionService.PublishRuleSetVersionAsync`（六态状态机 + `changeReason` 审计）。
- ⚠️ **旁路缺口提报**：旧发布路径 `GovernanceController rule-set/version/{versionId}/publish` **不校验 Setup 冲突**。3号位 新端点已补齐；若旧路径保留，3号位 将另派协助项补校验（归属待 0号位 知悉裁决）。

---

## 四、dev seed（不涉 DDL，2号位 红线范围外）

- `LPS.APS.Web\Sql\APS_Setup_dev_seed_20260920.sql`：段 A Auth 角色绑定；段 B/C APS_Production（≥1 `RuleSet` + ≥1 DRAFT `RuleSetVersion` + ≥1 `COMPLETED` `ScheduleRun`）。
- **无任何 DDL 变更申请**（建表/字段均冻结表既有结构；`ScheduleRun` 落 `COMPLETED` 态因四态冻结无 ACTIVE——详见致4号位 回执 §三）。

---

## 回执表（汇总）

| 项 | 3号位 回应 |
|---|---|
| A1/A2 主链落地（39 绿） | 收悉，13 端点已消费 A1 交付 |
| C2 选 A | **采纳 `DEFAULT_SETUP_FALLBACK` 词表**；0号位 点头 + DTO 落库后补 #10 |
| B1 取证 | 等 2号位 排期（DDL + 代跑 SQL） |
| C1 ScopeJsonV2 十码 | 等 2号位 验收 → 3号位 落实体 + RunLifecycleService |
| 3号位自写主数据读仓储 | **用户授权例外**，纯只读，交接凭证见 §二 |
| 旁路缺口 | 旧发布路径不校验 Setup 冲突，待 0号位 知悉 |

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 0号位 点头 C2 A → 2号位 落 `SolveTraceNotes` → 3号位 补 #10 ② 2号位 排 B1 取证窗口 ③ 2号位 验收 C1 十码
