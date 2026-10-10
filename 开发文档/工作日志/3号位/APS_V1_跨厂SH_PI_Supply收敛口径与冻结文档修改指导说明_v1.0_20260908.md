# APS V1 跨厂 SH / PI Supply 收敛口径与冻结文档修改指导说明

**版本**：v1.0  
**日期**：2026-09-08（北京时间）  
**用途**：提交 APS 冻结文档统一修订对话，作为本轮跨厂 Supply / SH / PI 口径收敛的修改输入  
**文档性质**：冻结文档修改指导说明，不重新设计 APS V1 业务

---

# 一、本轮修改的核心目的

本轮不是重新打开跨厂业务设计，而是清理当前有效冻结文档中仍残留的旧表达，尤其是：

```text
同SH在途
→ 同SH号ZP/BP Received
→ 未生产
```

这类容易被开发理解成“三段式 Supply / 三段式 Pegging”的历史表达。

当前统一口径必须收敛为：

1. **SH 作为 Supply 时：只有一份跨厂 SH Supply。**
2. **ZP/BP 只是跨厂在途内部的小阶段/状态，不再作为独立 Received Supply，也不作为独立 Pegging 阶段。**
3. **PI 作为 Supply 时：ProductionInstruction 本身仍是唯一 Supply。**
4. **PI 跨厂过程通过 `ProductionInstructionPositionSnapshot.PositionType = INTERPLANT_TRANSIT` 表达；ZP/BP 同理只是跨厂过程中的位置/状态，不形成新的 Supply。**
5. **SH 作为 Demand 时：只有一条需求，不存在 Transit / Received / Unproduced 三个 Demand 状态。**
6. **“未生产”如果代表源工厂仍需生产的数量，应进入源工厂下一层生产 Demand，而不是作为第三类 SH Supply。**

---

# 二、统一业务口径

## 2.1 SH 作为 Supply

正式模型：

```text
目标侧 Demand
    ↓
SH Supply（唯一跨厂供给身份）
```

SH 的跨厂物流过程可以存在内部状态，例如：

```text
SH Supply
    └─ 跨厂在途状态
        ├─ 运输中
        ├─ 已到目标工厂
        ├─ ZP/BP暂存
        └─ 其它尚未完成目标正式履约/正式入库的中间状态
```

但上述内部状态：

- 不生成新的 Supply；
- 不进入 Supply 排序；
- 不独立产生 Allocation；
- 不形成 `Transit Supply → Received Supply` 的串行 Pegging；
- 不改变 SH 作为唯一跨厂 Supply 的身份。

### ZP/BP 的正确定位

ZP/BP 可以继续存在于 ERP / ODS / 5号位事实层中，用于：

- 状态追溯；
- 当前物流位置解释；
- 数据核对；
- AvailableTime 计算基础事实；
- 跨厂履行过程的证据。

但不得在 2号位 Pegging 中被建模为：

```text
Received Supply
```

也不得作为独立供给阶段被单独消费。

---

## 2.2 PI 作为 Supply

正式模型：

```text
ProductionInstruction Supply
        +
ProductionInstructionPositionSnapshot
```

若 PI 数量处于跨厂过程：

```text
PositionType = INTERPLANT_TRANSIT
```

因此：

```text
PI Supply
```

仍然是唯一 Supply 身份。

禁止重新拆成：

```text
PI Supply
+ Transit Supply
+ ZP/BP Received Supply
```

PI Position 只回答：

> 这份 PI RemainingQty 当前在哪里、从哪里继续。

不回答：

> 再新增一份什么 Supply。

---

## 2.3 SH 作为 Demand

必须和“SH 作为 Supply”彻底区分。

SH 作为源工厂生产/履约需求时：

```text
ShippingInstruction Demand
Material
SourceFactory
RequiredQty
DueTime
```

它就是一条 Demand。

不再拆：

