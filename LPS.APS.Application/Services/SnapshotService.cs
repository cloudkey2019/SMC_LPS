using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Models;
using LPS.APS.Core.Models.Scheduling;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 排程快照封存服务（§2.6 V1 stub — 文件系统实现待补充）
/// </summary>
public class SnapshotService : ISnapshotService
{
    private readonly ILogger<SnapshotService> _logger;

    public SnapshotService(ILogger<SnapshotService> logger)
    {
        _logger = logger;
    }

    public Task<SnapshotInfo> SaveAsync(SchedulingContext context, int planVersionId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[{PlanVersionId}] SnapshotService.SaveAsync: V1 stub，快照未持久化", planVersionId);
        return Task.FromResult(new SnapshotInfo
        {
            FilePath       = string.Empty,
            OriginalSize   = 0,
            CompressedSize = 0,
            FileHash       = new string('0', 64),
            CreatedAt      = DateTime.Now
        });
    }

    public Task<SchedulingContext> LoadAsync(int planVersionId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("SnapshotService.LoadAsync: V1 未实现");

    public Task<bool> VerifyAsync(int planVersionId)
        => Task.FromResult(false);
}
