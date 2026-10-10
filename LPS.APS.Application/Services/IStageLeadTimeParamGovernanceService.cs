using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Application.Services;

/// <summary>
/// 阶段提前期参数治理服务接口（StageLeadTimeParam，PM 2026-09-28 终裁归 3号位 规则参数体系）。
/// 载体「直接生效（时间窗）」：不进 FrozenStrategySnapshot，CRUD 即最终态，靠 EffectiveFrom / EffectiveTo + IsActive 控生效。
/// 红线 #5：本接口为新建治理契约，不改动既有冻结签名。
/// </summary>
public interface IStageLeadTimeParamGovernanceService
{
    /// <summary>列出阶段提前期参数（可按工厂/阶段/生产部门/启用态过滤）。
    /// P0-03（0号位 审核 2026-10-09）：以受信主体 Business Scope 约束——非 Global 按 Factory/Department 授权集合过滤；
    /// 无授权 → fail-closed 空列表。</summary>
    /// <param name="factoryCode">工厂编码（可选精确匹配）</param>
    /// <param name="stageCode">大工艺阶段码（可选精确匹配）</param>
    /// <param name="productionDeptCode">生产部门编码（可选精确匹配）</param>
    /// <param name="isActive">启用态过滤（null = 全部）</param>
    /// <param name="actorUserId">登录用户 Id（受信主体，Scope 约束来源）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>参数列表（按 FactoryCode, StageCode, Priority, Id 排序）</returns>
    Task<IReadOnlyList<StageLeadTimeParamDto>> ListAsync(
        string? factoryCode,
        string? stageCode,
        string? productionDeptCode,
        bool? isActive,
        int actorUserId,
        CancellationToken ct = default);

    /// <summary>新增阶段提前期参数（治理侧直维护 + 审计）。业务键冲突 / 数值红线 → 数据红线异常。</summary>
    /// <param name="input">新增请求（整对象）</param>
    /// <param name="actorUserId">操作人用户 Id（审计，后端从身份取）</param>
    /// <param name="actorUserCode">操作人工号</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>落库后的参数投影（含 Id / IsActive=true）</returns>
    Task<StageLeadTimeParamDto> CreateAsync(
        SaveStageLeadTimeParamRequest input,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);

    /// <summary>编辑阶段提前期参数（PUT 整对象替换，不改 IsActive；不存在 → 资源不存在异常）。</summary>
    /// <param name="id">参数主键（StageLeadTimeParam.Id）</param>
    /// <param name="input">编辑请求（整对象替换）</param>
    /// <param name="actorUserId">操作人用户 Id</param>
    /// <param name="actorUserCode">操作人工号</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>更新后的参数投影</returns>
    Task<StageLeadTimeParamDto> UpdateAsync(
        int id,
        SaveStageLeadTimeParamRequest input,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);

    /// <summary>停用阶段提前期参数（软删 IsActive=0，保留历史与生效窗口）。</summary>
    /// <param name="id">参数主键（StageLeadTimeParam.Id）</param>
    /// <param name="actorUserId">操作人用户 Id</param>
    /// <param name="actorUserCode">操作人工号</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>停用后的参数投影（IsActive=false）</returns>
    Task<StageLeadTimeParamDto> DeactivateAsync(
        int id,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);
}