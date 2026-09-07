using LPS.APS.BusinessRules.Calculators;
using LPS.APS.BusinessRules.Loaders;
using LPS.APS.BusinessRules.Models;
using LPS.APS.BusinessRules.Services;
using LPS.APS.BusinessRules.Tests.TestData;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using Moq;
using NUnit.Framework;
using System.Data;

namespace LPS.APS.BusinessRules.Tests.Integration;

[TestFixture]
public class Position5IntegrationTests
{
    private Position5SupplyService _service;
    private Mock<DatabaseConnectionManager> _mockConnectionManager;

    [SetUp]
    public void SetUp()
    {
        _mockConnectionManager = new Mock<DatabaseConnectionManager>();
        var loader = new TimedSupplyFactLoader(_mockConnectionManager.Object);
        _service = new Position5SupplyService(loader);
    }

    [Test]
    public async Task EndToEnd_ValidProcurementFacts_ReturnsAllConverted()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int> { 1001, 1002, 1003, 1004, 1005 },
            FactoryIds = new List<int> { 5001 }
        };

        var mockData = MockProcurementDataBuilder.BuildValidProcurementFacts(5);

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(mockData);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(5));
        Assert.That(result.ValidFactCount, Is.EqualTo(5));
        Assert.That(result.InvalidFactCount, Is.EqualTo(0));
        Assert.That(result.TimedSupplyFacts.Count, Is.EqualTo(5));
        Assert.That(result.Issues.Count, Is.EqualTo(0));
        Assert.That(result.Duration.TotalSeconds, Is.LessThan(1));
    }

    [Test]
    public async Task EndToEnd_MixedQualityFacts_ProcessesValidAndRecordsIssues()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        var mockData = MockProcurementDataBuilder.BuildMixedQualityFacts();

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(mockData);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(4));
        Assert.That(result.ValidFactCount, Is.EqualTo(2));
        Assert.That(result.InvalidFactCount, Is.EqualTo(2));
        Assert.That(result.TimedSupplyFacts.Count, Is.EqualTo(2));
        Assert.That(result.Issues.Count, Is.EqualTo(2));

        Assert.That(result.TimedSupplyFacts[0].PhysicalSourceKey, Is.EqualTo("PO-GOOD-001"));
        Assert.That(result.TimedSupplyFacts[1].PhysicalSourceKey, Is.EqualTo("PO-GOOD-002"));

        var f15Issue = result.Issues.FirstOrDefault(i => i.PhysicalSourceKey == "PO-BAD-F15-001");
        Assert.That(f15Issue, Is.Not.Null);
        Assert.That(f15Issue.IssueCode, Is.EqualTo("F21"));
        Assert.That(f15Issue.Severity, Is.EqualTo("WARNING"));

        var typeIssue = result.Issues.FirstOrDefault(i => i.PhysicalSourceKey == "PO-BAD-TYPE-001");
        Assert.That(typeIssue, Is.Not.Null);
        Assert.That(typeIssue.IssueCode, Is.EqualTo("F21"));
        Assert.That(typeIssue.RawSupplyType, Is.EqualTo("INVALID_SUPPLY_TYPE"));
    }

    [Test]
    public async Task EndToEnd_AllSupplyTypes_MapsCorrectly()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        var mockData = MockProcurementDataBuilder.BuildAllSupplyTypesFacts();

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(mockData);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ValidFactCount, Is.EqualTo(4));
        Assert.That(result.Issues.Count, Is.EqualTo(0));

        var supplyTypes = result.TimedSupplyFacts.Select(f => f.SupplyType).Distinct().ToList();
        Assert.That(supplyTypes.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task EndToEnd_EtaPriorityRules_CalculatesCorrectly()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        var mockData = MockProcurementDataBuilder.BuildEtaPriorityTestFacts();
        var baseDate = new DateTime(2026, 8, 15);

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(mockData);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ValidFactCount, Is.EqualTo(3));

        var manualFact = result.TimedSupplyFacts.First(f => f.MaterialCode == "MAT-MANUAL-ONLY");
        Assert.That(manualFact.AvailableTime, Is.EqualTo(baseDate.AddDays(20)));

        var erpFact = result.TimedSupplyFacts.First(f => f.MaterialCode == "MAT-ERP-ONLY");
        Assert.That(erpFact.AvailableTime, Is.EqualTo(baseDate.AddDays(18)));

        var releaseFact = result.TimedSupplyFacts.First(f => f.MaterialCode == "MAT-RELEASE-ONLY");
        Assert.That(releaseFact.AvailableTime, Is.EqualTo(baseDate.AddDays(14)));
    }

    [Test]
    public async Task Performance_1000ProcurementFacts_CompletesWithinTarget()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        var mockData = MockProcurementDataBuilder.BuildPerformanceTestData(1000);

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(mockData);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawFactCount, Is.EqualTo(1000));
        Assert.That(result.ValidFactCount, Is.EqualTo(1000));
        Assert.That(result.Duration.TotalSeconds, Is.LessThan(5),
            $"Performance target not met: {result.Duration.TotalSeconds:F2}s (target: <5s)");
    }

    [Test]
    public async Task EndToEnd_EmptyScope_ReturnsEmptyResult()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
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
    public async Task EndToEnd_VerifyFieldMapping_AllFieldsPreserved()
    {
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20)
        };

        var testDate = new DateTime(2026, 8, 15, 14, 30, 0);
        var mockData = new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialCode = "MAT-TEST-999",
                MaterialId = 9999,
                FactoryId = 8888,
                FactoryCode = "TEST",
                RemainingQty = 12345.67m,
                ManualEta = testDate.AddDays(10),
                Eta = testDate.AddDays(15),
                ReleaseDate = testDate,
                StorageCode = "WH-TEST",
                SupplyType = "VMI_ONSITE",
                CommitmentStatus = "TENTATIVE",
                Confidence = "MEDIUM",
                PhysicalSourceKey = "TEST-KEY-001",
                SourceDocumentLineNo = "999",
                SourceUpdatedAt = testDate
            }
        };

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(mockData);

        var result = await _service.LoadProcurementSupplyAsync(scope, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ValidFactCount, Is.EqualTo(1));

        var fact = result.TimedSupplyFacts[0];
        Assert.That(fact.MaterialCode, Is.EqualTo("MAT-TEST-999"));
        Assert.That(fact.MaterialId, Is.EqualTo(9999));
        Assert.That(fact.FactoryId, Is.EqualTo(8888));
        Assert.That(fact.FactoryCode, Is.EqualTo("TEST"));
        Assert.That(fact.RemainingQty, Is.EqualTo(12345.67m));
        Assert.That(fact.AvailableTime, Is.EqualTo(testDate.AddDays(10)));
        Assert.That(fact.WarehouseCode, Is.EqualTo("WH-TEST"));
        Assert.That(fact.CommitmentStatus, Is.EqualTo("TENTATIVE"));
        Assert.That(fact.Confidence, Is.EqualTo("MEDIUM"));
        Assert.That(fact.PhysicalSourceKey, Is.EqualTo("TEST-KEY-001"));
        Assert.That(fact.SourceDocumentLineNo, Is.EqualTo("999"));
        Assert.That(fact.SourceUpdatedAt, Is.EqualTo(testDate));
    }
}
