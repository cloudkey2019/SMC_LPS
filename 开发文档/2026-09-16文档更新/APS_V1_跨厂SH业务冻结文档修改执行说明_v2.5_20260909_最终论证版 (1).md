# APS V1 跨厂SH业务冻结文档修改执行说明

**版本：v2.5（最终论证版）**  
**日期：2026-09-09**  
**用途：指导当前APS V1有效冻结文档统一修订跨厂SH业务口径**  
**适用对象：0号位、1号位、2号位、3号位、5号位及文档治理人员**  
**文档性质：冻结文档修改执行说明，不替代修订完成后的正式冻结文档**

> **替代关系（重要）**：本文件仅替代《APS_V1_跨厂SH_PI_Supply收敛口径与冻结文档修改指导说明_v1.0_20260908》中**与厂间出荷指示（SH）有关的修改指导**。  
> 2026-09-08文档中已经确认且未被本轮重新打开的**生产指示（PI）单一供给（Supply）+ 生产指示位置（PI Position）**等内容继续有效，本文件不得被理解为废止或重开PI口径。  
> 2026-09-08版本关于“SH单一供给身份”的方向继续有效，但其中“5号位必须提供完整SH权威供给事实”“普通需求与SH的关系必须由5号位提供固定单据关系”“按在途/已到/ZP/BP解释SH不同数量和不同可用时间”等SH内容，已被2026-09-09最新业务事实和统一裁决修正，不得继续作为当前SH开发依据。

---

# 一、本次为什么重新打开跨厂SH问题

本次不是为了重新设计跨厂业务，而是为了解决当前冻结文档、2号位代码和真实ERP数据语义之间已经暴露出的几个冲突。

## 1. 最初把SH单独拿出来讨论的原因

跨厂业务原来同时存在两类对象：

1. 厂间出荷指示（SH）；
2. 大工艺接续型生产指示（PI）。

其中，大工艺接续型生产指示（PI）已经明确收敛为：

> **生产指示（PI）始终只有一个供给（Supply）身份，跨厂过程通过生产指示位置（PI Position）表达，不再创建额外的“在途供给”。**

但是2号位现有SH实现仍保留了历史三段式处理：

```text
在途供给
→ 已到供给
→ 未生产供给
```

或者将同一张SH拆成：

```text
在途部分
+
已到部分
+
未生产部分
```

分别进入供需承接（Pegging）。

这会带来三个问题：

### 1.1 同一物理SH可能被重复当成多份供给

一张SH本来是一个真实业务单据，如果同时形成“在途供给”和“已到供给”，容易造成同一物理数量重复进入供给池，破坏供给余额唯一性。

### 1.2 当前ERP事实无法支撑SH内部数量切片

当前跨厂在途视图（ERP_InterplantInTransit_View）可以提供在途数量，但不能可靠说明同一SH剩余数量分别属于：

- 在途；
- ZP；
- BP；
- 已到；
- 其它内部小阶段。

例如：

```text
SH总量 = 100
跨厂在途视图数量 = 70
```

当前业务事实并不能证明另外30件一定是“已到”，也不能证明属于ZP/BP。

因此不能人为构造：

```text
70件一个供给时间
+
30件另一个供给时间
```

### 1.3 ZP/BP等小阶段对当前V1没有独立排程价值

当前业务已经明确：

> **跨厂SH在V1中统一按“跨厂履行中/在途”理解，不再细分ZP/BP等内部小阶段参与供需承接。**

这些ERP内部事实如果存在，可以保留用于追溯和数据核对，但不应再变成新的供给身份。

因此，本轮最初的核心目的就是：

> **把SH从“多阶段、多供给”重新收敛成“一张SH、一个供给身份”，防止同一物理供给重复计算。**

---

# 二、为什么2026-09-08修改指导还需要再次修正

2026-09-08版本正确解决了“SH不能再拆三段供给”的问题，但为了补齐SH事实，又进一步假设：

- 5号位需要提供一套完整的SH跨厂供给事实；
- 5号位需要提供SH权威数量；
- 5号位需要提供普通订单/需求与SH之间的固定单据关系；
- SH内部状态仍可能用于不同数量、不同可用时间的解释。

经过2号位、5号位继续追问并结合真实ERP订单模型复核后，确认这些假设过重，而且部分与真实数据模型不符。

本轮新增确认的真实业务事实如下。

---

# 三、本轮新增确认的真实业务事实

## 1. SH不是独立于订单之外的第二套主档

厂间出荷指示（SH）本身就在APS订单表中。

它与普通客户订单一样，订单类型统一为：

> **销售订单（SALES_ORDER）**

SH通过客户分类识别：

```text
订单类型（OrderType） = 销售订单（SALES_ORDER）
客户分类（CustomerSegment） = “跨厂”
数据库标准值 = INTER_FACTORY
```

文档正文使用中文“跨厂”，代码、数据库和接口实现使用当前标准值 `INTER_FACTORY`，不得把中文“跨厂”直接写成新的数据库枚举值。

对于这类跨厂订单：

> **订单号（OrderNo）就是SH号。**

因此，不存在“Order再关联一个SH主档”的额外一对一关系。

### 1.1 第一层顶层需求池必须排除SH订单本身

这是本轮二次论证后补充的关键红线。

当前旧冻结文档把第一层顶层独立需求笼统写成“销售订单（SALES_ORDER）”。但本轮已经确认：厂间出荷指示（SH）本身也属于销售订单（SALES_ORDER），只是通过客户分类（CustomerSegment）=“跨厂”（INTER_FACTORY）识别。

因此，如果正式文档仍然简单写：

```text
第一层顶层Demand = 全部SALES_ORDER
```

会造成同一张SH同时被当成：

- 第一层普通顶层需求；
- 第一层SH供给；
- 第二层源厂SH生产需求。

这是重复建模，必须修正。

正式口径应改为：

```text
第一层普通顶层独立需求
=
销售订单（SALES_ORDER）
但排除客户分类（CustomerSegment）=“跨厂”（INTER_FACTORY）的SH订单
+
顶层MTS/SS/SS_U等其它独立需求
```

SH订单本身不作为“目标工厂普通客户需求”进入第一层Demand池。

同一条SH订单在APS中的业务角色统一为：

```text
目标工厂视角：
SH = 第一层可承接其它需求的跨厂供给（Supply）

源工厂视角：
若尚未进入跨厂履行状态
SH = 第二层一条完整源厂生产需求（Demand）
```

因此，正式修订《最终全部流程与业务基线》《Pegging业务说明》《核心排产全流程走查》和2号位实施包时，必须同步修正第一层Demand池定义，不能只增加第二层SH规则而保留“全部SALES_ORDER无差别进入第一层”的旧写法。

## 2. SH在订单表中的身份，与供需承接中的多对多关系必须分开

虽然OrderNo就是SH号，但SH作为目标侧供给时，与需求之间仍然天然允许多对多。

例如：

```text
需求A 60件 ─┐
            ├─ SH001 100件
需求B 40件 ─┘
```

或者：

```text
需求A 100件
├─ SH001 40件
└─ SH002 60件
```

这些关系是2号位在本次供需承接（Pegging）中形成的分配关系（Allocation），不是ERP需要预先提供的固定单据关系。

因此必须删除旧指导中：

