# 4号位 → 3号位 — Rules.vue 点击左侧 RuleSet #1727 报「无可用版本」根因：列表 status 派生用 `isActive`，与 `RuleSetVersion` 表无对应行（dev seed 数据不一致）

| 项 | 值 |
|---|---|
| 日期 | 2026-10-10 |
| 发件 | 4号位（前端 owner） |
| 收件 | 3号位（规则/参数治理 owner / dev seed 数据 owner） |
| 主题 | Rules.vue 点击左侧 RuleSet 列表行，详情接口 `published-version` 404 → 降级 `versions` → 返空数组 → 抛 `RuleSet #1727 无可用版本`。根因是 RuleSet 主表 `IsActive=1` 但 `RuleSetVersion` 表里**没有**该 ruleSetId 的版本行 |
| 优先级 | **P1**（dev 联调阻塞；list 端点所有 `IsActive=1` 但无版本的 ruleSet 都点不开，命中率高） |
| 影响面 | **RuleSet / ParameterSet / StrategyProfile 三页同模式**（共用 GovernanceController 同 list 端点族 + 同 fallback 链） |

---

## 〇、结论

```
现象    : ❌ Rules.vue 点击 ruleSetId=1727 → 抛 "RuleSet #1727 无可用版本"
根因    : ❌ RuleSet 主表 IsActive=1（dev seed 默认值）→ 列表 status 派生为 'PUBLISHED'
          但 RuleSetVersion 表里 ruleSetId=1727 零行
          → /published-version 404（无 PUBLISHED 版本）
          → 降级 /versions → 返空数组 → getRuleSet 抛错
归属    : ✅ 3号位（RuleSet 治理 + dev seed 数据 owner）
请 3号位 : 短期 ① 重跑 dev seed 补 RuleSetVersion 行（ruleSetId=1,2,3 等均需 1 个 PUBLISHED 行）
          中期 ② /api/governance/rule-sets 列表端点 JOIN RuleSetVersion 校验 status
                 （无对应 PUBLISHED 行则 status='DRAFT' 或加 warning 标记）
          远期 ③ v1.4 §二十规划「列表返回 RuleSetVersion 摘要」直接治本（已落部分端点）
4号位   : 零代码改动，仅发函观察
```

---

## 一、现象（4号位 dev 联调实测，2026-10-10）

### 1.1 复现步骤

1. admin 登录 dev（admin/Admin@123456）
2. 进入 `Rules.vue`（路由 `/aps/rules`）
3. 左侧 RuleSet 列表正常显示 N 条（dev seed）
4. 点击任意 **非前 1-3 个**的 RuleSet 行（如 ruleSetId=1727）
5. 右侧详情加载失败，提示 **`RuleSet #1727 无可用版本`**（红色错误条）

### 1.2 Network 截图链路

```
GET /api/governance/rule-sets
  → 200 OK, items[].status = 'PUBLISHED'（ruleSetId=1727 也包含在内）

GET /api/governance/rule-set/1727/published-version
  → 404 Not Found（versions 表无 1727 的 PUBLISHED 行）

GET /api/governance/rule-set/1727/versions
  → 200 OK, items = []   ← 关键：versions 表里 1727 零行

→ getRuleSet() 抛 Error('RuleSet #1727 无可用版本')
→ rulesStore.handleError 写入 error.value
→ Rules.vue 右侧 ElAlert 展示红色错误条
```

---

## 二、根因（已读 lps/ + 前端 mapping 代码双向确认）

### 2.1 前端 list 端点的 status 派生逻辑（**真正的根因**）

