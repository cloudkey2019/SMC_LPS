# APS V1 4号位页面与业务操作开发实施包 v1.5
## Setup 换型规则专项冻结对齐补充版

**版本**：v1.5
**日期**：2026-09-16（北京时间）
**基于版本**：《APS V1 4号位页面与业务操作开发实施包 v1.4（20260915 白天业务场景 / Candidate 人工调整冻结对齐版）》
**专项依据**：《APS V1 Setup 换型规则与有限产能优化——冻结文档修改指导 v1.2（20260916 当前工序产品转换 / 生产日有界优化 / 最终收口版）》

**上位依据**：
- 《APS V1 最终全部流程与业务基线 v1.6（20260915 白天业务场景 / Candidate 人工调整冻结对齐版）》
- 《APS Pegging 供需承接与分层计算业务说明 v1.5（同上）》
- 《APS 有限产能排产与滚动 90 天计划业务说明 v1.5（同上）》
- 《APS 集成接口设计 v1.32（同上）》
- 《APS 核心排产全流程走查 V3.22（同上）》
- 《APS 数据架构与防腐层设计方案 v1.43（同上）》
- 《APS 数据库字段说明文档 v5.1.7（同上）》
- 《APS 数据库表结构设计 DDL v5.1.7（同上）》
- 《APS V1 Domain 定义、分域计算与运行边界业务裁决 v1.0（20260901）》
- 《APS V1 3 号位规则、参数、认证权限与运行生命周期开发实施包 v1.5（同白天业务场景 / Candidate 运行治理冻结对齐版）》（责任方 3 号位 实施包）

**适用对象**：4 号位及其开发 AI
**文档性质**：在 v1.4（20260915 白天业务场景）基础上的 Setup 换型规则专项对齐版，**完整继承 v1.4 全部职责、接口与权限**，仅追加 Setup 模块（5 个新维护页 + 13 个依赖 3 号位 API + 共同合法设备推荐算法 + 0 分钟兜底统计）。

---

# 0. 本版定位

本版只补充 4 号位 在 Setup v1.2 收口版（2026-09-16 冻结）下的页面与接口职责。

继续保持 v1.4 已冻结的前端边界：

```text
4 号位
├─ → 3 号位：认证、授权、规则、参数、策略、ScheduleRun、Candidate、Domain 治理、**Setup 规则治理**（新增）
└─ → 5 号位：计划结果、订单、甘特、供给追溯、PI Position、Manual ETA、Demand Protection 展示及业务操作中转
```

**4 号位 在 Setup 模块的边界（§十九.19.2 收口版）**：
- ✅ **做**：Setup 维护页面（产品转换 / 默认规则 / 0 分钟兜底 / 版本 Diff / 红线页）
- ✅ **做**：共同合法设备推荐 UI（后端走 3 号位 `/api/governance/operation-resource-eligibility`）
- ❌ **不做**：Pegging 计算、Solver 序列优化、SetupAttribute 维护、跨产品枚举、SetupAttribute 维度推导
- ❌ **不做**：直接调用 2/1 号位（保持 v1.4 边界）

**与 v1.4 边界一致**：
- 不直接调用 2 号位
- 不直接调用 1 号位
- 不直接维护 APS 业务数据库
- 不在前端重做业务判断

---

# 1. Setup 模块在 4 号位 的定位

## 1.1 Setup 业务模型重大变化（v1.1 → v1.2 收口版）

| 维度 | v1.1（已被废止）| v1.2 收口版（2026-09-16）|
|---|---|---|
| 工序维度 | **前小工序 + 后小工序** | **当前工序**（唯一工序维度）|
| Setup 来源 | MES `RoutingOperation.SetupTime` | 业务规则（SetupRule EXACT/DEFAULT）|
| 设备资格 | 静态 ResourceGroup | 动态 OperationResourceEligibility 真相 |
| 无规则兜底 | 取 RoutingOperation.SetupTime | 0 分钟（§9 第三优先）|
| 同产品 | 取 RoutingOperation.SetupTime | 0 分钟（§10）|
| SetupAttribute | 存在 | **删除**（§1.1 收口版明确废止）|
| 优先级 | 单层 | 3 层：EXACT > DEFAULT > 0 分钟 |

## 1.2 4 号位 在 Setup 模块的职责边界（§十九.19.2 收口版）

> **明确产品转换维护**（§19.2 第一段）：
> - 用户选择：生产部门 / 大工艺 / 当前小工序 / 前产品 / 后产品
> - 系统基于 OperationResourceEligibility 显示**当前工序下前后产品的共同合法设备**
> - 用户：选择一台或多台设备 + 输入 Setup 分钟 + 保存到当前 DRAFT RuleSetVersion
>
> **默认规则维护**（§19.2 第二段）：
> - 用户选择：生产部门 / 大工艺 / 当前小工序 / 一台或多台设备 / 默认 Setup 分钟
> - 同时提供：冲突提示 / 缺失规则查询 / 0 分钟兜底查询 / 规则版本 / Diff / 发布状态

