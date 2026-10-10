# APS V1 跨版本排程连续性——各号位复核与冻结文档修改指导

**版本：v2.0（待各号位复核确认版）**  
**日期：2026-09-10（北京时间）**  
**适用范围：APS V1**  
**文档性质：业务裁决建议 + 各号位复核清单 + 后续冻结文档修改指导**

> **重要声明**：本文件当前不是新的正式冻结基线。  
> 本文件用于1号位、2号位、3号位、5号位再进行一轮交叉确认。各号位确认没有业务、数据、接口和代码落地矛盾后，再由0号位确认收口，并交由文档修订对话按本说明增量修改当前有效冻结文档。  
> 在正式冻结文档完成升版以前，本文件不得单独覆盖现有最高冻结业务基线。

---

# 一、本轮为什么重新讨论“跨版本排程连续性”

当前冻结基线已经明确：

```text
夜间每次全量排程（FULL）
→ 生成新的计划版本（PlanVersion）
→ 生成新的TaskId
```

同时又明确：

```text
上一正式生效计划（ACTIVE）
中的锁定（Firm）/冻结（Frozen）
→ 下一版本继续形成不可移动约束
```

制造执行系统（MES）真实执行事实也会进入次日重新计算。

因此，现有冻结基线已经有“跨版本连续”的原则，但还没有把以下问题完全讲透：

1. 新计划版本产生新TaskId以后，怎样知道新旧版本是否还是同一条生产Task？
2. 已经下发MES的Task，怎样与MES工单建立稳定关系？
3. MES工单已经部分生产，下一版本到底继续排多少数量？
4. MES实际生产设备与上一APS计划设备不一致时，下一版本优先用哪个资源？
5. 当前MES只能取得累计报工结果，不能取得“此刻仍未结束的实时执行设备段”，APS怎样做V1可落地的连续性判断？
6. 锁定/冻结（Firm/Frozen）计划约束与MES真实执行事实冲突时，谁优先？
7. 1号位、2号位、3号位、5号位各自做到哪里，才不会重复解析MES、重复计算围栏（Fence）或重复维护Task身份？

本轮的目标不是建设一套新的“执行管理平台”，而是：

> **用最少的新字段、最少的新表和最小的跨号位契约，把“上一版本计划 → MES执行 → 下一版本计划”接成一条可以实际运行、可以追溯、数量不重复、资源不自相矛盾的闭环。**

---

# 二、当前已经确认的现有冻结基础

本轮不重新打开以下既有口径。

## 2.1 TaskId是计划版本内数据库身份

每个新的计划版本（PlanVersion）重新生成Task结果，因此：

```text
TaskId
= 当前PlanVersion内的数据库身份
```

不要求跨PlanVersion复用TaskId。

## 2.2 Firm/Frozen跨版本不依赖旧TaskId

2号位从上一正式生效计划（ACTIVE）读取仍然有效的：

- 锁定（Firm）；
- 冻结（Frozen）；
- 已经形成现实约束的执行事实；

转换成本次1号位的不可移动约束。

不建设独立冻结区快照平台（FrozenZoneSnapshot Platform）。

## 2.3 MES现实高于旧计划，但只限真实事实

优先级继续保持：

```text
真实执行事实（Execution）
>
冻结计划（Frozen）
>
锁定计划（Firm）
>
普通可移动计划（Movable）
```

但本轮增加一个非常重要的解释：

> **只有MES真正提供的事实才属于“执行事实”。**

例如：

- 工单状态；
- 累计完成量；
- 剩余量；
- 最后一次已经完成报工的设备；
- 最后报工时间；

这些是MES事实。

但是：

> **“未来还一定继续占最后报工设备”并不是MES事实，而是APS V1为了保持连续性采用的代理规则（Proxy Rule）。**

因此，最后报工设备不能以“MES现实”的名义强行突破其它Task已经存在的真实执行、冻结或锁定硬约束。

## 2.4 PI数量真相不由Task/Firm/Frozen决定

跨版本Task连续性不能反过来修改生产指示（PI）的数量真相。

当前生产数量主链继续保持：

```text
ERP生产指示剩余数量（PI RemainingQty）
+
MES执行事实
+
生产指示位置（PI Position）
+
下一工序上下文（NextOperationContext）
        ↓
本次逻辑生产需求（LogicalProductionDemand）
```

Task、Firm、Frozen、TaskNo只解决：

> 本次逻辑生产需求中，哪些数量已经存在跨版本执行/计划连续性，不能被重复生成或随意改动。

---

# 三、跨版本连续性的最终建议模型

本轮建议把跨版本连续性分成三种不同连续性，避免混在一起。

## 3.1 业务Task身份连续性

回答：

> “下一版本这条Task还是不是上一版本那条Task？”

使用：

```text
任务号（TaskNo）
```

## 3.2 数量连续性

回答：

> “这条MES工单还剩多少数量需要在下一版本继续承接？”

使用：

