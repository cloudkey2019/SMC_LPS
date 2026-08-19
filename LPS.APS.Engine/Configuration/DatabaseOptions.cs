namespace LPS.APS.Engine.Configuration;

/// <summary>
/// 单个数据库连接配置
/// </summary>
public class DatabaseConnectionOptions
{
    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 数据库提供程序
    /// </summary>
    public string Provider { get; set; } = "SqlServer";

    /// <summary>
    /// 命令超时时间（秒）
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// 连接超时时间（秒）
    /// </summary>
    public int ConnectionTimeout { get; set; } = 15;

    /// <summary>
    /// 最大连接池大小
    /// </summary>
    public int MaxPoolSize { get; set; } = 100;

    /// <summary>
    /// 最小连接池大小
    /// </summary>
    public int MinPoolSize { get; set; } = 5;

    /// <summary>
    /// 是否启用连接池
    /// </summary>
    public bool Pooling { get; set; } = true;

    /// <summary>
    /// 是否只读
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ConnectionString) &&
               CommandTimeout > 0 &&
               ConnectionTimeout > 0 &&
               MaxPoolSize > 0 &&
               MinPoolSize >= 0;
    }

    /// <summary>
    /// 构建完整的连接字符串
    /// </summary>
    public string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("连接字符串不能为空");
        }

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
        {
            ConnectTimeout = ConnectionTimeout,
            MaxPoolSize = MaxPoolSize,
            MinPoolSize = MinPoolSize,
            Pooling = Pooling,
            CommandTimeout = CommandTimeout
        };

        return builder.ConnectionString;
    }
}

/// <summary>
/// 数据库配置选项（三库架构：APS本地库 + ODS集成防腐层 + Auth权限库）
/// 对应文档中的物理隔离架构设计
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// APS本地库连接配置（APS_Production）
    /// 对应文档中的"计算标准层 - APS本地库"
    /// 职责：排程计算、业务数据落地、快照归档、主数据、订单、任务、Pegging
    /// </summary>
    public DatabaseConnectionOptions APS { get; set; } = new();

    /// <summary>
    /// ODS集成防腐层连接配置（MES_Integration）
    /// 对应文档中的"集成防腐层 - ODS库"
    /// 职责：BOM展开请求/结果（存储过程级数据库编程）、契约视图
    /// 注意：BOM展开主要由SQL Server Agent Job和存储过程驱动
    /// </summary>
    public DatabaseConnectionOptions ODS { get; set; } = new();

    /// <summary>
    /// Auth权限库连接配置（APS_Auth）
    /// 职责：RBAC权限管理（User/Role/Permission）、审批流、审计日志、数据范围策略
    /// 共13张表，详见 APS_Auth数据库DDL_v1.0.sql
    /// </summary>
    public DatabaseConnectionOptions Auth { get; set; } = new();

    /// <summary>
    /// 是否启用重试
    /// </summary>
    public bool EnableRetry { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 重试延迟（毫秒）
    /// </summary>
    public int RetryDelay { get; set; } = 1000;

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public bool IsValid()
    {
        return APS.IsValid() &&
               ODS.IsValid() &&
               Auth.IsValid() &&
               MaxRetryCount >= 0 &&
               RetryDelay >= 0;
    }
}
