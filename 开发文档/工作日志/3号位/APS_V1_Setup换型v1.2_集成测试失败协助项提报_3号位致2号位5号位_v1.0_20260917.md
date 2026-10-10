# Setup换型 v1.2 —— 集成测试失败协助项提报（3号位 致 2号位 / 5号位）

> 发件：3号位（规则/参数/运行生命周期治理）／抄 0号位、2号位、5号位
> 日期：2026-09-17
> 依据：2026-09-17 集体 `dotnet test LPS.APS.sln` 实测
> 性质：3号位 自属项（Setup 治理 #1~#5 + 审计 FK seed）已收口转绿；剩余 **19 个失败**跨号位，按红线（不越位）转正式协助项提报。
> 术语约定：英文契约词一律「中文名称（英文）」；代码锚点引 `文件:行`。

---

## 〇 测试基线（2026-09-17 实测）

| 程序集 | 失败 | 通过 | 总计 |
|---|---|---|---|
| LPS.APS.Tests | 1 | 262 | 263 |
| LPS.APS.BusinessRules.Tests | 18 | 51 | 69 |
| **合计** | **19** | **313** | **332** |

> 3号位 已修 2 个审计集成测试（`GovernanceVersionServiceIntegrationTests`：`AuditLog.UserId` 外键冲突 → 补 `dbo.[User]` seed），故 `LPS.APS.Tests` 由原 3 失败降至 1 失败。

---

## 一、A 类 —— Moq 无法代理 DatabaseConnectionManager（12 个，致 5号位 / 2号位）

### 根因

`DatabaseConnectionManager`（`LPS.APS.Engine\Data\DatabaseConnectionManager.cs:38,49`）当前**仅一个构造** `public DatabaseConnectionManager(IOptions<DatabaseOptions> options)`。

测试 mock 该类型时，Castle 生成代理报：

```
System.ArgumentException : Can not instantiate proxy of class: LPS.APS.Engine.Data.DatabaseConnectionManager.
  ----> System.MissingMethodException : Constructor on type 'Castle.Proxies.DatabaseConnectionManagerProxy' not found.
```

即 mock 用法与当前构造签名不匹配（类无无参构造，或传参集不含 `IOptions<DatabaseOptions>`）。

### 涉及测试文件（2 个）

- `LPS.APS.BusinessRules.Tests\Loaders\TimedSupplyFactLoaderTests.cs`
- `LPS.APS.BusinessRules.Tests\Integration\Position5IntegrationTests.cs`

### 失败用例（12 个）

1. `EndToEnd_AllSupplyTypes_MapsCorrectly`
2. `EndToEnd_EmptyScope_ReturnsEmptyResult`
3. `EndToEnd_EtaPriorityRules_CalculatesCorrectly`
4. `EndToEnd_MixedQualityFacts_ProcessesValidAndRecordsIssues`
5. `EndToEnd_ValidProcurementFacts_ReturnsAllConverted`
6. `EndToEnd_VerifyFieldMapping_AllFieldsPreserved`
7. `LoadRawFactsAsync_WithAllSupplyTypes_ReturnsAllTypes`
8. `LoadRawFactsAsync_WithEmptyMaterialIds_QueryWithoutMaterialFilter`
9. `LoadRawFactsAsync_WithNullConnectionManager_ThrowsArgumentNullException`
10. `LoadRawFactsAsync_WithNullMaterialIds_QueryWithoutMaterialFilter`
11. `LoadRawFactsAsync_WithValidData_ReturnsRawProcurementFacts`
12. `Performance_1000ProcurementFacts_CompletesWithinTarget`

### 期望动作

- **5号位**：将 `new Mock<DatabaseConnectionManager>(…)` 统一改为按 `IOptions<DatabaseOptions>` 构造注入（如 `new Mock<DatabaseConnectionManager>(Mock.Of<IOptions<DatabaseOptions>>(o => o.Value == new DatabaseOptions{…}))`），或改用可替代边界（接口/子类）。
- **2号位**：确认 `DatabaseConnectionManager` 构造近期是否变更；若需 mock 友好，评估是否补充无参构造或可替代接口边界。

---

## 二、B 类 —— 生产指令位置计算器断言漂移（6 个，致 5号位）

### 根因

`ProductionInstructionPositionCalculator`（工作树 M 状态）计算输出与既有测试期望不符，疑似计算口径近期改动未同步测试期望。代表性断言失败：

- `unlocatedPosition.Quantity` → `Expected 15m / But 40m`
- `s10Position.Quantity` → `Expected 10m / But 40m`
- `stagePosition` → `Expected not null / But null`
- `transitPosition.Quantity` → `Expected 30m / But 60m`；`Expected 60m / But 40m`

### 失败用例（6 个）

1. `F05_TransitIndependent_ShouldNotOverlapStage`
2. `F07_StrongFactCorrection_ShouldAdjustPosition`
3. `F09_StageHandoff_ShouldReturnPITransitWithoutShippingTask`
4. `F10_SameSHReceived_ShouldMatchCorrectly`
5. `F11_DifferentSHSameMaterial_ShouldNotMix`
6. `F12_TransitAlreadyReceived_ShouldNotDuplicate`

### 期望动作

- **5号位**：核对 `ProductionInstructionPositionCalculator` 最新口径，明确 F07 强事实修正、F09 阶段交接（PI Transit 无 Shipping Task）、F10/F11/F12 同/异 S/H 匹配与重复去重口径，回写测试期望或修正计算逻辑。

---

## 三、D 类 —— Pegging 编排未落库（1 个，致 2号位）

### 根因

`RealSchedulingIntegrationTest.cs:588`（`VerifyTaskTableAsync`）报：

```
System.Exception : Task表没有数据！PeggingOrchestrator可能未正确INSERT
```

即 `PeggingOrchestrator`（2号位 编排）运行后 `Task` 未 INSERT 落库。

### 期望动作

- **2号位**：排查 `PeggingOrchestrator` 的 Task 持久化链路（SqlBulkCopy / 仓储），确认 INSERT 是否被条件分支跳过、事务回滚或映射遗漏。

---

## 四、3号位 自属已收口（供核对，勿再处置）

- **Setup 治理**（实体 / DDL / 快照载荷 / 冲突校验 / 权限口径 / 审计口径）：单元测试全绿（`SetupTransitionRuleConflictValidatorTests` 6/6；`SolverStrategyValidatorTests` 全绿）。
- **审计 FK 冲突**：已修（`GovernanceVersionServiceIntegrationTests` 5/5，seed `dbo.[User]` 取 `SCOPE_IDENTITY()` 作操作者，替换硬编码 `1001`）。
- 其余挂起项（⑥ 三预算参数待 1号位提值 / ⑨ ExplainTrace 解释类型协调）不属本提报范围，另案推进。