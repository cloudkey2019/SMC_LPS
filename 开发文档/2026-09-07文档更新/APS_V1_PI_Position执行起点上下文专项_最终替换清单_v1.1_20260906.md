# APS V1 PI Position执行起点上下文专项——最终替换清单

**版本**：v1.1  
**日期**：2026-09-06（北京时间）  
**用途**：项目来源正式替换、代码审核与最终验收基线登记。

## 一、正式替换文件

| 类别 | 当前正式文件 | 大小 |
|---|---|---:|
| 最高业务基线 | `APS_V1_最终全部流程与业务基线_v1.4_20260904_PI_Position执行起点上下文冻结对齐版_最终治理校正版.md` | 71.9 KB |
| Pegging业务说明 | `APS_Pegging供需承接与分层计算业务说明_v1.3_20260904_PI_Position执行起点上下文冻结对齐版_最终治理校正版.md` | 32.3 KB |
| 有限产能业务说明 | `APS_有限产能排产与滚动90天计划业务说明_v1.3_20260904_PI_Position执行起点上下文冻结对齐版_最终治理校正版.md` | 34.7 KB |
| 核心全流程走查 | `APS_核心排产全流程走查_V3.20_20260904_PI_Position执行起点上下文冻结对齐版_最终治理校正版.md` | 55.8 KB |
| 集成接口设计 | `APS_集成接口设计_v1.30_20260904_PI_Position执行起点上下文冻结对齐版_最终治理校正版.md` | 25.3 KB |
| 2号位实施包 | `APS_V1_2号位增量开发实施包_v1.5_20260906_PI_Position执行起点上下文冻结对齐版.md` | 45.5 KB |
| 5号位实施包 | `APS_V1_5号位复杂业务事实ODS与业务接口接入开发实施包_v1.3_20260906_PI_Position执行起点上下文冻结对齐版.md` | 47.0 KB |
| 1号位实施包 | `APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md` | 26.3 KB |
| 0号位总体验收包 | `APS_V1_0号位总体项目验收包_v1.2_20260906_PI_Position执行起点上下文专项冻结收口版.md` | 27.2 KB |

## 二、本专项最终冻结链

```text
MES执行事实 + Routing + PI Position
        ↓
5号位：PI Position + NextOperationContext
        ↓
2号位：Demand ↔ ProductionInstruction Supply Pegging
        + LogicalProductionDemand
        ↓
1号位：从StartStage / StartOperation继续90天有限产能求解
        ↓
FinalTask
```

冻结：

- ProductionInstruction仍是Supply；
- PI Position不是Supply；
- NextOperationContext不是Supply、Allocation或新的Position身份；
- 不做Operation级Pegging；
- 5号位负责PI Position与NextOperationContext业务事实计算；
- 2号位负责Pegging、数量闭合、LogicalProductionDemand和必要运行快照持久化；
- 1号位不读取MES，已完成前序Operation不得重复排产；
- 本专项不新增DDL或NextOperationContext独立生命周期平台。

## 三、本轮无需因该专项升版

- Domain定义与分域运行边界裁决；
- 数据库字段说明；
- 数据库DDL；
- 3号位实施包；
- 4号位实施包。

上述文件继续沿用当前冻结版本；只有后续发现真实字段/持久化缺口，才重新打开数据库专项。