## 1.3 4 号位 不做的（Setup 模块）

- ❌ **不维护 SetupAttribute**（v1.2 收口版 §1.1 明确废止，§二十一 必删字段）
- ❌ **不直接调用 2/1 号位 引擎**（保持 v1.4 §3.2 接口边界；走 3 号位 治理 API）
- ❌ **不维护 SetupRule 实体**（3 号位 负责 §十九.19.1）
- ❌ **不做共同合法设备算法计算**（调用 3 号位 端点，前端只展示结果）
- ❌ **不枚举 0 分钟兜底的全量**（调用 3 号位 聚合统计端点）

---

# 2. Setup 维护页面范围（新增 5 个页面）

| # | 页面 | 路由 | 依赖 3 号位 API |
|---|---|---|---|
| 2.1 | 明确产品转换规则维护 | `/aps/setup/exact` | 8 + 1 端点 |
| 2.2 | 默认 Setup 规则维护 | `/aps/setup/default` | 8 + 1 端点 |
| 2.3 | 未维护 Setup 产品转换查询（0 分钟兜底）| `/aps/setup/uncovered` | 1 端点 |
| 2.4 | Setup 规则版本 Diff | `/aps/setup/diff` | 复用 RuleSetVersion Diff + Setup 维度扩展 |
| 2.5 | Setup 维护红线（贯穿所有页面）| 无独立路由 + 各页面顶部 Alert | — |

---

# 3. 页面 2.1：明确产品转换规则维护

## 3.1 页面职责

用户在选定的 RuleSetVersion（DRAFT 状态）下，维护**产品 A → 产品 B** 在指定工序 + 设备上的换型分钟数。

## 3.2 输入字段（§9 推荐最小字段）

| 字段 | 必填 | 说明 |
|---|---|---|
| `DepartmentCode` | ✅ | 生产部门（联动下拉）|
| `StageCode` | ✅ | 大工艺（联动下拉）|
| `OperationCode` | ✅ | 当前小工序（联动下拉）|
| `FromMaterialCode` | ✅ | 前产品（联动下拉）|
| `ToMaterialCode` | ✅ | 后产品（联动下拉）|
| `ResourceCode` | ✅ | 设备（**多选**，共同合法设备推荐算法筛选）|
| `SetupMinutes` | ✅ | 换型分钟数（> 0）|
| `RuleSetVersionId` | 自动 | 当前 DRAFT 版本 |
| `Status` | 自动 | DRAFT |
| `Remark` | ❌ | 备注 |

## 3.3 共同合法设备推荐算法（§7.2）

> **算法定义**：当前工序下前后产品的**共同合法设备集合** = 当前 Operation 合法资源 ∩ 同部门同 Stage 任一 Operation 合法资源

**前端实现**：
- 当用户选定 `OperationCode` + `FromMaterialCode` + `ToMaterialCode` 后
- 调用 `GET /api/governance/operation-resource-eligibility?operationCode={op}&materialId={fromId}&materialId={toId}`
- 响应：`ResourceCode[]` 共同合法设备列表
- UI：设备下拉只展示共同合法设备；非共同设备灰显 + Tooltip「不在前后产品共同合法设备集合中」

## 3.4 唯一性约束（§10）

- 提交前前端预校验：同 RuleSetVersionId + 7 元组（Department/Stage/Operation/Resource/FromMaterial/ToMaterial）不可重复
- 重复提交 → 后端 422 + 业务消息"EXACT 规则已存在"

## 3.5 保存行为

- POST → 当前 DRAFT RuleSetVersion 新增一条 EXACT
- 保存后自动 reload 列表
- 不允许直接改 PUBLISHED 数据（v1.4 §十八 红线）

## 3.6 红线

- ❌ 同产品连续（`FromMaterial == ToMaterial`）→ 0 分钟，不存规则
- ❌ 跨工序（`FromOperationCode/ToOperationCode` 维度）→ 不允许
- ❌ SetupMinutes = 0（兜底是 0 分钟自动命中，不需规则）
- ❌ 编辑 PUBLISHED 版本数据

---

# 4. 页面 2.2：默认 Setup 规则维护

## 4.1 页面职责

用户在选定的 RuleSetVersion（DRAFT 状态）下，维护**无前后产品指定**的默认换型分钟数。

## 4.2 与 2.1 的区别

| 维度 | 2.1 EXACT | 2.2 DEFAULT |
|---|---|---|
| `FromMaterial/ToMaterial` | 必填 | 必空 |
| 唯一键 | 7 元组 | 5 元组（Department/Stage/Operation/Resource）|
| 命中优先级 | 高（§9 第一优先）| 中（§9 第二优先）|
| 共同合法设备推荐 | 必走 | 不需要（无前后产品）|

