using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.BusinessRules.Calculators;

/// <summary>
/// 生产指示位置计算器（5号位核心能力）
///
/// 职责边界：
///   - 接收2号位装载好的完整事实包（ProductionInstructionPositionInput）
///   - 进行纯计算：Stage差分、XC/Transit互斥、UNLOCATED、总量闭合、Issue生成
///   - 返回Position结果（ProductionInstructionPositionResult）
///   - 不访问数据库，不注入Repository
///   - 不决定PI最终分配给哪个Demand（由2号位负责）
///
/// 设计原则：
///   - DTO进、Result出，纯计算逻辑
///   - 2号位负责数据装载和DataCutoffTime一致性
///   - 5号位只负责复杂位置判断
/// </summary>
public class ProductionInstructionPositionCalculator : IProductionInstructionPositionCalculator
{
    private readonly ILogger<ProductionInstructionPositionCalculator> _logger;

    public ProductionInstructionPositionCalculator(ILogger<ProductionInstructionPositionCalculator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductionInstructionPositionResult>> CalculateProductionInstructionPositionsAsync(
        IReadOnlyList<ProductionInstructionPositionInput> inputs,
        FrozenFactParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProductionInstructionPositionResult>();

        foreach (var input in inputs)
        {
            try
            {
                var result = CalculateSinglePiPosition(input);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PI Position计算失败: PI={PiNo}, Material={MatId}, Factory={FactId}",
                    input.ProductionInstructionNo, input.MaterialId, input.FactoryId);

                results.Add(new ProductionInstructionPositionResult
                {
                    ProductionInstructionNo = input.ProductionInstructionNo,
                    TotalRemainingQty = input.ErpRemainingQty,
                    IsSuccess = false,
                    FailureReason = $"计算异常: {ex.Message}",
                    Positions = Array.Empty<PositionSlice>(),
                    Issues = new[]
                    {
                        new PositionIssue
                        {
                            IssueType = "CALCULATION_EXCEPTION",
                            Level = PositionIssueLevel.ERROR,
                            Description = $"PI Position计算发生异常",
                            ProductionInstructionNo = input.ProductionInstructionNo,
                            ContextData = ex.ToString()
                        }
                    }
                });
            }
        }

        return Task.FromResult<IReadOnlyList<ProductionInstructionPositionResult>>(results);
    }

    /// <summary>
    /// 计算单个PI的Position
    /// </summary>
    private ProductionInstructionPositionResult CalculateSinglePiPosition(ProductionInstructionPositionInput input)
    {
        var issues = new List<PositionIssue>();
        var positions = new List<PositionSlice>();

        // 第一步：计算Stage位置（累计差分）
        var stagePositions = CalculateStagePositions(input, issues);
        positions.AddRange(stagePositions);

        // 第二步：处理XC位置
        var xcPositions = CalculateXcPositions(input, issues);
        positions.AddRange(xcPositions);

        // 第三步：处理厂间在途
        var transitPositions = CalculateTransitPositions(input, issues);
        positions.AddRange(transitPositions);

        // 第四步：处理PI级库存事实（WAITING/Stage库存定位）
        var piInventoryPositions = CalculatePiInventoryPositions(input, issues);
        positions.AddRange(piInventoryPositions);

        // 第五步：处理强事实
        ApplyStrongFacts(input, positions, issues);

        // 第五步：Position互斥消重
        var deduplicatedPositions = DeduplicatePositions(positions, issues);

        // 第六步：计算UNLOCATED并总量闭合
        var finalPositions = EnsureTotalClosure(
            input.ErpRemainingQty,
            deduplicatedPositions,
            input.ProductionInstructionNo,
            issues);

        // 第七步：校验总量是否闭合
        decimal totalQty = finalPositions.Sum(p => p.Quantity);
        bool isSuccess = Math.Abs(totalQty - input.ErpRemainingQty) < 0.0001m;

        if (!isSuccess)
        {
            issues.Add(new PositionIssue
            {
                IssueType = "QUANTITY_NOT_CLOSED",
                Level = PositionIssueLevel.ERROR,
                Description = $"Position总量无法闭合: ERP={input.ErpRemainingQty}, 计算总量={totalQty}, 差额={input.ErpRemainingQty - totalQty}",
                ProductionInstructionNo = input.ProductionInstructionNo,
                AffectedQuantity = input.ErpRemainingQty - totalQty
            });
        }

        return new ProductionInstructionPositionResult
        {
            ProductionInstructionNo = input.ProductionInstructionNo,
            TotalRemainingQty = input.ErpRemainingQty,
            Positions = finalPositions,
            Issues = issues,
            IsSuccess = isSuccess,
            FailureReason = isSuccess ? null : "Position总量无法与ERP RemainingQty闭合"
        };
    }

