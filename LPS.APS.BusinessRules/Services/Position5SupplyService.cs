using LPS.APS.BusinessRules.Calculators;
using LPS.APS.BusinessRules.Loaders;
using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Position 5 Supply Service
///
/// 【职责边界说明 - 2026-08-25新基线】
/// 5号位职责：提供原始采购事实（ETA/ReleaseDate/Warehouse等）
/// 5号位不再负责：计算Effective ETA和AvailableTime（属于2号位职责）
///
/// 正式链路（新基线）：
///   5号位：装载原始事实（ERP ETA + ReleaseDate）
///     ↓
///   2号位：读取ManualEta覆盖 + 计算Effective ETA + 计算AvailableTime
///     ↓
///   2号位：Pegging / Solver
///
/// 参考：文档/20260818/更新文档20260825/APS_V1_5号位新基线增量整改开发包_v1.0_20260825.md
/// </summary>
public class Position5SupplyService
{
    private readonly ITimedSupplyFactLoader _loader;

    public Position5SupplyService(ITimedSupplyFactLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    /// <summary>
    /// 装载原始采购供给事实（5号位新职责：只提供原始事实，不计算Effective ETA/AvailableTime）
    /// </summary>
    public async Task<Position5SupplyResult> LoadProcurementSupplyAsync(
        SupplyFactScope scope,
        FrozenFactParameters parameters,
        CancellationToken ct)
    {
        if (scope == null)
            throw new ArgumentNullException(nameof(scope));
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));

        var result = new Position5SupplyResult
        {
            Scope = scope,
            LoadStartTime = DateTime.UtcNow
        };

        try
        {
            var rawFacts = await _loader.LoadRawFactsAsync(scope, ct);
            result.RawFactCount = rawFacts.Count;

            var validFacts = new List<TimedSupplyFact>();
            var issues = new List<Position5Issue>();

            foreach (var rawFact in rawFacts)
            {
                try
                {
                    // 【2026-08-25新基线】5号位只转换原始事实，不计算Effective ETA/AvailableTime
                    // Effective ETA和AvailableTime由2号位计算
                    var supplyFact = ConvertToSupplyFact(rawFact);
                    validFacts.Add(supplyFact);
                }
                catch (InvalidOperationException ex)
                {
                    issues.Add(new Position5Issue
                    {
                        Severity = "WARNING",
                        IssueCode = "F21",
                        PhysicalSourceKey = rawFact.PhysicalSourceKey,
                        MaterialCode = rawFact.MaterialCode,
                        FactoryCode = rawFact.FactoryCode,
                        Message = ex.Message,
                        RawSupplyType = rawFact.SupplyType,
                        DetectedAt = DateTime.UtcNow
                    });
                }
                catch (ArgumentException ex)
                {
                    issues.Add(new Position5Issue
                    {
                        Severity = "WARNING",
                        IssueCode = "F21",
                        PhysicalSourceKey = rawFact.PhysicalSourceKey,
                        MaterialCode = rawFact.MaterialCode,
                        FactoryCode = rawFact.FactoryCode,
                        Message = ex.Message,
                        RawSupplyType = rawFact.SupplyType,
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }

            result.ValidFactCount = validFacts.Count;
            result.TimedSupplyFacts = validFacts;
            result.Issues = issues;
            result.LoadEndTime = DateTime.UtcNow;
            result.Success = true;

            return result;
        }
        catch (Exception ex)
        {
            result.LoadEndTime = DateTime.UtcNow;
            result.Success = false;
            result.ErrorMessage = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// 转换原始采购事实为Supply Fact（仅填充原始字段，不计算Effective ETA/AvailableTime）
    ///
    /// 【2026-08-26审核修正】必须透出ReleaseDate字段供2号位F15兜底使用
    /// 参考：复审报告P0-02
    /// </summary>
    private TimedSupplyFact ConvertToSupplyFact(RawProcurementFact raw)
    {
        return new TimedSupplyFact
        {
            SupplyType = raw.SupplyType,
            PhysicalSourceKey = raw.PhysicalSourceKey,
            MaterialId = raw.MaterialId,
            MaterialCode = raw.MaterialCode,
            FactoryId = raw.FactoryId,
            FactoryCode = raw.FactoryCode,
            WarehouseCode = raw.StorageCode,
            RemainingQty = raw.RemainingQty,
            Eta = raw.Eta,  // ERP原始ETA，不是Effective ETA
            ReleaseDate = raw.ReleaseDate,  // PO发行日期，供2号位F15兜底使用
            AvailableTime = null,  // 不计算，由2号位负责
            CommitmentStatus = raw.CommitmentStatus,
            Confidence = raw.Confidence,
            SourceDocumentNo = raw.SourceDocumentNo,
            SourceDocumentLineNo = raw.SourceDocumentLineNo,
            SourceUpdatedAt = raw.SourceUpdatedAt
        };
    }
}

public class Position5SupplyResult
{
    public SupplyFactScope Scope { get; set; }
    public DateTime LoadStartTime { get; set; }
    public DateTime LoadEndTime { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public int RawFactCount { get; set; }
    public int ValidFactCount { get; set; }
    public IReadOnlyList<TimedSupplyFact> TimedSupplyFacts { get; set; }
    public IReadOnlyList<Position5Issue> Issues { get; set; }

    public TimeSpan Duration => LoadEndTime - LoadStartTime;
    public int InvalidFactCount => RawFactCount - ValidFactCount;
}

public class Position5Issue
{
    public string Severity { get; set; }
    public string IssueCode { get; set; }
    public string PhysicalSourceKey { get; set; }
    public string MaterialCode { get; set; }
    public string FactoryCode { get; set; }
    public string Message { get; set; }
    public string RawSupplyType { get; set; }
    public DateTime DetectedAt { get; set; }
}
