using LPS.APS.BusinessRules.Calculators;
using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;
using NUnit.Framework;

namespace LPS.APS.BusinessRules.Tests.Calculators;

/// <summary>
/// TimedSupplyFactCalculator测试
/// 覆盖F13-F18、F20业务场景
/// </summary>
public sealed class TimedSupplyFactCalculatorTests
{
    private readonly TimedSupplyFactCalculator _calculator = new();

    #region F13: Manual ETA优先

    [Test]
    public void F13_ManualETA_ShouldTakePriority()
    {
        // Arrange
        var raw = new RawProcurementFact
        {
            SupplyType = "OPEN_PO_REMAINING",
            PhysicalSourceKey = "PO-2026-001",
            MaterialId = 101,
            MaterialCode = "MAT-101",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH01",
            RemainingQty = 500,
            ManualEta = new DateTime(2026, 8, 25, 14, 0, 0),  // 人工ETA
            Eta = new DateTime(2026, 8, 28, 9, 0, 0),      // ERP ETA
            ReleaseDate = new DateTime(2026, 7, 20),
            CommitmentStatus = "COMMITTED",
            Confidence = "CONFIRMED",
            SourceDocumentNo = "PO-2026-001",
            SourceDocumentLineNo = "10",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int> { ["WH01"] = 8 }  // 8小时
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F13验收 - 人工ETA优先
        Assert.That(result.Eta, Is.EqualTo(new DateTime(2026, 8, 25, 14, 0, 0)));

        // F17验收 - 含8小时偏移
        Assert.That(result.AvailableTime, Is.EqualTo(new DateTime(2026, 8, 25, 22, 0, 0)));

        // 其他字段验证
        Assert.That(result.SupplyType, Is.EqualTo("OPEN_PO_REMAINING"));
        Assert.That(result.PhysicalSourceKey, Is.EqualTo("PO-2026-001"));
        Assert.That(result.RemainingQty, Is.EqualTo(500));
    }

    #endregion

    #region F14: 人工ETA取消回退ERP ETA

    [Test]
    public void F14_ManualETA_CancelledShouldFallbackToErpETA()
    {
        // Arrange: ManualEta = null 表示已取消（V1简化方案）
        var raw = new RawProcurementFact
        {
            SupplyType = "OPEN_PO_REMAINING",
            PhysicalSourceKey = "PO-2026-002",
            MaterialId = 102,
            MaterialCode = "MAT-102",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH01",
            RemainingQty = 300,
            ManualEta = null,  // 人工ETA已取消
            Eta = new DateTime(2026, 8, 28, 9, 0, 0),  // 回退到ERP ETA
            ReleaseDate = new DateTime(2026, 7, 25),
            CommitmentStatus = "COMMITTED",
            Confidence = "ESTIMATED",
            SourceDocumentNo = "PO-2026-002",
            SourceDocumentLineNo = "20",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int> { ["WH01"] = 8 }
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F14验收 - 回退到ERP ETA
        Assert.That(result.Eta, Is.EqualTo(new DateTime(2026, 8, 28, 9, 0, 0)));
        Assert.That(result.AvailableTime, Is.EqualTo(new DateTime(2026, 8, 28, 17, 0, 0)));
    }

    #endregion

    #region F15: ERP ETA为空时用PO Release Date + DefaultLT

    [Test]
    public void F15_MissingErpETA_ShouldUseReleaseDatePlusDefaultLT()
    {
        // Arrange: 设置referenceTime早于计算出的默认ETA，避免触发F16逾期逻辑
        var releaseDate = new DateTime(2026, 7, 15);
        var raw = new RawProcurementFact
        {
            SupplyType = "OPEN_PO_REMAINING",
            PhysicalSourceKey = "PO-2026-003",
            MaterialId = 103,
            MaterialCode = "MAT-103",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH02",
            RemainingQty = 200,
            ManualEta = null,  // 无人工ETA
            Eta = null,     // 无ERP ETA
            ReleaseDate = releaseDate,  // 基准日期
            CommitmentStatus = "NOT_COMMITTED",
            Confidence = "ESTIMATED",
            SourceDocumentNo = "PO-2026-003",
            SourceDocumentLineNo = "30",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,  // 30天提前期
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int> { ["WH02"] = 4 }  // 4小时
        };

        var referenceTime = new DateTime(2026, 8, 10);  // 早于默认ETA，不触发F16

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F15验收 - ReleaseDate + DefaultLT
        var expectedEta = releaseDate.AddDays(30);  // 2026-08-14
        Assert.That(result.Eta, Is.EqualTo(expectedEta));
        Assert.That(result.AvailableTime, Is.EqualTo(expectedEta.AddHours(4)));
    }

    #endregion

    #region F16: Default ETA过期应用Margin

    [Test]
    public void F16_OverdueDefaultETA_ShouldApplyMargin()
    {
        // Arrange: ReleaseDate + DefaultLT落在referenceTime之前
        var releaseDate = new DateTime(2026, 7, 1);  // 很早的Release日期
        var raw = new RawProcurementFact
        {
            SupplyType = "OPEN_PO_REMAINING",
            PhysicalSourceKey = "PO-2026-004",
            MaterialId = 104,
            MaterialCode = "MAT-104",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH01",
            RemainingQty = 100,
            ManualEta = null,
            Eta = null,
            ReleaseDate = releaseDate,
            CommitmentStatus = "NOT_COMMITTED",
            Confidence = "ESTIMATED",
            SourceDocumentNo = "PO-2026-004",
            SourceDocumentLineNo = "40",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 5,  // 5天容差
            ArrivalToUsableOffsets = new Dictionary<string, int> { ["WH01"] = 8 }
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F16验收 - 过期ETA应用Margin
        // ReleaseDate + DefaultLT = 2026-07-31 (过期)
        // 应用Margin: 2026-07-31 + 5天 = 2026-08-05 (还是过期)
        // 最终返回referenceTime
        Assert.That(result.Eta, Is.EqualTo(referenceTime));
    }

    [Test]
    public void F16_OverdueDefaultETA_MarginBringsItCurrent_ShouldUseMarginedETA()
    {
        // Arrange: Margin加完后不过期
        var releaseDate = new DateTime(2026, 7, 10);
        var raw = new RawProcurementFact
        {
            SupplyType = "OPEN_PO_REMAINING",
            PhysicalSourceKey = "PO-2026-005",
            MaterialId = 105,
            MaterialCode = "MAT-105",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH01",
            RemainingQty = 150,
            ManualEta = null,
            Eta = null,
            ReleaseDate = releaseDate,
            CommitmentStatus = "NOT_COMMITTED",
            Confidence = "ESTIMATED",
            SourceDocumentNo = "PO-2026-005",
            SourceDocumentLineNo = "50",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 15,  // 15天容差
            ArrivalToUsableOffsets = new Dictionary<string, int>()
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert
        // ReleaseDate + DefaultLT = 2026-08-09 (过期11天)
        // 应用Margin: 2026-08-09 + 15天 = 2026-08-24 (不过期)
        var expectedEta = releaseDate.AddDays(30).AddDays(15);
        Assert.That(result.Eta, Is.EqualTo(expectedEta));
    }

    #endregion

    #region F17: Arrived含Warehouse Offset

    [Test]
    public void F17_ArrivedSupply_ShouldIncludeWarehouseOffset()
    {
        // Arrange
        var raw = new RawProcurementFact
        {
            SupplyType = "ARRIVED_NOT_RECEIVED",
            PhysicalSourceKey = "ARR-2026-001",
            MaterialId = 106,
            MaterialCode = "MAT-106",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH03",
            RemainingQty = 250,
            ManualEta = null,
            Eta = new DateTime(2026, 8, 22, 10, 0, 0),
            ReleaseDate = null,
            CommitmentStatus = "COMMITTED",
            Confidence = "CONFIRMED",
            SourceDocumentNo = "ARR-2026-001",
            SourceDocumentLineNo = "10",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int>
            {
                ["WH01"] = 8,
                ["WH02"] = 4,
                ["WH03"] = 12  // WH03需要12小时Inspection/Inbound
            }
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F17验收 - ETA + 12小时偏移
        Assert.That(result.Eta, Is.EqualTo(new DateTime(2026, 8, 22, 10, 0, 0)));
        Assert.That(result.AvailableTime, Is.EqualTo(new DateTime(2026, 8, 22, 22, 0, 0)));
    }

    [Test]
    public void F17_WarehouseWithNoOffset_ShouldUseETADirectly()
    {
        // Arrange: 仓库未配置偏移
        var raw = new RawProcurementFact
        {
            SupplyType = "ARRIVED_NOT_RECEIVED",
            PhysicalSourceKey = "ARR-2026-002",
            MaterialId = 107,
            MaterialCode = "MAT-107",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH99",  // 未配置的仓库
            RemainingQty = 80,
            ManualEta = null,
            Eta = new DateTime(2026, 8, 23, 15, 30, 0),
            ReleaseDate = null,
            CommitmentStatus = "COMMITTED",
            Confidence = "CONFIRMED",
            SourceDocumentNo = "ARR-2026-002",
            SourceDocumentLineNo = "20",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int>
            {
                ["WH01"] = 8,
                ["WH02"] = 4
                // WH99未配置
            }
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: 无偏移，AvailableTime = ETA
        Assert.That(result.Eta, Is.EqualTo(new DateTime(2026, 8, 23, 15, 30, 0)));
        Assert.That(result.AvailableTime, Is.EqualTo(new DateTime(2026, 8, 23, 15, 30, 0)));
    }

    #endregion

    #region F18: VMI独立SupplyType

    [Test]
    public void F18_VMI_ShouldBeIndependentSupplyType()
    {
        // Arrange
        var raw = new RawProcurementFact
        {
            SupplyType = "VMI_ONSITE",  // F18验收：VMI保持独立SupplyType
            PhysicalSourceKey = "VMI-WH-CN-001",
            MaterialId = 108,
            MaterialCode = "MAT-108",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "VMI-WH",
            RemainingQty = 500,
            ManualEta = null,
            Eta = new DateTime(2026, 8, 21, 8, 0, 0),  // VMI有ETA
            ReleaseDate = null,
            CommitmentStatus = "COMMITTED",
            Confidence = "CONFIRMED",
            SourceDocumentNo = "VMI-DOC-001",
            SourceDocumentLineNo = "1",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int>()
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F18验收 - VMI保持独立类型，按真实AvailableTime消费
        Assert.That(result.SupplyType, Is.EqualTo("VMI_ONSITE"));
        Assert.That(result.PhysicalSourceKey, Is.EqualTo("VMI-WH-CN-001"));
        Assert.That(result.Eta, Is.EqualTo(new DateTime(2026, 8, 21, 8, 0, 0)));
        Assert.That(result.AvailableTime, Is.EqualTo(new DateTime(2026, 8, 21, 8, 0, 0)));
    }

    #endregion

    #region F20: 真实Supply全空返回真实空

    [Test]
    public void F20_EmptySupply_ShouldReturnEmptyNotPlaceholder()
    {
        // Arrange: 所有ETA字段都为null
        var raw = new RawProcurementFact
        {
            SupplyType = "OPEN_PO_REMAINING",
            PhysicalSourceKey = "PO-2026-999",
            MaterialId = 999,
            MaterialCode = "MAT-999",
            FactoryId = 1,
            FactoryCode = "CN",
            StorageCode = "WH01",
            RemainingQty = 0,  // 数量也为0
            ManualEta = null,
            Eta = null,
            ReleaseDate = null,  // 连ReleaseDate都没有
            CommitmentStatus = "NOT_COMMITTED",
            Confidence = "ESTIMATED",
            SourceDocumentNo = "PO-2026-999",
            SourceDocumentLineNo = "999",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 30,
            OverdueMargin = 2,
            ArrivalToUsableOffsets = new Dictionary<string, int>()
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: F20验收 - 返回真实空（ETA=null, AvailableTime=null）
        // 不生成Placeholder
        Assert.That(result.Eta, Is.Null);
        Assert.That(result.AvailableTime, Is.Null);
        Assert.That(result.RemainingQty, Is.EqualTo(0));
        Assert.That(result.PhysicalSourceKey, Is.EqualTo("PO-2026-999"));  // SourceKey仍保留
    }

    #endregion

    #region 综合场景：多种优先级混合

    [Test]
    public void Integration_ManualETA_OverridesEverything()
    {
        // Arrange: 同时有ManualETA, ErpETA, ReleaseDate
        var raw = new RawProcurementFact
        {
            SupplyType = "PURCHASE_IN_TRANSIT",
            PhysicalSourceKey = "PO-INT-001",
            MaterialId = 201,
            MaterialCode = "MAT-201",
            FactoryId = 2,
            FactoryCode = "BJ",
            StorageCode = "WH01",
            RemainingQty = 1000,
            ManualEta = new DateTime(2026, 9, 1, 10, 0, 0),   // 人工ETA
            Eta = new DateTime(2026, 9, 5, 14, 0, 0),      // ERP ETA
            ReleaseDate = new DateTime(2026, 7, 15),           // Release Date
            CommitmentStatus = "COMMITTED",
            Confidence = "CONFIRMED",
            SourceDocumentNo = "PO-INT-001",
            SourceDocumentLineNo = "1",
            SourceUpdatedAt = DateTime.Now
        };

        var parameters = new FrozenFactParameters
        {
            StrategyProfileVersionId = 1,
            DefaultPurchaseLt = 45,
            OverdueMargin = 3,
            ArrivalToUsableOffsets = new Dictionary<string, int> { ["WH01"] = 6 }
        };

        var referenceTime = new DateTime(2026, 8, 20);

        // Act
        var result = _calculator.CalculateEffectiveSupply(raw, parameters, referenceTime);

        // Assert: ManualETA优先，忽略其他所有来源
        Assert.That(result.Eta, Is.EqualTo(new DateTime(2026, 9, 1, 10, 0, 0)));
        Assert.That(result.AvailableTime, Is.EqualTo(new DateTime(2026, 9, 1, 16, 0, 0)));
    }

    #endregion
}
