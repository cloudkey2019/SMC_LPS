# 确认单：无设备工序规则参数请求 —— 1号位 致 3号位

> 收件：3号位（规则/治理）
> 日期：2026-09-21
> 性质：**请 3号位 维护 OperationPlanningMode ＋ 出 Operation Transition Rule / Stage Transfer Time 规则参数**（1号位 为消费方）

---

## 一、背景（0号位 终版裁决 文件21 定案）

- `OperationPlanningMode` = **APS 工艺规划属性（Operation Planning Attribute）**，不是规则引擎、不是 SetupTransitionRule 类动态规则。载体建议 `OperationPlanningParam` 或 `APS Operation Extension`，表达 `OperationCode + PlanningMode + EffectiveVersion`。
- **3号位 维护，2号位 加载，1号位 消费**。
- 无资源工序**必须**配 `UNCONSTRAINED` / `WAIT_ONLY`，否则 1号位 判异常（fail-closed，禁止无资源自动无限产能）。

## 二、请 3号位 维护（1号位 消费口径）

| 参数 | 语义 | 1号位 消费行为 |
|---|---|---|
| `OperationPlanningMode`（三分类） | `FINITE_RESOURCE`（需资源）/ `UNCONSTRAINED`（占时不占资源）/ `WAIT_ONLY`（只加等待） | FINITE 走现有资源找槽；UNCONSTRAINED 跳过资源、叠加 `StandardDuration×数量` 到链上、不判失败；WAIT_ONLY 跳过资源、叠加等待。改动限 Phase2 正排/倒排分型，Scheduling 层 |
| `Operation Transition Rule` | 部门/Stage 级工序间默认等待+转运（例：机加工内部默认转运 30 分钟） | 在工序间隙 `edge.LagTime` 接入点叠加（`PhaseTwoInitialScheduler.cs:867`） |
| `Stage Transfer Time` | 大工艺间转运时间（例：机加工→表面处理 4 小时） | 同上在跨 Stage 间隙叠加 |

**接入方式**：三个参数独立进 `DomainSolveRequest`（仿 SetupTransitionRules），**不进 Frozen 六块**（0号位 §八 定案）。

## 三、请 3号位 回执

1. `OperationPlanningParam` 载体字段名/类型/示例（键 = 工序标识，值 = 三分类）？
2. Operation Transition Rule / Stage Transfer Time 的规则参数定义 + 默认值？
3. 消费时：规则缺失时回落 Routing LagTime 还是规则优先？（0号位 已裁：当前 LagTime=0 不作为有效事实、不覆盖规则——请 3号位 确认规则缺省兜底行为）

1号位 侧承诺：**通道数据到位当天开消费代码**（Phase2 分型 + 测试，范围限 Scheduling 层，Core DTO 不动）。

## 四、回执表

| 收件 | 动作 |
|---|---|
| 3号位 | ① 维护 `OperationPlanningMode` 分类（载体自定，建议 OperationPlanningParam）；② 出 Operation Transition Rule / Stage Transfer Time 规则参数；③ 回执 §三 三个问题 |
| 1号位 | 通道到位当天开消费代码；等待/转运在 `edge.LagTime` 接入点叠加 |

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）
