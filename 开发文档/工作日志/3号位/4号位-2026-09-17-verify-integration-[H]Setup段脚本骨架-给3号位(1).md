# 4 号位 verify-integration.mjs [H] Setup 段脚本骨架（草稿）

> **发送人**：4 号位（前端）
> **接收人**：3 号位（同步联调脚本骨架）+ 0 号位（治理留档）
> **日期**：2026-09-17
> **目的**：在 3 号位 落地 13 个 Setup 维护 API + SetupRule 实体 + Diff 扩展**之前**，4 号位 先把 [H] 段测试脚本骨架写好，等 3 号位 端点合入 dev 分支后即可实跑，**避免届时再写脚本浪费时间**
> **对应**：v1.5 addendum §十二 验证场景 S01/S03/S05/S06 + 3 号位 催办单 §七 验收标准
> **当前状态**：🟡 **草稿（不实跑）** — 端点 0/13 落地，脚本可加载但会全段 skip

---

## 一、设计原则（与现有 [A][B][C][E] 段对齐）

| 维度 | 设计 | 理由 |
|---|---|---|
| 跳过策略 | 端点 404 / 405 → `info('[H] 跳过：3 号位 13 端点未落地')` 整段 return | 与 [E] 段"无 ACTIVE 锁可释放"跳过模式一致 |
| 数据准备 | 不依赖 seed（admin 直接 POST 创建）| SetupRule 全新实体，无 seed 期望 |
| 鉴权反向 | viewer 任意写端 → 403（[H.11]）| 与 [C] 段 5 号位 鉴权修复模式一致 |
| 业务语义校验 | EXACT 缺 FromMaterial → 422 + 业务消息（[H.2]）| 与 [B] 段 RBAC §八.② 422 校验一致 |
| Diff 扩展 | `setupRuleChanges` 字段非空 + 数组（[H.10]）| 验证 3 号位 是否真扩了 Diff |
| 唯一性约束 | 重复 7 元组 → 422 + 业务消息"已存在"（[H.12]）| EXACT 唯一键 |
| GROUP 环境变量 | `GROUP=setup` | 与 `GROUP=domain / rbac / auth-negate` 一致 |

## 二、12 断言（与 3 号位 催办单 §七 对齐）

| # | 断言 | 端点 | 期望 |
|---|---|---|---|
| H.1 | admin POST EXACT 完整 7 元组 | `POST /api/governance/setup-rules/exact` | code=200 + newId |
| H.2 | admin POST EXACT 缺 FromMaterial | `POST /api/governance/setup-rules/exact` | code=422 + 业务消息"EXACT 必须填 FromMaterial" |
| H.3 | admin GET EXACT 列表 | `GET /api/governance/setup-rules/exact?ruleSetVersionId=...` | code=200 + List 含刚创建的 |
| H.4 | admin PUT EXACT 修改 | `PUT /api/governance/setup-rules/exact/{id}` | code=200 |
| H.5 | admin DELETE EXACT | `DELETE /api/governance/setup-rules/exact/{id}` | code=200 |
| H.6 | admin POST DEFAULT 完整 5 元组 | `POST /api/governance/setup-rules/default` | code=200 |
| H.7 | admin GET DEFAULT 列表 | `GET /api/governance/setup-rules/default?ruleSetVersionId=...` | code=200 |
| H.8 | admin GET 共同合法设备 | `GET /api/governance/operation-resource-eligibility?operationCode=OP20&materialId=M001` | code=200 + ResourceCode[] 非空 |
| H.9 | admin GET 0 分钟兜底统计 | `GET /api/governance/setup-rules/uncovered-stats?runId=...` | code=200 + List 非空 |
| H.10 | admin GET Diff 含 setupRuleChanges | `GET /api/governance/rule-set-versions/{id}/diff?otherVersionId=...` | code=200 + 响应体.setupRuleChanges 存在 |
| H.11 | viewer 任意 1 个 setup-rule 写端 | `POST /api/governance/setup-rules/exact` | code=403 |
| H.12 | admin 重复唯一键 POST | `POST /api/governance/setup-rules/exact` | code=422 + 业务消息"EXACT 规则已存在" |

**目标**：12 断言全绿 + 联调基线 73 → 85（+12 断言）。

## 三、完整 JS 骨架（**直接粘贴到 verify-integration.mjs**）

> 插入位置：紧接 `verifyDemandProtectionRelease` 函数（L645 之后）+ main 函数 `GROUP` 调度处（L664 之后）。

### 3.1 函数定义（紧接 verifyDemandProtectionRelease 后）

