using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LPS.APS.Web.Filters;

/// <summary>
/// 全局异常过滤器
/// 捕获所有 Controller 未处理的异常，统一包装为 ApiResponse 格式返回
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception,
            "未处理异常: {Method} {Path}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);

        var (code, message) = context.Exception switch
        {
            UnauthorizedAccessException => (401, "未授权访问"),
            KeyNotFoundException => (404, "资源不存在"),
            ArgumentException ex => (400, ex.Message),
            InvalidOperationException ex => (400, ex.Message),
            _ => (500, "服务器内部错误")
        };

        var response = ApiResponse.Fail(code, message);
        response.TraceId = context.HttpContext.TraceIdentifier;

        // 开发环境附加异常详情
        if (_env.IsDevelopment())
        {
            response.Data = new
            {
                exception = context.Exception.GetType().Name,
                detail = context.Exception.Message,
                stackTrace = context.Exception.StackTrace
            };
        }

        context.Result = new ObjectResult(response) { StatusCode = code };
        context.ExceptionHandled = true;
    }
}
