# APS 端到端代码示例：从需求到排程结果

**版本**：v1.0  
**日期**：2026-03-31  
**适用人员**：1-5号位所有开发人员  
**目的**：展示一个完整功能如何跨号位协作实现

---

## 📋 示例场景：单产品族排程

**业务场景**：PMC 发起产品族A的排程，系统自动完成 BOM展开 → Pegging连线 → 合批 → 时间槽排程 → 结果落盘 → 前端展示。

**涉及号位**：全部（1-5号位各自负责一部分）

---

## 🔄 完整数据流

```
PMC点击"开始排程"
    ↓
[4号位] 前端发起 POST /api/v1/schedules/run
    ↓
[3号位] API接收请求，入队 Hangfire 异步任务
    ↓
[3号位] 调度器按 DAG 拓扑顺序启动域排程
    ↓
[2号位] 框架加载数据快照（从数据库预加载到内存）
    ↓
[2号位] 调用5号位的 Pegging 插件 → 获得连线凭证
    ↓
[2号位] 执行连线凭证（物理状态变更）
    ↓
[2号位] 调用5号位的合批插件 → 获得合批凭证
    ↓
[2号位] 执行合批凭证，生成 Task 列表
    ↓
[2号位] 将 Task 列表传给1号位引擎
    ↓
[1号位] 纯内存排程推演（倒排→正排→换型优化）
    ↓
[1号位] 返回排好的 Task 时间安排
    ↓
[2号位] SqlBulkCopy 落盘 + 版本切换
    ↓
[3号位] 通知前端排程完成
    ↓
[4号位] 前端加载甘特图展示
```

---

## 📝 各号位代码示例

### **4号位：前端发起排程**

```vue
<!-- SchedulePanel.vue -->
<script setup lang="ts">
import { ref } from 'vue'

const scheduling = ref(false)
const taskId = ref<string | null>(null)
const status = ref<string>('idle')

interface ScheduleRequest {
  factoryId: number
  productFamilyId: number
}

/**
 * 发起排程
 * 【红线】必须带 factoryId 和 productFamilyId，禁止全量排程
 */
async function startSchedule(request: ScheduleRequest) {
  scheduling.value = true
  status.value = 'submitting'

  try {
    const response = await fetch('/api/v1/schedules/run', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${getToken()}`
      },
      body: JSON.stringify(request)
    })

    const result = await response.json()
    taskId.value = result.data.taskId
    status.value = 'queued'

    // 开始轮询状态
    pollStatus()
  } catch (err) {
    status.value = 'error'
  }
}

/**
 * 轮询排程状态
 */
async function pollStatus() {
  if (!taskId.value) return

  const interval = setInterval(async () => {
    const res = await fetch(`/api/v1/schedules/status/${taskId.value}`)
    const result = await res.json()

    status.value = result.data.status

    if (result.data.status === 'completed' || result.data.status === 'failed') {
      clearInterval(interval)
      scheduling.value = false

      if (result.data.status === 'completed') {
        // 刷新甘特图
        emit('schedule-completed', result.data.planVersionId)
      }
    }
  }, 5000) // 每5秒轮询
}

function getToken(): string {
  return localStorage.getItem('aps_token') || ''
}

const emit = defineEmits<{
  (e: 'schedule-completed', planVersionId: number): void
}>()
</script>
```

---

### **3号位：API + 调度编排**

```csharp
// ScheduleController.cs — 接收排程请求
[ApiController]
[Route("api/v1/schedules")]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleOrchestrator _orchestrator;
    private readonly ILogger<ScheduleController> _logger;

    public ScheduleController(
        IScheduleOrchestrator orchestrator,
        ILogger<ScheduleController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>发起排程（异步）</summary>
    [HttpPost("run")]
    // [ApsAuthorize("aps.plan.run")]
    public async Task<IActionResult> Run([FromBody] ScheduleRequest request)
    {
        // 参数校验
        if (request.FactoryId <= 0)
            return BadRequest(ApiResponse<object>.Fail("FactoryId 不能为空"));

        // 入队异步任务（不阻塞API线程）
        var taskId = await _orchestrator.EnqueueAsync(request);

        _logger.LogInformation(
            "排程任务已入队: TaskId={TaskId}, Factory={FactoryId}, PF={ProductFamilyId}",
            taskId, request.FactoryId, request.ProductFamilyId);

        // 立即返回
        return Accepted(ApiResponse<object>.Success(new
        {
            TaskId = taskId,
            Status = "Queued"
        }));
    }

    /// <summary>查询排程状态</summary>
    [HttpGet("status/{taskId}")]
    public async Task<IActionResult> GetStatus(string taskId)
    {
        var status = await _orchestrator.GetStatusAsync(taskId);
        return Ok(ApiResponse<object>.Success(status));
    }
}

