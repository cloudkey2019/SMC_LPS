using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 功能权限校验服务（F-G3，3号位 应用编排）
/// 依赖 Auth 域只读查询，返回用户是否具备指定权限码。
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly IPermissionCodeRepository _repository;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(IPermissionCodeRepository repository, ILogger<PermissionService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(int userId, string permissionCode, CancellationToken ct = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        // M1：按需查码——直接经仓储做单码 EXISTS 判定，避免拉全量后在内存比对；
        // 仍保持「每次请求命中 DB 权威、撤销即时生效」，不引入缓存导致的权限残留。
        return await _repository.HasPermissionAsync(userId, permissionCode, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetPermissionCodesAsync(int userId, CancellationToken ct = default)
        => await _repository.GetPermissionCodesByUserIdAsync(userId, ct);
}
