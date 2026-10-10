# APS V1 用户输出能力冻结文档修改指导（最终收口版）

**版本**：v1.0  
**日期**：2026-09-20（北京时间）  
**文档性质**：冻结文档修订上位指导 / 用户输出专项最终收口  
**适用范围**：APS V1 用户输出、结果解释、计划版本追溯、Candidate比较、资源负荷、Setup统计、业务查询职责与相关代码整改  
**代码核查基线**：`LangMao123/LPS`，`main`，Commit `6a681a0848b7c1e8d98c95acca7848ec227ea4c5`  
**语言规范**：中文业务解释为主，必要英文术语放在中文后的括号中

---

# 0. 文档使用说明

本文不是新增一套独立业务方案，也不是只用于补充一份“用户输出说明”。

本文的正式用途是：

> **作为本轮用户输出专项的上位修改指导，用于逐份修订当前APS V1冻结文档，并同步指导现有代码整改。**

因此本文同时承担四个作用：

1. 冻结本轮新增/补充的用户输出业务能力；
2. 明确哪些旧冻结口径已不再正确，必须被替换；
3. 明确每一份受影响冻结文档具体修改什么内容；
4. 明确当前代码中已经发现、需要后续整改的具体漏洞。

本轮修改必须遵守既有冻结修订原则：

- 不重新设计已经冻结的Pegging、Allocation、有限产能、Domain、Candidate、权限、MES下发等主业务；
- 不因“职责更漂亮”而搬迁已经成熟稳定的代码；
- 不允许形成第二套业务真相；
- 不允许5号位通过查询统计重新执行业务决策；
- 不允许为页面输出反向增加2号位不必要的新主链开发；
- 错误旧口径必须被正式替换，不得仅在文末追加“补充说明”而继续保留冲突正文；
- 修改完成后必须执行一次全量模拟读取审核，确认业务、字段、接口、号位职责和代码整改要求一致。

---

# 1. 本轮业务变更边界

## 1.1 本轮新增/补充内容

本轮正式补齐以下用户输出能力：

1. 订单客户交期与APS预计完成时间对比；
2. 计划延期状态、延期时长及延期原因；
3. 执行偏差风险；
4. 甘特图中的资源、订单、Task视角；
5. Task影响订单及影响数量；
6. Pegging / Supply / PI Position / 跨厂供给追溯；
7. 计划版本（PlanVersion）追溯及版本变化解释；
8. 设备资源负荷率；
9. 换型（Setup）次数、总时长、平均时长；
10. 白天CTP、插单、人工调整、设备故障、Calendar变化等Candidate场景统一输出：
    - 调整前后变化；
    - 影响范围；
    - 原因；
11. Candidate Base与Candidate详细差异比较；
12. 业务排程战报与运行治理战报的职责拆分；
13. 用户工作台统一输出原则。

---

## 1.2 本轮正式覆盖的旧冻结结论

以下旧口径由本专项正式覆盖：

### A. `OrderScheduleSummary / ResourceLoadSummary / PlanKpiSummary`“阶段一必须生成”

旧冻结DDL/字段说明将三张表定义为阶段一即用读模型，并要求2号位在Task/Pegging落库后生成。

**本轮正式覆盖为：**

> 三张表保留数据库兼容结构，但不再作为APS V1用户输出的强制生成链路；定位调整为“可选结果缓存 / 性能优化读模型”。

V1允许5号位直接基于正式结果事实查询与聚合。

只有在真实性能测试证明直接聚合无法满足页面性能目标时，才启用上述Summary表作为缓存优化。

---

### B. G4 / G7 / G8 / Schedule普通查询必须统一迁移至5号位

旧《20260901职责、前端接口与权限归属调整正式裁决》要求G4、G7、G8及Schedule查询逐步迁5，并倾向5号位再调用2号位Query Service。

**本轮正式覆盖为：**

> 治理事实继续由3号位直接提供；成熟Schedule查询代码不因职责形式统一而强迁。

具体为：

- Run生命周期 → 3号位；
- Candidate生命周期 / Base / 激活 → 3号位；
- DomainDefinition → 3号位；
- DomainDependency → 3号位；
- Rule / Parameter / Strategy版本 → 3号位；
- Candidate详细Diff → 保留3号位现有成熟实现；
- Gantt / Schedule Summary若3号位现有实现成熟，V1允许原位保留；
- 5号位重点承担新增业务结果查询与统计，不要求重写已有成熟Query。

---

### C. 订单页面“延期状态”等同 `Order.DelayStatus`

**本轮正式覆盖为：**

> `Order.DelayStatus`不得作为“当前PlanVersion是否满足客户交期”的计划延期真值。

APS计划延期必须依据：

> 当前PlanVersion下该订单有效FinalTask的最晚 `PlannedEndTime` 与 `CustomerDueDate` 比较。

---

### D. `RiskLevel`作为V1正式风险等级

当前冻结文档/DDL对 `RiskLevel` 值域存在不一致：

- 一处为 `ON_TRACK / AT_RISK / DELAYED`；
- 一处为 `LOW / MEDIUM / HIGH / CRITICAL`。

且用户已明确V1不建设复杂风险评分。

**本轮正式覆盖为：**

> V1不再使用复杂RiskLevel作为正式风险真值；计划交付状态与执行偏差风险拆开。

---

## 1.3 本轮明确不动的冻结内容

