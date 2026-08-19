namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_SyncRoutingData 存储过程执行结果
/// 对应 4 张工艺路线表的增量 Upsert 统计
/// </summary>
public class RoutingSyncResultDto
{
    /// <summary>批次号（ROUTING_yyyyMMdd_HHmmss）</summary>
    public string BatchNo { get; set; } = string.Empty;

    // ─── RoutingOperation（工序节点）───
    public int OperationInserted { get; set; }
    public int OperationUpdated { get; set; }
    public int OperationDeactivated { get; set; }

    // ─── RoutingDependency（工序依赖）───
    public int DependencyInserted { get; set; }
    public int DependencyUpdated { get; set; }
    public int DependencyDeactivated { get; set; }

    // ─── RoutingStage（阶段字典）───
    public int StageInserted { get; set; }
    public int StageUpdated { get; set; }
    public int StageDeactivated { get; set; }

    // ─── OperationResourceEligibility（工序资源能力）───
    public int EligibilityInserted { get; set; }
    public int EligibilityUpdated { get; set; }
    public int EligibilityDeactivated { get; set; }

    /// <summary>
    /// 未映射 MES_ID 的行数合计（4 张视图累加）
    /// MaterialMapping 缺失 MES_ID 时跳过该行
    /// </summary>
    public int UnmappedSkipped { get; set; }

    /// <summary>
    /// 未映射 ResourceCode 的行数（Resource 表中不存在该编码）
    /// </summary>
    public int ResourceUnmappedSkipped { get; set; }

    /// <summary>
    /// 未映射 ProductionDeptCode 的行数（ProductionDepartment 字典中不存在）
    /// v3.0新增
    /// </summary>
    public int DeptUnmappedSkipped { get; set; }

    /// <summary>错误信息（null 表示成功）</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    public int TotalAffected =>
        OperationInserted + OperationUpdated + OperationDeactivated +
        DependencyInserted + DependencyUpdated + DependencyDeactivated +
        StageInserted + StageUpdated + StageDeactivated +
        EligibilityInserted + EligibilityUpdated + EligibilityDeactivated;
}
