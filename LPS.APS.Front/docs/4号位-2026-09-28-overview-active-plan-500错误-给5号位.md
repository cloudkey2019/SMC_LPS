# 4 号位 → 5 号位：`/api/overview/active-plan` 越界请求 500 而非 4xx（pmc 视角实测）

> **紧急度**：🔴 P0（阻塞 4 号位 排产总览 / Gantt 页面调试：pmc 登录后两页均空白）
> **发送人**：4 号位（前端）
> **接收人**：5 号位（查询侧端点 Owner）
> **抄送**：3 号位（RBAC scope 校验 Owner）、0 号位（治理留档）
> **日期**：2026-09-28
> **性质**：**单点催办**（1 个端点、3 个根因可能方向、1 个契约澄清）
> **期望修复**：**本周内（2026-10-03 前合入 dev 分支）**

---

## 一、问题陈述（5 分钟读完）

`/aps/overview`（排产总览）页面今日用 pmc 登录后实测，**右侧 4 张卡片全部空白**，打开 `/aps/gantt` 后**甘特图同样空白**。F12 Network 抓到一个端点直接 500：

```
GET https://localhost:7044/api/overview/active-plan?factoryCode=SZ
→ HTTP/1.1 500 Internal Server Error
```

| 实测项 | 结果 |
|---|---|
| `?factoryCode=SZ`（pmc token） | ❌ **500** |
| `?factoryCode=BJ`（pmc token） | ⚠️ 待复测（用户尚未跑，**估计也 500**——同一 SQL 路径） |
| `?factoryCode=SUZ`（pmc token） | ⚠️ 待复测 |
| 不带 `factoryCode`（pmc token） | ⚠️ 待复测 |
| `?factoryCode=SZ`（admin token） | ⚠️ 待复测（admin.scope=* 应允许；但仍可能 500，**说明根因未必在 scope**） |

**期望行为**（任选其一即可，由 5 号位 裁决）：
- ① **`factoryCode` 不在用户 scope 内 → 403**（前端 `assertScope` 已支持，详见 §三）
- ② **`factoryCode` 格式非法 / 不存在 → 400**（带可读 message 提示前端提示用户）
- ③ **factoryCode 合法且在 scope 内 → 200**（可能为空数据）

**绝对不能接受**：500（既不暴露根因、也不让前端做出重试/降级判断）。

---

## 二、实测证据（F12 + curl 待 4 号位 回填）

### 2.1 F12 Network 截图（4 号位 浏览器实测 09-28）

**用户提供的 Console 抓包**（部分截图）：
```
> fetch('/api/overview/active-plan?factoryCode=SZ', {
    headers: { 'Authorization': 'Bearer <pmc-token>' }
  }).then(r => r.status + ' ' + r.statusText)
< 500 Internal Server Error
```

→ 4 号位 F12 Network 截图将于本函附录 2 次回填（请 5 号位 看到此函时回执索取）。

### 2.2 curl 复现脚本（5 号位 直接跑）

```bash
# 1. 登录 pmc 取 token
$ PMC_TOKEN=$(curl -sk -X POST https://localhost:7044/api/auth/login \
    -H 'Content-Type: application/json' \
    -d '{"userCode":"pmc","password":"Password@123"}' \
  | jq -r '.data.accessToken')

# 2. 复现 500
$ curl -sk -i -H "Authorization: Bearer $PMC_TOKEN" \
    "https://localhost:7044/api/overview/active-plan?factoryCode=SZ"
HTTP/1.1 500 Internal Server Error
... (HTML / 框架默认错误页)
```

### 2.3 admin 视角对照（判断根因是否在 RBAC）

```bash
$ ADMIN_TOKEN=$(curl -sk -X POST https://localhost:7044/api/auth/login \
    -H 'Content-Type: application/json' \
    -d '{"userCode":"admin","password":"Admin@123456"}' \
  | jq -r '.data.accessToken')

$ curl -sk -i -H "Authorization: Bearer $ADMIN_TOKEN" \
    "https://localhost:7044/api/overview/active-plan?factoryCode=SZ"
# 期望：admin.scope=* 应返 200（哪怕空数据）；若仍 500 → 根因不在 RBAC，在 SQL
```