## 4.3 输入字段

| 字段 | 必填 | 说明 |
|---|---|---|
| `DepartmentCode` | ✅ | 生产部门 |
| `StageCode` | ✅ | 大工艺 |
| `OperationCode` | ✅ | 当前小工序 |
| `ResourceCode` | ✅ | 设备（多选）|
| `SetupMinutes` | ✅ | 默认换型分钟数（> 0）|
| `RuleSetVersionId` | 自动 | 当前 DRAFT 版本 |
| `Status` | 自动 | DRAFT |
| `Remark` | ❌ | 备注 |

## 4.4 红线

- ❌ 编辑 PUBLISHED 版本数据
- ❌ `FromMaterial/ToMaterial` 字段非空（与 EXACT 互斥）
- ❌ 与 2.1 EXACT 同 5 元组重复 → 后端 422 冲突

---

# 5. 页面 2.3：未维护 Setup 产品转换查询（0 分钟兜底统计）

## 5.1 页面职责

统计 Run 中**所有未命中 EXACT / DEFAULT 规则**的产品转换对，用于：
- 排查 0 分钟兜底是否合理
- 指导 EXACT / DEFAULT 规则补全
- 评估 Setup 规则覆盖率

## 5.2 输入字段（筛选条件）

| 字段 | 必填 | 说明 |
|---|---|---|
| `RunId` | ✅ | 选定的 Run |
| `DepartmentCode` | ❌ | 按部门筛选 |
| `StageCode` | ❌ | 按大工艺筛选 |
| `ResourceCode` | ❌ | 按设备筛选 |

## 5.3 输出列表

| 字段 | 说明 |
|---|---|
| `DepartmentCode` | 生产部门 |
| `StageCode` | 大工艺 |
| `OperationCode` | 当前工序 |
| `ResourceCode` | 设备 |
| `FromMaterialCode` | 前产品 |
| `ToMaterialCode` | 后产品 |
| `Count` | 该转换对出现次数 |
| `LastOccurrence` | 最近一次出现时间 |

## 5.4 出口操作

- 点击"去补 EXACT" → 跳转页面 2.1，预填 4 字段（Department/Stage/Operation/Resource）
- 点击"去补 DEFAULT" → 跳转页面 2.2，预填 3 字段（Department/Stage/Operation/Resource）
- 导出 CSV（v1.4 §十三 审批边界外的常规导出）

## 5.5 红线

- ❌ 不在前端聚合（性能问题，调用后端聚合端点）
- ❌ 不展示 Run 外的统计（避免与 RuleSetVersion 解耦）

---

# 6. 页面 2.4：Setup 规则版本 Diff / 发布状态

## 6.1 页面职责

复用 RuleSetVersion Diff 机制（v1.4 §十八），扩展 Setup 维度：
- 对比两个 RuleSetVersion 的 SetupRule 差异
- 展示 EXACT/DEFAULT 规则的 added / modified / removed

## 6.2 列表字段

| 字段 | 说明 |
|---|---|
| `RuleSetVersionId` | 当前版本 |
| `OtherVersionId` | 对比版本 |
| `AddedCount` | 新增规则数（EXACT + DEFAULT）|
| `ModifiedCount` | 修改规则数 |
| `RemovedCount` | 删除规则数 |
| `PublishStatus` | 当前版本发布状态（DRAFT / PUBLISHED / DEPRECATED）|

## 6.3 Diff 操作

- 选定两个 RuleSetVersion → 调 `GET /api/governance/rule-set-versions/{id}/diff?otherVersionId=...`
- 响应体新增 `setupRuleChanges` 字段：
  ```json
  {
    "added": [{ "ruleType": "EXACT", "operationCode": "OP20", "fromMaterial": "A", "toMaterial": "B", "setupMinutes": 45 }],
    "modified": [{ "ruleType": "EXACT", "id": 123, "before": {...}, "after": {...} }],
    "removed": [{ "ruleType": "EXACT", "id": 456, ... }]
  }
  ```
- 支持按 Department/Stage/Operation 筛选

## 6.4 红线

- ❌ 不展示非 Setup 维度的 Diff（ParameterSet/StrategyProfile 仍走原 v1.4 §十八 规则参数维护页）
- ❌ 不允许跨 RuleSet 直接编辑（必须发布 DRAFT 后再编辑）

---

# 7. 页面 2.5：Setup 维护红线（贯穿所有页面）

## 7.1 数据结构红线