以下内容不因本轮用户输出专项调整：

- Pegging主规则；
- Allocation规则；
- Supply优先级与供需闭合；
- PI Position不是Supply；
- Domain定义与夜间/白天运行边界；
- 夜间FULL每Domain独立PlanVersion；
- 白天Candidate严格单Domain；
- Firm近端3天；
- Candidate不直接修改ACTIVE；
- CTP不是最高优先级插单；
- 1号位纯内存有限产能求解红线；
- 2号位主流程与核心持久化红线；
- 3号位规则、参数、权限、运行治理职责；
- 5号位不重算Pegging / Allocation / Solver；
- 4号位不做业务计算、不直接写运行数据库；
- 4号位仍只直接面对3号位和5号位；
- 现有权限角色、业务范围权限、后端鉴权原则；
- MES实际设备利用率继续由MES负责，不进入APS V1重复统计。

---

# 2. 用户输出最终业务模型

APS V1用户最终需要理解的不是“算法内部发生了什么”，而是以下业务链路：

```text
订单交付结果
↓
是否延期 / 延期多少
↓
为什么延期
↓
当前排程结果在哪里
↓
需求由什么供给承接
↓
Task影响哪些订单
↓
资源负荷和换型情况
↓
白天调整后变化了什么
↓
为什么昨天和今天不同
```

---

# 3. 用户输出能力最终冻结口径

## 3.1 订单交付结果

每个订单至少输出：

- 客户交期（CustomerDueDate）；
- APS预计完成时间；
- 当前计划是否按期；
- 延期时长；
- 是否未排程 / 未满足；
- 主要延期原因；
- 必要时显示影响工序、资源、物料、生产部门。

### 3.1.1 APS预计完成时间

正式定义：

> 当前PlanVersion下，该订单所有有效FinalTask的最大 `PlannedEndTime`。

禁止：

- 使用任意一个Task的结束时间代替订单完成时间；
- 使用 `Order.DelayStatus` 直接代替APS计划交付状态；
- 因订单无Task而自动判定为按期。

### 3.1.2 APS计划交付状态

建议统一为：

- `ON_TIME`：APS预计完成时间不晚于客户交期；
- `DELAYED`：APS预计完成时间晚于客户交期；
- `UNSCHEDULED` / `UNFULFILLED`：无法形成完整有效排程或正式缺口未闭合。

### 3.1.3 延期时长

```text
DelayHours = max(0, APS预计完成时间 - CustomerDueDate)
```

页面可按小时或天显示，但底层必须保留稳定时间精度。

---

## 3.2 延期原因

延期原因不能由页面或5号位自行猜测。

正式区分两类原因：

### A. 有限产能排程原因

例如：

- 资源能力不足；
- 资源等待；
- 前序工序等待；
- Calendar不可用；
- Setup切换影响；
- 冻结区 / 执行锁影响；
- 设备不可用；
- 其它Solver约束。

职责：

> 1号位产生正式排程解释事实（ScheduleExplanationFact Draft） → 2号位持久化 → 5号位查询、翻译和展示。

### B. Pegging / Supply原因

例如：

- 哪类Supply承接该需求；
- 哪个PI承接；
- 为什么形成正式物料短缺；
- Supply ETA对需求造成的约束；
- 跨厂供给状态。

职责：

> 2号位产生Pegging / Allocation正式真相 → 5号位查询和展示。

### 3.2.1 5号位的边界

5号位可以：

- 根据正式ReasonCode转换中文说明；
- 关联生产部门、工序、Resource、Material、Warehouse、ETA等业务字段；
- 聚合原因分布；
- 输出Explanation View DTO。

5号位不得：

- 重新运行Solver；
- 重新运行Pegging；
- 从Task、库存、Allocation“猜”一个原系统没有记录的正式原因；
- 覆盖1号位/2号位正式ReasonCode。

如果正式原因事实缺失，应明确显示“正式原因事实未记录”，并纳入数据完整性整改。

---

## 3.3 风险输出

V1不建设：

- LOW / MEDIUM / HIGH / CRITICAL综合风险评分；
- 复杂风险模型；
- ML预测；
- 独立Risk Engine。

正式只保留：

### A. 计划交付状态

反映当前PlanVersion是否满足客户交期。

### B. 执行偏差风险

基于已经发生或正在发生的执行偏差，例如：

- 生产Task已到计划完成时间但未完成；
- 采购已超过正式ETA仍未到货；
- 上游Task实际延期并已影响后续；
- 设备故障事实导致当前计划存在失效风险。

执行偏差风险由5号位基于正式执行事实聚合，不允许建立复杂评分。

---

## 3.4 甘特图

甘特图继续作为APS V1核心用户输出。

必须支持：

- Resource视角；
- Order视角；
- Task视角；
- 当前ACTIVE；
- 指定Candidate PlanVersion；
- 必要时显示Pegging/订单影响；
- 必要时显示Explanation信息。

V1不要求为了职责归属重新迁移现有成熟Gantt查询代码。

---

## 3.5 Task影响订单

用户需要回答：

> 某个Task变化后，影响哪些订单、影响多少数量、是否影响客户交期。

正式真相来源：

- AllocationTaskShare；
- FinalTask；
- Order。

5号位负责查询统计，不重新分配Demand/Supply。

---

## 3.6 Pegging / Supply / PI Position追溯