```text
MES工单状态（WorkOrderStatus）
+
累计良品数量（GoodQty）
+
剩余数量（RemainingQty）
```

## 3.3 资源连续性

回答：

> “下一版本剩余数量优先接着在哪台设备上排？”

由于当前MES ODS拿不到真正“此刻仍开放的执行段”，V1不假装自己拥有实时设备占用事实。

采用：

```text
最后报工设备（LastReportResourceCode）
+
上一ACTIVE中同TaskNo原计划资源（OriginalTaskResourceId）
```

形成确定、简单、可解释的资源连续性代理规则。

---

# 四、TaskNo跨版本生命周期

## 4.1 TaskNo的正式定位

建议正式冻结为：

```text
TaskId
= PlanVersion内数据库身份

TaskNo
= 一条生产Task跨计划版本、跨MES执行阶段的稳定业务身份
```

但TaskNo并不是永久业务主键。

它只在：

> **同一条生产Task一对一连续存在**

时保持。

## 4.2 普通可移动Task

上一版本普通可移动Task（Movable）在新FULL中重新形成后：

> 不做跨版本相似度匹配，不要求继承TaskNo。

可以生成新的TaskNo。

这样避免为了“编号稳定”重新引入复杂的跨版本相似度匹配。

## 4.3 Firm/Frozen一对一继续

如果上一ACTIVE中的Task仍然：

- 同一业务生产对象；
- 同一工序；
- 同一Task结构；
- 没有拆批；
- 没有合批；

则下一PlanVersion：

```text
新TaskId
+
继承原TaskNo
```

## 4.4 已经与MES工单建立关系的Task

一旦：

```text
TaskNo
↔
MESWorkOrderNo
```

已经建立稳定对应关系，则该TaskNo进入MES执行生命周期。

只要该MES工单尚未完成：

> **APS夜间FULL不得自行把该TaskNo拆掉、与其它TaskNo合并或变成另一个TaskNo。**

现场实际换设备生产，不改变TaskNo。

## 4.5 TaskNo生命周期结束

MES工单正式完成时：

```text
MES工单 = CLOSED / 完成
→ 原TaskNo执行生命周期结束
```

如果之后仍有新的合法生产缺口：

> 作为新的未来生产Task处理，生成新的TaskNo。

尚未进入MES执行生命周期的Task如果发生拆批、合批、工序实质变化或取消，旧TaskNo结束，新Task重新编号。

---

# 五、APS Task与MES工单的绑定关系

## 5.1 当前确认的业务顺序

MES工单号不可能在APS计划下发前生成。

正式业务顺序应是：

```text
APS生成Task
↓
APS已有TaskNo
↓
APS把计划送给MES
↓
MES创建MES工单
↓
MES生成MESWorkOrderNo
↓
建立：TaskNo ↔ MESWorkOrderNo
```

因此，旧接口/旧文档中如果存在“APS下发时已经携带MESWorkOrderNo”的表述，后续冻结文档必须修正。

## 5.2 当前2号位、5号位倾向的技术方案

当前2号位和5号位倾向：

> **采用中间表方式，并优先考虑把中间表放在APS数据库中。**

但目前技术方案仍在讨论，本文件暂时只冻结业务要求，不提前冻结：

- 最终表名；
- 最终数据库Schema；
- 由哪一个服务轮询；
- MES直接写还是集成程序写；
- 具体事务与重试方式；
- 幂等方案。

本轮明确：

> **幂等不作为本次业务裁决前置问题，后续由2号位、5号位在技术方案中自行保证不会重复创建MES工单。**

## 5.3 中间表至少必须表达的业务状态

如果最终采用中间表，则从业务生命周期上至少需要表达：

```text
待MES处理
→ 工单已创建
或
→ 创建失败
```

建议状态语义：

```text
待处理（PENDING）
已创建（CREATED）
失败（FAILED）
```

具体状态编码由2号位、5号位最终技术方案确认。

## 5.4 最小业务字段

中间表至少需要能够关联：

```text
APS TaskNo
APS来源TaskId / PlanVersionId（用于首次下发追溯）
生产指示号
MESWorkOrderNo
创建状态
失败原因
创建/处理时间
```

计划下发实际还需要的物料、工序、资源、数量、计划时间等字段，按MES现有创建工单要求确定，不在本业务指导中重复设计完整MES接口字段。

## 5.5 创建失败必须对APS可见

不能采用：

> “MES工单原则上不允许创建失败，所以APS不需要知道失败”

这种系统假设。

业务上允许系统目标是“尽量不失败”，但跨系统衔接必须能够区分：

```text
还没有处理
已创建成功
创建失败
```

失败至少需要记录：

```text
TaskNo
失败状态
失败原因
```

这样APS才能知道：

> 这张Task尚未真正进入MES执行生命周期。

本轮不进一步裁决自动重试次数、重试责任和人工重发方式，由2号位、5号位技术方案继续确认。

---

