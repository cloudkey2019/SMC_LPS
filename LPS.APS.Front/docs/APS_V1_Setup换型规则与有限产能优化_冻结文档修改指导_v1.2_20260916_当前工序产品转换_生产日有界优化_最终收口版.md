# APS V1 Setup换型规则与有限产能优化——冻结文档修改指导

**版本**：v1.2（最终收口版）  
**日期**：2026-09-16  
**适用范围**：APS V1 当前冻结文档修订、0～5号位实施包补充、后续代码整改与验收  
**文档性质**：冻结文档修改指导，不是新的长期平行业务基线  
**替代版本**：v1.1及更早Setup专项指导  
**使用方式**：下一轮冻结文档修订必须先按本文做全量影响分析，再增量回写正式基线；在正式文档更新完成前，各号位可以提出实现疑问，但不得绕过冻结文档直接自由编码。

---

# 0. 本轮重新打开Setup冻结口径的原因

现有冻结文档及部分实施包仍按以下假设描述Setup：

```text
模具 / 刀具 / 材质 / 颜色等SetupAttribute
+
MES / RoutingOperation.SetupTime
+
局部相邻Task启发式
```

2026-09-16开发核查与业务确认后，正式确认：

1. MES当前没有可供APS稳定消费的模具、刀具、颜色、材质等Setup属性事实；
2. MES当前没有可作为APS运行真相的 `RoutingOperation.SetupTime` 数据；
3. Setup不能依赖不存在的数据源；
4. 同一台设备可能承担多道小工序，不同“当前工序”下产品切换时间可能不同；
5. 但Setup不是“前工序→后工序”的工序转换模型；
6. Setup的正确业务语义是：
   > **当前准备排的Task属于某一道当前工序，在当前实际设备上，设备上一产品切换到当前产品需要多少换型时间。**
7. 某些大工艺中Setup时间显著影响设备稼动效率，仅做极小邻域相邻修复不足，需要在同一生产日内进行受控的序列优化。

因此本轮正式改为：

> **车间直接维护“当前工序 + 当前设备”下的产品转换换型时间，并由1号位在有限产能排程中真实占用资源和进行生产日内有界序列优化。**

## 0.1 最终收口红线

本版再补严以下边界，后续冻结文档不得重新解释：

1. Setup规则中只有一个工序维度：**当前Task自身OperationCode**；
2. 上一相邻Task只提供前产品（FromMaterial），其Operation不参与Setup命中；
3. 生产日只是优化搜索边界，不会自动清空设备上一产品状态；
4. 如果确实没有任何可追溯上一产品，则按“初始设备状态”处理，不人为构造虚拟前产品；
5. Setup是真实设备占用，不是纯排序分数；
6. Task插入、删除、移动、交换后，所有受影响的前后邻接Setup必须重新计算；
7. Setup从初始Resource/时间槽候选评价阶段就参与，而不是排完设备以后再补算；
8. 夜间FULL允许同一Resource、同一生产日、固定锚点之间全部可移动Task进入有界邻域搜索候选集；
9. “全部进入候选集”不等于枚举全部排列，不做全局TSP，不承诺数学绝对最优；
10. 动态设备集合只是维护辅助，不得错误要求“前产品也必须能在当前Operation加工”。


---

# 一、本轮最高裁决

## 1.1 V1不再依赖SetupAttribute

正式废止V1以下必备前提：

- Mold；
- Tool；
- Color；
- Material属性组合；
- `SchedulingContext.SetupAttribute`必须有数据才能运行；
- `SetupParams.Dimensions = Mold/Tool/Material/Color`作为正式V1换型判断依据。

上述属性未来如果数据成熟，可以作为自动生成/辅助维护Setup规则的来源，但不是V1运行前提。

---

## 1.2 MES不存在SetupTime运行真相

正式纠正：

> **MES当前没有可作为APS Setup运行真相的 `RoutingOperation.SetupTime` / MES Routing SetupTime数据。**

