using LPS.APS.Core.DTOs.Setup;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// Setup 换型规则治理编排服务契约（4号位 Setup 契约 §11.1 #1-8）。
/// 职责：入参/版本态校验（400 状态红线 / 422 数据红线）→ 写路径（子块 CRUD + §十 冲突校验 + 统一审计，SetupRuleService 自实现）
///       → 主数据 Code 回带（<see cref="IMasterDataLookupRepository"/>）→ 版本态派生 Status → SetupRuleDto 读模型。
/// 错误映射：唯一键冲突/数据红线抛 <see cref="Exceptions.SetupRuleDataRedLineException"/>（422）；版本/规则不存在抛
///          <see cref="Exceptions.ResourceNotFoundException"/>（404）；非 DRAFT 写操作抛 <see cref="InvalidOperationException"/>（400）。
/// </summary>
public interface ISetupRuleService
{
    /// <summary>按版本 + 类型列出规则（#1 GET setup-rules/exact / #5 GET setup-rules/default）</summary>
    Task<IReadOnlyList<SetupRuleDto>> ListAsync(long ruleSetVersionId, string ruleType, CancellationToken cancellationToken = default);

    /// <summary>按 Id 查询单条规则（读模型回执）</summary>
    Task<SetupRuleDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>新增 EXACT 规则（#2）</summary>
    Task<SetupRuleDto> CreateExactAsync(SetupRuleExactInput input, int actorUserId, string actorUserCode, CancellationToken cancellationToken = default);

    /// <summary>新增 DEFAULT 规则（#6）</summary>
    Task<SetupRuleDto> CreateDefaultAsync(SetupRuleDefaultInput input, int actorUserId, string actorUserCode, CancellationToken cancellationToken = default);

    /// <summary>更新 EXACT 规则（#3）</summary>
    Task<SetupRuleDto> UpdateExactAsync(long id, SetupRuleExactInput input, int actorUserId, string actorUserCode, CancellationToken cancellationToken = default);

    /// <summary>更新 DEFAULT 规则（#7）</summary>
    Task<SetupRuleDto> UpdateDefaultAsync(long id, SetupRuleDefaultInput input, int actorUserId, string actorUserCode, CancellationToken cancellationToken = default);

    /// <summary>删除规则（#4 / #8，按 Id 硬删）</summary>
    Task DeleteAsync(long id, int actorUserId, string actorUserCode, CancellationToken cancellationToken = default);

    /// <summary>工序资源资格查询（#9：当前 Operation 合法资源 ∩ 前后产品合法设备交集，Code 回带）</summary>
    Task<OperationResourceEligibilityDto> GetEligibilityAsync(string operationCode, int materialId, int? toMaterialId, CancellationToken cancellationToken = default);

    /// <summary>规则集版本列表（#11；可选按规则集/状态过滤，Status 为治理六态原文）</summary>
    Task<IReadOnlyList<SetupRuleSetVersionDto>> ListRuleSetVersionsAsync(long? ruleSetId, string? status, CancellationToken cancellationToken = default);

    /// <summary>版本 Setup 换型规则差异（#12：added/modified/removed + 计数 + 目标版本发布态）</summary>
    Task<SetupDiffDto> GetDiffAsync(long versionId, long otherVersionId, CancellationToken cancellationToken = default);

    /// <summary>发布规则集版本（#13：发布前 Setup 冲突全量预校验 → 治理版本状态机推进）</summary>
    Task PublishAsync(long versionId, string? changeReason, int actorUserId, string actorUserCode, CancellationToken cancellationToken = default);

    /// <summary>主数据 Code→Id 读：部门下拉（Setup 三 Dialog 数据源；search 模糊 DeptCode；红线 #4 返回列表）</summary>
    Task<IReadOnlyList<DepartmentLookupItem>> LookupDepartmentsAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>主数据 Code→Id 读：设备下拉（Setup 三 Dialog 数据源；search 模糊 ResourceCode/ResourceName；红线 #4 返回列表）</summary>
    Task<IReadOnlyList<ResourceLookupItem>> LookupResourcesAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>主数据 Code→Id 读：物料下拉（Setup 三 Dialog 数据源；search 模糊 MaterialCode/MaterialName/Spec；activeOnly=true 仅活动；红线 #4 返回列表）</summary>
    Task<IReadOnlyList<MaterialLookupItem>> LookupMaterialsAsync(string? search, bool activeOnly = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// #10 uncovered-stats：规则缺失（0 分钟兜底）命中频次聚合（v1.4 §十一 KPI 监控）。
    /// 数据源 = [Task].SetupSource='SETUP_RULE_MISSING_ZERO_FALLBACK' 聚合；四级下钻 runId → 部门 → 工序 → 设备 → 产品，
    /// 全部过滤参数可选（未传该级不筛）。响应 Code 回带（部门/设备/物料），前端零映射。
    /// 只读查询；空结果返回空列表（非 null）。
    /// </summary>
    Task<IReadOnlyList<SetupUncoveredStatDto>> GetUncoveredStatsAsync(
        int? runId,
        int? departmentId,
        string? operationCode,
        int? resourceId,
        int? materialId,
        CancellationToken cancellationToken = default);
}
