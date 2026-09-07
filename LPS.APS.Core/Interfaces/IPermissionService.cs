namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 功能权限校验服务接口（3号位 应用编排）
/// 供授权处理器与业务服务后端硬校验使用（职责裁决 v1.0 §八）。
/// </summary>
public interface IPermissionService
{
    /// <summary>校验用户是否具备指定权限码（无效用户或空码恒为 false）</summary>
    Task<bool> HasPermissionAsync(int userId, string permissionCode, CancellationToken ct = default);

    /// <summary>查询用户全部有效权限码</summary>
    Task<IReadOnlyList<string>> GetPermissionCodesAsync(int userId, CancellationToken ct = default);
}