因此：

- 不得把MES `SetupTime`写成已存在事实；
- 不得把它作为新Setup规则缺失后的第五层或任何fallback；
- 不得继续要求3号位/2号位从MES补传一个实际上不存在的SetupTime；
- 不得让1号位依赖Routing节点上的SetupTime完成换型计算。

如果APS历史DDL/实体中已经存在 `RoutingOperation.SetupTime` 兼容字段：

> **正式退出V1运行真相。**

物理字段是否立即删除由数据库兼容评估决定，但：

- 新业务文档不得继续把它写成MES来源；
- 新代码不得读取；
- 不参与fallback；
- 不与新Setup转换规则叠加。

---

# 二、Setup正式业务模型

## 2.1 Setup看“当前工序 + 当前设备 + 前后产品”

对当前准备排入某一实际资源的Task：

```text
当前Task：
当前产品 = B
当前工序 = OP20
当前设备 = MC01
```

如果MC01上一相邻Task生产的是：

```text
前产品 = A
```

则本次Setup查找的是：

```text
当前工序 OP20
+
当前设备 MC01
+
前产品 A
+
当前产品 B
→ SetupMinutes
```

正式业务键：

```text
ProductionDepartment
+
Stage
+
CurrentOperation
+
Resource
+
FromMaterial
+
ToMaterial
→ SetupMinutes
```

其中：

- `CurrentOperation` = **当前要排的后一个Task自身工序**；
- `Resource` = **当前要把这个Task排到的实际设备**；
- `FromMaterial` = 当前设备上一相邻Task的产品；
- `ToMaterial` = 当前Task产品。

---

## 2.2 明确废止“前小工序 + 后小工序”模型

v1.0曾错误定义：

```text
FromOperation
+
ToOperation
```

本版正式废止。

原因：

> Setup业务不是在计算“上一Task的小工序切到当前Task的小工序需要多久”，而是在当前Task所属工序、当前候选设备这个生产场景下，判断设备从上一产品切换到当前产品需要多久。

因此：

- 上一Task只贡献“前产品”状态；
- 当前Task提供“当前小工序”；
- 不维护 `FromOperationCode`；
- 不维护 `ToOperationCode`；
- 只维护一个当前工序字段 `OperationCode`。

---

## 2.3 为什么仍需要当前工序

同一设备可能承担多道小工序。

例如同一MC01：

```text
当前工序 OP10：
A → B = 15分钟
```

而：

```text
当前工序 OP20：
A → B = 40分钟
```

这两条必须允许不同。

因此不能只按：

```text
Resource + FromMaterial + ToMaterial
```

维护。

正确应为：

```text
Operation + Resource + FromMaterial + ToMaterial
```

并保留生产部门、大工艺作为业务范围和唯一性上下文。

---

# 三、换型规则方向性

必须允许：

```text
当前工序OP20 / MC01：
A → B = 20分钟
```

与：

```text
当前工序OP20 / MC01：
B → A = 60分钟
```

不同。

因此前产品、后产品是有方向关系。

不能把A/B作为无方向产品组合。

---

# 四、默认换型规则

如果没有维护明确的产品A→B规则，允许维护当前工序、当前设备下的默认换型时间。

正式默认规则粒度：

```text
ProductionDepartment
+
Stage
+
CurrentOperation
+
Resource
→ DefaultSetupMinutes
```

含义：

> 在当前工序、当前设备上，如果前后产品没有明确产品对规则，则使用该设备在该工序下的默认产品切换时间。

默认规则不再使用：

- FromOperation；
- ToOperation；
- 模具/刀具/颜色/材质属性。

---

# 五、Setup命中优先级

运行时只保留三层确定性逻辑。

## 第一优先：明确产品转换规则

命中：

```text
Department
+ Stage
+ CurrentOperation
+ Resource
+ FromMaterial
+ ToMaterial
```

使用该 `SetupMinutes`。

---

## 第二优先：当前工序 + 当前设备默认规则