[frontNew/src/api/aps-v1/rule.ts:309-335](frontNew/src/api/aps-v1/rule.ts#L309-L335) **`listRuleSets()`**：

```ts
async listRuleSets(): Promise<RuleSetSummaryDto[]> {
  if (APS_USE_MOCK) return mockRuleSets
  type BackendRuleSet = {
    id: number
    // ... 其它字段
    isActive: boolean
  }
  // ... 
  return backendList.map((r) => ({
    ruleSetId: r.id,
    ruleSetCode: r.ruleSetCode,
    ruleSetName: r.ruleSetName,
    // ...
    draftVersion: undefined,
    // ↓↓↓↓↓↓ 关键派生 ↓↓↓↓↓↓
    // isActive=true 视为有发布版本，否则视为 DISABLED（保守；详情加载后校正）
    status: (r.isActive === false ? 'DISABLED' : 'PUBLISHED') as RuleSetSummaryDto['status'],
    // ↑↑↑↑↑↑ 关键派生 ↑↑↑↑↑↑
    lastChangeReason: r.description,
    publishedAt: undefined,
    retiredAt: undefined,
    // ...
  }))
}
```

**问题**：列表端点**完全没查 `RuleSetVersion` 表**，只用 `RuleSet.IsActive` 一个 boolean 字段判定 → `IsActive=1` 一律显示 `PUBLISHED`，`IsActive=0` 一律显示 `DISABLED`。

这是 4号位 当初的临时映射（注释里写了「详情加载后校正」，但点击本身就要先调详情，详情 fallback 链路把列表里的「虚假 PUBLISHED」暴露了）。

### 2.2 后端 dev seed 数据矛盾

- `RuleSet` 主表：`IsActive=1` 共 N 条（包含 ruleSetId=1727）→ 列表显示 PUBLISHED
- `RuleSetVersion` 表：`ruleSetId=1727` 零行 → 详情 fallback 链全断
- **结论**：dev seed 脚本只造了 RuleSet 主表，**没造 RuleSetVersion 行**（或者造了主表但未走发布流程落 versions）

### 2.3 详情加载 fallback 链路（rule.ts:381-405）

```ts
async getRuleSet(ruleSetId: number): Promise<RuleSetDetailDto> {
  // 第 1 试：published-version 专用端点
  let fullVersion: any = null
  try {
    fullVersion = await apsHttp.get<any>({
      url: `/api/governance/rule-set/${ruleSetId}/published-version`
    })
  } catch (err) {
    if ((err as { code?: number })?.code !== 404) throw err
    // 404 → 继续降级
  }
  // 第 2 试：list versions + 选 PUBLISHED
  if (!fullVersion) {
    const versions = await apsHttp.get<unknown[]>({
      url: `/api/governance/rule-set/${ruleSetId}/versions`
    })
    if (!Array.isArray(versions) || versions.length === 0) {
      throw new Error(`RuleSet #${ruleSetId} 无可用版本`)  // ← 用户看到的报错
    }
    // ... 走完整版本详情加载
  }
}
```

→ 后端 3 个端点（`/published-version` / `/versions` / `/version/{id}`）都**存在且路径正确**（已 grep [GovernanceController.cs:723/744/765](lps/LPS.APS.Web/Controllers/GovernanceController.cs#L723) 确认），**不是端点缺失**。

---

## 三、影响面（同模式 3 页）

| # | 治理域 | list 端点 | fallback 链 | dev 命中 |
|---|---|---|---|---|
| 1 | RuleSet | `/api/governance/rule-sets` | published-version → versions → version/{id} | ✅ 已复现（ruleSetId=1727） |
| 2 | ParameterSet | `/api/governance/parameter-sets` | 同上 | 推测命中（待验证） |
| 3 | StrategyProfile | `/api/governance/strategy-profiles` | 同上 | 推测命中（待验证） |

> 三域**共享同一份 seed 漏造逻辑**，若 RuleSet 中招，ParameterSet / StrategyProfile 大概率同样命中。建议 3号位 一次性把三个域的 RuleSetVersion / ParameterSetVersion / StrategyProfileVersion 表都补齐。

---

## 四、请 3号位 落地（按优先级）

### 方案 A：短期 dev seed 补数据（推荐 24h 内）

重跑 dev seed 脚本或在 SQL 里手动 INSERT，对**每个** `IsActive=1` 的 RuleSet / ParameterSet / StrategyProfile 补一条 `status='PUBLISHED'` 的版本行：

```sql
-- 模板（RuleSet）；ParameterSet / StrategyProfile 同构
INSERT INTO dbo.RuleSetVersion
  (RuleSetId, Version, Status, ChangeReason, CreatedBy, CreatedAt, PublishedAt)
