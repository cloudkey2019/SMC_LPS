using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// ODS/复杂事实Issue查询Repository实现
/// 从多个5号位事实源的Issue表聚合查询
/// </summary>
public class BusinessFactIssueRepository : IBusinessFactIssueRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public BusinessFactIssueRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<BusinessFactIssueDto>> QueryBomWorksetIssuesAsync(
        string? batchNo = null,
        string? materialCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        // Q5 三级 Scope 处理（31-0 裁决）：
        // ① 有 ExpectedFactory/ActualFactory → 按 Factory Scope 过滤
        // ② Factory 均 NULL → 仅 Global 用户可见（本查询无法通过其他权威关系确定 Scope）
        // Dapper 列表参数只进 IN；@AllowedFactories IS NULL 标量判空会随列表一起扩成 (…) 导致 4145，用 Has 标志替代；空集 fail-closed 返空。
        if (allowedFactories is { Count: 0 })
            return new List<BusinessFactIssueDto>();
        var hasFactories = allowedFactories is { Count: > 0 };

        var sql = @"
SELECT
    'BOM_WORKSET' AS Source,
    IssueType,
    Severity,
    Detail,
    ISNULL(ParentMaterialCode, ChildMaterialCode) AS MaterialCode,
    COALESCE(ExpectedFactory, ActualFactory) AS FactoryCode,
    BOMNO AS DocumentNo,
    NULL AS StageCode,
    NULL AS AffectedQuantity,
    DegradeAction,
    ReviewStatus,
    ReviewedBy,
    ReviewedAt,
    CreatedAt
FROM MES_APS_BOM_Workset_Issues
WHERE 1=1
    AND (@BatchNo IS NULL OR BatchNo = @BatchNo)
    AND (@MaterialCode IS NULL OR ParentMaterialCode = @MaterialCode OR ChildMaterialCode = @MaterialCode)
    AND (@Severity IS NULL OR Severity = @Severity)
    AND (@ReviewStatus IS NULL OR ReviewStatus = @ReviewStatus)
    AND (@HasFactories = 0 OR COALESCE(ExpectedFactory, ActualFactory) IN @AllowedFactories)
ORDER BY CreatedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            BatchNo = batchNo,
            MaterialCode = materialCode,
            Severity = severity,
            ReviewStatus = reviewStatus,
            HasFactories = hasFactories,
            AllowedFactories = allowedFactories,
            Skip = skip,
            Take = take
        };

        // MES_APS_BOM_Workset_Issues 属 ODS 集成层（MES_Integration，随 MES_APS_BOM_Workset 同库），非 APS_Production。
        // 此前误用 DatabaseId.APS → 查错库 → "对象名无效" 500（4号位 2026-10-10 报）。
        var results = await _connectionManager.QueryAsync<BusinessFactIssueDto>(
            sql, parameters, CommandType.Text, DatabaseId.ODS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<BusinessFactIssueDto>> QueryMaterialStageContextIssuesAsync(
        string? batchNo = null,
        string? materialCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        // Q5 三级 Scope 处理（31-0 裁决）：
        // ① 有 StageCode → 通过 ProcessCodeDict 派生 FactoryCode → 按 Factory Scope 过滤
        // ② StageCode 均无法派生 FactoryCode → 仅 Global 用户可见
        // Dapper 列表参数只进 IN；@AllowedFactories IS NULL 会导致 4145，用 Has 标志替代；空集 fail-closed 返空。
        if (allowedFactories is { Count: 0 })
            return new List<BusinessFactIssueDto>();
        var hasFactories = allowedFactories is { Count: > 0 };

        var sql = @"
SELECT
    'MATERIAL_STAGE_CONTEXT' AS Source,
    m.IssueType,
    m.Severity,
    m.Detail,
    m.MaterialCode,
    pc.FactoryCode,
    NULL AS DocumentNo,
    m.StageCode,
    NULL AS AffectedQuantity,
    m.DegradeAction,
    m.ReviewStatus,
    m.ReviewedBy,
    m.ReviewedAt,
    m.CreatedAt
FROM MaterialStageDeptContext_Issues m
LEFT JOIN ext_MES_ProcessCode_View pc
    ON pc.StageCode = m.StageCode
WHERE 1=1
    AND (@BatchNo IS NULL OR m.BatchNo = @BatchNo)
    AND (@MaterialCode IS NULL OR m.MaterialCode = @MaterialCode)
    AND (@Severity IS NULL OR m.Severity = @Severity)
    AND (@ReviewStatus IS NULL OR m.ReviewStatus = @ReviewStatus)
    AND (@HasFactories = 0 OR pc.FactoryCode IN @AllowedFactories)
ORDER BY m.CreatedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            BatchNo = batchNo,
            MaterialCode = materialCode,
            Severity = severity,
            ReviewStatus = reviewStatus,
            HasFactories = hasFactories,
            AllowedFactories = allowedFactories,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<BusinessFactIssueDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<BusinessFactIssueDto>> QueryAllAsync(
        string? source = null,
        string? materialCode = null,
        string? factoryCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        var allIssues = new List<BusinessFactIssueDto>();

        // BOM Workset Issues
        if (string.IsNullOrEmpty(source) || source == "BOM_WORKSET")
        {
            var bomIssues = await QueryBomWorksetIssuesAsync(
                materialCode: materialCode, severity: severity,
                reviewStatus: reviewStatus, take: take, ct: ct,
                allowedFactories: allowedFactories);
            allIssues.AddRange(bomIssues);
        }

        // MaterialStageDeptContext Issues
        if (string.IsNullOrEmpty(source) || source == "MATERIAL_STAGE_CONTEXT")
        {
            var mscIssues = await QueryMaterialStageContextIssuesAsync(
                materialCode: materialCode, severity: severity,
                reviewStatus: reviewStatus, take: take, ct: ct,
                allowedFactories: allowedFactories);
            allIssues.AddRange(mscIssues);
        }

        // 按CreatedAt降序排序，分页
        return allIssues
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
    }
}
