using Dapper;
using LPS.APS.Core.Exceptions;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 决定「本次排程该读哪一批**原始 MES 快照**（<c>ScheduleRun</c> 分区）」。
///
/// 依据 0号位《PM回复1009-1》§三 A-Q1 / A-Q2 / A-Q3：
/// <list type="bullet">
/// <item>夜间 FULL：读本 Run 自身的完整三类快照（<c>工单 / 工序 / Stage</c>）。</item>
/// <item>白天 Candidate / FAILED 恢复：**继承 Base 链上那批完整快照**——不得读「Run 号相同的快照」、
/// 不得读「最新同步的快照」，也不得用 <c>MAX(RunId)</c> / 最新 <c>RUNNING</c> / 静默 <c>COALESCE</c> 猜来源。</item>
/// <item>追溯路径：本 PlanVersion 所属 Run → <c>ScheduleRun.BasePlanVersionId</c> → Base PlanVersion →
/// 该 PlanVersion 所属 Run → 逐跳上溯，直到命中「有三类完整快照」的源 Run。</item>
/// <item>断链 / 成环 / Base 缺失 ⇒ 抛 <see cref="SnapshotRunResolutionException"/>（fail-closed）。</item>
/// </list>
///
/// <para>
/// <b>⚠️ 读 / 写分离（A-Q2 四类表）—— 本解析结果<b>只</b>用于「读原始 MESWorkOrder / Operation / Stage 快照」。</b>
/// 候选自身的运行身份 / 账本 / Task / Explanation 归属、以及本次重算的候选衍生结果
/// （如 <c>ProductionInstructionPositionSnapshot</c>）必须落**候选自己的 Run**，绝不能用本解析结果替换——
/// A-Q2 明令「不能把 <c>PeggingOrchestrator</c> 里所有使用 <c>SourceScheduleRunId</c> 的读取/写入位置统一替换为快照源 Run 号」。
/// </para>
///
/// <para>
/// <b>⚠️ 完整性证据的最小缺口（待 0号位 裁定证据源）：</b>A-Q3 要求「三类装载成功证据齐备、同源 Run 与一致业务截止；
/// 不允许仅通过行数&gt;0 证明完整」，并优先复用 <c>APS_ETL_Log</c>。但实库 <c>APS_ETL_Log</c> **无 RunId 列**、
/// <c>BatchNo</c> 亦不落到 <c>ScheduleRun</c>（已核 2026-10-10）⇒ 无法按 Run 关联同步状态。当前实现退而取
/// 「三表在该 Run 分区下行存在 + 三表 <c>DataCutoffTime</c> 唯一且一致」作为可用证据；严格按 A-Q3 所需的
/// 「完整同步状态」证据源已挂账，待 0号位 批准后再补。
/// </para>
/// </summary>
internal static class SnapshotRunResolver
{
    internal readonly record struct PlanVersionNode(int Id, int? SourceScheduleRunId);

    internal readonly record struct ScheduleRunNode(int Id, int? BasePlanVersionId);

    /// <summary>
    /// DB 封装：读全部 <c>PlanVersion</c> / <c>ScheduleRun</c> 元数据（两表皆为小字典）后走纯逻辑
    /// <see cref="ResolveCoreAsync"/>。仅供 2号位 的原始 MES 快照读点调用。
    /// </summary>
    internal static async Task<int> ResolveSnapshotRunIdAsync(
        DatabaseConnectionManager connectionManager,
        int planVersionId,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var pvRows = await connectionManager.QueryAsync<PvRow>(
            "SELECT Id, SourceScheduleRunId FROM PlanVersion", null, db: DatabaseId.APS);
        var runRows = await connectionManager.QueryAsync<RunRow>(
            "SELECT Id, BasePlanVersionId FROM ScheduleRun", null, db: DatabaseId.APS);

        var planVersions = pvRows.ToDictionary(r => r.Id, r => new PlanVersionNode(r.Id, r.SourceScheduleRunId));
        var runs = runRows.ToDictionary(r => r.Id, r => new ScheduleRunNode(r.Id, r.BasePlanVersionId));

        // 链上同一 Run 可能被重复问及 ⇒ 缓存，避免重复查三表。
        var completeCache = new Dictionary<int, bool>();
        async Task<bool> CompleteAsync(int runId)
        {
            if (completeCache.TryGetValue(runId, out var cached)) return cached;
            var v = await HasCompleteSnapshotBatchAsync(connectionManager, runId);
            completeCache[runId] = v;
            return v;
        }

        var resolved = await ResolveCoreAsync(planVersionId, planVersions, runs, CompleteAsync);
        logger?.LogInformation(
            "[Pegging] 原始 MES 快照源 Run 解析：请求 PlanVersionId={PlanVersionId} ⇒ 读快照 Run={SnapshotRunId}",
            planVersionId, resolved);
        return resolved;
    }

