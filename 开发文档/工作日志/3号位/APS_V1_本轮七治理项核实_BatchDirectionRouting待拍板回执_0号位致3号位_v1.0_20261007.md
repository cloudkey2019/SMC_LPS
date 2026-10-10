# APS V1 0号位对3号位 Batch / Direction / Routing职责边界问题正式裁决

**适用对象**：3号位  
**裁决范围**：Batch Policy、Direction、V1多Routing及3号位Routing职责边界  
**裁决性质**：现有冻结业务执行确认 + 3号位实施包Routing职责越界最小勘误  
**特别说明**：本裁决不重新打开V1多Routing业务，不新增Routing业务能力、DDL、字段、接口或第二套Routing治理平台。

---

# 一、总体裁决

3号位本次提出的三个事项应分别处理：

1. **Batch Policy：当前V1正式能力，本轮应按冻结基线实现；**
2. **Direction：当前V1正式能力，本轮应按冻结基线实现；**
3. **Routing：3号位v1.7中“Routing治理”表述越界，应做冻结后正式文档一致性最小勘误；3号位不建设Routing治理能力。**

当前有效职责清单已经明确：

- 1号位负责有限产能求解，包括Direction、C桶Batch及Routing选择；
- 2号位负责运行主流程及Solver调用；
- 3号位负责Rule / Parameter / Strategy、Batch Policy、Direction治理、StageLeadTimeParam和Run/Candidate治理；
- 5号位负责ODS、MES事实及Routing回接。

当前正式职责中并没有授予3号位“Routing治理Owner”职责。

因此，3号位实施包中的“Routing治理”必须向上位冻结职责收敛，而不是反过来要求其它号位为该句补业务能力。

---

# 二、Batch Policy裁决

## 2.1 是否本轮实施

**是。**

Batch Policy已经属于当前正式V1能力，不再维持历史阶段“本轮不落码”的口径。

当前3号位职责正式包括Batch Policy治理。

当前业务粒度为：

```text
Material
+
ProductionDepartment
```

治理内容包括：

- 最小批量（Min）；
- 最大批量（Max）；
- 偏好批量（Preferred）；
- 是否允许拆分（AllowSplit）；
- 是否允许合并（AllowMerge）；
- 优化拆分搜索预算。

职责边界：

```text
3号位
= 参数与策略治理

1号位
= 最终Batch Decision / Solver计算
```

3号位不得执行拆批、合批或Execution Batch求解。

---

# 三、Direction裁决

## 3.1 是否本轮实施

**是。**

当前正式Direction支持：

```text
AUTO
FORWARD
BACKWARD
MIXED
```

不得继续按OrderType硬编码正排/倒排。

职责关系为：

```text
3号位
= Direction策略与参数治理

1号位
= 根据当前求解上下文决定最终Direction
```

当前1号位正式职责已经包含Direction求解。

因此3号位只治理策略，不为每张订单直接计算最终Direction。

---

# 四、Routing问题首先纠正一个概念

3号位v1.7当前存在以下表述：

> “Routing治理：支持真实RouteCode+PathId的启用/停用、优先级/偏好等静态治理；3号位不执行动态路径求解。”

该句已经超出上位正式基线授权的3号位职责范围，应作为**冻结后正式文档一致性最小勘误**处理。

3号位v1.7确实把它列入了“本轮新增治理项”。

但上位当前有效职责并未把Routing治理分配给3号位。

因此：

> **该句不得继续作为当前3号位开发依据。**

---

# 五、当前V1多Routing真正的业务模型

当前V1已经正式支持：

```text
一个业务对象
→ 可能存在多个合法RouteCode + PathId
```

不是过去：

```text
RouteCode = DEFAULT
PathId = 1
```

的单一路径假设。

最新端到端流程明确：

```text
2号位识别A/B/C
→ A/B固定真实Path
→ C提供多Routing候选
→ 1号位在同一Solver中完成Batch + Direction + Routing + Resource
→ FinalTask
→ Task持久化真实Route/Path
→ MES执行
→ 下一Run回接真实Route/Path
```



同时，当前正式红线已经明确：

> 凡属于业务真值、持久化、接口追溯、FinalTask、Task、MES执行身份的RouteCode / PathId必须保存真实值；`DEFAULT / 1`不得继续作为归一化后的业务真值。

---

# 六、“真实RouteCode / PathId多Routing投影整改”到底是什么意思

这里不用理解成新增一种Routing业务。

用业务语言说，就是：

> **既然系统已经承认现场真实存在多条工艺路线，那么APS任何看到、传递、计算、保存Routing的地方，都必须知道它到底是哪一条真实路线，不能再全部伪装成DEFAULT / Path1。**

例如同一个产品有：

```text
Route R01 / Path 1
Route R02 / Path 1
Route R02 / Path 2
```

系统就必须能够分别识别这三条业务路线。

所谓“整改”，主要是以下业务结果：

### 1. 查询时不能把真实多Routing抹掉

3号位涉及Routing相关查询或投影时：

不能显示：

