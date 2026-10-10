# 回执：P1-02 Solver 策略冻结参数 —— v1.1 收口修订

> 收件：0号位（PM）／1号位（排程算法）／2号位（数据引擎+编排）
> 发件：3号位（策略参数治理：冻结 Snapshot + 发布前校验）
> 依据：2号位 对《P1-02 回执 v1.0》的回执（4 处修正）；1号位《硬编码值清单回执》
> 日期：2026-09-14
> 版本：v1.1（吞 2号位 修正；v1.0 作废存档）

---

## 一、结论一句话（v1.1 修订）

v1.0 核心命题——「1号位 硬编码真因是 2号位 投影只带 4 字段，而非 3号位 未冻结」——**已被 2号位 核实成立**。但 2号位 的纠偏成立、必须采纳：**⑤⑥ 里大部分字段在 1号位 根本无消费点**，投影过去也是死数据。故 P1-02 的「9 类策略」是虚数，真·闭环面只有 **行为即变 3 字段 + live 硬编码 5 项**；其余属死代码 / 数据驱动 / aspirational，是「0号位 裁决留·砍·移」的**存量**，不是「3号位 要冻结的参数」。

---

## 二、认领 2号位 4 处修正（全部采纳）

| # | 2号位 修正 | 3号位 认领 | 落地 |
|---|---|---|---|
| 1 | ⑤⑥ 大部分字段 1号位 无消费点；SetupOptimizer/TimeSlotFinder 是死代码（`FiniteCapacitySolver.cs:27`、`SchedulingServiceExtensions.cs:20-25`）；三阶段闭环只对 3 字段成立 | ✅ | §三 重定四分台账 |
| 2 | Split 候选行号错（`TrySplitOperation`，非原 :1068）；AllowSplit 消费在 Phase4 | ✅ | §三 改「值+符号锚」 |
| 3 | 漏拆分下限 `0.1`（`qtyPerSplit`） | ✅ | 并入台账 B 组 |
| 4 | MaxIterations 真实在 `Core SchedulingOptions.MaxIterations=1_000_000`（死路径），冻结 1000 脱节 | ✅ | §四 存留问题集 |

---

## 三、收敛台账：⑤⑥ 全字段「四分法」（本回执核心）

| 分组 | 字段 / 硬编码 | 1号位 消费锚 | 处置 |
|---|---|---|---|
| **A 行为即变** | `AllowSplit` / `AllowMerge` / `SchedulingDirection` | Phase4 拆批 / Phase2 合批 / Phase2 倒正排 | 已投影已消费，不再动 |
| **B live 硬编码** | 瓶颈阈值 `0.85`（识别）/ `0.90`（严重） | `PhaseThreeDiagnostics` inline | **3号位 补字段** → 2号位 投影 → 1号位 换读 |
| | 传播警戒 `30%` | `maxAffectedRatio` inline | 已冻结 `ImpactedTaskWarningPercent=30`，缺投影+换读 |
| | 传播轮数 `10` | `maxPropagationRounds` inline | 已冻结 `MaxPropagationRounds=10`，缺投影+换读 |
| | Split 候选 `{2,3}` | `TrySplitOperation` inline | 已冻结 `SplitAlternatives=3`，缺投影+换读 |
| | 拆分下限 `0.1` | `qtyPerSplit` inline | **3号位 改默认 `MinBatchQty=0.1`** |
| **C 死代码** | Setup `30/5` | `SetupOptimizer`（死） | **0号位 裁决**：留档 vs 复活落地 |
| **D 存留待裁** | `OnTimeTarget` | 无（数据驱动 `RequiredAvailableTime`） | **0号位 裁决**：留/砍/移 |
| | `StageOverlap` | 无（数据驱动 Routing `TransferBatchSize`） | 同上 |
| | `BottleneckMode` 3 枚举 | 无（仅 Auto 阈值） | 同上（aspirational） |
| | `MaxIterations` | `SchedulingOptions=1_000_000`（死路径） | 同上（脱节） |
| | 早做惩罚 / 稳定性偏好 | 无（算法结构既定） | 同上（是否新增可配字段） |

