using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 主数据双源三表协同同步服务接口（2号位职责 — §2.4.3）
/// 
/// 数据路径（双源同构契约）：
///   ERP: ext_ERP_Master_View → sp_SyncMasterData('ERP') → Material + MaterialMapping + MaterialSupplyContext
///   MES: ext_MES_Material_View → sp_SyncMasterData('MES') → Material + MaterialMapping + MaterialSupplyContext
/// 
/// SCD Type 2 逻辑：
///   - MaterialMapping: SourceID 变更时关闭旧版本、开新版本
///   - MaterialSupplyContext: 供给属性变更时关闭旧版本、开新版本
///   - Material: 增量Upsert，ID稳定
/// </summary>
public interface IMasterDataSyncService
{
    /// <summary>
    /// 同步ERP主数据（生产：每日00:10）
    /// 调用 sp_SyncMasterData(@SourceType='ERP')
    /// </summary>
    Task<MasterDataSyncResultDto> SyncERPMasterDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步MES主数据（生产：每日00:20）
    /// 调用 sp_SyncMasterData(@SourceType='MES')
    /// </summary>
    Task<MasterDataSyncResultDto> SyncMESMasterDataAsync(CancellationToken cancellationToken = default);
}
