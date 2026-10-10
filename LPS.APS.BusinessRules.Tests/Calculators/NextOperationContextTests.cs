using LPS.APS.BusinessRules.Calculators;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace LPS.APS.BusinessRules.Tests.Calculators;

/// <summary>
/// NextOperationContext测试
/// 验证PI执行起点上下文计算逻辑
/// </summary>
public class NextOperationContextTests
{
    private readonly ProductionInstructionPositionCalculator _calculator;

    public NextOperationContextTests()
    {
        _calculator = new ProductionInstructionPositionCalculator(NullLogger<ProductionInstructionPositionCalculator>.Instance);
    }

    /// <summary>
    /// 唯一 frontier 判定（39-0 §四.1）
    /// NC 已开工未完成（GoodQty=800>0, RemainingQty=200>0），挤丝未开工（GoodQty=0, RemainingQty=1000>0）
    /// 期望：1个切片，StartOperation = NC（唯一已开工未完成的工序），整量 1000
    /// </summary>
    [Test]
    public async Task NextOp_MultiSlice_SplitsCorrectly()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-MULTI-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 1000m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 1000m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            OperationProgress = new[]
            {
                new OperationProgressFact
                {
                    OperationCode = "NC001",
                    OperationName = "NC",
                    StageCode = "CN_MACHINING",
                    GoodQty = 800m,
                    RemainingQty = 200m,
                    OperationSequence = 1
                },
                new OperationProgressFact
                {
                    OperationCode = "EXTRUDE001",
                    OperationName = "挤丝",
                    StageCode = "CN_MACHINING",
                    GoodQty = 0m,
                    RemainingQty = 1000m,
                    OperationSequence = 2
                }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(1000m));

        // 39-0：唯一 frontier → 整量给该工序（不再按 OperationSequence 级联拆分）
        Assert.That(result.NextOperationContexts.Count, Is.EqualTo(1));

