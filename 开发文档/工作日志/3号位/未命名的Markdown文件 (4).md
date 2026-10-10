# 0号位关于 TaskSplitRuleConfig dev库DDL升级的正式裁决

## 一、结论

本事项属于**已有冻结职责边界下的数据库落地执行问题**，不是新的业务裁决。

正式确认：

> **APS V1继续执行“数据库结构按业务Owner负责”的职责模式，不恢复“所有APS数据库DDL统一由2号位执行”的旧模式。**

本次 `TaskSplitRuleConfig` 增量字段属于 **Batch Policy治理结构**，Batch Policy治理Owner为3号位。

因此：

> **本次TaskSplitRuleConfig DDL由3号位负责。**

考虑到该表位于共享APS业务库，且今天需要解除4号位真实联调阻塞，现由0号位明确授权：

> **3号位可以在dev环境执行当前正式DDL中已经定义好的TaskSplitRuleConfig幂等Schema升级脚本。**

本次授权仅用于执行已经确定的正式结构，不授权3号位自行扩展新的字段、表、索引语义或业务模型。

---

# 二、为什么不是等待2号位

原有项目中确实长期采用过：

```text
APS数据库Schema变更
→ 2号位统一执行
```

但2026-09-01正式职责裁决已经调整该模式：

> **数据库结构由业务Owner负责；涉及其它Owner核心业务表时，再进行跨Owner评审。**

并明确废止：

> “所有数据库DDL都必须由2号位执行”的泛化理解。

因此：

```text
2号位今天不在
```

本身不再构成：

```text
其它业务Owner的DDL必须停工
```

的理由。

---

# 三、本次TaskSplitRuleConfig属于谁

当前Batch Policy正式业务粒度为：

```text
Material
+
ProductionDepartment
```

由3号位负责治理。

`TaskSplitRuleConfig`当前增量承载：

- `ProductionDepartmentId`
- `MinExecutionBatchQty`
- `MaxExecutionBatchQty`
- `PreferredBatchQty`
- `AllowSplit`
- `AllowMerge`
- `MaxOptimizationSplitCount`
- `MaxBatchCandidates`

这些均属于已经确定的Batch Policy治理结构。

当前正式DDL也已经存在对应物理结构，并已经定义：

```text
IX_TaskSplitRuleConfig_Query
(MaterialId, ProductionDepartmentId, IsActive)
```

本次不是3号位因为代码报错临时要求加字段，而是dev数据库Schema没有跟上既有正式DDL。

因此职责链是：

```text
3号位
Batch Policy业务/治理Owner
        ↓
负责TaskSplitRuleConfig对应治理结构
        ↓
DDL / Entity / Service / CRUD对齐
        ↓
2号位Run时读取冻结后的策略
        ↓
1号位Solver消费
```

2号位是运行消费方，不因为消费该表就自动成为该表DDL Owner。

---

# 四、今天dev环境怎么处理

3号位报告的问题已经确认：

4号位调用：

```text
GET /api/governance/task-split-rule-configs
```

返回500。

原因是dev库缺少：

- `ProductionDepartmentId`
- `MinExecutionBatchQty`
- `MaxExecutionBatchQty`
- `PreferredBatchQty`
- `AllowSplit`

而当前正式DDL已经存在对应的幂等升级脚本。

因此本次执行：

> **dev立即升级。**

3号位只允许执行当前正式DDL中已经存在的：

```text
IF COL_LENGTH(...) IS NULL
    ALTER TABLE ...
```

对应最小升级内容，并核对：

```text
IX_TaskSplitRuleConfig_Query
```

是否已经与当前正式DDL一致。

---

# 五、本次授权边界

本次授权3号位执行dev库DDL，不代表：

> 3号位可以任意修改APS核心数据库。

正式原则仍然是：

> **业务Owner负责自己的数据库结构，但共享数据库的物理修改仍必须遵守环境权限和变更控制。**

因此分三层理解。

### 第一层：谁决定结构

由业务Owner决定。

例如：

