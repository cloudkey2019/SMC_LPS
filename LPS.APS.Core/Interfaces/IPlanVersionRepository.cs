using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// PlanVersion 治理仓储接口（3号位，P0-08 / 二轮复审 P0-06）
/// 对应表：APS_Production.dbo.PlanVersion（冻结 DDL v5.1.2 §3.2）
/// 边界：仅更新确认/激活列（Status / ActivatedAt / ActivatedBy / ArchivedAt）；不重写 2号位结果持久化流转。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IPlanVersionRepository
{
    /// <summary>按 Id 读取 PlanVersion（不存在返回 null）</summary>
    System.Threading.Tasks.Task<PlanVersion?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 更新 PlanVersion（仅确认/激活：Status / ActivatedAt / ActivatedBy）。
    /// 二轮复审 P0-05 后：确认不再走本方法（确认仅记审计）；本方法保留供激活单版本状态更新/其他治理路径使用。
    /// </summary>
    System.Threading.Tasks.Task UpdateAsync(PlanVersion version, CancellationToken ct = default);

    /// <summary>
    /// 原子替换（二轮复审 P0-06）：单事务内将同 DomainKey 下既有 ACTIVE 归档（Status→ARCHIVED + ArchivedAt），
    /// 再将 candidate 置 ACTIVE（Status→ACTIVE + ActivatedAt/ActivatedBy）。
    /// 语义：保留 UQ_PlanVersion_OneActivePerDomain 红线，采用动作原子化，不做"先拒绝再手工处理旧 ACTIVE"。
    /// </summary>
    System.Threading.Tasks.Task ReplaceActiveAsync(
        PlanVersion candidate,
        string actor,
        DateTime activatedAt,
        CancellationToken ct = default);

    /// <summary>按 SourceScheduleRunId 取最新 PlanVersion（Run 引用追溯；无则 null）</summary>
    System.Threading.Tasks.Task<PlanVersion?> GetLatestByScheduleRunIdAsync(int scheduleRunId, CancellationToken ct = default);

    /// <summary>按 DomainKey 取当前 ACTIVE 计划版本（B-1：白天 Candidate 解析 Base；无则 null）</summary>
    System.Threading.Tasks.Task<PlanVersion?> GetActiveByDomainKeyAsync(string domainKey, CancellationToken ct = default);

    /// <summary>按 SourceScheduleRunId 取全部 PlanVersion（G8：Run 域级状态汇总；按 DomainKey 分组倒序）</summary>
    System.Threading.Tasks.Task<IReadOnlyList<PlanVersion>> GetByScheduleRunIdAsync(int scheduleRunId, CancellationToken ct = default);
}
