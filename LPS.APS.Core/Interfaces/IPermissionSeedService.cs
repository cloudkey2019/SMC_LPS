namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 权限码种子服务接口（F-G5 收尾，3号位）
/// 启动时确保 Permission 表包含代码侧 V1 功能权限码（幂等，按 PermissionCode 逐条补齐）。
/// </summary>
public interface IPermissionSeedService
{
    /// <summary>确保代码侧 V1 功能权限码已落库（幂等）。</summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验权限基线对齐（P1-06）：返回代码侧 V1 功能权限码（<see cref="LPS.APS.Core.Authorization.PermissionCodes.All"/>）
    /// 中尚未落库的集合（空集合 = 已对齐）。供 HealthCheck / Startup readiness 将「权限基线未对齐」标记为不可验收。
    /// </summary>
    Task<IReadOnlyCollection<string>> FindMissingPermissionCodesAsync(CancellationToken cancellationToken = default);
}
