# 5号位与 Windsurf 协作全流程模拟演练

**版本**：v1.0  
**日期**：2026-04-01  
**适用角色**：5号位（业务规则与插件开发）  
**目的**：以一个真实任务为例，完整模拟人与 Windsurf Cascade AI 的协作过程

---

## 📌 演练背景设定

**今天的任务**：实现"CTP插单评估规则"——当VIP客户临时插单时，系统需要在5分钟内评估：能不能做？最早什么时候交？会牺牲哪些现有订单？

**前提条件**：
- 2号位已经定义好了 `ICTPRule` 接口（在 `internal-contracts.yml` 中）
- 2号位已经提供了 `CTPContext`（只读的排程快照）和 `CTPEvaluationResult`（返回结果DTO）
- 5号位需要实现这个接口的具体业务逻辑

---

## 🎬 全流程时间线

```
08:30  开工准备（5分钟）
08:35  第一轮对话：亮身份 + 喂文档 + 要计划（10分钟）
08:45  人审阅计划，确认或调整（5分钟）
08:50  第二轮对话：逐步实现代码（30分钟）
09:20  第三轮对话：编写单元测试（20分钟）
09:40  第四轮对话：红线自查 + 提交前检查（10分钟）
09:50  人做最终审阅，提交代码（10分钟）
10:00  完成
```

---

# 第一阶段：开工准备（人的动作）

## 人做什么？

### ① 打开 Windsurf IDE，打开 APS 项目

确保工作区根目录下有 `.windsurf/` 文件夹，里面包含：
```
.windsurf/
├── rules.md                    ← 架构红线（必读）
├── contracts/
│   └── internal-contracts.yml  ← 接口契约（必读）
├── templates/code/
│   └── 5-business-rule-plugin-template.cs  ← 5号位代码模板
└── workflows/
    └── ai-prompts-guide.md     ← 提示词指南
```

### ② 花2分钟看一眼今天的任务

查看任务看板或邮件，确认今天的任务是：**实现 ICTPRule 接口**。

### ③ 打开 Cascade 聊天面板

在 Windsurf 左侧或底部找到 Cascade（AI助手）面板，准备开始对话。

> **💡 关键心态**：把 Cascade 当作一个"很聪明但不了解项目的新同事"。你需要告诉他项目规则、你的角色、今天要做什么。他会帮你写代码，但你要审查。

---

# 第二阶段：第一轮对话 — 亮身份 + 喂文档 + 要计划

## 人发给 Windsurf 的第一条消息

> **💬 人说（复制粘贴到 Cascade 聊天框）**：

```
我是5号位（业务规则与插件开发），今天需要实现"CTP插单评估规则"。

我的职责边界：
- 只写业务规则代码，不写框架代码
- 插件返回只读凭证，不直接修改状态
- 全程禁止查库（纯函数，基于内存快照运算）
- Pegging只连Order层级，严禁连到Task层级

请先阅读以下文件，理解项目上下文：
1. .windsurf/rules.md（架构红线）
2. .windsurf/contracts/internal-contracts.yml（内部契约）
3. .windsurf/templates/code/5-business-rule-plugin-template.cs（5号位代码模板）

读完后，请输出你的实现计划，包括：
1. 需要创建/修改的文件清单
2. 每个文件的核心改动点
3. 可能的风险点
4. 验证方式

等我确认后再开始写代码。不要先写代码。
```

## Windsurf 会做什么？

Cascade 收到这条消息后，会：

1. **自动读取你指定的3个文件**（你会在聊天面板中看到它调用 Read 工具）
2. **理解项目上下文**：架构红线、接口契约、代码风格
3. **输出一份实现计划**（而不是直接写代码）

## Windsurf 大概会回复什么？

> **🤖 Cascade 回复（示意）**：

