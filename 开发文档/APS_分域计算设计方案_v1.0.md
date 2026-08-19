# APS 分域计算设计方案

**版本**：v1.1  
**日期**：2026-02-26  
**基于**：《APS数据画像调研问卷 v1.0》（已补充跨域订单识别逻辑和分区管理策略）

---

## 一、分域计算必要性分析

### 1.1 规模挑战

根据问卷数据：
- **月均订单量**：15万单/月
- **计划窗口**：90天
- **在线订单量**：约45万单
- **平均工序数**：10道
- **总Task数**：约450万Task

**内存压力估算**：
```
单个Task对象大小：约200字节（含引用）
450万Task × 200字节 = 900MB

加上Pegging图（假设每Task 3个Pegging节点）：
450万 × 3 × 150字节 = 2025MB

加上主数据（BOM、Routing、Resource等）：
约500MB

总内存需求：约3.5GB（纯数据）
实际GC堆内存：约10-15GB（考虑GC开销）
```

**性能风险**：
- 单次全量排程可能需要数小时
- GC压力极大，可能导致STW（Stop-The-World）超过10秒
- 无法满足"每日1次滚动排程"的要求

### 1.2 分域计算收益

| 指标 | 全量计算 | 分域计算（7域） | 收益 |
|------|---------|----------------|------|
| 单次Task数 | 450万 | 64万/域 | **减少86%** |
| 内存占用 | 15GB | 2GB/域 | **减少87%** |
| 排程耗时 | 预计4小时 | 30分钟/域（并行40分钟） | **减少94%** |
| GC压力 | 极高 | 中等 | **显著降低** |

---

## 二、分域策略设计

### 2.1 分域维度选择

#### 方案A：按产品族分域（推荐）

**分域规则**：
```
DomainKey = ProductFamilyCode
```

**优势**：
- 产品族内BOM相似度高（>80%），数据关联性强
- 7个产品族，规模适中
- 业务语义清晰，易于理解和维护

**劣势**：
- 跨产品族的共用物料需要特殊处理（通过虚拟库存硬约束解决）
- 同产品族跨工厂协同订单需要跨域处理（通过厂间订单发货Task解决）

**适用场景**：
- 产品族之间物料共用较少
- **同产品族不同工厂设备通用**（如：铸造厂→机加厂→装配厂）
- 跨工厂协同订单占比高（50%+），但希望在域内统一调度设备资源池

#### 方案B：按工厂分域

**分域规则**：
```
DomainKey = FactoryCode
```

**优势**：
- 工厂物理隔离，资源不共享
- 5个工厂，规模适中

**劣势**：
- 跨工厂协同订单占比未知（问卷显示有跨厂协同）
- 单个工厂内可能包含多个产品族，规模仍然较大

**适用场景**：
- 跨工厂协同订单占比<10%
- 各工厂独立运作

#### 方案C：混合分域（产品族 × 工厂）

**分域规则**：
```
DomainKey = ProductFamilyCode + "_" + FactoryCode
```

**优势**：
- 最细粒度，单域规模最小
- 完全隔离，无跨域依赖

**劣势**：
- 最多35个域（7产品族 × 5工厂），管理复杂
- 跨域订单处理复杂度高
- 并行计算资源需求高（需要35个Worker）

**适用场景**：
- 单域规模仍然超限（>10万Task）
- 有足够的计算资源支持并行

### 2.2 推荐方案：按产品族分域

**理由**：
1. 问卷显示产品族内BOM相似度高（>80%），适合分域
2. 7个域规模适中，单域约6.4万单/64万Task，接近初始假设
3. 可并行计算，7个Worker即可
4. 业务语义清晰，易于沟通

**实施细节**：
```sql
-- Order表增加DomainKey字段
ALTER TABLE [Order] ADD DomainKey NVARCHAR(100) NULL;

-- 计算DomainKey（基于ProductFamily）
UPDATE [Order]
SET DomainKey = pf.Code
FROM [Order] o
INNER JOIN ProductFamily pf ON o.ProductFamilyId = pf.Id;

-- 创建索引
CREATE INDEX IX_Order_Domain ON [Order](DomainKey, Status);
```