> “普通需求要承接哪张SH，必须由5号位提供Order/Demand ↔ SH固定Document关系”

这一要求。

只有ERP真实存在客户专属、强制指定、质量资格等不可替代关系时，5号位才需要提供严格绑定证据（Strict Binding Evidence）。

## 3. 当前订单表只有一个工厂字段，其真实语义是目标工厂

当前订单链路中只有一个FactoryCode / FactoryId。

结合真实ERP业务确认，其含义应正式固定为：

> **订单最终要入库、要满足需求的工厂，也就是目标工厂。**

普通订单通常生产工厂与入库工厂相同，所以过去只有一个工厂字段没有明显问题。

但是跨厂SH不同：

```text
源工厂生产
→ 跨厂运输
→ 目标工厂入库
```

因此对于SH：

```text
现有FactoryId = 目标工厂
新增SourceFactoryId = 源工厂
```

这是本轮必须补的真实字段缺口。

## 4. 不保留源工厂仓库/工序代码字段

此前讨论过增加“源工厂仓库/工序代码”，本轮最终裁决为：

> **正式Order不保留SourceWarehouseProcessCode。**

V1只补源工厂身份即可。

如果ERP取源工厂时内部使用仓库/工序代码做映射，可以作为ETL内部过程使用，但不要求进入最终Order业务模型。

## 5. 5号位现有跨厂在途视图本轮不要求改结构

跨厂在途视图（ERP_InterplantInTransit_View）继续定位为：

> **ERP跨厂物流事实视图。**

本轮不要求为了SH重新设计或新建完整SH主档视图。

如果当前视图某些字段暂时没有真实ERP数据：

> **允许保持空值，或在联调阶段使用模拟数据（Mock / Fixture）。**

不得为了补齐SH业务主档，强迫5号位在该视图重复提供SH权威总量、完整SH主档、普通需求与SH之间的分配关系。

## 6. 同一个SH号只能整单一次入库

ERP业务规则新增确认：

> **同一SH号不允许分多次入库。**

如果业务确实需要分两次交货，必须由业务人员在ERP中拆成两个SH号。

因此，同一SH虽然源工厂内部可能分批生产，但目标工厂不能把它理解成多个分批可用供给。

例如：

```text
SH001 = 100件

源厂内部：
40件 9月15日完成
60件 9月17日完成
```

目标侧不能形成：

```text
40件 9月16日可用
60件 9月18日可用
```

而应形成：

```text
SH001整单准备完成时间 = 9月17日
SH001目标侧可用时间 = 9月17日 + 跨厂运输提前期
```

即：

> **一张SH、一个整单目标侧可用时间。**

---

# 四、SH分层供需承接的最终业务模型

## 1. 第一层：SH作为目标侧供给

第一层回答：

> 顶层需求由什么真实供给承接。

当目标侧需求由SH承接时：

```text
顶层需求
↓
SH供给
```

当前层到SH结束。

普通需求与SH之间可以多对多，由2号位运行时供需承接形成分配关系。

## 2. 第二层：SH转换成源工厂履约需求

SH作为目标侧供给存在，并不表示这张SH一定还需要“新增生产”。

第二层真正要解决的是：

> **这张仍处于活跃状态、且尚未进入跨厂在途的SH，在源工厂侧如何完成其生产履约义务。**

按当前已冻结的厂间SH源厂承接顺序，第二层继续采用：

```text
生产指示（PI）
→ 生产指示位置（PI Position）
→ 仍有缺口时形成新增生产
```

本轮不得自行增加“搜索源工厂普通库存来承接SH”的新路径。

因此，第二层是否生成源工厂SH需求，先按照**订单状态 + 跨厂在途视图是否存在该SH号**做整单判断，不做70/30等数量切片。

### 2.1 SH订单已经关闭或取消

如果订单状态为：

```text
已关闭（CLOSED）
或
已取消（CANCELLED）
```

则该SH已经退出APS活跃订单集合：

> **本次不再作为SH供给，也不再生成第二层源工厂履约需求。**

本轮业务已经确认：

> **同一SH完成目标工厂整单入库后，ERP会将该SH订单置为已关闭（CLOSED）。**

因此，SH完成目标侧履约后会自然退出后续排程，不存在“入库完成后从在途视图消失，又被APS误判为需要重新履约”的循环。

### 2.2 SH订单仍然活跃，且跨厂在途视图存在该SH号

如果：

```text
订单状态 = 活跃（OPEN）
且
ERP_InterplantInTransit_View中存在该SH号
```

则正式判断为：

> **该SH整单已经进入跨厂履行/在途状态。**

处理规则：

- 该SH继续作为目标侧的一份SH供给；
- 不再在第二层生成源工厂履约需求；
- 不使用视图中的在途数量去拆70/30；
- 不再追问其中多少件是ZP、BP、已到等小阶段。

即使视图中的Quantity与Order.Quantity不同，本轮V1也只使用：

> **“该SH号是否存在于在途视图”**

作为整单是否已经进入跨厂履行的判断依据，不根据Quantity做内部份额推导。

### 2.3 SH订单仍然活跃，但跨厂在途视图不存在该SH号

如果：

```text
订单状态 = 活跃（OPEN）
且
ERP_InterplantInTransit_View中不存在该SH号
```

则正式判断为：

> **该SH尚未进入跨厂履行，源工厂侧仍有整张SH的履约义务。**

第二层形成：

```text
一条完整的SH源厂履约需求（Demand）
RequiredQty = Order.Quantity
SourceFactoryId = Order.SourceFactoryId
```

这里特别强调：

> **形成RequiredQty = Order.Quantity的“源厂履约需求”，不等于直接下结论“必须新增生产Order.Quantity”。**

第二层继续执行源工厂供需承接（Pegging）：

```text
SH源厂生产需求
↓
优先由已有生产指示（PI）承接
↓
再根据生产指示位置（PI Position）判断该PI当前已经做到哪里
↓
仍有未被PI承接的缺口
↓
才形成新增生产需求
↓
进入1号位有限产能排程
```

因此：

> **`RequiredQty = Order.Quantity`表示整张SH的源厂生产需求数量，不等于新增生产数量。**

已有生产指示（PI）及其位置事实能够承接的部分，先由现有供给承接；只有最终缺口才形成新的生产需求。

这也解释了为什么当前业务可以安全采用：

```text
OPEN + 在途视图无SH号
→ 第二层生成一条RequiredQty = Order.Quantity的源厂生产Demand
```

而不再根据在途数量去计算所谓“未生产份额”。

### 2.4 视图返回0行是合法成功结果

正式规则改为：

> **跨厂在途视图正常查询成功后，返回0行属于合法成功结果。**

必须区分：

```text
查询/接口执行失败
≠
查询成功但0行
```

如果整个视图正常返回0行，则业务含义只是：

> **本次没有任何SH被ERP记录为处于跨厂在途状态。**

对于某一张活跃SH：

```text
视图中查不到该SH号
→ 尚未进入跨厂履行
→ 第二层形成源厂履约Demand
```

程序不得用“至少必须有1行”来判断接口是否成功。

### 2.5 当前真实ERP数据尚未接通时的调试方式

当前跨厂在途视图尚未正式供数，但未来会正式提供数据。

现阶段开发和联调可以直接按最终规则调试：