    /// <summary>
    /// 计算Stage位置（累计差分）
    /// </summary>
    private List<PositionSlice> CalculateStagePositions(
        ProductionInstructionPositionInput input,
        List<PositionIssue> issues)
    {
        var stagePositions = new List<PositionSlice>();

        if (input.StageProgress == null || input.StageProgress.Count == 0)
        {
            return stagePositions;
        }

        // 按Stage序号排序
        var sortedStages = input.StageProgress
            .OrderBy(s => s.StageSequence)
            .ToList();

        // 检查下游累计大于上游的情况
        for (int i = 0; i < sortedStages.Count - 1; i++)
        {
            var currentStage = sortedStages[i];
            var nextStage = sortedStages[i + 1];

            if (nextStage.CumulativeCompletedQty > currentStage.CumulativeCompletedQty)
            {
                issues.Add(new PositionIssue
                {
                    IssueType = "DOWNSTREAM_GT_UPSTREAM",
                    Level = PositionIssueLevel.WARN,
                    Description = $"下游Stage累计量({nextStage.CumulativeCompletedQty})大于上游Stage累计量({currentStage.CumulativeCompletedQty})",
                    ProductionInstructionNo = input.ProductionInstructionNo,
                    StageCode = nextStage.StageCode,
                    AffectedQuantity = nextStage.CumulativeCompletedQty - currentStage.CumulativeCompletedQty,
                    ContextData = $"上游Stage: {currentStage.StageCode}, 下游Stage: {nextStage.StageCode}"
                });

                // 保守处理：下修下游有效累计量
                var correctedStage = new StageProgressFact
                {
                    StageCode = nextStage.StageCode,
                    CumulativeCompletedQty = currentStage.CumulativeCompletedQty,
                    StageSequence = nextStage.StageSequence,
                    SnapshotId = nextStage.SnapshotId,
                    UpdatedAt = nextStage.UpdatedAt
                };
                sortedStages[i + 1] = correctedStage;
            }
        }

        // 计算每个Stage的区间数量（差分）
        for (int i = sortedStages.Count - 1; i >= 0; i--)
        {
            decimal qty;
            if (i == sortedStages.Count - 1)
            {
                // 最后一个Stage：累计量就是该Stage的数量
                qty = sortedStages[i].CumulativeCompletedQty;
            }
            else
            {
                // 中间Stage：本Stage累计量 - 下游Stage累计量
                qty = sortedStages[i].CumulativeCompletedQty - sortedStages[i + 1].CumulativeCompletedQty;
            }

            if (qty > 0.0001m)  // 只记录有数量的Stage
            {
                // 判断PositionType：首工序待开始 vs Stage等待
                var isFirstStage = input.StagePath.Any(sp =>
                    sp.StageCode == sortedStages[i].StageCode && sp.IsStartStage);
                var hasNoCompletion = sortedStages[i].CumulativeCompletedQty <= 0.0001m;

                var positionType = (isFirstStage && hasNoCompletion)
                    ? PositionType.FIRST_STAGE_PENDING
                    : PositionType.STAGE_WAITING;

                stagePositions.Add(new PositionSlice
                {
                    PositionType = positionType,
                    StageCode = sortedStages[i].StageCode,
                    Quantity = qty,
                    IsStrongEvidence = false,
                    SourceKey = sortedStages[i].SnapshotId?.ToString(),
                    IsUnlocated = false
                });
            }
        }

        return stagePositions;
    }

    /// <summary>
    /// 计算XC位置
    /// </summary>
    private List<PositionSlice> CalculateXcPositions(
        ProductionInstructionPositionInput input,
        List<PositionIssue> issues)
    {
        var xcPositions = new List<PositionSlice>();

        if (input.XcFacts == null || input.XcFacts.Count == 0)
        {
            return xcPositions;
        }

        foreach (var xc in input.XcFacts)
        {
            if (xc.Quantity > 0.0001m)
            {
                xcPositions.Add(new PositionSlice
                {
                    PositionType = PositionType.XC,
                    LocationKey = xc.XcWarehouseCode,
                    StageCode = xc.RelatedStageCode,
                    Quantity = xc.Quantity,
                    AvailableTime = xc.AvailableTime,
                    IsStrongEvidence = true,  // XC是强事实
                    SourceKey = xc.SourceDocument,
                    IsUnlocated = false
                });
            }
        }

        return xcPositions;
    }

