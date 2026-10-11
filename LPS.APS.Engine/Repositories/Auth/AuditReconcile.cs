using LPS.APS.Core.Entities.Auth;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// 审计失败对账日志辅助（R-01，方案B，0号位 复审 v1.1）。
/// 场景：APS 业务状态已落库、Auth 审计写入失败（跨库无 2PC，存在「业务已写、审计缺」窗口）——
/// 落一条结构化对账日志（关键审计字段 + 完整 AuditLog JSON），供运维/审计页据此人工补录，
/// 随后调用方原样重抛（P1-05 fail-closed，绝不静默吞掉审计）。
/// 独立静态辅助便于单测（对账日志契约 = 可检索标记「需对账」 + 完整可重建负载）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public static class AuditReconcile
{
    /// <summary>对账日志统一消息模板：业务状态已落库、审计缺、需对账；含可重建全部审计字段。</summary>
    public const string ReconcileMessage =
        "审计写入失败（业务状态已落库、审计缺、需对账）：Action={ActionCode} EntityType={EntityType} EntityId={EntityId} VersionCode={VersionCode} Before={OldValue} After={NewValue} UserId={UserId} UserCode={UserCode} OccurredAt={OccurredAt} Remark={Remark} AuditLogJson={AuditLogJson}";

    /// <summary>
    /// 落审计失败对账日志（结构化属性便于按字段检索，完整 JSON 便于一键重建 AuditLog 补录）。
    /// </summary>
    /// <param name="logger">调用方日志器（结构化属性随调用方分类）</param>
    /// <param name="exception">审计写入异常（携带原始堆栈/原因）</param>
    /// <param name="auditLog">未能落库的审计记录（对账日志以全量 JSON 回放）</param>
    public static void Log(ILogger logger, Exception exception, AuditLog auditLog)
    {
        logger.LogError(exception,
            ReconcileMessage,
            auditLog.ActionCode,
            auditLog.EntityType,
            auditLog.EntityId,
            auditLog.VersionCode,
            auditLog.OldValue,
            auditLog.NewValue,
            auditLog.UserId,
            auditLog.UserCode,
            auditLog.OccurredAt,
            auditLog.Remark,
            JsonSerializer.Serialize(auditLog));
    }
}