```
已阅读完3份文件。以下是实现计划：

## 实现计划

### 需要创建的文件
1. `src/APS.BusinessRules/Plugins/CTPEvaluationRule.cs`
   - 实现 ICTPRule 接口
   - 核心方法：Evaluate(CTPContext context) → CTPEvaluationResult
   
2. `tests/APS.BusinessRules.Tests/CTPEvaluationRuleTests.cs`
   - xUnit 测试，覆盖6个场景

### 核心算法思路
1. 遍历插单需求的BOM，检查各层物料可用量
2. 在资源时间轴上寻找"产能白地"（空闲时段）
3. 如果白地不够，评估哪些现有订单可以被"挤压"
4. 计算代价：被挤压订单的延期天数
5. 输出可行性判断 + 最早交期 + 代价清单

### 风险点
- 需要确认 CTPContext 中是否包含资源时间轴数据
- 需要确认"可牺牲订单"的筛选规则（只能牺牲SS，不能牺牲SO？）

### 验证方式
- 单元测试覆盖率 > 80%
- 红线自查（纯函数、不查库、不改状态）

请确认计划是否OK？有两个风险点需要你澄清。
```

## 人做什么？（审阅计划）

> **💡 这是人最重要的价值环节**。不要跳过。花5分钟认真看计划。

### 检查清单：
- [ ] 文件路径是否正确？（是否放在了 `APS.BusinessRules` 命名空间下）
- [ ] 算法思路是否符合业务需求？
- [ ] 有没有遗漏的场景？
- [ ] 风险点是否需要澄清？

### 人回复 Windsurf

> **💬 人说**：

```
计划整体OK。关于你提到的两个风险点：

1. CTPContext 中包含 ResourceTimeSlots 字段，是资源时间轴的只读快照
2. 可牺牲订单的规则：只能牺牲 SS 和 SS-U，不能牺牲 SO。
   且牺牲优先级为：SS优先被牺牲 > SS-U > SO绝对保护。
   同优先级内，交期最晚的优先被牺牲。

另外补充一点：插单评估必须在5分钟内完成，算法复杂度不能超过 O(n log n)。

请根据以上澄清更新计划，确认后开始实现。
```

## Windsurf 会做什么？

Cascade 会更新计划，吸收你的业务澄清，然后等你最终确认。

> **🤖 Cascade 回复（示意）**：

```
收到，已更新计划：
- CTPContext.ResourceTimeSlots 用于产能白地检索
- 牺牲优先级：SS > SS-U > SO（绝对保护）
- 同优先级内：交期最晚的优先牺牲
- 算法复杂度控制在 O(n log n)

确认后我开始实现第一个文件。
```

> **💬 人说**：

```
确认，开始实现。我们一个方法一个方法来。先实现核心的 Evaluate 方法。
```

---

# 第三阶段：第二轮对话 — 逐步实现代码

## Windsurf 会做什么？

Cascade 开始写代码。你会在聊天面板中看到它调用 `write_to_file` 或 `edit` 工具，直接在你的项目中创建/修改文件。

**关键**：代码会直接出现在你的 IDE 编辑器中，你可以实时看到。

## Windsurf 大概会生成什么？

> **🤖 Cascade 生成代码（直接写入你的项目文件）**：

