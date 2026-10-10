namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// 白天候选 BOM 实时展开结果（IBomRealtimeExpandService 返回）。
/// 依据 5号位 回执 §3.3：返回须做三重校验（ExpandedRowCount>0 / Issues 无错误级 / Status=READY），
/// 不能只看 Status='READY'（实时链路「永不阻塞、状态机永远 READY」，0 行也 READY）。
/// </summary>
public class BomRealtimeExpandResult
{
    /// <summary>本次请求展开的订单数</summary>
    public int RequestedOrders { get; init; }

    /// <summary>补写入 MES_API_BOM_Request_Detail 的明细行数（已存在的跳过）</summary>
    public int DetailInserted { get; init; }

    /// <summary>实际调用 sp_ExpandBOMRealtime_vNext 的订单数</summary>
    public int ExpandedOrders { get; init; }

    /// <summary>展开结果行数合计（MES_APS_BOM_Workset_Realtime，按 RequestDetailId 隔离）</summary>
    public int ExpandedRowCount { get; init; }

    /// <summary>问题信息（三重校验未通过的订单，如 0 行展开 / Issues 有错误级）</summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
