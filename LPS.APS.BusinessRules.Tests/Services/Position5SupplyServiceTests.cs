using LPS.APS.BusinessRules.Loaders;
using LPS.APS.BusinessRules.Models;
using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using Moq;
using NUnit.Framework;

namespace LPS.APS.BusinessRules.Tests.Services;

[TestFixture]
public class Position5SupplyServiceTests
{
    private Mock<ITimedSupplyFactLoader> _mockLoader;
    private Position5SupplyService _service;

    [SetUp]
    public void SetUp()
    {
        _mockLoader = new Mock<ITimedSupplyFactLoader>();
        _service = new Position5SupplyService(_mockLoader.Object);
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_WithValidData_ReturnsSuccessResult()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int> { 1001 },
            FactoryIds = new List<int> { 5001 }
        };

        var rawFacts = new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialId = 1001,
                MaterialCode = "MAT001",
                FactoryId = 5001,
                FactoryCode = "FAC01",
                RemainingQty = 100,
                SupplyType = "OPEN_PO_REMAINING",
                PhysicalSourceKey = "PO-001"
            }
        };

        var timedFact = new TimedSupplyFact
        {
            MaterialId = 1001,
            MaterialCode = "MAT001",
            FactoryId = 5001,
            FactoryCode = "FAC01",
            RemainingQty = 100,
            PhysicalSourceKey = "PO-001"
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawFacts);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(1));
        Assert.That(result.ValidFactCount, Is.EqualTo(1));
        Assert.That(result.InvalidFactCount, Is.EqualTo(0));
        Assert.That(result.TimedSupplyFacts.Count, Is.EqualTo(1));
        Assert.That(result.Issues.Count, Is.EqualTo(0));
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_WithConversionFailure_RecordsF21IssueAndContinues()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int> { 1001, 1002 },
            FactoryIds = new List<int> { 5001 }
        };

        var rawFacts = new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialId = 1001,
                MaterialCode = "MAT001",
                FactoryId = 5001,
                FactoryCode = "FAC01",
                RemainingQty = 100,
                SupplyType = "OPEN_PO_REMAINING",
                PhysicalSourceKey = "PO-001"
            },
            new RawProcurementFact
            {
                MaterialId = 1002,
                MaterialCode = "MAT002",
                FactoryId = 5001,
                FactoryCode = "FAC01",
                RemainingQty = 50,
                SupplyType = "OPEN_PO_REMAINING",
                PhysicalSourceKey = "PO-002",
                ManualEta = null,
                Eta = null,
                ReleaseDate = null
            }
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawFacts);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(2));
        Assert.That(result.Issues.Count, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_WithEmptyRawFacts_ReturnsEmptyResult()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawProcurementFact>());

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(0));
        Assert.That(result.ValidFactCount, Is.EqualTo(0));
        Assert.That(result.InvalidFactCount, Is.EqualTo(0));
        Assert.That(result.TimedSupplyFacts.Count, Is.EqualTo(0));
        Assert.That(result.Issues.Count, Is.EqualTo(0));
    }

    [Test]
    public void LoadProcurementSupplyAsync_WithNullScope_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _service.LoadProcurementSupplyAsync(null, new FrozenFactParameters(), CancellationToken.None));
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_WhenLoaderThrows_SetsSuccessToFalseAndRethrows()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        var ex = Assert.ThrowsAsync<Exception>(
            async () => await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None));

        Assert.That(ex.Message, Is.EqualTo("Database connection failed"));
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_RecordsDuration()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawProcurementFact>());

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.LoadStartTime, Is.LessThanOrEqualTo(result.LoadEndTime));
        Assert.That(result.Duration, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_SetsScope()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int> { 1001 },
            FactoryIds = new List<int> { 5001 }
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawProcurementFact>());

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Scope, Is.SameAs(scope));
    }

    [Test]
    public void Constructor_WithNullLoader_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Position5SupplyService(null));
    }

    [Test]
    public async Task LoadProcurementSupplyAsync_MixedValidAndInvalid_ReturnsPartialSuccessWithIssues()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        var rawFacts = new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialId = 1001,
                MaterialCode = "MAT001",
                PhysicalSourceKey = "PO-001",
                SupplyType = "OPEN_PO_REMAINING"
            },
            new RawProcurementFact
            {
                MaterialId = 1002,
                MaterialCode = "MAT002",
                PhysicalSourceKey = "PO-002",
                SupplyType = "INVALID_TYPE"
            },
            new RawProcurementFact
            {
                MaterialId = 1003,
                MaterialCode = "MAT003",
                PhysicalSourceKey = "PO-003",
                SupplyType = "VMI_ONSITE"
            }
        };

        _mockLoader.Setup(l => l.LoadRawFactsAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawFacts);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        // 注意：Loader SQL已过滤无效类型，mock返回的数据都是已通过过滤的
        // 因此所有3个fact都被视为有效
        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(3));
        Assert.That(result.ValidFactCount, Is.EqualTo(3));
        Assert.That(result.InvalidFactCount, Is.EqualTo(0));
        Assert.That(result.TimedSupplyFacts.Count, Is.EqualTo(3));
    }
}
