# 3号位 → 4号位：TaskSplitRuleConfig（执行批拆分规则 / 批量策略）维护接口契约（v1.0）

> 发件：3号位（规则参数治理 Owner）｜ 致：4号位（前端）｜ 抄：0号位、1号位、2号位、5号位
> 日期：2026-10-07 ｜ 版本：v1.0
> 依据：0号位 2026-10-07《批量正倒排（Batch Policy + Direction）裁决》= TaskSplitRuleConfig 归 3号位 规则参数体系、载体「两级承接」
> 性质：红线 #5 接口即契约——**契约先行**，后端落码与前端对接据此执行

---

## 〇、速答

```
归属        : 3号位 规则参数体系（Batch Policy 批量策略，0号位 2026-10-07 裁决本轮落码）
粒度        : MaterialId + ProductionDepartmentId（ProductionDepartmentId NULL = Material 级默认；同一物料最多一条 NULL 部门规则）
载体        : 两级承接——主题表 CRUD 即最终态 + 发布时投影进 ParameterSetVersion.ContentSnapshotJson.BatchPolicy 子块（供 1号位 经 FrozenStrategySnapshot.BatchPolicy 消费）
权限        : 查看 aps.rule.view；维护 aps.rule.edit（复用 Rule 码，零权限种子变更）
停用        : DELETE 软删 IsActive=0（不物理删，保留历史与生效窗口）
DDL         : 零改动（表已由 2号位 冻结于 DDL v5.1.8.3；结构变更归 2号位 红线 #6；本轮仅 DML 治理 CRUD）
```

---

## 一、端点（前缀 /api/governance）

| # | 方法 | 路由 | 鉴权 | 语义 |
|---|---|---|---|---|
| 1 | GET | `/task-split-rule-configs` | aps.rule.view | 列表（可过滤） |
| 2 | POST | `/task-split-rule-configs` | aps.rule.edit | 新增 |
| 3 | PUT | `/task-split-rule-configs/{id}` | aps.rule.edit | 编辑（整对象替换，不改 IsActive） |
| 4 | DELETE | `/task-split-rule-configs/{id}` | aps.rule.edit | 停用（IsActive=0） |

响应统一 `ApiResponse<T>`（失败字段 `error`，保留真实 HTTP code；与 Setup/StageLeadTimeParam 等治理端点同轨）。

---

## 二、查询参数与请求体

### 2.1 GET 列表查询参数（全部可选）

`materialId`（int?）/ `productionDepartmentId`（int?）/ `isActive`（bool?）

排序：`MaterialId, ProductionDepartmentId, Id`。

### 2.2 POST / PUT 请求体 `SaveTaskSplitRuleConfigRequest`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| MaterialId | int | ✅ | 物料（正式业务键） |
| ProductionDepartmentId | int? | 否 | 生产部门（NULL = Material 级默认） |
| MinExecutionBatchQty | decimal? | 否 | 硬最小执行批量 |
| MaxExecutionBatchQty | decimal? | 否 | 硬最大执行批量（NULL = 无硬上限） |
| PreferredBatchQty | decimal? | 否 | 软偏好切点批量 |
| AllowSplit | bool? | ✅ | 是否允许拆分（须显式提供，无默认值） |
| AllowMerge | bool? | ✅ | 是否允许合并（须显式提供，无默认值） |
| MaxOptimizationSplitCount | int? | 否 | 仅限优化性拆分搜索的最大拆分次数 |
| MaxBatchCandidates | int? | 否 | 单问题最多评估的候选数 |
| BottleneckSplitStrategy | string? | 否 | `PREFER_SPLIT` / `PREFER_MERGE` |
| NonBottleneckStrategy | string? | 否 | `PREFER_LARGE_BATCH` / `PREFER_SMALL_BATCH` |
| EffectiveFrom | DateTime? | 否 | 生效起始 |
| EffectiveTo | DateTime? | 否 | 生效截止（null = 永久） |

> **兼容字段不暴露**：`ResourceGroupId` / `MinimumOrderQuantity` / `EconomicOrderQuantity` 仅历史兼容，不进请求体、不进 DTO、不投快照（V1 正式匹配只用 Material + 部门）。

### 2.3 响应体 `TaskSplitRuleConfigDto` = 请求体字段 + `Id / IsActive / CreatedAt / UpdatedAt`（无兼容字段）

---

## 三、校验与错误（fail-fast）

| 场景 | 状态码 |
|---|---|
| MaterialId ≤ 0 | 400 |
| ProductionDepartmentId ≤ 0 | 400 |
| AllowSplit / AllowMerge 省略（须显式提供） | 422 |
| MinExecutionBatchQty / MaxExecutionBatchQty / PreferredBatchQty 为负 | 422 |
| MaxExecutionBatchQty < MinExecutionBatchQty | 422 |
| MaxOptimizationSplitCount / MaxBatchCandidates 为负 | 422 |
| BottleneckSplitStrategy 非 `PREFER_SPLIT` / `PREFER_MERGE` | 422 |
| NonBottleneckStrategy 非 `PREFER_LARGE_BATCH` / `PREFER_SMALL_BATCH` | 422 |
| EffectiveTo < EffectiveFrom | 422 |
| 业务键重复（MaterialId + ProductionDepartmentId 全等） | 422 |
| id 不存在 | 404 |
| 未授权 | 403 |

---

## 四、审计

写 / 停用均落统一 `AuditLog`（EntityType=`TaskSplitRuleConfig`，ActionCode=Create/Update/Deactivate），带真实 actorUserId/actorUserCode（后端从身份取，不信任请求体）。

---

## 五、前端对接提示（4号位）

- 执行批拆分规则维护页数据源即本 4 端点，**无需 mock**（表当前按 dev 库实际行显示）。
- 排序/分页由前端决定是否需要；后端现返全量（规则参数行数有限，未分页）。
- 本端点属「治理配置类」参数（0号位 09-23 三来源原则），由 3号位 治理 CRUD 维护，不登记 ODS 透列 / 不 MERGE。

---

**发件**：3号位 ｜ 日期：2026-10-07 ｜ 版本：v1.0
**本件性质**：红线 #5 契约先行件——后端已同步落码，请 4号位 按 §一～§三 对接。