| 红线 | 触发条件 | UI 拦截 |
|---|---|---|
| 不得出现 `FromOperationCode/ToOperationCode` | 表单字段不存在 | 字段不渲染 |
| 不得出现 `SetupAttribute` | 已删除 | 搜索/筛选不渲染 |
| 同产品连续 = 0 分钟 | `FromMaterial == ToMaterial` | 提交按钮 disabled + Tooltip |
| SetupMinutes 必须 > 0（EXACT/DEFAULT 规则）| = 0 | 提交按钮 disabled + Tooltip |
| EXACT 必须填 FromMaterial/ToMaterial | 任意空 | 提交按钮 disabled |
| DEFAULT 必须 FromMaterial/ToMaterial 都空 | 任意非空 | 提交按钮 disabled |

## 7.2 业务红线

| 红线 | 触发条件 | UI 拦截 |
|---|---|---|
| 编辑 PUBLISHED 数据 | RuleSetVersion.Status = PUBLISHED | 编辑/新增按钮 disabled + Tooltip「只能编辑 DRAFT 版本」|
| 共同合法设备外不允许保存 | 选中非共同设备 | 提交按钮 disabled |
| 同 5 元组 EXACT + DEFAULT 重复 | 提交时后端 422 | ElMessage.error 后端原文 |
| 同 7 元组 EXACT 重复 | 提交时后端 422 | ElMessage.error 后端原文 |

## 7.3 不做的（前端硬约束）

- ❌ **不做** Pegging 计算（v1.4 §4.1 红线）
- ❌ **不做** Solver 序列优化（v1.4 §4.1 红线）
- ❌ **不做** 跨产品全量枚举（§19.4 1 号位 红线，4 号位 不越界）
- ❌ **不做** SetupAttribute 维护（§1.1 已废止）
- ❌ **不做** Setup 算法参数推导（§十九.19.1 归属 3 号位）

---

# 8. Setup 字段 / DDL 修订特别注意（§二十一）

## 8.1 必删 / 退役字段

| 字段 | 所属表 | 处理 |
|---|---|---|
| `RoutingOperation.SetupTime` | `RoutingOperation` | **删除**（v1.2 收口版 §1.1 明确废止 MES 提供 SetupTime）|
| 历史物理列若暂不 DROP | — | 加 `[Obsolete("Setup v1.2 收口版废止，新代码禁止使用")]` |
| `SetupAttribute.*`（如存在）| `SetupAttribute` | **整表退役**（§1.1）|

## 8.2 必加字段（Setup 规则实体）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `Id` | bigint | ✅ | PK |
| `RuleSetVersionId` | bigint | ✅ | FK → RuleSetVersion |
| `RuleType` | enum | ✅ | EXACT / DEFAULT |
| `DepartmentCode` | nvarchar(64) | ✅ | 生产部门 |
| `StageCode` | nvarchar(64) | ✅ | 大工艺 |
| `OperationCode` | nvarchar(64) | ✅ | **唯一工序维度** |
| `ResourceCode` | nvarchar(64) | ✅ | 设备 |
| `FromMaterialCode` | nvarchar(64) | EXACT 必填 / DEFAULT 必空 | 前产品 |
| `ToMaterialCode` | nvarchar(64) | EXACT 必填 / DEFAULT 必空 | 后产品 |
| `SetupMinutes` | int | ✅ | 换型分钟数（> 0）|
| `Status` | enum | ✅ | DRAFT / ACTIVE / DEPRECATED |
| `Remark` | nvarchar(500) | ❌ | 备注 |
| `CreatedAt` | datetime2 | ✅ | — |
| `CreatedBy` | nvarchar(64) | ✅ | — |
| `UpdatedAt` | datetime2 | ❌ | — |
| `UpdatedBy` | nvarchar(64) | ❌ | — |

## 8.3 唯一性约束（§10）

```sql
-- EXACT 唯一键
ALTER TABLE SetupRule ADD CONSTRAINT UK_SetupRule_EXACT
UNIQUE (RuleSetVersionId, DepartmentCode, StageCode, OperationCode, ResourceCode, FromMaterialCode, ToMaterialCode)
WHERE RuleType = 'EXACT';

-- DEFAULT 唯一键
ALTER TABLE SetupRule ADD CONSTRAINT UK_SetupRule_DEFAULT
UNIQUE (RuleSetVersionId, DepartmentCode, StageCode, OperationCode, ResourceCode)
WHERE RuleType = 'DEFAULT';
```

**业务约束**（CHECK）：
- `RuleType = 'EXACT'` → `FromMaterialCode IS NOT NULL AND ToMaterialCode IS NOT NULL`
- `RuleType = 'DEFAULT'` → `FromMaterialCode IS NULL AND ToMaterialCode IS NULL`
- `FromMaterialCode != ToMaterialCode`（同产品连续 = 0 分钟，不存规则）
- `SetupMinutes > 0`

## 8.4 索引建议