# 六、MES侧V1真正需要增加什么数据

## 6.1 不保存MES全部历史报工流水

本轮最新确认：

> **APS/ODS为了跨版本连续性，不需要复制MES中TaskNo建立以后全部历史报工明细。**

MES继续保留自己的完整执行历史。

APS侧只需要当前跨版本重排所需要的“最后已知事实”。

## 6.2 当前已有事实继续保留

现有工单/工序快照已经有：

```text
MESWorkOrderNo
WorkOrderStatus
GoodQty
RemainingQty
LastReportTime
DataCutoffTime
```

这些继续使用。

## 6.3 最小新增字段：最后报工设备

在与最后报工时间（LastReportTime）同一粒度上增加：

```text
最后报工设备（LastReportResourceCode）
```

语义必须写清：

> **LastReportResourceCode表示与LastReportTime对应的“最后一笔已经形成报工事实”的设备。**

它不是：

- 当前此刻一定正在生产的设备；
- 当前全部并行设备集合；
- 未来一定继续使用的硬锁定设备。

## 6.4 为什么不用ActualResourceCode

当前MES允许：

> 同一MES工单同时在两台或多台设备并行开工。

同时当前ODS只能取得已经形成报工结果的数据，不能取得尚未结束、尚未形成报工数量的实时开放执行段。

所以用一个单值：

```text
ActualResourceCode
```

会产生错误暗示。

正式建议字段为：

```text
LastReportResourceCode
```

因为它只陈述MES真正知道的历史事实。

## 6.5 不要求新增ActualStartTime / ActualEndTime

V1跨版本连续性不以完整实际开工/结束历史作为必需输入。

当前需要的是：

```text
工单状态
剩余数量
最后报工时间
最后报工设备
```

因此实际开始时间（ActualStartTime）和实际结束时间（ActualEndTime）都不是本轮P0必需字段。

MES如果未来自然能提供，可以用于实绩分析和追溯，但本轮不为了跨版本连续性强制建设。

## 6.6 不要求强制新增MES原始OperationCode

继续沿用当前冻结：

```text
工序名称（OperationName）
+
大工艺编码（StageCode）
```

作为MES进度标准化主轴。

MES原始工序编码（OperationCode）如果自然存在可以保留，但不是本轮P0必增字段。

---

# 七、MES工单未完成时的跨版本数量规则

## 7.1 基本判断

当：

```text
MES工单状态 = 未完成
且
RemainingQty > 0
```

说明：

> 该TaskNo还有跨版本生产连续性。

## 7.2 RemainingQty的作用

MES剩余数量（RemainingQty）只用于确定：

> **这条TaskNo还剩多少执行连续性份额。**

它不能取代：

```text
ERP PI RemainingQty
PI Position
NextOperationContext
```

形成的本次生产数量真相。

## 7.3 2号位必须做数量闭合

2号位本次先形成：

```text
当前LogicalProductionDemand
```

然后识别其中属于已有TaskNo执行连续性的数量。

概念上：

```text
本次LogicalProductionDemand
=
已有跨版本连续份额
+
本次可重新安排份额
```

不能把MES RemainingQty再与完整LogicalProductionDemand机械相加，否则会造成重复排产。

## 7.4 数量不一致时不能静默相加

如果MES RemainingQty与2号位本次从PI、MES位置事实得到的当前逻辑生产数量存在明显矛盾：

> 2号位必须记录数据问题（Data Issue），按当前冻结的权威数量链收口，不允许把两个数量机械相加。

具体容差和数据问题处理方式由2号位结合现有数据质量规则确认。

---

# 八、MES工单未完成时的资源连续性——V1核心代理规则

## 8.1 MES有过报工

如果：

```text
MES工单状态 = 未完成
RemainingQty > 0
LastReportResourceCode 有值
```

则：

> **下一版本剩余数量首选最后报工设备继续安排。**

例如：

```text
上一ACTIVE：
TaskNo = TK001
原计划设备 = MC01

MES实际历史：
曾在MC02生产
最后一次报工设备 = MC03

MES仍剩350件
```

下一版本首选：

```text
TK001剩余350
→ MC03
```

## 8.2 这只是资源连续性代理，不是MES硬事实

必须明确：

```text
LastReportResourceCode = MC03
```

只证明：

> MC03是最近一次已经完成报工的设备。

不能证明：

> 排程截止时点MC03仍然正在执行这张工单。

因此：

> **LastReportResourceCode形成资源优先关系（Preferred Resource），不是执行资源硬锁（Execution Resource Lock）。**

这条区别是本轮必须写进正式冻结文档的关键红线。

## 8.3 MES工单已创建，但还没有任何报工

如果：

```text
MESWorkOrderNo 已经创建
RemainingQty > 0
LastReportResourceCode = NULL
```

则没有MES实际设备事实。

V1首选：

> **上一ACTIVE中同TaskNo的原计划设备。**

即：

```text
PreferredResource
=
OriginalTaskResourceId
```