```text
视图中无SH记录
→ 按“尚未进入跨厂履行”分支测试

人工/模拟增加某个SH记录
→ 按“已经进入跨厂履行”分支测试
```

建议至少准备三类测试数据：

```text
SH001：OPEN + 视图无记录
→ 生成源厂履约Demand

SH002：OPEN + 视图有记录
→ 不生成源厂履约Demand

SH003：CLOSED
→ 无论视图有无记录，均退出APS活跃计算
```

真实ERP在途数据接入后，不需要改变业务算法，只需要把模拟事实替换成真实视图事实。

在上述判断确认“OPEN且尚未进入跨厂履行”后，进入下一计算层：

> **SH从“目标侧供给”转换成“源工厂履约需求”。**

第二层重新回答：

> 源工厂由哪些已有生产指示（PI）承接，以及仍有多少缺口需要新增生产。

正式表达：

```text
第一层：
顶层需求 → SH供给

第二层：
SH源厂生产需求
→ PI / PI Position / 新增生产
```

不能在第一层继续把“顶层需求 → SH → 源厂PI”直接揉成一次供需承接。

原因是两层的需求主体、供给池、数量余额、排序规则、分配关系都不同。

## 3. 第二层SH始终只是一条完整源厂履约需求

一张SH不按第一层多个需求分配份额拆成多条源厂生产需求。

例如：

```text
SH001 = 100件

第一层：
需求A占30
需求B占40
需求C占30
```

第二层仍然只形成：

```text
SH001源厂履约需求 = 1条完整需求
```

这里“完整需求”表示：

- 不按第一层多个需求分配份额拆成多条SH源厂需求；
- 不按在途/已到/ZP/BP状态拆成多条SH源厂需求；
- 同一SH号在第二层只形成一条完整源厂履约需求。

本轮进一步确认：

> **Order.Quantity就是该SH需要生产的权威数量。**

因此，在已经确认该SH**尚未进入跨厂履行、源厂侧仍需继续履约**的前提下：

```text
SH源厂履约需求数量（RequiredQty）
=
Order.Quantity
```

不再保留“Quantity可能只是原始开单总量、还需再确认当前剩余语义”的疑问。

同时继续严格禁止：

```text
Order.Quantity - TransitQty
Order.Quantity - ReceivedQty
Order.Quantity - ZP/BP Qty
```

等任何自行倒算“剩余待生产量”的做法。

## 4. 第二层SH优先级

### 4.1 没有被上层需求占用的SH

如果一张仍有效的SH当前没有被顶层需求占用，但ERP业务上仍存在生产/履约义务：

> 按SH自身业务优先级进入第二层。

### 4.2 承接了上层需求的SH

如果一张SH承接了多个上层需求：

> **整张SH源厂生产需求采用“SH自身优先关系”和“当前所有上层分配关系中最强业务优先关系”中的更强者。**

不能只给其中某一数量份额继承高优先级。

原因：

> 只要同一SH不能分批入库，高优先客户最终仍必须等待整张SH完成。

该规则需同步进入3号位的需求排序规则配置说明。

---

# 五、SH源工厂、目标工厂和排程域的最终语义

## 1. 现有FactoryId固定解释为目标工厂

当前Order中的FactoryId不进行全局重命名，避免大范围代码影响。

但业务语义必须在所有冻结文档中写死为：

> **目标工厂，即该订单最终入库/需求满足工厂。**

## 2. 新增SourceFactoryId

Order新增SourceFactoryId。

业务含义：

> **实际负责生产/履行该订单的工厂。**

普通订单：

```text
SourceFactoryId = FactoryId
```

跨厂SH：

```text
SourceFactoryId ≠ FactoryId
```

例如：

```text
SH001
FactoryId       = TJ   // 目标工厂
SourceFactoryId = BJ   // 源工厂
```

## 3. SH第二层不能沿用第一层目标工厂排程域

第一层SH作为目标侧供给时，按目标工厂参与目标侧供需承接。

第二层SH转换成源厂生产需求时：

> **必须以源工厂（SourceFactoryId）重新确定源厂排程域（Domain）。**

不得继续沿用第一层目标工厂的排程域标识（DomainKey）。

同时必须防止另一个误解：

> **不要为了第二层源厂需求去改写原Order的目标侧FactoryId，也不要把原Order.DomainKey永久改成源厂DomainKey。**

原SH订单仍然代表“目标工厂要接收的跨厂订单”；第二层产生的是本次排程运行中的**派生源厂需求**，该派生需求在内存/运行上下文中重新归域。

2号位应根据当前有效排程域定义（DomainDefinition），使用SourceFactoryId + ProductFamily或当前排程域定义实际要求的维度重新归域。具体落在哪个现有Demand/LogicalProductionDemand运行对象字段，由《集成接口设计》和2号位实施包对齐，不因此新增第二套永久Order主表。

---

# 六、SH时间的最终业务口径

## 1. SH订单交货期不是采购式ETA

SH订单里的交货期表示：

> **目标工厂希望什么时候收到这张SH。**

不是采购单那种供应商预计到货时间（ETA）。

因此不得把订单交货期直接作为SH真实可用时间。

## 2. 源工厂要求完成时间

若目标工厂要求到货时间为T，跨厂运输提前期为LT，则：

```text
源工厂要求完成时间
=
T - LT
```

这只是排程目标。

## 3. 源工厂真实完成时间由1号位计算

源工厂真正什么时候能把整张SH需要的数量生产完成：

> **由1号位有限产能排程计算。**

2号位不能自行估算。

## 4. SH厂间订单型的标准跨厂提前期由3号位规则参数治理

本轮只裁决**厂间出荷指示（SH）这一类厂间订单型业务**。

对于SH使用的标准跨厂运输/检验/转运提前期：

> **由3号位参数集（ParameterSet）治理并在本次排程运行开始时冻结。**

如果ERP提供某张SH的真实发运、真实到达或其它更强物流事实：

> 由5号位作为事实提供，不能被标准参数覆盖。

特别说明：

> **本轮不得借SH修订把生产指示（PI）大工艺接续型原有的工厂/Stage提前期事实归属一起改掉。**

生产指示（PI）跨厂的现有Stage/Factory提前期事实继续按当前最高冻结文档执行；如未来需要统一LT治理，必须另行裁决。

## 5. 同排程域与跨排程域的处理

### 5.1 源工厂和目标工厂属于同一个排程域

2号位构建同一个排程域求解请求时，应把源厂生产需求、目标厂后续需求、跨厂运输提前期、前后依赖一起交给1号位。

由1号位在一次单排程域有限产能求解中形成完整时间链。

不应出现：

```text
1号位先算源厂
→ 回2号位
→ 再把同一个排程域重新交给1号位
```

这种人为切断。

这里冻结的是**业务和求解边界原则**，不是在本指导说明中直接发明新的接口字段。具体如何在现有排程域求解请求（DomainSolveRequest）中表达“源厂生产完成→跨厂提前期→目标厂后续”的依赖，必须在修订《集成接口设计》和1号位/2号位实施包时基于现有接口能力增量补齐，优先复用现有物料数量—时间约束、工艺/依赖关系和逻辑生产需求，不建设第二套求解器或新的跨厂排程平台。

### 5.2 源工厂和目标工厂属于不同排程域

