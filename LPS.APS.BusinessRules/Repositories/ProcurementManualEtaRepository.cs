using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 采购人工ETA Repository实现
///
/// 【实现说明 - 2026-08-26审核修正】
/// - 数据库：DatabaseId.APS（不是ODS）
/// - 表名：ProcurementManualEtaOverride
/// - 业务键：PONo + LineNo + MaterialId + ReceivingWarehouse
/// - 取消方式：IsActive=0（不物理删除）
///
/// 审核裁决：Manual ETA属于APS业务事实表，不属于ODS防腐层
/// 参考：APS_V1_5号位_Commit_42a16ed_新基线整改复审报告_20260825.md P0-01
/// </summary>
public class ProcurementManualEtaRepository : IProcurementManualEtaRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ProcurementManualEtaRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<PageResult<ProcurementManualEtaOverride>> QueryAsync(
        List<int>? materialIds = null,
        List<string>? materialCodes = null,
        List<string>? poNos = null,
        List<string>? receivingWarehouses = null,
        DateTime? etaBefore = null,
        DateTime? etaAfter = null,
        DateTime? updatedAfter = null,
        bool activeOnly = true,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // Dapper 列表参数只能出现在 IN 内；@X IS NULL(标量) 会随 @X 一起扩成 (@p1,@p2) → "(@p1,@p2) IS NULL" 非法 SQL 4145。
        // 用 Has 标志替代标量判空；任一个列表空集即 fail-closed 返空，避免空集传入 IN ()。
        if (materialIds is { Count: 0 } || materialCodes is { Count: 0 }
            || poNos is { Count: 0 } || receivingWarehouses is { Count: 0 })
            return new PageResult<ProcurementManualEtaOverride>
            {
                Items = Array.Empty<ProcurementManualEtaOverride>(),
                Total = 0,
                Page = pageIndex <= 0 ? 1 : pageIndex,
                PageSize = pageSize
            };
        var hasMaterialIds = materialIds is { Count: > 0 };
        var hasMaterialCodes = materialCodes is { Count: > 0 };
        var hasPONos = poNos is { Count: > 0 };
        var hasWarehouses = receivingWarehouses is { Count: > 0 };

        var whereSql = @"
WHERE 1=1
    AND (@ActiveOnly = 0 OR IsActive = 1)
    AND (@HasMaterialIds = 0 OR MaterialId IN @MaterialIds)
    AND (@HasMaterialCodes = 0 OR MaterialCode IN @MaterialCodes)
    AND (@HasPONos = 0 OR PONo IN @PONos)
    AND (@HasWarehouses = 0 OR ReceivingWarehouse IN @Warehouses)
    AND (@EtaBefore IS NULL OR ManualEta <= @EtaBefore)
    AND (@EtaAfter IS NULL OR ManualEta >= @EtaAfter)
    AND (@UpdatedAfter IS NULL OR UpdatedAt >= @UpdatedAfter)";

        var baseParameters = new
        {
            ActiveOnly = activeOnly ? 1 : 0,
            HasMaterialIds = hasMaterialIds,
            HasMaterialCodes = hasMaterialCodes,
            HasPONos = hasPONos,
            HasWarehouses = hasWarehouses,
            MaterialIds = materialIds,
            MaterialCodes = materialCodes,
            PONos = poNos,
            Warehouses = receivingWarehouses,
            EtaBefore = etaBefore,
            EtaAfter = etaAfter,
            UpdatedAfter = updatedAfter
        };

        var total = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM ProcurementManualEtaOverride" + whereSql,
            baseParameters,
            CommandType.Text,
            DatabaseId.APS,
            commandTimeout: 30);

        var page = pageIndex <= 1 ? 1 : pageIndex;
        var skip = (page - 1) * pageSize;
        var take = pageSize;

        var pagedSql = @"
SELECT
    PONo, [LineNo], MaterialId, MaterialCode, ReceivingWarehouse,
    ManualEta, IsActive, UpdatedBy, UpdatedAt, CreatedBy, CreatedAt, Remark
