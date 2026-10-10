# APS V1 1号位、3号位问题统一回复意见

版本：v1.0  
日期：2026-09-22

## 一、总体原则

本次基于重新更新后的APS V1冻结文档进行回复。

本轮不是重新打开业务设计，而是检查：

- 已冻结业务是否有技术承载；
- DDL是否缺字段/缺表；
- 接口是否需要同步；
- 代码实现是否符合冻结口径。

红线：

> 不允许因为代码未实现、DDL缺失或实现困难，自动简化、删除或改变已经冻结的业务裁决。

---

# 二、1号位问题回复

## 1. SetupSource

结论：

SetupSource需要保留，并补入Task事实模型。

原因：

Setup已经冻结为需要解释的业务事实，需要记录：

- 命中的换型规则类型；
- 为什么得到该Setup时间；
- 是否使用默认规则；
- 是否规则缺失兜底。

---

## 2. SetupSource枚举统一

代码当前存在：

- EXACT
- DEFAULT
- SAME_PRODUCT
- NONE
- INITIAL

统一调整为：

|值|含义|
|-|-|
|INITIAL_SETUP_STATE|无上一产品，初始设备状态|
|EXACT|命中明确产品转换规则|
|DEFAULT|命中默认换型规则|
|SETUP_RULE_MISSING_ZERO_FALLBACK|无规则，按0分钟兜底|

不保留：

- NONE
- SAME_PRODUCT

原因：

SAME_PRODUCT只是业务条件，不是规则来源。

---

# 三、ResourceCalendar

结论：

ResourceCalendar属于有限产能资源事实，不属于3号位规则治理。

当前业务已经确定需要Resource Calendar能力。

问题属于：

> 业务已冻结，但数据库物理承载需要补齐。

职责：

|角色|职责|
|-|-|
|1号位|消费ResourceCalendar进行求解|
|2号位|DDL、表结构、加载|
|3号位|不负责资源日历事实|
|5号位|外部资源事实输入|

Setup生产窗口：

- 使用ResourceCalendar连续可用窗口；
- 不按自然日00:00切断；
- 不新增ProductionDay字段；
- 固定任务只切割可移动段。

---

# 四、ScopeJson

采用SchemaVersion演进。

不能因为早期字段说明固定11字段，就限制Candidate扩展。

SchemaVersion=2可承载：

- BusinessTriggerType；
- PriorityMode；
- OrderTargets；
- TaskTargets；
- ChangedResourceIds。

需要同步：

- 接口设计；
- 字段说明；
- DDL。

---

# 五、3号位问题回复

## 1. OperationPlanningMode

结论：

不新增独立参数表。

OperationPlanningMode属于工艺数据，继续归RoutingOperation。

---

## 2. OperationTransitionRule / StageTransferTime

需要特别区分：

Setup不是工序转换模型。

Setup冻结模型：

当前Operation + Resource + FromMaterial + ToMaterial

决定Setup时间。

不是：

FromOperation → ToOperation。

因此：

OperationTransitionRule不能用于承载Setup。

---

如果表达：

- 工序间等待；
- 自然冷却；
- 阶段转运；

属于另外的业务事实。

当前不能因为提出表名，就认为冻结已经规定：

- OperationTransitionRule表；
- StageTransferTime表。

如果需要补充，应作为独立能力确认物理承载。

---

# 六、配品M101

后续不再单独讨论。

配品M101只是无设备小工序示例。

不形成：

- 独立业务模型；
- 独立裁决项。

后续只讨论：

- OperationPlanningMode；
- FINITE_RESOURCE；
- UNCONSTRAINED；
- WAIT_ONLY。

---

# 七、最终执行原则

处理顺序：

业务冻结  
→ 数据承载  
→ 接口  
→ 代码

已冻结内容不得重新讨论：

- Setup业务模型；
- ResourceCalendar能力需求；
- OperationPlanningMode归属；
- 有限产能职责边界。

本轮需要补齐：

- Task.SetupSource字段；
- ResourceCalendar物理承载；
- ScopeJson版本化；
- 必要接口同步。

不得：

- 因DDL缺失否定业务；
- 因代码已有实现改变冻结；
- 因示例场景扩大业务范围；
- 重新打开已裁决事项。

