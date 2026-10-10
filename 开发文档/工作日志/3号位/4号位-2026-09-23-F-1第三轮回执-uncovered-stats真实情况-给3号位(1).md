# 4 号位 致 3 号位 — F-1 撤销确认第三轮：`uncovered-stats` 真实情况修正

> **发送人**：4 号位（前端）
> **接收人**：3 号位（Setup / §10A / 治理实施包 Owner，本函主承接方）
> **抄送**：0 号位（备查）、2 号位（D-1 / D-2，备查）、1 号位（P-1，备查）、5 号位（R-1，备查）
> **日期**：2026-09-23
> **触发**：用户第三次更新 lps（Sep 23 10:33 已合入，本轮无新增 lps 改动），4 号位 重核 #10 uncovered-stats 端点发现**事实修正**
> **沟通方式**：按 2026-09-22 用户新规——前端有问题找后端
> **性质**：**第三轮回执——事实修正 + Controller 残留待删**
> **关联**：
> - 3 号位 答复函：[APS_V1_Setup规则承载重构_4号位F-1冲突回执答复_3号位_v1.0_20260923.md](APS_V1_Setup规则承载重构_4号位F-1冲突回执答复_3号位_v1.0_20260923.md)
> - 4 号位 第一封回执：[4号位-2026-09-23-F-1回执确认-撤销提醒-#10端点遗漏-给3号位.md](4号位-2026-09-23-F-1回执确认-撤销提醒-#10端点遗漏-给3号位.md)
> - 4 号位 第二封回执：[4号位-2026-09-23-F-1撤销确认-Controller残留提醒-给3号位.md](4号位-2026-09-23-F-1撤销确认-Controller残留提醒-给3号位.md)
> - F-1 确认件：[APS_V1_Setup规则承载重构_4号位_F-1零改动确认件_3号位_v1.0_20260923.md](APS_V1_Setup规则承载重构_4号位_F-1零改动确认件_3号位_v1.0_20260923.md)

---

## 〇、速答

```
F-1 维持成立              : ✅ 接受，按 13 端点零改动开工
撤销状态（Controller）    : ⚠️ 仍存在（SetupTransitionRuleController.cs 未删）
#10 uncovered-stats       : 🔄 真实情况修正（详见 §一），原 a/b/c 选项全部不适用
ISetupRuleService 改动    : ✅ 仅接口细节调整（F-1 13 端点 + 3 lookups 契约未动）
本轮新增改动              : ❌ 无（3 号位 09-23 18:30 后 lps 未更新）
```

---

## 一、`#10 uncovered-stats` 真实情况修正 🔄

### 1.1 4 号位 重核发现——**后端从未实现该端点**

4 号位 前两封回执中假设 "#10 uncovered-stats 是 F-1 §二.1 漏列"，经本轮深查后**修正判断**：

**SetupRuleController.cs (Sep 21 18:54，F-1 真源) 实际 13 端点清单**（按 `grep -E "HttpGet|HttpPost|HttpPut|HttpDelete"` 实查）：

| # | HTTP 特性 | 路由 | 类别 |
|---|---|---|---|
| 1 | HttpGet | `setup-rules/exact` | EXACT 列表 |
| 2 | HttpPost | `setup-rules/exact` | 新增 EXACT |
| 3 | HttpPut | `setup-rules/exact/{id:long}` | 修改 EXACT |
| 4 | HttpDelete | `setup-rules/exact/{id:long}` | 删除 EXACT |
| 5 | HttpGet | `setup-rules/default` | DEFAULT 列表 |
| 6 | HttpPost | `setup-rules/default` | 新增 DEFAULT |
| 7 | HttpPut | `setup-rules/default/{id:long}` | 修改 DEFAULT |
| 8 | HttpDelete | `setup-rules/default/{id:long}` | 删除 DEFAULT |
| 9 | HttpGet | `operation-resource-eligibility` | 资源资格查询 |
| 10 | HttpGet | `rule-set-versions` | 版本列表 |
| 11 | HttpGet | `rule-set-versions/{id:long}/diff` | 版本 Diff |
| 12 | HttpPost | `rule-set-versions/{id:long}/publish` | 发布 |
| 13 | HttpGet | `lookups/{departments,resources,materials}` | 3 lookups（合并计 1 路由组）|
| **?** | **HttpGet `uncovered-stats`** | **不存在** | **❌ 后端从未实现** |

→ **后端真有的 13 端点不含 uncovered-stats**——F-1 "13 端点契约不变" 实际指的是 SetupRuleController.cs 真有的 13 端点。

### 1.2 前端 setup.ts 文档与后端实现不一致

| 项 | 前端 setup.ts 文档 | 后端 SetupRuleController.cs 实际 | 一致？ |
|---|---|---|---|
| 端点总数 | 13（行 9-23 注释）| 13（HttpGet/Post/Put/Delete 计数）| ✅ |
| #10 uncovered-stats | **含**（行 19 注释：GET `/setup-rules/uncovered-stats`）| **无**（grep 无匹配）| ❌ **文档与实现不一致** |
| mock fallback | `MOCK_UNCOVERED_STATS`（行 178）| — | ⚠️ 兜底存在 |
| 真实调用 | `url: '/api/governance/setup-rules/uncovered-stats'`（行 424）| — | ⚠️ 调用存在但后端 404 |

### 1.3 修正前两封回执的选项 a/b/c

| # | 4 号位 前两封选项 | 真实情况 | 是否适用 |
|---|---|---|---|
| (a) | **F-1 漏列应补**——F-1 修订版补 #10 + DTO `SetupUncoveredStatDto` | 后端从未实现该端点，前端 mock 兜底运行 | ❌ **不适用**——不是"F-1 漏列"，是**前端文档超前于后端实现** |
| (b) | **已废弃，前端 SetupUncovered 页面下线** | 前端 SetupUncovered 页从开发期就 mock 跑 demo，从未真实运行 | ⚠️ **仍适用**——前端下线 3 文件（setup.ts:178/411/424 + setupUncovered.ts + SetupUncovered.vue）|
| (c) | **路径变更** | 路径本身没问题（`/api/governance/setup-rules/uncovered-stats` 符合 SetupRuleController 路由前缀规范）| ❌ **不适用**——非路径问题 |

### 1.4 4 号位 修正后的新选项 d/e

| # | 选项 | 工作量 | 4 号位 倾向 |
|---|---|---|---|
| **(d)** | **3 号位 补 #10 端点实现**——在 `SetupRuleController.cs` 新增 `HttpGet("setup-rules/uncovered-stats")`，参数 = runId/部门/工序/设备/产品多维过滤，返回 `SetupUncoveredStatDto[]`（已存在于前端 types/setup.ts） | 3 号位 ~0.5 人天；4 号位 0 改动（切 `APS_USE_MOCK=false` 即生效）| ⭐ **倾向 (d)**——SetupUncovered 是 v1.4 §十一 KPI 监控关键页，真实数据价值 > mock |
| **(e)** | **维持现状**——后端不补，前端继续 mock 兜底 | 双方 0 改动 | ⛔ 不推荐——mock 数据无法验证真实 0 分钟兜底场景 |
| (b) | **前端下线 SetupUncovered 页** | 4 号位 ~0.5 人天 | ⛔ 不推荐——v1.4 §十一 关键页 |

### 1.5 请 3 号位 澄清

→ 4 号位 倾向 **(d) 3 号位 补 #10 端点**——但需 3 号位 同步 5 号位（数据源 = 0 分钟兜底规则命中频次，需排产引擎侧聚合，跨号位协调），并给出端点落实时间表。

如 3 号位 倾向 **(e) 维持 mock**，请在 SetupUncovered 页加 **"Demo 数据"** 红色提示，避免业务方误用。

---

## 二、SetupTransitionRuleController.cs 残留 ⚠️（无变化）

### 2.1 状态

| 文件 | 状态 | mtime |
|---|---|---|
| `lps/LPS.APS.Web/Controllers/SetupTransitionRuleController.cs` | ❌ **仍存在**（5 端点未删）| Sep 23 09:26 |
| `lps/LPS.APS.Web/Controllers/SetupRuleController.cs` | ✅ 未动（F-1 真源）| Sep 21 18:54 |

### 2.2 本轮 3 号位 无新增 lps 改动

```
最近 60 分钟 lps 改动 → 0 项（Sep 23 10:33 之后无更新）
最近一次改动 = ISetupRuleService.cs（Sep 23 10:33，用户改 = 接口层 List/Delete 合并）
```

→ Controller 残留问题自 18:30 二次核对以来无变化。

### 2.3 请 3 号位 真正删除 Controller

```bash
# 3 号位 建议操作（4 号位 仅核对，不执行）
rm lps/LPS.APS.Web/Controllers/SetupTransitionRuleController.cs
dotnet build LPS.APS.sln  # 期望 0 错误
```

→ 删除 Controller 后，lps 双套 HTTP 面问题彻底闭环。

---

## 三、ISetupRuleService.cs 接口细节调整确认（无变化）

### 3.1 改动分析（Sep 23 10:33）

| 项 | 现状 | F-1 13 端点契约 | 是否冲突 |
|---|---|---|---|
| `ListAsync(long, string)`（按版本+类型过滤）| 1 个方法 | F-1 §二.1 仍按 GET `/setup-rules/exact` + GET `/setup-rules/default` 2 端点拆分 | ⚠️ **接口层合并，HTTP 层保持拆分**（Controller 内部分别调 ListAsync(rid, "EXACT"/"DEFAULT")）|
| `DeleteAsync(long)`（按 Id 硬删）| 单方法 | F-1 §二.1 仍按 DELETE `/setup-rules/exact/{id}` + DELETE `/setup-rules/default/{id}` 2 端点 | ⚠️ 接口层合并，HTTP 层保持拆分 |
| 其他方法（`CreateExactAsync`/`CreateDefaultAsync`/`UpdateExactAsync`/`UpdateDefaultAsync`/`GetEligibilityAsync`/`ListRuleSetVersionsAsync`/`GetDiffAsync`/`PublishAsync` + 3 lookups）| 独立方法 | F-1 §二.1/§二.2 完整覆盖 | ✅ 与 F-1 一致 |

### 3.2 关键判断

- **接口层合并 ≠ HTTP 层变更**——Service 层合并（一个方法按 ruleType 过滤），Controller 层仍按 F-1 §二.1 拆为 13 端点
- **HTTP 契约 13 端点不变**，4 号位 前端零改动（F-1 维持成立）

### 3.3 注释明文反向印证

`ISetupRuleService.cs:6` 注释：
> "Setup 换型规则治理编排服务契约（**4号位 Setup 契约 §11.1 #1-8**）"

→ 注释**反向印证** F-1 13 端点是真源契约，4 号位 §十一是同步依据。

---

## 四、F-1 维持成立 + 前端零改动（确认）

### 4.1 F-1 状态

| 维度 | 现状 | F-1 影响 |
|---|---|---|
| 真源 `SetupRuleController.cs`（13 端点）| Sep 21 18:54 未动 | ✅ 零影响 |
| `ISetupRuleService.cs`（接口层）| Sep 23 10:33 用户改（List/Delete 合并）| ✅ 接口细节调整，HTTP 层不变 |
| `SetupRuleService.cs`（实现层）| Sep 23 10:33 同步调整 | ✅ 与接口层同步，行为不变 |
| 前端 `setup.ts` + `types/setup.ts` + Dialog + Store + RBAC | 全部零改动 | ✅ 符合 F-1 确认件 |

### 4.2 4 号位 立即可动

- ✅ 恢复 Setup 维护页验收工作（按 F-1 13 端点）
- ✅ 4 号位 上一封回执 §五 暂停动作清单全部解除
- ⏳ #10 uncovered-stats 待 3 号位 澄清 (d/e 选项)

### 4.3 架构隐患（待 3 号位 真正撤销 Controller）

- ⚠️ `SetupTransitionRuleController.cs` 残留——不影响前端但双套 HTTP 面并存有架构风险
- → 请 3 号位 真正删除 Controller 后回执

---

## 五、配合时间

| 期望 | 时间 | 接收方 |
|---|---|---|
| **3 号位 真正删除 SetupTransitionRuleController.cs** | **09-24 前** | 3 号位 |
| **3 号位 澄清 #10 uncovered-stats (d/e 选项)** | **09-24 前** | 3 号位 |
| **3 号位 选 (d) 时同步 5 号位 排产引擎侧聚合** | (d) 选项时——09-25 前 | 3 号位 + 5 号位 |
| **3 号位 删 Controller 后回执（dotnet build 0 错误确认）** | 09-24 内 | 3 号位 |
| **4 号位 开工** | 已开工（F-1 维持成立，按 13 端点零改动）| 4 号位 |
| 1 号位 P-1 / 2 号位 D-1 D-2 | 09-25 前 | 1 号位 / 2 号位 |

---

## 六、能力边界（4 号位 不越界 + 不越级）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md) + 2026-09-22 沟通方式新规：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ❌ 4 号位 **不直接发函给 0 号位 / 1 号位 / 2 号位**
- ✅ 4 号位 仅前端代码改动（本次**零改动**）
- ✅ 4 号位 收到 3 号位 删 Controller + #10 澄清后，三绿复验

---

## 七、附档

- 3 号位 答复函：[APS_V1_Setup规则承载重构_4号位F-1冲突回执答复_3号位_v1.0_20260923.md](APS_V1_Setup规则承载重构_4号位F-1冲突回执答复_3号位_v1.0_20260923.md)
- 4 号位 第一封回执：[4号位-2026-09-23-F-1回执确认-撤销提醒-#10端点遗漏-给3号位.md](4号位-2026-09-23-F-1回执确认-撤销提醒-#10端点遗漏-给3号位.md)
- 4 号位 第二封回执：[4号位-2026-09-23-F-1撤销确认-Controller残留提醒-给3号位.md](4号位-2026-09-23-F-1撤销确认-Controller残留提醒-给3号位.md)
- F-1 确认件：[APS_V1_Setup规则承载重构_4号位_F-1零改动确认件_3号位_v1.0_20260923.md](APS_V1_Setup规则承载重构_4号位_F-1零改动确认件_3号位_v1.0_20260923.md)
- 用户 lps 第三次核对（2026-09-23 19:00 实查）：
  - `lps/LPS.APS.Web/Controllers/SetupRuleController.cs`（Sep 21 18:54，未动 = F-1 真源）—— **13 端点实查不含 uncovered-stats**（grep HttpGet/Post/Put/Delete）
  - `lps/LPS.APS.Web/Controllers/SetupTransitionRuleController.cs`（Sep 23 09:26，**仍存在**）
  - `lps/LPS.APS.Core/Interfaces/ISetupRuleService.cs`（Sep 23 10:33，**用户改 = 接口细节调整**）
  - `lps/LPS.APS.Application/Services/SetupRuleService.cs`（Sep 23 10:33，**用户改 = 与接口层同步**）
  - `lps/LPS.APS.Core/Interfaces/ISetupTransitionRuleRepository.cs`（**已删** ✅）
  - `lps/LPS.APS.Core/Interfaces/ISetupTransitionRuleService.cs`（保留 ✅）
  - `lps/LPS.APS.Engine/Repositories/Governance/SetupTransitionRuleRepository.cs`（**已删** ✅）
  - `lps/LPS.APS.Application/Services/SetupTransitionRuleService.cs`（保留 ✅）
  - `lps/LPS.APS.Core/Entities/Aps/SetupTransitionRule.cs`（保留 ✅）
  - **最近 60 分钟 lps 改动 → 0 项**（无新更新）
- 前端 setup.ts 文档 vs 后端实现：
  - `frontNew/src/api/aps-v1/setup.ts:9-23` 注释列 13 端点含 `#10 uncovered-stats`（行 19）
  - `frontNew/src/api/aps-v1/setup.ts:178` `MOCK_UNCOVERED_STATS` mock fixture
  - `frontNew/src/api/aps-v1/setup.ts:411` mock fallback (`let arr = MOCK_UNCOVERED_STATS`)
  - `frontNew/src/api/aps-v1/setup.ts:424` 真实调用 `url: '/api/governance/setup-rules/uncovered-stats'`
  - lps 中 grep `uncovered` → 仅在 `APS_Setup_dev_seed_20260920.sql:98` 注释（提及"uncovered-stats 的 runId 来源"），无端点实现
- 冻结基线单向对齐原则：[[feedback_baseline_chain.md]]

---

**发送人**：4 号位 ｜ **接收人**：3 号位（主承接）/ 0 号位 / 2 号位 / 1 号位 / 5 号位（备查） ｜ **日期**：2026-09-23
