using System;
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
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 最小自洽合成集成测试 —— 不依赖真实数据的「代码链绿 gate」。
///
/// 背景：<see cref="RealSchedulingIntegrationTest"/> 走真实物料数据，每次红都被 3 道真实数据卡点拖住
/// （① 设备编码 EquipNo↔MachNO 不一致 ② 半成品未归族 ③ 配品无工序/首工序缺资格），与 2号位 代码无关。
/// 本测试用一套【全自洽合成夹具】钉住同一条代码链
///   Order → (无 BOM) → Pegging NEW_REQUIREMENT → LogicalProductionDemand → Routing(1工序) ─┐
///   → OperationResourceEligibility + Resource(7x24 合成日历) + MaterialStageDeptContext ──┤
///   → 1号位 Solver → FinalTask > 0 → [Task] 落库
/// 把所有「部门锁定 / 资质 / 产能系数」对上游装载的依赖全部显式闭环，证的是代码链而非业务数据。
///
/// 复用 RealSchedulingIntegrationTest 的 DI 脚手架（Fixture 冻结策略 + 真实策略包 251L）：
/// 策略包/策略快照属于「配置」而非被卡住的「真实数据」，故沿用既有种子值，只替换数据侧。
/// </summary>
public class SyntheticSchedulingIntegrationTest
{
    // ── 合成夹具唯一标识（固定业务键，测试内幂等清理；与真实数据 Id 空间隔离）──
    private const int    TEST_MATERIAL_ID   = 9900001;
    private const string TEST_MATERIAL_CODE = "SYN-FG-001";
    private const int    TEST_DEPT_ID       = 999001;
    private const string TEST_RESOURCE_CODE = "SYN-RES-001";
    private const string TEST_ORDER_NO      = "SYN-SO-001";
    private const string TEST_BATCH_NO      = "SYN-REQ-20260921";
    private const string STAGE_CODE         = "SYN-STAGE-1";
    private const string OPERATION_CODE     = "OP10";
    private const string ROUTE_CODE         = "DEFAULT";
    private const int    PATH_ID            = 1;
    private const int    TEST_FACTORY_ID    = 2;          // BJ（复用既有工厂，同 RealSchedulingIntegrationTest）
    private const long   STRATEGY_VERSION   = 251L;       // B 项种子后真实策略包 SP-DEMO-V2.0（同 RealSchedulingIntegrationTest）

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly SchedulingOrchestrator _schedulingOrchestrator;

    public SyntheticSchedulingIntegrationTest()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        services.AddSchedulingServices();
        services.AddBusinessRuleServices();
        services.AddApplicationServices();
        // 联调专用：测试库无真实 PUBLISHED 策略包，Fixture 仅测试用，不得进生产 DI。
        services.AddScoped<IDemandPriorityConfigProvider, DemandPriorityFixtureProvider>();
        services.AddScoped<IFrozenStrategySnapshotProvider, FrozenStrategySnapshotFixtureProvider>();
        // S-3：SetupTransitionRuleRepository 已撤销（承载 = RuleSetVersion.ContentSnapshotJson 子块，Provider 装配第⑦块）
        services.AddLogging();
        services.AddScoped<SchedulingOrchestrator>();