继续沿用现有跨排程域串行求解规则：

```text
源厂Domain
↓
1号位计算整张SH源厂真实准备完成时间
↓
2号位跨Domain编排
↓
源厂真实准备完成时间 + 跨厂运输提前期
↓
形成目标Domain的SH可用时间约束
↓
目标Domain再调用1号位
```

其中：

- 1号位拥有有限产能时间真相；
- 2号位只负责跨Domain编排和结果传播；
- 3号位提供提前期参数；
- 5号位不生成生产计划ETA。

---

# 七、SH跨厂小状态的最终口径

V1不再按以下状态拆SH供给：

- 在途（Transit）；
- 已到（Received）；
- ZP；
- BP。

正式统一为：

> **同一SH跨厂履行过程中的事实状态。**

这些状态如果ERP提供，可以保留用于查询、追溯、异常说明、数据质量核对。

但不得：

- 生成多份供给；
- 生成多个分配关系；
- 生成70/30数量切片；
- 生成不同数量对应的不同可用时间；
- 通过状态数量倒算SH未生产量。

---

# 八、当前跨厂在途视图的处理原则

跨厂在途视图（ERP_InterplantInTransit_View）本轮：

> **不要求结构性变更。**

继续保留当前视图定义和已有字段。

该视图在SH主链中的正式业务职责只有一个核心判断：

> **对当前活跃（OPEN）的SH，判断该SH号是否已经存在于跨厂在途视图中。**

判断规则：

```text
OPEN + 视图有该SH号
→ 已进入跨厂履行
→ 不生成第二层源厂履约Demand

OPEN + 视图无该SH号
→ 尚未进入跨厂履行
→ 第二层生成一条RequiredQty = Order.Quantity的源厂履约Demand

CLOSED / CANCELLED
→ 退出APS活跃计算
```

## 1. 0行属于合法成功

只要数据库查询/接口调用本身正常成功：

> **返回0行完全合法。**

不能把：

```text
0 rows
```

当成：

```text
接口失败
```

接口失败应依据连接异常、执行异常、超时、刷新失败等技术结果判断，而不是依据返回行数判断。

## 2. 在途视图Quantity不参与SH内部数量拆分

即使：

```text
Order.Quantity = 100
在途视图Quantity = 70
```

也只表示：

> 该SH号已经进入跨厂履行。

不得推导：

```text
70件在途
30件未生产
```

也不得生成两个可用时间。

## 3. 当前未正式供数期间允许直接按最终逻辑调试

当前真实数据尚未接入时，可以：

- 保持视图为空，用于测试“OPEN + 无在途记录”的源厂履约分支；
- 人工加入模拟SH记录，用于测试“OPEN + 有在途记录”的在途分支。

未来ERP真实数据接入后，业务代码不改变，只替换数据来源。

## 4. 视图定位不变

该视图统一定位为：

> **跨厂物流事实，不是SH主档，不是SH总量权威源，也不是普通需求与SH分配关系的权威源。**

SH通过SH号（即OrderNo）与视图中的同一SH事实关联。

# 九、数据库字段和订单ETL必须修改

这是本轮唯一明确需要新增的Order业务字段。

## 1. ERP订单暂存层

建议新增：

```text
SourceFactoryCode
```

用途：

> 保存ERP提供的源工厂代码，用于后续映射SourceFactoryId。

如果当前接入适配器可以直接把源工厂代码送入Canonical，也可以不额外扩大其它中间DTO；但必须保证源工厂代码在进入Order前可追溯。

## 2. 订单标准化层

订单标准化表（Order_Canonical）新增：

```text
SourceFactoryCode
```

普通订单：

> 如果ERP没有单独源工厂代码，可在标准化时使用现有FactoryCode。

跨厂SH：

> 必须使用真实源工厂代码，不允许直接把目标FactoryCode当成源工厂兜底。

## 3. 正式订单表

订单表（Order）新增：

```text
SourceFactoryId
```

并按当前Factory主数据方式建立关联。

正式语义：

```text
FactoryId       = 目标工厂
SourceFactoryId = 源工厂
```

## 4. 不新增SourceWarehouseProcessCode

正式Order不增加SourceWarehouseProcessCode。

相关ERP仓库/工序代码如果只是为了识别源工厂，可在ETL内部使用后丢弃，不进入V1正式Order数据模型。

## 5. 同步存储过程

必须同步修改：

- 订单校验与标准化存储过程；
- Order_Canonical写入；
- Order分区表同步过程；
- 对应Entity / DTO / Mapper / Repository。

特别禁止：

> 对跨厂SH在SourceFactoryCode缺失时自动使用目标FactoryCode作为源工厂。

跨厂SH缺失源工厂应形成明确数据问题，避免把源厂生产错误排到目标工厂。

普通非跨厂订单可使用：

```text
SourceFactoryId = FactoryId
```

作为统一语义。

---

# 十、需要修改的正式冻结文档

以下按优先级执行。

---

# P0：最高业务与主流程文档，必须先改

## 1. 《APS V1最终全部流程与业务基线 v1.4》

**建议目标版本：v1.5**

必须新增“跨厂SH单一供给、分层需求与源/目标工厂”专项章节。

至少写清：

1. SH识别：订单类型（OrderType）=销售订单（SALES_ORDER）；客户分类（CustomerSegment）=“跨厂”（INTER_FACTORY）；订单号（OrderNo）=SH号。
2. 第一层普通顶层需求池必须**排除**CustomerSegment=INTER_FACTORY的SH订单，避免SH同时成为普通顶层Demand和SH Supply。
3. SH主档来自2号位Order，不建设第二套SH主档。
4. 第一层普通Demand ↔ SH允许多对多，由2号位运行时供需承接（Pegging）形成。
5. 第一层到SH结束。
6. SH第二层采用“订单状态 + 在途视图是否存在SH号”的整单判定：OPEN且视图无该SH号→形成一条源厂履约Demand；OPEN且视图有该SH号→不再生成源厂履约Demand；CLOSED/CANCELLED→退出活跃计算。
7. 第二层SH形成一条完整源厂生产Demand，RequiredQty=Order.Quantity；第二层继续按“PI → PI Position → 新增生产”的冻结顺序推进，只有未被已有PI承接的缺口才形成新增生产。
6. 同一SH整单一次入库。
7. 第二层SH优先级采用自身与上层最强业务优先关系中的更强者。
8. FactoryId = 目标工厂；SourceFactoryId = 源工厂。
9. SH第二层按SourceFactoryId重新归域。
10. SH不拆Transit/Received/ZP/BP数量和时间。
11. SH订单交货期不是实际ETA。
12. 1号位负责真实SourceReadyTime。
13. 3号位负责跨厂LT参数。
14. 同Domain一次求解；跨Domain由2号位串行传播。

**必须删除或覆盖：**

- “5号位提供SH权威总量”；
- “普通Demand↔SH由5号位提供固定Document关系”；
- “Transit/Received/ZP/BP可分别决定SH数量/时间”。

## 2. 《APS Pegging供需承接与分层计算业务说明 v1.3》

**建议目标版本：v1.4**

这是本轮最重要的专项修改文档。

重点重写“第二层厂间SH生产需求”章节。

必须明确：

```text
第一层：
顶层Demand → SH Supply
```

与：

