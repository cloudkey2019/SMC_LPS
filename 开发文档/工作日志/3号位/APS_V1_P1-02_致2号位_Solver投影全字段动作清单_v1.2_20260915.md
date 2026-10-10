# 致 2号位：P1-02 Solver 投影全字段动作清单 —— v1.2

> 收件：2号位（数据引擎+编排）
> 发件：3号位（策略参数治理：冻结 Snapshot + 发布前校验）
> 依据：0号位《关于 P1-02 Solver 策略存量字段处置的正式回复》（终裁）+《关于"传播轮数10 / 瓶颈阈值0.85~0.90"归属的补充裁决》；3号位《P1-02 回执 v1.2》§五
> 日期：2026-09-15
> 性质：请确认并回执

---

## 一、结论一句话

0号位 已终裁：P1-02 Solver 策略字段**全部保留并须实际消费**；代码未消费＝投影/实现缺口。1↔2 契约 DTO `FiniteCapacityParameters` 目前只有 4 字段、投影丢弃了 ⑤⑥ 全量——这是 1号位 硬编码的根因。2号位 请按下面扩投影。

## 二、动作清单

### 1. 投影范围扩大为 ⑤＋⑥ 全字段

不再只投「行为即变 3 字段」；须投：

- `SolverStrategyBlock` 全量（含 `BottleneckMode` 四模式／`OnTimeTarget`／`Setup`／`StageOverlap`）
- 整块 `CandidateGuardrailBlock`（含传播轮数 `10` 等）
- 3号位 新增的**瓶颈阈值字段**（`0.85` 识别／`0.90` 严重，属动态瓶颈策略）

### 2. 投影 ≠ 立刻消费

字段全量投影即可，消费点由 1号位 逐项接（Setup / OnTimeTarget / StageOverlap / BottleneckMode 均为 1号位 实现缺口）。

### 3. `Mode`↔`SchedulingDirection` 字符串映射在投影处正式化（保留）

### 4. 同步扩展 `FiniteCapacityParameters`

3号位 补瓶颈阈值字段就位后，请 2号位 同步扩展 1↔2 契约 DTO `FiniteCapacityParameters` 并接投影。

> **归属澄清（0号位 补充裁决）**：瓶颈阈值与传播轮数均**留在 3号位 治理**（动态瓶颈策略／Candidate Guardrail），经 2号位 随 `StrategySnapshot` 传递、1号位 消费；只有 `MaxIterations` 划给 1号位，无需 2号位 投影治理。

## 三、回执表

| # | 事项 | 确认（是/否）| 时序 | 备注 |
|---|---|---|---|---|
| 1 | 投影扩为 ⑤⑥ 全字段 | | | |
| 2 | 投影只投不消费（消费在 1号位）| | | |
| 3 | Mode↔SchedulingDirection 映射正式化 | | | |
| 4 | FiniteCapacityParameters 扩展 + 接投影 | | | |