```sql
CREATE INDEX IX_SetupRule_EXACT_Lookup
ON SetupRule (RuleSetVersionId, DepartmentCode, StageCode, OperationCode, ResourceCode, FromMaterialCode, ToMaterialCode)
WHERE RuleType = 'EXACT' AND Status = 'ACTIVE';

CREATE INDEX IX_SetupRule_DEFAULT_Lookup
ON SetupRule (RuleSetVersionId, DepartmentCode, StageCode, OperationCode, ResourceCode)
WHERE RuleType = 'DEFAULT' AND Status = 'ACTIVE';
```

---

# 9. Setup 命中优先级（§5，前端只展示不计算）

| 优先 | 命中条件 | SetupMinutes |
|---|---|---|
| 1 | EXACT 规则：7 元组完全匹配 | 规则值 |
| 2 | DEFAULT 规则：5 元组匹配 | 规则值 |
| 3 | 无任何规则 | **0 分钟**（兜底）|

**前端展示**：
- 甘特图 / 资源计划页（v1.4 §九）展示每个 Task 的 Setup 来源
- Setup 来源枚举：`EXACT` / `DEFAULT` / `NONE(0分钟)`
- NONE 时显示红色 Tag + Tooltip「无 Setup 规则兜底，请补规则」

**前端不计算命中**，只读后端 `SetupSource` 字段。

---

# 10. 同产品连续生产（§六）

> A → A = 0 分钟（不存规则）

**前端处理**：
- 甘特图相邻 Task 同产品 → Setup 展示 0 分钟，来源 `SAME_PRODUCT`
- 2.1 EXACT 维护页禁止提交 `FromMaterial == ToMaterial`
- 2.5 红线页明文列出此规则

---

# 11. 与 3 号位 / 5 号位 / 1 号位 / 2 号位 接口边界

## 11.1 4 → 3：Setup 规则治理 API（依赖 3 号位）

4 号位 需要的 3 号位 Setup API（**Setup 维护页面开工的前置阻塞**）：

| # | API | 用途 | Owner | 状态 |
|---|---|---|---|---|
| 1 | `GET /api/governance/setup-rules/exact?ruleSetVersionId=...` | EXACT 规则列表 | 3 号位 | 🆕 新增 |
| 2 | `POST /api/governance/setup-rules/exact` | 新增 EXACT | 3 号位 | 🆕 新增 |
| 3 | `PUT /api/governance/setup-rules/exact/{id}` | 修改 EXACT | 3 号位 | 🆕 新增 |
| 4 | `DELETE /api/governance/setup-rules/exact/{id}` | 删除 EXACT | 3 号位 | 🆕 新增 |
| 5 | `GET /api/governance/setup-rules/default?ruleSetVersionId=...` | DEFAULT 规则列表 | 3 号位 | 🆕 新增 |
| 6 | `POST /api/governance/setup-rules/default` | 新增 DEFAULT | 3 号位 | 🆕 新增 |
| 7 | `PUT /api/governance/setup-rules/default/{id}` | 修改 DEFAULT | 3 号位 | 🆕 新增 |
| 8 | `DELETE /api/governance/setup-rules/default/{id}` | 删除 DEFAULT | 3 号位 | 🆕 新增 |
| 9 | `GET /api/governance/operation-resource-eligibility?operationCode=...&materialId=...` | 共同合法设备推荐 | 3 号位 | 🆕 新增 |
| 10 | `GET /api/governance/setup-rules/uncovered-stats?runId=...&...filters` | 0 分钟兜底统计 | 3 号位 | 🆕 新增 |
| 11 | `GET /api/governance/rule-set-versions` | RuleSetVersion 列表 | 3 号位 | ✅ 已有 |
| 12 | `GET /api/governance/rule-set-versions/{id}/diff?otherVersionId=...` | 版本 Diff | 3 号位 | ⚠️ 需补 Setup 维度 |
| 13 | `POST /api/governance/rule-set-versions/{id}/publish` | 发布 | 3 号位 | ✅ 已有 |

**实际新增 = 9 个端点（#1-#9）+ 1 个 Diff 扩展（#12）= 10 处改动**。

**4 号位 无 Pegging 和 Solver 计算职责**（§19.2 末段）；Setup 维护 API 不涉及求解端点。

## 11.2 4 ↔ 5：5 号位 无新增 Setup 核心职责（§19.5）

| 项 | 5 号位 状态 |
|---|---|
| 建 Setup 事实 | 不需要 |
| 推导 Setup 属性 | 不需要 |
| 维护 Setup 规则 | 不需要（4 号位 调 3 号位 API）|
| 决定 Setup 时间 | 不需要（车间录入 + 1 号位 Solver 真实占用）|

5 号位 实施包不需要 Setup 增量（§二十一"5 号位实施包原则上不增加 Setup 核心职责"）。

## 11.3 4 ↔ 1：Solver 计算归 1 号位（§19.4）

