# APS 1号位有限产能Solver实施包 v1.1
## 20260902 双专项边界确认版

> 本版本为双专项冻结后的边界确认版，不改变1号位有限产能Solver设计。

---

# 一、本次修订范围

仅补充：

1. Domain Resolve与Solver边界；
2. Supply / Position与Solver边界。

不新增：

- Solver输入模型；
- 新约束算法；
- Supply判断逻辑。

---

# 二、1号位职责

1号位负责：

- 有限产能求解；
- Resource Constraint处理；
- Task生成。

1号位不负责：

- Domain Resolve；
- Supply解析；
- ProductionInstruction Position计算。

---

# 三、Solver输入边界

Solver消费：

Pegging后的排程输入。

包括：

- 已确定需求承接关系；
- 工序任务；
- 资源约束。

Solver不直接消费：

- DomainDefinition；
- Position计算规则；
- ERP/MES事实。

---

# 四、冻结说明

本版本属于职责边界确认。

保持：

- 有限产能模型；
- 多目标优化；
- Task生成流程。