---

## 三、跨域订单处理

### 3.1 跨域订单识别

**定义**：
- 订单的上下游物料跨越多个产品族
- 订单需要跨工厂协同生产
- **半成品厂间转移**（如：铸造厂→机加厂→装配厂）
- **委外加工**（约5%订单）

**识别逻辑**：
```csharp
public bool IsCrossDomainOrder(Order order)
{
    // 规则1：检查BOM是否跨产品族
    var bomMaterials = GetBOMMaterials(order.MaterialId);
    var productFamilies = bomMaterials.Select(m => m.ProductFamilyId).Distinct();
    
    if (productFamilies.Count() > 1)
        return true;
    
    // 规则2：检查工艺路线是否跨工厂
    var routing = GetRouting(order.MaterialId);
    var factories = routing.Steps.Select(s => s.ResourceGroup.FactoryCode).Distinct();
    
    if (factories.Count() > 1)
        return true;
    
    // 规则3：检查是否有半成品厂间转移（新增）
    if (HasIntermediateTransfer(routing))
        return true;
    
    // 规则4：检查是否有委外加工（新增）
    if (HasOutsourcing(routing))
        return true;
    
    return false;
}

// 新增：检测半成品转移
private bool HasIntermediateTransfer(Routing routing)
{
    // 场景：铸造厂→机加厂→装配厂
    var factories = routing.Steps
        .Select(s => s.ResourceGroup.FactoryCode)
        .Distinct()
        .ToList();
    
    // 如果工艺路线跨越多个工厂，且有明确的转移步骤
    if (factories.Count > 1)
    {
        // 检查是否有"转移"类型的工序
        var hasTransferStep = routing.Steps
            .Any(s => s.ProcessType == "TRANSFER" || s.ProcessType == "SHIPPING");
        
        return hasTransferStep;
    }
    
    return false;
}

// 新增：检测委外加工
private bool HasOutsourcing(Routing routing)
{
    // 检查是否有委外工序（如：热处理、表面处理、特殊加工）
    return routing.Steps.Any(s => 
        s.IsOutsourced == true ||
        s.ResourceGroup.IsExternal == true ||
        s.ProcessType == "OUTSOURCING"
    );
}
```

**标记跨域订单**：
```sql
UPDATE [Order]
SET IsCrossDomain = 1
WHERE Id IN (
    SELECT DISTINCT o.Id
    FROM [Order] o
    INNER JOIN Lot l ON l.OrderId = o.Id
    INNER JOIN BOM b ON b.ParentMaterialId = l.MaterialId
    INNER JOIN Material m ON m.Id = b.ChildMaterialId
    WHERE m.ProductFamilyId <> o.ProductFamilyId
);
```

### 3.2 跨域订单处理策略

#### 策略A：同域跨厂场景 - 厂间订单发货Task（推荐）

**⚠️ 架构红线说明**：
- **此策略只用于同域跨厂场景**：订单属于同一产品族，但半成品和成品在不同物理工厂生产
- **绝对禁止用于跨产品族场景**：跨产品族依赖必须使用策略B（虚拟库存硬约束）

**原理**：
- 在同产品族的工厂边界插入发货Task（基于真实ERP厂间订单）
- 发货Task作为上游工厂的输出和下游工厂的输入
- 在单个域的排程算法内部处理

**示例**：
```
订单：SO001（产品族X整机，跨工厂生产）

域X排程（内部处理跨厂协同）：
  Task1: A厂生产半成品MAT_X001（产品族X）
  Task_Transfer: 厂间订单发货Task（A厂→B厂，同域内部，2.5天，ERP单号：SO-Inter-001）
  Task2: B厂使用半成品MAT_X001，生产成品MAT_X002（产品族X）
```

