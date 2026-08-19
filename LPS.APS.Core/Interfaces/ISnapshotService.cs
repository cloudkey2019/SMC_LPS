using LPS.APS.Core.Models;
using LPS.APS.Core.Models.Scheduling;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 排程快照封存服务接口（2号位职责 — §2.6 历史追溯的终极解法）
///
/// 时序：每日02:15（排程完成后自动触发，非独立Hangfire任务）
///
/// 功能：
///   - SaveAsync:   将 SchedulingContext 序列化为 .json.gz，计算 SHA256，更新 PlanVersion 快照元数据
///   - LoadAsync:   按 PlanVersionId 读取快照，校验 SHA256，反序列化为 SchedulingContext
///   - VerifyAsync: 校验快照文件完整性（SHA256）
///
/// 存储路径：由 appsettings.json 的 Snapshot:StoragePath 配置（默认 D:\APS_Snapshots）
/// </summary>
public interface ISnapshotService
{
    Task<SnapshotInfo> SaveAsync(SchedulingContext context, int planVersionId, CancellationToken cancellationToken = default);

    Task<SchedulingContext> LoadAsync(int planVersionId, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(int planVersionId);
}
