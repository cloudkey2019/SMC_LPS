using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Application.Services;

/// <summary>
/// 执行批拆分规则（Batch Policy）治理服务接口（0号位 2026-10-07 裁决：本轮落码，归 3号位 治理）。
/// 载体「两级承接」：主题表 CRUD + 发布时投影进 FrozenStrategySnapshot.BatchPolicy（第⑧块）。
/// 主题表 TaskSplitRuleConfig 为直接生效物理表（非版本化 JSON），靠 IsActive + EffectiveFrom/EffectiveTo 控生效。
/// 红线 #5：本接口为新建治理契约，不改动既有冻结签名。
/// </summary>
public interface ITaskSplitRuleConfigGovernanceService
{
    /// <summary>分页列出执行批拆分规则（可按物料/生产部门/启用态过滤；R2 标准分页契约，4号位 2026-10-08 提请）。
    /// P0-01（0号位 审核 2026-10-09）：以受信主体 Business Scope 约束——Global 不过滤；非 Global 按 Department 授权部门
    /// IN 约束 COUNT + 列表（Auth §9.3）；无授权 → fail-closed 空页；显式越权部门 → 403。</summary>
    /// <param name="materialId">物料 Id（可选精确匹配）</param>
    /// <param name="productionDepartmentId">生产部门 Id（可选精确匹配；越权 → 403）</param>
    /// <param name="isActive">启用态过滤（null = 全部）</param>
    /// <param name="actorUserId">登录用户 Id（受信主体，Scope 约束来源）</param>
    /// <param name="pageIndex">页码（1 基，&lt; 1 强制归 1）</param>
    /// <param name="pageSize">每页条数（1~200，超限截断；默认 20）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>PageResult（Items 按 MaterialId, ProductionDepartmentId, Id 排序；Total 为筛选后总数）</returns>
    Task<PageResult<TaskSplitRuleConfigDto>> ListAsync(
        int? materialId,
        int? productionDepartmentId,
        bool? isActive,
        int actorUserId,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>新增执行批拆分规则（治理侧直维护 + 审计）。业务键冲突 / 数值红线 → 数据红线异常。</summary>
    Task<TaskSplitRuleConfigDto> CreateAsync(
        SaveTaskSplitRuleConfigRequest input,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);

    /// <summary>编辑执行批拆分规则（PUT 整对象替换，不改 IsActive；不存在 → 资源不存在异常）。</summary>
    Task<TaskSplitRuleConfigDto> UpdateAsync(
        int id,
        SaveTaskSplitRuleConfigRequest input,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);

    /// <summary>停用执行批拆分规则（软删 IsActive=0，保留历史与生效窗口）。</summary>
    Task<TaskSplitRuleConfigDto> DeactivateAsync(
        int id,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default);

    /// <summary>
    /// 取 active + 生效区间内规则并投影为第⑧块快照（BatchPolicyRuleSnapshot）。
    /// 供 GovernanceVersionService 发布参数集版本时注入 ContentSnapshotJson.BatchPolicy 子块。
    /// 去审计/兼容字段；缺规则 = 空列表（fail-closed 属消费侧，本方法不抛）。
    /// </summary>
    Task<IReadOnlyList<BatchPolicyRuleSnapshot>> GetActiveRulesForSnapshotAsync(CancellationToken ct = default);
}