    /// <summary>
    /// 计算厂间在途位置（仅PI级Transit）
    ///
    /// 职责边界：
    /// - P前缀单据 = 生产指示级Transit，属于PI Position计算范围
    /// - O前缀单据 = 出荷指示级Transit，属于跨厂订单链（INTER_FACTORY_ORDER），不在此处理
    /// - F10-F12的SH逻辑已移除，由跨厂订单链独立处理
    /// </summary>
    private List<PositionSlice> CalculateTransitPositions(
        ProductionInstructionPositionInput input,
        List<PositionIssue> issues)
    {
        var transitPositions = new List<PositionSlice>();

        if (input.TransitFacts == null || input.TransitFacts.Count == 0)
        {
            return transitPositions;
        }

        // 处理每个Transit
        foreach (var transit in input.TransitFacts)
        {
            if (transit.Quantity <= 0.0001m)
            {
                continue;
            }

            // 使用CrossFactoryEdges定位Transit应从哪个Stage扣除
            string? relatedStageCode = null;

            if (input.CrossFactoryEdges != null && input.CrossFactoryEdges.Count > 0)
            {
                // 匹配SourceFactory→TargetFactory
                var matchingEdges = input.CrossFactoryEdges
                    .Where(e => e.FromFactoryCode == transit.SourceFactoryCode
                             && e.ToFactoryCode == transit.TargetFactoryCode)
                    .ToList();

                if (matchingEdges.Count == 1)
                {
                    // 唯一匹配：Transit应从FromStage扣除
                    relatedStageCode = matchingEdges[0].FromStageCode;
                }
                else if (matchingEdges.Count > 1)
                {
                    // 多个匹配：无法唯一定位，登记Issue
                    issues.Add(new PositionIssue
                    {
                        IssueType = "TRANSIT_AMBIGUOUS_STAGE",
                        Level = PositionIssueLevel.WARN,
                        Description = $"Transit {transit.TransitDocumentNo} 无法唯一定位Stage：{matchingEdges.Count}个跨厂边匹配 {transit.SourceFactoryCode}→{transit.TargetFactoryCode}",
                        ProductionInstructionNo = input.ProductionInstructionNo,
                        AffectedQuantity = transit.Quantity,
                        ContextData = $"Transit: {transit.TransitDocumentNo}, Edges: {string.Join(", ", matchingEdges.Select(e => $"{e.FromStageCode}→{e.ToStageCode}"))}"
                    });
                    // 保守降级：无法可靠定位则不关联Stage
                    relatedStageCode = null;
                }
                // matchingEdges.Count == 0: 没有匹配的边，relatedStageCode保持null
            }

            transitPositions.Add(new PositionSlice
            {
                PositionType = PositionType.INTERPLANT_TRANSIT,
                LocationKey = $"{transit.SourceFactoryCode}→{transit.TargetFactoryCode}",
                StageCode = relatedStageCode,  // 关联到FromStage（如果能唯一定位）
                Quantity = transit.Quantity,
                AvailableTime = transit.EstimatedArrivalTime,
                IsStrongEvidence = true,
                SourceKey = transit.TransitDocumentNo,
                IsUnlocated = relatedStageCode == null  // 无法定位Stage时标记为Unlocated
            });
        }

        return transitPositions;
    }

