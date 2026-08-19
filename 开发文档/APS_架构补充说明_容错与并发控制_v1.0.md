# APS 架构补充说明：容错与并发控制

**版本**：v1.0  
**日期**：2026-02-26  
**目的**：补充Hangfire重试策略和EF Core并发控制的详细实现

---

## 一、Hangfire 重试策略

### 1.1 问题背景

在分域计算场景下，多个Worker并发执行排程任务，可能遇到：
- 临时网络故障
- 数据库死锁
- 内存不足导致的临时失败
- 外部系统（ERP/MES）暂时不可用

**需要**：自动重试机制，避免人工干预。

### 1.2 Hangfire自动重试配置

#### 1.2.1 全局配置

```csharp
// Startup.cs 或 Program.cs
services.AddHangfire(config =>
{
    config.UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    });
    
    // 全局重试过滤器
    config.UseFilter(new AutomaticRetryAttribute
    {
        Attempts = 3,  // 默认重试3次
        DelaysInSeconds = new[] { 60, 300, 900 }  // 1分钟、5分钟、15分钟
    });
});
```

#### 1.2.2 排程任务重试策略

```csharp
// 域排程任务（关键任务，重试次数多）
[AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 300, 900, 1800, 3600 })]
[DisableConcurrentExecution(timeoutInSeconds: 3600)]  // 防止同一域并发排程
[Queue("scheduling")]  // 使用专用队列
public async Task ScheduleDomainAsync(string domainKey, int planVersionId)
{
    try
    {
        _logger.LogInformation($"开始排程域: {domainKey}, 版本: {planVersionId}");
        
        // 排程逻辑
        await _schedulingEngine.ScheduleAsync(domainKey, planVersionId);
        
        _logger.LogInformation($"域排程完成: {domainKey}");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"域排程失败: {domainKey}");
        
        // 记录失败原因到数据库
        await _dbContext.PlanVersion
            .Where(pv => pv.Id == planVersionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "FAILED")
                .SetProperty(p => p.ErrorMessage, ex.Message));
        
        throw;  // 重新抛出异常，触发Hangfire重试
    }
}

// MES实绩数据处理（高频任务，快速重试）
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 30, 60 })]
[Queue("mes-processing")]
public async Task ProcessMESActualsAsync()
{
    var pendingActuals = await _dbContext.MES_Actual_Staging
        .Where(a => a.Status == "NEW")
        .OrderBy(a => a.EventTime)
        .Take(1000)
        .ToListAsync();
    
    foreach (var actual in pendingActuals)
    {
        try
        {
            await ProcessSingleActualAsync(actual);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"处理MES实绩失败: {actual.MessageId}");
            // 单条失败不影响整批，继续处理下一条
        }
    }
}

// 集成接口调用（外部依赖，重试间隔长）
[AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 120, 300, 600, 1200, 2400 })]
[Queue("integration")]
public async Task SyncERPOrdersAsync()
{
    try
    {
        await _erpAdapter.SyncOrdersAsync();
    }
    catch (HttpRequestException ex)
    {
        _logger.LogWarning(ex, "ERP系统暂时不可用，将自动重试");
        throw;
    }
    catch (SqlException ex) when (ex.Number == 1205)  // 死锁
    {
        _logger.LogWarning(ex, "数据库死锁，将自动重试");
        throw;
    }
}
```

### 1.3 自定义重试过滤器

```csharp
public class SmartRetryAttribute : JobFilterAttribute, IElectStateFilter
{
    public int MaxAttempts { get; set; } = 3;
    public int[] DelaysInSeconds { get; set; } = new[] { 60, 300, 900 };
    
    public void OnStateElection(ElectStateContext context)
    {
        var failedState = context.CandidateState as FailedState;
        if (failedState == null) return;
        
        var retryAttempt = context.GetJobParameter<int>("RetryCount") + 1;
        
        // 判断是否应该重试
        if (retryAttempt <= MaxAttempts && ShouldRetry(failedState.Exception))
        {
            var delay = retryAttempt <= DelaysInSeconds.Length 
                ? DelaysInSeconds[retryAttempt - 1] 
                : DelaysInSeconds.Last();
            
            context.SetJobParameter("RetryCount", retryAttempt);
            context.CandidateState = new ScheduledState(TimeSpan.FromSeconds(delay))
            {
                Reason = $"自动重试 ({retryAttempt}/{MaxAttempts}): {failedState.Exception.Message}"
            };
            
            _logger.LogWarning($"任务失败，{delay}秒后重试 ({retryAttempt}/{MaxAttempts})");
        }
    }
    
    private bool ShouldRetry(Exception ex)
    {
        // 可重试的异常类型
        return ex is HttpRequestException ||
               ex is TimeoutException ||
               ex is SqlException sqlEx && (sqlEx.Number == 1205 || sqlEx.Number == -2) ||  // 死锁或超时
               ex is DbUpdateConcurrencyException;
    }
}

// 使用自定义重试过滤器
[SmartRetry(MaxAttempts = 5)]
public async Task CriticalTaskAsync()
{
    // 关键任务逻辑
}
```

### 1.4 重试监控与告警

