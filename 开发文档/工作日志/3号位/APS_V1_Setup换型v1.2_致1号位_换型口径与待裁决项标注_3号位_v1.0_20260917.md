# 回执：Setup换型 v1.2 口径与待裁决项标注 —— 3号位 致 1号位

> 收件：1号位（排程算法）／抄 0号位、2号位
> 发件：3号位（规则/参数/运行生命周期治理）
> 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》
> 日期：2026-09-17
> 性质：换型口径翻转通知 + 待裁决项标注。**本件不含任何自定的冻结数值**，凡数值/解释码一律挂「待 0号位 终裁」。

---

## 〇 一句话结论

Setup 从「属性式换型」翻转为**「当前工序 + 当前设备 + 前产品 + 后产品 → SetupMinutes」规则模型**，你侧 `SetupOptimizer` 需重写。本件把「已定死、可立即接」与「待 0号位 裁决、勿自定值」两截分清楚。

---

## 一、已定死、可立即接的口径（不依赖 0号位）

| # | v1.2 口径 | 对你侧含义 |
|---|---|---|
| 1 | 属性式废止 | 删 `SchedulingContext.SetupAttribute`（Mold/Tool/Material/Color 判型不再存在）；3号位 已删 `Dimensions` |
| 2 | 规则查表 | 换型时间由 `SetupTransitionRule` 查表得到，命中顺序 **EXACT → DEFAULT → 无规则 0 分钟** |
| 3 | 真实占用 | Setup 是真实资源占用（非排序分数），计入 Resource Timeline / Start / End / 冲突 / 交期 / 稼动 |
| 4 | 换型键 | 一律用「被排入的当前 Task 自身 `OperationCode` + Resource + 前产品 + 当前产品」，**不读上一 Task 工序**（废止 FromOperation/ToOperation 模型） |
| 5 | 求解阶段 | 初始 Resource/时间槽评价阶段即消费 Setup，随后序列优化再消费（不能排完再补） |
| 6 | 生产日边界 | 生产日只是搜索边界，**不重置设备上一产品状态** |
| 7 | 初始设备状态 | 无上一产品 → 初始设备状态 Setup=0 + Explanation |
| 8 | 禁令 | 禁读 3号位 库、禁自维护规则、禁 `RoutingOperation.SetupTime` fallback、禁全厂/90 天全排列、禁建第二套 Solver |

---

## 二、待 0号位 裁决、不可自定的事（已提 0号位，编号=裁决请求内编号）

| 项 | 内容 | 对你侧影响 | 状态 |
|---|---|---|---|
| 裁决项 1 | 三预算参数**数值**：有界搜索预算 / 最大邻域尝试次数 / 单生产日保护阈值 | 夜间有界序列优化直接消费 | **待 0号位 定值** |
| 裁决项 2 | Setup 规则在 `FrozenStrategySnapshot` 的承载方式（建议方案 A：随 RuleSetVersionId 锚装载） | 决定你从哪读规则 | **待 0号位 定** |
| 裁决项 3 | Q1 生产日识别口径 | 夜间 FULL 的搜索边界实现依赖它 | **待 0号位 定** |
| 裁决项 4 | Q4「初始设备状态」Explanation 码值 | 第 7 条的码值未定 | **待 0号位 定** |

---

## 三、旧锚不沿用警告（重要）

`SetupLookAheadSize = 5`、`DefaultSetupMinutes = 30 / 45` 是**旧模型**的数值，v1.2 已废止（全局统一 30 分钟被禁止）。**不要拿它们写新代码**——新预算值以 0号位 终裁为准，在未定前只在代码里留参数位。

---

## 四、你侧待改动作（对应《影响对照清单 v1.0》§四）

| 现状 | v1.2 口径 | 动作 |
|---|---|---|
| `SchedulingContext.SetupAttribute` | 废止 | 删除 |
| `SetupOptimizer` 按属性判型 + default 30min | 规则查表 | 重写为查 `SetupTransitionRule` |
| 夜间 FULL 现有逻辑 | 单 Resource × 生产日 × 固定锚点间可移动段，全候选 + 有界邻域搜索，不枚举排列 | 改 |
| 白天 Candidate | 局部优先，重算受影响邻接 Setup，不洗牌全天 | 增强 |
| — | 初始设备状态 Setup=0 + Explanation | 补覆盖 |

> 数据通道：Setup 规则查找结构由 **2号位** 装入 `DomainSolveRequest`（§六.2），你**换读**即可，不自建读取。

---

## 五、给你的建议节奏

1. **现在即可开工**：第 1~8 条口径 + 删 `SetupAttribute` + `SetupOptimizer` 骨架重写。
2. **预算数值 / Explanation 码 / 生产日边界**：留参数位，等 0号位 终裁回填。
3. **规则数据来源**：等 2号位 的 `DomainSolveRequest` 查找结构（头号阻塞在他那），你不自建。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）