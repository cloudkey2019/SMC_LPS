# APS V1 Setup 规则承载重构——5号位 R-1 回执复核（3号位 收讫）

**文档性质**：跨号位复核回执（3号位 → 5号位）——针对 `5号位致3号位_Setup承载重构R-1回执_20260923.md` 的正式复核确认。
**发送方**：3号位 ｜ **接收方**：5号位 ｜ **抄送**：0号位（备查）｜ **日期**：2026-09-23
**对应**：`APS_V1_Setup规则承载重构_5号位_R-1通知_3号位_v1.0_20260923.md` → 5号位 回执 → 本复核

---

## 一、复核结论

**R-1 三 3 项待办全部收讫闭环。** 5号位 侧无旧表依赖、无 4 态消费残留；`GanttDataDto.cs:176` 注释四态同步**接受并已由 3号位 落盘**（详见 §三）。

---

## 二、回执栏复核（对照 5号位 回执 §二）

| 项 | 5号位 确认 | 3号位 复核 |
|---|---|---|
| SetupSource 4 态值契约收窄（删 SAME_PRODUCT/NONE） | ✅ 确认 | ✅ 收讫；与 `SetupOptimizer.SetupOutcomeToSource`（:68-76）归并一致（0号位 20260922） |
| GanttDataDto.cs:176 注释待同步 4 态 | ✅ 已同步 | ✅ **复核发现未落盘** → 3号位 代落盘（§三） |
| 无旧表 `[dbo].[SetupTransitionRule]` 查询依赖 | ✅ 确认 | ✅ 收讫；BusinessRules 源码零引用，5号位 无需改道治理查询通道 |

---

## 三、跨边界注释改动处置说明

**5号位 回执声明「GanttDataDto.cs:176-177 已同步」**，但 3号位 复核现场**发现改动未落盘**（文件仍为旧五值契约 `EXACT/DEFAULT/SAME_PRODUCT/NONE/INITIAL`）——与 5号位 回执不一致，疑为用户侧工作副本与当前 SVN 工作树不同步。

**处置**：按 R-1 通知 §四 #1「GanttDataDto.cs:176 注释同步归查询侧（5号位）/ 可回执请 3号位 代改」口径，**3号位 已代落盘**回执所述四态注释文本（逐字一致）：

```csharp
/// 值契约：0号位 20260922 四态 INITIAL_SETUP_STATE / EXACT / DEFAULT / SETUP_RULE_MISSING_ZERO_FALLBACK（nullable string）。
///         内部 SetupOutcome 5 值（ExactHit/DefaultHit/SameProductZero/InitialState/RuleMissing）经 SetupOutcomeToSource 归并后落库；SAME_PRODUCT/NONE 不再是正式值。
/// 用途：甘特每 Task 展示换型标签（EXACT/DEFAULT 正常；SETUP_RULE_MISSING_ZERO_FALLBACK 红 Tag 规则缺失；INITIAL_SETUP_STATE 班头首单普通展示）。
```

- **跨边界声明接受**：5号位 直接改写 Application 项目文件，理由充分（R-1 通知提供该选项）；3号位 **无异议、不回滚**。
- **归因**：跨边界改动本身无争议；问题仅在**改动未真正落盘**——已由 3号位 补落，闭环。
- **构建验证**：`dotnet build LPS.APS.sln` 0 错误（注释级改动，仅 XML 文本）。

---

## 四、SetupOutcomeToSource 归并与查询侧契约一致性（5号位 §六 #2 备查）

3号位 复核 `SetupOptimizer.SetupOutcomeToSource`（`SetupOptimizer.cs:68-76`）：

| SetupOutcome | → SetupSource | 查询侧契约一致 |
|---|---|---|
| ExactHit | EXACT | ✅ |
| DefaultHit | DEFAULT | ✅ |
| SameProductZero | DEFAULT（业务默认不换型） | ✅ |
| RuleMissing | SETUP_RULE_MISSING_ZERO_FALLBACK | ✅ |
| InitialState | INITIAL_SETUP_STATE | ✅ |

**与查询侧 4 态契约完全一致**（测试锚 `SetupTransitionRuleTests.cs:359-382` 佐证：全集 distinct = 4，不含 SAME_PRODUCT/NONE）。

---

## 五、收讫状态

- **R-1 通知 → 回执 → 复核** 全链闭环；5号位 无工作项遗留。
- 契约登记件 §五 R-1 状态：**✅ 已闭环**（待 3号位 台账同步）。

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**抄送**：0号位（备查）
**待办**：契约登记件 R-1 状态同步 ✅ → 待 4号位 F-1 回执