明确产品对不存在时，命中：

```text
Department
+ Stage
+ CurrentOperation
+ Resource
```

使用 `DefaultSetupMinutes`。

---

## 第三优先：无规则

明确规则和默认规则均不存在：

> **Setup = 0分钟。**

同时必须记录解释/数据质量事实：

> “当前部门 / Stage / 工序 / 设备 / 前后产品未维护Setup规则，本次按0分钟计算。”

正式禁止任何额外fallback：

- MES `RoutingOperation.SetupTime`；
- APS历史 `RoutingOperation.SetupTime`；
- 全公司统一30分钟；
- 模具/刀具/颜色属性自动猜测；
- 相似产品推测。

---

# 六、同产品连续生产

在同一当前工序、同一当前设备上：

```text
A → A
```

默认：

> **0分钟。**

如果某现场确实存在同产品连续生产仍需固定准备/清理时间：

> 允许显式维护 `A → A` 规则覆盖默认0分钟。

注意：

> 是否为“同产品”只比较前后产品；不存在“上一工序不同所以自动产生Setup”的逻辑。

当前小工序由当前Task确定。

---

# 七、动态可用设备集合只用于维护辅助

## 7.1 不恢复静态ResourceGroup

设备资格真相继续来自：

> 工序资源资格（OperationResourceEligibility）

不得为了Setup重新建立永久静态资源组。

---

## 7.2 当前工序下的Setup维护建议设备集合

维护：

```text
当前工序 = OP20
前产品 = A
后产品 = B
```

时，不能简单要求：

```text
A也必须能够在OP20加工
```

因为上一产品A可能是在同一设备的其它小工序上加工的；Setup规则本身不包含上一Task工序。

因此建议设备集合应这样理解：

### 当前产品B

设备必须满足当前Task真实资格：

```text
B + 当前Operation OP20 + Resource
```

即B在OP20上的有效 `OperationResourceEligibility`。

### 前产品A

只需要证明A有可能成为该设备上的合法上一产品，例如：

```text
A在同一生产部门 / 同一大工艺下
存在任一有效Operation可以合法使用该Resource
```

因此页面建议集合可以按：

```text
B在当前Operation的合法Resource
∩
A在同部门/同Stage任一有效Operation上可使用的Resource
```

生成。

这只是维护辅助，不是运行时资格真相。

如果现场确认某设备存在真实A→B换型场景，而现有A侧资格历史不完整：

> 允许在“B当前Operation合法Resource”中人工选择该设备维护Setup，但不得因此反向修改A的OperationResourceEligibility。

页面允许：

- 全选建议设备；
- 去掉部分设备；
- 对特殊设备单独维护不同Setup分钟。


## 7.3 动态集合不进入运行模型

动态设备集合只是4号位维护页面的辅助选择工具。

正式保存后：

> **规则最终落到具体Resource。**

Solver运行时不需要知道：

- 当初是否批量创建；
- 属于哪个动态集合；
- 设备是否属于永久组。

因此正式运行态不存在：

- 动态集合规则层；
- SetupResourceGroup主数据。

---

# 八、Setup规则复用现有规则版本治理

## 8.1 不新增第二套Setup版本平台

继续复用：

```text
RuleSet
→ RuleSetVersion

ParameterSet
→ ParameterSetVersion

StrategyProfile
→ StrategyProfileVersion
```

Setup转换规则作为：

> **RuleSetVersion下的具体规则内容。**

不得再建：

- SetupRuleVersion；
- Setup独立发布版本；
- Setup独立生命周期平台。

---

## 8.2 规则内容与算法参数分开

### RuleSetVersion治理

包括：

- 明确产品转换Setup规则；
- 当前工序/设备默认Setup规则；
- 规则启停；
- 冲突校验；
- 版本Diff。

### ParameterSetVersion治理

只放纯算法数值：

- 有界搜索预算；
- 最大邻域尝试次数；
- 单生产日保护阈值；
- 其它Guardrail。

