namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 用户权限码只读查询接口（Auth 域，3号位 治理）
/// 返回某用户经 UserRole → RolePermission → Permission 关联得到的有效 PermissionCode 去重集合。
/// </summary>
public interface IPermissionCodeRepository
{
    /// <summary>按用户主键查询其有效权限码集合</summary>
    Task<IReadOnlyList<string>> GetPermissionCodesByUserIdAsync(int userId, CancellationToken ct = default);
}
