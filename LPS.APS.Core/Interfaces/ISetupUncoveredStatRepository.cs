using LPS.APS.Core.DTOs.Setup;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// Setup 规则缺失（uncovered-stats）只读聚合查询仓储契约。
/// 数据源 = [dbo].[Task].[SetupSource] = 'SETUP_RULE_MISSING_ZERO_FALLBACK' 的命中频次聚合
/// （0 分钟规则缺失兜底，v1.4 §十一 KPI 监控；runId 经 PlanVersion.SourceScheduleRunId JOIN）。
/// 只读查询，无任何写操作；红线 #6 不涉及（不建表不改 DDL）。3号位 依 G4 只读查询归属自写。
/// </summary>
public interface ISetupUncoveredStatRepository
{
    /// <summary>
    /// 按四级维度（runId → 部门 → 工序 → 设备 → 产品）聚合规则缺失命中分布。
    /// 全部过滤参数可选；未传该级即不筛。返回按维度分组聚合行（含 Code 回带 + 首条示例 TaskId）。
    /// 只读查询；空结果返回空列表（非 null）。
    /// </summary>
    Task<IReadOnlyList<SetupUncoveredStatDto>> GetUncoveredAsync(
        int? runId,
        int? departmentId,
        string? operationCode,
        int? resourceId,
        int? materialId,
        CancellationToken cancellationToken = default);
}