```text
SH Demand - Transit
SH Demand - Received
SH Demand - Unproduced
```

这些不是 Demand 生命周期。

如果目标侧的 SH Supply 还对应源工厂待生产数量，应在下一层形成源工厂生产 Demand，而不是把“未生产”作为 SH Supply 的第三个阶段。

---

# 三、开发实现统一口径

## 3.1 禁止继续实现三段式 SH Supply 链

以下实现全部退出正式 V1 主链：

```text
SH Transit Supply
→ SH Received Supply
→ SH Unproduced Supply
```

以及：

```text
Transit先Allocation
Received再Allocation
Unproduced再Allocation
```

正式应为：

```text
Demand
→ SH Supply
```

内部跨厂状态只作为事实解释。

---

## 3.2 Received 事实可以保留，但身份必须降级

ERP / ODS 如果仍存在：

```text
DocumentNo
WarehouseCode
ZP/BP
ReceivedQty
```

可以继续保留。

但它只能作为：

- 跨厂在途内部状态；
- 状态证据；
- 追溯事实。

不再作为：

- 独立 Supply；
- 独立 Allocation 对象；
- Supply 排序对象；
- 必须经过的一层 Pegging。

---

## 3.3 SH Supply 是否存在，不能由 Transit + Received 倒推

SH 作为跨厂 Supply 的业务身份和数量，必须来自 SH 自身或源系统权威跨厂供给事实。

不得通过：

```text
TransitQty + ReceivedQty
```

反推出：

```text
SH RemainingQty
```

更不能因为 Transit 视图暂时为 0 行、ZP/BP Received 暂时取不到，就得出：

```text
SH Supply不存在
```

Transit / ZP/BP 只说明：

> 这份跨厂 Supply 当前处于什么小阶段。

---

## 3.4 Demand 如何匹配具体 SH，仍需权威关系

阶段收敛并没有消除这个事实需求。

如果 2号位需要知道：

> 某个 Demand 到底应该承接哪一张 SH Supply？

仍然需要 5号位从 ERP / ODS 提供真实的：

```text
Order / Demand ↔ SH No
```

Document 关系。

禁止 2号位仅依靠：

- CustomerSegment=跨厂；
- Material；
- TargetFactory；
- SourceFactory；
- 名称相似；
- 时间接近；

自行猜测 SH 关系。

---

# 四、当前有效冻结文档中需要修改的文件

## P0：必须修改

### 4.1 《APS V1 最终全部流程与业务基线 v1.4》

**当前风险**

仍可能保留类似：

```text
SH内部履行状态：
在途
→ 同SH号ZP/BP Received
→ 未生产
```

的流程式表达。

**修改要求**

统一改成：

> SH 作为 Supply 时保持单一跨厂 SH Supply 身份。Transit、已到目标工厂、ZP/BP 等仅为该跨厂 Supply 的内部物流状态/事实，不形成独立 Supply、不参与独立 Allocation。若源工厂仍有待生产数量，则该数量进入下一计算层形成源工厂生产 Demand。

**必须补充**

明确区分：

```text
SH as Supply
```

与：

```text
SH as Demand
```

SH 作为 Demand 时只是一条需求，不存在 Transit / Received / Unproduced 多状态需求链。

---

### 4.2 《APS Pegging供需承接与分层计算业务说明 v1.3》

**当前风险**

历史正文仍容易出现：

```text
在途
→ Received
→ 未生产
```

以及“进入 SH 后依次消费内部状态”的理解。

**修改要求**

将 SH 章节统一为：

```text
先确定SH Supply身份
↓
Demand → SH Supply
```

SH 内部：

```text
Transit / ZP/BP / 已到厂等
```

仅作为跨厂状态事实。

删除或改写任何可能被理解为：

```text
Transit Supply
Received Supply
Unproduced Supply
```

的表述。

**必须保留的规则**

