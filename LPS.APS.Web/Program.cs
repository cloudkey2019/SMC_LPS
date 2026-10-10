using System.Globalization;
using System.Text;
using Hangfire;
using LPS.APS.Application.Extensions;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Extensions;
using LPS.APS.Scheduling.Extensions;
using LPS.APS.Shared.Extensions;
using LPS.APS.Web.Extensions;
using LPS.APS.Web.Filters;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// ==================== Serilog 配置 ====================
// 配置 Serilog 日志系统，支持按日志级别分文件夹存储
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "LPS.APS")
    .Enrich.WithProperty("MachineName", Environment.MachineName)
    .CreateLogger();

try
{
    Log.Information("========================================");
    Log.Information("LPS.APS 应用程序启动中...");  
    Log.Information("========================================");

var builder = WebApplication.CreateBuilder(args);

// 使用 Serilog 替换默认日志
builder.Host.UseSerilog();

// ==================== 服务注册 ====================

// 注册Shared基础服务（日志、缓存、序列化、配置验证）
builder.Services.AddSharedServices(builder.Configuration);

// 注册数据库服务（三库架构：APS本地库 + ODS集成防腐层 + Auth权限库）
builder.Services.AddDatabaseServices(builder.Configuration);
builder.Services.AddDatabaseHealthCheck();

// 注册治理仓储（3号位：RuleSetVersion / ParameterSetVersion，阶段 A-3）
builder.Services.AddGovernanceRepositories();

// 注册排程算法服务（1号位：纯内存计算引擎）
builder.Services.AddSchedulingServices();

// 注册业务规则服务（5号位：业务规则插件 + 原始供应事实；Pegging 的消费与 Quantity-Time 传播归 2号位）
builder.Services.AddBusinessRuleServices();

// 注册应用服务（3号位：治理/生命周期用例编排；Pegging 与排程计算类编排归 2号位）
builder.Services.AddApplicationServices();

// 注册Hangfire定时服务（使用APS库存储Job数据）
builder.Services.AddHangfireServices(builder.Configuration);

// 配置JSON序列化 + 全局异常过滤器
builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
})
.ConfigureApiBehaviorOptions(options =>
{
    // [ApiController] 自动模型校验失败时，统一返回 422 + ApiResponse 信封（前端按 json.code 解析，替代默认 400 ProblemDetails）
    options.InvalidModelStateResponseFactory = context =>
    {
        var firstError = context.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .SelectMany(kv => kv.Value!.Errors.Select(e => e.ErrorMessage))
            .FirstOrDefault() ?? "请求参数校验失败";

        return new UnprocessableEntityObjectResult(
            ApiResponse.Fail(422, firstError));
    };
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// 本地化支持（中文）
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("zh-CN");
    options.SupportedCultures = new[] { new CultureInfo("zh-CN") };
    options.SupportedUICultures = new[] { new CultureInfo("zh-CN") };
});

builder.Services.AddEndpointsApiExplorer();

// Swagger配置（含JWT认证支持）
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LPS.APS 排产系统 API",
        Version = "v1",
        Description = "高级计划与排程系统（APS）"
    });

    // Swagger JWT Bearer 认证配置
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "输入 JWT Token（不需要加 Bearer 前缀）"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddHttpContextAccessor();

// 健康检查（包含数据库健康检查在AddDatabaseHealthCheck中注册）
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("运行正常"));

// JWT 认证
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("缺少 Jwt:SecretKey 配置");

// 安全加固：占位符或长度不足的签名密钥会使 JWT 可被伪造，生产环境拒绝启动
if (jwtSecretKey.Length < 32 || jwtSecretKey.StartsWith("REPLACE_WITH_YOUR_", StringComparison.OrdinalIgnoreCase))
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException("生产环境禁止使用占位符或长度不足的 Jwt:SecretKey，请通过环境变量/User Secrets 配置真实密钥");
    Log.Warning("Jwt:SecretKey 为占位符或长度不足，开发环境已放行；生产环境将拒绝启动");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

// 授权：全局默认要求已认证（安全默认），公共端点以 [AllowAnonymous] 显式放行
// F-G1/F-G2（3号位）：关闭业务端点"全裸奔"缺口；角色级策略待 0号位 裁决 RoleCode 后补充
builder.Services.AddPermissionAuthorization();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // F-G3：注册 V1 全部功能权限点策略（策略名 = PermissionCode）
    options.AddPermissionPolicies();
});

// 跨域（从 appsettings.json 读取配置）
var corsOrigins = builder.Configuration.GetSection("Application:Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000", "http://localhost:8080" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });

    // 开发环境保留全放行策略
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// 响应压缩
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// ==================== M3 登录速率限制（3号位） ====================
// IP 维度滑动窗口 + 未知IP 全局兜底，防暴力破解/用户枚举；
// 阈值从 appsettings（Auth:LoginRateLimit:*）读取，缺省 1 分钟 5 次。
var loginPermitLimit = builder.Configuration.GetValue<int>("Auth:LoginRateLimit:PermitLimit", 5);
var loginWindowSeconds = builder.Configuration.GetValue<int>("Auth:LoginRateLimit:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        var body = System.Text.Json.JsonSerializer.Serialize(
            ApiResponse.Fail(429, "登录尝试过于频繁，请稍后再试"),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        await context.HttpContext.Response.WriteAsync(body, ct);
    };

    options.AddPolicy("login", httpContext =>
    {
        // 优先客户端真实 IP；不可得时(反向代理未透传)统一落到 "unknown" 桶，充当全局限流兜底
        var ip = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromSeconds(loginWindowSeconds),
            QueueLimit = 0,
            SegmentsPerWindow = loginWindowSeconds,
        });
    });
});

var app = builder.Build();

// ==================== 请求管道 ====================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LPS.APS API V1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseResponseCompression();
app.UseCors(app.Environment.IsDevelopment() ? "AllowAll" : "Default");
app.UseHttpsRedirection();

// M3：登录速率限制中间件（须在 UseAuthentication 之前，作用于登录端点）
app.UseRateLimiter();

app.UseAuthentication();

// Hangfire Dashboard
// 开发环境：无鉴权，可直接访问 /hangfire 手动触发任务
// 生产环境：需配合 Authorization Filter 保护
app.UseHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
{
    DashboardTitle = "LPS.APS 定时任务",
    StatsPollingInterval = 2000,
    Authorization = app.Environment.IsDevelopment()
        ? Array.Empty<Hangfire.Dashboard.IDashboardAuthorizationFilter>()
        : new[] { new HangfireAuthorizationFilter() }
});

// ==================== Hangfire 定时任务注册 ====================
app.UseHangfireJobs();

// 健康检查端点
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration
            }),
            totalDuration = report.TotalDuration
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
}).AllowAnonymous();

app.UseAuthorization();
app.MapControllers();

// ==================== 启动种子（F-G5 收尾，3号位） ====================
// 确保代码侧 34 个 V1 功能权限码已落库（幂等；APS_Auth 不可用时仅告警不阻断启动）
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IPermissionSeedService>();
    try
    {
        await seeder.EnsureSeededAsync();
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "权限码播种失败（APS_Auth 可能不可用），不影响启动");
    }
}

app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();

Log.Information("LPS.APS 应用程序启动完成");
app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "LPS.APS 应用程序启动失败");
    throw;
}
finally
{
    Log.Information("LPS.APS 应用程序关闭");
    Log.CloseAndFlush();
}