```csharp
public class RetryMonitoringFilter : JobFilterAttribute, IServerFilter
{
    private readonly ILogger<RetryMonitoringFilter> _logger;
    private readonly IAlertService _alertService;
    
    public void OnPerforming(PerformingContext context)
    {
        var retryCount = context.GetJobParameter<int>("RetryCount");
        
        // 如果重试次数>=3，发送告警
        if (retryCount >= 3)
        {
            _alertService.SendAlertAsync(new Alert
            {
                Level = "WARNING",
                Title = $"Hangfire任务多次重试: {context.BackgroundJob.Job.Method.Name}",
                Message = $"任务已重试{retryCount}次，请关注",
                JobId = context.BackgroundJob.Id
            });
        }
    }
    
    public void OnPerformed(PerformedContext context)
    {
        // 任务执行完成后的逻辑
    }
}
```

---

## 二、EF Core 并发控制

### 2.1 问题背景

在分域计算场景下，多个Worker可能同时修改同一个`PlanVersion`记录：
- Worker A完成域1排程，更新版本状态
- Worker B完成域2排程，也更新版本状态
- **冲突**：后者可能覆盖前者的更新

**需要**：乐观并发控制，确保数据一致性。

### 2.2 乐观并发控制（RowVersion）

#### 2.2.1 实体类配置

```csharp
public class PlanVersion
{
    public int Id { get; set; }
    public string VersionCode { get; set; }
    public string Status { get; set; }
    public int CompletedDomains { get; set; }  // 已完成的域数量
    public int TotalDomains { get; set; }      // 总域数量
    
    // 乐观并发控制：RowVersion
    [Timestamp]  // SQL Server自动管理
    public byte[] RowVersion { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

// Fluent API配置（可选）
public class PlanVersionConfiguration : IEntityTypeConfiguration<PlanVersion>
{
    public void Configure(EntityTypeBuilder<PlanVersion> builder)
    {
        builder.Property(p => p.RowVersion)
            .IsRowVersion()  // SQL Server: rowversion类型
            .IsConcurrencyToken();
    }
}
```

#### 2.2.2 并发更新处理

```csharp
public class DomainSchedulingOrchestrator
{
    private readonly ApsDbContext _dbContext;
    private readonly ILogger<DomainSchedulingOrchestrator> _logger;
    
    // 域排程完成后，更新版本状态
    public async Task OnDomainCompletedAsync(int planVersionId, string domainKey)
    {
        const int maxRetries = 3;
        int retryCount = 0;
        
        while (retryCount < maxRetries)
        {
            try
            {
                // 1. 加载最新版本数据（包含RowVersion）
                var planVersion = await _dbContext.PlanVersion
                    .FirstOrDefaultAsync(pv => pv.Id == planVersionId);
                
                if (planVersion == null)
                {
                    _logger.LogError($"计划版本不存在: {planVersionId}");
                    return;
                }
                
                // 2. 更新已完成域数量
                planVersion.CompletedDomains += 1;
                
                // 3. 如果所有域都完成，更新版本状态
                if (planVersion.CompletedDomains >= planVersion.TotalDomains)
                {
                    planVersion.Status = "COMPLETED";
                    planVersion.CompletedAt = DateTime.Now;
                }
                
                // 4. 保存更改（EF Core会自动检查RowVersion）
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation($"域排程完成: {domainKey}, 进度: {planVersion.CompletedDomains}/{planVersion.TotalDomains}");
                return;  // 成功，退出循环
            }
            catch (DbUpdateConcurrencyException ex)
            {
                retryCount++;
                _logger.LogWarning(ex, $"并发冲突，重试 ({retryCount}/{maxRetries})");
                
                if (retryCount >= maxRetries)
                {
                    _logger.LogError($"并发冲突重试失败: {planVersionId}");
                    throw;
                }
                
                // 等待一小段时间后重试
                await Task.Delay(TimeSpan.FromMilliseconds(100 * retryCount));
                
                // 刷新DbContext，丢弃当前更改
                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync();
                }
            }
        }
    }
}
```

### 2.3 悲观并发控制（数据库锁）

对于极少数需要强一致性的场景，可以使用数据库行锁：

```csharp
public async Task UpdatePlanVersionWithLockAsync(int planVersionId)
{
    using var transaction = await _dbContext.Database.BeginTransactionAsync(
        IsolationLevel.ReadCommitted);
    
    try
    {
        // 使用UPDLOCK锁定行（SQL Server）
        var planVersion = await _dbContext.PlanVersion
            .FromSqlRaw(@"
                SELECT * FROM PlanVersion WITH (UPDLOCK, ROWLOCK)
                WHERE Id = {0}", planVersionId)
            .FirstOrDefaultAsync();
        
        if (planVersion == null)
        {
            throw new InvalidOperationException($"计划版本不存在: {planVersionId}");
        }
        
        // 更新逻辑
        planVersion.CompletedDomains += 1;
        
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

**注意**：悲观锁会降低并发性能，仅在必要时使用。

### 2.4 分布式锁（Redis）

对于跨进程的并发控制，可以使用Redis分布式锁：

```csharp
public class DistributedLockService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DistributedLockService> _logger;
    
    public async Task<bool> TryAcquireLockAsync(
        string lockKey, 
        TimeSpan expiry, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var lockValue = Guid.NewGuid().ToString();
        
        var acquired = await db.StringSetAsync(
            lockKey, 
            lockValue, 
            expiry, 
            When.NotExists);
        
        if (acquired)
        {
            _logger.LogInformation($"获取分布式锁成功: {lockKey}");
            return true;
        }
        
        _logger.LogWarning($"获取分布式锁失败: {lockKey}");
        return false;
    }
    
    public async Task ReleaseLockAsync(string lockKey)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(lockKey);
        _logger.LogInformation($"释放分布式锁: {lockKey}");
    }
}

