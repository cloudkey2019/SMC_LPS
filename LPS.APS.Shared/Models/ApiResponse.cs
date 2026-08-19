namespace LPS.APS.Shared.Models;

/// <summary>
/// 统一 API 响应格式
/// 所有 Controller 返回值均使用此包装，前端只需解析一种结构
/// 
/// 成功示例：{ "code": 200, "message": "success", "data": {...}, "timestamp": "..." }
/// 失败示例：{ "code": 400, "message": "用户名或密码错误", "data": null, "timestamp": "..." }
/// </summary>
public class ApiResponse<T>
{
    /// <summary>业务状态码（200=成功，400=参数错误，401=未认证，403=无权限，500=服务器错误）</summary>
    public int Code { get; set; }

    /// <summary>提示信息</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>业务数据</summary>
    public T? Data { get; set; }

    /// <summary>请求追踪ID（用于日志关联）</summary>
    public string? TraceId { get; set; }

    /// <summary>响应时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>成功响应</summary>
    public static ApiResponse<T> Success(T? data, string message = "success")
        => new() { Code = 200, Message = message, Data = data };

    /// <summary>失败响应</summary>
    public static ApiResponse<T> Fail(int code, string message)
        => new() { Code = code, Message = message };
}

/// <summary>
/// 无数据的统一响应（用于删除/更新等无返回值场景）
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    /// <summary>成功响应（无数据）</summary>
    public static ApiResponse Ok(string message = "success")
        => new() { Code = 200, Message = message };

    /// <summary>失败响应</summary>
    public new static ApiResponse Fail(int code, string message)
        => new() { Code = code, Message = message };
}
