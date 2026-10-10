# 📢 3 号位 催办单 — GovernanceController 28 GET 端点裸奔

> **紧急度**：🔴 P0（阻塞上线，与 5 号位 ① ② 同等量级）
> **发送人**：4 号位（前端）
> **接收人**：3 号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
> **日期**：2026-09-12（**2026-09-13 修订：撤销类级 `[Authorize(PlanView)]` 方案，改按业务动作使用对应权限码**）
> **期望修复**：**本周内（2026-09-18 前）**
> **详细上下文**：[3号位契约-依赖与扩展项.md §八.⑤](3号位契约-依赖与扩展项.md) + [4号位-2026-09-13-裁定回退清单.md](4号位-2026-09-13-裁定回退清单.md)

---

## 一、问题陈述（5 分钟读完）

`lps/LPS.APS.Web/Controllers/GovernanceController.cs` 类级**未挂 `[Authorize]`**，类内 50 个 Http 端点中：

- ✅ **22 个写端（POST/PUT/DELETE）已逐方法挂 `[Authorize]`**（1 月前 P0 修复覆盖）
- ❌ **28 个 GET 端点全裸奔**（无 Authorize 也无 AllowAnonymous）

**后果**：任何未登录 / 任意角色 / 任意 scope 的用户都能直接 `GET` 以下端点：

| 泄露面 | 端点示例 | 影响 |
|---|---|---|
| 规则集元数据 | `/api/governance/rule-sets`、`/api/governance/rule-set/{id}/versions` | 泄露规则集结构与版本历史 |
| 参数集元数据 | `/api/governance/parameter-sets`、`/api/governance/parameter-set/{id}/versions` | 泄露参数集结构 |
| 策略 Profile | `/api/governance/strategy-profiles`、`/api/governance/strategy-profile/default` | 泄露策略元数据 |
| 运行历史 | `/api/governance/runs`、`/api/governance/run/{id}/trace` | 泄露 Run ID 序列与执行轨迹 |
| 域定义 | `/api/governance/domain-definition`、`/api/governance/domain-definition/active` | 泄露 `factoryId` / `productFamilyId` / 域激活状态 |
| 域依赖图 | `/api/governance/domain-dependencies` | 泄露跨域依赖关系 |

**对标现状**：5 号位已修完的 11 个查询 Controller + ManualEta 写端（[verify-integration.mjs](verify-integration.mjs) `[C]` 段 8/8 + `[C.D]` 末尾 ManualEta POST/DELETE 2/2 = 10/10 全绿），**3 号位的这 28 个 GET 是当前联调脚本唯一的红**。

---

## 二、实测证据（4 号位联调脚本 2026-09-12 跑出）

```bash
$ cd frontNew && node scripts/verify-integration.mjs

[C.D] GovernanceController 28 GET 鉴权反向（裸账号 → 应 code=403）
  ❌ 裸账号 GET /api/governance/rule-set/1/versions — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/parameter-set/1/versions — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/strategy-profiles — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/rule-sets — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/domain-definition — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/domain-definition/active — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/domain-definition/1 — 期望 403 实际 code=200
  ❌ 裸账号 GET /api/governance/runs — 期望 403 实际 code=200
  ...（共 28 个 GET 端点全裸）

=== 结果 ===
✅ 通过：36
❌ 失败：28   ← 全是 3 号位 §八.⑤
```

**注**：`code=404` 是后端数据不存在（test ID 不存在），`code=200` 是真泄露，`code=400` 是参数校验拦下 — **三种响应都不是 403 = 全裸奔**。

---

## 三、建议修法（5 分钟修复，**2026-09-13 修订**）

**❌ 撤销类级 `[Authorize(PlanView)]` 方案**（1号位 裁定）：
- view-level 角色与 route-level 不一致无法修复（v1.0/v1.1 教训）
- 粒度太粗，无法满足"按业务动作使用对应权限码"的 RBAC 设计原则

**✅ 改用精细化方案**：按业务动作使用对应权限码（28 个 GET 端点逐方法挂）：

```csharp
// 不要这样（2026-09-13 撤销）
[Authorize(Policy = PermissionCodes.PlanView)]  // ← 类级兜底
[ApiController]
[Route("api/[controller]")]
public class GovernanceController : ControllerBase { ... }

// 改这样（按业务动作使用对应权限码）
[ApiController]
[Route("api/[controller]")]
public class GovernanceController : ControllerBase
{
    [Authorize(Policy = PermissionCodes.PlanGovernanceRuleView)]
    [HttpGet("rule-set/{id}/versions")]
    public async Task<...> GetRuleSetVersions(...) { ... }
    
    [Authorize(Policy = PermissionCodes.PlanGovernanceParameterView)]
    [HttpGet("parameter-set/{id}/versions")]
    public async Task<...> GetParameterSetVersions(...) { ... }
    
    // ... 28 个 GET 端点逐方法挂
}
```