---

## 四、请 0号位（PM）裁决（存量问题集，4 组）

| # | 问题 | 选项 | 影响 |
|---|---|---|---|
| 1 | Setup 优化（死代码）本阶段是否落地？ | □ 留档冻结、不消费 | 冻结 `Setup` 字段保留，1号位 不复活 SetupOptimizer |
| | | □ 落地（1号位 复活 + 3号位 出参） | 新增 1号位 工作量，3号位 补参数链路 |
| 2 | `OnTimeTarget`/`StageOverlap`/`BottleneckMode` 三块（1号位 数据驱动/无消费）留·砍·移？ | □ 留（业务级治理，标注「归档」） | 冻结模型保留、注明非 Solver 消费 |
| | | □ 砍（YAGNI） | 从 ⑤ 移除，缩小契约 |
| 3 | 早做惩罚 / 稳定性偏好 是否需新增可配字段？ | □ 需配（3号位 补字段） | 3号位 冻结 + 1号位 消费 |
| | | □ 算法既定、不配 | 从 P1-02 范围移除 |
| 4 | `MaxIterations` 冻结值对齐？ | □ 对齐 `1_000_000`（死路径值） | 仅形式对齐、无实际意义 |
| | | □ 保留 `1000` 留档 / 移除 | 契约变干净 |

---

## 五、请 2号位 确认（投影 ≠ 消费）

2号位 已同意 §六问 2（投影点补齐）。此处新增一关键边界，请 2号位 回执确认：

1. **行为即变 = 仅 `AllowSplit/AllowMerge/SchedulingDirection` 三字段**（现状已投影已消费）。
2. **B 组 5 项 live 硬编码**：是否同意「参数先投影进 `FiniteCapacityParameters`，1号位 换读后逐项激活」？—— 投影完整 ≠ 立刻被消费，字段先行、消费点由 1号位 分阶段接。
3. Mode↔SchedulingDirection 字符串映射在投影处正式化（live 字段、属行为即变，需固定契约）。

---

## 六、请 1号位 确认

1. SetupOptimizer/TimeSlotFinder 是否确认为**死代码**？（2号位 已核；1号位 清单把 Setup 30/5 列为现值但未标死，请确认口径一致）
2. B 组 5 个 live 硬编码换读点位（瓶颈阈值 / 警戒 30% / 轮数 10 / Split {2,3} / 下限 0.1）是否确认，并承诺参数就绪后替换为 `StrategySnapshot.Parameters` 读取？
3. MaxIterations 实际迭代上限确认：当前求解是无循环 Phase1-5 流水线，`SchedulingOptions.MaxIterations=1_000_000` 是否同为死路径？

---

## 七、3号位 本轮动作 + 工艺约定

**3号位 侧（待 0号位 存量裁决回复后落地；改动仅限 2↔3 契约，不越 2号位 编排、1号位 算法）：**

- `SolverStrategyBlock` 补**瓶颈识别阈值字段**（0.85 识别 / 0.90 严重）——B 组唯一缺字段项；
- `Split.MinBatchQty` 默认 `0` → `0.1`；
- `SolverStrategyValidator` 加两条校验（阈值 0~1、MinBatchQty>0）。

**工艺约定（建议三方采纳）**：此后冻结/回执一律只引用「**值 + 符号锚**」（`TrySplitOperation` / `qtyPerSplit` / `maxPropagationRounds` / `maxAffectedRatio`），**不再 cite 行号**——本轮三方行号已漂（3号位 :1068 → 1号位 :1249 → 2号位 :1140），行号引用不可靠。

---

## 八、回执表（三方各自回复）

| 收件 | 请回复 | 章节 |
|---|---|---|
| 0号位 | 4 组存量裁决 | §四 |
| 2号位 | 「投影 ≠ 消费」边界确认（3 条） | §五 |
| 1号位 | 死代码口径 + B 组 5 点换读承诺 + MaxIterations 确认 | §六 |