# APS有限产能排程业务说明 v1.1
## 20260902 双专项边界确认版

---

# 一、本次修订范围

补充Solver与Domain、Position边界。

---

# 二、有限产能排程边界

Solver负责：

- 资源能力匹配；
- 工序排序；
- Task生成。

Solver不负责：

- Demand归域；
- Supply判断；
- Position计算。

---

# 三、输入关系

流程：

Pegging结果
↓
有限产能Solver
↓
Task

---

# 四、冻结说明

不改变V1有限产能业务。