不得把产品转换矩阵塞进参数JSON。

---

# 九、建议最小规则数据结构

建议业务实体：

> 产品转换换型规则（SetupTransitionRule）

最小字段：

| 中文含义 | 建议英文名 | 说明 |
|---|---|---|
| 所属规则集版本 | RuleSetVersionId | 复用现有规则版本 |
| 生产部门 | ProductionDepartmentId | 业务范围 |
| 大工艺 | StageCode | 业务范围 |
| 当前小工序 | OperationCode | 当前要排Task的小工序 |
| 当前设备 | ResourceId | 当前候选/实际Resource |
| 前产品 | FromMaterialId | 明确规则有值 |
| 后产品 | ToMaterialId | 明确规则有值 |
| 规则类型 | RuleType | EXACT / DEFAULT |
| 换型分钟 | SetupMinutes | 真实资源占用分钟 |
| 是否有效 | IsActive | 当前版本内容 |
| 审计字段 | Created/Updated By/At | 沿用现有治理规范 |

### EXACT

要求：

```text
FromMaterialId
ToMaterialId
```

均明确。

### DEFAULT

产品为空：

```text
FromMaterialId = NULL
ToMaterialId = NULL
```

但：

```text
OperationCode
ResourceId
```

必须明确。

---

# 十、规则唯一性和冲突

同一 `RuleSetVersion` 内：

### 明确规则

以下组合只能有一条有效规则：

```text
ProductionDepartmentId
+ StageCode
+ OperationCode
+ ResourceId
+ FromMaterialId
+ ToMaterialId
```

### 默认规则

以下组合只能有一条有效默认规则：

```text
ProductionDepartmentId
+ StageCode
+ OperationCode
+ ResourceId
```

冲突必须在3号位发布前阻止。

不得让Solver运行时随机选规则。

---

# 十一、Setup必须真实占用设备产能

例如：

```text
前Task结束：10:00
当前Task工序：OP20
当前设备：MC01
A → B Setup：45分钟
当前Task加工：120分钟
```

则：

```text
10:00～10:45
MC01被Setup占用

10:45
当前B任务最早开始加工
```

正式冻结：

> **Setup不是排序分数，而是真实资源时间占用。**

V1不要求：

- 单独生成MES Setup工单；
- 单独生成可下发MES的Setup Task。

但1号位必须把Setup计入：

- Resource Timeline；
- Start/End；
- 资源冲突；
- 交期评价；
- 稼动时间。

## 11.1 Task插入/移动后必须重算两侧Setup

原序列：

```text
P → N
```

插入X后：

```text
P → X → N
```

原来的：

```text
Setup(P→N)
```

必须被替换为：

```text
Setup(P→X)
+
Setup(X→N)
```

其中：

```text
Setup(P→X)
= X.Operation + X.Resource + P.Material → X.Material
```

而：

```text
Setup(X→N)
= N.Operation + N.Resource + X.Material → N.Material
```

这仍然不是“前工序→后工序”模型。

每一笔Setup永远使用：

> **被排在后面的当前Task自身OperationCode。**

Task删除、移动、交换时，同样必须把受影响邻接Setup全部重新计算，不能沿用旧序列结果。

---

# 十二、Setup从初始资源/时间槽选择阶段就参与

不能：

```text
先完全不看Setup选设备
↓
资源分配完成
↓
最后再修补Setup
```

正确：

> **Setup既参与初始Resource/时间槽候选评价，也参与后续序列优化。**

例如当前Task为：

```text
产品B
工序OP20
```

可选：

```text
MC01：上一产品A，A→B Setup=60分钟
MC02：上一产品A，A→B Setup=10分钟
```

评价MC01/MC02候选时间槽时，必须同时考虑：

```text
Setup + Process
```

的真实资源占用和完成时间。

如果候选位置处于两个既有Task之间，还必须评价插入后对后继Task Setup的改变，不能只看当前Task自己的Setup。