// 使用示例
public async Task ScheduleDomainWithLockAsync(string domainKey, int planVersionId)
{
    var lockKey = $"scheduling:domain:{domainKey}";
    
    if (!await _lockService.TryAcquireLockAsync(lockKey, TimeSpan.FromMinutes(30)))
    {
        _logger.LogWarning($"域{domainKey}正在被其他Worker排程，跳过");
        return;
    }
    
    try
    {
        // 排程逻辑
        await _schedulingEngine.ScheduleAsync(domainKey, planVersionId);
    }
    finally
    {
        await _lockService.ReleaseLockAsync(lockKey);
    }
}
```

---

## 三、GC.Collect() 使用说明

### 3.1 问题背景

另一个AI审核时指出："避免GC.Collect()"过于绝对。

### 3.2 专业判断

在排程完成后主动GC是**合理的**，理由：
1. 450万Task对象已经不再需要
2. 及时释放15GB内存，避免影响下一次排程
3. 这不是热路径，不影响性能

### 3.3 正确用法

```csharp
public async Task ScheduleAllDomainsAsync(int planVersionId)
{
    try
    {
        // 1. 加载主数据到内存
        var masterData = await LoadMasterDataAsync();
        
        // 2. 并行排程7个域
        var tasks = _domains.Select(domain => 
            ScheduleDomainAsync(domain, planVersionId, masterData));
        
        await Task.WhenAll(tasks);
        
        _logger.LogInformation("所有域排程完成");
    }
    finally
    {
        // 排程完成，主动释放大对象堆内存（仅此处允许）
        // 原因：450万Task对象已不再需要，及时释放15GB内存
        GC.Collect(2, GCCollectionMode.Aggressive);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive);
        
        _logger.LogInformation("内存已释放");
    }
}
```

**禁止在以下场景使用GC.Collect()**：
- 热路径（如：每个Task排程完成后）
- 高频调用的方法
- 用户交互响应路径

---

## 四、监控与告警

### 4.1 Hangfire Dashboard监控

```csharp
// Startup.cs
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    StatsPollingInterval = 2000  // 2秒刷新一次
});

// 自定义授权过滤器
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.IsInRole("Admin");
    }
}
```

### 4.2 性能监控

```csharp
public class PerformanceMonitoringFilter : JobFilterAttribute, IServerFilter
{
    private readonly ILogger<PerformanceMonitoringFilter> _logger;
    
    public void OnPerforming(PerformingContext context)
    {
        context.SetJobParameter("StartTime", DateTime.Now);
    }
    
    public void OnPerformed(PerformedContext context)
    {
        var startTime = context.GetJobParameter<DateTime>("StartTime");
        var duration = DateTime.Now - startTime;
        
        _logger.LogInformation($"任务执行耗时: {context.BackgroundJob.Job.Method.Name} - {duration.TotalSeconds}秒");
        
        // 如果耗时超过阈值，发送告警
        if (duration.TotalMinutes > 30)
        {
            _alertService.SendAlertAsync(new Alert
            {
                Level = "WARNING",
                Title = "任务执行超时",
                Message = $"{context.BackgroundJob.Job.Method.Name} 耗时 {duration.TotalMinutes:F1} 分钟"
            });
        }
    }
}
```

---

## 五、最佳实践总结

### 5.1 Hangfire重试

✅ **推荐做法**：
- 为不同类型的任务配置不同的重试策略
- 使用队列隔离不同优先级的任务
- 监控重试次数，及时告警
- 记录失败原因到数据库

❌ **避免做法**：
- 所有任务使用相同的重试策略
- 无限重试（设置合理的最大重试次数）
- 忽略重试告警

### 5.2 EF Core并发控制

✅ **推荐做法**：
- 默认使用乐观并发控制（RowVersion）
- 捕获`DbUpdateConcurrencyException`并重试
- 对于极少数强一致性场景，使用悲观锁
- 跨进程并发使用Redis分布式锁

❌ **避免做法**：
- 忽略并发冲突异常
- 过度使用悲观锁导致性能下降
- 不设置锁超时时间

### 5.3 GC管理

✅ **推荐做法**：
- 仅在排程完成后主动GC
- 添加注释说明原因
- 监控GC暂停时间

❌ **避免做法**：
- 在热路径中调用GC.Collect()
- 频繁调用GC.Collect()

---

**文档结束**