- ❌ 4 号位 不调 1 号位 Solver
- ❌ 4 号位 不做 Pegging 计算
- ❌ 4 号位 不做枚举全厂/90 天排列
- ✅ 1 号位 输出 `SetupSource` 字段（EXACT/DEFAULT/NONE(0分钟)）到 4 号位 展示

## 11.4 4 ↔ 2：Pegging / Solver 装载归 2 号位（§19.3）

- 2 号位 按本 Run Frozen Strategy Snapshot 装载 SetupRule
- 2 号位 形成内存快速查找结构
- 2 号位 传给 1 号位 DomainSolveRequest
- ❌ 4 号位 不直接调 2 号位（保持 v1.4 §3.2 边界）

---

# 12. Setup 验证场景（§二十二，前端 UI 验收）

S01-S18 中 **4 号位 涉及 4 个**：

| 场景 | 4 号位 验证点 | 期望 |
|---|---|---|
| **S01** 当前工序产品转换命中 | 2.1 EXACT 维护页 | 7 元组完全匹配 → 规则值；不匹配 → DEFAULT 兜底 |
| **S03** 同设备不同当前工序 | 2.1 设备下拉 | 不同 Operation 下推荐设备集合不同 |
| **S05** 默认规则 | 2.2 DEFAULT 维护页 | 5 元组匹配 → 规则值；不匹配 → 0 分钟 |
| **S06** 无规则 | 2.3 0 分钟兜底统计 | 统计列表正确展示未命中规则的产品转换对 |

其余 14 个场景由 1/2/3 号位 联调验证。

---

# 13. Setup 性能与交互要求

| 维度 | 要求 |
|---|---|
| 列表加载 | ≤ 1s（≤ 1000 条）|
| 共同合法设备推荐响应 | ≤ 500ms |
| 0 分钟兜底统计响应 | ≤ 2s（聚合查询）|
| Diff 响应 | ≤ 3s |
| 表单提交响应 | ≤ 1s |
| 失败时 ElMessage | 弹后端原文 message（@see v1.4 §二十三.23.3）|

---

# 14. Setup 明确不做（§二十三）

| # | 项 | 原因 |
|---|---|---|
| 1 | SetupAttribute 维护 | §1.1 收口版废止 |
| 2 | 前小工序 + 后小工序 Setup 模型 | §二十 明确废止 |
| 3 | MES `RoutingOperation.SetupTime` fallback | §1.1 收口版废止 |
| 4 | 静态 ResourceGroup | §7.1 不恢复 |
| 5 | 第二套 Solver / Setup 版本平台 | §8.1 不新增 |
| 6 | SetupAttribute 维度推导 | §十九.19.5 5 号位 不做，4 号位 更不做 |
| 7 | 跨厂/跨域 Setup 特殊处理 | §十九 边界外 |
| 8 | 客户端聚合 0 分钟兜底 | §5.3 性能问题 |
| 9 | 全量 Setup 规则导入 | 由 3 号位 治理 API 兜底，4 号位 只调不实现 |
| 10 | Setup 历史版本回滚 | 4 号位 只展示，发布由 3 号位 负责 |
| 11 | Setup 模拟器 | 4 号位 不做模拟，1 号位 Solver 真实占用 |
| 12 | 跨 Run Setup 对比 | 由 3 号位 Diff 端点兜底 |
| 13 | Setup 规则模板市场 | 4 号位 不做 |
| 14 | Setup AI 推荐 | 不在 v1.0/v1.1/v1.2 范围 |
| 15 | Setup 跨班次/跨工厂 | 4 号位 边界外 |

---

# 15. Setup 模块完成定义（Definition of Done）

## 15.1 v1.4 DoD 全部继承

- 不直接写 APS 运行库
- 只展示 FinalTask
- 4 号位 只直接调用 3 号位和 5 号位
- 不新增 4→2 HTTP 依赖
- 不存在 4→1 调用
- U01～U50 全部通过或明确列出未通过项及原因
- v1.4 §三十八 原业务 DoD + v1.1 新增 DoD 全部继续成立

## 15.2 本 addendum 新增 DoD（Setup 模块）

| # | DoD |
|---|---|
| 1 | 5 个 Setup 维护页面（2.1-2.5）前端实现 + 路由 + 权限 |
| 2 | 13 个 3 号位 API 全部接入（含 9 个新增 + 1 个 Diff 扩展）|
| 3 | 共同合法设备推荐算法（§3.3）前端实现 |
| 4 | 0 分钟兜底统计（§5）前端展示 + 导出 CSV |
| 5 | SetupRule 唯一性约束前端预校验 |
| 6 | 7 项数据结构红线（§7.1）全部 UI 拦截 |
| 7 | 4 项业务红线（§7.2）全部 UI 拦截 |
| 8 | 4 个 S01/S03/S05/S06 验证场景前端实测通过 |
| 9 | 性能 6 项（§13）全部达标 |
| 10 | 10 项明确不做（§14）前端无越界 |