必须继续支持：

- 订单 → 库存；
- 订单 → PI；
- 订单 → 采购；
- 订单 → 跨厂供给；
- Task → 受影响订单；
- PI剩余总量；
- PI当前执行位置；
- 跨厂源工厂 → 目标工厂 → SH → 到货时间。

保持既有冻结：

> PI Position不是新的Supply。

---

## 3.7 资源负荷

V1必须输出：

- Resource可用时间；
- 计划占用时间；
- 负荷率。

正式统计口径：

```text
计划占用时间
= 当前PlanVersion下FinalTask在该Resource统计窗口内的有效占用时间

可用时间
= Resource Calendar正式可生产窗口

负荷率
= 计划占用时间 / 可用时间
```

职责：

> 5号位负责结果统计。

不要求2号位新增 `ResourceLoadSummary` 生成器。

### 资源负荷与“瓶颈解释”分开

- 负荷率：5号位统计；
- 为什么资源导致排程等待：1号位Explanation事实；
- V1不建设复杂瓶颈排行或综合瓶颈指数。

---

## 3.8 Setup换型KPI

输出：

- 每日换型次数；
- 每日换型总时长；
- 平均换型时长。

正式口径：

```text
换型次数
= 正式排程结果中发生有效Setup切换的次数

换型总时长
= Σ SetupDuration

平均换型时长
= 换型总时长 / 换型次数
```

职责：

> 1号位产生参与排程的Setup事实 → 2号位保存必要结果 → 5号位统计KPI。

不得要求1号位增加报表层。

---

## 3.9 计划版本追溯

版本追溯为V1 P0能力。

至少展示：

- PlanVersion；
- Domain；
- 生成时间；
- 数据截止时间；
- 创建人；
- 触发方式；
- FULL / LOCAL / MANUAL / CTP等Run类型；
- BasePlanVersion；
- 当前 / Candidate / History关系；
- Rule / Parameter / Strategy版本；
- Candidate人工输入（适用时）。

---

## 3.10 “为什么昨天和今天不同”

V1不建设复杂“自动解释引擎”，但必须支持版本变化追溯。

### 必须可比较

- 哪些订单预计完成时间变化；
- 哪些订单新增延期；
- 哪些订单解除延期；
- 哪些Task增加；
- 哪些Task删除；
- 哪些Task时间变化；
- 哪些Task Resource变化；
- Pegging关键承接变化；
- 资源负荷变化；
- Setup变化。

### 原因只能引用正式证据

例如：

- ERP订单变化；
- MES执行事实变化；
- Supply变化；
- Calendar变化；
- 设备状态变化；
- Rule / Parameter / Strategy版本变化；
- 人工调整输入；
- Candidate场景输入；
- ScheduleExplanationFact。

禁止5号位通过重新运行算法推测“为什么”。

---

## 3.11 白天Candidate场景统一输出

以下白天场景统一采用同一个结果输出框架：

- CTP；
- 插单影响分析；
- 局部重排；
- 已有订单提前；
- 人工Task调整；
- 设备故障；
- Calendar变化；
- LOCAL_RESCHEDULE；
- MANUAL_RESCHEDULE；
- 其它已冻结白天单Domain Candidate入口。

统一输出三部分：

### 调整前后变化

- Order；
- Task；
- Resource。

### 影响范围

- 影响哪些订单；
- 延期 / 提前多少；
- 哪些Resource变化；
- 关键Supply / Pegging变化；
- 哪些Task增删改。

### 原因

- 优先级；
- Resource；
- Material；
- Predecessor；
- Calendar；
- 人工输入；
- 执行事实变化。

继续保持：

> CTP不是最高优先级插单。

---

# 4. 用户输出最终职责划分

## 4.1 总原则

V1最终阶段的职责判断优先级正式调整为：

1. 不能产生第二套业务真相；
2. 不能重复计算核心业务；
3. 已成熟稳定代码不为职责美观强制迁移；
4. 尽量减少新的跨号位依赖；
5. 平衡开发工作量；
6. 最后才考虑职责分类是否绝对整齐。

正式采用：

> **真相Owner唯一，查询实现允许就地保留。**

---

## 4.2 最终职责矩阵

| 用户输出主题 | 事实/计算Owner | 持久化/核心真相 | 查询/统计/展示 |
|---|---|---|---|
| FinalTask时间/资源 | 1号位 | 2号位 | 3或5复用现有成熟Query |
| 有限产能延期原因 | 1号位 | 2号位 | 5号位 |
| Pegging / Allocation | 2号位 | 2号位 | 5号位 |
| 订单预计完成 / 延期 | 正式FinalTask事实 | 2保存Task | 5号位统计 |
| Task影响订单 | 2号位AllocationTaskShare | 2号位 | 5号位 |
| Resource负荷 | 正式Task + Calendar | 不新增强制Summary | 5号位 |
| Setup KPI | 1号位Setup事实 | 2号位必要持久化 | 5号位 |
| Pegging Trace | 2号位真相 | 2号位 | 5号位 |
| PI Position | 5号位规则计算 | 2号位运行快照 | 5号位 |
| Supply Trace | 既有Owner | 既有 | 5号位 |
| Candidate生命周期 | 3号位治理 + 2号位运行 | 既有 | 3号位 |
| Candidate详细Diff | 现有3号位实现 | 不新增表 | 3号位 |
| Candidate概览 | 正式Candidate结果 | — | 5号位可组合 |
| Gantt | 1产生/2保存结果 | 既有 | 3号位成熟实现可保留，5号位可做统一业务出口 |
| Run生命周期 | 3号位 | 既有 | 3号位 |
| Domain定义/依赖 | 3号位 | 既有 | 3号位 |
| 业务排程战报 | 多源正式事实 | — | 5号位 |
| 运行治理战报 | ScheduleRun / Governance | — | 3号位 |
| 页面组合 | — | — | 4号位 |

