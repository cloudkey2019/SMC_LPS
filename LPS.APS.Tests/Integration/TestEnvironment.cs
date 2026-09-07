using Dapper;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 集成测试环境探测辅助（3号位，P0-05/06/07/08 集成测试共用）
/// 用途：探测测试库实际 schema / 数据库可用性，缺依赖时测试动态 Skip（不假绿、不误红），
///       待 2号位完成 v5.1.2 迁移后自动转绿，无需改测试代码。
/// 边界：只读探测，绝不修改库结构（红线 #6：DB schema 变更是 2号位专属）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public static class TestEnvironment
{
    private static DatabaseConnectionManager? _cachedConnectionManager;
    private static bool? _authDbAvailable;
    private static bool? _scheduleRunHasExpectedDomainKeysColumn;
    private static bool? _hasContentSnapshotJsonColumn;
    private static bool? _hasGovernanceAuditLogTable;

    /// <summary>按 appsettings.Test.json 构造连接管理器（惰性单例，集成测试共用）</summary>
    public static DatabaseConnectionManager GetConnectionManager()
    {
        if (_cachedConnectionManager != null)
        {
            return _cachedConnectionManager;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();

        var dbOptions = configuration.GetSection("Database").Get<LPS.APS.Engine.Configuration.DatabaseOptions>()
            ?? throw new InvalidOperationException("appsettings.Test.json 未配置 Database 节");

        _cachedConnectionManager = new DatabaseConnectionManager(Options.Create(dbOptions));
        return _cachedConnectionManager;
    }

    /// <summary>APS_Auth 审计库是否可用（治理发布/确认/激活/恢复均强制写审计，Auth 库缺失则相关链路无法落地）</summary>
    public static bool IsAuthDbAvailable()
    {
        if (_authDbAvailable.HasValue)
        {
            return _authDbAvailable.Value;
        }

        try
        {
            _authDbAvailable = GetConnectionManager().TestConnectionAsync(DatabaseId.Auth).GetAwaiter().GetResult();
        }
        catch
        {
            _authDbAvailable = false;
        }

        return _authDbAvailable.Value;
    }

    /// <summary>冻结 DDL v5.1.2 ScheduleRun.ExpectedDomainKeysJson 列是否已迁移到测试库（P0-08 恢复/追溯依赖）</summary>
    public static bool HasScheduleRunExpectedDomainKeysColumn()
    {
        if (_scheduleRunHasExpectedDomainKeysColumn.HasValue)
        {
            return _scheduleRunHasExpectedDomainKeysColumn.Value;
        }

        try
        {
            var count = GetConnectionManager().QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ScheduleRun') AND name = 'ExpectedDomainKeysJson'",
                null,
                db: DatabaseId.APS).GetAwaiter().GetResult();

            _scheduleRunHasExpectedDomainKeysColumn = count > 0;
        }
        catch
        {
            _scheduleRunHasExpectedDomainKeysColumn = false;
        }

        return _scheduleRunHasExpectedDomainKeysColumn.Value;
    }

    /// <summary>
    /// 方案 A（0号位裁决，P0-01/P0-02）ContentSnapshotJson 列是否已迁移到测试库。
    /// 语义：方案 A 下六主题 JSON 列退出持久化、ContentSnapshotJson 为唯一内容载体——
    /// 该列由 2号位 v5.1.2 迁移（红线 #6 DB schema 变更专属）写入，迁移前相关集成测试动态 Skip，
    /// 迁移后自动转绿无需改测试代码。
    /// </summary>
    public static bool HasContentSnapshotJsonColumn()
    {
        if (_hasContentSnapshotJsonColumn.HasValue)
        {
            return _hasContentSnapshotJsonColumn.Value;
        }

        try
        {
            var count = GetConnectionManager().QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RuleSetVersion') AND name = 'ContentSnapshotJson'",
                null,
                db: DatabaseId.APS).GetAwaiter().GetResult();

            _hasContentSnapshotJsonColumn = count > 0;
        }
        catch
        {
            _hasContentSnapshotJsonColumn = false;
        }

        return _hasContentSnapshotJsonColumn.Value;
    }

    /// <summary>
    /// APS_Auth 库 GovernanceAuditLog 表是否已建（治理审计，3号位 A-7 配套 2号位 DDL）。
    /// 语义：实体/仓储/DbSet 已由 3号位 交付，物理表需 2号位 Auth 库 DDL 补建并迁移测试库
    /// （红线 #6 DB 结构变更 2号位 专属）；建表前相关治理发布/确认/激活集成测试动态 Skip，
    /// 建表后自动转绿无需改测试代码。
    /// </summary>
    public static bool HasGovernanceAuditLogTable()
    {
        if (_hasGovernanceAuditLogTable.HasValue)
        {
            return _hasGovernanceAuditLogTable.Value;
        }

        try
        {
            var count = GetConnectionManager().QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE name = 'GovernanceAuditLog'",
                null,
                db: DatabaseId.Auth).GetAwaiter().GetResult();

            _hasGovernanceAuditLogTable = count > 0;
        }
        catch
        {
            _hasGovernanceAuditLogTable = false;
        }

        return _hasGovernanceAuditLogTable.Value;
    }

    /// <summary>清理测试数据（按 FK 依赖倒序 DELETE）</summary>
    public static async Task CleanupAsync(DatabaseConnectionManager cm, params (string Sql, object? Param)[] deletes)
    {
        foreach (var (sql, param) in deletes)
        {
            try
            {
                await cm.ExecuteAsync(sql, param, db: DatabaseId.APS);
            }
            catch
            {
                // 清理尽力而为，单条失败不阻断后续清理
            }
        }
    }
}