**实现**：
```csharp
public async Task HandleSameDomainCrossFactoryOrderAsync(Order order)
{
    // ⚠️ 架构契约：此方法只处理同域跨厂场景
    // 跨产品族场景使用虚拟库存硬约束（见策略B）
    
    // 1. 识别同域跨厂物料（同产品族，不同工厂）
    var crossFactoryMaterials = IdentifySameDomainCrossFactoryMaterials(order);
    
    // 2. 为每个跨厂物料创建基于真实ERP单号的发货Task
    foreach (var material in crossFactoryMaterials)
    {
        var transferTask = new Task
        {
            TaskType = "SHIPPING",
            SourceFactoryCode = material.SourceFactory,
            TargetFactoryCode = material.TargetFactory,
            ProductFamilyCode = material.ProductFamily,  // 同一产品族
            MaterialId = material.Id,
            Duration = GetFactoryTransferTime(material.SourceFactory, material.TargetFactory),
            ResourceId = GetVirtualLogisticsResource()
        };
        
        await _dbContext.Task.AddAsync(transferTask);
    }
    
    // 3. 建立Pegging关系
    await CreateCrossFactoryPeggingAsync(order, crossFactoryMaterials);
}

// 半成品厂间转移处理（新增）
public void CreateIntermediateTransferTask(Task sourceTask, Task targetTask)
{
    // 场景：铸造厂→机加厂的半成品转移
    var transferTask = new Task
    {
        TaskNo = $"TRANSFER_{sourceTask.TaskNo}_TO_{targetTask.FactoryCode}",
        TaskType = "SHIPPING",
        SourceFactoryCode = sourceTask.FactoryCode,
        TargetFactoryCode = targetTask.FactoryCode,
        MaterialCode = sourceTask.MaterialCode,
        Quantity = sourceTask.Quantity,
        
        // 时间约束
        PlannedStartTime = sourceTask.PlannedEndTime,  // 紧接着上游完工
        PlannedEndTime = sourceTask.PlannedEndTime.AddHours(GetTransferLeadTime(
            sourceTask.FactoryCode, targetTask.FactoryCode)),
        
        // 资源占用（可选）
        ResourceCode = "LOGISTICS_TRUCK",  // 物流车辆
        
        // Pegging关系
        PredecessorTaskId = sourceTask.Id,
        SuccessorTaskId = targetTask.Id
    };
    
    _dbContext.Task.Add(transferTask);
}

// 委外加工处理（新增）
public void HandleOutsourcingTask(Task task, RoutingStep outsourcingStep)
{
    // 委外工序视为"黑盒"，只关注交期
    task.ProcessingTime = outsourcingStep.OutsourcingLeadTime;  // 如：7天
    task.ResourceCode = "OUTSOURCE_VENDOR_" + outsourcingStep.VendorCode;
    task.IsOutsourced = true;
    
    // 不参与设备排程，但占用时间窗口
    task.SchedulingMode = "FIXED_DURATION";
    
    // 委外工序不消耗内部资源
    task.ResourceCapacityRequired = 0;
}

private decimal GetTransferTime(string sourceDomain, string targetDomain)
{
    // 从配置表读取跨域转移时间
    // 如：ProductFamilyA -> ProductFamilyB: 1天
    return _config.GetTransferTime(sourceDomain, targetDomain);
}

private decimal GetTransferLeadTime(string sourceFactory, string targetFactory)
{
    // 从配置表读取厂间转移时间
    // 如：铸造厂→机加厂: 4小时
    return _config.GetFactoryTransferTime(sourceFactory, targetFactory);
}
```

#### 策略B：异域跨族场景 - 虚拟库存硬约束（必须）

**⚠️ 架构红线说明**：
- **此策略只用于异域跨族场景**：订单跨越多个产品族（如：产品族A需要产品族B的半成品）
- **绝对禁止使用厂间订单发货Task**：跨产品族依赖必须通过"阶段0.5静态扫描 + 虚拟库存硬约束"处理