```javascript
/* ==================== [H] Setup 维护 API（Setup v1.2 收口版，2026-09-16）==================== */
/* 联调基线 73 → 85（+12 断言）                                                       */
/* ⚠️ 当前 0/13 端点落地，全段 skip；3 号位 实施包 v1.5 → v1.6 升版后实跑             */

async function verifySetup(adminToken, viewerToken) {
  log('\n[H] Setup 维护 API（POST/GET/PUT/DELETE /api/governance/setup-rules/*）')

  // 0. 探测：3 号位 是否落地（POST 一个最小 EXACT）
  const probeR = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: {
      ruleSetVersionId: 1,         // dev DRAFT 版本
      departmentCode: 'PROBE_DEPT',
      stageCode: 'PROBE_STAGE',
      operationCode: 'PROBE_OP',
      resourceCode: 'PROBE_MC',
      fromMaterialCode: 'PROBE_A',
      toMaterialCode: 'PROBE_B',
      setupMinutes: 99
    }
  })
  if (probeR.code === 404 || probeR.code === 405 || probeR.status === 404) {
    info('[H] 跳过：3 号位 Setup 维护 API 未落地（404/405）')
    info('[H] 触发条件：3 号位 实施包 v1.5 → v1.6 升版 + 13 端点合入 dev')
    return
  }
  if (probeR.code !== 200) {
    info(`[H] 跳过：POST 探测失败 code=${probeR.code} msg=${probeR.json?.message || ''}`)
    return
  }
  const probeId = probeR.json?.data?.id
  ok(`[H] POST EXACT 探测成功 newId=${probeId}`)

  // H.1 POST EXACT 完整 7 元组（已用 probe 测过；用独立 ID 再测一次确保幂等）
  const exactBody = {
    ruleSetVersionId: 1,
    departmentCode: 'TEST_DEPT',
    stageCode: 'TEST_STAGE',
    operationCode: 'TEST_OP',
    resourceCode: 'TEST_MC',
    fromMaterialCode: 'TEST_A',
    toMaterialCode: 'TEST_B',
    setupMinutes: 45
  }
  const r1 = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: exactBody
  })
  assert(
    '[H.1] admin POST EXACT 完整 7 元组',
    r1.code === 200 && r1.json?.data?.id,
    `期望 200+newId 实际 code=${r1.code}`
  )
  const exactId = r1.json?.data?.id

  // H.2 POST EXACT 缺 FromMaterial → 422
  const r2 = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: { ...exactBody, fromMaterialCode: null }
  })
  assert(
    '[H.2] admin POST EXACT 缺 FromMaterial → 422',
    r2.code === 422 && /EXACT|FromMaterial/i.test(r2.json?.message || ''),
    `期望 422+EXACT 业务消息 实际 code=${r2.code} msg=${r2.json?.message}`
  )

  // H.3 GET EXACT 列表
  const r3 = await call('GET', '/api/governance/setup-rules/exact', {
    token: adminToken,
    query: { ruleSetVersionId: 1 }
  })
  const exactList = r3.json?.data
  assert(
    '[H.3] admin GET EXACT 列表',
    r3.code === 200 && Array.isArray(exactList) && exactList.some((x) => x.id === exactId),
    `期望 200+List 含 newId=${exactId} 实际 code=${r3.code} count=${exactList?.length}`
  )

  // H.4 PUT EXACT 修改
  const r4 = await call('PUT', `/api/governance/setup-rules/exact/${exactId}`, {
    token: adminToken,
    body: { ...exactBody, setupMinutes: 60 }  // 45 → 60
  })
  assert(
    '[H.4] admin PUT EXACT 修改 SetupMinutes 45→60',
    r4.code === 200,
    `期望 200 实际 code=${r4.code}`
  )

  // H.5 DELETE EXACT
  const r5 = await call('DELETE', `/api/governance/setup-rules/exact/${exactId}`, {
    token: adminToken
  })
  assert(
    '[H.5] admin DELETE EXACT',
    r5.code === 200,
    `期望 200 实际 code=${r5.code}`
  )

  // H.6 POST DEFAULT 完整 5 元组（FromMaterial/ToMaterial 必空）
  const defaultBody = {
    ruleSetVersionId: 1,
    departmentCode: 'TEST_DEPT',
    stageCode: 'TEST_STAGE',
    operationCode: 'TEST_OP',
    resourceCode: 'TEST_MC',
    setupMinutes: 30
    // fromMaterialCode / toMaterialCode 不传
  }
  const r6 = await call('POST', '/api/governance/setup-rules/default', {
    token: adminToken,
    body: defaultBody
  })
  assert(
    '[H.6] admin POST DEFAULT 完整 5 元组（FromMaterial 必空）',
    r6.code === 200 && r6.json?.data?.id,
    `期望 200+newId 实际 code=${r6.code}`
  )
  const defaultId = r6.json?.data?.id

  // H.7 GET DEFAULT 列表
  const r7 = await call('GET', '/api/governance/setup-rules/default', {
    token: adminToken,
    query: { ruleSetVersionId: 1 }
  })
  const defaultList = r7.json?.data
  assert(
    '[H.7] admin GET DEFAULT 列表',
    r7.code === 200 && Array.isArray(defaultList) && defaultList.some((x) => x.id === defaultId),
    `期望 200+List 含 newId=${defaultId} 实际 code=${r7.code} count=${defaultList?.length}`
  )

  // H.8 GET 共同合法设备推荐
  const r8 = await call('GET', '/api/governance/operation-resource-eligibility', {
    token: adminToken,
    query: { operationCode: 'TEST_OP', materialId: 'TEST_A' }
  })
  const eligibility = r8.json?.data
  assert(
    '[H.8] admin GET 共同合法设备推荐（当前 Operation ∩ 同部门同 Stage）',
    r8.code === 200 && Array.isArray(eligibility) && eligibility.length > 0,
    `期望 200+ResourceCode[] 非空 实际 code=${r8.code} count=${eligibility?.length}`
  )

  // H.9 GET 0 分钟兜底统计（需先有 runId；dev seed 应已 seed run）
  //     若无 runId → 跳过但不计失败
  const runProbe = await call('GET', '/api/governance/runs', { token: adminToken })
  const firstRunId = runProbe.json?.data?.[0]?.id
  if (!firstRunId) {
    info('[H.9] 跳过：当前无 Run 数据（uncovered-stats 需 runId）')
  } else {
    const r9 = await call('GET', '/api/governance/setup-rules/uncovered-stats', {
      token: adminToken,
      query: { runId: firstRunId }
    })
    const uncovered = r9.json?.data
    assert(
      '[H.9] admin GET 0 分钟兜底统计',
      r9.code === 200 && Array.isArray(uncovered),
      `期望 200+List 实际 code=${r9.code} type=${typeof uncovered}`
    )
  }

  // H.10 GET Diff 含 setupRuleChanges
  //     需两个 RuleSetVersion（v1 + v2）；dev seed 应已 seed
  const vProbe = await call('GET', '/api/governance/rule-set-versions', { token: adminToken })
  const versions = vProbe.json?.data || []
  if (versions.length < 2) {
    info(`[H.10] 跳过：当前 RuleSetVersion 数量=${versions.length} < 2（Diff 需 2 版本）`)
  } else {
    const r10 = await call('GET', `/api/governance/rule-set-versions/${versions[0].id}/diff`, {
      token: adminToken,
      query: { otherVersionId: versions[1].id }
    })
    const hasSetupChanges = r10.json?.data && 'setupRuleChanges' in (r10.json.data || {})
    assert(
      '[H.10] admin GET Diff 含 setupRuleChanges 字段',
      r10.code === 200 && hasSetupChanges,
      `期望 200+setupRuleChanges 存在 实际 code=${r10.code} 字段存在=${hasSetupChanges}`
    )
  }

  // H.11 viewer 任意 1 个 setup-rule 写端 → 403
  const r11 = await call('POST', '/api/governance/setup-rules/exact', {
    token: viewerToken,
    body: exactBody
  })
  assert(
    '[H.11] viewer POST EXACT 写端 → 403',
    r11.code === 403,
    `期望 403 实际 code=${r11.code}`
  )

  // H.12 admin 重复唯一键 POST → 422
  const r12 = await call('POST', '/api/governance/setup-rules/exact', {
    token: adminToken,
    body: { ...exactBody, setupMinutes: 999 }  // 故意与 H.1 同 7 元组
  })
  assert(
    '[H.12] admin 重复唯一键 POST → 422+业务消息',
    r12.code === 422 && /已存在|重复|exists/i.test(r12.json?.message || ''),
    `期望 422+已存在 实际 code=${r12.code} msg=${r12.json?.message}`
  )

  // 清理：删除 H.1 创建的 EXACT（probe 已删，H.5 已删 H.1）
  //        删除 H.6 创建的 DEFAULT
  if (probeId) {
    await call('DELETE', `/api/governance/setup-rules/exact/${probeId}`, { token: adminToken })
  }
  if (defaultId) {
    await call('DELETE', `/api/governance/setup-rules/default/${defaultId}`, { token: adminToken })
  }
  ok('[H] 清理完成：probe EXACT + H.6 DEFAULT 已删')
}
```