```text
Batch Policy
→ 3号位

Pegging / Allocation / PlanVersion等运行结构
→ 2号位

ODS / 复杂业务事实
→ 5号位

APS_Auth
→ 3号位
```

### 第二层：谁编写和维护DDL

原则上仍由对应业务Owner负责其自属结构。

### 第三层：谁实际在某个环境执行

根据：

- 数据库账号权限；
- 环境管理规则；
- 发布流程；

执行。

必要时由0号位或项目数据库管理规则给予执行授权。

因此：

> **业务Owner ≠ 可以无审批任意改共享数据库。**

但也：

> **没有必要再把所有DDL统一转交2号位。**

---

# 六、本次是否需要2号位参与

## dev升级

**不需要等待2号位。**

3号位按本裁决执行即可。

## 后续运行

2号位仍然需要按正常职责：

- 装载本Run冻结的Batch Policy；
- 将对应策略交给1号位Solver。

这不因为本次DDL由3号位执行而改变。

## 不要求2号位

本次不要求2号位：

- 代写Batch Policy DDL；
- 代执行dev升级；
- 接管TaskSplitRuleConfig治理；
- 新增接口；
- 改Run主链。

---

# 七、test和prod如何处理

本次环境策略：

### dev

> **立即执行。**

目的是解除当前4号位真实联调阻塞。

### test

> **随本轮测试部署同步执行同一正式升级脚本。**

不得让dev与test长期处于不同Schema。

### prod

> **继续按照正式发布窗口和生产变更流程执行。**

本次dev联调阻塞不构成提前直接修改生产库的理由。

---

# 八、对3号位原三项问题逐项回复

| 3号位问题 | 0号位裁决 |
|---|---|
| dev库今天是否执行DDL | **是，立即执行当前正式DDL中的幂等升级脚本** |
| 2号位不在，由谁执行 | **3号位作为Batch Policy业务Owner执行；本次明确授权dev环境落地** |
| 是否恢复2号位统一执行 | **否，不恢复** |
| test是否同步 | **随本轮测试部署同步** |
| prod是否今天同步 | **否，按正式生产发布节奏执行** |
| 是否允许顺带加字段/改模型 | **不允许** |
| 是否改变2号位运行职责 | **不改变** |

---

# 九、对“DDL红线#6”的纠正

3号位原申请中写：

> `DDL = 红线 #6 = 2号位专属`

该表述已经不符合当前正式数据库Owner原则。

应修正为：

> **数据库结构按业务Owner负责；对于共享APS数据库，实际Schema执行应服从环境权限和变更控制，必要时取得明确授权。涉及其它业务Owner核心结构时必须进行跨Owner评审。**

因此今后不得再机械采用：

```text
只要是DDL
→ 一律找2号位
```

而应先判断：

```text
这个结构的业务Owner是谁？
```

再决定DDL责任归属。

---

# 十、是否需要修改冻结文档

## 业务基线

**不需要正式业务变更。**

因为“数据库结构按业务Owner负责”已经是现有正式职责裁决。

## 3号位内部红线/开发说明

如果仍存在：

> “所有DB Schema变更均由2号位专属执行”

则属于历史规则残留，应做**一致性勘误**。

建议统一修正成：

> **数据库结构由业务Owner负责；共享数据库实际执行受环境权限与变更控制约束，必要时取得授权；跨Owner核心结构修改必须走跨号位评审。**

这只是职责口径收口，不改变Batch Policy业务。

---

# 十一、最终裁决

> **APS V1继续采用数据库结构业务Owner制，不恢复2号位统一执行所有DDL的旧模式。TaskSplitRuleConfig本轮Batch Policy增量结构归3号位负责；当前dev库Schema落后于正式DDL并已阻塞联调，现授权3号位直接执行既有正式幂等升级脚本完成dev对齐。test随测试部署同步，prod按正式发布流程执行。业务Owner制不等于可以无控制修改共享数据库，环境执行仍须遵守权限和变更管理；涉及其它Owner核心结构时必须进行跨Owner评审。**