- 同一物理数量只允许一个 Supply 身份；
- SH 本身为跨厂 Supply；
- ZP/BP 不独立产生 Supply；
- “未生产”不是第三类 Supply；
- 源厂待生产数量进入下一层 Demand；
- SH 作为 Demand 时只保留单一 Demand 身份。

---

### 4.3 《APS 核心排产全流程走查 V3.20》

**当前风险**

当前仍容易出现：

```text
Inter-factory Order
→ SH
→ 同SH在途
→ 同SH Received
→ 未生产
```

这种流程图。

**修改要求**

改成两条独立链：

#### 目标侧 Supply 链

```text
目标侧 Demand
→ SH Supply
```

其中：

```text
跨厂在途 / ZP/BP / 已到目标厂
```

只是 SH Supply 的内部状态。

#### 源厂 Demand 链

```text
若SH仍需源厂生产
→ 形成源厂生产Demand
→ 进入下一层Pegging / PI / 新增生产
```

禁止再用一条箭头链把 Transit / Received / Unproduced 串成 Supply 生命周期。

---

### 4.4 《APS V1 5号位复杂业务事实、ODS与业务接口接入开发实施包 v1.3》

**当前风险**

可能仍将：

- Transit；
- Received；
- ZP/BP；
- Unproduced；

作为需要分别交付给 2号位消费的多阶段对象。

**修改要求**

5号位正式输出应收敛为：

#### SH跨厂供给事实

至少包括：

- SH No；
- Material；
- SourceFactory；
- ReceivingFactory / TargetFactory；
- SH权威数量；
- AvailableTime基础事实；
- 当前跨厂状态；
- 必要Document关系；
- SourceUpdatedAt / Evidence。

#### 跨厂状态事实

可以包含：

- IN_TRANSIT；
- ARRIVED；
- ZP/BP；
- 其它尚未正式结束跨厂过程的内部状态。

但明确：

> 这些只是同一 SH Supply 的状态，不是多份 Supply。

### Order / Demand ↔ SH 关系

5号位必须继续承担源系统权威 Document 关系标准化职责。

不得把 SH 选择责任推给 2号位猜测。

---

# 五、P1：应同步修改

### 5.1 《APS 集成接口设计 v1.30》

**修改重点**

如果接口仍存在：

```text
TransitFact
ReceivedFact
```

分别作为 Pegging 输入对象，应调整语义。

推荐统一成：

```text
InterplantShipmentSupplyFact
```

或等价统一契约。

内部可带：

```text
TransitStage / CurrentState / LocationRole / ZP_BP_Flag
```

但必须明确：

> 状态字段不改变 Supply 身份。

**不强制**

本轮不强制改接口类名，只要现有 DTO 可以通过语义修正完成收敛，不为了命名重新重构。

---

### 5.2 《APS 数据架构与防腐层设计方案 v1.41》

**修改重点**

清理历史正文中：

- Transit Supply；
- Received Supply；
- SH多阶段Supply；

等残留表达。

保持现有正确方向：

> 厂间订单型使用跨厂 Supply；不建设 Transit Supply 模型。

同时补充：

> ZP/BP Received 如保留，仅属于 ODS / 跨厂状态事实，不进入独立 Supply 链。

---

### 5.3 《APS V1 2号位增量开发实施包 v1.5》

**修改重点**

搜索并清理类似：

```text
ConsumeTransit
ConsumeReceived
ConsumeUnproduced
```

或者：

```text
Transit → Received → Unproduced
```

的 SH Supply 实现指导。

统一改成：

```text
Load authoritative SH Supply
↓
Demand Pegging to SH Supply
↓
read internal transit state only for time/state interpretation
```

若存在源厂待生产量：

```text
create source-factory production Demand
```

而不是创建第三类 SH Supply。

---

### 5.4 《APS 数据库字段说明文档 v5.1.5》

**修改重点**

检查：