---

## 4.3 2号位本轮明确不新增的内容

本轮用户输出专项不得要求2号位新增：

- OrderReportService；
- ResourceReportService；
- PeggingPageService；
- CandidateReportService；
- DashboardService；
- OrderScheduleSummary Generator；
- ResourceLoadSummary Generator；
- PlanKpiSummary Generator；
- Run/Domain治理Query Service重复实现；
- 页面专用复杂统计服务。

2号位只需保证已有核心运行事实：

- 算对；
- 落全；
- 可被稳定读取。

---

## 4.4 3号位保留的成熟查询

当前仓库已经存在：

- `ScheduleQueryService.GetVersionsAsync()`；
- `ScheduleQueryService.GetGanttAsync()`；
- `ScheduleQueryService.GetSummaryAsync()`；
- `ScheduleQueryService.GetCandidateComparisonAsync()`。

V1不要求为了职责“整齐”而强迁。

尤其：

> Candidate详细Diff必须保留单一实现，优先保留3号位当前成熟版本。

---

## 4.5 5号位新增重点

5号位应在现有：

```text
Controller
→ Service
→ Repository
→ DTO
```

骨架上扩展：

- 订单交付统计；
- 延期原因用户查询；
- Resource负荷率；
- Setup KPI；
- 执行偏差风险；
- Pegging/Supply/PI Position展示；
- 业务排程战报；
- PlanVersion业务差异聚合；
- 用户工作台业务结果聚合。

但不得进入核心业务重算。

---

# 5. 三张Summary读模型最终处理

## 5.1 当前代码事实

当前仓库核查未发现：

- `OrderScheduleSummary` 对应Entity；
- `ResourceLoadSummary` 对应Entity；
- `PlanKpiSummary` 对应Entity；
- 对应Repository；
- 对应Generator / Refresh Service；
- SchedulingOrchestrator结束后刷新调用；
- 查询端实际读取三张Summary的代码。

当前实际查询路线为：

```text
Order / Task / Resource / PlanVersion / PeggingSupplyAllocation / Explanation
↓
Query Repository / Query Service
↓
DTO
↓
4号位
```

---

## 5.2 最终定位

三张表：

- 保留数据库物理结构；
- 不要求删除；
- 不作为V1业务真相；
- 不作为V1页面查询强制依赖；
- 不要求2号位补生成器；
- 定位为可选性能缓存。

---

## 5.3 启用条件

只有满足以下条件之一时才考虑启用：

1. 10万Task级页面实时查询经过真实性能测试无法达到目标；
2. 某业务KPI重复聚合成本明显过高；
3. 前端高频查询对主结果表造成显著数据库压力。

启用时必须：

- 明确刷新时点；
- 明确失效条件；
- 明确PlanVersion隔离；
- 明确与正式事实的最终一致性；
- 不允许Summary反向成为核心业务判断依据。

---

# 6. V1优先级最终调整

## 6.1 P0

1. 甘特图；
2. 订单交付结果；
3. 延期原因解释；
4. Pegging / Supply / PI Position展示；
5. Task影响订单；
6. Candidate详细变化比较；
7. PlanVersion完整追溯；
8. 版本变化“为什么不同”的证据化解释；
9. 白天Candidate场景统一“变化 + 影响 + 原因”。

---

## 6.2 P1

1. Resource负荷率；
2. Setup次数 / 总时长 / 平均时长；
3. 执行偏差风险；
4. 统一工作台整合。

---

## 6.3 V1不做

- 独立管理层驾驶舱；
- 综合风险评分；
- AI风险预测；
- 工艺总负荷；
- 普通设备工序聚合负荷；
- 复杂瓶颈排行；
- MES实际设备利用率；
- 自动优化建议；
- 为用户输出重新建设第二套排程事实平台。

---

# 7. 受影响冻结文档及逐份修改说明

以下为本轮必须用于实际修订的冻结文档清单。

---

## 7.1 《APS V1用户输出能力冻结文档修改指导建议》

**处理方式：整份替换为本文，不再保留旧版第9章和第10章。**

重点修改：

1. 增加“本轮覆盖旧冻结结论”；
2. 补充计划延期正式计算口径；
3. 区分 `Order.DelayStatus` 与APS计划延期；
4. 增加执行偏差风险定义；
5. 增加PlanVersion追溯；
6. 增加“为什么昨天与今天不同”；
7. 增加三张Summary读模型降级说明；
8. 重写0～5号位职责矩阵；
9. 明确Candidate Diff保留3号位单一实现；
10. 明确Gantt成熟查询V1不强迁；
11. P0增加版本追溯与白天Candidate完整影响输出；
12. 增加代码漏洞整改清单；
13. 增加逐份冻结文档修改矩阵。

---

## 7.2 《APS V1最终全部流程与业务基线 v1.6》

