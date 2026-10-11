using FluentAssertions;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Engine.Repositories.Auth;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 审计失败对账日志契约测试（R-01 方案B，0号位 复审 v1.1，3号位）。
/// 契约：审计写入失败（业务状态已落库、审计缺）时，AuditReconcile.Log 落一条结构化对账日志——
/// 可检索标记「需对账」+ 关键审计字段（结构化属性）+ 完整 AuditLog JSON（供运维/审计页一键重建补录）。
/// </summary>
public class AuditReconcileTests
{
    [Fact]
    public void Log_审计写失败_落结构化对账日志_含完整负载与需对账标记()
    {
        // Arrange
        var logger = new SpyLogger();
        var ex = new InvalidOperationException("模拟审计库不可写（预检后 Auth 连接中断）");
        var log = new AuditLog
        {
            ActionCode = "Create",
            EntityType = "DomainDefinition",
            EntityId = "10",
            VersionCode = null,
            OldValue = null,
            NewValue = "Active",
            UserId = 7,
            UserCode = "u7",
            OccurredAt = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Local),
            Remark = "域定义 DOM1（FAMILY）"
        };

        // Act
        AuditReconcile.Log(logger, ex, log);

        // Assert：对账日志契约 = Error 级 + 异常原样携带 + 可检索标记「需对账」+ 关键字段结构化 + 完整 JSON 回放
        logger.LogLevel.Should().Be(LogLevel.Error);
        logger.Exception.Should().BeSameAs(ex);
        IsReconcileState(logger.FormattedMessage!).Should().BeTrue();
    }

    private static bool IsReconcileState(string text) =>
        text.Contains("审计写入失败")
        && text.Contains("需对账")
        && text.Contains("Action=Create")
        && text.Contains("EntityType=DomainDefinition")
        && text.Contains("EntityId=10")
        && text.Contains("UserId=7")
        && text.Contains("UserCode=u7")
        && text.Contains("AuditLogJson=");

    /// <summary>ILogger 间谍：记录最近一条日志的级别/异常/格式化消息（规避 Moq It.IsAnyType 弱类型匹配复杂度）。</summary>
    private sealed class SpyLogger : ILogger<AuditLogRepository>
    {
        public LogLevel? LogLevel { get; private set; }
        public Exception? Exception { get; private set; }
        public string? FormattedMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogLevel = logLevel;
            Exception = exception;
            FormattedMessage = formatter(state, exception);
        }
    }
}