```csharp
// 文件：src/APS.BusinessRules/Plugins/CTPEvaluationRule.cs

using System;
using System.Collections.Generic;
using System.Linq;
using APS.Engine.Contracts;

namespace APS.BusinessRules.Plugins
{
    /// <summary>
    /// CTP插单评估规则
    /// 评估插单可行性、最早交期、以及对现有订单的影响
    /// </summary>
    /// <remarks>
    /// 【5号位红线】：
    /// - 纯函数：禁止查库、禁止I/O、禁止修改输入数据
    /// - 返回只读 CTPEvaluationResult，不直接修改任何排程状态
    /// - 算法复杂度 ≤ O(n log n)
    /// </remarks>
    public class CTPEvaluationRule : ICTPRule
    {
        public CTPEvaluationResult Evaluate(CTPContext context)
        {
            if (context == null) 
                throw new ArgumentNullException(nameof(context));

            // Step 1: 检查物料可用性
            var materialGaps = CheckMaterialAvailability(
                context.InsertOrder, 
                context.AvailableSupplies);

            // Step 2: 在资源时间轴上寻找产能白地
            var capacitySlots = FindCapacitySlots(
                context.InsertOrder,
                context.ResourceTimeSlots);

            // Step 3: 如果白地不够，评估可牺牲订单
            var sacrificeList = new List<SacrificeItem>();
            if (!capacitySlots.IsSufficient)
            {
                sacrificeList = EvaluateSacrifice(
                    context.ExistingOrders,
                    context.InsertOrder,
                    capacitySlots.Deficit);
            }

            // Step 4: 汇总结果
            return BuildResult(
                context.InsertOrder, 
                materialGaps, 
                capacitySlots, 
                sacrificeList);
        }
        
        // ... 后续私有方法 ...
    }
}
```

## 人做什么？（实时审查代码）

> **💡 不要等 Cascade 全部写完再看。边写边看，发现问题立即说。**

### 审查要点：

| 检查项 | 怎么检查 | 通过？ |
|--------|---------|--------|
| 命名空间是否正确 | `APS.BusinessRules.Plugins` | ✅ |
| 是否有数据库操作 | 搜索 `DbContext`、`SqlConnection` | ✅ 没有 |
| 是否修改了输入数据 | 检查是否有 `context.XXX = yyy` | ✅ 没有 |
| 是否返回只读结果 | 检查返回类型 | ✅ |
| 算法复杂度 | 有没有嵌套循环导致 O(n²) | ⚠️ 需要继续看私有方法 |

### 如果发现问题，人立即反馈

假设你发现 `EvaluateSacrifice` 方法里有一个嵌套循环：

> **💬 人说**：

```
EvaluateSacrifice 方法中有一个 O(n²) 的嵌套循环。
请优化为 O(n log n)，可以先对 existingOrders 按牺牲优先级排序，
然后用贪心算法逐个填充产能缺口。
```

## Windsurf 会做什么？

Cascade 会修改代码，用 `edit` 工具直接在文件中替换那段逻辑。你会在 IDE 中看到 diff。

### 继续逐方法推进

> **💬 人说**：

```
Evaluate 主方法OK。现在实现 CheckMaterialAvailability 私有方法。
需求：
- 遍历插单的BOM（已展开，在 context.InsertOrder.BomItems 中）
- 检查每个物料的可用量是否满足需求
- 不满足的输出缺口列表
```

如此反复，直到所有方法实现完成。

---

# 第四阶段：第三轮对话 — 编写单元测试

## 人发给 Windsurf 的消息

> **💬 人说**：

```
CTPEvaluationRule 的代码已经实现完毕。
现在请帮我编写完整的 xUnit 单元测试。

测试文件路径：tests/APS.BusinessRules.Tests/CTPEvaluationRuleTests.cs

请覆盖以下场景：
1. 正常插单：产能充足、物料充足 → 返回"可行"
2. 产能不足但可牺牲SS → 返回"可行" + 牺牲清单
3. 产能不足且无可牺牲订单 → 返回"不可行"
4. 物料缺口 → 返回"不可行" + 缺料清单
5. 空输入 → 抛出 ArgumentNullException
6. 极端场景：10万条订单（性能不超时）

测试命名格式：Evaluate_场景描述_预期结果
使用 Moq 构造测试数据，不要连接真实数据库。

请同时在测试中验证：
- 返回结果是只读的
- 原始 context 没有被修改
```

## Windsurf 会做什么？

Cascade 会在 `tests/` 目录下生成完整的测试文件，包含所有6个测试用例。

## Windsurf 大概会生成什么？

> **🤖 Cascade 生成测试代码（示意）**：