也就是说，候选位置评价至少要覆盖：

```text
新Task前置Setup
+
新Task加工
+
后继Task因前产品变化产生的新Setup
```

以及由这些变化引起的资源时间轴传播。

---

# 十三、Setup优化目标层级

正式顺序：

```text
第一层：硬约束
↓
第二层：业务优先级 / Demand Protection
↓
第三层：交期与履约目标
↓
第四层：Setup / WIP / 利用率 / 稳定性等次级优化
```

禁止为了减少Setup：

- 延误必须保护的高优先订单；
- 突破Material AvailableTime；
- 突破Routing Dependency；
- 突破Resource Eligibility；
- 移动不可移动执行/Frozen/Firm Task；
- 破坏数量闭合。

---

# 十四、夜间FULL：单资源生产日内有界序列优化

## 14.1 业务目标

某些大工艺换型时间严重影响设备稼动效率。

因此V1不再只做：

> “插入一个Task后检查两个邻居”。

夜间FULL应允许：

> **在同一Resource、同一生产日、同一可移动段内，全部可移动Task进入候选集合，进行有计算预算的启发式序列优化。**

目标：

> 在高层业务目标不恶化的前提下，尽量减少该生产日总Setup时间，提高设备有效稼动。

---

## 14.2 不枚举全部排列

正式定义：

> **单资源有限生产日内的有界序列优化：窗口内全部可移动Task进入候选集合，但只执行有计算预算的启发式邻域搜索，不枚举全部排列，不承诺数学全局最优。**

允许：

- 相邻交换；
- Task插入；
- 小块移动；
- 小范围重新排序；
- 其它等价有界启发式。

不冻结具体算法。

---

## 14.3 生产日边界

以Resource Calendar定义的：

> 生产日 / 连续生产窗口

作为主要搜索边界。

不机械按自然日00:00切断。

但：

> **生产日只是搜索边界，不是设备状态重置点。**

本生产日第一Task仍要读取窗口外最近的上一已确定Task的产品作为：

```text
FromMaterial
```

计算第一笔Setup。

如果某设备在当前计划和可追溯执行上下文中确实找不到任何上一产品状态：

> 按“初始设备状态”处理，本次不形成产品转换Setup，即Setup=0，并记录Explanation。

V1不人为构造“虚拟上一产品”。如未来需要设备冷启动/开机固定准备时间，应单独冻结，不混入产品转换Setup。

---

# 十五、固定锚点切割可移动段

资源生产日中存在：

- 已执行Task；
- Frozen Task；
- Firm不可移动Task；
- 其它明确不可移动Task；

时，它们作为固定锚点。

例如：

```text
固定产品A Task
｜
B C D E   ← 可移动段
｜
固定产品F Task
```

只优化B/C/D/E。

不能跨过固定A/F。

评价段内顺序必须计算：

```text
固定A产品 → 段内第一Task产品
+
段内所有相邻产品转换
+
段内最后Task产品 → 固定F产品
```

每一次Setup查找仍使用：

> **被排入的当前Task自身OperationCode + 当前Resource + 前产品 + 当前产品。**

不读取上一Task的OperationCode作为Setup键。

---

# 十六、夜间FULL建议求解流程

## 第一步：建立初始可行计划

按现有逻辑同时考虑：

- Demand业务优先关系；
- Material AvailableTime；
- Routing / Dependency；
- Resource Eligibility；
- Calendar；
- Firm/Frozen/Execution；
- 当前工序/当前设备产品转换Setup。

---

## 第二步：按Resource × 生产日 × 可移动段形成候选集合

窗口内全部可移动Task都进入候选集合。

---

## 第三步：计算相邻Setup

对某个当前Task：

```text
FromMaterial
= 当前设备上上一相邻Task产品

ToMaterial
= 当前Task产品

OperationCode
= 当前Task小工序

ResourceId
= 当前Task候选/实际设备
```

据此查Setup。

---

## 第四步：有界邻域搜索

