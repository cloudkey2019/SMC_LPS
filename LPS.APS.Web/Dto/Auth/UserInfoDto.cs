namespace LPS.APS.Web.Dto.Auth;

/// <summary>当前用户信息</summary>
public class UserInfoDto
{
    /// <summary>用户ID</summary>
    public int UserId { get; set; }

    /// <summary>用户工号</summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>用户姓名</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>角色列表</summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>功能权限码列表（登录签发时注入 JWT，供前端菜单/按钮渲染）</summary>
    public List<string> Permissions { get; set; } = new();
}