```text
第二层：
SH Demand → 源厂Supply
```

是两次独立的供需承接。

必须增加：

- 第一层普通顶层Demand池排除CustomerSegment=INTER_FACTORY的SH订单；
- 第一层SH供给（Supply）与普通Demand多对多；
- 第二层一SH一Demand；
- 第二层SH池的形成条件：仅OPEN且跨厂在途视图中不存在该SH号时生成一条源厂履约Demand；OPEN且视图存在SH号时不生成；CLOSED/CANCELLED退出活跃计算；
- SH自身优先级；
- 上层最强优先关系继承；
- SH整单入库导致优先级按整单继承；
- 第二层使用SourceFactoryId；
- 第二层重新归Domain；
- 未生产不是第三种Supply；
- 禁止Transit→Received→Unproduced三段Pegging。

**必须修正原有“上层触发份额继承、剩余份额按SH自身优先”表述：**

由于同一SH不能分批入库，不再按SH内部数量份额分别继承优先级。

改为：

> **一张SH只形成一条完整源厂Demand；如果承接上层Demand，则整张SH采用当前上层Allocation中最强业务优先关系与SH自身优先关系中的更强者。**

## 3. 《APS 核心排产全流程走查 V3.20》

**建议目标版本：V3.21**

端到端流程必须改成清楚的两层角色切换：

```text
目标侧Demand
↓
SH Supply
↓
当前层Pegging结束

下一计算层
↓
SH Source Demand
↓
源厂Pegging
↓
LogicalProductionDemand
↓
1号位有限产能
```

必须增加源/目标工厂字段流：

```text
Order.FactoryId
= 目标工厂

Order.SourceFactoryId
= 源工厂
```

并说明：

- CustomerSegment=INTER_FACTORY的SH订单不作为普通SALES_ORDER进入第一层顶层Demand池；
- 第一层使用目标工厂；
- 第二层只有在SH为OPEN且跨厂在途视图不存在该SH号时才生成源厂履约Demand；
- 第二层使用源工厂；
- 第二层不能沿用目标DomainKey；
- 同Domain和跨Domain的求解方式不同；
- 跨Domain才经过2号位结果传播。

端到端流程图中不得再出现“SH → Transit → Received → Unproduced”作为供给生命周期。

## 4. 《APS 有限产能排产与滚动90天计划业务说明 v1.3》

**建议目标版本：v1.4**

原文已经规定1号位拥有资源/时间真相、跨Domain传递Quantity-Time、2号位负责跨Domain编排。

本轮只需要增量补清SH特殊时间语义：

1. SH订单交货期是要求时间，不是实际ETA；
2. 源厂SH Demand的要求完成时间 = 目标要求到货时间 - 跨厂LT；
3. 同一SH整单一次入库，因此整张SH的SourceReadyTime取整单全部生产义务满足后的时间；
4. 同Domain时，由1号位在同一Domain求解中处理源厂生产→跨厂LT→目标厂下游依赖；
5. 跨Domain时，由上游1号位输出SourceReadyTime，2号位加冻结LT形成下游输入；
6. 不将SH分成多个物流状态Quantity-Time Slice。

该文档不要重新打开普通跨Domain Quantity-Time通用规则。

应明确：

> **“SH整单一次入库”是SH单据业务约束，不代表所有普通跨Domain物料都取消Quantity-Time分段。**

---

# P1：技术契约与数据库文档，必须同步修改

## 5. 《APS 集成接口设计规范 v1.30》

**建议目标版本：v1.31**

重点修改三部分。

### 5.1 订单接口

增加源工厂字段SourceFactoryCode，订单运行对象增加SourceFactoryId。

明确：

- FactoryId = 目标工厂；
- SourceFactoryId = 源工厂；
- 普通订单两者相同；
- SH允许不同。

### 5.2 2↔5跨厂事实接口

删除“5号位完整SH供给事实”的过重契约。

5号位不再被要求提供：

- SH权威总量；
- 普通Demand↔SH关系；
- SH生产完成ETA。

跨厂在途事实接口继续保持现有视图，不强制本轮改结构。

正式业务判断直接采用：

```text
对当前OPEN的SH：
在ERP_InterplantInTransit_View中存在该SH号
→ 已进入跨厂履行

不存在该SH号
→ 尚未进入跨厂履行
```

本轮不要求提供70/30等内部数量切片，也不要求新增ZP/BP细状态字段。

查询成功但0行属于合法成功结果。当前真实视图尚未供数时允许用空视图和模拟SH记录进行程序联调；未来真实数据接入后沿用同一判断逻辑。

### 5.3 1↔2排程接口

补充同Domain SH跨厂时间依赖的输入语义。

不建议新建第二套接口。

优先利用现有：

- 物料时间约束（Material Constraints）；
- 工艺/依赖关系（Routing / Dependency）；
- 逻辑生产需求（LogicalProductionDemand）；
- 冻结策略参数快照。

明确：

> 1号位不用知道SH在ERP里的复杂状态，只需要收到正确的源厂生产需求、目标侧依赖和跨厂LT约束。

## 6. 《APS 数据架构与防腐层设计方案 v1.41》

**建议目标版本：v1.42**

必须增加：

### 6.1 SH主档边界

```text
2号位Order
= SH业务主档真相

5号位ODS
= SH跨厂物流事实
```

禁止再把5号位写成“完整SH Supply主档Owner”。

### 6.2 工厂双语义

```text
FactoryId       = Target Factory
SourceFactoryId = Source Factory
```

并说明Order仍只新增一个字段，不建设新的跨厂订单Header平台。

### 6.3 不新增的结构

本轮禁止新增：

- SH Header表；
- SH Supply状态表；
- Demand↔SH固定关系表；
- Transit Supply表；
- Received Supply表；
- SH状态Allocation表。

### 6.4 5号位视图保持

ERP_InterplantInTransit_View保持现状。

字段不完整时可空，不以此阻塞Order主链字段补充。

## 7. 《APS 数据库字段说明文档 v5.1.5》

**建议目标版本：v5.1.6**

必须修改：

### Order字段

增加SourceFactoryId。

业务说明：

> 实际负责生产/履约的源工厂。

现有FactoryId字段说明改清楚：

> 最终入库/需求满足的目标工厂。

### Order_Canonical

增加SourceFactoryCode。

### ERP_Order_Staging

建议增加SourceFactoryCode，用于ERP原始事实透传。

### Quantity

对跨厂SH明确写入：

> **现有Order.Quantity就是该SH需要生产并整单履约的权威数量。**

第二层SH源厂生产需求直接使用：

```text
RequiredQty = Order.Quantity
```

不新增“SH剩余生产量”字段，不用Transit/Received/ZP/BP数量倒算。

### CustomerSegment

明确：

```text
订单类型（OrderType） = 销售订单（SALES_ORDER）
客户分类（CustomerSegment） = “跨厂”（INTER_FACTORY）
```

用于识别厂间出荷指示（SH）。

不能再写OrderType = SH。

### SH Supply说明

删除：

- Received独立Supply；
- ZP/BP独立Supply；
- Transit/Received不同Quantity-Time；
- 5号位提供普通Demand↔SH关系。

## 8. 《APS 数据库表结构设计 DDL v5.1.5》

**建议目标版本：v5.1.6**

