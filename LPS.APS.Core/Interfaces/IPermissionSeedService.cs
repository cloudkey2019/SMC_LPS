namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 权限码种子服务接口（F-G5 收尾，3号位）
/// 启动时确保 Permission 表包含代码侧 V1 功能权限码（幂等，按 PermissionCode 逐条补齐）。
/// </summary>
public interface IPermissionSeedService
{
    /// <summary>确保代码侧 V1 功能权限码已落库（幂等）。</summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);
}