// ScheduleOrchestrator.cs — 调度编排
public class ScheduleOrchestrator : IScheduleOrchestrator
{
    private readonly IBackgroundJobClient _hangfire;

    public async Task<string> EnqueueAsync(ScheduleRequest request)
    {
        var taskId = Guid.NewGuid().ToString("N");

        // 使用 Hangfire 入队（异步执行，不阻塞）
        _hangfire.Enqueue<IDomainScheduleJob>(
            job => job.Execute(taskId, request.FactoryId, request.ProductFamilyId));

        return taskId;
    }
}

// DomainScheduleJob.cs — 域排程作业（Hangfire执行）
public class DomainScheduleJob : IDomainScheduleJob
{
    private readonly IBOMTraversalEngine _bomEngine;        // 2号位框架
    private readonly ISchedulingEngine _scheduleEngine;      // 1号位引擎
    private readonly IBulkCopyService _bulkCopyService;      // 2号位落盘
    private readonly ILogger<DomainScheduleJob> _logger;

    public async Task Execute(string taskId, int factoryId, int productFamilyId)
    {
        _logger.LogInformation("开始域排程: {TaskId}", taskId);

        try
        {
            // 阶段1：2号位 — 加载数据快照
            var snapshot = await _bomEngine.LoadSnapshot(factoryId, productFamilyId);

            // 阶段2：2号位 — BOM遍历（内部调用5号位插件）
            var traversalResult = _bomEngine.Execute(snapshot);

            // 阶段3：1号位 — 纯内存排程推演
            var scheduleResult = _scheduleEngine.Schedule(traversalResult.Tasks);

            // 阶段4：2号位 — SqlBulkCopy 落盘
            await _bulkCopyService.BulkInsertResults(scheduleResult);

            _logger.LogInformation("域排程完成: {TaskId}", taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "域排程失败: {TaskId}", taskId);
            throw;
        }
    }
}
```

---

### **2号位：框架 — 数据快照加载 + 插件调用 + 落盘**

```csharp
// BOMTraversalEngine.cs — BOM遍历引擎
public class BOMTraversalEngine : IBOMTraversalEngine
{
    private readonly IPeggingRule _peggingRule;          // 5号位插件
    private readonly ILotSizingRule _lotSizingRule;      // 5号位插件
    private readonly IDbContextFactory _dbFactory;