```text
DEFAULT / 1
DEFAULT / 1
DEFAULT / 1
```

而应该保持：

```text
R01 / 1
R02 / 1
R02 / 2
```

---

### 2. 不同Path的工艺内容不能串起来

例如：

```text
Path 1：
OP10 → OP20 → OP30

Path 2：
OP10 → OP25 → OP30
```

必须作为两条完整路线存在。

不能因为都有OP10或OP30，就把Path1的工序、Dependency、Eligibility与Path2混在一起。

当前正式规则已经明确：Route/Path必须真实保存，同名Operation跨Path也必须隔离。

---

### 3. C桶必须把所有合法完整Routing作为候选

对于没有既有MES执行路径的C桶：

```text
2号位
→ 提供全部合法RoutingCandidates[]

1号位
→ 为Execution Batch选择其中一条完整Path
```

而不是2号位预先人为压成DEFAULT Path。

核心走查已经明确允许两个C Execution Batch分别选择不同真实Routing。

---

### 4. A/B已经执行的Routing必须保持

如果一个执行批已经在：

```text
Route R02 / Path 2
```

上实际生产，

下一次排程不能因为系统内部还有R01，就重新把它切回R01。

A/B继续固定历史真实Route/Path。

---

### 5. 最终结果必须知道“到底走了哪条路线”

FinalTask、Task、MES工单以及下一Run执行回接必须保存实际选择结果。

不能Solver内部选了R02/P2，最后数据库又写：

```text
DEFAULT / 1
```

否则下一次运行就无法恢复真实连续性。

因此：

> **“多Routing投影整改”实际上是多Routing身份不丢失整改，不是Routing治理能力建设。**

---

# 七、当前V1有没有“默认Routing”？

## 裁决：没有。

当前冻结业务没有定义：

```text
DefaultRoute
DefaultPath
```

也没有定义：

```text
PathId = 1
= 默认路线
```

尤其不得因为ODS中某些常用工艺恰好ETL为PathId=1，就推导：

> PathId=1优先。

本次明确：

> **PathId只是Path身份，不是业务优先级。**

因此：

```text
PathId=1
```

不能解释为：

- 默认路线；
- 主路线；
- 首选路线；
- 优先路线。

本次不增加任何“Path=1优选”规则。

---

# 八、当前V1有没有Route / Path优先级？

## 裁决：没有正式冻结。

当前V1没有正式冻结：

```text
RoutePriority
PathPriority
Priority=1/2/3
```

也没有：

```text
主Routing
备用Routing
```

这种静态路线等级模型。

因此3号位不得新增：

- PathPriority；
- RoutePriority；
- DefaultRoute；
- DefaultPath。

---

# 九、当前V1有没有Preferred Routing（首选工艺路线）？

## 裁决：当前没有。

需要特别区分：

### 当前已经有：

```text
PreferredResourceCode
```

这是**资源连续性软偏好**。

例如：

> 上一轮已经在MC01生产，在MC01仍然合法且可用的情况下，可以尽量继续使用MC01。

但它不是：

```text
PreferredRouting
```

而且PreferredResource不得突破Eligibility、Calendar、Firm/Frozen、Material、Routing等硬约束。

---

## 当前没有：

```text
PreferredRouteCode
PreferredPathId
IsPreferredPath
RoutePreference
PathPreference
```

因此目前的业务模型不是：

```text
R01优先
R02备用
```

而是：

```text
R01合法
R02合法
R03合法

→ 作为RoutingCandidates[]
→ 由1号位在当前Solver中进行动态Routing选择
```

---

# 十、为什么“合法Routing”与“Preferred Routing”不是一回事

这是需要3号位特别理解的地方。

## 合法Routing

回答：

> 这条工艺路线能不能生产这个产品？

属于Routing事实。

例如：

```text
R01：合法
R02：合法
```

两条都能生产。

---

## Preferred Routing

如果未来建设，它回答的是：

> 两条都能生产，但正常情况下业务更希望走哪一条？

例如：

```text
R01
= 成熟常规工艺
= 正常首选

R02
= 也能生产
= 但成本较高或仅用于产能紧张时
```

这是一种：

> **软业务偏好。**

不是合法性判断。

---

# 十一、为什么当前不直接增加Preferred Routing

现场未来确实可能存在这种需求。

例如：

- 某条Routing生产成本更低；
- 某条Routing质量稳定性更好；
- 某条Routing属于正常工艺；
- 另一条Routing主要用于瓶颈绕行；
- 某条Path虽然合法，但通常不希望首选。

这种场景下，“Preferred Routing”可能有业务价值。

但这与本次3号位职责勘误是两件事。

当前冻结模型没有定义：

- 谁维护这个偏好；
- 偏好粒度是Route还是Path；
- 是Material级还是Material+Factory/Department级；
- 偏好是否分等级；
- 偏好进入1号位目标函数的强度；
- 什么条件允许Solver为了交期放弃首选路线；
- 是否需要人工指定；
- ODS是否已有可靠事实来源。

因此本轮不得借3号位实施包勘误顺手增加。

### 本轮裁决