这是本轮需要真实改DDL的文档。

必须实施：

### 8.1 ERP_Order_Staging

新增：

```sql
SourceFactoryCode NVARCHAR(50) NULL
```

### 8.2 Order_Canonical

新增：

```sql
SourceFactoryCode NVARCHAR(50) NULL
```

### 8.3 Order

新增：

```sql
SourceFactoryId INT NULL
```

并按当前Factory主数据方式建立关联。

### 8.4 ETL过程

修改：

- sp_ValidateAndPromoteOrders；
- sp_SyncOrdersToPartitionTable；
- 相关MERGE / INSERT / UPDATE。

保证：

```text
SourceFactoryCode
→ Factory
→ SourceFactoryId
```

### 8.5 普通订单兜底

非跨厂普通订单：

```text
SourceFactoryId = FactoryId
```

### 8.6 跨厂SH红线

对于CustomerSegment = 跨厂，如果SourceFactory无法识别：

> 不允许静默把目标FactoryId写入SourceFactoryId。

应登记数据Issue或阻断该SH源厂生产链。

### 8.7 明确不新增

不新增SourceWarehouseProcessCode，也不新增任何SH专用Header或状态表。

---

# P1：各号位实施包需要修改

## 9. 《APS V1 1号位有限产能排程开发实施包 v1.2》

**建议目标版本：v1.3**

1号位职责不改变，只补SH场景。

增加：

- 同Domain时1号位一次求解源厂→跨厂LT→目标厂后续时间链；
- SH整单SourceReadyTime语义；
- SH不允许因内部生产批次形成多个目标侧可用片段；
- 不重新判断SH是哪个Demand承接；
- 不读取ERP跨厂在途原始视图。

不用新增：

- SH Solver；
- Logistics Solver；
- MultiDomain Solver。

## 10. 《APS V1 2号位增量开发实施包 v1.5》

**建议目标版本：v1.6**

这是本轮实现修改最重的号位文档。

必须增加/修改：

1. SH识别：SALES_ORDER + CustomerSegment=跨厂 + OrderNo=SH号；
2. FactoryId解释为目标工厂；
3. 新增SourceFactoryId消费；
4. 第一层Demand↔SH多对多Allocation；
5. 删除Transit/Received双入池；
6. 删除Transit→Received→Unproduced三段消费；
7. 不通过在途数量倒算未生产量；
8. 下一层创建一条完整SH源厂Demand；
9. 第二层使用SourceFactoryId重新归Domain；
10. 第二层SH优先级整体继承最强上层业务优先关系；
11. 同Domain与跨Domain不同的时间编排方式；
12. 跨Domain只传播1号位真实完成结果，不自行估算生产完成时间；
13. 5号位在途视图字段缺失允许空/Mock，不阻塞结构整改。

## 11. 《APS V1 3号位规则、参数、认证权限与运行生命周期开发实施包 v1.3》

**建议目标版本：v1.4**

只需补规则参数职责，不改变运行Owner。

增加：

### 11.1 SH第二层优先级规则

规则配置需要能表达：

> SH自身业务优先关系与上层最强业务优先关系取更强者。

不要求3号位逐笔计算。

仍然是：

> 3号位配置和冻结，2号位运行时执行。

### 11.2 SH厂间订单型跨厂运输提前期

明确SH厂间订单型使用的标准跨厂运输/检验/转运提前期属于：

> 参数集（ParameterSet）治理内容。

3号位维护参数，运行（Run）开始时冻结。

5号位如果提供某张SH真实发运/到达事实，则真实事实优先。

本轮只修SH，不修改生产指示（PI）大工艺接续型现有Stage/Factory提前期事实归属。

## 12. 《APS V1 5号位复杂业务事实、ODS与业务接口接入开发实施包 v1.3》

**建议目标版本：v1.4**

当前文档中“厂间订单型”章节需要明显收缩。

删除5号位必须标准化：

- SH权威总量/剩余；
- 普通Demand↔SH Document关系；
- SH生产AvailableTime；
- Transit/Received数量去重后形成Supply。

保留：

- SH号相关跨厂ERP事实；
- 跨厂在途视图中SH号存在性事实；
- 当前在途事实；
- 真实发运/到达事实（ERP有则提供）；
- 源/目标工厂事实如果当前视图有则提供，没有可空；
- 严格绑定证据（ERP真实存在时）；
- 数据质量Issue。

增加一句：

> **SH业务主档、权威数量、目标工厂、源工厂正式运行语义以2号位Order为主；5号位跨厂在途视图不要求在本轮升级成完整SH主档视图。**

同时修正：

> 标准跨厂LT参数由3号位治理；5号位只提供ERP真实物流事实。

---

# P2：验收、审核和治理索引文档

## 13. 《APS V1 0号位总体项目验收包 v1.2》

**建议目标版本：v1.3**

增加SH专项验收项：

- SH识别正确；
- INTER_FACTORY SH订单不作为普通SALES_ORDER重复进入第一层顶层Demand池；
- FactoryId/SourceFactoryId语义正确；
- 普通Demand↔SH多对多；
- OPEN且在途视图存在SH号时不得再次生成第二层源厂履约Demand；
- OPEN且在途视图不存在SH号时第二层RequiredQty必须等于Order.Quantity，并按“PI → PI Position → 新增生产”的冻结顺序继续Pegging；
- CLOSED/CANCELLED SH必须退出APS活跃计算；
- 查询成功返回0行必须按合法成功处理；
- SH第二层一单一Demand；
- SH第二层重新源厂归域；
- SH整单一次入库；
- 不存在Transit/Received/ZP/BP重复Supply；
- 不按70/30拆时间；
- 1号位真实完成时间；
- 3号位LT参数；
- 5号位不重复建设SH主档。

## 14. 《APS V1代码审核检查基线》

建议增加SH审核红线。

### 2号位禁止

- #TRANSIT / #RECEIVED分别入池；
- 三段式消费；
- 用TransitQty倒推未生产量；
- 使用目标FactoryId作为SH第二层源厂；
- 沿用第一层DomainKey；
- 普通Demand↔SH依赖5号位固定关系。

### 5号位禁止

- 新建完整SH主档平台；
- 新建普通Demand↔SH关系表；
- 将ZP/BP/Received变成独立Supply；
- 计算源厂有限产能完成ETA。

## 15. 《APS V1 Supply / PI Position专项冻结收口确认单》

如果该文档仍保留在当前有效项目来源，只修改SH章节。

PI部分继续保持：

> **ProductionInstruction唯一Supply + PI Position。**

不得借SH修订重新打开PI Position业务。

## 16. 《APS V1当前有效冻结基线索引》

全部正式文档完成新版本后：

- 更新当前有效版本号；
- 将2026-09-08 SH修改指导标记为“已被2026-09-09 v2.0替代”；
- 将本轮实际修订后的新版本列入当前有效基线。

## 17. 《APS V1冻结基线变更记录》

新增2026-09-09 SH专项变更记录。

至少记录：

- 变更原因；
- 真实ERP新增事实；
- 受影响文档；
- 新增SourceFactoryId；
- 废止5号位完整SH主档契约；
- 废止普通Demand↔SH固定Document关系；
- SH整单一次入库；
- 第二层整单优先级继承；
- 0908指导被替代。