```csharp
public class CTPEvaluationRuleTests
{
    private readonly CTPEvaluationRule _sut = new();

    [Fact]
    public void Evaluate_SufficientCapacityAndMaterial_ReturnsFeasible()
    {
        // Arrange
        var context = BuildContext(
            insertQty: 100, 
            availableCapacity: 200, 
            availableMaterial: 500);
        
        // Act
        var result = _sut.Evaluate(context);
        
        // Assert
        result.IsFeasible.Should().BeTrue();
        result.SacrificeList.Should().BeEmpty();
        result.MaterialGaps.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_InsufficientCapacity_SacrificesSS_ReturnsFeasible()
    {
        // Arrange
        var context = BuildContext(
            insertQty: 100,
            availableCapacity: 50,  // 不够
            existingOrders: new[]
            {
                CreateOrder("SS-001", OrderType.SS, dueDate: "2026-04-10"),
                CreateOrder("SO-001", OrderType.SO, dueDate: "2026-04-08"),
            });

        // Act
        var result = _sut.Evaluate(context);

        // Assert
        result.IsFeasible.Should().BeTrue();
        result.SacrificeList.Should().HaveCount(1);
        result.SacrificeList[0].OrderId.Should().Be("SS-001"); // SS被牺牲，SO被保护
    }

    [Fact]
    public void Evaluate_NullContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Evaluate(null));
    }

    [Fact]
    public void Evaluate_DoesNotMutateContext()
    {
        // Arrange
        var context = BuildContext(insertQty: 100, availableCapacity: 50);
        var originalOrderCount = context.ExistingOrders.Count;

        // Act
        _sut.Evaluate(context);

        // Assert — 原始数据不应被修改
        context.ExistingOrders.Count.Should().Be(originalOrderCount);
    }
    
    // ... 更多测试 ...
}
```

## 人做什么？（运行测试）

> **💬 人说**：

```
请运行这些单元测试，看看是否全部通过。
```

Cascade 会在终端中执行 `dotnet test`，你可以看到测试结果。

### 如果有测试失败

> **💬 人说**：

```
Evaluate_InsufficientCapacity_SacrificesSS_ReturnsFeasible 这个测试失败了。
错误信息是：Expected SacrificeList to have 1 item(s), but found 0.
请分析原因并修复。注意：只在我的职责范围内修复。
```

Cascade 会分析失败原因，定位到代码 bug，然后用 `edit` 工具修复。

---

# 第五阶段：第四轮对话 — 红线自查 + 提交前检查

## 人发给 Windsurf 的消息

> **💬 人说**：

```
代码和测试都OK了。提交前请帮我做最后检查：

1. 请逐条对照 .windsurf/rules.md 中的红线，检查 CTPEvaluationRule.cs：
   - 是否有数据库操作？（禁止）
   - 是否有修改输入状态的代码？（禁止）
   - 是否有连到Task层级的Pegging？（禁止）
   - 是否有凭空造单的逻辑？（禁止）
   - 是否有长篇if-else硬编码？（应该用策略模式）

2. 是否有未处理的编译警告？
3. 是否有硬编码的魔法数字？
4. 测试覆盖率是否足够？
5. 提交信息建议？
```

## Windsurf 会做什么？

Cascade 会重新读取你的代码文件和 `rules.md`，逐条对比检查，然后输出一份检查报告。

## Windsurf 大概会回复什么？

> **🤖 Cascade 回复（示意）**：

