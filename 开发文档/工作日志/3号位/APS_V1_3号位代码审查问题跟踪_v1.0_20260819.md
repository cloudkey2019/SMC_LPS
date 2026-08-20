# APS V1 3号位代码审查问题跟踪

> 生成日期：2026-08-19
> 审查对象：DemandPriority 功能（阶段 A 数据模型 / 阶段 B 验证器 / 阶段 C 匹配器）
> 审查方式：code-reviewer agent（superpowers）
> 关联交付契约：`APS_V1_3号位交付契约_v0.2_20260817.md`

---

## 一、审查范围

| 阶段 | 文件 | 说明 |
|---|---|---|
| A（数据模型） | `LPS.APS.Core/Dto/FrozenStrategySnapshot.cs` | 2↔3 共享 DTO 契约 |
| B（验证器） | `LPS.APS.Application/Services/DemandPriorityValidator.cs` | 发布前校验服务 |
| C（匹配器） | `LPS.APS.Application/Services/DemandPriorityMatcher.cs` | 匹配与排序服务 |

---

## 二、已解决问题（2026-08-19）

### 阶段 C - 核心缺陷修复

| # | 问题 | 级别 | 状态 |
|---|---|---|---|
| C-1 | 集成测试失败：JSON 反序列化后 `SegmentMatchCondition.Value` 为 `JsonElement`，类型比较失败导致所有 Demand 无法匹配 Segment | 阻断 | ✅ 已修复 |
| C-2 | `NormalizeJsonValue` 数值类型转换不完整（全转 decimal，与 int/double 字段不匹配） | MEDIUM | ✅ 已修复 |
| C-3 | `IsInList` 未规范化列表项（JSON 数组元素仍为 JsonElement） | MEDIUM | ✅ 已修复 |
| C-4 | `CompareValues` 缺少类型兼容性检查（类型不兼容时 CompareTo 抛异常） | MEDIUM | ✅ 已修复 |
| C-5 | `FindFirstMatchSegment` 每次调用重复排序 | MEDIUM | ✅ 已修复（补充文档说明） |
| C-6 | 魔法值 `int.MaxValue` 表示"未命中" | LOW | ✅ 已修复（提取常量） |

### 阶段 B - 验证器缺陷修复

| # | 问题 | 级别 | 状态 |
|---|---|---|---|
| B-1 | IN 操作符验证误判：`is not IEnumerable` 错误匹配 string | HIGH | ✅ 已修复 |
| B-2 | PriorityScore 检测过宽：`Contains("Score")` 误伤 CustomerScore 等 | HIGH | ✅ 已修复 |
| B-3 | null 值验证错误消息缺少操作符信息 | MEDIUM | ✅ 已修复 |
| B-4 | 缺少 SegmentOrder 范围验证（须为正整数） | MEDIUM | ✅ 已修复 |
| B-5 | 警告与错误混合："建议配置排序字段"被当错误 | MEDIUM | ✅ 已修复（新增 Warnings 集合） |

### 阶段 A - 按决定跳过

| # | 问题 | 级别 | 状态 |
|---|---|---|---|
| A-1 | 不可变性：`{ get; set; }` 改 `{ get; init; }` | CRITICAL | ⏭️ 按 3 号位决定跳过，保持 `set` 现状 |

---

## 三、未解决问题

### 阶段 A（数据模型 `FrozenStrategySnapshot.cs`）

| # | 问题 | 级别 | 影响 | 处理建议 |
|---|---|---|---|---|
| A-2 | **可变集合暴露**：`Segments`/`MatchConditions`/`SortFields` 等 `List<T>` 属性 + setter，外部代码可修改内部状态 | 🔴 CRITICAL | 违背冻结语义（注释声明"不可变，Run 内不刷新"，但实现允许外部篡改共享配置） | 契约级改动，需先更新契约文档（红线 5）；建议改为 `IReadOnlyList<T>` |
| A-3 | **`Value` 弱类型**：`SegmentMatchCondition.Value` 用 `object?`，编译期无类型安全 | 🟡 HIGH | 运行时类型错误、序列化问题、验证困难 | 契约级重构，建议判别联合（`ScalarValue`/`ListValue` 分离）；当前 `NormalizeJsonValue` 已兜底，非紧急 |
| A-4 | **缺少 `required` 关键字**：`SegmentName`、`Field`、`Operator` 等必填属性未标记 | 🟢 MEDIUM | 允许构造非法 DTO | 低风险局部改动 |
| A-5 | **空值语义不一致**：可选字段混用 `string?` 与 `= ""` 默认值 | 🟢 MEDIUM | 可读性/一致性 | 低风险局部改动 |

### 阶段 B（验证器 `DemandPriorityValidator.cs`）

| # | 问题 | 级别 | 影响 | 处理建议 |
|---|---|---|---|---|
| B-6 | **未验证单个 SortField 合法性**：只检查 `SortFields.Count == 0`，未校验内部元素 | 🟢 MEDIUM | 非法 SortField 可能通过校验 | 低风险局部改动 |
| B-7 | **未检查 SegmentName 重复**：允许重名 Segment | 🟢 MEDIUM | UI/日志混淆 | 低风险局部改动 |
| B-8 | **无输入大小限制**：无 Segment/条件数量上限 | 🔵 LOW | 潜在 DOS 风险（超大配置对象） | 建议添加合理上限（如 Segment ≤ 100、条件 ≤ 50、名称 ≤ 255） |
| B-9 | **ValidationResult 可改 record** | 🔵 LOW | 可维护性改进 | 风格性建议，可跳过 |

### 阶段 C（匹配器 `DemandPriorityMatcher.cs`）

| # | 问题 | 级别 | 影响 | 处理建议 |
|---|---|---|---|---|
| C-7 | **GetTieBreakValue 静默回退**：配置错误字段名被静默忽略（默认回退 OrderId） | 🔵 LOW | 配置错误难调试 | 建议记录警告或开发环境抛异常 |

---

## 四、建议优先级

1. **高优先（契约级，需 0 号位/2 号位协同）**：A-2、A-3
2. **中优先（局部改动，价值/成本比高）**：B-6、B-7、C-7
3. **低优先（防御性/风格性）**：A-4、A-5、B-8、B-9

## 五、红线提醒

- **红线 5**：接口即契约。A-2、A-3 涉及修改 2↔3 共享 DTO 签名，必须先更新契约文档（`APS_V1_3号位交付契约_v0.2`）再改代码。
- **红线 1/2**：本功能在 Application 层（3 号位），无数据库依赖、无 LINQ 热点路径，合规。