**修改性质：新增用户输出正式业务章节，不修改既有Pegging/Solver主链。**

应增加：

### A. 订单交付输出

明确：

- CustomerDueDate；
- APS预计完成时间；
- ON_TIME / DELAYED / UNSCHEDULED；
- DelayHours；
- 正式延期原因。

### B. 版本追溯

明确：

- PlanVersion；
- DataCutoffTime；
- CreatedBy / Trigger；
- RunType；
- BasePlanVersion；
- Strategy / Rule / Parameter；
- Candidate输入；
- 历史差异。

### C. 白天Candidate统一输出

所有白天业务场景统一要求：

- Before / After；
- Impact Scope；
- Reason。

### D. 用户输出非主链原则

新增：

> 用户输出不得重新计算Pegging、Allocation、Solver或产生第二套业务真相。

---

## 7.3 《APS有限产能排产与滚动90天计划业务说明 v1.5》

**修改性质：补充1号位结果事实输出边界。**

应增加：

1. FinalTask作为用户结果时间/资源真相；
2. ScheduleExplanationFact Draft由1号位产生；
3. 资源等待、前序等待、Calendar、Setup、设备不可用等原因由1号位产出正式Explanation；
4. Setup事实必须能支撑结果统计；
5. 1号位不承担Resource负荷报表；
6. 1号位不承担Setup KPI报表；
7. 1号位不承担OrderReport / Dashboard；
8. 订单预计完成时间由结果统计层基于FinalTask最大结束时间形成；
9. “瓶颈解释”和“负荷率”必须区分。

不得新增：

- 1号位数据库写入；
- 1号位页面Query；
- 复杂风险评分。

---

## 7.4 《APS Pegging供需承接与分层计算业务说明 v1.5》

**修改性质：仅补用户解释输出，不改变Pegging算法。**

应增加：

1. Pegging / Allocation结果必须支持用户追溯；
2. 必须能回答订单由何种Supply承接；
3. Task影响订单以AllocationTaskShare为正式数量真相；
4. Pegging原因若需要业务解释，由2号位产生正式事实/证据；
5. 5号位只能展示，不得重算Pegging；
6. PI Position继续不是Supply；
7. Supply Trace与Pegging Trace区分；
8. 计划延期不能由Pegging查询层自行推断Solver原因。

---

## 7.5 《APS核心排产全流程走查 V3.22》

**修改性质：补结果输出闭环和运行后统计节点。**

应在流程中增加：

### 排程完成后

```text
FinalTask / Allocation / Explanation落库
↓
用户结果查询与统计
↓
订单交付
Resource Load
Setup KPI
Pegging Trace
Version Diff
Candidate Diff
```

应明确：

- 结果统计不回写改变Solver/Pegging；
- Summary三表不是主链必经步骤；
- 白天Candidate结束后必须形成可比较结果；
- Run完成不等于用户输出解释完成，必须有查询闭环。

---

## 7.6 《APS集成接口设计 v1.32》

**修改性质：是本轮接口修订重点文档。**

必须修改：

### A. 删除“一刀切迁移”逻辑

不再要求：

> G4 / G7 / G8 / Schedule全部迁5并通过2号位Query Service。

改为：

- Governance / Run / Domain → 4→3；
- Candidate生命周期 → 4→3；
- Candidate详细Diff → 4→3（V1现有实现）；
- 业务Overview / Order / Explanation / Pegging / Supply / PI Position / Load / Setup KPI → 4→5；
- Gantt成熟查询允许4→3继续使用；如5号位已有稳定业务出口也可复用，但不得重复实现核心逻辑。

### B. 增加用户输出接口

需要定义或扩展：

- Order Delivery；
- Order Delay Reason；
- Resource Load；
- Setup KPI；
- Version Trace；
- Version Diff；
- Candidate Diff；
- Execution Deviation Risk；
- Business Schedule Report。

### C. 明确接口Owner与Truth Owner不是同一概念

5号位可作为业务接口Owner，但不得因此重新拥有Pegging/Solver真相。

---

## 7.7 《APS V1 20260901职责、前端接口与权限归属调整正式裁决 v1.0》

**修改性质：专项覆盖，不建议整份废弃。**

必须替换原“第十章 G4 / G7 / G8 / Schedule查询归属调整”。

原文中以下内容不再作为V1强制要求：

- 全部逐步移交5号位；
- 3号位不再新增同类查询；
- 5号位统一读取2号位事实或调用2号位Query Service。

正式改为：

> 治理真相留3；成熟Query允许原位保留；新增业务结果优先5；2号位不因页面查询新增重复Query体系。

同时保留：

- 4号位只直接调用3、5；
- 4不直接调用2；
- 5不得建设第二套2号位运行事实；
- 权限仍由3号位统一治理。

---

## 7.8 《APS V1 1号位有限产能排程开发实施包 v1.4》

**修改性质：增加用户解释事实交付要求，不扩张报表职责。**

新增交付项：

1. FinalTask正式时间/资源结果完整；
2. ScheduleExplanationFact Draft结构完整；
3. Resource/Predecessor/Calendar/Setup等原因有稳定ReasonCode；
4. Setup事实能被后续统计；
5. Candidate排程也产生同口径Explanation。

明确不交付：

- Order KPI报表；
- Resource Load报表；
- Setup报表；
- Dashboard；
- 页面接口。