不因为“已经有MES工单”而虚构一个实际设备。

---

# 九、最后报工设备与Firm/Frozen资源占用冲突怎么办

## 9.1 先纠正概念

“设备有Fence”不是严格业务术语。

围栏（Fence）作用于Task计划区间。

设备上真正影响排程的是：

> **其它Task已经形成的真实执行（Execution）、冻结（Frozen）、锁定（Firm）不可移动资源占用块。**

## 9.2 V1建议资源选择顺序

对仍未完成的MES TaskNo：

```text
第一优先：
最后报工设备（LastReportResourceCode）

如果没有最后报工设备：
直接使用上一ACTIVE原计划设备

如果最后报工设备与其它不可移动资源块冲突，
无法按本次连续性规则安排：
↓
第二优先：
上一ACTIVE中同TaskNo原计划设备（OriginalTaskResourceId）
```

## 9.3 暂不自动扩展到第三、第四台设备

为了保持V1简单、可解释：

> **MES执行连续性路径暂不自动遍历其它历史报工设备。**

也不根据历史报工量做多设备比例分配。

如果最后报工设备不可行、原计划设备也无法形成合法未来计划，当前0号位建议：

```text
返回未排（UNSCHEDULED）
+
明确原因
+
进入现有人工/局部重排（Candidate）机制
```

是否允许1号位在这一特殊场景继续自动搜索其它合格资源（Eligible Resource），本轮请1号位和2号位再次确认。

> **0号位当前建议：V1先不自动扩展第三资源，保持保守；如1号位证明现有求解器（Solver）在不增加复杂度的情况下天然可继续合法替代，可在本轮复核后再决定。**

## 9.4 不能被自己的上一版本Task挡住

这是2号位构造输入时必须保证的红线。

例如：

```text
上一ACTIVE：
TK001 → MC01 02:00~06:00
```

本次又要继续排TK001 RemainingQty，则上一版本TK001自己的旧未来时间块：

> **不能同时作为“其它不可移动资源阻挡”再次传入1号位。**

否则会出现TK001被自己的旧Task挡住。

正确方式：

> 同TaskNo旧连续事实要在本次输入中被识别、替换/归并；只有其它TaskNo的有效Execution/Frozen/Firm占用才构成外部阻挡。

---

# 十、Firm/Frozen与MES连续性的最终关系

## 10.1 真实MES事实优先

如果上一计划TK001计划资源为MC01，但MES最后实际报工资源为MC03，则说明现场已经真实在MC03生产过。

下一版本资源连续性首选MC03。

## 10.2 但LastReportResource不是硬Execution锁

如果MC03未来被其它真正不可移动Task占住：

> 不能用“Execution > Frozen”去强行推开对方。

因为“MC03未来继续”只是APS代理规则，不是MES当前实时执行事实。

所以：

```text
真实硬约束
>
资源连续性代理偏好
```

## 10.3 Firm/Frozen本身仍按原冻结规则执行

普通未进入MES执行生命周期的Firm/Frozen任务：

- 不拆；
- 不合；
- 不移动；
- 不换资源。

Firm只有经过最小人工授权/确认重新打开后，才可进入Candidate调整。

Frozen不因普通夜间FULL自动打开。

---

# 十一、1号位输入边界——继续坚持“薄消费”

1号位提出“不想自己解析MES、不想自己算Fence”，本轮建议确认其方向正确。

正式职责：

```text
5号位
→ 提供MES标准化事实

2号位
→ 形成当前LogicalProductionDemand
→ 判定Execution/Frozen/Firm/Movable
→ 做数量闭合
→ 形成硬约束和资源连续性偏好

1号位
→ 只做有限产能求解
```

## 11.1 1号位不需要消费完整MES原始字段

不要求1号位直接理解：

- MESWorkOrderNo；
- MES原始WorkOrderStatus；
- GoodQty历史；
- MES报工事件；
- LastReportTime的MES业务含义；
- TaskNo ↔ MES工单如何建立；
- FenceConfig如何版本化。

## 11.2 1号位需要区分两种输入

### A. 不可移动硬约束（Hard Constraint）

例如：

- Execution真实硬事实；
- Frozen；
- Firm；
- 外Domain ACTIVE共享资源阻挡。

这类不能被Solver移动。

### B. 资源连续性偏好（Resource Continuity Preference）

针对MES工单未完成、当前又无法取得实时开放执行设备的场景，由2号位提供：

```text
首选资源（PreferredResourceId）
回退原资源（OriginalTaskResourceId）
连续数量
```

这类不能与真正硬约束混为一谈。

---

# 十二、1号位拆批/合批边界

上一轮1号位已经指出当前合批逻辑可能把固定Anchor再次参与合批。

本轮继续建议确认：

对真正Firm/Frozen/Execution硬约束，普通FULL中：

```text
不拆
不合
不移动
不换资源
```

对已经建立MESWorkOrderNo且尚未完成的TaskNo，即使本次资源连续性只是“首选设备”而非硬资源锁：

