# APS V1 Setup 规则承载重构——4号位 F-1 零改动确认件

**文档性质**：跨号位确认件（4号位 收）——Setup 规则承载重构（独立物理表 → `RuleSetVersion.ContentSnapshotJson.SetupTransitionRules` 子块）对前端契约的影响确认。
**发送方**：3号位 ｜ **日期**：2026-09-23
**依据**：重构方案 `APS_V1_SetupTransitionRule重构方案_复用RuleSetVersion快照承载_3号位_v1.0_20260922.md`（S-1~S-9 已全绿）；契约登记件 `APS_V1_Setup规则承载契约登记件_3号位_v1.0_20260923.md`；契约 §11.1（Setup 维护 13 端点）
**抄送**：0号位（备查）

---

## 一、结论

**4号位 零代码改动（F-1 回执确认）。** 13 个 Setup 契约端点 + 3 个 lookups 数据源端点的**接口签名、DTO、权限码全部不变**（红线 #5：接口即契约）——本次重构仅改后端底层承载（物理表 → 快照子块），前端无感知。SetupSource 查询通道变更归 5号位 R-1，非 4号位 工作。

## 二、契约不变清单（路由前缀 `/api/governance`）

### 2.1 Setup 契约 13 端点（DTO 传 Id，2026-09-20 终定口径不变）

| # | 方法 | 路径 | 权限码 |
|---|---|---|---|
| 1 | GET | `setup-rules/exact` | aps.setup.view |
| 2 | POST | `setup-rules/exact` | aps.setup.edit |
| 3 | PUT | `setup-rules/exact/{id:long}` | aps.setup.edit |
| 4 | DELETE | `setup-rules/exact/{id:long}` | aps.setup.edit |
| 5 | GET | `setup-rules/default` | aps.setup.view |
| 6 | POST | `setup-rules/default` | aps.setup.edit |
| 7 | PUT | `setup-rules/default/{id:long}` | aps.setup.edit |
| 8 | DELETE | `setup-rules/default/{id:long}` | aps.setup.edit |
| 9 | GET | `operation-resource-eligibility` | aps.setup.view |
| 11 | GET | `rule-set-versions` | aps.setup.view |
| 12 | GET | `rule-set-versions/{id:long}/diff` | aps.setup.view |
| 13 | POST | `rule-set-versions/{id:long}/publish` | aps.setup.publish |

### 2.2 主数据 Code→Id 读端点（P0 主数据缺口方案 a，2026-09-20 裁决）

| 方法 | 路径 | 权限码 | 语义 |
|---|---|---|---|
| GET | `lookups/departments` | aps.setup.view | masterId=Id，code=DeptCode；search 模糊 DeptCode |
| GET | `lookups/resources` | aps.setup.view | masterId=Id，code=ResourceCode；search 模糊 ResourceCode/ResourceName |
| GET | `lookups/materials` | aps.setup.view | masterId=Material.Id，code=MaterialCode；activeOnly=true 仅活动 |

**DTO 不变**：`SetupRuleDto` / `SetupRuleExactInput` / `SetupRuleDefaultInput` / `SetupDiffDto` / `OperationResourceEligibilityDto` / `DepartmentLookupItem` / `ResourceLookupItem` / `MaterialLookupItem`。

## 三、前端语义提示（重构后需知，无需改动）

1. **规则 Id = 治理合成 Id**：`BuildRuleId(ruleSetVersionId, seq) = ruleSetVersionId * 10_000_000 + seq`（前端 DTO 传 Id，回带 Code）；跨版本高位隔离，全局唯一。前端**无需感知合成规则**，继续以 Id 为主键交互。
2. **Setup 规则「版本」= RuleSetVersion 版本态**：DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED 六态——不再有独立 Setup 表版本，规则随 `RuleSetVersionId` 冻结。
3. **fail-open 列表语义**：子块缺省/为空/损坏 → 规则列表为空（无规则 = DEFAULT 兜底 0 分钟）。**前端列表为空是合法态**，不视为错误，不弹异常提示。
4. **错误映射不变**：422 数据红线 / 404 不存在 / 400 状态红线与参数 / 403 权限不足。

## 四、SetupSource 归 5号位（R-1，非 4号位）

`GanttDataDto.cs:176` SetupSource 4 值：填充源 = 1号位 Solver 计算（`SetupOutcomeToSource`），查询通道变更属 5号位 查询契约（R-1 通知）——4号位 前端仅消费显示，不涉承载改动。

## 五、回执栏（4号位 填写）

| 项 | 确认 |
|---|---|
| F-1 13 端点 + lookups 契约不变（接口/DTO/权限码，红线 #5） | ☐ 确认 |
| 零代码改动无工作项 | ☐ 确认 |
| 备注 |  |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**待办**：4号位 F-1 回执
