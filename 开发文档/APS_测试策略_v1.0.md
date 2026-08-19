# APS 测试策略文档

**版本**：v1.0  
**日期**：2026-03-31  
**适用人员**：1-5号位所有开发人员  
**测试框架**：xUnit + Moq + FluentAssertions  
**前端测试**：Vitest + Vue Test Utils

---

## 📋 测试分层策略

### **三层测试金字塔**

```
            /  E2E 测试  \          ← 少量，覆盖关键流程
           / （集成测试）  \         ← 适量，覆盖模块协作
          / （单元测试）     \       ← 大量，覆盖核心逻辑
         /___________________\
```

| 测试层级 | 占比 | 执行频率 | 负责人 |
|---------|------|---------|--------|
| 单元测试 | 70% | 每次提交前 | 各号位自行编写 |
| 集成测试 | 20% | 每日构建 | 2号位+3号位 |
| E2E测试 | 10% | 每周/里程碑 | 3号位+4号位 |

---

## 🎯 各号位测试职责

### **1号位：计算域测试**

**测试重点**：
- 排程算法正确性（倒排、正排、翻转）
- 时间槽寻址精度
- 换型优化效果
- 性能基准（GC零分配、耗时达标）

**覆盖率要求**：> 90%（核心算法必须100%）

**特殊要求**：
- 使用 BenchmarkDotNet 做性能基准测试
- 验证热点路径无堆内存分配
- 边界条件：空输入、单任务、满负荷、时间溢出

**测试数据规模**：
- 小规模：100 Task
- 中规模：1万 Task
- 大规模：45万 Task（生产级压测）

---

### **2号位：框架与基础设施测试**

**测试重点**：
- BOM遍历引擎正确性（DFS、LLC计算、环路检测）
- 数据同步正确性（MaterialMapping SCD Type 2）
- SqlBulkCopy 性能与数据完整性
- 权限校验（双重防护、数据范围）
- 插槽接口契约一致性

**覆盖率要求**：> 85%

**特殊要求**：
- 数据库相关测试使用 TestContainers 或本地测试库
- SqlBulkCopy 测试必须验证去重（.Distinct()）
- BOM环路检测必须有专项测试
- 权限测试覆盖正向+越权场景

---

### **3号位：API与集成测试**

**测试重点**：
- API 响应格式、状态码、错误处理
- Hangfire 调度正确性（拓扑顺序、超时、重试）
- MES集成（幂等性、去重、容错）
- 审批流状态机
- 全局异常拦截

**覆盖率要求**：> 80%

**特殊要求**：
- API 测试使用 WebApplicationFactory（内存测试服务器）
- MES集成测试使用 Mock MES 端点
- 并发测试：模拟7域同时排程
- 异常注入测试：网络超时、数据库不可用

---

### **4号位：前端测试**

**测试重点**：
- 组件渲染正确性
- 用户交互（拖拽、点击、筛选）
- API调用参数校验（必须带过滤条件）
- 权限控制（按钮显隐、路由守卫）

**覆盖率要求**：> 70%

**特殊要求**：
- 使用 Vitest + Vue Test Utils
- 甘特图组件使用 Mock 数据
- 验证无过滤全量请求会被拦截
- 响应式布局测试（桌面端+平板端）

---

### **5号位：业务规则测试**

**测试重点**：
- 每个业务规则插件的完整测试
- Pegging连线正确性（只连Order，不连Task）
- 合批规则正确性（MinBatchSize、时间窗）
- CTP评估准确性
- 纯函数验证（无副作用）

**覆盖率要求**：> 90%（业务规则必须100%）

**特殊要求**：
- 每个插件至少10个测试用例
- 必须验证"不修改输入数据"（纯函数）
- 必须验证"不查库"（无I/O依赖）
- 业务规则变更时，先修改测试再修改代码（TDD）

---

## 📝 测试命名规范

### **方法命名格式**

```
方法名_场景_期望结果
```

**示例**：
```csharp
[Fact]
public void CalculateTimeSlot_WhenDueDateIsInPast_ShouldFlipToForwardScheduling()

[Fact]
public void PeggingRule_WhenSupplyInsufficient_ShouldReturnPartialMatch()

[Fact]
public void BOMTraversal_WhenCircularReference_ShouldThrowCycleDetectedException()
```

### **测试类命名格式**

```
被测类名Tests
```

**示例**：
```csharp
public class TimeWindowTests { }
public class PeggingRuleTests { }
public class BOMTraversalEngineTests { }
```

---

## 🧪 测试数据管理

### **原则**

1. **测试数据自包含**：每个测试用例自己准备数据，不依赖外部数据源
2. **测试间隔离**：测试之间不共享可变状态
3. **可重复执行**：任何测试在任何环境下执行结果一致