在计算预算内尝试：

- swap；
- insertion；
- block move；
- 等价局部搜索。

---

## 第五步：重新验证高层约束

至少验证：

- Resource冲突；
- Routing Dependency；
- Material AvailableTime；
- Firm/Frozen/Execution；
- Demand Protection；
- 业务优先关系；
- DueDate/履约；
- Quantity-Time约束。

---

## 第六步：只接受高层不恶化且Setup更优的序列

比较：

1. 硬约束全部通过；
2. 保护需求和高优先订单不被牺牲；
3. 关键履约不恶化；
4. Setup总时间下降；
5. 同等情况下优先稳定性。

---

# 十七、白天Candidate继续局部优先

白天已经有ACTIVE计划。

因此：

- 新订单插入；
- 已有订单提前；
- 甘特拖拽；
- 设备故障；
- Calendar变化；

继续：

> **变化起点 → 真实影响传播 → 局部重排优先 → 重算受影响邻接Setup。**

不能因为Setup规则增强，就把每次白天变化自动变成全天Task全部重新排序。

如影响真实扩大到现有LOCAL全Domain兜底条件，继续使用现有Candidate规则。

---

# 十八、缺失规则数据治理

因为：

```text
明确规则不存在
+
默认规则不存在
→
Setup=0
```

所以必须统计：

- 本次Run按0分钟兜底次数；
- ProductionDepartment；
- Stage；
- OperationCode；
- Resource；
- FromMaterial；
- ToMaterial。

4号位可提供：

> 未维护Setup产品转换查询。

优先让车间补充：

- 高频转换；
- 长换型；
- 瓶颈设备；
- 稼动率影响明显的工序。

不要求上线前一次维护完整N×N矩阵。

---

# 十九、职责边界

## 19.1 3号位

负责：

- Setup规则治理；
- RuleSetVersion内容；
- 冲突校验；
- 权限；
- 审计；
- Setup算法参数治理；
- 本Run规则版本冻结；
- Setup维护API。

不负责Pegging和Solver序列计算。

---

## 19.2 4号位

### 明确产品转换维护

用户选择：

- 生产部门；
- 大工艺；
- 当前小工序；
- 前产品；
- 后产品。

系统基于OperationResourceEligibility显示当前工序下前后产品的共同合法设备。

用户：

- 选择一台或多台设备；
- 输入Setup分钟；
- 保存到当前DRAFT RuleSetVersion。

### 默认规则维护

用户选择：

- 生产部门；
- 大工艺；
- 当前小工序；
- 一台或多台设备；
- 默认Setup分钟。

同时提供：

- 冲突提示；
- 缺失规则查询；
- 0分钟兜底查询；
- 规则版本/Diff/发布状态。

---

## 19.3 2号位

负责：

- 按本Run Frozen Strategy Snapshot装载Setup规则；
- 只加载本Domain涉及规则；
- 形成内存快速查找结构；
- 传给1号位DomainSolveRequest；
- 不自己进行Setup序列优化。

运行时快速查找：

```text
Resource
+ Operation
+ FromMaterial
+ ToMaterial
→ SetupMinutes
```

以及：

```text
Resource
+ Operation
→ DefaultSetupMinutes
```

ProductionDepartment / Stage作为规则裁剪、唯一性和业务范围上下文保留。

---

## 19.4 1号位

负责：

- Setup真实资源占用；
- 初始Resource/时间槽评价时消费Setup；
- 相邻Task产品转换Setup计算；
- 夜间Resource × 生产日 × 可移动段有界序列优化；
- 白天Candidate局部Setup修复；
- 固定锚点；
- 最终Resource / Start / End；
- 无规则0分钟；
- Explanation；
- 不建设第二套Solver。

禁止：

- 读取3号位数据库；
- 自己维护Setup规则；
- 使用FromOperation/ToOperation模型；
- 使用MES/RoutingOperation.SetupTime fallback；
- 枚举全厂/90天全部排列。