    /// <summary>
    /// 纯逻辑（无 DB，可单测）。给定 PV / Run 元数据与「某 Run 是否具备三类完整快照」的判定委托，
    /// 返回应当读取的源快照 RunId；断链 / 成环 / Base 缺失时抛 <see cref="SnapshotRunResolutionException"/>。
    /// </summary>
    internal static async Task<int> ResolveCoreAsync(
        int planVersionId,
        IReadOnlyDictionary<int, PlanVersionNode> planVersions,
        IReadOnlyDictionary<int, ScheduleRunNode> runs,
        Func<int, Task<bool>> hasCompleteSnapshotBatchAsync)
    {
        if (!planVersions.TryGetValue(planVersionId, out var pv))
            throw new SnapshotRunResolutionException($"PlanVersionId={planVersionId} 不存在，无法确定快照源 Run");

        if (pv.SourceScheduleRunId is not int ownRunId || ownRunId <= 0)
            throw new SnapshotRunResolutionException($"PlanVersionId={planVersionId} 无 SourceScheduleRunId，无法确定快照源 Run");

        if (!runs.TryGetValue(ownRunId, out var ownRun))
            throw new SnapshotRunResolutionException($"ScheduleRunId={ownRunId} 不存在（PlanVersionId={planVersionId}）");

        // 无 Base ⇒ 本 PV 所属 Run 即原始来源（夜间 FULL / 历史条目）。行为与旧单跳读取逐字一致。
        if (ownRun.BasePlanVersionId is not int basePvId || basePvId <= 0)
            return ownRunId;

        // 有 Base ⇒ 候选 / FAILED 恢复：不得读候选自身 Run，须沿 Base 链找到「有三类完整快照」的源 Run。
        var visited = new HashSet<int> { planVersionId };
        var trail = new List<string> { $"PV{planVersionId}(Run{ownRunId})" };
        var cursorPv = basePvId;
        while (true)
        {
            if (!visited.Add(cursorPv))
                throw new SnapshotRunResolutionException($"Base 链成环：{string.Join(" → ", trail)} → PV{cursorPv}");

            if (!planVersions.TryGetValue(cursorPv, out var node))
                throw new SnapshotRunResolutionException($"Base 链上 PlanVersionId={cursorPv} 不存在：{string.Join(" → ", trail)}");

            if (node.SourceScheduleRunId is not int runId || runId <= 0)
                throw new SnapshotRunResolutionException($"Base 链上 PlanVersionId={cursorPv} 无 SourceScheduleRunId：{string.Join(" → ", trail)}");

            if (!runs.TryGetValue(runId, out var run))
                throw new SnapshotRunResolutionException($"Base 链上 ScheduleRunId={runId} 不存在：{string.Join(" → ", trail)}");

            trail.Add($"PV{cursorPv}(Run{runId})");

            if (await hasCompleteSnapshotBatchAsync(runId))
                return runId;

            if (run.BasePlanVersionId is not int nextBase || nextBase <= 0)
                throw new SnapshotRunResolutionException(
                    $"Base 链未命中完整三类 MES 快照且无更上层 Base ⇒ fail-closed：{string.Join(" → ", trail)}");

            cursorPv = nextBase;
        }
    }

    /// <summary>
    /// 某 Run 是否具备「完整三类 MES 快照」：
    /// 三表在该 Run 分区下均有行，且三表 <c>DataCutoffTime</c> 全局唯一（= 同源 Run + 一致业务截止）。
    /// 证据限度见类型级注释（A-Q3 的「完整同步状态」证据源待 0号位 裁定）。
    /// </summary>
    private static async Task<bool> HasCompleteSnapshotBatchAsync(
        DatabaseConnectionManager connectionManager, int runId)
    {
        var row = await connectionManager.QueryFirstOrDefaultAsync<SnapshotCompletenessRow>(
            @"SELECT
                  (SELECT COUNT(*) FROM MESWorkOrderSnapshot      WHERE ScheduleRunId=@RunId) AS WoRows,
                  (SELECT COUNT(*) FROM OperationProgressSnapshot WHERE ScheduleRunId=@RunId) AS OpRows,
                  (SELECT COUNT(*) FROM StageProgressSnapshot     WHERE ScheduleRunId=@RunId) AS StRows,
                  (SELECT COUNT(DISTINCT DataCutoffTime) FROM (
                      SELECT DataCutoffTime FROM MESWorkOrderSnapshot      WHERE ScheduleRunId=@RunId
                      UNION SELECT DataCutoffTime FROM OperationProgressSnapshot WHERE ScheduleRunId=@RunId
                      UNION SELECT DataCutoffTime FROM StageProgressSnapshot     WHERE ScheduleRunId=@RunId
                  ) cutoffs) AS DistinctCutoffs",
            new { RunId = runId },
            db: DatabaseId.APS);

        return row is { WoRows: > 0, OpRows: > 0, StRows: > 0, DistinctCutoffs: 1 };
    }

    private sealed class PvRow
    {
        public int Id { get; set; }
        public int? SourceScheduleRunId { get; set; }
    }

    private sealed class RunRow
    {
        public int Id { get; set; }
        public int? BasePlanVersionId { get; set; }
    }

    private sealed class SnapshotCompletenessRow
    {
        public int WoRows { get; set; }
        public int OpRows { get; set; }
        public int StRows { get; set; }
        public int DistinctCutoffs { get; set; }
    }
}
