# 3号位 白天Candidate治理 v1.5 差距清单（自检）

> 发件：3号位（规则/参数/认证权限/运行生命周期治理）
> 依据：《3号位实施包 v1.5（20260915 白天业务场景 / Candidate运行治理冻结对齐版）》§十五/§十六(16A)/§十九/§二十六
>       《0号位总体项目验收包 v1.4》§26（DAY-01~28）
> 日期：2026-09-16
> 性质：对 v1.5 白天 Candidate 治理增量做代码核对，产出「已达标 vs 差距」清单，供对齐与派单

---

## 一、审计方法

- `grep` 全仓库 `.cs`：`BusinessTriggerType / PriorityMode / SchemaVersion`（仅命中无关 `EtaInvariantTests.cs`）、`BasePlanVersionId / CanActivate / BASE_PLAN_STALE`、`ScopeJson / ExpectedDomainKeysJson`；
- 精读治理核心：`CandidateRunCreateSpec.cs`、`ScheduleRunGov.cs`、`RunLifecycleService.cs`。

## 二、已达标（无需返工）

| v1.5 要求 | 落地位置 |
|---|---|
| Candidate 严格单 Domain（DomainKey 必填 / RESCHEDULE 类恰 1 Domain）| `RunLifecycleService.CreateCandidateRunAsync` + `ValidateDomainKeys` |
| Base 只读锚定 + 非 ACTIVE / 不存在 / DomainKey 不一致拒绝 | `RunLifecycleService.cs`（Base 解析段）|
| 旧 Purpose×RunType 合法组合（3 类）| `LegalPurposeByRunType` |
| 激活前置：最小人工确认 + 来源 Run 可激活（INSERT_ORDER_WHATIF 永不激活）| `ActivateCandidateAsync` + `EnsureSourceRunActivatableAsync` |
| 激活原子替换同域旧 ACTIVE | `ReplaceActiveAsync` |
| ExpectedDomainKeys 冻结 + FAILED 恢复新建、不回改 RUNNING | `ValidateDomainKeys` / `RecoverFailedRunAsync` |
| Business Scope Domain 维度后端硬校验（fail-closed → 403）| `EnsureDomainInScopeAsync` |

## 三、差距清单（v1.5 增量未落地）

| # | 差距 | v1.5 章节 | 证据 |
|---|---|---|---|
| G1 | Candidate Input Schema v2 未实现：无 `SchemaVersion` / `BusinessTriggerType` / `PriorityMode` 字段；无 `OrderTargets[]` / `TaskTargets[]` / `ChangedResourceIds[]` 承载 | §十五 | `CandidateRunCreateSpec` 仍旧三字段；grep 治理侧无上述 token |
| G2 | 8 类 `BusinessTriggerType↔RunType↔Purpose↔PriorityMode` 合法矩阵未实现；一 Run 一 BusinessTriggerType、混合场景拒绝（`GANTT+EXPEDITE` 等）无代码 | §十九 | 现仅 3 RunType 旧 Purpose 映射，无 BusinessTriggerType/PriorityMode 维度 |
| G3 | 激活过期拒绝（BASE_PLAN_STALE）未实现：激活前未复核「当前 ACTIVE 仍 == 该 Candidate 的 Base」；无禁止 rebase 绕过重算护栏 | §16A | `ActivateCandidateAsync` 无 stale 复检分支 |
| G4 | 白天场景验收矩阵 DAY-01~28 无代码映射（场景合法性与拒绝规则无校验/无测试）| §26.2 | 无对应校验或测试 |
| G5 | ScopeJson 仍旧语义（注释「v5.1.4 起仅保留只读」），未升 Schema v2 | §十五/§26.3 | `ScheduleRunGov.ScopeJson` |

## 四、结论

v1.5 白天 Candidate 治理的**运行生命周期骨架（Base 锚定 + 单域 + 激活原子替换 + 权限）已到位**；但 **Input 治理层整体缺失**：G1（Schema v2 承载）为根，G2（场景矩阵）依赖 G1，G3（激活过期）为独立补齐项，G4 为验收矩阵落地，G5 为存量语义升级。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）