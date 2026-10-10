# 📢 3 号位 催办单 — GovernanceController 28 GET 端点裸奔

> **紧急度**：🔴 P0（阻塞上线，与 5 号位 ① ② 同等量级）
> **发送人**：4 号位（前端）
> **接收人**：3 号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
> **日期**：2026-09-12
> **期望修复**：**本周内（2026-09-18 前）**
> **详细上下文**：[3号位契约-依赖与扩展项.md §八.⑤](3号位契约-依赖与扩展项.md)

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

## 三、建议修法（5 分钟修复）

**最小工作量方案**：在 [lps/LPS.APS.Web/Controllers/GovernanceController.cs:20](d:\proj\aps\aps-production-new\lps\LPS.APS.Web\Controllers\GovernanceController.cs#L20) 类声明前挂类级 `[Authorize(Policy = PermissionCodes.PlanView)]`（对应 `aps.plan.view`，5 角色均持有，不会误拦）。

```csharp
[Authorize(Policy = PermissionCodes.PlanView)]  // ← 类级兜底（28 GET 一次性堵漏）
[ApiController]
[Route("api/[controller]")]
public class GovernanceController : ControllerBase
{
    // 现有 22 个写端的方法级 [Authorize] 保留（精细化策略）
    // 28 个 GET 端点无需改动 → 走类级兜底
}
```

**精细化方案**（如需按业务码细分，可逐方法挂）：
| 端点分组 | 建议 Policy |
|---|---|
| 治理读端（RuleSet / ParameterSet / StrategyProfile / Run）| `PlanView` |
| 域依赖 / 域定义 | `PlanView` |
| 候选对比 | `PlanView` |
| 审计日志查询（已挂 `AuditView`）| 保持不变 |

---

## 四、4 号位验收方式

3 号位 PR merged 后，4 号位会复跑：

```bash
cd frontNew && node scripts/verify-integration.mjs
```

**期望**：
- `[C.D]` 段 28 条全部转 ✅ 绿
- `scan-controllers.mjs --strict` 退出码 = 0（防御纵深自检）
- 整体结果：`✅ 通过：64 / ❌ 失败：0`（当前 36 绿 + 28 红 = 64 目标）

---

## 五、时间期望与依赖关系

| 项 | 期望 | 依赖 |
|---|---|---|
| 类级 `[Authorize(PlanView)]` | **2026-09-18 前（本周内）** | 3 号位独立完成，无上下游依赖 |
| 4 号位复跑 `verify-integration.mjs` 验证 | 3 号位 PR merged 后 1h 内 | — |
| 上线准入 | 本项 + 5 号位 ③④ 全绿后解锁 | — |

**无前置依赖** — 这是 3 号位 5 分钟内可独立修复的 P0，不阻塞任何其它工作。

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