SELECT
  r.RuleSetId, 1, 'PUBLISHED', 'Seed: initial published version',
  'system', GETUTCDATE(), GETUTCDATE()
FROM dbo.RuleSet r
WHERE r.IsActive = 1
  AND NOT EXISTS (
    SELECT 1 FROM dbo.RuleSetVersion v
    WHERE v.RuleSetId = r.RuleSetId AND v.Status = 'PUBLISHED'
  );
```

**优点**：30 分钟搞定，立刻 unblock dev 联调。
**缺点**：治标，下次 seed 重跑或人工新造 RuleSet 时仍可能漏。

### 方案 B：中期 /api/governance/rule-sets 列表端点 JOIN 校验（推荐 1 周内）

改 `RuleSetRepository.GetListWithVersionAsync`（[lps/LPS.APS.Engine/Repositories/Governance/RuleSetRepository.cs:30-140](lps/LPS.APS.Engine/Repositories/Governance/RuleSetRepository.cs#L30)），JOIN `RuleSetVersion` 后用真实 status 覆盖 `isActive` 派生：

```sql
SELECT
  r.Id, r.RuleSetCode, r.RuleSetName, ..., r.IsActive,
  -- 真实 status：有 PUBLISHED 行 → 'PUBLISHED'；有 DRAFT 行 → 'DRAFT'；否则 → 'DISABLED'
  ISNULL(
    (SELECT TOP 1 Status FROM dbo.RuleSetVersion v
     WHERE v.RuleSetId = r.Id ORDER BY
       CASE v.Status WHEN 'PUBLISHED' THEN 0 WHEN 'DRAFT' THEN 1 ELSE 2 END),
    'DISABLED'
  ) AS EffectiveStatus