- SupplyFact_Pipeline；
- InterplantTransit；
- Received；
- SH相关字段；
- PI Position；

是否仍有：

> Received = 独立 Supply

或：

> ZP/BP = Pegging独立供给阶段

的描述。

统一改为：

> Received / ZP/BP 可以保留为 ODS / 状态事实，但不形成独立 Supply。

### PI部分

确认：

```text
ProductionInstruction Supply
+ PositionType=INTERPLANT_TRANSIT
```

继续保持。

---

### 5.5 《APS 数据库表结构设计 DDL v5.1.5》

**修改重点**

本轮原则上：

> **优先改注释、视图/SP语义，不为了收敛新增表。**

重点搜索：

- `Received`
- `Transit`
- `SHIPPING_INSTRUCTION`
- `SupplyFact_Pipeline`
- `INTERPLANT_IN_TRANSIT`
- `ZP`
- `BP`

如存在：

```text
Received作为独立Supply
```

或：

```text
Transit+Received分别进入Pegging
```

的注释/SP逻辑，需要退出。

**禁止**

本轮禁止新建：

- TransitSupply表；
- ReceivedSupply表；
- SHStateSupply表；
- Transit/Received Allocation表。

---

# 六、P2：建议补充但不重构

### 6.1 《APS V1 Supply_PI_Position专项冻结收口确认单》

当前方向本身基本正确：

- 厂间订单型使用跨厂 Supply；
- 大工艺接续型使用 PI Supply + Position；
- 不建设 Transit Supply 模型。

只建议补一句：

> ZP/BP Received 如ERP/ODS仍提供，仅作为跨厂在途内部状态/证据，不形成独立Supply或独立Pegging阶段。

---

### 6.2 《APS V1 20260903代码审核检查基线》

建议补充一条代码审核红线：

**SH跨厂Supply审核**

必须满足：

- SH作为Supply时只有一个跨厂Supply身份；
- ZP/BP不能独立生成Received Supply；
- Transit/ZP/BP状态不得分别产生Allocation；
- 源厂待生产量形成Demand，不形成Unproduced Supply。

禁止：

```text
Transit Supply
Received Supply
Unproduced Supply
```

三段式恢复。

---

### 6.3 《APS V1 0号位总体项目验收包 v1.2》

建议在跨厂/供给验收增加：

- SH Supply身份唯一；
- ZP/BP并入跨厂在途内部状态；
- PI跨厂通过Position表达；
- SH作为Demand时不拆状态；
- 不存在Transit / Received / Unproduced三类Supply重复。

---

# 七、历史文档处理原则

如果以下旧版本已经不在当前有效项目来源中，不要求逐份回修正文：

- Pegging旧版本；
- 旧5号位实施包；
- 旧核心走查；
- 旧接口设计；
- 旧字段/DDL版本。

处理方式：

> 保留历史追溯，但明确“不再作为当前开发/审核依据”。

不得因为旧版本仍写：

```text
Transit → Received → Unproduced
```

就恢复旧业务。

---

# 八、本轮不需要修改的核心内容

以下业务本身不重新打开：

1. SH作为跨厂Supply的资格/Document关系；
2. Demand → SH 的实际 Pegging 仍由2号位执行；
3. Order / Demand ↔ SH No 权威关系仍由源系统 / 5号位提供；
4. PI仍是Supply；
5. PI Position仍由5号位负责事实解释；
6. 2号位仍拥有Pegging/Allocation数量真相；
7. 1号位仍只消费最终LogicalProductionDemand与有限产能约束；
8. V1不建设Transit Supply模型；
9. V1不新增第二套跨厂状态平台。

---

# 九、文档修改时必须防止的错误

## 错误1：把“删除Received Supply”改成“删除所有Received/ZP/BP事实”

错误。

正确：

> ERP/ODS事实可以保留，但不进入独立Supply身份。

## 错误2：把ZP/BP重新建成库存Supply

错误。

