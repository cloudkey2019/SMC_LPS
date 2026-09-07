using Hangfire;
using Hangfire.SqlServer;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Services.Sync;

namespace LPS.APS.Web.Extensions;

/// <summary>
/// Hangfire定时服务配置扩展
/// </summary>
public static class HangfireServiceExtensions
{
    /// <summary>
    /// 注册Hangfire服务（使用独立 APS_Hangfire 库存储Job数据）
    /// </summary>
    public static IServiceCollection AddHangfireServices(this IServiceCollection services, IConfiguration configuration)
    {
        var hangfireConnectionString = configuration["Database:Hangfire:ConnectionString"];
        
        if (string.IsNullOrWhiteSpace(hangfireConnectionString))
        {
            throw new InvalidOperationException("Hangfire需要独立数据库连接字符串（Database:Hangfire:ConnectionString）");
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(hangfireConnectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                SchemaName = "HangfireSchema"
            }));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = Environment.ProcessorCount * 2;
            options.Queues = new[] { "critical", "default", "low" };
            options.ServerName = $"APS-{Environment.MachineName}";
        });

        // 全局禁用自动重试（失败即终止，不干扰调试）
        // 生产环境如需重试，可在具体 Job 方法上标注 [AutomaticRetry(Attempts = 3)]
        GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 0 });

        return services;
    }

    // 永不触发的 cron（2月31日不存在），开发环境使用
    // 任务仍注册在 Dashboard 的 Recurring Jobs 列表中，可点 "Trigger Now" 手动执行
    private const string NeverFireCron = "0 0 31 2 *";

    /// <summary>
    /// 注册所有Hangfire定时任务（在 app.Build() 之后调用）
    /// 开发环境：注册但使用永不触发的cron，仅支持 Dashboard 手动触发
    /// 生产环境：使用真实cron自动执行
    /// </summary>
    public static WebApplication UseHangfireJobs(this WebApplication app)
    {
        var isDev = app.Environment.IsDevelopment();

        // ERP订单增量同步（生产：每小时整点）
        RecurringJob.AddOrUpdate<IERPOrderSyncService>(
            "erp-order-incremental-sync",
            service => service.IncrementalSyncAsync(CancellationToken.None),
            isDev ? NeverFireCron : "0 * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // BOM展开请求推送（生产：每日00:00）
        // 从 Order_Canonical 划定活跃根集合，推送 BOMNO 到 ODS 库
        // 后续：SQL Agent Job 00:05 执行 sp_ExpandBOMBatch 展开
        RecurringJob.AddOrUpdate<IBOMRequestService>(
            "bom-request-push",
            service => service.PushBOMRequestToODSAsync(CancellationToken.None),
            isDev ? NeverFireCron : "0 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 主数据同步-ERP（生产：每日00:10）— §2.4.3
        // sp_SyncMasterData('ERP') → Material + MaterialMapping + MaterialSupplyContext
        RecurringJob.AddOrUpdate<IMasterDataSyncService>(
            "master-data-sync-erp",
            service => service.SyncERPMasterDataAsync(CancellationToken.None),
            isDev ? NeverFireCron : "10 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 主数据同步-MES（生产：每日00:20）— §2.4.3
        // sp_SyncMasterData('MES') → Material + MaterialMapping + MaterialSupplyContext
        RecurringJob.AddOrUpdate<IMasterDataSyncService>(
            "master-data-sync-mes",
            service => service.SyncMESMasterDataAsync(CancellationToken.None),
            isDev ? NeverFireCron : "20 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 资源主数据同步（生产：每日00:15）— v5.0.16 新增
        // ⚠️ 排在 master-data-sync-erp（00:10）之后（Factory 字典已就绪）
        // ⚠️ 排在 routing-sync（00:25）之前（Resource 表为 OperationResourceEligibility 的前置依赖）
        // sp_SyncResourceData('MES') → Resource（双字典映射：FactoryCode + ProductionDeptCode）
        RecurringJob.AddOrUpdate<IResourceSyncService>(
            "resource-sync",
            service => service.SyncAsync(CancellationToken.None),
            isDev ? NeverFireCron : "15 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 工艺路线同步（生产：每日00:25）— 核心流程文档 §阶段1 00:15
        // ⚠️ 排在 master-data-sync-mes（00:20）之后，确保 MaterialMapping(MES) 已就绪供 MES_ID→MaterialId 映射
        // sp_SyncRoutingData → RoutingOperation + RoutingDependency + RoutingStage + OperationResourceEligibility
        RecurringJob.AddOrUpdate<IRoutingSyncService>(
            "routing-sync",
            service => service.SyncAllAsync(CancellationToken.None),
            isDev ? NeverFireCron : "25 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 库存同步（生产：每日00:35）— 五层库存架构 L1→L4 全链路
        // ⚠️ 排在 master-data-sync-mes（00:20）之后，确保 MaterialMapping 就绪供 SourceID→MaterialCode 反查
        // ⚠️ 排在 nightly-batch（00:30）之前/之后均可，但必须早于 scheduling（02:00）
        // sp_SyncInventory → InventoryFact_ERP/MES → sp_RefreshInventoryBalance → InventoryBalance
        RecurringJob.AddOrUpdate<IInventorySyncService>(
            "inventory-sync",
            service => service.SyncAsync(CancellationToken.None),
            isDev ? NeverFireCron : "35 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 夜间批次管道（生产：每日00:30）
        // 步骤：全量订单同步 → 创建ScheduleRun（锁定DataCutoffTime）→ 创建PlanVersion → 订单装载 → BOM接货
        RecurringJob.AddOrUpdate<INightlyBatchOrchestrator>(
            "nightly-batch",
            service => service.RunAsync(CancellationToken.None),
            isDev ? NeverFireCron : "30 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // MES快照 Step1：工单级快照（生产：每日00:40）
        // ScheduleRun 已在 00:30 nightly-batch 中创建，此处直接读取
        // sp_SyncMESWorkOrderSnapshot → MESWorkOrderSnapshot
        RecurringJob.AddOrUpdate<IMESSnapshotSyncService>(
            "mes-snapshot-workorder",
            service => service.SyncWorkOrderAsync(CancellationToken.None),
            isDev ? NeverFireCron : "40 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // MES快照 Step2：工序进度快照（生产：每日00:45）
        // sp_SyncMESOperationProgressSnapshot → OperationProgressSnapshot
        RecurringJob.AddOrUpdate<IMESSnapshotSyncService>(
            "mes-snapshot-operation-progress",
            service => service.SyncOperationProgressAsync(CancellationToken.None),
            isDev ? NeverFireCron : "45 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // MES快照 Step3：大工艺进度快照（生产：每日00:50）
        // sp_SyncMESStageProgressSnapshot → StageProgressSnapshot
        // 1号位优先消费此表（粒度粗、性能好）
        RecurringJob.AddOrUpdate<IMESSnapshotSyncService>(
            "mes-snapshot-stage-progress",
            service => service.SyncStageProgressAsync(CancellationToken.None),
            isDev ? NeverFireCron : "50 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 管道供给同步（生产：每日00:55）— §6.6 SupplyFact_Pipeline
        // ⚠️ 排在 inventory-sync（00:35）之后，确保 MaterialMapping 已就绪供 MaterialCode 映射
        // ⚠️ 排在 domain-dependency-scan（01:50）和 scheduling（02:00）之前
        // sp_SyncPipelineSupply → SupplyFact_Pipeline（V1 空跑；V1.1 起全量装载真实数据）
        RecurringJob.AddOrUpdate<IPipelineSupplySyncService>(
            "pipeline-supply-sync",
            service => service.SyncAsync(CancellationToken.None),
            isDev ? NeverFireCron : "55 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 跨域依赖静态扫描（生产：每日01:50）— §阶段0.5 / 走查文档 v3.3
        // ⚠️ 排在 nightly-batch（00:30 完成 LLC）之后、scheduling（02:00）之前
        // 扫描 APS_BOM_RAW + Material + ProductFamily 的跨产品族血缘关系，
        // TRUNCATE + INSERT 全量刷新 Domain_Dependency 表，供 3 号位 Kahn 拓扑排序
        // sp_ScanDomainDependency（参见 APS_跨域依赖扫描DDL补充_v1.0.sql）
        RecurringJob.AddOrUpdate<IDomainDependencyScanService>(
            "domain-dependency-scan",
            service => service.ScanAsync(CancellationToken.None),
            isDev ? NeverFireCron : "50 1 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 物料×阶段→默认生产部门 上下文重建（生产：每日01:55）— §4.3c MaterialStageDeptContext
        // ⚠️ 排在 domain-dependency-scan（01:50）之后、scheduling-trigger（02:00）之前
        // 全量重建 MaterialStageDeptContext（SCD Type 2），供 1号位排程按 (MaterialId,StageCode) 锁定 Routing 三件套
        // sp_RebuildMaterialStageDeptContext（@TriggerMode='FULL'；Database/Scripts/APS/ 独立脚本）
        RecurringJob.AddOrUpdate<IMaterialStageDeptContextRebuildService>(
            "material-stage-dept-context-rebuild",
            service => service.RebuildAsync(CancellationToken.None),
            isDev ? NeverFireCron : "55 1 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        // 排程推演（生产：每日02:00）— §2.5.1 排程发令枪
        // 所有数据管道完成后，自动查找待排PlanVersion → 装载沙盘 → 求解 → 落盘
        RecurringJob.AddOrUpdate<ISchedulingOrchestrator>(
            "scheduling-trigger",
            service => service.RunSchedulingAutoAsync(CancellationToken.None),
            isDev ? NeverFireCron : "0 2 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });

        return app;
    }
}