> **TaskNo身份本身不得被普通FULL拆掉或与其它TaskNo合并。**

因为MES已经有真实工单绑定。

---

# 十三、Task数量与产能数量继续区分

当前Task已经区分：

```text
净产出数量（Quantity / NetOutputQty）
实际用于有限产能占用的计划加工数量（PlannedProcessQty）
```

因此跨版本连续份额也不能只用一个含糊的LockedQuantity解决全部问题。

建议2号位给1号位时明确：

```text
连续净产出数量（ContinuationOutputQty）
连续加工数量（ContinuationProcessQty）
```

连续加工数量继续按本次冻结的计划良率等规则计算。

1号位只按加工数量占用能力。

---

# 十四、围栏（Fence）和3号位职责

## 14.1 当前FenceConfig作用域可以继续

现有：

```text
产品族
× 工厂
× 工艺类型
```

已经能满足当前Firm/Frozen配置作用域，不需要因为跨版本连续性再增加更细维度。

## 14.2 不建议新建Fence专属版本平台

当前3号位已经有：

```text
规则集版本（RuleSetVersion）
参数集版本（ParameterSetVersion）
策略版本（StrategyProfileVersion）
冻结策略快照（FrozenStrategySnapshot）
```

建议：

> **冻结天数（FrozenDays）和锁定天数（FirmDays）纳入现有参数版本/策略快照。**

不再新建FenceConfigVersion、FenceSnapshot或第二套Fence版本治理服务。

现有FenceConfig如果代码仍使用，可以先兼容保留。

## 14.3 快照粒度

建议继续确认：

> **一次排程运行（ScheduleRun）冻结一套Fence参数。**

同一ScheduleRun下多个Domain的PlanVersion使用同一套本次冻结参数。

## 14.4 Firm重开

继续沿用：

> **最小人工授权/确认 + 审计。**

不建设完整办公自动化审批（OA Approval）主链。

3号位只负责谁能重开、权限、确认和审计；重开以后怎样重新排Task仍归2号位+1号位。

---

# 十五、5号位MES事实边界

5号位本轮建议收缩为：

```text
MES工单状态
累计良品数量
剩余数量
最后报工时间
最后报工设备
MES工单号
生产指示号
```

并保证这些事实按数据截止时间进入当前排程快照。

## 15.1 TaskNo怎样进入MES事实链

由于最终计划下发技术方案仍未冻结，本轮不先指定TaskNo一定必须直接冗余进每一张ODS表。

只冻结：

> **5号位最终标准化MES事实时，必须能够把MESWorkOrderNo对应回APS TaskNo。**

如果中间表已经可以稳定提供：

```text
TaskNo ↔ MESWorkOrderNo
```

则OperationProgressSnapshot是否再冗余TaskNo，由2号位、5号位根据实现性能和便利性决定。

不为了“字段看起来完整”重复存储。

---

# 十六、计划下发方式暂不影响本轮业务闭环

当前计划下发到底采用接口还是中间表，由2号位、5号位作为MES工程师继续讨论。

本轮只要求最终方案满足：

```text
APS TaskNo能够送到MES
MES创建MESWorkOrderNo
两者能稳定绑定
MES创建成功/失败对APS可见
后续MES执行事实能够回到原TaskNo
```

只要这五点满足：

> 采用接口还是中间表，不改变跨版本连续性的业务模型。

---

# 十七、完整端到端闭环

## 17.1 第一天APS排程

```text
PlanVersion V1
TaskId = 10001
TaskNo = TK001
Quantity = 1000
Resource = MC01
```

## 17.2 下发MES

```text
APS送出TK001
↓
MES创建工单
MESWorkOrderNo = WO90001
↓
绑定：TK001 ↔ WO90001
```

如果创建失败：

```text
TK001
状态 = FAILED
FailureReason = ...
```

该Task尚未进入正常MES执行生命周期。

## 17.3 MES实际多设备生产

MES允许WO90001先后或同时在MC02、MC03、MC04生产。

APS/ODS不复制全部历史报工。

截至本次数据截止时间只保存：

```text
WorkOrderStatus = IN_PROGRESS
GoodQty = 650
RemainingQty = 350
LastReportTime = 01:20
LastReportResourceCode = MC03
```

## 17.4 第二天FULL

2号位先依据PI/MES Position形成本次LogicalProductionDemand，再识别TK001仍有350连续份额，防止该350再次作为普通新Task重复生成。

## 17.5 资源连续性

首选MC03，因为最后报工设备是MC03。

如果MC03未来与其它有效不可移动Task冲突，按V1当前建议回退上一ACTIVE中TK001原计划设备MC01。

如果MC01也不能形成合法计划：

```text
UNSCHEDULED
+
Explanation
+
人工/局部重排
```

本轮不根据MES历史设备比例拆350件到MC02、MC03、MC04。

## 17.6 新PlanVersion