## 15.3 阻塞 / 解除条件

| 状态 | 条件 |
|---|---|
| **当前阻塞** | 3 号位 13 个 Setup 维护 API 未落地（0/13）|
| **解除条件** | 3 号位 13 端点 + SetupRule 实体 + Diff 扩展全部合入 dev 分支 |
| **期望时长** | 3 号位 5-7 工作日 + 4 号位 3-5 工作日 = **1-2 周闭环** |

---

# 16. 一句话交付要求（Setup 模块）

> **4 号位 在 Setup 模块的任务是：在 v1.4 已冻结的前端边界下，仅通过 3 号位 Setup 维护 API（13 端点），把"明确产品转换 / 默认规则 / 0 分钟兜底 / 版本 Diff / 红线" 5 个页面准确呈现给有权限的业务人员，让他们只能在自己的 RuleSetVersion DRAFT 上维护 Setup 规则，绝不绕过 3 号位 治理层直接改数据库、绝不调用 2/1 号位 引擎、绝不恢复 SetupAttribute / 前小工序 + 后小工序 等 v1.2 收口版已废止的旧模型。**

---

# 附录 A：v1.4 → v1.5 增量对账

## A.1 状态更新（2026-09-16）

| 项 | v1.4 状态 | v1.5 状态 |
|---|---|---|
| Pkg-8 RBAC 管理 UI | ✅ 4/4 页闭环 + verify 36/36 | ✅ **4/4 页闭环 + verify 73/73**（2026-09-16 第二波完工：4 读回端点接入 + AssignDialog 预勾选）|
| 5 号位 A 方案（DemandProtection）| ❌ 待 5 号位 | ✅ **2026-09-16 A 方案 超承诺闭环**（完整 Application 层 + dev DB seed）|
| 3 号位 record attribute fix | ❌ POST /roles + /permissions 400 | ✅ **r13366 + 实测 200/422**（@see [4号位-2026-09-16-record修复验收-给3号位.md](../../frontNew/docs/4号位-2026-09-16-record修复验收-给3号位.md)）|
| 28 GET 鉴权修复 | ❌ 0/28（裸奔）| ✅ **28/28 全 403** |
| ConfigureApiBehaviorOptions 422 + ApiResponse 信封 | ❌ 400 | ✅ **422 + 业务消息** |
| **Setup v1.2 收口版落地** | ❌ 未涉及 | ✅ **本 v1.5 addendum 闭环**（5 个维护页 + 13 端点依赖）|

## A.2 Setup v1.2 新增（2026-09-16）

- 明确产品转换维护页（§3，CRUD 7 元组 + 共同合法设备推荐）
- 默认规则维护页（§4，CRUD 5 元组）
- 未维护 Setup 产品转换查询（§5，0 分钟兜底统计）
- Setup 规则版本 Diff（§6，复用 RuleSetVersion + 补 setupRuleChanges 维度）
- 共同合法设备推荐算法（§3.3）
- 红线：唯一工序维度 + 不出现 FromOperationCode / ToOperationCode + 真实资源占用

## A.3 主进度文档链接

- [Pkg-8-RBAC 完成报告.md](../../frontNew/docs/Pkg-8-RBAC完成报告.md)
- [v1.2-完成报告.md](../../frontNew/docs/v1.2-完成报告.md)
- [5号位-剩余修改清单-2026-09-12.md](../../frontNew/docs/5号位-剩余修改清单-2026-09-12.md)
- [4号位-2026-09-16-Pkg-8第二波完工-给3号位.md](../../frontNew/docs/4号位-2026-09-16-Pkg-8第二波完工-给3号位.md)
- [4号位-2026-09-16-5号位A方案验收-给5号位.md](../../frontNew/docs/4号位-2026-09-16-5号位A方案验收-给5号位.md)
- [3号位催办单-2026-09-17-Setup维护API-12端点.md](../../frontNew/docs/3号位催办单-2026-09-17-Setup维护API-12端点.md)

---

# 附录 B：§二十一 11 项必改清单对账（4 号位 涉及项声明）

> **目的**：避免 0/1 号位 误以为 4 号位 需要直接修改 11 份冻结文档。本附录明确：**4 号位 强涉及 = 1 项（#11 自身，本 v1.5 addendum 已闭环）；弱涉及 3 项（#1/#5/#7）只通过本 addendum 引用声明边界，不直接修改对应文档**。

## B.1 11 项对账表