**原理**：
- 01:50静态扫描时，通过SQL将跨产品族依赖固化到`DomainDependency`表
- 02:00排程时，按拓扑顺序执行：上游域先排，立即落盘
- 下游域启动前，读取上游落盘结果，构建**虚拟库存**（带AvailableTime）
- 下游域排程时，虚拟库存的AvailableTime作为**硬约束**，算法自动"撞墙"推迟

**示例**：
```
订单：SO002（产品族A整机，需要产品族B的电机）

01:50静态扫描：
- SQL扫描生成：DomainDependency（产品族B → 产品族A）

02:00排程：
- 拓扑排序：产品族B先排，产品族A后排
- 产品族B排完，立即落盘（电机完工时间：3月3日14:00）
- 产品族A启动前，读取产品族B结果，构建虚拟库存：
  - MaterialId = 电机
  - AvailableTime = 3月3日14:00 + 物流2天 = 3月5日14:00
- 产品族A排程时，装配Task尝试排在3月1日→撞墙→自动推迟到3月5日14:00
```

**实现**：
```csharp
public async Task ScheduleWithVirtualInventoryConstraintAsync(string domainKey)
{
    // ⚠️ 架构契约：此方法只处理异域跨族场景
    // 同域跨厂场景使用厂间订单发货Task（见策略A）
    
    // 1. 加载本域订单
    var orders = await LoadDomainOrdersAsync(domainKey);
    
    // 2. 加载跨域依赖（上游域的输出，已落盘）
    var upstreamOutputs = await LoadUpstreamOutputsAsync(domainKey);
    
    // 3. 将上游输出作为虚拟库存硬约束
    foreach (var output in upstreamOutputs)
    {
        _memoryInventory[output.MaterialId] = new VirtualInventory
        {
            MaterialId = output.MaterialId,
            Quantity = output.Quantity,
            AvailableTime = output.PlannedEndTime.AddHours(output.LogisticsLeadTime),  // 硬约束时间
            SourceDomain = output.SourceDomain
        };
    }
    
    // 4. 执行排程（算法会自动"撞墙"推迟）
    await _schedulingEngine.ScheduleAsync(orders, _memoryInventory);
}
```

**⚠️ 架构契约**：
- 虚拟库存硬约束**只用于异域跨族**场景
- 必须严格按拓扑顺序执行（上游先排，下游后排）
- 上游的时间自动变成下游的物理时间墙
- **绝对禁止**使用厂间订单发货Task处理跨产品族依赖

### 3.3 跨域订单排程顺序

**方案A：拓扑排序（推荐）**

```csharp
public List<string> GetDomainSchedulingOrder()
{
    // 1. 构建域依赖图
    var graph = new Dictionary<string, List<string>>();
    
    foreach (var order in _crossDomainOrders)
    {
        var upstreamDomain = GetUpstreamDomain(order);
        var downstreamDomain = GetDownstreamDomain(order);
        
        if (!graph.ContainsKey(upstreamDomain))
            graph[upstreamDomain] = new List<string>();
        
        graph[upstreamDomain].Add(downstreamDomain);
    }
    
    // 2. 拓扑排序
    var sorted = TopologicalSort(graph);
    
    // 3. 独立域可并行
    return sorted;
}

// 示例结果：
// [ProductFamilyA, ProductFamilyB] -> 并行
// [ProductFamilyC] -> 依赖A和B，等待A和B完成
// [ProductFamilyD, ProductFamilyE, ProductFamilyF, ProductFamilyG] -> 并行
```

**方案B：两阶段排程**

```
阶段1：独立域并行排程
  - 识别无跨域依赖的域
  - 并行执行排程

阶段2：跨域订单专项处理
  - 收集所有域的输出
  - 对跨域订单单独排程
  - 更新相关域的计划
```

---

## 四、分域计算实现架构

### 4.1 调度流程