## 18. 《APS V1最终文档目录与引用规则》

如果正式版本号和文件名发生升级，同步更新文档目录中的当前版本引用。

不改变引用治理规则本身。

---

# 十一、不需要修改或不应重新打开的文档/业务

## 1. 4号位页面实施包

本轮没有新增新的独立页面业务，不要求修改4号位核心实施包。

未来如页面要展示源工厂、目标工厂、SH跨厂状态，可在页面需求中补充，但不属于本轮冻结业务闭环的前置条件。

## 2. 生产指示PI跨厂业务

本轮SH修订不得改变：

> **PI仍是一份ProductionInstruction Supply，跨厂通过PI Position的INTERPLANT_TRANSIT表达。**

不新增Transit Supply。

## 3. 跨厂在途视图

本轮不要求修改ERP_InterplantInTransit_View结构。

真实数据后续由0号位继续与ERP团队对齐。

字段暂缺可以为空或Mock。

## 4. 不建设物流有限产能

继续禁止：

- ShippingTask；
- 车辆Task；
- 月台Task；
- 物流Solver。

跨厂运输仍以提前期约束表达。

---

# 十二、文档修改时必须防止的几个典型错误

## 错误1：把SH识别写成OrderType=SH

错误。

正确：

```text
订单类型（OrderType） = 销售订单（SALES_ORDER）
客户分类（CustomerSegment） = “跨厂”（INTER_FACTORY）
订单号（OrderNo） = SH号
```

## 错误2：因为OrderNo就是SH号，就认为Demand↔SH是一对一

错误。

正确：

> OrderNo只解决SH自身身份；普通Demand↔SH的供需承接（Pegging）关系仍允许多对多。

---

## 错误2A：因为SH也是SALES_ORDER，就把它当普通顶层Demand

错误。

正确：

> CustomerSegment=INTER_FACTORY的SH订单从普通第一层SALES_ORDER Demand池中排除；它在目标侧作为SH供给，在尚未进入跨厂履行时于第二层作为源厂SH生产需求。

## 错误3：让5号位提供普通Demand↔SH固定关系

错误。

正确：

> 普通关系由2号位运行时Pegging产生；5号位只提供真实Strict Binding Evidence。

## 错误4：用在途数量计算未生产量

错误。

例如：

```text
OrderQty=100
TransitQty=70
```

不得推导未生产=30。

当前事实不支持。

## 错误5：因为源厂内部可以分批完成，就把同一SH分批可用

错误。

同一SH必须整单一次入库。

## 错误6：第二层仍使用目标FactoryId

错误。

第二层SH生产需求必须使用SourceFactoryId。

## 错误7：第二层继续沿用第一层DomainKey

错误。

SH角色从目标侧Supply切换为源厂Demand后，必须重新按SourceFactoryId归域。

## 错误8：让2号位估源厂生产完成时间

错误。

真实完成时间属于1号位有限产能结果。

## 错误9：让5号位维护默认跨厂LT

错误。

标准可配置LT属于3号位参数治理；5号位只提供ERP真实物流事实。

## 错误10：借本轮修订重新设计PI

错误。

本轮只修SH。

---

# 十三、建议统一写入正式文档的标准业务表述

建议各正式冻结文档按各自层级引用下面这组标准口径，不必逐字完全一致，但业务语义不得变化。

> **厂间出荷指示（SH）在APS订单表中属于销售订单（SALES_ORDER），通过客户分类（CustomerSegment）=“跨厂”（数据库标准值 `INTER_FACTORY`）识别，订单号（OrderNo）即SH号。现有订单工厂（FactoryId）统一表示目标工厂，新增源工厂（SourceFactoryId）表示实际负责生产/履约的工厂。普通订单源工厂与目标工厂相同；跨厂SH允许不同。**
>
> **第一层普通顶层需求池必须排除CustomerSegment=INTER_FACTORY的SH订单，避免同一SH同时作为普通顶层Demand和SH Supply重复建模。SH作为目标侧供给（Supply）时始终保持一个供给身份。普通顶层需求与SH之间允许多对多，由2号位供需承接（Pegging）运行时形成分配关系（Allocation）。在途、已到、ZP、BP等仅为同一SH跨厂履行过程中的事实状态，不形成独立供给，也不拆分SH数量和可用时间。**
>
> **当前层需求承接到SH后，该层供需承接结束。对每张SH先看订单状态：CLOSED/CANCELLED直接退出活跃计算；OPEN时再判断跨厂在途视图中是否存在该SH号。存在则表示整张SH已经进入跨厂履行，不再生成源厂履约需求；不存在则在第二层形成一条RequiredQty=Order.Quantity的完整源厂履约需求，并按SourceFactoryId重新归入源厂排程域继续供需承接。该RequiredQty先由已有生产指示（PI）承接，并消费其生产指示位置（PI Position）；只有未被已有PI承接的缺口才形成新增生产。SH作为源厂需求不按上层多个分配份额拆分。**
>
> **同一SH号在ERP中只能整单一次入库；如需分批交货，必须拆成多个SH号。因此同一SH在目标侧只有一个整单可用时间。SH订单交货期表示目标工厂业务要求时间，不是采购式ETA。源厂真实准备完成时间由1号位有限产能排程计算；标准跨厂运输提前期由3号位参数治理；跨排程域时由2号位负责将上游真实完成结果向下游传播。**
>
> **5号位继续负责ERP跨厂物流事实和必要数据质量证据，但不重复建设SH主档、不提供普通Demand↔SH固定关系、不负责源厂有限产能完成时间。现有ERP跨厂在途视图本轮不要求结构性变更；对OPEN SH，以视图中是否存在该SH号作为整单是否已进入跨厂履行的判断依据。查询成功0行属于合法成功；当前未正式供数期间可使用空视图和模拟数据联调。**

---

# 十四、交给其它对话修改时必须遵守的自检约束

本文件已经对照当前最高冻结业务文档、排程域（Domain）专项、2号位实施包、字段说明和DDL进行二次交叉检查。为防止另一个对话“按字面机械修改”造成新漂移，正式修改时还必须遵守以下约束：

## 1. 不得把本文件当成一份新系统设计书

本文件的作用是：

> **指出现有冻结文档哪些地方需要按最新SH业务事实修正。**

另一个对话必须以“当前有效冻结文档原文 + 本说明”进行增量修改，不能脱离母版从头重写。

## 2. 版本号仅为建议

本文中“建议目标版本：v1.x / V3.x”等仅用于说明升版方向。

正式生成文件时必须先读取《当前有效冻结基线索引》和当前项目来源中的真实最新版本，再顺延版本号，不能只按本说明机械命名。

## 3. 不得误伤生产指示（PI）口径

本轮只修厂间出荷指示（SH）。

生产指示（PI）的：

- 单一供给身份；
- 生产指示位置（PI Position）；
- 下一工序上下文（NextOperationContext）；
- 跨厂位置（INTERPLANT_TRANSIT）；

继续按当前最高冻结文档执行。

## 4. SH第二层“1条完整需求”的数量语义已经确认

本轮已经由业务确认：

> **Order.Quantity就是该SH需要生产并整单履约的权威数量。**

因此另一个对话修改正式冻结文档时，应直接写清：

```text
SH第二层源厂生产需求数量（RequiredQty） = Order.Quantity
```

