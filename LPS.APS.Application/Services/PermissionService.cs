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

        var codes = await _repository.GetPermissionCodesByUserIdAsync(userId, ct);
        return codes.Any(c => string.Equals(c, permissionCode, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetPermissionCodesAsync(int userId, CancellationToken ct = default)
        => await _repository.GetPermissionCodesByUserIdAsync(userId, ct);
}
