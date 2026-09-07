using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 5: 压缩空隙与最终评价
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 Phase 5
///
/// 职责：
/// - 在不破坏高优先级交期的情况下：
///   * 减少不必要等待
///   * 减少 WIP
///   * 减少 Setup
///   * 提升利用率
///   * 避免过早生产
///   * 尽量保持计划稳定
/// </summary>
internal class PhaseFiveCompression
{
    /// <summary>
    /// 执行空隙压缩与最终评价
    /// 文档：§六 Phase 5
    /// 职责：在不破坏高优先级交期的情况下压缩空隙
    /// </summary>
    public DomainSolveResult Compress(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        RepairResult repairResult,
        DiagnosticsResult diagnostics,
        ConstraintContext constraints)
    {
        // 合并所有已排程任务
        var allScheduledTasks = new List<FinalTaskDraft>();
        allScheduledTasks.AddRange(scheduleResult.ScheduledTasks);
        allScheduledTasks.AddRange(repairResult.RepairedTasks);

        // TODO P13: 实现真实的空隙压缩优化（§六 Phase 5）
        // 在不破坏高优先级交期的情况下：
        // - 减少不必要等待
        // - 减少 WIP
        // - 减少 Setup
        // - 提升利用率
        // - 避免过早生产
        // - 尽量保持计划稳定

        // 生成 AllocationTaskShare（追溯 Allocation → Task 的份额）
        var allocationShares = GenerateAllocationShares(allScheduledTasks, request, constraints);

        // 第5轮修复：TaskDependency必须在硬校验之前生成，以便校验Dependency约束
        // P0-13修复：生成 TaskDependency（基于 Routing 工序依赖关系）
        var taskDependencies = GenerateTaskDependencies(allScheduledTasks, request, constraints);

        // 第4轮Item 10：Phase5最终硬约束校验（§十一）
        var validationResult = ValidateHardResult(allScheduledTasks, allocationShares, taskDependencies, request, constraints);
        if (!validationResult.IsValid)
        {
            return new DomainSolveResult
            {
                Success = false,
                ErrorMessage = $"Phase5硬约束校验失败: {validationResult.ErrorMessage}",
                IsRoughCut = false,
                FinalTasks = Array.Empty<FinalTaskDraft>(),
                AllocationShares = Array.Empty<AllocationTaskShare>(),
                UnscheduledTasks = Array.Empty<UnscheduledTaskResult>(),
                PhysicalPeggingDrafts = Array.Empty<FinalTaskPeggingDraft>(),
                ExplanationFacts = Array.Empty<ScheduleExplanationFact>(),
                Summary = new SolveSummary
                {
                    TotalDrafts = 0,
                    ScheduledCount = 0,
                    UnscheduledCount = 0,
                    ElapsedMs = 0,
                    IssueCount = 0,
                    UsedRoughCut = false
                }
            };
        }

        // 收集未排程需求
        var unscheduledTasks = new List<UnscheduledTaskResult>();

        // Phase 2 未排程的需求
        foreach (var demandKey in scheduleResult.UnscheduledDemandKeys)
        {
            if (!repairResult.RepairedTasks.Any(t => t.SourceDraftId == demandKey))
            {
                // 最小B：缺失生产部门 Context 的需求，Reason 单独标识
                var missingDept = IsMissingDepartmentContext(demandKey, request, constraints);
                unscheduledTasks.Add(new UnscheduledTaskResult
                {
                    DraftId = demandKey,
                    Reason = missingDept
                        ? "MISSING_PRODUCTION_DEPARTMENT_CONTEXT"
                        : "Phase 2 初始排程失败，Phase 4 修复未成功"
                });
            }
        }

        // Phase 4 仍未排程的需求
        foreach (var demandKey in repairResult.StillUnscheduledKeys)
        {
            unscheduledTasks.Add(new UnscheduledTaskResult
            {
                DraftId = demandKey,
                Reason = "Phase 4 局部修复后仍无法排程"
            });
        }

        // P0-03+P0-15修复：区分技术失败与业务Unscheduled
        // 技术失败：Routing非法、数量闭合错误、硬资源约束破坏 → Success=false
        // 业务结果：产能不足、物料太晚、DueDate无法满足 → Success=true + Unscheduled
        bool technicalFailure = scheduleResult.TechnicalFailure;
        string? errorMessage = technicalFailure ? scheduleResult.TechnicalFailureReason : null;

        return new DomainSolveResult
        {
            Success = !technicalFailure,
            ErrorMessage = errorMessage,
            IsRoughCut = false,
            FinalTasks = allScheduledTasks,
            AllocationShares = allocationShares,
            UnscheduledTasks = unscheduledTasks,
            PhysicalPeggingDrafts = taskDependencies,
            ExplanationFacts = diagnostics.ExplanationFacts,
            Summary = new SolveSummary
            {
                TotalDrafts = allScheduledTasks.Count + unscheduledTasks.Count,
                ScheduledCount = allScheduledTasks.Count,
                UnscheduledCount = unscheduledTasks.Count,
                ElapsedMs = 0, // 由 FiniteCapacitySolver 填充
                IssueCount = diagnostics.ExplanationFacts.Count,
                UsedRoughCut = false
            }
        };
    }