---

## 7.9 《APS V1 2号位增量开发实施包 v1.7》

**修改性质：明确减负，并保证真相落全。**

应增加：

### 必须保证

- FinalTask持久化；
- AllocationTaskShare完整；
- PeggingSupplyAllocation完整；
- ScheduleExplanationFact落库；
- PlanVersion / ScheduleRun必要关联；
- Candidate结果持久化；
- 5号位稳定读取正式结果所需最小契约。

### 明确不要求新增

- 三张Summary Generator；
- OrderReportService；
- ResourceReportService；
- CandidateReportService；
- DashboardService；
- Run/Domain重复Query Service。

### 仅在必要时补

> 5号位确实无法从稳定表结构读取核心真相时，再补“最小只读契约”，不得按每个页面逐项新建Application Service。

---

## 7.10 《APS V1 3号位规则参数认证权限与运行生命周期开发实施包 v1.5》

**修改性质：删除强制查询迁移任务，保留成熟Candidate/Run能力。**

必须修改：

1. 删除“G4 / G7 / G8 / Schedule普通结果查询逐步移交5号位”为强制任务；
2. 删除“G4/G7/G8/Schedule迁移交接记录”为必交付物；
3. 保留：
   - Run生命周期；
   - Domain Definition / Dependency；
   - Candidate生命周期；
   - Base校验；
   - Candidate激活；
   - Rule / Parameter / Strategy；
4. Candidate详细Diff保留当前 `ScheduleQueryService.GetCandidateComparisonAsync()`；
5. 现有成熟Gantt / Schedule Summary可以V1原位保留；
6. 增加“运行治理战报”输出责任；
7. 增加版本治理元数据供版本追溯使用。

---

## 7.11 《APS V1 4号位页面与业务操作开发实施包 v1.4》

**修改性质：补页面输出主题，修正接口迁移要求。**

必须修改：

### 删除/替换

- “G4/G7/G8/Schedule逐步迁5”；
- “迁移后URL/DTO兼容测试”作为强制验收条件。

### 新增页面能力

- 订单交付结果；
- 延期原因；
- Resource负荷；
- Setup KPI；
- PlanVersion追溯；
- Version Diff；
- Candidate详细Diff；
- 执行偏差风险；
- Task影响订单。

### 接口规则

仍保持：

- 4→3；
- 4→5；
- 禁止4→2；
- 禁止4自己计算Pegging/Solver。

Candidate详细Diff可直接调用3号位现有接口。

---

## 7.12 《APS V1 5号位复杂业务事实ODS与业务接口接入开发实施包 v1.5》

**修改性质：本轮新增开发职责最多的实施包。**

必须新增：

1. Order Delivery统计；
2. APS计划延期统计；
3. Explanation用户查询与业务字段补充；
4. Resource Load统计；
5. Setup KPI统计；
6. Execution Deviation Risk；
7. Business Schedule Report；
8. Version Trace业务视图；
9. Version Diff业务聚合；
10. 工作台业务聚合。

必须删除/整改：

- 5号位内部重复Candidate Diff计算；
- 继续假设所有Schedule结果必须来自2号位Query；
- 把 `Order.DelayStatus` 当作APS计划延期；
- `DelayStatus='RISK'` 旧逻辑；
- Resource Load只统计Task Hours但仍对外称“负荷率”。

必须保留：

- Overview；
- Order；
- Explanation；
- Pegging Trace；
- Supply Trace；
- PI Position；
- Manual ETA；
- Candidate结果查询包装能力。

---

## 7.13 《APS数据库字段说明文档 v5.1.7》

**修改性质：必须做字段语义修订。**

### A. 三张Summary

把：

- “阶段一即用”
- “2号位必须异步生成”

修改为：

> 可选性能缓存 / 兼容读模型，V1正式业务查询不得依赖其必须预生成。

### B. `RiskLevel`

统一说明：

> 历史兼容字段，不作为V1正式风险真值。

不得继续同时存在两套值域解释。

### C. `DelayStatus`

必须明确：

> `Order.DelayStatus`属于订单业务/同步属性，不等于当前PlanVersion的APS计划延期状态。

### D. APS计划延期

若不新增字段，则明确由查询层实时派生。

若未来增加持久化字段，必须绑定PlanVersion，不得覆盖源订单属性。

---

## 7.14 《APS数据库表结构设计 DDL v5.1.7》

**修改性质：不删表，改注释和冻结定位。**

不要求删除：

- `OrderScheduleSummary`
- `ResourceLoadSummary`
- `PlanKpiSummary`

但必须修改注释：

从：

> 阶段一即用

改为：

> V1可选结果缓存 / 性能优化读模型，不作为主链强制依赖。

同时修复：

- `OrderScheduleSummary.RiskLevel`值域注释与字段说明冲突；
- 不再注明2号位必须生成；
- 明确Summary数据不是业务真相源。

---

## 7.15 《APS数据架构与防腐层设计方案 v1.43》

**修改性质：补用户查询架构。**

应增加：

1. 正式事实层与结果查询层分离；
2. Summary缓存不是强制中间层；
3. 5号位允许直接基于APS正式事实表构建只读结果；
4. 只读结果层不得反向写入核心运行事实；
5. 如后续启用Summary，属于性能缓存，不属于新的Truth Layer。

---

## 7.16 《APS V1 20260915代码审核检查基线 v1.2》

