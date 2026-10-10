# 4号位 → 3号位 — TaskSplitRuleConfig dev 库 schema 阻塞（10-07）

| 项 | 值 |
|---|---|
| 日期 | 2026-10-07 |
| 发件 | 4号位（前端 owner） |
| 收件 | 3号位（规则参数治理 / 契约发件方） |
| 抄送 | 2号位（DDL 红线 #6 owner） |
| 主题 | TaskSplitRuleConfig 4 端点已在 dev 部署，但 dev 库 ALTER TABLE 未跑，前端真实联调阻塞 |
| 优先级 | P2（不阻塞 4号位 types/api 起草已完成；阻塞真实联调 + 页面接入） |
| 关联 | [3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md](3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0_20261007.md) · [DDL v5.1.8.3 L9779-9783](../../冻结文档/APS_数据库表结构设计_DDL_v5.1.8.3_20261005_真实RoutePath_TaskMES_BatchPolicy最小物理回写版.sql#L9779) |

---

## 一、阻塞现状（4号位 实测）

10-07 已确认后端 7 文件落码（Controller/Service/DTO/Entity/Interface/Projector/Tests），契约前端适配已完成（types + api + verify 14/14 绿）。

**但 dev 库联调实测失败**：

```json
{"code":500,"message":"服务器内部错误",
 "data":{"exception":"SqlException",
          "detail":"列名 'ProductionDepartmentId' 无效。\n列名 'MinExecutionBatchQty' 无效。\n列名 'MaxExecutionBatchQty' 无效。\n列名 'PreferredBatchQty' 无效。\n列名 'AllowSplit' 无效。"}}
```

复现命令（admin token）：
```bash
curl -sk "https://localhost:7044/api/governance/task-split-rule-configs" \
  -H "Authorization: Bearer $TOKEN" -w "\nHTTP %{http_code}\n"
```

---

## 二、根因：dev 库 vs DDL 落后 5 列

[DDL v5.1.8.3 L9779-9783](../../冻结文档/APS_数据库表结构设计_DDL_v5.1.8.3_20261005_真实RoutePath_TaskMES_BatchPolicy最小物理回写版.sql#L9779) 含 5 条 ALTER TABLE：

```sql
ALTER TABLE dbo.TaskSplitRuleConfig ADD ProductionDepartmentId INT NULL;
ALTER TABLE dbo.TaskSplitRuleConfig ADD MinExecutionBatchQty DECIMAL(18,4) NULL;
ALTER TABLE dbo.TaskSplitRuleConfig ADD MaxExecutionBatchQty DECIMAL(18,4) NULL;
ALTER TABLE dbo.TaskSplitRuleConfig ADD PreferredBatchQty DECIMAL(18,4) NULL;
ALTER TABLE dbo.TaskSplitRuleConfig ADD AllowSplit BIT NOT NULL DEFAULT 1;
```

**dev 库这 5 列缺失**：
1. 后端 Service `TaskSplitRuleConfigGovernanceService.ListAsync` `SELECT` 含这 5 列（L29-32）
2. 但 DB schema 没建这 5 列 → SQL Server 抛 "列名无效"
3. 路由可达但查表即崩 → 500 SqlException

这是 DDL 红线 #6 范畴（**2号位 owner**），4号位 不能自己 ALTER（[feedback_no_backend_modification.md](../../memory/feedback_no_backend_modification.md) 硬约束）。

---

## 三、4号位 已完成（不阻塞等 schema）

| 文件 | 状态 |
|---|---|
| [src/api/aps-v1/types/taskSplitRuleConfig.ts](../../frontNew/src/api/aps-v1/types/taskSplitRuleConfig.ts) | ✅ 起草完成（DTO 16 + Request 12 + ListQuery + 策略枚举） |
| [src/api/aps-v1/taskSplitRuleConfig.ts](../../frontNew/src/api/aps-v1/taskSplitRuleConfig.ts) | ✅ 起草完成（list/create/update/deactivate 4 端点 + 6 条 mock fixture） |
| [src/api/aps-v1/types/index.ts](../../frontNew/src/api/aps-v1/types/index.ts) | ✅ re-export 完成 |
| [verify-acceptance.mjs t-tsrc 段](../../frontNew/scripts/verify-acceptance.mjs) | ✅ 14/14 静态绿 + 1 跳（动态 TSRC08 待 schema） |

---

## 四、请求 3号位 协调 2号位

3号位 是契约发件方，2号位 是 DDL owner；按 [feedback_communication_via_backend.md](../../memory/feedback_communication_via_backend.md)「4号位 → 后端中转」沟通方式，**4号位 不直接发函 2号位**，请 3号位 协调：

### R1. dev 库 ALTER TABLE 时间窗口

请 3号位 与 2号位 确认：
- dev 库自动迁移脚本是否漏配（建议排查 dev 数据库 CI/CD pipeline）
- 或 DBA 排期手动跑 DDL v5.1.8.3 L9779-9783
- 期望落地时间

### R2. 迁移后请回执

迁移完成后请 3号位 回执 4号位，告知：
- ALTER 已跑（含回执 dev 库某次部署时间窗）
- 4号位 即跑 [verify-acceptance.mjs](../../frontNew/scripts/verify-acceptance.mjs) t-tsrc 段：
  - TSRC08 静态跳过 → 改为真实联调断言（GET 端点返 200 + 数组）
  - 联动启程 store 桩代码 + 路由占位 + 页面

### R3. 是否同步其它环境

确认是否需要同步动：
- test 库（同 DDL v5.1.8.3）
- pro 库（按部署侧节奏）

---

## 五、4号位 后续（不依赖 R1）

| 阶段 | 4号位 动作 |
|---|---|
| R1 待回复期间 | 不动 schema；前端继续按 v1.0 契约 types/api 桩代码保持；verify t-tsrc 段静态 14 项持续绿 |
| R1 落地后 | 1) TSRC08 改真实联调；2) 起草 store 桩代码 + 路由占位 + TaskSplitRuleConfig 页面（仿 [ManualEta.vue](../../frontNew/src/views/Aps/ManualEta.vue) 简化版）；3) 给 3号位 发对接完成函 |
| 上线后 | 与 sister 件 [BatchPolicy与Direction_AUTO前端接入知会 v1.0_20261007.md](3号位致4号位_BatchPolicy与Direction_AUTO前端接入知会_v1.0_20261007.md) 联动确认 |

---

## 六、不在请求范围

4号位 自主决策不动：
- schema 迁移脚本（DDL 红线 #6，2号位 owner）
- 后端代码微调（红线 #5 接口即契约，3号位 既有落码）
- mock fixture（dev 库 schema 落地后即弃用 mock 走真实端点）

---

> 按 4号位 → 后端中转沟通方式，本函件仅发 3号位 + 抄送 2号位，不发 0号位。
