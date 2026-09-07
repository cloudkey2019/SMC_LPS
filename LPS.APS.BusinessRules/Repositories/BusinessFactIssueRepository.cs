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
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    'BOM_WORKSET' AS Source,
    IssueType,
    Severity,
    Detail,
    ISNULL(ParentMaterialCode, ChildMaterialCode) AS MaterialCode,
    NULL AS FactoryCode,
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
ORDER BY CreatedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            BatchNo = batchNo,
            MaterialCode = materialCode,
            Severity = severity,
            ReviewStatus = reviewStatus,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<BusinessFactIssueDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<BusinessFactIssueDto>> QueryMaterialStageContextIssuesAsync(
        string? batchNo = null,
        string? materialCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    'MATERIAL_STAGE_CONTEXT' AS Source,
    IssueType,
    Severity,
    Detail,
    MaterialCode,
    NULL AS FactoryCode,
    NULL AS DocumentNo,
    StageCode,
    NULL AS AffectedQuantity,
    DegradeAction,
    ReviewStatus,
    ReviewedBy,
    ReviewedAt,
    CreatedAt
FROM MaterialStageDeptContext_Issues
WHERE 1=1
    AND (@BatchNo IS NULL OR BatchNo = @BatchNo)
    AND (@MaterialCode IS NULL OR MaterialCode = @MaterialCode)
    AND (@Severity IS NULL OR Severity = @Severity)
    AND (@ReviewStatus IS NULL OR ReviewStatus = @ReviewStatus)
ORDER BY CreatedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            BatchNo = batchNo,
            MaterialCode = materialCode,
            Severity = severity,
            ReviewStatus = reviewStatus,
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
        CancellationToken ct = default)
    {
        var allIssues = new List<BusinessFactIssueDto>();

        // BOM Workset Issues
        if (string.IsNullOrEmpty(source) || source == "BOM_WORKSET")
        {
            var bomIssues = await QueryBomWorksetIssuesAsync(
                materialCode: materialCode, severity: severity,
                reviewStatus: reviewStatus, take: take, ct: ct);
            allIssues.AddRange(bomIssues);
        }

        // MaterialStageDeptContext Issues
        if (string.IsNullOrEmpty(source) || source == "MATERIAL_STAGE_CONTEXT")
        {
            var mscIssues = await QueryMaterialStageContextIssuesAsync(
                materialCode: materialCode, severity: severity,
                reviewStatus: reviewStatus, take: take, ct: ct);
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