形成：

```text
新TaskId = 20031
TaskNo仍 = TK001
```

因为仍然是同一MES工单对应的同一生产Task继续执行。

## 17.7 MES工单完成

当WO90001 = CLOSED，则TK001执行生命周期结束。

已经完成的历史事实不再作为未来资源占用。

如果之后仍产生新的合法生产缺口，生成新的未来Task和新的TaskNo。

---

# 十八、本轮明确不做的事情

为了保持APS V1最小闭环，本轮不建设：

1. MES全部报工历史复制表；
2. MES实时开放执行段平台；
3. 多设备RemainingQty历史比例推断；
4. Task跨版本相似度匹配算法；
5. 独立Execution Lifecycle平台；
6. Fence专属版本平台；
7. FrozenZoneSnapshot平台；
8. 完整OA审批主链；
9. 新的MultiDomain Solver；
10. 由1号位直接读取/解析MES。

也暂不把幂等作为本轮业务裁决项。

---

# 十九、请1号位本轮重点确认

请1号位只回答“能否落地 / 有何冲突 / 最小需要改什么”，不要重新设计其它号位业务。

### Q1

现有Solver是否可以明确区分：

```text
不可移动硬约束
vs
资源连续性偏好
```

即LastReportResourceCode是首选资源（Preferred Resource），不是执行硬锁。

### Q2

对于MES未完成Task：

```text
PreferredResourceId = LastReportResource
FallbackResourceId = 上一ACTIVE原Resource
```

这种两级资源选择，现有1号位结构能否最小增量实现？

### Q3

如果首选和原资源都不可行：

> V1返回UNSCHEDULED而不自动扩展第三资源，是否会与当前已冻结的Solver资源选择机制产生实质冲突？

如果现有Solver天然继续搜索其它合格资源且删除反而更复杂，请明确提出。

### Q4

已经建立MESWorkOrderNo的TaskNo是否可以在普通拆批/合批前直接排除？

### Q5

是否需要把现有一个LockedQuantity拆成：

```text
ContinuationOutputQty
ContinuationProcessQty
```

或者现有字段已有等价能力？

### Q6

同TaskNo旧版本自己的资源块，能否在新Run组装时由2号位标识后避免被当成外部阻挡？

---

# 二十、请2号位本轮重点确认

### Q1

TaskNo跨版本生命周期是否可以按本文执行：

```text
普通Movable不强保TaskNo
Firm/Frozen一对一连续可继承
已绑定MES工单且未完成必须继承
拆/合/完成后重新生成
```

### Q2

是否能够在本次LogicalProductionDemand形成后，把MES RemainingQty识别成已有TaskNo的连续份额，避免与普通新Task数量重复？

### Q3

是否能保证同TaskNo上一ACTIVE旧块不再作为另一个外部不可移动资源块重复送给1号位？

### Q4

是否同意：

```text
有最后报工设备
→ 首选LastReportResource

无最后报工设备
→ 首选原Task Resource
```

### Q5

最后报工设备与其它硬块冲突时回退原Task Resource，是否能由2号位组装契约 + 1号位Solver最小实现？

### Q6

与5号位讨论的“APS数据库中间表”技术方向，请给出最终建议：

- 是否采用；
- 表放哪里；
- 谁写待处理；
- 谁写CREATED/FAILED；
- MESWorkOrderNo何时回写；
- 创建失败以后怎样处理。

本轮可以不讨论幂等。

---

# 二十一、请3号位本轮重点确认

### Q1

FrozenDays / FirmDays是否同意纳入：

```text
ParameterSetVersion
→ StrategyProfileVersion
→ FrozenStrategySnapshot
```

而不新建Fence专属版本平台？

### Q2

一次ScheduleRun冻结一套Fence参数，是否符合3号位当前治理结构？

### Q3

Firm重新打开继续采用：

```text
最小人工授权/确认
+
审计
```

不建设完整OA审批，是否无冲突？

### Q4

3号位是否确认：

> Fence运行时判定、MES连续性资源选择、TaskNo生命周期均不属于3号位执行职责？

---

# 二十二、请5号位本轮重点确认

### Q1

现有MES数据是否可以在当前OperationProgressSnapshot同等粒度增加：

```text
LastReportResourceCode
```

并保证它与LastReportTime对应同一笔最后报工事实？

### Q2

是否确认：

> 为跨版本连续性不需要复制MES全部历史报工流水？

### Q3

是否确认ActualStartTime / ActualEndTime不是本轮P0必需字段？

### Q4

是否确认MES原始OperationCode不是本轮P0必增字段，继续使用OperationName + StageCode主轴？

### Q5

与2号位讨论的APS数据库中间表方案，能否保证：

```text
TaskNo ↔ MESWorkOrderNo
```

稳定建立，并能返回：

```text
已创建 / 失败 / 失败原因
```

### Q6

如果TaskNo不直接冗余进入全部ODS表，5号位能否通过：

