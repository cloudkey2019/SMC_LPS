using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 工艺路线同步服务（2号位职责 — 每日 00:25）
///
/// 契约链路（v5.0.1 MES_ID 映射，2026-04-02）：
///   MES 工艺源表
///     → ODS 契约视图（3号位创建）
///         - MES_APS_Routing_Operation_View
///         - MES_APS_Routing_Dependency_View
///         - MES_APS_Routing_Stage_View
///         - APS_OperationResourceEligibility_View
///     → APS 跨库包装视图（2号位创建，ext_ 前缀）
///     → APS 本地表（增量 Upsert + 软删除）
///         - RoutingOperation（工序节点）
///         - RoutingDependency（工序依赖图）
///         - RoutingStage（阶段字典，v1.7 新增，v1.8 定位为字典）
///         - OperationResourceEligibility（工序资源能力矩阵，v5.0 新增）
///
/// 架构红线：
///   ❌ 禁止每天全量删除重建（保持 Id 稳定，Task 引用 MaterialId+OperationCode 查询）
///   ✅ 通过 MaterialMapping(Source='MES', IsCurrent=1) 映射 MES_ID → MaterialId
///   ✅ 通过 ProductionDepartment(DeptCode, IsActive=1) 映射 ProductionDeptCode → ProductionDepartmentId
///   ✅ 通过 Resource(IsActive=1) 映射 ResourceCode → ResourceId
///   ✅ 软删除：视图中消失的记录标记 IsActive=0，不物理删除
/// </summary>
public interface IRoutingSyncService
{
    /// <summary>
    /// 同步全部工艺路线数据（Operation + Dependency + Stage + Eligibility）
    /// 调用 sp_SyncRoutingData 存储过程在 APS 库本地完成 MERGE
    /// </summary>
    Task<RoutingSyncResultDto> SyncAllAsync(CancellationToken cancellationToken = default);
}
