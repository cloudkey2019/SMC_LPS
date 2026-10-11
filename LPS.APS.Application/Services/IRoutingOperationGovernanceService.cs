using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Application.Services;

/// <summary>
/// 工序工艺属性治理服务接口（T1 · S2/S3 OPM 治理 API）。
/// OperationPlanningMode（OPM）属 APS 工艺规划属性（治理配置，0号位 2026-09-23 Q1 裁决），
/// 治理侧直维护唯一主路径；不依赖 MES/ODS 供给。红线 #5：本接口为新建治理契约，不改动既有冻结签名。
/// </summary>
public interface IRoutingOperationGovernanceService
{
    /// <summary>
    /// 按物料列工序（含当前 OPM 值），供 4号位 配置页面选择工序维护 OPM。
    /// 0号位 再审核 3-NEW-P0-02：列表按操作人 Department Scope 过滤——Global 不过滤；非 Global 仅返回授权部门工序
    /// （同物料 A/B 部门工序隔离，防跨部门读取工艺属性）；无任何部门授权 → fail-closed 空列表。
    /// <paramref name="actorUserId"/> 为受信主体（Controller 从登录身份取）。
    /// </summary>
    /// <param name="materialId">物料主键（Material.Id），须 &gt; 0</param>
    /// <param name="actorUserId">操作人用户 Id（后端从登录身份取，列表 Scope 过滤）</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<RoutingOperationDto>> ListOperationsAsync(int materialId, int actorUserId, CancellationToken ct = default);

    /// <summary>
    /// 维护指定工序的 OPM 值（治理侧直维护 + 审计）。三态非法 → 抛数据红线异常；工序不存在 → 抛资源不存在异常。
    /// </summary>
    /// <param name="operationId">工序主键（RoutingOperation.Id）</param>
    /// <param name="mode">OPM 三态之一</param>
    /// <param name="actorUserId">操作人用户 Id（审计真实 Actor，后端从身份取）</param>
    /// <param name="actorUserCode">操作人工号</param>
    /// <param name="ct">取消令牌</param>
    Task<RoutingOperationDto> UpdateOperationPlanningModeAsync(
        long operationId,
        string mode,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);
}