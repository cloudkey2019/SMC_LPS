# APS V1 3号位代码正式复审报告（8802d56 Commit冻结版）

## 一、审核结论

审核对象：
- 仓库：https://github.com/cloudkey2019/SMC_LPS
- Commit：8802d56588adedaa7938de76ec4aed83911a49d1

总体结论：

> 3号位自身整改基本通过，进入跨号位联调阶段。

本轮已关闭：
- ContentSnapshotJson生命周期问题；
- CRUD绕过发布状态机问题；
- Publish校验链问题；
- 历史Snapshot重放问题；
- Diff读取旧字段问题。

尚未完成最终验收，需要：
1. 2号位数据库迁移；
2. 2号位SchedulingOrchestrator接口依赖整改；
3. 真实集成测试回归。

---

# 二、已关闭问题

## 1. ContentSnapshotJson生命周期

上一轮问题：
DRAFT内容无法可靠持久化。

本轮：
- RuleSetVersion使用ContentSnapshotJson；
- ParameterSetVersion使用ContentSnapshotJson；
- Publish阶段基于Snapshot校验和发布。

结论：
已关闭。

---

## 2. CRUD状态机

上一轮问题：
Create/Update可能绕过Publish。

本轮：
- Create强制DRAFT；
- Update经过Service状态检查；
- Published版本禁止原地修改；
- Publish成为正式发布入口。

结论：
已关闭。

---

## 3. 历史版本重放

上一轮问题：
历史版本可能因当前有效期检查无法恢复。

本轮：
区分：
- 新Run版本合法性检查；
- 历史Snapshot不可变内容读取。

结论：
基本关闭。

---

## 4. Diff迁移

上一轮：
Diff依赖旧主题JSON。

本轮：
Diff改为基于ContentSnapshotJson比较。

结论：
已关闭。

---

# 三、R1裁决：SchedulingOrchestrator DI

## 裁决

采用方案2：

> SchedulingOrchestrator依赖IFiniteCapacityScheduler。

责任：

> 2号位修改。

原因：

正确架构：

SchedulingOrchestrator
→ IFiniteCapacityScheduler
→ FiniteCapacitySolver

不应：

SchedulingOrchestrator
→ FiniteCapacitySolver

方案1虽然可以快速补注册，但会破坏依赖倒置，使流程层绑定具体Solver实现。

---

# 四、R2裁决：方案A验收口径

## 裁决

采用口径A（调整版）。

即：

3号位代码整改完成，但不提前宣布最终验收通过。

最终验收条件：

1. 2号位完成ContentSnapshotJson字段迁移；
2. 执行真实数据库集成测试；
3. 完成：
Create → Reload → Update → Validate → Publish → Reload → Snapshot → 历史重放。

---

# 五、剩余问题

## P1：仓库二进制文件污染

Commit包含大量：
- bin
- obj
- dll
- pdb

建议增加.gitignore：

bin/
obj/
*.dll
*.pdb

不影响功能验收。

---

# 六、跨号位责任

|事项|责任|
|-|-|
|ContentSnapshotJson数据库迁移|2号位/数据库|
|SchedulingOrchestrator依赖接口整改|2号位|
|真实集成测试执行|3号位配合|
|最终验收|0号位|

---

# 七、最终状态

3号位：
- 代码整改：完成
- 最终验收：等待跨号位联调

下一阶段重点：
- 2号位数据库迁移；
- 2号位接口调整；
- 1/2/3号位真实集成验证。