---

## 19.5 5号位

无Setup核心新增职责。

不需要：

- 建Setup事实；
- 推导Setup属性；
- 维护Setup规则；
- 决定Setup时间。

---

# 二十、需要正式覆盖的旧口径

## 20.1 MES已有SetupTime

废止。

## 20.2 Setup依赖Mold/Tool/Material/Color属性

废止。

## 20.3 RoutingOperation.SetupTime进入Solver

废止。

## 20.4 前小工序 + 后小工序Setup模型

废止。

正式改为：

> **当前Task小工序 + 当前候选/实际Resource + 前产品 + 当前产品。**

## 20.5 Setup只做局部相邻修复

部分覆盖：

- 初始资源/时间槽即考虑Setup；
- 夜间FULL同Resource同生产日内做有界序列优化；
- Task插入/删除/移动/交换后必须重算受影响前后邻接Setup。白天Candidate仍局部优先。

---

# 二十一、冻结文档影响范围

## 必须修改

1. 《APS V1 最终全部流程与业务基线》
2. 《APS 有限产能排产与滚动90天计划业务说明》
3. 《APS 核心排产全流程走查》
4. 《APS 集成接口设计》
5. 《APS 数据库字段说明文档》
6. 《APS 数据库表结构设计 DDL》
7. 《APS 数据架构与防腐层设计方案》
8. 《APS V1 1号位有限产能排程开发实施包》
9. 《APS V1 2号位增量开发实施包》
10. 《APS V1 3号位规则参数认证权限与运行生命周期开发实施包》
11. 《APS V1 4号位页面与业务操作开发实施包》

### 字段/DDL修订特别注意

- 删除/退役任何“MES提供RoutingOperation.SetupTime”的说明；
- 历史物理列如暂不DROP，标记为兼容废弃，新代码禁止使用；
- 新Setup规则只保留一个 `OperationCode`；
- **不得出现 `FromOperationCode / ToOperationCode`**；
- 精确唯一键按：
  `RuleSetVersion + Department + Stage + Operation + Resource + FromMaterial + ToMaterial`
- 默认唯一键按：
  `RuleSetVersion + Department + Stage + Operation + Resource`

### Pegging

原则上不改算法。

Setup属于有限产能资源时间真相，不进入Pegging规则。

### 5号位实施包

原则上不增加Setup核心职责。

---

# 二十二、建议新增验收场景

## S01 当前工序产品转换命中

当前Task：

```text
产品B
工序OP20
Resource=MC01
```

MC01上一产品A。

规则：

```text
OP20 + MC01 + A→B = 45
```

必须命中45。

---

## S02 上一Task工序不参与Setup键

上一Task即使是OP10或OP30：

只要：

```text
上一产品=A
当前Task工序=OP20
当前Task产品=B
当前Resource=MC01
```

都应命中：

```text
OP20 + MC01 + A→B
```

不得因上一Task Operation不同产生不同Setup。

---

## S03 同设备不同当前工序

同一MC01：

```text
OP10下 A→B = 15
OP20下 A→B = 40
```

必须分别命中。

---

## S04 方向性

```text
OP20 + MC01 + A→B = 20
OP20 + MC01 + B→A = 60
```

必须不同。

---

## S05 默认规则

当前：

```text
OP20 + MC01
```

没有明确A→B，但默认=30。

必须使用30。

---

## S06 无规则

明确和默认都没有：

> Setup=0，并登记缺失解释。

---

## S07 禁止RoutingOperation.SetupTime fallback

即使历史APS物理列非0：

> 仍不得兜底。

---

## S08 同产品连续

```text
OP20 + MC01 + A→A
```

无显式规则时0分钟。

---

## S09 动态共同设备集合

A在OP20合法：

```text
MC01 MC02 MC03
```

B在OP20合法：

```text
MC02 MC03 MC04
```

建议设备集合：

```text
MC02 MC03
```

保存后落具体Resource，不形成永久组。