```text
MESWorkOrderNo
→ 中间表
→ TaskNo
```

稳定关联到APS Task？

---

# 二十三、建议确认后需要修改的冻结文档

以下为建议影响范围。正式修改时必须以届时项目来源中的最新有效母版为准增量升版，不能按照本文从零重写。

## P0：业务和主流程文档

### 1. 《APS V1 最终全部流程与业务基线》

需要补：

- TaskId / TaskNo跨版本身份；
- Firm/Frozen跨版本承接；
- MES工单绑定后TaskNo连续；
- MES未完成Task的数量连续性；
- LastReportResource代理规则；
- 同TaskNo旧块不得自阻挡；
- MES工单完成后TaskNo生命周期结束。

### 2. 《APS 有限产能排产与滚动90天计划业务说明》

需要补：

- 1号位硬约束 vs 资源连续性偏好；
- LastReportResource首选、原资源回退；
- 固定Task禁止普通拆/合；
- MES绑定TaskNo禁止普通拆/合；
- 连续净产出数量/加工数量语义；
- 不能让上一版本同TaskNo自己的块成为阻挡。

### 3. 《APS 核心排产全流程走查》

需要把：

```text
上一ACTIVE
→ MES状态/RemainingQty/LastReportResource
→ 本次LogicalProductionDemand
→ 连续份额
→ Solver
→ 新TaskId/继承TaskNo
```

画成完整端到端链。

## P1：Pegging、接口、数据和DDL

### 4. 《APS Pegging供需承接与分层计算业务说明》

只做最小补充：

> TaskNo连续不等于Demand↔PI关系跨版本固定。

每个FULL普通Pegging仍按当前最新优先级重新计算。

MES Task连续份额不能改变PI Supply数量真相。

### 5. 《APS 集成接口设计》

需要修正：

- APS下发时不存在MESWorkOrderNo；
- TaskNo先由APS产生；
- MES创建工单后再产生MESWorkOrderNo；
- 接口/中间表最终方案按2/5确认回写；
- 1↔2增加/确认资源连续性偏好语义；
- 不要求1号位直接消费MES原始字段。

### 6. 《APS 数据架构与防腐层设计方案》

需要补：

- APS TaskNo与MESWorkOrderNo关联边界；
- 不复制MES全部报工历史；
- LastReportResourceCode属于ODS聚合事实；
- 中间表若放APS数据库的归属按2/5最终方案确定。

### 7. 《APS 数据库字段说明文档》

预计需要：

```text
OperationProgressSnapshot
+ LastReportResourceCode
```

并明确：

- 与LastReportTime对应；
- 是最后报工设备，不是当前实时ActualResource；
- WorkOrderStatus / GoodQty / RemainingQty继续沿用。

中间表字段待2/5技术方案确认后再正式写入。

### 8. 《APS 数据库表结构设计 DDL》

预计至少需要：

```text
OperationProgressSnapshot
+ LastReportResourceCode
```

计划下发中间表是否新增、名称和字段等2/5号位本轮确认后再落DDL，不在本文件抢先定死。

## P1：各号位实施包

### 9. 《APS V1 1号位有限产能排程开发实施包》

增加：

- 硬Anchor与资源连续性偏好区别；
- MES连续Task的Preferred/Fallback Resource；
- MES绑定Task禁止普通拆/合；
- 连续净产出数量/加工数量；
- 同TaskNo不得自阻挡。

### 10. 《APS V1 2号位增量开发实施包》

增加：

- TaskNo生命周期Owner；
- MES连续份额数量闭合；
- Fence运行时判定；
- 资源连续性偏好组装；
- 同TaskNo旧块替换；
- 中间表/计划下发方案（待2/5最终确认）。

### 11. 《APS V1 3号位规则参数认证权限与运行生命周期开发实施包》

增加：

- FrozenDays/FirmDays版本治理；
- ScheduleRun级冻结；
- Firm重开最小授权；
- 不负责运行时Fence/Task连续性计算。

### 12. 《APS V1 5号位复杂业务事实ODS与业务接口接入开发实施包》

增加：

- LastReportResourceCode；
- TaskNo↔MESWorkOrderNo关联消费方式；
- 不建设MES历史执行流水复制；
- 不强制ActualStart/End；
- 不强制MES OperationCode；
- 与2号位协同的下发中间表方案（待确认）。

## P2：验收与审核基线

### 13. 《APS V1 0号位总体项目验收包》

增加跨版本连续性专项验收。

### 14. 《APS V1 代码审核检查基线》

增加：

- TaskNo跨版本错误复用；
- MES绑定Task被拆/合；
- RemainingQty重复排产；
- LastReportResource被错误当成硬Execution资源；
- 同TaskNo自阻挡；
- 1号位自行解析MES/Fence；
- 5号位复制整套MES历史流水等检查项。

### 15. 当前有效冻结基线索引 / 变更记录

全部文档确认升版后统一更新。

---

# 二十四、最低验收场景