    public BOMTraversalEngine(
        IPeggingRule peggingRule,
        ILotSizingRule lotSizingRule,
        IDbContextFactory dbFactory)
    {
        _peggingRule = peggingRule;
        _lotSizingRule = lotSizingRule;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// 阶段1：全量条件预加载（一次性载入内存，严禁后续按需查库）
    /// </summary>
    public async Task<ScheduleSnapshot> LoadSnapshot(int factoryId, int productFamilyId)
    {
        using var db = _dbFactory.CreateReadOnlyContext();

        // 一次性加载所有数据到内存
        var orders = await db.Orders
            .Where(o => o.FactoryId == factoryId
                     && o.ProductFamilyId == productFamilyId
                     && o.Status == "Open")
            .ToListAsync();

        var inventory = await db.InventoryBalance
            .Where(i => i.ProductFamilyId == productFamilyId)
            .ToListAsync();

        var routings = await db.Routings
            .Where(r => r.IsActive)
            .ToListAsync();

        var resources = await db.Resources
            .Where(r => r.IsActive && r.FactoryId == factoryId)
            .ToListAsync();

        // 后续排程过程中不再查库
        return new ScheduleSnapshot
        {
            Orders = orders,
            Inventory = inventory,
            Routings = routings,
            Resources = resources
        };
    }

    /// <summary>
    /// 阶段2：BOM遍历 + 调用5号位插件
    /// </summary>
    public TraversalResult Execute(ScheduleSnapshot snapshot)
    {
        // 构建5号位需要的只读上下文
        var peggingContext = new PeggingContext
        {
            Demands = snapshot.Orders.Select(o => new DemandSnapshot
            {
                DemandId = o.OrderId.ToString(),
                MaterialCode = o.MaterialCode,
                Quantity = o.Quantity,
                DueDate = o.DueDate,
                Priority = o.Priority
            }).ToList(),
            Supplies = snapshot.Inventory.Select(i => new SupplySnapshot
            {
                SupplyId = i.Id.ToString(),
                MaterialCode = i.MaterialCode,
                AvailableQuantity = i.AvailableQuantity,
                AvailableDate = i.AvailableDate
            }).ToList()
        };

        // ✅ 调用5号位插件（纯函数，返回凭证）
        var peggingVouchers = _peggingRule.Execute(peggingContext);

        // ✅ 2号位执行凭证（物理状态变更由框架统一执行）
        foreach (var v in peggingVouchers)
        {
            // 在内存中建立连线关系
            // ...
        }

        // 调用合批插件
        var lotContext = new LotSizingContext
        {
            Demands = peggingContext.Demands,
            MinBatchSize = 100m,
            TimeWindowDays = 3
        };
        var lotVouchers = _lotSizingRule.Execute(lotContext);

        // 生成 Task 列表（交给1号位排程）
        var tasks = GenerateTasks(snapshot, peggingVouchers, lotVouchers);

        return new TraversalResult { Tasks = tasks };
    }

    private List<ScheduleTask> GenerateTasks(/* ... */) => throw new NotImplementedException();
}

// BulkCopyService.cs — 落盘
public class BulkCopyService : IBulkCopyService
{
    /// <summary>
    /// SqlBulkCopy 落盘
    /// 【红线】禁止 TableLock，使用中转堆表
    /// 【红线】落盘前 .Distinct() 去重
    /// </summary>
    public async Task BulkInsertResults(ScheduleResult result)
    {
        // 去重
        var distinctTasks = result.Tasks
            .DistinctBy(t => new { t.TaskId, t.PlanVersionId })
            .ToList();

        // 写入中转堆表
        using var bulkCopy = new SqlBulkCopy(connectionString, SqlBulkCopyOptions.Default)
        {
            DestinationTableName = "Staging_TaskResult",
            BatchSize = 10000,
            BulkCopyTimeout = 300  // 5分钟
            // 注意：不使用 SqlBulkCopyOptions.TableLock（红线！）
        };

        await bulkCopy.WriteToServerAsync(distinctTasks.ToDataTable());

        // 从中转表合并到正式表
        await ExecuteStoredProcedure("sp_MergeStagingToTaskResult",
            result.PlanVersionId, result.BatchNo);
    }
}
```

---

### **5号位：业务规则插件**

```csharp
// FifoPeggingRule.cs — 供需匹配（见 5-business-rule-plugin-template.cs）
// 这里展示的是它在整个流程中被调用的位置和方式

// 关键点：
// 1. 由2号位在 BOMTraversalEngine.Execute() 中调用
// 2. 接收只读的 PeggingContext
// 3. 返回只读的 PeggingVoucher 列表
// 4. 不修改任何外部状态
// 5. 不查库、不做I/O

// 注册方式（DI）：
// services.AddScoped<IPeggingRule, FifoPeggingRule>();
// services.AddScoped<ILotSizingRule, DynamicLotSizingRule>();
```

---

### **1号位：排程引擎**

```csharp
// SchedulingEngine.cs — 纯内存排程
public class SchedulingEngine : ISchedulingEngine
{
    /// <summary>
    /// 核心排程方法
    /// 【红线】纯内存、无I/O、无LINQ（热点路径）、GC零分配
    /// </summary>
    public ScheduleResult Schedule(List<ScheduleTask> tasks)
    {
        // 步骤1：按优先级排序
        tasks.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        // 步骤2：为每个任务寻找时间槽
        for (int i = 0; i < tasks.Count; i++)
        {
            ref var task = ref tasks[i]; // 使用 ref 避免 struct 拷贝

            // 倒排：从 DueDate 向前寻找
            var slot = FindTimeSlot(
                task.ResourceGroupId,
                task.Duration,
                task.PlannedWindow.End,
                backward: true);

            if (slot.HasValue)
            {
                task.PlannedWindow = slot.Value;
                task.Status = TaskStatus.Scheduled;
            }
            else
            {
                // 翻转为正排
                slot = FindTimeSlot(
                    task.ResourceGroupId,
                    task.Duration,
                    DateTime.Now,
                    backward: false);

                if (slot.HasValue)
                {
                    task.PlannedWindow = slot.Value;
                    task.Status = TaskStatus.Scheduled;
                }
            }
        }

        // 步骤3：换型优化（同 SetupAttribute 的任务连续排）
        OptimizeSetups(tasks);

        return new ScheduleResult { Tasks = tasks };
    }