```
## 红线检查报告

### ✅ 通过项
- [红线] 无数据库操作：未发现 DbContext、SqlConnection 等
- [红线] 无状态修改：所有方法均为纯函数，未修改 context
- [红线] 无Task层级Pegging：仅操作 Order 层级
- [红线] 无凭空造单：未发现 new Order() 等
- [红线] 无长篇if-else：使用了排序+贪心策略

### ⚠️ 建议修改
1. 第47行：`var threshold = 3;` 是魔法数字
   → 建议提取为常量：`private const int DefaultSacrificeThresholdDays = 3;`

2. 第82行：缺少日志输出
   → 建议添加：当牺牲订单列表非空时，输出 Warning 级别日志

### 📝 提交信息建议
feat(business-rules): implement CTP evaluation rule

- Implement ICTPRule interface for insert order evaluation
- Support sacrifice priority: SS > SS-U > SO (protected)
- Algorithm complexity: O(n log n)
- Unit test coverage: 6 scenarios
```

## 人做什么？（确认修改）

> **💬 人说**：

```
两个建议都接受，请帮我修改。
修改完成后，再运行一次测试确认全部通过。
```

Cascade 修改代码，运行测试，确认全部通过。

---

# 第六阶段：人做最终审阅，提交代码

## 人做什么？

### ① 最终肉眼审阅（5分钟）

在 IDE 中打开修改过的文件，快速浏览：
- 代码风格是否与团队一致？
- 注释是否清晰？
- 有没有"AI味"太重的写法？（如过于verbose的注释）

### ② 确认 diff

用 IDE 的 Git diff 功能，确认所有改动都是预期内的，没有意外修改到其他文件。

### ③ 提交代码

```bash
svn add src/APS.BusinessRules/Plugins/CTPEvaluationRule.cs
svn add tests/APS.BusinessRules.Tests/CTPEvaluationRuleTests.cs
svn commit -m "feat(business-rules): implement CTP evaluation rule"
```

### ④（可选）通知2号位审查

> 给2号位发消息：
> "我完成了 CTP插单评估规则 的实现，已提交。请帮忙 review。
> 文件：CTPEvaluationRule.cs + CTPEvaluationRuleTests.cs"

---

# 📊 全流程总结

## 人 vs Windsurf 的分工

| 环节 | 人做什么 | Windsurf做什么 |
|------|---------|---------------|
| **开工准备** | 确认任务、打开项目 | — |
| **亮身份** | 告诉AI自己是谁、能做什么、不能做什么 | 读取并理解项目规则 |
| **喂文档** | 指定AI要读哪些文件 | 读取rules.md、契约、模板 |
| **要计划** | 要求AI先出计划不出代码 | 输出实现计划 + 风险点 |
| **审计划** | **审阅计划、澄清业务问题** | 吸收澄清、更新计划 |
| **写代码** | 指导"先写哪个方法"、审查每段代码 | 生成代码，直接写入项目文件 |
| **发现问题** | **指出问题、给出修改方向** | 修改代码 |
| **写测试** | 列出测试场景 | 生成测试代码 |
| **跑测试** | 确认测试结果 | 执行 dotnet test |
| **红线自查** | 要求AI自查 | 逐条对照红线检查 |
| **最终审阅** | **肉眼审阅、确认diff** | — |
| **提交代码** | svn commit | — |

## 人的核心价值（AI不能替代的）

| 能力 | 说明 |
|------|------|
| **业务判断** | "可牺牲订单只能牺牲SS，不能牺牲SO"——这是业务决策 |
| **方案取舍** | 算法有3种方案，选哪个？——这是工程判断 |
| **质量把关** | 最终决定代码能不能提交——这是责任 |
| **跨角色沟通** | 需要和2号位确认接口细节——这是协作 |
| **异常处理** | AI生成了违规代码，要能识别并拒绝——这是经验 |

## Windsurf 的核心价值（人手动做太慢的）

| 能力 | 说明 |
|------|------|
| **代码生成** | 根据你的指令快速生成符合模板的代码 |
| **规则记忆** | 记住所有红线并自查，不会像人一样"忘了" |
| **测试生成** | 快速生成6个测试用例，覆盖各种边界 |
| **模式套用** | 严格遵循 5-business-rule-plugin-template.cs 模板 |
| **重复劳动** | 批量命名、添加注释、格式化——AI做更快更准 |

---

