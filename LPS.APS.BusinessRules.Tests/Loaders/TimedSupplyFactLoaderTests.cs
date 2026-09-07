using LPS.APS.BusinessRules.Loaders;
using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using Moq;
using NUnit.Framework;
using System.Data;

namespace LPS.APS.BusinessRules.Tests.Loaders;

[TestFixture]
public class TimedSupplyFactLoaderTests
{
    private Mock<DatabaseConnectionManager> _mockConnectionManager;
    private TimedSupplyFactLoader _loader;

    [SetUp]
    public void SetUp()
    {
        _mockConnectionManager = new Mock<DatabaseConnectionManager>();
        _loader = new TimedSupplyFactLoader(_mockConnectionManager.Object);
    }

    [Test]
    public async Task LoadRawFactsAsync_WithValidData_ReturnsRawProcurementFacts()
    {
        // Arrange
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int> { 1001, 1002 },
            FactoryIds = new List<int> { 5001 }
        };

        var expectedFacts = new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialCode = "MAT001",
                MaterialId = 1001,
                FactoryId = 5001,
                FactoryCode = "FAC01",
                RemainingQty = 100,
                ManualEta = new DateTime(2026, 8, 25),
                Eta = new DateTime(2026, 8, 26),
                ReleaseDate = new DateTime(2026, 8, 15),
                StorageCode = "WH01",
                SupplyType = "OPEN_PO_REMAINING",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-20260815-001",
                SourceDocumentLineNo = "10",
                SourceUpdatedAt = new DateTime(2026, 8, 15, 10, 30, 0)
            }
        };

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(expectedFacts);

        // Act
        var result = await _loader.LoadRawFactsAsync(scope, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].MaterialCode, Is.EqualTo("MAT001"));
        Assert.That(result[0].SupplyType, Is.EqualTo("OPEN_PO_REMAINING"));
        Assert.That(result[0].RemainingQty, Is.EqualTo(100));
    }

    [Test]
    public async Task LoadRawFactsAsync_WithNullMaterialIds_QueryWithoutMaterialFilter()
    {
        // Arrange
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = null,
            FactoryIds = new List<int> { 5001 }
        };

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(new List<RawProcurementFact>());

        // Act
        var result = await _loader.LoadRawFactsAsync(scope, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockConnectionManager.Verify(
            m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()),
            Times.Once);
    }

    [Test]
    public async Task LoadRawFactsAsync_WithEmptyMaterialIds_QueryWithoutMaterialFilter()
    {
        // Arrange
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int>(),
            FactoryIds = new List<int> { 5001 }
        };

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(new List<RawProcurementFact>());

        // Act
        var result = await _loader.LoadRawFactsAsync(scope, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task LoadRawFactsAsync_WithAllSupplyTypes_ReturnsAllTypes()
    {
        // Arrange
        var scope = new SupplyFactScope
        {
            DataCutoffTime = new DateTime(2026, 8, 20),
            MaterialIds = new List<int> { 1001 },
            FactoryIds = new List<int> { 5001 }
        };

        var expectedFacts = new List<RawProcurementFact>
        {
            new RawProcurementFact { SupplyType = "OPEN_PO_REMAINING", RemainingQty = 100, PhysicalSourceKey = "PO1" },
            new RawProcurementFact { SupplyType = "PURCHASE_IN_TRANSIT", RemainingQty = 50, PhysicalSourceKey = "PO2" },
            new RawProcurementFact { SupplyType = "ARRIVED_NOT_RECEIVED", RemainingQty = 30, PhysicalSourceKey = "PO3" }
        };

        _mockConnectionManager
            .Setup(m => m.QueryAsync<RawProcurementFact>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CommandType>(),
                It.IsAny<DatabaseId>(),
                It.IsAny<int?>()))
            .ReturnsAsync(expectedFacts);

        // Act
        var result = await _loader.LoadRawFactsAsync(scope, CancellationToken.None);

        // Assert
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Count(f => f.SupplyType == "OPEN_PO_REMAINING"), Is.EqualTo(1));
        Assert.That(result.Count(f => f.SupplyType == "PURCHASE_IN_TRANSIT"), Is.EqualTo(1));
        Assert.That(result.Count(f => f.SupplyType == "ARRIVED_NOT_RECEIVED"), Is.EqualTo(1));
    }

    [Test]
    public void LoadRawFactsAsync_WithNullConnectionManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TimedSupplyFactLoader(null));
    }
}