ZP/BP只属于跨厂过程中内部小阶段/位置事实。

## 错误3：把SH作为Demand也拆成多个状态

错误。

SH作为Demand只是一条Demand。

## 错误4：把“未生产”继续留在SH Supply生命周期里

错误。

源厂仍需生产时，进入下一层源厂生产Demand。

## 错误5：因为阶段收敛就取消 Order / Demand ↔ SH No 权威关系

错误。

Supply阶段简化和Document关系是两个不同问题。

Demand要承接哪张SH，仍必须有权威关系。

---

# 十、建议统一替换的标准表述

建议当前有效文档统一采用以下文字：

> **厂间出荷指示（SH）作为Supply时，在APS中保持单一跨厂Supply身份。运输中、已到目标工厂、ZP/BP暂存等均属于同一跨厂供给过程中的内部状态/事实，不形成独立Transit Supply或Received Supply，不独立参与Supply排序和Allocation。若SH对应源工厂仍存在待生产数量，则该数量在下一计算层形成源工厂生产Demand，而不是作为SH第三类Supply。**
>
> **生产指示（PI）作为Supply时同样保持ProductionInstruction唯一Supply身份；跨厂过程通过ProductionInstructionPositionSnapshot的INTERPLANT_TRANSIT Position表达，ZP/BP等仅为Position/过程状态。**
>
> **SH作为Demand时只表达一条源工厂生产/履约需求，不拆Transit、Received、Unproduced多个Demand状态。**

---

# 十一、文档修改优先级总结

| 优先级 | 文档 | 修改程度 |
|---|---|---|
| P0 | 最终全部流程与业务基线 v1.4 | 必改，修正最高业务表述 |
| P0 | Pegging业务说明 v1.3 | 必改，删除三段式Supply链 |
| P0 | 核心排产全流程走查 V3.20 | 必改，修改端到端流程图/文字 |
| P0 | 5号位实施包 v1.3 | 必改，统一ODS/事实输出语义 |
| P1 | 集成接口设计 v1.30 | 改接口语义，避免Transit/Received双Supply |
| P1 | 数据架构与防腐层 v1.41 | 清理历史双阶段表达 |
| P1 | 2号位实施包 v1.5 | 清理Transit→Received→Unproduced实现指导 |
| P1 | 数据库字段说明 v5.1.5 | 修正Received/ZP/BP身份说明 |
| P1 | DDL v5.1.5 | 检查SP/视图/注释，不新建表 |
| P2 | Supply_PI_Position收口确认单 | 补ZP/BP并入跨厂在途说明 |
| P2 | 代码审核检查基线 | 补SH单一Supply审核红线 |
| P2 | 0号位总体项目验收包 | 补跨厂Supply身份唯一验收 |

---

# 十二、最终交接要求

另一个文档治理对话执行修改时必须遵守：

1. 只清理旧三段式Supply/状态链表达；
2. 不重新设计跨厂业务；
3. 不新增表和平台；
4. 不删除ERP/ODS真实状态事实；
5. 不取消Order/Demand ↔ SH的权威Document关系；
6. 不改变2号位Pegging Owner；
7. 不改变5号位事实Owner；
8. 不改变PI Position职责；
9. 修改完成后逐文档检查：
   - 是否仍出现“Transit Supply”；
   - 是否仍出现“Received Supply”；
   - 是否仍出现“Transit → Received → Unproduced”作为Pegging流程；
   - 是否把SH作为Demand错误拆成多个状态；
   - 是否把ZP/BP重新定义成独立Supply。

本轮完成后的唯一正式口径为：

```text
SH as Supply
= 单一跨厂SH Supply
+ 跨厂在途内部状态（含ZP/BP等）

PI as Supply
= ProductionInstruction Supply
+ PI Position（跨厂为INTERPLANT_TRANSIT）

SH as Demand
= 单一Demand
```

不得再恢复三段式Supply模型。