**修改性质：增加用户输出专项审核项。**

新增审核项：

- 计划延期是否正确按FinalTask最大完成时间计算；
- 是否误用Order.DelayStatus；
- Candidate Diff是否存在重复实现；
- Resource Load是否真的使用Calendar可用时间；
- Setup KPI是否基于正式Setup事实；
- Explanation是否来源正式Fact；
- 是否新增第二套Pegging/Solver判断；
- 是否补了不必要的三张Summary Generator；
- Version Trace是否完整；
- Candidate白天场景是否都有Before/After/Impact/Reason；
- 4号位是否仍绕过3/5调用2号位。

---

## 7.17 《APS V1 0号位总体项目验收包 v1.4》

**修改性质：增加最终用户验收场景。**

至少增加：

1. 一个按期订单；
2. 一个资源原因延期订单；
3. 一个物料原因延期订单；
4. 一个未排程 / 未满足订单；
5. 一个Task影响多个订单；
6. 一个高负荷Resource；
7. 一天多次Setup；
8. 一个Base vs Candidate比较；
9. 一个Candidate新增延期；
10. 一个Candidate解除延期；
11. 一次Rule / Parameter / Data Cutoff变化导致版本差异；
12. 一次执行偏差风险；
13. 一个Pegging Trace；
14. 一个PI Position；
15. 一个跨厂Supply Trace；
16. 权限范围下用户只看到本Factory / Domain结果。

---

## 7.18 《APS V1 20260915冻结基线索引 v1.4》

**修改性质：最终收口时更新。**

在所有相关文档完成修订后：

- 替换新版文件名 / 版本；
- 标记本用户输出专项指导为修订依据；
- 确认旧版用户输出指导退出当前有效基线；
- 确认被专项覆盖的20260901查询迁移条款不再作为当前验收标准。

---

# 8. 当前代码必须整改的具体漏洞

以下问题基于当前仓库 `main` Commit `6a681a0848b7c1e8d98c95acca7848ec227ea4c5` 核查。

---

## P0-OUT-01 Candidate Diff重复实现

### 当前问题

3号位已有：

`ScheduleQueryService.GetCandidateComparisonAsync()`

5号位 `OverviewQueryRepository` 又存在：

`ComputeCandidateComparisonAsync()`

且注释明确说明为从ScheduleQueryService移植。

### 风险

- 同一业务Diff两套算法；
- 后续Stable Task Key变化时容易漂移；
- NewDelay / ImpactedOrder口径可能不一致。

### 整改

> Candidate详细Diff只保留3号位现有实现。

5号位Overview若需要摘要：

- 调用3号位已有结果；
- 或只做轻量组合；
- 不再维护第二套Diff算法。

---

## P0-OUT-02 订单交付摘要误用 `Order.DelayStatus`

### 当前问题

5号位 `OrderQueryRepository.GetSummaryAsync()` 直接按：

- `ON_TIME`
- `FIRST_DELAY`
- `REPEATED_DELAY`
- `RISK`

统计订单。

### 风险

`Order.DelayStatus`不是当前PlanVersion的APS计划交付状态。

### 整改

改成：

> 按订单当前PlanVersion有效FinalTask最大 `PlannedEndTime` 与 `CustomerDueDate` 比较。

---

## P0-OUT-03 `DelayStatus='RISK'` 与冻结值域冲突

### 当前问题

当前冻结合法值为：

- ON_TIME
- FIRST_DELAY
- REPEATED_DELAY

但5号位代码存在：

`DelayStatus = 'RISK'`

### 整改

删除该混用。

执行风险改成独立结果统计，不继续扩展DelayStatus。

---

## P0-OUT-04 `Order.cs` Entity与数据库字段漂移

### 当前问题

数据库/查询代码存在 `DelayStatus`，但当前 `Order.cs` Entity未定义该属性。

### 风险

- Entity / DDL / DTO语义不一致；
- 后续EF/Dapper混用时容易形成隐藏缺陷。

### 整改

执行字段一致性核对。

但即使补Entity，也不得把APS计划延期写回该字段。

---

## P0-OUT-05 Resource Load当前是假“负荷”

### 当前问题

5号位当前 `GetResourceBottleneckAsync()` 主要统计：

- TaskCount；
- TotalPlannedHours。

同时：

- `UtilizationRate = NULL`
- `IsBottleneck = 0`

### 风险

页面可能误以为已经实现设备负荷率。

### 整改

补：

- Calendar可用时间；
- 计划占用时间；
- LoadRate。

删除“必须由2号位正式Query才能算负荷率”的旧依赖。

---

## P0-OUT-06 三张Summary只有DDL设计，没有应用链路

### 当前问题

未发现：

- Entity；
- Repository；
- Generator；
- Refresh Service；
- Orchestrator刷新调用；
- Query消费代码。

### 整改

> 不补2号位生成链。

正式降级为可选缓存。

---

## P0-OUT-07 Candidate Diff中的“新增延期”需要统一订单级口径

### 当前问题

现有Candidate Diff代码存在按Task与CustomerDueDate比较的路径。

### 风险

一个订单多Task时，可能把中间Task超过交期误判为订单新增延期，或产生重复语义。

### 整改

Candidate新增延期必须基于：

> Base与Candidate各自“订单最终完成时间”比较。

---

