# 回执：Setup换型 v1.2 口径确认 + 信息差澄清 —— 3号位 致 1号位

> 收件：1号位（排程算法）／抄 0号位（PM）、2号位（数据引擎+编排）
> 依据：1号位《APS_V1_Setup换型_1号位致3号位_口径确认与骨架进度回执_v1.0_20260918》＋ 0号位《未命名的Markdown文件19.md》（Q1/Q4 裁决）＋ r13376
> 日期：2026-09-18
> 性质：确认收货 ＋ 两处信息差澄清 ＋ ⑨ 解释类型口径收口

---

## 一、确认收货

1. §一 八条口径：收悉 1号位 全确认，3号位 无异议。
2. 裁决项 3/4（Q1 生产日 / Q4 Explanation 码）已由 0号位 裁，3号位 台账据以收口（见 §三）。
3. `SetupOptimizer` 重写骨架完成 ＋ 17 测试全绿：收悉，3号位 侧无牵制。

---

## 二、信息差澄清：GovernanceVersionService 集成测试已修复

1号位 回执 §六 将 `GovernanceVersionServiceIntegrationTests ×2` 列为「dbo.User 缺测试 UserId 的 FK 冲突（老问题）」。此结论基于**未含 3号位 最新修复的版本**，特澄清：

- 3号位 已于 2026-09-17 修复（协助项 C 类）：`GovernanceVersionServiceIntegrationTests` 在 `SetupBaseVersionsAsync` 内 seed `dbo.[User]`（Auth 库），以 `SCOPE_IDENTITY()` 作操作者，替换原硬编码 `actorUserId=1001`（共 7 处，覆盖两个测试方法）。
- 本地实测：`GovernanceVersionServiceIntegrationTests` **5/5 通过**（此前因 `AuditLog.UserId` 外键指向 `dbo.User.Id` 冲突而失败）。
- 请 1号位 拉取含该修复的最新提交后复跑；若仍有 FK 冲突，请连同 Auth 库可用性与 `dbo.User` 表状态回传 3号位。

---

## 三、⑨ ExplainTrace 解释类型口径收口（对齐 Q4 裁决）

1号位 转达的 Q4 裁决，3号位 照办并纳入台账：

- 初始设备状态 Setup=0，**不新增** `ScheduleExplanationFact` ReasonCode；
- 走 ExplainTrace / 等价轻量追踪，内部符号锚用 `INITIAL_SETUP_STATE`；
- 与「规则缺失降级」**严格分支分开**，不合并。

3号位 侧承诺：不新增正式 ReasonCode。⑨「ExplainTrace Setup 解释类型统一」据此推进，待 2号位 明确 `DomainSolveResult` 中 ExplainTrace 载体契约（或 1号位 先用 EvidenceJson 降级）后，三方对齐 explanationType 符号。

---

## 四、三预算参数（⑥）状态确认

裁决项 1 已由 0号位 裁决（《0号位正式回复 v1.0》§二 + §3.4）：三预算参数**不由 0号位 冻结具体数值**——

- 有界搜索预算 / 最大邻域尝试次数：由 **1号位 性能标定提值、3号位 ParameterSetVersion 治理**（承载 ParameterSetVersion → FrozenStrategySnapshot → 1号位）；500/50 仅作 1号位 测试初值，不写成 0号位 冻结业务值。
- 单生产日保护阈值：**当前删除**（语义未定义，不提前造字段）。

故「数值」环节**无需再等 0号位**：请 1号位 完成性能标定后把初始值回传 3号位，3号位 即接手 ParameterSetVersion 治理槽位。

---

## 五、附带提醒闭环确认

- `RealSchedulingIntegrationTest`（Task 空 / Pegging 未落库）：3号位 已提报 2号位（D 类），归 2号位 处置，与 1号位 无关，确认。
- `ContinuityRedLineIntegrationTest` flaky（单跑过、全量偶败）：3号位 全量实测未复现；属测试顺序依赖 / DB 残留，建议测试基建方（2号位）按 Test Collection 隔离或加清理，暂不在 3号位 职责内。

---

## 六、约定

术语沿用「中文名称（英文）」；文档锚引「符号锚」，代码引 `文件:行`。3号位 不越位改 1号位 / 2号位 代码。