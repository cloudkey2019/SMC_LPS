# ⚠️ 已撤回 · superseded —— 3号位 → 2号位：TaskSplitRuleConfig dev 库 DDL 协助申请（v1.0）

**本件已撤回**（2026-10-07）：建于已废止的旧红线「DDL 2号位专属」。经 0号位 正式裁决（`未命名的Markdown文件 (4).md`）——TaskSplitRuleConfig 属 Batch Policy 治理结构，DDL Owner=3号位，dev 由 3号位 直接落地，不再转 2号位。本件不生效，以补丁 SQL + 4号位 直接回执为准。

> 发件：3号位（规则参数治理 / 契约发件方）｜ 致：2号位（DDL 红线 #6 owner）｜ 抄：0号位、4号位
> 日期：2026-10-07 ｜ 优先级：P2（阻塞 4号位 前端真实联调；不阻塞其 types/api 起草）
> 性质：**DDL 红线 #6 专属 2号位**——3号位 提报协助、不自行执行。依据 4号位→3号位→2号位 中转沟通方式。
> 关联：4号位《TaskSplitRuleConfig-dev库schema阻塞-给3号位》(2026-10-07)；《3号位致4号位_TaskSplitRuleConfig维护接口契约_v1.0》

---

## 一、阻塞现状

4号位 实测 `GET /api/governance/task-split-rule-configs` 返 500：

```
SqlException：列名 'ProductionDepartmentId' / 'MinExecutionBatchQty' /
             'MaxExecutionBatchQty' / 'PreferredBatchQty' / 'AllowSplit' 无效。
```

后端 Service `TaskSplitRuleConfigGovernanceService` `SELECT` 已含完整 18 列，但 dev 库 `TaskSplitRuleConfig` 表停留旧中期版，**缺 5 列**，查表即崩。

## 二、根因与准确落点（3号位 已核实）

冻结 DDL 文件真位置（4号位 函件所引 `冻结文档/` 路径不存在，以此为准）：

- **文件**：`开发文档/2026-10-07文档更新/APS_数据库表结构设计_DDL_v5.1.8.3_20261005_真实RoutePath_TaskMES_BatchPolicy最小物理回写版.sql`
- **位置**：**L9779–9786**，共 **8 条**幂等 `IF COL_LENGTH(...) IS NULL ALTER TABLE ... ADD`：
  - L9779 `ProductionDepartmentId INT NULL`
  - L9780 `MinExecutionBatchQty DECIMAL(18,4) NULL`
  - L9781 `MaxExecutionBatchQty DECIMAL(18,4) NULL`
  - L9782 `PreferredBatchQty DECIMAL(18,4) NULL`
  - L9783 `AllowSplit BIT NOT NULL DEFAULT 1`
  - L9784 `AllowMerge BIT NOT NULL DEFAULT 0`
  - L9785 `MaxOptimizationSplitCount INT NULL`
  - L9786 `MaxBatchCandidates INT NULL`
- dev 实测缺失 = 前 5 条（L9779–9783）；后 3 条 dev 已存在，幂等守卫自动跳过。**整段跑安全**。

## 三、请求项

### R1（阻塞，P2）— dev 库跑幂等 ALTER
请按 2号位 既有迁移/发布流程，在 **dev** 库执行 L9779–9786 整段幂等 ALTER（或等价迁移），并回执预期/实际落地时间窗。

### R2（阻塞，P2）— 索引核对
`IX_TaskSplitRuleConfig_Query(MaterialId, ProductionDepartmentId, IsActive)`（DDL L4484）——dev 旧表若建表时无 `ProductionDepartmentId`，该索引需随列补齐后重建，请一并核对。

### R3（连带，非阻塞，可另行排期）— 唯一约束评估
后端已有应用层 `EnsureNotDuplicateAsync` 防重复；为防并发重复，请 2号位 评估唯一过滤索引（`MaterialId + ProductionDepartmentId`；`ProductionDepartmentId NULL` 的「每物料最多一条 NULL 部门规则」语义需 filtered unique index 表达）。**勿阻塞 R1/R2**。

## 四、test / pro 同步（转 4号位 R3 之问）

请回执是否需要同步：
- test 库（同 DDL v5.1.8.3）
- pro 库（按部署侧节奏）

## 五、回执后动作

2号位 落地 dev 后请回执 3号位（含时间窗）→ 3号位 即转 4号位 跑 `verify-acceptance.mjs` t-tsrc 段真实联调（TSRC08 由静态跳过改真实断言）。

---

**发件**：3号位 ｜ **日期**：2026-10-07 ｜ **版本**：v1.0
**本件性质**：红线 #6 DDL 提报协助件——3号位 不自行执行，仅转达事实与请求。