> **Preferred Routing当前不属于V1正式能力。**

如果后续现场确认：

> “多条Route虽然都合法，但确实存在稳定的常规首选/次选业务关系。”

则应作为一个独立业务变化重新裁决。

届时即使增加，也应首先定义为：

> **软偏好，不是Hard Lock。**

也就是说，若未来建设：

```text
Preferred Route = R01
```

其业务含义应类似：

> 条件相当时优先R01，但当R01无法满足资源、产能、交期等正式约束时，Solver仍可以选择其它合法Routing。

不能把“Preferred”理解成：

> 永远只能走R01。

但这是未来可能的设计方向，**不是当前冻结规则**。

---

# 十二、3号位Routing职责最终边界

本次正式纠正为：

> **3号位不拥有Routing事实，不负责RouteCode / PathId的启用/停用、路径优先级或路径偏好治理，不建设独立Routing治理/选择平台，也不执行动态路径求解。**

现有正式运行链继续保持：

```text
正式ODS / Routing事实
        ↓
5号位提供/回接真实Routing事实
        ↓
2号位装载合法RoutingCandidates[]
        ↓
1号位完成动态Routing选择
        ↓
FinalTask保存真实RouteCode + PathId
```

不得建设第二套Routing选择平台。

---

# 十三、3号位v1.7正式勘误要求

原文：

> `Routing治理：支持真实RouteCode+PathId的启用/停用、优先级/偏好等静态治理；3号位不执行动态路径求解。`

整句退出当前实施依据。

建议替换为：

> **Routing职责边界：3号位不拥有Routing事实，不负责RouteCode/PathId的启用/停用、路径优先级或路径偏好治理，不建设独立Routing治理/选择平台，也不执行动态路径求解。合法Routing候选以当前正式ODS/Routing事实契约为准，由2号位按正式运行链装载并向1号位提供RoutingCandidates[]，由1号位按已冻结多Routing规则完成动态路径选择。**

本修改属于：

> **冻结后正式文档一致性最小勘误。**

不是新增业务变化。

---

# 十四、文档处理

仅修改：

> 《APS V1_3号位规则参数认证权限与运行生命周期开发实施包 v1.7》

生成新的正式未占用版本，建议：

> `v1.8`

建议文件名：

> `APS_V1_3号位规则参数认证权限与运行生命周期开发实施包_v1.8_20261007_Batch_Direction_StageLeadTime_Routing职责边界勘误版.md`

其它正式正文：

- 最终业务基线；
- 有限产能业务说明；
- Pegging说明；
- 核心走查；
- 数据架构；
- 集成接口；
- 字段说明；
- DDL；
- 0/1/2/4/5号位实施包；

**均不因本事项修改。**

不得要求2号位新增：

- Routing治理表；
- Route Enable/Disable字段；
- PathPriority；
- PreferredRouting；
- 新Repository；
- 新Service；
- 新API。

---

# 十五、控制层处理

3号位新实施包正式替换v1.7后：

1. 当前有效冻结规则清单仅更新3号位正式来源版本引用；
2. 文档依赖关系矩阵同步更新引用；
3. 废止/覆盖规则清单原则上不新增业务废止项；
4. 冻结基线索引最后重新生成，并将3号位v1.7替换为新的正式版本。

当前有效规则清单本身对3号位职责的正文无需改变，因为其现有R-004本来就没有把Routing治理列入3号位职责。

---

# 十六、给3号位的最终执行回执

| 事项 | 正式裁决 |
|---|---|
| Batch Policy | 当前V1正式能力，本轮实施；3号位治理，1号位计算 |
| Direction | 当前V1正式能力，本轮实施；3号位治理，1号位最终求解 |
| V1多Routing | 保持现有正式主链，不重新裁决 |
| 3号位Routing治理 | **无此职责，v1.7越界表述删除** |
| Route/Path启用停用治理 | 本轮不新增 |
| Route/Path优先级 | 当前无正式规则，不新增 |
| 默认Route / 默认Path | 当前无正式规则 |
| PathId=1优先 | **禁止推导，不新增** |
| Preferred Routing | 当前无正式能力；如未来有真实业务需要，独立立项 |
| PreferredResource | 继续保持现有资源连续性软偏好，与Routing偏好不是一回事 |
| 真实RouteCode/PathId | 必须全链保持，不得继续DEFAULT/1假真相 |
| 2号位Routing治理DDL | **不启动** |
| 第二Routing治理/选择平台 | **禁止建设** |

---

# 十七、最终一句话裁决

> **APS V1多Routing当前采用“真实合法Routing候选 + 1号位动态路径选择”模型，不存在默认Route、Path=1优先、Route/Path静态优先级或Preferred Routing正式规则。3号位不承担Routing治理职责，其v1.7中“Routing启停、优先级/偏好静态治理”属于实施包越界表述，应做冻结后最小勘误删除。真实RouteCode/PathId必须在查询、候选、Solver结果、Task/MES身份及后续执行回接中完整保留；这属于多Routing真实身份闭环，不等于建设Routing治理平台。**