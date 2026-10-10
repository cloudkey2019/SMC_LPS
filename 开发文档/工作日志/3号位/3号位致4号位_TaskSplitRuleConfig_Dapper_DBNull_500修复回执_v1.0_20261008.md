# 3号位 → 4号位 — TaskSplitRuleConfig POST/PUT 500（Dapper nullable DBNull）P0 修复回执

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 发件 | 3号位（规则参数治理 owner / 后端） |
| 收件 | 4号位（前端 owner） |
| 抄送 | 2号位（DDL 红线 #6 owner，仅信息同步，无动作请求） |
| 主题 | TaskSplitRuleConfig POST/PUT 500（任意 nullable 字段 DBNull）— **P0 已修复（方案 A）+ 同类 P0' 连带修复** |
| 优先级 | P0 已闭环（修复 + 真实 dev 库集成测试验证通过） |
| 关联 | [4号位-2026-10-08-TaskSplitRuleConfig-Dapper-任意nullable字段DBNull-500-给3号位.md](4号位-2026-10-08-TaskSplitRuleConfig-Dapper-任意nullable字段DBNull-500-给3号位.md) |

---

## 一、结论

4号位 指认的「任意 nullable 字段为 null → POST/PUT 500（Dapper `NotSupportedException`）」系统性问题已**按方案 A 根治**，并连带修复同构的 `StageLeadTimeParamGovernanceService`（同类反模式，P0'）。真实 dev 库集成测试验证通过，`@Dept IS NULL` 无需改 SQL（见 §三）。

## 二、修复内容（方案 A：3 参 `Add(name, value, DbType)`，null 值原生支持）

### 2.1 `TaskSplitRuleConfigGovernanceService.cs`（P0 主案）

| 方法 | 修复参数 | DbType |
|---|---|---|
| `CreateAsync` / `UpdateAsync` | `ProductionDepartmentId` | `Int32` |
| 同上 | `MinExecutionBatchQty` / `MaxExecutionBatchQty` / `PreferredBatchQty` | `Decimal` |
| 同上 | `MaxOptimizationSplitCount` / `MaxBatchCandidates` | `Int32` |
| 同上 | `BottleneckSplitStrategy` / `NonBottleneckStrategy` | `String` |
| 同上 | `EffectiveFrom` / `EffectiveTo` | `DateTime` |
| `EnsureNotDuplicateAsync` | `@Dept` | `Int32` |

共 11 个 nullable 参数 × 2 方法 + `@Dept`，全部从 `(object?)X ?? DBNull.Value` 改为显式 DbType。

### 2.2 `StageLeadTimeParamGovernanceService.cs`（P0' 同类，连带修复）

| 方法 | 修复参数 | DbType |
|---|---|---|
| `CreateAsync` / `UpdateAsync` | `ProductionDeptCode` / `MaterialCode` / `ProductFamilyCode` | `String` |
| 同上 | `EffectiveTo` | `DateTime` |
| `EnsureNotDuplicateAsync` | `@Dept` / `@Material` / `@Family` | `String` |

> 依据：该文件与 TaskSplitRuleConfig **完全同构**（string/DateTime nullable + 相同 NULL==NULL 去重 SQL），同一缺陷模式，一并根治，避免 4号位 后续在 StageLeadTimeParam 页面再踩同一 500。

## 三、关于前置函件遗留的 `@Dept IS NULL`

**无需改 SQL。** 现有去重 SQL 已正确实现 NULL==NULL 匹配：

```sql
((@Dept IS NULL AND ProductionDepartmentId IS NULL) OR ProductionDepartmentId = @Dept)
```

4号位 担心的「`@Dept IS NULL` 命中 Dapper 其它路径编译错误」其根因正是**参数无 DbType**——已通过 `DbType.Int32` 根治。前置函件方案 A 的 `ISNULL(ProductionDepartmentId, -1) = ISNULL(@Dept, -1)` **不需要**，且会引入哨兵值 -1 的语义污染，不采纳。

## 四、验证证据（真实 dev 库）

新增集成测试 `TaskSplitRuleConfigGovernanceServiceIntegrationTests`（3 用例，直连 APS_Production）：

1. **CreateAsync 全部 nullable 字段 null + productionDepartmentId=null** → 成功入库（复现函件 §二 触发 A+B）
2. **同 Material + NULL Dept 二次 Create** → 命中去重 `@Dept IS NULL` 分支，抛去重异常
3. **UpdateAsync 部分 nullable 字段 null** → 成功更新

定向测试全绿：

| 测试集 | 结果 |
|---|---|
| TaskSplitRuleConfig（3 集成 + 6 投影单测） | **9/9** |
| StageLeadTimeParam（既有单测） | **14/14** |
| FrozenStrategySnapshotProvider（含 2 真实库） | **24/24** |
| build | **0 错误** |

> 测试物料为运行时挑选的「无既有规则的既有 Material」（满足 FK→Material.Id），Dispose 仅清理自建数据，不触碰 4号位 curl 数据。

## 五、请 4号位 侧动作

| 阶段 | 动作 |
|---|---|
| 现在 | 移除 Drawer 顶部红警「非必填字段不填也会报服务器内部错误…」；optional 字段 placeholder 恢复「（留空 = 不启用）」 |
| 复跑 verify | TSRC14 动态断言：POST `productionDepartmentId=null` + 6 字段 null → **code=201** |
| 复验 | StageLeadTimeParam CRUD 任意 nullable 字段留空 → 不再 500（同类已修） |

## 六、其它

- 认可 4号位「不在请求范围」约定：后端**不注入哨兵值**（如 `MaxOptimizationSplitCount=999999`），不污染业务数据。
- 本修复**不改数据契约**（nullable 语义不变）、**不改冻结文档**、**无 DDL**（DML 层修复，红线 #6 不涉）。
- 抄送 2号位：仅信息同步，无动作请求。

---

> 按 3号位 ↔ 4号位 中转沟通方式，本回执仅发 4号位 + 抄送 2号位。
