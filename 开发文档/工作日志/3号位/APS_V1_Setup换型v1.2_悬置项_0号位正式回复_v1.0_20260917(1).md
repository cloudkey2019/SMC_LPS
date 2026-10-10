# 0号位关于 Setup换型 v1.2 悬置项的正式回复

**版本**：v1.0  
**日期**：2026-09-17  
**对象**：3号位  
**抄送**：1号位、2号位、4号位  
**依据**：  
- 《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》  
- 2026-09-15 最新 APS V1 冻结业务/字段/接口/实施包基线  
- 已给1号位的 Setup 收口答复

---

# 一、总体结论

Setup v1.2 已经确定的业务不重新打开：

1. V1 不依赖模具、刀具、颜色、材质等 SetupAttribute；
2. 不使用 MES / RoutingOperation.SetupTime 作为运行真相；
3. Setup 按“当前 Task 小工序 + 当前设备 + 前产品 + 当前产品”命中；
4. 精确 Setup 规则归 RuleSetVersion；
5. Setup 算法技术参数归 ParameterSetVersion；
6. Setup 真实占用设备产能；
7. 夜间 FULL 在“单一 Resource × 连续生产窗口 × 固定锚点之间可移动段”做有界序列优化；
8. 白天 Candidate 仍然局部调整优先；
9. 不做全工厂、全90天、全部任务的全局 TSP；
10. Setup 缺失时按照冻结 fallback 继续排程，并保留解释/数据质量追踪。

本次只处理3号位提出的四项悬置问题，不重新设计 Setup 主业务。

---

# 二、裁决项1：Setup 搜索预算参数不由0号位冻结具体数值

3号位提出：

- 有界搜索预算；
- 最大邻域尝试次数；
- 单生产日保护阈值；

并希望0号位确定具体数字。

## 2.1 0号位不裁 500 / 50 等具体值

这类参数的中文业务含义是：

> 限制1号位在一个资源连续生产窗口内，为了减少换型最多允许做多少次搜索、交换、插入或其它邻域尝试，避免算法无界运行。

它们属于：

> **Solver内部有界搜索技术参数。**

不属于：

- 订单业务优先级；
- Demand Protection；
- Firm / Frozen；
- Setup产品转换业务规则；
- 其它需要0号位确定的制造业务规则。

因此：

> **0号位不冻结“500”“50”等具体数值。**

500、50可以作为1号位性能测试的初始实验值，但不能写成0号位冻结业务值。

## 2.2 正确职责

### 1号位

根据：

- 夜间整体性能目标；
- 实际每台资源每日Task规模；
- Setup优化收益；
- 搜索收敛情况；
- 计算时间；

提出合理默认值和允许范围。

### 3号位

负责：

- ParameterSetVersion治理；
- 参数版本；
- 发布；
- 历史追溯；
- 本Run冻结。

### 0号位

只冻结业务红线：

> **Setup必须采用有界搜索，不能无限枚举；Setup优化不得破坏夜间总体性能目标。**

## 2.3 “单生产日保护阈值”当前不新增

目前“单生产日保护阈值”没有被定义清楚到底指：

- Task数量；
- 搜索次数；
- 搜索时间；
- 邻域规模；
- Setup分钟；
- 还是其它技术量。

因此：

> **当前先从必须参数清单中删除，不造一个没有明确语义的字段。**

如果1号位实际开发和性能测试后确认确实需要该参数，再由1号位提交：

- 中文技术含义；
- 触发条件；
- 为什么已有预算参数不足；
- 默认值及测试依据；

再由3号位纳入 ParameterSetVersion。

---

# 三、裁决项2：Setup规则进入Run冻结快照，但不新增第二套版本平台

本项采用：

> **方案B的业务语义，但不强制新建一个独立“Setup版本体系”。**

## 3.1 Setup规则仍然属于 RuleSetVersion

不新增：

- SetupRuleVersion；
- Setup独立发布流程；
- Setup独立生命周期。

Setup产品转换规则继续作为：

> **RuleSetVersion中的正式规则内容。**

Setup搜索预算等技术参数继续来自：

> **ParameterSetVersion。**

## 3.2 一次Run必须一次性冻结并装载Setup规则

当前APS规则治理已经冻结：

> 一次ScheduleRun开始时绑定StrategyProfileVersion、RuleSetVersion、ParameterSetVersion；Run开始后不能因为新版本发布而漂移。

同时3号位给2号位的规则/策略数据应是：

> **一次性FrozenStrategySnapshot，而不是Solver逐Task在线查询3号位。**

因此本轮正式要求：

> **当前Run实际使用的Setup规则内容必须在本次FrozenStrategySnapshot/等价一次性冻结DTO中交付给2号位。**

