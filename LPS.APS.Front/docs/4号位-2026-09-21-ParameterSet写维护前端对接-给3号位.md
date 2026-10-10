# 4 号位 → 3 号位：ParameterSet 写维护前端对接缺口（Q1-Q5 确认 + 4 端点对接请求）

> **发送人**：4 号位（前端）
> **接收人**：3 号位（治理后端 / RuleSet / ParameterSet / StrategyProfile Owner）
> **抄送**：0 号位（治理留档 + 仲裁口径）
> **日期**：2026-09-21
> **性质**：**催办 + 设计同步**——请回执 5 项 Q&A，4 号位 当日承接前端封装 + UI 实施

---

## 〇、本函结论（一行）

**后端写端点已实现（4 个 POST/PUT）**，但**前端 4 号位 完全没对接**——"规则与参数维护"页面只有[校验草稿][发布][退役] 三按钮可见，**[+ 新建草稿] 不存在；参数维护 Tab 仅内联 mock 编辑无法保存**。请 3 号位 回执 Q1-Q5 五项设计确认（≈0.5 天），4 号位 当日承接前端封装 + 5 项 UI + verify（≈1.5 天）。

---

## 一、当前缺口现场（4 号位 端亲验）

| 项 | 现状 | 证据 |
|---|---|---|
| **页面顶部 [+ 新建草稿] 按钮** | ❌ 不存在 | [Rules.vue:177-187](frontNew/src/views/Aps/Rules.vue#L177-L187) header-actions 只有 `actor` + `刷新列表` 两元素 |
| **"参数维护" Tab 可编辑表单** | ⚠️ UI 仅 mock | [Rules.vue:366-380](frontNew/src/views/Aps/Rules.vue#L366-L380) 提示文案"参数可在表格内编辑（mock）"自承 |
| **store 写 actions** | ❌ 仅 validateDraft/publish/retire | [rules.ts:111-275](frontNew/src/store/modules/aps/rules.ts#L111-L275) 暴露的 actions 无 createDraft/updateDraft |
| **api rule.ts 写封装** | ❌ 仅读端点封装 | [rule.ts:287-521](frontNew/src/api/aps-v1/rule.ts#L287-L521) 暴露 listRuleSets/getPublishedVersion/getRuleSet/diffVersions/validateDraft/publish/retire **无 createDraft/updateDraft** |

**用户 09-21 现场体验**：打开"规则与参数维护"页（[screenshot](frontNew/docs/screenshots/2026-09-21-rules-page-no-new-draft.png)）→ 选已存在 DRAFT（RS-DEFAULT v13）→"可编辑"列全"是"→ 表单可输入值 → **保存按钮找不到；数据丢失**。

---

## 二、后端写端点已实现的事实（实测，非催"新建"）

按 4 号位 在 `lps/LPS.APS.Web/Controllers/GovernanceController.cs` 实测，**后端已有 4 个写端点**：

| # | 端点 | 行号 | 权限码 | 前端调用方 |
|---|---|---|---|---|
| 1 | `POST /api/governance/rule-set/version` | L108-114 | `RuleMaintain` (`aps.rule.edit`) | ❌ 未对接 |
| 2 | `POST /api/governance/parameter-set/version` | L164-170 | `RuleMaintain` (`aps.rule.edit`) | ❌ 未对接 |
| 3 | `PUT /api/governance/parameter-set/version/{versionId}` | L174-188 | `RuleMaintain` (`aps.rule.edit`) | ❌ 未对接 |
| 4 | `PUT /api/governance/rule-set/version/{versionId}` | L118-132 | `RuleMaintain` (`aps.rule.edit`) | ❌ 未对接 |
| 5 | `POST /api/governance/parameter-set/version/{versionId}/publish` | L222-228 | `RulePublish` (`aps.rule.publish`) | ❌ 未对接（前端调的是 `rule-set/version/.../publish`，未级联 ParameterSet） |
| 6 | `POST /api/governance/parameter-set/version/{versionId}/disable` | L269-275 | `RuleMaintain` (`aps.rule.edit`) | ❌ 未对接 |

加上后端已有 `RuleSetVersion`/`ParameterSetVersion` 实体（L48: 含 LockJson/SupplyJson/ProcurementJson/SolverStrategyJson/CandidateGuardrailJson 五个子 JSON 字段）。

**结论**：3 号位 已把基础设施搭好，缺口在 4 号位 端没有调用层——并非"催新建端点"，而是"催设计口径确认 + 4 号位 自承接对接"。

---

## 三、Q1-Q5 设计确认（请 3 号位 回执）

前端要写"建草稿 + 改参数 + 保存草稿"三段流程，但对接前有 5 项口径必须确认（**任一未清，前端不敢动笔**）：

### Q1：从 PUBLISHED fork 出 DRAFT 的正确姿势？

- 选项 A：**前端手工 fork**——`GET /parameter-set/version/{publishedVersionId}` → 把整个 ParameterSetVersion 复制 → 改 versionCode = "DRAFT-v13" → `POST /parameter-set/version`
- 选项 B：**3 号位 加封装端点** `POST /parameter-set/version/{publishedVersionId}/fork` 一键 fork（推荐，工时少 + 语义明确）
- 4 号位 倾向 B，但请 3 号位 拍板（**B 路径 3 号位 工作量 ≈0.5 天**）

### Q2：PUT /parameter-set/version/{versionId} 是整对象替换还是 partial PATCH？

- 文档说"内容归一化到 ContentSnapshotJson 持久化"（L116 注释）
- 后端 `RuleSetVersion` 实体含 5 个子 JSON 字段（Lock/Supply/Procurement/Solver/CandidateGuardrail）
- **若前端改了 ProcurementJson 一项，是否必须把其他 4 个 JSON 原样回传**？

### Q3：ParameterSet publish 与 RuleSet publish 是级联还是独立？

- 当前前端调 `POST /rule-set/version/{id}/publish`（rule.ts:483 调用 GovernanceController L200）
- 后端**另有** `POST /parameter-set/version/{id}/publish` (L222) 是独立端点
- **前端 publish() 应不应该同时调两个 publish**？还是一个会自动级联另一个（依 `CreateRuleSetVersionAsync` 实现）？

### Q4：LockJson/SupplyJson/ProcurementJson/SolverStrategyJson/CandidateGuardrailJson 当前值的获取路径？

- 当前 RuleSetDetailDto.parameterSet.parameters 只有 5 条（mockParametersFor L124-179: defaultPurchaseLT/demandProtectionThreshold/planningYield/crossDomainBlockBuffer/planningPurchasePlaceholderEnabled）
- **真实后端返回是否覆盖这 5 条，还是 DTO 形状需重设计**？4 号位 需调 `GET /parameter-set/version/{versionId}` 实际取一次，对齐 DTO

### Q5：GovernanceVersionStatus 6 态 vs DTO 3 态

- 后端 `GovernanceVersionStatus` 含 DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED（L9 注释）
- 前端 `RuleStatus` type 只有 DRAFT/PUBLISHED/RETIRED（rule.ts:71-80 STATUS_LABEL）
- **前端要不要兼容 SUBMITTED/APPROVED/DISABLED/ARCHIVED**？还是要 3 号位 输出时收敛到 3 态？

---

## 四、4 号位 自承接实施清单（Q1-Q5 回执后开工）

| # | 任务 | 文件 | 工时 |
|---|---|---|---|
| 1 | 加 `ruleApi.createDraft()` + `updateDraft()` 封装 | `src/api/aps-v1/rule.ts` | 0.3 天 |
| 2 | 加 `rulesStore.createDraft()` + `saveDraft()` actions | `src/store/modules/aps/rules.ts` | 0.3 天 |
| 3 | 加 [+ 新建草稿] 按钮 + Dialog（基于 RuleSet 单选 + versionCode 输入） | `src/views/Aps/Rules.vue:177-187` | 0.3 天 |
| 4 | "参数维护" Tab 改可编辑 ElInput/ElSwitch/ElSelect（按 parameterType） + 每行"保存"按钮 + "保存草稿" | `src/views/Aps/Rules.vue:381-420` | 0.5 天 |
| 5 | verify-acceptance.mjs 新增 [P] 段：`createDraft` + `updateDraft` + `publishParameterSet` 三断言 | `scripts/verify-acceptance.mjs` | 0.3 天 |
| 6 | 三绿验证（ts:check/lint/build + GROUP=new-p + GROUP=static + GROUP=current-user 回归） | — | 0.3 天 |
| **合计** | | | **≈2 天** |

> 4 号位 起步依赖：**Q1 答案 + Q2 答案**（决定 PUT 形状 + fork 路径）；Q3/Q4/Q5 重要但不阻塞前端基础结构落地。

---

## 五、UI 拟改造示意（4 号位 自决）

### 5.1 页面顶部 header-actions（[Rules.vue:177-187](frontNew/src/views/Aps/Rules.vue#L177-L187)）

```vue
<ElButton
  v-if="canWrite"
  type="primary"
  size="default"
  @click="openCreateDraftDialog"
>
  <Icon icon="vi-mdi:file-plus-outline" />
  新建草稿
</ElButton>
```

### 5.2 新建草稿 Dialog（参考现有 PublishDialog 范式 Rules.vue:643-680）

```
+----------------------------------+
| 新建参数集 DRAFT                [×]|
+----------------------------------+
| 基于 RuleSet  [RS-DEFAULT  v12  ▼]|
| 新版本号      v13   (自动派生)    |
| 变更原因      [_____________] (必填)|
|               [取消]   [确认创建]  |
+----------------------------------+
```

### 5.3 "参数维护" Tab（[Rules.vue:381-420](frontNew/src/views/Aps/Rules.vue#L381-L420)）

```
当 DRAFT 状态 + canWrite:
  参数行 "当前值" 列改可编辑:
    - NUMBER/STRING/PERCENT/DURATION → ElInputNumber
    - BOOLEAN                          → ElSwitch
  Tab 底部:
    [保存草稿] [取消未保存]
```

---

## 六、依赖与排期建议（请 0 号位 知会）

| 角色 | 任务 | 建议时序 |
|---|---|---|
| **3 号位** | Q1-Q5 回执 | 09-21 当日 |
| **3 号位** | 若 Q1 选 B：加 `/fork` 端点 | 09-22 ~ 09-23（≈0.5 天） |
| **4 号位** | 第 1-6 步前端实施 | Q1 答案后次日即可起（09-22） |
| **0 号位** | 若 Q1-Q5 有仲裁争议裁决 | 09-21 EOD |
| **整体** | 闭环 | 09-23 或 09-24（取决于 Q1 + fork 工时） |

---

## 七、不在本函范围

- SolverStrategyJson / CandidateGuardrailJson 当前前端不展示（仅在 Strategy Tab 显示 strategyProfile 概要）——本次不实施编辑
- 审计字段/LoginLogout 缺口不在本函范畴（已另函《关于 §22.6 Audit Login/Logout 缺口》催办 3 号位，未回执）
- StrategyProfile 独立维护页面（`/aps/strategy-profile`，已闭环）不在本函范畴

---

## 八、附件 / 参考文件

- 用户 09-21 现场截图：见 4 号位 ↔ 用户 当日对话（含 3 张页面截图：全部 / DRAFT / PUBLISHED 三态）；顶部 [+ 新建草稿] 缺位在所有截图均可见
- RuleSet/ParameterSetVersion 实体：[lps/LPS.APS.Core/Entities/Aps/ParameterSetVersion.cs](lps/LPS.APS.Core/Entities/Aps/ParameterSetVersion.cs)（五子 JSON 字段形状）
- Controller 写端点 6 个：[lps/LPS.APS.Web/Controllers/GovernanceController.cs:108-275](lps/LPS.APS.Web/Controllers/GovernanceController.cs#L108-L275)
- 前端当前页面 + store + api：
  - `frontNew/src/views/Aps/Rules.vue`（732-1110 行 template + script setup）
  - `frontNew/src/store/modules/aps/rules.ts`
  - `frontNew/src/api/aps-v1/rule.ts`

---

## 九、性质提示

本函是**正向协同**——3 号位 后端基础牢固、文档清晰（实体注释、Controller summary 完整），4 号位 在等设计口径+分工确认。**非缺陷上报、非"催交付"语气**。建议 3 号位 在 09-21 EOD 前以 **Q1 答案** 起回执链式推进。

4 号位 可立即开始：第 1 步（rule.ts 骨架）+ 第 2 步（rules.ts store 骨架）（按 Q1-Q5 默认假设；Q1-Q5 回执到位即合并）。
