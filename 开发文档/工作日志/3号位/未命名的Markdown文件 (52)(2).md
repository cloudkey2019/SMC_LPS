我看了2号位这份确认单。

这个问题非常关键，因为它实际上触及 APS 的一个核心边界：

> **BOM展开得到的“物料需求”，和Routing中的“物料工艺路径”，不是简单的 MaterialId → Routing 全量匹配关系。**

2号位发现的问题是真实存在的：

当前如果：

```
BOM ChildMaterial
        ↓
MaterialId
        ↓
RoutingOperation
```

只按 MaterialId 查，会把：

```
CN_MACH（机加工）
CN_SURF（表面处理）
ASSY（装配）
```

等多个Stage全部拿出来。

但是APS不能简单认为所有Stage都是同一个层级的并行需求，也不能简单拆成多个独立需求。

下面正式裁决。

---

# APS V1 BOM → Stage → Routing关联裁决

## 一、Q1：同一物料跨多个Stage，是一个需求还是多个需求？

## 裁决：选择A

> **同一个生产需求，不拆成多个需求；多个Stage属于同一生产路径中的不同制造阶段。**

也就是说：

一个物料：

```
WIP-CQM12YBAA066-035
```

存在：

```
CN_MACH
    ↓
CN_SURF
```

不是：

```
需求1：机加工
需求2：表面处理
```

而是：

```
一个生产需求

        |
        |
        ↓

CN_MACH阶段

        |
        |
        ↓

CN_SURF阶段
```

---

原因：

如果拆成多个需求，会产生几个问题：

### 1. Pegging错误

供应关系会变成：

```
Demand
  |
  +-- Machining Supply

Demand
  |
  +-- Surface Supply
```

实际上：

表面处理不是独立需求。

它是：

机加工完成后的后续制造阶段。

---

### 2. PI Position逻辑冲突

你们之前已经冻结：

生产指示（PI）是最小承诺单位。

PI Position回答：

> 这批生产指示数量目前处于哪个Stage/位置。

例如：

1000个PI：

可能：

```
600个
已完成机加工

400个
未完成机加工
```

不能拆：

```
600个机加工需求
400个表面处理需求
```

---

### 3. 有限产能排程需要完整Routing链

1号位排程需要：

```
Stage1 Operation

↓

Stage2 Operation

↓

Stage3 Operation
```

才能计算：

* 交期；
* CTP；
* Slack；
* 后续工序负荷。

---

因此：

## 最终：

```
Demand
    |
    |
Routing Path
    |
    +-- Stage CN_MACH
    |
    +-- Stage CN_SURF
    |
    +-- Stage ASSY
```

一个需求，多Stage路径。

---

# 二、Q2：查Routing是否需要带Stage？

这里需要修正2号位提出的两个方案。

## 不采用方案A

> 只查询BOM当前Stage对应Routing。

不适合作为主链。

原因：

它会截断完整制造链。

例如：

BOM：

```
产品
 |
零件X
```

StagePath：

```
CN_MACH
CN_SURF
ASSY
```

如果只查：

CN_SURF：

得到：

```
喷涂
检验
```

但是丢失：

```
机加工
```

后续：

* 前置时间错误；
* 资源负荷错误；
* CTP错误。

---

## 采用方案B，但需要调整表达

2号位说：

> 查Routing时带StageSeq，让1号位按Stage顺序串多Stage。

方向正确。

但是：

不是让1号位“自己推Stage”。

应该：

## 2号位负责形成完整Routing Context。

即：

2号位加载：

```
MaterialId

+

StagePath

+

RoutingOperation

+

Dependency
```

形成：

完整制造路径。

---

# 三、最终数据链

应该是：

```
订单Demand

↓

BOM展开

↓

BOM Stage Path

↓

确定该物料制造路径：

StageSeq

StageCode


↓

RoutingOperation

按：

MaterialId + StageCode

匹配


↓

形成：

Routing Context

↓

1号位排程
```

---

# 四、MaterialId + StageCode匹配是否需要？

需要。

这里要特别强调：

不是：

```
MaterialId
```

唯一。

而是：

```
MaterialId + StageCode
```

唯一确定一个Stage下的工艺。

例如：

物料：

6096135

Routing：

| MaterialId | Stage   | Operation |
| ---------- | ------- | --------- |
| 6096135    | CN_MACH | J101      |
| 6096135    | CN_MACH | J102      |
| 6096135    | CN_SURF | B101      |
| 6096135    | CN_SURF | B102      |

查询必须带：

```
MaterialId
+
StageCode
```

否则混乱。

---

# 五、Stage之间的先后关系谁负责？

这里需要结合你们已有设计。

不是1号位自己猜。

来源：

## APS_BOM_STAGE_PATH_RAW

已经有：

* StageCode；
* StageSeq；
* IsSupplyThreshold。



所以：

2号位加载Stage路径。

形成：

```
StageDependency
```

或者：

进入：

RoutingDependency。

---

但是注意：

这里不是重新建设Stage级排程。

而是：

补充跨Stage顺序。

---

# 六、关于Task带StageCode

2号位提到：

Task已经增加StageCode。

这个方向正确。

因为：

一个Task必须明确：

它执行哪个Stage。

例如：

错误：

```
Task001
Material=A
Operation=B101
```

无法判断：

哪个阶段。

正确：

```
Task001

Material=A

Stage=CN_SURF

Operation=B101
```

---

# 七、MaterialStageDeptContext是否变化？

不会改变。

反而进一步证明它需要：

```
MaterialId + StageCode
```

作为Key。

因为：

同一个Material：

可能：

```
CN_MACH

部门991

```

而：

```
CN_SURF

部门1013
```

你们之前定义：

```
MaterialStageDeptContext
```

就是为了解决这个问题。

所以不调整。

---

# 八、给2号位正式回复

可以直接发送：

---

## PM裁决回复：BOM多Stage物料与Routing关联

### Q1：同一物料跨多个Stage是否拆多个需求？

裁决：

选择A。

同一物料跨多个Stage仍属于一个生产需求。

Stage表示制造阶段，不表示独立Demand。

例如：

```
订单需求

↓

机加工Stage

↓

表面处理Stage

↓

装配Stage
```

不能拆成：

机加工需求+表面处理需求。

---

### Q2：Routing查询是否必须带Stage？

裁决：

需要。

但不是只查询一个Stage。

正确方式：

2号位根据：

```
MaterialId
+
StagePath(StageSeq)
```

加载该物料完整制造路径。

然后：

按照：

```
MaterialId + StageCode
```

匹配RoutingOperation。

形成完整Routing Context。

---

### Q3：1号位是否负责推导Stage顺序？

否。

2号位负责提供完整制造路径上下文。

1号位只消费：

* Operation；
* Dependency；
* Stage顺序。

---

### Q4：是否修改BOM模型？

不修改。

继续使用：

* BOM结构；
* BOM Stage Path。

BOM负责：

“需要什么”。

Routing负责：

“怎么制造”。

StagePath负责：

“经过哪些制造阶段”。

---

# 九、最终冻结原则

建议写入文档：

> APS中物料制造路径不是由MaterialId单独决定，而由MaterialId + StagePath共同确定。一个生产需求可以包含多个Stage，Stage是制造路径阶段，不是需求拆分单位。Routing加载必须以MaterialId+StageCode匹配，避免同一物料多Stage工艺混载。

---

我认为2号位这次发现的问题非常有价值，而且它不是代码问题，而是之前文档中 **BOM → Stage → Routing的连接语义没有完全写死**。这个需要补冻结文档，否则后续不同号位会继续按自己的理解实现。