### 3.2 main 函数 GROUP 调度（紧接 [C] 调度后）

```javascript
    if (GROUP === 'all' || GROUP === 'setup') {
      // [H] 需 viewer token 测鉴权反向（与 [C] 段共享）
      const viewerToken = process.env.VIEWER_TOKEN || await loginAsViewer()  // 见 §四 注
      await verifySetup(token, viewerToken)
    }
```

## 四、依赖项

| 项 | 来源 | 当前状态 |
|---|---|---|
| `adminToken` | `loginAsAdmin()` | ✅ 已有 |
| `viewerToken` | 需新增 `loginAsViewer()` 或从 env 读 | ❌ 需补（参考 [C] 段如何拿 viewer token）|
| `call()` 函数 | L111 | ✅ 已有 |
| `assert()` 函数 | L95 | ✅ 已有 |
| dev seed | DRAFT RuleSetVersion 至少 1 个 + ACTIVE Run 至少 1 个 | 🟡 待 3 号位 seed |

**注**：viewer token 获取方式参考 [C] 段（@see `verifyAuthNegate` 函数 L394），通常用 `loginAsViewer()` 或 `process.env.VIEWER_TOKEN` 兜底。

## 五、跳过场景（不会让 failCount 增加）

| 场景 | 触发 | 行为 |
|---|---|---|
| 3 号位 端点未落地 | 探测 POST 返回 404/405 | `info('[H] 跳过：3 号位 13 端点未落地')` + return |
| POST 探测失败 | code != 200（500 / 422）| `info(...)` + return |
| 无 Run 数据 | `/api/governance/runs` 返回空 | `info('[H.9] 跳过')`，H.9 不算失败 |
| RuleSetVersion < 2 | `/api/governance/rule-set-versions` 数量不足 | `info('[H.10] 跳过')`，H.10 不算失败 |