```
┌─────────────────────────────────────────────────────────────┐
│                   Scheduling Orchestrator                    │
│  1. 读取所有订单，按DomainKey分组                            │
│  2. 识别跨域订单，构建域依赖图                               │
│  3. 拓扑排序，确定排程顺序                                   │
│  4. 提交排程任务到Hangfire                                   │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                    Hangfire Job Queue                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │ Domain A │  │ Domain B │  │ Domain C │  │ Domain D │   │
│  │   Job    │  │   Job    │  │   Job    │  │   Job    │   │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘   │
└─────────────────────────────────────────────────────────────┘
       ↓              ↓              ↓              ↓
┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐
│ Worker 1 │  │ Worker 2 │  │ Worker 3 │  │ Worker 4 │
│ Domain A │  │ Domain B │  │ Domain C │  │ Domain D │
└──────────┘  └──────────┘  └──────────┘  └──────────┘
       ↓              ↓              ↓              ↓
┌─────────────────────────────────────────────────────────────┐
│              Write to Database (Partitioned)                 │
│  Task表（按PlanVersionId分区）                               │
│  Pegging表（按PlanVersionId分区）                            │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 核心代码实现

#### 4.2.1 调度编排器

```csharp
public class SchedulingOrchestrator
{
    private readonly IBackgroundJobClient _hangfire;
    private readonly ApsDbContext _dbContext;

    public async Task<string> StartDomainSchedulingAsync(SchedulingRequest request)
    {
        var jobId = Guid.NewGuid().ToString();
        
        // 1. 读取所有订单，按DomainKey分组
        var domainGroups = await _dbContext.Order
            .Where(o => o.Status == "NEW" || o.Status == "PLANNED")
            .GroupBy(o => o.DomainKey)
            .ToListAsync();
        
        // 2. 识别跨域订单
        var crossDomainOrders = await _dbContext.Order
            .Where(o => o.IsCrossDomain)
            .ToListAsync();
        
        // 3. 构建域依赖图并拓扑排序
        var schedulingOrder = GetDomainSchedulingOrder(crossDomainOrders);
        
        // 4. 为每个域创建PlanVersion
        var versionMap = new Dictionary<string, int>();
        foreach (var domainKey in schedulingOrder.SelectMany(batch => batch))
        {
            var version = await CreatePlanVersionAsync(domainKey, request);
            versionMap[domainKey] = version.Id;
        }
        
        // 5. 按批次提交Hangfire任务
        foreach (var batch in schedulingOrder)
        {
            var batchJobIds = new List<string>();
            
            foreach (var domainKey in batch)
            {
                var jobId = _hangfire.Enqueue<DomainSchedulingJob>(
                    job => job.ExecuteAsync(versionMap[domainKey], domainKey, null)
                );
                batchJobIds.Add(jobId);
            }
            
            // 等待本批次完成后，再提交下一批次
            if (schedulingOrder.IndexOf(batch) < schedulingOrder.Count - 1)
            {
                _hangfire.ContinueBatchWith(batchJobIds, () => 
                    Console.WriteLine($"Batch {batch} completed")
                );
            }
        }
        
        return jobId;
    }
}
```

#### 4.2.2 域排程任务

```csharp
public class DomainSchedulingJob
{
    private readonly SchedulingEngine _engine;
    private readonly ApsDbContext _dbContext;

    public async Task ExecuteAsync(int versionId, string domainKey, CancellationToken cancellationToken)
    {
        try
        {
            // 1. 更新版本状态
            await UpdateVersionStatusAsync(versionId, "RUNNING");
            
            // 2. 加载本域数据到内存
            var domainData = await LoadDomainDataAsync(domainKey);
            
            // 3. 加载跨域依赖（上游输出）
            var upstreamOutputs = await LoadUpstreamOutputsAsync(domainKey, versionId);
            
            // 4. 执行排程
            var result = await _engine.ScheduleAsync(
                domainData.Orders,
                domainData.Lots,
                domainData.Resources,
                domainData.Inventory,
                upstreamOutputs,
                cancellationToken
            );
            
            // 5. 写入数据库（批量写入）
            await WriteToDatabaseAsync(versionId, result);
            
            // 6. 更新版本状态
            await UpdateVersionStatusAsync(versionId, "COMPLETED");
            
            _logger.LogInformation($"Domain {domainKey} scheduling completed: {result.TotalTasks} tasks");
        }
        catch (Exception ex)
        {
            await UpdateVersionStatusAsync(versionId, "FAILED", ex.Message);
            throw;
        }
    }