---

## S10 初始资源评价消费Setup

当前Task B/OP20：

```text
MC01 A→B Setup=60
MC02 A→B Setup=10
```

候选时间槽评价必须真实包含Setup。

---

## S11 FULL生产日优化

同一Resource同一生产日多个可移动Task。

在高层目标不恶化条件下：

> 优化后Setup总时间不得高于初始可行序列。

---

## S12 固定锚点

不可跨已执行/Frozen/Firm锚点重新排列。

---

## S13 跨生产日设备状态

昨天最后产品A，今天第一Task为B/OP20。

今天第一笔必须计算：

```text
OP20 + 当前Resource + A→B
```

---

## S14 Candidate局部变化

白天插入Task时优先重算真实受影响邻接，不得自动洗牌全天稳定计划。

---

## S15 规则版本可重放

旧Run重放必须继续使用当时RuleSetVersion中的Setup规则。

---


## S16 插入Task必须重算两侧Setup

原序列：

```text
A → C
```

插入B后：

```text
A → B → C
```

必须取消旧A→C Setup，并分别计算：

```text
B自身Operation下 A→B
+
C自身Operation下 B→C
```

## S17 前产品不要求能做当前Operation

A只能在MC01的OP10加工，B当前Task为OP20且MC01对B/OP20合法。

只要现场存在A作为MC01上一产品的真实可能：

> MC01仍可以维护 `当前OP20 + A→B` Setup。

不得因为“A不能做OP20”而错误排除。

## S18 无上一产品初始状态

某Resource不存在任何可追溯上一Task：

> 当前第一Task Setup=0，并输出“初始设备状态/无上一产品”的解释。


# 二十三、V1明确不做

1. Mold主数据平台；
2. Tool主数据平台；
3. Color/Material属性式Setup体系；
4. MES SetupTime补造；
5. RoutingOperation.SetupTime fallback；
6. FromOperation/ToOperation Setup模型；
7. 静态Setup ResourceGroup；
8. 全产品完整N×N矩阵强制维护；
9. 全工厂TSP；
10. 90天全部Task全排列；
11. 数学全局最优承诺；
12. 第二套Setup Solver；
13. 无界搜索；
14. Setup独立版本平台；
15. Setup为了少换型突破硬约束、业务优先和履约保护。

---

# 二十四、最终冻结口径

> **APS V1 Setup不依赖模具、刀具、颜色、材质等当前缺失的SetupAttribute，也不把MES或RoutingOperation.SetupTime作为运行真相或任何fallback。Setup的业务判断对象是“当前要排的Task”。对当前Task，使用其当前小工序、当前候选/实际设备、该设备上一相邻Task的产品和当前Task产品来确定换型时间。因此精确Setup规则按“生产部门 + 大工艺 + 当前小工序 + 当前设备 + 前产品 + 后产品”生效，默认规则按“生产部门 + 大工艺 + 当前小工序 + 当前设备”生效；不存在前小工序/后小工序两个维度。明确规则优先于默认规则，均无规则时按0分钟并登记缺失解释。OperationResourceEligibility继续作为设备资格真相和维护页面共同合法设备集合的数据基础，Setup规则不得反向改变资格，也不得恢复静态ResourceGroup。Setup规则纳入现有RuleSetVersion治理，算法参数纳入ParameterSetVersion，不建设第二套版本平台。Setup真实占用资源，并从初始Resource/时间槽评价阶段参与求解。夜间FULL对“单一Resource × 同一生产日 × 固定锚点之间的可移动段”执行有计算预算的启发式序列优化，段内全部可移动Task均可成为候选，但不枚举全部排列、不承诺数学全局最优；生产日只是搜索边界，不重置设备上一产品状态；若完全无上一产品状态则按初始设备状态Setup=0并解释。Task插入/删除/移动/交换后必须重算受影响前后邻接Setup。白天Candidate仍局部优先。Setup始终属于履约之后的次级优化。**

---

**结束**
