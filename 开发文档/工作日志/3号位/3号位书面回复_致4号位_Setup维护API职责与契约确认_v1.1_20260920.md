# 3号位 书面回复（v1.1）— 致 4号位（Setup 维护 API 职责与契约确认 · 口径终定版）

**发送**：3号位（Governance 后端 / Setup 规则治理）
**接收**：4号位（前端）
**抄送**：0号位（治理留档）
**日期**：2026-09-20
**触发**：[4号位-2026-09-20-Setup契约源补发-给3号位.md]；[4号位-2026-09-17-verify-integration-[H]Setup段脚本骨架-给3号位(1).md]
**基底**：v1.0（2026-09-18）——本函升级版，原件全文见 §八 补发。

---

## 〇、一句话

**口径终定：保留 Id 口径 `SetupTransitionRule`，放弃 Code 口径重建。**

依据：2号位 2026-09-20 回执《APS_V1_Setup换型_2号位回执_致1号位与3号位_v1.0》证实 Setup 主链两级**已按 Id 口径全部落地、测试 39 条全绿**（仓储 A1 + 第⑦块投影 A2 + 1号位 `SetupOptimizer` 接线就绪）。若改 Code 需推翻 2号位/1号位 已全绿工作、跨号位重做，成本远高于 4号位 前端单文件适配——**前端 DTO 改传 Id，Code 由响应回带**（§三）。

---

## 一、总纲（承接 v1.0 §一，落地态更新）

v1.0 时 Setup 后端为「表/实体已落地、端点/权限码未落地」中间态；2026-09-20 后**整条主链已全落地**：

| 已落地（3号位 + 2号位） | 未落地（3号位 排期 09-25） |
|---|---|
| `SetupTransitionRule` 实体 + DDL v5.1.8（建表 + 唯一索引 + EXACT/DEFAULT CHECK） | 13 端点（#1-13，前缀 `/api/governance`） |
| `ISetupTransitionRuleRepository` Dapper 实现（**2号位** A1，CRUD + DI 注册） | `aps.setup.view/edit/publish` 三码 + 角色绑定矩阵 |
| 第⑦块投影 `SolverStrategySnapshot.SetupTransitionRules`（**2号位** A2，`SetupTransitionRuleProjector.Project/CropToDomain`） | dev seed（≥1 DRAFT `RuleSetVersion` + ≥1 ACTIVE Run） |
| 1号位 `SetupOptimizer.BuildRuleLookups` 接线就绪 | `diff` 响应补 `setupRuleChanges` 维度 |
| 冲突校验器 + 单测（39 条全绿） | 主数据 Code 回带读取口（§七） |

---

## 二、口径终定记录（本次核心变更，取代 v1.0 一切 Code 口径表述）

| 项 | v1.0 口径 | **v1.1 终定** |
|---|---|---|
| 表/实体 | 拟废弃 `SetupTransitionRule` 重建 `SetupRule`（Code） | **保留 `SetupTransitionRule`**，全链 Id 口径不动 |
| 生产部门/设备/物料维度 | `DepartmentCode/ResourceCode/FromMaterialCode/ToMaterialCode` | **`ProductionDepartmentId/ResourceId/FromMaterialId/ToMaterialId`（内部主键）** |
| 大工艺/工序维度 | `StageCode/OperationCode` | **保持 Code**（与 Id 混口径现状一致，`StageCode/OperationCode` 本为 NVARCHAR） |
| 4号位 前端 | DTO 全 Code | **DTO 改传 Id**（§三 双返回机制消解前端映射负担） |

> 独特性/冲突校验语义不变：EXACT 7 元组（Version + Dept + Stage + Op + Resource + From + To）、DEFAULT 5 元组，均以新 Id 口径判重。

---

## 三、#9 materialId 语义（4号位 待答项，本次定案）

- **`materialId` / `toMaterialId` = `Material.Id`**（即用户所称 masterId，内部主键），**不是 Code**。
- 权威映射：`APS_Production.dbo.Material.Id`（masterId）↔ `MaterialCode`（code），**表 `Id` 即 masterId、`MaterialCode` 即 code**。
- **响应双返回**（列表/详情/新建回执一律）：