        var ncSlice = result.NextOperationContexts.First();
        Assert.That(ncSlice.StartOperationCode, Is.EqualTo("NC"));
        Assert.That(ncSlice.SliceQty, Is.EqualTo(1000m));
        Assert.That(ncSlice.StartStageCode, Is.EqualTo("CN_MACHINING"));
    }

    /// <summary>
    /// 测试无OperationProgress时只输出StartStageCode
    /// </summary>
    [Test]
    public async Task NextOp_NoOperationProgress_OutputStartStageOnly()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-NOOP-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 100m,  // 所有100件都在这个Stage
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.NextOperationContexts.Count, Is.EqualTo(1));
        Assert.That(result.NextOperationContexts[0].StartStageCode, Is.EqualTo("CN_MACHINING"));
        Assert.That(result.NextOperationContexts[0].StartOperationCode, Is.Null);
        Assert.That(result.NextOperationContexts[0].SliceQty, Is.EqualTo(100m));
    }

    /// <summary>
    /// 测试UNLOCATED时IsUnlocated=true
    /// </summary>
    [Test]
    public async Task NextOp_Unlocated_SetsIsUnlocatedTrue()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-UNLOC-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 100m,
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);

        // 应有UNLOCATED的NextOperationContext
        var unlocatedContext = result.NextOperationContexts.FirstOrDefault(c => c.IsUnlocated);
        Assert.That(unlocatedContext, Is.Not.Null);
        Assert.That(unlocatedContext!.StartStageCode, Is.EqualTo("CN_MACHINING"));
    }

    /// <summary>
    /// 测试XC位置被跳过（不产生NextOperationContext）
    /// </summary>
    [Test]
    public async Task NextOp_XcPosition_Skipped()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-XC-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 100m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 100m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            XcFacts = new[]
            {
                new XcFact
                {
                    XcWarehouseCode = "XC-WH-01",
                    RelatedStageCode = "CN_MACHINING",
                    Quantity = 30m,
                    SourceDocument = "XC-DOC-001"
                }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);

        // XC位置不应产生NextOperationContext
        var xcContext = result.NextOperationContexts.FirstOrDefault(c => c.PositionType == "XC");
        Assert.That(xcContext, Is.Null);

        // 应有STAGE_WAITING的NextOperationContext
        var stageContext = result.NextOperationContexts.FirstOrDefault(c => c.PositionType == "STAGE_WAITING");
        Assert.That(stageContext, Is.Not.Null);
    }

    /// <summary>
    /// 测试NextOperationContext不生成Operation Supply
    /// </summary>
    [Test]
    public async Task NextOp_DoesNotCreateOperationSupply()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-NOOP-SUPPLY-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 1000m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 800m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            OperationProgress = new[]
            {
                new OperationProgressFact
                {
                    OperationCode = "NC001",
                    OperationName = "NC",
                    StageCode = "CN_MACHINING",
                    GoodQty = 800m,
                    RemainingQty = 200m,
                    OperationSequence = 1
                },
                new OperationProgressFact
                {
                    OperationCode = "EXTRUDE001",
                    OperationName = "挤丝",
                    StageCode = "CN_MACHINING",
                    GoodQty = 0m,
                    RemainingQty = 1000m,
                    OperationSequence = 2
                }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        // NextOperationContext不是Supply类型
        foreach (var context in result.NextOperationContexts)
        {
            Assert.That(context.PositionType, Is.Not.EqualTo("SUPPLY"));
            Assert.That(context.PositionType, Is.Not.EqualTo("OPERATION_SUPPLY"));
        }

        // Position不是Operation级
        foreach (var position in result.Positions)
        {
            Assert.That(position.PositionType, Is.Not.EqualTo(PositionType.XC) | Is.Not.EqualTo(PositionType.INTERPLANT_TRANSIT));
        }
    }

    /// <summary>
    /// 多 frontier → NEXT_OPERATION_AMBIGUOUS（39-0 §四.2 / §五）
    /// NC(GoodQty=500>0, RemainingQty=500>0) + 挤丝(GoodQty=200>0, RemainingQty=800>0) 均已开工未完成
    /// 研磨(GoodQty=0, RemainingQty=1000>0) 未开工
    /// 2个 frontier 候选 → 无法唯一定位，降级到 Stage 级 + NEXT_OPERATION_AMBIGUOUS
    /// </summary>
    [Test]
    public async Task NextOp_ThreeOperations_SplitsCorrectly()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-3OP-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 1000m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 1000m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            OperationProgress = new[]
            {
                new OperationProgressFact
                {
                    OperationCode = "NC001",
                    OperationName = "NC",
                    StageCode = "CN_MACHINING",
                    GoodQty = 500m,
                    RemainingQty = 500m,
                    OperationSequence = 1
                },
                new OperationProgressFact
                {
                    OperationCode = "EXTRUDE001",
                    OperationName = "挤丝",
                    StageCode = "CN_MACHINING",
                    GoodQty = 200m,
                    RemainingQty = 800m,
                    OperationSequence = 2
                },
                new OperationProgressFact
                {
                    OperationCode = "GRIND001",
                    OperationName = "研磨",
                    StageCode = "CN_MACHINING",
                    GoodQty = 0m,
                    RemainingQty = 1000m,
                    OperationSequence = 3
                }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Positions.Sum(p => p.Quantity), Is.EqualTo(1000m));

        // 39-0：2个已开工未完成（NC + 挤丝）→ AMBIGUOUS，降级到 Stage 级
        Assert.That(result.NextOperationContexts.Count, Is.EqualTo(1));

        var ambiguousSlice = result.NextOperationContexts.First();
        Assert.That(ambiguousSlice.StartOperationCode, Is.Null);
        Assert.That(ambiguousSlice.SliceQty, Is.EqualTo(1000m));
        Assert.That(ambiguousSlice.StartStageCode, Is.EqualTo("CN_MACHINING"));
        Assert.That(ambiguousSlice.IssueCode, Is.EqualTo("NEXT_OPERATION_AMBIGUOUS"));
    }

    // ========================================================================
    // S11: DAG 拓扑前沿测试（39-0 §二/§五，Routing 数据可用时）
    // ========================================================================

    /// <summary>
    /// DAG 串行链：A→B→C，A 完成，B 已开工未完成，C 未开工
    /// 前沿 = B（唯一已开工未完成且前驱全部完成）
    /// </summary>
    [Test]
    public async Task NextOp_DagSerialChain_FindsFrontier()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-DAG-SERIAL-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 1000m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 1000m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            OperationProgress = new[]
            {
                new OperationProgressFact
                {
                    OperationCode = "OP-A", OperationName = "NC",
                    StageCode = "CN_MACHINING", GoodQty = 1000m, RemainingQty = 0m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-B", OperationName = "挤丝",
                    StageCode = "CN_MACHINING", GoodQty = 200m, RemainingQty = 800m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-C", OperationName = "研磨",
                    StageCode = "CN_MACHINING", GoodQty = 0m, RemainingQty = 1000m
                }
            },
            RoutingOperations = new[]
            {
                new RoutingOperationFact { OperationCode = "OP-A", OperationName = "NC", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-B", OperationName = "挤丝", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-C", OperationName = "研磨", StageCode = "CN_MACHINING" }
            },
            RoutingDependencies = new[]
            {
                new RoutingDependencyFact { FromOperationCode = "OP-A", ToOperationCode = "OP-B" },
                new RoutingDependencyFact { FromOperationCode = "OP-B", ToOperationCode = "OP-C" }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);

        // DAG 唯一前沿 = 挤丝（NC 完成，挤丝已开工，研磨未开工）
        Assert.That(result.NextOperationContexts.Count, Is.EqualTo(1));
        var slice = result.NextOperationContexts.First();
        Assert.That(slice.StartOperationCode, Is.EqualTo("挤丝"));
        Assert.That(slice.SliceQty, Is.EqualTo(1000m));
        Assert.That(slice.IssueCode, Is.Null);
    }

    /// <summary>
    /// DAG 并行分支：A→B, A→C, B→D, C→D，A 完成，B 和 C 均已开工未完成
    /// AMBIGUOUS（2 个合法前沿，不得重新线性化）
    /// </summary>
    [Test]
    public async Task NextOp_DagParallelBranch_Ambiguous()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-DAG-PARALLEL-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 1000m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 1000m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            OperationProgress = new[]
            {
                new OperationProgressFact
                {
                    OperationCode = "OP-A", OperationName = "NC",
                    StageCode = "CN_MACHINING", GoodQty = 1000m, RemainingQty = 0m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-B", OperationName = "挤丝",
                    StageCode = "CN_MACHINING", GoodQty = 200m, RemainingQty = 800m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-C", OperationName = "研磨",
                    StageCode = "CN_MACHINING", GoodQty = 300m, RemainingQty = 700m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-D", OperationName = "检测",
                    StageCode = "CN_MACHINING", GoodQty = 0m, RemainingQty = 1000m
                }
            },
            RoutingOperations = new[]
            {
                new RoutingOperationFact { OperationCode = "OP-A", OperationName = "NC", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-B", OperationName = "挤丝", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-C", OperationName = "研磨", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-D", OperationName = "检测", StageCode = "CN_MACHINING" }
            },
            RoutingDependencies = new[]
            {
                new RoutingDependencyFact { FromOperationCode = "OP-A", ToOperationCode = "OP-B" },
                new RoutingDependencyFact { FromOperationCode = "OP-A", ToOperationCode = "OP-C" },
                new RoutingDependencyFact { FromOperationCode = "OP-B", ToOperationCode = "OP-D" },
                new RoutingDependencyFact { FromOperationCode = "OP-C", ToOperationCode = "OP-D" }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);

        // 并行分支：挤丝 + 研磨 均已开工，A 完成 → AMBIGUOUS
        Assert.That(result.NextOperationContexts.Count, Is.EqualTo(1));
        var slice = result.NextOperationContexts.First();
        Assert.That(slice.StartOperationCode, Is.Null);
        Assert.That(slice.IssueCode, Is.EqualTo("NEXT_OPERATION_AMBIGUOUS"));
        Assert.That(slice.SliceQty, Is.EqualTo(1000m));
    }

    /// <summary>
    /// DAG 并行结构但仅一侧开工：A→B, A→C，A 完成，B 已开工未完成，C 未开工
    /// 前沿 = B（只有 B 是已开工且前驱完成的）
    /// </summary>
    [Test]
    public async Task NextOp_DagParallelOneStarted_FindsFrontier()
    {
        var input = new ProductionInstructionPositionInput
        {
            ProductionInstructionNo = "PI-DAG-PARALLEL-ONE-001",
            MaterialId = 1001,
            MaterialCode = "MAT-001",
            FactoryId = 5001,
            FactoryCode = "CN",
            ErpRemainingQty = 1000m,
            StageProgress = new[]
            {
                new StageProgressFact
                {
                    StageCode = "CN_MACHINING",
                    GoodCompletedQty = 1000m,
                    StageSequence = 1,
                    SnapshotId = 1
                }
            },
            OperationProgress = new[]
            {
                new OperationProgressFact
                {
                    OperationCode = "OP-A", OperationName = "NC",
                    StageCode = "CN_MACHINING", GoodQty = 1000m, RemainingQty = 0m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-B", OperationName = "挤丝",
                    StageCode = "CN_MACHINING", GoodQty = 200m, RemainingQty = 800m
                },
                new OperationProgressFact
                {
                    OperationCode = "OP-C", OperationName = "研磨",
                    StageCode = "CN_MACHINING", GoodQty = 0m, RemainingQty = 1000m
                }
            },
            RoutingOperations = new[]
            {
                new RoutingOperationFact { OperationCode = "OP-A", OperationName = "NC", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-B", OperationName = "挤丝", StageCode = "CN_MACHINING" },
                new RoutingOperationFact { OperationCode = "OP-C", OperationName = "研磨", StageCode = "CN_MACHINING" }
            },
            RoutingDependencies = new[]
            {
                new RoutingDependencyFact { FromOperationCode = "OP-A", ToOperationCode = "OP-B" },
                new RoutingDependencyFact { FromOperationCode = "OP-A", ToOperationCode = "OP-C" }
            },
            StagePath = new[]
            {
                new StagePathFact { StageCode = "CN_MACHINING", StageSequence = 1, IsStartStage = true }
            }
        };

        var results = await _calculator.CalculateProductionInstructionPositionsAsync(
            new[] { input }, new FrozenFactParameters(), CancellationToken.None);

        var result = results.First();

        Assert.That(result.IsSuccess, Is.True);

        // 并行结构但只有 B 开工 → 唯一前沿 = 挤丝
        Assert.That(result.NextOperationContexts.Count, Is.EqualTo(1));
        var slice = result.NextOperationContexts.First();
        Assert.That(slice.StartOperationCode, Is.EqualTo("挤丝"));
        Assert.That(slice.SliceQty, Is.EqualTo(1000m));
        Assert.That(slice.IssueCode, Is.Null);
    }
}