    /// <summary>
    /// 计算PI级库存位置（WAITING/Stage库存定位）
    ///
    /// 职责边界：
    /// - PiInventories不是额外Supply，只是定位RemainingQty内部位置
    /// - LocationCategory由2号位根据MaterialStageDeptContext等映射表确定
    /// - STAGE_INVENTORY: 明确属于某个Stage → 形成该Stage Position
    /// - INTER_STAGE_WAITING: 已离开上一Stage未进入下一Stage → 形成WAITING Position
    /// - UNKNOWN: 无法判断 → 暂不形成Position，由UNLOCATED兜底
    /// </summary>
    private List<PositionSlice> CalculatePiInventoryPositions(
        ProductionInstructionPositionInput input,
        List<PositionIssue> issues)
    {
        var inventoryPositions = new List<PositionSlice>();

        if (input.PiInventories == null || input.PiInventories.Count == 0)
        {
            return inventoryPositions;
        }

        foreach (var inventory in input.PiInventories)
        {
            if (inventory.Quantity <= 0.0001m)
            {
                continue;
            }

            // 缺少LocationCategory时登记Issue并跳过
            if (string.IsNullOrWhiteSpace(inventory.LocationCategory))
            {
                issues.Add(new PositionIssue
                {
                    IssueType = "PI_INVENTORY_MISSING_CATEGORY",
                    Level = PositionIssueLevel.WARN,
                    Description = $"PI库存缺少LocationCategory: WarehouseCode={inventory.WarehouseCode}",
                    ProductionInstructionNo = input.ProductionInstructionNo,
                    AffectedQuantity = inventory.Quantity,
                    ContextData = $"WarehouseCode: {inventory.WarehouseCode}, SourceDocument: {inventory.SourceDocument}"
                });
                continue;
            }

            // STAGE_INVENTORY: 明确映射到Stage
            if (inventory.LocationCategory == "STAGE_INVENTORY")
            {
                if (string.IsNullOrWhiteSpace(inventory.RelatedStageCode))
                {
                    issues.Add(new PositionIssue
                    {
                        IssueType = "STAGE_INVENTORY_MISSING_STAGE",
                        Level = PositionIssueLevel.ERROR,
                        Description = $"STAGE_INVENTORY类型但缺少RelatedStageCode: WarehouseCode={inventory.WarehouseCode}",
                        ProductionInstructionNo = input.ProductionInstructionNo,
                        AffectedQuantity = inventory.Quantity,
                        ContextData = $"WarehouseCode: {inventory.WarehouseCode}"
                    });
                    continue;
                }

                inventoryPositions.Add(new PositionSlice
                {
                    PositionType = PositionType.STAGE_WAITING,
                    StageCode = inventory.RelatedStageCode,
                    Quantity = inventory.Quantity,
                    LocationKey = $"PiInventory:{inventory.WarehouseCode}",
                    IsUnlocated = false
                });
            }
            // INTER_STAGE_WAITING: Stage间等待
            else if (inventory.LocationCategory == "INTER_STAGE_WAITING")
            {
                // WAITING Position可以关联Stage（如果2号位能推断出在哪两个Stage之间）
                // 也可以不关联Stage（只知道在等待但不确定具体位置）
                inventoryPositions.Add(new PositionSlice
                {
                    PositionType = PositionType.STAGE_WAITING,
                    StageCode = inventory.RelatedStageCode,  // 可能为null
                    Quantity = inventory.Quantity,
                    LocationKey = $"Waiting:{inventory.WarehouseCode}",
                    IsUnlocated = false
                });
            }
            // UNKNOWN: 无法可靠判断
            else if (inventory.LocationCategory == "UNKNOWN")
            {
                // 不形成Position，由后续UNLOCATED兜底
                issues.Add(new PositionIssue
                {
                    IssueType = "PI_INVENTORY_UNKNOWN_LOCATION",
                    Level = PositionIssueLevel.WARN,
                    Description = $"PI库存位置类型为UNKNOWN，无法定位: WarehouseCode={inventory.WarehouseCode}",
                    ProductionInstructionNo = input.ProductionInstructionNo,
                    AffectedQuantity = inventory.Quantity,
                    ContextData = $"WarehouseCode: {inventory.WarehouseCode}, 将由UNLOCATED兜底"
                });
            }
            else
            {
                // 未知的LocationCategory值
                issues.Add(new PositionIssue
                {
                    IssueType = "PI_INVENTORY_INVALID_CATEGORY",
                    Level = PositionIssueLevel.ERROR,
                    Description = $"PI库存LocationCategory值无效: {inventory.LocationCategory}",
                    ProductionInstructionNo = input.ProductionInstructionNo,
                    AffectedQuantity = inventory.Quantity,
                    ContextData = $"WarehouseCode: {inventory.WarehouseCode}, LocationCategory: {inventory.LocationCategory}"
                });
            }
        }

        return inventoryPositions;
    }