    /// <summary>
    /// 判断某 Demand 是否因缺失生产部门 Context 而被排除（最小B）。
    /// 部门锁定在 Phase 1 完成，缺失 Context 的 Material 记入 constraints.MissingDepartmentContextMaterialIds；
    /// 此处按 LogicalDemandKey → MaterialId 反查，给 Unscheduled 结果补正确的 Reason。
    /// </summary>
    private bool IsMissingDepartmentContext(
        string demandKey,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var demand = request.LogicalProductionDemands
            .FirstOrDefault(d => d.LogicalDemandKey == demandKey);
        if (demand == null)
        {
            return false;
        }
        return constraints.MissingDepartmentContextMaterialIds.Contains(demand.MaterialId);
    }

    /// <summary>
    /// 生成 AllocationTaskShare（追溯机制）
    /// 文档：§五 5.2
    /// P0-14修复（0号位严重Bug反馈）：只有末端Task记录净产出份额，串行前序通过TaskDependency追溯
    /// 闭合检查：Σ ShareQty = 该Allocation需制造的NetOutputQty
    /// </summary>
    private List<AllocationTaskShare> GenerateAllocationShares(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var shares = new List<AllocationTaskShare>();

        // 按 SourceDraftId（需求）分组任务
        var tasksByDemand = tasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.PlannedStartTime).ToList());

        // 构建TaskDependency，用于识别末端Task
        var downstreamTasks = new HashSet<string>();
        foreach (var demandGroup in tasksByDemand)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandGroup.Key);
            if (demand == null) continue;

            // 获取工艺路线依赖关系
            if (!constraints.RoutingGraphs.TryGetValue(demand.MaterialId, out var routeGraphs))
                continue;
            if (!routeGraphs.TryGetValue("DEFAULT", out var routingGraph))
                continue;

            // 标记所有有downstream的Task（非末端）
            foreach (var depList in routingGraph.Dependencies.Values)
            {
                foreach (var dep in depList)
                {
                    var upstreamTask = demandGroup.Value
                        .FirstOrDefault(t => t.OperationCode == dep.FromOperationCode);
                    if (upstreamTask != null)
                    {
                        downstreamTasks.Add(upstreamTask.FinalDraftId);
                    }
                }
            }
        }

        // 为每个Allocation生成Share，只记录末端Task
        var tasksByAllocation = tasks
            .Select(task =>
            {
                var demand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);
                return new { Task = task, Demand = demand };
            })
            .Where(x => x.Demand != null)
            .GroupBy(x => x.Demand!.AllocationSequence);

        foreach (var group in tasksByAllocation)
        {
            var allocationSeq = group.Key;
            var expectedQty = group.First().Demand!.NetOutputQty;

            // 只为末端Task（没有downstream的Task）记录Share
            var endTasks = group
                .Where(x => !downstreamTasks.Contains(x.Task.FinalDraftId))
                .Select(x => x.Task)
                .ToList();

            if (endTasks.Count == 0)
            {
                // 没有末端Task（可能是单工序或全部Task都是中间工序）
                // 兜底：取最后一个Task
                var lastTask = group.OrderByDescending(x => x.Task.PlannedEndTime).First().Task;
                shares.Add(new AllocationTaskShare
                {
                    FinalDraftId = lastTask.FinalDraftId,
                    AllocationSequence = allocationSeq,
                    ComponentQty = expectedQty
                });
            }
            else if (endTasks.Count == 1)
            {
                // 单个末端Task：直接记录全量
                shares.Add(new AllocationTaskShare
                {
                    FinalDraftId = endTasks[0].FinalDraftId,
                    AllocationSequence = allocationSeq,
                    ComponentQty = expectedQty
                });
            }
            else
            {
                // 多个末端Task（可能是拆批、并行工艺等）
                // 按Task.Quantity比例分配，最后一个Task补差
                decimal totalTaskQty = endTasks.Sum(t => t.Quantity);
                for (int i = 0; i < endTasks.Count; i++)
                {
                    var task = endTasks[i];
                    decimal shareQty;

                    if (i == endTasks.Count - 1)
                    {
                        // 最后一个Task：补差闭合
                        var alreadyAllocated = shares
                            .Where(s => s.AllocationSequence == allocationSeq)
                            .Sum(s => s.ComponentQty);
                        shareQty = expectedQty - alreadyAllocated;
                    }
                    else if (totalTaskQty > 0)
                    {
                        // 按比例分配
                        shareQty = Math.Round(expectedQty * task.Quantity / totalTaskQty, 3);
                    }
                    else
                    {
                        // 异常：总数量为0，平均分配
                        shareQty = expectedQty / endTasks.Count;
                    }

                    shares.Add(new AllocationTaskShare
                    {
                        FinalDraftId = task.FinalDraftId,
                        AllocationSequence = allocationSeq,
                        ComponentQty = shareQty
                    });
                }
            }
        }

        return shares;
    }

    /// <summary>
    /// 生成 TaskDependency（基于 Routing 工序依赖关系）
    /// 文档：§五 5.3
    /// P0-13修复：根据工艺路线生成工序间的物理依赖关系
    /// </summary>
    private List<FinalTaskPeggingDraft> GenerateTaskDependencies(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var dependencies = new List<FinalTaskPeggingDraft>();

        // 按 SourceDraftId（需求）分组任务
        var tasksByDemand = tasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.PlannedStartTime).ToList());

        // 为每个需求生成工序依赖
        foreach (var demandGroup in tasksByDemand)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandGroup.Key);
            if (demand == null) continue;

            // 获取工艺路线
            if (!constraints.RoutingGraphs.TryGetValue(demand.MaterialId, out var routeGraphs))
                continue;
            if (!routeGraphs.TryGetValue("DEFAULT", out var routingGraph))
                continue;

            // 遍历工艺路线中的依赖关系
            // 第5轮修复：Split场景下，一个Operation可能对应多个Task，必须为所有组合建立Dependency
            foreach (var depList in routingGraph.Dependencies.Values)
            {
                foreach (var dep in depList)
                {
                    // 找到对应的所有上游Task和下游Task
                    var upstreamTasks = demandGroup.Value
                        .Where(t => t.OperationCode == dep.FromOperationCode)
                        .ToList();
                    var downstreamTasks = demandGroup.Value
                        .Where(t => t.OperationCode == dep.ToOperationCode)
                        .ToList();

                    // 为所有上下游Task组合建立Dependency
                    foreach (var upstreamTask in upstreamTasks)
                    {
                        foreach (var downstreamTask in downstreamTasks)
                        {
                            dependencies.Add(new FinalTaskPeggingDraft
                            {
                                UpstreamFinalDraftId = upstreamTask.FinalDraftId,
                                DownstreamFinalDraftId = downstreamTask.FinalDraftId,
                                UpstreamMaterialId = demand.MaterialId,
                                DownstreamMaterialId = demand.MaterialId,
                                Quantity = demand.NetOutputQty,
                                UOM = string.Empty,
                                InheritedPriority = demand.DemandSequence,
                                DependencyType = dep.DependencyType,
                                LagTime = dep.LagTime
                            });
                        }
                    }
                }
            }
        }

        return dependencies;
    }

    /// <summary>
    /// 第4轮Item 10：Phase5最终硬约束校验（§十一）
    /// 验证FinalTask结果是否违反硬约束
    /// 第5轮修复：增加TaskDependency校验
    /// </summary>
    private ValidationResult ValidateHardResult(
        List<FinalTaskDraft> tasks,
        List<AllocationTaskShare> allocationShares,
        List<FinalTaskPeggingDraft> taskDependencies,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        // 1. 验证每个Allocation的ΣShareQty == NetOutputQty
        var sharesByAllocation = allocationShares
            .GroupBy(s => s.AllocationSequence)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var demand in request.LogicalProductionDemands)
        {
            if (!sharesByAllocation.TryGetValue(demand.AllocationSequence, out var shares))
            {
                // 如果该Demand未排程，会在UnscheduledTasks中体现，不算硬约束失败
                continue;
            }

            decimal totalShare = shares.Sum(s => s.ComponentQty);
            if (Math.Abs(totalShare - demand.NetOutputQty) > 0.001m)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Allocation {demand.AllocationSequence} 数量闭合失败: ΣShareQty={totalShare}, NetOutputQty={demand.NetOutputQty}"
                };
            }

            // 2. 验证每个ShareQty > 0
            foreach (var share in shares)
            {
                if (share.ComponentQty <= 0)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Task {share.FinalDraftId} 的 ShareQty <= 0: {share.ComponentQty}"
                    };
                }
            }
        }

        // 3. 验证每个FinalTask的ΣShare不超过Task.Quantity
        var sharesByTask = allocationShares
            .GroupBy(s => s.FinalDraftId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.ComponentQty));

        foreach (var task in tasks)
        {
            if (sharesByTask.TryGetValue(task.FinalDraftId, out var totalTaskShare))
            {
                if (totalTaskShare > task.Quantity + 0.001m)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Task {task.FinalDraftId} 的 ΣShare={totalTaskShare} 超过 Quantity={task.Quantity}"
                    };
                }
            }
        }

        // 4. 验证FinalTask Resource硬互斥（同一资源上的时间段不重叠）
        var tasksByResource = tasks
            .GroupBy(t => t.ResourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.PlannedStartTime).ToList());

        foreach (var resourceGroup in tasksByResource.Values)
        {
            for (int i = 0; i < resourceGroup.Count - 1; i++)
            {
                var current = resourceGroup[i];
                var next = resourceGroup[i + 1];

                // 当前Task的结束时间（包括Setup）必须 <= 下一个Task的开始时间（Setup之前）
                var currentOccupancyStart = current.PlannedStartTime.AddMinutes(-(double)current.SetupTime);
                var nextOccupancyStart = next.PlannedStartTime.AddMinutes(-(double)next.SetupTime);

                if (current.PlannedEndTime > nextOccupancyStart)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Resource {current.ResourceId} 时间冲突: Task {current.FinalDraftId} [{currentOccupancyStart:HH:mm:ss}-{current.PlannedEndTime:HH:mm:ss}] 与 Task {next.FinalDraftId} [{nextOccupancyStart:HH:mm:ss}-{next.PlannedEndTime:HH:mm:ss}] 重叠"
                    };
                }
            }
        }

        // 5. 验证Task不早于Material AvailableTime
        // 第5轮修复：必须按累计数量达到Task所需Qty，取真正Material Ready Time
        foreach (var task in tasks)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);
            if (demand == null) continue;

            if (constraints.MaterialAvailability.TryGetValue(demand.AllocationSequence, out var segments) && segments.Count > 0)
            {
                // 按时间排序Segments，累计数量直到满足Task需求
                var sortedSegments = segments.OrderBy(s => s.AvailableTime).ToList();
                decimal cumulativeQty = 0m;
                DateTime? materialReadyTime = null;

                // Task所需的物料数量（取PlannedProcessQty，因为这是实际加工需要的数量）
                var requiredQty = task.PlannedProcessQty;

                foreach (var segment in sortedSegments)
                {
                    cumulativeQty += segment.Quantity;
                    if (cumulativeQty >= requiredQty)
                    {
                        materialReadyTime = segment.AvailableTime;
                        break;
                    }
                }

                // 如果累计数量仍不足，取最后一个Segment的时间（物料始终不足）
                if (materialReadyTime == null && sortedSegments.Count > 0)
                {
                    materialReadyTime = sortedSegments.Last().AvailableTime;
                }

                if (materialReadyTime.HasValue && task.PlannedStartTime < materialReadyTime.Value)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Task {task.FinalDraftId} 开始时间 {task.PlannedStartTime:yyyy-MM-dd HH:mm:ss} 早于物料累计可用时间 {materialReadyTime.Value:yyyy-MM-dd HH:mm:ss}"
                    };
                }
            }
        }

        // 6. 验证Locked Anchor是否保持原地
        foreach (var lockedTask in constraints.LockedTasks.Values)
        {
            var correspondingTask = tasks
                .FirstOrDefault(t => t.SourceDraftId == lockedTask.DraftId &&
                                    t.ResourceId == lockedTask.ResourceId);

            if (correspondingTask != null)
            {
                // 验证时间是否与锁定时间一致
                if (Math.Abs((correspondingTask.PlannedStartTime - lockedTask.LockedStart).TotalSeconds) > 1 ||
                    Math.Abs((correspondingTask.PlannedEndTime - lockedTask.LockedEnd).TotalSeconds) > 1)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Locked Task {lockedTask.DraftId} 时间未保持原地: 期望[{lockedTask.LockedStart:HH:mm:ss}-{lockedTask.LockedEnd:HH:mm:ss}], 实际[{correspondingTask.PlannedStartTime:HH:mm:ss}-{correspondingTask.PlannedEndTime:HH:mm:ss}]"
                    };
                }
            }
        }

        // 7. 第5轮修复：验证TaskDependency硬约束
        var taskDict = tasks.ToDictionary(t => t.FinalDraftId);
        foreach (var dep in taskDependencies)
        {
            // 检查上游Task是否存在
            if (!taskDict.TryGetValue(dep.UpstreamFinalDraftId, out var upstreamTask))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"TaskDependency引用的上游Task {dep.UpstreamFinalDraftId} 不存在"
                };
            }

            // 检查下游Task是否存在
            if (!taskDict.TryGetValue(dep.DownstreamFinalDraftId, out var downstreamTask))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"TaskDependency引用的下游Task {dep.DownstreamFinalDraftId} 不存在"
                };
            }

            // 检查时间约束：下游开始时间必须 >= 上游结束时间 + Lag
            var lagTime = TimeSpan.FromMinutes((double)dep.LagTime);
            var earliestDownstreamStart = upstreamTask.PlannedEndTime + lagTime;
            if (downstreamTask.PlannedStartTime < earliestDownstreamStart)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"TaskDependency违反时间约束: 下游Task {downstreamTask.FinalDraftId} 开始时间 {downstreamTask.PlannedStartTime:yyyy-MM-dd HH:mm:ss} 早于上游Task {upstreamTask.FinalDraftId} 结束时间 {upstreamTask.PlannedEndTime:yyyy-MM-dd HH:mm:ss} + Lag {dep.LagTime}分钟"
                };
            }
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    private class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