**精细化映射**（按业务码细分）：
| 端点分组 | 建议 Policy |
|---|---|
| 治理读端（RuleSet）| `PlanGovernanceRuleView` |
| 治理读端（ParameterSet）| `PlanGovernanceParameterView` |
| 治理读端（StrategyProfile）| `PlanGovernanceStrategyView` |
| 治理读端（Run）| `PlanGovernanceRunView` |
| 域依赖 / 域定义 | `PlanDomainView` |
| 候选对比 | `PlanCandidateView` |
| 审计日志查询（已挂 `AuditView`）| 保持不变 |

> **PermissionCodes 常量参考**：[lps/LPS.APS.Core/Authorization/PermissionCodes.cs](../../lps/LPS.APS.Core/Authorization/PermissionCodes.cs)（若部分常量未建，需先补建）

---

## 四、4 号位验收方式

3 号位 PR merged 后，4 号位会复跑：

```bash
cd frontNew && node scripts/verify-integration.mjs
```

**期望**：
- `[C.D]` 段 28 条全部转 ✅ 绿
- `scan-controllers.mjs --strict` 退出码 = 0（防御纵深自检）
- 整体结果：`✅ 通过：64 / ❌ 失败：0`（当前 37 绿 + 28 红 = 65 目标；~~[F] 段已 2026-09-13 删除~~）

---

## 五、时间期望与依赖关系

| 项 | 期望 | 依赖 |
|---|---|---|
| ~~类级 `[Authorize(PlanView)]`~~ | ~~2026-09-18 前（本周内）~~ | ❌ **2026-09-13 撤销方案**（1号位 裁定：粒度太粗）|
| 精细化方案（28 GET 逐方法挂 `PlanGovernance*` Policy）| **2026-09-18 前（本周内）** | 3 号位独立完成；若部分 PermissionCodes 常量未建，需先补建 |
| 4 号位复跑 `verify-integration.mjs` 验证 | 3 号位 PR merged 后 1h 内 | — |
| 上线准入 | 本项 + 5 号位 ②-2 + ③-path 全绿后解锁 | — |

**前置依赖**：PermissionCodes 完整度 — 若 `PlanGovernanceRuleView` 等常量未建，需 3 号位先补建。

---

## 五.补 EnsureInScopeAsync 等待项 — ❌ 2026-09-13 撤销

~~**额外缺口**：`IDataScopeService.EnsureInScopeAsync(userId, scopeType, value)` 统一断言方法 3 号位仍未补，5 号位 Service 层手工 factory 判断未替换~~ → ❌ **2026-09-13 撤销**：

- 3 号位 已有 `ResolveScopeAsync + DataScopeContext.Allows` 完整替代 `EnsureInScopeAsync`
- 5 号位 Service 层手工 factory 判断**已生效**，不阻塞联调
- 4 号位前端**不再等待** `EnsureInScopeAsync` 落地

**3 号位 无需操作此项**。

---

## 六、附：[C.D] 段 28 个端点红名单（来自 `scan-controllers.mjs` 实测）

完整列表见 [3号位契约-依赖与扩展项.md §八.⑤](3号位契约-依赖与扩展项.md)，含每个端点的 Controller 行号 / Verb / Route / 方法名。

简要归类：

| 资源 | GET 端点数 | 路径前缀 |
|---|---|---|
| RuleSet | 7 | `/api/governance/rule-set*` |
| ParameterSet | 5 | `/api/governance/parameter-set*` |
| StrategyProfile | 8 | `/api/governance/strategy-profile*` |
| Run | 3 | `/api/governance/run*` + `/api/governance/runs` |
| PlanVersion | 1 | `/api/governance/plan-version/{id}/compare-with*` |
| Domain | 4 | `/api/governance/domain-definition*` + `/domain-dependencies` |

合计：**28 个 GET 端点**。

---

## 七、联络点

- 4 号位：本催办单发送人
- 5 号位同步：[5号位契约-依赖与扩展项.md](5号位契约-依赖与扩展项.md)（5 号位 P0 ①② 已闭环，与本催办无关）
- 详细技术细节：[3号位契约-依赖与扩展项.md §八.⑤](3号位契约-依赖与扩展项.md)
- 联调脚本：[scripts/verify-integration.mjs](../scripts/verify-integration.mjs)
- 自检脚本：[scripts/scan-controllers.mjs](../scripts/scan-controllers.mjs)（`--strict` 模式）