        var serviceProvider = services.BuildServiceProvider();
        _connectionManager = serviceProvider.GetRequiredService<DatabaseConnectionManager>();
        _schedulingOrchestrator = serviceProvider.GetRequiredService<SchedulingOrchestrator>();
    }

    [Fact(DisplayName = "合成自洽夹具：全链排程产 Task（不依赖真实数据）")]
    public async Task SyntheticSelfConsistentFixture_ProducesScheduledTasks()
    {
        // 0. 幂等清理：删掉上次运行残留的合成夹具行（真实数据不动）。
        await CleanupAsync();

        // 1. 装配全自洽合成夹具（订单 + 无 BOM + 1 工序 + 资质 + 资源 + 部门上下文，同一部门闭环）。
        var planVersionId = await SeedAsync();

        // 2. 跑完整排程链（装载 → Pegging → 1号位 Solver → Task 落库）。
        var result = await _schedulingOrchestrator.RunSchedulingAsync(
            planVersionId, STRATEGY_VERSION, CancellationToken.None);

        Assert.True(result.IsSuccess, $"合成排程失败: {result.ErrorMessage}");

        // 3. 断言：Task 已落库且被真实排定（PlannedStartTime/PlannedEndTime 非空 = 非 Unscheduled）。
        var tasks = (await _connectionManager.QueryAsync<dynamic>(
            @"SELECT Id, PlanVersionId, MaterialId, Quantity, PlannedStartTime, PlannedEndTime
              FROM [Task]
              WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS)).ToList();

        if (tasks.Count == 0)
        {
            throw new InvalidOperationException("Task 表无数据：合成夹具未产出任务（代码链断裂）");
        }

        var foundMaterial = false;
        var anyScheduled = false;
        foreach (var task in tasks)
        {
            if ((int)task.MaterialId == TEST_MATERIAL_ID)
            {
                foundMaterial = true;
            }

            if (task.PlannedStartTime != null && task.PlannedEndTime != null)
            {
                anyScheduled = true;
                if ((decimal)task.Quantity <= 0)
                {
                    throw new InvalidOperationException($"Task.Quantity 无效: {task.Quantity}");
                }
            }
        }

        Assert.True(foundMaterial, "Task 中未找到合成物料，物料链未贯通");
        Assert.True(anyScheduled, "所有 Task 均为 Unscheduled（PlannedStart/End 为空），求解未排定");
    }

    /// <summary>
    /// 真实数据 FULL 集成测试——复用合成夹具的 DI 脚手架，对**指定的**真实 PlanVersion 跑全链。
    /// 验证人工槽 OperationName 切主关联、ResourceCode 通道、PI续排 NextOperation、零资格兜底诊断未破坏全链。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 【2026-09-29 修复】原为无闸门 `[Fact]` + **硬编码 `PlanVersionId = 540`**，两处都错：
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>红灯根因</b>：`540` 及其 `[Order]`（11,661 条）已被 2026-09-29 的授权测试残留清理删除
    /// ⇒ `RunSchedulingAsync` 在版本校验处即抛「计划版本不存在」⇒ **该测试恒失败**。
    /// 教训与 `DomainDefinitionGovernanceServiceTests` 同一类：**集成测试不能钉死一个会被清掉的库 id**。
    /// </item>
    /// <item>
    /// <b>无闸门</b>：它每次 `dotnet test` 都会**真跑一次数分钟的全链排程并写库**。
    /// 先前只因「PV 不存在」提前失败才没写进去 —— 属侥幸。现与
    /// <c>RealDomainFullRunTest</c> 统一口径：须显式设 <c>APS_REAL_DOMAIN_RUN=1</c> 才跑。
    /// </item>
    /// </list>
    /// <para>
    /// 因此目标 PlanVersion 改为**由环境变量 <c>APS_REAL_PLAN_VERSION_ID</c> 指定**，默认跳过。
    /// 例：<c>APS_REAL_DOMAIN_RUN=1 APS_REAL_PLAN_VERSION_ID=633 dotnet test --filter RealFullData_ProducesTasks</c>
    /// （`633` = `SEAM_20260929_FAMILY_X`，自带 4,109 条订单）。两个变量缺一即 Skip，不再用陈旧 id 冒充绿灯。
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task RealFullData_ProducesTasks()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("APS_REAL_DOMAIN_RUN") == "1",
            "真实 FULL 排程会真跑数分钟并写库；须显式设 APS_REAL_DOMAIN_RUN=1 才运行。");

        var planVersionIdRaw = Environment.GetEnvironmentVariable("APS_REAL_PLAN_VERSION_ID");
        Skip.If(string.IsNullOrWhiteSpace(planVersionIdRaw),
            "未指定 APS_REAL_PLAN_VERSION_ID —— 本测试不再钉死库 id（原硬编码 540 已被清理删除，见 remarks）。");
        var planVersionId = int.Parse(planVersionIdRaw!);

        const long strategyVersion = 811; // SP-DEMO-V3.0 (IsDefault=1)

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _schedulingOrchestrator.RunSchedulingAsync(planVersionId, strategyVersion, CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.IsSuccess, $"真实 FULL 排程失败: {result.ErrorMessage}");

        var tasks = (await _connectionManager.QueryAsync<dynamic>(
            @"SELECT COUNT(*) Total,
                     SUM(CASE WHEN PlannedStartTime IS NOT NULL THEN 1 ELSE 0 END) Scheduled,
                     SUM(CASE WHEN ManualSlotId IS NOT NULL THEN 1 ELSE 0 END) OnManualSlot,
                     SUM(CASE WHEN MTS_InstructionNo IS NOT NULL THEN 1 ELSE 0 END) WithMTS,
                     SUM(ISNULL(Quantity,0)) TotalQty
              FROM [Task] WITH(NOLOCK)
              WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS)).First();

        var explanationCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(*) FROM ScheduleExplanationFact WITH(NOLOCK) WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        Console.WriteLine($"=== 真实 FULL 排程结果 ===");
        Console.WriteLine($"耗时: {stopwatch.Elapsed.TotalSeconds:F0}s");
        Console.WriteLine($"Task: Total={(int?)tasks.Total}, Scheduled={(int?)tasks.Scheduled}, ManualSlot={(int?)tasks.OnManualSlot}, MTS={(int?)tasks.WithMTS}, Qty={(decimal?)tasks.TotalQty}");
        Console.WriteLine($"Unscheduled/Explanation: {explanationCount}");

        Assert.True((int?)tasks.Total > 0, "Task 表无数据：真实 FULL 未产出任何任务");
        Assert.True((int?)tasks.Scheduled > 0, "所有 Task 均为 Unscheduled：求解未排定任何任务");
    }

    /// <summary>删除合成夹具行（按固定合成业务键精准删除，不触碰真实数据；依赖顺序由外向内）。</summary>
    private async Task CleanupAsync()
    {
        await _connectionManager.ExecuteAsync(
            "DELETE FROM OrderBomRequestLink WHERE BatchNo = @BatchNo",
            new { BatchNo = TEST_BATCH_NO }, db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            "DELETE FROM [Order] WHERE OrderNo = @OrderNo",
            new { OrderNo = TEST_ORDER_NO }, db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            "DELETE FROM MaterialStageDeptContext WHERE MaterialId = @MaterialId",
            new { MaterialId = TEST_MATERIAL_ID }, db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            "DELETE FROM OperationResourceEligibility WHERE MaterialId = @MaterialId",
            new { MaterialId = TEST_MATERIAL_ID }, db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            "DELETE FROM RoutingOperation WHERE MaterialId = @MaterialId",
            new { MaterialId = TEST_MATERIAL_ID }, db: DatabaseId.APS);

        // Resource 为 IDENTITY，按 ResourceCode 幂等删除（不依赖自增 Id）。
        await _connectionManager.ExecuteAsync(
            "DELETE FROM Resource WHERE ResourceCode = @ResourceCode",
            new { ResourceCode = TEST_RESOURCE_CODE }, db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            "DELETE FROM Material WHERE Id = @MaterialId",
            new { MaterialId = TEST_MATERIAL_ID }, db: DatabaseId.APS);

        // 生产部门最后删（RoutingOperation/Resource/MSC/资质 FK 指向它）。
        await _connectionManager.ExecuteAsync(
            "DELETE FROM ProductionDepartment WHERE Id = @DeptId",
            new { DeptId = TEST_DEPT_ID }, db: DatabaseId.APS);
    }

    private async Task<int> SeedAsync()
    {
        // ── 基础字典（复用/补齐，同 RealSchedulingIntegrationTest 口径；真实库已有 Factory 2 / PF 1）──
        var factoryExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(1) FROM Factory WHERE Id = @Id", new { Id = TEST_FACTORY_ID }, db: DatabaseId.APS);
        if (factoryExists == 0)
        {
            await _connectionManager.ExecuteAsync(
                "INSERT INTO Factory (Id, Code, Name, IsActive, CreatedAt, UpdatedAt) VALUES (@Id, 'BJ', '北京工厂', 1, GETDATE(), GETDATE())",
                new { Id = TEST_FACTORY_ID }, db: DatabaseId.APS);
        }

        var pfExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(1) FROM ProductFamily WHERE Id = 1", null, db: DatabaseId.APS);
        if (pfExists == 0)
        {
            await _connectionManager.ExecuteAsync(
                "INSERT INTO ProductFamily (Id, Code, Name, IsActive, CreatedAt, UpdatedAt) VALUES (1, 'PF_TEST', '测试产品族', 1, GETDATE(), GETDATE())",
                db: DatabaseId.APS);
        }

        // ── 物料（合成成品，无 BOM → 根节点即生产需求）。
        //    Material.Id 为 IDENTITY（sp_SyncMasterData 以 IDENTITY_INSERT 灌 ERP 业务键）；
        //    合成夹具需落固定业务键 9900001，故临时开 IDENTITY_INSERT，插完立刻 OFF（会话级，不影响其它表）。──
        await _connectionManager.ExecuteAsync("SET IDENTITY_INSERT Material ON", db: DatabaseId.APS);
        try
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO Material
                    (Id, MaterialCode, MaterialName, MaterialType, ProductFamilyId, UOM, LeadTimeDays, SafetyStock, LowLevelCode, IsPurchased, IsSimpleItem, IsActive, CreatedAt, UpdatedAt)
                  VALUES
                    (@Id, @MaterialCode, '合成成品', 'MFG', 1, 'EA', 0, 0, 0, 0, 0, 1, GETDATE(), GETDATE())",
                new { Id = TEST_MATERIAL_ID, MaterialCode = TEST_MATERIAL_CODE }, db: DatabaseId.APS);
        }
        finally
        {
            await _connectionManager.ExecuteAsync("SET IDENTITY_INSERT Material OFF", db: DatabaseId.APS);
        }

        // ── 生产部门（合成，Routing 三件套 / Resource / MaterialStageDeptContext 的 ProductionDepartmentId FK 均指向此；
        //    ProductionDepartment.Id 为 IDENTITY，临时开 IDENTITY_INSERT 落固定业务键 999001；StageCode 为软引用无 FK）──
        await _connectionManager.ExecuteAsync("SET IDENTITY_INSERT ProductionDepartment ON", db: DatabaseId.APS);
        try
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO ProductionDepartment (Id, DeptCode, DeptName, FactoryId, StageCode, IsActive, CreatedAt, UpdatedAt)
                  VALUES (@Id, 'SYN-DEPT-001', '合成部门', @FactoryId, @StageCode, 1, GETDATE(), GETDATE())",
                new { Id = TEST_DEPT_ID, FactoryId = TEST_FACTORY_ID, StageCode = STAGE_CODE },
                db: DatabaseId.APS);
        }
        finally
        {
            await _connectionManager.ExecuteAsync("SET IDENTITY_INSERT ProductionDepartment OFF", db: DatabaseId.APS);
        }

        // ── ScheduleRun + PlanVersion（合成，DomainKey 显式落值，避免 NULL 域键）──
        var scheduleRunId = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            @"INSERT INTO ScheduleRun (RunType, Status, DataCutoffTime, TriggeredBy, StartedAt, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES ('FULL_SCHEDULE', 'RUNNING', GETDATE(), 'TEST_SYN', GETDATE(), GETDATE())",
            db: DatabaseId.APS);

        var versionCode = "SYN-PLAN-" + DateTime.Now.ToString("yyyyMMddHHmmss");
        var planVersionId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO PlanVersion
                (VersionCode, VersionCategory, BatchNo, SourceScheduleRunId, DomainKey, PlanHorizonStart, PlanHorizonEnd, ComputeMode, Status, CreatedBy, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES
                (@VersionCode, 'TEST', @BatchNo, @SourceScheduleRunId, 'SYN-DOMAIN', GETDATE(), DATEADD(DAY, 30, GETDATE()), 'SIMULATION', 'Created', 'TEST_SYN', GETDATE())",
            new { VersionCode = versionCode, BatchNo = TEST_BATCH_NO, SourceScheduleRunId = scheduleRunId },
            db: DatabaseId.APS);

        // ── 订单（合成，SALES_ORDER，交期在 7x24 合成日历窗内）──
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO [Order]
                (PlanVersionId, OrderNo, OrderType, MaterialId, MaterialCode, ProductFamilyId, Quantity, UOM, CustomerDueDate, Priority, Status, FactoryId, CreatedAt, UpdatedAt)
              VALUES
                (@PlanVersionId, @OrderNo, 'SALES_ORDER', @MaterialId, @MaterialCode, 1, 1, 'EA', DATEADD(DAY, 7, GETDATE()), 10, 'CONFIRMED', @FactoryId, GETDATE(), GETDATE())",
            new
            {
                PlanVersionId = planVersionId,
                OrderNo = TEST_ORDER_NO,
                MaterialId = TEST_MATERIAL_ID,
                MaterialCode = TEST_MATERIAL_CODE,
                FactoryId = TEST_FACTORY_ID
            },
            db: DatabaseId.APS);

        var orderId = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            "SELECT Id FROM [Order] WHERE OrderNo = @OrderNo",
            new { OrderNo = TEST_ORDER_NO }, db: DatabaseId.APS);

        // OrderBomRequestLink：列集精确对齐 RealSchedulingIntegrationTest 已证路径（无 SyncedAt）。
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO OrderBomRequestLink
                (PlanVersionId, BatchNo, OrderId, OrderCanonicalId, OrderNo, SourceSystem, RequestDetailId)
              VALUES
                (@PlanVersionId, @BatchNo, @OrderId, @OrderCanonicalId, @OrderNo, 'TEST_SYN', @RequestDetailId)",
            new
            {
                PlanVersionId = planVersionId,
                BatchNo = TEST_BATCH_NO,
                OrderId = orderId,
                OrderCanonicalId = orderId,
                OrderNo = TEST_ORDER_NO,
                RequestDetailId = orderId
            },
            db: DatabaseId.APS);

        // ── 工艺路线：单工序 OP10（无 RoutingDependency → OP10 即无入边源结点 → StartStageCode 自动回填）。
        //    SetupTime 列仍物理存在（兼容废弃，非 NULL），填 0；Solver 已停填停读，不影响求解。──
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO RoutingOperation
                (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, OperationName, ProcessType, StageCode, StandardDuration, SetupTime, IsActive, CreatedAt, UpdatedAt)
              VALUES
                (@MaterialId, @DeptId, @RouteCode, @PathId, @OperationCode, '合成工序', 'ASSEMBLY', @StageCode, 60, 0, 1, GETDATE(), GETDATE())",
            new
            {
                MaterialId = TEST_MATERIAL_ID,
                DeptId = TEST_DEPT_ID,
                RouteCode = ROUTE_CODE,
                PathId = PATH_ID,
                OperationCode = OPERATION_CODE,
                StageCode = STAGE_CODE
            },
            db: DatabaseId.APS);

        // ── 资源（合成，AVAILABLE；Resource.Id 为 IDENTITY，OUTPUT 捕获运行时 Id 供资质引用；
        //    日历由 LoadResourcesAndCalendarAsync 按 [PlanHorizonStart, +730d] 自动生成 7x24）──
        var resourceId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO Resource
                (ResourceCode, ResourceName, ResourceType, FactoryId, ProductionDepartmentId, CapacityFactor, IsActive, Status, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES
                (@ResourceCode, '合成设备', 'MACHINE', @FactoryId, @DeptId, 1.0, 1, 'AVAILABLE', GETDATE(), GETDATE())",
            new { ResourceCode = TEST_RESOURCE_CODE, FactoryId = TEST_FACTORY_ID, DeptId = TEST_DEPT_ID },
            db: DatabaseId.APS);

        // ── 资源日历（S4 改真实装载后，不再合成 7×24；合成夹具需自给 ResourceCalendarSlot 覆盖排程期）──
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO ResourceCalendarSlot (ResourceId, StartTime, EndTime, AvailableFlag, CreatedAt, UpdatedAt)
              VALUES (@ResourceId, @StartTime, @EndTime, 1, GETDATE(), GETDATE())",
            new
            {
                ResourceId = resourceId,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddDays(30)
            },
            db: DatabaseId.APS);

        // ── 资源资质（工序 → 合成资源，CapacityFactor=1.0）：列集精确对齐 sp_SyncRoutingData_v2.0 INSERT ──
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO OperationResourceEligibility
                (MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, ResourceId, Priority, CapacityFactor, IsActive, EffectiveFrom, CreatedAt, UpdatedAt)
              VALUES
                (@MaterialId, @DeptId, @RouteCode, @PathId, @OperationCode, @ResourceId, 1, 1.0, 1, CAST(GETDATE() AS DATE), GETDATE(), GETDATE())",
            new
            {
                MaterialId = TEST_MATERIAL_ID,
                DeptId = TEST_DEPT_ID,
                RouteCode = ROUTE_CODE,
                PathId = PATH_ID,
                OperationCode = OPERATION_CODE,
                ResourceId = resourceId
            },
            db: DatabaseId.APS);

        // ── 部门上下文：(MaterialId, StageCode) → 默认部门，与 Routing/资质 同部门闭环；
        //    SourceType 受 CHECK(AUTO/MANUAL/MIXED) 约束，测试人工落库取 MANUAL。──
        await _connectionManager.ExecuteAsync(
            @"INSERT INTO MaterialStageDeptContext
                (MaterialId, StageCode, DefaultProductionDepartmentId, SourceType, ValidFrom, IsCurrent, CreatedAt, UpdatedAt)
              VALUES
                (@MaterialId, @StageCode, @DeptId, 'MANUAL', GETDATE(), 1, GETDATE(), GETDATE())",
            new { MaterialId = TEST_MATERIAL_ID, StageCode = STAGE_CODE, DeptId = TEST_DEPT_ID },
            db: DatabaseId.APS);

        return planVersionId;
    }
}