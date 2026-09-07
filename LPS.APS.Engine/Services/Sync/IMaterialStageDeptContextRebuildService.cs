using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 物料×阶段→默认生产部门 上下文重建服务（2号位职责 — 每日 01:55）
///
/// 调用 sp_RebuildMaterialStageDeptContext（FULL 全量重建），在 APS 库本地完成：
///   1. MaterialSupplyContext(IsCurrent=1) 自动归一化 → (MaterialId, StageCode, DeptId) 草稿
///   2. MaterialStageDeptOverride 人工覆盖（优先于自动草稿）
///   3. SCD Type 2 写入 MaterialStageDeptContext（失效旧版 + 插入新版）
///   4. 冲突/缺失登记 MaterialStageDeptContext_Issues，旧 IsCurrent=1 不动（降级原则）
///
/// 消费契约：1号位排程按 (MaterialId, StageCode) → DefaultProductionDepartmentId 锁定 Routing 三件套。
/// SP 契约：sp_RebuildMaterialStageDeptContext（Database/Scripts/APS/ 独立脚本，不修改冻结文档）
/// </summary>
public interface IMaterialStageDeptContextRebuildService
{
    /// <summary>
    /// FULL 全量重建 MaterialStageDeptContext
    /// </summary>
    Task<MaterialStageDeptContextRebuildResultDto> RebuildAsync(CancellationToken cancellationToken = default);
}