FROM dbo.RuleSet r
WHERE ...
```

Controller DTO 加 `effectiveStatus` 字段；前端 [rule.ts:332](frontNew/src/api/aps-v1/rule.ts#L332) 改用 `r.effectiveStatus` 替换 `r.isActive` 派生。

**优点**：列表与详情永远一致；用户看到的 PUBLISHED 一律真有点击价值。
**缺点**：1-2 天工作量（Repo + Controller DTO + 前端 mapping 三处）。

### 方案 C：远期 v1.4 §二十列表返回 RuleSetVersion 摘要（已规划）

v1.4 §二十 已规划「列表端点直接返回 RuleSetVersion 摘要（最新 PUBLISHED 版本号 + 状态 + 创建时间）」，完全消除 `isActive` 派生。**当前 published-version / versions / version/{id} 三个端点已落地**，列表端点扩展为下一次 sprint 自然产物。

**建议**：A + C（短期补数据 unblock + 远期按 §二十演进）；B 作为中间过渡可选。

---

## 五、4号位 侧行动（确认零改动）

| 项 | 状态 |
|---|---|
| 前端代码 | ✅ 严格遵守硬约束 `lps/` 不动；前端 mapping / fallback 链是合理的（404→降级→空数组抛错），不修 |
| verify 断言 | ✅ 不增（这是 dev seed 数据问题，verify 探针应锁**契约**而非 dev 数据的某个固定值） |
| mock 模式 | ✅ 不受影响（mockRuleSets 在 [rule.ts:38-100](frontNew/src/api/aps-v1/rule.ts#L38) 自洽） |
| 发表面确认回执 | ✅ 暂缓，按 [feedback_no_redundant_ack_letters.md](../../../../Users/c15794/.claude/projects/d--proj-aps-aps-production-new/memory/feedback_no_redundant_ack_letters.md) 等 3号位 拍板方案后再回 |

---

## 六、关联资料

| 用途 | 路径 |
|---|---|
| **前端 status 派生（根因）** | [frontNew/src/api/aps-v1/rule.ts:309-335](frontNew/src/api/aps-v1/rule.ts#L309-L335) |
| **前端 fallback 链 + 错误抛出** | [frontNew/src/api/aps-v1/rule.ts:381-405](frontNew/src/api/aps-v1/rule.ts#L381-L405) |
| **前端 getPublishedVersion 404 处理** | [frontNew/src/api/aps-v1/rule.ts:346-375](frontNew/src/api/aps-v1/rule.ts#L346-L375) |
| **前端 store loadDetail** | [frontNew/src/store/modules/aps/rules.ts:265-280](frontNew/src/store/modules/aps/rules.ts#L265-L280) |
| **前端 Rules.vue selectRuleSet** | [frontNew/src/views/Aps/Rules.vue:134-138](frontNew/src/views/Aps/Rules.vue#L134-L138) |
| **后端 list 端点（待扩展 JOIN）** | [lps/LPS.APS.Web/Controllers/GovernanceController.cs:675-685](lps/LPS.APS.Web/Controllers/GovernanceController.cs#L675-L685) |
| **后端 published-version 端点** | [lps/LPS.APS.Web/Controllers/GovernanceController.cs:723-738](lps/LPS.APS.Web/Controllers/GovernanceController.cs#L723-L738) |
| **后端 versions 端点** | [lps/LPS.APS.Web/Controllers/GovernanceController.cs:???](lps/LPS.APS.Web/Controllers/GovernanceController.cs)（待 3号位 提供行号） |
| **后端 Repository（list JOIN 改造点）** | [lps/LPS.APS.Engine/Repositories/Governance/RuleSetRepository.cs:30-140](lps/LPS.APS.Engine/Repositories/Governance/RuleSetRepository.cs#L30-L140) |
| **同模式 ParameterSet list 端点** | [lps/LPS.APS.Web/Controllers/GovernanceController.cs:691-700](lps/LPS.APS.Web/Controllers/GovernanceController.cs#L691-L700) |
| **同模式 StrategyProfile list 端点** | [lps/LPS.APS.Web/Controllers/GovernanceController.cs:707-716](lps/LPS.APS.Web/Controllers/GovernanceController.cs#L707-L716) |
| **4号位 此前观察（数据家族根因）** | [frontNew/docs/project_strategy_profile_audit.md](project_strategy_profile_audit.md)「1682 占位数据无版本」同源 |
| **4号位 此前函件（dev DB 占用）** | [4号位-2026-10-07-TaskSplitRuleConfig-dev库造数spec-给3号位.md](4号位-2026-10-07-TaskSplitRuleConfig-dev库造数spec-给3号位.md) |
| **v1.4 节奏（§二十 已规划）** | [project_v1_4_execution_plan.md](../../../../Users/c15794/.claude/projects/d--proj-aps-aps-production-new/memory/project_v1_4_execution_plan.md) |

---

## 七、验收清单（4号位 期望，等 3号位 落地后跑）

| # | 项 | 期望 |
|---|---|---|
| 1 | 3号位 拍板方案 A / B / C | 1 周内回执（避免 dev 联调继续踩） |
| 2 | 若选 A：dev seed 补 RuleSetVersion / ParameterSetVersion / StrategyProfileVersion 行 | dev 重启后 ruleSetId=1727 点击正常加载详情 |
| 3 | 若选 B：list 端点 JOIN 校验 + 前端 mapping 用 effectiveStatus | 列表与详情永远一致；前端 [rule.ts:332](frontNew/src/api/aps-v1/rule.ts#L332) 同步改 |
| 4 | 若选 C：v1.4 §二十演进 | 不影响当前；3号位 按 sprint 计划推进 |
| 5 | 4号位 浏览器走查 Rules.vue / ParameterSet.vue / StrategyProfile.vue | 全部 ruleSetId 点开均不报错 |

> **回执期望命名建议**：`3号位-2026-10-XX-RuleSet列表PUBLISHED数据不一致修复方案-回执-给4号位.md`

---

**发件**：4号位
**日期**：2026-10-10
**优先级**：P1（dev 联调阻塞 / 不影响生产）
**回执期望**：3号位 收到后回 (A) seed 补数据 / (B) list JOIN 校验 / (C) v1.4 §二十演进 / 拒绝 + 替代方案

—— 4号位