2号位不得：

- Solver运行中逐Task查3号位；
- 直接依赖3号位规则库；
- 每次Setup命中重新远程取规则。

## 3.3 不强制具体DTO名称

可以实现为：

- FrozenStrategySnapshot中的 `SetupTransitionRules`；
- 或现有规则Snapshot内的等价结构。

具体类名由3号位和2号位对齐。

但业务要求必须满足：

```text
RuleSetVersion
    ↓
发布后的Setup规则内容
    ↓
本Run一次性FrozenSnapshot
    ↓
2号位按当前Domain裁剪/建立内存索引
    ↓
1号位DomainSolveRequest消费
```

因此：

> **不是只有RuleSetVersionId而运行中再去查库；也不是新建一套Setup版本平台。**

## 3.4 ParameterSetVersion仍负责算法参数

Setup搜索预算、邻域搜索次数等已经确定需要版本治理的技术参数：

```text
ParameterSetVersion
    ↓
FrozenStrategySnapshot
    ↓
1号位
```

具体数值由1号位性能标定，不由0号位拍数。

---

# 四、裁决项3：Setup中的“生产日”统一定义为 Resource Calendar 最大连续可用生产窗口

本项与已给1号位的答复完全统一。

## 4.1 不新增 ProductionDay 字段或主数据

V1不为Setup新增：

- ProductionDayId；
- Setup生产日表；
- Setup班次主数据；
- 第二套Resource Calendar。

Setup直接使用现有：

> **Resource Calendar。**

## 4.2 正式识别规则

对同一Resource：

```text
按Start排序ResourceCalendarSlot
↓
取 IsAvailable = true 的可用Slot
↓
时间连续的相邻可用Slot合并
↓
形成最大连续可用生产窗口
↓
遇到不可用时间段则结束当前窗口
```

因此：

```text
9/17 20:00 ～ 9/18 08:00
```

如果中间连续可用：

> 是同一个Setup优化窗口。

不因为跨自然日 `00:00` 强行切断。

例如：

```text
08:00～12:00 可用
12:00～13:00 不可用
13:00～17:00 可用
```

则属于两个连续生产窗口。

## 4.3 固定锚点只进一步切割可移动段

同一个连续生产窗口内部如果存在：

- 已执行Task；
- Frozen Task；
- Firm不可移动Task；
- 其它不可移动Task；

这些任务作为固定锚点。

例如：

```text
连续生产窗口
    ↓
固定A
    ↓
B C D E    ← 可移动段
    ↓
固定F
```

Setup有界序列优化只针对：

> B / C / D / E

但仍必须计算：

- 固定A → 段内第一Task；
- 段内相邻Task；
- 段内最后Task → 固定F；

的换型时间。

## 4.4 窗口边界不重置上一产品状态

这是Setup v1.2已经明确的红线：

> **连续生产窗口是搜索边界，不是设备产品状态清零点。**

例如：

昨天最后一个可追溯Task产品=A；

今天第一个Task产品=B。

今天第一笔仍然要查：

> 当前B Task自身Operation + 当前Resource + A→B。

只有确实完全没有任何可追溯上一产品时，才进入“初始设备状态”。

## 4.5 本项最终一句话

> **Setup中的生产日/生产窗口 = Resource Calendar定义的最大连续可用生产窗口；不按自然日切，不新增ProductionDay字段；固定锚点只在连续窗口内部进一步切割可移动段。**

---

# 五、裁决项4：不新增ScheduleExplanationFact ReasonCode

本项与已给1号位的答复统一，并对之前“新增Setup ReasonCode”的想法正式纠正。

当前冻结字段基线已经明确：

- `ScheduleExplanationFact` 是结构化原因事实；
- `ReasonCode` 使用当前权威冻结字典；
- `ExplainTrace` 是Task级轻量决策追踪；
- 两者共存，不允许混用。

因此：

> **Setup的正常计算过程说明不新增新的ScheduleExplanationFact ReasonCode。**

## 5.1 无上一产品：初始设备状态

业务场景：

> 当前Resource在现有可追溯计划/执行上下文里完全没有上一产品。

此时：

```text
Setup = 0
```

原因是：

> 根本不存在“前产品→当前产品”的产品转换关系。

这不是：

- 延期根因；
- 不可排根因；
- 资源异常；
- 正式业务风险原因。

所以：

> **不新增 `INITIAL_SETUP_STATE` ReasonCode。**

建议写入：

> **ExplainTrace / 等价轻量追踪。**

建议：

```text
TraceType = TIME_CALCULATION
TraceLevel = INFO
```

Message：

