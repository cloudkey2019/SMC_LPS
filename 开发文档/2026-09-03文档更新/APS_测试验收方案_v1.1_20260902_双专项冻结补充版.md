# APS测试验收方案 v1.1
## 20260902 双专项冻结补充版

> 本版本替换原测试验收方案，仅补充Domain Resolve与Supply / Position专项测试，不改变原有验收体系。

---

# 一、本次补充范围

增加：

1. Domain配置冻结测试；
2. Domain Resolve结果测试；
3. ProductionInstruction Position数量闭环测试；
4. Transit不生成Supply测试。

---

# 二、Domain专项测试

## 测试场景：配置变化冻结

步骤：

1. ScheduleRun启动；
2. 修改DomainDefinition；
3. 当前Run继续执行。

期望：

当前Run继续使用启动时冻结配置。

下一次Run使用新配置。

---

## 测试场景：DomainKey生成

验证：

Order / Demand经过Domain Resolve后生成DomainKey。

禁止：

ERP直接提供DomainKey。

---

# 三、PI Position专项测试

## 数量闭环

输入：

ERP PI RemainingQty = 100

Position：

- XC 30
- STAGE_WAITING 70

结果：

通过。

---

异常：

Position合计超过或小于PI RemainingQty。

结果：

登记异常，不允许自动修正事实。

---

# 四、Supply边界测试

验证：

PI Transit：

结果：

Position状态。

不是：

Transit Supply。

---

SH Transit：

结果：

履行状态。

不是：

Transit Supply。

---

# 五、冻结说明

本版本仅补充专项验收案例。