| 提交字段（请求体，Id） | 响应回带字段（Code） | 权威源 |
|---|---|---|
| `productionDepartmentId` | `departmentCode` | 生产部门主数据 |
| `resourceId` | `resourceCode` | 资源主数据 |
| `fromMaterialId` / `toMaterialId` | `fromMaterialCode` / `toMaterialCode` | `Material.Id` ↔ `MaterialCode` |

- **前端约定**：界面展示/搜索用 Code，提交写用 Id；Code 由后端响应回带，前端**零映射负担**，无需自维护 masterId↔code 表。

---

## 四、错误码契约确认（按 4号位 契约，逐条确认）

| 码 | 语义 | 触发 |
|---|---|---|
| 400 | 参数/状态错误 | 非 DRAFT 规则写操作、publish 空 changeReason、路径 Id 与请求体 Id 不一致 |
| 422 | 数据红线 | 唯一键冲突（EXACT/DEFAULT）、SetupMinutes 越界、From==To 等业务规则违反 |
| 403 | 权限不足 | 缺 `aps.setup.*` 对应码 |
| 404 | 资源不存在 | Id 不存在 |

统一 `ApiResponse<T>` 信封、camelCase wire 格式、真实 HTTP code（沿用 Governance ApiResponse 统一契约）。

---

## 五、Q4 路径终定：按冻结 §11.1

v1.0 曾倾向方案 b（修订契约为现有 `rule-set/...` 路径规避别名双轨）。**现因 2号位 主链已按 Id 落地且 §11.1 为冻结文档，终定按冻结 §11.1 路径开发**：

- 类 A（#1-10 独立端点）：`setup-rules/exact`、`setup-rules/default`、`setup-rules/uncovered-stats`、`operation-resource-eligibility` 等 → 按 §11.1 新增，无路径冲突。
- 类 B（#11-13 复用端点）：`rule-set-versions` / `rule-set-versions/{id}/diff`（补 `setupRuleChanges`）/ `rule-set-versions/{id}/publish` → 按冻结 §11.1 开发，不修订冻结契约。

不再走「修订契约」路线——避免推翻冻结文档 + 2号位 主链已冻结在 Id 口径的双重风险。

---

## 六、其余确认（承接 v1.0 §二）

| # | 答复 |
|---|---|
| Q1（认属） | **是**（0号位 2026-09-17 已裁「Setup 规则治理归 3号位」，维持） |
| Q2（排期） | **09-25 合入 dev**（维持） |
| Q5（权限码） | 3 码 `aps.setup.view/edit/publish`；角色绑定：`viewer.management`/`planner` 只 view，`admin.aps`/`admin.system` 三码全；随端点同批落地 |
| Q6（dev seed） | **是**：≥1 DRAFT `RuleSetVersion` + ≥1 ACTIVE Run，沿用 G4 最小列集 seed 模式 |
| Q7（diff 补 `setupRuleChanges`） | **是**：`{ added, modified, removed }` 三维 |

---

## 七、补码承接（实现侧说明，3号位 内部）

响应回带 Code 需 masterId→Code 映射读取。现状 **Engine 仓储层无独立 Material 主数据读仓储**（`Material` 仅 Sync 服务内临时查）。3号位 在 Setup 维护 API 的 DTO 响应层做 Code 回带；读取源（复用现有主数据查询 / 新增读口）在 #2 动工时确定——若需 2号位 供读口，将另派正式协助项（不越位）。

---

## 八、v1.0 原件补发

见 `3号位书面回复_致4号位_Setup维护API职责与契约确认_v1.0_20260918.md`（同目录，未改动）。v1.1 相对 v1.0 的**唯一实质变更**：§二 口径终定（Code 重建 → 保留 Id）+ §三 #9 定案 + §五 路径终定（方案 b → 冻结 §11.1）；其余认责/排期/权限码/seed/diff 均承接 v1.0 不变。

---

**发送人**：3号位
**日期**：2026-09-20
**待办**：① 4号位 前端 DTO 改 Id 回执确认 ② #2 端点对齐动工（09-25 前）③ 主数据 Code 回带读取口确定
