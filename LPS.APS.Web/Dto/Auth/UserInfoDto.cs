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
}
