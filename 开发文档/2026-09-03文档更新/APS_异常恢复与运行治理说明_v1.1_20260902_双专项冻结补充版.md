# APS异常恢复与运行治理说明 v1.1
## 20260902 双专项冻结补充版

> 本版本补充双专项运行异常治理要求。

---

# 一、Domain异常治理

## 场景

DomainDefinition无法匹配。

处理：

- 当前ScheduleRun失败；
- 不生成不确定计划。

---

## 配置漂移

场景：

运行期间修改DomainDefinition。

处理：

当前Run继续使用冻结配置。

---

# 二、Position异常治理

异常：

- Position数量不闭合；
- Position缺失；
- UNLOCATED比例异常。

处理：

- 登记异常；
- 保留事实；
- 不由2号位自行修正。

---

# 三、Supply异常治理

禁止：

将以下对象错误转为Supply：

- PI Position；
- PI Transit；
- SH Transit；
- SH Received。

---

# 四、冻结说明

本版本保持原运行恢复机制。

仅增加专项异常规则。