## C01 普通Movable跨版本

```text
上一Task可移动
→ 新FULL重新排
→ 不要求TaskNo继承
```

## C02 Firm一对一延续

```text
上一Task Firm
→ 下一版本新TaskId
→ 原资源/时间保持
→ TaskNo继承
```

## C03 MES工单已创建但无报工

```text
TaskNo已绑定MESWorkOrderNo
LastReportResourceCode = NULL
RemainingQty > 0
→ TaskNo继承
→ 首选上一ACTIVE原资源
```

## C04 MES有报工且未完成

```text
LastReportResource = MC03
RemainingQty = 350
→ TaskNo继承
→ MC03首选
```

## C05 最后报工设备被其它Frozen挡住

```text
MC03存在其它Task的不可移动块
→ 不移动对方
→ 回退原Task Resource
```

## C06 同TaskNo不能自阻挡

```text
上一ACTIVE TK001旧块
+
本次TK001连续份额
→ 不能把旧块作为“其它Task”阻挡自己
```

## C07 多设备历史不做比例拆分

```text
同一MES工单曾在MC01/MC02/MC03报工
最后报工MC03
RemainingQty=300
→ 不按历史比例拆成3份
→ 只按V1最后报工资源代理规则处理
```

## C08 RemainingQty不能与LogicalDemand重复

```text
LogicalProductionDemand = 500
其中TK001连续RemainingQty = 200
→ 本次总排程仍是500
→ 不是700
```

## C09 MES工单完成

```text
MESWorkOrderStatus = CLOSED
→ 原TaskNo执行生命周期结束
→ 不再形成未来资源连续性
```

## C10 MES工单创建失败

```text
TaskNo存在
MES创建失败
→ 状态FAILED
→ 记录失败原因
→ 不把它当成已进入MES正常执行生命周期
```

---

# 二十五、本轮复核后才能正式冻结的开放项

本轮业务主模型已经收敛，但为了避免0号位替开发号位拍技术细节，以下事项等待本轮反馈后再最终写死：

```text
A. 1号位：
   PreferredResource / FallbackResource
   最小怎样接入现有Solver；
   是否坚持“两级失败即UNSCHEDULED”，
   还是现有Solver天然继续合法Eligible Resource。

B. 2号位：
   TaskNo继承与MES连续份额
   如何接入当前Task持久化/组装逻辑。

C. 2号位 + 5号位：
   计划下发最终采用接口还是APS库中间表；
   当前倾向中间表，但技术方案尚未冻结。

D. 2号位 + 5号位：
   中间表最终字段、物理位置、读写责任、
   创建失败后重试/人工处理方式。

E. 5号位：
   LastReportResourceCode是否能够稳定从MES报工事实得到，
   并与LastReportTime保持同一最后报工记录语义。
```

除以上技术实现点外，各号位如认为本文还有数量无法闭合、数据源无法提供、职责冲突、当前代码结构无法最小实现或与现有冻结文档明显冲突，必须在本轮一次提出。

---

# 二十六、本轮建议最终收口的一句话

如果各号位本轮确认无冲突，APS V1跨版本排程连续性正式收口为：

> **每个PlanVersion生成新的TaskId；TaskNo只在同一生产Task一对一连续时跨版本保持，尤其已绑定MES工单且尚未完成的Task必须保持TaskNo。MES只向APS提供工单状态、累计/剩余数量、最后报工时间和最后报工设备等当前必要事实，不复制完整历史报工。2号位先按PI数量真相形成本次LogicalProductionDemand，再识别同TaskNo剩余连续份额，防止重复排产；MES有最后报工设备时将其作为V1资源连续性首选，没有报工时使用上一ACTIVE原计划设备。最后报工设备只是代理偏好，不得突破其它真实Execution/Frozen/Firm硬占用；必要时回退原资源。1号位只消费2号位整理好的硬约束和连续性偏好，不解析MES、不计算Fence；3号位只治理Fence参数版本和Firm重开授权；5号位只提供MES标准化事实。计划下发采用接口还是中间表不改变上述业务模型，最终只需保证TaskNo能够进入MES、MES创建后的MESWorkOrderNo能够稳定反绑TaskNo，并把创建成功/失败结果返回APS。**

---

# 二十七、交给后续文档修订对话时的使用规则

各号位全部确认以后，后续文档修订对话必须：

```text
1. 读取当前项目来源中的最新有效母版；
2. 按本文逐份做增量修订；
3. 不从零重写母版；
4. 不借跨版本连续性重新打开PI Position、Pegging、Domain、
   Supply模型或有限产能其它已冻结业务；
5. 对仍标记“待2/5技术确认”的中间表细节，
   必须使用最终确认后的技术方案，不能由AI自行发明；
6. 正式版本号以届时当前有效版本顺延，
   不机械采用本文中的示例版本号。
```

本文件在各号位确认前保持“待确认版”；确认后由0号位决定是否升为正式文档修改执行基线。