# 🎯 5号位协作 Tips

### Tip 1：每次新对话都要"重新亮身份"

Windsurf 的 Cascade **不会记住上一次对话**的上下文。每次打开新的聊天，第一条消息必须包含：
- 你是几号位
- 你的职责边界
- 今天要做什么

### Tip 2：永远让 AI "先出计划、再写代码"

不要说"帮我实现XXX"然后就等着。要说"先出计划，等我确认"。这样你可以在代码生成之前纠偏，而不是在生成之后返工。

### Tip 3：小步迭代，一个方法一个方法来

不要说"把整个类全部实现"。要说"先实现 Evaluate 主方法"，确认OK后再说"现在实现 CheckMaterialAvailability"。小步 = 好审查 = 少Bug。

### Tip 4：发现违规立即拒绝

如果 AI 生成了包含 `DbContext`、`HttpClient`、`context.Orders.Add(...)` 等禁止操作的代码，**立即说**：

```
这段代码违反了5号位红线。5号位禁止查库、禁止修改状态。
请删除这段代码并重写，使用纯函数方式实现。
```

不要想着"先这样，回头再改"。

### Tip 5：善用"模板"

5号位有现成的代码模板（`5-business-rule-plugin-template.cs`），在对话开始时让AI读取这个模板，AI会自动按照模板风格生成代码。

### Tip 6：不确定的时候问 AI

```
我不确定这个实现是否违反了红线，请帮我检查：
[粘贴代码]
请逐条对照 .windsurf/rules.md 检查。
```

### Tip 7：用 AI 做代码审查

当你自己写了代码（不是AI生成的），也可以让 AI 帮你审查：

```
我手动写了以下代码，请帮我检查是否符合5号位的红线要求：
[粘贴代码]
```

---

# 📋 5号位每日协作检查清单

每天开工时，对照此清单：

- [ ] 打开 Windsurf IDE，确认 `.windsurf/` 目录完整
- [ ] 新对话第一条消息：亮身份 + 喂文档
- [ ] 要求 AI 先出计划，我确认后再写代码
- [ ] 逐方法审查 AI 生成的代码
- [ ] 检查：无 DbContext、无 HttpClient、无状态修改
- [ ] 检查：Pegging 只连 Order 层级
- [ ] 检查：返回只读凭证
- [ ] 让 AI 生成单元测试，运行并确认通过
- [ ] 提交前让 AI 做红线自查
- [ ] 最终肉眼审阅 diff
- [ ] 用规范格式提交代码

---

# 🔄 当遇到跨号位问题时怎么办？

### 场景：5号位发现2号位的接口定义有问题

```
我是5号位，在实现 ICTPRule 时发现 CTPContext 缺少 ResourceTimeSlots 字段。
这个字段应该由2号位在 CTPContext 中添加。

请帮我：
1. 确认 .windsurf/contracts/internal-contracts.yml 中 CTPContext 的定义
2. 告诉我缺少哪些字段
3. 帮我写一份给2号位的接口变更请求

注意：我不应该自己修改 CTPContext（那是2号位的代码），我只能提出请求。
```

**Windsurf 会做什么**：帮你对比契约和实际需要，生成一份结构化的变更请求，你复制发给2号位即可。

### 场景：5号位需要 Mock 2号位的接口做本地测试

```
我是5号位，需要调用2号位提供的 IDataSnapshotProvider 接口。
但2号位还没实现完。

请帮我：
1. 查看 .windsurf/contracts/internal-contracts.yml 中该接口的契约定义
2. 生成一个 Mock 实现（用于我本地测试）
3. 确保 Mock 实现符合契约
```

---

**文档结束**

> 此文档是以5号位为例的模拟演练。1-4号位的协作流程结构相同，
> 只是"亮身份"内容、红线要点、和代码模板不同。
> 各号位可参考 `.windsurf/workflows/ai-prompts-guide.md` 获取自己的开场白和提示词。
