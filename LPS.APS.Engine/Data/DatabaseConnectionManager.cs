using Microsoft.Data.SqlClient;
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
    private SqlConnection? _apsConnection;
    private SqlConnection? _odsConnection;
    private SqlConnection? _authConnection;
    private bool _disposed = false;

    public DatabaseConnectionManager(IOptions<DatabaseOptions> options)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));

        if (!_options.IsValid())
        {
            throw new InvalidOperationException("数据库配置无效，请检查APS、ODS和Auth连接配置");
        }

        _apsSemaphore = new SemaphoreSlim(1, 1);
        _odsSemaphore = new SemaphoreSlim(1, 1);
        _authSemaphore = new SemaphoreSlim(1, 1);
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
    /// 执行SQL查询（返回列表）
    /// </summary>
    /// <param name="commandTimeout">命令超时时间（秒），null表示使用配置的默认超时</param>
    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CommandType commandType = CommandType.Text, DatabaseId db = DatabaseId.APS, int? commandTimeout = null)
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
                catch
                {
                    await transaction.RollbackAsync();
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
                catch
                {
                    transaction.Rollback();
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