不得再保留“Quantity是否只是原始开单总量”“是否还要计算当前剩余生产量”的开放问题。

需要继续保留的红线只有一条：

> **不得使用在途、已到、ZP/BP等物流状态数量去扣减或反推Order.Quantity。**

## 5. 跨厂在途视图0行语义已经确认

本轮已经确认：

> **跨厂在途视图查询成功返回0行属于合法成功。**

对当前OPEN的SH：

```text
视图存在该SH号
→ 已进入跨厂履行
→ 不生成第二层源厂履约Demand

视图不存在该SH号
→ 尚未进入跨厂履行
→ 生成一条RequiredQty=Order.Quantity的源厂履约Demand
```

但另一个对话必须继续写清：

> **“生成Order.Quantity的履约Demand”不等于“新增生产Order.Quantity”。**

该Demand仍然先进入源厂Pegging，并严格沿用既有“PI → PI Position → 新增生产”顺序；只有未被已有PI承接的缺口才成为新增生产。

## 6. 第二层重新归域不改写原SH订单身份

原SH订单仍以目标工厂为业务归属事实。

第二层源厂生产需求在运行时使用SourceFactoryId重新归域。

不得为了源厂排产把原Order.FactoryId改成源工厂，也不得永久覆盖原Order.DomainKey。

## 7. 同Domain规则只冻结“不能人为拆成两次Solver”

具体接口字段必须基于现有1↔2接口增量修改。

禁止另一个对话因为本说明新增：

- 第二套跨厂Solver；
- ShippingTask；
- 跨厂中间PlanVersion；
- 新的“层平台”；
- 永久SH Demand表；

除非当前正式文档已有明确要求。

---

# 十五、最终论证后的额外确认

本轮最后一次交叉论证又发现并修正了一处容易造成业务漂移的问题：

> **第二层SH源厂Demand不能自行扩展为“源工厂普通库存 → PI → 新增生产”。**

当前最高冻结的SH源厂承接顺序一直是：

```text
PI
→ PI Position
→ 新增生产
```

因此本文件正式保持这一顺序，不新增源工厂普通库存自动承接SH的路径。

本轮最终确认后，以下链路之间已经不存在业务冲突：

1. **身份链**：SH是SALES_ORDER中的INTER_FACTORY订单，OrderNo即SH号；
2. **顶层需求链**：SH订单本身从普通第一层SALES_ORDER Demand池排除；
3. **目标侧供给链**：一张SH只有一个Supply身份，可与普通Demand形成多对多Allocation；
4. **状态链**：OPEN + 在途视图有SH号=已进入跨厂履行；OPEN + 无SH号=尚未进入；CLOSED/CANCELLED退出；
5. **数量链**：尚未在途时第二层RequiredQty=Order.Quantity，不做70/30扣减；
6. **源厂承接链**：PI → PI Position → 新增生产；
7. **工厂链**：FactoryId=目标工厂，SourceFactoryId=源工厂；
8. **排程域链**：第二层源厂Demand按SourceFactoryId重新归域，不改写原Order目标侧身份；
9. **时间链**：源厂实际生产完成时间由1号位有限产能产生，SH标准跨厂提前期由3号位参数治理，跨Domain由2号位传播；
10. **结束链**：目标厂整单入库后ERP将SH置为CLOSED，次日自然退出APS活跃集合；
11. **事实层职责**：5号位只提供ERP跨厂事实，不建设第二套SH主档或普通Demand↔SH关系。

除“真实ERP在途视图最终具体供哪些可选时间字段”需要按ERP最终接口事实落地外，本轮SH业务主模型不再存在需要另一个对话自行裁决的开放设计项。时间字段缺失时不得由文档修订人员自行发明新的采购式ETA规则。

---

# 十六、建议文档修改顺序

为避免再次出现“下层文档先改、上层业务后改”的循环，建议严格按以下顺序执行：

```text
第一步
最终全部流程与业务基线
        ↓
第二步
Pegging业务说明
有限产能业务说明
        ↓
第三步
核心排产全流程走查
        ↓
第四步
集成接口设计
数据架构与防腐层
        ↓
第五步
数据库字段说明
DDL
        ↓
第六步
1号位 / 2号位 / 3号位 / 5号位实施包
        ↓
第七步
0号位验收包
代码审核检查基线
        ↓
第八步
当前有效冻结基线索引
冻结基线变更记录
最终文档目录
```

完成每一步以后再进入下一层，避免后面的技术文档再次把旧业务带回去。

---

# 十七、本轮完成后的最终业务闭环

修订完成后，跨厂SH主链应唯一理解为：

```text
订单表
OrderType = SALES_ORDER
CustomerSegment = 跨厂（INTER_FACTORY）
OrderNo = SH号
FactoryId = 目标工厂
SourceFactoryId = 源工厂
        ↓
SH订单本身不进入第一层普通SALES_ORDER Demand池
        ↓
第一层
其它目标侧普通Demand
        ↓
2号位Pegging
        ↓
SH单一Supply
        ↓
当前层结束
        ↓
判断Order状态
        ├─ CLOSED/CANCELLED
        │      → 退出APS活跃计算
        │
        └─ OPEN
              ↓
        查询跨厂在途视图是否存在该SH号
              ├─ 有
              │    → 已进入跨厂履行
              │    → 不生成源厂履约Demand
              │
              └─ 无
                   ↓
             下一计算层
             SH整单源厂履约Demand
             RequiredQty = Order.Quantity
                   ↓
             源厂Pegging
                   ├─ PI / PI Position承接
                   └─ 只有缺口进入新增生产
        ↓
按SourceFactoryId重新归Domain
        ↓
源厂Pegging
        ↓
PI / PI Position / 新增生产
        ↓
LogicalProductionDemand
        ↓
1号位有限产能
        ↓
整张SH SourceReadyTime
        ↓
跨厂LT
        ↓
目标侧SH整单AvailableTime
        ↓
目标工厂后续有限产能
```

整个过程中始终禁止恢复：

```text
Transit Supply
→ Received Supply
→ Unproduced Supply
```

也禁止恢复：

```text
70件一个AvailableTime
+
30件另一个AvailableTime
```

---

# 十八、本轮文档修订的边界总结

本轮真正修改的是四件事：

1. **SH供给身份收敛**：一张SH只有一个供给（Supply）。
2. **SH分层供需承接收敛**：第一层SH是供给；OPEN且未出现在跨厂在途视图中的SH，第二层形成一条完整源厂生产需求（Demand），RequiredQty=Order.Quantity，并严格按“PI → PI Position → 新增生产”继续源厂Pegging，只有未被已有PI承接的缺口才形成新增生产。
3. **SH工厂语义补全**：FactoryId=目标工厂，新增SourceFactoryId=源工厂。
4. **SH整单时间语义收敛**：同SH一次入库，一个整单可用时间（AvailableTime），真实完成时间来自1号位。

本轮不重新设计：

- 生产指示位置（PI Position）；
- 普通需求/供给模型（Demand/Supply）；
- 跨排程域总架构（Cross-Domain）；
- 有限产能求解器（Solver）；
- 物流任务（Logistics Task）；
- 4号位页面体系；
- 第二套SH平台。

这应作为下一轮正式冻结文档修订的唯一执行边界。