```text
当前资源无可追溯上一产品，按初始设备状态处理，Setup=0。
```

ContextData可包含：

```json
{
  "explanationType": "INITIAL_SETUP_STATE",
  "resourceId": "...",
  "operationCode": "...",
  "toMaterialId": "...",
  "setupMinutes": 0
}
```

其中：

> `INITIAL_SETUP_STATE`

只是ExplainTrace内部解释类型，不是ScheduleExplanationFact.ReasonCode。

## 5.2 精确产品对规则缺失，但命中默认Setup

业务场景：

```text
存在前产品A
存在当前产品B
EXACT A→B 未维护
但当前Operation + Resource有DEFAULT规则
```

则：

> 使用DEFAULT SetupMinutes。

这属于正常fallback。

建议：

```text
TraceType = TIME_CALCULATION
TraceLevel = INFO
```

记录：

> “未命中明确产品转换规则，使用当前工序/设备默认换型时间。”

不新增正式ReasonCode。

## 5.3 精确规则和默认规则都不存在

业务场景：

```text
有前产品A
有当前产品B
EXACT不存在
DEFAULT也不存在
```

按照Setup v1.2：

> **Setup=0。**

但这个场景与“初始设备状态”必须严格分开。

它代表：

> **换型规则数据尚未维护完整。**

处理：

1. 本次排程继续；
2. Setup按0分钟；
3. 写 ExplainTrace WARNING；
4. 同时进入4号位“换型规则缺失 / 0分钟兜底”数据质量查询。

建议：

```text
TraceType = TIME_CALCULATION
TraceLevel = WARNING
```

ContextData：

```json
{
  "explanationType": "SETUP_RULE_MISSING_ZERO_FALLBACK",
  "productionDepartmentId": "...",
  "stageCode": "...",
  "operationCode": "...",
  "resourceId": "...",
  "fromMaterialId": "...",
  "toMaterialId": "...",
  "setupMinutes": 0
}
```

同样：

> `SETUP_RULE_MISSING_ZERO_FALLBACK`

只是轻量追踪解释类型，不是正式ReasonCode。

---

# 六、四项最终结论汇总

| 3号位悬置项 | 0号位最终回复 |
|---|---|
| 有界搜索预算 | 不由0号位定具体数字；1号位性能测试提出，3号位ParameterSetVersion治理 |
| 最大邻域尝试次数 | 同上；500/50等只能作为测试初值 |
| 单生产日保护阈值 | 当前删除；语义未定义，不提前造字段 |
| Setup规则Snapshot承载 | Setup规则属于RuleSetVersion，但本Run规则内容必须通过FrozenStrategySnapshot/等价一次性DTO完整交给2号位，不允许Solver运行中再查3号位 |
| Setup独立版本 | 不新增 |
| Setup算法参数 | ParameterSetVersion治理 |
| Setup生产日 | Resource Calendar最大连续可用生产窗口 |
| 自然日00:00 | 不作为强制切点 |
| 固定锚点 | 在连续窗口内部继续切割可移动段 |
| 窗口边界 | 不清空上一产品状态 |
| 无上一产品 | Setup=0，写ExplainTrace，不新增ReasonCode |
| EXACT缺失但有DEFAULT | 使用默认Setup，写轻量追踪即可 |
| EXACT和DEFAULT均缺失 | Setup=0，ExplainTrace WARNING + 缺失规则数据质量查询，不新增ReasonCode |

---

# 七、给3号位的执行要求

3号位可以继续：

1. 删除旧 `Setup.Dimensions`；
2. 按Setup v1.2建设“当前工序 + 当前设备 + 前产品 + 后产品”的产品转换规则；
3. Setup规则纳入RuleSetVersion；
4. Setup算法参数纳入ParameterSetVersion；
5. 与2号位补齐本Run一次性FrozenSnapshot中的Setup规则承载；
6. 不把500/50写成0号位冻结值；
7. 当前不建设“单生产日保护阈值”；
8. 不新增Setup专属ReasonCode；
9. 与1/2号位统一ExplainTrace的Setup解释类型；
10. 不新增ProductionDay字段或Setup专属Calendar。

---

# 八、红线

本轮不得再次打开：

- SetupAttribute；
- Mold / Tool / Color / Material必备维度；
- MES / RoutingOperation.SetupTime运行真相；
- FromOperation / ToOperation模型；
- Setup静态ResourceGroup；
- 自然日00:00强制切窗口；
- Setup专属版本平台；
- Setup新的正式ReasonCode；
- 全工厂/全90天全局TSP；
- Setup为了减少换型突破硬约束、业务优先和履约目标。

**算法阈值尚需性能标定，不代表Setup业务尚未冻结。**

---

**结束**
