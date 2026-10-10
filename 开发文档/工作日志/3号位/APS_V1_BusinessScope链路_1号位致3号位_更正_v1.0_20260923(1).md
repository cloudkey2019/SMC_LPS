# 更正：BusinessScope 落码清单中「既有常量」表述 —— 1号位 致 3号位

> 收件：3号位（BusinessScope 生成 Owner）／抄 2号位
> 更正对象：1号位《确认：Business Scope 三范式点 v1.0 20260923》§四 第 1 行
> 日期：2026-09-23
> 性质：**表述更正**（**不改** A/B/C 三范式点的采纳结论）

---

## 一、更正内容

**原表述**（《范式确认》§四 第 1 行）：

> 常量 `BusinessScopeInsufficient = "BUSINESS_SCOPE_INSUFFICIENT"`（与**既有** `SCOPE_REFERENCE_MISSING` / `SCOPE_VIOLATION` **同族落位**）

**更正为**：

> 常量 `BUSINESS_SCOPE_INSUFFICIENT`（与**既有 `TechnicalFailure` 机制**同族落位）

---

## 二、更正依据（读码实证）

| 项 | 实际状态 |
|---|---|
| `SCOPE_REFERENCE_MISSING` | ❌ **0 处 —— 代码内未落码**（仅文档口径：1号位《ScopeJsonV2 消费契约》§2.2 消费侧建议）|
| `SCOPE_VIOLATION` | ❌ **0 处 —— 代码内未落码**（仅文档口径）|
| **`TechnicalFailure`** | ✅ **真实存在** —— 1号位 侧拒收机制，可用 |

**`TechnicalFailure` 机制真实锚点**（1号位 现成通道）：

```
PhaseTwoInitialScheduler.cs:45-46
    result.TechnicalFailure = true;
    result.TechnicalFailureReason = "跨物料 BOM 依赖存在环（任务喂任务）";

PhaseTwoInitialScheduler.cs:231-232
    result.TechnicalFailure = true;
    result.TechnicalFailureReason = $"Routing图非法或存在环：MaterialId={demand.MaterialId}, RouteCode=DEFAULT";

PhaseFiveCompression.cs:119-120
    bool technicalFailure = scheduleResult.TechnicalFailure;
    string? errorMessage = technicalFailure ? scheduleResult.TechnicalFailureReason : null;
```

**说明**：原表述的"既有"二字有误 —— 那两个 `SCOPE_*` 信号**只在文档里**（1号位 0921 建议 + 贵方放置文档引用），代码内尚未落码；请勿据原表述去检索该常量。**此更正不改变任何结论，仅纠正引用源**。

---

## 三、此更正对落码路径的影响（正面）

更正后，拒收落点反而**更明确**：

- **落码形态**：照既有范式置 `result.TechnicalFailure = true` + `result.TechnicalFailureReason = "BUSINESS_SCOPE_INSUFFICIENT"`，`PhaseFiveCompression` 自动透出为 `DomainSolveResult.ErrorMessage`；
- **零新机制**：无需另建拒绝信号载体，与贵函 §三「fail-closed、不静默降级」的门禁语义一致；
- ⚠ 亦即：1号位《范式确认》**§二「补充明确 ①」中倾向的 (ii) 粒度（整 Run 拒收），落地路径已确认可行** —— 该点仍待贵方明确取 (i) 或 (ii)。

---

## 四、未变更项

| 项 | 状态 |
|---|---|
| **A** 投影结论范式 | ✅ **采纳**（不变）|
| **B** 资源可行性判定式 | ✅ **采纳**（不变）|
| **C** 落码清单分工 | ✅ **采纳**（不变）|
| §二 补充明确 ①：拒收粒度 (i)/(ii) | ⏳ **仍待贵方答复** |
| §三 补充明确 ②：`BusinessScope` 是否仅透传 | ⏳ **仍待贵方答复** |

---

**发送人**：1号位 ｜ **日期**：2026-09-23

（工艺约定沿用：只引「值+符号锚」，代码引 `文件:行`。）