| # | 必改文档 | 4 号位 涉及度 | 涉及方式 | 4 号位 是否直接修改 |
|---|---|---|---|---|
| 1 | 《最终全部流程与业务基线》| 🟡 弱 | 流程图含 Setup 维护节点（v1.5 §1.2 已声明 §十九.19.2 边界）| ❌ **不直接改**，仅 v1.5 §1.2 引用 §十九.19.2 同步 |
| 2 | 《有限产能排产与滚动 90 天计划业务说明》| ⚪ 不涉及 | 文字描述，v1.5 未引用 | ❌ 不改 |
| 3 | 《核心排产全流程走查》| ⚪ 不涉及 | 走查图，v1.5 未引用 | ❌ 不改 |
| 4 | 《集成接口设计》| ⚪ 不涉及 | 4 号位 走 3 号位 治理 API（v1.5 §11.1），不直连 2/1 号位 | ❌ 不改 |
| 5 | 《数据库字段说明文档》| 🟡 弱 | DDL 删 SetupAttribute 影响前端表单字段；**v1.5 §8 已写明"必删/必加字段"清单** | ❌ **不直接改**，仅 v1.5 §8 引用 §二十一 字段/DDL 修订特别注意 |
| 6 | 《数据库表结构设计 DDL》| ⚪ 不涉及 | 1/2 号位 字段层；4 号位 只读 DTO | ❌ 不改 |
| 7 | 《数据架构与防腐层》| 🟡 弱 | 4 号位 → 3 号位（治理 API）走防腐层（v1.5 §11.1 声明）| ❌ **不直接改**，仅 v1.5 §11.1 引用防腐层边界 |
| 8 | 《1 号位 实施包》| ⚪ 不涉及 | 1 号位 自管 | ❌ 不改 |
| 9 | 《2 号位 实施包》| ⚪ 不涉及 | 2 号位 自管 | ❌ 不改 |
| 10 | 《3 号位 实施包》| ⚪ 不涉及 | 3 号位 自管（v1.5 → v1.6 升版由 3 号位 负责）| ❌ 不改 |
| 11 | **《4 号位 实施包》** | ✅ **强** | **本 v1.5 addendum 即为 #11 的 §二十一 合规修订** | ✅ **本文件即为 4 号位 闭环交付** |

## B.2 4 号位 涉及项汇总

| 类别 | 数量 | 说明 |
|---|---|---|
| ✅ 强涉及（4 号位 自身闭环）| 1 | #11 4 号位 实施包 → v1.5 addendum 落地 |
| 🟡 弱涉及（4 号位 引用声明，不直接改）| 3 | #1 流程基线 / #5 字段 / #7 防腐层 |
| ⚪ 不涉及 | 7 | #2/#3/#4/#6/#8/#9/#10 |
| **合计** | **11** | **强涉及 1 + 弱涉及 3 + 不涉及 7 = 11 全覆盖** |

## B.3 4 号位 实施包不直接修改 #1/#5/#7 的理由

### #1 《最终全部流程与业务基线》

- 流程图 Owner = 0/1 号位，4 号位 实施包只引用"Setup 维护页在 4 号位"的边界声明（v1.5 §1.2 + §十九.19.2）
- 若 4 号位 改流程图，会破坏文档 Owner 单一原则
- **同步机制**：v1.5 addendum §1.2 + §十九.19.2 引用即是同步；下游读者按 §十九 → §1.2 → v1.5 addendum 链路即可拼出完整 4 号位 边界

### #5 《数据库字段说明文档》

- DDL/字段 Owner = 1/2 号位
- 4 号位 关注的是"哪些字段在 DTO 上有/没有"，v1.5 §8 已列：
  - **必删 / 退役字段**（如历史物理列 `RoutingOperation.SetupTime`，标记兼容废弃）
  - **必加字段**（Setup 规则实体新结构，§8.2 表）
  - **唯一性约束**（§8.3）
- **同步机制**：4 号位 实施包 v1.5 §8 引用 §二十一"字段/DDL 修订特别注意"段即足够；具体 DDL 改法由 1/2 号位 在其 DDL 文档落地

### #7 《数据架构与防腐层》

- 防腐层 Owner = 1/2/3 号位（按调用方分层）
- 4 号位 仅声明"4 号位 → 3 号位（治理 API）走防腐层"，v1.5 §11.1 第 1 段已写明
- **同步机制**：4 号位 实施包 v1.5 §11.1 引用 §十九.19.1（3 号位）+ §十九.19.3（2 号位）的防腐层边界即足够

## B.4 一句话对账声明

> **4 号位 仅修改 11 项必改清单中的 #11（自身实施包，v1.5 addendum 已闭环）；#1/#5/#7 弱涉及但 4 号位 不直接修改，引用声明见 v1.5 §1.2 / §8 / §11.1；#2/#3/#4/#6/#8/#9/#10 与 4 号位 无任何关系，0/1 号位 治理 11 项时勿误判 4 号位 漏改**。
