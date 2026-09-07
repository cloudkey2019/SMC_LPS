# APS V1 PI Position执行起点上下文专项——最终一致性审核报告

**版本**：v1.0  
**日期**：2026-09-06（北京时间）  
**审核对象**：本专项9份正式替换文档。  
**结论**：**通过，可作为当前专项冻结基线进入项目来源并用于后续代码审核。**

## 一、本轮发现并关闭的问题

本轮最终模拟读取发现5份早期生成文件虽然正文已包含专项增量，但顶部“当前版本/日期/上位基线”仍残留母版旧值；另外接口v1.29历史增量中保留了“2号位自行形成APS Position解释”的旧职责表述，若不加当前版本覆盖声明，容易与新职责链产生歧义。

本轮已关闭：

1. 业务总基线顶部版本治理；
2. Pegging业务说明顶部版本治理；
3. 有限产能业务说明顶部版本治理；
4. 核心全流程走查顶部版本与权威关系治理；
5. 集成接口设计顶部版本治理；
6. 集成接口v1.30明确覆盖v1.29旧PI Position Owner表述；
7. Pegging与核心走查明确5号位负责PI Position + NextOperationContext业务计算；
8. 最终替换清单补入0号位验收包与1号位v1.2正式版。

## 二、专项一致性检查

| 检查项 | 结果 |
|---|---|
| ProductionInstruction继续作为Supply | 通过 |
| NextOperationContext进入正式链路 | 通过 |
| 5号位PI Position/NextOperationContext职责已明确 | 通过 |
| 2号位不重新解析MES | 通过 |
| 1号位从StartStage/StartOperation继续 | 通过 |
| Operation级Pegging被禁止 | 通过 |
| 未新增DDL要求 | 需复核 |

## 三、最终职责链

```text
MES执行事实
+
Routing
+
PI Position
        ↓
5号位
PI Position业务计算
+
NextOperationContext
        ↓
2号位
Demand ↔ ProductionInstruction Supply Pegging
+
Allocation / 数量闭合
+
LogicalProductionDemand
        ↓
1号位
从StartStage / StartOperation继续Routing
+
90天有限产能Solver
        ↓
FinalTask
```

## 四、不得重新打开项

- ProductionInstruction仍是PI Supply；
- PI Position不变成Supply；
- NextOperationContext不变成Supply/Allocation/新Position身份；
- 不做Demand↔PI↔Operation三级Pegging；
- 5号位不做Demand分配、Resource选择和排程时间；
- 2号位不重新解析MES判断下一工序；
- 1号位不读取MES；
- 已完成Operation不得重复排产；
- 不因本专项新建Operation Position平台；
- 不因本专项新增第二套DDL或独立NextOperationContext生命周期平台；
- Domain、90天统一Solver、Candidate现有冻结边界不变。

## 五、数据库结论

当前文档链已经可以通过DTO / ScheduleContext / LogicalProductionDemand / DomainSolveRequest表达执行起点。

因此本轮**不修改数据库字段说明和DDL**。  
只有代码实施证明现有运行上下文无法表达必要字段且确实需要持久化时，才重新打开数据库专项；不得先建表再反向修改业务。

## 六、后续代码审核基线

后续审核1号位、2号位、5号位代码时，应同时检查：

- 已实现内容是否符合本专项；
- 应实现但缺失的接口/链路；
- 是否存在历史旧Owner逻辑仍在正式主链；
- 是否出现Operation级Pegging/Position/Supply回流；
- 是否存在已完成工序重复排产；
- 是否存在1/2号位自行重新解释MES；
- 是否完整保留原冻结Domain、数量、时间、事务和MES资格闭环。

**最终结论：本专项文档层可以冻结收口。**
