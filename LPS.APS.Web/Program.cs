using System.Globalization;
using System.Text;
using Hangfire;
using LPS.APS.Application.Extensions;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Engine.Extensions;
using LPS.APS.Scheduling.Extensions;
using LPS.APS.Shared.Extensions;
using LPS.APS.Web.Extensions;
using LPS.APS.Web.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// 注册业务规则服务（5号位：Pegging、LotSizing、Priority 等）
builder.Services.AddBusinessRuleServices();

// 注册应用服务（3号位：用例编排）
builder.Services.AddApplicationServices();

// 注册Hangfire定时服务（使用APS库存储Job数据）
builder.Services.AddHangfireServices(builder.Configuration);

// 注册 IMonitoringApi（供 DomainLayerCoordinatorJob 轮询域 Job 状态）
builder.Services.AddSingleton<Hangfire.Storage.IMonitoringApi>(
    _ => JobStorage.Current.GetMonitoringApi());

// 配置JSON序列化 + 全局异常过滤器
builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
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
});

app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger"));

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