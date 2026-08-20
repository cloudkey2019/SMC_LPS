using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// PlanVersion 治理仓储接口（3号位，P0-08）
/// 对应表：APS_Production.dbo.PlanVersion（冻结 DDL v5.1.2 §3.2）
/// 边界：仅更新确认/激活列（Status / ActivatedAt / ActivatedBy）；不重写 2号位结果持久化流转。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IPlanVersionRepository
{
    /// <summary>按 Id 读取 PlanVersion（不存在返回 null）</summary>
    System.Threading.Tasks.Task<PlanVersion?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 同 DomainKey 下现有 ACTIVE 版本（应用层对 UQ_PlanVersion_OneActivePerDomain 的预检；exceptPlanVersionId 排除自身）。
    /// 语义：每 Domain 同时只有一个 ACTIVE 版本（V1 每域单一正式采用版本）。
    /// </summary>
    System.Threading.Tasks.Task<PlanVersion?> GetActiveByDomainKeyAsync(string domainKey, int? exceptPlanVersionId = null, CancellationToken ct = default);

    /// <summary>更新 PlanVersion（仅确认/激活：Status / ActivatedAt / ActivatedBy）</summary>
    System.Threading.Tasks.Task UpdateAsync(PlanVersion version, CancellationToken ct = default);

    /// <summary>按 SourceScheduleRunId 取最新 PlanVersion（Run 引用追溯；无则 null）</summary>
    System.Threading.Tasks.Task<PlanVersion?> GetLatestByScheduleRunIdAsync(int scheduleRunId, CancellationToken ct = default);
}
