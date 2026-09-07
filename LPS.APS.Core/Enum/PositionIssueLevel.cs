namespace LPS.APS.Core.Enum;

/// <summary>
/// Position计算问题等级
/// </summary>
public enum PositionIssueLevel
{
    /// <summary>
    /// 信息级别（追溯性信息，不影响计算）
    /// </summary>
    INFO = 1,

    /// <summary>
    /// 警告级别（可以保守降级继续）
    /// </summary>
    WARN = 2,

    /// <summary>
    /// 错误级别（该对象无法形成可靠业务事实，可能导致Domain失败）
    /// </summary>
    ERROR = 3
}