    /// <summary>
    /// 时间槽寻址（使用 IntervalTree）
    /// 【红线】不使用 LINQ，不分配堆内存
    /// </summary>
    private TimeWindow? FindTimeSlot(
        int resourceGroupId, TimeSpan duration, DateTime anchor, bool backward)
    {
        // 使用 IntervalTree 快速检索可用时间段
        // ...（具体实现省略，核心是O(log n)的区间查询）
        throw new NotImplementedException();
    }

    private void OptimizeSetups(List<ScheduleTask> tasks)
    {
        // 同 SetupAttributeHash 的任务尽量连续排产
        // ...
    }
}
```

---

## 🧪 端到端测试

```csharp
[Fact]
[Trait("Category", "E2E")]
public async Task FullSchedulingFlow_ShouldProduceValidResults()
{
    // Arrange — 准备测试数据
    var factory = TestDataFactory.CreateFactory(id: 1);
    var productFamily = TestDataFactory.CreateProductFamily(id: 1);
    var orders = TestDataFactory.CreateOrders(count: 100, factoryId: 1, pfId: 1);
    var inventory = TestDataFactory.CreateInventory(factoryId: 1);

    await SeedTestData(factory, productFamily, orders, inventory);

    // Act — 发起排程
    var response = await _client.PostAsJsonAsync("/api/v1/schedules/run", new
    {
        FactoryId = 1,
        ProductFamilyId = 1
    });
    var result = await response.Content.ReadFromJsonAsync<ApiResponse<ScheduleTaskResult>>();
    var taskId = result.Data.TaskId;

    // 等待排程完成（最多5分钟）
    var completed = await WaitForCompletion(taskId, timeout: TimeSpan.FromMinutes(5));

    // Assert
    completed.Should().BeTrue("排程应在5分钟内完成");

    // 验证结果
    var tasks = await GetScheduledTasks(factoryId: 1, pfId: 1);
    tasks.Should().NotBeEmpty("应有排程结果");

    // 验证时间约束
    foreach (var task in tasks)
    {
        task.PlannedEnd.Should().BeAfter(task.PlannedStart, "结束时间必须晚于开始时间");
    }

    // 验证无时间冲突（同资源不重叠）
    var resourceGroups = tasks.GroupBy(t => t.ResourceId);
    foreach (var group in resourceGroups)
    {
        var sorted = group.OrderBy(t => t.PlannedStart).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            sorted[i].PlannedStart.Should().BeOnOrAfter(sorted[i - 1].PlannedEnd,
                "同资源上的任务不应时间重叠");
        }
    }
}
```

---

## 📊 关键协作点总结

| 阶段 | 负责号位 | 输入 | 输出 | 关键红线 |
|------|---------|------|------|---------|
| 请求发起 | 4号位 | 用户点击 | POST请求 | 必须带工厂+产品族参数 |
| 请求接收 | 3号位 | HTTP请求 | Hangfire任务 | 异步执行，不阻塞 |
| 数据加载 | 2号位 | 数据库 | 内存快照 | 一次性加载，后续不查库 |
| Pegging | 5号位 | 只读上下文 | 凭证列表 | 纯函数，不改状态 |
| 凭证执行 | 2号位 | 凭证列表 | 内存状态更新 | 只有框架能改状态 |
| 排程推演 | 1号位 | Task列表 | 时间安排 | 纯内存，无I/O |
| 结果落盘 | 2号位 | 排程结果 | 数据库 | BulkCopy去重，禁TableLock |
| 结果展示 | 4号位 | API数据 | 甘特图 | 分页加载，不全量 |

---

**维护责任人**：2号位  
**最后更新**：2026-03-31