### **测试数据构造方式**

**方式1：Builder 模式（推荐）**

```csharp
var order = new OrderBuilder()
    .WithMaterialCode("FG-A900")
    .WithQuantity(100)
    .WithDueDate(DateTime.Today.AddDays(7))
    .Build();
```

**方式2：Object Mother 模式**

```csharp
var task = TestDataFactory.CreateStandardTask();
var tasks = TestDataFactory.CreateTaskBatch(count: 1000);
```

**方式3：JSON Fixture（复杂场景）**

```
tests/
├── fixtures/
│   ├── small-domain-100tasks.json
│   ├── medium-domain-10000tasks.json
│   └── large-domain-450000tasks.json
```

---

## ⚡ 性能测试策略

### **性能基准指标**

| 指标 | 目标值 | 红线值 |
|------|--------|--------|
| 单域排程时间 | ≤ 20分钟 | ≤ 30分钟 |
| 单域内存占用 | ≤ 1.5GB | ≤ 2GB |
| SqlBulkCopy 350万行 | ≤ 3分钟 | ≤ 5分钟 |
| BOM展开 80万BOMNO | ≤ 10分钟 | ≤ 15分钟 |
| LLC计算 350万行 | ≤ 3分钟 | ≤ 5分钟 |
| API响应时间（P99） | ≤ 200ms | ≤ 500ms |
| 快照封存 | ≤ 30秒 | ≤ 1分钟 |
| 快照读取 | ≤ 15秒 | ≤ 30秒 |

### **性能测试执行**

**1号位性能测试**：
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class SchedulingBenchmark
{
    [Benchmark]
    public void Schedule_SingleDomain_450KTasks()
    {
        var engine = new SchedulingEngine();
        var context = TestDataFactory.CreateLargeDomainContext();
        engine.Schedule(context);
    }
}
```

**2号位性能测试**：
```csharp
[Fact]
public async Task SqlBulkCopy_350WRows_ShouldCompleteWithin5Minutes()
{
    var stopwatch = Stopwatch.StartNew();
    await _bulkCopyService.BulkInsert(testData_350W);
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed.TotalMinutes <= 5);
}
```

---

## 🔄 测试执行流程

### **开发时（每次提交前）**

```bash
# 运行本号位的单元测试
dotnet test src/APS.Core.Tests --filter "Category=Unit"

# 快速检查（< 1分钟）
dotnet test --filter "Category!=Performance & Category!=Integration"
```

### **每日构建（CI）**

```bash
# 运行所有单元测试
dotnet test --filter "Category=Unit"

# 运行集成测试
dotnet test --filter "Category=Integration"

# 生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

### **里程碑/发布前**

```bash
# 全量测试（含性能测试）
dotnet test

# 性能基准测试
dotnet run --project tests/APS.Performance.Tests

# E2E 测试
dotnet test --filter "Category=E2E"
```

---

## 📊 测试覆盖率要求

| 模块 | 最低覆盖率 | 目标覆盖率 |
|------|-----------|-----------|
| APS.Core（1号位） | 90% | 95% |
| APS.Engine（2号位） | 85% | 90% |
| APS.Orchestrator（3号位） | 80% | 85% |
| APS.WebUI（4号位） | 70% | 80% |
| APS.BusinessRules（5号位） | 90% | 95% |
| APS.Shared | 80% | 85% |
| **整体** | **80%** | **85%** |

### **覆盖率例外**

以下代码**不计入**覆盖率统计：
- 自动生成的代码（EF Core Migrations 等）
- 第三方库的包装类
- 纯配置类（appsettings.json 映射）
- Main/Program.cs 启动代码

---

## 🚨 测试红线

1. **严禁删除已有测试**（除非对应功能被彻底移除）
2. **严禁跳过失败的测试**（[Fact(Skip = "...")] 只能临时使用）
3. **严禁测试依赖执行顺序**
4. **严禁测试依赖外部网络**
5. **严禁在测试中使用 Thread.Sleep**（使用 async/await）
6. **严禁测试使用生产数据库**

---

## ✅ 测试检查清单

**每次提交前**：
- [ ] 新代码有对应的单元测试
- [ ] 所有单元测试通过
- [ ] 覆盖率满足要求
- [ ] 无跳过的测试

**每日构建**：
- [ ] 所有单元测试通过
- [ ] 所有集成测试通过
- [ ] 覆盖率报告已生成

**发布前**：
- [ ] 全量测试通过
- [ ] 性能测试达标
- [ ] E2E测试通过
- [ ] 覆盖率满足要求

---

**维护责任人**：2号位  
**最后更新**：2026-03-31