FROM ProcurementManualEtaOverride" + whereSql + @"
ORDER BY UpdatedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var pagedParameters = new
        {
            ActiveOnly = baseParameters.ActiveOnly,
            HasMaterialIds = hasMaterialIds,
            HasMaterialCodes = hasMaterialCodes,
            HasPONos = hasPONos,
            HasWarehouses = hasWarehouses,
            MaterialIds = materialIds,
            MaterialCodes = materialCodes,
            PONos = poNos,
            Warehouses = receivingWarehouses,
            EtaBefore = etaBefore,
            EtaAfter = etaAfter,
            UpdatedAfter = updatedAfter,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<ProcurementManualEtaOverride>(
            pagedSql,
            pagedParameters,
            CommandType.Text,
            DatabaseId.APS,
            commandTimeout: 30);

        return new PageResult<ProcurementManualEtaOverride>
        {
            Items = results.ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<ProcurementManualEtaOverride>> GetActiveOverridesAsync(CancellationToken ct = default)
    {
        var sql = @"
SELECT
    PONo, [LineNo], MaterialId, MaterialCode, ReceivingWarehouse,
    ManualEta, IsActive, UpdatedBy, UpdatedAt, CreatedBy, CreatedAt, Remark
FROM ProcurementManualEtaOverride
WHERE IsActive = 1
ORDER BY UpdatedAt DESC";

        var results = await _connectionManager.QueryAsync<ProcurementManualEtaOverride>(
            sql,
            null,
            CommandType.Text,
            DatabaseId.APS,
            commandTimeout: 30);

        return results.ToList();
    }

    public async Task<ProcurementManualEtaOverride?> GetByBusinessKeyAsync(
        string poNo,
        int lineNo,
        int materialId,
        string receivingWarehouse,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    PONo, [LineNo], MaterialId, MaterialCode, ReceivingWarehouse,
    ManualEta, IsActive, UpdatedBy, UpdatedAt, CreatedBy, CreatedAt, Remark
FROM ProcurementManualEtaOverride
WHERE PONo = @PONo
    AND [LineNo] = @LineNo
    AND MaterialId = @MaterialId
    AND ReceivingWarehouse = @ReceivingWarehouse";

        var parameters = new
        {
            PONo = poNo,
            LineNo = lineNo,
            MaterialId = materialId,
            ReceivingWarehouse = receivingWarehouse
        };

        var results = await _connectionManager.QueryAsync<ProcurementManualEtaOverride>(
            sql,
            parameters,
            CommandType.Text,
            DatabaseId.APS,
            commandTimeout: 10);

        return results.FirstOrDefault();
    }

    public async Task<ProcurementManualEtaOverride> UpsertAsync(ProcurementManualEtaOverride @override, CancellationToken ct = default)
    {
        var sql = @"
MERGE INTO ProcurementManualEtaOverride AS target
USING (SELECT
    @PONo AS PONo,
    @LineNo AS [LineNo],
    @MaterialId AS MaterialId,
    @ReceivingWarehouse AS ReceivingWarehouse
) AS source
ON target.PONo = source.PONo
    AND target.[LineNo] = source.[LineNo]
    AND target.MaterialId = source.MaterialId
    AND target.ReceivingWarehouse = source.ReceivingWarehouse
WHEN MATCHED THEN
    UPDATE SET
        ManualEta = @ManualEta,
        IsActive = @IsActive,
        UpdatedBy = @UpdatedBy,
        UpdatedAt = GETDATE(),
        Remark = @Remark
WHEN NOT MATCHED THEN
    INSERT (PONo, [LineNo], MaterialId, MaterialCode, ReceivingWarehouse,
            ManualEta, IsActive, UpdatedBy, UpdatedAt, CreatedBy, CreatedAt, Remark)
    VALUES (@PONo, @LineNo, @MaterialId, @MaterialCode, @ReceivingWarehouse,
            @ManualEta, @IsActive, @UpdatedBy, GETDATE(), @UpdatedBy, GETDATE(), @Remark);";

        var parameters = new
        {
            PONo = @override.PONo,
            LineNo = @override.LineNo,
            MaterialId = @override.MaterialId,
            MaterialCode = @override.MaterialCode,
            ReceivingWarehouse = @override.ReceivingWarehouse,
            ManualEta = @override.ManualEta,
            IsActive = @override.IsActive ? 1 : 0,
            UpdatedBy = @override.UpdatedBy,
            Remark = @override.Remark
        };

        await _connectionManager.ExecuteAsync(
            sql,
            parameters,
            CommandType.Text,
            DatabaseId.APS,
            commandTimeout: 30);

        // 查询保存后的完整实体返回
        var saved = await GetByBusinessKeyAsync(
            @override.PONo, @override.LineNo, @override.MaterialId, @override.ReceivingWarehouse, ct);

        return saved!;
    }

    public async Task<bool> CancelAsync(
        string poNo,
        int lineNo,
        int materialId,
        string receivingWarehouse,
        string updatedBy,
        CancellationToken ct = default)
    {
        var sql = @"
UPDATE ProcurementManualEtaOverride
SET IsActive = 0,
    UpdatedBy = @UpdatedBy,
    UpdatedAt = GETDATE()
WHERE PONo = @PONo
    AND [LineNo] = @LineNo
    AND MaterialId = @MaterialId
    AND ReceivingWarehouse = @ReceivingWarehouse;";

        var parameters = new
        {
            PONo = poNo,
            LineNo = lineNo,
            MaterialId = materialId,
            ReceivingWarehouse = receivingWarehouse,
            UpdatedBy = updatedBy
        };

        var rowCount = await _connectionManager.ExecuteAsync(
            sql,
            parameters,
            CommandType.Text,
            DatabaseId.APS,
            commandTimeout: 10);

        return rowCount > 0;
    }
}