    /// <summary>
    /// 应用强事实校正
    ///
    /// 强事实（如ReceivedFact）可以直接修正Position的数量
    /// 例如：MES已报工数量可以直接扣减Stage累计进度
    /// </summary>
    /// <summary>
    /// 应用强位置事实（MES Stage内部报工/进度证据）
    ///
    /// 语义边界（F05）：
    /// - StrongFacts只能包含"仍属于ERP RemainingQty内部的位置事实"
    /// - MES Stage报工/工序进度 = 属于RemainingQty内部，可以定位Stage Position
    /// - SH Received = 跨厂订单链内部事实，不在此处理
    /// - 最终已入目标M库的Received = 已从ERP RemainingQty中排除，绝不能再进入此方法
    ///
    /// 二次扣减风险：
    /// - 如果StrongFacts错误包含"已入M库、ERP已扣除"的数量，会造成PI总量边界错误
    /// - 2号位必须确保传入的StrongFacts只包含RemainingQty内部的位置证据
    /// </summary>
    private void ApplyStrongFacts(
        ProductionInstructionPositionInput input,
        List<PositionSlice> positions,
        List<PositionIssue> issues)
    {
        // 处理StrongFacts：MES Stage内部强位置证据（仍在RemainingQty边界内）
        if (input.StrongFacts != null && input.StrongFacts.Count > 0)
        {
            foreach (var received in input.StrongFacts)
            {
                if (received.Quantity <= 0.0001m)
                {
                    continue;
                }

                // P0边界校验：使用DocumentType判断，不靠P/O前缀
                if (string.IsNullOrWhiteSpace(received.DocumentType))
                {
                    issues.Add(new PositionIssue
                    {
                        IssueType = "RECEIVED_MISSING_DOCUMENT_TYPE",
                        Level = PositionIssueLevel.ERROR,
                        Description = $"Received事实缺少DocumentType: DocumentNo={received.DocumentNo}",
                        ProductionInstructionNo = input.ProductionInstructionNo,
                        AffectedQuantity = received.Quantity,
                        ContextData = $"DocumentNo: {received.DocumentNo}, WarehouseCode: {received.WarehouseCode}"
                    });
                    continue;
                }

                // SHIPPING_INSTRUCTION禁止进入PI Position
                if (received.DocumentType == "SHIPPING_INSTRUCTION" || received.DocumentType == "SH")
                {
                    issues.Add(new PositionIssue
                    {
                        IssueType = "RECEIVED_SHIPPING_IN_PI_POSITION",
                        Level = PositionIssueLevel.ERROR,
                        Description = $"厂间订单Received不得进入PI Position: DocumentNo={received.DocumentNo}",
                        ProductionInstructionNo = input.ProductionInstructionNo,
                        AffectedQuantity = received.Quantity,
                        ContextData = $"DocumentType: {received.DocumentType}, 应由INTER_FACTORY_ORDER链处理"
                    });
                    continue;
                }

                // PRODUCTION_INSTRUCTION必须匹配当前PI号
                if (received.DocumentType == "PRODUCTION_INSTRUCTION" || received.DocumentType == "PI")
                {
                    if (received.DocumentNo != input.ProductionInstructionNo)
                    {
                        issues.Add(new PositionIssue
                        {
                            IssueType = "RECEIVED_PI_MISMATCH",
                            Level = PositionIssueLevel.ERROR,
                            Description = $"Received的PI号({received.DocumentNo})与当前PI号({input.ProductionInstructionNo})不匹配",
                            ProductionInstructionNo = input.ProductionInstructionNo,
                            AffectedQuantity = received.Quantity,
                            ContextData = $"DocumentNo: {received.DocumentNo}"
                        });
                        continue;
                    }
                }
                else if (received.DocumentType == "UNKNOWN")
                {
                    issues.Add(new PositionIssue
                    {
                        IssueType = "RECEIVED_UNKNOWN_DOCUMENT_TYPE",
                        Level = PositionIssueLevel.WARN,
                        Description = $"Received DocumentType=UNKNOWN，不允许直接扣Stage: DocumentNo={received.DocumentNo}",
                        ProductionInstructionNo = input.ProductionInstructionNo,
                        AffectedQuantity = received.Quantity,
                        ContextData = $"DocumentNo: {received.DocumentNo}, WarehouseCode: {received.WarehouseCode}"
                    });
                    continue;
                }

                // 边界校验通过后，才能进行Stage扣减
                // 找到对应Stage的Position
                var stagePosition = positions
                    .FirstOrDefault(p => p.PositionType == PositionType.STAGE_WAITING && p.StageCode == received.RelatedStageCode);

                if (stagePosition != null)
                {
                    // 从Stage Position中扣除已报工数量
                    decimal adjustedQty = stagePosition.Quantity - received.Quantity;

                    if (adjustedQty >= -0.0001m)
                    {
                        // 扣除后数量>=0，更新Position
                        int index = positions.IndexOf(stagePosition);
                        if (adjustedQty > 0.0001m)
                        {
                            positions[index] = new PositionSlice
                            {
                                PositionType = stagePosition.PositionType,
                                StageCode = stagePosition.StageCode,
                                LocationKey = stagePosition.LocationKey,
                                Quantity = adjustedQty,
                                AvailableTime = stagePosition.AvailableTime,
                                IsStrongEvidence = stagePosition.IsStrongEvidence,
                                SourceKey = stagePosition.SourceKey,
                                IsUnlocated = stagePosition.IsUnlocated
                            };
                        }
                        else
                        {
                            // 扣除后数量=0，移除Position
                            positions.RemoveAt(index);
                        }
                    }
                    else
                    {
                        // 报工数量超过Stage数量，记录Issue
                        issues.Add(new PositionIssue
                        {
                            IssueType = "RECEIVED_EXCEEDS_STAGE",
                            Level = PositionIssueLevel.WARN,
                            Description = $"Stage {received.RelatedStageCode} 已报工数量({received.Quantity})超过Stage Position数量({stagePosition.Quantity})",
                            ProductionInstructionNo = input.ProductionInstructionNo,
                            StageCode = received.RelatedStageCode,
                            AffectedQuantity = -adjustedQty
                        });

                        // 移除被完全消耗的Stage Position
                        positions.Remove(stagePosition);
                    }
                }
                else
                {
                    // 没有对应的Stage Position，记录Issue
                    issues.Add(new PositionIssue
                    {
                        IssueType = "RECEIVED_WITHOUT_STAGE",
                        Level = PositionIssueLevel.INFO,
                        Description = $"Stage {received.RelatedStageCode} 有报工记录({received.Quantity})但无对应Stage Position",
                        ProductionInstructionNo = input.ProductionInstructionNo,
                        StageCode = received.RelatedStageCode,
                        AffectedQuantity = received.Quantity
                    });
                }
            }
        }
    }

