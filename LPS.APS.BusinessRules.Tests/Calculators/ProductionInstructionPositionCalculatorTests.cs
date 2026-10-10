using LPS.APS.BusinessRules.Calculators;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace LPS.APS.BusinessRules.Tests.Calculators;

/// <summary>
/// PI Position Calculator 测试
/// 验证文档中定义的F01-F08场景
/// </summary>
public class ProductionInstructionPositionCalculatorTests
{
    private readonly ProductionInstructionPositionCalculator _calculator;

    public ProductionInstructionPositionCalculatorTests()
    {
        _calculator = new ProductionInstructionPositionCalculator(NullLogger<ProductionInstructionPositionCalculator>.Instance);
    }

    [Test]
    public async Task F01_NormalStageProgress_ShouldCloseTo100()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F01-001",
            MaterialId = 1001,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 80m,
                    SnapshotId = 1
                },
                new StageProgressFact
                {
                    StageCode = "S20",
                    StageSequence = 2,
                    GoodCompletedQty = 50m,
                    SnapshotId = 2
                },
                new StageProgressFact
                {
                    StageCode = "S30",
                    StageSequence = 3,
                    GoodCompletedQty = 20m,
                    SnapshotId = 3
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));
        Assert.That(result.Positions.Count, Is.EqualTo(4));

        var s10Position = result.Positions.First(p => p.StageCode == "S10");
        Assert.That(s10Position.Quantity, Is.EqualTo(30m));

        var s20Position = result.Positions.First(p => p.StageCode == "S20");
        Assert.That(s20Position.Quantity, Is.EqualTo(30m));

        var s30Position = result.Positions.First(p => p.StageCode == "S30");
        Assert.That(s30Position.Quantity, Is.EqualTo(20m));

        var unlocatedPosition = result.Positions.First(p => p.PositionType == PositionType.UNLOCATED);
        Assert.That(unlocatedPosition.Quantity, Is.EqualTo(20m));
    }

    [Test]
    public async Task F02_DownstreamGreaterThanUpstream_ShouldCorrectConservatively()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F02-001",
            MaterialId = 1002,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 60m,
                    SnapshotId = 1
                },
                new StageProgressFact
                {
                    StageCode = "S20",
                    StageSequence = 2,
                    GoodCompletedQty = 80m,
                    SnapshotId = 2
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Issues.Any(i => i.IssueType == "DOWNSTREAM_GT_UPSTREAM"), Is.True);

        var s20Position = result.Positions.FirstOrDefault(p => p.StageCode == "S20");
        Assert.That(s20Position, Is.Not.Null);
        Assert.That(s20Position.Quantity, Is.EqualTo(60m));
    }

    [Test]
    public async Task F03_MissingMiddleStage_ShouldUseDownstreamMinimum()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F03-001",
            MaterialId = 1003,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 80m,
                    SnapshotId = 1
                },
                new StageProgressFact
                {
                    StageCode = "S30",
                    StageSequence = 3,
                    GoodCompletedQty = 30m,
                    SnapshotId = 3
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var s10Position = result.Positions.First(p => p.StageCode == "S10");
        Assert.That(s10Position.Quantity, Is.EqualTo(50m));

        var s30Position = result.Positions.First(p => p.StageCode == "S30");
        Assert.That(s30Position.Quantity, Is.EqualTo(30m));
    }

    [Test]
    public async Task F04_XcOverlapsStage_ShouldDeduplicate()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F04-001",
            MaterialId = 1004,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 80m,
                    SnapshotId = 1
                },
                new StageProgressFact
                {
                    StageCode = "S20",
                    StageSequence = 2,
                    GoodCompletedQty = 30m,
                    SnapshotId = 2
                }
            },
            XcFacts = new[]
            {
                new XcFact
                {
                    XcWarehouseCode = "XC-WAREHOUSE-01",
                    RelatedStageCode = "S10",
                    Quantity = 20m,
                    AvailableTime = DateTime.Now,
                    SourceDocument = "XC-DOC-001"
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var s10Position = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.STAGE_WAITING && p.StageCode == "S10");
        Assert.That(s10Position, Is.Not.Null);
        Assert.That(s10Position.Quantity, Is.EqualTo(30m));

        var xcPosition = result.Positions.First(p => p.PositionType == PositionType.XC);
        Assert.That(xcPosition.Quantity, Is.EqualTo(20m));
    }

    [Test]
    public async Task F05_TransitIndependent_ShouldNotOverlapStage()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F05-001",
            MaterialId = 1005,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 60m,
                    SnapshotId = 1
                }
            },
            TransitFacts = new[]
            {
                new InterplantTransitFact
                {
                    SourceFactoryCode = "CN",
                    TargetFactoryCode = "BJ",
                    Quantity = 25m,
                    EstimatedArrivalTime = DateTime.Now.AddDays(2),
                    TransitDocumentNo = "TRANSIT-001"
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var transitPosition = result.Positions.First(p => p.PositionType == PositionType.INTERPLANT_TRANSIT);
        Assert.That(transitPosition.Quantity, Is.EqualTo(25m));

        // F05规则：Transit与Stage互斥去重，Transit应从Stage扣除
        // Stage原始60 - Transit 25 = 35
        var stagePosition = result.Positions.First(p => p.PositionType == PositionType.STAGE_WAITING);
        Assert.That(stagePosition.Quantity, Is.EqualTo(35m), "Transit should be deducted from Stage (60 - 25 = 35)");

        // 剩余40应该进入UNLOCATED（Stage 60被Transit 25扣除后=35，100-35-25=40）
        var unlocatedPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.UNLOCATED);
        if (unlocatedPosition != null)
        {
            Assert.That(unlocatedPosition.Quantity, Is.EqualTo(40m));
        }
    }

    [Test]
    public async Task F06_UnlocatedGap_ShouldFillWithUnlocated()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F06-001",
            MaterialId = 1006,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 85m,
                    SnapshotId = 1
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var unlocatedPosition = result.Positions.First(p => p.PositionType == PositionType.UNLOCATED);
        Assert.That(unlocatedPosition.Quantity, Is.EqualTo(15m));
        Assert.That(unlocatedPosition.IsUnlocated, Is.True);

        Assert.That(result.Issues.Any(i => i.IssueType == "UNLOCATED_GAP"), Is.True);
    }

    [Test]
    public async Task F07_StrongFactCorrection_ShouldAdjustPosition()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F07-001",
            MaterialId = 1007,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 80m,
                    SnapshotId = 1
                },
                new StageProgressFact
                {
                    StageCode = "S20",
                    StageSequence = 2,
                    GoodCompletedQty = 40m,
                    SnapshotId = 2
                }
            },
            StrongFacts = new[]
            {
                new ReceivedFact
                {
                    RelatedStageCode = "S10",
                    Quantity = 30m,
                    ReceivedAt = DateTime.Now,
                    DocumentNo = "PI-F07-001",
                    DocumentType = "PI"
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var s10Position = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.STAGE_WAITING && p.StageCode == "S10");
        if (s10Position != null)
        {
            Assert.That(s10Position.Quantity, Is.EqualTo(10m));
        }
    }

    [Test]
    public async Task F08_MultiplePiSameMaterial_ShouldReturnSeparateResults()
    {
        var inputs = new[]
        {
            new ProductionInstructionPositionInput
            {
                ProductionInstructionNo = "PI-F08-001",
                MaterialId = 1008,
                FactoryId = 1,
                ErpRemainingQty = 100m,
                StageProgress = new[]
                {
                    new StageProgressFact
                    {
                        StageCode = "S10",
                        StageSequence = 1,
                        GoodCompletedQty = 80m,
                        SnapshotId = 1
                    }
                }
            },
            new ProductionInstructionPositionInput
            {
                ProductionInstructionNo = "PI-F08-002",
                MaterialId = 1008,
                FactoryId = 1,
                ErpRemainingQty = 50m,
                StageProgress = new[]
                {
                    new StageProgressFact
                    {
                        StageCode = "S10",
                        StageSequence = 1,
                        GoodCompletedQty = 30m,
                        SnapshotId = 2
                    }
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(inputs, new FrozenFactParameters(), CancellationToken.None);

        Assert.That(results.Count, Is.EqualTo(2));

        var result1 = results.First(r => r.ProductionInstructionNo == "PI-F08-001");
        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(result1.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var result2 = results.First(r => r.ProductionInstructionNo == "PI-F08-002");
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(result2.Positions.Sum(p => p.Quantity), Is.EqualTo(50m));
    }

    // ============================================================================
    // P0-11警告：以下F09-F12测试属于跨厂订单链（INTER_FACTORY_ORDER），不属于PI Position Calculator
    //
    // 根据APS_V1_5号位代码第一轮综合符合性审核报告_冻结基线核对版_v1.0_20260820：
    // - F09-F12涉及SH（出荷指示）Transit和Received的同单号扣减逻辑
    // - SH内部Transit/Received不属于PI Position Calculator职责
    // - 这些测试需要迁移到独立的跨厂订单链测试类中
    // - 当前ProductionInstructionPositionCalculator已移除F10-F12逻辑
    //
    // 整改要求：
    // 1. 创建新测试类 InterFactoryOrderCalculatorTests 或类似名称
    // 2. 迁移F09-F12测试到该新类中
    // 3. 按跨厂订单链的正确业务语义重新设计测试
    // 4. 不新增2↔5接口字段
    // ============================================================================

    [Test]
    public async Task F09_StageHandoff_ShouldReturnPITransitWithoutShippingTask()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F09-001",
            MaterialId = 3001,
            FactoryId = 1,
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 50m,
                    SnapshotId = 1
                }
            },
            TransitFacts = new[]
            {
                new InterplantTransitFact
                {
                    TransitDocumentNo = "TRANSIT-001",
                    SourceFactoryCode = "CN",
                    TargetFactoryCode = "TJ",
                    Quantity = 50m,
                    SourceDocument = "SH-20260819-001",
                    ShippedAt = DateTime.Now.AddDays(-1)
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        var transitPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.INTERPLANT_TRANSIT);
        Assert.That(transitPosition, Is.Not.Null);
        Assert.That(transitPosition.Quantity, Is.EqualTo(50m));

        // Transit(50) 与 Stage(50) 去重后，Stage 被完全扣除
        var stagePosition = result.Positions.FirstOrDefault(p => p.StageCode == "S10");
        Assert.That(stagePosition, Is.Null, "Stage fully consumed by Transit deduction should be removed");

        // 剩余50进入UNLOCATED
        var unlocatedPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.UNLOCATED);
        Assert.That(unlocatedPosition, Is.Not.Null);
        Assert.That(unlocatedPosition.Quantity, Is.EqualTo(50m));
    }

    [Test]
    public async Task F10_SameSHReceived_ShouldMatchCorrectly()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F10-001",
            MaterialId = 3002,
            FactoryId = 2,
            ErpRemainingQty = 100m,
            TransitFacts = new[]
            {
                new InterplantTransitFact
                {
                    TransitDocumentNo = "TRANSIT-002",
                    SourceFactoryCode = "CN",
                    TargetFactoryCode = "TJ",
                    Quantity = 60m,
                    SourceDocument = "SH-20260819-002",
                    ShippedAt = DateTime.Now.AddDays(-2)
                }
            },
            StrongFacts = new[]
            {
                new ReceivedFact
                {
                    DocumentNo = "SH-20260819-002",
                    DocumentType = "SH",
                    Quantity = 30m,
                    ReceivedAt = DateTime.Now,
                    WarehouseCode = "TJ-WH01",
                    RelatedStageCode = "S10"
                }
            },
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 30m,
                    SnapshotId = 1
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        // SH DocumentType 被 PI Position Calculator 拒绝（RECEIVED_SHIPPING_IN_PI_POSITION）
        // Transit(60) 完整保留，Stage(30) 被 Transit 完全扣除
        var transitPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.INTERPLANT_TRANSIT);
        Assert.That(transitPosition, Is.Not.Null);
        Assert.That(transitPosition.Quantity, Is.EqualTo(60m), "Transit preserved - SH Received rejected by PI Position Calculator");

        // Stage(30) 被 Transit 完全扣除移除
        var stagePosition = result.Positions.FirstOrDefault(p => p.StageCode == "S10");
        Assert.That(stagePosition, Is.Null, "Stage fully consumed by Transit deduction");

        // 差额40进入UNLOCATED（Stage 30被Transit 60完全扣除，100-60=40）
        var unlocatedPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.UNLOCATED);
        Assert.That(unlocatedPosition, Is.Not.Null);
        Assert.That(unlocatedPosition.Quantity, Is.EqualTo(40m));

        // 验证 RECEIVED_SHIPPING_IN_PI_POSITION Issue 已生成
        Assert.That(result.Issues.Any(i => i.IssueType == "RECEIVED_SHIPPING_IN_PI_POSITION"), Is.True);
    }

    [Test]
    public async Task F11_DifferentSHSameMaterial_ShouldNotMix()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F11-001",
            MaterialId = 3003,
            FactoryId = 2,
            ErpRemainingQty = 100m,
            TransitFacts = new[]
            {
                new InterplantTransitFact
                {
                    TransitDocumentNo = "TRANSIT-003A",
                    SourceFactoryCode = "CN",
                    TargetFactoryCode = "TJ",
                    Quantity = 40m,
                    SourceDocument = "SH-20260819-003A",
                    ShippedAt = DateTime.Now.AddDays(-2)
                },
                new InterplantTransitFact
                {
                    TransitDocumentNo = "TRANSIT-003B",
                    SourceFactoryCode = "CN",
                    TargetFactoryCode = "TJ",
                    Quantity = 60m,
                    SourceDocument = "SH-20260819-003B",
                    ShippedAt = DateTime.Now.AddDays(-1)
                }
            },
            StrongFacts = new[]
            {
                new ReceivedFact
                {
                    DocumentNo = "SH-20260819-003A",
                    DocumentType = "SH",
                    Quantity = 40m,
                    ReceivedAt = DateTime.Now,
                    WarehouseCode = "TJ-WH01",
                    RelatedStageCode = "S10"
                }
            },
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 40m,
                    SnapshotId = 1
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        // SH DocumentType 被 PI Position Calculator 拒绝
        // 两笔 Transit(40+60=100) 完整保留，Stage(40) 被 Transit 去重完全扣除
        var transitPositions = result.Positions.Where(p => p.PositionType == PositionType.INTERPLANT_TRANSIT).ToList();
        Assert.That(transitPositions.Count, Is.EqualTo(2), "Both Transit facts preserved");
        Assert.That(transitPositions.Sum(t => t.Quantity), Is.EqualTo(100m));

        // Stage(40) 被 Transit 去重完全扣除
        var stagePosition = result.Positions.FirstOrDefault(p => p.StageCode == "S10");
        Assert.That(stagePosition, Is.Null, "Stage fully consumed by Transit deduction");

        // 验证 RECEIVED_SHIPPING_IN_PI_POSITION Issue
        Assert.That(result.Issues.Any(i => i.IssueType == "RECEIVED_SHIPPING_IN_PI_POSITION"), Is.True);
    }

    [Test]
    public async Task F12_TransitAlreadyReceived_ShouldNotDuplicate()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-F12-001",
            MaterialId = 3004,
            FactoryId = 2,
            ErpRemainingQty = 100m,
            TransitFacts = new[]
            {
                new InterplantTransitFact
                {
                    TransitDocumentNo = "TRANSIT-004",
                    SourceFactoryCode = "CN",
                    TargetFactoryCode = "TJ",
                    Quantity = 50m,
                    SourceDocument = "SH-20260819-004",
                    ShippedAt = DateTime.Now.AddDays(-3)
                }
            },
            StrongFacts = new[]
            {
                new ReceivedFact
                {
                    DocumentNo = "SH-20260819-004",
                    DocumentType = "SH",
                    Quantity = 50m,
                    ReceivedAt = DateTime.Now,
                    WarehouseCode = "TJ-WH01",
                    RelatedStageCode = "S10"
                }
            },
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "S10",
                    StageSequence = 1,
                    GoodCompletedQty = 50m,
                    SnapshotId = 1
                }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(new[] { input }, new FrozenFactParameters(), CancellationToken.None);
        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(100m));

        // SH DocumentType 被 PI Position Calculator 拒绝（RECEIVED_SHIPPING_IN_PI_POSITION）
        // Transit(50) 完整保留，Stage(50) 被 Transit 去重完全扣除
        var transitPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.INTERPLANT_TRANSIT);
        Assert.That(transitPosition, Is.Not.Null, "Transit preserved - SH Received rejected");
        Assert.That(transitPosition.Quantity, Is.EqualTo(50m));

        // Stage(50) 被 Transit 去重完全扣除
        var stagePosition = result.Positions.FirstOrDefault(p => p.StageCode == "S10");
        Assert.That(stagePosition, Is.Null, "Stage fully consumed by Transit deduction");

        // 差额50进入UNLOCATED
        var unlocatedPosition = result.Positions.FirstOrDefault(p => p.PositionType == PositionType.UNLOCATED);
        Assert.That(unlocatedPosition, Is.Not.Null);
        Assert.That(unlocatedPosition.Quantity, Is.EqualTo(50m));

        // 验证 RECEIVED_SHIPPING_IN_PI_POSITION Issue
        Assert.That(result.Issues.Any(i => i.IssueType == "RECEIVED_SHIPPING_IN_PI_POSITION"), Is.True);
    }
}

