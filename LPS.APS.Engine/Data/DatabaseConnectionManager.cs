using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using Dapper;
using LPS.APS.Engine.Configuration;

namespace LPS.APS.Engine.Data;

/// <summary>
/// 数据库标识枚举
/// 对应文档中的物理隔离架构：APS本地库 + ODS集成防腐层 + Auth权限库
/// </summary>
public enum DatabaseId
{
    /// <summary>
    /// APS本地库（APS_Production）
    /// 职责：排程计算、业务数据落地、快照归档、主数据、订单、任务、Pegging
    /// </summary>
    APS,

    /// <summary>
    /// ODS集成防腐层（MES_Integration）
    /// 职责：BOM展开请求/结果（存储过程级数据库编程）、契约视图
    /// </summary>
    ODS,

    /// <summary>
    /// Auth权限库（APS_Auth）
    /// 职责：RBAC权限管理、审批流、审计日志、数据范围策略
    /// </summary>
    Auth
}

/// <summary>
/// 数据库连接管理器（三库架构）
/// 管理APS本地库、ODS集成防腐层和Auth权限库的连接
/// </summary>
public class DatabaseConnectionManager : IDisposable
{
    private readonly DatabaseOptions _options;
    private readonly SemaphoreSlim _apsSemaphore;
    private readonly SemaphoreSlim _odsSemaphore;
    private readonly SemaphoreSlim _authSemaphore;
    private readonly ILogger<DatabaseConnectionManager>? _logger;
    private SqlConnection? _apsConnection;
    private SqlConnection? _odsConnection;
    private SqlConnection? _authConnection;
    private bool _disposed = false;

    /// <param name="options">三库连接配置</param>
    /// <param name="logger">日志（DI 注入）；直接 <c>new</c> 的调用点走下面的单参重载。</param>
    public DatabaseConnectionManager(
        IOptions<DatabaseOptions> options,
        ILogger<DatabaseConnectionManager>? logger)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        if (!_options.IsValid())
        {
            throw new InvalidOperationException("数据库配置无效，请检查APS、ODS和Auth连接配置");
        }

        _apsSemaphore = new SemaphoreSlim(1, 1);
        _odsSemaphore = new SemaphoreSlim(1, 1);
        _authSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 单参构造重载（无日志）。
    ///
    /// ⚠️ 本重载存在**不是**为了「写起来方便」，而是 **Moq / Castle DynamicProxy 的硬约束**：
    /// <c>new Mock&lt;DatabaseConnectionManager&gt;(args)</c> 走 <c>CreateClassProxy(..., constructorArguments)</c>，
    /// 按**实参个数与类型精确匹配**构造函数，**不会**替调用方补 C# 可选参数的默认值。
    /// 故只留 `(IOptions, ILogger? = null)` 一个签名时，测试里 `Mock&lt;T&gt;(Options.Create(...))`（**1 个实参**）
    /// 匹配失败 ⇒ 代理类型造不出来 ⇒ <c>Mock.Object</c> 取值即抛 ArgumentException
    /// （2026-10-08 r13561 曾因此致 24 个用例由绿转红，见台账 §T-1008q）。
    /// 保留本重载 ⇒ 1 参 mock 与直接 <c>new</c> 都能绑定，**无需改任何测试**。
    ///
    /// ⇒ 通用结论（1号位 2026-10-08 提出、本号位复核采纳）：**「可选参数不影响 Moq」不成立**；
    ///    凡以 <c>Mock&lt;T&gt;(args)</c> 构造的类，若要兼容可选参数，就必须同时保留等价的**定参重载**。
    /// </summary>
    public DatabaseConnectionManager(IOptions<DatabaseOptions> options)
        : this(options, null)
    {
    }