## P0-OUT-08 Schedule Summary中的DelayedTasks与DelayedOrders需要语义区分

### 当前问题

当前3号位Schedule Summary同时统计：

- DelayedTasks；
- DelayedOrders。

但用户真正关心的是订单交付。

### 整改

- `DelayedOrders`按订单最终完成时间；
- `DelayedTasks`若保留，必须改名或解释为“Task结束晚于订单交期”的技术统计，不得作为订单延期KPI。

---

## P0-OUT-09 Explanation事实完整性需要验收

### 当前问题

现有架构支持Explanation，但用户输出需要确保真实延期能够落到正式原因事实。

### 整改

增加自动检查：

> 订单被判定为DELAYED时，如无任何正式Explanation且并非明确物料短缺/未满足，记录数据完整性Issue。

不得由5号位猜补ReasonCode。

---

## P0-OUT-10 Version Trace当前信息不足

### 当前问题

现有版本查询有PlanVersion、部分Run信息，但还不足以完整回答：

> “为什么昨天和今天不同”。

### 整改

补齐：

- DataCutoffTime；
- Trigger / CreatedBy；
- BasePlanVersion；
- Rule / Parameter / Strategy版本；
- Candidate Input摘要；
- Version Diff；
- 有证据的变化原因。

---

## P1-OUT-01 Setup KPI尚未形成正式查询能力

### 整改

5号位新增：

- SetupCount；
- TotalSetupDuration；
- AvgSetupDuration；
- Resource / Date维度查询。

---

## P1-OUT-02 执行偏差风险尚未形成统一模型

### 整改

以正式事实派生：

- 生产未按计划完成；
- 采购超过ETA未到；
- 上游实际延期。

禁止复杂评分。

---

## P1-OUT-03 Overview中“RiskCount”旧逻辑需要重审

### 当前问题

现有Overview对 `IN_PROGRESS + IsCriticalPath` 做RiskCount。

### 风险

Critical Path不等同于执行偏差风险。

### 整改

RiskCount改为正式执行偏差风险数量，或在V1先移除该KPI，禁止误导用户。

---

## P1-OUT-04 “Bottleneck”命名需要收口

### 当前问题

现有5号位方法名 `GetResourceBottleneckAsync()`，但并未真正计算Solver瓶颈。

### 整改

建议拆为：

- `GetResourceLoadAsync()`：5号位正式负荷；
- `GetResourceDelayReason...()`：消费1号位Explanation。

V1不建设复杂Bottleneck Ranking。

---

# 9. 修改执行顺序

建议本轮按以下顺序执行，避免再次产生跨文档漂移：

1. 以本文冻结本轮业务与职责；
2. 修改《最终全部流程与业务基线》；
3. 修改有限产能 / Pegging业务说明；
4. 修改《20260901职责裁决》；
5. 修改《集成接口设计》；
6. 修改1～5号位实施包；
7. 修改数据库字段说明 / DDL / 数据架构；
8. 修改核心流程走查；
9. 修改代码审核检查基线；
10. 修改0号位验收包；
11. 更新冻结基线索引；
12. 再按最终文档执行代码整改；
13. 代码整改完成后重新审核；
14. 最终再次读取全部冻结文档，确认无旧口径残留。

---

# 10. 最终冻结结论

本轮用户输出专项最终原则冻结如下：

> **APS V1不再为用户输出单独建设第二套结果平台。**

> **1号位负责把有限产能时间、资源、Setup和排程原因算对；2号位负责把Pegging、Allocation、FinalTask及Explanation等核心运行真相落全；3号位继续负责Run、Domain、Candidate治理，并保留已经成熟的Candidate Diff / Gantt等只读能力；5号位基于正式事实承担新增的订单交付、资源负荷、Setup、Explanation、Pegging/Supply Trace、执行偏差风险、业务战报和版本业务差异统计；4号位负责最终页面与交互。**

> **`OrderScheduleSummary / ResourceLoadSummary / PlanKpiSummary` 保留数据库结构，但降级为可选性能缓存，不再要求2号位补生成器。**

> **APS计划延期必须依据当前PlanVersion下订单FinalTask最终完成时间与CustomerDueDate比较，不得继续用 `Order.DelayStatus` 代替。**

> **Candidate详细Diff只保留一套正式实现，V1优先保留3号位现有成熟代码。**

> **成熟代码不为职责形式统一而强制搬迁；新增能力优先落到5号位和3号位，2号位只承担不可替代的核心真相与运行主链。**

---

# 11. 本轮修订完成后的验收判定

只有同时满足以下条件，本轮用户输出专项才能判定完成：

- 所有受影响冻结文档均已按本文修改；
- 不再存在“Summary三表阶段一必须由2号位生成”的有效冻结描述；
- 不再存在“G4/G7/G8/Schedule必须全部迁5”的强制有效描述；
- 计划延期与Order.DelayStatus已经拆开；
- Candidate Diff不存在两套正式算法；
- Resource Load真正有Available / Planned / Rate；
- Setup KPI可查询；
- Version Trace可回答昨天/今天版本变化；
- 白天Candidate场景全部能输出变化、影响和原因；
- Explanation不得由5号位猜测；
- 4号位仍只直接调用3号位和5号位；
- 2号位未因本轮增加新的页面报表平台；
- 冻结基线索引已更新；
- 最终全量模拟读取审核无冲突。

---

**文档结束**