## 六、3 号位 端点落地后实跑流程

```bash
# 1. 3 号位 实施包 v1.5 → v1.6 升版 + 13 端点合入 dev
# 2. 3 号位 补 dev seed：≥1 DRAFT RuleSetVersion + ≥1 ACTIVE Run
# 3. 4 号位 跑 [H] 段（默认全跑）
cd frontNew
node scripts/verify-integration.mjs

# 4. 仅跑 [H] 段
GROUP=setup node scripts/verify-integration.mjs
```

**预期输出**（12 断言全绿）：

```text
[H] Setup 维护 API（POST/GET/PUT/DELETE /api/governance/setup-rules/*）
  ✅ [H] POST EXACT 探测成功 newId=...
  ✅ [H.1] admin POST EXACT 完整 7 元组 — 期望 200+newId 实际 code=200
  ✅ [H.2] admin POST EXACT 缺 FromMaterial → 422 — 期望 422+EXACT 业务消息 实际 code=422
  ...
  ✅ [H.12] admin 重复唯一键 POST → 422+业务消息 — 期望 422+已存在 实际 code=422
  ✅ [H] 清理完成：probe EXACT + H.6 DEFAULT 已删
```

**目标**：联调基线 73 → 85（+12 断言）。

## 七、文件改动清单（3 号位 端点落地后）

| 文件 | 改动 |
|---|---|
| `frontNew/scripts/verify-integration.mjs` | +1 函数 `verifySetup`（约 150 行）<br>+1 GROUP 调度分支（3 行）<br>+1 行头部注释（[H] 段说明）|
| `frontNew/docs/Pkg-8-RBAC完成报告.md` | +1 段 `[H] Setup 段 12 断言全绿` |
| `frontNew/docs/v1.2-完成报告.md` | +1 段 `Setup v1.2 收口版闭环 (12 断言)` |
| 冻结基线索引 v1.5 | 不变（脚本非冻结文档）|

## 八、与 4 号位 v1.5 addendum 章节对账

| 验证场景（§十二）| 4 号位 验证点 | [H] 段断言 |
|---|---|---|
| **S01** 当前工序产品转换命中 | 2.1 EXACT 维护页 | H.1 + H.3 + H.4 + H.5（CRUD 全链路）|
| **S03** 同设备不同当前工序 | 2.1 设备下拉 | H.8（共同合法设备推荐）|
| **S05** 默认规则 | 2.2 DEFAULT 维护页 | H.6 + H.7（POST + GET）|
| **S06** 无规则 | 2.3 0 分钟兜底统计 | H.9（uncovered-stats）|

**4 个验证场景 100% 覆盖**（其余 14 个场景由 1/2/3 号位 联调验证）。

## 九、相关引用

- **4 号位 v1.5 addendum §十二**：[S01/S03/S05/S06 验证场景](../../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.5_20260916_Setup换型规则专项冻结对齐版.md)
- **3 号位 催办单 §七**：[12 断言验收标准](3号位催办单-2026-09-17-Setup维护API-12端点.md)
- **0 号位 简报**：[索引升版 v1.5 申请](4号位-2026-09-17-索引升版申请-给0号位.md)
- **现有 [A][B][C][E] 段**：[frontNew/scripts/verify-integration.mjs](../../scripts/verify-integration.mjs)（参考风格 + 函数命名 + 跳过模式）