→ **此条是定位关键，请 5 号位 优先跑一下**。

---

## 三、4 号位 已做的 scope 校验（仅供参考）

前端已在调用前置 scope 校验，但**无法阻止后端 500**。代码层：

[src/store/modules/aps/scope.ts:84-99](src/store/modules/aps/scope.ts#L84-L99)
```ts
function assertScope(scope: DataScope, target: { factoryCode?: string; ... }):
  | { ok: true }
  | { ok: false; field: string; actual: string }
{
  if (target.factoryCode !== undefined) {
    const allowed = scope.factoryCodes
    if (allowed.length > 0 && !allowed.includes(target.factoryCode)) {
      return { ok: false, field: 'factoryCodes', actual: target.factoryCode }
    }
  }
  ...
}
```

→ 前端只在校验 `aps.included(factoryCode)` 时返回 `ok:false`；**不会主动剔除非法 factoryCode 也不抛 500**。

→ **因此 500 一定是后端的问题**（SQL 关联缺值 / 索引外查询 / 全局 try-catch 未覆盖 等可能）。

---

## 五、5 号位 需要落地的 1 项 + 1 个澄清

### 项 1：修复 500（P0 唯一项）

请定位 `OverviewController.cs` 中 `GET /api/overview/active-plan` 的实现，按**期望行为三选一**修复：

```csharp
// 伪代码示意，按贵方实际架构调整
[HttpGet("active-plan")]
public async Task<IActionResult> GetActivePlan([FromQuery] string? factoryCode)
{
    // A. 校验 scope（前置）
    if (!string.IsNullOrEmpty(factoryCode))
    {
        var userFactories = await _rbacService.GetUserFactoryCodes(User);
        if (userFactories.Count > 0 && !userFactories.Contains(factoryCode))
            return Forbid();  // → 403
    }

    // B. 校验格式（前置，避免脏数据进 SQL）
    if (!string.IsNullOrEmpty(factoryCode) && !IsValidFactoryCode(factoryCode))
        return BadRequest(new { code = 400, message = $"非法 factoryCode: {factoryCode}" });

    // C. 真正查询（包 try-catch 转 5xx 为 500 + 内部日志，避免裸异常冒到框架）
    try { return Ok(await _overviewService.GetActivePlanAsync(factoryCode)); }
    catch (Exception ex)
    {
        _logger.LogError(ex, "active-plan 查询异常 factoryCode={FactoryCode}", factoryCode);
        return StatusCode(500, new { code = 500, message = "查询失败，请重试或联系管理员" });
    }
}
```

### 项 2：factoryCode 格式澄清（P1，3 个不一致）

| 来源 | 值样例 |
|---|---|
| `seed-dev.mjs` L108-116 | `'BJ'` / `'SUZ'` |
| `src/store/modules/aps/scope.ts:209`（mock fixture） | `'F-BJ-01'` / `'F-SUZ-01'` |
| 前端用户实测 | `'SZ'` |

→ **3 套不一致**。请 5 号位 确认：
1. **生产/seed 真实值**是 `BJ/SUZ` 还是 `F-BJ-01/F-SUZ-01`？
2. 前端发 `'SZ'`（大写 SZ）会被后端识别吗？还是需要 `SUZ`？

→ 若生产值是 `BJ/SUZ`：前端 mock 与生产 fixture（`F-*-01`）需要同步对齐 seed，**本函 §六 4 号位 也会修**。

---

## 六、4 号位 验收清单（5 号位 1 项 + 1 个澄清落地后 4 号位 立即跑）

| # | 验证 | 通过标准 |
|---|---|---|
| V1 | `factoryCode=SZ`（pmc） | 期望 4xx（403 或 400），**不再 500** |
| V2 | `factoryCode=BJ`（pmc） | 200（pmc.scope 含 BJ） |
| V3 | `factoryCode=SUZ`（pmc） | 200（pmc.scope 含 SUZ） |
| V4 | 不带 `factoryCode`（pmc） | 200（pmc 全域聚合） |
| V5 | `factoryCode=SZ`（admin） | 200（admin.scope=*）—— **关键**：验证 SQL 不在 scope 上崩 |
| V6 | verify 脚本 `GROUP=interfaces` | 新增 U34b 断言转绿（详见 §七） |
| V7 | 浏览器 `/aps/overview` + `/aps/gantt`（pmc） | 4 卡片 + 甘特图**不再空白** |

---

## 七、关联 verify 静态 / 动态断言（4 号位 准备做）

`scripts/verify-acceptance.mjs` 已存在 U24（basic 端点可达）+ U50（空 scope 用户）但**未覆盖"用户有 scope、factoryCode 不在 scope 内"路径**。

**4 号位 准备补**一条 U34b 动态断言（需 §六 V1-V5 都通过后落地）：

```js
// 拟新增 — 待 §六 5 号位 修复后 4 号位 落地
r = await call('GET', '/api/overview/active-plan', {
  token: pmcToken,
  query: { factoryCode: 'ZZ' }   // 既不在 pmc.scope，又非法
})
assert(
  'U34b 越界 factoryCode 不再 500',
  r.code === 403 || r.code === 400,
  `code=${r.code}（403=scope 拦截；400=格式校验；任一即过；500=失败）`
)
```

→ 落地后归入 `GROUP=interfaces` 自动跑，覆盖 pmc / viewer / rule_admin 三种视角。

---

## 八、能力边界声明（4 号位 不越界）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md)：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ✅ 4 号位 可提供：F12 截图、curl 命令、§七 断言草稿
- ✅ 4 号位 可做：V6/V7 自验，工厂码 seed-mock 不一致对齐（**待 §五 项 2 澄清后**）

---

## 九、配合时间

| 期望 | 时间 |
|---|---|
| **项 1 修复 500** | **本周内（10-03 前）** — 阻塞排产总览 / Gantt 调试 |
| **项 2 factoryCode 格式澄清** | 09-30 前回执即可（无需代码改动） |
| 4 号位 V6 断言落地 | 项 1 合并后**当日**完成 |

→ 项 1 合并后请回执（无论项 2 是否同步），4 号位 立即跑 V1-V7。

---

## 十、附档

- 前端 scope 校验：[src/store/modules/aps/scope.ts](src/store/modules/aps/scope.ts) §三 L84-99
- 前端调用：[src/api/aps-v1/overview.ts:18-30](src/api/aps-v1/overview.ts#L18-L30)
- 前端 store：[src/store/modules/aps/overview.ts](src/store/modules/aps/overview.ts)
- 既有 verify 段：[scripts/verify-acceptance.mjs:215-228](scripts/verify-acceptance.mjs#L215-L228)（U24）+ [L580-616](scripts/verify-acceptance.mjs#L580-L616)（U50）
- seed 用户：[frontNew/scripts/seed-dev.mjs:46](scripts/seed-dev.mjs#L46)（pmc）+ [L118-127](scripts/seed-dev.mjs#L118-L127)（aps.planner factory 范围 BJ/SUZ）
- 关联先例：[docs/3号位催办单-2026-09-12-GovernanceController-28-GET.md](3号位催办单-2026-09-12-GovernanceController-28-GET.md)（§10A 端点缺口催办格式参考）
- 关联先例：[docs/3号位催办单-2026-09-22-Rules.vue联调阻塞-3项.md](3号位催办单-2026-09-22-Rules.vue联调阻塞-3项.md)

---

**发送人**：4 号位 ｜ **接收人**：5 号位（抄送 3 号位）｜ **日期**：2026-09-28
**回执请求**：§五 项 1 修复时间 + 项 2 factoryCode 真实值澄清。