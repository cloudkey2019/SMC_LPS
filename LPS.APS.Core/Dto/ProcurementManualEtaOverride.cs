namespace LPS.APS.Core.Dto;

/// <summary>
/// 采购人工ETA覆盖事实（从ProcurementManualEtaOverride表装载）
///
/// 【2026-08-26新基线职责边界】
/// - 5号位职责：提供ProcurementManualEtaOverride表和Repository接口，供4号位维护
/// - 2号位职责：读取Manual ETA并计算Effective ETA (ManualEta ?? ErpEta ?? ReleaseDate+DefaultLT)
/// - 2号位职责：计算AvailableTime (Effective ETA + ArrivalToUsableOffset)
/// - 取消时IsActive=0，2号位回退到ERP ETA或ReleaseDate+DefaultLT
///
/// 参考：APS_V1_5号位新基线增量整改开发包_v1.0_20260825.md
/// 参考：复审报告P1-02
/// </summary>
public sealed class ProcurementManualEtaOverride
{
    /// <summary>
    /// 采购订单号
    /// </summary>
    public string PONo { get; init; } = string.Empty;

    /// <summary>
    /// 行号
    /// </summary>
    public int LineNo { get; init; }

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 接收仓库
    /// </summary>
    public string ReceivingWarehouse { get; init; } = string.Empty;

    /// <summary>
    /// 人工设定的到货预期时间
    /// </summary>
    public DateTime ManualEta { get; init; }

    /// <summary>
    /// 是否生效（0=取消，2号位回退ERP ETA）
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 最后更新人
    /// </summary>
    public string UpdatedBy { get; init; } = string.Empty;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// 创建人
    /// </summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; init; } = string.Empty;
}
