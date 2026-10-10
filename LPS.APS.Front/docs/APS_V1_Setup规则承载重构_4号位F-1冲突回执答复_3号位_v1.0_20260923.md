# APS V1 Setup 规则承载重构——对 4号位 F-1 冲突回执的答复

**文档性质**：跨号位答复函（3号位 → 4号位）——针对 4号位 2026-09-23《Setup 承载重构 F-1 确认件与 lps 实际代码冲突回执（5 项端点变更 + 请澄清）》的正式裁定与处置说明。
**发送方**：3号位 ｜ **接收方**：4号位（前端）｜ **日期**：2026-09-23
**抄送**：0号位（备查）、2号位（D-1 待执行）、1号位（P-1 待回执）
**性质**：**答复**——F-1 原结论成立，13 端点 = 唯一 Setup 维护 API；双轨第二套（`SetupTransitionRuleController` + 物理表仓储）已撤销。

---

## 〇、一句话

**4号位 观察到的「双套 Setup 端点」属实，但归因与裁定如下**：用户 lps 中并非「13 端点被重写为 5 端点」，而是**并存了两套 HTTP 面**——契约真源 `SetupRuleController`（13 端点，`aps.setup.*`）未动；第二套 `SetupTransitionRuleController`（5 端点，`RuleView/RuleMaintain`）系 3号位 重构 S-2 期间遗留的未登记产物，**已撤销**。F-1「13 端点契约不变、前端零改动」结论**维持成立**，前端按 13 端点开工。

---

## 一、冲突事实核验（3号位 已逐一取证）

| # | 4号位 回执指控 | 核验结果 | 证据 |
|---|---|---|---|
| 1 | 路由前缀改为 `/api/setup-transition-rule` | ⚠️ **存在第二套**：`SetupTransitionRuleController`（`api/[controller]`），但 `SetupRuleController`（`api/governance/setup-rules/*`）**仍存在未动** | 两 Controller 并存；`SetupTransitionRuleController.cs` 已删 |
| 2 | EXACT/DEFAULT 拆端点取消，改单端点按 RuleType 过滤 | ⚠️ 同上——第二套用单端点，真源 13 端点未变 | `SetupRuleController.cs:57-110,130-183` |
| 3 | DTO 重写（双返回 → 单 Id 实体） | ⚠️ 第二套用实体直传；真源 `SetupRuleDto` 双返回（Code 回带）未动 | `SetupRuleController.cs:68` `SetupRuleDto` |
| 4 | Input DTO 删除（实体直传） | ⚠️ 第二套实体直传；真源 `SetupRuleExactInput`/`SetupRuleDefaultInput` 未动 | `SetupRuleController.cs:79,95,152,168` |
| 5 | 权限码改 `RuleView`/`RuleMaintain` | ⚠️ **并存非替换**：`PermissionCodes.cs:44-48` `RuleView=aps.rule.view`/`RuleMaintain=aps.rule.edit` 与 `:76-80` `SetupView/SetupEdit/SetupPublish` **两套都在** | 权限码常量双轨；Setup 维护 API 挂 `aps.setup.*` |

**结论**：4号位 所述「13 端点契约不变完全不成立」**不准确**——13 端点契约**始终成立**；问题在于代码里多出一套未登记的第二 HTTP 面。此为 3号位 重构 S-2 期间的遗留，非用户 lps 有意重写，特此澄清。

## 二、裁定：F-1 维持成立，13 端点 = 唯一真源

对 4号位 三选项答复：

| 选项 | 裁定 |
|---|---|
| (a) F-1 写错、按 lps 重写 | ❌ **不采纳**——F-1「13 端点不变」成立 |
| (b) 撤销恢复 13 端点 | ✅ **采纳（变体）**——撤销对象是第二套 `SetupTransitionRuleController`（非「lps 重写」） |
| (c) 新业务基线 | ❌ 无新基线 |

**真源**：`SetupRuleController` 13 端点 + 3 lookups + `aps.setup.view/edit/publish` = 唯一 Setup 维护 API（契约登记件 §四 S-1 登记对象，红线 #5 接口签名未动）。

## 三、3号位 已执行的撤销（2026-09-23）

| # | 撤销对象 | 说明 | 状态 |
|---|---|---|---|
| 1 | `SetupTransitionRuleController.cs` | 第二套 HTTP 面（5 端点，`api/setup-transition-rule`，`RuleView/RuleMaintain`）——契约登记件未登记，撤销 | ✅ 已删 |
| 2 | `ISetupTransitionRuleRepository.cs` | 今早复活的物理表仓储接口——违反 S-3「物理表访问清零」+ 0号位「不建独立表」口径 | ✅ 已删 |
| 3 | `SetupTransitionRuleRepository.cs`（Engine/Governance） | Dapper 直读 `[dbo].[SetupTransitionRule]` 实现，同违反 | ✅ 已删 |
| 4 | `SetupTransitionRuleService` + `ISetupTransitionRuleService` | **保留**（治理内部 5 方法，SetupRuleService 引用其常量；非 HTTP 面） | ✅ 保留 |

> 构建验证：`dotnet build LPS.APS.sln` 0 错误（11 个既有历史警告，非本次引入）。

## 四、对 4号位 前端的工作指引

1. **前端按 F-1 原确认件开工**（13 端点 + 3 lookups + `aps.setup.*`），**零代码改动**——4号位 回执 §五 暂停动作清单可全部解除。
2. **无需处理** 回执 §二 列出的 9 文件改动（SetupExactDialog / SetupDefaultDialog 不合并、双 store 不合并、权限码不改）——那些是针对第二套的推测，现已撤销。
3. **SetupSource 4 值**（`GanttDataDto.cs:176`）：填充源 = 1号位 Solver（`SetupOutcomeToSource`），查询通道变更归 **5号位 R-1**——4号位 仅消费显示，不涉改动；涉及 5号位 事务由 4号位 直通沟通。

## 五、关联号位待办（不受本回执影响）

| 号位 | 任务 | 状态 |
|---|---|---|
| 1号位 | P-1 快照第⑦块语义回执（零改动） | ⏳ 待回执 |
| 2号位 | D-1 DROP `SetupTransitionRule` 物理表（红线 #6）+ D-2 容量回执 | ⏳ 待执行 |
| 5号位 | R-1 SetupSource 查询通道 | ⏳ 待通知（4号位直通） |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**待办**：4号位 确认后按 13 端点开工 → P-1/F-1 回执 → D-1 闭环
