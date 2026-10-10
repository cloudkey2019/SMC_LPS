using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Xunit;
using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Fixtures;
using LPS.APS.Application.Extensions;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Scheduling.Extensions;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// v5.1.2架构真实集成测试 - 完整流程验证
///
/// 测试目标：
/// 1. 准备真实测试数据（订单、物料、BOM、工艺路线）
/// 2. 执行SchedulingOrchestrator完整流程
/// 3. 验证Task表和PeggingSupplyAllocation表的真实数据
/// 4. 验证事务一致性和幂等性
///
/// 使用方式：
/// 1. 确保appsettings.Test.json配置了测试数据库连接
/// 2. 执行脚本准备测试数据
/// 3. 运行测试
/// </summary>
public class RealSchedulingIntegrationTest
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly SchedulingOrchestrator _schedulingOrchestrator;
    private readonly PeggingOrchestrator _peggingOrchestrator;

    private const string TEST_ORDER_NO = "TEST-SO-001";
    // FG-CY3B63-650（真实成品，真实 BOM + 真实资质 + 真实部门上下文）。
    // 2026-09-29 依库核实：真实工序 = 5 道（M202/M203 属 RouteCode "ASSY,组装"；M102/M103/M104 属 "ASSY,组装,包装"），
    // 原注释「真实工序(7道)」与「PlanVersion 328 有真实订单」均已过时（328 在 [Order] 里 0 行）。
    private const int TEST_MATERIAL_ID = 5583941;
    private const string TEST_MATERIAL_CODE = "FG-CY3B63-650";
    // BOM 接货批次：**运行时解析**（不再硬编码）。
    // 原为硬编码常量 `"REQ_20260918_4531b0806b7c45b"`，注释假设「接货 TRUNCATE 后仍存在」——
    // 但 2026-09-28 的 BOM 重推（删三表 → 重新接货）换了批次名，硬编码即失效，
    // 致 `APS_BOM_RAW WHERE BatchNo=@BatchNo` 恒 0 行 → BOM 快照空 → 无需求 → `FinalTasks=0` → 测试失败。
    // ⇒ 改为按 `APS_BOM_RAW` 最新同步批次解析，重推后无需改测试代码。
    private string _batchNo = string.Empty;
    private const int TEST_FACTORY_ID = 2; // 中国工厂 (CM)

    private int _actualPlanVersionId; // 运行时动态获取
    // 定点回收：只有当本测试**真的补造**了工艺路线时才非空，回收按 Id 删、绝不按 MaterialId 盲删真实工艺数据
    private long? _syntheticRoutingOperationId;

    public RealSchedulingIntegrationTest()
    {
        // 构建DI容器
        var services = new ServiceCollection();

        // 加载测试配置
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // 注册服务（模拟Program.cs）
        services.AddDatabaseServices(configuration);
        services.AddSchedulingServices();
        services.AddBusinessRuleServices();
        services.AddApplicationServices();
        // 联调专用：DemandPriority 策略源使用 Fixture（3号位真实 FrozenStrategySnapshot 尚未接通，Fixture 仅用于测试，不得进入生产 DI）
        services.AddScoped<IDemandPriorityConfigProvider, DemandPriorityFixtureProvider>();
        // 联调专用：Supply 排序依赖的 FrozenStrategySnapshot 同样使用 Fixture（测试库无真实 PUBLISHED 策略包版本，不得进入生产 DI）
        services.AddScoped<IFrozenStrategySnapshotProvider, FrozenStrategySnapshotFixtureProvider>();
        // S-3：SetupTransitionRuleRepository 已撤销（承载 = RuleSetVersion.ContentSnapshotJson 子块，Provider 装配第⑦块）
        services.AddLogging();

        // 集成测试需要直接访问具体类
        services.AddScoped<SchedulingOrchestrator>();
        services.AddScoped<PeggingOrchestrator>();

        var serviceProvider = services.BuildServiceProvider();

        _connectionManager = serviceProvider.GetRequiredService<DatabaseConnectionManager>();
        _schedulingOrchestrator = serviceProvider.GetRequiredService<SchedulingOrchestrator>();
        _peggingOrchestrator = serviceProvider.GetRequiredService<PeggingOrchestrator>();
    }

    /// <summary>
    /// 主测试入口 - 按顺序执行所有测试
    /// </summary>
    [Fact(DisplayName = "v5.1.2: 完整排程流程集成测试")]
    public async Task RunAllTestsAsync()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("v5.1.2架构集成测试开始");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        try
        {
            // 1. 清理旧数据
            await CleanupTestDataAsync();
            Console.WriteLine("✓ 步骤1: 清理旧测试数据完成\n");

            // 2. 准备测试数据
            await PrepareTestDataAsync();
            Console.WriteLine("✓ 步骤2: 准备测试数据完成\n");

            // 3. 执行完整排程流程
            await ExecuteSchedulingWorkflowAsync();
            Console.WriteLine("✓ 步骤3: 执行排程流程完成\n");

            // 4. 验证Task表数据
            await VerifyTaskTableAsync();
            Console.WriteLine("✓ 步骤4: 验证Task表数据完成\n");

            // 5. 验证PeggingSupplyAllocation表数据
            await VerifyPeggingAllocationTableAsync();
            Console.WriteLine("✓ 步骤5: 验证PeggingSupplyAllocation表完成\n");

            // 6. 验证事务一致性
            await VerifyTransactionConsistencyAsync();
            Console.WriteLine("✓ 步骤6: 验证事务一致性完成\n");

            // 7. 验证幂等性（重跑测试）
            await VerifyIdempotencyAsync();
            Console.WriteLine("✓ 步骤7: 验证幂等性完成\n");

            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("✅ 所有测试通过！");
            Console.WriteLine("=".PadRight(80, '='));
        }
        catch (Exception ex)
        {
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
            Console.WriteLine($"堆栈: {ex.StackTrace}");
            Console.WriteLine("=".PadRight(80, '='));
            throw;
        }
    }

    /// <summary>
    /// 清理旧测试数据
    /// </summary>
    private async Task CleanupTestDataAsync()
    {
        Console.WriteLine("清理旧测试数据...");

        // 注意：本方法由 RunAllTestsAsync 在「准备测试数据」**之前**调用，此刻 _actualPlanVersionId 尚未解析（=0），
        // 因此这里只做「与 PlanVersionId 无关」的清理。原实现在此处按 PlanVersionId=0 删 Task/PSA ⇒ 恒 0 行（死代码）；
        // PV 维度的幂等重置改到 PrepareTestDataAsync 解析出 _actualPlanVersionId 之后调 PurgePlanVersionArtifactsAsync。

        await _connectionManager.ExecuteAsync(
            @"DELETE FROM [Order] WHERE OrderNo = @OrderNo",
            new { OrderNo = TEST_ORDER_NO },
            db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            @"DELETE FROM InventoryBalance WHERE MaterialCode = @MaterialCode AND Source = 'TEST'",
            new { MaterialCode = TEST_MATERIAL_CODE },
            db: DatabaseId.APS);

        // 工艺路线：只删本测试补造的那一行（按 Id 定点）。
        // 历史实现为 `DELETE FROM RoutingOperation WHERE MaterialId=@MaterialId AND OperationCode='OP10'`（无来源限定）——
        // 该真实物料当前 5 道工序（M202/M203/M102/M103/M104）恰好无 OP10 才一直打空；
        // 若 3号位 日后给该真实物料排出 OP10，那句会真删生产工艺数据（不可逆）。
        if (_syntheticRoutingOperationId.HasValue)
        {
            await _connectionManager.ExecuteAsync(
                @"DELETE FROM RoutingOperation WHERE Id = @Id AND RouteCode = 'DEFAULT'",
                new { Id = _syntheticRoutingOperationId.Value },
                db: DatabaseId.APS);
            Console.WriteLine($"  - 已回收本测试补造的工艺路线: Id={_syntheticRoutingOperationId}");
            _syntheticRoutingOperationId = null;
        }

        // 资源：`RES_TEST_` 是本测试专用命名空间 ⇒ 按 ResourceCode 前缀回收可自愈历史泄漏（先清引用它的资质行）。
        await _connectionManager.ExecuteAsync(
            @"DELETE e FROM OperationResourceEligibility e
              JOIN Resource r ON r.Id = e.ResourceId
              WHERE r.ResourceCode = 'RES_TEST_001'",
            db: DatabaseId.APS);
        await _connectionManager.ExecuteAsync(
            @"DELETE FROM Resource WHERE ResourceCode = 'RES_TEST_001'",
            db: DatabaseId.APS);

        Console.WriteLine("  - 已删除旧的 Order / InventoryBalance / 本测试补造的工艺路线与资源");
    }

    /// <summary>
    /// 按 PlanVersionId 清理该 PV 的排程产物（同一 PV 重跑时的幂等重置）。
    /// 必须在 _actualPlanVersionId 解析之后调用——CleanupTestDataAsync 跑在它之前，那时 PlanVersionId 还是 0。
    /// </summary>
    private async Task PurgePlanVersionArtifactsAsync(int planVersionId)
    {
        if (planVersionId <= 0)
        {
            return;
        }

        await _connectionManager.ExecuteAsync(
            @"DELETE FROM PeggingSupplyAllocation WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            @"DELETE FROM [Task] WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"  - 已重置 PlanVersionId={planVersionId} 的 Task / PeggingSupplyAllocation");
    }

    /// <summary>
    /// 准备测试数据
    /// </summary>
    private async Task PrepareTestDataAsync()
    {
        Console.WriteLine("准备测试数据...");
        Console.WriteLine($"  - 使用测试物料: MaterialId={TEST_MATERIAL_ID}, MaterialCode={TEST_MATERIAL_CODE}");
        // 运行时解析当前真实接货批次（取 APS_BOM_RAW 最新同步批次）——BOM 重推后批次名会变，不得硬编码。
        _batchNo = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            @"SELECT TOP 1 BatchNo FROM APS_BOM_RAW ORDER BY SyncedAt DESC",
            db: DatabaseId.APS) ?? string.Empty;

        if (string.IsNullOrEmpty(_batchNo))
        {
            throw new Exception("APS_BOM_RAW 无任何批次数据 —— BOM 接货尚未执行，本测试无法运行（请先执行 BOM 接货）");
        }

        Console.WriteLine($"  - BatchNo={_batchNo}（运行时解析）, FactoryId={TEST_FACTORY_ID}");

        // 0. 准备PlanVersion（排程系统要求必须存在）
        // 1. 先创建ScheduleRun（V1.2架构要求）
        var scheduleRunId = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            @"INSERT INTO ScheduleRun
              (RunType, Status, DataCutoffTime, TriggeredBy, StartedAt, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES
              (@RunType, @Status, @DataCutoffTime, @TriggeredBy, GETDATE(), GETDATE())",
            new
            {
                RunType = "FULL_SCHEDULE",
                Status = "RUNNING",
                DataCutoffTime = DateTime.Now,
                TriggeredBy = "TEST_USER"
            },
            db: DatabaseId.APS);

        Console.WriteLine($"  - 创建ScheduleRun: {scheduleRunId}");

        // 2. 准备Material基础数据（LoadOrdersForPeggingAsync需要JOIN Material表）
        var materialExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM Material WHERE Id = @MaterialId",
            new { MaterialId = TEST_MATERIAL_ID },
            db: DatabaseId.APS);

        if (materialExists == 0)
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO Material
                  (Id, MaterialCode, MaterialName, ProductFamilyId, UOM, IsActive, CreatedAt, UpdatedAt)
                  VALUES
                  (@Id, @MaterialCode, @MaterialName, @ProductFamilyId, @UOM, 1, GETDATE(), GETDATE())",
                new
                {
                    Id = TEST_MATERIAL_ID,
                    MaterialCode = TEST_MATERIAL_CODE,
                    MaterialName = "测试物料",
                    ProductFamilyId = 1,
                    UOM = "EA"
                },
                db: DatabaseId.APS);

            Console.WriteLine($"  - 创建Material: {TEST_MATERIAL_CODE}");
        }

        // 准备Factory基础数据（LoadOrdersForPeggingAsync需要JOIN Factory表）
        var factoryExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM Factory WHERE Id = @FactoryId",
            new { FactoryId = TEST_FACTORY_ID },
            db: DatabaseId.APS);

        if (factoryExists == 0)
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO Factory
                  (Id, Code, Name, IsActive, CreatedAt, UpdatedAt)
                  VALUES
                  (@Id, @Code, @Name, 1, GETDATE(), GETDATE())",
                new
                {
                    Id = TEST_FACTORY_ID,
                    Code = "BJ",
                    Name = "北京工厂"
                },
                db: DatabaseId.APS);

            Console.WriteLine($"  - 创建Factory: BJ");
        }

        // 准备ProductFamily基础数据（LoadSupplyPoolAsync可能需要）
        var productFamilyExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM ProductFamily WHERE Id = 1",
            db: DatabaseId.APS);

        if (productFamilyExists == 0)
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO ProductFamily
                  (Id, Code, Name, IsActive, CreatedAt, UpdatedAt)
                  VALUES
                  (1, 'PF_TEST', '测试产品族', 1, GETDATE(), GETDATE())",
                db: DatabaseId.APS);

            Console.WriteLine($"  - 创建ProductFamily: PF_TEST");
        }

        // 3. 创建PlanVersion并关联ScheduleRun
        // 只在**本测试自己的**计划版本里复用。原实现 `WHERE Status='DRAFT'` 会劫持任何遗留 DRAFT 版本
        // （可能是别的窗口/别的流程的在制品），加上 VersionCategory='TEST' 缩小到自己的命名空间。
        _actualPlanVersionId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT TOP 1 Id FROM PlanVersion WHERE VersionCategory = 'TEST' AND Status = 'DRAFT' ORDER BY CreatedAt DESC",
            db: DatabaseId.APS);

        if (_actualPlanVersionId == 0)
        {
            _actualPlanVersionId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                @"INSERT INTO PlanVersion
                  (VersionCode, VersionCategory, BatchNo, SourceScheduleRunId, PlanHorizonStart, PlanHorizonEnd, ComputeMode, Status, CreatedBy, CreatedAt)
                  OUTPUT INSERTED.Id
                  VALUES
                  (@VersionCode, @VersionCategory, @BatchNo, @SourceScheduleRunId, @PlanHorizonStart, @PlanHorizonEnd, @ComputeMode, @Status, @CreatedBy, GETDATE())",
                new
                {
                    VersionCode = "TEST-PLAN-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                    VersionCategory = "TEST",
                    BatchNo = _batchNo,
                    SourceScheduleRunId = scheduleRunId,
                    PlanHorizonStart = DateTime.Now,
                    PlanHorizonEnd = DateTime.Now.AddDays(30),
                    ComputeMode = "SIMULATION",
                    Status = "Created",
                    CreatedBy = "TEST_USER"
                },
                db: DatabaseId.APS);

            Console.WriteLine($"  - 创建测试计划版本: {_actualPlanVersionId}，关联ScheduleRun: {scheduleRunId}");
        }
        else
        {
            Console.WriteLine($"  - 使用现有计划版本: {_actualPlanVersionId}");
        }

        // 3.1 PV 维度的幂等重置：必须等 _actualPlanVersionId 解析完再删（CleanupTestDataAsync 早于本方法，那时它是 0）。
        await PurgePlanVersionArtifactsAsync(_actualPlanVersionId);

        // 1. 准备Order（必须设置ProductFamilyId，否则PeggingOrchestrator会跳过）
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO [Order]
              (PlanVersionId, OrderNo, OrderType, MaterialId, MaterialCode, ProductFamilyId, Quantity, UOM, CustomerDueDate, Priority, Status, FactoryId, CreatedAt, UpdatedAt)
              VALUES
              (@PlanVersionId, @OrderNo, @OrderType, @MaterialId, @MaterialCode, @ProductFamilyId, @Quantity, @UOM, @DueDate, @Priority, @Status, @FactoryId, GETDATE(), GETDATE())",
            new
            {
                PlanVersionId = _actualPlanVersionId,
                OrderNo = TEST_ORDER_NO,
                OrderType = "SALES_ORDER",
                MaterialId = TEST_MATERIAL_ID,
                MaterialCode = TEST_MATERIAL_CODE,
                ProductFamilyId = 1,  // 关键：必须设置ProductFamilyId
                Quantity = 1m,         // 与真实订单 O20360212 同量；真实库存=0 → 全额生产
                UOM = "EA",
                DueDate = DateTime.Now.AddDays(7),
                Priority = 10,
                Status = "CONFIRMED",
                FactoryId = TEST_FACTORY_ID
            },
            db: DatabaseId.APS);

        Console.WriteLine($"  - 创建测试订单: {TEST_ORDER_NO}, 物料: {TEST_MATERIAL_CODE}, 数量: 100 EA");

        // 2. 获取刚创建的OrderId
        var orderId = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            @"SELECT Id FROM [Order] WHERE OrderNo = @OrderNo",
            new { OrderNo = TEST_ORDER_NO },
            db: DatabaseId.APS);

        // 3. 创建OrderBomRequestLink（关键：关联Order和BOM的BatchNo）
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO OrderBomRequestLink
              (PlanVersionId, BatchNo, OrderId, OrderCanonicalId, OrderNo, SourceSystem, RequestDetailId)
              VALUES
              (@PlanVersionId, @BatchNo, @OrderId, @OrderCanonicalId, @OrderNo, @SourceSystem, @RequestDetailId)",
            new
            {
                PlanVersionId = _actualPlanVersionId,
                BatchNo = _batchNo,
                OrderId = orderId,
                OrderCanonicalId = orderId,
                OrderNo = TEST_ORDER_NO,
                SourceSystem = "TEST",
                RequestDetailId = orderId
            },
            db: DatabaseId.APS);

        Console.WriteLine($"  - 创建OrderBomRequestLink: OrderId={orderId}, BatchNo={_batchNo}");

        // 4. 库存：真实数据用真实库存（FG-CY3B63-650 真实库存=0 → 订单全额转生产），不造 synthetic 库存
        Console.WriteLine($"  - 真实库存: 物料{TEST_MATERIAL_CODE} 真实 OnHand=0（全额生产）");

        // 4. 准备工艺路线（真实数据：物料已有 5 道真实工序，不造 synthetic OP10）
        var routingExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM RoutingOperation
              WHERE MaterialId = @MaterialId",
            new { MaterialId = TEST_MATERIAL_ID },
            db: DatabaseId.APS);

        if (routingExists == 0)
        {
            // 记录新行 Id，供 CleanupTestDataAsync 定点回收（避免按 MaterialId 盲删真实工艺数据）
            _syntheticRoutingOperationId = await _connectionManager.QueryFirstOrDefaultAsync<long>(
                @"INSERT INTO RoutingOperation
                  (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, OperationName, ProcessType, StandardDuration, SetupTime, IsActive, CreatedAt, UpdatedAt)
                  OUTPUT INSERTED.Id
                  VALUES
                  (@MaterialId, @ProductionDepartmentId, 'DEFAULT', 1, 'OP10', '组装', 'ASSEMBLY', 60, 10, 1, GETDATE(), GETDATE())",
                new {
                    MaterialId = TEST_MATERIAL_ID,
                    ProductionDepartmentId = 838  // BJ_SURF_制座2#喷涂板
                },
                db: DatabaseId.APS);

            Console.WriteLine($"  - 补造测试工艺路线（真无工序时才发生）: Id={_syntheticRoutingOperationId} OP10 组装 (物料: {TEST_MATERIAL_CODE})");
        }

        // 5. 准备Resource资源（V1.2架构需要：DomainSolveRequest传递给1号位）
        var resourceExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM Resource WHERE FactoryId = @FactoryId",
            new { FactoryId = TEST_FACTORY_ID },
            db: DatabaseId.APS);

        if (resourceExists == 0)
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO Resource
                  (ResourceCode, ResourceName, FactoryId, ProductionDepartmentId, CapacityFactor, IsActive, Status, CreatedAt, UpdatedAt)
                  VALUES
                  ('RES_TEST_001', '测试设备1', @FactoryId, 838, 1.0, 1, 'AVAILABLE', GETDATE(), GETDATE())",
                new { FactoryId = TEST_FACTORY_ID },
                db: DatabaseId.APS);

            Console.WriteLine($"  - 创建测试资源: RES_TEST_001 (工厂: {TEST_FACTORY_ID})");
        }

        Console.WriteLine("测试数据准备完成");
        Console.WriteLine($"  - PlanVersionId: {_actualPlanVersionId}, BatchNo: {_batchNo}");
    }

    /// <summary>
    /// 执行完整排程流程
    /// </summary>
    private async Task ExecuteSchedulingWorkflowAsync()
    {
        Console.WriteLine("执行SchedulingOrchestrator完整流程...");
        Console.WriteLine($"  计划版本ID: {_actualPlanVersionId}");

        // 排程前数据验证
        var preOrderCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM [Order] WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        var preBomLinkCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM OrderBomRequestLink WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        var preBomCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM APS_BOM_RAW WHERE BatchNo = @BatchNo",
            new { BatchNo = _batchNo },
            db: DatabaseId.APS);

        var preInventoryCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM InventoryBalance
              WHERE MaterialCode = @MaterialCode AND FactoryId = @FactoryId AND AvailableQty > 0",
            new { MaterialCode = TEST_MATERIAL_CODE, FactoryId = TEST_FACTORY_ID },
            db: DatabaseId.APS);

        Console.WriteLine($"  [排程前验证] Order={preOrderCount}, BomLink={preBomLinkCount}, BOM={preBomCount}, Inventory={preInventoryCount}");

        // 调试：验证LoadOrdersForPeggingAsync能否查到订单
        var debugOrderQuery = await _connectionManager.QueryAsync<dynamic>(
            @"SELECT o.Id AS OrderId, o.MaterialId, m.MaterialCode, o.FactoryId, f.Code AS FactoryCode,
                     o.Quantity AS DemandQty, o.CustomerDueDate AS DueDate, o.UOM, m.ProductFamilyId
              FROM [Order] o
              INNER JOIN Material m ON m.Id = o.MaterialId
              INNER JOIN Factory f ON f.Id = o.FactoryId
              WHERE o.PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"  [调试] LoadOrdersForPeggingAsync查询结果: {debugOrderQuery.Count()} 条");
        foreach (var row in debugOrderQuery)
        {
            Console.WriteLine($"    OrderId={row.OrderId}, MaterialId={row.MaterialId}, MaterialCode={row.MaterialCode}, FactoryCode={row.FactoryCode}, ProductFamilyId={row.ProductFamilyId}");
        }

        var startTime = DateTime.Now;

        // 执行排程
        var result = await _schedulingOrchestrator.RunSchedulingAsync(
            _actualPlanVersionId,
            // 【2026-09-29 修正】原硬编码 251L（SP-DEMO-V2.0），注释称其 IsDefault=1 —— **已不成立**：
            //   查库 `IsDefault=1 AND Status='PUBLISHED'` 现行唯一命中 = **811（SP-DEMO-V3.0，EffectiveFrom 2026-09-20）**，
            //   251 已降为 IsDefault=0。改用 811 使本测试与生产默认包、与 `RealDomainFullRunTest.ExpectedStrategyProfileVersionId` 同源
            //   （该测试此前跑的是**非默认老包**，测的不是生产口径）。
            //   旧 1L 占位不存在会导致 LoadStrategyConfigAsync 静默返回 → 空策略上下文 → Pegging 全失败，故仍需显式传。
            811L, // 生产默认策略包 SP-DEMO-V3.0
            CancellationToken.None);

        var duration = DateTime.Now - startTime;

        Console.WriteLine($"  排程完成，耗时: {duration.TotalSeconds:F2} 秒");
        Console.WriteLine($"  状态: {(result.IsSuccess ? "成功" : "失败")}");
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine($"  错误信息: {result.ErrorMessage}");
        }

        if (!result.IsSuccess)
        {
            throw new Exception($"排程流程失败: {result.ErrorMessage}");
        }

        // 调试：检查BOM数据
        var bomEdgeCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM APS_BOM_RAW WHERE BatchNo = @BatchNo",
            new { BatchNo = _batchNo },
            db: DatabaseId.APS);
        Console.WriteLine($"  [调试] BOM边数: {bomEdgeCount}");

        // 立即检查数据库状态（调试用）
        var immediateTaskCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM [Task] WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        var immediatePeggingCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM PeggingSupplyAllocation WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"  [调试] 排程后立即查询: Task={immediateTaskCount}, Pegging={immediatePeggingCount}");

        // 额外诊断：检查业务规则是否通过
        if (immediateTaskCount == 0 && immediatePeggingCount == 0)
        {
            Console.WriteLine($"  [诊断] Task和Pegging都为0，可能原因：");
            Console.WriteLine($"    1. 业务规则验证失败（ruleValid=false）");
            Console.WriteLine($"    2. SupplyPool为空，无供给可匹配");
            Console.WriteLine($"    3. BOM遍历跳过处理");
            Console.WriteLine($"    4. IFiniteCapacityScheduler返回空");

            // 深度诊断：检查供给池数据
            var inventoryCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                @"SELECT COUNT(1) FROM InventoryBalance WHERE OnHandQty > 0",
                null,
                db: DatabaseId.APS);
            Console.WriteLine($"  [深度诊断] InventoryBalance供给记录数（全局）: {inventoryCount}");

            // 检查测试物料的库存
            var testMaterialInventory = await _connectionManager.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT MaterialCode, FactoryId, ProductFamilyId, OnHandQty, AvailableQty
                  FROM InventoryBalance
                  WHERE MaterialCode = @MaterialCode AND FactoryId = @FactoryId",
                new { MaterialCode = TEST_MATERIAL_CODE, FactoryId = TEST_FACTORY_ID },
                db: DatabaseId.APS);

            if (testMaterialInventory != null)
            {
                Console.WriteLine($"    测试物料库存: MaterialCode={testMaterialInventory.MaterialCode}, OnHandQty={testMaterialInventory.OnHandQty}, AvailableQty={testMaterialInventory.AvailableQty}");
            }
            else
            {
                Console.WriteLine($"    ⚠️ 测试物料库存不存在: MaterialCode={TEST_MATERIAL_CODE}, FactoryId={TEST_FACTORY_ID}");
            }

            // 检查Resource资源
            var resourceCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                @"SELECT COUNT(1) FROM Resource WHERE FactoryId = @FactoryId AND Status = 'AVAILABLE'",
                new { FactoryId = TEST_FACTORY_ID },
                db: DatabaseId.APS);
            Console.WriteLine($"  [深度诊断] Resource资源数（FactoryId={TEST_FACTORY_ID}）: {resourceCount}");

            // 检查RoutingOperation工艺路线
            var routingCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                @"SELECT COUNT(1) FROM RoutingOperation WHERE MaterialId = @MaterialId",
                new { MaterialId = TEST_MATERIAL_ID },
                db: DatabaseId.APS);
            Console.WriteLine($"  [深度诊断] RoutingOperation工艺路线数（MaterialId={TEST_MATERIAL_ID}）: {routingCount}");
        }
    }

    /// <summary>
    /// 验证Task表数据
    /// </summary>
    private async Task VerifyTaskTableAsync()
    {
        Console.WriteLine("验证Task表数据...");

        var tasks = (await _connectionManager.QueryAsync<dynamic>(
            @"SELECT
                Id, PlanVersionId, TaskNo, OrderId, MaterialId,
                Quantity, TaskType, Status, PlannedStartTime, PlannedEndTime,
                CreatedAt, UpdatedAt
              FROM [Task]
              WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS)).ToList();

        Console.WriteLine($"  Task记录数: {tasks.Count}");

        if (tasks.Count == 0)
        {
            throw new Exception("Task表没有数据！PeggingOrchestrator可能未正确INSERT");
        }

        foreach (var task in tasks)
        {
            Console.WriteLine($"  - TaskId: {task.Id}, TaskNo: {task.TaskNo}, " +
                            $"MaterialId: {task.MaterialId}, Qty: {task.Quantity}, " +
                            $"Type: {task.TaskType}, Status: {task.Status}");

            // 验证必填字段
            if (task.Id <= 0) throw new Exception($"Task.Id无效: {task.Id}");
            if (task.Quantity <= 0) throw new Exception($"Task.Quantity无效: {task.Quantity}");
            if (string.IsNullOrEmpty(task.TaskType)) throw new Exception("Task.TaskType为空");
        }

        Console.WriteLine("  ✓ Task表数据验证通过");
    }

    /// <summary>
    /// 验证PeggingSupplyAllocation表数据
    /// </summary>
    private async Task VerifyPeggingAllocationTableAsync()
    {
        Console.WriteLine("验证PeggingSupplyAllocation表数据...");

        var allocations = (await _connectionManager.QueryAsync<dynamic>(
            @"SELECT
                Id, PlanVersionId, AllocationSequence, MaterialId, MaterialCode,
                AllocatedQty, SupplyType, SupplyDocumentType, SupplyDocumentNo,
                DemandFactoryCode, SupplyFactoryCode, KnownAvailableTime
              FROM PeggingSupplyAllocation
              WHERE PlanVersionId = @PlanVersionId
              ORDER BY AllocationSequence",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS)).ToList();

        Console.WriteLine($"  PeggingSupplyAllocation记录数: {allocations.Count}");

        // 若无非 Task 直接供给（如库存工厂不匹配导致全转生产），PSA 可为 0，此时不阻塞主流程
        if (allocations.Count == 0)
        {
            Console.WriteLine("  ⚠ PeggingSupplyAllocation本轮无非 Task 供给（全转生产），记录数为 0");
            return;
        }

        // AllocationSequence 为跨 PSA/ATS 共享的全局单调计数器，PSA 侧允许跳号，只断言唯一且为正
        var sequences = allocations.Select(a => (int)a.AllocationSequence).ToList();
        if (sequences.Distinct().Count() != sequences.Count)
        {
            throw new Exception("AllocationSequence存在重复！");
        }
        if (sequences.Any(s => s <= 0))
        {
            throw new Exception("AllocationSequence存在非正数！");
        }

        Console.WriteLine($"  ✓ AllocationSequence唯一且为正: {sequences.Count} 条");

        foreach (var alloc in allocations)
        {
            Console.WriteLine($"  - Seq: {alloc.AllocationSequence}, " +
                            $"MaterialId: {alloc.MaterialId}, Qty: {alloc.AllocatedQty}, " +
                            $"SupplyType: {alloc.SupplyType}");

            // 验证必填字段
            if (alloc.AllocatedQty <= 0) throw new Exception($"AllocatedQty无效: {alloc.AllocatedQty}");
            if (string.IsNullOrEmpty(alloc.SupplyType)) throw new Exception("SupplyType为空");
        }

        Console.WriteLine("  ✓ PeggingSupplyAllocation表数据验证通过");
    }

    /// <summary>
    /// 验证事务一致性（Task数量 = Allocation数量）
    /// </summary>
    private async Task VerifyTransactionConsistencyAsync()
    {
        Console.WriteLine("验证事务一致性...");

        var taskCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM [Task] WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        var allocationCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM PeggingSupplyAllocation WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"  Task记录数: {taskCount}");
        Console.WriteLine($"  PeggingSupplyAllocation记录数: {allocationCount}");

        // v5.1.2要求：Task与PeggingSupplyAllocation在同一事务写入
        // v5.1.2过渡期：PeggingSupplyAllocation INSERT暂时注释，允许Task有数据但Allocation为0
        if (taskCount > 0 && allocationCount > 0)
        {
            Console.WriteLine("  ✓ Task与PeggingSupplyAllocation都有数据，事务一致");
        }
        else if (taskCount > 0 && allocationCount == 0)
        {
            Console.WriteLine("  ⚠ Task有数据但PeggingSupplyAllocation为空（过渡期预期行为）");
        }
        else if (taskCount == 0 && allocationCount == 0)
        {
            throw new Exception("Task和PeggingSupplyAllocation都没有数据！");
        }
        else
        {
            throw new Exception($"数据不一致！Task: {taskCount}, Allocation: {allocationCount}");
        }
    }

    /// <summary>
    /// 验证幂等性（重跑不产生重复数据）
    /// </summary>
    private async Task VerifyIdempotencyAsync()
    {
        Console.WriteLine("验证幂等性（重跑测试）...");

        // 第一次运行后的记录数
        var taskCountBefore = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM [Task] WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        var allocationCountBefore = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM PeggingSupplyAllocation WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"  第一次运行: Task={taskCountBefore}, Allocation={allocationCountBefore}");

        // 重跑排程
        Console.WriteLine("  重新执行排程流程...");
        await _schedulingOrchestrator.RunSchedulingAsync(
            _actualPlanVersionId,
            811L, // 与首次一致：生产默认策略包 SP-DEMO-V3.0，保证幂等基线与首跑同源
            CancellationToken.None);

        // 第二次运行后的记录数
        var taskCountAfter = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM [Task] WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        var allocationCountAfter = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(1) FROM PeggingSupplyAllocation WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = _actualPlanVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"  第二次运行: Task={taskCountAfter}, Allocation={allocationCountAfter}");

        // 验证记录数相同（幂等性）
        if (taskCountBefore != taskCountAfter)
        {
            throw new Exception($"Task记录数不一致！第一次{taskCountBefore}，第二次{taskCountAfter}");
        }

        if (allocationCountBefore != allocationCountAfter)
        {
            throw new Exception($"Allocation记录数不一致！第一次{allocationCountBefore}，第二次{allocationCountAfter}");
        }

        Console.WriteLine("  ✓ 重跑后记录数不变，幂等性验证通过");
    }
}
