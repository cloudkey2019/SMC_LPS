# APS运行手册 v1.1
## 20260902 双专项冻结补充版

> 本版本补充日常运行中Domain与Position相关操作说明。

---

# 一、排程运行流程

标准流程：

ScheduleRun启动

↓

冻结DomainDefinition配置

↓

Domain Resolve

↓

Pegging

↓

有限产能排程

↓

发布计划

---

# 二、Domain运行注意事项

运行期间：

禁止修改影响当前Run的Domain配置。

如需变更：

下一次ScheduleRun生效。

---

# 三、Position检查

运行监控：

检查：

- PI Position数量闭合；
- UNLOCATED异常；
- AvailableTime合理性。

---

# 四、Supply检查

确认：

PI Position不是Supply。

Transit状态不是Transit Supply。

---

# 五、冻结说明

本版本仅补充运行操作说明。