---

**修完任意一项让 4 号位复跑 `verify-integration.mjs` 即可验证。**

---

## 八、❌ 2026-09-14 治理红线复核说明 — **2026-09-15 已撤销（基于错误归因）**

> **⚠️ 本节已撤销**：2026-09-15 用户纠正归因错误 — [未命名的Markdown文件.md](未命名的Markdown文件.md) 实际是 **0号位 项目权威** 出的业务裁决（**程序有效**），不是 1号位架构师 的技术裁定。本节红线复核的前提（1号位 ≠ 0号位 → 1号位 9月13日 RBAC 粒度细化裁定程序无效）**部分不成立**：
>
> - ❌ 「9月13日 ManualEta DepartmentCode 撤销裁定程序无效」— 不成立（文件是 0号位 业务裁决，程序有效）
> - ✅ 「1号位 9月13日 RBAC 粒度细化裁定（撤销类级 `[Authorize(PlanView)]`）程序有效」— 仍然成立（与红线 §3.2 / §十一 一致）
>
> **本催办单核心（28 GET 端点精细化方案）不受影响** — §三 精细化方案 §五 时间期望 §六 28 端点红名单全部保留。

---

**历史原文**（2026-09-14 当日写的、现已撤销 — 仅作历史记录）：

**复核依据**：[APS_V1_各号位AI冻结基线治理红线_v1.0_20260914.md](APS_V1_各号位AI冻结基线治理红线_v1.0_20260914.md)

### 8.1 本催办单范围不受红线复核影响

本催办单针对 `GovernanceController` 28 个 GET 端点**鉴权缺失**问题（粒度细化方案），与 9月13日 1号位 裁定 ManualEta DepartmentCode 撤销**无关**，因此：

- ✅ 「撤销类级 `[Authorize(PlanView)]` 方案」+「精细化方案」**保持不变**
- ✅ 期望修复时间 2026-09-18 前不变
- ✅ 28 个 GET 端点红名单不变

### 8.2 9月13日 1号位 裁定的红线复核背景

9月13日 1号位 1号位 裁定 ManualEta DepartmentCode 撤销**程序上无效**（@see 治理红线 §十三：1号位 ≠ 0号位），需走 0号位 正式裁决流程。

**对本催办单的影响**：
- 无直接影响（28 GET 端点鉴权是 3号位 独立任务）
- 仅作背景说明：1号位 9月13日同时做了两类裁定 — 一类是 ManualEta DepartmentCode 撤销（程序无效，等 0号位 裁决），一类是 RBAC 粒度细化（撤销类级 `[Authorize(PlanView)]`）（程序有效，与红线 §3.2 / §十一 一致）

### 8.3 红线复核对本催办单的启示

| 红线条款 | 对本催办单的指引 |
|---|---|
| §3.2 冻结要求存在 + 代码未实现 = 实现缺口 | 28 GET 鉴权缺失 = P0 实现缺口，应整改 |
| §十一 不得因对方号位未交付而删除本号位应有职责 | 3号位 治理端鉴权是 3号位 应有职责，不应等 4号位/5号位 |
| §13 1号位 ≠ 0号位 | 1号位 9月13日 RBAC 粒度细化裁定与红线 §3.2 一致，**程序有效** |
| §4.2 最新高位冻结文档正式覆盖旧结论 | RBAC 精细化方案符合「按业务动作用对应权限码」冻结原则 |

### 8.4 后续动作（❌ 2026-09-15 部分作废）

~~- [ ] 3号位 按本催办单 §三 精细化方案修复（2026-09-18 前）~~ → ✅ 仍有效（核心催办内容不受红线复核影响）
~~- [ ] 4号位 复跑 `verify-integration.mjs` 验证~~ → ✅ 仍有效
- [ ] ~~红线复核背景同步给 0号位 / 1号位~~ → ❌ 作废（红线复核作废）

**实际状态**（2026-09-15）：
- ✅ 3号位 仍按本催办单 §三 精细化方案修复 28 GET 端点（与红线复核无关，是 RBAC 粒度细化原则）
- ❌ 红线复核不再适用（基于错误归因）
- ❌ 不再发 0号位 正式裁决请求（撤回）

---

**复核人**：4号位
**复核日期**：2026-09-14
**对本催办单影响**：~~无影响（28 GET 端点鉴权修复仍按 §三 精细化方案）~~ → ❌ **2026-09-15 部分作废**：红线复核前提错误，但核心催办内容（28 GET 精细化）仍有效