    /// <summary>
    /// Position互斥消重
    /// 同一物理份额不能同时算在Stage、XC和Transit（F05）
    ///
    /// 消重规则：
    ///   1. 强事实（XC、Transit）优先级高于弱推导（Stage）
    ///   2. 同Stage的XC会从该Stage Position中扣除
    ///   3. Transit与Stage重叠时必须去重（F05）
    /// </summary>
    private List<PositionSlice> DeduplicatePositions(
        List<PositionSlice> positions,
        List<PositionIssue> issues)
    {
        // 阶段A：按Stage分组，扣除XC数量
        var stagePositions = positions
            .Where(p => p.PositionType == PositionType.STAGE_WAITING)
            .ToList();

        var xcPositions = positions
            .Where(p => p.PositionType == PositionType.XC)
            .ToList();

        var transitPositions = positions
            .Where(p => p.PositionType == PositionType.INTERPLANT_TRANSIT)
            .ToList();

        var deduplicatedStages = new List<PositionSlice>();

        // 对每个Stage Position，扣除关联的XC数量
        foreach (var stage in stagePositions)
        {
            // 找到该Stage关联的XC
            var relatedXc = xcPositions
                .Where(xc => xc.StageCode == stage.StageCode)
                .Sum(xc => xc.Quantity);

            decimal adjustedQty = stage.Quantity - relatedXc;

            if (adjustedQty > 0.0001m)
            {
                // Stage数量大于XC，保留差额
                deduplicatedStages.Add(new PositionSlice
                {
                    PositionType = stage.PositionType,
                    StageCode = stage.StageCode,
                    LocationKey = stage.LocationKey,
                    Quantity = adjustedQty,
                    AvailableTime = stage.AvailableTime,
                    IsStrongEvidence = stage.IsStrongEvidence,
                    SourceKey = stage.SourceKey,
                    IsUnlocated = stage.IsUnlocated
                });
            }
            else if (adjustedQty < -0.0001m)
            {
                // XC数量超过Stage，记录异常
                issues.Add(new PositionIssue
                {
                    IssueType = "XC_EXCEEDS_STAGE",
                    Level = PositionIssueLevel.WARN,
                    Description = $"Stage {stage.StageCode} 的XC数量({relatedXc})超过Stage推导数量({stage.Quantity})",
                    StageCode = stage.StageCode,
                    AffectedQuantity = -adjustedQty
                });
                // Stage被XC完全覆盖，不保留Stage Position
            }
            // else: Stage恰好等于XC，Stage被完全覆盖，不保留
        }

        // 阶段B：扣除Transit与Stage的重叠（F05）
        // Transit是强事实，从Stage中扣除与Transit重叠的数量
        var totalTransitQty = transitPositions.Sum(t => t.Quantity);

        if (totalTransitQty > 0.0001m && deduplicatedStages.Count > 0)
        {
            decimal remainingTransitToDeduct = totalTransitQty;
            var finalStages = new List<PositionSlice>();

            // 从最早Stage开始扣除Transit
            foreach (var stage in deduplicatedStages.OrderBy(s => s.StageCode))
            {
                if (remainingTransitToDeduct < 0.0001m)
                {
                    // 没有更多Transit需要扣除，保留剩余Stage
                    finalStages.Add(stage);
                    continue;
                }

                if (stage.Quantity <= remainingTransitToDeduct + 0.0001m)
                {
                    // 该Stage被Transit完全覆盖
                    remainingTransitToDeduct -= stage.Quantity;
                    // 不保留该Stage Position
                }
                else
                {
                    // 该Stage部分被Transit覆盖
                    decimal adjustedQty = stage.Quantity - remainingTransitToDeduct;
                    finalStages.Add(new PositionSlice
                    {
                        PositionType = stage.PositionType,
                        StageCode = stage.StageCode,
                        LocationKey = stage.LocationKey,
                        Quantity = adjustedQty,
                        AvailableTime = stage.AvailableTime,
                        IsStrongEvidence = stage.IsStrongEvidence,
                        SourceKey = stage.SourceKey,
                        IsUnlocated = stage.IsUnlocated
                    });
                    remainingTransitToDeduct = 0m;
                }
            }

            deduplicatedStages = finalStages;

            if (remainingTransitToDeduct > 0.0001m)
            {
                // Transit数量超过Stage，记录Issue
                issues.Add(new PositionIssue
                {
                    IssueType = "TRANSIT_EXCEEDS_STAGE",
                    Level = PositionIssueLevel.WARN,
                    Description = $"Transit数量({totalTransitQty})超过Stage总量，超出{remainingTransitToDeduct}",
                    AffectedQuantity = remainingTransitToDeduct
                });
            }
        }

        // 合并结果：去重后的Stage + 所有XC + 所有Transit
        var result = new List<PositionSlice>();
        result.AddRange(deduplicatedStages);
        result.AddRange(xcPositions);
        result.AddRange(transitPositions);

        return result;
    }

