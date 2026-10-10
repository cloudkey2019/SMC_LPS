namespace LPS.APS.Core.DTOs.Setup;

/// <summary>
/// Setup 规则缺失（0 分钟兜底）命中频次聚合行（#10 uncovered-stats 端点响应元素）。
/// 数据源 = [Task].SetupSource = 'SETUP_RULE_MISSING_ZERO_FALLBACK' 的聚合；runId 经 PlanVersion.SourceScheduleRunId JOIN。
/// Code 回带：部门/设备/物料在聚合 SQL 内直接 JOIN 主数据表回带（DepartmentCode/ResourceCode/MaterialCode），前端零映射。
/// 四级下钻语义：runId → 部门 → 工序 → 设备 → 产品，后端按当前过滤维度返回聚合行（未传该级即不筛）。
/// </summary>
public sealed class SetupUncoveredStatDto
{
    /// <summary>排程 Run Id（经 PlanVersion.SourceScheduleRunId；无关联 Run 为 null）</summary>
    public int? ScheduleRunId { get; set; }

    /// <summary>生产部门主键</summary>
    public int DepartmentId { get; set; }

    /// <summary>生产部门编码（Code 回带）</summary>
    public string DepartmentCode { get; set; } = string.Empty;

    /// <summary>工序编码</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>资源主键</summary>
    public int ResourceId { get; set; }

    /// <summary>设备编码（Code 回带）</summary>
    public string ResourceCode { get; set; } = string.Empty;

    /// <summary>物料主键</summary>
    public int MaterialId { get; set; }

    /// <summary>物料编码（Code 回带）</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>该维度命中频次（Task 计数）</summary>
    public int HitCount { get; set; }

    /// <summary>首条命中 Task Id（下钻定位用）</summary>
    public long? SampleTaskId { get; set; }
}