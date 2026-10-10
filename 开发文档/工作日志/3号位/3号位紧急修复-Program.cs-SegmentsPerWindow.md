# 3号位 紧急修复 — Program.cs RateLimiter 启动崩溃

**接收人**：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
**来源**：4号位（前端页面与业务操作）
**生成日期**：2026-09-10
**严重度**：🚨 **P0 阻塞** — 后端任何请求都 500

---

## 一、现象

后端启动 OK（Swagger 可在 https://localhost:7044/swagger/index.html 看到），但**任何请求**（包括 `POST /api/auth/login`）一律返回 500，错误栈：

```
System.ArgumentException: SegmentsPerWindow must be set to a value greater than 0. (Parameter 'options')
   at System.Threading.RateLimiting.SlidingWindowRateLimiter..ctor(SlidingWindowRateLimiterOptions options)
   at System.Threading.RateLimiting.RateLimitPartition.<>c__DisplayClass4_0`1.<GetSlidingWindowLimiter>b__0(TKey key)
   at System.Threading.RateLimiting.DefaultPartitionedRateLimiter`2.<>c__DisplayClass18_0.<GetRateLimiter>b__0()
```

curl 复现（即便用 Admin@123456 也失败）：
```bash
curl -sk -X POST https://localhost:7044/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"userCode":"admin","password":"Admin@123456"}'
# → 500，above stack trace
```

## 二、根因（已定位）

`lps/LPS.APS.Web/Program.cs:245-251`，M3 登录速率限制构造 `SlidingWindowRateLimiterOptions` 时**漏了必填字段 `SegmentsPerWindow`**。

```csharp
return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
{
    PermitLimit = loginPermitLimit,
    Window = TimeSpan.FromSeconds(loginWindowSeconds),
    QueueLimit = 0,  // ← 缺 SegmentsPerWindow（运行时抛 ArgumentException）
});
```

.NET 8 `SlidingWindowRateLimiterOptions` 的**三个必填字段**是 `PermitLimit` / `Window` / `SegmentsPerWindow`。`SlidingWindow` 滑动窗口算法要求把窗口切成 N 个等长 segment，N 就是 `SegmentsPerWindow`。

## 三、修复（1 行）

`Program.cs:250`（`QueueLimit = 0,` 行）后新增一行：

```csharp
return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
{
    PermitLimit = loginPermitLimit,
    Window = TimeSpan.FromSeconds(loginWindowSeconds),
    QueueLimit = 0,
    SegmentsPerWindow = loginWindowSeconds,  // ← 加这一行
});
```

`SegmentsPerWindow` 取值建议：`loginWindowSeconds`（每秒一个 segment，最细粒度）或固定 6（10 秒窗口切成 6 段）。本次用前者。

## 四、修复后请验证

```bash
# 1) Ctrl+C 杀掉 dotnet run
# 2) 重启
cd lps
dotnet run --project LPS.APS.Web
# 3) curl 自检
curl -sk -X POST http://localhost:5163/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"userCode":"admin","password":"Admin@123456"}' | head -c 400
```

期望返回 200，含 `accessToken` / `refreshToken` / `expiresAt` / `userId` / `userCode:"admin"` / `roles:["aps.admin.system"]` / 34 个 permissions。

## 五、影响范围

- **修复前**：4号位 无法 curl 验证 → 无法 seed:dev → 无法 Phase D 联调
- **修复后**：4号位 Phase D-1 代码已就绪（[frontNew/src/api/aps-v1/http.ts](../frontNew/src/api/aps-v1/http.ts) 独立 axios 实例 + 401 自动 refresh + [frontNew/src/api/login/index.ts](../frontNew/src/api/login/index.ts) loginApi 桥接 + [frontNew/.env.dev](../frontNew/.env.dev) 新增 `VITE_APS_API_BASE_URL=http://localhost:5163`），**改 .env.dev 一行 `VITE_USE_MOCK=false` 即可联通**

## 六、签署

- 4号位：Phase D-1 代码三绿（ts:check / lint / build），阻塞仅剩后端 1 行漏字段
- 3号位：修 Program.cs:250 → 重启 → 通知 4号位
- 修通后 4号位 即跑 curl 联调 + 写 seed:dev.mjs