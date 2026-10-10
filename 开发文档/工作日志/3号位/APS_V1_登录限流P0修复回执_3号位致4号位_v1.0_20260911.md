# 回执：Program.cs 登录限流 P0 修复（SegmentsPerWindow）

> 收件：4号位（前端页面与业务操作）
> 发件：3号位（认证 / 用户 / 角色 / 权限 / Domain 治理）
> 依据：派单《3号位紧急修复-Program.cs-SegmentsPerWindow.md》（生成 2026-09-10，来源 4号位）
> 日期：2026-09-11

---

## 一、结论一句话

P0 阻塞已修复。后端登录速率限制用的滑动窗口限流器（SlidingWindowRateLimiter）漏填必填字段「每窗口分段数（SegmentsPerWindow）」——已补一行 `SegmentsPerWindow = loginWindowSeconds`，启动后任何请求一律 500 的现象已消除；登录接口（POST /api/auth/login）实测 200 + 访问令牌（accessToken）+ 34 权限码。4号位 改 `.env.dev` 一行即可联调。

---

## 二、回执表（对应派单 §二 根因 / §三 修复）

| 项 | 派单要求 | 回执 |
|---|---|---|
| 根因 | 滑动窗口限流选项（SlidingWindowRateLimiterOptions）缺必填字段 SegmentsPerWindow，运行时抛 ArgumentException | ☑ 确认，已补 |
| 修复 | `QueueLimit = 0,` 后新增 `SegmentsPerWindow = loginWindowSeconds`（每秒一段，最细粒度） | ☑ 已按派单落 1 行，取值 `loginWindowSeconds`（默认 60） |
| 验证 | curl 登录期望 200，含 accessToken / refreshToken / expiresAt / userId / userCode:"admin" / roles / 34 permissions | ☑ 实测通过（见 §三、§五） |

---

## 三、3号位 已完成（2026-09-11，编译 0 错误 + 运行时 curl 自检通过）

1. **代码修复**：`LPS.APS.Web/Program.cs`（登录速率限制 M3 段）补必填字段：
   ```csharp
   return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
   {
       PermitLimit = loginPermitLimit,
       Window = TimeSpan.FromSeconds(loginWindowSeconds),
       QueueLimit = 0,
       SegmentsPerWindow = loginWindowSeconds,   // ← 补这一行
   });
   ```
2. **编译验证**：`dotnet build LPS.APS.Web` → **0 错误**（告警为既有）。
3. **运行时自检**（`dotnet run --project LPS.APS.Web --launch-profile http`）：
   - `/health` → HTTP 200，三个数据库（认证库 Auth / 生产库 APS_Production / 集成库 ODS）+ 权限基线（permission-baseline）全部 Healthy。
   - `POST /api/auth/login`（admin / Admin@123456）→ **HTTP 200，`code:200`「登录成功」**，含 `accessToken`、`roles:["aps.admin.system"]`、`permissionCode`（34 码）。

---

## 四、请 4号位 下一步（联调）

按派单 §五/§六：

1. 改 `frontNew/.env.dev` 一行：`VITE_USE_MOCK=false`（其余 `VITE_APS_API_BASE_URL=http://localhost:5163` 已就绪）。
2. 自检命令（与派单 §四 一致）：

```bash
curl -sk -X POST http://localhost:5163/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"userCode":"admin","password":"Admin@123456"}'
```

期望返回 `code:200`「登录成功」+ `accessToken` + `refreshToken` + `expiresAt` + `userId` + `userCode:"admin"` + `roles:["aps.admin.system"]` + 34 个 `permissionCode`。

---

## 五、验证证据

- `dotnet build LPS.APS.Web` 0 错误，`dotnet run` 启动无异常。
- `/health` 全绿：`database-auth` / `database-aps` / `database-ods` / `permission-baseline` 均 Healthy。
- 登录实测响应片段（已脱敏截取）：

```json
{"code":200,"message":"登录成功","data":{
  "accessToken":"eyJhbGciOi…（截断）",
  "roles":["aps.admin.system"],
  "permissionCode":["aps.audit.view","aps.auth.permission.assign","aps.auth.role.edit",…（34 码）]
}}
```