    /// <summary>
    /// 确保总量闭合，必要时添加UNLOCATED
    /// </summary>
    private List<PositionSlice> EnsureTotalClosure(
        decimal erpRemainingQty,
        List<PositionSlice> positions,
        string piNo,
        List<PositionIssue> issues)
    {
        decimal totalQty = positions.Sum(p => p.Quantity);
        decimal gap = erpRemainingQty - totalQty;

        if (Math.Abs(gap) < 0.0001m)
        {
            // 已经闭合，无需UNLOCATED
            return positions;
        }

        if (gap > 0.0001m)
        {
            // 缺口：添加UNLOCATED
            issues.Add(new PositionIssue
            {
                IssueType = "UNLOCATED_GAP",
                Level = PositionIssueLevel.WARN,
                Description = $"无法定位数量{gap}，进入UNLOCATED",
                ProductionInstructionNo = piNo,
                AffectedQuantity = gap
            });

            var unlocatedPosition = new PositionSlice
            {
                PositionType = PositionType.UNLOCATED,
                Quantity = gap,
                IsStrongEvidence = false,
                IsUnlocated = true,
                SourceKey = "AUTO_GENERATED"
            };

            return positions.Append(unlocatedPosition).ToList();
        }
        else
        {
            // 超量：严重问题
            issues.Add(new PositionIssue
            {
                IssueType = "QUANTITY_OVERFLOW",
                Level = PositionIssueLevel.ERROR,
                Description = $"Position总量({totalQty})超过ERP RemainingQty({erpRemainingQty})，超出{-gap}",
                ProductionInstructionNo = piNo,
                AffectedQuantity = -gap
            });

            return positions;
        }
    }
}