    private async Task<DomainData> LoadDomainDataAsync(string domainKey)
    {
        // 仅加载本域数据，减少内存占用
        var orders = await _dbContext.Order
            .Where(o => o.DomainKey == domainKey)
            .ToListAsync();
        
        var lots = await _dbContext.Lot
            .Where(l => orders.Select(o => o.Id).Contains(l.OrderId))
            .ToListAsync();
        
        // ... 加载其他数据
        
        return new DomainData { Orders = orders, Lots = lots, ... };
    }
}
```

### 4.3 批量写入优化

```csharp
public async Task WriteToDatabaseAsync(int versionId, SchedulingResult result)
{
    // 使用SqlBulkCopy批量写入
    using var bulkCopy = new SqlBulkCopy(_connectionString)
    {
        DestinationTableName = "Task",
        BatchSize = 10000,
        BulkCopyTimeout = 300
    };
    
    // 映射列
    bulkCopy.ColumnMappings.Add("PlanVersionId", "PlanVersionId");
    bulkCopy.ColumnMappings.Add("TaskNo", "TaskNo");
    // ... 其他列
    
    // 转换为DataTable
    var dataTable = ConvertToDataTable(result.Tasks, versionId);
    
    // 批量写入
    await bulkCopy.WriteToServerAsync(dataTable);
    
    _logger.LogInformation($"Bulk inserted {result.Tasks.Count} tasks");
}
```

---

## 五、性能优化建议

### 5.1 并行度配置

```json
{
  "Hangfire": {
    "WorkerCount": 7,
    "Queues": ["domain_a", "domain_b", "domain_c", "domain_d", "domain_e", "domain_f", "domain_g"],
    "PollingInterval": "00:00:01"
  }
}
```

### 5.2 内存管理

```csharp
public class SchedulingEngine
{
    public async Task<SchedulingResult> ScheduleAsync(...)
    {
        try
        {
            // 执行排程
            var result = await ExecuteSchedulingAsync(...);
            
            return result;
        }
        finally
        {
            // 强制GC回收（仅在排程完成后）
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
```

### 5.3 监控指标

| 指标 | 说明 | 告警阈值 |
|------|------|---------|
| **域排程耗时** | 单个域排程耗时 | >30分钟 |
| **总排程耗时** | 所有域排程总耗时 | >60分钟 |
| **GC暂停时间** | 单次GC暂停时间 | >1秒 |
| **内存占用** | 单个Worker内存占用 | >4GB |
| **跨域订单占比** | 跨域订单占总订单比例 | >30% |

---

## 六、分域计算总结

### 6.1 关键收益

| 维度 | 收益 |
|------|------|
| **性能** | 排程耗时从预计4小时降至40分钟（并行） |
| **内存** | 单Worker内存从15GB降至2GB |
| **可扩展性** | 支持水平扩展（增加Worker） |
| **可维护性** | 域隔离，故障影响范围小 |

### 6.2 实施建议

1. **优先验证单域性能**：Spike阶段先验证单域6.4万单的排程性能
2. **逐步推进**：先实现单域排程，再实现跨域处理
3. **监控先行**：部署前建立完善的监控体系
4. **灰度发布**：先在1-2个产品族试点，再全面推广

### 6.3 风险与应对

| 风险 | 应对措施 |
|------|---------|
| 跨域订单占比过高（>30%） | 调整分域策略，考虑混合分域 |
| 单域规模仍超限 | 启用CRITICAL_PATH降级模式 |
| 拓扑排序出现环路 | 人工介入，拆分跨域订单 |
| 并行计算资源不足 | 增加Worker数量或分批次排程 |

---

**文档结束**
