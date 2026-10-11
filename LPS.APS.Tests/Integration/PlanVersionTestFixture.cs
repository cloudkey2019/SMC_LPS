using System.Linq;
using System.Threading.Tasks;
using LPS.APS.Engine.Data;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 集成测试的 PlanVersion / StrategyProfileVersion 夹具解析（**不硬编码 Id**）。
/// </summary>
/// <remarks>
/// <para>
/// 背景：09-29 的 PlanVersion 清理把 <c>328</c> 等版本清掉后，写死 <c>PlanVersionId = 328</c>
/// 的测试全部失效（328 在 <c>[Order]</c> 里 0 行，见 <c>RealSchedulingIntegrationTest</c> 注释）。
/// 10-08 已按「动态解析」修过一批（<c>BOMIntakeIntegrationTest.ResolvePlanVersionIdsAsync</c>），
/// 但 <c>ContinuityRedLineIntegrationTest</c> / <c>BomBfsMainchainProbeTest</c> /
/// <c>BomDualRunIntegrationTest</c> 三只漏改。此处把口径收敛到一处共用，避免再次腐化。
/// </para>
/// <para>
/// <b>解析不到 = 环境态，不是缺陷</b> ⇒ 调用方必须 <c>Skip</c>，不得断言失败
/// （否则没有对应数据的日子会恒红，与 <c>BOMIntakeIntegrationTest</c> 同一教训）。
/// </para>
/// </remarks>
internal static class PlanVersionTestFixture
{
    /// <summary>
    /// 订单最多的 PlanVersion —— 供 BFS 整域 / 双跑对照使用（两者都要足量真实订单，不依赖 MES 快照）。
    /// </summary>
    internal static async Task<int?> ResolveMostPopulatedAsync(DatabaseConnectionManager conn)
    {
        var ids = (await conn.QueryAsync<int>(
            @"SELECT TOP 1 pv.Id
              FROM PlanVersion pv
              WHERE EXISTS (SELECT 1 FROM [Order] o WHERE o.PlanVersionId = pv.Id)
              ORDER BY (SELECT COUNT(*) FROM [Order] o WHERE o.PlanVersionId = pv.Id) DESC, pv.Id DESC",
            db: DatabaseId.APS)).ToList();
        return ids.Count == 0 ? null : ids[0];
    }

    /// <summary>
    /// 源 Run 上**三张 MES 快照齐全**、且订单最多的 PlanVersion —— 供连续性红线使用。
    /// 连续性份额来自 MES 在制事实，源 Run 无快照分区则 Continuation 侧恒空、测不出东西
    /// （读侧 <c>SnapshotRunResolver</c> 正按 <c>ScheduleRunId</c> 分区取快照）。
    /// </summary>
    internal static async Task<int?> ResolveWithMesSnapshotAsync(DatabaseConnectionManager conn)
    {
        var ids = (await conn.QueryAsync<int>(
            @"SELECT TOP 1 pv.Id
              FROM PlanVersion pv
              WHERE pv.SourceScheduleRunId IS NOT NULL
                AND EXISTS (SELECT 1 FROM [Order] o WHERE o.PlanVersionId = pv.Id)
                AND EXISTS (SELECT 1 FROM MESWorkOrderSnapshot      s WHERE s.ScheduleRunId = pv.SourceScheduleRunId)
                AND EXISTS (SELECT 1 FROM OperationProgressSnapshot s WHERE s.ScheduleRunId = pv.SourceScheduleRunId)
                AND EXISTS (SELECT 1 FROM StageProgressSnapshot     s WHERE s.ScheduleRunId = pv.SourceScheduleRunId)
              ORDER BY (SELECT COUNT(*) FROM [Order] o WHERE o.PlanVersionId = pv.Id) DESC, pv.Id DESC",
            db: DatabaseId.APS)).ToList();
        return ids.Count == 0 ? null : ids[0];
    }

    /// <summary>
    /// 指定策略版本是否仍为 <c>PUBLISHED</c> —— 与 PlanVersionId 同源的 Id 腐烂（如 <c>SP-DEMO-V2.0</c>=251
    /// 被下架/删除），同批防呆。
    /// </summary>
    internal static async Task<bool> IsPublishedStrategyProfileVersionAsync(DatabaseConnectionManager conn, long id)
        => await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM StrategyProfileVersion WHERE Id=@id AND Status='PUBLISHED'",
            new { id }, db: DatabaseId.APS) > 0;
}