    /// <summary>
    /// 获取数据库连接（默认APS库）
    /// </summary>
    public async Task<IDbConnection> GetConnectionAsync(DatabaseId db = DatabaseId.APS)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseConnectionManager));
        }

        var (semaphore, connOptions) = GetDbResources(db);

        await semaphore.WaitAsync();
        try
        {
            switch (db)
            {
                case DatabaseId.APS:
                    if (_apsConnection == null || _apsConnection.State != ConnectionState.Open)
                    {
                        _apsConnection?.Dispose();
                        _apsConnection = new SqlConnection(connOptions.BuildConnectionString());
                        await _apsConnection.OpenAsync();
                    }
                    return _apsConnection;

                case DatabaseId.ODS:
                    if (_odsConnection == null || _odsConnection.State != ConnectionState.Open)
                    {
                        _odsConnection?.Dispose();
                        _odsConnection = new SqlConnection(connOptions.BuildConnectionString());
                        await _odsConnection.OpenAsync();
                    }
                    return _odsConnection;

                case DatabaseId.Auth:
                    if (_authConnection == null || _authConnection.State != ConnectionState.Open)
                    {
                        _authConnection?.Dispose();
                        _authConnection = new SqlConnection(connOptions.BuildConnectionString());
                        await _authConnection.OpenAsync();
                    }
                    return _authConnection;

                default:
                    throw new ArgumentOutOfRangeException(nameof(db), db, "不支持的数据库标识");
            }
        }
        catch
        {
            semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 获取指定数据库的信号量和连接配置
    /// </summary>
    private (SemaphoreSlim semaphore, DatabaseConnectionOptions connOptions) GetDbResources(DatabaseId db)
    {
        return db switch
        {
            DatabaseId.APS => (_apsSemaphore, _options.APS),
            DatabaseId.ODS => (_odsSemaphore, _options.ODS),
            DatabaseId.Auth => (_authSemaphore, _options.Auth),
            _ => throw new ArgumentOutOfRangeException(nameof(db), db, "不支持的数据库标识")
        };
    }

    /// <summary>
    /// 释放连接
    /// </summary>
    public void ReleaseConnection(DatabaseId db = DatabaseId.APS)
    {
        var (semaphore, _) = GetDbResources(db);
        semaphore.Release();
    }

    /// <summary>
    /// 丢弃指定库的缓存连接（连接状态已不可信时调用，例如回滚失败留下未结束的事务）。
    /// 只处置并置空引用，下次 <see cref="GetConnectionAsync"/> 会重建；**不影响信号量** ——
    /// 调用方仍须按配对调用 <see cref="ReleaseConnection(DatabaseId)"/>。
    /// </summary>
    private void DiscardConnection(DatabaseId db)
    {
        switch (db)
        {
            case DatabaseId.APS:
                _apsConnection?.Dispose();
                _apsConnection = null;
                break;
            case DatabaseId.ODS:
                _odsConnection?.Dispose();
                _odsConnection = null;
                break;
            case DatabaseId.Auth:
                _authConnection?.Dispose();
                _authConnection = null;
                break;
        }
    }

    /// <summary>
    /// 执行SQL查询（返回列表）
    /// </summary>
    /// <param name="commandTimeout">命令超时时间（秒），null表示使用配置的默认超时</param>
    public virtual async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CommandType commandType = CommandType.Text, DatabaseId db = DatabaseId.APS, int? commandTimeout = null)
    {
        var (_, connOptions) = GetDbResources(db);
        var timeout = commandTimeout ?? connOptions.CommandTimeout;

        var connection = await GetConnectionAsync(db);
        try
        {
            return await connection.QueryAsync<T>(sql, parameters, commandType: commandType, commandTimeout: timeout);
        }
        finally
        {
            ReleaseConnection(db);
        }
    }

    /// <summary>
    /// 执行SQL查询（返回单个对象）
    /// </summary>
    /// <param name="commandTimeout">命令超时时间（秒），null表示使用配置的默认超时</param>
    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null, CommandType commandType = CommandType.Text, DatabaseId db = DatabaseId.APS, int? commandTimeout = null)
    {
        var (_, connOptions) = GetDbResources(db);
        var timeout = commandTimeout ?? connOptions.CommandTimeout;

        var connection = await GetConnectionAsync(db);
        try
        {
            return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters, commandType: commandType, commandTimeout: timeout);
        }
        finally
        {
            ReleaseConnection(db);
        }
    }

    /// <summary>
    /// 执行非查询SQL（INSERT、UPDATE、DELETE）
    /// </summary>
    /// <param name="commandTimeout">命令超时时间（秒），null表示使用配置的默认超时</param>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CommandType commandType = CommandType.Text, DatabaseId db = DatabaseId.APS, int? commandTimeout = null)
    {
        var (_, connOptions) = GetDbResources(db);
        var timeout = commandTimeout ?? connOptions.CommandTimeout;

        var connection = await GetConnectionAsync(db);
        try
        {
            return await connection.ExecuteAsync(sql, parameters, commandType: commandType, commandTimeout: timeout);
        }
        finally
        {
            ReleaseConnection(db);
        }
    }

    /// <summary>
    /// 执行存储过程
    /// </summary>
    public async Task<IEnumerable<T>> ExecuteStoredProcedureAsync<T>(string procedureName, object? parameters = null, DatabaseId db = DatabaseId.APS)
    {
        return await QueryAsync<T>(procedureName, parameters, CommandType.StoredProcedure, db);
    }

    /// <summary>
    /// 执行批量插入（使用SqlBulkCopy）
    /// 对应文档中的SqlBulkCopy极速推送/拉取场景
    /// </summary>
    public async Task BulkInsertAsync(DataTable dataTable, string tableName, DatabaseId db = DatabaseId.APS)
    {
        var (_, connOptions) = GetDbResources(db);
        var connection = await GetConnectionAsync(db);
        try
        {
            using var bulkCopy = new SqlBulkCopy((SqlConnection)connection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = tableName,
                BulkCopyTimeout = connOptions.CommandTimeout,
                BatchSize = 50000
            };

            // 映射列
            foreach (DataColumn column in dataTable.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(dataTable);
        }
        finally
        {
            ReleaseConnection(db);
        }
    }

    /// <summary>
    /// 批量插入（SqlBulkCopy）到调用方提供的事务连接（参与该事务，回滚即撤销）。
    /// 供 BOM 接货在 ExecuteInTransactionAsync 内写 OrderBomRequestLink 使用，
    /// 避免在事务持有同一库信号量期间再经 GetConnectionAsync 取连接造成死锁。
    /// </summary>
    public async Task BulkInsertToTransactionAsync(
        DataTable dataTable,
        string tableName,
        SqlConnection destinationConnection,
        SqlTransaction destinationTransaction,
        int timeoutSeconds = 600)
    {
        using var bulkCopy = new SqlBulkCopy(destinationConnection, SqlBulkCopyOptions.Default, destinationTransaction)
        {
            DestinationTableName = tableName,
            BulkCopyTimeout = timeoutSeconds,
            BatchSize = 50000
        };

        foreach (DataColumn column in dataTable.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable);
    }

    /// <summary>
    /// 执行事务操作
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> operation, DatabaseId db = DatabaseId.APS)
    {
        var connection = await GetConnectionAsync(db);
        try
        {
            if (connection is SqlConnection sqlConnection)
            {
                using var transaction = await sqlConnection.BeginTransactionAsync();
                try
                {
                    var result = await operation(connection, transaction);
                    await transaction.CommitAsync();
                    return result;
                }
                catch (Exception ex)
                {
                    // 【2026-10-08 修复】原实现直接 `await transaction.RollbackAsync()`：一旦**回滚自己**抛异常
                    // （本事务可能已插入百万级行——BOM 接货一批 680 万行——回滚要逐行 undo，本身就可能
                    // 跑很久乃至超时），该异常会**顶替原始异常**向外传播。
                    // ⚠️ 与恢复模式无关：2026-10-08 当时 APS_Production 是 FULL（日志近满 ⇒ 回滚代价更大），
                    //    但即便改成 SIMPLE，只要回滚自身抛错，顶替效应就照样发生 ⇒ 此修法必须保留。
                    // 后果：调用方只看到「执行超时」而看不到真正的原因（业务逻辑异常）。
                    // 2026-10-08 BOM 接货连续两次「超时」即此：真因是 ToDictionary 重复键（毫秒即抛），
                    // 却被约 9 分钟的巨额回滚超时盖住，根因因此被排查了两轮。
                    // ⇒ 现在：先留痕原始异常 → 尽力回滚 → **始终重抛原始异常**；回滚失败则丢弃该连接
                    //   （带着未结束的事务复用同一 SqlConnection 会毒化后续所有调用，且可能持锁阻塞全库读）。
                    _logger?.LogError(ex, "事务内操作失败 (db={Db})，开始回滚；本事务已插入的数据将全部撤销", db);
                    try
                    {
                        await transaction.RollbackAsync();
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger?.LogError(rollbackEx,
                            "事务回滚失败 (db={Db})：原始异常见上一条（本条的 message 不含真因）。" +
                            "已丢弃该连接、下次调用重建；若服务端仍残留活动事务需人工确认，否则会阻塞全库读取。", db);
                        DiscardConnection(db);
                    }
                    throw;
                }
            }
            else
            {
                using var transaction = connection.BeginTransaction();
                try
                {
                    var result = await operation(connection, transaction);
                    transaction.Commit();
                    return result;
                }
                catch (Exception ex)
                {
                    // 同上（SqlConnection 分支）：回滚失败不得顶替原始异常。
                    _logger?.LogError(ex, "事务内操作失败 (db={Db})，开始回滚；本事务已插入的数据将全部撤销", db);
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger?.LogError(rollbackEx,
                            "事务回滚失败 (db={Db})：原始异常见上一条（本条的 message 不含真因）。已丢弃该连接。", db);
                        DiscardConnection(db);
                    }
                    throw;
                }
            }
        }
        finally
        {
            ReleaseConnection(db);
        }
    }

    /// <summary>
    /// 跨库流式传输：从源库 ExecuteReader 流式读取，通过 SqlBulkCopy 写入目标库
    /// 适用于百万级行跨库搬运（如 ODS.MES_APS_BOM_Workset → APS.APS_BOM_RAW）
    /// ⚠️ 全程流式处理，内存占用与行数无关
    /// </summary>
    public async Task BulkCopyFromReaderAsync(
        string sourceSql,
        object? sourceParameters,
        DatabaseId sourceDb,
        string destinationTable,
        DatabaseId destinationDb,
        IDictionary<string, string>? columnMappings = null,
        int batchSize = 10000,
        int timeoutSeconds = 600)
    {
        var sourceConnection = await GetConnectionAsync(sourceDb);
        var destConnection = await GetConnectionAsync(destinationDb);
        try
        {
            var sqlSourceConn = (SqlConnection)sourceConnection;
            var sqlDestConn = (SqlConnection)destConnection;

            using var command = new SqlCommand(sourceSql, sqlSourceConn);
            command.CommandTimeout = timeoutSeconds;

            if (sourceParameters != null)
            {
                foreach (var prop in sourceParameters.GetType().GetProperties())
                {
                    command.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(sourceParameters) ?? DBNull.Value);
                }
            }

            using var reader = await command.ExecuteReaderAsync();
            using var bulkCopy = new SqlBulkCopy(sqlDestConn)
            {
                DestinationTableName = destinationTable,
                BatchSize = batchSize,
                BulkCopyTimeout = timeoutSeconds
            };

            if (columnMappings != null)
            {
                foreach (var mapping in columnMappings)
                {
                    bulkCopy.ColumnMappings.Add(mapping.Key, mapping.Value);
                }
            }
            else
            {
                // 自动映射同名列
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    bulkCopy.ColumnMappings.Add(name, name);
                }
            }

            await bulkCopy.WriteToServerAsync(reader);
        }
        finally
        {
            ReleaseConnection(sourceDb);
            ReleaseConnection(destinationDb);
        }
    }

    /// <summary>
    /// 跨库流式传输（参与目标库事务版）：从源库流式读取，SqlBulkCopy 写入调用方提供的事务连接上的目标表。
    /// 与 BulkCopyFromReaderAsync 的区别：目标连接/事务由调用方传入（用于包在 ExecuteInTransactionAsync 内），
    /// 本方法只负责打开并释放「源库」连接；⚠️ 若 sourceDb == destinationDb 会因同库信号量重入死锁，调用方须保证跨库。
    /// </summary>
    public async Task BulkCopyFromReaderToTransactionAsync(
        string sourceSql,
        object? sourceParameters,
        DatabaseId sourceDb,
        string destinationTable,
        SqlConnection destinationConnection,
        SqlTransaction destinationTransaction,
        IDictionary<string, string>? columnMappings = null,
        int batchSize = 10000,
        int timeoutSeconds = 600)
    {
        var sourceConnection = await GetConnectionAsync(sourceDb);
        try
        {
            var sqlSourceConn = (SqlConnection)sourceConnection;

            using var command = new SqlCommand(sourceSql, sqlSourceConn);
            command.CommandTimeout = timeoutSeconds;

            if (sourceParameters != null)
            {
                foreach (var prop in sourceParameters.GetType().GetProperties())
                {
                    command.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(sourceParameters) ?? DBNull.Value);
                }
            }

            using var reader = await command.ExecuteReaderAsync();
            using var bulkCopy = new SqlBulkCopy(destinationConnection, SqlBulkCopyOptions.Default, destinationTransaction)
            {
                DestinationTableName = destinationTable,
                BatchSize = batchSize,
                BulkCopyTimeout = timeoutSeconds
            };

            if (columnMappings != null)
            {
                foreach (var mapping in columnMappings)
                {
                    bulkCopy.ColumnMappings.Add(mapping.Key, mapping.Value);
                }
            }
            else
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    bulkCopy.ColumnMappings.Add(name, name);
                }
            }

            await bulkCopy.WriteToServerAsync(reader);
        }
        finally
        {
            ReleaseConnection(sourceDb);
        }
    }

    /// <summary>
    /// 测试数据库连接
    /// </summary>
    public async Task<bool> TestConnectionAsync(DatabaseId db = DatabaseId.APS)
    {
        try
        {
            var connection = await GetConnectionAsync(db);
            try
            {
                await connection.ExecuteScalarAsync<int>("SELECT 1");
                return true;
            }
            finally
            {
                ReleaseConnection(db);
            }
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _apsConnection?.Dispose();
            _odsConnection?.Dispose();
            _authConnection?.Dispose();
            _apsSemaphore?.Dispose();
            _odsSemaphore?.Dispose();
            _authSemaphore?.Dispose();
            _disposed = true;
        }
    }
}
