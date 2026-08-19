using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 资源主数据同步服务（2号位职责 — 每日 00:15）
///
/// 契约链路（v5.0.16 双字典映射）：
///   MES 设备主数据源表
///     → ODS 契约视图（3号位创建）
///         - MES_APS_Resource_View（ResourceCode+FactoryCode+ProductionDeptCode）
///     → APS 跨库包装视图（2号位创建，ext_ 前缀）
///     → APS 本地表 Resource（MERGE Upsert）
///
/// 架构红线：
///   ✅ 通过 Factory(Code) 映射 FactoryCode → FactoryId
///   ✅ 通过 ProductionDepartment(DeptCode, IsActive=1) 映射 ProductionDeptCode → ProductionDepartmentId
///   ✅ 映射失败行不阻塞批次，登记 APS_ETL_Log 告警并跳过
///   ⚠️ v1占位策略：源端没有的旧资源暂不自动停用
/// </summary>
public interface IResourceSyncService
{
    /// <summary>
    /// 同步 MES 资源主数据
    /// 调用 sp_SyncResourceData(@SourceType='MES') 在 APS 库本地完成 MERGE
    /// </summary>
    Task<ResourceSyncResultDto> SyncAsync(CancellationToken cancellationToken = default);
}
